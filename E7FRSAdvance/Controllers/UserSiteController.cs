using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class UserSiteController : Controller
    {
        // GET: UserSite
        private readonly ISiteKeepingService siteKeepingService;
        private readonly IFRSAlertService frsAlertService;
        public UserSiteController(ISiteKeepingService siteKeepingService, IFRSAlertService frsAlertService)
        {
            this.siteKeepingService = siteKeepingService;
            this.frsAlertService = frsAlertService;
        }
        public ActionResult Index()
        {
            SiteLister mSiteLister = new SiteLister();
            GetAllZones();
            GetAllDivisions();

            mSiteLister.SearchCriteria.FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            mSiteLister.SearchCriteria.ToDate = DateTime.Now;

            return View(mSiteLister);
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            //mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSiteLister.SearchCriteria.IsCheckStatus = true;
            mSiteLister.SearchCriteria.IsActive = true;

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
                            SetSummaryViewCount(mSiteLister);
                            var mHooterSetting = GetSetting();
                            if (mHooterSetting != null)
                            {
                                mSiteLister.HooterSetting.DataInterval = mHooterSetting.DataInterval * 1000;
                                mSiteLister.HooterSetting.AudioDuration = mHooterSetting.AudioDuration;
                                mSiteLister.HooterSetting.ReminderFrequency = mHooterSetting.ReminderFrequency * 1000;
                                mSiteLister.HooterSetting.AlertSound = mHooterSetting.AlertSound;
                            }

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
                GetAllZones();
                GetAllDivisions();
            }


            return PartialView(mSiteLister);
        }

        public ActionResult _SummaryView(AssetGroup assetGroup)
        {
            //mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            List<AssetGroup> mAssetGroups = new List<AssetGroup>();
            var mSiteLister = new SiteLister();
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.Name = assetGroup.SiteName;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mSiteLister.SearchCriteria.FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            mSiteLister.SearchCriteria.ToDate = DateTime.Now;
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
                            mAssetGroups = SetAssetGroup(mSiteLister, assetGroup);

                            //foreach (var mSite in mSiteLister.mSites)
                            //{
                            //    if (assetGroup.AssetTypeId > 0)
                            //    {

                            //    }
                            //}

                        }
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

            return PartialView(mAssetGroups);
        }

        public void GetAllZones()
        {
            List<Domain.Zone> mZones = new List<Domain.Zone>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetAllZones")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mZones = JsonConvert.DeserializeObject<List<Domain.Zone>>(jsonString);
                        mZones.Insert(0, new Domain.Zone { Id = 0, Name = "Select Zone" });
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

        public ActionResult DownloadRosterPDF(int siteId)
        {
            string filePath = string.Empty;
            var mRosters = GetAllRoster(siteId);
            if (mRosters != null && mRosters.Count > 0)
            {
                var roster = mRosters.LastOrDefault();

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/DownloadRosterPDF/{0}/{1}", roster.Id, siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        filePath = JsonConvert.DeserializeObject<string>(jsonString);
                    }
                }

            }

            return Json(filePath, JsonRequestBehavior.AllowGet);
        }

        public List<Roster> GetAllRoster(int siteId)
        {
            List<Roster> rosters = new List<Roster>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Maintenance/GetAllRoster/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        rosters = JsonConvert.DeserializeObject<List<Roster>>(jsonString);
                        if (rosters != null && rosters.Count > 0)
                        {
                            foreach (var item in rosters)
                            {
                                item.SiteId = siteId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return rosters;
        }

        public ActionResult LoadPerformance(int siteId)
        {
            List<AnalyticsLog> mAnalyticsLogs = new List<AnalyticsLog>();
            try
            {
                var filePath = DownloadAlertAnalytics(siteId);
                if (filePath.IsNotNullOrEmpty())
                {
                    filePath = ConfigurationManager.AppSettings.Get("FileBaseUrl") + filePath;

                    string tr1 = "";
                    using (WebClient client = new WebClient())
                    {
                        tr1 = client.DownloadString(filePath);
                    }

                    if (tr1.IsNotNullOrEmpty())
                    {
                        //read
                        var arrayPfLogs = tr1.Replace("\r", "").Split('\n').ToList();
                        if (arrayPfLogs != null && arrayPfLogs.Count() > 0)
                        {
                            List<string> mBlocks = arrayPfLogs.Where(x => x.StartsWith("[") && x.EndsWith("]")).ToList();
                            if (mBlocks != null)
                            {

                                int i = 1;
                                foreach (var mBlock in mBlocks)
                                {
                                    AnalyticsLog mAnalyticsLog = new AnalyticsLog();

                                    var idx = arrayPfLogs.FindIndex(x => x.Contains(mBlock));
                                    if (idx > -1)
                                        idx++;

                                    int nextidx = 0;
                                    if (i < mBlocks.Count)
                                    {
                                        nextidx = arrayPfLogs.FindIndex(x => x.Contains(mBlocks[i]));
                                        if (nextidx > -1)
                                            nextidx--;
                                    }

                                    List<string> datas = new List<string>();
                                    if (idx > 0)
                                    {
                                        if (nextidx > 0)
                                        {
                                            int take = nextidx - idx;
                                            datas = arrayPfLogs.Skip(idx).Take(take).Select(s => s.Trim()).ToList();
                                        }
                                        else
                                        {
                                            datas = arrayPfLogs.Skip(idx).Select(s => s.Trim()).ToList();
                                        }

                                    }

                                    mAnalyticsLog.BlockName = mBlock.Replace("[", "").Replace("]", "");
                                    mAnalyticsLog.BlockLines = datas.Where(x => x != "").ToList();

                                    i++;

                                    mAnalyticsLogs.Add(mAnalyticsLog);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView("_Analysis", mAnalyticsLogs);
        }

        public ActionResult Summary(int siteId)
        {
            var msite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite != null && mSite.Id > 0)
                        {
                            ViewBag.SiteName = mSite.Name;
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
            finally
            {
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                ViewBag.Years = new SelectList(GetYears());
            }
            ViewBag.SiteId = siteId;

            return View(msite);
        }

        public string DownloadAlertAnalytics(int siteId)
        {
            string filePath = string.Empty;

            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync(String.Format("Maintenance/DownloadAlertAnalytics/{0}", siteId)).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    filePath = JsonConvert.DeserializeObject<string>(jsonString);
                }
            }

            return filePath;
        }

        public ActionResult ApplyMaintenanceInput(MaintenanceInput mMaintenanceInput)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceInput);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceInput"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Updated." };
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                        data = new { type = "Error", result = "Something went wrong!" };
                    else
                        data = new { type = "Error", result = "Something went wrong!" };

                }
            }
            catch (Exception)
            {
                data = new { type = "Error", result = "Something went wrong!" };
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        #region Predictive Alert

        public ActionResult _TrainRunningMoment(TrainRunningMomentLister mTrainRunningMomentLister)
        {
            try
            {
                mTrainRunningMomentLister = siteKeepingService.TrainRunningMoment(mTrainRunningMomentLister);

                //Bind Probability
                var mMomentLister = new MomentLister();
                mMomentLister.SearchCriteria.SiteId = mTrainRunningMomentLister.SearchCriteria.SiteId;
                mMomentLister.SearchCriteria.TimeStamp = mTrainRunningMomentLister.SearchCriteria.TimeStamp;
                BindProbabilityAssets(mMomentLister, "TrainMomentLog");
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mTrainRunningMomentLister);
        }

        public ActionResult _GluedLogDataLister(GluedLogDataLister mGluedLogDataLister)
        {
            try
            {
                mGluedLogDataLister = siteKeepingService.GluedLogDataLister(mGluedLogDataLister);


                //Bind Probability
                var mMomentLister = new MomentLister();
                mMomentLister.SearchCriteria.SiteId = mGluedLogDataLister.SearchCriteria.SiteId;
                mMomentLister.SearchCriteria.TimeStamp = mGluedLogDataLister.SearchCriteria.TimeStamp;
                BindProbabilityAssets(mMomentLister, "GluedLog");
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return PartialView(mGluedLogDataLister);
        }

        public ActionResult _IndexAlertLogLister(IndexAlertLogLister mIndexAlertLogLister)
        {
            try
            {
                mIndexAlertLogLister = siteKeepingService.IndexAlertLogLister(mIndexAlertLogLister);

                //Bind Probability
                var mMomentLister = new MomentLister();
                mMomentLister.SearchCriteria.SiteId = mIndexAlertLogLister.SearchCriteria.SiteId;
                mMomentLister.SearchCriteria.TimeStamp = mIndexAlertLogLister.SearchCriteria.TimeStamp;
                BindProbabilityAssets(mMomentLister, mIndexAlertLogLister.SearchCriteria.Remarks);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return PartialView(mIndexAlertLogLister);
        }

        public ActionResult _EarthFaultLister(SMSLogLister mSMSLogLister)
        {
            List<Asset> mEarthFaults = new List<Asset>();
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;

            mSMSLogLister.SearchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.Earth_Fault;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            mSMSLogLister.Pager.Take = -1;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null)
            {
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
                                var mSMSLogs = mSMSLogLister.mSMSLogs;
                                foreach (var mSMSLog in mSMSLogs)
                                {
                                    if (mSMSLog != null && mSMSLog.Message.IsNotNullOrEmpty())
                                    {
                                        var msg = GetAssetAndAttribute(mSMSLog.Message);
                                        if (msg.IsNotNullOrEmpty())
                                        {
                                            var spitMsg = msg.Split(' ');
                                            if (spitMsg != null && spitMsg.Count() > 1)
                                            {
                                                string attributeName = spitMsg.LastOrDefault().Trim();
                                                if (attributeName.IsNotNullOrEmpty())
                                                {
                                                    string assetName = msg.Replace(attributeName, "").Trim();
                                                    string name = $"{assetName} ({attributeName})";

                                                    if (mEarthFaults != null && mEarthFaults.Where(x => x.Name == name && x.SiteId == mSMSLogLister.SearchCriteria.SiteId).Count() == 0)
                                                        mEarthFaults.Add(new Asset() { Name = name, SiteId = mSMSLogLister.SearchCriteria.SiteId });
                                                }
                                            }
                                        }
                                    }
                                }
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
            }

            return PartialView(mEarthFaults);
        }

        public ActionResult _AxleCounterLister(WatchListLister mWatchListLister)
        {
            try
            {
                mWatchListLister = siteKeepingService.AxleCounterLister(mWatchListLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }

            return PartialView(mWatchListLister);
        }

        public ActionResult _SignalMomentLister(MomentLister mMomentLister)
        {
            try
            {
                mMomentLister = siteKeepingService.SignalMomentLister(mMomentLister);

                //Bind Probability
                BindProbabilityAssets(mMomentLister, "SignalMoment");
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mMomentLister);
        }

        public ActionResult _PointIndicationLister(MomentLister mMomentLister)
        {
            try
            {
                mMomentLister = siteKeepingService.PointIndicationLister(mMomentLister);

                //Bind Probability
                BindProbabilityAssets(mMomentLister, "PointMachineMoment");
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mMomentLister);
        }

        public ActionResult _TPRMomentLister(MomentLister mMomentLister)
        {
            try
            {
                mMomentLister = siteKeepingService.TPRMomentLister(mMomentLister);

                //Bind Probability
                BindProbabilityAssets(mMomentLister, "TPRMoment");
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mMomentLister);
        }

        public ActionResult _AlertLogLister(SMSLogLister mSMSLogLister)
        {
            try
            {
                if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null)
                {
                    if (mSMSLogLister.SearchCriteria.FromDate == null || mSMSLogLister.SearchCriteria.FromDate == DateTime.MinValue)
                    {
                        mSMSLogLister.SearchCriteria.FromDate = DateTime.Now;
                    }

                    if (mSMSLogLister.SearchCriteria.ToDate == null || mSMSLogLister.SearchCriteria.ToDate == DateTime.MinValue)
                    {
                        mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
                    }
                }
                mSMSLogLister = siteKeepingService.GetAlertLogLister(mSMSLogLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error!";
            }

            return PartialView(mSMSLogLister);
        }

        public ActionResult _PerformanceLister(PerformanceLister mPerformanceLister)
        {
            try
            {
                mPerformanceLister = siteKeepingService.PerformanceLister(mPerformanceLister);

            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            return PartialView(mPerformanceLister);
        }


        #endregion


        public ActionResult UserHistory(int siteId)
        {
            var mAppAccessLister = new AppAccessLister();
            mAppAccessLister.SearchCriteria.SiteId = siteId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite != null && mSite.Id > 0)
                        {
                            ViewBag.SiteName = mSite.Name;
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

            return View(mAppAccessLister);
        }

        public ActionResult _UserHistoryLister(AppAccessLister mAppAccessLister)
        {
            if (mAppAccessLister != null && mAppAccessLister.SearchCriteria != null && mAppAccessLister.SearchCriteria.StartTime != null && mAppAccessLister.SearchCriteria.StartTime == DateTime.MinValue)
                mAppAccessLister.SearchCriteria.StartTime = DateTime.Now;

            mAppAccessLister.Pager.Take = -1;
            if (mAppAccessLister != null && mAppAccessLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAppAccessLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("AppAccess/GetAllLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAppAccessLister = JsonConvert.DeserializeObject<AppAccessLister>(jsonString);

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
            }

            return PartialView(mAppAccessLister);
        }

        public ActionResult GetSMSLogList(List<int> siteIds)
        {
            var smsSiteIds = new List<int>();

            //var mSMSLogLister = new SMSLogLister();
            var mHooterSetting = GetSetting();
                   

            try
            {
                Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();
                if (mHooterSetting != null)
                    mFRSAlertLister.SearchCriteria.FromDate = DateTime.Now.AddMinutes(-mHooterSetting.LastMinuteData);

                mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;
                mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
                mFRSAlertLister = frsAlertService.GetListerWithTimeFilter(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    var oldSMSLogs = TempData.Peek($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}") as List<FRSAlert>;
                    if (oldSMSLogs != null && oldSMSLogs.Count > 0)
                    {
                        var smsIds = oldSMSLogs.Select(x => x.Id).ToList();
                        mFRSAlertLister.mFRSAlerts = mFRSAlertLister.mFRSAlerts.Where(x => !smsIds.Contains(x.Id)).ToList();
                        oldSMSLogs.AddRange(mFRSAlertLister.mFRSAlerts);
                        TempData[$"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}"] = oldSMSLogs;
                        TempData.Keep();
                    }
                    else
                    {
                        TempData.Remove($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}");
                        TempData[$"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}"] = mFRSAlertLister.mFRSAlerts;
                        TempData.Keep();
                    }
                    var groupedCustomerList = mFRSAlertLister.mFRSAlerts.GroupBy(u => u.SiteId).Select(grp => grp.Key).ToList();
                    if (groupedCustomerList != null && groupedCustomerList.Count > 0)
                    {
                        foreach (var siteId in groupedCustomerList)
                        {
                            if (smsSiteIds.Where(x => x == siteId).Count() <= 0)
                                smsSiteIds.Add(siteId);
                        }
                    }

                }
                else
                {
                    TempData.Remove($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}");
                }
            }
            catch (Exception)
            {

            }
            //return Json(mSMSLogLister, JsonRequestBehavior.AllowGet);

            var jsonResult = Json(smsSiteIds, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult _SMSLogLister(int siteId)
        {
            
            var oldFRSAlerts = TempData.Peek($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}") as List<FRSAlert>;
            if (oldFRSAlerts != null && oldFRSAlerts.Count > 0)
            {
                ViewBag.OldFRSAlerts = oldFRSAlerts.Where(x => x.SiteId == siteId).ToList();
            }


            //try
            //{
            //    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            //    {
            //        var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
            //        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
            //        var response = hcf.client.PostAsync(String.Format("SMSLog/GetAllSMSLogLister"), str).Result;
            //        if (response.StatusCode == HttpStatusCode.OK)
            //        {
            //            string jsonString = response.Content.ReadAsStringAsync().Result;
            //            mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

            //        }

            //    }
            //}
            //catch (Exception)
            //{
            //}
            return PartialView();
        }

        public JsonResult GenerateSummaryPdf(Summary mSummary)
        {
            Byte[] res = null;
            string base64 = string.Empty;
            string path = HttpContext.Server.MapPath("~/Template/Summary.html");
            FileReader fileReader = new FileReader(path);
            string pdfTemplate = fileReader.Read();

            if (mSummary != null)
            {
                if (mSummary.Date.IsNullOrEmpty())
                    mSummary.Date = DateTime.Now.ToShortDateString();

            }

            pdfTemplate = pdfTemplate.Replace("{%Sitename%}", mSummary.SiteName);
            pdfTemplate = pdfTemplate.Replace("{%Date%}", mSummary.Date);
            pdfTemplate = pdfTemplate.Replace("{%tbodyImageData%}", "<img  width='730' src='" + mSummary.ImageBase64 + "' >");
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    var pdf = TheArtOfDev.HtmlRenderer.PdfSharp.PdfGenerator.GeneratePdf(pdfTemplate, PdfSharp.PageSize.A3, 5);
                    pdf.Save(ms);
                    res = ms.ToArray();
                    if (res != null && res.Length > 0)
                        base64 = Convert.ToBase64String(res);

                }

            }
            catch (Exception ex)
            {
            }
            return Json(base64, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public PartialViewResult Setting()
        {
            return PartialView("_Setting", GetSetting());
        }

        [HttpPost]
        public PartialViewResult _Setting(Domain.HooterSetting mHooterSetting)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    if (mHooterSetting.DataInterval != 0 && mHooterSetting.LastMinuteData != 0 && mHooterSetting.AudioDuration != 0 && mHooterSetting.ReminderFrequency != 0)
                    {
                        HttpCookie hooterSettingCookie = new HttpCookie("HooterSettingCookies");
                        //Set the Cookie value.

                        if ((hooterSettingCookie != null) && (hooterSettingCookie.Value != ""))
                        {
                            Response.Cookies["DataInterval"].Value = Convert.ToString(mHooterSetting.DataInterval);
                            Response.Cookies["LastMinuteData"].Value = Convert.ToString(mHooterSetting.LastMinuteData);
                            Response.Cookies["AlertSound"].Value = Convert.ToString(mHooterSetting.AlertSound);
                            Response.Cookies["AudioDuration"].Value = Convert.ToString(mHooterSetting.AudioDuration);
                            Response.Cookies["ReminderFrequency"].Value = Convert.ToString(mHooterSetting.ReminderFrequency);
                            Response.Cookies.Add(hooterSettingCookie);

                            ViewBag.Type = "Success";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return PartialView("_Setting", mHooterSetting);

        }

        public ActionResult _FailureAlert(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mSMSLogLister.SearchCriteria.FromDate = DateTime.Now;
                mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
            }
            else
            {
                mSMSLogLister.SearchCriteria.FromDate = mSMSLogLister.SearchCriteria.TimeStamp;
                mSMSLogLister.SearchCriteria.ToDate = mSMSLogLister.SearchCriteria.TimeStamp;
            }

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
            GetAllDivisions();
            return PartialView(mSMSLogLister);
        }

        public ActionResult _AlertList(SMSLogLister mSMSLogLister)
        {
            try
            {
                mSMSLogLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-1);
                mSMSLogLister.SearchCriteria.ToDate = DateTime.Now.AddDays(-1);
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMSLog/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);

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
            return PartialView(mSMSLogLister);
        }

        #region DataLogger
        public ActionResult DataLogger(int siteId)
        {
            var msite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSiteById/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        msite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);

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
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                ViewBag.Years = new SelectList(GetYears());
            }

            return View(msite);
        }

        public ActionResult _GetDataLoggerAssetType(int siteId)
        {
            List<Domain.DataloggerAssetType> mAssetType = new List<Domain.DataloggerAssetType>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAssetType/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAssetType = JsonConvert.DeserializeObject<List<Domain.DataloggerAssetType>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mAssetType);
        }

        public ActionResult _DataLogger(int siteId, int assetTypeId, string searchDate = null)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            if (searchDate.IsNullOrEmpty())
                searchDate = DateTime.Now.ToString("d-M-yyyy");


            ViewBag.SiteId = siteId;
            ViewBag.AssetTypeId = assetTypeId;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAsset/GetGraphData/{0}/{1}", siteId, searchDate.Replace("/", "-"))).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                        //if (mDataloggers != null && mDataloggers.Count > 0)
                        //{
                        //    mDataloggers = mDataloggers.Where(x => x.AssetTypeId == assetTypeId).ToList();

                        //    var mDataloggerAssetLister = new DataloggerAssetLister();
                        //    mDataloggerAssetLister.SearchCriteria.SiteId = siteId;
                        //    mDataloggerAssetLister.SearchCriteria.DataloggerAssetTypeId = assetTypeId;
                        //    mDataloggerAssetLister.Pager.Take = -1;
                        //    mDataloggerAssetLister = GetDataloggerAssetType(mDataloggerAssetLister);
                        //    if (mDataloggers != null)
                        //    {
                        //        foreach (var mDatalogger in mDataloggers)
                        //        {
                        //            string assetName = string.Empty;
                        //            var nameSplit = mDatalogger.AssetName.Split(' ');
                        //            if (nameSplit != null)
                        //            {
                        //                assetName = nameSplit.FirstOrDefault();
                        //            }
                        //            var dsd = mDataloggerAssetLister.DataloggerAssets.Where(x => x.Name == assetName).FirstOrDefault();
                        //            if (dsd != null && dsd.Id > 0)
                        //            {
                        //                var mDataloggerAttribute = dsd.mDataloggerAttributes.Where(x => x.Id == mDatalogger.AttributeId).FirstOrDefault();
                        //                if (mDataloggerAttribute != null && mDataloggerAttribute.Id > 0)
                        //                {
                        //                    mDatalogger.SrNo = mDataloggerAttribute.SrNo;
                        //                }
                        //            }
                        //        }
                        //    }
                        //}



                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mDataloggers);
        }

        public ActionResult _DataloggerAsset(int siteId, int assetId, string searchDate = null)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            if (searchDate.IsNullOrEmpty())
                searchDate = DateTime.Now.ToString("d-M-yyyy");

            ViewBag.SiteId = siteId;
            ViewBag.AssetId = assetId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("DataloggerAsset/GetAssetGraphData/{0}/{1}/{2}", siteId, assetId, searchDate.Replace("/", "-"))).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView("_DataLogger", mDataloggers);
        }

        public ActionResult _DataLoggerJsonView(int siteId, int assetTypeId)
        {
            var mDataloggerAssetLister = new DataloggerAssetLister();
            mDataloggerAssetLister.Pager.Take = -1;
            mDataloggerAssetLister.SearchCriteria.SiteId = siteId;
            mDataloggerAssetLister.SearchCriteria.DataloggerAssetTypeId = assetTypeId;
            mDataloggerAssetLister = GetDataloggerAssetType(mDataloggerAssetLister);
            return PartialView(mDataloggerAssetLister);
        }

        public ActionResult _GetDataLoggerAssetDetails(DataloggerAssetLister mAssetLister)
        {
            mAssetLister.Pager.Take = -1;// mAssetLister.Pager.PageSize;

            mAssetLister = GetDataloggerAssetType(mAssetLister);
            return PartialView(mAssetLister);
        }

        public DataloggerAssetLister GetDataloggerAssetType(DataloggerAssetLister mAssetLister)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<DataloggerAssetLister>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return mAssetLister;
        }



        #endregion

        #region Consolidated

        public ActionResult _ConsolidatedLister(ConsolidatedLister mConsolidatedLister)
        {
            //mConsolidatedList.Pager.Take = -1;
            //if (mConsolidatedList != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            //    mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);

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
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                ViewBag.Years = new SelectList(GetYears());
            }
            return PartialView(mConsolidatedLister);
        }


        public ActionResult _ConsolidatedAlertList(ConsolidatedLister mConsolidatedLister)
        {
            //mConsolidatedList.Pager.Take = -1;
            //if (mConsolidatedList != null && mProbabilityLister.SearchCriteria != null && mProbabilityLister.SearchCriteria.TimeStamp != null && mProbabilityLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            //    mProbabilityLister.SearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);
                        if (mConsolidatedLister != null && mConsolidatedLister.mSMSLogs != null && mConsolidatedLister.mSMSLogs.Count > 0)
                        {
                            TempData["tempConsolidatedAlert"] = mConsolidatedLister.mSMSLogs;
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
            return PartialView(mConsolidatedLister);
        }


        public ActionResult _ConsolidatedProbabilityList(ConsolidatedLister mConsolidatedLister)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mConsolidatedLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("ConsolidatedReport/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mConsolidatedLister = JsonConvert.DeserializeObject<ConsolidatedLister>(jsonString);
                        if (mConsolidatedLister != null && mConsolidatedLister.Probability != null && mConsolidatedLister.Probability.Count > 0)
                        {
                            ChangeProbabilityName(mConsolidatedLister.Probability);
                            TempData["tempConsolidatedProbability"] = mConsolidatedLister.Probability;
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
            return PartialView(mConsolidatedLister);
        }
        #endregion

        #region Probability

        public ActionResult _ProbabilityList(SearchCriteria mSearchCriteria)
        {
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetProbability"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var dt = JsonConvert.DeserializeObject<DataTable>(jsonString);
                        if (dt != null)
                        {
                            ViewBag.Alert = GetProbabilityAlert(mSearchCriteria);
                            ViewBag.Probability = GetProbability(mSearchCriteria);
                            mSearchCriteria.DataTable = dt;
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
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
                ViewBag.Years = new SelectList(GetYears());
            }

            return PartialView(mSearchCriteria);
        }

        public ActionResult DownloadProbabilityList(SearchCriteria mSearchCriteria)
        {
            string base64 = string.Empty;
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/DownloadProbability"), str).Result;
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
            finally
            {
            }

            return Json(base64, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _ProbabilityAlertList(SearchCriteria mSearchCriteria)
        {
            return PartialView(GetProbabilityAlert(mSearchCriteria));
        }

        public ActionResult _ProbabilityCSVList(SearchCriteria mSearchCriteria)
        {
            return PartialView(GetProbability(mSearchCriteria));
        }

        public SMSLogLister GetProbabilityAlert(SearchCriteria mSearchCriteria)
        {
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            var mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mSMSLogLister.SearchCriteria.FromDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 1);
            mSMSLogLister.SearchCriteria.ToDate = mSMSLogLister.SearchCriteria.FromDate.AddMonths(1).AddDays(-1);
            mSMSLogLister.Pager.Take = -1;
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
            return mSMSLogLister;
        }

        public ProbabilityLister GetProbability(SearchCriteria mSearchCriteria)
        {
            var mProbabilityLister = new ProbabilityLister();

            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            mProbabilityLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mProbabilityLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mProbabilityLister.SearchCriteria.StartDate = new DateTime(mSearchCriteria.TimeStamp.Year, mSearchCriteria.TimeStamp.Month, 1);
            mProbabilityLister.SearchCriteria.EndDate = mProbabilityLister.SearchCriteria.StartDate.AddMonths(1).AddDays(-1);
            mProbabilityLister.Pager.Take = -1;
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
                            ChangeProbabilityName(mProbabilityLister.Probability);

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

            return mProbabilityLister;
        }

        public ActionResult GetProbabilityCluster(SearchCriteria mSearchCriteria)
        {
            List<AICluster> mAICluster = new List<AICluster>();
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetCluster"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAICluster = JsonConvert.DeserializeObject<List<AICluster>>(jsonString);
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
                ViewBag.Months = new SelectList(DateTimeFormatInfo.CurrentInfo.MonthNames.ToList());
            }

            return PartialView("_ProbabilityCluster", mAICluster);
        }

        public ActionResult GetProbabilityClusterCount(SearchCriteria mSearchCriteria)
        {
            List<AIClusterCount> mAICluster = new List<AIClusterCount>();
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetClusterCount"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAICluster = JsonConvert.DeserializeObject<List<AIClusterCount>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {

            }


            return Json(mAICluster);
        }

        public ActionResult GetProbabilityMain(SearchCriteria mSearchCriteria)
        {
            DataTable dt = new DataTable();
            if (mSearchCriteria.Month.IsNotNullOrEmpty() && mSearchCriteria.Year.IsNotNullOrEmpty())
                mSearchCriteria.TimeStamp = DateTime.ParseExact($"{mSearchCriteria.Month}/{mSearchCriteria.Year}", "MMMM/yyyy", CultureInfo.CurrentCulture);
            else
                mSearchCriteria.TimeStamp = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AISection/GetMainDF"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        dt = JsonConvert.DeserializeObject<DataTable>(jsonString);
                        ViewBag.Week = mSearchCriteria.Week;
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
                dt.Rows.Clear();
            }

            return PartialView("_ProbabilityMain", dt);
        }

        public void ChangeProbabilityName(List<Probability> mProbabilites)
        {
            try
            {
                if (mProbabilites != null && mProbabilites.Count > 0)
                {
                    foreach (var mProbability in mProbabilites)
                    {
                        var strings = mProbability.Category.Split('(');
                        if (strings != null && strings.Count() > 0)
                        {
                            var firstString = strings[0];
                            if (firstString == "SignalMoment")
                            {
                                firstString = "Unstable Current";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "TPRMoment")
                            {
                                firstString = "Unstable Voltage";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "PointMachineMoment")
                            {
                                firstString = "IRegular Signature";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }

                            if (firstString == "TrainMomentLog")
                            {
                                firstString = "Prone To Shorting";
                                mProbability.Category = $"{firstString}";
                                if (strings.Count() > 1)
                                {
                                    var secondString = strings[1];
                                    if (secondString.IsNotNullOrEmpty())
                                        mProbability.Category += $"({secondString}";
                                }
                            }



                        }

                    }
                }

            }
            catch (Exception ex)
            {
            }
        }
        #endregion

        #region Private Method

        private string GetAssetAndAttribute(string msg)
        {
            try
            {
                var index = msg.IndexOf("detected on");
                var f1 = msg.Substring(0, index);
                msg = msg.Replace(f1, "");

                var index1 = msg.IndexOf("\n");
                var f2 = msg.Substring(index1);
                msg = msg.Replace(f2, "");

                msg = msg.Replace("detected on", "").Trim();
            }
            catch (Exception ex)
            {
            }
            return msg;
        }

        private HooterSetting GetSetting()
        {
            HooterSetting mHooterSetting = new HooterSetting();
            HttpCookie ckLoginUser = Request.Cookies["HooterSettingCookies"];
            if (ckLoginUser != null)
            {
                mHooterSetting.DataInterval = Convert.ToInt32(Request.Cookies["DataInterval"].Value);
                mHooterSetting.LastMinuteData = Convert.ToInt32(Request.Cookies["LastMinuteData"].Value);
                mHooterSetting.AlertSound = Convert.ToBoolean(Request.Cookies["AlertSound"].Value);
                mHooterSetting.AudioDuration = Convert.ToInt32(Request.Cookies["AudioDuration"].Value);
                mHooterSetting.ReminderFrequency = Convert.ToInt32(Request.Cookies["ReminderFrequency"].Value);
            }
            else
            {
                mHooterSetting.DataInterval = 30;
                mHooterSetting.LastMinuteData = 5;
                mHooterSetting.AlertSound = true;
                mHooterSetting.AudioDuration = 5;
                mHooterSetting.ReminderFrequency = 30;
            }

            return mHooterSetting;
        }

        private void BindProbabilityAssets(MomentLister mMomentLister, string category)
        {
            //Bind Probability
            var mProbabilityLister = new ProbabilityLister();
            mProbabilityLister.SearchCriteria.TimeStamp = mMomentLister.SearchCriteria.TimeStamp;
            mProbabilityLister.SearchCriteria.SiteId = mMomentLister.SearchCriteria.SiteId;
            mProbabilityLister.SearchCriteria.Category = category;
            mProbabilityLister = siteKeepingService.GetProbabilityLister(mProbabilityLister);
            if (mProbabilityLister != null && mProbabilityLister.Probability != null && mProbabilityLister.Probability.Count > 0)
            {
                var mAssets = new List<Domain.Asset>();
                var probabilityAssets = mProbabilityLister.Probability.GroupBy(x => new { x.AssetId, x.AssetName }).Select(x => new { x.Key.AssetId, x.Key.AssetName }).ToList();
                if (probabilityAssets != null && probabilityAssets.Count > 0)
                {
                    foreach (var probabilityAsset in probabilityAssets)
                    {
                        mAssets.Add(new Domain.Asset() { Id = probabilityAsset.AssetId, Name = probabilityAsset.AssetName });
                    }
                    ViewBag.Asset = mAssets;
                }

            }
        }

        private List<int> GetYears()
        {
            List<int> Years = new List<int>();
            DateTime startYear = DateTime.Now.AddYears(-3);
            while (startYear.Year <= DateTime.Now.Year)
            {
                Years.Add(startYear.Year);
                startYear = startYear.AddYears(1);
            }
            return Years;
        }

        private List<AssetGroup> SetAssetGroup(Domain.SiteLister mSiteLister, AssetGroup assetGroup)
        {
            List<AssetGroup> mAssetGroups = new List<AssetGroup>();
            if (mSiteLister != null && mSiteLister.mSites != null && mSiteLister.mSites.Count > 0)
            {
                foreach (var site in mSiteLister.mSites)
                {
                    var smsLogs = site.SMSLogs.Where(x => x.AssetTypeId == assetGroup.AssetTypeId).ToList();
                    if (smsLogs != null && smsLogs.Count > 0)
                    {
                        foreach (var item in smsLogs.GroupBy(x => x.AssetId))
                        {
                            var thisAssetLogs = item.ToList();
                            if (thisAssetLogs != null && thisAssetLogs.Count > 0)
                            {
                                var assetName = item.ToList().FirstOrDefault().AssetName;
                                mAssetGroups.Add(new AssetGroup() { AssetId = item.Key, AssetName = assetName, mSMSLogs = thisAssetLogs, SiteId = site.Id, SiteName = site.Name, AssetTypeId = assetGroup.AssetTypeId });
                            }
                        }
                    }

                    var prbability = site.Probabilities.Where(x => x.AssetTypeId == assetGroup.AssetTypeId).ToList();
                    if (prbability != null && prbability.Count > 0)
                    {
                        foreach (var item in prbability.GroupBy(x => x.AssetId))
                        {
                            var thisAssetLogs = item.ToList();
                            if (thisAssetLogs != null && thisAssetLogs.Count > 0)
                            {
                                var assetName = item.ToList().FirstOrDefault().AssetName;
                                if (mAssetGroups != null && mAssetGroups.Count > 0 && mAssetGroups.Where(x => x.AssetId == item.Key).Count() > 0)
                                {
                                    var selectedEntry = mAssetGroups.Where(x => x.AssetId == item.Key).FirstOrDefault();
                                    selectedEntry.mProbabilities = thisAssetLogs;
                                }
                                else
                                {
                                    mAssetGroups.Add(new AssetGroup() { AssetId = item.Key, AssetName = assetName, mProbabilities = thisAssetLogs, SiteId = site.Id, SiteName = site.Name, AssetTypeId = assetGroup.AssetTypeId });
                                }
                            }
                        }
                    }

                    //var performance = site.Performances.Where(x => x.AssetTypeId == assetGroup.AssetTypeId).ToList();
                    //if (performance != null && performance.Count > 0)
                    //{
                    //    foreach (var item in performance.GroupBy(x => x.AssetId))
                    //    {
                    //        var thisAssetLogs = item.ToList();
                    //        if (thisAssetLogs != null && thisAssetLogs.Count > 0)
                    //        {
                    //            var assetName = item.ToList().FirstOrDefault().AssetName;
                    //            if (mAssetGroups != null && mAssetGroups.Count > 0 && mAssetGroups.Where(x => x.AssetId == item.Key).Count() > 0)
                    //            {
                    //                var selectedEntry = mAssetGroups.Where(x => x.AssetId == item.Key).FirstOrDefault();
                    //                selectedEntry.mPerformances = thisAssetLogs;
                    //            }
                    //            else
                    //            {
                    //                mAssetGroups.Add(new AssetGroup() { AssetId = item.Key, AssetName = assetName, mPerformances = thisAssetLogs, SiteName = site.Name, AssetTypeId = assetGroup.AssetTypeId });
                    //            }
                    //        }
                    //    }
                    //}
                }

            }

            return mAssetGroups;
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
                    if (site.Id == 14)
                    {

                    }
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

                            trackCount += smsLogs.Where(x => x.AssetTypeId == trackId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
                            signalCount += smsLogs.Where(x => x.AssetTypeId == signalId).GroupBy(x => x.AssetId).Distinct().ToList().Count();
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

        //private Domain.FRSAlertLister GetFRSAlertList(Domain.FRSAlertLister mFRSAlertLister)
        //{
        //    try
        //    {
        //        mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
        //        mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
        //        mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
        //        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
        //        {
        //            var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
        //            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
        //            var response = hcf.client.PostAsync(String.Format("FRSAlert/GetListerWithTimeFilter"), str).Result;
        //            if (response.StatusCode == HttpStatusCode.OK)
        //            {
        //                string jsonString = response.Content.ReadAsStringAsync().Result;
        //                mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);

        //            }
        //            else if (response.StatusCode == HttpStatusCode.Forbidden)
        //            {
        //                ViewBag.Type = "Error";
        //                ViewBag.Message = "Forbidden!";
        //            }
        //            else
        //            {
        //                ViewBag.Type = "Error";
        //                ViewBag.Message = "Internal server error!";
        //            }
        //        }
        //    }
        //    catch (Exception)
        //    {
        //        ViewBag.Type = "Error";
        //        ViewBag.Message = "Something went wrong!";
        //    }
        //    return mFRSAlertLister;
        //}
    }

    #endregion


}