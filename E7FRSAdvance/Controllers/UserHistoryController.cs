using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
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
    public class UserHistoryController : Controller
    {
        // GET: UserHistory
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        public UserHistoryController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
        }

        public ActionResult Index()
        {
            SiteLister mUserLister = new SiteLister();
            mUserLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-7);
            mUserLister.SearchCriteria.ToDate = DateTime.Now;
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            return View(mUserLister);
        }

        public PartialViewResult SiteList(SiteLister mSiteLister)
        {
            mSiteLister.Pager.Take = mSiteLister.Pager.PageSize;
            mSiteLister.SearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
            mSiteLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Site/GetSiteLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteLister = JsonConvert.DeserializeObject<SiteLister>(jsonString);
                        if (mSiteLister != null && mSiteLister.mSites != null)
                        {
                            UserLister mUserLister = new UserLister();
                            mUserLister.SearchCriteria.FromDate = mSiteLister.SearchCriteria.FromDate;
                            mUserLister.SearchCriteria.ToDate = mSiteLister.SearchCriteria.ToDate;
                            mUserLister.SearchCriteria.SiteIds.AddRange(mSiteLister.mSites.Select(x => x.Id).ToList());
                            mUserLister = GetUserLister(mUserLister);
                            if (mUserLister != null && mUserLister.Users != null && mUserLister.Users.Count > 0)
                            {
                                if (mSiteLister.SearchCriteria.SearchMinutes != null && mSiteLister.SearchCriteria.SearchMinutes.Value > 0)
                                {
                                    var selectedSites = new List<Domain.Site>();
                                    var selectedUsers = new List<User>();
                                    foreach (var us in mUserLister.Users)
                                    {
                                        bool isValid = false;
                                        if (mSiteLister.SearchCriteria.Type == "Greater")
                                        {
                                            if (us.Duration > mSiteLister.SearchCriteria.SearchMinutes.Value || us.WebDuration > mSiteLister.SearchCriteria.SearchMinutes.Value)
                                                isValid = true;
                                        }
                                        else if (mSiteLister.SearchCriteria.Type == "Less")
                                        {
                                            if (us.Duration < mSiteLister.SearchCriteria.SearchMinutes.Value || us.WebDuration < mSiteLister.SearchCriteria.SearchMinutes.Value)
                                                isValid = true;
                                        }
                                        if (isValid)
                                        {
                                            selectedUsers.Add(us);
                                            if (us.UserSites != null && us.UserSites.Count > 0)
                                            {
                                                foreach (var item in us.UserSites)
                                                {
                                                    var mSite = mSiteLister.mSites.Where(x => x.Id == item.SiteId).FirstOrDefault();

                                                    if (mSite != null && selectedSites.Where(x => x.Id == item.SiteId).Count() <= 0)
                                                        selectedSites.Add(mSite);
                                                }

                                            }
                                        }

                                    }
                                    mSiteLister.mSites = selectedSites;
                                    ViewBag.Users = selectedUsers;
                                }
                                else
                                    ViewBag.Users = mUserLister.Users;
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
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
                ViewBag.Zones = new SelectList(_zoneService.GetAll(), "Id", "Name");
                ViewBag.Divisions = new SelectList(_divisionService.GetAll(), "Id", "Name");
            }
            return PartialView(mSiteLister);
        }

        public UserLister GetUserLister(UserLister mUserLister)
        {
            mUserLister.Pager.Take = -1;
            mUserLister.SearchCriteria.RoleId = (int)E7FRSAdvance.Utility.Utility.Role.User;
            mUserLister.SearchCriteria.SiteKeepingSearch = false;

            if (mUserLister.SearchCriteria.FromDate == DateTime.MinValue)
                mUserLister.SearchCriteria.FromDate = DateTime.Now.AddDays(-7);

            if (mUserLister.SearchCriteria.ToDate == DateTime.MinValue)
                mUserLister.SearchCriteria.ToDate = DateTime.Now;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetUserHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserLister = JsonConvert.DeserializeObject<UserLister>(jsonString);

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
            return mUserLister;
        }

        public ActionResult _SiteTraining()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");

            return PartialView(new Domain.SiteTraining());
        }

        public ActionResult _SiteBenefit()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            return PartialView(new Domain.SiteBenefit());
        }

        public ActionResult _HistoryDetails(AppAccessLister mAppAccessLister)
        {
            if (mAppAccessLister != null && mAppAccessLister.SearchCriteria != null)
            {
                mAppAccessLister.Pager.Take = -1;
                try
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mAppAccessLister);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("AppAccess/GetAllLister"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAppAccessLister = JsonConvert.DeserializeObject<AppAccessLister>(jsonString);
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
                    ViewBag.WebLoginHistory = GetWebLoginHistory(new SearchCriteria() { UserId = mAppAccessLister.SearchCriteria.UserId, StartDate = mAppAccessLister.SearchCriteria.StartTime, EndDate = mAppAccessLister.SearchCriteria.EndTime });
                }
            }
            return PartialView(mAppAccessLister);
        }

        public List<Domain.LoginHistory> GetWebLoginHistory(SearchCriteria searchCriteria)
        {
            List<Domain.LoginHistory> mLoginHistories = new List<Domain.LoginHistory>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(searchCriteria);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetWebLoginHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLoginHistories = JsonConvert.DeserializeObject<List<Domain.LoginHistory>>(jsonString);
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
            return mLoginHistories;
        }

        public ActionResult GetFRSAlertList(Domain.FRSAlertLister mFRSAlertLister)
        {
            try
            {
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
                    var response = hcf.client.PostAsync(String.Format("FRSAlert/GetLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mFRSAlertLister = JsonConvert.DeserializeObject<FRSAlertLister>(jsonString);


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

            return PartialView("_FRSAlertList", mFRSAlertLister);
        }

        public ActionResult GetSiteBenefitFRSAlert(int siteBenefitId)
        {
            var mFRSAlerts = new List<FRSAlert>();
            Domain.FRSAlertLister mFRSAlertLister = new FRSAlertLister();
            ViewBag.SiteBenefitId = siteBenefitId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"FRSAlert/SiteBenefitId/{siteBenefitId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlerts = JsonConvert.DeserializeObject<List<Domain.FRSAlert>>(jsonString);
                        mFRSAlertLister.mFRSAlerts = mFRSAlerts;
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Something went wrong!";
            }

            return PartialView("_FRSAlertList", mFRSAlertLister);
        }

        public ActionResult GetSiteTrainingList(int siteId)
        {
            var mSiteTrainings = new List<Domain.SiteTraining>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SiteTraining/SiteId/{siteId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSiteTrainings = JsonConvert.DeserializeObject<List<Domain.SiteTraining>>(jsonString);

                    }
                }
            }
            catch (Exception ex)
            {
            }

            return Json(mSiteTrainings, JsonRequestBehavior.AllowGet);
        }

        public ActionResult SaveUserTraining(Domain.SiteTraining mSiteTraining)
        {
            try
            {
                mSiteTraining.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteTraining);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteTraining"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteTraining = JsonConvert.DeserializeObject<Domain.SiteTraining>(jsonString);
                        if (mSiteTraining != null && mSiteTraining.Id > 0)
                        {
                            ViewBag.Message = "Record has been saved.";
                            ViewBag.Type = "Success";
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
                ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            }
            return PartialView("_SiteTraining", mSiteTraining);
        }

        public ActionResult SaveSiteBenefit(Domain.SiteBenefit mSiteBenefit)
        {
            dynamic data = new ExpandoObject();
            try
            {
                mSiteBenefit.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mSiteBenefit);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SiteBenefit"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mSiteBenefit = JsonConvert.DeserializeObject<Domain.SiteBenefit>(jsonString);
                        if (mSiteBenefit != null && mSiteBenefit.Id > 0)
                        {
                            data = new { type = "success", result = "Remark has been Updated" };
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
            return Json(data);
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

        public List<User> GetAllUsers()
        {
            List<User> mUsers = new List<User>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetAllUser")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUsers = JsonConvert.DeserializeObject<List<User>>(jsonString);
                        mUsers.Insert(0, new Domain.User { Id = 0, EmailAddress = "All" });
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
            return mUsers;
        }

        public ActionResult GetUserById(int id)
        {
            User mUser = new User();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetUserById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUser = JsonConvert.DeserializeObject<User>(jsonString);
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
            finally
            {
                ViewBag.UserClass = new SelectList(GetUserClasses(), "Id", "ClassName");
            }
            return PartialView("_UserPartial", mUser);
        }

        public List<UserClass> GetUserClasses()
        {
            List<UserClass> mUserClasses = new List<UserClass>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserClass/GetAllUserClass")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserClasses = JsonConvert.DeserializeObject<List<UserClass>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return mUserClasses;
        }

    }
}