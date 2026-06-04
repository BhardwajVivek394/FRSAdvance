using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System.Net;
using System;
using System.Web.Mvc;
using E7FRSAdvance.Interface;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;


namespace E7FRSAdvance.Controllers
{
    //[Authenticate]
    public class DivisionViewController : Controller
    {
        // GET: DivisionView
        private readonly IDivisionService divisionService;
        public DivisionViewController(IDivisionService divisionService)
        {
            this.divisionService = divisionService;
        }
        public ActionResult Index(int divisionId = 0)
        {
            var mDivision = new Domain.Division();
            var mDivisions = divisionService.GetAll();
            if (mDivisions != null && mDivisions.Count > 0)
            {
                ViewBag.Division = new SelectList(mDivisions, "Id", "Name");
                if (divisionId > 0)
                {
                    mDivision = mDivisions.Where(x => x.Id == divisionId).FirstOrDefault();
                }
                else
                {
                    mDivision = mDivisions.FirstOrDefault();
                }
            }

            return View(mDivision);
        }

        public ActionResult GetDivisionView(int divisionId)
        {
            Domain.DivisionView mDivisionView = new Domain.DivisionView();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var response = hcf.client.GetAsync(String.Format("DivisionView/DivisionId/{0}", divisionId)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mDivisionView = JsonConvert.DeserializeObject<Domain.DivisionView>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
            }
            return Json(mDivisionView, JsonRequestBehavior.AllowGet);
        }

        public ActionResult _SitePartial(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite == null || (mSite != null && mSite.Id <= 0))
                        {
                            mSite = new Domain.Site();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView(mSite);
        }

        public ActionResult _MISSitePartial(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            HttpCookie loginCookie = new HttpCookie("MISLoginUser");
            //Set the Cookie value.
            loginCookie.Values.Add("EmailAddress", ClsHttpContent.LoginUser.EmailAddress);
            loginCookie.Values.Add("Password", ClsHttpContent.LoginUser.Password);
            loginCookie.Expires = DateTime.Now.AddMinutes(2);
            Response.Cookies.Add(loginCookie);

            try
            {
                using (var hcf = new HttpClientFactory(baseUrl: System.Configuration.ConfigurationManager.AppSettings.Get("MISAPIBaseUrl"), token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                        if (mSite == null || (mSite != null && mSite.Id <= 0))
                        {
                            mSite = new Domain.Site();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return PartialView(mSite);
        }

        [HttpPost]
        public ActionResult GetSite(string siteName)
        {
            Domain.Site mSite = new Domain.Site();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Site/StationCode/{0}", siteName)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mSite = JsonConvert.DeserializeObject<Domain.Site>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
            }
            return Json(mSite, JsonRequestBehavior.AllowGet);
        }

        public Domain.MISUser MISLogin()
        {
            Domain.MISUser mUser = new Domain.MISUser();
            try
            {
                var mLogin = new Domain.Login();
                mLogin.EmailAddress = ClsHttpContent.LoginUser.EmailAddress;
                mLogin.Password = ClsHttpContent.LoginUser.Password;
                using (var hcf = new HttpClientFactory(baseUrl: System.Configuration.ConfigurationManager.AppSettings.Get("MISAPIBaseUrl")))
                {
                    var jsonStr = JsonConvert.SerializeObject(mLogin);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Login/Authenticate"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUser = JsonConvert.DeserializeObject<Domain.MISUser>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Type = "Error";
                ViewBag.Message = ex.Message;
            }
            return mUser;
        }
    }
}