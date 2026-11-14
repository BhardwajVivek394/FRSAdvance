using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static E7.AiCore.AiSettings;
using static E7.AiCore.AiText;
using static E7.AiCore.AiJson;
using static E7.AiCore.AiTime;
using static E7.AiCore.Redaction;
using static E7.AiCore.ScopeGuard;
using static E7.AiCore.EngineIdentity;
using static E7.AiCore.TrainingText;
using static E7.AiCore.VerdictRules;

namespace E7.AiCore
{
    // Pure verdict rules: categories, deterministic actions, provenance, action filters, track-failure causes.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class VerdictRules
    {

        // ---- strict verdict schema validation + operational-action filtering ----
        public static readonly string[] _verdictCategories =
            { "Field HW", "Config", "Calibration/Threshold", "Platform",
              "A10 transition", "Datalogger", "Genuine failure",
              "Coverage gap",     // v1.0.75.0: mechanical downgrade category (D6)
              // v1.0.160.87: separate "the alert was RIGHT and it passed" from "the alert was WRONG".
              "Transient - no action",   // breach real, recovered inside the sustain window
              "Derived artifact" };      // breach not real -- divisor moved / component held


        internal const string _noRailwayAction =
            "No railway field action follows from this alert.";


        // recommended_action is where operational advice would actually appear, so
        // it carries the wider net as well.
        // v1.0.160.165: monitoring/telemetry WORK sentences. The action field is what a technician
        // is sent to do, and that is railway signalling work only. Matches the instruction, not the
        // noun: "modem" inside a diagnosis is fine, "check the modem" as an action is not.
        internal static readonly Regex _monActionRx = new Regex(
            @"\b(?:check|inspect|verify|test|replace|restart|reboot|reset|reseat|swap|raise|report|"
          + @"escalate|clear|acknowledge|ack|chase|follow\s+up|contact|inform)\b[^.;]*?"
          + @"\b(?:modem|OFC|4G|GSM|SIM|RSSI|signal\s+strength|gateway|MQTT|TCP|broker|"
          + @"A10|ADC|datalogger|data\s+logger|telemetry|monitoring(?:\s+team)?|RDPMS|EdgeX|"
          + @"DataReceiver|LogWatch|ingestion|network|connectivity|stale\s+alerts?)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        internal const string _opRefusal =
            "I cannot make that determination. Operational decisions belong to the section controller.";


        // Advisory / system-action words. These are ONLY suppressed in the
        // recommended_action field. They were previously applied everywhere, which
        // wiped out factual EVIDENCE lines: "reset", "should" and "must" are
        // ordinary analytical words -- "the alert reset at 12:02" is an
        // observation, not an instruction -- and a matching line was replaced
        // wholesale by the refusal text.
        internal static readonly Regex OpPatternAdvisory = new Regex(
            @"\b(should|must|acknowledge alert|ack alert|override)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        // Directing train or signalling movements is the thing that must never be
        // produced. This list is applied to EVERY field.
        internal static readonly Regex OpPattern = new Regex(
            @"\b(allow train|hold train|dispatch|send train|clear signal|" +
            @"change aspect|throw point|operate point)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        // v1.0.160.170: the 10 TC FAILURE causes (same list as the TRACK-FAILURE TRAIN-MOVEMENT CHECK
        // in the prompt). A failure on any of these cannot end INCONCLUSIVE -- the card FAIL is
        // authoritative; only CONFIRMED (genuine) or NOT_CONFIRMED (train movement) are valid.
        internal static readonly HashSet<string> _trackFailureCauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TC CKT OPEN", "TC SHORT", "TC RAIL RES OPEN", "TC VAR RES OPEN", "TC CH RES OPEN",
            "TC TFC O/P VOLT FAIL", "TC TR RELAY DEFECT", "TC TR UP TPR DN", "TC TPR RELAY DEFECT",
            "REASON UNKNOWN"
        };


        // Remove a clause that justifies work by the ML standing condition. That finding has its own
        // band and its own action; folding it in here turned "monitor" into "schedule".
        internal static readonly Regex _standingClauseRx = new Regex(
            @"\s*(?:;|,|\.|--)?\s*(?:per|as per|due to|because of|given|citing|based on)\s+(?:the\s+)?ML[^.;]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public static string FirstStr(JObject o, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                JToken t = o[keys[i]];
                if (t != null && t.Type != JTokenType.Null) { return t.ToString(); }
            }
            return null;
        }


        // SL 3: actions that FOLLOW from the category, so they do not vary run to run. Deliberately a
        // short list -- only where the category itself already decided the outcome.
        public static string DeterministicAction(string category)
        {
            if (string.IsNullOrEmpty(category)) { return null; }
            if (category.Equals("Transient - no action", StringComparison.OrdinalIgnoreCase))
            { return "No action for this alert. Monitor; raise a job only if it recurs."; }
            if (category.Equals("Derived artifact", StringComparison.OrdinalIgnoreCase))
            { return "No field action. The breach is a computation artefact, not a measured fault."; }
            if (category.Equals("Coverage gap", StringComparison.OrdinalIgnoreCase))
            { return "No conclusion possible from the available history. Re-run once coverage is restored."; }
            return null;
        }


        public static string StripStandingConditionClause(string v)
        {
            if (string.IsNullOrEmpty(v)) { return v; }
            if (v.IndexOf("ML", StringComparison.Ordinal) < 0) { return v; }
            string o = _standingClauseRx.Replace(v, "").Trim();
            o = o.TrimEnd(';', ',', ' ');
            if (o.Length > 0 && !o.EndsWith(".")) { o += "."; }
            return o.Length >= 8 ? o : v;
        }


        // v1.0.110.0: map an unknown/misspelled/descriptive category to the nearest valid
        // bucket, so a good verdict is never discarded over a label. Case-insensitive exact
        // match first; then keyword heuristics; then a verdict-appropriate default. Returns a
        // value guaranteed to be in _verdictCategories.
        public static string CoerceCategory(string raw, string verdict)
        {
            string c = (raw ?? "").Trim();
            // exact match ignoring case
            for (int i = 0; i < _verdictCategories.Length; i++)
            {
                if (string.Equals(c, _verdictCategories[i], StringComparison.OrdinalIgnoreCase))
                { return _verdictCategories[i]; }
            }
            string l = c.ToLowerInvariant();
            // keyword heuristics -> valid bucket. ORDER MATTERS: check the more-specific/physical
            // buckets before the generic ones so "point wiring failure" -> Field HW (not Config)
            // and "network data gap" -> Coverage gap (not Platform).
            // v1.0.160.87: a computation artifact is NOT a field fault -- test it FIRST, or phrases
            // like "spike drove the derived value" fall through to Genuine failure.
            if (l.Contains("artifact") || l.Contains("artefact") || l.Contains("divisor")
                || l.Contains("computation") || l.Contains("computed value") || l.Contains("formula"))
            { return "Derived artifact"; }
            if (l.Contains("transient") || l.Contains("momentary") || l.Contains("self-clear")
                || l.Contains("self clear") || l.Contains("recovered") || l.Contains("settled")
                || l.Contains("brief") || l.Contains("spike"))
            { return "Transient - no action"; }
            if (l.Contains("obstruct") || l.Contains("mechanic") || l.Contains("jam")
                || l.Contains("point machine") || l.Contains("motor") || l.Contains("physical")
                || l.Contains("genuine") || l.Contains("real fail") || l.Contains("field fail"))
            { return "Genuine failure"; }
            // coverage / no-data BEFORE platform (a "network data gap" is a coverage gap)
            if (l.Contains("coverage") || l.Contains("no data") || l.Contains("no-data")
                || l.Contains("data gap") || l.Contains("missing") || l.Contains("insufficient"))
            { return "Coverage gap"; }
            // field hardware (incl. wiring/cable/relay/sensor) BEFORE config
            if (l.Contains("hardware") || l.Contains("wiring") || l.Contains("cable")
                || l.Contains("relay") || l.Contains("sensor") || l.Contains("field")
                || l.Equals("hw") || l.Contains(" hw"))
            { return "Field HW"; }
            if (l.Contains("calib") || l.Contains("thresh") || l.Contains("tuning") || l.Contains("limit"))
            { return "Calibration/Threshold"; }
            if (l.Contains("datalog") || l.Contains("logger") || l.Contains("dl "))
            { return "Datalogger"; }
            if (l.Contains("a10") || l.Contains("transition") || l.Contains("migrat"))
            { return "A10 transition"; }
            if (l.Contains("config") || l.Contains("mapping") || l.Contains("setting") || l.Contains("parameter"))
            { return "Config"; }
            if (l.Contains("platform") || l.Contains("server") || l.Contains("comm")
                || l.Contains("network") || l.Contains("connectivity"))
            { return "Platform"; }
            // default by verdict: a confirmed condition is a genuine field issue; an
            // inconclusive one is most often a coverage/data gap; otherwise Field HW.
            if (string.Equals(verdict, "CONFIRMED", StringComparison.OrdinalIgnoreCase)) { return "Field HW"; }
            if (string.Equals(verdict, "INCONCLUSIVE", StringComparison.OrdinalIgnoreCase)) { return "Coverage gap"; }
            return "Field HW";
        }


        // v1.0.160.27 (Item 6): tally the per-line provenance tags the model put on evidence bullets
        // ([fetched]/[record]/[computed]) into a small summary for meta. Static, best-effort, no side effects.
        public static JObject ComputeEvidenceProvenance(JObject v)
        {
            if (v == null) { return null; }
            JArray ev = v["evidence"] as JArray;
            if (ev == null || ev.Count == 0) { return null; }
            int fetched = 0, record = 0, computed = 0, tagged = 0;
            for (int i = 0; i < ev.Count; i++)
            {
                JToken t = ev[i];
                if (t == null || t.Type != JTokenType.String) { continue; }
                string low = t.ToString().TrimStart().ToLowerInvariant();
                if (low.StartsWith("[fetched]")) { fetched++; tagged++; }
                else if (low.StartsWith("[record]")) { record++; tagged++; }
                else if (low.StartsWith("[computed]")) { computed++; tagged++; }
            }
            JObject o = new JObject();
            o["fetched"] = fetched;
            o["record"] = record;
            o["computed"] = computed;
            o["tagged"] = tagged;
            o["total"] = ev.Count;
            o["verified"] = (fetched + computed) > 0;   // any independently-obtained evidence at all?
            return o;
        }


        public static int DemoteFetchedTags(JArray ev)
        {
            if (ev == null) { return 0; }
            int n = 0;
            for (int i = 0; i < ev.Count; i++)
            {
                JToken t = ev[i];
                if (t == null || t.Type != JTokenType.String) { continue; }
                string s = t.ToString();
                if (s.StartsWith("[fetched]", StringComparison.OrdinalIgnoreCase))
                {
                    ev[i] = "[unverified]" + s.Substring("[fetched]".Length);
                    n++;
                }
            }
            return n;
        }


        public static string AppendCaveat(string existing, string add)
        {
            if (string.IsNullOrWhiteSpace(existing)) { return add; }
            string e = existing.TrimEnd();
            if (!e.EndsWith(";") && !e.EndsWith(".")) { e += ";"; }
            return e + " " + add;
        }


        public static void StripUnknown(JObject o, string[] allowed)
        {
            List<string> drop = new List<string>();
            foreach (var prop in o.Properties())
            {
                bool ok = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(prop.Name, allowed[i], StringComparison.Ordinal)) { ok = true; break; }
                }
                if (!ok) { drop.Add(prop.Name); }
            }
            for (int i = 0; i < drop.Count; i++) { o.Remove(drop[i]); }
        }


        // The prompt states hard word budgets; the validator never checked them, so
        // "MAX 15 WORDS" was advice the system did not keep. Trim rather than reject:
        // a sound verdict should not be discarded for being wordy, and the operator
        // reads a fixed-width card either way. Truncation does NOT save generation
        // time -- the model has already paid to write the words -- so this is about
        // consistency and payload, while max_tokens on the synthesis client is what
        // actually bounds the time.
        public static string TrimWords(string text, int maxWords)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            string[] w = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length <= maxWords) { return text; }
            return string.Join(" ", w, 0, maxWords).TrimEnd(',', ';', '-') + "...";
        }


        public static bool NumOrNull(JObject o, string key, out string reason)
        {
            reason = null;
            JToken t = o[key];
            if (t == null || t.Type == JTokenType.Null) { return true; }
            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) { return true; }
            reason = "viz." + key + " must be a number or null";
            return false;
        }


        public static JObject ExtractFirstJsonObject(string text)
        {
            if (string.IsNullOrEmpty(text)) { return null; }
            int start = text.IndexOf('{');
            if (start < 0) { return null; }
            int depth = 0; bool inStr = false; bool esc = false;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (esc) { esc = false; continue; }
                if (c == '\\' && inStr) { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) { continue; }
                if (c == '{') { depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        try { return JObject.Parse(text.Substring(start, i - start + 1)); }
                        catch { return null; }
                    }
                }
            }
            return null;
        }


        // v1.0.160.12: a triggered condition "failed" if its own status is FAIL, or any of its
        // threshold rows is FAIL. Card 'main' conditions carry status on thresholds[]; some (ips)
        // carry a top-level status. Used to anchor coverage on the DECIDING (failed) attribute.
        public static bool TriggeredConditionFailed(JObject cond)
        {
            if (cond == null) { return false; }
            JToken st = cond["status"];
            if (st != null && st.ToString().ToUpperInvariant() == "FAIL") { return true; }
            JToken thrs = cond["thresholds"];
            if (thrs != null && thrs.Type == JTokenType.Array)
            {
                foreach (JToken tk in (JArray)thrs)
                {
                    JObject to = tk as JObject; if (to == null) { continue; }
                    JToken ts = to["status"];
                    if (ts != null && ts.ToString().ToUpperInvariant() == "FAIL") { return true; }
                }
            }
            return false;
        }


        // v1.0.160.111 (BUG-5): true if ANY triggered condition on the card carries a FAIL. When the
        // card provides the failing value, the incidence is established regardless of history density,
        // so the coverage gate must not downgrade to "Coverage gap".
        public static bool CardHasFailCondition(JObject ctx)
        {
            try
            {
                JToken card = ctx != null ? ctx["alertCard"] : null;
                JToken tc = card != null ? card["triggeredConditions"] : null;
                if (tc is JArray)
                {
                    foreach (JToken c in (JArray)tc)
                    {
                        if (TriggeredConditionFailed(c as JObject)) { return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool IsTrackFailureCauseCode(string cause)
        {
            return !string.IsNullOrEmpty(cause) && _trackFailureCauses.Contains(cause.Trim());
        }


        public static string FilterOperationalActions(string text)
        {
            return FilterOperationalActions(text, _opRefusal);   // 1.0.167.0: AiChat's wording; logic shared with ChatBot
        }

        // 1.0.167.0: shared by AiChat and ChatBot -- each product passes its own refusal wording.
        public static string FilterOperationalActions(string text, string refusal)
        {
            if (text != null && OpPattern.IsMatch(text)) { return refusal; }
            return text;
        }


        // Strip monitoring-work sentences from an action, keeping the railway ones. When nothing
        // railway-side survives, say so plainly rather than leaving an empty or half-sentence field.
        public static string FilterMonitoringAction(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            string[] parts = text.Split(new char[] { ';', '.' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder kept = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0)
                {
                    continue;
                }
                if (_monActionRx.IsMatch(p))
                {
                    continue;
                }
                if (kept.Length > 0)
                {
                    kept.Append("; ");
                }
                kept.Append(p);
            }
            if (kept.Length == 0)
            {
                return _noRailwayAction;
            }
            string outp = kept.ToString();
            // Stripping the FIRST clause can leave the field starting mid-sentence
            // ("inspect point machine slide chairs") -- restore the capital.
            if (outp.Length > 0 && char.IsLower(outp[0]))
            {
                outp = char.ToUpperInvariant(outp[0]) + outp.Substring(1);
            }
            if (!outp.EndsWith(".") && !outp.EndsWith(";"))
            {
                outp = outp + ".";
            }
            return outp;
        }


        public static string FilterRecommendedAction(string text)
        {
            if (text == null) { return null; }
            if (OpPattern.IsMatch(text) || OpPatternAdvisory.IsMatch(text)) { return _opRefusal; }
            // v1.0.160.165: railway-only scope for the instruction (owner rule).
            return FilterMonitoringAction(text);
        }
    }
}
