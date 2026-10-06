// ============================================================================
// ENERGY7 RDPMS -- Consolidated CauseDef Catalog  (BuildCauseCatalog, item #5)
// ============================================================================
// ONE file, one CauseDef per cause, built from the authoritative FRS catalog PDF
// (ENERGY7_RDPMS_CauseDef_Catalog_EFFECTIVE_THRESHOLD_FINAL, 147 causes) and
// RECONCILED against the current controller maps (CauseLogicMaps.CauseToLogic /
// CauseToDerived, RdpmsMaps.CauseToAttributes / RdpmsAttrMap).
//
// WHY: the per-cause knowledge is today scattered across CauseToLogic (a loose
// string), CauseToAttributes (names only, no attid, relay gate often omitted),
// CauseToDerived (attid only), and the attid resolution done live at runtime.
// This catalog is the single typed source of truth that the verifier, prefetch,
// and viz-anchor can all read from.
//
// -------------------------- LOAD-BEARING PRINCIPLES --------------------------
// P0. VERDICT DOCTRINE -- the whole purpose of the AI. The verdict is BINARY:
//     CONFIRMED or NOT_CONFIRMED (never INCONCLUSIVE). The alert CARD is the
//     CLAIM, not the authority. Each card carries its cause logic (thresholds,
//     relay states, sustain window); the AI RE-VERIFIES that logic against
//     HISTORY telemetry and decides on the HISTORY, never on the card alone:
//       - history shows the card's logic HELD at/through the incidence   -> CONFIRMED
//       - history shows it did NOT hold (transient, not sustained for the
//         SustainSec window, relay/aspect not in the required state, the
//         adjacent TPR did not drop, ...)                                -> NOT_CONFIRMED
//       - history ABSENT / cannot verify -> NOT_CONFIRMED, tagged
//         "coverage: re-check" for the maintainer. Missing data never CONFIRMS.
//     >>> This REVERSES the controller's current W7 ("card is authoritative; a
//     card FAIL is CONFIRMED even with no history"). Under P0 the card FAIL only
//     SETS THE CLAIM to verify; history decides. W7 is RETIRED. Consequence:
//     the ROSIG/SHSIG "sparse history" cases resolve NOT_CONFIRMED (history could
//     not verify the sustained active-aspect breach), not CONFIRMED. <<<
//
//     EVALUATION WINDOW: each cause has its own SustainSec (from the FRS). The
//     cause logic must be CONTINUOUSLY satisfied in history across the window
//     [incidence, incidence + SustainSec] -- every sample in that span must pass:
//       - held for the whole window                 -> CONFIRMED
//       - satisfied at incidence but LAPSES before the window ends (transient) -> NOT_CONFIRMED
//       - SustainSec == 0 (GJ SHORT, point op-time, UNKNOWN) -> single INSTANT
//         check at the incidence, no window
//       - history does not cover the window (can't prove persistence) ->
//         NOT_CONFIRMED + "coverage: re-check"
//     So it is neither "ever breached" nor "one instant" -- it is "true, without
//     interruption, from the incident through incident + SustainSec".
//
// P1. FAILURE = TWO STAGES. Failure is decided on the DataLogger; WHICH cause it
//     is, is decided by RDPMS. A track-failure verdict runs two stages, in order,
//     both over the cause's SustainSec window (P0):
//
//     STAGE 1 -- IS IT A REAL FAILURE?  (DataLogger relays + train/shunt exclusion)
//       (1) MAIN track TPR = DROP (DN), AND
//       (2) EVERY adjacent/family track TPR = UP  -- a dropped neighbour = a train
//           rolling through (sequential DROP->PICK) -> NOT a failure, and
//       (3) the failing track's MAPPED signal has NO OFFECR UP  -- an OFFECR up =
//           a shunt route was set -> NOT a failure.
//       Adjacent tracks come from siteContext.familyTracks; the mapped signal from
//       siteContext.signalTrack (both cross-asset, fetched from HISTORY at the
//       incidence +/- window). If Stage 1 fails -> NOT_CONFIRMED (train/shunt, not
//       a failure). This stage answers ONLY "failure: true/false".
//
//     STAGE 2 -- WHICH CAUSE?  (RDPMS analog logic)
//       Only if Stage 1 passes. Evaluate the TRIGGERED cause's RDPMS logic:
//         - satisfied            -> CONFIRMED as the triggered cause
//         - NOT satisfied        -> search the track-failure family (#17-26) for the
//                                   cause whose RDPMS logic IS satisfied -> CONFIRMED,
//                                   RE-CLASSIFIED to that cause (e.g. card said TC SHORT
//                                   but TPR RELAY DEFECT logic matches -> verdict is
//                                   TPR RELAY DEFECT, and vice-versa)
//         - none match           -> REASON UNKNOWN (#26)
//
//     Two return values: (a) Stage 1 = failure true/false; (b) Stage 2 = cause
//     correct, or the corrected cause. The shared Stage-1 gate is TrackFailureGate
//     below; each #17-26 entry's Attrs are its Stage-2 selector (its own TPR DN +
//     the RDPMS conditions). PREDICTIVE causes use only the single-track logic in
//     their entry (no Stage-1 adjacent/OFFECR exclusion).
//
// P2. EffectiveThreshold is a RUNTIME formula, not a fixed number. Resolve per
//     asset at analysis time:
//       lower predictive  '< (LD%avg OR MinSafe)'  -> MAX(avg*LD, MinSafe)
//       upper predictive  '> (HD%avg OR MaxSafe)'  -> MIN(avg*HD, MaxSafe)
//       percentage-only                            -> avg*pct
//       MinSafe / MaxSafe / MinFail                -> direct threshold
//       '2xavg' / '0.10*T2 original' etc.          -> as written in EffectiveThreshold
//     Track predictive LD1=80 LD2=50 LD3=90 HD1=120 HD2=150; Track failure LD1=70
//     LD2=90 HD1=120; Signal/CO/RO LD=80 HD=120; IPS LD=90. (FRS 2.1-2.7.)
//
// P3. NULL-ATTRIBUTE RULE. A CauseDef is the FULL template. At runtime, for THIS
//     asset, resolve each attr against the asset's TAG TABLE:
//       - attr NOT in the tag table (asset genuinely lacks it -- e.g. a 2-aspect
//         RG+DG signal has no HR/HG/HHG) -> NULL -> SKIP that condition. Evaluate
//         only the conditions whose attrs EXIST in the tag table. Applies to any
//         attr in the logic -- relay, analog, or IPS -- including a decisive relay.
//       - attr EXISTS in the tag table but has NO DATA at the incidence -> this is
//         NOT a skip; it is the P0 coverage case (predictive -> NOT_CONFIRMED +
//         coverage; failure -> relays still decide).
//     i.e. "not configured" (skip) is different from "configured but no history"
//     (coverage). Depends on tag-table accuracy: a tag mis-marked absent wrongly
//     relaxes the check -- a data-integrity dependency, not a logic choice.
//
// --------------------------- VERIFIER CONTRACT ------------------------------
//   1. card JSON = the CLAIM (cause, asset, incidence). Not the authority.
//   2. look up CauseCatalog.ByCause[cause] -> typed attrs.
//   3. resolve each attr in the ASSET's tag table: present -> evaluate;
//      not present -> NULL/skip (P3).
//   4. FETCH data at the incidence over [incidence, incidence + SustainSec] using
//      the EXISTING fetch path (same source the current verdict flow uses).
//   5. evaluate the cause logic over that data:
//        predictive -> logic holds continuously across the window -> CONFIRMED;
//                      lapses -> NOT_CONFIRMED; window uncoverable -> NOT_CONFIRMED+coverage.
//        failure    -> relays decide CONFIRMED/NOT_CONFIRMED (Stage 1); RDPMS only
//                      selects WHICH cause (Stage 2).
//   6. history / pattern analysis as the existing flow -- for narrative + confidence,
//      AFTER the verdict is set.
//
// ---------------------------- REVIEW METHODOLOGY -----------------------------
// PDF = spec (authoritative). Controller = current state. Every cause carries a
// [REVIEW] note recording: does the controller match? what does the catalog add
// or correct? Known controller<->PDF DIVERGENCES to resolve as we go:
//   - #16 TC GJ SHORT: this is a CROSS-ASSET cause -- T1 (the shorted track, TPR DN)
//     and T2 (the adjacent/alert track, TPR UP) are DIFFERENT (adjacent) track assets.
//     The controller's CauseToLogic DOES carry both scopes ("T1[...] && T2[...]"), so my
//     earlier "missing T2" note was wrong. The real gaps are: (a) the single-asset AI
//     analysis cannot fetch the adjacent track T2, so the T2 condition can't actually be
//     verified at runtime; (b) the IBalst threshold is left blank in CauseToLogic (should be
//     HD1=120%avg); (c) SustainSec should be 0 but the string parser defaults it to 15.
//     RESOLVED in the #16 entry below; the cross-asset fetch is a verifier TODO.
//   (more will be added as each cause is reviewed)
//
// STATUS: building from cause #1. Sections follow the PDF order (Track Predictive,
// Track Failure, Signal, CO, RO, Point, IPS, Shunt, Legacy).
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace E7MRIWeb.Areas.FRS25.Controllers
{
    // One attribute row of a cause, mirroring the PDF per-attribute table exactly.
    public sealed class CauseAttr
    {
        public string Name;                 // FRS attribute name, e.g. "VTC TFC I/P"
        public int    Attid;                // AssetAttributeId; 0 = runtime/per-asset (PDF shows "-")
        public string Datatype;             // "DataLogger" | "RDPMS" | "IPS"
        public string Class;                // "relay" | "analog" | "temporal" | "ips" | "derived"
        public string State;                // "UP" | "DN" | "UP->DN" | "" (analog)
        public string Cmp;                  // "<" | ">" | ">=" | "<=" | "==" | "" (relay)
        public string ThresholdRef;         // FRS ref, e.g. "LD1=80%avg OR MinSafe", "MinSafe", "0+"
        public string EffectiveThreshold;   // runtime formula, e.g. "MAX(avg*0.80, MinSafe)"
        public string Gate;                 // scope/gate tag, e.g. "TPR", "T1", "OR-G1", ""
    }

    public sealed class CauseDef
    {
        public int      Seq;                // PDF number (#1..#147)
        public string   Cause;              // cause code (matches alert causeCode)
        public string   AlertType;          // "PREDICTIVE" | "FAILURE"
        public double?  LdPercent;          // null when PDF shows "-"
        public bool     LowerRule;          // LowerRule=True/False
        public int      SustainSec;         // dwell window in seconds (900 = 15min, etc.)
        public int      DerivedFormula;     // derived attid; 0 when PDF shows "-"
        public string   Source;             // "FRS" | "FRS-MISSING" | "CODE-ONLY"
        public string   RuleRaw;            // full FRS rule expression
        public CauseAttr[] Attrs;           // ordered as the PDF lists them (alerting/gate first)
        public string   VerdictLogic;       // the "=> CONFIRMED" line

        public bool IsFailure { get { return AlertType == "FAILURE"; } }

        // P1: the DataLogger relay gate -- "is this a failure?" (also the enabling
        // relays for predictive causes).
        public IEnumerable<CauseAttr> RelayGate()
        {
            return Attrs.Where(a => a.Class == "relay" && a.Datatype == "DataLogger");
        }

        // P1: the RDPMS analog/derived attributes that SELECT which cause / carry
        // the breach value (the anchor set for the verdict + viz).
        public IEnumerable<CauseAttr> RdpmsSelectors()
        {
            return Attrs.Where(a => a.Datatype == "RDPMS");
        }
    }

    public static class CauseCatalog
    {
        // P1 STAGE-1 gate, shared by every track-FAILURE cause (#17-26). Applied
        // BEFORE the per-cause Stage-2 logic, over the cause's SustainSec window.
        // All three are relay/DataLogger checks fetched from HISTORY:
        //   - main track TPR = DN (each entry also carries this as its first attr)
        //   - EVERY family track (siteContext.familyTracks) TPR = UP  (train exclusion)
        //   - mapped signal (siteContext.signalTrack) OFFECR != UP     (shunt exclusion)
        // If any fails -> NOT_CONFIRMED (train/shunt movement, not a failure).
        public static readonly CauseAttr[] TrackFailureGate = new []
        {
            new CauseAttr{ Name="TPR (main)",            Attid=6, Datatype="DataLogger", Class="relay", State="DN", Gate="STAGE1" },
            new CauseAttr{ Name="TPR (every family track)", Attid=0, Datatype="DataLogger", Class="relay", State="UP", ThresholdRef="siteContext.familyTracks", EffectiveThreshold="ALL adjacent tracks must be UP (any DN = train)", Gate="STAGE1/cross-asset" },
            new CauseAttr{ Name="OFFECR (mapped signal)", Attid=0, Datatype="DataLogger", Class="relay", State="not UP", ThresholdRef="siteContext.signalTrack", EffectiveThreshold="mapped-signal OFFECR must NOT be UP within +/- window (UP = shunt route)", Gate="STAGE1/cross-asset" },
        };

        // v...: ROSIG route-aspect attids. The route that fired (AUG/BUG/CUG/DUG/EUG) is read from the
        // card's triggered param; resolve the slot to the attid here. CURRENT comparisons SUM
        // PilotRootMa (631, ~20 mA, distinct from shunt PILOT); VOLTAGE comparisons never sum.
        public static readonly Dictionary<string, int> RosigRouteMa = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        { { "AUG", 356 }, { "BUG", 326 }, { "CUG", 332 }, { "DUG", 357 }, { "EUG", 358 } };
        public static readonly Dictionary<string, int> RosigRouteV = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
        { { "AUG", 497 }, { "BUG", 573 }, { "CUG", 574 }, { "DUG", 602 }, { "EUG", 613 } };
        public const int PilotRootMa = 631;   // add to the triggered route mA (if present) before compare
        public const int PilotRootV  = 632;   // NOT summed (voltage has no addition)

        // Point A/B-end attid pairs (card gives the end). N/R in the name = Normal/Reverse
        // OPERATION, not the A/B end. Operation V/I/Time are the EdgeX PM channels (the "-Avg"
        // for that throw); indication + relays are RDPMS/DataLogger. Value tuple = (A-end, B-end).
        // DB cause-code id -> cause name (assetTypeId/id/name/type table, 151 rows). The alert card's
        // numeric cause id resolves here, then ByCause[name] -> CauseDef -- id-based lookup is immune to
        // the UNKOWN/spacing spelling drift between DB and FRS. Source of truth = the RDPMS DB.
        public static readonly System.Collections.Generic.Dictionary<int, string> DbIdToCause =
            new System.Collections.Generic.Dictionary<int, string>()
        {
            { 7, "PT N TIME HIGH" }, { 8, "PT R TIME HIGH" }, { 12, "PT NWKR RELAY DEFECT" }, { 14, "PT N VOLT/CURR FAIL" },
            { 15, "PT N OBS" }, { 21, "PT RWKR RELAY DEFECT" }, { 23, "PT R VOLT/CURR FAIL" }, { 24, "PT R OBS" },
            { 27, "TC TFC I/P VOLT LOW" }, { 30, "TC BT CHG CURR HIGH" }, { 38, "TC RAIL RES HIGH" }, { 39, "TC TR OVER ENERIZATION" },
            { 43, "TC TR CONTACT RES HIGH" }, { 44, "TC TPR I/P VOLT LOW" }, { 45, "TC SHORT" }, { 47, "TC VAR RES OPEN" },
            { 49, "TC RAIL RES OPEN" }, { 51, "TC TR RELAY DEFECT" }, { 52, "TC TR UP TPR DN" }, { 53, "TC TPR RELAY DEFECT" },
            { 54, "REASON UNKNOWN" }, { 55, "SIG RG VOLT/CURR LOW" }, { 56, "SIG HG VOLT/CURR LOW" }, { 57, "SIG HHG VOLT/CURR LOW" },
            { 58, "SIG DG VOLT/CURR LOW" }, { 59, "SIG RG CURR HIGH" }, { 60, "SIG HG CURR HIGH" }, { 61, "SIG HHG CURR HIGH" },
            { 62, "SIG DG CURR HIGH" }, { 63, "SIG HPR VOLT LOW" }, { 64, "SIG HHPR VOLT LOW" }, { 65, "SIG DPR VOLT LOW" },
            { 66, "SIG RG VOLT/CURR FAIL" }, { 67, "SIG RG RECR RELAY DEFECT" }, { 68, "SIG RG UNKOWN" }, { 69, "SIG HG VOLT/CURR FAIL" },
            { 70, "SIG HG HPR VOLT FAIL" }, { 71, "SIG HG HECR RELAY DEFECT" }, { 72, "SIG HG UNKOWN" }, { 73, "SIG HHG VOLT/CURR FAIL" },
            { 74, "SIG HHG HHPR VOLT FAIL" }, { 75, "SIG HHG HHECR RELAY DEFECT" }, { 76, "SIG HHG UNKOWN" }, { 77, "SIG DG VOLT/CURR FAIL" },
            { 78, "SIG DG DPR VOLT FAIL" }, { 79, "SIG DG DECR RELAY DEFECT" }, { 80, "SIG DG UNKOWN" }, { 82, "SIG BLANK" },
            { 95, "SHSIG ON ASPECT VOLT/CURR LOW" }, { 96, "SHSIG ON ASPECT CURR HIGH" }, { 97, "SHSIG OFF ASPECT VOLT/CURR LOW" }, { 98, "SHSIG OFF ASPECT CURR HIGH" },
            { 99, "SHSIG OFF ASPECT HPR VOLT LOW" }, { 100, "SHSIG ON ASPECT VOLT/CURR FAIL" }, { 101, "SHSIG ON ASPECT ECR RELAY DEFECT" }, { 102, "SHSIG ON ASPECT REASON UNKNOWN" },
            { 103, "SHSIG OFF ASPECT VOLT/CURR FAIL" }, { 104, "SHSIG OFF ASPECT HPR VOLT FAIL" }, { 105, "SHSIG OFF ASPECT ECR RELAY DEFECT" }, { 106, "SHSIG OFF ASPECT REASON UNKOWN" },
            { 114, "IPS 24V DC LOW" }, { 115, "IPS 60V DC LOW" }, { 116, "IPS 110V DC LOW" }, { 117, "IPS 110V AC LOW" },
            { 134, "IPS 24V DC FAIL" }, { 135, "IPS 60V DC FAIL" }, { 136, "IPS 110V DC FAIL" }, { 151, "IPS 110V AC FAIL" },
            { 198, "PT N VOLT/CURR LOW" }, { 199, "PT R VOLT/CURR LOW" }, { 200, "PT N IND VOLT LOW AT LOC" }, { 201, "PT R IND VOLT LOW AT LOC" },
            { 202, "PT VOLT LOW AT NWKR" }, { 203, "PT VOLT LOW AT RWKR" }, { 204, "PT N IND VOLT FAIL AT LOC" }, { 205, "PT R IND VOLT FAIL AT LOC" },
            { 206, "PT VOLT FAIL AT NWKR" }, { 207, "PT VOLT FAIL AT RWKR" }, { 208, "PT N FAIL UNKNOWN" }, { 209, "PT R FAIL UNKNOWN" },
            { 210, "TC TFC O/P VOLT LOW (Charging)" }, { 211, "TC TFC O/P VOLT LOW (Discharging)" }, { 212, "TC BT CHG CURR LOW" }, { 213, "TC CH RES LOW" },
            { 214, "TC CH RES HIGH" }, { 215, "TC VAR RES LOW" }, { 216, "TC VAR RES HIGH" }, { 217, "TC BALST/SLPR RES LOW" },
            { 218, "TC TR VOLT LOW" }, { 219, "TC GJ SHORT" }, { 220, "TC CH RES OPEN" }, { 221, "TC TFC O/P VOLT FAIL" },
            { 222, "TC CKT OPEN" }, { 223, "COSIG ASPECT VOLT/CURR LOW" }, { 224, "COSIG ASPECT CURR HIGH" }, { 225, "COSIG HPR VOLT LOW" },
            { 226, "COSIG ASPECT VOLT/CURR FAIL" }, { 227, "COSIG HPR VOLT FAIL" }, { 228, "COSIG HECR RELAY DEFECT" }, { 229, "COSIG REASON UNKOWN" },
            { 230, "ROSIG ASPECT VOLT/CURR LOW" }, { 231, "ROSIG ASPECT CURR HIGH" }, { 232, "ROSIG HPR VOLT LOW" }, { 233, "ROSIG ASPECT VOLT/CURR FAIL" },
            { 234, "ROSIG HPR VOLT FAIL" }, { 235, "ROSIG UECR RELAY DEFECT" }, { 236, "ROSIG REASON UNKOWN" }, { 249, "PT VOLT FAIL AT NWKR OP" },
            { 250, "PT VOLT FAIL AT RWKR OP" }, { 251, "PT NWKR RELAY DEFECT OP" }, { 252, "PT RWKR RELAY DEFECT OP" }, { 253, "PT N FAIL UNKNOWN OP" },
            { 254, "PT R FAIL UNKNOWN OP" }, { 255, "IPS I/P VOLT LOW" }, { 256, "IPS 110 DC BATT VOLT LOW (Charging)" }, { 257, "IPS 110 DC BATT VOLT LOW (Discharging)" },
            { 258, "IPS 110 AC Sig-1 VOLT LOW" }, { 259, "IPS 110 AC TR-1 VOLT LOW" }, { 260, "IPS SMR-1 VOLT LOW" }, { 261, "IPS DC-DC R INT VOLT LOW" },
            { 262, "IPS DC-DC R EXT VOLT LOW" }, { 263, "IPS DC-DC AXLE C VOLT LOW" }, { 264, "IPS DC-DC PAN IND VOLT LOW" }, { 265, "IPS DC-DC BLOCK LOCAL VOLT LOW" },
            { 266, "IPS DC-DC HKT MAG VOLT LOW" }, { 267, "IPS DC-DC BLOCK LINE UP VOLT LOW" }, { 268, "IPS DC-DC BLOCK LINE DN VOLT LOW" }, { 269, "IPS DC-DC BLOCK TEL UP VOLT LOW" },
            { 270, "IPS DC-DC BLOCK TEL DN VOLT LOW" }, { 271, "IPS DC-DC DATALOG VOLT LOW" }, { 272, "IPS DC-DC EI VOLT LOW" }, { 273, "IPS BATT CHAR CURR LOW" },
            { 274, "IPS I/P VOLT FAIL" }, { 275, "IPS 110 DC VOLT FAIL" }, { 276, "IPS 110 AC Sig-1 VOLT FAIL" }, { 277, "IPS 110 AC TR-1 VOLT FAIL" },
            { 278, "IPS SMR-1 VOLT FAIL" }, { 279, "IPS DC-DC R INT VOLT FAIL" }, { 280, "IPS DC-DC R EXT VOLT FAIL" }, { 281, "IPS DC-DC AXLE C VOLT FAIL" },
            { 282, "IPS DC-DC PAN IND VOLT FAIL" }, { 283, "IPS DC-DC BLOCK LOCAL VOLT FAIL" }, { 284, "IPS DC-DC HKT MAG VOLT FAIL" }, { 285, "IPS DC-DC BLOCK LINE UP VOLT FAIL" },
            { 286, "IPS DC-DC BLOCK LINE DN VOLT FAIL" }, { 287, "IPS DC-DC BLOCK TEL UP VOLT FAIL" }, { 288, "IPS DC-DC BLOCK TEL DN VOLT FAIL" }, { 289, "IPS DC-DC DATALOG VOLT FAIL" },
            { 290, "IPS DC-DC EI VOLT FAIL" }, { 291, "IPS BATT CHAR CURR FAIL" }, { 292, "IPS DC-DC BATT CHAR CURR FAIL" },
        };

        public static readonly System.Collections.Generic.Dictionary<string, int[]> PointAttid =
            new System.Collections.Generic.Dictionary<string, int[]>(System.StringComparer.OrdinalIgnoreCase)
        {
            // operation VALUES (EdgeX PM -Avg for the throw)
            { "VPT 110DC Loc N", new[]{2002,4002} }, { "IPT N", new[]{1002,3002} },
            { "VPT 110DC Loc R", new[]{7002,9002} }, { "IPT R", new[]{6002,8002} },
            { "TPT N", new[]{1005,3005} },           { "TPT R", new[]{6005,8005} },
            // indication (steady) VALUES
            { "VPT 24DC Loc N", new[]{576,578} },    { "VPT 24DC Loc R", new[]{577,579} },
            { "VPT NWKR", new[]{25,27} },            { "VPT RWKR", new[]{26,28} },
            // common relays (not A/B paired)
            { "NWCR", new[]{282,282} }, { "RWCR", new[]{283,283} },
            { "NWKR", new[]{267,267} }, { "RWKR", new[]{268,268} },
        };

        public static readonly CauseDef[] All = new CauseDef[]
        {
            // ================================================================
            // Track Predictive (FRS 2.3(a))
            // ================================================================

            // #1  TC TFC I/P VOLT LOW
            // [REVIEW] Controller CauseToLogic MATCHES the PDF (TPR UP & VIPS TR-1
            //   110AC>=MinSafe & VTC TFC I/P < 80%avg OR MinSafe for 15min); not in
            //   CauseToDerived (correct, PDF DerivedFormula=-). Catalog ADDS: the
            //   attids (TPR=6, VIPS TR-1=621, VTC TFC I/P=344) and the TPR relay
            //   gate row, which CauseToAttributes omits (it lists only the 2 analog/
            //   IPS attrs). Minor: controller spells it "VIPS TR-1 110 AC" (space),
            //   PDF "VIPS TR-1 110AC" -- normalise on the PDF spelling.
            new CauseDef {
                Seq = 1, Cause = "TC TFC I/P VOLT LOW", AlertType = "PREDICTIVE",
                LdPercent = 0.80, LowerRule = true, SustainSec = 900, DerivedFormula = 0, Source = "FRS",
                RuleRaw = "TPR UP && VIPS TR-1 110AC>=MinSafe && VTC TFC I/P < (LD1=80%avg OR MinSafe) for 15min",
                Attrs = new []
                {
                    new CauseAttr { Name="TPR",             Attid=6,   Datatype="DataLogger", Class="relay",  State="UP" },
                    new CauseAttr { Name="VIPS TR-1 110AC", Attid=621, Datatype="IPS",        Class="ips",   Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr { Name="VTC TFC I/P",     Attid=344, Datatype="RDPMS",      Class="analog", Cmp="<",  ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)" },
                },
                VerdictLogic = "TPR UP & VIPS TR-1 110AC>=MinSafe & VTC TFC I/P < (LD1=80%avg OR MinSafe) for 15min => CONFIRMED"
            },

            // #2  TC TFC O/P VOLT LOW (Charging)   [REVIEW] matches controller; deriv 585 ok.
            new CauseDef {
                Seq=2, Cause="TC TFC O/P VOLT LOW (Charging)", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=585, Source="FRS",
                RuleRaw="TPR UP && ITC Batt Charg>0+ && VIPS TR-1 110AC>=MinSafe && VTC TFC O/P < (LD1=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Batt Charg", Attid=585, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+" },
                    new CauseAttr{ Name="VIPS TR-1 110AC", Attid=621, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VTC TFC O/P", Attid=569, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)" },
                },
                VerdictLogic="TPR UP & ITC Batt Charg>0+ & VIPS TR-1 110AC>=MinSafe & VTC TFC O/P < (LD1=80%avg OR MinSafe) for 15s => CONFIRMED"
            },

            // #3  TC TFC O/P VOLT LOW (Discharging)   [REVIEW] matches controller; deriv 585 ok.
            new CauseDef {
                Seq=3, Cause="TC TFC O/P VOLT LOW (Discharging)", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=585, Source="FRS",
                RuleRaw="TPR UP && ITC Batt Charg<0 && VIPS TR-1 110AC>=MinSafe && VTC TFC O/P < (LD1=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Batt Charg", Attid=585, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="0", EffectiveThreshold="0" },
                    new CauseAttr{ Name="VIPS TR-1 110AC", Attid=621, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VTC TFC O/P", Attid=569, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)" },
                },
                VerdictLogic="TPR UP & ITC Batt Charg<0 & VIPS TR-1 110AC>=MinSafe & VTC TFC O/P < (LD1=80%avg OR MinSafe) for 15s => CONFIRMED"
            },

            // #4  TC BT CHG CURR HIGH   [REVIEW] matches controller (> MaxSafe); deriv 585 ok.
            new CauseDef {
                Seq=4, Cause="TC BT CHG CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=false, SustainSec=15, DerivedFormula=585, Source="FRS",
                RuleRaw="TPR UP && ITC Batt Charg > MaxSafe for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Batt Charg", Attid=585, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="MaxSafe", EffectiveThreshold="MaxSafe" },
                },
                VerdictLogic="TPR UP & ITC Batt Charg > MaxSafe for 15s => CONFIRMED"
            },

            // #5  TC BT CHG CURR LOW   [REVIEW] matches controller (< MinSafe); deriv 585 ok.
            new CauseDef {
                Seq=5, Cause="TC BT CHG CURR LOW", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=true, SustainSec=15, DerivedFormula=585, Source="FRS",
                RuleRaw="TPR UP && ITC Batt Charg < MinSafe for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Batt Charg", Attid=585, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                },
                VerdictLogic="TPR UP & ITC Batt Charg < MinSafe for 15s => CONFIRMED"
            },

            // #6  TC CH RES LOW   [REVIEW] controller left threshold as "(low/short)"; PDF fills LD2=50%avg. deriv 587 ok.
            new CauseDef {
                Seq=6, Cause="TC CH RES LOW", AlertType="PREDICTIVE",
                LdPercent=0.50, LowerRule=true, SustainSec=15, DerivedFormula=587, Source="FRS",
                RuleRaw="TPR UP && RTC CH Feed End < LD2=50%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RTC CH Feed End", Attid=587, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD2=50%avg", EffectiveThreshold="avg*0.50" },
                },
                VerdictLogic="TPR UP & RTC CH Feed End < LD2=50%avg for 15s => CONFIRMED"
            },

            // #7  TC CH RES HIGH   [REVIEW] controller "(high)"; PDF fills HD2=150%avg. deriv 587 ok.
            new CauseDef {
                Seq=7, Cause="TC CH RES HIGH", AlertType="PREDICTIVE",
                LdPercent=1.50, LowerRule=false, SustainSec=15, DerivedFormula=587, Source="FRS",
                RuleRaw="TPR UP && RTC CH Feed End > HD2=150%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RTC CH Feed End", Attid=587, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD2=150%avg", EffectiveThreshold="avg*1.50" },
                },
                VerdictLogic="TPR UP & RTC CH Feed End > HD2=150%avg for 15s => CONFIRMED"
            },

            // #8  TC VAR RES LOW   [REVIEW] controller "(low/short)"; PDF fills LD2=50%avg. deriv 588 ok.
            new CauseDef {
                Seq=8, Cause="TC VAR RES LOW", AlertType="PREDICTIVE",
                LdPercent=0.50, LowerRule=true, SustainSec=15, DerivedFormula=588, Source="FRS",
                RuleRaw="TPR UP && RTC Var Res < LD2=50%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RTC Var Res", Attid=588, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD2=50%avg", EffectiveThreshold="avg*0.50" },
                },
                VerdictLogic="TPR UP & RTC Var Res < LD2=50%avg for 15s => CONFIRMED"
            },

            // #9  TC VAR RES HIGH   [REVIEW] controller "(high)"; PDF fills HD2=150%avg. deriv 588 ok.
            new CauseDef {
                Seq=9, Cause="TC VAR RES HIGH", AlertType="PREDICTIVE",
                LdPercent=1.50, LowerRule=false, SustainSec=15, DerivedFormula=588, Source="FRS",
                RuleRaw="TPR UP && RTC Var Res > HD2=150%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RTC Var Res", Attid=588, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD2=150%avg", EffectiveThreshold="avg*1.50" },
                },
                VerdictLogic="TPR UP & RTC Var Res > HD2=150%avg for 15s => CONFIRMED"
            },

            // #10  TC BALST/SLPR RES LOW   [REVIEW] controller thresholds blank; PDF fills. deriv 684 ok.
            new CauseDef {
                Seq=10, Cause="TC BALST/SLPR RES LOW", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=true, SustainSec=15, DerivedFormula=684, Source="FRS",
                RuleRaw="TPR UP && ITC Relay End < (LD1=80%avg OR MinSafe) && IBalst > HD1=120%avg && dIBalst>dITC Relay End for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="TPR" },
                    new CauseAttr{ Name="IBalst", Attid=684, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD1=120%avg", EffectiveThreshold="avg*1.20" },
                    new CauseAttr{ Name="dIBalst", Attid=0, Datatype="RDPMS", Class="derived", Cmp=">", ThresholdRef="dITC Relay End", EffectiveThreshold="compare with dITC Relay End" },
                },
                VerdictLogic="TPR UP & ITC Relay End<(LD1=80%avg OR MinSafe) & IBalst>HD1=120%avg & dIBalst>dITC Relay End for 15s => CONFIRMED"
            },

            // #11  TC RAIL RES HIGH
            // [REVIEW] HARD CONFLICT RESOLVED per owner ("pdf is ok, controller wrong"):
            //   controller had RRail > 120%avg; FRS/PDF is HD2=150%avg (sibling CH/VAR RES HIGH
            //   are also 150). Catalog adopts 150. deriv 590 ok. >>> controller CauseToLogic
            //   line 1676 needs correcting from 120%avg to 150%avg. <<<
            new CauseDef {
                Seq=11, Cause="TC RAIL RES HIGH", AlertType="PREDICTIVE",
                LdPercent=1.50, LowerRule=false, SustainSec=15, DerivedFormula=590, Source="FRS",
                RuleRaw="TPR UP && RRail > HD2=150%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RRail", Attid=590, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD2=150%avg", EffectiveThreshold="avg*1.50" },
                },
                VerdictLogic="TPR UP & RRail > HD2=150%avg for 15s => CONFIRMED"
            },

            // #12  TC TR VOLT LOW   [REVIEW] controller vague; PDF fills LD1=80 + prior-cause exclusion. no deriv.
            new CauseDef {
                Seq=12, Cause="TC TR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR UP && ITC Relay End < (LD1=80%avg OR MinSafe) && prior RailRes/Balst low-resistance causes do not qualify for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="TPR" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="RailRes/Balst causes NOT qualified", EffectiveThreshold="EXCLUSION: RailRes/Balst causes NOT qualified" },
                },
                VerdictLogic="TPR UP & ITC Relay End<(LD1=80%avg OR MinSafe) & prior RailRes/Balst causes do not qualify for 15s => CONFIRMED"
            },

            // #13  TC TR OVER ENERIZATION   [REVIEW] controller "(over-energized)"; PDF fills HD1=120%avg OR MaxSafe. no deriv.
            new CauseDef {
                Seq=13, Cause="TC TR OVER ENERIZATION", AlertType="PREDICTIVE",
                LdPercent=1.20, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR UP && ITC Relay End > (HD1=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD1=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="TPR" },
                },
                VerdictLogic="TPR UP & ITC Relay End > (HD1=120%avg OR MaxSafe) for 15s => CONFIRMED"
            },

            // #14  TC TR CONTACT RES HIGH   [REVIEW] controller vague; PDF fills VTC 24DC Loc<LD1=80%avg. no deriv.
            new CauseDef {
                Seq=14, Cause="TC TR CONTACT RES HIGH", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR UP && VIPS DC R EXT>=MinSafe && VTC 24DC Loc < LD1=80%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VTC 24DC Loc", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg", EffectiveThreshold="avg*0.80" },
                },
                VerdictLogic="TPR UP & VIPS DC R EXT>=MinSafe & VTC 24DC Loc < LD1=80%avg for 15s => CONFIRMED"
            },

            // #15  TC TPR I/P VOLT LOW   [REVIEW] controller vague; PDF fills LD3=90%avg gate + LD1=80/MinSafe. no deriv.
            new CauseDef {
                Seq=15, Cause="TC TPR I/P VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR UP && VTC 24DC Loc >= LD3=90%avg && VTC 24DC TPR I/P < (LD1=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VTC 24DC Loc", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD3=90%avg", EffectiveThreshold="avg*0.90" },
                    new CauseAttr{ Name="VTC 24DC TPR I/P", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)" },
                },
                VerdictLogic="TPR UP & VTC 24DC Loc>=LD3=90%avg & VTC 24DC TPR I/P < (LD1=80%avg OR MinSafe) for 15s => CONFIRMED"
            },

            // #16  TC GJ SHORT  --  CROSS-ASSET, two-track (glued-joint short between adjacent tracks)
            // -------------------------------------------------------------------------------------
            // [FRS 2.3(a) p.117 + owner clarification] "TR current low in adjacent track; glued joint
            //   between T1 and T2 may be defective."
            //   * T1 = the SHORTED track, TPR DN (drop): ITC Relay End < MinFail & > 0+, IBalst rises
            //     > HD1=120%avg, and dIBalst > dITC Relay End.
            //   * T2 = the ADJACENT / ALERT track, TPR UP (pickup): its ITC Relay End goes DOWN --
            //     FRS uses dITC Relay End > 10% of T2's value-when-TPR(T1)-was-UP.
            //   * d (delta) = magnitude (no sign) between the value BEFORE TPR(T1) UP and AFTER TPR(T1) DN.
            //   The alert fires on the TPR-UP track reporting the current drop; the neighbour (TPR DN)
            //   is the physically shorted track. SustainSec=0 (transition logic). deriv 684 (IBalst).
            //
            // [CONTROLLER REVIEW] CauseToLogic DOES carry T1[...]&&T2[...] (my earlier "missing T2" was
            //   wrong). Real gaps: (a) CROSS-ASSET -- T2 is a DIFFERENT (adjacent) track asset; the
            //   single-asset analysis can't fetch it, so the T2 branch is currently unverifiable at
            //   runtime (verifier TODO: fetch the adjacent track); (b) IBalst threshold blank in the
            //   string -> set HD1=120%avg here; (c) SustainSec should be 0, string parser defaults 15.
            //
            // [OPEN QUESTION for owner] FRS T2 = "dITC Relay End > 10% drop"; your verbal note said
            //   "IR mA < MinSafe on the TPR-UP track". These differ (relative 10% drop vs absolute
            //   MinSafe). Catalog uses the FRS (>10% drop) as authoritative per "pdf is ok"; confirm
            //   whether the <MinSafe check should be ADDED (OR-branch) or REPLACES the 10% rule.
            //   Also the "no OFFECR up for the failure track" remark in your note is unclear -- pls
            //   confirm what relay/condition that refers to before it goes into the logic.
            new CauseDef {
                Seq=16, Cause="TC GJ SHORT", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=true, SustainSec=0, DerivedFormula=684, Source="FRS",
                RuleRaw="T1[TPR DN && ITC Relay End<MinFail && ITC Relay End>0+ && IBalst>HD1=120%avg && dIBalst>dITC Relay End && adjacent(family) TPR UP] && T2[TPR UP && ITC Relay End<MinSafe]",
                Attrs=new [] {
                    // ---- T1: the shorted track (TPR DN) ----
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN", Gate="T1" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR/T1" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR/T1" },
                    new CauseAttr{ Name="IBalst", Attid=684, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD1=120%avg", EffectiveThreshold="avg*1.20", Gate="T1" },
                    new CauseAttr{ Name="dIBalst", Attid=0, Datatype="RDPMS", Class="derived", Cmp=">", ThresholdRef="dITC Relay End", EffectiveThreshold="compare with dITC Relay End", Gate="T1" },
                    // ---- T2: the adjacent / alert track (TPR UP) ----
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="UP", Gate="T2" },
                    // T2 threshold: owner confirmed the adjacent (TPR-UP) track's current uses
                    // ITC Relay End < MinSafe (not the FRS dITC Relay End > 10% drop; that variant
                    // is kept in the comment above for reference).
                    new CauseAttr{ Name="ITC Relay End (T2 adjacent)", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe (adjacent TPR-UP track current below MinSafe)", Gate="T2" },
                },
                VerdictLogic="T1 conditions & T2 conditions (adjacent tracks, separate scopes) => CONFIRMED"
            },

            // ================================================================
            // Track Failure (FRS 2.3(b))  --  LD1=70, LD2=90, HD1=120.
            // Section rule: FIRST confirm a genuine track failure via the
            // adjacent-track sequence rule (rule out train movement: adjacent
            // family-track TPR DROP->PICK in correspondence with the main
            // recovering = movement -> NOT_CONFIRMED; mapped signalTrack OFFECR=1
            // within +/-5s of the drop = shunt route -> NOT_CONFIRMED). THEN apply
            // the cause-specific failure logic below. Per P1, the DataLogger relay
            // (TPR DN) is the failure gate; the RDPMS attrs select WHICH cause.
            // Per P0, all of this is verified against HISTORY, not the card.
            // ================================================================

            // #17  TC SHORT   [REVIEW] matches controller; IBalst threshold blank in CauseToLogic -> HD1=120%avg here. deriv 684 ok.
            new CauseDef {
                Seq=17, Cause="TC SHORT", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=684, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End<MinFail && ITC Relay End>0+ && IBalst>HD1=120%avg && dIBalst>dITC Relay End for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="IBalst", Attid=684, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD1=120%avg", EffectiveThreshold="avg*1.20" },
                    new CauseAttr{ Name="dIBalst", Attid=0, Datatype="RDPMS", Class="derived", Cmp=">", ThresholdRef="dITC Relay End", EffectiveThreshold="compare with dITC Relay End" },
                },
                VerdictLogic="TPR DN & ITC Relay End<MinFail & ITC Relay End>0+ & IBalst>HD1=120%avg & dIBalst>dITC Relay End for 10s => CONFIRMED"
            },

            // #18  TC CH RES OPEN   [REVIEW] matches controller. deriv 587 ok.
            new CauseDef {
                Seq=18, Cause="TC CH RES OPEN", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=587, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End<MinFail && ITC Feed End>0+ && RTC CH Feed End>2xavg for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR" },
                    new CauseAttr{ Name="ITC Feed End", Attid=1, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="RTC CH Feed End", Attid=587, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="2xavg", EffectiveThreshold="2*avg" },
                },
                VerdictLogic="TPR DN & ITC Relay End<MinFail & ITC Feed End>0+ & RTC CH Feed End>2xavg for 10s => CONFIRMED"
            },

            // #19  TC VAR RES OPEN   [REVIEW] matches controller. deriv 588 ok.
            new CauseDef {
                Seq=19, Cause="TC VAR RES OPEN", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=588, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End<MinFail && ITC Feed End>0+ && RTC Var Res>2xavg for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR" },
                    new CauseAttr{ Name="ITC Feed End", Attid=1, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="RTC Var Res", Attid=588, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="2xavg", EffectiveThreshold="2*avg" },
                },
                VerdictLogic="TPR DN & ITC Relay End<MinFail & ITC Feed End>0+ & RTC Var Res>2xavg for 10s => CONFIRMED"
            },

            // #20  TC RAIL RES OPEN   [REVIEW] matches controller. deriv 590 ok.
            new CauseDef {
                Seq=20, Cause="TC RAIL RES OPEN", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=590, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End<MinFail && ITC Relay End>0+ && ITC Feed End>0+ && RRail>2xavg for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="ITC Feed End", Attid=1, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="RRail", Attid=590, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="2xavg", EffectiveThreshold="2*avg" },
                },
                VerdictLogic="TPR DN & ITC Relay End<MinFail & ITC Relay End>0+ & ITC Feed End>0+ & RRail>2xavg for 10s => CONFIRMED"
            },

            // #21  TC TFC O/P VOLT FAIL   [REVIEW] matches controller. no deriv.
            new CauseDef {
                Seq=21, Cause="TC TFC O/P VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End<MinFail && ITC Feed End>0+ && VTC TFC O/P<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="TPR" },
                    new CauseAttr{ Name="ITC Feed End", Attid=1, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR" },
                    new CauseAttr{ Name="VTC TFC O/P", Attid=569, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="TPR DN & ITC Relay End<MinFail & ITC Feed End>0+ & VTC TFC O/P<MinFail for 10s => CONFIRMED"
            },

            // #22  TC CKT OPEN   [REVIEW] matches controller (OR-group). no deriv.
            new CauseDef {
                Seq=22, Cause="TC CKT OPEN", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && (ITC Relay End<0+ OR ITC Feed End<0+) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR / OR-G1" },
                    new CauseAttr{ Name="ITC Feed End", Attid=1, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="0+", EffectiveThreshold="0+", Gate="TPR / OR-G1" },
                },
                VerdictLogic="relayAND(TPR:DN) & (ITC Relay End<0+ OR ITC Feed End<0+) => CONFIRMED"
            },

            // #23  TC TR RELAY DEFECT   [REVIEW] controller left VTC 24DC Loc threshold blank; PDF fills LD1=70%avg. no deriv.
            new CauseDef {
                Seq=23, Cause="TC TR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=0.70, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End>=MinSafe && VIPS DC R EXT>=MinFail && VTC 24DC Loc<LD1=70%avg for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="TPR" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VTC 24DC Loc", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=70%avg", EffectiveThreshold="avg*0.70" },
                },
                VerdictLogic="TPR DN & ITC Relay End>=MinSafe & VIPS DC R EXT>=MinFail & VTC 24DC Loc<LD1=70%avg for 10s => CONFIRMED"
            },

            // #24  TC TR UP TPR DN   [REVIEW] controller left VTC 24DC Loc threshold blank; PDF fills LD2=90%avg. no deriv.
            new CauseDef {
                Seq=24, Cause="TC TR UP TPR DN", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End>=MinSafe && VTC 24DC Loc>=LD2=90%avg && VTC 24DC TPR I/P<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="TPR" },
                    new CauseAttr{ Name="VTC 24DC Loc", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90" },
                    new CauseAttr{ Name="VTC 24DC TPR I/P", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="TPR DN & ITC Relay End>=MinSafe & VTC 24DC Loc>=LD2=90%avg & VTC 24DC TPR I/P<MinFail for 10s => CONFIRMED"
            },

            // #25  TC TPR RELAY DEFECT   [REVIEW] matches controller. no deriv.
            new CauseDef {
                Seq=25, Cause="TC TPR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && ITC Relay End>=MinSafe && VTC 24DC TPR I/P>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="ITC Relay End", Attid=2, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="TPR" },
                    new CauseAttr{ Name="VTC 24DC TPR I/P", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                },
                VerdictLogic="TPR DN & ITC Relay End>=MinSafe & VTC 24DC TPR I/P>=MinSafe for 10s => CONFIRMED"
            },

            // #26  REASON UNKNOWN (Track)   [REVIEW] exclusion/fallback; may be absent from controller CauseToLogic. SustainSec 0.
            new CauseDef {
                Seq=26, Cause="REASON UNKNOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="TPR DN && none of the preceding track-failure cause logics qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="TPR", Attid=6, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="-", EffectiveThreshold="-" },
                },
                VerdictLogic="TPR DN after track-failure confirmation & no preceding cause qualifies => CONFIRMED"
            },

            // ================================================================
            // Signal Predictive (FRS 2.4(a))  LD=80 HD=120.  [REVIEW] controller stored bare
            // percentages and DROPPED 'OR MinSafe'/'OR MaxSafe' -- catalog uses MAX/MIN effective
            // thresholds (the exact fix for the ROSIG/BUG mis-threshold). Derived: none.
            // ================================================================

            // #27  SIG RG VOLT/CURR LOW
            // [REVIEW] controller dropped 'OR MinSafe' (bare 80%avg); catalog uses MAX(avg*0.80,MinSafe) -- fixes the ROSIG/BUG mis-threshold class
            new CauseDef {
                Seq=27, Cause="SIG RG VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="RECR UP && VIPS Sig-1 110AC>=MinSafe && (VSIG RG<(LD=80%avg OR MinSafe) OR ISIG RG<(LD=80%avg OR MinSafe)) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG RG", Attid=9, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="RECR / OR-G1" },
                    new CauseAttr{ Name="ISIG RG", Attid=10, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="RECR / OR-G1" },
                },
                VerdictLogic="relayAND(RECR:UP) & VIPS Sig-1 110AC>=MinSafe & (VSIG RG<MAX(avg*0.80, MinSafe) OR ISIG RG<MAX(avg*0.80, MinSafe)) for 15s => CONFIRMED"
            },

            // #28  SIG HG VOLT/CURR LOW
            // [REVIEW] controller dropped 'OR MinSafe' (bare 80%avg); catalog uses MAX(avg*0.80,MinSafe) -- fixes the ROSIG/BUG mis-threshold class
            new CauseDef {
                Seq=28, Cause="SIG HG VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HECR UP && VIPS Sig-1 110AC>=MinSafe && (VSIG HG<(LD=80%avg OR MinSafe) OR ISIG HG<(LD=80%avg OR MinSafe)) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HG", Attid=13, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HECR / OR-G1" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HECR / OR-G1" },
                },
                VerdictLogic="relayAND(HECR:UP) & VIPS Sig-1 110AC>=MinSafe & (VSIG HG<MAX(avg*0.80, MinSafe) OR ISIG HG<MAX(avg*0.80, MinSafe)) for 15s => CONFIRMED"
            },

            // #29  SIG HHG VOLT/CURR LOW
            // [REVIEW] controller dropped 'OR MinSafe' (bare 80%avg); catalog uses MAX(avg*0.80,MinSafe) -- fixes the ROSIG/BUG mis-threshold class
            new CauseDef {
                Seq=29, Cause="SIG HHG VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HHECR UP && VIPS Sig-1 110AC>=MinSafe && (VSIG HHG<(LD=80%avg OR MinSafe) OR ISIG HHG<(LD=80%avg OR MinSafe)) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HHG", Attid=15, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HHECR / OR-G1" },
                    new CauseAttr{ Name="ISIG HHG", Attid=16, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HHECR / OR-G1" },
                },
                VerdictLogic="relayAND(HHECR:UP) & VIPS Sig-1 110AC>=MinSafe & (VSIG HHG<MAX(avg*0.80, MinSafe) OR ISIG HHG<MAX(avg*0.80, MinSafe)) for 15s => CONFIRMED"
            },

            // #30  SIG DG VOLT/CURR LOW
            // [REVIEW] controller dropped 'OR MinSafe' (bare 80%avg); catalog uses MAX(avg*0.80,MinSafe) -- fixes the ROSIG/BUG mis-threshold class
            new CauseDef {
                Seq=30, Cause="SIG DG VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="DECR UP && VIPS Sig-1 110AC>=MinSafe && (VSIG DG<(LD=80%avg OR MinSafe) OR ISIG DG<(LD=80%avg OR MinSafe)) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG DG", Attid=11, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="DECR / OR-G1" },
                    new CauseAttr{ Name="ISIG DG", Attid=12, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="DECR / OR-G1" },
                },
                VerdictLogic="relayAND(DECR:UP) & VIPS Sig-1 110AC>=MinSafe & (VSIG DG<MAX(avg*0.80, MinSafe) OR ISIG DG<MAX(avg*0.80, MinSafe)) for 15s => CONFIRMED"
            },

            // #31  SIG RG CURR HIGH
            // [REVIEW] controller dropped 'OR MaxSafe'; catalog uses MIN(avg*1.20,MaxSafe)
            new CauseDef {
                Seq=31, Cause="SIG RG CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="RECR UP && ISIG RG > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISIG RG", Attid=10, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="RECR" },
                },
                VerdictLogic="RECR UP & ISIG RG > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #32  SIG HG CURR HIGH
            // [REVIEW] controller dropped 'OR MaxSafe'; catalog uses MIN(avg*1.20,MaxSafe)
            new CauseDef {
                Seq=32, Cause="SIG HG CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HECR UP && ISIG HG > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="HECR" },
                },
                VerdictLogic="HECR UP & ISIG HG > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #33  SIG HHG CURR HIGH
            // [REVIEW] controller dropped 'OR MaxSafe'; catalog uses MIN(avg*1.20,MaxSafe)
            new CauseDef {
                Seq=33, Cause="SIG HHG CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HHECR UP && ISIG HHG > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISIG HHG", Attid=16, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="HHECR" },
                },
                VerdictLogic="HHECR UP & ISIG HHG > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #34  SIG DG CURR HIGH
            // [REVIEW] controller dropped 'OR MaxSafe'; catalog uses MIN(avg*1.20,MaxSafe)
            new CauseDef {
                Seq=34, Cause="SIG DG CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="DECR UP && ISIG DG > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISIG DG", Attid=12, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="DECR" },
                },
                VerdictLogic="DECR UP & ISIG DG > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #35  SIG HPR VOLT LOW
            // [REVIEW] controller dropped 'OR MinSafe'; catalog uses MAX(avg*0.80,MinSafe)
            new CauseDef {
                Seq=35, Cause="SIG HPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HECR UP && VIPS DC R EXT>=MinSafe && VSIG HPR < (LD=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HECR" },
                },
                VerdictLogic="HECR UP & VIPS DC R EXT>=MinSafe & VSIG HPR < MAX(avg*0.80, MinSafe) for 15s => CONFIRMED"
            },

            // #36  SIG HHPR VOLT LOW
            // [REVIEW] controller dropped 'OR MinSafe'; catalog uses MAX(avg*0.80,MinSafe)
            new CauseDef {
                Seq=36, Cause="SIG HHPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="HHECR UP && VIPS DC R EXT>=MinSafe && VSIG HHPR < (LD=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HHPR", Attid=327, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="HHECR" },
                },
                VerdictLogic="HHECR UP & VIPS DC R EXT>=MinSafe & VSIG HHPR < MAX(avg*0.80, MinSafe) for 15s => CONFIRMED"
            },

            // #37  SIG DPR VOLT LOW
            // [REVIEW] controller dropped 'OR MinSafe'; catalog uses MAX(avg*0.80,MinSafe)
            new CauseDef {
                Seq=37, Cause="SIG DPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="DECR UP && VIPS DC R EXT>=MinSafe && VSIG DPR < (LD=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG DPR", Attid=328, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="DECR" },
                },
                VerdictLogic="DECR UP & VIPS DC R EXT>=MinSafe & VSIG DPR < MAX(avg*0.80, MinSafe) for 15s => CONFIRMED"
            },


            // ================================================================
            // Signal Failure (FRS 2.4(b))  MinFail/MinSafe direct. Stage-1 relays decide the
            // verdict (aspect-state incl. OR-relay-groups OR-GR1/OR-GR2); Stage-2 RDPMS V/I picks
            // the sub-cause. UNKNOWN = exclusion fallback. (controller UNKNOWN keys spelt 'UNKOWN'.)
            // ================================================================

            // #38  SIG RG VOLT/CURR FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=38, Cause="SIG RG VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR DN && RECR DN && VIPS Sig-1 110AC>=MinFail && (VSIG RG<MinFail OR ISIG RG<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG RG", Attid=9, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="RECR / OR-G1" },
                    new CauseAttr{ Name="ISIG RG", Attid=10, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="RECR / OR-G1" },
                },
                VerdictLogic="relayAND(HR:DN,RECR:DN) & VIPS Sig-1 110AC>=MinFail & (VSIG RG<MinFail OR ISIG RG<MinFail) => CONFIRMED"
            },

            // #39  SIG RG RECR RELAY DEFECT
            // [REVIEW] matches controller
            new CauseDef {
                Seq=39, Cause="SIG RG RECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR DN && RECR DN && VSIG RG>=MinSafe && ISIG RG>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VSIG RG", Attid=9, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="RECR" },
                    new CauseAttr{ Name="ISIG RG", Attid=10, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="RECR" },
                },
                VerdictLogic="HR DN & RECR DN & VSIG RG>=MinSafe & ISIG RG>=MinSafe for 10s => CONFIRMED"
            },

            // #40  SIG RG UNKNOWN
            // [REVIEW] controller key 'SIG RG UNKOWN' (sic)
            new CauseDef {
                Seq=40, Cause="SIG RG UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="HR DN && RECR DN && SIG RG failure causes above do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="HR DN & RECR DN & SIG RG Sr1/Sr2 do not qualify => CONFIRMED"
            },

            // #41  SIG HG VOLT/CURR FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=41, Cause="SIG HG VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR DN && DR DN && HECR DN && RECR UP && VSIG HPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && (VSIG HG<MinFail OR ISIG HG<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG HG", Attid=13, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HECR / OR-G1" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HECR / OR-G1" },
                },
                VerdictLogic="HG relays & VSIG HPR>=MinSafe & IPS>=MinFail & (VSIG HG<MinFail OR ISIG HG<MinFail) => CONFIRMED"
            },

            // #42  SIG HG HPR VOLT FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=42, Cause="SIG HG HPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR DN && DR DN && HECR DN && RECR UP && VIPS DC R EXT>=MinFail && VSIG HPR<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HECR" },
                },
                VerdictLogic="HG relays & VIPS DC R EXT>=MinFail & VSIG HPR<MinFail => CONFIRMED"
            },

            // #43  SIG HG HECR RELAY DEFECT
            // [REVIEW] matches controller
            new CauseDef {
                Seq=43, Cause="SIG HG HECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR DN && DR DN && HECR DN && RECR UP && VSIG HPR>=MinSafe && VSIG HG>=MinSafe && ISIG HG>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                    new CauseAttr{ Name="VSIG HG", Attid=13, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                },
                VerdictLogic="HG relays & VSIG HPR/HG>=MinSafe & ISIG HG>=MinSafe => CONFIRMED"
            },

            // #44  SIG HG UNKNOWN
            // [REVIEW] controller key 'SIG HG UNKOWN' (sic)
            new CauseDef {
                Seq=44, Cause="SIG HG UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR DN && DR DN && HECR DN && RECR UP && SIG HG causes Sr4/Sr5/Sr6 do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="HG relays & Sr4/5/6 do not qualify => CONFIRMED"
            },

            // #45  SIG HHG VOLT/CURR FAIL
            // [REVIEW] matches controller (OR-relay groups)
            new CauseDef {
                Seq=45, Cause="SIG HHG VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR DN && (HECR DN OR HHECR DN) && (RECR UP OR HECR UP OR HHECR UP) && VSIG HPR>=MinSafe && VSIG HHPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && (VSIG HG<MinFail OR VSIG HHG<MinFail OR ISIG HG<MinFail OR ISIG HHG<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HHPR", Attid=327, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG HG", Attid=13, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HECR / OR-G3" },
                    new CauseAttr{ Name="VSIG HHG", Attid=15, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HHECR / OR-G3" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HECR / OR-G3" },
                    new CauseAttr{ Name="ISIG HHG", Attid=16, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="HHECR / OR-G3" },
                },
                VerdictLogic="HHG relays & HPR/HHPR safe & IPS>=MinFail & (any HG/HHG V/I < MinFail) => CONFIRMED"
            },

            // #46  SIG HHG HHPR VOLT FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=46, Cause="SIG HHG HHPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR DN && (HECR DN OR HHECR DN) && (RECR UP OR HECR UP OR HHECR UP) && VIPS DC R EXT>=MinFail && (VSIG HPR<MinFail OR VSIG HHPR<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G3" },
                    new CauseAttr{ Name="VSIG HHPR", Attid=327, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G3" },
                },
                VerdictLogic="HHG relays & VIPS DC R EXT>=MinFail & (VSIG HPR<MinFail OR VSIG HHPR<MinFail) => CONFIRMED"
            },

            // #47  SIG HHG HHECR RELAY DEFECT
            // [REVIEW] matches controller
            new CauseDef {
                Seq=47, Cause="SIG HHG HHECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR DN && (HECR DN OR HHECR DN) && (RECR UP OR HECR UP OR HHECR UP) && VSIG HPR>=MinSafe && VSIG HHPR>=MinSafe && VSIG HG>=MinSafe && VSIG HHG>=MinSafe && ISIG HG>=MinSafe && ISIG HHG>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="VSIG HPR", Attid=329, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HHPR", Attid=327, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSIG HG", Attid=13, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                    new CauseAttr{ Name="VSIG HHG", Attid=15, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HHECR" },
                    new CauseAttr{ Name="ISIG HG", Attid=14, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HECR" },
                    new CauseAttr{ Name="ISIG HHG", Attid=16, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="HHECR" },
                },
                VerdictLogic="HHG relays & all HPR/HHPR/HG/HHG V,I >=MinSafe => CONFIRMED"
            },

            // #48  SIG HHG UNKNOWN
            // [REVIEW] controller key 'SIG HHG UNKOWN' (sic)
            new CauseDef {
                Seq=48, Cause="SIG HHG UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR DN && (HECR DN OR HHECR DN) && (RECR UP OR HECR UP OR HHECR UP) && HHG causes Sr8/Sr9/Sr10 do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="DN", Gate="OR-GR1" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR2" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="HHG relays & Sr8/9/10 do not qualify => CONFIRMED"
            },

            // #49  SIG DG VOLT/CURR FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=49, Cause="SIG DG VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR UP && DECR DN && (RECR UP OR HECR UP OR HHECR UP) && VSIG DPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && (VSIG DG<MinFail OR ISIG DG<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="VSIG DPR", Attid=328, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="DECR" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG DG", Attid=11, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="DECR / OR-G2" },
                    new CauseAttr{ Name="ISIG DG", Attid=12, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="DECR / OR-G2" },
                },
                VerdictLogic="DG relays & VSIG DPR>=MinSafe & IPS>=MinFail & (VSIG DG<MinFail OR ISIG DG<MinFail) => CONFIRMED"
            },

            // #50  SIG DG DPR VOLT FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=50, Cause="SIG DG DPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR UP && DECR DN && (RECR UP OR HECR UP OR HHECR UP) && VIPS DC R EXT>=MinFail && VSIG DPR<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSIG DPR", Attid=328, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="DECR" },
                },
                VerdictLogic="DG relays & VIPS DC R EXT>=MinFail & VSIG DPR<MinFail => CONFIRMED"
            },

            // #51  SIG DG DECR RELAY DEFECT
            // [REVIEW] matches controller
            new CauseDef {
                Seq=51, Cause="SIG DG DECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR UP && DECR DN && (RECR UP OR HECR UP OR HHECR UP) && VSIG DPR>=MinSafe && VSIG DG>=MinSafe && ISIG DG>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="VSIG DPR", Attid=328, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="DECR" },
                    new CauseAttr{ Name="VSIG DG", Attid=11, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="DECR" },
                    new CauseAttr{ Name="ISIG DG", Attid=12, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="DECR" },
                },
                VerdictLogic="DG relays & VSIG DPR/DG>=MinSafe & ISIG DG>=MinSafe => CONFIRMED"
            },

            // #52  SIG DG UNKNOWN
            // [REVIEW] controller key 'SIG DG UNKOWN' (sic)
            new CauseDef {
                Seq=52, Cause="SIG DG UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && HHR UP && DR UP && DECR DN && (RECR UP OR HECR UP OR HHECR UP) && DG causes Sr12/Sr13/Sr14 do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="HHR", Attid=67, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DR", Attid=66, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="UP", Gate="OR-GR1" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="DG relays & Sr12/13/14 do not qualify => CONFIRMED"
            },

            // #53  SIG BLANK
            // [REVIEW] matches controller
            new CauseDef {
                Seq=53, Cause="SIG BLANK", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="HR UP && RECR DN && HECR DN && HHECR DN && DECR DN for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="HR", Attid=136, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="RECR", Attid=48, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HECR", Attid=45, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="HHECR", Attid=47, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="DECR", Attid=46, Datatype="DataLogger", Class="relay", State="DN" },
                },
                VerdictLogic="HR UP & RECR DN & HECR DN & HHECR DN & DECR DN for 10s => CONFIRMED"
            },


            // ================================================================
            // Calling-Signal (CO) (FRS 2.5)  mirrors ROSIG shape: CO-HECR / VCOSIG / ICOSIG.
            // Predictive uses MAX/MIN thresholds; failure = Stage-1 relays + Stage-2 RDPMS.
            // ================================================================

            // #54  COSIG ASPECT VOLT/CURR LOW
            // [REVIEW] controller dropped 'OR MinSafe'; catalog uses MAX(avg*0.80,MinSafe)
            new CauseDef {
                Seq=54, Cause="COSIG ASPECT VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HECR UP && VIPS Sig-1 110AC>=MinSafe && (VCOSIG<(LD=80%avg OR MinSafe) OR ICOSIG<(LD=80%avg OR MinSafe)) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VCOSIG", Attid=611, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="Co_HECR / OR-G1" },
                    new CauseAttr{ Name="ICOSIG", Attid=610, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="Co_HECR / OR-G1" },
                },
                VerdictLogic="CO-HECR UP & VIPS Sig-1 110AC>=MinSafe & (VCOSIG<MAX(avg*0.80, MinSafe) OR ICOSIG<MAX(avg*0.80, MinSafe)) for 15s => CONFIRMED"
            },

            // #55  COSIG ASPECT CURR HIGH
            // [REVIEW] controller dropped 'OR MaxSafe'; catalog uses MIN(avg*1.20,MaxSafe)
            new CauseDef {
                Seq=55, Cause="COSIG ASPECT CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HECR UP && ICOSIG > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ICOSIG", Attid=610, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="Co_HECR" },
                },
                VerdictLogic="CO-HECR UP & ICOSIG > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #56  COSIG HPR VOLT LOW
            // [REVIEW] controller dropped 'OR MinSafe'
            new CauseDef {
                Seq=56, Cause="COSIG HPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HECR UP && VIPS DC R EXT>=MinSafe && VCOSIG HPR < (LD=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VCOSIG HPR", Attid=612, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="Co_HECR" },
                },
                VerdictLogic="CO-HECR UP & VIPS DC R EXT>=MinSafe & VCOSIG HPR < MAX(avg*0.80, MinSafe) for 15s => CONFIRMED"
            },

            // #57  COSIG ASPECT VOLT/CURR FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=57, Cause="COSIG ASPECT VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HR UP && CO-HECR DN && VCOSIG HPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && (VCOSIG<MinFail OR ICOSIG<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HR", Attid=270, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VCOSIG HPR", Attid=612, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="Co_HECR" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VCOSIG", Attid=611, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="Co_HECR / OR-G1" },
                    new CauseAttr{ Name="ICOSIG", Attid=610, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="Co_HECR / OR-G1" },
                },
                VerdictLogic="CO-HR UP & CO-HECR DN & VCOSIG HPR>=MinSafe & IPS>=MinFail & (VCOSIG<MinFail OR ICOSIG<MinFail) => CONFIRMED"
            },

            // #58  COSIG HPR VOLT FAIL
            // [REVIEW] matches controller
            new CauseDef {
                Seq=58, Cause="COSIG HPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HR UP && CO-HECR DN && VIPS DC R EXT>=MinFail && VCOSIG HPR<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HR", Attid=270, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VCOSIG HPR", Attid=612, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="Co_HECR" },
                },
                VerdictLogic="CO-HR UP & CO-HECR DN & VIPS DC R EXT>=MinFail & VCOSIG HPR<MinFail => CONFIRMED"
            },

            // #59  COSIG HECR RELAY DEFECT
            // [REVIEW] matches controller
            new CauseDef {
                Seq=59, Cause="COSIG HECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HR UP && CO-HECR DN && VCOSIG HPR>=MinSafe && VCOSIG>=MinSafe && ICOSIG>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HR", Attid=270, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VCOSIG HPR", Attid=612, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="Co_HECR" },
                    new CauseAttr{ Name="VCOSIG", Attid=611, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="Co_HECR" },
                    new CauseAttr{ Name="ICOSIG", Attid=610, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="Co_HECR" },
                },
                VerdictLogic="CO-HR UP & CO-HECR DN & VCOSIG HPR/VCOSIG/ICOSIG>=MinSafe => CONFIRMED"
            },

            // #60  COSIG REASON UNKNOWN
            // [REVIEW] matches controller
            new CauseDef {
                Seq=60, Cause="COSIG REASON UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="CO-HR UP && CO-HECR DN && preceding Calling-ON failure logics do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="CO-HR", Attid=270, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="CO-HECR", Attid=271, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="CO-HR UP & CO-HECR DN & preceding Calling-ON failure logics do not qualify => CONFIRMED"
            },

            // ================================================================
            // Route-Signal (ROSIG)  --  FRS 2.6  [built early, per owner focus]
            // The route that fired (AUG/BUG/CUG/DUG/EUG) is read from the CARD's triggered
            // param, then resolved to an attid via RosigRouteMa / RosigRouteV. So route V/I
            // rows are RUNTIME SLOTS (Attid=0). CURRENT comparisons use the SUM
            // (route mA + PilotRootMa 631, if present) vs the route attr's own effective
            // threshold; VOLTAGE comparisons use route V alone (no sum). LOW/HIGH/FAIL all sum.
            // ================================================================

            // #61  ROSIG ASPECT VOLT/CURR LOW  (predictive)
            new CauseDef {
                Seq=61, Cause="ROSIG ASPECT VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="UECR UP && VIPS Sig-1 110AC>=MinSafe && ( <routeV> < (LD=80%avg OR MinSafe) OR (<routeI> + PILOTRoot mA) < (LD=80%avg OR MinSafe) ) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="<route> V (from card: AUG/BUG/CUG/DUG/EUG)", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe) of route attr -- VOLTAGE, no sum", Gate="UECR / OR-G1" },
                    new CauseAttr{ Name="<route> mA (from card: AUG/BUG/CUG/DUG/EUG)", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="(route mA + PILOTRoot mA[631] if present) < MAX(avg*0.80, MinSafe) of route attr -- CURRENT sums", Gate="UECR / OR-G1" },
                    new CauseAttr{ Name="PILOTRoot mA", Attid=631, Datatype="RDPMS", Class="derived", Cmp="+", ThresholdRef="summation input to route mA (if not null)", EffectiveThreshold="added to route mA before compare; P3-skip if absent" },
                },
                VerdictLogic="UECR UP & VIPS Sig-1 110AC>=MinSafe & ( routeV < eff(routeV) OR (routeI + PILOTRoot mA) < eff(routeI) ) for 15s => CONFIRMED"
            },

            // #62  ROSIG ASPECT CURR HIGH  (predictive)  -- current sums PILOTRoot too
            new CauseDef {
                Seq=62, Cause="ROSIG ASPECT CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.20, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="UECR UP && (<routeI> + PILOTRoot mA) > (HD=120%avg OR MaxSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="<route> mA (from card)", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="(route mA + PILOTRoot mA[631] if present) > MIN(avg*1.20, MaxSafe) of route attr -- CURRENT sums", Gate="UECR" },
                    new CauseAttr{ Name="PILOTRoot mA", Attid=631, Datatype="RDPMS", Class="derived", Cmp="+", ThresholdRef="summation input to route mA (if not null)", EffectiveThreshold="added to route mA before compare; P3-skip if absent" },
                },
                VerdictLogic="UECR UP & (routeI + PILOTRoot mA) > MIN(avg*1.20, MaxSafe) for 15s => CONFIRMED"
            },

            // #63  ROSIG HPR VOLT LOW  (predictive)  -- voltage, no sum
            new CauseDef {
                Seq=63, Cause="ROSIG HPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.80, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="UECR UP && VIPS DC R EXT>=MinSafe && VROSIG HPR < (LD=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VROSIG HPR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe) -- VOLTAGE, no sum", Gate="UECR" },
                },
                VerdictLogic="UECR UP & VIPS DC R EXT>=MinSafe & VROSIG HPR < (LD=80%avg OR MinSafe) for 15s => CONFIRMED"
            },

            // #64  ROSIG ASPECT VOLT/CURR FAIL  (FAILURE: Stage1 relays UHR/UECR; Stage2 RDPMS V/I)
            new CauseDef {
                Seq=64, Cause="ROSIG ASPECT VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="UHR UP && UECR DN && VROSIG HPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && ( <routeV><MinFail OR (<routeI>+PILOTRoot mA)<MinFail ) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="UHR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VROSIG HPR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="UECR" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="<route> V (from card)", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail -- VOLTAGE, no sum", Gate="UECR / OR-G1" },
                    new CauseAttr{ Name="<route> mA (from card)", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="(route mA + PILOTRoot mA[631] if present) < MinFail -- CURRENT sums", Gate="UECR / OR-G1" },
                    new CauseAttr{ Name="PILOTRoot mA", Attid=631, Datatype="RDPMS", Class="derived", Cmp="+", ThresholdRef="summation input to route mA (if not null)", EffectiveThreshold="added to route mA before compare; P3-skip if absent" },
                },
                VerdictLogic="UHR UP & UECR DN & VROSIG HPR>=MinSafe & VIPS Sig-1 110AC>=MinFail & (routeV<MinFail OR (routeI+PILOTRoot mA)<MinFail) for 10s => CONFIRMED"
            },

            // #65  ROSIG HPR VOLT FAIL  (FAILURE)  -- voltage, no sum
            new CauseDef {
                Seq=65, Cause="ROSIG HPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="UHR UP && UECR DN && VIPS DC R EXT>=MinFail && VROSIG HPR<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="UHR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VROSIG HPR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail -- VOLTAGE, no sum", Gate="UECR" },
                },
                VerdictLogic="UHR UP & UECR DN & VIPS DC R EXT>=MinFail & VROSIG HPR<MinFail for 10s => CONFIRMED"
            },

            // #66  ROSIG UECR RELAY DEFECT  (FAILURE)  -- V and I healthy => relay is the fault
            new CauseDef {
                Seq=66, Cause="ROSIG UECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="UHR UP && UECR DN && VROSIG HPR>=MinSafe && VROSIG>=MinSafe && (<routeI>+PILOTRoot mA)>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="UHR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VROSIG HPR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="UECR" },
                    new CauseAttr{ Name="<route> V (from card)", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe -- VOLTAGE, no sum", Gate="UECR" },
                    new CauseAttr{ Name="<route> mA (from card)", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="(route mA + PILOTRoot mA[631] if present) >= MinSafe -- CURRENT sums", Gate="UECR" },
                    new CauseAttr{ Name="PILOTRoot mA", Attid=631, Datatype="RDPMS", Class="derived", Cmp="+", ThresholdRef="summation input to route mA (if not null)", EffectiveThreshold="added to route mA before compare; P3-skip if absent" },
                },
                VerdictLogic="UHR UP & UECR DN & VROSIG HPR>=MinSafe & VROSIG>=MinSafe & (routeI+PILOTRoot mA)>=MinSafe for 10s => CONFIRMED"
            },

            // #67  ROSIG REASON UNKNOWN  (FAILURE, exclusion/fallback)
            new CauseDef {
                Seq=67, Cause="ROSIG REASON UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="UHR UP && UECR DN && preceding Route-Signal failure logics do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="UHR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="UECR", Attid=44, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding ROSIG failure causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="UHR UP & UECR DN & preceding Route-Signal failure logics do not qualify => CONFIRMED"
            },

            // ================================================================
            // Point (FRS 2.2)  --  A/B end pairs, temporal operation window.
            // A/B ATTID: each point analog attr resolves to an A-end/B-end attid pair
            // (card gives the end). See PointAttid table above. N/R = Normal/Reverse
            // OPERATION, not the A/B end.
            // Op-window (SustainSec=0): NWCR/RWCR temporal 'op<=12s' + NWKR/RWKR 'within 2s'.
            //   Op V/I = the EdgeX PM -Avg for that throw (2002/4002 N-V, 1002/3002 N-C, ...).
            // Transition failures (SustainSec=2): NWKR/RWKR UP->DN, evaluate 2s after DN.
            // Indication dwell (SustainSec=15): NWKR/RWKR UP + VPT 24DC/NWKR steady.
            // P1: relays (NWCR/RWCR/NWKR/RWKR states+transitions) decide the FAILURE verdict;
            //     point analog (VPT/IPT/TPT) selects WHICH cause. Op V/I are the -Avg values.
            // ================================================================

            // #68  PT N VOLT/CURR LOW
            new CauseDef {
                Seq=68, Cause="PT N VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR UP(entire duration or 12s max) && IIPS Batt Char 110DC>0+ && VIPS 110DC>=MinSafe && (VPT 110DC Loc N<(LD1=80%avg OR MinSafe) OR IPT N<(LD1=80%avg OR MinSafe)); NWKR UP within 2s",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VPT 110DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OR-G1" },
                    new CauseAttr{ Name="IPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OR-G1" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                },
                VerdictLogic="NWCR UP(op<=12s) & IIPS Batt Char 110DC>0+ & VIPS 110DC>=MinSafe & (VPT 110DC Loc N<eff OR IPT N<eff); NWKR UP within 2s => CONFIRMED"
            },

            // #69  PT R VOLT/CURR LOW
            new CauseDef {
                Seq=69, Cause="PT R VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="RWCR UP(entire duration or 12s max) && IIPS Batt Char 110DC>0+ && VIPS 110DC>=MinSafe && (VPT 110DC Loc R<(LD1=80%avg OR MinSafe) OR IPT R<(LD1=80%avg OR MinSafe)); RWKR UP within 2s",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VPT 110DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OR-G1" },
                    new CauseAttr{ Name="IPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OR-G1" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                },
                VerdictLogic="RWCR UP(op<=12s) & IIPS Batt Char 110DC>0+ & VIPS 110DC>=MinSafe & (VPT 110DC Loc R<eff OR IPT R<eff); RWKR UP within 2s => CONFIRMED"
            },

            // #70  PT N IND VOLT LOW AT LOC
            new CauseDef {
                Seq=70, Cause="PT N IND VOLT LOW AT LOC", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="NWKR UP && VIPS DC R EXT>=MinSafe && VPT 24DC Loc N < LD1=80%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VPT 24DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg", EffectiveThreshold="avg*0.80", Gate="NWKR" },
                },
                VerdictLogic="NWKR UP & VIPS DC R EXT>=MinSafe & VPT 24DC Loc N < avg*0.80 for 15s => CONFIRMED"
            },

            // #71  PT R IND VOLT LOW AT LOC
            new CauseDef {
                Seq=71, Cause="PT R IND VOLT LOW AT LOC", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="RWKR UP && VIPS DC R EXT>=MinSafe && VPT 24DC Loc R < LD1=80%avg for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VPT 24DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg", EffectiveThreshold="avg*0.80", Gate="RWKR" },
                },
                VerdictLogic="RWKR UP & VIPS DC R EXT>=MinSafe & VPT 24DC Loc R < avg*0.80 for 15s => CONFIRMED"
            },

            // #72  PT VOLT LOW AT NWKR
            new CauseDef {
                Seq=72, Cause="PT VOLT LOW AT NWKR", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="NWKR UP && VPT 24DC Loc N>=LD2=90%avg && VPT NWKR<(LD1=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VPT 24DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="NWKR" },
                    new CauseAttr{ Name="VPT NWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="NWKR" },
                },
                VerdictLogic="NWKR UP & VPT 24DC Loc N>=avg*0.90 & VPT NWKR<eff for 15s => CONFIRMED"
            },

            // #73  PT VOLT LOW AT RWKR
            new CauseDef {
                Seq=73, Cause="PT VOLT LOW AT RWKR", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="RWKR UP && VPT 24DC Loc R>=LD2=90%avg && VPT RWKR<(LD1=80%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VPT 24DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="RWKR" },
                    new CauseAttr{ Name="VPT RWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="RWKR" },
                },
                VerdictLogic="RWKR UP & VPT 24DC Loc R>=avg*0.90 & VPT RWKR<eff for 15s => CONFIRMED"
            },

            // #74  PT N TIME HIGH
            new CauseDef {
                Seq=74, Cause="PT N TIME HIGH", AlertType="PREDICTIVE",
                LdPercent=1.5, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR UP(entire duration or 12s max); NWKR UP within 2s; TPT N>HD=150%avg",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                    new CauseAttr{ Name="TPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=150%avg", EffectiveThreshold="avg*1.50" },
                },
                VerdictLogic="NWCR UP(op<=12s); NWKR UP within 2s; TPT N>avg*1.50 => CONFIRMED"
            },

            // #75  PT R TIME HIGH
            new CauseDef {
                Seq=75, Cause="PT R TIME HIGH", AlertType="PREDICTIVE",
                LdPercent=1.5, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="RWCR UP(entire duration or 12s max); RWKR UP within 2s; TPT R>HD=150%avg",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                    new CauseAttr{ Name="TPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=150%avg", EffectiveThreshold="avg*1.50" },
                },
                VerdictLogic="RWCR UP(op<=12s); RWKR UP within 2s; TPT R>avg*1.50 => CONFIRMED"
            },

            // ---- Point Failure (relays decide verdict; point analog selects cause) ----
            // #76  PT N IND VOLT FAIL AT LOC
            new CauseDef {
                Seq=76, Cause="PT N IND VOLT FAIL AT LOC", AlertType="FAILURE",
                LdPercent=0.7, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && RWKR DN && NWKR UP->DN && VIPS DC R EXT>=MinFail && VPT 24DC Loc N<LD1=70%avg for 2s after NWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VPT 24DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=70%avg", EffectiveThreshold="avg*0.70", Gate="NWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & RWKR DN & NWKR UP->DN & VIPS DC R EXT>=MinFail & VPT 24DC Loc N<avg*0.70 for 2s after NWKR DN => CONFIRMED"
            },

            // #77  PT R IND VOLT FAIL AT LOC
            new CauseDef {
                Seq=77, Cause="PT R IND VOLT FAIL AT LOC", AlertType="FAILURE",
                LdPercent=0.7, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && NWKR DN && RWKR UP->DN && VIPS DC R EXT>=MinFail && VPT 24DC Loc R<LD1=70%avg for 2s after RWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VPT 24DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD1=70%avg", EffectiveThreshold="avg*0.70", Gate="RWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & NWKR DN & RWKR UP->DN & VIPS DC R EXT>=MinFail & VPT 24DC Loc R<avg*0.70 for 2s after RWKR DN => CONFIRMED"
            },

            // #78  PT VOLT FAIL AT NWKR OP
            new CauseDef {
                Seq=78, Cause="PT VOLT FAIL AT NWKR OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="SEQ1[NWCR UP(op<=12s) && VPT 110DC Loc N>=MinSafe && IPT N>=MinSafe] THEN SEQ2[NWKR DN up to 2s && VPT 24DC Loc N>=LD2=90%avg && VPT NWKR<MinFail]",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="VPT 24DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="NWKR / SEQ2" },
                    new CauseAttr{ Name="VPT NWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="NWKR / SEQ2" },
                },
                VerdictLogic="SEQ1[NWCR UP(op<=12s) & VPT 110DC Loc N>=MinSafe & IPT N>=MinSafe] THEN SEQ2[NWKR DN<=2s & VPT 24DC Loc N>=avg*0.90 & VPT NWKR<MinFail] => CONFIRMED"
            },

            // #79  PT VOLT FAIL AT RWKR OP
            new CauseDef {
                Seq=79, Cause="PT VOLT FAIL AT RWKR OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="SEQ1[RWCR UP(op<=12s) && VPT 110DC Loc R>=MinSafe && IPT R>=MinSafe] THEN SEQ2[RWKR DN up to 2s && VPT 24DC Loc R>=LD2=90%avg && VPT RWKR<MinFail]",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="VPT 24DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="RWKR / SEQ2" },
                    new CauseAttr{ Name="VPT RWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="RWKR / SEQ2" },
                },
                VerdictLogic="SEQ1[RWCR UP(op<=12s) & VPT 110DC Loc R>=MinSafe & IPT R>=MinSafe] THEN SEQ2[RWKR DN<=2s & VPT 24DC Loc R>=avg*0.90 & VPT RWKR<MinFail] => CONFIRMED"
            },

            // #80  PT NWKR RELAY DEFECT OP
            new CauseDef {
                Seq=80, Cause="PT NWKR RELAY DEFECT OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="SEQ1[NWCR UP(op<=12s) && VPT 110DC Loc N>=MinSafe && IPT N>=MinSafe] THEN SEQ2[NWKR DN up to 2s && VPT NWKR>=MinSafe]",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="VPT NWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="NWKR / SEQ2" },
                },
                VerdictLogic="SEQ1[NWCR UP(op<=12s) & VPT 110DC Loc N>=MinSafe & IPT N>=MinSafe] THEN SEQ2[NWKR DN<=2s & VPT NWKR>=MinSafe] => CONFIRMED"
            },

            // #81  PT RWKR RELAY DEFECT OP
            new CauseDef {
                Seq=81, Cause="PT RWKR RELAY DEFECT OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="SEQ1[RWCR UP(op<=12s) && VPT 110DC Loc R>=MinSafe && IPT R>=MinSafe] THEN SEQ2[RWKR DN up to 2s && VPT RWKR>=MinSafe]",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="VPT RWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="RWKR / SEQ2" },
                },
                VerdictLogic="SEQ1[RWCR UP(op<=12s) & VPT 110DC Loc R>=MinSafe & IPT R>=MinSafe] THEN SEQ2[RWKR DN<=2s & VPT RWKR>=MinSafe] => CONFIRMED"
            },

            // #82  PT N VOLT/CURR FAIL
            new CauseDef {
                Seq=82, Cause="PT N VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR UP(op<=12s) && VIPS 110DC>=MinFail && (VPT 110DC Loc N<MinFail OR IPT N<MinFail); NWKR DN up to 2s",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VPT 110DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G1" },
                    new CauseAttr{ Name="IPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G1" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                },
                VerdictLogic="NWCR UP(op<=12s) & VIPS 110DC>=MinFail & (VPT 110DC Loc N<MinFail OR IPT N<MinFail); NWKR DN<=2s => CONFIRMED"
            },

            // #83  PT R VOLT/CURR FAIL
            new CauseDef {
                Seq=83, Cause="PT R VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="RWCR UP(op<=12s) && VIPS 110DC>=MinFail && (VPT 110DC Loc R<MinFail OR IPT R<MinFail); RWKR DN up to 2s",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VPT 110DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G1" },
                    new CauseAttr{ Name="IPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OR-G1" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                },
                VerdictLogic="RWCR UP(op<=12s) & VIPS 110DC>=MinFail & (VPT 110DC Loc R<MinFail OR IPT R<MinFail); RWKR DN<=2s => CONFIRMED"
            },

            // #84  PT N OBS
            new CauseDef {
                Seq=84, Cause="PT N OBS", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR UP(op<=12s) && VPT 110DC Loc N>=MinSafe && IPT N>=MinSafe; NWKR DN up to 2s; TPT N>MaxSafe",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="TPT N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="MaxSafe", EffectiveThreshold="MaxSafe", Gate="SEQ2" },
                },
                VerdictLogic="NWCR UP(op<=12s) & VPT 110DC Loc N>=MinSafe & IPT N>=MinSafe; NWKR DN<=2s; TPT N>MaxSafe => CONFIRMED"
            },

            // #85  PT R OBS
            new CauseDef {
                Seq=85, Cause="PT R OBS", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="RWCR UP(op<=12s) && VPT 110DC Loc R>=MinSafe && IPT R>=MinSafe; RWKR DN up to 2s; TPT R>MaxSafe",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="SEQ1" },
                    new CauseAttr{ Name="VPT 110DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="IPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="SEQ1" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="SEQ2" },
                    new CauseAttr{ Name="TPT R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="MaxSafe", EffectiveThreshold="MaxSafe", Gate="SEQ2" },
                },
                VerdictLogic="RWCR UP(op<=12s) & VPT 110DC Loc R>=MinSafe & IPT R>=MinSafe; RWKR DN<=2s; TPT R>MaxSafe => CONFIRMED"
            },

            // #86  PT VOLT FAIL AT NWKR
            new CauseDef {
                Seq=86, Cause="PT VOLT FAIL AT NWKR", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && RWKR DN && NWKR UP->DN && VPT 24DC Loc N>=LD2=90%avg && VPT NWKR<MinFail for 2s after NWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VPT 24DC Loc N", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="NWKR" },
                    new CauseAttr{ Name="VPT NWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="NWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & RWKR DN & NWKR UP->DN & VPT 24DC Loc N>=avg*0.90 & VPT NWKR<MinFail for 2s after NWKR DN => CONFIRMED"
            },

            // #87  PT VOLT FAIL AT RWKR
            new CauseDef {
                Seq=87, Cause="PT VOLT FAIL AT RWKR", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && NWKR DN && RWKR UP->DN && VPT 24DC Loc R>=LD2=90%avg && VPT RWKR<MinFail for 2s after RWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VPT 24DC Loc R", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="LD2=90%avg", EffectiveThreshold="avg*0.90", Gate="RWKR" },
                    new CauseAttr{ Name="VPT RWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="RWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & NWKR DN & RWKR UP->DN & VPT 24DC Loc R>=avg*0.90 & VPT RWKR<MinFail for 2s after RWKR DN => CONFIRMED"
            },

            // #88  PT NWKR RELAY DEFECT
            new CauseDef {
                Seq=88, Cause="PT NWKR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && RWKR DN && NWKR UP->DN && VPT NWKR>=MinSafe for 2s after NWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VPT NWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="NWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & RWKR DN & NWKR UP->DN & VPT NWKR>=MinSafe for 2s after NWKR DN => CONFIRMED"
            },

            // #89  PT RWKR RELAY DEFECT
            new CauseDef {
                Seq=89, Cause="PT RWKR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=2, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR DN && RWCR DN && NWKR DN && RWKR UP->DN && VPT RWKR>=MinSafe for 2s after RWKR DN",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="VPT RWKR", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="RWKR" },
                },
                VerdictLogic="NWCR DN & RWCR DN & NWKR DN & RWKR UP->DN & VPT RWKR>=MinSafe for 2s after RWKR DN => CONFIRMED"
            },

            // #90  PT N FAIL UNKNOWN OP
            new CauseDef {
                Seq=90, Cause="PT N FAIL UNKNOWN OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="NWCR UP(op<=12s) && NWKR DN up to 2s && none of operation-failure causes Sr7/Sr9/Sr11/Sr13 qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding point causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="NWCR UP(op<=12s) & NWKR DN<=2s & no operation-failure cause qualifies => CONFIRMED"
            },

            // #91  PT R FAIL UNKNOWN OP
            new CauseDef {
                Seq=91, Cause="PT R FAIL UNKNOWN OP", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS",
                RuleRaw="RWCR UP(op<=12s) && RWKR DN up to 2s && none of operation-failure causes Sr8/Sr10/Sr12/Sr14 qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="temporal", State="UP", ThresholdRef="entire UP duration or 12s max", EffectiveThreshold="TIMING: <=12s operation window", Gate="OP" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="DN", ThresholdRef="within/up to 2s after operation", EffectiveThreshold="TIMING: <=2s after operation", Gate="POST" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding point causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="RWCR UP(op<=12s) & RWKR DN<=2s & no operation-failure cause qualifies => CONFIRMED"
            },

            // ---- Point Failure (added from FRS: N/R FAIL UNKNOWN, indication side) ----
            // #129  PT N FAIL UNKNOWN
            new CauseDef {
                Seq=129, Cause="PT N FAIL UNKNOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="NWCR DN && RWCR DN && RWKR DN && NWKR UP->DN && none of point indication-failure causes Sr1/Sr3/Sr5 qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding point causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="NWCR DN & RWCR DN & RWKR DN & NWKR UP->DN & no indication-failure cause qualifies => CONFIRMED"
            },

            // #130  PT R FAIL UNKNOWN
            new CauseDef {
                Seq=130, Cause="PT R FAIL UNKNOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="NWCR DN && RWCR DN && NWKR DN && RWKR UP->DN && none of point indication-failure causes Sr2/Sr4/Sr6 qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="NWCR", Attid=282, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWCR", Attid=283, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="NWKR", Attid=267, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="RWKR", Attid=268, Datatype="DataLogger", Class="temporal", State="UP->DN", ThresholdRef="transition; evaluate 2s after DN", EffectiveThreshold="TIMING: transition + 2s", Gate="TRANS" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding point causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="NWCR DN & RWCR DN & NWKR DN & RWKR UP->DN & no indication-failure cause qualifies => CONFIRMED"
            },

            // ================================================================
            // IPS  (FRS 2.1)  --  supply monitors, NO relay. LD=90 -> MAX(avg*0.90,MinSafe).
            // NOTE on P1: IPS has no DataLogger relay, so IPS FAILURE is decided by the IPS
            // ANALOG crossing MinFail (there is no relay to gate it); still history-verified per
            // P0 (VIPS < MinFail must persist 10s). Single-attr except #93/#94 (charge/discharge
            // gated by IIPS Batt Char 110DC sign). deriv: none. [REVIEW] uniform vs FRS.
            // ================================================================

            // #92  IPS I/P VOLT LOW
            new CauseDef {
                Seq=92, Cause="IPS I/P VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS I/P < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS I/P", Attid=660, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS I/P < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #93  IPS 110 DC BATT VOLT LOW (Charging)
            new CauseDef {
                Seq=93, Cause="IPS 110 DC BATT VOLT LOW (Charging)", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="IIPS Batt Char 110DC>0+ && VIPS 110DC<(LD=90%avg OR MinSafe) for 15s while charging",
                Attrs=new [] {
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp=">", ThresholdRef="0+", EffectiveThreshold="0+" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="IIPS Batt Char 110DC>0+ & VIPS 110DC<MAX(avg*0.90, MinSafe) for 15s (charging) => CONFIRMED"
            },

            // #94  IPS 110 DC BATT VOLT LOW (Discharging)
            new CauseDef {
                Seq=94, Cause="IPS 110 DC BATT VOLT LOW (Discharging)", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="IIPS Batt Char 110DC<0 && VIPS 110DC<(LD=90%avg OR MinSafe) for 15s on load",
                Attrs=new [] {
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="0", EffectiveThreshold="0" },
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="IIPS Batt Char 110DC<0 & VIPS 110DC<MAX(avg*0.90, MinSafe) for 15s (discharging) => CONFIRMED"
            },

            // #95  IPS 110 AC Sig-1 VOLT LOW
            new CauseDef {
                Seq=95, Cause="IPS 110 AC Sig-1 VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS Sig-1 110AC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS Sig-1 110AC < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #96  IPS 110 AC TR-1 VOLT LOW
            new CauseDef {
                Seq=96, Cause="IPS 110 AC TR-1 VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS TR-1 110AC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS TR-1 110AC", Attid=621, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS TR-1 110AC < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #97  IPS SMR-1 VOLT LOW
            new CauseDef {
                Seq=97, Cause="IPS SMR-1 VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS SMR-1 110DC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS SMR-1 110DC", Attid=625, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS SMR-1 110DC < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #98  IPS DC-DC R INT VOLT LOW
            new CauseDef {
                Seq=98, Cause="IPS DC-DC R INT VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC R INT < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC R INT", Attid=647, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC R INT < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #99  IPS DC-DC R EXT VOLT LOW
            new CauseDef {
                Seq=99, Cause="IPS DC-DC R EXT VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC R EXT < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC R EXT < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #100  IPS DC-DC AXLE C VOLT LOW
            new CauseDef {
                Seq=100, Cause="IPS DC-DC AXLE C VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC AXLE C < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC AXLE C", Attid=649, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC AXLE C < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #101  IPS DC-DC PAN IND VOLT LOW
            new CauseDef {
                Seq=101, Cause="IPS DC-DC PAN IND VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC PAN IND < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC PAN IND", Attid=650, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC PAN IND < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #102  IPS DC-DC BLOCK LOCAL VOLT LOW
            new CauseDef {
                Seq=102, Cause="IPS DC-DC BLOCK LOCAL VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LOCAL < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LOCAL", Attid=651, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC BLOCK LOCAL < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #103  IPS DC-DC HKT MAG VOLT LOW
            new CauseDef {
                Seq=103, Cause="IPS DC-DC HKT MAG VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC HKT MAG < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC HKT MAG", Attid=652, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC HKT MAG < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #104  IPS DC-DC BLOCK LINE UP VOLT LOW
            new CauseDef {
                Seq=104, Cause="IPS DC-DC BLOCK LINE UP VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LINE UP < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LINE UP", Attid=653, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC BLOCK LINE UP < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #105  IPS DC-DC BLOCK LINE DN VOLT LOW
            new CauseDef {
                Seq=105, Cause="IPS DC-DC BLOCK LINE DN VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LINE DN < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LINE DN", Attid=654, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC BLOCK LINE DN < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #106  IPS DC-DC BLOCK TEL UP VOLT LOW
            new CauseDef {
                Seq=106, Cause="IPS DC-DC BLOCK TEL UP VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK TEL UP < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK TEL UP", Attid=655, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC BLOCK TEL UP < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #107  IPS DC-DC BLOCK TEL DN VOLT LOW
            new CauseDef {
                Seq=107, Cause="IPS DC-DC BLOCK TEL DN VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK TEL DN < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK TEL DN", Attid=656, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC BLOCK TEL DN < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #108  IPS DC-DC DATALOG VOLT LOW
            new CauseDef {
                Seq=108, Cause="IPS DC-DC DATALOG VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC DATALOG < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC DATALOG", Attid=657, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC DATALOG < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #109  IPS DC-DC EI VOLT LOW
            new CauseDef {
                Seq=109, Cause="IPS DC-DC EI VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC EI < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC EI", Attid=658, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="VIPS DC EI < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #110  IPS BATT CHAR CURR LOW
            new CauseDef {
                Seq=110, Cause="IPS BATT CHAR CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.9, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS",
                RuleRaw="IIPS Batt Char 110DC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" },
                },
                VerdictLogic="IIPS Batt Char 110DC < MAX(avg*0.90, MinSafe) for 15s => CONFIRMED"
            },

            // #111  IPS I/P VOLT FAIL
            new CauseDef {
                Seq=111, Cause="IPS I/P VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS I/P < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS I/P", Attid=660, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS I/P < MinFail for 10s => CONFIRMED"
            },

            // #112  IPS 110 DC VOLT FAIL
            new CauseDef {
                Seq=112, Cause="IPS 110 DC VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS 110DC < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS 110DC < MinFail for 10s => CONFIRMED"
            },

            // #113  IPS 110 AC Sig-1 VOLT FAIL
            new CauseDef {
                Seq=113, Cause="IPS 110 AC Sig-1 VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS Sig-1 110AC < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS Sig-1 110AC < MinFail for 10s => CONFIRMED"
            },

            // #114  IPS 110 AC TR-1 VOLT FAIL
            new CauseDef {
                Seq=114, Cause="IPS 110 AC TR-1 VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS TR-1 110AC < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS TR-1 110AC", Attid=621, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS TR-1 110AC < MinFail for 10s => CONFIRMED"
            },

            // #115  IPS SMR-1 VOLT FAIL
            new CauseDef {
                Seq=115, Cause="IPS SMR-1 VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS SMR-1 110DC < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS SMR-1 110DC", Attid=625, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS SMR-1 110DC < MinFail for 10s => CONFIRMED"
            },

            // #116  IPS DC-DC R INT VOLT FAIL
            new CauseDef {
                Seq=116, Cause="IPS DC-DC R INT VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC R INT < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC R INT", Attid=647, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC R INT < MinFail for 10s => CONFIRMED"
            },

            // #117  IPS DC-DC R EXT VOLT FAIL
            new CauseDef {
                Seq=117, Cause="IPS DC-DC R EXT VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC R EXT < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC R EXT < MinFail for 10s => CONFIRMED"
            },

            // #118  IPS DC-DC AXLE C VOLT FAIL
            new CauseDef {
                Seq=118, Cause="IPS DC-DC AXLE C VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC AXLE C < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC AXLE C", Attid=649, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC AXLE C < MinFail for 10s => CONFIRMED"
            },

            // #119  IPS DC-DC PAN IND VOLT FAIL
            new CauseDef {
                Seq=119, Cause="IPS DC-DC PAN IND VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC PAN IND < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC PAN IND", Attid=650, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC PAN IND < MinFail for 10s => CONFIRMED"
            },

            // #120  IPS DC-DC BLOCK LOCAL VOLT FAIL
            new CauseDef {
                Seq=120, Cause="IPS DC-DC BLOCK LOCAL VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LOCAL < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LOCAL", Attid=651, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC BLOCK LOCAL < MinFail for 10s => CONFIRMED"
            },

            // #121  IPS DC-DC HKT MAG VOLT FAIL
            new CauseDef {
                Seq=121, Cause="IPS DC-DC HKT MAG VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC HKT MAG < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC HKT MAG", Attid=652, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC HKT MAG < MinFail for 10s => CONFIRMED"
            },

            // #122  IPS DC-DC BLOCK LINE UP VOLT FAIL
            new CauseDef {
                Seq=122, Cause="IPS DC-DC BLOCK LINE UP VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LINE UP < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LINE UP", Attid=653, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC BLOCK LINE UP < MinFail for 10s => CONFIRMED"
            },

            // #123  IPS DC-DC BLOCK LINE DN VOLT FAIL
            new CauseDef {
                Seq=123, Cause="IPS DC-DC BLOCK LINE DN VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK LINE DN < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK LINE DN", Attid=654, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC BLOCK LINE DN < MinFail for 10s => CONFIRMED"
            },

            // #124  IPS DC-DC BLOCK TEL UP VOLT FAIL
            new CauseDef {
                Seq=124, Cause="IPS DC-DC BLOCK TEL UP VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK TEL UP < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK TEL UP", Attid=655, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC BLOCK TEL UP < MinFail for 10s => CONFIRMED"
            },

            // #125  IPS DC-DC BLOCK TEL DN VOLT FAIL
            new CauseDef {
                Seq=125, Cause="IPS DC-DC BLOCK TEL DN VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC BLOCK TEL DN < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC BLOCK TEL DN", Attid=656, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC BLOCK TEL DN < MinFail for 10s => CONFIRMED"
            },

            // #126  IPS DC-DC DATALOG VOLT FAIL
            new CauseDef {
                Seq=126, Cause="IPS DC-DC DATALOG VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC DATALOG < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC DATALOG", Attid=657, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC DATALOG < MinFail for 10s => CONFIRMED"
            },

            // #127  IPS DC-DC EI VOLT FAIL
            new CauseDef {
                Seq=127, Cause="IPS DC-DC EI VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="VIPS DC EI < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS DC EI", Attid=658, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="VIPS DC EI < MinFail for 10s => CONFIRMED"
            },

            // #128  IPS DC-DC BATT CHAR CURR FAIL
            new CauseDef {
                Seq=128, Cause="IPS DC-DC BATT CHAR CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS",
                RuleRaw="IIPS Batt Char 110DC < MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="IIPS Batt Char 110DC < MinFail for 10s => CONFIRMED"
            },

            // ================================================================
            // Shunt-Signal (FRS 2.7)  --  ON/OFF aspects. PILOT is a SEPARATE OR-branch in
            // PREDICTIVE (VSHSIG/ISHSIG/ISHSIG PILOT each < eff); FAILURE current is the SUM
            // (ISHSIG ON/OFF + ISHSIG PILOT) < MinFail. Shunt PILOT is DISTINCT from the ROSIG
            // PilotRoot(631). SH-HR / SH-ECRON / SH-ECROFF attids are runtime (Attid=0).
            // Failure: relays decide verdict; RDPMS V/I selects cause (P1).
            // ================================================================

            // [NOTE] ON: PILOT is a SEPARATE OR-branch here (predictive); shunt PILOT != ROSIG PilotRoot.
            // #131  SHSIG ON ASPECT VOLT/CURR LOW
            new CauseDef {
                Seq=131, Cause="SHSIG ON ASPECT VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR DN && SH-ECRON UP && VIPS Sig-1 110AC>=MinSafe && (VSHSIG ON<eff OR ISHSIG ON<eff OR ISHSIG PILOT<eff) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="SH-ECRON", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSHSIG ON", Attid=51, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="ONECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG ON", Attid=52, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="ONECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="ONECR / OR-G1" },
                },
                VerdictLogic="SH-HR DN & SH-ECRON UP & VIPS Sig-1 110AC>=MinSafe & (VSHSIG ON<eff OR ISHSIG ON<eff OR ISHSIG PILOT<eff) for 15s => CONFIRMED"
            },

            // #132  SHSIG ON ASPECT CURR HIGH
            new CauseDef {
                Seq=132, Cause="SHSIG ON ASPECT CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR DN && SH-ECRON UP && (ISHSIG ON>eff OR ISHSIG PILOT>eff) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="SH-ECRON", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISHSIG ON", Attid=52, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="ONECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="ONECR / OR-G1" },
                },
                VerdictLogic="SH-HR DN & SH-ECRON UP & (ISHSIG ON>eff OR ISHSIG PILOT>eff) for 15s => CONFIRMED"
            },

            // #133  SHSIG OFF ASPECT VOLT/CURR LOW
            new CauseDef {
                Seq=133, Cause="SHSIG OFF ASPECT VOLT/CURR LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF UP && VIPS Sig-1 110AC>=MinSafe && (VSHSIG OFF<eff OR ISHSIG OFF<eff OR ISHSIG PILOT<eff) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSHSIG OFF", Attid=53, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OFFECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG OFF", Attid=54, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OFFECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OFFECR / OR-G1" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF UP & VIPS Sig-1 110AC>=MinSafe & (VSHSIG OFF<eff OR ISHSIG OFF<eff OR ISHSIG PILOT<eff) for 15s => CONFIRMED"
            },

            // #134  SHSIG OFF ASPECT CURR HIGH
            new CauseDef {
                Seq=134, Cause="SHSIG OFF ASPECT CURR HIGH", AlertType="PREDICTIVE",
                LdPercent=1.2, LowerRule=false, SustainSec=15, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF UP && (ISHSIG OFF>eff OR ISHSIG PILOT>eff) for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="ISHSIG OFF", Attid=54, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="OFFECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">", ThresholdRef="HD=120%avg OR MaxSafe", EffectiveThreshold="MIN(avg*1.20, MaxSafe)", Gate="OFFECR / OR-G1" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF UP & (ISHSIG OFF>eff OR ISHSIG PILOT>eff) for 15s => CONFIRMED"
            },

            // #135  SHSIG OFF ASPECT HPR VOLT LOW
            new CauseDef {
                Seq=135, Cause="SHSIG OFF ASPECT HPR VOLT LOW", AlertType="PREDICTIVE",
                LdPercent=0.8, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF UP && VIPS DC R EXT>=MinSafe && VSHSIG HPR<eff for 15s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe" },
                    new CauseAttr{ Name="VSHSIG HPR", Attid=630, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="LD=80%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.80, MinSafe)", Gate="OFFECR" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF UP & VIPS DC R EXT>=MinSafe & VSHSIG HPR<eff for 15s => CONFIRMED"
            },

            // [NOTE] FAILURE current is a SUM: ISHSIG ON + ISHSIG PILOT (shunt-specific; distinct from ROSIG PilotRoot).
            // #136  SHSIG ON ASPECT VOLT/CURR FAIL
            new CauseDef {
                Seq=136, Cause="SHSIG ON ASPECT VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR DN && SH-ECRON DN && VIPS Sig-1 110AC>=MinFail && (VSHSIG ON<MinFail OR (ISHSIG ON+ISHSIG PILOT)<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="SH-ECRON", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSHSIG ON", Attid=51, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="ONECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG ON + ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="derived", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="(ISHSIG ON[52] + ISHSIG PILOT) < MinFail", Gate="OR-G1" },
                },
                VerdictLogic="SH-HR DN & SH-ECRON DN & VIPS Sig-1 110AC>=MinFail & (VSHSIG ON<MinFail OR (ISHSIG ON+ISHSIG PILOT)<MinFail) for 10s => CONFIRMED"
            },

            // #137  SHSIG ON ASPECT ECR RELAY DEFECT
            new CauseDef {
                Seq=137, Cause="SHSIG ON ASPECT ECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR DN && SH-ECRON DN && VSHSIG ON>=MinSafe && ISHSIG ON>=MinSafe && ISHSIG PILOT>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="SH-ECRON", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VSHSIG ON", Attid=51, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="ONECR" },
                    new CauseAttr{ Name="ISHSIG ON", Attid=52, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="ONECR" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="ONECR" },
                },
                VerdictLogic="SH-HR DN & SH-ECRON DN & VSHSIG ON>=MinSafe & ISHSIG ON>=MinSafe & ISHSIG PILOT>=MinSafe for 10s => CONFIRMED"
            },

            // #138  SHSIG ON ASPECT REASON UNKNOWN
            new CauseDef {
                Seq=138, Cause="SHSIG ON ASPECT REASON UNKNOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR DN && SH-ECRON DN && preceding Shunt-ON failure logics do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="SH-ECRON", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding shunt causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="SH-HR DN & SH-ECRON DN & preceding Shunt-ON failure logics do not qualify => CONFIRMED"
            },

            // #139  SHSIG OFF ASPECT VOLT/CURR FAIL
            new CauseDef {
                Seq=139, Cause="SHSIG OFF ASPECT VOLT/CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF DN && VSHSIG HPR>=MinSafe && VIPS Sig-1 110AC>=MinFail && (VSHSIG OFF<MinFail OR (ISHSIG OFF+ISHSIG PILOT)<MinFail) for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VSHSIG HPR", Attid=630, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="OFFECR" },
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSHSIG OFF", Attid=53, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OFFECR / OR-G1" },
                    new CauseAttr{ Name="ISHSIG OFF + ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="derived", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="(ISHSIG OFF[54] + ISHSIG PILOT) < MinFail", Gate="OR-G1" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF DN & VSHSIG HPR>=MinSafe & VIPS Sig-1 110AC>=MinFail & (VSHSIG OFF<MinFail OR (ISHSIG OFF+ISHSIG PILOT)<MinFail) for 10s => CONFIRMED"
            },

            // #140  SHSIG OFF ASPECT HPR VOLT FAIL
            new CauseDef {
                Seq=140, Cause="SHSIG OFF ASPECT HPR VOLT FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF DN && VIPS DC R EXT>=MinFail && VSHSIG HPR<MinFail for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VIPS DC R EXT", Attid=648, Datatype="IPS", Class="ips", Cmp=">=", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                    new CauseAttr{ Name="VSHSIG HPR", Attid=630, Datatype="RDPMS", Class="analog", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail", Gate="OFFECR" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF DN & VIPS DC R EXT>=MinFail & VSHSIG HPR<MinFail for 10s => CONFIRMED"
            },

            // #141  SHSIG OFF ASPECT ECR RELAY DEFECT
            new CauseDef {
                Seq=141, Cause="SHSIG OFF ASPECT ECR RELAY DEFECT", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=10, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF DN && VSHSIG HPR>=MinSafe && VSHSIG OFF>=MinSafe && ISHSIG OFF>=MinSafe && ISHSIG PILOT>=MinSafe for 10s",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="VSHSIG HPR", Attid=630, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="OFFECR" },
                    new CauseAttr{ Name="VSHSIG OFF", Attid=53, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="OFFECR" },
                    new CauseAttr{ Name="ISHSIG OFF", Attid=54, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="OFFECR" },
                    new CauseAttr{ Name="ISHSIG PILOT", Attid=0, Datatype="RDPMS", Class="analog", Cmp=">=", ThresholdRef="MinSafe", EffectiveThreshold="MinSafe", Gate="OFFECR" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF DN & VSHSIG HPR>=MinSafe & VSHSIG OFF>=MinSafe & ISHSIG OFF>=MinSafe & ISHSIG PILOT>=MinSafe for 10s => CONFIRMED"
            },

            // [NOTE] FRS key spelt 'UNKOWN'.
            // #142  SHSIG OFF ASPECT REASON UNKOWN
            new CauseDef {
                Seq=142, Cause="SHSIG OFF ASPECT REASON UNKOWN", AlertType="FAILURE",
                LdPercent=null, LowerRule=false, SustainSec=0, DerivedFormula=0, Source="FRS-MISSING",
                RuleRaw="SH-HR UP && SH-ECROFF DN && preceding Shunt-OFF failure logics do not qualify",
                Attrs=new [] {
                    new CauseAttr{ Name="SH-HR", Attid=0, Datatype="DataLogger", Class="relay", State="UP" },
                    new CauseAttr{ Name="SH-ECROFF", Attid=0, Datatype="DataLogger", Class="relay", State="DN" },
                    new CauseAttr{ Name="Prior-cause exclusion", Attid=0, Datatype="RDPMS", Class="derived", Cmp="==", ThresholdRef="preceding shunt causes NOT qualified", EffectiveThreshold="EXCLUSION" },
                },
                VerdictLogic="SH-HR UP & SH-ECROFF DN & preceding Shunt-OFF failure logics do not qualify => CONFIRMED"
            },

            // ================================================================
            // Legacy / code-only aliases -- map to their FRS equivalents.
            // ================================================================

            // [NOTE] LEGACY ALIAS of IPS 110 DC BATT VOLT LOW (use charge/discharge context #93/#94)
            // #143  IPS 110V DC LOW
            new CauseDef {
                Seq=143, Cause="IPS 110V DC LOW", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="CODE-ONLY",
                RuleRaw="LEGACY ALIAS of IPS 110 DC BATT VOLT LOW (use charge/discharge context #93/#94)",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="- (alias)", EffectiveThreshold="- (alias)" },
                },
                VerdictLogic="LEGACY ALIAS => CONFIRMED"
            },

            // [NOTE] LEGACY ALIAS: 110V AC bus low (no exact FRS cause)
            // #144  IPS 110V AC LOW
            new CauseDef {
                Seq=144, Cause="IPS 110V AC LOW", AlertType="PREDICTIVE",
                LdPercent=null, LowerRule=true, SustainSec=0, DerivedFormula=0, Source="CODE-ONLY",
                RuleRaw="LEGACY ALIAS: 110V AC bus low (no exact FRS cause)",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="- (alias)", EffectiveThreshold="- (alias)" },
                },
                VerdictLogic="LEGACY ALIAS => CONFIRMED"
            },

            // [NOTE] LEGACY ALIAS of IPS 110 DC VOLT FAIL (#112)
            // #145  IPS 110V DC FAIL
            new CauseDef {
                Seq=145, Cause="IPS 110V DC FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="CODE-ONLY",
                RuleRaw="LEGACY ALIAS of IPS 110 DC VOLT FAIL (#112)",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS 110DC", Attid=616, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="LEGACY ALIAS => CONFIRMED"
            },

            // [NOTE] LEGACY ALIAS: 110V AC bus fail (no exact FRS cause)
            // #146  IPS 110V AC FAIL
            new CauseDef {
                Seq=146, Cause="IPS 110V AC FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="CODE-ONLY",
                RuleRaw="LEGACY ALIAS: 110V AC bus fail (no exact FRS cause)",
                Attrs=new [] {
                    new CauseAttr{ Name="VIPS Sig-1 110AC", Attid=617, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="- (alias)", EffectiveThreshold="- (alias)" },
                },
                VerdictLogic="LEGACY ALIAS => CONFIRMED"
            },

            // [NOTE] CODE ALIAS of IPS DC-DC BATT CHAR CURR FAIL (#128)
            // #147  IPS BATT CHAR CURR FAIL
            new CauseDef {
                Seq=147, Cause="IPS BATT CHAR CURR FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="CODE-ONLY",
                RuleRaw="CODE ALIAS of IPS DC-DC BATT CHAR CURR FAIL (#128)",
                Attrs=new [] {
                    new CauseAttr{ Name="IIPS Batt Char 110DC", Attid=659, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" },
                },
                VerdictLogic="LEGACY ALIAS => CONFIRMED"
            },

            // ---- DB legacy rails (24V/60V): present in DB, not in the supplied FRS pages. Same IPS
            // ---- pattern as 110V; the specific rail attid is asset/DB-defined (runtime, Attid=0). ----
            // #148  IPS 24V DC LOW  (db#114)
            new CauseDef {
                Seq=148, Cause="IPS 24V DC LOW", AlertType="PREDICTIVE",
                LdPercent=0.90, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="DB",
                RuleRaw="VIPS 24DC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] { new CauseAttr{ Name="VIPS 24DC", Attid=0, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" } },
                VerdictLogic="VIPS 24DC < (LD=90%avg OR MinSafe) for 15s => CONFIRMED"
            },
            // #149  IPS 60V DC LOW  (db#115)
            new CauseDef {
                Seq=149, Cause="IPS 60V DC LOW", AlertType="PREDICTIVE",
                LdPercent=0.90, LowerRule=true, SustainSec=15, DerivedFormula=0, Source="DB",
                RuleRaw="VIPS 60DC < (LD=90%avg OR MinSafe) for 15s",
                Attrs=new [] { new CauseAttr{ Name="VIPS 60DC", Attid=0, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="LD=90%avg OR MinSafe", EffectiveThreshold="MAX(avg*0.90, MinSafe)" } },
                VerdictLogic="VIPS 60DC < (LD=90%avg OR MinSafe) for 15s => CONFIRMED"
            },
            // #150  IPS 24V DC FAIL  (db#134)
            new CauseDef {
                Seq=150, Cause="IPS 24V DC FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="DB",
                RuleRaw="VIPS 24DC < MinFail for 10s",
                Attrs=new [] { new CauseAttr{ Name="VIPS 24DC", Attid=0, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" } },
                VerdictLogic="VIPS 24DC < MinFail for 10s => CONFIRMED"
            },
            // #151  IPS 60V DC FAIL  (db#135)
            new CauseDef {
                Seq=151, Cause="IPS 60V DC FAIL", AlertType="FAILURE",
                LdPercent=null, LowerRule=true, SustainSec=10, DerivedFormula=0, Source="DB",
                RuleRaw="VIPS 60DC < MinFail for 10s",
                Attrs=new [] { new CauseAttr{ Name="VIPS 60DC", Attid=0, Datatype="IPS", Class="ips", Cmp="<", ThresholdRef="MinFail", EffectiveThreshold="MinFail" } },
                VerdictLogic="VIPS 60DC < MinFail for 10s => CONFIRMED"
            },
        };

        // Fast lookup by cause code (case-insensitive), for the verifier/prefetch/viz.
        public static readonly Dictionary<string, CauseDef> ByCause =
            All.ToDictionary(d => d.Cause, System.StringComparer.OrdinalIgnoreCase);
    }
}
