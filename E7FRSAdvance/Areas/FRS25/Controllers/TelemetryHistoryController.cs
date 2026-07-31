using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class TelemetryHistoryController : Controller
    {
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;

        public TelemetryHistoryController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
        }

        // GET: FRS25/TelemetryHistory
        public ActionResult Index()
        {
            // Same shared filter cache used by Alert Live.
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag,_siteService,_zoneService,_divisionService,includeAssetType: false);
            // FRS asset types are enum-based, so retain existing behaviour.
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(),"Id","Name");
            return View();
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            return Json(_divisionService.GetByZoneId(zoneId), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        // Server-side proxy to call History API (avoids browser CORS issues)
        // Server-side proxy to fetch all Asset Attribute definitions (Id → Title mapping)
        public ActionResult GetAssetAttributes()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetAttribute/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        // Server-side proxy to call History API (avoids browser CORS issues)
        public ActionResult GetHistoryData(int assetId, string startDate, string endDate)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format("{0}/api/HistoryValue?AssetId={1}&StartDate={2}&EndDate={3}",
                    baseUrl, assetId, startDate, endDate);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    var response = client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult GetUserAssetInfoDatalogger(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetInfoDatalogger/GetUserAssetInfoDatalogger/SiteId/" + siteId).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        public async Task<ActionResult> GetUserAssetInfo(int siteId)
        {
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync("AssetInfo/GetUserAssetInfo/SiteId/" + siteId).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    return Content(json, "application/json");
                }
                else
                {
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
        }
        #region Private Method

        private List<Domain.AssetType> GetFRSAssetType()
        {
            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

        #endregion
    }
}
