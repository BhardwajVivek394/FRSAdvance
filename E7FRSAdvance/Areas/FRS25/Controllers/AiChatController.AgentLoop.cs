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

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // AiChatController -- agentic loop, SSE writer, history trimming, token estimate.
    // Split out of AiChatController.cs in 1.0.161.0 (move-only; see CHANGELOG.md).
    public partial class AiChatController : Controller
    {

        private void WriteSse(string type, object data)
        {
            try
            {
                JObject payload = JObject.FromObject(data);
                payload["type"] = type;
                if (Engine.Sess._jsonCapture != null)
                {
                    // text is captured losslessly at the fullText site in the loop;
                    // here only record the first error and swallow everything else.
                    if (type == "error" && payload["message"] != null && Engine.Sess._jsonCaptureError == null)
                    {
                        Engine.Sess._jsonCaptureError = payload["message"].ToString();
                    }
                    return; // never touch Response in capture mode
                }
                Response.Write("data: " + payload.ToString(Formatting.None) + "\n\n");
                Response.Flush();
            }
            catch { }
        }
    }
}