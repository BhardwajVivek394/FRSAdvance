using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Mvc;
using E7FRSAdvance.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class MaintenanceTabulationController : Controller
    {
        // GET: MaintenanceTabulation
        public ActionResult Index()
        {
            SiteLister mSiteLister = new SiteLister();
            mSiteLister.Pager.Take = -1;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetAllSite"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
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
            GetAllZones();
            GetAllDivisions();
            return View(mSiteLister);
        }

        public ActionResult _RFStatus(RFStatusLister mRFStatusLister)
        {
            mRFStatusLister.Pager.Take = -1;
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
                    }
                }
            }
            catch (Exception)
            {

            }
            return Json(mRFStatusLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _SiteStatus(SiteStatusLister mSiteStatusLister)
        {
            mSiteStatusLister.Pager.Take = -1;

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
                    }
                }
            }
            catch (Exception)
            {
            }
            return Json(mSiteStatusLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _WatchList(WatchListLister mWatchListLister)
        {
            mWatchListLister.Pager.Take = -1;

            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.CreatedDate != null && mWatchListLister.SearchCriteria.CreatedDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.CreatedDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("WatchList/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);

                    }

                }
            }
            catch (Exception)
            {
            }
            return Json(mWatchListLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _GluedLogDataLister(GluedLogDataLister mGluedLogDataLister)
        {

            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null && mGluedLogDataLister.SearchCriteria.TimeStamp != null && mGluedLogDataLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mGluedLogDataLister.SearchCriteria.TimeStamp = DateTime.Now;

            mGluedLogDataLister.Pager.Take = -1;
            if (mGluedLogDataLister != null && mGluedLogDataLister.SearchCriteria != null)
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
                            if (mGluedLogDataLister != null && mGluedLogDataLister.GluedLogDatas != null && mGluedLogDataLister.GluedLogDatas.Count > 0 && mGluedLogDataLister.Assets != null && mGluedLogDataLister.Assets.Count > 0)
                            {
                                mGluedLogDataLister.Assets.ForEach(x =>
                                {

                                    x.SiteId = mGluedLogDataLister.GluedLogDatas.Where(y => y.MainTrackId == x.Id).Select(z => z.SiteId).FirstOrDefault();

                                });
                                //foreach (var asset in mGluedLogDataLister.Assets)
                                //{
                                //    asset
                                //}
                            }
                        }

                    }
                }
                catch (Exception)
                {
                }
            }

            return Json(mGluedLogDataLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _PointMachineSignatureLister(PMDataTableLister mPMDataTableLister)
        {
            if (mPMDataTableLister != null && mPMDataTableLister.SearchCriteria != null && mPMDataTableLister.SearchCriteria.TimeStamp != null && mPMDataTableLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mPMDataTableLister.SearchCriteria.TimeStamp = DateTime.Now;

            mPMDataTableLister.Pager.Take = -1;
            if (mPMDataTableLister != null && mPMDataTableLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mPMDataTableLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetAllPointMachineSignatureLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mPMDataTableLister = JsonConvert.DeserializeObject<PMDataTableLister>(jsonString);
                            if (mPMDataTableLister != null && mPMDataTableLister.PMDataTables != null && mPMDataTableLister.PMDataTables.Count > 0 && mPMDataTableLister.Assets != null && mPMDataTableLister.Assets.Count > 0)
                            {
                                mPMDataTableLister.Assets.ForEach(x =>
                                {

                                    x.SiteId = mPMDataTableLister.PMDataTables.Where(y => y.AssetId == x.Id).Select(z => z.SiteId).FirstOrDefault();

                                });
                            }
                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return Json(mPMDataTableLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _IndexAlertLogLister(IndexAlertLogLister mIndexAlertLogLister)
        {
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null && mIndexAlertLogLister.SearchCriteria.TimeStamp != null && mIndexAlertLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mIndexAlertLogLister.SearchCriteria.TimeStamp = DateTime.Now;

            mIndexAlertLogLister.Pager.Take = -1;
            if (mIndexAlertLogLister != null && mIndexAlertLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mIndexAlertLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetAllIndexAlertLogLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mIndexAlertLogLister = JsonConvert.DeserializeObject<IndexAlertLogLister>(jsonString);

                        }
                    }
                }
                catch (Exception)
                {
                }
            }

            return Json(mIndexAlertLogLister, JsonRequestBehavior.AllowGet);
        }

        #region Probebility

        public ActionResult _TrainRunningMoment(TrainRunningMomentLister mTrainRunningMomentLister)
        {
            mTrainRunningMomentLister.Pager.Take = mTrainRunningMomentLister.Pager.PageSize;

            if (mTrainRunningMomentLister != null && mTrainRunningMomentLister.SearchCriteria != null && mTrainRunningMomentLister.SearchCriteria.TimeStamp != null && mTrainRunningMomentLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mTrainRunningMomentLister.SearchCriteria.TimeStamp = DateTime.Now;

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
            return PartialView(mTrainRunningMomentLister);
        }

        public ActionResult _EarthFaultLister(SMSLogLister mSMSLogLister)
        {
            List<Asset> mEarthFaults = new List<Asset>();
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null && mSMSLogLister.SearchCriteria.TimeStamp != null && mSMSLogLister.SearchCriteria.TimeStamp == DateTime.MinValue)
                mSMSLogLister.SearchCriteria.TimeStamp = DateTime.Now;

            mSMSLogLister.SearchCriteria.AssetTypeId = (int)E7FRSAdvance.Utility.Utility.AssetType.Earth_Fault;
            mSMSLogLister.SearchCriteria.IsSmsLogActive = true;
            mSMSLogLister.Pager.Take = -1;
            if (mSMSLogLister != null && mSMSLogLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mSMSLogLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SMSLog/GetAllSMSLogLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mSMSLogLister = JsonConvert.DeserializeObject<SMSLogLister>(jsonString);
                            if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                            {
                                var mSMSLogs = mSMSLogLister.mSMSLogs;
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

                                                    if (mEarthFaults != null && mEarthFaults.Where(x => x.Name == name && x.SiteId == mSMSLogLister.SearchCriteria.SiteId).Count() == 0)
                                                        mEarthFaults.Add(new Asset() { Name = name, SiteId = mSMSLogLister.SearchCriteria.SiteId });
                                                }
                                            }
                                        }
                                    }
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
            }

            return PartialView(mEarthFaults);
        }

        public ActionResult _AxleCounterLister(WatchListLister mWatchListLister)
        {
            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null && mWatchListLister.SearchCriteria.CreatedDate != null && mWatchListLister.SearchCriteria.CreatedDate == DateTime.MinValue)
                mWatchListLister.SearchCriteria.CreatedDate = DateTime.Now;

            mWatchListLister.Pager.Take = -1;
            if (mWatchListLister != null && mWatchListLister.SearchCriteria != null)
            {
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mWatchListLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("SiteKeeping/GetAxleCounterIssuesLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mWatchListLister = JsonConvert.DeserializeObject<WatchListLister>(jsonString);

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

            return PartialView(mWatchListLister);
        }

        #endregion

        public void GetAllZones()
        {
            List<Zone> mZones = new List<Zone>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Zone/GetAllZones")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mZones = JsonConvert.DeserializeObject<List<Zone>>(jsonString);
                        mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
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

        public void GetAllDivisions()
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

    }
}