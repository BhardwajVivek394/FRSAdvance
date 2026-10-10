using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    [E7FRSAdvance.Areas.FRS25.Filter.Authenticate]
    public class AlertAnalysisController : Controller
    {
        bool siteKeep = E7FRSAdvance.Utility.ClsHttpContent.LoginUser.IsSiteKeeping;

        // ------------------------------------------------------------------
        // AlertAnalysis -- component version (Major.Minor.Build.Revision)
        // ------------------------------------------------------------------
        public const string ComponentVersion = "1.0.6.6";
        // 1.0.6.6 - LiveValues log lines now carry the RUN ID and ALERT ID. The 1.0.6.5 lines landed
        //           in the trace log with nothing to join them to the AI diagnostic but a timestamp,
        //           so the line explaining a blank card could not be found from the run that produced
        //           it. runId is the same traceId the diagnostic prints. It arrives from the browser,
        //           so it is sanitised before it reaches a log file -- an unfiltered id could forge a
        //           line break and inject a fake entry.
        // 1.0.6.5 - LiveValues called the WRONG HOST. 1.0.6.4 mirrored GetFRSAlertById's
        //           HttpClientFactory, which carries the FRS base (APIBaseUrl, ...:90/api/) --
        //           but LiveValue is an EdgeX DataAPI route (ProxyBaseUrl, ...:8083/api/). The
        //           request 404'd, the catch swallowed the reason, and every Refresh reported
        //           "No readings returned". Now calls the DataAPI base directly and LOGS both the
        //           success count and the failure reason, so the next fault of this kind is visible
        //           in one line instead of two rounds of guessing.

        // 1.0.6.6: every LiveValues line carries the RUN ID and ALERT ID. Without them a failure
        // logs into a different file from the AI diagnostic with nothing to join on but a timestamp,
        // so the one line that explains a blank card cannot be found from the run that produced it.
        // runId is the same traceId the diagnostic prints (sse-xxxxxxxx).
        private static void AiLogLive(string runId, int alertId, string msg)
        {
            try
            {
                string tag = "[AlertAnalysis]"
                           + (string.IsNullOrEmpty(runId) ? "" : " [" + SafeIdForLog(runId) + "]")
                           + (alertId > 0 ? " [alert " + alertId + "]" : "");
                System.Diagnostics.Trace.TraceInformation(tag + " " + msg);
            }
            catch { }
        }

        // The id comes from the browser, so it is untrusted input on its way into a log file.
        // Cap it and strip anything that could forge a new log line or a new field.
        private static string SafeIdForLog(string v)
        {
            if (string.IsNullOrEmpty(v)) { return ""; }
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length && sb.Length < 40; i++)
            {
                char c = v[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') { sb.Append(c); }
            }
            return sb.ToString();
        }
        // 1.0.6.4 - LiveValues parsed fields the DataAPI does not return. Its response carries
        //           Value / AssetAttributeId / TagUnit / TimestampDevice -- not CurrentValue,
        //           AssetAttributeName, MinValue or MaxValue. Every row failed the numeric parse and
        //           was skipped, so Refresh reported "No readings returned" every time. Now reads the
        //           real fields; names and safe ranges are merged client-side from the snapshot.
        // 1.0.6.3 - LiveValues endpoint for the AI popup's Live data chapter: on-demand current
        //           readings with safe-range state decided server-side, so the card and the
        //           analysis-time snapshot cannot disagree about what "in range" means.
        // 1.0.6.2 - Projects Remark + MaintainerRemarks. They were fetched and discarded. A bare
        //           "true"/"false" in Remark is the maintainer confirming whether the alert was
        //           genuine -- the only ground truth the system holds, and the basis for measuring
        //           verdict accuracy.
        // 1.0.6.1 - ResetTimeStamp is DateTime? (nullable). 1.0.6.0 assigned it straight to a
        //           DateTime, which does not compile. Read through HasValue/Value -- and note that
        //           the null case is not an edge case here: it IS the "still open" alert, which the
        //           Duration/Rectification columns are meant to render as a dash.
        // 1.0.6.0 - GetLiveAlerts returns the columns the board table was missing: ZoneName,
        //           DivisionName, ResetTimeStamp (the populated clear time -- RectificationDateTime
        //           is null on live rows) and a precomputed DurationText (reset - set, d-hh:mm:ss).
        //           Duration is computed HERE, not in the browser: both timestamps are server-side
        //           DateTime, so formatting once avoids every client re-parsing ISO strings and
        //           disagreeing about timezone. An alert with no reset is still OPEN -- it returns
        //           empty strings, and the view renders a dash rather than inventing a duration.

        // Version history (one line per shipped version, newest first):
        // 1.0.5.0 - VerdictDryRun sets TrySkipIisCustomErrors so JSON error bodies are
        //           not replaced by the IIS error page; names the missing config key
        // 1.0.4.0 - VerdictDryRun: encode non-JSON upstream bodies instead of splicing
        //           them raw (produced invalid JSON and hid the real failure)
        // 1.0.3.0 - VerdictDryRun uses HttpWebRequest instead of HttpClient: the
        //           System.Net.Http reference/binding-redirect can stop the app pool
        // 1.0.2.0 - VerdictDryRun: server-side proxy that calls the AnalyzeAlertJson
        //           service endpoint so a triggered alert can be dry-run against the
        //           exact contract the alert service will consume
        // 1.0.1.0 - GetLiveAlerts skips Test alerts (Validity) and Maintenance alerts (Remark)
        // 1.0.0.0 - Initial AlertAnalysis controller

        private readonly IFRSAlertService _frsAlertService;

        public AlertAnalysisController(IFRSAlertService frsAlertService)
        {
            _frsAlertService = frsAlertService;
        }

        // GET: FRS25/AlertAnalysis
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public JsonResult GetLiveAlerts(DateTime? fromDate, DateTime? toDate)
        {
            var rows = new List<object>();
            try
            {
                var lister = new Domain.FRSAlertLister();
                //lister.SearchCriteria.IsAcknowledgement = false;
                if (fromDate.HasValue) { lister.SearchCriteria.FromDate = fromDate.Value; }
                if (toDate.HasValue) { lister.SearchCriteria.ToDate = toDate.Value; }
                lister.Pager.Take = -1;
                lister = _frsAlertService.GetAcknowledgementAllAlert(lister);


                if (lister != null && lister.mFRSAlerts != null && lister.mFRSAlerts.Count > 0)
                {
                    var ids = lister.mFRSAlerts.Select(x => x.Id).ToList();
                    var mAlertAudits = new List<Domain.AlertAudit>();
                    if (lister.SearchCriteria.FromDate != null && lister.SearchCriteria.FromDate != DateTime.MinValue)
                    {
                        mAlertAudits = GetAlertAudit(lister.SearchCriteria.FromDate, lister.SearchCriteria.ToDate);
                    }
                    else
                    {
                        mAlertAudits = GetAlertAudit(ids);
                    }


                    if (mAlertAudits != null && mAlertAudits.Count > 0)
                    {
                        foreach (var frsAlert in lister.mFRSAlerts)
                        {
                            var alertAudit = mAlertAudits.Where(x => x.AlertId == frsAlert.Id && x.AlertDate.Date == frsAlert.SetTimeStamp.Date && x.AlertDate.Hour == frsAlert.SetTimeStamp.Hour && x.AlertDate.Minute == frsAlert.SetTimeStamp.Minute).FirstOrDefault();
                            if (alertAudit != null && alertAudit.Id > 0)
                            {
                                frsAlert.mAlertAudit = alertAudit;
                                frsAlert.IsTest = alertAudit.IsTest;

                                frsAlert.IsAlert = alertAudit.IsAlert;
                                if (alertAudit.IsTest)
                                {
                                    frsAlert.IsAlert = null;

                                }
                            }
                        }
                    }
                }

                if (lister != null && lister.mFRSAlerts != null)
                {
                    foreach (var a in lister.mFRSAlerts)
                    {
                        // Skip TEST alerts (from Validity) and MAINTENANCE alerts (from Remark)
                        // so they never reach the board or the AI analysis.

                        if (a.IsTest != null)
                        {
                            if (a.IsTest.Value || a.IsAlert == null)
                            { continue; }
                            else
                            {

                            }
                        }

                        string alertType = "";
                        if (a.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Failure)
                        {
                            alertType = "Failure";
                        }
                        else if (a.AlertTypeId == (int)E7FRSAdvance.Utility.Utility.AlertType.Predictive)
                        {
                            alertType = "Predictive";
                        }

                        // v1.0.6.0: the clear time. RectificationDateTime is null on live rows, so
                        // ResetTimeStamp is the field that actually carries it.
                        // v1.0.6.1: ResetTimeStamp is NULLABLE (DateTime?) -- an alert that has not
                        // cleared has no reset time at all, which is precisely the "still open" case
                        // this column exists to show. Read it through the nullable, never assign it
                        // straight to a DateTime.
                        DateTime resetTs = a.ResetTimeStamp.HasValue ? a.ResetTimeStamp.Value : DateTime.MinValue;
                        bool hasReset = a.ResetTimeStamp.HasValue
                                        && resetTs != DateTime.MinValue && a.SetTimeStamp != DateTime.MinValue
                                        && resetTs >= a.SetTimeStamp;
                        string durationText = "";
                        if (hasReset)
                        {
                            TimeSpan d = resetTs - a.SetTimeStamp;
                            durationText = ((int)d.TotalDays).ToString() + "-"
                                         + d.Hours.ToString("00") + ":"
                                         + d.Minutes.ToString("00") + ":"
                                         + d.Seconds.ToString("00");
                        }
                        rows.Add(new
                        {
                            Id = a.Id,
                            SiteId = a.SiteId,
                            SiteName = a.SiteName,
                            ZoneName = a.ZoneName ?? "",
                            DivisionName = a.DivisionName ?? "",
                            AlertType = alertType,
                            AssetName = a.AssetName,
                            AssetType = a.AssetType,
                            CauseCode = a.CauseCode,
                            TimeText = a.SetTimeStamp != DateTime.MinValue ? a.SetTimeStamp.ToString("dd/MM/yyyy HH:mm:ss") : "-",
                            TimeSort = a.SetTimeStamp != DateTime.MinValue ? a.SetTimeStamp.ToString("yyyy-MM-ddTHH:mm:ss") : "",
                            ResetText = hasReset ? resetTs.ToString("dd/MM/yyyy HH:mm:ss") : "",
                            DurationText = durationText,
                            // v1.0.6.2: the maintainer's own words. This is the only GROUND TRUTH in
                            // the system -- "Due to rain fall", "Checked and planning to replace RG
                            // LED", or a bare "true"/"false" confirming whether the alert was real.
                            // Nothing has ever read it back; surfacing it on the board is the first
                            // step to scoring verdicts against what the field actually found.
                            Remark = a.Remark ?? "",
                            MaintainerRemarks = a.MaintainerRemarks ?? ""
                        });
                    }
                }
            }
            catch (Exception)
            {
                rows = new List<object>();
            }

            var json = Json(rows);
            json.MaxJsonLength = int.MaxValue;
            return json;
        }

        // 1.0.6.3: current values for one asset, on demand. The AI popup's snapshot is taken at
        // ANALYSIS time; on an alert two days old that reading is two days stale and the reader had
        // no way to refresh it. Mirrors GetFRSAlertById's HttpClientFactory pattern -- same auth,
        // same error shape.
        [HttpPost]
        public JsonResult LiveValues(int assetId, int siteId, int alertId = 0, string runId = null)
        {
            var outRows = new List<object>();
            string stamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
            try
            {
                // 1.0.6.5: LiveValue lives on the EdgeX DataAPI, NOT the FRS API. 1.0.6.4 mirrored
                // GetFRSAlertById's HttpClientFactory -- which carries the FRS base
                // (APIBaseUrl, ...:90/api/) -- so the request went to a host that has no LiveValue
                // route at all. It 404'd, the catch swallowed it, and the card reported
                // "No readings returned" on every click. Copying a pattern copied its BASE URL too.
                string apiBase = (ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0)
                { apiBase = (ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0)
                { AiLogLive(runId, alertId, "LiveValues: no DataApiBaseUrl/ProxyBaseUrl configured"); return Json(new { retrievedAt = stamp, values = outRows, error = "not configured" }); }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }
                string url = apiBase + "api/LiveValue/" + assetId.ToString();
                {
                    string jsonString = "";
                    HttpStatusCode code = HttpStatusCode.OK;
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 8000;
                    req.Accept = "application/json";
                    // the DataAPI is https with an internal certificate on some hosts
                    System.Net.ServicePointManager.SecurityProtocol =
                        System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
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
                            for (int i = 0; i < arr.Count && outRows.Count < 16; i++)
                            {
                                var r = arr[i];
                                if (r == null) { continue; }
                                // 1.0.6.4: LiveValue returns Value / AssetAttributeId / TagUnit /
                                // TimestampDevice. It does NOT return CurrentValue,
                                // AssetAttributeName, MinValue or MaxValue -- parsing those made
                                // every row fail and the grid come back empty on every click.
                                double val;
                                string raw = r["Value"] != null ? r["Value"].ToString() : "";
                                if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                                double mn = 0, mx = 0; bool hasMn = false, hasMx = false;
                                if (r["MinValue"] != null && double.TryParse(r["MinValue"].ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out mn)) { hasMn = true; }
                                if (r["MaxValue"] != null && double.TryParse(r["MaxValue"].ToString(),
                                    System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out mx)) { hasMx = true; }
                                string state = "";
                                if (hasMn || hasMx)
                                {
                                    bool below = hasMn && val < mn, above = hasMx && val > mx;
                                    state = below ? "low" : (above ? "high" : "ok");
                                }
                                string at = "";
                                if (r["TimestampDevice"] != null)
                                {
                                    DateTime td;
                                    if (DateTime.TryParse(r["TimestampDevice"].ToString(),
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out td))
                                    { at = td.ToString("dd/MM HH:mm"); }
                                }
                                // Safe range is NOT in this response; the client already holds it
                                // from the analysis snapshot and merges it by attribute id.
                                outRows.Add(new
                                {
                                    attrId = r["AssetAttributeId"] != null ? r["AssetAttributeId"].ToString() : "",
                                    attr = r["AssetAttributeName"] != null
                                             ? r["AssetAttributeName"].ToString().Trim() : "",
                                    unit = r["TagUnit"] != null ? r["TagUnit"].ToString().Trim() : "",
                                    value = Math.Round(val, 3),
                                    minSafe = hasMn ? (object)mn : null,
                                    maxSafe = hasMx ? (object)mx : null,
                                    state = state,
                                    at = at
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 1.0.6.5: SAY SO. The 1.0.6.4 catch discarded the reason, so a 404 from the wrong
                // host looked identical to an asset with no tags -- which is why the real fault took
                // two rounds to find.
                AiLogLive(runId, alertId, "LiveValues failed for asset " + assetId + ": " + ex.GetType().Name + " " + ex.Message);
                outRows = new List<object>();
                return Json(new { retrievedAt = stamp, values = outRows, error = "fetch failed" });
            }
            AiLogLive(runId, alertId, "LiveValues asset " + assetId + " returned " + outRows.Count + " readings");
            return Json(new { retrievedAt = stamp, values = outRows });
        }

        public ActionResult GetFRSAlertById(int id)
        {
            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                mFRSAlert = GetFRSAlert(id);

            }
            catch (Exception)
            {
                mFRSAlert = new Domain.FRSAlert();
            }
            return Json(mFRSAlert);
        }

        private Domain.FRSAlert GetFRSAlert(int id)
        {

            var mFRSAlert = new Domain.FRSAlert();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var response = hcf.client.GetAsync(String.Format("FRSAlert/{0}", id)).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mFRSAlert = JsonConvert.DeserializeObject<Domain.FRSAlert>(jsonString);
                        if (mFRSAlert == null)
                            mFRSAlert = new Domain.FRSAlert();
                    }
                }
            }
            catch (Exception ex)
            {
                mFRSAlert = new Domain.FRSAlert();
            }

            return mFRSAlert;
        }

        // ------------------------------------------------------------------
        // POST /FRS25/AlertAnalysis/VerdictDryRun
        // Dry-run the SERVICE verdict contract against an already-triggered
        // alert. The browser cannot call AnalyzeAlertJson directly because the
        // shared key must never reach client script, so this action forwards
        // the request server-to-server with the key from config -- the same
        // call the alert service will make. The reply carries BOTH the request
        // payload and the verbatim envelope, so accuracy and contract can be
        // checked side by side.
        // Config: AnalyzeJsonUrl, InternalApiKey.
        // ------------------------------------------------------------------
        // NOTE: deliberately uses HttpWebRequest (System.dll) rather than
        // HttpClient. System.Net.Http needs an assembly reference and, on
        // .NET Framework web apps, an assemblyBinding redirect -- a missing
        // redirect stops the app pool at startup ("HTTP 503 The service is
        // unavailable"). HttpWebRequest has no such dependency.

        private static bool LooksLikeJson(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) { return false; }
            string x = t.TrimStart();
            return x.Length > 0 && (x[0] == '{' || x[0] == '[');
        }

        private static string LimitBody(string t)
        {
            if (t == null) { return ""; }
            t = t.Trim();
            return t.Length <= 400 ? t : t.Substring(0, 400) + " ...";
        }

        // minimal JSON string encoder -- no serializer dependency
        private static string JsonStr(string t)
        {
            if (t == null) { return "null"; }
            StringBuilder b = new StringBuilder(t.Length + 16);
            b.Append('"');
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '"') { b.Append("\\\""); }
                else if (c == '\\') { b.Append("\\\\"); }
                else if (c == '\n') { b.Append("\\n"); }
                else if (c == '\r') { b.Append("\\r"); }
                else if (c == '\t') { b.Append("\\t"); }
                else if (c < 32) { b.Append("\\u").Append(((int)c).ToString("x4")); }
                else { b.Append(c); }
            }
            b.Append('"');
            return b.ToString();
        }

        [HttpPost]
        public async Task<ActionResult> VerdictDryRun()
        {
            // Without this, IIS httpErrors (existingResponse="Replace") throws away
            // our JSON body on any non-2xx and substitutes its own page -- which is
            // how a plain "not_configured" message surfaced as the opaque IIS text
            // "The service is unavailable." and looked like a dead app pool.
            try { Response.TrySkipIisCustomErrors = true; } catch { }
            string ctxJson;
            using (StreamReader sr = new StreamReader(Request.InputStream, Encoding.UTF8))
            {
                ctxJson = await sr.ReadToEndAsync().ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(ctxJson))
            {
                Response.StatusCode = 400;
                return Content("{\"dryRun\":\"bad_request\",\"detail\":\"empty context\"}", "application/json");
            }

            string url = ConfigurationManager.AppSettings["AnalyzeJsonUrl"];
            string key = ConfigurationManager.AppSettings["InternalApiKey"];
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key))
            {
                string missing = string.IsNullOrEmpty(url)
                    ? (string.IsNullOrEmpty(key) ? "AnalyzeJsonUrl and InternalApiKey are" : "AnalyzeJsonUrl is")
                    : "InternalApiKey is";
                Response.StatusCode = 503;
                return Content("{\"dryRun\":\"not_configured\",\"detail\":"
                    + JsonStr(missing + " missing from E7FRSAdvance web.config appSettings.")
                    + ",\"hint\":"
                    + JsonStr("Add AnalyzeJsonUrl (the AnalyzeAlertJson URL on E7FRSAdvance) and InternalApiKey (identical to the value on E7FRSAdvance), then retry.")
                    + "}", "application/json");
            }

            int timeoutSec;
            if (!int.TryParse(ConfigurationManager.AppSettings["VerdictDryRunTimeoutSec"], out timeoutSec) || timeoutSec <= 0)
            {
                timeoutSec = 180;
            }

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string body = null;
            int status = 0;
            string failNote = null;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Accept = "application/json";
                req.Timeout = timeoutSec * 1000;
                req.ReadWriteTimeout = timeoutSec * 1000;
                req.Headers.Add("X-Internal-Api-Key", key);
                req.Headers.Add("X-Request-Id", "dryrun-" + Guid.NewGuid().ToString("N").Substring(0, 12));

                byte[] payload = Encoding.UTF8.GetBytes(ctxJson);
                req.ContentLength = payload.Length;
                using (Stream rs = await req.GetRequestStreamAsync().ConfigureAwait(false))
                {
                    await rs.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                }

                using (HttpWebResponse res = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                using (StreamReader rd = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)res.StatusCode;
                    body = await rd.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException wex)
            {
                // a non-2xx still carries the envelope we want to show
                HttpWebResponse er = wex.Response as HttpWebResponse;
                if (er != null)
                {
                    status = (int)er.StatusCode;
                    try
                    {
                        using (StreamReader rd = new StreamReader(er.GetResponseStream(), Encoding.UTF8))
                        {
                            body = rd.ReadToEnd();
                        }
                    }
                    catch { body = null; }
                    er.Close();
                }
                else
                {
                    failNote = wex.Status.ToString();
                }
                System.Diagnostics.Trace.TraceError("[VerdictDryRun] " + wex.ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                System.Diagnostics.Trace.TraceError("[VerdictDryRun] " + ex.ToString());
                Response.StatusCode = 502;
                return Content("{\"dryRun\":\"unreachable\",\"detail\":\"verdict endpoint could not be reached\"}", "application/json");
            }
            if (status == 0)
            {
                sw.Stop();
                Response.StatusCode = 502;
                return Content("{\"dryRun\":\"unreachable\",\"detail\":\"verdict endpoint could not be reached ("
                    + (failNote ?? "no response") + ")\"}", "application/json");
            }
            sw.Stop();

            // The upstream body is only JSON when the verdict endpoint itself
            // answered. An infrastructure reply (IIS "The service is
            // unavailable.", an HTML error page, a proxy notice) is NOT JSON and
            // must be encoded as a string -- splicing it raw produced invalid
            // JSON, which the browser could not parse, hiding the real cause.
            bool upstreamJson = LooksLikeJson(body);
            bool upstreamOk = status >= 200 && status < 300;
            string state = (upstreamOk && upstreamJson) ? "ok" : "upstream_error";

            StringBuilder outp = new StringBuilder();
            outp.Append("{\"dryRun\":\"").Append(state).Append("\"");
            outp.Append(",\"httpStatus\":").Append(status);
            outp.Append(",\"roundTripMs\":").Append(sw.ElapsedMilliseconds);
            if (!upstreamJson)
            {
                outp.Append(",\"detail\":").Append(JsonStr(
                    "The verdict endpoint did not return JSON (HTTP " + status +
                    "). This is an infrastructure reply, not a verdict."));
                outp.Append(",\"hint\":").Append(JsonStr(
                    status == 503
                        ? "HTTP 503 with this body is IIS on the E7FRSAdvance side: its application pool is stopped or failing to start. Start that pool and browse the AnalyzeAlertJson URL directly."
                        : "Check AnalyzeJsonUrl points at the AnalyzeAlertJson action on E7FRSAdvance and that the app is running."));
                outp.Append(",\"rawBody\":").Append(JsonStr(LimitBody(body)));
            }
            outp.Append(",\"request\":").Append(ctxJson);
            outp.Append(",\"response\":").Append(upstreamJson ? body : "null");
            outp.Append("}");
            return Content(outp.ToString(), "application/json");
        }

        public List<Domain.AlertAudit> GetAlertAudit(List<int> ids)
        {
            var mAlertAudits = new List<Domain.AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {

                    var jsonStr = JsonConvert.SerializeObject(ids);
                    StringContent str = new StringContent(jsonStr, Encoding.UTF8, "application/json");
                    var response = hcf.client.PostAsync(String.Format("AlertAudit/GetByAlertId"), str).Result;
                    string jsonString = response.Content.ReadAsStringAsync().Result;
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<Domain.AlertAudit>>(jsonString);
                    }
                }
            }
            catch (Exception)
            {
                mAlertAudits = new List<Domain.AlertAudit>();
            }
            return mAlertAudits;
        }


        // =====================================================================================
        // v1.0.55.164 -- ADD THESE TWO ACTIONS TO AlertAnalysisController.
        // Index.cshtml 1.0.55.164 calls both; without RateAnalysis the Rate button fails with HTTP
        // 404 and the overlay will (correctly) show NOT SAVED, and without GetAnalysisRating the
        // overlay stays in its UNKNOWN state and never offers the form at all.
        //
        // WHY A PROXY AND NOT A DIRECT CALL FROM THE BROWSER
        //   http://proxy.energy7.org:8083 is plain http on an internal name. An https page cannot
        //   call it (mixed content) and a client machine cannot resolve it. This is exactly the
        //   wall .157 hit with PM Analysis, fixed the same way -- same-origin action, server does
        //   the outbound call.
        //
        // WHY IDENTITY IS STAMPED HERE
        //   If UserName/UserRole came from the browser, any operator could file a rating under
        //   another name. LoginUser is available synchronously in a plain MVC action, so the
        //   server decides who is rating. The view sends only analysisId / rating / comment.
        //
        // NOT IN AiChatController ON PURPOSE -- .101 moved this out of the AI flow: it is a UI
        // action, not part of the analysis.
        //
        // v1.0.55.169 -- THIS ACTION IS ALSO THE EDIT PATH. The upstream is an Upsert keyed on
        //   AnalysisId + the UserName stamped below, so a second post from the same operator
        //   UPDATES their row rather than adding one. That is what makes the overlay's Edit
        //   button safe, and it is also why identity must keep being stamped here: if the
        //   browser supplied UserName, an operator could overwrite somebody else's rating by
        //   sending their name.
        //
        // Required usings (add whichever are missing at the top of the controller):
        //   using System; using System.Linq; using System.Net.Http; using System.Text;
        //   using System.Threading.Tasks; using System.Web.Mvc; using Newtonsoft.Json.Linq;
        // =====================================================================================

        // One HttpClient for the app. A new HttpClient per request exhausts sockets under load
        // (TIME_WAIT), which shows up as intermittent rating failures long before anything else.
        private static readonly HttpClient _ratingHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        //private const string RATING_UPSERT_URL = "http://proxy.energy7.org:8083/api/AIRating/Upsert";
        // private const string RATING_GET_URL = "http://proxy.energy7.org:8083/api/AIRating/Get?AnalysisId=";

        // v1.0.55.169: the remark limit the SERVER enforces. The textarea carries the same number
        // as maxlength, but maxlength is a convenience for the operator, not a control -- this
        // action is reachable without the page. Rejecting is deliberate: silently truncating a
        // remark would file words the operator never approved and never told them it happened.
        // This is the limit THIS action enforces; if the rating service's Comment column is
        // narrower, the upstream still decides what it stores.
        private const int RATING_COMMENT_MAX = 2000;

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> RateAnalysis(long analysisId, int rating, string comment)
        {
            string RATING_UPSERT_URL = $"{ConfigurationManager.AppSettings["ProxyBaseUrl"]}/api/AIRating/Upsert";
            if (analysisId <= 0 || rating < 1 || rating > 5)
            {
                return Json(new { ok = false, error = "bad request" });
            }

            string trimmedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            if (trimmedComment != null && trimmedComment.Length > RATING_COMMENT_MAX)
            {
                return Json(new
                {
                    ok = false,
                    error = "remark is " + trimmedComment.Length + " characters -- the limit is " + RATING_COMMENT_MAX
                });
            }

            var loginUser = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            if (loginUser == null)
            {
                return Json(new { ok = false, error = "your session has expired -- sign in again" });
            }

            string userName = ((loginUser.FirstName ?? "") + " " + (loginUser.LastName ?? "")).Trim();
            if (string.IsNullOrEmpty(userName))
            {
                userName = "Operator";
            }

            // UserClassId -> enum NAME. Cast on the raw id so an id outside the enum still yields
            // its number rather than throwing; a rating from an unmapped role is worth more than
            // a 500.
            string userRole;
            try
            {
                userRole = ((E7FRSAdvance.Utility.Utility.UserClass)loginUser.UserClassId).ToString();
            }
            catch
            {
                userRole = Convert.ToString(loginUser.UserClassId);
            }

            var body = new JObject();
            body["AnalysisId"] = analysisId;
            body["UserName"] = userName;
            body["UserRole"] = userRole;
            body["Rating"] = rating;
            body["Comment"] = trimmedComment;

            try
            {
                using (var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"))
                using (var resp = await _ratingHttp.PostAsync(RATING_UPSERT_URL, content))
                {
                    string text = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        // The upstream body is passed back trimmed so a rejected duplicate ("already
                        // rated") reaches the operator as words instead of a bare status code.
                        return Json(new
                        {
                            ok = false,
                            error = "rating service returned HTTP " + (int)resp.StatusCode
                                    + (string.IsNullOrWhiteSpace(text) ? "" : (": " + text.Trim().Substring(0, Math.Min(200, text.Trim().Length))))
                        });
                    }

                    return Json(new { ok = true, userName = userName, userRole = userRole });
                }
            }
            catch (TaskCanceledException)
            {
                return Json(new { ok = false, error = "rating service did not respond within 20s" });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = "could not reach the rating service: " + ex.Message });
            }
        }


        // =====================================================================================
        // v1.0.55.164 -- READ BACK AN EXISTING RATING.
        // The upstream returns an ARRAY, empty when nothing is rated. The row is picked HERE so
        // the view never has to decide which of several is "the" rating: the caller's own row
        // wins, otherwise the most recently touched one. Returning the array raw would push that
        // choice into JavaScript, where it would be made differently by the next person to touch it.
        //
        // ok=false is NOT "not rated". The view keeps those apart on purpose -- an unreachable
        // service must not be rendered as an empty star row, because the next submit would
        // overwrite the existing rating through Upsert.
        // =====================================================================================
        [HttpGet]
        public async Task<ActionResult> GetAnalysisRating(long analysisId)
        {
            string RATING_GET_URL = $"{ConfigurationManager.AppSettings["ProxyBaseUrl"]}/api/AIRating/Get?AnalysisId=";

            if (analysisId <= 0)
            {
                return Json(new { ok = false, error = "bad request" }, JsonRequestBehavior.AllowGet);
            }

            var loginUser = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            string me = loginUser == null
                ? ""
                : ((loginUser.FirstName ?? "") + " " + (loginUser.LastName ?? "")).Trim();

            try
            {
                using (var resp = await _ratingHttp.GetAsync(RATING_GET_URL + analysisId))
                {
                    string text = await resp.Content.ReadAsStringAsync();

                    if (!resp.IsSuccessStatusCode)
                    {
                        return Json(new { ok = false, error = "rating service returned HTTP " + (int)resp.StatusCode },
                                    JsonRequestBehavior.AllowGet);
                    }

                    JArray rows;
                    try
                    {
                        rows = string.IsNullOrWhiteSpace(text) ? new JArray() : JArray.Parse(text);
                    }
                    catch
                    {
                        // A 200 carrying something that is not the documented array is a broken contract,
                        // not an empty result. Say so rather than reporting "not rated".
                        return Json(new { ok = false, error = "rating service returned an unexpected body" },
                                    JsonRequestBehavior.AllowGet);
                    }

                    if (rows.Count == 0)
                    {
                        return Json(new { ok = true, rated = false }, JsonRequestBehavior.AllowGet);
                    }

                    JObject pick = null;
                    DateTime pickAt = DateTime.MinValue;

                    foreach (JObject row in rows.OfType<JObject>())
                    {
                        string who = (string)row["UserName"] ?? "";

                        // The caller's own rating wins outright, whatever its timestamp.
                        if (!string.IsNullOrEmpty(me) && string.Equals(who, me, StringComparison.OrdinalIgnoreCase))
                        {
                            pick = row;
                            break;
                        }

                        DateTime at;
                        if (!DateTime.TryParse((string)row["UpdatedAtUtc"], out at) &&
                            !DateTime.TryParse((string)row["RatedAtUtc"], out at))
                        {
                            at = DateTime.MinValue;
                        }

                        if (pick == null || at > pickAt)
                        {
                            pick = row;
                            pickAt = at;
                        }
                    }

                    int ratingVal = 0;
                    if (pick["Rating"] != null)
                    {
                        int.TryParse(pick["Rating"].ToString(), out ratingVal);
                    }

                    string byName = (string)pick["UserName"] ?? "Operator";

                    return Json(new
                    {
                        ok = true,
                        rated = true,
                        rating = ratingVal,
                        comment = (string)pick["Comment"],
                        by = byName,
                        role = (string)pick["UserRole"],
                        at = (string)(pick["UpdatedAtUtc"] ?? pick["RatedAtUtc"]),
                        mine = (!string.IsNullOrEmpty(me) && string.Equals(byName, me, StringComparison.OrdinalIgnoreCase)),
                        count = rows.Count
                    }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (TaskCanceledException)
            {
                return Json(new { ok = false, error = "rating service did not respond within 20s" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = "could not reach the rating service: " + ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        private List<AlertAudit> GetAlertAudit(DateTime FromDate, DateTime ToDate)
        {
            List<AlertAudit> mAlertAudits = new List<AlertAudit>();
            try
            {
                using (var hcf = new HttpClientFactory(token: ClsHttpContent.LoginUser.Token))
                {
                    var fromDate = FromDate.ToShortDateString().Replace("/", "-");
                    var toDate = ToDate.ToShortDateString().Replace("/", "-");

                    var response = hcf.client.GetAsync($"AlertAudit/GetByAlertDate/StartDate/{fromDate}/EndDate/{toDate}").Result;
                    string jsonStr = response.Content.ReadAsStringAsync().Result;

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        mAlertAudits = JsonConvert.DeserializeObject<List<AlertAudit>>(jsonStr);
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return mAlertAudits;
        }

        // =====================================================================================
        // AlertAnalysisController 1.0.6.7 -- ALERT BOOKMARKS.
        // Paste these two actions (and the helper) into AlertAnalysisController, and bump:
        //     public const string ComponentVersion = "1.0.6.7";
        //     // 1.0.6.7 - GetMyBookmarks / ToggleBookmark: same-origin proxy to the AlertBookmark API.
        //     //           The user is stamped HERE from LoginUser -- the browser sends only alertId,
        //     //           siteId and the target state, so nobody can bookmark (or un-bookmark) as
        //     //           someone else. Target state is explicit, not "flip", so a retry is idempotent.
        //
        // Config (web.config appSettings): BookmarkApiBaseUrl, else falls back to ProxyBaseUrl --
        // the same host RateAnalysis already uses.
        //
        // !! CONFIRM: loginUser.Id below must be your LoginUser's numeric user-id property
        //    (rename if it is UserId / UserMasterId etc.).
        // =====================================================================================

        private static string BookmarkApiBase()
        {
            string b = (ConfigurationManager.AppSettings["APIBaseUrl"] ?? "").Trim();
            if (b.Length == 0) { b = (ConfigurationManager.AppSettings["APIBaseUrl"] ?? "").Trim(); }
            return b.TrimEnd('/');
        }

        // GET: returns the CURRENT USER's bookmarked alert ids.
        // ok=false is NOT "nothing bookmarked" -- the view keeps the buttons in an UNKNOWN state then.
        [HttpGet]
        public async Task<ActionResult> GetMyBookmarks()
        {
            var loginUser = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            if (loginUser == null)
            {
                return Json(new { ok = false, error = "session expired" }, JsonRequestBehavior.AllowGet);
            }
            string apiBase = BookmarkApiBase();
            if (apiBase.Length == 0)
            {
                return Json(new { ok = false, error = "BookmarkApiBaseUrl/ProxyBaseUrl not configured" }, JsonRequestBehavior.AllowGet);
            }

            try
            {
                string url = apiBase + "/AlertBookmark/GetByUser?UserId=" + loginUser.Id;
                using (var resp = await _ratingHttp.GetAsync(url))
                {
                    string text = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        return Json(new { ok = false, error = "bookmark service returned HTTP " + (int)resp.StatusCode },
                                    JsonRequestBehavior.AllowGet);
                    }

                    JArray rows;
                    try { rows = string.IsNullOrWhiteSpace(text) ? new JArray() : JArray.Parse(text); }
                    catch
                    {
                        return Json(new { ok = false, error = "bookmark service returned an unexpected body" },
                                    JsonRequestBehavior.AllowGet);
                    }

                    var ids = new List<int>();
                    foreach (JObject r in rows.OfType<JObject>())
                    {
                        int aid;
                        if (r["AlertId"] != null && int.TryParse(r["AlertId"].ToString(), out aid) && aid > 0)
                        { ids.Add(aid); }
                    }
                    return Json(new { ok = true, ids = ids.Distinct().ToList() }, JsonRequestBehavior.AllowGet);
                }
            }
            catch (TaskCanceledException)
            {
                return Json(new { ok = false, error = "bookmark service did not respond within 20s" }, JsonRequestBehavior.AllowGet);
            }
            catch (Exception ex)
            {
                AiLogLive(null, 0, "GetMyBookmarks failed: " + ex.GetType().Name + " " + ex.Message);
                return Json(new { ok = false, error = "could not reach the bookmark service" }, JsonRequestBehavior.AllowGet);
            }
        }

        // POST: set the bookmark for one alert to the EXPLICIT state the operator sees.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ToggleBookmark(int alertId, int siteId, bool bookmarked)
        {
            if (alertId <= 0)
            {
                return Json(new { ok = false, error = "bad request" });
            }
            var loginUser = E7FRSAdvance.Utility.ClsHttpContent.LoginUser;
            if (loginUser == null)
            {
                return Json(new { ok = false, error = "your session has expired -- sign in again" });
            }
            string apiBase = BookmarkApiBase();
            if (apiBase.Length == 0)
            {
                return Json(new { ok = false, error = "bookmark service not configured" });
            }

            string userName = ((loginUser.FirstName ?? "") + " " + (loginUser.LastName ?? "")).Trim();
            if (string.IsNullOrEmpty(userName)) { userName = "Operator"; }

            var body = new JObject();
            body["AlertId"] = alertId;
            body["SiteId"] = siteId;
            body["UserId"] = loginUser.Id;          // stamped server-side, never from the browser
            body["UserName"] = userName;
            body["IsBookmarked"] = bookmarked;

            try
            {
                using (var content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json"))
                using (var resp = await _ratingHttp.PostAsync(apiBase + "/AlertBookmark/Upsert", content))
                {
                    string text = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        string t = (text ?? "").Trim();
                        return Json(new
                        {
                            ok = false,
                            error = "bookmark service returned HTTP " + (int)resp.StatusCode
                                    + (t.Length == 0 ? "" : (": " + t.Substring(0, Math.Min(200, t.Length))))
                        });
                    }
                    return Json(new { ok = true, alertId = alertId, bookmarked = bookmarked });
                }
            }
            catch (TaskCanceledException)
            {
                return Json(new { ok = false, error = "bookmark service did not respond within 20s" });
            }
            catch (Exception ex)
            {
                AiLogLive(null, alertId, "ToggleBookmark failed: " + ex.GetType().Name + " " + ex.Message);
                return Json(new { ok = false, error = "could not reach the bookmark service" });
            }
        }
    }
}