using Domain;
using E7MRIWeb.Areas.FRS25.Controllers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // AnalysisEngine -- Prefetch.
    // Moved verbatim from AiChatController.Prefetch.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {
        internal static string PrefetchCacheKey(string tool, JObject args)
        {
            if (_prefetchCacheTtlSec <= 0) { return null; }
            if (!string.Equals(tool, "get_attribute_range", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(tool, "get_attribute_range_history", StringComparison.OrdinalIgnoreCase)) { return null; }
            try { return tool + "|" + (args != null ? args.ToString(Newtonsoft.Json.Formatting.None) : ""); }
            catch { return null; }
        }

        internal static bool TryGetPrefetchCache(string key, out string val)
        {
            val = null;
            KeyValuePair<DateTime, string> e;
            if (_prefetchCache.TryGetValue(key, out e) && e.Key > DateTime.UtcNow) { val = e.Value; return true; }
            return false;
        }

        internal static void SetPrefetchCache(string key, string val)
        {
            _prefetchCache[key] = new KeyValuePair<DateTime, string>(DateTime.UtcNow.AddSeconds(_prefetchCacheTtlSec), val);
        }


        // v1.0.160.110 SL7: grounding must live in ctx, not be recoverable only from a sibling
        // object. Fill-only-when-empty -- never overwrite what the caller supplied.
        internal void EnrichContextGrounding(JObject ctx)
        {
            try
            {
                if (ctx == null) { return; }
                JObject card = ctx["alertCard"] as JObject;
                if (GetCtx(ctx, "station").Length == 0 && card != null)
                {
                    JToken nm = card["stationName"] ?? card["stationCode"];
                    if (nm != null && nm.ToString().Trim().Length > 0) { ctx["station"] = nm.ToString().Trim(); }
                }
                if (GetCtx(ctx, "assetType").Length == 0 && card != null)
                {
                    JToken at = card["assetType"];
                    if (at != null && at.ToString().Trim().Length > 0) { ctx["assetType"] = at.ToString().Trim(); }
                }
                // Assert rather than hope: a field that silently stays blank is how this survived.
                if (GetCtx(ctx, "assetType").Length == 0)
                {
                    // v1.0.160.134: this fires BEFORE the FRS alert row is available, and .129 fills
                    // assetType from that row moments later. Both lines appeared 11 times in the same
                    // log, which reads as a contradiction. It is not a warning -- it is a statement
                    // about what is known at THIS point.
                    AiLog("INFO", "CTX", "assetType not in the alert card at enrichment -- to be filled from the FRS alert row if that lookup succeeds");
                }
            }
            catch { }
        }


        // v1.0.160.198: fetch the live "tag table" (api/LiveValue) once and return
        // AssetAttributeId -> latest value. Shared by the chat PM bind and the analysis snapshot so
        // both read the SAME source. Returns an empty map on any failure (never throws). 4s timeout.
        internal static Dictionary<int, double> FetchLiveValueMap(int assetId)
        {
            var map = new Dictionary<int, double>();
            try
            {
                if (assetId <= 0) { return map; }
                string apiBase = (System.Configuration.ConfigurationManager.AppSettings["DataApiBaseUrl"] ?? "").Trim();
                if (apiBase.Length == 0) { apiBase = (System.Configuration.ConfigurationManager.AppSettings["ProxyBaseUrl"] ?? "").Trim(); }
                if (apiBase.Length == 0) { return map; }
                if (!apiBase.EndsWith("/")) { apiBase += "/"; }
                string url = apiBase + "api/LiveValue/" + assetId.ToString();
                string jsonString = "";
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "GET"; req.Timeout = 4000; req.Accept = "application/json";
                System.Net.ServicePointManager.SecurityProtocol =
                    System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode != 200) { return map; }
                    using (var sr = new System.IO.StreamReader(resp.GetResponseStream())) { jsonString = sr.ReadToEnd(); }
                }
                if (string.IsNullOrEmpty(jsonString)) { return map; }
                JArray arr = JArray.Parse(jsonString);
                for (int i = 0; i < arr.Count; i++)
                {
                    JObject r = arr[i] as JObject; if (r == null) { continue; }
                    int a; double val;
                    if (r["AssetAttributeId"] != null && int.TryParse(r["AssetAttributeId"].ToString(), out a)
                        && r["Value"] != null && double.TryParse(r["Value"].ToString(),
                            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val))
                    { map[a] = val; }
                }
            }
            catch { }
            return map;
        }


        // train-free reference on a RAW series given exclusion intervals (epoch seconds).
        internal JObject ComputeTrainFreeRef(List<object[]> raw, List<double[]> excl)
        {
            int minSamp = ClampCfg(ReadInt("AnalyzeTrmMinSamples", 8), 3, 200);
            List<double> keep = new List<double>(); int excluded = 0;
            foreach (object[] pt in raw)
            {
                double ep = TrendTsToEpoch(pt[0] is JToken ? (JToken)pt[0] : new JValue(pt[0].ToString()));
                double vv; if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                if (double.IsNaN(ep)) { continue; }   // v1.0.152.5: cannot prove a sample train-free without a usable timestamp
                bool masked = false;
                for (int i = 0; i < excl.Count; i++) { if (ep >= excl[i][0] && ep <= excl[i][1]) { masked = true; break; } }
                if (masked) { excluded++; } else { keep.Add(vv); }
            }
            if (keep.Count < minSamp) { return null; }
            keep.Sort();
            double med = (keep.Count % 2 == 1) ? keep[keep.Count / 2] : (keep[keep.Count / 2 - 1] + keep[keep.Count / 2]) / 2.0;
            int lo10 = (int)(keep.Count * 0.10), hi10 = (int)(keep.Count * 0.90);
            double tsum = 0; int tcnt = 0; for (int ti = lo10; ti < hi10 && ti < keep.Count; ti++) { tsum += keep[ti]; tcnt++; }
            double trimMean = tcnt > 0 ? tsum / tcnt : med;
            JObject rf = new JObject();
            rf["median"] = Math.Round(med, 3);
            rf["trimmedMean"] = Math.Round(trimMean, 3);
            rf["usedSamples"] = keep.Count;
            rf["trainExcluded"] = excluded;
            return rf;
        }


        // Build [startEpoch,endEpoch] exclusion intervals from a TPR series: any span while
        // TPR value <= 0.5 (dropped = occupied), padded by AnalyzeTrmExcludeSec on each side,
        // plus a +/-window around every 0<->1 transition. Epoch seconds.
        // v1.0.152.1: exclusion intervals from a RAW TPR series (epoch seconds). Sorts and
        // dedupes by time, builds occupied spans (down..next up) + a +/-window around each real
        // 0<->1 transition, then MERGES overlapping intervals. Two-pointer filtering in
        // ComputeTrainFreeRef consumes these.
        internal List<double[]> BuildTrainExclusionRaw(List<object[]> tprRaw)
        {
            List<double[]> ex = new List<double[]>();
            if (tprRaw == null || tprRaw.Count < 2) { return ex; }
            int win = ClampCfg(ReadInt("AnalyzeTrmExcludeSec", 120), 10, 900);
            // parse to (epochSec, value), drop unparseable, sort by time, dedupe identical ts
            List<double[]> pts = new List<double[]>();
            foreach (object[] pt in tprRaw)
            {
                double ep = TrendTsToEpoch(new JValue(pt[0].ToString()));
                double vv; if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                if (double.IsNaN(ep)) { continue; }
                pts.Add(new double[] { ep, vv });
            }
            if (pts.Count < 2) { return ex; }
            pts.Sort(delegate(double[] a, double[] c) { return a[0].CompareTo(c[0]); });
            double prevV = double.NaN, prevT = double.NaN, dropStart = double.NaN;
            for (int i = 0; i < pts.Count; i++)
            {
                double ep = pts[i][0], vv = pts[i][1];
                if (!double.IsNaN(prevT) && ep == prevT) { continue; }   // dedupe same ts
                bool dropped = vv <= 0.5;
                if (!double.IsNaN(prevV) && ((prevV <= 0.5) != dropped)) { ex.Add(new double[] { ep - win, ep + win }); }
                if (dropped && double.IsNaN(dropStart)) { dropStart = ep; }
                if (!dropped && !double.IsNaN(dropStart)) { ex.Add(new double[] { dropStart - win, ep + win }); dropStart = double.NaN; }
                prevV = vv; prevT = ep;
            }
            if (!double.IsNaN(dropStart)) { ex.Add(new double[] { dropStart - win, prevT + win }); }
            // merge overlapping intervals
            if (ex.Count <= 1) { return ex; }
            ex.Sort(delegate(double[] a, double[] c) { return a[0].CompareTo(c[0]); });
            List<double[]> merged = new List<double[]>();
            double[] curi = ex[0];
            for (int i = 1; i < ex.Count; i++)
            {
                if (ex[i][0] <= curi[1]) { if (ex[i][1] > curi[1]) { curi[1] = ex[i][1]; } }
                else { merged.Add(curi); curi = ex[i]; }
            }
            merged.Add(curi);
            return merged;
        }


        internal void CollectAssetHints(JToken toolArgs)
        {
            JObject o = toolArgs as JObject;
            if (o == null || Sess._jsonAssetHints == null) { return; }
            foreach (var prop in o.Properties())
            {
                string ln = prop.Name.ToLowerInvariant();
                if (ln != "assetid" && ln != "assetids" && ln != "aid" && ln != "asset_id") { continue; }
                JToken v = prop.Value;
                if (v == null) { continue; }
                if (v.Type == JTokenType.Array)
                {
                    foreach (JToken e in (JArray)v)
                    {
                        if (e != null && e.Type != JTokenType.Null) { Sess._jsonAssetHints.Add(e.ToString()); }
                    }
                }
                else if (v.Type != JTokenType.Null) { Sess._jsonAssetHints.Add(v.ToString()); }
            }
        }


        // get_attribute_range is SITE-scoped by design: the server calls
        // FRSAttributeRange/SiteId/{id} and there is no AssetId filter, so it
        // returns the baseline row for EVERY attribute of EVERY asset at the
        // station -- measured at ~266 KB, which then gets truncated in transit and
        // discarded. The analysis only ever concerns one asset, so keep the rows
        // for the AssetIds this request has actually used and drop the rest.
        // Falls through untouched if nothing matches, so a mismatch can never
        // remove evidence.
        internal string NarrowAttributeRange(string toolName, string result)
        {
            if (!string.Equals(toolName, "get_attribute_range", StringComparison.OrdinalIgnoreCase)) { return result; }
            if (Sess._jsonAssetHints == null || Sess._jsonAssetHints.Count == 0) { return result; }
            if (string.IsNullOrEmpty(result)) { return result; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(result);
                for (int d = 0; d < docs.Count; d++)
                {
                    // v1.0.104.0: the FRSAttributeRangeHistory response is a JOBJECT with an
                    // AssetAttributes[] array PLUS top-level site context (Latitude, Longitude,
                    // FamilyTrack, SignalTrackMapping, SiteName, StationCode). Emit ONE VALID
                    // JSON OBJECT {siteContext, legend, rows} - NOT text+JSON. get_attribute_range
                    // is an evidence tool (requireJson=true); a "[SITE CONTEXT] ..." or "legend:"
                    // prefix is not valid JSON and the evidence validator rejects the whole
                    // result, dropping the baseline (the 1.0.102 defect). A single object passes
                    // validation on prefetch AND both agentic paths while keeping geo + topology.
                    JObject obj = docs[d] as JObject;
                    if (obj != null)
                    {
                        JArray attrs = PropCI(obj, "AssetAttributes") as JArray;
                        if (attrs != null)
                        {
                            JArray keptO = new JArray();
                            for (int i = 0; i < attrs.Count; i++)
                            {
                                JObject row = attrs[i] as JObject;
                                if (row == null) { continue; }
                                JToken aid = PropCI(row, "AssetId") ?? PropCI(row, "Id");
                                if (aid == null || Sess._jsonAssetHints.Contains(aid.ToString())) { keptO.Add(row); }
                            }
                            if (keptO.Count == 0) { keptO = attrs; }
                            AddDiag("ATTR_RANGE_NARROWED");
                            return BuildRangeJson(obj, keptO);
                        }
                    }
                    JArray arr = docs[d] as JArray;
                    if (arr == null) { continue; }
                    JArray kept = new JArray();
                    for (int i = 0; i < arr.Count; i++)
                    {
                        JObject row = arr[i] as JObject;
                        if (row == null) { continue; }
                        JToken aid = PropCI(row, "AssetId");
                        if (aid != null && Sess._jsonAssetHints.Contains(aid.ToString())) { kept.Add(row); }
                    }
                    if (kept.Count > 0)
                    {
                        AddDiag("ATTR_RANGE_NARROWED");
                        // bare-array form (no site context available) - still emit valid JSON
                        return BuildRangeJson(null, kept);
                    }
                }
            }
            catch { }
            return result;
        }


        // v1.0.104.0: assemble the compact attribute-range evidence as ONE valid JSON object.
        // Shape: { "siteContext": {...}, "legend": {...}, "rows": [ {a,t,n,avg,max,min,fail}, ... ] }
        // siteContext is omitted when no geo/topology is present (bare-array form). This is the
        // single thing get_attribute_range returns to the model, and it must be valid JSON.
        internal static string BuildRangeJson(JObject src, JArray keptRows)
        {
            JObject outObj = new JObject();
            if (src != null)
            {
                JObject sc = SiteContextObject(src);
                if (sc != null) { outObj["siteContext"] = sc; }
            }
            outObj["legend"] = new JObject
            {
                ["a"] = "AssetId", ["t"] = "AssetAttributeId", ["n"] = "AttributeName",
                ["avg"] = "AverageValue", ["max"] = "MaxSafeValue", ["min"] = "MinSafeValue",
                ["fail"] = "MinFailValue"
            };
            JArray rows = new JArray();
            for (int i = 0; i < keptRows.Count; i++)
            {
                JObject r = keptRows[i] as JObject;
                if (r == null) { continue; }
                JObject c = new JObject();
                CopyIf(r, c, "AssetId", "a");
                CopyIf(r, c, "AssetAttributeId", "t");
                CopyIf(r, c, "AttributeName", "n");
                CopyIf(r, c, "AverageValue", "avg");
                CopyIf(r, c, "MaxSafeValue", "max");
                CopyIf(r, c, "MinSafeValue", "min");
                CopyIf(r, c, "MinFailValue", "fail");
                // Raw FRS rows use Id/Title rather than AssetAttributeId/AttributeName - fall
                // back to those so the attribute id/name survive whichever shape the tool emits.
                if (c["t"] == null) { CopyIf(r, c, "Id", "t"); }
                if (c["n"] == null) { CopyIf(r, c, "Title", "n"); }
                if (c.Count > 0) { rows.Add(c); }
            }
            outObj["rows"] = rows;
            return outObj.ToString(Formatting.None);
        }


        // v1.0.104.0: site context (geo + topology) as a JSON OBJECT, or null if nothing present.
        // These are top-level fields on the FRSAttributeRangeHistory object, dropped by the per-row
        // compactor, so they are surfaced here. Topology values are numeric asset IDs; the model is
        // told to cite the ID (not invent a name) unless site-alert evidence maps the ID to a name.
        internal static JObject SiteContextObject(JObject obj)
        {
            if (obj == null) { return null; }
            JToken lat = PropCI(obj, "Latitude");
            JToken lon = PropCI(obj, "Longitude");
            JToken site = PropCI(obj, "SiteName");
            JToken code = PropCI(obj, "StationCode");
            JToken fam = PropCI(obj, "FamilyTrack");
            JToken sigMap = PropCI(obj, "SignalTrackMapping") ?? PropCI(obj, "SignalTrackMappings");
            JToken ptMap = PropCI(obj, "PointTrackMapping") ?? PropCI(obj, "PointTrackMappings");
            bool any = site != null || code != null || lat != null || lon != null
                       || fam != null || sigMap != null || ptMap != null;
            if (!any) { return null; }
            JObject sc = new JObject();
            if (site != null) { sc["site"] = site.ToString(); }
            if (code != null) { sc["code"] = code.ToString(); }
            if (lat != null) { sc["latitude"] = lat; }
            if (lon != null) { sc["longitude"] = lon; }
            if (fam != null) { sc["familyTracks"] = IdArray(fam, "TrackFamilyId"); }
            if (sigMap != null) { sc["signalTrack"] = IdArray(sigMap, "SignalAssetId"); }
            if (ptMap != null) { sc["pointTrack"] = IdArray(ptMap, "PointAssetId"); }
            return sc;
        }


        // Render a mapping array like [{"TrackFamilyId":41516},...] as a compact JSON id array.
        internal static JArray IdArray(JToken t, string idField)
        {
            JArray outA = new JArray();
            JArray a = t as JArray;
            if (a == null) { return outA; }
            for (int i = 0; i < a.Count; i++)
            {
                JObject o = a[i] as JObject;
                if (o != null) { JToken v = PropCI(o, idField); if (v != null) { outA.Add(v); } }
                else { outA.Add(a[i]); }
            }
            return outA;
        }


        internal static void CopyIf(JObject src, JObject dst, string from, string to)
        {
            JToken v = PropCI(src, from);
            if (v != null && v.Type != JTokenType.Null) { dst[to] = v; }
        }


        // v1.0.117.0: pull latitude/longitude out of a get_attribute_range_history response.
        // The handoff delivers them as top-level "latitude"/"longitude" STRINGS (e.g. "26.7046");
        // accept string or number, case-insensitively. Returns false if either is absent/unparseable.
        // v1.0.160.46: the station code can arrive in four places depending on which page opened the
        // analysis. Ordered most-specific-first; all are CODES (never the station NAME).
        internal static string ResolveStationCode(JObject ctx, string rangeHistoryJson)
        {
            // v1.0.160.136: CODE first. ctx["station"] carries the station NAME since 1.0.160.110,
            // and a name is not a valid timetable key.
            string stn = GetCtx(ctx, "stationCode");
            if (!string.IsNullOrEmpty(stn)) { return stn.Trim(); }
            try
            {
                JObject card = ctx != null ? ctx["alertCard"] as JObject : null;
                if (card != null)
                {
                    string cs = GetCtx(card, "stationCode");
                    if (!string.IsNullOrEmpty(cs)) { return cs.Trim(); }
                }
            }
            catch { }
            // v1.0.160.136: ctx["station"] LAST, and only if it looks like a code rather than a
            // name -- SAKHUN is a name, SK is a code. A wrong key fetches nothing and, on a
            // best-effort path, fails silently.
            string nm = GetCtx(ctx, "station");
            if (!string.IsNullOrEmpty(nm))
            {
                string t = nm.Trim();
                if (t.Length <= 5 && t.IndexOf(' ') < 0) { return t; }
            }
            // Last resort: the E7 API returns stationCode alongside lat/lon in the range-history
            // payload, so a client that sends no code at all still resolves.
            try
            {
                if (!string.IsNullOrEmpty(rangeHistoryJson))
                {
                    // v1.0.160.81: tolerant parse. JObject.Parse throws when the server truncates this
                    // payload mid-array, and the station code was being lost even though it sits in the
                    // first ~120 chars and survives every cut. TryExtractLatLon below already parses
                    // this defensively -- that asymmetry is why weather kept working and trains did not.
                    List<JToken> rhDocs = ParseJsonDocuments(rangeHistoryJson);
                    for (int i = 0; i < rhDocs.Count; i++)
                    {
                        JObject rho = rhDocs[i] as JObject;
                        if (rho == null) { continue; }
                        string rs2 = GetCtx(rho, "stationCode");
                        if (!string.IsNullOrEmpty(rs2)) { return rs2.Trim(); }
                    }
                    // Last resort: read it straight out of the head. A cut payload is not valid JSON,
                    // but the field is still sitting there in plain text.
                    Match sm = Regex.Match(rangeHistoryJson,
                        "\"stationCode\"\\s*:\\s*\"([A-Za-z0-9_-]{1,12})\"");
                    if (sm.Success)
                    {
                        AiLogStatic("INFO", "TRAIN", "station code recovered by scan from a truncated range-history payload");
                        return sm.Groups[1].Value.Trim();
                    }
                }
            }
            catch { }
            return "";
        }


        internal static bool TryExtractLatLon(string rangeHistoryJson, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            if (string.IsNullOrEmpty(rangeHistoryJson)) { return false; }
            try
            {
                List<JToken> docs = ParseJsonDocuments(rangeHistoryJson);
                for (int d = 0; d < docs.Count; d++)
                {
                    JObject o = docs[d] as JObject;
                    if (o == null) { continue; }
                    JToken latT = PropCI(o, "latitude");
                    JToken lonT = PropCI(o, "longitude");
                    if (latT == null || lonT == null) { continue; }
                    if (double.TryParse(latT.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lat)
                        && double.TryParse(lonT.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lon)
                        && (Math.Abs(lat) > 1e-6 || Math.Abs(lon) > 1e-6))
                    { return true; }
                }
            }
            catch { }
            return false;
        }

        internal static System.Net.Http.HttpClient MakeTrainCtxHttp()
        {
            System.Net.Http.HttpClient h = new System.Net.Http.HttpClient();
            h.MaxResponseContentBufferSize = _trainCtxMaxBytes;   // cap the buffered body (P1: was unbounded)
            return h;
        }


        // Weather matters for track/rail-resistance/leakage/ballast (rain) and for surge/
        // multi-asset events (lightning). It is NOT relevant to point-machine mechanical or
        // pure config alerts, so those skip the fetch (deterministic, cheaper than a model turn).
        internal static bool IsWeatherRelevantCause(string causeCode)
        {
            if (string.IsNullOrEmpty(causeCode)) { return false; }
            string c = causeCode.ToUpperInvariant();
            return c.Contains("RAIL RES") || c.Contains("TRACK") || c.Contains("TC ")
                || c.Contains("BALLAST") || c.Contains("LEAK") || c.Contains("RRAIL")
                || c.Contains("IPS") || c.Contains("POWER") || c.Contains("VOLT")
                || c.Contains("EARTH") || c.Contains("SURGE");
        }


        // Fetch a compact weather summary for the site around the incidence. Uses Open-Meteo's
        // historical archive (past incidence) by lat/long. Returns null on any problem.
        internal async Task<string> FetchWeatherAsync(double lat, double lon, DateTime incidenceIst, System.Threading.CancellationToken ct)
        {
            try
            {
                // Archive API wants a date window; take the incidence day and the day before
                // (covers a pre-incidence rain build-up). Dates are the site-local date.
                string end = incidenceIst.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                string start = incidenceIst.AddDays(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                string url = "https://archive-api.open-meteo.com/v1/archive"
                    + "?latitude=" + lat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&longitude=" + lon.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "&start_date=" + start + "&end_date=" + end
                    + "&hourly=precipitation,rain,weather_code,temperature_2m,wind_speed_10m"
                    + "&timezone=auto";

                using (System.Threading.CancellationTokenSource wc = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    wc.CancelAfter(TimeSpan.FromSeconds(_weatherTimeoutSec));
                    System.Net.Http.HttpResponseMessage resp = await _weatherHttp.GetAsync(url, wc.Token).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { return null; }
                    string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return SummariseWeather(raw, incidenceIst);
                }
            }
            catch { return null; }   // egress blocked, timeout, parse fault -> no weather
        }


        // v1.0.119.0: reshape the internal weather JSON (rain/precip/thunderstorm/temp_c_range)
        // into the card's contract {condition, tempC, summary} so the verdict WEATHER tile renders
        // the real data. Attached to the verdict server-side (weather is a server-only key). Returns
        // null if the input is unusable.
        // v1.0.160.49: WMO weather-code -> condition word. Codes per the Open-Meteo/WMO table the
        // hourly weather_code field already returns. Falls back to the pre-160.49 rain/thunder
        // booleans when no code is present, so a missing code degrades to old behaviour.
        internal static string WmoCondition(int wc, bool thunder, bool rain)
        {
            if (wc < 0)
            {
                return thunder ? "Thunderstorm" : (rain ? "Rain" : "Clear");
            }
            if (wc >= 95) { return "Thunderstorm"; }
            if (wc == 0) { return "Clear"; }
            if (wc == 1) { return "Mainly clear"; }
            if (wc == 2) { return "Partly cloudy"; }
            if (wc == 3) { return "Overcast"; }
            if (wc == 45 || wc == 48) { return "Fog"; }
            if (wc >= 51 && wc <= 57) { return "Drizzle"; }
            if (wc >= 61 && wc <= 67) { return "Rain"; }
            if (wc >= 71 && wc <= 77) { return "Snow"; }
            if (wc >= 80 && wc <= 82) { return "Rain showers"; }
            if (wc == 85 || wc == 86) { return "Snow showers"; }
            // unknown code: fall back to the measured booleans rather than inventing a sky.
            return thunder ? "Thunderstorm" : (rain ? "Rain" : "Clear");
        }


        internal static JObject BuildWeatherCard(string weatherJson)
        {
            if (string.IsNullOrEmpty(weatherJson)) { return null; }
            try
            {
                JObject w = JObject.Parse(weatherJson);
                bool rain = w["rain"] != null && w["rain"].Type == JTokenType.Boolean && (bool)w["rain"];
                bool thunder = w["thunderstorm"] != null && w["thunderstorm"].Type == JTokenType.Boolean && (bool)w["thunderstorm"];
                double precip = 0; if (w["precip_mm_total"] != null) { double.TryParse(w["precip_mm_total"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out precip); }

                // v1.0.160.49: map the WMO weather_code to a REAL sky state. The old three-way
                // ternary could only say Thunderstorm/Rain/"Dry", and "Dry" matches none of the
                // view's rvwxKind() patterns, so every dry alert drew the generic cloud. Every word
                // emitted below already matches an existing pattern (clear|cloud|overcast|fog|
                // drizzle|rain|shower|snow|thunder), so NO view change is needed.
                int wc = -1;
                if (w["weather_code"] != null) { int.TryParse(w["weather_code"].ToString(), out wc); }
                string condition = WmoCondition(wc, thunder, rain);
                JObject card = new JObject();
                card["condition"] = condition;
                // mid of the temp range, if present
                JArray tr = w["temp_c_range"] as JArray;
                if (tr != null && tr.Count == 2)
                {
                    double a, b;
                    if (double.TryParse(tr[0].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out a)
                        && double.TryParse(tr[1].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out b))
                    { card["tempC"] = Math.Round((a + b) / 2.0, 1); }
                }
                // summary: rainfall amount if any, else the plain condition
                if (rain || precip > 0.1) { card["summary"] = precip.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mm around alert"; }
                // v1.0.160.49: the view classifies on condition+summary COMBINED, so the summary must
                // not contain a trigger word. The old text "no rain at alert time" matched /rain/ and
                // drew the RAIN icon on a dry day -- a pre-existing bug. "no precipitation" is neutral.
                else if (!thunder) { card["summary"] = "no precipitation around alert"; }
                return card;
            }
            catch { return null; }
        }


        // v1.0.160.33: fetch a public station timetable page (totaltraininfo) by station code. Direct
        // HTTP GET, short timeout, best-effort -- returns the raw HTML, or null on any problem.
        internal static async Task<string> FetchStationTrainsAsync(string stationCode, System.Threading.CancellationToken ct)
        {
            try
            {
                string code = (stationCode ?? "").Trim().ToLowerInvariant();
                if (code.Length == 0) { return null; }
                // v1.0.160.35: serve from the short-TTL per-station cache when fresh (avoid re-hitting the site).
                Tuple<DateTime, string> hit;
                if (_trainCtxCacheMin > 0 && _trainCtxCache.TryGetValue(code, out hit) && hit != null
                    && (DateTime.UtcNow - hit.Item1).TotalMinutes < _trainCtxCacheMin)
                {
                    return hit.Item2;
                }
                string url = _trainCtxBaseUrl + Uri.EscapeDataString(code) + "/";
                using (System.Threading.CancellationTokenSource cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(_trainCtxTimeoutMs);
                    using (System.Net.Http.HttpRequestMessage req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url))
                    {
                        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (compatible; E7MRIWeb/1.0)");
                        // v1.0.160.35: ResponseContentRead so the linked-CTS timeout covers the FULL body
                        // download (headers-only completion let a stalled body run past the timeout).
                        using (System.Net.Http.HttpResponseMessage resp = await _trainCtxHttp.SendAsync(req, System.Net.Http.HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
                        {
                            if (!resp.IsSuccessStatusCode) { return null; }
                            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            // v1.0.160.36: cache ONLY a VALIDATED page (marker + >=1 parsed train), so a
                            // challenge / changed-format page can never suppress the station for the whole
                            // TTL; sweep expired entries first, and keep a hard ceiling to bound memory.
                            if (_trainCtxCacheMin > 0 && !string.IsNullOrEmpty(body) && ParseStationTrains(body, code).Count > 0)
                            {
                                SweepTrainCtxCache();
                                if (_trainCtxCache.Count < 512) { _trainCtxCache[code] = Tuple.Create(DateTime.UtcNow, body); }
                            }
                            return body;
                        }
                    }
                }
            }
            catch { return null; }
        }


        // v1.0.160.36: drop expired cache entries (TTL controls reuse but never removed them before, so the
        // dictionary could grow for the process lifetime). Snapshots the keys so removal is enumerator-safe.
        internal static void SweepTrainCtxCache()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                foreach (string k in new System.Collections.Generic.List<string>(_trainCtxCache.Keys))
                {
                    Tuple<DateTime, string> v;
                    if (_trainCtxCache.TryGetValue(k, out v) && (v == null || (now - v.Item1).TotalMinutes >= _trainCtxCacheMin))
                    {
                        Tuple<DateTime, string> rm;
                        _trainCtxCache.TryRemove(k, out rm);
                    }
                }
            }
            catch { }
        }


        // Parse the station timetable HTML into rows of {train_no, name, arr, dep}. Tolerant: isolate
        // the trains table, split on <tr>, and per row take the first 4-5 digit train number + the
        // first two HH:MM times; skip rows that do not match. Best-effort -- a page-format change just
        // yields fewer/zero rows (no train_context), never an exception.
        internal static List<string[]> ParseStationTrains(string html, string stationCode)
        {
            List<string[]> outp = new List<string[]>();
            if (string.IsNullOrEmpty(html)) { return outp; }
            try
            {
                // v1.0.160.41 GUARD A (page identity): an unknown station code is NOT a 404 -- the site
                // returns a generic page whose <title> has a BLANK station ("/  Railway Station | ..."),
                // while a real page reads "PPTA / Patliputra Junction Railway Station | ...". Requiring
                // the title to start with the requested code rejects the generic page outright, which
                // restores the fail-safe the pre-160.38 </table> bound provided by accident.
                if (!string.IsNullOrEmpty(stationCode))
                {
                    System.Text.RegularExpressions.Match ttl = System.Text.RegularExpressions.Regex.Match(
                        html, "<title[^>]*>(.*?)</title>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (!ttl.Success) { return outp; }
                    string tt = System.Net.WebUtility.HtmlDecode(ttl.Groups[1].Value);
                    tt = System.Text.RegularExpressions.Regex.Replace(tt, "\\s+", " ").Trim();
                    string want = stationCode.Trim();
                    bool idOk = tt.StartsWith(want + " ", StringComparison.OrdinalIgnoreCase)
                                || tt.StartsWith(want + "/", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(tt, want, StringComparison.OrdinalIgnoreCase);
                    if (!idOk) { return outp; }   // generic/changed page -> no rows -> block omitted
                }
                // marker gate (unchanged): no "List of all trains" marker => not a valid timetable page
                // (bot-challenge / changed / invalid station) => return no rows, so BuildTrainContext
                // omits train_context rather than asserting a false "no train".
                int ti = html.IndexOf("List of all trains", StringComparison.OrdinalIgnoreCase);
                if (ti < 0) { return outp; }
                string region = html.Substring(ti);

                // v1.0.160.38 PATH A (current site layout): totaltraininfo migrated the timetable from
                // <table>/<tr> to <div class="sch-Table/sch-Row/sch-Cell">. The old </table> bound now
                // hits an unrelated table right after the marker and truncates to ~1 row. Split the
                // post-marker region on the schedule-exclusive "sch-Row" token; a genuine train row
                // carries a /train/<no>/schedule anchor (this also skips the header row and any
                // non-train segment). Arr/Dep = the HH:MM cells (1st=Arr, 2nd=Dep; a single time is an
                // originating/terminating train => Arr=Dep). Name from the row's title="..." attribute
                // (the anchor text is ellipsis-truncated); cosmetic.
                string[] divRows = System.Text.RegularExpressions.Regex.Split(region, "sch-Row", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                for (int r = 0; r < divRows.Length; r++)
                {
                    System.Text.RegularExpressions.Match dno = System.Text.RegularExpressions.Regex.Match(divRows[r], "/train/(\\d{4,6})/schedule", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!dno.Success) { continue; }

                    // v1.0.160.40 (D2 fix): read the row's CELLS, not the flattened row. Taking the first
                    // two HH:MM out of the whole row meant any stray time before the Arr cell became the
                    // arrival (e.g. an "Updated 23:59" cell -> arr=23:59). A time cell is a sch-Cell whose
                    // ENTIRE text is a bare HH:MM, so labelled cells ("Halt 10m", "Updated ...") cannot win.
                    System.Text.RegularExpressions.MatchCollection dcells = System.Text.RegularExpressions.Regex.Matches(
                        divRows[r], "sch-Cell[^>]*>(.*?)</div>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    List<string> dtimes = new List<string>();
                    string dname = "";
                    for (int ci = 0; ci < dcells.Count; ci++)
                    {
                        string cin = dcells[ci].Groups[1].Value;
                        string ctx = System.Text.RegularExpressions.Regex.Replace(cin, "<[^>]+>", " ");
                        ctx = System.Net.WebUtility.HtmlDecode(ctx);
                        ctx = System.Text.RegularExpressions.Regex.Replace(ctx, "\\s+", " ").Trim();
                        if (System.Text.RegularExpressions.Regex.IsMatch(ctx, "^\\d{1,2}:\\d{2}$")) { dtimes.Add(ctx); }
                        // v1.0.160.40 (D3 fix): the train name is the title= on the cell that ALSO carries
                        // the /train/<no>/schedule anchor -- not merely the first title= in the row (a
                        // leading UI title such as "Sort ascending" would otherwise win).
                        if (dname.Length == 0
                            && System.Text.RegularExpressions.Regex.IsMatch(cin, "/train/\\d{4,6}/schedule", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            System.Text.RegularExpressions.Match cti = System.Text.RegularExpressions.Regex.Match(cin, "title=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (cti.Success)
                            {
                                dname = System.Net.WebUtility.HtmlDecode(cti.Groups[1].Value);
                                dname = System.Text.RegularExpressions.Regex.Replace(dname, "\\s+", " ").Trim();
                            }
                        }
                    }
                    // Fallback ONLY if the cell structure is unrecognisable (markup changed again): scan
                    // the flattened row as 160.38 did, so a layout tweak degrades instead of dropping rows.
                    if (dtimes.Count < 1)
                    {
                        string dtext = System.Text.RegularExpressions.Regex.Replace(divRows[r], "<[^>]+>", " ");
                        dtext = System.Net.WebUtility.HtmlDecode(dtext);
                        dtext = System.Text.RegularExpressions.Regex.Replace(dtext, "\\s+", " ").Trim();
                        System.Text.RegularExpressions.MatchCollection dtmc = System.Text.RegularExpressions.Regex.Matches(dtext, "\\b(\\d{1,2}:\\d{2})\\b");
                        for (int ti2 = 0; ti2 < dtmc.Count; ti2++) { dtimes.Add(dtmc[ti2].Groups[1].Value); }
                    }
                    if (dtimes.Count < 1) { continue; }
                    string darr = dtimes[0];
                    string ddep = dtimes.Count > 1 ? dtimes[1] : darr;
                    if (dname.Length == 0)
                    {
                        System.Text.RegularExpressions.MatchCollection danchors = System.Text.RegularExpressions.Regex.Matches(
                            divRows[r], "<a\\b[^>]*>(.*?)</a>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                        for (int ai = 0; ai < danchors.Count; ai++)
                        {
                            string at = System.Text.RegularExpressions.Regex.Replace(danchors[ai].Groups[1].Value, "<[^>]+>", " ");
                            at = System.Net.WebUtility.HtmlDecode(at);
                            at = System.Text.RegularExpressions.Regex.Replace(at, "\\s+", " ").Trim();
                            if (at.Length >= 3 && System.Text.RegularExpressions.Regex.IsMatch(at, "[A-Za-z]")
                                && !System.Text.RegularExpressions.Regex.IsMatch(at, "^\\d{1,2}:\\d{2}$"))
                            { dname = at; break; }
                        }
                    }
                    if (dname.Length > 60) { dname = dname.Substring(0, 60).Trim(); }
                    outp.Add(new string[] { dno.Groups[1].Value, dname, darr, ddep });
                }
                if (outp.Count > 0) { return outp; }   // div layout parsed -> done

                // v1.0.160.38 PATH B (fallback, VERBATIM 160.36 logic): the div layout parsed nothing,
                // so the page may still be the legacy <table>/<tr> format. Bound marker -> next </table>
                // and split on <tr>. No closing </table> => malformed => no rows.
                int te = region.IndexOf("</table>", StringComparison.OrdinalIgnoreCase);
                if (te < 0) { return outp; }
                string tregion = region.Substring(0, te);
                string[] rows = System.Text.RegularExpressions.Regex.Split(tregion, "<tr", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                for (int r = 0; r < rows.Length; r++)
                {
                    string text = System.Text.RegularExpressions.Regex.Replace(rows[r], "<[^>]+>", " ");
                    text = System.Net.WebUtility.HtmlDecode(text);
                    text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
                    System.Text.RegularExpressions.Match no = System.Text.RegularExpressions.Regex.Match(text, "\\b(\\d{4,5})\\b");
                    if (!no.Success) { continue; }
                    System.Text.RegularExpressions.MatchCollection tmc = System.Text.RegularExpressions.Regex.Matches(text, "\\b(\\d{1,2}:\\d{2})\\b");
                    if (tmc.Count < 1) { continue; }
                    string arr = tmc[0].Groups[1].Value;
                    string dep = tmc.Count > 1 ? tmc[1].Groups[1].Value : arr;
                    string name = "";
                    System.Text.RegularExpressions.MatchCollection anchors = System.Text.RegularExpressions.Regex.Matches(
                        rows[r], "<a\\b[^>]*>(.*?)</a>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    for (int ai = 0; ai < anchors.Count; ai++)
                    {
                        string at = System.Text.RegularExpressions.Regex.Replace(anchors[ai].Groups[1].Value, "<[^>]+>", " ");
                        at = System.Net.WebUtility.HtmlDecode(at);
                        at = System.Text.RegularExpressions.Regex.Replace(at, "\\s+", " ").Trim();
                        if (at.Length >= 3 && System.Text.RegularExpressions.Regex.IsMatch(at, "[A-Za-z]")
                            && !System.Text.RegularExpressions.Regex.IsMatch(at, "^\\d{1,2}:\\d{2}$"))
                        { name = at; break; }
                    }
                    if (name.Length > 60) { name = name.Substring(0, 60).Trim(); }
                    outp.Add(new string[] { no.Groups[1].Value, name, arr, dep });
                }

                // v1.0.160.41 GUARD B (row-count sanity): belt-and-braces behind Guard A. A single
                // station lists tens of trains; the generic all-services page lists ~1454. Over the
                // ceiling we cannot trust the page identity, so return NOTHING rather than assert a
                // false "train at_station" on every alert.
                if (outp.Count > _trainCtxMaxTrains) { outp.Clear(); }
            }
            catch { }
            return outp;
        }


        internal static bool TryHHMM(string s, out int minutes)
        {
            minutes = -1;
            if (string.IsNullOrEmpty(s)) { return false; }
            string[] parts = s.Split(':');
            int h, m;
            if (parts.Length == 2 && int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)
                && h >= 0 && h < 24 && m >= 0 && m < 60) { minutes = h * 60 + m; return true; }
            return false;
        }


        // v1.0.160.33 (hardened 160.35): assemble the train_context block -- trains scheduled through the
        // station within +/- windowMin of the arrival..departure interval. Returns NULL when nothing parsed
        // (so a blocked/changed/invalid page is omitted, never a false "no train"). Running day is NOT
        // verified. SCHEDULED only; the note always states it is not confirmed passage.
        internal static JObject BuildTrainContext(string html, string stationCode, DateTime alertIst)
        {
            List<string[]> trains = ParseStationTrains(html, stationCode);
            // P1: if NOTHING parsed we cannot tell an empty/blocked/changed page from a station with
            // genuinely no trains -> return null so NO train_context is attached. A page that DID parse
            // trains but none in the window still yields a real scheduled=false below.
            if (trains.Count == 0) { return null; }
            JObject tc = new JObject();
            tc["station"] = stationCode;
            tc["alert_time"] = alertIst.ToString("HH:mm");
            tc["window_min"] = _trainCtxWindowMin;
            tc["running_day_checked"] = false;   // P1: booked timetable; the running day is NOT verified
            int alertMin = alertIst.Hour * 60 + alertIst.Minute;
            JArray matches = new JArray();
            string[] nearest = null; int nearestAbs = int.MaxValue; int nearestDelta = 0; string nearestPhase = null;
            for (int i = 0; i < trains.Count; i++)
            {
                int aMin, dMin;
                if (!TryHHMM(trains[i][2], out aMin)) { continue; }
                if (trains[i].Length < 4 || !TryHHMM(trains[i][3], out dMin)) { dMin = aMin; }
                int delta = SignedDeltaToInterval(alertMin, aMin, dMin);   // 0 = standing at the station
                if (Math.Abs(delta) <= _trainCtxWindowMin)
                {
                    string ph = TrainPhase(alertMin, aMin, dMin);   // v1.0.160.39: arriving | at_station | passed
                    JObject m = new JObject();
                    m["train_no"] = trains[i][0];
                    m["train_name"] = trains[i][1];
                    m["sched_time"] = trains[i][2];
                    m["dep_time"] = trains[i][3];
                    m["delta_min"] = delta;
                    m["phase"] = ph;   // v1.0.160.39
                    matches.Add(m);
                    if (Math.Abs(delta) < nearestAbs) { nearestAbs = Math.Abs(delta); nearestDelta = delta; nearest = trains[i]; nearestPhase = ph; }
                }
            }
            bool scheduled = matches.Count > 0;
            tc["scheduled"] = scheduled;
            if (nearest != null)
            {
                JObject n = new JObject();
                n["train_no"] = nearest[0]; n["train_name"] = nearest[1]; n["sched_time"] = nearest[2]; n["dep_time"] = nearest[3]; n["delta_min"] = nearestDelta; n["phase"] = nearestPhase;   // v1.0.160.39
                tc["nearest"] = n;
            }
            else { tc["nearest"] = null; }
            tc["matches"] = matches;

            // v1.0.160.47: last/next train REGARDLESS of the window. Full 24h wrap, so at 23:50 the
            // "next" train is the first of the following morning rather than nothing.
            string[] prevT = null; int prevGap = int.MaxValue;
            string[] nextT = null; int nextGap = int.MaxValue;
            for (int i = 0; i < trains.Count; i++)
            {
                int aMin2, dMin2;
                if (!TryHHMM(trains[i][2], out aMin2)) { continue; }
                if (trains[i].Length < 4 || !TryHHMM(trains[i][3], out dMin2)) { dMin2 = aMin2; }
                int since = ModFwd(alertMin - dMin2);   // minutes since it departed
                int until = ModFwd(aMin2 - alertMin);   // minutes until it arrives
                if (since > 0 && since < prevGap) { prevGap = since; prevT = trains[i]; }
                if (until > 0 && until < nextGap) { nextGap = until; nextT = trains[i]; }
            }
            if (prevT != null)
            {
                JObject p = new JObject();
                p["train_no"] = prevT[0]; p["train_name"] = prevT[1];
                p["sched_time"] = prevT[2]; p["dep_time"] = prevT[3];
                p["mins_since"] = prevGap; p["phase"] = "passed";
                tc["previous"] = p;
            }
            else { tc["previous"] = null; }
            if (nextT != null)
            {
                JObject nx = new JObject();
                nx["train_no"] = nextT[0]; nx["train_name"] = nextT[1];
                nx["sched_time"] = nextT[2]; nx["dep_time"] = nextT[3];
                nx["mins_until"] = nextGap; nx["phase"] = "arriving";
                tc["next"] = nx;
            }
            else { tc["next"] = null; }
            tc["trains_today"] = trains.Count;
            tc["note"] = scheduled
                ? "scheduled (booked timetable; running day NOT verified) through the station, NOT confirmed passage -- verify the running day and live status with section control; not authority to occupy the track"
                : "no train in the booked timetable within the window (running day NOT verified; scheduled data only) -- verify live with section control";
            // v1.0.160.47: previous/next are ORIENTATION ONLY. Only a WINDOW match (scheduled=true)
            // indicates a train could plausibly have been on the track at the alert minute; a train
            // hours away must NEVER be offered as the cause of a breach.
            tc["prev_next_note"] = "previous/next are the nearest scheduled trains OUTSIDE the match window -- traffic orientation only, NOT evidence that a train influenced this alert. Only scheduled=true (a train within the window) supports a shunt/occupation explanation.";
            return tc;
        }


        // v1.0.160.35: signed minutes from the alert to a train's arrival..departure interval, nearest-
        // midnight wrap. 0 = the alert falls inside the interval (train standing at the station); negative
        // = the train was at the station before the alert; positive = after. Covers a train already
        // standing when the alert fired, which arrival-only matching would miss.
        internal static int SignedDeltaToInterval(int alertMin, int aMin, int dMin)
        {
            int da = WrapDelta(aMin - alertMin);   // arrival vs alert (negative if arrival before alert)
            int dd = WrapDelta(dMin - alertMin);   // departure vs alert
            if (da <= 0 && dd >= 0) { return 0; }  // arrival <= alert <= departure -> standing
            return (Math.Abs(da) <= Math.Abs(dd)) ? da : dd;   // otherwise the nearer edge
        }


        // v1.0.160.47: forward distance in minutes over a full 24h clock (0..1439). WrapDelta clamps
        // to +/-720, which cannot express a gap over 12h -- at a quiet station the next train can be
        // further away than that (SAKHUN 20:01 -> 07:03 = 662 min, and sparser lines exceed 720).
        internal static int ModFwd(int d)
        {
            int m = d % 1440;
            if (m < 0) { m += 1440; }
            return m;
        }


        internal static int WrapDelta(int d)
        {
            if (d > 720) { d -= 1440; } else if (d < -720) { d += 1440; }
            return d;
        }


        // v1.0.160.39: label the alert position relative to a train's arrival..departure interval,
        // so the verdict reads "arriving"/"at_station"/"passed" without interpreting delta_min.
        //   arriving   = arrival is still ahead of the alert (train approaching)
        //   at_station = alert falls inside [arrival, departure] (dwelling, or passing through now)
        //   passed     = departure is behind the alert (train has gone through)
        internal static string TrainPhase(int alertMin, int aMin, int dMin)
        {
            int da = WrapDelta(aMin - alertMin);
            int dd = WrapDelta(dMin - alertMin);
            if (da <= 0 && dd >= 0) { return "at_station"; }
            if (da > 0) { return "arriving"; }
            return "passed";
        }


        // Reduce the hourly Open-Meteo payload to the hours around incidence and a compact,
        // model-readable summary. Returns null if nothing usable.
        internal static string SummariseWeather(string raw, DateTime incidenceIst)
        {
            try
            {
                JObject o = JObject.Parse(raw);
                JObject hourly = o["hourly"] as JObject;
                if (hourly == null) { return null; }
                JArray times = hourly["time"] as JArray;
                JArray precip = hourly["precipitation"] as JArray;
                JArray codes = hourly["weather_code"] as JArray;
                JArray temp = hourly["temperature_2m"] as JArray;
                if (times == null) { return null; }

                // window: incidence hour +/- 3h
                DateTime lo = incidenceIst.AddHours(-3), hi = incidenceIst.AddHours(3);
                double precipTotal = 0, peak = 0, tMin = double.MaxValue, tMax = double.MinValue;
                bool thunder = false; bool anyHour = false;
                // v1.0.160.49: keep the WMO code AT the incidence hour (what the sky actually was)
                // and the WORST code in the window (so a shower an hour either side is not lost).
                int wcAt = -1, wcWorst = -1; double bestGapH = double.MaxValue;
                for (int i = 0; i < times.Count; i++)
                {
                    DateTime ht;
                    if (!DateTime.TryParse(times[i] != null ? times[i].ToString() : "", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out ht)) { continue; }
                    if (ht < lo || ht > hi) { continue; }
                    anyHour = true;
                    if (precip != null && i < precip.Count) { double p; if (double.TryParse(precip[i].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out p)) { precipTotal += p; if (p > peak) { peak = p; } } }
                    if (codes != null && i < codes.Count)
                    {
                        int wc;
                        if (int.TryParse(codes[i].ToString(), out wc))
                        {
                            if (wc == 95 || wc == 96 || wc == 99) { thunder = true; }
                            if (wc > wcWorst) { wcWorst = wc; }
                            double gapH = Math.Abs((ht - incidenceIst).TotalHours);
                            if (gapH < bestGapH) { bestGapH = gapH; wcAt = wc; }
                        }
                    }
                    if (temp != null && i < temp.Count) { double t; if (double.TryParse(temp[i].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out t)) { if (t < tMin) { tMin = t; } if (t > tMax) { tMax = t; } } }
                }
                if (!anyHour) { return null; }

                JObject w = new JObject();
                w["window"] = "incidence +/- 3h (site-local)";
                w["rain"] = precipTotal > 0.1;
                w["precip_mm_total"] = Math.Round(precipTotal, 2);
                w["peak_precip_mm_h"] = Math.Round(peak, 2);
                w["thunderstorm"] = thunder;
                if (wcAt >= 0) { w["weather_code"] = wcAt; }          // v1.0.160.49: sky at the incidence hour
                if (wcWorst >= 0) { w["weather_code_worst"] = wcWorst; }
                if (tMin <= tMax) { w["temp_c_range"] = new JArray(Math.Round(tMin, 1), Math.Round(tMax, 1)); }
                return w.ToString(Formatting.None);
            }
            catch { return null; }
        }

        internal static bool IsOptionalEnrichment(string toolName)
        {
            for (int i = 0; i < _optionalTools.Length; i++)
            {
                if (string.Equals(toolName, _optionalTools[i], StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }


        internal async Task<string> ResolveIpsAssetIdAsync(string siteId, System.Threading.CancellationToken ct)
        {
            if (string.IsNullOrEmpty(siteId)) { return null; }
            string cached;
            if (Sess._ipsAssetBySite.TryGetValue(siteId, out cached)) { return cached; }
            string found = null;
            try
            {
                string res = await CallToolRoutedAsync("search_assets", NewArgs("SiteId", siteId), ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(res))
                {
                    JArray arr = JArray.Parse(res);
                    for (int i = 0; i < arr.Count; i++)
                    {
                        JObject a = arr[i] as JObject; if (a == null) { continue; }
                        JToken ty = a["AssetTypeId"];
                        int tid;
                        if (ty != null && int.TryParse(ty.ToString(), out tid) && tid == 39)
                        {
                            if (a["AssetId"] != null) { found = a["AssetId"].ToString(); break; }
                        }
                    }
                }
            }
            catch { found = null; }
            Sess._ipsAssetBySite[siteId] = found;
            return found;
        }


        internal async Task BackfillContextAsync(JObject ctx, System.Threading.CancellationToken ct)
        {
            if (ctx == null) { return; }
            string cause = GetCtx(ctx, "causeCode");
            string siteId = GetCtx(ctx, "siteId");
            string assetId = GetCtx(ctx, "assetId");
            // nothing to do if the cause is already known and both ids present
            if (!string.IsNullOrEmpty(cause) && !string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(assetId)) { return; }

            string station = GetCtx(ctx, "station");
            string assetName = GetCtx(ctx, "assetName");
            string alertId = GetCtx(ctx, "alertId");

            // 0) alertId-only path: fetch_alert_details_frs returns the alert row directly, so even a
            //    near-empty context (only alertId) can be recovered in ONE call. Try this first.
            if (!string.IsNullOrEmpty(alertId) && (string.IsNullOrEmpty(cause) || string.IsNullOrEmpty(siteId) || string.IsNullOrEmpty(assetId)))
            {
                try
                {
                    JObject dargs = NewArgs("AlertId", alertId);
                    string dres = await CallToolRoutedAsync("fetch_alert_details_frs", dargs, ct).ConfigureAwait(false);
                    JObject row = FirstAlertRow(dres);
                    if (row != null)
                    {
                        ApplyAlertRow(ctx, row);
                        cause = GetCtx(ctx, "causeCode"); siteId = GetCtx(ctx, "siteId"); assetId = GetCtx(ctx, "assetId");
                        station = GetCtx(ctx, "station"); assetName = GetCtx(ctx, "assetName");
                    }
                }
                catch { }
            }
            // if the by-id path already filled the cause and ids, we're done
            if (!string.IsNullOrEmpty(cause) && !string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(assetId))
            {
                AiLog("INFO", "BACKFILL", "context backfill (by alertId): cause='" + cause + "' site=" + siteId + " asset=" + assetId);
                return;
            }
            if (string.IsNullOrEmpty(assetName) && string.IsNullOrEmpty(station) && string.IsNullOrEmpty(siteId)) { return; }  // nothing left to resolve from

            // 1) siteId from station name
            if (string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(station))
            {
                try
                {
                    string sres = await CallToolRoutedAsync("search_sites", NewArgs("SiteName", station), ct).ConfigureAwait(false);
                    string sid = HarvestIdToken(sres, "SiteId") ?? HarvestIdToken(sres, "site_id");
                    if (!string.IsNullOrEmpty(sid)) { siteId = sid; ctx["siteId"] = sid; }
                }
                catch { }
            }
            // 2) assetId from asset name (+site)
            if (string.IsNullOrEmpty(assetId) && !string.IsNullOrEmpty(assetName))
            {
                try
                {
                    JObject aargs = NewArgs("AssetName", assetName);
                    if (!string.IsNullOrEmpty(siteId)) { aargs["SiteId"] = siteId; }
                    string ares = await CallToolRoutedAsync("search_assets", aargs, ct).ConfigureAwait(false);
                    string aid = HarvestIdToken(ares, "AssetId") ?? HarvestIdToken(ares, "asset_id");
                    if (!string.IsNullOrEmpty(aid)) { assetId = aid; ctx["assetId"] = aid; }
                    if (string.IsNullOrEmpty(siteId))
                    {
                        string sid2 = HarvestIdToken(ares, "SiteId") ?? HarvestIdToken(ares, "site_id");
                        if (!string.IsNullOrEmpty(sid2)) { siteId = sid2; ctx["siteId"] = sid2; }
                    }
                }
                catch { }
            }
            // 3) the FRS alert row around the incidence -> causeCode / alertType / description
            if (string.IsNullOrEmpty(cause) && !string.IsNullOrEmpty(siteId))
            {
                try
                {
                    DateTime incT;
                    if (!TryParseAlertTime(GetCtx(ctx, "time"), out incT)) { incT = DateTime.UtcNow; }
                    string fres = await CallToolRoutedAsync("get_frs_alerts", NewSiteAlertArgs(siteId, incT), ct).ConfigureAwait(false);
                    JObject fo = null;
                    try { fo = JObject.Parse(fres); } catch { }
                    JArray rows = fo != null ? fo["mFRSAlerts"] as JArray : null;
                    JObject pick = null;
                    if (rows != null)
                    {
                        long aidWant; long.TryParse(alertId, out aidWant);
                        foreach (JToken tk in rows)
                        {
                            JObject ro = tk as JObject; if (ro == null) { continue; }
                            long rid; long.TryParse(FirstStr(ro, "Id", "AlertId", "id"), out rid);
                            string raid = FirstStr(ro, "AssetId", "assetId");
                            if (aidWant > 0 && rid == aidWant) { pick = ro; break; }
                            if (pick == null && !string.IsNullOrEmpty(assetId) && raid == assetId) { pick = ro; }
                        }
                        if (pick == null && rows.Count > 0) { pick = rows[0] as JObject; }
                    }
                    if (pick != null) { ApplyAlertRow(ctx, pick); }
                }
                catch { }
            }
            AiLog("INFO", "BACKFILL", "context backfill: cause='" + GetCtx(ctx, "causeCode") + "' site=" + GetCtx(ctx, "siteId")
                + " asset=" + GetCtx(ctx, "assetId") + " (from station='" + station + "' assetName='" + assetName + "')");
        }


        // v1.0.160.190: pull the first alert row out of a tool result (fetch_alert_details_frs returns
        // either a bare array or { mFRSAlerts:[...] } / { data:[...] }).
        internal static JObject FirstAlertRow(string res)
        {
            if (string.IsNullOrEmpty(res)) { return null; }
            try
            {
                JToken t = JToken.Parse(res);
                JArray arr = t as JArray;
                if (arr == null && t is JObject)
                {
                    JObject o = (JObject)t;
                    arr = (o["mFRSAlerts"] as JArray) ?? (o["data"] as JArray) ?? (o["rows"] as JArray);
                    if (arr == null && (o["CauseCode"] != null || o["PossibleCause"] != null)) { return o; }
                }
                if (arr != null && arr.Count > 0) { return arr[0] as JObject; }
            }
            catch { }
            return null;
        }


        // v1.0.160.190: copy blank ctx fields from an FRS alert row (fills only what's missing).
        internal static void ApplyAlertRow(JObject ctx, JObject row)
        {
            if (ctx == null || row == null) { return; }
            if (GetCtx(ctx, "causeCode") == "") { string cc = FirstStr(row, "CauseCode", "PossibleCause", "causeCode", "cause_code"); if (!string.IsNullOrEmpty(cc)) ctx["causeCode"] = cc; }
            if (GetCtx(ctx, "siteId") == "") { string sid = FirstStr(row, "SiteId", "siteId", "site_id"); if (!string.IsNullOrEmpty(sid)) ctx["siteId"] = sid; }
            if (GetCtx(ctx, "assetId") == "") { string aid = FirstStr(row, "AssetId", "assetId", "asset_id"); if (!string.IsNullOrEmpty(aid)) ctx["assetId"] = aid; }
            if (GetCtx(ctx, "assetName") == "") { string an = FirstStr(row, "AssetName", "assetName"); if (!string.IsNullOrEmpty(an)) ctx["assetName"] = an; }
            if (GetCtx(ctx, "station") == "") { string sn = FirstStr(row, "SiteName", "siteName", "station"); if (!string.IsNullOrEmpty(sn)) ctx["station"] = sn; }
            if (GetCtx(ctx, "description") == "") { string ds = FirstStr(row, "Description", "description"); if (!string.IsNullOrEmpty(ds)) ctx["description"] = ds; }
            if (GetCtx(ctx, "time") == "" || GetCtx(ctx, "time") == "-") { string tm = FirstStr(row, "SetTimeStamp", "setTimeStamp", "time"); if (!string.IsNullOrEmpty(tm)) ctx["time"] = tm; }
            if (string.IsNullOrEmpty(GetCtx(ctx, "alertType")))
            {
                string cat = FirstStr(row, "AlertCategoryId", "alertCategoryId");
                if (cat == "1") ctx["alertType"] = "PREDICTIVE";
                else if (cat == "2") ctx["alertType"] = "FAILURE";
            }
        }


        // v1.0.160.195 (memory-loss B): rebuild the analyze input context from what was persisted at
        // analysis start (AlertInputJson holds the full input incl. the alert card). Read-only; reuses
        // the breaker-safe CacheGet (404 = miss, breaker untouched) and returns null on any miss so the
        // caller falls back to today's behaviour. IncludeJson=1 is required or the heavy JSON is stripped.
        internal static JObject LoadStoredAlertContext(JObject payload)
        {
            try
            {
                if (payload == null) { return null; }
                string alertId = payload["alertId"] != null ? payload["alertId"].ToString()
                                : (payload["AlertId"] != null ? payload["AlertId"].ToString() : "");
                long aid;
                if (string.IsNullOrEmpty(alertId) || !long.TryParse(alertId, out aid) || aid <= 0) { return null; }
                JArray rows = CacheGet("/api/AIAnalysis/Get?AlertId=" + aid + "&IncludeJson=1");
                if (rows == null || rows.Count == 0) { return null; }
                // rows are newest-first; take the newest that actually carries the stored input context.
                for (int i = 0; i < rows.Count; i++)
                {
                    JObject row = rows[i] as JObject;
                    if (row == null) { continue; }
                    JToken inTok = row["AlertInputJson"];
                    if (inTok == null || inTok.Type == JTokenType.Null) { continue; }
                    string inStr = inTok.ToString();
                    if (string.IsNullOrWhiteSpace(inStr) || inStr == "{}") { continue; }
                    try
                    {
                        JObject stored = JObject.Parse(inStr);
                        if (stored["alertCard"] is JObject || stored["causeCode"] != null) { return stored; }
                    }
                    catch { }
                }
                return null;
            }
            catch { return null; }
        }


        // The alert record carries the numeric ids; the board could not have rendered
        // the row otherwise. Presenting them removes the resolution chain entirely.
        // It cannot succeed anyway: the station is a site CODE ("SK") and search_sites
        // matches the site NAME ("SAKHUN"), and without a SiteId an asset-name search
        // is ambiguous because names repeat across stations.
        // ==================================================================
        // PRE-FETCH ENGINE
        // Phase 1 runs the independent lookups together; phase 2 needs a TagId
        // from phase 1, so it follows. Anything that fails is simply omitted --
        // pre-fetch NEVER blocks the analysis, it only saves turns.
        // ==================================================================
        internal async Task<string> PrefetchEvidenceAsync(JObject ctx, ToolLoadResult loaded, System.Threading.CancellationToken ct)
        {
            // v1.0.160.110 SL5: WALL-CLOCK for the whole prefetch phase. Distinct from
            // toolCallMsSum, which sums concurrent calls and can legitimately exceed this.
            var _swPrefetchPhase = System.Diagnostics.Stopwatch.StartNew();
            try
            {
            // v1.0.160.110 SL7: fill station/assetType from the alert card before anything reads
            // them. They were empty in the payload while alertCard carried stationName/stationCode
            // and the alert row carried AssetType -- and the FOLLOW-UP chat reuses this same ctx,
            // where there is no card to fall back on.
            EnrichContextGrounding(ctx);
            string assetId = GetCtx(ctx, "assetId");
            string siteId = GetCtx(ctx, "siteId");
            if (assetId.Length == 0) { AiLog("INFO", "PREFETCH", "no assetId in context -- skipped"); return null; }

            // narrowing needs the AssetId BEFORE any tool arg has supplied it
            if (Sess._jsonAssetHints == null) { Sess._jsonAssetHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
            if (Sess._prefetchDone == null) { Sess._prefetchDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
            Sess._jsonAssetHints.Add(assetId);
            Sess._prefetchAssetId = assetId;   // v1.0.160.166: the scope of every one-shot denial

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            bool gotTags = false, gotRange = false, gotTagIds = false, gotPred = false;

            bool gotAudit = false, gotAlerts = false;
            string histWhy = null;
            StringBuilder ev = new StringBuilder();
            ev.Append("=== PRE-FETCHED EVIDENCE ===").Append("\n");
            ev.Append("These steps are ALREADY DONE. The results are below. Do NOT repeat them - ");
            ev.Append("re-running a lookup whose answer is already here wastes a full turn and ");
            // -- v1.0.75.0 -- was: "Where a section says MISSING, that is the only
            // thing worth a tool call." -- contradicted the RETRY REQUIRED status.
            ev.Append("returns the same bytes. Where a section says MISSING or RETRY REQUIRED, ");
            ev.Append("those are the only things worth a tool call.").Append("\n\n");

            // v1.0.160.169 (BUG-3a/4 + 3b): pin the incidence-value verdict anchor at the TOP of the
            // evidence so the model judges the moment the value BREACHED, not the moment it recovered.
            ev.Append(BuildVerdictAnchor(ctx));
            ev.Append(BuildPmVerdictAnchor(ctx));   // v1.0.160.171: PM common-condition (4 steps)
            ev.Append(BuildRawMessageAnchor(ctx));   // v1.0.160.173: cardless / rawMessage alerts

            // v1.0.95.0: for a cause code that maps to MULTIPLE attributes, list the FULL
            // related set so the model checks coherence across all of them, not just the
            // anchor. Owner-confirmed: all mapped attributes matter (e.g. TC RAIL RES OPEN ->
            // Ir mA + If mA + RRAIL must be read together; TC BALST/SLPR RES LOW -> Ir + IBALST).
            // The anchor tag (attrs[0]) still drives the primary history fetch; this note tells
            // the model the coherence set to weigh alongside it.
            {
                string cc2 = GetCtx(ctx, "causeCode");
                if (!string.IsNullOrEmpty(cc2))
                {
                    string[] relAttrs;
                    if (RdpmsMaps.CauseToAttributes.TryGetValue(cc2.Trim(), out relAttrs)
                        && relAttrs != null && relAttrs.Length > 1)
                    {
                        ev.Append("RELATED ATTRIBUTES for this cause code (check coherence across those that have ");
                        ev.Append("timestamp-aligned values): ").Append(string.Join(", ", relAttrs));
                        ev.Append(". Evaluate coherence ONLY for related attributes that have incidence-aligned values ");
                        ev.Append("in the alert card, fault audit, or history evidence below. Missing related history is a ");
                        ev.Append("LIMITATION, not evidence that those attributes failed to move - do NOT use current or ");
                        ev.Append("untimestamped values to contradict an incidence-time trend on the alerting attribute. If the ");
                        ev.Append("anchor's aligned history settles the condition AND the alert's conditionLogic permits it, ");
                        ev.Append("a binary verdict is allowed - for AND logic every required branch must support CONFIRMED, ");
                        ev.Append("for OR logic one breached branch may support CONFIRMED; lower confidence and note in caveats ");
                        ev.Append("each required related condition left uncorroborated.").Append("\n\n");
                    }
                }
            }

            // ---- phase 1: independent lookups, in parallel ----
            Task<string> tagsT = PrefetchCallAsync("get_asset_tags",
                NewArgs("AssetId", assetId), loaded, ct);
            // v1.0.160.6: AssetId-scope get_attribute_range (see note at the plan builder) so the
            // baseline arrives complete (~3KB) instead of site-scoped-and-truncated (~266KB->12KB).
            JObject arArgs2 = NewArgs("SiteId", siteId);
            if (!string.IsNullOrWhiteSpace(assetId)) { arArgs2["AssetId"] = assetId; }
            Task<string> rangeT = siteId.Length > 0
                ? PrefetchCallAsync("get_attribute_range", arArgs2, loaded, ct)
                : null;
            // get_asset_tags returns AssetAttributeId and CURRENT VALUES but NO TagID.
            // history_get requires a TagId, and search_tags is the only tool that
            // returns one. So this call is not optional -- without it there is no
            // series, which is why the model kept calling search_tags no matter what
            // the prompt said.
            string assetName = GetCtx(ctx, "assetName");
            Task<string> stagsT = assetName.Length > 0
                ? PrefetchCallAsync("search_tags", LogDriverSet(NewSearchArgs(assetName, siteId, ctx, IsDerivedCard(ctx)), ctx), loaded, ct)
                : null;
            // IMPORTANT DISTINCTION: the prediction APIs are keyed by ASSET
            // (SiteId + AssetId), while EdgeX history is keyed by TAG. So the ML
            // analyses need nothing we do not already have from the alert record --
            // no tag resolution, no matching, no dependency on search_tags. Fetch
            // them here, in parallel, and they cost the model zero turns.
            string predTool = PredictToolFor(GetCtx(ctx, "causeCode"));
            Task<string> predT = (predTool != null && siteId.Length > 0)
                ? PrefetchCallAsync(predTool, NewPredictArgs(predTool, siteId, assetId), loaded, ct)
                : null;

            // These two cost the model a full turn each and need nothing it has to
            // discover: analyse_alert takes the station CODE and the alert facts
            // verbatim, get_frs_alerts takes the SiteId. Measured: fetching them
            // late cost 13.2s of model time across three extra turns.
            DateTime incT;
            bool haveInc = TryParseAlertTime(GetCtx(ctx, "time"), out incT);
            // v1.0.160.13: analyse_alert (srv2 FRS tool) DROPPED as an evidence source. Its only
            // unique output was cause_code_def (the FRS pass/fail rule); that rule is now embedded
            // locally in CauseLogicMaps.CauseToLogic and injected below - no srv2 round-trip, and the
            // cause-name-mismatch class that made analyse_alert miss 9 of 11 PM causes cannot occur.
            // (get_frs_alerts + get_hist_realtime remain on srv2 and are unaffected.)
            Task<string> alertsT = (siteId.Length > 0 && haveInc)
                ? PrefetchCallAsync("get_frs_alerts", NewSiteAlertArgs(siteId, incT), loaded, ct)
                : null;
            // v1.0.160.154 Layer A: the asset's OWN alert history, fetched in parallel. The +/-2h
            // call above answers "what else fired around this asset"; this answers "is this asset
            // a repeat offender". OFF by default (AssetAlertHistoryEnabled).
            Task<string> ahistT = (_assetHistEnabled && siteId.Length > 0 && assetId.Length > 0 && haveInc)
                ? PrefetchCallAsync("get_frs_alerts", NewAssetHistoryArgs(siteId, assetId, incT, _assetHistDays), loaded, ct)
                : null;
            // v1.0.117.0: get_attribute_range_history - asset-scoped historical AVG trend over the
            // evidence window PLUS the site geo/topology (lat/long/familyTracks/signalTrack). This
            // is the source of the site lat/long the controller-side weather fetch needs, and gives
            // the model a historical trend to complement get_attribute_range (LIVE/current normal)
            // and history_get (precise timestamped samples). Asset + ddMMyyyy_HHmmss window.
            Task<string> rangeHistT = (assetId.Length > 0 && haveInc)
                ? PrefetchCallAsync("get_attribute_range_history", NewRangeHistoryArgs(assetId, incT), loaded, ct)
                : null;

            string tags = await SafeAwait(tagsT).ConfigureAwait(false);
            // v1.0.160.112 SL17: the snapshot describes NOW, not the alert. Stamp it so nothing
            // downstream can read it as an at-incidence reading.
            if (!string.IsNullOrEmpty(tags))
            {
                Sess._snapshotRetrievedAt = DateTime.Now;
                Sess._snapshotValues = ParseSnapshotValues(tags);
            }
            string range = DropE7ApiFailure(rangeT != null ? await SafeAwait(rangeT).ConfigureAwait(false) : null, "get_attribute_range");
            // v1.0.160.135: fold AverageValue / MinFailValue into the snapshot now that the range
            // result exists. Kept as a separate pass rather than reordering the awaits -- the
            // prefetch is concurrent and moving an await to suit a display field would serialise it.
            MergeRangeIntoSnapshot(range);
            // v1.0.160.198: op V/C tags read 0 in get_asset_tags (the snapshot source); overwrite them
            // with the last-operation average from the live tag table so the FIRST render matches Refresh.
            // No-op for non-point assets (PmOpChannelForName returns 0 for every row) and on any fetch miss.
            {
                int _pmAid;
                if (!string.IsNullOrEmpty(assetId) && int.TryParse(assetId, out _pmAid)) { MergePmAvgIntoSnapshot(_pmAid); }
            }
            string stags = stagsT != null ? await SafeAwait(stagsT).ConfigureAwait(false) : null;
            string pred = DropE7ApiFailure(predT != null ? await SafeAwait(predT).ConfigureAwait(false) : null, predTool != null ? predTool : "predict");
            // v1.0.160.112 SL12: keep the RAW ML result for controller-side promotion. The model is
            // never asked to elevate it -- that is what left a Critical finding at the bottom of
            // caveats on 543194.
            Sess._predictRaw = pred;
            string alerts = DropE7ApiFailure(alertsT != null ? await SafeAwait(alertsT).ConfigureAwait(false) : null, "get_frs_alerts");
            // v1.0.160.150: state what the prefetch actually GOT. A run where get_attribute_range
            // and get_frs_alerts return nothing looks, downstream, exactly like a code regression:
            // no envelopes ("configured envelope unavailable"), no average markers, no assetType and
            // no resetTime -- so the panel says "Still active" on a cleared alert. Those are four
            // visible symptoms of one upstream fact, and nothing logged the fact.
            AiLog((string.IsNullOrEmpty(range) || string.IsNullOrEmpty(alerts)) ? "WARN" : "INFO", "PREFETCH",
                "inputs: tags=" + (tags == null ? 0 : tags.Length)
                + "B range=" + (range == null ? 0 : range.Length)
                + "B alerts=" + (alerts == null ? 0 : alerts.Length) + "B"
                + ((string.IsNullOrEmpty(range) || string.IsNullOrEmpty(alerts))
                   ? " -- MISSING INPUT: envelopes, averages, assetType and resetTime all depend on these"
                   : ""));
            // v1.0.160.123 SL5: alertCard has no assetType; the FRS row does. Fill it here, still
            // fill-only-when-empty, and only from the row whose Id matches THIS alert -- a
            // neighbouring alert on another asset would otherwise supply the wrong type.
            if (!string.IsNullOrEmpty(alerts) && GetCtx(ctx, "assetType").Length == 0)
            {
                try
                {
                    string wantId = GetCtx(ctx, "alertId");
                    JObject ao = JObject.Parse(alerts);
                    JArray rows = ao["mFRSAlerts"] as JArray;
                    if (rows != null && wantId.Length > 0)
                    {
                        for (int ri = 0; ri < rows.Count; ri++)
                        {
                            JObject r = rows[ri] as JObject;
                            if (r == null || r["Id"] == null) { continue; }
                            if (!string.Equals(r["Id"].ToString(), wantId, StringComparison.Ordinal)) { continue; }
                            JToken at = r["AssetType"];
                            if (at != null && at.ToString().Trim().Length > 0)
                            {
                                ctx["assetType"] = at.ToString().Trim();
                                AiLog("INFO", "CTX", "assetType filled from the FRS alert row: " + ctx["assetType"]);
                            }
                            // v1.0.160.130: and the RESET. The client had two optional sources for
                            // this and could end up with neither, announcing "Still active" on an
                            // alert the same page showed as cleared. This row is authoritative.
                            JToken rt = r["ResetTimeStamp"];
                            if (rt != null && rt.Type != JTokenType.Null && GetCtx(ctx, "resetTime").Length == 0)
                            {
                                DateTime rtd;
                                if (DateTime.TryParse(rt.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                                        System.Globalization.DateTimeStyles.None, out rtd))
                                {
                                    ctx["resetTime"] = rtd.ToString("dd/MM/yyyy HH:mm:ss");
                                    AiLog("INFO", "CTX", "resetTime filled from the FRS alert row: " + ctx["resetTime"]);
                                }
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
            string rangeHist = DropE7ApiFailure(rangeHistT != null ? await SafeAwait(rangeHistT).ConfigureAwait(false) : null, "get_attribute_range_history");
            // v1.0.160.154 Layer A: reduce the asset history to a compact summary -- never the raw
            // rows -- and speak it as ground truth. Failure (incl. an E7-API 403 via
            // DropE7ApiFailure) just leaves the chapter absent; this is context, not a required
            // input, so it never degrades the verdict on its own.
            string ahist = DropE7ApiFailure(ahistT != null ? await SafeAwait(ahistT).ConfigureAwait(false) : null, "get_frs_alerts(asset-history)");
            // v1.0.160.155: when the flag is ON, this block always speaks -- summary on success,
            // WARN with the head of the result otherwise. .154's silent null is how a schema
            // rejection ("SiteIds (int array) is required.") hid for a whole deploy cycle.
            if (_assetHistEnabled)
            {
                if (!string.IsNullOrEmpty(ahist) && LooksUsableToolResult(ahist, false))
                {
                    Sess._assetHistory = BuildAssetHistory(ahist, GetCtx(ctx, "alertId"), GetCtx(ctx, "causeCode"), _assetHistDays);
                }
                if (Sess._assetHistory != null)
                {
                    ev.Append(FormatAssetHistoryEvidence(Sess._assetHistory)).Append("\n\n");
                    AiLog("INFO", "PREFETCH", "asset-history: total=" + Sess._assetHistory["total"]
                        + " sameCause=" + Sess._assetHistory["sameCause"]
                        + " causes=" + (((JObject)Sess._assetHistory["byCause"]) != null ? ((JObject)Sess._assetHistory["byCause"]).Count : 0)
                        + " over " + _assetHistDays + "d");
                    // v1.0.160.162: now that Layer A has landed, refresh the greeting WITH the
                    // synopsis line. The client replaces the header text in place.
                    if (_greetingEnabled)
                    {
                        try
                        {
                            string g2 = BuildGreeting(ctx, true);
                            if (!string.IsNullOrEmpty(g2)) { WriteSse("greeting", new { text = g2 }); }
                        }
                        catch { }
                    }
                }
                else
                {
                    string ahHead = ahist == null ? "(no result -- task not started or E7-API failure already warned)" : ahist.Trim();
                    if (ahHead.Length > 120)
                    {
                        ahHead = ahHead.Substring(0, 120);
                    }
                    AiLog("WARN", "PREFETCH", "asset-history: enabled but nothing usable -- " + ahHead);
                }
            }

            if (!string.IsNullOrEmpty(tags) && LooksUsableToolResult(tags, false))
            {
                gotTags = true; MarkPrefetched("get_asset_tags");
                // v1.0.160.72: no longer points at history_get(AssetId) -- see the note above.
                ev.Append("[ASSET TAGS - attribute names, AssetAttributeId and current values (NO TagID; get TagIDs from search_tags, then one history_get per TagId -- never history_get(AssetId))]").Append("\n");
                // v1.0.160.110: strip the bulky -Array waveform CurrentValues (attid 1001/2001/... on a
                // point machine) from the MODEL PROMPT only, so the relay-state row is not pushed off the
                // 4000-char budget. StripTagWaveforms returns a COPY -- `tags` is untouched, so the raw
                // waveform stays available to the chart and predict_pm_operation. search_tags already
                // gets this treatment.
                ev.Append(TrimForPrompt(StripTagWaveforms(tags), 4000)).Append("\n\n");
            }

            // v1.0.152.5 (P0): FORCE-FETCH the digital TPR STATE history for TRACK assets so the
            // deterministic train mask has data. Moved OUTSIDE the get_asset_tags-usable block --
            // the TPR TagID comes from search_tags, so this must run even if get_asset_tags failed.
            // Best-effort: failure just leaves the mask "unavailable", never blocks the verdict.
            try
            {
                string atc = (GetCtx(ctx, "assetType") ?? "").ToUpperInvariant();
                string ccc = (GetCtx(ctx, "causeCode") ?? "").ToUpperInvariant();
                bool trackAsset = atc.Contains("TRACK") || ccc.StartsWith("TC ") || ccc.Contains("RAIL RES");
                if (trackAsset && haveInc)
                {
                    // get_asset_tags carries NO TagID -- resolve TPR from search_tags. Try EXACT
                    // digital-state names first (v1.0.152.5): a broad Contains("TPR") can otherwise
                    // grab the analog "TPR V". stags is already unfiltered for derived/track cards;
                    // fall back to a fresh unfiltered AssetId search_tags for a narrowed direct case.
                    string tprSrc = stags;
                    string tprTag = ResolveTprTag(tprSrc, assetId, false);   // v1.0.152.7: stags is name-scoped -> fail-closed
                    if (tprTag == null && assetId.Length > 0)
                    {
                        JObject tprSearchArgs = NewArgs("AssetId", assetId); tprSearchArgs["Limit"] = 50;   // v1.0.152.6: search_tags defaults to 3 rows -- ask for the asset's full set (server cap 50)
                        string tprAll = await SafeAwait(PrefetchCallAsync("search_tags", tprSearchArgs, loaded, ct)).ConfigureAwait(false);
                        tprTag = ResolveTprTag(tprAll, assetId, true);   // v1.0.152.7: AssetId-scoped fallback -> a row missing AssetId is acceptable
                    }
                    if (tprTag != null)
                    {
                        RegisterTagMeta(tprTag, "TPR", true, false);   // the mask needs this identity
                        // v1.0.152.5: history API caps at 500 regardless; digital state records only
                        // CHANGES, so 500 changes span a long window.
                        JObject tprArgs = HistWindowArgs("TagId", tprTag,
                            incT.AddDays(-_evidenceWindowDays), incT.AddHours(2),
                            _limitCapEvidence, "columnar");
                        string tprHist = await HistFetchAsync(tprArgs, loaded, ct).ConfigureAwait(false);
                        // v1.0.160.120 SL1: force-fetch BOTH currents on a track asset. K2 needs feed
                        // AND relay; before this it got whatever the alert's own formula happened to
                        // pull, so IR MA reached the harvest ONCE in 135 runs and K2 skipped 90% of
                        // the time. HistFetchAsync dedupes, so a series the formula already fetched
                        // costs nothing here.
                        string[] k2Names = new string[] { "If mA", "Ir mA" };
                        for (int ki = 0; ki < k2Names.Length; ki++)
                        {
                            string k2Tag = TagIdForAttributeNameExactForAsset(tprSrc, k2Names[ki], assetId, true);
                            if (string.IsNullOrEmpty(k2Tag)) { continue; }
                            RegisterTagMeta(k2Tag, k2Names[ki], false, false);
                            JObject k2Args = HistWindowArgs("TagId", k2Tag,
                                incT.AddDays(-_evidenceWindowDays), incT.AddHours(2),
                                _limitCapEvidence, "columnar");
                            string k2Hist = await HistFetchAsync(k2Args, loaded, ct).ConfigureAwait(false);
                            AiLog("INFO", "CIRCUIT", "K2 input " + k2Names[ki] + " tag=" + k2Tag
                                + " (bytes=" + (k2Hist != null ? k2Hist.Length : 0) + ")");
                        }
                        AiLog("INFO", "TREND", "force-fetched TPR state tag=" + tprTag + " for track mask (bytes=" + (tprHist != null ? tprHist.Length : 0) + ")");
                        // v1.0.160.108: surface the force-fetched digital TPR STATE series into the model
                        // evidence (previously it fed only the train mask, so the model reported "No
                        // fetched TPR datalogger" and softened a card-confirmed relay-state FAILURE to
                        // INCONCLUSIVE). Labelled + authoritative for relay-state FAILUREs.
                        if (!string.IsNullOrEmpty(tprHist) && LooksUsableToolResult(tprHist, IsEvidenceTool("history_get")))
                        {
                            MarkPrefetched("history_get");
                            ev.Append("[DIGITAL TPR STATE (DataLogger, attid 6) history around incidence - 0=DROP/occupied, 1=PICK/clear. AUTHORITATIVE relay-state series for the event; a validly-fired relay-state FAILURE is CONFIRMED even when the analog partners (Ir/If/TPR V) look healthy.]").Append("\n");
                            ev.Append(TrimForPrompt(tprHist, 2000)).Append("\n\n");
                        }
                    }
                    else { AiLog("INFO", "TREND", "track asset but no TPR digital tag resolved -> mask may be unavailable"); }

                    // v1.0.160.69: fetch history for EVERY resolved DRIVER, not just the alerting tag.
                    // 160.55 stopped NewSearchArgs narrowing the TAG SEARCH, but the history plan was
                    // never extended -- the live trace showed exactly two history_get calls (TPR and
                    // ITC RELAY END) while driverSet already read "IR MA, IF MA". Resolving a driver
                    // set and then not fetching it is why every K2 fix so far changed nothing: the
                    // residual needs If AND Ir series, and If was never requested.
                    try
                    {
                        List<string> drv = DriverSetForCause(ctx);
                        // v1.0.160.80: the canonical name of the FAILING condition -- the attribute the
                        // alerting path will fetch. Derived from the card here because no variable in
                        // this scope holds it (checked; inventing one was the first attempt and it did
                        // not compile).
                        string _alertAttrCanon = FirstFailingParamCanon(ctx);
                        for (int di = 0; di < drv.Count; di++)
                        {
                            string dn = drv[di];
                            if (string.IsNullOrEmpty(dn)) { continue; }
                            // v1.0.160.80: skip the ALERTING attribute -- the alerting-tag fetch below
                            // pulls it anyway, WITH the coverage and truncation checks this loop does
                            // not run. Fetching it here too produced two identical calls per analysis.
                            if (!string.IsNullOrEmpty(_alertAttrCanon)
                                && NormAttrKey(dn) == NormAttrKey(_alertAttrCanon))
                            {
                                AiLog("INFO", "TREND", "driver " + dn + " is the alerting attribute -- left to the alerting path, not duplicated here");
                                continue;
                            }
                            bool have = false;
                            foreach (KeyValuePair<string, List<object[]>> hk in Sess._trendRawByAttr)
                            { if (NormAttrKey(hk.Key) == NormAttrKey(dn)) { have = true; break; } }
                            if (have) { continue; }   // already harvested (unit-suffix aware)
                            string dTag = TagIdForAttributeNameExactForAsset(tprSrc, dn, assetId, true);
                            if (string.IsNullOrEmpty(dTag))
                            {
                                // v1.0.160.186 (#2): IPS drivers (VIPS.../IIPS...) live on the site's
                                // IPS asset (AssetTypeId 39), not the alerting asset, so they resolved
                                // empty and were dropped -- e.g. VIPS DC R EXT on TC TR CONTACT RES HIGH.
                                // Resolve cross-asset when the card carries the term. Card-gated + IPS
                                // asset cached per site => no fan-out. Only this dropped branch changes.
                                if (_ipsCrossAssetEnabled)
                                {
                                    string _du = (dn ?? "").TrimStart().ToUpperInvariant();
                                    if ((_du.StartsWith("VIPS") || _du.StartsWith("IIPS")) && CardMentionsDriver(ctx, dn))
                                    {
                                        try
                                        {
                                            string ipsAssetId = await ResolveIpsAssetIdAsync(GetCtx(ctx, "siteId"), ct).ConfigureAwait(false);
                                            if (!string.IsNullOrEmpty(ipsAssetId))
                                            {
                                                JObject ipsArgs = NewArgs("AssetId", ipsAssetId); ipsArgs["Limit"] = 50;
                                                string ipsTags = await CallToolRoutedAsync("search_tags", ipsArgs, ct).ConfigureAwait(false);
                                                string ipsTag = TagIdForAttributeNameExactForAsset(ipsTags, dn, ipsAssetId, true);
                                                if (!string.IsNullOrEmpty(ipsTag))
                                                {
                                                    RegisterTagMeta(ipsTag, dn, false, false);
                                                    JObject ipsHistArgs = HistWindowArgs("TagId", ipsTag,
                                                        incT.AddDays(-_evidenceWindowDays), incT.AddHours(2),
                                                        _limitCapEvidence, "columnar");
                                                    string ipsHist = await HistFetchAsync(ipsHistArgs, loaded, ct).ConfigureAwait(false);
                                                    AiLog("INFO", "TREND", "IPS cross-asset driver " + dn + " ips=" + ipsAssetId
                                                        + " tag=" + ipsTag + " (bytes=" + (ipsHist != null ? ipsHist.Length : 0) + ")");
                                                }
                                                else { AiLog("INFO", "TREND", "IPS cross-asset driver " + dn + " not found on IPS asset " + ipsAssetId); }
                                            }
                                            else { AiLog("INFO", "TREND", "IPS cross-asset driver " + dn + " -- no IPS asset (type 39) at site"); }
                                        }
                                        catch (Exception ipx) { AiLog("WARN", "TREND", "IPS cross-asset fetch skipped: " + SanitizeJsonError(ipx.Message)); }
                                    }
                                }
                                continue;        // derived names / unresolved non-IPS have no tag
                            }
                            RegisterTagMeta(dTag, dn, false, false);
                            JObject dArgs = HistWindowArgs("TagId", dTag,
                                incT.AddDays(-_evidenceWindowDays), incT.AddHours(2),
                                _limitCapEvidence, "columnar");
                            string dHist = await HistFetchAsync(dArgs, loaded, ct).ConfigureAwait(false);
                            AiLog("INFO", "TREND", "driver history " + dn + " tag=" + dTag
                                + " (bytes=" + (dHist != null ? dHist.Length : 0) + ")");
                        }
                    }
                    catch (Exception dex) { AiLog("WARN", "TREND", "driver history fetch skipped: " + SanitizeJsonError(dex.Message)); }
                }
            }
            catch (Exception exTpr) { AiLog("WARN", "TREND", "TPR force-fetch failed (mask falls back): " + SanitizeJsonError(exTpr.Message)); }

            string panelAssetRange = null;   // v1.0.160.104: retained ASSET-narrowed range (compact rows a/t/min/max/fail) for the envelope lookup
            if (!string.IsNullOrEmpty(range))
            {
                string narrowed = NarrowAttributeRange("get_attribute_range", range);
                panelAssetRange = narrowed;   // v1.0.160.104: use the asset-narrowed range for RangeBoundsForAsset
                if (LooksUsableToolResult(narrowed, IsEvidenceTool("get_attribute_range")))
                {
                    gotRange = true; MarkPrefetched("get_attribute_range");
                    ev.Append("[BASELINE - safe/average/fail limits for this asset]").Append("\n");
                    ev.Append(TrimForPrompt(narrowed, 3000)).Append("\n\n");
                }
            }
            // v1.0.117.0: historical AVG trend + geo for THIS asset (get_attribute_range_history).
            // Complements the LIVE baseline above with a trend over the evidence window; also the
            // source of the site lat/long used for weather (extracted below).
            if (!string.IsNullOrEmpty(rangeHist) && LooksUsableToolResult(rangeHist, IsEvidenceTool("get_attribute_range_history")))
            {
                MarkPrefetched("get_attribute_range_history");
                ev.Append("[HISTORICAL AVG TREND for this asset over the window + site geo/topology - ")
                  .Append("bucketed averages (NOT timestamped samples; use history evidence for exact-time values)]").Append("\n");
                ev.Append(TrimForPrompt(rangeHist, 3500)).Append("\n\n");
            }
            // -- v1.0.80.0 -- DEGRADED baseline fallback. When get_attribute_range
            // returns nothing usable (currently: the MCP server calls the E7 API
            // without Basic auth, so FRSAttributeRange returns a non-JSON auth page),
            // surface the alerting attribute's thresholds that the alert CARD already
            // carries, instead of leaving the model with no baseline and forcing it to
            // waste a turn re-calling the broken tool. Partial by nature (the card has
            // only the triggered attribute's MinSafe + percent-Avg, not MaxSafe/MinFail/
            // true-Average, and only for the one triggered attribute), so it is clearly
            // labelled DEGRADED. Fires ONLY when the tool yielded nothing, so it is
            // inert once the server auth fix lands and get_attribute_range returns JSON.
            if (!gotRange)
            {
                string cardBaseline = BuildCardBaseline(ctx);
                if (!string.IsNullOrEmpty(cardBaseline))
                {
                    gotRange = true;   // baseline now satisfied from the card
                    ev.Append("[BASELINE (DEGRADED - from alert card; get_attribute_range unavailable) - thresholds for the ALERTING attribute only]").Append("\n");
                    ev.Append(cardBaseline).Append("\n\n");
                    AiLog("INFO", "PREFETCH", "baseline from alert card (get_attribute_range unavailable)");
                }
            }

            // v1.0.123.0: POINT OPERATION prefetch. For a TIME cause the owner spec is exactly
            // three tags: the operation-TIME of the alerting END (A/B from the description) + the
            // two proving relays WCR & WKR (they prove the operation happened and completed). For
            // the other operation causes (VOLT/CURR/OBS) the map-based op attids are used. All
            // resolved op attid/relay attid -> TagId via the already-fetched search_tags, then
            // history_get. Detection-relay/indication point causes stay on the DB path. Best-effort.
            if (haveInc && !string.IsNullOrEmpty(stags))
            {
                string causeCode = GetCtx(ctx, "causeCode");
                // v1.0.124.0: unified per-cause tag spec - each entry [attid, alt, label]. Covers
                // ALL point causes (op tags + relays + per-END indication), end resolved from the
                // description. VPT-110/IPT/VIPS-110 params resolve via the existing mapping.
                List<string[]> want = PmTagsForCause(causeCode, PmEndFromAlert(ctx));
                if (want.Count > 0)
                {
                    DateTime pmSdT = incT.AddDays(-_evidenceWindowDays);
                    DateTime pmEdT = incT.AddHours(2);
                    List<Task<string>> opTasks = new List<Task<string>>();
                    List<string> opLabels = new List<string>();
                    for (int i = 0; i < want.Count; i++)
                    {
                        string attid = want[i][0]; string alt = want[i][1]; string label = want[i][2];
                        // resolve by AssetAttributeId; fall back to the alternate attid (e.g. NWCR 282->284)
                        string opTagId = PickTagIdByAttrId(stags, assetId, attid);
                        if (string.IsNullOrEmpty(opTagId) && !string.IsNullOrEmpty(alt))
                        { opTagId = PickTagIdByAttrId(stags, assetId, alt); }
                        if (string.IsNullOrEmpty(opTagId)) { continue; }
                        opTasks.Add(HistFetchAsync(HistWindowArgs("TagId", opTagId, pmSdT, pmEdT, _limitCapEvidence, "columnar"), loaded, ct));
                        // v1.0.160.6: point-machine ranges live under a DIFFERENT (RDPMS) attid than
                        // the operation VALUE attid -- surface the range-attid so the model reads the
                        // Min/MaxSafe from the correct row (value 2002 -> range 216 "A End - NW-V").
                        string rngAttid = PmRangeAttidFor(attid);
                        string rngHint = (!string.Equals(rngAttid, attid, StringComparison.Ordinal))
                            ? (", range attid " + rngAttid) : "";
                        opLabels.Add(label + " [attid " + attid + rngHint + ", TagId " + opTagId + "]");
                    }
                    if (opTasks.Count > 0)
                    {
                        ev.Append("[POINT OPERATION / RELAY / INDICATION history around incidence - the tags this ")
                          .Append("cause needs. For OPERATION-TIME: op-time vs 150% avg - HIGH with the WCR/WKR relays ")
                          .Append("showing a proper operation = mechanical binding/friction; HIGH with abnormal WCR/WKR ")
                          .Append("= operation did not complete cleanly; >6000ms = possible obstruction (WCR proves the ")
                          .Append("throw ran op<=12s, WKR proves it completed <=2s). For CURRENT/VOLTAGE: judge vs the ")
                          .Append("baseline band. For INDICATION/RELAY-DEFECT: check the relay transitions + indication ")
                          .Append("voltage vs Min-Safe. Per-operation data; compare A vs B end to localize. ")
                          .Append("NOTE: a point-machine operation value's Min/MaxSafe RANGE is stored in the FRS range ")
                          .Append("DB under the 'range attid' shown in each tag label (a different RDPMS attid than the ")
                          .Append("value attid) -- read the threshold from that range row, and also use the alert card's ")
                          .Append("configured limit which is authoritative for the specific alert.]").Append("\n");
                        for (int i = 0; i < opTasks.Count; i++)
                        {
                            string series = await SafeAwait(opTasks[i]).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(series) && LooksUsableToolResult(series, IsEvidenceTool("history_get")))
                            {
                                MarkPrefetched("history_get");
                                ev.Append("[").Append(opLabels[i]).Append("]").Append("\n");
                                ev.Append(TrimForPrompt(series, 2000)).Append("\n\n");
                            }
                        }
                        AiLog("INFO", "PREFETCH", "point operation history prefetched (" + opTasks.Count + " tags: op-time + relays as applicable)");

                        // v1.0.160.133: THE STROKE ITSELF. The block above spans two days so the
                        // model can see the operating band; it cannot show the shape of one throw.
                        // Re-fetch the same tags across the seconds around incidence -- that series
                        // IS the current signature, and no summary contains it.
                        try
                        {
                            DateTime sigSd = incT.AddSeconds(-90);
                            DateTime sigEd = incT.AddSeconds(150);
                            List<Task<string>> sigTasks = new List<Task<string>>();
                            List<string> sigLabels = new List<string>();
                            for (int i = 0; i < want.Count; i++)
                            {
                                string sAttid = want[i][0]; string sAlt = want[i][1]; string sLabel = want[i][2];
                                string sTag = PickTagIdByAttrId(stags, assetId, sAttid);
                                if (string.IsNullOrEmpty(sTag) && !string.IsNullOrEmpty(sAlt))
                                { sTag = PickTagIdByAttrId(stags, assetId, sAlt); }
                                if (string.IsNullOrEmpty(sTag)) { continue; }
                                sigTasks.Add(HistFetchAsync(HistWindowArgs("TagId", sTag, sigSd, sigEd, 500, "columnar"), loaded, ct));
                                sigLabels.Add(sLabel + " [stroke, TagId " + sTag + "]");
                            }
                            if (sigTasks.Count > 0)
                            {
                                StringBuilder sigSb = new StringBuilder();
                                int sigWith = 0;
                                for (int i = 0; i < sigTasks.Count; i++)
                                {
                                    string sSeries = await SafeAwait(sigTasks[i]).ConfigureAwait(false);
                                    if (string.IsNullOrEmpty(sSeries) || !LooksUsableToolResult(sSeries, IsEvidenceTool("history_get"))) { continue; }
                                    MarkPrefetched("history_get");
                                    sigSb.Append("[").Append(sigLabels[i]).Append("]").Append("\n");
                                    sigSb.Append(TrimForPrompt(sSeries, 1500)).Append("\n\n");
                                    sigWith++;
                                }
                                if (sigWith > 0)
                                {
                                    ev.Append("[CURRENT SIGNATURE - the same tags across the SECONDS around the incidence ")
                                      .Append("(-90s to +150s), which is the stroke itself. This is the only block that ")
                                      .Append("contains the SHAPE of the throw: where in the stroke the current peaks, how ")
                                      .Append("long it stays there, and whether it falls away cleanly at cut-off. Read it ")
                                      .Append("as MEASURED evidence and cite it as [fetched]. A high flat plateau through ")
                                      .Append("the stroke is obstruction; a slow rise with a late peak is friction or ")
                                      .Append("creeping; a normal peak that does not drop at the end is a cut-off problem. ")
                                      .Append("Any label from predict_pm_* is a CLASSIFICATION, not a measurement -- if it ")
                                      .Append("disagrees with this series, say so and prefer what was measured. If this ")
                                      .Append("block is absent or empty, say the signature was not captured; do not infer ")
                                      .Append("a stroke shape from averages.]").Append("\n");
                                    ev.Append(sigSb.ToString());
                                    AiLog("INFO", "PREFETCH", "stroke signature prefetched (" + sigWith + " of " + sigTasks.Count + " tags, -90s..+150s)");
                                }
                                else
                                {
                                    AiLog("WARN", "PREFETCH", "stroke signature EMPTY for all " + sigTasks.Count + " tags around incidence");
                                }
                            }
                        }
                        catch (Exception exSig)
                        {
                            AiLog("WARN", "PREFETCH", "stroke signature fetch failed: " + exSig.GetType().Name);
                        }
                        // v1.0.160.22: for a FAIL UNKNOWN cause, the alert generator could not pin a
                        // specific cause -- so the operation values above (110V, current, op-time) are
                        // provided precisely so the model can RECLASSIFY. Tell it to try.
                        if (causeCode != null && causeCode.ToUpperInvariant().Contains("UNKNOWN"))
                        {
                            ev.Append("[UNKNOWN-CAUSE RECLASSIFICATION: this alert is tagged FAIL UNKNOWN -- the generator ")
                              .Append("could not identify the specific cause. Examine the operation values above against the ")
                              .Append("FRS criteria and, if one clearly breaches, NAME the real cause: 110V loc below Min-safe ")
                              .Append("-> PT VOLT/CURR LOW (or FAIL); current abnormal -> PT VOLT/CURR LOW/FAIL; op-time > 150% ")
                              .Append("avg or > 6000ms with proper WCR/WKR -> PT OBS. If none breaches and the relays show a ")
                              .Append("clean operation, UNKNOWN stands. State which cause the evidence actually supports.]").Append("\n\n");
                        }
                        // v1.0.160.8: for point machines the tags above are already resolved by
                        // AssetId+AssetAttributeId (attid) and their history is fetched. Tell the model
                        // NOT to call search_tags by AttributeName -- PM tags are named by NUMBER
                        // ("NNNNN-PM-02002-Avg"), so a human-name search ("RTC VAR RES", "A TPT N",
                        // "ITC RELAY END") returns "No tags found" and wastes a call. Everything it
                        // needs is in the resolved tags + history already provided.
                        ev.Append("[RESOLUTION NOTE: the point-machine tags above are already resolved by ")
                          .Append("AssetId + attid and their history is included. Do NOT call search_tags by ")
                          .Append("AttributeName for this asset -- these tags are named by number, so a name ")
                          .Append("search returns no rows. Use the TagIds already listed; if you need another ")
                          .Append("attribute, request it by its attid, not its human name.]").Append("\n\n");
                    }
                }
            }

            // v1.0.117.0: WEATHER (Option A) - controller fetches it, not the MCP server. The site
            // lat/long comes from the get_attribute_range_history response (top-level latitude/
            // longitude); fetch only for weather-relevant causes and only when we have coordinates
            // + incidence. Best-effort: any failure leaves no weather block, model never mentions it.
            if (_weatherEnabled && haveInc && !string.IsNullOrEmpty(rangeHist)
                && IsWeatherRelevantCause(GetCtx(ctx, "causeCode")))
            {
                double wLat, wLon;
                if (TryExtractLatLon(rangeHist, out wLat, out wLon))
                {
                    string weather = await SafeAwait(FetchWeatherAsync(wLat, wLon, incT, ct)).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(weather))
                    {
                        ev.Append("[WEATHER at the site around incidence - CORROBORATIVE. rain=true supports ")
                          .Append("ballast/leakage/drainage; thunderstorm=true supports surge/multi-asset. Absence ")
                          .Append("of rain does NOT clear a fault.]").Append("\n");
                        ev.Append(weather).Append("\n\n");
                        // v1.0.119.0: also keep a card-shaped weather object so the verdict's WEATHER
                        // tile renders the real data (the card reads v.weather {condition,tempC,summary}).
                        // Attached to the verdict server-side (weather is a server-only key).
                        Sess._weatherCard = BuildWeatherCard(weather);
                        AiLog("INFO", "PREFETCH", "weather injected (controller-side, Option A)");
                    }
                }
            }

            // v1.0.160.33: TRAIN-CONTEXT -- scheduled trains through the alert STATION around the alert
            // time. Direct HTTP to a public timetable, parsed controller-side. Best-effort: any failure
            // (egress/timeout/parse) leaves no train_context and never touches the verdict. SCHEDULED,
            // not confirmed passage. Gated OFF by default (TrainContextEnabled).
            // v1.0.160.134: say which exit was taken, always.
            if (!_trainCtxEnabled)
            {
                AiLog("INFO", "PREFETCH", "train-context OFF (TrainContextEnabled is false)");
            }
            else if (!haveInc)
            {
                AiLog("INFO", "PREFETCH", "train-context skipped: incidence time unresolved");
            }
            if (_trainCtxEnabled && haveInc)
            {
                string stn = ResolveStationCode(ctx, rangeHist);   // v1.0.160.46
                if (string.IsNullOrEmpty(stn)) { AiLog("INFO", "PREFETCH", "train-context skipped: no station code in ctx or range-history"); }
                if (!string.IsNullOrEmpty(stn))
                {
                    string trHtml = await SafeAwait(FetchStationTrainsAsync(stn, ct)).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(trHtml))
                    {
                        // The most likely cause in production is egress: this is a direct call to a
                        // public timetable from the app server. Name it so nobody has to guess.
                        AiLog("WARN", "PREFETCH", "train-context: no page returned for station " + stn
                            + " (timeout, egress blocked, or site unreachable) -- base "
                            + Trunc(_trainCtxBaseUrl, 60));
                    }
                    if (!string.IsNullOrEmpty(trHtml))
                    {
                        JObject tcx = BuildTrainContext(trHtml, stn.Trim().ToUpperInvariant(), incT);
                        if (tcx != null) { Sess._trainContext = tcx; AiLog("INFO", "PREFETCH", "train-context injected (controller-side, totaltraininfo)"); }
                        else { AiLog("WARN", "PREFETCH", "train-context: station " + stn + " page fetched (" + trHtml.Length + " chars) but no trains parsed"); }
                    }
                }
            }

            // v1.0.160.13: FRS cause rule from the embedded RDPMS master (CauseLogicMaps.CauseToLogic),
            // replacing the srv2 analyse_alert fault audit. Keyed by the exact master cause name, so a
            // direct lookup hits for every authored cause. Thresholds (Min-safe/Min-fail/%avg) are
            // resolved by the model from the range evidence already gathered above.
            // v1.0.160.50 PHASE 2: the K2 discriminator, as one [circuit] evidence line.
            string _circRes = BuildCircuitResidualLine();
            if (!string.IsNullOrEmpty(_circRes))
            {
                ev.Append("[CIRCUIT INVARIANT - which side of the circuit actually moved. Corroboration only: it never overturns a card-confirmed breach]").Append("\n");
                ev.Append(_circRes).Append("\n\n");
                AiLog("INFO", "PREFETCH", "circuit residual injected: " + Trunc(_circRes, 160));
            }

            string frsRule;
            if (CauseLogicMaps.CauseToLogic.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out frsRule)
                && !string.IsNullOrEmpty(frsRule))
            {
                ev.Append("[FRS CAUSE RULE - authoritative pass/fail logic for this cause (RDSO/SPN/257/2025, RDPMS master)]").Append("\n");
                ev.Append(frsRule).Append("\n");
                ev.Append("Threshold resolution: use avg x LD% when an average is present in the range; otherwise fall back to MinSafe (predictive) / MinFail (failure).").Append("\n\n");
                gotAudit = true; MarkPrefetched("analyse_alert");
            }

            // v1.0.160.14: for a cause whose FRS rule tests a DERIVED attribute (a resistance or
            // difference), inject how that value is computed from the base TPR-gated sensors. The
            // derived attribute has no sensor of its own; its components are already on srv1, so the
            // value can be computed from the component readings gathered above (deterministic math,
            // no telemetry tag needed). Covers the 15 TRACK derived causes (RRAIL, RTC VAR RES, etc.).
            int derivedAttid;
            if (CauseLogicMaps.CauseToDerived.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out derivedAttid))
            {
                string dFormula;
                if (CauseLogicMaps.DerivedFormula.TryGetValue(derivedAttid, out dFormula) && !string.IsNullOrEmpty(dFormula))
                {
                    ev.Append("[DERIVED VALUE - this cause tests a COMPUTED attribute; compute it from the component sensor averages, then compare to the FRS range]").Append("\n");
                    ev.Append(dFormula).Append("\n");
                    ev.Append("Base sensors: If = If mA, Ir = Ir mA, Vf, Vr, Choke = Choke V, ChgMa = Charger mA, ChgOpV = Charger OP V (all TPR-gated averages).").Append("\n\n");
                }
            }

            if (!string.IsNullOrEmpty(alerts) && LooksUsableToolResult(alerts, IsEvidenceTool("get_frs_alerts")))
            {
                ev.Append("[SITE ALERTS around the incidence - use for multi-asset correlation]").Append("\n");
                ev.Append(TrimForPrompt(alerts, 5000)).Append("\n\n");
                gotAlerts = true; MarkPrefetched("get_frs_alerts");
                // v1.0.111.0: keep the raw alert history + this alert's identity so triage can be
                // computed SERVER-SIDE (same AssetId + same cause code, count, direction) rather
                // than trusting the model to decide maintenance priority.
                Sess._triageAlertsRaw = alerts;
                Sess._triageAssetId = GetCtx(ctx, "assetId");
                Sess._triageCauseCode = GetCtx(ctx, "causeCode");
                Sess._triageCurrentAlertId = GetCtx(ctx, "alertId");
            }

            if (!string.IsNullOrEmpty(pred) && LooksUsableToolResult(pred, false))
            {
                ev.Append("[ASSET-LEVEL ML ANALYSIS - ").Append(predTool).Append("]").Append("\n");
                ev.Append(TrimForPrompt(pred, 4000)).Append("\n\n");
                gotPred = true; MarkPrefetched(predTool);
            }

            // ---- phase 2: the value series, which needs a TagId from phase 1 ----
            // -- v1.0.78.0 -- match FIRST, then decide whether the search_tags payload
            // is even about this asset. search_tags can return OTHER assets' tags
            // (point machines etc.); appending that misleads the model even when the
            // AssetId fallback later grounds the right history. Gate the append on
            // (matcher resolved) OR (result has rows for this AssetId).
            // -- v1.0.82.0 rev J -- detect a DERIVED alerting attribute up front.
            {
                string dP; string dF;
                Sess._derivedAlert = DetectDerived(ctx, out dP, out dF);
                Sess._derivedParam = dP;
                // v1.0.160.111 (BUG-5): capture the two coverage-gate bypass signals while ctx is live.
                Sess._cardHasFail = CardHasFailCondition(ctx);
                Sess._isFailureAlert = (GetCtx(ctx, "alertType") ?? "").Trim().ToUpperInvariant().StartsWith("F");
                Sess._isTrackFailureCause = IsTrackFailureCauseCode(GetCtx(ctx, "causeCode"));   // v1.0.160.170
                string _rmA, _rmV, _rmD; bool _rmU;   // v1.0.160.173
                Sess._rawHasBreach = RawMessageBreach(ctx, out _rmA, out _rmV, out _rmD, out _rmU);
                if (Sess._derivedAlert)
                {
                    AiLog("INFO", "PREFETCH", "alerting attribute is DERIVED ('" + (dP ?? "?")
                        + "'); grounding via formula components");
                }
            }
            string tagId = Sess._derivedAlert ? null : PickAlertingTagId(stags, ctx, assetId);
            // v1.0.152.5 (P0): register the DIRECT alerting tag as the trigger BEFORE its history is
            // fetched -- prefetch history_get carries only a TagId, so without this the trigger series
            // stays anonymous and TprCoversTrigger can never find it (mask stuck partial).
            if (!Sess._derivedAlert && !string.IsNullOrEmpty(tagId))
            {
                string trigAttrName = null;
                try { JToken _c = ctx["alertCard"]; JToken _tcx = _c != null ? _c["triggeredConditions"] : null; if (_tcx is JArray && ((JArray)_tcx).Count > 0) { JObject _t0 = ((JArray)_tcx)[0] as JObject; if (_t0 != null && _t0["paramLabel"] != null) { trigAttrName = _t0["paramLabel"].ToString().Trim(); } } } catch { }
                RegisterTagMeta(tagId, string.IsNullOrEmpty(trigAttrName) ? "trigger" : trigAttrName, false, true);
                Sess._triggerAttId = AttIdForTag(stags, tagId);   // v1.0.159.0: needed by gated_get/summary_get
            }
            // v1.0.159.0 (C2/C3): ask the DB for what it can compute better than we can.
            // gated_get -> train-free stats (gate = TPR, value = 1) in SQL, which is what the
            // deterministic mask does row-by-row in C#. summary_get -> the multi-day baseline
            // WITHOUT spending raw rows, which matters because C1 now pages the raw rows to the
            // event end of the window. Both are best-effort: a failure changes nothing.
            if ((_gatedRefEnabled || _summaryBaselineEnabled)
                && !Sess._derivedAlert && !string.IsNullOrEmpty(Sess._triggerAttId)
                && !string.IsNullOrEmpty(siteId) && !string.IsNullOrEmpty(assetId) && haveInc)
            {
                string gsSd = incT.AddDays(-_evidenceWindowDays).ToString(_ddFmt);
                string gsEd = incT.AddHours(2).ToString(_ddFmt);
                if (_gatedRefEnabled)
                {
                    try
                    {
                        // v1.0.160.0: the gate is chosen PER ATTRIBUTE from the GatedAverage map, not
                        // hardcoded to TPR as 1.0.159.0 did. Gating a signal aspect on TPR is
                        // meaningless -- an aspect is proved by its OWN ECR. The relay is then
                        // resolved BY NAME among the asset's DataLogger rows (its AssetAttributeId is
                        // asset-specific and collides with analog ids), and THAT row's id is what
                        // gated_get needs. Unresolvable gate -> skip, fail-closed, and say so.
                        int trigAttNum;
                        string gateNm = int.TryParse(Sess._triggerAttId, out trigAttNum) ? GateNameForAttId(trigAttNum) : null;
                        bool ungated = (gateNm == null) && int.TryParse(Sess._triggerAttId, out trigAttNum) && GateIsUngatedId(trigAttNum);
                        // get_asset_tags carries the DataLogger relay rows (RoleType + names);
                        // search_tags is the fallback when the asset-tags call came back thin.
                        JObject gateRow = null;
                        if (gateNm != null)
                        {
                            gateRow = GateRelayRow(tags, gateNm);
                            if (gateRow == null) { gateRow = GateRelayRow(stags, gateNm); }
                        }
                        string gateAttId = null;
                        if (gateRow != null)
                        {
                            JToken gid = PropCI(gateRow, "AssetAttributeId");
                            if (gid != null) { gateAttId = gid.ToString(); }
                        }
                        if (ungated)
                        {
                            AiLog("INFO", "GATE", "attid " + Sess._triggerAttId + " is UNGATED (charger / point-operation) -- no gated reference is meaningful");
                        }
                        else if (gateNm == null)
                        {
                            AiLog("INFO", "GATE", "attid " + Sess._triggerAttId + " is not in the gate map -- gated reference skipped");
                        }
                        else if (string.IsNullOrEmpty(gateAttId))
                        {
                            AiLog("WARN", "GATE", "gate relay '" + gateNm + "' not found (or ambiguous) among this asset's DataLogger relays -- gated reference skipped (fail-closed)");
                        }
                        else
                        {
                            JObject gArgs = new JObject();
                            gArgs["SiteId"] = siteId; gArgs["AssetId"] = assetId;
                            gArgs["AttributeId"] = Sess._triggerAttId; gArgs["Type"] = "a";
                            gArgs["StartDate"] = gsSd; gArgs["EndDate"] = gsEd;
                            gArgs["GateAttributeId"] = gateAttId; gArgs["GateValue"] = 1;
                            gArgs["SummaryType"] = "AVG"; gArgs["Interval"] = "15m";
                            string gres = await PrefetchCallAsync("gated_get", gArgs, loaded, ct).ConfigureAwait(false);
                            if (LooksUsableToolResult(gres, false))
                            {
                                Sess._gatedRefSummary = gres;
                                Sess._gateNameUsed = gateNm;
                                ev.Append("[GATED REFERENCE (gated_get) - the alerting attribute aggregated ONLY while its")
                                  .Append(" governing relay ").Append(gateNm).Append(" is UP. ")
                                  .Append(string.Equals(gateNm, "TPR", StringComparison.OrdinalIgnoreCase)
                                      ? "TPR UP means the track is CLEAR, so train-occupancy samples are excluded."
                                      : ("This is the relay that proves the reading is meaningful; TPR would NOT prove it."))
                                  .Append("]").Append("\n");
                                ev.Append(TrimForPrompt(gres, 2500)).Append("\n\n");
                                MarkPrefetched("gated_get");
                                AiLog("INFO", "GATE", "gated reference for attid " + Sess._triggerAttId
                                    + " gated on " + gateNm + " (attid " + gateAttId + ")==1");
                            }
                        }
                    }
                    catch (Exception gex) { AiLog("WARN", "GATE", "gated_get failed: " + gex.Message); }
                }
                if (_summaryBaselineEnabled)
                {
                    try
                    {
                        JObject sArgs = new JObject();
                        sArgs["SiteId"] = siteId; sArgs["AssetId"] = assetId;
                        sArgs["AttributeId"] = Sess._triggerAttId; sArgs["Type"] = "a";
                        sArgs["StartDate"] = gsSd; sArgs["EndDate"] = gsEd;
                        sArgs["SummaryType"] = "AVG"; sArgs["Interval"] = "1h";
                        string sres = await PrefetchCallAsync("summary_get", sArgs, loaded, ct).ConfigureAwait(false);
                        if (LooksUsableToolResult(sres, false))
                        {
                            ev.Append("[BASELINE (summary_get) - hourly average of the alerting attribute across the ")
                              .Append("whole evidence window; use this for 'what is normal', not the raw sample list, ")
                              .Append("which is paged to the event]").Append("\n");
                            ev.Append(TrimForPrompt(sres, 2500)).Append("\n\n");
                            MarkPrefetched("summary_get");
                        }
                    }
                    catch (Exception sex) { AiLog("WARN", "SUMMARY", "summary_get failed: " + sex.Message); }
                }
            }
            bool stagsPertains = (tagId != null) || TagsContainAsset(stags, assetId);
            // -- v1.0.79.0 -- prove the search_tags parameter fix is working (reviewer
            // RI-2): record what filter was sent implicitly (AssetName+optional
            // AttributeName), the row count, the unique AssetIds returned, whether the
            // requested AssetId is among them, and the resolved TagId.
            {
                int rowCount; int uniqueAssets; bool hasRequested;
                SearchTagsStats(stags, assetId, out rowCount, out uniqueAssets, out hasRequested);
                AiLog("INFO", "PREFETCH", "search_tags result assetName='" + assetName
                    + "' rows=" + rowCount + " uniqueAssets=" + uniqueAssets
                    + " requestedAssetPresent=" + (hasRequested ? "yes" : "no")
                    + " resolvedTag=" + (tagId ?? "none"));
            }
            if (!string.IsNullOrEmpty(stags) && LooksUsableToolResult(stags, false) && stagsPertains)
            {
                ev.Append("[TAG IDS - TagID per attribute, needed by history_get]").Append("\n");
                // v1.0.160.6: strip the bulky -Array waveform CurrentValues before the prompt. For a
                // point machine, search_tags returns the operation waveform arrays (attid 1001/2001/
                // 9001 "..-Array") whose CurrentValue is a 130-point comma string (~600+ chars each) ->
                // the payload balloons to ~22KB and the server truncates it mid-list (10535 chars
                // dropped on PT-117/118), which can cut the very TagID rows the resolver needs. The
                // model resolves tags on TagID + AssetAttributeId + AssetAttributeName + RoleType, NOT
                // on the waveform values, so dropping those long CurrentValue arrays keeps every tag
                // record intact and lets the useful list fit without blunt truncation.
                ev.Append(TrimForPrompt(StripTagWaveforms(stags), 4000)).Append("\n\n");
                gotTagIds = true; MarkPrefetched("search_tags");
            }
            else if (!string.IsNullOrEmpty(stags))
            {
                // suppress the wrong payload; do not let it reach the model
                MarkPrefetched("search_tags");   // still count it as attempted (don't re-call)
                AiLog("INFO", "PREFETCH", "search_tags returned no rows for AssetId "
                    + (string.IsNullOrEmpty(assetId) ? "?" : assetId)
                    + "; payload ignored, resolving via history_get(AssetId)");
            }

            // -- v1.0.82.0 rev J -- DERIVED alerting attribute (e.g. RRAIL): it has NO
            // telemetry tag; hunting one (and the AssetId fallback) can never succeed
            // and previously guaranteed a Coverage-gap INCONCLUSIVE. Ground on the
            // formula's COMPONENT tags instead: resolve each component from the
            // search_tags rows (TagID + AssetAttributeName), pull their series in
            // parallel, and satisfy coverage via components (J3 policy below).
            if (Sess._derivedAlert)
            {
                string dFormula; string dParamTmp;
                DetectDerived(ctx, out dParamTmp, out dFormula);
                // v1.0.160.2: emit the alias->title bridge for THIS formula, before any component
                // discussion, so the model cannot call a fetched component "unavailable" merely
                // because the formula names it by its alias.
                try
                {
                    string _bridge = BuildAliasBridge((dFormula ?? "") + " " + (GetCtx(ctx, "description") ?? ""));
                    if (!string.IsNullOrEmpty(_bridge))
                    {
                        ev.Append(_bridge);
                        AiLog("INFO", "ALIAS", "name bridge emitted for the derived formula");
                    }
                }
                catch { }
                // v1.0.84.0: the authoritative derived value from the card (for the
                // "use THIS, don't recompute" guard note below).
                string dMeasured = null;
                {
                    JToken _c = ctx["alertCard"];
                    JToken _tc = _c != null ? _c["triggeredConditions"] : null;
                    if (_tc != null && _tc.Type == JTokenType.Array && ((JArray)_tc).Count > 0)
                    {
                        JObject _t0 = ((JArray)_tc)[0] as JObject;
                        if (_t0 != null && _t0["measured"] != null)
                        {
                            dMeasured = _t0["measured"].ToString();
                            if (_t0["unit"] != null) { dMeasured += " " + _t0["unit"].ToString(); }
                        }
                    }
                }
                List<string> comps = ExtractFormulaComponents(dFormula);
                // v1.0.160.30 (Item 4c): for a KNOWN derived attid, fetch its base sensors by the
                // pinned canonical list (which resolves against search_tags) instead of the prose
                // formula's short forms (ChgOpV/Vf/If) that DomainAttributeName drops -- otherwise
                // the inputs are never fetched and the derived value cannot be computed.
                if (_derivedComputeV2)
                {
                    int _dAttInp;
                    if (CauseLogicMaps.CauseToDerived.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out _dAttInp))
                    {
                        List<string> _pinned = DerivedBaseInputs(_dAttInp);
                        if (_pinned != null && _pinned.Count > 0) { comps = _pinned; }
                    }
                }
                DateTime dInc;
                bool dHaveInc = TryParseAlertTime(GetCtx(ctx, "time"), out dInc);
                // v1.0.160.104: the movement panel anchors on the MAIN triggered-condition time when
                // present (the stamped incidence can post-date the real trigger sample by minutes),
                // else the stamped incidence. The verdict/coverage path (dInc/compAsOf/dComputed) is
                // unchanged; this anchor drives ONLY the previous-in-envelope comparator. Fix 6: never
                // call ToUnixSeconds on a missing/malformed incidence -- if we have no valid anchor the
                // comparator is skipped (as before, this fails safe).
                bool cmpHaveAnchor = dHaveInc;
                DateTime cmpAnchorDt = dHaveInc ? dInc : DateTime.MinValue;
                {
                    string mainTrigTime = MainTriggeredTime(ctx);
                    DateTime parsedTrig;
                    if (mainTrigTime != null && TryParseAlertTime(mainTrigTime, out parsedTrig))
                    {
                        cmpAnchorDt = parsedTrig;
                        cmpHaveAnchor = true;
                    }
                }
                long cmpAnchorEpoch = cmpHaveAnchor ? ToUnixSeconds(cmpAnchorDt) : 0L;
                long cmpIncidenceEpoch = dHaveInc ? ToUnixSeconds(dInc) : cmpAnchorEpoch;   // referenceAgeSec is incidence-relative (design schema)
                bool cmpTrackAsset = (GetCtx(ctx, "assetType") ?? "").ToUpperInvariant().Contains("TRACK")
                                  || (GetCtx(ctx, "causeCode") ?? "").ToUpperInvariant().StartsWith("TC ")
                                  || (GetCtx(ctx, "causeCode") ?? "").ToUpperInvariant().Contains("RAIL RES");
                int covered = 0; bool covI = false; bool covV = false;
                bool anyI = false; bool anyV = false; int resolved = 0;
                List<string> missing = new List<string>();
                // v1.0.88.0: collect each component's as-of value (for in-code compute)
                Dictionary<string, double> compAsOf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                // v1.0.160.31 (Item 4b): keep each component's FULL series (not just the as-of) so the
                // derived value can be reconstructed point-by-point on the union timeline.
                Dictionary<string, List<double[]>> compSeries = new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, double> compBaseline = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase); // v1.0.109.0: per-component mean for drift %
                Dictionary<string, bool> compBaselineTrainFree = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);   // v1.0.160.121 SL2
                Dictionary<string, long> compAgeSec = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); // v1.0.114.0: staleness per component
                StringBuilder dEv = new StringBuilder();
                dEv.Append("[COMPONENT SERIES for DERIVED '").Append(Sess._derivedParam ?? "attribute")
                   .Append("' - formula: ").Append(dFormula ?? "(not supplied)").Append("]").Append("\n");
                List<string> compTags = new List<string>();
                List<int> compAttids = new List<int>();   // v1.0.160.103: per-component AssetAttributeId (from the same search_tags row) for the envelope lookup
                Dictionary<string, DerivedCompare> compCompare = new Dictionary<string, DerivedCompare>(StringComparer.OrdinalIgnoreCase); // v1.0.160.103: previous-in-envelope result per component
                List<Task<string>> compPulls = new List<Task<string>>();
                for (int ci = 0; ci < comps.Count; ci++)
                {
                    bool isCur = comps[ci].IndexOf("MA", StringComparison.Ordinal) >= 0;
                    if (isCur) { anyI = true; } else { anyV = true; }
                    string cTag;
                    int cAttid;
                    ResolveTagAndAttidForAsset(stags, comps[ci], assetId, out cTag, out cAttid);   // v1.0.160.104: TagID + AssetAttributeId from the SAME search_tags row
                    compTags.Add(cTag);
                    compAttids.Add(cAttid);   // aligned with compTags index; -1 when unresolved
                    if (cTag == null) { missing.Add(comps[ci]); compPulls.Add(null); continue; }
                    resolved++;
                    RegisterTagMeta(cTag, comps[ci], false, false);   // v1.0.152.4: driver identity for chart labels + derived-hero synth
                    // v1.0.160.109 CUT A: this is the site that produced SL 1. It hand-built its args
                    // and omitted sort, so under the 500-row cap it received the OLDEST rows and the
                    // comparator read a 32 h stale sample as the value at the alert. Same builder as
                    // every other site now, and HistFetchAsync dedupes it against the driver fetch above.
                    JObject cArgs = dHaveInc
                        ? HistWindowArgs("TagId", cTag, dInc.AddDays(-_evidenceWindowDays), dInc.AddHours(2),
                                         _limitCapEvidence, "columnar")
                        : HistWindowArgs("TagId", cTag, DateTime.Now.AddDays(-_evidenceWindowDays), DateTime.Now,
                                         _limitCapEvidence, "columnar");
                    compPulls.Add(HistFetchAsync(cArgs, loaded, ct));
                }
                // v1.0.160.42: resolve the digital TPR state series ONCE for the gate (epoch seconds,
                // 0/1). Null/empty => TPR unavailable => gate is a no-op (keep all samples + caveat below).
                List<double[]> _gateTprState = _gatedDerivedEnabled ? BuildTprStateSeries() : null;
                int _gateDropped = 0;
                for (int ci = 0; ci < comps.Count; ci++)
                {
                    if (compPulls[ci] == null) { continue; }
                    string cHist = await compPulls[ci].ConfigureAwait(false);
                    if (string.IsNullOrEmpty(cHist) || !LooksUsableToolResult(cHist, true))
                    {
                        missing.Add(comps[ci]);
                        continue;
                    }
                    if (_derivedSeriesEnabled)
                    {
                        // Item 4b: retain the full (ts,value) series for the union-timeline compute.
                        List<double[]> _cs = ParseTsvSeries(cHist);
                        // v1.0.160.42: gate track-sensor components on TPR steady-UP (+/-GateGuardSec).
                        // Charger inputs (CHARGER/CHG) are ungated -- charger current is meaningful during
                        // occupation. No band test: sub-MinSafe/above-MaxSafe values during steady-up are kept.
                        if (_gatedDerivedEnabled && _cs.Count > 0 && _gateTprState != null && _gateTprState.Count > 0)
                        {
                            string _cn = comps[ci] ?? "";
                            bool _ungatedComp = _cn.IndexOf("CHARGER", StringComparison.OrdinalIgnoreCase) >= 0
                                             || _cn.IndexOf("CHG", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!_ungatedComp)
                            {
                                int _b4 = _cs.Count;
                                _cs = FilterCompSeriesByGate(_cs, _gateTprState, _gateGuardSec);
                                _gateDropped += (_b4 - _cs.Count);
                                Sess._gateDroppedLast = _gateDropped;   // v1.0.160.77: audit-visible
                            }
                        }
                        if (_cs.Count > 0) { compSeries[comps[ci]] = _cs; }
                    }
                    // v1.0.88.0: COV-correct coverage. Use the AS-OF (last value at/before
                    // incidence) rather than requiring a sample NEAR the incidence. Under
                    // change-of-value, a steady component has no in-window sample but its
                    // carried-forward value IS valid at the incidence -- so a component is
                    // COVERED whenever an as-of value exists; only genuine no-data (no row
                    // at all <= incidence) is uncovered. Age is context, never a gate.
                    double asOf; long ageSec; bool bracketOnly; int inWin; int nRows;
                    bool haveAsOf = AsOfValue(cHist, dInc, out asOf, out ageSec, out bracketOnly, out inWin, out nRows);
                    if (haveAsOf)
                    {
                        covered++;
                        compAsOf[comps[ci]] = asOf;
                        if (ageSec >= 0) { compAgeSec[comps[ci]] = ageSec; }   // v1.0.114.0
                        // v1.0.160.121 SL2: TRAIN-FREE median, not a mean over both populations.
                        // A shunt moves If by ~3x, so a mixed statistic describes neither state.
                        // _gateTprState is the same exclusion source the trend frame uses.
                        // Same source the trend frame uses: the harvested raw series for this
                        // component, plus exclusion intervals built from the DIGITAL TPR state.
                        // (Neither RawSeriesFromToolResult nor _gateTprRaw exists -- both were my
                        // invention and would not have compiled.)
                        bool gotTf = false;
                        List<object[]> cRaw = null;
                        if (Sess._trendRawByAttr.TryGetValue(comps[ci], out cRaw) && cRaw != null && cRaw.Count > 0)
                        {
                            string tprK = ResolveTprStateKey();
                            List<object[]> tprR = null;
                            if (tprK != null) { Sess._trendRawByAttr.TryGetValue(tprK, out tprR); }
                            if (tprR != null && tprR.Count > 0)
                            {
                                List<double[]> cExcl = BuildTrainExclusionRaw(tprR);
                                if (cExcl != null && cExcl.Count > 0)
                                {
                                    JObject tf = ComputeTrainFreeRef(cRaw, cExcl);
                                    if (tf != null && tf["median"] != null)
                                    {
                                        compBaseline[comps[ci]] = (double)tf["median"];
                                        compBaselineTrainFree[comps[ci]] = true;
                                        gotTf = true;
                                    }
                                }
                            }
                        }
                        // Fall back to the all-sample mean ONLY when no mask exists; it is then
                        // labelled as such so it cannot be read as a train-free reference.
                        double compMean;
                        if (!gotTf && SeriesMean(cHist, out compMean)) { compBaseline[comps[ci]] = compMean; } // v1.0.109.0
                        // v1.0.160.104: previous-in-envelope comparison for the movement panel. cHist is
                        // only in scope here; the result is stored and the derived_inputs block reads it.
                        // Uses the retained ASSET-narrowed range (falling back to raw range) and the
                        // incidence epoch for referenceAgeSec. Skipped when there is no valid anchor.
                        if (cmpHaveAnchor)
                        {
                            double envMinSafe, envMaxSafe, envMinFail;
                            bool haveBounds = RangeBoundsForAsset(panelAssetRange != null ? panelAssetRange : range, assetId, ci < compAttids.Count ? compAttids[ci] : -1, out envMinSafe, out envMaxSafe, out envMinFail);
                            DerivedCompare dc = PrevInEnvelopeSample(cHist, cmpAnchorEpoch, cmpIncidenceEpoch, cmpTrackAsset, haveBounds, envMinSafe, envMaxSafe, envMinFail, (long)_prevCompMaxAgeMin * 60L);
                            if (dc != null) { compCompare[comps[ci]] = dc; }
                        }
                        bool isCur = comps[ci].IndexOf("MA", StringComparison.Ordinal) >= 0;
                        if (isCur) { covI = true; } else { covV = true; }
                        SetTagCoverage(compTags[ci], HistoryCoverageLevel.VerifiedAdequate);
                        string ageStr = ageSec >= 0
                            ? (ageSec < 90 ? ageSec + "s" : (ageSec / 60) + "min") + " before incidence"
                            : "age unknown";
                        dEv.Append("[").Append(comps[ci]).Append(" - TagId ").Append(compTags[ci])
                           .Append(" - as-of value ").Append(asOf.ToString(System.Globalization.CultureInfo.InvariantCulture))
                           .Append(" (last change ").Append(ageStr)
                           .Append(bracketOnly ? "; STEADY - no change in window, value held" : "; " + inWin + " changes in window")
                           .Append(")").Append(HealthBandNote(comps[ci])).Append("]").Append("\n");
                    }
                    else
                    {
                        missing.Add(comps[ci]);
                        SetTagCoverage(compTags[ci], HistoryCoverageLevel.Unverified);
                        dEv.Append("[").Append(comps[ci]).Append(" - TagId ").Append(compTags[ci])
                           .Append(" - NO as-of value (no sample at/before incidence)]").Append("\n");
                    }
                    // v1.0.90.0: emit a COMPACT readable window around incidence (the model
                    // reads times, not raw epochs), then a REDUCED raw tail as a safety net.
                    // The as-of + computed derived value (above / below) already give the
                    // model the authoritative numbers; this is just trend context.
                    string cw = CompactWindow(cHist, dInc, 8);
                    if (cw.Length > 0)
                    {
                        dEv.Append("  near-incidence samples (IST): ").Append(cw).Append("\n");
                        dEv.Append("  raw(tail): ").Append(TailForPrompt(cHist, 500)).Append("\n");
                    }
                    else
                    {
                        // parse fell through -- keep the original larger tail so nothing is lost
                        dEv.Append(TailForPrompt(cHist, 1200)).Append("\n");
                    }
                    Sess.gotUsableHistoryGet = true;
                    Sess._jsonGotHistory = true;
                    Sess._jsonGotEvidence = true;
                }
                // v1.0.160.42: gate-result note (provenance). TPR unavailable => kept-all caveat;
                // otherwise report how many train/edge samples the relay-steady-UP filter removed.
                if (_gatedDerivedEnabled)
                {
                    if (_gateTprState == null || _gateTprState.Count == 0)
                    {
                        dEv.Append("[GATE] TPR state unavailable -- component samples ungated (kept); derived NOT train-filtered this run").Append("\n");
                    }
                    else if (_gateDropped > 0)
                    {
                        dEv.Append("[GATE] relay-steady-UP filter removed ").Append(_gateDropped)
                           .Append(" train/edge sample(s) (+/-").Append(_gateGuardSec)
                           .Append("s TPR); derived + sustain computed on clear-track data only").Append("\n");
                    }
                }
                // J3 policy (COV): >=2 components AVAILABLE as-of, incl. one V and one I
                // when the formula uses both. Steady components count -- they are valid.
                Sess._derivedComponentsAdequate = covered >= 2 && (!anyI || covI) && (!anyV || covV);
                if (Sess._derivedComponentsAdequate) { Sess._lastCoverageKind = CoverageStateKind.Covered; }
                // v1.0.88.0: compute the derived value IN CODE from the components'
                // as-of values, using the card's own formula string. Zero drift risk
                // (same formula the alert engine used). Cross-check against the card's
                // measured value; agreement is strong corroboration, divergence is
                // itself diagnostic (means the as-of inputs differ from the alert instant).
                string dComputed = null;
                if (compAsOf.Count > 0 && !string.IsNullOrEmpty(dFormula))
                {
                    double cv;
                    if (TryEvalFormula(dFormula, compAsOf, out cv))
                    {
                        dComputed = cv.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    }
                    else if (_derivedComputeV2)
                    {
                        // v1.0.160.30 (Item 4c): TryEvalFormula cannot parse the NESTED derived formulas
                        // (VTC_VAR_RES / TR_V571 underscores, [If != 0], where-clauses). Compute directly
                        // from the base sensors + R737 (attid 737 from the FRS range; TR_V571 = Ir*R737/1000).
                        int _dAttC;
                        if (CauseLogicMaps.CauseToDerived.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out _dAttC))
                        {
                            double? _r737 = DerivedNeedsR737(_dAttC) ? RangeAvgForAttid(range, 737) : (double?)null;
                            double _dv;
                            if (ComputeDerivedDirect(_dAttC, compAsOf, _r737, out _dv))
                            {
                                dComputed = _dv.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }
                    }
                }
                // v1.0.160.103: build the structured derived_inputs for the card as a
                // PREVIOUS-IN-ENVELOPE comparison (not a window statistic). For each component the
                // comparator (run in-loop, stored in compCompare) supplies the at-alert value and, if
                // available, the previous IN-ENVELOPE reference and the delta. The at-alert value is
                // NEVER envelope-gated (a genuine step above MaxSafe stays visible, flagged
                // currentOutsideEnvelope). windowMedian is retained as a diagnostic only. The panel's
                // derived value is the AUTHORITATIVE alert-card value (clean numeric), not the
                // incidence-time dComputed, so the shown derived value and the shown inputs are
                // consistent in what they claim.
                if (compAsOf.Count > 0)
                {
                    JArray inputsArr = new JArray();
                    string maxName = null; double maxAbsDelta = -1;
                    bool anyNull = false;      // a formula input read exactly 0 vs a non-zero window median
                    foreach (KeyValuePair<string, double> kv in compAsOf)
                    {
                        double bval; bool haveB = compBaseline.TryGetValue(kv.Key, out bval);
                        JObject inp = new JObject();
                        inp["name"] = kv.Key;
                        DerivedCompare dc;
                        bool haveCmp = compCompare.TryGetValue(kv.Key, out dc);
                        // Fix 3: noReading must test the value actually DISPLAYED (anchor-time dc.AtVal
                        // when the comparator ran, else the incidence-time as-of value), not a value on
                        // a different timeline than the delta.
                        double shownVal = haveCmp ? dc.AtVal : kv.Value;
                        bool noReading = (Math.Abs(shownVal) < 1e-9) && haveB && Math.Abs(bval) > 1e-6;
                        if (noReading) { anyNull = true; }
                        if (haveCmp)
                        {
                            inp["value"] = Math.Round(dc.AtVal, 3);
                            inp["valueTs"] = dc.AtTs;
                            if (dc.CurrentOutside) { inp["currentOutsideEnvelope"] = true; }
                            if (dc.HasRef)
                            {
                                inp["referenceValue"] = Math.Round(dc.RefVal, 3);
                                inp["referenceTs"] = dc.RefTs;
                                inp["referenceAgeSec"] = dc.RefAgeSec;
                                if (dc.Kind != null) { inp["comparisonKind"] = dc.Kind; }
                                // v1.0.160.109 CUT A (SL 1): a stale at-alert sample publishes NO delta
                                // and is NOT eligible for largestDeviation -- the stalest input must
                                // never become the headline mover, which is exactly what 543194 did.
                                if (!noReading && dc.HasDelta && !dc.StaleAtSample)
                                {
                                    inp["deltaPct"] = Math.Round(dc.DeltaPct, 1);
                                    if (Math.Abs(dc.DeltaPct) > maxAbsDelta) { maxAbsDelta = Math.Abs(dc.DeltaPct); maxName = kv.Key; }
                                }
                            }
                            if (dc.StaleAtSample)
                            {
                                inp["noRecentSample"] = true;
                                inp["reason"] = "no comparable reading near the alert (last sample "
                                    + Math.Round(dc.AtAgeSec / 3600.0, 1) + " h before)";
                            }
                            else if (dc.Reason != null) { inp["reason"] = dc.Reason; }
                        }
                        else
                        {
                            // comparator produced no at-alert sample (e.g. component uncovered) -- fall
                            // back to the as-of value so the input still renders; no delta.
                            inp["value"] = Math.Round(kv.Value, 3);
                        }
                        // window median retained as a DIAGNOSTIC only -- never the reference, never rendered as movement.
                        // v1.0.160.121 SL2: name it for what it IS. A train-free median and an
                        // all-sample mean are different statistics; on a shunted series they differ
                        // by ~3x, and one field name for both is how -64.9% got shown as movement.
                        if (haveB)
                        {
                            bool tfOk;
                            if (compBaselineTrainFree.TryGetValue(kv.Key, out tfOk) && tfOk)
                            { inp["windowMedianTrainFree"] = Math.Round(bval, 3); }
                            else
                            { inp["windowMeanAllSamples"] = Math.Round(bval, 3); }
                        }
                        if (noReading) { inp["noReading"] = true; }
                        // Fix (verification 1): the age must match the DISPLAYED value. When the comparator
                        // ran, use the anchor-relative age of the at-alert sample (dc.AtAgeSec); only fall
                        // back to the incidence-time compAgeSec when no comparator result exists.
                        if (haveCmp)
                        {
                            if (dc.AtAgeSec >= 0) { inp["ageSec"] = dc.AtAgeSec; }
                        }
                        else
                        {
                            long compAge;
                            if (compAgeSec.TryGetValue(kv.Key, out compAge) && compAge >= 0) { inp["ageSec"] = compAge; }
                        }
                        inputsArr.Add(inp);
                    }
                    // largestDeviation is now the largest VALID comparable-sample movement (largest
                    // |deltaPct|); absent when no input has a comparison. Still an OBSERVATION, not a
                    // causal "driver" claim. A noReading input is never eligible.
                    if (maxName != null && maxAbsDelta >= 0)
                    {
                        for (int ii = 0; ii < inputsArr.Count; ii++)
                        {
                            JObject io = (JObject)inputsArr[ii];
                            io["largestDeviation"] = (io["name"] != null && io["name"].ToString() == maxName);
                        }
                    }
                    JObject di = new JObject();
                    di["param"] = Sess._derivedParam ?? "derived";
                    if (!string.IsNullOrEmpty(dFormula)) { di["formula"] = dFormula; }
                    // Clarification A: the panel's derived VALUE is the AUTHORITATIVE alert-card value
                    // for the derived param (clean numeric parsed from the matching condition's
                    // 'measured'), NOT the incidence-time dComputed -- so the shown derived value is not
                    // presented as computed from the (anchor-time) displayed inputs.
                    double cardDerivedVal, cardLimit; bool cardHaveLimit, cardBreachAbove; string cardLimitUnit;
                    if (CardDerivedValue(ctx, Sess._derivedParam, out cardDerivedVal, out cardLimit, out cardHaveLimit, out cardLimitUnit, out cardBreachAbove))
                    {
                        di["value"] = Math.Round(cardDerivedVal, 3);
                        if (cardHaveLimit)
                        {
                            di["limit"] = Math.Round(cardLimit, 3);
                            di["breachAbove"] = cardBreachAbove;
                            if (!string.IsNullOrEmpty(cardLimitUnit)) { di["limitUnit"] = cardLimitUnit; }
                        }
                    }
                    di["inputs"] = inputsArr;
                    // honest flag for the card: this is observation, not attribution
                    di["deviationOnly"] = true;
                    if (anyNull) { di["inputSuspect"] = true; }
                    Sess._derivedInputs = di;
                }
                dEv.Append("NOTE: '").Append(Sess._derivedParam ?? "this attribute")
                   .Append("' is DERIVED via the formula above - it has NO direct history series; ")
                   .Append("do NOT call history_get for it. Judge via the component values provided.");
                // v1.0.88.0: the controller now computes the derived value in code from
                // the components' as-of (carry-forward) values using the card's own
                // formula -- present that, plus the card's measured value, and let the
                // model reason about mechanism + staleness. No hand-arithmetic needed.
                dEv.Append(" DERIVED VALUE (computed by the system from component as-of values via the formula): ")
                   .Append(dComputed ?? "not computable")
                   .Append("; alert card measured value: ").Append(dMeasured ?? "see card")
                   .Append(". Coverage is by AS-OF / carry-forward: under change-of-value telemetry a component that ")
                   .Append("did not change has no in-window sample but its last-known value IS its value at the incidence, ")
                   .Append("so a STEADY component is valid, not missing. Use the component as-of values to judge WHICH ")
                   .Append("input drove the change (compare each to its baseline) and weigh how OLD each last change is ")
                   .Append("(a value steady for hours is weaker evidence than a fresh change). Do NOT recompute the derived ")
                   .Append("value yourself; the system value above is authoritative.");
                if (missing.Count > 0)
                {
                    dEv.Append(" Components with no as-of value (genuine no-data): ")
                       .Append(string.Join(", ", missing))
                       .Append(".");
                }
                dEv.Append("\n");
                // v1.0.160.31 (Item 4b): reconstruct the derived value as a per-instance SERIES on the
                // union timeline of the input sensors, find the longest breach run, and confirm it
                // against the FRS sustain window -- so sustained-vs-transient is COMPUTED, not inferred.
                if (_derivedSeriesEnabled && compSeries.Count > 0)
                {
                    int _dAttS;
                    if (CauseLogicMaps.CauseToDerived.TryGetValue((GetCtx(ctx, "causeCode") ?? "").Trim(), out _dAttS))
                    {
                        double? _r737s = DerivedNeedsR737(_dAttS) ? RangeAvgForAttid(range, 737) : (double?)null;
                        List<double[]> _series = ComputeDerivedSeries(_dAttS, compSeries, _r737s, dHaveInc ? (double)ToUnixSeconds(dInc) : 0.0);
                        if (_series.Count >= 2)
                        {
                            string _ccS = (GetCtx(ctx, "causeCode") ?? "").Trim();
                            bool _upper = _ccS.IndexOf("LOW", StringComparison.OrdinalIgnoreCase) < 0;   // HIGH/OPEN = upper, LOW = lower
                            double? _thr = _upper
                                ? RangeNumForAttid(range, _dAttS, new string[] { "MaxSafeValue", "max" })
                                : RangeNumForAttid(range, _dAttS, new string[] { "MinSafeValue", "min" });
                            if (_thr.HasValue)
                            {
                                double _st, _en, _pk;
                                double _dur = LongestBreachRun(_series, _thr.Value, _upper, out _st, out _en, out _pk);
                                string _logicS; CauseLogicMaps.CauseToLogic.TryGetValue(_ccS, out _logicS);
                                int _need = FrsSustainSeconds(_logicS);
                                bool _sustained = _dur >= _need;
                                dEv.Append("[DERIVED SERIES (computed point-by-point on the union timeline of the input sensors, ")
                                   .Append(_series.Count.ToString()).Append(" points): longest ")
                                   .Append(_upper ? "over-threshold" : "under-threshold").Append(" run = ")
                                   .Append(((long)_dur).ToString()).Append("s, peak ")
                                   .Append(_pk.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                                   .Append(_upper ? " vs MaxSafe " : " vs MinSafe ")
                                   .Append(_thr.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                                   .Append("; FRS rule requires the breach to hold for >= ").Append(_need.ToString())
                                   .Append("s -> ")
                                   .Append(_sustained
                                       ? "SUSTAINED breach (duration confirmed by the computed derived series)"
                                       : "MOMENTARY (breach did not hold for the FRS sustain window -- weigh as transient unless the card/incidence says otherwise)")
                                   .Append("]").Append("\n");
                                // v1.0.160.37: COMPUTED-VERDICT decision (owner rule). PREDICTIVE/ANALOG only:
                                // sustained (run >= FRS window) -> CONFIRMED; else NOT_CONFIRMED. Coherence is
                                // not structured here, so the breach alone decides (owner ruling: "if no
                                // coherence defined, consider only the alert"). Failure/relay-state alerts are
                                // not PREDICTIVE, so they never stash a decision and stay on the 160.25 rule.
                                if (_computedVerdictEnabled)
                                {
                                    string _kindCV = (GetCtx(ctx, "alertType") ?? "").ToUpperInvariant();
                                    if (_kindCV.IndexOf("PREDICT", StringComparison.Ordinal) >= 0)
                                    {
                                        StashComputedVerdict(_sustained, (long)_dur, _need, "derived series " + _ccS);
                                    }
                                }
                            }
                        }
                    }
                }
                ev.Append(dEv.ToString()).Append("\n");
                if (!Sess._derivedComponentsAdequate)
                {
                    Sess._prefetchGap = "derived attribute: component coverage insufficient ("
                        + covered + " covered)";
                }
                AiLog("INFO", "PREFETCH", "derived '" + (Sess._derivedParam ?? "?") + "' components="
                    + comps.Count + " resolved=" + resolved + " covered=" + covered
                    + " adequate=" + (Sess._derivedComponentsAdequate ? "yes" : "no"));
            }
            else if (tagId != null)
            {
                // -- v1.0.76.0 -- pin the identity the INSTANT it is matched, not
                // after history_get succeeds: a failed/unusable fetch must never
                // erase an already-resolved alerting tag.
                Sess.requiredAlertingTagId = tagId;
                // v1.0.160.109 CUT A: hand-built and sort-less before; same builder as every site now.
                DateTime inc;
                JObject hargs = TryParseAlertTime(GetCtx(ctx, "time"), out inc)
                    ? HistWindowArgs("TagId", tagId, inc.AddDays(-_evidenceWindowDays), inc.AddHours(2),
                                     _limitCapEvidence, "columnar")
                    : HistWindowArgs("TagId", tagId, DateTime.Now.AddDays(-_evidenceWindowDays), DateTime.Now,
                                     _limitCapEvidence, "columnar");
                string hist = await HistFetchAsync(hargs, loaded, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(hist) && LooksUsableToolResult(hist, true))
                {
                    long shortBy;
                    string covWhy;
                    CoverageStateKind covState = HistoryCoverageState(hist, out shortBy, out covWhy);
                    Sess._lastCoverageKind = covState;
                    // requiredAlertingTagId = tagId;   -- v1.0.76.0: moved above, before the fetch (Item 1)
                    Sess.gotUsableHistoryGet = true;
                    string covNote = null;
                    if (covState == CoverageStateKind.Covered)
                    {
                        SetTagCoverage(tagId, HistoryCoverageLevel.VerifiedAdequate);
                    }
                    else if (covState == CoverageStateKind.Unverifiable)
                    {
                        covNote = "NOTE: incidence coverage of this series could NOT be verified - it "
                                + covWhy + ". " + CoverageRecoveryAdvice(covState) + ".";
                        Sess._prefetchGap = "history coverage could not be verified";
                        SetTagCoverage(tagId, HistoryCoverageLevel.Unverified);
                    }
                    else
                    {
                        covNote = "NOTE: this series is NOT sufficient evidence - it " + covWhy + ". "
                                + CoverageRecoveryAdvice(covState) + ".";
                        Sess._prefetchGap = "history " + covWhy;
                        SetTagCoverage(tagId, HistoryCoverageLevel.Inadequate);
                    }
                    ev.Append("[VALUE HISTORY - the alerting attribute, oldest first]").Append("\n");
                    ev.Append(TrimForPrompt(hist, 12000)).Append("\n");
                    if (covNote != null) { ev.Append(covNote).Append("\n"); }
                    ev.Append("\n");
                    // pre-fetched telemetry grounds the verdict exactly as a tool call
                    // would: the gate exists to require REAL data, not a tool call.
                    Sess._jsonGotHistory = true;
                    Sess._jsonGotEvidence = true;
                }
                else
                {
                    histWhy = "TagId " + tagId + " returned no usable series - fetch it yourself";
                    Sess._prefetchGap = "history unusable";
                    AiLog("WARN", "PREFETCH", histWhy);
                }
            }
            else
            {
                // -- v1.0.78.0 -- AssetId fallback. search_tags could not resolve the
                // alerting TagId (wrong/other assets). history_get accepts AssetId
                // directly (unique, no site/tag lookup); its JSON response carries
                // TagID + AssetAttributeName per block, the exact fields the matcher
                // reads. Probe MUST be outputMode=json (preserveJson=true): columnar
                // cols/rows have no field names.
                string fbTagId = null;
                DateTime fbInc;
                bool fbHaveInc = TryParseAlertTime(GetCtx(ctx, "time"), out fbInc);
                // v1.0.160.177: for a POINT MACHINE, the history_get(AssetId) fallback probe returns the
                // last row for EVERY attribute -- including the ~48 -PM- operation ARRAY tags (each a
                // 120-250 point waveform string). That single call cost ~21s in the wire trace (>90% of
                // prefetch), and those array tags are NOT used for the verdict. The alerting tag resolves
                // from search_tags (already fetched) + the attid maps, so skip this wide probe for points.
                string _fbType = (GetCtx(ctx, "assetType") ?? "").ToUpperInvariant();
                bool _fbIsPoint = _fbType.IndexOf("POINT") >= 0;
                if (!string.IsNullOrEmpty(assetId) && !_fbIsPoint)
                {
                    // v1.0.160.109 CUT A: outputMode is deliberately JSON here and the limit is 1
                    // (rows PER TAG, not attribute count -- see RF-5). The block below scans the
                    // response for the literal field name "TagID"; emitting columnar would return
                    // positional arrays and break PickAlertingTagId SILENTLY.
                    JObject probeArgs = fbHaveInc
                        ? HistWindowArgs("AssetId", assetId, fbInc.AddDays(-_evidenceWindowDays), fbInc.AddHours(2), 1, "json")
                        : HistWindowArgs("AssetId", assetId, DateTime.Now.AddDays(-_evidenceWindowDays), DateTime.Now, 1, "json");
                    string probe = await HistFetchAsync(probeArgs, loaded, ct, true).ConfigureAwait(false);
                    int blocks = -1;
                    bool truncated = false;
                    if (!string.IsNullOrEmpty(probe))
                    {
                        truncated = _truncMarker.IsMatch(probe);
                        int c = 0; int idx = 0;
                        while ((idx = probe.IndexOf("TagID", idx, StringComparison.OrdinalIgnoreCase)) >= 0) { c++; idx += 5; }
                        blocks = c;
                        fbTagId = PickAlertingTagId(probe, ctx, assetId);
                    }
                    AiLog("INFO", "PREFETCH", "AssetId probe assetId=" + assetId
                        + " chars=" + (probe == null ? 0 : probe.Length)
                        + " truncated=" + truncated
                        + " tagBlocks=" + blocks
                        + " resolved=" + (fbTagId ?? "none"));
                }
                if (fbTagId != null)
                {
                    Sess.requiredAlertingTagId = fbTagId;
                    JObject fbArgs = fbHaveInc
                        ? HistWindowArgs("TagId", fbTagId, fbInc.AddDays(-_evidenceWindowDays), fbInc.AddHours(2), 500, "columnar")
                        : HistWindowArgs("TagId", fbTagId, DateTime.Now.AddDays(-_evidenceWindowDays), DateTime.Now, 500, "columnar");
                    string fbHist = await HistFetchAsync(fbArgs, loaded, ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(fbHist) && LooksUsableToolResult(fbHist, true))
                    {
                        long fbShortBy;
                        string fbWhy;
                        CoverageStateKind fbState = HistoryCoverageState(fbHist, out fbShortBy, out fbWhy);
                        Sess._lastCoverageKind = fbState;
                        Sess.gotUsableHistoryGet = true;
                        string fbNote = null;
                        if (fbState == CoverageStateKind.Covered)
                        {
                            SetTagCoverage(fbTagId, HistoryCoverageLevel.VerifiedAdequate);
                        }
                        else if (fbState == CoverageStateKind.Unverifiable)
                        {
                            fbNote = "NOTE: incidence coverage of this series could NOT be verified - it "
                                   + fbWhy + ". " + CoverageRecoveryAdvice(fbState) + ".";
                            Sess._prefetchGap = "history coverage could not be verified";
                            SetTagCoverage(fbTagId, HistoryCoverageLevel.Unverified);
                        }
                        else
                        {
                            fbNote = "NOTE: this series is NOT sufficient evidence - it " + fbWhy + ". "
                                   + CoverageRecoveryAdvice(fbState) + ".";
                            Sess._prefetchGap = "history " + fbWhy;
                            SetTagCoverage(fbTagId, HistoryCoverageLevel.Inadequate);
                        }
                        ev.Append("[VALUE HISTORY - the alerting attribute, oldest first]").Append("\n");
                        ev.Append(TrimForPrompt(fbHist, 12000)).Append("\n");
                        if (fbNote != null) { ev.Append(fbNote).Append("\n"); }
                        ev.Append("\n");
                        Sess._jsonGotHistory = true;
                        Sess._jsonGotEvidence = true;
                        AiLog("INFO", "PREFETCH", "search_tags miss -> grounded via AssetId fallback, TagId " + fbTagId);
                    }
                    else
                    {
                        histWhy = "AssetId fallback: TagId " + fbTagId + " returned no usable series";
                        Sess._prefetchGap = "history unusable";
                        AiLog("WARN", "PREFETCH", histWhy);
                    }
                }
                else
                {
                    histWhy = "could not match the alerting attribute to a TagId (search_tags AND AssetId fallback) - pick it from the ASSET TAGS above and call history_get";
                    Sess._prefetchGap = "alerting attribute could not be matched to a TagId";
                    AiLog("INFO", "PREFETCH", histWhy);
                }
            }

            // Every condition the one-turn path depends on. Any miss keeps the tools.
            if (!gotTags) { Sess._prefetchGap = Sess._prefetchGap ?? "asset tags missing"; }
            if (!gotRange) { Sess._prefetchGap = Sess._prefetchGap ?? "baseline thresholds missing"; }
            if (!Sess._jsonGotHistory) { Sess._prefetchGap = Sess._prefetchGap ?? "no value history"; }
            // Cause-specific depth. The verdicts that proved most useful leaned on the
            // fault audit and the ML leakage events -- mechanism and corroboration, not
            // just the threshold crossing. For a track or point-machine alert, entering
            // one-turn synthesis without EITHER of those would silently produce a
            // thinner analysis than the agentic path would have.
            if (predTool != null && !gotPred && !gotAudit)
            {
                Sess._prefetchGap = Sess._prefetchGap ?? "neither fault audit nor ML analysis available for this asset type";
            }
            Sess._prefetchComplete = gotTags && gotRange && Sess._jsonGotHistory && Sess._prefetchGap == null;

            ev.Append("[STATUS]").Append("\n");
            ev.Append("  asset tags .... ").Append(gotTags ? "DONE - do not call get_asset_tags" : "MISSING").Append("\n");
            ev.Append("  baseline ...... ").Append(gotRange ? "DONE - do not call get_attribute_range" : "MISSING").Append("\n");
            ev.Append("  tag ids ....... ").Append(gotTagIds ? "DONE - do not call search_tags" : "MISSING").Append("\n");
            ev.Append("  fault audit ... ").Append(gotAudit ? "DONE - do not call analyse_alert" : "unavailable").Append("\n");
            ev.Append("  site alerts ... ").Append(gotAlerts ? "DONE - do not call get_frs_alerts" : "unavailable").Append("\n");
            if (predTool != null)
            {
                ev.Append("  ML analysis ... ").Append(gotPred ? ("DONE - do not call " + predTool) : "unavailable this cycle").Append("\n");
            }
            // -- v1.0.75.0 -- REMOVED: keyed on _jsonGotHistory alone, so SHORT
            // history read as "DONE - do not call history_get".
            // ev.Append("  value history . ").Append(_jsonGotHistory ? "DONE - do not call history_get" : ("MISSING (" + (histWhy ?? "not attempted") + ")")).Append("\n");
            string histStatus;
            if (Sess._jsonGotHistory && AlertingCoverageLevel() == HistoryCoverageLevel.VerifiedAdequate)
            {
                histStatus = "DONE - do not call history_get";
            }
            else if (Sess._jsonGotHistory)
            {
                histStatus = "RETRY REQUIRED - " + CoverageRecoveryAdvice(Sess._lastCoverageKind);
            }
            else
            {
                histStatus = "MISSING (" + (histWhy ?? "not attempted") + ")";
            }
            ev.Append("  value history . ").Append(histStatus).Append("\n");

            // v1.0.92.0 STAGE 4 (PM branch): for a POINT-MACHINE alert, add a detection-
            // coherence note. Point detection has a structure track/signal alerts lack:
            // per end NWKR xor RWKR (Normal vs Reverse), and A-END must AGREE with B-END.
            // We surface the eight detection Titles (room + location, both ends) + the PM ML
            // tools so the model checks agreement. ADDITIVE and POINT-ONLY -- never touches track/signal paths.
            // NOTE: this guides the model using the detection attributes; it does not yet
            // fetch each detection tag's live state in prefetch (that is the next refinement
            // once we have a real PM alert to validate the fetch against). Kept honest.
            if (IsPointCard(ctx))
            {
                ev.Append("=== POINT-MACHINE DETECTION COHERENCE ===").Append("\n");
                ev.Append("Current Point cause class: ")
                  .Append(IsPointDetectionRelayCause(GetCtx(ctx, "causeCode")) ? "DETECTION-RELAY" : "OPERATION/CURRENT/VOLTAGE")
                  .Append(".").Append("\n");
                ev.Append("This is a point-machine alert. Point detection has structure track/signal lack: a point end ");
                ev.Append("is normally Normal (NWKR up) XOR Reverse (RWKR up), and A-END normally agrees with B-END. ");
                ev.Append("Detection relays: ").Append(string.Join(", ", _pmDetectionAttrs)).Append(". ");
                ev.Append("ROOM vs LOC: the room-side relays (A/B End - NWKR/RWKR) are AUTHORITATIVE - what the interlocking ");
                ev.Append("acts on; the (Loc) relays are what the field/point reports before the cable run back to the room. ");
                ev.Append("If the (Loc) side shows a lie the room side does not, that SUPPORTS localization to the cable ");
                ev.Append("path between the point and the relay room (telemetry alone does not establish the failed ");
                ev.Append("component). Room and Loc agreeing means no room-versus-location transmission mismatch; ");
                ev.Append("it does NOT establish the correct commanded lie - still verify NWKR/RWKR exclusivity, A/B-end ");
                ev.Append("agreement, and that the detected lie matches the command outcome. ");
                ev.Append("GUARD: use a detection value for incidence coherence ONLY when its timestamp is within the alert ");
                ev.Append("window - current or untimestamped values (e.g. from get_asset_tags) describe present state, ");
                ev.Append("possibly a later throw or the reset, and cannot prove the state at the alert. ");
                ev.Append("CAUSE-AWARE: for a current/voltage/operation alert, detection disagreement WEAKENS the mechanical ");
                ev.Append("assessment and normally warrants INCONCLUSIVE unless independently resolved; for a detection-relay ");
                ev.Append("alert (NWKR/RWKR RELAY DEFECT), the same disagreement MAY CONFIRM the reported condition when ");
                ev.Append("timestamp-aligned. Operation values (IPT/VPT/TPT) exist only during a throw: absence = idle ONLY ");
                ev.Append("if no throw/control event (NWCR/RWCR) is in the window; a commanded throw with absent operation ");
                ev.Append("telemetry is a DATA GAP -> INCONCLUSIVE. A throw with current but no detection is an operation/");
                ev.Append("detection coherence failure; telemetry cannot name the cause. Operation TIME is on the CURRENT ");
                ev.Append("channel (A/B end) in milliseconds; a normal throw is ~5 s, and an operation over ~6000 ms (6 s) ");
                ev.Append("is EXTENDED - flag as a possible obstruction/high-load concern for the engineer, do not name the ");
                ev.Append("cause. The predict_pm_operation / predict_pm_cluster ML tools cover point health if needed.").Append("\n");
            }

            // v1.0.149.0: per-section STATUS truth table (checked responses, not hope) +
            // deterministic computed blocks the model reads instead of deriving.
            ev.Append("=== SECTION STATUS (checked) ===").Append("\n");
            ev.Append("tags=").Append(gotTags ? "ok" : "MISSING")
              .Append("  range=").Append(gotRange ? "ok" : "MISSING")
              .Append("  searchTags=").Append(gotTagIds ? "ok" : "MISSING")
              .Append("  history=").Append(histWhy == null ? "ok" : ("MISSING(" + histWhy + ")"))
              .Append("  mlAudit=").Append(gotAudit ? "ok" : "MISSING")
              .Append("  alerts=").Append(gotAlerts ? "ok" : "MISSING")
              .Append("  predictions=").Append(gotPred ? "ok" : "n/a").Append("\n");
            string computed = BuildComputedTrendBlock();
            if (!string.IsNullOrEmpty(computed)) { ev.Append(computed); }
            ev.Append("=== END PRE-FETCHED EVIDENCE ===").Append("\n");

            sw.Stop();
            AiLog("INFO", "PREFETCH", "gathered " + ev.Length + " chars in " + sw.ElapsedMilliseconds + "ms"
                + " (history=" + (Sess._jsonGotHistory ? "yes" : "no") + ")");
            JObject tr = new JObject();
            tr["ms"] = sw.ElapsedMilliseconds; tr["chars"] = ev.Length; tr["grounded"] = Sess._jsonGotHistory;
            TraceAnalyze("prefetch", tr);
            // ok reflects whether the pre-fetch achieved ANYTHING, not whether it
            // achieved EVERYTHING. Flagging the step amber because one of four items
            // was missing read as a failure when tags, baseline and tag ids had all
            // been gathered -- the same misleading-progress trap as the old checklist.
            int got = (gotTags ? 1 : 0) + (gotRange ? 1 : 0) + (gotTagIds ? 1 : 0) + (Sess._jsonGotHistory ? 1 : 0)

                    + (gotPred ? 1 : 0) + (gotAudit ? 1 : 0) + (gotAlerts ? 1 : 0);
            string statusNote = got + "/" + (predTool != null ? 7 : 6) + " gathered";
            if (!Sess._jsonGotHistory) { statusNote += " \u00b7 history left to the model"; }
            WriteSse("step", new { phase = "tool_done", name = "prefetch_evidence",
                                   engine = _engineName1, ms = sw.ElapsedMilliseconds,
                                   chars = ev.Length, ok = got > 0, note = statusNote });
            return ev.ToString();
            }
            finally
            {
                // v1.0.160.110 SL5: wall-clock for the prefetch phase, recorded on EVERY exit path
                // (including the early `return null` when there is no assetId).
                _swPrefetchPhase.Stop();
                Sess._trPrefetchWallMs += _swPrefetchPhase.ElapsedMilliseconds;
            }
        }


        internal static JObject NewArgs(string k, string v)
        {
            JObject o = new JObject();
            long n;
            if (long.TryParse(v, out n)) { o[k] = n; } else { o[k] = v; }
            return o;
        }


        // v1.0.160.109 CUT A: deliberately NOT HistWindowArgs. This serves get_attribute_range_history,
        // which takes a NAIVE DateTime server-side and is already CORRECT -- applying the history_get
        // UTC compensation here would introduce the very 5.5 h skew Cut A removes.
        // v1.0.117.0: args for get_attribute_range_history - AssetId + a ddMMyyyy_HHmmss window
        // ending just after incidence and reaching back the evidence window (same window the
        // component/history fetches use, so the trend lines up with the samples).
        internal static JObject NewRangeHistoryArgs(string assetId, DateTime incidence)
        {
            JObject o = new JObject();
            long n;
            if (long.TryParse(assetId, out n)) { o["AssetId"] = n; } else { o["AssetId"] = assetId; }
            o["StartDate"] = incidence.AddDays(-_evidenceWindowDays).ToString(_ddFmt);
            o["EndDate"] = incidence.AddHours(2).ToString(_ddFmt);
            return o;
        }


        internal static async Task<string> SafeAwait(Task<string> t)
        {
            if (t == null) { return null; }
            try { return await t.ConfigureAwait(false); }
            catch { return null; }
        }


        // Same policy path a model-issued call takes: clamps, budget, routing.
        // -- v1.0.78.0 -- preserveJson: the AssetId metadata probe needs named
        // fields (TagID, AssetAttributeName) to resolve the alerting tag, but
        // ClampToolArgs pins evidence tools to outputMode=columnar (positional
        // cols/rows, no field names). When preserveJson is set, restore json
        // AFTER the clamp -- probe only; the trend pull keeps columnar.
        // v1.0.160.110 SL5: thin timing wrapper. The body is unchanged (renamed to ...CoreAsync) --
        // wrapping it in place would have meant a try/finally around a long method with early
        // returns, which the 160.63 note rightly called a bigger edit than the fix needs. Every
        // existing call site keeps calling PrefetchCallAsync and is timed for free.
        internal async Task<string> PrefetchCallAsync(string tool, JObject args, ToolLoadResult loaded, System.Threading.CancellationToken ct, bool preserveJson = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // v1.0.160.172 (Fix 3): serve static range calls from cache when fresh.
                string ck = PrefetchCacheKey(tool, args);
                if (ck != null)
                {
                    string cached;
                    if (TryGetPrefetchCache(ck, out cached)) { return cached; }
                    string fresh = await PrefetchWithTimeoutAsync(tool, args, loaded, ct, preserveJson).ConfigureAwait(false);
                    if (LooksUsableToolResult(fresh, IsEvidenceTool(tool))) { SetPrefetchCache(ck, fresh); }
                    return fresh;
                }
                return await PrefetchWithTimeoutAsync(tool, args, loaded, ct, preserveJson).ConfigureAwait(false);
            }
            finally
            {
                sw.Stop();
                // Interlocked: prefetch tasks run CONCURRENTLY (tagsT/rangeT/stagsT/predT/alertsT/
                // rangeHistT all start before any await), so this is a genuine race without it.
                System.Threading.Interlocked.Add(ref Sess._trToolCallMsSum, sw.ElapsedMilliseconds);
            }
        }


        // v1.0.160.172 (Fix 4): per-call timeout + one retry, active only in parallel mode
        // (!_serializeClients). The underlying client call cannot be cancelled mid-flight, but
        // AwaitWithCancel abandons the caller on the linked-token timeout, so a stalled call stops
        // holding up THIS caller; the retry usually lands on a free slot. In serialized mode the
        // timeout is disabled (no CancelAfter) so legit lock-waits are never killed.
        internal async Task<string> PrefetchWithTimeoutAsync(string tool, JObject args, ToolLoadResult loaded, System.Threading.CancellationToken ct, bool preserveJson)
        {
            if (_serializeClients)
            {
                return await PrefetchCallCoreAsync(tool, args, loaded, ct, preserveJson).ConfigureAwait(false);
            }
            int attempts = _prefetchRetries + 1;
            for (int i = 0; i < attempts; i++)
            {
                using (var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(_prefetchTimeoutMs);
                    try
                    {
                        return await PrefetchCallCoreAsync(tool, args, loaded, cts.Token, preserveJson).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        AiLog("WARN", "PREFETCH", tool + " timed out after " + _prefetchTimeoutMs + "ms (attempt " + (i + 1) + "/" + attempts + ")");
                    }
                }
            }
            return null;
        }


        internal async Task<string> PrefetchCallCoreAsync(string tool, JObject args, ToolLoadResult loaded, System.Threading.CancellationToken ct, bool preserveJson = false)
        {
            if (loaded.OwnerMap == null || !loaded.OwnerMap.ContainsKey(tool)) { return null; }
            // Pre-fetch must NOT consume the model's per-tool budget. It made 3 calls
            // of its own, one of them search_tags -- which left the model only ONE
            // search_tags before the cap of 2 refused it, and a denied lookup is worse
            // than no pre-fetch at all. Pre-fetch is bounded by construction (a fixed
            // set of calls, once), so the allowlist alone is the right guard here.
            if (!_jsonToolCaps.ContainsKey(tool)) { return null; }
            HashSet<string> declared = null;
            if (loaded.ArgMap != null && loaded.ArgMap.ContainsKey(tool)) { declared = loaded.ArgMap[tool]; }
            // v1.0.160.63: count prefetch tool calls. The agentic-loop counter (L13756) never sees
            // these, so the audit reported 0 for a run that made 8. Count only -- no try/finally
            // wrapper around this long method, which would be a far bigger edit than the fix needs.
            Sess._trPrefetchCalls++;
            JToken clamped = ClampToolArgs(tool, args, declared);
            if (preserveJson)
            {
                JObject co = clamped as JObject;
                // only if the tool actually declares outputMode (undeclared args are dropped)
                if (co != null && declared != null && declared.Contains("outputMode"))
                {
                    string omName = null;
                    foreach (string dn in declared)
                    {
                        if (string.Equals(dn, "outputMode", StringComparison.OrdinalIgnoreCase)) { omName = dn; break; }
                    }
                    co[omName ?? "outputMode"] = "json";
                }
            }
            int owner = loaded.OwnerMap[tool];
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            string res = await CallToolRoutedAsync(tool, clamped, ct, owner).ConfigureAwait(false);
            // v1.0.149.0: per-call CHECK -- one automatic retry when a prefetch response is
            // absent/tiny/errored, so a transient MCP hiccup does not silently cost a section.
            if (res == null || res.Length < 25 || res.IndexOf("\"isError\":true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AiLog("INFO", "PREFETCH", "retrying " + tool + " (first response " + (res == null ? "null" : (res.Length + "B")) + ")");
                try { await Task.Delay(300, ct).ConfigureAwait(false); } catch { }
                string res2 = await CallToolRoutedAsync(tool, clamped, ct, owner).ConfigureAwait(false);
                if (res2 != null && (res == null || res2.Length > res.Length)) { res = res2; }
            }
            sw.Stop();
            // v1.0.158.4: if the series was cut, re-fetch just the TAIL of the window. The tail
            // holds the incidence, which is the part the verdict actually needs; without this the
            // analysis reasons from telemetry that stops a day or more before the event.
            if (NoteHistoryTruncation(tool, clamped, res))
            {
                if (_histPagingEnabled)
                {
                    res = await HistoryPageToEndAsync(tool, clamped, res, ct, owner).ConfigureAwait(false);
                }
                else
                try
                {
                    JObject ta = clamped as JObject;
                    string endStr = (ta != null && ta["EndDate"] != null) ? ta["EndDate"].ToString() : null;
                    DateTime endDt;
                    if (!string.IsNullOrEmpty(endStr)
                        && DateTime.TryParseExact(endStr, _ddFmt, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out endDt))
                    {
                        // v1.0.160.109 CUT A: derived from the already-stamped EndDate, so it stays in
                        // whatever frame the builder produced -- do NOT re-apply HistStamp here.
                        JObject tailArgs = (JObject)ta.DeepClone();
                        tailArgs["StartDate"] = endDt.AddHours(-_histTailHours).ToString(_ddFmt);
                        AiLog("INFO", "HIST", "re-fetching TAIL " + _histTailHours + "h for " + tool
                            + " (cut series -> the incidence was outside the returned rows)");
                        string tailRes = await CallToolRoutedAsync(tool, tailArgs, ct, owner).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(tailRes) && tailRes.IndexOf("\"ts\"", StringComparison.Ordinal) >= 0)
                        {
                            res = tailRes;   // the tail reaches the event; the head did not
                            NoteHistoryTruncation(tool, tailArgs, tailRes);
                        }
                    }
                }
                catch (Exception tex) { AiLog("WARN", "HIST", "tail re-fetch failed: " + tex.Message); }
            }
            Sess._jsonToolOk += LooksUsableToolResult(res, IsEvidenceTool(tool)) ? 1 : 0;
            // v1.0.158.2 (P0): log prefetch calls under the RUN's trace id, not the literal string
            // "prefetch". Since the prefetch-v2 work most evidence is gathered BEFORE the model loop,
            // so a typical run makes ZERO in-loop tool calls -- every call was filed under "prefetch"
            // and a Tool trace lookup by sse-... found nothing. That is why the trace looked broken
            // even with logging enabled. toolUseId stays "prefetch" so the reader can badge the stage.
            LogToolEvent(string.IsNullOrEmpty(Sess._traceId) ? "prefetch" : Sess._traceId,
                "response", -1, "prefetch", tool, owner, clamped, res, null, sw.ElapsedMilliseconds);
            RecordToolTiming(tool, clamped, res, -1, sw.ElapsedMilliseconds);
            return res;
        }


        // Match the alert's triggered parameter against the tag list. The card names
        // the parameter that actually breached (e.g. "ITC RELAY END (Ir) mA"), so the
        // series we want is the tag whose attribute name shares the most words with it.
        // Cause-code prefix identifies the asset class, which selects the ML tool.
        internal static string PredictToolFor(string causeCode)
        {
            if (string.IsNullOrEmpty(causeCode)) { return null; }
            string c = causeCode.Trim().ToUpperInvariant();
            if (c.StartsWith("TC ")) { return "predict_track_health"; }
            if (c.StartsWith("PT ")) { return "predict_pm_operation"; }
            return null;   // signals and power supply have no ML analysis
        }


        internal static JObject NewPredictArgs(string tool, string siteId, string assetId)
        {
            JObject o = new JObject();
            long sn, an;
            if (long.TryParse(siteId, out sn)) { o["SiteId"] = sn; }
            if (long.TryParse(assetId, out an))
            {
                // predict_track_health takes an ARRAY of track ids; the PM tools take one
                if (string.Equals(tool, "predict_track_health", StringComparison.OrdinalIgnoreCase))
                {
                    JArray ids = new JArray(); ids.Add(an); o["TrackIds"] = ids;
                }
                else { o["AssetId"] = an; }
            }
            return o;
        }


        internal void MarkPrefetched(string tool)
        {
            if (Sess._prefetchDone != null && !string.IsNullOrEmpty(tool)) { Sess._prefetchDone.Add(tool); }
        }


        // history_get is deliberately NOT deniable: the model may legitimately want a
        // DIFFERENT tag than the one pre-fetch chose. Only asset-level, one-shot
        // lookups are refused, because a second call returns identical bytes.
        // v1.0.160.166: pull every asset-ish id out of a tool-call's arguments. AssetId / AssetIds[]
        // / TrackIds[] all name an asset; a call carrying an id that prefetch did NOT cover is a new
        // question, not a repeat.
        internal static void CollectArgAssetIds(JObject args, List<string> into)
        {
            if (args == null || into == null) { return; }
            string[] keys = new string[] { "AssetId", "assetId", "AssetIds", "assetIds", "TrackIds", "trackIds" };
            for (int i = 0; i < keys.Length; i++)
            {
                JToken t = args[keys[i]];
                if (t == null) { continue; }
                if (t.Type == JTokenType.Array)
                {
                    foreach (JToken e in (JArray)t)
                    {
                        string s = e != null ? e.ToString().Trim() : "";
                        if (s.Length > 0) { into.Add(s); }
                    }
                }
                else
                {
                    string s = t.ToString().Trim();
                    if (s.Length > 0) { into.Add(s); }
                }
            }
        }


        internal string PrefetchAlreadyHas(string tool, JObject args)
        {
            if (Sess._prefetchDone == null || !Sess._prefetchDone.Contains(tool)) { return null; }
            // v1.0.160.166: SCOPE THE DENIAL TO THE PREFETCHED ASSET. Prefetch only ever gathered
            // the alerting asset, so a call naming a different asset -- a family track, an adjacent
            // track, another asset at the site -- is genuinely new data and must go through.
            // Without this the model is told to check the adjacent family-track log and then
            // refused the only call that resolves its TagId, which is why verdicts kept carrying
            // "No adjacent track telemetry".
            if (!string.IsNullOrEmpty(Sess._prefetchAssetId))
            {
                List<string> ids = new List<string>();
                CollectArgAssetIds(args, ids);
                bool namesOther = false;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!string.Equals(ids[i], Sess._prefetchAssetId, StringComparison.OrdinalIgnoreCase))
                    {
                        namesOther = true;
                        break;
                    }
                }
                if (namesOther)
                {
                    AiLog("INFO", "TOOL", tool + " allowed for a non-prefetched asset ("
                        + string.Join(",", ids.ToArray()) + "; prefetch covered " + Sess._prefetchAssetId + ")");
                    return null;
                }
            }
            // DEAD END GUARD. search_tags is the ONLY source of a TagId, and history_get
            // needs one. If pre-fetch ran search_tags but could not match the alerting
            // attribute, denying it leaves the model unable to find the tag itself --
            // observed: "series": [] and no chart, because every route to a TagId was
            // closed. Only suppress it once history is actually in hand.
            if (string.Equals(tool, "search_tags", StringComparison.OrdinalIgnoreCase)
                && !Sess._jsonGotHistory)
            {
                return null;
            }
            return "Already provided: '" + tool + "' output is in the PRE-FETCHED EVIDENCE block "
                 + "at the top of this conversation. Calling it again returns identical bytes and "
                 + "costs a full turn. Read it there and continue.";
        }


        // analyse_alert takes the station CODE directly plus the alert facts, so it
        // needs no lookup of any kind.
        internal static JObject NewAuditArgs(JObject ctx, string siteId, string assetId, DateTime inc)
        {
            JObject o = new JObject();
            o["cause_code"] = GetCtx(ctx, "causeCode");
            o["asset_name"] = GetCtx(ctx, "assetName");
            o["site_code"] = GetCtx(ctx, "station");
            o["asset_type"] = AssetTypeFor(GetCtx(ctx, "causeCode"));
            o["alert_timestamp"] = inc.ToString("yyyy-MM-dd HH:mm:ss");
            long n;
            if (long.TryParse(siteId, out n)) { o["site_id"] = n; }
            if (long.TryParse(assetId, out n)) { o["asset_id"] = n; }
            return o;
        }


        internal static string AssetTypeFor(string causeCode)
        {
            string c = (causeCode ?? "").Trim().ToUpperInvariant();
            if (c.StartsWith("PT ")) { return "POINT MACHINE"; }
            if (c.StartsWith("SIG") || c.StartsWith("SHSIG") || c.StartsWith("ROSIG") || c.StartsWith("COSIG")) { return "SIGNAL"; }
            if (c.StartsWith("TC ")) { return "TRACK"; }
            // Anything else (IPS power supply, datalogger, unknown prefixes) must NOT
            // be submitted as TRACK -- that contaminates the audit with the wrong
            // cause-code definition. Return null and skip the audit instead.
            return null;
        }


        // Alerts across the SITE around the incidence. Simultaneous alerts on other
        // assets are what separate a site-wide event from a local defect, and that
        // correlation has produced the strongest verdicts in testing.
        // v1.0.160.154 Layer A: THIS asset's own alert record over the lookback window --
        // ground-truth recurrence, distinct from the +/-2h SITE window (NewSiteAlertArgs).
        internal static JObject NewAssetHistoryArgs(string siteId, string assetId, DateTime inc, int lookbackDays)
        {
            JObject o = new JObject();
            long n;
            // v1.0.160.155: SiteIds is REQUIRED by the tool schema (v1.0.118.0 note) -- .154 sent
            // AssetIds alone and the server refused with "SiteIds (int array) is required."
            if (long.TryParse(siteId, out n))
            {
                JArray s = new JArray();
                s.Add(n);
                o["SiteIds"] = s;
            }
            if (long.TryParse(assetId, out n))
            {
                JArray a = new JArray();
                a.Add(n);
                o["AssetIds"] = a;
            }
            o["FromDate"] = inc.AddDays(-lookbackDays).ToString("yyyy-MM-ddTHH:mm:ss");
            o["ToDate"] = inc.ToString("yyyy-MM-ddTHH:mm:ss");
            o["PageSize"] = 50;
            return o;
        }


        // v1.0.160.154 Layer A: extract the cause code from an FRS alert row. Explicit fields
        // first; else the LAST >=3-char double-quoted token in the description's head -- FRS
        // descriptions read <station>,"<asset>","<CAUSE>"..., station codes are 2 chars and drop
        // out, and nothing after the cause is quoted in the observed TC/SIG/PT shapes. Scan is
        // capped at 400 chars: causes appear early, and later free text must not hijack the pick.
        internal static string FrsRowCause(JObject ro)
        {
            string cc = FirstStr(ro, "causeCode", "alertCode", "cause_code", "CauseCode", "AlertCode");
            if (!string.IsNullOrEmpty(cc))
            {
                return cc.Trim();
            }
            string desc = FirstStr(ro, "Description", "description");
            if (string.IsNullOrEmpty(desc))
            {
                return "";
            }
            string head = desc.Length > 400 ? desc.Substring(0, 400) : desc;
            string last = "";
            int i = 0;
            while (i < head.Length)
            {
                int q1 = head.IndexOf('"', i);
                if (q1 < 0)
                {
                    break;
                }
                int q2 = head.IndexOf('"', q1 + 1);
                if (q2 < 0)
                {
                    break;
                }
                string inner = head.Substring(q1 + 1, q2 - q1 - 1).Trim();
                if (inner.Length >= 3)
                {
                    last = inner;
                }
                i = q2 + 1;
            }
            return last;
        }


        // v1.0.160.154 Layer A: reduce the raw asset-history rows to the summary the prompt and
        // the story pie consume. Excludes the current alert; de-dupes by row id (SelectTokens can
        // revisit nested copies -- same guard as the triage counter).
        internal JObject BuildAssetHistory(string raw, string currentAlertId, string causeNow, int lookbackDays)
        {
            try
            {
                List<JToken> docs = ParseJsonDocuments(raw);
                if (docs.Count == 0)
                {
                    return null;
                }
                System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                JObject byCause = new JObject();
                int total = 0;
                int sameCause = 0;
                DateTime lastSame = DateTime.MinValue;
                double lastSameResetMin = -1.0;
                DateTime spanMin = DateTime.MaxValue;
                DateTime spanMax = DateTime.MinValue;
                for (int d = 0; d < docs.Count; d++)
                {
                    if (docs[d] == null)
                    {
                        continue;
                    }
                    foreach (JToken tok in docs[d].SelectTokens("$..*"))
                    {
                        if (tok == null || tok.Type != JTokenType.Object)
                        {
                            continue;
                        }
                        JObject ro = (JObject)tok;
                        string cause = FrsRowCause(ro);
                        if (string.IsNullOrEmpty(cause))
                        {
                            continue;
                        }
                        string rowId = FirstStr(ro, "Id", "id", "AlertId", "alertId");
                        if (!string.IsNullOrEmpty(currentAlertId) && !string.IsNullOrEmpty(rowId)
                            && string.Equals(rowId, currentAlertId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (!string.IsNullOrEmpty(rowId))
                        {
                            if (seen.Contains(rowId))
                            {
                                continue;
                            }
                            seen.Add(rowId);
                        }
                        total++;
                        int prev = byCause[cause] != null ? (int)byCause[cause] : 0;
                        byCause[cause] = prev + 1;
                        DateTime setT;
                        bool haveSet = DateTime.TryParse(FirstStr(ro, "SetTimeStamp", "setTimeStamp"), out setT);
                        if (haveSet)
                        {
                            if (setT < spanMin)
                            {
                                spanMin = setT;
                            }
                            if (setT > spanMax)
                            {
                                spanMax = setT;
                            }
                        }
                        if (!string.IsNullOrEmpty(causeNow) && string.Equals(cause, causeNow, StringComparison.OrdinalIgnoreCase))
                        {
                            sameCause++;
                            if (haveSet && setT > lastSame)
                            {
                                lastSame = setT;
                                DateTime rstT;
                                if (DateTime.TryParse(FirstStr(ro, "ResetTimeStamp", "resetTimeStamp"), out rstT) && rstT > setT)
                                {
                                    lastSameResetMin = (rstT - setT).TotalMinutes;
                                }
                                else
                                {
                                    lastSameResetMin = -1.0;
                                }
                            }
                        }
                    }
                }
                if (total <= 0)
                {
                    return null;
                }
                JObject o = new JObject();
                o["windowDays"] = lookbackDays;
                o["total"] = total;
                o["sameCause"] = sameCause;
                o["byCause"] = byCause;
                if (spanMax != DateTime.MinValue)
                {
                    o["span"] = spanMin.ToString("dd.MM") + " .. " + spanMax.ToString("dd.MM.yyyy");
                }
                if (lastSame != DateTime.MinValue)
                {
                    o["lastSame"] = lastSame.ToString("dd.MM.yyyy HH:mm");
                    if (lastSameResetMin >= 0.0)
                    {
                        o["lastSameResetMin"] = Math.Round(lastSameResetMin);
                    }
                }
                // TRACK family rollup (design 6b Q2): exact cause stays the PRIMARY count; the
                // family total is one secondary line, and only when this alert is a TC cause.
                if (!string.IsNullOrEmpty(causeNow) && causeNow.TrimStart().StartsWith("TC ", StringComparison.OrdinalIgnoreCase))
                {
                    int fam = 0;
                    foreach (var p in byCause.Properties())
                    {
                        if (p.Name.TrimStart().StartsWith("TC ", StringComparison.OrdinalIgnoreCase))
                        {
                            fam += (int)p.Value;
                        }
                    }
                    o["trackFamily"] = fam;
                }
                return o;
            }
            catch
            {
                return null;
            }
        }


        // v1.0.160.154 Layer A: the evidence block. Numbers plus stated reading boundaries -- no
        // pre-baked severity score (design 6b Q3).
        internal static string FormatAssetHistoryEvidence(JObject ah)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("[ASSET ALERT HISTORY - ").Append(ah["windowDays"]).Append("d, THIS asset only, field record (ground truth)]").Append("\n");
            sb.Append(" total alerts: ").Append(ah["total"]);
            JObject bc = ah["byCause"] as JObject;
            if (bc != null)
            {
                sb.Append(" across ").Append(bc.Count).Append(" causes");
            }
            if (ah["span"] != null)
            {
                sb.Append("; span ").Append(ah["span"]);
            }
            sb.Append("\n");
            sb.Append(" same-cause as this alert: ").Append(ah["sameCause"]).Append(" (current excluded)");
            if (ah["lastSame"] != null)
            {
                sb.Append("; last ").Append(ah["lastSame"]);
                sb.Append(ah["lastSameResetMin"] != null ? ", reset in " + ah["lastSameResetMin"] + " min" : ", no reset recorded");
            }
            sb.Append("\n");
            if (ah["trackFamily"] != null)
            {
                sb.Append(" track-health family total: ").Append(ah["trackFamily"]).Append("\n");
            }
            if (bc != null)
            {
                sb.Append(" by cause: ");
                bool firstP = true;
                foreach (var p in bc.Properties())
                {
                    if (!firstP)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(p.Name).Append("=").Append(p.Value);
                    firstP = false;
                }
                sb.Append("\n");
            }
            sb.Append(" read: repeats that reset <15 min = transient/managed; 15-120 min = sustained-but-recovered;");
            sb.Append(" >120 min or never-reset = serious. Recurrence is CONTEXT; the electrical trend decides the verdict.");
            return sb.ToString();
        }


        internal static JObject NewSiteAlertArgs(string siteId, DateTime inc)
        {
            JObject o = new JObject();
            long n;
            if (long.TryParse(siteId, out n)) { JArray a = new JArray(); a.Add(n); o["SiteIds"] = a; }
            // v1.0.160.129: ISO, NOT _ddFmt. get_frs_alerts is the one tool in the catalog that
            // takes yyyy-MM-ddTHH:mm:ss; sending ddMMyyyy_HHmmss made the server drop the window
            // entirely and return an unfiltered page.
            o["FromDate"] = inc.AddHours(-2).ToString("yyyy-MM-ddTHH:mm:ss");
            o["ToDate"] = inc.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss");
            o["PageSize"] = 25;
            return o;
        }


        // v1.0.118.0: get_frs_alerts probe args for DryRunTools (tool-health check, no incidence
        // time). SiteIds/AssetIds are ARRAYS per the declared schema (required: SiteIds). Prefer
        // SiteId; fall back to AssetIds if only an asset is known.
        // v1.0.160.180: LIVE reset re-check for the CACHE-SERVE path. A verdict cached while the
        // alert was ACTIVE stored an empty resetTime; if the alert has since RESET, the served panel
        // still announces "Still active" on a cleared fault. This fetches the CURRENT FRS row for
        // THIS alert and reports its reset state so the serve path can override the frozen value.
        // Returns: a non-empty stamp (dd/MM/yyyy HH:mm:ss) => RESET now; "" => row found, still
        // ACTIVE; null => cannot determine (no site/asset id, call failed, row absent) -> leave the
        // cached value untouched, never regress a good serve on a transient fetch failure.
        internal async Task<string> TryLiveResetTimeAsync(JObject ctx, System.Threading.CancellationToken ct)
        {
            try
            {
                string siteId = GetCtx(ctx, "siteId");
                string assetId = GetCtx(ctx, "assetId");
                string wantId = GetCtx(ctx, "alertId");
                if (wantId.Length == 0) { return null; }
                if (siteId.Length == 0 && assetId.Length == 0) { return null; }
                string alerts = await CallToolRoutedAsync("get_frs_alerts", NewFrsAlertsProbeArgs(siteId, assetId), ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(alerts)) { return null; }
                JObject ao = JObject.Parse(alerts);
                JArray rows = ao["mFRSAlerts"] as JArray;
                if (rows == null) { return null; }
                for (int ri = 0; ri < rows.Count; ri++)
                {
                    JObject r = rows[ri] as JObject;
                    if (r == null || r["Id"] == null) { continue; }
                    if (!string.Equals(r["Id"].ToString(), wantId, StringComparison.Ordinal)) { continue; }
                    JToken rt = r["ResetTimeStamp"];
                    if (rt == null || rt.Type == JTokenType.Null) { return ""; }   // found, still active
                    DateTime rtd;
                    if (DateTime.TryParse(rt.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out rtd))
                    { return rtd.ToString("dd/MM/yyyy HH:mm:ss"); }
                    return "";
                }
                return null;   // this alert was not in the returned page
            }
            catch { return null; }
        }


        internal static JObject NewFrsAlertsProbeArgs(string siteId, string assetId)
        {
            JObject o = new JObject();
            long n;
            if (!string.IsNullOrWhiteSpace(siteId) && long.TryParse(siteId, out n))
            { JArray a = new JArray(); a.Add(n); o["SiteIds"] = a; }
            else if (!string.IsNullOrWhiteSpace(assetId) && long.TryParse(assetId, out n))
            { JArray a = new JArray(); a.Add(n); o["AssetIds"] = a; }
            o["PageSize"] = 20;
            return o;
        }


        internal static JObject NewSearchArgs(string assetName, string siteId, JObject ctx, bool isDerived)
        {
            JObject o = new JObject();
            o["AssetName"] = assetName;
            long n;
            if (siteId != null && long.TryParse(siteId, out n)) { o["SiteId"] = n; }

            // v1.0.160.55: a cause depends on a SET of attributes, not one. Build the set (L1 cause map,
            // L2 every condition failing-first, L3 circuit-wisdom drivers of derived members). Taking
            // triggeredConditions[0] was the 522365 bug: that card's [0] is the condition that PASSED.
            List<string> drivers = DriverSetForCause(ctx);
            string want = "";
            if (drivers.Count > 0) { want = drivers[0]; }
            else if (ctx != null) { want = GetCtx(ctx, "causeCode"); }
            string canonical = DomainAttributeName(want);
            // v1.0.86.0: for a DERIVED alert, DO NOT narrow by AttributeName. The
            // derived name ("RRAIL") is NOT a stored tag, so AttributeName="RRAIL"
            // makes the server return ZERO rows (confirmed on the wire: prefetch
            // search {AssetName:117T, AttributeName:RRAIL} -> "No tags found", while
            // the same asset by AssetId returns all 11 component tags). The derived
            // branch needs the FULL tag list to resolve its formula components, so
            // suppress the narrowing here and let it come back unfiltered.
            // v1.0.152.2: SAME problem for POINT MACHINE and IPS -- their tags are named by
            // number ("NNNNN-PM-02002-*"), so narrowing by the human AttributeName returns
            // few/zero rows and starves the attid resolver (which needs the asset's FULL tag
            // list to match by attid). Confirmed on the wire (asset 41898): {AttributeName:
            // "B END - NW-V"} -> 1 row, while AssetId returns all 32 tags. Suppress narrowing
            // for PM/IPS too; fetch the full list and let the attid fast-path pick by number.
            string _ccW = ctx != null ? GetCtx(ctx, "causeCode") : null;
            bool _pmOrIps = (ctx != null && IsPointCard(ctx))
                || (!string.IsNullOrEmpty(_ccW) && _ccW.Trim().ToUpperInvariant().StartsWith("PT "))
                || (!string.IsNullOrEmpty(_ccW) && _ipsCauseAttid.ContainsKey(_ccW.Trim()));
            // v1.0.160.55: more than one driver -> do NOT narrow. AttributeName selects ONE attribute,
            // so a multi-driver cause would starve on the others. Fetch the asset's full tag list and
            // let the matcher pick, exactly as the derived and PM/IPS branches already do.
            // v1.0.160.56: NO AiLog here -- this method is static and AiLog is an instance member
            // (that was the 160.55 CS0120). The driver set is logged at the instance call site below.
            bool _multiDriver = drivers.Count > 1;
            if (!string.IsNullOrEmpty(canonical) && !isDerived && !_pmOrIps && !_multiDriver)
            {
                // DomainAttributeName returns an UPPERCASE canonical ("IR MA"); the
                // server ILIKE is case-insensitive, so this matches "Ir mA".
                o["AttributeName"] = canonical;
            }
            // search_tags default Limit is 3 (doc 2.1); keep an explicit higher value
            // so an asset's full attribute set is available to the matcher. Server
            // hard cap is 50, so ask for exactly that (60 previously exceeded the cap).
            o["Limit"] = 50;
            return o;
        }
    }
}
