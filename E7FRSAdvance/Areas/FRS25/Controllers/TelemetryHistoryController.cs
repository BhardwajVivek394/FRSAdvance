using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
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
            Helper.FilterCacheHelper.SetFilterViewBag(ViewBag,_siteService,_zoneService,_divisionService,includeAssetType: false);
            // FRS asset types are enum-based, so retain existing behaviour.
            ViewBag.AssetTypes = new SelectList(GetFRSAssetType(),"Id","Name");
            return View();
        }

        public JsonResult GetDivisionByZoneId(int zoneId)
        {
            return Json(_divisionService.GetByZoneId(zoneId), JsonRequestBehavior.AllowGet);
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
            return Json(_assetService.GetAssestBy(siteId, assetTypeId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Asset types available at one station. The Asset Type dropdown is
        /// rendered once from ViewBag.AssetTypes at page load, so without this
        /// it keeps showing the first station's types after the user picks a
        /// different station. Mirrors Telemetry Live's endpoint of the same name.
        /// </summary>
        [HttpPost]
        public ActionResult GetAssetTypeBySiteId(int siteId)
        {
            List<Domain.AssetType> mAssetTypes = new List<Domain.AssetType>();
            if (siteId > 0)
            {
                try
                {
                    mAssetTypes = _assetTypeService.GetBy(siteId);
                }
                catch (Exception ex)
                {
                    // An empty list leaves the dropdown untouched rather than
                    // failing the whole page; the client logs the error.
                    ViewBag.Error = ex.Message;
                }
            }

            return Json(mAssetTypes, JsonRequestBehavior.AllowGet);
        }

        // Server-side proxy to call History API (avoids browser CORS issues)
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
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        // Server-side proxy to call History API (avoids browser CORS issues)
        public ActionResult GetHistoryData(int assetId, string startDate, string endDate)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                string apiUrl = string.Format("{0}/api/HistoryValue?AssetId={1}&StartDate={2}&EndDate={3}",
                    baseUrl, assetId, startDate, endDate);

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    var response = client.GetAsync(apiUrl).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /*
         * PAGINATION CONTRACT (DashboardHistory family)
         * ---------------------------------------------
         * The upstream is CURSOR-paginated, NOT offset-paginated.
         *
         *   `page`   is echoed metadata only. Sending page=5 with no cursor
         *            returns page 1 again -- upstream behaviour, not a bug here.
         *   `cursor` is what actually selects the data window. It is the
         *            `nextCursor` returned with the previous page, and it must
         *            be replayed VERBATIM (no re-encoding, no reformatting).
         *
         * Response envelope: { columns[], rows[], page, tsLimit, nextCursor,
         * hasNextPage }. nextCursor: null + hasNextPage: false = last page.
         *
         * So page N+1 is reachable ONLY by carrying page N's cursor forward;
         * the client keeps that chain in serverSideState[idx].cursorByPage.
         */

        // README: tsLimit -- "Distinct timestamps per page (default 50, max 500)".
        private const int DefaultTsLimit = 50;
        private const int MaxTsLimit = 500;

        // Server-side proxy to call the new paginated DashboardHistory API.
        // Used for Track, Signal and all non-Point-Machine asset types.
        // Query params sent to upstream:
        //   AssetId, StartDate, EndDate, tsLimit=<pageSize>, sort=<asc|desc>,
        //   fillGaps=<true|false>, page=<page>, cursor=<nextCursor of page N-1>
        public ActionResult GetDashboardHistoryData(int assetId, string startDate, string endDate,
            int page = 1, int pageSize = 50, string sort = "desc", bool fillGaps = true,
            string cursor = null)
        {
            try
            {
                if (page < 1) page = 1;
                pageSize = ClampPageSize(pageSize);
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

        // Point Machine — OPERATION data.
        //   http://<proxy>/api/DashboardPMHistoryOperation
        //     ?AssetId=..&StartDate=..&EndDate=..&sort=<asc|desc>&fillGaps=<bool>
        // Upstream is NOT paginated (operations are sparse events); the client
        // slices locally with the standard 10/25/50/100 page-size selector.
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

        // Point Machine — INDICATION + DATALOGGER data (paginated).
        //   http://<proxy>/api/DashboardPMHistoryIntegration
        //     ?AssetId=..&StartDate=..&EndDate=..&tsLimit=<pageSize>&sort=<asc|desc>
        //     &fillGaps=<bool>&page=<page>&cursor=<nextCursor of page N-1>
        // Cursor-paginated exactly like DashboardHistory -- see the PAGINATION
        // CONTRACT note above.
        public ActionResult GetDashboardPMIntegrationData(int assetId, string startDate, string endDate,
            int page = 1, int pageSize = 50, string sort = "desc", bool fillGaps = true,
            string cursor = null)
        {
            try
            {
                if (page < 1) page = 1;
                pageSize = ClampPageSize(pageSize);
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

        /// <summary>
        /// Single site record. Report headers resolve Zone / Division from this
        /// when the user searched by station and left those filters on "All".
        /// </summary>
        public ActionResult GetSiteById(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("Site/GetSiteById/" + siteId).Result;
                    string json = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(json, "application/json");
                    }

                    return Json(new { error = "API returned status: " + response.StatusCode },
                        JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        /// <summary>
        /// Per-site vibration configuration (thresholds and per-end wiring).
        /// The vibration table needs this to know which attribute belongs to
        /// which end and what counts as out of range.
        /// </summary>
        public ActionResult GetVibrationConfig(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("VibrationConfig/SiteId/" + siteId).Result;
                    string json = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(json, "application/json");
                    }

                    return Json(new { error = "API returned status: " + response.StatusCode },
                        JsonRequestBehavior.AllowGet);
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        #region v616-compatible Dashboard proxies
        /*
         * The Telemetry History feature modules ported from the web project
         * (vibration, IPS/ELD, graphs) call the endpoint NAMES that project
         * uses. Those differ from the names this controller grew, and the
         * shapes differ in two ways that change the DATA, not just the URL:
         *
         *   1. assetId is a STRING here, not an int. IPS and ELD union every
         *      selected asset into ONE request as a comma-separated list
         *      ("43431,43432,..."). An int parameter cannot bind that, so the
         *      union request failed outright and only single-asset searches
         *      ever returned anything.
         *
         *   2. fillGaps is per-endpoint, not always true. Vibration and
         *      Operation opt OUT of the upstream's last-observation-carried-
         *      forward behaviour: with LOCF on, one real reading is repeated
         *      onto every later row in the window, which shows as a column of
         *      identical values that were never actually measured.
         *
         * These are added alongside the existing endpoints rather than
         * replacing them, so the view's own inline code keeps working.
         */
        private const string ApiDashboardHistory = "DashboardHistory";
        private const string ApiPmHistoryOperation = "DashboardPMHistoryOperation";
        private const string ApiPmHistoryOperationArray = "DashboardPMHistoryOperationArray";
        private const string ApiPmHistoryIntegration = "DashboardPMHistoryIntegration";
        private const string ApiPmHistoryVibration = "DashboardPMHistoryVibration";

        private const string SortNewestFirst = "desc";

        // Per-endpoint fillGaps default. See note (2) above.
        private static string ResolveFillGaps(string endpoint)
        {
            if (endpoint == ApiPmHistoryVibration) return "false";
            if (endpoint == ApiPmHistoryOperation) return "false";
            // OperationArray exists only to fetch waveforms; LOCF would carry
            // one real array onto every later row, so the "closest array-shaped
            // row" lookup would plot an older operation's trace.
            if (endpoint == ApiPmHistoryOperationArray) return "false";
            return "true";
        }

        private static string BuildDashboardApiUrl(
            string baseUrl, string endpoint, string assetId,
            string startDate, string endDate, int tsLimit, int page,
            string cursor, string attrIds, string roleType, bool? fillGapsOverride)
        {
            string fillGapsValue = fillGapsOverride.HasValue
                ? (fillGapsOverride.Value ? "true" : "false")
                : ResolveFillGaps(endpoint);

            string url = string.Format(
                "{0}/api/{1}?AssetId={2}&StartDate={3}&EndDate={4}&tsLimit={5}&sort={6}&fillGaps={7}&page={8}",
                baseUrl, endpoint, assetId,
                Uri.EscapeDataString(startDate ?? string.Empty),
                Uri.EscapeDataString(endDate ?? string.Empty),
                tsLimit, SortNewestFirst, fillGapsValue, page);

            if (!string.IsNullOrWhiteSpace(attrIds))
                url += "&attrIds=" + Uri.EscapeDataString(attrIds);

            // roleType filters grid columns to one tag role (e.g. "v" = Vibration).
            if (!string.IsNullOrWhiteSpace(roleType))
                url += "&roletype=" + Uri.EscapeDataString(roleType);

            // Sent EXACTLY as received from the previous page's nextCursor --
            // no escaping, no reformatting; upstream compares it byte-for-byte.
            if (!string.IsNullOrWhiteSpace(cursor))
                url += "&cursor=" + cursor;

            return url;
        }

        private ActionResult ProxyDashboard(
            string endpoint, string assetId, string startDate, string endDate,
            int? page, string cursor, int? pageSize, string attrIds,
            string roleType = null, bool? fillGaps = null)
        {
            try
            {
                string baseUrl = ConfigurationManager.AppSettings["ProxyBaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    return Json(new { error = "ProxyBaseUrl is not configured." },
                        JsonRequestBehavior.AllowGet);
                }

                /*
                 * NOT clamped to MaxTsLimit.
                 *
                 * The 500 cap exists for the on-screen pager, where a page is
                 * something a person scrolls. The export is a different job: it
                 * asks for the window's own totalRecords so the file holds every
                 * row, and clamping that back to 500 silently truncated the
                 * report to its first page. Only a missing or nonsense value
                 * falls back to the default.
                 */
                int tsLimit = (pageSize.HasValue && pageSize.Value > 0)
                    ? pageSize.Value
                    : DefaultTsLimit;
                int requestedPage = (page.HasValue && page.Value > 0) ? page.Value : 1;

                string apiUrl = BuildDashboardApiUrl(
                    baseUrl, endpoint, assetId, startDate, endDate,
                    tsLimit, requestedPage, cursor, attrIds, roleType, fillGaps);

                return _ProxyGet(apiUrl, 120);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult GetHistoryDataDashboard(string assetId, string startDate, string endDate,
            int? page = null, string cursor = null, int? pageSize = null,
            string roleType = null, string attrIds = null, int? tsLimit = null, bool? fillGaps = null)
        {
            // tsLimit overrides pageSize as the record cap when both are sent --
            // the download path needs pageSize=1 with tsLimit=totalRecords.
            return ProxyDashboard(ApiDashboardHistory, assetId, startDate, endDate,
                page, cursor, tsLimit ?? pageSize, attrIds, roleType, fillGaps);
        }

        public ActionResult GetPMHistoryOperation(string assetId, string startDate, string endDate,
            int? page = null, string cursor = null, int? pageSize = null, string attrIds = null)
        {
            return ProxyDashboard(ApiPmHistoryOperation, assetId, startDate, endDate,
                page, cursor, pageSize, attrIds);
        }

        public ActionResult GetPMHistoryOperationArray(string assetId, string startDate, string endDate,
            int? page = null, string cursor = null, int? pageSize = null, string attrIds = null)
        {
            return ProxyDashboard(ApiPmHistoryOperationArray, assetId, startDate, endDate,
                page, cursor, pageSize, attrIds);
        }

        public ActionResult GetPMHistoryIntegration(string assetId, string startDate, string endDate,
            int? page = null, string cursor = null, int? pageSize = null, string attrIds = null)
        {
            return ProxyDashboard(ApiPmHistoryIntegration, assetId, startDate, endDate,
                page, cursor, pageSize, attrIds);
        }

        public ActionResult GetPMHistoryVibration(string assetId, string startDate, string endDate,
            int? page = null, string cursor = null, int? pageSize = null,
            string roleType = null, string attrIds = null)
        {
            return ProxyDashboard(ApiPmHistoryVibration, assetId, startDate, endDate,
                page, cursor, pageSize, attrIds, roleType);
        }
        #endregion

        // tsLimit is documented as "default 50, max 500" -- a larger value is
        // silently rejected upstream, which would return a differently-sized
        // page than the client is paginating against.
        private static int ClampPageSize(int pageSize)
        {
            if (pageSize < 1) return DefaultTsLimit;
            return Math.Min(pageSize, MaxTsLimit);
        }

        /*
         * Append the cursor that selects page N+1's data window.
         *
         * Sent EXACTLY as received from the previous page's nextCursor -- no
         * Uri.EscapeDataString, no reformatting: the upstream compares it
         * byte-for-byte against the TimestampDevice it emitted. Omitted
         * entirely when blank, because "&cursor=" is read by some upstream
         * builds as a real (empty) cursor rather than "first page".
         */
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

        public ActionResult GetUserAssetInfoDatalogger(int siteId)
        {
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync("AssetInfoDatalogger/GetUserAssetInfoDatalogger/SiteId/" + siteId).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(jsonString, "application/json");
                    }
                    else
                    {
                        return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                    }
                }
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }
        public async Task<ActionResult> GetUserAssetInfo(int siteId)
        {
            using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
            {
                var response = hcf.client.GetAsync("AssetInfo/GetUserAssetInfo/SiteId/" + siteId).Result;
                string jsonString = response.Content.ReadAsStringAsync().Result;
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    return Content(json, "application/json");
                }
                else
                {
                    return Json(new { error = "API returned status: " + response.StatusCode }, JsonRequestBehavior.AllowGet);
                }
            }
        }
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
