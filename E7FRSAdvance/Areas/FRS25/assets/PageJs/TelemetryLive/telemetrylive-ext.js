/* v618.28: value-held window for the Graph view hover -- [from, to) of the
   value in force at t: last change at or before t, next change after it
   (to = null while the value is still holding). pts = [[ms, value], ...] */
function tlgvChangeWindow(pts, t) {
    if (!pts || !pts.length) return null;
    var lo = 0, hi = pts.length - 1, i = -1;
    while (lo <= hi) { var mid = (lo + hi) >> 1; if (pts[mid][0] <= t) { i = mid; lo = mid + 1; } else hi = mid - 1; }
    if (i < 0) return null;
    var v = pts[i][1], a = i, b = i + 1;
    while (a > 0 && pts[a - 1][1] === v) a--;
    while (b < pts.length && pts[b][1] === v) b++;
    return { from: pts[a][0], to: b < pts.length ? pts[b][0] : null };
}
/* =============================================================================
 * telemetrylive-ext.js  —  Telemetry Live v617 extensions (release 617.6)
 *
 * One file that replaces the five separate add-on scripts delivered in 617.2:
 *   1. telemetrylive-pm616.js        Point Machine features ported from v616
 *   2. telemetrylive-signal616.js    Signal / RDPMS / Circuit features from v616
 *   3. telemetrylive-ips616.js       IPS cards + table (v616) and IPS card v2
 *   4. telemetrylive-infra616.js     WebSocket URL, selection bar, export, etc.
 *   5. telemetrylive-graphview.js    Overlay / Stacked / Individual graph views
 *   6. (617.6) MULTI-ASSET GRAPH     Graph view for 1..N selected assets (Track /
 *                                    Signal / ELD / IPS / other; PM keeps own panel)
 *   7. (617.6) SWITCHING             asset-type / view / asset-no switching and
 *                                    cross-type leakage fixes (visible view of
 *                                    wsLiveData, ingest gate, metadata coalescing)
 *
 * LOAD ORDER (Index.cshtml): after telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-pmvibration.js. Every section wraps/overrides globals defined
 * by those files, so it must come last.
 *
 * Each section runs inside its own try/catch, so a failure in one section is
 * logged and does not stop the others (same isolation the separate files had).
 * ========================================================================== */


/* #############################################################################
 * SECTION: POINT MACHINE (v616 port)
 * (was telemetrylive-pm616.js)
 * ########################################################################## */
try {
/* =============================================================================
 * telemetrylive-pm616.js — Point Machine features from v616, ported to v617
 * (Aurora)
 *
 * Load order: AFTER telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-pmvibration.js (classic <script>). This file does not edit
 * those files. It replaces or wraps their global functions. Every caller
 * listed below looks the function up by name at call time (bare name or
 * window.X), so the replacement always takes effect.
 *
 * v616 line numbers refer to v616 TelemetryLive/telemetrylive.js.
 *
 *  G3  Replay-stale styling (grey "!" badge + tooltip) ......... v616 25797-25852
 *      getPmStructuredData(): entries also carry DataType, BroadcastKind,
 *      IsFresh, RawIsFresh, TimestampDevice, TimestampLocal, source;
 *      RDPMS AttrId falls back to AssetAttributeId (fixes B4) .. v616 26276-26305
 *  G1  PM Table view: 2 header rows, 3 sticky columns, A/B IPT/VPT110/TPT/
 *      LOC/KR, NWKR/RWKR/NWCR/RWCR relay badges ................. v616 29206-29316,
 *      cell builders _pmValTd/_pmValTdLinked/_pmIptTdLinked/_pmDlTd/
 *      _pmOpPosCellHtml ........................................... v616 26208-26275
 *      buildPmTableRowModel() (direction resolved like the v616 table,
 *      values floored at 0, older-operation values dropped) ...... v616 26013-26206
 *  G2  Update table cells in place by data-field, asset filter
 *      (updatePointMachineTableCell/_pmAssetInFilter) ............ v616 30019-30121, 32160-32175
 *  G4  Same-day operation history modal from .pm-hist-link ...... v616 29840-30018
 *      (renamed pm616ShowDayHistory etc. so it does not clash with the
 *      private helpers inside v617's inline-history IIFE)
 *  B1  Inline card history (plus icon) now parses the real History-API
 *      response (fixes v617 telemetrylive.js 23326-23352)
 *  G5  Waveform graph from a table value (pmShowTableArrayGraph/
 *      loadTableArrayData) ......................................... v616 27781-27932
 *  B2  Vibration section survives a Table re-render .............. v616 29306-29311
 *  B3  updatePmVoltageDisplay ignores the non-operation direction  v616 31445-31480
 *  G6  Indication graph: aborts superseded requests, request token,
 *      75 s timeout, {error} inside a 200 reply, Retry button
 *      (abortPmIndicationRequest/loadPmIndicationByRange) ........ v616 17987-18099, 19037-19040
 *  B5  Single-day picker keeps its UI; end time clamped to now,
 *      future dates blocked (v616 max/clamp idea) ................ v616 17928-17979
 *      closeGraphModal: a delayed remove no longer deletes a newly
 *      opened overlay
 *  G7  Indication chart carries the last value to the end of the range  v616 18189-18199, 18311-18321
 *  G10 Waveform boxes follow table data; graph links survive live
 *      updates (pmRowHasValue/pmRowHasCount/pmSyncWaveFlags/
 *      pmSetValueCell) ............................................. v616 25566-25580, 27577-27595,
 *                                                                    25640-25700, 25709-25780, 29154-29189
 *  G8  Values older than the operation show a placeholder
 *      (pmEntryMatchesOperation) in card rows, the DataLogger popup
 *      and the table model ........................................ v616 25448-25467, 25516-25528,
 *                                                                    27385-27398, 24035-24041
 *  G11 Point Machine attribute names from GetUserAssetInfo
 *      (loadPointMachineUserAssetInfo) ............................ v616 3523-3618, 4155-4175
 *      Export: the table's 2 header rows are flattened into aligned
 *      rows (tlCollectExportSections wrapper).
 *  Also: pmFmt/pmFmtInt show values <= 0 as 0 (v616 26323-26324).
 *
 *  Not ported: G9 (card direction and date resolver). v617's card works
 *  out direction per end in a 450-line updatePmCard; replacing that is not
 *  a low-risk change. G12/G13 are owned by someone else.
 * ========================================================================== */

/* ---- Global onclick targets (must be window properties) ------------------ */
function pmShowTableArrayGraph(assetId, direction, type, opTimestampDevice) {
    if (window.PM616 && typeof window.PM616.showTableArrayGraph === 'function') {
        return window.PM616.showTableArrayGraph(assetId, direction, type, opTimestampDevice);
    }
}
function loadTableArrayData() {
    if (window.PM616 && typeof window.PM616.loadTableArrayData === 'function') {
        return window.PM616.loadTableArrayData();
    }
}
function fnShowPmDayHistory(assetId) {
    if (window.PM616 && typeof window.PM616.showDayHistory === 'function') {
        return window.PM616.showDayHistory(assetId);
    }
}
function abortPmIndicationRequest() {
    if (window.PM616 && typeof window.PM616.abortIndication === 'function') {
        return window.PM616.abortIndication();
    }
}

(function (window, document) {
    'use strict';

    var $ = window.jQuery;
    if (!$) { try { console.warn('[PM616] jQuery missing — PM 616 features not installed'); } catch (e) { } return; }
    if (window.__pm616Installed) return;
    window.__pm616Installed = true;

    var PM616 = window.PM616 = window.PM616 || {};
    var DASH = '—';

    /* ======================================================================
     * Small helpers
     * ==================================================================== */
    function fn(name) { return (typeof window[name] === 'function') ? window[name] : null; }
    function call(name) {
        var f = fn(name);
        if (!f) return undefined;
        return f.apply(window, Array.prototype.slice.call(arguments, 1));
    }
    function zf(v) {
        var f = fn('tlZeroFloor');
        if (f) return f(v);
        var n = parseFloat(v);
        if (isNaN(n)) return v;
        return n <= 0 ? 0 : n;
    }
    function esc(t) {
        if (t === null || t === undefined) return '';
        return String(t).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function pad2(n) { n = parseInt(n, 10) || 0; return n < 10 ? '0' + n : '' + n; }
    function live() { return window.wsLiveData || {}; }
    function histBase() {
        return (typeof window.HISTORY_API_BASE === 'string' && window.HISTORY_API_BASE)
            ? window.HISTORY_API_BASE : '/FRS25/Telemetry/GetHistoryData';
    }
    function fmtApi(d) {
        var f = fn('formatDateForHistoryApi');
        if (f) return f(d);
        return pad2(d.getDate()) + pad2(d.getMonth() + 1) + d.getFullYear() + '_' +
            pad2(d.getHours()) + pad2(d.getMinutes()) + pad2(d.getSeconds());
    }
    function secMs(ts) {
        var f = fn('pmTsToSecMs');
        if (f) return f(ts);
        if (!ts) return 0;
        var ms = new Date(ts).getTime();
        return (ms > 0) ? Math.floor(ms / 1000) * 1000 : 0;
    }
    function tsMsAny(ts) {
        if (ts === null || ts === undefined || ts === '') return NaN;
        if (String(ts).indexOf('0001-01-01') !== -1) return NaN;
        var t = new Date(ts).getTime();
        if (!isNaN(t)) return t;
        var m = String(ts).match(/^(\d{2})(\d{2})(\d{4})_(\d{2})(\d{2})(\d{2})$/);
        if (m) return new Date(+m[3], (+m[2]) - 1, +m[1], +m[4], +m[5], +m[6]).getTime();
        return NaN;
    }
    function deviceMs(ts) {
        var f = fn('pmParseDeviceTs');
        var v = f ? f(ts) : tsMsAny(ts);
        return (typeof v === 'number') ? v : NaN;
    }
    function ptName(assetId) {
        var a = live()[assetId];
        var base = String((a && a.AssetName) || assetId);
        return base.indexOf('PT-') === 0 ? base : 'PT-' + base;
    }
    function fmtClock(d) {
        var f = fn('fmtTime');
        if (f) return f(d);
        if (!d) return DASH;
        return pad2(d.getHours()) + ':' + pad2(d.getMinutes()) + ':' + pad2(d.getSeconds());
    }
    function isPmType() {
        var f = fn('isPointAssetType');
        try { return !!(f ? f() : false) || !!window.pmTableMode; } catch (e) { return !!window.pmTableMode; }
    }
    function warn(msg, title) {
        var f = fn('showWarning');
        if (f) f(msg, title || 'Warning'); else try { console.warn('[PM616]', msg); } catch (e) { }
    }
    function hasVal(v) {
        return v !== null && v !== undefined && v !== '' && v !== DASH && v !== '--' && v !== '-';
    }
    PM616.helpers = { zf: zf, esc: esc, deviceMs: deviceMs };

    /* ======================================================================
     * Styles (Aurora tokens from aurora-telemetry.css)
     * ==================================================================== */
    function injectStyles() {
        if (document.getElementById('pm616-styles')) return;
        var T = '#wsLiveTable.pm616-table';
        var css = [
            /* --- PM table (G1) --- */
            T + '{border-collapse:separate;border-spacing:0;}',
            T + ' thead tr th{font-size:11px !important;letter-spacing:.06em !important;padding:9px 10px !important;text-align:center !important;vertical-align:middle !important;}',
            T + ' thead tr th.pm616-grp-a{color:var(--at-brand,#22d3ee) !important;box-shadow:inset 0 -2px 0 var(--at-brand,#22d3ee);}',
            T + ' thead tr th.pm616-grp-b{color:var(--at-violet,#a78bfa) !important;box-shadow:inset 0 -2px 0 var(--at-violet,#a78bfa);}',
            T + ' thead tr th.pm616-sub-a{color:var(--at-brand,#22d3ee) !important;opacity:.9;}',
            T + ' thead tr th.pm616-sub-b{color:var(--at-violet,#a78bfa) !important;opacity:.9;}',
            T + ' thead tr th.pm616-dl-h{color:var(--at-warn,#fbbf24) !important;}',
            T + ' tbody tr td{padding:8px 10px !important;text-align:center;white-space:nowrap;font-family:var(--at-font-mono,monospace) !important;font-size:12px;}',
            T + ' tbody tr td.pm616-name{text-align:left;font-family:var(--at-font-body,sans-serif) !important;}',
            T + ' tbody tr td.col-a{background:rgba(34,211,238,0.035);}',
            T + ' tbody tr td.col-b{background:rgba(167,139,250,0.035);}',
            T + ' .pm616-s1,' + T + ' .pm616-s2,' + T + ' .pm616-s3{position:sticky !important;}',
            T + ' tbody tr td.pm616-s1,' + T + ' tbody tr td.pm616-s2,' + T + ' tbody tr td.pm616-s3{z-index:3;background:var(--at-bg1,#0a0f24) !important;}',
            T + ' thead tr th.pm616-s1,' + T + ' thead tr th.pm616-s2,' + T + ' thead tr th.pm616-s3{z-index:6 !important;}',
            T + ' .pm616-s1{left:0;}',
            T + ' .pm616-s3{box-shadow:6px 0 8px -6px rgba(0,0,0,.55);}',
            'body[data-aurora="light"] ' + T + ' tbody tr td.pm616-s1,body[data-aurora="light"] ' + T + ' tbody tr td.pm616-s2,body[data-aurora="light"] ' + T + ' tbody tr td.pm616-s3{background:#f8fafc !important;}',
            T + ' a.pm-table-array-link{color:inherit;text-decoration:underline dotted;text-underline-offset:3px;cursor:pointer;}',
            T + ' a.pm-table-array-link:hover{color:var(--at-brand,#22d3ee);}',
            T + ' a.pm-hist-link{color:var(--at-t1,#fff);font-weight:700;text-decoration:underline;text-decoration-color:rgba(34,211,238,.45);text-underline-offset:3px;cursor:pointer;}',
            T + ' a.pm-hist-link i{font-size:10px;opacity:.65;color:var(--at-brand,#22d3ee);margin-left:4px;}',
            T + ' tbody tr td.pm616-ind-n{color:var(--at-ok,#34d399) !important;font-weight:600;}',
            T + ' tbody tr td.pm616-ind-r{color:var(--at-warn,#fbbf24) !important;font-weight:600;}',
            T + ' .pm616-na{color:var(--at-t4,rgba(255,255,255,.34));}',
            T + ' .pm616-dl-badge{display:inline-block;padding:3px 12px;border-radius:20px;font-size:10.5px;font-weight:700;letter-spacing:.04em;font-family:var(--at-font-body,sans-serif);}',
            T + ' .pm616-dl-badge.pickup{background:rgba(52,211,153,.16);color:var(--at-ok,#34d399);border:1px solid rgba(52,211,153,.35);}',
            T + ' .pm616-dl-badge.drop{background:rgba(251,191,36,.14);color:var(--at-warn,#fbbf24);border:1px solid rgba(251,191,36,.35);}',
            T + ' .pm-dir-badge{padding:3px 10px;font-size:11px;}',
            T + ' td.pm616-flash{animation:pm616Flash 1.1s ease-out;}',
            '@keyframes pm616Flash{0%{box-shadow:inset 0 0 0 999px rgba(34,211,238,.22);}100%{box-shadow:inset 0 0 0 999px rgba(34,211,238,0);}}',
            /* --- Replay-stale (G3) --- */
            '.pm-replay-stale{color:var(--at-t3,rgba(255,255,255,.5)) !important;background:rgba(148,163,184,.14) !important;opacity:.78;filter:grayscale(1);cursor:not-allowed;position:relative;border-radius:4px;}',
            'span.pm-replay-stale{display:inline-block;padding:0 4px 0 18px;}',
            T + ' tbody tr td.pm-replay-stale,' + T + ' tbody tr:hover td.pm-replay-stale{color:var(--at-t3,rgba(255,255,255,.5)) !important;background:rgba(148,163,184,.14) !important;padding-left:24px !important;}',
            '.pm-replay-stale::before{content:"!";position:absolute;left:3px;top:50%;transform:translateY(-50%);width:13px;height:13px;line-height:13px;text-align:center;font-size:10px;font-weight:700;color:var(--at-bg0,#060914);background:var(--at-t3,rgba(255,255,255,.5));border-radius:50%;font-family:var(--at-font-body,sans-serif);}',
            'td.pm-replay-stale::before{left:6px;}',
            '.pm-replay-stale a{cursor:pointer;}',
            /* --- Day history modal (G4) --- */
            '#modalalertlogs .modal-dialog.pm-hist-dialog{max-width:1000px !important;width:96% !important;}',
            '#modalalertlogs .modal-dialog.pm-hist-dialog.pm-hist-full{max-width:100vw !important;width:100vw !important;margin:0 !important;height:100vh;}',
            '.pm-hist-dialog.pm-hist-full .modal-content{height:100vh;border-radius:0 !important;display:flex;flex-direction:column;}',
            '.pm-hist-dialog.pm-hist-full .modal-body{flex:1 1 auto;max-height:none !important;}',
            '.pm-hist-dialog.pm-hist-full .pm616-hist-scroll{max-height:calc(100vh - 250px) !important;}',
            '.pm-hist-dialog .modal-header{position:relative;}',
            '#pmHistFsBtn{position:absolute;right:62px;top:50%;transform:translateY(-50%);background:rgba(255,255,255,.08);color:#fff;border:1px solid rgba(255,255,255,.35);border-radius:8px;width:32px;height:32px;cursor:pointer;font-size:13px;line-height:1;}',
            '#pmHistFsBtn:hover{background:rgba(34,211,238,.18);}',
            '.pm616-hist-wrap{background:var(--at-bg1,#0a0f24);border:1px solid var(--at-edge,rgba(255,255,255,.1));border-radius:12px;padding:12px 14px;color:var(--at-t2,rgba(255,255,255,.72));font-family:var(--at-font-body,sans-serif);}',
            'body[data-aurora="light"] .pm616-hist-wrap{background:#fff;}',
            '.pm616-hist-loader{display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:220px;gap:14px;color:var(--at-t3,rgba(255,255,255,.5));font-size:13px;}',
            '.pm616-hist-spinner{width:36px;height:36px;border:3px solid rgba(34,211,238,.22);border-top-color:var(--at-brand,#22d3ee);border-radius:50%;animation:pm616Spin .8s linear infinite;}',
            '@keyframes pm616Spin{to{transform:rotate(360deg);}}',
            '.pm616-hist-sum{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;margin:0 0 8px;color:var(--at-t3,rgba(255,255,255,.5));font-size:12px;}',
            '.pm616-hist-sum b{color:var(--at-t1,#fff);}',
            '.pm616-hist-sum b.n{color:var(--at-ok,#34d399);}',
            '.pm616-hist-sum b.r{color:var(--at-warn,#fbbf24);}',
            '.pm616-hist-scroll{max-height:60vh;overflow:auto;border:1px solid var(--at-edge,rgba(255,255,255,.1));border-radius:10px;}',
            'table.pm616-hist-table{width:100%;border-collapse:separate;border-spacing:0;font-size:12px;margin:0;}',
            'table.pm616-hist-table thead th{position:sticky;background:rgba(6,9,20,.96);color:var(--at-t3,rgba(255,255,255,.5));font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.06em;padding:8px;text-align:center;white-space:nowrap;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.1));}',
            'body[data-aurora="light"] table.pm616-hist-table thead th{background:rgba(240,245,255,.96);}',
            'table.pm616-hist-table thead tr:nth-child(1) th{top:0;z-index:3;}',
            'table.pm616-hist-table thead tr:nth-child(2) th{top:33px;z-index:2;}',
            'table.pm616-hist-table thead th.grp-a-head,table.pm616-hist-table thead th.grp-a{color:var(--at-brand,#22d3ee);}',
            'table.pm616-hist-table thead th.grp-b-head,table.pm616-hist-table thead th.grp-b{color:var(--at-violet,#a78bfa);}',
            'table.pm616-hist-table thead th.grp-a-head{box-shadow:inset 0 -2px 0 var(--at-brand,#22d3ee);}',
            'table.pm616-hist-table thead th.grp-b-head{box-shadow:inset 0 -2px 0 var(--at-violet,#a78bfa);}',
            'table.pm616-hist-table tbody td{text-align:center;white-space:nowrap;padding:6px 8px;color:var(--at-t2,rgba(255,255,255,.72));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.08));font-family:var(--at-font-mono,monospace);}',
            'table.pm616-hist-table tbody tr:hover td{background:var(--at-g2,rgba(255,255,255,.06));color:var(--at-t1,#fff);}',
            '.pm616-hist-dir{display:inline-block;min-width:64px;padding:2px 10px;border-radius:10px;font-size:11px;font-weight:600;font-family:var(--at-font-body,sans-serif);}',
            '.pm616-hist-dir.n{background:rgba(52,211,153,.14);color:var(--at-ok,#34d399);}',
            '.pm616-hist-dir.r{background:rgba(251,191,36,.14);color:var(--at-warn,#fbbf24);}',
            '.pm616-hist-na{color:var(--at-t4,rgba(255,255,255,.34));}',
            '.pm616-hist-note{margin-top:6px;color:var(--at-t4,rgba(255,255,255,.34));font-size:11px;}',
            '.pm616-hist-err{padding:30px;text-align:center;color:var(--at-bad,#fb7185);}',
            /* --- Inline card history (B1) — same look as v617's private IIFE --- */
            '.pm-hist-toggle{cursor:pointer;margin-left:7px;font-size:11px;color:#38bdf8;display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border-radius:3px;transition:transform .15s,background .15s;vertical-align:middle;}',
            '.pm-hist-toggle:hover{background:rgba(56,189,248,.18);}',
            '.pm-hist-toggle.open{transform:rotate(45deg);color:#f472b6;}',
            '.pm-hist-inline{margin:6px 0 4px 0;border:1px solid rgba(56,189,248,.35);border-radius:8px;overflow:hidden;background:#0b1220;box-shadow:0 2px 10px rgba(0,0,0,.25);}',
            '.pm-hist-inline-head{display:flex;align-items:center;justify-content:space-between;padding:8px 12px;background:linear-gradient(90deg,#0f2233,#0b1220);border-bottom:1px solid rgba(56,189,248,.25);}',
            '.pm-hist-inline-title{font-size:12.5px;font-weight:600;color:#e2e8f0;}',
            '.pm-hist-inline-close{cursor:pointer;color:#94a3b8;font-size:14px;padding:2px 6px;border-radius:4px;}',
            '.pm-hist-inline-close:hover{color:#f472b6;background:rgba(244,114,182,.12);}',
            '.pm-hist-inline-body{max-height:340px;overflow:auto;}',
            '.pm-hist-inline .pm-hist-loader{padding:26px;text-align:center;color:#94a3b8;font-size:13px;}',
            '.pm-hist-inline .pm-hist-spinner{width:22px;height:22px;border:3px solid rgba(56,189,248,.25);border-top-color:#38bdf8;border-radius:50%;margin:0 auto 10px;animation:pm616Spin .8s linear infinite;}',
            '.pm-hist-tbl{width:100%;border-collapse:collapse;font-size:12px;}',
            '.pm-hist-tbl th{position:sticky;top:0;background:#0f2233;color:#7dd3fc;font-weight:600;padding:8px 10px;text-align:left;white-space:nowrap;border-bottom:1px solid rgba(56,189,248,.3);}',
            '.pm-hist-tbl td{padding:7px 10px;border-bottom:1px solid rgba(148,163,184,.12);color:#e2e8f0;white-space:nowrap;}',
            '.pm-hist-tbl tr:hover td{background:rgba(56,189,248,.06);}',
            '.pm-hist-sum{padding:8px 12px;font-size:11.5px;color:#94a3b8;display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;border-bottom:1px solid rgba(148,163,184,.12);}',
            '.pm-hist-dir-N{color:#34d399;font-weight:600;}',
            '.pm-hist-dir-R{color:#fb923c;font-weight:600;}',
            /* --- Indication graph retry (G6) --- */
            '.pm616-retry{background:linear-gradient(135deg,#22d3ee,#0891b2);color:#04222b;border:none;border-radius:6px;padding:6px 16px;font-size:12px;font-weight:700;cursor:pointer;}'
        ].join('\n');
        var st = document.createElement('style');
        st.id = 'pm616-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }

    /* ======================================================================
     * G3 — replay-stale helpers (v616 25797-25852)
     * ==================================================================== */
    function normFresh(raw) {
        if (raw === true || raw === 'true' || raw === 1 || raw === '1') return true;
        if (raw === false || raw === 'false' || raw === 0 || raw === '0') return false;
        return null;
    }
    function isReplayStale(source) {
        if (!source) return false;
        var raw = source.raw || null;
        var dataType = String(source.DataType || (raw && raw.DataType) || '').trim().toLowerCase();
        if (dataType === 'datalogger') return false;
        var kind = String(source.BroadcastKind || (raw && raw.BroadcastKind) || '').trim().toLowerCase();
        var rawFresh = source.RawIsFresh;
        if (rawFresh === undefined || rawFresh === null) rawFresh = source.IsFresh;
        if ((rawFresh === undefined || rawFresh === null) && raw) rawFresh = raw.IsFresh;
        return kind === 'replay' && normFresh(rawFresh) === false;
    }
    function staleTitle(source) {
        if (!source) return '';
        var raw = source.raw || null;
        var bk = source.BroadcastKind || (raw && raw.BroadcastKind) || '';
        var fr = (source.RawIsFresh !== undefined && source.RawIsFresh !== null) ? source.RawIsFresh
            : (source.IsFresh !== undefined && source.IsFresh !== null) ? source.IsFresh
                : (raw ? raw.IsFresh : '');
        var ts = source.TimestampDevice || (raw && raw.TimestampDevice) || source.Timestamp || '';
        return 'BroadcastKind: ' + bk + '\nIsFresh: ' + fr + '\nTimestampDevice: ' + ts;
    }
    function staleCls(source) { return isReplayStale(source) ? ' pm-replay-stale' : ''; }
    function staleAttrs(source) {
        if (!isReplayStale(source)) return '';
        return ' aria-disabled="true" title="' + esc(staleTitle(source)) + '"';
    }
    function applyStale(node, source) {
        if (!node) return;
        var stale = isReplayStale(source);
        if (node.classList) node.classList.toggle('pm-replay-stale', stale);
        if (stale) {
            node.setAttribute('title', staleTitle(source));
            node.setAttribute('aria-disabled', 'true');
        } else {
            node.removeAttribute('aria-disabled');
            node.removeAttribute('title');
        }
    }
    PM616.isReplayStale = isReplayStale;
    PM616.buildStaleTitle = staleTitle;
    PM616.applyReplayStale = applyStale;
    if (!fn('isPmReplayStale')) window.isPmReplayStale = isReplayStale;
    if (!fn('buildPmStaleTitle')) window.buildPmStaleTitle = staleTitle;
    if (!fn('applyPmReplayStale')) window.applyPmReplayStale = applyStale;

    /* ======================================================================
     * G8 — drop values older than the operation (v616 25448-25467)
     * ==================================================================== */
    var _gate = null;   // { assetId: String, ms: Number } while a gated build runs
    // Round 2: default tolerance 3000 ms (was 0). Motor groups belonging to
    // ONE operation can report device timestamps a second or two apart; with
    // zero tolerance those ends showed em-dashes. 3 s keeps genuine previous-
    // operation values (minutes older) out while accepting same-operation skew.
    if (typeof window.PM616_ROW_STALE_TOLERANCE_MS !== 'number') window.PM616_ROW_STALE_TOLERANCE_MS = 3000;

    function entryMatchesOperation(entry, opTsMs) {
        if (!entry) return false;
        if (!opTsMs) return true;
        var eTs = secMs(entry.TimestampDevice || entry.timestamp);
        if (!eTs) return false;
        return eTs >= (opTsMs - (window.PM616_ROW_STALE_TOLERANCE_MS || 0));
    }
    function withGate(assetId, opMs, work) {
        var prev = _gate;
        _gate = (assetId !== undefined && assetId !== null && opMs) ? { assetId: String(assetId), ms: opMs } : null;
        try { return work(); } finally { _gate = prev; }
    }
    PM616.entryMatchesOperation = entryMatchesOperation;
    if (!fn('pmEntryMatchesOperation')) window.pmEntryMatchesOperation = entryMatchesOperation;

    /* ======================================================================
     * getPmStructuredData — v617 decoding + v616 extra fields + gate
     * ==================================================================== */
    function getPmStructuredData616(assetId) {
        var asset = live()[assetId];
        if (!asset) return null;
        var r = { Normal: { AC: {}, AV: {}, BC: {}, BV: {} }, Reverse: { AC: {}, AV: {}, BC: {}, BV: {} }, RDPMS: {}, DataLogger: {} };
        var decode = fn('decodePmAttribute'), parseName = fn('parsePmAttrName');
        var gate = (_gate && _gate.assetId === String(assetId)) ? _gate : null;
        var attrs = asset.attrs || {};
        for (var an in attrs) {
            if (!Object.prototype.hasOwnProperty.call(attrs, an)) continue;
            var ad = attrs[an];
            if (!ad) continue;
            var numId = parseInt(ad.AssetAttributeId || ad.AttrId || 0, 10);
            var d = (numId && decode) ? decode(numId) : null;
            if (!d && parseName && decode) { var p = parseName(an); if (p) d = decode(p.attrId); }
            var common = {
                value: ad.Value, timestamp: ad.Timestamp, changed: ad.changed, attrName: an,
                DataType: ad.DataType, BroadcastKind: ad.BroadcastKind,
                RawIsFresh: ad.RawIsFresh, IsFresh: ad.IsFresh,
                TimestampDevice: ad.TimestampDevice, TimestampLocal: ad.TimestampLocal,
                source: ad
            };
            if (d) {
                if (gate && !entryMatchesOperation(common, gate.ms)) continue;
                r[d.direction][d.type][d.metric] = common;
            } else {
                common.AttrId = ad.AttrId || ad.AssetAttributeId;
                common.AssetAttributeId = ad.AssetAttributeId || ad.AttrId;
                r.RDPMS[an] = common;
            }
        }
        if (asset.dlRelays) {
            for (var rk in asset.dlRelays) {
                if (Object.prototype.hasOwnProperty.call(asset.dlRelays, rk)) r.DataLogger[rk] = asset.dlRelays[rk];
            }
        }
        return r;
    }
    window.getPmStructuredData = getPmStructuredData616;

    /* pmFmt / pmFmtInt: values <= 0 show as 0 (v616 26323-26324) */
    window.pmFmt = function (v, dec) {
        if (v === null || v === undefined || v === '') return '-';
        var n = parseFloat(v);
        return isNaN(n) ? v : Number(zf(n)).toFixed(dec !== undefined ? dec : 2);
    };
    window.pmFmtInt = function (v) {
        if (v === null || v === undefined || v === '') return '-';
        var n = parseFloat(v);
        return isNaN(n) ? v : String(Math.round(Number(zf(n))));
    };

    /* ======================================================================
     * Direction resolver used by the table model (v616 26013-26098)
     * ==================================================================== */
    function energisedDirection(asset) {
        if (!asset || !asset.attrs) return null;
        var nIds = window.PM_NWKR_IDS || [25, 27, 576, 578];
        var rIds = window.PM_RWKR_IDS || [26, 28, 577, 579];
        var thr = (typeof window.PM_DIRECTION_THRESHOLD === 'number') ? window.PM_DIRECTION_THRESHOLD : 10;
        var maxN = 0, maxR = 0;
        for (var an in asset.attrs) {
            if (!Object.prototype.hasOwnProperty.call(asset.attrs, an)) continue;
            var ad = asset.attrs[an];
            var id = parseInt(ad.AttrId || ad.AssetAttributeId || 0, 10);
            var v = parseFloat(ad.Value);
            if (isNaN(v)) continue;
            if (nIds.indexOf(id) !== -1) { if (v > maxN) maxN = v; }
            else if (rIds.indexOf(id) !== -1) { if (v > maxR) maxR = v; }
        }
        var nEn = maxN > thr, rEn = maxR > thr;
        if (nEn && !rEn) return 'Normal';
        if (rEn && !nEn) return 'Reverse';
        return null;
    }
    function resolveDirection(assetId, pm) {
        var asset = live()[assetId];
        if (!asset) return 'Normal';
        var en = energisedDirection(asset);
        if (en) return en;
        var fresh = fn('determinePmDirectionByFreshness') ? window.determinePmDirectionByFreshness(asset) : null;
        if (fresh && fresh.direction) return fresh.direction === 'REVERSE' ? 'Reverse' : 'Normal';
        var ind = fn('resolveLatestPmIndication') ? window.resolveLatestPmIndication(asset) : { found: false };
        if (ind && ind.found) return ind.direction;
        var pos = fn('resolvePmPosition') ? window.resolvePmPosition(asset, pm) : null;
        if (pos && pos.direction && pos.direction !== 'Unknown') return pos.direction;
        var op = fn('resolveLatestPmOperation') ? window.resolveLatestPmOperation(asset, pm, null) : null;
        return (op && op.direction) || 'Normal';
    }
    function resolveOperationForDirection(assetId, pm, dir) {
        var asset = live()[assetId];
        var op = fn('resolveLatestPmOperation') ? window.resolveLatestPmOperation(asset, pm, dir) : null;
        if (!op) op = { direction: dir, timestampDevice: null, sourceGroup: null, source: null };
        op.direction = dir;
        return op;
    }
    PM616.resolveDirection = resolveDirection;
    PM616.energisedDirection = energisedDirection;

    /* ======================================================================
     * buildPmTableRowModel (v616 26119-26206) — also used by the PDF export
     * ==================================================================== */
    function mEntry(mo, dec) {
        if (!mo || mo.value === undefined || mo.value === null || mo.value === '')
            return { text: DASH, source: (mo && mo.source) || null, present: false };
        var n = parseFloat(mo.value);
        return { text: isNaN(n) ? String(mo.value) : Number(zf(n)).toFixed(dec), source: mo.source || null, present: !isNaN(n) };
    }
    function mEntryInt(mo) {
        if (!mo || mo.value === undefined || mo.value === null || mo.value === '')
            return { text: DASH, source: (mo && mo.source) || null, present: false };
        var n = parseFloat(mo.value);
        return { text: isNaN(n) ? String(mo.value) : String(Math.round(Number(zf(n)))), source: mo.source || null, present: !isNaN(n) };
    }
    function buildPmTableRowModel616(assetId) {
        var asset = live()[assetId];
        var pm = getPmStructuredData616(assetId);
        var name = ptName(assetId);
        var dir = resolveDirection(assetId, pm);
        var op = resolveOperationForDirection(assetId, pm, dir);
        var pos = fn('resolvePmPosition') ? window.resolvePmPosition(asset, pm) : { direction: 'Unknown', source: 'none', timestampDevice: null };
        var isRev = (dir === 'Reverse');
        var opMs = secMs(op && op.timestampDevice);

        var rdById = {};
        if (pm && pm.RDPMS) {
            for (var rk in pm.RDPMS) {
                if (!Object.prototype.hasOwnProperty.call(pm.RDPMS, rk)) continue;
                var rv = pm.RDPMS[rk];
                var rid = parseInt((rv.source && (rv.source.AssetAttributeId || rv.source.AttrId)) || rv.AttrId || rv.AssetAttributeId || 0, 10);
                if (rid && !rdById[rid]) rdById[rid] = rv;
            }
        }
        function rdEntry(id) {
            var e = rdById[id];
            if (!e || e.value === undefined || e.value === null || e.value === '')
                return { text: DASH, source: (e && e.source) || null, present: false };
            var n = parseFloat(e.value);
            return { text: isNaN(n) ? String(e.value) : Number(zf(n)).toFixed(2), source: e.source || null, present: !isNaN(n) };
        }
        function endFields(end) {
            var C = (end === 'A') ? 'AC' : 'BC';
            var V = (end === 'A') ? 'AV' : 'BV';
            var d = (pm && pm[dir]) ? pm[dir] : { AC: {}, AV: {}, BC: {}, BV: {} };
            var locId = (end === 'A') ? (isRev ? 577 : 576) : (isRev ? 579 : 578);
            var krId = (end === 'A') ? (isRev ? 26 : 25) : (isRev ? 28 : 27);
            var krLabel = isRev ? 'RWKR' : 'NWKR';
            function indEntry(id) {
                var e = rdEntry(id);
                if (e.present) e.text = krLabel + ': ' + e.text;
                return e;
            }
            var g = function (mo) { return entryMatchesOperation(mo, opMs) ? mo : null; };
            return {
                iptMax: mEntry(g(d[C].Max), 2), iptAvg: mEntry(g(d[C].Avg), 2),
                vpt110Avg: mEntry(g(d[V].Avg), 2), tpt: mEntryInt(g(d[C].OperationTime)),
                locIndication: indEntry(locId), krIndication: indEntry(krId),
                krLabel: krLabel
            };
        }
        var opDate = DASH;
        if (op.timestampDevice) {
            var dt = new Date(op.timestampDevice);
            if (!isNaN(dt.getTime())) {
                opDate = pad2(dt.getDate()) + '/' + pad2(dt.getMonth() + 1) + '/' + dt.getFullYear() +
                    ' ' + pad2(dt.getHours()) + ':' + pad2(dt.getMinutes()) + ':' + pad2(dt.getSeconds());
            }
        }
        function dlBadge(colKey) {
            var dl = (asset && asset.dlRelays) || {};
            var accepted = ['Combined-' + colKey, 'A End - ' + colKey, 'B End - ' + colKey, colKey];
            var keys = Object.keys(dl);
            for (var a = 0; a < accepted.length; a++) {
                var want = accepted[a].toLowerCase();
                for (var i = 0; i < keys.length; i++) {
                    var rr = dl[keys[i]];
                    if (!rr) continue;
                    if (String(rr.displayName || keys[i] || '').toLowerCase() === want) return rr;
                }
            }
            return null;
        }
        return {
            assetId: assetId, assetName: name,
            operation: { direction: dir, timestampDevice: op.timestampDevice, sourceGroup: op.sourceGroup, source: op.source },
            position: { direction: pos.direction, source: pos.source, timestampDevice: pos.timestampDevice, conflict: (pos.direction !== 'Unknown' && pos.direction !== dir) },
            operationDate: opDate, A: endFields('A'), B: endFields('B'),
            datalogger: { NWKR: dlBadge('NWKR'), RWKR: dlBadge('RWKR'), NWCR: dlBadge('NWCR'), RWCR: dlBadge('RWCR') },
            lastTelemetryTimestamp: (asset && asset.lastUpdated) ? asset.lastUpdated : null
        };
    }
    window.buildPmTableRowModel = buildPmTableRowModel616;

    /* ======================================================================
     * G1 — PM table cells (v616 26208-26275, Aurora look)
     * ==================================================================== */
    var TABLE_CLASS = 'pm616-table';

    function arrayLink(inner, assetId, dirPrefix, type, opTs) {
        return '<a href="javascript:void(0)" class="pm-table-array-link" title="Click to view full-day waveform"' +
            ' data-pm616-aid="' + esc(assetId) + '" data-pm616-dir="' + dirPrefix + '" data-pm616-type="' + type + '"' +
            ' data-pm616-ts="' + esc(opTs) + '">' + inner + '</a>';
    }
    function valSpan(cls, e) {
        return '<span class="' + cls + staleCls(e.source) + '"' + staleAttrs(e.source) + '>' + esc(e.text) + '</span>';
    }
    function dlCell(relay) {
        if (!relay) return { cls: 'dl-col-cell', html: '<span class="pm616-na">' + DASH + '</span>' };
        var cls = relay.isPickup ? 'pickup' : 'drop';
        return { cls: 'dl-col-cell', html: '<span class="rdpms-dl-badge pm616-dl-badge ' + cls + '">' + (relay.isPickup ? 'Pickup' : 'Drop') + '</span>' };
    }

    /* One description per data cell. The builder and the in-place updater
       both use it, so a full render and a live update always match. */
    function rowCellSpecs(m, ends) {
        var aid = m.assetId;
        var specs = [];
        var dirPrefix = (m.operation.direction === 'Reverse') ? 'R' : 'N';
        var opTs = m.operation.timestampDevice || '';
        var indCls = (m.operation.direction === 'Reverse') ? ' pm616-ind-r' : ' pm616-ind-n';

        specs.push({
            field: 'assetName', cls: 'asset-name pm616-name pm616-s1', noFlash: true,
            html: '<a href="javascript:void(0);" class="pm-hist-link" data-id="' + esc(aid) + '" title="View same-day operation history">' +
                esc(m.assetName) + '<i class="fas fa-history"></i></a>' +
                /* v618.32: AI button -> asset drawer */
                '<button type="button" class="tl-asset-action tl-aa-ai" style="margin-left:6px" onclick="event.stopPropagation();tlOpenAssetDrawer(\'' + esc(aid) + '\')" title="Asset dashboard: values, AI, circuit, alerts, graph" aria-label="Open asset dashboard for ' + esc(m.assetName) + '"><i class="fa-solid fa-wand-magic-sparkles"></i></button>'
        });
        specs.push({ field: 'opDateTime', cls: 'pm-datetime-cell pm616-s2', html: esc(m.operationDate) });
        specs.push({
            field: 'opPosition', cls: 'pm616-s3',
            html: '<span class="pm-dir-badge ' + (m.operation.direction === 'Reverse' ? 'reverse' : 'normal') + '">' + esc(m.operation.direction) + '</span>'
        });

        function end(E, colCls, cType, vType) {
            var e = m[E];
            var iptInner = valSpan('pm-ipt-max', e.iptMax) + ' / ' + valSpan('pm-ipt-avg', e.iptAvg);
            if ((e.iptMax.present || e.iptAvg.present) && opTs) iptInner = arrayLink(iptInner, aid, dirPrefix, cType, opTs);
            specs.push({ field: E + '.ipt', cls: colCls, html: iptInner });

            var vInner = esc(e.vpt110Avg.text);
            if (e.vpt110Avg.present && opTs) vInner = arrayLink(vInner, aid, dirPrefix, vType, opTs);
            specs.push({ field: E + '.vpt110', cls: colCls, source: e.vpt110Avg.source, html: vInner });

            specs.push({ field: E + '.tpt', cls: colCls, source: e.tpt.source, html: esc(e.tpt.text) });
            specs.push({ field: E + '.loc', cls: colCls + (e.locIndication.present ? indCls : ''), source: e.locIndication.source, html: esc(e.locIndication.text) });
            specs.push({ field: E + '.kr', cls: colCls + (e.krIndication.present ? indCls : ''), source: e.krIndication.source, html: esc(e.krIndication.text) });
        }
        if (ends.hasA) end('A', 'col-a', 'AC', 'AV');
        if (ends.hasB) end('B', 'col-b', 'BC', 'BV');

        ['NWKR', 'RWKR', 'NWCR', 'RWCR'].forEach(function (k) {
            var c = dlCell(m.datalogger[k]);
            specs.push({ field: 'dl.' + k, cls: c.cls, html: c.html });
        });
        specs.push({
            field: 'lastTelemetry', cls: 'time-cell', noFlash: true,
            html: esc(m.lastTelemetryTimestamp ? fmtClock(m.lastTelemetryTimestamp) : DASH)
        });

        specs.forEach(function (s) {
            s.stale = isReplayStale(s.source);
            s.fullCls = s.cls + (s.stale ? ' pm-replay-stale' : '');
            s.title = s.stale ? staleTitle(s.source) : '';
            s.sig = s.fullCls + '|' + s.title + '|' + s.html;
        });
        return specs;
    }
    function tdHtml(s) {
        return '<td data-field="' + s.field + '" class="' + s.fullCls + '" data-pm616-sig="' + esc(s.sig) + '"' +
            (s.stale ? ' aria-disabled="true" title="' + esc(s.title) + '"' : '') + '>' + s.html + '</td>';
    }

    /* ---- Which assets / which ends ---- */
    function assetInFilter(assetId) {
        var f = window.wsCurrentFilterAssetIds || [];
        if (!f.length || f[0] === '' || f[0] === '0') return true;
        return f.map(String).indexOf(String(assetId)) !== -1;
    }
    PM616.assetInFilter = assetInFilter;
    if (!fn('_pmAssetInFilter')) window._pmAssetInFilter = assetInFilter;

    function tableAssetIds() {
        var data = live();
        var ids = Object.keys(data).filter(assetInFilter);
        ids.sort(function (a, b) {
            return String(data[a].AssetName || '').localeCompare(String(data[b].AssetName || ''), undefined, { numeric: true, sensitivity: 'base' });
        });
        var bdl = fn('blankDataLast');
        if (bdl) { try { ids = bdl(ids) || ids; } catch (e) { } }
        return ids;
    }
    var _endsCache = null;
    function tableEnds(ids) {
        var key = ids.join(',');
        var nowMs = Date.now();
        if (_endsCache && _endsCache.key === key && (nowMs - _endsCache.t) < 50) return _endsCache.val;
        var val = computeTableEnds(ids);
        _endsCache = { key: key, t: nowMs, val: val };
        return val;
    }
    function computeTableEnds(ids) {
        var hasA = false, hasB = false;
        var ends = fn('pmGetAvailableEnds');
        for (var i = 0; i < ids.length; i++) {
            var pm = getPmStructuredData616(ids[i]);
            if (!pm || !ends) continue;
            var e = ends(pm);
            if (e.hasA) hasA = true;
            if (e.hasB) hasB = true;
        }
        if (!hasA && !hasB) { hasA = true; hasB = true; }
        return { hasA: hasA, hasB: hasB, sig: (hasA ? 'A' : '') + (hasB ? 'B' : '') };
    }

    /* ---- Full table HTML (also used by the export staging path) ---- */
    function buildPmTableViewHtml616() {
        var ids = tableAssetIds();
        if (!ids.length) return '';
        var ends = tableEnds(ids);
        var subs = ['IPT N/R(A)<br>(Max/Avg)', 'VPT 110 DC LOC N/R(V)', 'TPT N/R(ms)', 'VPT 24 DC LOC N/R(V)', 'VPT N/R(V)'];

        var h = '<div class="table-responsive pm616-wrap" style="max-height:72vh;overflow-y:auto;overflow-x:auto;">';
        h += '<table class="table mb-0 ' + TABLE_CLASS + '" id="wsLiveTable" data-pm616-ends="' + ends.sig + '">';
        h += '<thead><tr>';
        h += '<th rowspan="2" class="pm616-s1">Asset Name</th>';
        h += '<th rowspan="2" class="pm616-s2">Date &amp; Time</th>';
        h += '<th rowspan="2" class="pm616-s3">Direction</th>';
        if (ends.hasA) h += '<th colspan="5" class="col-a pm616-grp-a">A End</th>';
        if (ends.hasB) h += '<th colspan="5" class="col-b pm616-grp-b">B End</th>';
        ['NWKR', 'RWKR', 'NWCR', 'RWCR'].forEach(function (k) { h += '<th rowspan="2" class="pm616-dl-h">' + k + '</th>'; });
        h += '<th rowspan="2">Last Update</th>';
        h += '</tr><tr>';
        if (ends.hasA) subs.forEach(function (s) { h += '<th class="col-a pm616-sub-a">' + s + '</th>'; });
        if (ends.hasB) subs.forEach(function (s) { h += '<th class="col-b pm616-sub-b">' + s + '</th>'; });
        h += '</tr></thead><tbody>';

        for (var i = 0; i < ids.length; i++) {
            if (!live()[ids[i]]) continue;
            var specs = rowCellSpecs(buildPmTableRowModel616(ids[i]), ends);
            h += '<tr data-id="' + esc(ids[i]) + '">' + specs.map(tdHtml).join('') + '</tr>';
        }
        h += '</tbody></table></div>';
        return h;
    }
    window.buildPmTableViewHtml = buildPmTableViewHtml616;

    /* ---- Sticky offsets: measured, so long names never overlap ---- */
    function fixSticky() {
        var tbl = document.querySelector('#wsLiveTable.' + TABLE_CLASS);
        if (!tbl) return;
        var head = tbl.tHead;
        if (!head || head.rows.length < 2) return;
        var r1 = head.rows[0];
        var c1 = r1.cells[0], c2 = r1.cells[1];
        if (!c1 || !c2) return;
        var l2 = c1.offsetWidth, l3 = c1.offsetWidth + c2.offsetWidth;
        var top2 = r1.offsetHeight;
        var css = '#wsLiveTable.' + TABLE_CLASS + ' .pm616-s2{left:' + l2 + 'px;}' +
            '#wsLiveTable.' + TABLE_CLASS + ' .pm616-s3{left:' + l3 + 'px;}' +
            '#wsLiveTable.' + TABLE_CLASS + ' thead tr:nth-child(2) th{top:' + top2 + 'px !important;}';
        var st = document.getElementById('pm616-sticky-dyn');
        if (!st) {
            st = document.createElement('style');
            st.id = 'pm616-sticky-dyn';
            (document.head || document.documentElement).appendChild(st);
        }
        if (st.textContent !== css) st.textContent = css;
    }
    var _stickyTimer = null;
    $(window).off('resize.pm616').on('resize.pm616', function () {
        if (_stickyTimer) clearTimeout(_stickyTimer);
        _stickyTimer = setTimeout(fixSticky, 120);
    });

    /* ---- Full render (G1 + B2) ---- */
    function renderPointMachineTableView616() {
        var html = buildPmTableViewHtml616();
        if (!html) return;
        $('#wsWaiting').remove();
        var $c = $('#divTelemetryLive');
        // B2: keep the vibration section (it lives in the same container).
        var $vib = $c.find('#pmVibrationTableSection').detach();
        $c.html(html);
        if ($vib.length) {
            $c.append($vib);
        } else if (fn('renderPointMachineVibrationTable')) {
            try { window.renderPointMachineVibrationTable(); } catch (e) { try { console.warn('[PM616] vibration render', e); } catch (e2) { } }
        }
        fixSticky();
        if (fn('syncDownloadVisibility')) { try { window.syncDownloadVisibility(); } catch (e) { } }
        $('#downloadContainer').show();
        window.wsUpdatedAssets = {};
    }
    window.renderPointMachineTableView = renderPointMachineTableView616;

    /* ---- G2: update one row in place ---- */
    function updateRow(assetId) {
        var tbl = document.querySelector('#wsLiveTable.' + TABLE_CLASS);
        if (!tbl || !live()[assetId]) return false;
        var row = tbl.querySelector('tbody tr[data-id="' + String(assetId).replace(/"/g, '') + '"]');
        if (!row) return false;
        var ends = { hasA: tbl.getAttribute('data-pm616-ends').indexOf('A') > -1, hasB: tbl.getAttribute('data-pm616-ends').indexOf('B') > -1 };
        var specs = rowCellSpecs(buildPmTableRowModel616(assetId), ends);
        for (var i = 0; i < specs.length; i++) {
            var s = specs[i];
            var td = row.querySelector('td[data-field="' + s.field + '"]');
            if (!td) continue;
            if (td.getAttribute('data-pm616-sig') === s.sig) continue;
            td.className = s.fullCls;
            td.innerHTML = s.html;
            td.setAttribute('data-pm616-sig', s.sig);
            if (s.stale) {
                td.setAttribute('title', s.title);
                td.setAttribute('aria-disabled', 'true');
            } else {
                td.removeAttribute('title');
                td.removeAttribute('aria-disabled');
            }
            if (!s.noFlash) {
                void td.offsetWidth;
                td.classList.add('pm616-flash');
                (function (cell) { setTimeout(function () { cell.classList.remove('pm616-flash'); }, 1150); })(td);
            }
        }
        return true;
    }
    PM616.updateRow = updateRow;

    function scheduleFullRender(delay) {
        if (window.wsRenderTimer) return;
        window.wsRenderTimer = setTimeout(function () {
            window.wsRenderTimer = null;
            if (($('#drpView').val() === 'Table') && isPmType()) renderPointMachineTableView616();
        }, delay || 200);
    }

    function updatePointMachineTableCell616(assetId /*, attrName, value, hasChanged */) {
        var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
        if (!assetInFilter(assetId)) { $row.remove(); return; }
        var tbl = document.querySelector('#wsLiveTable.' + TABLE_CLASS);
        if (!$row.length || !tbl) {
            window.wsUpdatedAssets = window.wsUpdatedAssets || {};
            window.wsUpdatedAssets[assetId] = true;
            scheduleFullRender(200);
            return;
        }
        // A newly reported end (A/B) needs new columns.
        if (tableEnds(tableAssetIds()).sig !== tbl.getAttribute('data-pm616-ends')) {
            scheduleFullRender(200);
            return;
        }
        updateRow(assetId);
    }
    window.updatePointMachineTableCell = updatePointMachineTableCell616;

    /* executeUIUpdate/renderWsTable call renderPmTableView on every batch;
       update rows in place when the table already matches the data. */
    function renderPmTableView616() {
        var tbl = document.querySelector('#wsLiveTable.' + TABLE_CLASS);
        if (!tbl || !document.getElementById('divTelemetryLive') ||
            !$.contains(document.getElementById('divTelemetryLive'), tbl)) {
            return renderPointMachineTableView616();
        }
        var ids = tableAssetIds().filter(function (id) { return !!live()[id]; });
        var rows = tbl.querySelectorAll('tbody tr[data-id]');
        var same = rows.length === ids.length;
        if (same) {
            for (var i = 0; i < rows.length; i++) {
                if (ids.indexOf(rows[i].getAttribute('data-id')) === -1) { same = false; break; }
            }
        }
        if (!same || tableEnds(ids).sig !== tbl.getAttribute('data-pm616-ends')) {
            return renderPointMachineTableView616();
        }
        var upd = Object.keys(window.wsUpdatedAssets || {});
        var targets = upd.length ? upd : ids;
        for (var j = 0; j < targets.length; j++) {
            if (ids.indexOf(String(targets[j])) !== -1) updateRow(targets[j]);
        }
        fixSticky();
        // The vibration section is kept up to date by executeUIUpdate
        // (updatePointMachineVibrationRows) right after this call.
        $('#downloadContainer').show();
    }
    window.renderPmTableView = renderPmTableView616;
    PM616.renderTable = renderPointMachineTableView616;

    /* ---- Export: flatten the 2 header rows into aligned rows ---- */
    var _origCollect = fn('tlCollectExportSections');
    if (_origCollect) {
        window.tlCollectExportSections = function (scope) {
            var tables = (scope && scope.tables) || [];
            var out = [];
            for (var i = 0; i < tables.length; i++) {
                var tbl = tables[i];
                if (!(tbl.classList && tbl.classList.contains(TABLE_CLASS))) {
                    Array.prototype.push.apply(out, _origCollect({ tables: [tbl], release: function () { } }) || []);
                    continue;
                }
                var sec = exportPmTable(tbl);
                if (sec) out.push(sec);
            }
            return out;
        };
    }
    function cellText(c) {
        return String(c.innerText !== undefined ? c.innerText : c.textContent || '').replace(/\s*\n\s*/g, ' ').trim();
    }
    // Header text as written (the Aurora CSS upper-cases innerText).
    function headText(th) {
        var tmp = document.createElement('div');
        tmp.innerHTML = String(th.innerHTML || '').replace(/<br\s*\/?>/gi, ' ');
        return String(tmp.textContent || '').replace(/\s+/g, ' ').trim();
    }
    function exportPmTable(tbl) {
        var bodyTrs = tbl.querySelectorAll('tbody tr');
        if (!bodyTrs.length) return null;
        var grid = [];
        var hrows = tbl.tHead ? tbl.tHead.rows : [];
        for (var r = 0; r < hrows.length; r++) {
            grid[r] = grid[r] || [];
            var col = 0;
            for (var c = 0; c < hrows[r].cells.length; c++) {
                var th = hrows[r].cells[c];
                while (grid[r][col] !== undefined) col++;
                var cs = parseInt(th.getAttribute('colspan'), 10) || 1;
                var rs = parseInt(th.getAttribute('rowspan'), 10) || 1;
                var txt = headText(th);
                for (var rr = 0; rr < rs; rr++) {
                    grid[r + rr] = grid[r + rr] || [];
                    for (var cc = 0; cc < cs; cc++) {
                        grid[r + rr][col + cc] = (rr === 0 && cc === 0) ? txt : '';
                    }
                }
                col += cs;
            }
        }
        var body = [];
        bodyTrs.forEach(function (tr) {
            var row = [];
            tr.querySelectorAll('td').forEach(function (td) { row.push(cellText(td)); });
            if (row.length) body.push(row);
        });
        var width = body.length ? body[0].length : 0;
        var head = grid.map(function (g) {
            var row = [];
            for (var k = 0; k < Math.max(width, g.length); k++) row.push(g[k] === undefined ? '' : g[k]);
            return row;
        });
        return { label: tbl.getAttribute('data-export-label') || '', head: head, body: body };
    }
    PM616.exportPmTable = exportPmTable;

    /* ======================================================================
     * G5 — waveform graph from a table value (v616 27781-27932)
     * ==================================================================== */
    var _arrToken = 0, _arrXhr = null;
    function showTableArrayGraph(assetId, direction, type, opTimestampDevice) {
        var baseMap = {
            AC: direction === 'R' ? 6000 : 1000, AV: direction === 'R' ? 7000 : 2000,
            BC: direction === 'R' ? 8000 : 3000, BV: direction === 'R' ? 9000 : 4000
        };
        if (!baseMap[type]) return;
        var attrId = baseMap[type] + 1;
        var dirLabel = direction === 'R' ? 'Reverse' : 'Normal';
        var typeLabels = { AC: 'A Current (A)', AV: 'A Voltage (V)', BC: 'B Current (A)', BV: 'B Voltage (V)' };
        var units = { AC: 'A', AV: 'V', BC: 'A', BV: 'V' };
        var colors = { AC: '#2563eb', AV: '#059669', BC: '#dc2626', BV: '#d97706' };
        var title = ptName(assetId) + ' — ' + dirLabel + ' ' + typeLabels[type];

        var opTs = deviceMs(opTimestampDevice);
        if (!opTimestampDevice || isNaN(opTs)) { warn('No operation timestamp available for this row.', 'Graph'); return; }
        var od = new Date(opTs);
        var opDayLabel = pad2(od.getDate()) + '/' + pad2(od.getMonth() + 1) + '/' + od.getFullYear();

        var modalHtml = '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
            '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1200px;width:96%;">' +
            '<div class="rdpms-graph-header">' +
            '<h6 style="margin:0;font-size:18px;font-weight:700;display:flex;align-items:center;gap:10px;">' +
            '<i class="fas fa-chart-line"></i> ' + esc(title) + '</h6>' +
            '<div style="display:flex;align-items:center;gap:8px;">' +
            '<button class="rdpms-graph-fs" onclick="toggleGraphFullscreen(this)" title="Toggle Fullscreen" style="background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.12);border-radius:10px;color:rgba(255,255,255,0.75);width:34px;height:34px;cursor:pointer;font-size:14px;display:inline-flex;align-items:center;justify-content:center;"><i class="fas fa-expand"></i></button>' +
            '<button class="rdpms-graph-close" onclick="closeGraphModal()">&times;</button>' +
            '</div></div>' +
            '<div class="rdpms-graph-body" style="padding:20px;background:transparent;">' +
            '<div style="padding:10px 16px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);border-radius:10px;display:flex;align-items:center;gap:8px;margin-bottom:12px;flex-wrap:wrap;">' +
            '<i class="fas fa-clock" style="color:#22d3ee;font-size:13px;"></i>' +
            '<span style="font-size:13px;font-weight:600;color:rgba(255,255,255,0.85);" id="singleArrTimeRange">Loading...</span>' +
            '<span style="font-size:12px;color:rgba(255,255,255,0.5);">&nbsp;·&nbsp;full day ' + opDayLabel + '</span>' +
            '</div>' +
            '<div id="singleArrLoading" style="display:flex;align-items:center;justify-content:center;height:450px;color:rgba(255,255,255,0.6);gap:10px;">' +
            '<i class="fas fa-spinner fa-spin fa-lg"></i> Loading full-day array data...</div>' +
            '<div id="singleArrChartDiv" style="width:100%;height:500px;display:none;background:#0a1228;border-radius:10px;"></div>' +
            '<div id="singleArrError" style="display:none;text-align:center;padding:50px;color:#fb7185;"></div>' +
            '</div></div></div>';

        $('#rdpmsGraphOverlay').remove();
        $('body').append(modalHtml);
        window._singleArrParams = {
            assetId: assetId, attrId: attrId, title: title, unit: units[type], color: colors[type],
            type: type, opTimestamp: opTs, pm616: true
        };
        loadTableArrayData616();
    }
    function loadTableArrayData616() {
        var p = window._singleArrParams;
        if (!p || !p.opTimestamp) return;
        var $ld = $('#singleArrLoading'), $ch = $('#singleArrChartDiv'), $er = $('#singleArrError'), $tr = $('#singleArrTimeRange');
        $ld.show(); $ch.hide(); $er.hide();

        var dayStart = new Date(p.opTimestamp); dayStart.setHours(0, 0, 0, 0);
        var dayEnd = new Date(p.opTimestamp); dayEnd.setHours(23, 59, 59, 999);
        var now = new Date();
        if (dayEnd > now) dayEnd = now;
        $tr.text('Loading...');

        if (_arrXhr) { _arrXhr._pm616Aborted = true; try { _arrXhr.abort(); } catch (e) { } }
        var token = ++_arrToken;
        var url = histBase() + '?assetId=' + encodeURIComponent(p.assetId) + '&startDate=' + fmtApi(dayStart) + '&endDate=' + fmtApi(dayEnd);
        _arrXhr = $.ajax({
            url: url, type: 'GET', dataType: 'json', timeout: 75000,
            success: function (response) {
                if (token !== _arrToken || window._singleArrParams !== p) return;
                _arrXhr = null;
                $ld.hide();
                if (response && response.error) {
                    $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;"></i>Failed to load data: ' + esc(response.error)).show();
                    return;
                }
                if (!response || !response.Data || !response.Data.length) {
                    $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No data available for this day').show();
                    return;
                }
                var ops = [], seen = {};
                response.Data.forEach(function (item) {
                    if (!item || parseInt(item.AttributeId, 10) !== p.attrId || !item.Values) return;
                    for (var k in item.Values) {
                        var en = item.Values[k];
                        if (!en || typeof en.Value !== 'string' || en.Value.indexOf(',') === -1) continue;
                        var ts = en.Timestamp ? en.Timestamp.TimestampDevice : null;
                        if (!ts || String(ts).indexOf('0001') >= 0) continue;
                        var t = deviceMs(ts);
                        if (isNaN(t) || t <= 0 || seen[t]) continue;
                        seen[t] = true;
                        var arr = en.Value.split(',').map(function (v) { return parseFloat(String(v).trim()); })
                            .filter(function (v) { return !isNaN(v); })
                            .map(function (v) { return Number(zf(v)); });
                        if (arr.length) ops.push({ timestamp: t, array: arr, tsStr: ts });
                    }
                });
                if (!ops.length) {
                    $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No array data found for this operation<br><small>Attribute ID: ' + p.attrId + '</small>').show();
                    return;
                }
                ops.sort(function (a, b) { return a.timestamp - b.timestamp; });
                var matched = ops[0], minDiff = Math.abs(ops[0].timestamp - p.opTimestamp);
                for (var i = 1; i < ops.length; i++) {
                    var diff = Math.abs(ops[i].timestamp - p.opTimestamp);
                    if (diff < minDiff) { minDiff = diff; matched = ops[i]; }
                }
                var TOL = 5000;
                var d = new Date(matched.timestamp);
                $ch.show();
                $tr.text(pad2(d.getDate()) + '/' + pad2(d.getMonth() + 1) + ' ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes()) + ':' + pad2(d.getSeconds()) +
                    (minDiff > TOL ? '  (nearest match, Δ' + Math.round(minDiff / 1000) + 's)' : ''));
                if (fn('renderSingleArrayByTimestamp')) window.renderSingleArrayByTimestamp([matched], p.title, p.unit, p.color);
            },
            error: function (xhr, status, error) {
                if ((xhr && xhr._pm616Aborted) || status === 'abort' || token !== _arrToken) return;
                _arrXhr = null;
                $ld.hide();
                var msg = status === 'timeout' ? 'The server did not respond in time.' : ('Failed to load data: ' + (error || 'Connection error'));
                $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;"></i>' + esc(msg) +
                    '<div style="margin-top:14px;"><button type="button" class="pm616-retry" id="pm616ArrRetry"><i class="fas fa-redo"></i> Retry</button></div>').show();
                $('#pm616ArrRetry').off('click').on('click', loadTableArrayData616);
            }
        });
    }
    PM616.showTableArrayGraph = showTableArrayGraph;
    PM616.loadTableArrayData = loadTableArrayData616;

    $(document).off('click.pm616arr').on('click.pm616arr', 'a.pm-table-array-link[data-pm616-type]', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var a = this;
        showTableArrayGraph(a.getAttribute('data-pm616-aid'), a.getAttribute('data-pm616-dir'),
            a.getAttribute('data-pm616-type'), a.getAttribute('data-pm616-ts'));
    });

    /* ======================================================================
     * History-API rows (shared by G4 modal and B1 inline panel)
     * v616 29903-29925
     * ==================================================================== */
    function buildDayHistoryRows(data) {
        var map = {};
        var decode = fn('decodePmAttribute');
        if (!decode) return [];
        (data || []).forEach(function (at) {
            if (!at || !at.Values) return;
            var dec = decode(at.AttributeId);
            if (!dec || dec.metric === 'Array') return;
            for (var k in at.Values) {
                if (!Object.prototype.hasOwnProperty.call(at.Values, k)) continue;
                var en = at.Values[k];
                if (!en || !en.Timestamp || en.Value === undefined || en.Value === null) continue;
                var ms = tsMsAny(en.Timestamp.TimestampDevice);
                if (isNaN(ms)) ms = tsMsAny(en.Timestamp.TimestampLocal);
                if (isNaN(ms) || ms <= 0) continue;
                var sec = Math.floor(ms / 1000) * 1000;
                var key = dec.direction + '|' + sec;
                if (!map[key]) map[key] = { direction: dec.direction, ms: sec, A: {}, B: {} };
                var end = (dec.type === 'AC' || dec.type === 'AV') ? 'A' : 'B';
                var kind = (dec.type === 'AC' || dec.type === 'BC') ? 'C' : 'V';
                var n = parseFloat(en.Value);
                if (!isNaN(n)) map[key][end][kind + dec.metric] = n;
            }
        });
        var rows = [];
        for (var rk in map) { if (Object.prototype.hasOwnProperty.call(map, rk)) rows.push(map[rk]); }
        rows.sort(function (a, b) { return b.ms - a.ms; });
        return rows;
    }
    function hms(ms) { var d = new Date(ms); return pad2(d.getHours()) + ':' + pad2(d.getMinutes()) + ':' + pad2(d.getSeconds()); }
    function histNum(v, dec, naHtml) {
        if (v === undefined || v === null || isNaN(v)) return naHtml;
        var z = Number(zf(v));
        return dec === 0 ? String(Math.round(z)) : z.toFixed(2);
    }
    function fetchDayHistory(assetId, start, end) {
        var url = histBase() + '?assetId=' + encodeURIComponent(assetId) + '&startDate=' + fmtApi(start) + '&endDate=' + fmtApi(end);
        return $.ajax({ url: url, type: 'GET', dataType: 'json', timeout: 75000 });
    }
    // Lower bound only (as v616): a device clock slightly ahead of the
    // browser must not hide the newest operations.
    function sameDay(rows, start) {
        var s = start.getTime();
        return rows.filter(function (r) { return r.ms >= s; });
    }
    PM616.buildDayHistoryRows = buildDayHistoryRows;

    /* ======================================================================
     * G4 — same-day history modal (v616 29840-30018)
     * ==================================================================== */
    var _histReq = 0;
    function renderDayHistoryTable(rows) {
        if (!rows.length) {
            return '<div class="pm616-hist-wrap"><div style="padding:30px;text-align:center;">No operations recorded today for this point machine.</div></div>';
        }
        var nN = 0, nR = 0;
        rows.forEach(function (r) { if (r.direction === 'Reverse') nR++; else nN++; });
        var na = '<span class="pm616-hist-na">' + DASH + '</span>';
        var h = '<div class="pm616-hist-wrap">';
        h += '<div class="pm616-hist-sum"><span><b>' + rows.length + '</b> operations · Normal <b class="n">' + nN +
            '</b> · Reverse <b class="r">' + nR + '</b></span><span>' + hms(rows[rows.length - 1].ms) + ' – ' +
            hms(rows[0].ms) + ' · newest first</span></div>';
        h += '<div class="table-responsive pm616-hist-scroll"><table class="pm616-hist-table"><thead>';
        h += '<tr><th rowspan="2">Time</th><th rowspan="2">Direction</th><th colspan="3" class="grp-a-head">A End</th><th colspan="3" class="grp-b-head">B End</th></tr>';
        h += '<tr><th class="grp-a">IPT N/R(A)<br>(Max/Avg)</th><th class="grp-a">VPT 110 DC LOC N/R(V)</th><th class="grp-a">TPT N/R(ms)</th>' +
            '<th class="grp-b">IPT N/R(A)<br>(Max/Avg)</th><th class="grp-b">VPT 110 DC LOC N/R(V)</th><th class="grp-b">TPT N/R(ms)</th></tr>';
        h += '</thead><tbody>';
        rows.forEach(function (r) {
            var rev = r.direction === 'Reverse';
            h += '<tr><td style="font-weight:600;">' + hms(r.ms) + '</td>' +
                '<td><span class="pm616-hist-dir ' + (rev ? 'r' : 'n') + '">' + r.direction + '</span></td>' +
                '<td>' + histNum(r.A.CMax, 2, na) + ' / ' + histNum(r.A.CAvg, 2, na) + '</td>' +
                '<td>' + histNum(r.A.VAvg, 2, na) + '</td>' +
                '<td>' + histNum(r.A.COperationTime, 0, na) + '</td>' +
                '<td>' + histNum(r.B.CMax, 2, na) + ' / ' + histNum(r.B.CAvg, 2, na) + '</td>' +
                '<td>' + histNum(r.B.VAvg, 2, na) + '</td>' +
                '<td>' + histNum(r.B.COperationTime, 0, na) + '</td></tr>';
        });
        h += '</tbody></table></div>';
        h += '<div class="pm616-hist-note">' + DASH + ' = end did not record data for that operation</div></div>';
        return h;
    }
    function fixHistSticky() {
        var t = document.querySelector('#model-div-alert table.pm616-hist-table');
        if (!t || !t.tHead || t.tHead.rows.length < 2) return;
        var top = t.tHead.rows[0].offsetHeight;
        if (!top) return;
        $(t.tHead.rows[1].cells).css('top', top + 'px');
    }
    function showModal() {
        var $m = $('#modalalertlogs');
        if (!$m.length) return false;
        if (typeof $.fn.modal === 'function') { $m.modal('show'); return true; }
        if (window.bootstrap && window.bootstrap.Modal) {
            var M = window.bootstrap.Modal;
            var inst = (typeof M.getOrCreateInstance === 'function') ? M.getOrCreateInstance($m[0]) : new M($m[0]);
            inst.show();
            return true;
        }
        $m.addClass('show').css('display', 'block');
        return true;
    }
    function showDayHistory(assetId) {
        if (!document.getElementById('modalalertlogs')) { warn('History dialog is not available on this page.', 'History'); return; }
        var now = new Date();
        var start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
        $('#spHeader').text(ptName(assetId) + ' — Operation History (' + pad2(now.getDate()) + '/' + pad2(now.getMonth() + 1) + '/' + now.getFullYear() + ')');
        $('#model-div-alert').html('<div class="pm616-hist-wrap"><div class="pm616-hist-loader"><div class="pm616-hist-spinner"></div><div>Loading same-day history…</div></div></div>');
        var $m = $('#modalalertlogs');
        $m.find('.modal-dialog').addClass('pm-hist-dialog');
        if (!$('#pmHistFsBtn').length) {
            var $close = $m.find('.modal-header .close-btn-custom').first();
            var btn = '<button type="button" id="pmHistFsBtn" title="Toggle full screen"><i class="fas fa-expand"></i></button>';
            if ($close.length) $close.before(btn); else $m.find('.modal-header').append(btn);
        }
        $m.off('hidden.bs.modal.pmhist').on('hidden.bs.modal.pmhist', function () {
            $(this).find('.modal-dialog').removeClass('pm-hist-dialog pm-hist-full');
            $('#pmHistFsBtn').remove();
        });
        showModal();

        var my = ++_histReq;
        fetchDayHistory(assetId, start, now).then(function (r) {
            if (my !== _histReq) return;
            if (r && r.error) {
                $('#model-div-alert').html('<div class="pm616-hist-wrap"><div class="pm616-hist-err">Failed to load history data: ' + esc(r.error) + '</div></div>');
                return;
            }
            // The API can return records from before the requested start: keep today only.
            var rows = sameDay(buildDayHistoryRows((r && r.Data) ? r.Data : []), start, now);
            $('#model-div-alert').html(renderDayHistoryTable(rows));
            fixHistSticky();
        }, function () {
            if (my !== _histReq) return;
            $('#model-div-alert').html('<div class="pm616-hist-wrap"><div class="pm616-hist-err">Failed to load history data. Please try again.</div></div>');
        });
    }
    PM616.showDayHistory = showDayHistory;
    PM616.renderDayHistoryTable = renderDayHistoryTable;

    $(document).off('click.pm616hist').on('click.pm616hist', '.pm-hist-link[data-id]', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var aid = $(this).attr('data-id');
        if (aid) showDayHistory(aid);
    });
    $(document).off('click.pm616histfs').on('click.pm616histfs', '#pmHistFsBtn', function () {
        var $d = $('#modalalertlogs .modal-dialog');
        var full = $d.toggleClass('pm-hist-full').hasClass('pm-hist-full');
        $(this).find('i').attr('class', full ? 'fas fa-compress' : 'fas fa-expand');
        fixHistSticky();
    });

    /* ======================================================================
     * B1 — inline card history (replaces the broken private parser)
     * ==================================================================== */
    function renderInlineHistory(rows) {
        if (!rows.length) {
            return '<div style="padding:30px;text-align:center;color:#94a3b8;font-size:13px;">No operations recorded today for this point machine.</div>';
        }
        var nN = 0, nR = 0;
        rows.forEach(function (r) { if (r.direction === 'Reverse') nR++; else nN++; });
        var html = '<div class="pm-hist-sum"><span>' + rows.length + ' operations · Normal ' + nN + ' · Reverse ' + nR +
            '</span><span>' + hms(rows[rows.length - 1].ms) + ' – ' + hms(rows[0].ms) + ' · newest first</span></div>';
        html += '<table class="pm-hist-tbl"><thead><tr>' +
            '<th>Time</th><th>Direction</th><th>A · IPT Max/Avg</th><th>A · VPT 110</th><th>A · TPT ms</th>' +
            '<th>B · IPT Max/Avg</th><th>B · VPT 110</th><th>B · TPT ms</th></tr></thead><tbody>';
        rows.forEach(function (r) {
            var dc = r.direction === 'Reverse' ? 'pm-hist-dir-R' : 'pm-hist-dir-N';
            html += '<tr><td>' + hms(r.ms) + '</td><td class="' + dc + '">' + r.direction + '</td>' +
                '<td>' + histNum(r.A.CMax, 2, '-') + ' / ' + histNum(r.A.CAvg, 2, '-') + '</td><td>' + histNum(r.A.VAvg, 2, '-') + '</td><td>' + histNum(r.A.COperationTime, 0, '-') + '</td>' +
                '<td>' + histNum(r.B.CMax, 2, '-') + ' / ' + histNum(r.B.CAvg, 2, '-') + '</td><td>' + histNum(r.B.VAvg, 2, '-') + '</td><td>' + histNum(r.B.COperationTime, 0, '-') + '</td></tr>';
        });
        return html + '</tbody></table>';
    }
    var _inlineReq = {};
    function toggleInlineHistory(assetId, dateCell) {
        if (!dateCell) return;
        var card = (dateCell.closest && dateCell.closest('.pm-card-wrapper')) || document.getElementById('pmCard_' + assetId);
        if (!card) return;
        var toggleIc = dateCell.querySelector('.pm-hist-toggle');
        var existing = card.querySelector('.pm-hist-inline');
        if (existing) {
            existing.parentNode.removeChild(existing);
            var all = card.querySelectorAll('.pm-hist-toggle.open');
            for (var i = 0; i < all.length; i++) all[i].classList.remove('open');
            _inlineReq[assetId] = (_inlineReq[assetId] || 0) + 1;
            return;
        }
        if (toggleIc) toggleIc.classList.add('open');
        var now = new Date();
        var start = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
        var title = ptName(assetId) + ' — Operation History (' + pad2(now.getDate()) + '/' + pad2(now.getMonth() + 1) + '/' + now.getFullYear() + ')';

        var panel = document.createElement('div');
        panel.className = 'pm-hist-inline';
        panel.setAttribute('data-hist-panel', assetId);
        panel.innerHTML = '<div class="pm-hist-inline-head"><span class="pm-hist-inline-title">' + esc(title) + '</span>' +
            '<span class="pm-hist-inline-close" title="Collapse"><i class="fas fa-times"></i></span></div>' +
            '<div class="pm-hist-inline-body"><div class="pm-hist-loader"><div class="pm-hist-spinner"></div><div>Loading same-day history…</div></div></div>';
        var tblResp = card.querySelector('.table-responsive');
        if (tblResp && tblResp.parentNode) tblResp.parentNode.insertBefore(panel, tblResp.nextSibling);
        else card.appendChild(panel);

        panel.querySelector('.pm-hist-inline-close').addEventListener('click', function () {
            if (panel.parentNode) panel.parentNode.removeChild(panel);
            var open = card.querySelectorAll('.pm-hist-toggle.open');
            for (var j = 0; j < open.length; j++) open[j].classList.remove('open');
            _inlineReq[assetId] = (_inlineReq[assetId] || 0) + 1;
        });

        var body = panel.querySelector('.pm-hist-inline-body');
        var my = _inlineReq[assetId] = (_inlineReq[assetId] || 0) + 1;
        fetchDayHistory(assetId, start, now).then(function (r) {
            if (my !== _inlineReq[assetId] || !panel.parentNode) return;
            if (r && r.error) {
                body.innerHTML = '<div style="padding:30px;text-align:center;color:#fb7185;">Failed to load history data: ' + esc(r.error) + '</div>';
                return;
            }
            body.innerHTML = renderInlineHistory(sameDay(buildDayHistoryRows((r && r.Data) ? r.Data : []), start, now));
        }, function () {
            if (my !== _inlineReq[assetId] || !panel.parentNode) return;
            body.innerHTML = '<div style="padding:30px;text-align:center;color:#fb7185;">Failed to load history data. Please try again.</div>';
        });
    }
    PM616.toggleInlineHistory = toggleInlineHistory;
    window.fnTogglePmInlineHistory = toggleInlineHistory;
    // Replace the IIFE's handler (it called the private, broken parser).
    $(document).off('click.pmInlineHist').on('click.pmInlineHist', 'td.pm-date-cell[data-hist-id] .pm-hist-toggle', function (e) {
        e.preventDefault();
        e.stopPropagation();
        var cell = this.closest('td.pm-date-cell');
        if (cell) toggleInlineHistory(cell.getAttribute('data-hist-id'), cell);
    });

    /* ======================================================================
     * G6 / B5 / G7 — indication graph
     * ==================================================================== */
    var _indXhr = null;
    var _indToken = 0;
    function abortIndication() {
        if (_indXhr) {
            _indXhr._pmAbortedByUs = true;
            try { _indXhr.abort(); } catch (e) { }
            _indXhr = null;
        }
    }
    PM616.abortIndication = abortIndication;
    PM616.indicationToken = function () { return _indToken; };

    var _origRenderInd = fn('renderPmIndicationFromApi');
    function renderPmIndicationFromApi616(data, filterIds, end, rangeStart, rangeEnd) {
        if (!_origRenderInd) return;
        _origRenderInd(data, filterIds, end, rangeStart);
        var el = document.getElementById('pmIndChartDiv');
        if (!el || !window.echarts || typeof window.echarts.getInstanceByDom !== 'function') return;
        var chart = window.echarts.getInstanceByDom(el);
        if (!chart) return;
        var endMs = (typeof rangeEnd === 'number' && !isNaN(rangeEnd)) ? rangeEnd : null;
        var opt = chart.getOption() || {};
        var patch = (opt.series || []).map(function (s) {
            var pts = (s.data || []).map(function (p) {
                var v = (p && p.value) ? p.value.slice() : (Array.isArray(p) ? p.slice() : null);
                if (!v) return p;
                v[1] = Number(zf(v[1]));
                if (p && p.value) { var q = {}; for (var k in p) q[k] = p[k]; q.value = v; return q; }
                return v;
            });
            if (endMs !== null && pts.length) {
                var last = pts[pts.length - 1];
                var lv = last && last.value ? last.value : last;
                if (lv && lv[0] < endMs) pts.push({ value: [endMs, lv[1]], symbol: 'none', symbolSize: 0 });
            }
            return { data: pts };
        });
        var upd = { series: patch };
        if (endMs !== null) upd.xAxis = { max: endMs };
        chart.setOption(upd);
    }
    window.renderPmIndicationFromApi = renderPmIndicationFromApi616;

    function indError($er, msg, retry) {
        $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;color:#ef4444;"></i>' + esc(msg) +
            (retry ? '<div style="margin-top:14px;"><button type="button" class="pm616-retry" id="pmIndRetryBtn"><i class="fas fa-redo"></i> Retry</button></div>' : '')).show();
        if (retry) $('#pmIndRetryBtn').off('click').on('click', retry);
    }
    function loadPmIndicationByRange616(assetId, startDate, endDate, filterIds, end) {
        var $ld = $('#pmIndLoading'), $ch = $('#pmIndChartDiv'), $er = $('#pmIndError'), $tr = $('#pmIndTimeRange');
        $ld.show(); $ch.hide(); $er.hide();

        var now = new Date();
        abortIndication();
        var token = ++_indToken;
        if (startDate > now) {           // B5: a future day has nothing to load
            $ld.hide();
            indError($er, 'Future dates are not allowed.', null);
            return null;
        }
        if (endDate > now) endDate = now;   // B5: never ask for the future

        function fmt(d) {
            return pad2(d.getDate()) + '/' + pad2(d.getMonth() + 1) + '/' + d.getFullYear() + ' ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes());
        }
        $tr.text(fmt(startDate) + ' — ' + fmt(endDate));
        var url = histBase() + '?assetId=' + encodeURIComponent(assetId) + '&startDate=' + fmtApi(startDate) + '&endDate=' + fmtApi(endDate);
        var retry = function () { loadPmIndicationByRange616(assetId, startDate, endDate, filterIds, end); };

        _indXhr = $.ajax({
            url: url, type: 'GET', dataType: 'json',
            // Above the server's own 60 s wait on the upstream History API.
            timeout: 75000,
            success: function (response) {
                if (token !== _indToken) return;
                _indXhr = null;
                $ld.hide();
                if (response && response.error) {
                    indError($er, 'Failed to load data: ' + response.error, retry);
                    return;
                }
                if (response && response.Data && response.Data.length > 0) {
                    $ch.show();
                    window.renderPmIndicationFromApi(response.Data, filterIds, end, startDate.getTime(), endDate.getTime());
                } else {
                    $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No indication data available for the selected range.').show();
                }
            },
            error: function (xhr, status, error) {
                if (xhr && xhr._pmAbortedByUs) return;
                if (token !== _indToken || status === 'abort') return;
                _indXhr = null;
                $ld.hide();
                var msg;
                if (status === 'timeout') msg = 'The server did not respond in time for this date. Please try again.';
                else if (xhr && xhr.status === 0) msg = 'Connection lost. Check the network and try again.';
                else msg = 'Failed to load data: ' + (error || (xhr && xhr.status ? 'HTTP ' + xhr.status : 'Connection error'));
                indError($er, msg, retry);
            }
        });
        return _indXhr;
    }
    window.loadPmIndicationByRange = loadPmIndicationByRange616;

    var _origDirGraph = fn('fnPmDirGraph');
    function dateStr(d) { return d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()); }
    window.fnPmDirGraph = function (assetId, end) {
        abortIndication();
        _indToken++;
        if (!_origDirGraph) return;
        // v617 builds the modal and binds Load; its handler and first load call
        // window.loadPmIndicationByRange (replaced above), which clamps to now.
        _origDirGraph(assetId, end);
        var $d = $('#pmIndDate');
        if (!$d.length) return;
        var refreshMax = function () { $d.attr('max', dateStr(new Date())); };
        refreshMax();
        $d.off('focus.pm616 mousedown.pm616').on('focus.pm616 mousedown.pm616', refreshMax);
        $d.off('change.pm616 blur.pm616').on('change.pm616 blur.pm616', function () {
            var v = $d.val();
            if (v && v > dateStr(new Date())) {
                $d.val(dateStr(new Date()));
                warn('Future dates are not allowed.', 'Validation');
            }
        });
        // Block a typed future date before v617's Load handler runs.
        var btn = document.getElementById('pmIndLoadBtn');
        if (btn && !btn.__pm616Guard) {
            btn.__pm616Guard = true;
            btn.addEventListener('click', function (ev) {
                var v = $d.val();
                if (v && v > dateStr(new Date())) {
                    ev.stopImmediatePropagation();
                    ev.preventDefault();
                    $d.val(dateStr(new Date()));
                    warn('Future dates are not allowed.', 'Validation');
                }
            }, true);
        }
    };

    window.closeGraphModal = function (e) {
        if (e && e.target && !$(e.target).hasClass('tl-modal-overlay') && !$(e.target).hasClass('rdpms-graph-overlay')) return;
        abortIndication();
        _indToken++;
        _arrToken++;
        if (_arrXhr) { _arrXhr._pm616Aborted = true; try { _arrXhr.abort(); } catch (ex0) { } _arrXhr = null; }
        if (window.rdpmsGraphChart) {
            try { window.rdpmsGraphChart.dispose(); } catch (ex1) { }
            window.rdpmsGraphChart = null;
        }
        if (window._singleArrChartInst) {
            try { window._singleArrChartInst.dispose(); } catch (ex2) { }
            window._singleArrChartInst = null;
        }
        try {
            var ind = document.getElementById('pmIndChartDiv');
            var ic = (ind && window.echarts && window.echarts.getInstanceByDom) ? window.echarts.getInstanceByDom(ind) : null;
            if (ic) ic.dispose();
        } catch (ex3) { }
        $(window).off('resize.rdpmsGraph').off('resize.singleArrChart').off('resize.pmIndChart');
        window._singleArrParams = null;
        // Capture the closing overlay(s) and drop their id now, so a modal opened
        // within the 250 ms fade is never found (or removed) by this close.
        var $ov = $('[id="rdpmsGraphOverlay"]');
        $ov.removeClass('tl-modal-open').attr('id', 'rdpmsGraphOverlayClosing');
        $('body').removeClass('tl-modal-active');
        setTimeout(function () { $ov.remove(); }, 250);
    };

    /* ======================================================================
     * B3 — header KR/LOC voltages follow the operation direction
     * ==================================================================== */
    window.updatePmVoltageDisplay = function (assetId, attrName, value) {
        var n = parseFloat(value);
        if (isNaN(n)) return;
        var val = Number(zf(n)).toFixed(2);
        var low = String(attrName || '').toLowerCase();
        var endKey = (low.indexOf('a end') > -1 || low.indexOf('a-end') > -1) ? 'A'
            : ((low.indexOf('b end') > -1 || low.indexOf('b-end') > -1) ? 'B' : null);
        if (!endKey) return;
        var attrDir = low.indexOf('rwkr') > -1 ? 'REVERSE' : (low.indexOf('nwkr') > -1 ? 'NORMAL' : null);
        if (!attrDir) return;
        // Match what the card header currently says for this end; otherwise use
        // the shared v617 resolver.
        var shown = $.trim($('#pmKrLabel' + endKey + '_' + assetId).text()).toUpperCase();
        var opDir = shown === 'RWKR' ? 'REVERSE' : (shown === 'NWKR' ? 'NORMAL' : null);
        if (!opDir) {
            var asset = live()[assetId];
            var pm = getPmStructuredData616(assetId);
            var r = fn('determinePmDirection') ? window.determinePmDirection(asset, pm) : null;
            opDir = (r && r.direction === 'REVERSE') ? 'REVERSE' : 'NORMAL';
        }
        if (attrDir !== opDir) return;
        var isLoc = low.indexOf('loc') > -1;
        $((isLoc ? '#pmVdc' : '#pmVnwkr') + endKey + '_' + assetId).text(val);
    };

    /* ======================================================================
     * G8 — gate card rows and the DataLogger popup
     * ==================================================================== */
    var _origBuildOpRow = fn('buildPmOperationRow');
    if (_origBuildOpRow) {
        window.buildPmOperationRow = function (assetId, operationType, timestampDevice) {
            var self = this, args = arguments;
            return withGate(assetId, secMs(timestampDevice), function () { return _origBuildOpRow.apply(self, args); });
        };
    }
    function latestOpTsForDirection(asset, dir) {
        var ids = (dir === 'Reverse') ? (window.PM_REVERSE_OPERATION_IDS || []) : (window.PM_NORMAL_OPERATION_IDS || []);
        var best = 0;
        var attrs = (asset && asset.attrs) || {};
        for (var k in attrs) {
            if (!Object.prototype.hasOwnProperty.call(attrs, k)) continue;
            var a = attrs[k];
            var id = parseInt(a.AssetAttributeId || a.AttrId || 0, 10);
            if (ids.indexOf(id) === -1) continue;
            var t = a.TimestampDevice || a.Timestamp;
            if (!t || String(t).indexOf('0001-01-01') !== -1) continue;
            var ms = secMs(t);
            if (ms > best) best = ms;
        }
        return best;
    }
    var _origBuildDataRow = fn('buildPmDataRow');
    if (_origBuildDataRow) {
        window.buildPmDataRow = function (assetId, pm) {
            var self = this;
            var asset = live()[assetId];
            if (!asset) return _origBuildDataRow.call(self, assetId, pm);
            var dres = fn('determinePmDirection') ? window.determinePmDirection(asset, pm) : null;
            var dir = (dres && dres.direction === 'REVERSE') ? 'Reverse' : 'Normal';
            var opMs = latestOpTsForDirection(asset, dir);
            if (!opMs) return _origBuildDataRow.call(self, assetId, pm);
            var gated = withGate(assetId, opMs, function () { return getPmStructuredData616(assetId); });
            if (gated && pm && pm.RDPMS) gated.RDPMS = pm.RDPMS;
            return _origBuildDataRow.call(self, assetId, gated || pm);
        };
    }
    var _origDlEvent = fn('fnShowDataLoggerEvent');
    if (_origDlEvent) {
        window.fnShowDataLoggerEvent = function (assetId) {
            var self = this, args = arguments;
            var hist = window.pmEventHistory && window.pmEventHistory[assetId];
            var last = (hist && hist.length) ? hist[hist.length - 1] : null;
            var ms = last ? secMs(last.timestampDevice) : 0;
            return withGate(assetId, ms, function () { return _origDlEvent.apply(self, args); });
        };
    }

    /* ======================================================================
     * G10 — waveform boxes and card-row links
     * ==================================================================== */
    function rowHasValue(v) { return hasVal(v); }
    function rowHasCount(v) { return hasVal(v) && parseInt(v, 10) > 0; }
    function latestHistRow(assetId) {
        var hist = (window.pmEventHistory && window.pmEventHistory[assetId]) || [];
        var best = null, bestMs = -1;
        for (var i = 0; i < hist.length; i++) {
            var ms = secMs(hist[i].timestampDevice);
            if (ms >= bestMs) { bestMs = ms; best = hist[i]; }
        }
        return best;
    }
    function syncWaveFlags(assetId, row) {
        if (!row) return;
        $('#pmSeeMore_' + assetId)
            .attr('data-has-ac', rowHasCount(row.aAcCount) ? '1' : '0')
            .attr('data-has-av', rowHasValue(row.aAvAvg) ? '1' : '0')
            .attr('data-has-bc', rowHasCount(row.bBcCount) ? '1' : '0')
            .attr('data-has-bv', rowHasValue(row.bBvAvg) ? '1' : '0');
    }
    PM616.syncWaveFlags = syncWaveFlags;
    if (!fn('pmRowHasValue')) window.pmRowHasValue = rowHasValue;
    if (!fn('pmRowHasCount')) window.pmRowHasCount = rowHasCount;
    if (!fn('pmSyncWaveFlags')) window.pmSyncWaveFlags = syncWaveFlags;

    window.pmToggleSeeMore = function (assetId) {
        var $s = $('#pmSeeMore_' + assetId);
        var $btn = $('#pmCard_' + assetId + ' .pm-btn-seemore');
        if (!window.pmSeeMoreRendering) window.pmSeeMoreRendering = {};
        var rendering = window.pmSeeMoreRendering;
        if ($s.is(':visible')) {
            $s.slideUp(300);
            $btn.text('See More');
            rendering[assetId] = false;
            return;
        }
        var dirText = $.trim($('#pmDirBadgeA_' + assetId).text() || $('#pmDirBadgeB_' + assetId).text());
        var curDir = (dirText.indexOf('REVERSE') > -1) ? 'R' : 'N';
        $('#pmWaveDir_' + assetId).text(curDir === 'R' ? 'Reverse Operation' : 'Normal Operation');
        syncWaveFlags(assetId, latestHistRow(assetId));
        $s.find('.pm-wave-box').each(function () {
            var $box = $(this);
            var type = $box.attr('data-type');
            if (!type) {
                var inner = $box.find('[id^="pmWave_"]').attr('id') || '';
                var m = inner.match(/_(AC|AV|BC|BV)$/);
                type = m ? m[1] : '';
            }
            var flag = type ? $s.attr('data-has-' + type.toLowerCase()) : undefined;
            var hasData = flag !== '0';
            $box.toggle(String($box.attr('data-dir')) === curDir && hasData);
        });
        $s.slideDown(300, function () {
            if (rendering[assetId]) return;
            rendering[assetId] = true;
            setTimeout(function () {
                if (fn('updatePmWaveforms')) { try { window.updatePmWaveforms(assetId); } catch (e) { } }
                rendering[assetId] = false;
            }, 150);
        });
        $btn.text('See Less');
    };

    var TITLES = {
        AC: 'Click to view A Current waveform', AV: 'Click to view A Voltage waveform',
        BC: 'Click to view B Current waveform', BV: 'Click to view B Voltage waveform'
    };
    function setValueCell($td, assetId, dirPrefix, type, hasData, displayText, valClass) {
        if (!$td || !$td.length) return;
        var linkClass = (type === 'AV' || type === 'BV') ? 'pm-voltage-link' : 'pm-current-link';
        var html;
        if (hasData) {
            html = '<a href="javascript:void(0)" class="' + linkClass + '" onclick="pmShowSingleArrayGraph(\'' + esc(assetId) + '\',\'' + dirPrefix + '\',\'' + type + '\')" title="' + TITLES[type] + '">' +
                '<span class="' + valClass + '">' + esc(displayText) + '</span></a>';
        } else {
            html = '<span class="' + valClass + ' pm-no-data" style="cursor:default;opacity:0.6;">' + esc(displayText) + '</span>';
        }
        $td.html(html);
    }
    // Round 2: one placeholder for card rows — the v616 em-dash. History rows
    // written by v617's buildPmOperationRow still carry '--'; dv() maps every
    // absent form ('--', '-', '—') to '—' at render time so rows never mix.
    function dv(v) { return hasVal(v) ? v : DASH; }
    function currentText(max, avg, count) {
        // v616 27500: no Max for this operation -> the whole cell collapses.
        if (!hasVal(max)) return DASH + ' / ' + DASH + ' (0)';
        return max + ' / ' + dv(avg) + ' (' + (hasVal(count) ? count : '0') + ')';
    }
    function applyRowLinks($tr, assetId, row, showA, showB, startIdx) {
        var dirPrefix = (row.operationType === 'Reverse') ? 'R' : 'N';
        var $tds = $tr.children('td');
        var ci = startIdx;
        if (showA && row.showA !== false) {
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'AC', rowHasCount(row.aAcCount), currentText(row.aAcMax, row.aAcAvg, row.aAcCount), 'pm-current-val');
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'AV', rowHasValue(row.aAvAvg), dv(row.aAvAvg), 'pm-voltage-val');
            $tds.eq(ci++).text(dv(row.aAcOT));
        }
        if (showB && row.showB !== false) {
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'BC', rowHasCount(row.bBcCount), currentText(row.bBcMax, row.bBcAvg, row.bBcCount), 'pm-current-val-b');
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'BV', rowHasValue(row.bBvAvg), dv(row.bBvAvg), 'pm-voltage-val');
            $tds.eq(ci++).text(dv(row.bBcOT));
        }
    }
    PM616.setValueCell = setValueCell;
    if (!fn('pmSetValueCell')) window.pmSetValueCell = function ($td, assetId, dirPrefix, type, hasData, displayText, valClass) {
        setValueCell($td, assetId, dirPrefix, type, hasData, displayText, valClass);
    };

    window.updatePmRowInPlace = function (assetId, row, showA, showB, oldTs) {
        var $tbody = $('#pmTbody_' + assetId);
        if (!$tbody.length) return;
        var $tr;
        if (oldTs) $tr = $tbody.find('tr[data-ts="' + oldTs + '"][data-optype="' + row.operationType + '"]');
        if (!$tr || !$tr.length) $tr = $tbody.find('tr[data-optype="' + row.operationType + '"]').first();
        if (!$tr.length) {
            window.refreshPmOperationTable(assetId, (window.pmEventHistory && window.pmEventHistory[assetId]) || [], showA, showB);
            return;
        }
        $tr.attr('data-ts', row.timestampDevice);
        var $tds = $tr.children('td');
        var $date = $tds.eq(0);
        var tog = $date.find('.pm-hist-toggle').detach();
        $date.text(row.date);
        if (tog.length) $date.append(tog);
        $tds.eq(4).html('<a href="javascript:void(0)">C</a> / <a href="javascript:void(0)">V</a> T(' + esc(row.totalOT) + ')');
        applyRowLinks($tr, assetId, row, showA, showB, 5);
        $tds.slice(4).addClass('pm-val-flash-anim');
        setTimeout(function () { $tr.children('td').removeClass('pm-val-flash-anim'); }, 1200);
        syncWaveFlags(assetId, latestHistRow(assetId));
    };

    window.refreshPmOperationTable = function (assetId, hist, showA, showB) {
        var $tbody = $('#pmTbody_' + assetId);
        if (!$tbody.length) return;
        var sorted = (hist || []).slice().sort(function (a, b) { return secMs(b.timestampDevice) - secMs(a.timestampDevice); });
        var rows = '';
        for (var i = 0; i < sorted.length; i++) {
            var r = sorted[i];
            var dp = (r.operationType === 'Reverse') ? 'R' : 'N';
            rows += '<tr data-ts="' + esc(r.timestampDevice || '') + '" data-optype="' + esc(r.operationType || '') + '"' + (i === 0 ? ' class="pm-val-flash-anim"' : '') + '>';
            rows += '<td class="pm-date-cell" data-hist-id="' + esc(assetId) + '">' + esc(r.date) + '</td>';
            rows += '<td>' + esc(r.name) + '</td>';
            rows += '<td>' + esc(r.dir) + '</td>';
            rows += '<td><a href="javascript:void(0)" onclick="pmShowChart(\'' + esc(assetId) + '\',\'' + dp + '_AC\')">C</a> / ' +
                '<a href="javascript:void(0)" onclick="pmShowChart(\'' + esc(assetId) + '\',\'' + dp + '_AV\')">V</a></td>';
            rows += '<td><a href="javascript:void(0)">C</a> / <a href="javascript:void(0)">V</a> T(' + esc(r.totalOT) + ')</td>';
            if (r.showA !== false && showA) rows += '<td class="col-a"></td><td class="col-a"></td><td class="col-a"></td>';
            if (r.showB !== false && showB) rows += '<td class="col-b"></td><td class="col-b"></td><td class="col-b"></td>';
            rows += '</tr>';
        }
        if (!rows) return;
        $tbody.html(rows);
        $tbody.children('tr').each(function (idx) {
            applyRowLinks($(this), assetId, sorted[idx], showA, showB, 5);
        });
        syncWaveFlags(assetId, latestHistRow(assetId));
    };

    var _origPrepend = fn('prependPmOperationRow');
    if (_origPrepend) {
        window.prependPmOperationRow = function (assetId, row, showA, showB) {
            var hadTbody = $('#pmTbody_' + assetId).length > 0;
            var res = _origPrepend.apply(this, arguments);
            if (hadTbody) {
                var $tr = $('#pmTbody_' + assetId).children('tr').first();
                if ($tr.length && $tr.attr('data-ts') === String(row.timestampDevice || '')) {
                    applyRowLinks($tr, assetId, row, showA, showB, 5);
                }
            }
            syncWaveFlags(assetId, latestHistRow(assetId));
            return res;
        };
    }
    // buildPmDataRow renders its own links; only the flags need syncing.
    var _gatedDataRow = window.buildPmDataRow;
    if (typeof _gatedDataRow === 'function') {
        window.buildPmDataRow = function (assetId) {
            var res = _gatedDataRow.apply(this, arguments);
            syncWaveFlags(assetId, latestHistRow(assetId));
            return res;
        };
    }

    /* ======================================================================
     * G11 — GetUserAssetInfo for Point Machine (v616 3523-3618, 4155-4175)
     * ==================================================================== */
    function uaiTrim(v) { return (v === null || v === undefined) ? '' : String(v).trim(); }
    function loadPointMachineUserAssetInfo(siteId, callback) {
        var done = function () { if (typeof callback === 'function') callback(); };
        if (!siteId || siteId === '0') { done(); return; }
        $.ajax({
            url: '/FRS25/Telemetry/GetUserAssetInfo?siteId=' + encodeURIComponent(siteId),
            type: 'GET', dataType: 'json', timeout: 30000,
            success: function (rows) {
                try {
                    if (rows && !Array.isArray(rows)) rows = rows.Data || rows.data || rows.mUserAssetInfo || rows.Result || rows.result || rows;
                    if (!Array.isArray(rows)) { done(); return; }
                    var simple = window.userAssetSimpleMap || (window.userAssetSimpleMap = {});
                    var dlm = window.userAssetDataloggerMap || (window.userAssetDataloggerMap = {});
                    var cCount = 0, dCount = 0;
                    rows.forEach(function (r) {
                        if (!r) return;
                        var aid = (r.AssetId !== null && r.AssetId !== undefined) ? String(r.AssetId) : '';
                        var attrId = (r.AssetAttributeId !== null && r.AssetAttributeId !== undefined) ? r.AssetAttributeId : r.AttributeId;
                        if (!aid || attrId === null || attrId === undefined) return;
                        var idStr = String(attrId);
                        var role = uaiTrim(r.RoleType).toLowerCase();
                        var name = role === 'd' ? (uaiTrim(r.AttributeName) || uaiTrim(r.AliasName))
                            : (uaiTrim(r.AliasName) || uaiTrim(r.AttributeName));
                        if (!name) return;
                        var entry = {
                            name: name, attributeName: r.AttributeName || '', aliasName: r.AliasName || '',
                            assetName: r.AssetName || '', assetTypeId: r.AssetTypeId, siteId: r.SiteId,
                            roleType: role, multiplication: r.Multiplication, absolute: r.Absolute,
                            minValue: r.MinValue, maxValue: r.MaxValue
                        };
                        var keyIds = [idStr];
                        var roleId = uaiTrim(r.Role);
                        if (roleId && /^\d+$/.test(roleId) && roleId !== idStr) keyIds.push(roleId);
                        keyIds.forEach(function (kid) {
                            var mk = aid + '_' + kid;
                            if (role === 'd') dlm[mk] = entry; else simple[mk] = entry;
                            if (window.assetAttributeMap) {
                                window.assetAttributeMap[kid] = name;
                                var num = parseInt(kid, 10);
                                if (!isNaN(num)) window.assetAttributeMap[num] = name;
                            }
                            if (window.fallbackAliasById) window.fallbackAliasById[kid] = name;
                        });
                        if (window.assetAttributeByName) window.assetAttributeByName[name] = name;
                        if (role === 'd') dCount++; else cCount++;
                    });
                    window.assetAttributeLoaded = true;
                    try { console.log('[PM616 UserAssetInfo] site=' + siteId + ' — ' + cCount + ' analog (c), ' + dCount + ' datalogger (d)'); } catch (e) { }
                } catch (e) {
                    try { console.warn('[PM616 UserAssetInfo] parse error', e); } catch (e2) { }
                }
                done();
            },
            error: function (xhr) {
                try { console.warn('[PM616 UserAssetInfo] fetch failed: ' + (xhr && xhr.status)); } catch (e) { }
                done();
            }
        });
    }
    PM616.loadPointMachineUserAssetInfo = loadPointMachineUserAssetInfo;
    if (!fn('loadPointMachineUserAssetInfo')) window.loadPointMachineUserAssetInfo = loadPointMachineUserAssetInfo;

    var _origLoadUai = fn('loadUserAssetInfo');
    if (_origLoadUai) {
        window.loadUserAssetInfo = function (siteId, callback) {
            var atId = $('#drpAssetType').val() || window.wsCurrentAssetTypeId || '0';
            var isPm = fn('isPointMachineAssetTypeForBulkSkip') ? window.isPointMachineAssetTypeForBulkSkip(atId) : String(atId) === '3';
            if (!siteId || siteId === '0' || !isPm) return _origLoadUai.apply(this, arguments);
            if (window.userAssetInfoLoaded && window.userAssetInfoSiteId === String(siteId) &&
                window.userAssetInfoAssetTypeId === String(atId)) {
                if (typeof callback === 'function') callback();
                return;
            }
            if (fn('seedPointMachineAttributeFallbacks')) { try { window.seedPointMachineAttributeFallbacks(); } catch (e) { } }
            loadPointMachineUserAssetInfo(siteId, function () {
                if (fn('markUserAssetInfoLoadedForAssetType')) window.markUserAssetInfoLoadedForAssetType(siteId, atId);
                if (typeof callback === 'function') callback();
            });
        };
    }

    /* ======================================================================
     * PM ROUND 2 — G9: ONE direction/operation shared by card, table, header
     * (v616 26013-26104 resolvePmDirectionLikeTable / pmEnergisedDirection /
     *  resolvePmOperationForCurrentIndication / pmIndicationValueById,
     *  27252-27543 buildPmDataRow, 27140-27237 + 31483-31510 card header)
     * The card no longer resolves A and B separately and no longer falls back
     * across directions: an indication is looked up by attribute ID only and
     * shows the em-dash when absent — exactly like the table.
     * ==================================================================== */

    // v616 26104: numeric indication value by AssetAttributeId. No fallback.
    function indicationValueById(pm, id) {
        if (!pm || !pm.RDPMS || !id) return null;
        for (var k in pm.RDPMS) {
            if (!Object.prototype.hasOwnProperty.call(pm.RDPMS, k)) continue;
            var rv = pm.RDPMS[k];
            var rid = parseInt((rv.source && (rv.source.AssetAttributeId || rv.source.AttrId)) || rv.AttrId || rv.AssetAttributeId || 0, 10);
            if (rid === parseInt(id, 10)) {
                var n = parseFloat(rv.value);
                return isNaN(n) ? null : n;
            }
        }
        return null;
    }
    PM616.indicationValueById = indicationValueById;
    window.pmIndicationValueById = indicationValueById;

    // v616 parity names, all built on the shared resolver.
    window.pmEnergisedDirection = energisedDirection;
    window.resolvePmDirectionLikeTable = function (assetId, pm) {
        if (!pm) pm = getPmStructuredData616(assetId);
        return resolveDirection(assetId, pm);
    };
    window.resolvePmOperationForCurrentIndication = function (assetId, pm) {
        if (!pm) pm = getPmStructuredData616(assetId);
        return resolveOperationForDirection(assetId, pm, resolveDirection(assetId, pm));
    };

    // determinePmDirection: energised KR wins first (v616 pmEnergisedDirection),
    // then the original v617 logic (freshness, then the max-value formula).
    // Everything in telemetrylive.js that still calls determinePmDirection now
    // agrees with the table's direction.
    var _origDetermineDir = fn('determinePmDirection');
    if (_origDetermineDir) {
        window.determinePmDirection = function (asset, pm) {
            var r = _origDetermineDir.apply(this, arguments) ||
                { direction: 'NORMAL', nwkrMax: 0, rwkrMax: 0, source: 'default' };
            var en = energisedDirection(asset);
            if (en) {
                r.direction = (en === 'Reverse') ? 'REVERSE' : 'NORMAL';
                r.source = 'energised';
            }
            return r;
        };
    }

    // ---- card data rows: v616 buildPmDataRow, direction + operation ts from
    // the SAME resolver as the table, values gated against that operation ----
    function buildPmDataRow616(assetId, pm) {
        var asset = live()[assetId];
        if (!asset) return;
        if (!pm) pm = getPmStructuredData616(assetId);
        if (!pm) return;
        var name = ptName(assetId);

        var $tbody = $('#pmTbody_' + assetId);
        var showA = $tbody.attr('data-show-a') === '1';
        var showB = $tbody.attr('data-show-b') === '1';
        var showCombine = $tbody.attr('data-show-combine') === '1';
        if (!showA && !showB && window.pmEndInfo && window.pmEndInfo[assetId]) {
            var ends = window.pmEndInfo[assetId];
            showA = ends.hasA || (!ends.hasA && !ends.hasB);
            showB = ends.hasB || (!ends.hasA && !ends.hasB);
        }
        if (!showCombine && window.pmCombineColumnStatus && window.pmCombineColumnStatus[assetId]) showCombine = true;
        if (!showA && !showB) { showA = true; showB = true; }

        // G9: indication-first direction; the operation timestamp comes ONLY
        // from that direction's operation group (v616 27313-27380).
        var useDir = resolveDirection(assetId, pm);
        var op = resolveOperationForDirection(assetId, pm, useDir);
        var ts = (op && op.timestampDevice) || '';
        var isReverse = (useDir === 'Reverse');
        var dirText = isReverse ? 'Reverse Operation' : 'Normal Operation';

        var dateStr = DASH;
        if (ts) {
            var iso = String(ts).match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})/);
            if (iso) {
                dateStr = iso[1] + '-' + iso[2] + '-' + iso[3] + ' ' + iso[4] + ':' + iso[5] + ':' + iso[6];
            } else {
                var dt0 = new Date(ts);
                if (!isNaN(dt0.getTime())) {
                    dateStr = dt0.getFullYear() + '-' + pad2(dt0.getMonth() + 1) + '-' + pad2(dt0.getDate()) + ' ' +
                        pad2(dt0.getHours()) + ':' + pad2(dt0.getMinutes()) + ':' + pad2(dt0.getSeconds());
                }
            }
        }

        var opMs = secMs(ts);
        var dd = pm[useDir] || { AC: {}, AV: {}, BC: {}, BV: {} };
        var g = function (mo) { return entryMatchesOperation(mo, opMs) ? mo : null; };
        var F = window.pmFmt, FI = window.pmFmtInt;

        var aAcMax = g(dd.AC.Max) ? F(dd.AC.Max.value) : DASH;
        var aAcAvg = g(dd.AC.Avg) ? F(dd.AC.Avg.value) : DASH;
        var aAcCount = g(dd.AC.Count) ? FI(dd.AC.Count.value) : DASH;
        var aAcOTVal = g(dd.AC.OperationTime) ? FI(dd.AC.OperationTime.value) : DASH;
        var aAvAvg = g(dd.AV.Avg) ? F(dd.AV.Avg.value, 2) : DASH;
        var bBcMax = g(dd.BC.Max) ? F(dd.BC.Max.value) : DASH;
        var bBcAvg = g(dd.BC.Avg) ? F(dd.BC.Avg.value) : DASH;
        var bBcCount = g(dd.BC.Count) ? FI(dd.BC.Count.value) : DASH;
        var bBcOTVal = g(dd.BC.OperationTime) ? FI(dd.BC.OperationTime.value) : DASH;
        var bBvAvg = g(dd.BV.Avg) ? F(dd.BV.Avg.value, 2) : DASH;

        var totalOT = 0;
        if (showA) totalOT += (parseInt(aAcOTVal, 10) || 0);
        if (showB) totalOT += (parseInt(bBcOTVal, 10) || 0);

        if (!window.pmEventHistory) window.pmEventHistory = {};
        if (!window.pmEventHistory[assetId]) window.pmEventHistory[assetId] = [];
        var hist = window.pmEventHistory[assetId];

        var hasRealData = (showA && (hasVal(aAcMax) || hasVal(aAcAvg))) ||
            (showB && (hasVal(bBcMax) || hasVal(bBcAvg)));
        var currentRow = {
            date: dateStr, name: name, dir: dirText, operationType: useDir, timestampDevice: ts,
            aAcMax: aAcMax, aAcAvg: aAcAvg, aAcCount: aAcCount, aAcOT: aAcOTVal, aAvAvg: aAvAvg,
            bBcMax: bBcMax, bBcAvg: bBcAvg, bBcCount: bBcCount, bBcOT: bBcOTVal, bBvAvg: bBvAvg,
            totalOT: totalOT, showA: showA, showB: showB, hasRealData: hasRealData
        };

        // One row per unique device second (v616 27438-27470, keeping v617's
        // "drop placeholder seeds once real data arrives" improvement).
        var tsBdMs = ts ? secMs(ts) : 0;
        var bdIdx = -1;
        if (tsBdMs > 0) {
            for (var bhi = hist.length - 1; bhi >= 0; bhi--) {
                if (hist[bhi].timestampDevice && secMs(hist[bhi].timestampDevice) === tsBdMs) { bdIdx = bhi; break; }
            }
        }
        if (bdIdx !== -1) {
            var hr = hist[bdIdx];
            hr.date = dateStr; hr.dir = dirText; hr.operationType = useDir;
            hr.aAcMax = aAcMax; hr.aAcAvg = aAcAvg; hr.aAcCount = aAcCount; hr.aAcOT = aAcOTVal; hr.aAvAvg = aAvAvg;
            hr.bBcMax = bBcMax; hr.bBcAvg = bBcAvg; hr.bBcCount = bBcCount; hr.bBcOT = bBcOTVal; hr.bBvAvg = bBvAvg;
            hr.totalOT = totalOT; hr.hasRealData = hasRealData || hr.hasRealData;
        } else if (hasRealData && tsBdMs > 0) {
            for (var si = hist.length - 1; si >= 0; si--) {
                if (!hist[si].hasRealData) hist.splice(si, 1);
            }
            hist.push(currentRow);
            var MAXH = (typeof window.PM_MAX_HISTORY === 'number') ? window.PM_MAX_HISTORY : 20;
            if (hist.length > MAXH) hist.shift();
        } else if (hist.length === 0 && hasRealData) {
            hist.push(currentRow);
        }

        // Render the whole tbody from history, newest first (v616 27470-27543).
        var sorted = hist.slice().sort(function (x, y) { return secMs(y.timestampDevice) - secMs(x.timestampDevice); });
        var rows = '';
        for (var i = 0; i < sorted.length; i++) {
            var r = sorted[i];
            var dp = (r.operationType === 'Reverse' || r.dir === 'Reverse Operation') ? 'R' : 'N';
            rows += '<tr data-ts="' + esc(r.timestampDevice || '') + '" data-optype="' + esc(r.operationType || '') + '"' +
                (i === 0 ? ' class="pm-val-flash-anim"' : '') + '>';
            rows += '<td class="pm-date-cell" data-hist-id="' + esc(assetId) + '">' + esc(r.date) + '</td>';
            rows += '<td>' + esc(r.name) + '</td>';
            rows += '<td>' + esc(r.dir) + '</td>';
            if (showCombine) {
                rows += '<td><a href="javascript:void(0)" onclick="pmShowChart(\'' + esc(assetId) + '\',\'' + dp + '_AC\')">C</a> / ' +
                    '<a href="javascript:void(0)" onclick="pmShowChart(\'' + esc(assetId) + '\',\'' + dp + '_AV\')">V</a> T(' + esc(r.totalOT) + ')</td>';
            }
            var hasAc = rowHasCount(r.aAcCount), hasAv = rowHasValue(r.aAvAvg);
            var hasBc = rowHasCount(r.bBcCount), hasBv = rowHasValue(r.bBvAvg);
            if (showA && r.showA !== false) {
                if (hasAc) {
                    rows += '<td class="col-a"><a href="javascript:void(0)" class="pm-current-link" onclick="pmShowSingleArrayGraph(\'' + esc(assetId) + '\',\'' + dp + '\',\'AC\')" title="' + TITLES.AC + '"><span class="pm-current-val">' + esc(currentText(r.aAcMax, r.aAcAvg, r.aAcCount)) + '</span></a></td>';
                } else {
                    rows += '<td class="col-a"><span class="pm-current-val pm-no-data" style="cursor:default;opacity:0.6;">' + esc(currentText(r.aAcMax, r.aAcAvg, r.aAcCount)) + '</span></td>';
                }
                if (hasAv) {
                    rows += '<td class="col-a"><a href="javascript:void(0)" class="pm-voltage-link" onclick="pmShowSingleArrayGraph(\'' + esc(assetId) + '\',\'' + dp + '\',\'AV\')" title="' + TITLES.AV + '"><span class="pm-voltage-val">' + esc(dv(r.aAvAvg)) + '</span></a></td>';
                } else {
                    rows += '<td class="col-a"><span class="pm-voltage-val pm-no-data" style="cursor:default;opacity:0.6;">' + esc(dv(r.aAvAvg)) + '</span></td>';
                }
                rows += '<td class="col-a">' + esc(dv(r.aAcOT)) + '</td>';
            }
            if (showB && r.showB !== false) {
                if (hasBc) {
                    rows += '<td class="col-b"><a href="javascript:void(0)" class="pm-current-link" onclick="pmShowSingleArrayGraph(\'' + esc(assetId) + '\',\'' + dp + '\',\'BC\')" title="' + TITLES.BC + '"><span class="pm-current-val-b">' + esc(currentText(r.bBcMax, r.bBcAvg, r.bBcCount)) + '</span></a></td>';
                } else {
                    rows += '<td class="col-b"><span class="pm-current-val-b pm-no-data" style="cursor:default;opacity:0.6;">' + esc(currentText(r.bBcMax, r.bBcAvg, r.bBcCount)) + '</span></td>';
                }
                if (hasBv) {
                    rows += '<td class="col-b"><a href="javascript:void(0)" class="pm-voltage-link" onclick="pmShowSingleArrayGraph(\'' + esc(assetId) + '\',\'' + dp + '\',\'BV\')" title="' + TITLES.BV + '"><span class="pm-voltage-val">' + esc(dv(r.bBvAvg)) + '</span></a></td>';
                } else {
                    rows += '<td class="col-b"><span class="pm-voltage-val pm-no-data" style="cursor:default;opacity:0.6;">' + esc(dv(r.bBvAvg)) + '</span></td>';
                }
                rows += '<td class="col-b">' + esc(dv(r.bBcOT)) + '</td>';
            }
            rows += '</tr>';
        }
        if ($tbody.length && rows) $tbody.html(rows);
        syncWaveFlags(assetId, sorted[0] || null);
        if (typeof window._pmSyncPlusIcons === 'function') { try { window._pmSyncPlusIcons(); } catch (e) { } }
    }
    window.buildPmDataRow = buildPmDataRow616;
    PM616.buildDataRow = buildPmDataRow616;

    // ---- the CARD itself: v616 updatePmCard — one direction everywhere ----
    function updatePmCard616(assetId) {
        var asset = live()[assetId];
        if (!asset) return;
        var $card = $('#pmCard_' + assetId);
        if (!$card.length) return;
        var pm = getPmStructuredData616(assetId);
        if (!pm) return;

        if (fn('renderPmBadges')) { try { window.renderPmBadges(assetId, pm.DataLogger); } catch (e) { } }

        var dir = resolveDirection(assetId, pm);
        var isRev = (dir === 'Reverse');
        var DIRTXT = 'POINT IN ' + dir.toUpperCase();
        ['A', 'B'].forEach(function (E) {
            $('#pmDirBadge' + E + '_' + assetId).text(DIRTXT)
                .removeClass('normal reverse').addClass(isRev ? 'reverse' : 'normal');
            $('#pmKrLabel' + E + '_' + assetId).text(isRev ? 'RWKR' : 'NWKR');
            $('#pmDirLocLabel' + E + '_' + assetId).text(isRev ? 'Reverse' : 'Normal');
        });

        // Indications BY ATTRIBUTE ID, same ids as the table's endFields
        // (A KR 25/26, A LOC 576/577, B KR 27/28, B LOC 578/579).
        // No cross-direction fallback: absent -> em-dash (v616 27190-27210).
        function indText(id) {
            var v = indicationValueById(pm, id);
            return (v === null) ? DASH : Number(zf(v)).toFixed(2);
        }
        $('#pmVnwkrA_' + assetId).text(indText(isRev ? 26 : 25));
        $('#pmVdcA_' + assetId).text(indText(isRev ? 577 : 576));
        $('#pmVnwkrB_' + assetId).text(indText(isRev ? 28 : 27));
        $('#pmVdcB_' + assetId).text(indText(isRev ? 579 : 578));

        // Header averages from the ONE resolved direction (v616 31491-31498).
        var d = pm[dir] || { AC: {}, AV: {}, BC: {}, BV: {} };
        var F = window.pmFmt, FI = window.pmFmtInt;
        $('#pmIptAvgA_' + assetId).text(d.AC.Avg ? F(d.AC.Avg.value) : DASH);
        $('#pmVptAvgA_' + assetId).text(d.AV.Avg ? F(d.AV.Avg.value, 1) : DASH);
        $('#pmTptAvgA_' + assetId).text(d.AC.OperationTime ? FI(d.AC.OperationTime.value) : DASH);
        $('#pmIptAvgB_' + assetId).text(d.BC.Avg ? F(d.BC.Avg.value) : DASH);
        $('#pmVptAvgB_' + assetId).text(d.BV.Avg ? F(d.BV.Avg.value, 1) : DASH);
        $('#pmTptAvgB_' + assetId).text(d.BC.OperationTime ? FI(d.BC.OperationTime.value) : DASH);

        buildPmDataRow616(assetId, pm);

        $card.removeClass('pm-card-flash-anim');
        if ($card[0]) {
            void $card[0].offsetWidth;
            $card.addClass('pm-card-flash-anim');
            setTimeout(function () { $card.removeClass('pm-card-flash-anim'); }, 1200);
        }
    }
    window.updatePmCard = updatePmCard616;
    PM616.updateCard = updatePmCard616;

    // Header-averages helper used by the per-attribute card path
    // (updatePmCardDataOnly) — same single direction (v616 31483-31510).
    window.updatePmHeaderAverages = function (assetId, pm) {
        if (!pm) pm = getPmStructuredData616(assetId);
        if (!pm) return;
        var dir = resolveDirection(assetId, pm);
        var isRev = (dir === 'Reverse');
        var d = pm[dir] || { AC: {}, AV: {}, BC: {}, BV: {} };
        var F = window.pmFmt, FI = window.pmFmtInt;
        $('#pmIptAvgA_' + assetId).text(d.AC.Avg ? F(d.AC.Avg.value) : DASH);
        $('#pmVptAvgA_' + assetId).text(d.AV.Avg ? F(d.AV.Avg.value, 1) : DASH);
        $('#pmTptAvgA_' + assetId).text(d.AC.OperationTime ? FI(d.AC.OperationTime.value) : DASH);
        $('#pmIptAvgB_' + assetId).text(d.BC.Avg ? F(d.BC.Avg.value) : DASH);
        $('#pmVptAvgB_' + assetId).text(d.BV.Avg ? F(d.BV.Avg.value, 1) : DASH);
        $('#pmTptAvgB_' + assetId).text(d.BC.OperationTime ? FI(d.BC.OperationTime.value) : DASH);
        var t = 'POINT IN ' + dir.toUpperCase();
        $('#pmDirBadgeA_' + assetId).text(t).removeClass('normal reverse').addClass(isRev ? 'reverse' : 'normal');
        $('#pmDirBadgeB_' + assetId).text(t).removeClass('normal reverse').addClass(isRev ? 'reverse' : 'normal');
    };

    // Cards view never flushed WS operation events into pmEventHistory:
    // processItemsInternal only does that when drpView === 'PointMachine'
    // (the pre-Aurora view name). Flush them for the Aurora 'Cards' view too.
    (function () {
        var orig = window.processItemsInternal;
        if (typeof orig !== 'function') return;
        window.processItemsInternal = function (items) {
            var res = orig.apply(this, arguments);
            try {
                var view = $('#drpView').val();
                var isPm = (String(window.wsCurrentAssetTypeId) === '3');
                if (view === 'Cards' && isPm && items && items.length &&
                    fn('isPmOperationId') && fn('getPmOperationType') && fn('processPmOperationMessage')) {
                    var pend = {};
                    for (var i = 0; i < items.length; i++) {
                        var d = items[i];
                        if (!d || d.DataType !== 'PointMachine' || !window.isPmOperationId(d.AssetAttributeId)) continue;
                        var t = window.getPmOperationType(d.AssetAttributeId);
                        var ots = d.TimestampDevice || '';
                        if (!t || !ots || ots.indexOf('0001-01-01') !== -1) continue;
                        var key = d.AssetId + '_' + t;
                        if (!pend[key] || secMs(ots) > secMs(pend[key].ts)) pend[key] = { aid: d.AssetId, type: t, ts: ots };
                    }
                    for (var k in pend) {
                        if (Object.prototype.hasOwnProperty.call(pend, k)) {
                            window.processPmOperationMessage(pend[k].aid, pend[k].type, pend[k].ts);
                        }
                    }
                }
            } catch (e) { try { console.warn('[PM616] Cards op flush', e); } catch (e2) { } }
            return res;
        };
    })();

    /* ---------------------------------------------------------------------- */
    function boot() {
        injectStyles();
        // If a v617 PM table is already on screen, switch it to the 616 layout.
        var t = document.getElementById('wsLiveTable');
        if (t && !t.classList.contains(TABLE_CLASS) && ($('#drpView').val() === 'Table') && isPmType()) {
            try { renderPointMachineTableView616(); } catch (e) { }
        }
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();

    try { console.log('[PM616] Point Machine v616 feature port installed'); } catch (e) { }
})(window, document);
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-pm616" failed to load', e);
}


/* #############################################################################
 * SECTION: SIGNAL / RDPMS / CIRCUIT (v616 port)
 * (was telemetrylive-signal616.js)
 * ########################################################################## */
try {
/* =============================================================================
 * telemetrylive-signal616.js — Signal (RDPMS), signal table and circuit
 * features from v616, ported to v617 (Aurora)
 *
 * Load order: AFTER telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-pmvibration.js (classic <script>). Nothing in those files is
 * edited. Top-level functions there are window properties, and their callers
 * look them up by name when they run, so replacing window.X takes effect
 * everywhere (including callers inside telemetrylive.js).
 *
 * v616 line numbers below refer to v616 TelemetryLive/telemetrylive.js
 * unless marked "_Circuit".
 *
 * SIGNAL TABLE (List view)
 *  B1  Incremental update actually runs: the wrapper now carries the
 *      .sig-group-wrap class the v617 dispatchers look for, and
 *      updateSignalGroupedTables() routes to the 616 incremental
 *      updater ................................................... v616 5924-6016
 *  G1  Metadata-first column list and grouping fingerprint
 *      (_compareSignalColumns, _signalColumnKeys, _fetchAssetInfoList,
 *      _getSignalAttrFingerprint) ................................ v616 5465-5599, 4390-4394
 *      Group order/labels (_signalFpAttrs, _signalFpHasPrefix,
 *      _getSignalGroupMeta, _signalGroupLabel, _signalGroupSeqRank):
 *      plain 2/3/4 aspect groups first, then route/calling, shunt
 *      last; "+ N Routes" / "Calling ON" chips ................... v616 5653-5851
 *      Row: "Avg values" button, values <= 0 shown as 0 ........... v616 5853-5921
 *  B6  _assetInfoListCache survives a reconnect to the same site +
 *      asset type (connectWebSocket wrapper) .................... v616 4716-4720, 4764
 *
 * RDPMS SIGNAL CARDS
 *  B2  updateSignalDataTable(): alias-aware cell lookup
 *      (getSignalRdpmsCellTarget), V cells get the V label, values
 *      <= 0 shown as 0 ............................................ v616 31290-31375
 *  G3  Card labels from the asset's AliasName (getRdpmsDisplayLabel) v616 5435-5451, 8595-8760, 8944-8987
 *  G2  Dynamic HPR rows: every attribute whose AliasName contains
 *      "HPR", shown only when its paired voltage is above 5 V
 *      (_collectHprAttrs, _isVoltName, _hprBaseKey, _renderHprRows,
 *      _renderShuntHprRows); Calling V shown above 5 V ........... v616 8095-8226, 8702-8706
 *  G4  Grey "!" on stale lamp values (_rdpmsValSpan); "Waiting for
 *      live data" / Link Issue blanking with relay badges cleared
 *      (_clearDataloggerBadges); card markup repaired before paint;
 *      card builders recognise alias lamp names and always build
 *      the four value rows (getMainSignalCardHtml,
 *      getShuntSignalCardHtml) .................................... v616 7725-7844, 8231-8307,
 *                                                                   8445-8810, 8897-9001, 24641-24646
 *      repaintSignalRdpmsCardNow, updateShuntSignalLightsOnly ...... v616 8815-8828
 *  G7  Frame-by-frame paint queue for the RDPMS view
 *      (_rdpmsRAF, _enqueueRdpmsPaint, _drainRdpmsPaint) .......... v616 31780-31831, 32239
 *  G5  Placeholder cards: already done by v617
 *      (applyBulkMetadataMissingAttributeFix, telemetrylive.js 21146-21300);
 *      this file only adds the "Waiting for live data" text (G4) . v616 8309-8362
 *  G8  Console diagnostics (_rdpmsDiagRecord, dumpRdpmsDiag,
 *      copyRdpmsDiag, clearRdpmsDiag, debugSignalCard). Recording is
 *      OFF unless window.__RDPMS_DIAG === true ................... v616 1299-1363, 8832-8859
 *  G6  NOT changed: v617 keeps its own card order.
 *
 * CIRCUIT
 *  B4  Closing the circuit popup reconnects the main live stream and
 *      keeps the page's data/cards (connectCircuitWebSocket no longer
 *      wipes wsLiveData — v616 14781-14787; closeCircuitModal wrapper)
 *  B5  window.circuitDerived / window.circuitLeakage are set ....... v616 15025-15035
 *  C3  Leakage / IBALST labels and HTML cells
 *      (circuitLeakageNorm, circuitLeakageFormat,
 *      isCircuitLeakageLabel, updateCircuitLeakageIbalst*,
 *      updateHtmlString); track values <= 0 shown as 0 .......... v616 15100-15310
 *  C4  Derived labels and Point Machine NWKR/RWKR(+Loc) values inside
 *      embedded HTML (safeDerivedFmt, getCircuitRoot,
 *      getDerivedKeyFromText, getDerivedRuleByKey,
 *      updateTrackDerivedLabels, updateTrackDerivedForeignObject,
 *      applySafeCircuitDomUpdates) ............................... v616 34441-34991
 *  C2  Hidden asset-type ids synced for the circuit scripts; an inline
 *      circuit script that fails stops the load with an error
 *      (normalizeCircuitId, setCircuitHiddenValue,
 *      syncCircuitHiddenIds, completeCircuitLoad) ................. v616 14021-14227, 14451-14477
 *  C7  Circuit action hidden/refused for asset types without a
 *      diagram (Axle Counter, Gate, ELD, IPS) — matched by asset
 *      type NAME first (isCircuitUnsupportedAssetType) ............ v616 19096-19135, 9203-9218, 30531-30550
 *
 * Not ported here (need server/DB confirmation): C1 (Main/Calling/Route
 * tabs, shunt diagram 30/31), T1 (derived-value attribute-id fallbacks).
 * ========================================================================== */

/* ---- Global names used from inline onclick / console --------------------- */
function dumpRdpmsDiag() { return window.SIG616 && window.SIG616.dumpDiag ? window.SIG616.dumpDiag() : -1; }
function copyRdpmsDiag() { return window.SIG616 && window.SIG616.copyDiag ? window.SIG616.copyDiag() : -1; }
function clearRdpmsDiag() { if (window.SIG616 && window.SIG616.clearDiag) window.SIG616.clearDiag(); }
function debugSignalCard(assetId) { return window.SIG616 && window.SIG616.debugCard ? window.SIG616.debugCard(assetId) : null; }

(function () {
    'use strict';

    if (typeof window === 'undefined' || typeof window.jQuery === 'undefined') return;
    if (window.__sig616Installed) return;
    window.__sig616Installed = true;

    var $ = window.jQuery;
    var W = window;
    var SIG616 = W.SIG616 = W.SIG616 || {};

    function fn(name) { return typeof W[name] === 'function'; }
    function call(name) {
        if (!fn(name)) return undefined;
        return W[name].apply(W, Array.prototype.slice.call(arguments, 1));
    }
    function liveData() { return W.wsLiveData || {}; }
    function zf(v) {
        if (typeof W.tlZeroFloor === 'function') return W.tlZeroFloor(v);
        var n = parseFloat(v);
        return isNaN(n) ? v : (n <= 0 ? 0 : n);
    }
    function toNum(v) {
        if (v && typeof v === 'object' && v.Value !== undefined) v = v.Value;
        if (v === null || v === undefined || String(v).trim() === '') return null;
        var n = parseFloat(v);
        return isNaN(n) ? null : n;
    }
    function esc(s) {
        return String(s === null || s === undefined ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function normKey(s) { return String(s || '').toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function isShuntName(aid) {
        var a = liveData()[aid];
        return String((a && a.AssetName) || '').toLowerCase().indexOf('sh') > -1;
    }
    function attrValue(attrs, name) {
        var a = attrs ? attrs[name] : null;
        return (a && a.Value !== null && a.Value !== undefined) ? (parseFloat(a.Value) || 0) : 0;
    }
    function attrByAliases(attrs, aliases) {
        if (!attrs || !aliases) return null;
        if (fn('getSignalAttrByAliases')) return W.getSignalAttrByAliases(attrs, aliases);
        for (var i = 0; i < aliases.length; i++) if (attrs[aliases[i]]) return attrs[aliases[i]];
        return null;
    }
    function valByAliases(attrs, aliases) {
        var a = attrByAliases(attrs, aliases);
        return (a && a.Value !== null && a.Value !== undefined) ? (parseFloat(a.Value) || 0) : 0;
    }
    function maAliases(lamp) { return (W.SIGNAL_MA_ALIASES && W.SIGNAL_MA_ALIASES[lamp]) || [lamp + ' mA']; }
    function vAliases(lamp) { return (W.SIGNAL_V_ALIASES && W.SIGNAL_V_ALIASES[lamp]) || [lamp + ' V']; }
    // 616 value text: <= 0 -> "0.0", else 2 decimals (v616 _fv / table cells).
    function fv(v) {
        var n = parseFloat(v);
        if (isNaN(n)) return '0.0';
        n = zf(n);
        return (n === 0) ? '0.0' : n.toFixed(2);
    }
    function cellText(raw) {
        if (raw === null || raw === undefined || raw === '') return '—';
        var n = parseFloat(raw);
        if (isNaN(n)) return String(raw);
        n = zf(n);
        return (n === 0) ? '0.0' : n.toFixed(2);
    }

    /* ======================================================================
     * CSS (Aurora tokens)
     * ==================================================================== */
    (function injectStyles() {
        if (document.getElementById('sig616-styles')) return;
        var css =
            // server-stale value marker (v616 _injectWsFreshStyles, Aurora colours)
            '.ws-value-server-stale{color:var(--at-t3,rgba(255,255,255,.5))!important;}' +
            '.ws-value-server-stale::before{content:"!";display:inline-flex;align-items:center;justify-content:center;' +
            'width:14px;height:14px;margin-right:4px;border-radius:50%;background:var(--at-t4,rgba(255,255,255,.34));' +
            'color:var(--at-bg0,#060914);font-size:10px;font-weight:800;line-height:14px;vertical-align:middle;font-family:var(--at-font-body,inherit);}' +
            // dynamic HPR rows
            '.rdpms-route-table tr.rdpms-hpr-row td{color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.rdpms-route-table tr.rdpms-hpr-row td span{color:var(--at-brand,#22d3ee);font-family:var(--at-font-mono,monospace);}' +
            '.rdpms-signal-card .rdpms-waiting{color:var(--at-t3,rgba(255,255,255,.5));font-style:italic;}' +
            // signal group chips
            '.sig616-chip{display:inline-flex;align-items:center;font-size:10px;font-weight:700;padding:3px 9px;border-radius:12px;' +
            'background:rgba(255,255,255,.18);color:#fff;border:1px solid rgba(255,255,255,.22);white-space:nowrap;}' +
            '.sig616-chip-route{background:rgba(34,211,238,.22);}' +
            '.sig616-chip-calling{background:rgba(251,191,36,.25);}' +
            '.signal-group-card .sig616-avg{margin-left:6px;vertical-align:middle;}' +
            // circuit action hidden for asset types without a diagram
            'body.sig616-no-circuit .tl-aa-circuit,body.sig616-no-circuit .sig-action-circuit,' +
            'body.sig616-no-circuit [onclick^="fnGetAssetCircuit("],' +
            'body.sig616-no-circuit [onclick^="tlOpenAssetView(\'Circuit\'"]{display:none!important;}' +
            '#rdpmsCircuitError .sig616-circuit-detail{display:block;font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));margin-top:6px;}';
        var st = document.createElement('style');
        st.id = 'sig616-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    })();

    /* ======================================================================
     * G8  Diagnostics (v616 1299-1363, 8832-8859)
     * ==================================================================== */
    var DIAG_CAP = 20000;
    var diagCounts = {};
    if (!Array.isArray(W.__rdpmsDiagLog)) W.__rdpmsDiagLog = [];
    function diagRecord(tag, obj) {
        try {
            if (W.__rdpmsDiagLog.length >= DIAG_CAP) return;
            var line = new Date().toISOString() + '  [RDPMS-DIAG] ' + tag;
            if (typeof obj !== 'undefined') {
                var s;
                try { s = JSON.stringify(obj); } catch (e) { s = String(obj); }
                line += '  ' + s;
            }
            W.__rdpmsDiagLog.push(line);
        } catch (e2) { }
    }
    W._rdpmsDiagRecord = diagRecord;
    // telemetrylive.js calls the global _rdpmsDiag from computeSignalState etc.
    // It stays silent (as in v617) unless window.__RDPMS_DIAG === true.
    W._rdpmsDiag = function (tag, obj, maxN) {
        if (W.__RDPMS_DIAG !== true) return;
        try {
            diagRecord(tag, obj);
            maxN = maxN || 60;
            diagCounts[tag] = (diagCounts[tag] || 0) + 1;
            var n = diagCounts[tag];
            if (n > maxN) {
                if (n === maxN + 1) console.log('[RDPMS-DIAG] ' + tag + ' : further console lines suppressed (>' + maxN + '); dumpRdpmsDiag() has them all');
                return;
            }
            if (typeof obj === 'undefined') console.log('[RDPMS-DIAG] ' + tag);
            else console.log('[RDPMS-DIAG] ' + tag, obj);
        } catch (e) { }
    };
    SIG616.dumpDiag = function () {
        try {
            var text = '[RDPMS-DIAG] capture ' + new Date().toISOString() + ' lines=' + W.__rdpmsDiagLog.length +
                '\r\n' + W.__rdpmsDiagLog.join('\r\n') + '\r\n';
            var blob = new Blob([text], { type: 'text/plain' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url; a.download = 'rdpms-diag-' + Date.now() + '.txt';
            document.body.appendChild(a); a.click();
            setTimeout(function () { try { document.body.removeChild(a); URL.revokeObjectURL(url); } catch (e) { } }, 0);
            return W.__rdpmsDiagLog.length;
        } catch (e) { console.log('[RDPMS-DIAG] dump failed', e); return -1; }
    };
    SIG616.copyDiag = function () {
        var text = W.__rdpmsDiagLog.join('\r\n');
        try {
            if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).catch(function () { console.log(text); });
            else console.log(text);
        } catch (e) { console.log(text); }
        return W.__rdpmsDiagLog.length;
    };
    SIG616.clearDiag = function () { W.__rdpmsDiagLog = []; diagCounts = {}; };
    SIG616.debugCard = function (assetId) {
        var aid = String(assetId);
        var asset = liveData()[aid];
        if (!asset) { console.warn('[SignalCardDebug] no wsLiveData', aid); return null; }
        var attrs = asset.attrs || {};
        var ss = fn('computeSignalState') ? W.computeSignalState(aid) : null;
        console.warn('[SignalCardDebug]', {
            assetId: aid, assetName: asset.AssetName, state: ss, rawKeys: Object.keys(attrs),
            resolved: {
                RG: attrByAliases(attrs, maAliases('RG')), DG: attrByAliases(attrs, maAliases('DG')),
                HG: attrByAliases(attrs, maAliases('HG')), HHG: attrByAliases(attrs, maAliases('HHG'))
            },
            hpr: collectHprAttrs(aid, attrs),
            dlRelays: asset.dlRelays,
            dom: {
                card: $('#rdpmsCard_' + aid).length, rg: $('#rdpmsRG_' + aid).length, dg: $('#rdpmsDG_' + aid).length,
                hg: $('#rdpmsHG_' + aid).length, hhg: $('#rdpmsHHG_' + aid).length
            }
        });
        return ss;
    };

    /* ======================================================================
     * G1  Metadata attribute list per asset (v616 4390-4394, 5465-5599)
     * v617 never fills _assetInfoListCache from GetBulkAssetMetadata, and its
     * per-load _sigAssetInfoCache is wiped on every connect, so the list is
     * rebuilt from userAssetSimpleMap (filled by the bulk load, keyed
     * "<assetId>_<attrId>", attributeName = the WS attribute key).
     * ==================================================================== */
    var metaIdx = { ref: null, at: 0, byAid: {} };
    function metaIndex() {
        var m = W.userAssetSimpleMap;
        if (!m) return {};
        var now = Date.now();
        if (metaIdx.ref !== m || (now - metaIdx.at) > 3000) {
            var by = {};
            for (var k in m) {
                if (!m.hasOwnProperty(k)) continue;
                var cut = k.indexOf('_');
                if (cut < 1) continue;
                var e = m[k];
                if (!e) continue;
                var aid = k.slice(0, cut);
                (by[aid] || (by[aid] = [])).push({
                    attrId: k.slice(cut + 1),
                    title: String(e.attributeName || e.AttributeName || e.name || '').trim(),
                    alias: String(e.name || e.aliasName || '').trim()
                });
            }
            metaIdx = { ref: m, at: now, byAid: by };
        }
        return metaIdx.byAid;
    }
    SIG616.metaIndex = metaIndex;

    function compareSignalColumns(a, b) {
        var oa = fn('_getSignalColOrder') ? W._getSignalColOrder(a) : 999;
        var ob = fn('_getSignalColOrder') ? W._getSignalColOrder(b) : 999;
        if (oa !== ob) return oa - ob;
        return String(a || '').localeCompare(String(b || ''), undefined, { numeric: true, sensitivity: 'base' });
    }
    W._compareSignalColumns = compareSignalColumns;

    function isDlAttr(aid, k) { return fn('_isDataloggerAttr') ? !!W._isDataloggerAttr(aid, k) : false; }

    function metaSignalAttrs(aid) {
        var list = metaIndex()[String(aid)] || [];
        var seen = {}, out = [];
        for (var i = 0; i < list.length; i++) {
            var t = list[i].title;
            if (!t || seen[t]) continue;
            if (isDlAttr(aid, t)) continue;
            seen[t] = 1;
            out.push(t);
        }
        out.sort(compareSignalColumns);
        return out;
    }
    SIG616.metaSignalAttrs = metaSignalAttrs;

    function infoCache() {
        if (!W._assetInfoListCache || typeof W._assetInfoListCache !== 'object') W._assetInfoListCache = {};
        return W._assetInfoListCache;
    }
    function cachedInfoList(aid) {
        var cache = infoCache();
        var c = cache[aid];
        if (c && c.length) return c;
        var meta = metaSignalAttrs(aid);
        if (meta.length) { cache[aid] = meta; return meta; }
        return null;
    }

    W._fetchAssetInfoList = function (assetId, callback) {
        var list = cachedInfoList(assetId);
        if (!list) {
            var asset = liveData()[assetId];
            list = [];
            if (asset && asset.attrs) {
                for (var k in asset.attrs) {
                    if (!asset.attrs.hasOwnProperty(k)) continue;
                    if (fn('_isSignalMetadataAttr') ? W._isSignalMetadataAttr(assetId, k) : true) list.push(k);
                }
            }
            list.sort(compareSignalColumns);
            infoCache()[assetId] = list;
        }
        if (callback) callback(list);
    };

    W._signalColumnKeys = function (assetId) {
        var asset = liveData()[assetId];
        var seen = {}, keys = [];
        function add(k) {
            if (!k || seen[k] || isDlAttr(assetId, k)) return;
            seen[k] = 1; keys.push(k);
        }
        var cached = cachedInfoList(assetId) || [];
        for (var i = 0; i < cached.length; i++) add(cached[i]);
        if (asset && asset.attrs) {
            for (var k in asset.attrs) if (asset.attrs.hasOwnProperty(k)) add(k);
        }
        keys.sort(compareSignalColumns);
        return keys;
    };

    W._getSignalAttrFingerprint = function (assetId) {
        var seen = {}, keys = [];
        var cached = cachedInfoList(assetId);
        function add(k, trusted) {
            if (!k || seen[k] || isDlAttr(assetId, k)) return;
            if (!trusted && fn('_isSignalMetadataAttr') && !W._isSignalMetadataAttr(assetId, k)) return;
            seen[k] = 1; keys.push(k);
        }
        if (cached && cached.length) {
            for (var i = 0; i < cached.length; i++) add(cached[i], true);
        } else {
            var asset = liveData()[assetId];
            if (asset && asset.attrs) {
                for (var k in asset.attrs) if (asset.attrs.hasOwnProperty(k)) add(k, false);
            }
        }
        keys.sort(compareSignalColumns);
        return keys.join('|');
    };

    /* ======================================================================
     * G1  Signal group meta / labels / order (v616 5653-5726)
     * ==================================================================== */
    var GROUP_LAMPS = ['RG', 'DG', 'HG', 'HHG'];
    var ROUTE_PREFIXES = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];

    function signalFpAttrs(fpKey) {
        if (!fpKey) return [];
        return String(fpKey).split('|').filter(function (p) { return !!p; });
    }
    function signalFpHasPrefix(fpAttrs, prefix) {
        prefix = String(prefix || '').toUpperCase();
        for (var i = 0; i < fpAttrs.length; i++) {
            if (String(fpAttrs[i] || '').toUpperCase().indexOf(prefix) === 0) return true;
        }
        return false;
    }
    function groupRepName(groupArr) {
        if (fn('_groupRepName')) return W._groupRepName(groupArr) || '';
        var best = null;
        for (var i = 0; i < groupArr.length; i++) {
            var a = liveData()[groupArr[i].id];
            var nm = (a && a.AssetName) || '';
            if (best === null || nm.localeCompare(best, undefined, { numeric: true, sensitivity: 'base' }) < 0) best = nm;
        }
        return best || '';
    }
    function getSignalGroupMeta(fpKey, groupArr) {
        var fpAttrs = signalFpAttrs(fpKey);
        var repName = groupRepName(groupArr || []);
        var isShunt = repName.toLowerCase().indexOf('sh') > -1;
        var aspectCount = 0, routeCount = 0, i;
        for (i = 0; i < GROUP_LAMPS.length; i++) if (signalFpHasPrefix(fpAttrs, GROUP_LAMPS[i] + ' ')) aspectCount++;
        for (i = 0; i < ROUTE_PREFIXES.length; i++) if (signalFpHasPrefix(fpAttrs, ROUTE_PREFIXES[i])) routeCount++;
        var hasCalling = signalFpHasPrefix(fpAttrs, 'Co_Hg');
        return {
            aspectCount: aspectCount, routeCount: routeCount, hasRoute: routeCount > 0,
            callingCount: hasCalling ? 1 : 0, hasCalling: hasCalling,
            isSpecial: (routeCount > 0 || hasCalling),
            signalCount: groupArr ? groupArr.length : 0,
            columnCount: fpAttrs.length, isShunt: isShunt, repName: repName
        };
    }
    function signalGroupLabel(meta) {
        if (meta.isShunt) return 'Shunt Signal';
        var label = meta.aspectCount > 0 ? (meta.aspectCount + ' Aspect') : 'Signal';
        if (meta.routeCount > 0) label += ' + ' + meta.routeCount + ' Route' + (meta.routeCount > 1 ? 's' : '');
        if (meta.callingCount > 0) label += ' + Calling ON';
        return label;
    }
    W._signalFpAttrs = signalFpAttrs;
    W._signalFpHasPrefix = signalFpHasPrefix;
    W._getSignalGroupMeta = getSignalGroupMeta;
    W._signalGroupLabel = signalGroupLabel;
    W._signalGroupSeqRank = function (fpKey, groupArr) {
        var meta = getSignalGroupMeta(fpKey, groupArr);
        if (meta.isShunt) return 99;
        return meta.isSpecial ? 10 : 0;
    };

    /* ======================================================================
     * G1 / B1  Signal table builder + incremental updater
     * (v616 5728-6016, keeping v617's coloured group bar)
     * ==================================================================== */
    function dlCellHtml(asset) {
        var dlRelays = (asset && asset.dlRelays) || {};
        var keys = Object.keys(dlRelays);
        if (!keys.length) return '<span style="color:var(--at-t4,#94a3b8);font-size:10px;">—</span>';
        keys.sort(function (a, b) {
            var ra = dlRelays[a], rb = dlRelays[b];
            if (ra.isPickup && !rb.isPickup) return -1;
            if (!ra.isPickup && rb.isPickup) return 1;
            return String(ra.displayName || a).localeCompare(String(rb.displayName || b));
        });
        var h = '';
        for (var i = 0; i < keys.length; i++) {
            var r = dlRelays[keys[i]];
            h += '<span class="rdpms-dl-badge ' + (r.isPickup ? 'pickup' : 'drop') + '" data-attr="' + esc(keys[i]) +
                '" style="margin:1px;padding:2px 5px;font-size:9px;">' + esc(r.displayName || keys[i]) + ': ' +
                (r.isPickup ? 'Pickup' : 'Drop') + '</span> ';
        }
        return h;
    }
    function lastUpdateText(asset) {
        var opTs = fn('getOperationTimestampDevice') ? W.getOperationTimestampDevice(asset) : null;
        if (opTs && fn('fmtTimestampDevice')) return W.fmtTimestampDevice(opTs);
        if (asset && asset.lastUpdated && fn('fmtTime')) return W.fmtTime(asset.lastUpdated);
        return '—';
    }
    function storedAttr(aid, an) {
        var ad = fn('getStoredAttr') ? W.getStoredAttr(aid, an) : null;
        if (!ad) { var a = liveData()[aid]; ad = a && a.attrs ? a.attrs[an] : null; }
        return ad;
    }
    function isStaleLocal(aid, an) { return fn('isAttrStale') ? !!W.isAttrStale(aid, an) : false; }
    function serverStaleAttr(ad) {
        return fn('_wsStaleCellAttr') ? W._wsStaleCellAttr(ad) : { cls: '', title: '' };
    }

    function buildSignalGroupRow(assetId, columns) {
        var asset = liveData()[assetId];
        if (!asset) return '';
        var name = asset.AssetName || 'Signal';
        var h = '<tr data-id="' + esc(assetId) + '">';
        h += '<td class="asset-name" style="font-weight:600;white-space:nowrap;font-size:12px;padding:6px 12px;">' + esc(name) +
            '<button type="button" class="tl-asset-action tl-aa-ai" style="margin-left:6px" title="Asset dashboard: values, AI, circuit, alerts, graph" ' +
            'onclick="tlOpenAssetDrawer(\'' + esc(assetId) + '\')" aria-label="Open asset dashboard"><i class="fa-solid fa-wand-magic-sparkles"></i></button>' +
            '<button type="button" class="tl-asset-action tl-aa-avg sig616-avg" title="Avg values" ' +
            'onclick="fnShowFRSAttributeRangeHistory(\'' + esc(assetId) + '\')"><i class="fa-solid fa-chart-column"></i></button></td>';
        for (var ci = 0; ci < columns.length; ci++) {
            var an = columns[ci];
            var ad = storedAttr(assetId, an);
            var raw = ad ? ad.Value : null;
            var disp = cellText(raw);
            var cls = (disp === '—') ? 'val-na' : '';
            if (isStaleLocal(assetId, an)) cls += ' ws-stale-val';
            var sf = serverStaleAttr(ad);
            h += '<td class="' + cls + sf.cls + '" data-attr="' + esc(an) + '" style="font-size:12px;padding:6px 10px;"' + sf.title + '>' + esc(disp) + '</td>';
        }
        h += '<td class="dl-cell" style="padding:6px 10px;">' + dlCellHtml(asset) + '</td>';
        h += '<td class="time-cell" data-attr="LastUpdate" style="font-size:11px;padding:6px 10px;color:var(--at-t3,#64748b);">' + esc(lastUpdateText(asset)) + '</td>';
        h += '</tr>';
        return h;
    }
    W._buildSignalGroupRow = function (assetId, columns) { return buildSignalGroupRow(assetId, columns); };

    function colorStyle(label) {
        if (fn('getSignalGroupColorStyle')) return W.getSignalGroupColorStyle(label);
        return { bar: 'linear-gradient(135deg,#475569,#64748b)', icon: 'fa-signal' };
    }

    W._buildSignalTablesHtml = function (assetIds, forExport) {
        var LD = liveData();
        var groupMap = {};
        var groupCache = W._signalGroupCache || (W._signalGroupCache = {});
        for (var i = 0; i < assetIds.length; i++) {
            var aid = assetIds[i];
            if (!LD[aid]) continue;
            var fp = W._getSignalAttrFingerprint(aid);
            (groupMap[fp] || (groupMap[fp] = [])).push({ id: aid, fp: fp });
            if (!forExport) groupCache[aid] = fp;
        }
        var groupKeys = Object.keys(groupMap);
        var metaMap = {};
        groupKeys.forEach(function (k) { metaMap[k] = getSignalGroupMeta(k, groupMap[k]); });
        groupKeys.sort(function (a, b) {
            var ma = metaMap[a], mb = metaMap[b];
            if (ma.isShunt !== mb.isShunt) return ma.isShunt ? 1 : -1;
            if (ma.isSpecial !== mb.isSpecial) return ma.isSpecial ? 1 : -1;
            if (ma.aspectCount !== mb.aspectCount) return ma.aspectCount - mb.aspectCount;
            if (ma.routeCount !== mb.routeCount) return ma.routeCount - mb.routeCount;
            if (ma.callingCount !== mb.callingCount) return ma.callingCount - mb.callingCount;
            if (ma.columnCount !== mb.columnCount) return ma.columnCount - mb.columnCount;
            return ma.repName.localeCompare(mb.repName, undefined, { numeric: true, sensitivity: 'base' });
        });
        Object.keys(groupMap).forEach(function (gk) {
            groupMap[gk].sort(function (a, b) {
                return String(LD[a.id].AssetName || '').localeCompare(String(LD[b.id].AssetName || ''), undefined, { numeric: true, sensitivity: 'base' });
            });
        });

        var html = forExport
            ? '<div style="padding:5px 0;">'
            : '<div id="signalAspectTablesWrapper" class="sig-group-wrap sig616-wrap" style="padding:5px 0;">';
        var tableIndex = 0;
        for (var gi = 0; gi < groupKeys.length; gi++) {
            var fpKey = groupKeys[gi];
            var signals = groupMap[fpKey];
            if (!signals || !signals.length) continue;
            var columns = signalFpAttrs(fpKey);
            var meta = metaMap[fpKey];
            var label = signalGroupLabel(meta);
            var gs = colorStyle(label);
            if (tableIndex > 0) html += '<div style="height:18px;"></div>';
            html += '<div class="signal-group-card" data-group-label="' + esc(label) + '" data-aspect-count="' + meta.aspectCount +
                '" data-route-count="' + meta.routeCount + '" style="border:1px solid var(--at-edge,#e2e8f0);border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.06);">';
            html += '<div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:10px 16px;background:' + gs.bar +
                ';border-bottom:1px solid rgba(255,255,255,0.12);box-shadow:0 2px 8px rgba(0,0,0,0.12);">';
            html += '<span style="display:inline-flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:8px;background:rgba(255,255,255,0.20);color:#fff;">' +
                '<i class="fas ' + gs.icon + '" style="font-size:13px;"></i></span>';
            html += '<span class="sig616-group-label" style="font-size:13px;font-weight:700;color:#fff;letter-spacing:0.3px;">' + esc(label) + '</span>';
            if (!meta.isShunt) {
                if (meta.routeCount > 0) html += '<span class="sig616-chip sig616-chip-route">' + meta.routeCount + ' Route' + (meta.routeCount === 1 ? '' : 's') + '</span>';
                if (meta.callingCount > 0) html += '<span class="sig616-chip sig616-chip-calling">Calling ON</span>';
            }
            html += '<span style="margin-left:auto;font-size:10px;padding:3px 10px;border-radius:12px;background:rgba(255,255,255,0.22);color:#fff;font-weight:700;">' +
                signals.length + ' Signal' + (signals.length > 1 ? 's' : '') + '</span>';
            html += '</div>';
            var tableId = (forExport ? 'wsSignalTableX_' : 'wsSignalTable_') + tableIndex;
            html += '<div class="table-responsive" style="overflow-x:auto;">';
            html += '<table class="table table-sm table-hover mb-0" id="' + tableId + '">';
            html += '<thead><tr>';
            html += '<th style="min-width:90px;font-size:12px;padding:8px 12px;">Signal</th>';
            for (var hi = 0; hi < columns.length; hi++) {
                var disp = fn('getSignalHeaderLabel') ? W.getSignalHeaderLabel(signals[0].id, columns[hi]) : columns[hi];
                var fmtName = fn('formatAliasName') ? W.formatAliasName(disp) : esc(disp);
                html += '<th data-col="' + esc(columns[hi]) + '" title="' + esc(columns[hi]) + '" style="white-space:nowrap;font-size:11px;padding:8px 10px;">' + fmtName + '</th>';
            }
            html += '<th style="min-width:100px;font-size:12px;padding:8px 10px;">DataLogger</th>';
            html += '<th style="min-width:80px;font-size:12px;padding:8px 10px;">Last Update</th>';
            html += '</tr></thead><tbody>';
            for (var si = 0; si < signals.length; si++) html += buildSignalGroupRow(signals[si].id, columns);
            html += '</tbody></table></div></div>';
            tableIndex++;
        }
        html += '</div>';
        return html;
    };

    W._doRenderSignalTables = function (assetIds) {
        var html = W._buildSignalTablesHtml(assetIds, false);
        var snap = fn('_snapshotTableScroll') ? W._snapshotTableScroll() : null;
        $('#divTelemetryLive').html(html);
        if (fn('_restoreTableScroll')) W._restoreTableScroll(snap);
        if (fn('syncDownloadVisibility')) W.syncDownloadVisibility();
        else $('#downloadContainer').show();
    };

    function scheduleFullSignalRender(delay) {
        if (W._signalRebuildTimer) return;
        W._signalRebuildTimer = setTimeout(function () {
            W._signalRebuildTimer = null;
            if (fn('renderSignalAspectTables')) W.renderSignalAspectTables();
        }, delay || 150);
    }

    W.updateSignalAspectTablesIncremental = function (assetIds) {
        var wrap = document.getElementById('signalAspectTablesWrapper');
        if (!wrap) { if (fn('renderSignalAspectTables')) W.renderSignalAspectTables(); return; }
        var LD = liveData();
        var ids = assetIds || Object.keys(W.wsUpdatedAssets || {});
        var groupCache = W._signalGroupCache || {};
        var i;
        for (i = 0; i < ids.length; i++) {
            var aid = String(ids[i]);
            if (!LD[aid]) continue;
            if (groupCache[aid] !== W._getSignalAttrFingerprint(aid)) { scheduleFullSignalRender(150); break; }
        }
        for (i = 0; i < ids.length; i++) {
            var id = String(ids[i]);
            var asset = LD[id];
            if (!asset) continue;
            var rowEl = wrap.querySelector('tr[data-id="' + (window.CSS && CSS.escape ? CSS.escape(id) : id) + '"]');
            if (!rowEl) { scheduleFullSignalRender(150); continue; }
            var cells = fn('getRowCellMap') ? W.getRowCellMap(rowEl) : {};
            for (var an in cells) {
                if (!cells.hasOwnProperty(an)) continue;
                var cell = cells[an];
                if (an === 'LastUpdate') {
                    var ts = lastUpdateText(asset);
                    if (cell.textContent !== ts) cell.textContent = ts;
                    continue;
                }
                var ad = storedAttr(id, an);
                var disp = cellText(ad ? ad.Value : null);
                var hadMarker = cell.querySelector && cell.querySelector('.ws-stale-marker');
                if (cell.textContent !== disp || hadMarker) {
                    cell.textContent = disp;
                }
                var stale = isStaleLocal(id, an);
                if (fn('_setStaleCell')) W._setStaleCell($(cell), stale);
                else $(cell).toggleClass('ws-stale-val', stale);
                $(cell).toggleClass('val-na', disp === '—');
                if (fn('_applyWsServerStale')) W._applyWsServerStale(cell, ad);
            }
            var dlCell = rowEl.querySelector('td.dl-cell');
            if (dlCell) {
                var dh = dlCellHtml(asset);
                if (dlCell.innerHTML !== dh) dlCell.innerHTML = dh;
            }
        }
    };

    // All v617 dispatchers (executeUIUpdate, processItemsFixed,
    // processSingleLiveUpdateFixed) call these two names.
    W.updateSignalGroupedTables = function (updatedIds) {
        if (!document.getElementById('signalAspectTablesWrapper')) { W.renderSignalGroupedTables(); return; }
        var ids = (updatedIds && updatedIds.length) ? updatedIds : Object.keys(W.wsUpdatedAssets || {});
        var LD = liveData();
        var keep = [];
        for (var i = 0; i < ids.length; i++) {
            var aid = String(ids[i]);
            if (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(aid)) {
                delete LD[aid];
                if (W.wsUpdatedAssets) delete W.wsUpdatedAssets[aid];
                $('#signalAspectTablesWrapper tr[data-id="' + aid + '"]').remove();
                continue;
            }
            keep.push(aid);
        }
        if (keep.length) W.updateSignalAspectTablesIncremental(keep);
    };
    var pendingRenderIds = {};
    W.renderSignalGroupedTables = function () {
        var ups = Object.keys(W.wsUpdatedAssets || {});
        for (var i = 0; i < ups.length; i++) pendingRenderIds[ups[i]] = true;
        if (W._sigRenderTimer) clearTimeout(W._sigRenderTimer);
        W._sigRenderTimer = setTimeout(function () {
            W._sigRenderTimer = null;
            var ids = Object.keys(pendingRenderIds);
            pendingRenderIds = {};
            if (document.getElementById('signalAspectTablesWrapper') && ids.length) {
                W.updateSignalGroupedTables(ids);
            } else if (fn('renderSignalAspectTables')) {
                W.renderSignalAspectTables();
            }
        }, 80);
    };

    /* ======================================================================
     * G3  Alias labels (v616 5435-5451)
     * ==================================================================== */
    function getRdpmsDisplayLabel(assetId, title, alternateTitles) {
        var titles = [title].concat(alternateTitles || []);
        if (!fn('getSignalHeaderLabel')) return title;
        for (var i = 0; i < titles.length; i++) {
            var label = W.getSignalHeaderLabel(assetId, titles[i]);
            if (label && label !== titles[i]) return label;
        }
        // Alias lamp names (ISIG RG ...) carry their own AliasName under the
        // stored key, so try the key actually present on the asset too.
        var lamp = /^(RG|DG|HG|HHG) (mA|V)$/.exec(title);
        var asset = liveData()[assetId];
        if (lamp && asset && asset.attrs) {
            var list = lamp[2] === 'mA' ? maAliases(lamp[1]) : vAliases(lamp[1]);
            var ad = attrByAliases(asset.attrs, list);
            var key = ad && (ad.AssetAttributeName || ad.AttributeName);
            if (key && key !== title) {
                var l2 = W.getSignalHeaderLabel(assetId, key);
                if (l2) return l2;
            }
        }
        return W.getSignalHeaderLabel(assetId, title) || title;
    }
    W.getRdpmsDisplayLabel = getRdpmsDisplayLabel;
    function lbl(assetId, title, alt) { return esc(getRdpmsDisplayLabel(assetId, title, alt)); }

    /* ======================================================================
     * G2  Dynamic HPR rows (v616 8095-8226)
     * ==================================================================== */
    var RDPMS_VOLT_SHOW_THRESHOLD = 5;
    W.RDPMS_VOLT_SHOW_THRESHOLD = RDPMS_VOLT_SHOW_THRESHOLD;
    function isVoltName(name) {
        var s = String(name || '').toUpperCase();
        if (s.charAt(0) === 'V') return true;
        return /(^|[^A-Z])V([^A-Z]|$)/.test(s);
    }
    function hprBaseKey(name) {
        return normKey(String(name || '').toUpperCase().replace(/\bMA\b/g, ' ').replace(/\bV\b/g, ' '));
    }
    function collectHprAttrs(assetId, attrs) {
        var out = [], seen = {};
        var aid = String(assetId || '');
        if (!aid || !attrs) return out;
        var byId = {};
        for (var key in attrs) {
            if (!attrs.hasOwnProperty(key)) continue;
            var a = attrs[key];
            if (!a) continue;
            var id = String(a.AssetAttributeId || a.AttrId || '');
            if (id) byId[id] = a;
        }
        var items = [], voltByBase = {};
        var list = metaIndex()[aid] || [];
        for (var i = 0; i < list.length; i++) {
            var e = list[i];
            var alias = e.alias;
            if (!alias || alias.toUpperCase().indexOf('HPR') < 0) continue;
            var av = byId[e.attrId];
            if (!av && e.title && attrs[e.title]) av = attrs[e.title];
            var v = (av && av.Value !== null && av.Value !== undefined) ? parseFloat(av.Value) : NaN;
            if (isNaN(v)) v = 0;
            var volt = isVoltName(alias);
            var base = hprBaseKey(alias);
            if (volt && (!(base in voltByBase) || v > voltByBase[base])) voltByBase[base] = v;
            items.push({ alias: alias, base: base, value: v, isVolt: volt, attr: av || null });
        }
        var j, it;
        for (j = 0; j < items.length; j++) {
            it = items[j];
            if (it.isVolt) continue;
            var pv = voltByBase[it.base];
            var show = (pv !== undefined) ? (pv > RDPMS_VOLT_SHOW_THRESHOLD) : (it.value > 0);
            if (!show) continue;
            var nk = normKey(it.alias);
            if (seen[nk]) continue;
            seen[nk] = true;
            out.push({ label: it.alias, value: it.value, attr: it.attr });
        }
        for (j = 0; j < items.length; j++) {
            it = items[j];
            if (!it.isVolt || it.value <= RDPMS_VOLT_SHOW_THRESHOLD) continue;
            var sibling = false;
            for (var s = 0; s < items.length; s++) {
                if (!items[s].isVolt && items[s].base === it.base) { sibling = true; break; }
            }
            if (sibling) continue;
            var nk2 = normKey(it.alias);
            if (seen[nk2]) continue;
            seen[nk2] = true;
            out.push({ label: it.alias, value: it.value, attr: it.attr });
        }
        return out;
    }
    function hprRowsHtml(rows) {
        var html = '';
        for (var i = 0; i < rows.length; i++) {
            html += '<tr class="rdpms-hpr-row"><td data-attr="' + esc(rows[i].label) + '" colspan="2">' +
                esc(rows[i].label) + ' : <span>' + fv(rows[i].value) + '</span></td></tr>';
        }
        return html;
    }
    function renderHprRows(c, assetId, attrs) {
        var body = c && c.hprBody;
        if (!body) return;
        var rows = collectHprAttrs(assetId, attrs);
        if (!rows.length) {
            if (body.innerHTML !== '') body.innerHTML = '';
            body.style.display = 'none';
            return;
        }
        var html = hprRowsHtml(rows);
        if (body.innerHTML !== html) body.innerHTML = html;
        body.style.display = '';
    }
    function renderShuntHprRows(assetId, attrs) {
        var body = document.getElementById('rdpmsShHprBody_' + assetId);
        if (!body) return;
        var rows = collectHprAttrs(assetId, attrs);
        if (!rows.length) { body.innerHTML = ''; body.style.display = 'none'; return; }
        var html = hprRowsHtml(rows);
        if (body.innerHTML !== html) body.innerHTML = html;
        body.style.display = '';
    }
    W._isVoltName = isVoltName;
    W._hprBaseKey = hprBaseKey;
    W._collectHprAttrs = collectHprAttrs;
    W._renderHprRows = renderHprRows;
    W._renderShuntHprRows = renderShuntHprRows;

    /* ======================================================================
     * G4  Card markup (v616 7725-7844) with v617's Aurora header
     * ==================================================================== */
    function clearSigCache(aid) { if (W._sigDomCache) delete W._sigDomCache[aid]; }
    function cardHeaderRight(asset, assetId) {
        var siteId = (asset && asset.SiteId) || $('#drpSite').val();
        var a = esc(assetId), s = esc(siteId);
        /* v618.33: Graph / Circuit / Avg values replaced by one AI button -> asset drawer
           (the drawer keeps Full circuit view and Avg values as actions). Old buttons:
        return '<div class="card-header-right"><div class="sig-card-actions">' +
            '<button class="sig-action-btn sig-action-graph" onclick="fnGetAssetGraph(\'' + s + '\',\'' + a + '\')" title="Historical Graph"><i class="fa-solid fa-chart-line"></i></button>' +
            '<button class="sig-action-btn sig-action-circuit" onclick="fnGetAssetCircuit(\'' + a + '\')" title="Circuit Diagram"><i class="fa-solid fa-project-diagram"></i></button>' +
            '<button class="sig-action-btn sig-action-avg" onclick="fnShowFRSAttributeRangeHistory(\'' + a + '\')" title="Avg values"><i class="fa-solid fa-chart-column"></i></button>' +
            '</div></div>';
        */
        void s;
        return '<div class="card-header-right"><div class="sig-card-actions">' +
            '<button type="button" class="sig-action-btn tl-aa-ai" onclick="tlOpenAssetDrawer(\'' + a + '\')" title="Asset dashboard: values, AI, circuit, alerts, graph" aria-label="Open asset dashboard"><i class="fa-solid fa-wand-magic-sparkles"></i></button>' +
            '</div></div>';
    }
    var CARD_COL = 'col-12 col-xxl-4 col-xl-6 col-lg-6 col-md-12 col-sm-12 rdpms-signal-card sigv2-col';
    // 617.4: header text laid out horizontally (kind chip + name on one line, time under it)
    function cardHeaderLeft(kind, name, id) {
        return '<div class="card-header-left"><h6 class="sigv2-title">' +
            '<span class="sigv2-kind">' + esc(kind) + '</span>' +
            '<span class="sigv2-name" title="' + esc(name) + '">' + esc(name) + '</span></h6>' +
            '<span class="sigv2-ts" id="rdpmsTs_' + id + '">Updated: </span></div>';
    }
    function ensureSigCardV2Styles() {
        if (document.getElementById('sig-card-v2-styles')) return;
        // #divTelemetryLive prefix: Index.cshtml re-declares these containers as flex rows with !important later in the page.
        var M = '#divTelemetryLive #rdpmsMainContainer', S = '#divTelemetryLive #rdpmsShuntContainer';
        function both(sel) { return M + ' ' + sel + ',' + S + ' ' + sel; }
        var css =
            // grid instead of the legacy wrapping flex row + Bootstrap col widths
            M + ',' + S + '{display:grid!important;grid-template-columns:repeat(auto-fill,minmax(300px,1fr))!important;gap:16px!important;padding:16px!important;margin:0!important;align-items:stretch;}' +
            both('> .rdpms-signal-card') + '{flex:1 1 300px!important;width:auto!important;max-width:none!important;min-width:0!important;margin:0!important;padding:0!important;' +
            'background:transparent!important;border:0!important;box-shadow:none!important;display:flex;flex-direction:column;}' +
            // the card: style.css gives .card 30px padding, which squeezed the header
            both('.sigv2') + '{margin:0!important;padding:0!important;width:100%;height:100%;display:flex;flex-direction:column;border-radius:16px!important;overflow:hidden!important;' +
            'background:linear-gradient(180deg,rgba(255,255,255,.06),rgba(255,255,255,.02))!important;border:1px solid var(--at-edge,rgba(255,255,255,.10))!important;' +
            'box-shadow:0 8px 24px rgba(0,0,0,.28)!important;transition:border-color .18s ease,box-shadow .18s ease,transform .18s ease;}' +
            both('.sigv2:hover') + '{border-color:rgba(34,211,238,.35)!important;box-shadow:0 12px 30px rgba(0,0,0,.36)!important;transform:translateY(-1px);}' +
            both('.sigv2 > .card-header-signal') + '{display:flex!important;flex-direction:row!important;align-items:center!important;gap:10px;margin:0!important;' +
            'padding:12px 12px 12px 14px!important;border-radius:0!important;background:linear-gradient(135deg,rgba(34,211,238,.14),rgba(167,139,250,.10))!important;' +
            'border-bottom:1px solid rgba(255,255,255,.08)!important;min-height:0!important;}' +
            both('.sigv2 .card-header-left') + '{flex:1 1 auto!important;width:auto!important;min-width:0!important;padding:0!important;margin:0!important;background:transparent!important;' +
            'display:flex;flex-direction:column;gap:3px;border-radius:0!important;}' +
            both('.sigv2 .sigv2-title') + '{display:flex!important;align-items:center;gap:7px;margin:0!important;min-width:0;white-space:nowrap;line-height:1.2;}' +
            // legacy rule ".rdpms-signal-card .card-header-left h6 span" dims spans to 60% and forces 10px mono
            both('.sigv2 .sigv2-title > span') + '{opacity:1!important;}' +
            both('.sigv2 .sigv2-kind') + '{flex:0 0 auto;font-size:9px;font-weight:800;letter-spacing:.07em;text-transform:uppercase;padding:2px 7px;border-radius:999px;' +
            'color:var(--at-brand,#22d3ee);background:rgba(34,211,238,.12);border:1px solid rgba(34,211,238,.30);font-family:Manrope,system-ui,sans-serif;}' +
            both('.sigv2 .sigv2-name') + '{flex:1 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:15px!important;font-weight:800;' +
            'color:var(--at-t1,#fff)!important;letter-spacing:.01em;font-family:"Bricolage Grotesque",Manrope,system-ui,sans-serif;}' +
            both('.sigv2 .sigv2-ts') + '{display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-size:10.5px;color:var(--at-t3,rgba(255,255,255,.55));' +
            'font-family:"JetBrains Mono",monospace;opacity:1;}' +
            both('.sigv2 .card-header-right') + '{flex:0 0 auto!important;width:auto!important;min-width:0!important;min-height:0!important;padding:0!important;margin:0!important;' +
            'background:transparent!important;display:flex!important;align-items:center!important;border-radius:0!important;}' +
            both('.sigv2 .sig-card-actions') + '{display:flex!important;gap:5px!important;margin:0!important;padding:0!important;flex-wrap:nowrap!important;}' +
            both('.sigv2 .sig-action-btn') + '{width:30px!important;height:30px!important;border-radius:8px!important;display:inline-flex!important;align-items:center;justify-content:center;' +
            'background:rgba(255,255,255,.06)!important;border:1px solid rgba(255,255,255,.12)!important;color:var(--at-t2,rgba(255,255,255,.8))!important;font-size:12px!important;padding:0!important;}' +
            both('.sigv2 .sig-action-btn:hover') + '{color:var(--at-brand,#22d3ee)!important;border-color:rgba(34,211,238,.45)!important;background:rgba(34,211,238,.10)!important;}' +
            both('.sigv2 .sig-action-btn:focus-visible') + '{outline:2px solid var(--at-brand,#22d3ee);outline-offset:1px;}' +
            both('.sigv2 > .card-body-signal') + '{flex:1 1 auto;display:flex!important;flex-direction:column!important;gap:10px;padding:12px!important;margin:0!important;' +
            'background:transparent!important;justify-content:space-between;}' +
            // lamp stage: centred in the free space instead of stuck to the top
            both('.sigv2 .rdpms-sizeDiv') + ',' + both('.sigv2 .rdpms-sizeShuntDiv') + '{flex:1 1 auto;width:100%;min-height:150px;margin:0!important;display:flex;align-items:center;justify-content:center;' +
            'border-radius:12px;background:radial-gradient(ellipse at 50% 45%,rgba(34,211,238,.07),rgba(0,0,0,0) 65%),rgba(0,0,0,.14);border:1px solid rgba(255,255,255,.05);padding:10px 0 14px;}' +
            both('.sigv2 .rdpms-signal-container') + '{padding-top:calc(60px * var(--rdpms-scale,.7));}' +
            // shunt: the size div also holds the value table + relays, so stack them and stage only the lamp
            both('.sigv2 .rdpms-sizeShuntDiv') + '{flex-direction:column!important;align-items:stretch!important;justify-content:space-between!important;gap:10px;' +
            'background:none!important;border:0!important;padding:0!important;min-height:0!important;}' +
            both('.sigv2 .rdpms-sizeShuntDiv > .rdpms-shunt') + '{align-self:center;margin:18px auto 22px!important;}' +
            both('.sigv2 .rdpms-route-table') + '{width:100%;margin:0!important;}' +
            both('.sigv2 .rdpms-route-table table') + '{width:100%;border-radius:10px;overflow:hidden;}' +
            // relay strip
            both('.sigv2 .rdpms-datalogger-section') + '{margin:0!important;padding:8px 10px!important;border-radius:10px;background:rgba(255,255,255,.035);border:1px solid rgba(255,255,255,.06);}' +
            both('.sigv2 .rdpms-dl-row') + '{display:flex!important;flex-wrap:wrap!important;align-items:center;gap:6px 10px!important;width:100%;background:transparent!important;padding:0!important;border:0!important;}' +
            both('.sigv2 .rdpms-dl-item') + '{display:inline-flex!important;align-items:center;gap:5px;margin:0!important;}' +
            both('.sigv2 .rdpms-dl-item h6') + '{margin:0!important;font-size:11px!important;letter-spacing:.02em;}' +
            both('.sigv2 .rdpms-dl-details-btn') + '{margin-left:auto!important;}' +
            both('.sigv2 [id^="rdpmsDLMore_"]') + '{flex-wrap:wrap;gap:6px 10px;padding-top:4px;}' +
            'body[data-aurora="light"] ' + M + ' .sigv2 .rdpms-sizeDiv,body[data-aurora="light"] ' + S + ' .sigv2 .rdpms-sizeShuntDiv{background:rgba(15,23,42,.04);border-color:rgba(15,23,42,.06);}' +
            // narrow screens: one card per row, compact header
            '@media (max-width:640px){' + M + ',' + S + '{grid-template-columns:1fr!important;padding:10px!important;gap:12px!important;}' +
            both('.sigv2 .sigv2-name') + '{font-size:14px;}}' +
            // light theme
            'body[data-aurora="light"] ' + M + ' .sigv2,body[data-aurora="light"] ' + S + ' .sigv2{background:#fff!important;border-color:rgba(15,23,42,.10)!important;box-shadow:0 2px 12px rgba(15,23,42,.08)!important;}' +
            'body[data-aurora="light"] ' + M + ' .sigv2 > .card-header-signal,body[data-aurora="light"] ' + S + ' .sigv2 > .card-header-signal{background:linear-gradient(135deg,rgba(8,145,178,.10),rgba(124,58,237,.07))!important;border-bottom-color:rgba(15,23,42,.08)!important;}' +
            'body[data-aurora="light"] ' + M + ' .sigv2 .sigv2-name,body[data-aurora="light"] ' + S + ' .sigv2 .sigv2-name{color:#0b1530!important;}' +
            'body[data-aurora="light"] ' + M + ' .sigv2 .sigv2-ts,body[data-aurora="light"] ' + S + ' .sigv2 .sigv2-ts{color:#64748b;}' +
            'body[data-aurora="light"] ' + M + ' .sigv2 .sig-action-btn,body[data-aurora="light"] ' + S + ' .sigv2 .sig-action-btn{background:#f1f5f9!important;border-color:rgba(15,23,42,.12)!important;color:#334155!important;}' +
            '@media (prefers-reduced-motion:reduce){' + both('.sigv2:hover') + '{transform:none;}}';
        var st = document.createElement('style');
        st.id = 'sig-card-v2-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }
    W.ensureSigCardV2Styles = ensureSigCardV2Styles;

    function getMainSignalCardHtml(assetId) {
        clearSigCache(assetId);
        var asset = liveData()[assetId];
        if (!asset) return '';
        var attrs = asset.attrs || {};
        var id = esc(assetId);
        var has = {};
        GROUP_LAMPS.forEach(function (L) { has[L] = !!(attrByAliases(attrs, maAliases(L)) || attrByAliases(attrs, vAliases(L))); });
        var hasPilot = !!(attrs['PILOT mA'] || attrs['PILOT V'] || attrs['PILOTRoot mA'] || attrs['PILOTRoot V']);
        var routes = ROUTE_PREFIXES.filter(function (r) { return attrs[r + ' mA'] || attrs[r + ' V']; });
        var hasRoutes = routes.length > 0;
        var hasCalling = !!(attrs['Co_Hg mA'] || attrs['Co_Hg V']);
        var arms = ['rdpms-arm-1', 'rdpms-arm-2', 'rdpms-arm-3', 'rdpms-arm-4'];
        var h = '<div class="' + CARD_COL + '" id="rdpmsCard_' + id + '">';
        ensureSigCardV2Styles();
        h += '<div class="card card-border sigv2"><div class="card-header-signal">' +
            cardHeaderLeft('Signal', asset.AssetName || 'Signal', id) +
            cardHeaderRight(asset, assetId) + '</div>';
        h += '<div class="card-body-signal"><div class="rdpms-sizeDiv"><div class="rdpms-signal-container">';
        h += '<div class="rdpms-routeSignal' + (hasRoutes ? ' has-routes' : '') + '">';
        if (hasRoutes) {
            h += '<div class="rdpms-centerSignal"><div class="rdpms-white-center light-off" id="rdpmsRootMiddle_' + id + '"></div></div>';
            for (var ri = 0; ri < routes.length; ri++) {
                var armClass = (routes.length === 1) ? 'rdpms-arm-single' : (arms[ri] || arms[0]);
                h += '<div class="rdpms-Signal ' + armClass + '"><span class="rdpms-signal-label">' + routes[ri] + '</span>';
                for (var d = 1; d <= 4; d++) h += '<div class="rdpms-white-always light-off" id="rdpmsRoute_' + id + '_' + routes[ri] + '_' + d + '"></div>';
                h += '</div>';
            }
        }
        if (has.HHG) h += '<div class="rdpms-yellow light-off" id="rdpmsHHG_' + id + '"></div>';
        if (has.DG) h += '<div class="rdpms-green light-off" id="rdpmsDG_' + id + '"></div>';
        if (has.HG) h += '<div class="rdpms-yellow light-off" id="rdpmsHG_' + id + '"></div>';
        if (has.RG) h += '<div class="rdpms-red light-off" id="rdpmsRG_' + id + '"></div>';
        h += '</div>';
        if (hasCalling) h += '<div class="rdpms-routeSignal2"><div class="rdpms-yellow light-off" id="rdpmsCalling_' + id + '"></div><span>C</span></div>';
        h += '</div></div>';
        h += '<div class="rdpms-route-table"><table>';
        GROUP_LAMPS.forEach(function (L) {
            h += '<tr id="rdpmsTr' + L + '_' + id + '" style="display:none"><td id="rdpmsTd' + L + 'ma_' + id + '" data-attr="' + L + ' mA"></td>' +
                '<td id="rdpmsTd' + L + 'v_' + id + '" data-attr="' + L + ' V"></td></tr>';
        });
        if (hasPilot) h += '<tr id="rdpmsTrPilot_' + id + '" style="display:none"><td id="rdpmsTdPilotma_' + id + '" data-attr="PILOT mA"></td><td id="rdpmsTdPilotv_' + id + '" data-attr="PILOT V"></td></tr>';
        if (hasCalling) h += '<tr id="rdpmsTrCO_' + id + '" style="display:none"><td id="rdpmsTdCOma_' + id + '" data-attr="Co_Hg mA"></td><td id="rdpmsTdCOv_' + id + '" data-attr="Co_Hg V"></td></tr>';
        if (hasRoutes) h += '<tr id="rdpmsTrRoute_' + id + '" style="display:none"><td id="rdpmsTdRoutema_' + id + '" data-attr="UG mA"></td><td id="rdpmsTdRoutev_' + id + '" data-attr="UG V"></td></tr>';
        h += '<tr id="rdpmsTrDPR_' + id + '" style="display:none"><td id="rdpmsTdDPRval_' + id + '" data-attr="DPR" colspan="2"></td></tr>';
        h += '<tbody id="rdpmsHprBody_' + id + '" style="display:none"></tbody>';
        h += '</table></div>';
        h += fn('getDataloggerSectionHTML') ? W.getDataloggerSectionHTML(assetId) : '';
        h += '</div></div></div>';
        return h;
    }
    function getShuntSignalCardHtml(assetId) {
        clearSigCache(assetId);
        var asset = liveData()[assetId];
        if (!asset) return '';
        var id = esc(assetId);
        var h = '<div class="' + CARD_COL + '" id="rdpmsCard_' + id + '">';
        ensureSigCardV2Styles();
        h += '<div class="card card-border sigv2 sigv2-shunt"><div class="card-header-signal">' +
            cardHeaderLeft('Shunt', asset.AssetName || 'Shunt', id) +
            cardHeaderRight(asset, assetId) + '</div>';
        h += '<div class="card-body-signal"><div class="rdpms-sizeShuntDiv"><div class="rdpms-shunt"><img class="trianglePng" src="/assets/images/shunt.png" />';
        h += '<div class="rdpms-white light-off" id="rdpmsShuntTop_' + id + '"></div><div class="rdpms-white light-off" id="rdpmsShuntRight_' + id + '"></div><div class="rdpms-white light-off" id="rdpmsShuntLeft_' + id + '"></div></div>';
        h += '<div class="rdpms-route-table"><table>';
        h += '<tr id="rdpmsTrOn_' + id + '" style="display:none"><td id="rdpmsTdOnma_' + id + '" data-attr="On Aspect mA"></td><td id="rdpmsTdOnv_' + id + '" data-attr="On Aspect V"></td></tr>';
        h += '<tr id="rdpmsTrOff_' + id + '" style="display:none"><td id="rdpmsTdOffma_' + id + '" data-attr="Off Aspect mA"></td><td id="rdpmsTdOffv_' + id + '" data-attr="Off Aspect V"></td></tr>';
        h += '<tr id="rdpmsTrShPilot_' + id + '" style="display:none"><td id="rdpmsTdShPilotma_' + id + '" data-attr="PILOT mA"></td><td id="rdpmsTdShPilotv_' + id + '" data-attr="PILOT V"></td></tr>';
        h += '<tbody id="rdpmsShHprBody_' + id + '" style="display:none"></tbody>';
        h += '</table></div>';
        h += fn('getDataloggerSectionHTML') ? W.getDataloggerSectionHTML(assetId) : '';
        h += '</div></div></div></div>';
        return h;
    }
    W.getMainSignalCardHtml = getMainSignalCardHtml;
    W.getShuntSignalCardHtml = getShuntSignalCardHtml;

    // rebuildCard / ensureRdpmsCardDom / renderRDPMSView call these by name.
    W.buildMainSignalCard = function (assetId) {
        var h = getMainSignalCardHtml(assetId);
        if (!h) return;
        $('#rdpmsMainContainer').append(h);
        $('#rdpmsCard_' + assetId).hide().fadeIn(300);
    };
    W.buildShuntSignalCard = function (assetId) {
        var h = getShuntSignalCardHtml(assetId);
        if (!h) return;
        if (!$('#rdpmsShuntContainer').length) {
            var $wrap = $('#divTelemetryLive .rdpms-container');
            if (!$wrap.length) $wrap = $('#rdpmsMainContainer').parent();
            $wrap.append('<hr style="margin:20px 0;border-color:var(--at-edge,#e2e8f0);">' +
                '<h6 style="padding:0 10px;color:var(--at-t3,#64748b);margin-bottom:15px;"><i class="fas fa-random"></i> Shunt Signals</h6>' +
                '<div class="row" id="rdpmsShuntContainer"></div>');
        }
        $('#rdpmsShuntContainer').append(h);
        $('#rdpmsCard_' + assetId).show();
    };

    /* ---- DOM cache + lamp helpers (v616 8231-8307) ---------------------- */
    function buildSigCache(assetId) {
        var p = 'rdpms', g = function (s) { return document.getElementById(p + s + assetId); };
        var c = { id: assetId };
        c.CARD = document.getElementById('rdpmsCard_' + assetId);
        c.card = c.CARD;
        c.RG = g('RG_'); c.DG = g('DG_'); c.HG = g('HG_'); c.HHG = g('HHG_');
        c.CALL = g('Calling_'); c.ROOT = g('RootMiddle_');
        c.trRG = g('TrRG_'); c.trDG = g('TrDG_'); c.trHG = g('TrHG_'); c.trHHG = g('TrHHG_');
        c.trPilot = g('TrPilot_'); c.trCO = g('TrCO_'); c.trRoute = g('TrRoute_'); c.trDPR = g('TrDPR_');
        c.tdRGma = g('TdRGma_'); c.tdRGv = g('TdRGv_'); c.tdDGma = g('TdDGma_'); c.tdDGv = g('TdDGv_');
        c.tdHGma = g('TdHGma_'); c.tdHGv = g('TdHGv_'); c.tdHHGma = g('TdHHGma_'); c.tdHHGv = g('TdHHGv_');
        c.tdDPRval = g('TdDPRval_'); c.tdCOma = g('TdCOma_'); c.tdCOv = g('TdCOv_');
        c.tdPilotma = g('TdPilotma_'); c.tdPilotv = g('TdPilotv_');
        c.tdRoutema = g('TdRoutema_'); c.tdRoutev = g('TdRoutev_');
        c.tsEl = g('Ts_');
        c.hprBody = g('HprBody_');
        // legacy static HPR/HHPR rows (v617 cards built before this file loaded)
        c.trHPR = g('TrHPR_'); c.trHHPR = g('TrHHPR_');
        c.routeLights = {};
        for (var ri = 0; ri < ROUTE_PREFIXES.length; ri++) {
            var rn = ROUTE_PREFIXES[ri];
            c.routeLights[rn] = [null];
            for (var d = 1; d <= 4; d++) c.routeLights[rn].push(document.getElementById('rdpmsRoute_' + assetId + '_' + rn + '_' + d));
        }
        if (!W._sigDomCache) W._sigDomCache = {};
        W._sigDomCache[assetId] = c;
        return c;
    }
    W._buildSigCache = buildSigCache;
    function lightOn(el) { if (!el) return false; el.classList.remove('light-off'); el.classList.add('light-on'); el.style.opacity = '1'; el.style.visibility = 'visible'; return true; }
    function lightOff(el) { if (!el) return false; el.classList.add('light-off'); el.classList.remove('light-on'); el.style.opacity = ''; el.style.visibility = ''; return true; }
    function show(el) { if (el) el.style.display = ''; }
    function hide(el) { if (el) el.style.display = 'none'; }
    function setHtml(el, h) { if (el && el.innerHTML !== h) el.innerHTML = h; }
    function setText(el, t) { if (el && el.textContent !== t) el.textContent = t; }
    W._lightOn = lightOn;
    W._lightOff = lightOff;

    /* ---- G4 helpers ------------------------------------------------------ */
    function rdpmsValSpan(attrData, innerHtml, staleForDisplay) {
        if (staleForDisplay) {
            var t = fn('_wsStaleTitle') ? W._wsStaleTitle(attrData) : '';
            return '<span class="ws-value-server-stale" title="' + t + '">' + innerHtml + '</span>';
        }
        return '<span>' + innerHtml + '</span>';
    }
    W._rdpmsValSpan = rdpmsValSpan;
    function clearDataloggerBadges(assetId) {
        var $row = $('#rdpmsDL_' + assetId);
        if (!$row.length) return;
        $row.empty();
        $row.closest('.rdpms-datalogger-section').hide();
    }
    W._clearDataloggerBadges = clearDataloggerBadges;

    function cardDomReady(aid) {
        return fn('isRdpmsCardDomReady') ? !!W.isRdpmsCardDomReady(aid) : $('#rdpmsCard_' + aid).length > 0;
    }
    function lampStale(ss, L) { return !!(ss && ss.lamps && ss.lamps[L] && ss.lamps[L].staleForDisplay); }
    function tsTextFor(asset) {
        var attrs = asset.attrs || {};
        var latest = '';
        for (var k in attrs) {
            var t = attrs[k] && attrs[k].Timestamp;
            if (t && t > latest) latest = t;
        }
        if (latest) {
            try {
                return 'Updated: ' + new Date(latest).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
            } catch (e) { return 'Updated: ' + (fn('fmtTime') ? W.fmtTime(asset.lastUpdated) : ''); }
        }
        var raw = (asset.lastUpdated && fn('fmtTime')) ? W.fmtTime(asset.lastUpdated) : '';
        return raw ? 'Last: ' + raw : '';
    }
    function flashChanged(c, attrs) {
        if (!c.card) return;
        for (var k in attrs) {
            if (!attrs.hasOwnProperty(k) || !attrs[k] || !attrs[k].changed) continue;
            attrs[k].changed = false;
            var el = c.card.querySelector('[data-attr="' + String(k).replace(/"/g, '\\"') + '"]');
            if (el) { el.classList.remove('rdpms-val-flash'); void el.offsetWidth; el.classList.add('rdpms-val-flash'); }
        }
    }
    function thresholdFor(assetId, asset, onConfirmed) {
        var z = W.zeroOffsetCache ? W.zeroOffsetCache[assetId] : null;
        if ((!z || !z.fetched) && fn('fetchZeroOffsetForAsset')) {
            W.fetchZeroOffsetForAsset(assetId, onConfirmed);
        }
        if (fn('getSignalThreshold')) return W.getSignalThreshold(assetId).value;
        if (z && z.fetched && !isNaN(z.value)) return z.value;
        var def = (typeof W.RDPMS_DEFAULT_THRESHOLD === 'number') ? W.RDPMS_DEFAULT_THRESHOLD : 0.1;
        return parseFloat(asset.ZeroOffsetValue || def);
    }

    /* ---- G4 main signal painter (v616 8456-8808) ------------------------- */
    var repairing = {};
    function updateMainSignalLights616(assetId) {
        var LD = liveData();
        var asset = LD[assetId];
        if (!asset || !asset.attrs) return;
        var attrs = asset.attrs;
        W._rdpmsDiag('updateMainSignalLights ENTER', { assetId: assetId, domReady: cardDomReady(assetId) }, 40);

        if (fn('isMainSignalDomReady') && !W.isMainSignalDomReady(assetId)) {
            // No card area on screen (List view, other asset type): nothing to paint.
            if (!document.getElementById('rdpmsMainContainer') && !cardDomReady(assetId)) return;
            if (repairing[assetId]) return;
            repairing[assetId] = true;
            try {
                if (cardDomReady(assetId)) {
                    if (fn('rebuildCard')) { W.rebuildCard(assetId); return; }
                } else if (fn('ensureRdpmsCardDom')) {
                    W.ensureRdpmsCardDom(assetId);
                }
            } finally { repairing[assetId] = false; }
            clearSigCache(assetId);
            if (!W.isMainSignalDomReady(assetId)) {
                if (cardDomReady(assetId)) {
                    console.warn('[RDPMS Main Signal DOM incomplete]', { assetId: assetId, assetName: asset.AssetName });
                }
                return;
            }
        }
        if (fn('_startSignalLinkIssueTimer')) W._startSignalLinkIssueTimer();

        var cache = W._sigDomCache || {};
        var c = cache[assetId];
        if (!c || c.CARD !== document.getElementById('rdpmsCard_' + assetId)) c = buildSigCache(assetId);

        var threshold = thresholdFor(assetId, asset, function () {
            if (W.updateMainSignalLights) W.updateMainSignalLights(assetId);
        });

        var rgMa = valByAliases(attrs, maAliases('RG')), dgMa = valByAliases(attrs, maAliases('DG')),
            hgMa = valByAliases(attrs, maAliases('HG')), hhgMa = valByAliases(attrs, maAliases('HHG'));
        var rgV = valByAliases(attrs, vAliases('RG')), dgV = valByAliases(attrs, vAliases('DG')),
            hgV = valByAliases(attrs, vAliases('HG')), hhgV = valByAliases(attrs, vAliases('HHG'));
        var pilotMa = attrValue(attrs, 'PILOT mA') || attrValue(attrs, 'PILOTRoot mA');
        var pilotV = attrValue(attrs, 'PILOT V') || attrValue(attrs, 'PILOTRoot V');
        var coHgMa = attrValue(attrs, 'Co_Hg mA'), coHgV = attrValue(attrs, 'Co_Hg V');
        var dprVal = attrValue(attrs, 'DPR');

        var ss = fn('computeSignalState') ? W.computeSignalState(assetId) : { aspect: 'INACTIVE', lamps: {}, hasFreshRdpms: false };
        if (W.signalState) W.signalState[assetId] = ss;
        var current = fn('_signalAspectToPainterKey') ? W._signalAspectToPainterKey(ss.aspect) : 'none';

        lightOff(c.RG); lightOff(c.DG); lightOff(c.HG); lightOff(c.HHG); lightOff(c.CALL); lightOff(c.ROOT);
        for (var r = 0; r < ROUTE_PREFIXES.length; r++) {
            var rl = c.routeLights[ROUTE_PREFIXES[r]];
            lightOff(rl[1]); lightOff(rl[2]); lightOff(rl[3]); lightOff(rl[4]);
        }
        hide(c.trRG); hide(c.trDG); hide(c.trHG); hide(c.trHHG);
        hide(c.trPilot); hide(c.trCO); hide(c.trRoute); hide(c.trDPR);
        hide(c.trHPR); hide(c.trHHPR);
        if (c.hprBody) c.hprBody.style.display = 'none';

        var link = fn('getSignalLampLinkState') ? W.getSignalLampLinkState(assetId) : { linkIssue: false, newestTs: 1 };
        var hasActive = !!(ss.aspect && ss.aspect !== 'INACTIVE' && ss.aspect !== 'NONE');
        if (!hasActive && !ss.hasFreshRdpms) {
            if (link.newestTs <= 0) {
                if (fn('_setSignalLinkIssue')) W._setSignalLinkIssue(assetId, false);
                if (c.tsEl) { setText(c.tsEl, 'Waiting for live data'); c.tsEl.classList.add('rdpms-waiting'); }
            } else {
                if (fn('_setSignalLinkIssue')) W._setSignalLinkIssue(assetId, true);
                clearDataloggerBadges(assetId);
                if (c.tsEl) { setText(c.tsEl, 'Link Issue'); c.tsEl.classList.remove('rdpms-waiting'); }
            }
            return;
        }
        if (fn('_setSignalLinkIssue')) W._setSignalLinkIssue(assetId, false);
        if (current !== 'none' && W.lastSignalState) W.lastSignalState[assetId] = current;

        function lampRow(L, ma, v) {
            setHtml(c['td' + L + 'ma'], lbl(assetId, L + ' mA') + ' : ' +
                rdpmsValSpan(attrByAliases(attrs, maAliases(L)), fv(ma) + ' mA', lampStale(ss, L)));
            setHtml(c['td' + L + 'v'], lbl(assetId, L + ' V') + ' : <span>' + fv(v) + ' V</span>');
        }
        if (current === 'red') {
            lightOn(c.RG); show(c.trRG); lampRow('RG', rgMa, rgV);
            renderHprRows(c, assetId, attrs);
        } else if (current === 'doubleYellow') {
            lightOn(c.HG); lightOn(c.HHG);
            show(c.trHG); lampRow('HG', hgMa, hgV);
            show(c.trHHG); lampRow('HHG', hhgMa, hhgV);
            renderHprRows(c, assetId, attrs);
        } else if (current === 'singleYellow') {
            lightOn(c.HG); show(c.trHG); lampRow('HG', hgMa, hgV);
            renderHprRows(c, assetId, attrs);
        } else if (current === 'green') {
            lightOn(c.DG); show(c.trDG); lampRow('DG', dgMa, dgV);
            show(c.trDPR);
            var dprLabel = fn('rdpmsBind') ? W.rdpmsBind(assetId, 'DPR') : 'DPR';
            setHtml(c.tdDPRval, esc(dprLabel) + ' : <span>' + fv(dprVal) + '</span>');
            renderHprRows(c, assetId, attrs);
        }

        if (coHgMa > threshold) {
            lightOn(c.CALL); show(c.trCO);
            setHtml(c.tdCOma, lbl(assetId, 'Co_Hg mA') + ' : <span>' + fv(coHgMa) + ' mA</span>');
            setHtml(c.tdCOv, lbl(assetId, 'Co_Hg V') + ' : <span>' + fv(coHgV) + ' V</span>');
        }
        if (coHgV > RDPMS_VOLT_SHOW_THRESHOLD) {
            setHtml(c.tdCOv, lbl(assetId, 'Co_Hg V') + ' : <span>' + fv(coHgV) + ' V</span>');
        }

        var activeRoute = null, activeMa = 0, activeV = 0;
        for (var ri = 0; ri < ROUTE_PREFIXES.length; ri++) {
            var rn = ROUTE_PREFIXES[ri];
            var rMa = attrValue(attrs, rn + ' mA');
            if (rMa > threshold) {
                lightOn(c.ROOT);
                var rl2 = c.routeLights[rn];
                lightOn(rl2[1]); lightOn(rl2[2]); lightOn(rl2[3]); lightOn(rl2[4]);
                if (!activeRoute) { activeRoute = rn; activeMa = rMa; activeV = attrValue(attrs, rn + ' V'); }
            }
        }
        if (activeRoute) {
            show(c.trRoute);
            setHtml(c.tdRoutema, lbl(assetId, activeRoute + ' mA') + ' : <span>' + fv(activeMa) + ' mA</span>');
            setHtml(c.tdRoutev, lbl(assetId, activeRoute + ' V') + ' : <span>' + fv(activeV) + ' V</span>');
        }
        if (pilotMa > threshold) {
            show(c.trPilot);
            setHtml(c.tdPilotma, lbl(assetId, 'PILOT mA', ['PILOTRoot mA']) + ' : <span>' + fv(pilotMa) + ' mA</span>');
            setHtml(c.tdPilotv, lbl(assetId, 'PILOT V', ['PILOTRoot V']) + ' : <span>' + fv(pilotV) + ' V</span>');
        }

        if (c.tsEl) { c.tsEl.classList.remove('rdpms-waiting'); setText(c.tsEl, tsTextFor(asset)); }
        flashChanged(c, attrs);
        if (fn('renderDataloggerBadgesForAsset')) W.renderDataloggerBadgesForAsset(assetId);
    }
    W.updateMainSignalLights = updateMainSignalLights616;

    /* ---- G4 shunt painter (v616 8897-9001) ------------------------------- */
    function updateShuntSignalLights616(assetId) {
        var asset = liveData()[assetId];
        if (!asset || !asset.attrs) return;
        if (!cardDomReady(assetId)) return;
        var attrs = asset.attrs;
        var threshold = thresholdFor(assetId, asset, function () { W.updateShuntSignalLights(assetId); });
        var onMa = attrValue(attrs, 'On Aspect mA'), onV = attrValue(attrs, 'On Aspect V');
        var offMa = attrValue(attrs, 'Off Aspect mA'), offV = attrValue(attrs, 'Off Aspect V');
        var pilotMa = attrValue(attrs, 'PILOT mA') || attrValue(attrs, 'PILOTRoot mA');
        var pilotV = attrValue(attrs, 'PILOT V') || attrValue(attrs, 'PILOTRoot V');
        var aspect = (onMa > threshold) ? 'ON' : ((offMa > threshold) ? 'OFF' : 'NONE');
        var top = document.getElementById('rdpmsShuntTop_' + assetId);
        var right = document.getElementById('rdpmsShuntRight_' + assetId);
        var left = document.getElementById('rdpmsShuntLeft_' + assetId);
        if (aspect === 'ON') { lightOff(top); lightOn(right); lightOn(left); }
        else if (aspect === 'OFF') { lightOn(top); lightOn(right); lightOff(left); }
        else { lightOff(top); lightOff(right); lightOff(left); }
        var g = function (s) { return document.getElementById(s + assetId); };
        hide(g('rdpmsTrOn_')); hide(g('rdpmsTrOff_')); hide(g('rdpmsTrShPilot_'));
        if (aspect === 'ON') {
            show(g('rdpmsTrOn_'));
            setHtml(g('rdpmsTdOnma_'), lbl(assetId, 'On Aspect mA') + ' : <span>' + fv(onMa) + ' mA</span>');
            setHtml(g('rdpmsTdOnv_'), lbl(assetId, 'On Aspect V') + ' : <span>' + fv(onV) + ' V</span>');
        } else if (aspect === 'OFF') {
            show(g('rdpmsTrOff_'));
            setHtml(g('rdpmsTdOffma_'), lbl(assetId, 'Off Aspect mA') + ' : <span>' + fv(offMa) + ' mA</span>');
            setHtml(g('rdpmsTdOffv_'), lbl(assetId, 'Off Aspect V') + ' : <span>' + fv(offV) + ' V</span>');
        }
        if (pilotMa > threshold) {
            show(g('rdpmsTrShPilot_'));
            setHtml(g('rdpmsTdShPilotma_'), lbl(assetId, 'PILOT mA', ['PILOTRoot mA']) + ' : <span>' + fv(pilotMa) + ' mA</span>');
            setHtml(g('rdpmsTdShPilotv_'), lbl(assetId, 'PILOT V', ['PILOTRoot V']) + ' : <span>' + fv(pilotV) + ' V</span>');
        }
        renderShuntHprRows(assetId, attrs);
        $('#rdpmsCard_' + assetId).show();
        setText(g('rdpmsTs_'), tsTextFor(asset));
        var $card = $('#rdpmsCard_' + assetId + ' .card-border');
        if ($card.length) {
            $card.removeClass('rdpms-flash'); void $card[0].offsetWidth; $card.addClass('rdpms-flash');
            setTimeout(function () { $card.removeClass('rdpms-flash'); }, 1200);
        }
        if (fn('renderDataloggerBadgesForAsset')) W.renderDataloggerBadgesForAsset(assetId);
    }
    W.updateShuntSignalLights = updateShuntSignalLights616;

    /* ---- B2 per-attribute cell writer (v616 31290-31375) ---------------- */
    var CELLS = {
        'RG mA': { prefix: 'rdpmsTdRGma_', unit: ' mA', lamp: 'RG' }, 'RG V': { prefix: 'rdpmsTdRGv_', unit: ' V' },
        'DG mA': { prefix: 'rdpmsTdDGma_', unit: ' mA', lamp: 'DG' }, 'DG V': { prefix: 'rdpmsTdDGv_', unit: ' V' },
        'HG mA': { prefix: 'rdpmsTdHGma_', unit: ' mA', lamp: 'HG' }, 'HG V': { prefix: 'rdpmsTdHGv_', unit: ' V' },
        'HHG mA': { prefix: 'rdpmsTdHHGma_', unit: ' mA', lamp: 'HHG' }, 'HHG V': { prefix: 'rdpmsTdHHGv_', unit: ' V' },
        'PILOT mA': { prefix: 'rdpmsTdPilotma_', unit: ' mA' }, 'PILOT V': { prefix: 'rdpmsTdPilotv_', unit: ' V' },
        'Co_Hg mA': { prefix: 'rdpmsTdCOma_', unit: ' mA' }, 'Co_Hg V': { prefix: 'rdpmsTdCOv_', unit: ' V' },
        'On Aspect mA': { prefix: 'rdpmsTdOnma_', unit: ' mA' }, 'On Aspect V': { prefix: 'rdpmsTdOnv_', unit: ' V' },
        'Off Aspect mA': { prefix: 'rdpmsTdOffma_', unit: ' mA' }, 'Off Aspect V': { prefix: 'rdpmsTdOffv_', unit: ' V' }
    };
    function aliasListContains(list, name) {
        if (!list) return false;
        var n = normKey(name);
        for (var i = 0; i < list.length; i++) if (normKey(list[i]) === n) return true;
        return false;
    }
    function getSignalRdpmsCellTarget(attrName) {
        if (!attrName) return null;
        for (var i = 0; i < GROUP_LAMPS.length; i++) {
            var L = GROUP_LAMPS[i];
            if (aliasListContains(maAliases(L), attrName)) return { key: L + ' mA', def: CELLS[L + ' mA'] };
            if (aliasListContains(vAliases(L), attrName)) return { key: L + ' V', def: CELLS[L + ' V'] };
        }
        if (CELLS[attrName]) return { key: attrName, def: CELLS[attrName] };
        if (attrName === 'PILOTRoot mA') return { key: 'PILOT mA', def: CELLS['PILOT mA'] };
        if (attrName === 'PILOTRoot V') return { key: 'PILOT V', def: CELLS['PILOT V'] };
        return null;
    }
    W.getSignalRdpmsCellTarget = function (attrName) {
        var t = getSignalRdpmsCellTarget(attrName);
        return t ? { prefix: t.def.prefix, unit: t.def.unit, key: t.key } : null;
    };
    W.updateSignalDataTable = function (assetId, attrName, value) {
        var t = getSignalRdpmsCellTarget(attrName);
        if (!t) return;
        var cell = document.getElementById(t.def.prefix + assetId);
        if (!cell) return;
        var n = parseFloat(value);
        var txt = isNaN(n) ? '-' : fv(n);
        var alt = (t.key === 'PILOT mA') ? ['PILOTRoot mA'] : (t.key === 'PILOT V' ? ['PILOTRoot V'] : null);
        var stale = false, title = '';
        if (t.def.lamp) {
            var ss = (W.signalState && W.signalState[assetId]) || null;
            stale = lampStale(ss, t.def.lamp);
            if (stale && fn('_wsStaleTitle')) {
                var asset = liveData()[assetId];
                title = ' title="' + W._wsStaleTitle(asset ? attrByAliases(asset.attrs, maAliases(t.def.lamp)) : null) + '"';
            }
        }
        cell.innerHTML = lbl(assetId, t.key, alt) + ' : <span class="rdpms-val-flash' +
            (stale ? ' ws-value-server-stale' : '') + '"' + title + '>' + txt + t.def.unit + '</span>';
    };

    // Shunt: the per-attribute path now paints the whole card (lamps, rows,
    // HPR rows) instead of v617's partial lamp logic.
    W.updateShuntSignalLightsOnly = function (assetId) {
        W.updateShuntSignalLights(assetId);
    };
    // Same as v617/v616 but the "Updated" stamp no longer hides the
    // "Waiting for live data" / "Link Issue" state set by the painter.
    W.updateMainSignalLightsOnly = function (assetId, attrName, value) {
        W.updateMainSignalLights(assetId);
        W.updateSignalDataTable(assetId, attrName, value);
        var ts = document.getElementById('rdpmsTs_' + assetId);
        var blanked = $('#rdpmsCard_' + assetId + ' .signal-link-issue-badge').length > 0 ||
            (ts && /^(Waiting for live data|Link Issue)$/.test(ts.textContent));
        if (ts && !blanked && fn('fmtTime')) setText(ts, 'Updated: ' + W.fmtTime(new Date()));
        if (fn('flashSignalValue')) W.flashSignalValue(assetId, attrName);
        if (typeof W.reorderRdpmsCardsByAspect === 'function') W.reorderRdpmsCardsByAspect();
    };
    W.repaintSignalRdpmsCardNow = function (assetId, attrName, value) {
        var aid = String(assetId);
        if (!liveData()[aid]) return;
        W._rdpmsDiag('repaintSignalRdpmsCardNow', { aid: aid, attr: attrName, value: value }, 40);
        if (fn('ensureRdpmsCardDom')) W.ensureRdpmsCardDom(aid);
        if (fn('computeSignalState') && W.signalState) W.signalState[aid] = W.computeSignalState(aid);
        if (isShuntName(aid)) W.updateShuntSignalLightsOnly(aid, attrName, value);
        else if (fn('updateMainSignalLightsOnly')) W.updateMainSignalLightsOnly(aid, attrName, value);
        else W.updateMainSignalLights(aid);
    };

    /* ======================================================================
     * G7  Paint queue (v616 31780-31831)
     * ==================================================================== */
    var paintQueue = {}, paintRunning = false, paintDirty = false;
    var RDPMS_PAINT_CHUNK = 12;
    function raf(f) { return W.requestAnimationFrame ? W.requestAnimationFrame(f) : setTimeout(f, 16); }
    function enqueuePaint(ids) {
        if (!ids || !ids.length) return;
        for (var i = 0; i < ids.length; i++) paintQueue[String(ids[i])] = true;
        if (!paintRunning) { paintRunning = true; raf(drainPaint); }
    }
    function drainPaint() {
        if (!document.getElementById('rdpmsMainContainer')) {
            paintQueue = {}; paintRunning = false; paintDirty = false;
            return;
        }
        var ids = Object.keys(paintQueue);
        if (!ids.length) {
            if (paintDirty && typeof W.reorderRdpmsCardsByAspect === 'function') W.reorderRdpmsCardsByAspect();
            paintDirty = false; paintRunning = false;
            return;
        }
        var n = Math.min(ids.length, RDPMS_PAINT_CHUNK);
        for (var i = 0; i < n; i++) {
            var uid = ids[i];
            delete paintQueue[uid];
            try {
                if (!liveData()[uid]) continue;
                var was = cardDomReady(uid);
                if (fn('ensureRdpmsCardDom') && !W.ensureRdpmsCardDom(uid)) continue;
                if (!was) paintDirty = true;
                if (W.rdpmsCardsBuilt && fn('getCardFingerprint') && fn('rebuildCard') &&
                    W.rdpmsCardsBuilt[uid] !== W.getCardFingerprint(uid)) {
                    W.rebuildCard(uid);
                    paintDirty = true;
                    continue;   // rebuildCard paints
                }
                if (isShuntName(uid)) W.updateShuntSignalLights(uid);
                else W.updateMainSignalLights(uid);
            } catch (e) { console.warn('[SIG616] paint failed for', uid, e); }
        }
        raf(drainPaint);
    }
    W._rdpmsRAF = raf;
    W._enqueueRdpmsPaint = enqueuePaint;
    W._drainRdpmsPaint = drainPaint;
    SIG616.paintQueueSize = function () { return Object.keys(paintQueue).length + (paintRunning ? 0 : 0); };
    SIG616.isPainting = function () { return paintRunning; };

    W.updateRDPMSViewIncremental = function (assetIds) {
        var ids = assetIds || Object.keys(W.wsUpdatedAssets || {});
        if (!document.getElementById('rdpmsMainContainer')) {
            if (fn('renderRDPMSView')) W.renderRDPMSView();
            return;
        }
        var LD = liveData();
        var keep = [];
        for (var i = 0; i < ids.length; i++) {
            var aid = String(ids[i]);
            if (!LD[aid] || (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(aid))) {
                delete LD[aid];
                if (W.wsUpdatedAssets) delete W.wsUpdatedAssets[aid];
                $('#rdpmsCard_' + aid).remove();
                if (W.rdpmsCardsBuilt) delete W.rdpmsCardsBuilt[aid];
                continue;
            }
            keep.push(aid);
        }
        enqueuePaint(keep);
    };

    /* ======================================================================
     * B6 / B4  connectWebSocket wrapper
     * ==================================================================== */
    var infoCacheKey = null;
    var PRESERVE = ['wsLiveData', 'wsAttributeNames', 'wsAttributeOrder', 'wsStaleAttrs', 'rdpmsCardsBuilt',
        'pmCardsBuilt', 'pmEventHistory', 'pmLastOperationTimestamp', 'pmLastOperationType',
        '_signalGroupCache', '_assetInfoListCache', '_sigDomCache', '_rdpmsDomCache',
        'wsCurrentColumns', 'wsTableInitialized', 'pmTableMode'];
    var preserveNext = false;

    function wrapOnOpen(after, keepDom) {
        var ws = W.wsConnection;
        if (!ws || ws.__sig616Wrapped) return;
        ws.__sig616Wrapped = true;
        var orig = ws.onopen;
        ws.onopen = function (e) {
            var $c = $('#divTelemetryLive');
            var kids = keepDom ? $c.children().detach() : null;
            try { if (typeof orig === 'function') orig.call(this, e); }
            finally {
                if (keepDom) { $c.empty().append(kids); }
                if (after) { try { after(); } catch (e2) { console.warn('[SIG616] onopen follow-up failed', e2); } }
            }
        };
    }

    function paramsKey() {
        var p = W.wsConnParams || {};
        return String(p.siteId) + '|' + String(p.assetTypeId);
    }
    if (fn('connectWebSocket')) {
        var origConnect = W.connectWebSocket;
        W.connectWebSocket = function (siteId, assetTypeId, assetIds) {
            var keepCache = W._assetInfoListCache;
            var keep = null;
            if (preserveNext) {
                keep = {};
                PRESERVE.forEach(function (n) { keep[n] = W[n]; });
            }
            var result = origConnect.apply(this, arguments);
            // B6: the metadata column lists belong to a site + asset type, not
            // to one socket. Keep them when the target did not change.
            var key = paramsKey();
            if (keepCache && W._assetInfoListCache !== keepCache && infoCacheKey === key) {
                W._assetInfoListCache = keepCache;
            } else if (infoCacheKey !== key) {
                if (W._assetInfoListCache === keepCache) W._assetInfoListCache = {};
                infoCacheKey = key;
            }
            if (keep) {
                preserveNext = false;
                PRESERVE.forEach(function (n) {
                    if (keep[n] !== undefined && W[n] !== keep[n]) W[n] = keep[n];
                });
                wrapOnOpen(null, true);
            }
            return result;
        };
    }
    SIG616.infoCacheKey = function () { return infoCacheKey; };

    // Keep the cache key in step with the metadata that filled it.
    if (fn('loadBulkAssetMetadata')) {
        var origLBM = W.loadBulkAssetMetadata;
        W.loadBulkAssetMetadata = function (siteId, assetTypeId, callback) {
            return origLBM.call(this, siteId, assetTypeId, function () {
                var key = String(siteId) + '|' + String(assetTypeId);
                if (infoCacheKey !== key) { W._assetInfoListCache = {}; infoCacheKey = key; }
                if (typeof callback === 'function') callback.apply(this, arguments);
            });
        };
    }

    /* ======================================================================
     * C7  Circuit not available for Axle Counter, Gate, ELD, IPS
     * (v616 19096-19135) — asset type NAME first, id only as fallback
     * ==================================================================== */
    var CIRCUIT_UNSUPPORTED_ASSET_TYPE_IDS = [4, 5, 31, 34];
    function typeNameUnsupported(name) {
        var t = String(name || '').trim().toLowerCase();
        if (!t) return null;
        return t === 'ips' || t === 'eld' || t === 'gate' || t === 'axc' ||
            t.indexOf('axle counter') > -1 || /^eld\b/.test(t) || /^ips\b/.test(t);
    }
    function isCircuitUnsupportedAssetType(assetTypeId) {
        var $opt = $('#drpAssetType option:selected');
        if (assetTypeId !== undefined && assetTypeId !== null && String(assetTypeId) !== '') {
            var $o = $('#drpAssetType option').filter(function () { return String(this.value) === String(assetTypeId); });
            if ($o.length) $opt = $o.first();
        }
        var byName = typeNameUnsupported($opt.text());
        if (byName !== null) return byName;
        var id = parseInt((assetTypeId !== undefined && assetTypeId !== null && assetTypeId !== '') ? assetTypeId
            : ($('#drpAssetType').val() || W.wsCurrentAssetTypeId || 0), 10);
        return CIRCUIT_UNSUPPORTED_ASSET_TYPE_IDS.indexOf(id) !== -1;
    }
    W.CIRCUIT_UNSUPPORTED_ASSET_TYPE_IDS = CIRCUIT_UNSUPPORTED_ASSET_TYPE_IDS;
    W.isCircuitUnsupportedAssetType = isCircuitUnsupportedAssetType;
    function syncCircuitAvailability() {
        var off = isCircuitUnsupportedAssetType();
        $('body').toggleClass('sig616-no-circuit', !!off);
        var $opt = $('#drpView option[value="Circuit"]');
        if ($opt.length) {
            $opt.prop('disabled', !!off);
            if (off && $('#drpView').val() === 'Circuit') $('#drpView').val('Table');
        }
        return off;
    }
    SIG616.syncCircuitAvailability = syncCircuitAvailability;
    $(document).on('change.sig616 select2:select.sig616', '#drpAssetType', syncCircuitAvailability);
    $(syncCircuitAvailability);
    ['updateViewTypeRestrictions', 'updateViewTypeOptions'].forEach(function (name) {
        if (!fn(name)) return;
        var orig = W[name];
        W[name] = function () {
            var r = orig.apply(this, arguments);
            try { syncCircuitAvailability(); } catch (e) { }
            return r;
        };
    });
    function refuseCircuit(assetTypeId) {
        if (!isCircuitUnsupportedAssetType(assetTypeId)) return false;
        syncCircuitAvailability();
        if (fn('showInfo')) W.showInfo('Circuit view is not available for this asset type.', 'Circuit');
        return true;
    }

    /* ======================================================================
     * C2  Hidden id sync + safe loader (v616 14021-14227)
     * ==================================================================== */
    function normalizeCircuitId(value) {
        if (value === undefined || value === null) return '';
        value = $.trim(String(value));
        if (value === '' || value === '0') return '';
        var lc = value.toLowerCase();
        if (lc === 'undefined' || lc === 'null') return '';
        return value;
    }
    function setCircuitHiddenValue(id, value, $host) {
        value = normalizeCircuitId(value);
        if (!value) return;
        var $field = $('#' + id);
        if (!$field.length) {
            $host = ($host && $host.length) ? $host : $('#divTelemetryLive');
            if ($host.length) {
                $host.prepend('<input type="hidden" id="' + id + '" />');
                $field = $('#' + id);
            }
        }
        if ($field.length) $field.val(value);
    }
    function syncCircuitHiddenIds(assetTypeId, assetId, $host) {
        assetTypeId = normalizeCircuitId(assetTypeId) || normalizeCircuitId($('#drpAssetType').val()) ||
            normalizeCircuitId(W.currentCircuitAssetTypeId) || normalizeCircuitId(W.wsCurrentAssetTypeId);
        assetId = normalizeCircuitId(assetId) || normalizeCircuitId($('#drpAsset').val()) ||
            normalizeCircuitId(W.currentCircuitAssetId);
        W.currentCircuitAssetTypeId = assetTypeId;
        W.currentCircuitAssetId = assetId;
        setCircuitHiddenValue('hdnassetTypeId', assetTypeId, $host);
        setCircuitHiddenValue('hdndlAssetTypeId', assetTypeId, $host);
        setCircuitHiddenValue('hdnCircuitDiagramAssetTypeId', assetTypeId, $host);
    }
    W.normalizeCircuitId = normalizeCircuitId;
    W.setCircuitHiddenValue = setCircuitHiddenValue;
    W.syncCircuitHiddenIds = syncCircuitHiddenIds;

    function circuitScriptsLoaded() { return !!W._circuitScriptsLoaded; }
    // Shared loader for both the in-page view and the popup.
    // opts: { $host, assetTypeId, assetId, onReady, onFail }
    function loadCircuitMarkup(htmlContent, opts) {
        var cssRx = /<link[^>]+href=["']([^"']+)["'][^>]*>/gi, cm;
        while ((cm = cssRx.exec(htmlContent)) !== null) {
            var href = cm[1].replace(/^~\//, '/');
            if (!document.querySelector('link[href="' + href + '"]')) {
                var lnk = document.createElement('link');
                lnk.rel = 'stylesheet'; lnk.href = href;
                document.head.appendChild(lnk);
            }
        }
        var srcRx = /<script[^>]+data-circuit-script[^>]+src=["']([^"']+)["'][^>]*><\/script>/gi, m;
        var scripts = [];
        while ((m = srcRx.exec(htmlContent)) !== null) scripts.push(m[1]);
        var clean = htmlContent
            .replace(/<script[^>]+data-circuit-script[^>]*>[\s\S]*?<\/script>/gi, '')
            .replace(/<link[^>]+rel=["']stylesheet["'][^>]*>/gi, '');
        var $host = opts.$host;
        $host.empty()[0].innerHTML = clean;
        syncCircuitHiddenIds(opts.assetTypeId, opts.assetId, $host);

        function runInline() {
            var ok = true;
            $host.find('script:not([src])').each(function (index) {
                var text = String(this.textContent || this.innerText || '').trim();
                if (!text) return;
                try {
                    W.eval(text + '\n//# sourceURL=CircuitInlineScript_' + index + '.js');
                } catch (error) {
                    ok = false;
                    console.error('[Circuit] Inline script failed', {
                        index: index, length: text.length,
                        errorName: error && error.name, errorMessage: error && error.message ? error.message : String(error),
                        scriptEnding: text.length > 1500 ? text.substring(text.length - 1500) : text
                    });
                }
            });
            return ok;
        }
        function complete() {
            if (!runInline()) {
                $('#loader').hide();
                console.error('[Circuit] Circuit initialization stopped: the partial contains invalid inline JavaScript.');
                if (fn('showError')) W.showError('Circuit script is incomplete. Check the browser console for CircuitInlineScript details.', 'Circuit');
                if (typeof opts.onFail === 'function') opts.onFail();
                return;
            }
            setTimeout(function () { if (typeof opts.onReady === 'function') opts.onReady(); }, opts.readyDelay || 200);
        }
        if (circuitScriptsLoaded() || !scripts.length) { complete(); return; }
        (function next(i) {
            if (i >= scripts.length) { W._circuitScriptsLoaded = true; complete(); return; }
            var src = scripts[i].replace(/^~\//, '/');
            if (document.querySelector('script[src="' + src + '"]')) { next(i + 1); return; }
            var s = document.createElement('script');
            s.src = src;
            s.onload = function () { next(i + 1); };
            s.onerror = function () { console.warn('[Circuit] Failed to load:', src); next(i + 1); };
            document.head.appendChild(s);
        })(0);
    }
    SIG616.loadCircuitMarkup = loadCircuitMarkup;

    W.loadCircuitScriptsOnce = function (htmlContent, callback) {
        loadCircuitMarkup(htmlContent, { $host: $('#divTelemetryLive'), onReady: callback });
    };
    W.completeCircuitLoad = function () { /* see loadCircuitMarkup (kept as a name for v616 parity) */ };

    if (fn('fnBindCircuit')) {
        var origBindCircuit = W.fnBindCircuit;
        W.fnBindCircuit = function () {
            var atId = normalizeCircuitId($('#drpAssetType').val());
            if (refuseCircuit(atId)) return;
            var aId = normalizeCircuitId($('#drpAsset').val());
            if (!aId && fn('getSelectedAssetIds')) {
                var sel = W.getSelectedAssetIds() || [];
                if (sel.length) { aId = normalizeCircuitId(sel[0]); if (aId) $('#drpAsset').val(aId); }
            }
            if (aId) syncCircuitHiddenIds(atId, aId);
            return origBindCircuit.apply(this, arguments);
        };
    }

    /* ======================================================================
     * B4  Circuit popup keeps / restores the main live stream
     * ==================================================================== */
    // v616 connectCircuitWebSocket (14738-14877): same as v617 but does NOT
    // wipe wsLiveData (the page behind the popup keeps its data).
    if (fn('connectCircuitWebSocket')) {
        var origCircuitWs = W.connectCircuitWebSocket;
        W.connectCircuitWebSocket = function (siteId, assetTypeId, assetId) {
            var keepLive = W.wsLiveData;
            var r = origCircuitWs.apply(this, arguments);
            if (keepLive && W.wsLiveData !== keepLive) {
                var fresh = W.wsLiveData || {};
                W.wsLiveData = keepLive;
                // anything that already arrived for the circuit asset is kept
                for (var k in fresh) if (fresh.hasOwnProperty(k)) keepLive[k] = fresh[k];
            }
            return r;
        };
    }

    var circuitSession = null;
    function captureSession() {
        var p = W.wsConnParams || {};
        return {
            params: { siteId: p.siteId, assetTypeId: p.assetTypeId, assetIds: (p.assetIds || []).slice() },
            hadSocket: !!W.wsConnection,
            view: $('#drpView').val()
        };
    }
    function restoreSession(sess) {
        if (!sess || !sess.params || !normalizeCircuitId(sess.params.siteId)) return;
        if (!sess.hadSocket) return;
        preserveNext = true;
        try {
            W.connectWebSocket(sess.params.siteId, sess.params.assetTypeId, sess.params.assetIds);
        } finally { preserveNext = false; }
    }
    SIG616.circuitSession = function () { return circuitSession; };

    W.fnGetAssetCircuit = function (assetId) {
        var asset = liveData()[assetId];
        if (!asset) { if (fn('showWarning')) W.showWarning('No data available', 'Circuit'); return; }
        var siteId = asset.SiteId || $('#drpSite').val();
        var assetTypeId = asset.AssetTypeId || $('#drpAssetType').val();
        if (refuseCircuit(assetTypeId)) return;
        var assetName = asset.AssetName || assetId;
        if (!circuitSession) circuitSession = captureSession();

        W.circuitAssetId = assetId;
        $('#drpAsset').val(assetId);

        var modalHtml =
            '<div class="tl-modal-overlay" id="rdpmsCircuitOverlay" onclick="closeCircuitModal(event)">' +
            '<div class="tl-modal-shell tl-modal-wide" onclick="event.stopPropagation()">' +
            '<div class="tl-modal-head"><div class="tl-modal-head-left">' +
            '<span class="tl-modal-icon tl-modal-icon-circuit"><i class="fas fa-project-diagram"></i></span>' +
            '<div><div class="tl-modal-title">Circuit Diagram</div>' +
            '<div class="tl-modal-subtitle">' + esc(assetName) + ' <span class="tl-live-badge"><span class="tl-live-dot-sm"></span> LIVE</span></div></div>' +
            '</div><div class="tl-modal-head-actions">' +
            '<button class="tl-modal-fullscreen-btn" onclick="circuitZoom(0.15)" title="Zoom In"><i class="fas fa-search-plus"></i></button>' +
            '<button class="tl-modal-fullscreen-btn" onclick="circuitZoom(-0.15)" title="Zoom Out"><i class="fas fa-search-minus"></i></button>' +
            '<button class="tl-modal-fullscreen-btn" onclick="circuitZoomReset()" title="Fit / Reset Zoom"><i class="fas fa-compress-arrows-alt"></i></button>' +
            '<button class="tl-modal-fullscreen-btn" onclick="toggleModalFullscreen(\'rdpmsCircuitOverlay\')" title="Toggle Fullscreen"><i class="fas fa-expand"></i></button>' +
            '<button class="tl-modal-close" onclick="closeCircuitModal()" title="Close">&times;</button>' +
            '</div></div>' +
            '<div class="tl-modal-body tl-circuit-body">' +
            '<div id="rdpmsCircuitLoading" class="tl-modal-loader"><div class="tl-spinner"></div><span>Loading circuit diagram...</span></div>' +
            '<div id="rdpmsCircuitContent" class="tl-circuit-frame" style="display:none;"></div>' +
            '<div id="rdpmsCircuitError" class="tl-modal-error" style="display:none;"><i class="fas fa-exclamation-triangle"></i><span>Failed to load circuit diagram</span></div>' +
            '</div></div></div>';

        $('#rdpmsCircuitOverlay').remove();
        $('body').append(modalHtml);
        raf(function () { $('#rdpmsCircuitOverlay').addClass('tl-modal-open'); });
        $('body').addClass('tl-modal-active');
        if (fn('disconnectWebSocket')) W.disconnectWebSocket();

        $.ajax({
            url: '/FRS25/Telemetry/_Circuit',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ assetTypeId: assetTypeId, assetId: assetId }),
            dataType: 'text',
            success: function (htmlContent) {
                if (String(W.circuitAssetId) !== String(assetId) || !document.getElementById('rdpmsCircuitOverlay')) return;
                $('#rdpmsCircuitLoading').hide();
                var $content = $('#rdpmsCircuitContent');
                loadCircuitMarkup(htmlContent, {
                    $host: $content, assetTypeId: assetTypeId, assetId: assetId, readyDelay: 300,
                    onReady: function () {
                        $content.show();
                        $content.find('.col-lg-12[style*="border-bottom"]').css('border-bottom', '3px solid #259dab');
                        $content.find('.col-lg-12 > .row').first().hide();
                        if (W.appModel && W.appModel.paper) { try { W.appModel.paper.fitToContent({ padding: 20 }); } catch (e) { } }
                        if (siteId && siteId !== '0' && fn('connectCircuitWebSocket')) {
                            W.connectCircuitWebSocket(siteId, assetTypeId, assetId);
                        }
                        $content.off('wheel.czoom').on('wheel.czoom', function (ev) {
                            ev.preventDefault();
                            var dy = ev.originalEvent ? ev.originalEvent.deltaY : ev.deltaY;
                            if (fn('circuitZoom')) W.circuitZoom(dy < 0 ? 0.12 : -0.12);
                        });
                        if (liveData()[assetId] && fn('updateCircuitFromWebSocket')) {
                            setTimeout(function () { W.updateCircuitFromWebSocket(String(assetId)); }, 150);
                        }
                    },
                    onFail: function () {
                        $content.hide();
                        $('#rdpmsCircuitError').show().find('.sig616-circuit-detail').remove();
                        $('#rdpmsCircuitError').append('<span class="sig616-circuit-detail">The circuit partial contains a script error (see the browser console).</span>');
                    }
                });
                $content.show();
            },
            error: function () {
                $('#rdpmsCircuitLoading').hide();
                $('#rdpmsCircuitError').show();
            }
        });
    };

    if (fn('closeCircuitModal')) {
        var origClose = W.closeCircuitModal;
        W.closeCircuitModal = function (e) {
            var wasOpen = !!document.getElementById('rdpmsCircuitOverlay') &&
                $('#rdpmsCircuitOverlay').hasClass('tl-modal-open');
            var r = origClose.apply(this, arguments);
            var closed = wasOpen && !$('#rdpmsCircuitOverlay').hasClass('tl-modal-open');
            if (closed || (!wasOpen && !(e && e.target))) {
                var sess = circuitSession;
                circuitSession = null;
                restoreSession(sess);
            }
            return r;
        };
    }

    /* ======================================================================
     * C3 / B5  Track circuit: leakage + derived bridge (v616 15025-15310)
     * ==================================================================== */
    function circuitLeakageNorm(value) {
        if (value === undefined || value === null) return '';
        return String(value).toLowerCase().replace(/&nbsp;/g, ' ').replace(/<[^>]*>/g, '')
            .replace(/[\r\n]+/g, ' ').replace(/[^a-z0-9]/g, '');
    }
    function circuitLeakageFormat(value) {
        var n = parseFloat(zf(parseFloat(value)));
        return isNaN(n) ? '—' : n.toFixed(1);
    }
    function isCircuitLeakageLabel(text) {
        var n = circuitLeakageNorm(text);
        return n.indexOf('leakage') > -1 || n.indexOf('ibalst') > -1 || n.indexOf('iblast') > -1 ||
            n.indexOf('ibal') > -1 || n.indexOf('lkgval') > -1;
    }
    function derivedFor(attrs) {
        if (!fn('calculateDerivedValues')) return null;
        try { return W.calculateDerivedValues(attrs || {}, W.circuitAssetId); }
        catch (e) { return W.calculateDerivedValues(attrs || {}); }
    }
    function updateCircuitLeakageIbalstLabels(attrs, derived) {
        if (!W.parseJson || !W.parseJson.cells) return;
        derived = derived || derivedFor(attrs);
        var display = circuitLeakageFormat(derived ? derived.ibalst : null);
        $.each(W.parseJson.cells, function (id, val) {
            if (!val || !val.attrs || !val.attrs.label || val.attrs.label.text === undefined) return;
            if (val.type !== 'examples.Label' && val.type !== 'examples.LabelValue' && val.type !== 'examples.Text') return;
            var oldText = $.trim(String(val.attrs.label.text || '')).replace(/\n\(/g, '(');
            // LKGVAL placeholders become the bare value (v616). The cell is
            // marked so later ticks still find it once the text is a number.
            if (val._sig616Lkg || circuitLeakageNorm(oldText).indexOf('lkgval') > -1) {
                val._sig616Lkg = true;
                val.attrs.label.text = display;
                return;
            }
            if (!isCircuitLeakageLabel(oldText)) return;
            var labelOnly = $.trim(oldText.split(':')[0]);
            val.attrs.label.text = (labelOnly || 'Leakage') + '\n: ' + display;
        });
    }
    function updateHtmlString(html, display) {
        if (!html || !isCircuitLeakageLabel(html)) return html;
        var $wrap = $('<div></div>').html(html);
        var changed = false;
        $wrap.find('tr').each(function () {
            var $cells = $(this).children('th,td');
            if ($cells.length < 2) return;
            if (isCircuitLeakageLabel($cells.eq(0).text())) { $cells.eq(1).text(display); changed = true; }
        });
        $wrap.find('[data-derived],[data-key],[data-attr]').each(function () {
            var $el = $(this);
            var marker = [$el.attr('data-derived'), $el.attr('data-key'), $el.attr('data-attr')].join(' ');
            if (isCircuitLeakageLabel(marker)) { $el.text(display); changed = true; }
        });
        $wrap.find('td,th,span,div').each(function () {
            var $el = $(this);
            if ($el.children().length > 0) return;
            var t = $el.text();
            if (!t || t.indexOf(':') === -1 || !isCircuitLeakageLabel(t)) return;
            var nt = t.replace(/(:\s*)(.*)$/g, '$1' + display);
            if (nt !== t) { $el.text(nt); changed = true; }
        });
        return changed ? $wrap.html() : html;
    }
    function updateCircuitLeakageIbalstHtml(attrs, derived) {
        if (!W.parseJson || !W.parseJson.cells) return;
        derived = derived || derivedFor(attrs);
        var display = circuitLeakageFormat(derived ? derived.ibalst : null);
        $.each(W.parseJson.cells, function (id, cell) {
            if (!cell || !cell.attrs) return;
            if (cell.attrs.body && typeof cell.attrs.body.html === 'string') cell.attrs.body.html = updateHtmlString(cell.attrs.body.html, display);
            if (cell.attrs.html && typeof cell.attrs.html.html === 'string') cell.attrs.html.html = updateHtmlString(cell.attrs.html.html, display);
            if (cell.attrs.foreignObject && typeof cell.attrs.foreignObject.html === 'string') cell.attrs.foreignObject.html = updateHtmlString(cell.attrs.foreignObject.html, display);
        });
    }
    function updateCircuitLeakageIbalst(attrs) {
        var d = derivedFor(attrs);
        updateCircuitLeakageIbalstLabels(attrs, d);
        updateCircuitLeakageIbalstHtml(attrs, d);
    }
    W.circuitLeakageNorm = circuitLeakageNorm;
    W.circuitLeakageFormat = circuitLeakageFormat;
    W.isCircuitLeakageLabel = isCircuitLeakageLabel;
    W.updateCircuitLeakageIbalstLabels = function (attrs) { updateCircuitLeakageIbalstLabels(attrs); };
    W.updateCircuitLeakageIbalstHtml = function (attrs) { updateCircuitLeakageIbalstHtml(attrs); };
    W.updateCircuitLeakageIbalst = updateCircuitLeakageIbalst;
    W.updateHtmlString = function (html) {
        var d = derivedFor((liveData()[W.circuitAssetId] || {}).attrs);
        return updateHtmlString(html, circuitLeakageFormat(d ? d.ibalst : null));
    };

    // v616 updateCircuitTrack (15261-15310): values <= 0 shown as 0, then
    // leakage and derived labels. Label colours stay v617's (dark theme).
    W.updateCircuitTrack = function (attrs) {
        if (!W.parseJson || !W.parseJson.cells) return;
        var map = W.TRACK_CIRCUIT_LABEL_MAP || {};
        $.each(W.parseJson.cells, function (id, val) {
            if (!val.attrs || !val.attrs.label || !val.attrs.label.text) return;
            if (val.type !== 'examples.Label') return;
            var labelText = $.trim(val.attrs.label.text).replace(/\n\(/g, '(');
            for (var attrName in attrs) {
                if (!attrs.hasOwnProperty(attrName)) continue;
                var ad = attrs[attrName];
                if (!ad || ad.Value === null || ad.Value === undefined) continue;
                var n = parseFloat(ad.Value);
                var display = isNaN(n) ? ad.Value : zf(n).toFixed(1);
                var mapped = map[attrName], matched = false;
                if (mapped) {
                    for (var mi = 0; mi < mapped.length; mi++) {
                        if (labelText.indexOf(mapped[mi]) === 0) {
                            val.attrs.label.text = (mapped[mi] + ' : ' + display).replace(/\(/g, '\n(');
                            if (fn('applyCircuitLabelColor')) W.applyCircuitLabelColor(val, attrName, n);
                            matched = true;
                            break;
                        }
                    }
                }
                if (matched) break;
                var clean = attrName.replace(/\s*\([^)]*\)/g, '').trim();
                if (labelText.indexOf(clean) === 0 || labelText.indexOf(attrName) === 0) {
                    val.attrs.label.text = clean + ' : ' + display;
                    if (fn('applyCircuitLabelColor')) W.applyCircuitLabelColor(val, attrName, n);
                    break;
                }
            }
        });
        updateCircuitLeakageIbalst(attrs);
        updateTrackDerivedLabels(attrs);
    };

    /* ======================================================================
     * C4  Derived labels + PM values inside embedded HTML (v616 34441-34991)
     * ==================================================================== */
    function safeNorm(v) { return (v === undefined || v === null) ? '' : String(v).toLowerCase().replace(/[^a-z0-9]/g, ''); }
    function safeFmt(v) { var n = toNum(v); return n === null ? '—' : zf(n).toFixed(2); }
    function safeDerivedFmt(v) { var n = toNum(v); return n === null ? '—' : zf(n).toFixed(1); }
    function getCircuitRoot() {
        try {
            if (W.appModel && W.appModel.paper && W.appModel.paper.el) return $(W.appModel.paper.el);
        } catch (e) { }
        var $modal = $('#rdpmsCircuitContent');
        return $modal.length ? $modal : $('#divTelemetryLive');
    }
    function attrIdOf(a) {
        if (!a) return null;
        var n = parseInt(a.AttrId || a.AssetAttributeId || a.AttributeId || a.Id || a.id, 10);
        return isNaN(n) ? null : n;
    }
    function valueByNameOrId(attrs, names, ids) {
        attrs = attrs || {};
        var i;
        for (i = 0; i < names.length; i++) if (attrs[names[i]] !== undefined && attrs[names[i]] !== null) return toNum(attrs[names[i]]);
        var nameMap = {}, idMap = {};
        for (i = 0; i < names.length; i++) nameMap[safeNorm(names[i])] = true;
        for (i = 0; i < ids.length; i++) idMap[parseInt(ids[i], 10)] = true;
        for (var key in attrs) {
            if (!attrs.hasOwnProperty(key)) continue;
            var a = attrs[key];
            if (nameMap[safeNorm(key)]) return toNum(a);
            var an = a && (a.AssetAttributeName || a.AttributeName || a.Name || a.Title || a.AliasName);
            if (an && nameMap[safeNorm(an)]) return toNum(a);
            var aid = attrIdOf(a);
            if (aid !== null && idMap[aid]) return toNum(a);
        }
        return null;
    }
    function getPmValues(attrs) {
        return {
            aNwkr: valueByNameOrId(attrs, ['A End - NWKR', '(A)NWKR', 'NWKR A End', 'A_NWKR'], [25]),
            aRwkr: valueByNameOrId(attrs, ['A End - RWKR', '(A)RWKR', 'RWKR A End', 'A_RWKR'], [26]),
            bNwkr: valueByNameOrId(attrs, ['B End - NWKR', '(B)NWKR', 'NWKR B End', 'B_NWKR'], [27]),
            bRwkr: valueByNameOrId(attrs, ['B End - RWKR', '(B)RWKR', 'RWKR B End', 'B_RWKR'], [28]),
            aNwkrLoc: valueByNameOrId(attrs, ['A End - NWKR (Loc)', 'A NWKR Loc', 'A_NWKR_LOC'], [576]),
            aRwkrLoc: valueByNameOrId(attrs, ['A End - RWKR (Loc)', 'A RWKR Loc', 'A_RWKR_LOC'], [577]),
            bNwkrLoc: valueByNameOrId(attrs, ['B End - NWKR (Loc)', 'B NWKR Loc', 'B_NWKR_LOC'], [578]),
            bRwkrLoc: valueByNameOrId(attrs, ['B End - RWKR (Loc)', 'B RWKR Loc', 'B_RWKR_LOC'], [579])
        };
    }
    function isAEnd(t) { t = safeNorm(t); return t === 'a' || t.indexOf('aend') > -1 || t.indexOf('anwkr') > -1 || t.indexOf('arwkr') > -1; }
    function isBEnd(t) { t = safeNorm(t); return t === 'b' || t.indexOf('bend') > -1 || t.indexOf('bnwkr') > -1 || t.indexOf('brwkr') > -1; }
    function pmKeyFromText(text) {
        var t = safeNorm(text);
        var n = t.indexOf('nwkr') > -1, r = t.indexOf('rwkr') > -1, loc = t.indexOf('loc') > -1;
        if (!n && !r) return null;
        if (isAEnd(t)) return n ? (loc ? 'aNwkrLoc' : 'aNwkr') : (loc ? 'aRwkrLoc' : 'aRwkr');
        if (isBEnd(t)) return n ? (loc ? 'bNwkrLoc' : 'bNwkr') : (loc ? 'bRwkrLoc' : 'bRwkr');
        return null;
    }
    function setCell($cell, v) {
        if (!$cell || !$cell.length) return;
        var d = safeFmt(v);
        if ($cell.text() !== d) $cell.text(d);
    }
    function updatePmHeaderTable($table, values) {
        var $rows = $table.find('tr');
        if ($rows.length < 2) return;
        var $head = $rows.first().children('th,td');
        if ($head.length < 2) return;
        var col = { nwkr: -1, rwkr: -1, nwkrLoc: -1, rwkrLoc: -1 };
        $head.each(function (i) {
            var h = safeNorm($(this).text());
            if (h.indexOf('nwkr') > -1 && h.indexOf('loc') > -1) col.nwkrLoc = i;
            else if (h.indexOf('rwkr') > -1 && h.indexOf('loc') > -1) col.rwkrLoc = i;
            else if (h.indexOf('nwkr') > -1) col.nwkr = i;
            else if (h.indexOf('rwkr') > -1) col.rwkr = i;
        });
        $rows.slice(1).each(function () {
            var $cells = $(this).children('th,td');
            if (!$cells.length) return;
            var first = $cells.eq(0).text();
            var p = isAEnd(first) ? 'a' : (isBEnd(first) ? 'b' : null);
            if (!p) return;
            if (col.nwkr > -1) setCell($cells.eq(col.nwkr), values[p + 'Nwkr']);
            if (col.rwkr > -1) setCell($cells.eq(col.rwkr), values[p + 'Rwkr']);
            if (col.nwkrLoc > -1) setCell($cells.eq(col.nwkrLoc), values[p + 'NwkrLoc']);
            if (col.rwkrLoc > -1) setCell($cells.eq(col.rwkrLoc), values[p + 'RwkrLoc']);
        });
    }
    function updatePmForeignObjectTables(attrs) {
        var values = getPmValues(attrs || {});
        var $root = getCircuitRoot();
        if (!$root || !$root.length) return;
        $root.find('foreignObject').each(function () {
            var $fo = $(this);
            var text = safeNorm($fo.text());
            if (text.indexOf('nwkr') === -1 && text.indexOf('rwkr') === -1 && text.indexOf('point') === -1) return;
            $fo.find('[data-attr],[data-key],[data-name]').each(function () {
                var $el = $(this);
                var key = pmKeyFromText([$el.attr('data-attr'), $el.attr('data-key'), $el.attr('data-name')].join(' '));
                if (key) setCell($el, values[key]);
            });
            $fo.find('table').each(function () {
                var $t = $(this);
                updatePmHeaderTable($t, values);
                $t.find('tr').each(function () {
                    var $cells = $(this).children('th,td');
                    if ($cells.length < 2) return;
                    var key = pmKeyFromText($cells.eq(0).text());
                    if (key) setCell($cells.eq(1), values[key]);
                });
            });
            $fo.find('td,th,span,div').each(function () {
                var $el = $(this);
                if ($el.children().length > 0) return;
                var t = $el.text();
                if (!t || t.indexOf(':') === -1) return;
                var key = pmKeyFromText(t);
                if (!key) return;
                var nt = t.replace(/(:\s*)(.*)$/g, '$1' + safeFmt(values[key]));
                if (nt !== t) $el.text(nt);
            });
        });
    }

    var TRACK_DERIVED_RULES = [
        { key: 'itcBattCharg', label: 'ITC BATT CHARG', aliases: ['ITC BATT CHARG', 'BATT CHARG'] },
        { key: 'vtcVarRes', label: 'VTC VAR RES', aliases: ['VTC VAR RES'] },
        { key: 'rtcChFeedEnd', label: 'RTC CH FEED END', aliases: ['RTC CH FEED END'] },
        { key: 'rtcVarRes', label: 'RTC VAR RES', aliases: ['RTC VAR RES'] },
        { key: 'vtcTr', label: 'VTC TR', aliases: ['VTC TR'] },
        { key: 'ibalst', label: 'IBALST', aliases: ['IBALST', 'I BALST', 'IBLAST', 'I BLAST'] },
        { key: 'rrail', label: 'RRAIL', aliases: ['RRAIL', 'R RAIL'] }
    ];
    function getDerivedKeyFromText(text) {
        var t = safeNorm(text);
        for (var i = 0; i < TRACK_DERIVED_RULES.length; i++) {
            var rule = TRACK_DERIVED_RULES[i];
            for (var j = 0; j < rule.aliases.length; j++) {
                if (t.indexOf(safeNorm(rule.aliases[j])) > -1) return rule.key;
            }
        }
        return null;
    }
    function getDerivedRuleByKey(key) {
        for (var i = 0; i < TRACK_DERIVED_RULES.length; i++) if (TRACK_DERIVED_RULES[i].key === key) return TRACK_DERIVED_RULES[i];
        return null;
    }
    function updateTrackDerivedLabels(attrs) {
        if (!W.parseJson || !W.parseJson.cells) return;
        var derived = derivedFor(attrs);
        if (!derived) return;
        $.each(W.parseJson.cells, function (id, cell) {
            if (!cell || !cell.attrs || !cell.attrs.label || cell.attrs.label.text === undefined) return;
            var type = String(cell.type || '');
            if (type !== 'examples.Label' && type !== 'examples.LabelValue' && type !== 'examples.Text') return;
            var labelOnly = String(cell.attrs.label.text || '').replace(/\n/g, ' ').split(':')[0];
            var key = getDerivedKeyFromText(labelOnly);
            if (!key) return;
            var rule = getDerivedRuleByKey(key);
            if (rule) cell.attrs.label.text = rule.label + '\n: ' + safeDerivedFmt(derived[key]);
        });
    }
    function updateTrackDerivedForeignObject(attrs) {
        var derived = derivedFor(attrs);
        if (!derived) return;
        var $root = getCircuitRoot();
        if (!$root || !$root.length) return;
        $root.find('foreignObject').each(function () {
            var $fo = $(this);
            var text = safeNorm($fo.text());
            if (!/battcharg|varres|ibalst|iblast|rrail|vtctr|derived/.test(text)) return;
            $fo.find('[data-derived],[data-key],[data-attr]').each(function () {
                var $el = $(this);
                var key = getDerivedKeyFromText([$el.attr('data-derived'), $el.attr('data-key'), $el.attr('data-attr')].join(' '));
                if (!key) return;
                var d = safeDerivedFmt(derived[key]);
                if ($el.text() !== d) $el.text(d);
            });
            $fo.find('tr').each(function () {
                var $cells = $(this).children('th,td');
                if ($cells.length < 2) return;
                var key = getDerivedKeyFromText($cells.eq(0).text());
                if (!key) return;
                var d = safeDerivedFmt(derived[key]);
                if ($cells.eq(1).text() !== d) $cells.eq(1).text(d);
            });
            $fo.find('td,th,span,div').each(function () {
                var $el = $(this);
                if ($el.children().length > 0) return;
                var t = $el.text();
                if (!t || t.indexOf(':') === -1) return;
                var key = getDerivedKeyFromText(t);
                if (!key) return;
                var nt = t.replace(/(:\s*)(.*)$/g, '$1' + safeDerivedFmt(derived[key]));
                if (nt !== t) $el.text(nt);
            });
        });
    }
    function circuitTypeFor(assetId) {
        var asset = liveData()[String(assetId)] || liveData()[assetId];
        return parseInt($('#drpAssetType').val() || $('#hdnCircuitDiagramAssetTypeId').val() ||
            (asset && asset.AssetTypeId) || W.wsCurrentAssetTypeId || 0, 10);
    }
    function applySafeCircuitDomUpdates(assetId) {
        var asset = liveData()[String(assetId)] || liveData()[assetId];
        if (!asset || !asset.attrs) return;
        var t = circuitTypeFor(assetId);
        if (t === 1) updateTrackDerivedForeignObject(asset.attrs);
        else if (t === 3) updatePmForeignObjectTables(asset.attrs);
    }
    W.safeDerivedFmt = safeDerivedFmt;
    W.getCircuitRoot = getCircuitRoot;
    W.getDerivedKeyFromText = getDerivedKeyFromText;
    W.getDerivedRuleByKey = getDerivedRuleByKey;
    W.updateTrackDerivedLabels = updateTrackDerivedLabels;
    W.updateTrackDerivedForeignObject = updateTrackDerivedForeignObject;
    W.updatePmForeignObjectTables = updatePmForeignObjectTables;
    W.applySafeCircuitDomUpdates = applySafeCircuitDomUpdates;

    if (fn('updateCircuitFromWebSocket')) {
        var origUCW = W.updateCircuitFromWebSocket;
        W.updateCircuitFromWebSocket = function (assetId) {
            if (W.circuitAssetId && String(assetId) === String(W.circuitAssetId)) {
                var asset = liveData()[assetId] || liveData()[String(assetId)];
                if (asset && asset.attrs && circuitTypeFor(assetId) === 1) {
                    // B5: bridge read by _Circuit.cshtml BindCircuitTrackSimulation
                    var d = derivedFor(asset.attrs) || {};
                    W.circuitDerived = d;
                    W.circuitLeakage = (d.ibalst !== null && d.ibalst !== undefined && !isNaN(d.ibalst))
                        ? zf(parseFloat(d.ibalst)).toFixed(1) : '0';
                }
                if (asset && circuitTypeFor(assetId) === 3 && fn('fnBindPointTable')) {
                    try { W.fnBindPointTable(assetId); } catch (e) { }
                }
            }
            var r = origUCW.apply(this, arguments);
            setTimeout(function () { applySafeCircuitDomUpdates(assetId); }, 0);
            setTimeout(function () { applySafeCircuitDomUpdates(assetId); }, 100);
            return r;
        };
    }

    SIG616.version = '616-port-1';
    console.log('[SIG616] Signal / circuit v616 features installed');
})();
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-signal616" failed to load', e);
}


/* #############################################################################
 * SECTION: IPS CARDS + TABLE (v616 port, card v2)
 * (was telemetrylive-ips616.js)
 * ########################################################################## */
try {
/* =============================================================================
 * telemetrylive-ips616.js — IPS live view features from v616, ported to v617
 * (Aurora)
 *
 * Load order: AFTER telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-pmvibration.js (classic <script>). Nothing in those files is
 * edited; global functions are replaced on window, and every caller looks
 * them up by name when it runs.
 *
 * v616 line numbers refer to v616 TelemetryLive/telemetrylive.js.
 *
 *  I1  One display rule for cards and table: numbers to 2 decimals,
 *      values <= 0 shown as 0, text passed through
 *      (ipsFormatDisplayValue) ........................................ v616 9334-9366
 *  I2  Σ sum rows on BATT CHARGING / DISCHARGING cards
 *      (ensureIpsCardSumStyles, ipsCardAttrLabel, ipsCardBattBucket,
 *      ipsCardSumKey, buildIpsCardSumModels, ipsCardSumDisplay,
 *      buildIpsCardSumRowsHtml, updateIpsCardSums; buildIpsCard and
 *      updateIpsGridIncremental rebuild a card when its rows change) . v616 9368-9770
 *  I3  Voltage / Current / Digital tabs on the flat table
 *      (ipsClassifyTab, ipsApplyTab) .................................. v616 9842-9854, 10510-10540
 *  I4  BATT CHARGING / DISCHARGING banks summed into one row that
 *      expands to the per-bank rows (ipsGroupCurrentRows,
 *      ipsGroupIsExpanded, ipsExpandRowsForDisplay) ................... v616 10148-10309
 *  I5  Row model: relay (Digital) rows first, Pickup/Drop badges,
 *      replay-stale "!" marks (buildIpsTableRowModels) ................ v616 9856-10146
 *      v617 keeps relays in asset.dlRelays (not in attrs), so the
 *      Digital rows are built from dlRelays.
 *  I6  Downloads carry only the active tab, with banks summed
 *      (ipsActiveTabLabel, ipsViewSuffix, ipsActiveTabSlug,
 *      ipsExportRowsForActiveTab); the live-table scrape skips hidden
 *      rows and labels the section with the tab name ................ v616 10311-10345, 20628, 21968, 22500
 *      NOTE: window.buildIpsTableRowModels() now returns these export
 *      rows (v617's PDF export calls it); pass {all: true} for every row.
 *  Flat table renderer / updater (ipsRenderTable, ipsBuildTableHtml,
 *  ipsUpdateTable) .................................................... v616 10354-10492
 * ========================================================================== */

(function () {
    'use strict';

    if (typeof window === 'undefined' || typeof window.jQuery === 'undefined') return;
    if (window.__ips616Installed) return;
    window.__ips616Installed = true;

    var $ = window.jQuery;
    var W = window;
    var IPS616 = W.IPS616 = W.IPS616 || {};
    var EMPTY = '—';

    function fn(name) { return typeof W[name] === 'function'; }
    function liveData() { return W.wsLiveData || {}; }
    function esc(s) {
        return String(s === null || s === undefined ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function zf(v) {
        if (typeof W.tlZeroFloor === 'function') return W.tlZeroFloor(v);
        var n = parseFloat(v);
        return isNaN(n) ? v : (n <= 0 ? 0 : n);
    }
    function natural(a, b) {
        return String(a || '').localeCompare(String(b || ''), undefined, { numeric: true, sensitivity: 'base' });
    }

    /* ---- I1 ------------------------------------------------------------- */
    function ipsFormatDisplayValue(rawValue, emptyText) {
        if (rawValue === null || rawValue === undefined || String(rawValue).trim() === '') return emptyText;
        var n = parseFloat(rawValue);
        if (isNaN(n)) return rawValue;
        return Number(zf(n)).toFixed(2);
    }
    W.ipsFormatDisplayValue = ipsFormatDisplayValue;

    if (!fn('ipsBattBaseAttrName')) {
        W.ipsBattBaseAttrName = function (label) {
            return String(label || '')
                .replace(/(DISCHARGING|DISCHARGER|DISCHARGE|DISCHAR|CHARGING|CHARGER|CHARGE|CHAR)[\-_]\s*\d+/gi, '$1')
                .replace(/\s{2,}/g, ' ').trim();
        };
    }

    /* ---- styles (Aurora tokens) ------------------------------------------ */
    function ensureStyles() {
        if (document.getElementById('ips616-styles')) return;
        var css =
            '.ips-tabbar{display:flex;gap:6px;flex-wrap:wrap;margin:0 2px 10px;}' +
            '.ips-tab-btn{appearance:none;border:1px solid var(--at-edge-s,rgba(255,255,255,.18));background:var(--at-g1,rgba(255,255,255,.04));' +
            'color:var(--at-t2,rgba(255,255,255,.72));font-size:13px;font-weight:600;padding:7px 14px;border-radius:var(--at-r-sm,8px);cursor:pointer;' +
            'display:inline-flex;align-items:center;gap:6px;transition:background .15s ease,color .15s ease;font-family:var(--at-font-body,inherit);}' +
            '.ips-tab-btn:hover{background:var(--at-g3,rgba(255,255,255,.08));color:var(--at-t1,#fff);}' +
            '.ips-tab-btn.active{background:rgba(34,211,238,.16);border-color:var(--at-brand,#22d3ee);color:var(--at-brand,#22d3ee);}' +
            '.ips-tab-btn .ips-tab-count{font-size:11px;padding:1px 7px;border-radius:999px;background:var(--at-g3,rgba(255,255,255,.08));color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.ips-tab-empty{padding:22px 12px;text-align:center;color:var(--at-t4,rgba(255,255,255,.34));font-size:13px;' +
            'border:1px dashed var(--at-edge,rgba(255,255,255,.10));border-radius:var(--at-r-md,12px);margin-top:8px;}' +
            'table.ips-live-table tbody tr.ips-row-hidden{display:none;}' +
            'table.ips-live-table tbody tr.ips-row-group{background:rgba(34,211,238,.07);}' +
            'table.ips-live-table tbody tr.ips-row-group td.ips-aname{color:var(--at-brand,#22d3ee);}' +
            'table.ips-live-table tbody tr.ips-row-expandable{cursor:pointer;}' +
            'table.ips-live-table tbody tr.ips-row-expandable:hover{background:rgba(34,211,238,.12);}' +
            'table.ips-live-table tbody tr.ips-child-collapsed{display:none;}' +
            'table.ips-live-table tbody tr.ips-row-child{background:var(--at-g1,rgba(255,255,255,.03));}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-aname{font-weight:500;color:var(--at-t3,rgba(255,255,255,.5));padding-left:28px;position:relative;}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-aname::before{content:"\\21B3";position:absolute;left:12px;color:var(--at-t4,rgba(255,255,255,.34));}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-val{font-weight:600;color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.ips-caret{display:inline-block;width:0;height:0;margin-right:7px;vertical-align:middle;border-left:5px solid var(--at-brand,#22d3ee);' +
            'border-top:4px solid transparent;border-bottom:4px solid transparent;transition:transform .15s ease;}' +
            'tr.ips-group-open .ips-caret{transform:rotate(90deg);}' +
            '.ips-child-badge{display:inline-block;margin-left:6px;background:var(--at-brand-deep,#0ea5b7);color:#fff;font-size:11px;font-weight:700;' +
            'line-height:1;padding:2px 7px;border-radius:999px;vertical-align:middle;}' +
            'table.ips-live-table td.ips-val .rdpms-dl-badge{font-size:11px;}' +
            // replay-stale "!" marker (same look as the signal port)
            '.ips-live-table td.ips-val.ws-value-server-stale{color:var(--at-t3,rgba(255,255,255,.5))!important;}' +
            '.ips-live-table td.ips-val.ws-value-server-stale::before{content:"!";display:inline-flex;align-items:center;justify-content:center;' +
            'width:14px;height:14px;margin-right:4px;border-radius:50%;background:var(--at-t4,rgba(255,255,255,.34));color:var(--at-bg0,#060914);' +
            'font-size:10px;font-weight:800;line-height:14px;vertical-align:middle;}' +
            // Σ sum rows on cards
            '.ips-attr-row.ips-sum-row{border-top:1px dashed var(--at-edge-s,rgba(255,255,255,.18));margin-top:4px;padding-top:6px;}' +
            '.ips-attr-row.ips-sum-row .ips-attr-name{font-weight:700;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-row.ips-sum-row .ips-sum-sigma{font-weight:700;margin-right:5px;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-row.ips-sum-row .ips-sum-count{display:inline-block;margin-left:6px;background:var(--at-brand-deep,#0ea5b7);color:#fff;' +
            'font-size:10px;font-weight:700;line-height:1;padding:2px 6px;border-radius:999px;vertical-align:middle;}' +
            '.ips-attr-val.ips-sum-val{font-weight:800;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-val.ips-sum-val.no-data{color:var(--at-t4,rgba(255,255,255,.34));font-weight:400;}' +
            '.ips-card-head .tl-asset-action{margin-left:auto;margin-right:6px;}';
        var st = document.createElement('style');
        st.id = 'ips616-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }
    W.ensureIpsCardSumStyles = ensureStyles;
    ensureStyles();

    /* ---- labels / asset filter ------------------------------------------ */
    function plainLabel(assetId, attrKey, aObj) {
        aObj = aObj || {};
        var label = aObj.AliasName || aObj.AttrName || attrKey;
        var id = aObj.AttrId || aObj.AssetAttributeId;
        if (fn('getAttrDisplayNamePlain')) label = W.getAttrDisplayNamePlain(attrKey, id, assetId) || label;
        else if (fn('getAttrDisplayName')) label = W.getAttrDisplayName(attrKey, id, assetId) || label;
        return String(label).replace(/<[^>]*>/g, '').trim() || attrKey;
    }
    function ipsCardAttrLabel(attrKey, attrObject, assetId) { return plainLabel(assetId, attrKey, attrObject); }
    W.ipsCardAttrLabel = ipsCardAttrLabel;

    function ipsAssetIds() {
        var LD = liveData();
        return Object.keys(LD).filter(function (id) {
            if (!LD[id]) return false;
            if (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(id)) return false;
            return !fn('assetMatchesSelectedType') || W.assetMatchesSelectedType(id);
        });
    }
    function assetTs(asset) {
        var ts = EMPTY;
        if (fn('getOperationTimestampDevice')) {
            var op = W.getOperationTimestampDevice(asset);
            if (op && fn('fmtTimestampDevice')) ts = W.fmtTimestampDevice(op);
        }
        if (ts === EMPTY && asset.lastUpdated && fn('fmtTime')) ts = W.fmtTime(asset.lastUpdated);
        return ts;
    }
    function isDlAttr(asset, key, aObj) {
        return String((aObj && aObj.DataType) || '').trim().toLowerCase() === 'datalogger' ||
            Object.prototype.hasOwnProperty.call((asset && asset.dlRelays) || {}, key);
    }
    function sortedAttrKeys(attrs) {
        var keys = Object.keys(attrs || {});
        keys.sort(function (a, b) {
            var oA = (attrs[a] && attrs[a].AttrOrder != null) ? parseInt(attrs[a].AttrOrder, 10) : 999;
            var oB = (attrs[b] && attrs[b].AttrOrder != null) ? parseInt(attrs[b].AttrOrder, 10) : 999;
            return oA - oB;
        });
        return keys;
    }

    /* ---- I3 / I5  row models --------------------------------------------- */
    // Digital = data-logger relay; Current = IIPS reading, or a battery
    // charging / discharging reading (by attribute name, or because it belongs
    // to a BATT CHARGING / DISCHARGING asset) unless it is named as a voltage;
    // Voltage = everything else.
    function ipsBattBucketOf(text) {
        var up = String(text || '').toUpperCase();
        if (/DISCHAR/.test(up)) return 'discharging';
        if (/CHARG|\bCHAR[\s_\-]*\d/.test(up)) return 'charging';
        return null;
    }
    function ipsClassifyTab(isDataLogger, attrKey, attrLabel, assetName) {
        if (isDataLogger) return 'digital';
        var hay = (String(attrKey || '') + ' ' + String(attrLabel || '')).toUpperCase();
        if (hay.indexOf('IIPS') !== -1) return 'current';
        if ((ipsBattBucketOf(hay) || ipsBattBucketOf(assetName)) && !/VIPS|VOLT|\(V\)/.test(hay)) return 'current';
        return 'voltage';
    }
    W.ipsClassifyTab = ipsClassifyTab;

    // Per-bank readings of a battery group are not shown: only the Σ total
    // (table: no expandable bank rows; cards: no "tap for breakup").
    var IPS_SHOW_BANK_BREAKUP = false;

    function staleInfo(aObj) {
        var stale = fn('isPmReplayStale') ? W.isPmReplayStale(aObj) === true : false;
        var title = '';
        if (stale) {
            if (fn('buildPmStaleTitle')) title = W.buildPmStaleTitle(aObj);
            else if (fn('_wsStaleTitle')) title = W._wsStaleTitle(aObj).replace(/&#0?39;/g, "'").replace(/&amp;/g, '&');
            else title = 'Replay value (IsFresh = false)';
        }
        return { isStale: stale, staleTitle: title };
    }

    function buildRowsAll() {
        var rows = [];
        var LD = liveData();
        var ids = ipsAssetIds();
        for (var i = 0; i < ids.length; i++) {
            var aid = ids[i];
            var asset = LD[aid];
            var name = asset.AssetName || ('Asset ' + aid);
            var attrs = asset.attrs || {};
            var relays = asset.dlRelays || {};
            var ts = assetTs(asset);
            var keys = sortedAttrKeys(attrs);
            var relayKeys = Object.keys(relays);
            if (!keys.length && !relayKeys.length) {
                rows.push({ assetId: aid, assetName: name, attr: EMPTY, attrKey: '', value: EMPTY, ts: ts, dlClass: '',
                    isDataLogger: false, ipsTab: 'voltage', isStale: false, staleTitle: '' });
                continue;
            }
            var seenRelay = {};
            for (var k = 0; k < keys.length; k++) {
                var key = keys[k];
                var aObj = attrs[key] || {};
                var isDL = isDlAttr(asset, key, aObj);
                if (isDL) seenRelay[key] = true;
                var label = plainLabel(aid, key, aObj);
                var value = ipsFormatDisplayValue(aObj.Value, isDL ? '-' : EMPTY);
                var norm = String(value).trim().toLowerCase();
                var st = isDL ? { isStale: false, staleTitle: '' } : staleInfo(aObj);
                rows.push({
                    assetId: aid, assetName: name, attr: label, attrKey: key, value: value, ts: ts,
                    dlClass: norm === 'pickup' ? 'pickup' : (norm === 'drop' ? 'drop' : ''),
                    isDataLogger: isDL, ipsTab: ipsClassifyTab(isDL, key, label, name),
                    isStale: st.isStale, staleTitle: st.staleTitle
                });
            }
            for (var r = 0; r < relayKeys.length; r++) {
                var rk = relayKeys[r];
                if (seenRelay[rk]) continue;
                var relay = relays[rk] || {};
                var state = relay.isNull ? '-' : (relay.isPickup ? 'Pickup' : 'Drop');
                rows.push({
                    assetId: aid, assetName: name, attr: String(relay.displayName || rk), attrKey: rk, value: state, ts: ts,
                    dlClass: relay.isNull ? '' : (relay.isPickup ? 'pickup' : 'drop'),
                    isDataLogger: true, ipsTab: 'digital', isStale: false, staleTitle: ''
                });
            }
        }
        rows.sort(function (a, b) {
            if (a.isDataLogger !== b.isDataLogger) return a.isDataLogger ? -1 : 1;
            return natural(a.assetName, b.assetName) || natural(a.attr, b.attr);
        });
        return rows;
    }

    /* ---- I4  BATT grouping ----------------------------------------------- */
    var IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME = false;
    function ipsGroupCurrentRows(rows) {
        if (!rows || !rows.length) return rows;
        var groups = {}, out = [];
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var bucket = null;
            if (r && r.ipsTab === 'current' && !r.isDataLogger) {
                var up = String(r.assetName || '').toUpperCase();
                if (up.indexOf('DISCHARGING') !== -1) bucket = 'discharging';
                else if (up.indexOf('CHARGING') !== -1) bucket = 'charging';
                else bucket = ipsBattBucketOf(r.attr);   // banks kept as attributes of one asset
            }
            if (bucket === null) { out.push(r); continue; }
            var base = String(r.assetName || '').replace(/[\s\-_]*\d+\s*$/, '').trim() || String(r.assetName || '');
            var groupAttr = W.ipsBattBaseAttrName(r.attr);
            var key = bucket + '||' + groupAttr + '||' + base;
            var num = parseFloat(r.value);
            var g = groups[key];
            if (!g) {
                g = { _agg: true, assetId: 'ipsgrp::' + key, assetName: base,
                    attr: IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME ? r.attr : groupAttr, attrKey: groupAttr,
                    sum: isFinite(num) ? num : 0, count: isFinite(num) ? 1 : 0, ts: r.ts, members: [] };
                groups[key] = g;
                out.push(g);
            } else {
                if (isFinite(num)) { g.sum += num; g.count++; }
                if (String(r.ts).length === String(g.ts).length && String(r.ts) > String(g.ts)) g.ts = r.ts;
            }
            g.members.push(r);
        }
        for (var j = 0; j < out.length; j++) {
            var o = out[j];
            if (!o || !o._agg) continue;
            out[j] = {
                assetId: o.assetId, assetName: o.assetName, attr: o.attr, attrKey: o.attrKey,
                value: o.count > 0 ? ipsFormatDisplayValue(o.sum, EMPTY) : EMPTY, ts: o.ts, dlClass: '',
                isDataLogger: false, ipsTab: 'current', isStale: false, staleTitle: '', isGroup: true, members: o.members
            };
        }
        return out;
    }
    W.ipsGroupCurrentRows = ipsGroupCurrentRows;

    var expandedGroups = {};
    function ipsGroupIsExpanded(id) { return !!(id && expandedGroups[id]); }
    W.ipsGroupIsExpanded = ipsGroupIsExpanded;
    function ipsExpandRowsForDisplay(rows) {
        if (!rows || !rows.length) return rows || [];
        var flat = [];
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            flat.push(r);
            if (IPS_SHOW_BANK_BREAKUP && r && r.isGroup && r.members && r.members.length) {
                for (var m = 0; m < r.members.length; m++) {
                    var mr = r.members[m];
                    flat.push({
                        assetId: mr.assetId, assetName: mr.assetName, attr: mr.attr, attrKey: mr.attrKey,
                        value: mr.value, ts: mr.ts, dlClass: mr.dlClass || '', isDataLogger: false, ipsTab: 'current',
                        isStale: !!mr.isStale, staleTitle: mr.staleTitle || '', isChild: true, parentId: r.assetId
                    });
                }
            }
        }
        return flat;
    }
    W.ipsExpandRowsForDisplay = ipsExpandRowsForDisplay;

    /* ---- I6  active tab / export ----------------------------------------- */
    function tableMode() {
        if (fn('ipsTableMode')) return !!W.ipsTableMode();
        return $('#drpView').val() === 'Table';
    }
    function activeTab() {
        return (W.ipsActiveTab === 'current' || W.ipsActiveTab === 'digital') ? W.ipsActiveTab : 'voltage';
    }
    function tabLabel(t) { return t === 'current' ? 'IPS Current' : (t === 'digital' ? 'IPS Digital' : 'IPS Voltage'); }
    function ipsActiveTabLabel() { return tableMode() ? tabLabel(activeTab()) : ''; }
    function ipsViewSuffix() { var l = ipsActiveTabLabel(); return l ? '   |   View: ' + l : ''; }
    function ipsActiveTabSlug() { var l = ipsActiveTabLabel(); return l ? l.replace(/^IPS\s+/, '') + '_' : ''; }
    function ipsExportRowsForActiveTab() {
        var all = ipsGroupCurrentRows(buildRowsAll());
        if (!tableMode()) return all;
        var t = activeTab();
        return all.filter(function (r) { return r && (r.ipsTab || 'voltage') === t; });
    }
    W.ipsActiveTabLabel = ipsActiveTabLabel;
    W.ipsViewSuffix = ipsViewSuffix;
    W.ipsActiveTabSlug = ipsActiveTabSlug;
    W.ipsExportRowsForActiveTab = ipsExportRowsForActiveTab;
    W.buildIpsTableRowModels = function (opts) {
        return (opts && opts.all) ? buildRowsAll() : ipsExportRowsForActiveTab();
    };
    IPS616.buildRowsAll = buildRowsAll;

    /* ---- flat table ------------------------------------------------------ */
    function valueCellHtml(r) {
        var noData = (r.value === EMPTY);
        return '<td class="ips-val' + (noData ? ' no-data' : '') + (r.isStale ? ' ws-value-server-stale' : '') + '"' +
            (r.isStale ? ' title="' + esc(r.staleTitle) + '"' : '') + '>' +
            (r.dlClass ? '<span class="rdpms-dl-badge ' + r.dlClass + '">' + esc(r.value) + '</span>' : esc(r.value)) + '</td>';
    }
    function numberRows(rows) {
        var counters = { voltage: 0, current: 0, digital: 0 };
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var t = (r.ipsTab === 'current' || r.ipsTab === 'digital') ? r.ipsTab : 'voltage';
            r._tab = t;
            r._sno = r.isChild ? '' : String(++counters[t]);
        }
        return counters;
    }
    function displayRows() { return ipsExpandRowsForDisplay(ipsGroupCurrentRows(buildRowsAll())); }
    function assetCount() { return ipsAssetIds().length; }

    function ipsRenderTable() {
        if (fn('ensureIpsTableStyles')) W.ensureIpsTableStyles();
        ensureStyles();
        var $c = $('#divTelemetryLive');
        var rows = displayRows();
        if (!rows.length) {
            $c.html('<div class="ips-table-wrap"><div class="text-center text-muted p-5">' +
                '<i class="fas fa-satellite-dish fa-2x mb-3" style="color:#259dab;display:block;"></i>Waiting for live data…</div></div>');
            return;
        }
        var counts = numberRows(rows);
        var tab = activeTab();
        var n = assetCount();
        var h = '<div class="ips-table-wrap">';
        h += '<div class="ips-table-head"><h6><i class="fas fa-table" style="color:#259dab;"></i> IPS Live Grid</h6>' +
            '<span class="ips-count">' + n + ' Asset' + (n !== 1 ? 's' : '') + '</span></div>';
        h += '<div class="ips-tabbar" role="tablist">';
        [['voltage', 'fa-bolt'], ['current', 'fa-wave-square'], ['digital', 'fa-toggle-on']].forEach(function (t) {
            h += '<button type="button" class="ips-tab-btn" role="tab" data-ipstab="' + t[0] + '"><i class="fas ' + t[1] + '"></i> ' +
                tabLabel(t[0]) + ' <span class="ips-tab-count">' + counts[t[0]] + '</span></button>';
        });
        h += '</div>';
        h += '<div class="ips-table-scroll"><table id="ipsLiveTable" class="ips-live-table" data-export-label="' + esc(tabLabel(tab)) + '"><thead><tr>' +
            '<th class="ips-c-sno" style="width:64px;">S.No</th><th>Asset Name</th><th>Attribute</th>' +
            '<th class="ips-c-val" style="width:140px;">Live Value</th><th class="ips-c-ts" style="width:120px;">Timestamp</th>' +
            '</tr></thead><tbody>';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var cls = [];
            if (r._tab !== tab) cls.push('ips-row-hidden');
            var extra = '';
            var aname = esc(r.assetName);
            if (r.isChild) {
                cls.push('ips-row-child');
                if (!ipsGroupIsExpanded(r.parentId)) cls.push('ips-child-collapsed');
                extra = ' data-parent="' + esc(r.parentId) + '"';
            } else if (r.isGroup) {
                cls.push('ips-row-group');
                if (IPS_SHOW_BANK_BREAKUP && r.members && r.members.length) {
                    cls.push('ips-row-expandable');
                    if (ipsGroupIsExpanded(r.assetId)) cls.push('ips-group-open');
                    extra = ' data-ips-group="' + esc(r.assetId) + '" aria-expanded="' + ipsGroupIsExpanded(r.assetId) + '"';
                    aname = '<span class="ips-caret" aria-hidden="true"></span>' + aname +
                        ' <span class="ips-child-badge">' + r.members.length + '</span>';
                }
            }
            h += '<tr data-ips-id="' + esc(r.assetId) + '" data-attr="' + esc(r.attrKey) + '" data-ips-tab="' + r._tab + '"' + extra +
                (cls.length ? ' class="' + cls.join(' ') + '"' : '') + '>' +
                '<td class="ips-sno">' + r._sno + '</td>' +
                '<td class="ips-aname">' + aname + '</td>' +
                '<td class="ips-attr">' + esc(r.attr) + '</td>' +
                valueCellHtml(r) +
                '<td class="ips-ts">' + esc(r.ts) + '</td></tr>';
        }
        h += '</tbody></table></div>';
        ['voltage', 'current', 'digital'].forEach(function (t) {
            h += '<div class="ips-tab-empty" data-ips-tab="' + t + '" style="display:none;">No ' + tabLabel(t) + ' readings.</div>';
        });
        h += '</div>';
        $c.html(h);
        ipsApplyTab(tab);
        if (fn('syncDownloadVisibility')) W.syncDownloadVisibility(); else $('#downloadContainer').show();
    }

    // Plain table used by the download when no live table is on screen.
    function ipsBuildTableHtml(rows) {
        rows = rows || ipsExportRowsForActiveTab();
        if (!rows.length) return '';
        var label = ipsActiveTabLabel() || 'IPS Live Grid';
        var h = '<div class="ips-table-wrap"><div class="ips-table-scroll"><table class="ips-live-table" data-export-label="' + esc(label) + '"><thead><tr>' +
            '<th>S.No</th><th>Asset Name</th><th>Attribute</th><th>Live Value</th><th>Timestamp</th></tr></thead><tbody>';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            h += '<tr><td class="ips-sno">' + (i + 1) + '</td><td class="ips-aname">' + esc(r.assetName) + '</td>' +
                '<td class="ips-attr">' + esc(r.attr) + '</td><td class="ips-val">' + esc(r.value) + '</td>' +
                '<td class="ips-ts">' + esc(r.ts) + '</td></tr>';
        }
        return h + '</tbody></table></div></div>';
    }

    function ipsUpdateTable() {
        var table = document.getElementById('ipsLiveTable');
        if (!table || !table.tBodies.length) { ipsRenderTable(); return; }
        var rows = displayRows();
        var trs = table.tBodies[0].rows;
        if (trs.length !== rows.length) { ipsRenderTable(); return; }
        for (var c = 0; c < rows.length; c++) {
            if (trs[c].getAttribute('data-ips-id') !== String(rows[c].assetId) ||
                trs[c].getAttribute('data-attr') !== String(rows[c].attrKey)) { ipsRenderTable(); return; }
        }
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var $val = $(trs[i]).find('td.ips-val');
            if ($val.text() !== String(r.value)) {
                if (r.dlClass) $val.html('<span class="rdpms-dl-badge ' + r.dlClass + '">' + esc(r.value) + '</span>');
                else $val.text(r.value);
                if (r.value === EMPTY) $val.addClass('no-data');
                else {
                    $val.removeClass('no-data').addClass('ips-val-flash');
                    (function ($v) { setTimeout(function () { $v.removeClass('ips-val-flash'); }, 1200); })($val);
                }
            }
            var was = $val.hasClass('ws-value-server-stale');
            if (r.isStale && !was) $val.addClass('ws-value-server-stale');
            else if (!r.isStale && was) $val.removeClass('ws-value-server-stale');
            if (r.isStale) { if ($val.attr('title') !== r.staleTitle) $val.attr('title', r.staleTitle); }
            else if (was) $val.removeAttr('title');
            var $ts = $(trs[i]).find('td.ips-ts');
            if ($ts.text() !== String(r.ts)) $ts.text(r.ts);
        }
    }

    function ipsApplyTab(tab) {
        tab = (tab === 'current' || tab === 'digital') ? tab : 'voltage';
        W.ipsActiveTab = tab;
        var table = document.getElementById('ipsLiveTable');
        if (!table) return;
        var $wrap = $(table).closest('.ips-table-wrap');
        $wrap.find('.ips-tab-btn').each(function () {
            var on = $(this).attr('data-ipstab') === tab;
            $(this).toggleClass('active', on).attr('aria-selected', on ? 'true' : 'false');
        });
        var counts = { voltage: 0, current: 0, digital: 0 };
        $(table).find('tbody tr[data-ips-id]').each(function () {
            var t = $(this).attr('data-ips-tab') || 'voltage';
            counts[t] = (counts[t] || 0) + 1;
            $(this).toggleClass('ips-row-hidden', t !== tab);
        });
        ['voltage', 'current', 'digital'].forEach(function (t) {
            $wrap.find('.ips-tab-empty[data-ips-tab="' + t + '"]').css('display', (t === tab && !counts[t]) ? 'block' : 'none');
        });
        table.setAttribute('data-export-label', tabLabel(tab));
    }

    W.ipsRenderTable = ipsRenderTable;
    W.ipsBuildTableHtml = ipsBuildTableHtml;
    W.ipsUpdateTable = ipsUpdateTable;
    W.ipsApplyTab = ipsApplyTab;

    if (!W._ips616TabHandlerBound) {
        W._ips616TabHandlerBound = true;
        $(document).on('click.ips616', '.ips-tab-btn', function () { ipsApplyTab($(this).attr('data-ipstab')); });
        $(document).on('click.ips616', 'table.ips-live-table tr.ips-row-expandable', function () {
            var gid = $(this).attr('data-ips-group');
            if (!gid) return;
            var open = !ipsGroupIsExpanded(gid);
            if (open) expandedGroups[gid] = true; else delete expandedGroups[gid];
            $(this).toggleClass('ips-group-open', open).attr('aria-expanded', open ? 'true' : 'false');
            $(this).closest('table').find('tr.ips-row-child').each(function () {
                if ($(this).attr('data-parent') === gid) $(this).toggleClass('ips-child-collapsed', !open);
            });
        });
    }

    // Download scrape of the live table: skip rows of other tabs and the
    // per-bank breakdown (v616 exports the summed rows of the active tab).
    if (fn('tlCollectExportSections')) {
        var origCollect = W.tlCollectExportSections;
        W.tlCollectExportSections = function (scope) {
            var parked = [];
            try {
                ((scope && scope.tables) || []).forEach(function (tbl) {
                    if (!tbl.classList || !tbl.classList.contains('ips-live-table')) return;
                    tbl.querySelectorAll('tbody tr.ips-row-hidden, tbody tr.ips-row-child').forEach(function (tr) {
                        var mark = document.createComment('ips616');
                        tr.parentNode.replaceChild(mark, tr);
                        parked.push([mark, tr]);
                    });
                    var n = 0;
                    tbl.querySelectorAll('tbody tr td.ips-sno').forEach(function (td) { td.setAttribute('data-ips616-sno', td.textContent); td.textContent = String(++n); });
                });
                return origCollect.apply(this, arguments);
            } finally {
                parked.forEach(function (p) { if (p[0].parentNode) p[0].parentNode.replaceChild(p[1], p[0]); });
                ((scope && scope.tables) || []).forEach(function (tbl) {
                    if (!tbl.querySelectorAll) return;
                    tbl.querySelectorAll('td[data-ips616-sno]').forEach(function (td) {
                        td.textContent = td.getAttribute('data-ips616-sno');
                        td.removeAttribute('data-ips616-sno');
                    });
                });
            }
        };
    }

    /* ---- I2  card grid sums ---------------------------------------------- */
    var IPS_CARD_SUM_ONLY_CURRENT = true;
    var IPS_CARD_SUM_FALLBACK_ANY_NUMERIC = true;
    var IPS_CARD_SUM_MIN_MEMBERS = 2;

    function ipsCardBattBucket(asset, assetId) {
        if (!asset) return null;
        var up = String(asset.AssetName || '').toUpperCase();
        if (up.indexOf('DISCHARG') !== -1) return 'discharging';
        if (up.indexOf('CHARG') !== -1) return 'charging';
        var attrs = asset.attrs || {};
        var keys = Object.keys(attrs);
        for (var i = 0; i < keys.length; i++) {
            var l = (keys[i] + ' ' + plainLabel(assetId, keys[i], attrs[keys[i]])).toUpperCase();
            if (l.indexOf('DISCHAR') !== -1) return 'discharging';
            if (l.indexOf('CHAR') !== -1) return 'charging';
        }
        return null;
    }
    function ipsCardSumKey(label) {
        return String(label || '').toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    }
    function buildIpsCardSumModels(assetId) {
        var asset = liveData()[assetId];
        if (!asset || !ipsCardBattBucket(asset, assetId)) return [];
        var attrs = asset.attrs || {};
        var keys = sortedAttrKeys(attrs);
        function collect(currentOnly) {
            var groups = {}, order = [];
            for (var k = 0; k < keys.length; k++) {
                var an = keys[k];
                var aObj = attrs[an] || {};
                if (isDlAttr(asset, an, aObj)) continue;
                var label = plainLabel(assetId, an, aObj);
                if (currentOnly && ipsClassifyTab(false, an, label, asset.AssetName) !== 'current') continue;
                var num = parseFloat(ipsFormatDisplayValue(aObj.Value, EMPTY));
                var base = W.ipsBattBaseAttrName(label);
                var key = ipsCardSumKey(base);
                if (!key) continue;
                var g = groups[key];
                if (!g) { g = groups[key] = { key: key, label: base, sum: 0, count: 0, members: 0 }; order.push(g); }
                g.members++;
                if (isFinite(num)) { g.sum += num; g.count++; }
            }
            return order.filter(function (g) { return g.members >= IPS_CARD_SUM_MIN_MEMBERS; });
        }
        var models = collect(IPS_CARD_SUM_ONLY_CURRENT);
        if (!models.length && IPS_CARD_SUM_ONLY_CURRENT && IPS_CARD_SUM_FALLBACK_ANY_NUMERIC) models = collect(false);
        return models;
    }
    function ipsCardSumDisplay(m) {
        if (!m || m.count === 0) return EMPTY;
        return ipsFormatDisplayValue(m.sum, EMPTY);
    }
    function buildIpsCardSumRowsHtml(assetId) {
        var models = buildIpsCardSumModels(assetId);
        var h = '';
        for (var i = 0; i < models.length; i++) {
            var m = models[i];
            var disp = ipsCardSumDisplay(m);
            var title = 'Sum of ' + m.members + ' bank reading' + (m.members !== 1 ? 's' : '') + ' — ' + m.label;
            h += '<div class="ips-attr-row ips-sum-row" data-sum-key="' + esc(m.key) + '">' +
                '<span class="ips-attr-name" title="' + esc(title) + '"><span class="ips-sum-sigma">Σ</span>' + esc(m.label) +
                '<span class="ips-sum-count">' + m.members + '</span></span>' +
                '<span class="ips-attr-val ips-sum-val' + (disp === EMPTY ? ' no-data' : '') + '" data-sum="' + esc(m.key) + '">' + disp + '</span></div>';
        }
        return h;
    }
    function updateIpsCardSums($card, assetId) {
        var models = buildIpsCardSumModels(assetId);
        var $rows = $card.find('.ips-attr-row.ips-sum-row');
        if ($rows.length !== models.length) return false;
        for (var i = 0; i < models.length; i++) {
            var m = models[i];
            var $v = $card.find('.ips-sum-val').filter(function () { return this.getAttribute('data-sum') === m.key; });
            if (!$v.length) return false;
            var disp = ipsCardSumDisplay(m);
            if ($v.text() !== String(disp)) {
                $v.text(disp).toggleClass('no-data', disp === EMPTY);
                if (disp !== EMPTY) {
                    $v.addClass('ips-val-flash');
                    (function (el) { setTimeout(function () { el.removeClass('ips-val-flash'); }, 1200); })($v);
                }
            }
        }
        return true;
    }
    W.ipsCardBattBucket = ipsCardBattBucket;
    W.ipsCardSumKey = ipsCardSumKey;
    W.buildIpsCardSumModels = buildIpsCardSumModels;
    W.ipsCardSumDisplay = ipsCardSumDisplay;
    W.buildIpsCardSumRowsHtml = buildIpsCardSumRowsHtml;
    W.updateIpsCardSums = updateIpsCardSums;

    function staleMarks(assetId, an, rawVal, valCls) {
        var map = (W.wsStaleAttrs && W.wsStaleAttrs[assetId]) || {};
        if (map[an]) valCls += ' ws-stale-val';
        return {
            cls: valCls,
            tip: fn('_getValueTooltip') ? (W._getValueTooltip(an, parseFloat(rawVal), valCls) || '') : '',
            marks: fn('_getCellMarkers') ? W._getCellMarkers(valCls) : ''
        };
    }

    /* ---- IPS card v2 (617.2.3) -------------------------------------------
     * BATT CHARGING / DISCHARGING cards now read like every other IPS card:
     * one summary row (Σ of all banks) is shown, and a click on it opens the
     * per-bank breakup. The open/closed state is kept per card across live
     * updates and full re-renders. Styles are scoped under #ipsCardGrid so they
     * win over the legacy light-skin rules still inlined in Index.cshtml
     * (white footer, grey 55%-wide labels, 280px inner scroll).
     * --------------------------------------------------------------------- */
    var ipsBattOpen = W.__ipsBattOpen = W.__ipsBattOpen || {};   // "assetId|sumKey" -> true
    function battOpenKey(assetId, key) { return String(assetId) + '|' + key; }

    function ensureCardV2Styles() {
        if (document.getElementById('ips-card-v2-styles')) return;
        var G = '#ipsCardGrid ';
        var css =
            G + '.ips-asset-card.ipsv2{position:relative;display:flex;flex-direction:column;min-width:0;border-radius:14px;overflow:hidden;' +
            'background:linear-gradient(180deg,rgba(255,255,255,.06),rgba(255,255,255,.02));border:1px solid var(--at-edge,rgba(255,255,255,.10));' +
            'box-shadow:0 6px 18px rgba(0,0,0,.22);transition:border-color .18s ease,box-shadow .18s ease,transform .18s ease;}' +
            G + '.ips-asset-card.ipsv2:hover{border-color:rgba(34,211,238,.35);box-shadow:0 10px 26px rgba(0,0,0,.32);transform:translateY(-1px);}' +
            G + '.ips-asset-card.ipsv2::before{content:"";position:absolute;left:0;right:0;top:0;height:3px;background:var(--ipsv2-accent,#22d3ee);opacity:.9;}' +
            G + '.ips-asset-card.ipsv2.ipsv2-charging{--ipsv2-accent:#34d399;}' +
            G + '.ips-asset-card.ipsv2.ipsv2-discharging{--ipsv2-accent:#fbbf24;}' +
            G + '.ipsv2 .ips-card-head{display:flex;align-items:center;gap:8px;padding:11px 12px 8px;background:transparent;border-bottom:0;}' +
            G + '.ipsv2 .ips-card-head .ips-asset-name{flex:1 1 auto;min-width:0;color:var(--at-t1,#fff);font-size:12.5px;font-weight:800;letter-spacing:.03em;' +
            'text-transform:uppercase;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
            G + '.ipsv2 .ipsv2-title{flex:1 1 auto;min-width:0;display:flex;flex-direction:column;align-items:flex-start;gap:4px;}' +
            G + '.ipsv2 .ipsv2-title .ips-asset-name{max-width:100%;}' +
            G + '.ipsv2 .ipsv2-kind{flex:0 0 auto;font-size:9px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;padding:2px 7px;border-radius:999px;' +
            'color:var(--ipsv2-accent,#22d3ee);background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.10);}' +
            G + '.ipsv2 .ips-card-head .tl-asset-action{margin:0;flex:0 0 auto;}' +
            G + '.ipsv2 .ips-card-head .ips-status-dot{flex:0 0 auto;width:8px;height:8px;border-radius:50%;background:var(--at-ok,#34d399);box-shadow:0 0 8px var(--at-ok,#34d399);}' +
            // Index.cshtml caps .ips-attr-list at 300px with !important; a battery card must grow when its breakup is opened.
            G + '.ipsv2 .ips-attr-list{padding:2px 12px 8px;display:flex;flex-direction:column;gap:0;max-height:none!important;overflow:visible!important;min-width:0;}' +
            G + '.ipsv2 .ips-attr-row{display:flex;align-items:center;justify-content:space-between;gap:10px;padding:7px 0;min-width:0;border-bottom:1px solid rgba(255,255,255,.06);font-size:12px;}' +
            G + '.ipsv2 .ips-attr-row:last-child{border-bottom:0;}' +
            G + '.ipsv2 .ips-attr-row .ips-attr-name{flex:1 1 auto;min-width:0;max-width:none;color:var(--at-t2,rgba(255,255,255,.72));font-size:12px;font-weight:600;' +
            'line-height:1.3;white-space:normal;overflow-wrap:anywhere;}' +
            G + '.ipsv2 .ips-attr-row .ips-attr-name .alias-sub{font-size:88%;opacity:.95;letter-spacing:.01em;}' +
            G + '.ipsv2 .ips-attr-row .ips-attr-val{flex:0 0 auto;color:var(--at-brand,#22d3ee);font-family:"JetBrains Mono",var(--at-font-mono,monospace);' +
            'font-variant-numeric:tabular-nums;font-size:15px;font-weight:700;text-align:right;white-space:nowrap;max-width:none;padding:1px 4px;border-radius:5px;}' +
            G + '.ipsv2 .ips-attr-row .ips-attr-val.no-data{color:var(--at-t4,rgba(255,255,255,.34));font-weight:400;font-style:normal;}' +
            /* battery summary block */
            G + '.ipsv2 .ipsv2-sum.ips-attr-row{display:block;padding:0;gap:0;border-bottom:1px solid rgba(255,255,255,.08);}' +
            G + '.ipsv2 .ipsv2-sum{margin:6px 0 2px;border-radius:10px;background:rgba(255,255,255,.035);border:1px solid rgba(255,255,255,.08);overflow:hidden;}' +
            G + '.ipsv2 .ipsv2-sum-head{all:unset;box-sizing:border-box;width:100%;display:flex;align-items:center;gap:8px;padding:9px 10px;cursor:pointer;}' +
            G + '.ipsv2 .ipsv2-sum-head:hover{background:rgba(34,211,238,.07);}' +
            G + '.ipsv2 .ipsv2-sum-only .ipsv2-sum-head{cursor:default;}' +
            G + '.ipsv2 .ipsv2-sum-only .ipsv2-sum-head:hover{background:none;}' +
            G + '.ipsv2 .ipsv2-sum-head:focus-visible{outline:2px solid var(--at-brand,#22d3ee);outline-offset:-2px;}' +
            G + '.ipsv2 .ipsv2-chev{flex:0 0 auto;width:16px;height:16px;display:inline-flex;align-items:center;justify-content:center;color:var(--ipsv2-accent,#22d3ee);transition:transform .18s ease;}' +
            G + '.ipsv2 .ipsv2-sum.open .ipsv2-chev{transform:rotate(90deg);}' +
            G + '.ipsv2 .ipsv2-sum-label{flex:1 1 auto;min-width:0;display:flex;flex-direction:column;gap:2px;}' +
            G + '.ipsv2 .ipsv2-sum .ipsv2-sum-label b{font-size:12px;font-weight:600;color:var(--at-t1,#fff);overflow-wrap:anywhere;line-height:1.3;}' +
            G + '.ipsv2 .ipsv2-sum .ipsv2-sum-label b .alias-sub{font-size:88%;}' +
            G + '.ipsv2 .ipsv2-sum-label small{font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}' +
            G + '.ipsv2 .ipsv2-sum .ipsv2-sum-val.ips-attr-val{flex:0 0 auto;font-family:"JetBrains Mono",var(--at-font-mono,monospace);font-variant-numeric:tabular-nums;font-size:16px;font-weight:800;' +
            'color:var(--ipsv2-accent,#22d3ee);padding:1px 4px;border-radius:5px;}' +
            G + '.ipsv2 .ipsv2-sum .ipsv2-sum-val.ips-attr-val.no-data{color:var(--at-t4,rgba(255,255,255,.34));font-weight:400;}' +
            G + '.ipsv2 .ipsv2-bank-no{font-weight:700;color:var(--at-t1,#fff);margin-right:6px;}' +
            G + '.ipsv2 .ipsv2-bank-full{font-size:10px;color:var(--at-t4,rgba(255,255,255,.4));}' +
            G + '.ipsv2 .ipsv2-banks{display:none;padding:2px 10px 8px 12px;border-top:1px dashed rgba(255,255,255,.10);}' +
            G + '.ipsv2 .ipsv2-sum.open .ipsv2-banks{display:block;animation:ipsv2Open .18s ease-out;}' +
            '@keyframes ipsv2Open{from{opacity:0;transform:translateY(-3px);}to{opacity:1;transform:none;}}' +
            G + '.ipsv2 .ipsv2-banks .ips-attr-row{position:relative;padding:6px 0 8px;}' +
            G + '.ipsv2 .ipsv2-banks .ips-attr-row .ips-attr-name{font-weight:500;color:var(--at-t3,rgba(255,255,255,.6));font-size:11.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
            G + '.ipsv2 .ipsv2-banks .ips-attr-row .ips-attr-val{font-size:13px;color:var(--at-t1,#fff);}' +
            G + '.ipsv2 .ipsv2-bar{position:absolute;left:0;right:0;bottom:2px;height:3px;border-radius:3px;background:rgba(255,255,255,.06);overflow:hidden;}' +
            G + '.ipsv2 .ipsv2-bar i{display:block;height:100%;width:0;border-radius:3px;background:var(--ipsv2-accent,#22d3ee);opacity:.75;transition:width .35s ease;}' +
            /* footer */
            G + '.ipsv2 .ips-card-footer{margin-top:auto;display:flex;align-items:center;gap:6px;padding:7px 12px;background:rgba(0,0,0,.22);' +
            'border-top:1px solid rgba(255,255,255,.06);color:var(--at-t3,rgba(255,255,255,.55));font-size:10.5px;font-family:"JetBrains Mono",var(--at-font-mono,monospace);}' +
            G + '.ipsv2 .ips-card-footer i{color:var(--ipsv2-accent,#22d3ee);opacity:.9;}' +
            /* grid header */
            '#divTelemetryLive .ips-grid-header h6{color:var(--at-t1,#fff);}' +
            '#divTelemetryLive .ips-grid-header h6 i{color:var(--at-brand,#22d3ee)!important;}' +
            '.ips-grid-header .ipsv2-bulk{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:6px;margin-left:auto;margin-right:8px;padding:4px 11px;border-radius:999px;' +
            'font-size:11.5px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));background:rgba(255,255,255,.05);border:1px solid rgba(255,255,255,.12);}' +
            '.ips-grid-header .ipsv2-bulk:hover{color:var(--at-brand,#22d3ee);border-color:rgba(34,211,238,.4);}' +
            '.ips-grid-header .ipsv2-bulk:focus-visible{outline:2px solid var(--at-brand,#22d3ee);}' +
            /* light theme */
            'body[data-aurora="light"] ' + G + '.ips-asset-card.ipsv2{background:#fff!important;border-color:rgba(15,23,42,.10)!important;box-shadow:0 2px 12px rgba(15,23,42,.08)!important;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ips-card-head .ips-asset-name,body[data-aurora="light"] ' + G + '.ipsv2 .ipsv2-sum-label b{color:#0b1530;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ips-attr-row .ips-attr-name{color:#475569;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ips-attr-row{border-bottom-color:rgba(15,23,42,.07);}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ips-attr-row .ips-attr-val{color:#0e7490;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ipsv2-banks .ips-attr-row .ips-attr-val{color:#0b1530;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ipsv2-sum .ipsv2-sum-val.ips-attr-val{color:var(--ipsv2-accent,#0e7490);}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ipsv2-bank-no{color:#0b1530;}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ipsv2-sum{background:rgba(15,23,42,.03);border-color:rgba(15,23,42,.08);}' +
            'body[data-aurora="light"] ' + G + '.ipsv2 .ips-card-footer{background:rgba(15,23,42,.04)!important;color:#64748b!important;border-top-color:rgba(15,23,42,.06)!important;}' +
            'body[data-aurora="light"] ' + G + '.ips-asset-card.ipsv2.ipsv2-charging{--ipsv2-accent:#059669;}' +
            'body[data-aurora="light"] ' + G + '.ips-asset-card.ipsv2.ipsv2-discharging{--ipsv2-accent:#d97706;}' +
            '@media (prefers-reduced-motion:reduce){' + G + '.ipsv2 .ipsv2-sum.open .ipsv2-banks{animation:none;}' + G + '.ips-asset-card.ipsv2:hover{transform:none;}}';
        var st = document.createElement('style');
        st.id = 'ips-card-v2-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }
    W.ensureIpsCardV2Styles = ensureCardV2Styles;

    // Which attribute keys belong to which Σ group on this card.
    function sumMembership(assetId, models) {
        var asset = liveData()[assetId] || {};
        var attrs = asset.attrs || {};
        var byKey = {}, member = {};
        models.forEach(function (m) { byKey[m.key] = []; });
        sortedAttrKeys(attrs).forEach(function (an) {
            var aObj = attrs[an] || {};
            if (isDlAttr(asset, an, aObj)) return;
            var label = plainLabel(assetId, an, aObj);
            var key = ipsCardSumKey(W.ipsBattBaseAttrName(label));
            if (byKey[key]) { byKey[key].push(an); member[an] = key; }
        });
        return { byKey: byKey, member: member };
    }

    function bankShortLabel(label) {
        var m = String(label || '').match(/CHAR[A-Z]*\s*-?\s*(\d+)/i) || String(label || '').match(/-(\d+)\b/);
        return m ? 'Bank ' + m[1] : null;
    }
    function attrRowHtml(assetId, an, aObj, extraCls) {
        var disp = ipsFormatDisplayValue(aObj.Value, EMPTY);
        var label = plainLabel(assetId, an, aObj);
        var labelHtml = fn('formatAliasName') ? (W.formatAliasName(label) || esc(label)) : esc(label);
        if (extraCls === 'ipsv2-bank') {
            var short = bankShortLabel(label);
            if (short) labelHtml = '<b class="ipsv2-bank-no">' + esc(short) + '</b><span class="ipsv2-bank-full">' + esc(label) + '</span>';
        }
        var s = staleMarks(assetId, an, aObj.Value, disp === EMPTY ? 'ips-attr-val no-data' : 'ips-attr-val');
        return '<div class="ips-attr-row' + (extraCls ? ' ' + extraCls : '') + '">' +
            '<span class="ips-attr-name" title="' + esc(label) + '">' + labelHtml + '</span>' +
            '<span class="' + s.cls + '" data-attr="' + esc(an) + '"' + (s.tip ? ' title="' + esc(s.tip) + '"' : '') + '>' +
            esc(disp) + s.marks + '</span>' +
            (extraCls === 'ipsv2-bank' ? '<span class="ipsv2-bar" aria-hidden="true"><i></i></span>' : '') +
            '</div>';
    }

    function sumBlockHtml(assetId, m, memberKeys) {
        var attrs = (liveData()[assetId] || {}).attrs || {};
        var disp = ipsCardSumDisplay(m);
        if (!IPS_SHOW_BANK_BREAKUP) {
            // Σ total only. The bank values stay in the DOM, hidden, so the
            // incremental updater still finds one value cell per attribute.
            var hidden = '<div class="ipsv2-banks" hidden>';
            memberKeys.forEach(function (an) { hidden += attrRowHtml(assetId, an, attrs[an] || {}, 'ipsv2-bank'); });
            hidden += '</div>';
            return '<div class="ipsv2-sum ipsv2-sum-only ips-attr-row ips-sum-row" data-sum-key="' + esc(m.key) + '">' +
                '<div class="ipsv2-sum-head">' +
                '<span class="ipsv2-sum-label ips-attr-name"><b title="Total of ' + m.members + ' banks — ' + esc(m.label) + '">' +
                (fn('formatAliasName') ? (W.formatAliasName(m.label) || esc(m.label)) : esc(m.label)) + '</b></span>' +
                '<span class="ipsv2-sum-val ips-attr-val ips-sum-val' + (disp === EMPTY ? ' no-data' : '') + '" data-sum="' + esc(m.key) + '">' + esc(disp) + '</span>' +
                '</div>' + hidden + '</div>';
        }
        var open = !!ipsBattOpen[battOpenKey(assetId, m.key)];
        var bankId = 'ipsv2b_' + String(assetId).replace(/\W/g, '_') + '_' + m.key;
        var h = '<div class="ipsv2-sum ips-attr-row ips-sum-row' + (open ? ' open' : '') + '" data-sum-key="' + esc(m.key) + '">' +
            '<button type="button" class="ipsv2-sum-head" data-ipsv2-toggle="' + esc(m.key) + '" aria-expanded="' + open + '" aria-controls="' + bankId + '"' +
            ' title="' + (open ? 'Hide' : 'Show') + ' the ' + m.members + ' bank readings">' +
            '<span class="ipsv2-chev" aria-hidden="true"><i class="fas fa-chevron-right"></i></span>' +
            '<span class="ipsv2-sum-label ips-attr-name"><b title="Sum of ' + m.members + ' banks \u2014 ' + esc(m.label) + '">' +
            (fn('formatAliasName') ? (W.formatAliasName(m.label) || esc(m.label)) : esc(m.label)) + '</b>' +
            '<small data-ipsv2-hint>\u03A3 ' + m.members + ' banks \u00b7 ' + (open ? 'tap to hide' : 'tap for breakup') + '</small></span>' +
            '<span class="ipsv2-sum-val ips-attr-val ips-sum-val' + (disp === EMPTY ? ' no-data' : '') + '" data-sum="' + esc(m.key) + '">' + esc(disp) + '</span>' +
            '</button>' +
            '<div class="ipsv2-banks" id="' + bankId + '">';
        memberKeys.forEach(function (an) { h += attrRowHtml(assetId, an, attrs[an] || {}, 'ipsv2-bank'); });
        h += '</div></div>';
        return h;
    }

    function paintBars($card, assetId) {
        var attrs = (liveData()[assetId] || {}).attrs || {};
        $card.find('.ipsv2-sum').each(function () {
            var $rows = $(this).find('.ipsv2-bank');
            var vals = [], max = 0;
            $rows.each(function () {
                var an = $(this).find('.ips-attr-val[data-attr]').attr('data-attr');
                var n = parseFloat(ipsFormatDisplayValue((attrs[an] || {}).Value, EMPTY));
                n = isFinite(n) ? n : 0;
                vals.push(n); if (n > max) max = n;
            });
            $rows.each(function (i) {
                $(this).find('.ipsv2-bar > i').css('width', (max > 0 ? Math.round(vals[i] * 100 / max) : 0) + '%');
            });
        });
    }

    W.buildIpsCard = function (assetId) {
        var asset = liveData()[assetId];
        if (!asset) return '';
        ensureStyles();
        ensureCardV2Styles();
        var name = asset.AssetName || ('Asset ' + assetId);
        var attrs = asset.attrs || {};
        var keys = sortedAttrKeys(attrs);
        var ts = assetTs(asset);
        var bucket = ipsCardBattBucket(asset, assetId);
        var models = keys.length ? buildIpsCardSumModels(assetId) : [];
        var ms = sumMembership(assetId, models);
        var kindCls = bucket ? ' ipsv2-' + bucket : '';
        var kindTxt = bucket ? (bucket === 'charging' ? 'Charging' : 'Discharging') : '';

        var h = '<div class="ips-asset-card ipsv2' + kindCls + '" data-ips-id="' + esc(assetId) + '">';
        h += '<div class="ips-card-head"><div class="ipsv2-title"><span class="ips-asset-name" title="' + esc(name) + '">' + esc(name) + '</span>' +
            (kindTxt ? '<span class="ipsv2-kind">' + kindTxt + (IPS_SHOW_BANK_BREAKUP && models.length ? ' \u00b7 ' + models[0].members + ' banks' : '') + '</span>' : '') + '</div>' +
            '<button type="button" class="tl-asset-action tl-aa-avg" title="Avg values" onclick="fnShowFRSAttributeRangeHistory(\'' + esc(assetId) + '\')">' +
            '<i class="fa-solid fa-chart-column"></i></button><span class="ips-status-dot" title="Live"></span></div>';
        h += '<div class="ips-attr-list">';
        if (!keys.length) {
            h += '<div class="ips-attr-row"><span class="ips-attr-name" style="color:var(--at-t4,rgba(255,255,255,0.34));font-style:italic;">Waiting for data…</span></div>';
        } else {
            // plain attributes first (anything that is not a bank of a Σ group)
            for (var k = 0; k < keys.length; k++) {
                if (ms.member[keys[k]]) continue;
                h += attrRowHtml(assetId, keys[k], attrs[keys[k]] || {}, '');
            }
            // one collapsible summary per battery group
            for (var i = 0; i < models.length; i++) {
                h += sumBlockHtml(assetId, models[i], ms.byKey[models[i].key] || []);
            }
        }
        h += '</div>';
        h += '<div class="ips-card-footer"><i class="fas fa-clock"></i> ' + esc(ts) + '</div></div>';
        return h;
    };
    // Kept for callers of the 616 API: the Σ rows are now part of buildIpsCard.
    W.buildIpsCardSumRowsHtml = function (assetId) {
        var models = buildIpsCardSumModels(assetId);
        var ms = sumMembership(assetId, models);
        return models.map(function (m) { return sumBlockHtml(assetId, m, ms.byKey[m.key] || []); }).join('');
    };

    function setBattOpen($block, open) {
        var $card = $block.closest('.ips-asset-card');
        var aid = $card.attr('data-ips-id');
        var key = $block.attr('data-sum-key');
        if (open) ipsBattOpen[battOpenKey(aid, key)] = true; else delete ipsBattOpen[battOpenKey(aid, key)];
        $block.toggleClass('open', open);
        var $btn = $block.find('> .ipsv2-sum-head');
        $btn.attr('aria-expanded', open ? 'true' : 'false').attr('title', (open ? 'Hide' : 'Show') + ' the bank readings');
        $block.find('[data-ipsv2-hint]').text('\u03A3 ' + $block.find('.ipsv2-bank').length + ' banks \u00b7 ' + (open ? 'tap to hide' : 'tap for breakup'));
        if (open) paintBars($card, aid);
    }
    function syncBulkBtn() {
        var $blocks = $('#ipsCardGrid .ipsv2-sum').not('.ipsv2-sum-only');
        var $btn = $('.ips-grid-header .ipsv2-bulk');
        if (!$blocks.length) { $btn.remove(); return; }
        if (!$btn.length) {
            $btn = $('<button type="button" class="ipsv2-bulk"></button>');
            var $badge = $('.ips-grid-header .ips-grid-badge');
            if ($badge.length) $badge.before($btn); else $('.ips-grid-header').append($btn);
        }
        var allOpen = $blocks.filter('.open').length === $blocks.length;
        $btn.attr('data-open', allOpen ? '1' : '0')
            .html('<i class="fas ' + (allOpen ? 'fa-compress-alt' : 'fa-expand-alt') + '"></i> ' + (allOpen ? 'Hide bank breakup' : 'Show all bank breakup'));
    }
    W.ipsv2SyncBulkButton = syncBulkBtn;

    if (!W._ipsv2HandlersBound) {
        W._ipsv2HandlersBound = true;
        $(document).on('click.ipsv2', '#ipsCardGrid [data-ipsv2-toggle]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $block = $(this).closest('.ipsv2-sum');
            setBattOpen($block, !$block.hasClass('open'));
            syncBulkBtn();
        });
        $(document).on('click.ipsv2', '.ips-grid-header .ipsv2-bulk', function (e) {
            e.preventDefault();
            var open = $(this).attr('data-open') !== '1';
            $('#ipsCardGrid .ipsv2-sum').each(function () { setBattOpen($(this), open); });
            syncBulkBtn();
        });
    }

    // After every full grid render: bars + header button.
    if (fn('renderIpsGridView') && !W.renderIpsGridView.__ipsv2) {
        var _origRenderGrid = W.renderIpsGridView;
        W.renderIpsGridView = function () {
            var r = _origRenderGrid.apply(this, arguments);
            try {
                $('#ipsCardGrid .ips-asset-card.ipsv2').each(function () { paintBars($(this), $(this).attr('data-ips-id')); });
                syncBulkBtn();
            } catch (e) { if (W.console) console.warn('[IPS v2] post-render', e); }
            return r;
        };
        W.renderIpsGridView.__ipsv2 = true;
    }

    W.updateIpsGridIncremental = function (updatedAssetIds) {
        var $grid = $('#ipsCardGrid');
        if (!$grid.length) { if (fn('renderIpsGridView')) W.renderIpsGridView(); return; }
        var ids = (updatedAssetIds && updatedAssetIds.length) ? updatedAssetIds : Object.keys(liveData());
        var LD = liveData();
        for (var i = 0; i < ids.length; i++) {
            var aid = String(ids[i]);
            if (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(aid)) continue;
            if (fn('assetMatchesSelectedType') && !W.assetMatchesSelectedType(aid)) continue;
            var asset = LD[aid];
            if (!asset) continue;
            var $card = $grid.find('.ips-asset-card').filter(function () { return this.getAttribute('data-ips-id') === aid; });
            if (!$card.length) {
                $grid.append(W.buildIpsCard(aid));
                var total = $grid.find('.ips-asset-card').length;
                $('#divTelemetryLive .ips-grid-badge').text(total + ' Asset' + (total !== 1 ? 's' : ''));
                continue;
            }
            var attrs = asset.attrs || {};
            var rendered = $card.find('.ips-attr-val[data-attr]').length;
            if (!$card.hasClass('ipsv2') || rendered !== Object.keys(attrs).length || !updateIpsCardSums($card, aid)) {
                var $new = $(W.buildIpsCard(aid));
                $card.replaceWith($new);
                paintBars($new, aid);
                continue;
            }
            $card.find('.ips-attr-val[data-attr]').each(function () {
                var $v = $(this);
                var an = $v.attr('data-attr');
                var aObj = attrs[an] || {};
                var disp = ipsFormatDisplayValue(aObj.Value, EMPTY);
                var s = staleMarks(aid, an, aObj.Value, disp === EMPTY ? 'ips-attr-val no-data' : 'ips-attr-val');
                var old = $v.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
                if (old !== String(disp)) {
                    $v.html(esc(disp) + s.marks).attr('class', s.cls).attr('title', s.tip);
                    if (disp !== EMPTY) {
                        $v.addClass('ips-val-flash');
                        (function (el) { setTimeout(function () { el.removeClass('ips-val-flash'); }, 1200); })($v);
                    }
                } else {
                    $v.attr('class', s.cls).attr('title', s.tip);
                    $v.find('.ws-stale-marker,.ws-warn-marker').remove();
                    if (s.marks) $v.append(s.marks);
                }
            });
            paintBars($card, aid);
            $card.find('.ips-card-footer').html('<i class="fas fa-clock"></i> ' + esc(assetTs(asset)));
            $card.addClass('ips-card-flash');
            setTimeout(function (c) { return function () { c.removeClass('ips-card-flash'); }; }($card), 1000);
        }
        syncBulkBtn();
    };

    IPS616.version = '616-port-2 (card v2)';
    console.log('[IPS616] IPS v616 features installed');
})();
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-ips616" failed to load', e);
}


/* #############################################################################
 * SECTION: SHARED: WebSocket URL, info bar, export, asset types (v616 port)
 * (was telemetrylive-infra616.js)
 * ########################################################################## */
try {
/* =============================================================================
 * telemetrylive-infra616.js — shared v616 features ported to v617 (Aurora)
 *
 * Load order: a classic <script> AFTER telemetrylive.js,
 * telemetrylive-graphs.js and telemetrylive-pmvibration.js. It does not edit
 * those files. It replaces or wraps their global functions. The callers look
 * these functions up by name when they run (bare top-level name or window.X),
 * so a reassigned window.X takes effect. Order relative to
 * telemetrylive-pm616.js / -signal616.js / -ips616.js does not matter: every
 * wrapper keeps whatever function it finds and calls it.
 *
 * v616 line numbers refer to v616 TelemetryLive/telemetrylive.js.
 *
 *  F1  WebSocket URL: always /{site}/{type}/all, filter assets client-side
 *      (buildWebSocketUrl, normalizeWsSelectedAssetIds) ...... v616 2606-2650, 4639-4659
 *  F2  Selection info bar: Zone / Division / Station / Type / Asset chips,
 *      "+N more" popup, live clock, Zone/Division filled in from
 *      GetSiteById (updateSelInfoBar) ........................ v616 33760-34445,
 *                                                               v616 Index.cshtml 143-201
 *  F3  Export button visibility per view (isDownloadableView,
 *      syncDownloadVisibility). v617 also exports from card views, so only
 *      views without an export source hide it: Graph and the per-asset
 *      Graph/Circuit overlay ................................. v616 2583-2600, 24672-24677, 30475-30481
 *  F4  Equipment Room / Device Time never selectable
 *      (isHiddenAssetTypeName) ............................... v616 19747-19762, 19795-19803
 *  F5  Excel / CSV: S.No / Zone / Division / Station columns, merged
 *      multi-row headers, Point Machine vibration section/sheet
 *      (fnDownloadExcel, fnDownloadCSV) ...................... v616 20618-22130
 *  F6  DataLogger details popup: signal rows without DataLogger relays,
 *      in signal-group order (_detailKey); PM values limited to the latest
 *      operation when telemetrylive-pm616.js is not loaded ..... v616 23834-23875, 24035-24080,
 *                                                               v616 25448-25467
 *  F7  Derived values: find the operands through this asset's own
 *      metadata before the fixed-ID fallback (calculateDerivedValues
 *      wrapper) .............................................. v616 2933-3120
 *      The fixed fallback IDs are NOT changed (v616: 344/569/1/249/570/3/571,
 *      v617: 1/2/5/4). Which set is right has to be checked against the
 *      database, so v617's IDs stay as they are.
 *  F8  Styles: .ws-value-server-stale, .pm-conflict-flag, selection bar
 *      (Aurora --at-* tokens) ................................ v616 34996-35010, telemetrylive.css 3523
 *  F9  Load profiler (off by default; turn on with ?lp=1 or
 *      localStorage tlLoadProfile=1; lpReport() in the console) v616 1411-1482
 * ========================================================================== */
(function () {
    'use strict';

    if (window.__infra616Applied) return;
    window.__infra616Applied = true;

    var W = window;
    var $ = W.jQuery;
    if (typeof $ !== 'function') {
        if (W.console) console.warn('[infra616] jQuery missing; nothing applied');
        return;
    }

    function fn(name) { return typeof W[name] === 'function' ? W[name] : null; }
    function str(v) { return (v === undefined || v === null) ? '' : String(v); }
    function trim(v) { return $.trim(str(v)); }
    function esc(s) {
        return str(s).replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#039;');
    }
    function liveData() { return W.wsLiveData || {}; }
    var INFRA = W.INFRA616 = W.INFRA616 || {};

    /* ======================================================================
     * F8 — styles (injected once)
     * ==================================================================== */
    function injectStyles() {
        if (document.getElementById('infra616-styles')) return;
        var css = [
            /* Server-stale values (IsFresh=false) */
            '.ws-value-server-stale{color:var(--at-t3,rgba(255,255,255,.5)) !important;font-weight:500;}',
            '.ws-value-server-stale::before{content:"!";display:inline-flex;align-items:center;justify-content:center;' +
            'width:14px;height:14px;margin-right:4px;border-radius:50%;background:rgba(148,163,184,.32);' +
            'color:var(--at-t1,#fff);font-size:10px;font-weight:700;line-height:14px;vertical-align:middle;' +
            'font-family:var(--at-font-body,system-ui,sans-serif);}',
            /* PM operation / position disagreement */
            '.pm-conflict-flag{color:var(--at-warn,#fbbf24);font-size:10px;font-weight:700;margin-left:4px;white-space:nowrap;}',
            /* Selection info bar */
            '#selInfoBar{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:8px 0 0;padding:6px 10px;' +
            'background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.1));' +
            'border-radius:var(--at-r-md,12px);font:12px/1.4 var(--at-font-body,system-ui,sans-serif);' +
            'color:var(--at-t2,rgba(255,255,255,.72));min-height:34px;box-sizing:border-box;}',
            '#selInfoBar .sib-chip{display:inline-flex;align-items:center;gap:5px;padding:3px 10px;white-space:nowrap;' +
            'background:var(--at-g2,rgba(255,255,255,.06));border:1px solid var(--at-edge,rgba(255,255,255,.1));' +
            'border-radius:999px;max-width:100%;}',
            '#selInfoBar .sib-label{font-size:10px;font-weight:600;letter-spacing:.05em;text-transform:uppercase;' +
            'color:var(--at-t3,rgba(255,255,255,.5));}',
            '#selInfoBar .sib-val{font-weight:700;color:var(--at-t1,rgba(255,255,255,.96));overflow:hidden;text-overflow:ellipsis;max-width:220px;}',
            '#selInfoBar .sib-val.sib-all{font-weight:500;color:var(--at-t4,rgba(255,255,255,.34));}',
            '#selInfoBar .sib-time .sib-val{font-family:var(--at-font-mono,ui-monospace,monospace);font-weight:600;}',
            '#selInfoBar .sib-time .fa-clock{margin-right:3px;color:var(--at-brand,#22d3ee);}',
            '#selInfoBar .sib-more-badge{cursor:pointer;border:0;padding:1px 8px;border-radius:999px;font:inherit;' +
            'font-size:10px;font-weight:700;color:var(--at-brand,#22d3ee);background:rgba(34,211,238,.14);}',
            '#selInfoBar .sib-more-badge:hover,#selInfoBar .sib-more-badge:focus-visible{background:rgba(34,211,238,.26);outline:none;}',
            '#sibAssetsPopup{display:none;position:fixed;z-index:10050;min-width:220px;max-width:340px;max-height:320px;overflow:auto;' +
            'padding:10px 12px;background:var(--at-bg2,#0e1530);color:var(--at-t1,rgba(255,255,255,.96));' +
            'border:1px solid var(--at-edge-s,rgba(255,255,255,.18));border-radius:var(--at-r-md,12px);' +
            'box-shadow:var(--at-shadow-up,0 24px 48px -12px rgba(0,0,0,.5));font:12px/1.4 var(--at-font-body,system-ui,sans-serif);}',
            '#sibAssetsPopup .sib-popup-title{display:flex;justify-content:space-between;align-items:center;gap:8px;margin-bottom:6px;' +
            'font-weight:700;font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:var(--at-t2,rgba(255,255,255,.72));}',
            '#sibAssetsPopup .sib-close-btn{cursor:pointer;border:0;background:transparent;color:var(--at-t3,rgba(255,255,255,.5));font-size:13px;line-height:1;padding:2px 4px;}',
            '#sibAssetsPopup .sib-close-btn:hover{color:var(--at-t1,#fff);}',
            '#sibAssetsPopup .sib-context{font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));margin-bottom:8px;line-height:1.6;}',
            '#sibAssetsPopup .sib-context strong{color:var(--at-t2,rgba(255,255,255,.72));}',
            '#sibAssetsPopup .sib-asset-item{display:flex;align-items:center;gap:7px;padding:3px 6px;border-radius:6px;}',
            '#sibAssetsPopup .sib-asset-item:hover{background:var(--at-g2,rgba(255,255,255,.06));}',
            '#sibAssetsPopup .sib-asset-item::before{content:"";width:6px;height:6px;border-radius:50%;flex-shrink:0;background:var(--at-brand,#22d3ee);}',
            'body[data-aurora="light"] #sibAssetsPopup{background:#ffffff;}'
        ].join('\n');
        var st = document.createElement('style');
        st.id = 'infra616-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }

    /* ======================================================================
     * F1 — WebSocket URL: always the whole site + asset type stream
     * ==================================================================== */
    function normalizeWsSelectedAssetIds(assetIds) {
        if (!Array.isArray(assetIds)) return [];
        var out = [], seen = {};
        for (var i = 0; i < assetIds.length; i++) {
            var id = trim(assetIds[i]);
            if (!id || id === '0' || seen[id]) continue;
            seen[id] = true;
            out.push(id);
        }
        return out;
    }
    W.normalizeWsSelectedAssetIds = normalizeWsSelectedAssetIds;

    // The per-asset endpoint (/subscribe/liveValue/{assetId}) may leave out
    // the replay/snapshot payload (BroadcastKind=replay), so it is never used.
    // Asset selection is applied client-side through wsCurrentFilterAssetIds.
    function buildWebSocketUrl616(siteId, assetTypeId /*, assetIds (ignored) */) {
        var base = (typeof W.WS_BASE_URL !== 'undefined') ? W.WS_BASE_URL
            : ((typeof WS_BASE_URL !== 'undefined') ? WS_BASE_URL : ''); // eslint-disable-line no-undef
        siteId = trim(siteId);
        assetTypeId = trim(assetTypeId);
        if (siteId && siteId !== '0' && assetTypeId && assetTypeId !== '0') {
            return base + '/' + encodeURIComponent(siteId) + '/' + encodeURIComponent(assetTypeId) + '/all';
        }
        if (siteId && siteId !== '0') return base + '/' + encodeURIComponent(siteId) + '/all';
        return base + '/all';
    }
    W.buildWebSocketUrl = buildWebSocketUrl616;

    var _origConnect = fn('connectWebSocket');
    if (_origConnect) {
        W.connectWebSocket = function (siteId, assetTypeId, assetIds) {
            // Store IDs as strings: the client-side filters compare with
            // String(message.AssetId).
            return _origConnect.call(this, siteId, assetTypeId, normalizeWsSelectedAssetIds(assetIds));
        };
    }

    /* ======================================================================
     * F3 — Export button visibility
     * ==================================================================== */
    var EXPORT_VIEWS = ['Table', 'Cards', 'RDPMS', 'IPS', 'PointMachine'];

    function isDownloadableView() {
        try {
            if (W._tlOverlayActive) return false;               // per-asset Graph/Circuit overlay
            if ($('#tlBackBar').length && $('#tlBackBar').css('display') !== 'none') return false;
            var v = $('#drpView').val() || 'Table';
            return EXPORT_VIEWS.indexOf(v) !== -1;
        } catch (e) { return false; }
    }
    function syncDownloadVisibility() {
        var $dl = $('#downloadContainer');
        if (isDownloadableView()) $dl.show(); else $dl.hide();
    }
    // Hide only. Showing again is left to the renderers, so the button never
    // appears before anything has been drawn — except when a view is left
    // and live data is already on screen.
    function hideDownloadIfNotExportable() {
        if (!isDownloadableView()) $('#downloadContainer').hide();
    }
    function refreshDownloadAfterViewChange() {
        if (!isDownloadableView()) { $('#downloadContainer').hide(); return; }
        if (Object.keys(liveData()).length > 0) $('#downloadContainer').show();
    }
    W.isDownloadableView = isDownloadableView;
    W.syncDownloadVisibility = syncDownloadVisibility;
    INFRA.refreshDownloadAfterViewChange = refreshDownloadAfterViewChange;

    function installDownloadGuards() {
        $(document).on('change.infra616', '#drpView', function () {
            setTimeout(hideDownloadIfNotExportable, 0);
        });
        // Renderers call $('#downloadContainer').show() directly; undo that
        // while a view without an export source is on screen.
        var dl = document.getElementById('downloadContainer');
        if (dl && typeof MutationObserver !== 'undefined') {
            new MutationObserver(function () {
                if (dl.style.display !== 'none' && !isDownloadableView()) dl.style.display = 'none';
            }).observe(dl, { attributes: true, attributeFilter: ['style'] });
        }
        var _origApply = fn('_tlApplyViewMode');
        if (_origApply) {
            W._tlApplyViewMode = function () {
                try { return _origApply.apply(this, arguments); }
                finally { setTimeout(refreshDownloadAfterViewChange, 0); }
            };
        }
        var _origOpen = fn('tlOpenAssetView');
        if (_origOpen) {
            W.tlOpenAssetView = function () {
                $('#downloadContainer').hide();
                return _origOpen.apply(this, arguments);
            };
        }
        var _origReturn = fn('tlReturnFromAssetView');
        if (_origReturn) {
            W.tlReturnFromAssetView = function () {
                try { return _origReturn.apply(this, arguments); }
                finally { setTimeout(refreshDownloadAfterViewChange, 0); }
            };
        }
    }

    /* ======================================================================
     * F4 — asset types that are never selectable
     * ==================================================================== */
    // "Equipment Room" and "Device Time" are internal asset types with no
    // telemetry view. The API spells them inconsistently, so the match is
    // trimmed and case-insensitive.
    function isHiddenAssetTypeName(name) {
        var n = trim(name).toUpperCase();
        if (n === '') return false;
        return n.indexOf('EQUIPMENT ROOM') !== -1 ||
            n.indexOf('DEVICE TIME') !== -1 ||
            n.indexOf('DEVICETIME') !== -1;
    }
    W.isHiddenAssetTypeName = isHiddenAssetTypeName;

    // Covers the server-rendered options and every later rebuild
    // (GetAssetTypeBySiteId). Removing an option fires the Index pill
    // observer again, so the pill strip follows.
    function purgeHiddenAssetTypes() {
        var sel = document.getElementById('drpAssetType');
        if (!sel) return 0;
        var removed = 0;
        for (var i = sel.options.length - 1; i >= 0; i--) {
            var o = sel.options[i];
            if (o.value !== '' && o.value !== '0' && isHiddenAssetTypeName(o.text)) {
                sel.removeChild(o);
                removed++;
            }
        }
        return removed;
    }
    INFRA.purgeHiddenAssetTypes = purgeHiddenAssetTypes;
    function installHiddenAssetTypeFilter() {
        purgeHiddenAssetTypes();
        var sel = document.getElementById('drpAssetType');
        if (sel && typeof MutationObserver !== 'undefined') {
            new MutationObserver(function () { purgeHiddenAssetTypes(); })
                .observe(sel, { childList: true });
        }
    }

    /* ======================================================================
     * F2 — selection info bar
     * ==================================================================== */
    var _siteDetailCache = {};
    var _siteDetailPending = {};

    function selText(selector) {
        var $el = $(selector);
        if (!$el.length) return '';
        var v = $el.val();
        if (v === null || v === undefined || v === '' || v === '0') return '';
        return trim($el.find('option:selected').text());
    }
    function isAllText(v) { return !v || v === 'All' || v === 'Select' || v === '–' || v === '-'; }

    function getSelectedAssets() {
        var out = [];
        $('#listAssetNumber .dropdown-item').each(function () {
            var $cb = $(this).find('input[type="checkbox"]');
            if ($cb.prop('checked')) {
                out.push({ id: str($cb.val()), name: trim($(this).attr('data-text') || $(this).text() || $cb.val()) });
            }
        });
        return out;
    }

    function chip(label, value, extraHtml, cls) {
        var all = isAllText(value);
        return '<span class="sib-chip' + (cls ? ' ' + cls : '') + '">' +
            '<span class="sib-label">' + label + '</span>' +
            '<span class="sib-val' + (all ? ' sib-all' : '') + '" title="' + esc(all ? '' : value) + '">' +
            esc(all ? '–' : value) + '</span>' + (extraHtml || '') + '</span>';
    }

    // A miss is remembered for this long. GetSiteById answers a failure with
    // HTTP 200 and {error:...}, so without a negative entry one bad site is
    // re-requested on every single bar update, forever.
    var _siteDetailMissTtl = 60000;

    function settleSiteDetails(siteId, data) {
        _siteDetailCache[siteId] = { data: data, t: Date.now() };
        var waiting = _siteDetailPending[siteId] || [];
        delete _siteDetailPending[siteId];
        for (var i = 0; i < waiting.length; i++) {
            try { waiting[i](data); }
            catch (e) { if (W.console) console.warn('[infra616] site detail callback failed', e); }
        }
    }

    function fetchSiteDetails(siteId, cb) {
        siteId = trim(siteId);
        if (!siteId || siteId === '0') { cb(null); return; }

        var hit = _siteDetailCache[siteId];
        if (hit && (hit.data || Date.now() - hit.t < _siteDetailMissTtl)) { cb(hit.data); return; }

        // updateSelInfoBar runs on every filter change and GetSiteById proxies a
        // blocking server-side API call, so the same site gets asked for again
        // and again while the first answer is still on the wire — the cache only
        // fills on completion. Queue on the in-flight request instead of
        // starting a second one.
        if (_siteDetailPending[siteId]) { _siteDetailPending[siteId].push(cb); return; }
        _siteDetailPending[siteId] = [cb];

        $.ajax({
            url: '/FRS25/Telemetry/GetSiteById?siteId=' + encodeURIComponent(siteId),
            type: 'GET', dataType: 'json', timeout: 10000,
            success: function (data) { settleSiteDetails(siteId, (data && !data.error) ? data : null); },
            error: function () { settleSiteDetails(siteId, null); }
        });
    }
    // Shared with getReportLocationInfo (telemetrylive.js) so exports reuse this
    // cache instead of issuing their own uncached request per download.
    W.fetchSiteDetails = fetchSiteDetails;

    function positionPopup(trigger, $popup) {
        var r = trigger.getBoundingClientRect();
        var ph = $popup.outerHeight(), pw = $popup.outerWidth();
        var vh = W.innerHeight, vw = W.innerWidth;
        var top = r.bottom + 6, left = r.left;
        if (top + ph > vh - 10) top = r.top - ph - 6;
        if (top < 10) top = r.bottom + 6;
        if (left + pw > vw - 10) left = vw - pw - 10;
        if (left < 10) left = 10;
        $popup.css({ top: top + 'px', left: left + 'px' });
    }

    function showAssetsPopup(trigger, assets, ctx) {
        var $popup = $('#sibAssetsPopup');
        var parts = [];
        if (!isAllText(ctx.zone)) parts.push('<strong>Zone:</strong> ' + esc(ctx.zone));
        if (!isAllText(ctx.division)) parts.push('<strong>Division:</strong> ' + esc(ctx.division));
        if (!isAllText(ctx.station)) parts.push('<strong>Station:</strong> ' + esc(ctx.station));
        if (!isAllText(ctx.assetType)) parts.push('<strong>Type:</strong> ' + esc(ctx.assetType));
        $('#sibPopupContext').html(parts.join(' &nbsp;|&nbsp; '));
        $('#sibPopupCount').text(assets.length);
        var h = '';
        for (var i = 0; i < assets.length; i++) h += '<div class="sib-asset-item">' + esc(assets[i].name) + '</div>';
        $('#sibPopupList').html(h);
        $popup.show();
        positionPopup(trigger, $popup);
        $(W).off('scroll.sibPopup resize.sibPopup').on('scroll.sibPopup resize.sibPopup', function () {
            if ($popup.is(':visible') && document.body.contains(trigger)) positionPopup(trigger, $popup);
            else { $popup.hide(); $(W).off('scroll.sibPopup resize.sibPopup'); }
        });
    }

    var _lastBar = null;
    function renderBarChips(ctx, assets) {
        _lastBar = { ctx: ctx, assets: assets };
        var h = chip('Zone', ctx.zone) + chip('Division', ctx.division) +
            chip('Station', ctx.station) + chip('Type', ctx.assetType);
        if (assets.length <= 1) {
            h += chip('Asset', assets.length ? assets[0].name : '');
        } else {
            h += chip('Asset', assets[0].name,
                '<button type="button" class="sib-more-badge" id="sibMoreBadge" ' +
                'title="Show all selected assets" aria-haspopup="dialog">+' + (assets.length - 1) + ' more</button>');
        }
        h += '<span class="sib-chip sib-time" id="sibTimeChip"><span class="sib-label">' +
            '<i class="fas fa-clock" aria-hidden="true"></i>Time</span>' +
            '<span class="sib-val" id="sibTimeVal">' + esc(clockText()) + '</span></span>';
        $('#selInfoBar').html(h);
        $('#sibAssetsPopup').hide();
    }

    function updateSelInfoBar() {
        if (!$('#selInfoBar').length) return;
        var ctx = {
            zone: selText('#drpZones'),
            division: selText('#drpDivisions'),
            station: selText('#drpSite'),
            assetType: selText('#drpAssetType')
        };
        var assets = getSelectedAssets();
        var siteId = trim($('#drpSite').val());
        renderBarChips(ctx, assets);
        if ((isAllText(ctx.zone) || isAllText(ctx.division)) && siteId && siteId !== '0') {
            fetchSiteDetails(siteId, function (d) {
                if (!d || trim($('#drpSite').val()) !== siteId) return;   // selection moved on
                var z = d.ZoneName || d.Zone || d.zoneName || d.zone || '';
                var dv = d.DivisionName || d.Division || d.divisionName || d.division || '';
                renderBarChips({
                    zone: isAllText(ctx.zone) ? z : ctx.zone,
                    division: isAllText(ctx.division) ? dv : ctx.division,
                    station: d.Name || d.SiteName || ctx.station,
                    assetType: ctx.assetType
                }, assets);
            });
        }
    }
    W.updateSelInfoBar = updateSelInfoBar;

    function pad2(n) { return n < 10 ? '0' + n : String(n); }
    function clockText() {
        var d = new Date();
        return d.getDate() + '/' + pad2(d.getMonth() + 1) + '/' + d.getFullYear() + ' ' +
            pad2(d.getHours()) + ':' + pad2(d.getMinutes()) + ':' + pad2(d.getSeconds());
    }
    function tickClock() {
        var el = document.getElementById('sibTimeVal');
        if (el) el.textContent = clockText();
    }

    var _barTimer = null;
    function scheduleBar(ms) {
        if (_barTimer) clearTimeout(_barTimer);
        _barTimer = setTimeout(function () { _barTimer = null; updateSelInfoBar(); }, ms || 80);
    }
    INFRA.scheduleSelInfoBar = scheduleBar;

    function injectBar() {
        if ($('#selInfoBar').length) return;
        var $bar = $('<div id="selInfoBar" role="status" aria-live="polite" aria-label="Current selection"></div>');
        var $anchor = $('#wsStatusBar').first();
        if ($anchor.length) $anchor.after($bar);
        else if ($('#tlFiltersRow').length) $('#tlFiltersRow').after($bar);
        else if ($('#divTelemetryLive').length) $('#divTelemetryLive').before($bar);
        else $('body').prepend($bar);

        if (!$('#sibAssetsPopup').length) {
            $('body').append(
                '<div id="sibAssetsPopup" role="dialog" aria-label="Selected assets">' +
                '<div class="sib-popup-title"><span>Selected Assets (<span id="sibPopupCount">0</span>)</span>' +
                '<button type="button" class="sib-close-btn" id="sibPopupClose" title="Close" aria-label="Close">&#10005;</button></div>' +
                '<div class="sib-context" id="sibPopupContext"></div>' +
                '<div class="sib-asset-list" id="sibPopupList"></div></div>');
        }
        $(document).on('click.infra616sib', '#sibMoreBadge', function (e) {
            e.stopPropagation();
            var $p = $('#sibAssetsPopup');
            if ($p.is(':visible')) { $p.hide(); return; }
            if (_lastBar) showAssetsPopup(this, _lastBar.assets, _lastBar.ctx);
        });
        $(document).on('click.infra616sib', '#sibPopupClose', function () { $('#sibAssetsPopup').hide(); });
        $(document).on('click.infra616sib', function (e) {
            if (!$(e.target).closest('#sibAssetsPopup, #sibMoreBadge').length) $('#sibAssetsPopup').hide();
        });
        $(document).on('keydown.infra616sib', function (e) {
            if (e.key === 'Escape') $('#sibAssetsPopup').hide();
        });
    }

    function hookBarEvents() {
        $(document).on('change.infra616sib', '#drpZones, #drpDivisions, #drpSite, #drpAssetType', function () { scheduleBar(100); });
        $(document).on('click.infra616sib change.infra616sib',
            '#listAssetNumber .dropdown-item, #listAssetNumber input, #chkAllAssetNumber', function () { scheduleBar(80); });
        // The asset list is rebuilt by AJAX callbacks; follow it.
        var list = document.getElementById('listAssetNumber');
        if (list && typeof MutationObserver !== 'undefined') {
            new MutationObserver(function () { scheduleBar(120); }).observe(list, { childList: true });
        }
        ['_tlAutoLoad', 'fnAdvSearch', 'fnSearchView'].forEach(function (name) {
            var orig = fn(name);
            if (!orig) return;
            W[name] = function () {
                try { return orig.apply(this, arguments); }
                finally { scheduleBar(350); }
            };
        });
    }

    /* ======================================================================
     * F5 — Excel / CSV with location columns and vibration section
     * ==================================================================== */
    var LOC_HEAD = ['S.No', 'Zone', 'Division', 'Station'];
    var VIB_LABEL = 'Point Machine Vibration';

    function toast(kind, msg, title) {
        var f = fn({ warn: 'showWarning', ok: 'showSuccess', err: 'showError' }[kind]);
        if (f) f(msg, title);
    }

    function isPmExport() {
        try {
            return (typeof W.isPointAssetType === 'function' && W.isPointAssetType()) || !!W.pmTableMode;
        } catch (e) { return false; }
    }

    function vibrationSection(loc) {
        var head = [];
        if (fn('getPmVibrationExportHeaderRow')) head.push(W.getPmVibrationExportHeaderRow());
        if (fn('getPmVibrationExportEventHeaderRow')) head.push(W.getPmVibrationExportEventHeaderRow());
        if (!head.length) head.push(['Asset Name', 'Date & Time']);
        var rows = [];
        try {
            if (fn('buildPmVibrationExportRows')) rows = W.buildPmVibrationExportRows(loc) || [];
        } catch (e) {
            if (W.console) console.warn('[infra616] vibration export rows failed', e);
            rows = [];
        }
        var toArr = fn('pmVibrationExportRowArray') || function (r) { return [r.assetName, r.timestamp].concat(r.cells || []); };
        var body = rows.map(function (r) { return toArr(r).map(function (c) { return str(c); }); });
        return {
            label: VIB_LABEL,
            head: head,
            body: body,
            vibration: true,
            empty: body.length === 0,
            placeholder: 'No vibration data available for the selected point machines.'
        };
    }

    // Header text as written in the markup (Aurora CSS upper-cases innerText).
    function headCellText(th) {
        var tmp = document.createElement('div');
        tmp.innerHTML = str(th.innerHTML).replace(/<br\s*\/?>/gi, ' ');
        return trim(str(tmp.textContent).replace(/\s+/g, ' '));
    }
    // The <thead> as a grid: a colspan/rowspan cell keeps its text in its
    // first slot and leaves '' in the others (v616 expandHeader).
    function expandHeaderGrid(tbl) {
        var grid = [];
        var hrows = tbl.tHead ? tbl.tHead.rows : [];
        for (var r = 0; r < hrows.length; r++) {
            grid[r] = grid[r] || [];
            var col = 0;
            for (var c = 0; c < hrows[r].cells.length; c++) {
                var th = hrows[r].cells[c];
                while (grid[r][col] !== undefined) col++;
                var cs = parseInt(th.getAttribute('colspan'), 10) || 1;
                var rs = parseInt(th.getAttribute('rowspan'), 10) || 1;
                var txt = headCellText(th);
                for (var rr = 0; rr < rs; rr++) {
                    grid[r + rr] = grid[r + rr] || [];
                    for (var cc = 0; cc < cs; cc++) grid[r + rr][col + cc] = (rr === 0 && cc === 0) ? txt : '';
                }
                col += cs;
            }
        }
        var width = 0;
        grid.forEach(function (g) { width = Math.max(width, g.length); });
        return grid.map(function (g) {
            var row = [];
            for (var k = 0; k < width; k++) row.push(g[k] === undefined ? '' : g[k]);
            return row;
        });
    }
    function hasSpannedHeader(tbl) {
        return !!(tbl.tHead && tbl.tHead.querySelector('th[colspan],th[rowspan]'));
    }
    // v617's collector reads each header row flat, so a colspan/rowspan
    // header ends up shifted. Put the header back on the real column grid
    // when the table has spans and the collector's rows do not line up.
    function alignHeader(tbl, section) {
        if (!section || !hasSpannedHeader(tbl)) return section;
        var width = section.body.length ? section.body[0].length : 0;
        var aligned = section.head.length && section.head.every(function (r) { return r.length === width; });
        if (aligned) return section;                        // e.g. already expanded by telemetrylive-pm616.js
        var grid = expandHeaderGrid(tbl);
        var first = tbl.tBodies.length ? tbl.tBodies[0].rows[0] : null;
        var drop = {};
        if (first) {
            for (var i = 0; i < first.cells.length; i++) {
                if (first.cells[i].classList && first.cells[i].classList.contains('tl-asset-actions-cell')) drop[i] = true;
            }
        }
        grid = grid.map(function (r) { return r.filter(function (c, idx) { return !drop[idx]; }); });
        if (!grid.length || grid[0].length !== width) return section;
        section.head = grid;
        return section;
    }

    // Sections from the tables on screen (or the off-screen card-view
    // table), plus the vibration section for Point Machine.
    function collectSections(loc) {
        var scope = fn('tlOpenExportScope') ? W.tlOpenExportScope() : null;
        var sections = [];
        if (scope) {
            try {
                var collect = fn('tlCollectExportSections');
                (scope.tables || []).forEach(function (tbl) {
                    if (!collect) return;
                    var got = collect({ tables: [tbl], release: function () { } }) || [];
                    got.forEach(function (s) { sections.push(alignHeader(tbl, s)); });
                });
            } finally {
                if (typeof scope.release === 'function') scope.release();
            }
        }
        if (isPmExport() && (sections.length || Object.keys(liveData()).length)) {
            // The on-screen vibration table is replaced by the section built
            // from wsVibrationData, so it is not exported twice.
            sections = sections.filter(function (s) {
                return !/vibration/i.test(str(s && s.label));
            });
            sections.push(vibrationSection(loc));
        }
        return sections;
    }

    // Adds S.No / Zone / Division / Station in front of every header and
    // body row. Header rows are padded to the same width.
    function withLocation(section, loc) {
        var width = 0;
        section.head.concat(section.body).forEach(function (r) { width = Math.max(width, r.length); });
        if (section.empty) width = Math.max(width, 1);
        var head = section.head.map(function (r, i) {
            var pad = r.slice();
            while (pad.length < width) pad.push('');
            return (i === 0 ? LOC_HEAD.slice() : ['', '', '', '']).concat(pad);
        });
        var body;
        if (section.empty) {
            var ph = [section.placeholder];
            while (ph.length < width) ph.push('');
            body = [['', loc.zone || '', loc.division || '', loc.station || ''].concat(ph)];
        } else {
            body = section.body.map(function (r, i) {
                var row = r.slice();
                while (row.length < width) row.push('');
                return [i + 1, loc.zone || '', loc.division || '', loc.station || ''].concat(row);
            });
        }
        return { label: section.label, head: head, body: body, cols: width + LOC_HEAD.length, empty: !!section.empty, vibration: !!section.vibration };
    }

    // Merge ranges for a header block: a titled cell spreads right over
    // empty cells in its row, then down over rows that are empty under its
    // whole width. Ranges never overlap.
    function headerMerges(head) {
        var rows = head.length, cols = 0, used = [], out = [];
        head.forEach(function (r) { cols = Math.max(cols, r.length); });
        for (var r = 0; r < rows; r++) used.push([]);
        function blank(rr, cc) { return str(head[rr][cc]) === '' && !used[rr][cc]; }
        for (var r2 = 0; r2 < rows; r2++) {
            for (var c = 0; c < cols; c++) {
                if (used[r2][c] || str(head[r2][c]) === '') continue;
                var below = (r2 + 1 < rows) ? str(head[r2 + 1][c]) : null;
                var w = 1;
                if (below !== '') {
                    // Group title (sub-headers below it) or the last header
                    // row: spread over the empty cells to the right.
                    while (c + w < cols && blank(r2, c + w) &&
                        (r2 + 1 >= rows || str(head[r2 + 1][c + w]) !== '')) w++;
                }
                // below === '' : a rowspan title, one column wide.
                var h = 1;
                while (r2 + h < rows) {
                    var ok = true;
                    for (var k2 = 0; k2 < w; k2++) if (!blank(r2 + h, c + k2)) { ok = false; break; }
                    if (!ok) break;
                    h++;
                }
                for (var a = 0; a < h; a++) for (var b = 0; b < w; b++) used[r2 + a][c + b] = true;
                if (w > 1 || h > 1) out.push({ r: r2, c: c, h: h, w: w });
            }
        }
        return out;
    }
    INFRA.headerMerges = headerMerges;

    function fileStamp() { return new Date().toISOString().slice(0, 10); }

    function saveBlob(blob, name) {
        if (typeof W.saveAs === 'function') { W.saveAs(blob, name); return; }
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = name;
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 0);
    }

    function withLocationInfo(cb) {
        var f = fn('getReportLocationInfo');
        var fallback = {
            zone: selText('#drpZones'), division: selText('#drpDivisions'), station: selText('#drpSite')
        };
        if (!f) { cb(fallback); return; }
        var done = false;
        try {
            f(function (loc) {
                if (done) return;
                done = true;
                loc = loc || fallback;
                cb({
                    zone: isAllText(loc.zone) ? '' : loc.zone,
                    division: isAllText(loc.division) ? '' : loc.division,
                    station: isAllText(loc.station) ? '' : loc.station
                });
            });
        } catch (e) { if (!done) { done = true; cb(fallback); } }
    }

    function writeSheet(wb, sheetName, title, loc, sections) {
        var ws = wb.addWorksheet(sheetName);
        var cc = LOC_HEAD.length + 1;
        sections.forEach(function (s) { cc = Math.max(cc, s.cols); });

        var tr = ws.addRow([title]);
        tr.height = 30;
        ws.mergeCells(tr.number, 1, tr.number, cc);
        tr.getCell(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF0E1530' } };
        tr.getCell(1).font = { bold: true, color: { argb: 'FF22D3EE' }, size: 16 };
        tr.getCell(1).alignment = { vertical: 'middle', horizontal: 'center' };

        var info = 'Zone: ' + (loc.zone || '-') + '   |   Division: ' + (loc.division || '-') +
            '   |   Station: ' + (loc.station || '-') + '   |   Generated: ' + new Date().toLocaleString();
        var ir = ws.addRow([info]);
        ir.height = 20;
        ws.mergeCells(ir.number, 1, ir.number, cc);
        ir.getCell(1).font = { italic: true, size: 10, color: { argb: 'FF64748B' } };
        ir.getCell(1).alignment = { horizontal: 'center' };
        ws.addRow([]);

        var border = { style: 'thin', color: { argb: 'FF3B5998' } };
        sections.forEach(function (s, si) {
            if (si > 0) ws.addRow([]);
            if (s.label) {
                var lr = ws.addRow([s.label]);
                lr.height = 22;
                ws.mergeCells(lr.number, 1, lr.number, cc);
                lr.getCell(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF334155' } };
                lr.getCell(1).font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 11 };
                lr.getCell(1).alignment = { horizontal: 'left', vertical: 'middle' };
            }
            var firstHead = null;
            s.head.forEach(function (r, ri) {
                var er = ws.addRow(r);
                if (firstHead === null) firstHead = er.number;
                er.height = ri === 0 ? 26 : 22;
                for (var c = 1; c <= s.cols; c++) {
                    var cell = er.getCell(c);
                    var isLoc = c <= LOC_HEAD.length;
                    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: isLoc ? 'FF0F4C81' : (ri === 0 ? 'FF1E3A5F' : 'FF27466E') } };
                    cell.font = { bold: true, color: { argb: 'FFFFFFFF' }, size: ri === 0 ? 10 : 9 };
                    cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
                    cell.border = { left: border, right: border, top: border, bottom: border };
                }
            });
            if (firstHead !== null) {
                headerMerges(s.head).forEach(function (m) {
                    try {
                        ws.mergeCells(firstHead + m.r, m.c + 1, firstHead + m.r + m.h - 1, m.c + m.w);
                    } catch (e) { /* an overlapping range is left unmerged */ }
                });
            }
            s.body.forEach(function (r, i) {
                var er = ws.addRow(r);
                er.height = 20;
                for (var c = 1; c <= s.cols; c++) {
                    var cell = er.getCell(c);
                    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: i % 2 === 0 ? 'FFFFFFFF' : 'FFF1F5F9' } };
                    cell.font = s.empty && c === LOC_HEAD.length + 1 ? { italic: true, size: 10, color: { argb: 'FF64748B' } } : { size: 10 };
                    cell.border = { left: { style: 'thin', color: { argb: 'FFE2E8F0' } }, right: { style: 'thin', color: { argb: 'FFE2E8F0' } } };
                }
                if (s.empty) {
                    try { ws.mergeCells(er.number, LOC_HEAD.length + 1, er.number, s.cols); } catch (e) { }
                }
            });
        });

        ws.columns.forEach(function (col, idx) {
            var ml = idx === 0 ? 6 : 12;
            col.eachCell({ includeEmpty: false }, function (c) {
                if (c.isMerged && c.master !== c) return;
                var l = c.value ? String(c.value).length : 0;
                if (l > ml && l < 60) ml = l;
            });
            col.width = Math.min(ml + 2, 40);
        });
        return ws;
    }

    function fnDownloadExcel616() {
        if (typeof W.ExcelJS === 'undefined') { toast('err', 'Excel library not loaded', 'Error'); return; }
        withLocationInfo(function (loc) {
            var sections;
            try { sections = collectSections(loc); }
            catch (e) { if (W.console) console.error('[Excel Download] collect failed', e); sections = []; }
            if (!sections.length) { toast('warn', 'No data available to download. Please search first.', 'No Data'); return; }
            try {
                $('#loader').show();
                var wb = new W.ExcelJS.Workbook();
                wb.creator = 'RDPMS';
                wb.created = new Date();
                var main = [], vib = [];
                sections.forEach(function (s) {
                    var x = withLocation(s, loc);
                    if (x.vibration) vib.push(x); else main.push(x);
                });
                if (main.length) writeSheet(wb, 'Telemetry Live', 'Telemetry Live Report', loc, main);
                if (vib.length) writeSheet(wb, 'PM Vibration', 'Point Machine Vibration Report', loc, vib);
                wb.xlsx.writeBuffer().then(function (b) {
                    saveBlob(new Blob([b], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
                        'TelemetryLive_' + fileStamp() + '.xlsx');
                    $('#loader').hide();
                    toast('ok', 'Excel downloaded!', 'Download');
                }, function (err) {
                    $('#loader').hide();
                    if (W.console) console.error('[Excel Download] write failed', err);
                    toast('err', 'Download failed', 'Error');
                });
            } catch (e) {
                $('#loader').hide();
                if (W.console) console.error('[Excel Download] Error:', e);
                toast('err', 'Download failed', 'Error');
            }
        });
    }

    function buildCsvText(sections, loc) {
        var q = function (v) { return '"' + str(v).replace(/"/g, '""') + '"'; };
        var lines = [];
        lines.push(q('Telemetry Live Report'));
        lines.push(['Zone: ' + (loc.zone || '-'), 'Division: ' + (loc.division || '-'),
            'Station: ' + (loc.station || '-'), 'Generated: ' + new Date().toLocaleString()].map(q).join(','));
        sections.forEach(function (s0) {
            var s = withLocation(s0, loc);
            lines.push('');
            if (s.label) lines.push(q(s.label));
            s.head.forEach(function (r) { lines.push(r.map(q).join(',')); });
            s.body.forEach(function (r) { lines.push(r.map(q).join(',')); });
        });
        return '﻿' + lines.join('\r\n');
    }
    INFRA.buildCsvText = buildCsvText;
    INFRA.collectExportSections = collectSections;

    function fnDownloadCSV616() {
        withLocationInfo(function (loc) {
            var sections;
            try { sections = collectSections(loc); }
            catch (e) { if (W.console) console.error('[CSV Download] collect failed', e); sections = []; }
            if (!sections.length) { toast('warn', 'No data available to download. Please search first.', 'No Data'); return; }
            try {
                saveBlob(new Blob([buildCsvText(sections, loc)], { type: 'text/csv;charset=utf-8;' }),
                    'TelemetryLive_' + fileStamp() + '.csv');
                toast('ok', 'CSV downloaded!', 'Download');
            } catch (e) {
                if (W.console) console.error('[CSV Download] Error:', e);
                toast('err', 'Download failed', 'Error');
            }
        });
    }
    if (fn('fnDownloadExcel')) INFRA.origDownloadExcel = W.fnDownloadExcel;
    if (fn('fnDownloadCSV')) INFRA.origDownloadCSV = W.fnDownloadCSV;
    W.fnDownloadExcel = fnDownloadExcel616;
    W.fnDownloadCSV = fnDownloadCSV616;

    /* ======================================================================
     * F6 — DataLogger details popup
     * ==================================================================== */
    var DETAIL_GROUP_SEQ = [
        'RG', 'DG', 'HG', 'HHG',
        'AUG', 'CALLING',
        'DPR', 'HPR', 'HHPR', 'AUPR',
        'VCOHPR', 'VROHPR',
        'SHON', 'SHOFF', 'SHPILOT', 'SHHPR'   // shunt: ON, OFF, PILOT, HPR
    ];
    function normKey(s) {
        return fn('_normSignalKey') ? W._normSignalKey(s) : str(s).toUpperCase().replace(/[^A-Z0-9]/g, '');
    }
    function detailKey(name) {
        var n = normKey(name), g = '';
        if (n.indexOf('SHSIG') >= 0 && n.indexOf('HPR') >= 0) g = 'SHHPR';
        else if (n.indexOf('SHSIG') >= 0 && n.indexOf('PILOT') >= 0) g = 'SHPILOT';
        else if (n.indexOf('SHSIG') >= 0 && n.indexOf('OFF') >= 0) g = 'SHOFF';
        else if (n.indexOf('SHSIG') >= 0 && n.indexOf('ON') >= 0) g = 'SHON';
        else if (n.indexOf('AUPR') >= 0) g = 'AUPR';
        else if (n.indexOf('VCO') >= 0 && n.indexOf('HPR') >= 0) g = 'VCOHPR';
        else if (n.indexOf('VRO') >= 0 && n.indexOf('HPR') >= 0) g = 'VROHPR';
        else if (n.indexOf('HHPR') >= 0) g = 'HHPR';
        else if (n.indexOf('DPR') >= 0) g = 'DPR';
        else if (n.indexOf('HPR') >= 0) g = 'HPR';
        else if (n.indexOf('CALLING') >= 0 || n.indexOf('COHG') >= 0 || n === 'VCOSIG' || n === 'ICOSIG') g = 'CALLING';
        else if (n.indexOf('AUG') >= 0) g = 'AUG';
        else if (n.indexOf('HHG') >= 0) g = 'HHG';
        else if (n.indexOf('RG') >= 0) g = 'RG';
        else if (n.indexOf('DG') >= 0) g = 'DG';
        else if (n.indexOf('HG') >= 0) g = 'HG';
        var gi = DETAIL_GROUP_SEQ.indexOf(g);
        if (gi < 0) gi = 999;
        var unit = (/^(V|VSIG|VRO|VCO)/.test(n) || /V$/.test(n)) ? 0 : 1;   // voltage before current
        return gi * 10 + unit;
    }
    W._detailKey = W._detailKey || detailKey;
    INFRA.detailKey = detailKey;

    function tsToSecMs(ts) {
        if (fn('pmTsToSecMs')) return W.pmTsToSecMs(ts);
        if (!ts) return 0;
        var ms = new Date(ts).getTime();
        return ms > 0 ? Math.floor(ms / 1000) * 1000 : 0;
    }
    // Local copy of v616 pmEntryMatchesOperation (used only when no
    // window.pmEntryMatchesOperation exists).
    function entryMatchesOperationLocal(entry, opTsMs) {
        if (!entry) return false;
        if (!opTsMs) return true;
        var eTs = tsToSecMs(entry.TimestampDevice || entry.timestamp);
        if (!eTs) return false;
        return eTs >= opTsMs;
    }
    function entryMatchesOperation(entry, opTsMs) {
        return fn('pmEntryMatchesOperation') ? W.pmEntryMatchesOperation(entry, opTsMs)
            : entryMatchesOperationLocal(entry, opTsMs);
    }
    INFRA.entryMatchesOperation = entryMatchesOperation;

    function assetIsPm(assetId) {
        var a = liveData()[assetId] || {};
        var t = a.AssetTypeId;
        if (t === undefined || t === null || t === '') {
            t = (typeof W.wsCurrentAssetTypeId !== 'undefined' && W.wsCurrentAssetTypeId) ? W.wsCurrentAssetTypeId : $('#drpAssetType').val();
        }
        return String(t) === '3';
    }

    // Fallback gate for PM when telemetrylive-pm616.js is not loaded: drop
    // metric entries older than the latest operation.
    function withPmGate(assetId, work) {
        var orig = fn('getPmStructuredData');
        var hist = W.pmEventHistory && W.pmEventHistory[assetId];
        var last = (hist && hist.length) ? hist[hist.length - 1] : null;
        var opMs = last ? tsToSecMs(last.timestampDevice) : 0;
        if (!orig || !opMs) return work();
        var attrs = (liveData()[assetId] || {}).attrs || {};
        W.getPmStructuredData = function (aid) {
            var r = orig.apply(this, arguments);
            if (!r || String(aid) !== String(assetId)) return r;
            ['Normal', 'Reverse'].forEach(function (dir) {
                var grp = r[dir] || {};
                Object.keys(grp).forEach(function (g) {
                    var metrics = grp[g] || {};
                    Object.keys(metrics).forEach(function (m) {
                        var e = metrics[m];
                        if (!e) return;
                        var ad = attrs[e.attrName] || {};
                        var probe = {
                            TimestampDevice: e.TimestampDevice || ad.TimestampDevice || e.timestamp,
                            timestamp: e.timestamp,
                            attrName: e.attrName
                        };
                        if (!entryMatchesOperation(probe, opMs)) delete metrics[m];
                    });
                });
            });
            return r;
        };
        try { return work(); } finally { W.getPmStructuredData = orig; }
    }

    function plainText(html) {
        var d = document.createElement('div');
        d.innerHTML = str(html);
        return trim(d.textContent);
    }

    // Signal popup: drop DataLogger relays (they have their own table) and
    // order rows by signal group, voltage before current.
    function tidySignalRows(assetId) {
        var host = document.getElementById('model-div-alert');
        if (!host) return;
        var table = host.querySelector('table');
        if (!table || !table.tBodies.length) return;
        var body = table.tBodies[0];
        var rows = Array.prototype.slice.call(body.rows).filter(function (tr) { return tr.cells.length >= 3; });
        if (!rows.length) return;

        var pm = fn('getPmStructuredData') ? W.getPmStructuredData(assetId) : null;
        var rd = (pm && pm.RDPMS) || {};
        var dlLabels = {}, keepLabels = {};
        Object.keys(rd).forEach(function (n) {
            var e = rd[n];
            var isDl = (e && e.DataType) ? e.DataType === 'DataLogger'
                : (fn('_isDataloggerAttr') ? !!W._isDataloggerAttr(assetId, n) : false);
            var lbl = n;
            if (fn('getSignalHeaderLabel')) { var gl = W.getSignalHeaderLabel(assetId, n); if (gl) lbl = gl; }
            lbl = plainText(lbl);
            if (isDl) dlLabels[lbl] = (dlLabels[lbl] || 0) + 1;
            else keepLabels[lbl] = true;
        });

        var kept = [];
        rows.forEach(function (tr) {
            var t = trim(tr.cells[0].textContent);
            if (dlLabels[t] && !keepLabels[t]) {
                dlLabels[t]--;
                tr.parentNode.removeChild(tr);
            } else {
                kept.push(tr);
            }
        });
        kept.sort(function (a, b) {
            var la = trim(a.cells[0].textContent), lb = trim(b.cells[0].textContent);
            return (detailKey(la) - detailKey(lb)) ||
                la.localeCompare(lb, undefined, { numeric: true, sensitivity: 'base' });
        });
        kept.forEach(function (tr) { body.appendChild(tr); });
        if (!body.rows.length) {
            body.innerHTML = '<tr><td colspan="3" style="text-align:center;color:#94a3b8;font-style:italic;">No data available</td></tr>';
        }
    }
    INFRA.tidySignalRows = tidySignalRows;

    var _origDlEvent = fn('fnShowDataLoggerEvent');
    if (_origDlEvent) {
        W.fnShowDataLoggerEvent = function (assetId) {
            var self = this, args = arguments;
            var pmAsset = assetIsPm(assetId);
            var run = function () { return _origDlEvent.apply(self, args); };
            var result = (pmAsset && !W.PM616) ? withPmGate(assetId, run) : run();
            if (!pmAsset) {
                try { tidySignalRows(assetId); }
                catch (e) { if (W.console) console.warn('[infra616] detail rows', e); }
            }
            return result;
        };
    }

    /* ======================================================================
     * F7 — derived values: this asset's own metadata before fixed IDs
     * ==================================================================== */
    // Operand name lists as calculateDerivedValues (v617) looks them up.
    var DERIVED_OPERANDS = [
        ['If mA', 'ITC FEED END(mA)', 'ITC FEED END'],
        ['Ir mA', 'ITC RELAY END(mA)', 'ITC RELAY END'],
        ['Charger mA', 'ITC TFC O/P(mA)', 'ITC TFC O/P'],
        ['Choke V', 'VTC CH FEED END(V)', 'VTC CH FEED END'],
        ['Charger OP V', 'VTC TFC O/P', 'VTC TFC O/P(V)', 'VTC TFC I/P', 'VTC TFC I/P(V)'],
        ['Vf', 'Vf V', 'VTC FEED END', 'VTC FEED END(V)']
    ];
    function normName(v) { return str(v).trim().toLowerCase().replace(/[^a-z0-9]/g, ''); }
    function parseNum(v) {
        if (v === null || v === undefined || str(v).trim() === '') return null;
        var n = parseFloat(v);
        return isNaN(n) ? null : n;
    }
    function directValue(attrs, aliases) {
        var get = fn('getAttrValue');
        for (var i = 0; i < aliases.length; i++) {
            var raw = get ? get(attrs, aliases[i]) : (attrs[aliases[i]] ? attrs[aliases[i]].Value : null);
            if (parseNum(raw) !== null) return true;
        }
        return false;
    }
    function augmentDerivedAttrs(attrs, assetIdHint) {
        var map = W.userAssetSimpleMap;
        if (!attrs || !map) return null;
        var aid = fn('resolveAssetIdFromAttrs') ? W.resolveAssetIdFromAttrs(attrs, assetIdHint) : assetIdHint;
        aid = trim(aid);
        if (!aid) return null;
        var copy = null;
        for (var oi = 0; oi < DERIVED_OPERANDS.length; oi++) {
            var aliases = DERIVED_OPERANDS[oi];
            if (directValue(attrs, aliases)) continue;          // name lookup already works
            var wanted = aliases.map(normName);
            for (var key in attrs) {
                if (!Object.prototype.hasOwnProperty.call(attrs, key)) continue;
                var ad = attrs[key];
                if (!ad || parseNum(ad.Value) === null) continue;
                var attrId = trim(ad.AssetAttributeId || ad.AttrId);
                if (!attrId) continue;
                var meta = map[aid + '_' + attrId];
                if (!meta) continue;
                var names = [key, ad.AssetAttributeName, ad.AttributeName, meta.attributeName, meta.aliasName, meta.name];
                var hit = false;
                for (var ni = 0; ni < names.length && !hit; ni++) {
                    var nn = normName(names[ni]);
                    if (nn && wanted.indexOf(nn) !== -1) hit = true;
                }
                if (!hit) continue;
                if (!copy) { copy = {}; for (var k in attrs) if (Object.prototype.hasOwnProperty.call(attrs, k)) copy[k] = attrs[k]; }
                copy[aliases[0]] = ad;
                break;
            }
        }
        return copy;
    }
    INFRA.augmentDerivedAttrs = augmentDerivedAttrs;

    var _origDerived = fn('calculateDerivedValues');
    if (_origDerived) {
        W.calculateDerivedValues = function (attrs, assetIdHint) {
            var aug = null;
            try { aug = augmentDerivedAttrs(attrs, assetIdHint); } catch (e) { aug = null; }
            if (!aug) return _origDerived.apply(this, arguments);
            var args = Array.prototype.slice.call(arguments);
            args[0] = aug;
            // resolveAssetIdFromAttrs still works: the copy keeps every
            // original entry (and its AssetId).
            if (args.length < 2) args[1] = assetIdHint;
            return _origDerived.apply(this, args);
        };
    }

    /* ======================================================================
     * F9 — load profiler (opt-in)
     * ==================================================================== */
    var LP = { on: false, logMs: 5, data: {}, t0: 0, timer: null };
    try {
        LP.on = /[?&]lp=1(&|$)/.test(W.location.search) ||
            (W.localStorage && W.localStorage.getItem('tlLoadProfile') === '1');
    } catch (e) { LP.on = false; }
    function lpNow() { return (W.performance && W.performance.now) ? W.performance.now() : Date.now(); }
    function lpReset() { LP.data = {}; LP.t0 = lpNow(); }
    function lpLog(name, ms) {
        if (!LP.t0) LP.t0 = lpNow();
        var e = LP.data[name] || (LP.data[name] = { n: 0, total: 0, max: 0 });
        e.n++; e.total += ms; if (ms > e.max) e.max = ms;
        if (ms >= LP.logMs) console.log('[LOAD] ' + name + '  ' + ms.toFixed(1) + 'ms  (at +' + (lpNow() - LP.t0).toFixed(0) + 'ms)');
    }
    function lpReport() {
        var rows = Object.keys(LP.data).map(function (k) {
            var e = LP.data[k];
            return { fn: k, calls: e.n, total_ms: +e.total.toFixed(1), avg_ms: +(e.total / e.n).toFixed(2), max_ms: +e.max.toFixed(1) };
        }).sort(function (a, b) { return b.total_ms - a.total_ms; });
        console.log('[LOAD] ===== load profile (slowest total first) =====');
        if (console.table) console.table(rows);
        return rows;
    }
    function lpWrap(name, isRoot) {
        var f = W[name];
        if (typeof f !== 'function' || f.__lp) return;
        var w = function () {
            if (isRoot) lpReset();
            var t = lpNow();
            try { return f.apply(this, arguments); }
            finally {
                lpLog(name, lpNow() - t);
                if (isRoot) { clearTimeout(LP.timer); LP.timer = setTimeout(lpReport, 6000); }
            }
        };
        w.__lp = true; w.__lpOrig = f;
        W[name] = w;
    }
    W.lpReport = lpReport;
    function lpInstall() {
        if (!LP.on) return;
        ['fnAdvSearch', '_tlAutoLoad'].forEach(function (n) { lpWrap(n, true); });
        ['loadBulkAssetMetadata', 'connectWebSocket', 'seedSignalSkeletons', 'renderRDPMSView',
            'buildNewRDPMSCards', 'updateRDPMSViewIncremental', 'renderWsTable', 'renderWsTableFixed',
            'renderPointMachineView', 'renderIpsGridView', 'fnBindTrackCards', 'computeSignalState',
            'getSignalThreshold', 'executeUIUpdate'].forEach(function (n) { lpWrap(n, false); });
        console.log('[LOAD] profiler on. Call lpReport() at any time; summary about 6 s after a load.');
    }

    /* ======================================================================
     * Init (DOM-dependent parts)
     * ==================================================================== */
    function init() {
        injectStyles();
        installHiddenAssetTypeFilter();
        installDownloadGuards();
        injectBar();
        hookBarEvents();
        updateSelInfoBar();
        setInterval(tickClock, 1000);
        lpInstall();
        if (W.console) console.log('[infra616] shared v616 features applied');
    }
    if (document.readyState === 'loading') $(init);
    else init();
})();
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-infra616" failed to load', e);
}


/* #############################################################################
 * SECTION: INTERACTIVE GRAPH VIEWS (Overlay / Stacked / Individual)
 * (was telemetrylive-graphview.js)
 * ########################################################################## */
try {
/* =====================================================================
 * TELEMETRY LIVE — INTERACTIVE GRAPH VIEWS  (v617 + ported v616 behaviour)
 *
 * Load AFTER telemetrylive.js, telemetrylive-graphs.js (and the *616 files).
 *
 * Adds a three-way view switch to every history graph on the page:
 *   Overlay     – the existing v617 chart, unchanged.
 *   Stacked     – NEW "lane board": one lane per attribute on a single
 *                 synchronised time axis, HTML lane gutter with a live
 *                 cursor read-out (no floating tooltip), min/max/avg per
 *                 lane, relay lanes shown as Pickup bands, lane density
 *                 S/M/L, hide / focus per lane.  (v616 used white lanes
 *                 with an axis per lane and an "Av" label — replaced.)
 *   Individual  – NEW small-multiples grid: one chart card per attribute
 *                 with its own stats, live value, expand, PNG and hide;
 *                 cursor + zoom optionally synchronised across cards.
 *
 * Surfaces wired:
 *   grph  – Graph view (fnBindGrph / _gLoad / _gRender) in #divTelemetryLive
 *   modal – per-asset Historical Graph overlay (fnGetAssetGraph /
 *           loadHistoryGraphData / renderHistoryChart)
 *   ips   – IPS History Graph (telemetrylive-graphs.js, ipsG / _ipsRender);
 *           its old v616-style Stacked mode is replaced by the lane board.
 *
 * Ported from v616 telemetrylive.js:
 *   - _gToLocalInput / _gSyncDateInputs / _gLoadWindow / _gLoadRange
 *     (10825-10885, 11438-11547): custom From/To range for the Graph view,
 *     future dates blocked (max refreshed every 30 s + clamp on change).
 *   - _gUpdatePanelValuesOnlyFromWebSocket / formatLivePanelValue
 *     (11599-11800): attribute panel values refresh in place from the
 *     WebSocket store, floored at 0.
 *
 * Every value is floored with tlZeroFloor (<= 0 -> 0).
 * No shared file is modified; everything is done by wrapping globals.
 * ===================================================================== */
(function (window, $) {
    'use strict';
    if (!$ || window.TLGV) return;

    var TLGV = window.TLGV = { version: '1.0.1', ctx: {} };

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------
    function zf(v) {
        if (typeof window.tlZeroFloor === 'function') return window.tlZeroFloor(v);
        var n = parseFloat(v); if (isNaN(n)) return v; return n <= 0 ? 0 : n;
    }
    function num(v) { var n = parseFloat(v); return isNaN(n) ? null : n; }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function pad(n) { return String(n).padStart(2, '0'); }
    var MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    function fmtAxis(ms, span) {
        var d = new Date(ms);
        var hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
        if (span > 30 * 3600000) return pad(d.getDate()) + ' ' + MON[d.getMonth()] + '\n' + hm;
        return hm;
    }
    function fmtFull(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + ' ' + MON[d.getMonth()] + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    function toLocalInput(ms) {
        var d = new Date(ms);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }
    function hexA(hex, a) {
        var h = String(hex || '').trim();
        if (h.indexOf('rgb') === 0) return h.replace(/rgba?\(([^)]+)\)/, function (m, inner) {
            var p = inner.split(','); return 'rgba(' + p[0] + ',' + p[1] + ',' + p[2] + ',' + a + ')';
        });
        h = h.replace('#', '');
        if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
        var r = parseInt(h.substr(0, 2), 16), g = parseInt(h.substr(2, 2), 16), b = parseInt(h.substr(4, 2), 16);
        if (isNaN(r)) return 'rgba(34,211,238,' + a + ')';
        return 'rgba(' + r + ',' + g + ',' + b + ',' + a + ')';
    }
    function warn(msg) {
        if (typeof window.showWarning === 'function') window.showWarning(msg, 'Graph');
        else if (window.console) console.warn('[TLGV]', msg);
    }
    function niceMax(v) {
        if (!(v > 0)) return 1;
        var m = v * 1.15, p = Math.pow(10, Math.floor(Math.log10(m))), f = m / p;
        var nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
        return nf * p;
    }
    function loadPrefs() {
        try { return JSON.parse(window.localStorage.getItem('tlgv.prefs.v1')) || {}; } catch (e) { return {}; }
    }
    function savePrefs() {
        try { window.localStorage.setItem('tlgv.prefs.v1', JSON.stringify(prefs)); } catch (e) { /* ignore */ }
    }
    var prefs = loadPrefs();
    prefs.mode = prefs.mode || {};
    prefs.density = prefs.density || 'm';
    prefs.cols = prefs.cols || 'auto';
    if (prefs.sync === undefined) prefs.sync = true;

    // value at time t (carry-forward) — pts sorted asc
    function valueAt(pts, t) {
        if (!pts || !pts.length || t < pts[0][0]) return null;
        var lo = 0, hi = pts.length - 1, idx = -1;
        while (lo <= hi) { var mid = (lo + hi) >> 1; if (pts[mid][0] <= t) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
        return idx >= 0 ? pts[idx][1] : null;
    }
    // collapse consecutive equal values (keeps first of run + final point)
    function compress(pts) {
        var out = [];
        for (var i = 0; i < pts.length; i++) {
            var p = pts[i];
            if (out.length && out[out.length - 1][0] === p[0]) { out[out.length - 1] = p; continue; }
            if (out.length > 1 && out[out.length - 1][1] === p[1] && out[out.length - 2][1] === p[1]) { out[out.length - 1] = p; continue; }
            out.push(p);
        }
        return out;
    }

    function computeStats(s, xMin, xMax) {
        var st = { count: s.actual.length, min: null, max: null, avg: null, last: null, lastT: null, pickups: 0, drops: 0, onPct: null };
        var pts = s.pts;
        if (!pts.length) return st;
        var lastA = s.actual.length ? s.actual[s.actual.length - 1] : pts[pts.length - 1];
        st.last = lastA[1]; st.lastT = lastA[0];
        var end = Math.min(xMax || Date.now(), Date.now());
        var start = (xMin != null) ? xMin : pts[0][0];
        if (s.kind === 'bin') {
            var prev = null, on = 0, tot = 0;
            for (var i = 0; i < pts.length; i++) {
                var v = pts[i][1] >= 0.5 ? 1 : 0;
                if (prev !== null && v !== prev) { if (v === 1) st.pickups++; else st.drops++; }
                prev = v;
                var t0 = Math.max(pts[i][0], start), t1 = Math.min(i + 1 < pts.length ? pts[i + 1][0] : end, end);
                if (t1 > t0) { tot += t1 - t0; if (v === 1) on += t1 - t0; }
            }
            st.onPct = tot > 0 ? (on * 100 / tot) : null;
            return st;
        }
        var src = s.actual.length ? s.actual : pts;
        var mn = Infinity, mx = -Infinity;
        for (var j = 0; j < src.length; j++) { var x = src[j][1]; if (x < mn) mn = x; if (x > mx) mx = x; }
        st.min = mn; st.max = mx;
        // time-weighted average across the visible window
        var area = 0, span = 0;
        for (var k = 0; k < pts.length; k++) {
            var a0 = Math.max(pts[k][0], start), a1 = Math.min(k + 1 < pts.length ? pts[k + 1][0] : end, end);
            if (a1 > a0) { area += pts[k][1] * (a1 - a0); span += a1 - a0; }
        }
        if (span > 0) st.avg = area / span;
        else { var sum = 0; src.forEach(function (p) { sum += p[1]; }); st.avg = sum / src.length; }
        return st;
    }
    function pickupBands(pts, end) {
        var bands = [], startOn = null;
        for (var i = 0; i < pts.length; i++) {
            var on = pts[i][1] >= 0.5;
            if (on && startOn === null) startOn = pts[i][0];
            if (!on && startOn !== null) { bands.push([startOn, pts[i][0]]); startOn = null; }
        }
        if (startOn !== null) bands.push([startOn, end]);
        return bands;
    }
    function unitFor(name, kind) {
        if (kind === 'bin') return '0/1';
        var n = String(name || '');
        var m = n.match(/\((mA|mV|V|Ω|A)\)\s*$/);
        if (m) return m[1];
        if (/\bmV\b/i.test(n)) return 'mV';
        if (/\bmA\b/i.test(n)) return 'mA';
        if (kind === 'ma') return 'mA';
        if (kind === 'v') return 'V';
        if (/\bV\b/.test(n)) return 'V';
        return '';
    }
    function kindLabel(kind) {
        return { ma: 'Current', v: 'Voltage', bin: 'Relay', drv: 'Derived', other: 'Value' }[kind] || 'Value';
    }
    function fmtVal(v, kind) {
        if (v === null || v === undefined || isNaN(v)) return '—';
        if (kind === 'bin') return v >= 0.5 ? 'Pickup' : 'Drop';
        return Number(zf(v)).toFixed(2);
    }
    function valHtml(v, kind, unit) {
        if (kind === 'bin') {
            if (v === null || v === undefined || isNaN(v)) return '<span class="tlgv-na">—</span>';
            return '<span class="tlgv-badge ' + (v >= 0.5 ? 'up' : 'dn') + '">' + (v >= 0.5 ? 'Pickup' : 'Drop') + '</span>';
        }
        return '<b>' + fmtVal(v, kind) + '</b>' + (unit && v !== null && v !== undefined ? '<i>' + esc(unit) + '</i>' : '');
    }

    // ------------------------------------------------------------------
    // model adapters  ->  { xMin, xMax, series:[{key,name,sub,color,kind,unit,pts,actual,live}] }
    // ------------------------------------------------------------------
    function finishSeries(list, xMin, xMax) {
        var end = Math.min(xMax != null ? xMax : Date.now(), Date.now());
        list.forEach(function (s) {
            s.pts = s.pts.filter(function (p) { return p && !isNaN(p[0]) && p[1] !== null && !isNaN(p[1]); })
                .map(function (p) { return [p[0], Number(zf(p[1]))]; })
                .sort(function (a, b) { return a[0] - b[0]; });
            s.actual = (s.actual || s.pts).map(function (p) { return [p[0], Number(zf(p[1]))]; });
            s.pts = compress(s.pts);
            if (s.pts.length && s.pts[s.pts.length - 1][0] < end) s.pts.push([end, s.pts[s.pts.length - 1][1]]);
            if (!s.unit) s.unit = unitFor(s.name, s.kind);
            s.stats = computeStats(s, xMin, xMax);
        });
        return { xMin: xMin, xMax: end, series: list.filter(function (s) { return s.pts.length; }) };
    }

    function fromGraphView() {
        var ch = window.mainGraphChart;
        if (!ch || !ch.getOption) return null;
        var opt = ch.getOption() || {};
        var defs = window._DERIVED_DEFS || [];
        var out = [];
        (opt.series || []).forEach(function (s, i) {
            var raw = String(s.name || ('Series ' + i));
            var isDL = raw.indexOf('[DL] ') === 0, isDrv = raw.indexOf('[D] ') === 0;
            var name = raw.replace(/^\[DL\] /, '').replace(/^\[D\] /, '');
            var kind = isDL ? 'bin' : isDrv ? 'drv' : (s.yAxisIndex === 0 ? 'ma' : 'v');
            var unit = '';
            if (isDrv) defs.forEach(function (d) { if (d.label === name) unit = d.unit; });
            var pts = [], actual = [];
            (s.data || []).forEach(function (d) {
                var v = (d && d.value) ? d.value : d;
                if (!v || v.length < 2) return;
                var p = [Number(v[0]), Number(v[1])];
                pts.push(p);
                if (isDL || (d && d.symbolSize > 0)) actual.push(p);
            });
            if (isDL) actual = compress(actual);
            out.push({
                key: raw, name: name, sub: '', color: (s.lineStyle && s.lineStyle.color) || (s.itemStyle && s.itemStyle.color) || '#22d3ee',
                kind: kind, unit: unit, pts: pts, actual: actual,
                live: { src: 'ws', aid: window._gA, name: name, dl: isDL }
            });
        });
        var xMin = (window._gReqStart != null) ? window._gReqStart : null;
        var xMax = (window._gReqEnd != null) ? window._gReqEnd : Date.now();
        return finishSeries(out, xMin, xMax);
    }

    function fromModal() {
        var store = window._rdpmsTooltipSeries || [];
        var ch = window.rdpmsGraphChart, opt = null;
        try { opt = ch && ch.getOption ? ch.getOption() : null; } catch (e) { opt = null; }
        var xa = opt && opt.xAxis ? (opt.xAxis[0] || opt.xAxis) : {};
        var dashed = {};
        if (opt && opt.series) opt.series.forEach(function (s) { if (s.lineStyle && s.lineStyle.type === 'dashed') dashed[s.name] = true; });
        var aid = $('#rdpmsGraphOverlay').data('assetId');
        var xMax = (typeof xa.max === 'number') ? xa.max : Date.now();
        var xMin = (typeof xa.min === 'number') ? xa.min : null;
        var endAnchor = Math.min(xMax, Date.now());
        var out = store.map(function (s) {
            var pts = (s.points || []).map(function (p) { return [Number(p[0]), Number(p[1])]; });
            var actual = pts.slice();
            if (actual.length > 1 && actual[actual.length - 1][0] >= endAnchor - 1000 &&
                actual[actual.length - 1][1] === actual[actual.length - 2][1]) actual.pop();
            var allBin = actual.length > 0 && actual.every(function (p) { return p[1] === 0 || p[1] === 1; });
            var looksAnalog = /\b(m?A|m?V)\b|\((mA|V|mV|Ω)\)/i.test(s.name || '');
            var kind = dashed[s.name] ? 'drv' : (allBin && !looksAnalog) ? 'bin' : (s.isVoltage ? 'v' : (/\bmA\b/i.test(s.name) ? 'ma' : 'other'));
            return {
                key: s.name, name: s.name, sub: '', color: s.color || '#22d3ee', kind: kind, unit: '',
                pts: pts, actual: actual, live: { src: 'ws', aid: aid, name: s.name, dl: kind === 'bin' }
            };
        });
        return finishSeries(out, xMin, xMax);
    }

    function fromIps() {
        var g = window.ipsG;
        if (!g || !g.series) return null;
        var out = [], seen = {};
        (g.order || []).forEach(function (key) {
            if (seen[key]) return; seen[key] = true;
            var s = g.series[key];
            if (!s || g.checked[key] === false || !s.points || !s.points.length) return;
            var label = (typeof window.ipsSeriesLabel === 'function') ? window.ipsSeriesLabel(s) : s.name;
            var pts = s.points.map(function (p) { return Array.isArray(p) ? [Number(p[0]), Number(p[1])] : [Number(p.time), Number(p.value)]; });
            var name = String(s.name || label);
            var kind = s.isBin ? 'bin' : (/^\s*I|\bmA\b|CURRENT/i.test(name) ? 'ma' : (/^\s*V|\bV\b|VOLT/i.test(name) ? 'v' : 'other'));
            out.push({
                key: key, name: label, sub: (label !== s.assetName && (g.assetIds || []).length > 1) ? '' : '',
                color: s.color || '#22d3ee', kind: kind, unit: (kind === 'bin' ? '0/1' : (/\bmA\b/i.test(name) ? 'mA' : '')),
                pts: pts, actual: pts.slice(0, Math.max(1, pts.length - 1)),
                live: { src: 'ips', aid: s.assetId, attrId: s.attrId, members: s.members || s.memberKeys || null }
            });
        });
        return finishSeries(out, g.start || null, g.end || Date.now());
    }

    // ------------------------------------------------------------------
    // live values from the WebSocket store
    // ------------------------------------------------------------------
    function liveIndex(aid) {
        var store = window.wsLiveData || {};
        var a = store[aid] || store[parseInt(aid, 10)];
        var idx = { byName: {}, byId: {}, dl: {} };
        if (!a) return idx;
        if (a.attrs) {
            Object.keys(a.attrs).forEach(function (k) {
                var at = a.attrs[k]; if (!at) return;
                var id = parseInt(at.AttrId || at.AssetAttributeId || 0, 10);
                var v = num(at.Value);
                idx.byName[k] = v;
                if (id) idx.byId[id] = v;
                try {
                    if (typeof window.getAttrDisplayNamePlain === 'function') {
                        var dn = window.getAttrDisplayNamePlain(k, id, aid);
                        if (dn) idx.byName[dn] = v;
                        var dn2 = window.getAttrDisplayNamePlain(k, id);
                        if (dn2) idx.byName[dn2] = v;
                    }
                } catch (e) { /* ignore */ }
            });
        }
        if (a.dlRelays) {
            Object.keys(a.dlRelays).forEach(function (k) {
                var r = a.dlRelays[k]; if (!r) return;
                idx.dl[k] = r.isPickup ? 1 : 0;
                if (r.displayName) idx.dl[r.displayName] = r.isPickup ? 1 : 0;
            });
        }
        return idx;
    }
    function liveValueFor(s, cache) {
        var L = s.live; if (!L) return null;
        var aid = L.aid; if (aid === undefined || aid === null) return null;
        var idx = cache[aid] || (cache[aid] = liveIndex(aid));
        if (L.src === 'ips') { var v = idx.byId[parseInt(L.attrId, 10)]; return v === undefined ? null : v; }
        if (L.dl) { var d = idx.dl[L.name]; return d === undefined ? null : d; }
        var x = idx.byName[L.name];
        return x === undefined ? null : x;
    }

    // ------------------------------------------------------------------
    // styles
    // ------------------------------------------------------------------
    function injectStyles() {
        if (document.getElementById('tlgv-styles')) return;
        var css = [
            '.tlgv-bar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;padding:8px 14px;background:var(--at-g1,rgba(255,255,255,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));font-family:Manrope,system-ui,sans-serif;}',
            '.tlgv-seg{display:inline-flex;background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:10px;padding:3px;gap:2px;}',
            '.tlgv-seg button{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:6px;padding:5px 12px;border-radius:7px;font-size:11.5px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));transition:background .18s,color .18s;}',
            '.tlgv-seg button:hover{color:var(--at-t1,#fff);background:var(--at-g2,rgba(255,255,255,.06));}',
            '.tlgv-seg button.on{background:linear-gradient(135deg,var(--at-brand,#22d3ee),var(--at-brand-deep,#0ea5b7));color:#04121c;box-shadow:0 0 14px var(--at-brand-glow,rgba(34,211,238,.45));}',
            '.tlgv-seg button:focus-visible{outline:2px solid var(--at-brand,#22d3ee);outline-offset:1px;}',
            '.tlgv-lbl{font-size:10px;font-weight:800;letter-spacing:.6px;text-transform:uppercase;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-grp{display:inline-flex;align-items:center;gap:6px;}',
            '.tlgv-mini button{padding:4px 9px;font-size:10.5px;}',
            '.tlgv-search{background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:8px;color:var(--at-t1,#fff);font-size:11.5px;padding:5px 10px;width:170px;outline:none;}',
            '.tlgv-search:focus{border-color:var(--at-brand,#22d3ee);}',
            '.tlgv-btn{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:5px;padding:5px 10px;border-radius:8px;font-size:11px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlgv-btn:hover{color:var(--at-brand,#22d3ee);border-color:rgba(34,211,238,.4);}',
            '.tlgv-chk{display:inline-flex;align-items:center;gap:5px;font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));cursor:pointer;margin:0;}',
            '.tlgv-chk input{accent-color:var(--at-brand,#22d3ee);margin:0;}',
            '.tlgv-spacer{flex:1;}',
            '.tlgv-alt-only,.tlgv-stk-only,.tlgv-ind-only{display:none!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-alt-only,.tlgv-bar[data-mode="individual"] .tlgv-alt-only{display:inline-flex!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-stk-only{display:inline-flex!important;}',
            '.tlgv-bar[data-mode="individual"] .tlgv-ind-only{display:inline-flex!important;}',
            '.tlgv-host{position:relative;width:100%;}',
            '.tlgv-empty{padding:48px 16px;text-align:center;color:var(--at-t3,rgba(255,255,255,.5));font-size:13px;}',
            /* stacked lane board */
            '.tlgv-cursor{display:flex;align-items:center;gap:10px;padding:6px 14px;font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.08));background:rgba(0,0,0,.18);}',
            '.tlgv-cursor b{font-family:"JetBrains Mono",monospace;color:var(--at-brand,#22d3ee);font-size:12px;}',
            '.tlgv-cursor .tlgv-live-pill{display:inline-flex;align-items:center;gap:5px;padding:1px 8px;border-radius:99px;background:rgba(52,211,153,.12);color:#34d399;font-weight:700;}',
            '.tlgv-stack{position:relative;width:100%;}',
            '.tlgv-stack-chart{position:absolute;inset:0;}',
            '.tlgv-gutter{position:absolute;left:0;top:0;bottom:0;pointer-events:none;}',
            '.tlgv-lane{position:absolute;left:8px;right:8px;display:flex;flex-direction:column;justify-content:center;gap:2px;padding:4px 8px 4px 12px;border-radius:10px;background:linear-gradient(90deg,rgba(255,255,255,.055),rgba(255,255,255,.015));border:1px solid var(--at-edge,rgba(255,255,255,.08));pointer-events:auto;overflow:hidden;box-sizing:border-box;transition:border-color .18s;}',
            '.tlgv-lane:hover{border-color:rgba(34,211,238,.35);}',
            '.tlgv-lane.tlgv-lane-active{border-color:var(--lane-c,#22d3ee);background:linear-gradient(90deg,rgba(34,211,238,.14),rgba(255,255,255,.02));}',
            '.tlgv-lane::before{content:"";position:absolute;left:0;top:0;bottom:0;width:4px;background:var(--lane-c,#22d3ee);box-shadow:0 0 10px var(--lane-c,#22d3ee);}',
            '.tlgv-lane-top{display:flex;align-items:center;gap:6px;min-width:0;}',
            '.tlgv-lane-name{flex:1;min-width:0;font-size:11.5px;font-weight:700;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-unit{flex-shrink:0;font-size:9px;font-weight:800;letter-spacing:.4px;padding:1px 5px;border-radius:4px;background:rgba(255,255,255,.08);color:var(--at-t2,rgba(255,255,255,.72));}',
            '.tlgv-lane-val{display:flex;align-items:baseline;gap:4px;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-lane-val b{font-size:16px;color:var(--lane-c,#22d3ee);font-weight:700;}',
            '.tlgv-lane-val i{font-style:normal;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-lane-sub{font-size:9.5px;color:var(--at-t3,rgba(255,255,255,.5));white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-lane-act{display:inline-flex;gap:2px;opacity:0;transition:opacity .15s;}',
            '.tlgv-lane:hover .tlgv-lane-act,.tlgv-card:hover .tlgv-lane-act,.tlgv-lane-act:focus-within{opacity:1;}',
            '.tlgv-ico{all:unset;cursor:pointer;width:20px;height:20px;display:inline-flex;align-items:center;justify-content:center;border-radius:5px;color:var(--at-t3,rgba(255,255,255,.5));font-size:10px;}',
            '.tlgv-ico:hover{background:rgba(34,211,238,.15);color:var(--at-brand,#22d3ee);}',
            '.tlgv-ico:focus-visible{outline:2px solid var(--at-brand,#22d3ee);}',
            '.tlgv-dens-s .tlgv-lane-sub{display:none;}',
            '.tlgv-dens-s .tlgv-lane{flex-direction:row;align-items:center;justify-content:space-between;}',
            '.tlgv-dens-s .tlgv-lane-val b{font-size:13px;}',
            '.tlgv-badge{display:inline-block;padding:1px 8px;border-radius:5px;font-size:10px;font-weight:800;color:#04121c;font-family:Manrope,sans-serif;}',
            '.tlgv-badge.up{background:#34d399;}.tlgv-badge.dn{background:#fbbf24;}',
            '.tlgv-na{color:var(--at-t4,rgba(255,255,255,.34));}',
            '.tlgv-flash{animation:tlgvFlash 1s ease-out;}',
            '@keyframes tlgvFlash{0%{text-shadow:0 0 10px currentColor;}100%{text-shadow:none;}}',
            /* individual grid */
            '.tlgv-grid{display:grid;gap:12px;padding:12px;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));}',
            '.tlgv-grid.c1{grid-template-columns:1fr;}.tlgv-grid.c2{grid-template-columns:repeat(2,minmax(0,1fr));}.tlgv-grid.c3{grid-template-columns:repeat(3,minmax(0,1fr));}',
            '.tlgv-card{position:relative;display:flex;flex-direction:column;border-radius:14px;background:linear-gradient(180deg,rgba(255,255,255,.06),rgba(255,255,255,.02));border:1px solid var(--at-edge,rgba(255,255,255,.10));overflow:hidden;min-width:0;transition:border-color .18s,box-shadow .18s;}',
            '.tlgv-card:hover{border-color:rgba(34,211,238,.35);box-shadow:0 8px 26px rgba(0,0,0,.35);}',
            '.tlgv-card::before{content:"";position:absolute;left:0;right:0;top:0;height:3px;background:var(--lane-c,#22d3ee);}',
            '.tlgv-card.is-max{grid-column:1/-1;}',
            '.tlgv-card.is-flash{box-shadow:0 0 0 2px var(--at-brand,#22d3ee);}',
            '.tlgv-card-head{display:flex;align-items:center;gap:8px;padding:10px 12px 4px;min-width:0;}',
            '.tlgv-card-dot{width:10px;height:10px;border-radius:50%;background:var(--lane-c,#22d3ee);box-shadow:0 0 8px var(--lane-c,#22d3ee);flex-shrink:0;}',
            '.tlgv-card-title{flex:1;min-width:0;}',
            '.tlgv-card-title b{display:block;font-size:12.5px;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-card-title small{display:block;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-card-live{display:flex;align-items:baseline;gap:4px;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-card-live b{font-size:18px;color:var(--lane-c,#22d3ee);}',
            '.tlgv-card-live i{font-style:normal;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-dot-live{width:7px;height:7px;border-radius:50%;background:#34d399;box-shadow:0 0 6px #34d399;animation:tlgvPulse 2s infinite;align-self:center;}',
            '.tlgv-dot-live.off{background:var(--at-t4,rgba(255,255,255,.34));box-shadow:none;animation:none;}',
            '@keyframes tlgvPulse{0%,100%{opacity:1;}50%{opacity:.35;}}',
            '.tlgv-card-stats{display:flex;flex-wrap:wrap;gap:4px 12px;padding:2px 12px 6px;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));font-family:"JetBrains Mono",monospace;}',
            '.tlgv-card-stats span b{color:var(--at-t1,#fff);font-weight:600;}',
            '.tlgv-card-chart{height:180px;width:100%;}',
            '.tlgv-card.is-max .tlgv-card-chart{height:380px;}',
            '.tlgv-hiddenlist{display:inline-flex;}',
            '@media (max-width:700px){.tlgv-grid,.tlgv-grid.c2,.tlgv-grid.c3{grid-template-columns:1fr;padding:8px;}.tlgv-search{width:120px;}.tlgv-lane-sub{display:none;}.tlgv-lane-val b{font-size:12px;}}',
            '@media (prefers-reduced-motion:reduce){.tlgv-dot-live,.tlgv-flash{animation:none;}}'
        ].join('\n');
        var st = document.createElement('style');
        st.id = 'tlgv-styles';
        st.appendChild(document.createTextNode(css));
        document.head.appendChild(st);
    }

    // ------------------------------------------------------------------
    // context registry
    // ------------------------------------------------------------------
    function makeCtx(id, cfg) {
        var c = TLGV.ctx[id] || { id: id, charts: [], hidden: {}, search: '', model: null, cursor: null };
        c.cfg = cfg;
        c.group = 'tlgv-' + id;
        TLGV.ctx[id] = c;
        return c;
    }
    function getMode(id) {
        var c = TLGV.ctx[id];
        if (c && c.cfg && c.cfg.getMode) return c.cfg.getMode();
        return prefs.mode[id] || 'overlay';
    }
    function setMode(id, mode) {
        var c = TLGV.ctx[id];
        if (c && c.cfg && c.cfg.setMode) c.cfg.setMode(mode);
        else { prefs.mode[id] = mode; savePrefs(); }
    }
    function disposeCharts(c) {
        (c.charts || []).forEach(function (ch) { try { ch.dispose(); } catch (e) { /* ignore */ } });
        c.charts = [];
        if (c.io) { try { c.io.disconnect(); } catch (e) { } c.io = null; }
        if (c.ro) { try { c.ro.disconnect(); } catch (e) { } c.ro = null; }
    }

    function barHtml(id, extraLeft) {
        var m = getMode(id);
        var hiddenN = Object.keys((TLGV.ctx[id] || {}).hidden || {}).length;
        return '<div class="tlgv-bar" data-tlgv="' + id + '" data-mode="' + m + '">' +
            '<span class="tlgv-lbl">View</span>' +
            '<div class="tlgv-seg" role="group" aria-label="Graph view">' +
            '<button type="button" data-tlgv-mode="overlay" class="' + (m === 'overlay' ? 'on' : '') + '" aria-pressed="' + (m === 'overlay') + '" title="All attributes on one chart"><i class="fas fa-layer-group"></i> Overlay</button>' +
            '<button type="button" data-tlgv-mode="stacked" class="' + (m === 'stacked' ? 'on' : '') + '" aria-pressed="' + (m === 'stacked') + '" title="One lane per attribute on a shared time axis"><i class="fas fa-bars-staggered"></i> Stacked</button>' +
            '<button type="button" data-tlgv-mode="individual" class="' + (m === 'individual' ? 'on' : '') + '" aria-pressed="' + (m === 'individual') + '" title="A separate chart for every attribute"><i class="fas fa-table-cells-large"></i> Individual</button>' +
            '</div>' +
            (extraLeft || '') +
            '<span class="tlgv-grp tlgv-stk-only"><span class="tlgv-lbl">Lanes</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Lane height">' +
            ['s', 'm', 'l'].map(function (d) { return '<button type="button" data-tlgv-dens="' + d + '" class="' + (prefs.density === d ? 'on' : '') + '">' + d.toUpperCase() + '</button>'; }).join('') +
            '</div></span>' +
            '<span class="tlgv-grp tlgv-ind-only"><span class="tlgv-lbl">Columns</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Columns">' +
            ['auto', '1', '2', '3'].map(function (d) { return '<button type="button" data-tlgv-cols="' + d + '" class="' + (String(prefs.cols) === d ? 'on' : '') + '">' + (d === 'auto' ? 'Auto' : d) + '</button>'; }).join('') +
            '</div></span>' +
            '<span class="tlgv-spacer"></span>' +
            '<span class="tlgv-grp tlgv-alt-only"><input type="search" class="tlgv-search" data-tlgv-search placeholder="Filter attributes…" aria-label="Filter attributes" value="' + esc((TLGV.ctx[id] || {}).search || '') + '"/></span>' +
            '<label class="tlgv-chk tlgv-alt-only" title="Zoom / pan all charts together (hover stays on one attribute)"><input type="checkbox" data-tlgv-sync ' + (prefs.sync ? 'checked' : '') + '/> Sync zoom</label>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-unhide style="' + (hiddenN ? '' : 'display:none!important') + '"><i class="fas fa-eye"></i> Show hidden (<span data-tlgv-hn>' + hiddenN + '</span>)</button>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-reset><i class="fas fa-undo"></i> Reset zoom</button>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-png><i class="fas fa-download"></i> PNG</button>' +
            '</div>';
    }
    function syncBar(id) {
        var m = getMode(id);
        var $bar = $('.tlgv-bar[data-tlgv="' + id + '"]');
        $bar.attr('data-mode', m);
        $bar.find('[data-tlgv-mode]').each(function () {
            var on = $(this).attr('data-tlgv-mode') === m;
            $(this).toggleClass('on', on).attr('aria-pressed', on ? 'true' : 'false');
        });
        var hn = Object.keys((TLGV.ctx[id] || {}).hidden || {}).length;
        $bar.find('[data-tlgv-hn]').text(hn);
        $bar.find('[data-tlgv-unhide]').attr('style', hn ? '' : 'display:none!important');
    }

    // ------------------------------------------------------------------
    // apply: decide overlay vs alternative view for a context
    // ------------------------------------------------------------------
    function apply(id, rebuildModel) {
        var c = TLGV.ctx[id]; if (!c || !c.cfg) return;
        var host = c.cfg.host(), overlay = c.cfg.overlay();
        if (!host || !host.length) return;
        syncBar(id);
        var m = getMode(id);
        if (m === 'overlay') {
            disposeCharts(c);
            host.empty().hide();
            if (c.cfg.showOverlay) c.cfg.showOverlay(true);
            else if (overlay) overlay.show();
            if (c.cfg.overlayChart) { var oc = c.cfg.overlayChart(); if (oc) setTimeout(function () { try { oc.resize(); } catch (e) { } }, 30); }
            return;
        }
        if (!window.echarts) return;
        if (rebuildModel || !c.model) c.model = c.cfg.model();
        if (!c.model) { host.hide(); return; }
        if (c.cfg.showOverlay) c.cfg.showOverlay(false);
        else if (overlay) overlay.hide();
        host.show();
        if (m === 'stacked') renderStacked(c, host);
        else renderIndividual(c, host);
        ensureLiveTimer();
    }
    TLGV.apply = apply;

    function visibleSeries(c) {
        var q = String(c.search || '').trim().toLowerCase();
        return c.model.series.filter(function (s) {
            if (c.hidden[s.key]) return false;
            if (q && String(s.name).toLowerCase().indexOf(q) < 0) return false;
            return true;
        });
    }

    function tooltipBase() {
        return {
            backgroundColor: 'rgba(5,9,24,0.97)', borderColor: 'rgba(255,255,255,0.12)', borderWidth: 1,
            padding: [8, 12], textStyle: { color: 'rgba(255,255,255,0.9)', fontSize: 12 },
            extraCssText: 'box-shadow:0 8px 32px rgba(0,0,0,.5);border-radius:10px;'
        };
    }
    function seriesFor(s, xi, yi, end, dens) {
        var isBin = s.kind === 'bin';
        var o = {
            name: s.name, type: 'line', step: 'end', xAxisIndex: xi, yAxisIndex: yi,
            showSymbol: !isBin && s.actual.length <= 40, symbol: 'circle', symbolSize: 4,
            connectNulls: true, animation: false,
            lineStyle: { width: isBin ? 1.4 : 1.8, color: s.color, type: s.kind === 'drv' ? 'dashed' : 'solid' },
            itemStyle: { color: s.color },
            areaStyle: {
                color: {
                    type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
                    colorStops: [{ offset: 0, color: hexA(s.color, isBin ? 0.30 : 0.32) }, { offset: 1, color: hexA(s.color, 0.02) }]
                }
            },
            emphasis: { disabled: true },
            data: s.pts
        };
        if (isBin) {
            o.markArea = {
                silent: true,
                itemStyle: { color: 'rgba(52,211,153,0.10)' },
                data: pickupBands(s.pts, end).map(function (b) { return [{ xAxis: b[0] }, { xAxis: b[1] }]; })
            };
        } else if (s.stats && s.stats.avg !== null && dens !== 's') {
            o.markLine = {
                silent: true, symbol: 'none', animation: false,
                lineStyle: { color: s.color, type: 'dotted', width: 1, opacity: 0.6 },
                label: { show: false },
                data: [{ yAxis: Number(zf(s.stats.avg)) }]
            };
        }
        return o;
    }
    function statsLine(s) {
        var st = s.stats || {};
        if (s.kind === 'bin') {
            return '↑ ' + st.pickups + ' pickups · ↓ ' + st.drops + ' drops' + (st.onPct !== null ? ' · ' + st.onPct.toFixed(0) + '% up' : '');
        }
        if (st.min === null) return 'no samples';
        return 'min ' + fmtVal(st.min) + ' · max ' + fmtVal(st.max) + ' · avg ' + fmtVal(st.avg);
    }

    // ------------------------------------------------------------------
    // STACKED — lane board
    // ------------------------------------------------------------------
    var DENS = { s: 46, m: 78, l: 124 };
    function renderStacked(c, host) {
        disposeCharts(c);
        var list = visibleSeries(c);
        var model = c.model;
        if (!list.length) {
            host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden lanes.</div>');
            return;
        }
        var narrow = (host.width() || 900) < 640;
        var GW = narrow ? 132 : 210;
        var laneH = DENS[prefs.density] || DENS.m, gap = 12, top = 10, bottom = 64;
        var H = top + list.length * laneH + (list.length - 1) * gap + bottom;
        var span = (model.xMax || Date.now()) - (model.xMin || (list[0].pts[0] || [Date.now()])[0]);

        var lanes = '';
        list.forEach(function (s, i) {
            var y = top + i * (laneH + gap);
            lanes += '<div class="tlgv-lane" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + ';top:' + y + 'px;height:' + laneH + 'px;width:' + (GW - 16) + 'px;" title="' + esc(s.name) + ' — ' + kindLabel(s.kind) + '">' +
                '<div class="tlgv-lane-top"><span class="tlgv-lane-name">' + esc(s.name) + '</span>' +
                (s.unit ? '<span class="tlgv-unit">' + esc(s.unit) + '</span>' : '') +
                '<span class="tlgv-lane-act">' +
                '<button type="button" class="tlgv-ico" data-tlgv-focus="' + esc(s.key) + '" title="Open as individual chart" aria-label="Open ' + esc(s.name) + ' as individual chart"><i class="fas fa-up-right-and-down-left-from-center"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide lane" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                '</span></div>' +
                '<div class="tlgv-lane-val" data-tlgv-val>' + valHtml(s.stats.last, s.kind, s.unit) + '</div>' +
                '<div class="tlgv-lane-sub">' + esc(statsLine(s)) + '</div>' +
                '</div>';
        });
        host.html(
            '<div class="tlgv-cursor" data-tlgv-cursor><i class="fas fa-crosshairs"></i> <span data-tlgv-cursor-txt><span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span></span>' +
            '<span class="tlgv-spacer"></span><span>' + list.length + ' lane' + (list.length > 1 ? 's' : '') + ' · hover a lane to read that lane \u00b7 drag to pan \u00b7 Ctrl+wheel to zoom</span></div>' +
            '<div class="tlgv-stack tlgv-dens-' + prefs.density + '" style="height:' + H + 'px"><div class="tlgv-stack-chart"></div><div class="tlgv-gutter" style="width:' + GW + 'px">' + lanes + '</div></div>'
        );
        var el = host.find('.tlgv-stack-chart')[0];
        var chart = window.echarts.init(el, null, { renderer: 'canvas' });
        c.charts.push(chart);

        var grids = [], xs = [], ys = [], series = [], titles = [], allX = [];
        var left = GW + 44, right = 18;
        list.forEach(function (s, i) {
            var y = top + i * (laneH + gap), last = i === list.length - 1, isBin = s.kind === 'bin';
            grids.push({
                left: left, right: right, top: y, height: laneH, show: true,
                backgroundColor: i % 2 ? 'rgba(255,255,255,0.018)' : 'rgba(255,255,255,0.036)',
                borderColor: 'rgba(255,255,255,0.06)', borderWidth: 1
            });
            xs.push({
                type: 'time', gridIndex: i, min: model.xMin, max: model.xMax, boundaryGap: false,
                axisLine: { show: last, lineStyle: { color: 'rgba(255,255,255,0.18)' } },
                axisTick: { show: last }, splitNumber: narrow ? 4 : 8,
                axisLabel: { show: last, color: 'rgba(255,255,255,0.5)', fontSize: 10, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } },
                splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.045)' } },
                // pointer is drawn only in the lane under the mouse (no cross-lane link)
                axisPointer: { label: { show: false } }   // time + lane name are shown in the cursor header
            });
            var yMax = isBin ? 1 : niceMax(s.stats.max);
            ys.push({
                type: 'value', gridIndex: i, min: 0, max: yMax, splitNumber: prefs.density === 'l' ? 3 : 1,
                axisLine: { show: false }, axisTick: { show: false },
                axisLabel: {
                    color: hexA(s.color, 0.85), fontSize: 9, fontFamily: 'JetBrains Mono,monospace', margin: 6,
                    showMinLabel: false,   // "0" of one lane would collide with the max label of the next
                    formatter: isBin ? function (v) { return v === 1 ? 'P' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); }
                },
                splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.05)', type: 'dashed' } },
                axisPointer: { show: false }
            });
            series.push(seriesFor(s, i, i, model.xMax, prefs.density));
            titles.push({ show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: y + 4, textStyle: { color: s.color, fontSize: 11, fontWeight: 700 } });
            allX.push(i);
        });
        chart.setOption({
            backgroundColor: 'transparent', animation: false,
            title: titles,
            grid: grids, xAxis: xs, yAxis: ys, series: series,
            tooltip: { trigger: 'axis', showContent: false, axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.75)', width: 1, type: 'dashed' } } },
            axisPointer: { link: [], snap: false },
            dataZoom: [
                { type: 'inside', xAxisIndex: allX, filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, moveOnMouseMove: true, minValueSpan: 60000 },
                {
                    type: 'slider', xAxisIndex: allX, filterMode: 'none', height: 20, bottom: 6, left: left, right: right, minValueSpan: 60000,
                    backgroundColor: 'rgba(255,255,255,0.03)', borderColor: 'rgba(255,255,255,0.09)', fillerColor: 'rgba(34,211,238,0.12)',
                    handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, moveHandleStyle: { color: 'rgba(34,211,238,.5)' },
                    textStyle: { color: 'rgba(255,255,255,0.45)', fontSize: 10 }, labelFormatter: function (v) { return fmtFull(v); },
                    dataBackground: { lineStyle: { color: 'rgba(255,255,255,0.15)' }, areaStyle: { color: 'rgba(255,255,255,0.04)' } }
                }
            ]
        }, true);

        var readouts = {}, $lanes = {};
        host.find('.tlgv-lane').each(function () {
            var k = $(this).attr('data-key');
            $lanes[k] = $(this);
            readouts[k] = $(this).find('[data-tlgv-val]');
        });
        var $cur = host.find('[data-tlgv-cursor-txt]');
        var raf = 0, pending = null;
        c.cursor = null; c.cursorKey = null;
        // Only the hovered lane follows the cursor; every other lane keeps its latest/live value.
        function paint(p) {
            var prevKey = c.cursorKey;
            if (!p) {
                c.cursor = null; c.cursorKey = null;
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                $cur.html('<span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span>');
                updateLive(c, true);
                return;
            }
            var s = list[p.idx]; if (!s) return;
            c.cursor = p.t; c.cursorKey = s.key;
            if (prevKey !== s.key) {
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                if ($lanes[s.key]) $lanes[s.key].addClass('tlgv-lane-active');
                if (prevKey !== null) updateLive(c, true);   // restore the lane we just left
            }
            $cur.html('<b>' + fmtFull(p.t) + '</b> <span style="color:' + esc(s.color) + ';font-weight:700;margin-left:6px;">' + esc(s.name) + '</span>');
            var cw = tlgvChangeWindow(s.pts, p.t);
            if (readouts[s.key]) readouts[s.key].html(valHtml(valueAt(s.pts, p.t), s.kind, s.unit) +
                (cw ? '<div class="tlgv-since">since ' + esc(fmtFull(cw.from)) + (cw.to ? ' &rarr; ' + esc(fmtFull(cw.to)) : ' &rarr; now') + '</div>' : ''));
            if (cw) $cur.append(' <span class="tlgv-since-bar">held since <b>' + esc(fmtFull(cw.from)) + '</b>' + (cw.to ? ' until <b>' + esc(fmtFull(cw.to)) + '</b>' : ' (still)') + '</span>');
        }
        function schedule(p) {
            pending = p;
            if (!raf) raf = requestAnimationFrame(function () { raf = 0; paint(pending); });
        }
        chart.on('updateAxisPointer', function (e) {
            var info = (e && e.axesInfo) || [];
            var hit = null;
            for (var i = 0; i < info.length; i++) {
                if (info[i].axisDim === 'x') { hit = info[i]; break; }
            }
            if (!hit) { schedule(null); return; }
            var t = Number(hit.value);
            if (isNaN(t)) return;
            schedule({ t: t, idx: hit.axisIndex });
        });
        chart.getZr().on('globalout', function () { schedule(null); });
        c.exportPng = function () {
            chart.setOption({ title: titles.map(function (t) { return $.extend({}, t, { show: true }); }) });
            var url = chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            chart.setOption({ title: titles });
            download(url, 'telemetry_stacked.png');
        };
        c.resetZoom = function () { chart.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); };
        observeResize(c, host[0]);
        if (c.cfg.onRendered) c.cfg.onRendered(chart);
    }

    // ------------------------------------------------------------------
    // INDIVIDUAL — one chart card per attribute
    // ------------------------------------------------------------------
    function renderIndividual(c, host) {
        disposeCharts(c);
        var list = visibleSeries(c);
        var model = c.model;
        if (!list.length) {
            host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden charts.</div>');
            return;
        }
        var span = (model.xMax || Date.now()) - (model.xMin || list[0].pts[0][0]);
        var cols = prefs.cols === 'auto' ? '' : ' c' + prefs.cols;
        var h = '<div class="tlgv-grid' + cols + '">';
        list.forEach(function (s) {
            var st = s.stats || {};
            var stats = s.kind === 'bin'
                ? '<span>Pickups <b>' + st.pickups + '</b></span><span>Drops <b>' + st.drops + '</b></span><span>Up <b>' + (st.onPct !== null ? st.onPct.toFixed(0) + '%' : '—') + '</b></span><span>Events <b>' + st.count + '</b></span>'
                : '<span>Min <b>' + fmtVal(st.min) + '</b></span><span>Max <b>' + fmtVal(st.max) + '</b></span><span>Avg <b>' + fmtVal(st.avg) + '</b></span><span>Samples <b>' + st.count + '</b></span>';
            h += '<div class="tlgv-card" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + '">' +
                '<div class="tlgv-card-head"><span class="tlgv-card-dot"></span>' +
                '<div class="tlgv-card-title"><b title="' + esc(s.name) + '">' + esc(s.name) + '</b><small>' + kindLabel(s.kind) + (s.unit ? ' · ' + esc(s.unit) : '') + '</small></div>' +
                '<div class="tlgv-card-live" title="Live value (WebSocket) or last sample"><span class="tlgv-dot-live off" data-tlgv-dot></span><span data-tlgv-val>' + valHtml(st.last, s.kind, s.unit) + '</span></div>' +
                '<span class="tlgv-lane-act">' +
                '<button type="button" class="tlgv-ico" data-tlgv-max title="Expand / collapse" aria-label="Expand ' + esc(s.name) + '"><i class="fas fa-expand"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-cardpng title="Save PNG" aria-label="Save ' + esc(s.name) + ' as PNG"><i class="fas fa-download"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide chart" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                '</span></div>' +
                '<div class="tlgv-card-stats">' + stats + '</div>' +
                '<div class="tlgv-card-chart" role="img" aria-label="' + esc(s.name) + ' history chart"></div></div>';
        });
        h += '</div>';
        host.html(h);

        var byKey = {};
        list.forEach(function (s) { byKey[s.key] = s; });
        c.cardCharts = {};

        function initCard(card) {
            if (card.__tlgvInit) return;
            card.__tlgvInit = true;
            var s = byKey[card.getAttribute('data-key')]; if (!s) return;
            var el = card.querySelector('.tlgv-card-chart');
            var ch = window.echarts.init(el, null, { renderer: 'canvas' });
            var isBin = s.kind === 'bin';
            var tt = tooltipBase();
            tt.trigger = 'axis';
            tt.axisPointer = { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.7)', type: 'dashed' } };
            tt.formatter = function (params) {
                if (!params || !params.length) return '';
                var t = params[0].axisValue != null ? Number(params[0].axisValue) : Number(params[0].value[0]);
                var v = valueAt(s.pts, t);
                return '<div style="font-family:JetBrains Mono,monospace;color:#22d3ee;font-weight:700;margin-bottom:4px;">' + fmtFull(t) + '</div>' +
                    '<div style="display:flex;align-items:center;gap:8px;"><span style="width:9px;height:9px;border-radius:50%;background:' + s.color + '"></span>' +
                    '<span style="color:rgba(255,255,255,.75)">' + esc(s.name) + '</span><b style="margin-left:10px;color:#fff;">' + fmtVal(v, s.kind) + (isBin || v === null ? '' : ' ' + esc(s.unit)) + '</b></div>';
            };
            ch.setOption({
                backgroundColor: 'transparent', animation: false,
                title: { show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: 2, textStyle: { color: s.color, fontSize: 12 } },
                grid: { left: 46, right: 14, top: 12, bottom: 28 },
                tooltip: tt,
                xAxis: {
                    type: 'time', min: model.xMin, max: model.xMax, splitNumber: 4,
                    axisLabel: { color: 'rgba(255,255,255,0.45)', fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } },
                    axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                    splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.045)' } }
                },
                yAxis: {
                    type: 'value', min: 0, max: isBin ? 1 : niceMax(s.stats.max), splitNumber: isBin ? 1 : 3,
                    axisLabel: {
                        color: 'rgba(255,255,255,0.45)', fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace',
                        formatter: isBin ? function (v) { return v === 1 ? 'Pickup' : v === 0 ? 'Drop' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); }
                    },
                    splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)', type: 'dashed' } }
                },
                dataZoom: [{ type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseMove: true, minValueSpan: 60000 }],
                series: [seriesFor(s, 0, 0, model.xMax, 'm')]
            }, true);
            // Hover/tooltip stays inside this card. "Sync zoom" only mirrors zoom/pan to the other cards.
            ch.on('datazoom', function () {
                if (!prefs.sync || c.zoomSyncing) return;
                var dz = (ch.getOption().dataZoom || [])[0];
                if (!dz) return;
                c.zoomSyncing = true;
                try {
                    c.charts.forEach(function (o) {
                        if (o !== ch && !o.isDisposed()) o.dispatchAction({ type: 'dataZoom', start: dz.start, end: dz.end });
                    });
                } finally { c.zoomSyncing = false; }
            });
            if (prefs.sync && c.charts.length) {
                var dz0 = (c.charts[0].getOption().dataZoom || [])[0];
                if (dz0 && (dz0.start > 0 || dz0.end < 100)) ch.dispatchAction({ type: 'dataZoom', start: dz0.start, end: dz0.end });
            }
            c.charts.push(ch);
            c.cardCharts[s.key] = ch;
        }

        var cards = host.find('.tlgv-card').toArray();
        if ('IntersectionObserver' in window && cards.length > 8) {
            c.io = new IntersectionObserver(function (entries) {
                entries.forEach(function (en) { if (en.isIntersecting) { initCard(en.target); c.io && c.io.unobserve(en.target); } });
            }, { rootMargin: '200px' });
            cards.forEach(function (cd, i) { if (i < 6) initCard(cd); else c.io.observe(cd); });
        } else {
            cards.forEach(initCard);
        }

        c.exportPng = function () {
            // make sure every card is drawn, then compose one image
            cards.forEach(initCard);
            var first = c.charts[0]; if (!first) return;
            c.charts.forEach(function (ch) { ch.group = c.group; ch.setOption({ title: { show: true }, grid: { top: 26 } }); });
            window.echarts.connect(c.group);
            var url = first.getConnectedDataURL ? first.getConnectedDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' }) : first.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            c.charts.forEach(function (ch) { ch.setOption({ title: { show: false }, grid: { top: 12 } }); ch.group = ''; });
            window.echarts.disconnect(c.group);   // connected only while composing the image, never for hover
            download(url, 'telemetry_individual.png');
        };
        c.resetZoom = function () { c.charts.forEach(function (ch) { ch.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); }); };
        observeResize(c, host[0]);
        updateLive(c, true);
    }

    function download(url, name) {
        var a = document.createElement('a'); a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
    }
    function observeResize(c, el) {
        if (!('ResizeObserver' in window)) return;
        var t = 0;
        c.ro = new ResizeObserver(function () {
            clearTimeout(t);
            t = setTimeout(function () { (c.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); }, 60);
        });
        c.ro.observe(el);
    }

    // ------------------------------------------------------------------
    // live refresh (values only, 2 s)
    // ------------------------------------------------------------------
    function updateLive(c, force) {
        if (!c || !c.model || !c.cfg) return;
        var host = c.cfg.host(); if (!host || !host.is(':visible')) return;
        var cache = {};
        var m = getMode(c.id);
        c.model.series.forEach(function (s) {
            var lv = liveValueFor(s, cache);
            var isLive = lv !== null && lv !== undefined;
            var v = isLive ? lv : s.stats.last;
            var sel = '[data-key="' + (window.CSS && CSS.escape ? CSS.escape(s.key) : s.key.replace(/"/g, '\\"')) + '"]';
            var $item = host.find((m === 'stacked' ? '.tlgv-lane' : '.tlgv-card') + sel);
            if (!$item.length) return;
            if (m === 'stacked' && c.cursorKey && c.cursorKey === s.key) return;   // hovered lane shows the cursor value
            var $val = $item.find('[data-tlgv-val]');
            var html = valHtml(v, s.kind, s.unit);
            if ($val.html() !== html) {
                $val.html(html);
                if (!force) { $val.removeClass('tlgv-flash'); void $val[0].offsetWidth; $val.addClass('tlgv-flash'); }
            }
            $item.find('[data-tlgv-dot]').toggleClass('off', !isLive);
        });
    }
    var liveTimer = null;
    function ensureLiveTimer() {
        if (liveTimer) return;
        liveTimer = setInterval(function () {
            var any = false;
            Object.keys(TLGV.ctx).forEach(function (id) {
                var c = TLGV.ctx[id];
                if (!c.cfg) return;
                var host = c.cfg.host();
                if (!host || !host.length || !document.body.contains(host[0])) return;
                any = true;
                if (getMode(id) !== 'overlay') updateLive(c, false);
            });
            if (window._gA && document.getElementById('gWrap')) { any = true; updateGraphPanelValues(String(window._gA)); }
            if (!any) { clearInterval(liveTimer); liveTimer = null; }
        }, 2000);
    }
    TLGV.updateLive = function (id) { if (TLGV.ctx[id]) updateLive(TLGV.ctx[id], true); };

    // v616 _gUpdatePanelValuesOnlyFromWebSocket — values only, zero floored
    function updateGraphPanelValues(aid) {
        if (!aid || String(window._gA || '') !== aid || !document.getElementById('gWrap')) return;
        var idx = liveIndex(aid);
        $('#gFL .gAttrRow2, #gRL .gAttrRow2').each(function () {
            var key = $(this).find('input[type=checkbox]').data('key');
            if (key === undefined) return;
            var v = idx.byName[key];
            if (v === undefined || v === null) return;
            var txt = Number(zf(v)).toFixed(2);
            var $v = $(this).find('.gAttrVal2');
            if ($v.text() !== txt) { $v.text(txt); $v.removeClass('tlgv-flash'); void $v[0].offsetWidth; $v.addClass('tlgv-flash'); }
        });
        $('#gDL .gAttrRow2').each(function () {
            var key = String($(this).find('input[type=checkbox]').data('key') || '');
            if (key.indexOf('DL:') !== 0) return;
            var st = idx.dl[key.substring(3)];
            if (st === undefined) return;
            var $b = $(this).find('.gDlBadge2');
            var want = st ? 'pickup' : 'drop';
            if (!$b.hasClass(want)) $b.removeClass('pickup drop').addClass(want).text(st ? 'Pickup' : 'Drop');
        });
        if (window._gIsTrack && typeof window.calculateDerivedValues === 'function') {
            var store = window.wsLiveData || {};
            var a = store[aid];
            if (a && a.attrs && Object.keys(a.attrs).length) {
                var dv = {};
                try { dv = window.calculateDerivedValues(a.attrs) || {}; } catch (e) { dv = {}; }
                $('#gDerived .gDerivedRow').each(function () {
                    var key = String($(this).find('input[type=checkbox]').data('key') || '').replace(/^DRV:/, '');
                    var v = dv[key];
                    if (v === undefined || v === null || isNaN(v)) return;
                    var txt = (typeof window.formatDerivedValue === 'function') ? window.formatDerivedValue(v) : Number(zf(v)).toFixed(2);
                    var $v = $(this).find('.gAttrVal2');
                    if ($v.text() !== txt) $v.text(txt);
                });
            }
        }
    }
    TLGV.updateGraphPanelValues = updateGraphPanelValues;
    window._gUpdatePanelValuesOnlyFromWebSocket = window._gUpdatePanelValuesOnlyFromWebSocket || updateGraphPanelValues;

    // ------------------------------------------------------------------
    // delegated UI events
    // ------------------------------------------------------------------
    function ctxOf(el) {
        var $b = $(el).closest('.tlgv-bar');
        if ($b.length) return $b.attr('data-tlgv');
        var $h = $(el).closest('[data-tlgv-host]');
        return $h.length ? $h.attr('data-tlgv-host') : null;
    }
    // Bound on each bar / host element (NOT on document): the modal shell calls
    // event.stopPropagation(), which would swallow document-level delegation.
    function bindUi(root) {
      $(root)
        .off('.tlgv')
        .on('click.tlgv', '[data-tlgv-mode]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id) return;
            var mode = $(this).attr('data-tlgv-mode');
            if (getMode(id) === mode) return;
            setMode(id, mode);
            apply(id, true);
        })
        .on('click.tlgv', '[data-tlgv-dens]', function (e) {
            e.preventDefault(); e.stopPropagation();
            prefs.density = $(this).attr('data-tlgv-dens'); savePrefs();
            $('[data-tlgv-dens]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-dens') === prefs.density); });
            var id = ctxOf(this); if (id) apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-cols]', function (e) {
            e.preventDefault(); e.stopPropagation();
            prefs.cols = $(this).attr('data-tlgv-cols'); savePrefs();
            $('[data-tlgv-cols]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-cols') === String(prefs.cols)); });
            var id = ctxOf(this); if (id) apply(id, false);
        })
        .on('input.tlgv', '[data-tlgv-search]', function (e) {
            e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            var el = this;
            clearTimeout(el.__t);
            el.__t = setTimeout(function () { TLGV.ctx[id].search = el.value; apply(id, false); }, 220);
        })
        .on('change.tlgv', '[data-tlgv-sync]', function (e) {
            e.stopPropagation();
            prefs.sync = this.checked; savePrefs();
            $('[data-tlgv-sync]').prop('checked', prefs.sync);
            var id = ctxOf(this); if (!id) return;
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-hide]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            TLGV.ctx[id].hidden[$(this).attr('data-tlgv-hide')] = true;
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-unhide]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            TLGV.ctx[id].hidden = {};
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-reset]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var c = TLGV.ctx[ctxOf(this)]; if (c && c.resetZoom) c.resetZoom();
        })
        .on('click.tlgv', '[data-tlgv-png]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var c = TLGV.ctx[ctxOf(this)]; if (c && c.exportPng) c.exportPng();
        })
        .on('click.tlgv', '[data-tlgv-focus]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id) return;
            var key = $(this).attr('data-tlgv-focus');
            setMode(id, 'individual');
            apply(id, false);
            var c = TLGV.ctx[id];
            var $card = c.cfg.host().find('.tlgv-card').filter(function () { return $(this).attr('data-key') === key; });
            if ($card.length) {
                $card.addClass('is-max is-flash');
                if ($card[0].scrollIntoView) $card[0].scrollIntoView({ block: 'center', behavior: 'smooth' });
                setTimeout(function () { $card.removeClass('is-flash'); var ch = c.cardCharts && c.cardCharts[key]; if (ch) ch.resize(); }, 900);
            }
        })
        .on('click.tlgv', '[data-tlgv-max]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card');
            $card.toggleClass('is-max');
            $(this).find('i').toggleClass('fa-expand fa-compress');
            var c = TLGV.ctx[ctxOf(this)];
            var ch = c && c.cardCharts && c.cardCharts[$card.attr('data-key')];
            if (ch) setTimeout(function () { ch.resize(); }, 30);
        })
        .on('click.tlgv', '[data-tlgv-cardpng]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card');
            var c = TLGV.ctx[ctxOf(this)];
            var key = $card.attr('data-key');
            var ch = c && c.cardCharts && c.cardCharts[key];
            if (!ch) return;
            ch.setOption({ title: { show: true }, grid: { top: 26 } });
            var url = ch.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            ch.setOption({ title: { show: false }, grid: { top: 12 } });
            download(url, String(key).replace(/[^\w.-]+/g, '_') + '.png');
        });
    }
    TLGV.bindUi = bindUi;
    $(window).off('resize.tlgv').on('resize.tlgv', function () {
        Object.keys(TLGV.ctx).forEach(function (id) { (TLGV.ctx[id].charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); });
    });

    // ==================================================================
    // SURFACE 1 — Graph view (fnBindGrph / _gLoad / _gRender)
    // ==================================================================
    makeCtx('grph', {
        host: function () { return $('#gAlt'); },
        overlay: function () { return $('#gCH'); },
        overlayChart: function () { return window.mainGraphChart; },
        showOverlay: function (on) {
            if (on) { if (window._gD) { $('#gCH').show(); $('#gToolbar').show(); } }
            else { $('#gCH').hide(); $('#gToolbar').hide(); }
        },
        model: fromGraphView
    });

    function gRangeHtml() {
        return '<span class="tlgv-grp" id="gRangeBox" data-tlgv-host="grph">' +
            '<span class="tlgv-lbl">From</span><input type="datetime-local" id="gFrom" class="tlgv-search" style="width:auto;color-scheme:dark;" aria-label="From date and time"/>' +
            '<span class="tlgv-lbl">To</span><input type="datetime-local" id="gTo" class="tlgv-search" style="width:auto;color-scheme:dark;" aria-label="To date and time"/>' +
            '<button type="button" class="tlgv-btn" id="gApply"><i class="fas fa-search"></i> Load</button></span>';
    }
    function gSyncDateInputs(startMs, endMs) {
        if (startMs != null) $('#gFrom').val(toLocalInput(startMs));
        if (endMs != null) $('#gTo').val(toLocalInput(Math.min(endMs, Date.now())));
    }
    function gRefreshMax() { var n = toLocalInput(Date.now()); $('#gFrom,#gTo').attr('max', n); }
    function gClampFuture(sel) {
        var el = $(sel), v = el.val(); if (!v) return;
        var d = new Date(v);
        if (!isNaN(d.getTime()) && d.getTime() > Date.now()) { el.val(toLocalInput(Date.now())); warn('Future dates are not allowed.'); }
    }
    window._gToLocalInput = window._gToLocalInput || toLocalInput;
    window._gSyncDateInputs = window._gSyncDateInputs || gSyncDateInputs;

    // v616 _gLoadWindow: explicit [start,end] loader (same request/response handling as v617 _gLoad)
    var gLoadSeq = 0;
    function gLoadWindow(aid, startMs, endMs) {
        var seq = ++gLoadSeq;
        endMs = Math.min(endMs, Date.now());
        window._gReqStart = startMs; window._gReqEnd = endMs;
        $('#gLD').show(); $('#gCH').hide(); $('#gEM').hide(); $('#gToolbar').hide(); $('#gAlt').hide();
        var s = new Date(startMs), e = new Date(endMs);
        var f = function (d) { return pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()); };
        var spanH = Math.max(1, Math.round((endMs - startMs) / 3600000));
        $('#gTR').text(f(s) + ' — ' + f(e) + ' (' + spanH + 'h)');
        gSyncDateInputs(startMs, endMs);
        $('#gTB .gtb2').removeClass('active2');
        $.ajax({
            url: window.HISTORY_API_BASE + '?assetId=' + aid + '&startDate=' + window.formatDateForHistoryApi(s) + '&endDate=' + window.formatDateForHistoryApi(e),
            type: 'GET', dataType: 'json', timeout: 60000,
            success: function (r) {
                if (seq !== gLoadSeq || String(window._gA) !== String(aid)) return;
                $('#gLD').hide();
                if (r && r.Data && r.Data.length > 0) {
                    window._gD = r.Data; $('#gCH').show();
                    window._gPop(aid, r.Data);
                    window._gRender(r.Data, aid, window._gF || 'all');
                    updateGraphPanelValues(String(aid));
                    setTimeout(function () {
                        if (String(window._gA) === String(aid)) { window._gPop(aid, window._gD); updateGraphPanelValues(String(aid)); }
                    }, 1500);
                } else {
                    window._gD = null;
                    $('#gEM').show(); $('#gPT').text('0'); $('#gAlt').hide();
                }
            },
            error: function (xhr, status) {
                if (status === 'abort' || seq !== gLoadSeq) return;
                $('#gLD').hide();
                $('#gEM').html('<i class="fas fa-exclamation-triangle" style="color:#f87171;font-size:28px;display:block;margin-bottom:10px;"></i><p style="color:#f87171;font-size:13px;">Failed to load data. Please try again.</p>').show();
            }
        });
    }
    window._gLoadWindow = gLoadWindow;
    window._gLoadRange = function (aid, s, e) { gLoadWindow(aid, s, e); };

    if (typeof window.fnBindGrph === 'function') {
        var _origBindGrph = window.fnBindGrph;
        window.fnBindGrph = function () {
            var r = _origBindGrph.apply(this, arguments);
            try { decorateGraphView(); } catch (e) { if (window.console) console.error('[TLGV] Graph view decorate failed', e); }
            return r;
        };
    }
    function decorateGraphView() {
        if (!$('#gWrap').length || $('#gWrap .tlgv-bar').length) return;
        injectStyles();
        var c = TLGV.ctx.grph;
        c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
        $('#gCH').before(barHtml('grph', gRangeHtml()));
        $('#gCH').after('<div id="gAlt" class="tlgv-host" data-tlgv-host="grph" style="display:none;"></div>');
        bindUi($('#gWrap .tlgv-bar')); bindUi($('#gAlt'));
        gRefreshMax();
        gSyncDateInputs(Date.now() - 24 * 3600000, Date.now());
        if (window._gMaxTimer) clearInterval(window._gMaxTimer);
        window._gMaxTimer = setInterval(function () {
            if (!$('#gFrom').length) { clearInterval(window._gMaxTimer); window._gMaxTimer = null; return; }
            gRefreshMax();
        }, 30000);
        $('#gFrom').off('.gfut').on('change.gfut blur.gfut', function () { gClampFuture('#gFrom'); });
        $('#gTo').off('.gfut').on('change.gfut blur.gfut', function () { gClampFuture('#gTo'); });
        $('#gFrom,#gTo').off('keydown.gfut').on('keydown.gfut', function (e) { if (e.key === 'Enter') $('#gApply').trigger('click'); });
        $('#gApply').off('click').on('click', function () {
            var fv = $('#gFrom').val(), tv = $('#gTo').val();
            if (!fv || !tv) { warn('Please pick both From and To date/time.'); return; }
            var sMs = new Date(fv).getTime(), eMs = new Date(tv).getTime();
            if (isNaN(sMs) || isNaN(eMs)) { warn('Invalid date/time selected.'); return; }
            var now = Date.now();
            if (sMs > now || eMs > now + 60000) { warn('Future dates are not allowed.'); return; }
            if (sMs >= eMs) { warn('From date/time must be earlier than To date/time.'); return; }
            gLoadWindow(window._gA, sMs, eMs);
        });
        ensureLiveTimer();
    }
    TLGV.decorateGraphView = decorateGraphView;

    if (typeof window._gLoad === 'function') {
        var _origGLoad = window._gLoad;
        window._gLoad = function (aid, hrs) {
            gLoadSeq++; // supersede any custom-range request still in flight
            $('#gAlt').hide();
            var r = _origGLoad.apply(this, arguments);
            gSyncDateInputs(window._gReqStart, window._gReqEnd);
            return r;
        };
    }
    if (typeof window._gRender === 'function') {
        var _origGRender = window._gRender;
        window._gRender = function (data, aid, filter) {
            var r = _origGRender.apply(this, arguments);
            try {
                if ($('#gAlt').length) {
                    if (!window.mainGraphChart || $('#gEM').css('display') !== 'none') { disposeCharts(TLGV.ctx.grph); $('#gAlt').empty().hide(); }
                    else apply('grph', true);
                }
            } catch (e) { if (window.console) console.error('[TLGV] Graph view render failed', e); }
            return r;
        };
    }

    // ==================================================================
    // SURFACE 2 — per-asset Historical Graph modal
    // ==================================================================
    makeCtx('modal', {
        host: function () { return $('#rdpmsGraphAlt'); },
        overlay: function () { return $('#rdpmsGraphChartDiv'); },
        overlayChart: function () { return window.rdpmsGraphChart; },
        model: fromModal
    });
    if (typeof window.fnGetAssetGraph === 'function') {
        var _origAssetGraph = window.fnGetAssetGraph;
        window.fnGetAssetGraph = function (siteId, assetId) {
            // decorate BEFORE the first load finishes: the original kicks the load
            // synchronously, the response arrives later.
            var r = _origAssetGraph.apply(this, arguments);
            try {
                var $ov = $('#rdpmsGraphOverlay');
                if ($ov.length && !$ov.find('.tlgv-bar').length) {
                    injectStyles();
                    var c = TLGV.ctx.modal;
                    c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
                    var pills = '<span class="tlgv-grp"><span class="tlgv-lbl">Last</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Quick range">' +
                        ['1', '3', '6', '12', '24'].map(function (hh) { return '<button type="button" class="tl-time-pill' + (hh === '24' ? ' on active' : '') + '" data-hours="' + hh + '">' + hh + 'H</button>'; }).join('') +
                        '</div></span>';
                    $ov.find('.tl-modal-toolbar').after(barHtml('modal', pills));
                    $ov.find('#rdpmsGraphChartDiv').after('<div id="rdpmsGraphAlt" class="tlgv-host" data-tlgv-host="modal" style="display:none;"></div>');
                    bindUi($ov.find('.tlgv-bar')); bindUi($ov.find('#rdpmsGraphAlt'));
                    $ov.find('#graphFromDate,#graphToDate').attr('max', toLocalInput(Date.now()));
                    // quick-range pills: bound directly (shell stops propagation)
                    $ov.find('.tlgv-bar .tl-time-pill').on('click.tlgvpill', function (ev) {
                        ev.preventDefault(); ev.stopPropagation();
                        var hrs = parseInt($(this).attr('data-hours'), 10);
                        $(this).addClass('on active').siblings().removeClass('on active');
                        if (hrs > 0) window.loadHistoryGraphData(assetId, hrs);
                    });
                    $ov.find('#graphApplyRange').on('click.tlgvpill', function () { $ov.find('.tlgv-bar .tl-time-pill').removeClass('on active'); });
                }
            } catch (e) { if (window.console) console.error('[TLGV] modal decorate failed', e); }
            return r;
        };
    }
    if (typeof window.loadHistoryGraphData === 'function') {
        var _origLoadHist = window.loadHistoryGraphData;
        window.loadHistoryGraphData = function () {
            var c = TLGV.ctx.modal;
            disposeCharts(c);
            $('#rdpmsGraphAlt').empty().hide();
            // v616 parity: never request a window that ends in the future
            var args = Array.prototype.slice.call(arguments);
            if (args[3] && new Date(args[3]).getTime() > Date.now()) args[3] = new Date();
            return _origLoadHist.apply(this, args);
        };
    }
    if (typeof window.renderHistoryChart === 'function') {
        var _origRenderHist = window.renderHistoryChart;
        window.renderHistoryChart = function () {
            var r = _origRenderHist.apply(this, arguments);
            try { if ($('#rdpmsGraphAlt').length && window.rdpmsGraphChart) apply('modal', true); }
            catch (e) { if (window.console) console.error('[TLGV] modal render failed', e); }
            return r;
        };
    }
    if (typeof window.closeGraphModal === 'function') {
        var _origCloseGraph = window.closeGraphModal;
        window.closeGraphModal = function (e) {
            if (e && e.target && !$(e.target).hasClass('tl-modal-overlay') && !$(e.target).hasClass('rdpms-graph-overlay')) return _origCloseGraph.apply(this, arguments);
            disposeCharts(TLGV.ctx.modal);
            TLGV.ctx.modal.model = null;
            return _origCloseGraph.apply(this, arguments);
        };
    }
    if (typeof window.toggleModalFullscreen === 'function') {
        var _origFs = window.toggleModalFullscreen;
        window.toggleModalFullscreen = function () {
            var r = _origFs.apply(this, arguments);
            setTimeout(function () { (TLGV.ctx.modal.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); }, 120);
            return r;
        };
    }

    // ==================================================================
    // SURFACE 3 — IPS History Graph (telemetrylive-graphs.js)
    // ==================================================================
    makeCtx('ips', {
        host: function () { return $('#ipsgAlt'); },
        overlay: function () { return $('#ipsgChart'); },
        overlayChart: function () { return window.ipsG && window.ipsG.chart; },
        showOverlay: function (on) {
            if (on) { $('#ipsgChart').show(); $('.ipsg-toolbar').show(); }
            else { $('#ipsgChart').hide(); $('.ipsg-toolbar').hide(); }
        },
        getMode: function () { return (window.ipsG && window.ipsG.mode) || 'overlay'; },
        setMode: function (m) { if (window.ipsG) window.ipsG.mode = m; },
        model: fromIps
    });
    if (typeof window._ipsBuildShell === 'function') {
        var _origIpsShell = window._ipsBuildShell;
        window._ipsBuildShell = function () {
            var r = _origIpsShell.apply(this, arguments);
            try {
                injectStyles();
                var c = TLGV.ctx.ips;
                c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
                // replace the v616-style Overlay/Stacked bar with the shared bar
                $('.ipsg-modebar').replaceWith(barHtml('ips'));
                $('#ipsgChart').after('<div id="ipsgAlt" class="tlgv-host" data-tlgv-host="ips" style="display:none;"></div>');
                bindUi($('.tlgv-bar[data-tlgv="ips"]')); bindUi($('#ipsgAlt'));
            } catch (e) { if (window.console) console.error('[TLGV] IPS decorate failed', e); }
            return r;
        };
    }
    if (typeof window._ipsRender === 'function') {
        var _origIpsRender = window._ipsRender;
        window._ipsRender = function () {
            var g = window.ipsG;
            if (!g || !$('#ipsgAlt').length) return _origIpsRender.apply(this, arguments);
            var m = g.mode;
            if (m === 'stacked' || m === 'individual') {
                // draw the (hidden) overlay so ipsG.chart and the empty state stay correct
                g.mode = 'overlay';
                var r;
                try { r = _origIpsRender.apply(this, arguments); } finally { g.mode = m; }
                if ($('#ipsgEmpty').is(':visible')) { disposeCharts(TLGV.ctx.ips); $('#ipsgAlt').hide(); return r; }
                apply('ips', true);
                return r;
            }
            disposeCharts(TLGV.ctx.ips);
            $('#ipsgAlt').empty().hide();
            $('.ipsg-toolbar').show();
            syncBar('ips');
            return _origIpsRender.apply(this, arguments);
        };
    }
    if (typeof window.ipsSavePng === 'function') {
        var _origIpsPng = window.ipsSavePng;
        window.ipsSavePng = function () {
            var c = TLGV.ctx.ips;
            if (window.ipsG && window.ipsG.mode !== 'overlay' && c.exportPng) return c.exportPng();
            return _origIpsPng.apply(this, arguments);
        };
    }

    TLGV.injectStyles = injectStyles;
    TLGV._internals = { fromGraphView: fromGraphView, fromModal: fromModal, fromIps: fromIps, computeStats: computeStats, valueAt: valueAt, pickupBands: pickupBands, prefs: prefs };
})(window, window.jQuery);
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-graphview" failed to load', e);
}


/* #############################################################################
 * SECTION: MULTI-ASSET GRAPH VIEW (v617.6)
 * (was telemetrylive-multigraph.js — must load after the graph-view section)
 * ########################################################################## */
try {
/* =====================================================================
 * TELEMETRY LIVE — MULTI-ASSET GRAPH ENGINE  (v617 add-on)
 *
 * Load AFTER telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-ext.js (it reuses the TLGV view bar / lane / card look).
 *
 * WHAT IT DOES
 *   The Graph view type used to require exactly ONE asset in #drpAsset for
 *   every asset type except IPS / Point Machine ("Please select an asset").
 *   This file routes the Graph view for EVERY asset type except Point
 *   Machine through one multi-asset engine that works with the current
 *   Asset-No selection (1..N assets, or "Select All" = every asset of the
 *   selected type, auto-capped with an asset chooser to pick more):
 *
 *     - GetHistoryData per asset, concurrency limited, request token so a
 *       superseded load is ignored, abort + one retry, per-asset failure
 *       badge with Retry, loading / empty states.
 *     - From / To range (default today 00:00 -> now), quick 1H..24H,
 *       future blocked.
 *     - Series keyed "assetId||attrId", names from bulk metadata, asset
 *       name prefixed when more than one asset. DataLogger relays are
 *       binary lanes. Track derived series (ITC BATT CHARG ...) via
 *       calculateDerivedValues, Track only. ELD never gets Track/Signal
 *       fallback names. IPS keeps the BATT CHARGING / DISCHARGING
 *       group-sum series of the IPS graph.
 *     - Attribute panel grouped by asset (fold per asset, per-asset
 *       select all/none, global Select/Unselect, live values every 2 s,
 *       floored with tlZeroFloor), type filter All / mA / V / 0-1 / Derived.
 *     - Same View bar as the other graphs: Overlay (one ECharts, mA / V /
 *       0-1 axes, legend + tooltip grouped by asset), Stacked lanes (one
 *       lane per series under an asset header row, hover isolation: only
 *       the hovered lane follows the cursor), Individual cards (grouped by
 *       asset, tooltip local to the card, "Sync zoom" mirrors zoom only).
 *     - Distinct hue per asset, shade per attribute.
 *     - Every value <= 0 is drawn as 0 (tlZeroFloor).
 *
 * FIXES (why the IPS / ELD graph "did not work")
 *   1. getSelectedAssetIds() returns [] when EVERY asset is checked (the
 *      default after the asset list loads) -> fnBindIpsGraph / fnBindPmGraph
 *      warned "Please select at least one asset" and drew nothing. The
 *      engine (and a wrapped ipsSelectedIds / fnBindPmGraph) resolve an
 *      empty selection to the checked (= all) assets.
 *   2. IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME is declared with `var` inside an
 *      IIFE of telemetrylive-ext.js but read as a global by
 *      _ipsGroupChargingSeries (telemetrylive-graphs.js) -> ReferenceError
 *      as soon as an IPS asset named "... CHARGING n" was loaded, so the
 *      graph stayed on the spinner. A window-level default is defined here.
 *   3. ELD went to fnBindGrph (single #drpAsset, never set by the
 *      multi-select) -> "Please select an asset". ELD now uses the engine:
 *      DataLogger relays only, binary lanes, multi-asset.
 *
 * Nothing in the shared files is edited; every hook is a wrapper around a
 * window-level function with typeof guards. State: window.mgG.
 * ===================================================================== */
(function (window, $) {
    'use strict';
    if (!$ || window.TLMG) return;

    var TLMG = window.TLMG = { version: '1.0.0' };
    var MAX_AUTO = 12;      // "Select All" auto-loads at most this many assets
    var MAX_HARD = 30;      // hard ceiling for an explicit selection / chooser
    var CONCURRENT = 4;     // history requests in flight at once
    var LIVE_MS = 2000;

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------
    function fn(name) { return (typeof window[name] === 'function') ? window[name] : null; }
    function zf(v) {
        var f = fn('tlZeroFloor');
        if (f) return f(v);
        var n = parseFloat(v); if (isNaN(n)) return v; return n <= 0 ? 0 : n;
    }
    function num(v) { var n = parseFloat(v); return isNaN(n) ? null : n; }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function pad(n) { return String(n).padStart(2, '0'); }
    var MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    function fmtAxis(ms, span) {
        var d = new Date(ms), hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
        if (span > 30 * 3600000) return pad(d.getDate()) + ' ' + MON[d.getMonth()] + '\n' + hm;
        return hm;
    }
    function fmtFull(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + ' ' + MON[d.getMonth()] + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    function toLocalInput(ms) {
        var d = new Date(ms);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }
    function warn(msg) { var f = fn('showWarning'); if (f) f(msg, 'Graph'); else if (window.console) console.warn('[TLMG]', msg); }
    function info(msg) { var f = fn('showInfo'); if (f) f(msg, 'Graph'); }
    function isLight() { return document.body && document.body.getAttribute('data-aurora') === 'light'; }
    function theme() {
        var l = isLight();
        return {
            txt: l ? 'rgba(11,21,48,.62)' : 'rgba(255,255,255,0.5)',
            txt2: l ? 'rgba(11,21,48,.86)' : 'rgba(255,255,255,0.85)',
            axis: l ? 'rgba(11,21,48,.22)' : 'rgba(255,255,255,0.18)',
            split: l ? 'rgba(11,21,48,.07)' : 'rgba(255,255,255,0.05)',
            gridA: l ? 'rgba(11,21,48,.035)' : 'rgba(255,255,255,0.036)',
            gridB: l ? 'rgba(11,21,48,.015)' : 'rgba(255,255,255,0.018)',
            tipBg: l ? 'rgba(255,255,255,.98)' : 'rgba(5,9,24,0.97)',
            tipTxt: l ? 'rgba(11,21,48,.9)' : 'rgba(255,255,255,0.9)',
            pngBg: l ? '#f4f6fb' : '#0e1530'
        };
    }
    function hexA(hex, a) {
        var h = String(hex || '').replace('#', '');
        if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
        var r = parseInt(h.substr(0, 2), 16), g = parseInt(h.substr(2, 2), 16), b = parseInt(h.substr(4, 2), 16);
        if (isNaN(r)) return 'rgba(34,211,238,' + a + ')';
        return 'rgba(' + r + ',' + g + ',' + b + ',' + a + ')';
    }
    function hsl2hex(h, s, l) {
        h = ((h % 360) + 360) % 360; s /= 100; l /= 100;
        var c = (1 - Math.abs(2 * l - 1)) * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = l - c / 2, r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        function t(v) { return pad(Math.round((v + m) * 255).toString(16)); }
        return '#' + t(r) + t(g) + t(b);
    }
    function niceMax(v) {
        if (!(v > 0)) return 1;
        var m = v * 1.15, p = Math.pow(10, Math.floor(Math.log10(m))), f = m / p;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * p;
    }
    function parseTs(ts) {
        if (!ts) return NaN;
        var s = String(ts);
        if (s.indexOf('0001') === 0) return NaN;          // .NET empty date
        var t = new Date(s).getTime();
        if (!isNaN(t)) return t;
        var m = s.match(/^(\d{2})(\d{2})(\d{4})_(\d{2})(\d{2})(\d{2})$/);
        if (m) return new Date(+m[3], (+m[2]) - 1, +m[1], +m[4], +m[5], +m[6]).getTime();
        return NaN;
    }
    function entryTime(e) {
        var ts = e.Timestamp || {};
        var t = parseTs(ts.TimestampDevice || e.TimestampDevice);
        if (isNaN(t)) t = parseTs(ts.TimestampLocal || e.TimestampLocal || ts.TimestampChange);
        return t;
    }
    // value at time t (carry-forward) — pts sorted asc
    function valueAt(pts, t) {
        if (!pts || !pts.length || t < pts[0][0]) return null;
        var lo = 0, hi = pts.length - 1, idx = -1;
        while (lo <= hi) { var mid = (lo + hi) >> 1; if (pts[mid][0] <= t) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
        return idx >= 0 ? pts[idx][1] : null;
    }
    function compress(pts) {
        var out = [];
        for (var i = 0; i < pts.length; i++) {
            var p = pts[i];
            if (out.length && out[out.length - 1][0] === p[0]) { out[out.length - 1] = p; continue; }
            if (out.length > 1 && out[out.length - 1][1] === p[1] && out[out.length - 2][1] === p[1]) { out[out.length - 1] = p; continue; }
            out.push(p);
        }
        return out;
    }
    function pickupBands(pts, end) {
        var bands = [], on0 = null;
        for (var i = 0; i < pts.length; i++) {
            var on = pts[i][1] >= 0.5;
            if (on && on0 === null) on0 = pts[i][0];
            if (!on && on0 !== null) { bands.push([on0, pts[i][0]]); on0 = null; }
        }
        if (on0 !== null) bands.push([on0, end]);
        return bands;
    }
    function computeStats(s, xMin, xMax) {
        var st = { count: s.actual.length, min: null, max: null, avg: null, last: null, lastT: null, pickups: 0, drops: 0, onPct: null };
        var pts = s.pts; if (!pts.length) return st;
        var lastA = s.actual.length ? s.actual[s.actual.length - 1] : pts[pts.length - 1];
        st.last = lastA[1]; st.lastT = lastA[0];
        var end = Math.min(xMax || Date.now(), Date.now()), start = (xMin != null) ? xMin : pts[0][0];
        if (s.kind === 'bin') {
            var prev = null, on = 0, tot = 0;
            for (var i = 0; i < pts.length; i++) {
                var v = pts[i][1] >= 0.5 ? 1 : 0;
                if (prev !== null && v !== prev) { if (v === 1) st.pickups++; else st.drops++; }
                prev = v;
                var t0 = Math.max(pts[i][0], start), t1 = Math.min(i + 1 < pts.length ? pts[i + 1][0] : end, end);
                if (t1 > t0) { tot += t1 - t0; if (v === 1) on += t1 - t0; }
            }
            st.onPct = tot > 0 ? (on * 100 / tot) : null;
            return st;
        }
        var src = s.actual.length ? s.actual : pts, mn = Infinity, mx = -Infinity;
        for (var j = 0; j < src.length; j++) { var x = src[j][1]; if (x < mn) mn = x; if (x > mx) mx = x; }
        st.min = mn; st.max = mx;
        var area = 0, span = 0;
        for (var k = 0; k < pts.length; k++) {
            var a0 = Math.max(pts[k][0], start), a1 = Math.min(k + 1 < pts.length ? pts[k + 1][0] : end, end);
            if (a1 > a0) { area += pts[k][1] * (a1 - a0); span += a1 - a0; }
        }
        if (span > 0) st.avg = area / span; else { var sum = 0; src.forEach(function (p) { sum += p[1]; }); st.avg = sum / src.length; }
        return st;
    }
    function kindLabel(kind) { return { ma: 'Current', v: 'Voltage', bin: 'Relay', drv: 'Derived', other: 'Value' }[kind] || 'Value'; }
    function unitFor(name, kind) {
        if (kind === 'bin') return '0/1';
        var n = String(name || ''), m = n.match(/\((mA|mV|V|Ω|A)\)\s*$/);
        if (m) return m[1];
        if (/\bmV\b/i.test(n)) return 'mV';
        if (/\bmA\b/i.test(n)) return 'mA';
        if (kind === 'ma') return 'mA';
        if (kind === 'v') return 'V';
        if (/\bV\b/.test(n)) return 'V';
        return '';
    }
    function fmtVal(v, kind) {
        if (v === null || v === undefined || isNaN(v)) return '—';
        if (kind === 'bin') return v >= 0.5 ? 'Pickup' : 'Drop';
        return Number(zf(v)).toFixed(2);
    }
    function valHtml(v, kind, unit) {
        if (kind === 'bin') {
            if (v === null || v === undefined || isNaN(v)) return '<span class="tlgv-na">—</span>';
            return '<span class="tlgv-badge ' + (v >= 0.5 ? 'up' : 'dn') + '">' + (v >= 0.5 ? 'Pickup' : 'Drop') + '</span>';
        }
        return '<b>' + fmtVal(v, kind) + '</b>' + (unit && v !== null && v !== undefined ? '<i>' + esc(unit) + '</i>' : '');
    }
    function statsLine(s) {
        var st = s.stats || {};
        if (s.kind === 'bin') return '↑ ' + st.pickups + ' pickups · ↓ ' + st.drops + ' drops' + (st.onPct !== null ? ' · ' + st.onPct.toFixed(0) + '% up' : '');
        if (st.min === null) return 'no samples';
        return 'min ' + fmtVal(st.min) + ' · max ' + fmtVal(st.max) + ' · avg ' + fmtVal(st.avg);
    }
    function download(url, name) {
        var a = document.createElement('a'); a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
    }
    function cssKey(k) { return (window.CSS && CSS.escape) ? CSS.escape(k) : String(k).replace(/"/g, '\\"'); }

    // shared prefs (same store as the TLGV bar: density / cols / sync / per-view mode)
    function loadPrefs() { try { return JSON.parse(window.localStorage.getItem('tlgv.prefs.v1')) || {}; } catch (e) { return {}; } }
    var prefs = (window.TLGV && window.TLGV._internals && window.TLGV._internals.prefs) || loadPrefs();
    prefs.mode = prefs.mode || {}; prefs.density = prefs.density || 'm'; prefs.cols = prefs.cols || 'auto';
    if (prefs.sync === undefined) prefs.sync = true;
    function savePrefs() { try { window.localStorage.setItem('tlgv.prefs.v1', JSON.stringify(prefs)); } catch (e) { /* ignore */ } }

    // ------------------------------------------------------------------
    // state
    // ------------------------------------------------------------------
    var mgG = window.mgG = {
        kind: '', typeId: '', typeName: '', siteId: '',
        assetIds: [],     // assets being graphed (in order)
        assets: {},       // id -> { id, name, color, idx, virtual }
        pool: [],         // every asset of the type that can be picked
        capped: false, poolTotal: 0,
        fromMs: 0, toMs: 0, start: 0, end: 0,
        raw: {}, failed: {}, loaded: {},
        series: {}, order: [],
        checked: {}, fold: {}, collapsed: {},
        filter: 'all', mode: 'overlay',
        chart: null, charts: [], cardCharts: {}, hidden: {}, search: '', cursorKey: null,
        reqId: 0, xhrs: [], loading: false, model: null, selectionKey: ''
    };
    TLMG.state = mgG;

    // ------------------------------------------------------------------
    // asset type + asset resolution
    // ------------------------------------------------------------------
    function detectKind() {
        var id = String($('#drpAssetType').val() || window.wsCurrentAssetTypeId || '').trim();
        var txt = ($('#drpAssetType option:selected').text() || '').trim();
        var low = txt.toLowerCase(), kind = 'other';
        if (fn('isEldAssetType') && window.isEldAssetType()) kind = 'eld';
        else if (fn('isIpsAssetType') && window.isIpsAssetType()) kind = 'ips';
        else if (id === '1' || low === 'track' || low.indexOf('track') === 0) kind = 'track';
        else if (id === '2' || low === 'signal') kind = 'signal';
        return { kind: kind, typeId: id, typeName: txt || ({ track: 'Track', signal: 'Signal', eld: 'ELD', ips: 'IPS' }[kind] || 'Asset') };
    }
    function assetName(id) {
        var dd = $('#listAssetNumber .dropdown-item[data-value="' + id + '"]').attr('data-text');
        var live = window.wsLiveData && window.wsLiveData[id] && window.wsLiveData[id].AssetName;
        var g = fn('getBulkAssetName');
        if (g) { var b = g(id, dd || live); if (b && b !== ('Asset ' + id)) return String(b); }
        if (dd) return String(dd);
        if (live) return String(live);
        if (window.bulkAssetsList) for (var i = 0; i < window.bulkAssetsList.length; i++) if (String(window.bulkAssetsList[i].Id) === String(id)) return String(window.bulkAssetsList[i].Name || ('Asset ' + id));
        return 'Asset ' + id;
    }
    function assetPool(typeId) {
        var out = [], seen = {};
        $('#listAssetNumber .dropdown-item').each(function () {
            var v = String($(this).attr('data-value') || $(this).find('input').val() || '').trim();
            if (!v || v === '0' || seen[v]) return; seen[v] = true;
            out.push({ id: v, name: String($(this).attr('data-text') || $(this).text() || '').trim() || assetName(v) });
        });
        if (!out.length && window.bulkAssetsList && window.bulkAssetsList.length) {
            window.bulkAssetsList.forEach(function (a) {
                var v = String(a.Id); if (!v || seen[v]) return;
                if (typeId && a.AssetTypeId !== undefined && a.AssetTypeId !== null && String(a.AssetTypeId) !== String(typeId)) return;
                seen[v] = true; out.push({ id: v, name: String(a.Name || ('Asset ' + v)) });
            });
        }
        if (!out.length && window.wsLiveData) {
            Object.keys(window.wsLiveData).forEach(function (v) {
                var a = window.wsLiveData[v]; if (!a || seen[v]) return;
                if (typeId && a.AssetTypeId !== undefined && a.AssetTypeId !== null && String(a.AssetTypeId) !== String(typeId)) return;
                seen[v] = true; out.push({ id: v, name: String(a.AssetName || ('Asset ' + v)) });
            });
        }
        return out;
    }
    function selectedIds() {
        var g = fn('getSelectedAssetIds'), ids = (g ? g() : []) || [], out = [], seen = {};
        for (var i = 0; i < ids.length; i++) { var v = String(ids[i] || '').trim(); if (v && v !== '0' && !seen[v]) { seen[v] = true; out.push(v); } }
        return out;
    }
    function checkedIds() {
        var out = [], seen = {};
        $('#listAssetNumber input:checked').each(function () { var v = String($(this).val() || '').trim(); if (v && v !== '0' && !seen[v]) { seen[v] = true; out.push(v); } });
        return out;
    }
    // "Select All" detection. The stock getSelectedAssetIds() returned [] when
    // every asset was ticked; the SWITCHING section (617.6) returns the full
    // explicit list instead, so also treat "every checkbox ticked" / the
    // Select-All box as a Select All (auto cap, chooser to pick more).
    function allTicked(ids) {
        var $all = $('#listAssetNumber input[type="checkbox"]');
        if (!$all.length) return false;
        if ($('#chkAllAssetNumber').is(':checked')) return true;
        return ids.length >= $all.length && $all.filter(':checked').length === $all.length;
    }
    // Resolve the assets to graph from the Asset-No selection.
    //   explicit subset            -> those (hard cap)
    //   "Select All" ([] or all ticked) -> every checked asset, auto cap MAX_AUTO
    //   nothing checked / no list  -> every asset of the type, auto cap
    function resolveAssets(forcedIds) {
        var pool = assetPool(mgG.typeId), ids, capped = false, explicit = false;
        if (forcedIds && forcedIds.length) { ids = forcedIds.slice(); explicit = true; }
        else {
            ids = selectedIds();
            if (ids.length && !allTicked(ids)) explicit = true;
            else {
                if (!ids.length) ids = checkedIds();
                if (!ids.length) ids = pool.map(function (a) { return a.id; });
            }
        }
        var cap = explicit ? MAX_HARD : MAX_AUTO;
        if (ids.length > cap) { ids = ids.slice(0, cap); capped = true; }
        mgG.pool = pool; mgG.capped = capped; mgG.poolTotal = pool.length;
        return ids;
    }

    // ------------------------------------------------------------------
    // colours: one hue per asset, a shade per attribute
    // ------------------------------------------------------------------
    var HUES = [190, 268, 152, 38, 338, 214, 96, 20, 300, 174, 0, 240, 62, 126, 282, 330, 200, 50];
    var SHADES = [[0, 66, 88], [12, 56, 78], [-12, 74, 92], [22, 48, 70], [-22, 62, 96], [6, 44, 60], [-6, 80, 84], [30, 58, 74], [-30, 52, 66], [16, 70, 96]];
    function assetHue(idx) { return HUES[idx % HUES.length] + Math.floor(idx / HUES.length) * 11; }
    function assetColor(idx) { return hsl2hex(assetHue(idx), isLight() ? 62 : 82, isLight() ? 42 : 62); }
    function seriesColor(assetIdx, attrIdx) {
        var sh = SHADES[attrIdx % SHADES.length], light = isLight();
        return hsl2hex(assetHue(assetIdx) + sh[0], light ? Math.max(40, sh[2] - 22) : sh[2], light ? Math.max(30, sh[1] - 20) : sh[1]);
    }

    // ------------------------------------------------------------------
    // styles
    // ------------------------------------------------------------------
    function ensureTlgvStyles() {
        if (document.getElementById('tlgv-styles')) return;
        var T = window.TLGV;
        if (T && typeof T.injectStyles === 'function') { T.injectStyles(); return; }
        // TLGV keeps injectStyles private; its Graph-view decorator injects
        // them when it finds #gWrap, so hand it a throw-away one.
        if (T && typeof T.decorateGraphView === 'function' && !$('#gWrap').length) {
            var tmp = $('<div id="gWrap" style="display:none"><div id="gCH"></div></div>').appendTo(document.body);
            try { T.decorateGraphView(); } catch (e) { /* ignore */ }
            tmp.remove();
            if (window._gMaxTimer) { clearInterval(window._gMaxTimer); window._gMaxTimer = null; }
        }
        if (document.getElementById('tlgv-styles')) return;
        // last resort: the handful of TLGV rules the engine depends on
        var st = document.createElement('style'); st.id = 'tlgv-styles';
        st.appendChild(document.createTextNode([
            '.tlgv-bar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;padding:8px 14px;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlgv-seg{display:inline-flex;background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:10px;padding:3px;gap:2px;}',
            '.tlgv-seg button{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:6px;padding:5px 12px;border-radius:7px;font-size:11.5px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));}',
            '.tlgv-seg button.on{background:var(--at-brand,#22d3ee);color:#04121c;}',
            '.tlgv-lbl{font-size:10px;font-weight:800;letter-spacing:.6px;text-transform:uppercase;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-grp{display:inline-flex;align-items:center;gap:6px;}.tlgv-mini button{padding:4px 9px;font-size:10.5px;}.tlgv-spacer{flex:1;}',
            '.tlgv-search{background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:8px;color:var(--at-t1,#fff);font-size:11.5px;padding:5px 10px;width:170px;outline:none;}',
            '.tlgv-btn{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:5px;padding:5px 10px;border-radius:8px;font-size:11px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));border:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlgv-chk{display:inline-flex;align-items:center;gap:5px;font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));cursor:pointer;margin:0;}',
            '.tlgv-alt-only,.tlgv-stk-only,.tlgv-ind-only{display:none!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-alt-only,.tlgv-bar[data-mode="individual"] .tlgv-alt-only{display:inline-flex!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-stk-only{display:inline-flex!important;}.tlgv-bar[data-mode="individual"] .tlgv-ind-only{display:inline-flex!important;}',
            '.tlgv-host{position:relative;width:100%;}.tlgv-empty{padding:48px 16px;text-align:center;color:var(--at-t3,rgba(255,255,255,.5));font-size:13px;}',
            '.tlgv-cursor{display:flex;align-items:center;gap:10px;padding:6px 14px;font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.08));}',
            '.tlgv-cursor b{font-family:monospace;color:var(--at-brand,#22d3ee);font-size:12px;}',
            '.tlgv-stack{position:relative;width:100%;}.tlgv-stack-chart{position:absolute;inset:0;}.tlgv-gutter{position:absolute;left:0;top:0;bottom:0;pointer-events:none;}',
            '.tlgv-lane{position:absolute;left:8px;right:8px;display:flex;flex-direction:column;justify-content:center;gap:2px;padding:4px 8px 4px 12px;border-radius:10px;background:rgba(255,255,255,.04);border:1px solid var(--at-edge,rgba(255,255,255,.08));pointer-events:auto;overflow:hidden;box-sizing:border-box;}',
            '.tlgv-lane.tlgv-lane-active{border-color:var(--lane-c,#22d3ee);}.tlgv-lane::before{content:"";position:absolute;left:0;top:0;bottom:0;width:4px;background:var(--lane-c,#22d3ee);}',
            '.tlgv-lane-top{display:flex;align-items:center;gap:6px;min-width:0;}.tlgv-lane-name{flex:1;min-width:0;font-size:11.5px;font-weight:700;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-unit{font-size:9px;font-weight:800;padding:1px 5px;border-radius:4px;background:rgba(255,255,255,.08);color:var(--at-t2,rgba(255,255,255,.72));}',
            '.tlgv-lane-val{display:flex;align-items:baseline;gap:4px;font-family:monospace;}.tlgv-lane-val b{font-size:16px;color:var(--lane-c,#22d3ee);}.tlgv-lane-val i{font-style:normal;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-lane-sub{font-size:9.5px;color:var(--at-t3,rgba(255,255,255,.5));white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-lane-act{display:inline-flex;gap:2px;opacity:0;}.tlgv-lane:hover .tlgv-lane-act,.tlgv-card:hover .tlgv-lane-act{opacity:1;}',
            '.tlgv-ico{all:unset;cursor:pointer;width:20px;height:20px;display:inline-flex;align-items:center;justify-content:center;border-radius:5px;color:var(--at-t3,rgba(255,255,255,.5));font-size:10px;}',
            '.tlgv-badge{display:inline-block;padding:1px 8px;border-radius:5px;font-size:10px;font-weight:800;color:#04121c;}.tlgv-badge.up{background:#34d399;}.tlgv-badge.dn{background:#fbbf24;}.tlgv-na{color:var(--at-t4,rgba(255,255,255,.34));}',
            '.tlgv-grid{display:grid;gap:12px;padding:12px;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));}.tlgv-grid.c1{grid-template-columns:1fr;}.tlgv-grid.c2{grid-template-columns:repeat(2,minmax(0,1fr));}.tlgv-grid.c3{grid-template-columns:repeat(3,minmax(0,1fr));}',
            '.tlgv-card{position:relative;display:flex;flex-direction:column;border-radius:14px;background:rgba(255,255,255,.04);border:1px solid var(--at-edge,rgba(255,255,255,.10));overflow:hidden;min-width:0;}.tlgv-card::before{content:"";position:absolute;left:0;right:0;top:0;height:3px;background:var(--lane-c,#22d3ee);}.tlgv-card.is-max{grid-column:1/-1;}',
            '.tlgv-card-head{display:flex;align-items:center;gap:8px;padding:10px 12px 4px;min-width:0;}.tlgv-card-dot{width:10px;height:10px;border-radius:50%;background:var(--lane-c,#22d3ee);}.tlgv-card-title{flex:1;min-width:0;}.tlgv-card-title b{display:block;font-size:12.5px;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}.tlgv-card-title small{display:block;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-card-live{display:flex;align-items:baseline;gap:4px;font-family:monospace;}.tlgv-card-live b{font-size:18px;color:var(--lane-c,#22d3ee);}.tlgv-card-live i{font-style:normal;font-size:10px;}',
            '.tlgv-dot-live{width:7px;height:7px;border-radius:50%;background:#34d399;align-self:center;}.tlgv-dot-live.off{background:var(--at-t4,rgba(255,255,255,.34));}',
            '.tlgv-card-stats{display:flex;flex-wrap:wrap;gap:4px 12px;padding:2px 12px 6px;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}.tlgv-card-chart{height:180px;width:100%;}.tlgv-card.is-max .tlgv-card-chart{height:380px;}',
            '.tlgv-flash{animation:tlgvFlash 1s ease-out;}@keyframes tlgvFlash{0%{text-shadow:0 0 10px currentColor;}100%{text-shadow:none;}}',
            '@media (max-width:700px){.tlgv-grid,.tlgv-grid.c2,.tlgv-grid.c3{grid-template-columns:1fr;padding:8px;}}'
        ].join('\n')));
        document.head.appendChild(st);
    }
    function injectStyles() {
        if (document.getElementById('tlmg-styles')) return;
        var css = [
            '.tlmg-wrap{background:var(--at-bg2,#0e1530);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:14px;box-shadow:0 4px 20px rgba(0,0,0,.22);width:100%;overflow:hidden;font-family:Manrope,system-ui,sans-serif;}',
            '.tlmg-head{background:linear-gradient(135deg,rgba(34,211,238,.16),rgba(167,139,250,.08));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));padding:12px 16px;display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px 14px;}',
            '.tlmg-title{color:var(--at-t1,rgba(255,255,255,.96));display:flex;align-items:center;gap:10px;min-width:0;}',
            '.tlmg-title>i{color:var(--at-brand,#22d3ee);font-size:16px;}',
            '.tlmg-title b{font-size:15px;letter-spacing:.01em;display:block;}',
            '.tlmg-sub{color:var(--at-t3,rgba(255,255,255,.50));font-size:11px;margin-top:1px;font-family:"JetBrains Mono",monospace;}',
            '.tlmg-range{display:flex;align-items:center;gap:6px;flex-wrap:wrap;}',
            '.tlmg-range label{font-size:10px;font-weight:800;letter-spacing:.5px;text-transform:uppercase;color:var(--at-t3,rgba(255,255,255,.5));margin:0;}',
            '.tlmg-range input[type=datetime-local]{background:var(--at-g1,rgba(255,255,255,.06));border:1px solid var(--at-edge,rgba(255,255,255,.14));border-radius:8px;color:var(--at-t1,rgba(255,255,255,.96));font-size:11.5px;padding:4px 8px;outline:none;color-scheme:dark;}',
            'body[data-aurora="light"] .tlmg-range input[type=datetime-local]{color-scheme:light;}',
            '.tlmg-range input:focus{border-color:var(--at-brand,#22d3ee);}',
            '.tlmg-quick button{padding:4px 9px;font-size:10.5px;}',
            '.tlmg-btn{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:5px;padding:5px 11px;border-radius:8px;font-size:11px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.10));transition:all .18s;white-space:nowrap;}',
            '.tlmg-btn:hover{color:var(--at-brand,#22d3ee);border-color:rgba(34,211,238,.4);}',
            '.tlmg-btn.on,.tlmg-btn.tlmg-primary{background:linear-gradient(135deg,var(--at-brand,#22d3ee),var(--at-brand-deep,#0ea5b7));color:#04121c;border-color:transparent;box-shadow:0 0 12px var(--at-brand-glow,rgba(34,211,238,.35));}',
            '.tlmg-btn:focus-visible{outline:2px solid var(--at-brand,#22d3ee);outline-offset:1px;}',
            '.tlmg-btn:disabled{opacity:.5;cursor:default;}',
            '.tlmg-notice{display:flex;align-items:center;gap:8px;flex-wrap:wrap;padding:6px 14px;font-size:11.5px;color:var(--at-t2,rgba(255,255,255,.72));background:rgba(251,191,36,.08);border-bottom:1px solid rgba(251,191,36,.25);}',
            '.tlmg-notice i{color:#fbbf24;}',
            /* asset chooser popover */
            '.tlmg-assets{position:relative;}',
            '.tlmg-pop{position:absolute;right:0;top:calc(100% + 6px);z-index:60;width:min(360px,92vw);background:var(--at-bg2,#0e1530);border:1px solid var(--at-edge,rgba(255,255,255,.14));border-radius:12px;box-shadow:0 14px 40px rgba(0,0,0,.5);padding:10px;display:none;}',
            '.tlmg-pop.open{display:block;}',
            '.tlmg-pop-head{display:flex;align-items:center;gap:6px;margin-bottom:8px;}',
            '.tlmg-pop-head .tlgv-search{flex:1;width:auto;}',
            '.tlmg-pop-list{max-height:260px;overflow:auto;display:flex;flex-direction:column;gap:2px;}',
            '.tlmg-pop-item{display:flex;align-items:center;gap:8px;padding:5px 8px;border-radius:7px;cursor:pointer;font-size:12px;color:var(--at-t2,rgba(255,255,255,.72));user-select:none;}',
            '.tlmg-pop-item:hover{background:var(--at-g2,rgba(255,255,255,.06));color:var(--at-t1,#fff);}',
            '.tlmg-pop-item input{accent-color:var(--at-brand,#22d3ee);margin:0;}',
            '.tlmg-pop-item.hidden{display:none;}',
            '.tlmg-pop-foot{display:flex;align-items:center;gap:6px;margin-top:8px;font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));}',
            /* selection bar + panel */
            '.tlmg-selbar{display:flex;align-items:center;gap:6px 10px;flex-wrap:wrap;padding:6px 14px;background:var(--at-g1,rgba(255,255,255,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlmg-count{margin-left:auto;font-size:10.5px;color:var(--at-t3,rgba(255,255,255,.5));font-weight:600;font-family:"JetBrains Mono",monospace;}',
            '.tlmg-panel{max-height:290px;overflow-y:auto;overflow-x:hidden;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlmg-panel::-webkit-scrollbar{width:8px;}.tlmg-panel::-webkit-scrollbar-thumb{background:var(--at-edge,rgba(255,255,255,.18));border-radius:4px;}',
            '.tlmg-agrp{border-bottom:1px solid var(--at-edge,rgba(255,255,255,.07));}',
            '.tlmg-agrp:last-child{border-bottom:none;}',
            '.tlmg-ahead{display:flex;align-items:center;gap:8px;padding:5px 12px;cursor:pointer;user-select:none;background:linear-gradient(90deg,var(--ac-bg,rgba(34,211,238,.10)),transparent 70%);min-width:0;}',
            '.tlmg-ahead:hover{background:linear-gradient(90deg,var(--ac-bg2,rgba(34,211,238,.18)),transparent 70%);}',
            '.tlmg-chev{width:16px;height:16px;display:inline-flex;align-items:center;justify-content:center;color:var(--at-t3,rgba(255,255,255,.5));font-size:10px;transition:transform .18s;flex-shrink:0;}',
            '.tlmg-agrp.folded .tlmg-chev{transform:rotate(-90deg);}',
            '.tlmg-agrp.folded .tlmg-abody{display:none;}',
            '.tlmg-sw{width:10px;height:10px;border-radius:3px;background:var(--ac,#22d3ee);box-shadow:0 0 8px var(--ac,#22d3ee);flex-shrink:0;}',
            '.tlmg-aname{font-size:12px;font-weight:800;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0;}',
            '.tlmg-ameta{font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));font-family:"JetBrains Mono",monospace;white-space:nowrap;}',
            '.tlmg-abadge{font-size:9.5px;font-weight:800;padding:1px 7px;border-radius:99px;background:rgba(248,113,113,.15);color:#f87171;white-space:nowrap;}',
            '.tlmg-abadge.ok{background:rgba(52,211,153,.14);color:#34d399;}',
            '.tlmg-aact{margin-left:auto;display:inline-flex;gap:4px;align-items:center;}',
            '.tlmg-mini{all:unset;cursor:pointer;font-size:10px;font-weight:700;padding:2px 8px;border-radius:6px;color:var(--at-t2,rgba(255,255,255,.72));border:1px solid var(--at-edge,rgba(255,255,255,.10));background:var(--at-g1,rgba(255,255,255,.04));white-space:nowrap;}',
            '.tlmg-mini:hover{color:var(--at-brand,#22d3ee);border-color:rgba(34,211,238,.4);}',
            '.tlmg-abody{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:4px 6px;padding:5px 12px 8px 34px;}',
            '.tlmg-chip{display:inline-flex;align-items:center;gap:6px;cursor:pointer;padding:3px 8px;border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:7px;background:var(--at-g1,rgba(255,255,255,.04));user-select:none;min-width:0;min-height:24px;margin:0;transition:border-color .15s;}',
            '.tlmg-chip:hover{border-color:var(--ac,#22d3ee);}',
            '.tlmg-chip.off{opacity:.55;}',
            '.tlmg-chip.hide{display:none;}',
            '.tlmg-chip input{width:12px;height:12px;accent-color:var(--ac,#22d3ee);margin:0;flex-shrink:0;}',
            '.tlmg-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0;box-shadow:0 0 0 1px rgba(0,0,0,.25);}',
            '.tlmg-an{font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));font-weight:600;flex:1 1 auto;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlmg-av{font-size:11px;font-weight:700;font-family:"JetBrains Mono",monospace;font-variant-numeric:tabular-nums;flex:0 0 auto;min-width:40px;text-align:right;}',
            '.tlmg-av .tlgv-badge{font-size:9px;padding:0 6px;}',
            '.tlmg-kind{font-size:8.5px;font-weight:800;letter-spacing:.4px;padding:0 4px;border-radius:3px;background:rgba(255,255,255,.08);color:var(--at-t3,rgba(255,255,255,.5));flex-shrink:0;}',
            /* overlay */
            '.tlmg-legend{display:flex;flex-wrap:wrap;gap:6px;padding:8px 14px 0;}',
            '.tlmg-lg{display:inline-flex;align-items:center;gap:6px;padding:3px 9px;border-radius:99px;border:1px solid var(--at-edge,rgba(255,255,255,.10));font-size:11px;font-weight:700;color:var(--at-t1,#fff);cursor:pointer;background:var(--ac-bg,rgba(255,255,255,.04));user-select:none;}',
            '.tlmg-lg.off{opacity:.45;text-decoration:line-through;}',
            '.tlmg-lg small{font-weight:600;color:var(--at-t3,rgba(255,255,255,.5));font-family:"JetBrains Mono",monospace;}',
            '.tlmg-toolbar{padding:4px 12px;text-align:right;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlmg-chart{width:100%;height:clamp(380px,56vh,680px);display:none;}',
            '.tlmg-load{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;height:200px;color:var(--at-t3,rgba(255,255,255,.5));font-size:12.5px;font-weight:600;}',
            '.tlmg-spin{width:26px;height:26px;border:3px solid var(--at-edge,rgba(255,255,255,.12));border-top-color:var(--at-brand,#22d3ee);border-radius:50%;animation:tlmgspin .7s linear infinite;}',
            '@keyframes tlmgspin{to{transform:rotate(360deg);}}',
            '.tlmg-prog{width:220px;height:4px;border-radius:2px;background:var(--at-edge,rgba(255,255,255,.12));overflow:hidden;}',
            '.tlmg-prog>i{display:block;height:100%;background:var(--at-brand,#22d3ee);width:0;transition:width .25s;}',
            '.tlmg-empty{display:none;text-align:center;padding:48px 20px;color:var(--at-t3,rgba(255,255,255,.5));font-size:12.5px;}',
            '.tlmg-empty i{font-size:34px;display:block;margin-bottom:10px;opacity:.3;color:var(--at-brand,#22d3ee);}',
            /* asset header rows inside stacked / individual */
            '.tlmg-hdrs{position:absolute;inset:0;pointer-events:none;}',
            '.tlmg-ahdr{position:absolute;left:8px;right:18px;display:flex;align-items:center;gap:8px;padding:0 10px;border-radius:8px;background:linear-gradient(90deg,var(--ac-bg,rgba(34,211,238,.14)),rgba(255,255,255,.02));border-left:4px solid var(--ac,#22d3ee);pointer-events:auto;box-sizing:border-box;min-width:0;}',
            '.tlmg-ahdr.grid{position:static;grid-column:1/-1;height:30px;}',
            '.tlmg-ahdr .tlmg-aname{font-size:12px;}',
            '.tlmg-ahdr .tlmg-mini{margin-left:auto;}',
            '.tlmg-lane-asset{font-size:9px;color:var(--at-t3,rgba(255,255,255,.5));font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            /* light theme touch-ups for the shared lane / card chrome */
            'body[data-aurora="light"] .tlmg-wrap .tlgv-lane{background:rgba(11,21,48,.045);}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-card{background:rgba(11,21,48,.035);}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-seg,body[data-aurora="light"] .tlmg-wrap .tlgv-search{background:rgba(11,21,48,.06);}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-unit,body[data-aurora="light"] .tlmg-kind{background:rgba(11,21,48,.08);}',
            'body[data-aurora="light"] .tlmg-wrap{background:var(--at-g4,rgba(255,255,255,.88));box-shadow:0 2px 12px rgba(0,0,0,.10);}',
            'body[data-aurora="light"] .tlmg-head{background:linear-gradient(135deg,rgba(34,211,238,.14),rgba(167,139,250,.08));}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-bar,body[data-aurora="light"] .tlmg-selbar,body[data-aurora="light"] .tlmg-wrap .tlgv-cursor{background:rgba(11,21,48,.035);}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-seg button.on,body[data-aurora="light"] .tlmg-btn.on,body[data-aurora="light"] .tlmg-btn.tlmg-primary{color:#04121c;}',
            'body[data-aurora="light"] .tlmg-chip,body[data-aurora="light"] .tlmg-mini,body[data-aurora="light"] .tlmg-btn,body[data-aurora="light"] .tlmg-lg{background:rgba(11,21,48,.04);}',
            'body[data-aurora="light"] .tlmg-pop{background:#fff;}',
            'body[data-aurora="light"] .tlmg-wrap .tlgv-badge{color:#04121c;}',
            '@media (max-width:640px){.tlmg-head{padding:10px 12px;}.tlmg-title b{font-size:13.5px;}.tlmg-abody{grid-template-columns:1fr;padding-left:18px;}.tlmg-chart{height:320px;}.tlmg-selbar{padding:6px 10px;}.tlmg-count{width:100%;margin-left:0;}.tlmg-ahdr{right:8px;}}',
            '@media (prefers-reduced-motion:reduce){.tlmg-spin{animation:none;}}'
        ].join('\n');
        var st = document.createElement('style'); st.id = 'tlmg-styles';
        st.appendChild(document.createTextNode(css));
        document.head.appendChild(st);
    }

    // ------------------------------------------------------------------
    // entry point
    // ------------------------------------------------------------------
    // opts: { assetIds?: [] , keepRange?: bool }
    TLMG.open = function (opts) {
        opts = opts || {};
        var siteId = String($('#drpSite').val() || '');
        if (!siteId || siteId === '0') { warn('Please select a site'); return false; }
        var kd = detectKind();
        if (!kd.typeId || kd.typeId === '0') { warn('Please select an asset type'); return false; }
        mgG.siteId = siteId; mgG.kind = kd.kind; mgG.typeId = kd.typeId; mgG.typeName = kd.typeName;

        var ids = resolveAssets(opts.assetIds);
        /* v618.25: Track / Signal -> ONE asset at a time (selector in the header);
           IPS / ELD / others keep the multi-asset view. */
        mgG.single = false;
        if ((kd.kind === 'track' || kd.kind === 'signal') && ids.length > 1) {
            mgG.allIds = ids.slice();
            var pick = String(opts.single || window._tlmgSingle || '');
            if (ids.map(String).indexOf(pick) < 0) pick = String(ids[0]);
            ids = [pick];
            mgG.single = true;
        }
        if (!ids.length) {
            $('#divTelemetryLive').html('<div class="tlmg-wrap"><div class="tlmg-empty" style="display:block;"><i class="fas fa-chart-line"></i>' +
                '<div style="font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));margin-bottom:6px;">No ' + esc(kd.typeName) + ' assets to graph</div>' +
                '<div style="font-size:11px;">Pick one or more asset numbers, then press Search.</div></div></div>');
            return false;
        }
        var key = kd.typeId + '|' + ids.slice().sort().join(',');
        if (mgG.selectionKey !== key) { mgG.checked = {}; mgG.collapsed = {}; mgG.fold = {}; mgG.hidden = {}; mgG.search = ''; mgG.selectionKey = key; }
        mgG.realIds = ids.slice();      // what is requested from the History API
        mgG.assetIds = ids.slice();     // what is shown (IPS adds virtual BATT group rows)
        mgG.assets = {};
        ids.forEach(function (id, i) { mgG.assets[id] = { id: id, name: assetName(id), idx: i, color: assetColor(i) }; });
        mgG.mode = prefs.mode.multi || 'overlay';

        buildShell();
        if (mgG.single) {
            var $rg = $('#mgRange');
            $rg.find('.tlmg-assets').hide();
            var opt = mgG.allIds.map(function (id) {
                return '<option value="' + esc(id) + '"' + (String(id) === String(ids[0]) ? ' selected' : '') + '>' + esc(assetName(id)) + '</option>';
            }).join('');
            $rg.prepend('<label for="mgSingle" style="font-weight:600;">' + esc(mgG.typeName) + '</label>' +
                '<select id="mgSingle" class="tlmg-btn" style="min-width:120px;" aria-label="Asset">' + opt + '</select>');
            $('#mgSingle').on('change', function () {
                window._tlmgSingle = String(this.value);
                TLMG.open({ assetIds: mgG.allIds, single: window._tlmgSingle, keepRange: true });
            });
        }
        // Live values come from the WebSocket store. connectWebSocket() wipes
        // wsLiveData, so only (re)connect when nothing is streaming or the
        // stream is for a different filter (same rule as Search). The WS
        // repaint code skips the Graph view, so the panel is never overwritten.
        try {
            var c = window.wsConnection, WS = window.WebSocket;
            var open = c && WS && (c.readyState === WS.OPEN || c.readyState === WS.CONNECTING);
            var wantUrl = fn('buildWebSocketUrl') ? window.buildWebSocketUrl(siteId, mgG.typeId, selectedIds()) : null;
            if ((!open || (wantUrl && c.url && c.url !== wantUrl)) && fn('connectWebSocket')) window.connectWebSocket(siteId, mgG.typeId, selectedIds());
        } catch (e) { /* live values are optional */ }

        var lu = fn('loadUserAssetInfo');
        var fired = false;
        function go() { if (fired) return; fired = true; load(); }
        if (lu) {
            try { lu(siteId, go); } catch (e) { go(); }
            setTimeout(go, 4000);   // metadata callback never firing must not strand the graph
        } else go();
        return true;
    };

    // ------------------------------------------------------------------
    // shell
    // ------------------------------------------------------------------
    function viewBarHtml() {
        var m = mgG.mode, hn = Object.keys(mgG.hidden).length;
        function seg(list, attr, cur, pretty) {
            return '<div class="tlgv-seg tlgv-mini" role="group">' + list.map(function (d) {
                return '<button type="button" ' + attr + '="' + d + '" class="' + (String(cur) === d ? 'on' : '') + '">' + (pretty ? pretty(d) : d.toUpperCase()) + '</button>';
            }).join('') + '</div>';
        }
        return '<div class="tlgv-bar" data-tlgv="multi" data-mode="' + m + '">' +
            '<span class="tlgv-lbl">View</span>' +
            '<div class="tlgv-seg" role="group" aria-label="Graph view">' +
            '<button type="button" data-tlgv-mode="overlay" class="' + (m === 'overlay' ? 'on' : '') + '" aria-pressed="' + (m === 'overlay') + '" title="All attributes on one chart"><i class="fas fa-layer-group"></i> Overlay</button>' +
            '<button type="button" data-tlgv-mode="stacked" class="' + (m === 'stacked' ? 'on' : '') + '" aria-pressed="' + (m === 'stacked') + '" title="One lane per attribute, grouped by asset"><i class="fas fa-bars-staggered"></i> Stacked</button>' +
            '<button type="button" data-tlgv-mode="individual" class="' + (m === 'individual' ? 'on' : '') + '" aria-pressed="' + (m === 'individual') + '" title="A chart card for every attribute, grouped by asset"><i class="fas fa-table-cells-large"></i> Individual</button>' +
            '</div>' +
            '<span class="tlgv-grp"><span class="tlgv-lbl">Filter</span>' +
            seg(['all', 'ma', 'v', 'bin', 'drv'].filter(function (f) { return f !== 'drv' || mgG.kind === 'track'; }), 'data-mg-filter', mgG.filter,
                function (d) { return { all: 'All', ma: 'mA', v: 'V', bin: '0/1', drv: 'Derived' }[d]; }) + '</span>' +
            '<span class="tlgv-grp tlgv-stk-only"><span class="tlgv-lbl">Lanes</span>' + seg(['s', 'm', 'l'], 'data-tlgv-dens', prefs.density) + '</span>' +
            '<span class="tlgv-grp tlgv-ind-only"><span class="tlgv-lbl">Columns</span>' + seg(['auto', '1', '2', '3'], 'data-tlgv-cols', prefs.cols, function (d) { return d === 'auto' ? 'Auto' : d; }) + '</span>' +
            '<span class="tlgv-spacer"></span>' +
            '<span class="tlgv-grp tlgv-alt-only"><input type="search" class="tlgv-search" data-tlgv-search placeholder="Filter attributes…" aria-label="Filter attributes" value="' + esc(mgG.search) + '"/></span>' +
            '<label class="tlgv-chk tlgv-alt-only" title="Zoom / pan all charts together (hover stays on one attribute)"><input type="checkbox" data-tlgv-sync ' + (prefs.sync ? 'checked' : '') + '/> Sync zoom</label>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-unhide style="' + (hn ? '' : 'display:none!important') + '"><i class="fas fa-eye"></i> Show hidden (<span data-tlgv-hn>' + hn + '</span>)</button>' +
            '<button type="button" class="tlgv-btn" data-tlgv-reset><i class="fas fa-undo"></i> Reset zoom</button>' +
            '<button type="button" class="tlgv-btn" data-tlgv-png><i class="fas fa-download"></i> PNG</button>' +
            '</div>';
    }
    function syncBar() {
        var $bar = $('#mgWrap .tlgv-bar');
        $bar.attr('data-mode', mgG.mode);
        $bar.find('[data-tlgv-mode]').each(function () { var on = $(this).attr('data-tlgv-mode') === mgG.mode; $(this).toggleClass('on', on).attr('aria-pressed', on ? 'true' : 'false'); });
        $bar.find('[data-mg-filter]').each(function () { $(this).toggleClass('on', $(this).attr('data-mg-filter') === mgG.filter); });
        $bar.find('[data-tlgv-dens]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-dens') === prefs.density); });
        $bar.find('[data-tlgv-cols]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-cols') === String(prefs.cols)); });
        var hn = Object.keys(mgG.hidden).length;
        $bar.find('[data-tlgv-hn]').text(hn);
        $bar.find('[data-tlgv-unhide]').attr('style', hn ? '' : 'display:none!important');
    }
    function titleText() {
        var n = mgG.assetIds.length;
        if (n === 1) return mgG.assets[mgG.assetIds[0]].name;
        return n + ' ' + mgG.typeName + ' Assets';
    }
    function buildShell() {
        ensureTlgvStyles(); injectStyles();
        disposeAll();
        var siteName = ($('#drpSite option:selected').text() || '').trim();
        var h = '<div class="tlmg-wrap" id="mgWrap">';
        h += '<div class="tlmg-head">' +
            '<div class="tlmg-title"><i class="fas fa-chart-line"></i><div><b id="mgTitle">' + esc(titleText()) + '</b>' +
            '<div class="tlmg-sub">' + esc(siteName) + ' · ' + esc(mgG.typeName) + ' · Multi-Asset History Graph</div></div></div>' +
            '<div class="tlmg-range" id="mgRange">' +
            '<div class="tlmg-assets"><button type="button" class="tlmg-btn" id="mgAssetsBtn" aria-haspopup="true" aria-expanded="false"><i class="fas fa-cubes"></i> Assets <span id="mgAssetsN">' + mgG.assetIds.length + '</span> <i class="fas fa-chevron-down" style="font-size:9px;opacity:.6;"></i></button>' +
            '<div class="tlmg-pop" id="mgPop" role="dialog" aria-label="Choose assets"></div></div>' +
            '<div class="tlgv-seg tlgv-mini tlmg-quick" role="group" aria-label="Quick range">' +
            ['1', '3', '6', '12', '24'].map(function (hh) { return '<button type="button" data-mg-quick="' + hh + '">' + hh + 'H</button>'; }).join('') +
            '<button type="button" data-mg-quick="today" class="on">Today</button></div>' +
            '<label for="mgFrom">From</label><input type="datetime-local" id="mgFrom" aria-label="From date and time"/>' +
            '<label for="mgTo">To</label><input type="datetime-local" id="mgTo" aria-label="To date and time"/>' +
            '<button type="button" class="tlmg-btn tlmg-primary" id="mgLoadBtn"><i class="fas fa-sync-alt"></i> Load</button>' +
            '</div></div>';
        h += '<div id="mgNotice"></div>';
        h += viewBarHtml();
        h += '<div class="tlmg-selbar"><span class="tlgv-lbl">Attributes</span>' +
            '<button type="button" class="tlmg-mini" data-mg-all="1"><i class="fas fa-check-square"></i> Select All</button>' +
            '<button type="button" class="tlmg-mini" data-mg-all="0"><i class="far fa-square"></i> Unselect All</button>' +
            '<button type="button" class="tlmg-mini" data-mg-foldall="1" title="Collapse every asset group"><i class="fas fa-compress"></i> Fold</button>' +
            '<button type="button" class="tlmg-mini" data-mg-foldall="0" title="Expand every asset group"><i class="fas fa-expand"></i> Unfold</button>' +
            '<span class="tlmg-count" id="mgCount"></span></div>';
        h += '<div class="tlmg-panel" id="mgPanel"></div>';
        h += '<div class="tlmg-legend" id="mgLegend" style="display:none;"></div>';
        h += '<div id="mgLoad" class="tlmg-load"><div class="tlmg-spin"></div><span id="mgLoadTxt">Loading telemetry data…</span><div class="tlmg-prog"><i id="mgProg"></i></div></div>';
        h += '<div id="mgChart" class="tlmg-chart"></div>';
        h += '<div id="mgAlt" class="tlgv-host" data-tlgv-host="multi" style="display:none;"></div>';
        h += '<div id="mgEmpty" class="tlmg-empty"><i class="fas fa-chart-line"></i><div style="font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));margin-bottom:6px;">No data available</div><div style="font-size:11px;">Try a different time range, tick more attributes or check asset connectivity</div></div>';
        h += '</div>';
        $('#divTelemetryLive').empty().append(h);

        var now = Date.now();
        if (!mgG.toMs || mgG.toMs > now) mgG.toMs = now;
        if (!mgG.fromMs) { var m0 = new Date(now); m0.setHours(0, 0, 0, 0); mgG.fromMs = m0.getTime(); }
        $('#mgFrom').val(toLocalInput(mgG.fromMs)); $('#mgTo').val(toLocalInput(mgG.toMs));
        refreshMax();
        buildNotice(); buildChooser(); bindShell(); syncBar();
    }
    function refreshMax() { $('#mgFrom,#mgTo').attr('max', toLocalInput(Date.now())); }
    function buildNotice() {
        var $n = $('#mgNotice').empty();
        var failed = Object.keys(mgG.failed).length;
        var parts = [];
        if (mgG.capped) parts.push('<span><i class="fas fa-triangle-exclamation"></i> Showing the first <b>' + mgG.assetIds.length + '</b> of <b>' + mgG.poolTotal + '</b> ' + esc(mgG.typeName) + ' assets.</span>' +
            '<button type="button" class="tlmg-mini" data-mg-choose>Choose assets…</button>');
        if (failed) parts.push('<span><i class="fas fa-plug-circle-xmark"></i> <b>' + failed + '</b> asset' + (failed > 1 ? 's' : '') + ' failed to load.</span>' +
            '<button type="button" class="tlmg-mini" data-mg-retry-all><i class="fas fa-rotate-right"></i> Retry failed</button>');
        if (parts.length) $n.html('<div class="tlmg-notice">' + parts.join('<span style="opacity:.3">|</span>') + '</div>');
    }

    // ---- asset chooser popover --------------------------------------
    function buildChooser() {
        var $p = $('#mgPop');
        var cur = {}; mgG.assetIds.forEach(function (id) { cur[id] = true; });
        var h = '<div class="tlmg-pop-head"><input type="search" class="tlgv-search" data-mg-pop-search placeholder="Search asset…" aria-label="Search asset"/>' +
            '<button type="button" class="tlmg-mini" data-mg-pop-all="1">All</button><button type="button" class="tlmg-mini" data-mg-pop-all="0">None</button></div>' +
            '<div class="tlmg-pop-list">';
        mgG.pool.forEach(function (a) {
            h += '<label class="tlmg-pop-item"><input type="checkbox" value="' + esc(a.id) + '" ' + (cur[a.id] ? 'checked' : '') + '/><span>' + esc(a.name) + '</span></label>';
        });
        if (!mgG.pool.length) h += '<div class="tlgv-empty" style="padding:16px;">No asset list available</div>';
        h += '</div><div class="tlmg-pop-foot"><span data-mg-pop-n>' + mgG.assetIds.length + '</span>&nbsp;selected · max ' + MAX_HARD +
            '<span class="tlgv-spacer"></span><button type="button" class="tlmg-btn tlmg-primary" data-mg-pop-apply><i class="fas fa-check"></i> Apply</button></div>';
        $p.html(h);
    }
    function popCount() {
        var n = $('#mgPop input[type=checkbox]:checked').length;
        $('#mgPop [data-mg-pop-n]').text(n).css('color', n > MAX_HARD ? '#f87171' : '');
        $('#mgPop [data-mg-pop-apply]').prop('disabled', n === 0 || n > MAX_HARD);
    }
    function togglePop(open) {
        var $p = $('#mgPop'), isOpen = $p.hasClass('open');
        if (open === undefined) open = !isOpen;
        $p.toggleClass('open', open);
        $('#mgAssetsBtn').attr('aria-expanded', open ? 'true' : 'false');
        if (open) { popCount(); setTimeout(function () { $p.find('[data-mg-pop-search]').trigger('focus'); }, 10); }
    }

    // ---- shell events -----------------------------------------------
    function bindShell() {
        var $w = $('#mgWrap');
        $w.off('.tlmg');
        // range
        $w.on('click.tlmg', '#mgLoadBtn', function () {
            var fv = $('#mgFrom').val(), tv = $('#mgTo').val();
            if (!fv || !tv) { warn('Please pick both From and To date/time.'); return; }
            var s = new Date(fv).getTime(), e = new Date(tv).getTime(), now = Date.now();
            if (isNaN(s) || isNaN(e)) { warn('Invalid date/time selected.'); return; }
            if (s > now || e > now + 60000) { warn('Future dates are not allowed.'); return; }
            if (s >= e) { warn('From date/time must be earlier than To date/time.'); return; }
            mgG.fromMs = s; mgG.toMs = Math.min(e, now);
            $('#mgRange [data-mg-quick]').removeClass('on');
            load();
        });
        $w.on('change.tlmg blur.tlmg', '#mgFrom,#mgTo', function () {
            var v = $(this).val(); if (!v) return;
            var d = new Date(v).getTime();
            if (!isNaN(d) && d > Date.now()) { $(this).val(toLocalInput(Date.now())); warn('Future dates are not allowed.'); }
        });
        $w.on('keydown.tlmg', '#mgFrom,#mgTo', function (e) { if (e.key === 'Enter') $('#mgLoadBtn').trigger('click'); });
        $w.on('click.tlmg', '[data-mg-quick]', function () {
            var q = $(this).attr('data-mg-quick'), now = Date.now();
            if (q === 'today') { var d = new Date(now); d.setHours(0, 0, 0, 0); mgG.fromMs = d.getTime(); }
            else mgG.fromMs = now - parseInt(q, 10) * 3600000;
            mgG.toMs = now;
            $('#mgFrom').val(toLocalInput(mgG.fromMs)); $('#mgTo').val(toLocalInput(mgG.toMs));
            $('#mgRange [data-mg-quick]').removeClass('on'); $(this).addClass('on');
            load();
        });
        // asset chooser
        $w.on('click.tlmg', '#mgAssetsBtn,[data-mg-choose]', function (e) { e.stopPropagation(); togglePop(); });
        $w.on('click.tlmg', '#mgPop', function (e) { e.stopPropagation(); });
        $w.on('change.tlmg', '#mgPop input[type=checkbox]', popCount);
        $w.on('click.tlmg', '[data-mg-pop-all]', function () {
            var on = $(this).attr('data-mg-pop-all') === '1';
            $('#mgPop .tlmg-pop-item:not(.hidden) input').prop('checked', on);
            if (on) { var n = 0; $('#mgPop input[type=checkbox]:checked').each(function () { if (++n > MAX_HARD) this.checked = false; }); }
            popCount();
        });
        $w.on('input.tlmg', '[data-mg-pop-search]', function () {
            var q = String(this.value || '').trim().toLowerCase();
            $('#mgPop .tlmg-pop-item').each(function () { $(this).toggleClass('hidden', !!q && $(this).text().toLowerCase().indexOf(q) < 0); });
        });
        $w.on('click.tlmg', '[data-mg-pop-apply]', function () {
            var ids = []; $('#mgPop input[type=checkbox]:checked').each(function () { ids.push(String(this.value)); });
            if (!ids.length) { warn('Pick at least one asset.'); return; }
            if (ids.length > MAX_HARD) { warn('Pick at most ' + MAX_HARD + ' assets.'); return; }
            togglePop(false);
            // mirror the choice into the Asset-No multi-select so Search / other views agree
            try {
                var $items = $('#listAssetNumber .dropdown-item');
                if ($items.length) {
                    $items.each(function () { var on = ids.indexOf(String($(this).attr('data-value'))) > -1; $(this).toggleClass('checked', on).find('input').prop('checked', on); });
                    if (fn('updateAssetText')) window.updateAssetText();
                    if (fn('updateSelectAllState')) window.updateSelectAllState();
                }
            } catch (e2) { /* ignore */ }
            TLMG.open({ assetIds: ids });
        });
        $(document).off('click.tlmgpop').on('click.tlmgpop', function () { if ($('#mgPop').hasClass('open')) togglePop(false); });
        $w.on('keydown.tlmg', '#mgPop', function (e) { if (e.key === 'Escape') togglePop(false); });
        // retry
        $w.on('click.tlmg', '[data-mg-retry]', function () { retryAssets([String($(this).attr('data-mg-retry'))]); });
        $w.on('click.tlmg', '[data-mg-retry-all]', function () { retryAssets(Object.keys(mgG.failed)); });
        // view bar
        $w.on('click.tlmg', '[data-tlgv-mode]', function (e) {
            e.preventDefault();
            var m = $(this).attr('data-tlgv-mode'); if (m === mgG.mode) return;
            mgG.mode = m; prefs.mode.multi = m; savePrefs(); syncBar(); render();
        });
        $w.on('click.tlmg', '[data-mg-filter]', function () {
            mgG.filter = $(this).attr('data-mg-filter'); syncBar(); applyPanelFilter(); render();
        });
        $w.on('click.tlmg', '[data-tlgv-dens]', function (e) { e.preventDefault(); prefs.density = $(this).attr('data-tlgv-dens'); savePrefs(); syncBar(); if (mgG.mode === 'stacked') render(); });
        $w.on('click.tlmg', '[data-tlgv-cols]', function (e) { e.preventDefault(); prefs.cols = $(this).attr('data-tlgv-cols'); savePrefs(); syncBar(); if (mgG.mode === 'individual') render(); });
        $w.on('input.tlmg', '[data-tlgv-search]', function () {
            var el = this; clearTimeout(el.__t);
            el.__t = setTimeout(function () { mgG.search = el.value; if (mgG.mode !== 'overlay') render(); }, 220);
        });
        $w.on('change.tlmg', '[data-tlgv-sync]', function () { prefs.sync = this.checked; savePrefs(); if (mgG.mode === 'individual') render(); });
        $w.on('click.tlmg', '[data-tlgv-hide]', function (e) { e.preventDefault(); e.stopPropagation(); mgG.hidden[$(this).attr('data-tlgv-hide')] = true; syncBar(); render(); });
        $w.on('click.tlmg', '[data-tlgv-unhide]', function (e) { e.preventDefault(); mgG.hidden = {}; syncBar(); render(); });
        $w.on('click.tlmg', '[data-tlgv-reset]', function (e) { e.preventDefault(); resetZoom(); });
        $w.on('click.tlmg', '[data-tlgv-png]', function (e) { e.preventDefault(); exportPng(); });
        $w.on('click.tlmg', '[data-tlgv-focus]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var key = $(this).attr('data-tlgv-focus');
            mgG.mode = 'individual'; prefs.mode.multi = 'individual'; savePrefs(); syncBar(); render();
            var $card = $('#mgAlt .tlgv-card').filter(function () { return $(this).attr('data-key') === key; });
            if ($card.length) {
                $card.addClass('is-max is-flash');
                if ($card[0].scrollIntoView) $card[0].scrollIntoView({ block: 'center', behavior: 'smooth' });
                setTimeout(function () { $card.removeClass('is-flash'); var ch = mgG.cardCharts[key]; if (ch) ch.resize(); }, 900);
            }
        });
        $w.on('click.tlmg', '[data-tlgv-max]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card'); $card.toggleClass('is-max');
            $(this).find('i').toggleClass('fa-expand fa-compress');
            var ch = mgG.cardCharts[$card.attr('data-key')]; if (ch) setTimeout(function () { ch.resize(); }, 30);
        });
        $w.on('click.tlmg', '[data-tlgv-cardpng]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card'), key = $card.attr('data-key'), ch = mgG.cardCharts[key]; if (!ch) return;
            ch.setOption({ title: { show: true }, grid: { top: 26 } });
            var url = ch.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: theme().pngBg });
            ch.setOption({ title: { show: false }, grid: { top: 12 } });
            download(url, String(key).replace(/[^\w.-]+/g, '_') + '.png');
        });
        // attribute panel
        $w.on('change.tlmg', '[data-mg-key]', function () {
            mgG.checked[$(this).attr('data-mg-key')] = this.checked;
            $(this).closest('.tlmg-chip').toggleClass('off', !this.checked);
            updateCount(); render();
        });
        $w.on('click.tlmg', '[data-mg-all]', function () { setAllChecked($(this).attr('data-mg-all') === '1', null); });
        $w.on('click.tlmg', '[data-mg-asset-all]', function (e) { e.stopPropagation(); setAllChecked($(this).attr('data-mg-asset-all') === '1', String($(this).attr('data-mg-aid'))); });
        $w.on('click.tlmg', '.tlmg-ahead', function (e) {
            if ($(e.target).closest('button,input,label').length) return;
            var aid = String($(this).attr('data-mg-aid')), $g = $(this).closest('.tlmg-agrp');
            mgG.fold[aid] = !mgG.fold[aid]; $g.toggleClass('folded', !!mgG.fold[aid]);
            $(this).attr('aria-expanded', mgG.fold[aid] ? 'false' : 'true');
        });
        $w.on('keydown.tlmg', '.tlmg-ahead', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); $(this).trigger('click'); }
        });
        $w.on('click.tlmg', '[data-mg-foldall]', function () {
            var f = $(this).attr('data-mg-foldall') === '1';
            $('#mgPanel .tlmg-agrp').each(function () { var aid = String($(this).attr('data-mg-aid')); mgG.fold[aid] = f; $(this).toggleClass('folded', f).find('.tlmg-ahead').attr('aria-expanded', f ? 'false' : 'true'); });
        });
        // asset collapse (hides the asset in every view, keeps its ticks)
        $w.on('click.tlmg', '[data-mg-collapse]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var aid = String($(this).attr('data-mg-collapse'));
            mgG.collapsed[aid] = !mgG.collapsed[aid];
            renderPanel(); render();
        });
    }

    // ------------------------------------------------------------------
    // loading
    // ------------------------------------------------------------------
    function abortAll() {
        if (mgG.xhrs && mgG.xhrs.length) mgG.reqId++;   // late callbacks of the aborted load become no-ops
        (mgG.xhrs || []).forEach(function (x) { try { x.abort(); } catch (e) { /* ignore */ } });
        mgG.xhrs = [];
    }
    function histUrl(aid, s, e) {
        var base = (typeof window.HISTORY_API_BASE === 'string' && window.HISTORY_API_BASE) ? window.HISTORY_API_BASE : '/FRS25/Telemetry/GetHistoryData';
        var f = fn('formatDateForHistoryApi') || function (d) { return pad(d.getDate()) + pad(d.getMonth() + 1) + d.getFullYear() + '_' + pad(d.getHours()) + pad(d.getMinutes()) + pad(d.getSeconds()); };
        return base + '?assetId=' + encodeURIComponent(aid) + '&startDate=' + f(s) + '&endDate=' + f(e);
    }
    function showLoading(txt) {
        $('#mgLoad').show().find('#mgLoadTxt').text(txt || 'Loading telemetry data…');
        $('#mgProg').css('width', '0%');
        $('#mgChart,#mgAlt,#mgEmpty,#mgLegend').hide();
    }
    // Fetch every asset with a concurrency limit; one retry per asset; a newer
    // load (higher reqId) makes every late response of this one a no-op.
    function load(onlyIds) {
        mgG.end = Math.min(mgG.toMs || Date.now(), Date.now());
        if (!mgG.fromMs) { var m0 = new Date(mgG.end); m0.setHours(0, 0, 0, 0); mgG.fromMs = m0.getTime(); }
        mgG.start = mgG.fromMs;
        var s = new Date(mgG.start), e = new Date(mgG.end);
        abortAll();
        var myReq = ++mgG.reqId;
        var ids = (onlyIds && onlyIds.length) ? onlyIds.slice() : mgG.realIds.slice();
        if (!onlyIds) { mgG.raw = {}; mgG.failed = {}; mgG.loaded = {}; }
        else ids.forEach(function (a) { delete mgG.failed[a]; delete mgG.loaded[a]; });
        mgG.loading = true;
        showLoading('Loading ' + ids.length + ' asset' + (ids.length > 1 ? 's' : '') + '…');
        var next = 0, done = 0, active = 0;
        function finish() {
            if (myReq !== mgG.reqId) return;
            mgG.loading = false;
            $('#mgLoad').hide();
            buildSeries(); renderPanel(); buildNotice();
            if (!mgG.order.length) { $('#mgEmpty').show(); return; }
            render();
        }
        function step() {
            active--; done++;
            $('#mgProg').css('width', Math.round(done * 100 / ids.length) + '%');
            $('#mgLoadTxt').text('Loading telemetry data… ' + done + ' / ' + ids.length + ' assets');
            if (done >= ids.length) finish(); else pump();
        }
        function one(aid, attempt) {
            var xhr = $.ajax({ url: histUrl(aid, s, e), type: 'GET', dataType: 'json', timeout: 60000 });
            mgG.xhrs.push(xhr);
            xhr.done(function (r) {
                if (myReq !== mgG.reqId) return;               // superseded load: ignore
                mgG.raw[aid] = (r && r.Data) ? r.Data : []; mgG.loaded[aid] = true;
                step();
            }).fail(function (x, status) {
                if (myReq !== mgG.reqId) return;
                if (status !== 'abort' && attempt < 1) {       // one retry, same concurrency slot
                    setTimeout(function () { if (myReq === mgG.reqId) one(aid, attempt + 1); }, 700);
                    return;
                }
                mgG.raw[aid] = []; mgG.failed[aid] = true;
                step();
            });
        }
        function pump() { while (active < CONCURRENT && next < ids.length) { active++; one(ids[next++], 0); } }
        if (!ids.length) { finish(); return; }
        pump();
    }
    function retryAssets(ids) { if (ids && ids.length) load(ids); }
    TLMG.reload = function () { load(); };

    // ------------------------------------------------------------------
    // series
    // ------------------------------------------------------------------
    var FB_TRACK_SIGNAL = {
        1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)', 5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)',
        9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA', 13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA', 31: 'Rx1 mV', 32: 'Rx2 mV', 33: 'Tx1 V', 34: 'Tx2 V',
        35: 'Supply V', 36: 'Modem mV', 154: 'Root V', 155: 'Root mA', 227: 'UG V', 228: 'UG mA', 327: 'HHPR', 328: 'DPR', 329: 'HPR', 337: 'PILOT mA', 499: 'PILOT V', 610: 'Co_Hg mA', 611: 'Co_Hg V'
    };
    var SHORT_BY_ID = { 1: 'If mA', 2: 'Ir mA', 4: 'Choke V', 5: 'Charger mA' };
    function classify(name, isBin, isDrv) {
        if (isBin) return 'bin';
        if (isDrv) return 'drv';
        var n = String(name || '');
        if (mgG.kind === 'ips') return /^\s*I|\bmA\b|CURRENT/i.test(n) ? 'ma' : (/^\s*V|\bV\b|VOLT/i.test(n) ? 'v' : 'other');
        var l = n.toLowerCase();
        if (l.indexOf('ma') > -1 || l.indexOf('mv') > -1 || l.indexOf('-c') > -1) return 'ma';
        if (/\bv\b|volt/.test(l) || /v\)?$/.test(l)) return 'v';
        /* v618.28: RDPMS naming -- I... = current (ISIG RG, ICOSIG, IROSIG, IPT, ITC),
           V... = voltage (VSIG RG, VCOSIG, VPT, VTC); before, signal currents fell
           into 'other' and the mA filter showed "No data". */
        if (/^\s*I[A-Z]/.test(n)) return 'ma';
        if (/^\s*V[A-Z]/.test(n)) return 'v';
        return 'other';
    }
    function relayName(aid, id, first, rawName) {
        var key = String(aid) + '_' + String(id), n = '';
        if (window.dlAssetRoleMap && window.dlAssetRoleMap[key]) n = window.dlAssetRoleMap[key];
        if (!n && fn('resolveDataloggerDisplayName')) {
            try { n = window.resolveDataloggerDisplayName(aid, id, { DataType: first.DataType, AttributeId: id, AssetAttributeId: id, DataloggerAttributeId: id, Value: first.Value, DataloggerAttribute: first.DataloggerAttribute }); } catch (e) { n = ''; }
            if (n && /^attr\s*\d+$/i.test(n)) n = '';
        }
        if (!n) {
            var live = window.wsLiveData && window.wsLiveData[aid], rl = live && live.dlRelays;
            if (rl) { var r = rl[String(id)] || rl[id]; if (r && r.displayName) n = r.displayName; }
        }
        if (!n && window.dlRoleNameMap && window.dlRoleNameMap[String(id)]) n = window.dlRoleNameMap[String(id)];
        if (!n && rawName && !/^attr\s*\d+$/i.test(rawName)) n = rawName;
        return n || ('Relay ' + id);
    }
    function analogName(aid, id, first, rawName, liveNames) {
        var dt = String(first.DataType || '');
        if (mgG.kind === 'ips' && fn('ipsAttrName')) { try { var q = window.ipsAttrName(aid, id, dt, rawName); if (q) return q; } catch (e) { /* fall through */ } }
        var fbAllowed = (mgG.kind === 'track' || mgG.kind === 'signal');
        var rn = liveNames[id] || rawName || '';
        var dn = '';
        var g = fn('getAttrDisplayNamePlain');
        if (g) { try { dn = g(rn || ('Attr ' + id), id, aid) || ''; } catch (e) { dn = ''; } }
        if ((!dn || /^attr\s*\d+$/i.test(dn)) && fn('getBulkAliasName')) { try { dn = window.getBulkAliasName(aid, rawName, id) || ''; } catch (e2) { dn = ''; } }
        if ((!dn || /^attr\s*\d+$/i.test(dn)) && window.assetAttributeMap && window.assetAttributeMap[String(id)]) dn = window.assetAttributeMap[String(id)];
        if ((!dn || /^attr\s*\d+$/i.test(dn)) && rn) dn = rn;
        if ((!dn || /^attr\s*\d+$/i.test(dn)) && fbAllowed && FB_TRACK_SIGNAL[id]) dn = FB_TRACK_SIGNAL[id];
        return dn || ('Attr ' + id);
    }
    function keyOf(aid, id) { return String(aid) + '||' + String(id); }

    function buildSeries() {
        mgG.series = {}; mgG.order = [];
        var start = mgG.start, end = mgG.end;
        // drop virtual (IPS group) assets from a previous build; they are re-derived below
        Object.keys(mgG.assets).forEach(function (k) { if (mgG.assets[k].virtual) delete mgG.assets[k]; });
        mgG.assetIds = mgG.realIds.slice();
        mgG.realIds.forEach(function (aid) {
            var A = mgG.assets[aid], rows = mgG.raw[aid] || [], attrIdx = 0;
            var live = window.wsLiveData && (window.wsLiveData[aid] || window.wsLiveData[parseInt(aid, 10)]);
            var liveNames = {}, knownRelays = {}, hasLiveRelays = false;
            if (live && live.attrs) Object.keys(live.attrs).forEach(function (k) { var a = live.attrs[k]; if (!a) return; if (a.AttrId) liveNames[a.AttrId] = k; if (a.AssetAttributeId) liveNames[a.AssetAttributeId] = k; });
            if (live && live.dlRelays) Object.keys(live.dlRelays).forEach(function (k) { var r = live.dlRelays[k]; knownRelays[String(k).toLowerCase()] = true; if (r && r.displayName) knownRelays[String(r.displayName).toLowerCase()] = true; hasLiveRelays = true; });
            var analogMaps = {};   // name -> {t: v}  (Track derived)
            rows.forEach(function (row) {
                if (!row || row.AttributeId === undefined || row.AttributeId === null || !row.Values) return;
                var id = row.AttributeId, values = row.Values, vk = Object.keys(values); if (!vk.length) return;
                var first = values['1'] || values[vk[0]] || {};
                var dt = String(first.DataType || '').toLowerCase();
                var rawName = first.AssetAttributeName || first.AttributeName || row.AttributeName || '';
                var src = [], seq = 0, allBin = true;
                vk.forEach(function (k) {
                    var e = values[k]; if (!e) return;
                    var t = entryTime(e); if (isNaN(t) || t <= 0) return;
                    var v = parseFloat(e.Value); if (isNaN(v)) return;
                    if (v !== 0 && v !== 1) allBin = false;
                    src.push({ t: t, v: v, i: seq++ });
                });
                if (!src.length) return;
                src.sort(function (a, b) { return a.t - b.t || a.i - b.i; });
                var histDL = dt === 'datalogger';
                var isRelay;
                if (mgG.kind === 'ips') isRelay = false;
                else if (mgG.kind === 'eld') isRelay = histDL || allBin;
                else if (histDL && allBin) {
                    var roleKey = String(aid) + '_' + String(id);
                    isRelay = !hasLiveRelays || !!(window.dlAssetRoleMap && window.dlAssetRoleMap[roleKey]);
                    if (!isRelay) { var cand = relayName(aid, id, first, rawName); isRelay = !!knownRelays[String(cand).toLowerCase()]; }
                } else isRelay = false;
                var name = isRelay ? relayName(aid, id, first, rawName) : analogName(aid, id, first, rawName, liveNames);

                var pts = [], actual = [], before = null;
                for (var i = 0; i < src.length; i++) {
                    var p = src[i];
                    if (p.t < start) { before = p.v; continue; }
                    if (p.t > end) break;
                    var fv = Number(zf(p.v)); pts.push([p.t, fv]); actual.push([p.t, fv]);
                }
                if (before !== null) pts.unshift([start, Number(zf(before))]);
                else if (pts.length && pts[0][0] > start) pts.unshift([start, pts[0][1]]);
                if (!pts.length) return;
                if (pts[pts.length - 1][0] < end) pts.push([end, pts[pts.length - 1][1]]);

                var key = keyOf(aid, id);
                if (mgG.series[key]) {          // History API can repeat AssetId+AttributeId: merge
                    var ex = mgG.series[key], merged = ex.pts.concat(pts).sort(function (a, b) { return a[0] - b[0]; }), uniq = [], seen = {};
                    merged.forEach(function (q) { var sig = q[0] + '|' + q[1]; if (seen[sig]) return; seen[sig] = true; uniq.push(q); });
                    ex.pts = uniq; ex.actual = ex.actual.concat(actual).sort(function (a, b) { return a[0] - b[0]; });
                    return;
                }
                var kind = classify(name, isRelay, false);
                mgG.series[key] = {
                    key: key, assetId: String(aid), attrId: id, name: name, assetName: A.name, kind: kind, unit: unitFor(name, kind),
                    color: seriesColor(A.idx, attrIdx++), pts: pts, actual: actual, isBin: isRelay,
                    live: isRelay ? { src: 'ws', aid: aid, name: name, dl: true } : (mgG.kind === 'ips' ? { src: 'ips', aid: aid, attrId: id } : { src: 'ws', aid: aid, name: name, attrId: id, dl: false })
                };
                mgG.order.push(key);
                if (mgG.checked[key] === undefined) mgG.checked[key] = true;
                if (!isRelay) {
                    var vm = {}; actual.forEach(function (q) { vm[q[0]] = q[1]; });
                    analogMaps[name] = vm;
                    if (rawName && rawName !== name) analogMaps[rawName] = vm;
                    if (SHORT_BY_ID[id] && !analogMaps[SHORT_BY_ID[id]]) analogMaps[SHORT_BY_ID[id]] = vm;
                }
            });
            if (mgG.kind === 'track') addDerived(aid, analogMaps, attrIdx);
        });
        if (mgG.kind === 'ips') groupIpsBatt();
        Object.keys(mgG.checked).forEach(function (k) { if (!mgG.series[k]) delete mgG.checked[k]; });
        mgG.model = null;
        updateCount();
    }

    // Track derived series: the same formulas as the single-asset graph
    // (calculateDerivedValues on a carry-forward snapshot at every timestamp).
    function addDerived(aid, maps, attrIdx) {
        var calc = fn('calculateDerivedValues'), defs = window._DERIVED_DEFS || [];
        var names = Object.keys(maps);
        if (!calc || !defs.length || !names.length) return;
        var tsSet = {}; names.forEach(function (n) { Object.keys(maps[n]).forEach(function (t) { tsSet[t] = true; }); });
        var ts = Object.keys(tsSet).map(Number).sort(function (a, b) { return a - b; });
        if (!ts.length) return;
        var sorted = {}, ptr = {}, last = {};
        names.forEach(function (n) { sorted[n] = Object.keys(maps[n]).map(Number).sort(function (a, b) { return a - b; }); ptr[n] = 0; last[n] = null; });
        var out = {}; defs.forEach(function (d) { out[d.key] = []; });
        var stride = Math.max(1, Math.floor(ts.length / 4000));   // bound the work for very dense histories
        for (var i = 0; i < ts.length; i += stride) {
            var t = ts[i], snap = {};
            names.forEach(function (n) {
                var arr = sorted[n]; while (ptr[n] < arr.length && arr[ptr[n]] <= t) { last[n] = maps[n][arr[ptr[n]]]; ptr[n]++; }
                if (last[n] !== null) snap[n] = { Value: last[n] };
            });
            var dv; try { dv = calc(snap, aid) || {}; } catch (e) { dv = {}; }
            defs.forEach(function (d) { var v = dv[d.key]; if (v !== undefined && v !== null && !isNaN(v)) out[d.key].push([t, Number(zf(v))]); });
        }
        var A = mgG.assets[aid];
        defs.forEach(function (d, di) {
            var pts = out[d.key]; if (!pts.length) return;
            if (pts[0][0] > mgG.start) pts.unshift([mgG.start, pts[0][1]]);
            if (pts[pts.length - 1][0] < mgG.end) pts.push([mgG.end, pts[pts.length - 1][1]]);
            var key = keyOf(aid, 'DRV:' + d.key), label = d.label + ' (' + d.unit + ')';
            mgG.series[key] = {
                key: key, assetId: String(aid), attrId: 'DRV:' + d.key, name: label, assetName: A.name, kind: 'drv', unit: d.unit,
                color: seriesColor(A.idx, attrIdx + di), pts: pts, actual: out[d.key].slice(), isBin: false, isDrv: true, drvKey: d.key, live: { src: 'drv', aid: aid, key: d.key }
            };
            mgG.order.push(key);
            if (mgG.checked[key] === undefined) mgG.checked[key] = true;
        });
    }

    // IPS: sum the "BATT CHARGING n" / "BATT DISCHARGING n" assets into one
    // series per bucket + base attribute, exactly like the IPS graph / table.
    function chargingBucket(name) { var u = String(name || '').toUpperCase(); if (u.indexOf('DISCHARGING') > -1) return 'discharging'; if (u.indexOf('CHARGING') > -1) return 'charging'; return null; }
    function baseAssetName(name) { return String(name || '').replace(/[\s\-_]*\d+\s*$/, '').trim() || String(name || ''); }
    function baseAttrName(name) { var f = fn('ipsBattBaseAttrName'); if (f) return f(name); return String(name || '').replace(/(DISCHARGING|DISCHARGER|DISCHARGE|DISCHAR|CHARGING|CHARGER|CHARGE|CHAR)[\-_]\s*\d+/gi, '$1').replace(/\s{2,}/g, ' ').trim(); }
    function sumStep(list) {
        var tset = {}; list.forEach(function (pts) { pts.forEach(function (p) { tset[p[0]] = true; }); });
        var times = Object.keys(tset).map(Number).sort(function (a, b) { return a - b; });
        var ptr = list.map(function () { return 0; }), lastV = list.map(function () { return null; }), out = [];
        times.forEach(function (t) {
            list.forEach(function (pts, i) { while (ptr[i] < pts.length && pts[ptr[i]][0] <= t) { lastV[i] = pts[ptr[i]][1]; ptr[i]++; } });
            var s = 0, has = false; lastV.forEach(function (v) { if (v !== null && !isNaN(v)) { s += v; has = true; } });
            out.push([t, has ? Number(zf(s)) : 0]);
        });
        return out;
    }
    function groupIpsBatt() {
        var groups = {}, gorder = [];
        mgG.order.forEach(function (key) {
            var s = mgG.series[key]; if (!s) return;
            var bucket = chargingBucket(s.assetName); if (!bucket) return;
            // like the flat IPS table (ipsGroupCurrentRows): only CURRENT rows are summed
            // across banks; bank voltages stay with their own asset.
            if (s.kind !== 'ma') return;
            var gk = bucket + '||' + baseAttrName(s.name) + '||' + baseAssetName(s.assetName);
            if (!groups[gk]) { groups[gk] = { bucket: bucket, base: baseAssetName(s.assetName), attrId: s.attrId, name: baseAttrName(s.name), members: [] }; gorder.push(gk); }
            groups[gk].members.push(key);
        });
        if (!gorder.length) return;
        var gi = 0;
        gorder.forEach(function (gk) {
            var g = groups[gk], mem = g.members.map(function (k) { return mgG.series[k]; }).filter(Boolean);
            if (!mem.length) return;
            var vAid = 'battgrp::' + g.bucket;
            if (!mgG.assets[vAid]) { mgG.assets[vAid] = { id: vAid, name: g.base || (g.bucket === 'discharging' ? 'BATT DISCHARGING' : 'BATT CHARGING'), idx: mgG.assetIds.length + gi++, color: '', virtual: true, note: 'sum of ' + mem.length }; mgG.assets[vAid].color = assetColor(mgG.assets[vAid].idx); mgG.assetIds.push(vAid); }
            var V = mgG.assets[vAid];
            var key = 'battgrp::' + gk, attrIdx = Object.keys(mgG.series).filter(function (k) { return mgG.series[k].assetId === vAid; }).length;
            var pts = sumStep(mem.map(function (s) { return s.pts; }));
            mgG.series[key] = {
                key: key, assetId: vAid, attrId: g.attrId, name: g.name, assetName: V.name, kind: classify(g.name, false, false), unit: unitFor(g.name, classify(g.name, false, false)),
                color: seriesColor(V.idx, attrIdx), pts: pts, actual: pts.slice(0, Math.max(1, pts.length - 1)), isBin: false, isGroup: true,
                members: mem.map(function (s) { return { aid: s.assetId, attrId: s.attrId }; }), live: { src: 'sum', members: mem.map(function (s) { return { aid: s.assetId, attrId: s.attrId }; }) }
            };
            g.members.forEach(function (k) { delete mgG.series[k]; delete mgG.checked[k]; });
            mgG.order.push(key);
            if (mgG.checked[key] === undefined) mgG.checked[key] = true;
        });
        mgG.order = mgG.order.filter(function (k) { return !!mgG.series[k]; });
        // real assets that were fully absorbed into a group have no series left; drop them from the chrome
        mgG.assetIds = mgG.assetIds.filter(function (aid) { var A = mgG.assets[aid]; if (A.virtual) return true; return mgG.order.some(function (k) { return mgG.series[k].assetId === aid; }) || !chargingBucket(A.name); });
    }

    // ------------------------------------------------------------------
    // attribute panel (grouped by asset)
    // ------------------------------------------------------------------
    function passesFilter(s) {
        var f = mgG.filter;
        if (f === 'all') return true;
        if (f === 'bin') return s.kind === 'bin';
        if (f === 'drv') return s.kind === 'drv';
        if (f === 'ma') return s.kind === 'ma';
        if (f === 'v') return s.kind === 'v' || s.kind === 'other';
        return true;
    }
    function seriesOfAsset(aid) { return mgG.order.filter(function (k) { return mgG.series[k].assetId === aid; }).map(function (k) { return mgG.series[k]; }); }
    function renderPanel() {
        var h = '';
        mgG.assetIds.forEach(function (aid) {
            var A = mgG.assets[aid], list = seriesOfAsset(aid), failed = !!mgG.failed[aid], folded = !!mgG.fold[aid], collapsed = !!mgG.collapsed[aid];
            var on = list.filter(function (s) { return mgG.checked[s.key] !== false; }).length;
            h += '<div class="tlmg-agrp' + (folded ? ' folded' : '') + '" data-mg-aid="' + esc(aid) + '" style="--ac:' + A.color + ';--ac-bg:' + hexA(A.color, .10) + ';--ac-bg2:' + hexA(A.color, .18) + '">' +
                '<div class="tlmg-ahead" data-mg-aid="' + esc(aid) + '" role="button" tabindex="0" aria-expanded="' + (folded ? 'false' : 'true') + '">' +
                '<span class="tlmg-chev"><i class="fas fa-chevron-down"></i></span><span class="tlmg-sw"></span>' +
                '<span class="tlmg-aname" title="' + esc(A.name) + '">' + esc(A.name) + '</span>' +
                (A.virtual ? '<span class="tlmg-abadge ok">Σ ' + esc(A.note || 'group') + '</span>' : '') +
                '<span class="tlmg-ameta" data-mg-ameta>' + on + ' / ' + list.length + ' shown</span>' +
                (failed ? '<span class="tlmg-abadge"><i class="fas fa-plug-circle-xmark"></i> load failed</span><button type="button" class="tlmg-mini" data-mg-retry="' + esc(aid) + '"><i class="fas fa-rotate-right"></i> Retry</button>' : '') +
                (!failed && !list.length ? '<span class="tlmg-abadge" style="background:rgba(255,255,255,.08);color:var(--at-t3,rgba(255,255,255,.5))">no data in range</span>' : '') +
                '<span class="tlmg-aact">' +
                '<button type="button" class="tlmg-mini" data-mg-asset-all="1" data-mg-aid="' + esc(aid) + '" title="Tick every attribute of this asset">All</button>' +
                '<button type="button" class="tlmg-mini" data-mg-asset-all="0" data-mg-aid="' + esc(aid) + '" title="Untick every attribute of this asset">None</button>' +
                '<button type="button" class="tlmg-mini" data-mg-collapse="' + esc(aid) + '" title="' + (collapsed ? 'Show this asset in the graph' : 'Hide this asset from the graph (keeps the ticks)') + '"><i class="fas ' + (collapsed ? 'fa-eye-slash' : 'fa-eye') + '"></i></button>' +
                '</span></div>';
            h += '<div class="tlmg-abody">';
            list.forEach(function (s) {
                var checked = mgG.checked[s.key] !== false, st = s.stats || null;
                var last = s.actual.length ? s.actual[s.actual.length - 1][1] : (s.pts.length ? s.pts[s.pts.length - 1][1] : null);
                h += '<label class="tlmg-chip' + (checked ? '' : ' off') + (passesFilter(s) ? '' : ' hide') + '" data-mg-chip="' + esc(s.key) + '" style="--ac:' + s.color + '" title="' + esc(s.assetName + ' · ' + s.name) + ' — ' + kindLabel(s.kind) + '">' +
                    '<input type="checkbox" data-mg-key="' + esc(s.key) + '" ' + (checked ? 'checked' : '') + '/>' +
                    '<span class="tlmg-dot" style="background:' + s.color + '"></span>' +
                    '<span class="tlmg-an">' + esc(s.name) + '</span>' +
                    '<span class="tlmg-kind">' + (s.kind === 'bin' ? '0/1' : s.kind === 'drv' ? 'DRV' : (s.unit || kindLabel(s.kind))) + '</span>' +
                    '<span class="tlmg-av" data-mg-val style="color:' + s.color + '">' + (s.kind === 'bin' ? valHtml(last, 'bin') : fmtVal(last)) + '</span></label>';
                void st;
            });
            h += '</div></div>';
        });
        $('#mgPanel').html(h || '<div class="tlgv-empty">No attributes</div>');
        updateCount(); refreshLive(true);
    }
    function applyPanelFilter() {
        mgG.order.forEach(function (k) {
            var s = mgG.series[k];
            $('#mgPanel .tlmg-chip[data-mg-chip="' + cssKey(k) + '"]').toggleClass('hide', !passesFilter(s));
        });
    }
    function updateCount() {
        var total = mgG.order.length, on = mgG.order.filter(function (k) { return mgG.checked[k] !== false; }).length;
        $('#mgCount').text(on + ' / ' + total + ' selected · ' + mgG.assetIds.length + ' asset' + (mgG.assetIds.length > 1 ? 's' : ''));
        mgG.assetIds.forEach(function (aid) {
            var list = seriesOfAsset(aid), n = list.filter(function (s) { return mgG.checked[s.key] !== false; }).length;
            $('#mgPanel .tlmg-agrp[data-mg-aid="' + cssKey(aid) + '"] [data-mg-ameta]').text(n + ' / ' + list.length + ' shown');
        });
    }
    function setAllChecked(on, aid) {
        mgG.order.forEach(function (k) { if (aid === null || mgG.series[k].assetId === aid) mgG.checked[k] = on; });
        var sel = aid === null ? '#mgPanel [data-mg-key]' : '#mgPanel .tlmg-agrp[data-mg-aid="' + cssKey(aid) + '"] [data-mg-key]';
        $(sel).prop('checked', on).closest('.tlmg-chip').toggleClass('off', !on);
        updateCount(); render();
    }

    // ------------------------------------------------------------------
    // live values (WebSocket store), 2 s
    // ------------------------------------------------------------------
    function liveIndex(aid, cache) {
        if (cache[aid]) return cache[aid];
        var store = window.wsLiveData || {}, a = store[aid] || store[parseInt(aid, 10)];
        var idx = { byName: {}, byId: {}, dl: {}, attrs: a && a.attrs };
        if (a && a.attrs) Object.keys(a.attrs).forEach(function (k) {
            var at = a.attrs[k]; if (!at) return;
            var id = parseInt(at.AttrId || at.AssetAttributeId || 0, 10), v = num(at.Value);
            idx.byName[k] = v; if (id) idx.byId[id] = v;
            try { var g = fn('getAttrDisplayNamePlain'); if (g) { var dn = g(k, id, aid); if (dn) idx.byName[dn] = v; var dn2 = g(k, id); if (dn2) idx.byName[dn2] = v; } } catch (e) { /* ignore */ }
        });
        if (a && a.dlRelays) Object.keys(a.dlRelays).forEach(function (k) { var r = a.dlRelays[k]; if (!r) return; idx.dl[k] = r.isPickup ? 1 : 0; if (r.displayName) idx.dl[r.displayName] = r.isPickup ? 1 : 0; if (r.role) idx.dl[String(r.role)] = r.isPickup ? 1 : 0; });
        cache[aid] = idx; return idx;
    }
    function liveValue(s, cache) {
        var L = s.live; if (!L) return null;
        if (L.src === 'sum') {
            var sum = 0, has = false;
            L.members.forEach(function (m) { var v = liveIndex(m.aid, cache).byId[parseInt(m.attrId, 10)]; if (v !== undefined && v !== null) { sum += v; has = true; } });
            return has ? sum : null;
        }
        var idx = liveIndex(L.aid, cache);
        if (L.src === 'drv') {
            if (!idx.attrs || !Object.keys(idx.attrs).length) return null;
            var calc = fn('calculateDerivedValues'); if (!calc) return null;
            try { var dv = calc(idx.attrs, L.aid) || {}; var x = dv[L.key]; return (x === undefined || x === null || isNaN(x)) ? null : x; } catch (e) { return null; }
        }
        if (L.src === 'ips') { var v1 = idx.byId[parseInt(L.attrId, 10)]; return v1 === undefined ? null : v1; }
        if (L.dl) { var d = idx.dl[L.name]; if (d === undefined && L.attrId !== undefined) d = idx.dl[String(L.attrId)]; return d === undefined ? null : d; }
        var v2 = (L.attrId !== undefined) ? idx.byId[parseInt(L.attrId, 10)] : undefined;
        if (v2 === undefined || v2 === null) v2 = idx.byName[L.name];
        return (v2 === undefined) ? null : v2;
    }
    function refreshLive(force) {
        if (!$('#mgWrap').length) return;
        var cache = {}, lanes = mgG.mode === 'stacked', cards = mgG.mode === 'individual';
        mgG.order.forEach(function (k) {
            var s = mgG.series[k], lv = liveValue(s, cache), isLive = lv !== null && lv !== undefined;
            var last = s.actual.length ? s.actual[s.actual.length - 1][1] : (s.pts.length ? s.pts[s.pts.length - 1][1] : null);
            var v = isLive ? lv : last;
            if (v !== null && v !== undefined && s.kind !== 'bin') v = Number(zf(v));
            var $pv = $('#mgPanel .tlmg-chip[data-mg-chip="' + cssKey(k) + '"] [data-mg-val]');
            if ($pv.length) {
                var html = s.kind === 'bin' ? valHtml(v, 'bin') : fmtVal(v);
                if ($pv.html() !== html) { $pv.html(html); if (!force) { $pv.removeClass('tlgv-flash'); void $pv[0].offsetWidth; $pv.addClass('tlgv-flash'); } }
            }
            if (!lanes && !cards) return;
            if (lanes && mgG.cursorKey === k) return;   // hovered lane shows the cursor value
            var $item = $('#mgAlt ' + (lanes ? '.tlgv-lane' : '.tlgv-card') + '[data-key="' + cssKey(k) + '"]'); if (!$item.length) return;
            var $val = $item.find('[data-tlgv-val]'), h2 = valHtml(v, s.kind, s.unit);
            if ($val.html() !== h2) { $val.html(h2); if (!force) { $val.removeClass('tlgv-flash'); void $val[0].offsetWidth; $val.addClass('tlgv-flash'); } }
            $item.find('[data-tlgv-dot]').toggleClass('off', !isLive);
        });
    }
    var liveTimer = null;
    function ensureLiveTimer() {
        if (liveTimer) return;
        liveTimer = setInterval(function () {
            if (!document.getElementById('mgWrap')) { clearInterval(liveTimer); liveTimer = null; return; }
            refreshMax();
            try { refreshLive(false); } catch (e) { /* ignore */ }
        }, LIVE_MS);
    }

    // ------------------------------------------------------------------
    // model + render dispatch
    // ------------------------------------------------------------------
    function buildModel() {
        var many = mgG.assetIds.length > 1, out = [];
        mgG.order.forEach(function (k) {
            var s = mgG.series[k];
            if (mgG.checked[k] === false || !passesFilter(s) || mgG.collapsed[s.assetId]) return;
            var pts = compress(s.pts.slice());
            var A = mgG.assets[s.assetId] || { name: s.assetName, color: '#22d3ee', idx: 0 };
            var m = { key: k, name: many ? (A.name + ' · ' + s.name) : s.name, short: s.name, sub: A.name, color: s.color, kind: s.kind, unit: s.unit, pts: pts, actual: s.actual, live: s.live, asset: A };
            m.stats = computeStats(m, mgG.start, mgG.end);
            out.push(m);
        });
        mgG.model = { xMin: mgG.start, xMax: mgG.end, series: out };
        var c = window.TLGV && window.TLGV.ctx && window.TLGV.ctx.multi;
        if (c) { c.model = null; c.cursorKey = mgG.cursorKey; }
        return mgG.model;
    }
    function visible(model) {
        var q = String(mgG.search || '').trim().toLowerCase();
        return model.series.filter(function (s) { if (mgG.hidden[s.key]) return false; if (q && s.name.toLowerCase().indexOf(q) < 0) return false; return true; });
    }
    function groupsOf(list) {
        var out = [], by = {};
        mgG.assetIds.forEach(function (aid) { by[aid] = { asset: mgG.assets[aid], list: [] }; out.push(by[aid]); });
        list.forEach(function (s) { var g = by[s.asset.id]; if (g) g.list.push(s); });
        return out.filter(function (g) { return g.list.length; });
    }
    function disposeAlt() {
        (mgG.charts || []).forEach(function (ch) { try { ch.dispose(); } catch (e) { /* ignore */ } });
        mgG.charts = []; mgG.cardCharts = {}; mgG.cursorKey = null;
        if (mgG.io) { try { mgG.io.disconnect(); } catch (e) { /* ignore */ } mgG.io = null; }
        if (mgG.ro) { try { mgG.ro.disconnect(); } catch (e) { /* ignore */ } mgG.ro = null; }
        var c = window.TLGV && window.TLGV.ctx && window.TLGV.ctx.multi; if (c) c.charts = mgG.charts;
    }
    function disposeAll() {
        disposeAlt();
        if (mgG.chart) { try { mgG.chart.dispose(); } catch (e) { /* ignore */ } mgG.chart = null; }
        abortAll();
        $(window).off('resize.tlmg');
    }
    TLMG.dispose = disposeAll;
    function render() {
        if (mgG.loading || !$('#mgWrap').length) return;
        var model = buildModel();
        syncBar();
        $('#mgLoad').hide();
        if (!model.series.length) { disposeAlt(); if (mgG.chart) { mgG.chart.dispose(); mgG.chart = null; } $('#mgChart,#mgAlt,#mgLegend').hide(); $('#mgEmpty').show(); return; }
        $('#mgEmpty').hide();
        if (mgG.mode === 'overlay') {
            disposeAlt(); $('#mgAlt').empty().hide();
            renderOverlay(model);
        } else {
            if (mgG.chart) { try { mgG.chart.dispose(); } catch (e) { /* ignore */ } mgG.chart = null; }
            $('#mgChart,#mgLegend').hide(); $('#mgAlt').show();
            if (mgG.mode === 'stacked') renderStacked(model); else renderIndividual(model);
        }
        ensureLiveTimer();
        $(window).off('resize.tlmg').on('resize.tlmg', function () { if (mgG.chart) mgG.chart.resize(); (mgG.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { /* ignore */ } }); });
    }
    TLMG.render = render;
    function resetZoom() {
        if (mgG.mode === 'overlay') { if (mgG.chart) mgG.chart.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); return; }
        (mgG.charts || []).forEach(function (ch) { ch.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); });
    }
    function exportPng() {
        var bg = theme().pngBg;
        if (mgG.mode === 'overlay') { if (mgG.chart) download(mgG.chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: bg }), 'telemetry_multi_overlay.png'); return; }
        if (mgG.mode === 'stacked') {
            var ch = mgG.charts[0]; if (!ch) return;
            ch.setOption({ title: (mgG.titles || []).map(function (t) { return $.extend({}, t, { show: true }); }) });
            var url = ch.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: bg });
            ch.setOption({ title: mgG.titles || [] });
            download(url, 'telemetry_multi_stacked.png'); return;
        }
        // individual: draw every card, compose via a temporary connect group
        $('#mgAlt .tlgv-card').each(function () { initCard(this); });
        var first = mgG.charts[0]; if (!first) return;
        var grp = 'tlmg-png';
        mgG.charts.forEach(function (c) { c.group = grp; c.setOption({ title: { show: true }, grid: { top: 26 } }); });
        window.echarts.connect(grp);
        var u = first.getConnectedDataURL ? first.getConnectedDataURL({ type: 'png', pixelRatio: 2, backgroundColor: bg }) : first.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: bg });
        mgG.charts.forEach(function (c) { c.setOption({ title: { show: false }, grid: { top: 12 } }); c.group = ''; });
        window.echarts.disconnect(grp);
        download(u, 'telemetry_multi_individual.png');
    }

    // ------------------------------------------------------------------
    // OVERLAY — one chart, three axes, legend + tooltip grouped by asset
    // ------------------------------------------------------------------
    function renderOverlay(model) {
        var el = document.getElementById('mgChart'); if (!el || !window.echarts) return;
        var T = theme(), list = visible(model), span = model.xMax - model.xMin, narrow = ($('#mgWrap').width() || 900) < 640;
        // legend strip (per asset, click = collapse/show that asset)
        var $lg = $('#mgLegend').empty().show();
        mgG.assetIds.forEach(function (aid) {
            var A = mgG.assets[aid], n = seriesOfAsset(aid).filter(function (s) { return mgG.checked[s.key] !== false && passesFilter(s); }).length;
            if (!n && !mgG.collapsed[aid]) return;
            $lg.append('<span class="tlmg-lg' + (mgG.collapsed[aid] ? ' off' : '') + '" data-mg-collapse="' + esc(aid) + '" style="--ac-bg:' + hexA(A.color, .12) + '" title="Click to ' + (mgG.collapsed[aid] ? 'show' : 'hide') + ' this asset"><span class="tlmg-sw" style="--ac:' + A.color + '"></span>' + esc(A.name) + ' <small>' + n + '</small></span>');
        });
        $('#mgChart').show();
        if (mgG.chart) { try { mgG.chart.dispose(); } catch (e) { /* ignore */ } }
        var chart = mgG.chart = window.echarts.init(el, null, { renderer: 'canvas' });
        var maxMA = 0, maxV = 0, hasBin = false, hasMA = false, hasV = false, series = [];
        list.forEach(function (s) {
            var isBin = s.kind === 'bin', yi = isBin ? 2 : (s.kind === 'ma' || s.kind === 'drv') ? 0 : 1;
            if (isBin) hasBin = true; else if (yi === 0) { hasMA = true; if (s.stats.max > maxMA) maxMA = s.stats.max; } else { hasV = true; if (s.stats.max > maxV) maxV = s.stats.max; }
            var o = {
                id: s.key, name: s.name, type: 'line', step: 'end', xAxisIndex: 0, yAxisIndex: yi, showSymbol: false, symbol: 'circle', symbolSize: 3,
                connectNulls: true, animation: false, clip: true, sampling: 'lttb',
                lineStyle: { width: isBin ? 1.4 : 1.7, color: s.color, type: s.kind === 'drv' ? 'dashed' : 'solid' },
                itemStyle: { color: s.color }, emphasis: { focus: 'series', lineStyle: { width: 2.6 } }, blur: { lineStyle: { opacity: 0.18 } },
                data: s.pts
            };
            // relays share the full-height 0/1 axis: a fill would blanket the analog lines
            if (isBin) o.areaStyle = { color: hexA(s.color, .05) };
            series.push(o);
        });
        var byKey = {}; list.forEach(function (s) { byKey[s.key] = s; });
        var tip = {
            trigger: 'axis', confine: true, appendToBody: false, axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.7)', type: 'dashed' } },
            backgroundColor: T.tipBg, borderColor: 'rgba(34,211,238,.28)', borderWidth: 1, padding: [8, 12], textStyle: { color: T.tipTxt, fontSize: 11.5 },
            extraCssText: 'box-shadow:0 8px 32px rgba(0,0,0,.45);border-radius:10px;max-width:760px;',
            formatter: function (params) {
                if (!params || !params.length) return '';
                var t = params[0].axisValue != null ? Number(params[0].axisValue) : Number(params[0].value[0]);
                var h = '<div style="font-family:JetBrains Mono,monospace;color:#22d3ee;font-weight:700;margin-bottom:6px;">' + fmtFull(t) + '</div><div style="display:flex;flex-wrap:wrap;gap:6px 18px;">';
                groupsOf(list).forEach(function (g) {
                    h += '<div style="min-width:180px;"><div style="font-weight:800;color:' + g.asset.color + ';margin-bottom:2px;">' + esc(g.asset.name) + '</div>';
                    g.list.forEach(function (s) {
                        var v = valueAt(s.pts, t);
                        h += '<div style="display:flex;align-items:center;gap:6px;white-space:nowrap;"><span style="width:8px;height:8px;border-radius:50%;background:' + s.color + ';flex-shrink:0;"></span>' +
                            '<span style="opacity:.8;flex:1;">' + esc(s.short) + '</span><b style="margin-left:8px;font-family:JetBrains Mono,monospace;">' + fmtVal(v, s.kind) + (s.kind === 'bin' || v === null ? '' : ' ' + esc(s.unit)) + '</b></div>';
                    });
                    h += '</div>';
                });
                return h + '</div>';
            }
        };
        var yAxes = [
            { type: 'value', name: hasMA ? 'mA' : '', min: 0, max: niceMax(maxMA), show: hasMA, position: 'left', nameTextStyle: { color: T.txt, fontSize: 10 }, axisLabel: { color: T.txt, fontSize: 10, fontFamily: 'JetBrains Mono,monospace', formatter: function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); } }, splitLine: { lineStyle: { color: T.split, type: 'dashed' } }, axisLine: { show: false }, axisTick: { show: false } },
            { type: 'value', name: hasV ? 'V' : '', min: 0, max: niceMax(maxV), show: hasV, position: 'right', nameTextStyle: { color: T.txt, fontSize: 10 }, axisLabel: { color: T.txt, fontSize: 10, fontFamily: 'JetBrains Mono,monospace', formatter: function (v) { return String(+v.toFixed(2)); } }, splitLine: { show: !hasMA, lineStyle: { color: T.split, type: 'dashed' } }, axisLine: { show: false }, axisTick: { show: false } },
            { type: 'value', name: hasBin ? '0/1' : '', min: 0, max: 1, interval: 1, show: hasBin, position: 'right', offset: hasV ? 52 : 0, nameTextStyle: { color: T.txt, fontSize: 10 }, axisLabel: { color: T.txt, fontSize: 10, formatter: function (v) { return v === 1 ? 'P' : v === 0 ? 'D' : ''; } }, splitLine: { show: false }, axisLine: { show: false }, axisTick: { show: false } }
        ];
        chart.setOption({
            backgroundColor: 'transparent', animation: false,
            legend: { type: 'scroll', top: 6, left: 12, right: 12, selectedMode: false, icon: 'roundRect', itemWidth: 12, itemHeight: 6, textStyle: { color: T.txt2, fontSize: 10.5 }, pageTextStyle: { color: T.txt }, pageIconColor: '#22d3ee', pageIconInactiveColor: T.axis, data: list.map(function (s) { return { name: s.name, itemStyle: { color: s.color } }; }) },
            grid: { left: narrow ? 44 : 58, right: (hasBin && hasV ? 96 : (hasBin || hasV) ? 52 : 18) + (narrow ? 0 : 8), top: 44, bottom: 64, containLabel: false },
            tooltip: tip,
            xAxis: { type: 'time', min: model.xMin, max: model.xMax, boundaryGap: false, splitNumber: narrow ? 4 : 8, axisLine: { lineStyle: { color: T.axis } }, axisLabel: { color: T.txt, fontSize: 10, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } }, splitLine: { show: true, lineStyle: { color: T.split } } },
            yAxis: yAxes,
            dataZoom: [
                { type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, moveOnMouseMove: true, minValueSpan: 60000 },
                { type: 'slider', filterMode: 'none', height: 20, bottom: 8, minValueSpan: 60000, backgroundColor: 'rgba(255,255,255,0.03)', borderColor: 'rgba(255,255,255,0.09)', fillerColor: 'rgba(34,211,238,0.12)', handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, moveHandleStyle: { color: 'rgba(34,211,238,.5)' }, textStyle: { color: T.txt, fontSize: 10 }, labelFormatter: function (v) { return fmtFull(v); }, dataBackground: { lineStyle: { color: T.axis }, areaStyle: { color: T.gridA } } }
            ],
            series: series
        }, true);
    }

    // ------------------------------------------------------------------
    // STACKED — lane board grouped by asset, hover isolated per lane
    // ------------------------------------------------------------------
    var DENS = { s: 46, m: 78, l: 124 };
    function assetHdrHtml(g, extraCls, styleExtra) {
        var A = g.asset, collapsed = !!mgG.collapsed[A.id];
        return '<div class="tlmg-ahdr' + (extraCls || '') + '" data-mg-hdr="' + esc(A.id) + '" style="--ac:' + A.color + ';--ac-bg:' + hexA(A.color, .14) + ';' + (styleExtra || '') + '">' +
            '<span class="tlmg-sw" style="--ac:' + A.color + '"></span><span class="tlmg-aname" title="' + esc(A.name) + '">' + esc(A.name) + '</span>' +
            (A.virtual ? '<span class="tlmg-abadge ok">Σ ' + esc(A.note || 'group') + '</span>' : '') +
            '<span class="tlmg-ameta">' + g.list.length + ' ' + (g.list.length === 1 ? 'series' : 'series') + (collapsed ? ' · hidden' : '') + '</span>' +
            (mgG.failed[A.id] ? '<span class="tlmg-abadge">load failed</span>' : '') +
            '<button type="button" class="tlmg-mini" data-mg-collapse="' + esc(A.id) + '" title="' + (collapsed ? 'Show' : 'Hide') + ' this asset"><i class="fas ' + (collapsed ? 'fa-eye-slash' : 'fa-eye') + '"></i></button></div>';
    }
    function renderStacked(model) {
        disposeAlt();
        var host = $('#mgAlt'), list = visible(model), T = theme();
        if (!list.length) { host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden lanes.</div>'); return; }
        var narrow = (host.width() || 900) < 640, GW = narrow ? 132 : 220;
        var laneH = DENS[prefs.density] || DENS.m, gap = 26, top = 8, HDR = 28, bottom = 64;   /* v618.28: room for each lane's time axis */
        var span = model.xMax - model.xMin;
        var groups = groupsOf(list), lanes = '', hdrs = '', layout = [], y = top;
        groups.forEach(function (g) {
            hdrs += assetHdrHtml(g, '', 'top:' + y + 'px;height:' + HDR + 'px;');
            y += HDR + 6;
            g.list.forEach(function (s) {
                layout.push({ s: s, y: y });
                lanes += '<div class="tlgv-lane" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + ';top:' + y + 'px;height:' + laneH + 'px;width:' + (GW - 16) + 'px;" title="' + esc(s.name) + ' — ' + kindLabel(s.kind) + '">' +
                    '<div class="tlgv-lane-top"><span class="tlgv-lane-name">' + esc(s.short) + '</span>' +
                    (s.unit ? '<span class="tlgv-unit">' + esc(s.unit) + '</span>' : '') +
                    '<span class="tlgv-lane-act">' +
                    '<button type="button" class="tlgv-ico" data-tlgv-focus="' + esc(s.key) + '" title="Open as individual chart" aria-label="Open ' + esc(s.name) + ' as individual chart"><i class="fas fa-up-right-and-down-left-from-center"></i></button>' +
                    '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide lane" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                    '</span></div>' +
                    '<div class="tlgv-lane-val" data-tlgv-val>' + valHtml(s.stats.last, s.kind, s.unit) + '</div>' +
                    '<div class="tlgv-lane-sub">' + esc(statsLine(s)) + '</div></div>';
                y += laneH + gap;
            });
            y += 6;
        });
        var H = y + bottom;
        host.html(
            '<div class="tlgv-cursor" data-tlgv-cursor><i class="fas fa-crosshairs"></i> <span data-tlgv-cursor-txt><span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span></span>' +
            '<span class="tlgv-spacer"></span><span>' + layout.length + ' lane' + (layout.length > 1 ? 's' : '') + ' · ' + groups.length + ' asset' + (groups.length > 1 ? 's' : '') + ' · hover a lane to read that lane · drag to pan · Ctrl+wheel to zoom</span></div>' +
            '<div class="tlgv-stack tlgv-dens-' + prefs.density + '" style="height:' + H + 'px"><div class="tlgv-stack-chart"></div><div class="tlmg-hdrs">' + hdrs + '</div><div class="tlgv-gutter" style="width:' + GW + 'px">' + lanes + '</div></div>'
        );
        var el = host.find('.tlgv-stack-chart')[0];
        var chart = window.echarts.init(el, null, { renderer: 'canvas' });
        mgG.charts.push(chart);
        var grids = [], xs = [], ys = [], series = [], titles = [], allX = [], left = GW + 44, right = 18;
        layout.forEach(function (L, i) {
            var s = L.s, last = i === layout.length - 1, isBin = s.kind === 'bin';
            grids.push({ left: left, right: right, top: L.y, height: laneH, show: true, backgroundColor: i % 2 ? T.gridB : T.gridA, borderColor: T.split, borderWidth: 1 });
            xs.push({
                type: 'time', gridIndex: i, min: model.xMin, max: model.xMax, boundaryGap: false,
                axisLine: { show: true, lineStyle: { color: T.axis } }, axisTick: { show: true }, splitNumber: narrow ? 4 : 8,
                axisLabel: { show: true, color: T.txt, fontSize: last ? 10 : 9, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } },
                splitLine: { show: true, lineStyle: { color: T.split } },
                axisPointer: { label: { show: false } }
            });
            ys.push({
                type: 'value', gridIndex: i, min: 0, max: isBin ? 1 : niceMax(s.stats.max), splitNumber: prefs.density === 'l' ? 3 : 2,
                axisLine: { show: true, lineStyle: { color: T.axis } }, axisTick: { show: false },
                axisLabel: { color: T.txt, fontSize: 9, fontFamily: 'JetBrains Mono,monospace', margin: 6, showMinLabel: true, formatter: isBin ? function (v) { return v === 1 ? 'P' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); } },
                splitLine: { show: true, lineStyle: { color: T.split, type: 'dashed' } }, axisPointer: { show: false }
            });
            series.push(laneSeries(s, i, i, model.xMax, prefs.density));
            titles.push({ show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: L.y + 4, textStyle: { color: s.color, fontSize: 11, fontWeight: 700 } });
            allX.push(i);
        });
        mgG.titles = titles;
        chart.setOption({
            backgroundColor: 'transparent', animation: false, title: titles, grid: grids, xAxis: xs, yAxis: ys, series: series,
            tooltip: { trigger: 'axis', showContent: false, axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.75)', width: 1, type: 'dashed' } } },
            axisPointer: { link: [], snap: false },   // no cross-lane link: only the hovered lane follows the cursor
            dataZoom: [
                { type: 'inside', xAxisIndex: allX, filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, moveOnMouseMove: true, minValueSpan: 60000 },
                { type: 'slider', xAxisIndex: allX, filterMode: 'none', height: 20, bottom: 6, left: left, right: right, minValueSpan: 60000, backgroundColor: 'rgba(255,255,255,0.03)', borderColor: 'rgba(255,255,255,0.09)', fillerColor: 'rgba(34,211,238,0.12)', handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, moveHandleStyle: { color: 'rgba(34,211,238,.5)' }, textStyle: { color: T.txt, fontSize: 10 }, labelFormatter: function (v) { return fmtFull(v); }, dataBackground: { lineStyle: { color: T.axis }, areaStyle: { color: T.gridA } } }
            ]
        }, true);
        var readouts = {}, $lanes = {};
        host.find('.tlgv-lane').each(function () { var k = $(this).attr('data-key'); $lanes[k] = $(this); readouts[k] = $(this).find('[data-tlgv-val]'); });
        var $cur = host.find('[data-tlgv-cursor-txt]'), raf = 0, pending = null;
        mgG.cursorKey = null;
        function paint(p) {
            var prev = mgG.cursorKey;
            if (!p) {
                mgG.cursorKey = null;
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                $cur.html('<span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span>');
                refreshLive(true); return;
            }
            var L = layout[p.idx]; if (!L) return;
            var s = L.s;
            mgG.cursorKey = s.key;
            if (prev !== s.key) {
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                if ($lanes[s.key]) $lanes[s.key].addClass('tlgv-lane-active');
                if (prev !== null) refreshLive(true);   // restore the lane we just left
            }
            $cur.html('<b>' + fmtFull(p.t) + '</b> <span style="color:' + esc(s.asset.color) + ';font-weight:700;margin-left:6px;">' + esc(s.asset.name) + '</span> <span style="color:' + esc(s.color) + ';font-weight:700;margin-left:4px;">' + esc(s.short) + '</span>');
            var cw = tlgvChangeWindow(s.pts, p.t);
            if (readouts[s.key]) readouts[s.key].html(valHtml(valueAt(s.pts, p.t), s.kind, s.unit) +
                (cw ? '<div class="tlgv-since">since ' + esc(fmtFull(cw.from)) + (cw.to ? ' &rarr; ' + esc(fmtFull(cw.to)) : ' &rarr; now') + '</div>' : ''));
            if (cw) $cur.append(' <span class="tlgv-since-bar">held since <b>' + esc(fmtFull(cw.from)) + '</b>' + (cw.to ? ' until <b>' + esc(fmtFull(cw.to)) + '</b>' : ' (still)') + '</span>');
        }
        function schedule(p) { pending = p; if (!raf) raf = requestAnimationFrame(function () { raf = 0; paint(pending); }); }
        chart.on('updateAxisPointer', function (e) {
            var infoL = (e && e.axesInfo) || [], hit = null;
            for (var i = 0; i < infoL.length; i++) if (infoL[i].axisDim === 'x') { hit = infoL[i]; break; }
            if (!hit) { schedule(null); return; }
            var t = Number(hit.value); if (isNaN(t)) return;
            schedule({ t: t, idx: hit.axisIndex });
        });
        chart.getZr().on('globalout', function () { schedule(null); });
        observeResize(host[0]);
        syncTlgvCtx();
    }
    function laneSeries(s, xi, yi, end, dens) {
        var isBin = s.kind === 'bin';
        var o = {
            id: s.key, name: s.name, type: 'line', step: 'end', xAxisIndex: xi, yAxisIndex: yi, showSymbol: !isBin && s.actual.length <= 40, symbol: 'circle', symbolSize: 4,
            connectNulls: true, animation: false, sampling: 'lttb',
            lineStyle: { width: isBin ? 1.4 : 1.8, color: s.color, type: s.kind === 'drv' ? 'dashed' : 'solid' }, itemStyle: { color: s.color },
            areaStyle: { color: { type: 'linear', x: 0, y: 0, x2: 0, y2: 1, colorStops: [{ offset: 0, color: hexA(s.color, isBin ? 0.30 : 0.32) }, { offset: 1, color: hexA(s.color, 0.02) }] } },
            emphasis: { disabled: true }, data: s.pts
        };
        if (isBin) o.markArea = { silent: true, itemStyle: { color: 'rgba(52,211,153,0.10)' }, data: pickupBands(s.pts, end).map(function (b) { return [{ xAxis: b[0] }, { xAxis: b[1] }]; }) };
        else if (s.stats && s.stats.avg !== null && dens !== 's') o.markLine = { silent: true, symbol: 'none', animation: false, lineStyle: { color: s.color, type: 'dotted', width: 1, opacity: 0.6 }, label: { show: false }, data: [{ yAxis: Number(zf(s.stats.avg)) }] };
        return o;
    }
    function observeResize(el) {
        if (!('ResizeObserver' in window)) return;
        var t = 0;
        mgG.ro = new ResizeObserver(function () { clearTimeout(t); t = setTimeout(function () { (mgG.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { /* ignore */ } }); }, 60); });
        mgG.ro.observe(el);
    }

    // ------------------------------------------------------------------
    // INDIVIDUAL — cards grouped by asset, tooltip local, zoom synced
    // ------------------------------------------------------------------
    var cardModel = {};
    function renderIndividual(model) {
        disposeAlt();
        var host = $('#mgAlt'), list = visible(model);
        if (!list.length) { host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden charts.</div>'); return; }
        var cols = prefs.cols === 'auto' ? '' : ' c' + prefs.cols, h = '<div class="tlgv-grid' + cols + '">';
        cardModel = {};
        groupsOf(list).forEach(function (g) {
            h += assetHdrHtml(g, ' grid');
            g.list.forEach(function (s) {
                cardModel[s.key] = s;
                var st = s.stats || {};
                var stats = s.kind === 'bin'
                    ? '<span>Pickups <b>' + st.pickups + '</b></span><span>Drops <b>' + st.drops + '</b></span><span>Up <b>' + (st.onPct !== null ? st.onPct.toFixed(0) + '%' : '—') + '</b></span><span>Events <b>' + st.count + '</b></span>'
                    : '<span>Min <b>' + fmtVal(st.min) + '</b></span><span>Max <b>' + fmtVal(st.max) + '</b></span><span>Avg <b>' + fmtVal(st.avg) + '</b></span><span>Samples <b>' + st.count + '</b></span>';
                h += '<div class="tlgv-card" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + '">' +
                    '<div class="tlgv-card-head"><span class="tlgv-card-dot"></span>' +
                    '<div class="tlgv-card-title"><b title="' + esc(s.name) + '">' + esc(s.short) + '</b><small><span style="color:' + esc(g.asset.color) + ';font-weight:700;">' + esc(g.asset.name) + '</span> · ' + kindLabel(s.kind) + (s.unit ? ' · ' + esc(s.unit) : '') + '</small></div>' +
                    '<div class="tlgv-card-live" title="Live value (WebSocket) or last sample"><span class="tlgv-dot-live off" data-tlgv-dot></span><span data-tlgv-val>' + valHtml(st.last, s.kind, s.unit) + '</span></div>' +
                    '<span class="tlgv-lane-act">' +
                    '<button type="button" class="tlgv-ico" data-tlgv-max title="Expand / collapse" aria-label="Expand ' + esc(s.name) + '"><i class="fas fa-expand"></i></button>' +
                    '<button type="button" class="tlgv-ico" data-tlgv-cardpng title="Save PNG" aria-label="Save ' + esc(s.name) + ' as PNG"><i class="fas fa-download"></i></button>' +
                    '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide chart" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                    '</span></div>' +
                    '<div class="tlgv-card-stats">' + stats + '</div>' +
                    '<div class="tlgv-card-chart" role="img" aria-label="' + esc(s.name) + ' history chart"></div></div>';
            });
        });
        host.html(h + '</div>');
        var cards = host.find('.tlgv-card').toArray();
        if ('IntersectionObserver' in window && cards.length > 8) {
            mgG.io = new IntersectionObserver(function (entries) { entries.forEach(function (en) { if (en.isIntersecting) { initCard(en.target); mgG.io && mgG.io.unobserve(en.target); } }); }, { rootMargin: '200px' });
            cards.forEach(function (cd, i) { if (i < 6) initCard(cd); else mgG.io.observe(cd); });
        } else cards.forEach(initCard);
        observeResize(host[0]);
        refreshLive(true);
        syncTlgvCtx();
    }
    function initCard(card) {
        if (card.__tlmgInit) return;
        card.__tlmgInit = true;
        var s = cardModel[card.getAttribute('data-key')]; if (!s || !window.echarts) return;
        var model = mgG.model, T = theme(), span = model.xMax - model.xMin, isBin = s.kind === 'bin';
        var ch = window.echarts.init(card.querySelector('.tlgv-card-chart'), null, { renderer: 'canvas' });
        ch.setOption({
            backgroundColor: 'transparent', animation: false,
            title: { show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: 2, textStyle: { color: s.color, fontSize: 12 } },
            grid: { left: 46, right: 14, top: 12, bottom: 28 },
            tooltip: {
                trigger: 'axis', confine: true, axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.7)', type: 'dashed' } },
                backgroundColor: T.tipBg, borderColor: 'rgba(255,255,255,0.12)', borderWidth: 1, padding: [8, 12], textStyle: { color: T.tipTxt, fontSize: 12 }, extraCssText: 'box-shadow:0 8px 32px rgba(0,0,0,.5);border-radius:10px;',
                formatter: function (params) {
                    if (!params || !params.length) return '';
                    var t = params[0].axisValue != null ? Number(params[0].axisValue) : Number(params[0].value[0]), v = valueAt(s.pts, t);
                    return '<div style="font-family:JetBrains Mono,monospace;color:#22d3ee;font-weight:700;margin-bottom:4px;">' + fmtFull(t) + '</div>' +
                        '<div style="display:flex;align-items:center;gap:8px;"><span style="width:9px;height:9px;border-radius:50%;background:' + s.color + '"></span><span style="opacity:.75">' + esc(s.name) + '</span><b style="margin-left:10px;">' + fmtVal(v, s.kind) + (isBin || v === null ? '' : ' ' + esc(s.unit)) + '</b></div>';
                }
            },
            xAxis: { type: 'time', min: model.xMin, max: model.xMax, splitNumber: 4, axisLabel: { color: T.txt, fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } }, axisLine: { lineStyle: { color: T.axis } }, splitLine: { show: true, lineStyle: { color: T.split } } },
            yAxis: { type: 'value', min: 0, max: isBin ? 1 : niceMax(s.stats.max), splitNumber: isBin ? 1 : 3, axisLabel: { color: T.txt, fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace', formatter: isBin ? function (v) { return v === 1 ? 'Pickup' : v === 0 ? 'Drop' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); } }, splitLine: { lineStyle: { color: T.split, type: 'dashed' } } },
            dataZoom: [{ type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseMove: true, minValueSpan: 60000 }],
            series: [laneSeries(s, 0, 0, model.xMax, 'm')]
        }, true);
        // hover stays inside this card; "Sync zoom" only mirrors zoom / pan
        ch.on('datazoom', function () {
            if (!prefs.sync || mgG.zoomSyncing) return;
            var dz = (ch.getOption().dataZoom || [])[0]; if (!dz) return;
            mgG.zoomSyncing = true;
            try { mgG.charts.forEach(function (o) { if (o !== ch && !o.isDisposed()) o.dispatchAction({ type: 'dataZoom', start: dz.start, end: dz.end }); }); } finally { mgG.zoomSyncing = false; }
        });
        if (prefs.sync && mgG.charts.length) { var dz0 = (mgG.charts[0].getOption().dataZoom || [])[0]; if (dz0 && (dz0.start > 0 || dz0.end < 100)) ch.dispatchAction({ type: 'dataZoom', start: dz0.start, end: dz0.end }); }
        mgG.charts.push(ch); mgG.cardCharts[s.key] = ch;
    }

    // ------------------------------------------------------------------
    // TLGV context ('multi'): same registry so the shared window-resize
    // loop reaches these charts and TLGV.apply('multi') has a valid target.
    // ------------------------------------------------------------------
    function syncTlgvCtx() {
        var T = window.TLGV; if (!T || !T.ctx) return;
        var c = T.ctx.multi || (T.ctx.multi = { id: 'multi', charts: [], hidden: {}, search: '', model: null, cursor: null, group: 'tlgv-multi' });
        c.cfg = {
            host: function () { return $('#mgAlt'); }, overlay: function () { return $('#mgChart'); }, overlayChart: function () { return mgG.chart; },
            showOverlay: function (on) { if (on) { $('#mgChart,#mgLegend').show(); } else { $('#mgChart,#mgLegend').hide(); } },
            getMode: function () { return mgG.mode; }, setMode: function (m) { mgG.mode = m; prefs.mode.multi = m; },
            model: function () { var m = buildModel(); return { xMin: m.xMin, xMax: m.xMax, series: m.series.slice() }; }
        };
        c.charts = mgG.charts; c.hidden = mgG.hidden; c.search = mgG.search; c.model = null; c.cursorKey = mgG.cursorKey;
        c.resetZoom = resetZoom; c.exportPng = exportPng; c.cardCharts = mgG.cardCharts;
    }
    syncTlgvCtx();

    // ------------------------------------------------------------------
    // wiring: Graph view routing + fixes for the stock IPS / PM entry points
    // ------------------------------------------------------------------
    // (2) IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME is IIFE-private in ext.js but read
    //     as a global by graphs.js -> ReferenceError. Provide the default.
    if (typeof window.IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME === 'undefined') window.IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME = false;

    // (1) "Select All" => getSelectedAssetIds() === [] : resolve to the checked assets
    function allCheckedOrSelected() { var ids = selectedIds(); return ids.length ? ids : checkedIds(); }
    if (fn('ipsSelectedIds')) {
        window.ipsSelectedIds = function () { return allCheckedOrSelected(); };
    }
    function withSelection(ids, run) {
        var orig = window.getSelectedAssetIds;
        window.getSelectedAssetIds = function () { return ids.slice(); };
        try { return run(); } finally { window.getSelectedAssetIds = orig; }
    }

    // `routing` is true while the Graph VIEW dispatcher runs. graphs.js calls its
    // tlgBindGraphView through an IIFE-local binding, so the flag is raised at
    // the window-level entry points that lead there: _tlApplyViewMode('Graph')
    // (view buttons), fnSearchView() while the view is Graph (Search button)
    // and tlgBindGraphView() itself for direct callers. fnBindGrph /
    // fnBindIpsGraph called any other way keep their stock behaviour.
    var routing = false;
    function routed(orig, when) {
        return function () {
            var on = when ? when.apply(this, arguments) : true;
            if (!on) return orig.apply(this, arguments);
            routing = true;
            try { return orig.apply(this, arguments); }
            finally { routing = false; }
        };
    }
    if (fn('fnBindGrph')) {
        var _origBindGrph = window.fnBindGrph;
        window.fnBindGrph = function () {
            if (routing) { routing = false; return TLMG.open(); }
            return _origBindGrph.apply(this, arguments);   // single-asset callers (tlOpenAssetView) unchanged
        };
    }
    if (fn('fnBindIpsGraph')) {
        var _origBindIps = window.fnBindIpsGraph;
        window.fnBindIpsGraph = function () {
            if (routing) { routing = false; return TLMG.open(); }
            return _origBindIps.apply(this, arguments);
        };
    }
    if (fn('fnBindPmGraph')) {
        var _origBindPm = window.fnBindPmGraph;
        window.fnBindPmGraph = function () {
            routing = false;
            var ids = allCheckedOrSelected(), self = this, args = arguments;
            if (!ids.length) { var pool = assetPool($('#drpAssetType').val()); ids = pool.map(function (a) { return a.id; }); }
            if (!ids.length) return _origBindPm.apply(self, args);
            var cur = String(window._tlmgPmAsset || '');
            if (ids.indexOf(cur) < 0) cur = ids[0];
            var r = withSelection([cur], function () { return _origBindPm.apply(self, args); });
            // a small chooser so a multi-selection can switch machine without leaving the Graph view
            try {
                if (ids.length > 1 && $('.ipsg-head').length && !$('#mgPmAsset').length) {
                    var sel = '<label style="display:inline-flex;align-items:center;gap:6px;font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));margin:0 8px 0 0;">Machine ' +
                        '<select id="mgPmAsset" style="background:var(--at-g1,rgba(255,255,255,.06));border:1px solid var(--at-edge,rgba(255,255,255,.14));border-radius:6px;color:var(--at-t1,#fff);font-size:11px;padding:3px 6px;">' +
                        ids.map(function (id) { return '<option value="' + esc(id) + '"' + (id === cur ? ' selected' : '') + '>' + esc(assetName(id)) + '</option>'; }).join('') + '</select></label>';
                    $('.ipsg-head #pmgRange').prepend(sel);
                    $('#mgPmAsset').on('change', function () { window._tlmgPmAsset = String(this.value); window.fnBindPmGraph(); });
                }
            } catch (e) { /* chooser is optional */ }
            return r;
        };
    }
    if (fn('tlgBindGraphView')) window.tlgBindGraphView = routed(window.tlgBindGraphView);
    if (fn('fnSearchView')) window.fnSearchView = routed(window.fnSearchView, function () { return ($('#drpView').val() || '') === 'Graph'; });
    function hasCharts() { return !!(mgG.chart || (mgG.charts && mgG.charts.length)); }
    if (fn('_tlApplyViewMode')) {
        var _origApply = window._tlApplyViewMode;
        window._tlApplyViewMode = routed(function (mode) {
            // leaving the Graph view: free the charts (the container is emptied by graphs.js)
            if (mode !== 'Graph' && hasCharts()) disposeAll();
            return _origApply.apply(this, arguments);
        }, function (mode) { return mode === 'Graph'; });
    }
    // Cards / Table buttons: graphs.js empties #divTelemetryLive from a handler bound on
    // the button itself (and core off()s document-level .tl-vmode-btn handlers), so bind
    // on the buttons too and key the clean-up on live chart instances, not on the DOM.
    $(function () {
        $('.tl-vmode-btn').not('[data-vmode="Graph"]').off('click.tlmgleave').on('click.tlmgleave', function () { if (hasCharts()) disposeAll(); });
    });

    // theme flip (body[data-aurora]) -> recolour + redraw the open graph
    try {
        if ('MutationObserver' in window && document.body) {
            new MutationObserver(function () {
                if (!$('#mgWrap').length || mgG.loading) return;
                mgG.assetIds.forEach(function (aid) { var A = mgG.assets[aid]; if (A) A.color = assetColor(A.idx); });
                mgG.order.forEach(function (k) { var s = mgG.series[k], A = mgG.assets[s.assetId]; if (A) s.color = seriesColor(A.idx, seriesOfAsset(s.assetId).indexOf(s)); });
                renderPanel(); render();
            }).observe(document.body, { attributes: true, attributeFilter: ['data-aurora'] });
        }
    } catch (e) { /* optional */ }

    if (window.console) console.log('[TL MultiGraph] multi-asset graph engine registered (Track / Signal / ELD / IPS / other; PM keeps its own panel).');
})(window, window.jQuery);
;
} catch (e) {
    if (window.console) console.error('[telemetrylive-ext] section "telemetrylive-multigraph" failed to load', e);
}


/* #############################################################################
 * SECTION: SWITCHING — asset type / view mode / asset number (v617.6)
 * (telemetrylive-switch.js — loads after every other section; wraps globals)
 * ########################################################################## */
try {
/* =============================================================================
 * telemetrylive-switch.js — cross-type leakage + switching fixes
 *
 * Load order: a classic <script> AFTER telemetrylive.js, telemetrylive-graphs.js,
 * telemetrylive-pmvibration.js and telemetrylive-ext.js. It does not edit those
 * files; it wraps or replaces window.* functions that the page looks up by name
 * at call time.
 *
 * Root causes fixed (file:line refer to v617 TelemetryLive/telemetrylive.js
 * unless noted):
 *
 *  S1  wsLiveData is shared with sip-telemetry.js, which mirrors EVERY station
 *      asset (all types) into it (sip-telemetry.js 1580-1609, __sipMirror), and
 *      several renderers walk wsLiveData with no type / whitelist / asset-number
 *      filter (ext.js PM table tableAssetIds 665, core buildPmTableViewHtml
 *      16457, syncAttributeNamesFromLiveData 99, hasLive checks 20123/20314,
 *      export 11851/23838, renderRDPMSView 5491, renderPointMachineView 14046,
 *      renderIpsGridView 6879 ignore the Asset-No selection).
 *      FIX: every renderer / updater / export / stats entry point now runs
 *      against a VISIBLE VIEW of wsLiveData (selected type + bulk whitelist +
 *      Asset-No selection, never a SIP mirror entry). Additions/deletions the
 *      renderer makes are synced back to the full store.
 *
 *  S2  Ingest gate. processItemsInternal (4640) only had the bulk whitelist,
 *      which is stale for the new type until GetBulkAssetMetadata / GetAssestBy
 *      returns (isAssetInBulkWhitelist 460 rebuilds it from the OLD
 *      bulkAssetsList; PM returns true when the list is empty; the
 *      userAssetSimpleMap fallback still holds the old type). Frames of the old
 *      type (and the whole station, on a site-wide socket) therefore created
 *      rows/cards/columns of another type. FIX: frames are checked against the
 *      frame's AssetTypeId and a whitelist that is only trusted once it is
 *      known FOR THE CURRENT site+type; until then frames are buffered and
 *      replayed (no data loss), never rendered. DataLogger frames get the same
 *      gate (window.parseBatchMessages 19215 path). wsAttributeNames /
 *      wsCurrentColumns can therefore only carry the selected type's columns.
 *
 *  S3  Stale metadata responses. Rapid pill clicks put 3-4 GetBulkAssetMetadata
 *      requests in flight per click (Index.cshtml bindFromBulkList 11195, core
 *      GetByAssest 13069, loadAssetNumbersMulti 13070, fnSearchView 6654); a
 *      late response for the PREVIOUS type overwrote bulkAssetsList /
 *      wsValidAssetIds / userAssetSimpleMap and re-filled #listAssetNumber with
 *      the other type's assets, then auto-loaded them. FIX: requests for one
 *      site+type are coalesced into one AJAX; a response whose site+type no
 *      longer matches the selection is dropped (callbacks not run, cache
 *      invalidated, current selection re-loaded). A load for a foreign type
 *      (sip-asset-popup.js 3439) keeps the page's own metadata (snapshot /
 *      restore) and the wsLiveData entries that loader prunes (2357).
 *
 *  S4  Sockets for the wrong selection. connectWebSocket now refuses a socket
 *      for a site+type other than the current selection and redirects it to
 *      the selection (callers that replay captured parameters: Circuit-close
 *      restoreSession ext.js 3913, heartbeat / onclose reconnect 5292/5396,
 *      tlReturnFromAssetView 19916); frames of a superseded socket are
 *      dropped. The deferred SIP-only (typeless) connect (2702, 400 ms after
 *      a site change) is skipped once a type is selected — it replaced the
 *      typed stream with a _wsSipOnlyMode socket that renders nothing. The
 *      delegated handler at 13330 (atSwitchView('Table') → fnSearchView →
 *      fnBindPointMachineTable 6819, which connects BEFORE GetAssestBy
 *      answers) is kept from running by force-hiding #atCardView on type
 *      change, so _tlAutoLoad → fnAdvSearch is the only connect path.
 *
 *  S5  Asset-No changes reconnected the socket (fnAdvSearch 20362 →
 *      disconnectWebSocket + connectWebSocket) which wipes wsLiveData and
 *      blanks the page; and getSelectedAssetIds() returns [] when ALL are
 *      ticked (6502), so Index.cshtml applyAssetNumberFilter (10792) showed
 *      "Select one or more Asset Numbers" after re-ticking the last asset.
 *      FIX: getSelectedAssetIds returns the explicit id list (empty only when
 *      nothing is ticked); fnAdvSearch repaints in place when site+type and the
 *      live stream are unchanged (containers are cleared so unticked rows /
 *      cards disappear from Table, Cards, RDPMS, PM, IPS); every asset of the
 *      type is ingested so a re-ticked asset shows its data at once.
 *      wsCurrentFilterAssetIds is always the explicit visible list, which also
 *      makes the PM616 table filter (ext.js 657) and _sipFilteredAssetIds
 *      (19957) exclude foreign / mirror entries.
 *
 *  S6  View round trips (Cards ↔ Table ↔ Graph ↔ Cards, Back from the per-asset
 *      overlay, Circuit popup close): the repaint is done under the visible
 *      view (S1) and, if the live stream for the current selection is gone,
 *      it is re-established (ensureStream in the _tlApplyViewMode wrapper).
 *      The "Assets" counters (#wsSA, #atKpiAssets) show the visible count.
 *
 *  S7  Shape conflict: a SIP mirror entry created BEFORE our own frame for the
 *      same asset kept __sipMirror, so sip-telemetry.js kept overwriting attrs
 *      with its own shape (no TimestampDevice). FIX: the entry is claimed
 *      (flag removed, attrs reset) the first time our stream touches it.
 *
 *  S8  updateRowCells (3848) references an undeclared `assetId`
 *      (wsStaleAttrs[assetId]) → ReferenceError on every incremental Track
 *      table update; the live table froze after the first batch. Supplied for
 *      the duration of the call (guarded, no-op once the core is fixed).
 *
 *  S9  Per-asset Graph overlay (tlOpenAssetView 19885) was painted over by
 *      the next live batch: executeUIUpdate (4283) fell back to the full
 *      card renderer because the overlay had replaced its container. The
 *      list repaint is skipped while the overlay is open.
 *
 * Debug: window.TLSWITCH (state, visibleIds(), whitelist()).
 * ========================================================================== */
(function () {
    'use strict';

    if (window.__tlSwitchApplied) return;
    window.__tlSwitchApplied = true;

    var W = window;
    var $ = W.jQuery;
    if (typeof $ !== 'function') {
        if (W.console) console.warn('[TLSWITCH] jQuery missing; nothing applied');
        return;
    }

    var SW = W.TLSWITCH = W.TLSWITCH || {};
    SW.seq = 0;                 // bumped on every site / type change
    SW.listKey = null;          // site|type whose asset list is known to be loaded
    SW.buffer = [];             // frames waiting for the whitelist of the current type
    SW.bufferKey = '';
    SW.replayTimer = null;
    SW.replayStart = 0;
    SW.lastSelKey = null;       // last applied Asset-No selection (fnAdvSearch)
    SW.lastConnectAt = 0;
    SW.stats = { dropped: 0, buffered: 0, replayed: 0, staleMeta: 0, redirected: 0 };

    var BUF_MAX = 6000;
    var BUF_TIMEOUT_MS = 30000;

    function fn(name) { return typeof W[name] === 'function' ? W[name] : null; }
    function str(v) { return (v === undefined || v === null) ? '' : String(v); }
    function trim(v) { return $.trim(str(v)); }
    function validId(v) { v = trim(v); return v !== '' && v !== '0'; }
    function log() {
        if (!W.console || !console.log) return;
        var a = Array.prototype.slice.call(arguments);
        a.unshift('[TLSWITCH]');
        try { console.log.apply(console, a); } catch (e) { }
    }

    function curSite() { return trim($('#drpSite').val()); }
    function curType() { return trim($('#drpAssetType').val()); }
    function curKey() {
        var s = curSite(), t = curType();
        return (validId(s) && validId(t)) ? (s + '|' + t) : '';
    }
    function keyOf(site, type) { return trim(site) + '|' + trim(type); }
    function isPmType(t) {
        var f = fn('isPointMachineAssetTypeForBulkSkip');
        return f ? !!f(t) : (trim(t) === '3');
    }
    function activeMode() {
        var m = $('.tl-vmode-btn.active').attr('data-vmode');
        if (!m) m = ($('#drpView').val() === 'Table') ? 'Table' : 'Cards';
        return m;
    }

    /* ======================================================================
     * Whitelist knowledge for the CURRENT selection (S2 / S3)
     * ==================================================================== */
    function whitelist() {
        var site = curSite(), type = curType();
        var key = curKey();
        var unknown = { known: false, ids: null, n: 0, key: key };
        if (!key) return unknown;

        var markers = !!W.bulkMetadataLoaded &&
            str(W.bulkMetadataSiteId) === site && str(W.bulkMetadataAssetTypeId) === type;
        var pm = isPmType(type);
        var pmKnown = pm && SW.listKey === key;
        if (!markers && !pmKnown) return unknown;

        var ids = {}, n = 0, i;
        var list = W.bulkAssetsList;
        if (pm && Array.isArray(W.wsValidAssetIds) && W.wsValidAssetIds.length) {
            for (i = 0; i < W.wsValidAssetIds.length; i++) { ids[str(W.wsValidAssetIds[i])] = true; n++; }
        } else if (Array.isArray(list)) {
            for (i = 0; i < list.length; i++) {
                var a = list[i];
                if (!a || a.Id === undefined || a.Id === null) continue;
                // a stale list (other type / site) must never count as this type's whitelist
                if (a.AssetTypeId !== undefined && a.AssetTypeId !== null && str(a.AssetTypeId) !== '' &&
                    str(a.AssetTypeId) !== type) continue;
                if (a.SiteId !== undefined && a.SiteId !== null && str(a.SiteId) !== '' && str(a.SiteId) !== site) continue;
                ids[str(a.Id)] = true; n++;
            }
        }
        return { known: true, ids: ids, n: n, key: key };
    }
    SW.whitelist = whitelist;

    /* ======================================================================
     * Asset-No selection → explicit id list (S5)
     * ==================================================================== */
    function selectedMap() {
        var $all = $('#listAssetNumber input[type="checkbox"]');
        if (!$all.length) return null;                 // list not bound yet → no selection filter
        var $on = $all.filter(':checked');
        if ($on.length === $all.length) return null;    // everything ticked → no filter
        var m = {};
        $on.each(function () { m[str(this.value)] = true; });
        return m;
    }
    function selectionKey() {
        var out = [];
        $('#listAssetNumber input[type="checkbox"]:checked').each(function () { out.push(str(this.value)); });
        out.sort();
        return out.join(',');
    }

    // Explicit list of checked ids ([] only when nothing is ticked). The stock
    // version returned [] for "all ticked" too, which Index.cshtml's
    // applyAssetNumberFilter reads as "nothing selected".
    W.getSelectedAssetIds = function () {
        var ids = [];
        $('#listAssetNumber input[type="checkbox"]:checked').each(function () { ids.push(str($(this).val())); });
        return ids;
    };

    function setFilterIds(ids) {
        var list = [], i;
        if (Array.isArray(ids)) {
            for (i = 0; i < ids.length; i++) { var v = trim(ids[i]); if (validId(v)) list.push(v); }
        }
        if (!list.length) {
            // "all" → the whitelist itself, so downstream filters
            // (_sipFilteredAssetIds, PM616 assetInFilter) exclude foreign entries.
            var wl = whitelist();
            if (wl.known && wl.n) list = Object.keys(wl.ids);
        }
        W.wsCurrentFilterAssetIds = list;
        return list;
    }
    SW.setFilterIds = setFilterIds;

    /* ======================================================================
     * S1 — visible view of wsLiveData
     * ==================================================================== */
    function entryType(id, e) {
        if (e && e.AssetTypeId !== undefined && e.AssetTypeId !== null && str(e.AssetTypeId) !== '') return str(e.AssetTypeId);
        var g = fn('getBulkAssetTypeId');
        if (g) { var t = g(id, 0); if (t) return str(t); }
        return '';
    }
    function isVisibleEntry(id, e, wl, sel, type) {
        if (!e || typeof e !== 'object') return false;
        if (e.__sipMirror) return false;
        if (wl.known) {
            if (!wl.ids[id]) return false;
        } else {
            var t = entryType(id, e);
            if (t && t !== type) return false;
        }
        if (sel && !sel[id]) return false;
        return true;
    }
    function visibleMap() {
        var full = W.wsLiveData || {};
        var out = {};
        var wl = whitelist(), sel = selectedMap(), type = curType();
        for (var id in full) {
            if (!Object.prototype.hasOwnProperty.call(full, id)) continue;
            if (isVisibleEntry(id, full[id], wl, sel, type)) out[id] = full[id];
        }
        return out;
    }
    function visibleIds() { return Object.keys(visibleMap()); }
    SW.visibleIds = visibleIds;
    SW.visibleMap = visibleMap;

    var viewDepth = 0;
    function withView(f, thisArg, args) {
        if (viewDepth > 0 || !curKey()) return f.apply(thisArg, args);
        var full = W.wsLiveData;
        if (!full || typeof full !== 'object') return f.apply(thisArg, args);
        var view = visibleMap();
        var before = Object.keys(view);
        viewDepth++;
        W.wsLiveData = view;
        try {
            return f.apply(thisArg, args);
        } finally {
            viewDepth--;
            if (W.wsLiveData === view) {
                W.wsLiveData = full;
                var i, k;
                for (i = 0; i < before.length; i++) {
                    if (!Object.prototype.hasOwnProperty.call(view, before[i])) delete full[before[i]];
                }
                for (k in view) {
                    if (Object.prototype.hasOwnProperty.call(view, k) && !Object.prototype.hasOwnProperty.call(full, k)) full[k] = view[k];
                }
            }
            // else: something inside replaced the store (reconnect) — keep it
        }
    }
    SW.withView = withView;

    function wrapWithView(name) {
        var orig = W[name];
        if (typeof orig !== 'function' || orig.__tlswView) return;
        var wrapped = function () { return withView(orig, this, arguments); };
        for (var p in orig) { if (Object.prototype.hasOwnProperty.call(orig, p)) { try { wrapped[p] = orig[p]; } catch (e) { } } }
        wrapped.__tlswView = true;
        W[name] = wrapped;
    }

    /* ======================================================================
     * Render cache reset (used when the visible set changes)
     * ==================================================================== */
    function resetRenderCaches() {
        var objs = ['rdpmsCardsBuilt', 'pmCardsBuilt', 'pmCardsBuilding', '_sigDomCache', '_rdpmsDomCache'];
        var i;
        for (i = 0; i < objs.length; i++) if (typeof W[objs[i]] !== 'undefined') W[objs[i]] = {};
        var falses = ['wsTableInitialized', 'wsTableStructureBuilt', 'wsAssetOrderInitialized'];
        for (i = 0; i < falses.length; i++) if (typeof W[falses[i]] !== 'undefined') W[falses[i]] = false;
        var arrs = ['wsCurrentColumns', 'wsCurrentTableColumns'];
        for (i = 0; i < arrs.length; i++) if (typeof W[arrs[i]] !== 'undefined') W[arrs[i]] = [];
        if (typeof W.pmTableMode !== 'undefined') W.pmTableMode = false;
    }
    SW.resetRenderCaches = resetRenderCaches;

    function clearContainers() {
        var $tl = $('#divTelemetryLive');
        if ($tl.length) {
            $tl.find('#pmVibrationTableSection').remove();
            $tl.empty().append('<div id="trackCardContainer" class="sites-cards" style="display:none;"></div>');
        }
        $('#atCardView').empty();
        $('#rdpmsShuntContainer, .rdpms-container, #ipsGridContainer, .ips-grid-container').remove();
    }

    /* ======================================================================
     * S2 — ingest gate + buffer
     * ==================================================================== */
    function clearBuffer() {
        SW.buffer = [];
        SW.bufferKey = '';
        if (SW.replayTimer) { clearInterval(SW.replayTimer); SW.replayTimer = null; }
    }
    function bufferItems(items, key) {
        if (SW.bufferKey !== key) { SW.buffer = []; SW.bufferKey = key; }
        for (var i = 0; i < items.length; i++) SW.buffer.push(items[i]);
        if (SW.buffer.length > BUF_MAX) SW.buffer.splice(0, SW.buffer.length - BUF_MAX);
        SW.stats.buffered += items.length;
        if (!SW.replayTimer) {
            SW.replayStart = Date.now();
            SW.replayTimer = setInterval(tryReplay, 150);
        }
    }
    function tryReplay() {
        if (!SW.buffer.length || SW.bufferKey !== curKey()) { clearBuffer(); return; }
        var wl = whitelist();
        if (!wl.known) {
            if (Date.now() - SW.replayStart > BUF_TIMEOUT_MS) { log('whitelist never arrived; dropping', SW.buffer.length, 'buffered frames'); clearBuffer(); }
            return;
        }
        var items = SW.buffer;
        clearBuffer();
        SW.stats.replayed += items.length;
        log('replaying', items.length, 'buffered frames for', wl.key);
        var dl = [], rest = [];
        for (var i = 0; i < items.length; i++) {
            if (items[i] && items[i].DataType === 'DataLogger') dl.push(items[i]); else rest.push(items[i]);
        }
        try {
            if (dl.length && fn('parseBatchMessages')) W.parseBatchMessages(dl);
            if (rest.length) ingest(rest, true);
        } catch (e) { if (W.console) console.error('[TLSWITCH] replay failed', e); }
        if (fn('triggerUIUpdate')) { try { W.triggerUIUpdate(); } catch (e2) { } }
    }
    SW.tryReplay = tryReplay;

    function parseMsg(m) {
        if (typeof m === 'string') {
            try { m = JSON.parse(m); } catch (e) { return null; }
            if (typeof m === 'string') { try { m = JSON.parse(m); } catch (e2) { return null; } }
        }
        return (m && typeof m === 'object') ? m : null;
    }
    function flatten(messages) {
        var out = [];
        for (var i = 0; i < messages.length; i++) {
            var m = parseMsg(messages[i]);
            if (!m) continue;
            if (m.AssetId !== undefined && m.AssetAttributeName) { out.push(m); continue; }
            var nested = Array.isArray(m.Messages) ? m.Messages : (Array.isArray(m.Data) ? m.Data : null);
            if (nested) {
                for (var j = 0; j < nested.length; j++) { var s = parseMsg(nested[j]); if (s) out.push(s); }
            } else {
                out.push(m);
            }
        }
        return out;
    }

    // Splits frames into {pass, hold}. `hold` = whitelist not yet known.
    function gate(items) {
        var key = curKey();
        var type = curType();
        var wl = whitelist();
        var pass = [], hold = [], dropped = 0;
        for (var i = 0; i < items.length; i++) {
            var d = items[i];
            if (!d || typeof d !== 'object') continue;
            if (d.AssetId === undefined || d.AssetId === null) { pass.push(d); continue; }
            var aid = str(d.AssetId);
            var ft = (d.AssetTypeId !== undefined && d.AssetTypeId !== null) ? str(d.AssetTypeId) : '';
            if (ft && ft !== type && !(wl.known && wl.ids[aid])) { dropped++; continue; }
            if (wl.known) {
                if (!wl.ids[aid]) { dropped++; continue; }
                pass.push(d);
            } else {
                hold.push(d);
            }
        }
        if (dropped) SW.stats.dropped += dropped;
        return { pass: pass, hold: hold, key: key, dropped: dropped };
    }

    // S7 — take ownership of a SIP mirror entry the first time our stream reports it
    function claimMirror(aid) {
        var LD = W.wsLiveData;
        if (!LD) return;
        var e = LD[aid];
        if (!e || !e.__sipMirror) return;
        delete e.__sipMirror;
        e.AssetId = e.AssetId || aid;
        if (fn('getBulkAssetName')) e.AssetName = W.getBulkAssetName(aid, e.AssetName);
        if (fn('getBulkAssetTypeId')) e.AssetTypeId = W.getBulkAssetTypeId(aid, e.AssetTypeId);
        if (fn('getBulkSiteId')) e.SiteId = W.getBulkSiteId(aid, e.SiteId);
        e.attrs = {};
        e.dlRelays = {};
        e.lastUpdated = new Date();
        if (fn('getZeroOffsetForAsset')) { try { e.ZeroOffsetValue = W.getZeroOffsetForAsset(aid); } catch (err) { } }
    }

    var origPII = null;   // the chain below us (core + bulk-placeholder wrapper)
    function ingest(items, replaying) {
        if (!origPII) return;
        if (!Array.isArray(items) || !items.length) return;
        var key = curKey();
        if (!key) return origPII.call(W, items);           // SIP-only / nothing selected: untouched
        var g = gate(items);
        if (g.hold.length && !replaying) bufferItems(g.hold, key);
        if (!g.pass.length) return;
        for (var i = 0; i < g.pass.length; i++) if (g.pass[i].AssetId !== undefined) claimMirror(str(g.pass[i].AssetId));
        // every asset of the type is stored; the Asset-No selection is applied when rendering
        var saved = W.wsCurrentFilterAssetIds;
        W.wsCurrentFilterAssetIds = [];
        try { return origPII.call(W, g.pass); }
        finally { W.wsCurrentFilterAssetIds = saved; }
    }

    function installIngest() {
        var cur = W.processItemsInternal;
        if (typeof cur !== 'function' || cur.__tlswIngest) return;
        origPII = cur;
        var wrapped = function (items) { return ingest(items, false); };
        wrapped.__tlswIngest = true;
        W.processItemsInternal = wrapped;

        var origPBM = W.parseBatchMessages;
        if (typeof origPBM === 'function' && !origPBM.__tlswIngest) {
            var wrappedPBM = function (messages) {
                if (!Array.isArray(messages) || !messages.length || !curKey()) return origPBM.apply(this, arguments);
                var flat = flatten(messages);
                var g = gate(flat);
                if (g.hold.length) bufferItems(g.hold, g.key);
                if (!g.pass.length) return;
                var saved = W.wsCurrentFilterAssetIds;
                W.wsCurrentFilterAssetIds = [];
                try { return origPBM.call(this, g.pass); }
                finally { W.wsCurrentFilterAssetIds = saved; }
            };
            wrappedPBM.__tlswIngest = true;
            W.parseBatchMessages = wrappedPBM;
        }
        var origPSLU = W.processSingleLiveUpdate;
        if (typeof origPSLU === 'function' && !origPSLU.__tlswIngest) {
            var wrappedPSLU = function (d) {
                if (!d || !curKey()) return origPSLU.apply(this, arguments);
                var g = gate([d]);
                if (g.hold.length) { bufferItems(g.hold, g.key); return; }
                if (!g.pass.length) return;
                var saved = W.wsCurrentFilterAssetIds;
                W.wsCurrentFilterAssetIds = [];
                try { return origPSLU.call(this, g.pass[0]); }
                finally { W.wsCurrentFilterAssetIds = saved; }
            };
            wrappedPSLU.__tlswIngest = true;
            W.processSingleLiveUpdate = wrappedPSLU;
        }
    }

    // strict whitelist for everyone else (renderers, DataLogger creation)
    var origInWhitelist = fn('isAssetInBulkWhitelist');
    if (origInWhitelist) {
        W.isAssetInBulkWhitelist = function (assetId) {
            var wl = whitelist();
            if (wl.known) return !!wl.ids[str(assetId).replace(/^\s+|\s+$/g, '')];
            return origInWhitelist.apply(this, arguments);
        };
    }

    /* ======================================================================
     * S3 — metadata loads: coalesce, drop stale, protect against foreign loads
     * ==================================================================== */
    var META_GLOBALS = ['bulkAssetsList', 'bulkAssetMap', 'bulkAssetAttrMap', 'bulkDataloggerMap',
        'userAssetSimpleMap', 'userAssetDataloggerMap', 'bulkAliasByAssetAttrName', 'bulkAliasByAssetAttrId',
        'bulkAliasByAttrName', 'bulkAliasByAttrId', 'assetAttributeMap', 'assetAttributeByName', 'wsAttributeIds',
        'dlRoleNameMap', 'dlAssetRoleMap', '_sigAssetInfoCache', 'wsValidAssetIds', 'bulkMetadataLoaded',
        'bulkMetadataSiteId', 'bulkMetadataAssetTypeId', 'userAssetInfoLoaded', 'userAssetInfoSiteId',
        'userAssetInfoAssetTypeId', 'assetAttributeLoaded', '_assetInfoListCache',
        'bulkExpectedAttrMap', 'bulkAttrKeyByAssetAttrId', 'bulkAttributeUnion', 'bulkAssetNameById', 'bulkExpectedSchemaReady'];
    function snapshotMeta() {
        var s = { g: {}, live: {} };
        for (var i = 0; i < META_GLOBALS.length; i++) {
            if (typeof W[META_GLOBALS[i]] !== 'undefined') s.g[META_GLOBALS[i]] = W[META_GLOBALS[i]];
        }
        var LD = W.wsLiveData || {};
        for (var k in LD) if (Object.prototype.hasOwnProperty.call(LD, k)) s.live[k] = LD[k];
        return s;
    }
    function restoreMeta(s) {
        for (var n in s.g) if (Object.prototype.hasOwnProperty.call(s.g, n)) W[n] = s.g[n];
        var LD = W.wsLiveData;
        if (!LD || typeof LD !== 'object') return;
        var lost = [];
        for (var k in s.live) {
            if (Object.prototype.hasOwnProperty.call(s.live, k) && !Object.prototype.hasOwnProperty.call(LD, k)) {
                LD[k] = s.live[k];
                lost.push(k);
            }
        }
        if (lost.length) {
            W.wsUpdatedAssets = W.wsUpdatedAssets || {};
            for (var i = 0; i < lost.length; i++) W.wsUpdatedAssets[lost[i]] = true;
            if (fn('triggerUIUpdate')) { try { W.triggerUIUpdate(); } catch (e) { } }
        }
    }

    var pendingLoads = {};   // key → [callbacks]
    var origLoadBulk = fn('loadBulkAssetMetadata');
    if (origLoadBulk) {
        W.loadBulkAssetMetadata = function (siteId, assetTypeId, callback) {
            var site = trim(siteId), type = trim(assetTypeId);
            if (!validId(site) || !validId(type)) return origLoadBulk.apply(this, arguments);
            var key = keyOf(site, type);
            var cur = curKey();

            // A load for a site+type other than the selection (SIP asset popup):
            // let it run but give the page its own metadata back afterwards.
            if (cur && key !== cur) {
                var snap = snapshotMeta();
                var selAtReq = cur;
                return origLoadBulk.call(this, siteId, assetTypeId, function () {
                    try { if (typeof callback === 'function') callback.apply(this, arguments); }
                    finally { if (curKey() === selAtReq) restoreMeta(snap); }
                });
            }

            if (pendingLoads[key]) { pendingLoads[key].push(callback); return; }
            pendingLoads[key] = [callback];

            return origLoadBulk.call(this, siteId, assetTypeId, function () {
                var cbs = pendingLoads[key] || [];
                delete pendingLoads[key];
                var self = this, args = arguments, i;

                if (curKey() !== key) {
                    // Stale: the selection moved on while this request was in flight.
                    SW.stats.staleMeta++;
                    log('stale metadata response for', key, 'ignored (selection is', curKey() || 'none', ')');
                    if (fn('invalidateBulkMetadataCache')) { try { W.invalidateBulkMetadataCache(); } catch (e) { } }
                    var ck = curKey();
                    if (ck && !pendingLoads[ck] && !isPmType(curType())) {
                        // the maps now describe the wrong type — put the selection's back
                        W.loadBulkAssetMetadata(curSite(), curType(), rebindAfterStale);
                    }
                    return;
                }
                if (W.bulkMetadataLoaded && str(W.bulkMetadataSiteId) === site && str(W.bulkMetadataAssetTypeId) === type) {
                    SW.listKey = key;
                }
                for (i = 0; i < cbs.length; i++) {
                    if (typeof cbs[i] !== 'function') continue;
                    try { cbs[i].apply(self, args); }
                    catch (e2) { if (W.console) console.error('[TLSWITCH] metadata callback failed', e2); }
                }
                if (SW.buffer.length) tryReplay();
            });
        };
    }

    // After a stale response overwrote the maps and the selection's metadata was
    // re-loaded: make sure the Asset-No list belongs to the selection, then repaint.
    function rebindAfterStale() {
        var list = Array.isArray(W.bulkAssetsList) ? W.bulkAssetsList : [];
        var known = {};
        for (var i = 0; i < list.length; i++) if (list[i]) known[str(list[i].Id)] = true;
        var $items = $('#listAssetNumber .dropdown-item');
        var mismatch = !$items.length;
        $items.each(function () { if (!known[str($(this).attr('data-value'))]) mismatch = true; });
        if (fn('rebuildWsValidAssetIdsFromBulk')) { try { W.rebuildWsValidAssetIdsFromBulk(); } catch (e) { } }
        if (mismatch && fn('populateAssetDropdownsFromBulk')) {
            W.populateAssetDropdownsFromBulk({ autoLoad: true });
            return;
        }
        if (fn('_tlAutoLoad')) W._tlAutoLoad({});
    }

    // Point Machine lists come from GetAssestBy inside loadAssetNumbersMulti
    var pmWatch = null;
    function watchPmWhitelist(key) {
        if (pmWatch) { clearInterval(pmWatch.t); pmWatch = null; }
        var start = Date.now();
        var t = setInterval(function () {
            if (curKey() !== key) { clearInterval(t); pmWatch = null; return; }
            var list = W.bulkAssetsList;
            if (Array.isArray(list) && list.length && str(list[0].AssetTypeId) === curType() && str(list[0].SiteId) === curSite()) {
                clearInterval(t); pmWatch = null;
                SW.listKey = key;
                if (SW.buffer.length) tryReplay();
                return;
            }
            var $e = $('#listAssetNumber .dropdown-empty');
            if ($e.length && /no assets|error/i.test($e.text())) { clearInterval(t); pmWatch = null; return; }
            if (Date.now() - start > 20000) { clearInterval(t); pmWatch = null; }
        }, 100);
        pmWatch = { t: t, key: key };
    }
    var origLANM = fn('loadAssetNumbersMulti');
    if (origLANM) {
        W.loadAssetNumbersMulti = function (siteId, assetTypeId) {
            var key = keyOf(siteId, assetTypeId);
            var pm = validId(siteId) && validId(assetTypeId) && isPmType(assetTypeId);
            // Point Machine: Index.cshtml and the core type-change handler both call
            // this for one pill click; one GetAssestBy per site+type is enough while
            // the first is still pending. (Other types are coalesced in
            // loadBulkAssetMetadata below.)
            if (pm && pmWatch && pmWatch.key === key) {
                log('GetAssestBy already pending for', key, '— coalesced');
                return;
            }
            var r = origLANM.apply(this, arguments);
            if (pm) watchPmWhitelist(key);
            return r;
        };
    }

    /* ======================================================================
     * S4 — connectWebSocket guard
     * ==================================================================== */
    function hasLiveStream(key) {
        var ws = W.wsConnection;
        if (!ws) return false;
        if (ws.readyState !== 0 && ws.readyState !== 1) return false;
        if (W._wsSipOnlyMode) return false;
        var p = W.wsConnParams || {};
        return keyOf(p.siteId, p.assetTypeId) === key;
    }
    SW.hasLiveStream = hasLiveStream;

    function pruneForeign() {
        var LD = W.wsLiveData;
        if (!LD || typeof LD !== 'object') return;
        var wl = whitelist(), type = curType();
        for (var id in LD) {
            if (!Object.prototype.hasOwnProperty.call(LD, id)) continue;
            var e = LD[id];
            if (e && e.__sipMirror) continue;
            if (!isVisibleEntry(id, e, wl, null, type)) delete LD[id];
        }
    }

    var origConnect = fn('connectWebSocket');
    if (origConnect) {
        W.connectWebSocket = function (siteId, assetTypeId, assetIds) {
            var reqSite = trim(siteId), reqType = trim(assetTypeId);
            var cSite = curSite(), cType = curType();
            var typed = validId(reqType);
            var redirected = false;

            if (!typed && validId(cSite) && validId(cType) && trim(reqSite) === cSite) {
                // SIP-only background socket requested while a type is selected: the
                // typed stream (present or about to open) must not be replaced by it.
                log('SIP-only connect skipped — asset type', cType, 'is selected');
                return;
            }
            if (typed && validId(cSite) && validId(cType) && (reqType !== cType || reqSite !== cSite)) {
                SW.stats.redirected++;
                log('connect for', keyOf(reqSite, reqType), 'redirected to selection', keyOf(cSite, cType));
                siteId = cSite; assetTypeId = cType;
                assetIds = W.getSelectedAssetIds();
                redirected = true;
            }

            var r = origConnect.call(this, siteId, assetTypeId, assetIds);

            var ws = W.wsConnection;
            if (ws && !ws.__tlswTagged) {
                ws.__tlswTagged = true;
                ws.__tlswKey = validId(assetTypeId) ? keyOf(siteId, assetTypeId) : '';
                var om = ws.onmessage;
                if (typeof om === 'function') {
                    ws.onmessage = function (ev) {
                        if (W.wsConnection !== ws) return;                       // superseded socket
                        if (ws.__tlswKey && ws.__tlswKey !== curKey()) return;   // selection moved on
                        return om.apply(this, arguments);
                    };
                }
            }
            if (redirected) pruneForeign();
            if (validId(assetTypeId)) {
                SW.lastConnectAt = Date.now();
                setFilterIds(assetIds);
            }
            return r;
        };
    }

    /* ======================================================================
     * S5 — fnAdvSearch: repaint in place when the stream is unchanged
     * ==================================================================== */
    W.fnAdvSearch = function () {
        var site = curSite(), type = curType();
        if (!validId(site)) { if (fn('showWarning')) W.showWarning('Please select a station.', 'Validation'); return; }
        if (!validId(type)) { if (fn('showWarning')) W.showWarning('Please click an Asset Type pill first.', 'Validation'); return; }
        var ids = W.getSelectedAssetIds();
        if (!ids.length) {
            if ($('#listAssetNumber input[type="checkbox"]').length) {
                if (fn('showWarning')) W.showWarning('Please select at least one Asset Number.', 'Validation');
                return;
            }
        }
        var key = keyOf(site, type);
        W.wsCurrentAssetTypeId = type;
        W._tlSearchInitiated = true;
        var mode = activeMode();
        var selKey = selectionKey();
        var selChanged = (SW.lastSelKey !== null && SW.lastSelKey !== selKey) || SW.lastSelKeyFor !== key;
        SW.lastSelKey = selKey;
        SW.lastSelKeyFor = key;

        if (hasLiveStream(key) && whitelist().known) {
            setFilterIds(ids);
            if (selChanged) { resetRenderCaches(); clearContainers(); }
            log('selection applied without reconnect:', ids.length || 'all', 'asset(s)');
            W._tlApplyViewMode(mode === 'Graph' ? 'Graph' : mode, { dataAlreadyOpen: true });
            if (fn('syncDownloadVisibility')) { try { W.syncDownloadVisibility(); } catch (e) { } }
            return;
        }

        if (fn('disconnectWebSocket')) W.disconnectWebSocket();
        var seq = SW.seq;
        W.loadBulkAssetMetadata(site, type, function () {
            if (seq !== SW.seq || curKey() !== key) { log('search for', key, 'abandoned — selection changed'); return; }
            resetRenderCaches();
            W.connectWebSocket(site, type, ids);
            setFilterIds(ids);
            log('dispatching mode=' + mode + ' for ' + (ids.length || 'all') + ' asset(s)');
            W._tlApplyViewMode(mode === 'Graph' ? 'Graph' : mode, { dataAlreadyOpen: false });
        });
    };

    /* ======================================================================
     * S6 — view mode wrapper: render under the visible view, keep the stream
     * ==================================================================== */
    var ensureStreamBusy = false;
    function ensureStream(mode) {
        if (ensureStreamBusy) return;
        if (mode !== 'Cards' && mode !== 'Table') return;
        if (!W._tlSearchInitiated) return;
        var key = curKey();
        if (!key) return;
        if (hasLiveStream(key)) return;
        if (!whitelist().known) return;
        if (!$('#listAssetNumber input[type="checkbox"]:checked').length) return;
        if (Date.now() - SW.lastConnectAt < 1500) return;
        ensureStreamBusy = true;
        setTimeout(function () {
            ensureStreamBusy = false;
            if (!curKey() || hasLiveStream(curKey())) return;
            log('live stream for', curKey(), 'missing — re-establishing');
            W.fnAdvSearch();
        }, 0);
    }

    function fixStats() {
        var n = visibleIds().length;
        var $a = $('#wsSA'); if ($a.length) $a.text(n);
        var $k = $('#atKpiAssets'); if ($k.length) $k.text(n);
        var $rc = $('#atRowCount'); if ($rc.length && n > 0) $rc.text(n + ' asset' + (n !== 1 ? 's' : ''));
    }

    var origApply = fn('_tlApplyViewMode');
    if (origApply) {
        W._tlApplyViewMode = function (mode, opts) {
            var r = withView(origApply, this, arguments);
            try { ensureStream(mode); fixStats(); } catch (e) { }
            return r;
        };
    }

    var origStats = fn('updateWsStats');
    if (origStats) {
        W.updateWsStats = function () {
            var r = withView(origStats, this, arguments);
            try { fixStats(); } catch (e) { }
            return r;
        };
    }

    /* ======================================================================
     * S9 — per-asset Graph overlay: the socket stays open behind it, and the
     * next batch made executeUIUpdate (4283) call the full card/table renderer
     * (the incremental updaters fall back to it when their container is
     * missing), which painted the list over the open graph. Skip the list
     * repaint while the overlay is up; the graph panel has its own live timer.
     * ==================================================================== */
    var origExec = fn('executeUIUpdate');
    if (origExec) {
        W.executeUIUpdate = function () {
            if (W._tlOverlayActive === 'Graph' && (document.getElementById('gWrap') || $('#tlBackBar').is(':visible'))) {
                W.wsUpdatedAssets = {};
                if (origStats) { try { W.updateWsStats(); } catch (e) { } }
                return;
            }
            return origExec.apply(this, arguments);
        };
    }

    /* ======================================================================
     * S1 — wrap every renderer / updater / export entry point
     * ==================================================================== */
    var VIEW_WRAPPED = [
        'executeUIUpdate', 'updateViewMode',
        'renderWsTable', 'renderWsTableFixed', 'buildFullTable', 'incrementalTableUpdate',
        'renderRDPMSView', 'updateRDPMSViewIncremental', 'reorderRdpmsCardsByAspect',
        'renderSignalGroupedTables', 'updateSignalGroupedTables', 'renderSignalAspectTables',
        'updateSignalAspectTablesIncremental', '_renderSignalGroupedTablesCore',
        'renderPointMachineView', 'updatePMViewIncremental', 'renderPmTableView', 'renderPointMachineTableView',
        'buildPmTableViewHtml', 'updatePointMachineTableCell', 'renderPointMachineVibrationTable',
        'updatePointMachineVibrationRows',
        'renderIpsGridView', 'updateIpsGridIncremental', 'renderIpsTableView', 'updateIpsTableIncremental',
        'ipsRenderTable', 'ipsBuildTableHtml', 'buildIpsTableRowModels', 'ipsUpdateTable',
        'fnBindTrackCards', 'updateTrackCardsIncremental', 'atRenderCards',
        'syncAttributeNamesFromLiveData', 'getSortedAssetIds',
        'tlBuildExportTableHtml', 'tlOpenExportScope', 'tlCollectExportSections',
        'fnDownloadExcel', 'fnDownloadCSV', 'fnDownloadPDF'
    ];
    for (var vi = 0; vi < VIEW_WRAPPED.length; vi++) wrapWithView(VIEW_WRAPPED[vi]);

    /* ======================================================================
     * S8 — updateRowCells (3848) reads `wsStaleAttrs[assetId]` but has no
     * `assetId` in scope → ReferenceError on every incremental Track table
     * update (caught by executeUIUpdate, so the live table silently froze on
     * the first batch). Supply it for the duration of the call; the guard
     * keeps this a no-op once the core is fixed.
     * ==================================================================== */
    var origURC = fn('updateRowCells');
    if (origURC && /wsStaleAttrs\[assetId\]/.test(String(origURC)) &&
        !/var\s+assetId|function\s+updateRowCells\s*\([^)]*assetId/.test(String(origURC))) {
        W.updateRowCells = function ($row, asset, isTrack) {
            var had = Object.prototype.hasOwnProperty.call(W, 'assetId'), prev = W.assetId;
            W.assetId = (asset && asset.AssetId !== undefined && asset.AssetId !== null) ? asset.AssetId
                : (($row && $row.attr) ? $row.attr('data-id') : undefined);
            try { return origURC.apply(this, arguments); }
            finally {
                if (had) W.assetId = prev;
                else { try { delete W.assetId; } catch (e) { W.assetId = undefined; } }
            }
        };
    }

    /* ======================================================================
     * Site / type change bookkeeping
     * ==================================================================== */
    function onSelectionChange(kind) {
        SW.seq++;
        clearBuffer();
        SW.lastSelKey = null;
        SW.lastSelKeyFor = null;
        if (kind === 'type') {
            // never let the delegated handler (core 13330) see a visible card view
            // and start an early socket through atSwitchView('Table') → fnSearchView.
            $('#atCardView').removeClass('at-force-shown').addClass('at-force-hidden').hide().empty();
            $('#trackCardContainer').hide();
        }
        resetRenderCaches();
        W.wsCurrentFilterAssetIds = [];
        if (typeof W.wsAttributeNames !== 'undefined') W.wsAttributeNames = [];
        if (typeof W.wsAttributeOrder !== 'undefined') W.wsAttributeOrder = {};
        if (!curKey()) {
            if (fn('disconnectWebSocket')) { try { W.disconnectWebSocket(); } catch (e) { } }
        }
    }
    // direct (non-delegated) handlers run before document-level ones; the core's
    // own direct handler (13022) is registered earlier and runs first.
    $('#drpAssetType').on('change.tlswitch', function () { onSelectionChange('type'); });
    $('#drpSite').on('change.tlswitch', function () { onSelectionChange('site'); });

    /* ======================================================================
     * install the ingest gate now and re-assert it if something replaces it
     * without wrapping (sip-telemetry.js removeBridge restores its captured
     * original, which is us, so that is fine).
     * ==================================================================== */
    installIngest();

    log('installed — visible-view rendering, ingest gate, metadata coalescing, connect guard');
})();
} catch (e) { if (window.console) console.error('[TLSWITCH] section failed', e); }
