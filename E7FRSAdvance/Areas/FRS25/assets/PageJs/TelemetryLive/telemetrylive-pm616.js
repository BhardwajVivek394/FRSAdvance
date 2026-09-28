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
    if (typeof window.PM616_ROW_STALE_TOLERANCE_MS !== 'number') window.PM616_ROW_STALE_TOLERANCE_MS = 0;

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
                esc(m.assetName) + '<i class="fas fa-history"></i></a>'
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
    function currentText(max, avg, count) {
        return hasVal(max) ? (max + ' / ' + avg + ' (' + count + ')') : '-- / -- (0)';
    }
    function applyRowLinks($tr, assetId, row, showA, showB, startIdx) {
        var dirPrefix = (row.operationType === 'Reverse') ? 'R' : 'N';
        var $tds = $tr.children('td');
        var ci = startIdx;
        if (showA && row.showA !== false) {
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'AC', rowHasCount(row.aAcCount), currentText(row.aAcMax, row.aAcAvg, row.aAcCount), 'pm-current-val');
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'AV', rowHasValue(row.aAvAvg), hasVal(row.aAvAvg) ? row.aAvAvg : '--', 'pm-voltage-val');
            $tds.eq(ci++).text(row.aAcOT);
        }
        if (showB && row.showB !== false) {
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'BC', rowHasCount(row.bBcCount), currentText(row.bBcMax, row.bBcAvg, row.bBcCount), 'pm-current-val-b');
            setValueCell($tds.eq(ci++), assetId, dirPrefix, 'BV', rowHasValue(row.bBvAvg), hasVal(row.bBvAvg) ? row.bBvAvg : '--', 'pm-voltage-val');
            $tds.eq(ci++).text(row.bBcOT);
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
