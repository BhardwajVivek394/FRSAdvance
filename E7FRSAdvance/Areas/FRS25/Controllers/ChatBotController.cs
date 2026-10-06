using E7.AiCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

// FRS advance port of E7MRIWeb ChatBotController 1.5.0.0 (chat endpoints only; the ChatBot page itself is
// not ported). Same engine (E7.AiCore.ChatEngine), same request body {messages, provider, pageContext} and
// the same SSE frames (text / tool_use / error, then [DONE]). Used by the Telemetry Live asset drawer Ask AI.
namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class ChatBotController : Controller, IAnalysisHost
    {
        private readonly ChatEngine Engine;

        public ChatBotController()
        {
            AiUserContext.Current = () => E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            AiUserContext.AdminRoleId = E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode();
            AiUserContext.UserRoleId = E7FRSAdvance.Utility.Utility.Role.User.GetHashCode();
            Engine = new ChatEngine(this);
        }

        void IAnalysisHost.WriteSse(string type, object data) { WriteSse(type, data); }

        public const string ComponentVersion = "1.5.0.0";

        [HttpGet]
        public async Task<ActionResult> Status()
        {
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
                    version = ComponentVersion,
                    allowed = priv,
                    engineChoice = priv,
                    engineBrand = ChatEngine.ReadStr("AiEngineBrand", "ENERGY7 ULTRA"),
                    srv2Enabled = ChatEngine._srv2Enabled,
                    tools = loaded.Tools.Count,
                    mcp1 = new { tools = loaded.Count1, healthy = (loaded.Error1 == null) },
                    mcp2 = new { tools = loaded.Count2, healthy = (loaded.Error2 == null) }
                }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("[ChatBot] status failed: " + ex.Message);
                return Json(new { ok = false, error = "Unavailable" }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public async Task<ActionResult> Chat()
        {
            // Role and stations are captured before the first await: LoginUser is null after it.
            bool _privileged = ChatEngine.ChatBotAllowed();
            Engine._chatPrivileged = _privileged;
            Engine._chatStations = ChatEngine.GetAssignedStations();
            string body;
            using (System.IO.StreamReader sr = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8))
                body = await sr.ReadToEndAsync().ConfigureAwait(false);

            JArray messagesRaw;
            try
            {
                JObject payload = JObject.Parse(body);
                messagesRaw = payload["messages"] as JArray;
                string _reqProv = payload["provider"] != null ? payload["provider"].ToString() : "";
                _reqProv = ChatEngine.ResolveEngineAlias(_reqProv);   // opaque id -> raw provider BEFORE gating
                Engine._provider = Engine.GateProviderForRole(_reqProv, _privileged);
                Engine._pageContext = payload["pageContext"] as JObject;   // drawer: the asset being viewed
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
                // Refuse an instruction-extraction request before the model and tools run (fail closed).
                if (ChatEngine.IsInstructionExtractionRequest(ChatEngine.LastRealUserText(messagesRaw)))
                {
                    WriteSse("text", new { text = FixedExtractionRefusal });
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

        private const string FixedExtractionRefusal =
            "I can help with railway asset analysis and monitoring. I am not able to share system "
            + "configuration details. What would you like to know about your assets or alerts?";
    }
}
