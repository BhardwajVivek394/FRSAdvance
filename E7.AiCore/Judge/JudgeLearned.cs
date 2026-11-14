using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgeLearned (1.0.202.0) -- two checks learned from what reviewers already decided:
    //
    //   MAINTENANCE    the asset is inside a dbo.MaintenanceMode window at the alert time
    //                  (2,486 decided alerts of SK + GLTA: 659 discarded / 18 approved inside one)
    //   SIMILAR CASES  the card is compared with past decided alerts of the same cause from
    //                  judge-cases.json (written every night by E7.AlertCaseExtractor); the K most
    //                  similar decide: more discarded -> SUPPRESSED, more approved -> PASS
    //
    // Each has its own mode in judge-settings.json "Learned":
    //   off      not looked at
    //   report   looked at, written to the verdict (verdict.learned) and the log, changes NOTHING
    //   enforce  decides the verdict (decidedBy "maintenance" / "similar-cases"); history and AI are skipped
    // The defaults are "report", so publishing this DLL changes no verdict until a mode is set to enforce.
    //
    // Read-only. Neither check can fail a judgement: any error is recorded and the pipeline carries on.
    // ============================================================================================
    internal sealed class JudgeLearnedResult
    {
        // ---- maintenance ----
        public string MaintMode = "off";
        public bool MaintChecked;
        public string MaintError;
        public bool InMaintenance;
        public int MaintId;
        public DateTime? MaintFrom;
        public DateTime? MaintTo;

        // ---- similar cases ----
        public string CaseMode = "off";
        public bool CaseChecked;
        public string CaseError;
        public JudgeCaseMatch Match;
        public DateTime? FileBuiltAt;
        public string FileSource;

        /// <summary>What these checks say on their own: SUPPRESSED, PASS, or null when neither has an answer.</summary>
        public string WouldDecide;

        /// <summary>Which check that answer comes from: "maintenance" or "similar-cases".</summary>
        public string WouldDecideBy;

        /// <summary>True when that check's mode is enforce, i.e. WouldDecide IS the verdict.</summary>
        public bool Enforced;
    }

    internal static class JudgeLearned
    {
        private const string MaintSql = @"
SELECT TOP 1 m.Id, m.ActiveTime, m.InActiveTime
FROM   dbo.MaintenanceMode AS m WITH (NOLOCK)
WHERE  m.AssetId = @AssetId
  AND  @At >= m.ActiveTime
  AND  @At <= ISNULL(m.InActiveTime, GETDATE())
ORDER  BY m.ActiveTime DESC";

        private static readonly object Gate = new object();
        private static JudgeCaseSet cached;
        private static string cachedPath;
        private static DateTime cachedStamp;

        internal static JudgeLearnedResult Check(JudgeCard card, JToken cardJson, JudgeSettings cfg, List<string> log)
        {
            JudgeLearnedResult r = new JudgeLearnedResult();
            if (cfg == null || card == null)
            {
                return r;
            }

            r.MaintMode = Mode(cfg.LearnedMaintenanceMode);
            r.CaseMode = Mode(cfg.LearnedCaseMode);

            // PendingAlertApproval / MaintenanceMode times are IST wall clock, like the prior-discard lookup.
            DateTime at = DateTime.Now;
            if (card.TriggeredAt != default(DateTimeOffset))
            {
                at = card.TriggeredAt.ToOffset(TimeSpan.FromMinutes(330)).DateTime;
            }

            if (r.MaintMode != "off")
            {
                CheckMaintenance(r, card, at, cfg, log);
            }

            if (r.CaseMode != "off")
            {
                CheckCases(r, card, cardJson as JObject, at, cfg, log);
            }

            // maintenance first: it is the stronger signal and needs no past case
            if (r.InMaintenance)
            {
                r.WouldDecide = "SUPPRESSED";
                r.WouldDecideBy = "maintenance";
                r.Enforced = r.MaintMode == "enforce";
            }
            else if (r.Match != null && r.Match.Decision != null)
            {
                r.WouldDecide = r.Match.Decision;
                r.WouldDecideBy = "similar-cases";
                r.Enforced = r.CaseMode == "enforce";
            }

            if (r.WouldDecide != null)
            {
                log.Add("learned: " + r.WouldDecideBy + " -> " + r.WouldDecide + (r.Enforced ? " (ENFORCED)" : " (report only, verdict not changed)"));
            }

            return r;
        }

        private static string Mode(string configured)
        {
            string m = (configured ?? "").Trim().ToLowerInvariant();
            return m == "enforce" || m == "report" ? m : "off";
        }

        private static void CheckMaintenance(JudgeLearnedResult r, JudgeCard card, DateTime at, JudgeSettings cfg, List<string> log)
        {
            if (card.AssetId <= 0)
            {
                r.MaintError = "no asset id on the card";
                log.Add("learned: maintenance skipped (" + r.MaintError + ")");
                return;
            }

            System.Configuration.ConnectionStringSettings cs =
                System.Configuration.ConfigurationManager.ConnectionStrings[cfg.PriorDiscardConnectionName];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
            {
                r.MaintError = "connection string '" + cfg.PriorDiscardConnectionName + "' not configured";
                log.Add("learned: maintenance skipped (" + r.MaintError + ")");
                return;
            }

            try
            {
                using (SqlConnection cn = new SqlConnection(cs.ConnectionString))
                using (SqlCommand cmd = new SqlCommand(MaintSql, cn))
                {
                    cmd.CommandTimeout = Math.Max(1, cfg.LearnedTimeoutSec);
                    cmd.Parameters.Add("@AssetId", SqlDbType.Int).Value = card.AssetId;
                    cmd.Parameters.Add("@At", SqlDbType.DateTime).Value = at;
                    cn.Open();
                    using (SqlDataReader rd = cmd.ExecuteReader())
                    {
                        if (rd.Read())
                        {
                            r.InMaintenance = true;
                            r.MaintId = Convert.ToInt32(rd["Id"], CultureInfo.InvariantCulture);
                            r.MaintFrom = rd["ActiveTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["ActiveTime"], CultureInfo.InvariantCulture);
                            r.MaintTo = rd["InActiveTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(rd["InActiveTime"], CultureInfo.InvariantCulture);
                        }
                    }
                }

                r.MaintChecked = true;
                log.Add("learned: maintenance " + (r.InMaintenance
                    ? "window " + r.MaintId + " covers " + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " IST on asset " + card.AssetId
                    : "no window on asset " + card.AssetId + " at " + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " IST"));
            }
            catch (Exception ex)
            {
                r.MaintChecked = false;
                r.InMaintenance = false;
                r.MaintError = ex.GetType().Name + ": " + ex.Message;
                log.Add("learned: maintenance NOT CHECKED -- " + r.MaintError);
                JudgeLog.Write("WARN", "JUDGE", "maintenance lookup failed for asset " + card.AssetId + ": " + ex.Message);
            }
        }

        private static void CheckCases(JudgeLearnedResult r, JudgeCard card, JObject cardJson, DateTime at, JudgeSettings cfg, List<string> log)
        {
            if (cardJson == null || card.AssetId <= 0 || string.IsNullOrWhiteSpace(card.CauseCode))
            {
                r.CaseError = "no card JSON, asset id or cause code";
                log.Add("learned: similar cases skipped (" + r.CaseError + ")");
                return;
            }

            try
            {
                string path = string.IsNullOrWhiteSpace(cfg.LearnedCasesFile)
                    ? Path.Combine(cfg.BaseDir ?? "", "judge-cases.json")
                    : (Path.IsPathRooted(cfg.LearnedCasesFile) ? cfg.LearnedCasesFile : Path.Combine(cfg.BaseDir ?? "", cfg.LearnedCasesFile));
                JudgeCaseSet set = LoadCases(path);
                if (set == null)
                {
                    r.CaseError = "case file not found: " + path;
                    log.Add("learned: similar cases skipped (" + r.CaseError + ")");
                    return;
                }

                r.FileBuiltAt = set.BuiltAt;
                r.FileSource = set.Source;
                double ageHours = (DateTime.Now - set.BuiltAt).TotalHours;
                if (cfg.LearnedMaxFileAgeHours > 0 && ageHours > cfg.LearnedMaxFileAgeHours)
                {
                    // the 02:00 job has stopped: an old file must not keep deciding alerts on its own
                    r.CaseError = "case file is " + ((int)ageHours) + " h old (limit " + cfg.LearnedMaxFileAgeHours + " h) -- not used";
                    log.Add("learned: similar cases skipped (" + r.CaseError + ")");
                    JudgeLog.Write("WARN", "JUDGE", "judge-cases.json is stale: built " + set.BuiltAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                    return;
                }

                JudgeCaseFeatures f = JudgeCaseFeatures.FromCard(cardJson);
                r.Match = JudgeCaseMatcher.Match(set, card.CauseCode, card.AssetId, f, at, card.AlertId, Math.Max(1, cfg.LearnedK), Math.Max(0, cfg.LearnedAssetWeight));
                r.CaseChecked = true;
                if (r.Match.Decision != null && r.Match.Nearest.Count < Math.Max(1, cfg.LearnedMinCases))
                {
                    log.Add("learned: only " + r.Match.Nearest.Count + " past case(s), fewer than MinCases " + cfg.LearnedMinCases + " -- no answer");
                    r.Match.Decision = null;
                }

                log.Add("learned: similar cases for '" + JudgeCaseSet.CauseKey(card.CauseCode) + "': pool " + r.Match.PoolSize
                    + ", nearest " + r.Match.Nearest.Count + " -> " + r.Match.SuppressedCount + " discarded / " + r.Match.PassCount + " approved"
                    + (r.Match.Nearest.Count > 0 ? ", closest id " + r.Match.Nearest[0].Case.Id + " at distance " + r.Match.Nearest[0].Distance.ToString("0.###", CultureInfo.InvariantCulture) : "")
                    + " (file built " + set.BuiltAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")");
            }
            catch (Exception ex)
            {
                r.CaseChecked = false;
                r.Match = null;
                r.CaseError = ex.GetType().Name + ": " + ex.Message;
                log.Add("learned: similar cases NOT CHECKED -- " + r.CaseError);
                JudgeLog.Write("WARN", "JUDGE", "similar-case check failed: " + ex.Message);
            }
        }

        /// <summary>The parsed file, re-read only when its last-write time changes (the 02:00 job replaces it).</summary>
        private static JudgeCaseSet LoadCases(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            DateTime stamp = File.GetLastWriteTimeUtc(path);
            lock (Gate)
            {
                if (cached != null && string.Equals(cachedPath, path, StringComparison.OrdinalIgnoreCase) && cachedStamp == stamp)
                {
                    return cached;
                }

                JudgeCaseSet set = JudgeCaseSet.Load(path);
                cached = set;
                cachedPath = path;
                cachedStamp = stamp;
                JudgeLog.Write("INFO", "JUDGE", "judge-cases.json loaded: " + set.CaseCount + " case(s), " + set.Causes.Count + " cause(s), built "
                    + set.BuiltAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " by " + set.Source);
                return set;
            }
        }

        // ---- the reason a person reads, and the block in the verdict ---------------------------

        internal static string Reason(JudgeLearnedResult r)
        {
            if (r.WouldDecideBy == "maintenance")
            {
                return "The asset was in maintenance mode when this alert was raised (maintenance record " + r.MaintId
                    + (r.MaintFrom.HasValue ? ", on since " + r.MaintFrom.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) : "")
                    + "), so the alert is not sent.";
            }

            JudgeCaseMatch m = r.Match;
            string closest = m.Nearest.Count > 0
                ? " The closest is approval record " + m.Nearest[0].Case.Id + ", which a reviewer " + (m.Nearest[0].Case.Pass ? "approved" : "discarded"
                    + (string.IsNullOrEmpty(m.Nearest[0].Case.Reason) ? "" : " (" + m.Nearest[0].Case.Reason.ToLowerInvariant().Replace('_', ' ') + ")"))
                    + " on " + m.Nearest[0].Case.Decided.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) + "."
                : "";
            return "This card was compared with the past alerts of the same cause that reviewers decided. Of the " + m.Nearest.Count
                + " most similar, " + m.SuppressedCount + " were discarded and " + m.PassCount + " were approved, so this alert is "
                + (m.Decision == "PASS" ? "sent" : "not sent") + "." + closest;
        }

        internal static JObject Json(JudgeLearnedResult r)
        {
            JObject maint = new JObject();
            maint["mode"] = r.MaintMode;
            maint["checked"] = r.MaintChecked;
            maint["inMaintenance"] = r.InMaintenance;
            maint["recordId"] = r.InMaintenance ? (JToken)r.MaintId : JValue.CreateNull();
            maint["from"] = r.MaintFrom.HasValue ? (JToken)r.MaintFrom.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : JValue.CreateNull();
            maint["to"] = r.MaintTo.HasValue ? (JToken)r.MaintTo.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : JValue.CreateNull();
            maint["error"] = string.IsNullOrEmpty(r.MaintError) ? JValue.CreateNull() : (JToken)r.MaintError;

            JObject cases = new JObject();
            cases["mode"] = r.CaseMode;
            cases["checked"] = r.CaseChecked;
            cases["decision"] = r.Match != null && r.Match.Decision != null ? (JToken)r.Match.Decision : JValue.CreateNull();
            cases["discarded"] = r.Match != null ? r.Match.SuppressedCount : 0;
            cases["approved"] = r.Match != null ? r.Match.PassCount : 0;
            cases["poolSize"] = r.Match != null ? r.Match.PoolSize : 0;
            cases["fileBuiltAt"] = r.FileBuiltAt.HasValue ? (JToken)r.FileBuiltAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : JValue.CreateNull();
            cases["fileSource"] = string.IsNullOrEmpty(r.FileSource) ? JValue.CreateNull() : (JToken)r.FileSource;
            cases["error"] = string.IsNullOrEmpty(r.CaseError) ? JValue.CreateNull() : (JToken)r.CaseError;
            JArray nearest = new JArray();
            if (r.Match != null)
            {
                foreach (JudgeCaseNeighbor n in r.Match.Nearest)
                {
                    JObject o = new JObject();
                    o["pendingId"] = n.Case.Id;
                    o["assetId"] = n.Case.Asset;
                    o["decision"] = n.Case.Pass ? "APPROVED" : "DISCARDED";
                    o["reason"] = string.IsNullOrEmpty(n.Case.Reason) ? JValue.CreateNull() : (JToken)n.Case.Reason;
                    o["setAt"] = n.Case.Set.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    o["decidedAt"] = n.Case.Decided.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    o["distance"] = n.Distance;
                    nearest.Add(o);
                }
            }

            cases["nearest"] = nearest;

            JObject root = new JObject();
            root["wouldDecide"] = r.WouldDecide != null ? (JToken)r.WouldDecide : JValue.CreateNull();
            root["wouldDecideBy"] = r.WouldDecideBy != null ? (JToken)r.WouldDecideBy : JValue.CreateNull();
            root["enforced"] = r.Enforced;
            root["maintenance"] = maint;
            root["similarCases"] = cases;
            return root;
        }
    }
}
