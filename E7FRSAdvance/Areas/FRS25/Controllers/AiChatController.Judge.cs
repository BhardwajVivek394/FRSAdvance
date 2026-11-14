using E7.AiCore;
using E7.AiCore.Judge;
using System;
using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, AI clients) keep the 616 namespace
using System.Configuration;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // AiChatController -- the alert judge (2.0.1.0, E7.AiCore 1.0.170.0).
    //
    // Pass-through only, like AnalyzeAlertJson: the key check, the body read and the HTTP status
    // are decided by E7.AiCore.Judge.JudgeEngine; this file moves bytes and answers CORS. CHANGE
    // HERE only to rename a URL or a header, or to change who may call from a browser. The
    // request/response contract is E7.AiCore\Judge\JudgeEnvelope.cs.
    //
    //   POST    /FRS25/AiChat/JudgeAlertJson    header X-Internal-Api-Key (= web.config InternalApiKey)
    //           body: the AnalyzeAlertJson context + optional "judge": { mode, useAi, engine, familyTrackIds, trace }
    //           -> { status, verdict, meta, context_patch }   (verdict.finalDecision = PASS | SUPPRESSED)
    //   GET     /FRS25/AiChat/JudgeRuleJson?cause=217         one cause's rule;  no cause = the list
    //   OPTIONS on either                                     CORS preflight (see the CORS section at the end)
    public partial class AiChatController : Controller
    {
        [HttpPost]
        public async Task<ActionResult> JudgeAlertJson()
        {
            try
            {
                Response.TrySkipIisCustomErrors = true;
            }
            catch
            {
            }

            // first, so a browser can read an error reply too
            ApplyJudgeCors();

            JudgeHttpRequest req = new JudgeHttpRequest();
            try
            {
                req.ConfiguredKey = ConfigurationManager.AppSettings["InternalApiKey"];
                req.SuppliedKey = Request.Headers["X-Internal-Api-Key"];
                req.RequestId = Request.Headers["X-Request-Id"];
                req.ContentLength = Request.ContentLength;

                CancellationToken clientCt = CancellationToken.None;
                try
                {
                    clientCt = Response.ClientDisconnectedToken;
                }
                catch
                {
                }

                using (CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(clientCt))
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        req.Body = await AnalysisEngine.ReadBodyBoundedAsync(Request.InputStream, 262144, readCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        req.Body = null;
                    }
                }

                JudgeHttpResult result = await JudgeHost.Engine.HandleAsync(req, clientCt).ConfigureAwait(false);
                try
                {
                    Response.StatusCode = result.HttpStatus;
                    if (!string.IsNullOrEmpty(result.AuditId))
                    {
                        Response.Headers["X-Judge-Audit-Id"] = result.AuditId;
                    }
                }
                catch
                {
                }

                return Content(result.Json, "application/json");
            }
            catch
            {
                try
                {
                    Response.StatusCode = 200;
                }
                catch
                {
                }

                return Content("{\"status\":\"model_error\",\"verdict\":null,\"meta\":{\"error\":\"internal\"}}", "application/json");
            }
        }

        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult JudgeRuleJson(string cause)
        {
            ApplyJudgeCors();
            try
            {
                JudgeHttpRequest req = new JudgeHttpRequest();
                req.ConfiguredKey = ConfigurationManager.AppSettings["InternalApiKey"];
                req.SuppliedKey = Request.Headers["X-Internal-Api-Key"];
                req.RequestId = Request.Headers["X-Request-Id"];
                JudgeHttpResult result = JudgeHost.Engine.HandleRules(req, cause);
                try
                {
                    Response.StatusCode = result.HttpStatus;
                }
                catch
                {
                }

                return Content(result.Json, "application/json");
            }
            catch
            {
                return Content("{\"status\":\"model_error\",\"rule\":null,\"meta\":{\"error\":\"internal\"}}", "application/json");
            }
        }

        // ═════════════════════════════════════════════════════════
        //  CORS -- for a browser page that calls the judge directly
        // ═════════════════════════════════════════════════════════
        // 2.0.1.0. OFF unless web.config names the origins that may call:
        //
        //   <add key="JudgeCorsOrigins" value="https://energy7.in:619,http://localhost:5500" />
        //
        // Exact origins (scheme + host + port), comma or semicolon separated; a trailing "/" is
        // ignored; "*" means any origin. With the key missing or blank NOTHING changes -- no CORS
        // header is sent and the endpoints behave exactly as in 2.0.0.0.
        //
        // The reply always echoes the CALLER'S origin, never the literal "*", so it stays valid
        // alongside Access-Control-Allow-Credentials (a "*" origin plus credentials is rejected by
        // every browser). That also keeps it working if a page later calls with cookies.
        //
        // The OPTIONS preflight carries no API key -- a browser never sends custom headers on a
        // preflight -- so it is answered BEFORE the key check. The real POST still needs the key.
        //
        // IIS: an extensionless URL reaches MVC for GET/HEAD/POST/DEBUG only unless
        // ExtensionlessUrlHandler-Integrated-4.0 is registered with verb="*" (most MVC 5 web.configs
        // already are). If OPTIONS returns 200 with an "Allow:" header and no CORS header, IIS
        // answered it instead of this action -- see the deploy note.
        //
        // THE SECRET: X-Internal-Api-Key is a SERVICE key. A page allowed here must hold it in its
        // own JavaScript, where anyone who can open the page can read it. Keep JudgeCorsOrigins
        // blank in production and set it only on the test server. For a page inside RDPMS, a
        // session-authenticated action (no key, same origin, no CORS) is the right route.

        [AcceptVerbs(HttpVerbs.Options)]
        [ActionName("JudgeAlertJson")]
        public ActionResult JudgeAlertJsonPreflight()
        {
            return JudgePreflight();
        }

        [AcceptVerbs(HttpVerbs.Options)]
        [ActionName("JudgeRuleJson")]
        public ActionResult JudgeRuleJsonPreflight()
        {
            return JudgePreflight();
        }

        private static readonly char[] JudgeCorsSeparators = new char[] { ',', ';' };

        /// <summary>
        /// Adds the CORS response headers when the caller sent an Origin this site allows.
        /// Returns false, and sends nothing, for a non-browser call or an origin not on the list.
        /// </summary>
        private bool ApplyJudgeCors()
        {
            try
            {
                string origin = Request.Headers["Origin"];
                if (string.IsNullOrEmpty(origin))
                {
                    return false;   // Postman, AlertV3, the Debouncer: no Origin header, nothing to do
                }

                string configured = ConfigurationManager.AppSettings["JudgeCorsOrigins"];
                if (string.IsNullOrWhiteSpace(configured))
                {
                    return false;   // CORS not enabled on this site
                }

                string caller = origin.Trim().TrimEnd('/');
                bool allowed = false;
                string[] list = configured.Split(JudgeCorsSeparators, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < list.Length; i++)
                {
                    string entry = list[i].Trim().TrimEnd('/');
                    if (entry == "*" || string.Equals(entry, caller, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                {
                    return false;
                }

                Response.Headers["Access-Control-Allow-Origin"] = origin;
                Response.Headers["Access-Control-Allow-Credentials"] = "true";
                Response.Headers["Access-Control-Expose-Headers"] = "X-Judge-Audit-Id";
                Response.Headers["Vary"] = "Origin";   // the reply differs per origin: never cache it for another
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Answers the OPTIONS preflight: 204 with the allowed methods and headers, or 403.</summary>
        private ActionResult JudgePreflight()
        {
            try
            {
                Response.TrySkipIisCustomErrors = true;
            }
            catch
            {
            }

            bool allowed = ApplyJudgeCors();
            try
            {
                if (allowed)
                {
                    Response.Headers["Access-Control-Allow-Methods"] = "POST, GET, OPTIONS";
                    Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Internal-Api-Key, X-Request-Id";
                    Response.Headers["Access-Control-Max-Age"] = "600";   // one preflight per page, not per call
                }

                Response.StatusCode = allowed ? 204 : 403;
            }
            catch
            {
            }

            return new EmptyResult();
        }
    }
}
