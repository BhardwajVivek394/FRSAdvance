using Domain;
using E7FRSAdvance.Utility;
using E7FRSAdvance.Interface;
using Newtonsoft.Json.Linq;
using System;
using E7MRIWeb.Areas.FRS25.Controllers;   // Domain AI types (CauseCatalog, RdpmsMaps, AI clients) keep the 616 namespace
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Mvc;
using E7.AiCore;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // =====================================================================================
    //  1.6.0.0 -- MERGED FROM ChatBotNewController.FastPlan.cs (v1.3.0.0) so ChatBot carries the
    //  fast maintenance plan and ChatBotNew can be retired. Plan logic is unchanged; helper calls
    //  now go through the shared ChatEngine (ReadInt/ReadBool/LastRealUserText/CallModelAsync/
    //  ScrubPlatformInternals/MaskModelIdentity) instead of the ChatBotNew controller copies.
    //
    //  v1.3.0.0 -- FAST MAINTENANCE PLAN
    //
    //  A maintenance-plan question ("sakhun pe 4 ghante hain, kya karna chahiye") no longer
    //  runs the agentic loop. E7.AiCore builds the whole station plan in one call
    //  (StationPlanBuilder); this file only supplies the data sources, sends the plan to the
    //  page as one SSE "plan" event, then streams a short AI explanation written WITHOUT tools.
    //
    //  Off by default:   <add key="ChatBotFastPlan" value="true" />
    //  Any failure falls back to the normal agentic loop (a "plan_error" event is sent first).
    //
    //  The usings above mirror MaintenceRosterController, which already compiles against the
    //  same services (ISiteService, IAssetService, IFRSAlertService, HttpClientFactory).
    // =====================================================================================
    public partial class ChatBotController
    {
        private static readonly bool fastPlanEnabled = ChatEngine.ReadBool("ChatBotFastPlan", false);

        private static readonly HttpClient historianHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        private static readonly Regex planMarker = new Regex(@"\[\[plan:(\d+):", RegexOptions.Compiled);

        // Captured in Chat() BEFORE the first await: LoginUser and the resolver need the
        // request context, which is gone after ConfigureAwait(false).
        private bool fastPlanContextOk;
        private int fastPlanUserId;
        private int fastPlanRoleId;
        private string fastPlanToken;
        private ISiteService fastPlanSiteService;
        private IDivisionService fastPlanDivisionService;
        private IAssetService fastPlanAssetService;
        private IFRSAlertService fastPlanAlertService;

        // 1.6.0.1 -- FIX (was also broken in ChatBotNew 1.3.x): every service the plan reads
        // (DivisionService.GetAll, SiteService.GetBy, AssetService.GetAssestBy,
        // FRSAlertService.GetListerWithPagination, FilterCacheHelper) reads ClsHttpContent.LoginUser,
        // i.e. HttpContext.Current.Session. The plan runs those calls on pool threads (Task.Run, after
        // ConfigureAwait(false)) where HttpContext.Current is null, so they failed quietly, the site list
        // came back empty, no station matched, and every plan question fell through to the agentic loop.
        // The request is still open (the answer is streaming), so its context is captured here and put
        // back on the worker thread around each service call.
        private System.Web.HttpContext fastPlanHttpContext;

        private void CaptureFastPlanContext()
        {
            fastPlanContextOk = false;
            if (!fastPlanEnabled)
            {
                return;
            }

            try
            {
                var loginUser = ClsHttpContent.LoginUser;
                if (loginUser != null)
                {
                    fastPlanUserId = Convert.ToInt32(loginUser.Id);
                    fastPlanRoleId = Convert.ToInt32(loginUser.RoleId);
                    fastPlanToken = loginUser.Token;
                }

                fastPlanHttpContext = System.Web.HttpContext.Current;   // 1.6.0.1

                fastPlanSiteService = DependencyResolver.Current.GetService(typeof(ISiteService)) as ISiteService;
                fastPlanDivisionService = DependencyResolver.Current.GetService(typeof(IDivisionService)) as IDivisionService;
                fastPlanAssetService = DependencyResolver.Current.GetService(typeof(IAssetService)) as IAssetService;
                fastPlanAlertService = DependencyResolver.Current.GetService(typeof(IFRSAlertService)) as IFRSAlertService;

                fastPlanContextOk = fastPlanSiteService != null
                    && fastPlanDivisionService != null
                    && fastPlanAssetService != null
                    && fastPlanAlertService != null;

                if (!fastPlanContextOk)
                {
                    FastPlanLog("WARN", "services not resolvable -- fast plan disabled for this request");
                }
            }
            catch (Exception ex)
            {
                fastPlanContextOk = false;
                FastPlanLog("ERROR", "context capture failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Returns true when this request was answered by the fast plan path.
        private async Task<bool> TryRunFastPlanAsync(JArray messagesRaw)
        {
            if (!fastPlanEnabled || !fastPlanContextOk)
            {
                return false;
            }

            string question = ChatEngine.LastRealUserText(messagesRaw);
            if (string.IsNullOrWhiteSpace(question))
            {
                return false;
            }

            StationPlanOptions options = FastPlanOptions();
            FastPlanSource source = new FastPlanSource(
                fastPlanUserId,
                fastPlanRoleId,
                fastPlanToken,
                fastPlanSiteService,
                fastPlanDivisionService,
                fastPlanAssetService,
                fastPlanAlertService,
                fastPlanHttpContext);   // 1.6.0.1

            IList<PlanSiteInfo> sites;
            try
            {
                sites = await StationPlanBuilder.GetSitesCachedAsync(source, options, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FastPlanLog("ERROR", "site list failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }

            if (sites == null || sites.Count == 0)
            {
                FastPlanLog("WARN", "site list is empty -- no station can be matched, using the normal chat");   // 1.6.0.1
            }

            PlanIntent intent = PlanIntentParser.TryParse(question, sites, PreviousPlanSiteId(messagesRaw), ChatEngine.ReadInt("Plan.DefaultHours", 4));
            if (intent == null)
            {
                FastPlanLog("INFO", "not a plan question (or no station matched) -- normal chat; sites=" + (sites == null ? 0 : sites.Count));   // 1.6.0.1
                return false;
            }

            if (intent.AskForSite)
            {
                WriteSse("text", new { text = "Which station is the plan for? For example: Maintenance plan for SAKHUN, 4 hours." });
                return true;
            }

            WriteSse("tool_use", new { name = "Building station plan" });

            StationPlan plan;
            try
            {
                Task<StationPlan> build = StationPlanBuilder.BuildAsync(intent, source, options, CancellationToken.None);
                Task winner = await Task.WhenAny(build, Task.Delay(options.BuildTimeoutMs + 1500)).ConfigureAwait(false);
                if (winner != build)
                {
                    throw new TimeoutException("plan build exceeded " + (options.BuildTimeoutMs + 1500) + " ms");
                }

                plan = await build.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FastPlanLog("ERROR", "build failed site=" + intent.Site.SiteId + " " + ex.GetType().Name + " " + ex.Message);
                WriteSse("plan_error", new { message = "The quick plan could not be built, so the assistant is answering the usual way." });
                return false;
            }

            WriteSse("plan", new { plan = StationPlanJson.ToJObject(plan) });
            FastPlanLog("INFO", "plan site=" + plan.Site.Id + " cards=" + plan.Assets.Count
                + " buildMs=" + plan.Source.BuildMs
                + " alertsRead=" + plan.Source.AlertsRead
                + (plan.Source.Partial.Count > 0 ? " partial=" + string.Join(" | ", plan.Source.Partial) : ""));

            await WriteFastPlanSummaryAsync(plan, intent).ConfigureAwait(false);
            return true;
        }

        private async Task WriteFastPlanSummaryAsync(StationPlan plan, PlanIntent intent)
        {
            // The marker lets a follow-up ("aur 3T ke baare mein batao") resolve to the same
            // station. The page strips it from display.
            string marker = "[[plan:" + plan.Site.Id.ToString(CultureInfo.InvariantCulture) + ":"
                + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "]]\n";
            WriteSse("text", new { text = marker });

            if (plan.Assets.Count == 0)
            {
                WriteSse("text", new { text = "Nothing at this station needs attention today. All assets read healthy in the last 15 days." });
                return;
            }

            DateTime started = DateTime.UtcNow;
            string text = null;
            try
            {
                object[] messages = { new { role = "user", content = PlanSummaryPrompt.BuildDigest(plan) } };
                string raw = await Engine.CallModelAsync(messages, null, PlanSummaryPrompt.BuildSystemPrompt(intent.Language)).ConfigureAwait(false);
                JObject resp = JObject.Parse(raw);
                JArray content = resp["content"] as JArray;
                StringBuilder sb = new StringBuilder();
                if (content != null)
                {
                    foreach (JToken block in content)
                    {
                        if (block["type"] != null && block["type"].ToString() == "text" && block["text"] != null)
                        {
                            sb.Append(block["text"].ToString());
                        }
                    }
                }

                text = PlanSummaryPrompt.Sanitize(sb.ToString(), plan);
            }
            catch (Exception ex)
            {
                FastPlanLog("WARN", "summary failed: " + ex.GetType().Name + " " + ex.Message);
                text = null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                WriteSse("text", new { text = "The plan is ready. The short explanation could not be written right now." });
                return;
            }

            text = ChatEngine.MaskModelIdentity(ChatEngine.ScrubPlatformInternals(text));
            string[] words = text.Split(' ');
            StringBuilder chunk = new StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (chunk.Length > 0)
                {
                    chunk.Append(' ');
                }

                chunk.Append(words[i]);
                if (chunk.Length > 60 || i == words.Length - 1)
                {
                    WriteSse("text", new { text = chunk.ToString() });
                    chunk.Clear();
                }
            }

            FastPlanLog("INFO", "summary site=" + plan.Site.Id + " aiMs=" + (long)(DateTime.UtcNow - started).TotalMilliseconds);
        }

        private static int PreviousPlanSiteId(JArray messages)
        {
            if (messages == null)
            {
                return 0;
            }

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                JObject m = messages[i] as JObject;
                if (m == null || m["role"] == null || m["role"].ToString() != "assistant" || m["content"] == null)
                {
                    continue;
                }

                Match found = planMarker.Match(m["content"].ToString());
                int siteId;
                if (found.Success && int.TryParse(found.Groups[1].Value, out siteId))
                {
                    return siteId;
                }
            }

            return 0;
        }

        private static StationPlanOptions FastPlanOptions()
        {
            StationPlanOptions o = new StationPlanOptions();
            o.Now = DateTime.Now;
            o.WindowDays = ChatEngine.ReadInt("Plan.WindowDays", 15);
            o.CacheSeconds = ChatEngine.ReadInt("Plan.CacheSeconds", 300);
            o.AlertCacheSeconds = ChatEngine.ReadInt("Plan.AlertCacheSeconds", 60);
            o.LiveTimeoutMs = ChatEngine.ReadInt("Plan.LiveTimeoutMs", 4000);
            o.BuildTimeoutMs = ChatEngine.ReadInt("Plan.BuildTimeoutMs", 10000);
            o.MaxParallel = ChatEngine.ReadInt("Plan.MaxParallel", 6);
            o.UrgentCount = ChatEngine.ReadInt("Plan.UrgentCount", 5);
            o.SoonCount = ChatEngine.ReadInt("Plan.SoonCount", 3);
            o.PmUrgentShare = FastPlanReadDouble("Plan.PmUrgentShare", 0.5);
            o.PmSoonShare = FastPlanReadDouble("Plan.PmSoonShare", 0.1);
            o.SetupMinutes = ChatEngine.ReadInt("Plan.SetupMinutes", 15);
            o.MaxCards = ChatEngine.ReadInt("Plan.MaxMonitorCards", 12);
            return o;
        }

        private static double FastPlanReadDouble(string key, double def)
        {
            double v;
            string raw = System.Configuration.ConfigurationManager.AppSettings[key];
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }

            return def;
        }

        private static void FastPlanLog(string level, string message)
        {
            try
            {
                System.Diagnostics.Trace.TraceInformation("[ChatBot FastPlan " + ComponentVersion + "] " + level + " " + message);
            }
            catch
            {
            }

            // 1.6.0.1: Trace output is only visible under the debugger, so a plan that silently fell back
            // left no trace on a server. Also write it to App_Data\AiToolLogs\ai-log-yyyyMMdd.txt.
            try
            {
                E7.AiCore.AnalysisEngine.AiLogStatic(level, "FASTPLAN", message);
            }
            catch
            {
            }
        }

        // =================================================================================
        //  Data sources for E7.AiCore -- same endpoints and filters the Maintenance Roster uses
        // =================================================================================
        private sealed class FastPlanSource : IStationPlanSource
        {
            private readonly int userId;
            private readonly int roleId;
            private readonly string token;
            private readonly ISiteService siteService;
            private readonly IDivisionService divisionService;
            private readonly IAssetService assetService;
            private readonly IFRSAlertService alertService;
            private readonly System.Web.HttpContext requestContext;   // 1.6.0.1

            public FastPlanSource(int userId, int roleId, string token, ISiteService siteService,
                IDivisionService divisionService, IAssetService assetService, IFRSAlertService alertService,
                System.Web.HttpContext requestContext)
            {
                this.userId = userId;
                this.roleId = roleId;
                this.token = token;
                this.siteService = siteService;
                this.divisionService = divisionService;
                this.assetService = assetService;
                this.alertService = alertService;
                this.requestContext = requestContext;
            }

            // 1.6.0.1: Task.Run with the request's HttpContext put back on the worker thread, so the
            // services' ClsHttpContent.LoginUser (Session) reads work there. Restored afterwards so a
            // pool thread never keeps a finished request's context.
            private Task<T> RunInRequest<T>(Func<T> body, CancellationToken cancellationToken)
            {
                System.Web.HttpContext ctx = requestContext;
                return Task.Run<T>(() =>
                {
                    System.Web.HttpContext previous = System.Web.HttpContext.Current;
                    System.Web.HttpContext.Current = ctx;
                    try
                    {
                        return body();
                    }
                    finally
                    {
                        System.Web.HttpContext.Current = previous;
                    }
                }, cancellationToken);
            }

            public Task<IList<PlanSiteInfo>> GetSitesAsync(CancellationToken cancellationToken)
            {
                return RunInRequest<IList<PlanSiteInfo>>(() =>
                {
                    List<PlanSiteInfo> list = new List<PlanSiteInfo>();
                    object rawDivisions = divisionService.GetAll();
                    IEnumerable<Domain.Division> divisions = rawDivisions as IEnumerable<Domain.Division>;
                    if (divisions == null)
                    {
                        // 1.6.0.1: throw, do not return empty -- PlanCache keeps any non-null list for an
                        // hour, so an empty answer would switch the plan off for that long.
                        throw new InvalidOperationException("division list unavailable");
                    }

                    foreach (Domain.Division division in divisions)
                    {
                        if (division == null)
                        {
                            continue;
                        }

                        int divisionId = Convert.ToInt32(division.Id);
                        List<Domain.Site> sites = Helper.FilterCacheHelper.GetSitesByDivisionId(siteService, divisionId);
                        if (sites == null)
                        {
                            continue;
                        }

                        foreach (JObject s in JArray.FromObject(sites).OfType<JObject>())
                        {
                            int siteId = JInt(s, "SiteId", "Id");
                            if (siteId <= 0)
                            {
                                continue;
                            }

                            list.Add(new PlanSiteInfo
                            {
                                SiteId = siteId,
                                DivisionId = divisionId,
                                Name = JStr(s, "StationName", "SiteName", "Name") ?? ("Station " + siteId),
                                Code = JStr(s, "StationCode", "SiteCode", "Code", "ShortName", "ShortCode", "Abbreviation")
                            });
                        }
                    }

                    if (list.Count == 0)
                    {
                        throw new InvalidOperationException("no stations returned");   // 1.6.0.1: never cache an empty list
                    }

                    return list;
                }, cancellationToken);
            }

            public Task<IList<PlanAssetInfo>> GetAssetsAsync(int siteId, CancellationToken cancellationToken)
            {
                return RunInRequest<IList<PlanAssetInfo>>(() =>
                {
                    List<PlanAssetInfo> list = new List<PlanAssetInfo>();
                    object raw = assetService.GetAssestBy(siteId);
                    if (raw == null)
                    {
                        return list;
                    }

                    foreach (JObject a in JArray.FromObject(raw).OfType<JObject>())
                    {
                        int assetId = JInt(a, "AssetId", "Id");
                        if (assetId <= 0)
                        {
                            continue;
                        }

                        int typeId = JInt(a, "AssetTypeId", "AssetType");
                        string family = typeId > 0
                            ? PlanFamily.FromTypeId(typeId)
                            : PlanFamily.FromTypeName(JStr(a, "AssetFamily", "AssetTypeName", "AssetTypeTitle", "TypeName"));
                        list.Add(new PlanAssetInfo
                        {
                            AssetId = assetId,
                            Name = JStr(a, "AssetName", "Name") ?? ("Asset " + assetId),
                            TypeId = typeId,
                            Family = family
                        });
                    }

                    return list;
                }, cancellationToken);
            }

            public async Task<JToken> GetWorksheetAsync(int divisionId, int siteId, DateTime day, CancellationToken cancellationToken)
            {
                if (string.IsNullOrEmpty(token))
                {
                    throw new InvalidOperationException("not signed in");
                }

                // 1.6.0.1: HttpClientFactory's constructor reads HttpContext.Current (secure-connection
                // check, LoginUser headers); this runs after ConfigureAwait(false), so lend it the request.
                HttpClientFactory created;
                System.Web.HttpContext previous = System.Web.HttpContext.Current;
                System.Web.HttpContext.Current = requestContext;
                try
                {
                    created = new HttpClientFactory(token: token);
                }
                finally
                {
                    System.Web.HttpContext.Current = previous;
                }

                using (var hcf = created)
                {
                    hcf.client.DefaultRequestHeaders.Remove("UserId");
                    if (userId > 0)
                    {
                        hcf.client.DefaultRequestHeaders.Add("UserId", userId.ToString(CultureInfo.InvariantCulture));
                    }

                    string url = string.Format(CultureInfo.InvariantCulture, "Roster/Worksheet/Division/{0}/Site/{1}/Date/{2}",
                        divisionId, siteId, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    HttpResponseMessage response = await hcf.client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new InvalidOperationException("Roster API HTTP " + (int)response.StatusCode);
                    }

                    return string.IsNullOrWhiteSpace(body) ? null : JToken.Parse(body);
                }
            }

            // Same window read and exclusions as MaintenceRosterController.FetchWindowAlerts:
            // Test alerts and alerts acknowledged as Maintenance / Acknowledge do not drive a plan.
            public Task<IList<PlanAlertRow>> GetAlertsAsync(int divisionId, DateTime from, DateTime to, CancellationToken cancellationToken)
            {
                return RunInRequest<IList<PlanAlertRow>>(() =>
                {
                    Domain.FRSAlertLister lister = new Domain.FRSAlertLister();
                    if (lister.SearchCriteria == null)
                    {
                        lister.SearchCriteria = new Domain.FRSAlert();
                    }

                    if (lister.Pager == null)
                    {
                        lister.Pager = new Domain.Pager();
                    }

                    lister.SearchCriteria.DivisionId = divisionId;
                    lister.SearchCriteria.FromDate = from.Date;
                    lister.SearchCriteria.ToDate = to.Date.AddDays(1).AddSeconds(-1);
                    lister.SearchCriteria.UserId = userId;
                    lister.SearchCriteria.RoleId = roleId;
                    lister.Pager.Take = -1;

                    lister = alertService.GetListerWithPagination(lister);
                    List<PlanAlertRow> rows = new List<PlanAlertRow>();
                    if (lister == null || lister.mFRSAlerts == null)
                    {
                        return rows;
                    }

                    int maintId = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Maintenace /* 617 enum spelling, value 4 */;
                    int ackId = (int)E7FRSAdvance.Utility.Utility.AcknowledgemenStatus.Acknowledge;

                    foreach (Domain.FRSAlert a in lister.mFRSAlerts)
                    {
                        if (a == null || a.IsTest.Equals(true))
                        {
                            continue;
                        }

                        int statusId = 0;
                        try
                        {
                            statusId = Convert.ToInt32(a.AcknowledgemenStatusId);
                        }
                        catch
                        {
                            statusId = 0;
                        }

                        if (statusId == maintId || statusId == ackId)
                        {
                            continue;
                        }

                        DateTime reset;
                        bool hasReset = DateProp(a, "ResetTimeStamp", out reset);
                        rows.Add(new PlanAlertRow
                        {
                            AlertId = Convert.ToString(a.Id, CultureInfo.InvariantCulture),
                            AssetId = Convert.ToInt32((object)a.AssetId),
                            SiteId = Convert.ToInt32((object)a.SiteId),
                            AssetName = a.AssetName,
                            AssetTypeId = Convert.ToInt32((object)a.AssetTypeId),
                            AssetTypeName = a.AssetType,
                            Cause = a.CauseCode,
                            SetAt = a.SetTimeStamp,
                            ResetAt = hasReset && reset >= a.SetTimeStamp ? (DateTime?)reset : null
                        });
                    }

                    return rows;
                }, cancellationToken);
            }

            public Task<JObject> GetTrackModelAsync(int siteId, int assetId, CancellationToken cancellationToken)
            {
                return HistorianGetAsync("/api/asset/ai-prediction/track/" + siteId + "/" + assetId, cancellationToken);
            }

            public Task<JObject> GetPmModelAsync(int siteId, int assetId, CancellationToken cancellationToken)
            {
                return HistorianGetAsync("/api/asset/ai-prediction/pm-operation-v4/" + siteId + "/" + assetId, cancellationToken);
            }

            private static async Task<JObject> HistorianGetAsync(string path, CancellationToken cancellationToken)
            {
                string baseUrl = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiBaseUrl"] ?? "").Trim().TrimEnd('/');
                if (baseUrl.Length == 0)
                {
                    throw new InvalidOperationException("HistorianApiBaseUrl not configured");
                }

                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path))
                {
                    string auth = (System.Configuration.ConfigurationManager.AppSettings["HistorianApiAuth"] ?? "").Trim();
                    if (auth.Length > 0)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(auth)));
                    }

                    using (HttpResponseMessage response = await historianHttp.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new InvalidOperationException("Historian HTTP " + (int)response.StatusCode);
                        }

                        if (string.IsNullOrWhiteSpace(body))
                        {
                            return null;
                        }

                        return JToken.Parse(body) as JObject;
                    }
                }
            }

            private static bool DateProp(object o, string name, out DateTime value)
            {
                value = DateTime.MinValue;
                try
                {
                    var p = o.GetType().GetProperty(name);
                    if (p == null)
                    {
                        return false;
                    }

                    object v = p.GetValue(o, null);
                    if (v == null)
                    {
                        return false;
                    }

                    if (v is DateTime)
                    {
                        value = (DateTime)v;
                        return value != DateTime.MinValue;
                    }

                    return DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
                        && value != DateTime.MinValue;
                }
                catch
                {
                    return false;
                }
            }

            private static int JInt(JObject o, params string[] names)
            {
                foreach (string n in names)
                {
                    JToken t = o.Properties()
                        .Where(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.Value)
                        .FirstOrDefault();
                    int v;
                    if (t != null && t.Type != JTokenType.Null && int.TryParse(t.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    {
                        return v;
                    }
                }

                return 0;
            }

            private static string JStr(JObject o, params string[] names)
            {
                foreach (string n in names)
                {
                    JToken t = o.Properties()
                        .Where(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                        .Select(p => p.Value)
                        .FirstOrDefault();
                    if (t != null && t.Type != JTokenType.Null)
                    {
                        string s = t.ToString().Trim();
                        if (s.Length > 0)
                        {
                            return s;
                        }
                    }
                }

                return null;
            }
        }
    }
}
