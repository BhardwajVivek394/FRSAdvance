using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeCaseBase (1.0.202.0) -- past decided alerts as CASES, and "which past cases does this
    // card look like?".
    //
    // THIS FILE IS COMPILED INTO TWO ASSEMBLIES and must stay self-contained (Newtonsoft only, no
    // other Judge type):
    //   - E7.AiCore                 the Judge reads judge-cases.json and matches a live card
    //   - E7.AlertCaseExtractor     the 02:00 job builds judge-cases.json from decided alerts
    //                               (linked in its csproj: ..\..\E7.AiCore\Judge\JudgeCaseBase.cs)
    // Because both sides run the SAME FromCard, a stored case and a live alert are described by
    // exactly the same features. Never copy this logic anywhere else.
    //
    // Only the alert CARD is used (triggeredConditions + enablingConditions), because the card is
    // the one thing both sides have: AlertV3 stores it in PendingAlertApproval and sends the same
    // JSON to the Judge.
    // ============================================================================================

    /// <summary>What a card says, as numbers and words. Keys: v:NAME measured, t:NAME threshold, e:NAME enabling, margin, fails, trig, cmp.</summary>
    internal sealed class JudgeCaseFeatures
    {
        public Dictionary<string, double> Num = new Dictionary<string, double>(StringComparer.Ordinal);
        public Dictionary<string, string> Cat = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static JudgeCaseFeatures FromCard(JObject card)
        {
            JudgeCaseFeatures f = new JudgeCaseFeatures();
            if (card == null)
            {
                return f;
            }

            int fails = 0;
            bool firstFail = true;
            JArray trig = Arr(card, "triggeredConditions");
            if (trig != null)
            {
                foreach (JObject tc in trig.OfType<JObject>())
                {
                    string name = Key(Text(tc, "paramName") ?? Text(tc, "paramLabel"));
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    name = Unique(f, "v:", name);
                    JToken measured = Prop(tc, "measured");
                    double value;
                    bool hasValue = TryNum(measured, out value);
                    if (hasValue)
                    {
                        f.Num["v:" + name] = value;
                    }
                    else
                    {
                        string word = FirstWord(measured);
                        if (word.Length > 0)
                        {
                            f.Cat["v:" + name] = word;
                        }
                    }

                    JObject chosen = null;
                    bool failed = false;
                    JArray ths = Arr(tc, "thresholds");
                    if (ths != null)
                    {
                        chosen = ths.OfType<JObject>().FirstOrDefault(t => string.Equals(Text(t, "status"), "FAIL", StringComparison.OrdinalIgnoreCase));
                        failed = chosen != null;
                        if (chosen == null)
                        {
                            chosen = ths.OfType<JObject>().FirstOrDefault();
                        }
                    }

                    if (failed)
                    {
                        fails++;
                    }

                    if (chosen == null)
                    {
                        continue;
                    }

                    double threshold;
                    bool hasThreshold = TryNum(Prop(chosen, "value"), out threshold);
                    if (hasThreshold)
                    {
                        f.Num["t:" + name] = threshold;
                    }

                    if (failed && firstFail)
                    {
                        firstFail = false;
                        string cmp = (Text(chosen, "comparison") ?? "").Trim();
                        f.Cat["trig"] = name;
                        if (cmp.Length > 0)
                        {
                            f.Cat["cmp"] = IsLower(cmp) ? "<" : ">";
                        }

                        if (hasValue && hasThreshold && threshold != 0 && cmp.Length > 0)
                        {
                            double pct = IsLower(cmp) ? (threshold - value) / Math.Abs(threshold) * 100.0 : (value - threshold) / Math.Abs(threshold) * 100.0;
                            f.Num["margin"] = Math.Round(pct, 2);
                        }
                    }
                }

                f.Num["fails"] = fails;
            }

            JArray en = Arr(card, "enablingConditions");
            if (en != null)
            {
                foreach (JObject e in en.OfType<JObject>())
                {
                    string name = Key(Text(e, "label") ?? Text(e, "paramLabel"));
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    name = Unique(f, "e:", name);
                    JToken v = Prop(e, "value") ?? Prop(e, "measured");
                    double value;
                    if (TryNum(v, out value))
                    {
                        f.Num["e:" + name] = value;
                    }
                    else
                    {
                        string word = FirstWord(v);
                        if (word.Length > 0)
                        {
                            f.Cat["e:" + name] = word;
                        }
                    }
                }
            }

            return f;
        }

        internal JObject ToJson()
        {
            JObject n = new JObject();
            foreach (KeyValuePair<string, double> kv in Num.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                n[kv.Key] = kv.Value;
            }

            JObject c = new JObject();
            foreach (KeyValuePair<string, string> kv in Cat.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                c[kv.Key] = kv.Value;
            }

            JObject o = new JObject();
            o["n"] = n;
            o["c"] = c;
            return o;
        }

        internal static JudgeCaseFeatures FromJson(JObject o)
        {
            JudgeCaseFeatures f = new JudgeCaseFeatures();
            JObject n = o != null ? o["n"] as JObject : null;
            if (n != null)
            {
                foreach (JProperty p in n.Properties())
                {
                    if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Float)
                    {
                        f.Num[p.Name] = p.Value.Value<double>();
                    }
                }
            }

            JObject c = o != null ? o["c"] as JObject : null;
            if (c != null)
            {
                foreach (JProperty p in c.Properties())
                {
                    if (p.Value.Type == JTokenType.String)
                    {
                        f.Cat[p.Name] = (string)p.Value;
                    }
                }
            }

            return f;
        }

        // ---- helpers -------------------------------------------------------------------------

        private static JToken Prop(JObject o, string name)
        {
            return o != null ? o.GetValue(name, StringComparison.OrdinalIgnoreCase) : null;
        }

        private static JArray Arr(JObject o, string name)
        {
            return Prop(o, name) as JArray;
        }

        private static string Text(JObject o, string name)
        {
            JToken t = Prop(o, name);
            if (t == null || t.Type == JTokenType.Null || t is JContainer)
            {
                return null;
            }

            string s = t.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        /// <summary>Upper case, trimmed, inner whitespace collapsed -- so "Ir  mA" and "IR MA" are one key.</summary>
        private static string Key(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return "";
            }

            return string.Join(" ", s.Trim().ToUpperInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Unique(JudgeCaseFeatures f, string prefix, string name)
        {
            string candidate = name;
            int n = 2;
            while (f.Num.ContainsKey(prefix + candidate) || f.Cat.ContainsKey(prefix + candidate))
            {
                candidate = name + "#" + n.ToString(CultureInfo.InvariantCulture);
                n++;
            }

            return candidate;
        }

        private static string FirstWord(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null || t is JContainer)
            {
                return "";
            }

            string s = t.Type == JTokenType.Boolean ? (t.Value<bool>() ? "TRUE" : "FALSE") : t.ToString();
            string[] parts = s.Trim().ToUpperInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : "";
        }

        /// <summary>A JSON number, or a string that is or starts with a number ("148.61", "148.61 mA"). "DROP" is not a number.</summary>
        private static bool TryNum(JToken t, out double v)
        {
            v = 0;
            if (t == null)
            {
                return false;
            }

            if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float)
            {
                v = t.Value<double>();
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }

            if (t.Type != JTokenType.String)
            {
                return false;
            }

            string s = ((string)t ?? "").Trim();
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }

            Match m = Regex.Match(s, @"^[-+]?\d+(\.\d+)?");
            return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static bool IsLower(string comparison)
        {
            string c = (comparison ?? "").Trim();
            return c.StartsWith("<", StringComparison.Ordinal) || (c.Length > 0 && c[0] == (char)0x2264);
        }
    }

    /// <summary>One decided alert: who it was, what a person decided, and what its card said.</summary>
    internal sealed class JudgeCase
    {
        public long Id;
        public int Asset;
        public bool Pass;

        /// <summary>Discard reason group from the extractor (NOT_SENSITIVE, MAINTENANCE, TESTING, DUPLICATE, ...); empty for an approval.</summary>
        public string Reason = "";

        /// <summary>The asset was inside a maintenance window at the set time.</summary>
        public bool Maint;

        public DateTime Set;
        public DateTime Decided;
        public JudgeCaseFeatures F = new JudgeCaseFeatures();

        /// <summary>
        /// A case a new card may be compared with: decided for what the card showed. Maintenance,
        /// testing and "already sent" discards say nothing about the card, so they are never matched.
        /// </summary>
        internal bool Usable
        {
            get
            {
                return !Maint && Reason != "MAINTENANCE" && Reason != "TESTING" && Reason != "DUPLICATE";
            }
        }
    }

    internal sealed class JudgeCauseCases
    {
        public List<JudgeCase> Cases = new List<JudgeCase>();

        /// <summary>Per numeric feature: the spread (standard deviation) over this cause's cases; a difference is measured in these units.</summary>
        public Dictionary<string, double> Scale = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    /// <summary>judge-cases.json: every decided alert of the sites the extractor reads, by cause.</summary>
    internal sealed class JudgeCaseSet
    {
        internal const int FormatVersion = 1;
        private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

        public DateTime BuiltAt;
        public string Source = "";
        public List<int> Sites = new List<int>();
        public Dictionary<string, JudgeCauseCases> Causes = new Dictionary<string, JudgeCauseCases>(StringComparer.Ordinal);

        internal int CaseCount
        {
            get
            {
                return Causes.Values.Sum(c => c.Cases.Count);
            }
        }

        internal static string CauseKey(string cause)
        {
            return (cause ?? "").Trim().ToUpperInvariant();
        }

        internal void Add(string cause, JudgeCase c)
        {
            string key = CauseKey(cause);
            JudgeCauseCases list;
            if (!Causes.TryGetValue(key, out list))
            {
                list = new JudgeCauseCases();
                Causes[key] = list;
            }

            list.Cases.Add(c);
        }

        /// <summary>Call once after the last Add, before ToJson.</summary>
        internal void ComputeScales()
        {
            foreach (JudgeCauseCases cc in Causes.Values)
            {
                cc.Scale.Clear();
                foreach (string key in cc.Cases.SelectMany(c => c.F.Num.Keys).Distinct(StringComparer.Ordinal))
                {
                    List<double> v = cc.Cases.Where(c => c.F.Num.ContainsKey(key)).Select(c => c.F.Num[key]).ToList();
                    double sd = 0;
                    if (v.Count >= 2)
                    {
                        double mean = v.Average();
                        sd = Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Count);
                    }

                    cc.Scale[key] = sd > 0 ? Math.Round(sd, 6) : 1;
                }
            }
        }

        internal string ToJson()
        {
            JObject root = new JObject();
            root["format"] = FormatVersion;
            root["builtAt"] = BuiltAt.ToString(TimeFormat, CultureInfo.InvariantCulture);
            root["source"] = Source;
            root["sites"] = new JArray(Sites.OrderBy(x => x));
            JObject causes = new JObject();
            foreach (KeyValuePair<string, JudgeCauseCases> kv in Causes.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                JObject scale = new JObject();
                foreach (KeyValuePair<string, double> s in kv.Value.Scale.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    scale[s.Key] = s.Value;
                }

                JArray cases = new JArray();
                foreach (JudgeCase c in kv.Value.Cases.OrderBy(x => x.Set).ThenBy(x => x.Id))
                {
                    JObject o = new JObject();
                    o["id"] = c.Id;
                    o["a"] = c.Asset;
                    o["p"] = c.Pass ? 1 : 0;
                    o["r"] = c.Reason ?? "";
                    o["m"] = c.Maint ? 1 : 0;
                    o["s"] = c.Set.ToString(TimeFormat, CultureInfo.InvariantCulture);
                    o["d"] = c.Decided.ToString(TimeFormat, CultureInfo.InvariantCulture);
                    o["f"] = c.F.ToJson();
                    cases.Add(o);
                }

                JObject cause = new JObject();
                cause["scale"] = scale;
                cause["cases"] = cases;
                causes[kv.Key] = cause;
            }

            root["causes"] = causes;
            return root.ToString(Formatting.None);
        }

        internal static JudgeCaseSet Load(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        internal static JudgeCaseSet Parse(string json)
        {
            JObject root = JObject.Parse(json);
            int format = root["format"] != null ? root["format"].Value<int>() : 0;
            if (format != FormatVersion)
            {
                throw new InvalidDataException("judge-cases.json format " + format + " is not the supported format " + FormatVersion);
            }

            JudgeCaseSet set = new JudgeCaseSet();
            set.BuiltAt = Time((string)root["builtAt"]);
            set.Source = (string)root["source"] ?? "";
            JArray sites = root["sites"] as JArray;
            if (sites != null)
            {
                set.Sites = sites.Select(x => x.Value<int>()).ToList();
            }

            JObject causes = root["causes"] as JObject;
            if (causes == null)
            {
                return set;
            }

            foreach (JProperty p in causes.Properties())
            {
                JudgeCauseCases cc = new JudgeCauseCases();
                JObject cause = p.Value as JObject;
                JObject scale = cause != null ? cause["scale"] as JObject : null;
                if (scale != null)
                {
                    foreach (JProperty s in scale.Properties())
                    {
                        cc.Scale[s.Name] = s.Value.Value<double>();
                    }
                }

                JArray cases = cause != null ? cause["cases"] as JArray : null;
                if (cases != null)
                {
                    foreach (JObject o in cases.OfType<JObject>())
                    {
                        JudgeCase c = new JudgeCase();
                        c.Id = o["id"].Value<long>();
                        c.Asset = o["a"].Value<int>();
                        c.Pass = o["p"].Value<int>() == 1;
                        c.Reason = (string)o["r"] ?? "";
                        c.Maint = o["m"].Value<int>() == 1;
                        c.Set = Time((string)o["s"]);
                        c.Decided = Time((string)o["d"]);
                        c.F = JudgeCaseFeatures.FromJson(o["f"] as JObject);
                        cc.Cases.Add(c);
                    }
                }

                set.Causes[CauseKey(p.Name)] = cc;
            }

            return set;
        }

        private static DateTime Time(string s)
        {
            return DateTime.ParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
        }
    }

    internal sealed class JudgeCaseNeighbor
    {
        public JudgeCase Case;
        public double Distance;
    }

    internal sealed class JudgeCaseMatch
    {
        /// <summary>PASS, SUPPRESSED, or null when the cause has no usable past case.</summary>
        public string Decision;

        public int PassCount;
        public int SuppressedCount;

        /// <summary>How many past cases of this cause could be compared.</summary>
        public int PoolSize;

        public List<JudgeCaseNeighbor> Nearest = new List<JudgeCaseNeighbor>();
    }

    internal static class JudgeCaseMatcher
    {
        /// <summary>
        /// The k past cases of the same cause whose cards are most like this one. More of them
        /// discarded -> SUPPRESSED; more approved -> PASS; a tie goes with the single nearest.
        /// decidedBefore: only cases a person had already decided by then are used.
        /// </summary>
        internal static JudgeCaseMatch Match(JudgeCaseSet set, string cause, int asset, JudgeCaseFeatures f, DateTime decidedBefore, long excludeId, int k, double assetWeight, double maxDistance = 0)
        {
            JudgeCaseMatch m = new JudgeCaseMatch();
            JudgeCauseCases cc;
            if (set == null || f == null || !set.Causes.TryGetValue(JudgeCaseSet.CauseKey(cause), out cc))
            {
                return m;
            }

            List<JudgeCaseNeighbor> all = new List<JudgeCaseNeighbor>();
            foreach (JudgeCase c in cc.Cases)
            {
                if (!c.Usable || c.Decided >= decidedBefore || c.Id == excludeId)
                {
                    continue;
                }

                JudgeCaseNeighbor n = new JudgeCaseNeighbor();
                n.Case = c;
                n.Distance = Distance(f, asset, c, cc.Scale, assetWeight);

                // 1.0.208.0 -- only a past case with the SAME CONDITION counts: farther than
                // Learned.MaxDistance is a different situation and gives no answer.
                if (maxDistance > 0 && n.Distance > maxDistance)
                {
                    continue;
                }

                all.Add(n);
            }

            m.PoolSize = all.Count;
            if (all.Count == 0)
            {
                return m;
            }

            m.Nearest = all.OrderBy(x => x.Distance).ThenByDescending(x => x.Case.Decided).Take(Math.Max(1, k)).ToList();
            m.PassCount = m.Nearest.Count(x => x.Case.Pass);
            m.SuppressedCount = m.Nearest.Count - m.PassCount;
            bool suppress = m.SuppressedCount > m.PassCount || (m.SuppressedCount == m.PassCount && !m.Nearest[0].Case.Pass);
            m.Decision = suppress ? "SUPPRESSED" : "PASS";
            return m;
        }

        /// <summary>
        /// 0 = identical card, 1 = nothing in common. The mean, over every feature at least one side
        /// has, of: numbers -- the difference in spreads, capped at 3 and scaled to 0..1; words --
        /// 0 same / 1 different; a feature only one side has -- 1. A different asset adds assetWeight
        /// features that all differ.
        /// </summary>
        internal static double Distance(JudgeCaseFeatures a, int assetA, JudgeCase b, Dictionary<string, double> scale, double assetWeight)
        {
            double sum = 0;
            double count = 0;
            foreach (KeyValuePair<string, double> kv in a.Num)
            {
                count += 1;
                double other;
                if (!b.F.Num.TryGetValue(kv.Key, out other))
                {
                    sum += 1;
                    continue;
                }

                double s;
                if (!scale.TryGetValue(kv.Key, out s) || s <= 0)
                {
                    s = 1;
                }

                sum += Math.Min(Math.Abs(kv.Value - other) / s, 3.0) / 3.0;
            }

            foreach (string key in b.F.Num.Keys)
            {
                if (!a.Num.ContainsKey(key))
                {
                    count += 1;
                    sum += 1;
                }
            }

            foreach (KeyValuePair<string, string> kv in a.Cat)
            {
                count += 1;
                string other;
                if (!b.F.Cat.TryGetValue(kv.Key, out other) || !string.Equals(kv.Value, other, StringComparison.Ordinal))
                {
                    sum += 1;
                }
            }

            foreach (string key in b.F.Cat.Keys)
            {
                if (!a.Cat.ContainsKey(key))
                {
                    count += 1;
                    sum += 1;
                }
            }

            if (assetWeight > 0)
            {
                count += assetWeight;
                if (assetA != b.Asset)
                {
                    sum += assetWeight;
                }
            }

            return count <= 0 ? 1 : Math.Round(sum / count, 4);
        }
    }
}
