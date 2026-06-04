using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class DashboardController : Controller
    {
        // GET: Dashboard
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly IAssetTypeService assetTypeService;
        private readonly ISiteService siteService;
        private static List<Domain.ChatBot> mChatBots = new List<ChatBot>();
        public DashboardController(IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, ISiteService siteService)
        {
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.assetTypeService = assetTypeService;
            this.siteService = siteService;

        }

        public ActionResult Index(int zoneId = 0, int divisionId = 0)
        {
            var mSite = new Domain.Site();
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");
            mSite.ZoneId = zoneId;
            mSite.DivisionId = divisionId;
            if (divisionId > 0)
                ViewBag.Sites = siteService.GetBy(divisionId);
            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var response = hcf.client.GetAsync(String.Format($"Site/GetSites/DivisionId/{divisionId}")).Result;
            //        string jsonString = response.Content.ReadAsStringAsync().Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            ViewBag.Sites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
            //        }
            //        else
            //        {
            //            ViewBag.Error = "Internal server error.";
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    ViewBag.Error = ex.Message.ToString();
            //}

            return View(mSite);
        }

        public JsonResult GetListAlerts(Domain.SMSLog mSmsLog)
        {
            var data = new object();
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria = mSmsLog;
            try
            {
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
                            data = mSMSLogLister.mSMSLogs.GroupBy(
    p => p.SiteName,
    (key, g) => new { SiteName = key, Count = g.Count() }).ToList();
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetListPredictiveAlerts(Domain.Probability mProbability)
        {
            var data = new object();
            ProbabilityLister mProbabilityLister = new ProbabilityLister();
            mProbabilityLister.Pager.Take = -1;
            mProbabilityLister.SearchCriteria = mProbability;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mProbabilityLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mProbabilityLister = JsonConvert.DeserializeObject<ProbabilityLister>(jsonString);
                        if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
                        {
                            data = mProbabilityLister.Probability.GroupBy(
    p => p.SiteName,
    (key, g) => new { SiteName = key, Count = g.Count() }).ToList();
                        }
                    }
                }
            }
            catch (Exception)
            {

            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetListAlertByAssetType(Domain.SMSLog mSmsLog)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria = mSmsLog;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

                    }
                }
            }
            catch (Exception)
            {

            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return PartialView("_AlertCount", mSMSLogLister);
        }

        public ActionResult GetAlertCount(Domain.SMSLog mSmsLog)
        {
            int alertCount = 0;
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria = mSmsLog;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                        {
                            alertCount = mSMSLogLister.mSMSLogs.Count;
                        }

                    }
                }
            }
            catch (Exception)
            {

            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return Json(alertCount);
        }

        public ActionResult GetAssetCount(int divisionId)
        {
            List<Asset> mAssets = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Asset/DivisionId/{0}", divisionId)).Result;
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

            return PartialView("_AssetCount", mAssets);
        }

        public ActionResult _AlertList(Domain.SMSLog mSmsLog)
        {
            SMSLogLister mSMSLogLister = new SMSLogLister();
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria = mSmsLog;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                        {

                        }

                    }
                }
            }
            catch (Exception)
            {

            }
            return PartialView(mSMSLogLister);
        }

        public ActionResult GetMISSummary(string divisionName)
        {
            var mMISSummary = new List<Domain.MISSummary>();
            try
            {
                using (var hcf = new HttpClientFactory(baseUrl: System.Configuration.ConfigurationManager.AppSettings.Get("MISAPIBaseUrl"), token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("MISSummary/DivisionName/{0}", divisionName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMISSummary = JsonConvert.DeserializeObject<List<Domain.MISSummary>>(jsonString);
                        HttpCookie loginCookie = new HttpCookie("MISLoginUser");
                        //Set the Cookie value.
                        loginCookie.Values.Add("EmailAddress", ClsHttpContent.LoginUser.EmailAddress);
                        loginCookie.Values.Add("Password", ClsHttpContent.LoginUser.Password);
                        loginCookie.Expires = DateTime.Now.AddMinutes(2);
                        Response.Cookies.Add(loginCookie);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            // return PartialView("~/Views/Reporting/_GetAlertSMSLogs.cshtml", mSMSLogLister.mSMSLogs);
            return PartialView("_MISSummary", mMISSummary);
        }

        public JsonResult GetAllAttributes(int[] siteIds, int assetTypeId, int attributeId = 0, string database = null)
        {
            Asset asset = new Asset();
            bool isReadHttpPost = false;
            bool isReadLocal = false;
            if (!string.IsNullOrEmpty(database) && database == "HTTP")
                isReadHttpPost = true;

            if (!string.IsNullOrEmpty(database) && database == "Local")
                isReadLocal = true;

            if (siteIds != null && siteIds.Count() > 0)
            {
                foreach (var siteId in siteIds)
                {
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var response = hcf.client.GetAsync(String.Format("AssetType/GetAttributes/{0}/{1}/{2}/{3}/{4}", siteId, assetTypeId, attributeId, isReadHttpPost, isReadLocal)).Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                var assetAttributes = JsonConvert.DeserializeObject<Asset>(jsonString);
                                if (assetAttributes != null && assetAttributes.mAssets != null && assetAttributes.mAssets.Count > 0)
                                {
                                    asset.mAssets.AddRange(assetAttributes.mAssets);

                                }
                                if (assetAttributes != null && assetAttributes.mAssetInfos != null && assetAttributes.mAssetInfos.Count > 0 && assetAttributes.mAssets != null && assetAttributes.mAssets.Count > 0)
                                {
                                    foreach (var mAssetInfo in assetAttributes.mAssetInfos)
                                    {
                                        var mAsset = assetAttributes.mAssets.Where(x => x.Id == mAssetInfo.AssetId).FirstOrDefault();
                                        if (mAsset != null && mAsset.Id > 0)
                                        {
                                            mAssetInfo.AssetName = mAsset.Name;
                                            mAssetInfo.StationCode = mAsset.StationCode;
                                        }
                                    }
                                    asset.mAssetInfos.AddRange(assetAttributes.mAssetInfos);

                                }
                                if (assetTypeId == 1)
                                {
                                    var leakage = new Domain.AssetAttribute();
                                    leakage.AssetName = asset.assetAttributes.Select(x => x.AssetName).FirstOrDefault();
                                    leakage.Title = "Leakage";
                                    leakage.Id = -1;
                                    asset.assetAttributes.Add(leakage);
                                }
                                //if (attributeId == -1)
                                //{
                                //    foreach (var data in mAssetAttributes.mAssetInfos)
                                //    {
                                //        data.Channels = mAssetAttributes.mAssets.Where(x => x.Id == data.AssetId).Select(x => x.Name).FirstOrDefault();
                                //    }
                                //}
                                //mAssetAttributes.ChannelList = mAssetAttributes.mAssetInfos.Select(x => x.Channels).Distinct().ToList();
                            }
                            else
                            {
                                string jsonString = response.Content.ReadAsStringAsync().Result;
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
                }
            }

            return Json(asset, JsonRequestBehavior.AllowGet);
        }


        public ActionResult GetPointMachineDateRange(int assetId, string range, string type, int assetTypeId, List<int> siteIds, string operation, string sd, string ed, bool isThickWave = false)
        {
            List<PointMachineData> mPointMachines = new List<PointMachineData>();

            if (siteIds != null && siteIds.Count > 0)
            {
                foreach (var siteId in siteIds)
                {
                    PointMachineData pointMachineData = new PointMachineData();
                    pointMachineData.AssetId = assetId;
                    pointMachineData.Range = range;
                    pointMachineData.Type = type;
                    pointMachineData.AssetTypeId = assetTypeId;
                    pointMachineData.SiteId = siteId;
                    pointMachineData.IsThickWave = isThickWave;
                    if (range == "CustomeDate")
                    {
                        pointMachineData.StartDate = Convert.ToDateTime(sd);
                        pointMachineData.EndDate = Convert.ToDateTime(ed);
                    }

                    List<PointMachineData> mPointMachineDatas = new List<PointMachineData>();

                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(pointMachineData);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("Asset/GetPointMachineListRange"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                mPointMachineDatas = JsonConvert.DeserializeObject<List<PointMachineData>>(jsonString);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ViewBag.Error = ex.Message.ToString();
                    }

                    mPointMachines.AddRange(mPointMachineDatas);

                }
            }

            var jsonResult = Json(mPointMachines, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public JsonResult GetAssetAttributesBy(int assetTypeId)
        {
            List<AssetAttribute> mAssetAttributes = new List<AssetAttribute>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AssetAttribute/AssetTypeId/{assetTypeId}").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetAttributes = JsonConvert.DeserializeObject<List<AssetAttribute>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(mAssetAttributes, JsonRequestBehavior.AllowGet);
        }


        public ActionResult _ChatBotList()
        {
            mChatBots = new List<ChatBot>();
            mChatBots.Add(new Domain.ChatBot() { Message = "Hi, welcome to AI-Bot! Go ahead and ask me a question. 😄", Name = "ChatBot" });
            return PartialView("_ChatBotList", mChatBots);
        }

        public ActionResult BindMainMessage(string message = null)
        {
            //var sites = new List<Site>();
            if (!string.IsNullOrEmpty(message))
            {
                mChatBots.Add(new Domain.ChatBot() { Message = message, UserId = ClsHttpContent.LoginUser.Id, Name = $"{ClsHttpContent.LoginUser.FirstName} {ClsHttpContent.LoginUser.LastName}", MessageType = "Question" });

            }


            return PartialView("_ChatBotList", mChatBots);
        }

        [HttpPost]
        public ActionResult GetMessageList(string message = null, string fileType = null)
        {
            string strings = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new Domain.ChatBot() { Message = message, FileType = fileType });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ChatBot"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var outputstr = JsonConvert.DeserializeObject<string>(jsonString);
                        if (outputstr.IsNotNullOrEmpty())
                        {
                            strings = outputstr;
                            mChatBots.Add(new Domain.ChatBot() { Message = outputstr, Name = "ChatBot", MessageType = "Answer", Question = message });
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

            return Json(strings, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _ImpList(int divisionId)
        {
            List<Domain.DivisionImportantLink> mDivisionImportantLinks = new List<Domain.DivisionImportantLink>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DivisionImportantLink/DevisionId/{0}", divisionId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisionImportantLinks = JsonConvert.DeserializeObject<List<Domain.DivisionImportantLink>>(jsonString);
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
            return PartialView("_ImpList", mDivisionImportantLinks);
        }
        public ActionResult _SummaryofToday(int divisionId)
        {
            string strSummaryofToday = string.Empty;
            if (divisionId > 0)
            {
                var mProbabilityLister = new Domain.ProbabilityLister();
                mProbabilityLister.SearchCriteria.DivisionId = divisionId;
                mProbabilityLister.Pager.Take = -1;
                mProbabilityLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                mProbabilityLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

                if (mProbabilityLister != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                    mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mProbabilityLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("ChatBot/GetSummary"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            strSummaryofToday = JsonConvert.DeserializeObject<string>(jsonString);
                            //if (outputstr.IsNotNullOrEmpty())
                            //{
                            //    var outgdputstr = JsonConvert.DeserializeObject<object>(jsonString);
                            //    stuff = JsonConvert.DeserializeObject(outputstr);

                            //}
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

            return Json(strSummaryofToday, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _SitePartial(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite == null || (mSite != null && mSite.Id <= 0))
                        {
                            mSite = new Domain.Site();
                        }
                        else
                        {
                            SiteLister siteLister = new SiteLister();
                            siteLister.SearchCriteria.Id = mSite.Id;
                            siteLister = GetUserSite(siteLister);
                            if (siteLister != null && siteLister.mSites != null && siteLister.mSites.Count > 0)
                            {
                                SetSummaryViewCount(siteLister);
                                mSite = siteLister.mSites.FirstOrDefault();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView(mSite);
        }

        public ActionResult _MISSitePartial(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            HttpCookie loginCookie = new HttpCookie("MISLoginUser");
            //Set the Cookie value.
            loginCookie.Values.Add("EmailAddress", ClsHttpContent.LoginUser.EmailAddress);
            loginCookie.Values.Add("Password", ClsHttpContent.LoginUser.Password);
            loginCookie.Expires = DateTime.Now.AddMinutes(2);
            Response.Cookies.Add(loginCookie);

            try
            {
                using (var hcf = new HttpClientFactory(baseUrl: System.Configuration.ConfigurationManager.AppSettings.Get("MISAPIBaseUrl"), token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite == null || (mSite != null && mSite.Id <= 0))
                        {
                            mSite = new Domain.Site();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView(mSite);
        }

        public ActionResult _MISUserSitePartial(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            HttpCookie loginCookie = new HttpCookie("MISLoginUser");
            //Set the Cookie value.
            loginCookie.Values.Add("EmailAddress", ClsHttpContent.LoginUser.EmailAddress);
            loginCookie.Values.Add("Password", ClsHttpContent.LoginUser.Password);
            loginCookie.Expires = DateTime.Now.AddMinutes(2);
            Response.Cookies.Add(loginCookie);

            try
            {
                using (var hcf = new HttpClientFactory(baseUrl: System.Configuration.ConfigurationManager.AppSettings.Get("MISAPIBaseUrl"), token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite == null || (mSite != null && mSite.Id <= 0))
                        {
                            mSite = new Domain.Site();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView(mSite);
        }

        public SiteLister GetUserSite(SiteLister mSiteLister)
        {
            //mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetUserSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
                        if (mSiteLister != null && mSiteLister.mSites != null)
                        {

                        }
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
            finally
            {

            }


            return mSiteLister;
        }

        private void SetSummaryViewCount(Domain.SiteLister mSiteLister)
        {
            int trackId = (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK;
            int signalId = (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL;
            int pmId = (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE;
            if (mSiteLister != null && mSiteLister.mSites != null && mSiteLister.mSites.Count > 0)
            {
                foreach (var site in mSiteLister.mSites)
                {
                    var thisSiteRecords = mSiteLister.mSites.Where(x => x.Id == site.Id).FirstOrDefault();
                    if (thisSiteRecords != null && thisSiteRecords.Id > 0)
                    {
                        var trackCount = 0;
                        var signalCount = 0;
                        var pmCount = 0;

                        var smsLogs = thisSiteRecords.SMSLogs.Where(x => x.SiteId == thisSiteRecords.Id && x.IsSmsLogActive.HasValue && x.IsSmsLogActive.Value && x.TimeStamp.Year == DateTime.Now.Year && x.TimeStamp.Month == DateTime.Now.Month).ToList();
                        if (smsLogs != null && smsLogs.Count > 0)
                        {
                            site.SMSLogs = smsLogs;

                            //trackCount += smsLogs.Where(x => x.AssetTypeId == trackId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                            //signalCount += smsLogs.Where(x => x.AssetTypeId == signalId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                        }

                        var prbability = thisSiteRecords.Probabilities.Where(x => x.SiteId == thisSiteRecords.Id && x.TimeStamp.Date == DateTime.Now.Date).ToList();
                        if (prbability != null && prbability.Count > 0)
                        {
                            site.Probabilities = prbability;

                            var tempProbablity = prbability.Where(x =>
                            x.Category.ToLower() == "High Leakage".ToLower() ||
                            x.Category.ToLower() == "Approaching low energization".ToLower() ||
                            x.Category.ToLower() == "Battery defective".ToLower() ||
                            x.Category.ToLower() == "TrainMomentLog".ToLower() ||
                            x.Category.ToLower() == "GluedLog".ToLower() ||
                            x.Category.ToLower() == "SignalMoment".ToLower() ||
                            x.Category.ToLower() == "PointMachineMoment".ToLower() ||
                            x.Category.ToLower() == "TPRMoment".ToLower()
                            ).ToList();

                            if (tempProbablity != null && tempProbablity.Count > 0)
                            {
                                trackCount += tempProbablity.Where(x => x.AssetTypeId == trackId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                                signalCount += tempProbablity.Where(x => x.AssetTypeId == signalId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                                pmCount += tempProbablity.Where(x => x.AssetTypeId == pmId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                            }
                        }

                        var performance = thisSiteRecords.Performances.Where(x => x.SiteId == thisSiteRecords.Id && x.TimeStamp.Date == DateTime.Now.Date).ToList();
                        if (performance != null && performance.Count > 0)
                        {
                            site.Performances = performance;
                        }

                        if (trackCount > 0)
                        {
                            site.TrackCount = trackCount;
                        }

                        if (signalCount > 0)
                        {
                            site.SignalCount = signalCount;
                        }

                        if (pmCount > 0)
                        {
                            site.PointMachineCount = pmCount;
                        }
                    }
                }
            }

        }
    }
}