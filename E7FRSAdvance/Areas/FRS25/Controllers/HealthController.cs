using Domain;
using Domain.Dto;
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
using static E7FRSAdvance.Utility.Utility;

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
                     $"/{basePath}/mqtt/dl/heartbeat",
                     $"/{basePath}/alerthealth",
                     $"datareceiver/{site.Id}/health",
                     $"debouncer/{site.Id}/health",
                     $"pointServices/{site.Id}/health"
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

        /// <summary>
        /// Proxies Asset/GetAllSiteDetailsBySiteId to retrieve all assets and their
        /// attributes (sensors) for a given site.  The response is slim-projected so
        /// only the fields the Health dashboard JS actually needs are serialised.
        /// </summary>
        [HttpPost]
        public JsonResult GetSiteAssets(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var payload = new
                    {
                        SearchCriteria = new
                        {
                            SiteId = siteId,
                            CreatedBy = ClsHttpContent.LoginUser.Id,
                            IsMobileView = true,
                            StartDate = DateTime.Now.ToShortDateString(),
                            EndDate = DateTime.Now.ToShortDateString()
                        },
                        Pager = new { Take = -1 }
                    };

                    var jsonStr = JsonConvert.SerializeObject(payload);
                    var content = new StringContent(jsonStr, System.Text.Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Asset/GetAllSiteDetailsBySiteId", content).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;

                        // Deserialise into a dynamic so we can project slim fields
                        var raw = JsonConvert.DeserializeObject<dynamic>(jsonString);
                        var mAssets = raw?.mAssets as Newtonsoft.Json.Linq.JArray;

                        if (mAssets == null || mAssets.Count == 0)
                        {
                            return Json(new { success = true, mAssets = new List<object>() },
                                JsonRequestBehavior.AllowGet);
                        }

                        // Project only the fields the dashboard needs
                        var slim = mAssets.Select(a => new
                        {
                            Id = (int?)a["Id"] ?? 0,
                            Name = (string)a["Name"] ?? "",
                            AssetName = (string)a["AssetName"] ?? "",
                            SiteId = (int?)a["SiteId"] ?? siteId,
                            AssetTypeId = (int?)a["AssetTypeId"] ?? 0,
                            Sequence = (int?)a["Sequence"] ?? 0,
                            assetAttributes = (a["assetAttributes"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                                .Select(attr => new
                                {
                                    Id = (int?)attr["Id"] ?? 0,
                                    AssetTypeId = (int?)attr["AssetTypeId"] ?? 0,
                                    Title = (string)attr["Title"] ?? "",
                                    AliasName = (string)attr["AliasName"] ?? "",
                                    MinValue = (decimal?)attr["MinValue"],
                                    MaxValue = (decimal?)attr["MaxValue"]
                                }).ToList(),
                            mAssetInfoDataloggers = (a["mAssetInfoDataloggers"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                                .Select(dl => new
                                {
                                    Id = (int?)dl["Id"] ?? 0,
                                    DataloggerAttributeId = (int?)dl["DataloggerAttributeId"] ?? 0,
                                    DataloggerAttribute = (string)dl["DataloggerAttribute"] ?? "",
                                    DataloggerAssetName = (string)dl["DataloggerAssetName"] ?? "",
                                    Role = (string)dl["Role"] ?? ""
                                }).ToList()
                        }).ToList();

                        return new JsonResult
                        {
                            Data = new { success = true, mAssets = slim },
                            JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                            MaxJsonLength = int.MaxValue
                        };
                    }
                    else
                    {
                        Response.StatusCode = (int)response.StatusCode;
                        return Json(new
                        {
                            success = false,
                            mAssets = new List<object>(),
                            errorMessage = "API returned status: " + response.StatusCode
                        }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new
                {
                    success = false,
                    mAssets = new List<object>(),
                    errorMessage = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns the current TCP + MQTT connectivity status for all modems
        /// and A10 (ADC) devices at a site.
        /// API: A10Status/GetStatus/SiteId/{siteId}/SearchDate/{today}
        /// </summary>
        public ActionResult GetDeviceStatus(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string searchDate = DateTime.Now.ToString("dd-MM-yyyy");
                    string apiUrl = string.Format(
                        "A10Status/GetStatus/SiteId/{0}/SearchDate/{1}",
                        siteId, searchDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // Return raw JSON wrapped in success envelope.
                        // Avoids double-serialization (Newtonsoft → JavaScriptSerializer)
                        // which can mangle property names.
                        return Content(
                            "{\"success\":true,\"raw\":" + jsonString + "}",
                            "application/json");
                    }
                    else
                    {
                        Response.StatusCode = (int)response.StatusCode;
                        return Json(new { success = false, errorMessage = "API returned: " + response.StatusCode },
                            JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new { success = false, errorMessage = ex.Message },
                    JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns the raw CardLine list for a site.  CardLine carries the
        /// physical hardware mapping: attributeId → sensor, adcId → IoT device,
        /// modem fields → network device.
        /// </summary>
        public JsonResult GetSiteCardLines(int siteId)
        {
            try
            {
                var cardLines = GetCardLineData(siteId);

                return new JsonResult
                {
                    Data = new { success = true, cardLines = cardLines },
                    JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                    MaxJsonLength = int.MaxValue
                };
            }
            catch (Exception ex)
            {
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return Json(new
                {
                    success = false,
                    cardLines = new List<object>(),
                    errorMessage = ex.Message
                }, JsonRequestBehavior.AllowGet);
            }
        }

        private List<CardLine> GetCardLineData(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync($"CardLine/SiteId/{siteId}").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"CardLine API error: {response.StatusCode} - {jsonString}");
                        return new List<CardLine>();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCardLineData exception: {ex.Message}");
                return new List<CardLine>();
            }
        }

        /// <summary>
        /// Returns A10 (ADC) history for a given cluster + A10 in a date range.
        /// API: A10Status/GetA10History/ClusterId/{clusterId}/A10Id/{a10Id}/FromDate/{from}/ToDate/{to}
        /// </summary>
        [HttpGet]
        public ActionResult GetA10History(int clusterId, int a10Id, string fromDate = null, string toDate = null)
        {
            try
            {
                string from = string.IsNullOrEmpty(fromDate) ? DateTime.Now.ToString("dd-MM-yyyy") : fromDate;
                string to = string.IsNullOrEmpty(toDate) ? DateTime.Now.ToString("dd-MM-yyyy") : toDate;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var apiUrl = string.Format("A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                        clusterId, a10Id, from, to);
                    var response = hcf.client.GetAsync(apiUrl).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        return Content(json, "application/json");
                    }
                    return Json(new { Success = false, Message = "API returned: " + response.StatusCode },
                        JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Returns Modem history for a given cluster in a date range.
        /// API: Product/GetModemHistory/ClusterId/{clusterId}/FromDate/{from}/ToDate/{to}
        /// </summary>
        [HttpGet]
        public ActionResult GetModemHistory(int clusterId, string fromDate = null, string toDate = null)
        {
            try
            {
                string from = string.IsNullOrEmpty(fromDate) ? DateTime.Now.ToString("dd-MM-yyyy") : fromDate;
                string to = string.IsNullOrEmpty(toDate) ? DateTime.Now.ToString("dd-MM-yyyy") : toDate;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var apiUrl = string.Format("Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}",
                        clusterId, from, to);
                    var response = hcf.client.GetAsync(apiUrl).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        return Content(json, "application/json");
                    }
                    return Json(new { Success = false, Message = "API returned: " + response.StatusCode },
                        JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
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

        [HttpGet]
        public ActionResult GetSiteDeviceStatus(int siteId)
        {
            try
            {
                ApiStatusResponse apiData = null;
                string searchDate = DateTime.Now.ToString("dd-MM-yyyy");

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiUrl = string.Format(
                        "A10Status/GetStatus/SiteId/{0}/SearchDate/{1}",
                        siteId,
                        searchDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        serializer.MaxJsonLength = int.MaxValue;
                        apiData = serializer.Deserialize<ApiStatusResponse>(jsonResponse);
                    }
                }

                if (apiData == null)
                {
                    return Json(
                        new { Success = false, Message = "No data from status API", SiteId = siteId },
                        JsonRequestBehavior.AllowGet);
                }

                var modems = new List<object>();

                if (apiData.Modems != null)
                {
                    foreach (var apiModem in apiData.Modems)
                    {
                        // Same rule as the Network Dashboard: skip placeholder rows
                        if (string.IsNullOrEmpty(apiModem.ModemId))
                            continue;

                        var a10s = new List<object>();

                        if (apiModem.A10List != null)
                        {
                            foreach (var a10 in apiModem.A10List)
                            {
                                string adcTypeName;
                                try { adcTypeName = EnumHelper.GetDisplayName((ADCType)a10.ADCTypeId); }
                                catch { adcTypeName = "Type " + a10.ADCTypeId; }

                                a10s.Add(new
                                {
                                    A10Id = a10.A10Id,
                                    TcpStatus = a10.TcpStatus,
                                    MqttStatus = a10.MQTTStatus,
                                    LastDataTime = (a10.TimeStamp != default(DateTime))
                                        ? a10.TimeStamp.ToString("yyyy-MM-ddTHH:mm:ss")
                                        : null,
                                    Uptime = a10.Uptime,
                                    ADCTypeId = a10.ADCTypeId,
                                    ADCTypeName = adcTypeName,
                                    FirmwareVersion = a10.FirmwareVersion
                                });
                            }
                        }

                        modems.Add(new
                        {
                            ClusterId = apiModem.ClusterId,
                            ClusterName = apiModem.ClusterName,
                            ModemId = apiModem.ModemId,
                            ModemName = apiModem.ModemName,
                            Version = apiModem.ModemVersion,
                            SignalStrength = apiModem.SignalStrength,
                            TcpStatus = apiModem.TcpStatus,
                            MqttStatus = apiModem.MqttStatus,
                            IsOnline = apiModem.Status,
                            SimNumber = apiModem.SimNumber,
                            A10List = a10s
                        });
                    }
                }

                var result = Json(new
                {
                    Success = true,
                    SiteId = siteId,
                    SiteName = apiData.SiteName,
                    DivisionName = apiData.DivisionName,
                    ZoneName = apiData.ZoneName,
                    Modems = modems
                }, JsonRequestBehavior.AllowGet);

                // Large stations can exceed the default serializer limit
                result.MaxJsonLength = int.MaxValue;
                return result;
            }
            catch (Exception ex)
            {
                return Json(
                    new { Success = false, Message = ex.Message, SiteId = siteId },
                    JsonRequestBehavior.AllowGet);
            }
        }

        // --------------------------------------------------------------------------
        // 2) GET: Health/GetModemHistoryForGraph?clusterId=&fromDate=&toDate=
        //    Modem signal/status history for the graph (dates: dd-MM-yyyy).
        //    Returns the upstream JSON untouched:
        //      [{ TimeStamp, TypeId (1 = TCP, 2 = MQTT), SignalStrength }, ...]
        // --------------------------------------------------------------------------
        [HttpGet]
        public ActionResult GetModemHistoryForGraph(int clusterId, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiUrl = string.Format(
                        "Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}",
                        clusterId, fromDate, toDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }
                }

                return Content("[]", "application/json");
            }
            catch (Exception)
            {
                return Content("[]", "application/json");
            }
        }

        // --------------------------------------------------------------------------
        // 3) GET: Health/GetA10HistoryForGraph?clusterId=&a10Id=&fromDate=&toDate=
        //    A10 history for the graph (dates: dd-MM-yyyy). Upstream JSON untouched:
        //      [{ TimeStamp, TypeId (1 = TCP, 2 = MQTT), ResponseTime, Uptime }, ...]
        //    (the page plots ResponseTime for TCP and Uptime for MQTT, exactly like
        //    the Network Dashboard's A10 graph)
        // --------------------------------------------------------------------------
        [HttpGet]
        public ActionResult GetA10HistoryForGraph(int clusterId, int a10Id, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiUrl = string.Format(
                        "A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                        clusterId, a10Id, fromDate, toDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }
                }

                return Content("[]", "application/json");
            }
            catch (Exception)
            {
                return Content("[]", "application/json");
            }
        }

        // --------------------------------------------------------------------------
        // 4) GET: Health/GetA10HistoryRecords?clusterId=&a10Id=&ModemId=&fromDate=&toDate=
        //    A10 record list for the history panel, wrapped as { Success, Data }.
        //    ("Records" suffix avoids clashing with any existing GetA10History action.)
        // --------------------------------------------------------------------------
        [HttpGet]
        public ActionResult GetA10HistoryRecords(int clusterId, int a10Id, string ModemId, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiUrl = string.Format(
                        "A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                        clusterId, a10Id, fromDate, toDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        serializer.MaxJsonLength = int.MaxValue;
                        var data = serializer.DeserializeObject(jsonResponse);

                        var result = Json(new { Success = true, Data = data }, JsonRequestBehavior.AllowGet);
                        result.MaxJsonLength = int.MaxValue;
                        return result;
                    }
                }

                return Json(new { Success = false, Message = "No data from history API" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

    }
}
