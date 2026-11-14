using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeUtil -- JSON and text helpers shared by every Judge file.
    //
    // Ported from the System.Text.Json helpers of AlertJudge v1.1.2.0 (CardParser.Str/Int/Ts/TryNum,
    // EvidenceFetcher.ParseTs/ParseNum/TryLong/TryInt). Same keys, same fallback order. One deliberate
    // difference: where System.Text.Json THREW on a wrong token kind (TryGetInt64 on a string), these
    // return "not found" instead, so a malformed card or MCP row can never turn into an HTTP 500.
    // ============================================================================================
    internal static class JudgeUtil
    {
        internal static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

        /// <summary>
        /// Parses JSON WITHOUT Newtonsoft's automatic date conversion. A card time such as
        /// "2026-09-28T15:53:56" must stay a string so the card parser can treat it as IST.
        /// </summary>
        internal static JToken Parse(string json)
        {
            using (StringReader sr = new StringReader(json))
            using (JsonTextReader reader = new JsonTextReader(sr))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                JToken token = JToken.ReadFrom(reader);
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment)
                    {
                        throw new JsonReaderException("additional content after the JSON value");
                    }
                }

                return token;
            }
        }

        internal static bool IsObject(JToken t)
        {
            return t != null && t.Type == JTokenType.Object;
        }

        internal static bool IsArray(JToken t)
        {
            return t != null && t.Type == JTokenType.Array;
        }

        internal static JToken Prop(JToken o, string key)
        {
            JObject obj = o as JObject;
            if (obj == null)
            {
                return null;
            }

            JToken v;
            if (obj.TryGetValue(key, StringComparison.Ordinal, out v))
            {
                return v;
            }

            return null;
        }

        /// <summary>First key holding a string (or a number, as its raw text). Same as CardParser.Str.</summary>
        internal static string Str(JToken o, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken v = Prop(o, k);
                if (v == null)
                {
                    continue;
                }

                if (v.Type == JTokenType.String)
                {
                    return (string)v;
                }

                if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                {
                    return v.ToString(Formatting.None);
                }
            }

            return null;
        }

        /// <summary>First key holding an int (number or numeric string). Same as CardParser.Int.</summary>
        internal static int? Int(JToken o, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken v = Prop(o, k);
                if (v == null)
                {
                    continue;
                }

                int i;
                if (TryInt32(v, out i))
                {
                    return i;
                }

                if (v.Type == JTokenType.String && int.TryParse((string)v, out i))
                {
                    return i;
                }
            }

            return null;
        }

        internal static bool TryInt32(JToken v, out int i)
        {
            i = 0;
            if (v == null || v.Type != JTokenType.Integer)
            {
                return false;
            }

            try
            {
                long l = v.Value<long>();
                if (l < int.MinValue || l > int.MaxValue)
                {
                    return false;
                }

                i = (int)l;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryInt64(JToken v, out long l)
        {
            l = 0;
            if (v == null || v.Type != JTokenType.Integer)
            {
                return false;
            }

            try
            {
                l = v.Value<long>();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Number, or a numeric string (invariant culture). Same as CardParser.TryNum.</summary>
        internal static bool TryNum(JToken v, out double d)
        {
            d = 0;
            if (v == null)
            {
                return false;
            }

            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
            {
                d = v.Value<double>();
                return true;
            }

            if (v.Type == JTokenType.String)
            {
                return double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            }

            return false;
        }

        /// <summary>Card / request time. Same formats and order as CardParser.Ts.</summary>
        internal static DateTimeOffset? Ts(JToken o, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken v = Prop(o, k);
                if (v == null)
                {
                    continue;
                }

                if (v.Type == JTokenType.String)
                {
                    DateTimeOffset? parsed = ParseCardTime((string)v ?? "");
                    if (parsed != null)
                    {
                        return parsed;
                    }
                }

                long ms;
                if (TryInt64(v, out ms))
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(ms > 1e12 ? ms : ms * 1000);
                }
            }

            return null;
        }

        internal static DateTimeOffset? ParseCardTime(string str)
        {
            DateTime ist;

            // web card: "15:53:56/28.09.2026" (IST, no offset)
            if (DateTime.TryParseExact(str, "HH:mm:ss/dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out ist))
            {
                return new DateTimeOffset(ist, Ist);
            }

            // ProcessJson SetTime "2026-09-28T15:53:56" (IST, no offset) -- naive is IST, not UTC
            DateTime naive;
            if (DateTime.TryParseExact(str, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out naive))
            {
                return new DateTimeOffset(naive, Ist);
            }

            DateTimeOffset d;
            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
            {
                return d;
            }

            return null;
        }

        /// <summary>History row time. Same as EvidenceFetcher.ParseTs.</summary>
        internal static DateTimeOffset? ParseRowTs(JToken v)
        {
            if (v == null)
            {
                return null;
            }

            DateTimeOffset d;
            if (v.Type == JTokenType.String && DateTimeOffset.TryParse((string)v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d))
            {
                return d;
            }

            long n;
            if (TryInt64(v, out n))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(n > 1e12 ? n : n * 1000);
            }

            return null;
        }

        /// <summary>History row value. Same as EvidenceFetcher.ParseNum.</summary>
        internal static double? ParseRowNum(JToken v)
        {
            if (v == null)
            {
                return null;
            }

            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
            {
                return v.Value<double>();
            }

            double d;
            if (v.Type == JTokenType.String && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }

            if (v.Type == JTokenType.Boolean)
            {
                return v.Value<bool>() ? 1 : 0;
            }

            return null;
        }

        internal static bool TryLong(JToken e, out long id, params string[] keys)
        {
            id = 0;
            foreach (string k in keys)
            {
                JToken v = Prop(e, k);
                if (v == null)
                {
                    continue;
                }

                if (TryInt64(v, out id))
                {
                    return true;
                }

                if (v.Type == JTokenType.String && long.TryParse((string)v, out id))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool TryInt(JToken e, out int id, params string[] keys)
        {
            id = 0;
            foreach (string k in keys)
            {
                JToken v = Prop(e, k);
                if (v == null)
                {
                    continue;
                }

                if (TryInt32(v, out id))
                {
                    return true;
                }

                if (v.Type == JTokenType.String && int.TryParse((string)v, out id))
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------------------------------
        // Text
        // ---------------------------------------------------------------------------------------

        /// <summary>Same as CatalogVerifier.Norm: upper case, every run of non [A-Z0-9/] becomes one space.</summary>
        internal static string Norm(string s)
        {
            return Regex.Replace((s ?? "").ToUpperInvariant(), @"[^A-Z0-9/]+", " ").Trim();
        }

        /// <summary>"ITC RELAY END(mA)" -> "ITC RELAY END". Same as CardParser.StripUnit.</summary>
        internal static string StripUnit(string label)
        {
            int i = label.IndexOf('(');
            if (i > 0)
            {
                return label.Substring(0, i).Trim();
            }

            return label;
        }

        /// <summary>Same as CardParser.RelayBit.</summary>
        internal static int RelayBit(string state)
        {
            string u = (state ?? "").ToUpperInvariant();
            if (u.Contains("PICK") || u.Contains("UP") || u == "1" || u == "TRUE")
            {
                return 1;
            }

            return 0;
        }

        /// <summary>Same as CardParser.IsRelayName.</summary>
        internal static bool IsRelayName(string n)
        {
            return n.EndsWith("KR", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("CR", StringComparison.OrdinalIgnoreCase)
                || n.EndsWith("PR", StringComparison.OrdinalIgnoreCase)
                || n.Equals("HR", StringComparison.OrdinalIgnoreCase)
                || n.Equals("DR", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"&lt;=", "≤" -> "&lt;"; "&gt;=", "≥" -> "&gt;". Same as CatalogVerifier.SameDirection's N().</summary>
        internal static string Direction(string c)
        {
            string t = (c ?? "").Trim();
            if (t == "<" || t == "<=" || t == "≤")
            {
                return "<";
            }

            if (t == ">" || t == ">=" || t == "≥")
            {
                return ">";
            }

            return t;
        }

        // =========================================================================================
        // 1.0.190.0 -- ONE EFFECTIVE THRESHOLD, ONE DIRECTION. The owner's rule, which is also the
        // catalogue's own EffectiveThreshold form:
        //
        //     alert fired on Avg & MinSafe (a LOW rule)   ->  T = MAX(avg*LD, MinSafe),  breach v < T
        //     alert fired on Avg & MaxSafe (a HIGH rule)  ->  T = MIN(avg*LC, MaxSafe),  breach v > T
        //
        // catalogue #12  EffectiveThreshold="MAX(avg*0.80, MinSafe)"   LowerRule=true
        // catalogue #13  EffectiveThreshold="MIN(avg*1.20, MaxSafe)"   LowerRule=false
        //
        // WHY THIS EXISTS. Three places asked "does this value breach?" and two of them did it by
        // OR-ing the card's threshold clauses together instead of collapsing them to one number:
        //
        //   JudgeVerifier.ResolveThreshold   reads the catalogue's MAX()/MIN() form   CORRECT
        //   JudgeLiveCheck                   rules.Any(...) over the FAIL thresholds  right answer, no single number
        //   JudgeResetCheck                  rules.Any(...) over ALL thresholds       WRONG
        //
        // The reset check's version mixed DIRECTIONS. On 1023426 the card carried
        // "80% Avg 269.30 FAIL (<)" and "Min Safe 210 PASS (>=)", so its rule became
        // "< 269.3 OR >= 210" -- and the healthy reset value 273.68 satisfies ">= 210", so a
        // recovered alert was reported CONFIRMED_PERSISTS, "both breaching ... had not recovered".
        // The AI was then reading a sentence that contradicted the numbers printed beside it.
        //
        // And because the AI got the raw clause list rather than one number, it picked the wrong
        // half: on 1137362 it called 217.8 mA a recovery when the effective threshold was 261.26
        // (217.8 < 261.26 is still a breach), and on 1023426 it called 269.0006 a recovery against
        // 269.3. Both claims are arithmetically false, and both deleted a true alert.
        //
        // ONLY THE FAIL THRESHOLDS COUNT, and that is not a narrowing: for a low rule
        // MAX(all) == MAX(the ones that failed), because if the highest threshold had not failed
        // the value would sit above every one of them and no alert would have fired. Same in
        // reverse for MIN on a high rule. So this is the card's own rule, collapsed.
        //
        // Returns false when the card carries no usable threshold, or when the FAIL thresholds
        // point in BOTH directions (a band -- no single number can express it). Callers fall back
        // to their old Any(...) behaviour there, so nothing silently changes shape.
        // =========================================================================================
        internal static bool EffectiveThreshold(List<JudgeCardThreshold> all, out double threshold,
            out string direction, out string ruleText)
        {
            threshold = 0;
            direction = null;
            ruleText = null;
            if (all == null || all.Count == 0)
            {
                return false;
            }

            List<JudgeCardThreshold> fired = all
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Comparison)
                         && string.Equals(t.Status, "FAIL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // no FAIL marks at all (an older card, or a derived condition) -- use every threshold
            // that has an operator, exactly as the live re-check does in that case.
            if (fired.Count == 0)
            {
                fired = all.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Comparison)).ToList();
            }

            if (fired.Count == 0)
            {
                return false;
            }

            bool anyLow = fired.Any(t => Direction(t.Comparison) == "<");
            bool anyHigh = fired.Any(t => Direction(t.Comparison) == ">");
            if (anyLow == anyHigh)
            {
                return false;   // both, or neither -- not expressible as one number
            }

            JudgeCardThreshold pick = anyLow
                ? fired.OrderByDescending(t => t.Value).First()    // LOW  -> MAX
                : fired.OrderBy(t => t.Value).First();             // HIGH -> MIN

            threshold = pick.Value;
            direction = anyLow ? "<" : ">";
            ruleText = direction + " " + F(pick.Value, "0.###") + " (" + pick.Label + ")"
                + (fired.Count > 1
                    ? " = " + (anyLow ? "MAX" : "MIN") + " of "
                      + string.Join(", ", fired.Select(t => t.Label + " " + F(t.Value, "0.###")).ToArray())
                    : "");
            return true;
        }

        /// <summary>Same as CatalogVerifier.Cmp, plus the card's "≤" / "≥" spellings.</summary>
        internal static bool Cmp(string cmp, double obs, double thr)
        {
            string c = (cmp ?? "").Trim();
            if (c == "≤")
            {
                c = "<=";
            }
            else if (c == "≥")
            {
                c = ">=";
            }

            switch (c)
            {
                case "<":
                    return obs < thr;
                case "<=":
                    return obs <= thr;
                case ">":
                    return obs > thr;
                case ">=":
                    return obs >= thr;
                case "==":
                    return Math.Abs(obs - thr) < 1e-9;
                case "+":
                    return obs > thr;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Formatting (invariant culture; null prints as "", like C# interpolation of a null)
        // ---------------------------------------------------------------------------------------

        internal static string F(double? v, string format)
        {
            if (v == null)
            {
                return "";
            }

            return v.Value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>A double printed the way .NET Core prints it (shortest round-trip).</summary>
        internal static string R(double? v)
        {
            if (v == null)
            {
                return "";
            }

            return v.Value.ToString("R", CultureInfo.InvariantCulture);
        }

        internal static string T(DateTimeOffset? t, string format)
        {
            if (t == null)
            {
                return "";
            }

            return t.Value.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>ISO time as System.Text.Json writes a DateTimeOffset.</summary>
        internal static string Iso(DateTimeOffset t)
        {
            return t.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
        }

        internal static JToken IsoOrNull(DateTimeOffset? t)
        {
            if (t == null)
            {
                return JValue.CreateNull();
            }

            return Iso(t.Value);
        }

        internal static JToken NumOrNull(double? v)
        {
            if (v == null)
            {
                return JValue.CreateNull();
            }

            return new JValue(v.Value);
        }

        internal static string Trunc(string s, int max)
        {
            if (s == null)
            {
                return "";
            }

            if (s.Length <= max)
            {
                return s;
            }

            return s.Substring(0, max) + "…";
        }

        /// <summary>
        /// Makes Newtonsoft output match what System.Text.Json wrote in AlertJudge 1.1.2.0: null properties
        /// are removed (WhenWritingNull) and whole-number doubles are written without ".0" (21600, not 21600.0).
        /// Applied to the AI input pack so the model sees byte-identical JSON.
        /// </summary>
        internal static void Tidy(JToken t)
        {
            if (t == null)
            {
                return;
            }

            if (t.Type == JTokenType.Object)
            {
                foreach (JProperty p in ((JObject)t).Properties().ToList())
                {
                    if (IsNullValue(p.Value))
                    {
                        p.Remove();
                    }
                    else
                    {
                        Tidy(p.Value);
                    }
                }
            }
            else if (t.Type == JTokenType.Array)
            {
                JArray a = (JArray)t;
                for (int i = 0; i < a.Count; i++)
                {
                    JToken child = a[i];
                    if (child.Type == JTokenType.Float)
                    {
                        a[i] = TidyNumber((JValue)child);
                    }
                    else
                    {
                        Tidy(child);
                    }
                }
            }
            else if (t.Type == JTokenType.Float && t.Parent is JProperty)
            {
                ((JProperty)t.Parent).Value = TidyNumber((JValue)t);
            }
        }

        /// <summary>A JSON null, or a JValue holding a C# null (Newtonsoft types `obj["k"] = (string)null` as String).</summary>
        internal static bool IsNullValue(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                return true;
            }

            JValue v = t as JValue;
            return v != null && v.Value == null;
        }

        private static JToken TidyNumber(JValue v)
        {
            double d = v.Value<double>();
            if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Abs(d) < 9007199254740992.0 && d == Math.Floor(d))
            {
                return new JValue((long)d);
            }

            return v;
        }

        internal static string MapDecision(string configured)
        {
            if (string.Equals((configured ?? "").Trim(), "SUPPRESSED", StringComparison.OrdinalIgnoreCase))
            {
                return "SUPPRESSED";
            }

            return "PASS";
        }

        /// <summary>
        /// 1.0.192.3 -- renders the AI's tri-state answers for the operator's reason text. The
        /// distinction matters: "no" is a finding against the alert, "not answered" is an
        /// abstention, and before 1.0.192.3 the policy treated the second as the first. Spelling
        /// it out in words is what makes an overruled abstention auditable afterwards.
        /// </summary>
        internal static string TriText(bool? v)
        {
            if (v == null)
            {
                return "not answered";
            }

            return v.Value ? "yes" : "no";
        }
    }
}
