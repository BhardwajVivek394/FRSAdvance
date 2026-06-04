using Domain;
using E7FRSAdvance.MenuBuilder;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.UI.WebControls;


namespace E7FRSAdvance.Controllers
{
    public class LoginController : Controller
    {
        // GET: Login

        public ActionResult FRS25()
        {
            return RedirectToAction("Index", "Login", new { area = "FRS25" });
        }

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

                    User mUser = new User();
                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mLogin);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Login/Authenticate"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mUser = JsonConvert.DeserializeObject<User>(jsonString);
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
                                            //loginCookie.Values.Add("HostId", Convert.ToString(ClsHttpContent.hostDetails.Id);
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
                                        MenuFactory menuFactory = new MenuFactory(ClsHttpContent.LoginUser);
                                        IMenuBuilder menuBuilder = menuFactory.CreateInstance();
                                        ClsHttpContent.Menus = menuBuilder.Build(ClsHttpContent.LoginUser);
                                        if (mUser.RoleId == (int)Utility.Utility.Role.Admin)
                                        {
                                            return RedirectToAction("Index", "Zone");
                                        }
                                        else
                                        {
                                            return RedirectToAction("Index", "Site");
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


        // [HttpPost]
        public ActionResult LoginWithAnnexure(int annexureId)
        {
            ViewBag.AnnexureId = annexureId;
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetUserById/{0}", ClsHttpContent.LoginUser.Id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var mUser = JsonConvert.DeserializeObject<User>(jsonString);
                        if (mUser != null && mUser.Id > 0)
                        {
                            ClsHttpContent.LoginUser.AnnexureId = annexureId;
                            MenuFactory menuFactory = new MenuFactory(ClsHttpContent.LoginUser);
                            IMenuBuilder menuBuilder = menuFactory.CreateInstanceAnnexure(annexureId);
                            var menus = menuBuilder.Build(ClsHttpContent.LoginUser);  // ✅ Store in variable first added by vivek
                            //ClsHttpContent.Menus = menuBuilder.Build(ClsHttpContent.LoginUser);
                            if (annexureId == E7FRSAdvance.Utility.Utility.Annexure.Basic.GetHashCode())
                            {
                                //ClsHttpContent.Menus = menus;  // ✅ Main website menus
                                if (mUser.RoleId == (int)Utility.Utility.Role.Admin)
                                    return RedirectToAction("Index", "Zone");
                                else
                                    return RedirectToAction("Index", "Site");
                            }
                            else if (annexureId == E7FRSAdvance.Utility.Utility.Annexure.FRS_25.GetHashCode())
                            {
                                ClsHttpContent.FRS25Menus = menus;  // ✅ CHANGED: FRS25 menus added by vivek
                                if (mUser.RoleId == (int)Utility.Utility.Role.Admin)
                                    return RedirectToAction("Index", "Home", new { area = "FRS25" });
                                else
                                    return RedirectToAction("Index", "Home", new { area = "FRS25" });

                            }
                            else if (annexureId == E7FRSAdvance.Utility.Utility.Annexure.FRS_25Advanced.GetHashCode())
                            {
                                //ClsHttpContent.FRS25Menus = menus;  // ✅ CHANGED: FRS25 menus
                                if (mUser.RoleId == (int)Utility.Utility.Role.Admin)
                                    return RedirectToAction("IndexAdvanced", "Home", new { area = "FRS25" });
                                else
                                {
                                    return RedirectToAction("IndexAdvanced", "Home", new { area = "FRS25" });
                                }
                            }

                        }
                    }
                    else
                    {
                        return View("Index");
                    }
                }
            }
            catch (Exception)
            {
                return View("Index");
            }
            return View("Index");
        }


        [System.Runtime.InteropServices.DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref int PhyAddrLen);

        /// <summary>
        /// Gets the MAC address (<see cref="PhysicalAddress"/>) associated with the specified IP.
        /// </summary>
        /// <param name="ipAddress">The remote IP address.</param>
        /// <returns>The remote machine's MAC address.</returns>
        public static PhysicalAddress GetMacAddress(IPAddress ipAddress)
        {
            const int MacAddressLength = 6;
            int length = MacAddressLength;
            var macBytes = new byte[MacAddressLength];
            SendARP(BitConverter.ToInt32(ipAddress.GetAddressBytes(), 0), 0, macBytes, ref length);
            return new PhysicalAddress(macBytes);
        }

        public ActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        public ActionResult ForgotPassword(Domain.Login mLogin)
        {
            try
            {
                APIResponse mAPIResponse = new APIResponse();
                ModelState.Remove("Password");
                if (ModelState.IsValid)
                {
                    using (var hcf = new HttpClientFactory())
                    {
                        var jsonStr = JsonConvert.SerializeObject(mLogin);
                        StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                        var response = hcf.client.PostAsync(String.Format("Login/ForgotPassword"), str).Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            mAPIResponse = JsonConvert.DeserializeObject<APIResponse>(jsonString);
                            if (mAPIResponse.IsSuccess)
                            {
                                ViewBag.Success = "Password has been sent to your Registered Email.";
                            }
                            else
                            {
                                ViewBag.Error = mAPIResponse.Message;
                            }
                        }
                        else
                        {
                            string jsonString = response.Content.ReadAsStringAsync().Result;
                            ViewBag.Error = "Email id does not exist";
                            return View("ForgotPassword", mLogin);
                        }
                    }
                    return View("ForgotPassword", mLogin);
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return View("ForgotPassword", mLogin);
        }

        public string GetIPAddress()
        {
            string iPAddress = "";
            iPAddress = System.Web.HttpContext.Current.Request.UserHostAddress.ToString();
            return iPAddress;
        }
    }
}