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
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class UserSiteController : Controller
    {
        private readonly ISiteKeepingService siteKeepingService;
        private readonly IFRSAlertService frsAlertService;

        public UserSiteController(ISiteKeepingService siteKeepingService, IFRSAlertService frsAlertService)
        {
            this.siteKeepingService = siteKeepingService;
            this.frsAlertService = frsAlertService;
        }
        // GET: FRS25/UserSite
        public ActionResult Index()
        {
            SiteLister mSiteLister = new SiteLister();
            GetAllZones();
            GetAllDivisions();

            mSiteLister.SearchCriteria.FromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            mSiteLister.SearchCriteria.ToDate = DateTime.Now;

            return View(mSiteLister);
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


        // ===== REPLACE your existing GetSMSLogList method in FRS25 Area UserSiteController =====

        [HttpPost]
        public JsonResult GetSMSLogList(List<int> siteIds)
        {
            var smsAlerts = new List<FRSAlert>();
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
                        // Filter out already-seen alerts
                        var smsIds = oldSMSLogs.Select(x => x.Id).ToList();
                        var newAlerts = mFRSAlertLister.mFRSAlerts.Where(x => !smsIds.Contains(x.Id)).ToList();

                        oldSMSLogs.AddRange(newAlerts);
                        TempData[$"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}"] = oldSMSLogs;
                        TempData.Keep();
                    }
                    else
                    {
                        oldSMSLogs = mFRSAlertLister.mFRSAlerts;
                        TempData.Remove($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}");
                        TempData[$"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}"] = oldSMSLogs;
                        TempData.Keep();
                    }

                    // CHANGED: return the full alert objects (scoped to requested siteIds) instead of bare site IDs
                    smsAlerts = oldSMSLogs
                        .Where(x => siteIds == null || siteIds.Count == 0 || siteIds.Contains(x.SiteId))
                        .OrderByDescending(x => x.SetTimeStamp)
                        .ToList();
                }
                else
                {
                    // No active alerts from API — clear TempData
                    TempData.Remove($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}");
                }
            }
            catch (Exception)
            {
            }

            var jsonResult = Json(smsAlerts, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        [HttpPost]
        public JsonResult GetAssetTelemetry(int siteId, int assetId)
        {
            var result = new { Values = new List<double>(), Unit = "" };
            try
            {
                if (assetId > 0)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        // TODO: point this at your real telemetry-history API/route
                        var response = hcf.client.GetAsync(string.Format("Telemetry/GetAssetHistory/{0}?hours=24", assetId)).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            // Expecting something like: [{ "Timestamp": "...", "Value": 12.3, "Unit": "V" }, ...]
                            var points = JsonConvert.DeserializeObject<List<dynamic>>(jsonString);
                            if (points != null && points.Count > 0)
                            {
                                var values = points.Select(p => (double)p.Value).ToList();
                                var unit = (string)(points[0].Unit ?? "");
                                result = new { Values = values, Unit = unit };
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

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


        // ===== ADD THIS ACTION to your FRS25 Area UserSiteController =====
        // File: Areas/FRS25/Controllers/UserSiteController.cs
        // Add inside the class body, after GetSMSLogList method

        [HttpPost]
        public JsonResult GetFRSAlertsBySite(int siteId)
        {
            var alerts = new List<FRSAlert>();
            try
            {
                var oldFRSAlerts = TempData.Peek($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}") as List<FRSAlert>;
                if (oldFRSAlerts != null && oldFRSAlerts.Count > 0)
                {
                    alerts = oldFRSAlerts
                        .Where(x => x.SiteId == siteId)
                        .OrderByDescending(x => x.SetTimeStamp)
                        .Take(4)
                        .ToList();
                }
            }
            catch (Exception)
            {
                // Silently fail
            }

            var jsonResult = Json(alerts, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        // ===== ADD THESE 3 ACTIONS to your FRS25 Area UserSiteController =====
        // File: Areas/FRS25/Controllers/UserSiteController.cs
        // Add inside the class body

        // 1. Timeline — returns recent FRS alerts for a site
        [HttpPost]
        public JsonResult GetTimelineAlerts(int siteId)
        {
            var alerts = new List<FRSAlert>();
            try
            {
                // First check TempData (live alerts from hooter polling)
                var tempAlerts = TempData.Peek($"TempSiteHooterFRSAlert{ClsHttpContent.LoginUser.Id}") as List<FRSAlert>;
                if (tempAlerts != null && tempAlerts.Count > 0)
                {
                    alerts = tempAlerts
                        .Where(x => x.SiteId == siteId)
                        .OrderByDescending(x => x.SetTimeStamp)
                        .Take(10)
                        .ToList();
                }

                // If TempData is empty, fetch from API
                if (alerts.Count == 0)
                {
                    Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();
                    mFRSAlertLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-1);
                    mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;
                    mFRSAlertLister.SearchCriteria.SiteId = siteId;
                    mFRSAlertLister = frsAlertService.GetListerWithTimeFilter(mFRSAlertLister);
                    if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null)
                    {
                        alerts = mFRSAlertLister.mFRSAlerts
                            .Where(x => x.SiteId == siteId)
                            .OrderByDescending(x => x.SetTimeStamp)
                            .Take(10)
                            .ToList();
                    }
                }
            }
            catch (Exception)
            {
            }

            // Map to a simplified object for JS
            var result = alerts.Select(a => new
            {
                AssetName = a.AssetName,
                AlertType = a.AlertType,
                AlertTypeId = a.AlertTypeId,
                CauseCode = a.CauseCode,
                SetTimeStamp = a.SetTimeStamp,
                ResetTimeStamp = a.ResetTimeStamp,
                Status = a.ResetTimeStamp.HasValue ? "Cleared" : "Active",
                PossibleCause = a.PossibleCause,
                Description = a.Description
            }).ToList();

            var jsonResult = Json(result, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        // 2. Performance — returns performance metrics for a site
        [HttpPost]
        public JsonResult GetPerformanceData(int siteId)
        {
            var result = new { FailureAccuracy = 0.0, PredictiveAccuracy = 0.0, RdpmsCoverage = 0.0 };
            try
            {
                var mPerformanceLister = new FRSAlertLister();
                mPerformanceLister.SearchCriteria.SiteId = siteId;
                mPerformanceLister = frsAlertService.GetListerWithPagination(mPerformanceLister);

                if (mPerformanceLister != null && mPerformanceLister.mFRSAlerts != null && mPerformanceLister.mFRSAlerts.Count > 0)
                {
                    var perf = mPerformanceLister.mFRSAlerts;

                    // Calculate accuracy percentages
                    // Adjust these calculations based on your actual Performance domain model
                    double failureTotal = perf.Where(x => x.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure).Count();
                    double failureTrue = perf.Where(x => x.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure && (x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True || x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.PT)).Count();
                    double predictiveTotal = perf.Where(x => x.AlertType == "Predictive").Count();
                    double predictiveTrue = perf.Where(x => x.AlertType == "Predictive" && (x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True || x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.PT)).Count();

                    result = new
                    {
                        FailureAccuracy = failureTotal > 0 ? Math.Round((failureTrue / failureTotal) * 100, 2) : 0.0,
                        PredictiveAccuracy = predictiveTotal > 0 ? Math.Round((predictiveTrue / predictiveTotal) * 100, 2) : 0.0,
                        RdpmsCoverage = failureTotal > 0 ? Math.Round((failureTrue / Math.Max(failureTotal, 1)) * 100, 2) : 0.0
                    };
                }
            }
            catch (Exception)
            {
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        // 3. User History — returns recent user activity for a site
        [HttpPost]
        public JsonResult GetUserHistoryData(int siteId)
        {
            var result = new List<object>();
            try
            {
                var mAppAccessLister = new AppAccessLister();
                mAppAccessLister.SearchCriteria.SiteId = siteId;
                mAppAccessLister.SearchCriteria.StartTime = DateTime.Now.AddDays(-7);
                mAppAccessLister.Pager.Take = 10;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAppAccessLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AppAccess/GetAllLister"), str).Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAppAccessLister = JsonConvert.DeserializeObject<AppAccessLister>(jsonString);

                        if (mAppAccessLister != null && mAppAccessLister.mAppAccess != null)
                        {
                            //result = mAppAccessLister.mUser
                            //    .OrderByDescending(x => x.StartTime)
                            //    .Select(a => (object)new
                            //    {
                            //        UserName = a.UserName ?? a.Name ?? "User",
                            //        Action = a.Remarks ?? a.Action ?? "",
                            //        StartTime = a.StartTime
                            //    })
                            //    .ToList();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            var jsonResult = Json(result, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public UserLister GetUserLister(UserLister mUserLister)
        {
            mUserLister.Pager.Take = -1;
            mUserLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.User;
            mUserLister.SearchCriteria.SiteKeepingSearch = false;

            if (mUserLister.SearchCriteria.FromDate == DateTime.MinValue)
                mUserLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-7);

            if (mUserLister.SearchCriteria.ToDate == DateTime.MinValue)
                mUserLister.SearchCriteria.ToDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetUserHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserLister = JsonConvert.DeserializeObject<UserLister>(jsonString);

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
            return mUserLister;
        }
    }
}