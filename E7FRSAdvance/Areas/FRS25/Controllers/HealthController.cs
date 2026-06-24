using Domain;
using Domain.Dto;
using E7FRSAdvance.Controllers;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
        private static readonly string TCP_SERVER_HOST = ConfigurationManager.AppSettings["TcpServerHost"] ?? "proxy.energy7.org";

        // Base port for TCP connections
        private const int TCP_BASE_PORT = 1400;

        // Default Lc offset when no /LcXb pattern found
        private const int DEFAULT_LC_OFFSET = 30;
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


        public ActionResult History(int siteId)
        {

            if (siteId <= 0)
                return RedirectToAction("Index");

            var siteName = "Site " + siteId;
            try
            {
                var site = _siteService.Get(siteId);
                if (site != null && !string.IsNullOrWhiteSpace(site.Name))
                    siteName = site.Name;
            }
            catch
            {
                // Keep the history page usable even if site lookup fails;
                // device data is still loaded through GetSiteDeviceStatus(siteId).
            }

            ViewBag.SiteId = siteId;
            ViewBag.SiteName = siteName;
            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;

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

        // Proxy for the AlertHistory API (historical alerts over a date range).
        // Uses the same proxy base as the rest of the dashboard: "ProxyBaseUrlSSL"
        // for HTTPS requests, "ProxyBaseUrl" for HTTP (with a fallback to whichever
        // is configured). Dates are passed through verbatim in the ddMMyyyy_HHmmss
        // format the upstream API expects (e.g. 24062026_160200).
        public ActionResult GetAlertHistory(int siteId = 0, string startDate = "", string endDate = "")
        {
            try
            {
                bool isSecure = Request != null && Request.IsSecureConnection;
                string baseUrl = isSecure
                    ? ConfigurationManager.AppSettings["ProxyBaseUrlSSL"]
                    : ConfigurationManager.AppSettings["ProxyBaseUrl"];
                // Fall back to the other key if the scheme-specific one is not set.
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = isSecure
                        ? ConfigurationManager.AppSettings["ProxyBaseUrl"]
                        : ConfigurationManager.AppSettings["ProxyBaseUrlSSL"];

                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return Json(new { success = false, error = "ProxyBaseUrl / ProxyBaseUrlSSL not configured in AppSettings." },
                        JsonRequestBehavior.AllowGet);
                }

                baseUrl = baseUrl.TrimEnd('/');

                var qs = new List<string>();
                if (siteId > 0) qs.Add("siteId=" + siteId);
                if (!string.IsNullOrWhiteSpace(startDate)) qs.Add("StartDate=" + Uri.EscapeDataString(startDate));
                if (!string.IsNullOrWhiteSpace(endDate)) qs.Add("EndDate=" + Uri.EscapeDataString(endDate));
                string apiUrl = baseUrl + "/api/AlertHistory" + (qs.Count > 0 ? ("?" + string.Join("&", qs)) : "");

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
                        return Json(new { success = false, error = "AlertHistory API returned: " + response.StatusCode },
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
                     // datareceiver / debouncer / pointServices now publish health on
                     // separate Local and Cloud topics (a /local/ or /cloud/ segment is
                     // inserted just before /health). The dashboard subscribes to both
                     // and routes each message to the matching node by the topic path.
                     $"datareceiver/{site.Id}/local/health",
                     $"datareceiver/{site.Id}/cloud/health",
                     $"debouncer/{site.Id}/local/health",
                     $"debouncer/{site.Id}/cloud/health",
                     $"pointServices/{site.Id}/local/health",
                     $"pointServices/{site.Id}/cloud/health"
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

        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    using (var mHttpClientFactory = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = mHttpClientFactory.client.GetAsync(String.Format("AssetType/GetAllAssestType/{0}", siteId)).Result;
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            mAssetTypes = JsonConvert.DeserializeObject<List<Domain.AssetType>>(jsonString);
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
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }
        public ActionResult GetYardConfigByAssetType(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
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

            return PartialView("_YardConfigByAssetTypePartial", mCardLines);

        }
        public ActionResult GetYardConfigByAssetTypeJson(int siteId, int assetTypeId)
        {
            List<CardLine> mCardLines = new List<CardLine>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(
                        String.Format("CardLine/SiteId/{0}/assetTypeId/{1}", siteId, assetTypeId)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mCardLines = JsonConvert.DeserializeObject<List<CardLine>>(jsonString);
                    }
                }

                return Json(new { Success = true, Data = mCardLines }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Error = ex.Message, Data = new List<CardLine>() }, JsonRequestBehavior.AllowGet);
            }
        }
        private string ExtractCleanJsonResponse(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse))
            {
                return null;
            }

            int jsonStart = -1;
            int jsonEnd = -1;
            int braceCount = 0;
            bool inJson = false;

            for (int i = 0; i < rawResponse.Length; i++)
            {
                char c = rawResponse[i];

                if (c == '{')
                {
                    if (!inJson)
                    {
                        jsonStart = i;
                        inJson = true;
                    }
                    braceCount++;
                }
                else if (c == '}' && inJson)
                {
                    braceCount--;
                    if (braceCount == 0)
                    {
                        jsonEnd = i;

                        string potentialJson = rawResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);

                        if (potentialJson.Contains("CSQ") || potentialJson.Contains("ID"))
                        {
                            try
                            {
                                JObject json = JObject.Parse(potentialJson);

                                if (json["CSQ"] != null)
                                {
                                    return json.ToString(Newtonsoft.Json.Formatting.None);
                                }
                            }
                            catch
                            {
                            }
                        }

                        inJson = false;
                        braceCount = 0;
                    }
                }
            }

            int arrayStart = rawResponse.IndexOf("[\"CSQ\"");
            if (arrayStart >= 0)
            {
                int arrayEnd = rawResponse.IndexOf("]", arrayStart);
                if (arrayEnd > arrayStart)
                {
                    string arrayJson = rawResponse.Substring(arrayStart, arrayEnd - arrayStart + 1);

                    try
                    {
                        if (arrayJson.Contains("CSQ"))
                        {
                            int csqIndex = arrayJson.IndexOf("\"CSQ\"");
                            if (csqIndex >= 0)
                            {
                                int valueStart = arrayJson.IndexOf("\"", csqIndex + 5);
                                if (valueStart >= 0)
                                {
                                    int valueEnd = arrayJson.IndexOf("\"", valueStart + 1);
                                    if (valueEnd > valueStart)
                                    {
                                        string csqValue = arrayJson.Substring(valueStart + 1, valueEnd - valueStart - 1);

                                        string idValue = "";
                                        int idIndex = arrayJson.IndexOf("\"ID\"");
                                        if (idIndex < 0)
                                        {
                                            idIndex = arrayJson.IndexOf("ID\":");
                                        }
                                        if (idIndex >= 0)
                                        {
                                            int idValueStart = arrayJson.IndexOf("\"", idIndex + 3);
                                            if (idValueStart >= 0)
                                            {
                                                int idValueEnd = arrayJson.IndexOf("\"", idValueStart + 1);
                                                if (idValueEnd > idValueStart)
                                                {
                                                    idValue = arrayJson.Substring(idValueStart + 1, idValueEnd - idValueStart - 1);
                                                }
                                            }
                                        }

                                        JObject result = new JObject();
                                        result["CSQ"] = csqValue;
                                        if (!string.IsNullOrEmpty(idValue))
                                        {
                                            result["ID"] = idValue;
                                        }
                                        return result.ToString(Newtonsoft.Json.Formatting.None);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            int csqPlainIndex = rawResponse.IndexOf("CSQ");
            if (csqPlainIndex >= 0)
            {
                int colonIndex = rawResponse.IndexOf(":", csqPlainIndex);
                if (colonIndex < 0)
                {
                    colonIndex = rawResponse.IndexOf(",", csqPlainIndex);
                }

                if (colonIndex >= 0 && colonIndex < rawResponse.Length - 1)
                {
                    StringBuilder valueBuilder = new StringBuilder();
                    bool foundDigit = false;

                    for (int i = colonIndex + 1; i < rawResponse.Length && i < colonIndex + 10; i++)
                    {
                        char c = rawResponse[i];
                        if (char.IsDigit(c) || c == ',')
                        {
                            valueBuilder.Append(c);
                            foundDigit = true;
                        }
                        else if (foundDigit && !char.IsDigit(c) && c != ',')
                        {
                            break;
                        }
                    }

                    if (valueBuilder.Length > 0)
                    {
                        JObject result = new JObject();
                        result["CSQ"] = valueBuilder.ToString().Trim(',');
                        return result.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }

            return null;
        }

        [HttpPost]
        public JsonResult SendTcpCommand()
        {
            try
            {
                // Read JSON from request body
                Request.InputStream.Position = 0;
                string jsonBody;
                using (var reader = new System.IO.StreamReader(Request.InputStream))
                {
                    jsonBody = reader.ReadToEnd();
                }

                // Deserialize JSON
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var request = serializer.Deserialize<SendTcpCommandRequest>(jsonBody);

                if (request == null)
                {
                    return Json(new { success = false, error = "Invalid request" });
                }
                if (request.TcpInfo == null)
                {
                    return Json(new { success = false, error = "Modem not found: " + request.ModemId });
                }
                if (string.IsNullOrEmpty(request.TcpInfo.ClusterName))
                {
                    return Json(new { success = false, error = "Cluster not found for modem: " + request.ModemId });
                }

                //int tcpPort = CalculateTcpPort(request.TcpInfo.ClusterName);
                string value = request.TcpInfo.TcpSendPort;
                int tcpPort = int.Parse(value.Substring(value.LastIndexOf(':') + 1));
                string rawResponse = SendTcpCommandInternal(
                    TCP_SERVER_HOST,
                    tcpPort,
                    request.Command,
                    TimeSpan.FromSeconds(10)
                );
                string cleanResponse = ExtractCleanJsonResponse(rawResponse);

                return Json(new
                {
                    success = cleanResponse != null,
                    data = cleanResponse,
                    rawData = rawResponse,
                    protocol = "TCP",
                    host = TCP_SERVER_HOST,
                    port = tcpPort,
                    //siteId = tcpInfo.SiteId,
                    siteName = request.TcpInfo.StationName,
                    clusterName = request.TcpInfo.ClusterName,
                    commandType = "modem_at_tcp"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = "TCP Error: " + ex.Message });
            }
        }

        /// <summary>
        /// Internal method to send TCP command and receive response.
        /// </summary>
        private string SendTcpCommandInternal(string host, int port, string command, TimeSpan timeout)
        {
            string response = null;
            TcpClient client = null;
            NetworkStream stream = null;

            try
            {
                client = new TcpClient();

                IAsyncResult connectResult = client.BeginConnect(host, port, null, null);
                bool connected = connectResult.AsyncWaitHandle.WaitOne(timeout);

                if (!connected)
                {
                    throw new TimeoutException("TCP connection timeout to " + host + ":" + port.ToString());
                }

                client.EndConnect(connectResult);

                stream = client.GetStream();
                stream.ReadTimeout = (int)timeout.TotalMilliseconds;
                stream.WriteTimeout = (int)timeout.TotalMilliseconds;

                byte[] commandBytes = Encoding.ASCII.GetBytes(command + "\r\n");
                stream.Write(commandBytes, 0, commandBytes.Length);
                stream.Flush();

                Thread.Sleep(200);

                byte[] buffer = new byte[4096];
                StringBuilder responseBuilder = new StringBuilder();

                try
                {
                    DateTime startTime = DateTime.Now;
                    while ((DateTime.Now - startTime).TotalMilliseconds < timeout.TotalMilliseconds)
                    {
                        if (stream.DataAvailable)
                        {
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                                string currentResponse = responseBuilder.ToString();
                                if (currentResponse.Contains("\n") ||
                                    currentResponse.Contains("OK") ||
                                    currentResponse.Contains("ERROR") ||
                                    currentResponse.Contains("+CSQ") ||
                                    (currentResponse.Contains("[") && currentResponse.Contains("]")) ||
                                    (currentResponse.Contains("{") && currentResponse.Contains("}")))
                                {
                                    Thread.Sleep(100);

                                    if (stream.DataAvailable)
                                    {
                                        int extraBytes = stream.Read(buffer, 0, buffer.Length);
                                        if (extraBytes > 0)
                                        {
                                            responseBuilder.Append(Encoding.ASCII.GetString(buffer, 0, extraBytes));
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }
                    }

                    response = responseBuilder.ToString().Trim();

                    if (string.IsNullOrEmpty(response))
                    {
                        response = null;
                    }
                }
                catch (IOException)
                {
                    response = null;
                }
            }
            catch (SocketException ex)
            {
                throw new Exception("TCP Socket Error: " + ex.Message);
            }
            finally
            {
                if (stream != null)
                {
                    try { stream.Close(); } catch { }
                }
                if (client != null)
                {
                    try { client.Close(); } catch { }
                }
            }

            return response;
        }
    }
}
