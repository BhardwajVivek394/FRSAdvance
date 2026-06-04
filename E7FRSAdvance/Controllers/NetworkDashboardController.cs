using Domain;
using Domain.Dto;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;
using System.Web.Script.Serialization;
using static E7FRSAdvance.Utility.Utility;

namespace E7FRSAdvance.Controllers
{
    [Authenticate]
    public class NetworkDashboardController : Controller
    {
        
        // GET: NetworkDashboard
        public ActionResult Index()
        {
            var model = new DashboardPageVM();

            // 🔹 Build Sidebar Tree
            model.Zones = BuildTree();

            // 🔹 Calculate counts from tree
            model.TotalZones = model.Zones.Count;

            model.TotalDivisions = model.Zones
                .Where(z => z.Divisions != null)
                .Sum(z => z.Divisions.Count);

            model.TotalStations = model.Zones
                .Where(z => z.Divisions != null)
                .Sum(z => z.Divisions
                    .Where(d => d.Sites != null)
                    .Sum(d => d.Sites.Count));

            ViewBag.MQTTDetail = GetMQTTDetailList().mQTTDetailWeb;

            return View(model);
        }


        // 2️⃣ METHOD TO BUILD TREE SAFELY
        private List<ZoneTreeVM> BuildTree()
        {
            List<ZoneDto> zones = new List<ZoneDto>();
            List<DivisionDto> divisions = new List<DivisionDto>();
            List<SiteDto> sites = new List<SiteDto>();

            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                // 2.1 Zones
                var zoneRes = hcf.client.GetAsync("Zone/GetAllZones").Result;
                if (zoneRes.StatusCode == HttpStatusCode.OK)
                {
                    string zoneJson = zoneRes.Content.ReadAsStringAsync().Result;
                    if (!string.IsNullOrEmpty(zoneJson))
                        zones = JsonConvert.DeserializeObject<List<ZoneDto>>(zoneJson);
                }

                // 2.2 Divisions
                var divRes = hcf.client.GetAsync("Division/GetAllDivisions").Result;
                if (divRes.StatusCode == HttpStatusCode.OK)
                {
                    string divJson = divRes.Content.ReadAsStringAsync().Result;
                    if (!string.IsNullOrEmpty(divJson))
                        divisions = JsonConvert.DeserializeObject<List<DivisionDto>>(divJson);
                }

                // 2.3 Sites
                var siteRes = hcf.client.GetAsync("Site/GetAll").Result;
                if (siteRes.StatusCode == HttpStatusCode.OK)
                {
                    string siteJson = siteRes.Content.ReadAsStringAsync().Result;
                    if (!string.IsNullOrEmpty(siteJson))
                        sites = JsonConvert.DeserializeObject<List<SiteDto>>(siteJson);
                }
            }

            // 3️⃣ Build tree manually
            var tree = new List<ZoneTreeVM>();

            if (zones != null)
            {
                foreach (var z in zones)
                {
                    var zoneTree = new ZoneTreeVM
                    {
                        Id = z.Id,
                        Name = z.Name,
                        Divisions = new List<DivisionTreeVM>()
                    };

                    if (divisions != null)
                    {
                        foreach (var d in divisions.Where(dv => dv.ZoneId == z.Id))
                        {
                            var divisionTree = new DivisionTreeVM
                            {
                                Id = d.Id,
                                Name = d.Name,
                                Sites = new List<SiteTreeVM>()
                            };

                            if (sites != null)
                            {
                                foreach (var s in sites.Where(sv => sv.DivisionId == d.Id))
                                {
                                    divisionTree.Sites.Add(new SiteTreeVM
                                    {
                                        Id = s.Id,
                                        Name = s.Name
                                    });
                                }
                            }

                            zoneTree.Divisions.Add(divisionTree);
                        }
                    }

                    tree.Add(zoneTree);
                }
            }

            return tree;
        }
        private void FillDashboardCounts(DashboardPageVM model)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    // For overall data, pass all zeros
                    var request = new
                    {
                        UserId = 0,
                        RoleId = 0,
                        TodayDate = DateTime.Today,
                        ZoneId = 0,
                        DivisionId = 0,
                        SiteId = 0,
                        AssetTypeId = 0,
                        AssetId = 0,
                        AssetAttributeId = 0,
                        TypeId = 0,
                        Clusters = "",
                        Week = 0,
                        A10Id = 0,
                        SearchDate = ""
                    };

                    var jsonStr = JsonConvert.SerializeObject(request);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    var response = hcf.client.PostAsync("A10Status/GetModemSummary", content).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        var result = JsonConvert.DeserializeObject<ModemSummaryResponse>(jsonString);

                        if (result != null)
                        {
                            // Station & Modem counts
                            model.TotalStations = result.StationCount;
                            model.TotalModems = result.ModemCount;
                            model.TotalVersion1 = result.ModemV1Count;
                            model.TotalVersion2 = result.ModemV2Count;
                            model.V1MqttOnline = result.ModemV1OnlineCount;
                            model.V2MqttOnline = result.ModemV2OnlineCount;

                            // A10 counts
                            model.TotalA10Count = result.A10Count;
                            model.TCPA10Count = result.TCPA10Count;       // Total TCP A10
                            model.MQTTA10Count = result.MQTTA10Count;     // Total MQTT A10
                            model.TCPA10OnlineCount = result.TCPA10OnlineCount;
                            model.MQTTA10OnlineCount = result.MQTTA10OnlineCount;
                            model.ModemErrorCount = result.ModemErrorCount;
                            model.TCPErrorCount = result.TCPErrorCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("FillDashboardCounts Error: " + ex.Message);
            }
        }
        public ActionResult LoadDevices(int siteId, string filter = "All")
        {
            try
            {
                var model = new StationDeviceVM
                {
                    SiteId = siteId,
                    Modems = new List<Domain.Dto.ModemVM>()
                };

                // Get MQTT paths from database
                var modemMqttPaths = GetModemMqttPaths(siteId);

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    //string searchDate = DateTime.Now.ToString("dd-MM-yyyy");
                    string searchDate = DateTime.Now.ToString("dd-MM-yyyy");
                    string apiUrl = string.Format(
                        "A10Status/GetStatus/SiteId/{0}/SearchDate/{1}",
                        siteId,
                        searchDate);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;

                        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        var apiData = serializer.Deserialize<ApiStatusResponse>(jsonResponse);

                        if (apiData != null)
                        {
                            // Site info
                            if (!string.IsNullOrEmpty(apiData.SiteName))
                            {
                                model.SiteName = apiData.SiteName;
                                model.DivisionName = apiData.DivisionName;
                                model.ZoneDescription = apiData.ZoneName;
                            }
                            //else
                            //{
                            //    LoadSiteInfoFromDb(siteId, model);
                            //}

                            int totalA10Count = 0;

                            if (apiData.Modems != null)
                            {
                                foreach (var apiModem in apiData.Modems)
                                {
                                    if (string.IsNullOrEmpty(apiModem.ModemId))
                                        continue;

                                    bool isOnline = apiModem.Status;
                                    if (filter == "Online" && !isOnline) continue;
                                    if (filter == "Offline" && isOnline) continue;

                                    // Get MQTT paths
                                    ModemMqttPathInfo mqttInfo = null;
                                    modemMqttPaths.TryGetValue(apiModem.ModemId, out mqttInfo);

                                    var modemVm = new Domain.Dto.ModemVM
                                    {
                                        ClusterId = apiModem.ClusterId,
                                        ModemId = apiModem.ModemId,
                                        ModemName = apiModem.ModemName,
                                        Version = apiModem.ModemVersion,
                                        ClusterName = apiModem.ClusterName,
                                        SignalStrength = apiModem.SignalStrength,
                                        IsOnline = isOnline,
                                        TcpStatus = apiModem.TcpStatus,
                                        MqttStatus = apiModem.MqttStatus,
                                        SimNumber = apiModem.SimNumber,
                                        SiteName = model.SiteName,
                                        DivisionName = model.DivisionName,
                                        ZoneName = model.ZoneDescription,
                                        MqttBasePath = mqttInfo?.MqttBasePath ?? string.Empty,
                                        SubscribeTopic = mqttInfo?.SubscribeTopic ?? string.Empty,
                                        PublishTopic = mqttInfo?.PublishTopic ?? string.Empty
                                    };

                                    // A10 mapping (AS-IS)
                                    if (apiModem.A10List != null)
                                    {
                                        foreach (var apiA10 in apiModem.A10List)
                                        {
                                            var a10Vm = new A10VM
                                            {
                                                Id = apiA10.A10Id,
                                                SiteId = apiA10.SiteId,
                                                TcpStatus = apiA10.TcpStatus,
                                                MqttStatus = apiA10.MQTTStatus,
                                                LastDataTime = apiA10.TimeStamp,
                                                PollTime = apiA10.PollTime,
                                                Uptime = apiA10.Uptime,
                                                ADCTypeId = apiA10.ADCTypeId,
                                                ADCTypeName = EnumHelper.GetDisplayName(
                                                    (ADCType)apiA10.ADCTypeId),
                                                FirmwareVersion = apiA10.FirmwareVersion
                                            };

                                            modemVm.A10List.Add(a10Vm);
                                            totalA10Count++; // ✅ AS-IS count
                                        }
                                    }

                                    model.Modems.Add(modemVm); // ✅ Allow duplicate ModemId
                                }
                            }

                            model.A10Count = totalA10Count;
                        }
                    }
                }

                return PartialView("_DevicePanel", model);
            }
            catch (Exception ex)
            {
                return Content(ex.ToString());
            }
        }
        // Helper method to load site info from database (fallback)
        //private void LoadSiteInfoFromDb(int siteId, StationDeviceVM model)
        //{
        //    using (var con = new SqlConnection(
        //           ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString))
        //    {
        //        con.Open();

        //        using (var siteCmd = new SqlCommand(@"
        //    SELECT 
        //        s.Name AS SiteName,
        //        d.Name AS DivisionName,
        //        z.Description AS ZoneDescription
        //    FROM Site s
        //    INNER JOIN Division d ON s.DivisionId = d.Id
        //    INNER JOIN Zone z ON d.ZoneId = z.Id
        //    WHERE s.Id = @SiteId", con))
        //        {
        //            siteCmd.Parameters.AddWithValue("@SiteId", siteId);

        //            using (var r = siteCmd.ExecuteReader())
        //            {
        //                if (r.Read())
        //                {
        //                    model.SiteName = r["SiteName"] != DBNull.Value ? r["SiteName"].ToString() : null;
        //                    model.DivisionName = r["DivisionName"] != DBNull.Value ? r["DivisionName"].ToString() : null;
        //                    model.ZoneDescription = r["ZoneDescription"] != DBNull.Value ? r["ZoneDescription"].ToString() : null;
        //                }
        //            }
        //        }
        //    }
        //}
    //    public ActionResult LoadA10ByCluster(int clusterId)
    //    {
    //        List<A10VM> list = new List<A10VM>();
    //        using (SqlConnection con = new SqlConnection(
    //            ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString))
    //        {
    //            string sql = @"
    //SELECT
    //    a.Id,
    //    a.SequenceNumber,
    //    a.ADCNumber,
    //    a.ADCTypeId,
    //    c.Id AS ClusterId,
    //    c.Name AS ClusterName,
    //    c.ModemId,
    //    c.ModemName,
    //    s.Id AS SiteId,
    //    s.Name AS StationName,
    //    s.MQTTBasePath,
    //    d.Name AS DivisionName,
    //    CASE
    //        WHEN EXISTS (
    //            SELECT 1
    //            FROM ModemHistory mh
    //            WHERE mh.ClusterId = c.Id
    //              AND mh.ModemId = c.ModemId
    //              AND mh.TypeId = 1
    //              AND mh.Status = 1
    //              AND mh.TimeStamp >= DATEADD(MINUTE, -2, GETDATE())
    //        )
    //        THEN 1 ELSE 0
    //    END AS TcpStatus,
    //    CASE
    //        WHEN EXISTS (
    //            SELECT 1
    //            FROM ModemHistory mh
    //            WHERE mh.ClusterId = c.Id
    //              AND mh.ModemId = c.ModemId
    //              AND mh.TypeId = 2
    //              AND mh.Status = 1
    //              AND mh.TimeStamp >= DATEADD(MINUTE, -2, GETDATE())
    //        )
    //        THEN 1 ELSE 0
    //    END AS MqttStatus,
    //    (SELECT MAX(mh.TimeStamp) 
    //     FROM ModemHistory mh 
    //     WHERE mh.ClusterId = c.Id 
    //       AND mh.ModemId = c.ModemId) AS LastDataTime,
    //    -- MQTT Subscribe Topic (Response) - BasePath/ClusterName/P
    //    ISNULL(s.MQTTBasePath, '') + '/' + REPLACE(ISNULL(c.Name, ''), ' ', '') + '/P' AS MqttSubscribe,
    //    -- MQTT Publish Topic (Command) - BasePath/ClusterName/S
    //    ISNULL(s.MQTTBasePath, '') + '/' + REPLACE(ISNULL(c.Name, ''), ' ', '') + '/S' AS MqttPublish
    //FROM ADC a
    //LEFT JOIN Cluster c ON a.ClusterId = c.Id
    //LEFT JOIN Site s ON c.SiteId = s.Id
    //LEFT JOIN Division d ON s.DivisionId = d.Id
    //WHERE a.ClusterId = @ClusterId
    //  AND a.DeletedDate IS NULL
    //ORDER BY a.SequenceNumber";
    //            using (SqlCommand cmd = new SqlCommand(sql, con))
    //            {
    //                cmd.Parameters.AddWithValue("@ClusterId", clusterId);
    //                con.Open();
    //                using (SqlDataReader rdr = cmd.ExecuteReader())
    //                {
    //                    while (rdr.Read())
    //                    {
    //                        int adcTypeId = Convert.ToInt32(rdr["ADCTypeId"]);

    //                        A10VM item = new A10VM();
    //                        item.Id = Convert.ToInt32(rdr["Id"]);
    //                        item.ADCNumber = GetString(rdr, "ADCNumber");
    //                        item.ADCTypeId = adcTypeId;
    //                        item.ADCTypeName = EnumHelper.GetDisplayName((ADCType)adcTypeId);
    //                        item.ClusterId = GetInt(rdr, "ClusterId");
    //                        item.ClusterName = GetString(rdr, "ClusterName");
    //                        item.ModemId = GetString(rdr, "ModemId");
    //                        item.ModemName = GetString(rdr, "ModemName");
    //                        item.SiteId = GetInt(rdr, "SiteId");
    //                        item.StationName = GetString(rdr, "StationName");
    //                        item.DivisionName = GetString(rdr, "DivisionName");
    //                        item.MQTTBasePath = GetString(rdr, "MQTTBasePath");
    //                        item.TcpStatus = Convert.ToBoolean(rdr["TcpStatus"]);
    //                        item.MqttStatus = Convert.ToBoolean(rdr["MqttStatus"]);
    //                        item.LastDataTime = GetDateTime(rdr, "LastDataTime");
    //                        item.MqttSubscribe = GetString(rdr, "MqttSubscribe");
    //                        item.MqttPublish = GetString(rdr, "MqttPublish");
    //                        list.Add(item);
    //                    }
    //                }
    //            }
    //        }
    //        return PartialView("_A10List", list);
    //    }

        // Helper methods for null-safe reading
        private string GetString(SqlDataReader rdr, string column)
        {
            object val = rdr[column];
            if (val == null || val == DBNull.Value)
                return null;
            return val.ToString();
        }

        private int GetInt(SqlDataReader rdr, string column)
        {
            object val = rdr[column];
            if (val == null || val == DBNull.Value)
                return 0;
            return Convert.ToInt32(val);
        }

        private DateTime? GetDateTime(SqlDataReader rdr, string column)
        {
            object val = rdr[column];
            if (val == null || val == DBNull.Value)
                return null;
            return Convert.ToDateTime(val);
        }
        public ActionResult DashboardHeader(int? zoneId = null, int? divisionId = null, int? siteId = null)
        {
            var model = new DashboardPageVM();

            try
            {
                // Build request object
                var request = new
                {
                    UserId = 0,
                    RoleId = 0,
                    TodayDate = DateTime.Today,
                    ZoneId = zoneId ?? 0,
                    DivisionId = divisionId ?? 0,
                    SiteId = siteId ?? 0,
                    AssetTypeId = 0,
                    AssetId = 0,
                    AssetAttributeId = 0,
                    TypeId = 0,
                    Clusters = "",
                    Week = 0,
                    A10Id = 0,
                    SearchDate = ""
                };
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(request);
                    var content = new StringContent(jsonStr, Encoding.UTF8, "application/json");

                    var response = hcf.client.PostAsync("A10Status/GetModemSummary", content).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        var responseJson = response.Content.ReadAsStringAsync().Result;
                        var result = Newtonsoft.Json.JsonConvert.DeserializeObject<ModemSummaryResponse>(responseJson);

                        if (result != null)
                        {
                            // Map API response to DashboardPageVM
                            model.TotalStations = result.StationCount;
                            model.TotalModems = result.ModemCount;
                            model.TotalVersion1 = result.ModemV1Count;
                            model.TotalVersion2 = result.ModemV2Count;
                            model.V1MqttOnline = result.ModemV1OnlineCount;
                            model.V2MqttOnline = result.ModemV2OnlineCount;
                            model.TotalA10Count = result.A10Count;
                            model.TCPA10Count = result.TCPA10Count;
                            model.MQTTA10Count = result.MQTTA10Count;
                            model.TCPA10OnlineCount = result.TCPA10OnlineCount;
                            model.MQTTA10OnlineCount = result.MQTTA10OnlineCount;
                            model.ModemErrorCount = result.ModemErrorCount;
                            model.TCPErrorCount = result.TCPErrorCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DashboardHeader Error: " + ex.Message);
            }

            return PartialView("_DashboardHeader", model);
        }

        public ActionResult A10StatusModemWise(int clusterId)
        {
            var model = new List<A10StatusModemVM>();

            //if (!modemId.HasValue)
            //{
            //    return PartialView("_A10StatusModemWise", model);
            //}

            //long? clusterId = null;

            // Step 1: Get ClusterId from ModemId
            //using (var con = new SqlConnection(
            //       ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString))
            //{
            //    con.Open();

            //    using (var cmd = new SqlCommand(
            //        "SELECT Id FROM Cluster WHERE ModemId = @ModemId AND DeletedDate IS NULL", con))
            //    {
            //        cmd.Parameters.AddWithValue("@ModemId", modemId.Value.ToString());
            //        var result = cmd.ExecuteScalar();
            //        if (result != null && result != DBNull.Value)
            //        {
            //            clusterId = Convert.ToInt64(result);
            //        }
            //    }
            //}

            // Step 2: Call API if ClusterId found
            //if (clusterId.HasValue)
            //{
                try
                {
                    //string todayDate = DateTime.Now.ToString("dd-MM-yyyy");
                    string todayDate = DateTime.Now.ToString("dd-MM-yyyy");
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        // 2.1 Zones
                        var response = hcf.client.GetAsync($"A10Status/GetStatus/ClusterId/{clusterId}/SearchDate/{todayDate}").Result;

                        if (response.IsSuccessStatusCode)
                        {
                            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            model = JsonConvert.DeserializeObject<List<A10StatusModemVM>>(json)
                                    ?? new List<A10StatusModemVM>();
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message;
                }

            //ViewBag.ClusterId = clusterId;
            return PartialView("_A10StatusModemWise", model);
        }

        public ActionResult GetDashboardCounts()
        {
            var model = new DashboardPageVM();
            FillDashboardCounts(model);
            return Json(new
            {
                TotalModems = model.TotalModems,
                TotalVersion1 = model.TotalVersion1,
                TotalVersion2 = model.TotalVersion2,
                V1MqttOnline = model.V1MqttOnline,
                V2MqttOnline = model.V2MqttOnline,
                TotalA10Count = model.TotalA10Count,
                TCPA10Count = model.TCPA10Count,
                MQTTA10Count = model.MQTTA10Count,
                TCPA10OnlineCount = model.TCPA10OnlineCount,
                MQTTA10OnlineCount = model.MQTTA10OnlineCount
            }, JsonRequestBehavior.AllowGet);
        }

        public ActionResult ModemHistory(int clusterId,string fromDate=null, string toDate = null)
        {
            var model = new List<ModemHistoryVM>();
            //if (!modemId.HasValue)
            //{
            //    ViewBag.ClusterId = 0;
            //    return PartialView("_ModemHistory", model);
            //}

            //long? clusterId = null;

            // Step 1: Get ClusterId from ModemId
            //using (var con = new SqlConnection(
            //       ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString))
            //{
            //    con.Open();
            //    using (var cmd = new SqlCommand(
            //        "SELECT Id FROM Cluster WHERE ModemId = @ModemId AND DeletedDate IS NULL", con))
            //    {
            //        cmd.Parameters.AddWithValue("@ModemId", modemId.Value.ToString());
            //        var result = cmd.ExecuteScalar();
            //        if (result != null && result != DBNull.Value)
            //        {
            //            clusterId = Convert.ToInt64(result);
            //        }
            //    }
            //}

            // Set ViewBag AFTER clusterId is retrieved
            ViewBag.ClusterId = clusterId;

            // Step 2: Call API if ClusterId found
            //if (clusterId.HasValue)
            //{
                try
                {
                // Default to today if dates are not provided
                string fromDateStr = string.IsNullOrEmpty(fromDate)
                    ? DateTime.Now.ToString("dd-MM-yyyy")
                    : fromDate;

                string toDateStr = string.IsNullOrEmpty(toDate)
                    ? DateTime.Now.ToString("dd-MM-yyyy")
                    : toDate;
                //string dateStr = string.IsNullOrEmpty(searchDate)
                //    ? DateTime.Now.ToString("dd-MM-yyyy")
                //    : searchDate;

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var response = hcf.client.GetAsync(
                            string.Format("Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}", clusterId, fromDateStr, toDateStr)).Result;

                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                model = JsonConvert.DeserializeObject<List<ModemHistoryVM>>(json)
                                        ?? new List<ModemHistoryVM>();
                                model = model.OrderByDescending(x => x.TimeStamp).ToList();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.Error = ex.Message;
                }
            //}

            return PartialView("_ModemHistory", model);
        }

        // Helper class to hold MQTT path info
        private class ModemMqttPathInfo
        {
            public string ModemId { get; set; }
            public string MqttBasePath { get; set; }
            public string SubscribeTopic { get; set; }
            public string PublishTopic { get; set; }
        }
        private Dictionary<string, ModemMqttPathInfo> GetModemMqttPaths(int siteId)
        {
            var result = new Dictionary<string, ModemMqttPathInfo>();

            try
            {
                string mqttBasePath = "";

                // Step 1: Get MqttBasePath from Site API
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string siteEndpoint = string.Format("Site/GetSiteById/{0}", siteId);
                    var siteResponse = hcf.client.GetAsync(siteEndpoint).Result;

                    if (siteResponse.StatusCode == HttpStatusCode.OK)
                    {
                        var siteJson = siteResponse.Content.ReadAsStringAsync().Result;
                        var siteData = JsonConvert.DeserializeObject<dynamic>(siteJson);

                        if (siteData != null && siteData.MQTTBasePath != null)
                        {
                            mqttBasePath = siteData.MQTTBasePath.ToString();
                        }
                    }
                }

                // Step 2: Get Clusters from Cluster API
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string clusterEndpoint = string.Format("Cluster/SiteId/{0}", siteId);
                    var clusterResponse = hcf.client.GetAsync(clusterEndpoint).Result;

                    if (clusterResponse.StatusCode == HttpStatusCode.OK)
                    {
                        var clusterJson = clusterResponse.Content.ReadAsStringAsync().Result;
                        var clusters = JsonConvert.DeserializeObject<List<dynamic>>(clusterJson);

                        if (clusters != null)
                        {
                            foreach (var cluster in clusters)
                            {
                                // Skip if ModemId is null or empty
                                if (cluster.ModemId == null) continue;

                                string modemId = cluster.ModemId.ToString();
                                if (string.IsNullOrEmpty(modemId)) continue;

                                // Get cluster name and remove spaces
                                string clusterName = "";
                                if (cluster.Name != null)
                                {
                                    clusterName = cluster.Name.ToString().Replace(" ", "");
                                }

                                // Build MQTT paths
                                // MqttBasePath already has trailing slash like "/DNR/PPTA/"
                                // ClusterName is like "cluster_4/LC013_2"
                                // Result: /DNR/PPTA/cluster_4/LC013_2/P

                                string basePath = mqttBasePath.TrimEnd('/');
                                string subscribeTopic = string.Format("{0}/{1}/P", basePath, clusterName);
                                string publishTopic = string.Format("{0}/{1}/S", basePath, clusterName);

                                var info = new ModemMqttPathInfo
                                {
                                    ModemId = modemId,
                                    MqttBasePath = mqttBasePath,
                                    SubscribeTopic = subscribeTopic,
                                    PublishTopic = publishTopic
                                };

                                // Only add if not already exists (avoid duplicates)
                                if (!result.ContainsKey(modemId))
                                {
                                    result.Add(modemId, info);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error if needed
                System.Diagnostics.Debug.WriteLine("GetModemMqttPaths Error: " + ex.Message);
            }

            return result;
        }
        //private Dictionary<string, ModemMqttPathInfo> GetModemMqttPaths(int siteId)
        //{
        //    var result = new Dictionary<string, ModemMqttPathInfo>();

        //    string connStr = ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString;

        //    string sql = @"SELECT c.ModemId, ISNULL(s.MQTTBasePath, '') AS MqttBasePath,
        //    ISNULL(s.MQTTBasePath, '') + '/' + REPLACE(ISNULL(c.Name, ''), ' ', '') + '/P' AS MqttSubscribe,
        //    ISNULL(s.MQTTBasePath, '') + '/' + REPLACE(ISNULL(c.Name, ''), ' ', '') + '/S' AS MqttPublish
        //    FROM Cluster c
        //    INNER JOIN Site s ON c.SiteId = s.Id
        //    WHERE s.Id = @SiteId and c.DeletedDate is null and c.ModemId is not null";

        //    using (var con = new SqlConnection(connStr))
        //    using (var cmd = new SqlCommand(sql, con))
        //    {
        //        cmd.Parameters.AddWithValue("@SiteId", siteId);
        //        con.Open();

        //        using (var reader = cmd.ExecuteReader())
        //        {
        //            while (reader.Read())
        //            {
        //                string modemId = reader["ModemId"].ToString();

        //                var info = new ModemMqttPathInfo
        //                {
        //                    ModemId = modemId,
        //                    MqttBasePath = reader["MqttBasePath"] != DBNull.Value ? reader["MqttBasePath"].ToString() : "",
        //                    SubscribeTopic = reader["MqttSubscribe"] != DBNull.Value ? reader["MqttSubscribe"].ToString() : "",
        //                    PublishTopic = reader["MqttPublish"] != DBNull.Value ? reader["MqttPublish"].ToString() : ""
        //                };

        //                if (!result.ContainsKey(modemId))
        //                {
        //                    result.Add(modemId, info);
        //                }
        //            }
        //        }
        //    }

        //    return result;
        //}

        public ActionResult GetA10History(int clusterId, int a10Id, string ModemId, string fromDate, string toDate)
        {
            try
            {
                // Get ClusterId based on A10Id from database
                //string clusterId = GetClusterIdByA10Id(ModemId);

                //if (string.IsNullOrEmpty(clusterId))
                //{
                //    return Json(new { Success = false, Message = "Cluster not found for this A10" }, JsonRequestBehavior.AllowGet);
                //}

                // Format today's date as dd-MM-yyyy
                //string searchDate = DateTime.Now.ToString("dd-MM-yyyy");

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiEndpoint = string.Format("A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                        clusterId,
                        a10Id,
                        fromDate,
                        toDate);

                    var response = hcf.client.GetAsync(apiEndpoint).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            string jsonResponse = response.Content.ReadAsStringAsync().Result;

                            var serializer = new JavaScriptSerializer();
                            var historyData = serializer.Deserialize<List<A10HistoryModel>>(jsonResponse);

                            return Json(new { Success = true, Data = historyData }, JsonRequestBehavior.AllowGet);
                        }
                    }

                    return Json(new { Success = false, Message = "Failed to fetch history from API" }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = "Error fetching history" }, JsonRequestBehavior.AllowGet);
            }
        }

        //private string GetClusterIdByA10Id(string ModemId)
        //{
        //    string clusterId = string.Empty;

        //    using (SqlConnection conn = new SqlConnection(ConfigurationManager.ConnectionStrings["E7MRIV2DB_SQL"].ConnectionString))
        //    {
        //        conn.Open();
        //        string query = "SELECT Id FROM Cluster WHERE ModemId = @ModemId and DeletedDate is null and ModemId is not null";

        //        using (SqlCommand cmd = new SqlCommand(query, conn))
        //        {
        //            cmd.Parameters.AddWithValue("@ModemId", ModemId);
        //            object result = cmd.ExecuteScalar();

        //            if (result != null && result != DBNull.Value)
        //            {
        //                clusterId = Convert.ToString(result);
        //            }
        //        }
        //    }

        //    return clusterId;
        //}

        /// <summary>
        /// Gets A10 history data for graph plotting
        /// API: A10Status/GetA10History/ClusterId/{clusterId}/A10Id/{a10Id}/FromDate/{fromDate}/ToDate/{toDate}
        /// </summary>
        [HttpGet]
        public ActionResult GetA10HistoryForGraph(int clusterId, int a10Id, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiEndpoint = string.Format("A10Status/GetA10History/ClusterId/{0}/A10Id/{1}/FromDate/{2}/ToDate/{3}",
                        clusterId,
                        a10Id,
                        fromDate,
                        toDate);

                    var response = hcf.client.GetAsync(apiEndpoint).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }

                    return Json(new { Success = false, Message = "Failed to fetch data from API" }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Gets Modem history data for graph plotting
        /// API: Product/GetModemHistory/ClusterId/{clusterId}/FromDate/{fromDate}/ToDate/{toDate}
        /// </summary>
        [HttpGet]
        public ActionResult GetModemHistoryForGraph(int clusterId, string fromDate, string toDate)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiEndpoint = string.Format("Product/GetModemHistory/ClusterId/{0}/FromDate/{1}/ToDate/{2}",
                        clusterId,
                        fromDate,
                        toDate);

                    var response = hcf.client.GetAsync(apiEndpoint).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content.ReadAsStringAsync().Result;
                        return Content(jsonResponse, "application/json");
                    }

                    return Json(new { Success = false, Message = "Failed to fetch data from API" }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        /// <summary>
        /// GET: CardLine/SiteId/{id}
        /// Fetches CardLine configuration data for a site from the API
        /// </summary>
        /// <param name="id">Site ID</param>
        /// <returns>JSON array of CardLine data</returns>
        [HttpGet]
        public ActionResult SiteId(int id)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    string apiUrl = string.Format("CardLine/SiteId/{0}", id);

                    var response = hcf.client.GetAsync(apiUrl).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string content = response.Content.ReadAsStringAsync().Result;
                        return Content(content, "application/json");
                    }
                    else
                    {
                        string errorJson = JsonConvert.SerializeObject(new
                        {
                            success = false,
                            message = "API returned: " + response.StatusCode
                        });
                        return Content(errorJson, "application/json");
                    }
                }
            }
            catch (Exception ex)
            {
                string errorJson = JsonConvert.SerializeObject(new
                {
                    success = false,
                    message = ex.Message
                });
                return Content(errorJson, "application/json");
            }
        }


        public MQTTDetailList GetMQTTDetailList()
        {

            MQTTDetailList mMQTTDetail = new MQTTDetailList();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("User/GetMQTTDetail")).Result;
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

    }
}