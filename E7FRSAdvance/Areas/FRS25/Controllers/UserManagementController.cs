using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class UserManagementController : Controller
    {
        // GET: FRS25/UserManagement
        private readonly IDivisionService divisionService;
        private readonly ISiteService siteService;

        public ActionResult Index()
        {
            return View();
        }

        public UserManagementController(IDivisionService divisionService, ISiteService siteService)
        {
            this.divisionService = divisionService;
            this.siteService = siteService;
        }

        // ── DASHBOARD COUNTS ─────────────────────────────────────────────
        [HttpPost]
        public ActionResult GetFRSUserDashboardCount(FRSUserDashboardCount model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new { UserId = model.UserId });
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("User/GetFRSUserDashboardCount", content).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }
                }
                return Json(new { IsSuccess = false, Message = "API Error" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [HttpPost]
        public JsonResult GetUserListJson(UserLister mUserLister)
        {
            mUserLister.Pager.Take = mUserLister.Pager.PageSize;
            mUserLister.SearchCriteria.RoleId = ClsHttpContent.LoginUser.RoleId;

            if (mUserLister.SearchCriteria.ZoneId <= 0)
            {
                mUserLister.SearchCriteria.ZoneId = ClsHttpContent.LoginUser.ZoneId;
            }

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("User/GetLister", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserLister = JsonConvert.DeserializeObject<UserLister>(jsonString);
                        return Json(new
                        {
                            IsSuccess = true,
                            Users = mUserLister.Users,
                            Pager = mUserLister.Pager
                        }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception) { }
            return Json(new
            {
                IsSuccess = false,
                Users = new List<User>(),
                Pager = mUserLister.Pager
            }, JsonRequestBehavior.AllowGet);
        }

        // ── GET ALL ROLES ────────────────────────────────────────────────
        [HttpPost]
        public JsonResult GetAllRoles()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetAllRoles").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var roles = JsonConvert.DeserializeObject<List<Role>>(jsonString);
                        return Json(roles.Select(r => new { r.Id, r.Title }), JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch { }
            return Json(new List<object>(), JsonRequestBehavior.AllowGet);
        }

        // ── GET DIVISIONS BY ZONE ID ─────────────────────────────────────
        [HttpPost]
        public JsonResult GetDivisionByZoneId(SearchModel model)
        {
            try
            {
                int zoneId = 0;
                if (model.SearchCriteria != null && model.SearchCriteria.ZoneIds != null && model.SearchCriteria.ZoneIds.Count > 0)
                {
                    zoneId = model.SearchCriteria.ZoneIds[0];
                }

                List<Division> mDivisions = GetByZoneId(zoneId);
                return Json(mDivisions.Select(d => new { d.Id, d.Name }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // ── GET ALL SITES BY ZONE ID ─────────────────────────────────────
        [HttpPost]
        public JsonResult GetSitesByZoneId(ZoneIdRequest model)
        {
            try
            {
                int ZoneId = model != null ? model.ZoneId : 0;
                List<Division> divisions = GetByZoneId(ZoneId);
                var allSites = new List<object>();
                var seenIds = new HashSet<int>();

                foreach (var div in divisions)
                {
                    var sites = GetSitesBy(ZoneId, div.Id);
                    foreach (var site in sites)
                    {
                        if (!seenIds.Contains(site.Id))
                        {
                            seenIds.Add(site.Id);
                            allSites.Add(new { site.Id, site.Name, DivisionId = div.Id, DivisionName = div.Name });
                        }
                    }
                }

                return Json(allSites, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // ── GET SITES BY DIVISION ID(s) ──────────────────────────────────
        [HttpPost]
        public JsonResult GetSiteByDivisionId(SearchModel model)
        {
            try
            {
                int zoneId = model.ZoneId > 0 ? model.ZoneId : ClsHttpContent.LoginUser.ZoneId;
                var divisionIds = new List<int>();

                if (model.SearchCriteria != null && model.SearchCriteria.DivisionIds != null)
                {
                    divisionIds = model.SearchCriteria.DivisionIds;
                }

                var allSites = new List<object>();
                var seenIds = new HashSet<int>();

                foreach (var divId in divisionIds)
                {
                    var sites = GetSitesBy(zoneId, divId);
                    foreach (var site in sites)
                    {
                        if (!seenIds.Contains(site.Id))
                        {
                            seenIds.Add(site.Id);
                            allSites.Add(new { site.Id, site.Name, DivisionId = divId });
                        }
                    }
                }

                return Json(allSites, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // SECTION METHODS (Division → Section → Site cascade)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get sections by division ID(s) — Division → Section cascade
        /// If no division IDs provided, returns ALL sections
        /// </summary>
        [HttpPost]
        public JsonResult GetSectionByDivisionId(SearchModel model)
        {
            try
            {
                var divisionIds = new List<int>();
                if (model != null && model.SearchCriteria != null && model.SearchCriteria.DivisionIds != null)
                {
                    divisionIds = model.SearchCriteria.DivisionIds.Where(id => id > 0).ToList();
                }

                List<Section> allSections = GetAllSections();

                if (divisionIds.Count == 0)
                {
                    // No division filter — return ALL sections
                    return Json(allSections.Select(s => new {
                        s.Id,
                        Name = s.Name,
                        s.DivisionId
                    }).OrderBy(s => s.Name), JsonRequestBehavior.AllowGet);
                }

                var filtered = allSections
                    .Where(s => divisionIds.Contains(s.DivisionId))
                    .Select(s => new {
                        s.Id,
                        Name = s.Name,
                        s.DivisionId
                    })
                    .OrderBy(s => s.Name)
                    .ToList();

                return Json(filtered, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Get all sections by zone — all divisions under zone → all sections
        /// If ZoneId is 0, returns ALL sections (no division selected scenario)
        /// </summary>
        [HttpPost]
        public JsonResult GetSectionsByZoneId(ZoneIdRequest model)
        {
            try
            {
                int ZoneId = model != null ? model.ZoneId : 0;
                List<Section> allSections = GetAllSections();

                if (ZoneId <= 0)
                {
                    // No zone filter — return all sections
                    var result = allSections.Select(s => new {
                        s.Id,
                        Name = s.Name,
                        s.DivisionId
                    }).OrderBy(s => s.Name).ToList();
                    return Json(result, JsonRequestBehavior.AllowGet);
                }

                // Get all divisions for this zone
                List<Division> divisions = GetByZoneId(ZoneId);
                var divisionIds = divisions.Select(d => d.Id).ToList();

                // Filter sections by those divisions
                var filtered = allSections
                    .Where(s => divisionIds.Contains(s.DivisionId))
                    .Select(s => new {
                        s.Id,
                        Name = s.Name,
                        s.DivisionId
                    })
                    .OrderBy(s => s.Name)
                    .ToList();

                return Json(filtered, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Get sites by section ID(s) — Section → Site cascade
        /// If no section IDs provided, returns all sites for the zone
        /// </summary>
        [HttpPost]
        public JsonResult GetSiteBySectionId(SearchModel model)
        {
            try
            {
                var sectionIds = new List<int>();
                if (model != null && model.SearchCriteria != null && model.SearchCriteria.SectionIds != null)
                {
                    sectionIds = model.SearchCriteria.SectionIds.Where(id => id > 0).ToList();
                }

                int zoneId = (model != null && model.ZoneId > 0) ? model.ZoneId : ClsHttpContent.LoginUser.ZoneId;

                if (sectionIds.Count == 0)
                {
                    // No section filter — return all sites for zone
                    List<Division> divisions = GetByZoneId(zoneId);
                    var allSites = new List<object>();
                    var seenIds = new HashSet<int>();
                    foreach (var div in divisions)
                    {
                        var sites = GetSitesBy(zoneId, div.Id);
                        foreach (var site in sites)
                        {
                            if (!seenIds.Contains(site.Id))
                            {
                                seenIds.Add(site.Id);
                                allSites.Add(new { site.Id, site.Name, DivisionId = div.Id });
                            }
                        }
                    }
                    return Json(allSites, JsonRequestBehavior.AllowGet);
                }

                // Get the division IDs that these sections belong to
                List<Section> allSections = GetAllSections();
                var divisionIds = allSections
                    .Where(s => sectionIds.Contains(s.Id))
                    .Select(s => s.DivisionId)
                    .Distinct()
                    .ToList();

                // Get sites for those divisions
                var filteredSites = new List<object>();
                var seen = new HashSet<int>();
                foreach (var divId in divisionIds)
                {
                    var sites = GetSitesBy(zoneId, divId);
                    foreach (var site in sites)
                    {
                        if (!seen.Contains(site.Id))
                        {
                            seen.Add(site.Id);
                            filteredSites.Add(new { site.Id, site.Name, DivisionId = divId });
                        }
                    }
                }

                return Json(filteredSites, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Save user-section mapping via API
        /// </summary>
        [HttpPost]
        public JsonResult SaveUserSectionMapping(UserSectionMappingRequest model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(model);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("UserSection/SaveMapping", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        return Json(new
                        {
                            IsSuccess = result != null && result.IsSuccess,
                            Message = result != null ? result.Message : "Saved."
                        }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { IsSuccess = false, Message = "Failed to save section mappings." }, JsonRequestBehavior.AllowGet);
        }

        // ══════════════════════════════════════════════════════════════════
        // END SECTION METHODS
        // ══════════════════════════════════════════════════════════════════

        // ── GET ALL USER CLASSES ─────────────────────────────────────────
        [HttpPost]
        public JsonResult GetAllUserClasses()
        {
            try
            {
                List<UserClass> mUserClasses = new List<UserClass>();
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("UserClass/GetAllUserClass").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserClasses = JsonConvert.DeserializeObject<List<UserClass>>(jsonString);
                    }
                }
                return Json(mUserClasses.Select(uc => new { uc.Id, uc.ClassName }), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new List<object>(), JsonRequestBehavior.AllowGet);
            }
        }

        // ── SAVE USER ────────────────────────────────────────────────────
        [HttpPost]
        public JsonResult SaveUser(User userData)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    userData.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(userData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    HttpResponseMessage response;

                    if (userData.Id > 0)
                    {
                        response = hcf.client.PutAsync("User/Id/" + userData.Id, str).Result;
                    }
                    else
                    {
                        response = hcf.client.PostAsync("User/SaveUserFRS", str).Result;
                    }

                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var token = JToken.Parse(jsonString);
                        bool isSuccess = false;
                        int id = 0;
                        string message = "";

                        if (token.Type == JTokenType.Object)
                        {
                            var obj = (JObject)token;
                            isSuccess = obj["IsSuccess"] != null ? obj["IsSuccess"].Value<bool>() : false;
                            id = obj["Id"] != null ? obj["Id"].Value<int>() : 0;
                            message = obj["Message"] != null ? obj["Message"].Value<string>() : "Saved.";
                        }
                        else if (token.Type == JTokenType.Integer)
                        {
                            id = token.Value<int>();
                            isSuccess = id > 0;
                            message = "Saved.";
                        }
                        else if (token.Type == JTokenType.Boolean)
                        {
                            isSuccess = token.Value<bool>();
                            id = userData.Id;
                            message = isSuccess ? "Saved." : "Failed.";
                        }

                        if (id == 0 && userData.Id > 0) id = userData.Id;

                        return Json(new { IsSuccess = isSuccess, Id = id, Message = message }, JsonRequestBehavior.AllowGet);
                    }

                    //return Json(new { IsSuccess = false, Id = 0, Message = "API returned: " + response.StatusCode + " - " + jsonString }, JsonRequestBehavior.AllowGet);
                    string errorMessage = "API Error: " + response.StatusCode;
                    try
                    {
                        var errObj = JObject.Parse(jsonString);
                        if (errObj["Message"] != null) errorMessage = errObj["Message"].Value<string>();
                    }
                    catch { }
                    return Json(new { IsSuccess = false, Id = 0, Message = errorMessage }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Id = 0, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ── GET USER BY ID ───────────────────────────────────────────────
        [HttpPost]
        public JsonResult GetUserById(User model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetUserById/" + model.Id).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var user = JsonConvert.DeserializeObject<User>(jsonString);

                        // Get assigned divisions
                        try
                        {
                            var divResponse = hcf.client.GetAsync("User/GetAssignUserDivision/" + model.Id).Result;
                            if (divResponse.StatusCode == HttpStatusCode.OK)
                            {
                                string divJson = divResponse.Content.ReadAsStringAsync().Result;
                                var divList = JsonConvert.DeserializeObject<List<UserDivision>>(divJson);
                                if (divList != null && divList.Count > 0)
                                {
                                    user.DivisionIds = divList.Select(d => d.DivisionId).ToList();
                                    user.DivisionId = user.DivisionIds.FirstOrDefault();
                                }
                            }
                        }
                        catch { }

                        // Get assigned sites
                        try
                        {
                            var siteResponse = hcf.client.GetAsync("User/GetAssignUserSite/" + model.Id).Result;
                            if (siteResponse.StatusCode == HttpStatusCode.OK)
                            {
                                string siteJson = siteResponse.Content.ReadAsStringAsync().Result;
                                var siteList = JsonConvert.DeserializeObject<List<UserSite>>(siteJson);
                                if (siteList != null && siteList.Count > 0)
                                {
                                    user.SiteIds = siteList.Select(s => s.SiteId).ToList();
                                    user.SiteId = user.SiteIds.FirstOrDefault();
                                }
                            }
                        }
                        catch { }

                        // ✅ NEW: Get assigned sections
                        try
                        {
                            var secResponse = hcf.client.GetAsync("UserSection/GetByUserId/UserId/" + model.Id).Result;
                            if (secResponse.StatusCode == HttpStatusCode.OK)
                            {
                                string secJson = secResponse.Content.ReadAsStringAsync().Result;
                                var secList = JsonConvert.DeserializeObject<List<UserSection>>(secJson);
                                if (secList != null && secList.Count > 0)
                                {
                                    user.SectionIds = secList.Select(s => s.SectionId).ToList();
                                }
                            }
                        }
                        catch { /* Section fetch failed — continue without */ }

                        return Json(user, JsonRequestBehavior.AllowGet);
                    }

                    return Json(null, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(null, JsonRequestBehavior.AllowGet);
            }
        }

        // Update Holder (FirstName, LastName only)
        [HttpPost]
        public JsonResult UpdateUserName(User mUser)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new { mUser.Id, mUser.FirstName, mUser.LastName });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("User/UpdateUserName", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Json(new { IsSuccess = true }, JsonRequestBehavior.AllowGet);
                    }
                }
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
        }

        // ── DELETE USER ──────────────────────────────────────────────────
        [HttpPost]
        public JsonResult DeleteUser(User mUser)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"User/DeleteFRSUserById/" + mUser.Id).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Json(new { IsSuccess = true }, JsonRequestBehavior.AllowGet);
                    }
                }
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
        }

        // ── TOGGLE USER STATUS ───────────────────────────────────────────
        [HttpPost]
        public JsonResult ToggleUserStatus(User mUser)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new { mUser.Id, mUser.IsActive });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("User/ToggleUserStatus", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Json(new { IsSuccess = true }, JsonRequestBehavior.AllowGet);
                    }
                }
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception)
            {
                return Json(new { IsSuccess = false }, JsonRequestBehavior.AllowGet);
            }
        }

        // ── SAVE DIVISION/SITE MAPPING ───────────────────────────────────
        [HttpPost]
        public JsonResult SaveUserDivisionSiteMapping(UserDivisionSiteMappingRequest model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(model);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("User/SaveUserDivisionSiteMapping", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        return Json(new
                        {
                            IsSuccess = result.IsSuccess,
                            Message = result.Message
                        }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            return Json(new { IsSuccess = false, Message = "Failed to save mappings." }, JsonRequestBehavior.AllowGet);
        }

        // ── RESET PASSWORD ───────────────────────────────────────────────
        [HttpPost]
        public JsonResult ResetPassword(ChangePassword model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(new
                    {
                        Id = model.Id,
                        OldPassword = "",
                        NewPassword = model.NewPassword,
                        ConfirmPassword = model.ConfirmPassword
                    });
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    var response = hcf.client.PostAsync("User/ChangePasswordFRS", str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        try
                        {
                            var token = JToken.Parse(jsonString);
                            if (token.Type == JTokenType.Object)
                            {
                                var obj = (JObject)token;
                                return Json(new
                                {
                                    IsSuccess = obj["IsSuccess"] != null ? obj["IsSuccess"].Value<bool>() : false,
                                    Message = obj["Message"] != null ? obj["Message"].Value<string>() : ""
                                }, JsonRequestBehavior.AllowGet);
                            }
                        }
                        catch
                        {
                            return Json(new { IsSuccess = false, Message = jsonString }, JsonRequestBehavior.AllowGet);
                        }
                    }

                    return Json(new { IsSuccess = false, Message = "API returned: " + response.StatusCode + " - " + jsonString }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // PRIVATE HELPER METHODS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get all divisions by Zone ID via API
        /// </summary>
        private List<Division> GetByZoneId(int zoneId)
        {
            List<Division> mDivisions = new List<Division>();
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
                }
            }
            catch (Exception ex) { }
            return mDivisions;
        }

        /// <summary>
        /// Get sites by Zone ID and Division ID via API
        /// </summary>
        private List<Domain.Site> GetSitesBy(int zoneId, int divisionId)
        {
            var sites = new List<Domain.Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/GetSites/{0}/{1}", zoneId, divisionId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Domain.Site>>(jsonString);
                    }
                }
            }
            catch (Exception ex) { }
            return sites;
        }

        /// <summary>
        /// Get all active sections via API — calls Section/GetAll
        /// Returns dynamic list to handle varying property names (Name vs SectionName)
        /// </summary>
        private List<Section> GetAllSections()
        {
            List<Section> sections = new List<Section>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Section/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sections = JsonConvert.DeserializeObject<List<Section>>(jsonString);
                    }
                }
            }
            catch (Exception ex) { }
            return sections ?? new List<Section>();
        }

        [HttpPost]
        public JsonResult UpdateUser(User userData)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    userData.CreatedBy = ClsHttpContent.LoginUser.Id;
                    var jsonStr = JsonConvert.SerializeObject(userData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    // Call the Web API UpdateUser endpoint (POST)
                    HttpResponseMessage response = hcf.client.PostAsync("User/UpdateUser", str).Result;

                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var token = JToken.Parse(jsonString);
                        bool isSuccess = false;
                        int id = 0;
                        string message = "";

                        if (token.Type == JTokenType.Object)
                        {
                            var obj = (JObject)token;
                            isSuccess = obj["IsSuccess"] != null ? obj["IsSuccess"].Value<bool>() : false;
                            id = obj["Id"] != null ? obj["Id"].Value<int>() : 0;
                            message = obj["Message"] != null ? obj["Message"].Value<string>() : "Updated.";
                        }
                        else if (token.Type == JTokenType.Integer)
                        {
                            id = token.Value<int>();
                            isSuccess = id > 0;
                            message = "Updated.";
                        }
                        else if (token.Type == JTokenType.Boolean)
                        {
                            isSuccess = token.Value<bool>();
                            id = userData.Id;
                            message = isSuccess ? "Updated." : "Failed.";
                        }

                        if (id == 0 && userData.Id > 0) id = userData.Id;

                        return Json(new { IsSuccess = isSuccess, Id = id, Message = message }, JsonRequestBehavior.AllowGet);
                    }

                    return Json(new { IsSuccess = false, Id = 0, Message = "API returned: " + response.StatusCode + " - " + jsonString }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { IsSuccess = false, Id = 0, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        // ── GET ALL USER CLASS TYPES ────────────────────────────────────────────────
        [HttpPost]
        public JsonResult GetAllUserClassTypes()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetAllUserClassTypes").Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var roles = JsonConvert.DeserializeObject<List<UserClassType>>(jsonString);
                        return Json(roles.Select(r => new { r.Id, r.Name }), JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch { }
            return Json(new List<object>(), JsonRequestBehavior.AllowGet);
        }
        [HttpPost]
        public JsonResult GetAllUserClassByTypeId(int userClassTypeId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetByUserClassTypeId/Id/" + userClassTypeId).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var classes = JsonConvert.DeserializeObject<List<UserClass>>(jsonString);
                        return Json(classes.Select(r => new { r.Id, r.ClassName }), JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch { }
            return Json(new List<object>(), JsonRequestBehavior.AllowGet);
        }

    }

    // ── Helper Models ────────────────────────────────────────────────
    public class SearchModel
    {
        public int ZoneId { get; set; }
        public SearchCriteriaModel SearchCriteria { get; set; }
    }

    public class SearchCriteriaModel
    {
        public List<int> ZoneIds { get; set; }
        public List<int> DivisionIds { get; set; }
        public List<int> SectionIds { get; set; }
    }

    public class ZoneIdRequest
    {
        public int ZoneId { get; set; }
    }

    public class UserSectionMappingRequest
    {
        public int UserId { get; set; }
        public List<int> SectionIds { get; set; }
    }
}
