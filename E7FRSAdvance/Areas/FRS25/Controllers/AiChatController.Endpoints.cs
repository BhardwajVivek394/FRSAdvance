using E7.AiCore;
using Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, AI clients) keep the 616 namespace
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Mvc;
using AiModelConfig = E7.AiCore.AiModelConfig;   // 616 merge: Domain.AiModelConfig (the AI Model Config admin screen) has the same name

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // AiChatController -- HTTP actions (AnalyzeAlert, AnalyzeAlertJson, AnalyzeChat, Chat, Status, diagnostics).
    // Split out of AiChatController.cs in 1.0.161.0 (move-only; see CHANGELOG.md).
    public partial class AiChatController : Controller
    {

        // ═════════════════════════════════════════════════════════
        //  GET /AiChat/Status
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> Status()
        {
            // v1.0.160.107 -- F1-response. Privilege FIRST (CaptureLoginUser populates the identity
            // fields AiDebugAllowed reads, and both must run before the first await). A
            // non-privileged caller learns only whether the assistant is usable: no tool counts, no
            // mcp topology, no client-library name, no raw exception text.
            Engine.CaptureLoginUser();
            bool priv = Engine.AiDebugAllowed();
            try
            {
                AnalysisEngine.ToolLoadResult loaded = await Engine.LoadAllToolsAsync().ConfigureAwait(false);
                if (!priv)
                {
                    return Json(loaded.Tools.Count > 0
                        ? (object)new { ok = true }
                        : (object)new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
                }
                // v1.0.160.108 H4: booleans, not error text. No redaction attempt -- redaction is
                // open-ended and leaks eventually; the raw errors stay in the server log only.
                return Json(new
                {
                    ok = loaded.Tools.Count > 0,
                    version = AnalysisEngine.ComponentVersion,
                    tools = loaded.Tools.Count,
                    mcp1 = new { tools = loaded.Count1, healthy = (loaded.Error1 == null) },
                    mcp2 = new { tools = loaded.Count2, healthy = (loaded.Error2 == null) },
                    promptCache = new
                    {
                        enabled = AnalysisEngine._promptCache,
                        scope = "tools + system prefix (one cache_control marker on the system block)",
                        verifyWith = "meta.tokensCacheRead on the next analysis -- non-zero means it is working"
                    },
                    logging = AnalysisEngine.LogDiagnostics()
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                // v1.0.160.108 H4: ex.Message never leaves the server, at ANY privilege.
                Engine.AiLog("WARN", "STATUS", "status failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        // ═════════════════════════════════════════════════════════
        //  GET /AiChat/Status2
        // ═════════════════════════════════════════════════════════
        // v1.0.160.116 CUT G3: batch verdict-state lookup for the alert board. Display only.
        [HttpPost]
        public ActionResult VerdictStates()
        {
            Engine.CaptureLoginUser();
            Engine.Sess._roleSiteKeep = Engine.AiDebugAllowed();
            try
            {
                string body;
                using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
                { body = sr.ReadToEnd(); }
                JObject payload = JObject.Parse(body ?? "{}");
                JArray ids = payload["alertIds"] as JArray;
                if (ids == null || ids.Count == 0)
                { return Json(new { ok = true, states = new JObject() }, JsonRequestBehavior.AllowGet); }
                // Bounded: one page of the board, not an open-ended sweep of the alert table.
                List<long> list = new List<long>();
                for (int i = 0; i < ids.Count && list.Count < 200; i++)
                {
                    long v;
                    if (long.TryParse(ids[i].ToString(), out v) && v > 0) { list.Add(v); }
                }
                string engine = payload["engine"] != null ? payload["engine"].ToString() : "";
                JObject states = Engine.ListVerdictStates(list, engine, Engine.Sess._roleSiteKeep);
                return Content(new JObject { { "ok", true }, { "states", states } }.ToString(Formatting.None),
                               "application/json");
            }
            catch (Exception ex)
            {
                // Never ex.Message to a browser (H4). A list that cannot resolve states must degrade
                // to "not_analyzed", not break the board.
                Engine.AiLog("WARN", "VERDICT_STATES", "batch state lookup failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        // v1.0.160.184: per-operation rows for the popup's PM Analysis visuals, from the SAME
        // API the Point Machine Operation page's Model 4 uses -- HistorianApiBaseUrl +
        // api/pm4/analyze?SiteId&AssetId&WindowDays&AssetName&SiteName. (.183 called the
        // pm-operation-v4 route instead, which is a different surface.)
        // Same-origin on purpose: the analysis host must not be called from the browser.
        // Waveform arrays are dropped here -- a 30-day two-machine response carries current[] and
        // voltage[] on every op, which is megabytes; the visuals read only the fields in
        // PmOpKeepFields.
        [HttpGet]
        public async Task<ActionResult> PmOpsV4(string siteId, string assetId, int windowDays = 30,
            string assetName = "", string siteName = "", bool forceLive = false)
        {
            if (string.IsNullOrWhiteSpace(assetId))
            {
                return Json(new { ok = false, error = "assetId required" }, JsonRequestBehavior.AllowGet);
            }

            string baseUrl = ConfigurationManager.AppSettings["HistorianApiBaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Engine.AiLog("ERROR", "PM_OPS", "HistorianApiBaseUrl is not configured");
                return Json(new { ok = false, error = "Analysis API not configured" }, JsonRequestBehavior.AllowGet);
            }

            string url = baseUrl.TrimEnd('/')
                       + "/api/pm4/analyze?SiteId=" + Uri.EscapeDataString(siteId ?? "")
                       + "&AssetId=" + Uri.EscapeDataString(assetId)
                       + "&WindowDays=" + windowDays
                       + "&AssetName=" + Uri.EscapeDataString(assetName ?? "")
                       + "&SiteName=" + Uri.EscapeDataString(siteName ?? "")
                       + (forceLive ? "&ForceLive=true" : "");

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                string raw;

                using (System.Net.Http.HttpClient http = new System.Net.Http.HttpClient())
                {
                    // ForceLive re-analyzes instead of serving the cache, so it needs the same
                    // headroom the Point Machine Operation page allows its own proxy.
                    http.Timeout = TimeSpan.FromMinutes(5);
                    raw = await http.GetStringAsync(url).ConfigureAwait(false);
                }

                JObject root = JObject.Parse(raw);
                int kept = AnalysisEngine.SlimPmOps(root["a"] as JObject) + AnalysisEngine.SlimPmOps(root["b"] as JObject);
                string body = root.ToString(Newtonsoft.Json.Formatting.None);
                watch.Stop();

                Engine.AiLog("INFO", "PM_OPS", "pm4/analyze served for asset " + assetId
                    + " -- " + kept + " ops, " + raw.Length + " bytes in, " + body.Length + " bytes out, "
                    + watch.ElapsedMilliseconds + " ms" + (forceLive ? " (ForceLive)" : ""));

                return Content(body, "application/json");
            }
            catch (Exception ex)
            {
                watch.Stop();
                Engine.AiLog("ERROR", "PM_OPS", "pm4/analyze fetch failed for asset " + assetId
                    + " after " + watch.ElapsedMilliseconds + " ms: " + AnalysisEngine.SanitizeJsonError(ex.Message));
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpGet]
        public async Task<ActionResult> Status2()
        {
            // v1.0.160.107 -- F1-response. This endpoint publishes the srv2 tool catalogue, which is
            // internal topology. Privilege FIRST, before any await: a non-privileged caller gets a
            // fixed 403 and NO tool discovery is performed at all (not merely withheld).
            Engine.CaptureLoginUser();
            if (!Engine.AiDebugAllowed())
            {
                Response.StatusCode = 403;
                return Json(new { ok = false, error = "Forbidden" }, JsonRequestBehavior.AllowGet);
            }
            try
            {
                // v1.0.160.108 H4: no configuration-key names in a response -- "McpServerUrl2 not set"
                // told a caller both that srv2 exists and what the key is called.
                if (!AnalysisEngine._srv2Enabled)
                    return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
                JArray tools = await AnalysisEngine._mcp2.Value.ListToolsAsync().ConfigureAwait(false);
                // v1.0.160.108 H4: tool NAMES only -- the full schemas describe every argument the
                // internal tool surface accepts.
                JArray names = new JArray();
                for (int i = 0; i < tools.Count; i++)
                {
                    JToken nm = tools[i] != null ? tools[i]["name"] : null;
                    if (nm != null) { names.Add(nm.ToString()); }
                }
                return Json(new { ok = true, tools = names }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                // v1.0.160.108 H4: never ex.Message at any privilege -- it carries paths and hosts.
                Engine.AiLog("WARN", "STATUS2", "status2 failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        // ═════════════════════════════════════════════════════════════
        //  GET /AiChat/WisdomAuthProbe  --  TEMPORARY (identity probe, v1.0.124.0)
        //  Purpose: capture what User.Identity actually is in THIS deployment, so the coming
        //  Wisdom authorization gate can be pinned to a real identity value rather than
        //  guessed. Web.config declares no <authentication>/<authorization>, so the principal
        //  must be observed at runtime. Gated by a SEPARATE appSettings key "WisdomAuthProbe"
        //  (its own key, unrelated to the point-machine work). Key absent/false -> 404. Never
        //  logs anonymous hits. Logs every authenticated hit (no dedup, so a swallowed AiLog
        //  write cannot silently block capture); same generic body for every case; the real
        //  result lives only in the server log line WISDOM_AUTH. REMOVE this action once the
        //  identity is captured and set WisdomAuthProbe=false.
        // ═════════════════════════════════════════════════════════════
        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult WisdomAuthProbe()
        {
            bool probeOn = string.Equals(
                (ConfigurationManager.AppSettings["WisdomAuthProbe"] ?? "").Trim(),
                "true", StringComparison.OrdinalIgnoreCase);
            if (!probeOn)
            {
                Response.StatusCode = 404;
                return Content("", "text/plain");
            }

            System.Security.Principal.IIdentity identity = (User != null) ? User.Identity : null;
            if (identity == null || !identity.IsAuthenticated)
            {
                // No authenticated principal: log nothing, reveal nothing.
                return Content("probe completed", "text/plain");
            }

            // Log every authenticated hit (temporary endpoint; operator turns the key off
            // after capture). No one-shot dedup: AiLog swallows write failures, so marking an
            // identity "logged" before a failed write could silently prevent the retry the
            // probe exists for. Duplicate lines also reveal per-user/per-worker variance.
            Engine.AiLog("INFO", "WISDOM_AUTH",
                "isAuthenticated=" + identity.IsAuthenticated
                + " type=" + (identity.AuthenticationType ?? "")
                + " name=" + (identity.Name ?? "")
                + " principalType=" + ((User != null) ? User.GetType().FullName : "")
                + " identityType=" + identity.GetType().FullName);
            return Content("probe completed", "text/plain");
        }

        // v1.0.55.11: per-alert Tool trace. The verdict popup's "Tool trace" button calls this
        // with the run's conversationId (the sse-xxx traceId emitted as the "meta" SSE event) and
        // gets back JUST that alert's tool calls -- toolName, args, result preview, size, timing --
        // so an engineer can see what each tool was asked and what it returned, without downloading
        // the whole day's log. Read-only; filters the day's ai-tool jsonl by conversationId.
        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult ToolTrace(string convId)
        {
            // v1.0.160.58: ToolTrace had NO authorisation. It returns every tool call, argument and
            // RAW RESULT for a run -- the one place platform/hardware detail can reach a railway
            // user. Gated on the same predicate as the rest of debug/tools.
            if (!Engine.AiDebugAllowed())
            {
                Response.StatusCode = 403;
                return Json(new { ok = false, error = "tool trace is restricted to site-keeping users" },
                            JsonRequestBehavior.AllowGet);
            }
            try
            {
                if (string.IsNullOrEmpty(convId))
                { return Json(new { ok = false, error = "no convId" }, JsonRequestBehavior.AllowGet); }

                // v1.0.158.1: the popup used to say "No tool calls logged for this run" for THREE
                // different causes -- logging switched off, no log file, or id not found -- which
                // reads as "this run made no tool calls" and sent people hunting the wrong bug.
                // Report the actual reason.
                if (!AnalysisEngine._toolLoggingEnabled)
                {
                    JObject off = new JObject();
                    off["ok"] = true; off["convId"] = convId; off["count"] = 0;
                    off["calls"] = new JArray(); off["logging"] = false;
                    off["reason"] = "logging_disabled";
                    return Content(off.ToString(Formatting.None), "application/json");
                }
                JArray calls = new JArray();
                string path = System.IO.Path.Combine(
                    AnalysisEngine._toolLogDir, "ai-tool-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");
                // also try yesterday (a run just before UTC midnight can straddle files)
                string pathY = System.IO.Path.Combine(
                    AnalysisEngine._toolLogDir, "ai-tool-" + DateTime.UtcNow.AddDays(-1).ToString("yyyyMMdd") + ".jsonl");
                string[] files = { path, pathY };

                for (int fi = 0; fi < files.Length; fi++)
                {
                    if (!System.IO.File.Exists(files[fi])) { continue; }
                    string[] lines;
                    try
                    {
                        // open shared: today's file is being appended to concurrently
                        using (var fs = new System.IO.FileStream(files[fi], System.IO.FileMode.Open,
                            System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                        using (var sr = new System.IO.StreamReader(fs, Encoding.UTF8))
                        { lines = sr.ReadToEnd().Split('\n'); }
                    }
                    catch { continue; }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i];
                        if (string.IsNullOrEmpty(ln) || ln.IndexOf(convId, StringComparison.Ordinal) < 0) { continue; }
                        JObject o;
                        try { o = JObject.Parse(ln.Trim()); }
                        catch { continue; }
                        if (!string.Equals((string)o["conversationId"], convId, StringComparison.Ordinal)) { continue; }
                        // one row per tool call (the "response"/"error"/"denied" phase carries the result)
                        string phase = (string)o["phase"] ?? "";
                        if (phase == "requested") { continue; }   // skip the request-only echo; response row has both
                        JObject row = new JObject();
                        row["tool"] = (string)o["toolName"] ?? "";
                        row["phase"] = phase;
                        row["stage"] = (string.Equals((string)o["toolUseId"], "prefetch", StringComparison.Ordinal)) ? "prefetch" : "model";   // v1.0.158.2
                        row["server"] = (string)o["server"] ?? "";
                        row["iter"] = o["iteration"];
                        row["ms"] = o["elapsedMs"];
                        row["args"] = (string)o["argsPreview"] ?? "";
                        row["resultLen"] = o["resultLength"];
                        row["result"] = (string)o["resultPreview"] ?? "";
                        row["error"] = (string)o["error"] ?? "";
                        calls.Add(row);
                    }
                    // v1.0.158.1: do NOT stop after today's file -- a run straddling UTC midnight
                    // has rows in BOTH files and the tail was being dropped.
                }
                // v1.0.158.1: still nothing? the run may predate both files (retention keeps ~14
                // days) -- scan every retained tool log for this id before giving up.
                bool anyFile = System.IO.File.Exists(path) || System.IO.File.Exists(pathY);
                if (calls.Count == 0)
                {
                    string[] all;
                    try { all = System.IO.Directory.GetFiles(AnalysisEngine._toolLogDir, "ai-tool-*.jsonl"); }
                    catch { all = new string[0]; }
                    if (all.Length > 0) { anyFile = true; }
                    for (int fj = 0; fj < all.Length; fj++)
                    {
                        if (string.Equals(all[fj], path, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(all[fj], pathY, StringComparison.OrdinalIgnoreCase)) { continue; }
                        string[] ln2;
                        try
                        {
                            using (var fs2 = new System.IO.FileStream(all[fj], System.IO.FileMode.Open,
                                System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                            using (var sr2 = new System.IO.StreamReader(fs2, Encoding.UTF8))
                            { ln2 = sr2.ReadToEnd().Split('\n'); }
                        }
                        catch { continue; }
                        for (int i2 = 0; i2 < ln2.Length; i2++)
                        {
                            string l2 = ln2[i2];
                            if (string.IsNullOrEmpty(l2) || l2.IndexOf(convId, StringComparison.Ordinal) < 0) { continue; }
                            JObject o2;
                            try { o2 = JObject.Parse(l2.Trim()); }
                            catch { continue; }
                            if (!string.Equals((string)o2["conversationId"], convId, StringComparison.Ordinal)) { continue; }
                            string ph2 = (string)o2["phase"] ?? "";
                            if (ph2 == "requested") { continue; }
                            JObject r2 = new JObject();
                            r2["tool"] = (string)o2["toolName"] ?? ""; r2["phase"] = ph2;
                            r2["stage"] = (string.Equals((string)o2["toolUseId"], "prefetch", StringComparison.Ordinal)) ? "prefetch" : "model";
                            r2["server"] = (string)o2["server"] ?? ""; r2["iter"] = o2["iteration"];
                            r2["ms"] = o2["elapsedMs"]; r2["args"] = (string)o2["argsPreview"] ?? "";
                            r2["resultLen"] = o2["resultLength"]; r2["result"] = (string)o2["resultPreview"] ?? "";
                            r2["error"] = (string)o2["error"] ?? "";
                            calls.Add(r2);
                        }
                        if (calls.Count > 0) { break; }
                    }
                }

                JObject outp = new JObject();
                outp["ok"] = true;
                outp["convId"] = convId;
                outp["count"] = calls.Count;
                outp["calls"] = calls;
                outp["logging"] = true;
                if (calls.Count == 0) { outp["reason"] = anyFile ? "not_found" : "no_log_file"; }
                return Content(outp.ToString(Formatting.None), "application/json");
            }
            catch (Exception ex)
            {
                // v1.0.160.108 H4: this action is already 403-gated, but the rule is "no ex.Message
                // to a browser at ANY privilege" -- detail goes to the log.
                Engine.AiLog("WARN", "TOOLTRACE", "tool trace failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        // Fail-closed identity resolver. UNUSED here (compiles, no caller); placed so the
        // Wisdom build reuses it verbatim for endpoint authorization.
        private string ResolveWisdomIdentity()
        {
            // 1) Real ASP.NET identity when the site has authentication wired.
            if (User != null && User.Identity != null
                && User.Identity.IsAuthenticated
                && !string.IsNullOrWhiteSpace(User.Identity.Name))
            {
                return User.Identity.Name.Trim();
            }
            // 2) v1.0.130.0: this portal runs its own login (no <authentication>
            //    section), so User.Identity is anonymous for everyone. Fall back to
            //    the app session username under a CONFIG-NAMED key -- inert until
            //    WisdomIdentitySessionKey is set, so nothing changes by default.
            try
            {
                string key = System.Configuration.ConfigurationManager.AppSettings["WisdomIdentitySessionKey"];
                if (!string.IsNullOrWhiteSpace(key) && Session != null)
                {
                    object v = Session[key.Trim()];
                    string name = v == null ? null : Convert.ToString(v);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }
                }
            }
            catch { }
            // 3) v1.0.131.0: optional LAST-RESORT default identity (e.g. "ENERGY7")
            //    for portals with no per-user identity at all. Config-gated and off
            //    by default. NOTE: with this set, anyone who can reach the portal
            //    acts as this shared identity -- audit lines carry no per-user name.
            string defId = System.Configuration.ConfigurationManager.AppSettings["WisdomDefaultIdentity"];
            if (!string.IsNullOrWhiteSpace(defId))
            {
                return defId.Trim();
            }
            return null;
        }

        // ═════════════════════════════════════════════════════════
        //  POST /AiChat/DryRunTools?siteId=&assetId=&days=3&runs=1&includeMl=false
        //  v1.0.105.0: run the REAL read-only MCP tools the AI uses, server-side, for tool
        //  TRANSPORT/AVAILABILITY testing. Shares CallToolRoutedAsync with the verdict flow,
        //  but does NOT reproduce the full verdict pipeline (no ClampToolArgs, no
        //  NarrowAttributeRange, no evidence-grade validation, no per-request budgets) - so a
        //  green run proves the tools RESPOND with usable-looking data, NOT that the verdict
        //  path will accept every result. No verdict, no model call, no writes.
        //  AUTH: this is a BROWSER-FACING diagnostic (called from the authenticated Alert
        //  Analysis / Historian page, like AnalyzeAlert), so it does NOT use the service-to-
        //  service X-Internal-Api-Key (a browser cannot hold that secret). It is GATED by
        //  appSettings "EnableDryRunTools" (default OFF -> 403), a production kill-switch, and
        //  relies on the app-level authentication that protects those pages. For a stricter
        //  posture add [Authorize] (ideally a diagnostic/admin role) to this action. Resource
        //  caps: runs 1-5, days 1-15, ML tools ONLY when includeMl=true (they are the slowest).
        //  Observes client-disconnect via Response.ClientDisconnectedToken.
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> DryRunTools(string siteId, string assetId, int days = 3, int runs = 1, bool includeMl = false)
        {
            // GATE - appSettings "EnableDryRunTools" must be "true". Default OFF: production
            // ships with the endpoint disabled (403), a server-side kill-switch matching the
            // client AA_DEBUG_TOOLS flag. This is a browser-facing diagnostic (called from the
            // authenticated Alert Analysis / Historian page, same as AnalyzeAlert), so it relies
            // on the app-level auth that protects those pages rather than the service-to-service
            // X-Internal-Api-Key (which a browser cannot hold). Hard resource caps below still
            // apply whenever it is enabled.
            string enableFlag = ConfigurationManager.AppSettings["EnableDryRunTools"];
            if (!string.Equals(enableFlag, "true", StringComparison.OrdinalIgnoreCase))
            { Response.StatusCode = 403; return Json(new { ok = false, error = "DryRunTools disabled (set EnableDryRunTools=true in appSettings to enable)" }, JsonRequestBehavior.AllowGet); }

            if (string.IsNullOrWhiteSpace(assetId))
            { Response.StatusCode = 400; return Json(new { ok = false, error = "assetId required" }, JsonRequestBehavior.AllowGet); }
            if (days < 1) { days = 1; } if (days > 15) { days = 15; }
            if (runs < 1) { runs = 1; } if (runs > 5) { runs = 5; }   // capped low for production

            System.Threading.CancellationToken ct = default(System.Threading.CancellationToken);
            try { ct = Response.ClientDisconnectedToken; } catch { }

            var log = new List<string>();
            var summary = new Dictionary<string, DryRunAgg>(StringComparer.OrdinalIgnoreCase);
            Action<string> Line = delegate(string s) { log.Add("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + s); };

            AnalysisEngine.ToolLoadResult loaded;
            try { loaded = await Engine.LoadAllToolsAsync().ConfigureAwait(false); }
            catch (Exception ex) { return Json(new { ok = false, error = "tool load failed: " + ex.Message }, JsonRequestBehavior.AllowGet); }

            DateTime now = DateTime.Now;
            DateTime start = now.AddDays(-days);
            string sd = start.ToString(AnalysisEngine._ddFmt), ed = now.ToString(AnalysisEngine._ddFmt);

            // The read-only tools the AI actually uses, with real args. get_attribute_range
            // needs a SiteId. ML tools are the SLOWEST and run ONLY when includeMl=true.
            var plan = new List<Tuple<string, JObject, bool>>
            {
                Tuple.Create("get_asset_tags",    AnalysisEngine.NewArgs("AssetId", assetId), false),
                Tuple.Create("history_get",       AnalysisEngine.HistWindowArgs("AssetId", assetId, start, now, AnalysisEngine._limitCapEvidence, "columnar"), false),
                Tuple.Create("get_hist_realtime", AnalysisEngine.NewArgs("AssetId", assetId), false),
            };
            if (!string.IsNullOrWhiteSpace(siteId))
            {
                // v1.0.160.6: scope to AssetId when available. get_attribute_range is SITE-scoped
                // server-side (~266KB, truncated to ~12KB in transit -> the needed baseline can be
                // in the DROPPED part). Adding AssetId makes the server return just this asset's
                // ranges (~3KB, COMPLETE, no truncation) -- proven on the wire: {SiteId:14}=12083B
                // truncated vs {SiteId:14,AssetId:1607}=2968B intact. Falls back to SiteId-only when
                // assetId is blank.
                JObject rangeArgs = AnalysisEngine.NewArgs("SiteId", siteId);
                string arAsset = AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "assetId");
                if (!string.IsNullOrWhiteSpace(arAsset)) { rangeArgs["AssetId"] = arAsset; }
                plan.Add(Tuple.Create("get_attribute_range", rangeArgs, false));
            }
            else
            {
                // v1.0.160.151: siteId is what BOTH failing tools depend on, by two different
                // routes: get_attribute_range is not PLANNED AT ALL without it (this branch), and
                // get_frs_alerts IS planned but built with NewFrsAlertsProbeArgs(siteId, ...), so an
                // empty siteId sends an empty SiteIds array and the server returns nothing.
                // One blank field, two tools, and the four downstream symptoms
                // ("configured envelope unavailable", no average markers, blank assetType,
                // "Still active" on a cleared alert) all follow from that single fact with nothing
                // saying so. This is the difference between a tool that FAILED and a tool that was
                // never CALLED, which is the distinction the whole day kept turning on.
                Engine.AiLog("WARN", "PREFETCH", "siteId is EMPTY -- get_attribute_range and get_frs_alerts "
                    + "were NOT CALLED. Envelopes, averages, assetType and resetTime all depend on "
                    + "them; expect 'configured envelope unavailable' and 'Still active'. "
                    + "assetId=" + AnalysisEngine.Trunc(AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "assetId"), 20)
                    + " alertId=" + AnalysisEngine.Trunc(AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "alertId"), 20));
            }
            // v1.0.118.0: get_frs_alerts declares SiteIds/AssetIds as ARRAYS (required: SiteIds),
            // per the MCP1 tools/list. The old singular NewArgs("SiteId",...) sent an unknown arg
            // and omitted the required SiteIds, so this probe failed. Build the correct array shape.
            plan.Add(Tuple.Create("get_frs_alerts", AnalysisEngine.NewFrsAlertsProbeArgs(siteId, assetId), true));
            if (includeMl && !string.IsNullOrWhiteSpace(siteId))
            {
                plan.Add(Tuple.Create("predict_track_health", AnalysisEngine.NewPredictArgs("predict_track_health", siteId, assetId), true));
                plan.Add(Tuple.Create("predict_pm_operation", AnalysisEngine.NewPredictArgs("predict_pm_operation", siteId, assetId), true));
            }

            Line("=================================================================");
            Line("DRY-RUN (server-side, real MCP tools) asset=" + assetId + " site=" + (siteId ?? "-")
                + " window=" + days + "d runs=" + runs + " ml=" + includeMl + " toolsLoaded=" + loaded.Tools.Count);

            for (int run = 1; run <= runs; run++)
            {
                Line("----- run " + run + "/" + runs + " -----");
                foreach (var step in plan)
                {
                    if (ct.IsCancellationRequested) { Line("  (client disconnected - aborting)"); break; }
                    string toolName = step.Item1; JObject toolArgs = step.Item2; bool optional = step.Item3;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    DryRunAgg a = summary.ContainsKey(toolName) ? summary[toolName] : (summary[toolName] = new DryRunAgg());
                    string res = null; string err = null; bool ok = false;
                    try
                    {
                        res = await Engine.CallToolRoutedAsync(toolName, toolArgs, ct).ConfigureAwait(false);
                        ok = !string.IsNullOrEmpty(res) && AnalysisEngine.LooksUsableToolResult(res, false);
                        if (!ok && string.IsNullOrEmpty(err)) { err = res == null ? "null result" : "unusable/empty result"; }
                    }
                    catch (Exception ex) { err = ex.Message; }
                    sw.Stop();
                    int bytes = res != null ? res.Length : 0;
                    string shape = res != null ? AnalysisEngine.DryRunShape(res) : "";
                    a.Calls++; if (ok) a.Ok++; else a.Fail++; a.Ms.Add((int)sw.ElapsedMilliseconds); a.Bytes.Add(bytes); a.Shapes.Add(shape);
                    string flag = ok ? "OK " : (optional ? "opt" : "ERR");
                    Line("  [" + flag + "] " + toolName.PadRight(26) + " " + ((int)sw.ElapsedMilliseconds).ToString().PadLeft(6) + "ms "
                        + bytes.ToString().PadLeft(8) + "b  " + shape + (err != null ? ("  <" + AnalysisEngine.Trunc(err, 120) + ">") : ""));
                }
                if (ct.IsCancellationRequested) { break; }
            }
            Line("DRY-RUN done.");

            var summaryOut = new List<object>();
            bool allStable = true;
            foreach (var kv in summary)
            {
                var a = kv.Value;
                int avg = a.Ms.Count > 0 ? (int)a.Ms.Average() : 0;
                int avgB = a.Bytes.Count > 0 ? (int)a.Bytes.Average() : 0;
                bool stable = a.Shapes.Count == 1 && a.Fail == 0;
                if (!stable) { allStable = false; }
                summaryOut.Add(new
                {
                    tool = kv.Key, calls = a.Calls, ok = a.Ok, fail = a.Fail,
                    avgMs = avg, avgBytes = avgB, shapes = a.Shapes.Count,
                    stable = stable, msMin = a.Ms.Count > 0 ? a.Ms.Min() : 0, msMax = a.Ms.Count > 0 ? a.Ms.Max() : 0
                });
            }

            return Json(new
            {
                ok = true,
                version = AnalysisEngine.ComponentVersion,
                debugAllowed = Engine.AiDebugAllowed(),   // v1.0.160.58: drives which controls the view offers
                asset = assetId, site = siteId, windowDays = days, runs = runs,
                toolsLoaded = loaded.Tools.Count,
                allStable = allStable,
                summary = summaryOut,
                log = log
            }, JsonRequestBehavior.AllowGet);
        }

        // ═════════════════════════════════════════════════════════
        //  POST /AiChat/Chat
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> Chat()
        {
            // v1.0.160.107 -- pin 3: identity FIRST, before the body read.
            Engine.CaptureLoginUser();
            Engine.Sess._roleSiteKeep = Engine.AiDebugAllowed();   // v1.0.160.108 H2: one role decision, pre-await
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync().ConfigureAwait(false);

            JArray messagesRaw;
            JObject chatPayload = null;
            try
            {
                chatPayload = JObject.Parse(body);
                messagesRaw = chatPayload["messages"] as JArray;
                if (messagesRaw == null) messagesRaw = new JArray();
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }

            if (messagesRaw.Count == 0)
            {
                Response.StatusCode = 400;
                return Content("No messages", "text/plain");
            }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            try
            {
                // v1.0.160.107 -- F7. FAIL CLOSED: refuse an instruction-extraction request BEFORE the
                // model and tools run, so no summary or paraphrase can be produced. Inside this try so
                // the finally below still emits [DONE] exactly once; EmptyResult because a bare return
                // cannot compile in Task<ActionResult>.
                if (AnalysisEngine.IsInstructionExtractionRequest(AnalysisEngine.LastRealUserText(messagesRaw)))
                {
                    WriteSse("text", new { text = FixedExtractionRefusal });
                    return new EmptyResult();
                }

                // v1.0.173.0: same plumbing gate as the follow-up path below.
                if (ScopeGuard.IsInternalsQuestion(AnalysisEngine.LastRealUserText(messagesRaw)))
                {
                    WriteSse("text", new { text = ScopeGuard.InternalsRefusal });
                    return new EmptyResult();
                }

                // v1.0.160.191: FOLLOW-UP CHAT GROUNDING. The chat used to call RunAgenticLoop with only
                // the messages, so the model re-discovered the alert every turn (search_sites ->
                // get_frs_alerts -> ...) and hit the step limit without answering. The web now sends the
                // SAME alert card (+ alertId/analysisId) it gave to analyze; we build a grounding system
                // prompt from that card PLUS the cause's FRS logic (CauseToLogic) so the model starts with
                // everything -- alert detail, thresholds, relays, resolved values, and the exact rule --
                // and answers in 1-2 steps instead of re-investigating.
                // v1.0.160.195 (memory-loss B): if the chat POST didn't carry the alert card, self-heal
                // the grounding from the stored analyze input (AlertInputJson) so the model starts grounded
                // and does not re-discover the alert and thrash to the step limit. Gated on
                // ChatGroundBackfill; on any miss groundPayload stays as chatPayload (prior behaviour).
                JObject groundPayload = chatPayload;
                if (_chatGroundBackfill && !AnalysisEngine.ChatPayloadHasCard(chatPayload))
                {
                    JObject stored = AnalysisEngine.LoadStoredAlertContext(chatPayload);
                    if (stored != null)
                    {
                        if (stored["alertId"] == null && chatPayload["alertId"] != null) { stored["alertId"] = chatPayload["alertId"]; }
                        if (stored["analysisId"] == null && chatPayload["analysisId"] != null) { stored["analysisId"] = chatPayload["analysisId"]; }
                        groundPayload = stored;
                        Engine.AiLog("INFO", "CHAT", "grounding self-healed from stored AlertInputJson (alertId=" + (chatPayload["alertId"] != null ? chatPayload["alertId"].ToString() : "?") + ")");
                    }
                }
                string chatGrounding = AnalysisEngine.BuildChatGrounding(groundPayload);
                // v1.0.160.197: bind the point-machine operation averages into the grounding so chat
                // returns the last-operation voltage/current deterministically. Guidance alone wasn't
                // enough (live_get is unparseable by the model; predict_pm_operation can be down). PT* +
                // assetId only; "" on any miss -> the .196 guidance still applies.
                try
                {
                    string pmVals = AnalysisEngine.BuildPmOperationValues(groundPayload);
                    if (!string.IsNullOrEmpty(pmVals)) { chatGrounding = (chatGrounding ?? "") + pmVals; }
                }
                catch { }
                await Engine.RunAgenticLoop(messagesRaw, chatGrounding).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteSse("error", new { message = AnalysisEngine.FriendlyError(ex) });
            }
            finally
            {
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
            }

            return new EmptyResult();
        }

        // POST /AiChat/AnalyzeAlert
        // Scoped single-alert analysis. Reuses the same MCP tools + agentic loop as Chat,
        // but with an analyze-specific system prompt and raw JSON output (no operational
        // filter, no out-of-scope gate). Streams the SAME SSE frames as Chat.
        [HttpPost]
        public async Task<ActionResult> AnalyzeAlert()
        {
            // v1.0.160.107 -- pin 3: identity FIRST, before the body read. HttpContext.Current does
            // not survive the first await, so a capture placed after ReadToEndAsync (as it was) can
            // silently see no user. CaptureLoginUser must precede AiDebugAllowed -- the latter reads
            // the fields the former populates.
            string body = await CaptureRoleAndReadBodyAsync().ConfigureAwait(false);   // 3b: shared prologue (role decided pre-await)

            JObject ctx;
            try
            {
                ctx = JObject.Parse(body);
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }
            // v1.0.160.190: self-heal a thin context before analysis (same as the JSON path).
            if (_backfillCtxEnabled)
            {
                try { await Engine.BackfillContextAsync(ctx, default(System.Threading.CancellationToken)).ConfigureAwait(false); }
                catch (Exception bfx) { Engine.AiLog("WARN", "BACKFILL", "context backfill skipped: " + AnalysisEngine.SanitizeJsonError(bfx.Message)); }
            }

            Engine.Sess._traceActive = true; // timing always accumulates; file writes gated separately
                                 // v1.0.160.107: capture already done at the action entry (see top of AnalyzeAlert).
            Engine.Sess._traceId = "sse-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Engine.Sess._reqStartUtc = DateTime.UtcNow;   // v1.0.160.57: for the audit totalMs
            Engine.Sess._auditAnalysisId = 0;             // v1.0.160.73
            Engine.Sess._trModelMs = 0; Engine.Sess._trToolMs = 0; Engine.Sess._trDiscoveryMs = 0; Engine.Sess._trTurns = 0; Engine.Sess._circuitSkipReason = null;
            Engine.Sess._trAnalysisTurns = 0; Engine.Sess._trToolCallMsSum = 0; Engine.Sess._trPrefetchWallMs = 0; Engine.Sess._trInLoopWallMs = 0; Engine.Sess._trSlowestTool = null; Engine.Sess._trSlowestToolMs = 0; Engine.Sess._trDistinctTools = 0; lock (Engine.Sess._trendLock) { Engine.Sess._trTagFetches.Clear(); }
            Engine.Sess._trBytesSent = 0; Engine.Sess._trBytesRecv = 0; Engine.Sess._trToolBytes = 0; Engine.Sess._trToolBytesKept = 0;
            Engine.Sess._trTokIn = 0; Engine.Sess._trTokOut = 0; Engine.Sess._trTokCacheRead = 0; Engine.Sess._trTokCacheWrite = 0; Engine.Sess._trTokThinking = 0; Engine.Sess._effortEscalated = false;
            Engine.Sess._trToolCallCount = 0;
            JObject rs0 = new JObject();
            rs0["endpoint"] = "AnalyzeAlert"; rs0["alertId"] = AnalysisEngine.GetCtx(ctx, "alertId");
            Engine.TraceAnalyze("request_start", rs0);

            Engine.Sess._prefetchCtx = ctx;
            // v1.0.160.1: role gate merged from the other team's drop -- a non-site-keeping user is
            // forced onto QUANTUM (deepseek) server-side, so hiding the picker is presentation only.
            // v1.0.160.108 H2a: role captured ONCE before the body-read await (top of the action);
            // the late SafeIsSiteKeeping() call is gone -- after an await HttpContext.Current may be
            // null, so a late read can silently return false and mis-gate.
            bool _siteKeep = Engine.Sess._roleSiteKeep;
            if (_siteKeep) { Engine.AiLog("INFO", "GATE", "site-keeping active for this request"); }
            Engine.Sess._provider = Engine.GateProviderForRole(AnalysisEngine.ResolveEngineAlias(AnalysisEngine.GetCtx(ctx, "provider")), _siteKeep);   // v1.0.160.107: opaque id -> raw BEFORE gating
            Engine.Sess._thinkReq = AnalysisEngine.ParseThinkCtx(AnalysisEngine.GetCtx(ctx, "think"));   // v1.0.160.42 UI toggle
                                                               // v1.0.160.73: open the audit row AFTER the provider/model are resolved, since the
                                                               // row records them. Returns 0 if the DB is off, dormant, or the route is absent --
                                                               // and 0 simply means no child writes happen. Nothing here can fail the verdict.
            Engine.Sess._auditAnalysisId = Engine.AuditStartAnalysis(ctx);

            // v1.0.160.189: expose AnalysisId as a response header (also on the SSE path). Harmless if
            // headers are already flushed; wrapped.
            ExposeAnalysisIdHeader();   // 3b: shared helper

            // v1.0.160.188: link the analysis to its SOURCE row. The web reuses ctx.alertId for the id
            // in BOTH paths, so a discriminator decides the column: ctx.source == "pending" (or
            // ctx.isPending true, or an explicit ctx.pendingAlertApprovalId) => the id is a
            // PendingAlertApproval.Id; otherwise it is an FRSAlert.Id. Exactly one id column is set.
            // The [SQLLINK] log prints what the context carried so you can verify each path.
            if (_analysisLinkEnabled && Engine.Sess._auditAnalysisId > 0)
            {
                long _idVal = 0; long.TryParse((AnalysisEngine.GetCtx(ctx, "alertId") ?? "").Trim(), out _idVal);
                long _explicitPending = 0; long.TryParse((AnalysisEngine.GetCtx(ctx, "pendingAlertApprovalId") ?? "").Trim(), out _explicitPending);
                string _src = (AnalysisEngine.GetCtx(ctx, "source") ?? "").Trim().ToLowerInvariant();
                string _isPending = (AnalysisEngine.GetCtx(ctx, "isPending") ?? "").Trim().ToLowerInvariant();
                bool _pending = _explicitPending > 0
                    || _src == "pending" || _src == "pendingalertapproval"
                    || _isPending == "true" || _isPending == "1";

                long _pendingId = _explicitPending > 0 ? _explicitPending : (_pending ? _idVal : 0);
                int _frsId = 0; if (!_pending && _idVal > 0 && _idVal <= int.MaxValue) { _frsId = (int)_idVal; }

                Engine.AiLog("INFO", "SQLLINK", "link source=" + (_pending ? "pending" : "frs")
                    + " id=" + _idVal + " (src='" + _src + "' isPending='" + _isPending + "' explicitPending=" + _explicitPending + ")"
                    + " analysisId=" + Engine.Sess._auditAnalysisId);
                try
                {
                    if (_pendingId > 0) { AnalysisEngine.WriteAnalysisLink(0, _pendingId, Engine.Sess._auditAnalysisId); }
                    else if (_frsId > 0) { AnalysisEngine.WriteAnalysisLink(_frsId, 0, Engine.Sess._auditAnalysisId); }
                }
                catch (Exception lex) { Engine.AiLog("WARN", "SQLLINK", "FRSAlert_AIAnalysis upsert skipped: " + AnalysisEngine.SanitizeJsonError(lex.Message)); }
            }

            // v1.0.160.79: reuse a stored analysis when it is still valid. Returns null on ANY doubt
            // -- DB off, dormant, miss, stale, unparseable -- and null simply means analyse normally.
            bool _reRun = string.Equals(AnalysisEngine.GetCtx(ctx, "force"), "true", StringComparison.OrdinalIgnoreCase);
            JObject _cachedEnv = Engine.TryServeFromCache(ctx, _reRun);
            if (_cachedEnv != null)
            {
                try
                {
                    // v1.0.160.147: UNWRAP. TryServeFromCache returns the stored ENVELOPE, which is
                    // {"verdict":{...}}. Wrapping it again produced {"verdict":{"verdict":{...}}},
                    // so the browser read every field one level too deep -- likely_cause, category,
                    // action and confidence all resolved to undefined, and served_from_cache sat on
                    // the inner object where the badge never looked. That is the INCONCLUSIVE / 0% /
                    // all-dashes render WITH a FRESH ANALYSIS badge on a run the log recorded as a
                    // cache HIT. The fresh path sends the verdict object itself, so the cached path
                    // must send the same shape, not a shape one level deeper.
                    // v1.0.160.180: a cached verdict may have been produced while the alert was
                    // ACTIVE, in which case its stored context_patch carries an empty resetTime and
                    // the panel would print "Still active" even after the alert has cleared. Re-check
                    // the LIVE FRS row and, only when it is now reset, inject the current stamp so the
                    // served panel shows the exact current state. Gated to the empty-reset case: a
                    // cache that already holds a reset is finished history that cannot change, so it
                    // keeps its zero-latency serve and adds no fetch.
                    try
                    {
                        JObject _cpChk = _cachedEnv["context_patch"] as JObject;
                        string _cachedRst = (_cpChk != null && _cpChk["resetTime"] != null) ? _cpChk["resetTime"].ToString() : "";
                        if (_cachedRst.Length == 0)
                        {
                            string _liveRst = await Engine.TryLiveResetTimeAsync(ctx, System.Threading.CancellationToken.None).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(_liveRst))
                            {
                                if (_cpChk == null) { _cpChk = new JObject(); _cachedEnv["context_patch"] = _cpChk; }
                                _cpChk["resetTime"] = _liveRst;
                            }
                        }
                    }
                    catch { }
                    // v1.0.160.149: replay the side blocks BEFORE the verdict, in the order a fresh
                    // run emits them -- the view merges context_patch before it renders, so sending
                    // it after the verdict would arrive too late to be used.
                    try
                    {
                        JObject _cp = _cachedEnv["context_patch"] as JObject;
                        if (_cp != null && _cp.Count > 0) { WriteSse("context_patch", new { patch = _cp }); }
                        JObject _ac = _cachedEnv["asset_condition"] as JObject;
                        if (_ac != null) { WriteSse("asset_condition", new { asset_condition = _ac }); }
                        JObject _sn = _cachedEnv["snapshot"] as JObject;
                        if (_sn != null) { WriteSse("snapshot", new { snapshot = _sn }); }
                    }
                    catch { }
                    JObject _cachedVerdict = (_cachedEnv["verdict"] as JObject) ?? _cachedEnv;
                    WriteSse("verdict", new { verdict = _cachedVerdict });
                    WriteSse("stats", new { cached = true });
                    // v1.0.160.138 (audit Gap 1): CLOSE the row this request opened. Without this,
                    // AuditStartAnalysis fires, the verdict is served from store, and the method
                    // returns leaving the row RUNNING forever -- which then blocks the cache for that
                    // alert on every later open. Serving from store is still an operator action worth
                    // recording, so it is completed with a servedFromCache marker rather than not
                    // opened at all.
                    try
                    {
                        // v1.0.160.140: carry the SERVED verdict into the row. Completing with an
                        // empty _lastResponseFull would write a COMPLETED row with no payload -- and
                        // a payload-less COMPLETED row is exactly what blocked this alert's cache in
                        // the first place. Closing an audit row must not manufacture the next
                        // blockage.
                        try
                        {
                            if (string.IsNullOrEmpty(Engine.Sess._lastResponseFull) && _cachedEnv != null)
                            { Engine.Sess._lastResponseFull = _cachedEnv.ToString(Newtonsoft.Json.Formatting.None); }
                        }
                        catch { }
                        JObject _cAudit = new JObject();
                        _cAudit["servedFromCache"] = true;
                        Engine.AuditCompleteAnalysis(_cAudit, false, null);
                    }
                    catch { }
                }
                catch { }
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
                return new EmptyResult();
            }
            // Same per-request state the verdict path sets up. Without it the streaming
            // popup ran with NO argument clamps, NO budget caps, NO usability check and
            // NO payload narrowing -- every safeguard built for verdicts was skipped on
            // the path a user actually watches.
            Engine.Sess._jsonToolUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Engine.Sess._jsonToolTotal = 0;
            Engine.Sess._jsonToolOk = 0;
            Engine.Sess._jsonAssetHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Engine.Sess._prefetchDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Engine.Sess._jsonToolTrace = new List<string>();
            lock (AnalysisEngine._jsonDiagLock) { Engine.Sess._jsonDiag = new List<string>(); }
            DateTime incSse;
            Engine.Sess._incidenceEpoch = AnalysisEngine.TryParseAlertTime(AnalysisEngine.GetCtx(ctx, "time"), out incSse) ? AnalysisEngine.ToEpochIst(incSse) : 0;
            string userMsg = AnalysisEngine.BuildAnalyzeUserMessage(ctx);
            JObject pr0 = new JObject();
            pr0["promptVersion"] = "analyze-v1.0.9-narrative";
            pr0["system"] = AnalysisEngine.TraceTrunc(AnalysisEngine.RedactForTrace(AnalysisEngine.AnalyzeSystemPrompt()), 20000);
            pr0["user"] = AnalysisEngine.TraceTrunc(AnalysisEngine.RedactForTrace(userMsg), 20000);
            // v1.0.160.68: reuse the SAME redacted+truncated text for the audit record, so the audit
            // inherits RedactForTrace rather than carrying a second, unredacted copy of the prompt.
            Engine.Sess._lastPromptFull = pr0.ToString(Newtonsoft.Json.Formatting.None);
            Engine.TraceAnalyze("prompt", pr0);
            JArray messages = new JArray();
            JObject um = new JObject();
            um["role"] = "user";
            um["content"] = userMsg;
            messages.Add(um);

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            // v1.0.55.11: emit the run traceId to the client (for the per-alert Tool trace lookup).
            // MUST be after the SSE headers above -- writing before them corrupts the stream.
            try { WriteSse("meta", new { traceId = Engine.Sess._traceId }); } catch { }
            // v1.0.160.162: greeting header (option D), OFF by default, logged-in sessions only.
            // Emitted here so it paints before the first step. Never gates or delays the verdict.
            if (AnalysisEngine._greetingEnabled)
            {
                try
                {
                    string g = Engine.BuildGreeting(ctx, false);
                    if (!string.IsNullOrEmpty(g))
                    {
                        WriteSse("greeting", new { text = g });
                    }
                }
                catch { }
            }
            // v1.0.160.142: analysisId belongs HERE, with meta, for the same reason -- after the
            // headers. Emitting it at AuditStartAnalysis (v1.0.160.139) wrote to the stream before
            // the headers existed and blanked the popup.
            if (Engine.Sess._auditAnalysisId > 0)
            { try { WriteSse("analysis_id", new { analysisId = Engine.Sess._auditAnalysisId }); } catch { } }

            try
            {
                await Engine.RunAgenticLoop(messages, AnalysisEngine.AnalyzeSystemPrompt(), true, true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteSse("error", new { message = AnalysisEngine.FriendlyError(ex) });
            }
            finally
            {
                JObject re0 = new JObject();
                re0["turns"] = Engine.Sess._trTurns; re0["modelMs"] = Engine.Sess._trModelMs;
                re0["toolMs"] = Engine.Sess._trToolMs; re0["discoveryMs"] = Engine.Sess._trDiscoveryMs;
                re0["tokIn"] = Engine.Sess._trTokIn; re0["tokOut"] = Engine.Sess._trTokOut;
                re0["cacheRead"] = Engine.Sess._trTokCacheRead; re0["cacheWrite"] = Engine.Sess._trTokCacheWrite;
                if (Engine.Sess._lastVerdictSummary != null) { re0["verdict"] = Engine.Sess._lastVerdictSummary; }
                re0["causeCode"] = AnalysisEngine.GetCtx(ctx, "causeCode");   // v1.0.133.0: scope for candidate promote
                re0["siteId"] = AnalysisEngine.GetCtx(ctx, "siteId");
                re0["station"] = AnalysisEngine.GetCtx(ctx, "station");
                // v1.0.160.192: per-cause optimization trace -- surface the timing breakdown in the Log
                // section so you can see where an alert's response time went without the offline script.
                long _rqTotal = Engine.Sess._trModelMs + Engine.Sess._trToolMs + Engine.Sess._trDiscoveryMs;
                re0["totalMs"] = _rqTotal;
                re0["prefetchWallMs"] = Engine.Sess._trPrefetchWallMs;
                re0["inLoopWallMs"] = Engine.Sess._trInLoopWallMs;
                re0["toolCallMsSum"] = System.Threading.Interlocked.Read(ref Engine.Sess._trToolCallMsSum);
                re0["prefetchCalls"] = Engine.Sess._trPrefetchCalls;
                re0["inLoopCalls"] = Engine.Sess._trToolCallCount;
                re0["slowestTool"] = Engine.Sess._trSlowestTool ?? "";
                re0["slowestToolMs"] = Engine.Sess._trSlowestToolMs;
                re0["mode"] = Engine.Sess._synthesisMode ? "synthesis" : "agentic";
                re0["coverage"] = Engine.Sess._lastCoverageKind.ToString();
                try {
                    Engine.AiLog("INFO", "TIMING", "cause=" + AnalysisEngine.GetCtx(ctx, "causeCode")
                        + " total=" + _rqTotal + "ms model=" + Engine.Sess._trModelMs + " tool=" + Engine.Sess._trToolMs
                        + " prefetch=" + Engine.Sess._trPrefetchWallMs + " inLoop=" + Engine.Sess._trInLoopWallMs
                        + " turns=" + Engine.Sess._trTurns + " calls=" + (Engine.Sess._trPrefetchCalls + Engine.Sess._trToolCallCount)
                        + " slowest=" + (Engine.Sess._trSlowestTool ?? "-") + ":" + Engine.Sess._trSlowestToolMs + "ms"
                        + " mode=" + (Engine.Sess._synthesisMode ? "synthesis" : "agentic"));
                } catch { }
                // 1.0.197.0: token totals for this alert analysis
                if (Engine.Sess._trTurns > 0)
                {
                    try
                    {
                        Engine.AiLog("INFO", "TOKENS", TokensLine(ctx, _rqTotal));
                    }
                    catch
                    {
                    }
                }
                // v1.0.160.193: per-tag fetch breakdown for the Log -- each attr fetched with ms/bytes and
                // USED (in driver set / trigger) vs WASTED (fetched but not needed). Spot unwanted/heavy fetches.
                try
                {
                    string dset = (Engine.Sess._driverSetStr ?? "").ToUpperInvariant();
                    JArray tagArr = new JArray();
                    List<JObject> snap; lock (Engine.Sess._trendLock) { snap = new List<JObject>(Engine.Sess._trTagFetches); }
                    long wastedMs = 0; int wastedN = 0;
                    foreach (JObject tf in snap)
                    {
                        string attr = tf["attr"] != null ? tf["attr"].ToString() : "";
                        bool used = !string.IsNullOrEmpty(attr) && dset.Contains(attr.ToUpperInvariant());
                        tf["used"] = used;
                        if (!used) { wastedMs += (long)(tf["ms"] ?? 0); wastedN++; }
                        tagArr.Add(tf);
                    }
                    re0["tagFetches"] = tagArr;
                    re0["tagFetchCount"] = snap.Count;
                    re0["tagWastedCount"] = wastedN;
                    re0["tagWastedMs"] = wastedMs;
                    if (wastedN > 0)
                    {
                        System.Text.StringBuilder wb = new System.Text.StringBuilder();
                        foreach (JObject tf in snap) { if (tf["used"] != null && !(bool)tf["used"]) { wb.Append(tf["attr"]).Append("(").Append(tf["ms"]).Append("ms) "); } }
                        Engine.AiLog("INFO", "TAGOPT", "cause=" + AnalysisEngine.GetCtx(ctx, "causeCode") + " fetched=" + snap.Count
                            + " wasted=" + wastedN + " wastedMs=" + wastedMs + " -> " + wb.ToString().Trim());
                    }
                }
                catch { }
                Engine.TraceAnalyze("request_end", re0);
                // v1.0.160.57: the 7W audit record. try/catch so an audit failure can never affect a
                // verdict that has already been produced.
                if (_audit7W)
                {
                    try
                    {
                        JObject _a7 = Build7WAudit(ctx, "verdict", Engine.Sess._reqStartUtc);
                        Engine.TraceAnalyze("audit_7w", _a7);
                        // v1.0.160.141: store the VERDICT before completing the row. Without this the
                        // SSE path writes AnalysisStatus=COMPLETED with AnalysisOutputJson empty, and
                        // the verdict cache can never serve it -- which is exactly what
                        // "stored row has no verdict payload" was reporting. The envelope is built the
                        // same way the JSON path builds it, so a served row and a fresh one match.
                        try
                        {
                            if (string.IsNullOrEmpty(Engine.Sess._lastResponseFull) && Engine.Sess._lastVerdictSummary != null)
                            {
                                JObject _env = new JObject();
                                _env["verdict"] = Engine.Sess._lastVerdictSummary;
                                Engine.Sess._lastResponseFull = _env.ToString(Newtonsoft.Json.Formatting.None);
                            }
                        }
                        catch { }
                        Engine.AuditCompleteAnalysis(_a7, false, null);   // v1.0.160.73: same object as the JSONL
                        Engine.AuditRequestAndCalls(_a7, ctx);            // v1.0.160.78: Request + its MCP calls
                                                                   // v1.0.160.57: also push it to the client so the per-run Log download can carry
                                                                   // it. The "meta" event fires BEFORE the run (traceId only); the audit is only
                                                                   // known at the end, so it needs its own event. Emitted before [DONE].
                                                                   // v1.0.160.119: DO NOT SEND IT AT ALL unless privileged. The per-run
                                                                   // diagnostic is built client-side from this event, so withholding the event
                                                                   // is the actual control; redaction below is defence in depth, not the fix.
                                                                   // The server-side audit row and JSONL above are untouched -- ops keeps the
                                                                   // real values, which is where they belong.
                        if (Engine.Sess._roleSiteKeep)
                        {
                            // v1.0.160.156: CALLING NAMES ONLY, even for privileged sessions (owner:
                            // "imprint calling names instead"). Always the redacted copy: engine
                            // keeps the opaque calling id, provider/model/providerUrl removed, and
                            // the serialised scrub catches vendor strings anywhere else. The team
                            // maps calling name -> vendor internally; the server-side audit row and
                            // JSONL keep the real values.
                            WriteSse("audit", new { audit = AnalysisEngine.RedactAuditForClient(_a7, false) });
                        }
                        else
                        {
                            // Tell the client explicitly, so it can CLEAR any copy left over from an
                            // earlier privileged run in the same page.
                            WriteSse("audit_denied", new { reason = "restricted" });
                        }
                    }
                    catch { }
                }
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
            }

            return new EmptyResult();
        }

        // -- v1.0.6 rev2 -- service-to-service verdict endpoint. Returns ONE
        // JSON envelope; never streams. Auth: X-Internal-Api-Key vs appSettings
        // InternalApiKey. Cooperative cancellation: the loop observes the token
        // at every await/iteration boundary; the AI/MCP calls in flight at cancel
        // time (at most one model call or one parallel tool chunk of <=4) drain in
        // background -- observed, slot-counted and TTL-bounded (no abort available). Tool use is
        // ENFORCED read-only + budgeted in verdict mode (see JsonToolPermitted),
        // and the verdict is schema-validated + operational-action filtered.
        // No unhandled exception escapes this action (outer boundary below).
        [HttpPost]
        public async Task<ActionResult> AnalyzeAlertJson()
        {
            // v1.0.160.107 -- pin 3: identity FIRST, before Core reads the body.
            Engine.CaptureLoginUser();
            Engine.Sess._roleSiteKeep = Engine.AiDebugAllowed();   // v1.0.160.108 H2
            try
            {
                return await AnalyzeAlertJsonCore().ConfigureAwait(false);
            }
            catch
            {
                // absolute outer boundary: manually built, serializer-free reply
                try { Response.StatusCode = 200; } catch { }
                return Content("{\"status\":\"model_error\",\"verdict\":null,\"meta\":{\"error\":\"internal\"}}", "application/json");
            }
        }

        // presetCtxJson != null  =>  IN-PROCESS call from another controller in
        // this application. The shared-key check and the HTTP body read are then
        // skipped: the caller is already inside the authenticated pipeline and
        // supplies the context directly. Everything after that -- budget,
        // grounding gate, schema validation, envelope -- is identical, so an
        // in-process verdict is byte-for-byte what the HTTP endpoint returns.
        // ------------------------------------------------------------------
        // IN-PROCESS ENTRY POINT
        // Call this from another controller in the same application instead of
        // making the site issue an HTTP request to itself. No URL, no shared
        // key, no TLS, and no ASP.NET session-lock deadlock risk.
        //
        //   var ai = new AiChatController();
        //   ai.ControllerContext = this.ControllerContext;
        //   string envelopeJson = await ai.ProduceVerdictJsonAsync(ctxJson);
        //
        // Returns the SAME envelope JSON the HTTP endpoint returns. The caller's
        // own Response.StatusCode is preserved -- this never rewrites it.
        // ------------------------------------------------------------------
        public async Task<string> ProduceVerdictJsonAsync(string ctxJson)
        {
            int savedStatus = 200;
            try { savedStatus = Response.StatusCode; } catch { }
            try
            {
                ActionResult r = await AnalyzeAlertJsonCore(ctxJson ?? "").ConfigureAwait(false);
                ContentResult c = r as ContentResult;
                if (c != null && !string.IsNullOrEmpty(c.Content)) { return c.Content; }
                return "{\"status\":\"model_error\",\"verdict\":null,\"meta\":{\"error\":\"no content\"}}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("[ProduceVerdictJsonAsync] " + ex.ToString());
                return "{\"status\":\"model_error\",\"verdict\":null,\"meta\":{\"error\":\"internal\"}}";
            }
            finally
            {
                // the in-process path must not leak its status onto the caller
                try { Response.StatusCode = savedStatus; } catch { }
            }
        }

        private async Task<ActionResult> AnalyzeAlertJsonCore(string presetCtxJson = null)
        {
            DateTime t0 = DateTime.UtcNow;
            bool gateHeld = false;
            Func<string, object, ActionResult> envelope = (status, verdict) =>
            {
                JObject env = new JObject();
                env["status"] = status;
                env["verdict"] = verdict == null ? null : JToken.FromObject(verdict);
                JObject meta = new JObject();
                meta["promptVersion"] = "analyze-v1.0.9-narrative";
                meta["componentVersion"] = AnalysisEngine.ComponentVersion;
                string cfgModel = Engine.UseOpenAi
                    ? AiModelConfig.ModelFor("openai", AnalysisEngine.ReadStr("OpenAiModel", "gpt-5.4"))
                    : AiModelConfig.ModelFor("anthropic", ConfigurationManager.AppSettings["AnthropicModel"]);
                // v1.0.160.107 -- F1-response: the configured model id names the vendor. Masked for
                // EVERY browser response (privileged included); it remains in logs/audit.
                if (Engine.AiDebugAllowed()) { meta["model"] = "configured"; }
                meta["durationMs"] = (long)(DateTime.UtcNow - t0).TotalMilliseconds;
                meta["requestId"] = Engine.Sess._jsonRequestId ?? "";
                if (Engine.Sess._auditAnalysisId > 0)
                {
                    meta["analysisId"] = Engine.Sess._auditAnalysisId;
                }
                meta["toolCalls"] = Engine.Sess._jsonToolTotal;
                meta["toolSucceeded"] = Engine.Sess._jsonToolOk;
                if (AnalysisEngine._evidenceProvenanceEnabled)
                {
                    // Item 6: surface the evidence-provenance tally on the verdict meta (best-effort).
                    // v1.0.160.182: ENFORCE first -- demote fabricated [fetched] claims when the alerting
                    // tag's history does not cover the incidence -- so the tally below reflects reality.
                    if (_provEnforceEnabled)
                    {
                        try { Engine.EnforceEvidenceProvenance(env["verdict"] as JObject, Engine.Sess._lastCoverageKind); }
                        catch { }
                    }
                    try { JObject _prov = AnalysisEngine.ComputeEvidenceProvenance(env["verdict"] as JObject); if (_prov != null) { meta["provenance"] = _prov; } }
                    catch { }
                }
                // v1.0.160.186 (#1/#4/#7): force viz.threshold (+metric/value/unit) from the card's
                // triggering condition so "The numbers" matches what fired -- lower rule -> max of the
                // FAIL thresholds (= max(avg*LD%, MinSafe)), higher rule -> min (= min(avg*LD%, MaxSafe)).
                if (_vizCardThresholdEnabled)
                {
                    try { Engine.OverrideVizThresholdFromCard(env["verdict"] as JObject, Engine.Sess._prefetchCtx); }
                    catch { }
                }
                meta["bgReserved"] = System.Threading.Interlocked.CompareExchange(ref AnalysisEngine._jsonBgReserved, 0, 0);
                meta["turns"] = Engine.Sess._trTurns;
                meta["modelMs"] = Engine.Sess._trModelMs;
                meta["toolMs"] = Engine.Sess._trToolMs;
                meta["discoveryMs"] = Engine.Sess._trDiscoveryMs;
                meta["bytesSentToModel"] = Engine.Sess._trBytesSent;
                meta["bytesRecvFromModel"] = Engine.Sess._trBytesRecv;
                meta["toolBytesRaw"] = Engine.Sess._trToolBytes;
                meta["toolBytesKept"] = Engine.Sess._trToolBytesKept;
                if (Engine.Sess._trTokIn > 0 || Engine.Sess._trTokOut > 0)
                {
                    meta["tokensIn"] = Engine.Sess._trTokIn;
                    meta["tokensOut"] = Engine.Sess._trTokOut;
                    meta["promptCacheEnabled"] = AnalysisEngine._promptCache;
                    // enabled but never read => the marker is not reaching the API
                    // v1.0.160.110 SL6b: _trAnalysisTurns, NOT _trTurns. _trTurns includes the Hindi
                    // translation (a second billable call), so this fired on every bilingual
                    // single-turn run whether or not the cache was actually broken.
                    if (AnalysisEngine._promptCache && Engine.Sess._trTokCacheRead == 0 && Engine.Sess._trAnalysisTurns > 1)
                    {
                        // v1.0.160.108 H1: library name removed -- this string is server-side only now,
                        // but it must not name a vendor library even in the audit-facing object.
                        meta["promptCacheWarning"] = "cache enabled but 0 cache reads across "
                            + Engine.Sess._trTurns + " turns -- the client may be dropping cache_control";
                    }
                    if (Engine.Sess._trTokCacheRead > 0) { meta["tokensCacheRead"] = Engine.Sess._trTokCacheRead; }
                    if (Engine.Sess._trTokCacheWrite > 0) { meta["tokensCacheWrite"] = Engine.Sess._trTokCacheWrite; }
                }
                if (status == "timeout") { meta["timingPartial"] = true; } // in-flight phases may not have recorded yet
                if (Engine.Sess._jsonEvidenceArgs != null) { meta["evidenceArgs"] = Engine.Sess._jsonEvidenceArgs; }
                if (Engine.Sess._jsonFaultDetail != null) { meta["fault"] = Engine.Sess._jsonFaultDetail; }
                meta["mode"] = Engine.Sess._synthesisMode ? "synthesis (1 turn, no tools)" : "agentic";
                // v1.0.160.107 -- F1-response: opaque id only; the raw ProviderKind stays in the
                // audit row and the server log, never in a browser response.
                // 1.0.195.0: "" (no provider sent) is not null -- report the engine that actually ran
                string effEngine = !string.IsNullOrEmpty(Engine.Sess._provider) ? Engine.Sess._provider : AiModelConfig.DefaultEngine(Engine.ProviderKind);
                meta["provider"] = AnalysisEngine.OpaqueProviderId(effEngine);
                if (Engine.Sess._prefetchGap != null) { meta["prefetchGap"] = Engine.Sess._prefetchGap; }
                if (Engine.Sess.coverageDowngrade != null) { meta["coverageDowngrade"] = Engine.Sess.coverageDowngrade; }
                if (Engine.AlertingTagId() != null)
                {
                    meta["coverageTagId"] = Engine.AlertingTagId();
                    if (Engine.Sess.coverageTagPresumed) { meta["coverageTagPresumed"] = true; }
                }
                if (AnalysisEngine._aiCacheParamMissing) { meta["promptCacheWarning"] = "Domain.dll predates the cacheSystem parameter; caching is off"; }
                if (Engine.Sess._jsonToolTrace != null && Engine.Sess._jsonToolTrace.Count > 0)
                {
                    meta["toolTrace"] = string.Join(" | ", Engine.Sess._jsonToolTrace);
                }
                string diag = Engine.SnapshotDiag();
                if (diag.Length > 0) { meta["diagnostics"] = diag; }
                if (Engine.Sess._jsonCaptureError != null) { meta["error"] = AnalysisEngine.SanitizeJsonError(Engine.Sess._jsonCaptureError); }
                // v1.0.160.108 H1: emit a WHITELIST, built fresh. .107 masked only provider/model and
                // still shipped fault/toolTrace/diagnostics/promptCacheWarning/tokens/library-name to
                // every caller. A whitelist (not a filter over the existing object) means a key added
                // later is invisible by DEFAULT rather than leaking until someone notices.
                env["meta"] = AnalysisEngine.PublicMeta(meta, Engine.Sess._roleSiteKeep);
                // v1.0.160.112 SL12: its OWN field, not a caveat. Caveats hedge THIS verdict; a
                // standing condition is a separate finding with its own owner and action.
                JObject _assetCond = Engine.BuildAssetCondition();
                if (_assetCond != null) { env["asset_condition"] = _assetCond; }
                // v1.0.160.112 SL17: retrievedAt (NOT "asOf" -- analysis time is not measurement
                // time) plus the age from incidence, so a snapshot taken hours later cannot be read
                // as an at-alert reading. No occupancy claim: the TPR fetch ends at incidence+2h.
                if (Engine.Sess._snapshotRetrievedAt != DateTime.MinValue)
                {
                    JObject snap = new JObject();
                    snap["retrievedAt"] = Engine.Sess._snapshotRetrievedAt.ToString("dd/MM/yyyy HH:mm:ss");
                    DateTime _incF;
                    if (Engine.Sess._prefetchCtx != null && AnalysisEngine.TryParseAlertTime(AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "time"), out _incF))
                    { snap["ageFromIncidenceSec"] = (long)(Engine.Sess._snapshotRetrievedAt - _incF).TotalSeconds; }
                    if (Engine.Sess._snapshotValues != null && Engine.Sess._snapshotValues.Count > 0) { snap["values"] = Engine.Sess._snapshotValues; }
                    env["snapshot"] = snap;
                }
                // v1.0.160.110 SL7: return a MINIMAL patch the view merges into its stored context,
                // so the follow-up AnalyzeChat posts back populated fields. Mutating ctx server-side
                // does not reach the browser. Only these two keys -- never the full server context.
                if (Engine.Sess._prefetchCtx != null)
                {
                    JObject patch = new JObject();
                    string stn = AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "station");
                    string atp = AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "assetType");
                    if (stn.Length > 0) { patch["station"] = stn; }
                    if (atp.Length > 0) { patch["assetType"] = atp; }
                    // v1.0.160.130: resetTime too. Same reason as assetType -- the browser cannot
                    // reliably derive it, and getting it wrong prints "Still active" on a cleared alert.
                    string rst = AnalysisEngine.GetCtx(Engine.Sess._prefetchCtx, "resetTime");
                    if (rst.Length > 0) { patch["resetTime"] = rst; }
                    if (patch.Count > 0) { env["context_patch"] = patch; }
                }
                JObject tv = new JObject();
                tv["status"] = status; tv["totalMs"] = meta["durationMs"]; tv["turns"] = Engine.Sess._trTurns;
                tv["modelMs"] = Engine.Sess._trModelMs; tv["toolMs"] = Engine.Sess._trToolMs; tv["discoveryMs"] = Engine.Sess._trDiscoveryMs;
                tv["toolCalls"] = Engine.Sess._jsonToolTotal; tv["toolSucceeded"] = Engine.Sess._jsonToolOk;
                tv["bytesSentToModel"] = Engine.Sess._trBytesSent; tv["bytesRecvFromModel"] = Engine.Sess._trBytesRecv;
                tv["toolBytesRaw"] = Engine.Sess._trToolBytes; tv["toolBytesKept"] = Engine.Sess._trToolBytesKept;
                tv["tokensIn"] = Engine.Sess._trTokIn; tv["tokensOut"] = Engine.Sess._trTokOut;
                tv["tokensCacheRead"] = Engine.Sess._trTokCacheRead;
                // average sent per turn: the repetition cost of the agentic loop
                tv["avgSentPerTurn"] = Engine.Sess._trTurns > 0 ? (Engine.Sess._trBytesSent / Engine.Sess._trTurns) : 0;
                // v1.0.160.98: `tv` is the TIMING envelope, not the verdict. Store the verdict
                // envelope instead, and only if the streaming path has not already captured one.
                if (string.IsNullOrEmpty(Engine.Sess._lastResponseFull))
                {
                    try { Engine.Sess._lastResponseFull = env.ToString(Newtonsoft.Json.Formatting.None); } catch { }
                }
                // v1.0.160.186: close the row Item 1 opened. envelope() is the ONE choke point every
                // return path passes through, so a row cannot be left RUNNING. _prefetchCtx, not ctx:
                // this lambda is declared before ctx exists, so ctx is not in its scope. Early returns
                // (bad_request/unauthorized) never reach Start, so _auditAnalysisId is 0 and this is
                // skipped. try/catch: an audit failure must never affect a verdict already produced.
                if (Engine.Sess._auditAnalysisId > 0)
                {
                    try
                    {
                        JObject auditRow = Build7WAudit(Engine.Sess._prefetchCtx, "verdict-json", Engine.Sess._reqStartUtc);
                        Engine.AuditCompleteAnalysis(auditRow, status != "ok", status == "ok" ? null : status);
                        Engine.AuditRequestAndCalls(auditRow, Engine.Sess._prefetchCtx);
                    }
                    catch
                    {
                    }
                }
                // 1.0.195.0: 7W audit line (who/which engine-provider-model) for the JSON path too --
                // AnalyzeAlert writes one, this path did not. Only when a model was called.
                if (_audit7W && Engine.Sess._trTurns > 0)
                {
                    try
                    {
                        Engine.TraceAnalyze("audit_7w", Build7WAudit(Engine.Sess._prefetchCtx, "verdict-json", Engine.Sess._reqStartUtc));
                    }
                    catch
                    {
                    }
                }
                Engine.TraceAnalyze("envelope", tv);
                // 1.0.197.0: token totals for this alert analysis (JSON path)
                if (Engine.Sess._trTurns > 0)
                {
                    try
                    {
                        long jsonMs = 0;
                        long.TryParse(meta["durationMs"] != null ? meta["durationMs"].ToString() : "0", out jsonMs);
                        Engine.AiLog("INFO", "TOKENS", TokensLine(Engine.Sess._prefetchCtx, jsonMs));
                    }
                    catch
                    {
                    }
                }
                Engine.AiLog(status == "ok" ? "INFO" : "WARN", "REQ",
                    "verdict end status=" + status + " turns=" + Engine.Sess._trTurns
                    + " totalMs=" + meta["durationMs"] + " modelMs=" + Engine.Sess._trModelMs + " toolMs=" + Engine.Sess._trToolMs
                    + " tools=" + Engine.Sess._jsonToolTotal + "/" + Engine.Sess._jsonToolOk + "ok"
                    + " sent=" + Engine.Sess._trBytesSent + "B recv=" + Engine.Sess._trBytesRecv + "B"
                    + " tokIn=" + Engine.Sess._trTokIn + " tokOut=" + Engine.Sess._trTokOut
                    + (Engine.Sess._jsonCaptureError != null ? " err=" + AnalysisEngine.SanitizeJsonError(Engine.Sess._jsonCaptureError) : ""));
                return Content(env.ToString(Formatting.None), "application/json");
            };
            try { Response.TrySkipIisCustomErrors = true; } catch { }

            if (presetCtxJson == null)
            {
                string cfgKey = ConfigurationManager.AppSettings["InternalApiKey"];
                if (string.IsNullOrEmpty(cfgKey))
                {
                    Response.StatusCode = 403;
                    return envelope("disabled", null);
                }
                string gotKey = Request.Headers["X-Internal-Api-Key"];
                if (!AnalysisEngine.FixedTimeEquals(cfgKey, gotKey))
                {
                    Response.StatusCode = 401;
                    return envelope("unauthorized", null);
                }
            }

            // read + validate BEFORE taking any permit, so slow/chunked bodies can
            // never occupy the gate; the read itself is byte-bounded AND time-bounded.
            string body;
            if (presetCtxJson != null)
            {
                if (presetCtxJson.Length > 262144)
                {
                    return envelope("bad_request", null);
                }
                body = presetCtxJson;
            }
            else
            {
                if (Request.ContentLength > 262144)
                {
                    Response.StatusCode = 400;
                    return envelope("bad_request", null);
                }
                System.Threading.CancellationToken earlyClientCt = System.Threading.CancellationToken.None;
                try { earlyClientCt = Response.ClientDisconnectedToken; } catch { }
                using (System.Threading.CancellationTokenSource readCts =
                    System.Threading.CancellationTokenSource.CreateLinkedTokenSource(earlyClientCt))
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        body = await AnalysisEngine.ReadBodyBoundedAsync(Request.InputStream, 262144, readCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        Response.StatusCode = 400;
                        return envelope("bad_request", null);
                    }
                }
            }
            if (body == null)
            {
                Response.StatusCode = 400;
                return envelope("bad_request", null);
            }
            JObject ctx;
            try { ctx = JObject.Parse(body); }
            catch { Response.StatusCode = 400; return envelope("bad_request", null); }
            string shapeReason;
            if (!AnalysisEngine.ValidateCtxShape(ctx, out shapeReason))
            {
                Engine.Sess._jsonCaptureError = shapeReason;
                Response.StatusCode = 400;
                return envelope("bad_request", null);
            }
            if (AnalysisEngine.GetCtx(ctx, "causeCode") == "" && AnalysisEngine.GetCtx(ctx, "description") == "" && AnalysisEngine.GetCtx(ctx, "alertId") == "")
            {
                Response.StatusCode = 400;
                return envelope("bad_request", null);
            }
            // v1.0.160.190: thin-context self-heal. When causeCode/siteId/assetId are missing but the
            // station+assetName+time are present, resolve them from the FRS alert record BEFORE analysis
            // (search_sites -> search_assets -> get_frs_alerts around time -> match this alert) so the
            // cause's dependent attributes + condition load from CauseToLogic/_gateById instead of the
            // model thrashing blind. Best-effort, gated, only fills blanks.
            if (_backfillCtxEnabled)
            {
                try { await Engine.BackfillContextAsync(ctx, default(System.Threading.CancellationToken)).ConfigureAwait(false); }
                catch (Exception bfx) { Engine.AiLog("WARN", "BACKFILL", "context backfill skipped: " + AnalysisEngine.SanitizeJsonError(bfx.Message)); }
            }
            string callerReqId = Request.Headers["X-Request-Id"];
            if (string.IsNullOrEmpty(callerReqId)) { callerReqId = AnalysisEngine.GetCtx(ctx, "requestId"); }
            if (!string.IsNullOrEmpty(callerReqId) && callerReqId.Length > 128)
            {
                callerReqId = null; // oversized caller id: never echoed, fallback used
            }
            Engine.Sess._jsonRequestId = !string.IsNullOrEmpty(callerReqId)
                ? callerReqId
                : AnalysisEngine.GetCtx(ctx, "alertId") + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            Engine.Sess._traceActive = true; // timing always accumulates; file writes gated separately
            Engine.Sess._traceId = Engine.Sess._jsonRequestId;
            Engine.Sess._trModelMs = 0; Engine.Sess._trToolMs = 0; Engine.Sess._trDiscoveryMs = 0; Engine.Sess._trTurns = 0; Engine.Sess._circuitSkipReason = null;
            Engine.Sess._trAnalysisTurns = 0; Engine.Sess._trToolCallMsSum = 0; Engine.Sess._trPrefetchWallMs = 0; Engine.Sess._trInLoopWallMs = 0; Engine.Sess._trSlowestTool = null; Engine.Sess._trSlowestToolMs = 0; Engine.Sess._trDistinctTools = 0; lock (Engine.Sess._trendLock) { Engine.Sess._trTagFetches.Clear(); }
            Engine.Sess._trBytesSent = 0; Engine.Sess._trBytesRecv = 0; Engine.Sess._trToolBytes = 0; Engine.Sess._trToolBytesKept = 0;
            Engine.Sess._trTokIn = 0; Engine.Sess._trTokOut = 0; Engine.Sess._trTokCacheRead = 0; Engine.Sess._trTokCacheWrite = 0; Engine.Sess._trTokThinking = 0; Engine.Sess._effortEscalated = false;
            Engine.Sess._trToolCallCount = 0;
            JObject rs = new JObject();
            rs["endpoint"] = "AnalyzeAlertJson"; rs["alertId"] = AnalysisEngine.GetCtx(ctx, "alertId");
            Engine.AiLog("INFO", "REQ", "verdict start alert=" + AnalysisEngine.GetCtx(ctx, "alertId")
                + " cause=" + AnalysisEngine.GetCtx(ctx, "causeCode") + " asset=" + AnalysisEngine.GetCtx(ctx, "assetName")
                + " station=" + AnalysisEngine.GetCtx(ctx, "station")
                + " card=" + (ctx["alertCard"] != null ? "yes" : "NO"));
            Engine.TraceAnalyze("request_start", rs);

            DateTime incTime;
            Engine.Sess._incidenceEpoch = AnalysisEngine.TryParseAlertTime(AnalysisEngine.GetCtx(ctx, "time"), out incTime) ? AnalysisEngine.ToEpochIst(incTime) : 0;
            string userMsg = AnalysisEngine.BuildAnalyzeUserMessage(ctx);
            if (userMsg.Length > 32000)
            {
                userMsg = userMsg.Substring(0, 32000) + " ...[context truncated]";
            }
            JObject pr = new JObject();
            pr["promptVersion"] = "analyze-v1.0.9-narrative";
            pr["system"] = AnalysisEngine.TraceTrunc(AnalysisEngine.RedactForTrace(AnalysisEngine.AnalyzeSystemPrompt()), 20000);
            pr["user"] = AnalysisEngine.TraceTrunc(AnalysisEngine.RedactForTrace(userMsg), 20000);
            Engine.TraceAnalyze("prompt", pr);
            Engine.Sess._prefetchCtx = ctx;
            Engine.Sess._provider = AnalysisEngine.ResolveEngineAlias(AnalysisEngine.GetCtx(ctx, "provider"));   // v1.0.160.107: opaque id -> raw
            Engine.Sess._thinkReq = AnalysisEngine.ParseThinkCtx(AnalysisEngine.GetCtx(ctx, "think"));   // v1.0.160.42 UI toggle
            // v1.0.160.186: open the audit row HERE too. Start must follow the provider/model
            // resolution (the row records them), and it must precede the loop so the id exists
            // when the envelope is built. Returns 0 when the DB is off/dormant/absent -- 0 simply
            // means no child writes and no analysisId, exactly as on the SSE path.
            Engine.Sess._reqStartUtc = t0;
            Engine.Sess._auditAnalysisId = Engine.AuditStartAnalysis(ctx);
            // v1.0.160.189: expose AnalysisId as a response header on AnalyzeAlertJson so the web can
            // read it off the response (reopen / rating / chat / SQL link) without parsing the body.
            ExposeAnalysisIdHeader();   // 3b: shared helper
            JArray messages = new JArray();
            JObject um = new JObject();
            um["role"] = "user";
            um["content"] = userMsg;
            messages.Add(um);

            int timeoutSec;
            if (!int.TryParse(ConfigurationManager.AppSettings["AnalyzeJsonTimeoutSec"], out timeoutSec) || timeoutSec <= 0)
            {
                timeoutSec = 120;
            }
            // v1.0.160.42 (owner rule): THINK runs 43-92s of hidden reasoning BEFORE any tool turn,
            // so the 120s deadline would kill a working run mid-verdict. With thinking on the
            // controller deadline is DISABLED. The request is still bounded by the DeepSeek
            // HttpClient timeout (DeepSeekHttpTimeoutSec, default 600) -- deliberately finite, since
            // AnalyzeMaxConcurrentVerdicts is 5 and a never-returning call would wedge a slot.
            if (Engine.UseDeepSeek && Engine.ThinkOn)
            {
                Engine.AiLog("INFO", "LIFE", "THINK on -- verdict deadline disabled (was " + timeoutSec + "s); bounded by DeepSeek HTTP timeout");
                timeoutSec = int.MaxValue / 1000;
            }

            // admission: RESERVE this request's worst-case abandonable weight
            // against the hard cap, then take a concurrency permit. Reservation
            // is settled by the wrapper-loop completion continuation below, so
            // there is no gap between gate release and background accounting.
            if (System.Threading.Interlocked.Add(ref AnalysisEngine._jsonBgReserved, AnalysisEngine._jsonMaxChunk) > _jsonBgOpCap)
            {
                System.Threading.Interlocked.Add(ref AnalysisEngine._jsonBgReserved, -AnalysisEngine._jsonMaxChunk);
                Response.StatusCode = 503;
                return envelope("busy", null);
            }
            bool reservationSettled = false;
            // v1.0.160.6: WAIT for a slot instead of instant-rejecting. WaitAsync (not .Wait)
            // keeps the request thread free while queued; the client-disconnect token abandons
            // the wait the moment the tab closes. Only a fully elapsed budget returns busy.
            // The reservation taken just above is HELD while queued -- deliberate, and why
            // _jsonBgOpCap is sized for running + queued.
            System.Threading.CancellationToken _admitCt = System.Threading.CancellationToken.None;
            try { _admitCt = Response.ClientDisconnectedToken; } catch { }
            bool _admitted = false;   // pre-initialised: rule 2 (CS0165) -- never rely on try/catch definite assignment
            try { _admitted = await _jsonGate.WaitAsync(_jsonAdmitWaitMs, _admitCt).ConfigureAwait(false); }
            catch (OperationCanceledException) { _admitted = false; }   // client left while queued
            catch (ObjectDisposedException) { _admitted = false; }      // token source torn down
            if (!_admitted)
            {
                System.Threading.Interlocked.Add(ref AnalysisEngine._jsonBgReserved, -AnalysisEngine._jsonMaxChunk);
                Engine.AiLog("WARN", "LIFE", "verdict admission timed out after " + _jsonAdmitWaitMs +
                    "ms (concurrency " + _jsonMaxConcurrent + ", queue " + _jsonQueueDepth + ")");
                Response.StatusCode = 503;
                return envelope("busy", null);
            }
            gateHeld = true;
            Engine.Sess._jsonAbandonedW = 0;
            try
            {
                Engine.Sess._jsonCapture = new StringBuilder();
                Engine.Sess._jsonCaptureError = null;

                Engine.Sess._jsonFaultDetail = null;
                lock (AnalysisEngine._jsonDiagLock) { Engine.Sess._jsonDiag = new List<string>(); }
                Engine.Sess._jsonToolUsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                Engine.Sess._jsonToolTotal = 0;
                Engine.Sess._jsonToolOk = 0;
                Engine.Sess._jsonGotEvidence = false;
                Engine.Sess._jsonGotHistory = false;
                Engine.Sess.gotUsableHistoryGet = false;
                Engine.Sess.requiredAlertingTagId = null;
                Engine.Sess.presumedAlertingTagId = null;
                Engine.Sess.coverageTagPresumed = false;
                Engine.Sess.coverageByTag = null;
                Engine.Sess._derivedAlert = false;            // v1.0.82.0 rev J
                Engine.Sess._derivedParam = null;
                Engine.Sess._derivedInputs = null;            // v1.0.109.0
                Engine.Sess._weatherCard = null;              // v1.0.119.0
                Engine.Sess._trainContext = null;             // v1.0.160.33
                Engine.Sess._computedVerdict = null;          // v1.0.160.37
                Engine.Sess._gateDroppedLast = 0; Engine.Sess._computedWonLast = false;   // v1.0.160.77
                Engine.Sess._triageAlertsRaw = null; Engine.Sess._triageAssetId = null; Engine.Sess._triageCauseCode = null; Engine.Sess._triageCurrentAlertId = null;  // v1.0.111.0
                Engine.Sess._derivedComponentsAdequate = false;
                Engine.Sess._cardHasFail = false; Engine.Sess._isFailureAlert = false;   // v1.0.160.111 (BUG-5)
                Engine.Sess._isTrackFailureCause = false;   // v1.0.160.170
                Engine.Sess._rawHasBreach = false;   // v1.0.160.173
                Engine.Sess._lastCoverageKind = AnalysisEngine.CoverageStateKind.Unverifiable;
                Engine.Sess.coverageDowngrade = null;
                Engine.Sess._jsonEvidenceArgs = null;
                Engine.Sess._jsonToolTrace = new List<string>();
                Engine.Sess._jsonAssetHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Engine.Sess._prefetchDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                System.Threading.CancellationToken clientCt = System.Threading.CancellationToken.None;
                try { clientCt = Response.ClientDisconnectedToken; } catch { }
                using (System.Threading.CancellationTokenSource linked =
                    System.Threading.CancellationTokenSource.CreateLinkedTokenSource(clientCt))
                {
                    Task loop = null;
                    try
                    {
                        loop = Engine.RunAgenticLoop(messages, AnalysisEngine.AnalyzeSystemPrompt(), true, false, linked.Token);
                        // settles the reservation exactly once, AFTER any
                        // abandonment registration inside the loop has run:
                        // keep credits equal to the abandoned weight (those are
                        // released by the real tasks' own continuations),
                        // release the rest.
                        reservationSettled = true;
                        loop.ContinueWith(lt =>
                        {
                            var _ = lt.Exception; // observe
                            int kept = System.Threading.Interlocked.Exchange(ref Engine.Sess._jsonAbandonedW, 0);
                            if (kept > AnalysisEngine._jsonMaxChunk) { kept = AnalysisEngine._jsonMaxChunk; }
                            System.Threading.Interlocked.Add(ref AnalysisEngine._jsonBgReserved, -(AnalysisEngine._jsonMaxChunk - kept));
                        });
                        Task deadline = Task.Delay(TimeSpan.FromSeconds(timeoutSec));
                        Task disconnect = Task.Delay(System.Threading.Timeout.Infinite, linked.Token);
                        Task done = await Task.WhenAny(loop, deadline, disconnect).ConfigureAwait(false);
                        if (done != loop)
                        {
                            // the UNDERLYING client call in flight is counted inside
                            // AwaitWithCancel; the wrapper loop needs observation only.
                            linked.Cancel();
                            AnalysisEngine.ObserveAbandoned(loop);
                            Engine.AiLog("WARN", "LIFE", "deadline " + timeoutSec + "s reached after " + Engine.Sess._trTurns + " turns");
                            return envelope("timeout", null);
                        }
                        await loop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (linked.Token.IsCancellationRequested)
                        {
                            if (loop != null) { AnalysisEngine.ObserveAbandoned(loop); }
                            return envelope("timeout", null);
                        }
                        Engine.Sess._jsonCaptureError = "AI request timed out";
                        return envelope("model_error", null);
                    }
                    catch (AnalysisEngine.ClientBusyException)
                    {
                        if (loop != null && !loop.IsCompleted) { AnalysisEngine.ObserveAbandoned(loop); }
                        Engine.Sess._jsonCaptureError = "client busy";
                        Engine.AiLog("WARN", "LIFE", "client lock unavailable -- refusing rather than running unserialized");
                        Response.StatusCode = 503;
                        return envelope("busy", null);
                    }
                    catch (Exception ex)
                    {
                        if (loop != null && !loop.IsCompleted) { AnalysisEngine.ObserveAbandoned(loop); }
                        Engine.AiLogEx("LIFE", "agentic loop threw", ex);
                        Engine.Sess._jsonFaultDetail = AnalysisEngine.DescribeFault(ex);
                        if (Engine.Sess._jsonCaptureError == null) { Engine.Sess._jsonCaptureError = AnalysisEngine.FriendlyError(ex); }
                        return envelope("model_error", null);
                    }
                }

                if (Engine.Sess._jsonCaptureError != null)
                {
                    return envelope("model_error", null);
                }
                string text = Engine.Sess._jsonCapture.ToString().Trim();
                Engine.Sess._jsonCapture = null;
                if (text.Length == 0)
                {
                    Engine.Sess._jsonCaptureError = "no verdict text produced (empty response or step limit)";
                    Engine.AiLog("WARN", "VERDICT", "no text after " + Engine.Sess._trTurns + " turns (step limit " + AnalysisEngine._maxIter + ")");
                    return envelope("model_error", null);
                }
                if (!Engine.Sess._jsonGotEvidence || !Engine.Sess._jsonGotHistory)
                {
                    // ok requires at least one USABLE history/trend result (value
                    // evidence), not merely a baseline or alert lookup.
                    Engine.Sess._jsonCaptureError = "no usable history/baseline/alert evidence; verdict ungrounded";
                    Engine.AiLog("WARN", "GROUND", "gate failed: evidence=" + Engine.Sess._jsonGotEvidence
                        + " history=" + Engine.Sess._jsonGotHistory + " | " + Engine.SnapshotDiag());
                    return envelope("ungrounded", null);
                }
                if (text.IndexOf("```", StringComparison.Ordinal) >= 0)
                {
                    text = text.Replace("```json", "").Replace("```", "").Trim();
                }
                JObject verdictObj = null;
                try { verdictObj = JObject.Parse(text); }
                catch
                {
                    // The model sometimes wraps the object in a sentence ("Here is
                    // the verdict: {...}"). Salvage the outermost balanced object
                    // rather than discarding a good verdict over its packaging.
                    verdictObj = AnalysisEngine.ExtractFirstJsonObject(text);
                }
                if (verdictObj == null)
                {
                    Engine.Sess._jsonCaptureError = "verdict not valid JSON";
                    Engine.AiLog("WARN", "VERDICT", "model output was not JSON and could not be salvaged");
                    return envelope("model_error", null);
                }
                string schemaReason;
                if (!Engine.ValidateAndSanitizeVerdict(verdictObj, out schemaReason))
                {
                    Engine.Sess._jsonCaptureError = "verdict schema invalid: " + schemaReason;
                    Engine.AiLog("WARN", "VERDICT", "schema rejected: " + schemaReason);
                    return envelope("model_error", null);
                }
                // v1.0.111.0: attach server-computed derived_inputs (raw values behind the derived
                // alert + which showed the LARGEST DEVIATION) for the card - structured numeric
                // data, independent of the model's text. SUPPRESSED when the verdict is
                // INCONCLUSIVE or component coverage is inadequate: a "we don't know" verdict must
                // not display an input-deviation panel that implies a settled cause.
                if (Engine.ShouldAttachDerivedInputs(verdictObj)) { verdictObj["derived_inputs"] = Engine.Sess._derivedInputs; }
                // v1.0.111.0: attach server-VERIFIED triage (from parsed same-asset/same-cause
                // alert history), suppressed on INCONCLUSIVE / inadequate history.
                Engine.AttachTriageIfEligible(verdictObj);
                // v1.0.119.0: attach the card-shaped weather (server-only key) so the WEATHER tile
                // shows the real rain/thunderstorm data instead of "pending".
                if (Engine.Sess._weatherCard != null) { verdictObj["weather"] = Engine.Sess._weatherCard; }
                if (Engine.Sess._trainContext != null) { verdictObj["train_context"] = Engine.Sess._trainContext; }   // v1.0.160.33
                // v1.0.160.98: capture the VERDICT for AI_ALERT_Analysis.AnalysisOutputJson. It must
                // be taken HERE -- on the streaming path the UI actually uses, and at the point
                // verdictObj is fully built (sanitised, triage/weather/train attached) -- so a cache
                // hit renders exactly what the first operator saw.
                // Previously assigned in AnalyzeAlertJsonCore (the JSON BATCH path, never executed by
                // the UI) and from `tv`, the TIMING envelope -- so the column was empty, and would
                // have held stopwatch numbers rather than an analysis.
                try { Engine.Sess._lastResponseFull = verdictObj.ToString(Newtonsoft.Json.Formatting.None); } catch { }
                // v1.0.108.0 / hardened 1.0.111.0: bilingual. verdictObj is FULLY SANITIZED
                // (operational-action filtered + length-trimmed). AttachHinglishAsync translates the
                // SANITIZED fields under the AI lock, with its own timeout+cancellation, and records
                // its tokens - so the Hindi INHERITS the safety and the "one call" accounting stays
                // honest. Best-effort: a translation failure leaves the English verdict untouched.
                if (AnalysisEngine._bilingualHindi)
                {
                    try { await Engine.AttachHinglishAsync(verdictObj, clientCt).ConfigureAwait(false); }
                    catch (Exception exTr) { Engine.AiLog("WARN", "HI", "hinglish translate failed (English verdict unaffected): " + AnalysisEngine.SanitizeJsonError(exTr.Message)); }
                }
                // v1.0.160.7: assemble the notification "push" block from the (now fully
                // sanitized + optionally translated) verdict. Pure string composition, no model,
                // no lock. Best-effort: a failure must never sink a good verdict.
                try { verdictObj["push"] = AnalysisEngine.BuildPushBlock(verdictObj, ctx); }
                catch (Exception exPush) { Engine.AiLog("WARN", "PUSH", "push block build failed (verdict unaffected): " + AnalysisEngine.SanitizeJsonError(exPush.Message)); }
                return envelope("ok", verdictObj);
            }
            finally
            {
                if (gateHeld) { _jsonGate.Release(); }
                if (gateHeld && !reservationSettled)
                {
                    // loop never started: nothing could have been abandoned
                    System.Threading.Interlocked.Add(ref AnalysisEngine._jsonBgReserved, -AnalysisEngine._jsonMaxChunk);
                }
            }
        }

        // POST /AiChat/AnalyzeChat
        // Follow-up chat about ONE alert, continuing after AnalyzeAlert. The browser sends
        // the alert context, the verdict JSON it received, and the follow-up thread; the
        // server rebuilds the conversation (context message first, then the thread) and
        // streams prose answers through the same agentic loop + MCP tools. Same SSE frames.
        [HttpPost]
        public async Task<ActionResult> AnalyzeChat()
        {
            // v1.0.160.107 -- pin 3: identity FIRST, before the body read.
            string body = await CaptureRoleAndReadBodyAsync().ConfigureAwait(false);   // 3b: shared prologue (role decided pre-await)

            JObject root;
            try
            {
                root = JObject.Parse(body);
            }
            catch
            {
                Response.StatusCode = 400;
                return Content("Bad JSON", "text/plain");
            }

            JObject ctx = root["context"] as JObject;
            if (ctx == null)
            {
                ctx = new JObject();
            }
            // A follow-up must stay on the provider that produced the verdict, or a
            // comparison conversation silently mixes the two.
            // v1.0.160.108 H2b: .107 aliased here but NEVER gated -- a crafted follow-up could
            // request a restricted engine. Same gate as the verdict path, on the pre-await role.
            Engine.Sess._provider = Engine.GateProviderForRole(AnalysisEngine.ResolveEngineAlias(AnalysisEngine.GetCtx(ctx, "provider")), Engine.Sess._roleSiteKeep);
            Engine.Sess._thinkReq = AnalysisEngine.ParseThinkCtx(AnalysisEngine.GetCtx(ctx, "think"));   // v1.0.160.42 UI toggle
            JToken v = root["verdict"];
            string verdictJson = (v != null && v.Type == JTokenType.Object) ? v.ToString(Formatting.None) : "";
            JArray thread = root["messages"] as JArray;
            if (thread == null)
            {
                thread = new JArray();
            }

            string _chatCallId = Guid.NewGuid().ToString("N");
            if (AnalysisEngine._aiChatLogEnabled)
            {
                try
                {
                    string _lastUser = "";
                    for (int _i = thread.Count - 1; _i >= 0; _i--)
                    {
                        JObject _m = thread[_i] as JObject;
                        if (_m != null && (string)_m["role"] == "user")
                        {
                            _lastUser = (string)_m["content"] ?? ""; break;
                        }
                    }
                    Engine.LogChatUserTurn(ctx, _lastUser, _chatCallId);
                    Engine.Sess._assistantCapture = new System.Text.StringBuilder();
                }
                catch { }
            }

            JArray messages = new JArray();
            JObject first = new JObject();
            first["role"] = "user";
            bool _grounded = AnalysisEngine.MergeGrounding(ctx);   // v1.0.142.0: inherit ids resolved by the initial analysis
            first["content"] = AnalysisEngine.BuildFollowupContextMessage(ctx, verdictJson)
                + (_grounded ? "\n(Note: SiteId/AssetId above were resolved during the initial analysis - reuse them; do not re-resolve or call tools with zero ids.)" : "");
            string _wisFu = Engine.BuildWisdomBlock(ctx);
            if (!string.IsNullOrEmpty(_wisFu)) { first["content"] = first["content"].ToString() + "\n\n" + _wisFu; }
            // v1.0.160.199: bind the point-machine operation averages into the follow-up context so the
            // WEB chat (AnalyzeChat -- the endpoint the popup actually posts to) returns the last-operation
            // voltage/current deterministically. Same helper as Chat()/.197; the block carries the values
            // AND the "don't read the 0-at-rest RDPMS tags / raw history" guidance. PT* + assetId only, "".
            string _pmFu = AnalysisEngine.BuildPmOperationValues(ctx);
            if (!string.IsNullOrEmpty(_pmFu)) { first["content"] = first["content"].ToString() + "\n\n" + _pmFu; }
            messages.Add(first);

            foreach (JToken t in thread)
            {
                JObject m = t as JObject;
                if (m == null)
                {
                    continue;
                }
                string role = m["role"] != null ? m["role"].ToString() : "";
                string content = m["content"] != null ? m["content"].ToString() : "";
                if (content.Length == 0)
                {
                    continue;
                }
                if (content.Length > 4000)
                {
                    content = content.Substring(0, 4000);
                }
                JObject nm = new JObject();
                nm["role"] = role == "assistant" ? "assistant" : "user";
                nm["content"] = content;
                messages.Add(nm);
            }

            // v1.0.160.94: store the QUESTION. Fire-and-forget inside AuditChatTurn, so a DB problem
            // cannot delay or fail a follow-up.
            int _turnBase = (thread != null ? thread.Count : 0);
            try
            {
                JToken _lastQ = (thread != null && thread.Count > 0) ? thread[thread.Count - 1] : null;
                string _qTxt = (_lastQ != null && _lastQ["content"] != null) ? _lastQ["content"].ToString() : "";
                if (!string.IsNullOrEmpty(_qTxt)) { Engine.AuditChatTurn(_turnBase, "user", _qTxt, ctx); }
            }
            catch { }

            Response.ContentType = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";
            Response.Buffer = false;
            Response.BufferOutput = false;

            try
            {
                // v1.0.160.107 -- F7. FAIL CLOSED before the model runs. The detector is given the
                // latest real USER turn only -- never the serialized alert context, so an alert
                // description mentioning configuration cannot trigger a refusal. Inside this try so
                // the finally still emits [DONE] exactly once.
                if (AnalysisEngine.IsInstructionExtractionRequest(AnalysisEngine.LastRealUserText(thread)))
                {
                    WriteSse("text", new { text = FixedExtractionRefusal });
                    return new EmptyResult();
                }

                // v1.0.172.0 -- the follow-up path was the ONE route with no scope gate. ChatBot's
                // Chat() has had IsOutOfScope since v1.2.5.0 and the initial analysis has had it in
                // AnalysisEngine.AgentLoop since 1.0.162.0, so "how are you" / "who are you" typed
                // into the Asset Review panel ran the whole agent loop and came back with a
                // paragraph of persona that recited the alert summary and the internal site/asset
                // ids -- reported from testing as "unnecessary chatbot responses are being displayed
                // in the Asset Review panel". One line back, no model call, and the alert context is
                // untouched so the next real question continues normally.
                string _scopeText = AnalysisEngine.LastRealUserText(thread);
                // v1.0.173.0: a question about the plumbing (which table / which tool / what API)
                // gets one line that names nothing -- see ScopeGuard.IsInternalsQuestion.
                if (ScopeGuard.IsInternalsQuestion(_scopeText))
                {
                    WriteSse("text", new { text = ScopeGuard.InternalsRefusal });
                    return new EmptyResult();
                }

                // v1.0.182.0 -- a social turn gets a reply that FITS WHAT WAS SAID and is not the
                // same sentence as last time. FollowupScopeRedirect was one const string, so
                // "hello" and "how are you?" came back identical, one under the other in the Asset
                // Review panel -- reported 09-10-2026 as "same answer repeat". Out-of-scope is a
                // different case and keeps the fixed redirect: there the point IS to say the same
                // thing every time.
                if (ScopeGuard.IsSocialTurn(_scopeText))
                {
                    WriteSse("text", new { text = ScopeGuard.SocialReply(_scopeText, ScopeGuard.LastAssistantText(thread)) });
                    return new EmptyResult();
                }

                if (ScopeGuard.IsOutOfScope(_scopeText))
                {
                    WriteSse("text", new { text = FollowupScopeRedirect });
                    return new EmptyResult();
                }

                // -- v1.0.77.0 -- rawJsonMode is now FALSE: this is prose (the
                // follow-up prompt requires "plain concise prose ... No JSON"), so
                // it must get the same FilterOperationalActions safety pass the SSE
                // popup applies to prose, and must never be parsed as verdict JSON
                // (validateVerdictOutput stays default false). Previously rawJsonMode
                // was true here only to preserve the out-of-scope-check skip; that
                // skip is not needed for follow-up chat and enabling the guard is a
                // safety improvement, not a regression.
                await Engine.RunAgenticLoop(messages, AnalysisEngine.AnalyzeFollowupSystemPrompt(), false).ConfigureAwait(false);
                // v1.0.160.94: store the ANSWER. _assistantCapture already collects the assembled
                // reply for the chat log (1.0.133.0) and AnalyzeChat already initialises it -- so
                // reuse it rather than adding a second field holding the same text. My first pass
                // invented "_lastChatText", which does not exist.
                try
                {
                    if (Engine.Sess._assistantCapture != null && Engine.Sess._assistantCapture.Length > 0)
                    { Engine.AuditChatTurn(_turnBase + 1, "assistant", Engine.Sess._assistantCapture.ToString(), ctx); }
                }
                catch { }
            }
            catch (Exception ex)
            {
                WriteSse("error", new { message = AnalysisEngine.FriendlyError(ex) });
            }
            finally
            {
                if (AnalysisEngine._aiChatLogEnabled && Engine.Sess._assistantCapture != null)
                {
                    try { Engine.LogChatAssistantTurn(ctx, Engine.Sess._assistantCapture.ToString(), _chatCallId); } catch { }
                    Engine.Sess._assistantCapture = null;
                }
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
            }

            return new EmptyResult();
        }

        // ---- 3b (1.0.166.0): shared web glue for the analysis entry points ----------------------------------
        // Role capture runs synchronously BEFORE the first await (LoginUser is null after ConfigureAwait(false)),
        // exactly as it did inline in each action.
        private async Task<string> CaptureRoleAndReadBodyAsync()
        {
            Engine.CaptureLoginUser();
            Engine.Sess._roleSiteKeep = Engine.AiDebugAllowed();   // v1.0.160.108 H2: one role decision, pre-await
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
            {
                body = await sr.ReadToEndAsync().ConfigureAwait(false);
            }
            return body;
        }

        private void ExposeAnalysisIdHeader()
        {
            if (Engine.Sess._auditAnalysisId > 0)
            {
                try
                {
                    Response.Headers["X-Analysis-Id"] = Engine.Sess._auditAnalysisId.ToString();
                    Response.Headers["Access-Control-Expose-Headers"] = "X-Analysis-Id";
                }
                catch { }
            }
        }
    }
}