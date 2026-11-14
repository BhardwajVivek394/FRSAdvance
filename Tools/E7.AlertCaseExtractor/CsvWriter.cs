using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace E7.AlertCaseExtractor
{
    internal static class CsvWriter
    {
        /// <summary>
        /// Columns that depend on OTHER alerts. Computed over the whole set each time the CSV is written,
        /// so they never go stale and never enter a row's source hash.
        /// </summary>
        internal static void AddSiteContext(List<Dictionary<string, string>> rows, int windowMinutes)
        {
            List<Ctx> all = rows.Select(r => new Ctx(r)).ToList();
            foreach (Ctx a in all)
            {
                if (!a.Set.HasValue)
                {
                    continue;
                }

                int site = 0;
                int asset = 0;
                foreach (Ctx b in all)
                {
                    if (ReferenceEquals(a, b) || !b.Set.HasValue)
                    {
                        continue;
                    }

                    double minutes = Math.Abs((b.Set.Value - a.Set.Value).TotalMinutes);
                    if (minutes <= windowMinutes && b.Station == a.Station && b.Cause == a.Cause && a.Station.Length > 0)
                    {
                        site++;
                    }

                    if (b.Asset == a.Asset && a.Asset.Length > 0 && b.Set.Value < a.Set.Value && b.Set.Value >= a.Set.Value.AddDays(-7))
                    {
                        asset++;
                    }
                }

                a.Row["same_cause_site_count"] = site.ToString(CultureInfo.InvariantCulture);
                a.Row["same_asset_alerts_7d"] = asset.ToString(CultureInfo.InvariantCulture);
            }
        }

        internal static string Build(IEnumerable<Dictionary<string, string>> rows, string[] columns)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(string.Join(",", columns.Select(Escape)));
            sb.Append("\r\n");
            foreach (Dictionary<string, string> r in rows)
            {
                List<string> cells = new List<string>(columns.Length);
                foreach (string c in columns)
                {
                    string v;
                    cells.Add(Escape(r.TryGetValue(c, out v) ? v : ""));
                }

                sb.Append(string.Join(",", cells));
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        private static string Escape(string v)
        {
            if (string.IsNullOrEmpty(v))
            {
                return "";
            }

            if (v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return v;
            }

            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        private sealed class Ctx
        {
            public readonly Dictionary<string, string> Row;
            public readonly DateTime? Set;
            public readonly string Station;
            public readonly string Cause;
            public readonly string Asset;

            public Ctx(Dictionary<string, string> row)
            {
                Row = row;
                DateTime t;
                string s;
                if (row.TryGetValue("set_time", out s) && DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                {
                    Set = t;
                }

                Station = Get(row, "station");
                Cause = Get(row, "cause_code");
                Asset = Get(row, "asset_id");
            }

            private static string Get(Dictionary<string, string> row, string key)
            {
                string v;
                return row.TryGetValue(key, out v) && v != null ? v : "";
            }
        }
    }
}
