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
    // AnalysisEngine -- Grounding.Trend.
    // Moved verbatim from AiChatController.Grounding.Trend.cs in 1.0.165.0 (Shared AI Core 2e);
    // only access modifiers changed (private -> internal). The web app sees internals via InternalsVisibleTo.
    public sealed partial class AnalysisEngine
    {

        // v1.0.160.198: the analysis snapshot is built from get_asset_tags, which does NOT carry the
        // PM operation "-Avg" rows -- so op V/C cards read 0 until the operator hit Refresh (LiveValues,
        // which reads api/LiveValue). Fetch the same live tag table here and override the op V/C values
        // in the snapshot with the last-operation average, so the FIRST render already matches Refresh.
        // Point machines only; op V/C cards only (PmOpChannelForName); state recomputed vs the safe band.
        internal void MergePmAvgIntoSnapshot(int assetId)
        {
            try
            {
                if (Sess._snapshotValues == null || Sess._snapshotValues.Count == 0 || assetId <= 0) { return; }
                Dictionary<int, double> live = FetchLiveValueMap(assetId);
                if (live.Count == 0) { return; }
                int patched = 0;
                for (int i = 0; i < Sess._snapshotValues.Count; i++)
                {
                    JObject o = Sess._snapshotValues[i] as JObject;
                    if (o == null || o["attr"] == null) { continue; }
                    int ch = PmOpChannelForName(o["attr"].ToString());
                    if (ch == 0) { continue; }
                    double avg;
                    if (!live.TryGetValue(ch, out avg)) { continue; }
                    o["value"] = Math.Round(avg, 3);
                    // recompute in/out against the row's own safe band, matching ParseSnapshotValues
                    double mn = 0, mx = 0;
                    bool hasMn = o["minSafe"] != null && double.TryParse(o["minSafe"].ToString(),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mn);
                    bool hasMx = o["maxSafe"] != null && double.TryParse(o["maxSafe"].ToString(),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mx);
                    if (hasMn || hasMx)
                    {
                        bool below = hasMn && avg < mn, above = hasMx && avg > mx;
                        o["state"] = below ? "low" : (above ? "high" : "ok");
                    }
                    patched++;
                }
                if (patched > 0) { AiLog("INFO", "SNAPSHOT", "PM op averages merged from tag table into " + patched + " rows"); }
            }
            catch { }
        }


        // v1.0.160.127: the readings behind the caption. get_asset_tags already carries value,
        // MinValue/MaxValue and the per-attribute device time; nothing was reading them back.
        // v1.0.160.135: get_attribute_range carries AverageValue and MinFailValue per attribute.
        // Matched by AttributeName because the snapshot rows are keyed on the name, not the attid.
        internal void MergeRangeIntoSnapshot(string rangeJson)
        {
            if (Sess._snapshotValues == null || Sess._snapshotValues.Count == 0 || string.IsNullOrEmpty(rangeJson)) { return; }
            try
            {
                JArray rows = null;
                try { rows = JArray.Parse(rangeJson); }
                catch
                {
                    List<JToken> docs = ParseJsonDocuments(rangeJson);
                    for (int i = 0; i < docs.Count; i++) { rows = docs[i] as JArray; if (rows != null) { break; } }
                }
                if (rows == null) { return; }
                Dictionary<string, JObject> byName = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rows.Count; i++)
                {
                    JObject r = rows[i] as JObject;
                    if (r == null || r["AttributeName"] == null) { continue; }
                    string nm = r["AttributeName"].ToString().Trim();
                    if (nm.Length > 0 && !byName.ContainsKey(nm)) { byName[nm] = r; }
                }
                int merged = 0;
                for (int i = 0; i < Sess._snapshotValues.Count; i++)
                {
                    JObject v = Sess._snapshotValues[i] as JObject;
                    if (v == null || v["attr"] == null) { continue; }
                    JObject r;
                    if (!byName.TryGetValue(v["attr"].ToString().Trim(), out r)) { continue; }
                    double d;
                    if (r["AverageValue"] != null && r["AverageValue"].Type != JTokenType.Null
                        && double.TryParse(r["AverageValue"].ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out d))
                    { v["avg"] = Math.Round(d, 3); merged++; }
                    if (r["MinFailValue"] != null && r["MinFailValue"].Type != JTokenType.Null
                        && double.TryParse(r["MinFailValue"].ToString(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out d))
                    { v["minFail"] = Math.Round(d, 3); }
                }
                AiLog("INFO", "SNAPSHOT", "range merged into " + merged + " of " + Sess._snapshotValues.Count + " readings");
            }
            catch { }
        }


        internal static JArray ParseSnapshotValues(string tagsJson)
        {
            JArray outArr = new JArray();
            try
            {
                JArray rows = null;
                try { rows = JArray.Parse(tagsJson); }
                catch
                {
                    List<JToken> docs = ParseJsonDocuments(tagsJson);
                    for (int i = 0; i < docs.Count; i++) { rows = docs[i] as JArray; if (rows != null) { break; } }
                }
                if (rows == null) { return outArr; }
                // v1.0.160.174 (point LIVE bind): the per-end op V/C tags (212-219 "A End - NW-V" etc.)
                // are Periodic and read 0 at rest. The real last-operation value is in the -PM-0X002-Avg
                // rows (1002/2002/3002/4002 Normal, 6002/7002/8002/9002 Reverse). Pre-scan those so the
                // op V/C cards bind the last-operation average instead of 0; also detect the point machine
                // so the 16-row cap can be lifted (a PM has ~24 attrs; the Loc-indication rows fall past 16).
                Dictionary<int, double> pmAvg = new Dictionary<int, double>();
                bool isPM = false;
                for (int pi = 0; pi < rows.Count; pi++)
                {
                    JObject r0 = rows[pi] as JObject; if (r0 == null) { continue; }
                    string dt0 = r0["DataType"] != null ? r0["DataType"].ToString() : "";
                    string nm0 = r0["AssetAttributeName"] != null ? r0["AssetAttributeName"].ToString() : "";
                    if (dt0 == "PointMachine"
                        || nm0.IndexOf("Combined-NWKR", StringComparison.OrdinalIgnoreCase) >= 0
                        || nm0.IndexOf("Combined-RWKR", StringComparison.OrdinalIgnoreCase) >= 0) { isPM = true; }
                    int aid0 = 0;
                    if (r0["AssetAttributeId"] != null) { int.TryParse(r0["AssetAttributeId"].ToString(), out aid0); }
                    if (aid0 == 0 && nm0.Length > 0)
                    {
                        System.Text.RegularExpressions.Match mm = System.Text.RegularExpressions.Regex.Match(
                            nm0, @"-PM-0(\d)002-Avg", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (mm.Success) { aid0 = int.Parse(mm.Groups[1].Value) * 1000 + 2; }
                    }
                    if (aid0 == 1002 || aid0 == 2002 || aid0 == 3002 || aid0 == 4002
                        || aid0 == 6002 || aid0 == 7002 || aid0 == 8002 || aid0 == 9002)
                    {
                        string rv = r0["CurrentValue"] != null ? r0["CurrentValue"].ToString()
                                  : (r0["Value"] != null ? r0["Value"].ToString() : "");
                        double dv0;
                        if (double.TryParse(rv, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out dv0)) { pmAvg[aid0] = dv0; }
                    }
                }
                int cap = isPM ? 64 : 16;   // v1.0.160.174: lift the cap for point machines
                for (int i = 0; i < rows.Count && outArr.Count < cap; i++)
                {
                    JObject r = rows[i] as JObject;
                    if (r == null) { continue; }
                    string name = r["AssetAttributeName"] != null ? r["AssetAttributeName"].ToString().Trim() : "";
                    if (name.Length == 0) { continue; }
                    // v1.0.160.174: the raw internal PM operation rows ("43020-PM-0XXXX-*") are surfaced
                    // via the op V/C cards below, not as their own ugly-named cards.
                    if (name.IndexOf("-PM-0", StringComparison.OrdinalIgnoreCase) >= 0) { continue; }
                    // v1.0.160.175: the point LIVE panel shows ONLY point-control attributes (indication
                    // relays + op V/C). Vibration and other sensors on the same asset are not part of the
                    // point's electrical health view -> skip them so they don't flood the panel now that
                    // the row cap is lifted.
                    if (isPM)
                    {
                        string nu = name.ToUpperInvariant();
                        bool ctl = nu.IndexOf("NWKR") >= 0 || nu.IndexOf("RWKR") >= 0
                                || nu.IndexOf("NWCR") >= 0 || nu.IndexOf("RWCR") >= 0
                                || PmOpChannelForName(name) != 0;
                        if (!ctl) { continue; }
                    }
                    string raw = r["CurrentValue"] != null ? r["CurrentValue"].ToString().Trim() : "";
                    double v;
                    if (!double.TryParse(raw, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out v)) { continue; }
                    // v1.0.160.174: for a point op V/C card, bind the last-operation average (from the
                    // -PM-0X002-Avg row) rather than the Periodic per-end tag that reads 0 at rest.
                    if (isPM)
                    {
                        int ch = PmOpChannelForName(name);
                        if (ch != 0 && pmAvg.ContainsKey(ch)) { v = pmAvg[ch]; }
                    }
                    JObject o = new JObject();
                    o["attr"] = name;
                    o["value"] = Math.Round(v, 3);
                    double mn, mx; bool hasMn = false, hasMx = false;
                    if (r["MinValue"] != null && double.TryParse(r["MinValue"].ToString(),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mn))
                    { o["minSafe"] = mn; hasMn = true; } else { mn = 0; }
                    if (r["MaxValue"] != null && double.TryParse(r["MaxValue"].ToString(),
                        System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out mx))
                    { o["maxSafe"] = mx; hasMx = true; } else { mx = 0; }
                    // Decide in/out HERE. Leaving it to the view means two places can disagree about
                    // what "safe" means for the same reading.
                    if (hasMn || hasMx)
                    {
                        bool below = hasMn && v < mn, above = hasMx && v > mx;
                        o["state"] = below ? "low" : (above ? "high" : "ok");
                    }
                    // Per-attribute device time: a snapshot is only as current as its OLDEST channel,
                    // and on a shunted or dead channel these differ by hours.
                    if (r["TimestampDevice"] != null)
                    {
                        DateTime td;
                        if (DateTime.TryParse(r["TimestampDevice"].ToString(),
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out td))
                        { o["at"] = td.ToString("dd/MM HH:mm"); }
                    }
                    outArr.Add(o);
                }
            }
            catch { }
            return outArr;
        }


        // SL12: build the standing-condition band from the VALIDATED ML result. Controller-built by
        // design: a model-promoted condition could be invented, mis-scoped, or dropped silently.
        // The verdict and its confidence are NOT touched -- the two answer different questions.
        internal JObject BuildAssetCondition()
        {
            try
            {
                if (string.IsNullOrEmpty(Sess._predictRaw)) { return null; }
                JObject src = null;
                try { src = JObject.Parse(Sess._predictRaw); }
                catch
                {
                    List<JToken> docs = ParseJsonDocuments(Sess._predictRaw);
                    for (int i = 0; i < docs.Count; i++) { src = docs[i] as JObject; if (src != null) { break; } }
                }
                if (src == null) { return null; }

                string cond = TokStr(src.SelectToken("$..overall_condition"));
                string status = TokStr(src.SelectToken("$..leakage_status"));
                string sev = TokStr(src.SelectToken("$..severity"));
                string simple = TokStr(src.SelectToken("$..simple_summary"));
                string reason = TokStr(src.SelectToken("$..reason"));
                if (cond.Length == 0 && simple.Length == 0) { return null; }

                bool active = status.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0;
                bool severe = cond.IndexOf("critical", StringComparison.OrdinalIgnoreCase) >= 0
                           || cond.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0
                           || sev.IndexOf("critical", StringComparison.OrdinalIgnoreCase) >= 0
                           || sev.IndexOf("high", StringComparison.OrdinalIgnoreCase) >= 0;
                bool moderate = cond.IndexOf("moderate", StringComparison.OrdinalIgnoreCase) >= 0
                             || sev.IndexOf("moderate", StringComparison.OrdinalIgnoreCase) >= 0;

                // v1.0.160.126: leakage_detected stays TRUE after a leak clears, so it cannot decide
                // whether there is a problem NOW. Only an ACTIVE status can.
                string state, title, action;
                if (active && severe) { state = "issue"; title = "Attention needed"; action = "Raise a job. Separate from this alert."; }
                else if (active) { state = "watch"; title = "Worth watching"; action = "Monitor. Raise a job if it worsens."; }
                else if (severe || moderate) { state = "ok"; title = "Recovered"; action = "No action now. Past issue, cleared."; }
                else { state = "ok"; title = "Healthy"; action = "No action."; }

                JObject o = new JObject();
                o["state"] = state;                       // ok | watch | issue -> green | amber | red
                o["title"] = title;
                o["condition"] = cond.Length > 0 ? cond : (active ? "Active" : "Normal");
                // PLAIN LANGUAGE FIRST. The ML writes this for a non-specialist; `reason` is written
                // for an engineer and was what shipped.
                if (simple.Length > 0)
                { o["summary"] = TrimWords(ScrubPlatformInternals(FilterOperationalActions(simple), "asset_condition.summary"), 60); }
                // Technical line kept, but SECOND and labelled -- AUC means nothing to a maintainer.
                if (reason.Length > 0)
                { o["detail"] = TrimWords(ScrubPlatformInternals(FilterOperationalActions(PlainifyCondition(reason)), "asset_condition.detail"), 60); }
                o["source"] = "asset condition model";
                o["action"] = action;
                o["appliesTo"] = "asset";
                return o;
            }
            catch { return null; }
        }


        internal static string TokStr(JToken t)
        { return t == null ? "" : t.ToString().Trim(); }


        // v1.0.160.126: the ML emits internal terminology. Translate the two a railway maintainer
        // will not recognise; leave everything else alone rather than paraphrasing domain terms.
        internal static string PlainifyCondition(string v)
        {
            if (string.IsNullOrEmpty(v)) { return v; }
            string o = v;
            // "Total AUC 100.09 mA-days" -- area under the leakage curve. Nobody outside the model
            // team reads that as a quantity; "leak load" says the same thing in a usable way.
            o = Regex.Replace(o, @"\bTotal\s+AUC\b", "total leak load", RegexOptions.IgnoreCase);
            o = Regex.Replace(o, @"\bAUC\b", "leak load", RegexOptions.IgnoreCase);
            o = Regex.Replace(o, @"\bmA-days\b", "mA-days of leakage", RegexOptions.IgnoreCase);
            // The model writes "Sorting"; the railway term is SHORTING.
            o = Regex.Replace(o, @"External\s+Sorting", "External Shorting", RegexOptions.IgnoreCase);
            return o;
        }


        internal bool IsTrackAssetForCircuit()
        {
            try
            {
                string at = (GetCtx(Sess._prefetchCtx, "assetType") ?? "").ToUpperInvariant();
                if (at.Length > 0) { return at.Contains("TRACK"); }
                // assetType can still be empty on some paths -- fall back to the cause code, which is
                // what every other asset-type branch in this file already does.
                string cc = (GetCtx(Sess._prefetchCtx, "causeCode") ?? "").ToUpperInvariant();
                return cc.StartsWith("TC ") || cc.Contains("RAIL RES") || cc.Contains("BALST");
            }
            catch { return false; }
        }


        // v1.0.149.0: deterministic per-day stats + sensor-health from the harvested
        // trigger-attribute series (checklist items 3/4/5 for TRACK, 2/3/6 for SIGNAL) --
        // computed here so the model reads facts instead of deriving them.
        // v1.0.152.0: assemble the multi-series trend frame the chart consumes. Includes
        // every captured attribute series, marks the trigger and any TPR series, and -- when a
        // TPR series is present -- computes a TRAIN-FREE reference value + threshold for the
        // trigger deterministically (so the chart never shows a train-polluted 0 threshold).
        internal JObject BuildTrendFrame()
        {
            JObject tf = new JObject();
            tf["schema"] = 2;
            string trigAttr = Sess._trendCandidate != null && Sess._trendCandidate["attr"] != null ? Sess._trendCandidate["attr"].ToString() : "";
            // v1.0.152.1: is the alert's triggered attribute DERIVED (RRAIL etc.)? Derived attrs
            // have NO telemetry series of their own -- so we plot the drivers, do not compute a
            // derived reference/threshold, and let the client default to all-drivers + normalized.
            string dP, dF; bool isDerived = DetectDerived(Sess._prefetchCtx, out dP, out dF);
            tf["trigger"] = isDerived ? (dP ?? trigAttr) : trigAttr;
            tf["derived"] = isDerived;
            tf["alertTs"] = GetCtx(Sess._prefetchCtx, "time");
            tf["resetTs"] = GetCtx(Sess._prefetchCtx, "resetTime");

            // Resolve the DIGITAL TPR STATE series explicitly (not a broad "TPR" substring, which
            // would also match analog TPR V). Digital state = values are ~0/1 only.
            string tprKey = ResolveTprStateKey();
            List<object[]> tprRaw = null;
            if (tprKey != null) { Sess._trendRawByAttr.TryGetValue(tprKey, out tprRaw); }

            // Build exclusion intervals from RAW TPR (epoch seconds).
            List<double[]> excl = null; string maskStatus = "unavailable";
            if (tprRaw != null && tprRaw.Count >= 2)
            {
                excl = BuildTrainExclusionRaw(tprRaw);
                // coverage: does TPR span the trigger window?
                maskStatus = TprCoversTrigger(tprRaw, trigAttr) ? "complete" : "partial";
            }

            // emit series array (chart uses ~240 downsampled points, + any gap sentinels)
            JArray sarr = new JArray();
            bool aggAnyDriver = false; bool aggAllComplete = true;   // v1.0.152.6: per-series coverage roll-up (for the derived top-level mask status)
            foreach (KeyValuePair<string, JObject> kv in Sess._trendSeriesByAttr)
            {
                JObject so = new JObject();
                bool isTpr = string.Equals(kv.Key, tprKey, StringComparison.OrdinalIgnoreCase);
                bool isTrig = !isDerived && string.Equals(kv.Key, trigAttr, StringComparison.OrdinalIgnoreCase);
                so["attr"] = kv.Value["attr"];
                so["unit"] = GuessUnit(kv.Key);
                so["n"] = kv.Value["n"];
                so["points"] = kv.Value["points"];
                so["isTrigger"] = isTrig;
                so["isTpr"] = isTpr;
                // per-series train-free reference (median/trimmed-mean) on RAW masked data.
                // v1.0.152.5 (P0): coverage is checked PER SERIES -- a driver whose window extends
                // beyond the TPR series must not have out-of-coverage samples treated as train-free.
                if (excl != null && !isTpr)
                {
                    bool cov = tprRaw != null && TprCoversTrigger(tprRaw, kv.Key);
                    aggAnyDriver = true;
                    if (cov)
                    {
                        List<object[]> raw; JObject rf = null;
                        if (Sess._trendRawByAttr.TryGetValue(kv.Key, out raw)) { rf = ComputeTrainFreeRef(raw, excl); }
                        if (rf != null) { so["reference"] = rf; so["maskStatus"] = "complete"; }
                        else { so["maskStatus"] = "insufficient_samples"; aggAllComplete = false; }   // v1.0.152.7: covered, but too few train-free samples for a reference
                    }
                    else { so["maskStatus"] = "partial"; aggAllComplete = false; }
                }
                sarr.Add(so);
            }
            // v1.0.152.3: for a derived alert, synthesize the DERIVED value series (the hero)
            // from the drivers via the card formula and put it FIRST so the chart leads with it.
            if (isDerived)
            {
                JArray dpts = SynthDerivedSeries(dF);
                if (dpts != null && dpts.Count >= 3)
                {
                    JObject dso = new JObject();
                    dso["attr"] = dP ?? "derived";
                    // v1.0.152.6: carry the derived quantity's unit from the alert card (e.g. RRAIL -> Ohm).
                    string dUnit = "";
                    try { JObject _cU = Sess._prefetchCtx != null ? Sess._prefetchCtx["alertCard"] as JObject : null; JArray _tcU = _cU != null ? _cU["triggeredConditions"] as JArray : null; if (_tcU != null && _tcU.Count > 0) { JObject _t0U = _tcU[0] as JObject; if (_t0U != null && _t0U["unit"] != null) { dUnit = _t0U["unit"].ToString(); } } } catch { }
                    dso["unit"] = dUnit;
                    dso["n"] = dpts.Count;
                    dso["points"] = dpts;
                    dso["isTrigger"] = true;    // the derived value IS the alerting quantity
                    dso["isTpr"] = false;
                    dso["isDerived"] = true;
                    sarr.Insert(0, dso);
                    if (Sess._lastSynthMaxAgeSec > 0) { tf["synthMaxDriverAgeSec"] = Math.Round(Sess._lastSynthMaxAgeSec); }
                    if (Sess._lastSynthWorstRejAgeSec > 0) { tf["synthGapMaxAgeSec"] = Math.Round(Sess._lastSynthWorstRejAgeSec); }   // v1.0.152.8: the age that caused the gap
                    if (Sess._lastSynthGapped) { tf["synthGapped"] = true; }
                    lock (Sess._trendLock)
                    {
                        if (Sess._truncatedAttrs.Count > 0)
                        {
                            JArray ta = new JArray();
                            foreach (string tAttr in Sess._truncatedAttrs) { ta.Add(tAttr); }
                            tf["truncatedAttrs"] = ta;   // v1.0.158.3: client says "cut at the row limit", not "no samples"
                        }
                    }
                    if (Sess._lastSynthEvalGapped) { tf["synthEvalGapped"] = true; }   // v1.0.152.9: formula-undefined span broke the line
                    AiLog("INFO", "TREND", "synthesized derived series " + (dP ?? "?") + " n=" + dpts.Count + " from formula" + (Sess._lastSynthGapped ? " (gapped: a driver exceeded the freshness bound)" : ""));
                }
                else { AiLog("INFO", "TREND", "derived series synth failed (drivers/formula unresolved) -- plotting drivers only"); }
            }
            tf["series"] = sarr;

            // mask block
            // v1.0.152.6 (P1): for a DERIVED alert the top-level status must reflect ALL drivers,
            // not just whichever became _trendCandidate -- complete only when every driver is covered.
            if (isDerived && tprRaw != null && aggAnyDriver) { maskStatus = aggAllComplete ? "complete" : "partial"; }
            JObject mask = new JObject();
            mask["status"] = maskStatus;
            mask["excludeSec"] = ClampCfg(ReadInt("AnalyzeTrmExcludeSec", 120), 10, 900);
            tf["mask"] = mask;

            // DIRECT alert: compute + expose the trigger train-free reference (+ carded threshold).
            // DERIVED alert: no derived series -> no top-level reference (drivers carry their own).
            if (!isDerived && excl != null && maskStatus == "complete")
            {
                List<object[]> trigRaw; JObject rf = null;
                if (Sess._trendRawByAttr.TryGetValue(trigAttr, out trigRaw)) { rf = ComputeTrainFreeRef(trigRaw, excl); }
                if (rf != null)
                {
                    tf["refValue"] = rf["median"];
                    // threshold ONLY from a defined source (alert card); else omitted.
                    double cardThr; string thrSrc;
                    if (TryCardThreshold(out cardThr, out thrSrc)) { tf["refThreshold"] = Math.Round(cardThr, 3); tf["refThresholdSource"] = thrSrc; }
                    else { tf["refThresholdSource"] = "none"; }
                    tf["trainExcluded"] = rf["trainExcluded"];
                    tf["trainKept"] = rf["usedSamples"];
                    AiLog("INFO", "TREND", "train mask(raw) applied: kept=" + rf["usedSamples"] + " excluded=" + rf["trainExcluded"] + " status=" + maskStatus + " refMedian=" + rf["median"]);
                }
                else
                {
                    // v1.0.152.7 (P0): TPR covers the window but too few train-free samples remain
                    // -> coverage is complete yet there is NO authoritative reference. Say so.
                    tf["refNote"] = "train mask complete but fewer than the minimum train-free samples remain -- no authoritative reference";
                    tf["referenceStatus"] = "insufficient_samples";
                }
            }
            else if (!isDerived && excl != null)   // v1.0.152.4: exclusion exists but coverage partial
            {
                tf["refNote"] = "train mask partial: TPR does not span the trigger window -- no authoritative train-free reference";
            }
            else if (tprKey == null)
            {
                tf["trainMask"] = "unavailable (no digital TPR state series captured)";
            }

            // v1.0.152.1: legacy top-level attr/n/points (primary series) kept ONE release so the
            // SVG fallback still renders. Primary = trigger series (direct) else the first driver
            // (derived) -- so a derived alert's SVG fallback labels a real DRIVER, not the
            // nonexistent derived series.
            JObject primary = null;
            if (!isDerived && Sess._trendSeriesByAttr.ContainsKey(trigAttr)) { primary = Sess._trendSeriesByAttr[trigAttr]; }
            if (primary == null)
            {
                foreach (KeyValuePair<string, JObject> kv in Sess._trendSeriesByAttr)
                {
                    if (string.Equals(kv.Key, tprKey, StringComparison.OrdinalIgnoreCase)) { continue; }
                    primary = kv.Value; break;
                }
            }
            if (primary != null)
            {
                tf["attr"] = primary["attr"];
                tf["n"] = primary["n"];
                tf["points"] = primary["points"];
            }
            return tf;
        }


        internal static JArray DownsampleMinMax(List<double[]> pts, int maxPts)
        {
            JArray outArr = new JArray();
            if (pts == null || pts.Count == 0) { return outArr; }
            pts.Sort(delegate (double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            if (pts.Count <= maxPts)
            {
                for (int i = 0; i < pts.Count; i++)
                { JArray p = new JArray(); p.Add((long)pts[i][0]); p.Add(Math.Round(pts[i][1], 3)); outArr.Add(p); }
                return outArr;
            }
            int buckets = Math.Max(1, maxPts / 2);   // each bucket contributes a min AND a max
            double span = pts[pts.Count - 1][0] - pts[0][0];
            if (span <= 0) { span = 1; }
            double width = span / buckets;
            int idx = 0;
            for (int b = 0; b < buckets && idx < pts.Count; b++)
            {
                double lo = pts[0][0] + b * width, hi = lo + width;
                double[] mn = null, mx = null;
                while (idx < pts.Count && (pts[idx][0] < hi || b == buckets - 1))
                {
                    if (mn == null || pts[idx][1] < mn[1]) { mn = pts[idx]; }
                    if (mx == null || pts[idx][1] > mx[1]) { mx = pts[idx]; }
                    idx++;
                }
                if (mn == null) { continue; }
                double[] first = mn[0] <= mx[0] ? mn : mx;
                double[] second = mn[0] <= mx[0] ? mx : mn;
                JArray p1 = new JArray(); p1.Add((long)first[0]); p1.Add(Math.Round(first[1], 3)); outArr.Add(p1);
                if (second[0] != first[0])
                { JArray p2 = new JArray(); p2.Add((long)second[0]); p2.Add(Math.Round(second[1], 3)); outArr.Add(p2); }
            }
            return outArr;
        }


        // Returns the REAL series for the quantity the threshold applies to, or null.
        internal JArray RealVizSeries(JObject v, out string source)
        {
            source = "unavailable";
            try
            {
                // Derived alert: the breached quantity IS the derived one, and SynthDerivedSeries
                // already computes it from the drivers via the card formula.
                // The formula is not a field -- DetectDerived yields it, the same call BuildTrendFrame
                // uses. (_derivedFormula does not exist; reading it would not have compiled.)
                string _dP, _dF;
                bool _isDer = DetectDerived(Sess._prefetchCtx, out _dP, out _dF);
                if (_isDer && !string.IsNullOrEmpty(_dF))
                {
                    JArray d = SynthDerivedSeries(_dF);
                    if (d != null && d.Count > 0)
                    {
                        List<double[]> pts = new List<double[]>();
                        for (int i = 0; i < d.Count; i++)
                        {
                            JArray pr = d[i] as JArray;
                            if (pr != null && pr.Count >= 2)
                            { pts.Add(new double[] { (double)pr[0], (double)pr[1] }); }
                        }
                        if (pts.Count > 0) { source = "derived"; return DownsampleMinMax(pts, _vizSeriesMaxPts); }
                    }
                }
                // Otherwise: the trigger attribute's own captured raw series.
                JObject z = v != null ? v["viz"] as JObject : null;
                string metric = (z != null && z["metric"] != null) ? z["metric"].ToString() : "";
                if (metric.Length == 0) { return null; }
                List<object[]> raw = null;
                if (!Sess._trendRawByAttr.TryGetValue(metric, out raw))
                {
                    foreach (KeyValuePair<string, List<object[]>> kv in Sess._trendRawByAttr)
                    {
                        string k = kv.Key ?? "";
                        if (k.Equals(metric, StringComparison.OrdinalIgnoreCase)
                            || k.IndexOf(metric, StringComparison.OrdinalIgnoreCase) >= 0
                            || metric.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        { raw = kv.Value; break; }
                    }
                }
                if (raw == null || raw.Count == 0) { return null; }
                List<double[]> rp = new List<double[]>();
                for (int i = 0; i < raw.Count; i++)
                {
                    object[] r = raw[i];
                    if (r == null || r.Length < 2) { continue; }
                    double ts, val;
                    if (!double.TryParse(Convert.ToString(r[0], System.Globalization.CultureInfo.InvariantCulture),
                            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ts)) { continue; }
                    if (!double.TryParse(Convert.ToString(r[1], System.Globalization.CultureInfo.InvariantCulture),
                            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out val)) { continue; }
                    rp.Add(new double[] { ts, val });
                }
                if (rp.Count == 0) { return null; }
                source = "measured";
                return DownsampleMinMax(rp, _vizSeriesMaxPts);
            }
            catch { source = "unavailable"; return null; }
        }


        // v1.0.152.6: AS-OF lookup -- the latest sample AT OR BEFORE t (causal; no future look-ahead,
        // which the old nearest-either-side search allowed). Under change-of-value (deadband)
        // telemetry the last change before t is the held value at t. series is sorted ascending;
        // false only when no sample exists at or before t.
        internal static bool AsOfValueAt(List<double[]> series, double t, double maxAgeSec, out double val, out double ageSec)
        {
            val = double.NaN; ageSec = double.NaN; bool found = false; double at = 0;
            for (int i = 0; i < series.Count; i++)
            {
                if (series[i][0] <= t) { val = series[i][1]; at = series[i][0]; found = true; }
                else { break; }
            }
            if (!found) { return false; }
            ageSec = t - at;
            // v1.0.152.7: a driver older than the freshness bound is NOT carried across (a
            // stopped/dead feed would otherwise produce a valid-looking flat derived line). ageSec
            // is set even on this reject so the caller can distinguish stale-gap from no-sample.
            if (maxAgeSec > 0 && ageSec > maxAgeSec) { return false; }
            return true;
        }


        // Resolve which captured attribute is the DIGITAL TPR STATE (values ~0/1), as opposed to
        // analog TPR V. Prefers a raw series whose name contains TPR and whose values are all
        // within {0,1} (+/- tolerance). Returns the attr key or null.
        internal string ResolveTprStateKey()
        {
            string best = null;
            foreach (KeyValuePair<string, List<object[]>> kv in Sess._trendRawByAttr)
            {
                // v1.0.152.4: identify TPR by the registered role flag OR the name (registry is
                // authoritative; name is the fallback for model-issued fetches).
                bool metaFlag = false; JObject soT; if (Sess._trendSeriesByAttr.TryGetValue(kv.Key, out soT) && soT["isTpr"] != null) { metaFlag = (bool)soT["isTpr"]; }
                bool nameTpr = kv.Key != null && kv.Key.ToUpperInvariant().IndexOf("TPR", StringComparison.Ordinal) >= 0;
                if (!metaFlag && !nameTpr) { continue; }
                // digital if every value is ~0 or ~1
                bool digital = true; int seen = 0;
                foreach (object[] pt in kv.Value)
                {
                    double vv; if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                    seen++;
                    // v1.0.152.5: accept ONLY values near 0 or near 1 -- the old "<=1.5" test let
                    // 0.7 / 1.4 / negatives pass as digital.
                    bool near0 = vv >= -0.2 && vv <= 0.2;
                    bool near1 = vv >= 0.8 && vv <= 1.2;
                    if (!near0 && !near1) { digital = false; break; }
                }
                if (digital && seen >= 2) { best = kv.Key; break; }
            }
            return best;
        }


        // does the raw TPR series cover the trigger series time window?
        internal bool TprCoversTrigger(List<object[]> tprRaw, string trigAttr)
        {
            // v1.0.152.4: cannot confirm coverage without a trigger series -> NOT complete (was
            // returning true, wrongly claiming authority).
            List<object[]> trig; if (!Sess._trendRawByAttr.TryGetValue(trigAttr ?? "", out trig) || trig.Count < 2) { return false; }
            // v1.0.152.4: use MIN/MAX epoch, not positional first/last -- a latest-first history
            // response would otherwise invert the window and mis-judge coverage.
            double tLo, tHi, pLo, pHi;
            if (!SpanEpoch(trig, out tLo, out tHi) || !SpanEpoch(tprRaw, out pLo, out pHi)) { return false; }
            return pLo <= tLo + 1 && pHi >= tHi - 1;
        }


        // v1.0.152.4: min/max parsed epoch over a raw series; false if fewer than 2 parse.
        internal static bool SpanEpoch(List<object[]> raw, out double lo, out double hi)
        {
            lo = double.MaxValue; hi = double.MinValue; int seen = 0;
            for (int i = 0; i < raw.Count; i++)
            {
                double ep = TrendTsToEpoch(new JValue(raw[i][0].ToString()));
                if (double.IsNaN(ep)) { continue; }
                if (ep < lo) { lo = ep; } if (ep > hi) { hi = ep; } seen++;
            }
            return seen >= 2;
        }


        // best-effort unit from attribute name (mA / V), else "".
        internal static string GuessUnit(string attr)
        {
            if (string.IsNullOrEmpty(attr)) { return ""; }
            string u = attr.ToUpperInvariant();
            if (u.Contains("MA")) { return "mA"; }
            if (u.EndsWith(" V") || u.Contains(" V ") || u.Contains("VOLT")) { return "V"; }
            return "";
        }


        // Parse a trend point timestamp (ISO local "yyyy-MM-ddTHH:mm:ss", E7 "HH:mm:ss/dd.MM.yyyy",
        // or epoch string) to epoch SECONDS. NaN on failure.
        internal static double TrendTsToEpoch(JToken tsTok)
        {
            if (tsTok == null) { return double.NaN; }
            string str = tsTok.ToString();
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(str, "^(\\d{2}):(\\d{2}):(\\d{2})/(\\d{2})\\.(\\d{2})\\.(\\d{4})$");
            if (m.Success) { str = m.Groups[6].Value + "-" + m.Groups[5].Value + "-" + m.Groups[4].Value + "T" + m.Groups[1].Value + ":" + m.Groups[2].Value + ":" + m.Groups[3].Value; }
            DateTime dt;
            if (DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out dt))
            {
                return (dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            }
            long ep; if (long.TryParse(str, out ep)) { return ep < 100000000000L ? ep : ep / 1000.0; }
            return double.NaN;
        }


        internal static string BuildPointIndicationNote()
        {
            // v1.0.151.2: built FROM _pointIndication so there is one source of truth
            // (was hardcoded separately -- the two could drift).
            StringBuilder b = new StringBuilder();
            b.Append("Point INDICATION proves which end the point sits at: ");
            for (int i = 0; i < _pointIndication.Length; i++)
            {
                string[] row = _pointIndication[i];
                b.Append(row[0]).Append("<-").Append(row[1]).Append(" up ").Append(row[2]);
                b.Append(i == _pointIndication.Length - 1 ? ". " : ", ");
            }
            b.Append("If no indication is up the point is mid-stroke or indication is LOST -- ");
            b.Append("those samples are not a settled operation.");
            return b.ToString();
        }


        // Human-readable "which relay proves which aspect" block for the analysis prompt.
        internal static string BuildAspectProofNote()
        {
            StringBuilder b = new StringBuilder();
            b.Append("Aspect is ACTIVE only when its ECR relay is proved: ");
            for (int i = 0; i < _aspectProof.Length; i++)
            {
                string[] row = _aspectProof[i];
                b.Append(row[0]).Append("<-");
                for (int j = 1; j < row.Length; j++) { if (j > 1) { b.Append("+"); } b.Append(row[j]); }
                b.Append(i == _aspectProof.Length - 1 ? "." : "; ");
            }
            return b.ToString();
        }


        internal string BuildComputedTrendBlock()
        {
            try
            {
                JObject tc = Sess._trendCandidate;
                JArray pts = tc != null ? tc["points"] as JArray : null;
                if (pts == null || pts.Count < 5) { return null; }
                List<KeyValuePair<DateTime, double>> series = new List<KeyValuePair<DateTime, double>>();
                foreach (JToken pt in pts)
                {
                    JArray pa = pt as JArray; if (pa == null || pa.Count < 2) { continue; }
                    DateTime dt; double dv;
                    string tsr = pa[0].ToString();
                    // E7 "HH:mm:ss/dd.MM.yyyy" -> ISO
                    System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(tsr, "^(\\d{2}):(\\d{2}):(\\d{2})/(\\d{2})\\.(\\d{2})\\.(\\d{4})$");
                    if (m.Success) { tsr = m.Groups[6].Value + "-" + m.Groups[5].Value + "-" + m.Groups[4].Value + "T" + m.Groups[1].Value + ":" + m.Groups[2].Value + ":" + m.Groups[3].Value; }
                    if (!DateTime.TryParse(tsr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out dt)) { continue; }
                    if (!double.TryParse(pa[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv)) { continue; }
                    series.Add(new KeyValuePair<DateTime, double>(dt, dv));
                }
                if (series.Count < 5) { return null; }
                series.Sort(delegate(KeyValuePair<DateTime, double> a, KeyValuePair<DateTime, double> b) { return a.Key.CompareTo(b.Key); });
                StringBuilder sbTrend = new StringBuilder();
                int _fetchedN = 0; try { if (tc["n"] != null) { int.TryParse(tc["n"].ToString(), out _fetchedN); } } catch { }
                if (_fetchedN > series.Count)
                {
                    sbTrend.Append("=== COMPUTED TREND & SENSOR HEALTH (deterministic; stats from ").Append(series.Count).Append(" sampled points out of ").Append(_fetchedN).Append(" fetched) ===").Append("\n");
                }
                else
                {
                    sbTrend.Append("=== COMPUTED TREND & SENSOR HEALTH (deterministic, from ").Append(series.Count).Append(" fetched samples").Append(") ===").Append("\n");
                }
                // v1.0.152.5 (P0): NAME which series this block describes. For a derived alert
                // _trendCandidate is an arbitrary DRIVER, not the derived quantity -- say so, so the
                // model does not read a component's stats as the derived trend.
                {
                    string dPh, dFh; bool isDerivedH = DetectDerived(Sess._prefetchCtx, out dPh, out dFh);
                    string blkAttr = tc != null && tc["attr"] != null ? tc["attr"].ToString() : "";
                    if (isDerivedH)
                    {
                        sbTrend.Append("NOTE: the statistics below are for COMPONENT '").Append(string.IsNullOrEmpty(blkAttr) ? "a driver" : blkAttr).Append("' -- ONE DRIVER of the derived value, NOT the derived quantity's own trend. Judge the derived alert from the per-component as-of values and the computed derived value, not this single driver.\n");
                    }
                    else if (!string.IsNullOrEmpty(blkAttr))
                    {
                        sbTrend.Append("(series: ").Append(blkAttr).Append(")\n");
                    }
                }
                // per-day min/avg/max -- key by sortable yyyyMMdd so first/last day are
                // truly chronological (v1.0.151.2: was "dd.MM" in a Dictionary, enumerated
                // unsorted, so firstAvg/lastAvg could be out of order).
                Dictionary<string, List<double>> byDay = new Dictionary<string, List<double>>(StringComparer.Ordinal);
                Dictionary<string, string> dayLabel = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<DateTime, double> kv in series)
                {
                    string dayKey = kv.Key.ToString("yyyyMMdd");
                    List<double> l; if (!byDay.TryGetValue(dayKey, out l)) { l = new List<double>(); byDay[dayKey] = l; dayLabel[dayKey] = kv.Key.ToString("dd.MM"); }
                    l.Add(kv.Value);
                }
                List<string> dayKeys = new List<string>(byDay.Keys);
                dayKeys.Sort(StringComparer.Ordinal);
                double firstAvg = 0, lastAvg = 0; int di = 0;
                foreach (string day in dayKeys)
                {
                    List<double> l = byDay[day];
                    double mn = double.MaxValue, mx = double.MinValue, sum = 0;
                    foreach (double v in l) { if (v < mn) mn = v; if (v > mx) mx = v; sum += v; }
                    double avg = sum / l.Count;
                    if (di == 0) { firstAvg = avg; }
                    lastAvg = avg; di++;
                    sbTrend.Append("day ").Append(dayLabel[day]).Append(": min=").Append(mn.ToString("0.###")).Append(" avg=").Append(avg.ToString("0.###")).Append(" max=").Append(mx.ToString("0.###")).Append(" (n=").Append(l.Count).Append(")").Append("\n");
                }
                // v1.0.151.0: occupation-resistant resting reference. Train-movement spikes
                // are a minority of samples, so the MEDIAN (and the mean of the middle 80%,
                // trimming both tails) tracks the healthy resting value far better than the
                // raw mean, which the shunt/occupation excursions drag. Reported alongside
                // so the model has a train-resistant number even without the TPR mask.
                List<double> allv = new List<double>();
                foreach (KeyValuePair<DateTime, double> kv in series) { allv.Add(kv.Value); }
                allv.Sort();
                double median = (allv.Count % 2 == 1) ? allv[allv.Count / 2]
                    : (allv[allv.Count / 2 - 1] + allv[allv.Count / 2]) / 2.0;
                int lo10 = (int)(allv.Count * 0.10), hi10 = (int)(allv.Count * 0.90);
                double tsum = 0; int tcnt = 0;
                for (int ti = lo10; ti < hi10 && ti < allv.Count; ti++) { tsum += allv[ti]; tcnt++; }
                double trimMean = tcnt > 0 ? tsum / tcnt : median;
                sbTrend.Append("resting reference (occupation-resistant): median=").Append(median.ToString("0.###"))
                       .Append(", trimmed-mean(mid 80%)=").Append(trimMean.ToString("0.###"))
                       .Append(" -- prefer these over the raw mean for the healthy baseline; the raw mean is inflated by train-movement/shunt samples.").Append("\n");
                string dir = "stable";
                if (firstAvg != 0)
                {
                    double chg = (lastAvg - firstAvg) / Math.Abs(firstAvg);
                    if (chg > 0.05) { dir = "worsening (+" + (chg * 100).ToString("0.#") + "% avg)"; }
                    else if (chg < -0.05) { dir = "improving (" + (chg * 100).ToString("0.#") + "% avg)"; }
                }
                sbTrend.Append("trend direction (first-day avg vs last-day avg): ").Append(dir).Append("\n");
                // sensor health: stale age, flatline run, spikes vs 4-sigma
                double mean = 0; foreach (KeyValuePair<DateTime, double> kv in series) { mean += kv.Value; } mean /= series.Count;
                double var2 = 0; foreach (KeyValuePair<DateTime, double> kv in series) { var2 += (kv.Value - mean) * (kv.Value - mean); } 
                double sd = Math.Sqrt(var2 / series.Count);
                int flatRun = 1, maxFlat = 1, spikes = 0;
                for (int i = 1; i < series.Count; i++)
                {
                    if (series[i].Value == series[i - 1].Value) { flatRun++; if (flatRun > maxFlat) { maxFlat = flatRun; } }
                    else { flatRun = 1; }
                    if (sd > 0 && Math.Abs(series[i].Value - mean) > 4 * sd) { spikes++; }
                }
                TimeSpan staleAge = DateTime.Now - series[series.Count - 1].Key;
                sbTrend.Append("sensor health: last sample ").Append(staleAge.TotalMinutes < 1 ? "<1 min" : (staleAge.TotalHours >= 1 ? staleAge.TotalHours.ToString("0.#") + " h" : ((int)staleAge.TotalMinutes) + " min")).Append(" old");
                sbTrend.Append("; longest flatline run=").Append(maxFlat).Append(" samples");
                sbTrend.Append("; spikes beyond 4-sigma=").Append(spikes).Append("\n");
                // v1.0.152.1: MODEL-GROUNDING -- when a digital TPR series is present, state the
                // TRAIN-FREE reference computed on the RAW trigger series (samples during
                // occupation / near TPR transitions excluded). The model is told to prefer this
                // over the raw mean, since train dips are not the asset's resting state.
                try
                {
                    string dPg, dFg; bool isDerivedG = DetectDerived(Sess._prefetchCtx, out dPg, out dFg);
                    string tprK = ResolveTprStateKey();
                    List<object[]> tprRawC = null; if (tprK != null) { Sess._trendRawByAttr.TryGetValue(tprK, out tprRawC); }
                    string trigK = tc != null && tc["attr"] != null ? tc["attr"].ToString() : "";
                    List<object[]> trigRawC = null; Sess._trendRawByAttr.TryGetValue(trigK ?? "", out trigRawC);
                    // v1.0.152.4: DERIVED alerts have no trigger telemetry -- _trendCandidate is
                    // just the largest DRIVER, so do NOT present its masked stat as "the trigger
                    // reference". Ground only a DIRECT alert, and only when TPR fully covers it.
                    if (isDerivedG)
                    {
                        sbTrend.Append("derived alert: the plotted drivers each carry their own train-free reference -- judge the derived value from the components, not any single driver's masked statistic.\n");
                    }
                    else if (tprRawC != null && tprRawC.Count >= 2 && trigRawC != null && trigRawC.Count >= 5 && TprCoversTrigger(tprRawC, trigK))
                    {
                        List<double[]> exC = BuildTrainExclusionRaw(tprRawC);
                        JObject rfC = ComputeTrainFreeRef(trigRawC, exC);
                        if (rfC != null)
                        {
                            sbTrend.Append("train-free reference (TPR-masked, prefer this over the raw mean): median=")
                                   .Append(rfC["median"]).Append(", trimmed-mean=").Append(rfC["trimmedMean"])
                                   .Append(" over ").Append(rfC["usedSamples"]).Append(" train-free samples (")
                                   .Append(rfC["trainExcluded"]).Append(" excluded as train movement via the digital TPR state). ")
                                   .Append("Train-occupation dips in the trigger are NOT fault readings -- do not treat them as the fault.\n");
                        }
                    }
                }
                catch { }
                return sbTrend.ToString();
            }
            catch { return null; }
        }


        internal void TryHarvestTrend(string toolName, JToken args, string result)
        {
            try
            {
                if (string.IsNullOrEmpty(result) || result.Length < 40) { return; }
                bool care = false;
                for (int i = 0; i < _trendTools.Length; i++)
                {
                    if (string.Equals(toolName, _trendTools[i], StringComparison.OrdinalIgnoreCase)) { care = true; break; }
                }
                if (!care) { return; }
                JToken root; try { root = JToken.Parse(result); } catch { return; }
                List<object[]> best = null;
                Queue<JToken> q = new Queue<JToken>(); q.Enqueue(root); int visited = 0; int innerParses = 0;
                while (q.Count > 0 && visited < 400)
                {
                    JToken cur = q.Dequeue(); visited++;
                    // v1.0.148.0: MCP results often wrap the real payload as a STRING --
                    // {"result":{"content":[{"text":"<json>"}]}} -- so a plain walk never
                    // reaches the series. Parse embedded JSON strings too (bounded).
                    if (cur.Type == JTokenType.String && innerParses < 6)
                    {
                        string sv = cur.ToString();
                        if (sv.Length > 80)
                        {
                            string tv = sv.TrimStart();
                            if (tv.StartsWith("{") || tv.StartsWith("["))
                            {
                                try { q.Enqueue(JToken.Parse(sv)); innerParses++; } catch { }
                            }
                        }
                        continue;
                    }
                    // v1.0.150.0: E7 history_get returns PARALLEL ARRAYS -- {"n":38,
                    // "ts":[epochSec,...],"v":[val,...]} -- not object rows. Zip them.
                    JObject po = cur as JObject;
                    if (po != null)
                    {
                        JArray tsArr = po["ts"] as JArray;
                        JArray vArr = (po["v"] as JArray) ?? (po["values"] as JArray);
                        if (tsArr != null && vArr != null && tsArr.Count >= 5 && tsArr.Count == vArr.Count)
                        {
                            List<object[]> pts2 = new List<object[]>();
                            for (int i = 0; i < tsArr.Count; i++)
                            {
                                double dv2; long ep;
                                if (!double.TryParse(vArr[i].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dv2)) { continue; }
                                string tstr = tsArr[i].ToString();
                                if (long.TryParse(tstr, out ep))
                                {
                                    // epoch seconds (10 digits) vs ms (13): normalize to ISO local
                                    DateTimeOffset dto = ep < 100000000000L
                                        ? DateTimeOffset.FromUnixTimeSeconds(ep)
                                        : DateTimeOffset.FromUnixTimeMilliseconds(ep);
                                    tstr = dto.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss");
                                }
                                pts2.Add(new object[] { tstr, dv2 });
                            }
                            if (pts2.Count >= 5 && (best == null || pts2.Count > best.Count)) { best = pts2; }
                        }
                    }
                    JArray arr = cur as JArray;
                    if (arr != null && arr.Count >= 5 && arr[0] is JObject)
                    {
                        List<object[]> pts = new List<object[]>();
                        foreach (JToken it in arr)
                        {
                            JObject io = it as JObject; if (io == null) { continue; }
                            string tsv = null; double dv = double.NaN;
                            foreach (string tk in _tsKeys) { JToken t = PropCI(io, tk); if (t != null && t.Type != JTokenType.Null) { tsv = t.ToString(); break; } }
                            foreach (string vk in _valKeys) { JToken t = PropCI(io, vk); if (t != null && t.Type != JTokenType.Null) { double dd; if (double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dd)) { dv = dd; break; } } }
                            if (tsv != null && !double.IsNaN(dv)) { pts.Add(new object[] { tsv, dv }); }
                        }
                        if (pts.Count >= 5 && (best == null || pts.Count > best.Count)) { best = pts; }
                    }
                    if (cur is JObject || cur is JArray)
                    {
                        foreach (JToken ch in cur.Children()) { q.Enqueue(ch is JProperty ? ((JProperty)ch).Value : ch); }
                    }
                }
                if (best == null) { return; }
                string attr = null;
                JObject ao = args as JObject;
                if (ao != null)
                {
                    string[] ak = { "AttributeName", "attributeName", "attribute", "TagName", "tagName", "attr" };
                    foreach (string k in ak) { JToken t = PropCI(ao, k); if (t != null && t.Type == JTokenType.String && t.ToString().Length > 0) { attr = t.ToString(); break; } }
                }
                // v1.0.152.4: prefetch calls carry a TagId, not an AttributeName -> recover the
                // real attribute + role from the registry populated at fetch time.
                bool metaTpr = false, metaTrig = false;
                if (ao != null)
                {
                    string tagId = null;
                    string[] tik = { "TagId", "tagId", "TagID", "tagid" };
                    foreach (string k in tik) { JToken t = PropCI(ao, k); if (t != null && t.Type != JTokenType.Null) { tagId = t.ToString(); break; } }
                    if (!string.IsNullOrEmpty(tagId)) { JObject mm; lock (Sess._trendLock) { Sess._tagMeta.TryGetValue(tagId, out mm); } if (mm != null) { if (string.IsNullOrEmpty(attr) && mm["attr"] != null) { attr = mm["attr"].ToString(); } metaTpr = mm["isTpr"] != null && (bool)mm["isTpr"]; metaTrig = mm["isTrigger"] != null && (bool)mm["isTrigger"]; } }
                }
                int score = best.Count;
                try
                {
                    JObject card = Sess._prefetchCtx != null ? Sess._prefetchCtx["alertCard"] as JObject : null;
                    JArray tc = card != null ? card["triggeredConditions"] as JArray : null;
                    string lbl = (tc != null && tc.Count > 0 && tc[0]["paramLabel"] != null) ? tc[0]["paramLabel"].ToString().Trim() : null;
                    if (!string.IsNullOrEmpty(lbl) && !string.IsNullOrEmpty(attr)
                        && (attr.IndexOf(lbl, StringComparison.OrdinalIgnoreCase) >= 0 || lbl.IndexOf(attr, StringComparison.OrdinalIgnoreCase) >= 0))
                    { score += 100000; }
                }
                catch { }
                int n = best.Count, cap = 240;
                JArray pj = new JArray();
                if (n <= cap) { foreach (object[] pt in best) { pj.Add(new JArray(pt[0], pt[1])); } }
                else { for (int i = 0; i < cap; i++) { int idx = (int)(((long)i * (n - 1) + (cap - 1) / 2) / (cap - 1)); if (idx > n - 1) { idx = n - 1; } object[] pt = best[idx]; pj.Add(new JArray(pt[0], pt[1])); } }   // v1.0.151.2: retain first AND last sample
                JObject tr = new JObject();
                tr["attr"] = attr ?? "";
                tr["n"] = n;
                tr["points"] = pj;
                tr["isTpr"] = metaTpr;                 // v1.0.152.4: role from the TagId registry
                if (metaTrig) { score += 100000; }     // a registered trigger tag counts as the trigger
                // v1.0.152.0: keep EVERY attribute's series for the multi-line chart (dedup by
                // attr name; a later, larger fetch for the same attr replaces the earlier one).
                // v1.0.152.4 (P1): guard the shared series maps against concurrent history harvests.
                lock (Sess._trendLock)
                {
                    // v1.0.152.7 (P1): ONLY identified series (a real attribute name from args or the
                    // TagId registry) enter the multi-series chart + the derived mask aggregate. An
                    // anonymous range-history result would otherwise land as "seriesN", consume the
                    // 6-cap, and be counted as an uncovered driver -> a false "partial". It may still
                    // feed the legacy _trendCandidate (the registered trigger's +100000 score wins).
                    if (!string.IsNullOrEmpty(attr))
                    {
                        JObject prevS; bool hadPrev = Sess._trendSeriesByAttr.TryGetValue(attr, out prevS);
                        if (!hadPrev || (prevS["n"] != null && n > (int)prevS["n"]))
                        {
                            if (Sess._trendSeriesByAttr.Count < 6 || hadPrev) { Sess._trendSeriesByAttr[attr] = tr; Sess._trendRawByAttr[attr] = best; }   // v1.0.152.1: keep RAW too
                        }
                    }
                    // trigger/stats series = highest score (unchanged behaviour for computed stats)
                    if (Sess._trendCandidate == null || score > Sess._trendScore) { Sess._trendCandidate = tr; Sess._trendScore = score; }
                }
                AiLog("INFO", "TREND", "harvested tool=" + toolName + " attr=" + (attr ?? "?") + " n=" + n + " score=" + score + " seriesTotal=" + Sess._trendSeriesByAttr.Count);
            }
            catch { }
        }


        internal void TryHarvestGrounding(string toolName, string result)
        {
            try
            {
                if (Sess._prefetchCtx == null || string.IsNullOrEmpty(result)) { return; }
                bool care = false;
                for (int i = 0; i < _groundingTools.Length; i++)
                {
                    if (string.Equals(toolName, _groundingTools[i], StringComparison.OrdinalIgnoreCase)) { care = true; break; }
                }
                if (!care) { return; }
                string alertId = GetCtx(Sess._prefetchCtx, "alertId");
                if (string.IsNullOrEmpty(alertId)) { return; }
                string sid = HarvestIdToken(result, "SiteId");
                if (sid == null) { sid = HarvestIdToken(result, "site_id"); }
                string aid = HarvestIdToken(result, "AssetId");
                if (aid == null) { aid = HarvestIdToken(result, "asset_id"); }
                if (sid == null && aid == null) { return; }
                JObject g = _groundingByAlert.GetOrAdd(alertId, delegate(string k) { return new JObject(); });
                lock (g)
                {
                    if (sid != null && g["siteId"] == null) { g["siteId"] = sid; }
                    if (aid != null && g["assetId"] == null) { g["assetId"] = aid; }
                    g["ts"] = DateTime.UtcNow.ToString("o");
                }
                if (_groundingByAlert.Count > 300)
                {
                    foreach (string k in _groundingByAlert.Keys) { JObject drop; _groundingByAlert.TryRemove(k, out drop); if (_groundingByAlert.Count <= 200) { break; } }
                }
            }
            catch { }
        }


        // First positive integer following "<key>": in a JSON-ish blob (quotes optional).
        internal static string HarvestIdToken(string text, string key)
        {
            try
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                    text, "\"?" + key + "\"?\\s*[:=]\\s*\"?(\\d{1,10})\"?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string v = m.Groups[1].Value;
                    long n; if (long.TryParse(v, out n) && n > 0) { return v; }
                }
            }
            catch { }
            return null;
        }


        // Merge cached grounding into a follow-up chat ctx (fills only MISSING ids).
        internal static bool MergeGrounding(JObject ctx)
        {
            try
            {
                if (ctx == null) { return false; }
                string alertId = GetCtx(ctx, "alertId");
                if (string.IsNullOrEmpty(alertId)) { return false; }
                JObject g; if (!_groundingByAlert.TryGetValue(alertId, out g)) { return false; }
                bool merged = false;
                lock (g)
                {
                    if (string.IsNullOrEmpty(GetCtx(ctx, "siteId")) && g["siteId"] != null) { ctx["siteId"] = g["siteId"].ToString(); merged = true; }
                    if (string.IsNullOrEmpty(GetCtx(ctx, "assetId")) && g["assetId"] != null) { ctx["assetId"] = g["assetId"].ToString(); merged = true; }
                }
                return merged;
            }
            catch { return false; }
        }


        // v1.0.160.37: build the computed-verdict decision from the sustain result and stash it on the
        // instance. Owner rule (PREDICTIVE/ANALOG): a breach sustained for >= the FRS window is CONFIRMED;
        // a breach that did not hold is NOT_CONFIRMED (transient). Coherence, where structured, scales the
        // confidence; where not defined the breach alone decides (owner ruling). Best-effort: any fault
        // leaves _computedVerdict null so the model keeps deciding. No AiLog/prefetch calls (no CS0120).
        // v1.0.160.42: build the digital TPR state series (epoch seconds, value 0/1) for the gate,
        // from the already-fetched raw TPR trend. Empty when no digital TPR series is resolvable
        // (=> the gate is a no-op and samples are kept). No I/O.
        // v1.0.160.115 CUT B. OBSERVE-ONLY: classify and record. No verdict, confidence or caveat
        // depends on any of this.
        internal JArray BuildSensorHealth()
        {
            JArray outArr = new JArray();
            try
            {
                if (Sess._trendRawByAttr == null || Sess._trendRawByAttr.Count == 0) { return outArr; }
                // Occupancy, from the digital TPR state we already fetch. Recorded per channel rather
                // than applied as a blanket rule: a shunt suppresses If/Ir/Vf/TPR V but NOT the
                // charger family, so "occupied" does not excuse silence on every channel.
                List<double[]> tpr = BuildTprStateSeries();
                bool occupiedAtEnd = false;
                double occSinceEpoch = 0;
                if (tpr != null && tpr.Count > 0)
                {
                    tpr.Sort(delegate (double[] x, double[] y) { return x[0].CompareTo(y[0]); });
                    double[] last = tpr[tpr.Count - 1];
                    occupiedAtEnd = last[1] < 0.5;   // 0 = DROP = occupied
                    if (occupiedAtEnd) { occSinceEpoch = last[0]; }
                }
                foreach (KeyValuePair<string, List<object[]>> kv in Sess._trendRawByAttr)
                {
                    List<object[]> raw = kv.Value;
                    if (raw == null || raw.Count == 0) { continue; }
                    List<double> eps = new List<double>();
                    List<double> vals = new List<double>();
                    for (int i = 0; i < raw.Count; i++)
                    {
                        object[] pt = raw[i];
                        if (pt == null || pt.Length < 2 || pt[0] == null || pt[1] == null) { continue; }
                        double ep = TrendTsToEpoch(new JValue(pt[0].ToString()));
                        if (double.IsNaN(ep)) { continue; }
                        double vv;
                        if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                        eps.Add(ep); vals.Add(vv);
                    }
                    if (eps.Count == 0) { continue; }
                    double first = eps[0], lastTs = eps[0];
                    for (int i = 1; i < eps.Count; i++)
                    { if (eps[i] < first) { first = eps[i]; } if (eps[i] > lastTs) { lastTs = eps[i]; } }
                    double spanH = (lastTs - first) / 3600.0;
                    JObject o = new JObject();
                    o["attr"] = kv.Key;
                    o["rows"] = eps.Count;
                    o["observedSpanHours"] = Math.Round(spanH, 2);
                    // OBSERVED rate over the rows actually returned -- NOT rows/requestedWindow, which
                    // understates a capped block by an order of magnitude.
                    o["observedRate"] = spanH > 0 ? Math.Round((eps.Count - 1) / spanH, 2) : 0;
                    // A block returning exactly the cap is CENSORED: its rate is a lower bound and its
                    // boundary sample is not necessarily the nearest reading.
                    o["censored"] = (eps.Count >= 500);
                    o["lastSampleAgeSec"] = (long)(ToEpochIst(DateTime.Now) - lastTs);
                    // longest run of identical values -- a frozen channel and a steady one look the
                    // same on value alone, which is the whole difficulty.
                    int flat = 1, maxFlat = 1;
                    for (int i = 1; i < vals.Count; i++)
                    { if (vals[i] == vals[i - 1]) { flat++; if (flat > maxFlat) { maxFlat = flat; } } else { flat = 1; } }
                    o["longestFlatRun"] = maxFlat;
                    if (occupiedAtEnd)
                    {
                        o["trackOccupied"] = true;
                        o["occupiedSinceSec"] = (long)(ToEpochIst(DateTime.Now) - occSinceEpoch);
                    }
                    outArr.Add(o);
                }
            }
            catch { }
            return outArr;
        }


        internal List<double[]> BuildTprStateSeries()
        {
            List<double[]> outp = new List<double[]>();
            string tprKey = ResolveTprStateKey();
            if (tprKey == null) { return outp; }
            List<object[]> raw;
            if (!Sess._trendRawByAttr.TryGetValue(tprKey, out raw) || raw == null) { return outp; }
            foreach (object[] pt in raw)
            {
                if (pt == null || pt.Length < 2 || pt[0] == null || pt[1] == null) { continue; }
                double ep = TrendTsToEpoch(new JValue(pt[0].ToString()));
                if (double.IsNaN(ep)) { continue; }
                double vv;
                if (!double.TryParse(pt[1].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out vv)) { continue; }
                outp.Add(new double[] { ep, vv >= 0.5 ? 1.0 : 0.0 });
            }
            outp.Sort(delegate(double[] a, double[] b) { return a[0].CompareTo(b[0]); });
            return outp;
        }


        // v1.0.160.42: keep only the component samples taken while the relay is steady UP.
        internal static List<double[]> FilterCompSeriesByGate(List<double[]> cs, List<double[]> tpr, int guardSec)
        {
            if (cs == null) { return new List<double[]>(); }
            if (tpr == null || tpr.Count == 0) { return cs; }
            List<double[]> outp = new List<double[]>(cs.Count);
            for (int i = 0; i < cs.Count; i++)
            {
                if (cs[i] != null && cs[i].Length >= 2 && GateSteadyUp(tpr, cs[i][0], guardSec)) { outp.Add(cs[i]); }
            }
            return outp;
        }
    }
}
