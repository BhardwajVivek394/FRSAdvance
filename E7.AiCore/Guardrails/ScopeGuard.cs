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
    // Prompt-extraction and out-of-scope checks; last real user text.
    // Moved verbatim from AiChatController (E7MRIWeb) in 1.0.162.0 -- Shared AI Core step 2a-2c.
    public static class ScopeGuard
    {

        // v1.0.115.0: keep only clearly-unrelated topics. Removed keywords that mis-fired on
        // legitimate railway follow-ups: "translate" (users ask for Hindi), "weather" (relevant to
        // track/ballast condition and a feature in progress), "email"/"math"/"news"/"time in"
        // (substring-matched innocent words like "email the report", "estimate", "renews"). The
        // remaining list is genuinely off-domain; even these now get a SOFT redirect, not a canned
        // "not trained" line, and the alert context is preserved for the next question.
        internal static readonly string[] OutOfScope = new string[]
        {
            "recipe","cook","poem","write a joke","tell me a joke","tell a joke",
            "capital of","python error","2+2","bitcoin price","stock price"
        };


        // Tightened so ordinary railway language cannot trigger a refusal: the ambiguous nouns
        // (system / configuration / rules / guidelines) require the POSSESSIVE "your", because
        // "show me the system voltage at 03BT" and "show the configuration of point machine 04AT"
        // are legitimate questions. Only unambiguous internal nouns pair with "the".
        internal static readonly Regex _extractionRx = new Regex(
            @"\b(?:system\s+prompt|"
          + @"your\s+(?:instructions|rules|system\s+message|prompt|configuration|config|training\s+text|guidelines|directives)|"
          + @"initial\s+(?:instructions|prompt)|repeat\s+(?:everything|the\s+text)\s+above|"
          + @"ignore\s+(?:all\s+)?(?:previous|prior|above)\s+instructions|"
          + @"(?:print|show|reveal|display|output|dump|reproduce|repeat|list)\s+(?:me\s+)?(?:your|the)\s+"
          + @"(?:system\s+prompt|initial\s+prompt|prompt\s+text|instructions)|"
          + @"what\s+(?:are|were)\s+you\s+(?:told|instructed|programmed)|"
          + @"verbatim\s+(?:prompt|instructions))\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);


        public static bool IsInstructionExtractionRequest(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            return _extractionRx.IsMatch(text);
        }


        // Latest REAL user turn. Tool-result turns are not user input, and the serialized alert
        // context is never passed here -- an alert description must not trigger a refusal.
        public static string LastRealUserText(JArray messages)
        {
            if (messages == null) { return string.Empty; }
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                JObject m = messages[i] as JObject;
                if (m == null || m["role"] == null || m["role"].ToString() != "user") { continue; }
                JToken c = m["content"];
                if (c == null) { continue; }
                if (c.Type == JTokenType.String) { return c.ToString(); }
                JArray arr = c as JArray;
                if (arr == null) { continue; }
                if (arr.Count > 0 && arr[0]["type"] != null && arr[0]["type"].ToString() == "tool_result") { continue; }
                StringBuilder tb = new StringBuilder();
                for (int b = 0; b < arr.Count; b++)
                {
                    JToken blk = arr[b];
                    if (blk != null && blk["type"] != null && blk["type"].ToString() == "text" && blk["text"] != null)
                    {
                        if (tb.Length > 0) { tb.Append(' '); }
                        tb.Append(blk["text"].ToString());
                    }
                }
                return tb.ToString();
            }
            return string.Empty;
        }


        public static bool IsOutOfScope(string text)
        {
            return IsOutOfScope(text, OutOfScope);   // 1.0.167.0: AiChat's list; logic shared with ChatBot
        }

        // 1.0.167.0: shared by AiChat and ChatBot -- each product passes its own out-of-scope list.
        public static bool IsOutOfScope(string text, string[] terms)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < terms.Length; i++)
                if (lower.Contains(terms[i])) return true;
            return false;
        }
    }
}
