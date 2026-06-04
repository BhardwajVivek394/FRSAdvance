using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;
using static E7FRSAdvance.Utility.Utility;
using System.IO;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public class AlertsController : Controller
    {
        // GET: FRS25/Alerts
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService _assetAttributeService;
        private readonly ICardLineService _cardLineService;

        public AlertsController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService, ICardLineService cardLineService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _frsAlertService = frsAlertService;
            _assetAttributeService = assetAttributeService;
            _cardLineService = cardLineService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");


            return View();
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

        public ActionResult GetAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            if (ClsHttpContent.LoginUser == null)
            {
                Response.StatusCode = 401;
                return Content(
                    "<div style='text-align:center;padding:40px;color:#ef4444;font-family:sans-serif;'>" +
                    "<i class='fas fa-lock' style='font-size:28px;margin-bottom:12px;display:block;'></i>" +
                    "<strong>Session Expired</strong><br/>" +
                    "<a href='/Account/Login' style='color:#3b82f6;'>Click here to log in again</a>" +
                    "</div>", "text/html");
            }

            try
            {
                if (mFRSAlertLister?.Pager == null)
                    throw new ArgumentNullException("Pager is null");

                mFRSAlertLister.Pager.Take = mFRSAlertLister.Pager.PageSize;
                mFRSAlertLister = _frsAlertService.GetListerWithPagination(mFRSAlertLister);

                if (mFRSAlertLister?.mFRSAlerts != null)
                {
                    if (mFRSAlertLister.SearchCriteria.AlertStatus == 1)
                        mFRSAlertLister.mFRSAlerts = mFRSAlertLister.mFRSAlerts
                            .Where(x => x.AcknowledgemenTimeStamp != null).ToList();
                    else if (mFRSAlertLister.SearchCriteria.AlertStatus == 2)
                        mFRSAlertLister.mFRSAlerts = mFRSAlertLister.mFRSAlerts
                            .Where(x => x.AcknowledgemenTimeStamp == null).ToList();
                }

                if (mFRSAlertLister?.mFRSAlerts?.Count > 0)
                {
                    foreach (var mFRSAlert in mFRSAlertLister.mFRSAlerts
                        .GroupBy(x => new { x.AssetTypeId, x.AssetType })
                        .Select(x => x.Key).ToList())
                    {
                        mFRSAlertLister.AssetTypes.Add(
                            new Domain.AssetType() { Id = mFRSAlert.AssetTypeId, Name = mFRSAlert.AssetType });
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }

            // ── finally replaced: exceptions here were escaping unhandled → 500 on live ──
            try
            {
                ViewBag.AppConfigs = GetAppConfigByGroup("FeedbackDuration");
            }
            catch
            {
                ViewBag.AppConfigs = new List<Domain.AppConfig>();
            }

            try
            {
                var ids = mFRSAlertLister?.mFRSAlerts?.Select(x => x.Id).ToList()
                          ?? new List<int>();
                ViewBag.FRSAlertRemarks = GetFRSAlertRemark(ids);
            }
            catch
            {
                ViewBag.FRSAlertRemarks = new List<Domain.FRSAlertRemark>();
            }

            return PartialView("_List", mFRSAlertLister);
        }
        public List<Domain.AppConfig> GetAppConfigByGroup(string groupName)
        {
            var mAppConfigs = new List<Domain.AppConfig>();
            try
            {
                if (ClsHttpContent.LoginUser == null) return mAppConfigs;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"AppConfig/GroupName/{groupName}")
                                      .ConfigureAwait(false).GetAwaiter().GetResult();
                    string jsonString = response.Content.ReadAsStringAsync()
                                                .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (response.StatusCode == HttpStatusCode.OK)
                        mAppConfigs = JsonConvert.DeserializeObject<List<Domain.AppConfig>>(jsonString);
                }
            }
            catch
            {
                mAppConfigs = new List<Domain.AppConfig>();
            }
            return mAppConfigs;
        }

        public List<Domain.FRSAlertRemark> GetFRSAlertRemark(List<int> ids)
        {
            var mFRSAlertRemarks = new List<Domain.FRSAlertRemark>();
            try
            {
                if (ClsHttpContent.LoginUser == null) return mFRSAlertRemarks;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(ids);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("FRSAlert/GetFRSRemarkByIds", str)
                                      .ConfigureAwait(false).GetAwaiter().GetResult();
                    string jsonString = response.Content.ReadAsStringAsync()
                                                .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (response.StatusCode == HttpStatusCode.OK)
                        mFRSAlertRemarks = JsonConvert.DeserializeObject<List<Domain.FRSAlertRemark>>(jsonString);
                }
            }
            catch
            {
                mFRSAlertRemarks = new List<Domain.FRSAlertRemark>();
            }
            return mFRSAlertRemarks;
        }
        private void SetTempRemark(List<FRSAlert> fRSAlerts)
        {
            var TempFRSAlertLiveStatus = TempData.Peek("TempFRSAlertLiveStatus");
            if (TempFRSAlertLiveStatus != null && fRSAlerts != null && fRSAlerts.Count > 0)
            {
                var mFRSAlerts = TempFRSAlertLiveStatus as List<Domain.FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    foreach (var fRSAlert in fRSAlerts)
                    {
                        var seleFRSAlerts = mFRSAlerts.Where(x => x.Id == fRSAlert.Id).FirstOrDefault();
                        if (seleFRSAlerts != null && seleFRSAlerts.Id > 0 && seleFRSAlerts.TempRemark.IsNotNullOrEmpty())
                        {
                            fRSAlert.TempRemark = seleFRSAlerts.TempRemark;

                        }
                    }

                }
            }
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
        public ActionResult GetAlertCounts(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister.Pager.Take = -1;
                mFRSAlertLister = _frsAlertService.GetListerWithPagination(mFRSAlertLister);
                var alerts = mFRSAlertLister?.mFRSAlerts ?? new List<Domain.FRSAlert>();

                // Cleared = AcknowledgemenTimeStamp != null (feedback given today)
                var clearedToday = alerts.Where(x => x.AcknowledgemenTimeStamp != null
                                                  && x.AcknowledgemenTimeStamp.Value.Date == DateTime.Today).ToList();

                return Json(new
                {
                    failCount = alerts.Count(x => x.AlertTypeId == (int)AlertType.Failure),
                    predictiveCount = alerts.Count(x => x.AlertTypeId == (int)AlertType.Predictive),
                    failurePending = alerts.Count(x => x.AlertTypeId == (int)AlertType.Failure && x.AcknowledgemenTimeStamp == null),
                    failureCleared = alerts.Count(x => x.AlertTypeId == (int)AlertType.Failure && x.AcknowledgemenTimeStamp != null),
                    predictivePending = alerts.Count(x => x.AlertTypeId == (int)AlertType.Predictive && x.AcknowledgemenTimeStamp == null),
                    predictiveCleared = alerts.Count(x => x.AlertTypeId == (int)AlertType.Predictive && x.AcknowledgemenTimeStamp != null),

                    // Cleared Today tile
                    clearedToday = clearedToday.Count,

                    // True = AcknowledgemenStatus.True
                    trueCount = clearedToday.Count(x =>
                                       x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True),

                    // PT/F/M = PT + False + Maintenance
                    ptfmCount = clearedToday.Count(x =>
                                       x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.PT ||
                                       x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.False ||
                                       x.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace)

                }, JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new
                {
                    failCount = 0,
                    predictiveCount = 0,
                    failurePending = 0,
                    failureCleared = 0,
                    predictivePending = 0,
                    predictiveCleared = 0,
                    clearedToday = 0,
                    trueCount = 0,
                    ptfmCount = 0
                }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult _SidebarList(Domain.MaintenanceModeLister mMaintenanceModeLister)
        {
            try
            {
                if (mMaintenanceModeLister == null) mMaintenanceModeLister = new MaintenanceModeLister();
                if (mMaintenanceModeLister.SearchCriteria == null) mMaintenanceModeLister.SearchCriteria = new MaintenanceMode();
                if (mMaintenanceModeLister.Pager == null) mMaintenanceModeLister.Pager = new Domain.Pager();

                mMaintenanceModeLister.Pager.Take = -1;
                mMaintenanceModeLister.SearchCriteria.IsMaintenceMode = true;   // active only
                mMaintenanceModeLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mMaintenanceModeLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceModeLister);
                    var str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("MaintenanceMode/GetLister", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var jsonString = response.Content.ReadAsStringAsync().Result;
                        mMaintenanceModeLister = JsonConvert.DeserializeObject<MaintenanceModeLister>(jsonString);
                    }
                }
            }
            catch (Exception ex) { throw ex; }

            return PartialView(mMaintenanceModeLister);
        }

        public ActionResult UpdateMaintenanceInActiveMode(Domain.MaintenanceMode mMaintenanceMode)
        {
            dynamic data = new ExpandoObject();
            try
            {


                mMaintenanceMode.InActiveTime = DateTime.Now;
                mMaintenanceMode.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceMode);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/SetInActive"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }
        public ActionResult UpdateMaintenanceActiveMode(Domain.MaintenanceMode mMaintenanceMode)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mMaintenanceMode.IsMaintenceMode = true;
                if (mMaintenanceMode.FromDate != null && mMaintenanceMode.FromDate != DateTime.MinValue)
                {
                    DateTime baseDate = mMaintenanceMode.FromDate.Date;

                    if (mMaintenanceMode.FromTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mMaintenanceMode.FromTime, out TimeSpan time))
                            mMaintenanceMode.ActiveTime = baseDate.Add(time);
                        else
                            mMaintenanceMode.ActiveTime = baseDate;
                    }
                    else
                        mMaintenanceMode.ActiveTime = baseDate;

                }
                else
                    mMaintenanceMode.ActiveTime = DateTime.Now;

                if (mMaintenanceMode.ToDate != null && mMaintenanceMode.ToDate != DateTime.MinValue)
                {
                    DateTime baseDate = mMaintenanceMode.ToDate.Date;

                    if (mMaintenanceMode.ToTime.IsNotNullOrEmpty())
                    {
                        if (TimeSpan.TryParse(mMaintenanceMode.ToTime, out TimeSpan time))
                            mMaintenanceMode.InActiveTime = baseDate.Add(time);
                        else
                            mMaintenanceMode.InActiveTime = baseDate;
                    }
                    else
                        mMaintenanceMode.InActiveTime = baseDate;
                }
                mMaintenanceMode.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceMode);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceMode/Create"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }

        public ActionResult GetAssetById(int assetTypeId, int siteId)
        {
            List<Asset> mAsset = new List<Asset>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"Asset/GetAllAssest/{siteId}/{assetTypeId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAsset = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mAsset, JsonRequestBehavior.AllowGet);
        }
        [HttpPost]
        public ActionResult GetPerformanceMetrics(Domain.FRSAlertLister mFRSAlertLister)
        {
            double? failureAccuracy = null, predictiveAccuracy = null, actualDetection = null;
            try
            {
                mFRSAlertLister.Pager = mFRSAlertLister.Pager ?? new Domain.Pager();
                mFRSAlertLister.Pager.Take = -1;
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;

                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlertWithManual(mFRSAlertLister);

                if (mFRSAlertLister?.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    var alerts = mFRSAlertLister.mFRSAlerts;
                    int sTrue = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True;
                    int sPT = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.PT;
                    int sFalse = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.False;
                    int tFail = (int)E7FRSAdvance.Utility.Utility.AlertType.Failure;
                    int tPred = (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive;

                    // 1. Failure Alert Accuracy
                    int ptFail = alerts.Count(x => x.AlertTypeId == tFail && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT));
                    int totFail = alerts.Count(x => x.AlertTypeId == tFail && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT || x.AcknowledgemenStatusId == sFalse));
                    failureAccuracy = totFail > 0 ? Math.Round((double)ptFail / totFail * 100, 2) : 0.0;

                    // 2. Predictive Alert Accuracy
                    int ptPred = alerts.Count(x => x.AlertTypeId == tPred && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT));
                    int totPred = alerts.Count(x => x.AlertTypeId == tPred && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT || x.AcknowledgemenStatusId == sFalse));
                    predictiveAccuracy = totPred > 0 ? Math.Round((double)ptPred / totPred * 100, 2) : 0.0;

                    // 3. Actual Failure Detection
                    int rdpms = alerts.Count(x => x.AlertTypeId == tFail && x.isManual == 0 && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT));
                    int totAct = alerts.Count(x => x.AlertTypeId == tFail && x.isManual == 1 && (x.AcknowledgemenStatusId == sTrue || x.AcknowledgemenStatusId == sPT || x.AcknowledgemenStatusId == sFalse));
                    actualDetection = totAct > 0 ? Math.Round((double)rdpms / totAct * 100, 2) : 0.0;
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { FailureAccuracy = failureAccuracy, PredictiveAccuracy = predictiveAccuracy, ActualDetection = actualDetection }, JsonRequestBehavior.AllowGet);
        }


        public ActionResult GetTelemetryHistoryData(int frsAlertId, int siteId, int assetId, string startDate, string endDate)
        {
            string strss = string.Empty;
            try
            {
                try
                {
                    Asset mAsset = new Asset();
                    mAsset.Id = assetId;
                    mAsset.SortDirection = "Graph";
                    mAsset.IsGraphLoad = true;
                    mAsset.StartDate = DateTime.Parse(startDate).ToShortDateString();
                    mAsset.StartTime = DateTime.Parse(startDate).ToShortTimeString();

                    mAsset.EndDate = DateTime.Parse(endDate).ToShortDateString();
                    mAsset.EndTime = DateTime.Parse(endDate).ToShortTimeString();
                    mAsset.SiteId = siteId;

                    var mAssets = new List<Asset>();
                    mAssets.Add(mAsset);

                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                            //DownloadCsv(mAsset, assets);

                            string StartDate = mAssets.FirstOrDefault().StartDate;
                            string EndDate = mAssets.FirstOrDefault().EndDate;


                            string formattedstartDate = DateTime.Parse(startDate).ToString("ddMMyyyy_HHmmss");
                            string formattedEndDate = DateTime.Parse(endDate).ToString("ddMMyyyy_HHmmss");

                            var mRdpmsResponse = GetHistoryData(assetId, formattedstartDate, formattedEndDate);

                            //strss = _assetAttributeService.PrepareCSVWithEdgex(assets, mRdpmsResponse);
                            if (assets != null && assets.Count > 0)
                            {
                                foreach (var asset in assets)
                                {
                                    if (asset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                                    {
                                        var mFRSAlert = GetFRSAlert(frsAlertId);
                                        if (mFRSAlert != null && mFRSAlert.Id > 0)
                                        {
                                            if (mFRSAlert.CauseCode.ToUpper().Contains(" R "))
                                                asset.SortDirection = "reverse";
                                            else
                                                asset.SortDirection = "normal";
                                        }
                                    }
                                }
                            }


                            strss = PrepareCSVWithEdgexDataLogger(assets, startDate, endDate);
                        }
                        else
                        {
                            return Json(new { error = "Internal server error!" }, JsonRequestBehavior.AllowGet);
                        }
                    }

                }
                catch (Exception ex)
                {
                    return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }

            return Json(strss, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetMQTTHistoryGraphData(int siteId, int assetId, string startDate, string endDate)
        {
            string strss = string.Empty;
            try
            {
                try
                {
                    Asset mAsset = new Asset();
                    mAsset.Id = assetId;
                    mAsset.SortDirection = "Graph";
                    mAsset.IsGraphLoad = true;
                    mAsset.StartDate = DateTime.Parse(startDate).ToShortDateString();
                    mAsset.StartTime = DateTime.Parse(startDate).ToShortTimeString();

                    mAsset.EndDate = DateTime.Parse(endDate).ToShortDateString();
                    mAsset.EndTime = DateTime.Parse(endDate).ToShortTimeString();
                    mAsset.SiteId = siteId;

                    var mAssets = new List<Asset>();
                    mAssets.Add(mAsset);

                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAssets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                            //DownloadCsv(mAsset, assets);

                            string StartDate = mAssets.FirstOrDefault().StartDate;
                            string EndDate = mAssets.FirstOrDefault().EndDate;


                            strss = _assetAttributeService.PrepareCSV(assets);
                        }
                        else
                        {
                            return Json(new { error = "Internal server error!" }, JsonRequestBehavior.AllowGet);
                        }
                    }

                }
                catch (Exception ex)
                {
                    return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }

            return Json(strss, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GeDataLoggerJSONData(int siteId, int assetId, string startDate, string endDate)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();


            string formattedstartDate = DateTime.Parse(startDate).ToString("ddMMyyyy_HHmmss");
            string formattedEndDate = DateTime.Parse(endDate).ToString("ddMMyyyy_HHmmss");
            var mRdpmsResponse = GetHistoryData(assetId, formattedstartDate, formattedEndDate);
            if (mRdpmsResponse != null && mRdpmsResponse.Data != null && mRdpmsResponse.Data.Count > 0)
            {

                var mAssetInfoDataloggers = GetAssetInfoDatalogger(assetId);
                if (mAssetInfoDataloggers != null && mAssetInfoDataloggers.Count > 0)
                {
                    foreach (var mAssetInfoDatalogger in mAssetInfoDataloggers)
                    {
                        int.TryParse(mAssetInfoDatalogger.Value, out int dlAttrId);
                        var dlData = mRdpmsResponse.Data.Where(attr => attr.AssetId == assetId && attr.AttributeId == dlAttrId && attr.Values != null && attr.Values.Values.Any(v => v.DataType == "DataLogger")).FirstOrDefault();
                        if (dlData != null && dlData.AssetId > 0)
                        {
                            foreach (var item in dlData.Values)
                            {
                                Domain.Datalogger mDatalogger = new Domain.Datalogger();

                                double.TryParse(item.Value.Value, out double dlValue);
                                if (dlValue == 0)
                                {
                                    mDatalogger.CurrentValue = "DROP";
                                }
                                else if (dlValue == 1)
                                {
                                    mDatalogger.CurrentValue = "Pickup";
                                }

                                int.TryParse(mAssetInfoDatalogger.Value, out int SRNo);


                                mDatalogger.SrNo = SRNo;
                                mDatalogger.AssetName = mAssetInfoDatalogger.DataloggerAssetName;
                                mDatalogger.Timestamp = item.Value.Timestamp.TimestampDevice.ToString();
                                mDatalogger.CurrentDate = item.Value.Timestamp.TimestampEdgeX;

                                mDatalogger.AttributeName = mAssetInfoDatalogger.DataloggerAttribute;


                                mDatalogger.Type = mAssetInfoDatalogger.ContactType;
                                mDataloggers.Add(mDatalogger);
                            }
                        }
                        //var dsd= dlData.
                    }
                }


            }

            return Json(mDataloggers, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetFRSAlertById(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                mFRSAlert = GetFRSAlert(id);
            }
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }
            return Json(mFRSAlert);
        }
        public ActionResult GetTPRJSONData(int siteId, int assetId, string startDate, string endDate)
        {
            string prepareCSVStr = string.Empty;
            try
            {
                Asset mAsset = new Asset();
                mAsset.Id = assetId;
                mAsset.SortDirection = "Graph";
                mAsset.IsGraphLoad = true;
                mAsset.StartDate = DateTime.Parse(startDate).ToShortDateString();
                mAsset.StartTime = DateTime.Parse(startDate).ToShortTimeString();

                mAsset.EndDate = DateTime.Parse(endDate).ToShortDateString();
                mAsset.EndTime = DateTime.Parse(endDate).ToShortTimeString();
                mAsset.SiteId = siteId;

                mAsset.assetAttributes.Add(new AssetAttribute() { Id = 6 });
                var mFamilyTracks = GetFamilyTrack(assetId);

                var mAssets = new List<Asset>();
                mAssets.Add(mAsset);
                if (mFamilyTracks != null && mFamilyTracks.Count > 0)
                {
                    foreach (var mFamilyTrack in mFamilyTracks)
                    {
                        var assetAttributes = new List<AssetAttribute>();
                        assetAttributes.Add(new AssetAttribute() { Id = 6 });
                        mAssets.Add(new Asset()
                        {
                            Id = mFamilyTrack.TrackFamilyId,
                            SortDirection = "Graph",
                            IsGraphLoad = true,
                            SiteId = siteId,
                            StartDate = mAsset.StartDate,
                            StartTime = mAsset.StartTime,
                            EndDate = mAsset.EndDate,
                            EndTime = mAsset.EndTime

                        });
                    }

                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssets);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GenerateGraph"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var assets = JsonConvert.DeserializeObject<List<Asset>>(jsonString);
                        if (assets != null && assets.Count > 0)
                        {

                            string StartDate = mAssets.FirstOrDefault().StartDate;
                            string EndDate = mAssets.FirstOrDefault().EndDate;



                            foreach (var asset in assets)
                            {
                                var mSearchCriteria = new SearchCriteria();
                                mSearchCriteria.SearchDate = DateTime.Parse(startDate).ToString("d-M-yyyy");
                                mSearchCriteria.SiteId = asset.SiteId;
                                mSearchCriteria.AssetId = asset.Id;
                                var mDataloggers = DataLoggerEvent(mSearchCriteria);
                                if (mDataloggers != null && mDataloggers.Count > 0)
                                {
                                    foreach (var mDatalogger in mDataloggers)
                                    {
                                        asset.mDataloggers = mDataloggers;
                                    }
                                }
                            }


                            prepareCSVStr = PrepareCSVWithEdgexDataLogger(assets, startDate, endDate);
                        }
                    }
                    else
                    {
                    }
                }

            }
            catch (Exception ex)
            {
            }
            return Json(prepareCSVStr, JsonRequestBehavior.AllowGet);
        }
        public List<FamilyTrack> GetFamilyTrack(int assetId)
        {
            var mFamilyTracks = new List<FamilyTrack>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FamilyTrack/AssetId/{assetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFamilyTracks = JsonConvert.DeserializeObject<List<FamilyTrack>>(jsonString);
                    }
                    else
                    {
                        //  result = false;
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mFamilyTracks;

        }
        public ActionResult GetNetworkNumberList(int siteId, int assetId, string type, string protocol)
        {
            var result = new List<object>();

            try
            {

                if (type == "A10")
                {
                    // Replace with your actual DB/service call
                    var a10List = _cardLineService.GetByAssetId(assetId);

                    result = a10List.Select(x => (object)new
                    {
                        Number = x.ADCNumber,
                        DisplayName = $"{x.ADCNumber} ({x.AttributeName})",
                        ClusterId = x.ClusterId
                    }).ToList();
                }
                else if (type == "Modem")
                {
                    // Replace with your actual DB/service call
                    var modemList = _cardLineService.GetByAssetId(assetId);


                    result = modemList.Select(x => (object)new
                    {
                        Number = x.ClusterId,
                        DisplayName = $"{x.ClusterName} ({x.AttributeName})",
                        ClusterId = x.ClusterId
                    }).ToList();
                }

                return Json(result, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }

        }
        public ActionResult GetNetworkGraphData(int clusterId, int a10Id, string type, string protocol, string startDate, string endDate)
        {
            var fromDate = DateTime.Parse(startDate).ToString("d-M-yyyy");
            var toDate = DateTime.Parse(endDate).ToString("d-M-yyyy");
            try
            {
                if (type == "A10")
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        string apiEndpoint = string.Format("A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                            clusterId,
                            a10Id,
                            fromDate,
                            toDate);

                        var response = hcf.client.GetAsync(apiEndpoint).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var dbList = JsonConvert.DeserializeObject<List<Domain.Dto.A10HistoryModel>>(jsonString);
                            if (dbList != null && dbList.Count > 0)
                            {
                                if (protocol == "TCP")
                                {
                                    var result = dbList.Where(x => x.TypeId == 1).Select(x => new
                                    {
                                        Timestamp = DateTime.Parse(x.TimeStamp).ToString("HH:mm"),
                                        ResponseTime = x.ResponseTime,
                                    });

                                    return Json(result, JsonRequestBehavior.AllowGet);
                                }
                                else if (protocol == "MQTT")
                                {
                                    var result = dbList.Where(x => x.TypeId == 2).Select(x => new
                                    {
                                        Timestamp = DateTime.Parse(x.TimeStamp).ToString("HH:mm"),
                                        UpTime = x.Uptime
                                    });

                                    return Json(result, JsonRequestBehavior.AllowGet);
                                }

                            }

                        }

                        return Json(new { Success = false, Message = "Failed to fetch data from API" }, JsonRequestBehavior.AllowGet);
                    }
                }
                else if (type == "Modem")
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        string apiEndpoint = string.Format("Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}",
                          clusterId,
                          fromDate,
                          toDate);

                        var response = hcf.client.GetAsync(apiEndpoint).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var dbList = JsonConvert.DeserializeObject<List<Domain.Dto.ModemHistory>>(jsonString);
                            if (dbList != null && dbList.Count > 0)
                            {
                                if (protocol == "TCP")
                                {
                                    var result = dbList.Where(x => x.TypeId == 1).Select(x => new
                                    {
                                        Timestamp = x.TimeStamp.ToString("HH:ss"),
                                        SignalStrength = x.SignalStrength,
                                    });

                                    return Json(result, JsonRequestBehavior.AllowGet);
                                }
                                else if (protocol == "MQTT")
                                {
                                    var result = dbList.Where(x => x.TypeId == 2).Select(x => new
                                    {
                                        Timestamp = x.TimeStamp.ToString("HH:ss"),
                                        SignalStrength = x.SignalStrength
                                    });

                                    return Json(result, JsonRequestBehavior.AllowGet);
                                }

                            }

                        }

                        return Json(new { Success = false, Message = "Failed to fetch data from API" }, JsonRequestBehavior.AllowGet);
                    }
                }

            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { Success = false, Message = "Data Not Found" }, JsonRequestBehavior.AllowGet);
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

        public Domain.Dto.RdpmsResponse GetHistoryData(int assetId, string startDate, string endDate)
        {
            var mRdpmsResponse = new Domain.Dto.RdpmsResponse();
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
                        mRdpmsResponse = JsonConvert.DeserializeObject<Domain.Dto.RdpmsResponse>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mRdpmsResponse;
        }
        private Domain.FRSAlert GetFRSAlert(int id)
        {

            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("FRSAlert/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString);
                            if (mFRSAlert == null)
                                mFRSAlert = new Domain.FRSAlert();
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }

            return mFRSAlert;
        }

        public string PrepareCSVWithEdgexDataLogger(List<Asset> assets, string startDate, string endDate)
        {
            string csv = string.Empty;

            if (assets != null && assets.Count > 0)
            {
                csv += "Site Name,";
                csv += "Date,";
                csv += "Asset Type,";
                foreach (var asset in assets)
                {
                    if (asset.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                    {
                        asset.assetAttributes = asset.assetAttributes.Where(x => x.Title.Contains("NWKR") || x.Title.Contains("RWKR")).ToList();

                        if (asset.SortDirection == "reverse")
                        {
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 6002, Title = "A Avg" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 7002, Title = "A Voltage" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 6005, Title = "A Time" });

                            asset.assetAttributes.Add(new AssetAttribute() { Id = 8002, Title = "B Avg" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 9002, Title = "B Voltage" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 8005, Title = "B Time" });
                        }
                        else
                        {
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 1002, Title = "A Avg" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 2002, Title = "A Voltage" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 1005, Title = "A Time" });

                            asset.assetAttributes.Add(new AssetAttribute() { Id = 3002, Title = "B Avg" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 4002, Title = "B Voltage" });
                            asset.assetAttributes.Add(new AssetAttribute() { Id = 3005, Title = "B Time" });
                        }

                    }
                    foreach (var assetAttribute in asset.assetAttributes)
                    {
                        csv += $"{asset.Name} {assetAttribute.Title},";
                    }

                    var mAssetInfoDataloggers = GetAssetInfoDatalogger(asset.Id);
                    if (mAssetInfoDataloggers != null && mAssetInfoDataloggers.Count > 0)
                    {
                        asset.mAssetInfoDataloggers = mAssetInfoDataloggers;
                        foreach (var item in mAssetInfoDataloggers)
                        {
                            csv += $"{asset.Name} {item.DataloggerAttribute} DL Edgex,";
                        }
                    }
                    if (asset.mDataloggers != null && asset.mDataloggers.Count > 0)
                    {
                        foreach (var item in asset.mDataloggers.GroupBy(x => new { x.AttributeId, x.AttributeName }).Select(x => x.Key))
                        {
                            csv += $"{asset.Name} {item.AttributeName} DL SQL,";
                        }
                    }
                }

                csv += "\r\n";


                var rdpmsResponse = new Domain.Dto.RdpmsResponse();
                string formattedstartDate = DateTime.Parse(startDate).ToString("ddMMyyyy_HHmmss");
                string formattedEndDate = DateTime.Parse(endDate).ToString("ddMMyyyy_HHmmss");
                foreach (var item in assets)
                {
                    var mRdpmsResponse = GetHistoryData(item.Id, formattedstartDate, formattedEndDate);
                    if (mRdpmsResponse != null && mRdpmsResponse.Data != null && mRdpmsResponse.Data.Count > 0)
                    {
                        rdpmsResponse.Data.AddRange(mRdpmsResponse.Data);
                    }
                }


                if (rdpmsResponse != null && rdpmsResponse.Data != null && rdpmsResponse.Data.Count > 0)
                {
                    var startEdjexDate = rdpmsResponse.Data.Select(x => x.StartDate).FirstOrDefault();
                    var endEdjexDate = rdpmsResponse.Data.Select(x => x.EndDate).FirstOrDefault();

                    var timeStamps = rdpmsResponse.Data.SelectMany(x => x.Values).GroupBy(x => new DateTime(x.Value.Timestamp.TimestampDevice.Year, x.Value.Timestamp.TimestampDevice.Month, x.Value.Timestamp.TimestampDevice.Day, x.Value.Timestamp.TimestampDevice.Hour, x.Value.Timestamp.TimestampDevice.Minute, x.Value.Timestamp.TimestampDevice.Second)).Select(x => x.Key).OrderBy(x => x).ToList();

                    foreach (var timeStamp in timeStamps.Where(x => x >= startEdjexDate))
                    {
                        string newRow = string.Empty;
                        bool isUpdateAtType = false;
                        foreach (var asset in assets)
                        {
                            if (asset.assetAttributes != null)
                            {
                                if (!isUpdateAtType)
                                {
                                    var assetTypeName = asset.assetAttributes.Select(x => x.AssetTypeName).FirstOrDefault();
                                    newRow += asset.SiteName + ",";// + row;
                                    newRow += timeStamp.ToString() + ",";
                                    newRow += assetTypeName + ",";
                                    isUpdateAtType = true;
                                }
                                if (asset.assetAttributes != null && asset.assetAttributes.Count > 0)
                                {
                                    foreach (var assetAttribute in asset.assetAttributes)
                                    {
                                        var mMultipleLog = rdpmsResponse.Data.Where(x => x.AssetId == asset.Id && x.AttributeId == assetAttribute.Id).FirstOrDefault();
                                        if (mMultipleLog != null && mMultipleLog.Values != null && mMultipleLog.Values.Count > 0)
                                        {
                                            var firstValue = mMultipleLog.Values.OrderBy(x => x.Value.Timestamp.TimestampDevice).FirstOrDefault();
                                            if (firstValue.Value != null && assetAttribute.Data.Count == 0)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(firstValue.Value.Value));
                                            }
                                            //var changedValue = mMultipleLog.Values.Where(x => x.Value.Timestamp.TimestampDevice >= timeStamp.AddSeconds(-5) && x.Value.Timestamp.TimestampDevice <= timeStamp.AddSeconds(5)).FirstOrDefault();

                                            var changedValue = mMultipleLog.Values
.Where(x =>
x.Value.Timestamp.TimestampDevice.Year == timeStamp.Year &&
x.Value.Timestamp.TimestampDevice.Month == timeStamp.Month &&
x.Value.Timestamp.TimestampDevice.Day == timeStamp.Day &&
x.Value.Timestamp.TimestampDevice.Hour == timeStamp.Hour &&
x.Value.Timestamp.TimestampDevice.Minute == timeStamp.Minute &&
x.Value.Timestamp.TimestampDevice.Second == timeStamp.Second)
.FirstOrDefault();
                                            if (changedValue.Value != null)
                                            {
                                                assetAttribute.Data = new List<string>();
                                                assetAttribute.Data.Add(Convert.ToString(changedValue.Value.Value));
                                            }
                                        }

                                        if (assetAttribute.Data != null)
                                        {
                                            var data = assetAttribute.Data.FirstOrDefault();
                                            newRow += data + ",";
                                        }
                                        else
                                        {
                                            newRow += "0.0,";
                                        }
                                    }
                                }

                                if (asset.mAssetInfoDataloggers != null && asset.mAssetInfoDataloggers.Count > 0)
                                {
                                    foreach (var mAssetInfoDatalogger in asset.mAssetInfoDataloggers)
                                    {
                                        int.TryParse(mAssetInfoDatalogger.Value, out int dlId);
                                        var mMultipleLog = rdpmsResponse.Data.Where(x => x.AssetId == asset.Id && x.AttributeId == dlId && x.Values.Values.Any(v => v.DataType == "DataLogger")).FirstOrDefault();
                                        if (mMultipleLog != null && mMultipleLog.Values != null && mMultipleLog.Values.Count > 0)
                                        {
                                            var firstValue = mMultipleLog.Values.OrderBy(x => x.Value.Timestamp.TimestampDevice).FirstOrDefault();
                                            if (firstValue.Value != null && mAssetInfoDatalogger.DLStatus.IsNullOrEmpty())
                                            {
                                                mAssetInfoDatalogger.DLStatus = Convert.ToString(firstValue.Value.Value);
                                            }
                                            //var changedValue = mMultipleLog.Values.Where(x => x.Value.Timestamp.TimestampDevice >= timeStamp.AddSeconds(-5) && x.Value.Timestamp.TimestampDevice <= timeStamp.AddSeconds(5)).FirstOrDefault();

                                            var changedValue = mMultipleLog.Values
.Where(x =>
x.Value.Timestamp.TimestampDevice.Year == timeStamp.Year &&
x.Value.Timestamp.TimestampDevice.Month == timeStamp.Month &&
x.Value.Timestamp.TimestampDevice.Day == timeStamp.Day &&
x.Value.Timestamp.TimestampDevice.Hour == timeStamp.Hour &&
x.Value.Timestamp.TimestampDevice.Minute == timeStamp.Minute &&
x.Value.Timestamp.TimestampDevice.Second == timeStamp.Second)
.FirstOrDefault();
                                            if (changedValue.Value != null)
                                            {
                                                mAssetInfoDatalogger.DLStatus = Convert.ToString(changedValue.Value.Value);
                                            }
                                        }

                                        if (mAssetInfoDatalogger.DLStatus.IsNotNullOrEmpty())
                                        {
                                            double.TryParse(mAssetInfoDatalogger.DLStatus, out double dlstatus);
                                            string status = string.Empty;
                                            if (dlstatus == 1)
                                            {
                                                status = "Pickup";
                                            }
                                            else if (dlstatus == 0)
                                            {
                                                status = "Drop";
                                            }
                                            var data = status;
                                            newRow += data + ",";
                                        }
                                        else
                                        {
                                            newRow += "0.0,";
                                        }
                                    }
                                }
                                if (asset.mDataloggers != null && asset.mDataloggers.Count > 0)
                                {
                                    foreach (var item in asset.mDataloggers.GroupBy(x => new { x.AttributeId, x.AttributeName }).Select(x => x.Key))
                                    {
                                        var datalogger = asset.mDataloggers.Where(x => x.Date.AddMilliseconds(-x.Date.Millisecond) <= timeStamp.AddMilliseconds(-timeStamp.Millisecond)).OrderByDescending(x => x.Date).FirstOrDefault();
                                        if (datalogger != null && datalogger.AttributeId > 0)
                                        {
                                            newRow += $"{datalogger.CurrentValue},";
                                        }
                                        else
                                        {
                                            newRow += ",";
                                        }
                                    }
                                }
                            }
                            else
                            {

                            }
                        }

                        if (isUpdateAtType)
                        {
                            //Add the Data rows.
                            csv += newRow.TrimEnd(',');
                            //Add new line.
                            csv += "\r\n";
                        }
                    }
                }

            }


            return csv;
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
        [HttpGet]
        public JsonResult GetRedaFiles(string logType, string machine, string date)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (string.IsNullOrEmpty(logType) || string.IsNullOrEmpty(machine) || string.IsNullOrEmpty(date))
                    return Json(new { success = false, message = "Required params missing", data = new List<object>() }, JsonRequestBehavior.AllowGet);

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("RedaLog/GetFiles/{0}/{1}/{2}", logType, machine, date)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var jArray = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(jsonString);
                        var result = jArray.Select(x => new
                        {
                            FileName = x["FileName"] != null ? x["FileName"].ToString() :
                                       (x["fileName"] != null ? x["fileName"].ToString() : ""),
                            SizeInKB = x["SizeInKB"] != null ? (long)x["SizeInKB"] :
                                       (x["sizeInKB"] != null ? (long)x["sizeInKB"] : 0),
                            LastModified = x["LastModified"] != null ? Convert.ToDateTime(x["LastModified"]).ToString("dd/MM/yyyy HH:mm:ss") :
                                           (x["lastModified"] != null ? Convert.ToDateTime(x["lastModified"]).ToString("dd/MM/yyyy HH:mm:ss") : "")
                        }).ToList();

                        data = new { success = true, data = result };
                    }
                    else
                    {
                        data = new { success = false, message = "API Error: " + response.StatusCode, data = new List<object>() };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { success = false, message = ex.Message, data = new List<object>() };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        // 5. Download single file — proxy to RedaLog API
        [HttpGet]
        public ActionResult DownloadRedaFile(string logType, string machine, string date, string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(logType) || string.IsNullOrEmpty(machine)
                    || string.IsNullOrEmpty(date) || string.IsNullOrEmpty(fileName))
                    return new HttpStatusCodeResult(400, "All params required");

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    // Increase timeout for large files
                    hcf.client.Timeout = TimeSpan.FromMinutes(5);

                    // Call RedaLog download API (URL-encode fileName for safety)


                    var response = hcf.client.GetAsync(string.Format("RedaLog/DownloadFile/{0}/{1}/{2}/{3}", Uri.EscapeDataString(logType),
                        Uri.EscapeDataString(machine),
                        Uri.EscapeDataString(date),
                        Uri.EscapeDataString(fileName))).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var fileBytes = response.Content.ReadAsByteArrayAsync().Result;

                        // Pick up the content type from API response
                        var contentType = response.Content.Headers.ContentType != null
                            ? response.Content.Headers.ContentType.MediaType
                            : "application/octet-stream";

                        // Pick up file name from API response (or use original)
                        var returnFileName = fileName;
                        if (response.Content.Headers.ContentDisposition != null
                            && !string.IsNullOrEmpty(response.Content.Headers.ContentDisposition.FileName))
                        {
                            returnFileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
                        }

                        return File(fileBytes, contentType, returnFileName);
                    }
                    else
                    {
                        return new HttpStatusCodeResult((int)response.StatusCode, "Failed to download file");
                    }
                }
            }
            catch (Exception ex)
            {
                return new HttpStatusCodeResult(500, ex.Message);
            }
        }
        [HttpGet]
        public JsonResult GetRedaLogTypes()
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("RedaLog/GetLogTypes").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // Deserialize as JArray to handle any casing
                        var jArray = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(jsonString);
                        var result = jArray.Select(x => new
                        {
                            Name = x["Name"] != null ? x["Name"].ToString() :
                                   (x["name"] != null ? x["name"].ToString() : "")
                        }).ToList();

                        data = new { success = true, data = result };
                    }
                    else
                    {
                        data = new { success = false, message = "API Error: " + response.StatusCode, data = new List<object>() };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { success = false, message = ex.Message, data = new List<object>() };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateRemark(Domain.FRSAlert mFRSAlert)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mFRSAlert.UserClassId = ClsHttpContent.LoginUser.UserClassId;
                mFRSAlert.ResponsiblePersonId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlert);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("FRSAlert/UpdateRemark"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Remark has been Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data);
        }
        public ActionResult _FRSRemark(int id)
        {
            var mFRSAlertRemarks = new List<Domain.FRSAlertRemark>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSAlert/GetFRSRemark/FRSAlertId/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertRemarks = JsonConvert.DeserializeObject<List<Domain.FRSAlertRemark>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mFRSAlertRemarks = new List<Domain.FRSAlertRemark>();
            }
            return PartialView(mFRSAlertRemarks);
        }
        public JsonResult GetBy(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("FRSAlert/{0}", id)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString);
                            if (mFRSAlert == null)
                                mFRSAlert = new Domain.FRSAlert();

                            if (mFRSAlert != null && mFRSAlert.Id > 0)
                            {
                                var mAlertInfos = GetAlertInfos(mFRSAlert.AssetTypeId);
                                if (mAlertInfos != null && mAlertInfos.Count > 0)
                                {
                                    mAlertInfos = mAlertInfos.Where(x => x.Id != mFRSAlert.AlertInfoId).ToList();

                                    mFRSAlert.mAlertInfos = mAlertInfos;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }
            return Json(mFRSAlert, JsonRequestBehavior.AllowGet);
        }
        public List<Domain.AlertInfo> GetAlertInfos(int assetTypeId)
        {
            List<Domain.AlertInfo> mAlertInfos = new List<Domain.AlertInfo>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format($"AlertInfo/AssetTypeId/{assetTypeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertInfos = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mAlertInfos;
        }
        private string GetUserNameById(int userId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client
                        .GetAsync(String.Format("User/GetUserById/{0}", userId))
                        .Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var user = JsonConvert.DeserializeObject<Domain.User>(jsonString);
                        return user?.FirstName ?? user?.UserName ?? "User #" + userId;
                    }
                }
            }
            catch { }
            return "User #" + userId;
        }
        [HttpGet]
        public JsonResult GetNotificationDetail(int frsAlertId)
        {
            var result = new List<NotificationDetail>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client
                        .GetAsync($"SiteKeeping/GetNotificationDetail/FRSAlertId/{frsAlertId}")
                        .Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        result = JsonConvert.DeserializeObject<List<NotificationDetail>>(json)
                                 ?? new List<NotificationDetail>();

                        // Enrich each item with resolved user name
                        foreach (var item in result)
                        {
                            if (item.UserId > 0)
                                item.PersonName = GetUserNameById(item.UserId);
                        }
                    }
                }
            }
            catch { }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult GetTodayAlertsJson()
        {
            if (ClsHttpContent.LoginUser == null)
            {
                Response.StatusCode = 401;
                return Json(new { success = false, forbidden = true, alerts = new object[0] });
            }

            try
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1).AddSeconds(-1);

                var lister = new Domain.FRSAlertLister
                {
                    SearchCriteria = new Domain.FRSAlert
                    {
                        FromDate = today,
                        ToDate = tomorrow,
                        AlertStatus = 2   // Pending/Active only (unacknowledged)
                    },
                    Pager = new Domain.Pager
                    {
                        Skip = 0,
                        Take = 100,
                        PageSize = 100
                    }
                };

                lister.Pager.Take = lister.Pager.PageSize;
                lister = _frsAlertService.GetListerWithPagination(lister);

                // Same unacknowledged filter your GetAlertList uses
                if (lister?.mFRSAlerts != null && lister.SearchCriteria.AlertStatus == 2)
                {
                    lister.mFRSAlerts = lister.mFRSAlerts
                        .Where(x => x.AcknowledgemenTimeStamp == null)
                        .ToList();
                }

                var alerts = (lister?.mFRSAlerts ?? new List<Domain.FRSAlert>())
                    .Select(a => new
                    {
                        id = a.Id,
                        siteId = a.SiteId,
                        assetId = a.AssetId,
                        assetName = a.AssetName,
                        assetType = a.AssetType,
                        station = a.SiteName,
                        alertType = a.AlertType,
                        alertTypeId = a.AlertTypeId,
                        causeCode = a.CauseCode,
                        description = a.Description,
                        incidenceTime = a.SetTimeStamp
                    })
                    .ToList();

                return new JsonResult
                {
                    Data = new
                    {
                        success = true,
                        totalCount = alerts.Count,
                        alerts = alerts
                    },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    MaxJsonLength = int.MaxValue
                };
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message, alerts = new object[0] });
            }
        }

        //[HttpPost]
        //public JsonResult UploadPhotoEvidence()
        //{
        //    var savedNames = new List<string>();
        //    try
        //    {
        //        var files = Request.Files;
        //        if (files == null || files.Count == 0)
        //            return Json(new { success = false, message = "No files received." });

        //        var uploadFolder = Server.MapPath("~/Upload/AlertPhotoEvidence/");
        //        if (!Directory.Exists(uploadFolder))
        //            Directory.CreateDirectory(uploadFolder);

        //        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        //        const long maxBytes = 5 * 1024 * 1024;

        //        for (int i = 0; i < files.Count; i++)
        //        {
        //            var file = files[i];
        //            if (file == null || file.ContentLength == 0) continue;

        //            if (file.ContentLength > maxBytes)
        //                return Json(new { success = false, message = file.FileName + " exceeds 5MB." });

        //            var ext = Path.GetExtension(file.FileName).ToLower();
        //            if (!allowedExtensions.Contains(ext))
        //                return Json(new { success = false, message = "Invalid file type: " + ext });

        //            var safeName = Path.GetFileNameWithoutExtension(file.FileName).Replace(" ", "_");
        //            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        //            var newName = timestamp + "_" + safeName + ext;

        //            file.SaveAs(Path.Combine(uploadFolder, newName));
        //            savedNames.Add(newName);  // only filename, not full path
        //        }

        //        return Json(new { success = true, fileNames = savedNames });
        //    }
        //    catch (Exception ex)
        //    {
        //        return Json(new { success = false, message = ex.Message });
        //    }
        //}



        public ActionResult GetFRSRemarkImage(int id)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"FRSAlert/GetFRSRemarkBy/Id/{id}").Result;
                    string json = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        return Content(json, "application/json");
                }
            }
            catch { }
            return HttpNotFound();
        }
    }

}