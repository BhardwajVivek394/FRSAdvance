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
    // AnalysisEngine -- Mcp.
    // Moved verbatim from AiChatController.Mcp.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        internal static int ResolveToolOwner(string toolName)
        {
            int owner = 1;
            lock (_toolOwnerLock)
            {
                if (!string.IsNullOrEmpty(toolName) && _toolOwner.ContainsKey(toolName))
                    owner = _toolOwner[toolName];
            }
            return owner;
        }


        // v1.0.160.193: instance recorder for the per-run timing/tag optimization trace. Called from the
        // response-phase LogToolEvent call-sites (which ARE instance context) -- LogToolEvent itself is
        // static and cannot touch these instance fields.
        internal void RecordToolTiming(string toolName, JToken args, string result, int iteration, long elapsedMs)
        {
            if (elapsedMs < 0) { return; }
            if (elapsedMs > Sess._trSlowestToolMs) { Sess._trSlowestToolMs = elapsedMs; Sess._trSlowestTool = toolName ?? ""; }
            if (!string.Equals(toolName, "history_get", StringComparison.OrdinalIgnoreCase)) { return; }
            try
            {
                string tagId = null;
                string argsJson = args != null ? args.ToString(Formatting.None) : "";
                var mm = System.Text.RegularExpressions.Regex.Match(argsJson, "\"TagId\"\\s*:\\s*\"?(\\d+)");
                if (mm.Success) { tagId = mm.Groups[1].Value; }
                string attr = "";
                if (!string.IsNullOrEmpty(tagId)) { JObject md; lock (Sess._trendLock) { Sess._tagMeta.TryGetValue(tagId, out md); } if (md != null && md["attr"] != null) { attr = md["attr"].ToString(); } }
                JObject tf = new JObject();
                tf["tagId"] = tagId ?? "";
                tf["attr"] = attr;
                tf["ms"] = elapsedMs;
                tf["bytes"] = string.IsNullOrEmpty(result) ? 0 : result.Length;
                tf["iter"] = iteration;               // -1 = prefetch, >=0 = in agentic loop
                lock (Sess._trendLock) { Sess._trTagFetches.Add(tf); }
            }
            catch { }
        }


        internal static void LogToolEvent(
            string conversationId,
            string phase,
            int iteration,
            string toolUseId,
            string toolName,
            int owner,
            JToken args,
            string result,
            Exception error,
            long elapsedMs)
        {
            if (!_toolLoggingEnabled) return;

            try
            {
                JObject entry = new JObject();
                entry["tsUtc"] = DateTime.UtcNow.ToString("o");
                entry["conversationId"] = conversationId ?? string.Empty;
                entry["phase"] = phase ?? string.Empty; // requested / response / error
                entry["iteration"] = iteration;
                entry["toolUseId"] = toolUseId ?? string.Empty;
                entry["toolName"] = toolName ?? string.Empty;
                entry["server"] = owner > 0 ? "srv" + owner : string.Empty;
                if (elapsedMs >= 0) entry["elapsedMs"] = elapsedMs;
                string argsJson = args == null
                    ? string.Empty
                    : CloneAndRedact(args).ToString(Formatting.None);

                entry["argsPreview"] = LimitForLog(argsJson, _maxToolLogChars);
                entry["resultLength"] = string.IsNullOrEmpty(result) ? 0 : result.Length;
                entry["resultPreview"] = LimitForLog(result, _maxToolLogChars);
                entry["error"] = error == null ? string.Empty : LimitForLog(error.ToString(), _maxToolLogChars);

                // v1.0.160.78: collect for AI_ALERT_McpCall. Only "response"/"error" phases -- a
                // "requested" entry has no latency or result yet and would double-count every call.
                // Reuses the values ALREADY redacted above rather than re-reading the raw args.
                if (_auditDbEnabled && (phase == "response" || phase == "error"))
                {
                    JObject mc = new JObject();
                    mc["Iteration"] = iteration < 0 ? 0 : iteration;
                    mc["ToolName"] = toolName ?? "";
                    mc["ToolCallId"] = toolUseId ?? "";
                    mc["ToolArgsJson"] = entry["argsPreview"];
                    mc["ToolResultJson"] = entry["resultPreview"];
                    mc["ToolResultLength"] = entry["resultLength"];
                    mc["McpServerName"] = owner > 0 ? ("srv" + owner) : "";
                    mc["CalledAtUtc"] = entry["tsUtc"];
                    if (elapsedMs >= 0) { mc["LatencyMs"] = elapsedMs; }
                    if (error != null) { mc["ErrorMessage"] = entry["error"]; }
                    McpCollect(conversationId, mc);
                }

                System.IO.Directory.CreateDirectory(_toolLogDir);
                string path = System.IO.Path.Combine(
                    _toolLogDir,
                    "ai-tool-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");

                lock (_toolLogLock)
                {
                    System.IO.File.AppendAllText(
                        path,
                        entry.ToString(Formatting.None) + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception logEx)
            {
                System.Diagnostics.Trace.TraceWarning(
                    "[AiChat] Tool log failed: " + logEx.Message);
            }
        }


        internal async Task<ToolLoadResult> LoadAllToolsAsync(System.Threading.CancellationToken ct = default(System.Threading.CancellationToken))
        {
            ct.ThrowIfCancellationRequested();
            if (_toolCacheSec > 0)
            {
                lock (_toolCacheLock)
                {
                    if (_toolCache != null
                        && (DateTime.UtcNow - _toolCacheAt).TotalSeconds < _toolCacheSec)
                    {
                        return _toolCache;
                    }
                }
            }
            ToolLoadResult r = new ToolLoadResult();
            r.Tools = new JArray();
            r.Error1 = null;
            r.Error2 = null;

            JArray tools1 = new JArray();
            try
            {
                tools1 = await AwaitWithCancel(WithClientLock(_mcp1Lock, () => _mcp.Value.ListToolsAsync(), ct, Sess._jsonCapture != null), ct).ConfigureAwait(false);
                r.Count1 = tools1.Count;
            }
            catch (ClientBusyException) { throw; } // surfaces as HTTP 503 busy
            catch (OperationCanceledException ex)
            {
                if (ct.IsCancellationRequested) { throw; } // our cancel only
                r.Error1 = ex.Message; // ordinary client timeout: recoverable (rev2 parity)
            }
            catch (Exception ex)
            {
                r.Error1 = ex.Message;
            }

            ct.ThrowIfCancellationRequested(); // no MCP2 discovery after cancel

            JArray tools2 = new JArray();
            // v1.0.160.20: discover srv2 only when configured. When disabled, tools2 stays empty
            // (Count2 defaults to 0, Error2 stays NULL) so srv2 tools never enter the model tool
            // list / OwnerMap / _toolOwner, nothing routes to owner 2, the srv1 discovery cache is
            // NOT invalidated (it skips when Error2 != null), and no [MCP2 error] is logged or
            // surfaced. Silent, as required. No else branch by design.
            if (_srv2Enabled)
            {
                try
                {
                    tools2 = await AwaitWithCancel(WithClientLock(_mcp2Lock, () => _mcp2.Value.ListToolsAsync(), ct, Sess._jsonCapture != null), ct).ConfigureAwait(false);
                    r.Count2 = tools2.Count;
                }
                catch (ClientBusyException) { throw; } // surfaces as HTTP 503 busy
                catch (OperationCanceledException ex)
                {
                    if (ct.IsCancellationRequested) { throw; } // our cancel only
                    r.Error2 = ex.Message; // ordinary client timeout: recoverable (rev2 parity)
                }
                catch (Exception ex)
                {
                    r.Error2 = ex.Message;
                }
            }

            r.OwnerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            r.ArgMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<JArray> mapArgs = (arr) =>
            {
                foreach (JToken tool in arr)
                {
                    string tn = tool["name"] != null ? tool["name"].ToString() : null;
                    if (string.IsNullOrEmpty(tn) || claimed.Contains(tn)) { continue; }
                    claimed.Add(tn); // claim on FIRST server seen, matching OwnerMap precedence,
                                     // so MCP2 can never supply a schema for an MCP1-owned tool
                    // Only record a tool when discovery actually supplied a
                    // properties object. Present-but-empty => strip everything.
                    // Absent => unknown schema => leave args untouched.
                    JToken sch = tool["inputSchema"] ?? tool["input_schema"];
                    JToken pr = (sch != null && sch.Type == JTokenType.Object) ? sch["properties"] : null;
                    if (pr == null || pr.Type != JTokenType.Object) { continue; }
                    HashSet<string> props = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pp in ((JObject)pr).Properties()) { props.Add(pp.Name); }
                    r.ArgMap[tn] = props;
                }
            };
            mapArgs(tools1); mapArgs(tools2);
            foreach (JToken tool in tools1)
            {
                string name = tool["name"] != null ? tool["name"].ToString() : null;
                if (!string.IsNullOrEmpty(name) && !r.OwnerMap.ContainsKey(name)) { r.OwnerMap[name] = 1; }
            }
            foreach (JToken tool in tools2)
            {
                string name = tool["name"] != null ? tool["name"].ToString() : null;
                if (!string.IsNullOrEmpty(name) && !r.OwnerMap.ContainsKey(name)) { r.OwnerMap[name] = 2; }
            }

            lock (_toolOwnerLock)
            {
                _toolOwner.Clear();
                foreach (JToken tool in tools1)
                {
                    string name = tool["name"] != null ? tool["name"].ToString() : null;
                    if (!string.IsNullOrEmpty(name) && !_toolOwner.ContainsKey(name))
                    {
                        _toolOwner[name] = 1;
                        r.Tools.Add(tool);
                    }
                }
                foreach (JToken tool in tools2)
                {
                    string name = tool["name"] != null ? tool["name"].ToString() : null;
                    if (!string.IsNullOrEmpty(name) && !_toolOwner.ContainsKey(name))
                    {
                        _toolOwner[name] = 2;
                        r.Tools.Add(tool);
                    }
                }
            }

            if (_toolCacheSec > 0 && r.Error1 == null && r.Error2 == null && r.Tools.Count > 0)
            {
                lock (_toolCacheLock) { _toolCache = r; _toolCacheAt = DateTime.UtcNow; }
            }
            return r;
        }

        internal class ClientBusyException : Exception
        {
            public ClientBusyException() : base("client busy") { }
        }


        // SCOPE (corrected in rev 12): serialization applies ONLY to the verdict
        // endpoint (mustOwn = true). Chat / AnalyzeAlert / follow-up / Status keep
        // their original unserialized behaviour -- adding a cross-endpoint lock was
        // my scope creep and it created the rev-10 stall hazard on features that
        // already worked. In verdict mode the lock is MANDATORY: if it cannot be
        // owned within the bounded wait the call is NOT made; the request returns
        // busy. No unserialized singleton call ever happens on that path.
        internal static async Task<T> WithClientLock<T>(System.Threading.SemaphoreSlim gate, Func<Task<T>> body, System.Threading.CancellationToken ct, bool mustOwn)
        {
            if (!mustOwn || !_serializeClients) { return await body().ConfigureAwait(false); }
            bool taken = false;
            try
            {
                taken = await gate.WaitAsync(_clientLockMs, ct).ConfigureAwait(false);
                if (!taken)
                {
                    System.Diagnostics.Trace.TraceWarning("[AiChat] verdict client lock timeout; refusing rather than running unserialized");
                    throw new ClientBusyException();
                }
                ct.ThrowIfCancellationRequested(); // may have been cancelled while waiting
                return await body().ConfigureAwait(false);
            }
            finally
            {
                if (taken) { gate.Release(); }
            }
        }


        internal async Task<string> CallToolRoutedAsync(string toolName, JToken toolArgs, System.Threading.CancellationToken ct = default(System.Threading.CancellationToken), int ownerOverride = 0)
        {
            bool mustOwn = Sess._jsonCapture != null; // verdict mode only
            int owner = ownerOverride > 0 ? ownerOverride : ResolveToolOwner(toolName);

            // v1.0.160.200: the controller owns the E7 token. Fetch the LATEST generation (cached;
            // a login fires only on first use / TTL) and forward it to the MCP on this call so the
            // MCP uses THIS token, never its static startup one. Fetched OUTSIDE the client lock so
            // a login round-trip never holds _mcp1Lock/_mcp2Lock. Never throws: null means the MCP
            // falls back to its own token for this call -- logged, not fatal.
            string e7tok = await E7ApiToken.GetTokenAsync(ct).ConfigureAwait(false);
            if (e7tok == null && !string.IsNullOrEmpty(E7ApiToken.LastError))
            {
                AiLog("WARN", "E7TOKEN", E7ApiToken.LastError);
            }

            // 1.0.167.0: bash removed for all users -- refused before routing, so a model cannot call it by name.
            if (string.Equals(toolName, "bash", StringComparison.OrdinalIgnoreCase))
            {
                AiLog("WARN", "BASH", "bash requested by the model; refused (removed in 1.0.167.0)");
                return "{\"error\":\"tool unavailable (not offered to the assistant)\"}";
            }

            // McpClient and McpSseClient have the same method signature
            string __routedResult;
            if (owner == 2 && _srv2Enabled)
                __routedResult = await AwaitWithCancel(WithClientLock(_mcp2Lock, () => _mcp2.Value.CallToolAsync(toolName, toolArgs, _maxToolLen, e7tok), ct, mustOwn), ct).ConfigureAwait(false);
            else if (owner == 2)
                // v1.0.160.20: srv2 tool requested while srv2 is disabled. It should never be
                // advertised (tools2 empty), so this is defensive only: a clean unavailable result
                // (LooksUsableToolResult rejects an object carrying an "error") instead of touching
                // the disabled client.
                __routedResult = "{\"error\":\"tool unavailable (srv2 disabled)\"}";
            else
                // 1.0.167.0: braces-free single statement again. Until now a bash-budget "if" sat between this
                // else and the server-1 call, so the server-1 call ran after EVERY branch and overwrote the
                // server-2 result and the "srv2 disabled" result.
                __routedResult = await AwaitWithCancel(WithClientLock(_mcp1Lock, () => _mcp.Value.CallToolAsync(toolName, toolArgs, _maxToolLen, e7tok), ct, mustOwn), ct).ConfigureAwait(false);
            // v1.0.160.200: reactive path. The MCP reports E7 rejected the forwarded token ->
            // invalidate it so the NEXT tool call re-logs in (single-flight). No in-place retry:
            // this call returns the MCP's error result, which the loop already treats as unusable,
            // and every subsequent call carries a fresh token.
            if (e7tok != null && IsE7SessionExpired(__routedResult))
            {
                E7ApiToken.Invalidate();
                AiLog("WARN", "E7TOKEN", "E7 session expired on '" + toolName + "'; token invalidated, next call re-logs in");
            }

            // v1.0.146.0: single funnel for ALL tool calls (prefetch + loop + chat) --
            // harvest identity + real trend here so every path contributes.
            TryHarvestGrounding(toolName, __routedResult);
            TryHarvestTrend(toolName, toolArgs, __routedResult);
            return __routedResult;
        }


        // v1.0.160.200: recognizes the marker the MCP server returns when E7 rejects the forwarded
        // token (contract: the MCP emits "e7_session_expired" on an E7 401/403). Marker-only on
        // purpose -- generic "403"/"forbidden" text is NOT matched, so an unrelated permission
        // error can never churn logins.
        internal static bool IsE7SessionExpired(string toolResult)
        {
            if (string.IsNullOrEmpty(toolResult))
            {
                return false;
            }
            return toolResult.IndexOf("e7_session_expired", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // NOTE: reuses the existing _istOffset (declared near ToEpochIst) -- it was added for exactly
        // this reason ("ToUniversalTime() silently used the SERVER's timezone"). Do not redeclare it.

        internal static string HistStamp(DateTime ist)
        {
            // EXPLICIT IST -> UTC. Never ToUniversalTime(): that uses the AiChat host's locale, which
            // is not necessarily the EdgeX host's, and never Kind.Local for the same reason.
            DateTime t = _histGetSendsUtc ? ist - _istOffset : ist;
            return t.ToString(_ddFmt);
        }


        internal static JObject HistWindowArgs(string key, string val, DateTime fromIst, DateTime toIst,
            int limit, string outputMode)
        {
            JObject o = NewArgs(key, val);
            o["StartDate"] = HistStamp(fromIst);
            o["EndDate"] = HistStamp(toIst);
            o["limit"] = limit;
            // outputMode is a PARAMETER, not a constant: the AssetId probe needs JSON because
            // PickAlertingTagId scans the response for the literal field name "TagID".
            if (!string.IsNullOrEmpty(outputMode)) { o["outputMode"] = outputMode; }
            // v1.0.160.38: under a row cap keep the NEWEST rows -- the incident sits near the END of
            // the window. Omitting this is what made the derived-component fetch read a 32 h stale
            // sample as if it were the value at the alert.
            o["sort"] = "desc";
            return o;
        }


        internal static string HistCacheKey(JObject args)
        {
            if (args == null) { return ""; }
            string tgt = args["TagId"] != null ? ("T" + args["TagId"])
                       : (args["AssetId"] != null ? ("A" + args["AssetId"]) : "?");
            return tgt + "|" + (args["StartDate"] != null ? args["StartDate"].ToString() : "")
                 + "|" + (args["EndDate"] != null ? args["EndDate"].ToString() : "")
                 + "|" + (args["limit"] != null ? args["limit"].ToString() : "")
                 + "|" + (args["sort"] != null ? args["sort"].ToString() : "")
                 + "|" + (args["outputMode"] != null ? args["outputMode"].ToString() : "");
        }


        // Single entry point for every history_get fetch: dedupes identical requests within a run and
        // checks the window the server actually applied.
        internal async Task<string> HistFetchAsync(JObject args, ToolLoadResult loaded,
            System.Threading.CancellationToken ct, bool quiet = false)
        {
            string key = HistCacheKey(args);
            string hit;
            if (key.Length > 0 && Sess._histCache.TryGetValue(key, out hit))
            {
                Sess._histCacheHits++;
                return hit;
            }
            string res = await SafeAwait(PrefetchCallAsync("history_get", args, loaded, ct, quiet)).ConfigureAwait(false);
            if (key.Length > 0) { Sess._histCache[key] = res; }
            CheckHistWindowEcho(args, res);
            return res;
        }


        // The server echoes the from/to it actually used. If that disagrees with what we asked for,
        // the compensation or its configuration has failed and the evidence is for the wrong window.
        // Logged once per run; the decision to REJECT such a block is taken by the caller/verdict path.
        internal void CheckHistWindowEcho(JObject args, string res)
        {
            if (Sess._histSkewLogged || string.IsNullOrEmpty(res)) { return; }
            try
            {
                DateTime want;
                if (args == null || args["StartDate"] == null) { return; }
                if (!DateTime.TryParseExact(args["StartDate"].ToString(), _ddFmt,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out want)) { return; }
                int i = res.IndexOf("\"from\"", StringComparison.Ordinal);
                if (i < 0) { return; }
                int c = res.IndexOf(':', i); if (c < 0) { return; }
                int e = c + 1; while (e < res.Length && (res[e] == ' ')) { e++; }
                int st = e; while (e < res.Length && (char.IsDigit(res[e]))) { e++; }
                long epoch;
                if (e <= st || !long.TryParse(res.Substring(st, e - st), out epoch)) { return; }
                DateTime got = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(epoch);
                // v1.0.160.134: the echo is a UTC epoch; `want` is the IST WALL-CLOCK string we sent.
                // Comparing them directly was right only while we pre-converted to UTC
                // (HistGetSendsUtc=true). After the EdgeX 1.5.57.16 contract and the flag flip the
                // server reads our string as IST, so the comparison must add the IST offset. Without
                // this the assertion reports exactly the 330 min it was built to detect -- on windows
                // that are correct. It fired 7 times in one day's log. The check had to be flipped
                // WITH the flag; I flipped the flag and left the check behind.
                DateTime gotWall = _histGetSendsUtc ? got : got.Add(_istOffset);
                double diffMin = Math.Abs((gotWall - DateTime.SpecifyKind(want, DateTimeKind.Utc)).TotalMinutes);
                if (diffMin > 2)
                {
                    Sess._histSkewLogged = true;
                    AiLog("WARN", "HIST_WINDOW_SKEW", "requested " + args["StartDate"]
                        + " but the server applied a window " + Math.Round(diffMin) + " min away"
                        + " (HistGetSendsUtc=" + _histGetSendsUtc + ") -- history evidence may be for the wrong period");
                }
            }
            catch { }
        }


        internal static void McpCollect(string runId, JObject entry)
        {
            try
            {
                if (!_auditDbEnabled || string.IsNullOrEmpty(runId) || entry == null) { return; }
                // prune anything abandoned by a run that never completed
                DateTime cut = DateTime.UtcNow.AddMinutes(-30);
                foreach (KeyValuePair<string, Tuple<DateTime, List<JObject>>> kv in _mcpCollect)
                {
                    if (kv.Value != null && kv.Value.Item1 < cut)
                    { Tuple<DateTime, List<JObject>> gone; _mcpCollect.TryRemove(kv.Key, out gone); }
                }
                Tuple<DateTime, List<JObject>> slot = _mcpCollect.GetOrAdd(runId,
                    k => new Tuple<DateTime, List<JObject>>(DateTime.UtcNow, new List<JObject>()));
                lock (slot.Item2)
                {
                    if (slot.Item2.Count < _mcpMaxPerRun) { slot.Item2.Add(entry); }
                }
            }
            catch { }
        }


        internal static List<JObject> McpDrain(string runId)
        {
            try
            {
                Tuple<DateTime, List<JObject>> slot;
                if (string.IsNullOrEmpty(runId) || !_mcpCollect.TryRemove(runId, out slot) || slot == null) { return null; }
                return slot.Item2;
            }
            catch { return null; }
        }


        internal async Task<string> HistoryPageToEndAsync(string tool, JToken args, string page1, System.Threading.CancellationToken ct, int owner)
        {
            try
            {
                JObject a = args as JObject;
                if (a == null || a["limit"] == null) { return page1; }
                int lim;
                if (!int.TryParse(a["limit"].ToString(), out lim) || lim <= 0) { return page1; }

                int n = 0; long firstTs = 0, lastTs = 0, winTo = 0;
                if (!HistorySpan(page1, out n, out firstTs, out lastTs, out winTo)) { return page1; }
                if (n < 2 || winTo <= lastTs) { return page1; }

                double spanSec = (double)(lastTs - firstTs);
                if (spanSec <= 0) { return page1; }
                double ratePerSec = (double)(n - 1) / spanSec;
                double rowsToEnd = ratePerSec * (double)(winTo - lastTs);
                int guess = 1 + (int)Math.Ceiling(rowsToEnd / lim);
                if (guess < 2) { guess = 2; }

                string best = null; long bestLast = lastTs; int bestPage = 1;
                int probes = 0, pg = guess;
                while (probes < _histMaxPageProbes && pg >= 2)
                {
                    probes++;
                    JObject pArgs = (JObject)a.DeepClone();
                    pArgs["page"] = pg;
                    string pRes = await CallToolRoutedAsync(tool, pArgs, ct, owner).ConfigureAwait(false);
                    // Initialised at declaration: HistorySpan sits behind a short-circuiting &&, so
                    // the compiler cannot prove these are assigned on the path where pRes is empty.
                    int pn = 0; long pFirst = 0, pLast = 0, pTo = 0;
                    bool ok = !string.IsNullOrEmpty(pRes) && HistorySpan(pRes, out pn, out pFirst, out pLast, out pTo);
                    AiLog("INFO", "HIST", "page probe " + pg + " -> " + (ok ? ("n=" + pn) : "no rows"));
                    if (!ok || pn == 0)
                    {
                        pg--;                                   // overshot the data -- step back
                        if (pg < 2) { break; }
                        continue;
                    }
                    if (pLast > bestLast) { best = pRes; bestLast = pLast; bestPage = pg; }
                    if (pn < lim) { break; }                    // short page = last page
                    if (winTo - pLast <= 3600) { break; }       // reached the window end
                    pg++;                                       // still short -- go forward
                }

                if (best != null)
                {
                    AiLog("INFO", "HIST", "PAGED to page " + bestPage + " for " + tool
                        + " -- series now ends " + Math.Round((double)(winTo - bestLast) / 3600.0, 1)
                        + " h before the window end (page 1 ended "
                        + Math.Round((double)(winTo - lastTs) / 3600.0, 1) + " h before it)");
                    return best;
                }
                AiLog("WARN", "HIST", "paging found no later rows for " + tool + "; keeping page 1");
            }
            catch (Exception pex) { AiLog("WARN", "HIST", "paging failed: " + pex.Message); }
            return page1;
        }


        // n, first/last sample epoch and the requested window end, from a columnar history payload.
        internal static bool HistorySpan(string res, out int n, out long firstTs, out long lastTs, out long winTo)
        {
            n = 0; firstTs = 0; lastTs = 0; winTo = 0;
            try
            {
                if (string.IsNullOrEmpty(res)) { return false; }
                System.Text.RegularExpressions.Match mn = _histNRx.Match(res);
                if (!mn.Success || !int.TryParse(mn.Groups[1].Value, out n)) { return false; }
                System.Text.RegularExpressions.Match mto = _histToRx.Match(res);
                if (mto.Success) { long.TryParse(mto.Groups[1].Value, out winTo); }
                System.Text.RegularExpressions.Match mts = _histTsRx.Match(res);
                if (!mts.Success) { return false; }
                string arr = mts.Groups[1].Value;
                if (string.IsNullOrEmpty(arr.Trim())) { return false; }
                // v1.0.160.65: MIN and MAX, not first and last. Taking position for order was only ever
                // safe while every series arrived ascending; 160.38 added sort=desc to the TPR fetch and
                // silently inverted the meaning of "last". min/max is correct under any ordering.
                string[] toks = arr.Split(new char[] { ',' });
                long lo = long.MaxValue, hi = long.MinValue; int parsed = 0;
                for (int i = 0; i < toks.Length; i++)
                {
                    long tv;
                    if (!long.TryParse(toks[i].Trim(), out tv) || tv <= 0) { continue; }
                    if (tv < lo) { lo = tv; }
                    if (tv > hi) { hi = tv; }
                    parsed++;
                }
                if (parsed == 0) { return false; }
                firstTs = lo; lastTs = hi;
                return lastTs > 0;
            }
            catch { }
            return false;
        }


        internal bool NoteHistoryTruncation(string tool, JToken args, string res)
        {
            try
            {
                if (string.IsNullOrEmpty(res) || !IsHistoryFamily(tool)) { return false; }
                JObject a = args as JObject;
                if (a == null || a["limit"] == null) { return false; }
                int lim;
                if (!int.TryParse(a["limit"].ToString(), out lim) || lim <= 0) { return false; }
                System.Text.RegularExpressions.Match mn = _histNRx.Match(res);
                if (!mn.Success) { return false; }
                int n;
                if (!int.TryParse(mn.Groups[1].Value, out n)) { return false; }
                string tagId = "";
                System.Text.RegularExpressions.Match mt = _histTagRx.Match(res);
                if (mt.Success) { tagId = mt.Groups[1].Value; }
                string attr = "";
                lock (Sess._trendLock)
                {
                    if (!string.IsNullOrEmpty(tagId) && Sess._tagMeta.ContainsKey(tagId))
                    { attr = (string)Sess._tagMeta[tagId]["attr"] ?? ""; }
                }
                string who = string.IsNullOrEmpty(attr) ? ("tag " + tagId) : attr;
                // v1.0.158.4: n >= requested limit was the WRONG test. The API enforces its own hard
                // cap (500) BELOW what we ask for, so a cut series returns n=500 against limit=5000
                // and the old check never fired. The real signature is the DATA ENDING EARLY: a
                // substantial series whose last sample sits well before the requested window end.
                long lastTs = 0, winTo = 0;
                System.Text.RegularExpressions.Match mto = _histToRx.Match(res);
                if (mto.Success) { long.TryParse(mto.Groups[1].Value, out winTo); }
                System.Text.RegularExpressions.Match mts = _histTsRx.Match(res);
                if (mts.Success)
                {
                    // v1.0.160.66: MIN/MAX, not the last token. This parser is SEPARATE from
                    // HistorySpan (fixed in 160.65) and prints the line operators actually read, so
                    // the sort=desc bug survived here: TPR reported a 50 h gap that did not exist.
                    string arr = mts.Groups[1].Value;
                    string[] tks = arr.Split(new char[] { ',' });
                    long hiTs = 0;
                    for (int ti = 0; ti < tks.Length; ti++)
                    {
                        long tv;
                        if (long.TryParse(tks[ti].Trim(), out tv) && tv > hiTs) { hiTs = tv; }
                    }
                    // The original took the LAST token, which is the OLDEST sample once sort=desc is
                    // used. Those lines are DELETED, not merely preceded -- in 160.66 they still ran
                    // after this and overwrote the value.
                    lastTs = hiTs;
                }
                double shortBySec = (winTo > 0 && lastTs > 0) ? (double)(winTo - lastTs) : 0;
                // A sparse digital tag (TPR: 164 change-records, quiet for hours overnight) also ends
                // "early" and must NOT be called truncated. What marks a CUT series is hitting the
                // API's own row cap: n at or near _histCapHint (measured 500) while data stops short.
                bool cut = (n >= lim) || (n >= (_histCapHint - (_histCapHint / 10)) && shortBySec > 3600);
                if (cut)
                {
                    if (!string.IsNullOrEmpty(attr)) { lock (Sess._trendLock) { Sess._truncatedAttrs.Add(attr); } }
                    AiLog("WARN", "HIST", "TRUNCATED " + who + ": n=" + n + " (asked " + lim
                        + "), last sample is " + Math.Round(shortBySec / 3600.0, 1)
                        + " h BEFORE the window end -- default asc returns the OLDEST rows up to the"
                        + " cap; re-query with sort=desc to reach the recent end.");
                }
                else
                {
                    AiLog("INFO", "HIST", who + ": n=" + n + " of limit " + lim + ", ends "
                        + Math.Round(shortBySec / 3600.0, 1) + " h before window end (not truncated)"
                        + " [span=min/max, order-agnostic since 1.0.160.65]");
                }
                return cut;
            }
            catch { }
            return false;
        }


        internal static JToken ClampToolArgs(string toolName, JToken args, HashSet<string> declared)
        {
            // declared == null means "schema unknown": inject nothing, only clamp
            // values already present. Never send an argument a tool did not declare.
            // Limit policy: the HISTORY family carries the evidence the verdict
            // rests on (sustained vs transient), so it gets full scale up to the
            // API max. In columnar output ~500 samples is about 10 KB, whereas the
            // old cap of 10 gave roughly one sample per 17 hours over a 7-day
            // window -- far too sparse to judge persistence. Discovery/search
            // tools stay capped small.
            bool histFamily = string.Equals(toolName, "history_get", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(toolName, "trend_get", StringComparison.OrdinalIgnoreCase);
            // search_tags is a DISCOVERY tool: its rows are ~120 bytes and the caller
            // needs to see ALL of an asset's attributes to find the alerting one. The
            // server default is 3 (observed: a track asset returned only Charger tags,
            // no Ir/If, so the alerting attribute could never be matched) and the
            // generic cap of 10 was not enough either -- a track circuit has ~11 tags.
            bool breadthTool = string.Equals(toolName, "search_tags", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(toolName, "search_assets", StringComparison.OrdinalIgnoreCase);
            int limitCap = histFamily ? _limitCapEvidence : (breadthTool ? 60 : _limitCapOther);
            JObject o = args as JObject;
            if (o == null) { o = new JObject(); }
            bool sawLimit = false;
            List<string> names = new List<string>();
            foreach (var prop in o.Properties()) { names.Add(prop.Name); }
            for (int i2 = 0; i2 < names.Count; i2++)
            {
                string lower = names[i2].ToLowerInvariant();
                for (int k = 0; k < _limitKeys.Length; k++)
                {
                    if (lower == _limitKeys[k])
                    {
                        sawLimit = true;
                        o[names[i2]] = ClampDecimalToInt(o[names[i2]], 1, limitCap, limitCap);
                        break;
                    }
                }
                for (int k = 0; k < _skipKeys.Length; k++)
                {
                    if (lower == _skipKeys[k])
                    {
                        o[names[i2]] = ClampDecimalToInt(o[names[i2]], 0, 90, 0);
                        break;
                    }
                }
                JToken val = o[names[i2]];
                if (val != null && val.Type == JTokenType.Array && ((JArray)val).Count > 20)
                {
                    JArray src = (JArray)val;
                    JArray cut = new JArray();
                    for (int k = 0; k < 20; k++) { cut.Add(src[k]); }
                    o[names[i2]] = cut; // ID lists bounded
                }
            }
            if (histFamily && !sawLimit && declared != null && declared.Contains("limit"))
            {
                o["limit"] = _limitCapEvidence; // full scale (history_get: MCPHelper.cs:397)
            }
            // outputMode: pin to "columnar" where the tool DECLARES it.
            // Per the DataAPI README, columnar IS the token-efficient AI format
            // (cols/rows positional arrays, epoch timestamps) and it is still
            // JSON -- so it satisfies the evidence JSON requirement while being
            // far cheaper than "json" named-key output. It is also the MCP
            // default, so this only prevents the model from asking for "csv"
            // (which is NOT JSON and would fail grounding).
            // Undeclared tools get nothing injected (rev 9 regression fix).
            if (IsEvidenceTool(toolName) && declared != null && declared.Contains("outputMode"))
            {
                string omName = null;
                for (int i2 = 0; i2 < names.Count; i2++)
                {
                    if (string.Equals(names[i2], "outputMode", StringComparison.OrdinalIgnoreCase)) { omName = names[i2]; break; }
                }
                o[omName ?? "outputMode"] = "columnar";
            }
            // drop model-invented arguments the tool never declared
            if (declared != null) // present-but-empty strips everything
            {
                List<string> strip = new List<string>();
                foreach (var prop in o.Properties())
                {
                    if (!declared.Contains(prop.Name)) { strip.Add(prop.Name); }
                }
                for (int k = 0; k < strip.Count; k++) { o.Remove(strip[k]); }
            }
            string startName = null, endName = null;
            for (int i2 = 0; i2 < names.Count; i2++)
            {
                string lower = names[i2].ToLowerInvariant();
                if (startName == null) { for (int k = 0; k < _startKeys.Length; k++) { if (lower == _startKeys[k]) { startName = names[i2]; break; } } }
                if (endName == null) { for (int k = 0; k < _endKeys.Length; k++) { if (lower == _endKeys[k]) { endName = names[i2]; break; } } }
            }
            // only rewrite date formats we can reproduce EXACTLY: ddMMyyyy_HHmmss
            // or plain "yyyy-MM-dd HH:mm:ss". Any other format (ISO with T/offset/
            // fractions, etc.) is left untouched -- never corrupted (residual:
            // those windows stay unclamped pending per-tool schemas).
            DateTime sd, ed; bool sdDd, edDd;
            string sRaw = startName != null ? (o[startName] ?? "").ToString() : null;
            string eRaw = endName != null ? (o[endName] ?? "").ToString() : null;
            bool sPlain = sRaw != null && System.Text.RegularExpressions.Regex.IsMatch(sRaw, "^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}$");
            if (startName != null && endName != null
                && TryParseFlexibleDate(sRaw, out sd, out sdDd)
                && TryParseFlexibleDate(eRaw, out ed, out edDd)
                && (ed - sd).TotalDays > 15
                && (sdDd || sPlain))
            {
                DateTime ns = ed.AddDays(-15);
                o[startName] = sdDd ? ns.ToString(_ddFmt) : ns.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else if (startName != null && endName == null
                && TryParseFlexibleDate(sRaw, out sd, out sdDd)
                && (DateTime.Now - sd).TotalDays > 15
                && (sdDd || sPlain))
            {
                DateTime ns = DateTime.Now.AddDays(-15);
                o[startName] = sdDd ? ns.ToString(_ddFmt) : ns.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return o;
        }

        internal static bool IsMetadataKey(string name)
        {
            string ln = (name ?? "").ToLowerInvariant();
            for (int k = 0; k < _metaKeys.Length; k++) { if (ln == _metaKeys[k]) { return true; } }
            return false;
        }


        internal static bool IsErrorObject(JObject jo)
        {
            if (Truthy(PropCI(jo, "isError")) || Truthy(PropCI(jo, "is_error"))) { return true; }
            JToken succ = PropCI(jo, "success");
            if (succ != null && succ.Type != JTokenType.Null
                && succ.Type != JTokenType.Object && succ.Type != JTokenType.Array
                && !Truthy(succ)) { return true; } // covers success:0 and success:"0"
            if (PropCI(jo, "error") != null && PropCI(jo, "error").Type != JTokenType.Null) { return true; }
            JToken st = PropCI(jo, "status");
            if (st != null && st.Type == JTokenType.String)
            {
                string sv = st.ToString();
                for (int k = 0; k < _statusFailWords.Length; k++)
                {
                    if (string.Equals(sv, _statusFailWords[k], StringComparison.OrdinalIgnoreCase)) { return true; }
                }
            }
            string[] scKeys = { "statusCode", "status_code", "httpStatus", "http_status", "code" };
            for (int k = 0; k < scKeys.Length; k++)
            {
                JToken sc = PropCI(jo, scKeys[k]);
                if (sc != null && sc.Type != JTokenType.Null && sc.Type != JTokenType.Object && sc.Type != JTokenType.Array)
                {
                    decimal cv; // string "500" counts as well as numeric 500
                    if (decimal.TryParse(sc.ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out cv) && cv >= 400m) { return true; }
                }
            }
            JToken msg = PropCI(jo, "message");
            if (msg != null && msg.Type == JTokenType.String)
            {
                string ml = msg.ToString().ToLowerInvariant();
                for (int k = 0; k < _noDataPhrases.Length; k++)
                {
                    if (ml.Contains(_noDataPhrases[k])) { return true; }
                }
            }
            JToken ok = PropCI(jo, "ok");
            if (ok != null && (ok.Type == JTokenType.Boolean || ok.Type == JTokenType.String)
                && !Truthy(ok) && ok.Type != JTokenType.Null
                && (ok.Type == JTokenType.Boolean || string.Equals(ok.ToString(), "false", StringComparison.OrdinalIgnoreCase)))
            { return true; }
            return false;
        }


        // element substance is RECURSIVE (depth-capped): [[null]] has none
        internal static bool ElementHasSubstance(JToken e, int depth)
        {
            if (depth < 0) { return false; }
            if (e == null || e.Type == JTokenType.Null || e.Type == JTokenType.Boolean) { return false; }
            if (e.Type == JTokenType.Object)
            {
                JObject o = (JObject)e;
                if (o.Count == 0 || IsErrorObject(o)) { return false; }
                // a row must carry at least one non-null, non-metadata value:
                // {"x":null} and {"message":"..."} are not measurements
                foreach (var prop in o.Properties())
                {
                    JToken v = prop.Value;
                    if (v == null || v.Type == JTokenType.Null) { continue; }
                    if (v.Type == JTokenType.Boolean) { continue; }
                    if (IsMetadataKey(prop.Name)) { continue; } // ids/timestamps/status are not measurements
                    if (v.Type == JTokenType.Array) { if (ArrayHasSubstance((JArray)v, depth - 1)) { return true; } continue; }
                    if (v.Type == JTokenType.Object) { if (ElementHasSubstance(v, depth - 1)) { return true; } continue; }
                    string sv2 = v.ToString().Trim();
                    if (sv2.Length == 0) { continue; }
                    if (v.Type == JTokenType.String)
                    {
                        string lv = sv2.ToLowerInvariant();
                        bool nodata = false;
                        for (int k2 = 0; k2 < _noDataPhrases.Length; k2++)
                        {
                            if (lv.Contains(_noDataPhrases[k2])) { nodata = true; break; }
                        }
                        if (nodata) { continue; }
                    }
                    return true;
                }
                return false;
            }
            if (e.Type == JTokenType.Array) { return ArrayHasSubstance((JArray)e, depth - 1); }
            return e.ToString().Trim().Length > 0;
        }


        internal static bool ArrayHasSubstance(JArray a, int depth)
        {
            // POSITIONAL ROW heuristic: an array of only scalars is a columnar
            // row whose first element is conventionally the timestamp/offset, so
            // it alone is not a measurement -- require a non-null value at index
            // >= 1. (Result schemas would replace this heuristic with real
            // column semantics; it fails SAFE toward ungrounded.)
            bool allScalar = a.Count > 0; bool anyComposite = false;
            for (int k = 0; k < a.Count; k++)
            {
                JTokenType tt = a[k] == null ? JTokenType.Null : a[k].Type;
                if (tt == JTokenType.Object || tt == JTokenType.Array) { allScalar = false; anyComposite = true; }
            }
            if (anyComposite)
            {
                // MIXED row (e.g. [ts, {"value":null}]): scalars in a composite row
                // are positional identifiers, so only composite members can supply
                // substance.
                for (int k = 0; k < a.Count; k++)
                {
                    JTokenType tt2 = a[k] == null ? JTokenType.Null : a[k].Type;
                    if (tt2 != JTokenType.Object && tt2 != JTokenType.Array) { continue; }
                    if (ElementHasSubstance(a[k], depth)) { return true; }
                }
                return false;
            }
            if (allScalar)
            {
                for (int k = 1; k < a.Count; k++)
                {
                    JToken e = a[k];
                    if (e == null || e.Type == JTokenType.Null || e.Type == JTokenType.Boolean) { continue; }
                    string sv = e.ToString().Trim();
                    if (sv.Length == 0) { continue; }
                    // a measurement is numeric: also rejects header rows like
                    // ["ts","value"] and placeholders like "N/A"
                    decimal nv;
                    if (!decimal.TryParse(sv, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out nv)) { continue; }
                    return true;
                }
                return false;
            }
            for (int k = 0; k < a.Count; k++)
            {
                if (ElementHasSubstance(a[k], depth)) { return true; }
            }
            return false;
        }


        internal static void ScanDataKeys(JObject jo, int depth, ref bool saw, ref bool good)
        {
            if (depth < 0) { return; }
            foreach (var prop in jo.Properties())
            {
                string ln = prop.Name.ToLowerInvariant();
                JToken v = prop.Value;
                bool isDataKey = false;
                for (int k = 0; k < _dataKeys.Length; k++) { if (ln == _dataKeys[k]) { isDataKey = true; break; } }
                if (isDataKey && v != null && v.Type == JTokenType.Array)
                {
                    saw = true;
                    if (ArrayHasSubstance((JArray)v, 2)) { good = true; }
                }
                else if (v != null && v.Type == JTokenType.Object)
                {
                    // an error-marked wrapper cannot supply evidence, whatever it contains
                    if (IsErrorObject((JObject)v)) { saw = true; continue; }
                    ScanDataKeys((JObject)v, depth - 1, ref saw, ref good);
                }
            }
        }


        // The in-hand server wraps every tool outcome as
        //   McpToolResult { "content":[{"text":...}], "isError": true|absent }
        // inside a JSON-RPC SUCCESS result (MCPHelper.cs:59-68, :925;
        // MCPServerProject.HandleToolCallAsync). Unwrap that shape wherever it
        // appears (root, or under "result"), reject on isError at ANY level,
        // and re-judge extracted content text recursively -- so
        // "Tool execution failed: <raw>" dies whether the client hands us the
        // raw envelope, the result object, or the unwrapped text.
        internal static bool JudgeMcpShape(JObject jo, bool requireJson, int depth, out bool handled)
        {
            handled = false;
            if (depth < 0) { return false; }
            // ROOT error marker decides FIRST: a failure envelope cannot be
            // rescued by well-formed content nested under "result".
            if (IsErrorObject(jo)) { handled = true; return false; }
            JToken res = PropCI(jo, "result");
            if (res != null && res.Type == JTokenType.Object)
            {
                bool h2;
                bool ok = JudgeMcpShape((JObject)res, requireJson, depth - 1, out h2);
                if (h2) { handled = true; return ok; }
            }
            JToken content = PropCI(jo, "content");
            if (content != null && content.Type == JTokenType.Array)
            {
                handled = true;
                JArray ca = (JArray)content;
                StringBuilder sb = new StringBuilder();
                for (int k = 0; k < ca.Count; k++)
                {
                    if (ca[k] == null || ca[k].Type != JTokenType.Object) { continue; }
                    JToken tx = PropCI((JObject)ca[k], "text");
                    if (tx != null && tx.Type == JTokenType.String) { sb.Append(tx.ToString()); }
                }
                string inner = sb.ToString().Trim();
                if (inner.Length == 0) { return false; }
                return LooksUsableInner(inner, requireJson, depth - 1);
            }
            return false;
        }


        // The MCP transport can hand back SEVERAL concatenated JSON documents in
        // one string -- one per content block. JToken.Parse rejects that outright
        // ("additional text after finished reading"), which looked exactly like
        // truncation even though head and tail were both well formed. Parse
        // permissively so every document is judged on its own.
        internal static List<JToken> ParseJsonDocuments(string r)
        {
            List<JToken> parts = new List<JToken>();
            try { parts.Add(JToken.Parse(r)); return parts; }
            catch { parts.Clear(); }
            try
            {
                using (System.IO.StringReader sr = new System.IO.StringReader(r))
                using (JsonTextReader jr = new JsonTextReader(sr))
                {
                    jr.SupportMultipleContent = true;
                    while (jr.Read())
                    {
                        JToken t = JToken.ReadFrom(jr);
                        if (t != null) { parts.Add(t); }
                        if (parts.Count >= 25) { break; }
                    }
                }
            }
            catch { /* keep whatever parsed cleanly before the fault */ }
            if (parts.Count > 0) { return parts; }

            // TRANSPORT TRUNCATION. The MCP client splices a human-readable marker
            //   [...234311 chars truncated - narrow the query]
            // into the MIDDLE of the JSON, keeping a head and a tail. Head and tail
            // both look well formed, so this presented as mysterious corruption.
            // Salvage the HEAD: cut back to the last completed element and close the
            // open containers, which yields valid JSON holding the earliest samples
            // -- the oldest part of the window, which is what a trend needs.
            JToken repaired = RepairTruncatedJson(r);
            if (repaired != null) { parts.Add(repaired); return parts; }

            // Last resort: LINE-DELIMITED documents. Observed in the wild -- a tool
            // result arrives as a TRUNCATED document followed on a later line by a
            // COMPLETE one. A sequential reader aborts on the broken first document
            // and never reaches the intact copy, so the whole payload looked
            // corrupt. Judge each line independently and keep the ones that parse.
            try
            {
                string[] lines = r.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length < 3) { continue; }
                    if (ln[0] != '[' && ln[0] != '{') { continue; }
                    try { parts.Add(JToken.Parse(ln)); }
                    catch { /* truncated fragment: skip it */ }
                    if (parts.Count >= 25) { break; }
                }
            }
            catch { }
            return parts;
        }


        internal static JToken RepairTruncatedJson(string r)
        {
            if (string.IsNullOrEmpty(r)) { return null; }
            System.Text.RegularExpressions.Match m = _truncMarker.Match(r);
            if (!m.Success) { return null; }
            string head = r.Substring(0, m.Index);
            if (head.Length < 8) { return null; }

            // walk the head, remembering the last index at which a NESTED element
            // finished -- that is the last point we can cut without splitting a value
            List<char> stack = new List<char>();
            bool inStr = false, esc = false;
            int lastSafe = -1;
            for (int i = 0; i < head.Length; i++)
            {
                char c = head[i];
                if (esc) { esc = false; continue; }
                if (inStr && c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) { continue; }
                if (c == '{' || c == '[') { stack.Add(c); }
                else if (c == '}' || c == ']')
                {
                    if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); }
                    if (stack.Count > 0) { lastSafe = i; }
                }
            }
            if (lastSafe < 0) { return null; }

            string cut = head.Substring(0, lastSafe + 1);
            stack.Clear(); inStr = false; esc = false;
            for (int i = 0; i < cut.Length; i++)
            {
                char c = cut[i];
                if (esc) { esc = false; continue; }
                if (inStr && c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) { continue; }
                if (c == '{' || c == '[') { stack.Add(c); }
                else if (c == '}' || c == ']') { if (stack.Count > 0) { stack.RemoveAt(stack.Count - 1); } }
            }
            StringBuilder sb = new StringBuilder(cut);
            for (int i = stack.Count - 1; i >= 0; i--) { sb.Append(stack[i] == '{' ? '}' : ']'); }
            try { return JToken.Parse(sb.ToString()); }
            catch { return null; }
        }


        internal static bool JudgeJsonToken(JToken t, bool requireJson, int depth)
        {
            if (t == null) { return false; }
            return LooksUsableInner(t.ToString(Formatting.None), requireJson, depth);
        }


        // v1.0.160.153: the MCP server returns E7 API failures (403 session-expired etc.) as
        // PLAIN-TEXT tool RESULTS, not tool errors. Detect them by their fixed prefixes.
        internal static bool IsE7ApiFailureText(string s)
        {
            if (s == null)
            {
                return false;
            }
            string t = s.TrimStart();
            return t.StartsWith("E7 API returned ", StringComparison.Ordinal)
                || t.StartsWith("E7 API call failed", StringComparison.Ordinal);
        }


        // v1.0.160.153: name the failure in the log, mark the run degraded, and hand the caller
        // null so every consumer takes its existing missing-input path.
        internal string DropE7ApiFailure(string result, string toolName)
        {
            if (!IsE7ApiFailureText(result))
            {
                return result;
            }
            string one = result.Trim();
            if (one.Length > 140)
            {
                one = one.Substring(0, 140);
            }
            Sess._e7ApiInputFailed = true;
            AiLog("WARN", "PREFETCH", toolName + " returned an E7 API failure -- treated as MISSING input: " + one);
            return null;
        }


        internal static bool LooksUsableToolResult(string result, bool requireJson)
        {
            return LooksUsableInner(result, requireJson, 2);
        }


        internal static bool LooksUsableInner(string result, bool requireJson, int depth)
        {
            if (result == null || depth < 0) { return false; }
            string r = result.Trim();
            if (r.Length < 3) { return false; }
            if (r.StartsWith("Tool '", StringComparison.Ordinal)) { return false; }
            if (r.StartsWith("<", StringComparison.Ordinal)) { return false; } // HTML
            if (r.StartsWith("[") || r.StartsWith("{"))
            {
                List<JToken> _docs = ParseJsonDocuments(r);
                if (_docs.Count == 0) { return false; } // genuinely malformed
                if (_docs.Count > 1)
                {
                    // several concatenated documents: usable if ANY carries evidence
                    for (int _di = 0; _di < _docs.Count; _di++)
                    {
                        if (JudgeJsonToken(_docs[_di], requireJson, depth)) { return true; }
                    }
                    return false;
                }
                JToken t = _docs[0];
                if (t.Type == JTokenType.Array)
                {
                    JArray a = (JArray)t;
                    if (a.Count == 0) { return false; }
                    // client may project ONLY the MCP content[] array, dropping the
                    // parent isError flag: detect that shape and re-judge its text
                    // MCP content projection = EVERY element is an object carrying
                    // "text". Requiring "text" on all elements keeps legitimate data
                    // rows with a "type" column (e.g. [{"type":"analog","value":12.3}])
                    // out of this branch; a mixed array like [{"text":...},0] is not a
                    // projection either and falls through to substance rules.
                    // ANY element carrying "text" marks an MCP content projection
                    // (a mixed array like [{"text":...},0] is one too). Legitimate
                    // data rows have no "text" column, so [{"type":"analog",
                    // "value":12.3}] is unaffected. A data row that DID carry "text"
                    // would be re-judged and, if non-JSON, fail SAFE to ungrounded.
                    bool contentShape = false;
                    for (int k = 0; k < a.Count; k++)
                    {
                        if (a[k].Type == JTokenType.Object && PropCI((JObject)a[k], "text") != null)
                        {
                            contentShape = true; break;
                        }
                    }
                    if (contentShape)
                    {
                        StringBuilder cb = new StringBuilder();
                        for (int k = 0; k < a.Count; k++)
                        {
                            if (a[k] == null || a[k].Type != JTokenType.Object) { continue; } // scalars in a mixed array must not be cast
                            JToken tx = PropCI((JObject)a[k], "text");
                            if (tx != null && tx.Type == JTokenType.String) { cb.Append(tx.ToString()); }
                        }
                        string inner = cb.ToString().Trim();
                        if (inner.Length == 0) { return false; }
                        return LooksUsableInner(inner, requireJson, depth - 1);
                    }
                    return ArrayHasSubstance(a, 2);
                }
                if (t.Type == JTokenType.Object)
                {
                    JObject jo = (JObject)t;
                    if (jo.Count == 0) { return false; }
                    bool handled;
                    bool mcpOk = JudgeMcpShape(jo, requireJson, depth, out handled);
                    if (handled) { return mcpOk; } // MCP envelope/result decided
                    if (IsErrorObject(jo)) { return false; }
                    bool saw = false, good = false;
                    ScanDataKeys(jo, 2, ref saw, ref good);
                    if (saw) { return good; }
                    if (requireJson)
                    {
                        // EVIDENCE mode. A recognised data container is the norm, but the
                        // FRS tools return NESTED shapes with no top-level data key and no
                        // top-level numerics -- e.g. get_hist_realtime {"report":{...},
                        // "relay":[...]} and analyse_alert {cause_code_def, instant, reda,
                        // formula_check}. The earlier rule counted only top-level numbers
                        // and so rejected real telemetry as ungrounded. Accept a payload
                        // that carries substantive nested content; error shells, no-data
                        // prose and empty containers have already been rejected above, and
                        // an explicit data key still decides on its own.
                        if (ElementHasSubstance(jo, 3)) { return true; }
                        // scalar-METRIC response (trend_get: direction/slope/pct_change)
                        int metrics = 0;
                        foreach (var prop in jo.Properties())
                        {
                            JToken v = prop.Value;
                            if (v == null || v.Type == JTokenType.Null) { continue; }
                            if (IsMetadataKey(prop.Name)) { continue; }
                            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) { metrics++; }
                        }
                        return metrics >= 2;
                    }
                    foreach (var prop in jo.Properties())
                    {
                        string ln = prop.Name.ToLowerInvariant();
                        if ((ln == "count" || ln == "total" || ln == "rows")
                            && (prop.Value.Type == JTokenType.Integer || prop.Value.Type == JTokenType.Float)
                            && prop.Value.ToString() == "0") { return false; }
                    }
                    if (HasNonEmptyArray(jo, 2)) { return true; }
                    int informative = 0;
                    foreach (var prop in jo.Properties())
                    {
                        JToken v = prop.Value;
                        if (v == null || v.Type == JTokenType.Null) { continue; }
                        if (v.Type == JTokenType.Array) { continue; }
                        if (v.Type == JTokenType.Boolean) { continue; }
                        string lname = prop.Name.ToLowerInvariant();
                        if (lname == "message" || lname == "status" || lname == "success") { continue; }
                        informative++;
                    }
                    return informative >= 3;
                }
                return false;
            }
            if (requireJson) { return false; } // evidence must be JSON from this server
            string lower = r.ToLowerInvariant();
            for (int k = 0; k < _noDataPhrases.Length; k++)
            {
                if (lower.Contains(_noDataPhrases[k])) { return false; }
            }
            return r.Length > 10;
        }


        internal static bool HasNonEmptyArray(JToken t, int depth)
        {
            if (depth < 0 || t == null) { return false; }
            if (t.Type == JTokenType.Array) { return ArrayHasSubstance((JArray)t, depth); } // substance, not count
            if (t.Type == JTokenType.Object)
            {
                foreach (var prop in ((JObject)t).Properties())
                {
                    if (HasNonEmptyArray(prop.Value, depth - 1)) { return true; }
                }
            }
            return false;
        }

        // Why a payload was rejected. PARSE_FAIL almost always means the result
        // was truncated in transit -- compare the tail in the preview.
        internal static string RejectReason(string result, bool requireJson)
        {
            if (string.IsNullOrEmpty(result)) { return "EMPTY"; }
            string r = result.Trim();
            if (r.StartsWith("Tool '", StringComparison.Ordinal)) { return "TOOL_FAILED"; }
            if (r.StartsWith("<", StringComparison.Ordinal)) { return "HTML"; }
            if (r.StartsWith("[") || r.StartsWith("{"))
            {
                Exception pex = null;
                try { JToken.Parse(r); return "NO_SUBSTANCE"; }
                catch (Exception e1) { pex = e1; }
                List<JToken> docs = ParseJsonDocuments(r);
                if (docs.Count > 1) { return "MULTI_DOC(" + docs.Count + ")_NO_SUBSTANCE"; }
                if (docs.Count == 1) { return _truncMarker.IsMatch(r) ? "TRUNCATED_REPAIRED_NO_SUBSTANCE" : "NO_SUBSTANCE"; }
                // Newtonsoft names the offending character AND its position; quote
                // both plus the surrounding text so the defect is identified rather
                // than inferred.
                string pm = pex == null ? "" : pex.Message.Replace("\r", " ").Replace("\n", " ");
                if (pm.Length > 180) { pm = pm.Substring(0, 180); }
                return "PARSE_FAIL: " + pm + ContextAtPosition(r, pex);
            }
            return requireJson ? "NOT_JSON" : "PROSE";
        }


        // Head AND tail of a rejected payload: a tail that does not close the
        // JSON is proof of truncation. Redacted and newline-flattened.
        // Pull "position NNNN" out of the parser message and quote the text
        // around it, so a corrupt byte in the middle of a payload is visible.
        internal static string ContextAtPosition(string r, Exception pex)
        {
            if (pex == null || string.IsNullOrEmpty(r)) { return ""; }
            try
            {
                // Newtonsoft reports line AND position-within-that-line, not an
                // absolute offset. Resolve the line first, or the quoted context
                // points at the start of the payload and misleads.
                System.Text.RegularExpressions.Match ml =
                    System.Text.RegularExpressions.Regex.Match(pex.Message, @"line (\d+)");
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(pex.Message, @"position (\d+)");
                if (!m.Success) { return ""; }
                int pos = int.Parse(m.Groups[1].Value);
                if (ml.Success)
                {
                    int lineNo = int.Parse(ml.Groups[1].Value);
                    int off = 0;
                    for (int L = 1; L < lineNo && off >= 0; L++)
                    {
                        int nx = r.IndexOf('\n', off);
                        if (nx < 0) { off = -1; break; }
                        off = nx + 1;
                    }
                    if (off >= 0) { pos += off; }
                }
                int from = Math.Max(0, pos - 45);
                int len = Math.Min(90, r.Length - from);
                if (len <= 0) { return ""; }
                string around = r.Substring(from, len).Replace("\r", " ").Replace("\n", " ");
                StringBuilder esc = new StringBuilder();
                for (int i = 0; i < around.Length; i++)
                {
                    char c = around[i];
                    if (c < 32 || c == 127) { esc.Append("<0x").Append(((int)c).ToString("x2")).Append(">"); }
                    else { esc.Append(c); }
                }
                return " @pos" + pos + " >>[" + esc.ToString() + "]";
            }
            catch { return ""; }
        }


        // -- v1.0.75.0 -- covered now requires ADEQUACY of the UNIQUE PRE-ALERT
        // set: recency alone let a single row 10 s before the alert stand in for
        // a trend, and the flat 300 s gap rejected healthy periodic series
        // sampled every ~13 min (line 1966).
        internal CoverageStateKind HistoryCoverageState(string result, out long shortBySec, out string why)
        {
            shortBySec = 0;
            why = null;
            if (Sess._incidenceEpoch <= 0 || string.IsNullOrEmpty(result))
            {
                return CoverageStateKind.Unverifiable;
            }
            HistoryCoverage cov = ComputeHistoryCoverage(result);
            if (cov == null || cov.LastBefore <= 0)
            {
                why = "no readable sample at or before the incidence";
                return CoverageStateKind.Unverifiable;
            }
            shortBySec = Sess._incidenceEpoch - cov.LastBefore;
            if (shortBySec < 0)
            {
                shortBySec = 0;
            }
            long adaptive = cov.MedianPreAlertIntervalSec > 0 ? cov.MedianPreAlertIntervalSec * 2 : 0;
            if (adaptive > _historyAdaptiveCapSec)
            {
                adaptive = _historyAdaptiveCapSec;
            }
            long effectiveTol = _historyCoverToleranceSec;
            if (adaptive > effectiveTol)
            {
                effectiveTol = adaptive;
            }
            long spanSec = Sess._incidenceEpoch - cov.EarliestBefore;
            AiLog("INFO", "COVERAGE", "last-before gap=" + shortBySec + "s preSamples=" + cov.PreAlertSampleCount
                + " span=" + spanSec + "s median=" + cov.MedianPreAlertIntervalSec + "s effTol=" + effectiveTol
                + "s after=" + (cov.FirstAfter > 0 ? "yes" : "no"));
            // v1.0.160.5: NO temporal-tolerance gate. Owner rule: whatever sample exists at or before
            // the incidence IS the incidence value, however long before it was recorded (change-of-value
            // feeds legitimately HOLD a value for many minutes/hours between changes -- a reading
            // unchanged since 10:00 is still that value at a 13:13 alert, not a coverage gap). The old
            // "shortBySec > effectiveTol -> ShortAtEnd" gate is removed so a value-present alert is
            // never downgraded to coverage-gap merely because the last change predates the incidence by
            // more than N seconds. effectiveTol is still computed above for the log line only.
            // (Genuine no-data -- no row at all at/before incidence -- is still caught by the
            // cov.LastBefore <= 0 check earlier, which returns Unverifiable.)
            if (cov.PreAlertSampleCount < _historyMinSamples)
            {
                why = "has only " + cov.PreAlertSampleCount + " pre-alert sample(s) - cannot show sustained vs transient";
                return CoverageStateKind.InsufficientSamples;
            }
            if (spanSec < _historyMinSpanSec)
            {
                why = "spans only " + (spanSec / 60) + " min before the alert";
                return CoverageStateKind.InsufficientSpan;
            }
            return CoverageStateKind.Covered;
        }


        internal static string HistoryTagFromArgs(JObject args)
        {
            if (args == null)
            {
                return null;
            }
            JToken t = PropCI(args, "TagId");
            if (t == null || t.Type == JTokenType.Null)
            {
                return null;
            }
            string s = t.ToString().Trim();
            return s.Length == 0 ? null : s;
        }


        // -- v1.0.75.0 -- REMOVED: own regex scan with a one-hour allowance, so the
        // agentic path accepted series the prefetch gate rejected (300 s vs 3600 s),
        // and an unverifiable series produced NO warning at all.
        // private string HistoryCoverageNote(string result)
        // {
        //     if (_incidenceEpoch <= 0 || string.IsNullOrEmpty(result)) { return null; }
        //     try
        //     {
        //         long last = 0;
        //         foreach (System.Text.RegularExpressions.Match m in
        //             System.Text.RegularExpressions.Regex.Matches(result, @"\b(17\d{8})\b"))
        //         {
        //             long v;
        //             if (long.TryParse(m.Groups[1].Value, out v) && v > last && v < _incidenceEpoch + 86400)
        //             {
        //                 last = v;
        //             }
        //         }
        //         if (last <= 0) { return null; }
        //         long shortBy = _incidenceEpoch - last;
        //         if (shortBy < 3600) { return null; }   // within an hour: covered
        //         return "NOTE: this series ENDS " + (shortBy / 3600) + " HOURS BEFORE the alert, so the "
        //             + "breach itself is NOT in this data. The API returns the OLDEST rows up to `limit`, "
        //             + "so a wide window stops short. Re-request with a NARROWER window ending at the "
        //             + "incidence time to see the breach. Do not describe this series as covering the alert.";
        //     }
        //     catch { return null; }
        // }

        // One rule for BOTH paths. Takes the fetched TagId so coverage is tracked
        // PER TAG (a different tag's good series must never satisfy the ALERTING
        // tag's coverage requirement). The presumed alerting tag is resolved
        // MECHANICALLY (see TryResolveAlertingTagFromSearch below), never from
        // trusting whichever tag the model happened to fetch first.
        internal string HistoryCoverageNote(string result, string tagId)
        {
            // -- v1.0.76.0 -- moved before the incidence check: an unparseable
            // incidence with a usable payload must still register that a
            // history_get ran, or scoped enforcement silently skips the alert.
            Sess.gotUsableHistoryGet = true;
            if (Sess._incidenceEpoch <= 0 || string.IsNullOrEmpty(result))
            {
                Sess._lastCoverageKind = CoverageStateKind.Unverifiable;
                SetTagCoverage(tagId, HistoryCoverageLevel.Unverified);
                return "NOTE: incidence timestamp could not be parsed; history coverage "
                     + "cannot be verified.";
            }
            // -- v1.0.76.0 -- REMOVED: promoting the FIRST model-issued history_get to
            // "the alerting tag" trusted an unverified guess (the model may fetch TPR,
            // a coupled voltage, or any corroborating attribute first). Resolution is
            // now mechanical only, via TryResolveAlertingTagFromSearch (below) on a
            // successful search_tags result. If it still cannot resolve, AlertingTagId()
            // stays null and no tag is "presumed" -- a good series for a different tag
            // remains corroboration, never coverage.
            // if (requiredAlertingTagId == null && presumedAlertingTagId == null && tagId != null)
            // {
            //     presumedAlertingTagId = tagId;
            //     coverageTagPresumed = true;
            // }
            long shortBy;
            string why;
            CoverageStateKind state = HistoryCoverageState(result, out shortBy, out why);
            Sess._lastCoverageKind = state;
            if (state == CoverageStateKind.Covered)
            {
                SetTagCoverage(tagId, HistoryCoverageLevel.VerifiedAdequate);
                return null;
            }
            if (state == CoverageStateKind.Unverifiable)
            {
                SetTagCoverage(tagId, HistoryCoverageLevel.Unverified);
                return "NOTE: incidence coverage of this series could NOT be verified - it " + why
                    + ". " + CoverageRecoveryAdvice(state) + ".";
            }
            SetTagCoverage(tagId, HistoryCoverageLevel.Inadequate);
            return "NOTE: this series is NOT sufficient evidence - it " + why + ". "
                + CoverageRecoveryAdvice(state) + ".";
        }


        // Coverage stamps are only readable for the true series tool. analyse_alert
        // and get_hist_realtime stay in _jsonHistoryFamily for grounding and caps,
        // but their report shapes carry no cols/rows series to verify.
        internal static bool IsCoverageCheckedTool(string toolName)
        {
            return string.Equals(toolName, "history_get", StringComparison.OrdinalIgnoreCase);
        }


        internal static bool IsEvidenceTool(string toolName)
        {
            for (int i = 0; i < _evidenceTools.Length; i++)
            {
                if (string.Equals(toolName, _evidenceTools[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }


        // bounded byte read: returns null when the stream exceeds maxBytes
        internal static async Task<string> ReadBodyBoundedAsync(System.IO.Stream input, int maxBytes, System.Threading.CancellationToken ct)
        {
            byte[] buf = new byte[8192];
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                int read;
                while ((read = await input.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buf, 0, read);
                    if (ms.Length > maxBytes) { return null; }
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }


        internal bool JsonToolPermitted(string toolName, out string denyReason)
        {
            denyReason = null;
            string n = toolName ?? "";
            if (!_jsonToolCaps.ContainsKey(n))
            {
                denyReason = "Tool '" + n + "' is not permitted in verdict mode (read-only allowlist). Use only: " + string.Join(", ", _jsonToolCaps.Keys) + ".";
                return false;
            }
            if (Sess._jsonToolTotal >= _jsonToolTotalCap)
            {
                denyReason = "Tool budget exhausted (" + _jsonToolTotalCap + " calls). Produce the verdict JSON now from the evidence already gathered.";
                return false;
            }
            int used;
            Sess._jsonToolUsed.TryGetValue(n, out used);
            if (used >= _jsonToolCaps[n])
            {
                denyReason = "Call limit for '" + n + "' reached (" + _jsonToolCaps[n] + "). Produce the verdict from data already gathered.";
                return false;
            }
            bool isHist = false;
            for (int i = 0; i < _jsonHistoryFamily.Length; i++) { if (string.Equals(_jsonHistoryFamily[i], n, StringComparison.OrdinalIgnoreCase)) { isHist = true; break; } }
            if (isHist)
            {
                int fam = 0;
                for (int i = 0; i < _jsonHistoryFamily.Length; i++)
                {
                    int u; Sess._jsonToolUsed.TryGetValue(_jsonHistoryFamily[i], out u); fam += u;
                }
                if (fam >= _jsonHistoryFamilyCap)
                {
                    denyReason = "History-call budget reached (" + _jsonHistoryFamilyCap + " total). Produce the verdict from data already gathered.";
                    return false;
                }
            }
            Sess._jsonToolUsed[n] = used + 1;
            Sess._jsonToolTotal++;
            return true;
        }


        // ═════════════════════════════════════════════════════════
        //  AGENTIC LOOP
        // ═════════════════════════════════════════════════════════
        // -- v1.0.77.0 -- validateVerdictOutput is now SEPARATE from rawJsonMode:
        // rawJsonMode alone used to gate the "parse the completed turn as verdict
        // JSON and emit a validated SSE frame" step, but AnalyzeChat's follow-up
        // prose ALSO passed rawJsonMode=true (to keep the existing out-of-scope-
        // skip / pre-JSON-text behavior it relied on), which meant every follow-up
        // answer was parsed as JSON and failed with "Verdict was not valid JSON."
        // v1.0.160.194: which tools the chat repeat-guard may block. Read-only, idempotent lookups
        // only -- a repeated identical call returns the same data and is thrashing, not progress.
        // Deliberately narrow (the 3960 trace thrashed search_tags); widen to other proven-stable
        // lookups after testing. A tool that can legitimately refresh live/current data must NOT go here.
        internal static bool IsRepeatGuardTool(string toolName)
        {
            return string.Equals(toolName, "search_tags", StringComparison.OrdinalIgnoreCase);
        }
    }
}
