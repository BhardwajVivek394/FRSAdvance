using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
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
    public class JobCardMasterController : Controller
    {
        // GET: JobCardMaster
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        private readonly IUserService userService;
        private readonly IAssetTypeService assetTypeService;
        private readonly IAssetService assetService;
        private readonly IAssetAttributeService assetAttributeService;
        public JobCardMasterController(IDivisionService divisionService, ISiteService siteService, IUserService userService, IAssetTypeService assetTypeService, IAssetService assetService, IAssetAttributeService assetAttributeService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
            this.userService = userService;
            this.assetTypeService = assetTypeService;
            this.assetService = assetService;
            this.assetAttributeService = assetAttributeService;

        }

        public ActionResult Index()
        {
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return View();
        }

        public ActionResult _JobCardMasterList(JobCardLister mJobCardLister)
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
            }
            return PartialView(mJobCardLister);
        }

        public ActionResult GetJobCardMasterById(JobCardLister mJobCardLister)
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

            return PartialView(mJobCardOverallTargets);
        }
    }
}