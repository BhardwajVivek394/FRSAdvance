using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Service;
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
    [Authenticate]
    public class UserController : Controller
    {
        // GET: User
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;
        public UserController(IDivisionService divisionService, ISiteService siteService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
        }
        public ActionResult Index()
        {
            GetAllRole();
            GetUserLevels();
            ViewBag.UserClass = new SelectList(GetUserClasses(), "Id", "ClassName");
            return View();
        }

        public PartialViewResult UserList(UserLister mUserLister)
        {
            mUserLister.Pager.Take = mUserLister.Pager.PageSize;
            mUserLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetAllUserLister"), str).Result;
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
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            ViewBag.Divisions = new SelectList(divisionService.GetAll(), "Id", "Name");
            ViewBag.UserClass = new SelectList(GetUserClasses(), "Id", "ClassName");
            return PartialView("_UserListPartial", mUserLister);
        }

        public PartialViewResult LoginHistory(Domain.LoginHistoryLister mLoginHistoryLister)
        {
            //mLoginHistoryLister.Pager.Take = -1;
            mLoginHistoryLister.Pager.Take = mLoginHistoryLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLoginHistoryLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetLoginHistory"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLoginHistoryLister = JsonConvert.DeserializeObject<LoginHistoryLister>(jsonString);
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
            GetAllUsers();
            ViewBag.Sites = new SelectList(siteService.GetAll(), "Id", "Name");
            return PartialView("_LoginHistory", mLoginHistoryLister);
        }

        public void GetAllUsers()
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
            ViewBag.Users = new SelectList(mUsers.ToList(), "Id", "EmailAddress");
        }
        [HttpPost]
        public ActionResult SaveUser(User mUser)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                mUser.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUser);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/SaveUser"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "User has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving user!";
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error.";
            }
            GetAllRole();
            GetUserLevels();
            ViewBag.UserClass = new SelectList(GetUserClasses(), "Id", "ClassName");
            return PartialView("_AddUserPartial", new User());
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
            GetAllRole();
            GetUserLevels();
            ViewBag.UserClass = new SelectList(GetUserClasses(), "Id", "ClassName");
            return PartialView("~/Views/User/_AddUserPartial.cshtml", mUser);
        }

        public ActionResult DeleteUserById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/DeleteUserById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                        mAPIResponse.Message = "Internal server error.";
                    }
                }

            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }

        public ActionResult IsUserNameExisting(string UserName, int userId)
        {
            APIResponse mAPIResponse = new APIResponse();
            string result;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/IsUserExisting/{0}/{1}", UserName, userId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.IsSuccess)
                        {
                            if (mAPIResponse.Value)
                            {
                                result = "true";
                            }
                            else
                            {
                                result = "false";
                            }
                        }
                        else
                        {
                            result = "Internal server error.";
                        }
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
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public void GetAllRole()
        {
            List<Role> mRoles = new List<Role>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetAllRoles")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mRoles = JsonConvert.DeserializeObject<List<Role>>(jsonString);
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
            ViewBag.Roles = new SelectList(mRoles.ToList(), "Id", "Title");
        }

        public void GetUserLevels()
        {
            List<UserLevel> mUserLevels = new List<UserLevel>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserLevel/GetUserLevels")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserLevels = JsonConvert.DeserializeObject<List<UserLevel>>(jsonString);
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
            ViewBag.Levels = new SelectList(mUserLevels.ToList(), "Id", "Level");
        }

        public ActionResult Role()
        {
            return View();
        }

        public PartialViewResult RoleList(RoleLister mRoleLister)
        {
            mRoleLister.Pager.Take = mRoleLister.Pager.PageSize;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRoleLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Role/GetAllRoleLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mRoleLister = JsonConvert.DeserializeObject<RoleLister>(jsonString);
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
            return PartialView("_RoleListPartial", mRoleLister);
        }

        [HttpPost]
        public ActionResult SaveRole(Role mRole)
        {

            mRole.CreatedBy = ClsHttpContent.LoginUser.Id;
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mRole);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Role/SaveRole"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "Role has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving role!";
                        }
                    }
                    else
                    {
                        ViewBag.Type = "Error";
                        ViewBag.Message = "Internal server error.";
                    }
                }
            }
            catch (Exception)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = "Internal server error.";
            }
            return PartialView("_AddRolePartial", new Role());

        }

        public ActionResult GetRoleById(int id)
        {
            Role mRole = new Role();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Role/GetRoleById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mRole = JsonConvert.DeserializeObject<Role>(jsonString);
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
            return PartialView("~/Views/User/_AddRolePartial.cshtml", mRole);
        }

        public ActionResult DeleteRoleById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            string result;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Role/DeleteRoleById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.IsSuccess)
                        {
                            result = "true";
                        }
                        else
                        {
                            result = "false";
                        }
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
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public JsonResult MakeSessionLive()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Login/CheckToken").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        Utility.ClsHttpContent.MakeMeLive = 1;
                        return Json(new { Result = "Success", Message = "Live" }, JsonRequestBehavior.AllowGet);
                    }
                    else if (response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return Json(new { Result = "Forbidden", Message = "Expired" }, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        return Json(new { Result = "Error", Message = "Internal server error." }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception)
            {
                return Json(new { Result = "Error", Message = "Internal server error." }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult ChangePassword()
        {
            ChangePassword mChangePassword = new ChangePassword();
            mChangePassword.Id = Utility.ClsHttpContent.LoginUser.Id;
            return View(mChangePassword);
        }

        [HttpPost]
        public ActionResult ChangePassword(ChangePassword mChangePassword)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                if (ModelState.IsValid)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mChangePassword);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("User/ChangePassword"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                            if (mAPIResponse != null && mAPIResponse.IsSuccess == true)
                            {
                                ViewBag.Success = mAPIResponse.Message;
                            }
                            else if (mAPIResponse != null && mAPIResponse.IsSuccess == false)
                            {
                                ViewBag.Error = mAPIResponse.Message;
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return View("ChangePassword");
        }

        public JsonResult GetSites(int zoneId, int divisionId, int userId)
        {
            List<Site> mSites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSites/{0}/{1}/{2}", zoneId, divisionId, userId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetAllZones()
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
            return Json(mZones, JsonRequestBehavior.AllowGet);
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

        public JsonResult AssignSiteByUserId(int id, int siteId, bool isAssigned)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/AssignSiteByUserId/{0}/{1}/{2}", id, siteId, isAssigned)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult MyProfile()
        {
            User mUser = new User();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetUserById/{0}", ClsHttpContent.LoginUser.Id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUser = JsonConvert.DeserializeObject<User>(jsonString);
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
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message.ToString();
            }
            return View(mUser);
        }

        [HttpPost]
        public ActionResult MyProfile(User mUser)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                if (string.IsNullOrEmpty(mUser.FirstName) && string.IsNullOrWhiteSpace(mUser.FirstName))
                    ViewBag.Error = "First name is required!";
                else if (string.IsNullOrEmpty(mUser.LastName) && string.IsNullOrWhiteSpace(mUser.LastName))
                    ViewBag.Error = "Last name is required!";
                else if (string.IsNullOrEmpty(mUser.PhoneNumber) && string.IsNullOrWhiteSpace(mUser.PhoneNumber))
                    ViewBag.Error = "Phone number is required!";
                else if (string.IsNullOrEmpty(mUser.EmailAddress) && string.IsNullOrWhiteSpace(mUser.EmailAddress))
                    ViewBag.Error = "Email address is required!";
                else
                {
                    mUser.CreatedBy = ClsHttpContent.LoginUser.Id;
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var jsonStr = JsonConvert.SerializeObject(mUser);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("User/SaveUser"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                            if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                            {
                                ViewBag.Success = "User has been updated.";
                            }
                            else
                            {
                                ViewBag.Error = "Error occured while updating user!";
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message.ToString();
            }
            return View("MyProfile");
        }

        public PartialViewResult BlockIpList(Domain.BlockIPAddressLister mBlockIPAddressLister)
        {
            mBlockIPAddressLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBlockIPAddressLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetAllBolckIpLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mBlockIPAddressLister = JsonConvert.DeserializeObject<BlockIPAddressLister>(jsonString);
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
            return PartialView("_BlockIpAddressList", mBlockIPAddressLister);
        }

        public ActionResult SaveBlockIp(Domain.BlockIPAddress mBlockIP)
        {
            APIResponse mAPIResponse = new APIResponse();
            bool result = false; ;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBlockIP);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/SaveBlockIp"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.IsSuccess)
                        {
                            result = true;
                        }
                    }
                }

            }
            catch (Exception ex)
            {

            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteIpById(int id)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/DeleteIpId/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                    else
                    {
                        result = false;
                    }
                }

            }
            catch (Exception ex)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult MQTT()
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
            return View(mMQTTDetail);
        }

        [HttpPost]
        public ActionResult MQTT(MQTTDetailList mMQTTDetail)
        {
            APIResponse mAPIResponse = new APIResponse();

            try
            {
                ModelState.Remove("Id");
                if (ModelState.IsValid)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var mqtt = new MQTTDetail();
                        if (mMQTTDetail.mQTTDetailUtility != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailUtility.Type))
                        {
                            mqtt = mMQTTDetail.mQTTDetailUtility;
                        }
                        if (mMQTTDetail.mQTTDetailWeb != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailWeb.Type))
                        {
                            mqtt = mMQTTDetail.mQTTDetailWeb;
                        }
                        var jsonStr = JsonConvert.SerializeObject(mqtt);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("User/SaveMQQTDetail"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAPIResponse.IsSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                            if (mAPIResponse != null && mAPIResponse.IsSuccess == true)
                            {
                                ViewBag.Success = "MQTT Detail saved!";
                            }
                            else
                            {
                                ViewBag.Error = "Internal server error.";
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            if (mMQTTDetail.mQTTDetailWeb != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailWeb.Type) && mMQTTDetail.mQTTDetailWeb.Type == "Web")
            {
                return PartialView("_AddMQTTWeb", mMQTTDetail);
            }
            else
            {
                return PartialView("_AddMQTTUtility", mMQTTDetail);
            }
            //return View("MQTT");
        }


        [HttpPost]
        public ActionResult MQTT1(MQTTDetailList mMQTTDetail)
        {
            APIResponse mAPIResponse = new APIResponse();

            try
            {
                ModelState.Remove("Id");
                if (ModelState.IsValid)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var mqtt = new MQTTDetail();
                        if (mMQTTDetail.mQTTDetailUtility != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailUtility.Type))
                        {
                            mqtt = mMQTTDetail.mQTTDetailUtility;
                        }
                        if (mMQTTDetail.mQTTDetailWeb != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailWeb.Type))
                        {
                            mqtt = mMQTTDetail.mQTTDetailWeb;
                        }
                        var jsonStr = JsonConvert.SerializeObject(mqtt);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("User/SaveMQQTDetail"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAPIResponse.IsSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                            if (mAPIResponse != null && mAPIResponse.IsSuccess == true)
                            {
                                ViewBag.Success = "MQTT Detail saved!";
                            }
                            else
                            {
                                ViewBag.Error = "Internal server error.";
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            if (mMQTTDetail.mQTTDetailWeb != null && !string.IsNullOrEmpty(mMQTTDetail.mQTTDetailWeb.Type) && mMQTTDetail.mQTTDetailWeb.Type == "Web")
            {
                return PartialView("_AddMQTTWeb", mMQTTDetail);
            }
            else
            {
                return PartialView("_AddMQTTUtility", mMQTTDetail);
            }
            //return View("MQTT");
        }
        public ActionResult LoginProfile()
        {
            return View();
        }

        public PartialViewResult LoginMacAddressList(LoginMacAddressLister mLoginMacAddressLister)
        {
            mLoginMacAddressLister.Pager.Take = -1;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLoginMacAddressLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetAllLoginMacAddressLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLoginMacAddressLister = JsonConvert.DeserializeObject<LoginMacAddressLister>(jsonString);
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
            BindUser();
            return PartialView("_LoginProfileList", mLoginMacAddressLister);
        }

        public ActionResult GetLoginProfile(bool isApproved)
        {
            LoginMacAddressLister mLoginMacAddressLister = new LoginMacAddressLister();
            mLoginMacAddressLister.Pager.Take = -1;
            mLoginMacAddressLister.Pager.Skip = 0;
            mLoginMacAddressLister.SearchCriteria.IsApproved = isApproved;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLoginMacAddressLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/GetAllLoginMacAddressLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mLoginMacAddressLister = JsonConvert.DeserializeObject<LoginMacAddressLister>(jsonString);
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
            BindUser();
            return PartialView("_LoginProfileList", mLoginMacAddressLister);
        }

        public ActionResult UpdateMacAddress(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/UpdateMacAddress/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }

            }
            catch (Exception ex)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteMacAddressById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/DeleteMacAddressById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }

            }
            catch (Exception ex)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _AppAccessPartial()
        {
            return PartialView("_AppAccessPartial", new AppAccessLister());
        }

        public ActionResult GetAllAppAccess(AppAccessLister mAppAccessLister)
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

            return Json(mAppAccessLister, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DownloadAppAccess(AppAccessLister mAppAccessLister)
        {
            mAppAccessLister.Pager.Take = -1;
            string filePath = string.Empty;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAppAccessLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AppAccess/DownloadAppAccess"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        filePath = JsonConvert.DeserializeObject<string>(jsonString);

                    }

                }
            }
            catch (Exception)
            {
            }

            return Json(filePath, JsonRequestBehavior.AllowGet);
        }

        public void BindUser()
        {
            List<User> mUser = new List<User>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    HttpResponseMessage response;
                    response = hcf.client.GetAsync("User/GetAllUser").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUser = JsonConvert.DeserializeObject<List<User>>(jsonString);
                        if (mUser != null && mUser.Count > 0)
                        {
                            List<SelectListItem> mSelectListItems = new List<SelectListItem>();
                            mSelectListItems.Add(new SelectListItem() { Selected = true, Text = "All User", Value = "0" });
                            foreach (var ct in mUser)
                            {
                                mSelectListItems.Add(new SelectListItem() { Selected = true, Text = ct.EmailAddress, Value = Convert.ToString(ct.Id) });
                            }
                            ViewBag.User = mSelectListItems;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                //result = false;
            }
        }


        [HttpPost]
        public JsonResult GetSitesBy(int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                sites = siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
            }
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        public PartialViewResult _BlockMACList(BlockMacAddressLister mBlockMacAddressLister)
        {
            mBlockMacAddressLister.Pager.Take = mBlockMacAddressLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBlockMacAddressLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BlockMacAddress/GetAll"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mBlockMacAddressLister = JsonConvert.DeserializeObject<BlockMacAddressLister>(jsonString);
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
            return PartialView(mBlockMacAddressLister);
        }

        public ActionResult SaveBlockMac(Domain.BlockMacAddress mBlockMacAddress)
        {
            bool result = false;
            try
            {
                mBlockMacAddress.CreatedBy = ClsHttpContent.LoginUser.Id;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mBlockMacAddress);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("BlockMacAddress"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mBlockMacAddress = JsonConvert.DeserializeObject<Domain.BlockMacAddress>(jsonString);
                        if (mBlockMacAddress != null && mBlockMacAddress.Id > 0)
                        {
                            result = true;
                        }
                    }
                }

            }
            catch (Exception ex)
            {

            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteMacById(int id)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format("BlockMacAddress/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                    else
                    {
                        result = false;
                    }
                }

            }
            catch (Exception ex)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }


        //  [HttpPost]
        public ActionResult _AssignUserDivision(int userId)
        {
            var mUserDivisions = new List<Domain.UserDivision>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"User/GetAssignUserDivision/{userId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserDivisions = JsonConvert.DeserializeObject<List<Domain.UserDivision>>(jsonString);
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
            finally
            {
                ViewBag.Division = divisionService.GetAll();
                ViewBag.UserId = userId;
            }
            return PartialView(mUserDivisions);
        }

        [HttpPost]
        public JsonResult AssignUserDivision(UserDivision mUserDivision)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserDivision);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("User/AssignUserDivision"), str).Result;
                    if (response.StatusCode == HttpStatusCode.NoContent)
                    {
                        result = true;
                    }

                }
            }
            catch (Exception)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        public ActionResult DeleteUserDivision(int userId, int divisionId)
        {
            bool result = false;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.DeleteAsync(String.Format($"User/DeleteUserDivision/UserId/{userId}/DivisionId/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        result = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                }

            }
            catch (Exception ex)
            {
                result = false;
            }
            return Json(result, JsonRequestBehavior.AllowGet);
        }
    }
}