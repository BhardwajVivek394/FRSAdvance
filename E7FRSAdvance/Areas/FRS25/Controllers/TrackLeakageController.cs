using E7FRSAdvance.Utility;
using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

// FRS advance port of E7MRIWeb TrackLeakageController (track shorting / leakage):
// proxies the Historian leakage API so the Telemetry Live asset drawer can show the
// "Shorting (leakage) report" for a track. Same upstream routes as E7MRIWeb.
namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]

    public class TrackLeakageController : Controller
    {
        private static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "http://energy7.in:8005/"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        [HttpGet]
        public async Task<ActionResult> GetTracks(int siteId)
        {
            if (!SiteAllowed(siteId)) return new HttpStatusCodeResult(403, "Site not assigned to user");
            try { var r = await client.GetAsync($"api/sites/{siteId}/tracks"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        // GET: FRS25/TrackLeakage/GetTrackPlots?siteId=1&assetId=123&start_date=...&end_date=...&track_name=...
        // Proxies: api/track-plots/{siteId}/{assetId} -> Plotly figure JSON (raw, events, zoom plots) for one track.
        [HttpGet]
        public async Task<ActionResult> GetTrackPlots(int siteId, int assetId, string start_date, string end_date, string track_name = null)
        {
            if (!SiteAllowed(siteId)) return new HttpStatusCodeResult(403, "Site not assigned to user");
            try
            {
                var q = $"start_date={Uri.EscapeDataString(start_date ?? "")}&end_date={Uri.EscapeDataString(end_date ?? "")}";
                if (!string.IsNullOrEmpty(track_name)) q += "&track_name=" + Uri.EscapeDataString(track_name);
                var r = await client.GetAsync($"api/track-plots/{siteId}/{assetId}?{q}");
                var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j);
                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        // POST body: { site_id, track_ids:[..], start_date, end_date, use_llm, force_live, track_names, include_plots }
        [HttpPost, ValidateInput(false)]
        public Task<ActionResult> PredictApi() { return Forward("predict/api"); }

        // POST body: { site_id, track_id, start_date, end_date } -> AI reasoning for one track
        [HttpPost, ValidateInput(false)]
        public Task<ActionResult> PredictAi() { return Forward("predict/ai"); }

        [HttpPost, ValidateInput(false)]
        public Task<ActionResult> AiSummary() { return Forward("api/ai-summary"); }

        private async Task<ActionResult> Forward(string path)
        {
            try
            {
                Request.InputStream.Position = 0;
                string body;
                using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); }
                var c = new StringContent(body, Encoding.UTF8, "application/json");
                var r = await client.PostAsync(path, c);
                var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j);
                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        private static bool SiteAllowed(int siteId)
        {
            var u = ClsHttpContent.LoginUser;
            if (u == null || u.UserSites == null || !u.UserSites.Any()) return true;
            return u.UserSites.Any(x => x.SiteId == siteId);
        }
    }
}
