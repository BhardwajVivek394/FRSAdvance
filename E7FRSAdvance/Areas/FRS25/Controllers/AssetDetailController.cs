using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            ViewBag.RailwayAssetMake = GetRailwayAssetMake();
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

            return View(assetLister);
        }

        public ActionResult Utilization()
        {
            var assetLister = new Domain.AssetLister();

            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");


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

            return View(assetLister);
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