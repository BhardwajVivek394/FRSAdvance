using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using E7FRSAdvance.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // MaintenceRosterController -- component version (Major.Minor.Build.Revision)
    // Version: 2.8.8.0
    //
    // Version history (one line per shipped version, newest first):
    // 2.8.8.0 - TrackShort: major_events reach the browser as real objects (RosPlain; JavaScriptSerializer was turning the
    //     Newtonsoft JArray into nested empty arrays), and the event-count fallback no longer raises a track the model
    //     reads as MONITOR (low / resolved) -- appSetting RosTrkEventsRaiseMonitor=true restores the old rule.
    // 2.8.7.0 - Paired UI binds Ask AI to the real AiChat/AnalyzeChat contract: named context keys are filled and the
    //     roster evidence travels as text messages (AnalyzeChat ignores unknown context keys). Version stamp only here.
    // 2.8.6.0 - Paired UI adds Back to worksheet on a review opened from the station worksheet, and a blocked
    //     asset acknowledgement now names its real reason. Version stamp only here.
    // 2.8.5.0 - Paired UI binds to the saved generator run (no browser re-sweep on load), gives detail tabs and
    //     Ask AI a priority request lane, and reads saved evidence for site/division AI. Version stamp only here.
    // 2.8.4.0 - Paired UI restores alert drill-in, AI diagnostics/logs, follow-up chat and drift/track reading plots.
    // 2.8.3.0 - Preview-parity worksheet is embedded in Index; only controller + Index are required.
    // 2.8.2.0 - Index and Division render the agreed division workspace; Worksheet retains diagnostics.
    // 2.8.1.0 - Scoped 15-day AI alert evidence with explicit incomplete-read reporting.
    // 2.1.0.0 - GenerateDaily now sends REAL ITEMS. 2.0.0.0 posted an empty Items list, so the
    //     API dutifully wrote a run with zero items and the page showed an empty roster -- the
    //     endpoint worked and the feature did not, which is the worst combination.
    //     Regenerate now pulls the last 15 days of FRS alerts for the division and hands them
    //     to RosterItemBuilder, which applies the evidence rule (count, active-now, first vs
    //     second half, dominant cause) and returns ranked items. AlertsRead / AlertsTotal and a
    //     SourceNote travel with the request, so a run built from a truncated read is recorded
    //     as PARTIAL instead of presenting itself as complete.
    //     The ALERT WINDOW IS NOT the Active-only filter the old page used: a roster needs the
    //     alerts that RESET too, because an asset that failed five times and recovered each
    //     time is exactly the degrading one, and an Active-only query cannot see it.
    // 2.0.0.0 - ROSTER v2. The page is no longer a live dashboard recomputed in the browser;
    //     it READS a roster the nightly job generated and persisted, through the new
    //     api/Roster endpoints. Consequences that shaped this file:
    //       * Every action is a thin async proxy. No business logic lives here -- ranking,
    //         carry-forward, downgrade and failed-repair all belong to the service behind
    //         the API, so the station worksheet and the division roll-up can never diverge.
    //       * NOTHING is swallowed. Every failure returns { IsSuccess:false, Message } and
    //         the page prints the reason. The old file's three empty catch blocks turned a
    //         dead service, an expired token and an empty division into the same silent
    //         zeros -- the defect this whole redesign exists to remove.
    //       * async/await throughout: the old .Result on PostAsync/GetAsync inside an MVC
    //         action is the classic ASP.NET deadlock, which appears under load and never in
    //         testing.
    //       * The UserId header is set EXPLICITLY on every call. The API takes the closer's
    //         identity from that header and refuses a closure without it; relying on
    //         HttpClientFactory to add it would be an assumption this file cannot verify.
    // 1.0.1.0 - hardening pass on the old live-dashboard controller (superseded).
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public partial class MaintenceRosterController : Controller
    {
        public const string ComponentVersion = "2.8.8.0";

        // Master switch for the server-side composite. On: Regenerate folds track-short / pm /
        // drift / cause-SHORT into each item's persisted Priority, and the browser stops
        // re-fetching the same signals. Off: behaviour reverts to alert-only counts with the
        // browser doing its own per-asset scoring, exactly as 2.1.0.0 did.
        // Override without a rebuild: appSetting RosCompositeOff = true.
        private static bool RosCompositeOn
        {
            get
            {
                try
                {
                    return (System.Configuration.ConfigurationManager.AppSettings["RosCompositeOff"] ?? "").Trim().ToUpperInvariant() != "TRUE";
                }
                catch
                {
                    return true;
                }
            }
        }

        private readonly ISiteService _siteService;
        private readonly IZoneService _zoneService;
        private readonly IDivisionService _divisionService;
        private readonly IFRSAlertService _alertService;
        private readonly IAssetService _assetService;

        public MaintenceRosterController(ISiteService siteService, IZoneService zoneService, IDivisionService divisionService, IFRSAlertService alertService, IAssetService assetService)
        {
            _siteService = siteService;
            _zoneService = zoneService;
            _divisionService = divisionService;
            _alertService = alertService;
            _assetService = assetService;
        }

        public ActionResult GetAssestBy(int siteId)
        {
            return Json(_assetService.GetAssestBy(siteId), JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// The 15-day alert window for one division.
        /// AlertStatus is deliberately NOT set to Active: a roster ranks on history, and an
        /// asset that failed five times and reset each time is precisely the degrading one an
        /// Active-only query cannot see. Take = -1 for the same reason the old page used it --
        /// a truncated list would under-count and mis-rank; the count that comes back is
        /// reported to the API so a partial read is visible rather than assumed complete.
        /// </summary>
        private List<Domain.FRSAlert> FetchWindowAlerts(int divisionId, DateTime rosterDate, out int totalRecord)
        {
            totalRecord = 0;
            Domain.FRSAlertLister lister = new Domain.FRSAlertLister();
            if (lister.SearchCriteria == null) { lister.SearchCriteria = new Domain.FRSAlert(); }
            if (lister.Pager == null) { lister.Pager = new Domain.Pager(); }

            lister.SearchCriteria.DivisionId = divisionId;
            lister.SearchCriteria.FromDate = rosterDate.Date.AddDays(-Helper.RosterItemBuilder.WindowDays);
            lister.SearchCriteria.ToDate = rosterDate.Date.AddDays(1).AddSeconds(-1);
            lister.SearchCriteria.UserId = ClsHttpContent.LoginUser != null ? ClsHttpContent.LoginUser.Id : 0;
            lister.SearchCriteria.RoleId = ClsHttpContent.LoginUser != null ? ClsHttpContent.LoginUser.RoleId : 0;
            lister.Pager.Take = -1;

            lister = _alertService.GetListerWithPagination(lister);

            if (lister == null || lister.mFRSAlerts == null)
            {
                return new List<Domain.FRSAlert>();
            }
            if (lister.Pager != null)
            {
                totalRecord = Convert.ToInt32(lister.Pager.TotalRecord);
            }
            // Exclude alerts acknowledged as Maintenance or plain Acknowledge -- these are handled
            // administratively and must not drive the roster ranking (same spirit as skipping Test).
            int maintId = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace /* 616: .Maintenance (4) */;
            int ackId = 6 /* 616: AcknowledgemenStatus.Acknowledge; not in the 617 enum */;
            List<Domain.FRSAlert> kept = new List<Domain.FRSAlert>();
            int droppedMaint = 0;
            int droppedAck = 0;
            foreach (Domain.FRSAlert a in lister.mFRSAlerts)
            {
                if (a == null) { continue; }
                int sid = 0;
                try { sid = Convert.ToInt32(a.AcknowledgemenStatusId); } catch { sid = 0; }

                if (sid == maintId)
                {
                    droppedMaint++;
                    continue;
                }

                if (sid == ackId)
                {
                    droppedAck++;
                    continue;
                }

                kept.Add(a);
            }

            // Say what was excluded. "The roster still shows a maintenance alert" is
            // unanswerable when the filter drops them silently -- this line settles whether
            // the alert was filtered, or was simply acknowledged AFTER the roster was built.
            RosLog("INFO", "FetchWindowAlerts division=" + divisionId
                + " read=" + lister.mFRSAlerts.Count
                + " kept=" + kept.Count
                + " droppedMaintenance=" + droppedMaint
                + " droppedAcknowledge=" + droppedAck);

            totalRecord = kept.Count;   // report the roster-relevant count (avoids a false PARTIAL)
            return kept;
        }

        // =====================================================================
        // PATCH for MaintenceRosterController.cs  --  make RosLog VISIBLE
        //
        // WHY: the existing RosLog writes only to System.Diagnostics.Trace:
        //        Trace.WriteLine("[ROSTER-WEB] " + level + " " + message);
        //      In production there is usually NO TraceListener, so every roster
        //      log line (INFO/WARN/ERROR and all the new track DEBUG lines) goes
        //      nowhere. This patch ALSO appends each line to a daily log file so
        //      you can actually read it while debugging. Trace is kept for dev.
        //
        // WHERE THE FILE GOES:
        //   default:  ~/App_Data/RosterLog/roster-YYYYMMDD.log
        //   override: <add key="RosLogDir" value="D:\logs\roster" /> in Web.config
        //   (App_Data is not web-served, so the log is not publicly reachable.)
        //
        // LEVELS: nothing is filtered -- INFO/WARN/ERROR/DEBUG are all written.
        //   To mute DEBUG in production, set  <add key="RosLogMinLevel" value="INFO" />
        //   (accepted: DEBUG < INFO < WARN < ERROR). Omit it to log everything.
        //
        // APPLY: replace the existing RosLog method (around line 115) with the
        //        version below, and add the two static fields just above it.
        // =====================================================================

        // ---- add these two static fields next to the other private statics ----
        private static readonly object _rosLogLock = new object();
        private static int _rosLogMinRank = -1;   // resolved once from config; -1 = not yet
        private static bool _rosLogBanner = false;
        private static bool rosLogSinkFailed = false;   // one-shot alarm: the file sink died

        // ---- OLD (delete) --------------------------------------------------
        //   private static void RosLog(string level, string message)
        //   {
        //       try
        //       {
        //           System.Diagnostics.Trace.WriteLine("[ROSTER-WEB] " + level + " " + message);
        //       }
        //       catch { }
        //   }
        // ---- NEW (paste) ---------------------------------------------------
        private static int RosLevelRank(string level)
        {
            if (string.IsNullOrEmpty(level)) { return 1; }
            switch (level.Trim().ToUpperInvariant())
            {
                case "DEBUG": return 0;
                case "INFO": return 1;
                case "WARN": return 2;
                case "ERROR": return 3;
                default: return 1;
            }
        }

        private static void RosLogRaw(string level, string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [ROSTER-WEB v" + ComponentVersion + "] " + level + " " + message;
            try { System.Diagnostics.Trace.WriteLine(line); } catch { }
            try
            {
                string d = (System.Configuration.ConfigurationManager.AppSettings["RosLogDir"] ?? "").Trim();
                if (d.Length == 0) { try { d = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/RosterLog"); } catch { d = null; } if (string.IsNullOrEmpty(d)) { d = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data\\RosterLog"); } }
                if (!System.IO.Directory.Exists(d)) { System.IO.Directory.CreateDirectory(d); }
                lock (_rosLogLock) { System.IO.File.AppendAllText(System.IO.Path.Combine(d, "roster-" + DateTime.Now.ToString("yyyyMMdd") + ".log"), line + Environment.NewLine); }
            }
            catch { }
        }

        private static void RosLog(string level, string message)
        {
            // resolve the minimum level once (default: log everything)
            if (_rosLogMinRank < 0)
            {
                string min = (System.Configuration.ConfigurationManager.AppSettings["RosLogMinLevel"] ?? "").Trim();
                _rosLogMinRank = min.Length > 0 ? RosLevelRank(min) : 0;
            }
            if (RosLevelRank(level) < _rosLogMinRank) { return; }

            if (!_rosLogBanner)
            {
                _rosLogBanner = true;
                try { RosLogRaw("INFO", "=== Roster component v" + ComponentVersion + " logging started (pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + ") ==="); } catch { }
            }

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [ROSTER-WEB v" + ComponentVersion + "] " + level + " " + message;

            // keep Trace (visible in dev / DebugView / a configured listener)
            try { System.Diagnostics.Trace.WriteLine(line); } catch { }

            // ALSO append to a daily file so it is readable in production
            try
            {
                string dir = (System.Configuration.ConfigurationManager.AppSettings["RosLogDir"] ?? "").Trim();
                if (dir.Length == 0)
                {
                    try { dir = System.Web.Hosting.HostingEnvironment.MapPath("~/App_Data/RosterLog"); }
                    catch { dir = null; }
                    if (string.IsNullOrEmpty(dir))
                    { dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data\\RosterLog"); }
                }
                if (!System.IO.Directory.Exists(dir)) { System.IO.Directory.CreateDirectory(dir); }
                string path = System.IO.Path.Combine(dir, "roster-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                lock (_rosLogLock) { System.IO.File.AppendAllText(path, line + Environment.NewLine); }
            }
            catch (Exception ex)
            {
                // logging must never throw -- but say ONCE, loudly, that the file sink is dead,
                // so "the log is empty" is never mistaken for "nothing failed".
                if (!rosLogSinkFailed)
                {
                    rosLogSinkFailed = true;

                    try
                    {
                        System.Diagnostics.Trace.WriteLine("[ROSTER-WEB] FATAL roster file log disabled: "
                            + ex.GetType().Name + " " + ex.Message);
                    }
                    catch
                    {
                    }

                    try
                    {
                        System.Diagnostics.EventLog.WriteEntry("Application",
                            "Roster file log disabled: " + ex.GetType().Name + " " + ex.Message,
                            System.Diagnostics.EventLogEntryType.Warning);
                    }
                    catch
                    {
                    }
                }
            }
        }

        // =====================================================================
        // Item 8 helper -- HttpWebRequest.GetResponse() throws on any non-2xx, and
        // ex.Message is only "The remote server returned an error: (500) ...". The
        // upstream's ACTUAL reason (auth failure, unknown asset) lives in ex.Response
        // and was being thrown away. Used by TrackShort / PmOps / RangeHistory.
        // =====================================================================
        private static string RosUpstreamBody(Exception ex)
        {
            try
            {
                System.Net.WebException wex = ex as System.Net.WebException;
                if (wex == null || wex.Response == null)
                {
                    return "";
                }

                System.Net.HttpWebResponse resp = wex.Response as System.Net.HttpWebResponse;
                string status = resp != null ? (" http=" + (int)resp.StatusCode) : "";

                using (System.IO.StreamReader sr = new System.IO.StreamReader(wex.Response.GetResponseStream()))
                {
                    string text = sr.ReadToEnd();
                    if (text != null && text.Length > 300)
                    {
                        text = text.Substring(0, 300) + "...";
                    }

                    return status + " upstream=" + text;
                }
            }
            catch
            {
                return "";
            }
        }
        // =====================================================================
        // After this patch, tail the file to watch the track flow, e.g.:
        //   ~/App_Data/RosterLog/roster-20260829.log
        // and filter for "TrackShort" to see: entry, raw preview, leakage,
        // each major_event's liveNow decision, and the RESULT band.
        // =====================================================================



        /// <summary>
        /// The API resolves the acting user from the "UserId" header (see
        /// RosterController.GetHeaderUserId). Set it on every request rather than trusting
        /// the factory to do it -- a closure posted without it is rejected, and the operator
        /// would see "UserId header is required" with no idea why.
        /// </summary>
        private static void StampUser(HttpClient client)
        {
            try
            {
                int userId = ClsHttpContent.LoginUser != null ? ClsHttpContent.LoginUser.Id : 0;
                client.DefaultRequestHeaders.Remove("UserId");
                if (userId > 0)
                {
                    client.DefaultRequestHeaders.Add("UserId", userId.ToString());
                }
            }
            catch { }
        }

        private JsonResult Fail(string message, HttpStatusCode code)
        {
            RosLog("ERROR", message);
            Response.StatusCode = (int)code;
            Response.TrySkipIisCustomErrors = true;
            JsonResult r = Json(new { IsSuccess = false, Message = message }, JsonRequestBehavior.AllowGet);
            r.MaxJsonLength = int.MaxValue;
            return r;
        }

        // Existing menu URL and the explicit Division URL share one canonical Razor view.
        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult Index()
        {
            PrepareMaintenancePage(false);
            return View("Index");
        }

        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult Division()
        {
            PrepareMaintenancePage(false);
            return View("Index");
        }

        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult Worksheet()
        {
            PrepareMaintenancePage(true);
            return View("Index");
        }

        private void PrepareMaintenancePage(bool worksheet)
        {
            ViewBag.Divisions = _divisionService.GetAll();
            ViewBag.RosterOpenWorksheet = worksheet;
            ViewBag.RosterComponentVersion = ComponentVersion;
            ViewBag.DivisionMaintenanceToday = DateTime.UtcNow.AddMinutes(330).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            bool traceOn = false;
            try { traceOn = Request.QueryString["trace"] == "1" || (System.Configuration.ConfigurationManager.AppSettings["RosTraceUi"] ?? "").Trim().ToUpperInvariant() == "TRUE"; }
            catch { traceOn = false; }
            ViewBag.RosterTrace = traceOn;
            ViewBag.RosterComposite = RosCompositeOn;
            // Verify the deployed assembly in the HTTP response without adding technical UI labels.
            Response.AddHeader("X-RDPMS-Roster-Version", ComponentVersion);
            Response.AddHeader("X-RDPMS-Roster-View", "division");
            RosLog("INFO", (worksheet ? "Worksheet" : "Division") + " page open v" + ComponentVersion + RosWho());
        }

        /// <summary>
        /// All sites under the selected division. The division UI joins this inventory to
        /// the saved roster so sites with no alerts are still independently assessed.
        /// </summary>
        public JsonResult GetRosterSites(int divisionId)
        {
            try
            {
                List<Domain.Site> mSites = Helper.FilterCacheHelper.GetSitesByDivisionId(_siteService, divisionId);
                if (mSites == null)
                {
                    mSites = new List<Domain.Site>();
                }
                if (mSites.Count == 0)
                {
                    RosLog("WARN", "GetRosterSites division=" + divisionId + " returned 0 sites");
                }
                else
                {
                    RosLog("INFO", "GetRosterSites division=" + divisionId + " RESULT sites=" + mSites.Count);
                }
                return Json(mSites, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Fail("Site list unavailable: " + ex.Message, HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Division roll-up for a date -> GET api/Roster/Rollup/Division/{d}/Date/{date}
        /// </summary>
        public async Task<ActionResult> GetRollup(int divisionId, string rosterDate)
        {
            try
            {
                string date = NormalizeDate(rosterDate);
                RosLog("INFO", "GetRollup division=" + divisionId + " date=" + date + RosWho());
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    string url = String.Format("Roster/Rollup/Division/{0}/Date/{1}", divisionId, date);
                    HttpResponseMessage response = await hcf.client.GetAsync(url).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        RosLog("INFO", "GetRollup division=" + divisionId + " date=" + date
                            + " RESULT http=200 bodyLen=" + (body == null ? 0 : body.Length)
                            + RosCounts(body));

                        // No merge here any more. The signal breakdown is persisted on each item
                        // and RosterService projects it into every station line, so what the API
                        // returns is already complete -- and it survives an app-pool recycle.
                        return Content(body, "application/json");
                    }

                    RosLog("ERROR", "Rollup division=" + divisionId + " HTTP " + (int)response.StatusCode + " body=" + Head(body));
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = DescribeHttp("Roster roll-up", response.StatusCode)
                    }), "application/json");
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Rollup division=" + divisionId + " date=" + rosterDate + " "
                    + ex.GetType().Name + " " + ex.Message);
                return Content(JsonConvert.SerializeObject(new
                {
                    IsSuccess = false,
                    Message = "Roster roll-up unavailable: " + ex.Message
                }), "application/json");
            }
        }

        /// <summary>
        /// One station's worksheet -> GET api/Roster/Worksheet/Division/{d}/Site/{s}/Date/{date}
        /// </summary>
        public async Task<ActionResult> GetWorksheet(int divisionId, int siteId, string rosterDate)
        {
            try
            {
                string date = NormalizeDate(rosterDate);
                RosLog("INFO", "GetWorksheet division=" + divisionId + " site=" + siteId + " date=" + date + RosWho());
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    string url = String.Format("Roster/Worksheet/Division/{0}/Site/{1}/Date/{2}", divisionId, siteId, date);
                    HttpResponseMessage response = await hcf.client.GetAsync(url).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        RosLog("INFO", "GetWorksheet division=" + divisionId + " site=" + siteId
                            + " RESULT http=200 bodyLen=" + (body == null ? 0 : body.Length)
                            + RosCounts(body));

                        return Content(body, "application/json");
                    }

                    RosLog("ERROR", "Worksheet division=" + divisionId + " site=" + siteId + " HTTP " + (int)response.StatusCode + " body=" + Head(body));
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = DescribeHttp("Station worksheet", response.StatusCode)
                    }), "application/json");
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Worksheet division=" + divisionId + " site=" + siteId + " date=" + rosterDate + " "
                    + ex.GetType().Name + " " + ex.Message);
                return Content(JsonConvert.SerializeObject(new
                {
                    IsSuccess = false,
                    Message = "Station worksheet unavailable: " + ex.Message
                }), "application/json");
            }
        }

        /// <summary>
        /// Regenerate mid-day -> POST api/Roster/GenerateDaily with Force = true.
        /// The 00:30 run is NOT overwritten; the API records a new run and marks it current,
        /// keeping the scheduled one for audit.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> Regenerate(int divisionId, string rosterDate)
        {
            try
            {
                string date = NormalizeDate(rosterDate);
                int userId = ClsHttpContent.LoginUser != null ? ClsHttpContent.LoginUser.Id : 0;

                DateTime target;
                if (!DateTime.TryParse(date, out target)) { target = DateTime.Now.Date; }

                // Gather the evidence, then build the items. If the fetch itself fails the run
                // is NOT created with an empty list -- an empty roster and a broken roster must
                // never look the same.
                int totalRecord;
                List<Domain.FRSAlert> alerts;
                try
                {
                    alerts = FetchWindowAlerts(divisionId, target, out totalRecord);
                }
                catch (Exception fx)
                {
                    RosLog("ERROR", "Regenerate division=" + divisionId + " alert fetch failed: " + fx.Message);
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = "Could not read the alert history for this division: " + fx.Message
                                + " -- the roster was NOT regenerated."
                    }), "application/json");
                }

                Helper.RosterItemBuilder.BuildResult built =
                    Helper.RosterItemBuilder.Build(alerts, divisionId, target);

                RosLog("INFO", "Regenerate division=" + divisionId + " date=" + date
                    + " alerts=" + built.AlertsRead + "/" + totalRecord
                    + " items=" + built.Items.Count);

                // Fold track-short + pm-ops + pm-prediction + avg-drift into each item's Priority so
                // the PERSISTED roll-up counts are composite -- the landing KPI strip and the station
                // Urg/Soon/Mon columns read those numbers straight off the API and cannot compute them
                // themselves. Never breaks the build: on any failure we post the alert-only items.
                object itemsToPost = built.Items;

                try
                {
                    if (RosCompositeOn)
                    {
                        Newtonsoft.Json.Linq.JArray scored = RosCompositeRescore(built.Items, target, divisionId);

                        // null means the items were not readable as JSON. Post the originals --
                        // posting null would write an EMPTY roster over a good one.
                        if (scored != null)
                        {
                            itemsToPost = scored;
                        }
                    }
                    else
                    {
                        RosLog("WARN", "Rescore SKIPPED -- appSetting RosCompositeOff=true, posting alert-only items");
                    }
                }
                catch (Exception rex)
                {
                    RosLog("ERROR", "Rescore failed, posting alert-only items: "
                        + rex.GetType().Name + " " + rex.Message);
                    itemsToPost = built.Items;
                }

                var payload = new
                {
                    DivisionId = divisionId,
                    RosterDate = date,
                    GeneratedBy = userId > 0 ? "manual:" + userId : "manual",
                    Force = true,
                    AlertsRead = built.AlertsRead,
                    AlertsTotal = totalRecord > 0 ? totalRecord : built.AlertsRead,
                    SourceNote = built.SourceNote,
                    Items = itemsToPost
                };

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    StringContent str = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await hcf.client.PostAsync("Roster/GenerateDaily", str).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(body, "application/json");
                    }

                    RosLog("ERROR", "Regenerate division=" + divisionId + " HTTP " + (int)response.StatusCode + " body=" + Head(body));
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = DescribeHttp("Regenerate", response.StatusCode)
                    }), "application/json");
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Regenerate division=" + divisionId + " date=" + rosterDate + " "
                    + ex.GetType().Name + " " + ex.Message);
                return Content(JsonConvert.SerializeObject(new
                {
                    IsSuccess = false,
                    Message = "Regenerate failed: " + ex.Message
                }), "application/json");
            }
        }

        /// <summary>
        /// The 2-tap closure -> POST api/Roster/CloseItem.
        /// The API takes the closer from the UserId header, so the body carries no identity.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult> CloseItem(long itemId, string closeStatus, string remark)
        {
            try
            {
                if (itemId <= 0)
                {
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = "ItemId is required."
                    }), "application/json");
                }

                closeStatus = (closeStatus ?? "").Trim().ToUpperInvariant();
                remark = (remark ?? "").Trim();
                if ((closeStatus != "CONFIRMED_DEFECT" && closeStatus != "NO_DEFECT_FOUND" && closeStatus != "WORK_DONE") || remark.Length == 0 || remark.Length > 2000) { return Json(new { IsSuccess = false, Message = "Choose a valid outcome and enter a remark of 1–2000 characters." }); }

                var payload = new
                {
                    ItemId = itemId,
                    CloseStatus = closeStatus,
                    Remark = remark
                };

                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    StringContent str = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await hcf.client.PostAsync("Roster/CloseItem", str).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return Content(body, "application/json");
                    }

                    RosLog("ERROR", "CloseItem " + itemId + " HTTP " + (int)response.StatusCode + " body=" + Head(body));
                    return Content(JsonConvert.SerializeObject(new
                    {
                        IsSuccess = false,
                        Message = DescribeHttp("Close item", response.StatusCode)
                    }), "application/json");
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "CloseItem " + itemId + " status=" + closeStatus + " "
                    + ex.GetType().Name + " " + ex.Message);
                return Content(JsonConvert.SerializeObject(new
                {
                    IsSuccess = false,
                    Message = "Close failed: " + ex.Message
                }), "application/json");
            }
        }

        // Asset visit routes are additive. Existing alert acknowledgements keep their own endpoint.
        private const string MaintenanceStateApi = "Roster/MaintenanceState";
        private const string CloseAssetApi = "Roster/CloseAsset";

        [HttpGet]
        public async Task<ActionResult> GetMaintenanceState(int divisionId, int siteId, string rosterDate)
        {
            DateTime day;
            if (divisionId <= 0 || siteId <= 0 || !DateTime.TryParseExact(rosterDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out day)) { return Json(new { IsSuccess = false, Message = "Invalid maintenance scope." }, JsonRequestBehavior.AllowGet); }
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    var response = await hcf.client.GetAsync(MaintenanceStateApi + "?divisionId=" + divisionId + "&siteId=" + siteId + "&rosterDate=" + day.ToString("yyyy-MM-dd")).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) { return Json(new { IsSuccess = false, Message = "Maintenance status unavailable. Check the maintenance API integration." }, JsonRequestBehavior.AllowGet); }
                    return Content(await response.Content.ReadAsStringAsync().ConfigureAwait(false), "application/json");
                }
            }
            catch (Exception ex) { RosLog("WARN", "MaintenanceState " + ex.Message); return Json(new { IsSuccess = false, Message = "Maintenance status unavailable." }, JsonRequestBehavior.AllowGet); }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> CloseAsset(int divisionId, int siteId, int assetId, string rosterDate, string evidenceToken, string requestId, long expectedVisitId, string closeStatus, string remark)
        {
            DateTime day; Guid request;
            remark = (remark ?? "").Trim(); closeStatus = (closeStatus ?? "").Trim().ToUpperInvariant();
            if (divisionId <= 0 || siteId <= 0 || assetId <= 0 || !Guid.TryParse(requestId, out request) || string.IsNullOrWhiteSpace(evidenceToken) || evidenceToken.Length != 64 || !DateTime.TryParseExact(rosterDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out day) || day.Date != DateTime.UtcNow.AddMinutes(330).Date) { return Json(new { IsSuccess = false, Message = "Reload today's saved roster before recording maintenance." }); }
            if (remark.Length == 0 || remark.Length > 2000 || (closeStatus != "CONFIRMED_DEFECT" && closeStatus != "NO_DEFECT_FOUND" && closeStatus != "WORK_DONE")) { return Json(new { IsSuccess = false, Message = "Choose an outcome and enter a remark of 1–2000 characters." }); }
            try
            {
                var payload = new { DivisionId = divisionId, SiteId = siteId, AssetId = assetId, RosterDate = day.ToString("yyyy-MM-dd"), EvidenceToken = evidenceToken, RequestId = request, ExpectedVisitId = expectedVisitId, Outcome = closeStatus, Remark = remark };
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    StampUser(hcf.client);
                    using (var body = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"))
                    {
                        var response = await hcf.client.PostAsync(CloseAssetApi, body).ConfigureAwait(false);
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest) { return Content(json, "application/json"); }
                        return Json(new { IsSuccess = false, Message = "Asset acknowledgement unavailable. Check the maintenance API integration." });
                    }
                }
            }
            catch (Exception ex) { RosLog("ERROR", "CloseAsset " + assetId + " " + ex.Message); return Json(new { IsSuccess = false, Message = "The save could not be confirmed. Retry with the same remark; the request is protected against duplicates." }); }
        }

        // A date the API can parse, and never the server's idea of "today" silently:
        // an empty value is resolved here and the caller always sees which date it got.
        private static string NormalizeDate(string rosterDate)
        {
            DateTime d;
            if (!String.IsNullOrEmpty(rosterDate) && DateTime.TryParse(rosterDate, out d))
            {
                return d.ToString("yyyy-MM-dd");
            }
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        // 401/403 is the one an operator can act on, so it is named rather than lumped in
        // with "something went wrong".
        private static string DescribeHttp(string what, HttpStatusCode code)
        {
            if (code == HttpStatusCode.Unauthorized || code == HttpStatusCode.Forbidden)
            {
                return what + " refused (session or token expired) -- sign in again.";
            }
            if (code == HttpStatusCode.NotFound)
            {
                return what + " endpoint not found -- check the API deployment.";
            }
            return what + " service returned HTTP " + (int)code + ".";
        }

        // who is on the other end -- so a log line can be tied to an operator, not just a time
        private static string RosWho()
        {
            try
            {
                return ClsHttpContent.LoginUser != null ? (" user=" + ClsHttpContent.LoginUser.Id) : "";
            }
            catch
            {
                return "";
            }
        }

        // pull the headline counts out of a roll-up / worksheet reply so the log says what the
        // page actually received, not just that it received something. Best-effort and silent:
        // an unexpected shape returns "" rather than logging noise about its own parsing.
        private static string RosCounts(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body))
                {
                    return "";
                }

                string bt = body.TrimStart();
                if (!bt.StartsWith("{"))
                {
                    return "";
                }

                Newtonsoft.Json.Linq.JObject jo = Newtonsoft.Json.Linq.JObject.Parse(body);
                string outText = "";

                Newtonsoft.Json.Linq.JArray stations = jo["Stations"] as Newtonsoft.Json.Linq.JArray;
                if (stations != null)
                {
                    outText += " stations=" + stations.Count;
                }

                Newtonsoft.Json.Linq.JArray items = jo["Items"] as Newtonsoft.Json.Linq.JArray;
                if (items != null)
                {
                    outText += " items=" + items.Count;

                    // Did CompositeReason survive GenerateDaily and come back? If this is 0 while
                    // the rescore raised items, the API is dropping fields it does not know and the
                    // WHY can never reach the UI -- worth knowing without guessing.
                    int withWhy = 0;
                    foreach (Newtonsoft.Json.Linq.JToken t in items)
                    {
                        Newtonsoft.Json.Linq.JObject io2 = t as Newtonsoft.Json.Linq.JObject;
                        if (io2 != null && io2["CompositeReason"] != null
                            && !string.IsNullOrEmpty(io2["CompositeReason"].ToString()))
                        {
                            withWhy++;
                        }
                    }

                    outText += " withCompositeReason=" + withWhy;
                }

                if (jo["UrgentCount"] != null)
                {
                    outText += " urgent=" + jo["UrgentCount"].ToString();
                }

                if (jo["SoonCount"] != null)
                {
                    outText += " soon=" + jo["SoonCount"].ToString();
                }

                if (jo["IsSuccess"] != null)
                {
                    outText += " ok=" + jo["IsSuccess"].ToString();
                }

                return outText;
            }
            catch
            {
                return "";
            }
        }

        private static string Head(string s)
        {
            if (String.IsNullOrEmpty(s)) { return ""; }
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste this method inside the class (e.g. right after CloseItem).
        // No new using directives needed: HttpWebRequest/WebRequest/
        // ServicePointManager/SecurityProtocolType are in System.Net (already
        // imported); ConfigurationManager and JObject are fully-qualified.
        //
        // It powers the Live tab of the drill-down. It is the SAME source the
        // AiChat verdict popup uses -- AlertAnalysisController.LiveValues -- so
        // the readings match. LiveValue lives on the EdgeX DataAPI, NOT the FRS
        // API, so it does NOT use the FRS HttpClientFactory; it calls
        // {DataApiBaseUrl}api/LiveValue/{assetId} directly with HttpWebRequest
        // (no assembly-binding-redirect dependency, unlike System.Net.Http).
        //
        // Web.config <appSettings> (add if not present):
        //   <add key="DataApiBaseUrl" value="https://YOUR-EDGEX-DATAAPI-HOST:PORT/" />
        //   (ProxyBaseUrl is used as a fallback if DataApiBaseUrl is absent.)
        //
        // The DataAPI LiveValue row carries Value / AssetAttributeId /
        // AssetAttributeName / TagUnit / TimestampDevice and does NOT carry the
        // safe range. RANGES are merged SERVER-SIDE here from the FRS attribute
        // bands -- GET {RangeApiBaseUrl}FRSAttributeRange/SiteId/{siteId} (the
        // same per-attribute Min/Max/Average source the AI verdict evidence
        // uses). The site's bands are cached in-process for 10 minutes, rows are
        // matched by AssetId + AssetAttributeId, and each live row gains
        // minSafe / maxSafe / avg + a low/high/ok state.
        //
        // RANGE SOURCE (v2.4): NO CONFIG NEEDED. If RangeApiBaseUrl is absent,
        // the bands are fetched through the application's OWN FRS API client --
        // new HttpClientFactory(token: ClsHttpContent.LoginUser.Token) with the
        // relative path "FRSAttributeRange/SiteId/{id}" -- the same authenticated
        // client every other roster call uses, so it works out of the box.
        // RangeApiBaseUrl (+ optional RangeApiAuth "user:pass" Basic) remains as
        // an override for a non-default host. If both paths fail, the merge is
        // skipped silently and readings still render.
        //
        // Version: bump ComponentVersion and add a history line, e.g.
        //   2.4.0.0 - Live range merge now rides the app's own FRS API client
        //             (no config needed); RangeApiBaseUrl kept as an override.
        // =====================================================================
        // ---- FRS attribute-band cache (site -> assetId|attrId -> band), 10-minute TTL ----
        private sealed class RosterRangeBand { public double? Min; public double? Max; public double? Avg; }
        private static readonly object _rosterRangeLock = new object();
        private static readonly Dictionary<int, DateTime> _rosterRangeAt = new Dictionary<int, DateTime>();
        private static readonly Dictionary<int, Dictionary<string, RosterRangeBand>> _rosterRangeCache =
            new Dictionary<int, Dictionary<string, RosterRangeBand>>();

        private static Dictionary<string, RosterRangeBand> RosterSiteRanges(int siteId)
        {
            if (siteId <= 0) { return null; }
            lock (_rosterRangeLock)
            {
                DateTime at;
                if (_rosterRangeAt.TryGetValue(siteId, out at) && (DateTime.UtcNow - at).TotalMinutes < 10)
                { return _rosterRangeCache.ContainsKey(siteId) ? _rosterRangeCache[siteId] : null; }
            }
            Dictionary<string, RosterRangeBand> map = null;
            try
            {
                string body = null;
                string rBase = (System.Configuration.ConfigurationManager.AppSettings["RangeApiBaseUrl"] ?? "").Trim();
                if (rBase.Length > 0)
                {
                    // override: absolute host from config (external range API)
                    if (!rBase.EndsWith("/")) { rBase += "/"; }
                    string rUrl = rBase + "FRSAttributeRange/SiteId/" + siteId.ToString();
                    var rReq = (HttpWebRequest)WebRequest.Create(rUrl);
                    rReq.Method = "GET"; rReq.Timeout = 8000; rReq.Accept = "application/json";
                    string auth = (System.Configuration.ConfigurationManager.AppSettings["RangeApiAuth"] ?? "").Trim();
                    if (auth.Length > 0)
                    { rReq.Headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(auth)); }
                    using (var rResp = (HttpWebResponse)rReq.GetResponse())
                    using (var rSr = new System.IO.StreamReader(rResp.GetResponseStream()))
                    { body = rSr.ReadToEnd(); }
                }
                else
                {
                    // default: the application's OWN authenticated FRS API client,
                    // exactly like every other roster call (relative path, no api/ prefix).
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var resp = System.Threading.Tasks.Task.Run(
                            () => hcf.client.GetAsync("FRSAttributeRange/SiteId/" + siteId.ToString())
                        ).GetAwaiter().GetResult();
                        if (resp.StatusCode == HttpStatusCode.OK)
                        {
                            body = System.Threading.Tasks.Task.Run(
                                () => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                        }
                        else
                        { RosLog("WARN", "RosterSiteRanges site=" + siteId + " FRS client " + (int)resp.StatusCode); }
                    }
                }
                if (!string.IsNullOrEmpty(body))
                {
                    Newtonsoft.Json.Linq.JToken doc = Newtonsoft.Json.Linq.JToken.Parse(body);
                    Newtonsoft.Json.Linq.JArray rows = doc as Newtonsoft.Json.Linq.JArray;
                    if (rows == null && doc is Newtonsoft.Json.Linq.JObject)
                    { rows = ((Newtonsoft.Json.Linq.JObject)doc)["AssetAttributes"] as Newtonsoft.Json.Linq.JArray; }
                    if (rows != null)
                    {
                        map = new Dictionary<string, RosterRangeBand>();
                        foreach (Newtonsoft.Json.Linq.JToken t in rows)
                        {
                            var r = t as Newtonsoft.Json.Linq.JObject;
                            if (r == null) { continue; }
                            string aId = r["AssetId"] != null ? r["AssetId"].ToString() : "";
                            string attr = r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString()
                                        : (r["Id"] != null ? r["Id"].ToString() : "");
                            if (aId.Length == 0 || attr.Length == 0) { continue; }
                            var band = new RosterRangeBand();
                            double dv;
                            if (r["MinSafeValue"] != null && double.TryParse(r["MinSafeValue"].ToString(),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv)) { band.Min = dv; }
                            if (r["MaxSafeValue"] != null && double.TryParse(r["MaxSafeValue"].ToString(),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv)) { band.Max = dv; }
                            if (r["AverageValue"] != null && double.TryParse(r["AverageValue"].ToString(),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dv)) { band.Avg = dv; }
                            if (band.Min == null && band.Max == null && band.Avg == null) { continue; }
                            map[aId + "|" + attr] = band;
                        }
                        RosLog("INFO", "RosterSiteRanges site=" + siteId + " bands=" + map.Count);
                    }
                }
            }
            catch (Exception ex)
            { RosLog("WARN", "RosterSiteRanges site=" + siteId + " " + ex.GetType().Name + " " + ex.Message); map = null; }
            lock (_rosterRangeLock)
            {
                _rosterRangeAt[siteId] = DateTime.UtcNow;
                if (map != null) { _rosterRangeCache[siteId] = map; }
                return _rosterRangeCache.ContainsKey(siteId) ? _rosterRangeCache[siteId] : null;
            }
        }

        public JsonResult Live(int assetId, int siteId = 0)
        {
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            var outRows = new List<object>();
            try
            {
                string apiBase = (System.Configuration.ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0) { apiBase = (System.Configuration.ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0)
                {
                    RosLog("ERROR", "Live: no DataApiBaseUrl/ProxyBaseUrl configured");
                    return Json(new { retrievedAt = stamp, values = outRows, error = "not configured" }, JsonRequestBehavior.AllowGet);
                }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }
                string url = apiBase + "api/LiveValue/" + assetId.ToString();

                string jsonString = "";
                HttpStatusCode code = HttpStatusCode.OK;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 8000;
                req.Accept = "application/json";
                // DataAPI is https with an internal certificate on some hosts
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    code = resp.StatusCode;
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    { jsonString = sr.ReadToEnd(); }
                }
                if (code == HttpStatusCode.OK && !string.IsNullOrEmpty(jsonString))
                {
                    var arr = JsonConvert.DeserializeObject<List<Newtonsoft.Json.Linq.JObject>>(jsonString);
                    if (arr != null)
                    {
                        Dictionary<string, RosterRangeBand> ranges = RosterSiteRanges(siteId);
                        for (int i = 0; i < arr.Count && outRows.Count < 16; i++)
                        {
                            var r = arr[i];
                            if (r == null) { continue; }
                            double val;
                            string raw = r["Value"] != null ? r["Value"].ToString() : "";
                            if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                            double mn = 0, mx = 0; bool hasMn = false, hasMx = false; double? avg = null;
                            if (r["MinValue"] != null && double.TryParse(r["MinValue"].ToString(),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mn)) { hasMn = true; }
                            if (r["MaxValue"] != null && double.TryParse(r["MaxValue"].ToString(),
                                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mx)) { hasMx = true; }
                            if (!hasMn && !hasMx && ranges != null)
                            {
                                string attKey = assetId.ToString() + "|" + (r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString() : "");
                                RosterRangeBand band;
                                if (ranges.TryGetValue(attKey, out band))
                                {
                                    if (band.Min.HasValue) { mn = band.Min.Value; hasMn = true; }
                                    if (band.Max.HasValue) { mx = band.Max.Value; hasMx = true; }
                                    avg = band.Avg;
                                }
                            }
                            string state = "";
                            if (hasMn || hasMx) { bool below = hasMn && val < mn, above = hasMx && val > mx; state = below ? "low" : (above ? "high" : "ok"); }
                            string at = "";
                            if (r["TimestampDevice"] != null)
                            {
                                DateTime td;
                                if (DateTime.TryParse(r["TimestampDevice"].ToString(),
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out td))
                                { at = td.ToString("dd/MM HH:mm"); }
                            }
                            outRows.Add(new
                            {
                                attrId = r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString() : "",
                                attr = r["AssetAttributeName"] != null ? r["AssetAttributeName"].ToString().Trim() : "",
                                unit = r["TagUnit"] != null ? r["TagUnit"].ToString().Trim() : "",
                                value = Math.Round(val, 3),
                                minSafe = hasMn ? (object)mn : null,
                                maxSafe = hasMx ? (object)mx : null,
                                avg = avg.HasValue ? (object)Math.Round(avg.Value, 3) : null,
                                state = state,
                                at = at
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Live asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message);
                return Json(new { retrievedAt = stamp, values = new List<object>(), error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }
            RosLog("INFO", "Live asset=" + assetId + " site=" + siteId + " RESULT rows=" + outRows.Count);
            return Json(new { retrievedAt = stamp, values = outRows }, JsonRequestBehavior.AllowGet);
        }


        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste ALL of this inside the class (e.g. right after the Live action).
        // No new using directives needed (System.Net is already imported;
        // ConfigurationManager is fully-qualified).
        //
        // Powers the Point "Prediction" tab of the drill-down -- the SAME
        // pm-operation-v4 model the AiChat "PM Analysis" tab fetches, cloned.
        //
        //   PmPredict -> GET {PmPredictBaseUrl}/{siteId}/{assetId}
        //                (siteId segment omitted when 0 -- the model auto-detects)
        //                Plain fetch: states + ai_summary, ~2.6 KB, NO waveform
        //                arrays. The view renders machine A/B x Normal/Reverse
        //                state cards, rising-trend rows, findings, recommendation.
        //
        //   PmTrend   -> GET {PmPredictBaseUrl}/{siteId}/{assetId}/history
        //                     ?start=..&end=..   (plain: NO medium, NO details)
        //                ~26 KB: sample_signatures + transition_summary. The view
        //                plots classification transitions per machine end as a
        //                15-day timeline + the latest episodes.
        //
        // WHY A SERVER PROXY: the model endpoint is plain http (energy7.in:8005).
        // The roster page is served over https, so a direct browser fetch is
        // blocked as mixed content. Proxying server-side also keeps the model
        // host out of client script -- exactly the shape of the Live action.
        //
        // Web.config <appSettings> (add):
        //   <add key="PmPredictBaseUrl" value="http://energy7.in:8005/api/asset/ai-prediction/pm-operation-v4" />
        //
        // Version: one bump covers Live + these two, e.g.
        //   2.2.0.0 - Live proxy (EdgeX api/LiveValue) + PmPredict/PmTrend
        //             proxies (pm-operation-v4) for the drill-down tabs.
        // =====================================================================
        public ActionResult PmPredict(int assetId, int siteId = 0)
        {
            try
            {
                string url = PmUrl(assetId, siteId, null);
                if (url == null)
                {
                    RosLog("WARN", "PmPredict asset=" + assetId + " PmPredictBaseUrl not configured");
                    return Json(new { error = "not configured" }, JsonRequestBehavior.AllowGet);
                }

                RosLog("INFO", "PmPredict asset=" + assetId + " site=" + siteId);
                string pmBody = PmFetch(url, 15000);
                RosLog("INFO", "PmPredict asset=" + assetId + " RESULT bodyLen=" + (pmBody == null ? 0 : pmBody.Length));
                return Content(pmBody, "application/json");
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "PmPredict asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message);
                return Json(new { error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult PmTrend(int assetId, int siteId = 0, int days = 15)
        {
            try
            {
                if (days < 1 || days > 60) { days = 15; }
                string qs = "/history?start=" + DateTime.Now.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss")
                          + "&end=" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                string url = PmUrl(assetId, siteId, qs);
                if (url == null)
                {
                    RosLog("WARN", "PmTrend asset=" + assetId + " PmPredictBaseUrl not configured");
                    return Json(new { error = "not configured" }, JsonRequestBehavior.AllowGet);
                }

                RosLog("INFO", "PmTrend asset=" + assetId + " site=" + siteId + " days=" + days);
                string trBody = PmFetch(url, 15000);
                RosLog("INFO", "PmTrend asset=" + assetId + " RESULT bodyLen=" + (trBody == null ? 0 : trBody.Length));
                return Content(trBody, "application/json");
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "PmTrend asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message);
                return Json(new { error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }
        }

        // GET the model endpoint and hand the body back verbatim.
        // Throws on transport failure; the callers turn that into { error }.
        private static string PmFetch(string url, int timeoutMs)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = timeoutMs;
            req.Accept = "application/json";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
            { return sr.ReadToEnd(); }
        }

        private static string PmUrl(int assetId, int siteId, string suffix)
        {
            string apiBase = (System.Configuration.ConfigurationManager.AppSettings["PmPredictBaseUrl"] ?? "").Trim();
            if (apiBase.Length == 0) { return null; }
            while (apiBase.EndsWith("/")) { apiBase = apiBase.Substring(0, apiBase.Length - 1); }
            return apiBase + "/" + (siteId > 0 ? (siteId.ToString() + "/") : "") + assetId.ToString() + (suffix ?? "");
        }


        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste inside the class (e.g. after the Live method).
        //
        // Diagnose = the roster's AI verdict engine, CLONED from the stable
        // AiChat synthesis path: no tool loop, no streaming -- the view posts
        // the full roster context for one asset, the server wraps it in a
        // compact analyst prompt, calls the configured provider ONCE (with one
        // transient retry), and returns a strict JSON verdict the popup already
        // renders: { verdict, confidence, reason, action }.  A follow-up
        // question (question + history[] in the body) switches to chat mode and
        // returns { answer } as plain text.
        //
        // CONFIG: reuses the SAME appSettings the AiChat engine already has in
        // this application's Web.config -- NOTHING NEW TO ADD if AiChat runs:
        //   AiProvider            anthropic | openai | deepseek   (default anthropic)
        //   AnthropicApiKey, AnthropicModel        (default claude-sonnet-4-6)
        //   OpenAiApiKey, OpenAiBaseUrl (default https://api.openai.com/v1), OpenAiModel (default gpt-5.4)
        //   DeepSeekApiKey, DeepSeekBaseUrl (default https://api.deepseek.com/v1), DeepSeekModel (default deepseek-v4-pro)
        // Missing key -> { error: "not configured" } and the popup shows its
        // clean disabled state.  No provider or model name ever reaches the
        // client; full detail goes to RosLog only.
        //
        // Uses HttpWebRequest (no HttpClientFactory / binding-redirect needs),
        // fully-qualified ConfigurationManager + Newtonsoft -- no new usings.
        //
        // Version: fold into the pending bump, e.g.
        //   2.3.0.0 - ... + Diagnose AI verdict engine for the roster popup
        //             (cloned from the stable AiChat synthesis path).
        // =====================================================================
        [HttpPost]
        public JsonResult Diagnose()
        {
            try
            {
                string body = "";
                Request.InputStream.Position = 0;
                using (var sr = new System.IO.StreamReader(Request.InputStream))
                { body = sr.ReadToEnd(); }
                Newtonsoft.Json.Linq.JObject req;
                string bt = (body ?? "").TrimStart();
                if (bt.StartsWith("{")) { req = Newtonsoft.Json.Linq.JObject.Parse(body); }
                else
                {
                    // tolerate form-urlencoded posts from an older cached view
                    req = new Newtonsoft.Json.Linq.JObject();
                    if (bt.Length > 0)
                    {
                        var nv = System.Web.HttpUtility.ParseQueryString(body);
                        foreach (string k in nv.AllKeys)
                        { if (!string.IsNullOrEmpty(k) && nv[k] != "[object Object]") { req[k] = nv[k]; } }
                    }
                }

                string question = req["question"] != null ? req["question"].ToString() : "";
                if (question.Length > 500) { question = question.Substring(0, 500); }
                string ctx = req["context"] != null
                    ? req["context"].ToString(Newtonsoft.Json.Formatting.None) : "{}";
                if (ctx.Length > 7000) { ctx = ctx.Substring(0, 7000); }
                var history = req["history"] as Newtonsoft.Json.Linq.JArray;
                bool chatMode = question.Length > 0;

                // ---- provider config (same keys as the stable engine) ----
                string provider = (System.Configuration.ConfigurationManager.AppSettings["AiProvider"] ?? "anthropic").Trim().ToLowerInvariant();
                string apiKey, model, url; bool anthropicShape = false;
                if (provider == "openai")
                {
                    apiKey = (System.Configuration.ConfigurationManager.AppSettings["OpenAiApiKey"] ?? "").Trim();
                    model = (System.Configuration.ConfigurationManager.AppSettings["OpenAiModel"] ?? "gpt-5.4").Trim();
                    string ob = (System.Configuration.ConfigurationManager.AppSettings["OpenAiBaseUrl"] ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
                    url = ob + "/chat/completions";
                }
                else if (provider == "deepseek")
                {
                    apiKey = (System.Configuration.ConfigurationManager.AppSettings["DeepSeekApiKey"] ?? "").Trim();
                    model = (System.Configuration.ConfigurationManager.AppSettings["DeepSeekModel"] ?? "deepseek-v4-pro").Trim();
                    string db = (System.Configuration.ConfigurationManager.AppSettings["DeepSeekBaseUrl"] ?? "https://api.deepseek.com/v1").Trim().TrimEnd('/');
                    url = db + "/chat/completions";
                }
                else
                {
                    anthropicShape = true;
                    apiKey = (System.Configuration.ConfigurationManager.AppSettings["AnthropicApiKey"] ?? "").Trim();
                    model = (System.Configuration.ConfigurationManager.AppSettings["AnthropicModel"] ?? "claude-sonnet-4-6").Trim();
                    url = "https://api.anthropic.com/v1/messages";
                }
                if (apiKey.Length == 0)
                {
                    RosLog("WARN", "Diagnose: provider key missing");
                    return Json(new { error = "not configured" });
                }

                // ---- prompts (grounded, advisory, strict output) ----
                string sysVerdict =
                    "You are a senior Indian Railways signal and telecom maintenance analyst embedded in a station " +
                    "maintenance roster. You are given the roster context for ONE asset as JSON: its 15-day alert " +
                    "evidence, dominant cause, trend, and any live readings with safe bands. Judge whether the " +
                    "engineer should expect a real defect on site. Ground every statement ONLY in the provided " +
                    "context; NEVER invent readings, times or causes; if the evidence is thin, say INCONCLUSIVE. " +
                    "Respond with ONLY a JSON object, no markdown, no prose around it: " +
                    "{\"verdict\":\"CONFIRMED_DEFECT\"|\"NO_DEFECT\"|\"INCONCLUSIVE\"," +
                    "\"confidence\":0-100," +
                    "\"reason\":\"2-3 short sentences for the engineer, grounded in the context\"," +
                    "\"action\":\"one concrete first step on site\"}";
                string sysChat =
                    "You are a senior Indian Railways signal and telecom maintenance analyst embedded in a station " +
                    "maintenance roster, answering an engineer's follow-up about one asset or the whole station (context JSON provided). " +
                    "Answer in 2-5 short plain sentences, grounded ONLY in the provided context; never invent " +
                    "readings; be concrete about what to check on site. This is advisory - the engineer verifies on site.";

                var messages = new Newtonsoft.Json.Linq.JArray();
                if (chatMode && history != null)
                {
                    for (int i = 0; i < history.Count && i < 4; i++)
                    {
                        var h = history[i] as Newtonsoft.Json.Linq.JObject;
                        if (h == null) { continue; }
                        string hq = h["q"] != null ? h["q"].ToString() : "";
                        string ha = h["a"] != null ? h["a"].ToString() : "";
                        if (hq.Length > 300) { hq = hq.Substring(0, 300); }
                        if (ha.Length > 600) { ha = ha.Substring(0, 600); }
                        if (hq.Length > 0) { messages.Add(new Newtonsoft.Json.Linq.JObject { ["role"] = "user", ["content"] = hq }); }
                        if (ha.Length > 0) { messages.Add(new Newtonsoft.Json.Linq.JObject { ["role"] = "assistant", ["content"] = ha }); }
                    }
                }
                string userMsg = "ROSTER CONTEXT (JSON):\n" + ctx +
                    (chatMode ? ("\n\nENGINEER'S QUESTION: " + question) : "\n\nProduce the verdict JSON now.");
                messages.Add(new Newtonsoft.Json.Linq.JObject { ["role"] = "user", ["content"] = userMsg });

                string sys = chatMode ? sysChat : sysVerdict;
                var payload = new Newtonsoft.Json.Linq.JObject();
                payload["model"] = model;
                payload["max_tokens"] = chatMode ? 700 : 800;
                if (anthropicShape)
                {
                    payload["system"] = sys;
                    payload["messages"] = messages;
                }
                else
                {
                    var full = new Newtonsoft.Json.Linq.JArray();
                    full.Add(new Newtonsoft.Json.Linq.JObject { ["role"] = "system", ["content"] = sys });
                    foreach (var m in messages) { full.Add(m); }
                    payload["messages"] = full;
                }
                string reqBody = payload.ToString(Newtonsoft.Json.Formatting.None);

                // ---- one call + one transient retry (4xx never retried) ----
                string text = null; Exception last = null;
                for (int attempt = 0; attempt < 2 && text == null; attempt++)
                {
                    try { text = RosterModelCall(url, apiKey, anthropicShape, reqBody); }
                    catch (WebException wex)
                    {
                        last = wex;
                        var hr = wex.Response as HttpWebResponse;
                        if (hr != null && (int)hr.StatusCode >= 400 && (int)hr.StatusCode < 500) { break; }
                        if (attempt == 0) { System.Threading.Thread.Sleep(1500); }
                    }
                    catch (Exception ex) { last = ex; if (attempt == 0) { System.Threading.Thread.Sleep(1500); } }
                }
                if (text == null)
                {
                    RosLog("ERROR", "Diagnose model call failed: " + (last != null ? last.GetType().Name + " " + last.Message : "no response"));
                    return Json(new { error = "unavailable" });
                }

                if (chatMode)
                {
                    if (text.Length > 1600) { text = text.Substring(0, 1600); }
                    RosLog("INFO", "Diagnose RESULT follow-up answer chars=" + (text == null ? 0 : text.Length) + RosWho());
                    return Json(new { answer = text });
                }

                // ---- strict-JSON verdict extraction with graceful fallback ----
                string verdict = "INCONCLUSIVE", reason = "", action = ""; int confidence = 0;
                try
                {
                    string t = text.Trim();
                    int a = t.IndexOf('{'); int b = t.LastIndexOf('}');
                    if (a >= 0 && b > a) { t = t.Substring(a, b - a + 1); }
                    var v = Newtonsoft.Json.Linq.JObject.Parse(t);
                    if (v["verdict"] != null) { verdict = v["verdict"].ToString().Trim().ToUpperInvariant(); }
                    if (v["reason"] != null) { reason = v["reason"].ToString(); }
                    if (v["action"] != null) { action = v["action"].ToString(); }
                    double cf;
                    if (v["confidence"] != null && double.TryParse(v["confidence"].ToString(),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out cf))
                    { confidence = (int)Math.Max(0, Math.Min(100, cf)); }
                }
                catch
                {
                    reason = text.Length > 1200 ? text.Substring(0, 1200) : text;
                }
                if (reason.Length > 1200) { reason = reason.Substring(0, 1200); }
                if (action.Length > 400) { action = action.Substring(0, 400); }
                RosLog("INFO", "Diagnose RESULT verdict=" + (string.IsNullOrEmpty(verdict) ? "-" : verdict)
                    + " confidence=" + confidence + RosWho());
                return Json(new { verdict = verdict, confidence = confidence, reason = reason, action = action });
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Diagnose " + ex.GetType().Name + " " + ex.Message);
                return Json(new { error = "unavailable" });
            }
        }

        // Raw provider transport, cloned from the stable clients: Anthropic
        // /v1/messages (x-api-key + anthropic-version 2023-06-01, content
        // blocks) or an OpenAI-compatible /chat/completions (Bearer,
        // choices[0].message.content).
        private static string RosterModelCall(string url, string apiKey, bool anthropicShape, string reqBody)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            if (anthropicShape)
            {
                req.Headers["x-api-key"] = apiKey;
                req.Headers["anthropic-version"] = "2023-06-01";
            }
            else
            {
                req.Headers["Authorization"] = "Bearer " + apiKey;
            }
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(reqBody);
            req.ContentLength = buf.Length;
            using (var rs = req.GetRequestStream()) { rs.Write(buf, 0, buf.Length); }
            string respBody;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
            { respBody = sr.ReadToEnd(); }
            var root = Newtonsoft.Json.Linq.JObject.Parse(respBody);
            if (anthropicShape)
            {
                var content = root["content"] as Newtonsoft.Json.Linq.JArray;
                var sb = new System.Text.StringBuilder();
                if (content != null)
                {
                    foreach (var blk in content)
                    {
                        var o = blk as Newtonsoft.Json.Linq.JObject;
                        if (o != null && (o["type"] == null || o["type"].ToString() == "text") && o["text"] != null)
                        { sb.Append(o["text"].ToString()); }
                    }
                }
                return sb.ToString();
            }
            var choices = root["choices"] as Newtonsoft.Json.Linq.JArray;
            if (choices != null && choices.Count > 0)
            {
                var msg = choices[0]["message"] as Newtonsoft.Json.Linq.JObject;
                if (msg != null && msg["content"] != null) { return msg["content"].ToString(); }
            }
            return "";
        }


        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste inside the class (e.g. after the Live method).  REPLACES the
        // previous Roster_Day_Action.cs paste entirely (DayAlerts + helpers).
        //
        // TWO actions, both built on the compile-proven FRSAlertLister shape
        // from AlertAnalysisController.GetLiveAlerts and the typed
        // Domain.FRSAlert fields used by the alert views themselves
        // (Id / SetTimeStamp / ResetTimeStamp / CauseCode / AssetId / SiteId /
        //  IsTest / AcknowledgemenStatusId + Utility.AcknowledgemenStatus enum
        //  True / False / PT / Maintenance / Acknowledge).
        // ACKNOWLEDGEMENT TRUTH: an alert is acknowledged when
        // AcknowledgemenTimeStamp != null (the status id only classifies it);
        // unacknowledged = AcknowledgemenTimeStamp null.
        //
        // DATA BINDING (matches the two screens): the UNACKNOWLEDGED set comes
        // from the AlertLive query (GetAcknowledgementAllAlert) and the
        // ACKNOWLEDGED set from the Alert History query (GetListerWithPagination).
        // Both are fetched for the window and merged (dedupe by Id, history row
        // wins - it carries the acknowledgement fields).
        //
        //  DayAlerts  - POST { assetId, siteId, divisionId, date:"yyyy-MM-dd" }
        //               -> { count, events:[{ cause, set, reset, durMin,
        //                    acked, ackType "T"|"F"|"PT"|"M"|"A"|"" }] }
        //               One day's alerts for one asset - the Alerts-tab bar
        //               drill-in. Test alerts skipped.
        //
        //  AckSummary - POST { siteId, divisionId, date:"yyyy-MM-dd" }
        //               -> { total, acked, ackTrue, ackFalse, ackPT }
        //               15-day window ending on the roster date for the whole
        //               site - feeds the "Before you sign off"
        //               acknowledgement scorecard. Test alerts skipped.
        //
        // Version: fold into the pending bump, e.g.
        //   2.4.0.0 - ... + DayAlerts drill-in fetch + AckSummary scorecard.
        // =====================================================================
        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste inside the class (e.g. after the Live method).  REPLACES the
        // previous Roster_Day_Action.cs paste entirely (DayAlerts + helpers).
        //
        // TWO actions, both built on the compile-proven FRSAlertLister shape
        // from AlertAnalysisController.GetLiveAlerts and the typed
        // Domain.FRSAlert fields used by the alert views themselves
        // (Id / SetTimeStamp / ResetTimeStamp / CauseCode / AssetId / SiteId /
        //  IsTest / AcknowledgemenStatusId + Utility.AcknowledgemenStatus enum
        //  True / False / PT / Maintenance / Acknowledge).
        // ACKNOWLEDGEMENT TRUTH: an alert is acknowledged when
        // AcknowledgemenTimeStamp != null (the status id only classifies it);
        // unacknowledged = AcknowledgemenTimeStamp null.
        //
        // DATA BINDING (matches the two screens): the UNACKNOWLEDGED set comes
        // from the AlertLive query (GetAcknowledgementAllAlert) and the
        // ACKNOWLEDGED set from the Alert History query (GetListerWithPagination).
        // Both are fetched for the window and merged (dedupe by Id, history row
        // wins - it carries the acknowledgement fields).
        //
        //  DayAlerts  - POST { assetId, siteId, divisionId, date:"yyyy-MM-dd" }
        //               -> { count, events:[{ cause, set, reset, durMin,
        //                    acked, ackType "T"|"F"|"PT"|"M"|"A"|"" }] }
        //               One day's alerts for one asset - the Alerts-tab bar
        //               drill-in. Test alerts skipped.
        //
        //  AckSummary - POST { siteId, divisionId, date:"yyyy-MM-dd" }
        //               -> { total, acked, ackTrue, ackFalse, ackPT }
        //               15-day window ending on the roster date for the whole
        //               site - feeds the "Before you sign off"
        //               acknowledgement scorecard. Test alerts skipped.
        //
        // Version: fold into the pending bump, e.g.
        //   2.4.0.0 - ... + DayAlerts drill-in fetch + AckSummary scorecard.
        // =====================================================================

        [HttpPost]
        public JsonResult DayAlerts()
        {
            try
            {
                Newtonsoft.Json.Linq.JObject req = RosterReadBody();
                int assetId = req["assetId"] != null ? (int)req["assetId"] : 0;
                int siteId = req["siteId"] != null ? (int)req["siteId"] : 0;
                int page = req["page"] != null ? (int)req["page"] : 1;
                int pageSize = req["pageSize"] != null ? (int)req["pageSize"] : 20;
                if (page < 1 || pageSize < 1 || pageSize > 100) { return Json(new { error = "invalid paging: page >= 1, pageSize 1..100" }); }
                int divisionId = req["divisionId"] != null ? (int)req["divisionId"] : 0;
                DateTime day;
                if (assetId <= 0 || !RosterParseDay(req, out day))
                {
                    RosLog("WARN", "DayAlerts bad request asset=" + assetId + " division=" + divisionId);
                    return Json(new { error = "bad request" });
                }

                var svc = DependencyResolver.Current.GetService(typeof(IFRSAlertService)) as IFRSAlertService;
                if (svc == null) { RosLog("WARN", "DayAlerts: IFRSAlertService not resolvable"); return Json(new { error = "not configured" }); }

                var ackedIds = new HashSet<string>();
                var rows = RosterWindowAlerts(svc, day, day.AddDays(1), divisionId, ackedIds);
                rows.Sort((a, b) => a == null ? (b == null ? 0 : 1) : b == null ? -1 : b.SetTimeStamp.CompareTo(a.SetTimeStamp));
                var events = new List<object>();
                int dropMaint = 0;
                {
                    foreach (var a in rows)
                    {
                        if (a == null || a.IsTest.Equals(true)) { continue; }
                        if (a.AssetId != assetId || (siteId > 0 && a.SiteId != siteId)) { continue; }

                        // Same exclusion as FetchWindowAlerts. Without it the day popup listed
                        // alerts the roster had already filtered out, so the 15-day bar said one
                        // count and the drill-in showed another.
                        if (RosterAckType(a.AcknowledgemenStatusId) == "M")
                        {
                            dropMaint++;
                            continue;
                        }

                        DateTime setT = a.SetTimeStamp;
                        if (setT < day || setT >= day.AddDays(1)) { continue; }
                        DateTime resetT; bool hasReset = RosterDtProp(a, "ResetTimeStamp", out resetT);
                        object dur = null;
                        if (hasReset && resetT > setT) { dur = (int)Math.Round((resetT - setT).TotalMinutes); }
                        DateTime ackAt; bool acked = ackedIds.Contains(a.Id.ToString()) || RosterDtProp(a, "AcknowledgemenTimeStamp", out ackAt);
                        string ackType = acked ? RosterAckType(a.AcknowledgemenStatusId) : "";
                        events.Add(new
                        {
                            cause = a.CauseCode ?? "",
                            alertId = a.Id.ToString(),
                            assetId = a.AssetId,
                            siteId = a.SiteId,
                            incidentTime = setT.ToString("yyyy-MM-dd HH:mm:ss"),
                            set = setT.ToString("yyyy-MM-dd HH:mm:ss"),
                            reset = hasReset ? resetT.ToString("yyyy-MM-dd HH:mm:ss") : "-",
                            durMin = dur,
                            acked = acked,
                            ackType = ackType
                        });

                    }
                }
                RosLog("INFO", "DayAlerts asset=" + assetId + " division=" + divisionId
                    + " day=" + day.ToString("yyyy-MM-dd") + " RESULT events=" + events.Count
                    + (dropMaint > 0 ? " droppedMaintenance=" + dropMaint : ""));
                // Keep count/events compatible with the existing CSHTML; add real total and paging metadata.
                int totalCount = events.Count;
                long skipLong = ((long)page - 1) * pageSize;
                int skip = (int)Math.Min(skipLong, totalCount);
                List<object> pageEvents = events.GetRange(skip, Math.Min(pageSize, totalCount - skip));
                return Json(new { count = pageEvents.Count, totalCount, page, pageSize, hasMore = skip + pageEvents.Count < totalCount, events = pageEvents });
            }
            catch (Exception ex)
            { RosLog("ERROR", "DayAlerts " + ex.GetType().Name + " " + ex.Message); return Json(new { error = "unavailable" }); }
        }

        [HttpPost]
        public JsonResult AckSummary()
        {
            try
            {
                Newtonsoft.Json.Linq.JObject req = RosterReadBody();
                int siteId = req["siteId"] != null ? (int)req["siteId"] : 0;
                int divisionId = req["divisionId"] != null ? (int)req["divisionId"] : 0;
                DateTime day;
                if (!RosterParseDay(req, out day)) { return Json(new { error = "bad request" }); }

                var svc = DependencyResolver.Current.GetService(typeof(IFRSAlertService)) as IFRSAlertService;
                if (svc == null) { RosLog("WARN", "AckSummary: IFRSAlertService not resolvable"); return Json(new { error = "not configured" }); }

                var ackedIds = new HashSet<string>();
                var rows = RosterWindowAlerts(svc, day.AddDays(-14), day.AddDays(1), divisionId, ackedIds);
                int total = 0, acked = 0, ackTrue = 0, ackFalse = 0, ackPT = 0;
                {
                    foreach (var a in rows)
                    {
                        if (a == null || a.IsTest.Equals(true)) { continue; }
                        if (siteId > 0 && a.SiteId != siteId) { continue; }
                        total++;
                        DateTime ackAt;
                        bool isAcked = ackedIds.Contains(a.Id.ToString()) || RosterDtProp(a, "AcknowledgemenTimeStamp", out ackAt);
                        if (!isAcked) { continue; }
                        acked++;
                        string t = RosterAckType(a.AcknowledgemenStatusId);
                        if (t == "T") { ackTrue++; }
                        else if (t == "F") { ackFalse++; }
                        else if (t == "PT") { ackPT++; }
                    }
                }
                RosLog("INFO", "AckSummary site=" + siteId + " window=" + rows.Count + " total=" + total + " acked=" + acked + " un=" + (total - acked));
                return Json(new { total = total, acked = acked, ackTrue = ackTrue, ackFalse = ackFalse, ackPT = ackPT });
            }
            catch (Exception ex)
            { RosLog("ERROR", "AckSummary " + ex.GetType().Name + " " + ex.Message); return Json(new { error = "unavailable" }); }
        }

        // UI 2.1: complete scoped alert evidence for AI, independent of the day popup page.
        // Existing role/site filters and the authenticated FRS service remain authoritative.
        [HttpGet]
        public ActionResult AiAlertEvidence(int divisionId, string rosterDate, int siteId = 0, int assetId = 0)
        {
            DateTime day;
            if (divisionId <= 0 || siteId < 0 || assetId < 0 || (assetId > 0 && siteId == 0)
                || !DateTime.TryParseExact(rosterDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out day)
                || day.Date > DateTime.UtcNow.AddMinutes(330).Date)
            { return Json(new { IsSuccess = false, Message = "Invalid AI maintenance scope." }, JsonRequestBehavior.AllowGet); }
            try
            {
                var problems = new List<string>();
                var allowed = new HashSet<string>();
                var sites = Helper.FilterCacheHelper.GetSitesByDivisionId(_siteService, divisionId);
                if (sites == null) { throw new InvalidOperationException("Site inventory unavailable"); }
                var siteTokens = JArray.FromObject(sites);
                bool siteFound = siteId == 0;
                foreach (JObject st in siteTokens.OfType<JObject>())
                {
                    int sid = RosJInt(st, "SiteId", "siteId", "Id", "id");
                    if (sid <= 0 || (siteId > 0 && sid != siteId)) { continue; }
                    siteFound = true;
                    try
                    {
                        object raw = _assetService.GetAssestBy(sid);
                        if (raw == null) { throw new InvalidOperationException("Asset inventory unavailable"); }
                        foreach (JObject asset in JArray.FromObject(raw).OfType<JObject>())
                        {
                            int aid = RosJInt(asset, "AssetId", "assetId", "Id", "id");
                            int type = RosJInt(asset, "AssetTypeId", "assetTypeId", "AssetType");
                            if (aid > 0 && (assetId == 0 || aid == assetId) && (type == 1 || type == 2 || type == 3 || type == 34))
                            { allowed.Add(sid.ToString() + ":" + aid.ToString()); }
                        }
                    }
                    catch (Exception ex) { RosLog("WARN", "AiAlertEvidence inventory site=" + sid + " " + ex.GetType().Name); problems.Add("Asset inventory unavailable for site " + sid + "."); }
                }
                if (!siteFound || (assetId > 0 && !allowed.Contains(siteId.ToString() + ":" + assetId.ToString())))
                { return Json(new { IsSuccess = false, Message = "The requested asset or site is not available in this division." }, JsonRequestBehavior.AllowGet); }
                var svc = DependencyResolver.Current.GetService(typeof(IFRSAlertService)) as IFRSAlertService;
                if (svc == null) { throw new InvalidOperationException("Alert service unavailable"); }
                var ackedIds = new HashSet<string>();
                var from = day.AddDays(-14); var until = day.AddDays(1);
                var rows = RosterWindowAlerts(svc, from, until, divisionId, ackedIds, problems);
                var events = new List<object>();
                foreach (var a in rows.OrderByDescending(r => r.SetTimeStamp))
                {
                    if (!allowed.Contains(a.SiteId.ToString() + ":" + a.AssetId.ToString()) || a.SetTimeStamp < from || a.SetTimeStamp >= until) { continue; }
                    DateTime reset, ackAt;
                    bool hasReset = RosterDtProp(a, "ResetTimeStamp", out reset);
                    bool acked = ackedIds.Contains(a.Id.ToString()) || RosterDtProp(a, "AcknowledgemenTimeStamp", out ackAt);
                    string ackType = acked ? RosterAckType(a.AcknowledgemenStatusId) : "";
                    events.Add(new {
                        alertId = a.Id.ToString(), assetId = a.AssetId, siteId = a.SiteId, cause = a.CauseCode ?? "",
                        set = a.SetTimeStamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        reset = hasReset ? reset.ToString("yyyy-MM-dd HH:mm:ss") : null,
                        durMin = hasReset && reset > a.SetTimeStamp ? (double?)Math.Round((reset - a.SetTimeStamp).TotalMinutes) : null,
                        acked, ackType, isTest = a.IsTest.Equals(true),
                        excludedFromRoster = a.IsTest.Equals(true) || RosterAckType(a.AcknowledgemenStatusId) == "M"
                    });
                }
                // Content avoids MVC's default JSON length limit. Never silently Take(N) on AI evidence.
                return Content(JsonConvert.SerializeObject(new {
                    IsSuccess = true, complete = problems.Count == 0, divisionId, siteId, assetId,
                    from = from.ToString("yyyy-MM-dd"), to = day.ToString("yyyy-MM-dd"),
                    totalCount = events.Count, events, problems
                }), "application/json");
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AiAlertEvidence " + ex.GetType().Name);
                return Json(new { IsSuccess = false, Message = "Scoped alert evidence unavailable." }, JsonRequestBehavior.AllowGet);
            }
        }

        // ---- shared helpers for the two alert-bridge actions ----
        // The window fetched twice via GetListerWithPagination, exactly like the
        // two _List actions: pass 1 IsAcknowledgement=false (AlertLive /
        // unacknowledged), pass 2 IsAcknowledgement=true (Alert Detail /
        // acknowledged). Merged with an Id dedupe as a safety.
        private static List<Domain.FRSAlert> RosterWindowAlerts(IFRSAlertService svc, DateTime from, DateTime to, int divisionId, HashSet<string> ackedIds, List<string> problems = null)
        {
            var all = new List<Domain.FRSAlert>();
            var seen = new Dictionary<string, bool>();
            for (int pass = 0; pass < 2; pass++)
            {
                var lister = new Domain.FRSAlertLister();
                lister.SearchCriteria.FromDate = from;
                lister.SearchCriteria.ToDate = to;
                if (divisionId > 0) { lister.SearchCriteria.DivisionId = divisionId; }
                lister.SearchCriteria.IsAcknowledgement = (pass == 1);
                lister.Pager.Take = -1;
                try { lister = svc.GetListerWithPagination(lister); }
                catch (Exception ex)
                { RosLog("WARN", "RosterWindowAlerts pass" + pass + " " + ex.GetType().Name + " " + ex.Message); lister = null; }
                if (lister == null || lister.mFRSAlerts == null) { if (problems != null) { problems.Add((pass == 0 ? "Unacknowledged" : "Acknowledged") + " alerts unavailable."); } continue; }
                if (problems != null && lister.Pager != null && Convert.ToInt64(lister.Pager.TotalRecord) > lister.mFRSAlerts.Count) { problems.Add((pass == 0 ? "Unacknowledged" : "Acknowledged") + " alert read was truncated by the upstream service."); }
                foreach (var a in lister.mFRSAlerts)
                {
                    if (a == null) { continue; }
                    string k = a.Id.ToString();
                    if (pass == 1 && ackedIds != null) { ackedIds.Add(k); }
                    if (seen.ContainsKey(k)) { continue; }
                    seen[k] = true; all.Add(a);
                }
            }
            return all;
        }
        // Accepts BOTH encodings: application/json ({"siteId":167,...}) and
        // application/x-www-form-urlencoded (siteId=167&divisionId=33&date=...),
        // so old cached views keep working while the current view posts JSON.
        private Newtonsoft.Json.Linq.JObject RosterReadBody()
        {
            string body = "";
            Request.InputStream.Position = 0;
            using (var sr = new System.IO.StreamReader(Request.InputStream)) { body = sr.ReadToEnd(); }
            var o = new Newtonsoft.Json.Linq.JObject();
            if (string.IsNullOrEmpty(body)) { return o; }
            string t = body.TrimStart();
            if (t.StartsWith("{")) { return Newtonsoft.Json.Linq.JObject.Parse(body); }
            var nv = System.Web.HttpUtility.ParseQueryString(body);
            foreach (string k in nv.AllKeys)
            { if (!string.IsNullOrEmpty(k)) { o[k] = nv[k]; } }
            return o;
        }
        private static bool RosterParseDay(Newtonsoft.Json.Linq.JObject req, out DateTime day)
        {
            day = DateTime.Today;
            string dateStr = req["date"] != null ? req["date"].ToString().Trim() : "";
            if (dateStr.Length == 0) { return true; }   // no date -> today's roster
            string[] fmts = { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "yyyy/MM/dd" };
            if (DateTime.TryParseExact(dateStr, fmts, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out day)) { day = day.Date; return true; }
            if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out day)) { day = day.Date; return true; }
            day = DateTime.Today; return true;          // unparseable -> today, never a hard failure
        }
        // Classification straight from the app's own enum (see _FRSRemark / alert list badges):
        // True -> "T", False -> "F", PT (partial true) -> "PT", Maintenance -> "M",
        // plain Acknowledge -> "A", anything else / 0 -> "" (not acknowledged).
        private static string RosterAckType(object statusIdObj)
        {
            int sid = 0;
            try { if (statusIdObj != null) { sid = Convert.ToInt32(statusIdObj); } } catch { sid = 0; }
            if (sid == 0) { return ""; }
            if (sid == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.True) { return "T"; }
            if (sid == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.False) { return "F"; }
            if (sid == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.PT) { return "PT"; }
            if (sid == (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace /* 616: .Maintenance (4) */) { return "M"; }
            if (sid == 6 /* 616: AcknowledgemenStatus.Acknowledge; not in the 617 enum */) { return "A"; }
            return "A";
        }
        private static bool RosterDtProp(object o, string name, out DateTime dt)
        {
            dt = DateTime.MinValue;
            try
            {
                var p = o.GetType().GetProperty(name);
                if (p == null) { return false; }
                object v = p.GetValue(o, null);
                if (v == null) { return false; }
                if (v is DateTime) { dt = (DateTime)v; return dt != DateTime.MinValue; }
                return DateTime.TryParse(v.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt) && dt != DateTime.MinValue;
            }
            catch { return false; }
        }

        private static Newtonsoft.Json.Linq.JToken RosterPropCI(Newtonsoft.Json.Linq.JObject o, string name)
        {
            foreach (var p in o.Properties())
            { if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { return p.Value; } }
            return null;
        }
        private static string RosterFirstCI(Newtonsoft.Json.Linq.JObject o, string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                var v = RosterPropCI(o, names[i]);
                if (v != null && v.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                { string s = v.ToString().Trim(); if (s.Length > 0) { return s; } }
            }
            return null;
        }
        private static bool RosterDateCI(Newtonsoft.Json.Linq.JObject o, string[] names, out DateTime dt)
        {
            dt = DateTime.MinValue;
            string s = RosterFirstCI(o, names);
            if (string.IsNullOrEmpty(s)) { return false; }
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt);
        }



        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste this method inside the class (e.g. right after RangeHistory()).
        // No new using directives needed (same set as Roster_Live_Action.cs).
        //
        // PURPOSE  (POINT MACHINES ONLY)
        //   Same-origin proxy for the point-machine operation classifier and a
        //   server-computed PROBLEM-OP COUNT per point. The browser must not call
        //   the Historian (:8005) directly, so this proxies it server-side, the
        //   same way the AiChat controller does (baseUrl + path + HttpWebRequest).
        //
        //   Upstream (Historian, NOT the FRS API -- different host):
        //     GET {HistorianApiBaseUrl}/api/asset/ai-prediction/pm-operation-v4/{siteId}/{assetId}
        //     (v2.6.0.0: NO query string. ?details=true&start&end returns the full waveform
        //      history -- 1.2 MB per asset per month -- and wants ISO dates, not ddMMyyyy_HHmmss.)
        //   Web.config:
        //     <add key="HistorianApiBaseUrl" value="http://172.31.25.102:8005" />
        //     <add key="HistorianApiAuth"    value="user:pass" />   (optional Basic; usually none)
        //   Dates (optional) are ddMMyyyy_HHmmss; if omitted the server uses the
        //   last 30 days.
        //
        // COUNTING RULE (yours):
        //   For each operation, look at its classification grade -- the flat
        //   "grade" and the per-end "a_classification.grade" / "b_classification.grade".
        //   An operation is a PROBLEM when a grade is present and is NOT "NORMAL"
        //   and NOT "INVALID" (i.e. OBSTRUCTION and every other abnormal grade).
        //   problem = count of such operations.
        //   Bands (tune):  problem >= 3 -> "URGENT" ,  >= 1 -> "SOON" ,  else "" .
        //
        // RETURNS:
        //   { siteId, assetId, ops, problem, band, grades:{ GRADE: n, ... } }
        //
        // Version: bump ComponentVersion, e.g.
        //   2.6.0.0 - PmOps proxy (pm-operation-v4 abnormal-operation count for points)
        // =====================================================================

        // v2.6.0.0 -- COUNT thresholds replaced by a RATE over a recent window.
        // The old rule (>=1 problem op -> Soon, >=3 -> Urgent, measured over 30 days) flipped
        // practically every point machine to URGENT the moment real data arrived: a verified
        // sample showed 41 problem grades out of 219 ops in one month on a single machine.
        // A rate over the recent window is the signal; a raw count over a month is not.
        private const int RosPmOpRateDays = 7;        // window the rate is measured over
        private const int RosPmOpMinOps = 10;         // below this many ops the rate is not trusted
        private const double RosPmOpUrgentRate = 25.0;  // >= this % problem ops -> Urgent
        private const double RosPmOpSoonRate = 10.0;    // >= this % problem ops -> Soon

        // rank a point-machine state string exactly as the view's rosPmWorst and AiChat's
        // aiPmWorst do, so the roster count and both UIs can never disagree
        private static int RosPmStateRank(string state)
        {
            string sv = (state ?? "").Trim().ToUpperInvariant();
            if (sv.IndexOf("URGENT") >= 0 || sv.IndexOf("INSPECT") >= 0)
            {
                return 3;
            }

            if (sv.IndexOf("MAINTAIN") >= 0 || sv == "SOON")
            {
                return 2;
            }

            if (sv.IndexOf("WATCH") >= 0)
            {
                return 1;
            }

            return 0;
        }

        // worst state across a/b and Normal/Reverse in the SMALL (no-query) pm-operation-v4 reply
        private static int RosPmWorstState(Newtonsoft.Json.Linq.JObject pm, out string worstState)
        {
            worstState = "NORMAL";
            int worst = 0;
            if (pm == null)
            {
                return 0;
            }

            string[] ends = { "a", "b" };
            string[] dirs = { "Normal", "Reverse" };

            for (int e = 0; e < ends.Length; e++)
            {
                Newtonsoft.Json.Linq.JObject end = pm[ends[e]] as Newtonsoft.Json.Linq.JObject;
                if (end == null)
                {
                    continue;
                }

                Newtonsoft.Json.Linq.JObject states = end["states"] as Newtonsoft.Json.Linq.JObject;
                if (states == null)
                {
                    continue;
                }

                for (int d = 0; d < dirs.Length; d++)
                {
                    Newtonsoft.Json.Linq.JObject st = states[dirs[d]] as Newtonsoft.Json.Linq.JObject;
                    if (st == null || st["state"] == null)
                    {
                        continue;
                    }

                    string sv = st["state"].ToString();
                    int r = RosPmStateRank(sv);
                    if (r > worst)
                    {
                        worst = r;
                        worstState = sv.Trim().ToUpperInvariant();
                    }
                }
            }

            return worst;
        }

        // The condition text is the point-machine equivalent of the track's simple_summary.
        // In the DETAILS reply it sits at rows[].a_classification.condition; the small reply is
        // not documented, so look in the obvious places and settle for worst_confirmed rather
        // than showing the operator nothing.
        private static string RosPmCondition(Newtonsoft.Json.Linq.JObject pm)
        {
            try
            {
                if (pm == null)
                {
                    return "";
                }

                string[] ends = { "a", "b" };
                string[] dirs = { "Normal", "Reverse" };
                string best = "";

                for (int e = 0; e < ends.Length; e++)
                {
                    Newtonsoft.Json.Linq.JObject end = pm[ends[e]] as Newtonsoft.Json.Linq.JObject;
                    if (end == null)
                    {
                        continue;
                    }

                    Newtonsoft.Json.Linq.JObject states = end["states"] as Newtonsoft.Json.Linq.JObject;
                    if (states == null)
                    {
                        continue;
                    }

                    for (int d = 0; d < dirs.Length; d++)
                    {
                        Newtonsoft.Json.Linq.JObject st = states[dirs[d]] as Newtonsoft.Json.Linq.JObject;
                        if (st == null)
                        {
                            continue;
                        }

                        string[] keys = { "condition", "worst_condition", "classification" };
                        for (int k = 0; k < keys.Length; k++)
                        {
                            Newtonsoft.Json.Linq.JToken node = st[keys[k]];
                            if (node == null)
                            {
                                continue;
                            }

                            string v = node.Type == Newtonsoft.Json.Linq.JTokenType.Object
                                ? (node["condition"] != null ? node["condition"].ToString() : "")
                                : node.ToString();

                            v = (v ?? "").Trim();
                            if (v.Length > 0 && !v.Equals("null", StringComparison.OrdinalIgnoreCase) && best.Length == 0)
                            {
                                best = v;
                            }
                        }

                        if (best.Length == 0 && st["worst_confirmed"] != null)
                        {
                            string wc = st["worst_confirmed"].ToString().Trim().ToUpperInvariant();
                            if (wc.Length > 0 && wc != "NORMAL" && wc != "INVALID")
                            {
                                best = wc;
                            }
                        }
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                RosLog("WARN", "RosPmCondition " + ex.GetType().Name + " " + ex.Message);
                return "";
            }
        }

        // grade string off one op record, whatever the upstream calls the field
        private static string RosOpGrade(Newtonsoft.Json.Linq.JObject op)
        {
            if (op == null)
            {
                return "";
            }

            string[] keys = { "confirmed", "grade", "classification", "state" };
            for (int k = 0; k < keys.Length; k++)
            {
                Newtonsoft.Json.Linq.JToken node = op[keys[k]];
                if (node == null)
                {
                    continue;
                }

                if (node.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                {
                    Newtonsoft.Json.Linq.JToken g = node["grade"] ?? node["confirmed"];
                    if (g != null)
                    {
                        return g.ToString();
                    }
                }
                else
                {
                    return node.ToString();
                }
            }

            return "";
        }

        // timestamp off one op record, for the recent-window rate
        private static bool RosOpTime(Newtonsoft.Json.Linq.JObject op, out DateTime when)
        {
            when = DateTime.MinValue;
            if (op == null)
            {
                return false;
            }

            string[] keys = { "time", "timestamp", "at", "start", "op_time" };
            for (int k = 0; k < keys.Length; k++)
            {
                Newtonsoft.Json.Linq.JToken node = op[keys[k]];
                if (node == null)
                {
                    continue;
                }

                if (DateTime.TryParse(node.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out when))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RosGradeIsProblem(string g)
        {
            if (string.IsNullOrEmpty(g)) { return false; }
            g = g.Trim().ToUpperInvariant();
            return g != "NORMAL" && g != "INVALID";
        }

        private static void RosCollectGrade(Newtonsoft.Json.Linq.JObject op, string key,
            System.Collections.Generic.List<string> into)
        {
            if (op == null) { return; }
            var node = op[key];
            if (node == null) { return; }
            if (node.Type == Newtonsoft.Json.Linq.JTokenType.Object)
            {
                var g = node["grade"];
                if (g != null) { into.Add(g.ToString()); }
            }
            else { into.Add(node.ToString()); }   // flat "grade":"..."
        }

        public JsonResult PmOps(int siteId, int assetId, string start = null, string end = null)
        {
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            int opsTotal = 0, problem = 0;
            int stateRank = 0;
            string worstState = "NORMAL";
            double rate = 0.0;
            string rateBand = "";
            string stateBand = "";
            string pmCondition = "";
            var grades = new System.Collections.Generic.Dictionary<string, int>();
            try
            {
                if (siteId <= 0 || assetId <= 0)
                { RosLog("WARN", "PmOps bad args site=" + siteId + " asset=" + assetId); return Json(new { siteId, assetId, ops = 0, problem = 0, band = "", grades, error = "bad args" }, JsonRequestBehavior.AllowGet); }

                string baseUrl = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "").Trim();
                if (baseUrl.Length == 0)
                { RosLog("ERROR", "PmOps: HistorianApiBaseUrl not configured"); return Json(new { siteId, assetId, ops = 0, problem = 0, band = "", grades, error = "not configured" }, JsonRequestBehavior.AllowGet); }
                if (baseUrl.EndsWith("/")) { baseUrl = baseUrl.Substring(0, baseUrl.Length - 1); }

                // v2.6.0.0 -- NO query string, exactly as AiChat calls it. The same route with
                // ?details=true&start&end returns the full waveform history (1.2 MB for one asset
                // over one month) purely so we could read two grade strings per row, and it was
                // being sent ddMMyyyy_HHmmss dates this API does not parse -- which is why it
                // silently returned an empty rows[] and every band came back "-".
                // The plain reply carries the states the model already computed, plus ops[]
                // without the waveform arrays, in a few KB.
                string url = baseUrl + "/api/asset/ai-prediction/pm-operation-v4/" + siteId + "/" + assetId;
                RosLog("INFO", "PmOps site=" + siteId + " asset=" + assetId + " (states+ops, no waveforms)");

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET"; req.Timeout = 15000; req.Accept = "application/json";
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                string auth = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiAuth"] ?? "").Trim();
                if (auth.Length > 0)
                { req.Headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(auth)); }

                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                { body = sr.ReadToEnd(); }

                RosLog("DEBUG", "PmOps asset=" + assetId + " bodyLen=" + (body == null ? 0 : body.Length)
                    + " raw=" + (string.IsNullOrEmpty(body) ? "" : (body.Length > 300 ? body.Substring(0, 300) + "..." : body)));

                if (string.IsNullOrEmpty(body))
                {
                    RosLog("WARN", "PmOps asset=" + assetId + " EMPTY upstream body -- band not computed");
                }

                if (!string.IsNullOrEmpty(body))
                {
                    Newtonsoft.Json.Linq.JObject pm = Newtonsoft.Json.Linq.JToken.Parse(body) as Newtonsoft.Json.Linq.JObject;
                    if (pm == null)
                    {
                        RosLog("WARN", "PmOps asset=" + assetId + " reply is not an object -- band not computed");
                    }
                    else
                    {
                        // The description lives in classification.condition -- verified against a
                        // real reply: populated on FAULT / ALERT / WATCH (OBSTRUCTION,
                        // SIGNATURE-CHANGE), null on NORMAL and INVALID. Look for it wherever the
                        // small payload puts it, and fall back to worst_confirmed.
                        pmCondition = RosPmCondition(pm);

                        // ---- signal 1: the state the model already decided (a/b, Normal/Reverse)
                        stateRank = RosPmWorstState(pm, out worstState);
                        stateBand = stateRank == 3 ? "URGENT" : (stateRank == 2 ? "SOON" : "");
                        if (stateRank == 0 && pm["a"] == null && pm["b"] == null)
                        {
                            RosLog("WARN", "PmOps asset=" + assetId + " reply has no a/b machines -- state band not computed");
                        }
                        else
                        {
                            RosLog("INFO", "PmOps asset=" + assetId + " worstState=" + worstState
                                + " stateBand=" + (stateBand.Length > 0 ? stateBand : "-"));
                        }

                        // ---- signal 2: problem RATE over the recent window, from ops[] on both ends
                        DateTime cutoff = DateTime.Now.AddDays(-RosPmOpRateDays);
                        int undated = 0;
                        string[] ends = { "a", "b" };

                        for (int e = 0; e < ends.Length; e++)
                        {
                            Newtonsoft.Json.Linq.JObject endObj = pm[ends[e]] as Newtonsoft.Json.Linq.JObject;
                            if (endObj == null)
                            {
                                continue;
                            }

                            Newtonsoft.Json.Linq.JArray ops = endObj["ops"] as Newtonsoft.Json.Linq.JArray;
                            if (ops == null)
                            {
                                continue;
                            }

                            foreach (Newtonsoft.Json.Linq.JToken t in ops)
                            {
                                Newtonsoft.Json.Linq.JObject op = t as Newtonsoft.Json.Linq.JObject;
                                if (op == null)
                                {
                                    continue;
                                }

                                DateTime when;
                                if (RosOpTime(op, out when))
                                {
                                    if (when < cutoff)
                                    {
                                        continue;
                                    }
                                }
                                else
                                {
                                    undated++;
                                }

                                string g = RosOpGrade(op);
                                string gu = string.IsNullOrEmpty(g) ? "" : g.Trim().ToUpperInvariant();
                                if (gu.Length > 0)
                                {
                                    grades[gu] = (grades.ContainsKey(gu) ? grades[gu] : 0) + 1;
                                }

                                // INVALID is a bad capture, not a bad machine -- it counts in neither total nor problem
                                if (gu == "INVALID")
                                {
                                    continue;
                                }

                                opsTotal++;
                                if (RosGradeIsProblem(gu))
                                {
                                    problem++;
                                }
                            }
                        }

                        if (undated > 0)
                        {
                            RosLog("WARN", "PmOps asset=" + assetId + " " + undated
                                + " op(s) had no readable timestamp -- counted regardless, rate window may be wider than "
                                + RosPmOpRateDays + "d");
                        }

                        if (opsTotal >= RosPmOpMinOps)
                        {
                            rate = (double)problem * 100.0 / (double)opsTotal;
                            rateBand = rate >= RosPmOpUrgentRate ? "URGENT" : (rate >= RosPmOpSoonRate ? "SOON" : "");
                        }
                        else if (opsTotal > 0)
                        {
                            RosLog("INFO", "PmOps asset=" + assetId + " only " + opsTotal + " op(s) in "
                                + RosPmOpRateDays + "d (min " + RosPmOpMinOps + ") -- rate not trusted, state band only");
                        }
                        else
                        {
                            RosLog("WARN", "PmOps asset=" + assetId + " no ops[] in the reply -- rate band not computed");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "PmOps site=" + siteId + " asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message
                    + RosUpstreamBody(ex));
                return Json(new { siteId, assetId, ops = opsTotal, problem, band = "", grades, error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }

            string bandOut = RosWorstBand(stateBand, rateBand);
            RosLog("INFO", "PmOps site=" + siteId + " asset=" + assetId + " RESULT worstState=" + worstState
                + " condition=" + (pmCondition.Length > 0 ? pmCondition : "-")
                + " ops" + RosPmOpRateDays + "d=" + opsTotal + " problem=" + problem
                + " rate=" + Math.Round(rate, 1) + "% thresholds=" + RosPmOpSoonRate + "/" + RosPmOpUrgentRate
                + " stateBand=" + (stateBand.Length > 0 ? stateBand : "-")
                + " rateBand=" + (rateBand.Length > 0 ? rateBand : "-")
                + " band=" + (bandOut.Length > 0 ? bandOut : "-"));
            return Json(new
            {
                retrievedAt = stamp,
                siteId,
                assetId,
                ops = opsTotal,
                problem,
                rate = Math.Round(rate, 1),
                worstState,
                condition = pmCondition,
                band = bandOut,
                grades
            }, JsonRequestBehavior.AllowGet);
        }


        // =====================================================================
        // GetRollup adoption (server, to change the LANDING counts): for each
        // POINT asset of a site during the run, call PmOps(siteId, assetId) and
        // fold band into the effective band (worst of alert / short / drift / pm-ops).
        // Cache per run so it is not re-fetched on every Load.
        // =====================================================================



        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste this method inside the class (e.g. right after Live()).
        // No new using directives needed (same set Roster_Live_Action.cs uses:
        // System.Net / ConfigurationManager / Newtonsoft.Json fully-qualified).
        //
        // PURPOSE
        //   Same-origin proxy for the FRS attribute RANGE HISTORY (the avg TREND
        //   series), plus a server-computed AVG-DRIFT summary per asset. The
        //   browser cannot call the range host (:590) directly (CORS / auth), so
        //   this rides the application's OWN authenticated FRS API client -- the
        //   same pattern Live()'s range merge already uses (RosterSiteRanges).
        //
        //   Upstream (relative, through the app FRS client -- no config needed):
        //     GET FRSAttributeRangeHistory/AssetId/{assetId}/{start}/{end}
        //   Override host (optional, same keys as the Live range merge):
        //     <add key="RangeApiBaseUrl" value="https://host:590/" />
        //     <add key="RangeApiAuth"    value="user:pass" />   (optional Basic)
        //   Dates are ddMMyyyy_HHmmss (e.g. 21082026_090000), exactly as the
        //   upstream expects.
        //
        // UPSTREAM SHAPE (verified against energy7.in:590):
        //   { "AssetAttributes":[ {
        //       "Id": 6, "Title": "TPR V",
        //       "AvgValues":[ .., .., .. ],     // the sampled average series
        //       "AverageValue"?, "MinSafeValue"?, "MaxSafeValue"?  // if present
        //   }, ... ] }
        //   (also tolerates a "rows"/array shape with AssetAttributeId/AttributeName.)
        //   AUTH: reached through the app's own FRS client (token: LoginUser.Token) --
        //   the same authenticated path FRSAttributeRange uses -- so NO Basic-Auth
        //   creds are needed in config. If you point RangeApiBaseUrl at the raw
        //   host instead, set RangeApiAuth="Energy7@91Api:energy7@91" for Basic.
        //
        // DRIFT RULE (matches the AiChat reference: first-window avg vs last-window
        //   avg -> direction + magnitude):
        //     early = mean(first quarter of AvgValues)
        //     late  = mean(last  quarter of AvgValues)
        //     driftPct = (late - early) / |base| * 100     (base = early, or
        //                AverageValue when early ~ 0)
        //   asset drift = the max |driftPct| across its attributes.
        //   Bands (tune to taste):  |drift| >= 50 -> "URGENT"
        //                           |drift| >= 25 -> "SOON"
        //                           else          -> ""      (no promotion)
        //
        // RETURNS (compact, for the view + for GetRollup to reuse):
        //   { assetId, drift, dir, band, worstAttr,
        //     attrs:[ { attrId, attr, avg, min, max, n, drift, dir } ] }
        //
        // Version: bump ComponentVersion, e.g.
        //   2.6.0.0 - RangeHistory proxy + avg-drift summary (FRSAttributeRangeHistory)
        // =====================================================================

        // ---- drift thresholds (percent). Adjust here or move to appSettings. ----
        // Utility.AssetType -- of the twenty-five, only these two have a prediction API behind
        // them. Every other type still reaches the roster through an alert, and is never called.
        private const int RosAssetTypeTrack = 1;
        private const int RosAssetTypePoint = 3;

        private const int RosDriftDays = 7;          // drift window, shared by the rescore and the sweep
        private const double RosDriftUrgentPct = 50.0;
        private const double RosDriftSoonPct = 25.0;

        // How much of the safe band the recent level has used, measured from the middle:
        // 100% = sitting exactly on a limit, above 100% = already outside it.
        private const double RosSafeOutsidePct = 100.0;   // on or past a safe limit -> Urgent
        private const double RosSafeNearPct = 85.0;       // in the last 15% before a limit -> Soon

        // Even sampling down to at most `max` points, first and last always kept so the
        // drawn line starts and ends where the real series does.
        private static System.Collections.Generic.List<double> RosThin(
            System.Collections.Generic.List<double> xs, int max)
        {
            var outv = new System.Collections.Generic.List<double>();
            if (xs == null || xs.Count == 0)
            {
                return outv;
            }

            if (xs.Count <= max)
            {
                for (int i = 0; i < xs.Count; i++)
                {
                    outv.Add(Math.Round(xs[i], 3));
                }

                return outv;
            }

            double step = (double)(xs.Count - 1) / (max - 1);
            for (int i = 0; i < max; i++)
            {
                int idx = (int)Math.Round(i * step);
                if (idx > xs.Count - 1)
                {
                    idx = xs.Count - 1;
                }

                outv.Add(Math.Round(xs[idx], 3));
            }

            return outv;
        }

        private static double RosMean(System.Collections.Generic.List<double> xs, int from, int count)
        {
            if (xs == null || count <= 0 || from < 0 || from >= xs.Count) { return 0.0; }
            int to = Math.Min(xs.Count, from + count);
            double s = 0.0; int n = 0;
            for (int i = from; i < to; i++) { s += xs[i]; n++; }
            return n > 0 ? s / n : 0.0;
        }

        // Compute drift for one AvgValues series. Returns 0 when too short to judge.
        // A series with two or fewer distinct values is a state flag, not a measurement.
        // Its "drift" is meaningless -- a mean moving 0.1 -> 0.6 reads as 500%.
        private static bool RosSeriesIsDigital(System.Collections.Generic.List<double> series)
        {
            if (series == null || series.Count == 0)
            {
                return false;
            }

            var seen = new System.Collections.Generic.List<double>();
            for (int i = 0; i < series.Count; i++)
            {
                bool known = false;
                for (int k = 0; k < seen.Count; k++)
                {
                    if (Math.Abs(seen[k] - series[i]) < 1e-9)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    seen.Add(series[i]);
                    if (seen.Count > 2)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // v2.6.0.0 -- skipReason tells the caller WHY a drift was not computed, instead of
        // returning a fabricated number. The old version fell back to a base of 1.0 when the
        // early mean was near zero, which turned an absolute delta into a fake percentage:
        // a 0.6 unit move on a normally-zero channel read as 60% and tripped URGENT.
        private static double RosSeriesDriftPct(System.Collections.Generic.List<double> series, double averageValue,
            out string dir, out string skipReason)
        {
            dir = "";
            skipReason = "";

            if (series == null || series.Count < 6)
            {
                skipReason = "series too short (" + (series == null ? 0 : series.Count) + " < 6)";
                return 0.0;
            }

            if (RosSeriesIsDigital(series))
            {
                skipReason = "digital/state series";
                return 0.0;
            }

            int q = Math.Max(2, series.Count / 4);
            double early = RosMean(series, 0, q);
            double late = RosMean(series, series.Count - q, q);

            double base_ = 0.0;
            if (Math.Abs(early) > 1e-6)
            {
                base_ = early;
            }
            else if (Math.Abs(averageValue) > 1e-6)
            {
                base_ = averageValue;
            }
            else
            {
                // no honest denominator -- say so rather than inventing 1.0
                skipReason = "baseline is zero, percent drift undefined";
                return 0.0;
            }

            double drift = (late - early) / Math.Abs(base_) * 100.0;
            dir = drift > 0 ? "rising" : (drift < 0 ? "falling" : "");
            return drift;
        }

        // How close the recent level sits to its safe limits, as a percentage of the safe band.
        // 0 = at the middle, 100 = exactly on a limit, >100 = outside. Returns false when the
        // attribute has no usable safe band.
        private static bool RosSafeBandUse(System.Collections.Generic.List<double> series,
            double minSafe, double maxSafe, out double usedPct, out string edge)
        {
            usedPct = 0.0;
            edge = "";

            if (series == null || series.Count == 0)
            {
                return false;
            }

            double span = maxSafe - minSafe;
            if (span <= 1e-6)
            {
                return false;
            }

            int q = Math.Max(1, series.Count / 4);
            double late = RosMean(series, series.Count - q, q);
            double mid = (maxSafe + minSafe) / 2.0;
            double half = span / 2.0;

            usedPct = Math.Abs(late - mid) / half * 100.0;
            edge = late >= mid ? "max" : "min";
            return true;
        }

        public JsonResult RangeHistory(int assetId, string start, string end)
        {
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            var attrsOut = new System.Collections.Generic.List<object>();
            double assetDrift = 0.0; string assetDir = ""; string worstAttr = "";
            double worstUsedPct = 0.0; string worstUsedEdge = ""; string worstUsedAttr = "";
            try
            {
                if (assetId <= 0 || string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end))
                { RosLog("WARN", "RangeHistory bad args asset=" + assetId + " start=" + start + " end=" + end); return Json(new { assetId = assetId, drift = 0.0, dir = "", band = "", attrs = attrsOut, error = "bad args" }, JsonRequestBehavior.AllowGet); }
                RosLog("INFO", "RangeHistory asset=" + assetId + " window " + start + ".." + end);

                string rel = "FRSAttributeRangeHistory/AssetId/" + assetId.ToString() + "/" + start + "/" + end;
                string body = null;

                string rBase = (System.Configuration.ConfigurationManager.AppSettings["RangeApiBaseUrl"] ?? "").Trim();
                if (rBase.Length > 0)
                {
                    RosLog("DEBUG", "RangeHistory asset=" + assetId + " via override host " + rBase);
                    if (!rBase.EndsWith("/")) { rBase += "/"; }
                    var rReq = (HttpWebRequest)WebRequest.Create(rBase + rel);
                    rReq.Method = "GET"; rReq.Timeout = 12000; rReq.Accept = "application/json";
                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    string auth = (System.Configuration.ConfigurationManager.AppSettings["RangeApiAuth"] ?? "").Trim();
                    if (auth.Length > 0)
                    { rReq.Headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(auth)); }
                    using (var rResp = (HttpWebResponse)rReq.GetResponse())
                    using (var rSr = new System.IO.StreamReader(rResp.GetResponseStream()))
                    { body = rSr.ReadToEnd(); }
                }
                else
                {
                    // default: the app's OWN authenticated FRS API client (relative path), like RosterSiteRanges
                    RosLog("DEBUG", "RangeHistory asset=" + assetId + " via app FRS client");
                    using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                    {
                        var resp = System.Threading.Tasks.Task.Run(() => hcf.client.GetAsync(rel)).GetAwaiter().GetResult();
                        if (resp.StatusCode == HttpStatusCode.OK)
                        { body = System.Threading.Tasks.Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult(); }
                        else { RosLog("WARN", "RangeHistory asset=" + assetId + " FRS client " + (int)resp.StatusCode); }
                    }
                }

                RosLog("DEBUG", "RangeHistory asset=" + assetId + " bodyLen=" + (body == null ? 0 : body.Length));

                if (string.IsNullOrEmpty(body))
                {
                    RosLog("WARN", "RangeHistory asset=" + assetId + " EMPTY upstream body -- drift not computed");
                }

                if (!string.IsNullOrEmpty(body))
                {
                    Newtonsoft.Json.Linq.JToken doc = Newtonsoft.Json.Linq.JToken.Parse(body);
                    Newtonsoft.Json.Linq.JArray rows = null;
                    if (doc is Newtonsoft.Json.Linq.JObject)
                    {
                        var jo = (Newtonsoft.Json.Linq.JObject)doc;
                        rows = (jo["AssetAttributes"] as Newtonsoft.Json.Linq.JArray)   // energy7.in:590 shape
                            ?? (jo["rows"] as Newtonsoft.Json.Linq.JArray);             // tool-log shape
                    }
                    else { rows = doc as Newtonsoft.Json.Linq.JArray; }
                    if (rows == null)
                    {
                        RosLog("WARN", "RangeHistory asset=" + assetId + " no AssetAttributes/rows array in the reply -- drift not computed");
                    }

                    if (rows != null)
                    {
                        RosLog("INFO", "RangeHistory asset=" + assetId + " attributes=" + rows.Count);
                        foreach (Newtonsoft.Json.Linq.JToken t in rows)
                        {
                            var r = t as Newtonsoft.Json.Linq.JObject;
                            if (r == null) { continue; }

                            var series = new System.Collections.Generic.List<double>();
                            var av = r["AvgValues"] as Newtonsoft.Json.Linq.JArray;
                            if (av != null)
                            {
                                foreach (Newtonsoft.Json.Linq.JToken v in av)
                                {
                                    double dv;
                                    if (double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out dv)) { series.Add(dv); }
                                }
                            }
                            double avgVal = 0.0; double mn = 0.0, mx = 0.0;
                            double.TryParse((r["AverageValue"] ?? "").ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out avgVal);
                            double.TryParse((r["MinSafeValue"] ?? "").ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mn);
                            double.TryParse((r["MaxSafeValue"] ?? "").ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mx);

                            string dir;
                            string skipReason;
                            double drift = RosSeriesDriftPct(series, avgVal, out dir, out skipReason);

                            double usedPct = 0.0;
                            string edge = "";
                            bool hasBand = RosSafeBandUse(series, mn, mx, out usedPct, out edge);
                            if (hasBand)
                            {
                                if (usedPct > Math.Abs(worstUsedPct))
                                {
                                    worstUsedPct = usedPct;
                                    worstUsedEdge = edge;
                                    worstUsedAttr = r["Title"] != null ? r["Title"].ToString().Trim()
                                        : (r["AttributeName"] != null ? r["AttributeName"].ToString().Trim() : "");
                                }
                            }

                            attrsOut.Add(new
                            {
                                attrId = r["Id"] != null ? r["Id"].ToString() : (r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString() : ""),
                                attr = r["Title"] != null ? r["Title"].ToString().Trim() : (r["AttributeName"] != null ? r["AttributeName"].ToString().Trim() : ""),
                                avg = Math.Round(avgVal, 3),
                                min = mn,
                                max = mx,
                                n = series.Count,
                                drift = Math.Round(drift, 1),
                                dir = dir,
                                skipped = skipReason,
                                // The AvgValues series itself, so the drift popup can DRAW the trend
                                // instead of only stating a percentage. Downsampled and rounded --
                                // ten attributes of raw samples would dwarf the rest of the reply.
                                vals = RosThin(series, 80)
                            });

                            string attrName = r["Title"] != null ? r["Title"].ToString().Trim()
                                : (r["AttributeName"] != null ? r["AttributeName"].ToString().Trim() : "");

                            RosLog("DEBUG", "RangeHistory asset=" + assetId + " attr=" + attrName
                                + " n=" + series.Count + " avg=" + Math.Round(avgVal, 3)
                                + " drift=" + Math.Round(drift, 1) + "% dir=" + dir
                                + (skipReason.Length > 0 ? " SKIPPED(" + skipReason + ")" : "")
                                + (hasBand ? (" safeBandUse=" + Math.Round(usedPct, 1) + "% toward " + edge) : " noSafeBand"));

                            if (skipReason.Length == 0 && Math.Abs(drift) > Math.Abs(assetDrift))
                            {
                                assetDrift = drift;
                                assetDir = dir;
                                worstAttr = attrName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "RangeHistory asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message
                    + RosUpstreamBody(ex));
                return Json(new { assetId = assetId, drift = 0.0, dir = "", band = "", attrs = attrsOut, error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }

            double ad = Math.Abs(assetDrift);
            string driftBand = ad >= RosDriftUrgentPct ? "URGENT" : (ad >= RosDriftSoonPct ? "SOON" : "");

            // v2.6.0.0 -- MinSafeValue / MaxSafeValue were parsed, returned to the view, and never
            // used to decide anything. A value drifting 30% while sitting comfortably inside its
            // safe band was flagged, while one drifting 10% that had crossed its limit was not.
            // Proximity to the limit is the maintenance signal; raw drift is the secondary one.
            string safeBand = worstUsedPct >= RosSafeOutsidePct ? "URGENT"
                : (worstUsedPct >= RosSafeNearPct ? "SOON" : "");

            string band = RosWorstBand(driftBand, safeBand);

            // RESULT line -- TrackShort and PmOps both had one; drift did not, so the computed
            // number was invisible and "no avg drift" could not be explained from the log.
            RosLog("INFO", "RangeHistory asset=" + assetId + " RESULT attrs=" + attrsOut.Count
                + " worstAttr=" + (worstAttr.Length > 0 ? worstAttr : "-")
                + " drift=" + Math.Round(assetDrift, 1) + "% dir=" + (assetDir.Length > 0 ? assetDir : "-")
                + " driftBand=" + (driftBand.Length > 0 ? driftBand : "-")
                + " | safeBandAttr=" + (worstUsedAttr.Length > 0 ? worstUsedAttr : "-")
                + " use=" + Math.Round(worstUsedPct, 1) + "% toward " + (worstUsedEdge.Length > 0 ? worstUsedEdge : "-")
                + " safeBand=" + (safeBand.Length > 0 ? safeBand : "-")
                + " | thresholds drift=" + RosDriftSoonPct + "/" + RosDriftUrgentPct
                + " safe=" + RosSafeNearPct + "/" + RosSafeOutsidePct
                + " band=" + (band.Length > 0 ? band : "-"));

            return Json(new
            {
                retrievedAt = stamp,
                assetId = assetId,
                drift = Math.Round(assetDrift, 1),   // signed
                dir = assetDir,
                safeBandUse = Math.Round(worstUsedPct, 1),
                safeBandAttr = worstUsedAttr,
                safeBandEdge = worstUsedEdge,
                band = band,                         // "" / "SOON" / "URGENT"  -> promotion for the roster count
                worstAttr = worstAttr,
                attrs = attrsOut
            }, JsonRequestBehavior.AllowGet);
        }

        // =====================================================================
        // HOW GetRollup ADOPTS THIS (server, to change the LANDING counts):
        //   During the run/Regenerate, for each candidate asset of a site
        //   (all assets, not only alerted ones, to "add" pure drifters):
        //     var d = RangeHistory(assetId, start, end);   // start/end = the run window, ddMMyyyy_HHmmss
        //     // fold d.band into the asset's effective band (worst of alert / short / pm / drift):
        //     if (d.band == "URGENT") effBand = Urgent;
        //     else if (d.band == "SOON" && effBand < Soon) effBand = Soon;
        //   Then count assets per effBand as today. Cache d per run so it is not
        //   re-fetched on every Load. (Bounded: one RangeHistory call per asset
        //   per run; reuse the 10-min band cache pattern from RosterSiteRanges if
        //   you want to throttle.)
        // =====================================================================




        // =====================================================================
        // DROP-IN for MaintenceRosterController  (Areas/FRS25/Controllers/)
        // Paste this method inside the class (e.g. right after PmOps()).
        // No new using directives needed (same set as Roster_Live_Action.cs).
        //
        // PURPOSE  (TRACK CIRCUITS ONLY)
        //   Same-origin proxy for the track shorting/leakage predictor, and a
        //   server-computed SHORTING summary per track. Browser must not hit the
        //   Historian (:8005) directly, so this proxies it server-side.
        //
        //   Upstream (Historian):
        //     GET {HistorianApiBaseUrl}/api/asset/ai-prediction/track/{siteId}/{assetId}
        //   Web.config:
        //     <add key="HistorianApiBaseUrl" value="http://172.31.25.102:8005" />
        //     <add key="HistorianApiAuth"    value="user:pass" />   (optional Basic)
        //
        // UPSTREAM SHAPE (from the predict_track_health tool response):
        //   { asset_id, track_name, overall_condition,
        //     leakage_info:{ leakage_detected(bool), leakage_status, leakage_type,
        //                    severity, simple_summary, reason },
        //     score_breakdown:{...},
        //     major_events:[ { Start_Time, End_Time, Duration_Hours, Dominant_Cause }, ... ] }
        //   leakage_type locates the fault: "Both (Glued Joint)" = external shorting
        //   at the glued joint, "IF Only (Internal)" = feed side, "IR Only (Relay Side)"
        //   = relay side.  major_events are the shorting/leakage WINDOWS.
        //
        // RULE (yours):
        //   hasShorting = there are major_events (shorting windows) OR leakage_detected.
        //   live        = shorting is happening NOW = leakage_detected, OR the current
        //                 time falls inside a major_event window (Start..End), OR an
        //                 event has no End (still open).
        //   Band for the roster count:
        //     live         -> "URGENT"     (track is shorting right now)
        //     hasShorting  -> "SOON"       (had shorting windows, not currently live)
        //     else         -> ""
        //
        // RETURNS:
        //   { siteId, assetId, events, hasShorting, live, severity, overall,
        //     worstCause, band, lastEnd }
        //
        // Version: bump ComponentVersion, e.g.
        //   2.6.0.0 - TrackShort proxy (ai-prediction/track major_events + live shorting)
        // =====================================================================

        private static bool RosTryTime(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) { return false; }
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt);
        }

        // Past shorting windows count even after the leak reads Resolved -- see the banding note
        // below. One resolved event is noise; two is a pattern; three is a work order.
        private const int RosTrkSoonEvents = 2;
        private const int RosTrkUrgentEvents = 3;

        // MVC's Json() serialises with JavaScriptSerializer, which does not understand Newtonsoft tokens:
        // a JArray of JObject leaves the server as nested empty arrays ([[[]],[[]]]), so every field of every
        // event is lost in transport. Convert to plain dictionaries / lists / primitives first. Dates are
        // written as text so they do not become \/Date(...)\/.
        private static object RosPlain(Newtonsoft.Json.Linq.JToken token)
        {
            if (token == null)
            {
                return null;
            }

            Newtonsoft.Json.Linq.JObject obj = token as Newtonsoft.Json.Linq.JObject;

            if (obj != null)
            {
                var map = new Dictionary<string, object>();

                foreach (Newtonsoft.Json.Linq.JProperty prop in obj.Properties())
                {
                    map[prop.Name] = RosPlain(prop.Value);
                }

                return map;
            }

            Newtonsoft.Json.Linq.JArray arr = token as Newtonsoft.Json.Linq.JArray;

            if (arr != null)
            {
                var list = new List<object>();

                foreach (Newtonsoft.Json.Linq.JToken item in arr)
                {
                    list.Add(RosPlain(item));
                }

                return list;
            }

            Newtonsoft.Json.Linq.JValue val = token as Newtonsoft.Json.Linq.JValue;

            if (val == null)
            {
                return token.ToString();
            }

            if (val.Value is DateTime)
            {
                return ((DateTime)val.Value).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (val.Value is DateTimeOffset)
            {
                return ((DateTimeOffset)val.Value).ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture);
            }

            return val.Value;
        }

        public JsonResult TrackShort(int siteId, int assetId)
        {
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            int events = 0; bool leakageDetected = false, live = false, healthy = false;
            string trackName = "";
            string severity = "", overall = "", worstCause = "", lastEnd = "", leakStatus = "";
            string simpleSummary = "";
            string leakReason = "";
            Newtonsoft.Json.Linq.JArray eventsOut = null;
            try
            {
                if (siteId <= 0 || assetId <= 0)
                { RosLog("WARN", "TrackShort bad args site=" + siteId + " asset=" + assetId); return Json(new { siteId, assetId, events = 0, hasShorting = false, live = false, band = "", error = "bad args" }, JsonRequestBehavior.AllowGet); }

                string baseUrl = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "").Trim();
                if (baseUrl.Length == 0)
                { RosLog("ERROR", "TrackShort: HistorianApiBaseUrl not configured"); return Json(new { siteId, assetId, events = 0, hasShorting = false, live = false, band = "", error = "not configured" }, JsonRequestBehavior.AllowGet); }
                if (baseUrl.EndsWith("/")) { baseUrl = baseUrl.Substring(0, baseUrl.Length - 1); }
                string url = baseUrl + "/api/asset/ai-prediction/track/" + siteId + "/" + assetId;
                RosLog("INFO", "TrackShort site=" + siteId + " asset=" + assetId);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET"; req.Timeout = 12000; req.Accept = "application/json";
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                string auth = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiAuth"] ?? "").Trim();
                if (auth.Length > 0)
                { req.Headers["Authorization"] = "Basic " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(auth)); }

                string body;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                { body = sr.ReadToEnd(); }

                RosLog("DEBUG", "TrackShort asset=" + assetId + " bodyLen=" + (body == null ? 0 : body.Length)
                    + " raw=" + (string.IsNullOrEmpty(body) ? "" : (body.Length > 300 ? body.Substring(0, 300) + "..." : body)));
                if (string.IsNullOrEmpty(body))
                {
                    RosLog("WARN", "TrackShort asset=" + assetId + " EMPTY upstream body -- band not computed");
                }

                if (!string.IsNullOrEmpty(body))
                {
                    var doc = Newtonsoft.Json.Linq.JObject.Parse(body);
                    overall = doc["overall_condition"] != null ? doc["overall_condition"].ToString() : "";

                    var li = doc["leakage_info"] as Newtonsoft.Json.Linq.JObject;
                    if (li != null)
                    {
                        if (li["leakage_detected"] != null) { bool.TryParse(li["leakage_detected"].ToString(), out leakageDetected); }
                        severity = li["severity"] != null ? li["severity"].ToString() : "";
                        leakStatus = li["leakage_status"] != null ? li["leakage_status"].ToString() : "";
                        // simple_summary is the one-line verdict; reason is the model's justification.
                        // They are different sentences and both matter on site, so keep them apart --
                        // reason used to be a fallback and was lost whenever simple_summary existed.
                        if (li["simple_summary"] != null) { simpleSummary = li["simple_summary"].ToString().Trim(); }
                        if (li["reason"] != null) { leakReason = li["reason"].ToString().Trim(); }
                        string lt = li["leakage_type"] != null ? li["leakage_type"].ToString() : "";
                        if (!string.IsNullOrEmpty(lt) && lt.Trim().ToUpperInvariant() != "NONE") { worstCause = lt; }
                    }

                    // Classify current state. leakage_detected can be a PAST event -- the status/overall
                    // decide whether it is happening NOW. "Resolved"/"None" + overall "Normal" = healthy.
                    string stU = (leakStatus ?? "").Trim().ToUpperInvariant();
                    bool statusResolvedOrNone = stU.Length == 0 || stU == "RESOLVED" || stU == "NONE";
                    string ovU = (overall ?? "").Trim().ToUpperInvariant();
                    bool overallNormal = ovU.Length == 0 || ovU == "NORMAL";
                    healthy = statusResolvedOrNone && overallNormal;
                    bool leakActiveNow = leakageDetected && !statusResolvedOrNone;
                    RosLog("DEBUG", "TrackShort asset=" + assetId + " leakage detected=" + leakageDetected + " status=" + leakStatus + " severity=" + severity + " overall=" + overall + " healthy=" + healthy);

                    var me = doc["major_events"] as Newtonsoft.Json.Linq.JArray;
                    DateTime now = DateTime.Now;
                    if (me != null)
                    {
                        events = me.Count;
                        if (doc["track_name"] != null) { trackName = doc["track_name"].ToString(); }

                        // Hand major_events back VERBATIM. Its readings are plotted in the browser,
                        // and the field names inside an event are not documented anywhere I can
                        // verify -- passing the array through untouched means a rename upstream
                        // reaches the chart instead of being silently dropped by a parser here.
                        eventsOut = me;

                        Newtonsoft.Json.Linq.JObject firstEv = me.Count > 0 ? me[0] as Newtonsoft.Json.Linq.JObject : null;
                        if (firstEv != null)
                        {
                            var keyNames = new System.Collections.Generic.List<string>();
                            foreach (Newtonsoft.Json.Linq.JProperty pr in firstEv.Properties())
                            {
                                keyNames.Add(pr.Name + ":" + pr.Value.Type);
                            }

                            RosLog("DEBUG", "TrackShort asset=" + assetId + " major_event fields = "
                                + string.Join(", ", keyNames.ToArray()));
                        }
                        RosLog("INFO", "TrackShort asset=" + assetId + " major_events=" + events + " leakageDetected=" + leakageDetected + " status=" + leakStatus + " overall=" + overall);
                        foreach (Newtonsoft.Json.Linq.JToken t in me)
                        {
                            var ev = t as Newtonsoft.Json.Linq.JObject;
                            if (ev == null) { continue; }
                            string st = ev["Start_Time"] != null ? ev["Start_Time"].ToString() : "";
                            string en = ev["End_Time"] != null ? ev["End_Time"].ToString() : "";
                            if (string.IsNullOrEmpty(worstCause) && ev["Dominant_Cause"] != null) { worstCause = ev["Dominant_Cause"].ToString(); }
                            lastEnd = en;
                            DateTime sdt, edt; bool hasS = RosTryTime(st, out sdt), hasE = RosTryTime(en, out edt);
                            // live ONLY if now is inside a window, or the window is still open AND the leak is NOT resolved
                            bool evLive = false;
                            if (hasS && hasE) { if (now >= sdt && now <= edt) { evLive = true; } }
                            else if (hasS && !hasE && !statusResolvedOrNone) { evLive = true; }   // open window, unresolved
                            if (evLive) { live = true; }
                            RosLog("DEBUG", "TrackShort asset=" + assetId + " event start=" + st + " end=" + en
                                + " cause=" + (ev["Dominant_Cause"] != null ? ev["Dominant_Cause"].ToString() : "")
                                + " liveNow=" + evLive);
                        }
                    }
                    // a leak currently active AND an abnormal overall picture = live short
                    if (leakActiveNow && !overallNormal) { live = true; }
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "TrackShort site=" + siteId + " asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message
                    + RosUpstreamBody(ex));
                return Json(new { siteId, assetId, events, hasShorting = (events > 0 || leakageDetected), live, band = "", error = "fetch failed: " + ex.GetType().Name }, JsonRequestBehavior.AllowGet);
            }

            bool hasShorting = events > 0 || leakageDetected;

            // =============================================================================
            // BANDING (v2.6.0.0, second revision) -- read the model's own three fields
            // instead of inferring from leakage_detected + a healthy flag.
            //
            //   severity : normal (no leak) | low (<15) | moderate (15-40) | high (40-80)
            //              | critical (>=80)
            //   status   : none | active (leak in the last 3-4h) | ongoing (happening now)
            //              | recovery (receding now) | resolved (history)
            //   overall  : Normal | Moderate Risk | Critical Risk
            //
            //   1 URGENT   severity critical, OR status ongoing / recovery, OR overall Critical Risk
            //   2 SOON     severity moderate / high, OR status active
            //   3 MONITOR  severity low, OR status resolved, OR overall Moderate Risk
            //   4 ""       severity normal AND status none/absent
            //
            // First match wins, so a low-severity leak that is ONGOING is URGENT -- the rung
            // order is the rule, not the severity alone. Note the deliberate tension at rung 1:
            // status=recovery is URGENT even though overall usually reads Moderate Risk for a
            // recovering fault. A leak that is still receding is still a leak.
            //
            // The event count survives as a fallback BELOW rung 3, so a track with repeated
            // shorting windows still scores when the leak itself reads clear.
            // =============================================================================
            string sev = (severity ?? "").Trim().ToLowerInvariant();
            string stat = (leakStatus ?? "").Trim().ToLowerInvariant();
            string ovr = (overall ?? "").Trim().ToLowerInvariant();

            bool sevKnown = sev.Length == 0 || sev == "normal" || sev == "low" || sev == "moderate"
                || sev == "high" || sev == "critical";
            bool statKnown = stat.Length == 0 || stat == "none" || stat == "null" || stat == "active"
                || stat == "ongoing" || stat == "recovery" || stat == "resolved";

            if (!sevKnown || !statKnown)
            {
                // The band now hinges on these exact strings, so an unrecognised value is not a
                // detail -- it means the asset is scored on a vocabulary we do not know.
                RosLog("WARN", "TrackShort asset=" + assetId + " unrecognised upstream vocabulary"
                    + (sevKnown ? "" : " severity='" + severity + "'")
                    + (statKnown ? "" : " leakage_status='" + leakStatus + "'")
                    + " -- banded on what matched, check the model contract");
            }

            string bandReason;
            string rungBand;

            if (sev == "critical" || stat == "ongoing" || stat == "recovery" || ovr.IndexOf("critical risk") >= 0)
            {
                rungBand = "URGENT";
                bandReason = sev == "critical" ? "severity critical"
                    : (stat == "ongoing" ? "leak ongoing now"
                    : (stat == "recovery" ? "leak in recovery -- still receding" : "overall Critical Risk"));
            }
            else if (sev == "moderate" || sev == "high" || stat == "active")
            {
                rungBand = "SOON";
                bandReason = (sev == "moderate" || sev == "high")
                    ? ("severity " + sev) : "leak active in the last hours";
            }
            else if (sev == "low" || stat == "resolved" || ovr.IndexOf("moderate risk") >= 0)
            {
                rungBand = "MONITOR";
                bandReason = sev == "low" ? "severity low"
                    : (stat == "resolved" ? "leak resolved -- history only" : "overall Moderate Risk");
            }
            else if (sev == "normal" && (stat.Length == 0 || stat == "none" || stat == "null"))
            {
                rungBand = "";
                bandReason = "no leak";
            }
            else
            {
                rungBand = "";
                bandReason = "no rung matched (severity='" + sev + "' status='" + stat + "' overall='" + ovr + "')";
            }

            // fallback below rung 3 -- repeat offenders still score when the leak reads clear
            string eventBand = "";
            if (events >= RosTrkUrgentEvents)
            {
                eventBand = "URGENT";
            }
            else if (events >= RosTrkSoonEvents)
            {
                eventBand = "SOON";
            }

            // MONITOR is the model's explicit "no raise" (severity low / leak resolved / Moderate Risk). The event
            // count is a fallback for when the model gave NO opinion -- it must not turn "resolved, low" into
            // URGENT. appSetting RosTrkEventsRaiseMonitor = true restores the pre-2.8.8.0 behaviour.
            bool eventsMayRaise = rungBand.Length == 0
                || (rungBand == "MONITOR" && RosCfgBool("RosTrkEventsRaiseMonitor", false));

            if (eventBand.Length > 0 && !eventsMayRaise)
            {
                bandReason = bandReason + "; " + events + " event window(s) on record, not raised -- the model reads " + rungBand;
            }

            string band = rungBand;
            if (eventBand.Length > 0 && eventsMayRaise)
            {
                band = eventBand;
                bandReason = bandReason + "; raised by " + events + " event window(s) (>= "
                    + (eventBand == "URGENT" ? RosTrkUrgentEvents : RosTrkSoonEvents) + ")";
            }
            RosLog("INFO", "TrackShort site=" + siteId + " asset=" + assetId + " reason=" + bandReason);
            RosLog("INFO", "TrackShort asset=" + assetId
                + " summary=" + (simpleSummary.Length > 0 ? simpleSummary : "-")
                + " | reason=" + (leakReason.Length > 0 ? leakReason : "-"));
            RosLog("INFO", "TrackShort site=" + siteId + " asset=" + assetId
                + " RESULT severity=" + (severity.Length > 0 ? severity : "-")
                + " status=" + (leakStatus.Length > 0 ? leakStatus : "-")
                + " overall=" + (overall.Length > 0 ? overall : "-")
                + " events=" + events + " hasShorting=" + hasShorting + " live=" + live
                + " cause=" + worstCause
                + " rung=" + (rungBand.Length > 0 ? rungBand : "-")
                + " eventBand=" + (eventBand.Length > 0 ? eventBand : "-")
                + " band=" + (band.Length > 0 ? band : "-"));
            JsonResult trackResult = Json(new
            {
                retrievedAt = stamp,
                siteId,
                assetId,
                events,
                hasShorting,
                live,
                severity,
                overall,
                leakStatus,
                bandReason,
                simpleSummary,
                leakReason,
                worstCause,
                trackName,
                band,
                lastEnd,
                majorEvents = RosPlain(eventsOut)
            }, JsonRequestBehavior.AllowGet);
            trackResult.MaxJsonLength = int.MaxValue;
            return trackResult;
        }

        // =====================================================================
        // GetRollup adoption (server, LANDING counts): for each TRACK asset of a
        // site during the run, call TrackShort(siteId, assetId) and fold band into
        // the effective band (worst of alert / drift / pm-ops / track-short).
        // live short -> Urgent ; past shorting windows -> Soon. Cache per run.
        // =====================================================================

        [HttpPost]
        public JsonResult ClientLog(string level, string tag, string msg, string ver, string url)
        {
            try
            {
                string lvl = string.IsNullOrEmpty(level) ? "ERROR" : level.Trim().ToUpperInvariant();
                if (lvl != "DEBUG" && lvl != "INFO" && lvl != "WARN" && lvl != "ERROR") { lvl = "ERROR"; }

                string who = "";
                try { who = ClsHttpContent.LoginUser != null ? (" user=" + ClsHttpContent.LoginUser.Id) : ""; } catch { }

                string line = "UI[" + (tag ?? "") + "] " + (msg ?? "")
                            + (string.IsNullOrEmpty(url) ? "" : " @ " + url)
                            + (string.IsNullOrEmpty(ver) ? "" : " (view v" + ver + ")")
                            + who;

                // cap the stored message length defensively
                if (line.Length > 2000) { line = line.Substring(0, 2000) + "..."; }

                RosLog(lvl, line);
            }
            catch (Exception ex)
            {
                try { RosLog("WARN", "ClientLog handler failed: " + ex.Message); } catch { }
            }
            return Json(new { ok = true }, JsonRequestBehavior.AllowGet);
        }
        // =====================================================================
        // After this, tail the log and you will see, interleaved with the server
        // lines, entries like:
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[TRACK] fetch failed 46T asset=41814 ... @ https://.../MaintenceRoster (view v2.6.0.0) user=12
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[WINDOW] Cannot read properties of undefined ... @ .../Index:4821:17 (view v2.6.0.0)
        //   .. [ROSTER-WEB v2.6.0.0] ERROR UI[PROMISE] <rejected reason> (view v2.6.0.0)
        // =====================================================================
        // =====================================================================
        // DROP-IN for MaintenceRosterController.cs
        // Makes the LANDING KPI counts COMPOSITE (alert + track-short + pm-ops +
        // avg-drift), not alert-only. It re-scores built.Items INSIDE Regenerate,
        // BEFORE they are posted to Roster/GenerateDaily -- so the persisted
        // roster (and therefore GetRollup / the KPI strip / the station table)
        // reflect the composite. The browser cannot change these numbers; this is
        // the only place they can change.
        //
        // It REUSES the three proxy actions you already pasted (TrackShort, PmOps,
        // RangeHistory) -- one source of truth, including the resolved-leak fix --
        // by reading the `band` off each JsonResult. No logic is duplicated.
        //
        // REQUIRES (already added): TrackShort(sid,aid), PmOps(sid,aid[,start,end]),
        //   RangeHistory(aid,start,end), RosLog. Uses Newtonsoft.Json.Linq (JArray/JObject).
        //
        // ASSUMPTIONS (confirm against your roster item + GenerateDaily):
        //   1. Each built.Items entry serialises with fields: AssetId, SiteId,
        //      Priority, AssetTypeName. (The view already reads these keys.)
        //   2. Roster/GenerateDaily derives the roll-up bands FROM each item's
        //      Priority (the v2.1.0.0 "REAL ITEMS" change). If the backend re-derives
        //      priority server-side and ignores the posted value, raising it here has
        //      no effect -- then the same rule must live in RosterItemBuilder instead.
        //   3. Priority values are URGENT / SOON / MONITOR (case-insensitive). We
        //      write back UPPERCASE; if GenerateDaily is case/enum-sensitive, match it.
        //
        // COST: during Regenerate this makes up to 3 Historian/FRS calls per asset
        //   (sequential). For a large division that adds seconds-to-a-minute to the
        //   build. It runs once per Regenerate / nightly run and is cached in the
        //   persisted roster, NOT on every Load. Parallelise later if needed.
        //
        // Version: bump ComponentVersion to 2.6.0.0.
        // =====================================================================

        private static int RosPriRank(string p)
        {
            p = (p ?? "").Trim().ToUpperInvariant();
            return p == "URGENT" ? 1 : (p == "SOON" ? 2 : 3);
        }

        // worst (most urgent) of two bands; "" == none
        private static string RosWorstBand(string a, string b)
        {
            int r = Math.Min(RosPriRank(string.IsNullOrEmpty(a) ? "MONITOR" : a),
                             RosPriRank(string.IsNullOrEmpty(b) ? "MONITOR" : b));
            return r == 1 ? "URGENT" : (r == 2 ? "SOON" : "");
        }

        // raise cur by band; never lowers, preserves cur when band doesn't apply
        private static string RosRaisePriority(string cur, string band)
        {
            if (string.IsNullOrEmpty(band)) { return cur; }
            int r = Math.Min(RosPriRank(cur), RosPriRank(band));
            if (r == 1) { return "URGENT"; }
            if (r == 2) { return "SOON"; }
            return cur;
        }

        // v2.6.0.0 -- MUST mirror the view's rosFamily, cause fallback included. The old version
        // looked at AssetTypeName only. The view bothered to build a cause fallback, which means
        // AssetTypeName is not always populated on a roster item -- and when it is not, the server
        // classified EVERY asset as "Other", so the rescue never called TrackShort or PmOps at all.
        // Silent, because a call that is never made cannot log a dropped signal.
        private static string RosFamilyOf(string typeName, string cause)
        {
            string t = (typeName ?? "").ToUpperInvariant();
            string c = (cause ?? "").ToUpperInvariant();

            if (t.IndexOf("POINT") >= 0 || c.StartsWith("PT "))
            {
                return "Point";
            }

            if (t.IndexOf("SIGNAL") >= 0 || c.StartsWith("SIG") || c.IndexOf("ROSIG") >= 0)
            {
                return "Signal";
            }

            if (t.IndexOf("TRACK") >= 0 || c.StartsWith("TC ") || c.IndexOf("RAIL") >= 0 || c.IndexOf("BALST") >= 0)
            {
                return "Track";
            }

            return "Other";
        }

        // read the `band` string off a proxy action's JsonResult (reuses its logic).
        // A dropped signal is NEVER silent: every path that returns "" says why.
        private static string RosBandOf(JsonResult jr, string what, int assetId)
        {
            try
            {
                if (jr == null || jr.Data == null)
                {
                    RosLog("WARN", "RosBandOf " + what + " asset=" + assetId + " returned no data -- signal dropped");
                    return "";
                }

                System.Reflection.PropertyInfo errProp = jr.Data.GetType().GetProperty("error");
                if (errProp != null)
                {
                    string errText = errProp.GetValue(jr.Data, null) as string;
                    if (!string.IsNullOrEmpty(errText))
                    {
                        RosLog("WARN", "RosBandOf " + what + " asset=" + assetId + " reply error=" + errText);
                    }
                }

                System.Reflection.PropertyInfo prop = jr.Data.GetType().GetProperty("band");
                if (prop == null)
                {
                    RosLog("WARN", "RosBandOf " + what + " asset=" + assetId + " reply has no 'band' property -- signal dropped");
                    return "";
                }

                return (prop.GetValue(jr.Data, null) as string) ?? "";
            }
            catch (Exception ex)
            {
                RosLog("WARN", "RosBandOf " + what + " asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message);
                return "";
            }
        }

        // The view's rosPriEff has a fifth rule the server never had: a SHORT in the alert's
        // dominant cause raises the band with no fetch at all (rosIsShort). Without it an asset
        // could read SOON in the peek list and MONITOR in the station column right beside it.
        // Source order matches the view: Evidence15d.dominantCause first, then the item's Reason.
        private static readonly System.Text.RegularExpressions.Regex RosShortRx =
            new System.Text.RegularExpressions.Regex(@"(^|[^A-Z])SHORT([^A-Z]|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string RosCauseShortBand(Newtonsoft.Json.Linq.JObject it, out string cause, out bool sourceFound)
        {
            cause = "";
            sourceFound = false;

            try
            {
                bool activeNow = false;
                Newtonsoft.Json.Linq.JToken evTok = it["Evidence15d"] ?? it["evidence15d"];

                if (evTok != null)
                {
                    string raw = evTok.Type == Newtonsoft.Json.Linq.JTokenType.String ? evTok.ToString() : evTok.ToString();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        Newtonsoft.Json.Linq.JObject ev = Newtonsoft.Json.Linq.JObject.Parse(raw);
                        sourceFound = true;
                        if (ev["dominantCause"] != null)
                        {
                            cause = ev["dominantCause"].ToString();
                        }

                        if (ev["activeNow"] != null)
                        {
                            bool.TryParse(ev["activeNow"].ToString(), out activeNow);
                        }
                    }
                }

                if (string.IsNullOrEmpty(cause))
                {
                    Newtonsoft.Json.Linq.JToken rTok = it["Reason"] ?? it["reason"];
                    if (rTok != null)
                    {
                        sourceFound = true;
                        cause = rTok.ToString().Split('.')[0];
                    }
                }

                if (string.IsNullOrEmpty(cause) || !RosShortRx.IsMatch(cause))
                {
                    return "";
                }

                // same as the view: an active short is Urgent, a past one is Soon
                return activeNow ? "URGENT" : "SOON";
            }
            catch (Exception ex)
            {
                RosLog("WARN", "RosCauseShortBand " + ex.GetType().Name + " " + ex.Message);
                return "";
            }
        }

        // =====================================================================
        // The SITE SWEEP that lived here in 2.6.0.0 has been REMOVED in 2.7.0.0.
        //
        // It harvested asset ids out of FRSAttributeRange because no asset list was available,
        // then walked them in a second pass beside the alert-first rescore. RosCompositeRescore
        // is now asset-first and covers the same ground in ONE pass from a real asset list
        // (IAssetService.GetAssestBy), so keeping both would mean two places deciding which
        // assets get scored -- and they would drift.
        //
        // Still used by the asset-first loop: RosSweepDegree, RosSweepBudgetSeconds.
        // No longer used: RosSweepOff, RosSweepPm, RosSweepPmDegree, RosSweepMaxAssets,
        // RosAddDriftPct, RosAddSafePct, RosAddTrkEvents.
        // =====================================================================

        private static int RosCfgInt(string key, int fallback)
        {
            try
            {
                int v;
                if (int.TryParse((System.Configuration.ConfigurationManager.AppSettings[key] ?? "").Trim(), out v) && v > 0)
                {
                    return v;
                }
            }
            catch
            {
            }

            return fallback;
        }

        // Accepts the spellings people actually type. A kill switch that silently ignores
        // value="1" and leaves the feature running is worse than no kill switch, so an
        // unrecognised value is logged rather than quietly falling back.
        private static bool RosCfgBool(string key, bool fallback)
        {
            try
            {
                string v = (System.Configuration.ConfigurationManager.AppSettings[key] ?? "").Trim().ToUpperInvariant();

                if (v.Length == 0)
                {
                    return fallback;
                }

                if (v == "TRUE" || v == "1" || v == "YES" || v == "Y" || v == "ON")
                {
                    return true;
                }

                if (v == "FALSE" || v == "0" || v == "NO" || v == "N" || v == "OFF")
                {
                    return false;
                }

                RosLog("WARN", "appSetting " + key + "=\"" + v + "\" is not a recognised true/false value -- using "
                    + (fallback ? "true" : "false") + ". Use true or false.");
            }
            catch
            {
            }

            return fallback;
        }

        // Both sentences, one column. Skips the join when they are empty or say the same thing,
        // and caps at the SignalNote column width so a long reason cannot truncate the verdict.
        private static string RosJoinNote(string summary, string reason)
        {
            summary = (summary ?? "").Trim();
            reason = (reason ?? "").Trim();

            if (reason.Length == 0 || string.Equals(summary, reason, StringComparison.OrdinalIgnoreCase))
            {
                return summary.Length > 480 ? summary.Substring(0, 480) : summary;
            }

            if (summary.Length == 0)
            {
                return reason.Length > 480 ? reason.Substring(0, 480) : reason;
            }

            string joined = summary + " -- " + reason;
            return joined.Length > 480 ? joined.Substring(0, 480) : joined;
        }

        private static int RosSweepDegree { get { return RosCfgInt("RosSweepDegree", 8); } }
        private static int RosSweepBudgetSeconds { get { return RosCfgInt("RosSweepBudgetSeconds", 600); } }

        private static object RosPropOf(JsonResult jr, string name)
        {
            try
            {
                if (jr == null || jr.Data == null)
                {
                    return null;
                }

                System.Reflection.PropertyInfo p = jr.Data.GetType().GetProperty(name);
                return p == null ? null : p.GetValue(jr.Data, null);
            }
            catch
            {
                return null;
            }
        }

        private static int RosPropInt(JsonResult jr, string name)
        {
            object o = RosPropOf(jr, name);

            if (o == null)
            {
                return 0;
            }

            int i;
            return int.TryParse(Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture), out i) ? i : 0;
        }

        private static string RosPropString(JsonResult jr, string name)
        {
            object o = RosPropOf(jr, name);
            return o == null ? "" : Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
        }

        // bounded parallel map -- these are synchronous HttpWebRequest calls, so every one blocks
        // a thread-pool thread. Unbounded would starve the app on a large site.
        private static void RosParallel<T>(System.Collections.Generic.IList<T> items, int degree, Action<T> body)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            // every call targets ONE host, and .NET caps concurrent connections per host. Without
            // this the tasks run in parallel while the requests quietly queue two at a time.
            if (ServicePointManager.DefaultConnectionLimit < degree + 4)
            {
                ServicePointManager.DefaultConnectionLimit = degree + 4;
            }

            var opts = new System.Threading.Tasks.ParallelOptions();
            opts.MaxDegreeOfParallelism = Math.Max(1, degree);

            System.Threading.Tasks.Parallel.ForEach(items, opts, item =>
            {
                try
                {
                    body(item);
                }
                catch (Exception ex)
                {
                    RosLog("WARN", "Parallel task " + ex.GetType().Name + " " + ex.Message);
                }
            });
        }

        /// <summary>One asset from GetAssestBy, reduced to what a roster item needs.</summary>
        private class RosAsset
        {
            public int AssetId;
            public int SiteId;
            public int AssetTypeId;
            public string AssetName = "";
            public string AssetTypeName = "";
            public string StationName = "";
        }

        /// <summary>
        /// Every asset at a site, via the same service the GetAssestBy action uses.
        /// Serialised to JSON and read by candidate field name rather than cast to a Domain type:
        /// a field spelled AssetName in one service and Name in another would produce a roster
        /// that builds cleanly with nothing in it, which is the worst kind of failure here.
        /// </summary>
        private List<RosAsset> RosAssetsOfSite(int siteId, string stationName)
        {
            List<RosAsset> assets = new List<RosAsset>();

            try
            {
                object raw = _assetService.GetAssestBy(siteId);

                if (raw == null)
                {
                    RosLog("WARN", "RosAssetsOfSite site=" + siteId + " service returned null");
                    return assets;
                }

                Newtonsoft.Json.Linq.JArray rows = Newtonsoft.Json.Linq.JArray.FromObject(raw);

                foreach (Newtonsoft.Json.Linq.JToken t in rows)
                {
                    Newtonsoft.Json.Linq.JObject o = t as Newtonsoft.Json.Linq.JObject;
                    if (o == null)
                    {
                        continue;
                    }

                    RosAsset a = new RosAsset();
                    a.AssetId = RosJInt(o, "AssetId", "Id", "assetId", "id");
                    a.SiteId = RosJInt(o, "SiteId", "siteId");
                    a.AssetTypeId = RosJInt(o, "AssetTypeId", "AssetType", "assetTypeId");
                    a.AssetName = RosJStr(o, "AssetName", "Name", "assetName", "name");
                    a.AssetTypeName = RosJStr(o, "AssetTypeName", "AssetTypeTitle", "TypeName");
                    a.StationName = RosJStr(o, "StationName", "SiteName");

                    if (a.SiteId <= 0)
                    {
                        a.SiteId = siteId;
                    }

                    if (a.StationName.Length == 0)
                    {
                        a.StationName = stationName ?? "";
                    }

                    if (a.AssetId > 0)
                    {
                        assets.Add(a);
                    }
                }

                int tracks = 0;
                int points = 0;

                for (int i = 0; i < assets.Count; i++)
                {
                    if (assets[i].AssetTypeId == RosAssetTypeTrack) { tracks++; }
                    else if (assets[i].AssetTypeId == RosAssetTypePoint) { points++; }
                }

                RosLog("INFO", "RosAssetsOfSite site=" + siteId + " assets=" + assets.Count
                    + " track=" + tracks + " point=" + points
                    + " other=" + (assets.Count - tracks - points));

                if (assets.Count > 0 && tracks == 0 && points == 0)
                {
                    RosLog("WARN", "RosAssetsOfSite site=" + siteId + " returned " + assets.Count
                        + " asset(s) but NONE is AssetTypeId 1 or 3 -- no signal will be called here."
                        + " Check the AssetTypeId field name on the asset row.");
                }
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "RosAssetsOfSite site=" + siteId + " "
                    + ex.GetType().Name + " " + ex.Message);
            }

            return assets;
        }

        private static int RosJInt(Newtonsoft.Json.Linq.JObject o, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Newtonsoft.Json.Linq.JToken t = o[names[i]];

                if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    continue;
                }

                int v;
                if (int.TryParse(t.ToString(), out v) && v != 0)
                {
                    return v;
                }
            }

            return 0;
        }

        private static string RosJStr(Newtonsoft.Json.Linq.JObject o, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                Newtonsoft.Json.Linq.JToken t = o[names[i]];

                if (t == null || t.Type == Newtonsoft.Json.Linq.JTokenType.Null)
                {
                    continue;
                }

                string v = t.ToString().Trim();

                if (v.Length > 0)
                {
                    return v;
                }
            }

            return "";
        }

        private static string RosEscJson(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        /// <summary>
        /// ASSET-FIRST composition (2.7.0.0). Was alert-first, which meant an asset that was
        /// shorting but had raised no alert was never called and never stored.
        ///
        ///   1. every site in the division  ->  every asset  (GetAssestBy)
        ///   2. TRACK -> TrackShort, POINT_MACHINE -> PmOps
        ///   3. alert present  -> the built item, plus the signal bands
        ///      no alert, band -> a signal-only item, IsSwept = 1
        ///      neither        -> nothing
        ///
        /// The site list comes from the division, not from the alerts: a site with ZERO alerts
        /// is exactly where a silently shorting track hides, and an alert-first loop can never
        /// see it.
        ///
        /// Counting follows from the stored columns and needs no separate bookkeeping:
        /// Alert excludes IsSwept, Track shorting counts TrackShortBand, PM counts PmBand.
        /// </summary>
        private static string RosRosterFamily(int id) { return id == 1 ? "Track" : id == 2 ? "Signal" : id == 3 ? "Point" : id == 34 ? "IPS" : "Other"; }

        private static Newtonsoft.Json.Linq.JObject RosSourceSnapshot(JsonResult result, string kind)
        {
            var data = result != null && result.Data != null ? Newtonsoft.Json.Linq.JObject.FromObject(result.Data) : new Newtonsoft.Json.Linq.JObject();
            bool valid = string.IsNullOrWhiteSpace((string)data["error"]);
            if (kind == "drift") { var attrs = data["attrs"] as Newtonsoft.Json.Linq.JArray; valid = valid && attrs != null && attrs.OfType<Newtonsoft.Json.Linq.JObject>().Any(a => string.IsNullOrEmpty((string)a["skipped"]) && (int?)a["n"] >= 2); }
            if (kind == "pm") { valid = valid && ((int?)data["ops"] > 0 || !string.IsNullOrEmpty((string)data["band"])); }
            if (kind == "track") { valid = valid && (!string.IsNullOrEmpty((string)data["overall"]) || !string.IsNullOrEmpty((string)data["severity"]) || !string.IsNullOrEmpty((string)data["band"])); }
            // Keep the assessment and numeric summaries; chart point arrays are fetched on demand.
            var attrsOut = data["attrs"] as Newtonsoft.Json.Linq.JArray;
            if (attrsOut != null) { foreach (var at in attrsOut.OfType<Newtonsoft.Json.Linq.JObject>()) { foreach (string key in new[] { "values", "Values", "history", "History", "points", "Points" }) { at.Remove(key); } } }
            return new Newtonsoft.Json.Linq.JObject { ["valid"] = valid, ["data"] = data, ["error"] = valid ? "" : "Source unavailable or insufficient data" };
        }

        private static void RosAttachSources(Newtonsoft.Json.Linq.JObject item, Newtonsoft.Json.Linq.JObject sources, string start, string end)
        {
            Newtonsoft.Json.Linq.JObject evidence;
            try { evidence = Newtonsoft.Json.Linq.JObject.Parse((string)item["Evidence15d"] ?? "{}"); } catch { evidence = new Newtonsoft.Json.Linq.JObject(); }
            evidence["sources"] = sources.DeepClone(); evidence["sourceWindowStart"] = start; evidence["sourceWindowEnd"] = end; evidence["sourceSchema"] = 2;
            item["Evidence15d"] = evidence.ToString(Newtonsoft.Json.Formatting.None);
        }

        private Newtonsoft.Json.Linq.JArray RosCompositeRescore(object items, DateTime target, int divisionId)
        {
            Newtonsoft.Json.Linq.JArray arr = items as Newtonsoft.Json.Linq.JArray;

            if (arr == null)
            {
                try
                {
                    arr = Newtonsoft.Json.Linq.JArray.FromObject(items);
                }
                catch (Exception ex)
                {
                    RosLog("ERROR", "Rescore division=" + divisionId
                        + " items are not a JSON array: " + ex.Message + " -- posting them unchanged");
                    return null;
                }
            }

            // The agreed maintenance scope is Track, Signal, Point and IPS.
            foreach (var candidate in arr.OfType<Newtonsoft.Json.Linq.JObject>().ToList())
            {
                int type = RosJInt(candidate, "AssetTypeId", "assetTypeId");
                if (type > 0 && type != 1 && type != 2 && type != 3 && type != 34) { arr.Remove(candidate); }
            }

            // index the built (alert) items so an asset finds its own in one lookup
            var byAsset = new Dictionary<string, Newtonsoft.Json.Linq.JObject>();

            foreach (Newtonsoft.Json.Linq.JToken tok in arr)
            {
                Newtonsoft.Json.Linq.JObject it = tok as Newtonsoft.Json.Linq.JObject;

                if (it == null)
                {
                    continue;
                }

                int aid0 = RosJInt(it, "AssetId");
                int sid0 = RosJInt(it, "SiteId");

                if (aid0 > 0 && sid0 > 0)
                {
                    byAsset[sid0 + ":" + aid0] = it;
                }
            }

            List<Domain.Site> sites = new List<Domain.Site>();

            try
            {
                sites = Helper.FilterCacheHelper.GetSitesByDivisionId(_siteService, divisionId);
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "Rescore division=" + divisionId + " cannot list sites: "
                    + ex.GetType().Name + " " + ex.Message + " -- alert items posted unscored");
                return arr;
            }

            if (sites == null || sites.Count == 0)
            {
                RosLog("WARN", "Rescore division=" + divisionId
                    + " no sites -- alert items posted unscored");
                return arr;
            }

            string dStart = target.AddDays(-RosDriftDays).ToString("ddMMyyyy_HHmmss");
            string dEnd = target.ToString("ddMMyyyy_HHmmss");
            string pStart = target.AddDays(-30).ToString("ddMMyyyy_HHmmss");

            DateTime deadline = DateTime.Now.AddSeconds(RosSweepBudgetSeconds);
            int outOfTime = 0;
            int scanned = 0;
            int raised = 0;
            int addedTrack = 0;
            int addedPm = 0;
            int driftHits = 0;

            // How many assets the loop actually WALKED, per site. Not derivable from the items --
            // an asset that ran clear produces no row -- and it is what makes "11 of 16" mean
            // something rather than "11 of the ones we happened to keep".
            var scannedBySite = new Dictionary<int, int>();
            var added = new System.Collections.Concurrent.ConcurrentBag<Newtonsoft.Json.Linq.JObject>();

            for (int siteIndex = 0; siteIndex < sites.Count; siteIndex++)
            {
                int siteId = sites[siteIndex].Id;
                string stationName = sites[siteIndex].Name;

                List<RosAsset> assets = RosAssetsOfSite(siteId, stationName).FindAll(a => a.AssetTypeId == 1 || a.AssetTypeId == 2 || a.AssetTypeId == 3 || a.AssetTypeId == 34);
                int siteScanned = 0;

                if (assets.Count == 0)
                {
                    continue;
                }

                DateTime siteStart = DateTime.Now;

                RosParallel(assets, RosSweepDegree, asset =>
                {
                    if (DateTime.Now > deadline)
                    {
                        System.Threading.Interlocked.Increment(ref outOfTime);

                        // Mark the item so the dashboard can say "not scored" rather than showing
                        // a clear result. An analysis that never ran and one that ran and found
                        // nothing must never look the same -- that is the difference between
                        // "this track is fine" and "nobody looked at this track".
                        Newtonsoft.Json.Linq.JObject miss;

                        if (byAsset.TryGetValue(asset.SiteId + ":" + asset.AssetId, out miss))
                        {
                            lock (miss)
                            {
                                miss["NotScored"] = true;
                            }
                        }

                        return;
                    }

                    System.Threading.Interlocked.Increment(ref scanned);
                    System.Threading.Interlocked.Increment(ref siteScanned);

                    string trkBand = "";
                    string pmBand = "";
                    string driftBand = "";
                    string note = "";
                    string reason = "";

                    var sourceSnapshot = new Newtonsoft.Json.Linq.JObject();
                    // Each eligible asset is assessed independently, including assets with no alerts.
                    try
                    {
                        JsonResult djr = RangeHistory(asset.AssetId, dStart, dEnd);
                        driftBand = RosBandOf(djr, "RangeHistory", asset.AssetId);
                        sourceSnapshot["drift"] = RosSourceSnapshot(djr, "drift");
                        if (driftBand.Length > 0) { System.Threading.Interlocked.Increment(ref driftHits); }
                    }
                    catch (Exception dex)
                    {
                        sourceSnapshot["drift"] = new Newtonsoft.Json.Linq.JObject { ["valid"] = false, ["error"] = "Drift source unavailable" };
                        RosLog("WARN", "Rescore drift asset=" + asset.AssetId + " " + dex.Message);
                    }

                    try
                    {
                        if (asset.AssetTypeId == RosAssetTypeTrack)
                        {
                            JsonResult tjr = TrackShort(asset.SiteId, asset.AssetId);
                            trkBand = RosBandOf(tjr, "TrackShort", asset.AssetId);
                            sourceSnapshot["track"] = RosSourceSnapshot(tjr, "track");
                            note = RosJoinNote(RosPropString(tjr, "simpleSummary"),
                                               RosPropString(tjr, "leakReason"));

                            int events = RosPropInt(tjr, "events");
                            string worstCause = RosPropString(tjr, "worstCause");
                            string severity = RosPropString(tjr, "severity");
                            string leakStatus = RosPropString(tjr, "leakStatus");

                            reason = "Track shorting \u2014 " + events + " window(s)"
                                + (severity.Length > 0 ? ", severity " + severity.ToLowerInvariant() : "")
                                + (leakStatus.Length > 0 ? ", " + leakStatus.ToLowerInvariant() : "")
                                + (worstCause.Length > 0 ? " (" + worstCause + ")" : "");
                        }
                        else if (asset.AssetTypeId == RosAssetTypePoint)
                        {
                            JsonResult pjr = PmOps(asset.SiteId, asset.AssetId, pStart, dEnd);
                            pmBand = RosBandOf(pjr, "PmOps", asset.AssetId);
                            sourceSnapshot["pm"] = RosSourceSnapshot(pjr, "pm");
                            note = RosPropString(pjr, "condition");

                            string worstState = RosPropString(pjr, "worstState");
                            reason = "Point prediction \u2014 " + worstState
                                + (note.Length > 0 ? " (" + note + ")" : "");
                        }
                    }
                    catch (Exception ex)
                    {
                        RosLog("WARN", "Rescore asset=" + asset.AssetId + " site=" + asset.SiteId
                            + " " + ex.GetType().Name + " " + ex.Message);
                    }

                    Newtonsoft.Json.Linq.JObject item;
                    bool hasAlert = byAsset.TryGetValue(asset.SiteId + ":" + asset.AssetId, out item);

                    if (hasAlert)
                    {
                        // the alert item keeps its Reason, Evidence15d and Priority; a signal
                        // only ever RAISES it
                        lock (item)
                        {
                            string cause;
                            bool sourceFound;
                            string causeBand = RosCauseShortBand(item, out cause, out sourceFound);

                            string comp = RosWorstBand(RosWorstBand(RosWorstBand(causeBand, trkBand), pmBand), driftBand);
                            string pri = item["Priority"] != null ? item["Priority"].ToString() : "MONITOR";

                            item["AssetTypeId"] = asset.AssetTypeId;
                            item["AssetFamily"] = RosRosterFamily(asset.AssetTypeId);
                            RosAttachSources(item, sourceSnapshot, dStart, dEnd);
                            item["TrackShortBand"] = trkBand;
                            item["PmBand"] = pmBand;
                            item["DriftBand"] = driftBand;
                            item["SignalNote"] = note;

                            if (comp.Length > 0)
                            {
                                string why = "";
                                if (causeBand.Length > 0) { why += (why.Length > 0 ? "; " : "") + "cause SHORT (" + causeBand + ")"; }
                                if (trkBand.Length > 0) { why += (why.Length > 0 ? "; " : "") + "track-short (" + trkBand + ")"; }
                                if (pmBand.Length > 0) { why += (why.Length > 0 ? "; " : "") + "pm (" + pmBand + ")"; }
                                if (driftBand.Length > 0) { why += (why.Length > 0 ? "; " : "") + "avg-drift (" + driftBand + ")"; }

                                item["CompositeBand"] = comp;
                                item["CompositeReason"] = why;

                                string np = RosRaisePriority(pri, comp);

                                if (!string.Equals(np, pri, StringComparison.OrdinalIgnoreCase))
                                {
                                    item["Priority"] = np;
                                    System.Threading.Interlocked.Increment(ref raised);
                                    RosLog("INFO", "Rescore RAISED asset=" + asset.AssetId
                                        + " site=" + asset.SiteId + " " + pri + " -> " + np
                                        + " (composite=" + comp + ")");
                                }
                            }
                        }

                        return;
                    }

                    // no alert: store the asset only when a signal actually said something
                    bool trkRaises = trkBand == "URGENT" || trkBand == "SOON";
                    bool pmRaises = pmBand == "URGENT" || pmBand == "SOON";

                    bool driftRaises = driftBand == "URGENT" || driftBand == "SOON";
                    if (!trkRaises && !pmRaises && !driftRaises)
                    {
                        return;
                    }

                    string band = RosWorstBand(RosWorstBand(trkBand, pmBand), driftBand);
                    string family = RosRosterFamily(asset.AssetTypeId);
                    if (driftRaises) { reason = (reason.Length > 0 ? reason + "; " : "") + "Average drift (" + driftBand + ")"; }

                    var ni = new Newtonsoft.Json.Linq.JObject();
                    ni["DivisionId"] = divisionId;
                    ni["SiteId"] = asset.SiteId;
                    ni["StationName"] = asset.StationName;
                    ni["AssetId"] = asset.AssetId;
                    ni["AssetName"] = asset.AssetName.Length > 0 ? asset.AssetName : ("Asset " + asset.AssetId);
                    ni["AssetTypeName"] = asset.AssetTypeName.Length > 0 ? asset.AssetTypeName : family;
                    ni["AssetFamily"] = family;
                    ni["AssetTypeId"] = asset.AssetTypeId;
                    ni["Priority"] = band.Length > 0 ? band : "MONITOR";
                    ni["Reason"] = reason;
                    ni["ItemKey"] = divisionId + ":" + asset.SiteId + ":" + asset.AssetId + ":"
                        + "MAINTENANCE";
                    ni["FirstSeenDate"] = target.Date.ToString("yyyy-MM-dd");
                    ni["DayCount"] = 1;
                    ni["CloseStatus"] = "OPEN";
                    ni["TrackShortBand"] = trkBand;
                    ni["PmBand"] = pmBand;
                    ni["DriftBand"] = driftBand;
                    ni["CompositeBand"] = band;
                    ni["CompositeReason"] = (trkRaises ? "track-short (" + trkBand + "); " : "") + (pmRaises ? "pm-prediction (" + pmBand + "); " : "") + (driftRaises ? "avg-drift (" + driftBand + ")" : "");
                    ni["SignalNote"] = note;

                    // IsSwept is what keeps the Alert count honest: this asset raised no alert,
                    // so it must not be counted as one. It still counts in its own signal column.
                    ni["IsSwept"] = true;

                    // No alerts means no 15-day evidence. Write the zeros rather than NULL --
                    // the view parses this blob for every card and classifies on dominantCause.
                    ni["Evidence15d"] = "{\"windowDays\":15,\"total\":0,\"unacked\":0,\"activeNow\":false,"
                        + "\"firstHalf\":0,\"secondHalf\":0,\"trend\":\"stable\",\"dominantCause\":\""
                        + RosEscJson(reason) + "\",\"lastSet\":\"\",\"swept\":true}";

                    RosAttachSources(ni, sourceSnapshot, dStart, dEnd);
                    added.Add(ni);

                    if (trkRaises)
                    {
                        System.Threading.Interlocked.Increment(ref addedTrack);
                    }
                    else
                    {
                        System.Threading.Interlocked.Increment(ref addedPm);
                    }
                });

                scannedBySite[siteId] = siteScanned;

                RosLog("INFO", "Rescore site=" + siteId + " assets=" + assets.Count
                    + " in " + Math.Round((DateTime.Now - siteStart).TotalSeconds, 1)
                    + "s at degree " + RosSweepDegree);
            }

            foreach (Newtonsoft.Json.Linq.JObject candidate in arr.OfType<Newtonsoft.Json.Linq.JObject>())
            {
                Newtonsoft.Json.Linq.JObject scoredItem;
                if (!byAsset.TryGetValue(RosJInt(candidate, "SiteId") + ":" + RosJInt(candidate, "AssetId"), out scoredItem) || object.ReferenceEquals(candidate, scoredItem)) { continue; }
                foreach (string key in new[] { "AssetTypeId", "AssetFamily", "TrackShortBand", "PmBand", "DriftBand", "NotScored" }) { if (scoredItem[key] != null) { candidate[key] = scoredItem[key].DeepClone(); } }
                string band = RosWorstBand(RosWorstBand(RosJStr(candidate, "TrackShortBand"), RosJStr(candidate, "PmBand")), RosJStr(candidate, "DriftBand"));
                candidate["Priority"] = RosRaisePriority(RosJStr(candidate, "Priority"), band);
                try { var ev = Newtonsoft.Json.Linq.JObject.Parse(RosJStr(scoredItem, "Evidence15d")); var sources = ev["sources"] as Newtonsoft.Json.Linq.JObject; if (sources != null) { RosAttachSources(candidate, sources, dStart, dEnd); } } catch { }
            }

            foreach (Newtonsoft.Json.Linq.JObject ni in added)
            {
                arr.Add(ni);
            }

            // Carried on the items because that is the only channel to the API that exists.
            // RosterService can lift it onto the station line (it is the same value for every
            // item at a site) without a schema change on RosterRun.
            foreach (Newtonsoft.Json.Linq.JToken tok2 in arr)
            {
                Newtonsoft.Json.Linq.JObject it2 = tok2 as Newtonsoft.Json.Linq.JObject;

                if (it2 == null)
                {
                    continue;
                }

                int sid2 = RosJInt(it2, "SiteId");
                int sc;

                if (sid2 > 0 && scannedBySite.TryGetValue(sid2, out sc))
                {
                    it2["ScannedCount"] = sc;
                }
            }

            if (outOfTime > 0)
            {
                RosLog("WARN", "Rescore division=" + divisionId + " ran out of time after "
                    + RosSweepBudgetSeconds + "s -- " + outOfTime + " asset(s) not scored."
                    + " The roster is complete but partially scored. Raise RosSweepBudgetSeconds"
                    + " or RosSweepDegree.");
            }

            RosLog("INFO", "Rescore division=" + divisionId
                + " sites=" + sites.Count
                + " scanned=" + scanned
                + " alertItems=" + byAsset.Count
                + " raised=" + raised
                + " addedTrackShort=" + addedTrack
                + " addedPmPredict=" + addedPm
                + " driftBands=" + driftHits
                + " total=" + arr.Count
                + (outOfTime > 0 ? " notScored=" + outOfTime : ""));

            return arr;
        }

        // =====================================================================
        // REGENERATE HOOK  --  APPLIED in v2.6.0.0. The live code is in Regenerate()
        // just above the payload; the OLD -> NEW record below is kept for history only.
        // Do NOT paste it again -- it is already in.
        //
        // OLD:
        //     Helper.RosterItemBuilder.BuildResult built =
        //         Helper.RosterItemBuilder.Build(alerts, divisionId, target);
        //
        //     RosLog("INFO", "Regenerate division=" + divisionId + " date=" + date
        //         + " alerts=" + built.AlertsRead + "/" + totalRecord
        //         + " items=" + built.Items.Count);
        //
        //     var payload = new
        //     {
        //         DivisionId = divisionId,
        //         RosterDate = date,
        //         GeneratedBy = userId > 0 ? "manual:" + userId : "manual",
        //         Force = true,
        //         AlertsRead = built.AlertsRead,
        //         AlertsTotal = totalRecord > 0 ? totalRecord : built.AlertsRead,
        //         SourceNote = built.SourceNote,
        //         Items = built.Items
        //     };
        //
        // NEW:
        //     Helper.RosterItemBuilder.BuildResult built =
        //         Helper.RosterItemBuilder.Build(alerts, divisionId, target);
        //
        //     RosLog("INFO", "Regenerate division=" + divisionId + " date=" + date
        //         + " alerts=" + built.AlertsRead + "/" + totalRecord
        //         + " items=" + built.Items.Count);
        //
        //     // Fold track-short + pm-ops + avg-drift into each item's Priority so the
        //     // persisted roll-up counts are composite (never breaks the build).
        //     object itemsToPost = built.Items;
        //     try { itemsToPost = RosCompositeRescore(built.Items, target, divisionId); }
        //     catch (Exception rex)
        //     { RosLog("ERROR", "Rescore failed, posting alert-only items: " + rex.Message); itemsToPost = built.Items; }
        //
        //     var payload = new
        //     {
        //         DivisionId = divisionId,
        //         RosterDate = date,
        //         GeneratedBy = userId > 0 ? "manual:" + userId : "manual",
        //         Force = true,
        //         AlertsRead = built.AlertsRead,
        //         AlertsTotal = totalRecord > 0 ? totalRecord : built.AlertsRead,
        //         SourceNote = built.SourceNote,
        //         Items = itemsToPost            // <-- re-scored items
        //     };
        //
        // Then press Regenerate. The log will show "Rescore ... raised=N" and each
        // "Rescore RAISED asset=.." line; the KPI strip + station counts follow.
        // =====================================================================


    }
}