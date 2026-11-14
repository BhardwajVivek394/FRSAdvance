using E7.AiCore.Judge;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace E7.AlertCaseExtractor
{
    // 1.1.0 -- judge-cases.json: every decided alert as a case the Judge can compare a new card with.
    // The case features come from JudgeCaseBase.cs, the same source file the Judge compiles, so a
    // stored case and a live alert are always described the same way.
    internal static class JudgeCasesWriter
    {
        /// <summary>Ledger-row key that holds the case features. Not a CSV column.</summary>
        internal const string FeaturesKey = "_judge_features";

        internal const string FileName = "judge-cases.json";

        // the settings the Judge ships with (judge-settings.json "Learned": K, AssetWeight)
        internal const int DefaultK = 5;
        internal const double DefaultAssetWeight = 3;

        internal static JudgeCaseSet Build(IEnumerable<Dictionary<string, string>> rows, ExtractorSettings cfg, string source)
        {
            JudgeCaseSet set = new JudgeCaseSet();
            set.BuiltAt = DateTime.Now;
            set.Source = source;
            set.Sites = cfg.SiteIds.Distinct().ToList();
            foreach (Dictionary<string, string> r in rows)
            {
                DateTime setTime;
                DateTime decided;
                long id;
                int asset;
                string cause = Get(r, "cause_code");
                if (cause.Length == 0
                    || !Time(Get(r, "set_time"), out setTime)
                    || !Time(Get(r, "decided_at"), out decided)
                    || !long.TryParse(Get(r, "pending_id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                    || !int.TryParse(Get(r, "asset_id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out asset))
                {
                    continue;
                }

                JudgeCase c = new JudgeCase();
                c.Id = id;
                c.Asset = asset;
                c.Pass = Get(r, "label") == "PASS";
                c.Reason = c.Pass ? "" : Get(r, "discard_reason");
                c.Maint = Get(r, "in_maintenance_window") == "1";
                c.Set = setTime;
                c.Decided = decided;
                string features = Get(r, FeaturesKey);
                if (features.Length > 0)
                {
                    c.F = JudgeCaseFeatures.FromJson(JObject.Parse(features));
                }

                set.Add(cause, c);
            }

            set.ComputeScales();
            return set;
        }

        /// <summary>Folder or full .json path from the settings -> the file to write.</summary>
        internal static string Target(string judgeCasesPath)
        {
            string p = judgeCasesPath.Trim();
            return p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? p : Path.Combine(p, FileName);
        }

        /// <summary>
        /// Checks the matching on alerts it has not seen: the newest 30% of the cases are judged the
        /// way the Judge will judge them (maintenance window, then the most similar cases a person
        /// had decided before the last 02:00), and the result is compared with what the person did.
        /// </summary>
        internal static void Replay(JudgeCaseSet set, RunLog log)
        {
            List<KeyValuePair<string, JudgeCase>> all = set.Causes
                .SelectMany(kv => kv.Value.Cases.Select(c => new KeyValuePair<string, JudgeCase>(kv.Key, c)))
                .OrderBy(x => x.Value.Set)
                .ToList();
            if (all.Count < 20)
            {
                log.Info("replay: only " + all.Count + " case(s), nothing to test");
                return;
            }

            DateTime cut = all[(int)(all.Count * 0.7)].Value.Set;
            List<KeyValuePair<string, JudgeCase>> test = all.Where(x => x.Value.Set >= cut).ToList();
            int right = 0;
            int approved = 0;
            int approvedPassed = 0;
            int discarded = 0;
            int discardedSuppressed = 0;
            Dictionary<string, int[]> byStep = new Dictionary<string, int[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JudgeCase> t in test)
            {
                JudgeCase c = t.Value;
                string step;
                bool pass;
                if (c.Maint)
                {
                    step = "maintenance window";
                    pass = false;
                }
                else
                {
                    // what was in the file when this alert came: cases decided before the last 02:00
                    DateTime two = c.Set.Date.AddHours(2);
                    if (two > c.Set)
                    {
                        two = two.AddDays(-1);
                    }

                    JudgeCaseMatch m = JudgeCaseMatcher.Match(set, t.Key, c.Asset, c.F, two, c.Id, DefaultK, DefaultAssetWeight);
                    if (m.Decision == null)
                    {
                        step = "no past case (existing checks decide; counted as PASS)";
                        pass = true;
                    }
                    else
                    {
                        pass = m.Decision == "PASS";
                        step = pass ? "similar to approved cases" : "similar to discarded cases";
                    }
                }

                bool ok = pass == c.Pass;
                if (ok)
                {
                    right++;
                }

                if (c.Pass)
                {
                    approved++;
                    if (pass)
                    {
                        approvedPassed++;
                    }
                }
                else
                {
                    discarded++;
                    if (!pass)
                    {
                        discardedSuppressed++;
                    }
                }

                int[] s;
                if (!byStep.TryGetValue(step, out s))
                {
                    s = new int[2];
                    byStep[step] = s;
                }

                s[0]++;
                if (ok)
                {
                    s[1]++;
                }
            }

            log.Info("replay: " + all.Count + " cases, newest 30% = " + test.Count + " alert(s) set from " + cut.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " (k=" + DefaultK + ", assetWeight=" + DefaultAssetWeight.ToString(CultureInfo.InvariantCulture) + ")");
            log.Info("replay: same verdict as the person " + right + " of " + test.Count + " = " + Pct(right, test.Count));
            log.Info("replay: approved alerts passed " + approvedPassed + " of " + approved + " = " + Pct(approvedPassed, approved)
                + "; discarded alerts suppressed " + discardedSuppressed + " of " + discarded + " = " + Pct(discardedSuppressed, discarded));
            foreach (KeyValuePair<string, int[]> kv in byStep.OrderByDescending(x => x.Value[0]))
            {
                log.Info("replay:   " + kv.Key + ": " + kv.Value[0] + " alert(s), right " + Pct(kv.Value[1], kv.Value[0]));
            }
        }

        private static string Pct(int a, int b)
        {
            return b == 0 ? "n/a" : (100.0 * a / b).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static bool Time(string s, out DateTime t)
        {
            return DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
        }

        private static string Get(Dictionary<string, string> row, string key)
        {
            string v;
            return row.TryGetValue(key, out v) && v != null ? v : "";
        }
    }
}
