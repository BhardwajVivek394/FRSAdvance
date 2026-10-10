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

// FRS advance port of E7MRIWeb PointMachineClusteringController ("AI/ML Analysis > Point Machine Clustering"):
// thin proxy over the Historian pmcluster API. Same upstream routes as E7MRIWeb.
// (Unrelated to the older PointClusteringController.)
namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class PointMachineClusteringController : Controller
    {
        private static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "http://energy7.in:8005/"),
            Timeout = TimeSpan.FromMinutes(3)
        };

        public ActionResult Index() { return View(); }

        private async Task<ActionResult> ProxyGet(string path) { var r = await client.GetAsync(path); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
        private async Task<ActionResult> ProxyPost(string path) { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync(path, c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }

        [HttpGet]
        public async Task<ActionResult> GetSites()
        {
            var u = ClsHttpContent.LoginUser;
            var siteIds = (u != null && u.UserSites != null) ? u.UserSites.Select(x => x.SiteId).ToList() : new List<int>();
            try
            {
                var r = await client.GetAsync("api/historian/sites"); var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j);
                if (siteIds.Any())
                {
                    var allowed = new HashSet<int>(siteIds);
                    var sites = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(j);
                    sites = sites.Where(s => s.ContainsKey("SiteId") && allowed.Contains(Convert.ToInt32(s["SiteId"]))).ToList();
                    j = JsonConvert.SerializeObject(sites);
                }
                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet] public async Task<ActionResult> GetLiveValue(string siteId) { try { return await ProxyGet($"api/historian/livevalue?SiteId={Uri.EscapeDataString(siteId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetClusterCache(string siteId, string assetId) { try { return await ProxyGet($"api/pmcluster/cache?SiteId={Uri.EscapeDataString(siteId ?? "")}&AssetId={Uri.EscapeDataString(assetId ?? "")}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostClusterCache() { try { return await ProxyPost("api/pmcluster/cache"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostClusterSummary() { try { return await ProxyPost("api/pmcluster/summary"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostRefreshLive() { try { return await ProxyPost("api/pmcluster/refresh_live"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostRefreshCustomRange() { try { return await ProxyPost("api/pmcluster/refresh_custom_range"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
    }
}
