using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using PayPal.Api.OpenIdConnect;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using System.Windows.Forms;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class FRSReportController : Controller
    {
        // GET: FRSReport
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public FRSReportController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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
            //ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            //ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            //ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            //ViewBag.AssetTypes = new SelectList(_assetTypeService.GetAll(), "Id", "Name");

            return View();
        }

        public ActionResult _AlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);

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

                    UpdateCookie(mFRSAlertLister);
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
                ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");

            }
            return PartialView(mFRSAlertLister);
        }

        public ActionResult _AssetList(Domain.AssetLister mAssetLister)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);

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
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
                ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            }
            return PartialView(mAssetLister);
        }

        public ActionResult _AlertDetailReportList(Domain.FRSAlertLister mFRSAlertLister)
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

            try
            {
                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlert(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
                    foreach (var mFRSAlert in mFRSAlertLister.mFRSAlerts.GroupBy(x => new { x.AssetTypeId, x.AssetType }).Select(x => x.Key).ToList())
                    {
                        mFRSAlertLister.AssetTypes.Add(new Domain.AssetType() { Id = mFRSAlert.AssetTypeId, Name = mFRSAlert.AssetType });
                    }

                    TempData.Remove("TempFRSAlertDetailReport");
                    TempData["TempFRSAlertDetailReport"] = mFRSAlertLister.mFRSAlerts;
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
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
                ViewBag.CauseCode = new SelectList(GetCauseCode(), "Id", "Name");
                ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            }

            return PartialView(mFRSAlertLister);
        }

        public ActionResult _AlertSummaryReport(FRSAlertLister mFRSAlertLister)
        {
            if (mFRSAlertLister != null && mFRSAlertLister.SearchCriteria != null && mFRSAlertLister.SearchCriteria.FromDate != null && mFRSAlertLister.SearchCriteria.FromDate == DateTime.MinValue)
            {
                var fromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
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
                mFRSAlertLister = _frsAlertService.GetWithAcknowledgementAlert(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                {
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
            finally
            {
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");
                ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
                ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");
            }

            return PartialView(mFRSAlertLister);
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
            if (zoneId > 0)
            {
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

                    }

                }
                catch (Exception)
                {
                    mDivisions = new List<Division>();
                }
            }
            else
            {
                mDivisions = new List<Division>();
            }

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetAlertInfo(int assetTypeId)
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

            return Json(mAlertInfos, JsonRequestBehavior.AllowGet);
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

        public ActionResult DownloadFRSAlertLiveStatus()
        {
            var tempSMSLog = TempData.Peek("TempFRSAlertLiveStatus");
            if (tempSMSLog != null)
            {
                var mFRSAlertAudits = GetFRSAlertAuditGroupWise();
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mFRSAlerts = tempSMSLog as List<Domain.FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    if (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode() || (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode() && E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsSiteKeeping))
                    {
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}", "SR.No", "Zone", "Division", "Section", "Station", "Alert Type", "Asset Type", "Asset Number", "Incidence Date & Time", "Duration (h:m)", "Rectification Date & Time", "Cause_code", "Status", "Alert Message", "Reset Message", "Alert Instance", "True/False", "Issue", "Audit By");
                    }
                    else
                    {
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", "SR.No", "Zone", "Division", "Section", "Station", "Alert Type", "Asset Type", "Asset Number", "Incidence Date & Time", "Duration (h:m)", "Rectification Date & Time", "Cause_code", "Status", "Alert Message", "Reset Message");
                    }


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

                        if (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.Admin.GetHashCode() || (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.RoleId == E7FRSAdvance.Utility.Utility.Role.User.GetHashCode() && E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsSiteKeeping))
                        {
                            string issueType = string.Empty;
                            string auditBy = string.Empty;
                            string isAlert = string.Empty;
                            if (mFRSAlertAudits != null && mFRSAlertAudits.Count > 0)
                            {
                                var mFRSAlertAudit = mFRSAlertAudits.Where(x => x.FRSAlertId == item.Id).FirstOrDefault();
                                if (mFRSAlertAudit != null && mFRSAlertAudit.Id > 0)
                                {
                                    auditBy = mFRSAlertAudit.CreatedName;

                                    if (mFRSAlertAudit.IsAlert)
                                        isAlert = "True";
                                    else
                                        isAlert = "False";

                                    if (item.IsSetWatchList != null && item.IsSetWatchList.Value && mFRSAlertAudit.IssueTypeId != null)
                                    {

                                        issueType = Convert.ToString((E7FRSAdvance.Utility.Utility.FRSAlertIssue)Enum.ToObject(typeof(E7FRSAdvance.Utility.Utility.FRSAlertIssue), mFRSAlertAudit.IssueTypeId.Value));
                                    }
                                }
                            }


                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}", counter, item.ZoneName.RemoveComma(), item.DivisionName.RemoveComma(), item.SectionName.RemoveComma(), item.SiteName.RemoveComma(), alertType.RemoveComma(), item.AssetType.RemoveComma(), item.AssetName.RemoveComma(), item.SetTimeStamp.ToString(), duration, resetTimeStamp, item.CauseCode.RemoveComma(), alertStatus.RemoveComma(), message.RemoveComma(), resetAlertMessage.RemoveComma(), item.AlertInstanceCount, isAlert, issueType, auditBy);
                        }
                        else
                        {
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}", counter, item.ZoneName.RemoveComma(), item.DivisionName.RemoveComma(), item.SectionName.RemoveComma(), item.SiteName.RemoveComma(), alertType.RemoveComma(), item.AssetType.RemoveComma(), item.AssetName.RemoveComma(), item.SetTimeStamp.ToString(), duration, resetTimeStamp, item.CauseCode.RemoveComma(), alertStatus.RemoveComma(), message.RemoveComma(), resetAlertMessage.RemoveComma());
                        }

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

        private List<Domain.FRSAlertAudit> GetFRSAlertAuditGroupWise()
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

        public ActionResult DownloadFRSAlertDetail()
        {
            var tempSMSLog = TempData.Peek("TempFRSAlertDetailReport");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mFRSAlerts = tempSMSLog as List<Domain.FRSAlert>;
                if (mFRSAlerts != null && mFRSAlerts.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}", "SR.No", "Zone", "Division", "Section", "Station", "Asset Type", "Asset Number", "Alert Type", "Alert Status", "Cause code", "Incidence Date & Time", "Rectification Date & Time", "Incidence Duration (h:m)", "Alert Feedback", "Alert feedback date & time", "Maintainer Name", "Maintainer Designation", "Maintainer Mobile Number", "Maintainer Remarks");

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
                        string alertFeedback = string.Empty;
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
                            TimeSpan span = item.ResetTimeStamp.Value.Subtract(item.SetTimeStamp);
                            duration = $"{Convert.ToInt32(span.Hours)}:{Convert.ToInt32(span.Minutes)}";
                        }

                        if (item.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True)
                            alertFeedback = E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True.ToString();
                        else if (item.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.False)
                            alertFeedback = E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.False.ToString();
                        else if (item.AcknowledgemenStatusId == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace)
                            alertFeedback = E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace.ToString();


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
                        }

                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}", counter, item.ZoneName.RemoveComma(), item.DivisionName.RemoveComma(), item.SectionName.RemoveComma(), item.SiteName.RemoveComma(), item.AssetType.RemoveComma(), item.AssetName.RemoveComma(), alertType.RemoveComma(), alertStatus.RemoveComma(), item.CauseCode.RemoveComma(), item.SetTimeStamp.ToString(), resetTimeStamp, duration, alertFeedback.RemoveComma(), item.AcknowledgemenTimeStamp.ToString(), item.MaintainerName.RemoveComma(), item.MaintainerDesignation.RemoveComma(), item.MaintainerMobileNumber.RemoveComma(), item.Remark.RemoveComma());
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

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
            //ViewBag.StatusEnumList = new SelectList(enumData, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
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

        private void UpdateCookie(Domain.FRSAlertLister mFRSAlertLister)
        {
            Dictionary<int, bool> userInfo = new Dictionary<int, bool>();
            try
            {
                var cookie = Request.Cookies["ActiveAlertCount"];
                if (cookie != null)
                {
                    var token = cookie.Values["UserInfo"];
                    var json = (string)HttpRuntime.Cache.Get("ActiveAlertCount:" + token);
                    if (mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                    {
                        foreach (var id in mFRSAlertLister.mFRSAlerts.Select(x => x.Id).ToList())
                        {
                            userInfo.Add(id, false);
                        }
                    }

                    var cookieuserInfo = JsonConvert.DeserializeObject<Dictionary<int, bool>>(json);

                    if (cookieuserInfo != null)
                    {
                        var keys = cookieuserInfo.Keys.ToList();
                        for (int i = 0; i < keys.Count; i++)
                        {
                            int key = keys[i];
                            cookieuserInfo[key] = true; // set all to false
                        }

                        string dictJson = JsonConvert.SerializeObject(cookieuserInfo);
                        //c.Values["UserInfo"] = JsonConvert.SerializeObject(cookieuserInfo);
                        //c.HttpOnly = true; c.Secure = true; c.SameSite = SameSiteMode.Lax; c.Path = "/";
                        //Response.Cookies.Set(c);

                        HttpRuntime.Cache.Insert(
                     key: "ActiveAlertCount:" + token,
                     value: dictJson,
                     dependencies: null,
                     absoluteExpiration: DateTime.UtcNow.AddDays(15),
                     slidingExpiration: Cache.NoSlidingExpiration);

                        // 2) Put only the token in the cookie
                        cookie.Values["UserInfo"] = token;        // tiny!
                        cookie.Expires = DateTime.Now.AddDays(1);
                        cookie.HttpOnly = true; cookie.Secure = true; cookie.SameSite = SameSiteMode.Lax;

                        Response.Cookies.Set(cookie);
                    }
                }
            }
            catch (Exception)
            {

            }

        }

    }
}