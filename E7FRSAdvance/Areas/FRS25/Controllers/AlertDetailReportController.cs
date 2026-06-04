using System.Linq;
using System;
using System.Web.Mvc;
using E7FRSAdvance.Interface;
using System.Collections.Generic;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net;
using Domain;
using E7FRSAdvance.Utility;
using System.Text;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AlertDetailReportController : Controller
    {
        // GET: FRS25/AlertDetailReport
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        public AlertDetailReportController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService)
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
            ViewBag.CauseCode = new SelectList(GetCauseCode(), "Id", "Name");
            ViewBag.Sections = new SelectList(_sectionService.GetAll(), "Id", "Name");

            var mFRSAlertLister = new Domain.FRSAlertLister();
            var fromDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            mFRSAlertLister.SearchCriteria.FromDate = fromDate;
            mFRSAlertLister.SearchCriteria.ToDate = DateTime.Now;
            return View(mFRSAlertLister);
        }

        public ActionResult _List(Domain.FRSAlertLister mFRSAlertLister)
        {

            mFRSAlertLister.SearchCriteria.IsAcknowledgement = true;
            mFRSAlertLister.Pager.Take = mFRSAlertLister.Pager.PageSize;
            //mFRSAlertLister.SearchCriteria.AlertStatus = (int)E7FRSAdvance.Utility.Utility.AlertStatus.InActive;

            try
            {
                mFRSAlertLister = _frsAlertService.GetListerWithPagination(mFRSAlertLister);
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

            return PartialView(mFRSAlertLister);
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

        private List<Domain.AssetType> GetFRSAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
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

    }
}