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
using System.Security.Policy;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Utility.Authorization]
    public class JobCardController : Controller
    {
        // GET: JobCard
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        private readonly IUserService userService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetService assetService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IFRSAlertService alertService;
        public JobCardController(IDivisionService divisionService, ISiteService siteService, IUserService userService, IAssetTypeService assetTypeService, IAssetService assetService, IAssetAttributeService assetAttributeService, IFRSAlertService alertService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.userService = userService;
            this.assetTypeService = assetTypeService;
            this.assetService = assetService;
            this.assetAttributeService = assetAttributeService;
            this.alertService = alertService;
        }

        public ActionResult Index(int siteId, int? jobCardId = 0)
        {
            ViewBag.JobCardId = jobCardId;
            return View(siteService.Get(siteId));
        }

        public PartialViewResult List(JobCardLister mJobCardLister)
        {
            mJobCardLister.Pager.Take = mJobCardLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mJobCardLister = JsonConvert.DeserializeObject<JobCardLister>(jsonString);
                        if (mJobCardLister != null && mJobCardLister.mJobCards != null && mJobCardLister.mJobCards.Count > 0)
                        {
                            if (mJobCardLister.SearchCriteria.Id > 0)
                            {
                                var mJobCards = new List<JobCard>();
                                var mSelectJobCard = mJobCardLister.mJobCards.Where(x => x.Id == mJobCardLister.SearchCriteria.Id).FirstOrDefault();
                                var mUnSelectJobCard = mJobCardLister.mJobCards.Where(x => x.Id != mJobCardLister.SearchCriteria.Id).ToList();
                                if (mSelectJobCard != null && mSelectJobCard.Id > 0)
                                    mJobCards.Add(mSelectJobCard);

                                if (mUnSelectJobCard != null && mUnSelectJobCard.Count > 0)
                                    mJobCards.AddRange(mUnSelectJobCard);

                                mJobCardLister.mJobCards = mJobCards;
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
            return PartialView(mJobCardLister);
        }

        public ActionResult Create(Domain.JobCard mJobCard)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mJobCard.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCard);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Job Card has been saved." };
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

            return Json(data);
        }

        [HttpPost]
        public ActionResult UpdateLockStatus(Domain.JobCard mJobCard)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCard);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"JobCard/{mJobCard.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Job Card has been locked." };
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

        public ActionResult CreateJobCardDescription(Domain.JobCardDescription mJobCardDescription)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mJobCardDescription.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardDescription);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCardDescription"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Job Card has been saved." };
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

            return Json(data);
        }

        public ActionResult UpdateJobCardDescriptionRemark(Domain.JobCardDescription mJobCardDescription)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mJobCardDescription.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardDescription);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCardDescription/UpdateRemark"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Job Card has been saved." };
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

            return Json(data);
        }

        public ActionResult GetJobCardDescription(int jobCardId, string date)
        {
            var mJobCardDescription = new List<Domain.JobCardDescription>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardDescription/JobCardId/{jobCardId}/DateTime/{date.Replace(" / ", " - ")}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardDescription = JsonConvert.DeserializeObject<List<Domain.JobCardDescription>>(jsonString);
                        if (mJobCardDescription == null)
                        {
                            mJobCardDescription = new List<Domain.JobCardDescription>();
                        }

                    }

                }
            }
            catch (Exception)
            {

            }
            return Json(mJobCardDescription);
        }

        public ActionResult _JobCardPartial(int id)
        {
            var mJobCard = new Domain.JobCard();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCard/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCard = JsonConvert.DeserializeObject<Domain.JobCard>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mJobCard);
        }

        #region Assign Site Keeping User

        public ActionResult _AssignSiteKeepingUser(int jobCardId)
        {
            ViewBag.JobCardId = jobCardId;
            var mUsers = userService.GetSiteKeepingUser();
            ViewBag.JobCardUser = GetJobCardUser(jobCardId);
            return PartialView(mUsers);
        }

        public ActionResult AssignSiteKeepingUser(List<Domain.JobCardUser> mJobCardUsers)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardUsers);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard/CreateJobCardUser"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Site Keeping User has been assigned." };
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

        #region OverallTargets

        public ActionResult _AddOverallTargets(int siteId, int jobCardId)
        {
            ViewBag.JobCardId = jobCardId;

            var mJobCardOverallTargets = GetOverallTarget(jobCardId);

            var mA10Status = GetA10Status(siteId);
            if (mA10Status != null && mA10Status.Count > 0)
            {
                if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
                {
                    var mOverallTarget = mJobCardOverallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.LinkIssue).ToList();
                    if (mOverallTarget != null && mOverallTarget.Count > 0)
                    {
                        var cardLineIds = mOverallTarget.Where(x => x.A10Id.HasValue).Select(x => $"{x.A10Id.Value}").ToList();

                        mA10Status = mA10Status.Where(x => !cardLineIds.Contains(x.id)).ToList();
                    }
                }


                ViewBag.RFStatus = mA10Status;
            }



            WatchListLister mWatchListLister = new WatchListLister();
            mWatchListLister.SearchCriteria.SiteId = siteId;
            mWatchListLister = GetWatchList(mWatchListLister);
            if (mWatchListLister != null && mWatchListLister.WatchLists != null && mWatchListLister.WatchLists.Count > 0)
            {
                if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
                {
                    var mOverallTarget = mJobCardOverallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.WatchLists).ToList();
                    if (mOverallTarget != null && mOverallTarget.Count > 0)
                    {
                        var watchListIds = mOverallTarget.Select(x => x.WatchListId).ToList();
                        mWatchListLister.WatchLists = mWatchListLister.WatchLists.Where(x => !watchListIds.Contains(x.Id)).ToList();
                    }
                }

                ViewBag.WatchLists = mWatchListLister.WatchLists;
            }


            var mFRSWatchLists = GetFRSWatchList(siteId);
            if (mFRSWatchLists != null && mFRSWatchLists.Count > 0)
            {
                Domain.SearchCriteria searchCriteria = new Domain.SearchCriteria();
                searchCriteria.SiteId = siteId;
                searchCriteria.IsCheckAlertType = true;
                searchCriteria.IsAlert = false;
                var mFRSAlertAudit = GetFRSAlertAudit(searchCriteria);
                if (mFRSAlertAudit != null && mFRSAlertAudit.Count > 0)
                {
                    foreach (var item in mFRSWatchLists)
                    {
                        var frsAlertAudit = mFRSAlertAudit.Where(x => x.AssetId == item.AssetId && x.AlertInfoId == item.AlertInfoId).OrderByDescending(x => x.Id).FirstOrDefault();
                        if (frsAlertAudit != null && frsAlertAudit.IssueTypeId != null && frsAlertAudit.IssueTypeId.Value > 0)
                            item.IssueTypeId = frsAlertAudit.IssueTypeId;
                    }

                    mFRSWatchLists = mFRSWatchLists.Where(x => x.IsSet && x.IssueTypeId != null && (x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Calibration || x.IssueTypeId == (int)E7FRSAdvance.Utility.Utility.FRSAlertIssue.Hardware)).ToList();
                }

                if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
                {
                    var mSMSOverallTarget = mJobCardOverallTargets.Where(x => x.TypeId == (int)E7FRSAdvance.Utility.Utility.JobCardOverallTargetType.FRSWatchList).ToList();
                    if (mSMSOverallTarget != null && mSMSOverallTarget.Count > 0)
                    {
                        var smsIds = mSMSOverallTarget.Select(x => x.FRSWatchListId).ToList();
                        mFRSWatchLists = mFRSWatchLists.Where(x => !smsIds.Contains(x.Id) && x.IsSet).ToList();
                    }
                }
                ViewBag.FRSWatchList = mFRSWatchLists;
            }

            ViewBag.AssetTypes = new SelectList(assetTypeService.GetAll(), "Id", "Name");

            var issueType = EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.WatchIssue());
            var dynamicIssueTypes = GetIssueTypes();
            if (dynamicIssueTypes != null && dynamicIssueTypes.Count > 0)
            {
                issueType.AddRange(dynamicIssueTypes);
            }
            ViewBag.IssueTypes = issueType;

            return PartialView();
        }

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAssetAttributesBy(int assetTypeId)
        {
            var allAssetAttributes = assetAttributeService.GetAssetAttributesBy(assetTypeId);
            return Json(allAssetAttributes, JsonRequestBehavior.AllowGet);
        }

        public ActionResult CreateJobCardOverall(List<Domain.JobCardOverallTarget> mJobCardOverallTargets)
        {
            dynamic data = new ExpandoObject();
            if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
            {
                foreach (var mJobCardOverallTarget in mJobCardOverallTargets)
                {
                    mJobCardOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                }

                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mJobCardOverallTargets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("JobCardOverall"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            data = new { type = "success", result = "Overall Targets has been saved." };

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

            }


            return Json(data);
        }

        public ActionResult UpdateJobCardOverall(List<Domain.JobCardOverallTarget> mJobCardOverallTargets)
        {
            dynamic data = new ExpandoObject();

            if (mJobCardOverallTargets != null && mJobCardOverallTargets.Count > 0)
            {
                foreach (var mJobCardOverallTarget in mJobCardOverallTargets)
                {
                    mJobCardOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                }
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mJobCardOverallTargets);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("JobCardOverall/UpdateStatus"), str).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.NoContent)
                        {
                            data = new { type = "success", result = "Overall Targets has been saved." };
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


            }


            return Json(data);
        }

        public ActionResult _SiteKeepingOverallTargets(int jobCardId)
        {
            ViewBag.JobCardId = jobCardId;
            var mJobCardOverallTargets = new List<Domain.JobCardOverallTarget>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardOverall/GetBySitekipping/JobCardId/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardOverallTargets = JsonConvert.DeserializeObject<List<Domain.JobCardOverallTarget>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                var issueType = EnumHelper.GetEnumDisplayNames(new E7FRSAdvance.Utility.Utility.WatchIssue());
                var dynamicIssueTypes = GetIssueTypes();
                if (dynamicIssueTypes != null && dynamicIssueTypes.Count > 0)
                {
                    issueType.AddRange(dynamicIssueTypes);
                }
                ViewBag.WatchIssues = issueType;
            }

            return PartialView(mJobCardOverallTargets);
        }

        public void UpdateJobCardWatchList(List<WatchList> mWatchLists)
        {
            dynamic data = new ExpandoObject();
            try
            {
                foreach (var mWatchList in mWatchLists)
                {
                    mWatchList.UserId = ClsHttpContent.LoginUser.Id;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mWatchLists);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("SiteKeeping/UpdateJobCardWatchList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Status has been updated." };
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
            // return Json(data, JsonRequestBehavior.AllowGet);
        }

        public void UpdateJobCardRFStatusList(List<RFStatus> mRFStatus)
        {
            dynamic data = new ExpandoObject();
            try
            {
                foreach (var RFSta in mRFStatus)
                {
                    RFSta.UserId = ClsHttpContent.LoginUser.Id;
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRFStatus);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format("SiteKeeping/UpdateJobCardRFStatusList"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Status has been updated." };
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
            // return Json(data, JsonRequestBehavior.AllowGet);
        }

        #endregion

        #region Create Job Card Status

        public ActionResult CreateJobCardStatus(Domain.JobCardStatus mJobCardStatus)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mJobCardStatus.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardStatus);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCardStatus"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", result = "Status has been saved." };
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

            return Json(data);
        }

        #endregion

        public ActionResult _JobCardSummary(int jobCardId)
        {
            var mJobCard = new Domain.JobCard();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCard/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCard = JsonConvert.DeserializeObject<Domain.JobCard>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mJobCard);
        }

        #region Assign Maintainer User

        public ActionResult _AssignMaintainerUser(int jobCardId)
        {
            ViewBag.JobCardId = jobCardId;
            var mUsers = new List<Domain.MaintainerUser>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintainerUser/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<Domain.MaintainerUser>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mUsers);
        }

        public ActionResult AssignMaintainerUser(List<Domain.JobCardMaintainerUser> mMaintainerUsers)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mMaintainerUsers);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard/CreateJobCardMaintainerUser"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Maintainer User User has been assigned." };
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

        #region JobCardList

        public ActionResult _SingleJobCard(int id)
        {
            var mJobCard = new Domain.JobCard();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCard/{id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCard = JsonConvert.DeserializeObject<Domain.JobCard>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mJobCard);
        }

        public ActionResult GetJobCardById(JobCardLister mJobCardLister)
        {
            mJobCardLister.Pager.Take = mJobCardLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mJobCardLister = JsonConvert.DeserializeObject<JobCardLister>(jsonString);
                        if (mJobCardLister != null && mJobCardLister.mJobCards != null && mJobCardLister.mJobCards.Count > 0)
                        {
                            if (mJobCardLister.SearchCriteria.Id > 0)
                            {
                                var mJobCards = new List<JobCard>();
                                var mSelectJobCard = mJobCardLister.mJobCards.Where(x => x.Id == mJobCardLister.SearchCriteria.Id).FirstOrDefault();
                                var mUnSelectJobCard = mJobCardLister.mJobCards.Where(x => x.Id != mJobCardLister.SearchCriteria.Id).ToList();
                                if (mSelectJobCard != null && mSelectJobCard.Id > 0)
                                    mJobCards.Add(mSelectJobCard);

                                if (mUnSelectJobCard != null && mUnSelectJobCard.Count > 0)
                                    mJobCards.AddRange(mUnSelectJobCard);

                                mJobCardLister.mJobCards = mJobCards;
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
            return PartialView("_JobCardMaster", mJobCardLister);
        }

        #endregion

        #region Job Card Material

        public ActionResult _JobCardMaterial(int jobCardId)
        {
            var mJobCardStatus = new List<Domain.JobCardMaterial>();
            ViewBag.JobCardId = jobCardId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardMaterial/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardStatus = JsonConvert.DeserializeObject<List<Domain.JobCardMaterial>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return PartialView(mJobCardStatus);
        }

        public ActionResult SaveJobCardMaterial(List<Domain.JobCardMaterial> mJobCardMaterials)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (mJobCardMaterials != null && mJobCardMaterials.Count > 0)
                {
                    foreach (var mJobCardMaterial in mJobCardMaterials)
                    {
                        mJobCardMaterial.CreatedBy = ClsHttpContent.LoginUser.Id;
                    }
                }
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardMaterials);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCardMaterial"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Material has been saved." };
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

        public ActionResult DownloadJobCardPDF(int jobCardId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCard/DownloadPDF/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string stringInBase64 = JsonConvert.DeserializeObject<string>(jsonString);
                        if (stringInBase64.IsNotNullOrEmpty())
                        {
                            byte[] csvbytes = System.Convert.FromBase64String(stringInBase64);
                            if (csvbytes != null && csvbytes.Length > 0)
                            {
                                return File(csvbytes, "application/pdf", $"JobCard.pdf");
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

            return RedirectToAction("Index");
        }

        #region QualityAssurance

        public ActionResult QualityAssurance()
        {
            return View();
        }

        public ActionResult QualityAssuranceList(JobCardLister mJobCardLister)
        {
            mJobCardLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("JobCard/GetQualityAssurance"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mJobCardLister = JsonConvert.DeserializeObject<JobCardLister>(jsonString);
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

                ViewBag.SiteKeepingUsers = userService.GetSiteKeepingUser();
                ViewBag.MaintainerUsers = GetMaintainerUser();
            }
            return PartialView("QualityAssuranceList", mJobCardLister);
        }

        public ActionResult _QualityAssuranceJobCard(int jobCardId, string date)
        {
            ViewBag.JobCardId = jobCardId;
            var mJobCardOverallTargets = new List<Domain.JobCardOverallTarget>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardOverall/GetQualityAssurance/JobCardId/{jobCardId}/Date/{date}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardOverallTargets = JsonConvert.DeserializeObject<List<Domain.JobCardOverallTarget>>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return PartialView(mJobCardOverallTargets);
        }

        public ActionResult QualityAssuranceUpdate(Domain.JobCardOverallTarget mJobCardOverallTarget)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mJobCardOverallTarget.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mJobCardOverallTarget);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PutAsync(String.Format($"JobCardOverall/UpdateQualityAssurance/{mJobCardOverallTarget.Id}"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        data = new { type = "success", result = "Job Card has been Updated." };
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

        public ActionResult GetQualityAssuranceRemarks(int jobCardOverallTargetId)
        {
            var mJobCardOverallTargets = new List<Domain.JobCardOverallTargetRemark>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardOverall/GetRemarks/JobCardOverallTargetId/{jobCardOverallTargetId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardOverallTargets = JsonConvert.DeserializeObject<List<Domain.JobCardOverallTargetRemark>>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return PartialView("_QualityAssuranceRemarks", mJobCardOverallTargets);
        }

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

        #endregion


        #region Daily Work

        public ActionResult _DailyWork(int jobCardId)
        {
            ViewBag.jobCardId = jobCardId;
            return PartialView();
        }

        #endregion

        public ActionResult _Remark(int Id)
        {
            ViewBag.jobCardDescriptionId = Id;
            return PartialView();
        }

        public List<Domain.A10Status> GetA10Status(int siteId)
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
                        var a10Status = JsonConvert.DeserializeObject<List<Domain.A10Status>>(jsonString);
                        if (a10Status != null && a10Status.Count > 0)
                        {
                            a10Status.ForEach(x =>
                            {
                                DateTime.TryParse(x.timestamp, out DateTime cDate);
                                x.ConvertDate = cDate;
                            });
                            var maxdate = a10Status.Select(x => x.ConvertDate).Max();
                            a10Status = a10Status.Where(x => x.ConvertDate == maxdate).ToList();
                            foreach (var newA10Status in a10Status)
                            {
                                double seconds = 0;
                                double.TryParse(newA10Status.last_response, out double last_response);
                             

                                if (last_response > 50)
                                {
                                    mA10Status.Add(newA10Status);
                                }
                            }

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

        #region Private Mathod

        private List<JobCardUser> GetJobCardUser(int jobCardId)
        {
            var mJobCardUsers = new List<JobCardUser>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCard/GetJobCardUser/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardUsers = JsonConvert.DeserializeObject<List<JobCardUser>>(jsonString);

                    }

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mJobCardUsers;
        }

        private List<SMSLog> GetLast30AlertList(SMSLogLister mSMSLogLister)
        {
            var mSMSLogs = new List<SMSLog>();
            try
            {
                mSMSLogLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-1);
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
                        if (mSMSLogLister != null && mSMSLogLister.mSMSLogs != null && mSMSLogLister.mSMSLogs.Count > 0)
                        {
                            var assetIds = mSMSLogLister.mSMSLogs.GroupBy(x => x.AssetId).Select(x => x.Key).ToList();
                            if (assetIds != null && assetIds.Count > 0)
                            {
                                foreach (var assetId in assetIds)
                                {
                                    var mSMSLog = mSMSLogLister.mSMSLogs.Where(x => x.AssetId == assetId).OrderByDescending(x => x.TimeStamp).FirstOrDefault();

                                    if (mSMSLog != null && mSMSLog.Id > 0)
                                        mSMSLogs.Add(mSMSLog);
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mSMSLogs;
        }

        private List<Domain.FRSWatchList> GetFRSWatchList(int siteId)
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

        public RFStatusLister GetRFStatus(RFStatusLister mRFStatusLister)
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
            return mRFStatusLister;
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

        private List<Domain.MaintainerUser> GetMaintainerUser()
        {
            var mUsers = new List<Domain.MaintainerUser>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"MaintainerUser/GetAll")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<Domain.MaintainerUser>>(jsonString);
                        if (mUsers != null && mUsers.Count > 0)
                        {
                            foreach (var mUser in mUsers)
                            {

                                mUser.Name = $"{mUser.FirstName} {mUser.LastName}";

                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            return mUsers;
        }

        private List<Domain.JobCardOverallTarget> GetOverallTarget(int jobCardId)
        {
            var mJobCardOverallTargets = new List<Domain.JobCardOverallTarget>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"JobCardOverall/JobCardId/{jobCardId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mJobCardOverallTargets = JsonConvert.DeserializeObject<List<Domain.JobCardOverallTarget>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return mJobCardOverallTargets;
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

        #endregion

    }
}