/* =============================================================================
   telemetrylive-drawer.js  --  v618.38  ASSET DRAWER (Track first)

   One AI button on each Track card (replaces Graph / Circuit / Avg values)
   opens this drawer from the right -- the "Drawer final" mockup:

     header      asset, health score, TPR, live time, prev / next asset, close
     nav         SPA-style section jump: Live values, Ask AI, Alerts, Circuit,
                 Track shorting (/ Pattern analysis), Maintenance roster, Graph;
                 plus an Export CSV icon button.
                 (v618.38: replaced Open in SIP / Full circuit view / Avg values)
     row 1       Live values (safe-range gauges + calculated values)
                 | Ask AI (POST /FRS25/ChatBot/Chat, SSE text / tool_use / error frames; v618.37)
                   -- Analysis (Asset Health API) now shows here as the default view
                      and the conversation replaces it once you ask (v618.38)
     circuit     track circuit, live values on cards, animated current flow
     alerts      past alerts, 30 days (POST /FRS25/Telemetry/GetSipReplayAlerts),
                 filter chips, Replay -> SIP replay around the alert
     shorting    track shorting / leakage report (v618.36, port of E7MRIWeb TrackLeakage):
                 status, reasoning + AI Analysis, major events, If / Ir Plotly charts
     graph       every reading, own scale, step lines, shared time axis
                 (GET /FRS25/TelemetryHistory/GetDashboardHistoryData)

   Data for the live parts comes from TlHealthView.describe(aid) (same rows,
   ranges and health as the Health view); refreshed every second while open.
   ============================================================================= */
(function () {
    'use strict';
    var W = window;
    var D = { aid: null, timer: null, liveSig: '', chat: {}, alerts: null, alertFilter: 'all', graphHours: 24, graph: {}, busy: {}, health: {}, roster: {}, short: {}, shortDays: 7 };
    var HEALTH_URL = '/FRS25/MaintenceRoster/AssetHealth';
    var ROSTER_SHEET_URL = '/FRS25/MaintenceRoster/GetWorksheet';          /* v618.35 maintenance roster */
    var ROSTER_STATE_URL = '/FRS25/MaintenceRoster/GetMaintenanceState';
    var ROSTER_PAGE_URL = '/FRS25/MaintenceRoster/Index';   /* v618.34: Asset Health API (Roster 2.11.0.0 / E7.AiCore AssetHealthEngine) */
    var CHAT_URL = '/FRS25/ChatBot/Chat';   /* v618.37: E7MRIWeb ChatBot endpoint (E7.AiCore ChatEngine), was /FRS25/AiChat/Chat */
    var ALERTS_URL = '/FRS25/Telemetry/GetSipReplayAlerts';
    var HIST_URL = '/FRS25/TelemetryHistory/GetDashboardHistoryData';
    var TL_PREDICT_URL = '/FRS25/TrackLeakage/PredictApi';                  /* v618.36 track shorting (E7MRIWeb TrackLeakage port) */
    var TL_AI_URL = '/FRS25/TrackLeakage/PredictAi';
    var TL_PLOTS_URL = '/FRS25/TrackLeakage/GetTrackPlots';
    var PLOTLY_SRC = 'https://cdn.plot.ly/plotly-2.35.2.min.js';

    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function fmt(v) {
        if (v === null || v === undefined || isNaN(v)) return '--';
        var a = Math.abs(v);
        return a >= 1000 ? v.toFixed(0) : a >= 100 ? v.toFixed(1) : v.toFixed(2);
    }
    function norm(s) { return String(s || '').toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function isLight() {
        var a = document.documentElement.getAttribute('data-aurora') || (document.body && document.body.getAttribute('data-aurora')) || '';
        return String(a).toLowerCase() === 'light';
    }
    /* v618.32: 'pm' for point machines (asset type 3), 'signal' (type 2, v618.33), else 'track' */
    function kind() {
        var a = live()[D.aid] || {};
        var t = parseInt(a.AssetTypeId || W.wsCurrentAssetTypeId || ($('#drpAssetType').val && $('#drpAssetType').val()) || 0, 10);
        return t === 3 ? 'pm' : t === 2 ? 'signal' : 'track';
    }
    function siteId() { return String((W.jQuery && $('#drpSite').val()) || ''); }
    function live() { return W.wsLiveData || {}; }
    function describe(aid) {
        if (W.TlHealthView && typeof W.TlHealthView.describe === 'function') {
            try { return W.TlHealthView.describe(aid); } catch (e) { /* fall through */ }
        }
        return null;
    }
    function findRow(A, key) {
        if (!A) return null;
        var i;
        for (i = 0; i < A.rows.length; i++) if (A.rows[i].key === key) return A.rows[i];
        for (i = 0; i < (A.derived || []).length; i++) if (A.derived[i].key === key) return A.derived[i];
        return null;
    }
    var UNIT_RX = /^(mA|A|V|mV|ms|s|Ohm|ohm|kOhm|Hz|C|%)$/;
    function unitOf(label) {
        var m = /\(([^)]+)\)\s*$/.exec(String(label || ''));
        if (m && UNIT_RX.test(m[1])) return m[1];
        m = /\((mA|A|V|ms|Ohm)\)/.exec(String(label || ''));
        if (m) return m[1];
        if (/\bmA\b/i.test(label)) return 'mA';
        if (/\bV\b/.test(label)) return 'V';
        return '';
    }
    /* strips a trailing unit only -- "VROSIG (AUG)" keeps its route letter */
    function baseLabel(label) {
        var t = String(label || ''), m = /\s*\(([^)]*)\)\s*$/.exec(t);
        return m && UNIT_RX.test(m[1]) ? t.slice(0, m.index) : t;
    }

    /* ------------------------------------------------------------------
       CSS (page tokens --e7-*, both schemes)
       ------------------------------------------------------------------ */
    var CSS = '' +
        '.tld-ov{position:fixed;inset:0;z-index:1990;background:rgba(15,27,42,.32);}' +
        '.tld{position:fixed;top:0;right:0;bottom:0;z-index:1991;width:min(1040px,100vw);display:flex;flex-direction:column;' +
        'background:var(--e7-surface-2,#F7F9FB);color:var(--e7-text,#22303f);box-shadow:-18px 0 40px -12px rgba(15,27,42,.35);' +
        'font-family:inherit;font-size:14px;animation:tldIn .18s ease-out;}' +
        '@keyframes tldIn{from{transform:translateX(40px);opacity:.4}to{transform:none;opacity:1}}' +
        '.tld-head{display:flex;align-items:center;gap:12px;padding:14px 18px;border-bottom:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);}' +
        '.tld-ico{width:40px;height:40px;border-radius:11px;display:grid;place-items:center;background:linear-gradient(135deg,#2BC0C8,#2F6FDB);color:#fff;flex:none;}' +
        '.tld-title{font-size:18px;font-weight:800;color:var(--e7-heading,#1b3c55);}' +
        '.tld-chips{display:flex;flex-wrap:wrap;gap:6px;margin-top:4px;}' +
        '.tld-chip{display:inline-flex;align-items:center;height:22px;padding:0 9px;border-radius:999px;font-size:11.5px;font-weight:700;border:1px solid var(--e7-border,#e7ebf1);color:var(--e7-text-2,#55657a);background:var(--e7-surface-2,#f4f6f9);}' +
        '.tld-chip.ok{color:#1F9D55;border-color:rgba(31,157,85,.45);background:rgba(31,157,85,.08);}' +
        '.tld-chip.warn{color:#9A6A00;border-color:rgba(234,179,8,.55);background:rgba(234,179,8,.12);}' +
        '.tld-chip.bad{color:#C8323A;border-color:rgba(229,72,77,.45);background:rgba(229,72,77,.08);}' +
        '.tld-btn{height:34px;padding:0 12px;border-radius:9px;border:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);color:var(--e7-text-2,#415064);font:inherit;font-size:12.5px;font-weight:600;cursor:pointer;}' +
        '.tld-btn:hover{border-color:var(--e7-accent,#2f9ec4);color:var(--e7-accent,#2f9ec4);}' +
        '.tld-btn.sm{height:26px;padding:0 9px;font-size:11.5px;border-radius:7px;}' +
        '.tld-btn[aria-pressed="true"]{background:var(--e7-accent,#2f9ec4);border-color:var(--e7-accent,#2f9ec4);color:#fff;}' +
        '.tld-x{width:36px;height:36px;font-size:18px;padding:0;}' +
        '.tld-actions{display:flex;flex-wrap:wrap;align-items:center;gap:6px;padding:10px 18px;border-bottom:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);}' +
        /* v618.38 in-drawer section nav (SPA-style jump to each section) */
        '.tld-navbtn{height:30px;padding:0 12px;border-radius:999px;border:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);color:var(--e7-text-2,#415064);font:inherit;font-size:12px;font-weight:600;cursor:pointer;white-space:nowrap;}' +
        '.tld-navbtn:hover{border-color:var(--e7-accent,#2f9ec4);color:var(--e7-accent,#2f9ec4);}' +
        '.tld-navbtn[aria-pressed="true"]{background:var(--e7-accent,#2f9ec4);border-color:var(--e7-accent,#2f9ec4);color:#fff;}' +
        '.tld-navgap{flex:1;min-width:8px;}' +
        '.tld-defan .tld-sh{margin-bottom:6px;}' +
        '.tld-body{flex:1;min-height:0;overflow:auto;padding:14px 16px;display:flex;flex-direction:column;gap:12px;}' +
        '.tld-row{display:grid;grid-template-columns:minmax(0,1.05fr) minmax(0,1fr);gap:12px;align-items:stretch;}' +
        '.tld-col{display:flex;flex-direction:column;gap:12px;min-width:0;}' +
        '.tld-sec{border:1px solid var(--e7-border,#e7ebf1);border-radius:14px;background:var(--e7-surface,#fff);padding:12px 14px;display:flex;flex-direction:column;gap:8px;min-width:0;}' +
        '.tld-sec.fill{flex:1;min-height:0;}' +
        '.tld-sh{display:flex;align-items:center;gap:8px;}' +
        '.tld-sh h3{margin:0;font-size:13px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;color:var(--e7-text-2,#415064);flex:1;}' +
        '.tld-note{font-size:11px;color:var(--e7-text-3,#8b98a8);}' +
        '.tld-tiles{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px;}' +
        '.tld-tile{display:block;width:100%;text-align:left;font:inherit;color:inherit;cursor:pointer;border:1px solid var(--e7-border,#e7ebf1);border-radius:10px;padding:8px 10px 6px;background:var(--e7-surface,#fff);}' +
        '.tld-tile:hover{border-color:var(--e7-accent,#2f9ec4);} .tld-tile.sel{box-shadow:0 0 0 2px var(--e7-accent,#2f9ec4);}' +
        '.tld-calc button{display:flex;justify-content:space-between;gap:6px;width:100%;padding:3px 2px;border:none;border-bottom:1px dashed var(--e7-border,#e7ebf1);background:transparent;font:inherit;color:inherit;text-align:left;cursor:pointer;}' +
        '.tld-calc button:hover span,.tld-calc button.sel span{color:var(--e7-accent,#2f9ec4);}' +
        '#tldTrend{margin-top:10px;border:1px solid var(--e7-accent,#2f9ec4);border-radius:12px;padding:10px 12px;background:var(--e7-surface,#fff);}' +
        '.tld-tr-h{display:flex;align-items:center;gap:8px;margin-bottom:4px;} .tld-tr-h b{font-size:14px;flex:1;}' +
        '.tld-tr-s{display:flex;flex-wrap:wrap;gap:4px 14px;font-size:12px;color:var(--e7-text-3,#6B7A8C);margin-bottom:4px;} .tld-tr-s b{font-family:"JetBrains Mono",ui-monospace,monospace;color:var(--e7-heading,#0F1B2A);}' +
        '.tld-tile .hd{display:flex;align-items:flex-start;gap:6px;} .tld-tile .lb{flex:1;font-size:11.5px;line-height:1.25;color:var(--e7-text-2,#4A5868);min-height:29px;}' +
        '.tld-tile.occ .bg{background:rgba(127,140,160,.18);color:var(--e7-text-2,#55657a);}' +
        '.tld-tile .bg{font:700 10px "JetBrains Mono",ui-monospace,monospace;padding:1px 6px;border-radius:4px;}' +
        '.tld-tile .vl{font:700 18px "JetBrains Mono",ui-monospace,monospace;color:var(--e7-heading,#0F1B2A);margin-top:2px;}' +
        '.tld-tile .vl small{font-size:11px;color:var(--e7-text-3,#8b98a8);margin-left:3px;}' +
        '.tld-tile .sl{font-size:11.5px;font-weight:600;color:var(--e7-text-3,#6B7A8C);}' +
        '.tld-tile.low,.tld-tile.near{border-color:rgba(224,161,0,.55);background:rgba(224,161,0,.06);}' +
        '.tld-tile.low .vl,.tld-tile.low .sl,.tld-tile.near .vl,.tld-tile.near .sl{color:#B47B00;} .tld-tile.low .bg,.tld-tile.near .bg{background:rgba(224,161,0,.2);color:#8A5F00;}' +
        '.tld-tile.high{border-color:rgba(229,72,77,.55);background:rgba(229,72,77,.06);} .tld-tile.high .vl,.tld-tile.high .sl{color:#C8323A;} .tld-tile.high .bg{background:rgba(229,72,77,.16);color:#C8323A;}' +
        '.tld-g{position:relative;height:10px;margin:6px 0 2px;}' +
        '.tld-g .tr{position:absolute;left:0;right:0;top:3px;height:4px;border-radius:3px;background:linear-gradient(90deg,rgba(224,161,0,.55) 0%,rgba(31,157,85,.38) 22%,rgba(31,157,85,.38) 78%,rgba(229,72,77,.45) 100%);}' +
        '.tld-g .mk{position:absolute;top:-2px;width:5px;height:14px;margin-left:-2.5px;border-radius:2px;background:var(--e7-heading,#0F1B2A);}' +
        '.tld-g .mk.low,.tld-g .mk.near{background:#E0A100;} .tld-g .mk.high{background:#E5484D;}' +
        '.tld-gl{display:flex;justify-content:space-between;font:10px "JetBrains Mono",ui-monospace,monospace;color:var(--e7-text-3,#8492A6);}' +
        '.tld-calc{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:2px 18px;margin-top:8px;font-size:12px;}' +
        '.tld-calc div{display:flex;justify-content:space-between;gap:6px;padding:3px 0;border-bottom:1px dashed var(--e7-border,#e7ebf1);}' +
        '.tld-calc span{color:var(--e7-text-3,#6B7A8C);} .tld-calc b{font-family:"JetBrains Mono",ui-monospace,monospace;}' +
        '.tld-relays{display:flex;flex-wrap:wrap;gap:6px;}' +
        '.tld-an{display:flex;align-items:center;gap:12px;} .tld-an b{font-size:15px;}' +
        '.tld-find{list-style:none;margin:0;padding:0;font-size:12.5px;line-height:1.45;}' +
        '.tld-find li{display:flex;gap:8px;padding:4px 0;} .tld-find i{flex:none;width:8px;height:8px;border-radius:50%;margin-top:6px;}' +
        '.tld-ritem{border:1px solid var(--e7-border,#e7ebf1);border-radius:10px;padding:10px 12px;margin-bottom:8px;display:flex;flex-direction:column;gap:6px;}' +
        '.tld-rreason{font-size:13px;color:var(--e7-text,#22303f);}' +
        '.tld-rev summary{cursor:pointer;font-size:12px;color:var(--e7-accent,#2f9ec4);} .tld-rev div{font-size:12px;color:var(--e7-text-2,#55657a);white-space:pre-wrap;margin-top:4px;}' +
        '.tld-rvisit{font-size:12.5px;padding:8px 10px;border-radius:8px;background:rgba(31,157,85,.07);border:1px solid rgba(31,157,85,.3);margin-bottom:8px;}' +
        '.tld-ok{font-size:12.5px;padding:8px 10px;border-radius:8px;background:rgba(31,157,85,.07);border:1px solid rgba(31,157,85,.3);margin-bottom:6px;}' +
        '.tld-sum{margin:6px 0 4px;font-size:12.5px;line-height:1.5;color:var(--e7-text,#22303f);}' +
        '.tld-act{font-size:12.5px;padding:6px 10px;border-radius:8px;background:rgba(47,158,196,.08);border:1px solid rgba(47,158,196,.3);margin:4px 0 6px;}' +
        '.tld-hload{display:flex;align-items:center;gap:8px;font-size:12px;color:var(--e7-text-2,#55657a);margin-bottom:6px;}' +
        '.tld-spin{width:14px;height:14px;border-radius:50%;border:2px solid var(--e7-border,#e7ebf1);border-top-color:var(--e7-accent,#2f9ec4);animation:tldSpin .8s linear infinite;}' +
        '@keyframes tldSpin{to{transform:rotate(360deg)}}' +
        /* v618.37 Ask AI ("Drawer final" mockup + E7MRIWeb ChatBot answer rendering) */
        '.tld-chat{display:flex;flex-direction:column;gap:10px;flex:1;min-height:520px;}' +
        '.tld-ib{width:28px;height:28px;border-radius:7px;border:1px solid var(--e7-border,#E4E8EE);background:var(--e7-surface,#fff);color:var(--e7-text-2,#415064);cursor:pointer;display:inline-grid;place-items:center;padding:0;}' +
        '.tld-ib:hover,.tld-ib[aria-pressed="true"]{border-color:#0A7C86;color:#0A7C86;}' +
        '.tld-chatsec.full{position:fixed;top:0;right:0;bottom:0;width:min(1040px,100vw);z-index:1993;border-radius:0;box-shadow:-18px 0 40px -12px rgba(15,27,42,.35);}' +
        '.tld-chatsec.full .tld-chat{min-height:0;}' +
        '.tld-ctx{display:flex;flex-wrap:wrap;align-items:center;gap:6px;font-size:11px;color:var(--e7-text-3,#6B7A8C);}' +
        '.tld-ctx span{display:inline-flex;align-items:center;height:22px;padding:0 9px;border-radius:999px;font-size:11.5px;font-weight:700;color:#0A7C86;background:rgba(10,124,134,.08);border:1px solid rgba(10,124,134,.35);}' +
        '.tld-msgs{display:flex;flex-direction:column;gap:10px;flex:1;min-height:0;max-height:640px;overflow-y:auto;padding-right:4px;}' +
        '.tld-chatsec.full .tld-msgs{max-height:none;}' +
        '.tld-mu{align-self:flex-end;max-width:85%;background:#0A7C86;color:#fff;border-radius:14px 14px 4px 14px;padding:8px 12px;font-size:13px;white-space:pre-wrap;overflow-wrap:anywhere;}' +
        '.tld-ma{align-self:flex-start;max-width:92%;min-width:60px;background:var(--e7-surface-3,#F1F4F7);color:var(--e7-text,#1F2937);border-radius:14px 14px 14px 4px;padding:9px 12px;font-size:13px;line-height:1.45;overflow-wrap:anywhere;}' +
        '.tld-ma.err .tld-mdb{display:none;}' +
        '.tld-mdb h2,.tld-mdb h3,.tld-mdb h4{margin:8px 0 4px;font-size:13.5px;font-weight:800;color:var(--e7-heading,#0F1B2A);} .tld-mdb h2{font-size:14.5px;}' +
        '.tld-mdb ul,.tld-mdb ol{margin:4px 0 4px 18px;padding:0;} .tld-mdb li{margin:2px 0;}' +
        '.tld-mdb pre{background:rgba(15,27,42,.06);border-radius:8px;padding:8px 10px;overflow-x:auto;font-size:12px;margin:6px 0;} .tld-mdb code{font-family:"JetBrains Mono",ui-monospace,monospace;font-size:12px;}' +
        '.tld-mdb :not(pre)>code{background:rgba(15,27,42,.06);border-radius:4px;padding:0 4px;}' +
        '.tld-mdb blockquote{margin:6px 0;padding:2px 10px;border-left:3px solid #0A7C86;color:var(--e7-text-2,#55657a);} .tld-mdb hr{border:none;border-top:1px solid var(--e7-border,#e7ebf1);margin:8px 0;}' +
        '.tld-mdt{overflow-x:auto;margin:6px 0;} .tld-mdt table{border-collapse:collapse;font-size:12px;width:100%;}' +
        '.tld-mdt th,.tld-mdt td{border:1px solid var(--e7-border,rgba(15,23,42,.12));padding:4px 8px;text-align:left;} .tld-mdt th{background:rgba(15,27,42,.04);font-weight:700;}' +
        '.tld-pc{border:1px solid var(--e7-border,#e7ebf1);border-left:4px solid #94A3B8;border-radius:10px;background:var(--e7-surface,#fff);margin:6px 0;overflow:hidden;}' +
        '.tld-pc.urgent{border-left-color:#E5484D;} .tld-pc.soon{border-left-color:#E0A100;} .tld-pc.monitor{border-left-color:#1F9D55;}' +
        '.tld-pc .pc-head{display:flex;flex-wrap:wrap;align-items:center;gap:8px;padding:7px 10px;border-bottom:1px solid var(--e7-border,#e7ebf1);}' +
        '.tld-pc .pc-pill{font:800 10px "JetBrains Mono",ui-monospace,monospace;padding:2px 7px;border-radius:999px;background:rgba(148,163,184,.2);}' +
        '.tld-pc.urgent .pc-pill{background:rgba(229,72,77,.14);color:#C8323A;} .tld-pc.soon .pc-pill{background:rgba(224,161,0,.18);color:#8A5F00;} .tld-pc.monitor .pc-pill{background:rgba(31,157,85,.12);color:#1F9D55;}' +
        '.tld-pc .pc-asset{font-weight:800;} .tld-pc .pc-sub{font-size:11.5px;color:var(--e7-text-3,#6B7A8C);}' +
        '.tld-pc .pc-row{display:grid;grid-template-columns:120px 1fr;gap:8px;padding:5px 10px;font-size:12px;} .tld-pc .pc-k{color:var(--e7-text-3,#6B7A8C);font-weight:600;}' +
        '.tld-pc .pc-action{background:rgba(10,124,134,.06);} .tld-pc .pc-note .pc-v{color:var(--e7-text-2,#55657a);font-style:italic;}' +
        '.tld-tool{display:flex;align-items:center;gap:7px;font-size:11.5px;font-weight:600;color:#0A7C86;margin-bottom:4px;}' +
        '.tld-dots{display:inline-flex;gap:4px;padding:4px 0;} .tld-dots i{width:6px;height:6px;border-radius:50%;background:var(--e7-text-3,#8b98a8);animation:tldDot 1.2s infinite ease-in-out;}' +
        '.tld-dots i:nth-child(2){animation-delay:.15s} .tld-dots i:nth-child(3){animation-delay:.3s}' +
        '@keyframes tldDot{0%,80%,100%{opacity:.25;transform:translateY(0)}40%{opacity:1;transform:translateY(-3px)}}' +
        '.tld-caret{display:inline-block;width:7px;height:14px;margin-left:2px;vertical-align:-2px;background:#0A7C86;animation:tldBlink 1s steps(2) infinite;}' +
        '@keyframes tldBlink{50%{opacity:0}}' +
        '.tld-merr{margin-top:6px;font-size:12.5px;color:#C8323A;}' +
        '.tld-macts{display:flex;gap:6px;margin-top:6px;}' +
        '.tld-macts button{display:inline-flex;align-items:center;gap:4px;height:24px;padding:0 8px;border-radius:7px;border:1px solid transparent;background:transparent;font:inherit;font-size:11.5px;color:var(--e7-text-3,#6B7A8C);cursor:pointer;}' +
        '.tld-macts button:hover{border-color:var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);color:#0A7C86;}' +
        '.tld-sugg{display:flex;flex-wrap:wrap;gap:6px;}' +
        '.tld-sugg button{height:28px;padding:0 10px;border-radius:999px;border:1px solid var(--e7-border,#E4E8EE);background:var(--e7-surface,#fff);font:inherit;font-size:11.5px;color:var(--e7-text-2,#415064);cursor:pointer;}' +
        '.tld-sugg button:hover{border-color:#0A7C86;color:#0A7C86;} .tld-sugg button[disabled]{opacity:.5;cursor:wait;}' +
        '.tld-in{display:flex;gap:8px;align-items:flex-end;border:1px solid var(--e7-border,#E4E8EE);border-radius:12px;padding:8px;background:var(--e7-surface,#fff);}' +
        '.tld-in:focus-within{border-color:#0A7C86;box-shadow:0 0 0 3px rgba(10,124,134,.12);}' +
        '.tld-in textarea{flex:1;border:none;resize:none;font:inherit;font-size:13px;outline:none;background:transparent;color:inherit;}' +
        '.tld-send{width:36px;height:36px;border-radius:10px;border:none;background:#0A7C86;color:#fff;cursor:pointer;display:grid;place-items:center;flex:none;}' +
        '.tld-send.stop{background:#C8323A;}' +
        '.tld-tbl{overflow-x:auto;border:1px solid var(--e7-border,#e7ebf1);border-radius:10px;}' +
        '.tld-tbl table{border-collapse:collapse;width:100%;}' +
        '.tld-tbl th{font-size:10.5px;font-weight:800;letter-spacing:.05em;text-transform:uppercase;color:var(--e7-text-2,#415064);text-align:left;padding:8px 10px;border-bottom:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface-2,#F7F9FB);white-space:nowrap;}' +
        '.tld-tbl td{font-size:12.5px;padding:8px 10px;border-bottom:1px solid var(--e7-border,#e7ebf1);vertical-align:top;}' +
        '.tld-tbl td.t{font-family:"JetBrains Mono",ui-monospace,monospace;white-space:nowrap;}' +
        '.tld-tbl tr.f td:first-child{box-shadow:inset 3px 0 0 #E5484D;} .tld-tbl tr.p td:first-child{box-shadow:inset 3px 0 0 #E0A100;}' +
        '.tld-fil{display:flex;flex-wrap:wrap;gap:6px;align-items:center;}' +
        '.tld-fil button{display:inline-flex;align-items:center;gap:6px;height:28px;padding:0 11px;border-radius:999px;border:1px solid var(--e7-border,#e7ebf1);background:var(--e7-surface,#fff);color:var(--e7-text-2,#415064);font:inherit;font-size:12px;font-weight:700;cursor:pointer;}' +
        '.tld-fil button[aria-pressed="true"]{border-color:var(--e7-accent,#0A7C86);color:var(--e7-accent,#0A7C86);background:rgba(47,158,196,.08);}' +
        '.tld-empty{color:var(--e7-text-3,#8b98a8);font-size:12.5px;padding:8px 2px;}' +
        '.tld-pend{border:1px dashed var(--e7-border-2,#cbd5e1);border-radius:10px;padding:14px;color:var(--e7-text-2,#55657a);font-size:12.5px;line-height:1.5;}' +
        '@media (max-width:820px){.tld-row{grid-template-columns:1fr;}.tld-tiles{grid-template-columns:repeat(2,minmax(0,1fr));}.tld-calc{grid-template-columns:repeat(2,minmax(0,1fr));}}' +
        /* card button */
        '.tl-asset-action.tl-aa-ai,.sig-action-btn.tl-aa-ai{color:#7C5CFF;}';
    function ensureCss() {
        if (document.getElementById('tldCss')) return;
        var st = document.createElement('style');
        st.id = 'tldCss';
        st.textContent = CSS;
        document.head.appendChild(st);
    }

    /* ------------------------------------------------------------------
       LIVE VALUES + ANALYSIS
       ------------------------------------------------------------------ */
    function stOf(r) {
        if (r.st === 'occ') return 'occ';   /* relay-end reading low because the track is occupied (Health view rule) */
        var g = r.range;
        if (r.v === null || !g || g.min === null || g.max === null || !(g.max > g.min)) return r.st === 'occ' ? 'ok' : 'none';
        if (r.v < g.min) return 'low';
        if (r.v > g.max) return 'high';
        var sp = g.max - g.min;
        return (r.v < g.min + sp * 0.08 || r.v > g.max - sp * 0.08) ? 'near' : 'ok';
    }
    function statusLine(r, st) {
        var g = r.range;
        if (r.v === null) return 'no value';
        if (st === 'low') return 'LOW - below ' + fmt(g.min);
        if (st === 'high') return 'HIGH - above ' + fmt(g.max);
        if (st === 'near') return 'near ' + (r.v - g.min < g.max - r.v ? 'low' : 'high') + ' limit';
        if (st === 'none') return 'no safe range';
        if (st === 'occ') return 'low -- track occupied';
        return 'at normal';
    }
    function tileHtml(r) {
        var st = stOf(r), g = r.range;
        var lab = r.alias || r.label;
        var badge = st === 'low' ? 'LOW' : st === 'high' ? 'HIGH' : st === 'near' ? 'NEAR' : st === 'occ' ? 'OCC.' : '';
        var gauge = '';
        if (g && g.min !== null && g.max !== null && g.max > g.min && r.v !== null) {
            var p = Math.max(0, Math.min(100, ((r.v - g.min) / (g.max - g.min)) * 100));
            gauge = '<div class="tld-g"><span class="tr"></span><span class="mk ' + st + '" style="left:' + p.toFixed(1) + '%"></span></div>' +
                '<div class="tld-gl"><span>' + fmt(g.min) + '</span><span>' + fmt(g.max) + '</span></div>';
        }
        var sel = D.sel && D.sel.key === r.key;
        return '<button type="button" class="tld-tile ' + st + (sel ? ' sel' : '') + '" data-tld-k="' + esc(r.key) + '" aria-pressed="' + sel + '" title="' + esc(lab) + ' -- click for its trend"><div class="hd"><span class="lb">' + esc(lab) + '</span>' +
            (badge ? '<span class="bg">' + badge + '</span>' : '') + '</div>' +
            '<div class="vl">' + fmt(r.v) + '<small>' + esc(unitOf(lab)) + '</small></div>' +
            '<div class="sl">' + esc(statusLine(r, st)) + '</div>' + gauge + '</button>';
    }
    function valuesHtml(A) {
        if (!A || !A.rows.length) return '<div class="tld-empty">Waiting for live values...</div>';
        var h = '<div class="tld-tiles">' + A.rows.map(tileHtml).join('') + '</div>';
        if (A.derived && A.derived.length) {
            h += '<div class="tld-calc">' + A.derived.map(function (r) {
                var sel = D.sel && D.sel.key === r.key;
                return '<button type="button" data-tld-k="' + esc(r.key) + '" aria-pressed="' + sel + '" class="' + (sel ? 'sel' : '') + '" title="' + esc(r.alias || r.label) + ' -- click for details"><span>' + esc(r.alias || r.label) + '</span><b>' + fmt(r.v) + '</b></button>';
            }).join('') + '</div>';
        }
        if (A.relays && A.relays.length) {
            h += '<div class="tld-relays" style="margin-top:8px">' + A.relays.map(function (rl) {
                return '<span class="tld-chip ' + (rl.up ? 'ok' : 'warn') + '">' + esc(rl.name) + ' ' + (rl.up ? '&#8593; Pickup' : '&#8595; Drop') + '</span>';
            }).join('') + '</div>';
        }
        if (D.sel) h += '<div id="tldTrend">' + trendHtml(A) + '</div>';
        return h;
    }

    /* v618.31 -- TREND of the clicked reading (inside Live values): history of
       the same period as the Graph section, step line, safe band, min / avg /
       max, hover readout "value at time, held since ... until ...". */
    function colFor(G, r) {
        if (!G || !G.cols) return -1;
        var want = [norm(r.alias), norm(r.label), r.key, norm(baseLabel(r.alias)), norm(baseLabel(r.label))];
        for (var i = 0; i < G.cols.length; i++) {
            if (G.cols[i].skip) continue;
            var n = norm(G.cols[i].name), nb = norm(baseLabel(G.cols[i].name));
            if (want.indexOf(n) !== -1 || want.indexOf(nb) !== -1) return i;
        }
        return -1;
    }
    function trendHtml(A) {
        var r = A ? findRow(A, D.sel.key) : null;
        var lab = r ? (r.alias || r.label) : D.sel.label;
        var head = '<div class="tld-tr-h"><b>' + esc(lab) + '</b><span class="tld-note">' + (D.graphHours >= 168 ? 'last 7 days' : 'last ' + D.graphHours + ' h') + '</span>' +
            '<button type="button" class="tld-btn sm" data-tld-tograph="1">Show in graph</button><button type="button" class="tld-btn sm" data-tld-unsel="1" aria-label="Close trend">&times;</button></div>';
        if (r && r.derived) return head + '<div class="tld-empty">Calculated value (from the measured readings), so it has no history of its own. Click a measured reading for its trend.</div>';
        var G = D.graph;
        if (!G || G.loading || !G.rows) return head + '<div class="tld-empty">Loading history...</div>';
        if (G.err) return head + '<div class="tld-empty">' + esc(G.err) + '</div>';
        var ci = colFor(G, r || { alias: lab, label: lab, key: D.sel.key });
        if (ci < 0) return head + '<div class="tld-empty">No history column for this reading in the period.</div>';
        var pts = [];
        G.rows.forEach(function (row) { var v = parseFloat(row.v[ci]); if (!isNaN(v)) pts.push([row.t, v]); });
        if (!pts.length) return head + '<div class="tld-empty">No samples in this period.</div>';
        D.trendPts = pts;
        var g = r && r.range && r.range.min !== null && r.range.max > r.range.min ? r.range : null;
        var lo = Infinity, hi = -Infinity, sum = 0;
        pts.forEach(function (p) { if (p[1] < lo) lo = p[1]; if (p[1] > hi) hi = p[1]; sum += p[1]; });
        var mn = lo, mx = hi, avg = sum / pts.length;
        if (g) { lo = Math.min(lo, g.min); hi = Math.max(hi, g.max); }
        if (hi === lo) { hi += 1; lo -= 1; }
        var pad = (hi - lo) * 0.1; lo -= pad; hi += pad;
        var w = 640, h = 190, L = 50, R = 10, T = 10, B = 26, gw = w - L - R, gh = h - T - B, span = (G.t1 - G.t0) || 1;
        var X = function (t) { return L + Math.max(0, Math.min(1, (t - G.t0) / span)) * gw; };
        var Y = function (v) { return T + gh - ((v - lo) / (hi - lo)) * gh; };
        D.trendGeo = { L: L, gw: gw, w: w, t0: G.t0, span: span };
        var svg = '<svg id="tldTrendSvg" viewBox="0 0 ' + w + ' ' + h + '" width="100%" role="img" aria-label="' + esc(lab) + ' trend" style="display:block;cursor:crosshair">';
        [lo + pad, (lo + hi) / 2, hi - pad].forEach(function (v) {
            svg += '<line x1="' + L + '" x2="' + (w - R) + '" y1="' + Y(v).toFixed(1) + '" y2="' + Y(v).toFixed(1) + '" stroke="rgba(127,140,160,.25)"></line>' +
                '<text x="' + (L - 6) + '" y="' + (Y(v) + 3).toFixed(1) + '" text-anchor="end" font-size="10" fill="var(--e7-text-3,#8492A6)">' + fmt(v) + '</text>';
        });
        if (g) svg += '<rect x="' + L + '" y="' + Y(g.max).toFixed(1) + '" width="' + gw + '" height="' + (Y(g.min) - Y(g.max)).toFixed(1) + '" fill="rgba(31,157,85,.12)"></rect>';
        var d = 'M' + X(pts[0][0]).toFixed(1) + ' ' + Y(pts[0][1]).toFixed(1);
        for (var i = 1; i < pts.length; i++) d += 'L' + X(pts[i][0]).toFixed(1) + ' ' + Y(pts[i - 1][1]).toFixed(1) + 'L' + X(pts[i][0]).toFixed(1) + ' ' + Y(pts[i][1]).toFixed(1);
        d += 'L' + X(G.t1).toFixed(1) + ' ' + Y(pts[pts.length - 1][1]).toFixed(1);
        svg += '<path d="' + d + '" fill="none" stroke="var(--e7-accent,#0A7C86)" stroke-width="1.8"></path>';
        for (var k = 0; k <= 6; k++) {
            var tx = L + k * gw / 6, dt = new Date(G.t0 + span * k / 6);
            svg += '<text x="' + tx.toFixed(1) + '" y="' + (h - 8) + '" text-anchor="middle" font-size="10" fill="var(--e7-text-3,#8492A6)">' +
                (D.graphHours > 48 ? dt.getDate() + '/' + (dt.getMonth() + 1) : ('0' + dt.getHours()).slice(-2) + ':' + ('0' + dt.getMinutes()).slice(-2)) + '</text>';
        }
        svg += '<line id="tldTrendCur" x1="0" x2="0" y1="' + T + '" y2="' + (T + gh) + '" stroke="#F59E0B" stroke-width="1.2" stroke-dasharray="3 3" style="display:none"></line></svg>';
        var stats = '<div class="tld-tr-s"><span>now <b>' + fmt(pts[pts.length - 1][1]) + '</b></span><span>min <b>' + fmt(mn) + '</b></span><span>avg <b>' + fmt(avg) + '</b></span><span>max <b>' + fmt(mx) + '</b></span>' +
            (g ? '<span>safe <b>' + fmt(g.min) + ' - ' + fmt(g.max) + '</b></span>' : '') + '<span>' + pts.length + ' samples</span></div>';
        return head + stats + svg + '<div class="tld-note" id="tldTrendRead">Hover the trend to read a value and when it last changed.</div>';
    }
    function paintTrend() {
        var el = document.getElementById('tldTrend');
        if (el) el.innerHTML = trendHtml(describe(D.aid));
    }
    function trendHover(e) {
        var svg = e.target && e.target.closest ? e.target.closest('#tldTrendSvg') : null;
        if (!svg || !D.trendGeo || !D.trendPts) return;
        var rc = svg.getBoundingClientRect(), G = D.trendGeo;
        var x = (e.clientX - rc.left) / rc.width * G.w;
        if (x < G.L || x > G.L + G.gw) return;
        var t = G.t0 + (x - G.L) / G.gw * G.span, pts = D.trendPts, i = -1;
        for (var k = 0; k < pts.length; k++) { if (pts[k][0] <= t) i = k; else break; }
        var cur = document.getElementById('tldTrendCur'), out = document.getElementById('tldTrendRead');
        if (cur) { cur.setAttribute('x1', x.toFixed(1)); cur.setAttribute('x2', x.toFixed(1)); cur.style.display = ''; }
        if (!out) return;
        if (i < 0) { out.textContent = new Date(t).toLocaleString() + ': no sample yet'; return; }
        var v = pts[i][1], a = i, b = i + 1;
        while (a > 0 && pts[a - 1][1] === v) a--;
        while (b < pts.length && pts[b][1] === v) b++;
        out.innerHTML = '<b>' + fmt(v) + '</b> at ' + esc(new Date(t).toLocaleString()) + ' &middot; held since ' + esc(new Date(pts[a][0]).toLocaleTimeString()) +
            (b < pts.length ? ' until ' + esc(new Date(pts[b][0]).toLocaleTimeString()) : ' (still)');
    }
    function rowByLabel(A, label) {
        var k = norm(label);
        for (var i = 0; i < A.rows.length; i++) if (norm(A.rows[i].label) === k || A.rows[i].key === k) return A.rows[i];
        return null;
    }
    function pmFindings(A, finds) {
        var dir = A.pmDir || '';
        if (dir) finds.push(['#1F9D55', 'Last operation: ' + dir + (A.pmWhen && A.pmWhen !== '--' ? ' at ' + A.pmWhen : '') + '.']);
        var rl = {};
        (A.relays || []).forEach(function (r) { rl[norm(r.name).slice(-4)] = r.up; });
        if (rl.NWKR === false && rl.RWKR === false) finds.push(['#E5484D', 'Neither NWKR nor RWKR is picked up: the point is not detected in either position.']);
        else if (dir === 'Normal' && rl.NWKR === true) finds.push(['#1F9D55', 'NWKR picked up: point detected Normal, matching the last operation.']);
        else if (dir === 'Reverse' && rl.RWKR === true) finds.push(['#1F9D55', 'RWKR picked up: point detected Reverse, matching the last operation.']);
        else if (dir && (rl.NWKR !== undefined || rl.RWKR !== undefined)) finds.push(['#E0A100', 'Detection relay does not match the last operated direction (' + dir + ').']);
        ['A', 'B'].forEach(function (E) {
            var mx = rowByLabel(A, E + ' End IPT N/R(A) Max'), av = rowByLabel(A, E + ' End IPT N/R(A) Avg'), tp = rowByLabel(A, E + ' End TPT N/R(ms)');
            if (mx && mx.v !== null) finds.push(['#1F9D55', E + ' end: peak ' + fmt(mx.v) + ' A, average ' + fmt(av ? av.v : null) + ' A, operation time ' + fmt(tp ? tp.v : null) + ' ms.']);
        });
        var ma = rowByLabel(A, 'A End IPT N/R(A) Max'), mb = rowByLabel(A, 'B End IPT N/R(A) Max');
        if (ma && mb && ma.v && mb.v) {
            var diff = Math.abs(ma.v - mb.v) / Math.max(ma.v, mb.v) * 100;
            if (diff > 25) finds.push(['#E0A100', 'A / B end peak currents differ by ' + diff.toFixed(0) + '%: compare the two ends (friction, obstruction or adjustment).']);
        }
        var ta = rowByLabel(A, 'A End TPT N/R(ms)'), tb = rowByLabel(A, 'B End TPT N/R(ms)');
        if (ta && tb && ta.v && tb.v && Math.abs(ta.v - tb.v) > 600) finds.push(['#E0A100', 'A / B end operation times differ by ' + fmt(Math.abs(ta.v - tb.v)) + ' ms.']);
    }
    /* v618.34 ANALYSIS = Asset Health API
       GET /FRS25/MaintenceRoster/AssetHealth?assetId=&siteId=[&refresh=true]
       -> health {score, status, label, color, headline, bands}, findings [{level ok|warn|bad, text}],
          summary, action, findingsSource ai|computed, sources, generatedAt, cached.
       Computed score / status (2-day data, 15-day alerts, shorting or PM band) + AI wording; cached 10 min
       on the server. While it loads (10-30 s) or if it fails, the live check below is shown. */
    var HCOL = { green: '#22A55B', amber: '#E0A100', red: '#E5484D' };
    var LVL = { ok: '#1F9D55', warn: '#E0A100', bad: '#E5484D' };
    function loadHealth(refresh) {
        var aid = D.aid;
        if (!aid) return;
        var prev = D.health[aid];
        D.health[aid] = { loading: true, data: prev && prev.data };
        paintAnalysis();
        $.ajax({
            url: HEALTH_URL, type: 'GET', dataType: 'json', timeout: 90000, cache: false,
            data: { assetId: aid, siteId: siteId() || 0, refresh: refresh ? 'true' : 'false' },
            success: function (r) {
                if (!r || r.error) { D.health[aid] = { err: (r && r.error) || 'empty reply', data: prev && prev.data }; }
                else D.health[aid] = { data: r, at: Date.now() };
                if (D.aid === aid) { paintAnalysis(); paintHead(); }
                var R0 = D.roster[aid];
                if (D.aid === aid && (!R0 || R0.needDivision)) loadRoster(false);
            },
            error: function (x, t) {
                var msg = t === 'timeout' ? 'timed out' : (x && x.responseJSON && x.responseJSON.error) || ('HTTP ' + (x && x.status));
                D.health[aid] = { err: msg, data: prev && prev.data };
                if (D.aid === aid) paintAnalysis();
            }
        });
    }
    function apiAnalysisHtml(r) {
        var h = r.health || {}, score = Math.max(0, Math.min(100, parseInt(h.score, 10) || 0));
        var col = HCOL[String(h.color || '').toLowerCase()] || '#94A3B8', C = 2 * Math.PI * 19;
        var f = (r.findings || []).map(function (x) {
            return '<li><i style="background:' + (LVL[String(x.level || '').toLowerCase()] || '#94A3B8') + '"></i><span>' + esc(x.text) + '</span></li>';
        }).join('');
        var srcBad = [];
        if (r.sources) Object.keys(r.sources).forEach(function (k) { if (String(r.sources[k]).toLowerCase() !== 'ok') srcBad.push(k + ' ' + r.sources[k]); });
        return '<div class="tld-an"><svg width="54" height="54" viewBox="0 0 48 48" aria-hidden="true"><circle cx="24" cy="24" r="19" fill="none" stroke="var(--e7-border,#E4E8EE)" stroke-width="5"></circle>' +
            '<circle cx="24" cy="24" r="19" fill="none" stroke="' + col + '" stroke-width="5" stroke-linecap="round" stroke-dasharray="' + (score / 100 * C).toFixed(1) + ' ' + C.toFixed(1) + '" transform="rotate(-90 24 24)"></circle>' +
            '<text x="24" y="29" text-anchor="middle" font-size="14" font-weight="800" fill="currentColor">' + score + '</text></svg>' +
            '<div><b style="color:' + col + '">' + esc(h.label || h.status || '') + '</b><div class="tld-note" style="font-size:12.5px">' + esc(h.headline || '') + '</div></div></div>' +
            (f ? '<ul class="tld-find">' + f + '</ul>' : '') +
            (r.summary ? '<p class="tld-sum">' + esc(r.summary) + '</p>' : '') +
            (r.action ? '<div class="tld-act"><b>Action:</b> ' + esc(r.action) + '</div>' : '') +
            '<div class="tld-note">' + (r.findingsSource === 'computed' ? 'Findings computed from the evidence (AI unavailable)' : 'AI health summary') +
            ' &#183; 2-day data, 15-day alerts' + (kind() === 'pm' ? ', PM prediction' : kind() === 'track' ? ', track shorting' : '') +
            (r.generatedAt ? ' &#183; ' + esc(r.generatedAt) : '') + (r.cached ? ' (cached)' : '') +
            (srcBad.length ? ' &#183; <span style="color:#B47B00">' + esc(srcBad.join(', ')) + '</span>' : '') + '</div>';
    }
    /* ------------------------------------------------------------------
       v618.35 MAINTENANCE ROSTER -- this asset on today's roster
       GET /FRS25/MaintenceRoster/GetWorksheet?divisionId=&siteId=&rosterDate=yyyy-MM-dd   (items)
       GET /FRS25/MaintenceRoster/GetMaintenanceState?divisionId=&siteId=&rosterDate=      (visits)
       Division id: Asset Health reply (asset.divisionId), else the page's Division filter.
       ------------------------------------------------------------------ */
    function F(o, names, dflt) {
        if (!o) return dflt;
        for (var i = 0; i < names.length; i++) if (o[names[i]] !== undefined && o[names[i]] !== null) return o[names[i]];
        return dflt;
    }
    function divisionId() {
        var H = D.health[D.aid];
        var d = H && H.data && H.data.asset ? (H.data.asset.divisionId || H.data.asset.DivisionId) : 0;
        if (!d && W.jQuery) d = parseInt($('#drpDivisions').val(), 10) || 0;
        return d || 0;
    }
    function itemsOf(resp) {
        /* worksheet shapes: { Items:[..] } | { Data:{ Items } } | { Stations:[{ Items }] } | [ .. ] */
        var out = [];
        (function walk(o, depth) {
            if (!o || depth > 4) return;
            if (Array.isArray(o)) {
                if (o.length && typeof o[0] === 'object' && (F(o[0], ['ItemId', 'itemId', 'ItemKey', 'itemKey', 'AssetId', 'assetId'], null) !== null)) { out = out.concat(o); return; }
                o.forEach(function (x) { walk(x, depth + 1); });
                return;
            }
            if (typeof o === 'object') Object.keys(o).forEach(function (k) { if (o[k] && typeof o[k] === 'object') walk(o[k], depth + 1); });
        })(resp, 0);
        return out;
    }
    function loadRoster(force) {
        var aid = D.aid, sid = siteId(), div = divisionId();
        if (!aid) return;
        if (!div || !sid) { D.roster[aid] = { needDivision: true }; paintRoster(); return; }
        var day = ymd(Date.now()), R0 = D.roster[aid];
        if (!force && R0 && R0.day === day && Date.now() - (R0.at || 0) < 600000 && !R0.err) { paintRoster(); return; }
        D.roster[aid] = { loading: true, day: day };
        paintRoster();
        var got = { sheet: null, state: null }, left = 2;
        var done = function () {
            if (--left > 0) return;
            var items = itemsOf(got.sheet).filter(function (x) { return String(F(x, ['AssetId', 'assetId'], '')) === String(aid); });
            var sErr = got.sheet && (got.sheet.IsSuccess === false) ? (got.sheet.Message || 'worksheet unavailable') : got.sheet ? '' : 'worksheet unavailable';
            var visits = itemsOf(got.state).filter(function (x) { return String(F(x, ['AssetId', 'assetId'], '')) === String(aid); });
            D.roster[aid] = { day: day, at: Date.now(), div: div, items: items, visits: visits, sheetErr: sErr,
                stateErr: got.state && got.state.IsSuccess === false ? (got.state.Message || 'unavailable') : '' };
            if (D.aid === aid) paintRoster();
        };
        $.ajax({ url: ROSTER_SHEET_URL, type: 'GET', dataType: 'json', cache: false, timeout: 60000, data: { divisionId: div, siteId: sid, rosterDate: day },
            success: function (r) { got.sheet = r; done(); }, error: function () { got.sheet = null; done(); } });
        $.ajax({ url: ROSTER_STATE_URL, type: 'GET', dataType: 'json', cache: false, timeout: 60000, data: { divisionId: div, siteId: sid, rosterDate: day },
            success: function (r) { got.state = r; done(); }, error: function () { got.state = null; done(); } });
    }
    function prioChip(p) {
        var u = String(p || '').toUpperCase();
        return '<span class="tld-chip ' + (u === 'URGENT' ? 'bad' : u === 'SOON' ? 'warn' : '') + '">' + esc(u || 'ITEM') + '</span>';
    }
    function bandChip(name, b) {
        var u = String(b || '').toUpperCase();
        if (!u || u === 'NONE' || u === 'OK') return '';
        return '<span class="tld-chip ' + (u === 'URGENT' ? 'bad' : u === 'SOON' ? 'warn' : '') + '">' + esc(name) + ' ' + esc(u) + '</span>';
    }
    function rosterHtml() {
        var R = D.roster[D.aid];
        if (!R || R.loading) return '<div class="tld-hload"><span class="tld-spin"></span>Loading today\'s maintenance roster...</div>';
        if (R.needDivision) return '<div class="tld-empty">Waiting for the asset\'s division (from the health summary) -- or choose a Division in the page filter.</div>';
        var h = '';
        if (R.sheetErr && !R.items.length) h += '<div class="tld-empty" style="color:#B47B00">Roster worksheet: ' + esc(R.sheetErr) + '</div>';
        else if (!R.items.length) h += '<div class="tld-ok"><b>Not on today\'s maintenance roster</b> (' + esc(R.day) + ').</div>';
        R.items.forEach(function (it) {
            var since = F(it, ['FirstSeenDate', 'firstSeenDate'], ''), days = F(it, ['DayCount', 'dayCount'], null);
            var st = F(it, ['CloseStatus', 'closeStatus', 'Status', 'status'], '');
            var ev = F(it, ['Evidence15d', 'evidence15d'], '');
            h += '<div class="tld-ritem"><div class="tld-chips">' + prioChip(F(it, ['Priority', 'priority'], '')) +
                (st ? '<span class="tld-chip">' + esc(String(st)) + '</span>' : '') +
                (F(it, ['IsFailedRepair', 'isFailedRepair'], false) ? '<span class="tld-chip bad">Failed repair</span>' : '') +
                bandChip('Drift', F(it, ['DriftBand', 'driftBand'], '')) + bandChip('PM', F(it, ['PmBand', 'pmBand'], '')) + bandChip('Shorting', F(it, ['TrackShortBand', 'trackShortBand'], '')) +
                (since ? '<span class="tld-note">on roster since ' + esc(String(since).slice(0, 10)) + (days ? ' (' + esc(days) + ' day' + (days > 1 ? 's' : '') + ')' : '') + '</span>' : '') + '</div>' +
                '<div class="tld-rreason">' + esc(F(it, ['Reason', 'reason'], '') || 'Roster item') + '</div>' +
                (F(it, ['ActionText', 'actionText', 'Action', 'action'], '') ? '<div class="tld-act"><b>Action:</b> ' + esc(F(it, ['ActionText', 'actionText', 'Action', 'action'], '')) + '</div>' : '') +
                (ev ? '<details class="tld-rev"><summary>Evidence (15 days)</summary><div>' + esc(typeof ev === 'string' ? ev : JSON.stringify(ev)) + '</div></details>' : '') + '</div>';
        });
        R.visits.forEach(function (v) {
            var vis = F(v, ['Visit', 'visit'], null) || v;
            var out = F(vis, ['Outcome', 'outcome', 'CloseStatus', 'closeStatus'], '');
            var when = F(vis, ['ClosedAt', 'closedAt', 'VisitedAt', 'visitedAt', 'UpdatedAt', 'updatedAt', 'CreatedAt', 'createdAt'], '');
            var by = F(vis, ['ClosedBy', 'closedBy', 'UserName', 'userName'], '');
            var rem = F(vis, ['Remark', 'remark'], '');
            if (!out && !when) return;
            h += '<div class="tld-rvisit"><b>Maintenance visit:</b> ' + esc(out || 'recorded') + (when ? ' &#183; ' + esc(String(when).replace('T', ' ').slice(0, 16)) : '') +
                (by ? ' &#183; ' + esc(by) : '') + (rem ? '<div class="tld-note">' + esc(rem) + '</div>' : '') + '</div>';
        });
        if (R.stateErr) h += '<div class="tld-note" style="color:#B47B00">Maintenance visits: ' + esc(R.stateErr) + '</div>';
        return h + '<div class="tld-note">Roster ' + esc(R.day) + ' &#183; division ' + esc(R.div) + ' &#183; <a href="' + ROSTER_PAGE_URL + '" target="_blank" rel="noopener">Open Maintenance Roster</a></div>';
    }
    function paintRoster() { var el = document.getElementById('tldRoster'); if (el) el.innerHTML = rosterHtml(); }

    /* ------------------------------------------------------------------
       v618.36 TRACK SHORTING (leakage) REPORT -- port of E7MRIWeb TrackLeakage
       POST /FRS25/TrackLeakage/PredictApi  { site_id, track_ids:[aid], start_date, end_date, force_live, include_plots:false }
            -> { results:[{ asset_id, overall_condition, leakage_info{leakage_detected, leakage_type, leakage_status,
                 severity, simple_summary, reason}, score_breakdown, major_events[] }], errors:[], generated_at, _from_cache }
       POST /FRS25/TrackLeakage/PredictAi   { site_id, track_id, start_date, end_date } -> { leakage_info }
       GET  /FRS25/TrackLeakage/GetTrackPlots?siteId=&assetId=&start_date=&end_date=&track_name=
            -> { raw_plot_json, events_plot_json, zoom_plots_json[] }  (Plotly figures, lazy-loaded)
       ------------------------------------------------------------------ */
    function isoDay(t) { var d = new Date(t); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
    function num(v, dp) { var n = parseFloat(v); return isNaN(n) ? '--' : n.toFixed(dp == null ? 2 : dp); }
    function loadShort(force) {
        var aid = D.aid, sid = siteId(), days = D.shortDays;
        if (!aid || kind() !== 'track') return;
        if (!sid) { D.short[aid] = { err: 'Choose a site in the page filter to run the shorting analysis.' }; paintShort(); return; }
        var S0 = D.short[aid];
        if (!force && S0 && S0.days === days && (S0.loading || (S0.data && Date.now() - (S0.at || 0) < 600000))) { paintShort(); return; }
        var ctx = { site_id: parseInt(sid, 10), start_date: isoDay(Date.now() - days * 86400000), end_date: isoDay(Date.now()), days: days };
        var names = {}; names[aid] = (live()[aid] || {}).AssetName || aid;
        D.short[aid] = { loading: true, days: days, ctx: ctx };
        paintShort();
        paintShortPlots();
        $.ajax({
            url: TL_PREDICT_URL, type: 'POST', contentType: 'application/json', dataType: 'json', timeout: 300000,
            data: JSON.stringify({ site_id: ctx.site_id, track_ids: [parseInt(aid, 10)], start_date: ctx.start_date, end_date: ctx.end_date,
                use_llm: false, force_live: !!force, track_names: names, include_plots: false }),
            success: function (r) {
                var res = null, er = null;
                ((r && r.results) || []).forEach(function (x) { if (String(x.asset_id) === String(aid)) res = x; });
                ((r && r.errors) || []).forEach(function (x) { if (String(x.asset_id) === String(aid)) er = x.error; });
                if (!res && !er && r && r.results && r.results.length === 1) res = r.results[0];
                D.short[aid] = res ? { data: res, at: Date.now(), days: days, ctx: ctx, generatedAt: r.generated_at, cached: !!r._from_cache }
                    : { err: er || 'No shorting result for this track.', days: days, ctx: ctx };
                if (D.aid === aid) { paintShort(); paintShortPlots(); }
            },
            error: function (x, t) {
                var msg = t === 'timeout' ? 'timed out' : t === 'parsererror' ? 'not authorised or session expired (got a page instead of data)' :
                    (x && x.responseJSON && x.responseJSON.detail) || (x && x.responseText && x.responseText.length < 300 ? x.responseText : 'HTTP ' + (x && x.status));
                D.short[aid] = { err: 'Shorting analysis failed: ' + msg, days: days, ctx: ctx };
                if (D.aid === aid) paintShort();
            }
        });
    }
    function shortAi() {
        var aid = D.aid, S = D.short[aid];
        if (!S || !S.ctx || (S.ai && S.ai.loading)) return;
        S.ai = { loading: true };
        paintShort();
        $.ajax({
            url: TL_AI_URL, type: 'POST', contentType: 'application/json', dataType: 'json', timeout: 300000,
            data: JSON.stringify({ site_id: S.ctx.site_id, track_id: parseInt(aid, 10), start_date: S.ctx.start_date, end_date: S.ctx.end_date }),
            success: function (r) { S.ai = { info: (r && r.leakage_info) || {} }; if (D.aid === aid) paintShort(); },
            error: function (x, t) {
                S.ai = { err: t === 'timeout' ? 'timed out' : (x && x.responseJSON && x.responseJSON.detail) || ('HTTP ' + (x && x.status)) };
                if (D.aid === aid) paintShort();
            }
        });
    }
    var COND = { critical: 'bad', moderate: 'warn', normal: 'ok' };
    function shortHtml() {
        var S = D.short[D.aid];
        if (!S || S.loading) return '<div class="tld-hload"><span class="tld-spin"></span>Running the track shorting analysis (last ' + D.shortDays + ' day' + (D.shortDays > 1 ? 's' : '') + ')...</div>';
        if (S.err) return '<div class="tld-empty" style="color:#B47B00">' + esc(S.err) + '</div>';
        var r = S.data, info = r.leakage_info || {};
        var cond = r.overall_condition || 'Normal', cc = COND[String(cond).split(' ')[0].toLowerCase()] || '';
        var leak = !!info.leakage_detected && info.leakage_type && info.leakage_type !== 'None';
        var sev = info.severity && info.severity !== 'Normal' ? info.severity : '';
        var s24 = 0, peak = 0;
        Object.keys(r.score_breakdown || {}).forEach(function (band) {
            if (band.toLowerCase().indexOf('24 hour') === -1) return;
            Object.keys(r.score_breakdown[band] || {}).forEach(function (k) { var s = +(r.score_breakdown[band][k] || {}).Score || 0; if (s > s24) s24 = s; });
        });
        var evts = r.major_events || [];
        evts.forEach(function (ev) { var a = +ev.Total_AUC || 0; if (a > peak) peak = a; });
        var h = '<div class="tld-chips"><span class="tld-chip ' + cc + '">' + esc(cond) + '</span>' +
            (leak ? '<span class="tld-chip ' + cc + '">' + esc(info.leakage_type) + '</span>' : '<span class="tld-chip ok">No leakage detected</span>') +
            (leak && info.leakage_status && info.leakage_status !== 'None' ? '<span class="tld-chip">' + esc(info.leakage_status) + '</span>' : '') +
            (leak && sev ? '<span class="tld-chip ' + (sev === 'Moderate' || sev === 'Low' ? 'warn' : 'bad') + '">' + esc(sev) + ' severity</span>' : '') +
            (leak && s24 > 0 ? '<span class="tld-chip">Last 24 h score ' + num(s24, 1) + '</span>' : '') +
            (peak > 0 ? '<span class="tld-chip">Peak AUC ' + num(peak) + ' mA-d</span>' : '') + '</div>';
        var A = S.ai || {}, ai = A.info;
        h += '<div class="tld-sh" style="margin-top:4px"><h3 style="font-size:11.5px">Diagnostic reasoning' + (ai ? ' &#183; AI' : '') + '</h3>' +
            '<button type="button" class="tld-btn sm" data-tld-sai="1"' + (A.loading ? ' disabled' : '') + ' style="color:#7C5CFF">' + (A.loading ? 'Analyzing...' : ai ? '&#8635; AI Analysis' : A.err ? 'Retry AI' : 'AI Analysis') + '</button></div>' +
            '<div class="tld-ritem" style="margin:0">' +
            (A.loading ? '<div class="tld-hload"><span class="tld-spin"></span>AI is analysing the telemetry scores...</div>' : '') +
            (A.err ? '<div class="tld-note" style="color:#C8323A">AI analysis failed: ' + esc(A.err) + '</div>' : '') +
            '<div class="tld-rreason" style="font-weight:700">' + esc((ai && ai.simple_summary) || info.simple_summary || 'Track looks healthy right now -- no signs of leakage.') + '</div>' +
            '<div class="tld-note" style="font-size:12px;line-height:1.5">' + esc((ai && ai.reason) || info.reason || 'Track circuit baseline is stable with no signs of internal or external leakage.') + '</div></div>';
        if (evts.length) {
            var cause = { 'Both (Glued Joint)': ['Glued joint', 'bad'], 'IF Only (Internal)': ['Internal', 'warn'], 'IR Only (Relay Side)': ['Relay side', 'warn'] };
            h += '<div class="tld-note" style="font-weight:700;text-transform:uppercase;letter-spacing:.04em">Major leakage events (AUC-based, &#8805; 4 h) &#183; ' + evts.length + '</div>' +
                '<div class="tld-tbl"><table><thead><tr><th>#</th><th>Start</th><th>End</th><th>Duration</th><th>Cause</th><th>IF AUC</th><th>IR AUC</th><th>Total AUC</th></tr></thead><tbody>' +
                evts.map(function (ev) {
                    var c = cause[ev.Dominant_Cause] || [ev.Dominant_Cause || '--', ''];
                    return '<tr class="' + (c[1] === 'bad' ? 'f' : 'p') + '"><td>' + esc(ev.Event_ID) + '</td><td class="t">' + esc(ev.Start_Time) + '</td><td class="t">' + esc(ev.End_Time) + '</td>' +
                        '<td>' + num(ev.Duration_Hours, 1) + ' h</td><td><span class="tld-chip ' + c[1] + '">' + esc(c[0]) + '</span></td>' +
                        '<td class="t">' + num(ev.IF_AUC) + '</td><td class="t">' + num(ev.IR_AUC) + '</td><td class="t" style="color:#C8323A;font-weight:700">' + num(ev.Total_AUC) + ' mA-d</td></tr>';
                }).join('') + '</tbody></table></div>';
        } else h += '<div class="tld-ok">No major leakage events (no continuous fault of 4 hours or more) in the period.</div>';
        return h + '<div class="tld-note">' + esc(S.ctx.start_date) + ' &#8594; ' + esc(S.ctx.end_date) +
            (S.generatedAt ? ' &#183; ' + (S.cached ? 'cached result ' : 'live result ') + esc(new Date(S.generatedAt).toLocaleString()) : '') + '</div>';
    }
    function paintShort() { var el = document.getElementById('tldShort'); if (el) el.innerHTML = shortHtml(); }

    /* If / Ir charts: Plotly figures from the Historian, Plotly itself loaded on first use */
    var plotlyP = null;
    function ensurePlotly() {
        if (W.Plotly) return Promise.resolve(W.Plotly);
        if (plotlyP) return plotlyP;
        plotlyP = new Promise(function (ok, ko) {
            var s = document.createElement('script');
            s.src = PLOTLY_SRC; s.charset = 'utf-8';
            s.onload = function () { ok(W.Plotly); };
            s.onerror = function () { plotlyP = null; ko(new Error('chart library could not be loaded')); };
            document.head.appendChild(s);
        });
        return plotlyP;
    }
    function shortPlotsHtml() {
        var S = D.short[D.aid];
        var P = S && S.plots;
        if (!S || !S.data) return '';
        if (!P) return '<button type="button" class="tld-btn sm" data-tld-splots="1">Show If / Ir charts (raw + detected events)</button>';
        if (P.loading) return '<div class="tld-hload"><span class="tld-spin"></span>Loading If / Ir charts...</div>';
        if (P.err) return '<div class="tld-note" style="color:#B47B00">Charts: ' + esc(P.err) + ' <button type="button" class="tld-btn sm" data-tld-splots="1">Retry</button></div>';
        if (!P.raw && !P.events) return '<div class="tld-empty">No chart data for this period.</div>';
        return (P.raw ? '<div class="tld-note" style="font-weight:700">RAW DATA (UNFILTERED &#183; IST)</div><div id="tldShortRaw" style="width:100%;min-height:420px"></div>' : '') +
            (P.events ? '<div class="tld-note" style="font-weight:700;margin-top:8px">DETECTED LEAKAGE EVENTS (CLEANED &#183; NON-TRAIN)</div><div id="tldShortEv" style="width:100%;min-height:420px"></div>' : '') +
            ((P.zooms || []).length ? '<div class="tld-note" style="font-weight:700;margin-top:8px">PER-EVENT ZOOM</div><div style="display:flex;gap:10px;overflow-x:auto">' +
                P.zooms.map(function (_, i) { return '<div id="tldShortZ' + i + '" style="flex:none;width:760px;min-height:360px"></div>'; }).join('') + '</div>' : '');
    }
    function drawFig(id, figStr) {
        var el = document.getElementById(id);
        if (!el || !figStr || !W.Plotly) return;
        try {
            var fig = typeof figStr === 'string' ? JSON.parse(figStr) : figStr, lay = fig.layout || {};
            if (!isLight()) {
                lay.paper_bgcolor = 'rgba(0,0,0,0)'; lay.plot_bgcolor = 'rgba(0,0,0,0)';
                lay.font = $.extend({}, lay.font || {}, { color: '#cbd5e1' });
            }
            W.Plotly.newPlot(el, fig.data, lay, { responsive: true, displaylogo: false });
        } catch (e) { el.innerHTML = '<div class="tld-empty" style="color:#C8323A">Chart render error</div>'; }
    }
    function paintShortPlots() {
        var el = document.getElementById('tldShortPlots');
        if (!el) return;
        el.innerHTML = shortPlotsHtml();
        var S = D.short[D.aid], P = S && S.plots;
        if (!P || P.loading || P.err) return;
        drawFig('tldShortRaw', P.raw);
        drawFig('tldShortEv', P.events);
        (P.zooms || []).forEach(function (z, i) { drawFig('tldShortZ' + i, z); });
    }
    function loadShortPlots() {
        var aid = D.aid, S = D.short[aid];
        if (!S || !S.ctx) return;
        S.plots = { loading: true };
        paintShortPlots();
        var done = function (P) { S.plots = P; if (D.aid === aid) paintShortPlots(); };
        ensurePlotly().then(function () {
            $.ajax({
                url: TL_PLOTS_URL, type: 'GET', dataType: 'json', cache: false, timeout: 300000,
                data: { siteId: S.ctx.site_id, assetId: aid, start_date: S.ctx.start_date, end_date: S.ctx.end_date, track_name: (live()[aid] || {}).AssetName || aid },
                success: function (r) { done({ raw: r && r.raw_plot_json, events: r && r.events_plot_json, zooms: (r && r.zoom_plots_json) || [] }); },
                error: function (x, t) { done({ err: t === 'timeout' ? 'timed out' : (x && x.responseJSON && x.responseJSON.detail) || ('HTTP ' + (x && x.status)) }); }
            });
        }, function (e) { done({ err: e.message }); });
    }
    function shortDaysHtml() {
        return [[1, '1 d'], [7, '7 d'], [15, '15 d']].map(function (p) {
            return '<button type="button" class="tld-btn sm" data-tld-sd="' + p[0] + '" aria-pressed="' + (D.shortDays === p[0]) + '">' + p[1] + '</button>';
        }).join(' ') + ' <button type="button" class="tld-btn sm" data-tld-srefresh="1" title="Run the analysis live (skips the cached result)">&#8635; Live</button>';
    }
    function analysisHtml(A) {
        var H = D.aid ? D.health[D.aid] : null;
        if (H && H.data) {
            return apiAnalysisHtml(H.data) + (H.loading ? '<div class="tld-note">Refreshing...</div>' : '') +
                (H.err ? '<div class="tld-note" style="color:#B47B00">Refresh failed: ' + esc(H.err) + '</div>' : '');
        }
        var banner = !H || H.loading ? '<div class="tld-hload"><span class="tld-spin"></span>Loading the AI health summary (10-30 s). Live check meanwhile:</div>'
            : '<div class="tld-note" style="color:#B47B00;margin-bottom:6px">AI health summary unavailable (' + esc(H.err) + '). Live check:</div>';
        return banner + liveAnalysisHtml(A);
    }
    function paintAnalysis() { var el = document.getElementById('tldAnalysis'); if (el) el.innerHTML = analysisHtml(describe(D.aid)); }
    function paintHead() { var el = document.getElementById('tldHead'); if (el) el.innerHTML = headHtml(describe(D.aid)); }
    function liveAnalysisHtml(A) {
        if (!A || !A.have) return '<div class="tld-empty">Waiting for live values...</div>';
        var finds = [];
        var rows = A.rows;
        rows.forEach(function (r) {
            var st = stOf(r), g = r.range, lab = r.alias || r.label;
            if (st === 'low') finds.push(['#E0A100', lab + ' ' + fmt(r.v) + ' is below its safe minimum ' + fmt(g.min) + '.']);
            else if (st === 'high') finds.push(['#E5484D', lab + ' ' + fmt(r.v) + ' is above its safe maximum ' + fmt(g.max) + '.']);
            else if (st === 'near') finds.push(['#E0A100', lab + ' ' + fmt(r.v) + ' is close to its ' + (r.v - g.min < g.max - r.v ? 'lower limit ' + fmt(g.min) : 'upper limit ' + fmt(g.max)) + '.']);
        });
        if (kind() === 'pm') pmFindings(A, finds);
        else if (kind() === 'signal') sigFindings(A, finds);
        else if (A.occ === false) finds.push(['#1F9D55', 'TPR picked up: the track circuit is clear.']);
        else if (A.occ === true) finds.push(['#E0A100', 'TPR dropped: the track is occupied (relay-end readings fall while a train is in the section).']);
        var f = findRow(A, 'IFMA'), ir = findRow(A, 'IRMA');
        if (f && ir && f.v !== null && ir.v !== null && A.occ === false) {
            var d = Math.abs(f.v - ir.v);
            finds.push([d > Math.max(20, f.v * 0.1) ? '#E0A100' : '#1F9D55', 'Feed / relay end currents ' + fmt(f.v) + ' vs ' + fmt(ir.v) + ' mA' +
                (d > Math.max(20, f.v * 0.1) ? ': imbalance of ' + fmt(d) + ' mA, check for a leakage path between the ends.' : ': balanced, no leakage path between the ends.')]);
        }
        var ib = findRow(A, 'IBALST');
        if (ib && ib.v !== null) finds.push([ib.v > 30 ? '#E0A100' : '#1F9D55', 'IBALST ' + fmt(ib.v) + ' mA: ballast leakage ' + (ib.v > 30 ? 'raised.' : 'negligible.')]);
        var outs = rows.filter(function (r) { var s = stOf(r); return s === 'low' || s === 'high'; }).length;
        var near = rows.filter(function (r) { return stOf(r) === 'near'; }).length;
        var C = 2 * Math.PI * 19, col = A.status === 'fault' ? '#E5484D' : A.status === 'watch' ? '#E0A100' : '#22A55B';
        var txt = { fault: 'Fault', watch: 'Watch', ok: 'Healthy', none: 'No data' }[A.status] || A.status;
        return '<div class="tld-an"><svg width="54" height="54" viewBox="0 0 48 48" aria-hidden="true"><circle cx="24" cy="24" r="19" fill="none" stroke="var(--e7-border,#E4E8EE)" stroke-width="5"></circle>' +
            '<circle cx="24" cy="24" r="19" fill="none" stroke="' + col + '" stroke-width="5" stroke-linecap="round" stroke-dasharray="' + (A.score / 100 * C).toFixed(1) + ' ' + C.toFixed(1) + '" transform="rotate(-90 24 24)"></circle>' +
            '<text x="24" y="29" text-anchor="middle" font-size="14" font-weight="800" fill="currentColor">' + A.score + '</text></svg>' +
            '<div><b style="color:' + col + '">' + esc(txt) + '</b><div class="tld-note" style="font-size:12.5px">' + rows.length + ' readings, ' + outs + ' outside safe range, ' + near + ' near a limit</div></div></div>' +
            '<ul class="tld-find">' + finds.map(function (x) { return '<li><i style="background:' + x[0] + '"></i><span>' + esc(x[1]) + '</span></li>'; }).join('') + '</ul>';
    }

    /* ------------------------------------------------------------------
       TRACK CIRCUIT (option 3+, paper / navy) with animated current flow
       ------------------------------------------------------------------ */
    function pal() {
        return isLight() ? {
            bg: '#FFFFFF', panel: '#FBF6EA', pedge: '#E3D9C3', ink: '#1F2A44', mut: '#6B7A8C', navy: '#2B3A67', red: '#C0392B', brown: '#7A4E2D',
            sleeper: '#C9CED6', card: '#FFFFFF', cedge: '#D5DBE3', val: '#0F1B2A', tljb: '#232838', tljbText: '#FFFFFF', chg: '#9AA5B1', chgText: '#1F2937',
            blue: '#2F6FD6', green: '#16A34A', amber: '#B47B00', hi: '#C8323A', flow: '#F59E0B', ac: '#A855F7', tag: '#FFFFFF', p24: '#FCA5A5', n24: '#93C5FD'
        } : {
            bg: '#0B1424', panel: '#122036', pedge: '#24324A', ink: '#E6EDF5', mut: '#9FB0C6', navy: '#8EA2D8', red: '#F05252', brown: '#C08457',
            sleeper: '#2A3550', card: '#0F1B2D', cedge: '#2A3B55', val: '#F1F5F9', tljb: '#38BDF8', tljbText: '#0B1424', chg: '#64748B', chgText: '#F1F5F9',
            blue: '#3B82F6', green: '#22C55E', amber: '#F5B70A', hi: '#FF5A4E', flow: '#FBBF24', ac: '#C084FC', tag: '#0F1B2D', p24: '#FECACA', n24: '#93C5FD'
        };
    }
    function ccard(P, A, key, fallback, x, y, w) {
        var r = findRow(A, key);
        var lab = baseLabel(r ? (r.alias || r.label) : fallback);
        var unit = unitOf(r ? (r.alias || r.label) : fallback);
        var v = r ? r.v : null, st = r ? stOf(r) : 'none';
        var calc = r && r.derived;
        var sc = { ok: P.green, near: P.amber, low: P.amber, high: P.hi, none: P.mut, occ: P.mut }[st] || P.mut;
        var vc = (st === 'ok' || st === 'none' || st === 'occ') ? P.val : sc;
        var s = '';
        if (st === 'low' || st === 'high') {
            s += '<rect x="' + (x - 2) + '" y="' + (y - 2) + '" width="' + (w + 4) + '" height="60" rx="8" fill="none" stroke="' + sc + '" stroke-width="4" filter="url(#tldGl)">' +
                '<animate attributeName="opacity" values=".9;.2;.9" dur="1.4s" repeatCount="indefinite"></animate></rect>';
        }
        s += '<g' + (r ? ' data-tld-k="' + esc(r.key) + '" style="cursor:pointer"' : '') + '><title>' + esc(lab + ': ' + fmt(v) + ' ' + unit + (r && r.range && r.range.min !== null ? ' | safe ' + fmt(r.range.min) + ' - ' + fmt(r.range.max) : calc ? ' | calculated' : '') + (r ? ' -- click for its trend' : '')) + '</title>' +
            '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="56" rx="7" fill="' + P.card + '" stroke="' + (r && D.sel && D.sel.key === r.key ? '#0A9AA8' : P.cedge) + '" stroke-width="' + (r && D.sel && D.sel.key === r.key ? 2.6 : 1.2) + '"></rect>' +
            '<rect x="' + x + '" y="' + y + '" width="4" height="56" rx="2" fill="' + sc + '"></rect>' +
            '<text x="' + (x + 11) + '" y="' + (y + 16) + '" font-size="10.5" font-weight="700" fill="' + P.mut + '">' + esc(lab) + '</text>' +
            '<text x="' + (x + 11) + '" y="' + (y + 37) + '" font-size="18" font-weight="800" fill="' + vc + '" font-family="JetBrains Mono, ui-monospace, monospace">' + fmt(v) +
            '<tspan font-size="11" font-weight="700" fill="' + P.mut + '" dx="4">' + esc(unit) + '</tspan></text>';
        if (st === 'low' || st === 'high' || st === 'near' || st === 'occ') {
            s += '<text x="' + (x + w - 8) + '" y="' + (y + 16) + '" text-anchor="end" font-size="9.5" font-weight="800" fill="' + sc + '" font-family="JetBrains Mono, monospace">' + (st === 'occ' ? 'OCC.' : st.toUpperCase()) + '</text>';
        }
        if (r && r.range && r.range.min !== null && r.range.max > r.range.min && v !== null) {
            var p = Math.max(0, Math.min(1, (v - r.range.min) / (r.range.max - r.range.min))), gx = x + 11, gw = w - 22;
            s += '<rect x="' + gx + '" y="' + (y + 45) + '" width="' + gw + '" height="4" rx="2" fill="url(#tldGg)"></rect>' +
                '<rect x="' + (gx + p * gw - 2).toFixed(1) + '" y="' + (y + 41) + '" width="4" height="11" rx="2" fill="' + vc + '"></rect>';
        } else if (calc) {
            s += '<text x="' + (x + w - 8) + '" y="' + (y + 51) + '" text-anchor="end" font-size="9" font-weight="700" fill="' + P.mut + '">calculated</text>';
        }
        return s + '</g>';
    }
    function flow(d, dur, col, w, dash, alt) {
        var anim = alt ? '<animate attributeName="stroke-dashoffset" values="0;-18;0" dur="' + dur + 's" repeatCount="indefinite"></animate>'
            : '<animate attributeName="stroke-dashoffset" from="0" to="-36" dur="' + dur + 's" repeatCount="indefinite"></animate>';
        return '<path d="' + d + '" fill="none" stroke="' + col + '" stroke-width="' + (w || 2.8) + '" stroke-dasharray="' + (dash || '7 11') + '" stroke-linecap="round">' + anim + '</path>';
    }
    function trackCircuitSvg(A) {
        var P = pal(), s = [], i, x;
        var a = function (t) { s.push(t); };
        a('<defs><linearGradient id="tldGg" x1="0" x2="1"><stop offset="0" stop-color="#E0A100" stop-opacity=".65"></stop><stop offset=".22" stop-color="#1F9D55" stop-opacity=".5"></stop>' +
            '<stop offset=".78" stop-color="#1F9D55" stop-opacity=".5"></stop><stop offset="1" stop-color="#E5484D" stop-opacity=".6"></stop></linearGradient>' +
            '<filter id="tldGl" x="-20%" y="-30%" width="140%" height="160%"><feGaussianBlur stdDeviation="3"></feGaussianBlur></filter>' +
            '<marker id="tldAr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10" fill="none" stroke="context-stroke" stroke-width="2"></path></marker></defs>');
        a('<rect width="1440" height="940" fill="' + P.bg + '"></rect>');
        for (x = 150; x < 1220; x += 46) a('<rect x="' + x + '" y="94" width="10" height="70" rx="2" fill="' + P.sleeper + '"></rect>');
        var occ = A && A.occ === true;
        a('<line x1="112" y1="108" x2="1288" y2="108" stroke="' + P.red + '" stroke-width="6"></line><line x1="235" y1="108" x2="1165" y2="108" stroke="' + (occ ? P.red : P.navy) + '" stroke-width="6"></line>');
        a('<line x1="112" y1="150" x2="1288" y2="150" stroke="' + P.red + '" stroke-width="6"></line>');
        a('<text x="677" y="88" text-anchor="middle" font-size="19" font-weight="800" fill="' + P.ink + '">Rail 1</text><text x="677" y="190" text-anchor="middle" font-size="19" font-weight="800" fill="' + P.ink + '">Rail 2</text>');
        a('<text x="272" y="52" text-anchor="middle" font-size="14" fill="' + P.mut + '">RE Bond</text><path d="M292 60L318 96" stroke="' + P.mut + '" stroke-width="1.4"></path>');
        a('<text x="883" y="52" text-anchor="middle" font-size="14" fill="' + P.mut + '">RE Bond</text><path d="M862 60L838 96" stroke="' + P.mut + '" stroke-width="1.4"></path>');
        for (i = 0; i < 3; i++) {
            var px = [142, 600, 1012][i], pw = [428, 382, 322][i];
            a('<rect x="' + px + '" y="240" width="' + pw + '" height="612" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect>');
        }
        a('<text x="1173" y="228" text-anchor="middle" font-size="19" font-weight="800" fill="' + P.ink + '">RELAY ROOM</text>');
        a('<text x="158" y="796" font-size="16" fill="' + P.ink + '">LOCATION BOX</text><text x="158" y="824" font-size="18" font-weight="800" fill="' + P.ink + '">LCBOXFEED</text>');
        a('<text x="616" y="828" font-size="16" fill="' + P.ink + '">LOCATION BOX: <tspan font-weight="800">LCBOXRELAY</tspan></text>');
        a('<path d="M292 108V196M322 150V196M842 108V196M872 150V196" stroke="' + P.navy + '" stroke-width="2.4"></path>');
        a('<path d="M292 224V590M322 224V590" stroke="' + P.red + '" stroke-width="2.6"></path>');
        a('<path d="M292 296l-12 7l24 7l-24 7l24 7l-24 7l24 7l-12 7" stroke="' + P.red + '" stroke-width="2.4" fill="' + P.panel + '"></path><text x="344" y="298" font-size="14" fill="' + P.ink + '">Choke</text>');
        a('<path d="M278 524h28M284 534h16" stroke="' + P.ink + '" stroke-width="3"></path><text x="316" y="532" font-size="14" fill="' + P.ink + '">Battery</text>');
        a('<path d="M277 624V690M277 718V910H1400" fill="none" stroke="' + P.navy + '" stroke-width="2.6" marker-end="url(#tldAr)"></path>');
        a('<path d="M344 624V690M344 718V872H1400" fill="none" stroke="' + P.brown + '" stroke-width="2.6" marker-end="url(#tldAr)"></path>');
        a('<text x="242" y="712" text-anchor="end" font-size="14" fill="' + P.ink + '">(N)</text><text x="360" y="712" font-size="14" fill="' + P.ink + '">(P) 110V AC</text>');
        a('<text x="1356" y="862" text-anchor="end" font-size="15" font-weight="800" fill="' + P.ink + '">Bx110V</text><text x="1356" y="900" text-anchor="end" font-size="15" font-weight="800" fill="' + P.ink + '">Nx110V</text><text x="800" y="898" text-anchor="middle" font-size="15" fill="' + P.ink + '">U/G CABLE</text>');
        a('<path d="M842 224V400M872 224V400" stroke="' + P.red + '" stroke-width="2.6"></path>');
        a('<path d="M920 440V488H662V764H1400" fill="none" stroke="' + P.red + '" stroke-width="2.6" marker-end="url(#tldAr)"></path>');
        a('<path d="M890 440V526H692V718H1400" fill="none" stroke="' + P.navy + '" stroke-width="2.6" marker-end="url(#tldAr)"></path>');
        a('<text x="678" y="474" font-size="14" fill="' + P.ink + '">24V DC FROM R/R</text>');
        a('<text x="1356" y="704" text-anchor="end" font-size="15" font-weight="800" fill="' + P.ink + '">N-24V</text><text x="1356" y="752" text-anchor="end" font-size="15" font-weight="800" fill="' + P.ink + '">B-24V</text>');
        /* TPR */
        var tCol = A && A.occ === true ? P.amber : P.green;
        var tUp = !(A && A.occ === true);
        a('<rect x="1082" y="268" width="182" height="56" rx="6" fill="' + P.card + '" stroke="' + tCol + '" stroke-width="2"></rect>');
        a('<circle cx="1230" cy="296" r="15" fill="' + tCol + '" opacity=".25"><animate attributeName="r" values="12;20;12" dur="1.8s" repeatCount="indefinite"></animate>' +
            '<animate attributeName="opacity" values=".35;0;.35" dur="1.8s" repeatCount="indefinite"></animate></circle>');
        a('<circle cx="1230" cy="296" r="12" fill="' + tCol + '"></circle><text x="1230" y="301" text-anchor="middle" font-size="14" font-weight="800" fill="#FFFFFF">' + (tUp ? '&#8593;' : '&#8595;') + '</text>' +
            '<text x="1140" y="304" text-anchor="middle" font-size="24" font-weight="800" fill="' + tCol + '">TPR</text>');
        a('<text x="1173" y="350" text-anchor="middle" font-size="15" font-weight="700" fill="' + tCol + '">' + (A && A.occ === true ? 'Drop &#183; track occupied' : A && A.occ === false ? 'Pickup &#183; track clear' : 'TPR state unknown') + '</text>');
        /* animated current: DC loop through the rails, 24 V out, 110 V AC */
        if (!occ) {
            a(flow('M322 590V150H872V400', 2.2, P.flow));
            a(flow('M842 400V108H292V590', 2.2, P.flow));
            a(flow('M920 440V488H662V764H1392', 1.6, P.p24, 2.2));
            a(flow('M890 440V526H692V718H1392', 1.6, P.n24, 2.2));
        } else {
            a(flow('M322 590V150H560', 2.2, P.flow));            /* train shunts the feed: no current reaches the relay end */
        }
        a(flow('M344 690V624M344 718V872H1392', 0.6, P.ac, 2.2, '5 7', true));
        a(flow('M277 690V624M277 718V910H1392', 0.6, P.ac, 2.2, '5 7', true));
        /* boxes above the animation */
        [262, 812].forEach(function (bx) {
            a('<rect x="' + bx + '" y="196" width="92" height="28" rx="3" fill="' + P.tljb + '"></rect><text x="' + (bx + 46) + '" y="215" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.tljbText + '">TLJB</text>');
        });
        a('<rect x="250" y="590" width="122" height="34" rx="3" fill="' + P.chg + '"></rect><text x="311" y="612" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.chgText + '">Charger</text>');
        a('<rect x="768" y="400" width="178" height="40" rx="3" fill="' + P.blue + '"></rect><text x="857" y="426" text-anchor="middle" font-size="17" font-weight="800" fill="#FFFFFF">Track Relay</text>');
        [277, 344].forEach(function (fx) { a('<rect x="' + (fx - 7) + '" y="690" width="14" height="28" rx="4" fill="' + P.panel + '" stroke="' + P.red + '" stroke-width="2.2"></rect>'); });
        var ib = findRow(A, 'IBALST');
        a('<rect x="592" y="117" width="170" height="26" rx="13" fill="' + P.tag + '" stroke="' + P.cedge + '"></rect><text x="677" y="135" text-anchor="middle" font-size="12" font-weight="700" fill="' + P.mut + '">IBALST ' +
            '<tspan font-family="JetBrains Mono, monospace" font-size="14" font-weight="800" fill="' + P.val + '">' + fmt(ib ? ib.v : null) + '</tspan> mA</text>');
        /* value cards */
        var C = [['RTCVARRES', 'RTC VAR RES(Ohm)', 148, 258, 128], ['VTCVARRES', 'VTC VAR RES(V)', 148, 320, 128], ['RTCCHFEEDEND', 'RTC CH FEED END(Ohm)', 148, 382, 128],
            ['ITCBATTCHARG', 'ITC BATT CHARG(mA)', 148, 444, 128], ['CHARGERMA', 'ITC TFC O/P(mA)', 148, 506, 128], ['CHARGERV', 'VTC TFC I/P(V)', 148, 640, 120],
            ['CHOKEV', 'VTC CH FEED END(V)', 392, 264, 170], ['IFMA', 'ITC FEED END(mA)', 392, 328, 170], ['VF', 'VTC FEED END(V)', 392, 392, 170], ['CHARGEROPV', 'VTC TFC O/P(V)', 392, 594, 170],
            ['VR', 'VTC RELAY END(V)', 612, 256, 180], ['IRMA', 'ITC RELAY END(mA)', 612, 318, 180], ['RRAIL', 'RRAIL(Ohm)', 612, 380, 148], ['VTCTR', 'VTC TR(V)', 886, 296, 90],
            ['TPRVLOC', 'VTC 24 DC LOC(V)', 740, 616, 180], ['TPRV', 'VTC 24 DC TPR I/P(V)', 1052, 616, 246]];
        C.forEach(function (c) { a(ccard(P, A, c[0], c[1], c[2], c[3], c[4])); });
        return '<svg viewBox="0 0 1440 940" width="100%" role="img" aria-label="Track circuit with live values and animated current" style="display:block">' + s.join('') + '</svg>';
    }
    /* v618.32 POINT MACHINE circuit (same style): relay room with B/N-24V
       (detection) and B/N-110V (operation), NWKR / RWKR / NWCR / RWCR, A and B
       end location boxes with their readings, end motors, last-operation
       table. Indication current flows on the picked-up KR line. */
    function relayTagSvg(P, x, y, name, up) {
        var col = up === true ? P.green : up === false ? '#CA8A04' : P.mut;
        var w = name.length * 9 + 54;
        return '<g><rect x="' + x + '" y="' + (y - 14) + '" width="' + w + '" height="28" rx="5" fill="' + P.card + '" stroke="' + col + '" stroke-width="1.8"></rect>' +
            '<text x="' + (x + 10) + '" y="' + (y + 5) + '" font-size="14" font-weight="800" fill="' + col + '">' + esc(name) + ' ' + (up === true ? '&#8593;' : up === false ? '&#8595;' : '') + '</text></g>';
    }
    function pmCircuitSvg(A) {
        var P = pal(), s = [];
        var a = function (t) { s.push(t); };
        var rl = {};
        ((A && A.relays) || []).forEach(function (r) { rl[norm(r.name).slice(-4)] = r.up; });
        var dir = (A && A.pmDir) || '';
        a('<defs><linearGradient id="tldGg" x1="0" x2="1"><stop offset="0" stop-color="#E0A100" stop-opacity=".65"></stop><stop offset=".22" stop-color="#1F9D55" stop-opacity=".5"></stop>' +
            '<stop offset=".78" stop-color="#1F9D55" stop-opacity=".5"></stop><stop offset="1" stop-color="#E5484D" stop-opacity=".6"></stop></linearGradient>' +
            '<filter id="tldGl" x="-20%" y="-30%" width="140%" height="160%"><feGaussianBlur stdDeviation="3"></feGaussianBlur></filter>' +
            '<marker id="tldAr" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto"><path d="M0 0L10 5L0 10" fill="none" stroke="context-stroke" stroke-width="2"></path></marker></defs>');
        a('<rect width="1440" height="900" fill="' + P.bg + '"></rect>');
        /* panels */
        a('<rect x="30" y="60" width="520" height="800" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect><text x="290" y="46" text-anchor="middle" font-size="19" font-weight="800" fill="' + P.ink + '">RELAY ROOM</text>');
        a('<rect x="600" y="440" width="380" height="420" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="790" y="468" text-anchor="middle" font-size="18" font-weight="800" fill="' + P.ink + '">A END</text><text x="790" y="488" text-anchor="middle" font-size="13" fill="' + P.mut + '">Location Box</text>');
        a('<rect x="1010" y="60" width="400" height="420" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="1210" y="88" text-anchor="middle" font-size="18" font-weight="800" fill="' + P.ink + '">B END</text><text x="1210" y="108" text-anchor="middle" font-size="13" fill="' + P.mut + '">Location Box</text>');
        /* supplies */
        a('<text x="48" y="112" font-size="15" font-weight="800" fill="' + P.ink + '">B-24V</text><path d="M48 120H1200V130" fill="none" stroke="' + P.red + '" stroke-width="2.6"></path>');
        a('<text x="48" y="152" font-size="15" font-weight="800" fill="' + P.ink + '">N-24V</text><path d="M48 160H1180V130" fill="none" stroke="' + P.navy + '" stroke-width="2.6"></path>');
        a('<text x="48" y="252" font-size="15" font-weight="800" fill="' + P.ink + '">B-110V</text><path d="M48 260H700V440M700 260H1300V480" fill="none" stroke="#E67E22" stroke-width="2.6"></path>');
        a('<text x="48" y="352" font-size="15" font-weight="800" fill="' + P.ink + '">N-110V</text><path d="M48 360H760V440M760 360H1340V480" fill="none" stroke="' + P.navy + '" stroke-width="2.6"></path>');
        /* detection (24 V) lines to the A end and relays */
        a('<path d="M560 520H600M560 600H600" stroke="' + P.navy + '" stroke-width="2.4"></path>');
        a('<path d="M330 120V520H600" fill="none" stroke="' + P.red + '" stroke-width="2.4"></path><path d="M360 160V600H600" fill="none" stroke="' + P.navy + '" stroke-width="2.4"></path>');
        if (rl.NWKR === true || rl.RWKR === true) {
            a(flow('M48 120H330V520H600', 1.4, P.flow));
            a(flow('M600 600H360V160H48', 1.4, P.n24, 2.4));
        }
        a(relayTagSvg(P, 80, 470, 'NWKR', rl.NWKR) + relayTagSvg(P, 80, 560, 'RWKR', rl.RWKR) + relayTagSvg(P, 120, 300, 'NWCR', rl.NWCR) + relayTagSvg(P, 300, 300, 'RWCR', rl.RWCR));
        a('<text x="48" y="420" font-size="13" fill="' + P.mut + '">Detection 24 V: NWKR / RWKR</text><text x="48" y="232" font-size="13" fill="' + P.mut + '">Operation 110 V: NWCR / RWCR</text>');
        /* motors */
        var motor = function (x, y, label) {
            return '<circle cx="' + x + '" cy="' + y + '" r="24" fill="' + P.card + '" stroke="' + P.ink + '" stroke-width="2.4"></circle><text x="' + x + '" y="' + (y + 7) + '" text-anchor="middle" font-size="20" font-weight="800" fill="' + P.ink + '">M</text>' +
                '<text x="' + x + '" y="' + (y + 46) + '" text-anchor="middle" font-size="13" font-weight="700" fill="' + P.ink + '">' + label + '</text>';
        };
        a(motor(1060, 560, "'A' END MOTOR") + motor(1360, 540, "'B' END MOTOR"));
        a('<path d="M980 560H1036M1210 480V540H1336" fill="none" stroke="' + P.ink + '" stroke-width="2" stroke-dasharray="5 4"></path>');
        /* value cards per end */
        var lbl = ['VPT 24 DC LOC N/R(V)', 'VPT N/R(V)', 'IPT N/R(A) Max', 'IPT N/R(A) Avg', 'VPT 110 DC LOC N/R(V)', 'TPT N/R(ms)'];
        lbl.forEach(function (l, i) {
            a(ccard(P, A, norm('A End ' + l), 'A ' + l, 616 + (i % 2) * 182, 506 + Math.floor(i / 2) * 70, 170));
            a(ccard(P, A, norm('B End ' + l), 'B ' + l, 1026 + (i % 2) * 190, 126 + Math.floor(i / 2) * 70, 178));
        });
        /* last operation table */
        var tx = 600, ty = 60, XS = [10, 46, 172, 250, 312], hd = ['End', 'IPT Max / Avg (A)', 'VPT 110', 'TPT ms', 'VPT 24'];
        a('<rect x="' + tx + '" y="' + ty + '" width="380" height="150" rx="6" fill="' + P.card + '" stroke="' + P.cedge + '"></rect>');
        a('<text x="' + (tx + 14) + '" y="' + (ty + 26) + '" font-size="14" font-weight="800" fill="' + P.ink + '">LAST OPERATION</text>');
        if (dir) a('<rect x="' + (tx + 250) + '" y="' + (ty + 10) + '" width="116" height="24" rx="12" fill="' + (dir === 'Reverse' ? '#CA8A04' : P.green) + '"></rect><text x="' + (tx + 308) + '" y="' + (ty + 27) + '" text-anchor="middle" font-size="13" font-weight="800" fill="#FFFFFF">' + esc(dir) + '</text>');
        hd.forEach(function (h2, i) { a('<text x="' + (tx + XS[i]) + '" y="' + (ty + 56) + '" font-size="11" font-weight="700" fill="' + P.mut + '">' + h2 + '</text>'); });
        ['A', 'B'].forEach(function (E, j) {
            var v = function (l) { var r = rowByLabel(A || { rows: [] }, E + ' End ' + l); return r ? fmt(r.v) : '--'; };
            var vals = [E, v('IPT N/R(A) Max') + ' / ' + v('IPT N/R(A) Avg'), v('VPT 110 DC LOC N/R(V)'), v('TPT N/R(ms)'), v('VPT 24 DC LOC N/R(V)')];
            var y = ty + 86 + j * 30;
            a('<line x1="' + (tx + 8) + '" x2="' + (tx + 372) + '" y1="' + (y - 18) + '" y2="' + (y - 18) + '" stroke="' + P.cedge + '"></line>');
            vals.forEach(function (t, i) { a('<text x="' + (tx + XS[i]) + '" y="' + y + '" font-size="12.5" font-weight="' + (i ? 700 : 800) + '" fill="' + P.val + '" font-family="JetBrains Mono, monospace">' + esc(t) + '</text>'); });
        });
        if (A && A.pmWhen && A.pmWhen !== '--') a('<text x="' + (tx + 14) + '" y="' + (ty + 142) + '" font-size="11" fill="' + P.mut + '">' + esc(A.pmWhen) + '</text>');
        return '<svg viewBox="0 0 1440 900" width="100%" role="img" aria-label="Point machine circuit with live values" style="display:block">' + s.join('') + '</svg>';
    }
    /* ======================================================================
       v618.33 SIGNAL circuits -- Main / Calling / Route tabs, Shunt for shunt
       signals; drawn from the live readings and DataLogger relays in the
       style of the existing signal circuit view (relay room, location box,
       signal unit), animated feed on lit lamps.
       ====================================================================== */
    function sigRow(A, name) {
        if (!A) return null;
        var k = norm(name), i, rr = A.rows.concat(A.derived || []);
        for (i = 0; i < rr.length; i++) if (norm(rr[i].alias) === k || norm(rr[i].label) === k || rr[i].key === k) return rr[i];
        return null;
    }
    function relayUp(A, name) {
        if (!A || !A.relays) return undefined;
        var k = norm(name);
        for (var i = 0; i < A.relays.length; i++) if (norm(A.relays[i].name) === k) return !!A.relays[i].up;
        return undefined;
    }
    function sval(A, name) { var r = sigRow(A, name); return r && r.v !== null && r.v !== undefined ? r.v : null; }
    function scard(P, A, name, x, y, w) {
        var r = sigRow(A, name);
        return ccard(P, A, r ? r.key : '#none', name, x, y, w);
    }
    function contact(P, A, x, y, name) {
        var up = relayUp(A, name), col = up === true ? P.green : up === false ? '#CA8A04' : P.mut;
        return '<text x="' + x + '" y="' + y + '" font-size="13" font-weight="800" fill="' + col + '">' + esc(name) + (up === true ? ' &#8593;' : up === false ? ' &#8595;' : '') + '</text>';
    }
    function ecrBox(P, A, x, y, name) {
        var up = relayUp(A, name), col = up === true ? P.green : up === false ? '#CA8A04' : P.mut;
        var w = name.length * 10 + 34;
        return '<rect x="' + x + '" y="' + (y - 15) + '" width="' + w + '" height="28" rx="3" fill="' + P.card + '" stroke="' + col + '" stroke-width="2"></rect>' +
            '<text x="' + (x + w / 2) + '" y="' + (y + 5) + '" text-anchor="middle" font-size="13" font-weight="800" fill="' + col + '">' + esc(name) + (up === true ? ' &#8593;' : up === false ? ' &#8595;' : '') + '</text>';
    }
    function bnLabel(P, x, y, txt) { return '<text x="' + x + '" y="' + y + '" font-size="13" font-weight="800" fill="' + P.ink + '">' + esc(txt) + '</text>'; }
    function arrowL(P, x1, x2, y, col) {
        return '<path d="M' + x1 + ' ' + y + 'H' + (x2 + 4) + '" stroke="' + col + '" stroke-width="2.6"></path><path d="M' + (x2 + 14) + ' ' + (y - 7) + 'L' + x2 + ' ' + y + 'L' + (x2 + 14) + ' ' + (y + 7) + '" fill="none" stroke="' + col + '" stroke-width="2.6"></path>';
    }
    function svgDefs() {
        return '<defs><linearGradient id="tldGg" x1="0" x2="1"><stop offset="0" stop-color="#E0A100" stop-opacity=".65"></stop><stop offset=".22" stop-color="#1F9D55" stop-opacity=".5"></stop>' +
            '<stop offset=".78" stop-color="#1F9D55" stop-opacity=".5"></stop><stop offset="1" stop-color="#E5484D" stop-opacity=".6"></stop></linearGradient>' +
            '<filter id="tldGl" x="-20%" y="-30%" width="140%" height="160%"><feGaussianBlur stdDeviation="3"></feGaussianBlur></filter></defs>';
    }
    var ASPECT_ORDER = ['HHG', 'DG', 'HG', 'GG', 'YG', 'RG'];
    var ASPECT_COL = { RG: '#EF4444', DG: '#22C55E', GG: '#22C55E', HG: '#F5B70A', HHG: '#F5B70A', YG: '#F5B70A' };
    var ASPECT_ECR = { HHG: 'HHECR', DG: 'DECR', GG: 'DECR', HG: 'HECR', YG: 'HECR', RG: 'RECR' };
    var ASPECT_PR = { HHG: 'VSIG HHPR', DG: 'VSIG DPR', HG: 'VSIG HPR', GG: 'VSIG DPR', YG: 'VSIG HPR', RG: 'VSIG RPR' };
    var ASPECT_CONTACTS = { HHG: ['HR', 'DR', 'HHR'], DG: ['HR', 'HHR', 'DR'], GG: ['HR', 'DR'], HG: ['HR', 'DR'], YG: ['HR'], RG: ['HR', 'DECR', 'HHECR', 'HECR'] };
    function aspectsOf(A) {
        var out = [];
        (A ? A.rows : []).forEach(function (r) {
            var m = /^\s*[IV]SIG\s+([A-Z]+)\s*$/i.exec(r.alias || r.label || '');
            if (m && !/PR$/i.test(m[1])) { var x = m[1].toUpperCase(); if (out.indexOf(x) === -1) out.push(x); }
        });
        out.sort(function (p, q) { var a = ASPECT_ORDER.indexOf(p), b = ASPECT_ORDER.indexOf(q); return (a < 0 ? 9 : a) - (b < 0 ? 9 : b); });
        return out;
    }
    function aspectLit(A, x) {
        var i = sval(A, 'ISIG ' + x), v = sval(A, 'VSIG ' + x);
        return (i !== null && i >= 30) || (v !== null && v >= 50);
    }
    function routesOf(A) {
        var out = [];
        (A ? A.rows : []).forEach(function (r) {
            var m = /^\s*[IV]ROSIG\s*\(\s*([A-Z])UG\s*\)\s*$/i.exec(r.alias || r.label || '');
            if (m && out.indexOf(m[1].toUpperCase()) === -1) out.push(m[1].toUpperCase());
        });
        ((A && A.relays) || []).forEach(function (r) {
            var m = /^([A-E])UHR$/i.exec(norm(r.name));
            if (m && out.indexOf(m[1].toUpperCase()) === -1) out.push(m[1].toUpperCase());
        });
        return out.sort();
    }
    function hasCalling(A) { return !!(sigRow(A, 'ICOSIG') || sigRow(A, 'VCOSIG') || relayUp(A, 'Co-HR') !== undefined); }
    function isShunt(A) { return !!(A && A.rows.some(function (r) { return /^\s*[IV]SHSIG/i.test(r.alias || r.label || ''); })); }
    function sigTabs(A) {
        if (isShunt(A)) return [['shunt', 'Shunt']];
        var t = [['main', 'Main']];
        if (hasCalling(A)) t.push(['calling', 'Calling']);
        if (routesOf(A).length) t.push(['route', 'Route']);
        return t;
    }
    function sigMainSvg(A) {
        var P = pal(), asp = aspectsOf(A), s = [svgDefs()], rowH = 170, top = 70;
        var H = top + Math.max(1, asp.length) * rowH + 40;
        var a = function (t) { s.push(t); };
        a('<rect width="1440" height="' + H + '" fill="' + P.bg + '"></rect>');
        a('<rect x="20" y="' + (top - 30) + '" width="690" height="' + (asp.length * rowH) + '" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect>');
        a('<text x="365" y="' + (top - 40) + '" text-anchor="middle" font-size="17" font-weight="800" fill="' + P.ink + '">RELAY ROOM</text>');
        a('<rect x="740" y="' + (top - 30) + '" width="200" height="' + (asp.length * rowH) + '" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect>');
        a('<text x="840" y="' + (top - 40) + '" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">LOCATION BOX</text>');
        a('<line x1="965" y1="' + (top - 50) + '" x2="965" y2="' + (H - 20) + '" stroke="' + P.ink + '" stroke-dasharray="4 4" stroke-width="1.6"></line><text x="975" y="' + (top - 40) + '" font-size="15" font-weight="800" fill="' + P.ink + '">SIGNAL UNIT</text>');
        var headH = asp.length * rowH - 20;
        a('<rect x="1010" y="' + (top - 30) + '" width="150" height="' + headH + '" rx="70" fill="' + P.tljb + '"></rect>');
        a('<rect x="1072" y="' + (top - 30 + headH) + '" width="26" height="40" fill="' + P.tljb + '"></rect>');
        asp.forEach(function (x, i) {
            var y = top + i * rowH, lit = aspectLit(A, x), col = ASPECT_COL[x] || '#F5B70A';
            a(bnLabel(P, 30, y - 8, 'Bx110V') + '<path d="M28 ' + y + 'H1085" stroke="' + P.red + '" stroke-width="2.6"></path>');
            a(bnLabel(P, 30, y + 62, 'Nx110V') + arrowL(P, 1085, 26, y + 70, P.navy));
            if (lit) {
                a(flow('M28 ' + y + 'H1060', 1.2, P.flow));
                a(flow('M1060 ' + (y + 70) + 'H40', 1.2, P.n24, 2.4));
            }
            (ASPECT_CONTACTS[x] || ['HR']).forEach(function (c, k) { a(contact(P, A, 110 + k * 92, y - 8, c)); });
            a(ecrBox(P, A, 540, y, ASPECT_ECR[x] || 'ECR'));
            a('<text x="630" y="' + (y + 40) + '" text-anchor="middle" font-size="12.5" font-weight="800" fill="' + (lit ? col : P.mut) + '">' + x + (lit ? ' &#183; lit' : ' &#183; off') + '</text>');
            var pr = ASPECT_PR[x];
            if (pr && sigRow(A, pr)) a(scard(P, A, pr, 752, y + 6, 176));
            else a('<text x="840" y="' + (y + 40) + '" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">' + esc((pr || '').replace('VSIG ', '')) + '</text>');
            var cy = y + 35;
            if (lit) a('<circle cx="1085" cy="' + cy + '" r="60" fill="' + col + '" opacity=".35" filter="url(#tldGl)"><animate attributeName="opacity" values=".45;.15;.45" dur="1.6s" repeatCount="indefinite"></animate></circle>');
            a('<circle cx="1085" cy="' + cy + '" r="52" fill="' + (lit ? col : (isLight() ? '#3A3F55' : '#2B3044')) + '"></circle>');
            a(scard(P, A, 'ISIG ' + x, 1190, y - 26, 230) + scard(P, A, 'VSIG ' + x, 1190, y + 36, 230));
        });
        if (!asp.length) a('<text x="720" y="140" text-anchor="middle" font-size="16" fill="' + P.mut + '">No ISIG / VSIG aspect readings for this signal.</text>');
        return '<svg viewBox="0 0 1440 ' + H + '" width="100%" role="img" aria-label="Signal main circuit" style="display:block">' + s.join('') + '</svg>';
    }
    function ledSvg(P, x, y, lit, colLit) {
        return '<rect x="' + (x - 22) + '" y="' + (y - 16) + '" width="20" height="32" rx="2" fill="' + P.card + '" stroke="' + P.ink + '" stroke-width="2"></rect>' +
            '<path d="M' + x + ' ' + (y - 18) + 'h12a18 18 0 0 1 0 36h-12z" fill="' + (lit ? colLit : (isLight() ? '#3A3F55' : '#2B3044')) + '" stroke="' + P.ink + '" stroke-width="1.6"></path>' +
            (lit ? '<circle cx="' + (x + 14) + '" cy="' + y + '" r="22" fill="' + colLit + '" opacity=".3" filter="url(#tldGl)"></circle>' : '');
    }
    function sigCallingSvg(A) {
        var P = pal(), s = [svgDefs()], a = function (t) { s.push(t); };
        var lit = (sval(A, 'ICOSIG') || 0) >= 30;
        a('<rect width="1440" height="420" fill="' + P.bg + '"></rect>');
        a('<rect x="20" y="60" width="700" height="300" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect><text x="370" y="48" text-anchor="middle" font-size="17" font-weight="800" fill="' + P.ink + '">RELAY ROOM &#183; CALLING-ON</text>');
        a('<rect x="760" y="60" width="240" height="300" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="880" y="48" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">LOCATION BOX</text>');
        a('<rect x="1040" y="60" width="380" height="300" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="1230" y="48" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">SIGNAL UNIT</text>');
        a(bnLabel(P, 32, 128, 'Bx110V(SIG-W)') + '<path d="M30 140H1120V190H1150" fill="none" stroke="' + P.red + '" stroke-width="2.6"></path>');
        a(bnLabel(P, 32, 268, 'NX110(SIG-W)') + '<path d="M30 280H1120V230H1150" fill="none" stroke="' + P.navy + '" stroke-width="2.6"></path>');
        if (lit) { a(flow('M30 140H1120V190H1150', 1.2, P.flow)); a(flow('M1150 230H1120V280H30', 1.2, P.n24, 2.4)); }
        a(contact(P, A, 230, 128, 'Co-HR') + contact(P, A, 230, 268, 'Co-HR') + ecrBox(P, A, 420, 280, 'Co-HECR'));
        a(scard(P, A, 'VCOSIG HPR', 776, 180, 208));
        a(ledSvg(P, 1180, 210, lit, '#F8FAFC') + '<text x="1180" y="160" text-anchor="middle" font-size="13" font-weight="800" fill="' + P.ink + '">LED LIT SIG</text>');
        a(scard(P, A, 'ICOSIG', 1240, 120, 170) + scard(P, A, 'VCOSIG', 1240, 250, 170));
        a('<text x="1180" y="345" text-anchor="middle" font-size="12" font-weight="700" fill="' + P.mut + '">CURRENT REGULATING UNIT</text>');
        return '<svg viewBox="0 0 1440 420" width="100%" role="img" aria-label="Signal calling-on circuit" style="display:block">' + s.join('') + '</svg>';
    }
    function sigRouteSvg(A) {
        var P = pal(), rs = routesOf(A), s = [svgDefs()], a = function (t) { s.push(t); }, bh = 190, top = 60;
        var H = top + rs.length * bh + 20;
        a('<rect width="1440" height="' + H + '" fill="' + P.bg + '"></rect>');
        rs.forEach(function (x, i) {
            var y = top + i * bh, lit = (sval(A, 'IROSIG (' + x + 'UG)') || 0) >= 10;
            a('<rect x="20" y="' + (y - 20) + '" width="560" height="' + (bh - 30) + '" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect>');
            a('<rect x="600" y="' + (y - 20) + '" width="220" height="' + (bh - 30) + '" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect>');
            a('<rect x="840" y="' + (y - 20) + '" width="580" height="' + (bh - 30) + '" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect>');
            a(bnLabel(P, 30, y + 8, 'Bx110V(SIG-E1)') + '<path d="M28 ' + (y + 20) + 'H880" stroke="' + P.red + '" stroke-width="2.6"></path>');
            a(bnLabel(P, 30, y + 88, 'NX110(SIG-E1)') + '<path d="M28 ' + (y + 100) + 'H880" stroke="' + P.navy + '" stroke-width="2.6"></path>');
            if (lit) { a(flow('M28 ' + (y + 20) + 'H880', 1.2, P.flow)); a(flow('M880 ' + (y + 100) + 'H28', 1.2, P.n24, 2.4)); }
            a(contact(P, A, 200, y + 10, x + 'UHR') + contact(P, A, 200, y + 90, x + 'UHR') + ecrBox(P, A, 330, y + 100, 'UECR'));
            a('<text x="560" y="' + (y + 60) + '" text-anchor="end" font-size="13" font-weight="800" fill="' + (lit ? P.green : P.mut) + '">ROUTE ' + x + (lit ? ' &#183; lit' : ' &#183; off') + '</text>');
            a(scard(P, A, 'VROSIG HPR (' + x + 'UPR)', 612, y + 32, 196));
            for (var k = 0; k < 5; k++) a(ledSvg(P, 880 + k * 74, y + 58, lit, '#F8FAFC'));
            a('<text x="1035" y="' + (y + 112) + '" text-anchor="middle" font-size="11.5" font-weight="700" fill="' + P.mut + '">LED LIT SIG &#183; CURRENT REGULATING UNIT</text>');
            a(scard(P, A, 'IROSIG (' + x + 'UG)', 1250, y - 6, 162) + scard(P, A, 'VROSIG (' + x + 'UG)', 1250, y + 58, 162));
        });
        if (!rs.length) a('<text x="720" y="100" text-anchor="middle" font-size="16" fill="' + P.mut + '">No route indicator on this signal.</text>');
        return '<svg viewBox="0 0 1440 ' + Math.max(H, 160) + '" width="100%" role="img" aria-label="Signal route indicator circuit" style="display:block">' + s.join('') + '</svg>';
    }
    function sigShuntSvg(A) {
        var P = pal(), s = [svgDefs()], a = function (t) { s.push(t); };
        var L = [['OFF', 'OFFECR'], ['ON', 'ONECR'], ['PILOT', null]];
        var H = 760;
        a('<rect width="1440" height="' + H + '" fill="' + P.bg + '"></rect>');
        a('<rect x="20" y="40" width="660" height="520" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '"></rect><text x="350" y="30" text-anchor="middle" font-size="17" font-weight="800" fill="' + P.ink + '">RELAY ROOM &#183; SHUNT</text>');
        a('<rect x="720" y="40" width="200" height="700" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="820" y="30" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">LOCATION BOX</text>');
        a('<rect x="960" y="40" width="460" height="520" rx="6" fill="' + P.panel + '" stroke="' + P.pedge + '" stroke-dasharray="6 4"></rect><text x="1190" y="30" text-anchor="middle" font-size="15" font-weight="800" fill="' + P.ink + '">SIGNAL UNIT</text>');
        L.forEach(function (l, i) {
            var y = 90 + i * 160, lit = (sval(A, 'ISHSIG ' + l[0]) || 0) >= 30;
            a(bnLabel(P, 30, y - 8, i === 0 ? 'BX110(SIG-W)' : '') + '<path d="M28 ' + y + 'H1080V' + (y + 20) + 'H1100" fill="none" stroke="' + P.red + '" stroke-width="2.6"></path>');
            a(bnLabel(P, 30, y + 62, i === 0 ? 'Nx110(SIG-W)' : '') + '<path d="M28 ' + (y + 70) + 'H1080V' + (y + 50) + 'H1100" fill="none" stroke="' + P.navy + '" stroke-width="2.6"></path>');
            if (lit) { a(flow('M28 ' + y + 'H1080V' + (y + 20) + 'H1100', 1.2, P.flow)); a(flow('M1100 ' + (y + 50) + 'H1080V' + (y + 70) + 'H28', 1.2, P.n24, 2.4)); }
            a(contact(P, A, 260, y - 8, 'HR') + '<text x="420" y="' + (y - 8) + '" font-size="12.5" font-weight="700" fill="' + P.mut + '">' + l[0] + '</text>' +
                '<text x="420" y="' + (y + 62) + '" font-size="12.5" font-weight="700" fill="' + P.mut + '">' + l[0] + 'N</text>');
            if (l[1]) a(ecrBox(P, A, 520, y + 70, l[1]));
            a(ledSvg(P, 1124, y + 35, lit, '#F8FAFC') + '<text x="1130" y="' + (y - 4) + '" text-anchor="middle" font-size="12.5" font-weight="800" fill="' + P.ink + '">LED LIT SIG &#183; ' + l[0] + '</text>');
            a(scard(P, A, 'ISHSIG ' + l[0], 1210, y - 10, 200) + scard(P, A, 'VSHSIG ' + l[0], 1210, y + 54, 200));
        });
        a(bnLabel(P, 30, 610, 'B24(EXT-W)') + '<path d="M28 620H730V650M28 700H730V680" fill="none" stroke="' + P.red + '" stroke-width="2.6"></path>' + bnLabel(P, 30, 690, 'B24(EXT-W)'));
        a(contact(P, A, 260, 610, 'HR') + contact(P, A, 260, 690, 'HR'));
        a(scard(P, A, 'VSHSIG HPR', 732, 640, 176));
        return '<svg viewBox="0 0 1440 ' + H + '" width="100%" role="img" aria-label="Shunt signal circuit" style="display:block">' + s.join('') + '</svg>';
    }
    function sigCircuitSvg(A) {
        var tabs = sigTabs(A), ids = tabs.map(function (t) { return t[0]; });
        if (ids.indexOf(D.sigTab) === -1) D.sigTab = ids[0];
        if (D.sigTab === 'shunt') return sigShuntSvg(A);
        if (D.sigTab === 'calling') return sigCallingSvg(A);
        if (D.sigTab === 'route') return sigRouteSvg(A);
        return sigMainSvg(A);
    }
    function sigTabsHtml(A) {
        var tabs = sigTabs(A);
        if (tabs.length < 2) return '<span class="tld-chip ok">Live</span>';
        return '<div style="display:flex;gap:4px" role="tablist">' + tabs.map(function (t) {
            return '<button type="button" role="tab" class="tld-btn sm" data-tld-stab="' + t[0] + '" aria-pressed="' + (D.sigTab === t[0]) + '">' + t[1] + '</button>';
        }).join('') + '</div>';
    }
    function sigHeadChips(A) {
        if (!A) return '';
        if (isShunt(A)) {
            var on = ['OFF', 'ON', 'PILOT'].filter(function (l) { return (sval(A, 'ISHSIG ' + l) || 0) >= 30; });
            return '<span class="tld-chip">' + (on.length ? 'Lit: ' + on.join(', ') : 'No lamp lit') + '</span>';
        }
        var lit = aspectsOf(A).filter(function (x) { return aspectLit(A, x); });
        if (!lit.length) return '<span class="tld-chip bad">No aspect lit</span>';
        return lit.map(function (x) { return '<span class="tld-chip ' + (x === 'RG' ? 'bad' : x === 'DG' || x === 'GG' ? 'ok' : 'warn') + '">Aspect ' + x + '</span>'; }).join('');
    }
    function sigFindings(A, finds) {
        if (isShunt(A)) {
            ['OFF', 'ON', 'PILOT'].forEach(function (l) {
                var i = sval(A, 'ISHSIG ' + l);
                if (i !== null) finds.push([i >= 30 ? '#1F9D55' : '#94A3B8', l + ' lamp ' + (i >= 30 ? 'lit (' + fmt(i) + ' mA)' : 'off') + '.']);
            });
            return;
        }
        var asp = aspectsOf(A), lit = asp.filter(function (x) { return aspectLit(A, x); });
        if (!lit.length && asp.length) finds.push(['#E5484D', 'No main aspect lit (all ISIG below 30 mA): check the lamps / feed.']);
        else if (lit.length > 1) finds.push(['#E5484D', 'More than one main aspect lit: ' + lit.join(', ') + '.']);
        lit.forEach(function (x) {
            var e = ASPECT_ECR[x], up = e ? relayUp(A, e) : undefined;
            finds.push(['#1F9D55', x + ' lit (ISIG ' + fmt(sval(A, 'ISIG ' + x)) + ' mA, VSIG ' + fmt(sval(A, 'VSIG ' + x)) + ' V).']);
            if (up === false) finds.push(['#E0A100', x + ' is lit but ' + e + ' is dropped: lamp proving does not match.']);
            else if (up === true) finds.push(['#1F9D55', e + ' picked up: lamp proved.']);
        });
        if (hasCalling(A)) {
            var c = sval(A, 'ICOSIG');
            finds.push([c !== null && c >= 30 ? '#F5B70A' : '#94A3B8', 'Calling-on ' + (c !== null && c >= 30 ? 'lit (' + fmt(c) + ' mA).' : 'off.')]);
        }
        var rs = routesOf(A).filter(function (x) { return (sval(A, 'IROSIG (' + x + 'UG)') || 0) >= 10; });
        if (routesOf(A).length) finds.push(['#94A3B8', 'Route indicator: ' + (rs.length ? 'route ' + rs.join(', ') + ' lit.' : 'all off.')]);
    }
    function circuitSvg(A) { var k = kind(); return k === 'pm' ? pmCircuitSvg(A) : k === 'signal' ? sigCircuitSvg(A) : trackCircuitSvg(A); }
    function circuitLegend() {
        if (kind() === 'signal') {
            var S2 = pal();
            return '<div class="tld-note" style="display:flex;flex-wrap:wrap;gap:16px;font-size:12px"><span><b style="color:' + S2.flow + '">- - &#8594;</b> lamp current on the lit aspect / indicator</span>' +
                '<span><b style="color:#16A34A">&#8593;</b> relay picked up</span><span><b style="color:#CA8A04">&#8595;</b> relay dropped</span><span>click a card for its trend</span></div>';
        }
        if (kind() === 'pm') {
            var Q = pal();
            return '<div class="tld-note" style="display:flex;flex-wrap:wrap;gap:16px;font-size:12px"><span><b style="color:' + Q.flow + '">- - &#8594;</b> 24 V indication current through the picked-up detection relay</span>' +
                '<span><b style="color:#16A34A">&#8593;</b> relay picked up</span><span><b style="color:#CA8A04">&#8595;</b> relay dropped</span><span>hover a card for its value, click for its trend</span></div>';
        }
        var P = pal();
        return '<div class="tld-note" style="display:flex;flex-wrap:wrap;gap:16px;font-size:12px">' +
            '<span><b style="color:' + P.flow + '">- - &#8594;</b> DC track current (feed &#8594; rail 2 &#8594; relay end, back on rail 1)</span>' +
            '<span><b style="color:' + P.n24 + '">- -</b><b style="color:' + P.p24 + '">- -</b> 24 V DC to relay room</span><span><b style="color:' + P.ac + '">&#8764;</b> 110 V AC</span>' +
            '<span>card stripe: green in range, amber near / low, red outside (glows)</span><span>hover a card for its safe range</span></div>';
    }

    /* ------------------------------------------------------------------
       PAST ALERTS (30 days, this asset)
       ------------------------------------------------------------------ */
    function ymd(ms) { var d = new Date(ms); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }
    function loadAlerts() {
        var aid = D.aid, name = (live()[aid] || {}).AssetName || '';
        var sid = siteId();
        if (!sid) { D.alerts = { err: 'Select a site first.' }; paintAlerts(); return; }
        D.alerts = null;
        paintAlerts();
        var now = Date.now();
        $.ajax({
            url: ALERTS_URL, type: 'POST', dataType: 'json', contentType: 'application/x-www-form-urlencoded',
            data: $.param({ SearchCriteria: { AlertStatus: 0, AlertTypeId: 0, SiteIds: [sid], FromDate: ymd(now - 30 * 86400000), ToDate: ymd(now) }, Pager: { Skip: 0, PageSize: 100 } }),
            success: function (resp) {
                if (D.aid !== aid) return;
                var list = ((resp && resp.alerts) || []).filter(function (x) {
                    return String(x.assetId) === String(aid) || (name && norm(x.assetName) === norm(name));
                }).sort(function (p, q) { return String(q.setTime).localeCompare(String(p.setTime)); });
                D.alerts = { list: list };
                paintAlerts();
            },
            error: function (x) { if (D.aid === aid) { D.alerts = { err: 'Alerts could not be loaded (HTTP ' + (x && x.status) + ').' }; paintAlerts(); } }
        });
    }
    function durText(a) {
        if (!a.resetTime) return 'open';
        var ms = new Date(a.resetTime) - new Date(a.setTime);
        if (isNaN(ms) || ms < 0) return '--';
        var m = Math.round(ms / 60000);
        return m < 60 ? m + ' min' : (m / 60).toFixed(1) + ' h';
    }
    function alertsHtml() {
        var S = D.alerts;
        if (!S) return '<div class="tld-empty">Loading alerts...</div>';
        if (S.err) return '<div class="tld-empty">' + esc(S.err) + '</div>';
        var all = S.list, isF = function (a) { return String(a.alertTypeId) === '2'; };
        var cnt = { all: all.length, f: all.filter(isF).length, p: all.filter(function (a) { return !isF(a); }).length, o: all.filter(function (a) { return !a.resetTime; }).length };
        var fl = D.alertFilter;
        var shown = all.filter(function (a) { return fl === 'all' || (fl === 'f' && isF(a)) || (fl === 'p' && !isF(a)) || (fl === 'o' && !a.resetTime); });
        var chips = [['all', 'All', cnt.all], ['f', 'Failure', cnt.f], ['p', 'Predictive', cnt.p], ['o', 'Open', cnt.o]].map(function (c) {
            return '<button type="button" data-tld-af="' + c[0] + '" aria-pressed="' + (fl === c[0]) + '">' + c[1] + ' <span>' + c[2] + '</span></button>';
        }).join('');
        var h = '<div class="tld-fil">' + chips + '<span style="flex:1"></span><span class="tld-note">last 30 days</span></div>';
        if (!shown.length) return h + '<div class="tld-empty">No alerts for this asset in the last 30 days.</div>';
        h += '<div class="tld-tbl"><table><thead><tr><th scope="col">Time</th><th scope="col">Type</th><th scope="col">Cause</th><th scope="col">Duration</th><th scope="col">Status</th><th scope="col"><span style="position:absolute;left:-9999px">Action</span></th></tr></thead><tbody>';
        shown.slice(0, 200).forEach(function (a, i) {
            var f = isF(a);
            h += '<tr class="' + (f ? 'f' : 'p') + '"><td class="t">' + esc(String(a.setTime || '').replace('T', ' ').slice(0, 16)) + '</td>' +
                '<td><b style="color:' + (f ? '#C8323A' : '#B47B00') + '">' + esc(a.alertType || (f ? 'Failure' : 'Predictive')) + '</b></td>' +
                '<td>' + esc(a.causeCode || a.description || '--') + '</td><td class="t">' + esc(durText(a)) + '</td>' +
                '<td><span class="tld-chip ' + (a.resetTime ? 'ok' : 'warn') + '">' + (a.resetTime ? 'Cleared' : 'Open') + (a.acknowledged ? ' &#183; ack' : '') + '</span></td>' +
                '<td><button type="button" class="tld-btn sm" data-tld-replay="' + esc(a.setTime) + '|' + esc(a.resetTime || '') + '">Replay</button></td></tr>';
        });
        return h + '</tbody></table></div><div class="tld-note">From the alert lister (GetSipReplayAlerts). Replay opens the SIP replay around the alert.</div>';
    }
    function paintAlerts() { var el = document.getElementById('tldAlerts'); if (el) el.innerHTML = alertsHtml(); }

    /* ------------------------------------------------------------------
       GRAPH: every reading, own scale, step lines, shared time axis
       ------------------------------------------------------------------ */
    function histStamp(ms) {
        var d = new Date(ms), p2 = function (n) { return (n < 10 ? '0' : '') + n; };
        return p2(d.getDate()) + p2(d.getMonth() + 1) + d.getFullYear() + '_' + p2(d.getHours()) + p2(d.getMinutes()) + p2(d.getSeconds());
    }
    function loadGraph() {
        var aid = D.aid, hours = D.graphHours, now = Date.now();
        D.graph = { loading: true };
        paintGraph();
        $.ajax({
            url: HIST_URL, type: 'GET', dataType: 'json',
            data: { assetId: aid, startDate: histStamp(now - hours * 3600000), endDate: histStamp(now), page: 1, pageSize: 5000, sort: 'asc', fillGaps: 'true', cursor: '' },
            success: function (resp) {
                if (D.aid !== aid) return;
                var pl = resp || {}, inner = pl.Data || pl.data;
                if (inner && (inner.columns || inner.Columns)) pl = inner;
                var cols = (pl.columns || pl.Columns || []).map(function (c) {
                    return { name: String(c.name || c.Name || ''), dl: String(c.dataType || c.DataType || '').toLowerCase() === 'datalogger',
                        skip: (c.attrId === null || c.attrId === undefined) && (c.AttrId === null || c.AttrId === undefined) };
                });
                var rows = (pl.rows || pl.Rows || []).map(function (r) { return { t: new Date(r.ts || r.Ts).getTime(), v: r.v || r.V || [] }; })
                    .filter(function (r) { return !isNaN(r.t); }).sort(function (p, q) { return p.t - q.t; });
                D.graph = { cols: cols, rows: rows, t0: now - hours * 3600000, t1: now };
                paintGraph();
                paintTrend();
            },
            error: function (x) { if (D.aid === aid) { D.graph = { err: 'History could not be loaded (HTTP ' + (x && x.status) + ').' }; paintGraph(); } }
        });
    }
    function graphHtml() {
        var G = D.graph;
        if (!G || G.loading) return '<div class="tld-empty">Loading history...</div>';
        if (G.err) return '<div class="tld-empty">' + esc(G.err) + '</div>';
        if (!G.rows) return '<div class="tld-empty">Loading history...</div>';
        if (!G.rows.length) return '<div class="tld-empty">No history in this period.</div>';
        var idx = [];
        G.cols.forEach(function (c, i) { if (!c.skip) idx.push(i); });
        var w = 960, LH = 40, GAP = 8, top = 6, lab = 190, H = top + idx.length * (LH + GAP) + 24;
        var gw = w - lab - 14, span = (G.t1 - G.t0) || 1;
        var X = function (t) { return lab + Math.max(0, Math.min(1, (t - G.t0) / span)) * gw; };
        var cls = W.TlHealthView && W.TlHealthView.classify;
        var out = '<svg viewBox="0 0 ' + w + ' ' + H + '" width="100%" role="img" aria-label="Every reading of this asset over the selected period" style="display:block">';
        idx.forEach(function (ci, k) {
            var c = G.cols[ci], y0 = top + k * (LH + GAP), pts = [];
            G.rows.forEach(function (r) { var v = parseFloat(r.v[ci]); if (!isNaN(v)) pts.push([r.t, v]); });
            out += '<rect x="' + lab + '" y="' + y0 + '" width="' + gw + '" height="' + LH + '" fill="' + (k % 2 ? 'rgba(127,140,160,.07)' : 'rgba(127,140,160,.02)') + '"></rect>';
            var last = pts.length ? pts[pts.length - 1][1] : null;
            var rg = (cls && last !== null && !c.dl) ? (cls(D.aid, c.name, last) || {}).range : null;
            var st = (cls && last !== null && !c.dl) ? (cls(D.aid, c.name, last) || {}).st : 'ok';
            var vcol = st === 'high' ? '#C8323A' : st === 'low' ? '#B47B00' : 'currentColor';
            out += '<text x="0" y="' + (y0 + 15) + '" font-size="11" fill="var(--e7-text-2,#4A5868)">' + esc(c.name) + '</text>' +
                '<text x="0" y="' + (y0 + 32) + '" font-size="13" font-weight="800" fill="' + vcol + '" font-family="JetBrains Mono, monospace">' +
                (c.dl ? (last === null ? '--' : last >= 0.5 ? 'Pickup' : 'Drop') : fmt(last)) + '</text>';
            if (pts.length < 1) return;
            var lo = Infinity, hi = -Infinity;
            pts.forEach(function (p) { if (p[1] < lo) lo = p[1]; if (p[1] > hi) hi = p[1]; });
            if (rg && rg.min !== null && rg.max !== null) { lo = Math.min(lo, rg.min); hi = Math.max(hi, rg.max); }
            if (hi === lo) { hi += 1; lo -= 1; }
            var pad = (hi - lo) * 0.08; lo -= pad; hi += pad;
            var Y = function (v) { return y0 + LH - ((v - lo) / (hi - lo)) * LH; };
            if (rg && rg.min !== null && rg.max !== null) out += '<rect x="' + lab + '" y="' + Y(rg.max).toFixed(1) + '" width="' + gw + '" height="' + (Y(rg.min) - Y(rg.max)).toFixed(1) + '" fill="rgba(31,157,85,.12)"></rect>';
            var d = 'M' + X(pts[0][0]).toFixed(1) + ' ' + Y(pts[0][1]).toFixed(1);
            for (var i = 1; i < pts.length; i++) d += 'L' + X(pts[i][0]).toFixed(1) + ' ' + Y(pts[i - 1][1]).toFixed(1) + 'L' + X(pts[i][0]).toFixed(1) + ' ' + Y(pts[i][1]).toFixed(1);
            d += 'L' + X(G.t1).toFixed(1) + ' ' + Y(pts[pts.length - 1][1]).toFixed(1);
            out += '<path d="' + d + '" fill="none" stroke="' + (c.dl ? '#7C3AED' : '#0A7C86') + '" stroke-width="1.4"></path>' +
                '<text x="' + (lab - 4) + '" y="' + (y0 + 9) + '" text-anchor="end" font-size="8.5" fill="var(--e7-text-3,#8492A6)">' + (c.dl ? 'P' : fmt(hi - pad)) + '</text>' +
                '<text x="' + (lab - 4) + '" y="' + (y0 + LH) + '" text-anchor="end" font-size="8.5" fill="var(--e7-text-3,#8492A6)">' + (c.dl ? 'D' : fmt(lo + pad)) + '</text>';
        });
        for (var t = 0; t <= 8; t++) {
            var tx = lab + t * gw / 8, dt = new Date(G.t0 + span * t / 8);
            var lb = D.graphHours > 48 ? (dt.getDate() + '/' + (dt.getMonth() + 1)) : ('0' + dt.getHours()).slice(-2) + ':' + ('0' + dt.getMinutes()).slice(-2);
            out += '<line x1="' + tx.toFixed(1) + '" x2="' + tx.toFixed(1) + '" y1="' + top + '" y2="' + (H - 20) + '" stroke="rgba(127,140,160,.25)"></line>' +
                '<text x="' + tx.toFixed(1) + '" y="' + (H - 6) + '" text-anchor="middle" font-size="9.5" fill="var(--e7-text-3,#8492A6)">' + lb + '</text>';
        }
        return out + '</svg><div class="tld-note">Own scale per reading, green band = safe range, purple = relay (P = Pickup, D = Drop), time axis shared by all lanes.</div>';
    }
    function paintGraph() { var el = document.getElementById('tldGraph'); if (el) el.innerHTML = graphHtml(); }

    /* ------------------------------------------------------------------
       ASK AI  (POST /FRS25/ChatBot/Chat, SSE frames {type:"text"|"tool_use"|"error"})
       ------------------------------------------------------------------ */
    function contextText(A) {
        var a = live()[D.aid] || {};
        var parts = ['Asset: ' + ({ pm: 'POINT MACHINE ', signal: 'SIGNAL ' }[kind()] || 'TRACK ') + (a.AssetName || D.aid) + ' (assetId ' + D.aid + ', siteId ' + siteId() + ').'];
        if (A) {
            parts.push('Health ' + A.score + ' (' + A.status + ')' + (kind() === 'signal' ? '' : kind() === 'pm' ? ', last operation ' + (A.pmDir || 'unknown') + (A.pmWhen ? ' at ' + A.pmWhen : '') :
                ', TPR ' + (A.occ === true ? 'dropped (occupied)' : A.occ === false ? 'picked up (clear)' : 'unknown')) + '.');
            var RC = D.roster[D.aid];
            if (RC && RC.items && RC.items.length) parts.push('On today\'s maintenance roster: ' + RC.items.map(function (x) { return F(x, ['Priority', 'priority'], '') + ' - ' + F(x, ['Reason', 'reason'], ''); }).join('; ') + '.');
            else if (RC && RC.items) parts.push('Not on today\'s maintenance roster.');
            var HC = D.health[D.aid];
            if (HC && HC.data && HC.data.health) parts.push('Asset Health API: ' + HC.data.health.score + ' ' + (HC.data.health.label || '') + ' -- ' + (HC.data.health.headline || '') + '.');
            if (A.relays && A.relays.length) parts.push('Relays: ' + A.relays.map(function (r) { return r.name + ' ' + (r.up ? 'pickup' : 'drop'); }).join(', ') + '.');
            parts.push('Live values: ' + A.rows.map(function (r) {
                return (r.alias || r.label) + ' ' + fmt(r.v) + (r.range && r.range.min !== null ? ' (safe ' + fmt(r.range.min) + '-' + fmt(r.range.max) + ')' : '');
            }).join('; ') + '.');
            if (A.derived && A.derived.length) parts.push('Calculated: ' + A.derived.map(function (r) { return (r.alias || r.label) + ' ' + fmt(r.v); }).join('; ') + '.');
        }
        return parts.join(' ');
    }
    /* v618.37 ASK AI -- E7MRIWeb ChatBot client (Views/ChatBot 1.5.0.0) in the "Drawer final" layout:
       POST /FRS25/ChatBot/Chat {messages, pageContext} -> SSE {type:text|tool_use|error}, then [DONE].
       Markdown + plan cards, tool status line, Stop (abort), Copy / Retry, session-ended detection,
       out-of-scope guard, last 20 turns sent, full-screen toggle. */
    var CHAT_MAX_TURNS = 20, CHAT_MAX_CHARS = 500;
    var OUT_OF_SCOPE = ['weather', 'recipe', 'cook', 'poem', 'joke', 'capital of', 'javascript', 'python', '2+2', 'math', 'history of india',
        'news', 'stock', 'bitcoin', 'translate', 'who is', 'president', 'cricket', 'football', 'movie', 'song', 'write a story', 'write an essay', 'email', 'time in'];
    function isOutOfScope(t) { var l = String(t).toLowerCase(); return OUT_OF_SCOPE.some(function (k) { return l.indexOf(k) !== -1; }); }
    var TOOL_LABELS = {
        search_sites: 'Searching sites', search_assets: 'Searching assets', search_tags: 'Searching tags', get_asset_tags: 'Loading tags',
        get_tag_current: 'Reading live value', get_live_values: 'Reading live values', state_get: 'Getting state', get_history: 'Loading history',
        get_history_compact: 'Loading history', get_limited_history: 'Loading recent readings', get_timestamps: 'Reading timestamps',
        get_alerts: 'Checking alerts', get_alert_history: 'Loading alert history', alert_smart: 'Summarising alerts', summary_get: 'Calculating summary', trend_get: 'Analysing trend'
    };
    var CARD_FIELDS = { '15-DAY': '15-day record', 'ML': 'Model classification', 'LASTOP': 'Last operation', 'ACTION': 'Action', 'NOTE': 'Note' };
    function chatState() { return D.chat[D.aid] || (D.chat[D.aid] = { msgs: [], busy: false }); }
    function planCards(raw, stash) {
        if (!raw || raw.indexOf('[[CARD]]') < 0) return raw;
        return raw.replace(/\[\[CARD\]\]([\s\S]*?)\[\[\/CARD\]\]/g, function (_, body) {
            var lines = body.split('\n').map(function (l) { return l.replace(/^\s*[-*•]\s?/, '').trim(); }).filter(function (l) { return l.length; });
            if (!lines.length) return '';
            var head = {};
            lines[0].split('|').forEach(function (p) { var kv = p.split('='); if (kv.length >= 2) head[kv[0].trim().toLowerCase()] = kv.slice(1).join('=').trim(); });
            var pr = (head.priority || 'MONITOR').toUpperCase();
            var cls = pr.indexOf('URGENT') >= 0 ? 'urgent' : (pr.indexOf('INSPECT') >= 0 ? 'soon' : 'monitor');
            var rows = '';
            for (var i = 1; i < lines.length; i++) {
                var c = lines[i].indexOf(':');
                if (c < 0) { rows += '<div class="pc-row"><div class="pc-v">' + esc(lines[i]) + '</div></div>'; continue; }
                var key = lines[i].substring(0, c).trim().toUpperCase(), val = lines[i].substring(c + 1).trim();
                rows += '<div class="pc-row' + (key === 'NOTE' ? ' pc-note' : key === 'ACTION' ? ' pc-action' : '') + '"><div class="pc-k">' +
                    esc(CARD_FIELDS[key] || (key.charAt(0) + key.slice(1).toLowerCase())) + '</div><div class="pc-v">' + esc(val) + '</div></div>';
            }
            var sub = [head.type, head.site].filter(Boolean).map(esc).join(' &middot; ');
            stash.push('<div class="tld-pc ' + cls + '"><div class="pc-head"><span class="pc-pill">' + esc(pr) + '</span><span class="pc-asset">' + esc(head.asset || 'Asset') + '</span>' +
                (sub ? '<span class="pc-sub">' + sub + '</span>' : '') + '</div><div class="pc-body">' + rows + '</div></div>');
            return '\n\u0000CARD' + (stash.length - 1) + '\u0000\n';
        });
    }
    /* renderMarkdown of E7MRIWeb ChatBot 1.5.0.0 (escape first, then headings, lists, tables, code) */
    function md(raw) {
        if (!raw) return '';
        var stash = [];
        raw = planCards(String(raw), stash);
        var h = esc(raw);
        h = h.replace(/```[\w]*\n?([\s\S]*?)```/g, function (_, c) { return '<pre><code>' + c.trim() + '</code></pre>'; });
        h = h.replace(/`([^`\n]+)`/g, '<code>$1</code>');
        h = h.replace(/^\s{0,3}####\s+(.+?)\s*#*$/gm, '<h4>$1</h4>');
        h = h.replace(/^\s{0,3}###\s+(.+?)\s*#*$/gm, '<h3>$1</h3>');
        h = h.replace(/^\s{0,3}##?\s+(.+?)\s*#*$/gm, '<h2>$1</h2>');
        h = h.replace(/^\s*(?:---|\*\*\*|___)\s*$/gm, '<hr>');
        h = h.replace(/^\s*&gt;\s+(.+)$/gm, '<blockquote>$1</blockquote>');
        h = h.replace(/\*\*([^*\n]+)\*\*/g, '<b>$1</b>');
        h = h.replace(/\*([^*\n]+)\*/g, '<em>$1</em>');
        h = h.replace(/((\|[^\n]+\|\n?)+)/g, function (m) {
            var rows = m.trim().split('\n').filter(function (r) { return r.trim(); });
            if (rows.length < 2) return m;
            var t = '<div class="tld-mdt"><table>';
            rows.forEach(function (row, i) {
                if (/^\|[\s\-:|]+\|$/.test(row.trim())) return;
                var cells = row.split('|').filter(function (c, ci, a) { return ci > 0 && ci < a.length - 1; });
                var tag = i === 0 ? 'th' : 'td';
                t += '<tr>' + cells.map(function (c) { return '<' + tag + '>' + c.trim() + '</' + tag + '>'; }).join('') + '</tr>';
            });
            return t + '</table></div>';
        });
        h = h.replace(/^\s*\d+[.)] (.+)$/gm, '<li data-ol="1">$1</li>');
        h = h.replace(/^\s*[•\-\*] (.+)$/gm, '<li>$1</li>');
        h = h.replace(/(?:<li data-ol="1">[\s\S]*?<\/li>\n?)+/g, function (m) { return '<ol>' + m.replace(/ data-ol="1"/g, ' class="oli"').replace(/\n/g, '') + '</ol>'; });
        h = h.replace(/(?:<li>[\s\S]*?<\/li>\n?)+/g, function (m) { return '<ul>' + m.replace(/\n/g, '') + '</ul>'; });
        h = h.replace(/\n{2,}/g, '\n');
        h = h.replace(/\n(?!\s*<\/?(?:h2|h3|h4|ul|ol|li|pre|table|tr|hr|blockquote|div))/g, '<br>');
        h = h.replace(/\n/g, '');
        h = h.replace(/(<br>\s*)+(<(?:h2|h3|h4|ul|ol|pre|div|hr|blockquote))/g, '$2');
        h = h.replace(/(<\/(?:h2|h3|h4|ul|ol|pre|div|blockquote)>)(\s*<br>)+/g, '$1');
        if (stash.length) h = h.replace(/(<br>\s*)*\u0000CARD(\d+)\u0000(\s*<br>)*/g, function (_, a, i) { return stash[+i] || ''; });
        return h;
    }
    function nextAssetName() {
        var ids = cardOrder(), i = ids.indexOf(String(D.aid)), id = i >= 0 && i < ids.length - 1 ? ids[i + 1] : i > 0 ? ids[i - 1] : null;
        return id ? ((live()[id] || {}).AssetName || id) : '';
    }
    function suggestions(name) {
        var k = kind(), nx = nextAssetName();
        var s = ['Explain today\'s health score'];
        if (nx) s.push('Compare with ' + nx);
        s.push(k === 'pm' ? 'How did the last operations go?' : k === 'signal' ? 'Which aspects were lit in the last 24 h?' : 'What changed in last 24 h?');
        if (k === 'track') s.push('Any glued joint alerts in the last 30 days?');
        return s;
    }
    var ICON_SEND = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14M13 6l6 6-6 6"></path></svg>';
    var ICON_STOP = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="6" width="12" height="12" rx="2"></rect></svg>';
    var ICON_COPY = '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="11" height="11" rx="2"></rect><path d="M5 15V6a2 2 0 0 1 2-2h9"></path></svg>';
    var ICON_RETRY = '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 11a8 8 0 1 0-2.3 5.7"></path><path d="M20 4v7h-7"></path></svg>';
    function botHtml(m, i, last) {
        var C = chatState(), tn = m.tool ? String(m.tool).replace(/\s*\(srv\d+\)$/, '') : '';
        var body = m.content ? md(m.content) : (m.streaming ? '<span class="tld-dots" aria-label="Thinking"><i></i><i></i><i></i></span>' : '');
        var h = '<div class="tld-ma' + (m.err && !m.content ? ' err' : '') + '" id="tldBot' + i + '">' +
            (m.tool ? '<div class="tld-tool"><span class="tld-spin"></span>' + esc(TOOL_LABELS[tn] || tn) + '...</div>' : '') +
            '<div class="tld-mdb">' + body + (m.streaming && m.content ? '<span class="tld-caret" aria-hidden="true"></span>' : '') + '</div>';
        if (m.err) h += '<div class="tld-merr" role="alert">' + esc(m.err) + '</div>';
        if (m.note) h += '<div class="tld-note">' + esc(m.note) + '</div>';
        if (!m.streaming && m.content) {
            h += '<div class="tld-macts"><button type="button" data-tld-copy="' + i + '" aria-label="Copy answer">' + ICON_COPY + '<span>Copy</span></button>' +
                (last && !C.busy ? '<button type="button" data-tld-retry="1" aria-label="Regenerate answer">' + ICON_RETRY + '<span>Retry</span></button>' : '') + '</div>';
        }
        return h + '</div>';
    }
    function msgsHtml() {
        var C = chatState(), name = (live()[D.aid] || {}).AssetName || D.aid;
        /* v618.38: Analysis is the default content inside Ask AI; the conversation replaces it once you ask. */
        if (!C.msgs.length) return '<div class="tld-defan">' +
            '<div class="tld-sh"><h3>Analysis</h3><button type="button" class="tld-btn sm" data-tld-hrefresh="1" title="Ask the Asset Health API again (skips its 10-minute cache)">&#8635; Refresh</button></div>' +
            '<div id="tldAnalysis">' + analysisHtml(describe(D.aid)) + '</div>' +
            '<div class="tld-note" style="margin-top:10px">Ask anything about ' + esc(name) + ' below &#8212; its live values, history, alerts or health. ' +
            'The assistant gets this asset\'s live values with your first question and looks up history and alerts itself.</div></div>';
        return C.msgs.map(function (m, i) {
            return m.role === 'user' ? '<div class="tld-mu">' + esc(m.show || m.content) + '</div>' : botHtml(m, i, i === C.msgs.length - 1);
        }).join('');
    }
    function chatHtml() {
        var C = chatState(), name = (live()[D.aid] || {}).AssetName || D.aid;
        var sugg = suggestions(name).map(function (q) { return '<button type="button" data-tld-q="' + esc(q) + '"' + (C.busy ? ' disabled' : '') + '>' + esc(q) + '</button>'; }).join('');
        return '<div class="tld-chat"><div class="tld-ctx">Context: <span>' + esc(name) + ' live values</span><span>24 h history</span><span>30 d alerts</span></div>' +
            '<div class="tld-msgs" id="tldMsgs">' + msgsHtml() + '</div><div class="tld-sugg" id="tldSugg">' + sugg + '</div>' +
            '<div class="tld-in"><label for="tldAsk" style="position:absolute;left:-9999px">Ask about ' + esc(name) + '</label>' +
            '<textarea id="tldAsk" rows="2" maxlength="' + CHAT_MAX_CHARS + '" placeholder="Ask about ' + esc(name) + ' ..."></textarea>' +
            '<button type="button" class="tld-send' + (C.busy ? ' stop' : '') + '" id="tldSend" aria-label="' + (C.busy ? 'Stop generating' : 'Send') + '">' + (C.busy ? ICON_STOP : ICON_SEND) + '</button></div>' +
            '<div class="tld-note">' + (C.busy ? 'Working... press the stop button to cancel.' : 'Enter to send, Shift+Enter for a new line. Answers are AI-generated: check them against the live values.') + '</div></div>';
    }
    function nearBottom(box) { return !box || box.scrollHeight - box.scrollTop - box.clientHeight < 80; }
    /* full repaint keeps the typed text; streaming only repaints the last bubble */
    function paintChat() {
        var el = document.getElementById('tldChat');
        if (!el) return;
        var inp = document.getElementById('tldAsk'), val = inp ? inp.value : '', foc = inp && document.activeElement === inp;
        el.innerHTML = chatHtml();
        var ni = document.getElementById('tldAsk');
        if (ni) { ni.value = val; if (foc) ni.focus(); }
        var box = document.getElementById('tldMsgs');
        if (box) box.scrollTop = box.scrollHeight;
    }
    function paintBot(aid, i) {
        if (D.aid !== aid) return;
        var C = D.chat[aid], el = document.getElementById('tldBot' + i), box = document.getElementById('tldMsgs');
        if (!el || !C || !C.msgs[i]) { paintChat(); return; }
        var stick = nearBottom(box);
        el.outerHTML = botHtml(C.msgs[i], i, i === C.msgs.length - 1);
        if (stick && box) box.scrollTop = box.scrollHeight;
    }
    function pageContext(A) {
        var a = live()[D.aid] || {}, H = D.health[D.aid];
        var pc = { page: 'Telemetry Live - asset dashboard', asset: a.AssetName || D.aid, assetId: D.aid, assetType: { pm: 'Point machine', signal: 'Signal' }[kind()] || 'Track circuit',
            site: ($('#drpSite option:selected').text() || '').trim(), siteId: siteId() };
        if (A && A.have) pc.liveHealth = A.score + ' (' + A.status + ')';
        if (H && H.data && H.data.health) pc.healthApi = H.data.health.score + ' ' + (H.data.health.label || '');
        if (kind() === 'track' && A && A.occ !== null && A.occ !== undefined) pc.tpr = A.occ ? 'dropped (occupied)' : 'picked up (clear)';
        if (kind() === 'pm' && A && A.pmDir) pc.lastOperation = A.pmDir + (A.pmWhen ? ' at ' + A.pmWhen : '');
        return pc;
    }
    function ask(q, regen) {
        var C = chatState(), aid = D.aid;
        if (C.busy) return;
        if (!regen) {
            q = String(q || '').trim().slice(0, CHAT_MAX_CHARS);
            if (!q) return;
            if (isOutOfScope(q)) {
                C.msgs.push({ role: 'user', content: q, show: q, local: true });
                C.msgs.push({ role: 'assistant', content: 'I am not trained to answer that. I can only assist with RDPMS railway monitoring data -- asset values, history, alerts and timestamps.', local: true });
                paintChat();
                return;
            }
            var first = !C.msgs.some(function (m) { return m.role === 'user' && !m.local; });
            C.msgs.push({ role: 'user', content: first ? ('[Context] ' + contextText(describe(aid)) + '\n\n' + q) : q, show: q });
        }
        var bot = { role: 'assistant', content: '', streaming: true };
        C.msgs.push(bot);
        var bi = C.msgs.length - 1;
        C.busy = true;
        paintChat();
        var history = C.msgs.filter(function (m) { return m !== bot && !m.local && (m.role === 'user' || (m.content && !m.err)); })
            .slice(-CHAT_MAX_TURNS * 2).map(function (m) { return { role: m.role, content: m.content }; });
        var done = function () {
            bot.streaming = false; bot.tool = null; C.busy = false; C.ctrl = null;
            if (!bot.content && !bot.err) bot.note = bot.stopped ? 'Stopped before any answer arrived.' : 'No answer was returned. Try rephrasing the question.';
            else if (bot.stopped) bot.note = 'Stopped before the answer was complete.';
            if (D.aid === aid) paintChat();
        };
        if (!W.fetch || !W.TextDecoder) { bot.err = 'This browser cannot stream the AI answer.'; done(); return; }
        C.ctrl = W.AbortController ? new AbortController() : null;
        var rq = null;
        /* timer, not requestAnimationFrame: rAF stops in a hidden tab and the answer would only appear at the end */
        var render = function () { if (rq) return; rq = setTimeout(function () { rq = null; paintBot(aid, bi); }, 60); };
        W.fetch(CHAT_URL, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, signal: C.ctrl ? C.ctrl.signal : undefined,
            body: JSON.stringify({ messages: history, pageContext: pageContext(describe(aid)) }) })
            .then(function (res) {
                var ct = (res.headers.get('content-type') || '').toLowerCase();
                if (res.status === 401 || res.redirected || ct.indexOf('text/html') >= 0) throw { session: true };
                if (!res.ok) throw new Error('The assistant could not answer (server error ' + res.status + '). Please try again.');
                var reader = res.body.getReader(), dec = new TextDecoder(), buf = '';
                function pump() {
                    return reader.read().then(function (r) {
                        if (r.done) return;
                        buf += dec.decode(r.value, { stream: true });
                        var lines = buf.split('\n');
                        buf = lines.pop();
                        lines.forEach(function (line) {
                            if (line.indexOf('data: ') !== 0) return;
                            var raw = line.slice(6).trim();
                            if (raw === '[DONE]') return;
                            var ev; try { ev = JSON.parse(raw); } catch (e) { return; }
                            if (ev.type === 'text') { bot.content += ev.text || ''; bot.tool = null; }
                            else if (ev.type === 'tool_use') bot.tool = ev.name || 'Working';
                            else if (ev.type === 'error') bot.err = ev.message || 'Error';
                        });
                        render();
                        return pump();
                    });
                }
                return pump();
            })
            .catch(function (e) {
                if (e && e.name === 'AbortError') bot.stopped = true;
                else if (e && e.session) { bot.content = ''; bot.err = 'Your session has ended. Sign in to RDPMS again, then ask your question.'; }
                else bot.err = (e && e.message) || 'Connection error -- please try again.';
            })
            .then(done);
    }
    function regenerate() {
        var C = chatState();
        if (C.busy) return;
        if (C.msgs.length && C.msgs[C.msgs.length - 1].role === 'assistant') C.msgs.pop();
        if (!C.msgs.length || C.msgs[C.msgs.length - 1].role !== 'user') { paintChat(); return; }
        ask(null, true);
    }
    function copyText(text, btn) {
        var lab = btn.querySelector('span') || btn;
        var ok = function () { var t = lab.textContent; lab.textContent = 'Copied'; setTimeout(function () { lab.textContent = t; }, 1400); };
        var fallback = function () {
            var ta = document.createElement('textarea'); ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
            document.body.appendChild(ta); ta.select();
            try { document.execCommand('copy'); ok(); } catch (e) { /* ignore */ }
            ta.remove();
        };
        if (navigator.clipboard && W.isSecureContext) navigator.clipboard.writeText(text).then(ok, fallback); else fallback();
    }
    function setChatFull(on) {
        D.chatFull = !!on;
        var s = document.getElementById('tldChatSec'), b = document.querySelector('[data-tld-chatfull]');
        if (s) s.classList.toggle('full', D.chatFull);
        if (b) { b.setAttribute('aria-pressed', String(D.chatFull)); b.setAttribute('aria-label', D.chatFull ? 'Exit full screen' : 'Expand to full screen'); }
        var box = document.getElementById('tldMsgs'); if (box) box.scrollTop = box.scrollHeight;
    }

    /* ------------------------------------------------------------------
       DRAWER
       ------------------------------------------------------------------ */
    function cardOrder() {
        var ids = [];
        $('#atCardView .at-asset-card[data-id]').each(function () { ids.push(String($(this).attr('data-id'))); });
        if (!ids.length) ids = Object.keys(live());
        return ids;
    }
    function headHtml(A) {
        var a = live()[D.aid] || {};
        var ids = cardOrder(), i = ids.indexOf(String(D.aid));
        var prev = i > 0 ? ids[i - 1] : null, next = i >= 0 && i < ids.length - 1 ? ids[i + 1] : null;
        var nm = function (id) { return (live()[id] || {}).AssetName || id; };
        var st = A ? A.status : 'none';
        var cls = st === 'fault' ? 'bad' : st === 'watch' ? 'warn' : st === 'ok' ? 'ok' : '';
        var stTxt = { fault: 'Fault', watch: 'Watch', ok: 'Healthy', none: 'No data' }[st] || st;
        var HH = D.health[D.aid];
        if (HH && HH.data && HH.data.health) {
            var hc = String(HH.data.health.color || '').toLowerCase();
            cls = hc === 'red' ? 'bad' : hc === 'amber' ? 'warn' : hc === 'green' ? 'ok' : cls;
            stTxt = HH.data.health.label || stTxt;
            A = { have: true, score: HH.data.health.score, status: st, occ: A ? A.occ : null, pmDir: A && A.pmDir, pmWhen: A && A.pmWhen, rows: A ? A.rows : [], relays: A ? A.relays : [] };
        }
        var ts = a.lastUpdated ? (typeof W.fmtTime === 'function' ? W.fmtTime(a.lastUpdated) : new Date(a.lastUpdated).toLocaleTimeString()) : '--';
        var icon = kind() === 'signal' ? '<rect x="7" y="2" width="10" height="15" rx="5"></rect><circle cx="12" cy="6.5" r="1.6"></circle><circle cx="12" cy="11.5" r="1.6"></circle><path d="M12 17v5M8 22h8"></path>'
            : kind() === 'pm' ? '<path d="M3 18h18M6 18L18 6M14 6h4v4"></path>' : '<path d="M3 8h18M3 16h18M6 6v12M10 6v12M14 6v12M18 6v12"></path>';
        return '<span class="tld-ico"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round">' + icon + '</svg></span>' +
            '<div style="flex:1;min-width:0"><div class="tld-title">' + ({ pm: 'POINT : ', signal: 'SIGNAL : ' }[kind()] || 'TRACK : ') + esc(a.AssetName || D.aid) + '</div><div class="tld-chips">' +
            '<span class="tld-chip ' + cls + '">' + esc(stTxt) + (A && A.have ? ' ' + A.score : '') + '</span>' +
            (kind() === 'signal' ? sigHeadChips(A) : kind() === 'pm' ? (A && A.pmDir ? '<span class="tld-chip ' + (A.pmDir === 'Reverse' ? 'warn' : 'ok') + '">' + esc(A.pmDir) + (A.pmWhen && A.pmWhen !== '--' ? ' &#183; ' + esc(A.pmWhen) : '') + '</span>' : '') :
            (A && A.occ !== null && A.occ !== undefined ? '<span class="tld-chip ' + (A.occ ? 'warn' : 'ok') + '">TPR ' + (A.occ ? '&#8595; Drop' : '&#8593; Pickup') + '</span>' : '')) +
            '<span class="tld-chip">Live &#183; ' + esc(ts) + '</span></div></div>' +
            (prev ? '<button type="button" class="tld-btn" data-tld-go="' + esc(prev) + '">&#8592; ' + esc(nm(prev)) + '</button>' : '') +
            (next ? '<button type="button" class="tld-btn" data-tld-go="' + esc(next) + '">' + esc(nm(next)) + ' &#8594;</button>' : '') +
            '<button type="button" class="tld-btn tld-x" data-tld-close="1" aria-label="Close">&times;</button>';
    }
    function sec(title, body, extra, id, fill, secId) {
        return '<section class="tld-sec' + (fill ? ' fill' : '') + '"' + (secId ? ' id="' + secId + '"' : '') + '><div class="tld-sh"><h3>' + title + '</h3>' + (extra || '') + '</div><div' + (id ? ' id="' + id + '"' : '') + '>' + body + '</div></section>';
    }
    function shell() {
        var A = describe(D.aid);
        var periods = [[1, '1 h'], [24, '24 h'], [168, '7 d']].map(function (p) {
            return '<button type="button" class="tld-btn sm" data-tld-h="' + p[0] + '" aria-pressed="' + (D.graphHours === p[0]) + '">' + p[1] + '</button>';
        }).join(' ');
        /* v618.38: in-drawer section nav (SPA-style jump) -- replaces Open in SIP / Full circuit view */
        var navItems = [['tldSecValues', 'Live values'], ['tldChatSec', 'Ask AI'], ['tldSecAlerts', 'Alerts'], ['tldSecCircuit', 'Circuit']];
        if (kind() === 'track') navItems.push(['tldSecShort', 'Track shorting']);
        else if (kind() === 'pm') navItems.push(['tldSecPattern', 'Pattern analysis']);
        navItems.push(['tldSecRoster', 'Maintenance roster'], ['tldSecGraph', 'Graph']);
        var navHtml = navItems.map(function (n, i) {
            return '<button type="button" class="tld-navbtn" data-tld-nav="' + n[0] + '" aria-pressed="' + (i === 0) + '">' + n[1] + '</button>';
        }).join('');
        return '<div class="tld-head" id="tldHead">' + headHtml(A) + '</div>' +
            '<nav class="tld-actions" aria-label="Sections">' + navHtml +
            '<span class="tld-navgap"></span>' +
            '<button type="button" class="tld-ib" data-tld-act="csv" aria-label="Export CSV" title="Export CSV"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12M8 11l4 4 4-4M5 21h14"></path></svg></button></nav>' +
            '<div class="tld-body">' +
            '<div class="tld-row"><div class="tld-col" id="tldSecValues">' + sec('Live values', valuesHtml(A), '', 'tldValues') + '</div>' +
            '<div class="tld-col"><section class="tld-sec fill tld-chatsec' + (D.chatFull ? ' full' : '') + '" id="tldChatSec"><div class="tld-sh"><h3>Ask AI</h3>' +
            '<button type="button" class="tld-ib" data-tld-chatclear="1" aria-label="New chat" title="New chat"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 5v14M5 12h14"></path></svg></button>' +
            '<button type="button" class="tld-ib" data-tld-chatfull="1" aria-pressed="' + !!D.chatFull + '" aria-label="' + (D.chatFull ? 'Exit full screen' : 'Expand to full screen') + '" title="Full screen"><svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 9V4h5M20 9V4h-5M4 15v5h5M20 15v5h-5"></path></svg></button></div>' +
            '<div id="tldChat" style="display:flex;flex-direction:column;flex:1;min-height:0">' + chatHtml() + '</div></section></div></div>' +
            sec('Circuit &#183; live', '<div id="tldCircuit">' + circuitSvg(A) + '</div>' + circuitLegend(), kind() === 'signal' ? '<span id="tldSigTabs">' + sigTabsHtml(A) + '</span>' : '<span class="tld-chip ok">Live</span>', null, false, 'tldSecCircuit') +
            sec('Past alerts &#183; 30 days', alertsHtml(), '', 'tldAlerts', false, 'tldSecAlerts') +
            sec('Maintenance roster &#183; today', rosterHtml(), '<button type="button" class="tld-btn sm" data-tld-rrefresh="1">&#8635; Refresh</button>', 'tldRoster', false, 'tldSecRoster') +
            (kind() === 'signal' ? '' : kind() === 'pm' ? sec('Operation pattern analysis', '<div class="tld-pend"><b>Pattern analysis API pending.</b> This section will run the point-machine pattern checks on the recent operations and show the result: ' +
                'overall verdict (normal / watch / fault, confidence), each pattern checked (e.g. obstruction, friction / slagging, creeping, A / B end mismatch, slow throw, detection or indication low) with its status and the evidence (operations, current curve, values), and the operations that matched.</div>', '', null, false, 'tldSecPattern') :
            sec('Track shorting (leakage) report', '<div id="tldShort" style="display:flex;flex-direction:column;gap:8px">' + shortHtml() + '</div><div id="tldShortPlots" style="margin-top:8px">' + shortPlotsHtml() + '</div>',
                '<div style="display:flex;gap:4px">' + shortDaysHtml() + '</div>', null, false, 'tldSecShort')) +
            sec('Graph &#183; every reading', graphHtml(), '<div style="display:flex;gap:4px">' + periods + '</div>', 'tldGraph', false, 'tldSecGraph') +
            '</div>';
    }
    function setActiveNav(active) {
        var btns = document.querySelectorAll('[data-tld-nav]');
        for (var i = 0; i < btns.length; i++) btns[i].setAttribute('aria-pressed', String(btns[i] === active));
    }
    /* v618.38 scroll-spy: highlight the nav item for the section nearest the top of the body */
    function navSpy() {
        var body = document.querySelector('#tldDrawer .tld-body');
        var btns = document.querySelectorAll('[data-tld-nav]');
        if (!body || !btns.length) return;
        var ref = body.getBoundingClientRect().top + 24, active = btns[0], activeTop = -Infinity;
        for (var i = 0; i < btns.length; i++) {
            var el = document.getElementById(btns[i].getAttribute('data-tld-nav'));
            if (!el) continue;
            var top = el.getBoundingClientRect().top;
            if (top <= ref && top > activeTop) { activeTop = top; active = btns[i]; }
        }
        setActiveNav(active);
    }
    function liveRefresh() {
        if (!D.aid || !document.getElementById('tldDrawer')) return;
        var A = describe(D.aid);
        var sig = A ? (A.rows.map(function (r) { return r.v; }).join(',') + '|' + (A.derived || []).map(function (r) { return r.v; }).join(',') + '|' + A.occ + A.status + A.score + (A.pmDir || '') + (A.pmWhen || '') +
            (A.relays || []).map(function (r) { return r.up ? 1 : 0; }).join('') + '|' + isLight()) : 'none';
        var head = document.getElementById('tldHead');
        if (head) head.innerHTML = headHtml(A);
        if (sig === D.liveSig) return;
        D.liveSig = sig;
        var v = document.getElementById('tldValues'); if (v) v.innerHTML = valuesHtml(A);
        var an = document.getElementById('tldAnalysis'); if (an) an.innerHTML = analysisHtml(A);
        var c = document.getElementById('tldCircuit'); if (c) c.innerHTML = circuitSvg(A);
    }
    function exportCsv() {
        var A = describe(D.aid), a = live()[D.aid] || {};
        if (!A) return;
        var lines = ['Asset,Reading,Value,Safe min,Safe max,Status'];
        A.rows.concat(A.derived || []).forEach(function (r) {
            var g = r.range || {};
            lines.push([a.AssetName || D.aid, r.alias || r.label, r.v === null ? '' : r.v, g.min == null ? '' : g.min, g.max == null ? '' : g.max, r.derived ? 'calculated' : stOf(r)]
                .map(function (x) { return '"' + String(x).replace(/"/g, '""') + '"'; }).join(','));
        });
        var blob = new Blob([lines.join('\r\n')], { type: 'text/csv' });
        var link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = (a.AssetName || D.aid) + '_live_values.csv';
        document.body.appendChild(link);
        link.click();
        setTimeout(function () { URL.revokeObjectURL(link.href); link.remove(); }, 500);
    }
    function openSip() {
        var name = (live()[D.aid] || {}).AssetName;
        close();
        var card = document.getElementById('sipCardSection');
        if (card && card.scrollIntoView) card.scrollIntoView({ behavior: 'smooth', block: 'start' });
        if (name && W.SipTelemetry && typeof W.SipTelemetry.highlight === 'function') setTimeout(function () { W.SipTelemetry.highlight(name, true); }, 400);
    }
    function replay(setTime, resetTime) {
        var name = (live()[D.aid] || {}).AssetName;
        var t0 = new Date(setTime).getTime(), t1 = resetTime ? new Date(resetTime).getTime() : t0 + 30 * 60000;
        if (isNaN(t0)) return;
        close();
        var card = document.getElementById('sipCardSection');
        if (card && card.scrollIntoView) card.scrollIntoView({ behavior: 'smooth', block: 'start' });
        if (W.SipReplay && typeof W.SipReplay.replayAt === 'function') W.SipReplay.replayAt(t0 - 5 * 60000, Math.max(t1, t0 + 60000) + 5 * 60000, name);
    }
    function onClick(e) {
        var t = e.target;
        if (!t.closest) return;
        if (t.closest('[data-tld-close]')) { close(); return; }
        var go = t.closest('[data-tld-go]'); if (go) { open(go.getAttribute('data-tld-go')); return; }
        var nav = t.closest('[data-tld-nav]');
        if (nav) {
            var sect = document.getElementById(nav.getAttribute('data-tld-nav'));
            if (sect && sect.scrollIntoView) sect.scrollIntoView({ behavior: 'smooth', block: 'start' });
            setActiveNav(nav);
            return;
        }
        var act = t.closest('[data-tld-act]');
        if (act) {
            var k = act.getAttribute('data-tld-act');
            if (k === 'csv') exportCsv();
            return;
        }
        if (t.closest('[data-tld-unsel]')) { D.sel = null; D.liveSig = ''; liveRefresh(); return; }
        if (t.closest('[data-tld-tograph]')) {
            var gsec = document.getElementById('tldGraph');
            if (gsec && gsec.scrollIntoView) gsec.scrollIntoView({ behavior: 'smooth', block: 'start' });
            return;
        }
        var kk = t.closest('[data-tld-k]');
        if (kk) {
            var key = kk.getAttribute('data-tld-k');
            D.sel = (D.sel && D.sel.key === key) ? null : { key: key, label: kk.getAttribute('data-tld-l') || '' };
            D.liveSig = '';
            liveRefresh();
            if (D.sel) {
                var tr = document.getElementById('tldTrend');
                if (tr && tr.scrollIntoView) tr.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
            return;
        }
        if (t.closest('[data-tld-hrefresh]')) { loadHealth(true); return; }
        if (t.closest('[data-tld-rrefresh]')) { loadRoster(true); return; }
        if (t.closest('[data-tld-sai]')) { shortAi(); return; }
        if (t.closest('[data-tld-splots]')) { loadShortPlots(); return; }
        if (t.closest('[data-tld-srefresh]')) { loadShort(true); return; }
        var sd = t.closest('[data-tld-sd]');
        if (sd) {
            D.shortDays = +sd.getAttribute('data-tld-sd') || 7;
            var sb = document.querySelectorAll('[data-tld-sd]');
            for (var j = 0; j < sb.length; j++) sb[j].setAttribute('aria-pressed', String(sb[j] === sd));
            loadShort(false);
            return;
        }
        var stb = t.closest('[data-tld-stab]');
        if (stb) {
            D.sigTab = stb.getAttribute('data-tld-stab');
            var A2 = describe(D.aid), cc = document.getElementById('tldCircuit'), tb = document.getElementById('tldSigTabs');
            if (cc) cc.innerHTML = circuitSvg(A2);
            if (tb) tb.innerHTML = sigTabsHtml(A2);
            return;
        }
        var af = t.closest('[data-tld-af]'); if (af) { D.alertFilter = af.getAttribute('data-tld-af'); paintAlerts(); return; }
        var rp = t.closest('[data-tld-replay]'); if (rp) { var p = rp.getAttribute('data-tld-replay').split('|'); replay(p[0], p[1]); return; }
        var hb = t.closest('[data-tld-h]'); if (hb) {
            D.graphHours = +hb.getAttribute('data-tld-h') || 24;
            var bs = document.querySelectorAll('[data-tld-h]');
            for (var i = 0; i < bs.length; i++) bs[i].setAttribute('aria-pressed', String(bs[i] === hb));
            loadGraph();
            return;
        }
        var q = t.closest('[data-tld-q]'); if (q) { ask(q.getAttribute('data-tld-q')); return; }
        if (t.closest('[data-tld-chatfull]')) { setChatFull(!D.chatFull); return; }
        if (t.closest('[data-tld-chatclear]')) { var CC = chatState(); if (!CC.busy) { CC.msgs = []; paintChat(); } return; }
        if (t.closest('[data-tld-retry]')) { regenerate(); return; }
        var cp = t.closest('[data-tld-copy]');
        if (cp) { var cm = chatState().msgs[+cp.getAttribute('data-tld-copy')]; if (cm) copyText(cm.content, cp); return; }
        if (t.closest('#tldSend')) {
            var CS = chatState();
            if (CS.busy) { if (CS.ctrl) CS.ctrl.abort(); return; }
            var inp = document.getElementById('tldAsk'); if (inp) { var v = inp.value; inp.value = ''; ask(v); }
        }
    }
    function onKey(e) {
        if (e.key === 'Escape' && D.chatFull) { setChatFull(false); return; }
        if (e.key === 'Escape' && D.aid) { close(); return; }
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing && e.target && e.target.id === 'tldAsk') {
            e.preventDefault();
            if (chatState().busy) return;
            var v = e.target.value; e.target.value = ''; ask(v);
        }
    }
    function open(aid) {
        if (!aid) return;
        ensureCss();
        D.aid = String(aid);
        D.liveSig = '';
        D.alertFilter = 'all';
        D.sel = null;
        D.sigTab = null;
        var ov = document.getElementById('tldOverlay'), dr = document.getElementById('tldDrawer');
        if (!ov) {
            ov = document.createElement('div'); ov.id = 'tldOverlay'; ov.className = 'tld-ov';
            ov.addEventListener('click', close);
            document.body.appendChild(ov);
        }
        if (!dr) {
            dr = document.createElement('aside'); dr.id = 'tldDrawer'; dr.className = 'tld';
            dr.setAttribute('role', 'dialog'); dr.setAttribute('aria-modal', 'true'); dr.setAttribute('aria-label', 'Asset dashboard');
            dr.addEventListener('click', onClick);
            dr.addEventListener('mousemove', trendHover);
            document.body.appendChild(dr);
            document.addEventListener('keydown', onKey);
        }
        dr.innerHTML = shell();
        var bodyEl = dr.querySelector('.tld-body');
        if (bodyEl) bodyEl.addEventListener('scroll', navSpy, { passive: true });
        D.liveSig = '';
        liveRefresh();
        loadAlerts();
        loadRoster(false);
        loadShort(false);
        var HS = D.health[D.aid];
        if (!HS || (!HS.loading && (!HS.data || Date.now() - (HS.at || 0) > 600000))) loadHealth(false);
        loadGraph();
        if (!D.timer) D.timer = setInterval(liveRefresh, 1000);
        var x = dr.querySelector('[data-tld-close]'); if (x) x.focus();
    }
    function close() {
        D.chatFull = false;
        D.aid = null;
        if (D.timer) { clearInterval(D.timer); D.timer = null; }
        var ov = document.getElementById('tldOverlay'), dr = document.getElementById('tldDrawer');
        if (ov) ov.remove();
        if (dr) dr.remove();
        document.removeEventListener('keydown', onKey);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ensureCss); else ensureCss();
    W.tlOpenAssetDrawer = function (aid) { open(aid); };
    W.TlAssetDrawer = { open: open, close: close, isOpen: function () { return !!D.aid; } };
})();
