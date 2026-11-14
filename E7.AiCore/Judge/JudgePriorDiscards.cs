using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using Newtonsoft.Json.Linq;

namespace E7.AiCore.Judge
{
    // ============================================================================================
    // JudgePriorDiscards (2.1) -- what a human already decided about THIS asset and THIS cause.
    //
    // WHY. A reviewer who opens the Approval Dashboard, discards an alert and writes a remark has
    // made a judgement the machine cannot make: "this cause on this asset is not a real failure."
    // Until now the judge knew nothing about it, so the next identical trigger was judged from
    // scratch and raised again. This step reads that decision back.
    //
    // WHERE THE DATA COMES FROM. dbo.PendingAlertApproval, written by Energy7.AlertV3 /
    // Energy7.AlertApprovalTool. NOTHING in AlertV3 changes -- this is a read-only lookup from the
    // judge side, over the existing E7MRIV2DB_SQL connection string (same pattern as
    // AnalysisEngine.Grounding.Coverage.WriteAnalysisLink).
    //
    //   Status      2 = Discarded   (0 Pending, 1 Approved, 4 Hold, 5 Suppressed)
    //   UpdatedBy   the UserId who acted. NULL = system-driven -- the sibling-discard sweep and the
    //               active-conflict auto-discard both write NULL, and they carry NO human judgement,
    //               so they are excluded here. Only a row a person acted on counts.
    //   DiscardWasValid  (Patch 47) the reviewer's own True/False answer to "was the alert valid?".
    //               0 = the reviewer said the alert was NOT valid -> a repeat is suppressed.
    //               1 or NULL = discarded for some other reason -> evidence only, never decides.
    //   DiscardRemark    (Patch 47) the reviewer's free text. Carried into the verdict and the AI pack.
    //
    // FAIL-OPEN. Every failure path -- no connection string, Patch 47 not applied, SQL unreachable,
    // timeout -- returns Checked=false with an Error, and the judgement proceeds exactly as it did
    // before this step existed. A prior discard can only ever SUPPRESS; it can never turn a
    // SUPPRESSED into a PASS, so losing the lookup is always the safe direction.
    //
    // CHANGE THE RULE in JudgePolicy (what a discard does) or judge-settings.json "PriorDiscard"
    // (whether it runs, how far back, how many rows). Change the QUERY here.
    // ============================================================================================
    internal static class JudgePriorDiscards
    {
        /// <summary>Status 2 = Discarded in dbo.PendingAlertApproval.</summary>
        private const int StatusDiscarded = 2;

        private const string Sql = @"
SELECT TOP (@Top)
       p.Id, p.CauseCode, p.AlertType, p.CreatedAt, p.UpdatedAt, p.UpdatedBy,
       p.DiscardWasValid, p.DiscardRemark, p.AiVerdict
FROM   dbo.PendingAlertApproval AS p WITH (NOLOCK)
WHERE  p.AssetId  = @AssetId
  AND  p.Status   = @Status
  AND  p.UpdatedBy IS NOT NULL
  AND  p.UpdatedAt >= @Since
  AND  p.UpdatedAt <  @Until
  AND  UPPER(LTRIM(RTRIM(p.CauseCode))) = @CauseCode
ORDER BY p.UpdatedAt DESC";

        internal static JudgePriorDiscardResult Lookup(JudgeCard card, JudgeSettings cfg, List<string> log)
        {
            JudgePriorDiscardResult r = new JudgePriorDiscardResult();

            if (cfg == null || !cfg.PriorDiscardEnabled)
            {
                r.Error = "disabled";
                log.Add("priorDiscard: skipped (PriorDiscard.Enabled=false)");
                return r;
            }

            if (card == null || card.AssetId <= 0 || string.IsNullOrWhiteSpace(card.CauseCode))
            {
                r.Error = "no asset id or cause code on the card";
                log.Add("priorDiscard: skipped (" + r.Error + ")");
                return r;
            }

            System.Configuration.ConnectionStringSettings cs =
                System.Configuration.ConfigurationManager.ConnectionStrings[cfg.PriorDiscardConnectionName];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString))
            {
                r.Error = "connection string '" + cfg.PriorDiscardConnectionName + "' not configured";
                log.Add("priorDiscard: skipped (" + r.Error + ")");
                return r;
            }

            // 1.0.201.0 -- the window ends at the alert's OWN trigger time, not at "now". On a live
            // gate call the two are seconds apart and nothing changes. On a replay of a past alert,
            // "now" let the reviewer's discard of this very alert -- made after it triggered -- count
            // as a prior decision, so every replayed discard was suppressed by its own answer.
            // PendingAlertApproval times are IST wall clock (GetDate below), so compare in IST.
            DateTime until = DateTime.Now;
            if (card.TriggeredAt != default(DateTimeOffset))
            {
                until = card.TriggeredAt.ToOffset(TimeSpan.FromMinutes(330)).DateTime;
            }

            DateTime since = until.AddDays(-Math.Max(1, cfg.PriorDiscardLookbackDays));
            string cause = card.CauseCode.Trim().ToUpperInvariant();

            try
            {
                using (SqlConnection cn = new SqlConnection(cs.ConnectionString))
                using (SqlCommand cmd = new SqlCommand(Sql, cn))
                {
                    cmd.CommandTimeout = Math.Max(1, cfg.PriorDiscardTimeoutSec);
                    cmd.Parameters.Add("@Top", SqlDbType.Int).Value = Math.Max(1, cfg.PriorDiscardMaxRows);
                    cmd.Parameters.Add("@AssetId", SqlDbType.Int).Value = card.AssetId;
                    cmd.Parameters.Add("@Status", SqlDbType.Int).Value = StatusDiscarded;
                    cmd.Parameters.Add("@Since", SqlDbType.DateTime).Value = since;
                    cmd.Parameters.Add("@Until", SqlDbType.DateTime).Value = until;
                    cmd.Parameters.Add("@CauseCode", SqlDbType.NVarChar, 200).Value = cause;

                    cn.Open();
                    using (SqlDataReader rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            JudgePriorDiscard d = new JudgePriorDiscard();
                            d.Id = GetInt(rd, "Id");
                            d.CauseCode = GetString(rd, "CauseCode");
                            d.AlertType = GetInt(rd, "AlertType");
                            d.RaisedAt = GetDate(rd, "CreatedAt");
                            d.DiscardedAt = GetDate(rd, "UpdatedAt");
                            d.DiscardedBy = GetInt(rd, "UpdatedBy");
                            d.WasValid = GetBool(rd, "DiscardWasValid");
                            d.Remark = GetString(rd, "DiscardRemark");
                            d.AiVerdict = GetString(rd, "AiVerdict");
                            r.Rows.Add(d);
                        }
                    }
                }

                r.Checked = true;
                r.LookbackDays = cfg.PriorDiscardLookbackDays;

                for (int i = 0; i < r.Rows.Count; i++)
                {
                    if (r.Rows[i].WasValid == false)
                    {
                        r.RejectedBy = r.Rows[i];      // newest first -> the most recent rejection
                        break;
                    }
                }

                log.Add("priorDiscard: " + r.Rows.Count + " manual discard(s) for asset " + card.AssetId
                      + " cause '" + cause + "' in the " + r.LookbackDays + " d before "
                      + until.ToString("yyyy-MM-dd HH:mm:ss") + " IST"
                      + (r.RejectedBy != null
                            ? "; most recent says the alert was NOT valid (id " + r.RejectedBy.Id + ", "
                              + JudgeUtil.T(r.RejectedBy.DiscardedAt, "yyyy-MM-dd HH:mm") + ")"
                            : r.Rows.Count > 0 ? "; none marked not-valid" : ""));
            }
            catch (Exception ex)
            {
                // Patch 47 not applied, DB unreachable, timeout -- never fails a judgement.
                r.Checked = false;
                r.Rows.Clear();
                r.RejectedBy = null;
                r.Error = ex.GetType().Name + ": " + ex.Message;
                log.Add("priorDiscard: NOT CHECKED -- " + r.Error);
                JudgeLog.Write("WARN", "JUDGE", "priorDiscard lookup failed for asset " + card.AssetId + ": " + ex.Message);
            }

            return r;
        }

        // ---- the block the AI sees, and the block the caller sees in the envelope ----
        internal static JArray Json(JudgePriorDiscardResult r)
        {
            JArray a = new JArray();
            if (r == null)
            {
                return a;
            }

            for (int i = 0; i < r.Rows.Count; i++)
            {
                JudgePriorDiscard d = r.Rows[i];
                JObject o = new JObject();
                o["pendingId"] = d.Id;
                o["causeCode"] = d.CauseCode;
                o["raisedAt"] = JudgeUtil.IsoOrNull(d.RaisedAt);
                o["discardedAt"] = JudgeUtil.IsoOrNull(d.DiscardedAt);
                o["discardedByUserId"] = d.DiscardedBy;
                o["reviewerSaysAlertWasValid"] = d.WasValid.HasValue ? (JToken)d.WasValid.Value : JValue.CreateNull();
                o["remark"] = string.IsNullOrWhiteSpace(d.Remark) ? JValue.CreateNull() : (JToken)d.Remark.Trim();
                o["aiVerdictAtTheTime"] = string.IsNullOrWhiteSpace(d.AiVerdict) ? JValue.CreateNull() : (JToken)d.AiVerdict;
                a.Add(o);
            }

            return a;
        }

        private static string GetString(SqlDataReader rd, string col)
        {
            int i = rd.GetOrdinal(col);
            return rd.IsDBNull(i) ? null : Convert.ToString(rd.GetValue(i));
        }

        private static int GetInt(SqlDataReader rd, string col)
        {
            int i = rd.GetOrdinal(col);
            return rd.IsDBNull(i) ? 0 : Convert.ToInt32(rd.GetValue(i));
        }

        private static bool? GetBool(SqlDataReader rd, string col)
        {
            int i = rd.GetOrdinal(col);
            return rd.IsDBNull(i) ? (bool?)null : Convert.ToBoolean(rd.GetValue(i));
        }

        private static DateTimeOffset? GetDate(SqlDataReader rd, string col)
        {
            int i = rd.GetOrdinal(col);
            if (rd.IsDBNull(i))
            {
                return null;
            }

            DateTime t = Convert.ToDateTime(rd.GetValue(i));
            return new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeSpan.FromMinutes(330));
        }
    }
}
