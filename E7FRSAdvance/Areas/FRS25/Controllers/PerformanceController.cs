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
    public class PerformanceController : Controller
    {
        // GET: FRS25/Performance
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public PerformanceController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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

            return View();
        }

        public ActionResult _List(FRSAlertLister mFRSAlertLister)
        {
            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.FromDate != null && mFRSAlertLister.SearchCriteria.FromDate == DateTime.MinValue)
            {
                var today = DateTime.Today;
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var fromDate = today.AddDays(-diff);

                mFRSAlertLister.SearchCriteria.FromDate = fromDate;

            }

            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.ToDate != null && mFRSAlertLister.SearchCriteria.ToDate == DateTime.MinValue)
            {
                mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;
            }

            mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
            mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            if (ClsHttpContent.LoginUser.IsSiteKeeping)
            {
                mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
            }
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlertWithManual(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    var dsds = mFRSAlertLister.mFRSAlerts.Where(x => x.isManual == 1).ToList();
                    foreach (var mFRSAlert in mFRSAlertLister.mFRSAlerts.GroupBy(x => new { x.AssetTypeId, x.AssetType }).Select(x => x.Key).ToList())
                    {
                        
                        mFRSAlertLister.AssetTypes.Add(new Domain.AssetType() { Id = mFRSAlert.AssetTypeId, Name = mFRSAlert.AssetType });
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }

            return PartialView(mFRSAlertLister);
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
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }
        [HttpPost]
        public ActionResult SaveActualFailures(SaveActualFailuresRequest model)
        {
            var responseModel = new APIResponse();
            try
            {
                if (model?.Failures == null || !model.Failures.Any())
                {
                    responseModel.IsSuccess = false;
                    responseModel.Message = "No failure records to save.";
                    return Json(responseModel, JsonRequestBehavior.AllowGet);
                }

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(model);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    var response = hcf.client.PostAsync("PerformanceAlert/SaveActualFailures", content).Result;

                    string responseContent = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        responseModel = JsonConvert.DeserializeObject<APIResponse>(responseContent);
                    }
                    else
                    {
                        responseModel.IsSuccess = false;
                        responseModel.Message = $"API error: {response.StatusCode} – {responseContent}";
                    }
                }
            }
            catch (Exception ex)
            {
                responseModel.IsSuccess = false;
                responseModel.Message = ex.Message;
            }

            return Json(responseModel, JsonRequestBehavior.AllowGet);
        }




        [HttpPost]
        public ActionResult GetActualFailureList(PerformanceAlertLister model)
        {
            var responseModel = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    // ── Serialize with MM/dd/yyyy date format for downstream API ──
                    var jsonSettings = new JsonSerializerSettings
                    {
                        DateFormatString = "MM/dd/yyyy",
                        NullValueHandling = NullValueHandling.Ignore
                    };

                    var jsonStr = JsonConvert.SerializeObject(model, jsonSettings);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("PerformanceAlert/GetLister", content).Result;
                    string raw = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<PerformanceAlertLister>(raw);
                        var rows = (result?.mpfmAlerts ?? new List<Domain.PerformanceAlert>())
                            .Select(x => new
                            {
                                x.Id,
                                StationName = x.SiteName,
                                x.AssetType,
                                AssetNo = x.AssetName,
                                x.FailureDateFrom,
                                x.FailureDateTo,
                                x.FailureTimeFrom,
                                x.FailureTimeTo,
                                x.RectificationDateTimeFormatted,
                                x.CauseCode,
                                x.AlertInfoId,
                                x.Remark,
                                EnteredBy = x.MaintainerName,
                                x.AssetTypeId,
                                x.CreatedBy,
                                x.AlertStatus,
                                x.GeneratedBy,
                                x.SiteId,
                                x.AssetId
                            });

                        return Json(rows, JsonRequestBehavior.AllowGet);
                    }

                    responseModel.IsSuccess = false;
                    responseModel.Message = string.Format("API error: {0} – {1}", response.StatusCode, raw);
                }
            }
            catch (Exception ex)
            {
                responseModel.IsSuccess = false;
                responseModel.Message = ex.Message;
            }
            return Json(responseModel, JsonRequestBehavior.AllowGet);
        }


        [HttpPost]
        public ActionResult UpdateActualFailure(ActualFailureRow model)
        {
            var responseModel = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(model);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("PerformanceAlert/UpdateActualFailure", content).Result;
                    string raw = response.Content.ReadAsStringAsync().Result;

                    System.Diagnostics.Debug.WriteLine($"RAW: {raw}");  // <-- check Output window

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        responseModel = JsonConvert.DeserializeObject<APIResponse>(raw);
                        System.Diagnostics.Debug.WriteLine($"IsSuccess: {responseModel?.IsSuccess}, Message: {responseModel?.Message}");
                    }
                    else
                    {
                        responseModel = new APIResponse { IsSuccess = false, Message = $"API error: {response.StatusCode}" };
                    }
                }
            }
            catch (Exception ex) { responseModel = new APIResponse { IsSuccess = false, Message = ex.Message }; }

            return Json(responseModel, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult DeleteActualFailure(int id)
        {
            var responseModel = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var url = $"PerformanceAlert/DeleteActualFailure?id={id}&deletedBy={ClsHttpContent.LoginUser.Id}";
                    var response = hcf.client.PostAsync(url, null).Result;
                    string raw = response.Content.ReadAsStringAsync().Result;

                    responseModel = response.StatusCode == HttpStatusCode.OK
                        ? JsonConvert.DeserializeObject<APIResponse>(raw)
                        : new APIResponse { IsSuccess = false, Message = $"API error: {response.StatusCode}" };
                }
            }
            catch (Exception ex) { responseModel = new APIResponse { IsSuccess = false, Message = ex.Message }; }

            return Json(responseModel, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public ActionResult Get(int Id)
        {
            object result = null;
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var payload = new StringContent(
                        JsonConvert.SerializeObject(new { Id = Id }),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = mHttpClientFactory.client
                        .PostAsync("PerformanceAlert/Get", payload).Result;

                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<dynamic>(jsonString);
                    }
                    else
                    {
                        return Json(new { IsSuccess = false, Message = "API error: " + response.StatusCode },
                                    JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message },
                            JsonRequestBehavior.AllowGet);
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

    }
}