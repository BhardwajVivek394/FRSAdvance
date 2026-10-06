using Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace E7.AiCore
{
    // RosterItemBuilder -- the 15-day evidence rule, as pure logic.
    //
    // Kept OUT of the controller on purpose: it takes a list of alerts and a date and
    // returns items. No services, no HTTP, no ambient state -- so the ranking can be
    // reasoned about, and later moved server-side into the API (where the scheduler will
    // want it) without rewriting it.
    //
    // WHAT IT DOES NOT DO, deliberately:
    //   * It does not invent ML classifications. MlNote and LastOpNote are left null unless
    //     the caller supplies them. A fabricated "72.7% faulty" would be worse than a blank
    //     line, because a JE cannot tell the difference on the card.
    //   * It does not decide carry-forward, downgrade or failed-repair. Those need YESTERDAY,
    //     which only the API has -- RosterService applies them after this runs.
    public static class RosterItemBuilder
    {
        public const int WindowDays = 15;

        // A cause that has fired 3+ times in 15 days is a pattern, not noise. Below that we
        // do not promote on count alone.
        private const int PatternCount = 3;

        public class BuildResult
        {
            public List<RosterItem> Items { get; set; }
            public int AlertsRead { get; set; }
            public int AssetsConsidered { get; set; }
            public string SourceNote { get; set; }

            public BuildResult()
            {
                Items = new List<RosterItem>();
            }
        }

        public static BuildResult Build(List<FRSAlert> alerts, int divisionId, DateTime rosterDate)
        {
            BuildResult result = new BuildResult();
            DateTime windowStart = rosterDate.Date.AddDays(-WindowDays);
            DateTime midPoint = rosterDate.Date.AddDays(-(WindowDays / 2));   // 15 -> 7 days back

            if (alerts == null)
            {
                alerts = new List<FRSAlert>();
            }
            result.AlertsRead = alerts.Count;

            // One item per asset per cause FAMILY. Family, not exact cause: a track that
            // throws RAIL RES HIGH one day and BALST RES LOW the next is one degrading
            // asset, not two unrelated items -- and the key must stay stable across days
            // for the API's carry-forward to find it.
            var groups = alerts
                .Where(a => a != null && a.AssetId > 0)
                .GroupBy(a => new
                {
                    a.SiteId,
                    a.AssetId,
                    Family = CauseFamily(a.CauseCode ?? a.PossibleCause)
                });

            foreach (var g in groups)
            {
                List<FRSAlert> rows = g.ToList();
                result.AssetsConsidered++;

                int total = rows.Count;
                int firstHalf = rows.Count(a => Set(a) < midPoint);
                int secondHalf = total - firstHalf;
                bool activeNow = rows.Any(a => IsOpen(a));
                int unacked = rows.Count(a => a.IsAcknowledgement != true);   // ack status straight off the FRS alert rows

                FRSAlert latest = rows.OrderByDescending(a => Set(a)).First();
                FRSAlert dominant = rows
                    .GroupBy(a => (a.CauseCode ?? a.PossibleCause ?? "").Trim())
                    .OrderByDescending(x => x.Count())
                    .First()
                    .First();

                string trend = Trend(firstHalf, secondHalf);
                string priority = Rank(total, activeNow, trend);

                RosterItem item = new RosterItem();
                item.DivisionId = divisionId;
                item.SiteId = g.Key.SiteId;
                item.StationName = latest.SiteName;
                item.AssetId = g.Key.AssetId;
                item.AssetName = latest.AssetName;
                item.AssetTypeName = latest.AssetType;
                item.Priority = priority;
                item.ItemKey = divisionId + ":" + g.Key.SiteId + ":" + g.Key.AssetId + ":" + g.Key.Family;
                item.FirstSeenDate = rosterDate.Date;      // the API overwrites this on carry-forward
                item.DayCount = 1;
                item.CloseStatus = RosterCloseStatus.Open;
                item.Reason = Reason(total, activeNow, firstHalf, secondHalf, trend, dominant, latest);
                item.Evidence15d = Evidence(total, unacked, activeNow, firstHalf, secondHalf, trend, latest, dominant);
                item.ActionText = Action(dominant.CauseCode ?? dominant.PossibleCause, latest.AssetType);

                result.Items.Add(item);
            }

            // Worst first, so a truncated list still leads with what matters.
            result.Items = result.Items
                .OrderBy(x => Rankable(x.Priority))
                .ThenByDescending(x => x.AssetName)
                .ToList();

            result.SourceNote = "alerts read " + result.AlertsRead
                + "; " + result.AssetsConsidered + " asset/cause groups over " + WindowDays + " days"
                + "; ML classification not attached";
            return result;
        }

        // ---- rules ----

        private static string Rank(int total, bool activeNow, string trend)
        {
            // An ACTIVE alert outranks a bigger count that has fully reset: something is
            // wrong NOW beats something that was wrong and recovered.
            if (activeNow) { return RosterPriority.Urgent; }
            if (trend == "degrading" && total >= PatternCount) { return RosterPriority.Urgent; }
            if (total >= PatternCount) { return RosterPriority.Soon; }
            return RosterPriority.Monitor;
        }

        private static int Rankable(string priority)
        {
            if (priority == RosterPriority.Urgent) { return 1; }
            if (priority == RosterPriority.Soon) { return 2; }
            return 3;
        }

        private static string Trend(int firstHalf, int secondHalf)
        {
            if (secondHalf > firstHalf) { return "degrading"; }
            if (secondHalf < firstHalf) { return "improving"; }
            return "stable";
        }

        private static string Reason(int total, bool activeNow, int firstHalf, int secondHalf,
                                     string trend, FRSAlert dominant, FRSAlert latest)
        {
            // Numbers, never a bare adjective: "degrading" alone tells a JE nothing he can
            // check, "degrading (2 -> 5)" does.
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(total).Append(total == 1 ? " alert" : " alerts").Append(" in ").Append(WindowDays).Append(" days");
            sb.Append(", ").Append(trend).Append(" (").Append(firstHalf).Append(" -> ").Append(secondHalf).Append(")");
            if (activeNow)
            {
                sb.Append("; 1 or more ACTIVE now");
            }
            string cause = (dominant.CauseCode ?? dominant.PossibleCause ?? "").Trim();
            if (cause.Length > 0)
            {
                sb.Append(". Mainly ").Append(cause);
            }
            DateTime last = Set(latest);
            if (last != DateTime.MinValue)
            {
                sb.Append("; last ").Append(last.ToString("dd MMM HH:mm"));
                if (!IsOpen(latest) && latest.ResetTimeStamp.HasValue)
                {
                    double mins = (latest.ResetTimeStamp.Value - last).TotalMinutes;
                    if (mins >= 0 && mins < 100000)
                    {
                        sb.Append(", reset in ").Append(Math.Round(mins)).Append(" min");
                    }
                }
            }
            return sb.ToString();
        }

        private static string Evidence(int total, int unacked, bool activeNow, int firstHalf, int secondHalf,
                                       string trend, FRSAlert latest, FRSAlert dominant)
        {
            // Small hand-built JSON: no serializer dependency, and the shape stays readable
            // in the database, where somebody will eventually have to read it. "unacked" rides
            // here so the view's acknowledged summary needs no schema change and no extra API.
            return "{\"windowDays\":" + WindowDays
                 + ",\"total\":" + total
                 + ",\"unacked\":" + unacked
                 + ",\"activeNow\":" + (activeNow ? "true" : "false")
                 + ",\"firstHalf\":" + firstHalf
                 + ",\"secondHalf\":" + secondHalf
                 + ",\"trend\":\"" + Esc(trend) + "\""
                 + ",\"dominantCause\":\"" + Esc(dominant.CauseCode ?? dominant.PossibleCause) + "\""
                 + ",\"lastSet\":\"" + Esc(Set(latest).ToString("yyyy-MM-dd HH:mm")) + "\""
                 + "}";
        }

        // Cause -> inspection wording. Deliberately a SHORT, explicit map of causes seen in
        // the field, and null for anything unrecognised. A generated instruction that sends
        // a technician to the wrong component is worse than no instruction, so this list
        // should be reviewed by an SSE before it grows.
        private static string Action(string causeCode, string assetType)
        {
            string c = (causeCode ?? "").Trim().ToUpper();

            if (c.Contains("RAIL RES"))
            {
                return "Megger the section; inspect glued joints, bonds and ballast drainage.";
            }
            if (c.Contains("BALST") || c.Contains("SLPR"))
            {
                return "Inspect ballast and sleepers for leakage; verify drainage.";
            }
            if (c.Contains("TC SHORT"))
            {
                return "Inspect the section for a metallic short across the rails; check bonds.";
            }
            if (c.Contains("TC CKT OPEN") || c.Contains("RES OPEN"))
            {
                return "Check rail continuity, bonds and TLJB cable terminations.";
            }
            if (c.Contains("BT CHG CURR") || c.Contains("CHARG"))
            {
                return "Check the battery charger output and the TFC; verify charging current.";
            }
            if (c.Contains("TFC I/P") || c.Contains("VOLT LOW"))
            {
                return "Check the feed voltage and terminations at the location box.";
            }
            if (c.Contains("VAR RES"))
            {
                return "Check the variable resistance setting and connections.";
            }
            if (c.StartsWith("PT ") || (assetType ?? "").ToUpper().Contains("POINT"))
            {
                return "Inspect the point machine: slide chairs, lubrication, drive rod and detection.";
            }
            if (c.StartsWith("SIG ") || (assetType ?? "").ToUpper().Contains("SIGNAL"))
            {
                return "Verify the signal lamp circuit and aspect energisation.";
            }
            return null;   // unrecognised cause -> no instruction, rather than a guessed one
        }

        // ---- helpers ----

        private static string CauseFamily(string causeCode)
        {
            string c = (causeCode ?? "").Trim().ToUpper();
            if (c.Length == 0) { return "GEN"; }
            int sp = c.IndexOf(' ');
            return sp > 0 ? c.Substring(0, sp) : c;      // "TC RAIL RES HIGH" -> "TC"
        }

        private static DateTime Set(FRSAlert a)
        {
            return a.SetTimeStamp;
        }

        private static bool IsOpen(FRSAlert a)
        {
            // Both signals, because either alone has been wrong before: IsActive is the
            // service's view, a missing ResetTimeStamp is the record's.
            if (a.IsActive) { return true; }
            return !a.ResetTimeStamp.HasValue;
        }

        private static string Esc(string s)
        {
            if (String.IsNullOrEmpty(s)) { return ""; }
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
