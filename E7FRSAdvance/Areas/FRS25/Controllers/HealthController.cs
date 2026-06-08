using Domain;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class HealthController : Controller
    {
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IFRSAlertService _alertService;

        public HealthController(
            ISiteService siteService,
            IZoneService zoneService,
            IDivisionService divisionService,
            IFRSAlertService alertService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _alertService = alertService;
        }

        public ActionResult Index()
        {
            ViewBag.Sites = new SelectList(_siteService.GetAll(), "Id", "Name");
            ViewBag.Zones = new SelectList(_zoneService.GetAllZones(), "Id", "Name");
            ViewBag.Divisions = new SelectList(_divisionService.GetAllDivisions(), "Id", "Name");

            var mqttDetailList = GetMQTTDetailList();
            ViewBag.MQTTDetail = mqttDetailList != null ? mqttDetailList.mQTTDetailWeb : null;

            return View();
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            try
            {
                return Json(_divisionService.GetByZoneId(zoneId), JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new { success = false, message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();

            try
            {
                mSites = _siteService.GetBy(divisionId);
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }

            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetHealthMqttTopics(int siteId = 0)
        {
            try
            {
                var result = new List<object>();

                if (siteId > 0)
                {
                    // IMPORTANT: selected site basepath is taken only from _siteService.Get(siteId).
                    var site = _siteService.Get(siteId);
                    AddSiteHealthTopics(result, site);

                    return Json(new { success = true, data = result }, JsonRequestBehavior.AllowGet);
                }

                // No site selected: get all site IDs, then call _siteService.Get(id) for each site
                // so basepath is always taken from the full Site object returned by Get(siteId).
                var allSites = _siteService.GetAll() ?? new List<Domain.Site>();

                foreach (var basicSite in allSites)
                {
                    if (basicSite == null || basicSite.Id <= 0)
                        continue;

                    var site = _siteService.Get(basicSite.Id);
                    AddSiteHealthTopics(result, site);
                }

                return Json(new { success = true, data = result }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    data = new List<object>()
                }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult GetAlertLive(int siteId = 0)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Json(new { success = false, error = "ProxyBaseUrl not configured in AppSettings." },
                        JsonRequestBehavior.AllowGet);
                }

                baseUrl = baseUrl.TrimEnd('/');

                string apiUrl = siteId > 0
                    ? string.Format("{0}/api/AlertLive?siteId={1}&alertState=Active", baseUrl, siteId)
                    : string.Format("{0}/api/AlertLive?alertState=Active", baseUrl);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var response = client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        Response.StatusCode = (int)response.StatusCode;
                        return Json(new { success = false, error = "AlertLive API returned: " + response.StatusCode },
                            JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new { success = false, error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private void AddSiteHealthTopics(List<object> result, Domain.Site site)
        {
            if (site == null)
                return;

            var basePath = GetSiteBasePath(site);

            if (string.IsNullOrWhiteSpace(basePath))
                return;

            basePath = basePath.Trim().Trim('/');

            result.Add(new
            {
                SiteId = site.Id,
                SiteName = site.Name,
                BasePath = basePath,
                ZoneId = GetSiteReflectedString(site, new[] { "ZoneId", "zoneId", "Zone_Id" }),
                ZoneName = GetSiteReflectedString(site, new[] { "ZoneName", "zoneName", "Zone" }),
                DivisionId = GetSiteReflectedString(site, new[] { "DivisionId", "divisionId", "Division_Id" }),
                DivisionName = GetSiteReflectedString(site, new[] { "DivisionName", "divisionName", "Division" }),
                Topics = new[]
                {
                    basePath + "/mqtt/dl/heartbeat",
                    basePath + "/alerthealth",
                    "datareceiver/" + site.Id + "/health"
                }
            });
        }

        private string GetSiteReflectedString(Domain.Site site, string[] possibleNames)
        {
            if (site == null)
                return string.Empty;

            foreach (var name in possibleNames)
            {
                var prop = site.GetType().GetProperty(name);
                if (prop == null)
                    continue;

                var value = prop.GetValue(site, null);
                var text = value == null ? string.Empty : Convert.ToString(value);

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        private string GetSiteBasePath(Domain.Site site)
        {
            if (site == null)
                return string.Empty;

            // If your Domain.Site has a direct BasePath property, this helper will use it.
            // Reflection is used so the controller still compiles if your model property is named
            // Basepath/basepath/MqttBasePath/etc.
            var possibleNames = new[]
            {
                "MQTTBasePath",
                "MqttBasePath",
                "BasePath",
                "Basepath",
                "basepath",
                "MQTTBase",
                "MqttBase",
                "MQTTPath",
                "MqttPath",
                "MQTTTopicBase",
                "MqttTopicBase",
                "TopicBase",
                "MqttTopic",
                "MQTTTopic"
            };

            foreach (var name in possibleNames)
            {
                var prop = site.GetType().GetProperty(name);
                if (prop == null)
                    continue;

                var value = prop.GetValue(site, null);
                var text = value == null ? string.Empty : Convert.ToString(value);

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return string.Empty;
        }

        public MQTTDetailList GetMQTTDetailList()
        {
            MQTTDetailList mMQTTDetail = new MQTTDetailList();

            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("User/GetMQTTDetail").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mMQTTDetail = JsonConvert.DeserializeObject<MQTTDetailList>(jsonString);
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

            return mMQTTDetail;
        }
    }
}
