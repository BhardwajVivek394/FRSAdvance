using Domain;
using Domain.SMS;
using E7FRSAdvance.MenuBuilder;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mLogin);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Login"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            JObject jo = JObject.Parse(jsonString);

                            bool isBlockIp = jo["IsBlockIp"]?.Value<bool>() ?? false;
                            bool isMacIdApproved = jo["IsMacIdApproved"]?.Value<bool>() ?? false;
                            bool isSendOtpToUser = jo["IsSendOtpToUser"]?.Value<bool>() ?? false;

                            if (isBlockIp)
                            {
                                ViewBag.Error = "Invalid User Id or password";
                                return View("Index", mLogin);
                            }

                            if (!isMacIdApproved)
                            {
                                ViewBag.Error = "Suspicious Login detected!! Kindly Contact Admin.";
                                return View("Index", mLogin);
                            }

                            if (isSendOtpToUser)
                            {
                                // OTP required — session is NOT set here. Only the opaque
                                // OtpSessionId (server-resolved identity) is carried forward.
                                ViewBag.PhoneNumberMasked = jo["PhoneNumberMasked"]?.Value<string>();
                                ViewBag.OtpSessionId = jo["OtpSessionId"]?.Value<string>();
                                ViewBag.IsRedirect = true;
                                ViewBag.OTPResendDelay = GetOTPResendDelay();
                                return View("Index", mLogin);
                            }

                            // No OTP required — full user + real token returned directly.
                            Domain.User mUser = jo.ToObject<Domain.User>();

                            if (mUser == null || mUser.Id <= 0)
                            {
                                ViewBag.Error = "Invalid User Id or password";
                                return View("Index", mLogin);
                            }

                            if (!mUser.IsActive)
                            {
                                ViewBag.Error = "You are not authorize to access the portal";
                                return View("Index");
                            }

                            if (mLogin.RememberMe)
                            {
                                HttpCookie loginCookie = new HttpCookie("LoginUser");
                                loginCookie.Values.Add("EmailAddress", mLogin.EmailAddress);
                                loginCookie.Values.Add("Password", mLogin.Password);
                                loginCookie.Values.Add("RememberMe", Convert.ToString(mLogin.RememberMe));
                                loginCookie.Expires = DateTime.Now.AddDays(15);
                                Response.Cookies.Add(loginCookie);
                            }
                            else
                            {
                                var loginCookie = new HttpCookie("LoginUser");
                                loginCookie.Expires = DateTime.Now.AddDays(-1);
                                Response.Cookies.Add(loginCookie);
                            }

                            ClsHttpContent.LoginUser = mUser;
                            int annexureId = (int)E7FRSAdvance.Utility.Utility.Annexure.FRS_25;
                            ClsHttpContent.LoginUser.AnnexureId = annexureId;
                            MenuFactory menuFactory = new MenuFactory(ClsHttpContent.LoginUser);
                            IMenuBuilder menuBuilder = menuFactory.CreateInstanceAnnexure(annexureId);
                            ClsHttpContent.FRS25Menus = menuBuilder.Build(ClsHttpContent.LoginUser);

                            return RedirectToAction("Index", "Home", new { area = "FRS25" });
                        }
                        else if (response.StatusCode == (HttpStatusCode)429)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            ViewBag.Error = JsonConvert.DeserializeObject<string>(jsonString);
                            return View("Index", mLogin);
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

        /// <summary>
        /// Resend OTP during the pre-auth OTP step. No session/token exists yet —
        /// identity is resolved server-side from the OtpSessionId.
        /// </summary>
        public JsonResult ResendOtp(string otpSessionId)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var payload = new { OtpSessionId = otpSessionId };
                    var jsonStr = JsonConvert.SerializeObject(payload);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("SMS/ResendOtp", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        data = new { type = "success", message = "OTP sent successfully" };
                    }
                    else
                    {
                        data = new { type = "error", message = "Failed to resend OTP" };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", message = "Something went wrong. Please try again later." };
            }

            return Json(data);
        }

        public int GetOTPResendDelay()
        {
            int value = 0;
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var response = hcf.client.GetAsync(String.Format($"AppConfig/GetSecurityParams")).Result;
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

        /// <summary>
        /// Verifies the OTP for the pre-auth session. On success, the API returns the
        /// full User + real Token — THIS is where the session gets set, not before.
        /// </summary>
        public JsonResult VerifyOtp(string otpSessionId, string otp)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var payload = new { OtpSessionId = otpSessionId, Otp = otp };
                    var jsonStr = JsonConvert.SerializeObject(payload);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("SMS/VerifyOtpWithSession"), str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        JObject jo = JObject.Parse(jsonString);
                        bool success = jo["Success"]?.Value<bool>() ?? false;
                        string message = jo["Message"]?.Value<string>();

                        if (success)
                        {
                            // OTP verified — set the real session now, from the User
                            // object this response returned, not from a prior call.
                            Domain.User verifiedUser = jo["User"]?.ToObject<Domain.User>();
                            if (verifiedUser != null && verifiedUser.Id > 0)
                            {
                                ClsHttpContent.LoginUser = verifiedUser;
                                int annexureId = (int)E7FRSAdvance.Utility.Utility.Annexure.FRS_25;
                                ClsHttpContent.LoginUser.AnnexureId = annexureId;
                                MenuFactory menuFactory = new MenuFactory(ClsHttpContent.LoginUser);
                                IMenuBuilder menuBuilder = menuFactory.CreateInstanceAnnexure(annexureId);
                                ClsHttpContent.FRS25Menus = menuBuilder.Build(ClsHttpContent.LoginUser);

                                data = new
                                {
                                    type = "success",
                                    message = message,
                                    redirectUrl = Url.Action("Index", "Home", new { area = "FRS25" })
                                };
                            }
                            else
                            {
                                data = new { type = "error", message = "Verification succeeded but session could not be established." };
                            }
                        }
                        else
                        {
                            data = new { type = "error", message = message };
                        }
                    }
                    else
                    {
                        data = new { type = "error", message = "Session expired. Please log in again." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", message = "Something went wrong. Please try again later." };
            }

            return Json(data);
        }

        // Forgot password now lives inline on the login page (like 616).
        // Old links/bookmarks land on Login/Index with the forgot panel open.
        public ActionResult ForgotPassword()
        {
            return RedirectToAction("Index", "Login", new { area = "FRS25", fp = 1 });
        }

        // =====================================================
        // FORGOT PASSWORD — ported from 616 (phone → OTP → pick email + new password)
        // =====================================================

        [HttpPost]
        public JsonResult ForgotPasswordSendOtp(string phoneNumber)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var payload = new { PhoneNumber = phoneNumber };
                    var jsonStr = JsonConvert.SerializeObject(payload);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Login/ForgotPasswordSendOtp", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<dynamic>(jsonString);
                        bool success = result.Success;

                        if (success)
                        {
                            data = new
                            {
                                type = "success",
                                message = (string)result.Message,
                                phoneNumber = (string)result.PhoneNumber
                            };
                        }
                        else
                        {
                            data = new { type = "error", message = (string)result.Message };
                        }
                    }
                    else
                    {
                        data = new { type = "error", message = "Unable to process your request. Please try again." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", message = "Something went wrong. Please try again later." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult ForgotPasswordVerifyOtp(string mobileNumber, string otp)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var payload = new { Mobile = mobileNumber, Otp = otp };
                    var jsonStr = JsonConvert.SerializeObject(payload);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Login/ForgotPasswordVerifyOtp", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        // Same shape as 616's Domain.Dto.ForgotPasswordOtpResult
                        var result = JObject.Parse(jsonString);
                        var emails = result["Emails"] as JArray;
                        data = new
                        {
                            Success = (bool?)result["Success"] ?? false,
                            Message = (string)result["Message"],
                            ResetToken = (string)result["ResetToken"],
                            Emails = emails != null ? emails.Select(e => (string)e).ToArray() : new string[0]
                        };
                        return Json(data, JsonRequestBehavior.AllowGet);
                    }
                    else
                    {
                        data = new { type = "error", Success = false, Message = "Verification failed. Please try again." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", Success = false, Message = "Something went wrong. Please try again later." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult ForgotPasswordResendOtp(string mobileNumber)
        {
            dynamic data = new ExpandoObject();
            try
            {
                using (var hcf = new HttpClientFactory())
                {
                    var response = hcf.client.GetAsync($"Login/ForgotPasswordResendOtp/MobileNumber/{mobileNumber}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var respon = JsonConvert.DeserializeObject<dynamic>(jsonString);
                        if (respon.type == "success")
                        {
                            data = new { type = "success", message = "OTP sent successfully" };
                        }
                        else if (respon.type == "error")
                        {
                            data = new { type = "error", message = "Failed to send OTP" };
                        }
                    }
                    else
                    {
                        data = new { type = "error", message = "Failed to resend OTP" };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", message = "Something went wrong. Please try again later." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        public JsonResult ResetPassword(string emailAddress, string newPassword, string confirmPassword, string resetToken, string phoneNumber)
        {
            dynamic data = new ExpandoObject();
            try
            {
                if (string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
                {
                    data = new { type = "error", message = "Please fill in both password fields." };
                    return Json(data, JsonRequestBehavior.AllowGet);
                }

                if (newPassword != confirmPassword)
                {
                    data = new { type = "error", message = "Passwords do not match." };
                    return Json(data, JsonRequestBehavior.AllowGet);
                }

                if (newPassword.Length < 6)
                {
                    data = new { type = "error", message = "Password must be at least 6 characters." };
                    return Json(data, JsonRequestBehavior.AllowGet);
                }

                using (var hcf = new HttpClientFactory())
                {
                    var payload = new
                    {
                        EmailAddress = emailAddress,
                        PhoneNumber = phoneNumber,
                        NewPassword = newPassword,
                        ConfirmPassword = confirmPassword,
                        ResetToken = resetToken
                    };
                    var jsonStr = JsonConvert.SerializeObject(payload);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Login/ResetPassword", str).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<dynamic>(jsonString);
                        bool success = result.Success;
                        if (success)
                        {
                            data = new { type = "success", message = (string)result.Message };
                        }
                        else
                        {
                            data = new { type = "error", message = (string)result.Message };
                        }
                    }
                    else
                    {
                        data = new { type = "error", message = "Unable to reset password. Please try again." };
                    }
                }
            }
            catch (Exception ex)
            {
                data = new { type = "error", message = "Something went wrong. Please try again later." };
            }
            return Json(data, JsonRequestBehavior.AllowGet);
        }

        public string GetIPAddress()
        {
            string iPAddress = "";
            iPAddress = System.Web.HttpContext.Current.Request.UserHostAddress.ToString();
            return iPAddress;
        }
    }
}