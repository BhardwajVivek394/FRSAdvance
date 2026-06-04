using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AdminController : Controller
    {
        // GET: FRS25/Admin
        public ActionResult Index()
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
            finally
            {
                var mUserClasses = GetUserClasses();
                if (mUserClasses != null && mUserClasses.Count > 0)
                    mUserClasses = mUserClasses.Where(x => x.ClassName != "ENERGY7 CONTROL").ToList();

                ViewBag.UserClass = new SelectList(mUserClasses, "Id", "ClassName");
            }

            return View(mUser);
        }

        [HttpPost]
        public ActionResult SaveUser(User mUser)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (ModelState.IsValid)
                {
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        mUser.CreatedBy = ClsHttpContent.LoginUser.Id;
                        mUser.IsActive = true;
                        var jsonStr = JsonConvert.SerializeObject(mUser);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("User/SaveUser"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                            if (mAPIResponse != null && mAPIResponse.IsSuccess == true)
                            {

                                data = new { type = "success", result = "User has been created." };
                            }
                            else if (mAPIResponse != null && mAPIResponse.IsSuccess == false)
                            {
                                data = new { type = "error", result = "Internal server error." };
                            }
                        }
                        else if (response.StatusCode == HttpStatusCode.Forbidden)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            var mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                            data = new { type = "error", result = mAPIResponse.Message };
                        }
                        else
                        {
                            data = new { type = "error", result = "Internal server error." };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", result = "Internal server error." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
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


    }
}