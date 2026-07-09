using Domain;
using Domain.Dto;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
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
        private readonly IAssetAttributeService assetAttributeService;

        public UserSiteController(ISiteKeepingService siteKeepingService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService)
        {
            this.siteKeepingService = siteKeepingService;
            this.frsAlertService = frsAlertService;
            this.assetAttributeService = assetAttributeService;
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
        public JsonResult GetAssetTelemetry(int siteId, int assetId, int frsAlertId = 0)
        {
            var series = new List<object>();
            string rangeLabel = "24h";
            try
            {
                if (assetId > 0)
                {
                    var fromDate = DateTime.Now.AddHours(-24);
                    var toDate = DateTime.Now;
                    string pmSortDirection = null; // set below only if this is a Point Machine tied to a real alert

                    Domain.FRSAlert mFRSAlert = null;
                    if (frsAlertId <= 0)
                    {
                        mFRSAlert = GetLastByAssetId(assetId);

                        if (mFRSAlert != null)
                            frsAlertId = mFRSAlert.Id;
                    }
                    if (frsAlertId > 0)
                    {
                        mFRSAlert = GetFRSAlertById(frsAlertId);
                        if (mFRSAlert != null && mFRSAlert.Id > 0)
                        {
                            // Window tightly around the actual incident instead of a generic
                            // last-24h — same intent as AlertLiveController.GetTelemetryHistoryData.
                            fromDate = mFRSAlert.SetTimeStamp.AddMinutes(-30);
                            toDate = mFRSAlert.ResetTimeStamp.HasValue ? mFRSAlert.ResetTimeStamp.Value.AddMinutes(30) : DateTime.Now;
                            rangeLabel = fromDate.ToString("h:mm tt") + " – " + toDate.ToString("h:mm tt");

                            if (!string.IsNullOrEmpty(mFRSAlert.CauseCode))
                                pmSortDirection = mFRSAlert.CauseCode.ToUpper().Contains(" R ") ? "reverse" : "normal";
                        }
                    }
                    else
                    {

                    }

                    Asset mAsset = new Asset
                    {
                        Id = assetId,
                        SiteId = siteId,
                        SortDirection = "Graph", // request mode flag expected by Asset/GenerateGraph
                        IsGraphLoad = true,
                        StartDate = fromDate.ToShortDateString(),
                        StartTime = fromDate.ToShortTimeString(),
                        EndDate = toDate.ToShortDateString(),
                        EndTime = toDate.ToShortTimeString()
                    };
                    var mAssets = new List<Asset> { mAsset };

                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync("Asset/GenerateGraph", content).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                            var asset = assets != null ? assets.FirstOrDefault() : null;

                            if (asset != null)
                            {
                                // Point Machine reverse/normal cause-code logic, ported from
                                // AlertLiveController.GetTelemetryHistoryData — only meaningful
                                // once we actually know which alert this graph belongs to.
                                if (pmSortDirection != null && asset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                                    asset.SortDirection = pmSortDirection;

                                if (asset.assetAttributes != null && asset.assetAttributes.Count > 0
                                    && asset.MultipleLog != null && asset.MultipleLog.Count > 0)
                                {
                                    var mAllAssetAttributes = assetAttributeService.GetAll()
                                        .Where(x => x.AssetTypeId == asset.AssetTypeId)
                                        .ToList();

                                    var valuesByAttrId = asset.assetAttributes.ToDictionary(a => a.Id, a => new List<Tuple<DateTime, double>>());
                                    var titleByAttrId = asset.assetAttributes.ToDictionary(a => a.Id, a => a.Title);

                                    foreach (var log in asset.MultipleLog.OrderBy(x => x.TimeStamp))
                                    {
                                        if (log == null || log.TimeStamp == DateTime.MinValue || string.IsNullOrEmpty(log.CsvData))
                                            continue;

                                        var rowAttrs = assetAttributeService.GetGraphAttribute(
                                            asset.assetAttributes.Select(a => new AssetAttribute
                                            {
                                                Id = a.Id,
                                                Title = a.Title,
                                                AssetTypeId = a.AssetTypeId,
                                                AssetTypeName = a.AssetTypeName,
                                                Data = new List<string>()
                                            }).ToList(),
                                            mAllAssetAttributes,
                                            log.CsvData);

                                        foreach (var attr in rowAttrs)
                                        {
                                            var raw = attr.Data != null ? attr.Data.FirstOrDefault() : null;
                                            double val;
                                            if (raw != null && double.TryParse(raw, out val) && valuesByAttrId.ContainsKey(attr.Id))
                                                valuesByAttrId[attr.Id].Add(Tuple.Create(log.TimeStamp, val));
                                        }
                                    }

                                    foreach (var kv in valuesByAttrId)
                                    {
                                        if (kv.Value.Count < 2) continue;
                                        series.Add(new
                                        {
                                            Name = titleByAttrId[kv.Key],
                                            Unit = "",
                                            Labels = kv.Value.Select(p => p.Item1.ToString("h:mm tt")).ToList(),
                                            Values = kv.Value.Select(p => p.Item2).ToList()
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }

            return Json(new { Series = series, RangeLabel = rangeLabel }, JsonRequestBehavior.AllowGet);
        }

        // Same direct REST call AlertLiveController.GetFRSAlert uses
        private Domain.FRSAlert GetFRSAlertById(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("FRSAlert/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                        mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString) ?? new Domain.FRSAlert();
                }
            }
            catch (Exception) { }
            return mFRSAlert;
        }

        private Domain.FRSAlert GetLastByAssetId(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("FRSAlert/GetLastByAssetId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                        mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString) ?? new Domain.FRSAlert();
                }
            }
            catch (Exception) { }
            return mFRSAlert;
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

        // Mirrors HealthController.GetMQTTDetailList — gives the browser the broker
        // credentials it needs for the live MQTT connection (see Index.cshtml connectHealthMqtt()).
        [HttpGet]
        public JsonResult GetMQTTDetail()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetMQTTDetail").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var detail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
                        return Json(detail?.mQTTDetailWeb, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception) { }
            return Json(null, JsonRequestBehavior.AllowGet);
        }

        // Called by SiteList.cshtml's fnLoadSlideData() to populate the Health tab.
        // Hardware/Fixes come from the same A10Status + CardLine APIs the Health module
        // uses; Svc is a bootstrap value shown until the first live MQTT packet arrives
        // (see hBuildSvcForSite() in Index.cshtml, which then takes over).
        private static readonly Dictionary<string, string> SvcKeyToSourceId = new Dictionary<string, string>
{
    { "DataRecv",    "DataReceiverHealth" },
    { "Datalogger",  "DataloggerHealth"   },
    { "Alert",       "AlertHealth"        },
    { "Debouncer",   "DebouncerHealth"    },
    { "Point",       "PointMachineHealth" }
};

        [HttpPost]
        public JsonResult GetSiteHealth(int siteId)
        {
            var svc = new Dictionary<string, object>();
            var hardware = new Dictionary<string, object>();
            int fixesCount = 0;

            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                if (!string.IsNullOrWhiteSpace(baseUrl))
                {
                    baseUrl = baseUrl.TrimEnd('/');
                    string apiUrl = string.Format("{0}/api/AlertLive?siteId={1}&alertState=Active", baseUrl, siteId);

                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                        var resp = client.GetAsync(apiUrl).Result;
                        if (resp.StatusCode == HttpStatusCode.OK)
                        {
                            string json = resp.Content.ReadAsStringAsync().Result;
                            var arr = Newtonsoft.Json.Linq.JArray.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);

                            var byService = new Dictionary<string, Dictionary<string, Tuple<string, double?>>>();

                            foreach (var tok in arr)
                            {
                                var a = tok as Newtonsoft.Json.Linq.JObject;
                                if (a == null) continue;

                                string srcId = (string)(a["sourceId"] ?? a["SourceId"]) ?? "";
                                var svcMatch = SvcKeyToSourceId.FirstOrDefault(kv => kv.Value == srcId);
                                if (svcMatch.Key == null) continue;

                                string nodeId = ((string)(a["nodeIdentity"] ?? a["NodeIdentity"]) ?? "").ToLowerInvariant();
                                if (nodeId != "cloud" && nodeId != "local") continue;

                                string identifier = (string)(a["alertIdentifier"] ?? a["AlertIdentifier"]) ?? "";
                                string status = identifier.ToLowerInvariant().Contains("dead") ? "unhealthy" : "warning";

                                DateTime ts;
                                double? ago = null;
                                var tsToken = a["setTimeStamp"] ?? a["SetTimeStamp"];
                                if (tsToken != null && DateTime.TryParse(tsToken.ToString(), out ts))
                                    ago = (DateTime.Now - ts).TotalSeconds;

                                if (!byService.ContainsKey(svcMatch.Key)) byService[svcMatch.Key] = new Dictionary<string, Tuple<string, double?>>();
                                var nodes = byService[svcMatch.Key];
                                if (!nodes.ContainsKey(nodeId) || (status == "unhealthy" && nodes[nodeId].Item1 != "unhealthy"))
                                    nodes[nodeId] = Tuple.Create(status, ago);
                            }

                            foreach (var kv in SvcKeyToSourceId)
                            {
                                var nodesOut = new Dictionary<string, object>();
                                Dictionary<string, Tuple<string, double?>> nodes;
                                byService.TryGetValue(kv.Key, out nodes);

                                foreach (var node in new[] { "local", "cloud" })
                                {
                                    if (nodes != null && nodes.ContainsKey(node))
                                    {
                                        var code = nodes[node].Item1 == "unhealthy" ? "r" : "a";
                                        nodesOut[node] = new { sh = code, mq = code, ago = nodes[node].Item2 };
                                    }
                                    else
                                    {
                                        nodesOut[node] = new { sh = "g", mq = "g", ago = (double?)0 };
                                    }
                                }
                                svc[kv.Key] = nodesOut;
                            }

                            fixesCount = byService.Count(s => s.Value.Any(n => n.Value.Item1 == "unhealthy"));
                        }
                    }
                }
            }
            catch (Exception) { }

            svc["EdgeX"] = new { local = new { sh = "u", mq = "u", ago = (double?)null }, cloud = new { sh = "u", mq = "u", ago = (double?)null } };
            svc["Reminder"] = new { cloud = new { sh = "u", mq = "u", ago = (double?)null } };

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string searchDate = DateTime.Now.ToString("dd-MM-yyyy");
                    var response = hcf.client.GetAsync(string.Format("A10Status/GetStatus/SiteId/{0}/SearchDate/{1}", siteId, searchDate)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                        var apiData = serializer.Deserialize<ApiStatusResponse>(jsonResponse);

                        int netTotal = 0, netOnline = 0, iotTotal = 0, iotOnline = 0;
                        if (apiData != null && apiData.Modems != null)
                        {
                            foreach (var m in apiData.Modems)
                            {
                                if (string.IsNullOrEmpty(m.ModemId)) continue;
                                netTotal++;
                                if (m.TcpStatus || m.MqttStatus) netOnline++;

                                if (m.A10List != null)
                                {
                                    foreach (var a in m.A10List)
                                    {
                                        iotTotal++;
                                        if (a.TcpStatus || a.MQTTStatus) iotOnline++;
                                    }
                                }
                            }
                        }
                        hardware["network"] = new { total = netTotal, online = netOnline };
                        hardware["iot"] = new { total = iotTotal, online = iotOnline };
                    }
                }

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("CardLine/SiteId/{0}", siteId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var cardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString) ?? new List<CardLine>();
                        hardware["sensors"] = new { total = cardLines.Count, bad = 0 };
                    }
                }
            }
            catch (Exception) { }

            var result = new { Fixes = fixesCount, Hardware = hardware, Svc = svc };
            var jsonResult = Json(result, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }
    }
}