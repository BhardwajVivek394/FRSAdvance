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


        // 1.0.173.0 -- PLUMBING questions. _extractionRx above covers "show me your system prompt";
        // it does NOT cover "which table are you using?", and the model answered that one honestly:
        // it named the tool, said it had no SQL access, listed the fields it reads and the row limit.
        // None of that is the user's business and all of it helps someone probe the system.
        //
        // Deliberately HIGH PRECISION, not high recall. The bare word "tool" is a railway word --
        // "what tool do you use to clean the point machine" is a real maintenance question and must
        // never be refused -- so "tool" only counts in the possessive ("your tools") or list-me
        // shapes. Anything this misses is caught on the way out by ScrubInternalNames, which removes
        // the actual names whatever sentence they arrive in. Detector first, scrubber behind it.
        internal static readonly Regex _internalsRx = new Regex(
            @"(?:"
          + @"\b(?:which|what|whose)\s+(?:table|tables|database|databases|db|schema|sql|query|queries|"
          + @"data\s*source|datasource|data\s*sources)\b[^.?!]{0,30}\byou\b"
          + @"|\b(?:are|do|did)\s+you\s+(?:using|use|have|query|querying|hitting|calling)\b[^.?!]{0,30}"
          + @"\b(?:sql|database|db|table|tables|schema|mcp|api|apis|endpoint|endpoints)\b"
          + @"|\byour\s+(?:table|tables|database|db|schema|sql|backend|back\s*end|stack|architecture|"
          + @"data\s*source|datasource|tool|tools|api|apis|endpoint|endpoints|mcp|server|servers|llm|"
          + @"model\s+name|system\s+design)\b"
          + @"|\b(?:list|show|tell\s+me|name|give\s+me)\s+(?:me\s+)?(?:all\s+)?(?:your|the)\s+"
          + @"(?:tools|tool\s+names|apis|endpoints|tables|functions|data\s*sources)\b"
          + @"|\b(?:how|where)\s+(?:do|does|did)\s+you\s+(?:get|fetch|pull|query|retrieve|access|read)\b"
          + @"[^.?!]{0,25}\b(?:data|this|that|it|them|values|rows)\b"
          + @"|\bdo\s+you\s+have\s+(?:raw\s+|direct\s+)?(?:sql|database|db|table)\s+access\b"
          + @"|\bwhat\s+(?:llm|model|ai|engine)\s+(?:are|is)\s+you\b"
          + @")",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True when the question is about the assistant's own plumbing -- tables, tools, APIs,
        /// endpoints, schema, model -- rather than about railway assets.
        /// </summary>
        public static bool IsInternalsQuestion(string text)
        {
            if (string.IsNullOrEmpty(text)) { return false; }
            return _internalsRx.IsMatch(text);
        }

        /// <summary>The one answer every internals question gets. It names nothing.</summary>
        public const string InternalsRefusal =
            "I am not able to describe how this system is built internally. I can answer from the "
          + "railway data itself -- assets, alerts, tag values, history and thresholds. What would "
          + "you like to look at?";


        // Latest REAL user turn. Tool-result turns are not user input, and the serialized alert
        // context is never passed here -- an alert description must not trigger a refusal.
        /// <summary>
        /// 2.1 -- the last thing the assistant said, so SocialReply can avoid repeating it. Reads
        /// the thread the caller already holds; no session state to keep or lose.
        /// </summary>
        public static string LastAssistantText(JArray messages)
        {
            if (messages == null) { return string.Empty; }
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                JObject m = messages[i] as JObject;
                if (m == null || m["role"] == null || m["role"].ToString() != "assistant") { continue; }
                JToken c = m["content"];
                if (c == null) { continue; }
                if (c.Type == JTokenType.String) { return c.ToString(); }
                JArray arr = c as JArray;
                if (arr == null) { continue; }
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

        // 1.0.172.0: social / identity turns. These are NOT off-domain -- they are chit-chat aimed at
        // the assistant itself ("how are you", "who are you", "thanks"), and before this they ran the
        // whole agent loop and came back with a paragraph of persona that recited the alert summary
        // and the internal site/asset ids. Reported from testing as "unnecessary chatbot responses
        // are being displayed in the Asset Review panel".
        //
        // WHOLE-MESSAGE match, never a substring: the text is trimmed, lower-cased and stripped of
        // trailing punctuation, then compared for equality or a "starts with + nothing else of
        // substance" shape. That is the whole point -- "thanks, now show me the TPR history" is a
        // real question and must NOT be swallowed, while a bare "thanks" should be. A message longer
        // than ShortTurnMaxChars is never social, whatever it contains.
        private const int ShortTurnMaxChars = 40;

        internal static readonly string[] SocialTurns = new string[]
        {
            "hi","hello","hey","hii","helo","namaste","good morning","good afternoon","good evening",
            "how are you","how r u","how are u","hows it going","how is it going","whats up","what's up",
            "who are you","who r u","what are you","what r u","who am i talking to","are you a bot",
            "are you human","are you ai","what is your name","whats your name","what's your name",
            "thanks","thank you","thanx","thx","ty","ok thanks","okay thanks","great thanks",
            "bye","goodbye","good night","see you","ok","okay","hmm","nice","cool","great","good"
        };

        /// <summary>
        /// True when the message is nothing but a greeting, a pleasantry or a question about the
        /// assistant itself. Anything with a real question attached returns false.
        /// </summary>
        public static bool IsSocialTurn(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return false; }

            string t = text.Trim();
            if (t.Length > ShortTurnMaxChars) { return false; }

            t = t.ToLowerInvariant().Trim(' ', '\t', '.', '!', '?', ',', ';', ':', '\u2026');
            if (t.Length == 0) { return false; }

            for (int i = 0; i < SocialTurns.Length; i++)
            {
                if (t == SocialTurns[i]) { return true; }
            }

            return false;
        }

        // =========================================================================================
        // 2.1 -- THE SOCIAL REPLY.
        //
        // The bug this fixes: every social turn produced ONE hardcoded sentence
        // (AiChatController.FollowupScopeRedirect, a const). So "hello" and "how are you?" came back
        // with the same words, character for character, one after the other in the Asset Review
        // panel -- which reads as a broken bot, not a focused one. Reported 09-10-2026.
        //
        // Two things are wrong with one fixed string, and both are fixed here:
        //   1. it does not ANSWER. "thanks" and "who are you" are different things to say, and
        //      replying to a thank-you with an offer of four data options is a non-sequitur.
        //   2. it REPEATS. Even the right sentence twice in a row looks like a stuck process.
        //
        // So: the reply is picked by what the person actually said, the offer line varies, and the
        // caller passes the previous reply so the identical text is never returned twice running.
        // =========================================================================================

        private static readonly string[] Greetings =
        {
            "hi", "hello", "hey", "hii", "helo", "namaste",
            "good morning", "good afternoon", "good evening"
        };

        private static readonly string[] Thanks =
        {
            "thanks", "thank you", "thanx", "thx", "ty",
            "ok thanks", "okay thanks", "great thanks"
        };

        private static readonly string[] Farewells =
        {
            "bye", "goodbye", "good night", "see you"
        };

        private static readonly string[] AboutMe =
        {
            "who are you", "who r u", "what are you", "what r u", "who am i talking to",
            "are you a bot", "are you human", "are you ai",
            "what is your name", "whats your name", "what's your name"
        };

        private static readonly string[] HowAreYou =
        {
            "how are you", "how r u", "how are u", "hows it going", "how is it going",
            "whats up", "what's up"
        };

        /// <summary>The offers, varied so a second social turn does not echo the first.</summary>
        private static readonly string[] Offers =
        {
            "I can pull the alert history, the current tag values, the thresholds, or the track-health analysis.",
            "Ask me for this alert's history, its live readings, the thresholds it breached, or how the asset has been trending.",
            "Say the word and I will fetch the readings behind this alert, its thresholds, or what else this asset has raised recently."
        };

        /// <summary>
        /// 2.1 -- a reply to a social turn that fits what was said and does not repeat itself.
        /// <paramref name="previous"/> is the last social reply sent in this conversation (null on
        /// the first); when the chosen wording matches it, the next variant is used instead.
        /// Callers that cannot track the previous reply may pass null -- the reply is still
        /// appropriate to the message, it just loses the no-repeat guarantee.
        /// </summary>
        public static string SocialReply(string text, string previous)
        {
            string t = (text ?? "").Trim().ToLowerInvariant()
                .Trim(' ', '\t', '.', '!', '?', ',', ';', ':', '\u2026');

            string lead;
            if (In(t, Thanks))
            {
                lead = "You are welcome.";
            }
            else if (In(t, Farewells))
            {
                // Nothing to offer on a goodbye -- offering more is how a bot ignores you.
                return "Goodbye. I will be here when you need this alert looked at again.";
            }
            else if (In(t, AboutMe))
            {
                lead = "I am the RDPMS assistant for this alert -- I read the telemetry behind it and explain what it shows.";
            }
            else if (In(t, HowAreYou))
            {
                lead = "Running fine, thank you.";
            }
            else if (In(t, Greetings))
            {
                lead = Greeting(t);
            }
            else
            {
                // "ok", "hmm", "nice" and the rest: acknowledge without pretending it was a question.
                lead = "Right.";
            }

            // Pick an offer that is not the one already on screen.
            for (int i = 0; i < Offers.Length; i++)
            {
                string candidate = lead + " " + Offers[i];
                if (!string.Equals(candidate, previous, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return lead + " " + Offers[0];
        }

        private static string Greeting(string t)
        {
            if (t == "namaste") { return "Namaste."; }
            if (t.StartsWith("good ", StringComparison.Ordinal))
            {
                return char.ToUpperInvariant(t[0]) + t.Substring(1) + ".";
            }

            return "Hello.";
        }

        private static bool In(string t, string[] set)
        {
            for (int i = 0; i < set.Length; i++)
            {
                if (t == set[i]) { return true; }
            }

            return false;
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
