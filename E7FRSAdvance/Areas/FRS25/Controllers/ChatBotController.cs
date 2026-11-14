using Domain;
using E7FRSAdvance.Interface;   // v1.0.182.0: ISiteService, for assigned-station names
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
using E7.AiCore;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    /// <summary>
    /// Serves the standalone, fullscreen AI Assistant page AND hosts the
    /// chat backend itself (merged from AiChatController — no separate
    /// controller needed). The Index view calls back into THIS controller:
    ///   GET  /FRS25/ChatBot/Status  — tool/connectivity check
    ///   POST /FRS25/ChatBot/Chat    — streaming (SSE) agentic chat loop
    /// </summary>

    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]   // 1.4.1.0: enabled (was commented out) -- verify streaming on staging
    public partial class ChatBotController : Controller, IAnalysisHost
    {

        // ---- Shared AI Core 3a (1.2.10.0) ---------------------------------------------------------------------
        // The ChatBot logic now lives in E7.AiCore.ChatEngine. One engine per controller (= per request), created
        // in the constructor; the engine streams back through IAnalysisHost.WriteSse.
        private readonly ChatEngine Engine;

        public ChatBotController()
        {
            // 3c: let core code read the logged-in user without referencing the web project.
            AiUserContext.Current = () => E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            AiUserContext.AdminRoleId = E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode();
            AiUserContext.UserRoleId = E7FRSAdvance.Utility.Utility.Role.User.GetHashCode();
            Engine = new ChatEngine(this);
        }

        void IAnalysisHost.WriteSse(string type, object data) { WriteSse(type, data); }

        // 1.0.195.0 -- the dead Lazy<AnthropicClient> _ai field was removed here. It was declared
        // and never dereferenced (its only mention in this file was the declaration), so the only
        // thing it did was keep ChatEngine.CreateAiClient alive -- and with it the last
        // AnthropicApiKey read in ChatEngine.Main.cs, which has gone with it.

        // ═════════════════════════════════════════════════════════
        //  GET /FRS25/ChatBot/Index — the fullscreen chat page itself
        // ═════════════════════════════════════════════════════════
        // v1.1.0: there was NO version constant in this file -- you could not tell what was deployed.
        public const string ComponentVersion = "1.5.0.0";   // 1.5.0.0: chat-app UI (Views\ChatBot\Index.cshtml); controller logic unchanged since 1.4.1.0.   // 1.4.0.0: logic -> E7.AiCore.ChatEngine, bash removed; 1.4.1.0: [Authenticate] on, 13 copies -> shared core implementations

        public ActionResult Index()
        {
            return View();
        }

        // ═════════════════════════════════════════════════════════
        //  GET /FRS25/ChatBot/Status
        // ═════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<ActionResult> Status()
        {
            // v1.2.7.0 -- F1-response. Privilege is captured BEFORE the first await so the gate below
            // cannot be decided on a post-await HttpContext. A non-privileged caller learns only
            // whether the assistant is usable; topology (srv2Enabled, mcp1/mcp2), library names and
            // raw exception text are privileged-only. ex.Message is NEVER returned unprivileged.
            bool priv = ChatEngine.ChatBotAllowed();
            try
            {
                ChatEngine.ToolLoadResult loaded = await Engine.LoadAllToolsAsync().ConfigureAwait(false);
                if (!priv)
                {
                    return Json(loaded.Tools.Count > 0
                        ? (object)new { ok = true }
                        : (object)new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
                }
                return Json(new
                {
                    ok = loaded.Tools.Count > 0,
                    version = ComponentVersion,          // v1.1.0
                    allowed = priv,                      // v1.1.0: drives the UI notice
                    engineChoice = priv,                 // v1.2.0.0: picker only for privileged
                    engineBrand = ChatEngine.ReadStr("AiEngineBrand", "ENERGY7 ULTRA"),   // v1.2.0.0
                    srv2Enabled = ChatEngine._srv2Enabled,          // v1.1.0
                    tools = loaded.Tools.Count,
                    mcp1 = new { tools = loaded.Count1, healthy = (loaded.Error1 == null) },
                    mcp2 = new { tools = loaded.Count2, healthy = (loaded.Error2 == null) }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                // v1.2.8.0: ex.Message never leaves the server, at ANY privilege.
                System.Diagnostics.Trace.TraceWarning("[ChatBot] status failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        // ═════════════════════════════════════════════════════════
        //  v1.0.182.0 -- STATION ID -> NAME
        //
        //  The assistant was given its assigned stations as bare ids. Nothing in the prompt said
        //  what they were called, so it could only name a station whose name happened to appear in
        //  a tool result it had already fetched. Sites with recent alert activity resolved; the
        //  rest did not, and the user was told "the site lookup returned no matching records" for
        //  stations that are plainly in the Telemetry Live dropdown.
        //
        //  This resolves them from ISiteService -- the SAME service that fills that dropdown
        //  (TelemetryController.GetSiteByDivisionId / FilterCacheHelper), so if a station is
        //  selectable in the UI it is nameable here.
        //
        //  Via DependencyResolver rather than a constructor parameter: ChatBotController is
        //  constructed parameterless today and changing that risks how the container builds it,
        //  for no gain. Every failure degrades to the old behaviour (ids only) -- a chatbot that
        //  cannot name a station is a small loss; one that will not start is not.
        // ═════════════════════════════════════════════════════════
        private static Dictionary<string, string> ResolveStationNames(List<string> stationIds)
        {
            if (stationIds == null || stationIds.Count == 0) { return null; }

            try
            {
                ISiteService svc = System.Web.Mvc.DependencyResolver.Current
                    .GetService(typeof(ISiteService)) as ISiteService;
                if (svc == null) { return null; }

                Dictionary<string, string> outp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < stationIds.Count; i++)
                {
                    string raw = (stationIds[i] ?? "").Trim();
                    int id;
                    if (raw.Length == 0 || !int.TryParse(raw, out id) || id <= 0) { continue; }
                    if (outp.ContainsKey(raw)) { continue; }

                    try
                    {
                        Domain.Site site = svc.Get(id);
                        if (site == null || string.IsNullOrWhiteSpace(site.Name)) { continue; }

                        string name = site.Name.Trim();
                        if (!string.IsNullOrWhiteSpace(site.StationCode))
                        {
                            name += " (" + site.StationCode.Trim() + ")";
                        }

                        outp[raw] = name;
                    }
                    catch
                    {
                        // one unreadable site must not cost the other nine their names
                    }
                }

                return outp.Count > 0 ? outp : null;
            }
            catch
            {
                return null;
            }
        }


        // ═════════════════════════════════════════════════════════
        //  POST /FRS25/ChatBot/Chat
        // ═════════════════════════════════════════════════════════
        [HttpPost]
        public async Task<ActionResult> Chat()
        {
            // v1.2.0.0: the v1.1.0 403 is REPLACED by the AiChat scheme -- everyone may use the
            // assistant, but a normal user is forced to QUANTUM and gets no engine choice. That is
            // safe because v1.1.0 also added the tool allowlist: a normal user reaches telemetry
            // tools only -- no bash, no logwatch.
            bool _privileged = ChatEngine.ChatBotAllowed();
            Engine._chatPrivileged = _privileged;   // v1.2.4.0: the tool merge runs post-await
            Engine._chatStations = ChatEngine.GetAssignedStations();   // v1.2.9.6 (#2): LoginUser is null after the first await
            Engine._chatStationNames = ResolveStationNames(Engine._chatStations);   // v1.0.182.0
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync().ConfigureAwait(false);

            JArray messagesRaw;
            try
            {
                JObject payload = JObject.Parse(body);
                messagesRaw = payload["messages"] as JArray;
                // v1.2.0.0: honour the picker for privileged users; force QUANTUM otherwise.
                string _reqProv = payload["provider"] != null ? payload["provider"].ToString() : "";
                _reqProv = ChatEngine.ResolveEngineAlias(_reqProv);   // v1.2.7.0: opaque id -> raw provider BEFORE gating
                Engine._provider = Engine.GateProviderForRole(_reqProv, _privileged);
                Engine._pageContext = payload["pageContext"] as JObject;   // v1.2.9.6 (#5): optional sub-page context
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
                // v1.2.7.0 -- F7. FAIL CLOSED: an instruction-extraction request is refused BEFORE the
                // model and tools run, so a summary or paraphrase of the system prompt cannot be
                // produced at all. Stays inside this try so the finally below emits [DONE] exactly
                // once, and returns EmptyResult (a bare return cannot compile in Task<ActionResult>).
                if (ChatEngine.IsInstructionExtractionRequest(ChatEngine.LastRealUserText(messagesRaw)))
                {
                    WriteSse("text", new { text = FixedExtractionRefusal });
                    return new EmptyResult();
                }

                // v1.2.9.8: a question about the plumbing -- which table, which tool, what API --
                // is refused here too, before the model and the tools run. ChatEngine.Chat has the
                // same gate; this one covers the turn before the engine is entered.
                if (E7.AiCore.ScopeGuard.IsInternalsQuestion(ChatEngine.LastRealUserText(messagesRaw)))
                {
                    WriteSse("text", new { text = E7.AiCore.ScopeGuard.InternalsRefusal });
                    return new EmptyResult();
                }

                await Engine.RunAgenticLoop(messagesRaw).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteSse("error", new { message = ChatEngine.FriendlyError(ex) });
            }
            finally
            {
                try { Response.Write("data: [DONE]\n\n"); Response.Flush(); } catch { }
            }

            return new EmptyResult();
        }

        private void WriteSse(string type, object data)
        {
            try
            {
                JObject payload = JObject.FromObject(data);
                payload["type"] = type;
                Response.Write("data: " + payload.ToString(Formatting.None) + "\n\n");
                Response.Flush();
            }
            catch { }
        }

        // v1.2.5.0 =============================================================================
        //  MAINTENANCE SCOPE -- a railway maintenance verdict must contain ONLY railway-side field
        //  defects; RDPMS monitoring-chain conditions (network/comms down, device/sensor health) are
        //  not railway maintenance. See ComponentVersion note.
        // =====================================================================================

        // v1.2.7.0 =============================================================================
        //  F7 -- INSTRUCTION PROTECTION. The system prompt, its rules and the tool wiring are not
        //  content. A request to reveal them is refused BEFORE the model runs (fail closed), and a
        //  verbatim-echo scrub backstops any rephrasing that slips the detector.
        //  IMPORTANT: the protected corpus is the SYSTEM PROMPT ONLY. The training text carries
        //  domain knowledge that answers are SUPPOSED to use -- scrubbing it would redact legitimate
        //  railway doctrine.
        // =====================================================================================

        private const string FixedExtractionRefusal =
            "I can help with railway asset analysis and monitoring. I am not able to share system "
            + "configuration details. What would you like to know about your assets or alerts?";
    }
}
