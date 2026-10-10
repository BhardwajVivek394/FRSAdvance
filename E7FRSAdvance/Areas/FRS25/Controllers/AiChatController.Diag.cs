// AiChatController.Diag.cs -- v1.0.160.160
// DIAGNOSTIC PROBE CATALOG. As of .160 this file holds NO ACTIONS: the routes, the gate and
// the view live in the separate AiDiagController (owner: "one separate controller with view
// for Diag"). What stays here is the probe body and its two helpers, because they call ~19
// PRIVATE members of the analysis controller -- PrefetchCallAsync, LoadAllToolsAsync,
// HistWindowArgs/HistFetchAsync, NewSiteAlertArgs/NewAssetHistoryArgs/NewRangeHistoryArgs,
// IsE7ApiFailureText, LooksUsableToolResult, the audit-capability fields. Moving it out would
// mean widening all of those on proven code, or duplicating them; both are worse than one
// internal entry point. AiDiagController calls DiagRunCoreAsync() and DiagAllowedCore().
// The catalog runs against a site/asset the OPERATOR picked live from the Historian API
// (owner decision: no canary config keys). Every probe reuses the run-time plumbing --
// LoadAllToolsAsync, PrefetchCallAsync, HistWindowArgs/HistFetchAsync, IsE7ApiFailureText,
// LooksUsableToolResult -- so a green tile certifies the exact code path an analysis takes,
// not a parallel reimplementation. No model calls anywhere: a full run costs tool time only.
// Gate: AiDebugAllowed(), same policy as ToolTrace/RunLogFeed. E-group checks that need a
// DataAPI list endpoint (last-24h runs, degraded counts) render SKIP with the reason until
// that endpoint exists -- a grey tile that names its dependency beats a missing tile.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    public partial class AiChatController
    {
        private class DiagCheck
        {
            public string Id { get; set; }
            public string Group { get; set; }
            public string Name { get; set; }
            public string Status { get; set; }
            public long Ms { get; set; }
            public string Detail { get; set; }
        }

        // v1.0.160.158: the Diag surface is tighter than the rest of debug/tools (owner):
        // site-keeping is REQUIRED, and when DiagAllowedEmails is configured in web.config
        // (semicolon/comma-separated), the logged-in email must ALSO be on that list. An
        // empty/absent key means site-keeping alone suffices -- a configured-but-empty
        // allowlist must never lock every operator out of a diagnostics page.
        // v1.0.160.161: CaptureLoginUser() is private and must stay private -- every analysis
        // path depends on it running at a precise point. This shim lets AiDiagController do the
        // same capture the old in-controller Diag actions did as their first statement, without
        // widening the original.
        internal void CaptureLoginUserForDiag()
        {
            CaptureLoginUser();
        }

        internal bool DiagAllowedCore()
        {
            if (!AiDebugAllowed())
            {
                return false;
            }
            string list = null;
            try { list = System.Configuration.ConfigurationManager.AppSettings["DiagAllowedEmails"]; }
            catch { }
            if (string.IsNullOrEmpty(list) || list.Trim().Length == 0)
            {
                return true;
            }
            string em = (_luEmail ?? "").Trim();
            if (em.Length == 0)
            {
                return false;
            }
            string[] parts = list.Split(new char[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), em, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // v1.0.160.164: ProxyBaseUrl is the key (owner). DiagHistorianBase is still read as a
        // fallback so an existing deployment does not lose its setting on upgrade.
        internal static string DiagHistorianBase()
        {
            string v = null;
            try { v = System.Configuration.ConfigurationManager.AppSettings["ProxyBaseUrl"]; }
            catch { }
            if (string.IsNullOrEmpty(v))
            {
                try { v = System.Configuration.ConfigurationManager.AppSettings["DiagHistorianBase"]; }
                catch { }
            }
            return string.IsNullOrEmpty(v) ? "http://proxy.energy7.org:8083" : v.Trim().TrimEnd('/');
        }

        // v1.0.160.164: the browser must NOT call the proxy directly (owner). The historian fetches
        // that fed the Diag pickers ran from the page, which exposed the proxy host to every client,
        // needed CORS, and put an internal endpoint in the browser's network tab. They now run HERE
        // and the page calls same-origin actions instead. Its OWN HttpClient with a short timeout:
        // a picker fetch must never inherit a model-length one.
        private static readonly System.Net.Http.HttpClient _diagHttp = BuildDiagHttp();
        private static System.Net.Http.HttpClient BuildDiagHttp()
        {
            System.Net.Http.HttpClient h = new System.Net.Http.HttpClient();
            try { h.Timeout = TimeSpan.FromMilliseconds(ClampCfg(ReadInt("DiagProxyTimeoutMs", 8000), 1000, 60000)); }
            catch { }
            return h;
        }

        // GET a path off the proxy base and hand back the raw body. Returns null on any failure --
        // the caller turns that into an explicit FAIL tile, never a silent empty list.
        internal static async Task<string> DiagProxyGetAsync(string relativePath)
        {
            string url = DiagHistorianBase() + relativePath;
            try
            {
                System.Net.Http.HttpResponseMessage r = await _diagHttp.GetAsync(url).ConfigureAwait(false);
                string body = await r.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!r.IsSuccessStatusCode)
                {
                    AiLogStatic("WARN", "DIAG", "proxy GET " + relativePath + " -> HTTP " + (int)r.StatusCode);
                    return null;
                }
                return body;
            }
            catch (Exception ex)
            {
                AiLogStatic("WARN", "DIAG", "proxy GET " + relativePath + " failed: " + ex.Message);
                return null;
            }
        }

        // Head of a payload for the detail line -- never the whole thing.
        private static string DiagHead(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            string t = s.Trim().Replace("\r", " ").Replace("\n", " ");
            return t.Length > max ? t.Substring(0, max) : t;
        }

        // Shared classification: the same three failure classes this week produced.
        // FAIL: E7-API failure text (session/403), unusable result (schema rejection text),
        //       no result at all. WARN: usable but thin. OK: everything else.
        private static DiagCheck DiagClassify(string id, string group, string name, string res, long ms, int thinBytes)
        {
            DiagCheck c = new DiagCheck();
            c.Id = id;
            c.Group = group;
            c.Name = name;
            c.Ms = ms;
            if (string.IsNullOrEmpty(res))
            {
                c.Status = "FAIL";
                c.Detail = "no result (timeout, tool missing, or task not started)";
                return c;
            }
            if (IsE7ApiFailureText(res))
            {
                c.Status = "FAIL";
                c.Detail = "E7 API failure: " + DiagHead(res, 140);
                return c;
            }
            if (!LooksUsableToolResult(res, false))
            {
                c.Status = "FAIL";
                c.Detail = "unusable result: " + DiagHead(res, 140);
                return c;
            }
            if (res.Length < thinBytes)
            {
                c.Status = "WARN";
                c.Detail = "thin (" + res.Length + " B): " + DiagHead(res, 120);
                return c;
            }
            c.Status = "OK";
            c.Detail = res.Length + " B: " + DiagHead(res, 100);
            return c;
        }

        // v1.0.160.160: internal entry point. AiDiagController owns the route, the gate and the
        // 403; this runs the catalog and hands back the payload. It captures the login itself so
        // AiLog/AiDebugAllowed behave exactly as they do on an analysis request.
        internal async Task<JObject> DiagRunCoreAsync(string siteId, string assetId, string assetName, string assetType)
        {
            CaptureLoginUser();
            siteId = (siteId ?? "").Trim();
            assetId = (assetId ?? "").Trim();
            assetName = (assetName ?? "").Trim();
            assetType = (assetType ?? "").Trim();
            var swAll = System.Diagnostics.Stopwatch.StartNew();
            List<DiagCheck> checks = new List<DiagCheck>();
            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            System.Threading.CancellationToken ct = cts.Token;

            // ---- A2: tool discovery. Everything else depends on it, so it runs first, alone.
            ToolLoadResult loaded = null;
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string err = null;
                try { loaded = await LoadAllToolsAsync(ct).ConfigureAwait(false); }
                catch (Exception ex) { err = ex.Message; }
                sw.Stop();
                DiagCheck a2 = new DiagCheck();
                a2.Id = "A2";
                a2.Group = "A";
                a2.Name = "MCP tool discovery";
                a2.Ms = sw.ElapsedMilliseconds;
                int nTools = (loaded != null && loaded.Tools != null) ? loaded.Tools.Count : 0;
                string loadErr = err;
                if (loadErr == null && loaded != null)
                {
                    loadErr = !string.IsNullOrEmpty(loaded.Error1) ? loaded.Error1 : loaded.Error2;
                }
                if (nTools <= 0)
                {
                    a2.Status = "FAIL";
                    a2.Detail = "no tools discovered" + (loadErr != null ? (" -- " + DiagHead(loadErr, 100)) : "");
                }
                else if (!string.IsNullOrEmpty(loadErr))
                {
                    a2.Status = "WARN";
                    a2.Detail = nTools + " tools, one server erred: " + DiagHead(loadErr, 100);
                }
                else
                {
                    a2.Status = "OK";
                    a2.Detail = nTools + " tools (" + (loaded != null ? loaded.Count1 : 0) + "+" + (loaded != null ? loaded.Count2 : 0) + ")";
                }
                checks.Add(a2);
            }

            // ---- A3: audit DataAPI capability as probed at startup (no fresh call: the probe has
            // its own retry/dormancy machinery; this reports its verdict).
            {
                DiagCheck a3 = new DiagCheck();
                a3.Id = "A3";
                a3.Group = "A";
                a3.Name = "Audit DataAPI (/api/AI*)";
                a3.Ms = 0;
                int cap;
                lock (_auditLock) { cap = _auditCapable; }
                if (cap == 1)
                {
                    a3.Status = "OK";
                    a3.Detail = "audit DB active (min " + _auditDbMinVer + ")";
                }
                else if (cap == 0)
                {
                    a3.Status = "FAIL";
                    a3.Detail = "DataAPI predates /api/AI* or probe failed -- audit dormant";
                }
                else
                {
                    a3.Status = "WARN";
                    a3.Detail = "not yet probed this process";
                }
                checks.Add(a3);
            }

            bool haveSite = siteId.Length > 0;
            bool haveAsset = assetId.Length > 0;
            DateTime now = DateTime.Now;

            if (loaded != null && loaded.Tools != null && loaded.Tools.Count > 0 && haveSite && haveAsset)
            {
                // ---- Fire the tool probes in parallel; each is classified independently.
                long sN; long aN;
                long.TryParse(siteId, out sN);
                long.TryParse(assetId, out aN);

                JObject rangeArgs = new JObject();
                rangeArgs["SiteId"] = sN;
                rangeArgs["AssetId"] = assetId;

                JObject tagsArgs = new JObject();
                tagsArgs["SiteId"] = sN;
                tagsArgs["AssetId"] = assetId;

                JObject searchArgs = new JObject();
                if (assetName.Length > 0) { searchArgs["AssetName"] = assetName; }
                searchArgs["SiteId"] = sN;

                var tRange = PrefetchCallAsync("get_attribute_range", rangeArgs, loaded, ct);
                var tTags = PrefetchCallAsync("get_asset_tags", tagsArgs, loaded, ct);
                var tSearch = assetName.Length > 0 ? PrefetchCallAsync("search_tags", searchArgs, loaded, ct) : null;
                var tFrsSite = PrefetchCallAsync("get_frs_alerts", NewSiteAlertArgs(siteId, now), loaded, ct);
                var tFrsAsset = PrefetchCallAsync("get_frs_alerts", NewAssetHistoryArgs(siteId, assetId, now, _assetHistDays), loaded, ct);
                var tRangeHist = PrefetchCallAsync("get_attribute_range_history", NewRangeHistoryArgs(assetId, now), loaded, ct);
                // D2/D3 combined: last-hour window through the REAL HistWindowArgs/HistFetchAsync
                // path, so the HistGetSendsUtc conversion under test is the production one.
                var tHist = HistFetchAsync(HistWindowArgs("AssetId", assetId, now.AddHours(-1), now, 50, "json"), loaded, ct, true);

                string rRange = await SafeAwait(tRange).ConfigureAwait(false);
                string rTags = await SafeAwait(tTags).ConfigureAwait(false);
                string rSearch = tSearch != null ? await SafeAwait(tSearch).ConfigureAwait(false) : null;
                string rFrsSite = await SafeAwait(tFrsSite).ConfigureAwait(false);
                string rFrsAsset = await SafeAwait(tFrsAsset).ConfigureAwait(false);
                string rRangeHist = await SafeAwait(tRangeHist).ConfigureAwait(false);
                string rHist = await SafeAwait(tHist).ConfigureAwait(false);
                long msPar = swAll.ElapsedMilliseconds;

                // A1 doubles as the SESSION check: an expired E7 session shows here first.
                checks.Add(DiagClassify("A1", "A", "E7 session via get_attribute_range", rRange, msPar, 60));
                checks.Add(DiagClassify("B1", "B", "get_asset_tags", rTags, msPar, 100));
                if (rSearch != null)
                {
                    DiagCheck c1 = DiagClassify("C1", "C", "search_tags resolves asset", rSearch, msPar, 60);
                    if (c1.Status == "OK" && assetName.Length > 0
                        && rSearch.IndexOf(assetName, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        c1.Status = "WARN";
                        c1.Detail = "rows returned but requested asset '" + assetName + "' not present -- " + DiagHead(rSearch, 90);
                    }
                    checks.Add(c1);
                }
                else
                {
                    checks.Add(new DiagCheck { Id = "C1", Group = "C", Name = "search_tags resolves asset", Status = "SKIP", Ms = 0, Detail = "no assetName supplied" });
                }
                checks.Add(DiagClassify("B3", "B", "get_frs_alerts (site window)", rFrsSite, msPar, 40));
                // The exact .155 shape under test: SiteIds + AssetIds together.
                checks.Add(DiagClassify("B4", "B", "get_frs_alerts (asset history, SiteIds+AssetIds)", rFrsAsset, msPar, 40));
                checks.Add(DiagClassify("B5", "B", "get_attribute_range_history", rRangeHist, msPar, 60));

                // D2+D3: window sanity + freshness from the same fetch. The 330-min skew class
                // (HistGetSendsUtc wrong for the server) shows as every timestamp sitting far
                // outside the requested hour.
                {
                    DiagCheck d2 = new DiagCheck();
                    d2.Id = "D2";
                    d2.Group = "D";
                    d2.Name = "History window sanity + freshness (1 h)";
                    d2.Ms = msPar;
                    if (string.IsNullOrEmpty(rHist) || !LooksUsableToolResult(rHist, false))
                    {
                        d2.Status = IsE7ApiFailureText(rHist) ? "FAIL" : "WARN";
                        d2.Detail = string.IsNullOrEmpty(rHist) ? "no result" : DiagHead(rHist, 120);
                    }
                    else
                    {
                        DateTime best = DateTime.MinValue;
                        foreach (System.Text.RegularExpressions.Match m in
                                 System.Text.RegularExpressions.Regex.Matches(rHist, "\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}"))
                        {
                            DateTime t;
                            if (DateTime.TryParse(m.Value, out t) && t > best)
                            {
                                best = t;
                            }
                        }
                        if (best == DateTime.MinValue)
                        {
                            d2.Status = "WARN";
                            d2.Detail = "no timestamps in response -- site may be stale (" + rHist.Length + " B)";
                        }
                        else
                        {
                            double offMin = (best - now).TotalMinutes;
                            if (offMin > 30.0 || offMin < -90.0)
                            {
                                d2.Status = "FAIL";
                                d2.Detail = "freshest sample " + Math.Round(Math.Abs(offMin)) + " min "
                                    + (offMin > 0 ? "IN THE FUTURE" : "old") + " for a 1-h window -- timezone/window skew class (HistGetSendsUtc=" + _histGetSendsUtc + ")";
                            }
                            else
                            {
                                d2.Status = offMin < -10.0 ? "WARN" : "OK";
                                d2.Detail = "freshest sample " + Math.Round(Math.Abs(offMin)) + " min old (HistGetSendsUtc=" + _histGetSendsUtc + ")";
                            }
                        }
                    }
                    checks.Add(d2);
                }
            }
            else
            {
                checks.Add(new DiagCheck { Id = "B0", Group = "B", Name = "tool probes", Status = "SKIP", Ms = 0, Detail = haveSite && haveAsset ? "tool discovery failed -- see A2" : "select a site and asset, then Run" });
            }

            // ---- E group: honest SKIPs until the DataAPI list endpoint exists.
            checks.Add(new DiagCheck { Id = "E1", Group = "E", Name = "Runs last 24 h (count, success, orphans)", Status = "SKIP", Ms = 0, Detail = "needs /api/AIAnalysis list-by-date endpoint -- pending DataAPI" });
            checks.Add(new DiagCheck { Id = "E2", Group = "E", Name = "Degraded runs last 24 h", Status = "SKIP", Ms = 0, Detail = "needs the DegradedInputs column whitelisted server-side (.154 sends it)" });

            swAll.Stop();
            List<object> outChecks = new List<object>();
            int nOk = 0, nWarn = 0, nFail = 0, nSkip = 0;
            foreach (DiagCheck c in checks)
            {
                if (c.Status == "OK") { nOk++; }
                else if (c.Status == "WARN") { nWarn++; }
                else if (c.Status == "FAIL") { nFail++; }
                else { nSkip++; }
                outChecks.Add(new { id = c.Id, group = c.Group, name = c.Name, status = c.Status, ms = c.Ms, detail = c.Detail });
            }
            AiLog("INFO", "DIAG", "run site=" + siteId + " asset=" + assetId + " -> ok=" + nOk + " warn=" + nWarn + " fail=" + nFail + " skip=" + nSkip + " in " + swAll.ElapsedMilliseconds + "ms");
            JObject res = new JObject();
            res["ok"] = true;
            res["ranAt"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            res["tookMs"] = swAll.ElapsedMilliseconds;
            res["siteId"] = siteId;
            res["assetId"] = assetId;
            res["assetName"] = assetName;
            JObject sum = new JObject();
            sum["okCount"] = nOk;
            sum["warn"] = nWarn;
            sum["fail"] = nFail;
            sum["skip"] = nSkip;
            res["summary"] = sum;
            res["checks"] = JArray.FromObject(outChecks);
            return res;
        }
    }
}
