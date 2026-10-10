using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, OpenAiClient, DeepSeekClient) keep the 616 namespace

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // ============================================================================
    // v1.2.9.8 -- ChatBot CAUSE LOGIC (one file).
    //
    // The cause KNOWLEDGE lives in exactly ONE authoritative file -- CauseCatalog.cs
    // (shared with AiChat, same namespace/assembly: E7MRIWeb.Areas.FRS25.Controllers).
    // This partial is only the ChatBot's glue to it; it defines no cause rules of its
    // own, it just reads CauseCatalog.
    //
    // WHY: no live-data tool exposes the trigger condition / threshold / rule behind a
    // cause code, so the assistant used to refuse ("I don't have a tool that returns the
    // trigger condition ... and I won't guess"). When the user names a cause code, the
    // matching CauseDef from the catalog is injected into the system prompt as
    // authoritative doctrine, so the model answers the rule from the catalog instead of
    // refusing or inventing a threshold. Live/history tools are still used, as before,
    // when the user then asks to CHECK that rule against a real asset.
    // ============================================================================
    public partial class ChatBotController
    {
        // How many (most-specific) cause codes to inject per turn.
        private const int CauseInjectMax = 2;

        // Build a CAUSE DEFINITIONS block for any cause code named in the latest user turn.
        // Returns "" when no cause code is recognised (no regression to other questions).
        private static string BuildCauseConditionRule(List<JObject> messages)
        {
            try
            {
                string userText = LastUserTextForCause(messages);
                if (string.IsNullOrWhiteSpace(userText)) { return ""; }

                List<CauseDef> hits = MatchCauses(userText);
                if (hits.Count == 0) { return ""; }

                StringBuilder sb = new StringBuilder();
                sb.Append("\n\n=== CAUSE DEFINITIONS (authoritative -- from the RDPMS cause catalog) ===\n");
                sb.Append("These are the exact trigger conditions the system evaluates for the cause code(s) ");
                sb.Append("the user named. Answer any trigger-condition / threshold / rule / \"when does it ");
                sb.Append("fire\" question from THIS block -- do NOT say you cannot find it, and do NOT invent ");
                sb.Append("a number. A threshold written as a formula (avg*1.20, MinFail, MinSafe, 2xavg, ...) ");
                sb.Append("is resolved per asset at runtime; explain it as written and name the attribute it ");
                sb.Append("applies to. 'for Ns' is the sustain window the condition must hold. If the user then ");
                sb.Append("asks to CHECK the cause against a specific asset, use the live/history data tools as ");
                sb.Append("usual -- this block is the rule, the tools provide the readings.\n\n");
                for (int i = 0; i < hits.Count; i++) { sb.Append(FormatCauseDef(hits[i])); }
                sb.Append("=== END CAUSE DEFINITIONS ===");
                return sb.ToString();
            }
            catch { return ""; }
        }

        // Render one CauseDef as compact, readable doctrine.
        private static string FormatCauseDef(CauseDef d)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("CAUSE: ").Append(d.Cause)
              .Append("  (").Append(d.AlertType).Append(", FRS #").Append(d.Seq).Append(")\n");
            if (!string.IsNullOrEmpty(d.RuleRaw)) { sb.Append("  Trigger rule: ").Append(d.RuleRaw).Append("\n"); }
            sb.Append("  Sustain window: ").Append(d.SustainSec).Append(" s")
              .Append(d.SustainSec == 0 ? " (instantaneous check)\n" : "\n");
            if (d.Attrs != null && d.Attrs.Length > 0)
            {
                sb.Append("  Conditions:\n");
                for (int i = 0; i < d.Attrs.Length; i++)
                {
                    CauseAttr a = d.Attrs[i];
                    if (a == null) { continue; }
                    sb.Append("    - ").Append(a.Name);
                    if (!string.IsNullOrEmpty(a.State)) { sb.Append(" ").Append(a.State); }
                    if (!string.IsNullOrEmpty(a.Cmp)) { sb.Append(" ").Append(a.Cmp); }
                    string thr = !string.IsNullOrEmpty(a.EffectiveThreshold) ? a.EffectiveThreshold : a.ThresholdRef;
                    if (!string.IsNullOrEmpty(thr)) { sb.Append(" ").Append(thr); }
                    List<string> tags = new List<string>();
                    if (!string.IsNullOrEmpty(a.Datatype)) { tags.Add(a.Datatype); }
                    if (!string.IsNullOrEmpty(a.Class)) { tags.Add(a.Class); }
                    if (!string.IsNullOrEmpty(a.Gate)) { tags.Add("gate:" + a.Gate); }
                    if (tags.Count > 0) { sb.Append("  [").Append(string.Join(", ", tags)).Append("]"); }
                    sb.Append("\n");
                }
            }
            if (!string.IsNullOrEmpty(d.VerdictLogic)) { sb.Append("  Confirmed when: ").Append(d.VerdictLogic).Append("\n"); }
            sb.Append("\n");
            return sb.ToString();
        }

        private static readonly Regex _causeNonChar = new Regex(@"[^A-Z0-9 ]+", RegexOptions.Compiled);
        private static readonly Regex _causeMultiSpace = new Regex(@"\s+", RegexOptions.Compiled);

        // Normalise cause text for matching: uppercase, '/' and '-' -> space, strip other punctuation,
        // collapse whitespace. "tc tfc i/p volt low" and "TC TFC I P VOLT LOW" both -> "TC TFC I P VOLT LOW".
        private static string NormCause(string s)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            string up = s.ToUpperInvariant().Replace('/', ' ').Replace('-', ' ');
            up = _causeNonChar.Replace(up, " ");
            return _causeMultiSpace.Replace(up, " ").Trim();
        }

        // Find cause codes present in the user text as a space-delimited run (word boundary, so
        // "TC SHORTAGE" does not match "TC SHORT"). Keep only the most specific when one code is a
        // substring of another ("TC GJ SHORT" wins over "TC SHORT"); cap to CauseInjectMax.
        private static List<CauseDef> MatchCauses(string userText)
        {
            List<CauseDef> outp = new List<CauseDef>();
            string u = NormCause(userText);
            if (u.Length == 0) { return outp; }
            string uPad = " " + u + " ";

            List<CauseDef> found = new List<CauseDef>();
            foreach (CauseDef d in CauseCatalog.All)
            {
                if (d == null || string.IsNullOrEmpty(d.Cause)) { continue; }
                string c = NormCause(d.Cause);
                if (c.Length < 4) { continue; }
                if (uPad.IndexOf(" " + c + " ", StringComparison.Ordinal) >= 0) { found.Add(d); }
            }
            if (found.Count == 0) { return outp; }

            foreach (CauseDef d in found)
            {
                string c = NormCause(d.Cause);
                bool subsumed = false;
                foreach (CauseDef e in found)
                {
                    if (ReferenceEquals(d, e)) { continue; }
                    string ec = NormCause(e.Cause);
                    if (ec.Length > c.Length && ec.IndexOf(c, StringComparison.Ordinal) >= 0) { subsumed = true; break; }
                }
                if (!subsumed && !outp.Contains(d)) { outp.Add(d); }
            }
            return outp.OrderByDescending(x => NormCause(x.Cause).Length).Take(CauseInjectMax).ToList();
        }

        // Latest REAL user turn (tool_result turns are not user input).
        private static string LastUserTextForCause(List<JObject> messages)
        {
            if (messages == null) { return ""; }
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                JObject m = messages[i];
                if (m == null || m["role"] == null || m["role"].ToString() != "user") { continue; }
                JToken c = m["content"];
                if (c == null) { continue; }
                if (c.Type == JTokenType.String) { return c.ToString(); }
                JArray arr = c as JArray;
                if (arr == null) { continue; }
                if (arr.Count > 0 && arr[0]["type"] != null && arr[0]["type"].ToString() == "tool_result") { continue; }
                StringBuilder sb = new StringBuilder();
                foreach (JToken b in arr)
                {
                    if (b != null && b["type"] != null && b["type"].ToString() == "text" && b["text"] != null)
                    {
                        if (sb.Length > 0) { sb.Append(' '); }
                        sb.Append(b["text"].ToString());
                    }
                }
                return sb.ToString();
            }
            return "";
        }
    }
}
