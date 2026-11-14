using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace E7.AlertCaseExtractor
{
    /// <summary>One decided alert, exactly as read from dbo.PendingAlertApproval (+ its FRSAlert row).</summary>
    internal sealed class SourceRecord
    {
        public int PendingId;
        public int? FrsId;
        public int? Status;
        public int? SiteId;
        public int? AssetId;
        public string CauseCode;
        public DateTime? SetTime;
        public DateTime? ResetTime;
        public DateTime? UpdatedAt;
        public int? UpdatedBy;
        public bool? DiscardWasValid;
        public string DiscardRemark;
        public string ApprovalRemark;
        public string LocalCard;
        public string CloudCard;
        public string PlainCard;
        public string ResetCard;
        public string LocalRaw;
        public string Raw;
        public string LocalResetRaw;
        public string ResetRaw;
        public string FrsRemark;
        public int? AckStatusId;
        public string JudgeDecidedBy;
        public bool? Reapproved;
        public int? MaintId;
        public DateTime? MaintFrom;
        public DateTime? MaintTo;

        /// <summary>Every alert is keyed by its pending id: pending 3010 and FRS 3010 are different alerts.</summary>
        public string Key
        {
            get
            {
                return "P:" + PendingId;
            }
        }

        /// <summary>
        /// What the human did. Approved (Status 1) and reached a real FRS alert -> PASS.
        /// Discarded (Status 2) -> SUPPRESSED, whatever the discard reason. Only these two kinds are read.
        /// </summary>
        public string Label
        {
            get
            {
                if (Status == 1 && FrsId.HasValue && FrsId.Value > 0)
                {
                    return "PASS";
                }

                return "SUPPRESSED";
            }
        }
    }

    // Read-only access. The column list is built from the live table, so an optional column the
    // table does not have is simply left out instead of failing the whole query.
    internal static class SourceReader
    {
        // 1.0.1: UpdatedBy is optional (it is NULL on every row of the live DB); JudgeDecidedBy and
        // ReapprovedFromDiscard are read so the CSV can mark them.
        private static readonly string[] OptionalPendingColumns =
        {
            "SiteId", "UpdatedBy", "DiscardWasValid", "DiscardRemark", "ApprovalRemark",
            "JudgeDecidedBy", "ReapprovedFromDiscard",
            "LocalAlertCardJson", "CloudAlertCardJson", "AlertCardJson", "ResetAlertCardJson",
            "LocalRawJsonData", "RawJsonData", "LocalResetRawJsonData", "ResetRawJsonData"
        };

        // PASS = Status 1 with a real FRS id; SUPPRESSED = Status 2. Approvals without an FRS id are not read.
        private const string DecidedFilter = "((p.[Status] = 1 AND p.[FRSAlertId] > 0) OR p.[Status] = 2)";

        internal static List<SourceRecord> Read(ExtractorSettings cfg, DateTime from, RunLog log)
        {
            List<SourceRecord> list = new List<SourceRecord>();
            string sql;
            using (SqlConnection cn = new SqlConnection(cfg.ConnectionString))
            {
                cn.Open();
                HashSet<string> pending = Columns(cn, "dbo.PendingAlertApproval");
                HashSet<string> frs = Columns(cn, "dbo.FRSAlert");
                bool hasMaint = TableExists(cn, "dbo.MaintenanceMode");

                string[] required = { "Id", "FRSAlertId", "Status", "AssetId", "CauseCode", "UpdatedAt", cfg.SetTimeColumn };
                List<string> missing = required.Where(c => !pending.Contains(c)).ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidOperationException("dbo.PendingAlertApproval is missing required column(s): " + string.Join(", ", missing) + ". Run --check.");
                }

                List<string> select = new List<string>();
                select.Add("p.[Id] AS PendingId");
                select.Add("p.[FRSAlertId]");
                select.Add("p.[Status]");
                select.Add("p.[AssetId]");
                select.Add("p.[CauseCode]");
                select.Add("p.[UpdatedAt]");
                select.Add("p.[" + cfg.SetTimeColumn + "] AS SetTime");
                if (pending.Contains(cfg.ResetTimeColumn))
                {
                    select.Add("p.[" + cfg.ResetTimeColumn + "] AS ResetTime");
                }

                foreach (string c in OptionalPendingColumns)
                {
                    if (pending.Contains(c))
                    {
                        select.Add("p.[" + c + "]");
                    }
                }

                bool joinFrs = frs.Contains("Id");
                if (joinFrs && frs.Contains("Remark"))
                {
                    select.Add("f.[Remark] AS FrsRemark");
                }

                if (joinFrs && frs.Contains("AcknowledgemenStatusId"))
                {
                    select.Add("f.[AcknowledgemenStatusId] AS AckStatusId");
                }

                if (hasMaint)
                {
                    select.Add("mm.[Id] AS MaintId");
                    select.Add("mm.[ActiveTime] AS MaintFrom");
                    select.Add("mm.[InActiveTime] AS MaintTo");
                }

                sql = "SELECT TOP (@Batch) " + string.Join(", ", select)
                    + " FROM dbo.PendingAlertApproval AS p WITH (NOLOCK)"
                    + (joinFrs ? " LEFT JOIN dbo.FRSAlert AS f WITH (NOLOCK) ON f.[Id] = p.[FRSAlertId]" : "")
                    + (hasMaint ? MaintApply(cfg) : "")
                    + " WHERE " + DecidedFilter + SiteFilter(cfg)
                    + " AND p.[UpdatedAt] >= @From"
                    + " AND p.[Id] > @LastId"
                    + " ORDER BY p.[Id]";
            }

            // 1.0.1: read in batches by Id. One query of ~150 MB of JSON dropped the remote connection
            // after 5 minutes; a batch is a few MB, and a dropped batch is retried on a new connection.
            int batch = Math.Max(50, cfg.BatchSize);
            int lastId = 0;
            int batches = 0;
            while (true)
            {
                List<SourceRecord> part = ReadBatch(cfg, sql, from, lastId, batch, log);
                batches++;
                list.AddRange(part);
                if (part.Count < batch)
                {
                    break;
                }

                lastId = part[part.Count - 1].PendingId;
                if (batches % 5 == 0)
                {
                    log.Info("  ... " + list.Count + " read (last pending id " + lastId + ")");
                }
            }

            log.Info("read " + list.Count + " decided alert(s) updated since " + from.ToString("yyyy-MM-dd HH:mm") + " in " + batches + " batch(es)");
            return list;
        }

        /// <summary>One batch, on its own connection. A dropped connection is retried twice before the run fails.</summary>
        private static List<SourceRecord> ReadBatch(ExtractorSettings cfg, string sql, DateTime from, int lastId, int batch, RunLog log)
        {
            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    List<SourceRecord> part = new List<SourceRecord>();
                    using (SqlConnection cn = new SqlConnection(cfg.ConnectionString))
                    {
                        cn.Open();
                        using (SqlCommand cmd = new SqlCommand(sql, cn))
                        {
                            cmd.CommandTimeout = Math.Max(10, cfg.CommandTimeoutSec);
                            cmd.Parameters.Add("@Batch", SqlDbType.Int).Value = batch;
                            cmd.Parameters.Add("@From", SqlDbType.DateTime).Value = from;
                            cmd.Parameters.Add("@LastId", SqlDbType.Int).Value = lastId;
                            using (SqlDataReader rd = cmd.ExecuteReader())
                            {
                                HashSet<string> got = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                for (int i = 0; i < rd.FieldCount; i++)
                                {
                                    got.Add(rd.GetName(i));
                                }

                                while (rd.Read())
                                {
                                    part.Add(Map(rd, got));
                                }
                            }
                        }
                    }

                    return part;
                }
                catch (SqlException ex)
                {
                    if (attempt >= 3)
                    {
                        throw;
                    }

                    log.Warn("  batch after pending id " + lastId + " failed (attempt " + attempt + "): " + ex.Message + " -- retrying");
                    SqlConnection.ClearAllPools();
                }
            }
        }

        private static SourceRecord Map(SqlDataReader rd, HashSet<string> got)
        {
            SourceRecord r = new SourceRecord();
            r.PendingId = Convert.ToInt32(rd["PendingId"]);
            r.FrsId = Int(rd, got, "FRSAlertId");
            r.Status = Int(rd, got, "Status");
            r.SiteId = Int(rd, got, "SiteId");
            r.AssetId = Int(rd, got, "AssetId");
            r.CauseCode = (Str(rd, got, "CauseCode") ?? "").Trim().ToUpperInvariant();
            r.SetTime = Date(rd, got, "SetTime");
            r.ResetTime = Date(rd, got, "ResetTime");
            r.UpdatedAt = Date(rd, got, "UpdatedAt");
            r.UpdatedBy = Int(rd, got, "UpdatedBy");
            r.DiscardWasValid = Bool(rd, got, "DiscardWasValid");
            r.DiscardRemark = Str(rd, got, "DiscardRemark");
            r.ApprovalRemark = Str(rd, got, "ApprovalRemark");
            r.JudgeDecidedBy = Str(rd, got, "JudgeDecidedBy");
            r.Reapproved = Bool(rd, got, "ReapprovedFromDiscard");
            r.LocalCard = Str(rd, got, "LocalAlertCardJson");
            r.CloudCard = Str(rd, got, "CloudAlertCardJson");
            r.PlainCard = Str(rd, got, "AlertCardJson");
            r.ResetCard = Str(rd, got, "ResetAlertCardJson");
            r.LocalRaw = Str(rd, got, "LocalRawJsonData");
            r.Raw = Str(rd, got, "RawJsonData");
            r.LocalResetRaw = Str(rd, got, "LocalResetRawJsonData");
            r.ResetRaw = Str(rd, got, "ResetRawJsonData");
            r.FrsRemark = Str(rd, got, "FrsRemark");
            r.AckStatusId = Int(rd, got, "AckStatusId");
            r.MaintId = Int(rd, got, "MaintId");
            r.MaintFrom = Date(rd, got, "MaintFrom");
            r.MaintTo = Date(rd, got, "MaintTo");
            return r;
        }

        /// <summary>
        /// The maintenance window of the SAME asset that covers the set time, if any (latest one wins).
        /// An open window (InActiveTime NULL) counts up to now.
        /// </summary>
        private static string MaintApply(ExtractorSettings cfg)
        {
            return " OUTER APPLY (SELECT TOP 1 m.[Id], m.[ActiveTime], m.[InActiveTime]"
                + " FROM dbo.MaintenanceMode AS m WITH (NOLOCK)"
                + " WHERE m.[AssetId] = p.[AssetId]"
                + " AND p.[" + cfg.SetTimeColumn + "] >= m.[ActiveTime]"
                + " AND p.[" + cfg.SetTimeColumn + "] <= ISNULL(m.[InActiveTime], GETDATE())"
                + " ORDER BY m.[ActiveTime] DESC) AS mm";
        }

        /// <summary>Site lat/long from dbo.SiteTemperature, latest row per SiteId. Rows that do not parse, or are 0,0, are skipped.</summary>
        internal static Dictionary<int, StationGeo> ReadSiteGeo(ExtractorSettings cfg, RunLog log)
        {
            Dictionary<int, StationGeo> map = new Dictionary<int, StationGeo>();
            using (SqlConnection cn = new SqlConnection(cfg.ConnectionString))
            {
                cn.Open();
                if (!TableExists(cn, "dbo.SiteTemperature"))
                {
                    log.Warn("dbo.SiteTemperature not found: no site lat/long from the DB");
                    return map;
                }

                string sql = "SELECT SiteId, Latitude, Longitude FROM ("
                    + " SELECT SiteId, Latitude, Longitude, ROW_NUMBER() OVER (PARTITION BY SiteId ORDER BY Id DESC) AS rn"
                    + " FROM dbo.SiteTemperature WITH (NOLOCK)) AS x WHERE rn = 1";
                using (SqlCommand cmd = new SqlCommand(sql, cn))
                {
                    cmd.CommandTimeout = Math.Max(10, cfg.CommandTimeoutSec);
                    using (SqlDataReader rd = cmd.ExecuteReader())
                    {
                        while (rd.Read())
                        {
                            if (rd["SiteId"] == DBNull.Value)
                            {
                                continue;
                            }

                            double lat;
                            double lon;
                            string sLat = Convert.ToString(rd["Latitude"]);
                            string sLon = Convert.ToString(rd["Longitude"]);
                            if (!double.TryParse((sLat ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
                                || !double.TryParse((sLon ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out lon))
                            {
                                continue;
                            }

                            if (lat == 0 && lon == 0)
                            {
                                continue;
                            }

                            StationGeo g = new StationGeo();
                            g.Lat = lat;
                            g.Lon = lon;
                            map[Convert.ToInt32(rd["SiteId"])] = g;
                        }
                    }
                }
            }

            log.Info("site lat/long from dbo.SiteTemperature: " + map.Count + " site(s)");
            return map;
        }

        // --check: verifies columns and shows the counts before any real run. Reads no alert data.
        internal static void Check(ExtractorSettings cfg, RunLog log)
        {
            using (SqlConnection cn = new SqlConnection(cfg.ConnectionString))
            {
                cn.Open();
                log.Info("connected: " + cn.DataSource + " / " + cn.Database);

                HashSet<string> pending = Columns(cn, "dbo.PendingAlertApproval");
                HashSet<string> frs = Columns(cn, "dbo.FRSAlert");
                log.Info("dbo.PendingAlertApproval columns: " + pending.Count + ", dbo.FRSAlert columns: " + frs.Count);

                foreach (string c in new[] { "Id", "FRSAlertId", "Status", "AssetId", "CauseCode", "UpdatedAt" })
                {
                    log.Info("  required " + c + ": " + (pending.Contains(c) ? "OK" : "MISSING"));
                }

                foreach (string c in new[] { "SetTimeStamp", "DeviceTimestamp", "ResetTimeStamp" })
                {
                    log.Info("  time column " + c + ": " + (pending.Contains(c) ? "present" : "absent"));
                }

                bool setOk = pending.Contains(cfg.SetTimeColumn);
                log.Info("  configured SetTimeColumn '" + cfg.SetTimeColumn + "': " + (setOk ? "OK" : "MISSING -- change the setting"));

                foreach (string c in OptionalPendingColumns)
                {
                    log.Info("  optional " + c + ": " + (pending.Contains(c) ? "present" : "absent"));
                }

                log.Info("  FRSAlert Remark: " + (frs.Contains("Remark") ? "present" : "absent")
                    + ", AcknowledgemenStatusId: " + (frs.Contains("AcknowledgemenStatusId") ? "present" : "absent"));

                bool hasMaint = TableExists(cn, "dbo.MaintenanceMode");
                log.Info("  dbo.MaintenanceMode: " + (hasMaint ? "present" : "absent"));

                // 1.0.2: every PendingAlertApproval count below honours SiteIds
                string site = SiteFilter(cfg);
                log.Info("  " + SiteFilterText(cfg));
                string sinceFrom = "FROM dbo.PendingAlertApproval AS p WITH (NOLOCK) WHERE p.[UpdatedAt] >= @From" + site + " AND ";
                Count(cn, cfg, "approved with FRS id (-> PASS)", "SELECT COUNT(*) " + sinceFrom + "p.[Status] = 1 AND p.[FRSAlertId] > 0", log);
                Count(cn, cfg, "discarded (-> SUPPRESSED)", "SELECT COUNT(*) " + sinceFrom + "p.[Status] = 2", log);
                Count(cn, cfg, "approved with FRS id NULL or 0 (not read)", "SELECT COUNT(*) " + sinceFrom + "p.[Status] = 1 AND ISNULL(p.[FRSAlertId], 0) <= 0", log);
                Count(cn, cfg, "status 0 / 4 / 5 or other (not read)", "SELECT COUNT(*) " + sinceFrom + "p.[Status] NOT IN (1, 2)", log);

                // rows the UpdatedAt filter can never see
                Count(cn, cfg, "status 1/2 with UpdatedAt NULL (all time; missed by the date filter)",
                    "SELECT COUNT(*) FROM dbo.PendingAlertApproval AS p WITH (NOLOCK) WHERE p.[UpdatedAt] IS NULL" + site + " AND " + DecidedFilter, log, false);

                if (pending.Contains("JudgeDecidedBy"))
                {
                    Count(cn, cfg, "read rows with JudgeDecidedBy set (kept, marked)", "SELECT COUNT(*) " + sinceFrom + DecidedFilter + " AND p.[JudgeDecidedBy] IS NOT NULL", log);
                }

                if (hasMaint && setOk)
                {
                    Count(cn, cfg, "read rows inside a maintenance window of the same asset",
                        "SELECT COUNT(*) FROM dbo.PendingAlertApproval AS p WITH (NOLOCK)" + MaintApply(cfg)
                        + " WHERE p.[UpdatedAt] >= @From" + site + " AND " + DecidedFilter + " AND mm.[Id] IS NOT NULL", log);
                }

                if (TableExists(cn, "dbo.SiteTemperature"))
                {
                    Count(cn, cfg, "sites with lat/long in dbo.SiteTemperature",
                        "SELECT COUNT(DISTINCT SiteId) FROM dbo.SiteTemperature WITH (NOLOCK) WHERE ISNULL(Latitude, '') <> '' AND ISNULL(Longitude, '') <> ''", log, false);
                }
            }
        }

        /// <summary>" AND p.[SiteId] IN (161,167)", or "" when SiteIds is empty. Ints only, so the SQL text is safe.</summary>
        private static string SiteFilter(ExtractorSettings cfg)
        {
            if (cfg.SiteIds == null || cfg.SiteIds.Count == 0)
            {
                return "";
            }

            return " AND p.[SiteId] IN (" + string.Join(",", cfg.SiteIds.Distinct()) + ")";
        }

        /// <summary>Log text for the site filter.</summary>
        internal static string SiteFilterText(ExtractorSettings cfg)
        {
            if (cfg.SiteIds == null || cfg.SiteIds.Count == 0)
            {
                return "site filter: none, every site";
            }

            return "site filter: " + string.Join(", ", cfg.SiteIds.Distinct());
        }

        private static void Count(SqlConnection cn, ExtractorSettings cfg, string what, string sql, RunLog log)
        {
            Count(cn, cfg, what, sql, log, true);
        }

        private static void Count(SqlConnection cn, ExtractorSettings cfg, string what, string sql, RunLog log, bool sinceStart)
        {
            try
            {
                using (SqlCommand cmd = new SqlCommand(sql, cn))
                {
                    cmd.CommandTimeout = Math.Max(10, cfg.CommandTimeoutSec);
                    cmd.Parameters.Add("@From", SqlDbType.DateTime).Value = cfg.HistoryStart;
                    object n = cmd.ExecuteScalar();
                    string prefix = sinceStart ? "  since " + cfg.HistoryStart.ToString("yyyy-MM-dd") + ", " : "  ";
                    log.Info(prefix + what + ": " + n);
                }
            }
            catch (Exception ex)
            {
                log.Warn("  count '" + what + "' failed: " + ex.Message);
            }
        }

        private static bool TableExists(SqlConnection cn, string table)
        {
            using (SqlCommand cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@t, 'U') IS NULL THEN 0 ELSE 1 END", cn))
            {
                cmd.Parameters.Add("@t", SqlDbType.NVarChar, 256).Value = table;
                return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
            }
        }

        private static HashSet<string> Columns(SqlConnection cn, string table)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SqlCommand cmd = new SqlCommand("SELECT TOP 0 * FROM " + table + " WITH (NOLOCK)", cn))
            using (SqlDataReader rd = cmd.ExecuteReader(CommandBehavior.SchemaOnly))
            {
                for (int i = 0; i < rd.FieldCount; i++)
                {
                    set.Add(rd.GetName(i));
                }
            }

            return set;
        }

        private static string Str(SqlDataReader rd, HashSet<string> got, string col)
        {
            if (!got.Contains(col) || rd[col] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToString(rd[col]);
        }

        private static int? Int(SqlDataReader rd, HashSet<string> got, string col)
        {
            if (!got.Contains(col) || rd[col] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(rd[col]);
        }

        private static bool? Bool(SqlDataReader rd, HashSet<string> got, string col)
        {
            if (!got.Contains(col) || rd[col] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToBoolean(rd[col]);
        }

        private static DateTime? Date(SqlDataReader rd, HashSet<string> got, string col)
        {
            if (!got.Contains(col) || rd[col] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToDateTime(rd[col]);
        }
    }
}
