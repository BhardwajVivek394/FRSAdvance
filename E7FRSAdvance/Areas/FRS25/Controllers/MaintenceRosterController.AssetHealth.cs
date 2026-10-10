using Domain;
using E7FRSAdvance.Areas.FRS25.Helper;   // RosterItemBuilder (616: Areas/FRS25/Helper)
using E7FRSAdvance.Interface;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Mvc;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // Asset Health API for the Telemetry Live asset drawer (GET /FRS25/MaintenceRoster/AssetHealth).
    // 616's roster controller has no AssetHealth action; this partial carries the 617 action, its data bridge and
    // the AssetHealthEngine that used to live in E7.AiCore (Roster/), now nested here so it uses
    // the roster's own helpers (RosLog, RosJInt, RosterModelCall ...) instead of E7.AiCore.RosterEngine.
    public partial class MaintenceRosterController
    {

        // =========================================================================================
        // 2.11.0.0: Asset Health API
        // GET /FRS25/MaintenceRoster/AssetHealth?assetId=1234[&siteId=..][&divisionId=..][&refresh=true]
        // AssetHealthEngine builds the whole response (computed score/status + AI findings);
        // this action only passes the request in and the response out. Login: class-level [Authenticate].
        // =========================================================================================
        [HttpGet]
        [OutputCache(NoStore = true, Duration = 0, VaryByParam = "*")]
        public ActionResult AssetHealth(int assetId = 0, int siteId = 0, int divisionId = 0, bool refresh = false)
        {
            AssetHealthEngine.Result result = AssetHealthEngine.Run(assetId, siteId, divisionId, refresh, new RosterAssetHealthHost(this));
            Response.TrySkipIisCustomErrors = true;
            Response.StatusCode = result.HttpStatus;
            return Content(result.Body.ToString(Newtonsoft.Json.Formatting.None), "application/json");
        }

        // Raw-data bridge for AssetHealthEngine. Every member calls the roster's existing fetcher -- no logic here.
        private sealed class RosterAssetHealthHost : IAssetHealthHost
        {
            private readonly MaintenceRosterController owner;

            public RosterAssetHealthHost(MaintenceRosterController owner)
            {
                this.owner = owner;
            }

            public List<int> DivisionIds()
            {
                List<int> ids = new List<int>();
                var all = owner._divisionService.GetAll();
                if (all == null)
                {
                    return ids;
                }

                foreach (var division in all)
                {
                    int id = IntProp(division, "Id", "DivisionId");
                    if (id > 0)
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }

            public List<int> SiteIds(int divisionId)
            {
                List<int> ids = new List<int>();
                List<Domain.Site> sites = Helper.FilterCacheHelper.GetSitesByDivisionId(owner._siteService, divisionId);
                if (sites == null)
                {
                    return ids;
                }

                foreach (Domain.Site site in sites)
                {
                    int id = IntProp(site, "Id", "SiteId");
                    if (id > 0)
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }

            public Newtonsoft.Json.Linq.JArray AssetsOfSite(int siteId)
            {
                object raw = owner._assetService.GetAssestBy(siteId);
                if (raw == null)
                {
                    return new Newtonsoft.Json.Linq.JArray();
                }

                return Newtonsoft.Json.Linq.JArray.FromObject(raw);
            }

            public List<Domain.FRSAlert> Alerts(int divisionId, DateTime from, DateTime to)
            {
                IFRSAlertService svc = DependencyResolver.Current.GetService(typeof(IFRSAlertService)) as IFRSAlertService;
                if (svc == null)
                {
                    RosLog("WARN", "AssetHealth: IFRSAlertService not resolvable");
                    return new List<Domain.FRSAlert>();
                }

                return RosterWindowAlerts(svc, from, to, divisionId, new HashSet<string>());
            }

            public string AckType(Domain.FRSAlert alert)
            {
                return RosterAckType(alert.AcknowledgemenStatusId);
            }

            public Newtonsoft.Json.Linq.JObject RangeHistory(int assetId, string start, string end)
            {
                return DataOf(owner.RangeHistory(assetId, start, end));
            }

            public Newtonsoft.Json.Linq.JObject TrackShort(int siteId, int assetId)
            {
                return DataOf(owner.TrackShort(siteId, assetId));
            }

            public Newtonsoft.Json.Linq.JObject PmOps(int siteId, int assetId, string start, string end)
            {
                return DataOf(owner.PmOps(siteId, assetId, start, end));
            }

            public Newtonsoft.Json.Linq.JObject Live(int assetId, int siteId)
            {
                return DataOf(owner.Live(assetId, siteId));
            }

            public double SafeNearPct
            {
                get
                {
                    return RosSafeNearPct;
                }
            }

            public double SafeOutsidePct
            {
                get
                {
                    return RosSafeOutsidePct;
                }
            }

            public double DriftSoonPct
            {
                get
                {
                    return RosDriftSoonPct;
                }
            }

            public double DriftUrgentPct
            {
                get
                {
                    return RosDriftUrgentPct;
                }
            }

            private static Newtonsoft.Json.Linq.JObject DataOf(JsonResult jr)
            {
                if (jr == null || jr.Data == null)
                {
                    return new Newtonsoft.Json.Linq.JObject();
                }

                return Newtonsoft.Json.Linq.JObject.FromObject(jr.Data);
            }

            // Read an int property by name (no full serialisation of entities with navigation properties).
            private static int IntProp(object o, params string[] names)
            {
                if (o == null)
                {
                    return 0;
                }

                foreach (string name in names)
                {
                    System.Reflection.PropertyInfo p = o.GetType().GetProperty(name);
                    if (p == null)
                    {
                        continue;
                    }

                    object v = p.GetValue(o, null);
                    if (v == null)
                    {
                        continue;
                    }

                    try
                    {
                        return Convert.ToInt32(v);
                    }
                    catch
                    {
                    }
                }

                return 0;
            }
        }

        // ---- from E7.AiCore/Providers/EngineIdentity.cs (MaskModelIdentity) ----
        private static readonly Regex _ahModelNameRx = new Regex(
            @"\b(chat\s?gpt|openai|gpt[-\s]?[0-9._]*|o[0-9](?:[-\s]?mini)?|"
          + @"claude(?:[-\s][a-z0-9._]+)*|anthropic|sonnet|opus|haiku|"
          + @"deepseek(?:[-\s][a-z0-9._]+)*|gemini|llama|mistral|qwen|grok)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string AhMaskModelIdentity(string text, string brand)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            if (!_ahModelNameRx.IsMatch(text)) { return text; }
            return _ahModelNameRx.Replace(text, brand);
        }

        // ---- from E7.AiCore/Roster/AssetHealthEngine.cs ----
    // ============================================================================================
    // Asset Health -- one health summary for one asset (E7.AiCore 1.0.169.0 / Roster 2.11.0.0)
    //
    // E7.AiCore owns the whole feature: asset lookup, which sources to read for the asset type,
    // alert filtering and banding, the computed score / status / headline, the AI context, the
    // prompt, the model call, the parse, the fallback and the response. The web project only
    // supplies RAW data through IAssetHealthHost, because those fetches need the logged-in user's
    // token and the web DI services (the same bridge pattern as AnalysisEngine + IAnalysisHost).
    //
    // Computation decides, AI explains: score, status and headline are computed from the
    // evidence with the roster's own rules; the AI only writes the findings, summary and action.
    // ============================================================================================

    /// <summary>
    /// Raw-data bridge implemented by the web project. Every member only returns data -- no logic.
    /// </summary>
    public interface IAssetHealthHost
    {
        List<int> DivisionIds();

        List<int> SiteIds(int divisionId);

        JArray AssetsOfSite(int siteId);

        List<FRSAlert> Alerts(int divisionId, DateTime from, DateTime to);

        string AckType(FRSAlert alert);

        JObject RangeHistory(int assetId, string start, string end);

        JObject TrackShort(int siteId, int assetId);

        JObject PmOps(int siteId, int assetId, string start, string end);

        JObject Live(int assetId, int siteId);

        double SafeNearPct { get; }

        double SafeOutsidePct { get; }

        double DriftSoonPct { get; }

        double DriftUrgentPct { get; }
    }

    public static class AssetHealthEngine
    {
        public const string ComponentVersion = "1.0.0.0";

        public const int DataDays = 2;
        public const int AlertDays = 15;
        public const int PmDays = 30;

        private const int CacheMinutes = 10;
        private const int IndexHours = 6;
        private const int MaxContextChars = 12000;

        private const int AssetTypeTrack = 1;
        private const int AssetTypeSignal = 2;
        private const int AssetTypePoint = 3;

        private static readonly ConcurrentDictionary<int, CacheEntry> cache = new ConcurrentDictionary<int, CacheEntry>();
        private static readonly ConcurrentDictionary<int, AssetRef> assetIndex = new ConcurrentDictionary<int, AssetRef>();
        private static readonly object scanLock = new object();

        private sealed class CacheEntry
        {
            public DateTime AtUtc;
            public JObject Body;
        }

        private sealed class AssetRef
        {
            public int AssetId;
            public int SiteId;
            public int DivisionId;
            public int TypeId;
            public string Name = "";
            public string TypeName = "";
            public string Station = "";
            public DateTime SeenUtc;
        }

        private sealed class Deduction
        {
            public int Points;
            public string Band = "";
            public string Text = "";
        }

        /// <summary>Response plus the HTTP status the controller should send.</summary>
        public sealed class Result
        {
            public int HttpStatus;
            public JObject Body;
        }

        // ----------------------------------------------------------------------------------------
        // Entry point
        // ----------------------------------------------------------------------------------------
        public static Result Run(int assetId, int siteId, int divisionId, bool refresh, IAssetHealthHost host)
        {
            if (assetId <= 0)
            {
                return Fail(400, "assetId required");
            }

            if (host == null)
            {
                return Fail(500, "health summary unavailable");
            }

            try
            {
                if (!refresh)
                {
                    CacheEntry hit;
                    if (cache.TryGetValue(assetId, out hit) && (DateTime.UtcNow - hit.AtUtc).TotalMinutes < CacheMinutes)
                    {
                        JObject copy = (JObject)hit.Body.DeepClone();
                        copy["cached"] = true;
                        RosLog("INFO", "AssetHealth asset=" + assetId + " served from cache");
                        Result cached = new Result();
                        cached.HttpStatus = 200;
                        cached.Body = copy;
                        return cached;
                    }
                }

                AssetRef asset = ResolveAsset(assetId, siteId, divisionId, host);
                if (asset == null)
                {
                    RosLog("WARN", "AssetHealth asset=" + assetId + " not found (site=" + siteId + " division=" + divisionId + ")");
                    return Fail(404, "asset not found");
                }

                JObject body = Build(asset, host);

                CacheEntry entry = new CacheEntry();
                entry.AtUtc = DateTime.UtcNow;
                entry.Body = (JObject)body.DeepClone();
                cache[assetId] = entry;

                Result ok = new Result();
                ok.HttpStatus = 200;
                ok.Body = body;
                return ok;
            }
            catch (Exception ex)
            {
                RosLog("ERROR", "AssetHealth asset=" + assetId + " " + ex.GetType().Name + " " + ex.Message);
                return Fail(500, "health summary unavailable");
            }
        }

        private static Result Fail(int status, string message)
        {
            Result r = new Result();
            r.HttpStatus = status;
            r.Body = new JObject();
            r.Body["error"] = message;
            return r;
        }

        // ----------------------------------------------------------------------------------------
        // Asset lookup: site/division given -> read that site; otherwise walk divisions -> sites
        // -> assets once, remembering every asset seen for IndexHours.
        // ----------------------------------------------------------------------------------------
        private static AssetRef ResolveAsset(int assetId, int siteId, int divisionId, IAssetHealthHost host)
        {
            AssetRef found = FreshIndexEntry(assetId);

            if (found == null && siteId > 0)
            {
                IndexSite(siteId, divisionId, host);
                found = FreshIndexEntry(assetId);
            }

            if (found == null)
            {
                lock (scanLock)
                {
                    found = FreshIndexEntry(assetId);
                    if (found == null)
                    {
                        RosLog("INFO", "AssetHealth asset=" + assetId + " not indexed -- walking divisions/sites");
                        foreach (int div in host.DivisionIds())
                        {
                            foreach (int site in host.SiteIds(div))
                            {
                                IndexSite(site, div, host);
                                found = FreshIndexEntry(assetId);
                                if (found != null)
                                {
                                    break;
                                }
                            }

                            if (found != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            if (found == null)
            {
                return null;
            }

            if (found.DivisionId <= 0)
            {
                if (divisionId > 0)
                {
                    found.DivisionId = divisionId;
                }
                else
                {
                    foreach (int div in host.DivisionIds())
                    {
                        if (host.SiteIds(div).Contains(found.SiteId))
                        {
                            found.DivisionId = div;
                            break;
                        }
                    }
                }
            }

            return found;
        }

        private static AssetRef FreshIndexEntry(int assetId)
        {
            AssetRef a;
            if (assetIndex.TryGetValue(assetId, out a) && (DateTime.UtcNow - a.SeenUtc).TotalHours < IndexHours)
            {
                return a;
            }

            return null;
        }

        private static void IndexSite(int siteId, int divisionId, IAssetHealthHost host)
        {
            JArray rows = host.AssetsOfSite(siteId) ?? new JArray();
            foreach (JToken t in rows)
            {
                JObject o = t as JObject;
                if (o == null)
                {
                    continue;
                }

                AssetRef a = new AssetRef();
                a.AssetId = RosJInt(o, "AssetId", "Id", "assetId", "id");
                if (a.AssetId <= 0)
                {
                    continue;
                }

                a.SiteId = RosJInt(o, "SiteId", "siteId");
                if (a.SiteId <= 0)
                {
                    a.SiteId = siteId;
                }

                a.DivisionId = divisionId;
                a.TypeId = RosJInt(o, "AssetTypeId", "AssetType", "assetTypeId");
                a.Name = RosJStr(o, "AssetName", "Name", "assetName", "name");
                a.TypeName = RosJStr(o, "AssetTypeName", "AssetTypeTitle", "TypeName");
                a.Station = RosJStr(o, "StationName", "SiteName");
                a.SeenUtc = DateTime.UtcNow;
                assetIndex[a.AssetId] = a;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Build the full response for a resolved asset
        // ----------------------------------------------------------------------------------------
        private static JObject Build(AssetRef asset, IAssetHealthHost host)
        {
            DateTime nowIst = DateTime.UtcNow.AddMinutes(330);
            DateTime today = nowIst.Date;
            string family = RosRosterFamily(asset.TypeId);
            bool isTrack = asset.TypeId == AssetTypeTrack;
            bool isPoint = asset.TypeId == AssetTypePoint;

            string dataStart = nowIst.AddDays(-DataDays).ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);
            string dataEnd = nowIst.ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);
            string pmStart = nowIst.AddDays(-PmDays).ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);

            JObject sources = new JObject();
            JObject evidence = new JObject();
            List<Deduction> deductions = new List<Deduction>();

            // ---- last 2 days of data + drift + safe-band use ----
            JObject data2d = ReadData2d(asset, host, dataStart, dataEnd, nowIst, sources, deductions);
            evidence["data2d"] = data2d;

            // ---- 15-day alert history ----
            JObject alerts15d = ReadAlerts(asset, host, today, sources, deductions);
            evidence["alerts15d"] = alerts15d;

            // ---- type-specific source ----
            string shortingBand = null;
            string pmBand = null;
            if (isTrack)
            {
                JObject shorting = ReadShorting(asset, host, sources, deductions);
                evidence["shorting"] = shorting;
                shortingBand = (string)shorting["band"] ?? "";
            }
            else if (isPoint)
            {
                JObject pm = ReadPm(asset, host, pmStart, dataEnd, sources, deductions);
                evidence["pm"] = pm;
                pmBand = (string)pm["band"] ?? "";
            }

            // ---- current live reading (context for the AI; no deduction) ----
            evidence["live"] = ReadLive(asset, host, sources);

            // ---- computed score / status / headline ----
            string alertBand = (string)alerts15d["band"] ?? "";
            string dataBand = (string)data2d["band"] ?? "";
            int outsideCount = (int?)data2d["outsideCount"] ?? 0;

            int score = 100;
            foreach (Deduction d in deductions)
            {
                score -= d.Points;
            }

            bool anyUrgent = IsBand(alertBand, "URGENT") || IsBand(dataBand, "URGENT")
                || IsBand(shortingBand, "URGENT") || IsBand(pmBand, "URGENT");
            bool anySoon = IsBand(alertBand, "SOON") || IsBand(dataBand, "SOON")
                || IsBand(shortingBand, "SOON") || IsBand(pmBand, "SOON") || outsideCount > 0;

            if (anyUrgent && score > 49)
            {
                score = 49;
            }
            else if (anySoon && score > 79)
            {
                score = 79;
            }

            if (score < 0)
            {
                score = 0;
            }

            string status;
            string label;
            string color;
            if (score >= 80)
            {
                status = "HEALTHY";
                label = "Healthy";
                color = "green";
            }
            else if (score >= 50)
            {
                status = "WATCH";
                label = "Watch";
                color = "amber";
            }
            else
            {
                status = "ACTION";
                label = "Action needed";
                color = "red";
            }

            string headline = Headline(deductions, sources);

            JObject bands = new JObject();
            bands["alerts"] = alertBand;
            bands["data"] = dataBand;
            bands["shorting"] = shortingBand == null ? JValue.CreateNull() : (JToken)shortingBand;
            bands["pm"] = pmBand == null ? JValue.CreateNull() : (JToken)pmBand;

            JObject health = new JObject();
            health["score"] = score;
            health["status"] = status;
            health["label"] = label;
            health["color"] = color;
            health["headline"] = headline;
            health["bands"] = bands;

            JObject assetOut = new JObject();
            assetOut["name"] = asset.Name;
            assetOut["type"] = family;
            assetOut["typeName"] = asset.TypeName;
            assetOut["station"] = asset.Station.Length > 0 ? asset.Station : ((string)alerts15d["station"] ?? "");
            assetOut["siteId"] = asset.SiteId;
            assetOut["divisionId"] = asset.DivisionId;

            // ---- AI findings (or computed fallback) ----
            JObject ai = AskAi(assetOut, health, deductions, evidence, sources);

            JObject body = new JObject();
            body["assetId"] = asset.AssetId;
            body["asset"] = assetOut;
            body["health"] = health;
            body["findings"] = ai["findings"];
            body["summary"] = ai["summary"];
            body["action"] = ai["action"];
            body["findingsSource"] = ai["source"];
            if (ai["error"] != null)
            {
                body["aiError"] = ai["error"];
            }

            body["evidence"] = evidence;
            body["sources"] = sources;
            body["generatedAt"] = nowIst.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            body["cached"] = false;
            body["version"] = ComponentVersion;

            RosLog("INFO", "AssetHealth asset=" + asset.AssetId + " type=" + family + " site=" + asset.SiteId
                + " RESULT score=" + score + " status=" + status + " findings=" + (string)ai["source"]
                + " headline=" + headline);
            return body;
        }

        // ----------------------------------------------------------------------------------------
        // Sources
        // ----------------------------------------------------------------------------------------
        private static JObject ReadData2d(AssetRef asset, IAssetHealthHost host, string start, string end, DateTime nowIst,
            JObject sources, List<Deduction> deductions)
        {
            JObject outObj = new JObject();
            outObj["from"] = nowIst.AddDays(-DataDays).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            outObj["to"] = nowIst.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            outObj["band"] = "";
            outObj["outsideCount"] = 0;

            JObject raw;
            try
            {
                raw = host.RangeHistory(asset.AssetId, start, end) ?? new JObject();
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth asset=" + asset.AssetId + " data2d " + ex.Message);
                sources["data2d"] = "unavailable";
                return outObj;
            }

            JArray attrs = raw["attrs"] as JArray;
            if (!string.IsNullOrWhiteSpace((string)raw["error"]) || attrs == null || attrs.Count == 0)
            {
                sources["data2d"] = string.IsNullOrWhiteSpace((string)raw["error"]) ? "insufficient" : "unavailable";
                return outObj;
            }

            sources["data2d"] = "ok";
            outObj["band"] = Upper((string)raw["band"]);

            JArray attrsOut = new JArray();
            List<string> outsideNames = new List<string>();
            List<string> nearNames = new List<string>();
            double worstDrift = 0.0;
            string worstDriftAttr = "";
            string worstDriftDir = "";

            foreach (JToken t in attrs)
            {
                JObject a = t as JObject;
                if (a == null)
                {
                    continue;
                }

                string name = (string)a["attr"] ?? "";
                double avg = Num(a["avg"]);
                double min = Num(a["min"]);
                double max = Num(a["max"]);
                double drift = Num(a["drift"]);
                string dir = (string)a["dir"] ?? "";
                string skipped = (string)a["skipped"] ?? "";

                List<double> series = new List<double>();
                JArray vals = a["vals"] as JArray;
                if (vals != null)
                {
                    foreach (JToken v in vals)
                    {
                        series.Add(Num(v));
                    }
                }

                bool outside = false;
                bool near = false;
                double usedPct;
                string edge;
                if (RosSafeBandUse(series, min, max, out usedPct, out edge))
                {
                    if (usedPct >= host.SafeOutsidePct)
                    {
                        outside = true;
                        outsideNames.Add(name);
                    }
                    else if (usedPct >= host.SafeNearPct)
                    {
                        near = true;
                        nearNames.Add(name);
                    }
                }

                if (skipped.Length == 0 && Math.Abs(drift) > Math.Abs(worstDrift))
                {
                    worstDrift = drift;
                    worstDriftAttr = name;
                    worstDriftDir = dir;
                }

                if (attrsOut.Count < 25)
                {
                    JObject row = new JObject();
                    row["attr"] = name;
                    row["avg"] = Math.Round(avg, 3);
                    row["min"] = min;
                    row["max"] = max;
                    row["n"] = (int?)a["n"] ?? series.Count;
                    row["drift"] = Math.Round(drift, 1);
                    row["dir"] = dir;
                    row["outside"] = outside;
                    row["near"] = near;
                    if (skipped.Length > 0)
                    {
                        row["driftSkipped"] = skipped;
                    }

                    attrsOut.Add(row);
                }
            }

            outObj["worstDriftPct"] = Math.Round(worstDrift, 1);
            outObj["worstDriftAttr"] = worstDriftAttr;
            outObj["outsideCount"] = outsideNames.Count;
            outObj["nearCount"] = nearNames.Count;
            outObj["attrs"] = attrsOut;

            if (outsideNames.Count > 0)
            {
                Deduction d = new Deduction();
                d.Points = Math.Min(36, 12 * outsideNames.Count);
                d.Band = "SOON";
                d.Text = outsideNames.Count + (outsideNames.Count == 1 ? " reading" : " readings")
                    + " outside safe range (" + JoinNames(outsideNames) + ")";
                deductions.Add(d);
            }

            if (nearNames.Count > 0)
            {
                Deduction d = new Deduction();
                d.Points = Math.Min(15, 5 * nearNames.Count);
                d.Band = "SOON";
                d.Text = nearNames.Count + (nearNames.Count == 1 ? " reading" : " readings")
                    + " near a safe limit (" + JoinNames(nearNames) + ")";
                deductions.Add(d);
            }

            double absDrift = Math.Abs(worstDrift);
            if (absDrift >= host.DriftSoonPct)
            {
                Deduction d = new Deduction();
                d.Points = absDrift >= host.DriftUrgentPct ? 20 : 10;
                d.Band = absDrift >= host.DriftUrgentPct ? "URGENT" : "SOON";
                d.Text = "Drift " + Math.Round(worstDrift, 1).ToString(CultureInfo.InvariantCulture) + "% on " + worstDriftAttr
                    + (worstDriftDir.Length > 0 ? " (" + worstDriftDir + ")" : "");
                deductions.Add(d);
            }

            return outObj;
        }

        private static JObject ReadAlerts(AssetRef asset, IAssetHealthHost host, DateTime today, JObject sources,
            List<Deduction> deductions)
        {
            DateTime from = today.AddDays(-(AlertDays - 1));
            DateTime to = today.AddDays(1);

            JObject outObj = new JObject();
            outObj["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            outObj["to"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            outObj["band"] = "";
            outObj["total"] = 0;

            if (asset.DivisionId <= 0)
            {
                sources["alerts15d"] = "unavailable";
                return outObj;
            }

            List<FRSAlert> rows;
            try
            {
                rows = host.Alerts(asset.DivisionId, from, to) ?? new List<FRSAlert>();
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth asset=" + asset.AssetId + " alerts " + ex.Message);
                sources["alerts15d"] = "unavailable";
                return outObj;
            }

            // Same exclusions as the roster's DayAlerts: this asset only, no test alerts, no
            // maintenance-acknowledged ("M") alerts.
            List<FRSAlert> mine = new List<FRSAlert>();
            foreach (FRSAlert a in rows)
            {
                if (a == null || a.AssetId != asset.AssetId)
                {
                    continue;
                }

                if (asset.SiteId > 0 && a.SiteId != asset.SiteId)
                {
                    continue;
                }

                if (a.IsTest.Equals(true))
                {
                    continue;
                }

                if (host.AckType(a) == "M")
                {
                    continue;
                }

                mine.Add(a);
            }

            sources["alerts15d"] = "ok";
            outObj["total"] = mine.Count;
            if (mine.Count == 0)
            {
                return outObj;
            }

            mine.Sort((x, y) => y.SetTimeStamp.CompareTo(x.SetTimeStamp));
            outObj["station"] = mine[0].SiteName ?? "";
            outObj["unacked"] = mine.Count(x => x.IsAcknowledgement != true);

            // Band with the shared roster rule (RosterItemBuilder) -- the same verdict the roster gives.
            RosterItemBuilder.BuildResult built = RosterItemBuilder.Build(mine, asset.DivisionId, today);
            string band = "";
            string reason = "";
            if (built.Items.Count > 0)
            {
                band = PriorityBand(built.Items[0].Priority);
                reason = built.Items[0].Reason ?? "";
            }

            outObj["band"] = band;
            outObj["rosterReason"] = reason;

            JArray byCause = new JArray();
            foreach (var causeGroup in mine.GroupBy(x => (x.CauseCode ?? x.PossibleCause ?? "").Trim()).OrderByDescending(x => x.Count()).Take(6))
            {
                JObject c = new JObject();
                c["cause"] = causeGroup.Key.Length > 0 ? causeGroup.Key : "(no cause)";
                c["count"] = causeGroup.Count();
                byCause.Add(c);
            }

            outObj["byCause"] = byCause;

            JArray latest = new JArray();
            foreach (FRSAlert a in mine.Take(8))
            {
                JObject l = new JObject();
                l["time"] = a.SetTimeStamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                l["cause"] = (a.CauseCode ?? a.PossibleCause ?? "").Trim();
                l["acknowledged"] = a.IsAcknowledgement == true;
                latest.Add(l);
            }

            outObj["latest"] = latest;

            if (band.Length > 0)
            {
                Deduction d = new Deduction();
                d.Points = BandPoints(band);
                d.Band = band;
                // The roster reason already starts with the count ("4 alerts in 15 days, degrading (2 -> 3) ...").
                if (reason.Length > 0)
                {
                    d.Text = Clip(reason, 120);
                }
                else
                {
                    d.Text = mine.Count + (mine.Count == 1 ? " alert" : " alerts") + " in " + AlertDays + " days";
                }
                deductions.Add(d);
            }

            return outObj;
        }

        private static JObject ReadShorting(AssetRef asset, IAssetHealthHost host, JObject sources, List<Deduction> deductions)
        {
            JObject outObj = new JObject();
            outObj["band"] = "";
            JObject raw;
            try
            {
                raw = host.TrackShort(asset.SiteId, asset.AssetId) ?? new JObject();
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth asset=" + asset.AssetId + " shorting " + ex.Message);
                sources["shorting"] = "unavailable";
                return outObj;
            }

            bool valid = string.IsNullOrWhiteSpace((string)raw["error"])
                && (!string.IsNullOrEmpty((string)raw["overall"]) || !string.IsNullOrEmpty((string)raw["severity"])
                    || !string.IsNullOrEmpty((string)raw["band"]));
            sources["shorting"] = valid ? "ok" : (string.IsNullOrWhiteSpace((string)raw["error"]) ? "insufficient" : "unavailable");

            string band = Upper((string)raw["band"]);
            outObj["band"] = band;
            outObj["events"] = (int?)raw["events"] ?? 0;
            outObj["severity"] = (string)raw["severity"] ?? "";
            outObj["leakStatus"] = (string)raw["leakStatus"] ?? "";
            outObj["worstCause"] = (string)raw["worstCause"] ?? "";
            outObj["overall"] = (string)raw["overall"] ?? "";
            outObj["summary"] = Clip(((string)raw["simpleSummary"] ?? "") + " " + ((string)raw["leakReason"] ?? ""), 400).Trim();

            if (band.Length > 0)
            {
                Deduction d = new Deduction();
                d.Points = BandPoints(band);
                d.Band = band;
                string severity = (string)outObj["severity"];
                d.Text = "Track shorting: " + (int)outObj["events"] + " window(s)"
                    + (severity.Length > 0 ? ", severity " + severity.ToLowerInvariant() : "");
                deductions.Add(d);
            }

            return outObj;
        }

        private static JObject ReadPm(AssetRef asset, IAssetHealthHost host, string start, string end, JObject sources,
            List<Deduction> deductions)
        {
            JObject outObj = new JObject();
            outObj["band"] = "";
            JObject raw;
            try
            {
                raw = host.PmOps(asset.SiteId, asset.AssetId, start, end) ?? new JObject();
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth asset=" + asset.AssetId + " pm " + ex.Message);
                sources["pm"] = "unavailable";
                return outObj;
            }

            bool valid = string.IsNullOrWhiteSpace((string)raw["error"])
                && (((int?)raw["ops"] ?? 0) > 0 || !string.IsNullOrEmpty((string)raw["band"]));
            sources["pm"] = valid ? "ok" : (string.IsNullOrWhiteSpace((string)raw["error"]) ? "insufficient" : "unavailable");

            string band = Upper((string)raw["band"]);
            outObj["band"] = band;
            outObj["windowDays"] = PmDays;
            outObj["ops"] = (int?)raw["ops"] ?? 0;
            outObj["worstState"] = (string)raw["worstState"] ?? "";
            outObj["condition"] = (string)raw["condition"] ?? "";

            if (band.Length > 0)
            {
                Deduction d = new Deduction();
                d.Points = BandPoints(band);
                d.Band = band;
                string state = (string)outObj["worstState"];
                d.Text = "PM prediction: " + (state.Length > 0 ? state : band.ToLowerInvariant());
                deductions.Add(d);
            }

            return outObj;
        }

        private static JObject ReadLive(AssetRef asset, IAssetHealthHost host, JObject sources)
        {
            JObject outObj = new JObject();
            JObject raw;
            try
            {
                raw = host.Live(asset.AssetId, asset.SiteId) ?? new JObject();
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth asset=" + asset.AssetId + " live " + ex.Message);
                sources["live"] = "unavailable";
                return outObj;
            }

            JArray values = raw["values"] as JArray;
            if (!string.IsNullOrWhiteSpace((string)raw["error"]) || values == null || values.Count == 0)
            {
                sources["live"] = string.IsNullOrWhiteSpace((string)raw["error"]) ? "insufficient" : "unavailable";
                return outObj;
            }

            sources["live"] = "ok";
            outObj["retrievedAt"] = (string)raw["retrievedAt"] ?? "";

            JArray rowsOut = new JArray();
            foreach (JToken t in values)
            {
                JObject v = t as JObject;
                if (v == null)
                {
                    continue;
                }

                if (rowsOut.Count >= 40)
                {
                    break;
                }

                JObject row = new JObject();
                row["attr"] = (string)v["attr"] ?? "";
                row["value"] = v["value"];
                row["unit"] = (string)v["unit"] ?? "";
                row["minSafe"] = v["minSafe"];
                row["maxSafe"] = v["maxSafe"];
                row["state"] = v["state"];
                row["at"] = v["at"];

                double value = Num(v["value"]);
                bool hasMin = v["minSafe"] != null && v["minSafe"].Type != JTokenType.Null;
                bool hasMax = v["maxSafe"] != null && v["maxSafe"].Type != JTokenType.Null;
                bool outside = (hasMin && value < Num(v["minSafe"])) || (hasMax && value > Num(v["maxSafe"]));
                row["outsideNow"] = (hasMin || hasMax) ? (JToken)outside : JValue.CreateNull();
                rowsOut.Add(row);
            }

            outObj["values"] = rowsOut;
            return outObj;
        }

        // ----------------------------------------------------------------------------------------
        // Headline
        // ----------------------------------------------------------------------------------------
        private static string Headline(List<Deduction> deductions, JObject sources)
        {
            Deduction top = deductions.OrderByDescending(d => d.Points).FirstOrDefault();
            if (top != null)
            {
                return top.Text;
            }

            if ((string)sources["data2d"] != "ok")
            {
                return "Readings for the last " + DataDays + " days are not available";
            }

            return "All readings inside their safe ranges; no problem found";
        }

        // ----------------------------------------------------------------------------------------
        // AI: context -> findings / summary / action. Falls back to computed findings.
        // ----------------------------------------------------------------------------------------
        private static JObject AskAi(JObject assetOut, JObject health, List<Deduction> deductions, JObject evidence, JObject sources)
        {
            JObject result = new JObject();

            JObject ctx = new JObject();
            ctx["asset"] = assetOut;
            ctx["computedHealth"] = health;
            JArray why = new JArray();
            foreach (Deduction d in deductions.OrderByDescending(x => x.Points))
            {
                JObject w = new JObject();
                w["points"] = -d.Points;
                w["band"] = d.Band;
                w["text"] = d.Text;
                why.Add(w);
            }

            ctx["deductions"] = why;
            ctx["evidence"] = evidence;
            ctx["sources"] = sources;

            string ctxText = ctx.ToString(Newtonsoft.Json.Formatting.None);
            if (ctxText.Length > MaxContextChars)
            {
                ctxText = ctxText.Substring(0, MaxContextChars);
            }

            string provider = (ConfigurationManager.AppSettings["AiProvider"] ?? "anthropic").Trim().ToLowerInvariant();
            string apiKey;
            string model;
            string url;
            bool anthropicShape = false;
            if (provider == "openai")
            {
                apiKey = (ConfigurationManager.AppSettings["OpenAiApiKey"] ?? "").Trim();
                model = (ConfigurationManager.AppSettings["OpenAiModel"] ?? "gpt-5.4").Trim();
                url = (ConfigurationManager.AppSettings["OpenAiBaseUrl"] ?? "https://api.openai.com/v1").Trim().TrimEnd('/') + "/chat/completions";
            }
            else if (provider == "deepseek")
            {
                apiKey = (ConfigurationManager.AppSettings["DeepSeekApiKey"] ?? "").Trim();
                model = (ConfigurationManager.AppSettings["DeepSeekModel"] ?? "deepseek-v4-pro").Trim();
                url = (ConfigurationManager.AppSettings["DeepSeekBaseUrl"] ?? "https://api.deepseek.com/v1").Trim().TrimEnd('/') + "/chat/completions";
            }
            else
            {
                anthropicShape = true;
                apiKey = (ConfigurationManager.AppSettings["AnthropicApiKey"] ?? "").Trim();
                model = (ConfigurationManager.AppSettings["AnthropicModel"] ?? "claude-sonnet-4-6").Trim();
                url = "https://api.anthropic.com/v1/messages";
            }

            if (apiKey.Length == 0)
            {
                RosLog("WARN", "AssetHealth: provider key missing -- computed findings");
                return Computed(result, deductions, evidence, sources, "not configured");
            }

            string sys =
                "You are a senior Indian Railways signal and telecom (S&T) maintenance analyst. You receive the health context " +
                "of ONE asset as JSON: its computed health (score, status, headline) and the deductions behind it; the last " +
                DataDays + " days of readings per attribute (average, safe min/max, drift %, whether the recent readings sit " +
                "outside or near the safe range); the " + AlertDays + "-day alert history; the current live readings; and, for a " +
                "track circuit, the track shorting analysis or, for a point machine, the PM prediction. " +
                "Write the findings shown on the asset's health card. Rules: " +
                "1) Ground every statement ONLY in the context. Never invent readings, units, causes, dates or relay names. " +
                "2) Do not change or argue with the computed score, status or headline -- explain them. " +
                "3) Each finding names the reading or source and its value, with the unit when given. " +
                "4) level: \"bad\" = a fault or a reading outside its safe range; \"warn\" = near a limit, drifting, repeated " +
                "alerts or something to watch; \"ok\" = a part that is healthy. Include at least one \"ok\" finding when " +
                "something is healthy. " +
                "5) If a source is missing or unavailable, say so in one finding. " +
                "6) Plain words for a maintenance engineer; no generic advice. " +
                "Reply with ONLY a JSON object, no markdown: {\"findings\":[{\"level\":\"ok|warn|bad\",\"text\":\"...\"}]," +
                "\"summary\":\"2-3 sentences\",\"action\":\"one short recommended action, or No action needed\"}. " +
                "3 to 6 findings, most important first.";
            string userMsg = "ASSET HEALTH CONTEXT (JSON):\n" + ctxText + "\n\nReply with the JSON object now.";

            JArray messages = new JArray();
            JObject um = new JObject();
            um["role"] = "user";
            um["content"] = userMsg;

            JObject payload = new JObject();
            payload["model"] = model;
            payload["max_tokens"] = 900;
            if (anthropicShape)
            {
                payload["system"] = sys;
                messages.Add(um);
                payload["messages"] = messages;
            }
            else
            {
                JObject sm = new JObject();
                sm["role"] = "system";
                sm["content"] = sys;
                messages.Add(sm);
                messages.Add(um);
                payload["messages"] = messages;
            }

            string reqBody = payload.ToString(Newtonsoft.Json.Formatting.None);

            // one call + one transient retry (4xx never retried) -- same policy as roster Diagnose
            string text = null;
            Exception last = null;
            for (int attempt = 0; attempt < 2 && text == null; attempt++)
            {
                try
                {
                    text = RosterModelCall(url, apiKey, anthropicShape, reqBody);
                }
                catch (System.Net.WebException wex)
                {
                    last = wex;
                    System.Net.HttpWebResponse hr = wex.Response as System.Net.HttpWebResponse;
                    if (hr != null && (int)hr.StatusCode >= 400 && (int)hr.StatusCode < 500)
                    {
                        break;
                    }

                    if (attempt == 0)
                    {
                        System.Threading.Thread.Sleep(1500);
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (attempt == 0)
                    {
                        System.Threading.Thread.Sleep(1500);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                RosLog("ERROR", "AssetHealth model call failed: " + (last != null ? last.GetType().Name + " " + last.Message : "empty reply"));
                return Computed(result, deductions, evidence, sources, "unavailable");
            }

            string brand = (ConfigurationManager.AppSettings["AiEngineBrand"] ?? "ENERGY7 ULTRA").Trim();
            try
            {
                string t = text.Trim();
                int a = t.IndexOf('{');
                int b = t.LastIndexOf('}');
                if (a >= 0 && b > a)
                {
                    t = t.Substring(a, b - a + 1);
                }

                JObject v = JObject.Parse(t);
                JArray findings = new JArray();
                JArray raw = v["findings"] as JArray;
                if (raw != null)
                {
                    foreach (JToken f in raw)
                    {
                        if (findings.Count >= 6)
                        {
                            break;
                        }

                        string level = ((string)f["level"] ?? "").Trim().ToLowerInvariant();
                        if (level != "ok" && level != "warn" && level != "bad")
                        {
                            level = "warn";
                        }

                        string ft = AhMaskModelIdentity(Clip((string)f["text"] ?? "", 320), brand).Trim();
                        if (ft.Length == 0)
                        {
                            continue;
                        }

                        JObject fo = new JObject();
                        fo["level"] = level;
                        fo["text"] = ft;
                        findings.Add(fo);
                    }
                }

                if (findings.Count == 0)
                {
                    return Computed(result, deductions, evidence, sources, "unreadable reply");
                }

                result["findings"] = findings;
                result["summary"] = AhMaskModelIdentity(Clip((string)v["summary"] ?? "", 600), brand);
                result["action"] = AhMaskModelIdentity(Clip((string)v["action"] ?? "", 200), brand);
                result["source"] = "ai";
                return result;
            }
            catch (Exception ex)
            {
                RosLog("WARN", "AssetHealth AI reply not JSON: " + ex.Message);
                return Computed(result, deductions, evidence, sources, "unreadable reply");
            }
        }

        /// <summary>Findings built from the evidence when the AI cannot answer -- the card is never empty.</summary>
        private static JObject Computed(JObject result, List<Deduction> deductions, JObject evidence, JObject sources, string error)
        {
            JArray findings = new JArray();
            foreach (Deduction d in deductions.OrderByDescending(x => x.Points))
            {
                JObject f = new JObject();
                f["level"] = IsBand(d.Band, "URGENT") || d.Text.Contains("outside safe range") ? "bad" : "warn";
                f["text"] = d.Text + ".";
                findings.Add(f);
            }

            JObject data2d = evidence["data2d"] as JObject;
            if ((string)sources["data2d"] == "ok" && data2d != null && ((int?)data2d["outsideCount"] ?? 0) == 0
                && ((int?)data2d["nearCount"] ?? 0) == 0)
            {
                findings.Add(Finding("ok", "All readings inside their safe ranges over the last " + DataDays + " days."));
            }

            JObject alerts = evidence["alerts15d"] as JObject;
            if ((string)sources["alerts15d"] == "ok" && alerts != null && ((int?)alerts["total"] ?? 0) == 0)
            {
                findings.Add(Finding("ok", "No alerts in the last " + AlertDays + " days."));
            }

            foreach (JProperty p in sources.Properties())
            {
                string state = (string)p.Value;
                if (state != "ok")
                {
                    findings.Add(Finding("warn", SourceName(p.Name) + " " + (state == "insufficient" ? "has too little data" : "is unavailable") + "."));
                }
            }

            if (findings.Count == 0)
            {
                findings.Add(Finding("ok", "No problem found in the available data."));
            }

            result["findings"] = findings;
            result["summary"] = "";
            result["action"] = deductions.Count > 0 ? "Check the items listed above on the next visit." : "No action needed";
            result["source"] = "computed";
            result["error"] = error;
            return result;
        }

        // ----------------------------------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------------------------------
        private static JObject Finding(string level, string text)
        {
            JObject f = new JObject();
            f["level"] = level;
            f["text"] = text;
            return f;
        }

        private static string SourceName(string key)
        {
            if (key == "data2d")
            {
                return "Readings for the last " + DataDays + " days";
            }

            if (key == "alerts15d")
            {
                return "Alert history";
            }

            if (key == "shorting")
            {
                return "Track shorting analysis";
            }

            if (key == "pm")
            {
                return "PM prediction";
            }

            if (key == "live")
            {
                return "Live reading";
            }

            return key;
        }

        private static string PriorityBand(string priority)
        {
            if (string.Equals(priority, RosterPriority.Urgent, StringComparison.OrdinalIgnoreCase))
            {
                return "URGENT";
            }

            if (string.Equals(priority, RosterPriority.Soon, StringComparison.OrdinalIgnoreCase))
            {
                return "SOON";
            }

            if (string.Equals(priority, RosterPriority.Monitor, StringComparison.OrdinalIgnoreCase))
            {
                return "MONITOR";
            }

            return "";
        }

        private static int BandPoints(string band)
        {
            if (IsBand(band, "URGENT"))
            {
                return 35;
            }

            if (IsBand(band, "SOON"))
            {
                return 20;
            }

            if (IsBand(band, "MONITOR"))
            {
                return 8;
            }

            return 0;
        }

        private static bool IsBand(string band, string want)
        {
            return band != null && string.Equals(band.Trim(), want, StringComparison.OrdinalIgnoreCase);
        }

        private static string Upper(string s)
        {
            return (s ?? "").Trim().ToUpperInvariant();
        }

        private static double Num(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null)
            {
                return 0.0;
            }

            double d;
            if (double.TryParse(t.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                return d;
            }

            return 0.0;
        }

        private static string Clip(string s, int max)
        {
            if (s == null)
            {
                return "";
            }

            return s.Length > max ? s.Substring(0, max) : s;
        }

        private static string JoinNames(List<string> names)
        {
            List<string> shown = names.Where(n => !string.IsNullOrWhiteSpace(n)).Take(3).ToList();
            string text = string.Join(", ", shown);
            if (names.Count > shown.Count)
            {
                text += ", +" + (names.Count - shown.Count) + " more";
            }

            return text;
        }
    }

    }
}
