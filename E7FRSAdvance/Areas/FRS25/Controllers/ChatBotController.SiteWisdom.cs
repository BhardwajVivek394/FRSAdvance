using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace E7FRSAdvance.Areas.FRS25.Controllers
{
    // ============================================================================
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
"\n" +
"RECENT KNOWN ITEMS (as of 08-09 Oct 2026, all CLEARED -> Monitor; ALWAYS verify current state live before reporting):\n" +
"  - 08 Oct, TFC supply fuse: 1T, 31T, 32T all 'TC TFC I/P VOLT LOW', Charger-V -> 0 V, 15:36-18:16, cleared. One shared-feed fuse event.\n" +
"  - 08-09 Oct, 1AT 'TC TR VOLT LOW': relay current to ~134.8 mA (below MinSafe 210) on a clear track, charger input healthy ~125 V; feed-side under-energization, cleared.\n" +
"\n" +
"LIVE DATA: for any current alert, state, count or reading at G Cabin, call the data tools (frs_alerts_by_site with SiteId 71, frs_attribute_range, live/history) -- the asset map, roster rules and patterns above are the fixed reference; the current situation is always live. When several alerts share the fuse signature above, group them as one supply item.\n" +
"=== END SITE REFERENCE: G CABIN ==="
            },
        };
    }
}
