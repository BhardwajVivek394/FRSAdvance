using Domain;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class UtilityLogController : Controller
    {
        // GET: UtilityLog
        public ActionResult Index()
        {
            return View();
        }

        public PartialViewResult _UtilityAlert(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);
                        if(mUtilityLogs != null && mUtilityLogs.Count > 0)
                        {
                            //mUtilityLogs.AddRange(GetDataLog("LocalDB"));
                        }
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
            GetUtilityLogTest();
            GetAllZones();
            GetAllDivisions();
            return PartialView("_UtilityAlert", mUtilityLogs);
        }

        public PartialViewResult _UtilitySaveJson(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);
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
            GetAllZones();
            GetAllDivisions();
            return PartialView("_SaveJsonUtilityAlert", mUtilityLogs);
        }

        public PartialViewResult _UtilityOther(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);
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
            return PartialView("_OtherUtilityAlert", mUtilityLogs);
        }

        public PartialViewResult _PLCLog(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);
                      
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
            GetUtilityLogTest();
            GetAllZones();
            GetAllDivisions();
            return PartialView("_PLCLog", mUtilityLogs);
        }

        public PartialViewResult _PointMachine(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);

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
            GetUtilityLogTest();
            GetAllZones();
            GetAllDivisions();
            return PartialView("_PointMachine", mUtilityLogs);
        }

        private List<Domain.UtilityLog> GetDataLog(string utilityType)
        {
            List<Domain.UtilityLog> mUtilityLogs = new List<Domain.UtilityLog>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLog/GetAllUtility/{0}", utilityType)).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogs = JsonConvert.DeserializeObject<List<Domain.UtilityLog>>(jsonString);
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

            return mUtilityLogs;
        }

        private void GetUtilityLogTest()
        {
            List<Domain.UtilityLogTest> mUtilityLogTests = new List<Domain.UtilityLogTest>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("UtilityLogTest/GetAll")).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mUtilityLogTests = JsonConvert.DeserializeObject<List<Domain.UtilityLogTest>>(jsonString);

                        ViewBag.UtilityLogTest = mUtilityLogTests;
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
            ViewBag.Zones = mZones;
        }

        public void GetAllDivisions()
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
            ViewBag.Divisions = mDivisions;
        }
    }
}