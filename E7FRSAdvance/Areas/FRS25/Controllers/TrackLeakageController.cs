using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using E7MRIWeb.Areas.FRS25.Controllers;   // DeepSeekClient (Domain project keeps the 616 namespace)

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class TrackLeakageController : Controller
    {
        private static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(ConfigurationManager.AppSettings["HistorianApiBaseUrl"]),
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Track-leakage AI summary: generated here with DeepSeek (DeepSeekApiKey/DeepSeekModel,
        // same keys PM Operation / Alert Analysis use) instead of proxying the Historian
        // service's Anthropic-backed api/ai-summary. The page posts the per-track aggregates
        // and renders {verdict, points[], recommendation}.
        private static readonly Lazy<DeepSeekClient> _deepseek = new Lazy<DeepSeekClient>(() => new DeepSeekClient(
            ConfigurationManager.AppSettings["DeepSeekApiKey"],
            ConfigurationManager.AppSettings["DeepSeekModel"] ?? "deepseek-v4-pro",
            1500,
            ConfigurationManager.AppSettings["DeepSeekBaseUrl"] ?? "https://api.deepseek.com/v1",
            false));

        private const string AiSummarySystemPrompt =
            "You are a railway signalling maintenance engineer analysing track-circuit leakage / track shorting " +
            "health for Indian Railways. You receive, for one site over a time window, a list of tracks each with " +
            "overall condition, leakage type/status/severity, peak leakage-event AUC, last-24h leakage score and the " +
            "count of major events, plus a count of tracks that failed to analyse. Write a short diagnostic summary " +
            "for a maintenance supervisor. Base every statement only on the data given; do not invent values. " +
            "Reply with ONLY a JSON object, no markdown, in exactly this shape: " +
            "{\"verdict\":\"one short line overall leakage/shorting verdict for the site\",\"points\":[\"3 to 6 concise observations, each naming the track(s) and citing the figures\"],\"recommendation\":\"one or two sentences of concrete maintenance action\"}";

        public ActionResult Index() { return View(); }

        [HttpGet]
        public async Task<ActionResult> GetSites()
        {
            var siteIds = ClsHttpContent.LoginUser.UserSites.Select(x => x.SiteId).ToList();
            try
            {
                var r = await client.GetAsync("api/sites");
                var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode)
                    return new HttpStatusCodeResult((int)r.StatusCode, j);

                // Only return sites assigned to the logged-in user
                if (siteIds.Any())
                {
                    var allowed = new HashSet<int>(siteIds);
                    var sites = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(j);
                    sites = sites.Where(s => allowed.Contains(Convert.ToInt32(s["id"]))).ToList();
                    j = JsonConvert.SerializeObject(sites);
                }

                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetTracks(int siteId)
        {
            try { var r = await client.GetAsync($"api/sites/{siteId}/tracks"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        // GET: FRS25/TrackLeakage/GetTrackPlots?siteId=1&assetId=123&start_date=...&end_date=...
        // Proxies: api/track-plots/{siteId}/{assetId}?start_date=&end_date=
        // Returns pre-built Plotly figure JSON (raw, events, zoom plots) for a single
        // track — used by the "View Details" expand to lazy-load charts on demand
        // instead of bundling all plot JSON in the initial predict/api response.
        [HttpGet]
        public async Task<ActionResult> GetTrackPlots(int siteId, int assetId, string start_date, string end_date)
        {
            try
            {
                var q = $"start_date={Uri.EscapeDataString(start_date ?? "")}&end_date={Uri.EscapeDataString(end_date ?? "")}";
                var r = await client.GetAsync($"api/track-plots/{siteId}/{assetId}?{q}");
                var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j);
                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpPost, ValidateInput(false)]
        public async Task<ActionResult> PredictApi()
        {
            try { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync("predict/api", c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpPost, ValidateInput(false)]
        public async Task<ActionResult> PredictAi()
        {
            try { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync("predict/ai", c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpPost, ValidateInput(false)]
        public async Task<ActionResult> AiSummary()
        {
            Response.TrySkipIisCustomErrors = true;
            try
            {
                if (string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["DeepSeekApiKey"]))
                    return AiSummaryError(500, "DeepSeekApiKey is not configured.");

                Request.InputStream.Position = 0;
                string body;
                using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); }

                var messages = new object[] { new { role = "user", content = "Track leakage data:\n" + body } };
                string reply = await _deepseek.Value.SendMessageAsync(messages, null, AiSummarySystemPrompt);

                string text = string.Concat(((JArray)JObject.Parse(reply)["content"] ?? new JArray())
                    .Where(b => (string)b["type"] == "text").Select(b => (string)b["text"]));
                int s = text.IndexOf('{'), e = text.LastIndexOf('}');
                if (s < 0 || e <= s) return AiSummaryError(502, "AI returned no summary.");
                var parsed = JObject.Parse(text.Substring(s, e - s + 1));

                var result = new JObject
                {
                    ["verdict"] = (string)parsed["verdict"] ?? "",
                    ["points"] = parsed["points"] as JArray ?? new JArray(),
                    ["recommendation"] = (string)parsed["recommendation"] ?? ""
                };
                return Content(result.ToString(Formatting.None), "application/json");
            }
            catch (Exception ex) { return AiSummaryError(502, ex.Message); }
        }

        private ActionResult AiSummaryError(int status, string message)
        {
            Response.StatusCode = status;
            return Content(JsonConvert.SerializeObject(new { error = message }), "application/json");
        }
    }
}