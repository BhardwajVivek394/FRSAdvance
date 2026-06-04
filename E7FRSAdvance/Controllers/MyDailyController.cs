using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.Services.Description;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class MyDailyController : Controller
    {
        // GET: MyDaily
        public ActionResult Index()
        {
            GetAllZones();
            GetAllDivisions();
            //   GetAllSites();
            return View();
        }

        public void GetAllZones()
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
                        mZones.Insert(0, new Zone { Id = 0, Name = "Select Zone" });
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
            ViewBag.Zones = new SelectList(mZones.ToList(), "Id", "Name");
        }

        public List<Division> GetAllDivisions()
        {
            List<Division> mDivisions = new List<Division>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("Division/GetAllDivisions")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mDivisions = JsonConvert.DeserializeObject<List<Division>>(jsonString);
                        mDivisions.Insert(0, new Division { Id = 0, Name = "Select Divison" });
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
            ViewBag.Divisions = new SelectList(mDivisions.ToList(), "Id", "Name");

            return mDivisions;
        }

        public JsonResult GetAllSites()
        {
            var sites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSiteByUserId/UserId/{ClsHttpContent.LoginUser.Id}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
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
            return Json(sites, JsonRequestBehavior.AllowGet);
            // ViewBag.Sites = sites;
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

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GetSites(int zoneId, int divisionId)
        {
            var sites = new List<Site>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format($"Site/GetSites/{zoneId}/{divisionId}")).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        sites = JsonConvert.DeserializeObject<List<Site>>(jsonString);
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
            return Json(sites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult GeneratePDF(SearchCriteria mSearchCriteria)
        {
            string pdfBase64 = string.Empty;
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                if (mSearchCriteria != null)
                {
                    mSearchCriteria.UserId = ClsHttpContent.LoginUser.Id;
                }
                var jsonStr = JsonConvert.SerializeObject(mSearchCriteria);
                StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                var response = hcf.client.PostAsync(String.Format("SMSLog/GenerateMyDailyPDF"), str).Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    var pdfsrc = JsonConvert.DeserializeObject<string>(jsonString);
                    if (pdfsrc.IsNotNullOrEmpty())
                    {
                        var pdfStr = ConfigurationManager.AppSettings.Get("FileBaseUrl") + pdfsrc;
                        if (pdfStr.IsNotNullOrEmpty())
                        {
                            using (WebClient client = new WebClient())
                            {
                                var bytes = client.DownloadData(pdfStr);
                                pdfBase64 = Convert.ToBase64String(bytes);
                            }
                        }
                      
                    }

                }

            }

            return Json(pdfBase64, JsonRequestBehavior.AllowGet);
        }
    }
}