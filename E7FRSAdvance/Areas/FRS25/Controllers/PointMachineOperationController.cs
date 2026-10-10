using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;

// FRS advance port of E7MRIWeb PointMachineOperationController ("AI/ML Analysis > Point Machine Operation"):
// thin proxy over the Historian point-machine API (models 1-4). Same upstream routes as E7MRIWeb.
namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class PointMachineOperationController : Controller
    {
        private static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "http://energy7.in:8005/"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        public ActionResult Index()
        {
            var u = ClsHttpContent.LoginUser;
            ViewBag.IsSiteKeeping = u != null && u.IsSiteKeeping;
            return View();
        }

        private async Task<ActionResult> ProxyGet(string path) { var r = await client.GetAsync(path); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
        private async Task<ActionResult> ProxyPost(string path) { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync(path, c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
        private ActionResult FilterSites(string json)
        {
            var u = ClsHttpContent.LoginUser;
            var siteIds = (u != null && u.UserSites != null) ? u.UserSites.Select(x => x.SiteId).ToList() : new List<int>();
            if (siteIds.Any())
            {
                var allowed = new HashSet<int>(siteIds);
                var sites = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                sites = sites.Where(s => s.ContainsKey("SiteId") && allowed.Contains(Convert.ToInt32(s["SiteId"]))).ToList();
                json = JsonConvert.SerializeObject(sites);
            }
            return Content(json, "application/json");
        }

        // Model 1
        [HttpGet] public async Task<ActionResult> GetSites() { try { var r = await client.GetAsync("api/historian/sites"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return FilterSites(j); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetLiveValue(string siteId, string assetId) { try { string q = !string.IsNullOrEmpty(siteId) ? $"SiteId={Uri.EscapeDataString(siteId)}" : $"AssetId={Uri.EscapeDataString(assetId ?? "")}"; return await ProxyGet($"api/historian/livevalue?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPmData(string siteId, string assetId, string startDate, string endDate) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}&StartDate={Uri.EscapeDataString(startDate ?? "")}&EndDate={Uri.EscapeDataString(endDate ?? "")}"; return await ProxyGet($"api/pmdata?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetAssetWaveType(string siteId) { try { return await ProxyGet($"api/asset-wavetype?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetModelJson(string waveType) { if (string.IsNullOrWhiteSpace(waveType) || (!waveType.Equals("TWS", StringComparison.OrdinalIgnoreCase) && !waveType.Equals("IRS", StringComparison.OrdinalIgnoreCase))) return new HttpStatusCodeResult(400); try { return await ProxyGet($"api/model/{waveType}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetMonthlyCache(string siteId, string assetId) { try { return await ProxyGet($"api/monthly-health-cache?SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostMonthlyCache() { try { return await ProxyPost("api/monthly-health-cache"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostAiSummary() { try { return await ProxyPost("api/pm-ai-summary"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }

        // Model 2
        [HttpGet] public async Task<ActionResult> GetPm2Sites() { try { var r = await client.GetAsync("api/pm2/sites"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return FilterSites(j); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm2Assets(string siteId) { try { return await ProxyGet($"api/pm2/assets?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm2Analyze(string siteId, string assetId, int windowDays = 30, string assetName = "", string siteName = "", bool forceLive = false) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}&WindowDays={windowDays}&AssetName={Uri.EscapeDataString(assetName ?? "")}&SiteName={Uri.EscapeDataString(siteName ?? "")}" + (forceLive ? "&ForceLive=true" : ""); return await ProxyGet($"api/pm2/analyze?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }

        // Model 3
        [HttpGet] public async Task<ActionResult> GetPm3Sites() { try { var r = await client.GetAsync("api/historian/sites3"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return FilterSites(j); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm3LiveValue(string siteId) { try { return await ProxyGet($"api/historian/livevalue3?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm3AssetWaveType(string siteId) { try { return await ProxyGet($"api/asset-wavetype3?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm3PmData(string siteId, string assetId, string startDate, string endDate) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}&StartDate={Uri.EscapeDataString(startDate ?? "")}&EndDate={Uri.EscapeDataString(endDate ?? "")}"; return await ProxyGet($"api/pmdata3?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm3Predict() { try { return await ProxyPost("api/predict3"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm3PredictFeedback() { try { return await ProxyPost("api/predict3/feedback"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm3MonthlyCache(string siteId, string assetId) { try { return await ProxyGet($"api/monthly-health-cache3?SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm3MonthlyCache() { try { return await ProxyPost("api/monthly-health-cache3"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm3AiSummary() { try { return await ProxyPost("api/pm-ai-summary3"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm3PdfReport(string siteId, string assetId, string assetName) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}&AssetName={Uri.EscapeDataString(assetName ?? "")}"; return await ProxyGet($"pm_operation_combined/pdf?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }

        // Model 4
        [HttpGet] public async Task<ActionResult> GetPm4Sites() { try { var r = await client.GetAsync("api/pm4/sites"); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return FilterSites(j); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm4Assets(string siteId) { try { return await ProxyGet($"api/pm4/assets?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm4Analyze(string siteId, string assetId, int windowDays = 30, string assetName = "", string siteName = "", bool forceLive = false) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}&WindowDays={windowDays}&AssetName={Uri.EscapeDataString(assetName ?? "")}&SiteName={Uri.EscapeDataString(siteName ?? "")}" + (forceLive ? "&ForceLive=true" : ""); return await ProxyGet($"api/pm4/analyze?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetPm4Watchlist(string siteId, bool forceLive = false) { try { var q = $"SiteId={Uri.EscapeDataString(siteId ?? "")}" + (forceLive ? "&ForceLive=true" : ""); return await ProxyGet($"api/pm4/watchlist?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm4Feedback() { try { return await ProxyPost("api/pm4/feedback"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostPm4FeedbackClear() { try { return await ProxyPost("api/pm4/feedback/clear"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
    }
}
