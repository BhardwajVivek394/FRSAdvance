using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class ELDAIAnalysisController : Controller
    {
        private static readonly HttpClient client = new HttpClient
        {
            BaseAddress = new Uri(ConfigurationManager.AppSettings["HistorianApiBaseUrl"]),
            Timeout = TimeSpan.FromMinutes(5)
        };

        public ActionResult Index() { return View(); }

        private async Task<ActionResult> ProxyGet(string path) { var r = await client.GetAsync(path); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }
        private async Task<ActionResult> ProxyPost(string path) { Request.InputStream.Position = 0; string body; using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); } var c = new StringContent(body, Encoding.UTF8, "application/json"); var r = await client.PostAsync(path, c); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); }

        // ==================== ELD API ====================
        [HttpGet] public async Task<ActionResult> GetList() { try { return await ProxyGet("api/eld/list"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }

        [HttpGet]
        public async Task<ActionResult> GetSites()
        {
            try
            {
                var r = await client.GetAsync("api/eld/sites");
                var j = await r.Content.ReadAsStringAsync();
                if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j);

                var siteIds = ClsHttpContent.LoginUser.UserSites.Select(x => x.SiteId).ToList();
                if (siteIds.Any())
                {
                    var allowed = new HashSet<int>(siteIds);
                    var wrapper = JsonConvert.DeserializeObject<Dictionary<string, object>>(j);
                    var sitesArray = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(wrapper["sites"].ToString());
                    sitesArray = sitesArray.Where(s => allowed.Contains(Convert.ToInt32(s["id"]))).ToList();
                    wrapper["sites"] = sitesArray;
                    j = JsonConvert.SerializeObject(wrapper);
                }

                return Content(j, "application/json");
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }
        [HttpGet] public async Task<ActionResult> GetState() { try { return await ProxyGet("api/eld/state?events=40"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetDetail(string id, int? pre_sec, int? post_sec) { try { var path = $"api/eld/detail/{Uri.EscapeDataString(id ?? "")}"; if (pre_sec.HasValue || post_sec.HasValue) path += $"?pre_sec={pre_sec ?? 5}&post_sec={post_sec ?? 30}"; return await ProxyGet(path); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostAnalyze(string id, int window_min = 15, int pre_sec = 5, int post_sec = 30, string pivot = "") { try { var path = $"api/eld/analyze/{Uri.EscapeDataString(id ?? "")}?window_min={window_min}&pre_sec={pre_sec}&post_sec={post_sec}"; if (!string.IsNullOrEmpty(pivot)) path += $"&pivot={Uri.EscapeDataString(pivot)}"; var r = await client.PostAsync(path, null); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetLogs(int hours = 168) { try { return await ProxyGet($"api/eld/logs?hours={hours}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetHistoryAnalysis(string eld_id, int pre_sec = 5, int post_sec = 30, string start_date = "", string end_date = "", bool force_refresh = false) { try { var q = $"eld_id={Uri.EscapeDataString(eld_id ?? "")}&pre_sec={pre_sec}&post_sec={post_sec}"; if (!string.IsNullOrEmpty(start_date)) q += $"&start_date={Uri.EscapeDataString(start_date)}"; if (!string.IsNullOrEmpty(end_date)) q += $"&end_date={Uri.EscapeDataString(end_date)}"; if (force_refresh) q += "&force_refresh=true"; return await ProxyGet($"api/eld/history-analysis?{q}"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetMappingOverview() { try { return await ProxyGet("api/eld/mapping-overview"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpGet] public async Task<ActionResult> GetHealth() { try { return await ProxyGet("api/eld/health"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostAiSummary() { try { return await ProxyPost("api/eld/ai-summary"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }
        [HttpPost, ValidateInput(false)] public async Task<ActionResult> PostLogsRebuild() { try { var r = await client.PostAsync("api/eld/logs/rebuild", null); var j = await r.Content.ReadAsStringAsync(); if (!r.IsSuccessStatusCode) return new HttpStatusCodeResult((int)r.StatusCode, j); return Content(j, "application/json"); } catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); } }

        // Add this helper method
        private ActionResult FilterSites(string json, string idField = "SiteId")
        {
            var siteIds = ClsHttpContent.LoginUser.UserSites.Select(x => x.SiteId).ToList();
            if (siteIds.Any())
            {
                var allowed = new HashSet<int>(siteIds);
                var sites = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                sites = sites.Where(s => s.ContainsKey(idField) && allowed.Contains(Convert.ToInt32(s[idField]))).ToList();
                json = JsonConvert.SerializeObject(sites);
            }
            return Content(json, "application/json");
        }

        // ==================== Catch-all proxy (for sip.js API calls) ====================
        [HttpGet, HttpPost, ValidateInput(false)]
        public async Task<ActionResult> Proxy(string p)
        {
            if (string.IsNullOrEmpty(p)) return new HttpStatusCodeResult(400);
            try
            {
                var qsParts = new System.Collections.Generic.List<string>();
                foreach (string key in Request.QueryString)
                    if (key != null && !key.Equals("p", StringComparison.OrdinalIgnoreCase))
                        qsParts.Add($"{key}={Request.QueryString[key] ?? ""}");
                var qs = qsParts.Count > 0 ? "?" + string.Join("&", qsParts) : "";
                var fullPath = p + qs;

                HttpResponseMessage resp;
                if (Request.HttpMethod == "POST")
                {
                    Request.InputStream.Position = 0; string body;
                    using (var rd = new System.IO.StreamReader(Request.InputStream, Encoding.UTF8)) { body = await rd.ReadToEndAsync(); }
                    resp = await client.PostAsync(fullPath, string.IsNullOrEmpty(body) ? null : new StringContent(body, Encoding.UTF8, "application/json"));
                }
                else { resp = await client.GetAsync(fullPath); }

                var result = await resp.Content.ReadAsStringAsync();
                var ct = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                if (!resp.IsSuccessStatusCode) return new HttpStatusCodeResult((int)resp.StatusCode, result);
                return Content(result, ct);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        // ==================== SIP View API ====================
        [HttpGet]
        public async Task<ActionResult> GetSipSites()
        {
            try { return await ProxyGet("api/eld/sip/sites"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipList(string site_id)
        {
            try
            {
                var path = "api/eld/list";
                if (!string.IsNullOrEmpty(site_id)) path += $"?site_id={Uri.EscapeDataString(site_id)}";
                return await ProxyGet(path);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipLogs(string eld_id, string site_id, string start_date, string end_date)
        {
            try
            {
                var qs = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(eld_id)) qs.Add($"eld_id={Uri.EscapeDataString(eld_id)}");
                if (!string.IsNullOrEmpty(site_id)) qs.Add($"site_id={Uri.EscapeDataString(site_id)}");
                if (!string.IsNullOrEmpty(start_date)) qs.Add($"start_date={Uri.EscapeDataString(start_date)}");
                if (!string.IsNullOrEmpty(end_date)) qs.Add($"end_date={Uri.EscapeDataString(end_date)}");
                var path = "api/eld/logs" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
                return await ProxyGet(path);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipLayout(string site)
        {
            try { return await ProxyGet($"api/eld/sip/layout/{Uri.EscapeDataString(site ?? "")}"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpPost, ValidateInput(false)]
        public async Task<ActionResult> PostSipLayout(string site)
        {
            try { return await ProxyPost($"api/eld/sip/layout/{Uri.EscapeDataString(site ?? "")}"); }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipChange(string eld_id, string at, string before_min, string after_min, string full)
        {
            try
            {
                var qs = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(at)) qs.Add($"at={Uri.EscapeDataString(at)}");
                if (!string.IsNullOrEmpty(before_min)) qs.Add($"before_min={Uri.EscapeDataString(before_min)}");
                if (!string.IsNullOrEmpty(after_min)) qs.Add($"after_min={Uri.EscapeDataString(after_min)}");
                if (!string.IsNullOrEmpty(full)) qs.Add($"full={Uri.EscapeDataString(full)}");
                var path = $"api/eld/change/{Uri.EscapeDataString(eld_id ?? "")}" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
                return await ProxyGet(path);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipTrackCircuit(string eld_id, string asset_id, string at, string before_min, string after_min)
        {
            try
            {
                var qs = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(asset_id)) qs.Add($"asset_id={Uri.EscapeDataString(asset_id)}");
                if (!string.IsNullOrEmpty(at)) qs.Add($"at={Uri.EscapeDataString(at)}");
                if (!string.IsNullOrEmpty(before_min)) qs.Add($"before_min={Uri.EscapeDataString(before_min)}");
                if (!string.IsNullOrEmpty(after_min)) qs.Add($"after_min={Uri.EscapeDataString(after_min)}");
                var path = $"api/eld/sip/track-circuit/{Uri.EscapeDataString(eld_id ?? "")}" + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
                return await ProxyGet(path);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }

        [HttpGet]
        public async Task<ActionResult> GetSipPmOperations(string site, string asset_id, string at)
        {
            try
            {
                var path = $"api/eld/sip/pm-operations/{Uri.EscapeDataString(site ?? "")}/{Uri.EscapeDataString(asset_id ?? "")}";
                if (!string.IsNullOrEmpty(at)) path += $"?at={Uri.EscapeDataString(at)}";
                return await ProxyGet(path);
            }
            catch (Exception ex) { return new HttpStatusCodeResult(500, ex.Message); }
        }
    }
}