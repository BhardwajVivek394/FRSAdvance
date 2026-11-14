using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- Verdict.
    // Moved verbatim from AiChatController.Verdict.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // v1.0.111.0: decide whether to attach the server-computed derived_inputs panel. It is
        // SUPPRESSED when there is no panel, when the verdict is INCONCLUSIVE (a "we don't know"
        // verdict must not show an input-deviation panel implying a settled cause), or when the
        // derived-component coverage was inadequate.
        internal bool ShouldAttachDerivedInputs(JObject verdict)
        {
            if (Sess._derivedInputs == null) { return false; }
            if (!Sess._derivedComponentsAdequate) { return false; }
            string vd = (verdict != null && verdict["verdict"] != null) ? verdict["verdict"].ToString() : "";
            if (string.Equals(vd, "INCONCLUSIVE", StringComparison.OrdinalIgnoreCase)) { return false; }
            return true;
        }


        // v1.0.111.0: compute triage SERVER-SIDE from the prefetched alert history, and attach it
        // only when eligible. A generative model must not independently decide that railway
        // maintenance "can wait": severity/trend here come from a mechanical count of PRIOR alerts
        // with the SAME AssetId and SAME cause code, and their direction. Gated by appSettings
        // EnableTriage (default OFF, pending owner-approved thresholds). Suppressed on INCONCLUSIVE.
        internal void AttachTriageIfEligible(JObject verdict)
        {
            if (!_enableTriage) { return; }
            if (verdict == null) { return; }
            string vd = verdict["verdict"] != null ? verdict["verdict"].ToString() : "";
            if (string.Equals(vd, "INCONCLUSIVE", StringComparison.OrdinalIgnoreCase)) { return; }
            if (string.IsNullOrEmpty(Sess._triageAlertsRaw) || string.IsNullOrEmpty(Sess._triageCauseCode)) { return; }

            int priorSameCause = 0;
            try
            {
                // v1.0.112.0: parse the get_frs_alerts payload the SAME robust way the evidence
                // path does - ParseJsonDocuments handles MCP content[].text envelopes, multi-doc
                // and truncated results (a naive JToken.Parse silently found ZERO rows on the wire
                // format, so triage was never firing). Walk every object token in each document and
                // count those whose asset + cause match, EXCLUDING the current alert itself (its own
                // row is in the fetched window, so counting it would inflate every alert to >=1).
                List<JToken> docs = ParseJsonDocuments(Sess._triageAlertsRaw);
                System.Collections.Generic.HashSet<string> seenIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int d = 0; d < docs.Count; d++)
                {
                    if (docs[d] == null) { continue; }
                    foreach (JToken tok in docs[d].SelectTokens("$..*"))
                    {
                        if (tok == null || tok.Type != JTokenType.Object) { continue; }
                        JObject ro = (JObject)tok;
                        string cc = FirstStr(ro, "causeCode", "alertCode", "cause_code", "CauseCode");
                        if (string.IsNullOrEmpty(cc)) { continue; }          // not an alert row
                        string aId = FirstStr(ro, "assetId", "AssetId", "asset_id");
                        string rowAlertId = FirstStr(ro, "alertId", "AlertId", "id", "Id", "alert_id");

                        bool assetMatch = string.IsNullOrEmpty(Sess._triageAssetId) || string.Equals(aId, Sess._triageAssetId, StringComparison.OrdinalIgnoreCase);
                        bool causeMatch = string.Equals(cc, Sess._triageCauseCode, StringComparison.OrdinalIgnoreCase);
                        if (!assetMatch || !causeMatch) { continue; }
                        // exclude the current alert by id
                        if (!string.IsNullOrEmpty(Sess._triageCurrentAlertId) && !string.IsNullOrEmpty(rowAlertId)
                            && string.Equals(rowAlertId, Sess._triageCurrentAlertId, StringComparison.OrdinalIgnoreCase)) { continue; }
                        // de-dupe by alert id when present (SelectTokens can revisit nested copies)
                        if (!string.IsNullOrEmpty(rowAlertId))
                        {
                            if (seenIds.Contains(rowAlertId)) { continue; }
                            seenIds.Add(rowAlertId);
                        }
                        priorSameCause++;
                    }
                }
            }
            catch { return; }   // any parse failure -> no triage rather than a guess

            // Mechanical severity mapping. These thresholds are CONSERVATIVE DEFAULTS and MUST be
            // owner-approved before EnableTriage is turned on in production. The count is of OTHER
            // same-asset/same-cause alerts in the fetched window (the current alert is excluded);
            // the window is the incidence-aligned get_frs_alerts window, NOT all history, so this
            // is a recurrence signal within that window, not a lifetime count.
            //   0 other same-cause in window -> low  (isolated in window)
            //   1-2 other                    -> medium (recurring)
            //   3+ other                     -> high (frequent repeat)
            string severity, trend;
            if (priorSameCause >= 3) { severity = "high"; trend = "recurring"; }
            else if (priorSameCause >= 1) { severity = "medium"; trend = "recurring"; }
            else { severity = "low"; trend = "isolated"; }

            JObject tri = new JObject();
            tri["severity"] = severity;
            tri["trend"] = trend;
            tri["otherSameCauseInWindow"] = priorSameCause;
            tri["basis"] = "server-counted other same-asset, same-cause alerts in the fetched incidence window (current alert excluded)";
            verdict["triage"] = tri;
        }

        // Moved to E7.AiCore.VerdictRules.FirstStr in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string FirstStr(JObject o, params string[] keys) => VerdictRules.FirstStr(o);

        // Moved to E7.AiCore.VerdictRules.DeterministicAction in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string DeterministicAction(string category) => VerdictRules.DeterministicAction(category);

        // Moved to E7.AiCore.VerdictRules.StripStandingConditionClause in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string StripStandingConditionClause(string v) => VerdictRules.StripStandingConditionClause(v);

        // Moved to E7.AiCore.VerdictRules.CoerceCategory in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string CoerceCategory(string raw, string verdict) => VerdictRules.CoerceCategory(raw, verdict);

        // Moved to E7.AiCore.VerdictRules.ComputeEvidenceProvenance in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static JObject ComputeEvidenceProvenance(JObject v) => VerdictRules.ComputeEvidenceProvenance(v);


        internal void EnforceEvidenceProvenance(JObject v, CoverageStateKind cov)
        {
            if (v == null) { return; }
            if (cov == CoverageStateKind.Covered) { return; }   // history genuinely covers incidence -> [fetched] is legitimate
            int demoted = DemoteFetchedTags(v["evidence"] as JArray);
            JObject hi = v["hi"] as JObject;
            if (hi != null) { demoted += DemoteFetchedTags(hi["evidence"] as JArray); }
            if (demoted == 0) { return; }
            v["caveats"] = AppendCaveat(PushStr(v["caveats"]),
                "incidence coverage not established from history; fetched-telemetry claims marked unverified");
            if (hi != null)
            {
                hi["caveats"] = AppendCaveat(PushStr(hi["caveats"]),
                    "incidence coverage history se establish nahi hua; fetched-telemetry claims unverified");
            }
            bool capped = false;
            if (!Sess._isFailureAlert)
            {
                JToken ct = v["confidence"];
                if (ct != null && (ct.Type == JTokenType.Integer || ct.Type == JTokenType.Float))
                {
                    double c = (double)ct;
                    if (c > _provEnforceCap) { v["confidence"] = _provEnforceCap; capped = true; }
                }
            }
            AiLog("INFO", "PROV", "provenance enforce: coverage=" + cov + " demoted " + demoted
                + " [fetched] line(s)" + (Sess._isFailureAlert ? " (failure verdict; confidence kept)"
                    : (capped ? " (confidence capped " + _provEnforceCap + ")" : "")));
        }

        // Moved to E7.AiCore.VerdictRules.DemoteFetchedTags in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static int DemoteFetchedTags(JArray ev) => VerdictRules.DemoteFetchedTags(ev);

        // Moved to E7.AiCore.VerdictRules.AppendCaveat in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string AppendCaveat(string existing, string add) => VerdictRules.AppendCaveat(existing, add);


        internal void OverrideVizThresholdFromCard(JObject v, JObject ctx)
        {
            if (v == null || ctx == null) { return; }
            JObject viz = v["viz"] as JObject;
            if (viz == null) { return; }
            JObject card = ctx["alertCard"] as JObject;
            JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
            JObject cond = (tc != null && tc.Count > 0) ? tc[0] as JObject : null;
            if (cond == null) { return; }
            JArray thrs = cond["thresholds"] as JArray;

            bool haveMax = false, haveMin = false; double maxFail = 0, minFail = 0;
            bool lower = true, haveDir = false;
            if (thrs != null)
            {
                foreach (JToken tk in thrs)
                {
                    JObject to = tk as JObject; if (to == null) { continue; }
                    string cmp = to["comparison"] != null ? to["comparison"].ToString() : "";
                    if (cmp.IndexOf('<') >= 0) { lower = true; haveDir = true; }
                    else if (cmp.IndexOf('>') >= 0) { lower = false; haveDir = true; }
                    string st = to["status"] != null ? to["status"].ToString().ToUpperInvariant() : "";
                    if (st != "FAIL") { continue; }
                    JToken val = to["value"]; double dv;
                    if (val == null || !double.TryParse(val.ToString(),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out dv)) { continue; }
                    if (!haveMax || dv > maxFail) { maxFail = dv; haveMax = true; }
                    if (!haveMin || dv < minFail) { minFail = dv; haveMin = true; }
                }
            }
            if (!haveDir)
            {
                string rule;
                if (CauseLogicMaps.CauseToLogic.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out rule)
                    && !string.IsNullOrEmpty(rule) && rule.IndexOf('>') >= 0 && rule.IndexOf('<') < 0)
                { lower = false; }
            }
            if (!haveMax) { return; }
            double thr = lower ? maxFail : minFail;

            viz["threshold"] = Math.Round(thr, 3);
            if (cond["paramLabel"] != null) { viz["metric"] = cond["paramLabel"].ToString().Trim(); }
            JToken meas = cond["measured"] ?? cond["value"]; double mv;
            if (meas != null && double.TryParse(meas.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out mv))
            { viz["value"] = Math.Round(mv, 3); }
            if (cond["unit"] != null) { string u = cond["unit"].ToString().Trim(); if (u.Length > 0) { viz["unit"] = u; } }
            AiLog("INFO", "VIZ", "viz threshold from card = " + Math.Round(thr, 3)
                + (lower ? " (max, lower-rule)" : " (min, higher-rule)")
                + " metric=" + (cond["paramLabel"] != null ? cond["paramLabel"].ToString().Trim() : "?"));
        }

        // Moved to E7.AiCore.VerdictRules.StripUnknown in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static void StripUnknown(JObject o, string[] allowed) => VerdictRules.StripUnknown(o, allowed);

        // Moved to E7.AiCore.VerdictRules.TrimWords in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string TrimWords(string text, int maxWords) => VerdictRules.TrimWords(text, maxWords);

        // Moved to E7.AiCore.VerdictRules.NumOrNull in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool NumOrNull(JObject o, string key, out string reason) => VerdictRules.NumOrNull(o, key, out reason);


        internal bool ValidateAndSanitizeVerdict(JObject v, out string reason)
        {
            reason = null;
            // v1.0.111.0: SAFETY - remove any model-emitted server-only field FIRST, before
            // anything else. derived_inputs / triage / hi / weather are attached only by trusted
            // post-validation code; a model-supplied version (e.g. an invented Hindi block that
            // would bypass the English operational-action filter) must never survive.
            for (int i = 0; i < _serverOnlyVerdictKeys.Length; i++) { v.Remove(_serverOnlyVerdictKeys[i]); }
            StripUnknown(v, _verdictTopKeys); // unknown extras never pass through
            string verdict = v["verdict"] != null ? v["verdict"].ToString() : "";
            if (verdict != "CONFIRMED" && verdict != "NOT_CONFIRMED" && verdict != "INCONCLUSIVE")
            {
                reason = "verdict must be CONFIRMED | NOT_CONFIRMED | INCONCLUSIVE"; return false;
            }
            JToken conf = v["confidence"];
            int confVal;
            if (conf == null || !int.TryParse(conf.ToString(), out confVal))
            {
                // v1.0.136.0: coerce numeric confidence. Some models (DeepSeek flash) emit
                // 72.0 or "72.5" where the schema says integer; rejecting the whole verdict
                // for that is too strict -- round and clamp instead. Non-numeric still fails.
                double confD;
                if (conf != null && double.TryParse(conf.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out confD)
                    && !double.IsNaN(confD) && !double.IsInfinity(confD))
                {
                    // v1.0.138.0: DeepSeek emits confidence on a 0-1 scale ("0.85" = 85%).
                    // 1.0.136.0's plain rounding collapsed every such value to 0 or 1,
                    // making all its verdicts display as ~1% confidence. A DECIMAL value
                    // <= 1.0 is a fraction -> scale to percent before rounding. A plain
                    // integer "1" (no decimal point) still means 1%.
                    if (confD > 0 && confD <= 1.0 && conf.ToString().IndexOf('.') >= 0)
                    {
                        confD = confD * 100.0;
                    }
                    confVal = (int)Math.Round(confD);
                }
                else
                {
                    reason = "confidence must be an integer 0-100"; return false;
                }
            }
            if (confVal < 0 || confVal > 100)
            {
                reason = "confidence must be an integer 0-100"; return false;
            }
            v["confidence"] = confVal;
            string cat = v["category"] != null ? v["category"].ToString() : "";
            bool catOk = false;
            for (int i = 0; i < _verdictCategories.Length; i++)
            {
                if (string.Equals(cat, _verdictCategories[i], StringComparison.Ordinal)) { catOk = true; break; }
            }
            if (!catOk)
            {
                // v1.0.110/111: category is a CLASSIFICATION LABEL, the least safety-critical
                // field - coerce an unknown/misspelled category to the nearest valid bucket rather
                // than discarding an otherwise-valid verdict. Log the coercion for auditability.
                string coerced = CoerceCategory(cat, verdict);
                AiLog("INFO", "CATEGORY", "coerced category '" + (cat ?? "") + "' -> '" + coerced + "'");
                v["category"] = coerced;
            }
            string[] reqStr = { "headline", "likely_cause", "recommended_action" };
            for (int i = 0; i < reqStr.Length; i++)
            {
                JToken t = v[reqStr[i]];
                if (t == null || t.Type != JTokenType.String || t.ToString().Trim().Length == 0)
                {
                    reason = reqStr[i] + " must be a non-empty string"; return false;
                }
                string cleaned = (reqStr[i] == "recommended_action")
                    ? ScrubPlatformInternals(FilterRecommendedAction(t.ToString()), "recommended_action")
                    : ScrubPlatformInternals(FilterOperationalActions(t.ToString()), "verdict");
                if (reqStr[i] == "headline") { cleaned = TrimWords(cleaned, 15); }
                else if (reqStr[i] == "likely_cause") { cleaned = TrimWords(cleaned, 25); }
                else if (reqStr[i] == "recommended_action") { cleaned = TrimWords(cleaned, 35); }
                v[reqStr[i]] = cleaned;
            }
            JToken ev = v["evidence"];
            if (ev == null || ev.Type != JTokenType.Array || ((JArray)ev).Count < 1 || ((JArray)ev).Count > 10)
            {
                reason = "evidence must be an array of 1-10 strings"; return false;
            }
            JArray evArr = (JArray)ev;
            for (int i = 0; i < evArr.Count; i++)
            {
                if (evArr[i].Type != JTokenType.String || evArr[i].ToString().Trim().Length == 0)
                {
                    reason = "evidence[" + i + "] must be a non-empty string"; return false;
                }
                evArr[i] = TrimWords(ScrubPlatformInternals(FilterOperationalActions(evArr[i].ToString()), "evidence"), 20);
            }
            // The prompt asks for EXACTLY 4 bullets, but validation deliberately
            // ACCEPTS 1-10 and trims to 4: a 3-bullet verdict is still a verdict,
            // and rejecting it over formatting would burn the whole analysis.
            // Extra lines are almost always the 5th and 6th restating earlier
            // ones, so keep the first four -- the prompt orders them
            // most-important-first.
            while (evArr.Count > 4) { evArr.RemoveAt(evArr.Count - 1); }
            if (v["caveats"] == null || v["caveats"].Type != JTokenType.String)
            {
                v["caveats"] = "";
            }
            else
            {
                v["caveats"] = TrimWords(ScrubPlatformInternals(FilterOperationalActions(v["caveats"].ToString()), "caveats"), 20);
            }
            // v1.0.111.0: derived_inputs and triage are NO LONGER accepted from the model - they
            // were stripped at the top of this method and are attached only by trusted server-side
            // code after validation. (The former model-emitted validation blocks were removed.)
            JToken viz = v["viz"];
            if (viz != null && viz.Type != JTokenType.Null)
            {
                if (viz.Type != JTokenType.Object) { reason = "viz must be an object or null"; return false; }
                JObject z = (JObject)viz;
                StripUnknown(z, _vizKeys);
                string[] vizStrings = { "metric", "unit" };
                for (int i = 0; i < vizStrings.Length; i++)
                {
                    JToken t = z[vizStrings[i]];
                    if (t != null && t.Type != JTokenType.Null)
                    {
                        if (t.Type != JTokenType.String) { reason = "viz." + vizStrings[i] + " must be a string"; return false; }
                        z[vizStrings[i]] = ScrubPlatformInternals(FilterOperationalActions(t.ToString()), "viz");
                    }
                }
                if (!NumOrNull(z, "value", out reason)) { return false; }
                if (!NumOrNull(z, "threshold", out reason)) { return false; }
                if (!NumOrNull(z, "baseline", out reason)) { return false; }
                if (!NumOrNull(z, "pct_change", out reason)) { return false; }
                if (!NumOrNull(z, "trigger_pct", out reason)) { return false; }
                if (!NumOrNull(z, "recovery_pct", out reason)) { return false; }
                JToken pn = z["phase_note"];
                if (pn != null && pn.Type != JTokenType.Null)
                {
                    if (pn.Type != JTokenType.String) { reason = "viz.phase_note must be a string"; return false; }
                    z["phase_note"] = ScrubPlatformInternals(FilterOperationalActions(pn.ToString()), "phase_note");
                }
                JToken dir = z["direction"];
                if (dir != null && dir.Type != JTokenType.Null)
                {
                    string d = dir.ToString();
                    if (d != "declining" && d != "rising" && d != "flat")
                    {
                        reason = "viz.direction must be declining | rising | flat | null"; return false;
                    }
                }
                // v1.0.160.120 SL1: if K2 could not run on a track asset, SAY SO. Appended to caveats
                // rather than logged only -- the reader must be able to tell "checked and clean" from
                // "not checked", which is exactly the distinction 122 silent skips destroyed.
                if (!string.IsNullOrEmpty(Sess._circuitSkipReason))
                {
                    try
                    {
                        string cv = v["caveats"] != null ? v["caveats"].ToString() : "";
                        if (cv.IndexOf("coherence not checked", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            v["caveats"] = (cv.Length > 0 ? cv.TrimEnd('.', ' ') + "; " : "") + Sess._circuitSkipReason;
                        }
                    }
                    catch { }
                }

                // v1.0.160.122 SL3 (relocated in .124): pin the action where it is a CONSEQUENCE of
                // the category, not a judgement. This belongs in the VERDICT sanitiser -- .122 put it
                // in BuildPushBlock, which builds the push NOTIFICATION, so the verdict itself still
                // drifted while the push text was pinned. The anchor string existed in both methods.
                try
                {
                    string catNow = v["category"] != null ? v["category"].ToString().Trim() : "";
                    string fixedAct = DeterministicAction(catNow);
                    if (fixedAct != null) { v["recommended_action"] = fixedAct; }
                    else
                    {
                        // Not pinned: keep the model's text, but remove any clause that justifies work
                        // by the STANDING CONDITION -- that finding has its own band and its own action
                        // (Cut F); letting it leak in here is what turned "monitor" into "schedule".
                        string a0 = v["recommended_action"] != null ? v["recommended_action"].ToString() : "";
                        string a1 = StripStandingConditionClause(a0);
                        if (!string.Equals(a0, a1, StringComparison.Ordinal))
                        {
                            v["recommended_action"] = a1;
                            AiLog("INFO", "ACTION", "standing-condition clause removed from recommended_action");
                        }
                    }
                }
                catch { }

                // v1.0.160.117 CUT C: take the citation, record it, then REMOVE the field. It is
                // internal attribution -- a non-privileged browser must never receive rule ids, which
                // would disclose the doctrine taxonomy (same class F2/F7 exist to prevent).
                try
                {
                    JToken wu = v["wisdom_used"];
                    if (wu != null)
                    {
                        if (wu.Type == JTokenType.Array)
                        {
                            JArray keep = new JArray();
                            JArray src = (JArray)wu;
                            for (int i = 0; i < src.Count && i < 20; i++)
                            {
                                string id = src[i] != null ? src[i].ToString().Trim().Trim('[', ']') : "";
                                if (id.Length > 0 && _wisdomIdRx.IsMatch(id)) { keep.Add(id.ToUpperInvariant()); }
                            }
                            if (keep.Count > 0) { Sess._wisdomUsed = keep; }
                        }
                        v.Remove("wisdom_used");   // never reaches the browser
                    }
                }
                catch { try { v.Remove("wisdom_used"); } catch { } }

                // v1.0.160.114 CUT E: the MODEL's series is a restatement, not a measurement -- on
                // 543194 it was reset->breach->reset drawn as a V. Override with the real one the
                // controller already has; if there is none, emit an EMPTY series and say so rather
                // than letting an invented shape stand.
                {
                    string _srcKind;
                    JArray _real = RealVizSeries(v, out _srcKind);
                    if (_real != null && _real.Count > 0)
                    {
                        z["series"] = _real;
                        z["seriesSource"] = _srcKind;
                    }
                    else
                    {
                        z["series"] = new JArray();
                        z["seriesSource"] = "unavailable";
                    }
                }
                JToken series = z["series"];
                if (series != null && series.Type != JTokenType.Null)
                {
                    if (series.Type != JTokenType.Array) { reason = "viz.series must be an array"; return false; }
                    JArray sa = (JArray)series;
                    // 50 points is more chart resolution than a 500px SVG can show,
                    // and OUTPUT tokens dominate the final turn (measured: 1,569 out =
                    // 33 s, vs 10,540 in on the same turn). Trim rather than reject.
                    // NOTE: trimming here saves BYTES to the browser but NOT generation
                    // time -- the model has already paid to write them. The real control
                    // is the 12-point instruction in the prompt; this is the backstop.
                    while (sa.Count > 12) { sa.RemoveAt(1); }
                    // v1.0.87.0: COERCE rather than reject. The model sometimes emits a
                    // numeric value as a STRING ("8.53") or slips a null into the series
                    // -- rejecting the entire (otherwise grounded) verdict over that is a
                    // brittle output gate that turns a good analysis into a failure. Parse
                    // string numbers in place; drop nulls; only reject on a value that is
                    // genuinely not a number (e.g. an object or non-numeric text).
                    for (int i = sa.Count - 1; i >= 0; i--)
                    {
                        JToken el = sa[i];
                        if (el.Type == JTokenType.Integer || el.Type == JTokenType.Float) { continue; }
                        if (el.Type == JTokenType.Null) { sa.RemoveAt(i); continue; }
                        if (el.Type == JTokenType.String)
                        {
                            double dv;
                            string sv = el.ToString().Trim();
                            if (double.TryParse(sv, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out dv))
                            {
                                sa[i] = dv; continue;
                            }
                            // empty string -> drop; anything else non-numeric -> reject
                            if (sv.Length == 0) { sa.RemoveAt(i); continue; }
                        }
                        reason = "viz.series[" + i + "] must be numeric"; return false;
                    }
                }
                JToken sec = z["secondary"];
                if (sec != null && sec.Type != JTokenType.Null)
                {
                    if (sec.Type != JTokenType.Object) { reason = "viz.secondary must be an object"; return false; }
                    JObject so = (JObject)sec;
                    // v1.0.160.186 (#6): viz.secondary is OPTIONAL (like viz.series). If value is
                    // missing/non-numeric, DROP the whole secondary object instead of REJECTING the
                    // verdict (was: "Verdict failed validation: viz.secondary.value must be numeric" on
                    // causes with no natural secondary, e.g. ROSIG HPR VOLT LOW). label/unit/x_threshold
                    // already tolerate null; value was the only field that didn't.
                    JToken svPre = so["value"];
                    if (svPre == null || (svPre.Type != JTokenType.Integer && svPre.Type != JTokenType.Float))
                    {
                        z["secondary"] = null;
                    }
                    else
                    {
                        StripUnknown(so, _vizSecondaryKeys);
                        string[] secStr = { "label", "unit" };
                        for (int i = 0; i < secStr.Length; i++)
                        {
                            JToken sst = so[secStr[i]];
                            if (sst != null && sst.Type != JTokenType.Null)
                            {
                                if (sst.Type != JTokenType.String) { reason = "viz.secondary." + secStr[i] + " must be a string"; return false; }
                                so[secStr[i]] = ScrubPlatformInternals(FilterOperationalActions(sst.ToString()), "viz.secondary");
                            }
                        }
                        JToken xt = so["x_threshold"];
                        if (xt != null && xt.Type != JTokenType.Null
                            && xt.Type != JTokenType.Integer && xt.Type != JTokenType.Float)
                        {
                            reason = "viz.secondary.x_threshold must be numeric or null"; return false;
                        }
                    }
                }
            }

            // v1.0.75.0: mechanical final gate. Reads ONLY the ALERTING tag's
            // coverage level -- a different tag's good series is corroboration,
            // never coverage. AnalyzeStrictCoverage (D4) picks the policy:
            //   scoped (default) - downgrade only when a usable history_get ran
            //                       for SOME tag but the alerting tag never
            //                       reached VerifiedAdequate
            //   strict            - binary verdicts always require the alerting
            //                       tag at VerifiedAdequate, including alerts
            //                       grounded only by analyse_alert/ML
            // This is the single choke point both final paths pass through: the
            // JSON envelope and the SSE popup verdict frame each call this method.
            string vFinal = v["verdict"] != null ? v["verdict"].ToString() : "";
            if (vFinal == "CONFIRMED" || vFinal == "NOT_CONFIRMED")
            {
                // v1.0.82.0 rev J: a DERIVED alerting attribute has no tag of its own,
                // so the alerting-tag level is unsatisfiable by construction. For
                // derived alerts adequacy = component coverage (>=2 covered incl. one
                // voltage and one current when the formula uses both). Direct alerts:
                // unchanged strictness.
                bool adequate = Sess._derivedAlert
                    ? Sess._derivedComponentsAdequate
                    : AlertingCoverageLevel() == HistoryCoverageLevel.VerifiedAdequate;
                // v1.0.160.111 (BUG-5): card-authoritative bypass. If the card already carries the failing
                // value (a MinSafe/MaxSafe FAIL) or this is a FAILURE-type alert (a self-proving discrete
                // relay-state / blank / op-fail event), the incidence is established and sparse/absent
                // history must NOT force "Coverage gap" INCONCLUSIVE.
                if (Sess._cardHasFail || Sess._isFailureAlert || Sess._rawHasBreach) { adequate = true; }
                bool enforce = _strictCoverage ? !adequate : (!adequate && Sess.gotUsableHistoryGet);
                if (enforce)
                {
                    Sess.coverageDowngrade = "verdict " + vFinal + " downgraded: "
                        + (Sess._derivedAlert
                            ? "derived-attribute component coverage not established"
                            : "alerting-tag coverage "
                              + (AlertingCoverageLevel() == HistoryCoverageLevel.Unverified ? "unverified" : "not established")
                              + " (tag=" + (AlertingTagId() ?? "unresolved") + ")");
                    AiLog("WARN", "VERDICT", Sess.coverageDowngrade);
                    v["verdict"] = "INCONCLUSIVE";
                    v["headline"] = "Incidence coverage is insufficient for confirmation";
                    v["likely_cause"] = "Telemetry mechanism cannot be established from the available series";
                    v["recommended_action"] = "Retrieve incidence-covering history for the alerting attribute; "
                                            + "inspect only if independently corroborated";
                    v["category"] = "Coverage gap";
                    int confCap;
                    if (!int.TryParse(v["confidence"] == null ? "" : v["confidence"].ToString(), out confCap))
                    {
                        confCap = 0;
                    }
                    if (confCap > _downgradeConfidenceCap)
                    {
                        v["confidence"] = _downgradeConfidenceCap;
                    }
                    // P1: the downgrade reason comes FIRST so TrimWords can never hide it
                    string cav = v["caveats"] == null ? "" : v["caveats"].ToString();
                    v["caveats"] = TrimWords("Incidence coverage not established"
                        + (cav.Length > 0 ? "; " + cav : ""), 20);
                    // -- v1.0.76.0 -- REMOVED: word-matching a few assertive phrases
                    // ("confirmed", "proved", "genuine failure") let lines like "Active
                    // feed-side leakage diverted current from the relay end" survive
                    // unfiltered next to an INCONCLUSIVE verdict. A downgrade now
                    // REPLACES the evidence array outright with controlled factual
                    // lines rather than trying to detect every assertive phrasing.
                    // JArray evA = v["evidence"] as JArray;
                    // if (evA != null)
                    // {
                    //     for (int i = evA.Count - 1; i >= 0; i--)
                    //     {
                    //         string low = evA[i].ToString().ToLowerInvariant();
                    //         if (low.Contains("confirmed") || low.Contains("proved")
                    //             || low.Contains("proven") || low.Contains("genuine failure"))
                    //         {
                    //             evA.RemoveAt(i);   // factual/numeric lines stay; assertions go
                    //         }
                    //     }
                    //     if (evA.Count == 0)
                    //     {
                    //         evA.Add("Series does not reach the incidence with adequate unique samples");
                    //     }
                    // }
                    JArray controlledEv = new JArray();
                    controlledEv.Add("Alerting-attribute history did not provide adequate incidence coverage");
                    controlledEv.Add("Available alert facts remain visible; incidence-covering trend is not verified");
                    controlledEv.Add("Fault mechanism cannot be confirmed from the available trend");
                    controlledEv.Add("Additional incidence-covering history is required");
                    v["evidence"] = controlledEv;
                    // v1.0.76.0: neutralize the TREND-TELLING viz fields on a coverage
                    // downgrade -- a rejected series must not still narrate "confirmed
                    // mechanism" or "recovered" through the chart. value/threshold/
                    // baseline are directly-sourced scalars, not a trend claim, and stay.
                    JObject vizObj = v["viz"] as JObject;
                    if (vizObj != null)
                    {
                        vizObj["series"] = new JArray();
                        vizObj["trigger_pct"] = null;
                        vizObj["recovery_pct"] = null;
                        vizObj["phase_note"] = "Incidence-covering trend is unavailable; "
                                              + "development cannot be established";
                        vizObj["secondary"] = null;
                    }
                }
            }
            // v1.0.160.37: COMPUTED-VERDICT WINS (owner rule). This is the single choke point both final
            // paths pass through. When a PREDICTIVE/ANALOG sustain decision was computed AND coverage did
            // NOT downgrade the verdict, the computed class SETS the verdict deterministically (kills the
            // run-to-run flipping); the model's narration stays, only class + confidence + provenance change.
            // Failure/relay-state alerts never stash a decision, so they are untouched. Coverage-inadequate
            // (coverageDowngrade != null -> already INCONCLUSIVE) is left alone: no sustain trust on a bad series.
            if (_computedVerdictEnabled && Sess._computedVerdict != null && Sess.coverageDowngrade == null)
            {
                string _cvCls = Sess._computedVerdict["class"] != null ? Sess._computedVerdict["class"].ToString() : "";
                if (_cvCls == "CONFIRMED" || _cvCls == "NOT_CONFIRMED")
                {
                    string _vNow = v["verdict"] != null ? v["verdict"].ToString() : "";
                    string _cvR = Sess._computedVerdict["reason"] != null ? Sess._computedVerdict["reason"].ToString() : "computed from the FRS sustain window";
                    if (_vNow != _cvCls)
                    {
                        // v1.0.160.77: this branch is the ONLY place that knows the computed class
                        // actually replaced the model's. Comparing strings after the fact would
                        // read "won" whenever the model happened to agree, which is not the same.
                        Sess._computedWonLast = true;
                        AiLog("INFO", "VERDICT", "computed-verdict override " + _vNow + " -> " + _cvCls + " (" + _cvR + ")");
                        v["verdict"] = _cvCls;
                        JToken _cvConf = Sess._computedVerdict["confidence"];
                        if (_cvConf != null) { v["confidence"] = _cvConf; }
                        v["category"] = "Computed verdict";
                        string _cav0 = v["caveats"] == null ? "" : v["caveats"].ToString();
                        v["caveats"] = TrimWords(_cvR + (_cav0.Length > 0 ? "; " + _cav0 : ""), 24);
                    }
                    JArray _cvEvi = v["evidence"] as JArray;
                    if (_cvEvi == null) { _cvEvi = new JArray(); v["evidence"] = _cvEvi; }
                    _cvEvi.Insert(0, "[computed] " + _cvR);
                }
            }

            // v1.0.160.170/.171: a FAILURE alert cannot end INCONCLUSIVE. It fired because a card
            // condition FAILED, and the card is authoritative for the incidence -- so the only valid
            // outcomes are CONFIRMED (genuine) or NOT_CONFIRMED (normal throw / train movement, decided
            // by the DataLogger relay / adjacent-track check). If the verdict still came out INCONCLUSIVE
            // for a FAILURE alert with a card FAIL (track, point OR signal), resolve it to CONFIRMED and
            // say in the UI remark that INCONCLUSIVE is not a valid outcome for a failure. .171 broadened
            // this from the 10 TC track codes to ALL failure alerts.
            if (Sess._cardHasFail && (Sess._isTrackFailureCause || Sess._isFailureAlert))
            {
                string _fv = v["verdict"] != null ? v["verdict"].ToString() : "";
                if (_fv == "INCONCLUSIVE")
                {
                    AiLog("INFO", "VERDICT", "failure INCONCLUSIVE forbidden -> CONFIRMED (card FAIL authoritative)");
                    string _remark = "A FAILURE alert cannot be INCONCLUSIVE: the card condition FAILED and the card is authoritative for the incidence. Resolved to CONFIRMED (a normal throw / train movement would read NOT_CONFIRMED via the DataLogger relay / adjacent-track check).";
                    v["verdict"] = "CONFIRMED";
                    v["category"] = Sess._isTrackFailureCause ? "Track failure" : "Field HW";
                    v["headline"] = "Failure confirmed (card condition failed at the incidence)";
                    v["remark"] = _remark;
                    int _fc;
                    if (!int.TryParse(v["confidence"] == null ? "" : v["confidence"].ToString(), out _fc) || _fc < 70)
                    { v["confidence"] = 70; }
                    string _fcav = v["caveats"] == null ? "" : v["caveats"].ToString();
                    v["caveats"] = TrimWords(_remark + (_fcav.Length > 0 ? "; " + _fcav : ""), 34);
                    JArray _fev = new JArray();
                    _fev.Add("Card condition FAILED at the incidence -> the failure is established (card authoritative)");
                    _fev.Add("INCONCLUSIVE is not a valid outcome for a FAILURE alert");
                    _fev.Add("A normal throw / train movement would show as NOT_CONFIRMED via DataLogger relay / adjacent-track");
                    v["evidence"] = _fev;
                }
            }

            return true;
        }


        // Outermost balanced { ... } in a string, quote-aware and escape-aware.
        // v1.0.137.0: when a verdict is rejected (parse or validation), preserve WHAT the
        // model actually said -- single line, secret-redacted, clamped -- so provider
        // A/B failures are diagnosable from ai-log instead of being discarded.
        internal void LogRejectedReply(string fullText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fullText)) { AiLog("WARN", "VERDICT", "rejected reply: <empty>"); return; }
                string oneLine = fullText.Replace("\r", " ").Replace("\n", " ");
                AiLog("WARN", "VERDICT", "rejected reply (first 1500 chars): " + ClampLen(RedactSecretText(oneLine), 1500));
            }
            catch { }
        }

        // Moved to E7.AiCore.VerdictRules.ExtractFirstJsonObject in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static JObject ExtractFirstJsonObject(string text) => VerdictRules.ExtractFirstJsonObject(text);


        internal void StashComputedVerdict(bool sustained, long durSec, int needSec, string basis)
        {
            try
            {
                JObject cv = new JObject();
                cv["class"] = sustained ? "CONFIRMED" : "NOT_CONFIRMED";
                int conf = sustained ? (needSec > 0 && durSec >= (long)needSec * 4 ? 85 : 70) : 75;
                cv["confidence"] = conf;
                // v1.0.160.87: the COMPUTED path knows which case this is -- say so, rather than
                // leaving the label to the model. A breach that held < the FRS window is a real breach
                // that recovered, not a false alert.
                if (!sustained) { cv["category"] = "Transient - no action"; }
                cv["reason"] = sustained
                    ? ("computed: breach held " + durSec + "s >= FRS sustain " + needSec + "s (" + basis + ")")
                    : ("computed: breach held only " + durSec + "s < FRS sustain " + needSec + "s -- transient (" + basis + ")");
                cv["sustained"] = sustained;
                cv["durSec"] = durSec;
                cv["needSec"] = needSec;
                Sess._computedVerdict = cv;
            }
            catch { Sess._computedVerdict = null; }
        }

        // Moved to E7.AiCore.VerdictRules.TriggeredConditionFailed in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool TriggeredConditionFailed(JObject cond) => VerdictRules.TriggeredConditionFailed(cond);

        // Moved to E7.AiCore.VerdictRules.CardHasFailCondition in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool CardHasFailCondition(JObject ctx) => VerdictRules.CardHasFailCondition(ctx);

        // Moved to E7.AiCore.VerdictRules.IsTrackFailureCauseCode in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static bool IsTrackFailureCauseCode(string cause) => VerdictRules.IsTrackFailureCauseCode(cause);

        // Moved to E7.AiCore.VerdictRules.FilterOperationalActions in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string FilterOperationalActions(string text) => VerdictRules.FilterOperationalActions(text);

        // Moved to E7.AiCore.VerdictRules.FilterMonitoringAction in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string FilterMonitoringAction(string text) => VerdictRules.FilterMonitoringAction(text);

        // Moved to E7.AiCore.VerdictRules.FilterRecommendedAction in 1.0.162.0 (forwarder keeps call sites unchanged).
        internal static string FilterRecommendedAction(string text) => VerdictRules.FilterRecommendedAction(text);
    }
}
