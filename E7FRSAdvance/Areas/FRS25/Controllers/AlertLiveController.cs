using Domain;
using Domain.Dto;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Controllers;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AlertLiveController : Controller
    {
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService _assetAttributeService;
        private readonly ICardLineService _cardLineService;
        public AlertLiveController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService, ICardLineService cardLineService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
            _assetAttributeService = assetAttributeService;
            _cardLineService = cardLineService;
        }

        // GET: FRS25/AlertLive
        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            return View(new Domain.FRSAlertLister());
        }

        public ActionResult _List(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                //mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.Active;
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.Pager.Take = mFRSAlertLister.Pager.PageSize;
                mFRSAlertLister = _frsAlertService.GetListerWithPagination(mFRSAlertLister);

                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    foreach (var mFRSAlert in mFRSAlertLister.mFRSAlerts.GroupBy(x => new { x.AssetTypeId, x.AssetType }).Select(x => x.Key).ToList())
                    {
                        mFRSAlertLister.AssetTypes.Add(new Domain.AssetType() { Id = mFRSAlert.AssetTypeId, Name = mFRSAlert.AssetType });
                    }
                    SetTempRemark(mFRSAlertLister.mFRSAlerts);

                    TempData.Remove("TempFRSAlertLiveStatus");
                    TempData["TempFRSAlertLiveStatus"] = mFRSAlertLister.mFRSAlerts;
                    TempData.Keep();

                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.AppConfigs = GetAppConfigByGroup("FeedbackDuration");
                ViewBag.FRSAlertRemarks = GetFRSAlertRemark(mFRSAlertLister.mFRSAlerts.Select(x => x.Id).ToList());
            }
            return PartialView(mFRSAlertLister);
        }

        public List<Domain.AppConfig> GetAppConfigByGroup(string groupName)
        {
            var mAppConfigs = new List<Domain.AppConfig>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GroupName/{groupName}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAppConfigs = JsonConvert.DeserializeObject<List<Domain.AppConfig>>(jsonString);
                    }
                }
            }
            catch (Exception)
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
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(ids);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetFRSRemarkByIds"), str).Result;
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
            return mFRSAlertRemarks;
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

        public JsonResult GetAlertInfo(int assetTypeId)
        {
            return Json(GetAlertInfos(assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateAcknowledgementStatus(Domain.FRSAlert mFRSAlert)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mFRSAlert.ResponsiblePersonId = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlert);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("FRSAlert"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Status has been Updated" };
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

        public ActionResult UpdateRemark(Domain.FRSAlert mFRSAlert)
        {
            dynamic data = new ExpandoObject();
            try
            {
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

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetView(FRSAlertLister mFRSAlertLister)
        {
            dynamic data = new ExpandoObject();

            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.FromDate != null && mFRSAlertLister.SearchCriteria.FromDate == DateTime.MinValue)
                mFRSAlertLister.SearchCriteria.FromDate = DateTime.Now;

            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.ToDate != null && mFRSAlertLister.SearchCriteria.ToDate == DateTime.MinValue)
                mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;

            try
            {
                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlert(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null)
                {
                    if (mFRSAlertLister.SearchCriteria.GraphType == 1)
                    {
                        data = mFRSAlertLister.mFRSAlerts.GroupBy(n => n.AssetType)
                     .Select(n => new
                     {
                         Name = n.Key,
                         Count = n.Count()
                     }).ToList();


                    }
                    else if (mFRSAlertLister.SearchCriteria.GraphType == 2)
                    {
                        data = mFRSAlertLister.mFRSAlerts.GroupBy(n => new { n.AssetName, n.AssetType })
                     .Select(n => new
                     {
                         Name = n.Key.AssetName,
                         Type = n.Key.AssetType,
                         Count = n.Count()
                     }).ToList();


                    }
                    else if (mFRSAlertLister.SearchCriteria.GraphType == 3)
                    {
                        mFRSAlertLister.mFRSAlerts.ForEach(x =>
                        {
                            if (x.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive)
                            {
                                x.AlertType = E7FRSAdvance.Utility.Utility.AlertType.Predictive.ToString();
                            }

                            if (x.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure)
                            {
                                x.AlertType = E7FRSAdvance.Utility.Utility.AlertType.Failure.ToString();
                            }

                        });

                        data = mFRSAlertLister.mFRSAlerts.GroupBy(n => n.AlertType)
                     .Select(n => new
                     {
                         Name = n.Key,
                         Count = n.Count()
                     }).ToList();


                    }
                }



            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }


            return Json(data);
        }

        public ActionResult Debug(int siteId = 0, int assetTypeId = 0)
        {
            var site = new Domain.Site();

            if (siteId > 0)
                site = _siteService.Get(siteId);

            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");
            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            return View(site);
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

        public ActionResult DownloadFRSAlertLiveStatus()
        {
            var tempSMSLog = TempData.Peek("TempFRSAlertLiveStatus");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mFRSAlerts = tempSMSLog as List<Domain.FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", "SR.No", "Zone", "Division", "Section", "Station", "Alert Type", "Asset Type", "Asset Number", "Incidence Date & Time", "Duration (h:m)", "Rectification Date & Time", "Cause_code", "Status", "Alert Message", "Reset Message");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;
                    int counter = 0;
                    foreach (var item in mFRSAlerts)
                    {
                        counter++;
                        string alertType = string.Empty;
                        string alertStatus = string.Empty;
                        string duration = string.Empty;
                        string message = string.Empty;
                        string resetAlertMessage = string.Empty;
                        string resetTimeStamp = string.Empty;
                        if (item.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure)
                            alertType = E7FRSAdvance.Utility.Utility.AlertType.Failure.ToString();
                        else if (item.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive)
                            alertType = E7FRSAdvance.Utility.Utility.AlertType.Predictive.ToString();

                        if (item.IsActive)
                            alertStatus = E7FRSAdvance.Utility.Utility.AlertStatus.Active.ToString();
                        else
                            alertStatus = E7FRSAdvance.Utility.Utility.AlertStatus.InActive.ToString();

                        if (item.ResetTimeStamp != null && item.ResetTimeStamp.Value != DateTime.MinValue)
                        {
                            resetTimeStamp = item.ResetTimeStamp.Value.ToString();

                            var durationTime = (item.ResetTimeStamp.Value - item.SetTimeStamp);
                            duration = Convert.ToInt32(durationTime.TotalHours) + ":" + Convert.ToInt32(durationTime.Minutes);


                        }
                        if (item.SetAlertMessage.IsNotNullOrEmpty())
                        {
                            message += item.SetAlertMessage;
                            message += item.Description;
                            if (!string.IsNullOrEmpty(item.ResetAlertMessage))
                            {
                                resetAlertMessage = item.ResetAlertMessage;
                                resetAlertMessage = resetAlertMessage.Replace("\r", string.Empty);
                                resetAlertMessage = resetAlertMessage.Replace("\n", string.Empty);

                            }
                            message = message.Replace("\r", string.Empty);
                            message = message.Replace("\n", string.Empty);
                            message = message.Replace("<b>", string.Empty);
                            message = message.Replace("</b>", string.Empty);
                        }

                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", counter, item.ZoneName.RemoveComma(), item.DivisionName.RemoveComma(), item.SectionName.RemoveComma(), item.SiteName.RemoveComma(), alertType.RemoveComma(), item.AssetType.RemoveComma(), item.AssetName.RemoveComma(), item.SetTimeStamp.ToString(), duration, resetTimeStamp, item.CauseCode.RemoveComma(), alertStatus.RemoveComma(), message.RemoveComma(), resetAlertMessage.RemoveComma());
                        //val.Remark.RemoveComma());
                        csv.AppendLine(socsvstring);
                    }
                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", "attachment;filename=FRSAlerts" + DateTime.Now.Ticks + ".csv");
                    Response.Charset = "utf-8";
                    Response.ContentType = "text/csv";
                    Response.Output.Write(csv);
                    Response.Flush();
                    Response.End();
                }
            }

            return RedirectToAction("Index");
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

        public ActionResult DeleteFRSAlertById(int id)
        {
            bool result = false;
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("FRSAlert/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "FRS Alert has been Deleted." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
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

        public ActionResult DeleteFRSAlertByIds(List<int> ids)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(ids);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/DeleteMultiple"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "FRS Alert has been Deleted." };
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

        public ActionResult GetFRSAlertInstance(int frsAlertId)
        {
            List<Domain.FRSAlertInstance> mFRSAlertInstance = new List<Domain.FRSAlertInstance>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("FRSAlert/GetFRSAlertInstance/{0}", frsAlertId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertInstance = JsonConvert.DeserializeObject<List<Domain.FRSAlertInstance>>(jsonString);
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

            return PartialView("_FRSAlertInstance", mFRSAlertInstance);
        }

        public JsonResult UpdateTempRemark(int id, string tempRemark)
        {
            var TempFRSAlertLiveStatus = TempData.Peek("TempFRSAlertLiveStatus");
            if (TempFRSAlertLiveStatus != null && tempRemark.IsNotNullOrEmpty())
            {
                var mFRSAlerts = TempFRSAlertLiveStatus as List<Domain.FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    var seleFRSAlerts = mFRSAlerts.Where(x => x.Id == id).FirstOrDefault();
                    if (seleFRSAlerts != null && seleFRSAlerts.Id > 0)
                    {
                        seleFRSAlerts.TempRemark = tempRemark;


                        TempData.Remove("TempFRSAlertLiveStatus");
                        TempData["TempFRSAlertLiveStatus"] = mFRSAlerts;
                        TempData.Keep();
                    }
                }
            }
            return Json(JsonRequestBehavior.AllowGet);
        }

        public JsonResult ClearTempRemark()
        {
            var TempFRSAlertLiveStatus = TempData.Peek("TempFRSAlertLiveStatus");
            if (TempFRSAlertLiveStatus != null)
            {
                var mFRSAlerts = TempFRSAlertLiveStatus as List<FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    foreach (var item in mFRSAlerts)
                    {
                        item.TempRemark = null;
                    }

                    TempData.Remove("TempFRSAlertLiveStatus");
                    TempData["TempFRSAlertLiveStatus"] = mFRSAlerts;
                    TempData.Keep();
                }
            }
            return Json("", JsonRequestBehavior.AllowGet);
        }

        public ActionResult _DebugTrackFail(int id)
        {
            var mFRSAlert = GetFRSAlert(id);
            if (mFRSAlert != null && mFRSAlert.Id > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(String.Format("Asset/GetFamilyAsset/{0}", mFRSAlert.AssetId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            var mFamilyTracks = JsonConvert.DeserializeObject<List<Domain.FamilyTrack>>(jsonString);
                            if (mFamilyTracks != null && mFamilyTracks.Count > 0)
                            {
                                ViewBag.FamilyTracks = mFamilyTracks;
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


            return PartialView(mFRSAlert);
        }

        public ActionResult _DataLoggerEvent(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();
            // if (searchDate.IsNullOrEmpty())
            // searchCriteria.StartDate = DateTime.Now;
            // searchCriteria.EndDate = DateTime.Now;

            //if (searchCriteria.AssetName.IsNotNullOrEmpty())
            //{
            //    searchCriteria.RelayNames.Add(searchCriteria.AssetName);
            //}

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
                        mDataloggers = JsonConvert.DeserializeObject<List<Domain.Datalogger>>(jsonString);
                        if (mDataloggers != null && mDataloggers.Count > 0)
                        {
                            if (searchCriteria.RelayNames != null && searchCriteria.RelayNames.Count > 0)
                            {
                                mDataloggers = mDataloggers.Where(x => searchCriteria.RelayNames.Any(y => x.AssetName.StartsWith(y))).ToList();
                                //mDataloggers = mDataloggers.Where(x => x.AssetName.StartsWith(searchCriteria.RelayNames)).ToList();

                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return PartialView(mDataloggers);
        }

        public ActionResult _FRSAlertPartial(int id)
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
            return PartialView(mFRSAlert);
        }


        #region AlertAnalysis
        public ActionResult AlertAnalysis()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _AlertAnalysis(Domain.FRSAlertLister fRSAlertLister)
        {
            return View(_frsAlertService.GetAcknowledgementAllAlert(fRSAlertLister));
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

        public ActionResult GetMQTTHistoryData(int assetId)
        {
            string startDate = string.Empty;
            string endDate = string.Empty;
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

        public ActionResult GeDataloggerHistoryData(int assetId)
        {
            string startDate = string.Empty;
            string endDate = string.Empty;
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

        public ActionResult GeFRSAlertJSONData(int assetId)
        {
            string startDate = string.Empty;
            string endDate = string.Empty;
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


        #endregion



        #region Private Methods

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

        private List<Domain.Datalogger> DataLoggerEventGetMultipleAsset(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.Datalogger> mDataloggers = new List<Domain.Datalogger>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("DataloggerAsset/GetTempGraphDataByMultipleAsset"), str).Result;
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

        private MQTTDetailList GetMQTTDetailList()
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

        private List<Domain.AlertInfo> GetCauseCode()
        {
            List<Domain.AlertInfo> causeCodes = new List<Domain.AlertInfo>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("AlertInfo/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        causeCodes = JsonConvert.DeserializeObject<List<Domain.AlertInfo>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return causeCodes;
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

        private List<Domain.FRSAlertAudit> GetGroupWise()
        {
            List<Domain.FRSAlertAudit> mAlertAudits = new List<Domain.FRSAlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("FRSAlertAudit/GetGroupWise").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAlertAudits = JsonConvert.DeserializeObject<List<Domain.FRSAlertAudit>>(jsonString);
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
                ViewBag.Error = "Internal server error.";
            }
            return mAlertAudits;
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

        #endregion





    }
}