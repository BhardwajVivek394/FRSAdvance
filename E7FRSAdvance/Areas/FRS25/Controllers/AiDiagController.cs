// AiDiagController.cs -- component version (Major.Minor.Build.Revision)
// Version: 1.0.0.3
//
// Version history (one line per shipped version, newest first):
// 1.0.0.3 - Sites/Assets are fetched SERVER-SIDE now (owner). The page used to call the proxy
//     directly from the browser, which published the proxy host to every client, required CORS,
//     and put an internal endpoint in the network tab of anyone who opened the page. Two new
//     same-origin actions -- Sites() and Assets(siteId) -- call it through
//     AiChatController.DiagProxyGetAsync() using ConfigurationManager.AppSettings["ProxyBaseUrl"],
//     behind the same [Authenticate] + DiagAllowedCore() gate as the rest of the page. Both return
//     {ok,...} so a proxy failure renders as an explicit FAIL tile rather than an empty dropdown.
// 1.0.0.2 - FIXES the 403 the first deploy hit. THREE separate causes, all mine:
//     (a) NO [Authenticate] FILTER. AlertAnalysisController and ChatBotController both carry
//         [E7MRIWeb.Areas.FRS25.Filter.Authenticate]; the split controller carried nothing, so
//         the framework rejected the request before any of this code ran -- which is why the
//         reply was the IIS page, not the gate's own wording. Added.
//     (b) CaptureLoginUser() WAS NEVER CALLED. The old AiChat actions called it as their first
//         statement, then gated. DiagAllowedCore() reads _luSiteKeep/_luEmail, which only exist
//         after that capture, so the email allowlist could never match. Captured now, before
//         the gate, on both actions.
//     (c) IIS CUSTOM ERRORS REPLACED THE 403 BODY. Setting Response.StatusCode = 403 hands the
//         response to IIS's error page unless TrySkipIisCustomErrors is set, so even a correct
//         denial read as "you do not have permission to view this directory". Set on both.
// 1.0.0.1 - FIRST CUT. Owner: "implement one separate controller with view for Diag". The
//     diagnostic dashboard moves off AiChatController into its own controller and its own
//     Views/AiDiag folder, so the analysis controller is not carrying an ops surface and the
//     Diag routes can be reasoned about (and secured, and IIS-restricted) on their own.
//     Routes: GET /FRS25/AiDiag           -> the page
//             GET|POST /FRS25/AiDiag/Run  -> the probe catalog as JSON (LogWatch uses this)
//     WHY THE PROBE BODY IS NOT IN THIS FILE: it calls ~19 PRIVATE members of
//     AiChatController (PrefetchCallAsync, LoadAllToolsAsync, HistWindowArgs/HistFetchAsync,
//     NewSiteAlertArgs/NewAssetHistoryArgs/NewRangeHistoryArgs, IsE7ApiFailureText,
//     LooksUsableToolResult, the audit-capability fields). Moving it would mean widening all
//     of those on proven code, or duplicating them -- both worse than one internal entry
//     point. So this controller owns the ROUTE, the GATE, the VIEW and the RESPONSE, and
//     delegates the run to AiChatController.DiagRunCoreAsync(). Same assembly, so internal
//     is enough; nothing new is exposed publicly.
//     The gate is unchanged in behaviour (.158): site-keeping REQUIRED, and when
//     DiagAllowedEmails is configured the logged-in email must also be on it. It is evaluated
//     by AiChatController.DiagAllowedCore() -- ONE implementation, because a security gate
//     copied into two files is a gate that drifts.
using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // v1.0.0.2: same authentication filter every other FRS25 page controller carries. Without
    // it the framework denies the request before the action runs, and the caller sees the IIS
    // 403 page rather than this controller's own gate message.
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AiDiagController : Controller
    {
        public const string ComponentVersion = "1.0.0.3";

        // The gate and the historian base both live with the probe catalog so there is exactly
        // one implementation of each. This controller only decides what to DO when they say no.
        // v1.0.0.2: capture the login BEFORE handing the instance back. DiagAllowedCore() reads
        // the captured _luSiteKeep/_luEmail; the old AiChat actions did this as their first
        // statement and the split dropped it, so the gate was judging an empty session.
        private static AiChatController NewCore()
        {
            AiChatController c = new AiChatController();
            c.CaptureLoginUserForDiag();
            return c;
        }

        // GET /FRS25/AiDiag
        public ActionResult Index()
        {
            AiChatController core = NewCore();
            if (!core.DiagAllowedCore())
            {
                Response.StatusCode = 403;
                // v1.0.0.2: without this IIS swallows the body and serves its own 403 page.
                Response.TrySkipIisCustomErrors = true;
                return Content("diagnostics are restricted");
            }
            // v1.0.0.3: the page no longer receives the proxy base -- it calls Sites()/Assets()
            // instead, and the base stays server-side.
            ViewBag.DiagComponentVersion = ComponentVersion;
            return View("Index");
        }

        // GET /FRS25/AiDiag/Sites -- the site list, fetched server-side from ProxyBaseUrl.
        // /api/Sites first, /api/A10/Sites for older servers (the same fallback the page had).
        public async Task<ActionResult> Sites()
        {
            AiChatController core = NewCore();
            if (!core.DiagAllowedCore())
            {
                Response.StatusCode = 403;
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false }, { "error", "diagnostics are restricted" } }
                    .ToString(Formatting.None), "application/json");
            }
            string src = "/api/Sites";
            string body = await AiChatController.DiagProxyGetAsync(src).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body))
            {
                src = "/api/A10/Sites";
                body = await AiChatController.DiagProxyGetAsync(src).ConfigureAwait(false);
            }
            JArray rows = DiagParseArray(body);
            if (rows == null)
            {
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false },
                    { "error", "no site list from the proxy (/api/Sites and /api/A10/Sites)" } }
                    .ToString(Formatting.None), "application/json");
            }
            JObject res = new JObject();
            res["ok"] = true;
            res["source"] = src;
            res["sites"] = rows;
            return Content(res.ToString(Formatting.None), "application/json");
        }

        // GET /FRS25/AiDiag/Assets?siteId=161 -- live tag rows for the site, server-side. The page
        // derives the distinct assets from these rows exactly as before; the shape is unchanged, so
        // an empty list still means "site stale", which is check D0b.
        public async Task<ActionResult> Assets(string siteId)
        {
            AiChatController core = NewCore();
            if (!core.DiagAllowedCore())
            {
                Response.StatusCode = 403;
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false }, { "error", "diagnostics are restricted" } }
                    .ToString(Formatting.None), "application/json");
            }
            long sid;
            if (!long.TryParse((siteId ?? "").Trim(), out sid) || sid <= 0)
            {
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false }, { "error", "siteId is required" } }
                    .ToString(Formatting.None), "application/json");
            }
            string body = await AiChatController.DiagProxyGetAsync("/api/LiveValue?SiteId=" + sid).ConfigureAwait(false);
            JArray rows = DiagParseArray(body);
            if (rows == null)
            {
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false },
                    { "error", "no live data from the proxy for site " + sid } }
                    .ToString(Formatting.None), "application/json");
            }
            JObject res = new JObject();
            res["ok"] = true;
            res["tags"] = rows;
            return Content(res.ToString(Formatting.None), "application/json");
        }

        // Accepts a bare array or an object wrapping one; null when the body is unusable, so the
        // caller can say WHY rather than render an empty picker.
        private static JArray DiagParseArray(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }
            try
            {
                JToken t = JToken.Parse(body);
                if (t is JArray)
                {
                    return (JArray)t;
                }
                JObject o = t as JObject;
                if (o != null)
                {
                    string[] keys = new string[] { "data", "Data", "rows", "Rows", "result", "Result", "items", "Items" };
                    for (int i = 0; i < keys.Length; i++)
                    {
                        JToken v = o[keys[i]];
                        if (v is JArray)
                        {
                            return (JArray)v;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // GET|POST /FRS25/AiDiag/Run?siteId=&assetId=&assetName=&assetType=
        // The page and E7 LogWatch share this route.
        public async Task<ActionResult> Run(string siteId, string assetId, string assetName, string assetType)
        {
            AiChatController core = NewCore();
            if (!core.DiagAllowedCore())
            {
                Response.StatusCode = 403;
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false }, { "error", "diagnostics are restricted" } }
                    .ToString(Formatting.None), "application/json");
            }
            try
            {
                JObject res = await core.DiagRunCoreAsync(siteId, assetId, assetName, assetType).ConfigureAwait(false);
                if (res == null)
                {
                    res = new JObject { { "ok", false }, { "error", "no result" } };
                }
                // Content(), not Json(): the payload is already a JObject, and JsonResult would
                // re-serialise it through the default serialiser for no benefit.
                return Content(res.ToString(Formatting.None), "application/json");
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                Response.TrySkipIisCustomErrors = true;
                return Content(new JObject { { "ok", false }, { "error", ex.Message } }
                    .ToString(Formatting.None), "application/json");
            }
        }
    }
}
