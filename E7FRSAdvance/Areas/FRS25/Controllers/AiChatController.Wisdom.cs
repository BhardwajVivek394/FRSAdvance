using E7.AiCore;
// ============================================================================
//  AiChatController.Wisdom.cs  --  Wisdom self-learning subsystem (v1.0.125.0)
// ----------------------------------------------------------------------------
//  Partial class. All NEW wisdom code lives here so the proven controller
//  (point-machine 1.0.120-1.0.124) barely changes -- only small injection hooks
//  + the version bump in AiChatController.cs.
//
//  Feature: engineer corrections captured in the diagnostic chat are saved as
//  PENDING wisdom, reviewed, and (once APPROVED) injected as advisory domain
//  guidance into future analysis for the same cause/asset/site. Approved wisdom
//  is ALWAYS subordinate to measured evidence and the verdict/safety rules.
//
//  Safety posture:
//   - Endpoints are authorization-gated (reviewer/approver identity lists) and
//     anti-forgery protected; fail-closed (empty lists -> 403).
//   - Store is append-only, event-sourced, cross-process locked (lock file).
//   - Only the distilled one-line rule is injected, tagged [w-<id>], wrapped in
//     a precedence notice that forbids overriding evidence/coverage/schema/
//     operational-safety/INCONCLUSIVE.
//   - Distillation runs on a SEPARATE AI client + lock so it can never race or
//     block the verdict client (SendMessageAsync has no cancellation token).
//
//  Build note: add this file to the project (old-style csproj: a <Compile
//  Include="Areas\FRS25\Controllers\AiChatController.Wisdom.cs"/> entry; SDK
//  projects pick it up automatically). Uses System.Web.Helpers.AntiForgery
//  (ships with MVC5) -- confirm the reference exists.
// ============================================================================

using Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, AI clients) keep the 616 namespace
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public partial class AiChatController
    {
        private static readonly List<string> _wisdomReviewers = AnalysisEngine.ParseCsvList("WisdomReviewers");
        private static readonly List<string> _wisdomApprovers = AnalysisEngine.ParseCsvList("WisdomApprovers");
        private static readonly bool _wisdomRequireCause = AnalysisEngine.ReadBool("WisdomRequireCauseCode", true);
        private static readonly bool _wisdomRequireSite = AnalysisEngine.ReadBool("WisdomRequireSiteScope", true);
        private static readonly bool _wisdomAllowGlobal = AnalysisEngine.ReadBool("WisdomAllowGlobal", false);
        private static readonly int _wisdomMaxApproved = AnalysisEngine.ReadInt("WisdomMaxApproved", 20);

        // Anti-forgery: token sent by the page in the X-Wisdom-Token header + the
        // anti-forgery cookie. Uses System.Web.Helpers.AntiForgery (MVC5).
        // ------------------------------------------------------------------
        //  v1.0.165.1 -- NULL-SAFE ROLE CHECKS + API ROLE OVERRIDE
        //  Engine.IsWisdomReviewer()/IsWisdomApprover() read the portal LoginUser. With no
        //  portal session (Postman, expired session) LoginUser is null, which threw a
        //  NullReferenceException (500). These wrappers are what every wisdom endpoint now
        //  calls:
        //    - logged-in portal session -> exactly Engine's rule (unchanged behaviour)
        //    - no session               -> API role override if enabled, else false (403)
        //  API role override (Postman / server-to-server), OFF unless
        //  appSettings WisdomApiRoleOverride=true:
        //      X-Internal-Api-Key   : must equal appSettings InternalApiKey (required)
        //      X-Wisdom-RoleId      : RoleId as int (or query ?roleId=)
        //      X-Wisdom-SiteKeeping : true/1 for Site Keeping (or query ?isSiteKeeping=)
        //      X-Wisdom-User        : name written to createdBy/by audit fields (optional)
        //  Headers, not the JSON body: the role check runs BEFORE the body is read.
        //  SECURITY: anyone holding InternalApiKey can act as ANY role. Keep it out of
        //  source control and keep the flag false in production unless needed.
        // ------------------------------------------------------------------
        private bool _wApiResolved;
        private bool _wApiActive;
        private int _wApiRoleId = -1;
        private bool _wApiSiteKeep;
        private string _wApiUser;

        private static bool WisdomHasPortalLogin()
        {
            try { return E7FRSAdvance.Utility.ClsHttpContent.LoginUser != null; }
            catch { return false; }   // no session / no HttpContext -> treat as no login
        }

        private bool WisdomCanReview()
        {
            if (WisdomHasPortalLogin())
            {
                try { return Engine.IsWisdomReviewer(); } catch { return false; }
            }
            if (!AnalysisEngine._wisdomEnabled || !TryWisdomApiRole()) { return false; }
            return _wApiRoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode()
                || _wApiRoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode();
        }

        private bool WisdomCanApprove()
        {
            if (WisdomHasPortalLogin())
            {
                try { return Engine.IsWisdomApprover(); } catch { return false; }
            }
            if (!AnalysisEngine._wisdomEnabled || !TryWisdomApiRole()) { return false; }
            return _wApiRoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode()
                || (_wApiRoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode() && _wApiSiteKeep);
        }

        // Identity for audit fields: the API caller when the override authorized this request,
        // otherwise the normal resolver.
        private string WisdomWho()
        {
            if (_wApiActive && !string.IsNullOrEmpty(_wApiUser)) { return _wApiUser; }
            try { return ResolveWisdomIdentity(); } catch { return null; }
        }

        private static bool WisdomKeyEquals(string a, string b)
        {
            if (a == null || b == null) { return false; }
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            int diff = ba.Length ^ bb.Length;
            int n = Math.Max(ba.Length, bb.Length);
            for (int i = 0; i < n; i++)
            {
                byte x = i < ba.Length ? ba[i] : (byte)0;
                byte y = i < bb.Length ? bb[i] : (byte)0;
                diff |= x ^ y;
            }
            return diff == 0;
        }

        private bool TryWisdomApiRole()
        {
            if (_wApiResolved) { return _wApiActive; }
            _wApiResolved = true;
            try
            {
                string flag = ConfigurationManager.AppSettings["WisdomApiRoleOverride"];
                if (!string.Equals((flag ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase)) { return false; }

                string cfgKey = ConfigurationManager.AppSettings["InternalApiKey"];
                if (string.IsNullOrWhiteSpace(cfgKey)) { return false; }
                string gotKey = Request.Headers["X-Internal-Api-Key"];
                if (string.IsNullOrEmpty(gotKey) || !WisdomKeyEquals(cfgKey.Trim(), gotKey.Trim()))
                {
                    Engine.AiLog("WARN", "WISDOM", "api role override: bad or missing X-Internal-Api-Key");
                    return false;
                }

                string rs = Request.Headers["X-Wisdom-RoleId"];
                if (string.IsNullOrWhiteSpace(rs)) { rs = Request.QueryString["roleId"]; }
                int rid;
                if (!int.TryParse((rs ?? "").Trim(), out rid) || rid < 0) { return false; }

                string sk = Request.Headers["X-Wisdom-SiteKeeping"];
                if (string.IsNullOrWhiteSpace(sk)) { sk = Request.QueryString["isSiteKeeping"]; }
                sk = (sk ?? "").Trim();

                string user = (Request.Headers["X-Wisdom-User"] ?? "").Trim();
                StringBuilder ub = new StringBuilder();
                foreach (char ch in user)
                {
                    if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '.' || ch == '_' || ch == '-' || ch == '@') { ub.Append(ch); }
                    if (ub.Length >= 80) { break; }
                }

                _wApiRoleId = rid;
                _wApiSiteKeep = sk == "1" || string.Equals(sk, "true", StringComparison.OrdinalIgnoreCase);
                _wApiUser = "api:" + (ub.Length > 0 ? ub.ToString() : "unnamed");
                _wApiActive = true;
                Engine.AiLog("WARN", "WISDOM", "api role override USED user=" + _wApiUser + " roleId=" + rid
                    + " siteKeeping=" + _wApiSiteKeep + " path=" + (Request.Path ?? ""));
                return true;
            }
            catch { _wApiActive = false; return false; }
        }

        private bool WisdomAntiForgeryOk()
        {
            // v1.0.165.1: a request authorized by the API override carries no browser cookies,
            // and CSRF does not apply to a request authenticated by a secret header.
            if (_wApiActive) { return true; }
            try
            {
                string cookieToken = null;
                System.Web.HttpCookie c = Request.Cookies["__RequestVerificationToken"];
                if (c != null)
                {
                    cookieToken = c.Value;
                }
                string formToken = Request.Headers["X-Wisdom-Token"];
                System.Web.Helpers.AntiForgery.Validate(cookieToken, formToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // v1.0.139.0: 4xx rejection carrying the REAL reason as JSON. These were
        // plain-text bodies; the IIS custom error module replaces the body of codes it
        // does not recognize (422 -> "The custom error module does not recognize this
        // error"), which hid every validation reason from the panel.
        private ActionResult WisdomReject(int code, string msg)
        {
            Response.TrySkipIisCustomErrors = true;
            Response.StatusCode = code;
            return Json(new { ok = false, error = msg }, JsonRequestBehavior.AllowGet);
        }

        private ActionResult WisdomDenied(string action)
        {
            string who = WisdomWho();
            Engine.AiLog("WARN", "WISDOM", "denied identity=" + (who ?? "<none>") + " action=" + action);
            Response.TrySkipIisCustomErrors = true;
            Response.StatusCode = 403;
            // -- v1.0.143.0 -- REPLACED: access is now RoleId/IsSiteKeeping, not a
            // WisdomReviewers/WisdomApprovers appSettings list -- the old text pointed
            // engineers at a config file that no longer governs access.
            // string reason = who == null
            //     ? "no identity: portal login not visible to wisdom (set WisdomIdentitySessionKey to your login session variable)"
            //     : "'" + who + "' is not in WisdomReviewers/WisdomApprovers (add this exact name to appSettings)";
            string reason = who == null
                ? "no identity resolved (portal login not visible to wisdom logging - access is still evaluated by RoleId/IsSiteKeeping)"
                : "'" + who + "' requires Admin role, or User role with Site Keeping, for wisdom access";
            return Json(new { ok = false, error = "not authorized for " + action + " - " + reason },
                JsonRequestBehavior.AllowGet);
        }

        // Catch-all for wisdom endpoints: the REAL exception goes to ai-log
        // (WISDOM tag), the client gets clean JSON instead of the ASP.NET
        // "Runtime Error" HTML page (which customErrors also masks).
        private ActionResult WisdomFail(string action, Exception ex)
        {
            try
            {
                Engine.AiLog("ERROR", "WISDOM", action + " threw " + ex.GetType().Name + ": "
                    + AnalysisEngine.SanitizeJsonError(AnalysisEngine.ClampLen(ex.ToString(), 1800)));
            }
            catch { }
            Response.TrySkipIisCustomErrors = true;
            Response.StatusCode = 500;
            return Json(new
            {
                ok = false,
                error = action + " failed: " + ex.GetType().Name + " - "
                + AnalysisEngine.SanitizeJsonError(AnalysisEngine.ClampLen(ex.Message, 300)) + " (details in ai-log)"
            },
                JsonRequestBehavior.AllowGet);
        }


        // ==================================================================
        //  ENDPOINTS
        // ==================================================================

        // POST /AiChat/SaveWisdom
        // body: { context:{alertId,causeCode,siteId,station,provider}, verdictGiven, userText, aiText }
        [HttpPost]
        public async Task<ActionResult> SaveWisdom()
        {
            try
            {
                if (!WisdomCanReview()) { return WisdomDenied("SaveWisdom"); }
                if (!WisdomAntiForgeryOk()) { return WisdomReject(400, "bad token"); }

                JObject root = await ReadJsonBodyAsync().ConfigureAwait(false);
                if (root == null) { return WisdomReject(400, "bad json"); }

                JObject context = root["context"] as JObject ?? new JObject();
                string causeCode = AnalysisEngine.GetCtx(context, "causeCode").Trim().ToUpperInvariant();
                string assetType = AnalysisEngine.AssetTypeFor(causeCode) ?? "";
                string siteId = AnalysisEngine.GetCtx(context, "siteId");
                string siteCode = AnalysisEngine.GetCtx(context, "station");           // real payload carries "station"
                string provider = AnalysisEngine.GetCtx(context, "provider");
                string alertId = AnalysisEngine.GetCtx(context, "alertId");

                string userText = AnalysisEngine.ClampLen(root.Value<string>("userText") ?? "", 4000);
                string aiText = AnalysisEngine.ClampLen(root.Value<string>("aiText") ?? "", 1000);
                string verdictGiven = AnalysisEngine.ClampLen(root.Value<string>("verdictGiven") ?? "", 400);
                if (userText.Trim().Length == 0)
                {
                    return WisdomReject(400, "empty correction");
                }

                // Distill (best-effort). Raw text is stored either way, so a failed
                // distillation is recoverable via the edit step.
                string distilled = "";
                try
                {
                    // v1.0.160.48: distil on the SAME engine that produced the verdict being corrected.
                    // `provider` is already parsed from the context above; it was previously unused for the
                    // model call because distillation was hardcoded to Anthropic. THINK is forced OFF: a
                    // distilled rule is one line and does not justify 40-90s of hidden reasoning. A blank
                    // provider falls back to the AiProvider config default (same as before).
                    Engine.Sess._provider = provider;
                    Engine.Sess._thinkReq = false;
                    distilled = await Engine.WisdomDistillAsync(userText, causeCode, assetType).ConfigureAwait(false);
                }
                catch (Exception dex)
                {
                    Engine.AiLog("WARN", "WISDOM", "distill failed (raw kept): " + AnalysisEngine.SanitizeJsonError(dex.Message));
                }
                // Validate the distilled rule; if it fails, store empty (not approvable
                // until a valid rule is supplied via UpdateWisdomRule).
                string vreason = "";
                if (distilled.Length > 0 && !AnalysisEngine.ValidateWisdomRule(distilled, out vreason))
                {
                    Engine.AiLog("INFO", "WISDOM", "distilled rule rejected (" + vreason + "); stored empty, needs edit");
                    distilled = "";
                }

                JObject entry = new JObject();
                entry["kind"] = "entry";
                entry["id"] = "w-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                entry["ts"] = AnalysisEngine.NowIst();
                entry["createdBy"] = WisdomWho() ?? "";
                entry["alertId"] = alertId;
                JObject scopeObj = new JObject();
                scopeObj["causeCode"] = causeCode;
                scopeObj["assetType"] = assetType;
                scopeObj["siteId"] = siteId;
                scopeObj["siteCode"] = siteCode;
                entry["scope"] = scopeObj;
                entry["provider"] = provider;
                entry["verdictGiven"] = verdictGiven;
                entry["userText"] = AnalysisEngine.RedactSecretText(userText);
                entry["aiText"] = AnalysisEngine.RedactSecretText(aiText);
                entry["distilledRule"] = distilled;
                entry["redacted"] = true;

                string id = entry.Value<string>("id");
                bool ok = Engine.WisdomAppendEvent(entry);
                if (!ok) { Response.StatusCode = 500; return Content("store append failed", "text/plain"); }
                AnalysisEngine.InvalidateWisdomCache();
                Engine.AiLog("INFO", "WISDOM", "saved id=" + id + " scope=" + causeCode + "|" + assetType + "|" + siteId);

                return Json(new { ok = true, id = id, distilledRule = distilled });
            }
            catch (Exception wex)
            {
                return WisdomFail("SaveWisdom", wex);
            }
        }

        // GET /AiChat/ListWisdom?status=PENDING
        [HttpGet]
        public ActionResult ListWisdom(string status = "", string causeCode = "", string siteId = "")
        {
            try
            {
                if (!WisdomCanReview()) { return WisdomDenied("ListWisdom"); }
                List<AnalysisEngine.WisdomRule> all = Engine.LoadWisdomRules(true);
                // v1.0.139.0: optional scope filter so the popup shows only the current
                // alert's wisdom (cause + site; site-blank rules still match the site).
                if (!string.IsNullOrWhiteSpace(causeCode))
                {
                    string ccf = causeCode.Trim();
                    all = all.Where(r => string.Equals(r.CauseCode ?? "", ccf, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                if (!string.IsNullOrWhiteSpace(siteId))
                {
                    string sidf = siteId.Trim();
                    all = all.Where(r => string.IsNullOrEmpty(r.SiteId)
                        || string.Equals(r.SiteId, sidf, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                IEnumerable<AnalysisEngine.WisdomRule> q = all;
                if (!string.IsNullOrWhiteSpace(status))
                {
                    string s = status.Trim().ToUpperInvariant();
                    q = q.Where(r => string.Equals(r.Status, s, StringComparison.OrdinalIgnoreCase));
                }
                var rows = q.OrderByDescending(r => r.Seq).Take(200).Select(r => new
                {
                    id = r.Id,
                    status = r.Status,
                    scope = new { causeCode = r.CauseCode, assetType = r.AssetType, siteId = r.SiteId, siteCode = r.SiteCode },
                    distilledRule = r.Rule,
                    userText = r.UserText,
                    aiText = r.AiText,
                    verdictGiven = r.VerdictGiven,
                    createdBy = r.CreatedBy,
                    approvedBy = r.ApprovedBy,
                    ts = r.Ts
                }).ToList();
                return Json(new { ok = true, count = rows.Count, items = rows }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception wex)
            {
                return WisdomFail("ListWisdom", wex);
            }
        }

        // GET /AiChat/WisdomWhoAmI
        // Returns the CALLER's OWN identity + auth status so the panel can show exactly
        // what to add to the reviewer/approver lists -- folding the old auth probe into the
        // UI. Gated by EnableWisdom only (NOT by the lists), so an as-yet-unauthorized user
        // can still learn their own login name. Showing a user their own User.Identity.Name
        // is not a disclosure. This supersedes the temporary WisdomAuthProbe action.
        // v1.0.149.0: one-click diagnostic bundle -- zips the day's AI logs (ai-log /
        // ai-analyze / ai-tool / ai-chat) so they can be downloaded and shared for
        // diagnosis instead of collecting files from the server by hand.
        // Requires framework refs System.IO.Compression + System.IO.Compression.FileSystem.
        // v1.0.160.59: RUN LOG feed for the viewer tab. Reads the audit_7w lines that 1.0.160.57
        // already writes -- no new storage. Returns METADATA about runs only; raw tool results and
        // file content are deliberately excluded so the viewer cannot become a back door to the
        // platform logs the owner ruled out. Payloads stay behind ToolTrace, which 160.58 gated.
        [HttpGet]
        public ActionResult RunLogFeed(string date = "")
        {
            try
            {
                if (!Engine.AiDebugAllowed() && !WisdomCanReview()) { return WisdomDenied("RunLogFeed"); }
                string d = string.IsNullOrWhiteSpace(date) ? DateTime.UtcNow.ToString("yyyyMMdd") : date.Trim();
                string path = System.IO.Path.Combine(AnalysisEngine._toolLogDir, "ai-analyze-" + d + ".jsonl");
                JArray rows = new JArray();
                if (System.IO.File.Exists(path))
                {
                    string[] lines = System.IO.File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].IndexOf("\"audit_7w\"", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                        try
                        {
                            JObject o = JObject.Parse(lines[i]);
                            if (!string.Equals((string)o["step"], "audit_7w", StringComparison.OrdinalIgnoreCase)) { continue; }
                            JObject a = o["data"] as JObject;
                            if (a == null) { continue; }
                            JObject r = new JObject();
                            r["tsUtc"] = o["tsUtc"]; r["traceId"] = o["traceId"];
                            r["who"] = a["who"]; r["why"] = a["why"]; r["which"] = a["which"];
                            r["where"] = a["where"]; r["when"] = a["when"];
                            r["cost"] = a["cost"]; r["wisdom"] = a["wisdom"]; r["what"] = a["what"];
                            // v1.0.160.159: this feed leaves the server for a browser, so the
                            // .156 rule applies: calling name kept, provider/model/providerUrl
                            // removed, serialised scrub as backstop. The on-disk JSONL keeps the
                            // real values -- ops truth stays on the server.
                            rows.Add(AnalysisEngine.RedactAuditForClient(r, false));
                        }
                        catch { }
                    }
                }
                return Content(new JObject { { "ok", true }, { "date", d }, { "count", rows.Count }, { "rows", rows } }
                    .ToString(Formatting.None), "application/json");
            }
            catch (Exception ex) { return WisdomFail("RunLogFeed", ex); }
        }

        // v1.0.160.61: [HttpGet] restored. The 160.59 RunLogFeed insertion landed BETWEEN this
        // attribute and its method, so RunLogFeed carried two (CS0579) and DownloadAiLogs none.
        [HttpGet]
        public ActionResult DownloadAiLogs(string date = "")
        {
            try
            {
                if (!AnalysisEngine._wisdomEnabled) { Response.StatusCode = 404; return Content("not found", "text/plain"); }
                // v1.0.160.58: align with the owner rule -- site-keeping users and the explicit
                // config emails get all debug/tools. Reviewers keep access too, so an approver who
                // is not in the site-keeping list is not locked out of the logs they review.
                if (!Engine.AiDebugAllowed() && !WisdomCanReview()) { return WisdomDenied("DownloadAiLogs"); }
                string d = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyyMMdd") : date.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(d, "^\\d{8}$")) { return WisdomReject(400, "date must be yyyyMMdd"); }
                string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "AiToolLogs");
                string[] names = { "ai-log-" + d + ".txt", "ai-analyze-" + d + ".jsonl", "ai-tool-" + d + ".jsonl", "ai-chat-" + d + ".txt" };
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    using (System.IO.Compression.ZipArchive za = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                    {
                        int added = 0;
                        for (int i = 0; i < names.Length; i++)
                        {
                            string fp = System.IO.Path.Combine(dir, names[i]);
                            if (!System.IO.File.Exists(fp)) { continue; }
                            System.IO.Compression.ZipArchiveEntry e = za.CreateEntry(names[i], System.IO.Compression.CompressionLevel.Optimal);
                            // v1.0.160.159: the zip leaves the server for a browser, so the .156
                            // rule applies to it too -- vendor identity scrubbed, calling names
                            // (quantum11/nexus*/orion*) kept, so downloaded logs stay debuggable.
                            // The on-disk files are untouched.
                            string body;
                            using (System.IO.FileStream fs = new System.IO.FileStream(fp, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                            using (System.IO.StreamReader sr = new System.IO.StreamReader(fs))
                            { body = sr.ReadToEnd(); }
                            byte[] clean = System.Text.Encoding.UTF8.GetBytes(AnalysisEngine.ScrubEngineIdentity(body));
                            using (System.IO.Stream es = e.Open())
                            { es.Write(clean, 0, clean.Length); }
                            added++;
                        }
                        if (added == 0) { return WisdomReject(404, "no AI logs found for " + d); }
                    }
                    return File(ms.ToArray(), "application/zip", "AiLogs_" + d + "_" + DateTime.Now.ToString("HHmmss") + ".zip");
                }
            }
            catch (Exception ex) { return WisdomFail("DownloadAiLogs", ex); }
        }

        [HttpGet]
        public ActionResult WisdomWhoAmI()
        {
            try
            {
                if (!AnalysisEngine._wisdomEnabled) { Response.StatusCode = 404; return Content("disabled", "text/plain"); }
                string who = WisdomWho() ?? "";
                // -- v1.0.143.0 -- REPLACED: mirror IsWisdomReviewer()/IsWisdomApprover() so the
                // panel's flags can never disagree with what SetWisdomStatus/BulkScopeWisdom/
                // ListWisdom will actually allow.
                // bool isRev = who.Length > 0 && (_wisdomReviewers.Contains(who, StringComparer.OrdinalIgnoreCase)
                //     || _wisdomApprovers.Contains(who, StringComparer.OrdinalIgnoreCase));
                // bool isApp = who.Length > 0 && _wisdomApprovers.Contains(who, StringComparer.OrdinalIgnoreCase);
                bool isRev = WisdomCanReview();
                bool isApp = WisdomCanApprove();
                bool listsEmpty = _wisdomReviewers.Count == 0 && _wisdomApprovers.Count == 0;
                // -- v1.0.143.0 -- ADDED: displayName (FirstName + LastName from the portal
                // login) so the panel shows a real name instead of the shared
                // WisdomDefaultIdentity fallback ("ENERGY7"). identity/who unchanged -- still
                // what WisdomReviewers/WisdomApprovers/createdBy/approvedBy use.
                // v1.0.165.1: null-safe -- no portal login (Postman, expired session) -> empty name, not a 500.
                string displayName = "";
                try
                {
                    var __lu = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
                    if (__lu != null) { displayName = ((__lu.FirstName ?? "") + " " + (__lu.LastName ?? "")).Trim(); }
                }
                catch { }
                return Json(new
                {
                    ok = true,
                    enabled = AnalysisEngine._wisdomEnabled,
                    identity = who,
                    displayName = displayName,
                    authenticated = who.Length > 0,
                    isReviewer = isRev,
                    isApprover = isApp,
                    listsEmpty = listsEmpty
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception wex)
            {
                return WisdomFail("WisdomWhoAmI", wex);
            }
        }

        // POST /AiChat/UpdateWisdomRule   body: { id, editedRule, editedScope?:{causeCode,assetType,siteId,siteCode} }
        [HttpPost]
        public async Task<ActionResult> UpdateWisdomRule()
        {
            try
            {
                if (!WisdomCanReview()) { return WisdomDenied("UpdateWisdomRule"); }
                if (!WisdomAntiForgeryOk()) { return WisdomReject(400, "bad token"); }
                JObject root = await ReadJsonBodyAsync().ConfigureAwait(false);
                if (root == null) { return WisdomReject(400, "bad json"); }

                string id = root.Value<string>("id") ?? "";
                string editedRule = AnalysisEngine.ClampLen(root.Value<string>("editedRule") ?? "", 400).Trim();
                if (id.Length == 0 || editedRule.Length == 0)
                {
                    return WisdomReject(400, "id and editedRule required");
                }
                string vreason = "";
                if (!AnalysisEngine.ValidateWisdomRule(editedRule, out vreason))
                {
                    return WisdomReject(422, "rule rejected: " + vreason);
                }

                // Only a PENDING entry accepts edits.
                AnalysisEngine.WisdomRule cur = Engine.LoadWisdomRules(true).FirstOrDefault(r => r.Id == id);
                if (cur == null) { Response.StatusCode = 404; return Content("no such id", "text/plain"); }
                if (!string.Equals(cur.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    return WisdomReject(409, "only PENDING entries are editable");
                }

                JObject ev = new JObject();
                ev["kind"] = "edit";
                ev["id"] = id;
                ev["ts"] = AnalysisEngine.NowIst();
                ev["by"] = WisdomWho() ?? "";
                ev["editedRule"] = editedRule;
                JObject sc = root["editedScope"] as JObject;
                if (sc != null)
                {
                    JObject esObj = new JObject();
                    esObj["causeCode"] = (sc.Value<string>("causeCode") ?? "").Trim().ToUpperInvariant();
                    esObj["assetType"] = (sc.Value<string>("assetType") ?? "").Trim().ToUpperInvariant();
                    esObj["siteId"] = sc.Value<string>("siteId") ?? "";
                    esObj["siteCode"] = sc.Value<string>("siteCode") ?? "";
                    ev["editedScope"] = esObj;
                }
                if (!Engine.WisdomAppendEvent(ev)) { Response.StatusCode = 500; return Content("append failed", "text/plain"); }
                AnalysisEngine.InvalidateWisdomCache();
                Engine.AiLog("INFO", "WISDOM", "edit id=" + id);
                return Json(new { ok = true, id = id, editedRule = editedRule });
            }
            catch (Exception wex)
            {
                return WisdomFail("UpdateWisdomRule", wex);
            }
        }

        // POST /AiChat/SetWisdomStatus   body: { id, status, editedRule? }
        //   status in APPROVED | APPROVED_GLOBAL | REJECTED
        [HttpPost]
        public async Task<ActionResult> SetWisdomStatus()
        {
            try
            {
                if (!WisdomCanApprove()) { return WisdomDenied("SetWisdomStatus"); }
                if (!WisdomAntiForgeryOk()) { return WisdomReject(400, "bad token"); }
                JObject root = await ReadJsonBodyAsync().ConfigureAwait(false);
                if (root == null) { return WisdomReject(400, "bad json"); }

                string id = root.Value<string>("id") ?? "";
                string status = (root.Value<string>("status") ?? "").Trim().ToUpperInvariant();
                if (id.Length == 0 || (status != "APPROVED" && status != "APPROVED_GLOBAL" && status != "REJECTED"))
                {
                    return WisdomReject(400, "id + valid status required");
                }

                // The whole approval transaction runs under the cross-process file lock,
                // reloading effective state FROM DISK (not the cache) so two workers
                // cannot both approve for the same scope.
                using (FileStream fl = Engine.OpenWisdomLock())
                {
                    List<AnalysisEngine.WisdomRule> rules = Engine.ReadWisdomRulesFromDisk();
                    AnalysisEngine.WisdomRule cur = rules.FirstOrDefault(r => r.Id == id);
                    if (cur == null) { Response.StatusCode = 404; return Content("no such id", "text/plain"); }

                    if (status == "REJECTED")
                    {
                        long seqR = Engine.NextSeq(rules);
                        JObject rej = new JObject();
                        rej["kind"] = "reject"; rej["id"] = id; rej["ts"] = AnalysisEngine.NowIst(); rej["by"] = WisdomWho() ?? ""; rej["seq"] = seqR;
                        Engine.AppendLineLocked(rej);
                        AnalysisEngine.InvalidateWisdomCache();
                        Engine.AiLog("INFO", "WISDOM", "id=" + id + " -> REJECTED");
                        return Json(new { ok = true, id = id, status = "REJECTED" });
                    }

                    bool wantGlobal = (status == "APPROVED_GLOBAL");
                    bool isGlobalScope = string.IsNullOrEmpty(cur.CauseCode) && string.IsNullOrEmpty(cur.AssetType) && string.IsNullOrEmpty(cur.SiteId);

                    // Canary enforcement.
                    if (_wisdomRequireCause && string.IsNullOrEmpty(cur.CauseCode) && !isGlobalScope)
                    {
                        return WisdomReject(422, "causeCode required for approval");
                    }
                    if (_wisdomRequireSite && !isGlobalScope)
                    {
                        if (string.IsNullOrEmpty(cur.SiteId))
                        {
                            return WisdomReject(422, "siteId required for approval");
                        }
                        if (AnalysisEngine._wisdomCanarySites.Count > 0 && !AnalysisEngine._wisdomCanarySites.Contains(cur.SiteId, StringComparer.OrdinalIgnoreCase))
                        {
                            return WisdomReject(422, "siteId not in canary allowlist");
                        }
                    }
                    if (isGlobalScope || wantGlobal)
                    {
                        if (!_wisdomAllowGlobal)
                        {
                            return WisdomReject(409, "global wisdom disabled");
                        }
                    }

                    // Re-validate the effective rule INSIDE the transaction (guards
                    // against approving a rule that a later edit invalidated).
                    string effRule = cur.Rule ?? "";
                    string editedRule = root.Value<string>("editedRule");
                    if (!string.IsNullOrWhiteSpace(editedRule)) { effRule = editedRule.Trim(); }
                    string vreason = "";
                    if (effRule.Length == 0 || !AnalysisEngine.ValidateWisdomRule(effRule, out vreason))
                    {
                        return WisdomReject(422, "rule not approvable: " + (effRule.Length == 0 ? "empty" : vreason));
                    }

                    // Max approved cap.
                    int activeCount = rules.Count(r => r.IsActive);
                    if (activeCount >= _wisdomMaxApproved)
                    {
                        return WisdomReject(409, "max approved rules reached (" + _wisdomMaxApproved + ")");
                    }

                    // Global two-approver rule: APPROVED_GLOBAL must be a DIFFERENT
                    // approver than the one who set APPROVED.
                    if (wantGlobal)
                    {
                        if (!string.Equals(cur.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
                        {
                            return WisdomReject(409, "global needs APPROVED first");
                        }
                        if (string.Equals(cur.ApprovedBy, WisdomWho(), StringComparison.OrdinalIgnoreCase))
                        {
                            return WisdomReject(409, "global needs a second, different approver");
                        }
                    }

                    // One active rule per exact scopeKey: atomically supersede the
                    // existing active same-scope rule in the SAME approval event.
                    string scopeKey = cur.CauseCode + "|" + cur.AssetType + "|" + cur.SiteId;
                    string supersedesId = null;
                    if (!wantGlobal)
                    {
                        AnalysisEngine.WisdomRule clash = rules.FirstOrDefault(r => r.IsActive && r.Id != id &&
                            (r.CauseCode + "|" + r.AssetType + "|" + r.SiteId) == scopeKey);
                        if (clash != null) { supersedesId = clash.Id; }
                    }

                    long seq = Engine.NextSeq(rules);
                    JObject appr = new JObject();
                    appr["kind"] = "approval";
                    appr["id"] = id;
                    appr["ts"] = AnalysisEngine.NowIst();
                    appr["by"] = WisdomWho() ?? "";
                    appr["status"] = wantGlobal ? "APPROVED_GLOBAL" : "APPROVED";
                    appr["seq"] = seq;
                    if (!string.IsNullOrEmpty(supersedesId)) { appr["supersedesWisdomId"] = supersedesId; }
                    if (!string.IsNullOrWhiteSpace(editedRule)) { appr["approvedRule"] = effRule; }
                    Engine.AppendLineLocked(appr);
                    AnalysisEngine.InvalidateWisdomCache();
                    Engine.AiLog("INFO", "WISDOM", "id=" + id + " -> " + (wantGlobal ? "APPROVED_GLOBAL" : "APPROVED")
                        + (supersedesId != null ? " supersedes=" + supersedesId : ""));
                    return Json(new { ok = true, id = id, status = wantGlobal ? "APPROVED_GLOBAL" : "APPROVED", supersedes = supersedesId });
                }
            }
            catch (Exception wex)
            {
                return WisdomFail("SetWisdomStatus", wex);
            }
        }

        // GET /AiChat/WisdomManage -- full-page management UI (cause-code grouped).
        // Reviewer-gated like ListWisdom; renders a self-contained HTML page that
        // calls the existing JSON endpoints. Kept as a string so it needs no view file.
        [HttpGet]
        public ActionResult WisdomManage()
        {
            try
            {
                if (!AnalysisEngine._wisdomEnabled) { Response.StatusCode = 404; return Content("not found", "text/plain"); }
                if (!WisdomCanReview()) { return WisdomDenied("WisdomManage"); }
                string html = WisdomManageHtml();
                return Content(html, "text/html");
            }
            catch (Exception wex)
            {
                return WisdomFail("WisdomManage", wex);
            }
        }

        // POST /AiChat/BulkScopeWisdom  body: { causeCode, newSiteId?, newSiteCode? }
        // Approver-gated. Applies a scope edit to EVERY PENDING rule under a cause code
        // (bulk cause-code-wide change). Each becomes its own edit event; approved rules
        // are NOT touched (they must be superseded via the normal approve flow).
        [HttpPost]
        public async Task<ActionResult> BulkScopeWisdom()
        {
            try
            {
                if (!WisdomCanApprove()) { return WisdomDenied("BulkScopeWisdom"); }
                if (!WisdomAntiForgeryOk()) { return WisdomReject(400, "bad token"); }
                JObject root = await ReadJsonBodyAsync().ConfigureAwait(false);
                if (root == null) { return WisdomReject(400, "bad json"); }
                string causeCode = (root.Value<string>("causeCode") ?? "").Trim().ToUpperInvariant();
                if (causeCode.Length == 0) { return WisdomReject(400, "causeCode required"); }
                string newSiteId = root.Value<string>("newSiteId");
                string newSiteCode = root.Value<string>("newSiteCode");

                List<AnalysisEngine.WisdomRule> rules = Engine.LoadWisdomRules(true)
                    .Where(r => string.Equals(r.CauseCode, causeCode, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(r.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                int changed = 0;
                foreach (AnalysisEngine.WisdomRule r in rules)
                {
                    JObject ev = new JObject();
                    ev["kind"] = "edit";
                    ev["id"] = r.Id;
                    ev["ts"] = AnalysisEngine.NowIst();
                    ev["by"] = WisdomWho() ?? "";
                    ev["editedRule"] = r.Rule;   // rule text unchanged in a scope-only bulk edit
                    JObject esObj = new JObject();
                    esObj["causeCode"] = r.CauseCode;
                    esObj["assetType"] = r.AssetType;
                    esObj["siteId"] = newSiteId != null ? newSiteId : r.SiteId;
                    esObj["siteCode"] = newSiteCode != null ? newSiteCode : r.SiteCode;
                    ev["editedScope"] = esObj;
                    if (Engine.WisdomAppendEvent(ev)) { changed++; }
                }
                AnalysisEngine.InvalidateWisdomCache();
                Engine.AiLog("INFO", "WISDOM", "bulk scope cause=" + causeCode + " changed=" + changed);
                return Json(new { ok = true, causeCode = causeCode, changed = changed });
            }
            catch (Exception wex)
            {
                return WisdomFail("BulkScopeWisdom", wex);
            }
        }


        // Self-contained management page markup (no Razor view needed). The token is
        // rendered server-side via AntiForgeryToken helper so POSTs carry X-Wisdom-Token.
        private string WisdomManageHtml()
        {
            string tokenField;
            try { tokenField = System.Web.Helpers.AntiForgery.GetHtml().ToString(); }
            catch { tokenField = ""; }
            string body = WISDOM_MANAGE_TEMPLATE.Replace("{{TOKEN}}", tokenField);
            return body;
        }

        private const string WISDOM_MANAGE_TEMPLATE = @"<!doctype html>
<html><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>ENERGY7 Wisdom Management</title>
<style>
 body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#0f1115;color:#e8eaed}
 header{background:#151922;padding:14px 20px;border-bottom:1px solid #232a36;display:flex;align-items:center;gap:14px;position:sticky;top:0;z-index:5}
 header h1{font-size:16px;margin:0;font-weight:600}
 .who{font-size:12px;color:#9aa4b2;margin-left:auto}
 .wrap{max-width:1100px;margin:0 auto;padding:18px 20px 60px}
 .filters{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:16px}
 .filters button{background:#1b2130;color:#cbd3df;border:1px solid #2a3342;border-radius:20px;padding:6px 14px;cursor:pointer;font-size:13px}
 .filters button.on{background:#2563eb;border-color:#2563eb;color:#fff}
 .nav2{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 12px}
 .nav2 button{background:#161b26;color:#aeb8c6;border:1px solid #2a3342;border-radius:16px;padding:5px 12px;cursor:pointer;font-size:12px}
 .nav2 button.on{background:#0e7490;border-color:#0e7490;color:#fff}
 .atype{font-size:15px;font-weight:700;margin:18px 0 8px;padding-bottom:4px;border-bottom:2px solid #2a3342;letter-spacing:.4px}
 .cause{border:1px solid #232a36;border-radius:12px;margin-bottom:14px;overflow:hidden;background:#141922}
 .cause>summary{cursor:pointer;padding:12px 16px;font-weight:600;font-size:14px;background:#171d28;list-style:none;display:flex;align-items:center;gap:10px}
 .cause>summary::-webkit-details-marker{display:none}
 .badge{font-size:11px;padding:2px 8px;border-radius:10px;font-weight:600}
 .b-p{background:#3a2f00;color:#ffd666}.b-a{background:#06371f;color:#5eead4}.b-r{background:#3a0d0d;color:#fca5a5}.b-s{background:#22262e;color:#8b95a3}
 .cnt{margin-left:auto;font-size:12px;color:#8b95a3;font-weight:400}
 .rule{border-top:1px solid #232a36;padding:12px 16px}
 .rule .scope{font-size:12px;color:#93c5fd;margin-bottom:6px}
 .rule textarea{width:100%;box-sizing:border-box;background:#0d1017;color:#e8eaed;border:1px solid #2a3342;border-radius:8px;padding:8px;font:inherit;font-size:13px;min-height:40px}
 .rule .corr{font-size:12px;color:#8b95a3;margin:6px 0;white-space:pre-wrap;border-left:2px solid #2a3342;padding-left:8px}
 .rule .meta{font-size:11px;color:#6b7280;margin-top:4px}
 .acts{margin-top:8px;display:flex;gap:8px;flex-wrap:wrap}
 .acts button{border:0;border-radius:7px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:600}
 .ap{background:#059669;color:#fff}.rj{background:#dc2626;color:#fff}.ed{background:#334155;color:#e8eaed}.sc{background:#1d4ed8;color:#fff}
 .bulk{background:#161b24;border:1px dashed #2a3342;border-radius:8px;padding:8px 12px;margin:8px 16px;font-size:12px;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
 .bulk input{background:#0d1017;color:#e8eaed;border:1px solid #2a3342;border-radius:6px;padding:5px 8px;font:inherit;font-size:12px;width:90px}
 .empty{color:#6b7280;padding:30px;text-align:center}
 .toast{position:fixed;bottom:18px;left:50%;transform:translateX(-50%);background:#1f2937;border:1px solid #374151;color:#e8eaed;padding:10px 16px;border-radius:8px;font-size:13px;opacity:0;transition:.2s;z-index:10}
 .toast.show{opacity:1}
 a.back{color:#93c5fd;text-decoration:none;font-size:13px}
</style></head>
<body>
<header>
 <h1>ENERGY7 Wisdom Management</h1>
 <span id=""who"" class=""who"">...</span>
</header>
<div class=""wrap"">
 <div style=""display:none"">{{TOKEN}}</div>
 <div class=""filters"">
   <button data-f=""ALL"" class=""on"">All</button>
   <button data-f=""PENDING"">Pending</button>
   <button data-f=""APPROVED"">Approved</button>
   <button data-f=""REJECTED"">Rejected</button>
   <button data-f=""SUPERSEDED"">Superseded</button>
   <button data-f=""CANDIDATES"">Candidates</button>
   <button id=""refresh"" style=""margin-left:auto"">Refresh</button>
 </div>
 <div id=""list""><div class=""empty"">Loading...</div></div>
</div>
<div id=""toast"" class=""toast""></div>
<script>
(function(){
 var F='ALL', DATA=[], AT='', CC='';   // AT/CC: asset-type / cause-code chip drill-down (''=all)
 function tok(){var t=document.querySelector('input[name=""__RequestVerificationToken""]');return t?t.value:'';}
 function esc(s){return String(s==null?'':s).replace(/[&<>""']/g,function(m){return({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;',""'"":'&#39;'})[m];});}
 function toast(m){var t=document.getElementById('toast');t.textContent=m;t.className='toast show';setTimeout(function(){t.className='toast';},2200);}
 function post(url,obj){return fetch(url,{method:'POST',headers:{'Content-Type':'application/json','X-Wisdom-Token':tok()},body:JSON.stringify(obj)}).then(function(r){return r.ok?r.json():r.text().then(function(t){throw new Error(t.slice(0,200));});});}
 function badge(st){var c=st==='PENDING'?'b-p':(st.indexOf('APPROVED')===0?'b-a':(st==='REJECTED'?'b-r':'b-s'));return '<span class=""badge '+c+'"">'+esc(st)+'</span>';}

 function load(){
   fetch('WhoAmI'.replace('WhoAmI','WisdomWhoAmI'),{headers:{'X-Wisdom-Token':tok()}}).then(function(r){return r.json();}).then(function(w){
     document.getElementById('who').textContent='Signed in as '+(w.displayName||w.identity||'?')+' - reviewer:'+(w.isReviewer?'yes':'no')+' approver:'+(w.isApprover?'yes':'no');
     window.__isApprover=!!w.isApprover;
   }).catch(function(){});
   if(F==='CANDIDATES'){
     fetch('ListCandidates',{headers:{'X-Wisdom-Token':tok()}}).then(function(r){return r.ok?r.json():r.text().then(function(t){throw new Error(t.slice(0,200));});})
      .then(function(d){renderCandidates(d.items||[],d.date);})
      .catch(function(e){document.getElementById('list').innerHTML='<div class=""empty"">Error: '+esc(e.message)+'</div>';});
     return;
   }
   fetch('ListWisdom',{headers:{'X-Wisdom-Token':tok()}}).then(function(r){return r.ok?r.json():r.text().then(function(t){throw new Error(t.slice(0,200));});})
    .then(function(d){DATA=d.items||[];render();})
    .catch(function(e){document.getElementById('list').innerHTML='<div class=""empty"">Error: '+esc(e.message)+'</div>';});
 }

 function tsVal(it){var t=Date.parse(it.ts||'');return isNaN(t)?0:t;}
 function render(){
   var items=DATA.filter(function(it){return F==='ALL'||it.status===F||(F==='APPROVED'&&it.status==='APPROVED_GLOBAL');});
   // v1.0.141.0: chip drill-down -- ASSET TYPE chips, then CAUSE CODE chips, then rules.
   var byAsset={};
   items.forEach(function(it){
     var at=(it.scope&&it.scope.assetType)||'(no asset type)';
     var c=(it.scope&&it.scope.causeCode)||'(no cause)';
     var g=(byAsset[at]=byAsset[at]||{});
     (g[c]=g[c]||[]).push(it);
   });
   var akeys=Object.keys(byAsset).sort();
   if(AT && akeys.indexOf(AT)<0){AT='';CC='';}
   var h='';
   // asset-type chip row
   h+='<div class=""nav2"">';
   h+='<button class=""'+(AT===''?'on':'')+'"" data-at="""">All assets</button>';
   akeys.forEach(function(at){
     var n=0;Object.keys(byAsset[at]).forEach(function(c){n+=byAsset[at][c].length;});
     h+='<button class=""'+(AT===at?'on':'')+'"" data-at=""'+esc(at)+'"">'+esc(at)+' ('+n+')</button>';
   });
   h+='</div>';
   // cause-code chip row (only when an asset type is chosen)
   if(AT){
     var ckeys=Object.keys(byAsset[AT]||{}).sort();
     if(CC && ckeys.indexOf(CC)<0){CC='';}
     h+='<div class=""nav2"">';
     h+='<button class=""'+(CC===''?'on':'')+'"" data-cc="""">All causes</button>';
     ckeys.forEach(function(c){
       h+='<button class=""'+(CC===c?'on':'')+'"" data-cc=""'+esc(c)+'"">'+esc(c)+' ('+byAsset[AT][c].length+')</button>';
     });
     h+='</div>';
   }
   if(!akeys.length){document.getElementById('list').innerHTML=h+'<div class=""empty"">No wisdom in this view.</div>';return;}
   var shown=0;
   akeys.forEach(function(at){
     if(AT && at!==AT){return;}
     var causes=byAsset[at];
     var atCount=0;Object.keys(causes).forEach(function(c){atCount+=causes[c].length;});
     h+='<div class=""atype"">'+esc(at)+' <span class=""cnt"">'+atCount+' rule(s)</span></div>';
     Object.keys(causes).sort().forEach(function(c){
     if(AT && CC && c!==CC){return;}
     shown++;
     var rows=causes[c];
     var pend=rows.filter(function(r){return r.status==='PENDING';}).length;
     h+='<details class=""cause"" open><summary>'+esc(c)+' <span class=""cnt"">'+rows.length+' rule(s)'+(pend?(' , '+pend+' pending'):'')+'</span></summary>';
     if(window.__isApprover){
       h+='<div class=""bulk"">Bulk (pending only): set siteId <input id=""bs_'+esc(c)+'"" placeholder=""siteId""> <button class=""sc"" data-bulk=""'+esc(c)+'"">Apply to all pending</button></div>';
     }
     rows.sort(function(a,b){return tsVal(b)-tsVal(a);});   // latest first
     rows.forEach(function(it){
       var sc=it.scope||{};
       h+='<div class=""rule"" data-id=""'+esc(it.id)+'"">';
       h+='<div class=""scope"">'+badge(it.status)+' &nbsp; '+esc(sc.causeCode||'')+' | '+esc(sc.assetType||'')+' | site '+esc(sc.siteId||'(any)')+'</div>';
       h+='<textarea data-rule=""'+esc(it.id)+'"" '+(it.status==='PENDING'?'':'readonly')+'>'+esc(it.distilledRule||'')+'</textarea>';
       if(it.aiText){h+='<div class=""corr""><b>AI said:</b> '+esc((it.aiText||'').slice(0,300))+'</div>';}
       if(it.userText){h+='<div class=""corr""><b>Correction:</b> '+esc((it.userText||'').slice(0,300))+'</div>';}
       h+='<div class=""meta"">by '+esc(it.createdBy||'?')+(it.approvedBy?(' , approved by '+esc(it.approvedBy)):'')+' , '+esc(it.ts||'')+'</div>';
       h+='<div class=""acts"">';
       if(it.status==='PENDING'){
         h+='<button class=""ed"" data-act=""edit"" data-id=""'+esc(it.id)+'"">Save edit</button>';
         if(window.__isApprover){h+='<button class=""ap"" data-act=""approve"" data-id=""'+esc(it.id)+'"">Approve</button><button class=""rj"" data-act=""reject"" data-id=""'+esc(it.id)+'"">Reject</button>';}
       } else if(it.status.indexOf('APPROVED')===0 && window.__isApprover){
         h+='<button class=""rj"" data-act=""reject"" data-id=""'+esc(it.id)+'"">Reject (retire)</button>';
       }
       h+='</div></div>';
     });
     h+='</details>';
     });
   });
   if(!shown){h+='<div class=""empty"">No rules under this selection.</div>';}
   document.getElementById('list').innerHTML=h;
 }


 function renderCandidates(items,day){
   // group by cause code (falls back to '(no cause in log)')
   var groups={};
   items.forEach(function(it){var c=it.causeCode||'(no cause in log)';(groups[c]=groups[c]||[]).push(it);});
   var keys=Object.keys(groups).sort();
   if(!keys.length){document.getElementById('list').innerHTML='<div class=""empty"">No analyses logged for '+esc(day||'')+'.</div>';return;}
   var h='<div style=""font-size:12px;color:#8b95a3;margin-bottom:8px;"">Auto-captured from '+esc(day||'')+' analyses + chat. Promote turns one into a PENDING wisdom rule.</div>';
   keys.forEach(function(c){
     var rows=groups[c];
     h+='<details class=""cause"" open><summary>'+esc(c)+' <span class=""cnt"">'+rows.length+' analysis(es)</span></summary>';
     rows.forEach(function(it){
       h+='<div class=""rule"">';
       h+='<div class=""scope"">'+esc(it.verdict||'(no verdict)')+' &nbsp; alert '+esc(it.alertId)+' , site '+esc(it.siteId||'?')+(it.hasChat?' , <span style=""color:#5eead4"">has chat</span>':'')+'</div>';
       if(it.headline){h+='<div class=""corr"">'+esc(it.headline)+'</div>';}
       if(it.chat){h+='<div class=""corr""><b>Chat:</b> '+esc(it.chat)+'</div>';}
       h+='<div class=""meta"">'+esc(it.ts||'')+'</div>';
       h+='<div class=""acts""><input class=""cand-rule"" data-id=""'+esc(it.alertId)+'"" placeholder=""rule to teach (optional - blank = distill from verdict/chat)"" style=""flex:1;min-width:200px;background:#0d1017;color:#e8eaed;border:1px solid #2a3342;border-radius:6px;padding:6px 8px;font:inherit;font-size:12px;"">';
       h+='<button class=""sc"" data-promote=""'+esc(it.alertId)+'"" data-cause=""'+esc(it.causeCode||'')+'"" data-site=""'+esc(it.siteId||'')+'"" data-station=""'+esc(it.station||'')+'"">Promote to wisdom</button></div>';
       h+='</div>';
     });
     h+='</details>';
   });
   document.getElementById('list').innerHTML=h;
   // stash for promote lookups
   window.__cands={}; items.forEach(function(it){window.__cands[it.alertId]=it;});
 }

 document.addEventListener('click',function(e){
   var b=e.target; if(b.tagName!=='BUTTON')return;
   var promoteId=b.getAttribute('data-promote');
   if(promoteId!=null){
     var it=(window.__cands||{})[promoteId]||{};
     var typed=document.querySelector('input.cand-rule[data-id=""'+promoteId+'""]'); var manual=typed?typed.value.trim():'';
     var ctx={alertId:String(promoteId),causeCode:b.getAttribute('data-cause')||'',siteId:b.getAttribute('data-site')||'',station:b.getAttribute('data-station')||'',provider:''};
     // aiText = the AI's verdict/headline; userText = the engineer signal (typed rule, else the chat, else the verdict)
     var aiText=(it.verdict?('['+it.verdict+'] '):'')+(it.headline||'');
     var userText=manual||it.chat||it.headline||'';
     if(!ctx.causeCode){toast('This analysis has no cause code in the log - cannot scope a rule');return;}
     post('SaveWisdom',{context:ctx,verdictGiven:it.verdict||'',userText:userText,aiText:aiText}).then(function(d){toast('Promoted -> pending wisdom ('+(d.distilledRule?('rule: '+d.distilledRule.slice(0,40)):'needs edit')+')');}).catch(function(e2){toast('Promote failed: '+e2.message);});
     return;
   }
   if(b.hasAttribute('data-at')){AT=b.getAttribute('data-at')||'';CC='';render();return;}
   if(b.hasAttribute('data-cc')){CC=b.getAttribute('data-cc')||'';render();return;}
   if(b.hasAttribute('data-f')){F=b.getAttribute('data-f');document.querySelectorAll('.filters button[data-f]').forEach(function(x){x.className=x===b?'on':'';});render();return;}
   if(b.id==='refresh'){load();return;}
   var bulk=b.getAttribute('data-bulk');
   if(bulk!=null){
     var inp=document.getElementById('bs_'+bulk); var sid=inp?inp.value.trim():'';
     if(!sid){toast('Enter a siteId first');return;}
     post('BulkScopeWisdom',{causeCode:bulk,newSiteId:sid}).then(function(d){toast('Updated '+d.changed+' pending rule(s)');load();}).catch(function(e2){toast('Bulk failed: '+e2.message);});
     return;
   }
   var act=b.getAttribute('data-act'), id=b.getAttribute('data-id'); if(!act||!id)return;
   var ta=document.querySelector('textarea[data-rule=""'+id+'""]'); var rule=ta?ta.value:'';
   if(act==='edit'){post('UpdateWisdomRule',{id:id,editedRule:rule}).then(function(){toast('Saved');load();}).catch(function(e2){toast('Edit failed: '+e2.message);});}
   else if(act==='approve'){post('SetWisdomStatus',{id:id,status:'APPROVED',editedRule:rule}).then(function(){toast('Approved');load();}).catch(function(e2){toast('Approve failed: '+e2.message);});}
   else if(act==='reject'){post('SetWisdomStatus',{id:id,status:'REJECTED'}).then(function(){toast('Rejected');load();}).catch(function(e2){toast('Reject failed: '+e2.message);});}
 });

 load();
})();
</script>
</body></html>";

        // GET /AiChat/ListCandidates?date=YYYYMMDD  (reviewer-gated)
        // Candidates are DERIVED from the date-wise logs (no separate store):
        // ai-analyze (now carrying the verdict) joined with ai-chat by alertId.
        // Each is a promotable observation {alertId, causeCode, verdict, chat, ts}.
        [HttpGet]
        public ActionResult ListCandidates(string date = "")
        {
            try
            {
                if (!AnalysisEngine._wisdomEnabled) { Response.StatusCode = 404; return Content("not found", "text/plain"); }
                if (!WisdomCanReview()) { return WisdomDenied("ListCandidates"); }
                string day = string.IsNullOrWhiteSpace(date) ? DateTime.Now.ToString("yyyyMMdd") : new string(date.Where(char.IsDigit).ToArray());
                if (day.Length != 8) { day = DateTime.Now.ToString("yyyyMMdd"); }

                // 1) analyses for the day: alertId -> {causeCode?, verdict summary, ts}
                Dictionary<string, JObject> byAlert = new Dictionary<string, JObject>(StringComparer.Ordinal);
                string analyzePath = Path.Combine(AnalysisEngine._wisdomChatDir, "ai-analyze-" + day + ".jsonl");
                if (System.IO.File.Exists(analyzePath))
                {
                    foreach (string ln in System.IO.File.ReadAllLines(analyzePath))
                    {
                        if (string.IsNullOrWhiteSpace(ln)) { continue; }
                        JObject rec; try { rec = JObject.Parse(ln); } catch { continue; }
                        JObject data = rec["data"] as JObject; if (data == null) { continue; }
                        string aid = data.Value<string>("alertId"); if (string.IsNullOrEmpty(aid)) { continue; }
                        JObject slot; if (!byAlert.TryGetValue(aid, out slot)) { slot = new JObject(); slot["alertId"] = aid; byAlert[aid] = slot; }
                        string step = rec.Value<string>("step") ?? "";
                        if (step == "request_start" || step == "prompt") { slot["ts"] = rec.Value<string>("tsUtc") ?? slot.Value<string>("ts"); }
                        JToken vv = data["verdict"];
                        if (vv != null && vv.Type == JTokenType.Object) { slot["verdict"] = vv; }
                        if (data["causeCode"] != null && !string.IsNullOrEmpty((string)data["causeCode"])) { slot["causeCode"] = data["causeCode"]; }
                        if (data["siteId"] != null && !string.IsNullOrEmpty((string)data["siteId"])) { slot["siteId"] = data["siteId"]; }
                        if (data["station"] != null && !string.IsNullOrEmpty((string)data["station"])) { slot["station"] = data["station"]; }
                    }
                }

                // 2) chat for the day: alertId -> concatenated USER/ASSISTANT lines
                Dictionary<string, StringBuilder> chatByAlert = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
                string chatPath = Path.Combine(AnalysisEngine._wisdomChatDir, "ai-chat-" + day + ".txt");
                if (System.IO.File.Exists(chatPath))
                {
                    string curAlert = null;
                    foreach (string ln in System.IO.File.ReadAllLines(chatPath))
                    {
                        int ix = ln.IndexOf("alertId=", StringComparison.Ordinal);
                        if (ix >= 0)
                        {
                            string rest = ln.Substring(ix + 8);
                            int sp = rest.IndexOf(' '); curAlert = sp > 0 ? rest.Substring(0, sp) : rest.Trim();
                            continue;
                        }
                        if (curAlert != null && (ln.TrimStart().StartsWith("USER:") || ln.TrimStart().StartsWith("ASSISTANT")))
                        {
                            StringBuilder cb; if (!chatByAlert.TryGetValue(curAlert, out cb)) { cb = new StringBuilder(); chatByAlert[curAlert] = cb; }
                            if (cb.Length < 4000) { cb.Append(ln.Trim()).Append("\n"); }
                        }
                    }
                }

                // 3) join. causeCode is not in ai-analyze; the promote step supplies it
                // from the alert context. Group key on the client is verdict-derived, so
                // include what we have and let the UI/promote fill causeCode.
                List<object> items = new List<object>();
                foreach (KeyValuePair<string, JObject> kv in byAlert)
                {
                    JObject slot = kv.Value;
                    string chat = chatByAlert.ContainsKey(kv.Key) ? chatByAlert[kv.Key].ToString() : "";
                    JToken v = slot["verdict"];
                    string vd = (v != null && v.Type == JTokenType.Object && v["verdict"] != null) ? v["verdict"].ToString() : "";
                    string cat = (v != null && v.Type == JTokenType.Object && v["category"] != null) ? v["category"].ToString() : "";
                    string head = (v != null && v.Type == JTokenType.Object && v["headline"] != null) ? v["headline"].ToString() : "";
                    items.Add(new
                    {
                        alertId = kv.Key,
                        causeCode = slot.Value<string>("causeCode") ?? "",
                        siteId = slot.Value<string>("siteId") ?? "",
                        station = slot.Value<string>("station") ?? "",
                        verdict = vd,
                        category = cat,
                        headline = head,
                        hasChat = chat.Length > 0,
                        chat = AnalysisEngine.ClampLen(chat, 2000),
                        ts = slot.Value<string>("ts") ?? ""
                    });
                }
                return Json(new { ok = true, date = day, count = items.Count, items = items }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception wex)
            {
                return WisdomFail("ListCandidates", wex);
            }
        }

        // ==================================================================
        //  small local helpers
        // ==================================================================
        private async Task<JObject> ReadJsonBodyAsync()
        {
            try
            {
                string body;
                using (StreamReader sr = new StreamReader(Request.InputStream, Encoding.UTF8))
                {
                    body = await sr.ReadToEndAsync().ConfigureAwait(false);
                }
                return JObject.Parse(body);
            }
            catch { return null; }
        }
    }

}