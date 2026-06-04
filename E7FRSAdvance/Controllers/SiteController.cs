using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using System.Web.Razor.Parser.SyntaxTree;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class SiteController : Controller
    {
        // GET: Site
        private static IMqttClient _client;
        private static IMqttClientOptions _options;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly ISiteService siteService;
        private readonly ISiteKeepingService keepingService;
        private readonly IGraphService graphService;
        private readonly IAssetService assetService;
        private readonly IFRSAlertService fRSAlertService;
        private readonly IUserService userService;
        public SiteController(IAssetAttributeService assetAttributeService, ISiteService siteService, ISiteKeepingService keepingService, IGraphService graphService, IAssetService assetService, IFRSAlertService fRSAlertService, IUserService userService)
        {
            this.assetAttributeService = assetAttributeService;
            this.siteService = siteService;
            this.keepingService = keepingService;
            this.graphService = graphService;
            this.assetService = assetService;
            this.fRSAlertService = fRSAlertService;
            this.userService = userService;
        }
        public ActionResult Index()
        {
            GetAllZones();
            GetAllDivisions();
            return View();
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            //mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSite"), str).Result;
                    //var response = hcf.client.PostAsync(String.Format("Site/GetAllSiteLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            GetAllZones();
            GetAllDivisions();
            return PartialView("_SiteListPartial", mSiteLister);
        }

        [HttpPost]
        public ActionResult SaveSite(Site mSite)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mSite.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "Site has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving Site!";
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error.";
            }
            GetAllZones();
            return PartialView("_AddSitePartial", new Site());

        }

        public ActionResult GetSiteById(int id)
        {
            Site mSite = new Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Site>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            GetAllZones();
            return PartialView("~/Views/Site/_AddSitePartial.cshtml", mSite);
        }

        public ActionResult DeleteSiteById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/DeleteSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public void GetAllZones()
        {
            List<Zone> mZones = new List<Zone>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetAllZones")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mZones = JsonConvert.DeserializeObject<List<Zone>>(jsonString);
                        mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            ViewBag.Zones = new SelectList(mZones.ToList(), "Id", "Name");
        }

        public void GetAllPLCLog()
        {
            var enumData = from Utility.Utility.PLCLog e in Enum.GetValues(typeof(Utility.Utility.PLCLog))
                           select new
                           {
                               Id = (int)e,
                               Name = e.ToString().Replace("_", " ")
                           };

            ViewBag.PLCLogEnumList = new SelectList(enumData, "Id", "Name");
        }

        public void GetAllAdjacentType()
        {
            var enumData = (from Utility.Utility.AdjacentType e in Enum.GetValues(typeof(Utility.Utility.AdjacentType))
                            select new
                            {
                                Id = (int)e,
                                Name = e.ToString().Replace("_", " ")
                            }).ToList();

            ViewBag.AdjacentTypeList = enumData;
        }

        public List<Division> GetAllDivisions()
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisions")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        mDivisions.Insert(0, new Division { Id = 0, Name = "Select Divison" });
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            ViewBag.Divisions = new SelectList(mDivisions.ToList(), "Id", "Name");

            return mDivisions;
        }

        public List<Division> GetDivisions()
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisions")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return mDivisions;
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            if (zoneId > 0)
            {
                string result;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisionsByZoneId/{0}", zoneId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        }
                        else
                        {
                            result = "Internal server error.";
                        }
                    }

                }
                catch (Exception ex)
                {
                    result = ex.Message.ToString();
                }
            }
            else
            {
                mDivisions = GetDivisions();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssestTypes()
        {
            List<Domain.AssetType> mAssetType = new List<Domain.AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAllAssestType")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(mAssetType, JsonRequestBehavior.AllowGet);
        }

        public ActionResult AddAsset(int assetTypeId, int siteId)
        {
            Asset mAsset = new Asset();
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeInfo/{0}/{1}", assetTypeId, siteId)).Result;
                //var response = hcf.client.GetAsync(String.Format("Site/GetSiteChannels/{0}", siteId)).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                    mAsset.AssetTypeId = assetTypeId;
                    mAsset.SiteId = siteId;

                    ViewBag.AssetAttributes = assetAttributeService.GetAllAssetAttributeBy(mAsset.AssetTypeId);
                }
                else
                {
                    ViewBag.Type = "Error";
                    ViewBag.Message = "Internal server error!";
                }
            }
            GetAllAdjacentType();
            return PartialView("~/Views/Site/_AddAssetPartial.cshtml", mAsset);
        }

        public ActionResult SaveAsset(Asset mAsset)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/SaveAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            mAPIResponse.Message = "Asset has been saved.";
                            mAPIResponse.IsSuccess = true;
                        }
                        else
                        {
                            mAPIResponse.IsSuccess = false;
                            mAPIResponse.Message = "Error occured while saving asset!";
                        }
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                mAPIResponse.IsSuccess = false;
                mAPIResponse.Message = "Internal server error.";
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssetById(int id)
        {
            Asset mAsset = new Asset();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAssetById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        if (mAsset != null && mAsset.Id > 0)
                            ViewBag.AssetAttributes = assetAttributeService.GetAllAssetAttributeBy(mAsset.AssetTypeId);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            finally
            {
                GetAllAdjacentType();
            }
            return PartialView("~/Views/Site/_AddAssetPartial.cshtml", mAsset);
        }

        public ActionResult DeleteAssetById(int id, int siteId)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/DeleteAssetById/{0}/{1}", id, siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssetById(int siteId, string search = null)
        {
            List<Asset> mAsset = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAll/{0}/{1}", siteId, search)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SiteAssetData(int siteId = 0)
        {
            Site mSite = new Site();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Site>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            return View(mSite);
        }

        public ActionResult _GetAssetType(int siteId)
        {
            List<Domain.AssetType> mAssetType = new List<Domain.AssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetType);
        }

        public ActionResult _GetAssetDetails(AssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.StartDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();

                    if (string.IsNullOrEmpty(mAssetLister.SearchCriteria.EndDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();


                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            mAssetLister.mAssets = mAssetLister.mAssets.OrderBy(x => x.Sequence).ToList();
                        }
                        mAssetLister.Pager.PageSize = -1;

                        if (mAssetLister != null && mAssetLister.SearchCriteria != null)
                        {
                            var mAssetAttributes = assetAttributeService.GetByIsDerived(mAssetLister.SearchCriteria.AssetTypeId);
                            if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                            {
                                ViewBag.DerivedAttributes = mAssetAttributes;
                            }
                        }


                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Signal".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetSignalNewDashboard.cshtml", mAssetLister);
                            //if (System.Web.HttpContext.Current.Request.IsSecureConnection)
                            //{
                            //    return PartialView("~/Views/Site/_GetSignalNewDashboard.cshtml", mAssetLister);
                            //}
                            //else
                            //{
                            //    return PartialView("~/Views/Site/_GetSignalDashboard.cshtml", mAssetLister);
                            //}
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Gate".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetGateDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Point Machine".ToLower())
                        {
                            if (mAssetLister.SearchCriteria.SiteId == 14)
                            {
                                var mSearchCriteria = new SearchCriteria();
                                mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
                                mSearchCriteria.SiteId = mAssetLister.SearchCriteria.SiteId;
                                var mDatalogger = DataLoggerEventBySite(mSearchCriteria);
                                if (mDatalogger != null && mDatalogger.Count > 0)
                                {
                                    ViewBag.Datalogger = mDatalogger;
                                }
                            }


                            return PartialView("~/Views/Site/_GetPointMachineDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "AXLE COUNTER".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetAssetAxleCounte.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "BHMS".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetBHMSDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "ELD".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetELDDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Device Time".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetDeviceTimeDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Temperature".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetTemperatureDashboard.cshtml", mAssetLister);
                        }
                        if (mAssetLister.SearchCriteria != null && !string.IsNullOrEmpty(mAssetLister.SearchCriteria.AssetTypeName) && mAssetLister.SearchCriteria.AssetTypeName.ToLower() == "Humidity".ToLower())
                        {
                            return PartialView("~/Views/Site/_GetHumidityDashboard.cshtml", mAssetLister);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("~/Views/Site/_GetAssetDetails.cshtml", mAssetLister);
        }

        public ActionResult _GetDeviceTimeDashboard(AssetLister mAssetLister)
        {
            var site = siteService.Get(mAssetLister.SearchCriteria.SiteId);
            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            return PartialView(site);
        }

        public MQTTDetailList GetMQTTDetailList()
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMQTTDetail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return mMQTTDetail;
        }

        public ActionResult GetAssetInfo(int id, int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetAssetInfo/{0}/{1}", id, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_AssetInfoPartial", mAssetAttributes);
        }

        public JsonResult GetAssetAttribute(int assetId, int siteId, int assetTypeId, string date)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAttribueData/{0}/{1}/{2}/{3}", siteId, assetTypeId, assetId, date)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetData(int assetTypeId, int siteId)
        {
            List<SiteAttributeData> mSiteAttributeDatas = new List<SiteAttributeData>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteAttributeData/GetData/{0}/{1}", assetTypeId, siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteAttributeDatas = JsonConvert.DeserializeObject<List<SiteAttributeData>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return Json(mSiteAttributeDatas, JsonRequestBehavior.AllowGet);
        }

        public ActionResult EditSite(int siteId = 0)
        {
            Site mSite = new Site();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Site>(jsonString);
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            GetAllZones();
            GetAllPLCLog();
            ViewBag.Sections = new SelectList(GetAllSection(), "Id", "Name");
            //ViewBag.DataloggerVersions = E7FRSAdvance.Utility.EnumHelper.GetDisplayName(E7FRSAdvance.Utility.Utility.DataloggerVersion.Ver1);

            return View(mSite);
        }

        public JsonResult UpdateSite(Site mSite)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mSite.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            mAPIResponse.IsSuccess = true;
                            mAPIResponse.Message = "Site has been updated.";
                        }
                        else
                        {
                            mAPIResponse.IsSuccess = false;
                            mAPIResponse.Message = "Error occured while updating site!"; ;
                        }
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                mAPIResponse.IsSuccess = false;
                mAPIResponse.Message = "Internal server error.";
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _AddPointMachine(int siteId = 0)
        {
            SitePointMachine mSitePointMachine = new SitePointMachine();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SitePointMachine/GetSitePointMachine/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSitePointMachine = JsonConvert.DeserializeObject<SitePointMachine>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mSitePointMachine);
        }

        public ActionResult _GetAssignSiteUser(int siteId = 0)
        {
            List<Domain.UserSite> mUserSites = new List<Domain.UserSite>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetUsersBySiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserSites = JsonConvert.DeserializeObject<List<Domain.UserSite>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_AssignUserSite", mUserSites);
        }

        public JsonResult SavePointMachine(SitePointMachine mSitePointMachine)
        {
            bool isSuccess = false;
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSitePointMachine);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SitePointMachine/SaveSitePointMachine"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAsset(int siteId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAsset/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public JsonResult SaveRouteSite(RouteSite mRouteSite)
        {
            bool isSuccess = false;
            try
            {
                mRouteSite.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRouteSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("RouteSite/SaveRouteSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mRouteSite = JsonConvert.DeserializeObject<RouteSite>(jsonString);
                        if (mRouteSite.Id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult DeleteRouteById(int id)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("RouteSite/DeleteRouteSiteById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetRouteTrack(int routeId, int siteId)
        {
            RouteTrackLister mRouteTrackLister = new RouteTrackLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("RouteTrack/GetAllRouteTrack/{0}/{1}", routeId, siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mRouteTrackLister = JsonConvert.DeserializeObject<RouteTrackLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("~/Views/Site/_RouteTrackPartialList.cshtml", mRouteTrackLister);
        }

        public JsonResult SaveRouteTrack(RouteTrack mRouteTrack)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRouteTrack);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("RouteTrack/SaveRouteTrack"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mRouteTrack = JsonConvert.DeserializeObject<RouteTrack>(jsonString);
                        if (mRouteTrack.Id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult DeleteRouteTrackById(int id)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("RouteTrack/DeleteRouteTrackById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _AddNumbers(int siteId = 0)
        {
            SiteNumberLister mSiteNumberLister = new SiteNumberLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteNumber/GetAllSiteNumber/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteNumberLister = JsonConvert.DeserializeObject<SiteNumberLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mSiteNumberLister);
        }

        public ActionResult _AddSignalNumber(int siteId = 0)
        {
            SiteNumberLister mSiteNumberLister = new SiteNumberLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteNumber/GetAllSignalNumber/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteNumberLister = JsonConvert.DeserializeObject<SiteNumberLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mSiteNumberLister);
        }

        public ActionResult GetSmsTriggerConfig(int siteId = 0)
        {
            List<Domain.SmsTriggerSetting> smsTriggerSettings = new List<SmsTriggerSetting>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SMSLog/GetSMSTriggerSetting/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        smsTriggerSettings = JsonConvert.DeserializeObject<List<Domain.SmsTriggerSetting>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_SmsTriggerConfig", smsTriggerSettings);
        }

        public ActionResult GetChannelConfig(int siteId = 0)
        {
            List<Domain.ChannelConfig> mChannelConfig = new List<ChannelConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetChannelConfig/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mChannelConfig = JsonConvert.DeserializeObject<List<Domain.ChannelConfig>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_ChannelConfig", mChannelConfig);
        }

        public ActionResult GetSiteTemperature(int siteId = 0)
        {
            Domain.SiteTemperature mSiteTemperatures = new Domain.SiteTemperature();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteTemperature/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteTemperatures = JsonConvert.DeserializeObject<Domain.SiteTemperature>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_SiteTemperature", mSiteTemperatures);
        }

        public JsonResult SaveSiteTemperature(Domain.SiteTemperature mSiteTemperature)
        {
            bool isSuccess = false;
            try
            {
                mSiteTemperature.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteTemperature);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteTemperature"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteTemperature = JsonConvert.DeserializeObject<Domain.SiteTemperature>(jsonString);
                        if (mSiteTemperature.Id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetUserClasses()
        {
            List<Domain.UserClass> mUserClasses = new List<Domain.UserClass>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserClass/GetAllUserClass")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserClasses = JsonConvert.DeserializeObject<List<Domain.UserClass>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mUserClasses, JsonRequestBehavior.AllowGet);
        }

        public JsonResult SaveSiteNumber(SiteNumber mSiteNumber)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteNumber);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteNumber/SaveSiteNumber"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteNumber = JsonConvert.DeserializeObject<SiteNumber>(jsonString);
                        if (mSiteNumber.Id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult SaveAssignUser(UserSite mUserSite)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveAssignUser"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);

                    }
                }
            }
            catch (Exception)
            {
                mAPIResponse = new APIResponse();
                mAPIResponse.Value = false;
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }
        public JsonResult SaveChannelMacId(ChannelConfig channels)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(channels);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveChannelMacId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var id = JsonConvert.DeserializeObject<int>(jsonString);
                        if (id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }
        public JsonResult SaveSignalNumber(SiteNumber mSiteNumber)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteNumber);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteNumber/SaveSignalNumber"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteNumber = JsonConvert.DeserializeObject<SiteNumber>(jsonString);
                        if (mSiteNumber.Id > 0)
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult DeleteSiteNumberById(int id)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteNumber/DeleteSiteNumberById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult DeleteSignalNumberById(int id)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteNumber/DeleteSignalNumberById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }
        public JsonResult DeleteChannelById(int id)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/DeleteChannelById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAttribute(List<string> attributes, int assetId, int assetTypeId, int siteId)
        {
            Asset mAsset = new Asset();
            try
            {
                if (attributes != null)
                    mAsset.siteAttributeDatasList = attributes;
                mAsset.Id = assetId;
                mAsset.AssetTypeId = assetTypeId;
                mAsset.SiteId = siteId;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAttribute"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetMQTTData(string basePath, string channel)
        {
            string mqttValue = "";
            try
            {
                var factory = new MqttFactory();
                _client = factory.CreateMqttClient();
                string clientId = Guid.NewGuid().ToString();
                string mqttURI = "mqtt.wattmon.com";
                string mqttUser = "wattmon";
                string mqttPassword = "wattmon_2018";
                int mqttPort = 1883;
                _options = new MqttClientOptionsBuilder()
                    .WithClientId(clientId)
                    .WithCredentials(mqttUser, mqttPassword)
                    .WithTcpServer(mqttURI, mqttPort)
                    .WithCleanSession()
                    .Build();

                _client.UseConnectedHandler(e =>
            {
                _client.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(basePath + channel).Build()).Wait();
            });
                _client.UseDisconnectedHandler(e =>
                {

                });
                _client.UseApplicationMessageReceivedHandler(e =>
               {
                   mqttValue = Encoding.Default.GetString(e.ApplicationMessage.Payload);
                   mqttValue = mqttValue.Replace('[', ' ').Replace(']', ' ').Trim();
                   Console.WriteLine(mqttValue);
                   Console.WriteLine();
                   _client.DisconnectAsync();
               });
                _client.ConnectAsync(_options);
                int time = 3000;
                Task.Run(() => Thread.Sleep(time)).Wait();
            }
            catch (Exception ex)
            {
            }
            return Json(mqttValue.Split(','), JsonRequestBehavior.AllowGet);
        }

        public ActionResult Alerts(int assetId)
        {
            AlertsLister mAlertMessage = new AlertsLister();
            mAlertMessage.SearchCriteria.FromDate = DateTime.Now;
            mAlertMessage.SearchCriteria.ToDate = DateTime.Now;
            mAlertMessage.SearchCriteria.AssetId = assetId;
            return View("AlertsDashboard", mAlertMessage);
        }

        public ActionResult AlertsList(AlertsLister mAlertMessage)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertMessage.SearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"Asset/GetErrorCodeAlert", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertMessage = JsonConvert.DeserializeObject<AlertsLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_AlertDashboardList", mAlertMessage);
        }

        public JsonResult UpdateAssetMaintenance(int assetId, bool isUnderMaintenance)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/UpdateAssetMaintenance/{0}/{1}", assetId, isUnderMaintenance)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        isSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetPointMachineDate(PointMachineData pointMachineData)
        {
            var mAsset = assetService.Get(pointMachineData.AssetId);
            if (mAsset != null && mAsset.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineList"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                            {
                                mAsset.mPointMachineData = mPointMachineDatas;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return PartialView("_PointMachineDatePartial", mAsset);
        }

        public ActionResult BindPointEventLogData(PointMachineData pointMachineData)
        {
            var mAsset = assetService.Get(pointMachineData.AssetId);
            if (mAsset != null && mAsset.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineList"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                            {
                                mAsset.mPointMachineData = mPointMachineDatas;

                                var mSearchCriteria = new SearchCriteria();
                                mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
                                mSearchCriteria.SiteId = mAsset.SiteId;
                                mSearchCriteria.AssetId = mAsset.Id;
                                var mDatalogger = DataLoggerEvent(mSearchCriteria);
                                if (mDatalogger != null && mDatalogger.Count > 0)
                                {
                                    ViewBag.Datalogger = mDatalogger;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return PartialView(mAsset);
        }

        public ActionResult _BindPointEventLogDataPartial(PointMachineData pointMachineData)
        {
            var mAsset = assetService.Get(pointMachineData.AssetId);
            if (mAsset != null && mAsset.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineList"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                            {
                                mAsset.mPointMachineData = mPointMachineDatas;

                                var mSearchCriteria = new SearchCriteria();
                                mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
                                mSearchCriteria.SiteId = mAsset.SiteId;
                                mSearchCriteria.AssetId = mAsset.Id;
                                var mDatalogger = DataLoggerEvent(mSearchCriteria);
                                if (mDatalogger != null && mDatalogger.Count > 0)
                                {
                                    ViewBag.Datalogger = mDatalogger;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return PartialView(mAsset);
        }

        public ActionResult Graph(int siteId, int assetId)
        {
            Asset mAsset = new Asset();
            List<Asset> mAssets = new List<Asset>();
            try
            {
                ViewBag.AssetId = assetId;
                ViewBag.SiteId = siteId;
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return View();
        }

        [HttpPost]
        public JsonResult GetReportData(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            if (asset.IValue == "Today")
            {
                asset.StartDate = DateTime.Now.ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Yesterday")
            {
                asset.StartDate = DateTime.Now.AddDays(-1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddDays(-1).ToShortDateString();
            }
            if (asset.IValue == "Last 7 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-7).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last 30 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-30).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "This Month")
            {
                int day = DateTime.Now.Day * -1;
                asset.StartDate = DateTime.Now.AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last Month")
            {
                int day = DateTime.Now.AddMonths(-1).Day * -1;
                asset.StartDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(30).ToShortDateString();
            }
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                asset.IsPointMachineGraph = true;
                mAssets.Add(asset);
                TempData["AssetsSearch"] = mAssets;
                TempData.Keep();
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        var sessionData = mAsset;
                        System.Web.HttpContext.Current.Session["Assets"] = sessionData;
                        TempData["Assets"] = mAsset;
                        TempData.Keep();
                        TempData["AssetsSearch"] = mAssets;
                        TempData.Keep();
                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                        var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title == "Last Update Relay End( sec ago)" ||
                                    item.Title == "Last Update Feed End( sec ago)" || item.Title == "Zero offset" || item.Title == "Last Updated"
                                    || item.Title == "Last Access (Sec)" || item.Title == "Last Access" || item.Title == "A10 id"
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                                allAssetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.siteAttributeDatasList.Where(x => x.Contains(date)).OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        }
                                        graphService.PrepareWithDataArrayGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);

                                    }
                                    foreach (var atrv in mAsset.assetAttributes)
                                    {
                                        decimal value = 0;
                                        foreach (var item in atrv.DataArray)
                                        {

                                            if (!string.IsNullOrEmpty(item))
                                            {
                                                value += Convert.ToDecimal(item);
                                            }
                                            else
                                            {
                                                value += 0;
                                            }
                                        }
                                        if (atrv.DataArray.Count > 0)
                                            value = value / atrv.DataArray.Count;

                                        atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                        atrv.DataArray = new List<string>();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                {
                                    var array = vals.Split('~');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }
                                    graphService.PrepareGraphAttribute(mAsset.assetAttributes, allAssetAttributes, vals);
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetLeakgeReportData(Asset asset, List<int> familyTrack)
        {
            List<Asset> mAssets = new List<Asset>();
            if (asset.IValue == "Today")
            {
                asset.StartDate = DateTime.Now.ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Yesterday")
            {
                asset.StartDate = DateTime.Now.AddDays(-1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddDays(-1).ToShortDateString();
            }
            if (asset.IValue == "Last 7 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-7).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last 30 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-30).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "This Month")
            {
                int day = DateTime.Now.Day * -1;
                asset.StartDate = DateTime.Now.AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last Month")
            {
                int day = DateTime.Now.AddMonths(-1).Day * -1;
                asset.StartDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(30).ToShortDateString();
            }
            Asset familyTrackAsset = new Asset();
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                mAssets.Add(asset);
                foreach (var id in familyTrack)
                {
                    mAssets = new List<Asset>();
                    Asset mA = new Asset();
                    mA = asset;
                    mA.Id = id;
                    mAssets.Add(mA);

                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            Asset mAsset = new Asset();
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                            var sessionData = mAsset;
                            System.Web.HttpContext.Current.Session["Assets"] = sessionData;
                            TempData["Assets"] = mAsset;
                            TempData.Keep();
                            TempData["AssetsSearch"] = mAssets;
                            TempData.Keep();
                            List<AssetAttribute> assetAttributes = new List<AssetAttribute>();

                            if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                            {
                                foreach (var item in mAsset.assetAttributes)
                                {
                                    if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                        item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                        || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                        )
                                    {
                                        //mAsset.assetAttributes.Remove(item);
                                    }
                                    else
                                    {
                                        assetAttributes.Add(item);
                                    }
                                }
                                mAsset.assetAttributes = assetAttributes;

                                if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                                {
                                    var leakage = new Domain.AssetAttribute();
                                    leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                    leakage.Title = "Leakage";
                                    mAsset.assetAttributes.Add(leakage);
                                }
                                if (dayLeft >= 1)
                                {

                                    for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                    {
                                        mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                        var date = dateTime.ToString("dd MMM yyyy");
                                        foreach (var vals in mAsset.siteAttributeDatasList.Where(x => x.Contains(date)).OrderBy(x => x).ToList())
                                        {
                                            var array = vals.Split('~');

                                            if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                            {
                                                var data = Convert.ToDateTime(Convert.ToString(array[0]));


                                            }

                                            //mAsset.DateList.Add(Convert.ToString(array[0]));
                                            int i = 3;
                                            decimal ifma = 0;
                                            decimal irma = 0;
                                            foreach (var item in mAsset.assetAttributes)
                                            {
                                                if (i != 9 || i != 10)
                                                {
                                                    if (item.Title == "If mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            ifma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            ifma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Ir mA")
                                                    {
                                                        if (!string.IsNullOrEmpty(array[i]))
                                                        {
                                                            irma = Convert.ToDecimal(array[i]);
                                                        }
                                                        else
                                                        {
                                                            irma = 0;
                                                        }

                                                    }
                                                    if (item.Title == "Leakage")
                                                    {
                                                        item.DataArray.Add(Convert.ToString(ifma - irma));
                                                    }
                                                    else
                                                    {
                                                        item.DataArray.Add(Convert.ToString(array[i]));
                                                    }
                                                }
                                                i++;
                                            }


                                        }
                                        foreach (var atrv in mAsset.assetAttributes)
                                        {
                                            decimal value = 0;
                                            foreach (var item in atrv.DataArray)
                                            {

                                                if (!string.IsNullOrEmpty(item))
                                                {
                                                    value += Convert.ToDecimal(item);
                                                }
                                                else
                                                {
                                                    value += 0;
                                                }
                                            }
                                            if (atrv.DataArray.Count > 0)
                                                value = value / atrv.DataArray.Count;

                                            atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                            atrv.DataArray = new List<string>();
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                            mAsset.DateList.Add(data.ToString("HH:mm"));
                                        }

                                        //mAsset.DateList.Add(Convert.ToString(array[0]));
                                        int i = 3;
                                        decimal ifma = 0;
                                        decimal irma = 0;
                                        foreach (var item in mAsset.assetAttributes)
                                        {
                                            if (i != 9 || i != 10)
                                            {
                                                if (item.Title == "If mA")
                                                {
                                                    if (!string.IsNullOrEmpty(array[i]))
                                                    {
                                                        ifma = Convert.ToDecimal(array[i]);
                                                    }
                                                    else
                                                    {
                                                        ifma = 0;
                                                    }

                                                }
                                                if (item.Title == "Ir mA")
                                                {
                                                    if (!string.IsNullOrEmpty(array[i]))
                                                    {
                                                        irma = Convert.ToDecimal(array[i]);
                                                    }
                                                    else
                                                    {
                                                        irma = 0;
                                                    }

                                                }
                                                if (item.Title == "Leakage")
                                                {
                                                    item.Data.Add(Convert.ToString(ifma - irma));
                                                }
                                                else
                                                {
                                                    if (!string.IsNullOrEmpty(array[i]))
                                                    {
                                                        item.Data.Add(Convert.ToString(array[i]));
                                                    }
                                                    else
                                                    {
                                                        item.Data.Add("0");
                                                    }
                                                }
                                            }
                                            i++;
                                        }
                                    }
                                }

                                var leakageComparison = mAsset.assetAttributes.Where(x => x.Title == "Leakage").FirstOrDefault();
                                if (leakageComparison != null)
                                {
                                    leakageComparison.Title = leakageComparison.Title + " " + leakageComparison.AssetName;
                                    familyTrackAsset.assetAttributes.Add(leakageComparison);
                                }
                                mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
                                familyTrackAsset.DateList = mAsset.DateList;
                                mAsset.MultipleLog = new List<MultipleLog>();
                                mAsset.siteAttributeDatasList = null;
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error!";
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }

            return Json(familyTrackAsset, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GetReportActualTime(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();
            if (asset.IValue == "Today")
            {
                asset.StartDate = DateTime.Now.ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Yesterday")
            {
                asset.StartDate = DateTime.Now.AddDays(-1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddDays(-1).ToShortDateString();
            }
            if (asset.IValue == "Last 7 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-7).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last 30 Day")
            {
                asset.StartDate = DateTime.Now.AddDays(-30).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "This Month")
            {
                int day = DateTime.Now.Day * -1;
                asset.StartDate = DateTime.Now.AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.ToShortDateString();
            }
            if (asset.IValue == "Last Month")
            {
                int day = DateTime.Now.AddMonths(-1).Day * -1;
                asset.StartDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(1).ToShortDateString();
                asset.EndDate = DateTime.Now.AddMonths(-1).AddDays(day).AddDays(30).ToShortDateString();
            }
            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                asset.SortDirection = "Graph";
                asset.IsGraphLoad = true;
                asset.IsPointMachineGraph = true;
                mAssets.Add(asset);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();
                        var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(mAsset.AssetTypeId);
                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title == "Last Update Relay End( sec ago)" ||
                                    item.Title == "Last Update Feed End( sec ago)" || item.Title == "Zero offset" || item.Title == "Last Updated"
                                    || item.Title == "Last Access (Sec)" || item.Title == "Last Access" || item.Title == "A10 id"
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                                allAssetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.MultipleLog.Where(x => x.TimeStamp.Date == dateTime.Date).ToList())
                                    {
                                        var array = vals.CsvData.Split('~');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        }
                                        assetAttributeService.PrepareGraphAttributeWithActualTime(mAsset.assetAttributes, allAssetAttributes, vals.CsvData, vals.ActualTimestamp, vals.ChangeTimestamp);

                                    }
                                    foreach (var atrv in mAsset.assetAttributes)
                                    {
                                        decimal value = 0;
                                        foreach (var item in atrv.DataArray)
                                        {

                                            if (!string.IsNullOrEmpty(item))
                                            {
                                                value += Convert.ToDecimal(item);
                                            }
                                            else
                                            {
                                                value += 0;
                                            }
                                        }
                                        if (atrv.DataArray.Count > 0)
                                            value = value / atrv.DataArray.Count;

                                        atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                        atrv.DataArray = new List<string>();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.MultipleLog)
                                {
                                    var array = vals.CsvData.Split('~');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }
                                    assetAttributeService.PrepareGraphAttributeWithActualTime(mAsset.assetAttributes, allAssetAttributes, vals.CsvData, vals.ActualTimestamp, vals.ChangeTimestamp);
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.MultipleLog = new List<MultipleLog>();
            mAsset.siteAttributeDatasList = null;
            var jsonResult = Json(mAsset, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public JsonResult GraphHttpPostData(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();

            try
            {
                DateTime startDate = Convert.ToDateTime(asset.StartDate);
                DateTime endDate = Convert.ToDateTime(asset.EndDate);
                var dayLeft = endDate.Subtract(startDate).TotalDays;
                mAsset.SortDirection = "Graph";
                mAssets.Add(asset);
                TempData["AssetsSearch"] = mAssets;
                TempData.Keep();
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("HttpPost/GraphHttpPostData"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                        var sessionData = mAsset;
                        System.Web.HttpContext.Current.Session["Assets"] = sessionData;
                        TempData["Assets"] = mAsset;
                        TempData.Keep();
                        TempData["AssetsSearch"] = mAssets;
                        TempData.Keep();
                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();

                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                    item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                    || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                            }
                            if (dayLeft >= 1)
                            {

                                for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
                                {
                                    mAsset.DateList.Add(dateTime.ToString("MM/dd/yyyy"));
                                    var date = dateTime.ToString("dd MMM yyyy");
                                    foreach (var vals in mAsset.siteAttributeDatasList.Where(x => x.Contains(date)).OrderBy(x => x).ToList())
                                    {
                                        var array = vals.Split(',');

                                        if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                        {
                                            var data = Convert.ToDateTime(Convert.ToString(array[0]));


                                        }

                                        //mAsset.DateList.Add(Convert.ToString(array[0]));
                                        int i = 3;
                                        decimal ifma = 0;
                                        decimal irma = 0;
                                        foreach (var item in mAsset.assetAttributes)
                                        {
                                            if (i != 9 || i != 10)
                                            {
                                                if (item.Title == "If mA")
                                                {
                                                    if (!string.IsNullOrEmpty(array[i]))
                                                    {
                                                        ifma = Convert.ToDecimal(array[i]);
                                                    }
                                                    else
                                                    {
                                                        ifma = 0;
                                                    }

                                                }
                                                if (item.Title == "Ir mA")
                                                {
                                                    if (!string.IsNullOrEmpty(array[i]))
                                                    {
                                                        irma = Convert.ToDecimal(array[i]);
                                                    }
                                                    else
                                                    {
                                                        irma = 0;
                                                    }

                                                }
                                                if (item.Title == "Leakage")
                                                {
                                                    item.DataArray.Add(Convert.ToString(ifma - irma));
                                                }
                                                else
                                                {
                                                    item.DataArray.Add(Convert.ToString(array[i]));
                                                }
                                            }
                                            i++;
                                        }


                                    }
                                    foreach (var atrv in mAsset.assetAttributes)
                                    {
                                        decimal value = 0;
                                        foreach (var item in atrv.DataArray)
                                        {

                                            if (!string.IsNullOrEmpty(item))
                                            {
                                                value += Convert.ToDecimal(item);
                                            }
                                            else
                                            {
                                                value += 0;
                                            }
                                        }
                                        if (atrv.DataArray.Count > 0)
                                            value = value / atrv.DataArray.Count;

                                        atrv.Data.Add(Convert.ToString(value.ToString("0.##")));
                                        atrv.DataArray = new List<string>();
                                    }
                                }
                            }
                            else
                            {
                                foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                                {
                                    var array = vals.Split(',');

                                    if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                    {
                                        var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                        mAsset.DateList.Add(data.ToString("HH:mm"));
                                    }

                                    //mAsset.DateList.Add(Convert.ToString(array[0]));
                                    int i = 3;
                                    decimal ifma = 0;
                                    decimal irma = 0;
                                    foreach (var item in mAsset.assetAttributes)
                                    {
                                        if (i != 9 || i != 10)
                                        {
                                            if (item.Title == "If mA")
                                            {
                                                if (!string.IsNullOrEmpty(array[i]))
                                                {
                                                    ifma = Convert.ToDecimal(array[i]);
                                                }
                                                else
                                                {
                                                    ifma = 0;
                                                }

                                            }
                                            if (item.Title == "Ir mA")
                                            {
                                                if (!string.IsNullOrEmpty(array[i]))
                                                {
                                                    irma = Convert.ToDecimal(array[i]);
                                                }
                                                else
                                                {
                                                    irma = 0;
                                                }

                                            }
                                            if (item.Title == "Leakage")
                                            {
                                                item.Data.Add(Convert.ToString(ifma - irma));
                                            }
                                            else
                                            {
                                                if (!string.IsNullOrEmpty(array[i]))
                                                {
                                                    item.Data.Add(Convert.ToString(array[i]));
                                                }
                                                else
                                                {
                                                    item.Data.Add("0");
                                                }
                                            }
                                        }
                                        i++;
                                    }
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            mAsset.DateList = mAsset.DateList.OrderBy(x => x).ToList();
            mAsset.siteAttributeDatasList = null;
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        private Asset GetDataByDateRange(Asset asset)
        {
            List<Asset> mAssets = new List<Asset>();
            Asset mAsset = new Asset();

            DateTime startDate = Convert.ToDateTime(asset.StartDate);
            DateTime endDate = Convert.ToDateTime(asset.EndDate);
            var dayLeft = endDate.Subtract(startDate).TotalDays;
            mAsset.SortDirection = "Graph";


            for (DateTime dateTime = startDate; dateTime <= endDate; dateTime += TimeSpan.FromDays(1))
            {
                asset.StartDate = dateTime.ToShortDateString();
                asset.EndTime = dateTime.ToShortDateString();
                mAssets.Add(asset);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);


                        var sessionData = mAsset;
                        System.Web.HttpContext.Current.Session["Assets"] = sessionData;
                        TempData["Assets"] = mAsset;
                        TempData.Keep();

                        List<AssetAttribute> assetAttributes = new List<AssetAttribute>();

                        if (mAsset.assetAttributes != null && mAsset.assetAttributes.Count > 0)
                        {
                            foreach (var item in mAsset.assetAttributes)
                            {
                                if (item.Title.Contains("Last Update Relay End( sec ago)") ||
                                    item.Title.Contains("Last Update Feed End( sec ago)") || item.Title.Contains("Zero offset") || item.Title.Contains("Last Updated")
                                    || item.Title.Contains("Last Access (Sec)") || item.Title.Contains("Last Access")
                                    )
                                {
                                    //mAsset.assetAttributes.Remove(item);
                                }
                                else
                                {
                                    assetAttributes.Add(item);
                                }
                            }
                            mAsset.assetAttributes = assetAttributes;

                            if (mAsset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault().ToLower() == "TRACK".ToLower())
                            {
                                var leakage = new Domain.AssetAttribute();
                                leakage.AssetName = mAsset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                leakage.Title = "Leakage";
                                mAsset.assetAttributes.Add(leakage);
                            }

                            foreach (var vals in mAsset.siteAttributeDatasList.OrderBy(x => x).ToList())
                            {
                                var array = vals.Split(',');

                                if (!string.IsNullOrEmpty(Convert.ToString(array[0])))
                                {
                                    var data = Convert.ToDateTime(Convert.ToString(array[0]));

                                    mAsset.DateList.Add(data.ToString("HH:mm"));
                                }

                                //mAsset.DateList.Add(Convert.ToString(array[0]));
                                int i = 3;
                                decimal ifma = 0;
                                decimal irma = 0;
                                foreach (var item in mAsset.assetAttributes)
                                {
                                    if (i != 9 || i != 10)
                                    {
                                        if (item.Title == "If mA")
                                        {
                                            if (!string.IsNullOrEmpty(array[i]))
                                            {
                                                ifma = Convert.ToDecimal(array[i]);
                                            }
                                            else
                                            {
                                                ifma = 0;
                                            }

                                        }
                                        if (item.Title == "Ir mA")
                                        {
                                            if (!string.IsNullOrEmpty(array[i]))
                                            {
                                                irma = Convert.ToDecimal(array[i]);
                                            }
                                            else
                                            {
                                                irma = 0;
                                            }

                                        }
                                        if (item.Title == "Leakage")
                                        {
                                            item.Data.Add(Convert.ToString(ifma - irma));
                                        }
                                        else
                                        {
                                            item.Data.Add(Convert.ToString(array[i]));
                                        }
                                    }
                                    i++;
                                }
                            }

                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error!";
                    }
                }
            }

            return mAsset;
        }

        public ActionResult DownloadReportCsv()
        {
            try
            {
                Asset mAsset = new Asset();
                if (TempData["AssetsSearch"] != null)
                {
                    var mAssets = TempData.Peek("AssetsSearch") as List<Asset>;
                    TempData.Keep();
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateReport"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);
                            DownloadCsv(mAsset);
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error!";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return View();
        }

        public void DownloadCsv(Asset mAsset)
        {
            if (mAsset != null)
            {
                var mAssets = TempData.Peek("AssetsSearch") as List<Asset>;
                TempData.Keep();
                string StartDate = mAssets.FirstOrDefault().StartDate;
                string EndDate = mAssets.FirstOrDefault().EndDate;
                string csv = string.Empty;
                csv += "SiteName,";
                csv += "Date,";
                csv += "AssetType,";
                csv += "AssetName,";
                foreach (var assetAttribute in mAsset.assetAttributes)
                {
                    csv += assetAttribute.AssetTypeName + ">" + @assetAttribute.AssetName + ">" + @assetAttribute.Title + ',';
                }
                csv += "\r\n";
                foreach (var row in mAsset.siteAttributeDatasList)
                {
                    string newRow = mAsset.SiteName + "," + row.Replace("~", ",");
                    //Add the Data rows.
                    csv += newRow;
                    //Add new line.
                    csv += "\r\n";
                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=SiteReport(" + StartDate + " to " + EndDate + ").csv");
                Response.Charset = "";
                Response.ContentType = "application/text";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }
        }

        public JsonResult GetAlarmStatus(int assetId)
        {
            bool result = false;
            try
            {
                Domain.SMSLogLister mSMSLogLister = new Domain.SMSLogLister();
                mSMSLogLister.SearchCriteria.AssetId = assetId;
                mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                        {
                            result = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }



            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {

            //        var response = hcf.client.GetAsync(String.Format("SMSLog/GetSMSLogByAssetId/{0}", assetId)).Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            string jsonString = response.Content.ReadAsStringAsync().Result;
            //            result = JsonConvert.DeserializeObject<bool>(jsonString);
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    ViewBag.Error = ex.Message;
            //}
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetLogByAssetId(int assetId)
        {
            List<SMSLog> mSLogs = new List<SMSLog>();
            try
            {
                Domain.SMSLogLister mSMSLogLister = new Domain.SMSLogLister();
                mSMSLogLister.SearchCriteria.AssetId = assetId;
                mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                        {
                            mSLogs = mSMSLogLister.mSMSLogs;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSLogs);
        }

        public ActionResult SipView(int siteId)
        {
            Domain.SIPView mSipView = new Domain.SIPView();
            mSipView.SiteId = siteId;
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.SearchCriteria.SiteId = siteId;
            mAssetLister.SearchCriteria.AssetTypeId = 2;
            mAssetLister.Pager.Take = mAssetLister.Pager.PageSize;
            try
            {

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return View("SipView1", mAssetLister);
        }

        public ActionResult GetSipView(int siteId)
        {
            Domain.SIPView mSipView = new Domain.SIPView();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("Site/GetSipView/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSipView = JsonConvert.DeserializeObject<Domain.SIPView>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            if (mSipView == null)
            {
                mSipView = new Domain.SIPView();
                mSipView.SiteId = siteId;
            }
            return Json(mSipView, JsonRequestBehavior.AllowGet);
        }

        public JsonResult SaveSipView(Domain.SIPView sipView)
        {
            bool isSuccess = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(sipView);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/SaveSipView"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string value = JsonConvert.DeserializeObject<string>(jsonString);
                        if (value == "Success")
                            isSuccess = true;
                        else
                            isSuccess = false;
                    }
                    else
                    {
                        isSuccess = false;
                    }
                }
            }
            catch (Exception)
            {
                isSuccess = false;
            }
            return Json(isSuccess, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Sview(int siteId)
        {
            Domain.SIPView mSipView = new Domain.SIPView();
            mSipView.SiteId = siteId;
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.SearchCriteria.SiteId = siteId;
            mAssetLister.SearchCriteria.AssetTypeId = 0;
            mAssetLister.Pager.Take = -1;
            try
            {

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        mAssetLister.mAssets = mAssetLister.mAssets.Where(x => x.AssetTypeName.Contains("TRACK") || x.AssetTypeName.Contains("SIGNAL") || x.AssetTypeName.Contains("POINT MACHINE") || x.AssetTypeName.Contains("AXLE COUNTER") || x.AssetTypeName.Contains("GATE") || x.AssetTypeName.Contains("Bus Bar")).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return View("Sview", mAssetLister);
        }

        public JsonResult GetRosterHistory(int siteId, int assetId)
        {
            List<RosterHistory> rosterHistories = new List<RosterHistory>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"Maintenance/GetMaintenaceHistoryByAssetId/{assetId}/{siteId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        rosterHistories = JsonConvert.DeserializeObject<List<RosterHistory>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(rosterHistories, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAlertCountByAsset(AlertAnalyticsSearch mAlertAnalyticsSearch)
        {
            AnalyticsLister mAnalyticsLister = new AnalyticsLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAnalyticsSearch);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAlertCountByAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAnalyticsLister = JsonConvert.DeserializeObject<AnalyticsLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return Json(mAnalyticsLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetErrorcode(Domain.AlertMessage searchCriteria)
        {
            AlertsLister mAlertMessage = new AlertsLister();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync($"Asset/GetErrorCodeAlert", str).Result;

                    //var response = hcf.client.GetAsync(String.Format("Asset/GetErrorCodeAlert/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertMessage = JsonConvert.DeserializeObject<AlertsLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_ErrorCode", mAlertMessage);
        }

        public ActionResult GetPointMachineById(Domain.Asset mAsset)
        {
            Domain.AssetLister mAssetLister = new AssetLister();
            mAssetLister.Pager.Take = -1;
            mAssetLister.SearchCriteria.Id = mAsset.Id;
            mAssetLister.SearchCriteria.AssetTypeId = mAsset.AssetTypeId;
            mAssetLister.SearchCriteria.SiteId = mAsset.SiteId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_PointMachinePartial", mAssetLister);
        }

        public JsonResult GetAllAssetWithAttribute(int siteId)
        {
            List<Asset> mAsset = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAllAssetWithAttribute/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetHttpPostData(Domain.Asset mAsset)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("HttpPost/GetHttpPostData"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                    else
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var error = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetSignalAspectData(int assetId)
        {
            Domain.Asset mAsset = new Asset();
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var jsonStr = JsonConvert.SerializeObject(mAsset);
                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                var response = hcf.client.GetAsync(String.Format("Asset/GetSignalAspectData/{0}", assetId)).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    mAsset = JsonConvert.DeserializeObject<Asset>(jsonString);

                }
                else
                {
                    ViewBag.Error = "Internal server error!";
                }
            }

            return PartialView("_SignalAspectData", mAsset);
        }

        public JsonResult UpdateAssetSequence(int oldAssetId, int newAssetId)
        {
            bool isUpdate = false;
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync(String.Format("Asset/UpdateAssetSequence/{0}/{1}", oldAssetId, newAssetId)).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    isUpdate = JsonConvert.DeserializeObject<bool>(jsonString);

                }
                else
                {
                    ViewBag.Error = "Internal server error!";
                }
            }
            return Json(isUpdate, JsonRequestBehavior.AllowGet);
        }

        public ActionResult EIView(int siteId)
        {
            var mCOSConfigLister = new COSConfigLister();
            if (mCOSConfigLister != null && mCOSConfigLister.SearchCriteria != null)
            {
                mCOSConfigLister.SearchCriteria.SiteId = siteId;
                mCOSConfigLister.Pager.PageSize = 50;
            }

            return View(mCOSConfigLister);
        }

        public PartialViewResult _EIViewListPartial(COSConfigLister mCOSConfigLister)
        {
            mCOSConfigLister.Pager.Take = mCOSConfigLister.Pager.PageSize;
            var dts = DateTime.Now.TimeOfDay;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCOSConfigLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("COSConfig/GetAllCOSConfigLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mCOSConfigLister = JsonConvert.DeserializeObject<COSConfigLister>(jsonString);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView("_EIViewListPartial", mCOSConfigLister);
        }

        public JsonResult CheckChannelIsDownBySiteId(List<int> siteIds)
        {
            List<Site> mSites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(siteIds);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/CheckChannelIsDownBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
                    }

                }
            }
            catch (Exception)
            {

            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public PartialViewResult _GetAssignTestSiteUser(int siteId = 0)
        {
            List<Domain.UserSite> mUserSites = new List<Domain.UserSite>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetUsersBySiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserSites = JsonConvert.DeserializeObject<List<Domain.UserSite>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            GetAssignUtilityLog();
            ViewBag.SiteId = siteId;
            return PartialView("_AssignTestSiteUser", mUserSites);
        }

        public JsonResult SaveAssignUserSiteTest(Domain.UserSiteTest mUserSiteTest)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserSiteTest);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("UserSiteTest"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserSiteTest = JsonConvert.DeserializeObject<Domain.UserSiteTest>(jsonString);

                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mUserSiteTest, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssignTestSiteUser(int siteId = 0)
        {
            List<Domain.UserSiteTest> mUserSiteTests = new List<Domain.UserSiteTest>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserSiteTest/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserSiteTests = JsonConvert.DeserializeObject<List<Domain.UserSiteTest>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mUserSiteTests, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssest(int siteId, int assetTypeId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/GetAllAssest/{0}/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAssets(int siteId, int assetTypeId, int assetId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/SiteId/{0}/AssetTypeId/{1}/AssetId/{2}", siteId, assetTypeId, assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssets, JsonRequestBehavior.AllowGet);
        }

        public AssetLister SetPagination(AssetLister mAssetLister)
        {
            if (mAssetLister != null && mAssetLister.Pager != null)
            {
                var tempDataAssetLister = TempData.Peek("AssetLister") as AssetLister;
                if (tempDataAssetLister != null && tempDataAssetLister.Pager != null)
                {
                    //var dsd = tempDataAssetLister as AssetLister;
                    mAssetLister.Pager.Skip = tempDataAssetLister.Pager.Skip + 30;
                    mAssetLister.Pager.Take = 30;

                    mAssetLister.SearchCriteria.SiteId = tempDataAssetLister.SearchCriteria.SiteId;
                    mAssetLister.SearchCriteria.AssetTypeId = tempDataAssetLister.SearchCriteria.AssetTypeId;

                }
                else
                {
                    mAssetLister.Pager.Take = 30;
                }
            }
            return mAssetLister;
        }

        public ActionResult GetDigitalSignalAttribute(int assetTypeId, int assetId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetAttribute/AssetTypeId/{assetTypeId}/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                        if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                            mAssetAttributes = mAssetAttributes.Where(x => x.IsDigital != null && x.IsDigital.Value && x.GateContectTypeId != null).ToList();
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return Json(mAssetAttributes);
        }

        public ActionResult GetLuxSignalAttribute(int assetTypeId, int assetId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetAttribute/AssetTypeId/{assetTypeId}/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                        if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                            mAssetAttributes = mAssetAttributes.Where(x => x.Title.Contains("_DI")).ToList();
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return Json(mAssetAttributes);
        }

        public ActionResult GetMultiplication(int assetId, int attributeId)
        {
            AssetInfo mAssetInfo = new AssetInfo();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetInfo/AssetId/{0}/AttributeId/{1}", assetId, attributeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfo = JsonConvert.DeserializeObject<AssetInfo>(jsonString);
                        if (mAssetInfo != null && mAssetInfo.Id > 0)
                        {
                            var mCardLine = GetYardConfig(assetId, attributeId);
                            if (mCardLine != null && mCardLine.Id > 0 && mCardLine.SiteId != null)
                            {
                                var mCardLines = new List<CardLine>();
                                mCardLines.Add(mCardLine);
                                var mCalibData = GetA10Calibration(mCardLine.SiteId.Value);
                                var a10Multiplayer = keepingService.GetA10Multiplayer(mCalibData, mCardLines, assetId, attributeId);
                                if (a10Multiplayer != null && a10Multiplayer.A10Multiplication.IsNotNullOrEmpty())
                                {
                                    mAssetInfo.A10Multiplayer = a10Multiplayer.A10Multiplication;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetInfo, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateCalibration(AssetInfo mAssetInfo)
        {
            dynamic data = new ExpandoObject();
            if (mAssetInfo != null && mAssetInfo.AssetAttributeId > 0 && mAssetInfo.Multiplication != null)
            {
                var mAssetAttribute = GetAssetInfoBy(mAssetInfo.AssetId, mAssetInfo.AssetAttributeId);
                if (mAssetAttribute != null && mAssetAttribute.Id > 0 && mAssetAttribute.DefaultMultiplication != null)
                {
                    decimal percentage = 2.0m;
                    if (ClsHttpContent.LoginUser.CalibrationPercentage != null && ClsHttpContent.LoginUser.CalibrationPercentage.Value > 0)
                        percentage = ClsHttpContent.LoginUser.CalibrationPercentage.Value;

                    var minValue = mAssetAttribute.DefaultMultiplication.Value * (1 - percentage / 100m);
                    var maxValue = mAssetAttribute.DefaultMultiplication.Value * (1 + percentage / 100m);


                    if (mAssetInfo.Multiplication.Value > maxValue)
                    {
                        data = new { type = "success", result = $"Requested Value needs multiplayer {mAssetInfo.Multiplication} which is not allow so setting the multiplayer {maxValue}", multiplication = maxValue };

                        mAssetInfo.Multiplication = maxValue;

                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mAssetInfo);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PutAsync(String.Format($"AssetInfo/AssetId/{mAssetInfo.AssetId}/AssetAttributeId/{mAssetInfo.AssetAttributeId}"), str).Result;
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {

                                }
                                else
                                {
                                    data = new { type = "error", result = "Error occured while updating multiplication!" };
                                }

                            }
                        }
                        catch (Exception)
                        {
                            data = new { type = "error", result = "Error occured while updating multiplication!" };
                        }
                    }
                    else if (mAssetInfo.Multiplication.Value < minValue)
                    {
                        data = new { type = "success", result = $"Requested Value needs multiplayer {mAssetInfo.Multiplication} which is not allow so setting the multiplayer {minValue}", multiplication = minValue };
                        mAssetInfo.Multiplication = minValue;
                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mAssetInfo);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PutAsync(String.Format($"AssetInfo/AssetId/{mAssetInfo.AssetId}/AssetAttributeId/{mAssetInfo.AssetAttributeId}"), str).Result;
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {

                                }
                                else
                                {
                                    data = new { type = "error", result = "Error occured while updating multiplication!" };
                                }

                            }
                        }
                        catch (Exception)
                        {
                            data = new { type = "error", result = "Error occured while updating multiplication!" };
                        }

                    }
                    else
                    {
                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mAssetInfo);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PutAsync(String.Format($"AssetInfo/AssetId/{mAssetInfo.AssetId}/AssetAttributeId/{mAssetInfo.AssetAttributeId}"), str).Result;
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {
                                    data = new { type = "success", result = "Multiplication is updated.", multiplication = mAssetInfo.Multiplication };
                                }
                                else
                                {
                                    data = new { type = "error", result = "Error occured while updating multiplication!" };
                                }

                            }
                        }
                        catch (Exception)
                        {
                            data = new { type = "error", result = "Error occured while updating multiplication!" };
                        }
                    }

                }
                else
                    data = new { type = "error", result = "Default Multiplication not set!" };
            }
            else
                data = new { type = "error", result = "Multiplication is required!" };

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult Update(Site mSite)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mSite.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"Site/{mSite.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Status has been updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateSingleModamPartial(Domain.Asset mAsset)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAsset.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAsset);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"Asset/UpdateSingleModamPartial/AssetId/{mAsset.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var isUpdate = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (isUpdate)
                            data = new { type = "success", result = "Record has been updated." };
                        else
                            data = new { type = "error", result = "Internal server error." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }


        #region PointMachineGraph

        public ActionResult GetSitePointMachineGraph(int assetId, int take, string type, string dt, string edt)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            if (!string.IsNullOrEmpty(dt))
                dt = dt.Replace("/", "-");

            if (!string.IsNullOrEmpty(edt))
                edt = edt.Replace("/", "-");

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetSitePointMachine/AssetId/{assetId}/Take/{take}/Direction/{type}/DT/{dt}/EDT/{edt}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointMachineData>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult GetPointMachineDataBySiteId(int siteId, int take, string type, string dt, string edt, bool isThickWave = false)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            if (!string.IsNullOrEmpty(dt))
                dt = dt.Replace("/", "-");

            if (!string.IsNullOrEmpty(edt))
                edt = edt.Replace("/", "-");

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetSitePointMachine/SiteId/{siteId}/Take/{take}/Direction/{type}/DT/{dt}/EDT/{edt}/IsThickWave/{isThickWave}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointMachineData>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult GetPointMachineEventData(int assetId, string direction = null, string type = null, string dt = null, string edt = null)
        {
            dynamic data = new ExpandoObject();
            List<Domain.PointCsv> mPointCsv = new List<Domain.PointCsv>();
            var splitTime = string.Empty;


            if (dt.IsNotNullOrEmpty())
            {
                var spl = dt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                dt = spl[0].Replace("/", "-");
            }

            if (edt.IsNotNullOrEmpty())
            {
                var spl = edt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                edt = spl[0].Replace("/", "-");
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetPointMachineEventData/AssetId/{assetId}/DT/{dt}/EDT/{edt}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointCsv>>(jsonString);
                        if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                        {
                            if (direction.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.name.Contains(direction)).ToList();
                            }
                            if (splitTime.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.timestamp.Contains(splitTime)).ToList();
                            }
                            if (type.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.name.Contains(type)).ToList();
                            }
                        }
                        mPointCsv = mPointMachineDatas;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(mPointCsv, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult GetPointMachineEventDataBySite(int siteId, string direction = null, string type = null, string dt = null, string edt = null)
        {
            dynamic data = new ExpandoObject();
            List<Domain.PointCsv> mPointCsv = new List<Domain.PointCsv>();
            var splitTime = string.Empty;


            if (dt.IsNotNullOrEmpty())
            {
                var spl = dt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                dt = spl[0].Replace("/", "-");
            }

            if (edt.IsNotNullOrEmpty())
            {
                var spl = edt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                edt = spl[0].Replace("/", "-");
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetPointMachineEventDataBySiteId/SiteId/{siteId}/DT/{dt}/EDT/{edt}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointCsv>>(jsonString);
                        if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                        {
                            if (direction.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.name.Contains(direction)).ToList();
                            }
                            if (splitTime.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.timestamp.Contains(splitTime)).ToList();
                            }
                            if (type.IsNotNullOrEmpty())
                            {
                                mPointMachineDatas = mPointMachineDatas.Where(x => x.name.Contains(type)).ToList();
                            }
                        }
                        mPointCsv = mPointMachineDatas;
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(mPointCsv, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult DownloadPointMachineEvent(int siteId, string dt = null, string edt = null)
        {
            if (dt.IsNotNullOrEmpty())
            {
                var spl = dt.Split(' ');
                dt = spl[0].Replace("/", "-");
            }

            if (edt.IsNotNullOrEmpty())
            {
                var spl = edt.Split(' ');
                edt = spl[0].Replace("/", "-");
            }


            try
            {
                string date = DateTime.Now.ToShortDateString();
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetPointMachineEventDataBySiteId/SiteId/{siteId}/DT/{dt}/EDT/{edt}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointCsv>>(jsonString);
                        var csv = new StringBuilder();
                        var socsvstring = string.Empty;
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6}", "timestamp", "name", "value", "Site Name", "Series/parallel", "Half/Full", "Is TWS");
                        csv.AppendLine(socsvstring);
                        if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                        {
                            foreach (var mPointMachineData in mPointMachineDatas)
                            {
                                string pmType = string.Empty;
                                string pmOprationType = string.Empty;
                                string pmThickWaveType = string.Empty;
                                if (mPointMachineData.IsHalfPointMachine != null && mPointMachineData.IsHalfPointMachine.Value)
                                    pmType = "Half";
                                else
                                    pmType = "Full";

                                if (mPointMachineData.IsSeriesOpration != null && mPointMachineData.IsSeriesOpration.Value)
                                    pmOprationType = "Series";
                                else
                                    pmOprationType = "Parallel";

                                if (mPointMachineData.ThickWaveTypeId != null)
                                {
                                    pmThickWaveType = Enum.GetName(typeof(Utility.Utility.ThickWaveType), mPointMachineData.ThickWaveTypeId);
                                }

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6}", mPointMachineData.timestamp.RemoveComma(), mPointMachineData.name.RemoveComma(), "\"" + mPointMachineData.value + "\"", mPointMachineData.SiteName.RemoveComma(), pmOprationType, pmType, pmThickWaveType);
                                csv.AppendLine(socsvstring);

                            }
                        }


                        Response.Clear();
                        Response.Buffer = true;
                        Response.AddHeader("content-disposition", "attachment;filename=Event.csv");
                        Response.Charset = "utf-8";
                        Response.ContentType = "text/csv";
                        Response.Output.Write(csv);
                        Response.Flush();
                        Response.End();
                    }
                }
            }
            catch (Exception ex)
            {
            }


            return RedirectToAction("PDF");
        }

        public ActionResult GetPointmachineGif(int assetId, string direction = null, string dt = null, string edt = null)
        {
            dynamic data = new ExpandoObject();
            string base64 = string.Empty;
            List<Domain.PointCsv> mPointCsv = new List<Domain.PointCsv>();
            var splitTime = string.Empty;


            if (dt.IsNotNullOrEmpty())
            {
                var spl = dt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                dt = spl[0].Replace("/", "-");
            }

            if (edt.IsNotNullOrEmpty())
            {
                var spl = edt.Split(' ');
                if (spl.Length > 1)
                    splitTime = spl[1];

                edt = spl[0].Replace("/", "-");
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetPointMachineGif/AssetId/{assetId}/Direction/{direction}/DT/{dt}/EDT/{edt}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mPointMachineDatas = JsonConvert.DeserializeObject<string>(jsonString);
                        if (mPointMachineDatas.IsNotNullOrEmpty())
                        {
                            dynamic jsonResponse = JsonConvert.DeserializeObject(mPointMachineDatas);
                            base64 = Convert.ToString(jsonResponse.base64_string);
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(base64, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult DownloadPointMachine(int siteId, string startDate, string endDate)
        {
            AssetLister mAssetLister = new AssetLister();
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    mAssetLister.SearchCriteria.SiteId = siteId;
                    mAssetLister.SearchCriteria.CreatedBy = ClsHttpContent.LoginUser.Id;
                    mAssetLister.SearchCriteria.IsMobileView = true;
                    mAssetLister.SearchCriteria.AssetTypeId = (int)Utility.Utility.AssetType.POINT_MACHINE;
                    mAssetLister.SearchCriteria.AssetTypeName = Utility.Utility.AssetType.POINT_MACHINE.ToString().Replace("_", " ");
                    if (string.IsNullOrEmpty(startDate))
                        mAssetLister.SearchCriteria.StartDate = DateTime.Now.ToShortDateString();
                    else
                        mAssetLister.SearchCriteria.StartDate = startDate;

                    if (string.IsNullOrEmpty(endDate))
                        mAssetLister.SearchCriteria.EndDate = DateTime.Now.ToShortDateString();
                    else
                        mAssetLister.SearchCriteria.EndDate = endDate;

                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllSiteDetailsBySiteId"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            var csv = new StringBuilder();
                            foreach (var asset in mAssetLister.mAssets)
                            {

                                var socsvstring = string.Empty;
                                csv.AppendLine(asset.Name);
                                socsvstring = string.Format("{0},{1},{2}", "Date", "Name", "Direction");
                                if (asset.IsHalfPointMachine == null || (asset.IsHalfPointMachine != null && !asset.IsHalfPointMachine.Value))
                                {
                                    socsvstring += string.Format(",{0},{1},{2},{3}", "A Current(Max/Avg)", "A Voltage", "A Time (ms)", "A Array");
                                }
                                else
                                {
                                    socsvstring += string.Format(",{0},{1},{2},{3}", "Current(Max/Avg)", "Voltage", "Time (ms)", "A Array");
                                }

                                if (asset.IsHalfPointMachine == null || (asset.IsHalfPointMachine != null && !asset.IsHalfPointMachine.Value))
                                {
                                    socsvstring += string.Format(",{0},{1},{2},{3}", "B Current(Max/Avg)", "B Voltage", "B Time (ms)", "B Array");
                                }
                                csv.AppendLine(socsvstring);

                                if (asset != null && asset.mPointMachineData != null)
                                {
                                    foreach (var point in asset.mPointMachineData.OrderByDescending(x => x.Date))
                                    {
                                        var acc = string.Join(",", point.PointMachineJson.A_C);
                                        var aArray = $"{"\"" + acc + "\""}";
                                        var aPMData = $"{string.Format("{0:0.00}", point.PointMachineJson.A_C_MAX)} / {string.Format("{0:0.00}", point.PointMachineJson.A_C_AVERAGE)}";

                                        socsvstring = string.Format($"{point.Date},{point.AssetType},{point.Direction} Operation,{aPMData},{point.PointMachineJson.A_V_AVERAGE},{point.PointMachineJson.A_C_TIME},{aArray}");

                                        if (asset.IsHalfPointMachine == null || (asset.IsHalfPointMachine != null && !asset.IsHalfPointMachine.Value))
                                        {
                                            var bcc = string.Join(",", point.PointMachineJson.B_C);
                                            var bArray = $"{"\"" + bcc + "\""}";

                                            var bPMData = $"{string.Format("{0:0.00}", point.PointMachineJson.B_C_MAX)} / {string.Format("{0:0.00}", point.PointMachineJson.B_C_AVERAGE)}";
                                            socsvstring += string.Format($",{bPMData},{point.PointMachineJson.B_V_AVERAGE},{point.PointMachineJson.B_C_TIME},{bArray}");
                                        }

                                        csv.AppendLine(socsvstring);
                                    }
                                }

                                csv.AppendLine();
                            }
                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=PointMachine" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "utf-8";
                            Response.ContentType = "text/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();
                        }


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return RedirectToAction("");
        }

        public ActionResult GetPointMachineCurrentGraph(int assetId, string startDate, string endDate)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();

            if (!string.IsNullOrEmpty(startDate))
                startDate = startDate.Replace("/", "-");

            if (!string.IsNullOrEmpty(endDate))
                endDate = endDate.Replace("/", "-");

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SitePointMachine/GetSitePointMachineCurrentGraph/AssetId/{assetId}/DT/{startDate}/EDT/{endDate}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineDatas = JsonConvert.DeserializeObject<List<Domain.PointMachineData>>(jsonString);
                        if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                            mPointMachineDatas = mPointMachineDatas.OrderBy(x => x.TimeStamp).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            var jsonResult = Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        [HttpPost]
        public ActionResult GetPointClusterImage(Domain.SearchCriteria mSearchCriteria)
        {
            string base64 = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SitePointMachine/GetPointMachineClusterImage"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        base64 = JsonConvert.DeserializeObject<string>(jsonString);

                    }

                }
            }
            catch (Exception)
            {

            }
            return Json(base64, JsonRequestBehavior.AllowGet);

        }

        public ActionResult DownloadPointLogTxt(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/DownloadPointLogTxt/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {

                                return File(csvbytes, "text/plain", $"PointLog.txt");
                            }
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "File not found!";
                        }

                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Forbidden!";
                    }
                    else if (response.StatusCode == HttpStatusCode.InternalServerError)
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }

                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }
            return View("PDF");
        }

        public ActionResult DownloadPointMachineEventLog(int siteId)
        {
            var mPointMachineEvents = GetPointMachineEventBySite(siteId, DateTime.Now.ToString("d-M-yyyy"));
            if (mPointMachineEvents != null && mPointMachineEvents.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3}", "EventTime", "Direction", "source", "Updatetime");
                csv.AppendLine(socsvstring);
                foreach (var mPointMachineEvent in mPointMachineEvents.OrderBy(x => x.Timestamp))
                {
                    string source = "";
                    if (mPointMachineEvent.TypeId == 1)
                    {
                        source = "Cluster";
                    }
                    else if (mPointMachineEvent.TypeId == 2)
                    {
                        source = "MQTT";
                    }
                    else if (mPointMachineEvent.TypeId == 3)
                    {
                        source = "FTP";
                    }
                    socsvstring = string.Format("{0},{1},{2},{3}", mPointMachineEvent.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"), mPointMachineEvent.Name, source, mPointMachineEvent.EventTime.ToString("dd/MM/yyyy HH:mm:ss"), "");
                    csv.AppendLine(socsvstring);

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=PointMachineEventLog" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();


            }
            return View("PDF");
        }

        #endregion

        #region ELD

        public ActionResult GetELDEventlog(ELDEventlogLister mELDEventlogLister)
        {
            var mAllAssetAttributes = assetAttributeService.GetAssetAttributesBy((int)E7FRSAdvance.Utility.Utility.AssetType.ELD);
            var mAssetAttributes = assetAttributeService.GetAssetAttributes(mELDEventlogLister.SearchCriteria.SiteId, mELDEventlogLister.SearchCriteria.AssetId);

            Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
            searchCriteria.SiteId = mELDEventlogLister.SearchCriteria.SiteId;
            searchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.ELD;
            searchCriteria.StartDate = mELDEventlogLister.SearchCriteria.StartDate;
            searchCriteria.EndDate = mELDEventlogLister.SearchCriteria.EndDate;
            var mMultipleLogs = GetAllGraphData(searchCriteria);
            mELDEventlogLister.ELDEventlogs = GetELDEventlogs(mELDEventlogLister.SearchCriteria.AssetId, mELDEventlogLister.SearchCriteria.SiteId, mMultipleLogs, mAssetAttributes, mAllAssetAttributes);
            return PartialView("_ELDEventlog", mELDEventlogLister);
        }

        public ActionResult GetELDAnalyze(ELDEventlogLister mELDEventlogLister)
        {
            var mAllAssetAttributes = assetAttributeService.GetAssetAttributesBy((int)E7FRSAdvance.Utility.Utility.AssetType.ELD);
            var mAssetAttributes = assetAttributeService.GetAssetAttributes(mELDEventlogLister.SearchCriteria.SiteId, mELDEventlogLister.SearchCriteria.AssetId);

            Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
            searchCriteria.SiteId = mELDEventlogLister.SearchCriteria.SiteId;
            searchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.ELD;
            searchCriteria.StartDate = mELDEventlogLister.SearchCriteria.StartDate;
            searchCriteria.EndDate = mELDEventlogLister.SearchCriteria.EndDate;
            var mMultipleLogs = GetAllGraphData(searchCriteria);

            mELDEventlogLister.ELDEventlogs = GetELDEventlogs(mELDEventlogLister.SearchCriteria.AssetId, mELDEventlogLister.SearchCriteria.SiteId, mMultipleLogs, mAssetAttributes, mAllAssetAttributes);
            return PartialView("_ELDAnalyze", mELDEventlogLister);
        }


        public ActionResult DownloadELDEventlog(ELDEventlog mELDEventlog)
        {
            Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
            searchCriteria.SiteId = mELDEventlog.SiteId;
            searchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.ELD;
            searchCriteria.StartDate = mELDEventlog.StartDate;
            searchCriteria.EndDate = mELDEventlog.EndDate;
            var mMultipleLogs = GetAllGraphData(searchCriteria);
            var mAllAssetAttributes = assetAttributeService.GetAssetAttributesBy((int)E7FRSAdvance.Utility.Utility.AssetType.ELD);
            var mAssetAttributes = assetAttributeService.GetAssetAttributes(mELDEventlog.SiteId, mELDEventlog.AssetId);
            var eldEventlogs = GetELDEventlogs(mELDEventlog.AssetId, mELDEventlog.SiteId, mMultipleLogs, mAssetAttributes, mAllAssetAttributes);
            if (eldEventlogs != null && eldEventlogs.Count > 0)
            {

                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6}", "Asset Type", "TimeStamp", "Event", "Asset Name", "Attribute Name", "Current Value", "Previous Value");
                csv.AppendLine(socsvstring);

                foreach (var eldEventlog in eldEventlogs)
                {
                    foreach (var mDeadband in eldEventlog.mDeadbands)
                    {
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6}", mDeadband.AssetTypeName.RemoveComma(), eldEventlog.TimeStamp.ToString(), eldEventlog.Message.RemoveComma(), mDeadband.AssetName.RemoveComma(), mDeadband.AttributeName.RemoveComma(), mDeadband.CurrentValue.ToString(), mDeadband.PreviousValue.ToString());
                        csv.AppendLine(socsvstring);
                    }


                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", $"attachment;filename={mELDEventlog.AssetName} - ELDEventlog.csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();

            }
            return View("_ELDEventlog", mELDEventlog);
        }

        public ActionResult DownloadELDAnalyze(ELDEventlog mELDEventlog)
        {
            Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
            searchCriteria.SiteId = mELDEventlog.SiteId;
            searchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.ELD;
            searchCriteria.StartDate = mELDEventlog.StartDate;
            searchCriteria.EndDate = mELDEventlog.EndDate;
            var mMultipleLogs = GetAllGraphData(searchCriteria);

            var mAllAssetAttributes = assetAttributeService.GetAssetAttributesBy((int)E7FRSAdvance.Utility.Utility.AssetType.ELD);
            var mAssetAttributes = assetAttributeService.GetAssetAttributes(mELDEventlog.SiteId, mELDEventlog.AssetId);

            var eldEventlogs = GetELDEventlogs(mELDEventlog.AssetId, mELDEventlog.SiteId, mMultipleLogs, mAssetAttributes, mAllAssetAttributes);
            if (eldEventlogs != null && eldEventlogs.Count > 0)
            {
                var alermToHealthyMainCount = eldEventlogs.Where(x => x.Message == "Alerm To Healthy").Count();
                var healthyToAlermMainCount = eldEventlogs.Where(x => x.Message == "Healthy To Alerm").Count();
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3}", "Gear Name", "Asset Type", $"Healthy To Alerm ({healthyToAlermMainCount})", $"Alerm To Healthy ({alermToHealthyMainCount})");
                csv.AppendLine(socsvstring);

                var groupELDAnalyze = eldEventlogs.SelectMany(d => d.mDeadbands).GroupBy(x => new { x.AssetName, x.AttributeName, x.AssetTypeName }).Select(x =>
                                      new
                                      {
                                          AssetName = x.Key.AssetName,
                                          AttributeName = x.Key.AttributeName,
                                          AssetTypeName = x.Key.AssetTypeName
                                      }).ToList();

                if (groupELDAnalyze != null && groupELDAnalyze.Count > 0)
                {
                    foreach (var assetTypeName in groupELDAnalyze.GroupBy(x => x.AssetTypeName))
                    {
                        foreach (var eldAnalyze in groupELDAnalyze.Where(x => x.AssetTypeName == assetTypeName.Key))
                        {
                            var alermToHealthyCount = eldEventlogs.Where(x => x.Message == "Alerm To Healthy").SelectMany(d => d.mDeadbands).Where(x => x.AssetName == eldAnalyze.AssetName && x.AttributeName == eldAnalyze.AttributeName).Count();

                            var healthyToAlermCount = eldEventlogs.Where(x => x.Message == "Healthy To Alerm").SelectMany(d => d.mDeadbands).Where(x => x.AssetName == eldAnalyze.AssetName && x.AttributeName == eldAnalyze.AttributeName).Count();

                            socsvstring = string.Format("{0},{1},{2},{3}", $"{eldAnalyze.AssetName} - {eldAnalyze.AttributeName}", $"{eldAnalyze.AssetTypeName}", healthyToAlermCount, alermToHealthyCount);
                            csv.AppendLine(socsvstring);
                        }
                    }

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", $"attachment;filename={mELDEventlog.AssetName} - ELDAnalyze.csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();

            }
            return View("_ELDEventlog", mELDEventlog);
        }

        private List<ELDEventlog> GetELDEventlogs(int assetId, int siteId, List<MultipleLog> mMultipleLogs, List<AssetAttribute> mAssetAttributes, List<AssetAttribute> mAllAssetAttributes)
        {
            List<ELDEventlog> eldEventlogs = new List<ELDEventlog>();
            if (mMultipleLogs != null && mMultipleLogs.Count > 0)
            {
                if (mAllAssetAttributes != null && mAllAssetAttributes.Count > 0)
                {
                    string status = string.Empty;
                    bool flag = false;
                    foreach (var mMultipleLog in mMultipleLogs.Where(x => x.AssetId == assetId).ToList())
                    {
                        var datetime = new DateTime();

                        decimal v = 0;
                        var mAssetAttribute = assetAttributeService.GetGraphAttribute(mAssetAttributes, mAllAssetAttributes, mMultipleLog.CsvData);
                        if (mAssetAttribute != null && mAssetAttribute.Count > 0)
                        {
                            var earthFaultAttribute = mAssetAttribute.Where(x => x.Title == "Earth Fault").FirstOrDefault();
                            if (earthFaultAttribute != null && earthFaultAttribute.Id > 0 && earthFaultAttribute.Data != null)
                            {
                                v = Convert.ToDecimal(earthFaultAttribute.Data.FirstOrDefault());
                                datetime = earthFaultAttribute.TimeStamp;
                            }
                        }
                        //foreach (var item in mAllAssetAttributes)
                        //{
                        //    if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                        //    {
                        //        datetime = Convert.ToDateTime(array[0]);
                        //        v = Convert.ToDecimal(array[i]);
                        //    }
                        //    i++;
                        //}

                        if (v > 3)
                        {
                            //Ok
                            if (status != "OK" && flag)
                            {
                                var mDeadbandLister = new DeadbandLister();
                                mDeadbandLister.SearchCriteria.StartDate = datetime.AddMinutes(-2);
                                mDeadbandLister.SearchCriteria.EndDate = datetime.AddMinutes(2);
                                mDeadbandLister.SearchCriteria.Date = datetime;
                                mDeadbandLister.SearchCriteria.SiteId = siteId;
                                mDeadbandLister = OprationalViewList(mDeadbandLister);
                                if (mDeadbandLister != null && mDeadbandLister.mDeadbands != null)
                                {
                                    var mDeadbands = new List<Deadband>();
                                    foreach (var mDeadband in mDeadbandLister.mDeadbands)
                                    {
                                        var minValue = (mDeadband.CurrentValue - mDeadband.PreviousValue);
                                        if (minValue < 0)
                                        {
                                            var plusValue = minValue * -1;
                                            if (mDeadband.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL && plusValue < 50)
                                            {
                                            }
                                            else if (mDeadband.AttributeName == "TPR V" && plusValue < 10)
                                            {
                                            }
                                            else
                                            {
                                                mDeadbands.Add(mDeadband);
                                            }
                                        }
                                    }
                                    eldEventlogs.Add(new ELDEventlog() { Message = "Alerm To Healthy", TimeStamp = datetime, mDeadbands = mDeadbands });
                                }
                            }
                            flag = true;
                            status = "OK";

                        }
                        if (v < 3)
                        {
                            //Alarm
                            if (status != "Alarm" && flag)
                            {
                                var mDeadbandLister = new DeadbandLister();
                                mDeadbandLister.SearchCriteria.StartDate = datetime.AddMinutes(-2);
                                mDeadbandLister.SearchCriteria.EndDate = datetime.AddMinutes(2);
                                mDeadbandLister.SearchCriteria.Date = datetime;
                                mDeadbandLister.SearchCriteria.SiteId = siteId;
                                mDeadbandLister = OprationalViewList(mDeadbandLister);
                                if (mDeadbandLister != null && mDeadbandLister.mDeadbands != null)
                                {
                                    var mDeadbands = new List<Deadband>();
                                    foreach (var mDeadband in mDeadbandLister.mDeadbands)
                                    {
                                        var minValue = (mDeadband.CurrentValue - mDeadband.PreviousValue);
                                        if (minValue > 0)
                                        {
                                            if (mDeadband.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL && minValue < 50)
                                            {
                                            }
                                            else if (mDeadband.AttributeName == "TPR V" && minValue < 10)
                                            {
                                            }
                                            else
                                            {
                                                mDeadbands.Add(mDeadband);
                                            }
                                        }
                                    }
                                    eldEventlogs.Add(new ELDEventlog() { Message = "Healthy To Alerm", TimeStamp = datetime, mDeadbands = mDeadbands });
                                }


                            }
                            flag = true;
                            status = "Alarm";
                        }

                    }
                }
            }

            return eldEventlogs;
        }

        private List<MultipleLog> GetAllGraphData(Domain.SearchCriteria searchCriteria)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetAllGraphData"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }

            return mDynamoTables;
        }

        public DeadbandLister OprationalViewList(DeadbandLister mDeadbandLister)
        {
            mDeadbandLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDeadbandLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetOprationalView"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDeadbandLister = JsonConvert.DeserializeObject<DeadbandLister>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return mDeadbandLister;
        }

        public ActionResult GetBenchMarkingPointMachineEvent(int assetId, string direction = null, string type = null)
        {
            List<Domain.BenchMarkingPointMachine> mBenchMarkingPointMachines = new List<Domain.BenchMarkingPointMachine>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"BenchMarking/GetPointMachineEventPrimary/AssetId/{assetId}/Direction/{direction}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mBenchMarkingPointMachines = JsonConvert.DeserializeObject<List<Domain.BenchMarkingPointMachine>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mBenchMarkingPointMachines, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region UtilityLogTest
        public JsonResult SaveAssignUtilityLogTest(Domain.UtilityLogTest mUtilityLogTest)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUtilityLogTest);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("UtilityLogTest"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogTest = JsonConvert.DeserializeObject<Domain.UtilityLogTest>(jsonString);

                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mUtilityLogTest, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssignUtilityLogTest(int siteId = 0)
        {
            List<Domain.UtilityLogTest> mUtilityLogTests = new List<Domain.UtilityLogTest>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLogTest/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUtilityLogTests = JsonConvert.DeserializeObject<List<Domain.UtilityLogTest>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mUtilityLogTests, JsonRequestBehavior.AllowGet);
        }

        public void GetAssignUtilityLog()
        {
            try
            {
                var mUtilities = Enum.GetValues(typeof(Utility.Utility.Utilities))
               .Cast<Utility.Utility.Utilities>()
               .Select(t => new TypeViewModel
               {
                   Id = ((int)t),
                   Name = t.ToString()
               }).ToList();

                ViewBag.Utilities = mUtilities;

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
        }

        #endregion

        #region RDPMSMapAsset

        public ActionResult GetRDPMSMapAsset(int assetId)
        {
            DataloggerAsset mDataloggerAsset = new DataloggerAsset();
            ViewBag.MQTTDetail = GetMQTT();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAsset/GetRDPMSMapAsset/AssetId/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggerAsset = JsonConvert.DeserializeObject<DataloggerAsset>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView("_RDPMSMapDataloggerAsset", mDataloggerAsset);
        }

        public MQTTDetail GetMQTT()
        {
            MQTTDetail mMQTTDetail = new MQTTDetail();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mMQTTDetailList = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                        if (mMQTTDetailList != null && mMQTTDetailList.mQTTDetailWeb != null)
                        {
                            mMQTTDetail = mMQTTDetailList.mQTTDetailWeb;
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return mMQTTDetail;
        }
        #endregion

        #region Watchlist

        public JsonResult GetAttributesByAssestId(int id)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributesByAssestId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetWatchList(int siteId, int assetId)
        {
            string remarks = string.Empty;
            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.Pager.Take = mWatchListLister.Pager.PageSize;

            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.CreatedDate != null && mWatchListLister.SearchCriteria.CreatedDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.CreatedDate = DateTime.Now;

            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister.SearchCriteria.AssetId = assetId;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            remarks = mWatchListLister.WatchLists.FirstOrDefault().Remark;
                        }

                    }

                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return Json(remarks, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GetWatchListLister(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status == null || (x.Status != null && x.Status.ToLower() != "ok" && x.Status.ToLower() != "resolved")).ToList();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            return Json(mWatchListLister, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult _WatchListPartial(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;


            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status == null || (x.Status != null && x.Status.ToLower() != "ok" && x.Status.ToLower() != "resolved")).ToList();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            return PartialView(mWatchListLister);
        }

        public ActionResult DownloadWatchList(int siteId, string startdate, string endDate)
        {
            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister.SearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "M/d/yyyy");
            mWatchListLister.SearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "M/d/yyyy");

            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;


            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;
                            int counter = 0;
                            var assetNameGroup = mWatchListLister.WatchLists.GroupBy(x => x.AssetName).ToList();
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}", "Sr No.", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type");
                            csv.AppendLine(socsvstring);
                            foreach (var assetName in assetNameGroup)
                            {
                                foreach (var item in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper()).GroupBy(x => x.AttributeName).Select(y => y.Key).ToList())
                                {
                                    foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper() && x.AttributeName.Trim().ToUpper() == item.Trim().ToUpper()).OrderBy(x => x.Pin).ToList())
                                    {
                                        string srno = string.Empty;
                                        string adc = string.Empty;
                                        string lastModifiedDate = string.Empty;
                                        string createdByName = string.Empty;
                                        if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                        {
                                            lastModifiedDate = watchList.LastModifiedDate.ToString();
                                        }
                                        if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                        {

                                        }
                                        else
                                        {
                                            counter++;
                                            srno = counter.ToString();
                                        }

                                        if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                            createdByName = "User";
                                        else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                            createdByName = "System";

                                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName);
                                        csv.AppendLine(socsvstring);
                                    }

                                }
                            }

                            var mSite = siteService.Get(siteId);

                            if (mSite != null && mSite.Id > 0)
                            {
                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename=WatchList_{mSite.Name}_{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}" + ".csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(csv);
                                Response.Flush();
                                Response.End();
                            }
                        }
                    }

                }
            }
            catch (Exception)
            {
            }
            return RedirectToAction("SiteAssetData", new { siteId = siteId });
        }

        public ActionResult UpdateWatchList(List<WatchList> mWatchLists)
        {
            dynamic data = new ExpandoObject();
            try
            {
                foreach (var mWatchList in mWatchLists)
                {
                    mWatchList.UserId = ClsHttpContent.LoginUser.Id;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchLists);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Status has been updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult InsertManualWatchList(WatchList mWatchList)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mWatchList.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchList);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/InsertManualWatchList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Manual Watch has been inserted." };
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateWatchlistRegenerate(Site mSite)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mSite.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSite);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"Site/UpdateWatchlistRegenerate/{mSite.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Watchlist csv generation is under processing please wait for some minutes to re-download." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region CreatePannelTest

        public ActionResult GetLocationAsset(int siteId, int assetTypeId, int assetId)
        {
            List<Domain.CardLine> mCardLines = new List<Domain.CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var cardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                        if (cardLines != null && cardLines.Count > 0)
                        {
                            var cardLinesAssets = cardLines.Where(x => x.AssetId == assetId).ToList();
                            if (cardLinesAssets != null && cardLinesAssets.Count > 0)
                            {
                                var cardLineGroups = cardLinesAssets.GroupBy(x => new { x.ClusterName, x.ClusterId, x.AssetTypeId }).Select(x => x.Key).ToList();
                                if (cardLineGroups != null && cardLineGroups.Count > 0)
                                {
                                    foreach (var cardLineGroup in cardLineGroups)
                                    {
                                        var groupCardLineAssets = cardLines.Where(x => x.ClusterId == cardLineGroup.ClusterId && x.AssetTypeId == cardLineGroup.AssetTypeId).ToList();
                                        if (groupCardLineAssets != null && groupCardLineAssets.Count > 0)
                                        {
                                            mCardLines.AddRange(groupCardLineAssets);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return Json(mCardLines);
        }

        public ActionResult CreatePannelTest(PannelTest mPannelTest)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mPannelTest.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mPannelTest);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format($"PannelTest"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Pannel Test has been Created." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }
        #endregion

        #region dsd

        public ActionResult GetClusterValidationAutomaticHtml(int siteId)
        {
            string html = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CommissioningDocument/GetClusterValidationAutomaticHtml/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        html = JsonConvert.DeserializeObject<string>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {

            }
            return Json(html, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region Panel Test

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
        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }
        #endregion

        public JsonResult GetAssetAttributesBy(int assetTypeId)
        {
            var mAssetAttributes = assetAttributeService.GetAssetAttributesBy(assetTypeId);
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAllAssetAttributeBy(int assetTypeId)
        {
            var mAssetAttributes = assetAttributeService.GetAllAssetAttributeBy(assetTypeId);
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _AssetTypeCircuitDiagram(int assetTypeId, int assetId)
        {
            Domain.AssetTypeCircuitDiagram mAssetTypeCircuitDiagram = new Domain.AssetTypeCircuitDiagram();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetType/GetAssetTypeCircuitDiagram/{0}", assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetTypeCircuitDiagram = JsonConvert.DeserializeObject<AssetTypeCircuitDiagram>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            finally
            {
                ViewBag.Asset = assetService.Get(assetId);
                ViewBag.CardLine = GetCardLine(assetTypeId, assetId);
                var assetAttributes = assetAttributeService.GetAllAssetAttributeBy(assetTypeId);
                if (assetAttributes != null && assetAttributes.Count > 0)
                {
                    ViewBag.AssetAttributes = assetAttributes.Where(x => x.IsDerived != null && x.IsDerived.Value);
                }
                if (assetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE)
                {
                    //var PointMachineDatas = GetPointMachineData(new PointMachineData() { AssetId = assetId, StartDate = DateTime.Now, EndDate = DateTime.Now });
                    //if (PointMachineDatas != null && PointMachineDatas.Count > 0)
                    //{
                    //    var PointMachineDataN = PointMachineDatas.Where(x => x.Direction == "Normal").OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                    //    if (PointMachineDataN != null)
                    //    {
                    //        ViewBag.PointMachineDataN = PointMachineDataN.PointMachineJson;
                    //    }
                    //    var PointMachineDataR = PointMachineDatas.Where(x => x.Direction == "Reverse").OrderByDescending(x => x.TimeStamp).FirstOrDefault();
                    //    if (PointMachineDataR != null)
                    //    {
                    //        ViewBag.PointMachineDataR = PointMachineDataR.PointMachineJson;
                    //    }
                    //}
                }
            }
            return PartialView(mAssetTypeCircuitDiagram);
        }

        public ActionResult GetFRSAlertList(int assetId)
        {

            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();

            mFRSAlertLister.SearchCriteria.AssetId = assetId;
            mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
            mFRSAlertLister = fRSAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);

            return Json(mFRSAlertLister);
        }

        public ActionResult GetEventLog(int assetId)
        {
            var mAsset = assetService.Get(assetId);
            Domain.FRSAlertLister fRSAlertLister = new FRSAlertLister();
            fRSAlertLister.SearchCriteria.AssetId = assetId;
            //fRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.FRSAlertStatus.Active;
            fRSAlertLister = fRSAlertService.GetWithOutAcknowledgementAlert(fRSAlertLister);
            if (fRSAlertLister != null && fRSAlertLister.mFRSAlerts != null && fRSAlertLister.mFRSAlerts.Count > 0)
            {
                ViewBag.FRSAlerts = fRSAlertLister.mFRSAlerts;
            }

            if (mAsset != null && mAsset.Id > 0 && mAsset.AssetTypeId == (int)Utility.Utility.AssetType.POINT_MACHINE)
            {
                var mPointMachineEvents = GetPointMachineEvent(assetId, DateTime.Now.ToString("d-M-yyyy"));
                if (mPointMachineEvents != null && mPointMachineEvents.Count > 0)
                {
                    ViewBag.PointMachineEvent = mPointMachineEvents;
                }
            }

            if (mAsset != null && mAsset.Id > 0 && mAsset.IsDatalogger)
            {
                var mSearchCriteria = new SearchCriteria();
                mSearchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
                mSearchCriteria.SiteId = mAsset.SiteId;
                mSearchCriteria.AssetId = mAsset.Id;
                var mDatalogger = DataLoggerEvent(mSearchCriteria);
                if (mDatalogger != null && mDatalogger.Count > 0)
                {
                    ViewBag.Datalogger = mDatalogger;
                }

                var mPeriodicDatalogger = PeriodicDataLoggerEvent(mSearchCriteria);
                if (mPeriodicDatalogger != null && mPeriodicDatalogger.Count > 0)
                {
                    ViewBag.PeriodicDatalogger = mPeriodicDatalogger;
                }

                if (mAsset != null && mAsset.Id > 0)
                {
                    //var strLogs = ReadPointLogEventTxt(mAsset.SiteId);
                    //if (strLogs != null && strLogs.Count > 0)
                    //{
                    //    ViewBag.PointClusterEventLog = strLogs;
                    //}

                    //var strFTPLogs = ReadPointLogFTP(mAsset.SiteId);
                    //if (strFTPLogs != null && strFTPLogs.Count > 0)
                    //{
                    //    ViewBag.PointFTPEventLog = strFTPLogs;
                    //}

                    var mPointEventLogs = GetPointLog(assetId, DateTime.Now.ToString("d-M-yyyy"));
                    if (mPointEventLogs != null && mPointEventLogs.Count > 0)
                    {
                        ViewBag.PointEventLog = mPointEventLogs;
                    }
                }

                ViewBag.AssetInfoDatalogger = GetAssetInfoDatalogger(mAsset.Id);
            }
            if (mAsset != null && mAsset.Id > 0)
            {
                ViewBag.ChangeTimeAssets = GetChangeTime(mAsset.Id);
                ViewBag.AssetInfos = GetAssetInfo(mAsset.Id);
            }

            return PartialView("_EventLog", mAsset);
        }

        public ActionResult _EventDataLoggerGraph(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            var sdateTime = DateTime.Now;
            if (searchCriteria.SearchDate.IsNotNullOrEmpty())
            {
                sdateTime = ExtensionMethod.ConvertToDateTimeFormat(searchCriteria.SearchDate, "d/M/yyyy");
                searchCriteria.SearchDate = sdateTime.ToString("d-M-yyyy");
            }
            else
            {
                searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            }

            mDataloggers = DataLoggerEvent(searchCriteria);

            return PartialView(mDataloggers);
        }

        public ActionResult _PointMachineEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.PointMachineEvent> mPointMachineEvents = new List<Domain.PointMachineEvent>();

            var sdateTime = DateTime.Now;
            if (searchCriteria.SearchDate.IsNotNullOrEmpty())
            {
                sdateTime = ExtensionMethod.ConvertToDateTimeFormat(searchCriteria.SearchDate, "d/M/yyyy");
                searchCriteria.SearchDate = sdateTime.ToString("d-M-yyyy");
            }
            else
            {
                searchCriteria.SearchDate = DateTime.Now.ToString("d-M-yyyy");
            }

            mPointMachineEvents = GetPointMachineEvent(searchCriteria.AssetId, searchCriteria.SearchDate);

            return PartialView(mPointMachineEvents);
        }

        #region Private Mathod

        private List<AssetInfo> GetAssetInfo(int assetId)
        {
            var mAssetInfos = new List<AssetInfo>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetInfo/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfos = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                    }

                }
            }
            catch (Exception)
            {
            }
            return mAssetInfos;
        }

        private AssetInfo GetAssetInfoBy(int assetId, int attributeId)
        {
            var mAssetInfo = new AssetInfo();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AssetInfo/AssetId/{assetId}/AttributeId/{attributeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfo = JsonConvert.DeserializeObject<AssetInfo>(jsonString);
                    }

                }
            }
            catch (Exception)
            {
            }
            return mAssetInfo;
        }

        private CalibData GetA10Calibration(int siteId)
        {
            CalibData mCalibData = new CalibData();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/GetA10Calibration/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var str = JsonConvert.DeserializeObject<string>(jsonString);

                        mCalibData = JsonConvert.DeserializeObject<CalibData>(str);
                        if (mCalibData != null && mCalibData.calib != null)
                        {
                            mCalibData.calib = mCalibData.calib.Where(x => x != null).ToList();
                        }
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mCalibData;
        }

        public ActionResult GetA10Graph(int siteId, string date, string a10Id = null)
        {
            var mA10Status = new List<Domain.A10Status>();
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            if (a10Id.IsNotNullOrEmpty())
                                mA10Status = mA10Status.Where(x => x.id == a10Id).ToList();

                            mA10Status = mA10Status.Where(x => x.id != "0").ToList();

                            data = mA10Status.GroupBy(x => x.id)
      .ToDictionary(x => x.Key, x => x.Select(e => new { e.last_response, e.timestamp }).ToList());
                        }

                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(data, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        private CardLine GetYardConfig(int assetId, int attributeId)
        {
            CardLine mCardLine = new CardLine();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CardLine/AssetId/{assetId}/AttributeId/{attributeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLine = JsonConvert.DeserializeObject<CardLine>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }

            return mCardLine;

        }

        private List<CardLine> GetCardLine(int assetTypeId, int assetId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CardLine/AssetTypeId/{assetTypeId}/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error!";
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }

            return mCardLines;

        }

        private List<Domain.Section> GetAllSection()
        {
            List<Domain.Section> mSections = new List<Domain.Section>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Section/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSections = JsonConvert.DeserializeObject<List<Domain.Section>>(jsonString);
                    }
                    else
                    {
                        ViewBag.Error = "Internal server error.";
                    }
                }

            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mSections;

        }

        public ActionResult GetPointMachineData(PointMachineData pointMachineData)
        {
            List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();
            var mAsset = assetService.Get(pointMachineData.AssetId);
            if (mAsset != null && mAsset.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineList"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                            if (mPointMachineDatas != null && mPointMachineDatas.Count > 0)
                            {
                                mPointMachineDatas = mPointMachineDatas.OrderByDescending(x => x.TimeStamp).ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message.ToString();
                }
            }

            return Json(mPointMachineDatas, JsonRequestBehavior.AllowGet);
        }

        private List<Domain.PointMachineEvent> GetPointMachineEvent(int assetId, string searchDate)
        {
            List<Domain.PointMachineEvent> mPointMachineEvents = new List<Domain.PointMachineEvent>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineEvent/AssetId/{assetId}/SearchDate/{searchDate}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineEvents = JsonConvert.DeserializeObject<List<Domain.PointMachineEvent>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mPointMachineEvents;
        }

        private List<Domain.PointEventLog> GetPointMachineEventBySite(int siteId, string searchDate)
        {
            List<Domain.PointEventLog> mPointMachineEvents = new List<Domain.PointEventLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineEvent/GetEventLog/SiteId/{siteId}/SearchDate/{searchDate}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mPointMachineEvents = JsonConvert.DeserializeObject<List<Domain.PointEventLog>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mPointMachineEvents;
        }

        private List<Domain.Datalogger> DataLoggerEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphDataByAsset"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //mDataloggers = JsonConvert.DeserializeObject<List<Domain.DataloggerEventData>>(jsonString);


                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mDataloggers;
        }

        private List<Domain.Datalogger> DataLoggerEventBySite(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphData"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //mDataloggers = JsonConvert.DeserializeObject<List<Domain.DataloggerEventData>>(jsonString);


                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mDataloggers;
        }

        private List<Domain.Datalogger> PeriodicDataLoggerEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetPeriodicDataByAsset"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //mDataloggers = JsonConvert.DeserializeObject<List<Domain.DataloggerEventData>>(jsonString);


                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);


                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mDataloggers;
        }

        private List<Domain.AssetInfoDatalogger> GetAssetInfoDatalogger(int assetId)
        {
            List<Domain.AssetInfoDatalogger> mAssetInfoDataloggers = new List<Domain.AssetInfoDatalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AssetInfoDatalogger/AssetId/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetInfoDataloggers = JsonConvert.DeserializeObject<List<Domain.AssetInfoDatalogger>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mAssetInfoDataloggers;
        }

        private List<string> ReadPointLogEventTxt(int siteId)
        {
            List<string> strLogs = new List<string>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/ReadPointLogTxt/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        strLogs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return strLogs;
        }

        private List<string> ReadPointLogFTP(int siteId)
        {
            List<string> strLogs = new List<string>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/ReadPointLogFTP/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        strLogs = JsonConvert.DeserializeObject<List<string>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return strLogs;
        }

        private List<PointEventLog> GetPointLog(int assetId, string searchDate)
        {
            List<PointEventLog> strLogs = new List<PointEventLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"PointMachineEvent/GetEventLog/AssetId/{assetId}/SearchDate/{searchDate}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        strLogs = JsonConvert.DeserializeObject<List<PointEventLog>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }
            return strLogs;
        }

        private List<Domain.DynamoTable> GetChangeTime(int assetId)
        {
            List<Domain.DynamoTable> mDynamoTables = new List<Domain.DynamoTable>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteKeeping/GetChangeValue/SiteId/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDynamoTables = JsonConvert.DeserializeObject<List<Domain.DynamoTable>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return mDynamoTables;
        }

        #endregion

    }
}