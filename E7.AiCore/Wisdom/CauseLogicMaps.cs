using Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace E7.AiCore
{
    // Moved from AiChatController.Wisdom.cs into E7.AiCore in 1.0.165.0 (namespace unchanged).
    // v1.0.160.19: cause-logic + derived-formula maps kept HERE (an already-compiled file) in their
    // OWN class CauseLogicMaps -> NO csproj Compile entry, and NOT a partial of RdpmsMaps, so
    // regenerating RdpmsMaps.cs (which emits a non-partial class) can never conflict. Call sites use
    // CauseLogicMaps.CauseToLogic / DerivedFormula / CauseToDerived.
    public static class CauseLogicMaps
    {
        // ------------------------------------------------------------------
        // CauseToLogic  -- the FRS alert-logic rule per cause code, extracted
        // verbatim from RDPMS_cause_code_attid_map.xlsx col "Alert Logic (FRS)"
        // (RDSO/SPN/257/2025 v2.0). This is the cause_code_def.Formula that srv2
        // analyse_alert used to supply -- embedded so the verdict no longer needs
        // srv2. Keyed by the SAME master cause names as CauseToAttributes, so a
        // direct TryGetValue(causeCode) hits. Thresholds (Min-safe/Min-fail/%avg)
        // resolve at runtime from get_attribute_range + PmRangeAttidFor + HealthBands.
        // ------------------------------------------------------------------
        public static readonly Dictionary<string, string> CauseToLogic =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Point Machine ----
            { "PT N TIME HIGH", "NWCR UP(op<=12s); NWKR UP<=2s; TPT N > 150%avg (op-time high)" },
            { "PT R TIME HIGH", "RWCR UP(op<=12s); RWKR UP<=2s; TPT R > 150%avg (op-time high)" },
            { "PT N VOLT/CURR LOW", "NWCR UP(op<=12s) && IIPS Batt Char>0+ && VIPS 110DC>=Min-safe && (VPT 110DC Loc N<80%avg OR IPT N<80%avg); NWKR UP<=2s" },
            { "PT R VOLT/CURR LOW", "RWCR UP(op<=12s) && IIPS Batt Char>0+ && VIPS 110DC>=Min-safe && (VPT 110DC Loc R<80%avg OR IPT R<80%avg); RWKR UP<=2s" },
            { "PT N IND VOLT LOW AT LOC", "NWKR UP && VIPS DC R EXT>=Min-safe && VPT 24DC Loc N < 80%avg for 15s" },
            { "PT R IND VOLT LOW AT LOC", "RWKR UP && VIPS DC R EXT>=Min-safe && VPT 24DC Loc R < 80%avg for 15s" },
            { "PT VOLT LOW AT NWKR", "NWKR UP && VPT 24DC Loc N>= && VPT NWKR < 90%avg for 15s (cable)" },
            { "PT VOLT LOW AT RWKR", "RWKR UP && VPT 24DC Loc R>= && VPT RWKR < 90%avg for 15s (cable)" },
            { "PT NWKR RELAY DEFECT", "NWCR DN && RWCR DN && RWKR DN && NWKR UP>DN && VPT NWKR>=Min-safe for 2s" },
            { "PT N VOLT/CURR FAIL", "NWCR UP(op<=12s) && VIPS 110DC>=Min-fail && (VPT 110DC Loc N<Min-fail OR IPT N<Min-fail); NWKR DN<=2s" },
            { "PT N OBS", "NWCR UP(op<=12s) && VPT 110DC Loc N>=Min-safe && IPT N>=Min-safe; NWKR DN; TPT N>Max-safe (obstruction)" },
            { "PT RWKR RELAY DEFECT", "NWCR DN && RWCR DN && NWKR DN && RWKR UP>DN && VPT RWKR>=Min-safe for 2s" },
            { "PT R VOLT/CURR FAIL", "RWCR UP(op<=12s) && VIPS 110DC>=Min-fail && (VPT 110DC Loc R<Min-fail OR IPT R<Min-fail); RWKR DN<=2s" },
            { "PT R OBS", "RWCR UP(op<=12s) && VPT 110DC Loc R>=Min-safe && IPT R>=Min-safe; RWKR DN; TPT R>Max-safe (obstruction)" },
            { "PT N IND VOLT FAIL AT LOC", "NWCR DN && RWCR DN && RWKR DN && NWKR UP>DN && VIPS DC R EXT>=Min-fail && VPT 24DC Loc N<70%avg for 2s" },
            { "PT R IND VOLT FAIL AT LOC", "NWCR DN && RWCR DN && NWKR DN && RWKR UP>DN && VIPS DC R EXT>=Min-fail && VPT 24DC Loc R<70%avg for 2s" },
            { "PT VOLT FAIL AT NWKR", "NWCR DN && RWCR DN && RWKR DN && NWKR UP>DN && VPT 24DC Loc N>= && VPT NWKR<Min-fail for 2s" },
            { "PT VOLT FAIL AT RWKR", "NWCR DN && RWCR DN && NWKR DN && RWKR UP>DN && VPT 24DC Loc R>= && VPT RWKR<Min-fail for 2s" },
            { "PT N FAIL UNKNOWN", "NWCR DN && RWCR DN && RWKR DN && NWKR UP>DN; none of ind-fail logics qualify" },
            { "PT R FAIL UNKNOWN", "NWCR DN && RWCR DN && NWKR DN && RWKR UP>DN; none of ind-fail logics qualify" },
            { "PT VOLT FAIL AT NWKR OP", "NWCR UP(op<=12s) && VPT 110DC Loc N>=Min-safe && IPT N>=Min-safe; [NWKR DN && VPT 24DC Loc N>= && VPT NWKR<Min-fail] for 2s" },
            { "PT VOLT FAIL AT RWKR OP", "RWCR UP(op<=12s) && VPT 110DC Loc R>=Min-safe && IPT R>=Min-safe; [RWKR DN && VPT 24DC Loc R>= && VPT RWKR<Min-fail] for 2s" },
            { "PT NWKR RELAY DEFECT OP", "NWCR UP(op<=12s) && VPT 110DC Loc N>=Min-safe && IPT N>=Min-safe; [NWKR DN && VPT NWKR>=Min-safe] for 2s" },
            { "PT RWKR RELAY DEFECT OP", "RWCR UP(op<=12s) && VPT 110DC Loc R>=Min-safe && IPT R>=Min-safe; [RWKR DN && VPT RWKR>=Min-safe] for 2s" },
            { "PT N FAIL UNKNOWN OP", "NWCR UP(op<=12s); NWKR DN; none of op-fail logics (sr7/9/11/13) qualify" },
            { "PT R FAIL UNKNOWN OP", "RWCR UP(op<=12s); RWKR DN; none of op-fail logics (sr8/10/12/14) qualify" },
            // ---- Track Circuit ----
            { "TC TFC I/P VOLT LOW", "TPR UP && VIPS TR-1 110AC>=Min-safe && VTC TFC I/P < 80%avg OR Min-safe for 15min" },
            { "TC BT CHG CURR HIGH", "TPR UP && ITC Batt Charg > Max-safe for 15s" },
            { "TC RAIL RES HIGH", "TPR UP && RRail > 120%avg for 15s" },
            { "TC TR OVER ENERIZATION", "TPR UP && ITC Relay End > (over-energized) for 15s" },
            { "TC TR CONTACT RES HIGH", "TPR UP && VIPS DC R EXT>=Min-safe && VTC 24DC Loc < for 15s" },
            { "TC TPR I/P VOLT LOW", "TPR UP && VTC 24DC Loc>= && VTC 24DC TPR I/P < for 15s (cable)" },
            { "TC TFC O/P VOLT LOW (Charging)", "TPR UP && ITC Batt Charg>0+ && VIPS TR-1 110AC>=Min-safe && VTC TFC O/P < 80%avg(charging) for 15s" },
            { "TC TFC O/P VOLT LOW (Discharging)", "TPR UP && ITC Batt Charg<0 && VIPS TR-1 110AC>=Min-safe && VTC TFC O/P < 80%avg(on-load) for 15s" },
            { "TC BT CHG CURR LOW", "TPR UP && ITC Batt Charg < Min-safe for 15s" },
            { "TC CH RES LOW", "TPR UP && RTC CH Feed End < (low/short) for 15s" },
            { "TC CH RES HIGH", "TPR UP && RTC CH Feed End > (high) for 15s" },
            { "TC VAR RES LOW", "TPR UP && RTC Var Res < (low/short) for 15s" },
            { "TC VAR RES HIGH", "TPR UP && RTC Var Res > (high) for 15s" },
            { "TC BALST/SLPR RES LOW", "TPR UP && ITC Relay End< && IBalst> && dIBalst>dITC Relay End for 15s" },
            { "TC TR VOLT LOW", "TPR UP && ITC Relay End< (under-energized; if RailRes/Balst logics fail) for 15s" },
            { "TC GJ SHORT", "T1[TPR DN && ITC Relay End<Min-fail & >0+ && IBalst> && dIBalst>dI] && T2[TPR UP && dITC Relay End>10%] (glued-joint)" },
            { "TC SHORT", "TPR DN && ITC Relay End<Min-fail & >0+ && IBalst> && dIBalst>dI for 10s" },
            { "TC VAR RES OPEN", "TPR DN && ITC Relay End<Min-fail && ITC Feed End>0+ && RTC Var Res > 2xavg for 10s" },
            { "TC RAIL RES OPEN", "TPR DN && ITC Relay End<Min-fail & >0+ && ITC Feed End>0+ && RRail > 2xavg for 10s" },
            { "TC TR RELAY DEFECT", "TPR DN && ITC Relay End>=Min-safe && VIPS DC R EXT>=Min-fail && VTC 24DC Loc < for 10s" },
            { "TC TR UP TPR DN", "TPR DN && ITC Relay End>=Min-safe && VTC 24DC Loc>= && VTC 24DC TPR I/P<Min-fail for 10s" },
            { "TC TPR RELAY DEFECT", "TPR DN && ITC Relay End>=Min-safe && VTC 24DC TPR I/P>=Min-safe for 10s" },
            { "REASON UNKNOWN", "TPR DN; none of above track-fail logics qualify" },
            { "TC CH RES OPEN", "TPR DN && ITC Relay End<Min-fail && ITC Feed End>0+ && RTC CH Feed End > 2xavg for 10s" },
            { "TC TFC O/P VOLT FAIL", "TPR DN && ITC Relay End<Min-fail && ITC Feed End>0+ && VTC TFC O/P<Min-fail for 10s" },
            { "TC CKT OPEN", "TPR DN && (ITC Relay End<0+ OR ITC Feed End<0+) for 10s" },
            // ---- Signal ----
            { "SIG RG VOLT/CURR LOW", "RECR UP && VIPS Sig-1 110AC>=Min-safe && (VSIG RG<80%avg OR ISIG RG<80%avg) for 15s" },
            { "SIG HG VOLT/CURR LOW", "HECR UP && VIPS Sig-1 110AC>=Min-safe && (VSIG HG<80%avg OR ISIG HG<80%avg) for 15s" },
            { "SIG HHG VOLT/CURR LOW", "HHECR UP && VIPS Sig-1 110AC>=Min-safe && (VSIG HHG<80%avg OR ISIG HHG<80%avg) for 15s" },
            { "SIG DG VOLT/CURR LOW", "DECR UP && VIPS Sig-1 110AC>=Min-safe && (VSIG DG<80%avg OR ISIG DG<80%avg) for 15s" },
            { "SIG RG CURR HIGH", "RECR UP && ISIG RG > 120%avg for 15s" },
            { "SIG HG CURR HIGH", "HECR UP && ISIG HG > 120%avg for 15s" },
            { "SIG HHG CURR HIGH", "HHECR UP && ISIG HHG > 120%avg for 15s" },
            { "SIG DG CURR HIGH", "DECR UP && ISIG DG > 120%avg for 15s" },
            { "SIG HPR VOLT LOW", "HECR UP && VIPS DC R EXT>=Min-safe && VSIG HPR < 80%avg for 15s" },
            { "SIG HHPR VOLT LOW", "HHECR UP && VIPS DC R EXT>=Min-safe && VSIG HHPR < 80%avg for 15s" },
            { "SIG DPR VOLT LOW", "DECR UP && VIPS DC R EXT>=Min-safe && VSIG DPR < 80%avg for 15s" },
            { "SHSIG ON ASPECT VOLT/CURR LOW", "SH-HR DN && SH-ECRON UP && VIPS Sig-1 110AC>=Min-safe && (VSHSIG ON<80%avg OR ISHSIG ON<80%avg OR ISHSIG PILOT<80%avg) for 15s" },
            { "SHSIG ON ASPECT CURR HIGH", "SH-HR DN && SH-ECRON UP && (ISHSIG ON>120%avg OR ISHSIG PILOT>120%avg) for 15s" },
            { "SHSIG OFF ASPECT VOLT/CURR LOW", "SH-HR UP && SH-ECROFF UP && VIPS Sig-1 110AC>=Min-safe && (VSHSIG OFF<80%avg OR ISHSIG OFF<80%avg OR ISHSIG PILOT<80%avg) for 15s" },
            { "SHSIG OFF ASPECT CURR HIGH", "SH-HR UP && SH-ECROFF UP && (ISHSIG OFF>120%avg OR ISHSIG PILOT>120%avg) for 15s" },
            { "SHSIG OFF ASPECT HPR VOLT LOW", "SH-HR UP && SH-ECROFF UP && VIPS DC R EXT>=Min-safe && VSHSIG HPR < 80%avg for 15s" },
            { "COSIG ASPECT VOLT/CURR LOW", "CO-HECR UP && VIPS Sig-1 110AC>=Min-safe && (VCOSIG<80%avg OR ICOSIG<80%avg) for 15s" },
            { "COSIG ASPECT CURR HIGH", "CO-HECR UP && ICOSIG > 120%avg for 15s" },
            { "COSIG HPR VOLT LOW", "CO-HECR UP && VIPS DC R EXT>=Min-safe && VCOSIG HPR < 80%avg for 15s" },
            { "ROSIG ASPECT VOLT/CURR LOW", "UECR UP && VIPS Sig-1 110AC>=Min-safe && (VROSIG<80%avg OR IROSIG<80%avg) for 15s" },
            { "ROSIG ASPECT CURR HIGH", "UECR UP && IROSIG > 120%avg for 15s" },
            { "ROSIG HPR VOLT LOW", "UECR UP && VIPS DC R EXT>=Min-safe && VROSIG HPR < 80%avg for 15s" },
            { "SIG RG VOLT/CURR FAIL", "HR DN && RECR DN && VIPS Sig-1 110AC>=Min-fail && (VSIG RG<Min-fail OR ISIG RG<Min-fail) for 10s" },
            { "SIG RG RECR RELAY DEFECT", "HR DN && RECR DN && VSIG RG>=Min-safe && ISIG RG>=Min-safe for 10s" },
            { "SIG RG UNKOWN", "HR DN && RECR DN; sr1/2 do not qualify" },
            { "SIG HG VOLT/CURR FAIL", "HR UP && HHR DN && DR DN && HECR DN && RECR UP && VSIG HPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VSIG HG<Min-fail OR ISIG HG<Min-fail) for 10s" },
            { "SIG HG HPR VOLT FAIL", "HR UP && HHR DN && DR DN && HECR DN && RECR UP && VIPS DC R EXT>=Min-fail && VSIG HPR<Min-fail for 10s" },
            { "SIG HG HECR RELAY DEFECT", "HR UP && HHR DN && DR DN && HECR DN && RECR UP && VSIG HPR/HG>=Min-safe && ISIG HG>=Min-safe for 10s" },
            { "SIG HG UNKOWN", "HR UP && HHR DN && DR DN && HECR DN && RECR UP; sr4/5/6 do not qualify" },
            { "SIG HHG VOLT/CURR FAIL", "HR UP && HHR UP && DR DN && (HECR DN|HHECR DN) && (RECR|HECR|HHECR UP) && VSIG HPR&HHPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VSIG HG/HHG<Min-fail OR ISIG HG/HHG<Min-fail) for 10s" },
            { "SIG HHG HHPR VOLT FAIL", "HR UP && HHR UP && DR DN && (HECR|HHECR DN) && (RECR|HECR|HHECR UP) && VIPS DC R EXT>=Min-fail && (VSIG HPR<Min-fail OR VSIG HHPR<Min-fail) for 10s" },
            { "SIG HHG HHECR RELAY DEFECT", "HR UP && HHR UP && DR DN && (HECR|HHECR DN) && (RECR|HECR|HHECR UP) && HPR/HHPR & HG/HHG V,I >=Min-safe for 10s" },
            { "SIG HHG UNKOWN", "HR UP && HHR UP && DR DN && (HECR|HHECR DN) && (RECR|HECR|HHECR UP); sr8/9/10 do not qualify" },
            { "SIG DG VOLT/CURR FAIL", "HR UP && HHR UP && DR UP && DECR DN && (RECR|HECR|HHECR UP) && VSIG DPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VSIG DG<Min-fail OR ISIG DG<Min-fail) for 10s" },
            { "SIG DG DPR VOLT FAIL", "HR UP && HHR UP && DR UP && DECR DN && (RECR|HECR|HHECR UP) && VIPS DC R EXT>=Min-fail && VSIG DPR<Min-fail for 10s" },
            { "SIG DG DECR RELAY DEFECT", "HR UP && HHR UP && DR UP && DECR DN && (RECR|HECR|HHECR UP) && VSIG DPR/DG>=Min-safe && ISIG DG>=Min-safe for 10s" },
            { "SIG DG UNKOWN", "HR UP && HHR UP && DR UP && DECR DN && (RECR|HECR|HHECR UP); sr12/13/14 do not qualify" },
            { "SIG BLANK", "HR UP && RECR DN && HECR DN && HHECR DN && DECR DN for 10s" },
            { "SHSIG ON ASPECT VOLT/CURR FAIL", "SH-HR DN && SH-ECRON DN && VIPS Sig-1 110AC>=Min-fail && (VSHSIG ON<Min-fail OR (ISHSIG ON+PILOT)<Min-fail) for 10s" },
            { "SHSIG ON ASPECT ECR RELAY DEFECT", "SH-HR DN && SH-ECRON DN && VSHSIG ON>=Min-safe && ISHSIG ON>=Min-safe && ISHSIG PILOT>=Min-safe for 10s" },
            { "SHSIG ON ASPECT REASON UNKNOWN", "SH-HR DN && SH-ECRON DN; none qualify" },
            { "SHSIG OFF ASPECT VOLT/CURR FAIL", "SH-HR UP && SH-ECROFF DN && VSHSIG HPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VSHSIG OFF<Min-fail OR (ISHSIG OFF+PILOT)<Min-fail) for 10s" },
            { "SHSIG OFF ASPECT HPR VOLT FAIL", "SH-HR UP && SH-ECROFF DN && VIPS DC R EXT>=Min-fail && VSHSIG HPR<Min-fail for 10s" },
            { "SHSIG OFF ASPECT ECR RELAY DEFECT", "SH-HR UP && SH-ECROFF DN && VSHSIG HPR/OFF & ISHSIG OFF/PILOT>=Min-safe for 10s" },
            { "SHSIG OFF ASPECT REASON UNKOWN", "SH-HR UP && SH-ECROFF DN; none qualify" },
            { "COSIG ASPECT VOLT/CURR FAIL", "CO-HR UP && CO-HECR DN && VCOSIG HPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VCOSIG<Min-fail OR ICOSIG<Min-fail) for 10s" },
            { "COSIG HPR VOLT FAIL", "CO-HR UP && CO-HECR DN && VIPS DC R EXT>=Min-fail && VCOSIG HPR<Min-fail for 10s" },
            { "COSIG HECR RELAY DEFECT", "CO-HR UP && CO-HECR DN && VCOSIG HPR/VCOSIG/ICOSIG>=Min-safe for 10s" },
            { "COSIG REASON UNKOWN", "CO-HR UP && CO-HECR DN; none qualify" },
            { "ROSIG ASPECT VOLT/CURR FAIL", "UHR UP && UECR DN && VROSIG HPR>=Min-safe && VIPS Sig-1 110AC>=Min-fail && (VROSIG<Min-fail OR IROSIG<Min-fail) for 10s" },
            { "ROSIG HPR VOLT FAIL", "UHR UP && UECR DN && VIPS DC R EXT>=Min-fail && VROSIG HPR<Min-fail for 10s" },
            { "ROSIG UECR RELAY DEFECT", "UHR UP && UECR DN && VROSIG HPR/VROSIG/IROSIG>=Min-safe for 10s" },
            { "ROSIG REASON UNKOWN", "UHR UP && UECR DN; none qualify" },
            // ---- IPS ----
            { "IPS 110V DC LOW", "(legacy) VIPS 110DC low" },
            { "IPS 110V AC LOW", "(legacy) 110V AC bus low" },
            { "IPS I/P VOLT LOW", "VIPS I/P < 90%avg OR Min-safe for 15s" },
            { "IPS 110 DC BATT VOLT LOW (Charging)", "IIPS Batt Char 110DC > 0+ && VIPS 110DC < 90%avg(charging) OR Min-safe for 15s" },
            { "IPS 110 DC BATT VOLT LOW (Discharging)", "IIPS Batt Char 110DC < 0 && VIPS 110DC < 90%avg(on-load) OR Min-safe for 15s" },
            { "IPS 110 AC Sig-1 VOLT LOW", "VIPS Sig-1 110AC < 90%avg OR Min-safe for 15s" },
            { "IPS 110 AC TR-1 VOLT LOW", "VIPS TR-1 110AC < 90%avg OR Min-safe for 15s" },
            { "IPS SMR-1 VOLT LOW", "VIPS SMR-1 110DC < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC R INT VOLT LOW", "VIPS DC R INT < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC R EXT VOLT LOW", "VIPS DC R EXT < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC AXLE C VOLT LOW", "VIPS DC AXLE C < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC PAN IND VOLT LOW", "VIPS DC PAN IND < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC BLOCK LOCAL VOLT LOW", "VIPS DC BLOCK LOCAL < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC HKT MAG VOLT LOW", "VIPS DC HKT MAG < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC BLOCK LINE UP VOLT LOW", "VIPS DC BLOCK LINE UP < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC BLOCK LINE DN VOLT LOW", "VIPS DC BLOCK LINE DN < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC BLOCK TEL UP VOLT LOW", "VIPS DC BLOCK TEL UP < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC BLOCK TEL DN VOLT LOW", "VIPS DC BLOCK TEL DN < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC DATALOG VOLT LOW", "VIPS DC DATALOG < 90%avg OR Min-safe for 15s" },
            { "IPS DC-DC EI VOLT LOW", "VIPS DC EI < 90%avg OR Min-safe for 15s" },
            { "IPS BATT CHAR CURR LOW", "IIPS Batt Char 110DC < 90%avg OR Min-safe for 15s" },
            { "IPS 110V DC FAIL", "(legacy) VIPS 110DC fail" },
            { "IPS 110V AC FAIL", "(legacy) 110V AC bus fail" },
            { "IPS I/P VOLT FAIL", "VIPS I/P < Min-fail for 10s" },
            { "IPS 110 DC VOLT FAIL", "VIPS 110DC < Min-fail for 10s" },
            { "IPS 110 AC Sig-1 VOLT FAIL", "VIPS Sig-1 110AC < Min-fail for 10s" },
            { "IPS 110 AC TR-1 VOLT FAIL", "VIPS TR-1 110AC < Min-fail for 10s" },
            { "IPS SMR-1 VOLT FAIL", "VIPS SMR-1 110DC < Min-fail for 10s" },
            { "IPS DC-DC R INT VOLT FAIL", "VIPS DC R INT < Min-fail for 10s" },
            { "IPS DC-DC R EXT VOLT FAIL", "VIPS DC R EXT < Min-fail for 10s" },
            { "IPS DC-DC AXLE C VOLT FAIL", "VIPS DC AXLE C < Min-fail for 10s" },
            { "IPS DC-DC PAN IND VOLT FAIL", "VIPS DC PAN IND < Min-fail for 10s" },
            { "IPS DC-DC BLOCK LOCAL VOLT FAIL", "VIPS DC BLOCK LOCAL < Min-fail for 10s" },
            { "IPS DC-DC HKT MAG VOLT FAIL", "VIPS DC HKT MAG < Min-fail for 10s" },
            { "IPS DC-DC BLOCK LINE UP VOLT FAIL", "VIPS DC BLOCK LINE UP < Min-fail for 10s" },
            { "IPS DC-DC BLOCK LINE DN VOLT FAIL", "VIPS DC BLOCK LINE DN < Min-fail for 10s" },
            { "IPS DC-DC BLOCK TEL UP VOLT FAIL", "VIPS DC BLOCK TEL UP < Min-fail for 10s" },
            { "IPS DC-DC BLOCK TEL DN VOLT FAIL", "VIPS DC BLOCK TEL DN < Min-fail for 10s" },
            { "IPS DC-DC DATALOG VOLT FAIL", "VIPS DC DATALOG < Min-fail for 10s" },
            { "IPS DC-DC EI VOLT FAIL", "VIPS DC EI < Min-fail for 10s" },
            { "IPS BATT CHAR CURR FAIL", "IIPS Batt Char 110DC < Min-fail for 10s" },
            { "IPS DC-DC BATT CHAR CURR FAIL", "IIPS Batt Char 110DC < Min-fail for 10s" },
        };


        // ------------------------------------------------------------------
        // DerivedFormula  -- how each DERIVED attribute (resistance/difference) is
        // computed from the base TPR-gated sensors. Derived attributes have NO sensor
        // of their own; injected for a derived cause so the value can be computed from
        // the component readings already on srv1. Source: RDPMS derived-compute spec.
        // Base: If(1) Ir(2) Vf(249) Vr(3) Choke(4) ChgMa(5) ChgOpV(569).
        // ------------------------------------------------------------------
        public static readonly Dictionary<int, string> DerivedFormula =
            new Dictionary<int, string>
        {
            { 585, "ITC BATT CHARG = ChgMa - If" },
            { 586, "VTC VAR RES = ChgOpV - Vf - Choke" },
            { 587, "RTC CH FEED END = (Choke / If) * 1000    [If != 0]" },
            { 588, "RTC VAR RES = (VTC_VAR_RES(586) / If) * 1000    [If != 0]   where VTC_VAR_RES = ChgOpV - Vf - Choke" },
            { 589, "RTC CH RELAY END = (VTC_CH_RELAY_END(591) / Ir) * 1000    [Ir != 0]   where VTC_CH_RELAY_END = Vr - TR_V571; TR_V571 = Ir * R737 / 1000, where R737 = attid 737 (Track Relay res) from the FRS range for this AssetId (in the FRS RANGE evidence above); NEVER the measured 571 sensor" },
            { 590, "RRAIL = 2 * (Vf - TR_V571) / (If + Ir) * 1000    [If+Ir != 0]; TR_V571 = Ir * R737 / 1000, where R737 = attid 737 (Track Relay res) from the FRS range for this AssetId (in the FRS RANGE evidence above); NEVER the measured 571 sensor" },
            { 591, "VTC CH RELAY END = Vr - TR_V571; TR_V571 = Ir * R737 / 1000, where R737 = attid 737 (Track Relay res) from the FRS range for this AssetId (in the FRS RANGE evidence above); NEVER the measured 571 sensor" },
            { 684, "IBALST = If - Ir" },
        };

        // CauseToDerived -- cause code -> the derived attid its FRS rule tests, for the
        // 15 TRACK causes that depend on a derived attribute. Key = exact master name.
        public static readonly Dictionary<string, int> CauseToDerived =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "TC BT CHG CURR HIGH", 585 },
            { "TC BT CHG CURR LOW", 585 },
            { "TC TFC O/P VOLT LOW (Charging)", 585 },
            { "TC TFC O/P VOLT LOW (Discharging)", 585 },
            { "TC CH RES LOW", 587 },
            { "TC CH RES HIGH", 587 },
            { "TC CH RES OPEN", 587 },
            { "TC VAR RES LOW", 588 },
            { "TC VAR RES HIGH", 588 },
            { "TC VAR RES OPEN", 588 },
            { "TC BALST/SLPR RES LOW", 684 },
            { "TC GJ SHORT", 684 },
            { "TC SHORT", 684 },
            { "TC RAIL RES HIGH", 590 },
            { "TC RAIL RES OPEN", 590 },
        };

        // v1.0.160.21: PER-END RDPMS attids for the 14 PM NWKR/RWKR causes. The generic name
        // resolution (VPT NWKR -> rollup 890) misses the real per-end sensor; anchor the grounding
        // tag on the ALERTING attribute's PER-END attid instead. Attid-based on purpose -- the
        // attribute NAMES have spacing inconsistencies (VPT110 vs VPT 110) that break name lookups.
        // Order = alerting attribute first (A-end, B-end), then the healthy-gate attribute. The OP
        // variants share the SAME indication attids as their non-OP twin (their operation V/I is a
        // separate EdgeX-op namespace, not fetched here). VIPS DC R EXT (648, single) stays on the
        // name path. Verified against RDPMS_Attributes (RDSO/SPN/257/2025).
        public static readonly Dictionary<string, int[]> CauseToAttributesPerEnd =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "PT VOLT LOW AT NWKR",       new int[] { 25, 27, 576, 578 } },
            { "PT VOLT LOW AT RWKR",       new int[] { 26, 28, 577, 579 } },
            { "PT VOLT FAIL AT NWKR",      new int[] { 25, 27, 576, 578 } },
            { "PT VOLT FAIL AT RWKR",      new int[] { 26, 28, 577, 579 } },
            { "PT NWKR RELAY DEFECT",      new int[] { 25, 27 } },
            { "PT RWKR RELAY DEFECT",      new int[] { 26, 28 } },
            { "PT N IND VOLT LOW AT LOC",  new int[] { 576, 578 } },
            { "PT R IND VOLT LOW AT LOC",  new int[] { 577, 579 } },
            { "PT N IND VOLT FAIL AT LOC", new int[] { 576, 578 } },
            { "PT R IND VOLT FAIL AT LOC", new int[] { 577, 579 } },
            { "PT VOLT FAIL AT NWKR OP",   new int[] { 25, 27, 576, 578 } },
            { "PT VOLT FAIL AT RWKR OP",   new int[] { 26, 28, 577, 579 } },
            { "PT NWKR RELAY DEFECT OP",   new int[] { 25, 27 } },
            { "PT RWKR RELAY DEFECT OP",   new int[] { 26, 28 } },
        };
    }
}
