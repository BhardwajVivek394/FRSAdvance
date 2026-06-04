using E7FRSAdvance.Utility;
using System.Net;
using System;
using System.Web.Mvc;
using Domain;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Web.UI.WebControls;
using System.Web;
using System.Linq;
using System.Web.Security;
using E7FRSAdvance.Interface;
using System.Web.Caching;
using E7FRSAdvance.Utility;

namespace E7FRSAdvance.Controllers
{
    public class HomeController : Controller
    {
        private readonly IFRSAlertService _frsAlertService;
        public HomeController(IFRSAlertService frsAlertService)
        {
            _frsAlertService = frsAlertService;
        }
        public ActionResult Index()
        {
            return RedirectToAction("Index", "Login", new { area = "FRS25" });
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        public JsonResult GetActiveAlertCount(Domain.FRSAlertLister mFRSAlertLister)
        {
            int count = 0;
            try
            {
                mFRSAlertLister = _frsAlertService.GetWithOutAcknowledgementAlert(mFRSAlertLister);
                if (mFRSAlertLister != null && mFRSAlertLister.mFRSAlerts != null)
                {

                    Dictionary<int, bool> userInfo = new Dictionary<int, bool>();


                    var c = Request.Cookies["ActiveAlertCount"];
                    if (c != null)
                    {
                        var token = c.Values["UserInfo"];
                        var json = (string)HttpRuntime.Cache.Get("ActiveAlertCount:" + token);
                        if (json.IsNotNullOrEmpty())
                        {
                            var cookieuserInfo = JsonConvert.DeserializeObject<Dictionary<int, bool>>(json);

                            if (mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                            {
                                foreach (var id in mFRSAlertLister.mFRSAlerts.Select(x => x.Id).ToList())
                                {
                                    var valcookie = cookieuserInfo.Where(x => x.Key == id).FirstOrDefault();
                                    if (valcookie.Key > 0)
                                    {
                                        userInfo.Add(id, valcookie.Value);
                                    }
                                    else
                                    {
                                        userInfo.Add(id, false);
                                    }

                                }
                            }
                            count = userInfo.Where(x => !x.Value).Count();
                        }


                    }
                    else
                    {

                        count = mFRSAlertLister.mFRSAlerts.Count();
                        if (mFRSAlertLister.mFRSAlerts != null && mFRSAlertLister.mFRSAlerts.Count > 0)
                        {
                            foreach (var id in mFRSAlertLister.mFRSAlerts.Select(x => x.Id).ToList())
                            {
                                userInfo.Add(id, false);
                            }
                        }
                        string dictJson = JsonConvert.SerializeObject(userInfo);

                        var token = Guid.NewGuid().ToString("N");

                        // Example using ASP.NET Cache (you can use Session/DB/Redis instead)
                        HttpRuntime.Cache.Insert(
                            key: "ActiveAlertCount:" + token,
                            value: dictJson,
                            dependencies: null,
                            absoluteExpiration: DateTime.Now.AddDays(1),
                            slidingExpiration: Cache.NoSlidingExpiration);

                        // 2) Put only the token in the cookie
                        var cookie = new HttpCookie("ActiveAlertCount");
                        cookie.Values["UserInfo"] = token;        // tiny!
                        cookie.Expires = DateTime.UtcNow.AddDays(15);
                        cookie.HttpOnly = true; cookie.Secure = true; cookie.SameSite = SameSiteMode.Lax;
                        Response.Cookies.Set(cookie);
                    }
                }

            }
            catch (Exception)
            {

            }
            return Json(count, JsonRequestBehavior.AllowGet);
        }
    }
}