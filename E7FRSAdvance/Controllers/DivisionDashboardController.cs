using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class DivisionDashboardController : Controller
    {
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;

        // GET: DivisionDashboard

        public DivisionDashboardController(IZoneService zoneService, IDivisionService divisionService, ISiteService siteService)
        {
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.siteService = siteService;
        }

        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _DivisionDashboardList(DivisionLister mDivisionLister)
        {
            mDivisionLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenaceOperation/GetDivisionLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionLister = JsonConvert.DeserializeObject<DivisionLister>(jsonString);
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
                ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
                ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
                ViewBag.Site = siteService.GetSite();
            }
            return PartialView(mDivisionLister);
        }


        public ActionResult _PreviousDayAlertList(SMSLogLister mSMSLogLister)
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

        public ActionResult _Last30AlertList(SMSLogLister mSMSLogLister)
        {
            try
            {
                mSMSLogLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-30);
                mSMSLogLister.SearchCriteria.ToDate = DateTime.Now;
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
            return PartialView("_PreviousDayAlertList", mSMSLogLister);
        }

        public ActionResult _FilterAlertList(SMSLogLister mSMSLogLister)
        {
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
            return PartialView("_PreviousDayAlertList", mSMSLogLister);
        }

        public ActionResult _DivisionReport(DivisionReportModelLister mDivisionReportModelLister)
        {
            mDivisionReportModelLister.Pager.Take = -1;
            if (mDivisionReportModelLister.SearchCriteria.StartDate != null && mDivisionReportModelLister.SearchCriteria.StartDate != DateTime.MinValue && mDivisionReportModelLister.SearchCriteria.EndDate != null && mDivisionReportModelLister.SearchCriteria.EndDate != DateTime.MinValue)
            {
            }
            else
            {
                mDivisionReportModelLister.SearchCriteria.StartDate = DateTime.Now.AddDays(-1);
                mDivisionReportModelLister.SearchCriteria.EndDate = DateTime.Now.AddDays(-1);
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionReportModelLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetDivisionReportModel"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionReportModelLister = JsonConvert.DeserializeObject<DivisionReportModelLister>(jsonString);
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
            return PartialView(mDivisionReportModelLister);
        }

        [HttpPost]
        public ActionResult GetA10Status(int divisionId)
        {
            var mA10Status = new List<Domain.A10Status>();
            var date = DateTime.Now.ToString("d-M-yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/DivisionId/{divisionId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            mA10Status.ForEach(x =>
                            {
                                DateTime.TryParse(x.timestamp, out DateTime cDate);
                                x.ConvertDate = cDate;
                            });
                            var maxdate = mA10Status.Select(x => x.ConvertDate).Max();
                            mA10Status = mA10Status.Where(x => x.ConvertDate == maxdate).ToList();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(mA10Status, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public ActionResult DownloadDivisionReportPDF(int divisionId, string date)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Maintenance/DownloadDivisionReportPDF/DivisionId/{divisionId}/Date/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                var mDivision = divisionService.Get(divisionId);
                                if (mDivision != null && mDivision.Id > 0)
                                    return File(csvbytes, "application/pdf", $"{mDivision.Name}-MaintenaceReport.pdf");
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
    }
}