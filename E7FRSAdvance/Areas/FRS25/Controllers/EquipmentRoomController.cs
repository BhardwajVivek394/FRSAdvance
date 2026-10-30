using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class EquipmentRoomController : Controller
    {
        // GET: FRS25/EquipmentRoom
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;
        private readonly ISectionService _sectionService;
        private readonly IFRSAlertService _frsAlertService;
        private readonly IAssetAttributeService assetAttributeService;
        private readonly IGraphService graphService;

        public EquipmentRoomController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService, ISectionService sectionService, IFRSAlertService frsAlertService, IAssetAttributeService assetAttributeService, IGraphService graphService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
            _sectionService = sectionService;
            _frsAlertService = frsAlertService;
            this.assetAttributeService = assetAttributeService;
            this.graphService = graphService;
        }

        public ActionResult Index()
        {
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);

            return View();
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

        public ActionResult _List(Domain.AssetLister mAssetLister)
        {
            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetEquipmentRoom"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                var mMQTTDetailList = GetMQTTDetailList();
                if (mMQTTDetailList != null)
                    ViewBag.MQTTDetail = mMQTTDetailList.mQTTDetailWeb;
            }
            return PartialView(mAssetLister);
        }


        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            return Json(Helper.FilterCacheHelper.GetDivisionsByZoneId(_divisionService, zoneId), JsonRequestBehavior.AllowGet);
        }

        public JsonResult GetSiteByDivisionId(int divisionId)
        {
            var mSites = new List<Domain.Site>();
            try
            {
                mSites = Helper.FilterCacheHelper.GetSitesByDivisionId(_siteService, divisionId);
            }
            catch (Exception)
            {
                mSites = new List<Domain.Site>();
            }
            return Json(mSites, JsonRequestBehavior.AllowGet);
        }

        public ActionResult History()
        {
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);

            return View();
        }


        public ActionResult _HistoryList(Domain.AssetLister mAssetLister)
        {
            // From/To DateTime is now calculated here (was in the @{ } block of _HistoryList.cshtml)
            var sc = mAssetLister.SearchCriteria;
            // The page sends MM/dd/yyyy; parse culture-independently (server culture is en-IN)
            DateTime startDate = ParseClientDate(sc.StartDate);
            DateTime endDate = ParseClientDate(sc.EndDate);
            if (!string.IsNullOrEmpty(sc.StartTime))
                startDate = startDate.Date.Add(DateTime.Parse(sc.StartTime, CultureInfo.InvariantCulture).TimeOfDay);
            if (!string.IsNullOrEmpty(sc.EndTime))
                endDate = endDate.Date.Add(DateTime.Parse(sc.EndTime, CultureInfo.InvariantCulture).TimeOfDay);
            if (string.IsNullOrEmpty(sc.StartTime) && string.IsNullOrEmpty(sc.EndTime))
                endDate = endDate.AddHours(23).AddMinutes(58);

            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync("Asset/GetEquipmentRoomHistoryNew", str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                    }
                }

                var rows = BuildHistoryRows(mAssetLister, startDate, endDate);

                // EscapeHtml => no "</script>" can break the inline <script> in the partial
                ViewBag.ErRowsJson = JsonConvert.SerializeObject(rows, new JsonSerializerSettings
                {
                    StringEscapeHandling = StringEscapeHandling.EscapeHtml
                });
                ViewBag.ErRowCount = rows.Count;
            }
            catch (Exception)
            {
                throw; 
            }
            return PartialView(mAssetLister);
        }

        private static DateTime ParseClientDate(string value)
        {
            DateTime d;
            if (DateTime.TryParseExact(value, new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                return d;
            return Convert.ToDateTime(value);
        }

        private static List<object[]> BuildHistoryRows(AssetLister lister, DateTime startDate, DateTime endDate)
        {
            var rows = new List<object[]>();
            if (lister == null || lister.mAssets == null) return rows;

            foreach (var item in lister.mAssets)
            {
                if (item.siteAttributeDatasList == null || item.siteAttributeDatasList.Count == 0) continue;
                if (item.siteAttributeDatas == null || item.siteAttributeDatas.Count == 0) continue;
                if (item.assetAttributes == null || item.assetAttributes.Count == 0) continue;

                var readingsByAttr = item.siteAttributeDatas
                    .GroupBy(x => x.AttributeId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.TimeStamp).ToList());

                var loggers = item.mAssetInfoDataloggers;

                var loggerDates = (loggers != null)
                    ? loggers.Where(x => x.mDataloggers != null)
                             .SelectMany(x => x.mDataloggers)
                             .Select(x => x.Date)
                             .ToList()
                    : new List<DateTime>();

                foreach (var siteAttribute in item.siteAttributeDatasList)
                {
                    if (string.IsNullOrEmpty(siteAttribute)) continue;

                    var tempAttr = item.assetAttributes.FirstOrDefault(x => x.Title != null
                        && x.Title.EndsWith(siteAttribute)
                        && x.Title.IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0);
                    var humAttr = item.assetAttributes.FirstOrDefault(x => x.Title != null
                        && x.Title.EndsWith(siteAttribute)
                        && x.Title.IndexOf("humidity", StringComparison.OrdinalIgnoreCase) >= 0);

                    if (tempAttr == null || tempAttr.Id <= 0 || humAttr == null || humAttr.Id <= 0) continue;

                    var temps = readingsByAttr.ContainsKey(tempAttr.Id) ? readingsByAttr[tempAttr.Id] : null;
                    var hums = readingsByAttr.ContainsKey(humAttr.Id) ? readingsByAttr[humAttr.Id] : null;
                    int tCount = temps != null ? temps.Count : 0;
                    int hCount = hums != null ? hums.Count : 0;

                    string tempVal = tCount > 0 ? temps[tCount - 1].AttributeData : string.Empty;
                    string humVal = hCount > 0 ? hums[hCount - 1].AttributeData : string.Empty;

                    string doorVal = string.Empty;
                    if (loggers != null)
                    {
                        var dlInit = loggers.FirstOrDefault(x => x.DataloggerAttribute != null && x.DataloggerAttribute.EndsWith(siteAttribute));
                        if (dlInit != null && dlInit.DataloggerAttributeId > 0 && dlInit.mDataloggers != null && dlInit.mDataloggers.Count > 0)
                            doorVal = dlInit.mDataloggers.OrderByDescending(x => x.Timestamp).First().CurrentValue;
                    }

                    var dlRow = loggers != null
                        ? loggers.FirstOrDefault(x => x.DataloggerAttribute != null && x.DataloggerAttribute.Contains(siteAttribute))
                        : null;
                    bool hasDoor = dlRow != null && dlRow.DataloggerAttributeId > 0 && dlRow.mDataloggers != null && dlRow.mDataloggers.Count > 0;
                    var doorLogs = hasDoor ? dlRow.mDataloggers.OrderBy(x => x.Date).ToList() : null;
                    int dCount = hasDoor ? doorLogs.Count : 0;

                    IEnumerable<DateTime> sensorDates = Enumerable.Empty<DateTime>();
                    if (temps != null) sensorDates = sensorDates.Concat(temps.Select(x => x.TimeStamp));
                    if (hums != null) sensorDates = sensorDates.Concat(hums.Select(x => x.TimeStamp));

                    var dates = loggerDates.Concat(sensorDates)
                        .Where(x => x >= startDate && x <= endDate)
                        .Distinct()                       
                        .OrderByDescending(x => x)
                        .ToList();

                    int ti = tCount - 1, hi = hCount - 1, di = dCount - 1;

                    foreach (var d in dates)
                    {
                        while (ti >= 0 && temps[ti].TimeStamp > d) ti--;
                        if (ti >= 0) tempVal = temps[ti].AttributeData;

                        while (hi >= 0 && hums[hi].TimeStamp > d) hi--;
                        if (hi >= 0) humVal = hums[hi].AttributeData;

                        string door = string.Empty;
                        if (hasDoor)
                        {
                            while (di >= 0 && doorLogs[di].Date > d) di--;
                            if (di >= 0) doorVal = doorLogs[di].CurrentValue;
                            door = doorVal == "Pickup" ? "Open" : (doorVal == "Drop" ? "Close" : string.Empty);
                        }

                        rows.Add(new object[]
                        {
                    item.Zone,
                    item.Division,
                    item.SiteName,
                    d.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                    siteAttribute,
                    tempVal,
                    humVal,
                    door
                        });
                    }
                }
            }
            return rows;
        }

        public ActionResult GetEquipmentRoom(int siteId)
        {
            List<Domain.Asset> equipmentRooms = new List<Domain.Asset>();
            Domain.AssetLister mAssetLister = new AssetLister();
            mAssetLister.SearchCriteria.SiteId = siteId;
            try
            {
                mAssetLister.Pager.Take = -1;
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var jsonStr = JsonConvert.SerializeObject(mAssetLister);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("Asset/GetEquipmentRoom"), str).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonString = response.Content.ReadAsStringAsync().Result;
                        mAssetLister = JsonConvert.DeserializeObject<AssetLister>(jsonString);
                        if (mAssetLister != null && mAssetLister.mAssets != null && mAssetLister.mAssets.Count > 0)
                        {
                            foreach (var item in mAssetLister.mAssets)
                            {

                                if (item.siteAttributeDatasList != null && item.siteAttributeDatasList.Count > 0)
                                {
                                    foreach (var siteAttribute in item.siteAttributeDatasList)
                                    {
                                        equipmentRooms.Add(new Domain.Asset() { Name = siteAttribute });
                                    }
                                }

                                //if (item.mAssetInfoDataloggers != null && item.mAssetInfoDataloggers.Count > 0)
                                //{
                                //    foreach (var dl in item.mAssetInfoDataloggers)
                                //    {
                                //        equipmentRooms.Add(new Domain.Asset() { Name = dl.DataloggerAttribute });

                                //    }
                                //}
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return Json(equipmentRooms, JsonRequestBehavior.AllowGet);
        }
    }
}