using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Policy;
using System.Text;
using System.Web.Mvc;
using System.Web.Razor.Parser.SyntaxTree;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class AlertAnalysisController : Controller
    {
        // GET: AlertAnalysis
        private readonly IUserService _userService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly ISiteService _siteService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService _assetAttributeService;

        public AlertAnalysisController(IUserService userService,IZoneService zoneService, IDivisionService divisionService, ISiteService siteService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService)
        {
            _userService = userService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _siteService = siteService;
            _frsAlertService = frsAlertService;
            _assetAttributeService = assetAttributeService;
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult _AlertAnalysis(SearchCriteria mSearchCriteria)
        {
            mSearchCriteria.StartDate = DateTime.Now;
            mSearchCriteria.EndDate = DateTime.Now;
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Users= new SelectList(_userService.GetAuditUser(), "Id", "Name");
            return PartialView(mSearchCriteria);
        }

        public ActionResult _AlertAnalysisList(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;
            }
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
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            return PartialView(mSMSLogLister);
        }

        public ActionResult _EditAlertAnalysisList(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.Validity = true;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
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
            return PartialView(mSMSLogLister);
        }

        public ActionResult _EditAllAlertAnalysisList(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.Validity = true;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
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
            finally
            {
                ViewBag.Divisions = _divisionService.GetAllDivisions();
            }
            return PartialView(mSMSLogLister);
        }

        public ActionResult _EditAllAlertAnalysisListByZone(SMSLogLister mSMSLogLister)
        {
            mSMSLogLister.Pager.Take = -1;
            mSMSLogLister.SearchCriteria.Validity = true;
            mSMSLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSMSLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
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
            finally
            {
                ViewBag.Divisions = _divisionService.GetAllDivisions();
                ViewBag.Zones = _zoneService.GetAllZones();
            }
            return PartialView(mSMSLogLister);
        }

        public ActionResult AlertAnalysisAudit(string searchDate)
        {
            SearchCriteria searchCriteria = new SearchCriteria();
            searchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(searchDate, "M/d/yyyy");
            searchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(searchDate, "M/d/yyyy");
            return View(searchCriteria);
        }

        public ActionResult _AlertAnalysisAuditList(SearchCriteria searchCriteria)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AlertAudit/StartDate/{searchCriteria.StartDate.ToShortDateString().Replace("/", "-")}/EndDate/{searchCriteria.EndDate.ToShortDateString().Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Error = "Internal server error.";
            }
            return PartialView(mAlertAudits);
        }

        public ActionResult _AddAlertAnalysisAuditRemark(int id)
        {
            var mAlertAudit = new AlertAudit();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AlertAudit/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudit = JsonConvert.DeserializeObject<Domain.AlertAudit>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView(mAlertAudit);
        }

        public ActionResult UpdateAlertAnalysisAudit(AlertAudit mAlertAudit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAudit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"AlertAudit/Id/{mAlertAudit.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        data = new { type = "success", result = "Status Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal Server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal Server error." };
            }


            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _IsAlertTrueList(SearchCriteria mSearchCriteria)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AlertAudit/GetByIsAlertTrue/StartDate/{mSearchCriteria.StartDate.ToShortDateString().Replace("/", "-")}/EndDate/{mSearchCriteria.EndDate.ToShortDateString().Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Error = "Internal server error.";
            }
            return PartialView("_AlertAnalysisAuditList", mAlertAudits);
        }

        public ActionResult _AlertList(SearchCriteria mSearchCriteria)
        {
            //if (mSearchCriteria.Month.IsNotNullOrEmpty())
            //    mSearchCriteria.TimeStamp = DateTime.ParseExact(mSearchCriteria.Month, "MMMM", CultureInfo.CurrentCulture);
            //else
            //    mSearchCriteria.TimeStamp = DateTime.Now;

            var mSMSLogLister = new SMSLogLister();
            mSMSLogLister.SearchCriteria.SiteId = mSearchCriteria.SiteId;
            mSMSLogLister.SearchCriteria.AssetId = mSearchCriteria.AssetId;
            mSMSLogLister.SearchCriteria.FromDate = mSearchCriteria.TimeStamp;
            mSMSLogLister.SearchCriteria.ToDate = mSearchCriteria.TimeStamp;
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
            return PartialView(mSMSLogLister);
        }

        #region FRSAlert

        public ActionResult _AddFRSAlertAnalysisAuditRemark(int id)
        {
            var mAlertAudit = new AlertAudit();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("AlertAudit/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudit = JsonConvert.DeserializeObject<Domain.AlertAudit>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView(mAlertAudit);
        }

        public ActionResult _FRSAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetFalseAcknowledgementFRSAlert(mFRSAlertLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.FRSAlertAudits = GetGroupWise();
            }
            return PartialView(mFRSAlertLister);
        }

        public ActionResult _SRNumberViewPartial(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetFalseAcknowledgementFRSAlert(mFRSAlertLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.FRSAlertAudits = GetGroupWise();
            }
            return PartialView(mFRSAlertLister);
        }

        public ActionResult _EditFRSAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.Divisions = _divisionService.GetAllDivisions();
                ViewBag.Zones = _zoneService.GetAllZones();
            }
            return PartialView(mFRSAlertLister);
        }

        public ActionResult _EditAllFRSAlertAnalysisListByDivision(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.Divisions = _divisionService.GetAllDivisions();
                ViewBag.Zones = _zoneService.GetAllZones();
            }

            return PartialView(mFRSAlertLister);
        }

        public ActionResult _EditAllFRSAlertAnalysisListByZone(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }
            finally
            {
                ViewBag.Divisions = _divisionService.GetAllDivisions();
                ViewBag.Zones = _zoneService.GetAllZones();
            }

            return PartialView(mFRSAlertLister);
        }

        public ActionResult UpdateFRSAlertAnalysisAudit(Domain.FRSAlertAudit mFRSAlertAudit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mFRSAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertAudit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("FRSAlertAudit", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Alert remark has been Update." };
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

        public ActionResult FRSAlertAnalysisAudit()
        {
            SearchCriteria searchCriteria = new SearchCriteria();
            searchCriteria.StartDate = DateTime.Now;
            searchCriteria.EndDate = DateTime.Now;
            return View(searchCriteria);
        }

        public ActionResult _FRSAlertAnalysisAuditList(SearchCriteria searchCriteria)
        {
            List<Domain.FRSAlertAudit> mAlertAudits = new List<Domain.FRSAlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlertAudit/GetList"), str).Result;
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
            return PartialView(mAlertAudits);
        }

        public ActionResult _AddSingleFRSAlertAnalysisAuditRemark(int id)
        {
            var mFRSAlertAudit = new FRSAlertAudit();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("FRSAlertAudit/Id/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertAudit = JsonConvert.DeserializeObject<Domain.FRSAlertAudit>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return PartialView(mFRSAlertAudit);
        }

        public ActionResult UpdateRemarkFRSAlertAnalysisAudit(FRSAlertAudit mAlertAudit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mAlertAudit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAlertAudit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"FRSAlertAudit/Id/{mAlertAudit.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //var deviceINIs = JsonConvert.DeserializeObject<List<AssetInfo>>(jsonString);
                        data = new { type = "success", result = "Status Updated" };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal Server error." };
                    }
                }
            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal Server error." };
            }


            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _FRSAlertWithOutGroupList(Domain.FRSAlertLister mFRSAlertLister)
        {
            mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);
            return PartialView(mFRSAlertLister);
        }

        public ActionResult DownloadFRSAlert(SearchCriteria searchCriteria)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlertAudit/GetList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var mAlertAudits = JsonConvert.DeserializeObject<List<Domain.FRSAlertAudit>>(jsonString);

                        var csv = new StringBuilder();
                        var socsvstring = string.Empty;
                        if (mAlertAudits != null && mAlertAudits.Count > 0)
                        {
                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", "Tabulation Remark", "Primary Remark", "Primary Status", "Time Stamp", "Created By", "Primary Remark Updated By");

                            csv.AppendLine(socsvstring);

                            foreach (var val in mAlertAudits)
                            {
                                string maintainerRemark = string.Empty;
                                string isMaintainerAlert = string.Empty;
                                string lastModifiedName = string.Empty;

                                if (val.CreatedDate != val.LastModifiedDate)
                                {
                                    maintainerRemark = val.MaintainerRemark;
                                }
                                if (val.CreatedDate != val.LastModifiedDate)
                                {
                                    if (val.IsMaintainerAlert)
                                    {
                                        isMaintainerAlert = "True";
                                    }
                                    else
                                    {
                                        isMaintainerAlert = "False";
                                    }
                                }
                                if (val.CreatedDate != val.LastModifiedDate)
                                {
                                    lastModifiedName = val.LastModifiedName;
                                }

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", val.Remark.RemoveComma(), maintainerRemark.RemoveComma(), isMaintainerAlert, val.CreatedDate.ToString().RemoveComma(), val.CreatedName, lastModifiedName.RemoveComma());
                                csv.AppendLine(socsvstring);

                            }

                            Response.Clear();
                            Response.Buffer = true;
                            Response.AddHeader("content-disposition", "attachment;filename=FRSAlert" + DateTime.Now.Ticks + ".csv");
                            Response.Charset = "utf-8";
                            Response.ContentType = "text/csv";
                            Response.Output.Write(csv);
                            Response.Flush();
                            Response.End();
                        }
                        else
                            return RedirectToAction("Index");

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



            return View();
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

        public List<Domain.FRSAlertAudit> GetFRSAlertAnalysisAuditList(SearchCriteria searchCriteria)
        {
            List<Domain.FRSAlertAudit> mAlertAudits = new List<Domain.FRSAlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlertAudit/GetList"), str).Result;
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

        public List<Domain.FRSAlertAudit> GetGroupWise()
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

        public ActionResult GetFRSAlertAuditBy(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
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
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }
            finally
            {
                ViewBag.AssetAttributes = _assetAttributeService.GetAssetAttributesBy(mFRSAlert.AssetTypeId);
            }
            return PartialView("_FRSAlertAudit", mFRSAlert);
        }

        public ActionResult GetFRSAlertAuditViewBy(int assetId, int alertInfoId)
        {
            var mFRSAlertAudits = new List<Domain.FRSAlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSAlertAudit/AssetId/{assetId}/AlertInfoId/{alertInfoId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertAudits = JsonConvert.DeserializeObject<List<Domain.FRSAlertAudit>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView("_FRSAlertAuditView", mFRSAlertAudits);
        }

     

        #endregion

        #region Alert Summary

        public ActionResult AlertSummary()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            return View(new Domain.SearchCriteria());
        }

        public ActionResult _AlertSummaryCountList(Domain.SearchCriteria SearchCriteria)
        {
            var mFRSAlertAudits = GetFRSAlertAnalysisAuditList(SearchCriteria);

            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();
            mFRSAlertLister.SearchCriteria.FromDate = SearchCriteria.StartDate;
            mFRSAlertLister.SearchCriteria.ToDate = SearchCriteria.EndDate;
            mFRSAlertLister.SearchCriteria.DivisionId = SearchCriteria.DivisionId;
            mFRSAlertLister.SearchCriteria.SiteId = SearchCriteria.SiteId;
            if (mFRSAlertLister != null)
            {
                ViewBag.StartDate = SearchCriteria.StartDate.ToShortDateString();
                ViewBag.EndDate = SearchCriteria.EndDate.ToShortDateString();
                var frsdarta = GetFRSAlertListCount(mFRSAlertLister);
                if (mFRSAlertLister != null)
                {
                    ViewBag.FRSAlerts = frsdarta;

                }
            }

            return PartialView(mFRSAlertAudits);
        }

        public ActionResult _AlertSummaryDetailList(Domain.SearchCriteria SearchCriteria)
        {
            return PartialView(GetFRSAlertAnalysisAuditList(SearchCriteria));
        }

        public ActionResult _AlertSummarySiteWiseList(Domain.SearchCriteria SearchCriteria)
        {
            ViewBag.Zones = _zoneService.GetAll();
            ViewBag.Divisions = _divisionService.GetAll();
            return PartialView(GetFRSAlertAnalysisAuditList(SearchCriteria));
        }

        public ActionResult _AlertSummaryDateWiseList(Domain.SearchCriteria SearchCriteria)
        {
            ViewBag.Zones = _zoneService.GetAll();
            ViewBag.Divisions = _divisionService.GetAll();
            ViewBag.StartDate = SearchCriteria.StartDate;
            ViewBag.EndDate = SearchCriteria.EndDate;
            return PartialView(GetFRSAlertAnalysisAuditList(SearchCriteria));
        }

        public ActionResult DownloadAlertSummary(Domain.SearchCriteria SearchCriteria)
        {
            var mFRSAlertAudits = GetFRSAlertAnalysisAuditList(SearchCriteria);

            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();
            mFRSAlertLister.SearchCriteria.FromDate = SearchCriteria.StartDate;
            mFRSAlertLister.SearchCriteria.ToDate = SearchCriteria.EndDate;
            mFRSAlertLister.SearchCriteria.DivisionId = SearchCriteria.DivisionId;
            mFRSAlertLister.SearchCriteria.SiteId = SearchCriteria.SiteId;
            if (mFRSAlertLister != null && mFRSAlertAudits != null)
            {
                var mFRSAlerts = GetFRSAlertAuditIds(mFRSAlertLister);
                var csv = new StringBuilder();
                var socsvstring = string.Empty;

                if (SearchCriteria.Type == "All")
                {
                    Dictionary<int, int> totalCount = new Dictionary<int, int>();

                    socsvstring = string.Format("{0},{1},{2}", "Date", "Number Of Alerts", "Number Of Audit");
                    var enumData = from E7FRSAdvance.Utility.Utility.FRSAlertIssue e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAlertIssue))
                                   select new
                                   {
                                       Id = (int)e,
                                       Name = e.ToString().Replace("_", " ")
                                   };

                    foreach (var item in enumData)
                    {
                        socsvstring += $",{item.Name}";
                    }
                    socsvstring += $",True";
                    csv.AppendLine(socsvstring);

                    socsvstring = string.Empty;
                    int totalTrue = 0;
                    int totalaudit = 0;
                    for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate.ToShortDateString()); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate.ToShortDateString()); dateTime += TimeSpan.FromDays(1))
                    {
                        var thisDateData = mFRSAlertAudits.Where(x => x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date).ToList();

                        int frsCount = 0;
                        var frsallData = mFRSAlerts.Where(x => x.Key.Date == dateTime.Date).Select(x => x.Value).ToList();
                        if (frsallData != null && frsallData.Count > 0)
                        {
                            frsCount = frsallData.FirstOrDefault().Count();
                        }

                        totalTrue += thisDateData.Where(x => x.IsAlert).Count();
                        totalaudit += thisDateData.Count();
                        socsvstring = string.Format("{0},{1},{2}", $"{dateTime.ToShortDateString()}", $"{frsCount}", $"{thisDateData.Count()}");

                        foreach (var item in enumData)
                        {
                            var count = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                            socsvstring += $",{count}";

                            if (totalCount.Where(x => x.Key == item.Id).Count() > 0)
                            {
                                var lcount = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                                var lastCount = totalCount.Where(x => x.Key == item.Id).FirstOrDefault().Value;

                                totalCount[item.Id] = lastCount + lcount;
                            }
                            else
                            {
                                totalCount.Add(item.Id, thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count());

                            }
                        }
                        socsvstring += $",{thisDateData.Where(x => x.IsAlert).Count()}";
                        csv.AppendLine(socsvstring);
                    }

                    socsvstring = string.Empty;
                    socsvstring = string.Format("{0},{1},{2}", $"Total", $"", $"{totalaudit}");
                    foreach (var item in enumData)
                    {
                        if (totalCount.Where(x => x.Key == item.Id).Count() > 0)
                        {
                            var lastCount = totalCount.Where(x => x.Key == item.Id).FirstOrDefault().Value;
                            socsvstring += $",{lastCount}";
                        }
                        else
                        {
                            socsvstring += $",{0}";
                        }
                    }
                    socsvstring += $",{totalTrue}";
                    csv.AppendLine(socsvstring);


                    var assetTypes = mFRSAlertAudits.GroupBy(x => new { x.AssetTypeId, x.AssetType }).Select(x => x.Key).ToList();
                    foreach (var assetType in assetTypes)
                    {
                        socsvstring = string.Empty;
                        csv.AppendLine(socsvstring);
                        csv.AppendLine(socsvstring);
                        csv.AppendLine(assetType.AssetType);
                        Dictionary<int, int> totalCountAT = new Dictionary<int, int>();

                        socsvstring = string.Format("{0},{1},{2}", "Date", "Number Of Alerts", "Number Of Audit");


                        foreach (var item in enumData)
                        {
                            socsvstring += $",{item.Name}";
                        }
                        socsvstring += $",True";
                        csv.AppendLine(socsvstring);

                        socsvstring = string.Empty;
                        int totalTrueAT = 0;
                        int totalauditAT = 0;
                        for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate.ToShortDateString()); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate.ToShortDateString()); dateTime += TimeSpan.FromDays(1))
                        {
                            var thisDateData = mFRSAlertAudits.Where(x => x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.AssetTypeId == assetType.AssetTypeId).ToList();

                            var frsCount = 0;
                            var frsCountData = mFRSAlerts.Where(x => x.Key.Date == dateTime.Date).Select(x => x.Value).ToList();
                            if (frsCountData != null && frsCountData.Count > 0)
                            {
                                frsCount = frsCountData.FirstOrDefault().Where(x => x.AssetTypeId == assetType.AssetTypeId).Count();
                            }

                            totalTrueAT += thisDateData.Where(x => x.IsAlert).Count();
                            totalauditAT += thisDateData.Count();
                            socsvstring = string.Format("{0},{1},{2}", $"{dateTime.ToShortDateString()}", $"{frsCount}", $"{thisDateData.Count()}");

                            foreach (var item in enumData)
                            {
                                var count = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                                socsvstring += $",{count}";

                                if (totalCountAT.Where(x => x.Key == item.Id).Count() > 0)
                                {
                                    var lcount = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                                    var lastCount = totalCountAT.Where(x => x.Key == item.Id).FirstOrDefault().Value;

                                    totalCountAT[item.Id] = lastCount + lcount;
                                }
                                else
                                {
                                    totalCountAT.Add(item.Id, thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count());

                                }
                            }
                            socsvstring += $",{thisDateData.Where(x => x.IsAlert).Count()}";
                            csv.AppendLine(socsvstring);
                        }

                        socsvstring = string.Empty;
                        socsvstring = string.Format("{0},{1},{2}", $"Total", $"", $"{totalauditAT}");
                        foreach (var item in enumData)
                        {
                            if (totalCountAT.Where(x => x.Key == item.Id).Count() > 0)
                            {
                                var lastCount = totalCountAT.Where(x => x.Key == item.Id).FirstOrDefault().Value;
                                socsvstring += $",{lastCount}";
                            }
                            else
                            {
                                socsvstring += $",{0}";
                            }
                        }
                        socsvstring += $",{totalTrueAT}";
                        csv.AppendLine(socsvstring);


                        socsvstring = string.Empty;
                        csv.AppendLine(socsvstring);
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", "Station", "Asset Type", "Asset", "Cause Code", "Alert Message", "Alert Timestamp", "Remark", "True/False", "Issue Type", "Audit By");

                        csv.AppendLine(socsvstring);

                        socsvstring = string.Empty;

                        foreach (var mFRSAlertAudit in mFRSAlertAudits.Where(x => x.AssetTypeId == assetType.AssetTypeId))
                        {
                            string itType = string.Empty;
                            string msg = string.Empty;
                            if (mFRSAlertAudit.IssueTypeId != null)
                            {
                                itType = Convert.ToString((E7FRSAdvance.Utility.Utility.FRSAlertIssue)Enum.ToObject(typeof(E7FRSAdvance.Utility.Utility.FRSAlertIssue), mFRSAlertAudit.IssueTypeId.Value));
                            }

                            if (mFRSAlertAudit.AlertMessage.IsNotNullOrEmpty())
                                msg = mFRSAlertAudit.AlertMessage.Replace("\r", "").Replace("\n", "");

                            string timestamp = string.Empty;
                            if (mFRSAlertAudit.TimeStamp != null && mFRSAlertAudit.TimeStamp.Value != DateTime.MinValue)
                            {
                                timestamp = mFRSAlertAudit.TimeStamp.Value.ToString("dd/MM/yyyy HH:mm:ss");
                            }

                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", $"{mFRSAlertAudit.Site}", $"{mFRSAlertAudit.AssetType}", $"{mFRSAlertAudit.Asset}", $"{mFRSAlertAudit.CauseCode}", $"{msg.RemoveComma()}", $"{timestamp}", $"{mFRSAlertAudit.Remark.RemoveComma()}", $"{mFRSAlertAudit.IsAlert}", $"{itType}", $"{mFRSAlertAudit.CreatedName}");
                            csv.AppendLine(socsvstring);
                        }
                    }



                }
                else if (SearchCriteria.Type == "Count")
                {
                    Dictionary<int, int> totalCount = new Dictionary<int, int>();

                    socsvstring = string.Format("{0},{1},{2}", "Date", "Number Of Alerts", "Number Of Audit");
                    var enumData = from E7FRSAdvance.Utility.Utility.FRSAlertIssue e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAlertIssue))
                                   select new
                                   {
                                       Id = (int)e,
                                       Name = e.ToString().Replace("_", " ")
                                   };

                    foreach (var item in enumData)
                    {
                        socsvstring += $",{item.Name}";
                    }
                    socsvstring += $",True";
                    csv.AppendLine(socsvstring);

                    socsvstring = string.Empty;
                    int totalTrue = 0;
                    int totalAlert = 0;
                    int totalaudit = 0;
                    for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate.ToShortDateString()); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate.ToShortDateString()); dateTime += TimeSpan.FromDays(1))
                    {
                        var thisDateData = mFRSAlertAudits.Where(x => x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date).ToList();

                        var frsCount = 0;
                        var frsCountData = mFRSAlerts.Where(x => x.Key.Date == dateTime.Date).Select(x => x.Value).ToList();
                        if (frsCountData != null && frsCountData.Count > 0)
                        {
                            frsCount = frsCountData.FirstOrDefault().Count;
                        }

                        totalAlert += frsCount;
                        totalTrue += thisDateData.Where(x => x.IsAlert).Count();
                        totalaudit += thisDateData.Count();
                        socsvstring = string.Format("{0},{1},{2}", $"{dateTime.ToShortDateString()}", $"{frsCount}", $"{thisDateData.Count()}");

                        foreach (var item in enumData)
                        {
                            var count = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                            socsvstring += $",{count}";

                            if (totalCount.Where(x => x.Key == item.Id).Count() > 0)
                            {
                                var lcount = thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count();
                                var lastCount = totalCount.Where(x => x.Key == item.Id).FirstOrDefault().Value;

                                totalCount[item.Id] = lastCount + lcount;
                            }
                            else
                            {
                                totalCount.Add(item.Id, thisDateData.Where(x => x.IssueTypeId != null && x.IssueTypeId == item.Id).Count());

                            }
                        }
                        socsvstring += $",{thisDateData.Where(x => x.IsAlert).Count()}";
                        csv.AppendLine(socsvstring);
                    }

                    socsvstring = string.Empty;
                    socsvstring = string.Format("{0},{1},{2}", $"Total", $"{totalAlert}", $"{totalaudit}");
                    foreach (var item in enumData)
                    {
                        if (totalCount.Where(x => x.Key == item.Id).Count() > 0)
                        {
                            var lastCount = totalCount.Where(x => x.Key == item.Id).FirstOrDefault().Value;
                            socsvstring += $",{lastCount}";
                        }
                        else
                        {
                            socsvstring += $",{0}";
                        }
                    }
                    socsvstring += $",{totalTrue}";
                    csv.AppendLine(socsvstring);

                }
                else if (SearchCriteria.Type == "Detail")
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", "Station", "Asset Type", "Asset", "Cause Code", "Alert Message", "Alert Timestamp", "Remark", "True/False", "Issue Type", "Audit By");
                    csv.AppendLine(socsvstring);

                    socsvstring = string.Empty;
                    foreach (var mFRSAlertAudit in mFRSAlertAudits)
                    {
                        string itType = string.Empty;
                        string msg = string.Empty;
                        if (mFRSAlertAudit.IssueTypeId != null)
                        {
                            itType = Convert.ToString((E7FRSAdvance.Utility.Utility.FRSAlertIssue)Enum.ToObject(typeof(E7FRSAdvance.Utility.Utility.FRSAlertIssue), mFRSAlertAudit.IssueTypeId.Value));
                        }

                        if (mFRSAlertAudit.AlertMessage.IsNotNullOrEmpty())
                            msg = mFRSAlertAudit.AlertMessage.Replace("\r", "").Replace("\n", "");

                        string timestamp = string.Empty;
                        if (mFRSAlertAudit.TimeStamp != null && mFRSAlertAudit.TimeStamp.Value != DateTime.MinValue)
                        {
                            timestamp = mFRSAlertAudit.TimeStamp.Value.ToString("dd/MM/yyyy HH:mm:ss");
                        }

                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}", $"{mFRSAlertAudit.Site}", $"{mFRSAlertAudit.AssetType}", $"{mFRSAlertAudit.Asset}", $"{mFRSAlertAudit.CauseCode}", $"{msg.RemoveComma()}", $"{timestamp}", $"{mFRSAlertAudit.Remark.RemoveComma()}", $"{mFRSAlertAudit.IsAlert}", $"{itType}", $"{mFRSAlertAudit.CreatedName}");
                        csv.AppendLine(socsvstring);
                    }
                }
                else if (SearchCriteria.Type == "SiteWise")
                {
                    csv.AppendLine("Zone Wise");
                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", "Zone", "HardWare", "HardWare Resolved", "HardWare Pending", "Calibration", "Calibration Resolved", "Calibration Pending"));
                    var mZones = _zoneService.GetAll();
                    socsvstring = string.Empty;
                    var zoneIds = mFRSAlertAudits.GroupBy(x => x.ZoneId).Select(x => x.Key).ToList();
                    if (zoneIds != null && zoneIds.Count > 0)
                    {
                        foreach (var zoneId in zoneIds)
                        {
                            var hardwareCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var hardwareResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var hardwareUnResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();


                            var calibrationCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration).Count();
                            var calibrationResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var calibrationUnResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();

                            if (hardwareCount > 0 || calibrationCount > 0)
                            {
                                var zone = mZones.Where(x => x.Id == zoneId).Select(x => x.Name).FirstOrDefault();
                                csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", $"{zone}", $"{hardwareCount}", $"{hardwareResolvedCount}", $"{hardwareUnResolvedCount}", $"{calibrationCount}", $"{calibrationResolvedCount}", $"{calibrationUnResolvedCount}"));
                            }
                        }
                    }

                    csv.AppendLine(string.Empty);
                    csv.AppendLine("Division Wise");
                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", "Division", "HardWare", "HardWare Resolved", "HardWare Pending", "Calibration", "Calibration Resolved", "Calibration Pending"));
                    var divisionIds = mFRSAlertAudits.GroupBy(x => x.DivisionId).Select(x => x.Key).ToList();
                    if (divisionIds != null && divisionIds.Count > 0)
                    {
                        var mDivisions = _divisionService.GetAll();
                        foreach (var divisionId in divisionIds)
                        {
                            var hardwareCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var hardwareResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var hardwareUnResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();


                            var calibrationCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var calibrationResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            var calibrationUnResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                            if (hardwareCount > 0 || calibrationCount > 0)
                            {
                                string divisionName = string.Empty;
                                if (mDivisions != null && mDivisions.Count > 0)
                                {
                                    divisionName = mDivisions.Where(x => x.Id == divisionId).Select(x => x.Name).FirstOrDefault();

                                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", $"{divisionName}", $"{hardwareCount}", $"{hardwareResolvedCount}", $"{hardwareUnResolvedCount}", $"{calibrationCount}", $"{calibrationResolvedCount}", $"{calibrationUnResolvedCount}"));
                                }
                            }
                        }
                    }


                    csv.AppendLine(string.Empty);
                    csv.AppendLine("Site Wise");
                    csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", "Site", "HardWare", "HardWare Resolved", "HardWare Pending", "Calibration", "Calibration Resolved", "Calibration Pending"));


                    if (divisionIds != null && divisionIds.Count > 0)
                    {
                        foreach (var divisionId in divisionIds)
                        {
                            var siteIds = mFRSAlertAudits.Where(x => x.DivisionId == divisionId).GroupBy(x => x.SiteId).Select(x => x.Key).ToList();
                            foreach (var siteId in siteIds)
                            {
                                var hardwareCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                                var hardwareResolvedCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                                var hardwareUnResolvedCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();


                                var calibrationCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                                var calibrationResolvedCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                                var calibrationUnResolvedCount = mFRSAlertAudits.Where(x => x.SiteId == siteId && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();
                                if (hardwareCount > 0 || calibrationCount > 0)
                                {
                                    string siteName = string.Empty;
                                    if (mFRSAlertAudits != null && mFRSAlertAudits.Count > 0)
                                    {
                                        siteName = mFRSAlertAudits.Where(x => x.SiteId == siteId).Select(x => x.Site).FirstOrDefault();

                                        csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6}", $"{siteName}", $"{hardwareCount}", $"{hardwareResolvedCount}", $"{hardwareUnResolvedCount}", $"{calibrationCount}", $"{calibrationResolvedCount}", $"{calibrationUnResolvedCount}"));
                                    }
                                }
                            }
                        }
                    }


                }
                else if (SearchCriteria.Type == "DateWise")
                {
                    csv.AppendLine("Zone Wise");
                    csv.AppendLine("\"" + "Hardware Count,Hardware Resolved,Hardware Un Resolved" + "\"");
                    var mZones = _zoneService.GetAll();
                    socsvstring = string.Empty;
                    var zoneIds = mFRSAlertAudits.GroupBy(x => x.ZoneId).Select(x => x.Key).ToList();
                    if (zoneIds != null && zoneIds.Count > 0)
                    {
                        socsvstring += "Zone";
                        for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate); dateTime += TimeSpan.FromDays(1))
                        {
                            socsvstring += $",{dateTime.ToShortDateString()}";
                        }
                        csv.AppendLine(socsvstring);
                        socsvstring = string.Empty;

                        foreach (var zoneId in zoneIds)
                        {
                            var zone = mZones.Where(x => x.Id == zoneId).Select(x => x.Name).FirstOrDefault();
                            socsvstring += string.Format($"{zone}");

                            for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate); dateTime += TimeSpan.FromDays(1))
                            {
                                var hardwareCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();

                                var hardwareResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();

                                var hardwareUnResolvedCount = mFRSAlertAudits.Where(x => x.ZoneId == zoneId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();


                                socsvstring += $",";
                                socsvstring += "\"" + $"{hardwareCount},{hardwareResolvedCount},{hardwareUnResolvedCount}" + "\"";


                            }
                            csv.AppendLine(socsvstring);
                            socsvstring = string.Empty;
                        }
                    }


                    csv.AppendLine(string.Empty);
                    csv.AppendLine("Division Wise");
                    csv.AppendLine("\"" + "Hardware Count,Hardware Resolved,Hardware Un Resolved" + "\"");

                    socsvstring += "Division";
                    for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate); dateTime += TimeSpan.FromDays(1))
                    {
                        socsvstring += $",{dateTime.ToShortDateString()}";
                    }
                    csv.AppendLine(socsvstring);
                    socsvstring = string.Empty;
                    var divisionIds = mFRSAlertAudits.GroupBy(x => x.DivisionId).Select(x => x.Key).ToList();
                    if (divisionIds != null && divisionIds.Count > 0)
                    {
                        var mDivisions = _divisionService.GetAll();
                        foreach (var divisionId in divisionIds)
                        {
                            var division = mDivisions.Where(x => x.Id == divisionId).Select(x => x.Name).FirstOrDefault();
                            socsvstring += string.Format($"{division}");
                            for (DateTime dateTime = Convert.ToDateTime(SearchCriteria.StartDate); dateTime <= Convert.ToDateTime(SearchCriteria.EndDate); dateTime += TimeSpan.FromDays(1))
                            {
                                var hardwareCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();

                                var hardwareResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();

                                var hardwareUnResolvedCount = mFRSAlertAudits.Where(x => x.DivisionId == divisionId && x.TimeStamp != null && x.TimeStamp.Value.Date == dateTime.Date && x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware && !x.IsResolved).GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key).Count();



                                socsvstring += $",";
                                socsvstring += "\"" + $"{hardwareCount},{hardwareResolvedCount},{hardwareUnResolvedCount}" + "\"";
                            }

                            csv.AppendLine(socsvstring);
                            socsvstring = string.Empty;


                        }
                    }
                }



                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=Summary" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();

            }

            return RedirectToAction("AlertSummary");
        }

        public Dictionary<DateTime, List<Domain.FRSAlert>> GetFRSAlertAuditIds(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new Dictionary<DateTime, List<Domain.FRSAlert>>();
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                {
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetFRSAlertAuditIds"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlerts = JsonConvert.DeserializeObject<Dictionary<DateTime, List<Domain.FRSAlert>>>(jsonString);


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
            return mFRSAlerts;
        }

        public Dictionary<DateTime, int> GetFRSAlertListCount(Domain.FRSAlertLister mFRSAlertLister)
        {
            var mFRSAlerts = new Dictionary<DateTime, int>();
            try
            {
                mFRSAlertLister.SearchCriteria.IsAcknowledgement = false;
                mFRSAlertLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
                mFRSAlertLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                if (ClsHttpContent.LoginUser.IsSiteKeeping)
                {
                    mFRSAlertLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.Admin;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSAlertLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetFRSAlertAuditCount"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlerts = JsonConvert.DeserializeObject<Dictionary<DateTime, int>>(jsonString);
                        //if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null)
                        //{
                        //    mFRSAlerts = mFRSAlertLister.mFRSAlerts;
                        //}

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
            return mFRSAlerts;
        }

        public JsonResult DeleteFRSAlertAudit(int id)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("FRSAlertAudit/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var result = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (result)
                        {
                            data = new { type = "success", result = "Record has been Deleted." };
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

        #endregion

        #region FRS Audit Watchlist 

        public ActionResult FRSAuditWatchlist()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _FRSAuditWatchlist(Domain.SearchCriteria searchCriteria)
        {

            return PartialView(GetFRSAlertAuditWatchlist(searchCriteria));
        }

        public ActionResult DownloadFRSAuditWatchlist(Domain.SearchCriteria SearchCriteria)
        {
            var mFRSAlertAudits = GetFRSAlertAuditWatchlist(SearchCriteria);
            if (mFRSAlertAudits != null && mFRSAlertAudits.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                csv.AppendLine(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", "Station", "Asset Type", "Asset Cause code", "Alert Message", "Alert Timestamp", "Audit By", "Audit Date", "Resolve By", "Resolve Date"));

                foreach (var groupdata in mFRSAlertAudits.GroupBy(x => new { x.AssetId, x.AlertInfoId }).Select(x => x.Key))
                {
                    foreach (var mFRSAlertAudit in mFRSAlertAudits.Where(x => x.AssetId == groupdata.AssetId && x.AlertInfoId == groupdata.AlertInfoId).OrderByDescending(x => x.CreatedDate))
                    {
                        string msg = string.Empty;
                        if (mFRSAlertAudit.AlertMessage.IsNotNullOrEmpty())
                            msg = mFRSAlertAudit.AlertMessage.Replace("\r", "").Replace("\n", "");

                        csv.AppendLine(string.Format($"{mFRSAlertAudit.Site.RemoveComma()},{mFRSAlertAudit.AssetType.RemoveComma()},{mFRSAlertAudit.CauseCode.RemoveComma()},{msg.RemoveComma()},{mFRSAlertAudit.TimeStamp},{mFRSAlertAudit.CreatedName.RemoveComma()},{mFRSAlertAudit.CreatedDate},{mFRSAlertAudit.ResolvedName.RemoveComma()},{mFRSAlertAudit.ResolvedDate}"));
                    }
                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=FRSAuditResolved" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }

            return RedirectToAction("FRSAuditWatchlist");
        }

        private List<Domain.FRSAlertAudit> GetFRSAlertAuditWatchlist(Domain.SearchCriteria searchCriteria)
        {
            List<Domain.FRSAlertAudit> mAlertAudits = new List<Domain.FRSAlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlertAudit/GetResolvedList"), str).Result;
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

        #endregion

        #region Show Test WatchList

        public ActionResult TestWatchList()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");

            return View();
        }

        public ActionResult _TestWatchList(Domain.SearchCriteria searchCriteria)
        {
            FRSAlertLister fRSAlertLister = new FRSAlertLister();
            List<Domain.FRSAlert> mFRSAlerts = new List<Domain.FRSAlert>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetTestWatchList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlerts = JsonConvert.DeserializeObject<List<Domain.FRSAlert>>(jsonString);

                        fRSAlertLister.mFRSAlerts = mFRSAlerts;
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
            finally
            {
                ViewBag.FRSAlertAudits = GetGroupWise();
            }

            return PartialView("_FRSAlertList", fRSAlertLister);
        }

        #endregion

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
                // mDivisions = GetDivisions();
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
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }
    }
}