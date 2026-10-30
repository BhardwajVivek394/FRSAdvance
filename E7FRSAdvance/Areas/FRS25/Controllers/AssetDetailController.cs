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
    public class AssetDetailController : Controller
    {
        // GET: FRS25/AssetDetail

        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public AssetDetailController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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
            //ViewBag.AssetTypes = new SelectList(_assetTypeService.GetLister(new AssetTypeLister()), "Id", "Name");
            //ViewBag.RailwayAssetMake = GetRailwayAssetMake();
            return View();
        }

        public ActionResult _Details(Domain.AssetLister assetLister)
        {
            //  Domain.AssetLister assetLister = new Domain.AssetLister();
            try
            {
                assetLister.Pager.Take = -1;
                assetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                assetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(assetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetDetails"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        assetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return PartialView(assetLister);
        }

        /// <summary>
        /// Returns distinct Asset Types and Asset Makes from the Asset/GetDetails API
        /// based on the current search filters (Zone, Division, Site).
        /// Used to dynamically populate the Asset Type and Asset Make filter dropdowns.
        /// </summary>
        [HttpPost]
        public JsonResult GetAssetFilters(Domain.AssetLister assetLister)
        {
            var assetTypes = new List<object>();
            var assetMakes = new List<string>();

            try
            {
                var selectedTypes = (assetLister.SearchCriteria.AssetTypes ?? new List<string>())
                                    .Where(t => !string.IsNullOrWhiteSpace(t))
                                    .Select(t => t.Trim())
                                    .ToList();

                assetLister.SearchCriteria.AssetTypes = new List<string>();
                assetLister.Pager.Take = -1;
                assetLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                assetLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(assetLister);
                    var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Asset/GetDetails", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        assetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }

                if (assetLister?.mAssets != null && assetLister.mAssets.Count > 0)
                {
                    assetTypes = assetLister.mAssets
                        .Where(a => !string.IsNullOrEmpty(a.AssetTypeName))
                        .GroupBy(a => a.AssetTypeName.Trim())
                        .Select(g => (object)new { Name = g.Key })
                        .OrderBy(x => ((dynamic)x).Name)
                        .ToList();

                    var filtered = assetLister.mAssets.AsEnumerable();
                    if (selectedTypes.Count > 0)
                    {
                        filtered = filtered.Where(a => !string.IsNullOrEmpty(a.AssetTypeName) &&
                            selectedTypes.Contains(a.AssetTypeName.Trim(), StringComparer.OrdinalIgnoreCase));
                    }

                    assetMakes = filtered
                        .Where(a => !string.IsNullOrEmpty(a.RailwayAssetMake))
                        .Select(a => a.RailwayAssetMake.Trim())
                        .Distinct()
                        .OrderBy(m => m)
                        .ToList();
                }
            }
            catch (Exception)
            {
            }

            return Json(new { AssetTypes = assetTypes, AssetMakes = assetMakes });
        }
        public ActionResult Utilization()
        {
            var assetLister = new Domain.AssetLister();

            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);
            ViewBag.AssetTypes = new SelectList(Helper.FilterCacheHelper.GetAllAssetTypes(_assetTypeService), "Id", "Name");

            return View(assetLister);
        }

        public ActionResult _Utilization(Domain.AssetLister assetLister)
        {
            //  Domain.AssetLister assetLister = new Domain.AssetLister();
            try
            {
                assetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(assetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetUtilization"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        assetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return PartialView(assetLister);
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


        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetUtilizationAssestBy(List<int> siteIds, List<int> assetTypeIds)
        {
            var assets = new List<Asset>();
            try
            {
                Domain.AssetLister assetLister = new Domain.AssetLister();
                assetLister.SearchCriteria.IsListView = true;

                if (siteIds != null && siteIds.Count > 0)
                {
                    assetLister.SearchCriteria.SiteIds = new List<int>();
                    assetLister.SearchCriteria.SiteIds.AddRange(siteIds);
                }

                if (assetTypeIds != null && assetTypeIds.Count > 0)
                {
                    assetLister.SearchCriteria.AssetTypeIds = new List<int>();
                    assetLister.SearchCriteria.AssetTypeIds.AddRange(assetTypeIds);
                }                
                assetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(assetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetUtilization"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        assetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (assetLister != null && assetLister.mAssets != null)
                        {
                            assets = assetLister.mAssets;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(assets, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetDivisionByZoneId(DivisionLister mDivisionLister)
        {
            List<Division> mDivisions = new List<Division>();
            if (mDivisionLister.SearchCriteria.ZoneIds != null && mDivisionLister.SearchCriteria.ZoneIds.Count() > 0)
                mDivisions = _divisionService.GetDivisionList(mDivisionLister);
            else
                mDivisions = new List<Division>();

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(SiteLister mSiteLister)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                if (mSiteLister.SearchCriteria.DivisionIds != null && mSiteLister.SearchCriteria.DivisionIds.Count() > 0)
                    mSites = _siteService.GetSiteLister(mSiteLister);
                else
                    mSites = new List<Domain.Site>();
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
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

        public RailwayAsset GetRailwayAsset(int siteId)
        {
            RailwayAsset mRailwayAsset = new RailwayAsset();

            var mSite = _siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                var mZone = _zoneService.GetZoneById(mSite.ZoneId);
                var mDivision = _divisionService.Get(mSite.DivisionId);
                if (mZone != null && mZone.Id > 0 && mDivision != null && mDivision.Id > 0)
                {
                    var divisions = ExtensionMethod.BuildDivision(mZone.RepresentationCode);
                    var zones = ExtensionMethod.BuildZones();
                    if (divisions != null && divisions.Count > 0)
                    {
                        var divi = divisions.Where(x => x.Id == mDivision.RepresentationCode).FirstOrDefault();
                        var zon = zones.Where(x => x.Id == mZone.RepresentationCode).FirstOrDefault();
                        if (divi != null && zon != null)
                        {
                            try
                            {
                                var client = new HttpClient();

                                var request = new HttpRequestMessage(
                                    HttpMethod.Post,
                                    $"{ConfigurationManager.AppSettings.Get("RailwaySMMSAPIUrl")}/{zon.Code}/{divi.Code}/{mSite.StationCode}"
                                );

                                // Add cookie
                                request.Headers.Add("Cookie", "TS015f053d=01ee28b4440dd833dc70827baa28a93f79d0c659f6d69b28645118a5d2938e8a16395315ce2ddf4f9024e8f20649e2c939e0ce84b2");

                                // If API requires body, uncomment this line
                                // request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                                var response = client.SendAsync(request).GetAwaiter().GetResult();
                                response.EnsureSuccessStatusCode();

                                string result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                if (response.StatusCode == HttpStatusCode.OK)
                                    mRailwayAsset = JsonConvert.DeserializeObject<RailwayAsset>(result);


                            }
                            catch (Exception ex)
                            {
                                throw ex;
                            }
                        }
                    }
                }
            }

            return mRailwayAsset;
        }

        private List<string> GetRailwayAssetMake()
        {
            List<string> causeCodes = new List<string>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("DataloggerAssetMapping/GetRailwayAssetMake")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        causeCodes = JsonConvert.DeserializeObject<List<string>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return causeCodes;
        }


    }
}
