using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class UserClassController : Controller
    {
        // GET: UserClass
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult UserClassList(UserClassLister mUserClassLister)
        {
            mUserClassLister.Pager.Take = mUserClassLister.Pager.PageSize;

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserClassLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("UserClass/GetAllUserClassLister"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUserClassLister = JsonConvert.DeserializeObject<UserClassLister>(jsonString);
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
            return PartialView("_UserClassListPartial", mUserClassLister);
        }

        [HttpPost]
        public ActionResult SaveUserClass(UserClass mUserClass)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mUserClass);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("UserClass/SaveUserClass"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                        if (mAPIResponse.Value != null && mAPIResponse.IsSuccess)
                        {
                            ViewBag.Message = "User class has been saved.";
                            ViewBag.Type = "Success";
                        }
                        else
                        {
                            ViewBag.Type = "Error";
                            ViewBag.Message = "Error occured while saving UserClass!";
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
            return PartialView("_AddUserClassPartial", new UserClass());

        }

        public ActionResult GetUserClassById(int id)
        {
            UserClass mUserClass = new UserClass();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserClass/GetUserClassById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mUserClass = JsonConvert.DeserializeObject<UserClass>(jsonString);
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
            return PartialView("~/Views/UserClass/_AddUserClassPartial.cshtml", mUserClass);
        }

        public ActionResult DeleteUserClassById(int id)
        {
            APIResponse mAPIResponse = new APIResponse();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UserClass/DeleteUserClassById/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAPIResponse.IsSuccess = JsonConvert.DeserializeObject<bool>(jsonString);
                    }
                    else
                    {
                        mAPIResponse.IsSuccess = false;
                    }
                }
            }
            catch (Exception ex)
            {
                mAPIResponse.Message = ex.Message.ToString();
            }
            return Json(mAPIResponse, JsonRequestBehavior.AllowGet);
        }
    }
}