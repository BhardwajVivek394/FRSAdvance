using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class SiteKeepingController : Controller
    {
        // GET: SiteKeeping
        private readonly ISiteKeepingService siteKeepingService;
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        private readonly IZoneService zoneService;
        public SiteKeepingController(ISiteKeepingService siteKeepingService, IDivisionService divisionService, ISiteService siteService, IZoneService zoneService)
        {
            this.siteKeepingService = siteKeepingService;
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.zoneService = zoneService;
        }
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult _RFStatus(RFStatusLister mRFStatusLister)
        {
            mRFStatusLister.Pager.Take = mRFStatusLister.Pager.PageSize;
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
                            ViewBag.Houres = mRFStatusLister.mRFStatus.Select(x => x.TimeStamp.ToString("hh tt")).ToList().Distinct().ToList();
                            TempData["RFStatus"] = mRFStatusLister.mRFStatus;
                            TempData.Keep();
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            return PartialView(mRFStatusLister);
        }

        public ActionResult _SiteStatus(SiteStatusLister mSiteStatusLister)
        {
            mSiteStatusLister.Pager.Take = mSiteStatusLister.Pager.PageSize;
            mSiteStatusLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteStatusLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            if (mSiteStatusLister != null && mSiteStatusLister.SearchCriteria != null && mSiteStatusLister.SearchCriteria.TimeStamp != null && mSiteStatusLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            {
                mSiteStatusLister.SearchCriteria.TimeStamp = DateTime.Now;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteStatusLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetSiteStatusLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteStatusLister = JsonConvert.DeserializeObject<SiteStatusLister>(jsonString);
                        if (mSiteStatusLister != null && mSiteStatusLister.mSiteStatus != null && mSiteStatusLister.mSiteStatus.Count > 0)
                        {
                            ViewBag.Houres = mSiteStatusLister.mSiteStatus.Select(x => x.TimeStamp.ToString("hh tt")).ToList().Distinct().ToList();
                            TempData["SiteStatus"] = mSiteStatusLister.mSiteStatus;
                            TempData.Keep();
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            return PartialView(mSiteStatusLister);
        }

        public ActionResult _WatchList(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = mWatchListLister.Pager.PageSize;
            mWatchListLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mWatchListLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;


            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.CreatedDate != null && mWatchListLister.SearchCriteria.CreatedDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.CreatedDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetWatchListLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);

                        TempData.Remove("WatchListLister");
                        TempData["WatchListLister"] = mWatchListLister;
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            return PartialView(mWatchListLister);
        }

        public ActionResult _TrainRunningMoment(TrainRunningMomentLister mTrainRunningMomentLister)
        {
            mTrainRunningMomentLister.Pager.Take = mTrainRunningMomentLister.Pager.PageSize;

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.StartDate != null && mTrainRunningMomentLister.SearchCriteria.StartDate == DateTime.MinValue)
                mTrainRunningMomentLister.SearchCriteria.StartDate = DateTime.Now;

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.EndDate != null && mTrainRunningMomentLister.SearchCriteria.EndDate == DateTime.MinValue)
                mTrainRunningMomentLister.SearchCriteria.EndDate = DateTime.Now;

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.SiteId > 0)
            {
                mTrainRunningMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mTrainRunningMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetTrainRunningMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mTrainRunningMomentLister = JsonConvert.DeserializeObject<TrainRunningMomentLister>(jsonString);
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mTrainRunningMomentLister);
        }

        public ActionResult ApplyMaintenanceInput(MaintenanceInput mMaintenanceInput)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintenanceInput);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("MaintenanceInput"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Updated." };
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                        data = new { type = "Error", result = "Something went wrong!" };
                    else
                        data = new { type = "Error", result = "Something went wrong!" };

                }
            }
            catch (Exception)
            {
                data = new { type = "Error", result = "Something went wrong!" };
            }

            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _GluedLogDataLister(GluedLogDataLister mGluedLogDataLister)
        {

            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null && mGluedLogDataLister.SearchCriteria.TimeStamp != null && mGluedLogDataLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mGluedLogDataLister.SearchCriteria.TimeStamp = DateTime.Now;

            mGluedLogDataLister.Pager.Take = -1;
            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null && mGluedLogDataLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mGluedLogDataLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetGluedLogDataLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mGluedLogDataLister = JsonConvert.DeserializeObject<GluedLogDataLister>(jsonString);
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

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mGluedLogDataLister);
        }

        public ActionResult _PointMachineSignatureLister(PMDataTableLister mPMDataTableLister)
        {
            if (mPMDataTableLister != null && mPMDataTableLister.SearchCriteria != null && mPMDataTableLister.SearchCriteria.TimeStamp != null && mPMDataTableLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mPMDataTableLister.SearchCriteria.TimeStamp = DateTime.Now;

            mPMDataTableLister.Pager.Take = -1;
            if (mPMDataTableLister != null && mPMDataTableLister.SearchCriteria != null && mPMDataTableLister.SearchCriteria.SiteId > 0)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPMDataTableLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetPointMachineSignatureLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPMDataTableLister = JsonConvert.DeserializeObject<PMDataTableLister>(jsonString);
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

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mPMDataTableLister);
        }

        public ActionResult _IndexAlertLogLister(IndexAlertLogLister mIndexAlertLogLister)
        {
            mIndexAlertLogLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mIndexAlertLogLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null && mIndexAlertLogLister.SearchCriteria.TimeStamp != null && mIndexAlertLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mIndexAlertLogLister.SearchCriteria.TimeStamp = DateTime.Now;

            mIndexAlertLogLister.Pager.Take = mIndexAlertLogLister.Pager.PageSize;
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mIndexAlertLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetIndexAlertLogLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mIndexAlertLogLister = JsonConvert.DeserializeObject<IndexAlertLogLister>(jsonString);
                            if (mIndexAlertLogLister != null && mIndexAlertLogLister.IndexAlertLogs != null && mIndexAlertLogLister.IndexAlertLogs.Count > 0)
                            {
                                TempData["IndexAlertLogs"] = mIndexAlertLogLister.IndexAlertLogs;
                                TempData.Keep();
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

            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mIndexAlertLogLister);
        }

        public ActionResult _SignalMoment(MomentLister mMomentLister)
        {
            mMomentLister.Pager.Take = mMomentLister.Pager.PageSize;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetSignalMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);

                                }

                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mMomentLister);
        }

        public ActionResult _TPRMoment(MomentLister mMomentLister)
        {
            mMomentLister.Pager.Take = mMomentLister.Pager.PageSize;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetTPRMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);
                                    else
                                    {

                                    }

                                }

                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mMomentLister);
        }

        public ActionResult _PointIndication(MomentLister mMomentLister)
        {
            mMomentLister.Pager.Take = mMomentLister.Pager.PageSize;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.TimeStamp != null && mMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

            if (mMomentLister != null && mMomentLister.SearchCriteria != null && mMomentLister.SearchCriteria.SiteId > 0)
            {
                mMomentLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mMomentLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetPointMachineMomentLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mMomentLister = JsonConvert.DeserializeObject<MomentLister>(jsonString);
                            if (mMomentLister != null && mMomentLister.mMoments != null && mMomentLister.mMoments.Count > 0)
                            {
                                var moments = mMomentLister.mMoments.ToList();
                                mMomentLister.mMoments = new List<Moment>();
                                foreach (var moment in moments.OrderBy(x => x.TimeStamp).ToList())
                                {
                                    if (mMomentLister.mMoments.Where(x => x.TimeStamp == moment.TimeStamp && x.Aspect == moment.Aspect).Count() <= 0)
                                        mMomentLister.mMoments.Add(moment);



                                }

                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView(mMomentLister);
        }

        public ActionResult DownloadWatchListCSV(bool isWithCluster)
        {
            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.Pager.Take = -1;

            mWatchListLister = TempData.Peek("WatchListLister") as WatchListLister;

            //if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.TimeStamp != null && mWatchListLister.SearchCriteria.TimeStamp == DateTime.MinValue)
            //{
            //    mWatchListLister.SearchCriteria.TimeStamp = DateTime.Now;
            //}

            if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                //mCardLines = mCardLines.Where(x => x.Pin == null).ToList();
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", "Serial Number", "Site Name", "Asset Type Name", "Cluster Name", "Asset Name", "Attribute Name", "AttributeValue", "Remark", "TimeStamp");
                csv.AppendLine(socsvstring);

                foreach (var watchList in mWatchListLister.WatchLists)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}", watchList.SerialNumber, watchList.SiteName.RemoveComma(), watchList.AssetTypeName.RemoveComma(), watchList.ClusterName.RemoveComma(), watchList.AssetName.RemoveComma(), watchList.AttributeName.RemoveComma(), watchList.AttributeValue.RemoveComma(), watchList.Remark.RemoveComma(), watchList.CreatedDate);
                    csv.AppendLine(socsvstring);


                    if (isWithCluster && watchList.CardLines != null && watchList.CardLines.Count > 0)
                    {
                        var attributeNameGroup = watchList.CardLines.Where(x => x.AttributeName != null).GroupBy(x => x.AttributeName).ToList();

                        if (watchList.AssetName.IsNullOrEmpty())
                            continue;

                        socsvstring = string.Empty;
                        socsvstring += $"{watchList.AssetName},";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            socsvstring += attributeName.Key + ",";
                        }
                        csv.AppendLine(socsvstring);


                        //Cluster
                        socsvstring = string.Empty;
                        socsvstring += "Cluster" + ",";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            foreach (var item in watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                            {
                                socsvstring += "\"" + item.ClusterName + "\"" + ",";
                            }
                            if (watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                socsvstring += "" + ",";

                        }
                        csv.AppendLine(socsvstring);


                        //Card
                        socsvstring = string.Empty;
                        socsvstring += "Card" + ",";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            foreach (var item in watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                            {
                                socsvstring += $"{"\"" + item.CardName + "\""} ({item.Number})" + ",";
                            }
                            if (watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                socsvstring += "" + ",";

                        }
                        csv.AppendLine(socsvstring);


                        //ADC
                        socsvstring = string.Empty;
                        socsvstring += "ADC" + ",";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            foreach (var item in watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                            {
                                socsvstring += $"ADC {item.SequenceNumber} ({item.ADCNumber})" + ",";
                            }
                            if (watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                socsvstring += "" + ",";

                        }
                        csv.AppendLine(socsvstring);


                        //Pin
                        socsvstring = string.Empty;
                        socsvstring += "Pin" + ",";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            foreach (var item in watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                            {
                                socsvstring += item.Pin + ",";
                            }
                            if (watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                socsvstring += "" + ",";

                        }
                        csv.AppendLine(socsvstring);

                        //Role
                        socsvstring = string.Empty;
                        socsvstring += "Role" + ",";
                        foreach (var attributeName in attributeNameGroup)
                        {
                            foreach (var item in watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()))
                            {
                                socsvstring += item.Role + ",";
                            }
                            if (watchList.CardLines.Where(x => x.AssetId == watchList.AssetId && x.AttributeName != null && x.AttributeName.Trim().ToUpper() == attributeName.Key.Trim().ToUpper()).Count() <= 0)
                                socsvstring += "" + ",";

                        }
                        csv.AppendLine(socsvstring);

                        csv.AppendLine();
                    }

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=WatchList" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }

            return View("Index");
        }

        public ActionResult UploadImage(SiteKeeping mSiteKeeping)
        {

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteKeeping);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/UploadImage"), str).Result;

                }
            }
            catch (Exception)
            {

            }
            return Json(string.Empty, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetSites(int zoneId, int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSites/{zoneId}/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
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
            return Json(sites, JsonRequestBehavior.AllowGet);
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


            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetYardConfigByAssetType(int siteId, int assetId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return PartialView("_YardConfigByAssetTypePartial", mCardLines);
        }

        public JsonResult GetAlarmStatus(int assetId)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("SMSLog/GetSMSLogByAssetId/{0}", assetId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadRFStatus()
        {
            var mRFStatus = TempData.Peek("RFStatus") as List<RFStatus>;

            if (mRFStatus != null && mRFStatus != null && mRFStatus.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3},{4}", "SiteName", "DeviceName", "A10Id", "LastAccesstime", "TimeStamp");
                csv.AppendLine(socsvstring);

                foreach (var rfStatus in mRFStatus)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4}", rfStatus.SiteName, rfStatus.DeviceName.RemoveComma(), rfStatus.A10Id.RemoveComma(), rfStatus.LastAccesstime.RemoveComma(), rfStatus.TimeStamp.ToString());
                    csv.AppendLine(socsvstring);

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=RFStatus" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }

            return View("Index");
        }

        public ActionResult DownloadSiteStatus()
        {
            var mSiteStatus = TempData.Peek("SiteStatus") as List<SiteStatus>;

            if (mSiteStatus != null && mSiteStatus != null && mSiteStatus.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3},{4}", "SiteName", "SiteId", "Channel", "Mac", "TimeStamp");
                csv.AppendLine(socsvstring);

                foreach (var siteStatus in mSiteStatus)
                {
                    socsvstring = string.Format("{0},{1},{2},{3},{4}", siteStatus.SiteName, siteStatus.SiteId, siteStatus.Channel.RemoveComma(), siteStatus.Mac.RemoveComma(), siteStatus.TimeStamp.ToString());
                    csv.AppendLine(socsvstring);

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=SiteStatus" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }

            return View("Index");
        }

        public ActionResult DownloadIndexAlertLog()
        {
            var mIndexAlertLogs = TempData.Peek("IndexAlertLogs") as List<IndexAlertLog>;

            if (mIndexAlertLogs != null && mIndexAlertLogs != null && mIndexAlertLogs.Count > 0)
            {
                var csv = new StringBuilder();
                var socsvstring = string.Empty;
                socsvstring = string.Format("{0},{1},{2},{3}", "Site Name", "Asset Name", "Remarks", "TimeStamp");
                csv.AppendLine(socsvstring);

                foreach (var indexAlertLog in mIndexAlertLogs)
                {
                    socsvstring = string.Format("{0},{1},{2},{3}", indexAlertLog.SiteName, indexAlertLog.AssetName, indexAlertLog.Remarks.RemoveComma(), indexAlertLog.TimeStamp.ToString());
                    csv.AppendLine(socsvstring);

                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=IndexLog" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }

            return View("Index");
        }

        public ActionResult SaveAppKey(AppKey mAppKey)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAppKey);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AppKey"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                    }

                }
            }
            catch (Exception)
            {
            }

            return Json("");

        }

        #region Probability

        public ActionResult _DownloadProbability()
        {
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");           
            return PartialView();
        }

        public ActionResult DownloadProbability(int siteId, string startDate, string endDate)
        {
            var csv = new StringBuilder();
            var socsvstring = string.Format("{0},{1},{2},{3},{4},{5},{6}", "Date", "Glude", "Prone To Shorting", "Earth Fault", "Axle Counter", "Leakage", "Energization");

            if (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsTestUser != null && E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsTestUser.Value)
            {
                socsvstring += string.Format(",{0},{1},{2}", "Signal Moment", "TPR", "Point Indication");
            }
            csv.AppendLine(socsvstring);

            try
            {
                SMSLogLister mSMSLogLister = new SMSLogLister();
                mSMSLogLister.SearchCriteria.SiteId = siteId;
                mSMSLogLister.SearchCriteria.FromDate = Convert.ToDateTime(startDate);
                mSMSLogLister.SearchCriteria.ToDate = Convert.ToDateTime(endDate);
                var mEarthFault = siteKeepingService.EarthFaultLister(mSMSLogLister);

                for (DateTime dateTime = Convert.ToDateTime(startDate); dateTime <= Convert.ToDateTime(endDate); dateTime += TimeSpan.FromDays(1))
                {
                    socsvstring = $"{dateTime.ToShortDateString()},";
                    var mFamilyTracksCount = new List<int>();

                    //Glude
                    string gluedStr = string.Empty;

                    var mGluedLogDataLister = new GluedLogDataLister();
                    mGluedLogDataLister.SearchCriteria.SiteId = siteId;
                    mGluedLogDataLister.SearchCriteria.TimeStamp = dateTime;
                    mGluedLogDataLister = siteKeepingService.GluedLogDataLister(mGluedLogDataLister);
                    if (mGluedLogDataLister != null && mGluedLogDataLister.GluedLogDatas != null && mGluedLogDataLister.GluedLogDatas.Count > 0)
                    {
                        var mainTrack = mGluedLogDataLister.GluedLogDatas.GroupBy(x => new { x.MainTrack, x.MainTrackId });
                        if (mainTrack != null)
                        {
                            foreach (var assetId in mainTrack)
                            {
                                var mGluedLogDatas = mGluedLogDataLister.GluedLogDatas.Where(x => x.MainTrackId == assetId.Key.MainTrackId && x.LokedRecord == "1").ToList();
                                if (mGluedLogDatas != null && mGluedLogDatas.Count > 0)
                                {
                                    mFamilyTracksCount = mGluedLogDatas.GroupBy(x => x.FamilyTrack).Select(lg => lg.Count()).ToList();
                                }

                                var lokedRecordCount = mGluedLogDataLister.GluedLogDatas.Where(x => x.MainTrackId == assetId.Key.MainTrackId && x.LokedRecord == "1").Count();
                                if (mFamilyTracksCount != null && mFamilyTracksCount.Count > 0)
                                {
                                    var name = $"{assetId.Key.MainTrack}({string.Join("/", mFamilyTracksCount)})";
                                    gluedStr += $"{name}, ";
                                }
                                else
                                {
                                    var name = $"{assetId.Key.MainTrack}({lokedRecordCount})";
                                    gluedStr += $"{name}, ";
                                }
                            }
                        }
                    }
                    if (gluedStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + gluedStr.Substring(0, gluedStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";


                    //TrainRunningMoment
                    string trainRunningMomentStr = string.Empty;
                    TrainRunningMomentLister mTrainRunningMomentLister = new TrainRunningMomentLister();
                    mTrainRunningMomentLister.SearchCriteria.SiteId = siteId;
                    mTrainRunningMomentLister.SearchCriteria.TimeStamp = dateTime;
                    mTrainRunningMomentLister = siteKeepingService.TrainRunningMoment(mTrainRunningMomentLister);
                    if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.TrainRunningMoments != null)
                    {
                        var mainTrack = mTrainRunningMomentLister.TrainRunningMoments.GroupBy(x => new { x.AssetName, x.AssetId });
                        if (mainTrack != null)
                        {
                            foreach (var assetId in mainTrack)
                            {
                                trainRunningMomentStr += $"{assetId.Key.AssetName}, ";
                            }
                        }
                    }
                    if (trainRunningMomentStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + trainRunningMomentStr.Substring(0, trainRunningMomentStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";

                    //EarthFault
                    string earthFaultStr = string.Empty;
                    if (mEarthFault != null && mEarthFault.mSMSLogs != null && mEarthFault.mSMSLogs.Count > 0)
                    {
                        var mAssets = GetEarthFaultAsset(mEarthFault.mSMSLogs.Where(x => x.TimeStamp.Date == dateTime.Date).ToList(), siteId);
                        if (mAssets != null && mAssets.Count > 0)
                        {
                            foreach (var asset in mAssets)
                            {
                                earthFaultStr += $"{asset.Name}, ";
                            }

                        }
                    }

                    if (earthFaultStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + earthFaultStr.Substring(0, earthFaultStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";


                    //AxleCounter
                    string axleCounterStr = string.Empty;
                    WatchListLister mWatchListLister = new WatchListLister();
                    mWatchListLister.SearchCriteria.SiteId = siteId;
                    mWatchListLister.SearchCriteria.CreatedDate = dateTime;
                    mWatchListLister = siteKeepingService.AxleCounterLister(mWatchListLister);
                    if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
                    {
                        var assetWiseLogs = mWatchListLister.WatchLists.GroupBy(x => x.AssetName).ToList();
                        List<string> blocks = new List<string>();
                        foreach (var logs in assetWiseLogs)
                        {
                            var attributeNames = logs.Select(x => x.AttributeName).ToList();
                            if (attributeNames != null && attributeNames.Count > 0)
                            {
                                string str = "" + logs.Key + "(" + string.Join(",", attributeNames) + ")";
                                blocks.Add(str);
                                axleCounterStr += $"{str}, ";
                            }
                        }
                    }
                    if (axleCounterStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + axleCounterStr.Substring(0, axleCounterStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";

                    //IndexAlertLog Leakage
                    string leakageStr = string.Empty;
                    var mIndexAlertLogLister = new IndexAlertLogLister();
                    mIndexAlertLogLister.SearchCriteria.SiteId = siteId;
                    mIndexAlertLogLister.SearchCriteria.TimeStamp = dateTime;
                    mIndexAlertLogLister.SearchCriteria.Remarks = "Leakage";
                    mIndexAlertLogLister = siteKeepingService.IndexAlertLogLister(mIndexAlertLogLister);
                    if (mIndexAlertLogLister != null && mIndexAlertLogLister.IndexAlertLogs != null && mIndexAlertLogLister.IndexAlertLogs.Count > 0)
                    {
                        var mainTrack = mIndexAlertLogLister.IndexAlertLogs.GroupBy(x => new { x.AssetName, x.AssetId });
                        if (mainTrack != null)
                        {
                            foreach (var assetId in mainTrack)
                            {
                                var assetTypeId = mIndexAlertLogLister.IndexAlertLogs.Where(x => x.AssetId == assetId.Key.AssetId).Select(x => x.AssetTypeId).FirstOrDefault();
                                leakageStr += $"{assetId.Key.AssetName}, ";

                            }
                        }
                    }
                    if (leakageStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + leakageStr.Substring(0, leakageStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";

                    //IndexAlertLog Energization
                    string energizationStr = string.Empty;
                    var mEnergizationLister = new IndexAlertLogLister();
                    mEnergizationLister.SearchCriteria.SiteId = siteId;
                    mEnergizationLister.SearchCriteria.TimeStamp = dateTime;
                    mEnergizationLister.SearchCriteria.Remarks = "Energization";
                    mEnergizationLister = siteKeepingService.IndexAlertLogLister(mEnergizationLister);
                    if (mEnergizationLister != null && mEnergizationLister.IndexAlertLogs != null && mEnergizationLister.IndexAlertLogs.Count > 0)
                    {
                        var mainTrack = mEnergizationLister.IndexAlertLogs.GroupBy(x => new { x.AssetName, x.AssetId });
                        if (mainTrack != null)
                        {
                            foreach (var assetId in mainTrack)
                            {
                                var assetTypeId = mEnergizationLister.IndexAlertLogs.Where(x => x.AssetId == assetId.Key.AssetId).Select(x => x.AssetTypeId).FirstOrDefault();
                                energizationStr += $"{assetId.Key.AssetName}, ";

                            }
                        }
                    }
                    if (energizationStr.IsNotNullOrEmpty())
                        socsvstring += $"{"\"" + energizationStr.Substring(0, energizationStr.Length - 2) + "\""},";
                    else
                        socsvstring += $",";


                    if (E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsTestUser != null && E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsTestUser.Value)
                    {
                        //Signal Energization
                        string signalStr = string.Empty;
                        var mSignalMomentLister = new MomentLister();
                        mSignalMomentLister.SearchCriteria.SiteId = siteId;
                        mSignalMomentLister.SearchCriteria.TimeStamp = dateTime;
                        mSignalMomentLister = siteKeepingService.SignalMomentLister(mSignalMomentLister);
                        if (mSignalMomentLister != null && mSignalMomentLister.mMoments != null && mSignalMomentLister.mMoments.Count > 0)
                        {
                            var mainTrack = mSignalMomentLister.mMoments.GroupBy(x => new { x.AssetName, x.AssetId });
                            if (mainTrack != null)
                            {
                                foreach (var assetId in mainTrack)
                                {
                                    signalStr += $"{assetId.Key.AssetName}, ";

                                }
                            }
                        }
                        if (signalStr.IsNotNullOrEmpty())
                            socsvstring += $"{"\"" + signalStr.Substring(0, signalStr.Length - 2) + "\""},";
                        else
                            socsvstring += $",";


                        //TPR Energization
                        string tprStr = string.Empty;
                        var mTPRMomentLister = new MomentLister();
                        mTPRMomentLister.SearchCriteria.SiteId = siteId;
                        mTPRMomentLister.SearchCriteria.TimeStamp = dateTime;
                        mTPRMomentLister = siteKeepingService.TPRMomentLister(mTPRMomentLister);
                        if (mTPRMomentLister != null && mTPRMomentLister.mMoments != null && mTPRMomentLister.mMoments.Count > 0)
                        {
                            var mainTrack = mTPRMomentLister.mMoments.GroupBy(x => new { x.AssetName, x.AssetId });
                            if (mainTrack != null)
                            {
                                foreach (var assetId in mainTrack)
                                {
                                    tprStr += $"{assetId.Key.AssetName}, ";

                                }
                            }
                        }
                        if (tprStr.IsNotNullOrEmpty())
                            socsvstring += $"{"\"" + tprStr.Substring(0, tprStr.Length - 2) + "\""},";
                        else
                            socsvstring += $",";


                        //Point Machine
                        string pmStr = string.Empty;
                        var mPMLister = new MomentLister();
                        mPMLister.SearchCriteria.SiteId = siteId;
                        mPMLister.SearchCriteria.TimeStamp = dateTime;
                        mPMLister = siteKeepingService.PointIndicationLister(mPMLister);
                        if (mPMLister != null && mPMLister.mMoments != null && mPMLister.mMoments.Count > 0)
                        {
                            var mainTrack = mPMLister.mMoments.GroupBy(x => new { x.AssetName, x.AssetId });
                            if (mainTrack != null)
                            {
                                foreach (var assetId in mainTrack)
                                {
                                    pmStr += $"{assetId.Key.AssetName}, ";

                                }
                            }
                        }
                        if (pmStr.IsNotNullOrEmpty())
                            socsvstring += $"{"\"" + pmStr.Substring(0, pmStr.Length - 2) + "\""},";
                        else
                            socsvstring += $",";
                    }



                    csv.AppendLine(socsvstring);
                }

                Response.Clear();
                Response.Buffer = true;
                Response.AddHeader("content-disposition", "attachment;filename=Probability" + DateTime.Now.Ticks + ".csv");
                Response.Charset = "utf-8";
                Response.ContentType = "text/csv";
                Response.Output.Write(csv);
                Response.Flush();
                Response.End();
            }
            catch (Exception)
            {

            }


            return View("Index");
        }

        public List<Asset> GetEarthFaultAsset(List<SMSLog> mSMSLogs, int siteId)
        {
            List<Asset> mEarthFaults = new List<Asset>();
            if (mSMSLogs != null && mSMSLogs.Count > 0)
            {
                foreach (var mSMSLog in mSMSLogs)
                {
                    if (mSMSLog != null && mSMSLog.Message.IsNotNullOrEmpty())
                    {
                        var msg = GetAssetAndAttribute(mSMSLog.Message);
                        if (msg.IsNotNullOrEmpty())
                        {
                            var spitMsg = msg.Split(' ');
                            if (spitMsg != null && spitMsg.Count() > 1)
                            {
                                string attributeName = spitMsg.LastOrDefault().Trim();
                                if (attributeName.IsNotNullOrEmpty())
                                {
                                    string assetName = msg.Replace(attributeName, "").Trim();
                                    string name = $"{assetName} ({attributeName})";

                                    if (mEarthFaults != null && mEarthFaults.Where(x => x.Name == name && x.SiteId == siteId).Count() == 0)
                                        mEarthFaults.Add(new Asset() { Name = name, SiteId = siteId });
                                }
                            }
                        }
                    }
                }
            }
            return mEarthFaults;
        }

        private string GetAssetAndAttribute(string msg)
        {
            try
            {
                var index = msg.IndexOf("detected on");
                var f1 = msg.Substring(0, index);
                msg = msg.Replace(f1, "");

                var index1 = msg.IndexOf("\n");
                var f2 = msg.Substring(index1);
                msg = msg.Replace(f2, "");

                msg = msg.Replace("detected on", "").Trim();
            }
            catch (Exception ex)
            {
            }
            return msg;
        }

        #endregion

        #region Railway Observation Tally
        public ActionResult RailwayObservationTally()
        {
            DivisionReportModelLister mDivisionReportModelLister = new DivisionReportModelLister();
            mDivisionReportModelLister.SearchCriteria.StartDate = DateTime.Now;
            mDivisionReportModelLister.SearchCriteria.EndDate = DateTime.Now;
            return View(mDivisionReportModelLister);
        }

        public ActionResult _RailwayObservationTally(DivisionReportModelLister mDivisionReportModelLister)
        {
            mDivisionReportModelLister.Pager.Take = mDivisionReportModelLister.Pager.PageSize;
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
                ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
                ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            }
            return PartialView(mDivisionReportModelLister);
        }

        public ActionResult DownloadRailwayObservationTally(int divisionId, string startDate, string endDate)
        {
            DivisionReportModelLister mDivisionReportModelLister = new DivisionReportModelLister();
            mDivisionReportModelLister.SearchCriteria.DivisionId = divisionId;
            mDivisionReportModelLister.SearchCriteria.StartDate = ExtensionMethod.ConvertToDateTimeFormat(startDate, "M/d/yyyy");
            mDivisionReportModelLister.SearchCriteria.EndDate = ExtensionMethod.ConvertToDateTimeFormat(endDate, "M/d/yyyy");
            mDivisionReportModelLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionReportModelLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteKeeping/DownloadDivisionReportModel"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                        return File(csvbytes, "application/pdf", $"RailwayObservationTally.pdf");
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
            return PartialView(mDivisionReportModelLister);
        }

        public ActionResult UpdateRailwayObservationTally(DivisionReportModel mDivisionReportModel)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mDivisionReportModel.UpdatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mDivisionReportModel);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("SiteKeeping/UpdateDivisionReportModel"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Remark has been updated." };
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


        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = siteService.GetBy(divisionId);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }
    }
}