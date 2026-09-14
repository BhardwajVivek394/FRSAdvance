using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class RDPMSHealthLiveController : Controller
    {
        // GET: FRS25/RDPMSHealthLive
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;

        public RDPMSHealthLiveController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
        }

        public ActionResult Index()
        {
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);
            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetLister(new AssetTypeLister()), "Id", "Name");
            return View();
        }

        public ActionResult _List(RDPMSHealthLiveLister mAssetLister)
        {
            try
            {
                mAssetLister.Pager.Take = -1;
                mAssetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mAssetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetRDPMSHealthLiveDetails"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<RDPMSHealthLiveLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssetInfos != null && mAssetLister.mAssetInfos.Count > 0)
                        {
                            foreach (var assetInfo in mAssetLister.mAssetInfos)
                            {
                                if (assetInfo.AttributeAliasName.IsNotNullOrEmpty())
                                {
                                    assetInfo.AttributeName = assetInfo.AttributeAliasName;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
            }

            return PartialView(mAssetLister);
        }

        private List<Domain.AssetType> GetFRSAssetType()
        {
            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

        public ActionResult Detail()
        {
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);
            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetLister(new AssetTypeLister()), "Id", "Name");
            return View();
        }

        public ActionResult _Detail(RDPMSHealthLiveLister mAssetLister)
        {
            try
            {
                mAssetLister.Pager.Take = -1;
                mAssetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mAssetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                if (mAssetLister.SearchCriteria.FromDate != null && mAssetLister.SearchCriteria.FromDate != DateTime.MinValue)
                {
                    DateTime baseDate = mAssetLister.SearchCriteria.FromDate.Date;

                    if (mAssetLister.SearchCriteria.FromTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mAssetLister.SearchCriteria.FromTime, out TimeSpan time))
                            mAssetLister.SearchCriteria.FromDate = baseDate.Add(time);
                        else
                            mAssetLister.SearchCriteria.FromDate = baseDate;
                    }
                    else
                        mAssetLister.SearchCriteria.FromDate = baseDate;
                }
                else
                    mAssetLister.SearchCriteria.FromDate = DateTime.Now;

                if (mAssetLister.SearchCriteria.ToDate != null && mAssetLister.SearchCriteria.ToDate != DateTime.MinValue)
                {
                    DateTime baseDate = mAssetLister.SearchCriteria.ToDate.Date;

                    if (mAssetLister.SearchCriteria.ToTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mAssetLister.SearchCriteria.ToTime, out TimeSpan time))
                            mAssetLister.SearchCriteria.ToDate = baseDate.Add(time);
                        else
                            mAssetLister.SearchCriteria.ToDate = baseDate;
                    }
                    else
                        mAssetLister.SearchCriteria.ToDate = baseDate;
                }

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetRDPMSHealthSummary"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<RDPMSHealthLiveLister>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
            }

            return PartialView(mAssetLister);
        }

        public JsonResult GetAssetTypeBySiteIds(List<int> siteIds)
        {
            var mAssetTypes = new List<Domain.AssetType>();
            try
            {
                var mAssetTypeLister = new AssetTypeLister();
                mAssetTypeLister.SiteIds = siteIds;
                mAssetTypes = _assetTypeService.GetLister(mAssetTypeLister);
            }
            catch (Exception)
            {
                mAssetTypes = Helper.FilterCacheHelper.GetFRSAssetTypes();
            }
            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        // ---------------------------------------------------------------------
        // Modem / A10 (IoT) live + history proxies.
        //
        // Live telemetry values (bit flags, timestamps) primarily flow to the
        // client over the WebSocket subscribed via rdpms-health-live-socket.js.
        // These HTTP endpoints exist for:
        //   1. Bootstrapping the initial page state before the socket connects.
        //   2. Fetching graph history (which is not on the socket feed).
        // ---------------------------------------------------------------------

        public ActionResult GetModemLive(int siteId)
        {
            return ProxyModemLive(siteId, "Modem");
        }

        /// <summary>
        /// A10 (IoT) live status.
        /// Calls: {ProxyBaseUrl}/api/ModemLive/A10?siteId={siteId}&amp;DataSource=A10
        /// </summary>
        public ActionResult GetA10Live(int siteId)
        {
            return ProxyModemLive(siteId, "A10");
        }

        /// <summary>
        /// A10 (IoT) history for a site + cluster within a date range.
        /// Calls: {ProxyBaseUrl}/api/ModemHistory/A10?siteId=...&amp;clusterId=...&amp;StartDate=ddMMyyyy_HHmmss&amp;EndDate=ddMMyyyy_HHmmss
        /// </summary>
        public ActionResult GetA10History(int siteId, string clusterId, string startDate, string endDate)
        {
            return ProxyModemHistory(siteId, clusterId, startDate, endDate, "A10");
        }

        private ActionResult ProxyModemHistory(int siteId, string clusterId, string startDate, string endDate, string dataSource)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                    return Json(new { error = "ProxyBaseUrl not configured in Web.config" }, JsonRequestBehavior.AllowGet);

                if (siteId <= 0)
                    return Json(new { error = "Invalid siteId" }, JsonRequestBehavior.AllowGet);

                if (string.IsNullOrWhiteSpace(clusterId))
                    return Json(new { error = "Invalid clusterId" }, JsonRequestBehavior.AllowGet);

                if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
                    return Json(new { error = "StartDate/EndDate required" }, JsonRequestBehavior.AllowGet);

                string normalizedDataSource = string.Equals(dataSource, "A10", StringComparison.OrdinalIgnoreCase) ? "A10" : "Modem";

                string url = string.Format(
                    "{0}/api/ModemHistory/{1}?siteId={2}&clusterId={3}&StartDate={4}&EndDate={5}",
                    baseUrl.TrimEnd('/'),
                    normalizedDataSource,
                    siteId,
                    Uri.EscapeDataString(clusterId),
                    Uri.EscapeDataString(startDate),
                    Uri.EscapeDataString(endDate));

                // NOTE: uses the shared HttpClientFactory (post-perf-fix) instead of
                // `new HttpClient()` so the connection pool + timeout are consistent.
                using (var hcf = new HttpClientFactory(baseUrl: baseUrl))
                {
                    string relative = url.Substring(baseUrl.TrimEnd('/').Length + 1);
                    HttpResponseMessage response = hcf.client.GetAsync(relative).Result;
                    string responseContent = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                        return Content(responseContent, "application/json");

                    // Preserve the empty-array convention on failure so the frontend
                    // does not need bespoke error branching.
                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Shared proxy for Modem and A10 live status.
        ///   Modem: /api/ModemLive/Modem?siteId={siteId}&amp;DataSource=Modem
        ///   A10:   /api/ModemLive/A10?siteId={siteId}&amp;DataSource=A10
        /// </summary>
        private ActionResult ProxyModemLive(int siteId, string dataSource)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                    return Json(new { error = "ProxyBaseUrl not configured in Web.config" }, JsonRequestBehavior.AllowGet);

                if (siteId <= 0)
                    return Json(new { error = "Invalid siteId" }, JsonRequestBehavior.AllowGet);

                string normalizedDataSource = string.Equals(dataSource, "A10", StringComparison.OrdinalIgnoreCase) ? "A10" : "Modem";

                string relative = string.Format(
                    "api/ModemLive/{0}?siteId={1}&DataSource={2}",
                    normalizedDataSource,
                    siteId,
                    Uri.EscapeDataString(normalizedDataSource));

                using (var hcf = new HttpClientFactory(baseUrl: baseUrl))
                {
                    HttpResponseMessage response = hcf.client.GetAsync(relative).Result;
                    string responseContent = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                        return Content(responseContent, "application/json");

                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Proxies GET /api/CardLine/SiteId/{siteId}. Returns modem/channel config,
        /// used to derive total device counts per media type.
        /// </summary>
        public ActionResult GetCardLineData(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}", siteId)).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        return Content(json, "application/json");
                    }
                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Modem history for graph plotting.
        /// API: Product/GetModemHistory/ClusterId/{clusterId}/FromDate/{fromDate}/ToDate/{toDate}
        /// </summary>
        [HttpGet]
        public ActionResult GetModemHistoryForGraph(int clusterId, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiEndpoint = string.Format("Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}",
                        clusterId, fromDate, toDate);

                    var response = hcf.client.GetAsync(apiEndpoint).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }

                    return Json(new { Success = false, Message = "Failed to fetch data from API" }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Proxies GET api/sensorhealth/live/site/{siteId}.
        /// Sensor faults are served over REST (per requirement: WebSocket for
        /// IoT/Network/Gateway, API for sensors).
        ///
        ///   ResetTime == null                          → alert ACTIVE   (Faulty)
        ///   ResetTime != null AND ResetTime > SetTime  → alert CLEARED  (Healthy / resolved)
        ///   Otherwise                                  → treat as Faulty
        /// </summary>
        [HttpGet]
        public ActionResult GetSensorHealthLive(int siteId)
        {
            if (siteId <= 0)
                return Json(new { error = "Invalid siteId" }, JsonRequestBehavior.AllowGet);

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("sensorhealth/live/site/{0}", siteId)).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        return Content(json, "application/json");
                    }

                    // Return an empty array on any non-200 so the frontend's
                    // $.isArray(data) guard keeps working without special casing.
                    return Json(new List<object>(), JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
    }
}
