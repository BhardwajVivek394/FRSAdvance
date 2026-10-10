using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    /*
     * Telemetry History.
     *
     * Every action used by Views/TelemetryHistory/Index.cshtml mirrors the web
     * project's (E7MRIV2Web) TelemetryHistoryController one-for-one: same
     * action names, same parameters, same upstream URL / query string. The
     * history page script is a port of the web page, so the two must stay in
     * step.
     *
     * The "Legacy FRS endpoints" region at the bottom is NOT used by the
     * history page any more; it is kept because SIP replay and Telemetry Live
     * (telemetrylive-drawer.js / telemetrylive-health.js / sip-*.js) call it.
     */
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class TelemetryHistoryController : Controller
    {
        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IAssetTypeService _assetTypeService;
        private readonly IAssetService _assetService;

        public TelemetryHistoryController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IAssetTypeService assetTypeService, IAssetService assetService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _assetTypeService = assetTypeService;
            _assetService = assetService;
        }

        // GET: FRS25/TelemetryHistory
        public ActionResult Index()
        {
            // Same shared filter cache used by Alert Live.
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag, _siteService, _zoneService, _divisionService, includeAssetType: false);
            // FRS asset types are enum-based, so retain existing behaviour.
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(), "Id", "Name");
            return View();
        }

        public JsonResult GetDivisionByZoneId(DivisionLister mDivisionLister)
        {
            List<Division> mDivisions = new List<Division>();
            if (mDivisionLister != null && mDivisionLister.SearchCriteria != null &&
                mDivisionLister.SearchCriteria.ZoneIds != null && mDivisionLister.SearchCriteria.ZoneIds.Count() > 0)
                mDivisions = _divisionService.GetDivisionList(mDivisionLister);

            return Json(mDivisions, JsonRequestBehavior.AllowGet);
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

        public ActionResult GetAssestBy(int siteId, int assetTypeId)
        {
            return Json(_assetService.GetAllAssetOnly(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        // Server-side proxy to fetch all Asset Attribute definitions (Id → Title mapping)
        public ActionResult GetAssetAttributes()
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetAttribute/GetAll").Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Server-side proxy to call History API (avoids browser CORS issues)
        public async Task<ActionResult> GetHistoryData(int assetId, string startDate, string endDate)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format("{0}/api/HistoryValue?AssetId={1}&StartDate={2}&EndDate={3}",
                    baseUrl, assetId, startDate, endDate);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    var response = await client.GetAsync(apiUrl);
                    string jsonString = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /*
         * =================================================================
         * DASHBOARD HISTORY PROXIES — TRANSPORT ONLY
         * =================================================================
         *
         * Pure pass-through over the EdgeX DataAPI: build the documented
         * request, return the upstream body VERBATIM (no parsing, no row
         * filtering, no injected fields, no server-side cursor walking).
         *
         *   GET /api/DashboardHistory
         *   GET /api/DashboardPMHistoryOperation
         *   GET /api/DashboardPMHistoryOperationArray
         *   GET /api/DashboardPMHistoryIntegration
         *   GET /api/DashboardPMHistoryVibration
         *
         * Shared query: AssetId, StartDate/EndDate (ddMMyyyy_HHmmss),
         * tsLimit (default 50, max 500), cursor, page (echoed only),
         * fillGaps, sort=desc, attrIds (PM only).
         *
         * Response: { columns[], rows[], page, tsLimit, nextCursor, hasNextPage }
         *
         * PAGINATION is CURSOR-based: page N+1 is reachable only by replaying
         * page N's nextCursor. nextCursor: null + hasNextPage: false = last page.
         */

        private const string ApiDashboardHistory = "DashboardHistory";
        private const string ApiPmHistoryOperation = "DashboardPMHistoryOperation";
        private const string ApiPmHistoryOperationArray = "DashboardPMHistoryOperationArray";
        private const string ApiPmHistoryIntegration = "DashboardPMHistoryIntegration";
        private const string ApiPmHistoryVibration = "DashboardPMHistoryVibration";

        // Newest-first.
        private const string SortNewestFirst = "desc";

        // tsLimit — "Distinct timestamps per page (default 50, max 500)".
        private const int DefaultTsLimit = 50;
        private const int MaxTsLimit = 500;

        /*
         * fillGaps defaults to true (LOCF): Direction is computed from
         * level-held VPT NWKR / RWKR indication attrs that have no fresh
         * observation at an operation row's timestamp; without LOCF Direction
         * resolves to null. LOCF never mints rows, it only fills columns of
         * rows that already exist.
         *
         * Vibration opts out: events are correlated by exact TimestampDevice,
         * and LOCF smears one real event across every row another tag mints.
         *
         * Operation opts out: LOCF can carry both a Normal-side and a
         * Reverse-side attribute onto one boundary row, so presence no longer
         * tells which direction is current. The view's
         * resolvePMOperationDirection tie-break handles the remainder.
         */
        private const string FillGapsAlways = "true";
        private const string FillGapsVibration = "false";
        private const string FillGapsOperation = "false";

        // A single cursor page is small; fail fast rather than hang the tab.
        private const int UpstreamTimeoutMinutes = 2;

        /*
         * Point Machine "Operation" column whitelist (the optional `attrIds`
         * override): Normal/Reverse pairs for Avg Current, 110 DC Loc Avg
         * Voltage, Operation Time, Max/Min and Peak values, A End and B End.
         * Selects COLUMNS at the source; it is not a row trim.
         */
        private const string PmOperationAttrIds =
            "1002,6002,2002,7002,1005,6005,3002,8002,4002,9002,3005,8005," +
            "1004,6004,2004,7004,3004,8004,4004,9004," +
            "1001,6001,2001,7001,3001,8001,4001,9001";

        /*
         * GET /FRS25/TelemetryHistory/GetHistoryDataDashboard
         *
         * Proxy over /api/DashboardHistory — the pre-pivoted grid endpoint
         * (rows = timestamps, columns = tags), newest-first. assetId is a
         * string so IPS / ELD can union several assets ("1,2,3") into one
         * request. tsLimit overrides pageSize when both are supplied (report
         * download path); fillGaps overrides the per-endpoint default (IPS
         * battery charging / discharging sums send false).
         */
        public async Task<ActionResult> GetHistoryDataDashboard(
            string assetId,
            string startDate,
            string endDate,
            int? page = null,
            string cursor = null,
            int? pageSize = null,
            string roleType = null,
            string attrIds = null,
            int? tsLimit = null,
            bool? fillGaps = null)
        {
            return await ProxyDashboardApiAsync(
                ApiDashboardHistory,
                assetId, startDate, endDate,
                page, cursor, tsLimit ?? pageSize,
                attrIds: attrIds,
                roleType: roleType,
                fillGapsOverride: fillGaps);
        }

        /*
         * GET /FRS25/TelemetryHistory/GetPMHistoryOperationArray
         *
         * Proxy over /api/DashboardPMHistoryOperationArray — used only to
         * fetch the IPT waveform arrays for the operation popup graph.
         */
        public async Task<ActionResult> GetPMHistoryOperationArray(
            int assetId,
            string startDate,
            string endDate,
            int? page = null,
            int? tsLimit = null,
            string cursor = null)
        {
            return await ProxyDashboardApiAsync(
                ApiPmHistoryOperationArray,
                assetId.ToString(), startDate, endDate,
                page, cursor, pageSize: tsLimit,
                attrIds: null);
        }

        /*
         * GET /FRS25/TelemetryHistory/GetPMHistoryOperation
         *
         * Proxy over /api/DashboardPMHistoryOperation. Point Machine
         * operational columns (PmOperationAttrIds) plus the computed
         * Direction column at v[0]. fillGaps=false — see FillGapsOperation.
         */
        public async Task<ActionResult> GetPMHistoryOperation(
            int assetId,
            string startDate,
            string endDate,
            int? page = null,
            string cursor = null,
            int? pageSize = null)
        {
            return await ProxyDashboardApiAsync(
                ApiPmHistoryOperation,
                assetId.ToString(), startDate, endDate,
                page, cursor, pageSize: pageSize,
                attrIds: PmOperationAttrIds);
        }

        /*
         * GET /FRS25/TelemetryHistory/GetPMHistoryIntegration
         *
         * Proxy over /api/DashboardPMHistoryIntegration. Indication (RDPMS)
         * plus Combined DataLogger columns, with Direction at v[0].
         * fillGaps=true: indication state is level-held.
         */
        public async Task<ActionResult> GetPMHistoryIntegration(
            int assetId,
            string startDate,
            string endDate,
            int? page = null,
            string cursor = null,
            int? pageSize = null)
        {
            return await ProxyDashboardApiAsync(
                ApiPmHistoryIntegration,
                assetId.ToString(), startDate, endDate,
                page, cursor, pageSize: pageSize,
                attrIds: null);
        }

        /*
         * GET /FRS25/TelemetryHistory/GetPMHistoryVibration
         *
         * Proxy over /api/DashboardPMHistoryVibration. Column whitelist is
         * built upstream from the asset's vibration tags; no Direction column.
         */
        public async Task<ActionResult> GetPMHistoryVibration(
            int assetId,
            string startDate,
            string endDate,
            int? page = null,
            string cursor = null,
            int? pageSize = null)
        {
            return await ProxyDashboardApiAsync(
                ApiPmHistoryVibration,
                assetId.ToString(), startDate, endDate,
                page, cursor, pageSize,
                attrIds: null);
        }

        // -----------------------------------------------------------------
        // Shared proxy plumbing
        // -----------------------------------------------------------------

        /*
         * The one code path every Dashboard History action takes:
         * build URL -> GET -> return the body verbatim.
         */
        private async Task<ActionResult> ProxyDashboardApiAsync(
            string endpoint,
            string assetId,
            string startDate,
            string endDate,
            int? page,
            string cursor,
            int? pageSize,
            string attrIds,
            string roleType = null,
            bool? fillGapsOverride = null)
        {
            try
            {
                string baseUrl = ResolveDashboardBaseUrl();
                if (baseUrl == null)
                {
                    return Json(
                        new { error = "DashboardHistoryBaseUrl / ProxyBaseUrl is not configured." },
                        JsonRequestBehavior.AllowGet);
                }

                int tsLimit = ResolveTsLimit(pageSize);
                int requestedPage = ResolvePage(page);

                string apiUrl = BuildDashboardApiUrl(
                    baseUrl, endpoint, assetId, startDate, endDate,
                    tsLimit, requestedPage, cursor, attrIds, roleType,
                    fillGapsOverride);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(UpstreamTimeoutMinutes);

                    var response = await client.GetAsync(apiUrl);
                    string jsonString = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        return Json(
                            new { error = "API returned status: " + response.StatusCode, page = requestedPage },
                            JsonRequestBehavior.AllowGet);
                    }

                    // VERBATIM. Not parsed, not re-serialised, not augmented.
                    return Content(jsonString, "application/json");
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Prefer the dedicated setting; fall back to ProxyBaseUrl.
        private static string ResolveDashboardBaseUrl()
        {
            string baseUrl = ConfigurationManager.AppSettings["DashboardHistoryBaseUrl"];

            if (string.IsNullOrWhiteSpace(baseUrl))
                baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];

            return string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.TrimEnd('/');
        }

        /*
         * Page size precedence: client `pageSize` > web.config
         * `DashboardHistoryPageSize` > DefaultTsLimit. Clamped to the
         * documented maximum of 500 either way.
         */
        private static int ResolveTsLimit(int? pageSize)
        {
            int tsLimit = DefaultTsLimit;

            string configured = ConfigurationManager.AppSettings["DashboardHistoryPageSize"];
            int parsed;
            if (!string.IsNullOrWhiteSpace(configured) &&
                int.TryParse(configured, out parsed) &&
                parsed > 0)
            {
                tsLimit = Math.Min(parsed, MaxTsLimit);
            }

            if (pageSize.HasValue && pageSize.Value > 0)
                tsLimit = Math.Min(pageSize.Value, MaxTsLimit);

            return tsLimit;
        }

        private static int ResolvePage(int? page)
        {
            return (page.HasValue && page.Value > 0) ? page.Value : 1;
        }

        /*
         * Builds the documented query string. `cursor` is appended last, only
         * when present, and EXACTLY as received from the previous page's
         * nextCursor (no escaping) — upstream compares it byte-for-byte, and
         * "&cursor=" can be read by some upstream builds as a real empty cursor.
         */
        private static string BuildDashboardApiUrl(
            string baseUrl,
            string endpoint,
            string assetId,
            string startDate,
            string endDate,
            int tsLimit,
            int page,
            string cursor,
            string attrIds,
            string roleType = null,
            bool? fillGapsOverride = null)
        {
            string fillGapsValue = fillGapsOverride.HasValue
                ? (fillGapsOverride.Value ? "true" : "false")
                : ResolveFillGaps(endpoint);

            string url = string.Format(
                "{0}/api/{1}" +
                "?AssetId={2}" +
                "&StartDate={3}" +
                "&EndDate={4}" +
                "&tsLimit={5}" +
                "&sort={6}" +
                "&fillGaps={7}" +
                "&page={8}",
                baseUrl,
                endpoint,
                assetId,
                Uri.EscapeDataString(startDate ?? string.Empty),
                Uri.EscapeDataString(endDate ?? string.Empty),
                tsLimit,
                SortNewestFirst,
                fillGapsValue,
                page);

            if (!string.IsNullOrWhiteSpace(attrIds))
                url += "&attrIds=" + Uri.EscapeDataString(attrIds);

            // roleType filters grid columns to one tag role (e.g. "v" = Vibration).
            if (!string.IsNullOrWhiteSpace(roleType))
                url += "&roletype=" + Uri.EscapeDataString(roleType);

            if (!string.IsNullOrWhiteSpace(cursor))
                url += "&cursor=" + cursor;

            return url;
        }

        /*
         * Per-endpoint fillGaps value. Vibration and Operation opt out (see
         * above). OperationArray exists only to fetch waveforms; LOCF would
         * carry one real array onto every later row, so the closest-array
         * lookup in pmFetchAndShowWaveform would plot an older trace.
         */
        private static string ResolveFillGaps(string endpoint)
        {
            if (endpoint == ApiPmHistoryVibration) return FillGapsVibration;
            if (endpoint == ApiPmHistoryOperation) return FillGapsOperation;
            if (endpoint == ApiPmHistoryOperationArray) return "false";
            return FillGapsAlways;
        }

        public async Task<ActionResult> GetUserAssetInfoDatalogger(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = await hcf.client.GetAsync("AssetInfoDatalogger/GetUserAssetInfoDatalogger/SiteId/" + siteId);
                    string jsonString = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public async Task<ActionResult> GetUserAssetInfo(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = await hcf.client.GetAsync("AssetInfo/GetUserAssetInfo/SiteId/" + siteId);
                    string json = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(json, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Vibration sensor configuration for a site: which Point Machine assets
        // carry a vibration sensor and on which end (VibrationTypeId 1 = A End,
        // 2 = B End). Pure pass-through of VibrationConfig/SiteId/{siteId}.
        public async Task<ActionResult> GetVibrationConfig(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = await hcf.client.GetAsync("VibrationConfig/SiteId/" + siteId);
                    string json = await response.Content.ReadAsStringAsync();

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(json, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Asset types available at one station (the Asset Type dropdown is
        // rebuilt when the station changes).
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
                    ViewBag.Error = ex.Message;
                }
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        public ActionResult GetSiteById(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Site/GetSiteById/" + siteId).Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        return Content(json, "application/json");
                    }
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Per-site FRS attribute ranges (min / max per attribute) used to
        // colour out-of-range cells in the history table.
        [HttpGet]
        public ActionResult GetFRSAttributeRangeBySiteId(int siteId)
        {
            string jsonString = "[]";

            try
            {
                if (siteId <= 0)
                {
                    return Content(jsonString, "application/json");
                }

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(string.Format("FRSAttributeRange/SiteId/{0}", siteId)).Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var body = response.Content.ReadAsStringAsync().Result;
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            jsonString = body;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                jsonString = "[]";
            }

            return Content(jsonString, "application/json");
        }

        #region Legacy FRS endpoints (SIP replay / Telemetry Live)

        // SIP replay pulls a whole window in one page (tsLimit up to 10000).
        private const int LegacyMaxTsLimit = 10000;

        // Proxy over /api/DashboardHistory with caller-chosen sort / fillGaps.
        public ActionResult GetDashboardHistoryData(int assetId, string startDate, string endDate,
            int page = 1, int pageSize = 50, string sort = "desc", bool fillGaps = true,
            string cursor = null)
        {
            try
            {
                if (page < 1) page = 1;
                pageSize = ClampLegacyPageSize(pageSize);
                if (string.IsNullOrEmpty(sort)) sort = "desc";

                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format(
                    "{0}/api/DashboardHistory?AssetId={1}&StartDate={2}&EndDate={3}&tsLimit={4}&sort={5}&fillGaps={6}&page={7}",
                    baseUrl, assetId, startDate, endDate, pageSize, sort, fillGaps.ToString().ToLower(), page);

                apiUrl = AppendCursor(apiUrl, cursor);

                return _ProxyGet(apiUrl, 120);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Point Machine operation data (upstream not paginated).
        public ActionResult GetDashboardPMOperationData(int assetId, string startDate, string endDate,
            string sort = "desc", bool fillGaps = true)
        {
            try
            {
                if (string.IsNullOrEmpty(sort)) sort = "desc";
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format(
                    "{0}/api/DashboardPMHistoryOperation?AssetId={1}&StartDate={2}&EndDate={3}&sort={4}&fillGaps={5}",
                    baseUrl, assetId, startDate, endDate, sort, fillGaps.ToString().ToLower());
                return _ProxyGet(apiUrl, 120);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        // Point Machine indication + datalogger data (cursor-paginated).
        public ActionResult GetDashboardPMIntegrationData(int assetId, string startDate, string endDate,
            int page = 1, int pageSize = 50, string sort = "desc", bool fillGaps = true,
            string cursor = null)
        {
            try
            {
                if (page < 1) page = 1;
                pageSize = ClampLegacyPageSize(pageSize);
                if (string.IsNullOrEmpty(sort)) sort = "desc";
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format(
                    "{0}/api/DashboardPMHistoryIntegration?AssetId={1}&StartDate={2}&EndDate={3}&tsLimit={4}&sort={5}&fillGaps={6}&page={7}",
                    baseUrl, assetId, startDate, endDate, pageSize, sort, fillGaps.ToString().ToLower(), page);

                apiUrl = AppendCursor(apiUrl, cursor);

                return _ProxyGet(apiUrl, 120);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private static int ClampLegacyPageSize(int pageSize)
        {
            if (pageSize < 1) return DefaultTsLimit;
            return Math.Min(pageSize, LegacyMaxTsLimit);
        }

        // Cursor replayed verbatim; omitted when blank ("first page").
        private static string AppendCursor(string apiUrl, string cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor)) return apiUrl;
            return apiUrl + "&cursor=" + cursor;
        }

        // Shared HttpClient GET helper — passes upstream JSON through as-is.
        private ActionResult _ProxyGet(string apiUrl, int timeoutSeconds)
        {
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                var response = client.GetAsync(apiUrl).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return Content(jsonString, "application/json");
                }
                return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
            }
        }

        #endregion

        #region Private Method

        private List<Domain.AssetType> GetFRSAssetType()
        {
            return (from E7FRSAdvance.Utility.Utility.FRSAssetType e in Enum.GetValues(typeof(E7FRSAdvance.Utility.Utility.FRSAssetType))
                    select new Domain.AssetType
                    {
                        Id = (int)e,
                        Name = e.ToString().Replace("_", " ")
                    }).ToList();
        }

        #endregion
    }
}
