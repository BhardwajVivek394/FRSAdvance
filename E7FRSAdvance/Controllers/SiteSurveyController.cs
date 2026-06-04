using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class SiteSurveyController : Controller
    {
        // GET: SiteSurvey

        private readonly ISiteService siteService;
        private readonly IAssetTypeService assetTypeService;
        string[] mainSignal = { "HHG", "HG", "DG", "RG" };
        string[] shuntSignal = { "RG", "DG", "PG" };
        string[] rootSignal = { "UG", "UGA", "UGB", "UGC", "UGD", "ASin" };
        string[] callingSignal = { "HG" };
        public SiteSurveyController(ISiteService siteService, IAssetTypeService assetTypeService)
        {
            this.siteService = siteService;
            this.assetTypeService = assetTypeService;
        }

        public ActionResult Index(int siteId)
        {
            Domain.SiteSurveyLister mSiteSurveyLister = new SiteSurveyLister();
            var mSite = siteService.Get(siteId);
            if (mSite != null && mSite.Id > 0)
            {
                mSiteSurveyLister.SearchCriteria.SiteId = mSite.Id;
                mSiteSurveyLister.SearchCriteria.SiteName = mSite.Name;
            }
            return View(mSiteSurveyLister);
        }

        public PartialViewResult List(Domain.SiteSurveyLister mSiteSurveyLister)
        {
            mSiteSurveyLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteSurveyLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteSurvey/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteSurveyLister = JsonConvert.DeserializeObject<SiteSurveyLister>(jsonString);

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
                ViewBag.SurveyAttributes = Get(mSiteSurveyLister.SearchCriteria.SiteId);
            }
            return PartialView(mSiteSurveyLister);
        }

        [HttpPost]
        public ActionResult Save(SiteSurvey mSiteSurvey)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mSiteSurvey.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteSurvey);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteSurvey"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteSurvey = JsonConvert.DeserializeObject<SiteSurvey>(jsonString);
                        if (mSiteSurvey != null && mSiteSurvey.Id > 0)
                        {
                            data = new { type = "success", result = "Record has been Saved." };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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

        public ActionResult Delete(int id)
        {
            dynamic data = new ExpandoObject();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"SiteSurvey/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var isDeleted = JsonConvert.DeserializeObject<bool>(jsonString);
                        if (isDeleted)
                            data = new { type = "success", result = "Record has been deleted." };
                        else
                            data = new { type = "error", result = "Internal server error." };
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
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _PointPartial(int siteId)
        {
            List<Domain.SiteSurvey> mSiteSurvey = new List<Domain.SiteSurvey>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("SiteSurvey/SiteId/{0}", siteId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteSurvey = JsonConvert.DeserializeObject<List<Domain.SiteSurvey>>(jsonString);
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
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                ViewBag.SurveyPointMachine = GetSurveyPointMachine(siteId);
            }

            return PartialView(mSiteSurvey);
        }

        [HttpPost]
        public ActionResult SavePointPartial(List<Domain.SurveyPointMachine> mSurveyPointMachines)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSurveyPointMachines);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteSurvey/CreatePM"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Record has been Saved." };
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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

        public ActionResult _Finalize(int siteId)
        {
            Domain.SiteSurveyLister mSiteSurveyLister = new Domain.SiteSurveyLister();
            mSiteSurveyLister.SearchCriteria.SiteId = siteId;
            mSiteSurveyLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteSurveyLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteSurvey/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteSurveyLister = JsonConvert.DeserializeObject<SiteSurveyLister>(jsonString);
                        if (mSiteSurveyLister != null && mSiteSurveyLister.SiteSurveys != null && mSiteSurveyLister.SiteSurveys.Count > 0)
                        {
                            var trAssets = new List<string>();
                            var trSiteSurvey = mSiteSurveyLister.SiteSurveys.Where(x => x.TR != null && x.TR.IsNotNullOrEmpty()).Select(x => x.TR).ToList();
                            if (trSiteSurvey != null && trSiteSurvey.Count > 0)
                            {
                                trSiteSurvey.ForEach(x =>
                                {
                                    var splitstr = x.Split(',');
                                    if (splitstr != null && splitstr.Count() > 0)
                                    {
                                        trAssets.AddRange(splitstr);
                                    }
                                });
                            }

                            var tfAssets = new List<string>();
                            var tfSiteSurvey = mSiteSurveyLister.SiteSurveys.Where(x => x.TF != null && x.TF.IsNotNullOrEmpty()).Select(x => x.TF).ToList();
                            if (tfSiteSurvey != null && tfSiteSurvey.Count > 0)
                            {
                                tfSiteSurvey.ForEach(x =>
                                {
                                    var splitstr = x.Split(',');
                                    if (splitstr != null && splitstr.Count() > 0)
                                    {
                                        tfAssets.AddRange(splitstr);
                                    }
                                });
                            }

                            if (trAssets != null && trAssets.Count > 0 && tfAssets != null && tfAssets.Count > 0)
                            {
                                var trNotCompareAssets = trAssets.Except(tfAssets).ToList();
                                var tfNotCompareAssets = tfAssets.Except(trAssets).ToList();

                                if ((trNotCompareAssets != null && trNotCompareAssets.Count > 0) || (tfNotCompareAssets != null && tfNotCompareAssets.Count > 0))
                                {
                                    ViewBag.TRAssets = trNotCompareAssets;
                                    ViewBag.TFAssets = tfNotCompareAssets;
                                    return PartialView("_NotFinalize", mSiteSurveyLister);
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
            finally
            {
                ViewBag.SurveyAttributes = Get(mSiteSurveyLister.SearchCriteria.SiteId);
            }

            return PartialView(mSiteSurveyLister);
        }

        public ActionResult _AssetAttribute(int siteId)
        {
            List<Domain.SurveyAttribute> mSiteSurvey = new List<Domain.SurveyAttribute>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteSurvey/GetAttribute/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteSurvey = JsonConvert.DeserializeObject<List<Domain.SurveyAttribute>>(jsonString);

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
                ViewBag.Message = "Internal server error!";
            }
            finally
            {
                ViewBag.AssetTypes = GetSiteSurveyAssetType();
            }
            return PartialView(mSiteSurvey);
        }

        [HttpPost]
        public ActionResult SaveSurveyInfo(List<Domain.SurveyInfo> mSurveyInfos)
        {
            dynamic data = new ExpandoObject();
            if (mSurveyInfos != null)
            {
                foreach (var mSurveyInfo in mSurveyInfos)
                {
                    mSurveyInfo.CreatedBy = ClsHttpContent.LoginUser.Id;

                }
            }
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSurveyInfos);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SurveyInfo"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Record has been Saved." };
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


        public ActionResult _ClusterStatus(int siteId, int cluster)
        {
            List<Domain.SiteSurveyClusterStatus> mSiteSurveyClusterStatus = new List<Domain.SiteSurveyClusterStatus>();
            ViewBag.Cluster = cluster;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteSurvey/GetClusterStatus/SiteId/{siteId}/Cluster/{cluster}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var siteSurveyClusterStatus = JsonConvert.DeserializeObject<List<Domain.SiteSurveyClusterStatus>>(jsonString);

                        mSiteSurveyClusterStatus = (from E7FRSAdvance.Utility.Utility.SurveyClusterStatus e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.SurveyClusterStatus))
                                                    select new Domain.SiteSurveyClusterStatus
                                                    {
                                                        StatusTypeId = (int)e,
                                                        Name = e.ToString().Replace("_", " ")
                                                    }).ToList();

                        if (siteSurveyClusterStatus != null && siteSurveyClusterStatus.Count > 0 && mSiteSurveyClusterStatus != null && mSiteSurveyClusterStatus.Count > 0)
                        {
                            foreach (var csStatus in mSiteSurveyClusterStatus)
                            {
                                if (siteSurveyClusterStatus.Where(x => x.StatusTypeId == csStatus.StatusTypeId && x.IsChecked).Count() > 0)
                                {
                                    csStatus.IsChecked = true;
                                }
                                if (csStatus.StatusTypeId == (int)E7FRSAdvance.Utility.Utility.SurveyClusterStatus.Termination)
                                {
                                    var surveyClusterStatus = siteSurveyClusterStatus.Where(x => x.StatusTypeId == csStatus.StatusTypeId).FirstOrDefault();
                                    if (surveyClusterStatus != null && surveyClusterStatus.Id > 0)
                                    {
                                        csStatus.TerminationSignalCount = surveyClusterStatus.TerminationSignalCount;
                                        csStatus.TerminationTrackCount = surveyClusterStatus.TerminationTrackCount;
                                        csStatus.TerminationPointCount = surveyClusterStatus.TerminationPointCount;
                                    }

                                }

                            }
                        }


                        var mSiteSurveys = GetSiteSurvey(siteId, cluster);
                        if (mSiteSurveys != null && mSiteSurveys.Count > 0)
                        {
                            if (mSiteSurveys.Count == 1)
                            {
                                mSiteSurveyClusterStatus.RemoveAll(x => x.StatusTypeId == (int)E7FRSAdvance.Utility.Utility.SurveyClusterStatus.Trenching);
                            }

                            if (mSiteSurveys.Where(x => x.PointMachine != null && x.PointMachine != string.Empty).Count() <= 0)
                            {
                                mSiteSurveyClusterStatus.RemoveAll(x => x.StatusTypeId == (int)E7FRSAdvance.Utility.Utility.SurveyClusterStatus.Point_Machine_Test);
                            }
                            else
                            {
                                int pointCount = 0;
                                foreach (var item in mSiteSurveys.Where(x => x.PointMachine != null && x.PointMachine != string.Empty).Select(x => x.PointMachine))
                                {
                                    var splittrstr = item.Split(',');
                                    if (splittrstr != null && splittrstr.Count() > 0)
                                    {
                                        pointCount = pointCount + splittrstr.Count();
                                    }
                                }
                                ViewBag.PointCount = pointCount;
                            }

                            if (mSiteSurveys.Where(x => x.Signal != null && x.Signal != string.Empty).Count() <= 0)
                            {
                                mSiteSurveyClusterStatus.RemoveAll(x => x.StatusTypeId == (int)E7FRSAdvance.Utility.Utility.SurveyClusterStatus.Pannel_Testing_Signal);
                            }
                            else
                            {
                                int signalCount = 0;
                                var tAssets = new List<string>();
                                foreach (var item in mSiteSurveys.Where(x => x.Signal != null && x.Signal != string.Empty).Select(x => x.Signal))
                                {
                                    var splitSignal = item.Split(',');
                                    if (splitSignal != null && splitSignal.Count() > 0)
                                    {

                                        splitSignal = splitSignal.Where(x => x != null && x != string.Empty).ToArray();
                                        if (splitSignal != null && splitSignal.Count() > 0)
                                        {
                                            foreach (var sp in splitSignal)
                                            {
                                                var splitsignalattr = sp.Split(' ');
                                                if (splitsignalattr != null && splitsignalattr.FirstOrDefault() != string.Empty && splitsignalattr.Count() > 0 && tAssets.Where(x => x.Trim().ToLower() == splitsignalattr.FirstOrDefault().Trim().ToLower()).Count() <= 0)
                                                {
                                                    signalCount++;
                                                    tAssets.Add(splitsignalattr.FirstOrDefault());
                                                }

                                            }
                                        }


                                    }
                                }
                                ViewBag.SignalCount = signalCount;
                            }

                            if (mSiteSurveys.Where(x => (x.TF != null && x.TF != string.Empty) || (x.TR != null && x.TR != string.Empty)).Count() <= 0)
                            {
                                mSiteSurveyClusterStatus.RemoveAll(x => x.StatusTypeId == (int)E7FRSAdvance.Utility.Utility.SurveyClusterStatus.Pannel_Testing_Track);
                            }
                            else
                            {
                                int trackCount = 0;
                                foreach (var item in mSiteSurveys.Where(x => x.TR != null && x.TR != string.Empty).Select(x => x.TR))
                                {
                                    var splittrstr = item.Split(',');
                                    if (splittrstr != null && splittrstr.Count() > 0)
                                    {
                                        trackCount = trackCount + splittrstr.Count();
                                    }
                                }
                                foreach (var item in mSiteSurveys.Where(x => x.TF != null && x.TF != string.Empty).Select(x => x.TF))
                                {
                                    var splittrstr = item.Split(',');
                                    if (splittrstr != null && splittrstr.Count() > 0)
                                    {
                                        trackCount = trackCount + splittrstr.Count();
                                    }
                                }
                                ViewBag.TrackCount = trackCount;
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
                ViewBag.Message = "Internal server error!";
            }

            return PartialView(mSiteSurveyClusterStatus);
        }

        [HttpPost]
        public ActionResult SaveClusterStatus(List<Domain.SiteSurveyClusterStatus> mSiteSurveyClusterStatus)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteSurveyClusterStatus);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteSurvey/CreateClusterStatus"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Record has been Saved." };
                    }
                    else if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        data = new { type = "error", result = JsonConvert.DeserializeObject<string>(jsonString) };
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

        #region Private Method

        private List<SurveyAttribute> Get(int siteId)
        {
            List<Domain.SurveyAttribute> mSiteSurvey = new List<Domain.SurveyAttribute>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteSurvey/GetAttribute/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteSurvey = JsonConvert.DeserializeObject<List<Domain.SurveyAttribute>>(jsonString);
                        if (mSiteSurvey != null && mSiteSurvey.Count > 0)
                            mSiteSurvey = mSiteSurvey.Where(x => x.IsChecked).ToList();

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSiteSurvey;
        }

        private List<Domain.AssetType> GetSiteSurveyAssetType()
        {

            return (from E7FRSAdvance.Utility.Utility.SiteSurveyAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.SiteSurveyAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
            //ViewBag.StatusEnumList = new SelectList(enumData, "Id", "Name");

            // return new SelectList(enumData, "Id", "Name");
        }

        private List<SurveyPointMachine> GetSurveyPointMachine(int siteId)
        {
            List<Domain.SurveyPointMachine> mSurveyPointMachine = new List<Domain.SurveyPointMachine>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteSurvey/GetPM/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSurveyPointMachine = JsonConvert.DeserializeObject<List<Domain.SurveyPointMachine>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSurveyPointMachine;
        }

        private List<SiteSurvey> GetSiteSurvey(int siteId, int cluster)
        {
            List<Domain.SiteSurvey> mSiteSurveys = new List<Domain.SiteSurvey>();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteSurvey/SiteId/{siteId}/Cluster/{cluster}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteSurveys = JsonConvert.DeserializeObject<List<Domain.SiteSurvey>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSiteSurveys;
        }

        #endregion
    }
}