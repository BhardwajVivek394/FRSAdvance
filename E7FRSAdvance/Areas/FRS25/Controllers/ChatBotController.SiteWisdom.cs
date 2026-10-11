using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // ============================================================================
    // v1.3.2.0 -- merged the DEEPER PER-ASSET DETAIL sources (track-health, pm-operation,
    //            range-history) but gated ON DEMAND: single named asset / explicit deep-check only.
    //            A site maintenance plan stays on the code-enforced fast path (v1.3.1.2+).
    // v1.3.1.0 -- HARDCODED SITE WISDOM (one file).
    //
    // When a user asks about a known site, the fixed facts for that site (identity,
    // asset inventory, maintenance-roster rules, and the reusable diagnostic patterns
    // seen there) are injected into the system prompt as authoritative grounding, so
    // the chatbot answers "G Cabin maintenance" from this reference instead of guessing.
    //
    // This holds only the STABLE reference. CURRENT alert/state/values are never baked
    // here -- the model is told to fetch them live (frs_alerts_by_site, frs_attribute_range,
    // live/history). The few recent items kept below are DATED and marked verify-live.
    //
    // Pattern mirrors CauseLogic: one match on the latest user turn, one injected block,
    // "" when no known site is named (no effect on other questions). Add a site by adding
    // one SiteDef to _sites.
    // ============================================================================
    public partial class ChatBotController
    {
        private sealed class SiteDef
        {
            public int SiteId;
            public string Name;         // canonical
            public string[] Aliases;    // lower-case forms to match in the user text
            public string Block;        // the grounding text injected when matched
        }

        private static string BuildSiteWisdomRule(List<JObject> messages)
        {
            try
            {
                string u = (LastUserTextForCause(messages) ?? "").ToLowerInvariant();
                if (u.Length == 0) { return ""; }
                string un = Regex.Replace(u, @"[^a-z0-9 ]+", " ");
                un = Regex.Replace(un, @"\s+", " ").Trim();
                string pad = " " + un + " ";

                foreach (SiteDef s in _sites)
                {
                    bool hit = false;
                    foreach (string a in s.Aliases)
                    {
                        if (pad.IndexOf(" " + a + " ", StringComparison.Ordinal) >= 0) { hit = true; break; }
                    }
                    // also "site 71", "siteid 71", "site id 71", "siteid=71" -> the numeric id
                    if (!hit && Regex.IsMatch(u, @"site\s*id\s*[:=]?\s*" + s.SiteId + @"\b")) { hit = true; }
                    if (!hit && Regex.IsMatch(u, @"\bsite\s*" + s.SiteId + @"\b")) { hit = true; }
                    if (hit) { return s.Block; }
                }
                return "";
            }
            catch { return ""; }
        }

        // ── v1.3.1.2: code-enforced fast path for site maintenance plans ──────────────
        // Steps allowed when the fast path is on: step 1 = the alert call, step 2 = the forced
        // tool-less answer. (The loop guard withholds tools on the last allowed step.)
        private const int SiteFastPathMaxIter = 3;   // alerts, one scoped track-health, forced answer

        // Tools the fast path keeps -- alert lookups only. Everything else (predict_*, history,
        // live values, attribute ranges, search) is pruned for the plan turn.
        private static readonly string[] _siteFastToolAllow = new string[]
        {
            "frs_alerts_by_site", "frs_active_alert_count", "get_frs_alerts", "get_alerts", "alert_smart",
            "predict_track_health"   // one site-scoped call for reference-flagged TCs only (leak that outlives its alert)
        };

        private static readonly Regex _maintAskRxSite = new Regex(
            @"\b(?:maint\w*|plan|roster|defects?|attend|rectify|what\s+to\s+do|action\s+needed|fault\s+list)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // v1.3.2.0: a single named asset (PT-32, 1T, 27AT, S-AM1 ...) or an explicit deep-check
        // means the user wants the deeper per-asset tools -- that is NOT the site plan fast path.
        private static readonly Regex _singleAssetRx = new Regex(
            @"\b(?:PT-?\d{1,3}|\d{1,3}[A-Z]{1,3}\d?|UMT\d|UL\dT\d|S-?AM-?\d+|SH-?\d{1,3}|SA-?\d{1,3}|S\d{1,3})\b|deep[\s-]?check",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // True when the latest user turn names a hardcoded site AND asks for a maintenance plan
        // for the SITE (no single asset named, no deep-check requested).
        private static bool IsSiteFastPathPlan(List<JObject> messages)
        {
            try
            {
                string u = LastUserTextForCause(messages) ?? "";
                if (u.Length == 0) { return false; }
                if (!_maintAskRxSite.IsMatch(u)) { return false; }
                if (_singleAssetRx.IsMatch(u)) { return false; }      // per-asset question -> full tools
                return BuildSiteWisdomRule(messages).Length > 0;   // a site matched
            }
            catch { return false; }
        }

        private static object[] PruneToolsForSitePlan(JArray tools)
        {
            var keep = new JArray();
            if (tools != null)
            {
                foreach (JToken t in tools)
                {
                    string n = t["name"] != null ? t["name"].ToString() : "";
                    foreach (string a in _siteFastToolAllow)
                    {
                        if (string.Equals(n, a, StringComparison.OrdinalIgnoreCase)) { keep.Add(t); break; }
                    }
                }
            }
            return keep.ToObject<object[]>();
        }

        // ── v1.3.3.0: CODE-BUILT PLAN BOARD ───────────────────────────────────────────
        // One card per asset with a state, grouped Urgent > Inspect Soon > Monitor, plus summary
        // tiles. Emitted as a [[PLANBOARD]] block the view renders as a responsive grid.
        // Sources, in priority order:
        //   1. live alert rows (IsActive / no ResetTimeStamp -> URGENT; reset -> MONITOR)
        //   2. live track-health for reference-flagged TCs (leakage Active -> URGENT)
        //   3. the site's KNOWN items (dated reference) for anything the live data did not cover
        private sealed class PlanItem
        {
            public string Asset; public string Cls; public int Sev;   // 3 urgent, 2 soon, 1 monitor
            public string Cause; public string When; public string Evidence; public string Action;
        }

        private static readonly string[] _alertToolNames = new string[]
        { "frs_alerts_by_site", "get_frs_alerts", "frs_last_alert_by_asset", "get_alerts", "alert_smart" };

        private static bool IsAlertToolName(string n)
        {
            foreach (string a in _alertToolNames) { if (string.Equals(n, a, StringComparison.OrdinalIgnoreCase)) { return true; } }
            return false;
        }

        private static string BuildSitePlanBoard(List<JObject> messages, string alertJson, string trackHealthJson)
        {
            try
            {
                SiteDef site = MatchSite(messages);
                if (site == null) { return ""; }
                var items = new Dictionary<string, PlanItem>(StringComparer.OrdinalIgnoreCase);

                // 1) live alert rows
                int liveRows = 0; bool liveOk = false;
                foreach (JObject r in EnumerateRows(alertJson))
                {
                    liveOk = true; liveRows++;
                    string asset = Str(r, "AssetName", "assetName", "Asset", "asset");
                    if (asset.Length == 0) { continue; }
                    string cause = Str(r, "CauseCode", "causeCode", "AlertInfoName", "AlertName", "Cause", "alertCode", "PossibleCause");
                    string setT = Str(r, "SetTimeStamp", "setTimeStamp", "IncidenceTime", "SetTime", "time");
                    string resetT = Str(r, "ResetTimeStamp", "resetTimeStamp", "ResetTime");
                    bool active = IsTrue(r, "IsActive", "isActive", "Active") || (setT.Length > 0 && resetT.Length == 0 && !Has(r, "ResetTimeStamp"));
                    if (Has(r, "ResetTimeStamp") && resetT.Length == 0 && !IsTrue(r, "IsActive", "isActive", "Active")) { active = true; }
                    int sev = active ? 3 : 1;
                    PlanItem p;
                    if (!items.TryGetValue(asset, out p))
                    {
                        p = new PlanItem { Asset = asset, Cls = ClassOf(asset), Sev = 0 };
                        items[asset] = p;
                    }
                    if (sev > p.Sev || p.Cause == null)
                    {
                        p.Sev = Math.Max(p.Sev, sev);
                        p.Cause = cause.Length > 0 ? cause : (p.Cause ?? "alert");
                        p.When = active ? ("active since " + ShortT(setT)) : (ShortT(setT) + (resetT.Length > 0 ? " → reset " + ShortT(resetT) : ""));
                        p.Evidence = active ? "Alert ACTIVE now" : "Alert reset";
                        p.Action = ActionFor(cause, asset);
                    }
                }

                // 2) live track-health (leak that outlives its alert)
                foreach (JObject th in EnumerateRows(trackHealthJson))
                {
                    string asset = Str(th, "AssetName", "assetName", "asset", "TrackName");
                    string status = Str(th, "leakage_status", "LeakageStatus");
                    JObject li = th["leakage_info"] as JObject;
                    if (li != null && status.Length == 0) { status = Str(li, "leakage_status", "status"); }
                    string ltype = li != null ? Str(li, "leakage_type", "type") : Str(th, "leakage_type");
                    string sevTxt = li != null ? Str(li, "severity") : Str(th, "severity");
                    if (asset.Length == 0 || status.Length == 0) { continue; }
                    if (status.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        PlanItem p;
                        if (!items.TryGetValue(asset, out p)) { p = new PlanItem { Asset = asset, Cls = "Track Circuit" }; items[asset] = p; }
                        p.Sev = 3;
                        p.Cause = "Leakage ACTIVE" + (ltype.Length > 0 ? " — " + ltype : "");
                        p.Evidence = "Track-health: leakage active" + (sevTxt.Length > 0 ? ", " + sevTxt : "") + " (alert may have reset — leak has not)";
                        p.Action = "Inspect glued joints and rail connections for external shorting; check feed-end bonding and joint insulation";
                        if (string.IsNullOrEmpty(p.When)) { p.When = "now"; }
                    }
                }

                // 3) known items from the reference, only where live data said nothing about the asset
                foreach (PlanItem k in KnownItems(site.SiteId))
                {
                    if (!items.ContainsKey(k.Asset)) { items[k.Asset] = k; }
                }

                if (items.Count == 0) { return ""; }

                // counts + ordering
                int u = 0, s = 0, m = 0;
                foreach (PlanItem p in items.Values) { if (p.Sev >= 3) u++; else if (p.Sev == 2) s++; else m++; }
                var ordered = new List<PlanItem>(items.Values);
                ordered.Sort((a, b) => { int c = b.Sev.CompareTo(a.Sev); return c != 0 ? c : string.Compare(a.Asset, b.Asset, StringComparison.OrdinalIgnoreCase); });

                var sb = new StringBuilder();
                sb.Append("[[PLANBOARD]]\n");
                sb.Append("site=").Append(site.Name).Append(" | code=").Append(site.Aliases.Length > 0 ? site.Aliases[site.Aliases.Length - 1].ToUpperInvariant() : "")
                  .Append(" | siteid=").Append(site.SiteId)
                  .Append(" | urgent=").Append(u).Append(" | soon=").Append(s).Append(" | monitor=").Append(m)
                  .Append(" | live=").Append(liveOk ? liveRows.ToString() : "unavailable").Append("\n");
                foreach (PlanItem p in ordered)
                {
                    sb.Append("[[CARD]] priority=").Append(p.Sev >= 3 ? "URGENT" : (p.Sev == 2 ? "INSPECT SOON" : "MONITOR"))
                      .Append(" | asset=").Append(p.Asset).Append(" | type=").Append(p.Cls ?? "").Append(" | site=").Append(site.Name).Append("\n");
                    if (!string.IsNullOrEmpty(p.Cause)) { sb.Append("CAUSE: ").Append(p.Cause).Append("\n"); }
                    if (!string.IsNullOrEmpty(p.When)) { sb.Append("WHEN: ").Append(p.When).Append("\n"); }
                    if (!string.IsNullOrEmpty(p.Evidence)) { sb.Append("EVIDENCE: ").Append(p.Evidence).Append("\n"); }
                    if (!string.IsNullOrEmpty(p.Action)) { sb.Append("ACTION: ").Append(p.Action).Append("\n"); }
                    sb.Append("[[/CARD]]\n");
                }
                sb.Append("[[/PLANBOARD]]");
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static SiteDef MatchSite(List<JObject> messages)
        {
            string blk = BuildSiteWisdomRule(messages);
            if (blk.Length == 0) { return null; }
            foreach (SiteDef s in _sites) { if (ReferenceEquals(s.Block, blk)) { return s; } }
            return _sites.Length > 0 ? _sites[0] : null;
        }

        // Dated known items per site -- the Monitor/Soon lane when live data says nothing newer.
        private static List<PlanItem> KnownItems(int siteId)
        {
            var l = new List<PlanItem>();
            if (siteId != 71) { return l; }
            l.Add(new PlanItem { Asset = "1T", Cls = "Track Circuit", Sev = 3, Cause = "Leakage ACTIVE — External Shorting (Glued Joint), Critical", When = "since 08 Oct 15:51", Evidence = "Track-health active leak; TFC-fuse alert reset but leak has not", Action = "Inspect 1T glued joints and rail connections for external shorting; check feed-end bonding and joint insulation; verify common TFC distribution fuse/feed wiring" });
            l.Add(new PlanItem { Asset = "PT-32", Cls = "Point Machine", Sev = 2, Cause = "PT VOLT FAIL AT NWKR", When = "08 Oct 11:25 → reset", Evidence = "0 active", Action = "Inspect NWKR relay and 24 V DC local feed at A end; check pick-up / detection contacts" });
            l.Add(new PlanItem { Asset = "PT-31", Cls = "Point Machine", Sev = 2, Cause = "PT VOLT FAIL AT RWKR OP", When = "08 Oct 11:55 → reset", Evidence = "0 active", Action = "Inspect RWKR relay and 24 V DC local feed at A end; check pick-up during operation" });
            l.Add(new PlanItem { Asset = "PT-43", Cls = "Point Machine", Sev = 2, Cause = "PT VOLT FAIL AT NWKR OP", When = "08 Oct 11:00 → reset", Evidence = "0 active", Action = "Inspect NWKR relay and 24 V DC local feed at A end" });
            l.Add(new PlanItem { Asset = "31T", Cls = "Track Circuit", Sev = 1, Cause = "TC TFC I/P VOLT LOW — shared TFC feed fuse (with 1T, 32T)", When = "08 Oct 15:50 → reset 18:16", Evidence = "Cleared; same fuse event", Action = "Check common 110 V DC TFC distribution fuse and shared feed wiring; escalate if it repeats" });
            l.Add(new PlanItem { Asset = "32T", Cls = "Track Circuit", Sev = 1, Cause = "TC TFC I/P VOLT LOW — shared TFC feed fuse (with 1T, 31T)", When = "08 Oct 15:36 → reset 17:43", Evidence = "Cleared; same fuse event", Action = "Check common 110 V DC TFC distribution fuse and shared feed wiring; escalate if it repeats" });
            l.Add(new PlanItem { Asset = "1AT", Cls = "Track Circuit", Sev = 1, Cause = "TC TR VOLT LOW — relay under-energization (Ir 134.8 mA)", When = "09 Oct 19:39 → reset 20:14", Evidence = "Cleared; track health Normal; charger healthy", Action = "Inspect feed adjustment / relay-end wiring; megger; watch Ir trend" });
            l.Add(new PlanItem { Asset = "27AT", Cls = "Track Circuit", Sev = 1, Cause = "TC SHORT", When = "10 Oct 10:35 → reset 10:38", Evidence = "Cleared; track health Normal", Action = "Monitor" });
            l.Add(new PlanItem { Asset = "5AT1", Cls = "Track Circuit", Sev = 1, Cause = "TC SHORT", When = "08 Oct 13:49 → reset", Evidence = "Cleared; relay-side leakage Resolved", Action = "Monitor" });
            return l;
        }

        private static string ClassOf(string asset)
        {
            string a = (asset ?? "").ToUpperInvariant();
            if (a.StartsWith("PT")) { return "Point Machine"; }
            if (a.StartsWith("SH") || a.StartsWith("S-AM") || a.StartsWith("SA-") || (a.StartsWith("S") && a.Length <= 4 && char.IsDigit(a[1]))) { return "Signal"; }
            if (a.IndexOf("BPAC", StringComparison.Ordinal) >= 0) { return "BPAC"; }
            return "Track Circuit";
        }

        private static string ActionFor(string cause, string asset)
        {
            string c = (cause ?? "").ToUpperInvariant();
            if (c.Contains("TFC I/P")) { return "Check TFC input / common 110 V DC distribution fuse and feed wiring"; }
            if (c.Contains("TC SHORT") || c.Contains("GJ SHORT")) { return "Inspect for external shorting: glued joints, bonds, ballast/leakage path"; }
            if (c.Contains("TR VOLT") || c.Contains("RELAY END")) { return "Inspect feed adjustment and relay-end wiring; megger section; verify charger regulation"; }
            if (c.Contains("TPR") && c.Contains("RELAY")) { return "Inspect TPR relay and relay-end circuit"; }
            if (c.Contains("NWKR") || c.Contains("RWKR")) { return "Inspect the named WKR relay and 24 V DC local feed at the A end; check pick-up / detection contacts"; }
            if (c.Contains("OBS")) { return "Check for obstruction / friction in the switch; inspect slide chairs and stroke-end lubrication"; }
            if (c.Contains("SIG") || c.Contains("ASPECT") || c.Contains("LAMP")) { return "Check lamp / aspect supply and proving relay; inspect for filament ageing or loose connection"; }
            if (c.Contains("IPS")) { return "Check IPS output, battery and charger"; }
            return "Inspect " + asset + " per cause " + (cause ?? "").Trim();
        }

        // ── tiny JSON helpers ──
        private static IEnumerable<JObject> EnumerateRows(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) { yield break; }
            JToken root = null;
            try { root = JToken.Parse(json); } catch { yield break; }
            JArray arr = root as JArray;
            if (arr == null)
            {
                JObject o = root as JObject;
                if (o == null) { yield break; }
                foreach (string k in new[] { "rows", "data", "Data", "alerts", "Alerts", "items", "Items", "result", "Result", "records", "Records", "tracks", "Tracks" })
                {
                    if (o[k] is JArray) { arr = (JArray)o[k]; break; }
                }
                if (arr == null) { yield return o; yield break; }
            }
            foreach (JToken t in arr) { JObject jo = t as JObject; if (jo != null) { yield return jo; } }
        }
        private static string Str(JObject o, params string[] keys)
        {
            foreach (string k in keys) { JToken v = o[k]; if (v != null && v.Type != JTokenType.Null) { string s = v.ToString().Trim(); if (s.Length > 0) { return s; } } }
            return "";
        }
        private static bool Has(JObject o, string key) { return o[key] != null; }
        private static bool IsTrue(JObject o, params string[] keys)
        {
            foreach (string k in keys)
            {
                JToken v = o[k]; if (v == null || v.Type == JTokenType.Null) { continue; }
                string s = v.ToString().Trim().ToLowerInvariant();
                if (s == "1" || s == "true" || s == "yes") { return true; }
            }
            return false;
        }
        private static string ShortT(string t)
        {
            if (string.IsNullOrEmpty(t)) { return ""; }
            DateTime d;
            if (DateTime.TryParse(t, out d)) { return d.ToString("dd MMM HH:mm"); }
            return t.Length > 16 ? t.Substring(0, 16) : t;
        }

        private static readonly SiteDef[] _sites = new SiteDef[]
        {
            new SiteDef{
                SiteId = 71,
                Name = "G Cabin",
                Aliases = new[]{ "g cabin","gcabin","g-cabin","gcbc" },
                Block =
"\n\n=== SITE REFERENCE: G CABIN (SiteId 71) ===\n" +
"Identity: G Cabin, station code GCBC, SiteId 71. Zone SECR, Division Raipur. Vendor Energy7.\n" +
"Inventory (49 assets): 24 Track Circuits, 16 Signals, 7 Point Machines, 2 BPAC. Probe budget 529 analog + 85 relay channels.\n" +
"  Track circuits: 1T, 1AT, 31T, 32T, 5AT, 27AT, 29AT, 41AT, 41BT, 42AT, 42BT, 43AT, 45AT, 45BT, 5AT1, UMT1, UMT2, UMT3, UL1T1, UL1T2, UL1T3, UL2T1, UL2T2, UL2T3.\n" +
"  Signals: S1, S3, S5, S7, S9, S27, S29, SH-3, SH-5, SH-7, SH-9, SH-24, SH-26, S-AM1, S-AM-27, SA-385.\n" +
"  Point machines: PT-31, PT-32, PT-41, PT-42, PT-43, PT-44, PT-45.  BPAC: UP BPAC-A, UP BPAC-B.\n" +
"\n" +
"MAINTENANCE ROSTER -- raise signals decide a state; the asset's state is the WORST signal (Urgent > Soon > Monitor > Healthy):\n" +
"  1 Alert (FRSAlerts): active now or rate rising = Urgent; steady/moderate = Soon; occasional or settling = Monitor; closed-then-alerted-again = Failed repair (Urgent).\n" +
"  2 Track shorting (ai-prediction/track, track circuits only): live/open shorting window = Urgent; shorting windows or leakage present = Soon; leakage Resolved/None and condition Normal = Healthy.\n" +
"  3 PM operations (pm-operation-v4, points only): >= 3 problem ops (grade not NORMAL/INVALID) = Urgent; >= 1 = Soon.\n" +
"  4 Avg drift (FRSAttributeRangeHistory, all): max |drift| across attributes >= 50% = Urgent; >= 25% = Soon  (drift = (late-qtr avg - early-qtr avg)/|early| x100).\n" +
"  5 PM-predict (points): INSPECT/URGENT = Urgent; MAINTAIN-SOON = Soon; WATCH = Monitor.\n" +
"\n" +
"DIAGNOSTIC PATTERNS (reusable, confirm against live data):\n" +
"  - TFC supply fuse / short: several track circuits raising 'TC TFC I/P VOLT LOW' with Charger-V (VTC TFC I/P) collapsing to ~0 V at nearly the same time and recovering together, TPR picked, is ONE shared 110 V DC TFC feed fault -- report it as a single supply item, not N track faults. Action: the common TFC distribution fuse and the feed wiring shared by those TCs.\n" +
"  - Occupancy vs fault: a train drops TPR and snaps relay current (Ir) to 0 then back. An alert with TPR PICKUP and Ir sagging on a clear track is a genuine under-energization, NOT a train movement. 'TC TR VOLT LOW' fires only with TPR PICKUP for exactly this reason.\n" +
"  - Alert reset is not leak resolved: an FRS alert resetting does NOT mean a track-health (glued-joint / leakage) condition cleared -- the two are independent sources. A TC can be 'all alerts reset' and still carry an ACTIVE leakage event. Report both; an active leak keeps the TC URGENT regardless of alert state.\n" +
"\n" +
"RECENT KNOWN ITEMS (as of 10 Oct 2026; ALWAYS verify current state live before reporting):\n" +
"  - 08 Oct, TFC supply fuse: 1T, 31T, 32T all 'TC TFC I/P VOLT LOW', Charger-V -> 0 V, 15:36-18:16, alerts cleared. One shared-feed fuse event -> 31T, 32T = MONITOR.\n" +
"  - 1T (AssetId 9700) -- NOT just the fuse: track-health model shows leakage ACTIVE, External Shorting (Glued Joint), severity Critical, running since 08 Oct 15:51 (same instant as the fuse trip), feed-side dominant (IF ~822 mA vs IR ~21 mA), leak load ~843 mA-days as of 10 Oct. Its FRS alerts have reset, but the leak has not -> 1T = URGENT until the leak reads Resolved. Action: inspect 1T glued joints and rail connections for external shorting; check feed-end bonding and joint insulation; and verify the common TFC distribution fuse/feed wiring. Also 1T raised 'TC TR RELAY DEFECT' on 08 Oct (reset).\n" +
"  - Point machines, 08 Oct (all reset, 0 active -> INSPECT SOON): PT-32 'PT VOLT FAIL AT NWKR' 11:25; PT-31 'PT VOLT FAIL AT RWKR OP' 11:55; PT-43 'PT VOLT FAIL AT NWKR OP' 11:00. Action: inspect the named NWKR/RWKR relay and the 24 V DC local feed at the A end; check relay pick-up / detection contacts.\n" +
"  - Also reset, MONITOR: 27AT 'TC SHORT' 10 Oct 10:35 (reset 10:38, track health Normal); 5AT1 'TC SHORT' 08 Oct 13:49 (reset; relay-side leakage Resolved).\n" +
"  - 08-09 Oct, 1AT 'TC TR VOLT LOW': relay current to ~134.8 mA (below MinSafe 210) on a clear track, charger input healthy ~125 V; feed-side under-energization, cleared.\n" +
"\n" +
"LIVE DATA: for any current alert, state, count or reading at G Cabin, call the data tools (frs_alerts_by_site with SiteId 71, frs_attribute_range, live/history) -- the asset map, roster rules and patterns above are the fixed reference; the current situation is always live. When several alerts share the fuse signature above, group them as one supply item.\n" +
"\n" +
"FAST PATH FOR A MAINTENANCE PLAN AT THIS SITE (overrides the general 15-day per-asset evidence procedure):\n" +
"  Do NOT sweep every asset. Make AT MOST THREE tool calls, in this order: (1) frs_alerts_by_site SiteId 71 (fall back to the MCP alert tool for site 71 if it fails); (2) ONE predict_track_health call for SiteId 71 covering ONLY the TCs the reference flags with an open or recent leak (currently TrackIds [9700] = 1T) -- this is how a leak that outlives its alert is caught; (3) optionally frs_active_alert_count. Do NOT call predict_pm_operation, predict_pm_cluster, history, attribute ranges, or per-asset live tools for the plan -- answer from the site reference plus those results.\n" +
"  Then build the plan: assets with an ACTIVE alert now = URGENT (group fuse-signature alerts as one supply item); recently cleared alerts = MONITOR with the matching action from the patterns above; everything else = HEALTHY, listed by class count only. Point-machine ML / signature checks are offered as a follow-up ('ask me to deep-check PT-xx'), not run now.\n" +
"  If the alert call fails on both sources, still return the plan from the reference with the known Monitor items, and say current alert status could not be fetched.\n" +
"\n" +
"DEEPER PER-ASSET DETAIL -- ON DEMAND ONLY. Use these when the user asks about ONE named asset (\"deep-check PT-32\", \"is 1T shorting\", \"drift on 27AT\") or explicitly asks to deep-check the site. NEVER run them across the site for a maintenance plan (the plan is the fast path above).\n" +
"  - Track shorting -> track-health predictor (ai-prediction/track/71/{assetId}): overall_condition, leakage_info (detected / status / type / severity), major_events (shorting windows Start/End/Duration/Dominant_Cause). Tells whether a TC is shorting NOW, had past windows, or is Resolved.\n" +
"  - Point-machine operations -> pm-operation predictor (ai-prediction/pm-operation-v4/71/{assetId}?details=true&start&end): per-operation grade (NORMAL/INVALID/OBSTRUCTION/...); problem count = operations whose grade is NOT NORMAL and NOT INVALID. Note these tools DISCARD StartDate/EndDate -- never state a window for their results.\n" +
"  - Value trend / drift -> attribute range history (FRSAttributeRangeHistory/AssetId/{assetId}/{start}/{end}, ddMMyyyy_HHmmss): per-attribute AvgValues series + MinSafe/MaxSafe/Average; drift = (late-quarter avg - early-quarter avg)/|early| x100.\n" +
"  Report each as a model classification to confirm on site, never as established fact.\n" +
"=== END SITE REFERENCE: G CABIN ==="
            },
        };
    }
}
