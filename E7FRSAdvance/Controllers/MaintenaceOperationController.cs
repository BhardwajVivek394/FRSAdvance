using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Microsoft.Ajax.Utilities;
using Newtonsoft.Json;
using NReco.PdfGenerator;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using System.Web.UI.HtmlControls;
using System.Web.WebPages;
using TheArtOfDev.HtmlRenderer.Core;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class MaintenaceOperationController : Controller
    {
        // GET: MaintenaceOperation
        private readonly IZoneService zoneService;
        private readonly IDivisionService divisionService;
        private readonly ISiteKeepingService siteKeepingService;
        private readonly ISiteService siteService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IAssetService assetService;
        private readonly ICardLineService cardLineService;
        public MaintenaceOperationController(IZoneService zoneService, IDivisionService divisionService, ISiteKeepingService siteKeepingService, ISiteService siteService, IAssetAttributeService assetAttributeService, IAssetService assetService, ICardLineService cardLineService)
        {
            this.zoneService = zoneService;
            this.divisionService = divisionService;
            this.siteKeepingService = siteKeepingService;
            this.siteService = siteService;
            this.assetAttributeService = assetAttributeService;
            this.assetService = assetService;
            this.cardLineService = cardLineService;
        }

        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _List(MaintenaceOperationLister mMaintenaceOperationLister)
        {
            //mSiteLister.Pager.Take = -1;
            mMaintenaceOperationLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mMaintenaceOperationLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenaceOperationLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenaceOperation/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mMaintenaceOperationLister = JsonConvert.DeserializeObject<MaintenaceOperationLister>(jsonString);
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
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");

                ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;
            }
            return PartialView(mMaintenaceOperationLister);
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
            return PartialView("_AlertList", mSMSLogLister);
        }

        [HttpPost]
        public ActionResult _WatchList(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            mWatchListLister.SearchCriteria.CreationType = (int)WatchListCreationType.System;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetListerGroupOfAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status == null || (x.Status != null && x.Status.ToLower() != "ok" && x.Status.ToLower() != "resolved")).ToList();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            return PartialView(mWatchListLister);
        }

        [HttpPost]
        public ActionResult _UserWatchList(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            mWatchListLister.SearchCriteria.CreationType = (int)WatchListCreationType.User;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetListerGroupOfAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status == null || (x.Status != null && x.Status.ToLower() != "ok" && x.Status.ToLower() != "resolved")).ToList();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            return PartialView("_WatchList", mWatchListLister);
        }

        public ActionResult GetWatchListInstance(int assetId, int attributeId)
        {
            var mFRSAlertInstances = new List<Domain.WatchList>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"WatchList/AssetId/{assetId}/AttributeId/{attributeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertInstances = JsonConvert.DeserializeObject<List<Domain.WatchList>>(jsonString);
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
            return PartialView("_WatchListInstance", mFRSAlertInstances);
        }

        public ActionResult _RFStatus(RFStatusLister mRFStatusLister)
        {
            mRFStatusLister.Pager.Take = -1;
            mRFStatusLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mRFStatusLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            if (mRFStatusLister != null && mRFStatusLister.SearchCriteria != null && mRFStatusLister.SearchCriteria.TimeStamp != null && mRFStatusLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mRFStatusLister.SearchCriteria.TimeStamp = DateTime.Now;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRFStatusLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetRFStatusLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mRFStatusLister = JsonConvert.DeserializeObject<RFStatusLister>(jsonString);
                        if (mRFStatusLister != null && mRFStatusLister.mRFStatus != null && mRFStatusLister.mRFStatus.Count > 0)
                        {
                            //ViewBag.Houres = mRFStatusLister.mRFStatus.Select(x => x.TimeStamp.ToString("hh tt")).ToList().Distinct().ToList();
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
            return PartialView(mRFStatusLister);
        }

        public ActionResult _DivisionReport(DivisionReportModelLister mDivisionReportModelLister)
        {
            mDivisionReportModelLister.Pager.Take = -1;
            mDivisionReportModelLister.SearchCriteria.StartDate = DateTime.Now.AddDays(-1);
            mDivisionReportModelLister.SearchCriteria.EndDate = DateTime.Now.AddDays(-1);
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
            finally
            {
                //ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                //ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return PartialView(mDivisionReportModelLister);
        }

        public ActionResult DownloadWatchList(int siteId, string startdate, string endDate, string type)
        {
            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister.SearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "M/d/yyyy");
            mWatchListLister.SearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "M/d/yyyy");

            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            mWatchListLister.SearchCriteria.CreationType = (int)WatchListCreationType.System;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetListerGroupOfAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;

                            if (type == "Gropwise")
                            {
                                int counter = 0;

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Sr No.", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type", "Cause");
                                csv.AppendLine(socsvstring);

                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Link issue", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "linkissue"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }



                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Hardware", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause == null || x.Cause == string.Empty || x.Cause.Trim().ToLower() == "point not set"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }


                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Thresould", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "out of range" || x.Cause.Trim().ToLower() == "point instance"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }


                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "InActive", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "inactive"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }

                            }
                            else if (type == "List")
                            {
                                int counter = 0;
                                var assetNameGroup = mWatchListLister.WatchLists.GroupBy(x => x.AssetName).ToList();
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Sr No.", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type", "Cause");
                                csv.AppendLine(socsvstring);
                                foreach (var assetName in assetNameGroup)
                                {
                                    foreach (var item in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper()).GroupBy(x => x.AttributeName).Select(y => y.Key).ToList())
                                    {
                                        foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper() && x.AttributeName.Trim().ToUpper() == item.Trim().ToUpper()).OrderBy(x => x.Pin).ToList())
                                        {
                                            string srno = string.Empty;
                                            string adc = string.Empty;
                                            string lastModifiedDate = string.Empty;
                                            string createdByName = string.Empty;
                                            if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                            {
                                                lastModifiedDate = watchList.LastModifiedDate.ToString();
                                            }
                                            if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                            {

                                            }
                                            else
                                            {
                                                counter++;
                                                srno = counter.ToString();
                                            }

                                            if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                                createdByName = "User";
                                            else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                                createdByName = "System";

                                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                            csv.AppendLine(socsvstring);
                                        }

                                    }
                                }
                            }


                            var mSite = siteService.Get(siteId);

                            if (mSite != null && mSite.Id > 0)
                            {
                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename=WatchList_{mSite.Name}_{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}" + ".csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(csv);
                                Response.Flush();
                                Response.End();
                            }
                        }
                    }

                }
            }
            catch (Exception)
            {
            }
            return RedirectToAction("SiteAssetData", new { siteId = siteId });
        }

        public ActionResult DownloadUserWatchList(int siteId, string startdate, string endDate, string type)
        {
            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister.SearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startdate, "M/d/yyyy");
            mWatchListLister.SearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "M/d/yyyy");

            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            mWatchListLister.SearchCriteria.CreationType = (int)WatchListCreationType.User;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetListerGroupOfAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            var csv = new StringBuilder();
                            var socsvstring = string.Empty;

                            if (type == "Gropwise")
                            {
                                int counter = 0;

                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Sr No.", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type", "Cause");
                                csv.AppendLine(socsvstring);

                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Link issue", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "linkissue"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }



                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Hardware", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause == null || x.Cause == string.Empty || x.Cause.Trim().ToLower() == "point not set"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }


                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Thresould", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "out of range" || x.Cause.Trim().ToLower() == "point instance"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }


                                socsvstring = string.Empty;
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "InActive", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "", "");
                                csv.AppendLine(socsvstring);
                                foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.Cause != null && x.Cause.Trim().ToLower() == "inactive"))
                                {
                                    string srno = string.Empty;
                                    string adc = string.Empty;
                                    string lastModifiedDate = string.Empty;
                                    string createdByName = string.Empty;
                                    if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                    {
                                        lastModifiedDate = watchList.LastModifiedDate.ToString();
                                    }
                                    if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                    {

                                    }
                                    else
                                    {
                                        counter++;
                                        srno = counter.ToString();
                                    }

                                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                        createdByName = "User";
                                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                        createdByName = "System";

                                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                    csv.AppendLine(socsvstring);
                                }

                            }
                            else if (type == "List")
                            {
                                int counter = 0;
                                var assetNameGroup = mWatchListLister.WatchLists.GroupBy(x => x.AssetName).ToList();
                                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", "Sr No.", "AssetType", "Asset", "Attribute", "Value", "Cluster", "Card", "ADC", "Pin", "Created Date", "Updated Date", "Status", "Issue", "Remark", "Point Machine Instance", "Creation Type", "Cause");
                                csv.AppendLine(socsvstring);
                                foreach (var assetName in assetNameGroup)
                                {
                                    foreach (var item in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper()).GroupBy(x => x.AttributeName).Select(y => y.Key).ToList())
                                    {
                                        foreach (var watchList in mWatchListLister.WatchLists.Where(x => x.AssetName.Trim().ToUpper() == assetName.Key.Trim().ToUpper() && x.AttributeName.Trim().ToUpper() == item.Trim().ToUpper()).OrderBy(x => x.Pin).ToList())
                                        {
                                            string srno = string.Empty;
                                            string adc = string.Empty;
                                            string lastModifiedDate = string.Empty;
                                            string createdByName = string.Empty;
                                            if (watchList.LastModifiedDate != null && watchList.LastModifiedDate != DateTime.MinValue)
                                            {
                                                lastModifiedDate = watchList.LastModifiedDate.ToString();
                                            }
                                            if (watchList.Status != null && watchList.Status.Trim().ToLower() == "resolved")
                                            {

                                            }
                                            else
                                            {
                                                counter++;
                                                srno = counter.ToString();
                                            }

                                            if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                                                createdByName = "User";
                                            else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                                                createdByName = "System";

                                            socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}", srno, watchList.AssetTypeName, watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue, watchList.ClusterName.RemoveComma(), watchList.CardName.RemoveComma(), watchList.ADCName.RemoveComma(), watchList.Pin, watchList.CreatedDate.ToString(), lastModifiedDate, watchList.Status, watchList.Issue, watchList.Remark, watchList.PointMachineInstance, createdByName, watchList.Cause);
                                            csv.AppendLine(socsvstring);
                                        }

                                    }
                                }
                            }


                            var mSite = siteService.Get(siteId);

                            if (mSite != null && mSite.Id > 0)
                            {
                                Response.Clear();
                                Response.Buffer = true;
                                Response.AddHeader("content-disposition", $"attachment;filename=WatchList_{mSite.Name}_{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}" + ".csv");
                                Response.Charset = "utf-8";
                                Response.ContentType = "text/csv";
                                Response.Output.Write(csv);
                                Response.Flush();
                                Response.End();
                            }
                        }
                    }

                }
            }
            catch (Exception)
            {
            }
            return RedirectToAction("SiteAssetData", new { siteId = siteId });
        }

        public ActionResult GetSiteReboot(int siteId)
        {
            string date = string.Empty;
            var mSiteReboots = new List<Domain.SiteReboot>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSiteReboot/SiteId/{siteId}/Date/{DateTime.Now.AddDays(-1).ToShortDateString().Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteReboots = JsonConvert.DeserializeObject<List<Domain.SiteReboot>>(jsonString);
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
            return PartialView("_SiteReboot", mSiteReboots);
        }

        public ActionResult GetFRSWatchList(int siteId)
        {
            var mFRSWatchLists = new List<Domain.FRSWatchList>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSWatchList/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSWatchLists = JsonConvert.DeserializeObject<List<Domain.FRSWatchList>>(jsonString);

                        TempData.Remove("TempFRSWatchList");
                        TempData["TempFRSWatchList"] = mFRSWatchLists;
                        TempData.Keep();
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
                Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
                searchCriteria.SiteId = siteId;
                searchCriteria.IsCheckAlertType = true;
                searchCriteria.IsAlert = false;
                ViewBag.FRSAlertAudits = GetFRSAlertAudit(searchCriteria);
            }
            return PartialView("_FRSWatchList", mFRSWatchLists);
        }

        public ActionResult GetFRSWatchTrueList(int siteId)
        {
            List<Domain.FRSAlertAudit> mAlertAudits = new List<Domain.FRSAlertAudit>();
            Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
            searchCriteria.SiteId = siteId;
            searchCriteria.IsCheckAlertType = true;
            searchCriteria.IsCheckRailwayAlertNull = true;
            searchCriteria.IsAlert = true;
            return PartialView("_GetFRSWatchTrueList", GetFRSAlertAudit(searchCriteria));
        }

        public ActionResult GetWatchListFRSAlertInstance(int assetId, int alertInfoId)
        {
            var mFRSAlertInstances = new List<Domain.FRSAlertInstance>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSAlertInstance/GetWatchListFRSAlertInstance/AssetId/{assetId}/AlertInfoId/{alertInfoId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlertInstances = JsonConvert.DeserializeObject<List<Domain.FRSAlertInstance>>(jsonString);
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
            return PartialView("_WatchListFRSAlertInstance", mFRSAlertInstances);
        }

        public ActionResult DownloadFRSWatchList()
        {
            var tempSMSLog = TempData.Peek("TempFRSWatchList");
            if (tempSMSLog != null)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                var mFRSWatchLists = tempSMSLog as List<Domain.FRSWatchList>;
                if (mFRSWatchLists != null && mFRSWatchLists.Count > 0)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", "Asset Type", "Asset", "Cause Code", "Description", "Is Set", "Remark");

                    csv.AppendLine(socsvstring);
                    string type = string.Empty;

                    foreach (var val in mFRSWatchLists)
                    {
                        if (val.IsSet)
                            type = "True";
                        else
                            type = "False";

                        string value = string.Empty;
                        socsvstring = string.Format("{0},{1},{2},{3},{4},{5}", val.AssetType.RemoveComma(), val.Asset.RemoveComma(), val.CauseCode.RemoveComma(), val.Description, type, val.Remark);
                        csv.AppendLine(socsvstring);

                    }

                    Response.Clear();
                    Response.Buffer = true;
                    Response.AddHeader("content-disposition", "attachment;filename=FRSWatchList" + DateTime.Now.Ticks + ".csv");
                    Response.Charset = "utf-8";
                    Response.ContentType = "text/csv";
                    Response.Output.Write(csv);
                    Response.Flush();
                    Response.End();
                }
                else
                    return RedirectToAction("Index");
            }
            else
                return RedirectToAction("Index");


            return View();
        }

        public ActionResult UpdateFRSWatch(List<Domain.FRSWatchList> mFRSWatchLists)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mFRSWatchLists != null && mFRSWatchLists.Count > 0)
                {
                    foreach (var mFRSWatchList in mFRSWatchLists)
                    {
                        mFRSWatchList.CreatedBy = ClsHttpContent.LoginUser.Id;
                    }
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mFRSWatchLists);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("FRSWatchList"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "FRS Watch List has been updated." };
                    }
                    else
                    {
                        data = new { type = "error", result = "Internal server error." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        public ActionResult UpdateFRSAlertAudit(List<Domain.FRSAlertAudit> mFRSAlertAudits)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mFRSAlertAudits != null && mFRSAlertAudits.Count > 0)
                {
                    foreach (var mFRSWatchList in mFRSAlertAudits)
                    {
                        mFRSWatchList.CreatedBy = ClsHttpContent.LoginUser.Id;

                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(mFRSWatchList);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PutAsync(String.Format($"FRSAlertAudit/Id/{mFRSWatchList.Id}"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                                data = new { type = "success", result = "FRS Watch List has been updated." };
                            }
                        }
                    }
                }

            }
            catch (Exception)
            {
                data = new { type = "error", result = "Internal server error." };
            }

            return Json(data);
        }

        [HttpPost]
        public ActionResult ExcludeSite(int siteId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintenaceOperation/ExcludeSite/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Site has been exclude." };
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


        [HttpPost]
        public ActionResult IncludeSite(int siteId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintenaceOperation/IncludeSite/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Site has been include." };
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

        public ActionResult GetExcludeSite()
        {
            var mSiteMaintenaceOperations = new List<Domain.SiteMaintenaceOperation>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintenaceOperation/GetExclude")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteMaintenaceOperations = JsonConvert.DeserializeObject<List<Domain.SiteMaintenaceOperation>>(jsonString);
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
            return PartialView("_ExcludeSite", mSiteMaintenaceOperations);
        }

        public MQTTDetailList GetMQTTDetailList()
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

        [HttpPost]
        public ActionResult GetA10Status(int siteId)
        {
            var mA10Status = new List<Domain.A10Status>();
            var date = DateTime.Now.ToString("d-M-yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
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

        public ActionResult GetA10Graph(int siteId, string date, string a10Id = null)
        {
            var mA10Status = new List<Domain.A10Status>();
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            if (a10Id.IsNotNullOrEmpty())
                                mA10Status = mA10Status.Where(x => x.id == a10Id).ToList();

                            mA10Status = mA10Status.Where(x => x.id != "0").ToList();

                            data = mA10Status.GroupBy(x => x.id)
      .ToDictionary(x => x.Key, x => x.Select(e => new { e.last_response, e.timestamp }).ToList());
                        }

                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(data, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        [HttpPost]
        public ActionResult GetA10Product(int siteId)
        {
            var mProducts = new List<Domain.Product>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Product/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProducts = JsonConvert.DeserializeObject<List<Domain.Product>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(mProducts, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        [HttpPost]
        public ActionResult GetProductStatus(string deviceId)
        {
            var mProductStatus = new List<Domain.ProductStatus>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Product/GetStatus/DeviceId/{deviceId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProductStatus = JsonConvert.DeserializeObject<List<Domain.ProductStatus>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(mProductStatus, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return jsonResult;
        }

        public List<Domain.FRSAlertAudit> GetFRSAlertAudit(Domain.SearchCriteria searchCriteria)
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

        #region CombineWatchlist

        public ActionResult _CombineWatchlist(int siteId, string type = null)
        {
            var mSite = siteService.Get(siteId);
            var overallTargets = GetWithOutWatchList(new Domain.SearchCriteria() { SiteId = siteId });
            var mA10Status = GetA10StatusList(siteId);
            if (mA10Status != null && mA10Status.Count > 0)
            {
                foreach (var mA10Sta in mA10Status)
                {
                    var mCombineOverallTargets = overallTargets.Where(x => x.A10Id != null && x.A10Id == Convert.ToInt32(mA10Sta.id) && !x.IsResolved).FirstOrDefault();
                    if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                    {
                        mA10Sta.Reason = mCombineOverallTargets.Reason;
                        mA10Sta.MatrialCode = mCombineOverallTargets.MatrialCode;
                        mA10Sta.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                        mA10Sta.IsResolved = mCombineOverallTargets.IsResolved;
                    }
                }

                var mProducts = GetA10Products(siteId);
                if (mProducts != null && mProducts.Count > 0)
                {
                    foreach (var mA10Sta in mA10Status)
                    {


                        var mProduct = mProducts.Where(x => mA10Sta.name.Contains(x.ClusterName)).FirstOrDefault();
                        if (mProduct != null)
                            mA10Sta.mProduct = mProduct;
                    }
                }
                ViewBag.RFStatus = mA10Status;
            }

            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister = GetWatchList(mWatchListLister);
            if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                foreach (var watchList in mWatchListLister.WatchLists)
                {
                    if (overallTargets != null && overallTargets.Count > 0)
                    {
                        var mOverallTarget = overallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.WatchLists).ToList();
                        if (mOverallTarget != null && mOverallTarget.Count > 0)
                        {
                            var mCombineOverallTargets = mOverallTarget.Where(x => x.WatchListId != null && x.WatchListId == watchList.Id).FirstOrDefault();
                            if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                            {
                                watchList.Reason = mCombineOverallTargets.Reason;
                                watchList.MatrialCode = mCombineOverallTargets.MatrialCode;
                                watchList.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                                watchList.IsResolved = mCombineOverallTargets.IsResolved;
                                watchList.IssueType = mCombineOverallTargets.IssueType;
                            }
                        }
                    }

                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                        watchList.GeneratedFrom = "Mannual";
                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                        watchList.GeneratedFrom = "WatchList";
                }

            }

            var mFRSWatchLists = GetFRSWatchListBySite(siteId);
            if (mFRSWatchLists != null && mFRSWatchLists.Count > 0)
            {

                foreach (var frswatchlist in mFRSWatchLists)
                {
                    if (overallTargets != null && overallTargets.Count > 0)
                    {
                        var mOverallTarget = overallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.FRSWatchList).ToList();
                        if (mOverallTarget != null && mOverallTarget.Count > 0)
                        {
                            var mCombineOverallTargets = mOverallTarget.Where(x => x.FRSWatchListId != null && x.FRSWatchListId == frswatchlist.Id && x.AttributeId == frswatchlist.AttributeId).FirstOrDefault();
                            if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                            {
                                frswatchlist.Reason = mCombineOverallTargets.Reason;
                                frswatchlist.MatrialCode = mCombineOverallTargets.MatrialCode;
                                frswatchlist.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                                frswatchlist.IsResolved = mCombineOverallTargets.IsResolved;
                                frswatchlist.IssueType = mCombineOverallTargets.IssueType;
                            }
                        }
                    }

                    mWatchListLister.WatchLists.Add(new WatchList() { Id = frswatchlist.Id, AssetId = frswatchlist.AssetId, Cause = frswatchlist.CauseCode, AssetName = frswatchlist.Asset, AssetTypeName = frswatchlist.AssetType, CardName = frswatchlist.CardName, ClusterName = frswatchlist.ClusterName, ADCName = frswatchlist.ADCName, Pin = frswatchlist.Pin, GeneratedFrom = "FRSWatchList", CreatedDate = frswatchlist.CreatedDate, AttributeName = frswatchlist.Attribute, AttributeId = frswatchlist.AttributeId, AssetTypeId = frswatchlist.AssetTypeId, Reason = frswatchlist.Reason, MatrialCode = frswatchlist.MatrialCode, MaintainerUserId = frswatchlist.MaintainerUserId, IsResolved = frswatchlist.IsResolved, IssueType = frswatchlist.IssueType, SiteName = frswatchlist.SiteName });
                }
            }


            if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                ViewBag.WatchLists = mWatchListLister.WatchLists;
            }

            var mCardLines = cardLineService.Get(siteId);
            if (mCardLines != null && mCardLines.Count > 0)
            {
                ViewBag.CardLines = mCardLines;
            }

            var issueType = EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.WatchIssue());
            var dynamicIssueTypes = GetIssueTypes();
            if (dynamicIssueTypes != null && dynamicIssueTypes.Count > 0)
            {
                issueType.AddRange(dynamicIssueTypes);
            }

            ViewBag.IssueTypes = issueType;

            ViewBag.MaintainerUsers = GetMaintainerUser();

            ViewBag.SiteId = siteId;

            if (type.IsNotNullOrEmpty())
                ViewBag.Type = type;

            return PartialView(mSite);
        }

        public ActionResult DownloadCombineWatchlist(int siteId)
        {
            var mSite = siteService.Get(siteId);
            var overallTargets = GetWithOutWatchList(new Domain.SearchCriteria() { SiteId = siteId });
            var mA10Status = GetA10StatusList(siteId);
            if (mA10Status != null && mA10Status.Count > 0)
            {
                foreach (var mA10Sta in mA10Status)
                {
                    var mCombineOverallTargets = overallTargets.Where(x => x.A10Id != null && x.A10Id == Convert.ToInt32(mA10Sta.id) && !x.IsResolved).FirstOrDefault();
                    if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                    {
                        mA10Sta.Reason = mCombineOverallTargets.Reason;
                        mA10Sta.MatrialCode = mCombineOverallTargets.MatrialCode;
                        mA10Sta.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                        mA10Sta.IsResolved = mCombineOverallTargets.IsResolved;
                    }
                }

                var mProducts = GetA10Products(siteId);
                if (mProducts != null && mProducts.Count > 0)
                {
                    foreach (var mA10Sta in mA10Status)
                    {


                        var mProduct = mProducts.Where(x => mA10Sta.name.Contains(x.ClusterName)).FirstOrDefault();
                        if (mProduct != null)
                            mA10Sta.mProduct = mProduct;
                    }
                }
            }

            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister = GetWatchList(mWatchListLister);
            if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                foreach (var watchList in mWatchListLister.WatchLists)
                {
                    if (overallTargets != null && overallTargets.Count > 0)
                    {
                        var mOverallTarget = overallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.WatchLists).ToList();
                        if (mOverallTarget != null && mOverallTarget.Count > 0)
                        {
                            var mCombineOverallTargets = mOverallTarget.Where(x => x.WatchListId != null && x.WatchListId == watchList.Id).FirstOrDefault();
                            if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                            {
                                watchList.Reason = mCombineOverallTargets.Reason;
                                watchList.MatrialCode = mCombineOverallTargets.MatrialCode;
                                watchList.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                                watchList.IsResolved = mCombineOverallTargets.IsResolved;
                                watchList.IssueType = mCombineOverallTargets.IssueType;
                            }
                        }
                    }

                    if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.User)
                        watchList.GeneratedFrom = "Mannual";
                    else if (watchList.CreationType == (int)E7FRSAdvance.Utility.Utility.WatchListCreationType.System)
                        watchList.GeneratedFrom = "WatchList";
                }

            }

            var mFRSWatchLists = GetFRSWatchListBySite(siteId);
            if (mFRSWatchLists != null && mFRSWatchLists.Count > 0)
            {

                foreach (var frswatchlist in mFRSWatchLists)
                {
                    if (overallTargets != null && overallTargets.Count > 0)
                    {
                        var mOverallTarget = overallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.FRSWatchList).ToList();
                        if (mOverallTarget != null && mOverallTarget.Count > 0)
                        {
                            var mCombineOverallTargets = mOverallTarget.Where(x => x.FRSWatchListId != null && x.FRSWatchListId == frswatchlist.Id && x.AttributeId == frswatchlist.AttributeId).FirstOrDefault();
                            if (mCombineOverallTargets != null && mCombineOverallTargets.Id > 0)
                            {
                                frswatchlist.Reason = mCombineOverallTargets.Reason;
                                frswatchlist.MatrialCode = mCombineOverallTargets.MatrialCode;
                                frswatchlist.MaintainerUserId = mCombineOverallTargets.MaintainerUserId;
                                frswatchlist.IsResolved = mCombineOverallTargets.IsResolved;
                                frswatchlist.IssueType = mCombineOverallTargets.IssueType;
                            }
                        }
                    }

                    mWatchListLister.WatchLists.Add(new WatchList() { Id = frswatchlist.Id, AssetId = frswatchlist.AssetId, Cause = frswatchlist.CauseCode, AssetName = frswatchlist.Asset, AssetTypeName = frswatchlist.AssetType, CardName = frswatchlist.CardName, ClusterName = frswatchlist.ClusterName, ADCName = frswatchlist.ADCName, Pin = frswatchlist.Pin, GeneratedFrom = "FRSWatchList", CreatedDate = frswatchlist.CreatedDate, AttributeName = frswatchlist.Attribute, AttributeId = frswatchlist.AttributeId, AssetTypeId = frswatchlist.AssetTypeId, Reason = frswatchlist.Reason, MatrialCode = frswatchlist.MatrialCode, MaintainerUserId = frswatchlist.MaintainerUserId, IsResolved = frswatchlist.IsResolved, IssueType = frswatchlist.IssueType, SiteName = frswatchlist.SiteName });
                }
            }


            var issueType = EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.WatchIssue());
            var dynamicIssueTypes = GetIssueTypes();
            if (dynamicIssueTypes != null && dynamicIssueTypes.Count > 0)
            {
                issueType.AddRange(dynamicIssueTypes);
            }

            //ViewBag.IssueTypes = issueType;

            var MaintainerUsers = GetMaintainerUser();

            //ViewBag.SiteId = siteId;

            string path = HttpContext.Server.MapPath("~/Template/CombineOverallTarget.html");
            FileReader fileReader = new FileReader(path);
            string pdfTemplate = fileReader.Read();
            var htmlTable = string.Empty;

            #region watch
            var sbwatchlist = new StringBuilder();
            sbwatchlist.AppendLine("<h4>WatchList</h4>");
            sbwatchlist.AppendLine("<table class='table table-responsive' id='table-combine'>");
            sbwatchlist.AppendLine("<thead>");
            sbwatchlist.AppendLine("    <tr>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>S.No</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>AssetType</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Asset</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Card</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>ADC</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Pin</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Attribute</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Value</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Cause</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg text-center'>Replaced</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Issue Type</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Reason</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Code</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg'>Ground Staff</th>");
            sbwatchlist.AppendLine("        <th width='3%' class='tr-bg text-center'>Validate</th>");
            sbwatchlist.AppendLine("    </tr>");
            sbwatchlist.AppendLine("</thead>");
            sbwatchlist.AppendLine("<tbody>");

            int listcounter = 0;

            if (mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                foreach (var adc in mWatchListLister.WatchLists.Where(x => x.ClusterName != null)
                                              .GroupBy(x => x.ADCName)
                                              .Select(x => x.Key)
                                              .ToList())
                {
                    foreach (var item in mWatchListLister.WatchLists.Where(x => x.ClusterName != null && x.ADCName == adc))
                    {
                        var idCluster = Regex.Replace(item.ClusterName, @"[^\w\d]", "");
                        listcounter++;

                        sbwatchlist.AppendLine("<tr>");

                        // Info icon
                        //sbwatchlist.AppendLine($"<td><h5><i class='fa fa-info-circle' aria-hidden='true' " +
                        //                       $"data-bs-trigger='hover' data-container='body' data-bs-toggle='popover' " +
                        //                       $"data-bs-html='true' data-bs-placement='top' " +
                        //                       $"data-bs-original-title='Site Data' " +
                        //                       $"data-bs-content='<p>Date: {item.CreatedDate}</p><p>Generated From: {item.GeneratedFrom}</p>'></i></h5></td>");

                        sbwatchlist.AppendLine($"<td>{listcounter}</td>");
                        sbwatchlist.AppendLine($"<td>{item.AssetTypeName}</td>");
                        sbwatchlist.AppendLine($"<td>{item.AssetName}</td>");
                        sbwatchlist.AppendLine($"<td>{item.CardName}</td>");
                        sbwatchlist.AppendLine($"<td>{item.ADCName}</td>");
                        sbwatchlist.AppendLine($"<td>{item.Pin}</td>");
                        sbwatchlist.AppendLine($"<td>{item.AttributeName}</td>");
                        sbwatchlist.AppendLine($"<td>{item.AttributeValue}</td>");
                        sbwatchlist.AppendLine($"<td>{item.Cause}</td>");

                        // Replaced dropdown
                        sbwatchlist.AppendLine($"<td class='text-center'>");
                        if (!string.IsNullOrEmpty(item.MatrialCode))
                            sbwatchlist.AppendLine("Yes");
                        else if (string.IsNullOrEmpty(item.MatrialCode) && item.IsResolved != null)
                            sbwatchlist.AppendLine("No");

                        sbwatchlist.AppendLine($"</td>");


                        // Issue Type dropdown
                        sbwatchlist.AppendLine($"<td>{item.IssueType}</td>");

                        // Reason
                        var reason = item.Reason ?? "";
                        sbwatchlist.AppendLine($"<td>{reason}</td>");

                        // Code
                        var code = item.MatrialCode ?? "";
                        sbwatchlist.AppendLine($"<td>{code}</td>");

                        // Ground Staff dropdown
                        if (MaintainerUsers != null && item.MaintainerUserId != null)
                        {
                            foreach (var maintainerUsers in MaintainerUsers)
                            {
                                if (item.MaintainerUserId == maintainerUsers.Id)
                                {
                                    sbwatchlist.AppendLine($"<td>{maintainerUsers.FirstName} {maintainerUsers.LastName}</td>");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            sbwatchlist.AppendLine($"<td></td>");
                        }
                        // Validate
                        if (item.IsResolved != null)
                        {
                            if (item.IsResolved.Value)
                                sbwatchlist.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/validateIcon2.png' /></td>");
                            else
                                sbwatchlist.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/Warning.png' /></td>");
                        }
                        else
                        {
                            sbwatchlist.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/validateIcon1.png' /></td>");
                        }


                        sbwatchlist.AppendLine("</tr>");
                    }
                }
            }
            else
            {
                sbwatchlist.AppendLine("<tr><td colspan='15'></td></tr>");
            }

            sbwatchlist.AppendLine("</tbody>");
            sbwatchlist.AppendLine("</table>");


            htmlTable += sbwatchlist.ToString();
            #endregion


            #region link
            if (mA10Status != null && mA10Status.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("<div style='page-break-after: always;'></div>");
                sb.AppendLine("<h4>LinkIssue</h4>");
                sb.AppendLine("<table class='table table-lg' id='table-combine'>");
                sb.AppendLine("<thead>");
                sb.AppendLine("    <tr>");
                sb.AppendLine("        <th class='tr-bg'>S. No</th>");
                sb.AppendLine("        <th class='tr-bg'>ID</th>");
                sb.AppendLine("        <th class='tr-bg'>Cluster</th>");
                sb.AppendLine("        <th class='tr-bg'>Status</th>");
                sb.AppendLine("        <th class='tr-bg'>TCP</th>");
                sb.AppendLine("        <th class='tr-bg'>A10 Type</th>");
                sb.AppendLine("        <th class='tr-bg'>Modem</th>");
                sb.AppendLine("        <th class='tr-bg'>Modem Number</th>");
                sb.AppendLine("        <th class='tr-bg'>Connector status</th>");
                sb.AppendLine("        <th class='tr-bg'>Replaced</th>");
                sb.AppendLine("        <th class='tr-bg'>Reason</th>");
                sb.AppendLine("        <th class='tr-bg'>Code</th>");
                sb.AppendLine("        <th class='tr-bg'>Ground Staff</th>");
                sb.AppendLine("        <th class='tr-bg'>Validate</th>");
                sb.AppendLine("    </tr>");
                sb.AppendLine("</thead>");
                sb.AppendLine("<tbody>");

                int listLinkcounter = 0;
                if (mA10Status != null && mA10Status.Count > 0)
                {
                    foreach (var a10Status in mA10Status)
                    {
                        var idCluster = Regex.Replace(a10Status.name, @"[^\w\d]", "");
                        listLinkcounter++;

                        sb.AppendLine("<tr>");
                        sb.AppendLine($"<td>{listLinkcounter}</td>");
                        sb.AppendLine($"<td>{a10Status.id}</td>");
                        sb.AppendLine($"<td>{a10Status.name}</td>");
                        sb.AppendLine($"<td>{a10Status.status}</td>");
                        sb.AppendLine($"<td>{a10Status.paramsvalue}</td>");
                        sb.AppendLine($"<td>{a10Status.type}</td>");

                        // Modem status
                        if (a10Status.mProduct?.Status != null)
                        {
                            var statusText = a10Status.mProduct.Status.Value ? "online" : "offline";
                            var cssClass = a10Status.mProduct.Status.Value ? "yesno-green" : "yesno-red";
                            sb.AppendLine($"    <td><span class='{cssClass}'>{statusText}</span></td>");
                        }
                        else sb.AppendLine("    <td></td>");

                        // Modem number
                        if (!string.IsNullOrEmpty(a10Status?.mProduct?.DeviceID))
                            sb.AppendLine($"    <td>{a10Status.mProduct.DeviceID}</td>");
                        else
                            sb.AppendLine("    <td></td>");

                        // Connector status
                        if (!string.IsNullOrEmpty(a10Status?.mProduct?.ConnectorStatus))
                        {
                            var cssClass = a10Status.mProduct.ConnectorStatus.Trim().ToLower() == "yes" ? "yesno-green" : "yesno-red";
                            sb.AppendLine($"    <td><span class='{cssClass}'>{a10Status.mProduct.ConnectorStatus}</span></td>");
                        }
                        else sb.AppendLine("    <td></td>");


                        // Replaced dropdown
                        sb.AppendLine($"<td class='text-center'>");
                        if (!string.IsNullOrEmpty(a10Status.MatrialCode))
                            sb.AppendLine("Yes");
                        else if (string.IsNullOrEmpty(a10Status.MatrialCode) && a10Status.IsResolved != null)
                            sb.AppendLine("No");

                        sb.AppendLine($"</td>");


                        // Reason
                        var reason = a10Status.Reason ?? "";
                        sb.AppendLine($"<td>{reason}</td>");

                        // Code
                        var code = a10Status.MatrialCode ?? "";
                        sb.AppendLine($"<td>{code}</td>");

                        // Ground Staff dropdown
                        if (MaintainerUsers != null && a10Status.MaintainerUserId != null)
                        {
                            foreach (var maintainerUsers in MaintainerUsers)
                            {
                                if (a10Status.MaintainerUserId == maintainerUsers.Id)
                                {
                                    sb.AppendLine($"<td>{maintainerUsers.FirstName} {maintainerUsers.LastName}</td>");
                                    break;
                                }
                            }
                        }
                        else
                        {
                            sb.AppendLine($"<td></td>");
                        }
                        // Validate
                        if (a10Status.IsResolved != null)
                        {
                            if (a10Status.IsResolved.Value)
                                sb.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/validateIcon2.png' /></td>");
                            else
                                sb.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/Warning.png' /></td>");
                        }
                        else
                        {
                            sb.AppendLine($"<td class='text-center'><img src='{ConfigurationManager.AppSettings.Get("WebBaseUrl")}/assets/images/validateIcon1.png' /></td>");
                        }

                        sb.AppendLine("</tr>");
                    }
                }
                else
                {
                    sb.AppendLine("<tr><td colspan='15'></td></tr>");
                }

                sb.AppendLine("</tbody>");
                sb.AppendLine("</table>");


                htmlTable += sb.ToString();
            }



            #endregion

            if (pdfTemplate.IsNotNullOrEmpty() && htmlTable.IsNotNullOrEmpty())
                pdfTemplate = pdfTemplate.Replace("{%tabledata%}", htmlTable);
            else
                pdfTemplate = pdfTemplate.Replace("{%tabledata%}", string.Empty);


            if (pdfTemplate.IsNotNullOrEmpty() && mSite != null && mSite.Id > 0)
                pdfTemplate = pdfTemplate.Replace("{%SiteName%}", mSite.Name);
            else
                pdfTemplate = pdfTemplate.Replace("{%SiteName%}", string.Empty);

            pdfTemplate = pdfTemplate.Replace("{%header%}", "Combine WatchList");

            pdfTemplate = pdfTemplate.Replace("{%Date%}", DateTime.Now.ToString("dd/MM/yyyy"));

            HtmlToPdfConverter htmlToPdf = new NReco.PdfGenerator.HtmlToPdfConverter();
            htmlToPdf.Margins.Top = 0;
            htmlToPdf.Margins.Bottom = 0;
            htmlToPdf.Margins.Left = 0;
            htmlToPdf.Margins.Right = 0;
            htmlToPdf.Size = NReco.PdfGenerator.PageSize.A3;
            var res = htmlToPdf.GeneratePdf(pdfTemplate);

            var responses = System.Web.HttpContext.Current.Response;
            responses.BufferOutput = true;
            responses.Clear();
            responses.ClearHeaders();
            responses.AddHeader("content-disposition", $"attachment;filename=Test-{DateTime.Now.ToString("dd-MM-yyyy h-mm tt")}.pdf");
            responses.ContentType = System.Net.Mime.MediaTypeNames.Application.Pdf;
            responses.ContentEncoding = System.Text.Encoding.UTF8;
            responses.BinaryWrite(res);
            responses.End();


            return PartialView(mSite);
        }

        public ActionResult CheckCombineWatchlist(Domain.CombineOverallTarget mCombineOverallTarget)
        {
            dynamic data = new ExpandoObject();
            if (mCombineOverallTarget.SiteId <= 0)
            {
                data.type = "error";
                data.result = "Site Not Found.";
            }
            else
            {
                if (mCombineOverallTarget.TypeId == 1)
                {
                    mCombineOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                    try
                    {
                        using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                        {
                            var jsonStr = JsonConvert.SerializeObject(mCombineOverallTarget);
                            StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                            var response = hcf.client.PostAsync(String.Format("CombineOverallTarget"), str).Result;
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            if (response.StatusCode == HttpStatusCode.NoContent)
                            {
                                //data = new { type = "success", result = "FRS Watch List has been updated." };
                                data.type = "pending";
                                data.result = "Record has been added for validate.";
                            }
                            else
                            {
                                data.type = "error";
                                data.result = "Internal server error.";
                            }
                        }
                    }
                    catch (Exception)
                    {
                        data.type = "error";
                        data.result = "Internal server error.";
                    }
                }
                else
                {
                    var asset = assetService.Get(mCombineOverallTarget.AssetId.Value);
                    var mAssetAttribute = assetAttributeService.Get(mCombineOverallTarget.AttributeId.Value);
                    //var mAssetAttributes = assetAttributeService.GetAllAssetAttributeBy(mCombineOverallTarget.AssetTypeId.Value);
                    if (mCombineOverallTarget.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.TRACK)
                    {
                        var mDynamoTables = GetDynamoTable(mCombineOverallTarget.SiteId);
                        if (mDynamoTables != null && mDynamoTables.Count > 0)
                        {
                            var mDynamoTable = mDynamoTables.Where(x => x.AssetId == mCombineOverallTarget.AssetId).FirstOrDefault();
                            if (mDynamoTable != null)
                            {
                                var mAssetAttributes = assetAttributeService.GetAllAssetAttributeBy(mCombineOverallTarget.AssetTypeId.Value);
                                if (mAssetAttributes != null && mAssetAttributes.Count > 0)
                                {
                                    assetAttributeService.PrepareGraphAttribute(mAssetAttributes, mAssetAttributes, mDynamoTable.CsvData);

                                    data = CheckTrackData(mCombineOverallTarget, mAssetAttribute, mAssetAttributes);

                                }
                            }
                        }
                        else
                        {

                            data.type = "error";
                            data.result = "MQTT data Not Found.";
                        }

                        //CreateJobCardOverall(mCombineOverallTarget);
                    }
                    else if (mCombineOverallTarget.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.POINT_MACHINE)
                    {
                        mCombineOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mCombineOverallTarget);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PostAsync(String.Format("CombineOverallTarget"), str).Result;
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {
                                    //data = new { type = "success", result = "FRS Watch List has been updated." };
                                    data.type = "pending";
                                    data.result = "Record has been added for validate.";
                                }
                                else
                                {
                                    data.type = "error";
                                    data.result = "Internal server error.";
                                }
                            }
                        }
                        catch (Exception)
                        {
                            data.type = "error";
                            data.result = "Internal server error.";
                        }
                    }
                    else if (mCombineOverallTarget.AssetTypeId == (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL)
                    {
                        mCombineOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                        try
                        {
                            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                            {
                                var jsonStr = JsonConvert.SerializeObject(mCombineOverallTarget);
                                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                                var response = hcf.client.PostAsync(String.Format("CombineOverallTarget"), str).Result;
                                string jsonString = response.Content.ReadAsStringAsync().Result;
                                if (response.StatusCode == HttpStatusCode.NoContent)
                                {
                                    //data = new { type = "success", result = "FRS Watch List has been updated." };
                                    data.type = "pending";
                                    data.result = "Record has been added for validate.";
                                }
                                else
                                {
                                    data.type = "error";
                                    data.result = "Internal server error.";
                                }
                            }
                        }
                        catch (Exception)
                        {
                            data.type = "error";
                            data.result = "Internal server error.";
                        }
                    }

                }


            }



            return Json(data, JsonRequestBehavior.AllowGet);
        }

        private ExpandoObject CheckSignalData(Domain.Asset mAsset, Domain.CombineOverallTarget mCombineOverallTarget, AssetAttribute mAssetAttribute, List<AssetAttribute> mAssetAttributes)
        {
            dynamic data = new ExpandoObject();
            var mAllDynamoTablesSignal = GetAllGraphData(mCombineOverallTarget.SiteId, (int)E7FRSAdvance.Utility.Utility.AssetType.SIGNAL);

            if (mAllDynamoTablesSignal != null && mAllDynamoTablesSignal.Count > 0 && mAssetAttribute != null && mAssetAttribute.Id > 0)
            {
                var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                if (csvDatas != null && csvDatas.Count > 0)
                {
                    if (mAsset.Name.ToLower().Contains("sh"))
                    {
                        bool isDownChecked = false;
                        foreach (var csvData in csvDatas)
                        {
                            var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                            if (vale > 0)
                            {
                                if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 50 && vale < 65)
                                {
                                    isDownChecked = true;
                                    break;
                                }
                                else if (mAssetAttribute.Title.ToLower().Contains(" v") && (vale > 92 && vale < 130))
                                {
                                    isDownChecked = true;
                                    break;
                                }
                            }
                        }
                        if (!isDownChecked)
                        {
                            if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                            {
                                data.type = "error";
                                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {50} and maximum range is {65}.";
                            }
                            else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                            {
                                data.type = "error";
                                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {92} and maximum range is {150}.";
                            }

                        }
                        else
                        {
                            data.type = "success";
                            data.result = "Status has been Updated";
                        }
                    }
                    else
                    {
                        bool isDownChecked = false;
                        foreach (var csvData in csvDatas)
                        {
                            var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                            if (vale > 0)
                            {
                                if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 92 && vale < 150)
                                {
                                    isDownChecked = true;
                                    break;
                                }
                                else if (mAssetAttribute.Title.ToLower().Contains(" v") && (vale > 92 && vale < 130))
                                {
                                    isDownChecked = true;
                                    break;
                                }
                            }
                        }
                        if (!isDownChecked)
                        {
                            if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                            {
                                data.type = "error";
                                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {92} and maximum range is {150}.";
                            }
                            else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                            {
                                data.type = "error";
                                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {92} and maximum range is {150}.";
                            }

                        }
                        else
                        {
                            data.type = "success";
                            data.result = "Status has been Updated";
                            CreateJobCardOverall(mCombineOverallTarget);
                        }

                    }

                }

                //if (mCombineOverallTarget.CauseCode.Contains(" Down"))
                //{
                //    var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                //    if (csvDatas != null && csvDatas.Count > 0)
                //    {
                //        bool isDownChecked = false;
                //        foreach (var csvData in csvDatas)
                //        {
                //            var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                //            if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 85)
                //            {
                //                isDownChecked = true;
                //                break;
                //            }
                //            else if (mAssetAttribute.Title.ToLower().Contains(" v") && vale > 82)
                //            {
                //                isDownChecked = true;
                //                break;
                //            }
                //        }
                //        if (!isDownChecked)
                //        {
                //            if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                //            {
                //                data.type = "error";
                //                data.result = $"Current Value not in range because {mAssetAttribute.Title} minimum range is {85}.";
                //            }
                //            else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                //            {
                //                data.type = "error";
                //                data.result = $"Current Value not in range because {mAssetAttribute.Title} minimum range is {82}.";
                //            }

                //        }
                //        else
                //        {
                //            data.type = "success";
                //            data.result = "Status has been Updated";
                //        }
                //    }
                //}
                //else if (mCombineOverallTarget.CauseCode.Contains("Out of Range"))
                //{
                //    var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                //    if (csvDatas != null && csvDatas.Count > 0)
                //    {
                //        bool isDownChecked = false;
                //        foreach (var csvData in csvDatas)
                //        {
                //            var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                //            if (mAssetAttribute.Title.ToLower().Contains(" ma") && (vale > 92 || vale < 150))
                //            {
                //                isDownChecked = true;
                //                break;
                //            }
                //            else if (mAssetAttribute.Title.ToLower().Contains(" v") && (vale > 92 || vale < 130))
                //            {
                //                isDownChecked = true;
                //                break;
                //            }
                //        }
                //        if (!isDownChecked)
                //        {
                //            if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                //            {
                //                data.type = "error";
                //                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {92} and maximum range is {150}.";
                //            }
                //            else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                //            {
                //                data.type = "error";
                //                data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {92} and maximum range is {130}.";
                //            }

                //        }
                //        else
                //        {
                //            data.type = "success";
                //            data.result = "Status has been Updated";
                //        }
                //    }
                //}
                //else if (mCombineOverallTarget.CauseCode.Contains("V/I LOW"))
                //{
                //    var mFRSAttributeRanges = GetFRSAttributeRange(mCombineOverallTarget.AssetId.Value);
                //    if (mFRSAttributeRanges != null && mFRSAttributeRanges.Any(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value))
                //    {
                //        var mFRSAttributeRange = mFRSAttributeRanges.FirstOrDefault(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value);

                //        if (mFRSAttributeRange != null && mFRSAttributeRange.Id > 0 && mAssetAttributes != null && mAssetAttributes.Count > 0 && mFRSAttributeRange.MinSafeValue != null)
                //        {
                //            var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                //            if (csvDatas != null && csvDatas.Count > 0)
                //            {
                //                bool isDownChecked = false;
                //                foreach (var csvData in csvDatas)
                //                {
                //                    var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 0 && vale > (0.9m * mFRSAttributeRange.MinSafeValue))
                //                    {
                //                        isDownChecked = true;
                //                        break;
                //                    }
                //                    else if (mAssetAttribute.Title.ToLower().Contains(" v") && vale > 0 && vale > (0.9m * mFRSAttributeRange.MinSafeValue))
                //                    {
                //                        isDownChecked = true;
                //                        break;
                //                    }
                //                }
                //                if (!isDownChecked)
                //                {
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {mFRSAttributeRange.MinSafeValue}.";
                //                    }
                //                    else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {mFRSAttributeRange.MinSafeValue}.";
                //                    }

                //                }
                //                else
                //                {
                //                    data.type = "success";
                //                    data.result = "Status has been Updated";
                //                }
                //            }
                //        }
                //        else
                //        {
                //            data.type = "error";
                //            data.result = $"MinSafe Value is not inserted for {mAssetAttribute.Title} attribute.";
                //        }
                //    }


                //}
                //else if (mCombineOverallTarget.CauseCode.Contains("CURR HIGH"))
                //{
                //    var mFRSAttributeRanges = GetFRSAttributeRange(mCombineOverallTarget.AssetId.Value);
                //    if (mFRSAttributeRanges != null && mFRSAttributeRanges.Any(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value))
                //    {
                //        var mFRSAttributeRange = mFRSAttributeRanges.FirstOrDefault(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value);

                //        if (mFRSAttributeRange != null && mFRSAttributeRange.Id > 0 && mAssetAttributes != null && mAssetAttributes.Count > 0 && mFRSAttributeRange.MaxSafeValue != null)
                //        {
                //            var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                //            if (csvDatas != null && csvDatas.Count > 0)
                //            {
                //                bool isDownChecked = false;
                //                foreach (var csvData in csvDatas)
                //                {
                //                    var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 0 && vale < (1.1m * mFRSAttributeRange.MaxSafeValue))
                //                    {
                //                        isDownChecked = true;
                //                        break;
                //                    }

                //                }
                //                if (!isDownChecked)
                //                {
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} maximum range is {mFRSAttributeRange.MaxSafeValue}.";
                //                    }
                //                    else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} maximum range is {mFRSAttributeRange.MaxSafeValue}.";
                //                    }

                //                }
                //                else
                //                {
                //                    data.type = "success";
                //                    data.result = "Status has been Updated";
                //                }
                //            }
                //        }
                //        else
                //        {
                //            data.type = "error";
                //            data.result = $"MaxSafe Value is not inserted for {mAssetAttribute.Title} attribute.";
                //        }
                //    }


                //}
                //else if (mCombineOverallTarget.CauseCode.Contains(" LOW"))
                //{
                //    var mFRSAttributeRanges = GetFRSAttributeRange(mCombineOverallTarget.AssetId.Value);
                //    if (mFRSAttributeRanges != null && mFRSAttributeRanges.Any(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value))
                //    {
                //        var mFRSAttributeRange = mFRSAttributeRanges.FirstOrDefault(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value);

                //        if (mFRSAttributeRange != null && mFRSAttributeRange.Id > 0 && mAssetAttributes != null && mAssetAttributes.Count > 0 && mFRSAttributeRange.MinSafeValue != null)
                //        {
                //            var csvDatas = mAllDynamoTablesSignal.Where(x => x.AssetId == mCombineOverallTarget.AssetId).OrderByDescending(x => x.TimeStamp).Select(x => x.CsvData).ToList();
                //            if (csvDatas != null && csvDatas.Count > 0)
                //            {
                //                bool isDownChecked = false;
                //                foreach (var csvData in csvDatas)
                //                {
                //                    var vale = GetSignalAssetAttribute(mAssetAttributes, csvData, mAssetAttribute.Id);
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma") && vale > 0 && vale > (1.1m * mFRSAttributeRange.MinSafeValue))
                //                    {
                //                        isDownChecked = true;
                //                        break;
                //                    }
                //                }
                //                if (!isDownChecked)
                //                {
                //                    if (mAssetAttribute.Title.ToLower().Contains(" ma"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {mFRSAttributeRange.MinSafeValue}.";
                //                    }
                //                    else if (mAssetAttribute.Title.ToLower().Contains(" v"))
                //                    {
                //                        data.type = "error";
                //                        data.result = $"Current Value is not in range because {mAssetAttribute.Title} minimum range is {mFRSAttributeRange.MinSafeValue}.";
                //                    }

                //                }
                //                else
                //                {
                //                    data.type = "success";
                //                    data.result = "Status has been Updated";
                //                }
                //            }
                //        }
                //        else
                //        {
                //            data.type = "error";
                //            data.result = $"Min Safe Value is not inserted for {mAssetAttribute.Title} attribute.";
                //        }
                //    }


                //}
                //else if (mCombineOverallTarget.CauseCode.Contains("SIG BLANK"))
                //{


                //}
            }


            return data;
        }

        private ExpandoObject CheckPointData(SearchCriteria searchCriteria, AssetAttribute mAssetAttribute, List<AssetAttribute> mAssetAttributes)
        {
            throw new NotImplementedException();
        }

        private ExpandoObject CheckTrackData(Domain.CombineOverallTarget mCombineOverallTarget, Domain.AssetAttribute mAssetAttribute, List<AssetAttribute> mAssetAttributes)
        {
            dynamic data = new ExpandoObject();

            try
            {
                bool isTrainMoment = false;
                double? leakage = null;

                var machedTPRRow = mAssetAttributes.FirstOrDefault(x => x.Title.ToUpper() == Common.t_TPRV);
                var machedIFMARow = mAssetAttributes.FirstOrDefault(x => x.Title.ToUpper() == Common.t_IfmA);
                var machedIRMARow = mAssetAttributes.FirstOrDefault(x => x.Title.ToUpper() == Common.t_IrmA);

                if (machedTPRRow != null && machedTPRRow.Data != null && machedIRMARow != null && machedIRMARow.Data != null && machedIFMARow != null && machedIFMARow.Data != null)
                {
                    decimal.TryParse(machedTPRRow.Data.FirstOrDefault(), out decimal tprattributeValue);
                    decimal.TryParse(machedIRMARow.Data.FirstOrDefault(), out decimal irattributeValue);
                    decimal.TryParse(machedIFMARow.Data.FirstOrDefault(), out decimal ifattributeValue);

                    var leakage1 = ifattributeValue - irattributeValue;
                    if (tprattributeValue < 5 && leakage1 > 400)
                    {
                        isTrainMoment = true;
                        mCombineOverallTarget.IsResolved = false;
                        CreateJobCardOverall(mCombineOverallTarget);
                    }
                }


                var mFRSAttributeRanges = GetFRSAttributeRange(mCombineOverallTarget.AssetId.Value);
                if (mFRSAttributeRanges != null && mFRSAttributeRanges.Any(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value))
                {
                    var mFRSAttributeRange = mFRSAttributeRanges.FirstOrDefault(x => x.AssetAttributeId == mCombineOverallTarget.AttributeId.Value);

                    if (mFRSAttributeRange != null && mFRSAttributeRange.Id > 0 && mAssetAttributes != null && mAssetAttributes.Count > 0)
                    {

                        var dataAttr = mAssetAttributes.Where(x => x.Id == mAssetAttribute.Id).FirstOrDefault();
                        if (dataAttr != null && dataAttr.Id > 0 && dataAttr.Data != null && dataAttr.Data.Count > 0)
                        {
                            decimal.TryParse(dataAttr.Data.FirstOrDefault(), out decimal attributeValue);
                            if (mFRSAttributeRange.MinSafeValue != null && mFRSAttributeRange.MaxSafeValue != null)
                            {
                                if (attributeValue > mFRSAttributeRange.MinSafeValue && attributeValue < mFRSAttributeRange.MaxSafeValue)
                                {
                                    data.type = "success";
                                    data.result = "Status has been Updated";
                                    mCombineOverallTarget.IsResolved = true;
                                    CreateJobCardOverall(mCombineOverallTarget);
                                }
                                else
                                {
                                    data.type = "error";
                                    data.result = $"Current Value is {attributeValue} not in range because Vr minimum range is {mFRSAttributeRange.MinSafeValue} and maximum range is {mFRSAttributeRange.MaxSafeValue}.";
                                }
                            }
                            else if (!string.IsNullOrEmpty(mAssetAttribute.Title))
                            {
                                var title = mAssetAttribute.Title.ToUpper();

                                if (title == Common.t_IrmA && attributeValue > 400)
                                {
                                    data.type = "error";
                                    data.result = $"Current Value is {attributeValue} not in range because IrmA maximum range is 400.";
                                }
                                else if (title == Common.t_ChargerMA && (attributeValue < 100 || attributeValue > 3000))
                                {
                                    data.type = "error";
                                    data.result = $"Current Value is {attributeValue} not in range because ChargerMA minimum range is 100 and maximum range is 3000.";
                                }
                                else if (title == Common.t_ChargerV && (attributeValue < 100 || attributeValue > 120))
                                {
                                    data.type = "error";
                                    data.result = $"Current Value is {attributeValue} not in range because ChargerV minimum range is 100 and maximum range is 120.";
                                }

                                if (!isTrainMoment)
                                {
                                    if (title == Common.t_IfmA && (attributeValue < 250 || attributeValue > 500))
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because IfmA minimum range is 250 and maximum range is 500.";
                                    }
                                    else if (title == Common.t_IrmA && attributeValue < 250)
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because IrmA minimum range is 250.";
                                    }
                                    else if (title == Common.t_Vr && (attributeValue < 2 || attributeValue > 4.5m))
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because Vr minimum range is 2 and maximum range is 4.5.";
                                    }
                                    else if (title == Common.t_Vf && (attributeValue < 2.5m || attributeValue > 5))
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because Vf minimum range is 2.5 and maximum range is 5.";
                                    }
                                    else if (title == Common.t_TPRV && (attributeValue < 18 || attributeValue > 35))
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because TPRV minimum range is 18 and maximum range is 35.";
                                    }
                                    else if (title == Common.t_ChokeV && (attributeValue < 0.6m || attributeValue > 1.8m))
                                    {
                                        data.type = "error";
                                        data.result = $"Current Value is {attributeValue} not in range because ChokeV minimum range is 0.6 and maximum range is 1.8.";
                                    }
                                    else if (mAssetAttribute.Title == "Leakage")
                                    {
                                        // Leakage-related logic if needed
                                    }
                                    else
                                    {
                                        data.type = "success";
                                        data.result = "Status has been Updated";
                                        mCombineOverallTarget.IsResolved = true;
                                        CreateJobCardOverall(mCombineOverallTarget);
                                    }
                                }
                                else
                                {
                                    data.type = "pending";
                                    data.result = $"Currently TrainMoment secution on this track please wait for some time for validate";
                                }
                            }
                        }


                    }
                }
                else
                {
                    data.type = "error";
                    data.result = "Attribute Range Not Found.";
                }
            }
            catch (Exception ex)
            {
                data.type = "error";
                data.result = "An unexpected error occurred.";
                // Optionally log ex.Message
            }

            return data;
        }


        private List<MultipleLog> GetAllGraphData(int siteId, int assetTypeId)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("Asset/GetAllGraphData/{0}/{1}", siteId, assetTypeId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                        }

                    }
                }
                catch (Exception ex)
                {
                }
            }

            return mDynamoTables;
        }

        private decimal GetSignalAssetAttribute(List<AssetAttribute> allAssetAttributes, string csvData, int attributeId)
        {
            decimal value = 0.0m;

            try
            {
                List<AssetAttribute> attributes = new List<AssetAttribute>();
                var array = csvData.Split('~');
                int i = 3;
                foreach (var item in allAssetAttributes)
                {
                    var at = new AssetAttribute();
                    at.Title = item.Title;
                    at.Id = item.Id;
                    if (array.Length > i && !string.IsNullOrEmpty(array[i]))
                    {
                        at.Data.Add(Convert.ToString(array[i]));
                    }
                    else
                    {
                        at.Data.Add("0");
                    }
                    i++;

                    attributes.Add(at);
                }

                var sds = attributes.Where(x => x.Id == attributeId).FirstOrDefault();
                if (sds != null)
                {
                    var dsd = sds.Data.FirstOrDefault();
                    if (Convert.ToDecimal(dsd) > 30)
                    {
                        return Convert.ToDecimal(dsd);

                    }
                }
            }
            catch (Exception)
            {

            }
            return value;

        }

        public void CreateJobCardOverall(Domain.CombineOverallTarget mCombineOverallTarget)
        {
            mCombineOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCombineOverallTarget);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                }
            }
            catch (Exception)
            {
            }


        }

        public ActionResult CombineWatchlistClear(int siteId)
        {
            return View(siteService.Get(siteId));
        }

        public ActionResult _CombineWatchlistClearPartial(Domain.SearchCriteria mSearchCriteria)
        {
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();
            mSearchCriteria.IsCheckResolved = true;
            mSearchCriteria.IsResolved = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/GetBy"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mCombineOverallTargets = JsonConvert.DeserializeObject<List<Domain.CombineOverallTarget>>(jsonString);

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
            finally
            {
                //BindPDFDropDown();
            }
            return PartialView(mCombineOverallTargets);
        }

        public ActionResult DownloadClearCombineWatchlist(Domain.SearchCriteria mSearchCriteria)
        {
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();
            mSearchCriteria.IsCheckResolved = true;
            mSearchCriteria.IsResolved = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/DownloadPDF"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = response.Content.ReadAsStringAsync().Result;
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            string modifiedString = stringInBase64.Substring(1, stringInBase64.Length - 2);
                            byte[] csvbytes = System.Convert.FromBase64String(modifiedString);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"ClearCombineWatchlist.pdf");
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
            finally
            {
                //BindPDFDropDown();
            }
            return RedirectToAction("CombineWatchlistClear", new { siteId = mSearchCriteria.SiteId });
        }

        public ActionResult CombineWatchlistPending(int siteId)
        {
            return View(siteService.Get(siteId));
        }

        public ActionResult _CombineWatchlistPendingPartial(Domain.SearchCriteria mSearchCriteria)
        {
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();
            mSearchCriteria.IsCheckResolved = true;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/GetBy"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mCombineOverallTargets = JsonConvert.DeserializeObject<List<Domain.CombineOverallTarget>>(jsonString);

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
            finally
            {
                //BindPDFDropDown();
            }
            return PartialView(mCombineOverallTargets);
        }

        public ActionResult DownloadPendingCombineWatchlist(Domain.SearchCriteria mSearchCriteria)
        {
            mSearchCriteria.IsCheckResolved = true;
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/DownloadPDF"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = response.Content.ReadAsStringAsync().Result;
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            string modifiedString = stringInBase64.Substring(1, stringInBase64.Length - 2);
                            byte[] csvbytes = System.Convert.FromBase64String(modifiedString);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"PendingCombineWatchlist.pdf");
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
            finally
            {
                //BindPDFDropDown();
            }
            return RedirectToAction("CombineWatchlistPending", new { siteId = mSearchCriteria.SiteId });
        }

        public ActionResult _CombineWatchListHistory(Domain.SearchCriteria mSearchCriteria)
        {
            ViewBag.CalibrationDetail = GetCalibrationDetail(mSearchCriteria.AssetId, mSearchCriteria.AssetAttributeId);

            ViewBag.CombineOverallTargets = GetCombineOverallTarget(new SearchCriteria() { AssetId = mSearchCriteria.AssetId, AssetAttributeId = mSearchCriteria.AssetAttributeId });

            ViewBag.CombineLiskIssueOverallTargets = GetCombineOverallTarget(new SearchCriteria() { A10Id = mSearchCriteria.A10Id, TypeId = (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.LinkIssue });
            return PartialView(mSearchCriteria);
        }

        public ActionResult _CombineLiskIssueHistory(Domain.SearchCriteria mSearchCriteria)
        {
            ViewBag.CombineLiskIssueOverallTargets = GetCombineOverallTarget(mSearchCriteria);
            return PartialView("_CombineWatchListHistory");
        }

        public ActionResult DownloadCombineWatchListHistory(Domain.SearchCriteria mSearchCriteria)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/DownloadHistoryPDF"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = response.Content.ReadAsStringAsync().Result;
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            string modifiedString = stringInBase64.Substring(1, stringInBase64.Length - 2);
                            byte[] csvbytes = System.Convert.FromBase64String(modifiedString);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"WatchlistHistory.pdf");
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
            finally
            {
                //BindPDFDropDown();
            }

            return View();
        }

        public ActionResult GetA10ADCLinkIssue(int siteId, string clusterName)
        {
            var mA10Status = new List<Domain.A10Status>();
            var date = DateTime.Now.ToString("d-M-yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mA10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (mA10Status != null && mA10Status.Count > 0)
                        {
                            mA10Status = mA10Status.Where(x => x.name.Contains(clusterName)).ToList();
                            mA10Status = mA10Status.Where(x => x.id != "0").ToList();


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

        public ActionResult UploadForceFile()
        {
            var data = new Domain.CombineOverallTarget();

            for (int i = 0; i < Request.Files.Count; i++)
            {
                var file = Request.Files[i];

                byte[] thePictureAsBytes = new byte[file.ContentLength];
                using (BinaryReader theReader = new BinaryReader(file.InputStream))
                {
                    thePictureAsBytes = theReader.ReadBytes(file.ContentLength);
                }
                var fileBase64 = Convert.ToBase64String(thePictureAsBytes);
                string extension = System.IO.Path.GetExtension(file.FileName);
                data.FileBase64 = fileBase64;
                data.FileExtension = extension;
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult UpdateForceFile(Domain.CombineOverallTarget mCombineOverallTarget)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mCombineOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mCombineOverallTarget);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"CombineOverallTarget/{mCombineOverallTarget.Id}"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
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

        public ActionResult GetCombineOverallTarget(int id)
        {
            Domain.CombineOverallTarget mSite = new Domain.CombineOverallTarget();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CombineOverallTarget/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.CombineOverallTarget>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Json(mSite, JsonRequestBehavior.AllowGet);
        }

        #endregion


        #region Private Method

        private WatchListLister GetWatchList(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;


            if (mWatchListLister.SearchCriteria.StartDate == null || mWatchListLister.SearchCriteria.StartDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.StartDate = DateTime.Now;

            if (mWatchListLister.SearchCriteria.EndDate == null || mWatchListLister.SearchCriteria.EndDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.EndDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetListerGroupOfAsset"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);
                        if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                        {
                            mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => x.Status == null || (x.Status != null && x.Status.ToLower() != "ok" && x.Status.ToLower() != "resolved")).ToList();
                        }

                    }

                }
            }
            catch (Exception)
            {
            }
            return mWatchListLister;
        }

        private List<Domain.FRSWatchList> GetFRSWatchListBySite(int siteId)
        {
            var mFRSWatchLists = new List<Domain.FRSWatchList>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSWatchList/GetClusterWise/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSWatchLists = JsonConvert.DeserializeObject<List<Domain.FRSWatchList>>(jsonString);

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
            }
            return mFRSWatchLists;
        }

        private List<string> GetIssueTypes()
        {
            var issueTypes = new List<string>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardOverall/GetIssueTypes")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        issueTypes = JsonConvert.DeserializeObject<List<string>>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return issueTypes;
        }

        private List<Domain.A10Status> GetA10StatusList(int siteId)
        {
            var mA10Status = new List<Domain.A10Status>();
            var date = DateTime.Now.ToString("d-M-yyyy");
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"A10Status/GetGroupOfFixTimestamp/SiteId/{siteId}/SearchDate/{date.Replace("/", "-")}")).Result;
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
                            //var maxdate = a10Status.Select(x => x.ConvertDate).Max();
                            //a10Status = a10Status.Where(x => x.ConvertDate == maxdate).ToList();
                            //foreach (var newA10Status in a10Status)
                            //{
                            //    mA10Status.Add(newA10Status);
                            //}

                        }

                    }
                }
            }
            catch (Exception ex)
            {
            }

            var jsonResult = Json(mA10Status, JsonRequestBehavior.AllowGet);
            jsonResult.MaxJsonLength = int.MaxValue;
            return mA10Status;
        }

        private List<Domain.Product> GetA10Products(int siteId)
        {
            var mProducts = new List<Domain.Product>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Product/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mProducts = JsonConvert.DeserializeObject<List<Domain.Product>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return mProducts;
        }

        public List<Domain.MaintainerUser> GetMaintainerUser()
        {
            var mMaintainerUsers = new List<Domain.MaintainerUser>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintainerUser/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMaintainerUsers = JsonConvert.DeserializeObject<List<Domain.MaintainerUser>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mMaintainerUsers;
        }

        private List<MultipleLog> GetDynamoTable(int siteId)
        {
            List<MultipleLog> mDynamoTables = new List<MultipleLog>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("Asset/GetGraphData/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mDynamoTables = JsonConvert.DeserializeObject<List<MultipleLog>>(jsonString);
                        }

                    }
                }
                catch (Exception ex)
                {
                }
            }

            return mDynamoTables;
        }

        private List<Domain.FRSAttributeRange> GetFRSAttributeRange(int assetId)
        {
            List<Domain.FRSAttributeRange> mFRSAttributeRanges = new List<Domain.FRSAttributeRange>();
            try
            {
                using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = mHttpClientFactory.client.GetAsync(String.Format("FRSAttributeRange/AssetId/{0}", assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAttributeRanges = JsonConvert.DeserializeObject<List<Domain.FRSAttributeRange>>(jsonString);
                    }

                }
            }
            catch (Exception ex)
            {
            }

            return mFRSAttributeRanges;
        }

        private List<Domain.CombineOverallTarget> GetWithOutWatchList(Domain.SearchCriteria mSearchCriteria)
        {
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();

            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/GetWithOutWatchList"), str).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    mCombineOverallTargets = JsonConvert.DeserializeObject<List<Domain.CombineOverallTarget>>(jsonString);

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

            return mCombineOverallTargets;
        }

        private List<Domain.CombineOverallTarget> GetCombineOverallTarget(Domain.SearchCriteria mSearchCriteria)
        {
            var mCombineOverallTargets = new List<Domain.CombineOverallTarget>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("CombineOverallTarget/GetBy"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mCombineOverallTargets = JsonConvert.DeserializeObject<List<Domain.CombineOverallTarget>>(jsonString);

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
            finally
            {
                //BindPDFDropDown();
            }

            return mCombineOverallTargets;
        }

        private List<Domain.CalibrationDetail> GetCalibrationDetail(int assetId, int assetAttributeId)
        {
            List<Domain.CalibrationDetail> mCalibrationDetails = new List<Domain.CalibrationDetail>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"CalibrationDetail/AssetId/{assetId}/AssetAttributeId/{assetAttributeId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCalibrationDetails = JsonConvert.DeserializeObject<List<Domain.CalibrationDetail>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }

            return mCalibrationDetails;
        }



        #endregion
    }
}