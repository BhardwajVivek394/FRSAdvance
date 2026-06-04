using Domain;
using Domain.SMS;
using E7FRSAdvance.MenuBuilder;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.UI.WebControls;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public class LoginController : Controller
    {
        // GET: FRS25/Login     
        public ActionResult Index()
        {

            var mLogin = new Domain.Login();
            try
            {

                if (ClsHttpContent.LoginUser != null)
                {
                    LoginHistory mLoginHistory = new LoginHistory();
                    mLoginHistory.IpAddress = GetIPAddress();
                    mLoginHistory.UserId = ClsHttpContent.LoginUser.Id;
                    User mUser = new User();
                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mLoginHistory);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Login/LogOut"), str).Result;
                    }
                }
                ClsHttpContent.LoginUser = null;
                HttpCookie ckLoginUser = Request.Cookies["LoginUser"];
                if (ckLoginUser != null)
                {
                    mLogin.EmailAddress = ckLoginUser.Values["EmailAddress"];
                    mLogin.Password = ckLoginUser.Values["Password"];
                    mLogin.RememberMe = Convert.ToBoolean(ckLoginUser.Values["RememberMe"]);
                }

                foreach (var key in TempData.Keys.ToList())
                {
                    TempData.Remove(key);
                }
            }
            catch (Exception ex)
            {
                var Message = ex.Message;
            }
            return View(mLogin);
        }

        [HttpPost]
        public ActionResult Index(Domain.Login mLogin)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    mLogin.IsSendOTP = true;
                    mLogin.IpAddress = GetIPAddress();
                    mLogin.Source = "Website";

                    if (Request.Cookies["deviceId"] != null)
                    {
                        mLogin.MacAddress = Request.Cookies["deviceId"].Value.ToString();
                    }
                    else
                    {
                        Response.Cookies["deviceId"].Value = Guid.NewGuid().ToString();
                        Response.Cookies["deviceId"].Expires = DateTime.Now.AddYears(1);
                        mLogin.MacAddress = Request.Cookies["deviceId"].Value.ToString();
                    }

                    Domain.User mUser = new Domain.User();
                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mLogin);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Login/Authenticate"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mUser = JsonConvert.DeserializeObject<Domain.User>(jsonString);
                            if (mUser != null && !mUser.IsBlockIp)
                            {
                                if (mUser.IsMacIdApproved)
                                {
                                    if (mUser != null && mUser.Id > 0)
                                    {
                                        if (!mUser.IsActive)
                                        {
                                            ViewBag.Error = "You are not authorize to access the portal";
                                            return View("Index");
                                        }

                                        if (mLogin.RememberMe)
                                        {
                                            HttpCookie loginCookie = new HttpCookie("LoginUser");
                                            //Set the Cookie value.
                                            loginCookie.Values.Add("EmailAddress", mLogin.EmailAddress);
                                            loginCookie.Values.Add("Password", mLogin.Password);
                                            loginCookie.Values.Add("RememberMe", Convert.ToString(mLogin.RememberMe));
                                            //Set the Expiry date.
                                            loginCookie.Expires = DateTime.Now.AddDays(15);
                                            //Add the Cookie to Browser.
                                            Response.Cookies.Add(loginCookie);
                                        }
                                        else
                                        {
                                            var loginCookie = new HttpCookie("LoginUser");
                                            //Set the Expiry date.
                                            loginCookie.Expires = DateTime.Now.AddDays(-1);
                                            //Add the Cookie to Browser.
                                            Response.Cookies.Add(loginCookie);
                                        }
                                        ClsHttpContent.LoginUser = mUser;
                                        int annexureId = (int)E7FRSAdvance.Utility.Utility.Annexure.FRS_25;
                                        ClsHttpContent.LoginUser.AnnexureId = annexureId;
                                        MenuFactory menuFactory = new MenuFactory(ClsHttpContent.LoginUser);
                                        IMenuBuilder menuBuilder = menuFactory.CreateInstanceAnnexure(annexureId);
                                        ClsHttpContent.FRS25Menus = menuBuilder.Build(ClsHttpContent.LoginUser);



                                        if (mUser.IsSendOtpToUser)
                                        {
                                            ViewBag.MobileNumber = mUser.PhoneNumber;
                                            ViewBag.MacAddress = mLogin.MacAddress;
                                            ViewBag.UserId = mUser.Id;
                                            ViewBag.IsRedirect = true;
                                            ViewBag.OTPResendDelay = GetOTPResendDelay();

                                            return View("Index", mLogin);
                                        }
                                        else
                                        {
                                            return RedirectToAction("Index", "Home", new { area = "FRS25" });
                                        }


                                        //if (mUser.RoleId == (int)E7FRSAdvance.Utility.Utility.Role.Admin)
                                        //    return RedirectToAction("Index", "Home", new { area = "FRS25" });
                                        //else
                                        //{
                                        //    return RedirectToAction("Index", "Home", new { area = "FRS25" });
                                        //}
                                    }
                                    else
                                    {
                                        ViewBag.Error = "Invalid User name or password";
                                        return View("Index", mLogin);
                                    }
                                }
                                else
                                {
                                    ViewBag.Error = "Suspicious Login detected!! Kindly Contact Admin.";
                                    return View("Index", mLogin);
                                }
                            }
                            else
                            {
                                ViewBag.Error = "Invalid User name or password";
                                return View("Index", mLogin);
                            }
                        }
                        else
                        {
                            ViewBag.Error = "Internal server error";
                            return View("Index", mLogin);
                        }
                    }
                }
                return View("Index", mLogin);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View("Index", mLogin);
            }
        }

        public void UpdateLastOTPTime(Domain.LoginMacAddress loginMacAddress)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(loginMacAddress);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Login/UpdateLastOTPTime"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        //isSent = JsonConvert.DeserializeObject<bool>(jsonString);
                        //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                    }
                }
            }
            catch (Exception ex)
            {
            }

        }

        public JsonResult ResendOtp(string mobileNumber)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"SMS/SendSMS/MobileNumber/{mobileNumber}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", message = "OTP send successfully" };
                        //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                    }
                    else
                    {
                        data = new { type = "error", message = "Internal" };
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Json(data);
        }

        public int GetOTPResendDelay()
        {
            int value = 0;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GroupName/SecurityParams/AppKey/OTPResendDelay")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mAppConfig = JsonConvert.DeserializeObject<AppConfig>(jsonString);
                        if (mAppConfig != null && mAppConfig.Id > 0 && mAppConfig.AppValue.IsNotNullOrEmpty())
                        {
                            value = Convert.ToInt32(mAppConfig.AppValue);
                        }
                        //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return value;
        }

        public JsonResult VerifyOtp(OtpData otpData)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(otpData);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMS/VerifyOtp"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var dsds = JsonConvert.DeserializeObject<Domain.SMS.OtpResult>(jsonString);
                        if (dsds.Success)
                        {
                            //UpdateLastOTPTime();
                            data = new { type = "success", message = dsds.Message };
                        }
                        else
                        {

                            data = new { type = "error", message = dsds.Message };
                        }
                        //sites.Insert(0, new Domain.Site { Id = 0, Name = "Select site" });
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return Json(data);
        }

        public string GetIPAddress()
        {
            string iPAddress = "";
            iPAddress = System.Web.HttpContext.Current.Request.UserHostAddress.ToString();
            return iPAddress;
        }
    }
}