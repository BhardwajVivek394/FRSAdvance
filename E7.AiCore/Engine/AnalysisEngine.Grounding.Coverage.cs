using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- Grounding.Coverage.
    // Moved verbatim from AiChatController.Grounding.Coverage.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // Coverage must read the SAME payloads LooksUsableToolResult accepts: the
        // MCP client may deliver {"result":{"content":[{"text":"<json>"}]}} or the
        // bare projected [{"text":"<json>"}] array. Text members of ONE content
        // array are concatenated before parsing (parity with LooksUsableInner), so
        // a JSON document split across blocks is reconstructed. Depth-bounded.
        internal static List<JToken> ParseCoverageDocuments(string result, int depth)
        {
            List<JToken> docs = ParseJsonDocuments(result);
            if (depth <= 0)
            {
                return docs;
            }
            List<JToken> all = new List<JToken>(docs);
            for (int d = 0; d < docs.Count; d++)
            {
                foreach (JToken contentTok in docs[d].SelectTokens("$..content"))
                {
                    AddConcatenatedContent(contentTok as JArray, all, depth);
                }
                // bare projection: the content array itself IS the document
                AddConcatenatedContent(docs[d] as JArray, all, depth);
            }
            return all;
        }


        internal static void AddConcatenatedContent(JArray content, List<JToken> all, int depth)
        {
            if (content == null || content.Count == 0)
            {
                return;
            }
            StringBuilder cb = new StringBuilder();
            for (int k = 0; k < content.Count; k++)
            {
                JObject o = content[k] as JObject;
                if (o == null)
                {
                    continue;
                }
                JToken tx = PropCI(o, "text");
                if (tx != null && tx.Type == JTokenType.String)
                {
                    cb.Append(tx.ToString());
                }
            }
            string inner = cb.ToString().Trim();
            if (inner.Length < 3 || !(inner.StartsWith("{") || inner.StartsWith("[")))
            {
                return;
            }
            List<JToken> innerDocs = ParseCoverageDocuments(inner, depth - 1);
            for (int k = 0; k < innerDocs.Count; k++)
            {
                all.Add(innerDocs[k]);
            }
        }


        // One pass over every readable shape: {"ts":[...]} parallel arrays and the
        // columnar {"cols":[...],"rows":[[ts,v],...]} format this controller forces
        // via outputMode=columnar. All stamps land in one SET; statistics come from
        // the unique pre-alert subset.
        internal HistoryCoverage ComputeHistoryCoverage(string result)
        {
            try
            {
                long bound = Sess._incidenceEpoch + 15;
                HashSet<long> uniq = new HashSet<long>();
                List<JToken> docs = ParseCoverageDocuments(result, 2);
                for (int d = 0; d < docs.Count; d++)
                {
                    foreach (JToken tsArr in docs[d].SelectTokens("$..ts"))
                    {
                        JArray a = tsArr as JArray;
                        if (a == null)
                        {
                            continue;
                        }
                        for (int k = 0; k < a.Count; k++)
                        {
                            long v = NormalizeEpoch(a[k]);
                            if (v > 0)
                            {
                                uniq.Add(v);
                            }
                        }
                    }
                    foreach (JToken rowsTok in docs[d].SelectTokens("$..rows"))
                    {
                        JArray rows = rowsTok as JArray;
                        if (rows == null || rows.Count == 0)
                        {
                            continue;
                        }
                        int tsIdx = 0;
                        JProperty prop = rowsTok.Parent as JProperty;
                        JObject holder = prop != null ? prop.Parent as JObject : null;
                        JArray cols = holder != null ? holder["cols"] as JArray : null;
                        if (cols != null)
                        {
                            for (int c = 0; c < cols.Count; c++)
                            {
                                string cn = cols[c] == null ? "" : cols[c].ToString().Trim().ToLowerInvariant();
                                if (cn == "ts" || cn == "time" || cn == "timestamp")
                                {
                                    tsIdx = c;
                                    break;
                                }
                            }
                        }
                        for (int r = 0; r < rows.Count; r++)
                        {
                            JArray row = rows[r] as JArray;
                            if (row == null || tsIdx >= row.Count)
                            {
                                continue;
                            }
                            long v = NormalizeEpoch(row[tsIdx]);
                            if (v > 0)
                            {
                                uniq.Add(v);
                            }
                        }
                    }
                }
                if (uniq.Count == 0)
                {
                    return null;
                }
                List<long> sorted = new List<long>(uniq);
                sorted.Sort();
                List<long> before = new List<long>();
                long firstAfter = 0;
                for (int i = 0; i < sorted.Count; i++)
                {
                    if (sorted[i] <= bound)
                    {
                        before.Add(sorted[i]);
                    }
                    else
                    {
                        firstAfter = sorted[i];
                        break;
                    }
                }
                if (before.Count == 0)
                {
                    return null;
                }
                HistoryCoverage cov = new HistoryCoverage();
                cov.PreAlertSampleCount = before.Count;
                cov.EarliestBefore = before[0];
                cov.LastBefore = before[before.Count - 1];
                cov.FirstAfter = firstAfter;
                if (before.Count >= 3)
                {
                    List<long> gaps = new List<long>();
                    for (int i = 1; i < before.Count; i++)
                    {
                        long g = before[i] - before[i - 1];
                        if (g > 0)
                        {
                            gaps.Add(g);
                        }
                    }
                    if (gaps.Count > 0)
                    {
                        gaps.Sort();
                        cov.MedianPreAlertIntervalSec = gaps[gaps.Count / 2];
                    }
                }
                return cov;
            }
            catch
            {
                return null;
            }
        }


        // Epoch values may arrive in seconds or milliseconds; coverage math is in
        // seconds. Non-numeric and implausible (< 2001) values return 0 and are
        // ignored, so counters in unrelated "rows" cannot poison the result.
        internal static long NormalizeEpoch(JToken t)
        {
            if (t == null)
            {
                return 0;
            }
            long v;
            if (!long.TryParse(t.ToString(), out v))
            {
                return 0;
            }
            if (v > 100000000000L)
            {
                v = v / 1000;
            }
            if (v < 1000000000L)
            {
                return 0;
            }
            return v;
        }


        // The correct recovery DIFFERS by failure: only a series cut off by the
        // row cap is fixed by narrowing. Advice text is generated in ONE place so
        // status, prefetch note and agentic note can never disagree.
        internal static string CoverageRecoveryAdvice(CoverageStateKind kind)
        {
            if (kind == CoverageStateKind.ShortAtEnd)
            {
                return "call history_get again with sort=desc (newest-first) to return the MOST-RECENT "
                     + "rows in the range -- the incidence sits near the window end, so desc reaches it "
                     + "without narrowing; only narrow further if desc still stops short";
            }
            if (kind == CoverageStateKind.InsufficientSamples)
            {
                return "call history_get again with a WIDER window (or a different tag of the same "
                     + "asset for CORROBORATION - it cannot itself satisfy alerting-tag coverage) - "
                     + "narrowing cannot add samples";
            }
            if (kind == CoverageStateKind.InsufficientSpan)
            {
                return "call history_get again with an EARLIER start so the pre-alert span is "
                     + "longer - narrowing cannot lengthen it";
            }
            return "do not claim incidence coverage; if its samples do not reach the incidence "
                 + "time, one retry with a narrower window MAY help, otherwise treat coverage as unverified";
        }


        internal void SetTagCoverage(string tagId, HistoryCoverageLevel level)
        {
            if (Sess.coverageByTag == null)
            {
                Sess.coverageByTag = new Dictionary<string, HistoryCoverageLevel>(StringComparer.OrdinalIgnoreCase);
            }
            string key = tagId ?? "";
            HistoryCoverageLevel cur;
            if (!Sess.coverageByTag.TryGetValue(key, out cur) || level > cur)
            {
                Sess.coverageByTag[key] = level;   // monotonic upward PER TAG
            }
        }


        internal HistoryCoverageLevel AlertingCoverageLevel()
        {
            string tag = AlertingTagId();
            HistoryCoverageLevel lvl;
            if (tag == null || Sess.coverageByTag == null || !Sess.coverageByTag.TryGetValue(tag, out lvl))
            {
                return HistoryCoverageLevel.Unknown;
            }
            return lvl;
        }

        internal static bool ValidateCtxShape(JObject ctx, out string reason)
        {
            reason = null;
            for (int i = 0; i < _ctxScalarKeys.Length; i++)
            {
                JToken t = ctx[_ctxScalarKeys[i]];
                if (t == null || t.Type == JTokenType.Null) { continue; }
                if (t.Type == JTokenType.Object || t.Type == JTokenType.Array)
                {
                    reason = "ctx." + _ctxScalarKeys[i] + " must be a scalar"; return false;
                }
                string v = t.ToString();
                if (v.Length > 2000)
                {
                    reason = "ctx." + _ctxScalarKeys[i] + " too long (max 2000)"; return false;
                }
            }
            string[] cardKeys = { "alertCard", "resetCard" };
            for (int i = 0; i < cardKeys.Length; i++)
            {
                JToken t = ctx[cardKeys[i]];
                if (t == null || t.Type == JTokenType.Null) { continue; }
                if (t.Type != JTokenType.Object)
                {
                    reason = "ctx." + cardKeys[i] + " must be an object"; return false;
                }
            }
            return true;
        }


        internal static bool CardMentionsDriver(JObject ctx, string dn)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(dn)) { return false; }
            JObject card = ctx["alertCard"] as JObject;
            if (card == null) { return false; }
            try { return card.ToString(Newtonsoft.Json.Formatting.None)
                    .IndexOf(dn.Trim(), StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return false; }
        }


        internal static void WriteAnalysisLink(int frsAlertId, long pendingId, long newId)
        {
            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"];
            if (cs == null || string.IsNullOrEmpty(cs.ConnectionString)) { return; }
            using (var cn = new System.Data.SqlClient.SqlConnection(cs.ConnectionString))
            using (var cmd = new System.Data.SqlClient.SqlCommand(_analysisLinkSql, cn))
            {
                cmd.CommandTimeout = 5;
                cmd.Parameters.Add("@FRSAlertId", System.Data.SqlDbType.Int).Value = frsAlertId;   // 0 => NULLIF -> NULL
                cmd.Parameters.Add("@PendingId", System.Data.SqlDbType.BigInt).Value = pendingId;   // 0 => NULLIF -> NULL
                cmd.Parameters.Add("@NewId", System.Data.SqlDbType.BigInt).Value = newId;
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
