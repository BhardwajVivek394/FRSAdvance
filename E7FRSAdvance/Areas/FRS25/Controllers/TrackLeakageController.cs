using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

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
            try { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync("api/ai-summary", c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }
    }
}