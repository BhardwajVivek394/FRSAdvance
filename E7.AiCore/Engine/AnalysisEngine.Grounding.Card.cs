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
    // AnalysisEngine -- Grounding.Card.
    // Moved verbatim from AiChatController.Grounding.Card.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // Threshold from the alert card's configured limit, if present. thresholdSource set.
        internal bool TryCardThreshold(out double thr, out string src)
        {
            thr = double.NaN; src = null;
            try
            {
                JObject card = Sess._prefetchCtx != null ? Sess._prefetchCtx["alertCard"] as JObject : null;
                JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                JObject cond = (tc != null && tc.Count > 0) ? tc[0] as JObject : null;
                if (cond == null) { return false; }
                // v1.0.152.4: the real card stores configured limits at triggeredConditions[0]
                // .thresholds[] = [{label,value,unit,status,comparison}]. Prefer the FAIL entry;
                // else the first parseable value. (Scalar keys below kept as a fallback.)
                JArray thrs = cond["thresholds"] as JArray;
                if (thrs != null && thrs.Count > 0)
                {
                    JObject chosen = null;
                    foreach (JToken tk2 in thrs) { JObject to = tk2 as JObject; if (to == null) { continue; } string st = to["status"] != null ? to["status"].ToString().ToUpperInvariant() : ""; if (st == "FAIL") { chosen = to; break; } if (chosen == null) { chosen = to; } }
                    if (chosen != null && chosen["value"] != null)
                    {
                        double dv2; if (double.TryParse(chosen["value"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv2)) { thr = dv2; string lbl2 = chosen["label"] != null ? chosen["label"].ToString() : "threshold"; src = "alertCard:thresholds[" + lbl2 + "]"; return true; }
                    }
                }
                string[] keys = { "threshold", "limit", "thresholdValue", "setValue", "value" };
                foreach (string k in keys)
                {
                    JToken t = PropCI(cond, k);
                    if (t != null && t.Type != JTokenType.Null)
                    {
                        double d; if (double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) { thr = d; src = "alertCard:" + k; return true; }
                    }
                }
            }
            catch { }
            return false;
        }


        // v1.0.160.191: assemble the follow-up-chat grounding from the alert card the web already sent
        // to analyze, PLUS the cause's FRS logic. Returns null when there's no card (then the loop runs
        // as before). Passed as systemPromptOverride so the model starts fully grounded -- no rediscovery.
        // v1.0.160.197: fetch the point-machine operation AVERAGES (EdgeX "-Avg" attids) and format
        // them for the chat grounding, so chat returns the last-operation voltage/current DETERMINISTICALLY.
        // Guidance alone (.196) was not enough: live_get returns a positional payload the model cannot map,
        // and predict_pm_operation can be down -- so the model fell back to the 0-at-rest RDPMS tags.
        // Binding the parsed values removes that dependency. PT* cause + assetId only; "" on any miss
        // (then the .196 guidance still applies). Short timeout so a PM chat never stalls on this.
        internal static string BuildPmOperationValues(JObject payload)
        {
            try
            {
                if (payload == null) { return ""; }
                string cause = payload["causeCode"] != null ? payload["causeCode"].ToString() : "";
                if (string.IsNullOrEmpty(cause) && payload["alertCard"] is JObject)
                { cause = FirstStr((JObject)payload["alertCard"], "alertCode", "causeCode", "CauseCode"); }
                if (string.IsNullOrEmpty(cause) || !cause.TrimStart().ToUpperInvariant().StartsWith("PT ")) { return ""; }
                string assetId = payload["assetId"] != null ? payload["assetId"].ToString()
                                : (payload["AssetId"] != null ? payload["AssetId"].ToString() : "");
                int aid;
                if (string.IsNullOrEmpty(assetId) || !int.TryParse(assetId, out aid) || aid <= 0) { return ""; }

                Dictionary<int, double> v = FetchLiveValueMap(aid);
                if (v.Count == 0) { return ""; }
                // EdgeX PointMachine "-Avg" attid -> label + unit (V for voltage, A for operation current)
                string[][] defs = new string[][] {
                    new []{"2002","A End NW-V","V"}, new []{"4002","B End NW-V","V"},
                    new []{"1002","A End NW-C","A"}, new []{"3002","B End NW-C","A"},
                    new []{"7002","A End RW-V","V"}, new []{"9002","B End RW-V","V"},
                    new []{"6002","A End RW-C","A"}, new []{"8002","B End RW-C","A"},
                };
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                for (int i = 0; i < defs.Length; i++)
                {
                    int at = int.Parse(defs[i][0]); double val;
                    if (v.TryGetValue(at, out val))
                    {
                        if (sb.Length == 0)
                        {
                            sb.Append("POINT-MACHINE OPERATION VALUES (live, last-operation average -- AUTHORITATIVE. ")
                              .Append("Use these for operation voltage/current; do NOT read the 0-at-rest RDPMS ")
                              .Append("'A/B End - NW-V/NW-C/RW-V/RW-C' tags or a raw history sample):\n");
                        }
                        sb.Append("  ").Append(defs[i][1]).Append(": ")
                          .Append(Math.Round(val, 2)).Append(" ").Append(defs[i][2]).Append("\n");
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }


        // v1.0.160.103: the MAIN triggered-condition timestamp from the alert card
        // (alertCard.triggeredConditions[type=="main"].time), used to anchor the movement panel on
        // the real trigger sample rather than the (possibly later) stamped incidence. Null when absent.
        internal static string MainTriggeredTime(JObject ctx)
        {
            try
            {
                JToken card = ctx != null ? ctx["alertCard"] : null;
                JToken tc = card != null ? card["triggeredConditions"] : null;
                if (tc == null || tc.Type != JTokenType.Array) { return null; }
                JArray arr = (JArray)tc;
                for (int i = 0; i < arr.Count; i++)
                {
                    JObject cnd = arr[i] as JObject;
                    if (cnd == null) { continue; }
                    JToken ty = cnd["type"];
                    if (ty != null && string.Equals(ty.ToString(), "main", StringComparison.OrdinalIgnoreCase) && cnd["time"] != null)
                    {
                        return cnd["time"].ToString();
                    }
                }
            }
            catch { }
            return null;
        }


        // v1.0.160.104: the AUTHORITATIVE derived value AND its matching limit from the alert card.
        // Selects the triggered condition whose paramLabel canonicalizes to the derived param
        // (IBALST/RRAIL); falls back to the type=="main" condition. Parses 'measured' directly (already
        // unit-free) -- never the unit-appended dMeasured string. Also returns the FAILing threshold
        // from the SAME condition (its value/unit and whether the breach is above or below), so the
        // derived hero pairs the derived value with ITS OWN limit -- not the model viz.metric's limit
        // (on 527969 viz.metric is ITC RELAY END while the derived param is IBALST).
        internal static bool CardDerivedValue(JObject ctx, string derivedParam, out double val,
            out double limit, out bool haveLimit, out string limitUnit, out bool breachAbove)
        {
            val = double.NaN; limit = double.NaN; haveLimit = false; limitUnit = null; breachAbove = true;
            try
            {
                JToken card = ctx != null ? ctx["alertCard"] : null;
                JToken tc = card != null ? card["triggeredConditions"] : null;
                if (tc == null || tc.Type != JTokenType.Array) { return false; }
                JArray arr = (JArray)tc;
                string wantParam = string.IsNullOrEmpty(derivedParam) ? null : derivedParam.ToUpperInvariant().Trim();
                JObject mainCnd = null;
                for (int i = 0; i < arr.Count; i++)
                {
                    JObject cnd = arr[i] as JObject;
                    if (cnd == null) { continue; }
                    JToken ty = cnd["type"];
                    if (ty != null && string.Equals(ty.ToString(), "main", StringComparison.OrdinalIgnoreCase) && mainCnd == null) { mainCnd = cnd; }
                    if (wantParam == null) { continue; }
                    JToken pl = cnd["paramLabel"];
                    if (pl == null) { continue; }
                    string canon = DomainAttributeName(pl.ToString());
                    if (canon != null && canon.ToUpperInvariant().Trim() == wantParam)
                    {
                        double dv;
                        if (cnd["measured"] != null && double.TryParse(cnd["measured"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv))
                        {
                            val = dv;
                            ExtractFailingThreshold(cnd, out limit, out haveLimit, out limitUnit, out breachAbove);
                            return true;
                        }
                    }
                }
                if (mainCnd != null && mainCnd["measured"] != null)
                {
                    double dv2;
                    if (double.TryParse(mainCnd["measured"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv2))
                    {
                        val = dv2;
                        ExtractFailingThreshold(mainCnd, out limit, out haveLimit, out limitUnit, out breachAbove);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }


        // v1.0.160.104: the FAILing threshold of a triggered condition -- its numeric value, unit, and
        // whether the breach is above (comparison ">") or below ("<"). Picks the first status=="FAIL";
        // if none is marked, the first parseable threshold. haveLimit=false when none resolves.
        internal static void ExtractFailingThreshold(JObject cnd, out double limit, out bool haveLimit, out string limitUnit, out bool breachAbove)
        {
            limit = double.NaN; haveLimit = false; limitUnit = null; breachAbove = true;
            try
            {
                JToken thsT = cnd["thresholds"];
                if (thsT == null || thsT.Type != JTokenType.Array) { return; }
                JArray ths = (JArray)thsT;
                JObject firstParsable = null;
                for (int i = 0; i < ths.Count; i++)
                {
                    JObject th = ths[i] as JObject;
                    if (th == null || th["value"] == null) { continue; }
                    double tv;
                    if (!double.TryParse(th["value"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tv)) { continue; }
                    if (firstParsable == null) { firstParsable = th; }
                    JToken st = th["status"];
                    if (st != null && string.Equals(st.ToString(), "FAIL", StringComparison.OrdinalIgnoreCase))
                    {
                        limit = tv; haveLimit = true;
                        if (th["unit"] != null) { limitUnit = th["unit"].ToString(); }
                        JToken cmp = th["comparison"];
                        if (cmp != null) { breachAbove = cmp.ToString().IndexOf('<') < 0; }
                        return;
                    }
                }
                if (firstParsable != null)
                {
                    double tv2;
                    if (double.TryParse(firstParsable["value"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out tv2))
                    {
                        limit = tv2; haveLimit = true;
                        if (firstParsable["unit"] != null) { limitUnit = firstParsable["unit"].ToString(); }
                        JToken cmp2 = firstParsable["comparison"];
                        if (cmp2 != null) { breachAbove = cmp2.ToString().IndexOf('<') < 0; }
                    }
                }
            }
            catch { }
        }


        // v1.0.80.0: extract the alerting attribute's thresholds from the alert card
        // as a DEGRADED baseline, used only when get_attribute_range yields nothing.
        // Card shape (per triggeredConditions[0]):
        //   paramLabel: "ITC RELAY END(mA)", measured: "160.26", unit: "mA",
        //   thresholds: [ {label:"80% Avg", value:"214.80", comparison:"<", status:"FAIL"},
        //                 {label:"Min Safe", value:"210.00", comparison:"<", status:"FAIL"} ]
        // Returns a compact text block, or null if the card has no usable thresholds
        // (then the caller behaves exactly as before -- gap recorded, model may fetch).
        internal static string BuildCardBaseline(JObject ctx)
        {
            if (ctx == null) { return null; }
            JToken card = ctx["alertCard"];
            if (card == null || card.Type != JTokenType.Object) { return null; }
            JToken tc = card["triggeredConditions"];
            if (tc == null || tc.Type != JTokenType.Array || ((JArray)tc).Count == 0) { return null; }
            JObject cond = ((JArray)tc)[0] as JObject;
            if (cond == null) { return null; }

            string param = cond["paramLabel"] != null ? cond["paramLabel"].ToString() : "";
            string measured = cond["measured"] != null ? cond["measured"].ToString() : "";
            string unit = cond["unit"] != null ? cond["unit"].ToString() : "";
            string canonical = DomainAttributeName(param);   // map to the tag name if we can

            JToken th = cond["thresholds"];
            if (th == null || th.Type != JTokenType.Array || ((JArray)th).Count == 0) { return null; }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("attribute: ").Append(param);
            if (!string.IsNullOrEmpty(canonical)) { sb.Append(" (tag: ").Append(canonical).Append(")"); }
            if (measured.Length > 0) { sb.Append("  measured: ").Append(measured).Append(" ").Append(unit); }
            sb.Append("\n");
            bool any = false;
            foreach (JToken t in (JArray)th)
            {
                JObject to = t as JObject;
                if (to == null) { continue; }
                string label = to["label"] != null ? to["label"].ToString() : "";
                string val = to["value"] != null ? to["value"].ToString() : "";
                string cmp = to["comparison"] != null ? to["comparison"].ToString() : "";
                string status = to["status"] != null ? to["status"].ToString() : "";
                if (label.Length == 0 && val.Length == 0) { continue; }
                sb.Append("  ").Append(label).Append(": ").Append(cmp).Append(" ").Append(val).Append(" ").Append(unit);
                if (status.Length > 0) { sb.Append("  [").Append(status).Append("]"); }
                sb.Append("\n");
                any = true;
            }
            if (!any) { return null; }
            sb.Append("NOTE: degraded baseline - only the triggered attribute's card thresholds are available; MaxSafe/MinFail/true-average are NOT (get_attribute_range unavailable).");
            return sb.ToString();
        }


        // v1.0.160.169 (BUG-3a/4 + 3b): a deterministic verdict anchor built from the alert card, placed
        // at the TOP of the evidence so the model judges the INCIDENCE value, not the recovered one. A
        // card-FAIL that recovered is a CONFIRMED transient breach -- recovery sets severity, never the
        // verdict. Stops "it recovered -> INCONCLUSIVE" softening (542185/533604/534685/534033), flags a
        // systemic all-attributes-drop as NOT a per-attribute fault, and routes an impossible If<Ir to a
        // data-quality note (553745). Returns "" when the card carries no FAIL condition.
        internal static string BuildVerdictAnchor(JObject ctx)
        {
            try
            {
                JObject card = ctx != null ? ctx["alertCard"] as JObject : null;
                JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                if (tc == null) { return ""; }
                for (int i = 0; i < tc.Count; i++)
                {
                    JObject c = tc[i] as JObject;
                    if (c == null || c["paramLabel"] == null) { continue; }
                    JArray th = c["thresholds"] as JArray;
                    if (th == null) { continue; }
                    for (int k = 0; k < th.Count; k++)
                    {
                        JObject t0 = th[k] as JObject;
                        if (t0 == null || t0["status"] == null
                            || !string.Equals(t0["status"].ToString().Trim(), "FAIL", StringComparison.OrdinalIgnoreCase))
                        { continue; }
                        string attr = c["paramLabel"].ToString();
                        string meas = c["measured"] != null ? c["measured"].ToString() : "?";
                        string unit = c["unit"] != null ? c["unit"].ToString() : "";
                        string thL = t0["label"] != null ? t0["label"].ToString() : "limit";
                        string thV = t0["value"] != null ? t0["value"].ToString() : "?";
                        string cmp = t0["comparison"] != null ? t0["comparison"].ToString() : "vs";
                        bool reset = GetCtx(ctx, "resetTime").Trim().Length > 0;
                        StringBuilder a = new StringBuilder();
                        a.Append("[VERDICT ANCHOR (deterministic, from the alert card) -- ").Append(attr)
                         .Append(": incidence measured = ").Append(meas).Append(" ").Append(unit)
                         .Append(", status FAIL vs ").Append(thL).Append(" ").Append(cmp).Append(" ").Append(thV)
                         .Append(". This IS a confirmed breach at the incidence.");
                        if (reset)
                        {
                            a.Append(" It RESET (recovered) -> a CONFIRMED *TRANSIENT* breach; recovery sets SEVERITY, never the verdict.");
                        }
                        a.Append(" DECIDE from the INCIDENCE value, not the recovered one: fetched incidence sample agrees with this FAIL -> CONFIRMED")
                         .Append(reset ? " (transient)" : "")
                         .Append("; fetched incidence sample shows NO breach at the incidence -> NOT_CONFIRMED; ALL attributes read 0 together at the incidence (systemic power/comms/DL drop) -> NOT_CONFIRMED (systemic, not a per-attribute fault); value cannot be established at all -> INCONCLUSIVE (rare). Do NOT return INCONCLUSIVE for a card-FAIL that merely recovered. A physically-impossible reading (e.g. If < Ir, IBALST negative) is a channel/calibration artifact -> report as DATA QUALITY, never a verdict driver.]").Append("\n\n");
                        return a.ToString();
                    }
                }
            }
            catch { }
            return "";
        }


        // v1.0.160.171: the POINT MACHINE common-condition anchor (all PT causes) -- the PM analogue
        // of BuildVerdictAnchor. Structures the verdict around the 4 PM steps: (1) card verification,
        // (2) cause-logic mapped-attribute check at the incidence, (3) operation ARRAY pattern
        // (predict_pm_operation), (4) DataLogger RELAY state as the DECIDER. A card-FAIL that recovered
        // is CONFIRMED (transient); a PM failure never ends INCONCLUSIVE. Returns "" for non-PM alerts.
        internal static string BuildPmVerdictAnchor(JObject ctx)
        {
            try
            {
                string cause = GetCtx(ctx, "causeCode");
                string atype = GetCtx(ctx, "assetType");
                bool isPm = (atype != null && atype.ToUpperInvariant().Contains("POINT"))
                            || (cause != null && cause.TrimStart().ToUpperInvariant().StartsWith("PT "));
                if (!isPm) { return ""; }

                string attr = "", meas = "?", unit = "", thL = "limit", thV = "?", cmp = "vs";
                JObject card = ctx != null ? ctx["alertCard"] as JObject : null;
                JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                if (tc != null)
                {
                    for (int i = 0; i < tc.Count && attr.Length == 0; i++)
                    {
                        JObject c = tc[i] as JObject;
                        if (c == null || c["paramLabel"] == null) { continue; }
                        JArray th = c["thresholds"] as JArray;
                        if (th == null) { continue; }
                        for (int k = 0; k < th.Count; k++)
                        {
                            JObject t0 = th[k] as JObject;
                            if (t0 == null || t0["status"] == null
                                || !string.Equals(t0["status"].ToString().Trim(), "FAIL", StringComparison.OrdinalIgnoreCase)) { continue; }
                            attr = c["paramLabel"].ToString();
                            meas = c["measured"] != null ? c["measured"].ToString() : "?";
                            unit = c["unit"] != null ? c["unit"].ToString() : "";
                            thL = t0["label"] != null ? t0["label"].ToString() : "limit";
                            thV = t0["value"] != null ? t0["value"].ToString() : "?";
                            cmp = t0["comparison"] != null ? t0["comparison"].ToString() : "vs";
                            break;
                        }
                    }
                }
                bool reset = GetCtx(ctx, "resetTime").Trim().Length > 0;
                bool isFail = (GetCtx(ctx, "alertType") ?? "").Trim().ToUpperInvariant().StartsWith("F");

                StringBuilder a = new StringBuilder();
                a.Append("[POINT MACHINE VERDICT ANCHOR -- ").Append(cause).Append(". Decide from these four, in order:\n");
                a.Append(" 1. CARD: ");
                if (attr.Length > 0)
                {
                    a.Append(attr).Append(" measured ").Append(meas).Append(" ").Append(unit)
                     .Append(" FAIL vs ").Append(thL).Append(" ").Append(cmp).Append(" ").Append(thV)
                     .Append(" at incidence -- breach established (card authoritative).");
                }
                else { a.Append("the card's failing condition is authoritative for the incidence."); }
                a.Append("\n 2. CAUSE LOGIC -- verify the cause's mapped attributes at the INCIDENCE value (not the recovered one): the alerting attribute plus its coupled partners (LOC / room voltage, feed & relay-end current, indication voltage). Coherent with the cause -> supports CONFIRMED.");
                a.Append("\n 3. ARRAY PATTERN -- read predict_pm_operation for this asset/end: creeping / obstruction / voltage-sag / sluggish / normal signature.");
                a.Append("\n 4. DATALOGGER RELAY (the DECIDER) -- Combined-NWKR / RWKR / NWCR / RWCR (attid 267/268/282/283, DataLogger) states at the incidence. A relay drop/pick consistent with the cause -> CONFIRMED; relays healthy with a normal throw -> re-examine (possible normal throw-related de-energization).");
                if (reset) { a.Append("\n A card-FAIL that RESET (recovered) is a CONFIRMED *TRANSIENT* breach -- recovery sets severity, never the verdict."); }
                if (isFail) { a.Append(" A point-machine FAILURE never ends INCONCLUSIVE: resolve CONFIRMED (genuine) or NOT_CONFIRMED (normal throw / train)."); }
                a.Append("]\n\n");
                return a.ToString();
            }
            catch { }
            return "";
        }


        // v1.0.160.173: some alerts arrive with NO alertCard -- only a rawMessage sentence carrying
        // the breach value in <b>..</b> (e.g. "Operation Time high. <b>TPT N: 9.04</b>") or a failure
        // statement ("failed in R ... reasons not diagnosed"). Extract attribute/value/direction so a
        // cardless alert can still anchor on its stated breach. Returns false when there IS a card
        // (the card path handles it) or nothing parseable and it is not an undiagnosed failure.
        internal static bool RawMessageBreach(JObject ctx, out string attr, out string val, out string dir, out bool undiagnosed)
        {
            attr = ""; val = ""; dir = ""; undiagnosed = false;
            try
            {
                JToken card = ctx != null ? ctx["alertCard"] : null;
                if (card != null && card.Type == JTokenType.Object) { return false; }
                string raw = GetCtx(ctx, "rawMessage");
                if (string.IsNullOrEmpty(raw)) { return false; }
                string low = raw.ToLowerInvariant();
                if (low.Contains("not diagnosed") || (low.Contains("reason") && low.Contains("not"))) { undiagnosed = true; }
                dir = low.Contains("high") ? "high" : (low.Contains("low") ? "low" : "");
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                    raw, @"<b>\s*([A-Za-z0-9 _/().-]+?)\s*:\s*([-0-9.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) { attr = m.Groups[1].Value.Trim(); val = m.Groups[2].Value.Trim(); return true; }
                System.Text.RegularExpressions.Match m2 = System.Text.RegularExpressions.Regex.Match(
                    raw, @"([A-Za-z][A-Za-z0-9 _/().-]{1,24}?)\s*:\s*([-0-9.]+)\s*(A|V|sec|s)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m2.Success) { attr = m2.Groups[1].Value.Trim(); val = m2.Groups[2].Value.Trim(); return true; }
                return undiagnosed;
            }
            catch { }
            return false;
        }


        // v1.0.160.173: anchor for a cardless alert -- surfaces the rawMessage breach so the verdict
        // is judged on the stated value, not on the absent card / missing telemetry.
        internal static string BuildRawMessageAnchor(JObject ctx)
        {
            try
            {
                string attr, val, dir; bool undiag;
                if (!RawMessageBreach(ctx, out attr, out val, out dir, out undiag)) { return ""; }
                string cause = GetCtx(ctx, "causeCode");
                bool isFail = (GetCtx(ctx, "alertType") ?? "").Trim().ToUpperInvariant().StartsWith("F");
                StringBuilder a = new StringBuilder();
                a.Append("[RAW-MESSAGE ANCHOR -- this alert has NO structured card; the FRS reported it as text. Cause: ").Append(cause).Append(". ");
                if (attr.Length > 0 && val.Length > 0)
                {
                    a.Append("Stated breach: ").Append(attr).Append(" = ").Append(val);
                    if (dir.Length > 0) { a.Append(" (").Append(dir).Append(")"); }
                    a.Append(". This measured value IS the alert's evidence -- the alert fired on it. ");
                    a.Append(isFail
                        ? "A FAILURE with a stated value is CONFIRMED."
                        : "A predictive breach with a stated value is CONFIRMED (single event; transient if it reset).");
                }
                else if (undiag)
                {
                    a.Append("The FRS states a FAILURE occurred (reason not diagnosed). The failure is real -> CONFIRMED with mechanism undiagnosed.");
                }
                a.Append(" This asset may have NO configured telemetry; absence of history does NOT downgrade a stated breach or a failure. INCONCLUSIVE only if there is neither a stated value nor any telemetry AND it is not a failure.]");
                return a.ToString() + "\n\n";
            }
            catch { }
            return "";
        }
    }
}
