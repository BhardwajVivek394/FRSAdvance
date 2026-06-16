
'use strict';

var chart;
var openDropdown = null;
var useApiDataSource = false;
var API_BASE_URL = (typeof APP_CONFIG !== 'undefined' && APP_CONFIG.ProxyBaseUrl)
    ? APP_CONFIG.ProxyBaseUrl + '/api/liveValue'
    : '/api/liveValue';
// Line 23: same treatment
var WS_BASE_URL = (typeof APP_CONFIG !== 'undefined' && APP_CONFIG.WebSocketBaseUrl)
    ? APP_CONFIG.WebSocketBaseUrl + '/subscribe/liveValue'
    : '/subscribe/liveValue';
var WS_ENABLED_SITES = [14, 25, 155, 157, 92];
var wsConnection = null;
var wsReconnectTimer = null;
var wsReconnectAttempts = 0;
var MAX_RECONNECT_ATTEMPTS = 15;
var RECONNECT_DELAY = 3000;
var wsLiveData = {};
var wsAttributeNames = [];
var wsVibrationData = {};
var wsAttributeOrder = {};


var _trackColSeq = [
    'VTC TFC I/P',
    'VTC TFC O/P',
    'ITC TFC O/P',
    'VTC FEED END',
    'ITC FEED END',
    'VTC CH FEED END',
    'VTC RELAY END',
    'ITC RELAY END',
    'VTC TR',
    'VTC 24 DC LOC',
    'VTC 24 DC TPR I/P'
];


// ===== BLANK-DATA ASSET ORDERING =====
// An asset is treated as "blank" when none of its data attributes hold a
// usable (parseable numeric) value — i.e. every attribute would render as
// '--' in the UI. Blank-data assets are pushed to the end of every rendered
// list so that populated assets always appear first.
function assetHasNoData(assetId) {
    var a = wsLiveData[assetId];
    if (!a) return true;
    var attrs = a.attrs;
    if (!attrs) return true;
    var keys = Object.keys(attrs);
    if (keys.length === 0) return true;
    for (var i = 0; i < keys.length; i++) {
        var ad = attrs[keys[i]];
        if (!ad) continue;
        var raw = ad.Value;
        if (raw === null || raw === undefined || raw === '') continue;
        if (!isNaN(parseFloat(raw))) return false; // found a real value
    }
    return true;
}

// Stable partition: preserves order within each group, blank-data assets last.
function blankDataLast(ids) {
    if (!ids || ids.length < 2) return ids;
    var withData = [], blank = [];
    for (var i = 0; i < ids.length; i++) {
        if (assetHasNoData(ids[i])) blank.push(ids[i]);
        else withData.push(ids[i]);
    }
    return withData.concat(blank);
}

// ===== SYNC ATTRIBUTE NAMES FROM EXISTING LIVE DATA =====
function syncAttributeNamesFromLiveData() {
    if (!wsLiveData || Object.keys(wsLiveData).length === 0) return false;
    var firstAsset = wsLiveData[Object.keys(wsLiveData)[0]];
    if (!firstAsset.attrs) return false;
    var newAttrs = Object.keys(firstAsset.attrs);
    newAttrs.sort();
    if (JSON.stringify(wsAttributeNames) !== JSON.stringify(newAttrs)) {
        wsAttributeNames = newAttrs;
        wsCurrentColumns = [];
        wsTableInitialized = false;
        return true;
    }
    return false;
}
function atBuildTrackCard(aid) {
    var a = wsLiveData[aid];
    if (!a) return '';
    var name = a.AssetName || ('Asset ' + aid);
    var attrs = a.attrs || {};
    var dlRelays = a.dlRelays || {};
    var ts = a.lastUpdated ? (typeof fmtTime === 'function' ? fmtTime(a.lastUpdated) : '--') : '--';
    var siteIdForActions = a.SiteId || $('#drpSite').val();

    // ── Graph & Circuit action buttons (top-right) ─────────────────
    var actionsHtml = '<span class="tl-asset-actions" style="position:absolute; top:8px; right:8px;">' +
        '<button class="tl-asset-action" onclick="fnGetAssetGraph(\'' + siteIdForActions + '\',\'' + aid + '\')" title="Graph">' +
        '<i class="fa-solid fa-chart-line"></i>' +
        '</button>' +
        '<button class="tl-asset-action" onclick="fnGetAssetCircuit(\'' + aid + '\')" title="Circuit">' +
        '<i class="fa-solid fa-project-diagram"></i>' +
        '</button>' +
        '</span>';

    // ── Grid of attribute values (same as before) ─────────────────
    var grid = '';
    var attrList = (window.wsAttributeNames && window.wsAttributeNames.length) ? window.wsAttributeNames : Object.keys(attrs);
    for (var i = 0; i < attrList.length; i++) {
        var an = attrList[i];
        var ad = attrs[an];
        var raw = ad ? ad.Value : null;
        var val = (raw !== null && !isNaN(parseFloat(raw))) ? parseFloat(raw).toFixed(2) : '--';
        var cls = '';
        var num = parseFloat(raw);
        if (an === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'danger';
        else if ((an === 'TPR V' || an === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'danger';
        else if (an === 'Charger mA' && num < 100) cls = 'warn';
        else if (an === 'Choke V' && num > 1.8) cls = 'danger';
        var attrId = ad ? (ad.AttrId || ad.AssetAttributeId) : null;
        var lbl = (typeof getAttrDisplayName === 'function') ? getAttrDisplayName(an, attrId, aid) : an;
        var zone = i < 4 ? 'z1' : (i < 7 ? 'z2' : 'z3');
        var _trkStaleMap = wsStaleAttrs[aid] || {};
        if (_trkStaleMap[an]) cls += (cls ? ' ' : '') + 'ws-stale-val';
        var _tip = _getValueTooltip(an, num, cls);
        var _marks = _getCellMarkers(cls);
        grid += '<div class="' + zone + '">' +
            '<span class="at-tdg-lbl" title="' + lbl + '">' + lbl + '</span>' +
            '<span class="at-tdg-val ' + cls + '"' + (_tip ? ' title="' + _tip + '"' : '') + ' data-attr="' + an + '">' + val + _marks + '</span>' +
            '</div>';
    }

    // ── Derived values for Track ──────────────────────────────────
    if (typeof window.calculateDerivedValues === 'function') {
        var derived = window.calculateDerivedValues(attrs);
        var fmtD = window.formatDerivedValue || function (v) { return (v === null || v === undefined || isNaN(v)) ? '-' : v.toFixed(2); };
        grid += '<div class="z2"><span class="at-tdg-lbl" title="ITC BATT CHARG (mA)">ITC BATT CHARG (mA)</span><span class="at-tdg-val">' + fmtD(derived.itcBattCharg) + '</span></div>';
        grid += '<div class="z2"><span class="at-tdg-lbl" title="VTC VAR RES (V)">VTC VAR RES (V)</span><span class="at-tdg-val">' + fmtD(derived.vtcVarRes) + '</span></div>';
        grid += '<div class="z3"><span class="at-tdg-lbl" title="RTC CH FEED END (Ω)">RTC CH FEED END (Ω)</span><span class="at-tdg-val">' + fmtD(derived.rtcChFeedEnd) + '</span></div>';
        grid += '<div class="z3"><span class="at-tdg-lbl" title="RTC VAR RES (Ω)">RTC VAR RES (Ω)</span><span class="at-tdg-val">' + fmtD(derived.rtcVarRes) + '</span></div>';
        grid += '<div class="z3"><span class="at-tdg-lbl" title="VTC TR (V)">VTC TR (V)</span><span class="at-tdg-val">' + fmtD(derived.vtcTr) + '</span></div>';
        grid += '<div class="z1"><span class="at-tdg-lbl" title="IBALST (mA)">IBALST (mA)</span><span class="at-tdg-val">' + fmtD(derived.ibalst) + '</span></div>';
        grid += '<div class="z1"><span class="at-tdg-lbl" title="RRAIL (Ω)">RRAIL (Ω)</span><span class="at-tdg-val">' + fmtD(derived.rrail) + '</span></div>';
    }

    // ── DataLogger pills ──────────────────────────────────────────
    var pills = '';
    var dlKeys = Object.keys(dlRelays);
    for (var d = 0; d < Math.min(dlKeys.length, 5); d++) {
        var r = dlRelays[dlKeys[d]];
        var arrow = r.isPickup ? '↑' : '↓';
        var statusText = r.isPickup ? 'Pickup' : 'Drop';
        pills += '<span class="at-pill ' + (r.isPickup ? 'pickup' : 'drop') + '" title="' + statusText + '">' + (r.displayName || dlKeys[d]) + ' ' + arrow + '</span>';
    }
    if (dlKeys.length > 5) pills += '<span class="at-pill">+' + (dlKeys.length - 5) + ' more</span>';

    // ── Assemble the card HTML ────────────────────────────────────
    return '<div class="at-asset-card" data-state="live" data-id="' + aid + '" style="position:relative;">' +
        actionsHtml +
        '<div class="at-card-head">' +
        '<div><div class="at-card-name">TRACK : ' + name + '</div>' +
        '<div class="at-card-sub"><span class="at-live-dot"></span> Track Circuit · ' + ts + '</div></div>' +
        '</div>' +
        '<div class="at-track-grid">' + grid + '</div>' +
        (pills ? '<div class="at-card-foot">' + pills + '</div>' : '') +
        '</div>';
}
// ===== MODERN TRACK CARD RENDERER (GLOBAL) =====
//function atBuildTrackCard(aid) {
//    var a = wsLiveData[aid];
//    if (!a) return '';
//    var name = a.AssetName || ('Asset ' + aid);
//    var attrs = a.attrs || {};
//    var dlRelays = a.dlRelays || {};
//    var ts = a.lastUpdated ? (typeof fmtTime === 'function' ? fmtTime(a.lastUpdated) : '--') : '--';

//    var grid = '';
//    var attrList = (window.wsAttributeNames && window.wsAttributeNames.length) ? window.wsAttributeNames : Object.keys(attrs);
//    for (var i = 0; i < attrList.length; i++) {
//        var an = attrList[i];
//        var ad = attrs[an];
//        var raw = ad ? ad.Value : null;
//        var val = (raw !== null && !isNaN(parseFloat(raw))) ? parseFloat(raw).toFixed(2) : '--';
//        var cls = '';
//        var num = parseFloat(raw);
//        if (an === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'danger';
//        else if ((an === 'TPR V' || an === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'danger';
//        else if (an === 'Charger mA' && num < 100) cls = 'warn';
//        else if (an === 'Choke V' && num > 1.8) cls = 'danger';
//        var zone = i < 4 ? 'z1' : (i < 7 ? 'z2' : 'z3');
//        grid += '<div class="' + zone + '">' +
//            '<span class="at-tdg-lbl" title="' + an + '">' + an + '</span>' +
//            '<span class="at-tdg-val ' + cls + '">' + val + '</span>' +
//            '</div>';
//    }

//    // Add derived values for Track
//    if (typeof window.calculateDerivedValues === 'function') {
//        var derived = window.calculateDerivedValues(attrs);
//        grid += '<div class="z2"><span class="at-tdg-lbl">I TC BATT CHARG</span><span class="at-tdg-val">' + (window.formatDerivedValue ? window.formatDerivedValue(derived.itcBattCharg) : derived.itcBattCharg.toFixed(2)) + '</span></div>';
//        grid += '<div class="z2"><span class="at-tdg-lbl">V TC VAR RES</span><span class="at-tdg-val">' + (window.formatDerivedValue ? window.formatDerivedValue(derived.vtcVarRes) : derived.vtcVarRes.toFixed(2)) + '</span></div>';
//        grid += '<div class="z3"><span class="at-tdg-lbl">R TC CH FEED</span><span class="at-tdg-val">' + (window.formatDerivedValue ? window.formatDerivedValue(derived.rtcChFeedEnd) : derived.rtcChFeedEnd.toFixed(2)) + '</span></div>';
//        grid += '<div class="z3"><span class="at-tdg-lbl">R TC VAR RES</span><span class="at-tdg-val">' + (window.formatDerivedValue ? window.formatDerivedValue(derived.rtcVarRes) : derived.rtcVarRes.toFixed(2)) + '</span></div>';
//    }

//    // DataLogger pills
//    var pills = '';
//    var dlKeys = Object.keys(dlRelays);
//    for (var d = 0; d < Math.min(dlKeys.length, 5); d++) {
//        var r = dlRelays[dlKeys[d]];
//        pills += '<span class="at-pill ' + (r.isPickup ? 'pickup' : 'drop') + '">' + (r.displayName || dlKeys[d]) + '</span>';
//    }
//    if (dlKeys.length > 5) pills += '<span class="at-pill">+' + (dlKeys.length - 5) + ' more</span>';

//    var actions = (typeof window.tlBuildAssetActions === 'function') ? window.tlBuildAssetActions(aid) : '';

//    return '<div class="at-asset-card" data-state="live" data-id="' + aid + '">' +
//        '<div class="at-card-head">' +
//        '<div><div class="at-card-name">TRACK : ' + name + '</div>' +
//        '<div class="at-card-sub"><span class="at-live-dot"></span> Track Circuit · ' + ts + '</div></div>' +
//        actions +
//        '</div>' +
//        '<div class="at-track-grid">' + grid + '</div>' +
//        (pills ? '<div class="at-card-foot">' + pills + '</div>' : '') +
//        '</div>';
//}
window.atBuildSignalCard = window.atBuildSignalCard || function (aid) {
    var asset = wsLiveData[aid];
    if (!asset) return '';
    // Guard: if this asset is not a Signal, return an empty card or a placeholder
    if (parseInt(asset.AssetTypeId) !== 2) {
        return '<div class="at-asset-card"><div class="at-card-head">[Wrong asset type]</div></div>';
    }
    // Self-contained fallback. The previous version called
    // `atBuildSignalCard(aid)` here on the assumption that the
    // real builder would exist by call time — but since the `||`
    // already assigned THIS function to window.atBuildSignalCard,
    // that call was a recursive call back into itself, blowing
    // the stack on the first card render.
    var a = wsLiveData[aid];
    var name = a ? a.AssetName : aid;
    return '<div class="at-asset-card"><div class="at-card-head"><div class="at-card-name">SIGNAL : ' + name + '</div></div></div>';
};

window.atBuildPmCard = window.atBuildPmCard || function (aid) {
    // Self-contained fallback — same recursion bug as
    // atBuildSignalCard above. The inline call to atBuildPmCard()
    // was calling THIS fallback recursively.
    var a = wsLiveData[aid];
    var name = a ? a.AssetName : aid;
    return '<div class="at-asset-card"><div class="at-card-head"><div class="at-card-name">POINT : ' + name + '</div></div></div>';
};
function getTrackSortOrder(rawName) {
    if (!rawName) return 500;
    var attrId = wsAttributeOrder[rawName];
    var dn = rawName;
    if (attrId !== undefined && attrId !== null) {
        dn = assetAttributeMap[attrId] || assetAttributeMap[String(attrId)] || rawName;
    }
    var clean = dn.replace(/\s*\([^)]*\)\s*/g, '').replace(/_/g, ' ').trim().toUpperCase();
    for (var i = 0; i < _trackColSeq.length; i++) {
        var t = _trackColSeq[i].toUpperCase();
        if (clean === t || clean.indexOf(t) > -1 || t.indexOf(clean) > -1) return i + 1;
    }
    var rawClean = rawName.replace(/\s*\([^)]*\)\s*/g, '').replace(/_/g, ' ').trim().toUpperCase();
    if (rawClean !== clean) {
        for (var k = 0; k < _trackColSeq.length; k++) {
            var u = _trackColSeq[k].toUpperCase();
            if (rawClean === u || rawClean.indexOf(u) > -1 || u.indexOf(rawClean) > -1) return k + 1;
        }
    }
    return 500;
}
var wsCurrentAssetTypeId = null;
var wsValidAssetIds = [];   // whitelist from GetAssestBy / bulk asset list
var wsCurrentFilterAssetIds = [];

// ================================================================
// WS ASSET WHITELIST GUARD
// An asset must have been returned by GetBulkAssetMetadata (Track/Signal/IPS)
// or GetAssestBy (Point Machine) before its WebSocket frames are allowed to
// create a card / table row. The WS stream is site-wide and can carry assets
// that the bulk response never listed (e.g. assets of another sub-type, or
// stale/garbage assets). Those have no entry in bulkAssetMap, so their name
// resolves to "Asset <id>" and they show up as garbage rows in Signal cards /
// table view. This guard rejects any asset id not present in the bulk
// whitelist.
//
// IMPORTANT: returns true (allow) when the whitelist is empty, so SIP-only
// background connections and the brief window before bulk metadata finishes
// loading are not blocked. Once wsValidAssetIds is populated, only those ids
// pass.
function rebuildWsValidAssetIdsFromBulk() {
    wsValidAssetIds = [];

    if (Array.isArray(bulkAssetsList) && bulkAssetsList.length > 0) {
        for (var i = 0; i < bulkAssetsList.length; i++) {
            if (bulkAssetsList[i] && bulkAssetsList[i].Id !== undefined && bulkAssetsList[i].Id !== null) {
                wsValidAssetIds.push(String(bulkAssetsList[i].Id));
            }
        }
        return;
    }

    if (bulkAssetMap && Object.keys(bulkAssetMap).length > 0) {
        wsValidAssetIds = Object.keys(bulkAssetMap).map(String);
    }
}

function isAssetInBulkWhitelist(assetId) {
    var aid = String(assetId || '').trim();
    if (!aid) return false;

    // Rebuild whitelist if bulk metadata exists but wsValidAssetIds was not filled.
    if ((!wsValidAssetIds || wsValidAssetIds.length === 0) &&
        ((Array.isArray(bulkAssetsList) && bulkAssetsList.length > 0) ||
            (bulkAssetMap && Object.keys(bulkAssetMap).length > 0))) {
        rebuildWsValidAssetIdsFromBulk();
    }

    // When user selected an asset type, do NOT allow WS-only assets.
    // This prevents scrap WS assets from entering Signal Card/List before bulk finishes.
    var selectedType = $('#drpAssetType').val() || wsCurrentAssetTypeId;

    // FIX: Point Machine never calls GetBulkAssetMetadata — it uses GetAssestBy.
    // Its whitelist (wsValidAssetIds) is frequently empty/not-yet-ready when WS
    // frames arrive, so this guard was dropping EVERY PM frame ("not in bulk
    // response — ignoring") and the card/list never bound. The asset filter
    // (wsCurrentFilterAssetIds) already restricts PM to selected assets earlier
    // in processItemsInternal, so the bulk-whitelist gate must not apply to PM.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' &&
        isPointMachineAssetTypeForBulkSkip(selectedType)) {
        // If we DO have a populated PM whitelist, honour it; otherwise allow
        // through so live data binds while GetAssestBy is still resolving.
        if (wsValidAssetIds && wsValidAssetIds.length > 0) {
            return wsValidAssetIds.indexOf(aid) !== -1;
        }
        return true;
    }

    if (selectedType && selectedType !== '0') {
        return wsValidAssetIds && wsValidAssetIds.length > 0 &&
            wsValidAssetIds.indexOf(aid) !== -1;
    }

    // SIP/site-only background connection can still receive site-wide WS data.
    return true;
}
var wsIsConnected = false;
var wsMessageCount = 0;
var wsBatchCount = 0;
var wsRenderTimer = null;
var wsUpdatedAssets = {};
var wsSafetyInterval = null;
var wsHeartbeatInterval = null;
var wsLastMessageTime = null;
var WS_HEARTBEAT_TIMEOUT = 30000;
var wsConnParams = { siteId: null, assetTypeId: null, assetIds: [] };

// ===== RDPMS CONFIG =====
var RDPMS_DEFAULT_THRESHOLD = 5.0;


// STALE VALUE DETECTION
// ================================================================
var STALE_THRESHOLD_MS = 5 * 60 * 1000; // 5 minutes
var wsStaleAttrs = {};
var LIVE_VALUE_BASE_URL = '/FRS25/Telemetry/GetLiveValue';

// ── Single global batch timer (replaces per-asset timers) ─────────
var _stalePendingSet = {};
var _staleBatchTimer = null;
var STALE_BATCH_DELAY = 3000;       // wait 3 s of quiet before firing
var STALE_REQ_GAP_MS = 150;         // gap between sequential AJAX calls
var STALE_MAX_FLUSH = 5;            // never flush more than 5 assets at a time
var STALE_PATCH_THRESHOLD = 100;      // only check stale when ≤ 5 assets updated in a batch

// ── AUTO STALE CHECK: fires when no WS data received for 5 minutes ──
var _wsAutoStaleTimer = null;
var WS_AUTO_STALE_INTERVAL = 60 * 1000;    // check every 60s whether data went silent
var WS_AUTO_STALE_THRESHOLD = 5 * 60 * 1000; // 5 min no-data triggers auto stale call
var _wsAutoStaleRunning = false;
function isAttrStale(assetId, attrName) {
    return !!(wsStaleAttrs[assetId] && wsStaleAttrs[assetId][attrName]);
}

// ── INITIAL-LOAD GATE ─────────────────────────────────────────────
// The server sends a bulk dump of ALL attributes for ALL subscribed
// assets on connect. During that phase we must NOT fire GetLiveValue.
// After the dump stabilises, ONLY small incremental WS patches
// (≤ STALE_PATCH_THRESHOLD assets per batch) trigger stale checks.
var _wsInitialLoadComplete = false;
var _wsInitialLoadTimer = null;
var _WS_INITIAL_STABILISE_MS = 8000;  // 8 s of "no new asset" = dump done

function _markInitialLoadComplete() {
    if (_wsInitialLoadComplete) return;
    _wsInitialLoadComplete = true;
    console.log('[Stale] Initial WS load complete — incremental stale checks now enabled');
    // Start the auto-stale watchdog now that initial data is loaded
    startAutoStaleCheck();
}

// Helper: returns true when Signal asset type is selected AND view is List/Table.
function _isSignalListView() {
    if (typeof isSignalAssetType !== 'function' || !isSignalAssetType()) return false;
    var v = ($('#drpView').val() || '').toLowerCase();
    return (v === 'table');
}

// Called once per unique asset AFTER the WS batch loop (not per attribute).
// GUARD 1: Skip entirely during initial WS bulk load.
// GUARD 2: Signal assets — only check stale in List view.
// GUARD 3: _stalePendingSet is capped at STALE_MAX_FLUSH — excess dropped.
function checkStaleForAsset(assetId) {
    if (!assetId || assetId === '' || assetId === '0') return;

    // Never fire during the initial bulk load
    if (!_wsInitialLoadComplete) return;

    // Signal asset type — only check stale when in List/Table view
    if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
        if (!_isSignalListView()) return;
    }

    // Cap: don't accumulate more than STALE_MAX_FLUSH IDs
    if (Object.keys(_stalePendingSet).length >= STALE_MAX_FLUSH) return;

    _stalePendingSet[String(assetId)] = true;
    if (_staleBatchTimer) clearTimeout(_staleBatchTimer);
    _staleBatchTimer = setTimeout(function () {
        _staleBatchTimer = null;
        var ids = Object.keys(_stalePendingSet);
        _stalePendingSet = {};
        // Double-check cap on flush
        if (ids.length > STALE_MAX_FLUSH) ids = ids.slice(0, STALE_MAX_FLUSH);
        _flushStaleBatch(ids, 0);
    }, STALE_BATCH_DELAY);
}

function _flushStaleBatch(ids, idx) {
    if (idx >= ids.length) return;
    console.log('[Stale] GetLiveValue for asset ' + ids[idx] + ' (' + (idx + 1) + '/' + ids.length + ')');
    _fetchLiveValueAndCheckStale(ids[idx]);
    if (idx + 1 < ids.length) {
        setTimeout(function () { _flushStaleBatch(ids, idx + 1); }, STALE_REQ_GAP_MS);
    }
}

function _fetchLiveValueAndCheckStale(assetId) {
    var nowMs = Date.now();
    $.ajax({
        url: LIVE_VALUE_BASE_URL + '?assetId=' + assetId,
        type: 'GET',
        dataType: 'json',
        timeout: 15000,
        success: function (data) {
            if (!Array.isArray(data) || data.length === 0) return;
            _applyStaleFlags(assetId, data, nowMs);
        },
        error: function (xhr, status, err) {
            console.warn('[Stale] LiveValue error asset=' + assetId, status, err);
        }
    });
}

function stopStalePoll() {
    // Clear all pending debounce timers
    if (_staleBatchTimer) { clearTimeout(_staleBatchTimer); _staleBatchTimer = null; }
    _stalePendingSet = {};
    wsStaleAttrs = {};
}

// ── AUTO STALE: periodically checks if WS went silent ────────────
function startAutoStaleCheck() {
    stopAutoStaleCheck();
    _wsAutoStaleRunning = true;
    console.log('[AutoStale] Started — will check every ' + (WS_AUTO_STALE_INTERVAL / 1000) + 's for ' + (WS_AUTO_STALE_THRESHOLD / 60000) + 'min silence');
    _wsAutoStaleTimer = setInterval(_autoStaleCheckTick, WS_AUTO_STALE_INTERVAL);
}

function stopAutoStaleCheck() {
    if (_wsAutoStaleTimer) { clearInterval(_wsAutoStaleTimer); _wsAutoStaleTimer = null; }
    _wsAutoStaleRunning = false;
}

function _autoStaleCheckTick() {
    if (!wsIsConnected || !_wsInitialLoadComplete) return;
    var now = Date.now();
    var elapsed = now - (wsLastMessageTime || 0);

    if (elapsed < WS_AUTO_STALE_THRESHOLD) return; // data is still fresh

    console.log('[AutoStale] No WS data for ' + Math.round(elapsed / 1000) + 's — running stale check for all assets');

    var assetIds = Object.keys(wsLiveData || {});
    if (assetIds.length === 0) return;

    // Fetch stale for all assets in small batches to avoid flooding server
    var batch = assetIds.slice(0, STALE_MAX_FLUSH);
    var remaining = assetIds.slice(STALE_MAX_FLUSH);

    _flushAutoStaleBatch(batch, 0, remaining);
}

function _flushAutoStaleBatch(batch, idx, remaining) {
    if (idx >= batch.length) {
        // If there are remaining assets, schedule next batch after a gap
        if (remaining.length > 0) {
            var nextBatch = remaining.slice(0, STALE_MAX_FLUSH);
            var nextRemaining = remaining.slice(STALE_MAX_FLUSH);
            setTimeout(function () {
                _flushAutoStaleBatch(nextBatch, 0, nextRemaining);
            }, STALE_BATCH_DELAY);
        } else {
            console.log('[AutoStale] All assets checked');
        }
        return;
    }
    console.log('[AutoStale] GetLiveValue for asset ' + batch[idx] + ' (' + (idx + 1) + '/' + batch.length + ')');
    _fetchLiveValueAndCheckStale(batch[idx]);
    if (idx + 1 < batch.length) {
        setTimeout(function () { _flushAutoStaleBatch(batch, idx + 1, remaining); }, STALE_REQ_GAP_MS);
    } else {
        // Move to remaining
        _flushAutoStaleBatch(batch, batch.length, remaining);
    }
}
function _applyStaleFlags(assetId, apiItems, nowMs) {
    var aid = String(assetId);
    if (!wsStaleAttrs[aid]) wsStaleAttrs[aid] = {};
    var flagsChanged = false;

    apiItems.forEach(function (item) {
        var attrName = item.AssetAttributeName;
        if (!attrName) return;

        // FIX: Better DataLogger detection - check multiple sources
        var isDatalogger = false;

        // Check wsLiveData dlRelays first
        if (wsLiveData[aid] && wsLiveData[aid].dlRelays) {
            for (var dlk in wsLiveData[aid].dlRelays) {
                if (dlk === attrName ||
                    (wsLiveData[aid].dlRelays[dlk].displayName === attrName) ||
                    (wsLiveData[aid].dlRelays[dlk].attrName === attrName)) {
                    isDatalogger = true;
                    break;
                }
            }
        }

        // FIX: Check userAssetDataloggerMap
        if (!isDatalogger && typeof userAssetDataloggerMap !== 'undefined') {
            for (var udk in userAssetDataloggerMap) {
                if (udk.indexOf(aid + '_') === 0) {
                    var udEntry = userAssetDataloggerMap[udk];
                    if (udEntry.name === attrName ||
                        udEntry.attributeName === attrName ||
                        udEntry.AttributeName === attrName) {
                        isDatalogger = true;
                        break;
                    }
                }
            }
        }

        // FIX: Also check by DataType from the API item
        if (!isDatalogger && item.DataType === 'DataLogger') {
            isDatalogger = true;
        }

        if (isDatalogger) return;

        var tsStr = item.TimestampLocal || item.TimestampDevice || null;
        if (!tsStr) return;

        var tsMs = new Date(tsStr).getTime();
        if (isNaN(tsMs) || tsMs <= 0) return;

        var isStale = ((nowMs - tsMs) > STALE_THRESHOLD_MS);
        var wasStale = wsStaleAttrs[aid][attrName];

        if (wasStale !== isStale) {
            wsStaleAttrs[aid][attrName] = isStale;
            flagsChanged = true;
        }
    });

    if (flagsChanged) _applyStaleClassesToUI(aid);
}

function _staleMarkerHtml() {
    return ' <i class="fas fa-clock ws-stale-marker" title="Stale: no update for 5+ min"></i>';
}

function _warnMarkerHtml(cls) {
    if (cls.indexOf('val-danger') > -1 || cls.indexOf('danger') > -1) {
        return ' <i class="fas fa-exclamation-triangle ws-warn-marker ws-danger" title="Critical: out of safe range"></i>';
    }
    if (cls.indexOf('warn') > -1) {
        return ' <i class="fas fa-exclamation-triangle ws-warn-marker ws-warning" title="Warning: approaching limit"></i>';
    }
    return '';
}

// Returns both markers when both conditions apply
function _getCellMarkers(cls) {
    var html = '';
    if (cls.indexOf('warn') > -1 || cls.indexOf('danger') > -1 || cls.indexOf('val-danger') > -1) {
        html += _warnMarkerHtml(cls);
    }
    if (cls.indexOf('ws-stale-val') > -1) {
        html += _staleMarkerHtml();
    }
    return html;
}

function _setStaleCell($el, isStale) {
    $el.toggleClass('ws-stale-val', isStale);
    if (isStale) {
        if (!$el.find('.ws-stale-marker').length) {
            $el.append(_staleMarkerHtml());
        }
    } else {
        $el.find('.ws-stale-marker').remove();
    }
}

function _applyStaleClassesToUI(aid) {
    var staleMap = wsStaleAttrs[aid] || {};

    // ── Table cells (generic + signal) ──
    var $tableRow = $('#wsLiveTable tbody tr[data-id="' + aid + '"]');
    if (!$tableRow.length) $tableRow = $('#signalAspectTablesWrapper tr[data-id="' + aid + '"]');

    $tableRow.find('[data-attr]').each(function () {
        var an = $(this).attr('data-attr');
        if (an === 'LastUpdate') return;
        if ($(this).closest('.dl-cell').length) return;
        _setStaleCell($(this), !!staleMap[an]);
    });

    // ── Signal grouped tables (List view) ──
    var $sigRow = $('.sig-group-wrap tr[data-id="' + aid + '"]');
    if ($sigRow.length) {
        $sigRow.find('[data-attr]').each(function () {
            var an = $(this).attr('data-attr');
            if (an === 'LastUpdate') return;
            if ($(this).closest('.dl-cell').length) return;
            _setStaleCell($(this), !!staleMap[an]);
        });
    }

    // ── RDPMS signal card ──
    var $card = $('#rdpmsCard_' + aid);
    if ($card.length) {
        $card.find('[data-attr]').each(function () {
            var an = $(this).attr('data-attr');
            if ($(this).hasClass('rdpms-dl-badge')) return;
            _setStaleCell($(this), !!staleMap[an]);
        });

        // Collect stale names (excluding DataLogger)
        var staleNames = [];
        var dlRelays_ = (wsLiveData[aid] && wsLiveData[aid].dlRelays) || {};
        for (var sk in staleMap) {
            if (!staleMap[sk]) continue;
            var isDl = false;
            for (var dk in dlRelays_) {
                if (dk === sk || (dlRelays_[dk].displayName === sk) || (dlRelays_[dk].attrName === sk)) {
                    isDl = true;
                    break;
                }
            }
            if (!isDl) staleNames.push(sk);
        }

        // Remove old stale UI
        $card.find('.ws-stale-badge').remove();
        $card.find('.rdpms-stale-strip').remove();

        if (staleNames.length > 0) {
            $card.find('.card-header-signal, .rdpms-card-header').first()
                .append('<span class="ws-stale-badge"><i class="fas fa-clock"></i> ' + staleNames.length + ' Stale</span>');

            var labels = [];
            for (var si = 0; si < staleNames.length; si++) {
                var base = staleNames[si].replace(/ mA$/, '').replace(/ V$/, '');
                if (labels.indexOf(base) === -1) labels.push(base);
            }

            var stripHtml = '<div class="rdpms-stale-strip">' +
                '<span class="rdpms-stale-strip-icon"><i class="fas fa-clock"></i></span>' +
                '<span class="rdpms-stale-strip-text">Stale: ';
            for (var li = 0; li < labels.length; li++) {
                stripHtml += '<span class="rdpms-stale-chip">' + labels[li] + '</span>';
            }
            stripHtml += '</span></div>';

            var $routeTable = $card.find('.rdpms-route-table');
            if ($routeTable.length) { $routeTable.after(stripHtml); }
            else { $card.find('.card-body-signal').append(stripHtml); }
        }
    }
    // ── Point Machine table/cards ──
    var $pmRow = $('#pointMachineContainer tr[data-id="' + aid + '"]');
    if (!$pmRow.length) $pmRow = $('.pm-card[data-id="' + aid + '"]');
    $pmRow.find('[data-attr]').each(function () {
        var an = $(this).attr('data-attr');
        if (an === 'LastUpdate') return;
        if ($(this).closest('.dl-cell').length) return;
        _setStaleCell($(this), !!staleMap[an]);
    });

    // ── IPS cards ──
    var $ipsCard = $('[data-ips-id="' + aid + '"]');
    if ($ipsCard.length) {
        $ipsCard.find('.ips-attr-val[data-attr]').each(function () {
            var an = $(this).attr('data-attr');
            _setStaleCell($(this), !!staleMap[an]);
        });
    }

    // ── Track cards ──
    var $trackCard = $('.at-asset-card[data-id="' + aid + '"]');
    if ($trackCard.length) {
        $trackCard.find('[data-attr]').each(function () {
            var an = $(this).attr('data-attr');
            _setStaleCell($(this), !!staleMap[an]);
        });
    }
}



// ================================================================
// ZERO OFFSET CACHE AND FETCH LOGIC
// Fetches ZeroOffset from GetSignalAspectData API (mGlobalConfigs where AssetAttributeId = 30)
// ================================================================
var zeroOffsetCache = {}; // Cache: { assetId: { value: number, fetched: boolean, fetching: boolean } }

// ── OPTIMIZED: No AJAX — reads from zeroOffsetCache pre-populated
//    by loadBulkAssetMetadata(). Falls back to default if not cached.
function fetchZeroOffsetForAsset(assetId, callback) {
    var cached = zeroOffsetCache[assetId];
    var val = (cached && cached.fetched) ? cached.value : RDPMS_DEFAULT_THRESHOLD;

    // Ensure cache entry exists (so future lookups don't re-trigger)
    if (!cached || !cached.fetched) {
        zeroOffsetCache[assetId] = { value: val, fetched: true, fetching: false, pendingCallbacks: [] };
    }
    if (wsLiveData[assetId]) {
        wsLiveData[assetId].ZeroOffsetValue = val;
    }
    if (callback) {
        try { callback(val); } catch (e) { console.error('[ZeroOffset] callback error', e); }
    }
}

// Get ZeroOffset value (from cache only — bulk preloads all values)
function getZeroOffsetForAsset(assetId) {
    if (zeroOffsetCache[assetId] && zeroOffsetCache[assetId].fetched) {
        return zeroOffsetCache[assetId].value;
    }
    return RDPMS_DEFAULT_THRESHOLD;
}
var rdpmsCardsBuilt = {};

var SIGNAL_STRUCT_ATTRS = ['RG mA', 'RG V', 'DG mA', 'DG V', 'HG mA', 'HG V', 'HHG mA', 'HHG V',
    'PILOT mA', 'PILOT V', 'PILOTRoot mA', 'PILOTRoot V',
    'AUG mA', 'AUG V', 'BUG mA', 'BUG V', 'CUG mA', 'CUG V', 'DUG mA', 'DUG V', 'EUG mA', 'EUG V',
    'Co_Hg mA', 'Co_Hg V',
    'On Aspect mA', 'On Aspect V', 'Off Aspect mA', 'Off Aspect V',
    'DPR', 'DPR mA', 'DPR V', 'HPR', 'HPR mA', 'HPR V', 'HHPR', 'HHPR mA', 'HHPR V'];

function getCardFingerprint(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return '';
    var keys = [];
    for (var k in asset.attrs) { if (SIGNAL_STRUCT_ATTRS.indexOf(k) > -1) keys.push(k); }
    keys.sort();
    return keys.join(',');
}

function needsCardRebuild(assetId) {
    if (!rdpmsCardsBuilt[assetId]) return true;
    return getCardFingerprint(assetId) !== rdpmsCardsBuilt[assetId];
}

function rebuildCard(assetId) {

    var oldFP = rdpmsCardsBuilt[assetId] || '';
    var newFP = getCardFingerprint(assetId);
    var $oldCard = $('#rdpmsCard_' + assetId);
    var $prev = $oldCard.prev();
    var $parent = $oldCard.parent();
    $oldCard.remove();
    var name = (wsLiveData[assetId].AssetName || '').toLowerCase();
    if (name.indexOf('sh') > -1) { buildShuntSignalCard(assetId); }
    else { buildMainSignalCard(assetId); }
    var $newCard = $('#rdpmsCard_' + assetId);
    if ($prev.length) { $prev.after($newCard); }
    else if ($parent.length) { $parent.prepend($newCard); }
    rdpmsCardsBuilt[assetId] = getCardFingerprint(assetId);
    if (name.indexOf('sh') > -1) { updateShuntSignalLights(assetId); }
    else { updateMainSignalLights(assetId); }
}

// ===== TOAST =====
function showToast(type, title, message, duration) {
    duration = duration || 4000;
    var icons = { success: '<i class="fas fa-check"></i>', error: '<i class="fas fa-times"></i>', warning: '<i class="fas fa-exclamation-triangle"></i>', info: '<i class="fas fa-info-circle"></i>' };
    var id = 'toast_' + Date.now();
    $('#toastContainer').append('<div class="toast-notification toast-' + type + '" id="' + id + '"><div class="toast-icon">' + icons[type] + '</div><div class="toast-body"><div class="toast-title">' + title + '</div><div class="toast-message">' + message + '</div></div><button class="toast-close" onclick="closeToast(\'' + id + '\')">×</button><div class="toast-progress"></div></div>');
    setTimeout(function () { closeToast(id); }, duration);
}
function closeToast(id) { var $t = $('#' + id); if ($t.length && !$t.hasClass('hiding')) { $t.addClass('hiding'); setTimeout(function () { $t.remove(); }, 300); } }
function showSuccess(m, t) { showToast('success', t || 'Success', m); }
function showError(m, t) { showToast('error', t || 'Error', m); }
function showWarning(m, t) { showToast('warning', t || 'Warning', m); }
function showInfo(m, t) { showToast('info', t || 'Info', m); }

// ===== HELPERS =====
function isWebSocketSite(siteId) { return true; }
function toggleDataSource() {
    if ($('#chkDataSource').prop('disabled')) {
        $('#chkDataSource').prop('checked', false);
        useApiDataSource = false;
        showWarning('API mode is not available for Signal and Point Machine. WebSocket only.', 'Data Source');
        return;
    }

    useApiDataSource = $('#chkDataSource').prop('checked');
    disconnectWebSocket();

    if (useApiDataSource) {
        $('#lblApi').css('opacity', '1');
        $('#lblWs').css('opacity', '0.5');
        $('#btnRefresh').show();
        showInfo('Switched to REST API', 'Data Source');
    } else {
        $('#lblApi').css('opacity', '0.5');
        $('#lblWs').css('opacity', '1');
        $('#btnRefresh').hide();
        showInfo('Switched to WebSocket', 'Data Source');
    }
}


function refreshApiData() { if (useApiDataSource && $('#drpView').val() === 'Table') fnBindTableFromAPI(); }

function buildWebSocketUrl(siteId, assetTypeId, assetIds) {
    if (assetIds && assetIds.length === 1 && assetIds[0] !== '' && assetIds[0] !== '0') return WS_BASE_URL + '/' + assetIds[0];
    if (siteId && assetTypeId && assetTypeId !== '' && assetTypeId !== '0') return WS_BASE_URL + '/' + siteId + '/' + assetTypeId + '/all';
    if (siteId) return WS_BASE_URL + '/' + siteId + '/all';
    return WS_BASE_URL + '/all';
}
// Asset Attribute Map: Id → AliasName (loaded from API)
var assetAttributeMap = {};       // { 1: "Vr", 2: "If mA", 3: "Ir mA", ... }
var assetAttributeByName = {};    // { "Vr": "V<sub>r</sub>", "If mA": "I<sub>f</sub> mA", ... }
var assetAttributeLoaded = false;
var wsAttributeIds = {};          // { "Vr": 1, "If mA": 2, ... } - maps name to ID

// ===== FALLBACK ALIAS MAPPINGS (used when API doesn't return alias) =====
// These mappings ensure alias names always display correctly
var fallbackAliasById = {
    // Track Circuit
    1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)',
    5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)',
    // Signal
    9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA',
    13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA',
    154: 'Root V', 155: 'Root mA',
    227: 'UG V', 228: 'UG mA',
    327: 'HHPR', 328: 'DPR', 329: 'HPR',
    337: 'PILOT mA', 499: 'PILOT V',
    610: 'Co_Hg mA', 611: 'Co_Hg V',
    // Point Machine
    25: 'A End - NWKR', 26: 'A End - RWKR', 27: 'B End - NWKR', 28: 'B End - RWKR',
    212: 'B End - NW-V', 213: 'B End - NW-C', 214: 'B End - RW-V', 215: 'B End - RW-C',
    216: 'A End - NW-V', 217: 'A End - NW-C', 218: 'A End - RW-V', 219: 'A End - RW-C',
    // Track Circuit continued
    31: 'Rx1 mV', 32: 'Rx2 mV', 33: 'Tx1 V', 34: 'Tx2 V', 35: 'Supply V', 36: 'Modem mV'
};


//function loadAssetAttributes(callback) {
//    // Use the unified GetUserAssetInfo API instead of GetAssetAttributes.
//    // GetUserAssetInfo provides both analog (RoleType='c') and DataLogger (RoleType='d')
//    // attributes in one call, scoped to the current site. Aliases are per-site,
//    // so the global GetAssetAttributes endpoint returns the wrong names.
//    var siteId = $('#drpSite').val();

//    if (!siteId || siteId === '0') {
//        console.warn('[AssetAttr] No site selected — skipping attribute load');
//        if (callback) callback();
//        return;
//    }

//    loadUserAssetInfo(siteId, function () {
//        // Project the two site-scoped maps into the legacy assetAttributeMap /
//        // assetAttributeByName so all downstream code (table headers, cards,
//        // graph, circuit) keeps working unchanged.
//        var currentAssetTypeId = parseInt(wsCurrentAssetTypeId || 0);
//        var count = 0;

//        // RoleType='c' — RDPMS + PointMachine indication
//        // Key format in map: "assetId_assetAttributeId"  →  we want assetAttributeId as the map key
//        for (var sk in userAssetSimpleMap) {
//            var sEntry = userAssetSimpleMap[sk];
//            if (!sEntry || !sEntry.name) continue;

//            // Only keep attributes matching the current asset type so that
//            // Signal / PM attribute names don't overwrite Track IDs (and vice versa).
//            var sEntryAssetTypeId = parseInt(sEntry.assetTypeId || 0);
//            if (sEntryAssetTypeId > 0 && currentAssetTypeId > 0 && sEntryAssetTypeId !== currentAssetTypeId) {
//                continue;
//            }

//            var sParts = sk.split('_');
//            if (sParts.length !== 2) continue;
//            var aaId = sParts[1];

//            assetAttributeMap[aaId] = sEntry.name;
//            assetAttributeMap[String(aaId)] = sEntry.name;
//            var numAaId = parseInt(aaId);
//            if (!isNaN(numAaId)) assetAttributeMap[numAaId] = sEntry.name;

//            assetAttributeByName[sEntry.name] = sEntry.name;
//            count++;
//        }

//        // RoleType='d' — DataLogger
//        // Key format in map: "assetId_role"  →  we want role as the map key
//        for (var dk in userAssetDataloggerMap) {
//            var dEntry = userAssetDataloggerMap[dk];
//            if (!dEntry || !dEntry.name) continue;

//            var dEntryAssetTypeId = parseInt(dEntry.assetTypeId || 0);
//            if (dEntryAssetTypeId > 0 && currentAssetTypeId > 0 && dEntryAssetTypeId !== currentAssetTypeId) {
//                continue;
//            }

//            var dParts = dk.split('_');
//            if (dParts.length !== 2) continue;
//            var role = dParts[1];

//            assetAttributeMap[role] = dEntry.name;
//            assetAttributeMap[String(role)] = dEntry.name;
//            var numRole = parseInt(role);
//            if (!isNaN(numRole)) assetAttributeMap[numRole] = dEntry.name;

//            assetAttributeByName[dEntry.name] = dEntry.name;
//            count++;
//        }

//        assetAttributeLoaded = true;
//        console.log('[AssetAttr] Projected ' + count + ' attribute(s) from GetUserAssetInfo into legacy map');
//        if (callback) callback();
//    });
//}


function loadAssetAttributes(callback) {
    var siteId = $('#drpSite').val();
    var assetTypeId = $('#drpAssetType').val() || wsCurrentAssetTypeId;

    if (!siteId || siteId === '0' || !assetTypeId || assetTypeId === '0') {
        console.warn('[AssetAttr] Missing site/asset type — using existing bulk cache only');
        if (callback) callback();
        return;
    }

    loadBulkAssetMetadata(siteId, assetTypeId, callback);
}
function getAttrDisplayName(attrName, attrId, assetId) {
    // Only show AliasName returned by GetBulkAssetMetadata.assetAttributes.
    // Do not show raw AssetAttributeName and do not use hardcoded fallbackAliasById.
    var alias = '';

    if (typeof getBulkAliasName === 'function') {
        alias = getBulkAliasName(assetId, attrName, attrId);
        if (alias) return alias;
    }

    if (attrName && assetAttributeByName[attrName]) {
        return assetAttributeByName[attrName];
    }

    if (attrId !== undefined && attrId !== null) {
        if (assetAttributeMap[String(attrId)]) return assetAttributeMap[String(attrId)];

        var numId = parseInt(attrId);
        if (!isNaN(numId) && assetAttributeMap[numId]) return assetAttributeMap[numId];
    }

    return attrName || '';
}

window.getAttrDisplayName = getAttrDisplayName;
// Get plain text display name (strips HTML tags) - for use in ECharts/graphs
function getAttrDisplayNamePlain(attrName, attrId, assetId) {
    var displayName = getAttrDisplayName(attrName, attrId, assetId);
    return displayName ? displayName.replace(/<[^>]*>/g, '') : attrName;
}

// Format alias name with subscript styling
function formatAliasName(name) {
    if (!name || name.length < 2) return name;
    return name.charAt(0) + '<span class="alias-sub">' + name.substring(1) + '</span>';
}



// ===== DERIVED VALUES CALCULATION =====
var TR_RES = 10; // TR Resistance for QTA2 = 10Ω

function getAttrValue(attrs, name) {
    if (!attrs || !name) return null;
    if (attrs[name] !== undefined && attrs[name] !== null && attrs[name].Value !== undefined)
        return attrs[name].Value;
    var nameLc = name.toLowerCase();
    for (var key in attrs) {
        if (key.toLowerCase() === nameLc && attrs[key] !== null && attrs[key].Value !== undefined)
            return attrs[key].Value;
    }
    return null;
}

function calculateDerivedValues(attrs) {
    // Build AttrId → value map as fallback (WS stores AttrId on every attr entry)
    var byId = {};
    for (var k in attrs) {
        var a = attrs[k];
        if (a && a.Value !== undefined && a.Value !== null) {
            var numVal = parseFloat(a.Value);
            if (!isNaN(numVal)) {
                var aid1 = parseInt(a.AttrId || 0);
                var aid2 = parseInt(a.AssetAttributeId || 0);
                if (aid1 > 0) byId[aid1] = numVal;
                if (aid2 > 0 && byId[aid2] === undefined) byId[aid2] = numVal;
            }
        }
    }

    // Multi-alias resolver: tries each name variant, then AttrId fallback
    // Returns null if attribute is not present (so derived values can show '-')
    function gv(nameAliases, fallbackAttrId) {
        for (var ni = 0; ni < nameAliases.length; ni++) {
            var raw = getAttrValue(attrs, nameAliases[ni]);
            if (raw !== null && raw !== undefined) {
                var fv = parseFloat(raw);
                if (!isNaN(fv)) return fv;
            }
        }
        if (fallbackAttrId != null && byId[fallbackAttrId] !== undefined)
            return byId[fallbackAttrId];
        return null; // attribute not present
    }

    // Resolve each sensor -- short name first, then all known alias variants, then AttrId
    // AttrId: 1=If mA, 2=Ir mA, 4=Choke V, 5=Charger mA
    var ifMa = gv(['If mA', 'ITC FEED END(mA)', 'ITC FEED END'], 1);
    var irMa = gv(['Ir mA', 'ITC RELAY END(mA)', 'ITC RELAY END'], 2);
    var chargerMa = gv(['Charger mA', 'ITC TFC O/P(mA)', 'ITC TFC O/P'], 5);
    var chokeV = gv(['Choke V', 'VTC CH FEED END(V)', 'VTC CH FEED END'], 4);
    var chargerOpV = gv(['Charger OP V', 'VTC TFC O/P', 'VTC TFC O/P(V)',
        'VTC TFC I/P', 'VTC TFC I/P(V)'], null);
    var vf = gv(['Vf', 'Vf V', 'VTC FEED END',
        'VTC FEED END(V)'], null);
    var trVRelay = gv(['Vr', 'VTC TR', 'VTC RELAY END', 'VTC RELAY END(V)'], null);

    // Helper: if any required operand is null, the derived result is null (show '-')
    function safeCalc(fn, requiredVars) {
        for (var ri = 0; ri < requiredVars.length; ri++) {
            if (requiredVars[ri] === null) return null;
        }
        return fn();
    }

    var ifMa0 = ifMa !== null ? ifMa : 0;   // safe numeric for division guards
    var derived = {};
    // 1. ITC BATT CHARG (mA) = Charger mA − If mA
    derived.itcBattCharg = safeCalc(function () { return chargerMa - ifMa; }, [chargerMa, ifMa]);
    // 2. VTC VAR RES (V) = Charger OP V − Vf − Choke V
    derived.vtcVarRes = safeCalc(function () { return chargerOpV - vf - chokeV; }, [chargerOpV, vf, chokeV]);
    // 3. RTC CH FEED END (Ω) = (Choke V / If mA) × 1000
    derived.rtcChFeedEnd = safeCalc(function () { return ifMa !== 0 ? (chokeV / ifMa) * 1000 : null; }, [chokeV, ifMa]);
    // 4. RTC VAR RES (Ω) = (VTC VAR RES / If mA) × 1000
    derived.rtcVarRes = (derived.vtcVarRes === null || ifMa === null) ? null :
        (ifMa !== 0 ? (derived.vtcVarRes / ifMa) * 1000 : null);
    // 5. VTC TR (V) = Ir mA × TR_RES / 1000
    derived.vtcTr = safeCalc(function () { return (irMa * TR_RES) / 1000; }, [irMa]);
    // 6. IBALST (mA) = If mA − Ir mA
    derived.ibalst = safeCalc(function () { return ifMa - irMa; }, [ifMa, irMa]);
    // 7. RRAIL (Ω) = 2 × (Vf − VTC TR) / (If mA + Ir mA) × 1000
    derived.rrail = (vf === null || derived.vtcTr === null || ifMa === null || irMa === null) ? null :
        ((ifMa + irMa) !== 0 ? (2 * (vf - derived.vtcTr) / ((ifMa + irMa) / 1000)) : null);
    return derived;

    // Resolve each sensor -- short name first, then all known alias variants, then AttrId
    // AttrId: 1=If mA, 2=Ir mA, 4=Choke V, 5=Charger mA
    var ifMa = gv(['If mA', 'ITC FEED END(mA)', 'ITC FEED END'], 1);
    var irMa = gv(['Ir mA', 'ITC RELAY END(mA)', 'ITC RELAY END'], 2);
    var chargerMa = gv(['Charger mA', 'ITC TFC O/P(mA)', 'ITC TFC O/P'], 5);
    var chokeV = gv(['Choke V', 'VTC CH FEED END(V)', 'VTC CH FEED END'], 4);
    var chargerOpV = gv(['Charger OP V', 'VTC TFC O/P', 'VTC TFC O/P(V)',
        'VTC TFC I/P', 'VTC TFC I/P(V)'], null);
    var vf = gv(['Vf', 'Vf V', 'VTC FEED END',
        'VTC FEED END(V)'], null);
    var trVRelay = gv(['Vr', 'VTC TR', 'VTC RELAY END', 'VTC RELAY END(V)'], null);


    var derived = {};
    // 1. ITC BATT CHARG (mA) = Charger mA − If mA
    derived.itcBattCharg = chargerMa - ifMa;
    // 2. VTC VAR RES (V) = Charger OP V − Vf − Choke V
    derived.vtcVarRes = chargerOpV - vf - chokeV;
    // 3. RTC CH FEED END (Ω) = (Choke V / If mA) × 1000
    derived.rtcChFeedEnd = ifMa !== 0 ? (chokeV / ifMa) * 1000 : 0;
    // 4. RTC VAR RES (Ω) = (VTC VAR RES / If mA) × 1000
    derived.rtcVarRes = ifMa !== 0 ? (derived.vtcVarRes / ifMa) * 1000 : 0;
    // 5. VTC TR (V) = Ir mA × 1.4Ω / 1000
    derived.vtcTr = (irMa * TR_RES) / 1000;
    // 6. IBALST (mA) = If mA − Ir mA
    derived.ibalst = ifMa - irMa;
    // 7. RRAIL (Ω) = 2 × (Vf − VTC TR) / (If mA + Ir mA) × 1000
    var ifPlusIr = ifMa + irMa;
    //derived.rrail = ifPlusIr !== 0 ? (2 * (vf - derived.vtcTr) / ifPlusIr) * 1000 : 0;
    // CORRECT -- uses actual TR_V_Relay measured voltage
    derived.rrail = ifPlusIr !== 0 ? (2 * (vf - derived.vtcTr) / (ifPlusIr / 1000)) : 0;
    return derived;
}


function formatDerivedValue(val) {
    if (val === null || val === undefined || isNaN(val)) return '-';
    return val.toFixed(2);
}

function getDerivedHeaders() {
    return '<th class="derived-col" title="Charger mA - If mA">I<span class="alias-sub">TC BATT CHARG (mA)</span></th>' +
        '<th class="derived-col" title="Charger OP V - Vf - Choke V">V<span class="alias-sub">TC VAR RES (V)</span></th>' +
        '<th class="derived-col" title="Choke V / If mA × 1000">R<span class="alias-sub">TC CH FEED END (Ω)</span></th>' +
        '<th class="derived-col" title="VTC VAR RES / If mA × 1000">R<span class="alias-sub">TC VAR RES (Ω)</span></th>' +
        '<th class="derived-col" title="Ir mA × TR Res (10Ω)">V<span class="alias-sub">TC TR (V)</span></th>' +
        '<th class="derived-col" title="If mA - Ir mA">I<span class="alias-sub">BALST (mA)</span></th>' +
        '<th class="derived-col" title="2×(Vf - VTC TR) / (If + Ir) × 1000">R<span class="alias-sub">RAIL (Ω)</span></th>';
}

function getDerivedCells(derived) {
    return '<td class="derived-val">' + formatDerivedValue(derived.itcBattCharg) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.vtcVarRes) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.rtcChFeedEnd) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.rtcVarRes) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.vtcTr) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.ibalst) + '</td>' +
        '<td class="derived-val">' + formatDerivedValue(derived.rrail) + '</td>';
}

// ================================================================
// USER ASSET INFO (ported from OLD working version)
// Provides per-site asset metadata used by graph alias resolution:
//   - userAssetSimpleMap[assetId+'_'+attrId]        for RDPMS / PointMachine indication
//   - userAssetDataloggerMap[assetId+'_'+role]      for DataLogger
// resolveUserAssetName() looks up a display name from these maps.
// shouldIncludeInGraph() / isPmOperationAttrId() filter PM operation events
// (which are discrete events, not plottable values) out of the graph view.
//
// Endpoint /FRS25/Telemetry/GetUserAssetInfo is optional -- if it 404s,
// the maps stay empty and graph code falls back to its existing alias dictionary.
// NEW's existing loadDataloggerAttributeMap (below) is kept untouched because it
// uses a different (verified-working) endpoint on this deployment.
// ================================================================
var userAssetSimpleMap = {};            // RDPMS + PointMachine indication metadata
var userAssetDataloggerMap = {};        // DataLogger metadata (richer than dlRoleNameMap)
var userAssetInfoLoaded = false;
var userAssetInfoSiteId = null;
var userAssetInfoAssetTypeId = null;

// PointMachine OPERATION attribute IDs (these are events, NOT plottable values)
var PM_OPERATION_IDS_EXCLUDE = [
    1001, 1002, 1003, 1004, 1005, 1006,
    2002,
    3001, 3002, 3003, 3004, 3005, 3006,
    4001, 4002, 4003, 4004, 4005, 4006
];

function isPmOperationAttrId(attrId) {
    var n = parseInt(attrId);
    if (isNaN(n)) return false;
    return PM_OPERATION_IDS_EXCLUDE.indexOf(n) > -1;
}

//function loadUserAssetInfo(siteId, callback) {
//    if (!siteId || siteId === '0') { if (callback) callback(); return; }

//    // Skip re-fetch if already loaded for this site
//    if (userAssetInfoLoaded && userAssetInfoSiteId === String(siteId)) {
//        if (callback) callback();
//        return;
//    }

//    // Site changed -- reset maps
//    userAssetSimpleMap = {};
//    userAssetDataloggerMap = {};
//    userAssetInfoLoaded = false;
//    userAssetInfoSiteId = String(siteId);

//    $.ajax({
//        url: '/FRS25/Telemetry/GetUserAssetInfoDatalogger?siteId=' + siteId,
//        type: 'GET',
//        dataType: 'json',
//        success: function (response) {
//            var data = Array.isArray(response) ? response :
//                (response && (response.Data || response.data || response.Result || []));

//            if (data && data.length) {
//                data.forEach(function (item) {
//                    var assetId = String(item.AssetId || '');
//                    var aaId = String(item.AssetAttributeId || '');
//                    var role = String(item.Role || '');
//                    var roleType = String(item.RoleType || '').toLowerCase();
//                    var attrName = item.AliasName || item.AttributeName;
//                    var assetName = item.AssetName || '';

//                    if (!assetId || !attrName) return;

//                    var entry = {
//                        name: attrName,
//                        attributeName: item.AttributeName || '',
//                        assetName: assetName,
//                        assetTypeId: item.AssetTypeId,
//                        siteId: item.SiteId,
//                        multiplication: item.Multiplication,
//                        absolute: item.Absolute,
//                        minValue: item.MinValue,
//                        maxValue: item.MaxValue,
//                        clusterId: item.ClusterId,
//                        channelId: item.ChannelId
//                    };

//                    if (roleType === 'c' && aaId) {
//                        // Simple attribute: RDPMS + PointMachine indication
//                        userAssetSimpleMap[assetId + '_' + aaId] = entry;
//                    } else if (roleType === 'd' && role) {
//                        // DataLogger -- enrich the existing role maps too
//                        userAssetDataloggerMap[assetId + '_' + role] = entry;
//                        if (!dlRoleNameMap[role]) dlRoleNameMap[role] = attrName;
//                        if (!dlAssetRoleMap[assetId + '_' + role]) dlAssetRoleMap[assetId + '_' + role] = attrName;
//                    }
//                });

//                console.log('[UserAssetInfo] Site ' + siteId + ' loaded -- ' +
//                    Object.keys(userAssetSimpleMap).length + ' simple (c), ' +
//                    Object.keys(userAssetDataloggerMap).length + ' datalogger (d)');
//            } else {
//                console.warn('[UserAssetInfo] Empty response for site ' + siteId);
//            }
//            userAssetInfoLoaded = true;
//            if (callback) callback();
//        },
//        error: function (xhr, status, err) {
//            // Endpoint may not exist on this deployment -- non-fatal.
//            console.warn('[UserAssetInfo] Not available for site ' + siteId + ' (' + status + '): graph will use fallback aliases');
//            userAssetInfoLoaded = true;
//            if (callback) callback();
//        }
//    });
//}


// ================================================================
// POINT MACHINE METADATA / BULK-SKIP COMPATIBILITY
// Ported from the old working telemetrylive.js.
// Point Machine must use GetAssestBy + GetPMAssetMeta and must not call
// GetBulkAssetMetadata / getbulkassetdata.
// ================================================================
function normalizeAssetTypeForBulkSkip(value) {
    if (value === undefined || value === null) return '';
    value = $.trim(String(value));
    if (value === '' || value === '0') return '';
    return value;
}

function isPointMachineAssetTypeForBulkSkip(assetTypeId) {
    assetTypeId = normalizeAssetTypeForBulkSkip(assetTypeId) ||
        normalizeAssetTypeForBulkSkip($('#drpAssetType').val()) ||
        normalizeAssetTypeForBulkSkip(wsCurrentAssetTypeId);

    return assetTypeId === '3';
}

function seedPointMachineAttributeFallbacks() {
    var pmFallbacks = {
        25: 'A End - NWKR',
        26: 'A End - RWKR',
        27: 'B End - NWKR',
        28: 'B End - RWKR',

        212: 'B End - NW-V',
        213: 'B End - NW-C',
        214: 'B End - RW-V',
        215: 'B End - RW-C',
        216: 'A End - NW-V',
        217: 'A End - NW-C',
        218: 'A End - RW-V',
        219: 'A End - RW-C',

        576: 'A End - NWKR (Loc)',
        577: 'A End - RWKR (Loc)',
        578: 'B End - NWKR (Loc)',
        579: 'B End - RWKR (Loc)'
    };

    if (typeof fallbackAliasById === 'undefined') return;

    for (var id in pmFallbacks) {
        if (!pmFallbacks.hasOwnProperty(id)) continue;

        fallbackAliasById[id] = fallbackAliasById[id] || pmFallbacks[id];
        fallbackAliasById[String(id)] = fallbackAliasById[String(id)] || pmFallbacks[id];

        if (typeof assetAttributeMap !== 'undefined') {
            assetAttributeMap[id] = assetAttributeMap[id] || pmFallbacks[id];
            assetAttributeMap[String(id)] = assetAttributeMap[String(id)] || pmFallbacks[id];
        }

        if (typeof assetAttributeByName !== 'undefined') {
            assetAttributeByName[pmFallbacks[id]] = pmFallbacks[id];
        }
    }

    assetAttributeLoaded = true;
}

function markUserAssetInfoLoadedForAssetType(siteId, assetTypeId) {
    userAssetInfoLoaded = true;
    userAssetInfoSiteId = String(siteId || '');
    userAssetInfoAssetTypeId = String(assetTypeId || '');
}

var pmAssetMetaCache = {};       // assetId → metadata object
var pmMetaLoaded = false;        // whether fetch has completed
var pmMetaLoadPromise = null;    // in-flight deferred promise
var PM_TWS_TYPE = { A: 1, B: 2, BOTH: 3 };

function resetPmAssetMeta() {
    pmAssetMetaCache = {};
    pmMetaLoaded = false;
    pmMetaLoadPromise = null;
}

function loadPmAssetMeta(siteId, assetTypeId) {
    if (!siteId || siteId === '0' || String(assetTypeId) !== '3') {
        return $.Deferred().resolve().promise();
    }

    if (pmMetaLoaded) return $.Deferred().resolve().promise();
    if (pmMetaLoadPromise) return pmMetaLoadPromise;

    var deferred = $.Deferred();
    pmMetaLoadPromise = deferred.promise();

    var payload = {
        SearchCriteria: {
            SiteId: parseInt(siteId),
            AssetTypeId: parseInt(assetTypeId),
            AssetTypeName: $('#drpAssetType option:selected').text().trim()
        },
        Pager: {
            Take: -1,
            Skip: 0,
            PageSize: -1,
            CurrentPage: 1,
            TotalRecord: 0,
            TotalPage: 0
        }
    };

    $.ajax({
        url: '/FRS25/Telemetry/GetPMAssetMeta',
        type: 'POST',
        data: JSON.stringify(payload),
        contentType: 'application/json',
        success: function (data) {
            if (data && data.error) {
                console.error('[PM-Meta] Server error:', data.error, data.inner);
                pmMetaLoaded = true;
                pmMetaLoadPromise = null;
                deferred.resolve();
                return;
            }

            if (data && data.length) {
                data.forEach(function (item) {
                    pmAssetMetaCache[String(item.Id)] = {
                        isThickWave: !!(item.IsThickWave),
                        thickWaveTypeId: (item.ThickWaveTypeId !== null && item.ThickWaveTypeId !== undefined)
                            ? parseInt(item.ThickWaveTypeId)
                            : null,
                        aliasA: item.AliasDirectionA || '',
                        aliasB: item.AliasDirectionB || '',
                        isHalf: !!(item.IsHalfPointMachine)
                    };
                });
                console.log('[PM-Meta] Loaded', Object.keys(pmAssetMetaCache).length, 'assets:', pmAssetMetaCache);
            } else {
                console.warn('[PM-Meta] Empty response — all IRS');
            }

            pmMetaLoaded = true;
            pmMetaLoadPromise = null;
            deferred.resolve();
        },
        error: function (xhr, status, err) {
            console.error('[PM-Meta] HTTP error:', status, err);
            console.error('[PM-Meta] Response:', xhr.responseText);
            pmMetaLoaded = true;
            pmMetaLoadPromise = null;
            deferred.resolve(); // still resolve so cards build
        }
    });

    return deferred.promise();
}

function getPmEndMeta(assetId, end, assetName) {
    var meta = pmAssetMetaCache[String(assetId)];

    var cleanName = (assetName || ('Asset ' + assetId))
        .replace(/^(PT-)+/i, '');

    var isTws = false;
    var alias = '';

    if (meta) {
        alias = (end === 'A') ? (meta.aliasA || '') : (meta.aliasB || '');

        if (meta.isThickWave) {
            var tid = meta.thickWaveTypeId;
            isTws = (end === 'A')
                ? (tid === PM_TWS_TYPE.A || tid === PM_TWS_TYPE.BOTH)
                : (tid === PM_TWS_TYPE.B || tid === PM_TWS_TYPE.BOTH);
        }
    }

    var suffix = isTws ? 'TWS' : 'IRS';
    var label = alias
        ? ('PT-' + alias + ' ' + suffix)
        : ('PT-' + cleanName + end + ' ' + suffix);

    return {
        label: label,
        isTws: isTws,
        dotColor: isTws ? '#00A4E0' : '#dc8b33',
        dotClass: isTws ? 'point-dot-two' : 'point-dot-one'
    };
}

function loadUserAssetInfo(siteId, callback) {
    if (!siteId || siteId === '0') {
        if (callback) callback();
        return;
    }

    var assetTypeId = $('#drpAssetType').val() || wsCurrentAssetTypeId || '0';

    if (!assetTypeId || assetTypeId === '0') {
        console.warn('[UserAssetInfo] No assetType available — maps will remain empty');
        markUserAssetInfoLoadedForAssetType(siteId, '0');
        if (callback) callback();
        return;
    }

    // Cache hit must include assetTypeId also.
    // Otherwise Point Machine skip can make Track/Signal think metadata is loaded.
    if (userAssetInfoLoaded &&
        userAssetInfoSiteId === String(siteId) &&
        userAssetInfoAssetTypeId === String(assetTypeId)) {
        if (callback) callback();
        return;
    }

    // IMPORTANT: Point Machine uses old working flow and must not call GetBulkAssetMetadata/getbulkassetdata.
    if (isPointMachineAssetTypeForBulkSkip(assetTypeId)) {
        seedPointMachineAttributeFallbacks();
        markUserAssetInfoLoadedForAssetType(siteId, assetTypeId);
        console.log('[UserAssetInfo] Skipped GetBulkAssetMetadata for Point Machine');
        if (callback) callback();
        return;
    }

    loadBulkAssetMetadata(siteId, assetTypeId, function () {
        markUserAssetInfoLoadedForAssetType(siteId, assetTypeId);
        console.log('[UserAssetInfo] Loaded via GetBulkAssetMetadata (site=' + siteId + ', type=' + assetTypeId + ') — ' +
            Object.keys(userAssetSimpleMap).length + ' simple (c), ' +
            Object.keys(userAssetDataloggerMap).length + ' datalogger (d)');
        if (callback) callback();
    });
}

var bulkMetadataLoaded = false;
var bulkMetadataSiteId = null;
var bulkMetadataAssetTypeId = null;
var bulkAssetsList = [];

// Bulk AliasName maps from GetBulkAssetMetadata.assetAttributes.
// Display labels must come from AliasName only, not raw AssetAttributeName.
var bulkAliasByAssetAttrName = {};
var bulkAliasByAssetAttrId = {};
var bulkAliasByAttrName = {};
var bulkAliasByAttrId = {};

function _tlNormAttrKey(v) {
    return (v === null || v === undefined) ? '' : String(v).trim().toUpperCase();
}

function registerBulkAliasName(assetId, rawAttrName, attrId, aliasName) {
    var aid = String(assetId || '');
    var rawKey = _tlNormAttrKey(rawAttrName);
    var idKey = String(attrId || '').trim();
    var alias = String(aliasName || '').trim();

    if (!alias) return;

    if (aid && rawKey) bulkAliasByAssetAttrName[aid + '_' + rawKey] = alias;
    if (aid && idKey) bulkAliasByAssetAttrId[aid + '_' + idKey] = alias;

    if (rawKey) bulkAliasByAttrName[rawKey] = alias;
    if (idKey) bulkAliasByAttrId[idKey] = alias;
}

function getBulkAliasName(assetId, attrName, attrId) {
    var aid = String(assetId || '');
    var rawKey = _tlNormAttrKey(attrName);
    var idKey = String(attrId || '').trim();

    // 1. Exact asset + raw attribute name from bulk metadata
    if (aid && rawKey && bulkAliasByAssetAttrName[aid + '_' + rawKey]) {
        return bulkAliasByAssetAttrName[aid + '_' + rawKey];
    }

    // 2. Exact asset + attribute id from bulk metadata
    // This is only used to fetch AliasName from bulk map, never to display id.
    if (aid && idKey && bulkAliasByAssetAttrId[aid + '_' + idKey]) {
        return bulkAliasByAssetAttrId[aid + '_' + idKey];
    }

    // 3. Global raw attribute name from bulk metadata
    if (rawKey && bulkAliasByAttrName[rawKey]) {
        return bulkAliasByAttrName[rawKey];
    }

    // 4. Global attr id from bulk metadata
    if (idKey && bulkAliasByAttrId[idKey]) {
        return bulkAliasByAttrId[idKey];
    }

    // 5. Scan bulk userAssetSimpleMap by asset + raw name
    if (typeof userAssetSimpleMap !== 'undefined' && userAssetSimpleMap) {
        for (var k in userAssetSimpleMap) {
            if (!userAssetSimpleMap.hasOwnProperty(k)) continue;
            if (aid && k.indexOf(aid + '_') !== 0) continue;

            var e = userAssetSimpleMap[k];
            if (!e || !e.name) continue;

            if (
                _tlNormAttrKey(e.attributeName) === rawKey ||
                _tlNormAttrKey(e.AttributeName) === rawKey ||
                _tlNormAttrKey(e.title) === rawKey ||
                _tlNormAttrKey(e.Title) === rawKey ||
                _tlNormAttrKey(e.name) === rawKey
            ) {
                return e.name; // AliasName from bulk
            }
        }
    }

    return '';
}

window.getBulkAliasName = getBulkAliasName;

var bulkAssetMap = {};
var bulkAssetAttrMap = {};
var bulkDataloggerMap = {};

function _tlMetaStr(v) {
    return (v === null || v === undefined) ? '' : String(v);
}

function _tlFirstNonEmpty() {
    for (var i = 0; i < arguments.length; i++) {
        var v = arguments[i];
        if (v !== null && v !== undefined && String(v).trim() !== '') return v;
    }
    return '';
}

function _tlEscHtml(v) {
    return _tlMetaStr(v)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function getBulkAssetMeta(assetId) {
    return bulkAssetMap[_tlMetaStr(assetId)] || null;
}

//function getBulkAssetName(assetId, fallbackName) {
//    var meta = getBulkAssetMeta(assetId);
//    var nm = meta ? _tlFirstNonEmpty(meta.Name, meta.AssetName) : '';
//    return nm || fallbackName || ('Asset ' + assetId);
//}

function getBulkAssetName(assetId, fallbackName) {
    var meta = getBulkAssetMeta(assetId);
    var nm = meta ? _tlFirstNonEmpty(meta.Name, meta.AssetName) : '';

    // If asset is in selected telemetry type, name should be from API metadata.
    var selectedType = $('#drpAssetType').val() || wsCurrentAssetTypeId;
    if (selectedType && selectedType !== '0') {
        // FIX: don't blow away the real WS-supplied name when metadata is missing
        // (Point Machine has no bulkAssetMap entry from GetBulkAssetMetadata).
        // Prefer metadata name, then the WS fallback name, only then "Asset <id>".
        return nm || fallbackName || ('Asset ' + assetId);
    }

    // SIP/background fallback.
    return nm || fallbackName || ('Asset ' + assetId);
}

function getBulkAssetTypeId(assetId, fallbackTypeId) {
    var meta = getBulkAssetMeta(assetId);
    return (meta && meta.AssetTypeId !== undefined && meta.AssetTypeId !== null)
        ? meta.AssetTypeId
        : fallbackTypeId;
}

function getBulkSiteId(assetId, fallbackSiteId) {
    var meta = getBulkAssetMeta(assetId);
    return (meta && meta.SiteId !== undefined && meta.SiteId !== null)
        ? meta.SiteId
        : fallbackSiteId;
}

function _tlStripAssetPrefix(assetId, assetName, rawName) {
    var displayName = _tlMetaStr(rawName).trim();
    var aName = getBulkAssetName(assetId, assetName || '').trim();

    if (aName && displayName.indexOf(aName) === 0) {
        displayName = displayName.substring(aName.length).trim();
        if (displayName.charAt(0) === '-' || displayName.charAt(0) === '_' || displayName.charAt(0) === ':') {
            displayName = displayName.substring(1).trim();
        }
    }

    return displayName || rawName || '';
}

function resolveBulkDataloggerName(assetId, roleOrAttrId, rawName) {
    var aid = _tlMetaStr(assetId);
    var role = _tlMetaStr(roleOrAttrId).trim();
    var raw = _tlMetaStr(rawName).trim();
    var key = aid + '_' + role;

    var entry = (role && (userAssetDataloggerMap[key] || bulkDataloggerMap[key])) || null;
    if (entry && entry.name) return entry.name;

    if (role && typeof dlAssetRoleMap !== 'undefined' && dlAssetRoleMap[key]) return dlAssetRoleMap[key];
    if (role && typeof dlRoleNameMap !== 'undefined' && dlRoleNameMap[role]) return dlRoleNameMap[role];

    for (var k in bulkDataloggerMap) {
        if (k.indexOf(aid + '_') !== 0) continue;

        var e = bulkDataloggerMap[k];
        if (!e) continue;

        if (raw && (
            e.attributeName === raw ||
            e.dataloggerAttribute === raw ||
            e.dataloggerAssetName === raw ||
            e.name === raw
        )) {
            return e.name || raw;
        }
    }

    return _tlStripAssetPrefix(assetId, null, raw);
}
function resolveBulkDataloggerRole(assetId, roleOrRawName, rawName) {
    var aid = _tlMetaStr(assetId);
    var input = _tlMetaStr(roleOrRawName).trim();
    var raw = _tlMetaStr(rawName).trim();

    if (!aid) return '';

    function norm(v) {
        return _tlMetaStr(v).trim().toUpperCase();
    }

    function same(a, b) {
        return norm(a) && norm(a) === norm(b);
    }

    var prefix = aid + '_';

    // Direct Role key
    if (input && bulkDataloggerMap[prefix + input]) {
        return bulkDataloggerMap[prefix + input].role || bulkDataloggerMap[prefix + input].Role || input;
    }

    if (input && userAssetDataloggerMap[prefix + input]) {
        return userAssetDataloggerMap[prefix + input].role || userAssetDataloggerMap[prefix + input].Role || input;
    }

    // Match text/name to Role
    for (var k in bulkDataloggerMap) {
        if (!bulkDataloggerMap.hasOwnProperty(k)) continue;
        if (k.indexOf(prefix) !== 0) continue;

        var e = bulkDataloggerMap[k];
        if (!e) continue;

        var role = e.role || e.Role || k.substring(prefix.length);

        if (
            same(input, role) ||
            same(raw, role) ||
            same(input, e.dataloggerAttribute) ||
            same(raw, e.dataloggerAttribute) ||
            same(input, e.DataloggerAttribute) ||
            same(raw, e.DataloggerAttribute) ||
            same(input, e.dataloggerAssetName) ||
            same(raw, e.dataloggerAssetName) ||
            same(input, e.DataloggerAssetName) ||
            same(raw, e.DataloggerAssetName) ||
            same(input, e.name) ||
            same(raw, e.name)
        ) {
            return role;
        }
    }

    return '';
}

window.resolveBulkDataloggerRole = resolveBulkDataloggerRole;
function populateAssetDropdownsFromBulk(options) {
    options = options || {};
    var d = bulkAssetsList || [];

    if ($('#drpAsset').length) {
        $('#drpAsset').empty().append('<option value="">All</option>');

        for (var i = 0; i < d.length; i++) {
            $('#drpAsset').append(
                '<option value="' + _tlEscHtml(d[i].Id) + '">' +
                _tlEscHtml(d[i].Name) +
                '</option>'
            );
        }
    }

    if ($('#listAssetNumber').length) {
        if (d.length > 0) {
            var h = '';

            for (var j = 0; j < d.length; j++) {
                var id = _tlEscHtml(d[j].Id);
                var name = _tlEscHtml(d[j].Name);

                h += '<div class="dropdown-item" data-value="' + id + '" data-text="' + name + '">' +
                    '<input type="checkbox" value="' + id + '">' +
                    '<span>' + name + '</span>' +
                    '</div>';
            }

            $('#listAssetNumber').html(h);

            if (options.autoLoad && typeof window._tlAutoLoad === 'function') {
                window._tlAutoLoad({ selectAll: true });
            }
        } else {
            $('#listAssetNumber').html('<div class="dropdown-empty">No assets found</div>');
        }
    }
}

function loadBulkAssetMetadata(siteId, assetTypeId, callback) {
    if (!siteId || siteId === '0' || !assetTypeId || assetTypeId === '0') {
        console.warn('[BulkMeta] Missing siteId or assetTypeId — skipping');
        if (callback) callback();
        return;
    }


    // Hard guard: Point Machine must never call /FRS25/Telemetry/GetBulkAssetMetadata.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip(assetTypeId)) {
        if (typeof seedPointMachineAttributeFallbacks === 'function') seedPointMachineAttributeFallbacks();
        if (typeof markUserAssetInfoLoadedForAssetType === 'function') markUserAssetInfoLoadedForAssetType(siteId, assetTypeId);
        console.log('[BulkMeta] Skipped GetBulkAssetMetadata for Point Machine');
        if (callback) callback();
        return;
    }

    if (bulkMetadataLoaded &&
        bulkMetadataSiteId === String(siteId) &&
        bulkMetadataAssetTypeId === String(assetTypeId)) {
        console.log('[BulkMeta] Cache hit — site=' + siteId + ' type=' + assetTypeId);
        if (callback) callback();
        return;
    }

    console.log('[BulkMeta] Fetching — site=' + siteId + ' type=' + assetTypeId);

    var payload = {
        SearchCriteria: {
            SiteId: parseInt(siteId),
            AssetTypeId: parseInt(assetTypeId)
        }
    };

    $.ajax({
        url: '/FRS25/Telemetry/GetBulkAssetMetadata',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(payload),
        dataType: 'json',
        success: function (response) {
            if (!response || !response.success || !response.mAssets) {
                console.warn('[BulkMeta] Error: ' + (response && response.errorMessage));
                _fallbackToLegacyLoad(siteId, assetTypeId, callback);
                return;
            }

            var assets = response.mAssets || [];
            var zeroCount = 0;
            var attrCount = 0;
            var dlCount = 0;
            var sigCacheCount = 0;

            bulkAssetsList = [];
            bulkAssetMap = {};
            bulkAssetAttrMap = {};
            bulkDataloggerMap = {};

            // Reset maps for new site data
            userAssetSimpleMap = {};
            userAssetDataloggerMap = {};

            bulkAliasByAssetAttrName = {};
            bulkAliasByAssetAttrId = {};
            bulkAliasByAttrName = {};
            bulkAliasByAttrId = {};
            assetAttributeMap = {};
            assetAttributeByName = {};
            wsAttributeIds = {};

            dlRoleNameMap = {};
            dlAssetRoleMap = {};

            if (typeof _sigAssetInfoCache !== 'undefined') {
                _sigAssetInfoCache = {};
            }

            for (var bi = 0; bi < assets.length; bi++) {
                var asset = assets[bi];
                if (!asset || !asset.Id) continue;

                var aid = String(asset.Id);
                var assetName = _tlFirstNonEmpty(asset.Name, asset.AssetName, 'Asset ' + aid);

                bulkAssetMap[aid] = asset;

                bulkAssetsList.push({
                    Id: asset.Id,
                    Name: assetName,
                    SiteId: asset.SiteId,
                    AssetTypeId: asset.AssetTypeId
                });

                var rawOffset = parseFloat(asset.ZeroOffsetValue);
                var offsetVal = (!isNaN(rawOffset) && rawOffset > 0)
                    ? rawOffset
                    : RDPMS_DEFAULT_THRESHOLD;

                zeroOffsetCache[aid] = {
                    value: offsetVal,
                    fetched: true,
                    fetching: false,
                    pendingCallbacks: []
                };

                if (wsLiveData[aid]) {
                    wsLiveData[aid].AssetName = assetName;
                    wsLiveData[aid].AssetTypeId = asset.AssetTypeId || wsLiveData[aid].AssetTypeId;
                    wsLiveData[aid].SiteId = asset.SiteId || wsLiveData[aid].SiteId;
                    wsLiveData[aid].ZeroOffsetValue = offsetVal;
                }

                if (offsetVal !== RDPMS_DEFAULT_THRESHOLD) zeroCount++;

                var attrs = asset.assetAttributes || asset.AssetAttributes || [];
                var sigAttrNames = [];

                for (var j = 0; j < attrs.length; j++) {
                    var attr = attrs[j];

                    var attrId = String(attr.Id || attr.AssetAttributeId || attr.AttributeId || '').trim();
                    var attrAlias = String(attr.AliasName || '').trim();
                    var rawAttrName = String(attr.Title || attr.AttributeName || attr.Name || attrAlias || '').trim();

                    // Display label must be AliasName from GetBulkAssetMetadata.
                    // If AliasName is empty, only then use raw name as a last fallback.
                    var displayAlias = attrAlias || rawAttrName;

                    if (!attrId || !displayAlias) continue;

                    // Register strict bulk alias mapping
                    registerBulkAliasName(aid, rawAttrName, attrId, displayAlias);
                    registerBulkAliasName(aid, displayAlias, attrId, displayAlias);

                    // Global attribute map now stores AliasName only
                    assetAttributeMap[attrId] = displayAlias;
                    assetAttributeMap[String(attrId)] = displayAlias;

                    var numAttrId = parseInt(attrId);
                    if (!isNaN(numAttrId)) assetAttributeMap[numAttrId] = displayAlias;

                    // Name map: raw name -> AliasName, alias -> alias
                    if (rawAttrName) assetAttributeByName[rawAttrName] = displayAlias;
                    assetAttributeByName[displayAlias] = displayAlias;

                    // userAssetSimpleMap: name must be AliasName, attributeName is raw key only for matching
                    var simpleKey = aid + '_' + attrId;
                    if (!userAssetSimpleMap[simpleKey]) {
                        userAssetSimpleMap[simpleKey] = {
                            name: displayAlias,              // AliasName only
                            aliasName: displayAlias,
                            AliasName: displayAlias,
                            attributeName: rawAttrName,      // raw name only for lookup/matching
                            AttributeName: rawAttrName,
                            assetName: asset.Name || '',
                            assetTypeId: asset.AssetTypeId,
                            siteId: asset.SiteId,
                            multiplication: attr.Multiplication,
                            absolute: attr.Absolute,
                            minValue: attr.MinValue,
                            maxValue: attr.MaxValue
                        };
                    }

                    attrCount++;

                    // Keep raw attribute key for value matching; UI will convert it to AliasName.
                    var sigName = rawAttrName || displayAlias;
                    if (sigName && sigAttrNames.indexOf(sigName) === -1) {
                        sigAttrNames.push(sigName);
                    }
                }

                var dls = asset.mAssetInfoDataloggers || asset.MAssetInfoDataloggers || [];

                for (var k = 0; k < dls.length; k++) {
                    var dl = dls[k];
                    if (!dl) continue;

                    // IMPORTANT:
                    // DataLogger identity must come from Role only.
                    // Do NOT use Id / DataloggerAttributeId as the DataLogger id.
                    var dlRole = $.trim(String(dl.Value || dl.value || ''));

                    if (!dlRole || dlRole === '0' || dlRole.toLowerCase() === 'null') {
                        console.warn('[BulkMeta-DL] Skipped DL because Role is missing', {
                            assetId: aid,
                            assetName: assetName,
                            dl: dl
                        });
                        continue;
                    }

                    // Display name must be DataloggerAttribute, e.g. TPR.
                    var dlName = $.trim(String(dl.DataloggerAttribute || dl.dataloggerAttribute || ''));
                    if (!dlName) dlName = $.trim(String(dl.DataloggerAssetName || dl.dataloggerAssetName || ''));
                    if (!dlName) dlName = $.trim(String(dl.AttributeName || dl.Name || 'DL ' + dlRole));
                    if (!dlName) continue;

                    var dlEntry = {
                        // Canonical DataLogger id
                        role: dlRole,
                        Role: dlRole,
                        dataloggerRole: dlRole,

                        // Display fields
                        name: dlName,
                        Name: dlName,
                        attributeName: dl.DataloggerAttribute || dlName,
                        AttributeName: dl.DataloggerAttribute || dlName,
                        dataloggerAttribute: dl.DataloggerAttribute || '',
                        DataloggerAttribute: dl.DataloggerAttribute || '',
                        dataloggerAssetName: dl.DataloggerAssetName || '',
                        DataloggerAssetName: dl.DataloggerAssetName || '',

                        // Keep these only as metadata, not as lookup key
                        dataloggerAttributeId: dl.Value || null,
                        DataloggerAttributeId: dl.Value || null,
                        dataloggerValueId: dl.Value || null,
                        DataloggerValueId: dl.Value || null,
                        sourceId: dl.Id || null,
                        SourceId: dl.Id || null,

                        contactType: dl.ContactType || '',
                        assetName: assetName,
                        AssetName: assetName,
                        assetTypeId: asset.AssetTypeId,
                        siteId: asset.SiteId
                    };

                    var dlKey = aid + '_' + dlRole;

                    userAssetDataloggerMap[dlKey] = dlEntry;
                    bulkDataloggerMap[dlKey] = dlEntry;

                    // Role-based lookup only
                    dlRoleNameMap[dlRole] = dlName;
                    dlAssetRoleMap[dlKey] = dlName;

                    dlCount++;
                }



                //var dls = asset.mAssetInfoDataloggers || asset.MAssetInfoDataloggers || [];

                //for (var k = 0; k < dls.length; k++) {
                //    var dl = dls[k];
                //    if (!dl) continue;

                //    var dlRole = String(_tlFirstNonEmpty(
                //        dl.DataloggerAttributeId,
                //        dl.Role,
                //        dl.AssetAttributeId,
                //        dl.Id,
                //        dl.SrNo,
                //        dl.DataloggerAttribute
                //    ));

                //    var dlName = _tlFirstNonEmpty(
                //        dl.DataloggerAttribute,
                //        dl.DataloggerAssetName,
                //        dl.AttributeName,
                //        dl.Name,
                //        dlRole
                //    );

                //    if (!dlRole || !dlName) continue;

                //    var dlEntry = {
                //        name: dlName,
                //        attributeName: dlName,
                //        dataloggerAttribute: dl.DataloggerAttribute || '',
                //        dataloggerAssetName: dl.DataloggerAssetName || '',
                //        assetName: assetName,
                //        assetTypeId: asset.AssetTypeId,
                //        siteId: asset.SiteId
                //    };

                //    var dlKey = aid + '_' + dlRole;

                //    userAssetDataloggerMap[dlKey] = dlEntry;
                //    bulkDataloggerMap[dlKey] = dlEntry;
                //    dlRoleNameMap[dlRole] = dlName;
                //    dlAssetRoleMap[dlKey] = dlName;

                //    if (dl.DataloggerAttribute) {
                //        bulkDataloggerMap[aid + '_' + dl.DataloggerAttribute] = dlEntry;
                //        userAssetDataloggerMap[aid + '_' + dl.DataloggerAttribute] = dlEntry;
                //        dlAssetRoleMap[aid + '_' + dl.DataloggerAttribute] = dlName;
                //    }

                //    if (dl.DataloggerAssetName) {
                //        bulkDataloggerMap[aid + '_' + dl.DataloggerAssetName] = dlEntry;
                //        userAssetDataloggerMap[aid + '_' + dl.DataloggerAssetName] = dlEntry;
                //        dlAssetRoleMap[aid + '_' + dl.DataloggerAssetName] = dlName;
                //    }

                //    dlCount++;
                //}

                if (typeof _sigAssetInfoCache !== 'undefined' && sigAttrNames.length > 0) {
                    _sigAssetInfoCache[aid] = {
                        loaded: true,
                        loading: false,
                        attrs: sigAttrNames,
                        pending: []
                    };

                    sigCacheCount++;
                }
            }

            bulkAssetsList.sort(function (a, b) {
                return (a.Name || '').localeCompare(b.Name || '', undefined, {
                    numeric: true,
                    sensitivity: 'base'
                });
            });

            // Build allowed asset whitelist from API response.
            // WebSocket data is accepted only for these AssetIds.
            rebuildWsValidAssetIdsFromBulk();

            // Remove any WS-only/scrap assets that entered before bulk metadata completed.
            Object.keys(wsLiveData || {}).forEach(function (id) {
                if (wsValidAssetIds.indexOf(String(id)) === -1) {
                    console.warn('[BulkMeta] Removing WS-only asset not present in API response:', {
                        assetId: id,
                        assetName: wsLiveData[id] && wsLiveData[id].AssetName
                    });
                    delete wsLiveData[id];
                    delete wsUpdatedAssets[id];
                    $('#rdpmsCard_' + id).remove();
                    $('.sig-group-wrap tr[data-id="' + id + '"]').remove();
                    $('#wsLiveTable tbody tr[data-id="' + id + '"]').remove();
                }
            });

            window.bulkAssetsList = bulkAssetsList;
            window.bulkAssetMap = bulkAssetMap;
            window.bulkAssetAttrMap = bulkAssetAttrMap;
            window.bulkDataloggerMap = bulkDataloggerMap;
            window.wsValidAssetIds = wsValidAssetIds;

            assetAttributeLoaded = true;
            userAssetInfoLoaded = true;
            userAssetInfoSiteId = String(siteId);
            userAssetInfoAssetTypeId = String(assetTypeId);

            bulkMetadataLoaded = true;
            bulkMetadataSiteId = String(siteId);
            bulkMetadataAssetTypeId = String(assetTypeId);

            console.log('[BulkMeta] ✓ ' + bulkAssetsList.length + ' assets | ' +
                zeroCount + ' zero offsets | ' +
                attrCount + ' attributes | ' +
                dlCount + ' datalogger | ' +
                sigCacheCount + ' signal groups — 1 API call');

            if (callback) callback();
        },
        error: function (xhr, status, err) {
            console.error('[BulkMeta] AJAX error (' + status + '): ' + err);
            _fallbackToLegacyLoad(siteId, assetTypeId, callback);
        }
    });
}

function _fallbackToLegacyLoad(siteId, assetTypeId, callback) {
    console.warn('[BulkMeta] Bulk metadata unavailable; no legacy metadata API fallback will be called.');

    assetAttributeLoaded = true;
    userAssetInfoLoaded = true;
    userAssetInfoSiteId = String(siteId || '');
    userAssetInfoAssetTypeId = String(assetTypeId || '0');

    if (callback) callback();
}

function invalidateBulkMetadataCache() {
    bulkMetadataLoaded = false;
    bulkMetadataSiteId = null;
    bulkMetadataAssetTypeId = null;
}

window.loadBulkAssetMetadata = loadBulkAssetMetadata;
window.invalidateBulkMetadataCache = invalidateBulkMetadataCache;
window.getBulkAssetMeta = getBulkAssetMeta;
window.getBulkAssetName = getBulkAssetName;
window.resolveBulkDataloggerName = resolveBulkDataloggerName;

function resolveDataloggerDisplayName(assetId, attrNameOrId, rawD) {
    debugger;
    var aid = String(assetId || '');

    function clean(v) {
        if (v === undefined || v === null) return '';
        return $.trim(String(v));
    }

    function fromMap(id) {
        id = clean(id);
        if (!id || id === '0') return '';

        var key = aid + '_' + id;

        if (typeof userAssetDataloggerMap !== 'undefined') {
            var entry = userAssetDataloggerMap[key];
            if (entry && entry.name) return entry.name;
        }

        if (typeof bulkDataloggerMap !== 'undefined') {
            var bulkEntry = bulkDataloggerMap[key];
            if (bulkEntry && bulkEntry.name) return bulkEntry.name;
        }

        if (typeof dlAssetRoleMap !== 'undefined' && dlAssetRoleMap[key]) {
            return dlAssetRoleMap[key];
        }

        if (typeof dlRoleNameMap !== 'undefined' && dlRoleNameMap[id]) {
            return dlRoleNameMap[id];
        }

        return '';
    }

    if (rawD && clean(rawD.DataloggerAttribute)) {
        return clean(rawD.DataloggerAttribute);
    }

    var candidates = [];

    if (rawD) {
        // Role is the only canonical DataLogger id.
        candidates.push(rawD.Role);
        candidates.push(rawD.role);
        candidates.push(rawD.dataloggerRole);
        candidates.push(rawD.DataloggerRole);

        // Text fallbacks only. Do not use Id / DataloggerAttributeId as DL id.
        candidates.push(rawD.DataloggerAttribute);
        candidates.push(rawD.dataloggerAttribute);
        candidates.push(rawD.DataloggerAssetName);
        candidates.push(rawD.dataloggerAssetName);
    }

    candidates.push(attrNameOrId);

    if (typeof resolveBulkDataloggerRole === 'function') {
        var resolvedRole = resolveBulkDataloggerRole(assetId, attrNameOrId, rawText);
        if (resolvedRole) candidates.unshift(resolvedRole);
    }

    var rawText = clean(attrNameOrId);
    var nums = rawText.match(/\d+/g);
    if (nums && nums.length) {
        for (var ni = 0; ni < nums.length; ni++) {
            candidates.push(nums[ni]);
        }
    }

    for (var i = 0; i < candidates.length; i++) {
        var found = fromMap(candidates[i]);
        if (found) return found;
    }

    // Match by DataloggerAssetName or DataloggerAttribute if live/history sends text like 4_AXCPR
    if (typeof userAssetDataloggerMap !== 'undefined' && rawText) {
        var prefix = aid + '_';
        var rawLc = rawText.toLowerCase();

        for (var dk in userAssetDataloggerMap) {
            if (dk.indexOf(prefix) !== 0) continue;

            var e = userAssetDataloggerMap[dk];
            if (!e || !e.name) continue;

            var dlAssetName = clean(e.dataloggerAssetName).toLowerCase();
            var dlAttrName = clean(e.dataloggerAttribute || e.attributeName).toLowerCase();

            if (dlAssetName && dlAssetName === rawLc) return e.name;
            if (dlAttrName && dlAttrName === rawLc) return e.name;
        }
    }

    if (typeof resolveBulkDataloggerName === 'function') {
        var bulkName = resolveBulkDataloggerName(assetId, attrNameOrId, rawText);
        if (bulkName && !/^attr\s+\d+$/i.test(bulkName)) return bulkName;
    }

    if (rawText && !/^attr\s+\d+$/i.test(rawText)) return rawText;

    return rawText || '';
}

window.resolveDataloggerDisplayName = resolveDataloggerDisplayName;






// Resolve {name, assetName, ...} from the maps populated by loadUserAssetInfo.
//   DataType = "DataLogger"            -> datalogger map  (assetId + role)
//   DataType = "RDPMS" / "PointMachine"-> simple map      (assetId + assetAttributeId)
// Returns the entry, or null if not found (callers fall back to dictionary aliases).
function resolveUserAssetName(assetId, attributeId, dataType) {
    var key = String(assetId) + '_' + String(attributeId);
    var dt = String(dataType || '').toLowerCase();
    if (dt === 'datalogger') {
        return userAssetDataloggerMap[key] || null;
    }
    return userAssetSimpleMap[key] || null;
}

// Decide whether a history/live data point should be INCLUDED in the graph.
// Excludes PointMachine OPERATION data (operation IDs are events, not plottable).
function shouldIncludeInGraph(attributeId, dataType) {
    var dt = String(dataType || '').toLowerCase();
    if (dt === 'pointmachine' && isPmOperationAttrId(attributeId)) {
        return false;
    }
    return true;
}

// DataLogger role→name map: { '238': 'TPR', '227': 'RECR', ... }
var dlRoleNameMap = {};       // key = Role (string), value = DataloggerAttribute name
var dlAssetRoleMap = {};      // key = assetId+'_'+role, value = DataloggerAttribute name

//function loadDataloggerAttributeMap(siteId, callback) {
//    loadUserAssetInfo(siteId, callback);
//}


function loadDataloggerAttributeMap(siteId, callback) {
    var assetTypeId = $('#drpAssetType').val() || wsCurrentAssetTypeId;

    if (siteId && siteId !== '0' && assetTypeId && assetTypeId !== '0') {
        loadBulkAssetMetadata(siteId, assetTypeId, callback);
        return;
    }

    if (callback) callback();
}



// Load attributes on page init via bulk method (if site + assetType already selected)
$(document).ready(function () {
    var initSiteId = $('#drpSite').val();
    var initAssetTypeId = $('#drpAssetType').val();
    if (initSiteId && initSiteId !== '0' && initAssetTypeId && initAssetTypeId !== '0') {
        loadBulkAssetMetadata(initSiteId, initAssetTypeId, null);
    }
    // Full-screen toggle for live asset data
    (function () {
        let isAssetFullscreen = false;
        const $assetCard = $('.at-table-card');
        const $fsBtn = $('#atFullscreenBtn');
        const $fsIcon = $fsBtn.find('i');

        function toggleAssetFullscreen() {
            isAssetFullscreen = !isAssetFullscreen;
            if (isAssetFullscreen) {
                $assetCard.addClass('fullscreen-assets');
                $fsIcon.removeClass('fa-expand').addClass('fa-compress');
                $('body').css('overflow', 'hidden');
            } else {
                $assetCard.removeClass('fullscreen-assets');
                $fsIcon.removeClass('fa-compress').addClass('fa-expand');
                $('body').css('overflow', '');
            }
            // Resize ECharts and Chart.js instances after layout change
            setTimeout(() => {
                if (typeof mainGraphChart !== 'undefined' && mainGraphChart) mainGraphChart.resize();
                if (typeof rdpmsGraphChart !== 'undefined' && rdpmsGraphChart) rdpmsGraphChart.resize();
                if (window._singleArrChartInst) window._singleArrChartInst.resize();
                $(window).trigger('resize');
            }, 200);
        }

        $fsBtn.on('click', toggleAssetFullscreen);

        // ESC key exits full-screen
        $(document).on('keydown', function (e) {
            if (e.key === 'Escape' && isAssetFullscreen) {
                toggleAssetFullscreen();
            }
        });
    })();
});

// Reload DL map when site changes -- AND auto-connect a site-wide WS so SIP shows live data immediately
// ── SIP eager-setup is deferred so the asset-type dropdown can
//    populate first.  In the old telemetry there was no SIP layer
//    at all, so the dropdown felt instant. In this build we have
//    three extra calls on every site change:
//      • loadDataloggerAttributeMap(sid)   — DL role map AJAX
//      • connectWebSocket(sid, null, [])   — subscribes to ALL site
//                                            assets so SIP can show
//                                            live colours BEFORE the
//                                            user filters anything
//      • SipTelemetry.connectToSite(sid)   — loads SIP layout
//    The WS auto-subscribe is the biggest offender: the server
//    starts streaming live frames for every asset at the site,
//    which saturates the connection and pushes back the small
//    GetAssetTypeBySiteId response that populates the dropdown.
//
//    Solution: defer everything by ~400ms with a cancel-on-rapid-
//    change guard. If the user is clicking through sites quickly,
//    we don't bother kicking off SIP for sites they're skimming
//    past — only the one they actually settle on.
var _sipDeferTimer = null;
$('#drpSite').on('change', function () {
    invalidateBulkMetadataCache();
    var sid = $(this).val();
    dlRoleNameMap = {}; dlAssetRoleMap = {};

    // Update SIP card header label with the selected site name
    var siteText = $('#drpSite option:selected').text() || '';
    var $sipName = $('#sipSiteName');
    if ($sipName.length) {
        $sipName.text(sid && sid !== '0' ? siteText : 'Select a site');
    }

    // Cancel any pending SIP work from a previous site choice
    if (_sipDeferTimer) {
        clearTimeout(_sipDeferTimer);
        _sipDeferTimer = null;
    }

    if (sid && sid !== '0') {
        // Defer the SIP eager-setup so the asset-type dropdown wins
        // the network race. After 400ms (a) the small AJAX response
        // has almost certainly arrived, and (b) the user has clearly
        // settled on this site rather than tabbing past it.
        _sipDeferTimer = setTimeout(function () {
            _sipDeferTimer = null;
            try { loadDataloggerAttributeMap(sid); }
            catch (e) { console.warn('[SIP-defer] loadDataloggerAttributeMap failed', e); }

            if (typeof isWebSocketSite === 'function' && isWebSocketSite(sid)) {
                try { connectWebSocket(sid, null, []); }
                catch (e) { console.warn('[SIP-defer] auto-connect failed', e); }
            }
            if (window.SipTelemetry && typeof window.SipTelemetry.connectToSite === 'function') {
                try { window.SipTelemetry.connectToSite(sid); }
                catch (e) { console.warn('[SIP-defer] connectToSite failed', e); }
            }
        }, 400);
    } else {
        // No site → tear down SIP cleanly (immediate, not deferred)
        if (window.SipTelemetry && typeof window.SipTelemetry.disconnect === 'function') {
            try { window.SipTelemetry.disconnect(); } catch (e) { /* ignore */ }
        }
    }
});



// ===== WEBSOCKET CONNECT =====

function disconnectWebSocket() {
    if (wsReconnectTimer) { clearTimeout(wsReconnectTimer); wsReconnectTimer = null; }
    if (wsRenderTimer) { clearTimeout(wsRenderTimer); wsRenderTimer = null; }
    if (wsSafetyInterval) { clearInterval(wsSafetyInterval); wsSafetyInterval = null; }
    if (wsHeartbeatInterval) { clearInterval(wsHeartbeatInterval); wsHeartbeatInterval = null; }
    stopStalePoll();
    stopAutoStaleCheck();
    // Clear message queue and all timers
    if (wsQueueProcessTimer) { clearTimeout(wsQueueProcessTimer); wsQueueProcessTimer = null; }
    if (wsUIUpdateTimer) { clearTimeout(wsUIUpdateTimer); wsUIUpdateTimer = null; }
    // Clear MQTT-bridge polling interval (used when MQTT is active for circuit view)
    if (window._circuitMqttBridgeInterval) { clearInterval(window._circuitMqttBridgeInterval); window._circuitMqttBridgeInterval = null; }
    wsMessageQueue = [];
    wsIsProcessingQueue = false;
    wsPendingUIUpdate = false;
    wsStreamingActive = false;
    wsViewSwitchPending = false;
    wsRAFScheduled = false;
    if (wsConnection) { wsConnection.onclose = null; wsConnection.onerror = null; wsConnection.close(1000); wsConnection = null; wsIsConnected = false; stopStalePoll(); }
    $('#wsStatusBar').hide().empty();
}

function showWsStatus(status) {
    var $bar = $('#wsStatusBar'), html = '';
    if (status === 'connected') {
        html = '<div class="at-ws-badge ws-live">';
        html += '<span class="at-ws-dot"></span>';
        html += '<strong>Live</strong>&nbsp;-&nbsp;WebSocket Streaming';
        html += '<div class="at-ws-stats">';
        html += '<span>Msg/s&nbsp;<strong id="wsMPS">0</strong></span>';
        html += '<span>Queue&nbsp;<strong id="wsQ">0</strong></span>';
        html += '<span>Assets&nbsp;<strong id="wsSA">0</strong></span>';
        html += '</div></div>';
    } else if (status === 'connecting') {
        html = '<div class="at-ws-badge ws-connecting">';
        html += '<span class="at-ws-dot"></span>';
        html += '<strong>Connecting…</strong></div>';
    } else if (status === 'reconnecting') {
        html = '<div class="at-ws-badge ws-connecting">';
        html += '<span class="at-ws-dot"></span>';
        html += '<strong>Reconnecting</strong> (attempt&nbsp;' + wsReconnectAttempts + ')…</div>';
    } else {
        html = '<div class="at-ws-badge ws-error">';
        html += '<span class="at-ws-dot"></span>';
        html += '<strong>Disconnected</strong>&nbsp;-&nbsp;Click Search to reconnect</div>';
    }
    $bar.html(html).show();
}
function updateWsStats() {
    var $mps = $('#wsMPS'), $q = $('#wsQ'), $a = $('#wsSA');
    if ($mps.length) { $mps.text(wsProcessedPerSecond || 0); }
    if ($q.length) { $q.text(wsMessageQueue.length); }
    if ($a.length) { $a.text(Object.keys(wsLiveData).length); }
    // AT: KPI strip
    var _ac = Object.keys(wsLiveData).length;
    $('#atKpiAssets').text(_ac);
    $('#atKpiMps').text(typeof wsProcessedPerSecond !== 'undefined' ? wsProcessedPerSecond : 0);
    $('#atKpiQueue').text(typeof wsMessageQueue !== 'undefined' ? wsMessageQueue.length : 0);
    $('#atKpiBatches').text(typeof wsBatchCount !== 'undefined' ? wsBatchCount : 0);
    if (_ac > 0) {
        var $kpi = $('#atKpiRow'); if ($kpi.length && $kpi.css('display') === 'none') $kpi.show();
        var $rc = $('#atRowCount'); if ($rc.length) $rc.text(_ac + ' asset' + (_ac !== 1 ? 's' : '')).show();
    }
}

// ===== DIRECT LIVE UPDATE =====
// Now uses the queue system for consistency with batch updates
function processSingleLiveUpdate(d) {
    if (!d || !d.AssetId || !d.AssetAttributeName) return;

    // Add to queue as a single-item array
    queueWsMessages([d]);
}

// ===== CELL-LEVEL TABLE UPDATE =====

function updateTrackComputedCols(assetId, $row) {
    var asset = wsLiveData[assetId]; if (!asset) return;

    // Update DataLogger cell
    var $dlCell = $row.find('td.dl-cell');
    if ($dlCell.length) {
        var dlRelays = asset.dlRelays || {};
        var dlHtml = '';
        var dlKeys = Object.keys(dlRelays);
        if (dlKeys.length > 0) {
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        $dlCell.html(dlHtml);
    }
}

window.parseBatchMessages = parseBatchMessages;
function parseBatchMessages(messages) {
    if (!messages || !Array.isArray(messages)) {
        console.warn('[WS Parse] Invalid messages array:', messages);
        return;
    }

    var items = [];

    for (var i = 0; i < messages.length; i++) {
        var item = messages[i];

        // Handle string items (JSON encoded)
        if (typeof item === 'string') {
            try {
                item = JSON.parse(item);
            } catch (e) {
                console.warn('[WS Parse] Failed to parse string item:', item.substring(0, 100));
                continue;
            }

            // Double-encoded string
            if (typeof item === 'string') {
                try {
                    item = JSON.parse(item);
                } catch (e2) {
                    continue;
                }
            }
        }

        // Valid message with AssetId and AssetAttributeName
        if (item && item.AssetId !== undefined && item.AssetAttributeName) {
            items.push(item);
        }
        // Nested Messages array
        else if (item && Array.isArray(item.Messages)) {
            for (var j = 0; j < item.Messages.length; j++) {
                var sub = item.Messages[j];
                if (typeof sub === 'string') {
                    try { sub = JSON.parse(sub); } catch (e3) { continue; }
                    if (typeof sub === 'string') {
                        try { sub = JSON.parse(sub); } catch (e4) { continue; }
                    }
                }
                if (sub && sub.AssetId !== undefined && sub.AssetAttributeName) {
                    items.push(sub);
                }
            }
        }
        // Nested Data array
        else if (item && Array.isArray(item.Data)) {
            for (var k = 0; k < item.Data.length; k++) {
                var dataItem = item.Data[k];
                if (typeof dataItem === 'string') {
                    try { dataItem = JSON.parse(dataItem); } catch (e5) { continue; }
                }
                if (dataItem && dataItem.AssetId !== undefined && dataItem.AssetAttributeName) {
                    items.push(dataItem);
                }
            }
        }
    }

    console.log('[WS Parse] Extracted', items.length, 'valid items from', messages.length, 'messages');

    if (items.length > 0) {
        // Use the new queue system for processing
        queueWsMessages(items);
    }
}

// ===== PM TABLE VIEW -- Dedicated renderer for Point Machine in Table mode =====
function renderPmTableView() {
    // FIX: the legacy body below builds rows via buildPmTableRow(), which reads
    // pm.Operations[] — a shape getPmStructuredData() NEVER produces. The result
    // was a table with correct headers but every A/B data cell stuck on "--"
    // ("table view data not binding"). renderPointMachineTableView() reads the
    // real pm[Normal|Reverse].AC/AV/BC/BV buckets and is what the cell-level
    // updater (updatePointMachineTableCell) already targets, so delegate to it
    // to keep full-render and incremental-update paths consistent.
    if (typeof renderPointMachineTableView === 'function') {
        return renderPointMachineTableView();
    }

    var assetIds = getSortedAssetIds();
    if (assetIds.length === 0) return;
    $('#wsWaiting').remove();

    var h = '<div style="overflow-x:auto;">';
    h += '<table id="wsLiveTable" style="width:100%;border-collapse:collapse;font-size:12px;">';
    h += '<thead>';
    h += '<tr>';
    h += '<th style="background:rgba(34,211,238,0.15);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:120px;">POINT MACHINE</th>';
    h += '<th style="background:rgba(34,211,238,0.15);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:90px;">DIRECTION</th>';
    h += '<th style="background:rgba(34,211,238,0.10);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:140px;">A CURRENT<br><span style="font-size:9.5px;opacity:.7;">(MAX/AVG)</span></th>';
    h += '<th style="background:rgba(34,211,238,0.10);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">A VOLTAGE<br><span style="font-size:9.5px;opacity:.7;">(AVG)</span></th>';
    h += '<th style="background:rgba(34,211,238,0.10);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">A OP TIME<br><span style="font-size:9.5px;opacity:.7;">(MS)</span></th>';
    h += '<th style="background:rgba(34,211,238,0.10);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:80px;">A COUNT</th>';
    h += '<th style="background:rgba(34,211,238,0.10);color:#22d3ee;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">A OP DATE</th>';
    h += '<th style="background:rgba(167,139,250,0.10);color:#a78bfa;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:140px;">B CURRENT<br><span style="font-size:9.5px;opacity:.7;">(MAX/AVG)</span></th>';
    h += '<th style="background:rgba(167,139,250,0.10);color:#a78bfa;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">B VOLTAGE<br><span style="font-size:9.5px;opacity:.7;">(AVG)</span></th>';
    h += '<th style="background:rgba(167,139,250,0.10);color:#a78bfa;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">B OP TIME<br><span style="font-size:9.5px;opacity:.7;">(MS)</span></th>';
    h += '<th style="background:rgba(167,139,250,0.10);color:#a78bfa;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:80px;">B COUNT</th>';
    h += '<th style="background:rgba(167,139,250,0.10);color:#a78bfa;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:100px;">B OP DATE</th>';
    h += '<th style="background:rgba(251,191,36,0.10);color:#fbbf24;padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:160px;">DATALOGGER</th>';
    h += '<th style="background:rgba(255,255,255,0.04);color:rgba(255,255,255,0.55);padding:10px 12px;border:1px solid rgba(255,255,255,0.08);white-space:nowrap;font-weight:700;font-size:11px;text-transform:uppercase;letter-spacing:0.05em;min-width:90px;">LAST UPDATE</th>';
    h += '</tr></thead><tbody>';

    for (var ri = 0; ri < assetIds.length; ri++) {
        h += buildPmTableRow(assetIds[ri]);
    }

    h += '</tbody></table></div>';
    $('#divTelemetryLive').html(h);
    $('#downloadContainer').show();
}

function buildPmTableRow(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return '';

    // Name with PT- prefix
    var name = asset.AssetName || ('Asset ' + assetId);
    if (name.indexOf('PT-') !== 0) name = 'PT-' + name;

    // Direction from RDPMS data
    var direction = 'Normal';
    var dirCls = 'background:linear-gradient(135deg,#0d9488,#0f766e);color:#fff;';
    var pm = (typeof getPmStructuredData === 'function') ? getPmStructuredData(assetId) : null;
    if (pm) {
        var nwkrMax = 0, rwkrMax = 0;
        for (var rk in pm.RDPMS) {
            if (!pm.RDPMS.hasOwnProperty(rk)) continue;
            var v = parseFloat(pm.RDPMS[rk].value || 0);
            var rkl = rk.toLowerCase();
            if (rkl.indexOf('nwkr') > -1) nwkrMax = Math.max(nwkrMax, v);
            else if (rkl.indexOf('rwkr') > -1) rwkrMax = Math.max(rwkrMax, v);
        }
        if (rwkrMax > nwkrMax && rwkrMax > 10) {
            direction = 'Reverse';
            dirCls = 'background:linear-gradient(135deg,#e85d04,#c2410c);color:#fff;';
        }
    }
    var dirBadge = '<span style="display:inline-block;padding:3px 10px;border-radius:5px;font-size:11px;font-weight:700;letter-spacing:0.03em;' + dirCls + '">' + direction + '</span>';

    // A/B end data from pm structured data
    function fmtA(v) { return (v !== null && v !== undefined && !isNaN(parseFloat(v))) ? parseFloat(v).toFixed(2) : '--'; }
    var aMaxC = '--', aAvgC = '--', aVoltAvg = '--', aTime = '--', aCount = '--', aDate = '--';
    var bMaxC = '--', bAvgC = '--', bVoltAvg = '--', bTime = '--', bCount = '--', bDate = '--';

    if (pm) {
        var ops = pm.Operations || [];
        // Get most recent Normal or Reverse operation for A and B ends
        var latestA = null, latestB = null;
        for (var oi = 0; oi < ops.length; oi++) {
            var op = ops[oi];
            if (!op) continue;
            if (op.aAcMax !== undefined || op.aAcAvg !== undefined) {
                if (!latestA) latestA = op;
            }
            if (op.bBcMax !== undefined || op.bBcAvg !== undefined) {
                if (!latestB) latestB = op;
            }
        }
        if (latestA) {
            aMaxC = fmtA(latestA.aAcMax); aAvgC = fmtA(latestA.aAcAvg);
            aVoltAvg = fmtA(latestA.aAvAvg); aTime = latestA.aAcCount ? latestA.aAcCount + '' : '--';
            aCount = latestA.aAcCount ? latestA.aAcCount + '' : '--';
            aDate = latestA.Timestamp ? latestA.Timestamp.toString().substring(0, 16) : '--';
        }
        if (latestB) {
            bMaxC = fmtA(latestB.bBcMax); bAvgC = fmtA(latestB.bBcAvg);
            bVoltAvg = fmtA(latestB.bBvAvg); bTime = latestB.bBcCount ? latestB.bBcCount + '' : '--';
            bCount = latestB.bBcCount ? latestB.bBcCount + '' : '--';
            bDate = latestB.Timestamp ? latestB.Timestamp.toString().substring(0, 16) : '--';
        }
    }

    // DataLogger badges from dlRelays
    var dlHtml = '';
    var dlRelays = asset.dlRelays || {};
    var dlKeys = Object.keys(dlRelays);
    if (dlKeys.length === 0) {
        dlHtml = '<span style="color:rgba(255,255,255,0.25);font-size:10px;">--</span>';
    } else {
        for (var di = 0; di < Math.min(dlKeys.length, 3); di++) {
            var rl = dlRelays[dlKeys[di]];
            var dn = rl.displayName || dlKeys[di];
            var bc = rl.isPickup ? 'linear-gradient(135deg,#10b981,#059669)' : 'linear-gradient(135deg,#f59e0b,#d97706)';
            var bs = rl.isPickup ? 'Pickup' : 'Drop';
            dlHtml += '<span style="display:inline-block;background:' + bc + ';color:#fff;padding:2px 7px;border-radius:8px;font-size:10px;font-weight:700;margin:1px;">' + dn.substring(0, 18) + ' ' + bs + '</span> ';
        }
        if (dlKeys.length > 3) dlHtml += '<span style="color:rgba(255,255,255,0.35);font-size:10px;">+' + (dlKeys.length - 3) + '</span>';
    }

    // Last update
    var lastUpd = asset.lastUpdated ? (typeof fmtTime === 'function' ? fmtTime(asset.lastUpdated) : '--') : '--';

    var tdBase = 'padding:9px 10px;border:1px solid rgba(255,255,255,0.05);vertical-align:middle;font-family:\'JetBrains Mono\',monospace;font-size:11.5px;';
    var tdA = tdBase + 'background:rgba(34,211,238,0.04);color:rgba(255,255,255,0.75);';
    var tdB = tdBase + 'background:rgba(167,139,250,0.04);color:rgba(255,255,255,0.75);';
    var tdN = tdBase + 'color:rgba(255,255,255,0.72);background:rgba(255,255,255,0.02);';

    var h = '<tr data-id="' + assetId + '" style="border-bottom:1px solid rgba(255,255,255,0.04);transition:background .12s;">';
    h += '<td style="' + tdN + 'font-weight:700;color:rgba(255,255,255,0.95);font-family:\'Manrope\',sans-serif;white-space:nowrap;">' + name + '</td>';
    h += '<td style="' + tdN + 'text-align:center;">' + dirBadge + '</td>';
    h += '<td style="' + tdA + 'text-align:center;"><span style="color:#22d3ee;font-weight:700;">' + aMaxC + ' / ' + aAvgC + '</span></td>';
    h += '<td style="' + tdA + 'text-align:center;">' + aVoltAvg + '</td>';
    h += '<td style="' + tdA + 'text-align:center;">' + aTime + '</td>';
    h += '<td style="' + tdA + 'text-align:center;">' + aCount + '</td>';
    h += '<td style="' + tdA + 'text-align:center;font-size:10px;color:rgba(255,255,255,0.45);">' + aDate + '</td>';
    h += '<td style="' + tdB + 'text-align:center;"><span style="color:#a78bfa;font-weight:700;">' + bMaxC + ' / ' + bAvgC + '</span></td>';
    h += '<td style="' + tdB + 'text-align:center;">' + bVoltAvg + '</td>';
    h += '<td style="' + tdB + 'text-align:center;">' + bTime + '</td>';
    h += '<td style="' + tdB + 'text-align:center;">' + bCount + '</td>';
    h += '<td style="' + tdB + 'text-align:center;font-size:10px;color:rgba(255,255,255,0.45);">' + bDate + '</td>';
    h += '<td style="' + tdN + '">' + dlHtml + '</td>';
    h += '<td style="' + tdN + 'text-align:center;color:rgba(255,255,255,0.40);font-size:10.5px;">' + lastUpd + '</td>';
    h += '</tr>';
    return h;
}

// ===== RENDER WS TABLE =====
function renderWsTable() {
    // IPS asset type always renders as a card grid
    if (isIpsAssetType()) { renderIpsGridView(); return; }

    // Point Machine in table mode → use PM-specific table renderer
    if (window.pmTableMode || isPointAssetType()) { renderPmTableView(); return; }

    // Signal → grouped tables, one per attribute signature, smallest aspect first
    if (typeof isSignalAssetType === 'function' && isSignalAssetType() &&
        typeof renderSignalGroupedTables === 'function') {
        renderSignalGroupedTables();
        return;
    }

    var assetIds = Object.keys(wsLiveData).filter(function (id) {
        // Defense-in-depth: skip assets not in the bulk response.
        return (typeof isAssetInBulkWhitelist !== 'function') || isAssetInBulkWhitelist(id);
    });
    console.log('[WS Table] Rendering. Assets:', assetIds.length);

    if (assetIds.length === 0) return;
    $('#wsWaiting').remove();

    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    var isTrack = (assetTypeId === 1);
    var typeName = 'ASSET';
    var first = wsLiveData[assetIds[0]];
    if (first && first.AssetTypeId) {
        switch (parseInt(first.AssetTypeId)) {
            case 1: typeName = 'TRACK'; break;
            case 2: typeName = 'SIGNAL'; break;
            case 3: typeName = 'POINT'; break;
            default:
                var _tn = (first.AssetTypeName || '').toUpperCase();
                if (_tn.indexOf('IPS') > -1) typeName = 'IPS';
                break;
        }
    }

    assetIds.sort(function (a, b) {
        return (wsLiveData[a].AssetName || '').localeCompare(wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' });
    });
    assetIds = blankDataLast(assetIds);
    var $c = $('#divTelemetryLive');
    var $existingTable = $c.find('#wsLiveTable');
    var columnsChanged = !arraysEqual(wsCurrentColumns, wsAttributeNames);

    if ($existingTable.length === 0 || columnsChanged) {
        console.log('[WS Table] Full rebuild with AliasNames');
        wsCurrentColumns = wsAttributeNames.slice();
        buildFullTable(assetIds, typeName, isTrack);
        wsTableInitialized = true;
        $('#downloadContainer').show();
    } else {
        console.log('[WS Table] Incremental update');
        incrementalTableUpdate(assetIds, isTrack);
    }

    wsUpdatedAssets = {};
}

// ================================================================
// SIGNAL LIST-VIEW GROUPING - FIXED VERSION
// ================================================================
var _sigAssetInfoCache = {};
var _assetInfoListCache = {};   // cache for asset info API responses
var _signalGroupCache = {};     // cache for signal group mappings
var _SIG_ASSET_INFO_URLS = [
    '/FRS25/Telemetry/GetAssetInfoListData',
    '/Telemetry/GetAssetInfoListData'  // Fallback endpoint
];

function _sigEscAttr(s) { return String(s == null ? '' : s).replace(/"/g, '&quot;'); }

function _sigExtractAttrName(item) {
    if (!item) return null;
    return item.AssetAttributeName ||
        item.AttributeName ||
        item.AssetAttributeAliasName ||
        item.AliasName ||
        item.Name ||
        null;
}

// ── OPTIMIZED: No AJAX — reads from _sigAssetInfoCache pre-populated
//    by loadBulkAssetMetadata(). Falls back to wsLiveData keys.
function fetchSignalAssetInfo(assetId, cb) {
    var aid = String(assetId);
    var entry = _sigAssetInfoCache[aid];

    // Already loaded from bulk — return cached result
    if (entry && entry.loaded && entry.attrs && entry.attrs.length > 0) {
        if (cb) cb(entry.attrs);
        return;
    }

    // Not in cache — fall back to wsLiveData keys (no AJAX)
    var live = (wsLiveData[aid] && wsLiveData[aid].attrs)
        ? Object.keys(wsLiveData[aid].attrs) : [];

    // FIX-9b: Preserve _retryCount from any existing entry so the
    // infinite-loop guard in _renderSignalGroupedTablesCore works.
    var prevRetry = (_sigAssetInfoCache[aid] && _sigAssetInfoCache[aid]._retryCount) || 0;
    _sigAssetInfoCache[aid] = {
        loaded: true,
        loading: false,
        attrs: live,
        pending: [],
        _retryCount: prevRetry
    };

    if (live.length > 0) {
        console.log('[SigGroup] Asset ' + aid + ' attrs from wsLiveData:', live);
    } else {
        console.warn('[SigGroup] Asset ' + aid + ' — no attrs in cache or wsLiveData');
    }

    if (cb) cb(live);
}

// FIX: Clear cache when asset type changes
function clearSignalAssetInfoCache() {
    _sigAssetInfoCache = {};
    console.log('[SigGroup] Asset info cache cleared');
}
// ================================================================
// SIGNAL GROUPED TABLES — Full render + incremental update
// These functions were missing, causing the
// "[SigGroup] renderSignalGroupedTables is not defined!" error.
// ================================================================

// ── Row builder helper ───────────────────────────────────────────
function _buildSigTableRow(assetId, cols) {
    var asset = wsLiveData[assetId];
    if (!asset) return '';
    var name = asset.AssetName || ('Asset ' + assetId);
    var attrs = asset.attrs || {};
    var ts = asset.lastUpdated
        ? (typeof fmtTime === 'function' ? fmtTime(asset.lastUpdated) : '--')
        : '--';
    var siteIdForActions = asset.SiteId || $('#drpSite').val();

    var h = '<tr data-id="' + assetId + '">';
    h += '<td style="font-weight:600;white-space:nowrap;">' + name + '</td>';

    for (var c = 0; c < cols.length; c++) {
        var an = cols[c];
        var ad = attrs[an];
        var raw = ad ? ad.Value : null;
        var val = (raw !== null && raw !== undefined && raw !== '' && !isNaN(parseFloat(raw)))
            ? parseFloat(raw).toFixed(2) : '--';
        var cls = val === '--' ? 'val-na' : '';
        // Apply stale class during initial build if asset already has stale flags
        var _staleMap = wsStaleAttrs[assetId] || {};
        var _sigIsStale = !!_staleMap[an];
        if (_sigIsStale) cls += (cls ? ' ' : '') + 'ws-stale-val';
        var _tip = _getValueTooltip(an, parseFloat(raw), cls);
        var _marks = _getCellMarkers(cls);
        h += '<td class="' + cls + '" data-attr="' + _sigEscAttr(an) + '"' + (_tip ? ' title="' + _tip + '"' : '') + '>' + val + _marks + '</td>';
    }

    // DataLogger relay pills
    var dlRelays = asset.dlRelays || {};
    var dlKeys = Object.keys(dlRelays);
    var dlHtml = '';
    for (var d = 0; d < Math.min(dlKeys.length, 3); d++) {
        var r = dlRelays[dlKeys[d]];
        var bc = r.isPickup ? 'pickup' : 'drop';
        dlHtml += '<span class="rdpms-dl-badge ' + bc + '" style="margin:1px;padding:2px 6px;font-size:10px;">'
            + (r.displayName || dlKeys[d]) + ' ' + (r.isPickup ? '↑' : '↓') + '</span>';
    }
    if (dlKeys.length > 3) dlHtml += '<span class="rdpms-dl-badge" style="margin:1px;">+' + (dlKeys.length - 3) + '</span>';
    h += '<td class="dl-cell">' + (dlHtml || '<span style="color:#94a3b8;font-size:10px;">--</span>') + '</td>';
    h += '<td data-attr="LastUpdate" style="white-space:nowrap;font-size:11px;">' + ts + '</td>';
    h += '<td><button type="button" class="tl-asset-action" title="Graph" onclick="fnGetAssetGraph(\'' + siteIdForActions + '\',\'' + assetId + '\')"><i class="fa-solid fa-chart-line"></i></button> '
        + '<button type="button" class="tl-asset-action" title="Circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')"><i class="fa-solid fa-project-diagram"></i></button></td>';
    h += '</tr>';
    return h;
}

// ── Aspect analysis: detect signal type from attribute names ─────
// Core aspects: RG=Red, DG=Distant/Green, HG=Yellow, HHG=Double Yellow
// Extras: Route (AUG/BUG/CUG/DUG/EUG), Calling (Co_Hg), Pilot (PILOT)
function _sigAnalyseAttrs(attrList) {
    if (!attrList || attrList.length === 0) return { aspectCount: 0, label: 'Unknown', sortKey: 0 };

    var hasRG = false, hasDG = false, hasHG = false, hasHHG = false;
    var routeCount = 0, hasCalling = false, hasPilot = false;
    var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];

    for (var i = 0; i < attrList.length; i++) {
        var a = attrList[i];
        // Strip suffix to get base name
        var base = a.replace(/ mA$/, '').replace(/ V$/, '').replace(/ \(Loc\)$/, '').trim();

        if (base === 'RG') hasRG = true;
        else if (base === 'DG') hasDG = true;
        else if (base === 'HG') hasHG = true;
        else if (base === 'HHG') hasHHG = true;
        else if (base === 'Co_Hg' || base === 'Co_HG' || base === 'CoHg') hasCalling = true;
        else if (base === 'PILOT' || base === 'PILOTRoot') hasPilot = true;
        else {
            for (var r = 0; r < routeNames.length; r++) {
                if (base === routeNames[r]) { routeCount++; break; }
            }
        }
    }

    // Determine base aspect count
    var aspectCount = 0;
    if (hasRG) aspectCount++;
    if (hasDG) aspectCount++;
    if (hasHG) aspectCount++;
    if (hasHHG) aspectCount++;

    // If we couldn't detect any core aspects, fall back to total attr count
    if (aspectCount === 0) {
        return { aspectCount: attrList.length, label: attrList.length + ' Attribute' + (attrList.length === 1 ? '' : 's'), sortKey: attrList.length * 100 };
    }

    // Build label — just the base aspect count.
    // Extras (Route, Calling, Pilot) are shown as separate tag pills in the header.
    var label = aspectCount + ' Aspect';

    // Build sort key: aspectCount * 100 + feature weights (lower = earlier)
    // 2 Aspect = 200, 3 Aspect = 300, 3 Aspect (Route) = 310, etc.
    var sortKey = aspectCount * 100;
    if (routeCount > 0) sortKey += 10;
    if (hasCalling) sortKey += 20;
    if (hasPilot) sortKey += 5;

    return {
        aspectCount: aspectCount,
        routeCount: routeCount,
        hasCalling: hasCalling,
        hasPilot: hasPilot,
        label: label,
        sortKey: sortKey
    };
}

// Helper for per-asset aspect count (used in row sorting within groups)
function _sigGetAspectCount(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return 0;
    var attrs = asset.attrs || {};
    var count = 0;
    if (attrs['RG mA'] || attrs['RG V']) count++;
    if (attrs['DG mA'] || attrs['DG V']) count++;
    if (attrs['HG mA'] || attrs['HG V']) count++;
    if (attrs['HHG mA'] || attrs['HHG V']) count++;
    if (attrs['PILOT mA'] || attrs['PILOT V'] ||
        attrs['PILOTRoot mA'] || attrs['PILOTRoot V']) count++;
    if (attrs['Co_Hg mA'] || attrs['Co_Hg V']) count++;
    var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
    for (var r = 0; r < routeNames.length; r++) {
        if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) count++;
    }
    return count;
}

// ── Full render (debounced) ─────────────────────────────────────
var _sigRenderTimer = null;
var _sigRenderActive = false;       // re-entrancy guard

window.renderSignalGroupedTables = function () {
    // Debounce: collapse rapid successive calls into one
    if (_sigRenderTimer) clearTimeout(_sigRenderTimer);
    _sigRenderTimer = setTimeout(_renderSignalGroupedTablesCore, 80);
};

function _renderSignalGroupedTablesCore() {
    _sigRenderTimer = null;
    if (_sigRenderActive) return;  // prevent re-entrant calls
    _sigRenderActive = true;

    try {
        var $c = $('#divTelemetryLive');

        // Collect signal assets
        var ids = Object.keys(wsLiveData).filter(function (id) {
            var a = wsLiveData[id];
            if (!a) return false;
            // Defense-in-depth: never render an asset that isn't in the bulk
            // response (it would show as a "Asset <id>" garbage row).
            if (typeof isAssetInBulkWhitelist === 'function' && !isAssetInBulkWhitelist(id)) return false;
            if (a.AssetTypeId === undefined || a.AssetTypeId === null || a.AssetTypeId === '') return true;
            return parseInt(a.AssetTypeId) === 2;
        });
        if (ids.length === 0) { _sigRenderActive = false; return; }
        $('#wsWaiting').remove();

        // ── Fetch attribute info via GetAssetInfoListData (one call per uncached asset) ──
        // The API provides the COMPLETE attribute list for each signal asset.
        // wsLiveData keys are only used as a final fallback inside fetchSignalAssetInfo
        // when all API endpoints fail.
        var needFetch = [], pending = 0;
        for (var i = 0; i < ids.length; i++) {
            var e = _sigAssetInfoCache[String(ids[i])];
            if (!e || (!e.loaded && !e.loading)) {
                needFetch.push(ids[i]);
            }
            else if (e.loaded && (!e.attrs || e.attrs.length === 0)) {
                // FIX-9: Previous call returned empty. Allow ONE retry (WS data
                // may have arrived since the first attempt), but cap at 1 to
                // prevent the infinite loop: fetchSignalAssetInfo reads from
                // wsLiveData (no AJAX), so if it's still empty after 1 retry
                // it will stay empty until the next WS batch, which will
                // trigger its own incremental update.
                var retries = e._retryCount || 0;
                if (retries < 1) {
                    e._retryCount = retries + 1;
                    needFetch.push(ids[i]);
                }
                // else: already retried once — skip, render with what we have
            }
            else if (e.loading) pending++;
        }

        if (needFetch.length > 0 || pending > 0) {
            if ($c.find('.sig-group-wrap').length === 0) {
                $c.html('<div class="text-center p-4" style="color:#64748b;">'
                    + '<i class="fas fa-layer-group fa-lg" style="display:block;margin-bottom:8px;color:#10b981;"></i>'
                    + 'Loading signal attribute groups…</div>');
            }
            if (needFetch.length > 0) {
                var remaining = needFetch.length;
                for (var f = 0; f < needFetch.length; f++) {
                    fetchSignalAssetInfo(needFetch[f], function () {
                        remaining--;
                        if (remaining <= 0 && typeof isSignalAssetType === 'function' && isSignalAssetType()) {
                            // Schedule via the debounced wrapper (NOT direct)
                            window.renderSignalGroupedTables();
                        }
                    });
                }
            }
            _sigRenderActive = false;
            return;
        }

        // ── Sort assets by aspect count ascending, then alphabetically ──
        ids.sort(function (a, b) {
            // Blank-data assets sink to bottom
            var blankA = (typeof assetHasNoData === 'function') ? assetHasNoData(a) : false;
            var blankB = (typeof assetHasNoData === 'function') ? assetHasNoData(b) : false;
            if (blankA !== blankB) return blankA ? 1 : -1;

            // Aspect count ascending (fewer aspects first)
            var cntA = _sigGetAspectCount(a);
            var cntB = _sigGetAspectCount(b);
            if (cntA !== cntB) return cntA - cntB;

            // Tie-break: alphabetical name
            return (wsLiveData[a].AssetName || '').localeCompare(
                wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' });
        });

        // Group by attribute signature (unique set of attrs = one table)
        var groups = {};
        for (var g = 0; g < ids.length; g++) {
            var aid = ids[g];
            var ce = _sigAssetInfoCache[String(aid)];
            var attrs = (ce && ce.attrs && ce.attrs.length > 0)
                ? ce.attrs.slice()
                : Object.keys(wsLiveData[aid].attrs || {});
            var sig = attrs.slice().sort().join('|');
            if (!groups[sig]) {
                var analysis = _sigAnalyseAttrs(attrs);
                groups[sig] = { attrs: attrs, ids: [], sig: sig, analysis: analysis };
            }
            groups[sig].ids.push(aid);
        }

        // Sort groups by aspect sort key ascending (2 Aspect → 3 Aspect → 3 Aspect (Route) → 4 Aspect → ...)
        var groupList = Object.keys(groups).map(function (k) { return groups[k]; });
        groupList.sort(function (a, b) {
            if (a.analysis.sortKey !== b.analysis.sortKey) return a.analysis.sortKey - b.analysis.sortKey;
            return a.sig.localeCompare(b.sig);
        });

        // Build table HTML for each group
        var html = '<div class="sig-group-wrap">';
        for (var gi = 0; gi < groupList.length; gi++) {
            var grp = groupList[gi];
            var cols = grp.attrs.slice().sort(function (x, y) { return x.localeCompare(y); });
            var grpLabel = grp.analysis.label;
            var ac = grp.analysis.aspectCount || 0;
            var hasRoute = grp.analysis.routeCount > 0;
            var hasCalling = grp.analysis.hasCalling;
            var hasPilot = grp.analysis.hasPilot;

            // Color scheme per aspect type
            var grad, iconBg, icon;
            if (ac <= 2) {
                grad = 'linear-gradient(135deg,#059669,#10b981)';
                iconBg = 'rgba(255,255,255,0.2)';
                icon = 'fa-traffic-light';
            } else if (ac === 3 && !hasRoute && !hasCalling) {
                grad = 'linear-gradient(135deg,#2563eb,#3b82f6)';
                iconBg = 'rgba(255,255,255,0.2)';
                icon = 'fa-traffic-light';
            } else if (ac === 3) {
                grad = 'linear-gradient(135deg,#7c3aed,#8b5cf6)';
                iconBg = 'rgba(255,255,255,0.2)';
                icon = 'fa-project-diagram';
            } else if (ac >= 4 && (hasRoute || hasCalling)) {
                grad = 'linear-gradient(135deg,#be185d,#ec4899)';
                iconBg = 'rgba(255,255,255,0.2)';
                icon = 'fa-project-diagram';
            } else if (ac >= 4) {
                grad = 'linear-gradient(135deg,#1e3a5f,#2563eb)';
                iconBg = 'rgba(255,255,255,0.2)';
                icon = 'fa-traffic-light';
            } else {
                grad = 'linear-gradient(135deg,#475569,#64748b)';
                iconBg = 'rgba(255,255,255,0.15)';
                icon = 'fa-signal';
            }

            // Tag pills for extras
            var tags = '';
            if (hasRoute) tags += '<span style="background:rgba(255,255,255,0.2);color:#fff;padding:2px 8px;border-radius:10px;font-size:10px;font-weight:600;letter-spacing:0.3px;">ROUTE</span>';
            if (hasCalling) tags += '<span style="background:rgba(255,255,255,0.2);color:#fff;padding:2px 8px;border-radius:10px;font-size:10px;font-weight:600;letter-spacing:0.3px;">CALLING</span>';
            if (hasPilot) tags += '<span style="background:rgba(255,255,255,0.2);color:#fff;padding:2px 8px;border-radius:10px;font-size:10px;font-weight:600;letter-spacing:0.3px;">PILOT</span>';

            html += '<div class="sig-group" data-sig="' + _sigEscAttr(grp.sig) + '">';
            html += '<div style="display:flex;align-items:center;gap:12px;margin:20px 0 6px;padding:10px 16px;border-radius:8px;background:' + grad + ';box-shadow:0 2px 8px rgba(0,0,0,0.12);flex-wrap:wrap;">'
                + '<span style="display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;border-radius:8px;background:' + iconBg + ';flex-shrink:0;">'
                + '<i class="fas ' + icon + '" style="color:#fff;font-size:14px;"></i></span>'
                + '<span style="font-weight:700;color:#fff;font-size:15px;letter-spacing:0.3px;">'
                + grpLabel + '</span>'
                + tags
                + '<span style="margin-left:auto;background:rgba(255,255,255,0.2);color:#fff;padding:3px 10px;border-radius:12px;font-size:11px;font-weight:600;">'
                + grp.ids.length + ' Signal' + (grp.ids.length === 1 ? '' : 's') + '</span>'
                + '</div>';

            html += '<div class="table-responsive">'
                + '<table class="table mb-0" style="font-size:12px;">'
                + '<thead><tr style="background:#1e3a5f;color:#fff;">'
                + '<th style="min-width:100px;">Asset</th>';
            for (var ci = 0; ci < cols.length; ci++) {
                var cn = cols[ci];
                var aid0 = grp.ids[0];
                var ad0 = (wsLiveData[aid0] && wsLiveData[aid0].attrs) ? wsLiveData[aid0].attrs[cn] : null;
                var attrId0 = ad0 ? (ad0.AttrId || ad0.AssetAttributeId) : null;
                var lbl = (typeof getAttrDisplayName === 'function') ? getAttrDisplayName(cn, attrId0, aid0) : cn;
                html += '<th title="' + lbl + '">' + lbl + '</th>';
            }
            html += '<th>DL</th><th>Last Updated</th><th></th></tr></thead><tbody>';

            for (var ri = 0; ri < grp.ids.length; ri++) {
                html += _buildSigTableRow(grp.ids[ri], cols);
            }
            html += '</tbody></table></div></div>';
        }
        html += '</div>';

        $c.html(html);
        $('#downloadContainer').show();
        wsUpdatedAssets = {};
    } finally {
        _sigRenderActive = false;
    }
}

// ── Incremental update ───────────────────────────────────────────
window.updateSignalGroupedTables = function (updatedIds) {
    var $wrap = $('#divTelemetryLive .sig-group-wrap');
    if ($wrap.length === 0) {
        window.renderSignalGroupedTables();
        return;
    }
    if (!updatedIds || updatedIds.length === 0) return;

    for (var i = 0; i < updatedIds.length; i++) {
        var aid = String(updatedIds[i]);

        if (!isAssetInBulkWhitelist(aid)) {
            delete wsLiveData[aid];
            delete wsUpdatedAssets[aid];
            $wrap.find('tr[data-id="' + aid + '"]').remove();
            continue;
        }

        var asset = wsLiveData[aid];
        if (!asset) continue;

        var $row = $wrap.find('tr[data-id="' + aid + '"]');
        if (!$row.length) {
            window.renderSignalGroupedTables();
            return;
        }

        var attrs = asset.attrs || {};
        var staleMap = wsStaleAttrs[aid] || {};

        $row.find('td[data-attr]').each(function () {
            var attrName = $(this).data('attr');
            if (attrName === 'LastUpdate') {
                var ts = asset.lastUpdated
                    ? (typeof fmtTime === 'function' ? fmtTime(asset.lastUpdated) : '--')
                    : '--';
                $(this).text(ts);
                return;
            }
            var ad = attrs[attrName];
            var raw = ad ? ad.Value : null;
            var val = (raw !== null && raw !== undefined && raw !== '' && !isNaN(parseFloat(raw)))
                ? parseFloat(raw).toFixed(2) : '--';
            var cls = val === '--' ? 'val-na' : '';
            var isStale = !!staleMap[attrName];
            if (isStale) cls += (cls ? ' ' : '') + 'ws-stale-val';
            var _marks = _getCellMarkers(cls);
            var _tip = _getValueTooltip(attrName, parseFloat(raw), cls);
            $(this).html(val + _marks).attr('class', cls).attr('title', _tip || '');
        });

        var dlRelays = asset.dlRelays || {};
        var dlKeys = Object.keys(dlRelays);
        var $dl = $row.find('td.dl-cell');
        if ($dl.length) {
            var dlHtml = '';
            for (var d = 0; d < Math.min(dlKeys.length, 3); d++) {
                var relay = dlRelays[dlKeys[d]];
                var bc = relay.isPickup ? 'pickup' : 'drop';
                dlHtml += '<span class="rdpms-dl-badge ' + bc + '" style="margin:1px;padding:2px 6px;font-size:10px;">'
                    + (relay.displayName || dlKeys[d]) + ' ' + (relay.isPickup ? '↑' : '↓') + '</span>';
            }
            if (dlKeys.length > 3) dlHtml += '<span class="rdpms-dl-badge" style="margin:1px;">+' + (dlKeys.length - 3) + '</span>';
            $dl.html(dlHtml || '<span style="color:#94a3b8;font-size:10px;">--</span>');
        }
    }
};
function arraysEqual(a, b) {
    if (!a || !b) return false;
    if (!a || !b) return false;
    if (a.length !== b.length) return false;
    for (var i = 0; i < a.length; i++) {
        if (a[i] !== b[i]) return false;
    }
    return true;
}
// ================================================================
// getValueColorClass - returns CSS class for dangerous/warning values
// based on attribute ID and numeric value.
// ================================================================
function getValueColorClass(attrId, value) {
    if (value === undefined || value === null || isNaN(value)) return '';

    // Map attribute ID to name (if needed, use global assetAttributeMap)
    var attrName = '';
    if (typeof assetAttributeMap !== 'undefined' && attrId) {
        attrName = assetAttributeMap[String(attrId)] || assetAttributeMap[attrId] || '';
    }
    // Fallback: if no name found via ID, we cannot colour – return empty
    if (!attrName) return '';

    var lowerName = attrName.toLowerCase();

    // Danger rules (copied from original buildTableRow comments)
    // Vr: danger if (value > 0.1 && value < 2.5) || value > 4.2
    if ((lowerName === 'vr' || lowerName.indexOf('vr') !== -1) &&
        ((value > 0.1 && value < 2.5) || value > 4.2)) {
        return 'val-danger';
    }
    // TPR V / TPR V (Loc): danger if value > 0.1 && value < 20
    if ((lowerName.indexOf('tpr') !== -1) &&
        (value > 0.1 && value < 20)) {
        return 'val-danger';
    }
    // Charger mA: danger if value < 100
    if ((lowerName === 'charger ma' || lowerName.indexOf('charger') !== -1) &&
        value < 100) {
        return 'val-danger';
    }
    // Choke V: danger if value > 1.8
    if ((lowerName === 'choke v' || lowerName.indexOf('choke') !== -1) &&
        value > 1.8) {
        return 'val-danger';
    }

    return '';
}

// Returns a short hover tooltip for danger/warn/stale cells
// Returns short combined hover tooltip for danger/warn/stale cells.
function _getValueTooltip(attrName, num, cls) {
    if (!cls) return '';
    var parts = [];

    // Threshold part (short)
    if (attrName === 'Vr' && num > 4.2) parts.push('\u26a0 Vr > 4.2V');
    else if (attrName === 'Vr' && num > 0.1 && num < 2.5) parts.push('\u26a0 Vr 0.1\u20132.5V');
    else if ((attrName === 'TPR V' || attrName === 'TPR V (Loc)') && num > 0.1 && num < 20) parts.push('\u26a0 TPR 0.1\u201320V');
    else if (attrName === 'Charger mA' && num < 100) parts.push('\u26a0 Charger < 100mA');
    else if (attrName === 'Choke V' && num > 1.8) parts.push('\u26a0 Choke > 1.8V');
    else if (cls.indexOf('val-danger') > -1 || cls.indexOf('danger') > -1) parts.push('\u26a0 Out of range');
    else if (cls.indexOf('warn') > -1) parts.push('\u26a0 Approaching limit');

    // Stale part (short)
    if (cls.indexOf('ws-stale-val') > -1) parts.push('\ud83d\udd53 Stale 5+ min');

    return parts.join(' \u2502 ');
}

function buildFullTable(assetIds, typeName, isTrack) {
    var h = '<div class="table-responsive" style="max-height:72vh;overflow-y:auto;">';
    h += '<table class="table mb-0" id="wsLiveTable"><thead><tr>';
    h += '<th style="min-width:90px;">' + typeName + '</th>';

    // Build headers with AliasName
    for (var ci = 0; ci < wsAttributeNames.length; ci++) {
        var attrName = wsAttributeNames[ci];
        // Try multiple sources for attribute ID
        var attrId = null;
        if (wsAttributeIds && wsAttributeIds[attrName]) {
            attrId = wsAttributeIds[attrName];
        }
        // Also try to get ID from any asset's attr data
        if (!attrId) {
            for (var assetKey in wsLiveData) {
                var assetData = wsLiveData[assetKey];
                if (assetData && assetData.attrs && assetData.attrs[attrName] && assetData.attrs[attrName].AttrId) {
                    attrId = assetData.attrs[attrName].AttrId;
                    // Also store in wsAttributeIds for future use
                    if (wsAttributeIds) wsAttributeIds[attrName] = attrId;
                    break;
                }
            }
        }
        var displayName = getAttrDisplayName(attrName, attrId, null);
        var formattedName = formatAliasName(displayName);

        h += '<th data-col="' + attrName + '" title="' + displayName + '">' + formattedName + '</th>';
    }

    // Add derived value headers for Track assets
    if (isTrack) {
        h += getDerivedHeaders();
    }
    // DataLogger column for Track and Signal
    if (isTrack || parseInt(wsCurrentAssetTypeId) === 2) {
        h += '<th style="min-width:100px;">DataLogger</th>';
    }
    h += '<th style="min-width:80px;">Last Update</th><th style="min-width:70px;">Actions</th></tr></thead><tbody>';

    for (var ri = 0; ri < assetIds.length; ri++) {
        h += buildTableRow(assetIds[ri], isTrack, true);
    }

    h += '</tbody></table></div>';
    $('#divTelemetryLive').html(h);
}

function buildTableRow(assetId, isTrack, isNew) {
    var asset = wsLiveData[assetId];
    if (!asset) return '';

    // Check if Point Machine (AssetTypeId = 3) to add PT- prefix
    var isPointMachine = (parseInt(asset.AssetTypeId) === 3 || parseInt(wsCurrentAssetTypeId) === 3);
    var displayName = asset.AssetName || 'Asset ' + assetId;
    if (isPointMachine && displayName && displayName.indexOf('PT-') !== 0) {
        displayName = 'PT-' + displayName;
    }
    var h = '<tr data-id="' + assetId + '"' + (isNew ? ' class="ws-row-new"' : '') + '>';
    h += '<td class="asset-name">' + displayName + '</td>';
    // Graph + Circuit per-row action buttons (uses tlBuildAssetActions exposed at bottom of file).

    var ifMa = 0, irMa = 0, tprV = 0;

    for (var ai = 0; ai < wsAttributeNames.length; ai++) {
        var an = wsAttributeNames[ai];
        var ad = asset.attrs[an];
        var raw = ad ? ad.Value : null;
        var cls = '', disp = '';

        if (raw === null || raw === undefined || raw === '') {
            disp = '-'; cls = 'val-na';
        } else {
            var num = parseFloat(raw);
            if (isNaN(num)) { disp = raw; }
            else {
                if (num === 0) disp = '0.0';
                else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                else disp = num.toFixed(2);

                if (an === 'If mA') ifMa = num;
                else if (an === 'Ir mA') irMa = num;
                else if (an === 'TPR V') tprV = num;

                // Danger styling
                //if (an === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'val-danger';
                //else if ((an === 'TPR V' || an === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'val-danger';
                //else if (an === 'Charger mA' && num < 100) cls = 'val-danger';
                //else if (an === 'Choke V' && num > 1.8) cls = 'val-danger';

                var attrId = ad ? (ad.AssetAttributeId || ad.AttrId) : null;
                cls = getValueColorClass(attrId, num);
            }
            if (ad && ad.changed && isNew) {
                cls += ' ws-cell-flash val-changed-flash';
                ad.changed = false;
            }
        }
        var _genStaleMap = wsStaleAttrs[assetId] || {};
        var _genIsStale = !!_genStaleMap[an];
        if (_genIsStale) cls += (cls ? ' ' : '') + 'ws-stale-val';
        var _tip = _getValueTooltip(an, (typeof num !== 'undefined' ? num : parseFloat(raw)), cls);
        var _marks = _getCellMarkers(cls);
        h += '<td class="' + cls + '" data-attr="' + an + '"' + (_tip ? ' title="' + _tip + '"' : '') + '>' + disp + _marks + '</td>';
    }

    if (isTrack) {
        // Calculate and add derived values
        var derived = calculateDerivedValues(asset.attrs);
        h += getDerivedCells(derived);
    }

    // DataLogger relay badges for Track and Signal
    if (isTrack || parseInt(wsCurrentAssetTypeId) === 2) {
        var dlRelays = asset.dlRelays || {};
        var dlHtml = '';
        var dlKeys = Object.keys(dlRelays);
        if (dlKeys.length > 0) {
            // Sort: Pickup first, then Drop
            dlKeys.sort(function (a, b) {
                var ra = dlRelays[a], rb = dlRelays[b];
                if (ra.isPickup && !rb.isPickup) return -1;
                if (!ra.isPickup && rb.isPickup) return 1;
                return (ra.displayName || a).localeCompare(rb.displayName || b);
            });
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        h += '<td class="dl-cell">' + dlHtml + '</td>';
    }

    // Use TimestampDevice from operation ID attributes for date display
    var operationTs = getOperationTimestampDevice(asset);
    var ts = operationTs ? fmtTimestampDevice(operationTs) : (asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--');
    h += '<td class="time-cell" data-attr="LastUpdate">' + ts + '</td>';

    // Graph + Circuit per-row action buttons
    var rowSiteId = asset.SiteId || $('#drpSite').val();
    h += '<td class="tl-asset-actions-cell">' +
        '<span class="tl-asset-actions" data-asset-id="' + assetId + '">' +
        '<button type="button" class="tl-asset-action tl-aa-graph" title="Open Graph" onclick="fnGetAssetGraph(\'' + rowSiteId + '\',\'' + assetId + '\')">' +
        '<i class="fa-solid fa-chart-line"></i>' +
        '</button>' +
        '<button type="button" class="tl-asset-action tl-aa-circuit" title="Open Circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')">' +
        '<i class="fa-solid fa-project-diagram"></i>' +
        '</button>' +
        '</span>' +
        '</td>';

    h += '</tr>';
    return h;
}

function updateRowCells($row, asset, isTrack) {
    var ifMa = 0, irMa = 0, tprV = 0;

    for (var ai = 0; ai < wsAttributeNames.length; ai++) {
        var an = wsAttributeNames[ai];
        var ad = asset.attrs[an];
        var $cell = $row.find('td[data-attr="' + an + '"]');
        // Positional fallback: col 0 = asset name, attrs start at col 1.
        // FIX-3: was ai + 2 (off-by-one — actions are at the END, not col 1).
        if (!$cell.length) $cell = $row.find('td').eq(ai + 1);
        if (!$cell.length) continue;

        var raw = ad ? ad.Value : null;
        var cls = '', disp = '';

        if (raw === null || raw === undefined || raw === '') {
            disp = '-'; cls = 'val-na';
        } else {
            var num = parseFloat(raw);
            if (isNaN(num)) { disp = raw; }
            else {
                if (num === 0) disp = '0.0';
                else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                else disp = num.toFixed(2);

                if (an === 'If mA') ifMa = num;
                else if (an === 'Ir mA') irMa = num;
                else if (an === 'TPR V') tprV = num;

                var attrId = ad ? (ad.AssetAttributeId || ad.AttrId) : null;
                cls = getValueColorClass(attrId, num);
                // FIX-5: Hardcoded danger overrides removed — they are already
                // commented out in buildTableRow, so keeping them here created
                // a visual mismatch between initial render and incremental
                // updates. getValueColorClass is now the single source of truth.
            }
        }

        // Stale check
        var _incStaleMap = wsStaleAttrs[assetId] || {};
        var _incIsStale = !!_incStaleMap[an];
        if (_incIsStale) cls += (cls ? ' ' : '') + 'ws-stale-val';

        var _cellText = $cell.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
        if (_cellText !== disp) {
            if (ad && ad.changed) {
                cls += ' ws-cell-flash val-changed-flash';
                ad.changed = false;
                (function ($c) {
                    setTimeout(function () {
                        $c.removeClass('ws-cell-flash val-changed-flash');
                    }, 1200);
                })($cell);
            }
            var _incTip = _getValueTooltip(an, (typeof num !== 'undefined' ? num : NaN), cls);
            var _incMarks = _getCellMarkers(cls);
            $cell.attr('class', cls).attr('title', _incTip || '').html(disp + _incMarks);
        }
    }

    // Update derived values for Track assets
    if (isTrack) {
        var derived = calculateDerivedValues(asset.attrs);
        var $derivedCells = $row.find('td.derived-val');
        if ($derivedCells.length >= 7) {
            $derivedCells.eq(0).text(formatDerivedValue(derived.itcBattCharg));
            $derivedCells.eq(1).text(formatDerivedValue(derived.vtcVarRes));
            $derivedCells.eq(2).text(formatDerivedValue(derived.rtcChFeedEnd));
            $derivedCells.eq(3).text(formatDerivedValue(derived.rtcVarRes));
            $derivedCells.eq(4).text(formatDerivedValue(derived.vtcTr));
            $derivedCells.eq(5).text(formatDerivedValue(derived.ibalst));
            $derivedCells.eq(6).text(formatDerivedValue(derived.rrail));
        }

        // Update DataLogger column
        var $dlCell = $row.find('td.dl-cell');
        if ($dlCell.length) {
            var dlRelays = asset.dlRelays || {};
            var dlHtml = '';
            var dlKeys = Object.keys(dlRelays);
            if (dlKeys.length > 0) {
                for (var di = 0; di < dlKeys.length; di++) {
                    var relay = dlRelays[dlKeys[di]];
                    var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                    var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                    dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
                }
            } else {
                dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
            }
            $dlCell.html(dlHtml);
        }
    }

    // Update timestamp
    var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
    var $tsCell = $row.find('td[data-attr="LastUpdate"]');
    if (!$tsCell.length) $tsCell = $row.find('td.time-cell');
    if ($tsCell.length) $tsCell.text(ts);
}


// Re-initialize DataTable if it's being used
function reinitializeDataTable() {
    if (typeof $.fn.DataTable === 'function') {
        var $table = $('#wsLiveTable');
        if ($.fn.DataTable.isDataTable('#wsLiveTable')) {
            $table.DataTable().destroy();
        }

    }
}

// ================================================================
// STEP 4: REPLACE the existing updateSingleTableCell() function
// ================================================================

function updateSingleTableCell(assetId, attrName, value, hasChanged, timestamp) {
    var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');

    // If row doesn't exist, queue for batch render
    if (!$row.length) {
        wsUpdatedAssets[assetId] = true;
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                renderWsTable();
            }, 200);
        }
        return;
    }

    // Find cell by data-attr (fast lookup)
    var $cell = $row.find('td[data-attr="' + attrName + '"]');
    if (!$cell.length) {
        // Fallback to index-based lookup
        var colIdx = wsAttributeNames.indexOf(attrName);
        if (colIdx === -1) {
            // New column - need full rebuild
            wsUpdatedAssets[assetId] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    renderWsTable();
                }, 200);
            }
            return;
        }
        var cellIdx = colIdx + 1;
        var $cells = $row.find('td');
        if (cellIdx >= $cells.length) return;
        $cell = $($cells[cellIdx]);
    }

    // Format value
    var num = parseFloat(value);
    var disp = '', cls = '';

    if (value === null || value === undefined || value === '') {
        disp = '-';
        cls = 'val-na';
    }
    else if (isNaN(num)) {
        disp = value;
    }
    else {
        if (num === 0) disp = '0.0';
        else if (Math.abs(num) >= 100) disp = num.toFixed(2);
        else if (Math.abs(num) >= 10) disp = num.toFixed(2);
        else disp = num.toFixed(2);

        if (attrName === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'val-danger';
        else if ((attrName === 'TPR V' || attrName === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'val-danger';
        else if (attrName === 'Charger mA' && num < 100) cls = 'val-danger';
        else if (attrName === 'Choke V' && num > 1.8) cls = 'val-danger';
    }

    var _tip = _getValueTooltip(attrName, num, cls);

    // Add flash animation if value changed
    if (hasChanged) {
        cls += ' ws-cell-flash val-changed-flash';
        setTimeout(function () {
            $cell.removeClass('ws-cell-flash val-changed-flash');
        }, 1200);
    }

    // Update cell
    var _marks = _getCellMarkers(cls);
    $cell.attr('class', cls).attr('title', _tip || '').html(disp + _marks);

    // Update timestamp
    var _tsAsset = wsLiveData[assetId];
    var ts = (_tsAsset && _tsAsset.lastUpdated) ? fmtTime(_tsAsset.lastUpdated) : fmtTime(new Date());
    var $tsCell = $row.find('td[data-attr="LastUpdate"]');
    if (!$tsCell.length) $tsCell = $row.find('td.time-cell');
    if ($tsCell.length) $tsCell.text(ts);

    // Update computed columns for Track view
    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    if (assetTypeId === 1 && (attrName === 'If mA' || attrName === 'Ir mA' || attrName === 'TPR V')) {
        updateTrackComputedCols(assetId, $row);
    }
}

// ================================================================
// STEP 5: REPLACE the existing processItems() function with this
// (This is the KEY FIX for batch processing)
// ================================================================
var wsPendingUpdates = false;
var wsCurrentColumns = [];
var wsTableInitialized = false;

// ================================================================
// OPTIMIZED MESSAGE PROCESSING SYSTEM v2
// - Handles multiple clients/assets simultaneously
// - Smooth view switching without data loss
// - Continuous streaming for Track, Signal, Point Machine
// - requestAnimationFrame for smooth rendering
// ================================================================
var wsMessageQueue = [];              // Queue to hold pending messages
var wsIsProcessingQueue = false;      // Flag to prevent concurrent processing
var wsQueueProcessTimer = null;       // Timer for queue processing
var wsLastProcessedTime = 0;          // Track last processing time
var wsLastUIUpdateTime = 0;           // Track last UI update time
var wsPendingUIUpdate = false;        // Flag to track if UI update is needed
var wsUIUpdateTimer = null;           // Separate timer for UI updates
var wsCurrentViewType = '';           // Cache current view type
var wsViewSwitchPending = false;      // Flag for pending view switch
var wsStreamingActive = false;        // Flag for active streaming
var wsRAFScheduled = false;           // requestAnimationFrame flag

// Optimized settings - tuned for smooth performance
var WS_PROCESS_INTERVAL = 0;          // Process immediately (next tick)
var WS_UI_UPDATE_INTERVAL = 33;       // ~30fps UI updates (smooth)
var WS_QUEUE_BATCH_SIZE = 200;        // Reduced batch: process sooner, less delay
var WS_IMMEDIATE_THRESHOLD = 20;      // Process immediately if < 20 messages

// Asset type specific UI intervals
var WS_TRACK_UI_INTERVAL = 66;        // Track: ~15fps (more data per update)
var WS_SIGNAL_UI_INTERVAL = 0;        // Signal: immediate via requestAnimationFrame
var WS_PM_UI_INTERVAL = 33;           // Point Machine: ~30fps

// Performance tracking
var wsProcessedPerSecond = 0;
var wsLastSecondTime = Date.now();
var wsMessagesThisSecond = 0;

// ================================================================
// OPTIMIZED QUEUE FUNCTIONS
// ================================================================

// Queue incoming messages - optimized for continuous streaming
function queueWsMessages(messages) {

    if (!messages || !Array.isArray(messages) || messages.length === 0) return;

    wsStreamingActive = true;

    // Fast array concat
    if (wsMessageQueue.length === 0) {
        wsMessageQueue = messages.slice();
    } else {
        Array.prototype.push.apply(wsMessageQueue, messages);
    }

    // Track throughput
    wsMessagesThisSecond += messages.length;
    var now = Date.now();
    if (now - wsLastSecondTime >= 1000) {
        wsProcessedPerSecond = wsMessagesThisSecond;
        wsMessagesThisSecond = 0;
        wsLastSecondTime = now;
    }

    // Start processing
    if (!wsQueueProcessTimer) {
        var delay = messages.length < WS_IMMEDIATE_THRESHOLD ? 0 : WS_PROCESS_INTERVAL;
        wsQueueProcessTimer = setTimeout(processMessageQueue, delay);
    }
}

// Process queued messages - continuous streaming optimized
function processMessageQueue() {
    wsQueueProcessTimer = null;

    if (wsMessageQueue.length === 0) {
        wsIsProcessingQueue = false;
        wsStreamingActive = false;
        if (wsPendingUIUpdate) {
            scheduleUIUpdate(true);
        }
        return;
    }

    wsIsProcessingQueue = true;

    // Process large batches at once
    var processCount = Math.min(wsMessageQueue.length, WS_QUEUE_BATCH_SIZE);
    var batch = wsMessageQueue.splice(0, processCount);

    // Process batch — go through window.* so sip-telemetry.js bridge
    // (which monkey-patches window.processItemsInternal) is invoked too.
    // This is what makes the SIP view receive live WebSocket data.
    (window.processItemsInternal || processItemsInternal)(batch);

    wsLastProcessedTime = Date.now();
    wsPendingUIUpdate = true;

    // Schedule UI update
    scheduleUIUpdate(false);

    // Continue processing
    if (wsMessageQueue.length > 0) {
        wsQueueProcessTimer = setTimeout(processMessageQueue, WS_PROCESS_INTERVAL);
    } else {
        wsIsProcessingQueue = false;
    }
}

// Get optimal UI interval based on asset type
function getOptimalUIInterval() {
    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    switch (assetTypeId) {
        case 1: return WS_TRACK_UI_INTERVAL;
        case 2: return WS_SIGNAL_UI_INTERVAL;
        case 3: return WS_PM_UI_INTERVAL;
        default: return WS_UI_UPDATE_INTERVAL;
    }
}

// Schedule UI update with smart debouncing
function scheduleUIUpdate(forceImmediate) {
    //if (wsUIUpdateTimer && !forceImmediate) return;
    if (wsUIUpdateTimer && !forceImmediate) {
        wsPendingUIUpdate = true;
        return;
    }
    if (wsUIUpdateTimer) {
        clearTimeout(wsUIUpdateTimer);
        wsUIUpdateTimer = null;
    }

    var interval = forceImmediate ? 0 : getOptimalUIInterval();
    var timeSinceLastUI = Date.now() - wsLastUIUpdateTime;
    interval = Math.max(0, interval - timeSinceLastUI);

    if (interval === 0 && !wsRAFScheduled) {
        // Use requestAnimationFrame for immediate smooth updates
        wsRAFScheduled = true;
        requestAnimationFrame(function () {
            wsRAFScheduled = false;
            wsLastUIUpdateTime = Date.now();
            if (wsPendingUIUpdate && !wsViewSwitchPending) {
                wsPendingUIUpdate = false;
                executeUIUpdate();
            }
        });
    } else if (interval > 0) {
        wsUIUpdateTimer = setTimeout(function () {
            wsUIUpdateTimer = null;
            wsLastUIUpdateTime = Date.now();
            if (wsPendingUIUpdate && !wsViewSwitchPending) {
                wsPendingUIUpdate = false;
                executeUIUpdate();
            }
        }, interval);
    }
}

// Execute UI update
function executeUIUpdate() {
    // Gate: SIP-only connections should not render data into table/cards.
    // Only user-initiated Search clears this flag.
    if (window._wsSipOnlyMode) return;

    var viewType = $('#drpView').val();
    var updatedAssetIds = Object.keys(wsUpdatedAssets);

    if (updatedAssetIds.length === 0) return;

    if (wsRenderTimer) {
        clearTimeout(wsRenderTimer);
        wsRenderTimer = null;
    }

    wsCurrentViewType = viewType;

    try {
        // Find the Table view section in executeUIUpdate and update it:

        if (viewType === 'Table' && !isIpsAssetType()) {
            // SIGNAL - use grouped tables
            if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
                console.log('[executeUIUpdate] Signal Table - updating grouped tables');
                if (typeof updateSignalGroupedTables === 'function') {
                    updateSignalGroupedTables(updatedAssetIds);
                } else if (typeof renderSignalGroupedTables === 'function') {
                    renderSignalGroupedTables();
                }
            }
            else if (window.pmTableMode || isPointAssetType()) {
                renderPmTableView();
            }
            else {
                var $existingTable = $('#wsLiveTable');
                if ($existingTable.length > 0) {
                    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
                    var isTrack = (assetTypeId === 1);
                    incrementalTableUpdate(updatedAssetIds, isTrack);
                } else {
                    renderWsTable();
                }
            }
        } else if (viewType === 'IPS' || (viewType === 'Table' && isIpsAssetType())) {
            if (typeof window.updateIpsGridIncremental === 'function') {
                window.updateIpsGridIncremental(updatedAssetIds);
            } else {
                renderIpsGridView();
            }
        } else if (viewType === 'RDPMS') {
            updateRDPMSViewIncremental(updatedAssetIds);
        }
        else if (viewType === 'Cards') {
            // Use the same dispatcher logic as _tlApplyViewMode
            if (isSignalAssetType()) {
                if (typeof renderRDPMSView === 'function') renderRDPMSView();
            } else if (isPointAssetType()) {
                if (typeof renderPointMachineView === 'function') renderPointMachineView();
            } else if (isIpsAssetType()) {
                if (typeof renderIpsGridView === 'function') renderIpsGridView();
            } else {
                // Track or unknown → use the generic card binder
                if (typeof fnBindTrackCards === 'function') fnBindTrackCards();
            }
        }
        // PointMachine view = Card view for PM assets
        else if (viewType === 'PointMachine') {
            updatePMViewIncremental(updatedAssetIds);
        }

        // Circuit overlay live-update: only when the per-asset Circuit overlay is
        // active (set by tlOpenAssetView). The old viewType==='Circuit' trigger
        // is gone because Circuit isn't a view type anymore.
        if (window._tlOverlayActive === 'Circuit') {
            for (var i = 0; i < updatedAssetIds.length; i++) {
                if (typeof updateCircuitFromWebSocket === 'function') {
                    updateCircuitFromWebSocket(updatedAssetIds[i]);
                }
            }
        }

        wsUpdatedAssets = {};

        // Update stats less frequently during heavy streaming
        if (!wsStreamingActive || wsBatchCount % 5 === 0) {
            updateWsStats();
        }
    } catch (e) {
        console.error('[UI Update] Error:', e);
    }
}

// Backward compatibility wrapper
function triggerUIUpdate() {
    wsPendingUIUpdate = true;
    scheduleUIUpdate(true);
}

// ================================================================
// VIEW SWITCH HANDLING
// ================================================================
function handleViewSwitch(newViewType) {
    wsViewSwitchPending = true;
    wsCurrentViewType = newViewType;

    if (wsUIUpdateTimer) {
        clearTimeout(wsUIUpdateTimer);
        wsUIUpdateTimer = null;
    }

    // ── Clear pending stale batch on view switch ──
    // When switching Card ↔ List, all data is already in wsLiveData.
    // No need to fire GetLiveValue — the re-render uses WS data directly.
    if (_staleBatchTimer) { clearTimeout(_staleBatchTimer); _staleBatchTimer = null; }
    _stalePendingSet = {};

    setTimeout(function () {
        wsViewSwitchPending = false;
        wsPendingUIUpdate = true;
        scheduleUIUpdate(true);
    }, 50);
}

// ================================================================
// OPTIMIZED INCREMENTAL UPDATE FUNCTIONS
// ================================================================

// Optimized RDPMS update
function updateRDPMSViewIncremental(assetIds) {
    var $container = $('#rdpmsMainContainer');

    if ($container.length === 0) {
        renderRDPMSView();
        return;
    }

    var updatedIds = assetIds || Object.keys(wsUpdatedAssets);
    var cardsToUpdate = [];
    var cardsToCreate = [];

    // Sort into buckets
    for (var i = 0; i < updatedIds.length; i++) {
        var aid = String(updatedIds[i]);

        if (!isAssetInBulkWhitelist(aid)) {
            delete wsLiveData[aid];
            delete wsUpdatedAssets[aid];
            $('#rdpmsCard_' + aid).remove();
            delete rdpmsCardsBuilt[aid];
            continue;
        }

        if (!wsLiveData[aid]) continue;

        if (rdpmsCardsBuilt[aid]) {
            cardsToUpdate.push(aid);
        } else {
            cardsToCreate.push(aid);
        }
    }

    // Create new cards
    for (var j = 0; j < cardsToCreate.length; j++) {
        var aid = cardsToCreate[j];
        var assetName = (wsLiveData[aid].AssetName || '').toLowerCase();
        if (assetName.indexOf('sh') > -1) {
            buildShuntSignalCard(aid);
        } else {
            buildMainSignalCard(aid);
        }
        rdpmsCardsBuilt[aid] = getCardFingerprint(aid);
    }

    // Update all cards
    var allCards = cardsToCreate.concat(cardsToUpdate);
    for (var k = 0; k < allCards.length; k++) {
        var aid = allCards[k];
        var assetName = (wsLiveData[aid].AssetName || '').toLowerCase();
        if (assetName.indexOf('sh') > -1) {
            updateShuntSignalLights(aid);
        } else {
            updateMainSignalLights(aid);
        }
    }

    // Reorder once
    if (typeof window.reorderRdpmsCardsByAspect === 'function') {
        window.reorderRdpmsCardsByAspect();
    }
}

// Optimized PM update
function updatePMViewIncremental(assetIds) {
    var $container = $('#pmMainContainer');

    if ($container.length === 0) {
        renderPointMachineView();
        return;
    }

    var updatedIds = assetIds || Object.keys(wsUpdatedAssets);
    var cardsToUpdate = [];
    var cardsToCreate = [];

    for (var i = 0; i < updatedIds.length; i++) {
        var aid = updatedIds[i];
        if (!wsLiveData[aid]) continue;

        if (pmCardsBuilt[aid]) {
            cardsToUpdate.push(aid);
        } else {
            cardsToCreate.push(aid);
        }
    }

    // Create new cards
    for (var j = 0; j < cardsToCreate.length; j++) {
        buildPmCard(cardsToCreate[j]);
        pmCardsBuilt[cardsToCreate[j]] = true;
    }

    // Update all cards
    var allCards = cardsToCreate.concat(cardsToUpdate);
    for (var k = 0; k < allCards.length; k++) {
        updatePmCard(allCards[k]);
    }
}

// Optimized Table update
function incrementalTableUpdate(assetIds, isTrack) {
    var $tbody = $('#wsLiveTable tbody');
    if (!$tbody.length) return;

    var newRowsHtml = [];
    var rowsToUpdate = [];

    for (var i = 0; i < assetIds.length; i++) {
        var aid = assetIds[i];
        var asset = wsLiveData[aid];
        if (!asset) continue;

        var $row = $tbody.find('tr[data-id="' + aid + '"]');

        if (!$row.length) {
            newRowsHtml.push(buildTableRow(aid, isTrack, true));
        } else if (wsUpdatedAssets[aid]) {
            rowsToUpdate.push({ $row: $row, asset: asset });
        }
    }

    // Batch append
    if (newRowsHtml.length > 0) {
        $tbody.append(newRowsHtml.join(''));
    }

    // Batch update
    for (var j = 0; j < rowsToUpdate.length; j++) {
        updateRowCells(rowsToUpdate[j].$row, rowsToUpdate[j].asset, isTrack);
    }
}


// --- REPLACE processItems() with this version ---
// This function is now called processItemsInternal and only updates data structures
// UI updates are handled by triggerUIUpdate() after queue processing completes
window.processItemsInternal = processItemsInternal;
function processItemsInternal(items) {
    var atFilter = wsCurrentAssetTypeId;
    var aidFilter = wsCurrentFilterAssetIds;
    var pendingPmOps = {}; // keyed by assetId_opType_ts -- deduped per batch
    var processedCount = 0;
    var skippedByType = 0;
    var skippedByAsset = 0;
    var newColumnsAdded = false;
    var newAssetsAdded = false;

    // Debug: Log filter settings
    if (wsBatchCount <= 5) {
        console.log('[processItemsInternal] Filters:', {
            assetTypeFilter: atFilter,
            assetIdFilter: aidFilter,
            itemCount: items.length
        });
    }

    for (var i = 0; i < items.length; i++) {
        var d = items[i];
        var aid = d.AssetId;
        var attrName = d.AssetAttributeName;
        var attrId = d.AssetAttributeId;

        if (!aid || !attrName) {
            continue;
        }

        // FIX: was `length > 1` — skipped the filter entirely when exactly 1 asset was
        // selected, letting WS data from other assets pollute wsAttributeNames with
        // unvalidated attribute columns. Changed to `length > 0` so a single-asset
        // selection is filtered just like a multi-asset selection.
        if (aidFilter && aidFilter.length > 0 && aidFilter[0] !== '' && aidFilter[0] !== '0') {
            var incomingAid = String(aid);

            if (aidFilter.map(String).indexOf(incomingAid) === -1) {
                console.warn('[Asset Filter Skip]', {
                    incomingAssetId: incomingAid,
                    incomingAssetName: d.AssetName,
                    selectedAssetIds: aidFilter
                });

                skippedByAsset++;
                continue;
            }
        }

        // ── BULK WHITELIST GUARD ──
        // Reject any asset not returned by GetBulkAssetMetadata / GetAssestBy.
        // Without this, the site-wide WS stream creates garbage rows for assets
        // (wrong sub-type / stale ids) whose name only resolves to "Asset <id>"
        // because they have no bulk metadata entry. Skip BEFORE creating the
        // asset in wsLiveData or registering its attribute columns.
        if (!isAssetInBulkWhitelist(aid)) {
            if (!wsLiveData[aid]) {
                console.warn('[Asset Whitelist Skip] not in bulk response — ignoring', {
                    assetId: String(aid),
                    assetName: d.AssetName
                });
            }
            skippedByAsset++;
            continue;
        }

        wsMessageCount++;
        processedCount++;

        // Check if this is a new asset
        var isNewAsset = !wsLiveData[aid];
        if (isNewAsset) {
            newAssetsAdded = true;
            wsLiveData[aid] = {
                AssetId: aid,
                AssetName: (typeof getBulkAssetName === 'function' ? getBulkAssetName(aid, d.AssetName) : (d.AssetName || ('Asset ' + aid))),
                AssetTypeId: (typeof getBulkAssetTypeId === 'function' ? getBulkAssetTypeId(aid, d.AssetTypeId) : d.AssetTypeId),
                SiteId: (typeof getBulkSiteId === 'function' ? getBulkSiteId(aid, d.SiteId) : d.SiteId),
                attrs: {},
                dlRelays: {},
                lastUpdated: new Date(),
                ZeroOffsetValue: getZeroOffsetForAsset(aid) // Initialize with cached or default value
            };
            console.log('[processItemsInternal] New asset created:', aid, d.AssetName);

            // ── ZeroOffset hydration (lazy) ─────────────────────────
            // The old code here fired GetZeroOffsetValue on EVERY new
            // asset on EVERY reconnect — so the first WS frame with
            // 39 new assets meant 39 parallel AJAX calls. That was
            // the "calling recursively" / "very time consuming" the
            // user reported.
            //
            // Behaviour now:
            //   • If the value is already in zeroOffsetCache (e.g. a
            //     previous session fetched it), hydrate inline. No
            //     AJAX.
            //   • If NOT cached, do NOTHING here. The signal/PM card
            //     render paths (lines ~3293, ~3527, ~12616) will
            //     trigger a single fetch for the assets they actually
            //     paint, via the guarded fetchZeroOffsetForAsset().
            //     Assets that never render never trigger a fetch.
            //
            //   • Multiple render paths hitting the same asset are
            //     coalesced by GUARD 2 inside fetchZeroOffsetForAsset
            //     (in-flight check), so the total AJAX count equals
            //     the number of UNIQUE assets actually displayed —
            //     not the total in the WS subscription.
            (function (capturedAid) {
                var _cached = zeroOffsetCache[capturedAid];
                if (_cached && _cached.fetched) {
                    if (wsLiveData[capturedAid]) {
                        wsLiveData[capturedAid].ZeroOffsetValue = _cached.value;
                    }
                    if (window.updateMainSignalLights) {
                        window.updateMainSignalLights(capturedAid);
                    }
                }
                // No cache miss handling — render paths will fetch
                // on-demand for assets that are actually displayed.
            })(aid);
        }

        // Handle DataLogger type
        if (d.DataType && d.DataType === 'DataLogger') {
            // Use TimestampDevice as primary timestamp
            var timestamp = d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString();

            // Check if this is newer data
            var existingAttr = wsLiveData[aid].attrs[attrName];
            if (existingAttr && existingAttr.TimestampDevice && d.TimestampDevice) {
                var existingTs = new Date(existingAttr.TimestampDevice).getTime();
                var newTs = new Date(d.TimestampDevice).getTime();
                if (newTs > 0 && newTs <= existingTs) {
                    continue; // Skip older data
                }
            }

            if (typeof processWsDataloggerAttr === 'function') {
                processWsDataloggerAttr(aid, d.AssetName, attrName, d.Value, timestamp);
            }
            continue;
        }

        // Track new columns
        if (wsAttributeNames.indexOf(attrName) === -1) {
            newColumnsAdded = true;
            wsAttributeNames.push(attrName);
            wsAttributeOrder[attrName] = attrId || 9999;
            if (typeof wsAttributeIds !== 'undefined') {
                wsAttributeIds[attrName] = attrId;
            }
        }

        // ===== VIBRATION DATA CAPTURE =====
        // IMPORTANT: Must be done BEFORE the timestamp-skip check below so that
        // vibration attributes are always captured even when the main wsLiveData
        // update is skipped due to an older/epoch TimestampDevice.
        if (attrName && attrName.toLowerCase().indexOf('vibration') !== -1) {
            var vts = d.TimestampDevice || d.TimestampLocal || d.TimestampEdgeX || null;
            // Ignore epoch/null timestamps (year 0001) -- treat as no timestamp
            var vtsMs = vts ? new Date(vts).getTime() : 0;
            var isEpoch = (vtsMs < 0); // year 0001 parses to large negative ms
            if (isEpoch) { vts = null; vtsMs = 0; }
            if (!wsVibrationData[aid]) {
                wsVibrationData[aid] = { assetName: d.AssetName || ('Asset ' + aid), lastTimestamp: null, attrs: {} };
            }
            var existingVib = wsVibrationData[aid].attrs[attrName];
            var vibIsNewer = true;
            if (existingVib && existingVib.timestamp && vts) {
                var existingVibMs = new Date(existingVib.timestamp).getTime();
                vibIsNewer = (vtsMs >= existingVibMs);
            }
            // Always capture if no existing entry, or if this has a real (non-epoch) value
            if (vibIsNewer || !existingVib) {
                wsVibrationData[aid].attrs[attrName] = { value: d.Value, timestamp: vts };
                // Update lastTimestamp only if this is a real (non-epoch) timestamp
                if (vts) wsVibrationData[aid].lastTimestamp = vts;
                wsVibrationData[aid].assetName = d.AssetName || wsVibrationData[aid].assetName;
                // Live-update modal if open for this asset
                if (typeof fnUpdateVibrationModal === 'function') {
                    fnUpdateVibrationModal(aid);
                }
            }
        }

        // ===== TIMESTAMP DEVICE CHECK =====
        // Only process if TimestampDevice is newer than existing (ensures LIVE data only)
        var existingAttr = wsLiveData[aid].attrs[attrName];
        var incomingTimestampDevice = d.TimestampDevice || null;


        if (existingAttr && existingAttr.TimestampDevice && incomingTimestampDevice) {
            var existingDeviceTs = new Date(existingAttr.TimestampDevice).getTime();
            var newDeviceTs = new Date(incomingTimestampDevice).getTime();

            if (newDeviceTs < existingDeviceTs) {
                continue; // Skip strictly older data only
            }
            // Same timestamp allowed -- value may have changed (aspect transition drop to 0)
        }
        // Update attribute value - USE TimestampDevice as primary
        var prevVal = existingAttr ? existingAttr.Value : undefined;
        var hasChanged = (prevVal !== undefined && prevVal !== d.Value);

        wsLiveData[aid].attrs[attrName] = {
            Value: d.Value,
            AttrId: attrId,
            AssetAttributeId: d.AssetAttributeId || attrId,  // Store AssetAttributeId for operation ID matching
            Timestamp: d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString(),
            TimestampDevice: d.TimestampDevice || null,
            TimestampLocal: d.TimestampLocal || null,
            TimestampEdgeX: d.TimestampEdgeX || null,
            Source: d.DataSource || 'WS',
            changed: hasChanged,
            prevValue: prevVal
        };
        // NOTE: checkStaleForAsset moved to post-loop (see below) so it fires
        // once per unique asset, not once per attribute message.

        wsLiveData[aid].lastUpdated = new Date();

        wsUpdatedAssets[aid] = true;

        // ===== PM OPERATION-BASED ROW INSERTION =====
        // Check if this is PointMachine DataType with an operation ID
        //if (d.DataType === 'PointMachine' && typeof isPmOperationId === 'function' && isPmOperationId(attrId)) {
        //    processPmOperationMessage(aid, attrId, d.Value, d.TimestampDevice, d);
        //}

        // ===== PM OPERATION-BASED ROW INSERTION =====
        // Collect pending PM operations -- do NOT build rows mid-loop.
        // Reason: a batch of 66 messages may contain 20+ attributes all sharing
        // the same TimestampDevice (e.g. 1001-1006, 2002, 3001-3006, 4001-4006).
        // If we fire buildPmOperationRow on the first attribute, all other
        // attributes for the same timestamp haven't been stored yet → row shows --.
        // Instead, collect unique (assetId + operationType + timestamp) keys and
        // build rows AFTER the full loop, when wsLiveData has all values.
        if (d.DataType === 'PointMachine' && typeof isPmOperationId === 'function' && isPmOperationId(attrId)) {
            var opType = getPmOperationType(attrId);
            var opTs = d.TimestampDevice || '';
            if (opType && opTs && opTs !== '0001-01-01T00:00:00+00:00') {
                // Key by assetId+operationType ONLY (no timestamp in key).
                // Multiple motor groups in the same direction can have slightly
                // different TimestampDevice values -- we always want the LATEST
                // one so the row date matches the most recent motor group data.
                var opKey = aid + '_' + opType;
                var existing = pendingPmOps[opKey];
                if (!existing) {
                    pendingPmOps[opKey] = { assetId: aid, operationType: opType, timestampDevice: opTs };
                } else {
                    // Keep the latest TimestampDevice for this direction
                    var existingMs = 0, newMs = 0;
                    try { existingMs = new Date(existing.timestampDevice).getTime(); } catch (e) { }
                    try { newMs = new Date(opTs).getTime(); } catch (e) { }
                    if (newMs > existingMs) {
                        pendingPmOps[opKey].timestampDevice = opTs;
                    }
                }
            }
        }
    }

    // ===== FLUSH PM PENDING OPERATIONS =====
    // All attrs for this batch are now stored in wsLiveData.
    // Safe to build/update rows -- data is complete.

    // ===== INITIAL-LOAD STABILISATION =====
    // If this batch added NEW assets, the initial dump is still in progress.
    // Reset the stabilisation timer. Only when no new assets arrive for
    // _WS_INITIAL_STABILISE_MS do we consider the dump complete.
    if (newAssetsAdded) {
        if (_wsInitialLoadTimer) clearTimeout(_wsInitialLoadTimer);
        _wsInitialLoadTimer = setTimeout(function () {
            _wsInitialLoadTimer = null;
            _markInitialLoadComplete();
        }, _WS_INITIAL_STABILISE_MS);
    } else if (!_wsInitialLoadComplete && processedCount > 0) {
        // Batch with only attribute updates (no new assets) — if the timer
        // hasn't started yet, start it now.
        if (!_wsInitialLoadTimer) {
            _wsInitialLoadTimer = setTimeout(function () {
                _wsInitialLoadTimer = null;
                _markInitialLoadComplete();
            }, _WS_INITIAL_STABILISE_MS);
        }
    }

    // ===== BATCHED STALE CHECK =====
    // Only runs AFTER initial WS dump is complete AND only for small
    // incremental patches (≤ STALE_PATCH_THRESHOLD assets updated).
    // Large batches (bulk WS data) are skipped — they are NOT stale.
    if (_wsInitialLoadComplete) {
        var _staleAssets = Object.keys(wsUpdatedAssets);

        // GUARD: If this batch updated many assets at once, it is bulk
        // WS data, NOT a small incremental patch. Skip stale checks.
        if (_staleAssets.length <= STALE_PATCH_THRESHOLD) {
            var _inSignalList = (typeof _isSignalListView === 'function' && _isSignalListView());

            for (var _si = 0; _si < _staleAssets.length; _si++) {
                var _staleAid = _staleAssets[_si];

                // Signal List view: immediate single-asset re-check when stale data shown
                if (_inSignalList && wsStaleAttrs[_staleAid]) {
                    var _hasStale = false;
                    for (var _sk in wsStaleAttrs[_staleAid]) {
                        if (wsStaleAttrs[_staleAid][_sk]) { _hasStale = true; break; }
                    }
                    if (_hasStale) {
                        _fetchLiveValueAndCheckStale(_staleAid);
                        continue; // skip debounced batch for this asset
                    }
                }

                checkStaleForAsset(_staleAid);
            }
        }
        //else {
        //    console.log('[Stale] Skipping stale check — bulk batch (' + _staleAssets.length + ' assets > threshold ' + STALE_PATCH_THRESHOLD + ')');
        //}

        else {
            console.log('[Stale] Large batch (' + _staleAssets.length + ' assets) – forcing stale check anyway');
            // proceed with stale check
            var _inSignalList = (typeof _isSignalListView === 'function' && _isSignalListView());
            for (var _si = 0; _si < _staleAssets.length; _si++) {
                var _staleAid = _staleAssets[_si];
                if (_inSignalList && wsStaleAttrs[_staleAid] && hasStale) {
                    _fetchLiveValueAndCheckStale(_staleAid);
                    continue;
                }
                checkStaleForAsset(_staleAid);
            }
        }
    }

    if ($('#drpView').val() === 'PointMachine' && (wsCurrentAssetTypeId == 3 || wsCurrentAssetTypeId === '3')) {
        var _pmKeys = Object.keys(pendingPmOps);
        for (var _pi = 0; _pi < _pmKeys.length; _pi++) {
            var _op = pendingPmOps[_pmKeys[_pi]];
            processPmOperationMessage(_op.assetId, _op.operationType, _op.timestampDevice);
        }
    }

    // Debug logging
    if (wsBatchCount <= 10 || processedCount > 0) {
        console.log('[processItemsInternal] Result:', {
            processed: processedCount,
            skippedByType: skippedByType,
            skippedByAsset: skippedByAsset,
            totalAssets: Object.keys(wsLiveData).length,
            updatedAssets: Object.keys(wsUpdatedAssets).length,
            queueRemaining: wsMessageQueue.length
        });
    }

    if (processedCount === 0) {
        // If everything was filtered out, log a warning
        if (skippedByType > 0 || skippedByAsset > 0) {
            console.warn('[processItemsInternal] All items filtered out. Check asset type/ID filters.');
        }
        return;
    }

    wsAttributeNames.sort(function (a, b) {
        if (parseInt(wsCurrentAssetTypeId) === 1) {
            var ta = getTrackSortOrder(a), tb = getTrackSortOrder(b);
            if (ta !== tb) return ta - tb;
        }
        return (wsAttributeOrder[a] || 9999) - (wsAttributeOrder[b] || 9999);
    });

    // NOTE: UI updates are now handled by triggerUIUpdate() after queue processing
    // This prevents race conditions when multiple batches arrive rapidly
}

// Wrapper function for backward compatibility - calls the queue system
function processItems(items) {
    queueWsMessages(items);
    if (typeof window.sipRefresh === 'function') window.sipRefresh();
}


// ================================================================
// FIX 4: Updated connectWebSocket to use the enhanced handlers
// ================================================================

function connectWebSocket(siteId, assetTypeId, assetIds) {
    // ── Idempotency guard: if already connected/connecting to the SAME URL, do nothing ──
    var newWsUrl = buildWebSocketUrl(siteId, assetTypeId, assetIds);
    if (wsConnection && wsConnection.url === newWsUrl &&
        (wsConnection.readyState === WebSocket.OPEN || wsConnection.readyState === WebSocket.CONNECTING)) {
        console.log('[WS] Already connected to', newWsUrl, '-- skipping reconnect');
        return;
    }

    // Disconnect existing connection
    disconnectWebSocket();

    // Reset state
    wsLiveData = {};
    wsAttributeNames = [];
    wsAttributeOrder = {};
    wsAssetOrder = [];
    wsAssetOrderInitialized = false;
    wsMessageCount = 0;
    wsBatchCount = 0;
    wsUpdatedAssets = {};
    wsStaleAttrs = {};
    _assetInfoListCache = {};
    _signalGroupCache = {};
    _sigAssetInfoCache = {};  // Clear signal attr cache — stale loading states must not survive reconnect
    // Reset initial-load gate and stale timers
    _wsInitialLoadComplete = false;
    if (_wsInitialLoadTimer) { clearTimeout(_wsInitialLoadTimer); _wsInitialLoadTimer = null; }
    if (_staleBatchTimer) { clearTimeout(_staleBatchTimer); _staleBatchTimer = null; }
    _stalePendingSet = {};
    stopAutoStaleCheck();
    if (_sigRenderTimer) { clearTimeout(_sigRenderTimer); _sigRenderTimer = null; }
    _sigRenderActive = false;
    rdpmsCardsBuilt = {};
    pmCardsBuilt = {};
    pmEventHistory = {};
    pmLastOperationTimestamp = {};
    pmLastOperationType = {};
    _sigDomCache = {};  // Clear signal DOM cache -- all cards will be rebuilt

    // Reset queue system state
    wsMessageQueue = [];
    wsIsProcessingQueue = false;
    wsLastProcessedTime = 0;
    wsLastUIUpdateTime = 0;
    wsPendingUIUpdate = false;
    wsStreamingActive = false;
    wsViewSwitchPending = false;
    wsRAFScheduled = false;
    wsCurrentViewType = $('#drpView').val();
    wsProcessedPerSecond = 0;
    wsMessagesThisSecond = 0;
    wsLastSecondTime = Date.now();

    wsCurrentColumns = []; wsTableInitialized = false; window.pmTableMode = false;
    wsCurrentAssetTypeId = assetTypeId;
    wsCurrentFilterAssetIds = assetIds || [];
    wsLastMessageTime = Date.now();
    wsConnParams = { siteId: siteId, assetTypeId: assetTypeId, assetIds: assetIds || [] };



    var wsUrl = buildWebSocketUrl(siteId, assetTypeId, assetIds);

    // SSL auth — on HTTPS the server expects ?token=<WebSocketAuthToken>
    // for the WS upgrade handshake. Without it the connection is rejected.
    // isSecure + APP_CONFIG.WebSocketAuthToken are set by the Razor block in Index.cshtml.
    if (typeof isSecure !== 'undefined' && isSecure && APP_CONFIG && APP_CONFIG.WebSocketAuthToken) {
        var separator = wsUrl.indexOf('?') > -1 ? '&' : '?';
        wsUrl = wsUrl + separator + 'token=' + encodeURIComponent(APP_CONFIG.WebSocketAuthToken);
    }

    console.log('[WS] Connecting to:', wsUrl.replace(/:\/\/[^@]+@/, '://***:***@'));
    console.log('[WS] Filters:', { siteId: siteId, assetTypeId: assetTypeId, assetIds: assetIds });

    // SIP-only connection: when assetTypeId is null/empty, this is a background
    // connection for the schematic view — do NOT show status bar, toast, or
    // overwrite #divTelemetryLive. The user hasn't clicked Search yet.
    var _isSipOnly = (!assetTypeId || assetTypeId === '' || assetTypeId === '0' || assetTypeId === 0);

    // Global gate: SIP-only connections must NOT render data into the table/cards.
    // Only user-initiated Search (which passes a real assetTypeId) should render.
    window._wsSipOnlyMode = _isSipOnly;

    if (!_isSipOnly) {
        showWsStatus('connecting', wsUrl);
    }

    try {
        wsConnection = new WebSocket(wsUrl);
        wsConnection.onopen = function () {
            console.log('[WS] Connected successfully');
            wsIsConnected = true;
            wsReconnectAttempts = 0;

            wsLastMessageTime = Date.now();

            if (!_isSipOnly) {
                showWsStatus('connected');
                showSuccess('Live data streaming started', 'WebSocket Connected');
            } else {
                console.log('[WS] SIP-only connection — UI updates suppressed');
            }

            // Don't overwrite the container for Circuit/Graph/Yard views
            // Also don't overwrite for SIP-only connections (user hasn't searched yet)
            var currentViewType = $('#drpView').val();
            if (!_isSipOnly && currentViewType !== 'Circuit' && currentViewType !== 'Graph' && currentViewType !== 'Yard') {
                $('#divTelemetryLive').html(
                    '<div class="text-center p-5" id="wsWaiting">' +
                    '<i class="fas fa-satellite-dish fa-2x mb-3" style="color:#10b981;"></i>' +
                    '<div style="color:#64748b;">Connected! Waiting for live data...</div>' +
                    '</div>'
                );
            } else {
                console.log('[WS] Skipping container reset for ' + currentViewType + ' view (already loaded via AJAX)');
            }

            // Safety interval to force render if data arrives but render doesn't trigger
            if (wsSafetyInterval) clearInterval(wsSafetyInterval);
            wsSafetyInterval = setInterval(function () {
                if (Object.keys(wsLiveData).length > 0 && $('#wsWaiting').length > 0) {
                    console.log('[WS Safety] Data exists but view not rendered, forcing render...');
                    var viewType = $('#drpView').val();
                    if (viewType === 'Table') renderWsTable();
                    else if (viewType === 'RDPMS') renderRDPMSView();
                    else if (viewType === 'PointMachine') renderPointMachineView();
                }
            }, 2000);

            // Heartbeat to detect truly broken connections (NOT idle/no-data situations)
            if (wsHeartbeatInterval) clearInterval(wsHeartbeatInterval);
            wsHeartbeatInterval = setInterval(function () {
                if (!wsIsConnected || !wsConnection) return;

                var socketOpen = wsConnection && wsConnection.readyState === WebSocket.OPEN;
                if (socketOpen) {
                    // Socket is alive -- server just has no new data. Stay connected.
                    // (Do NOT reset wsLastMessageTime here -- that would mask a stalled stream.)
                    return;
                }

                // Socket is broken -- reconnect
                console.warn('[WS] Heartbeat: socket NOT open (state=' +
                    (wsConnection ? wsConnection.readyState : 'null') + ') -- reconnecting...');
                showWsStatus('reconnecting');
                if (wsConnection) {
                    wsConnection.onclose = null;
                    wsConnection.onerror = null;
                    try { wsConnection.close(); } catch (e) { /* ignore */ }
                    wsConnection = null;
                }
                wsIsConnected = false;
                wsReconnectAttempts++;
                connectWebSocket(wsConnParams.siteId, wsConnParams.assetTypeId, wsConnParams.assetIds);
            }, 10000);
        };

        // Use enhanced message handler
        wsConnection.onmessage = function (event) {
            wsLastMessageTime = Date.now();

            // ── GATE: SIP-only connections (no asset type) must not process
            //    data into wsLiveData or trigger any rendering. The SIP
            //    schematic uses its own separate WebSocket via
            //    SipTelemetry.connectToSite(). ──
            if (window._wsSipOnlyMode) return;

            try {
                var rawData = event.data;

                // Debug first few messages
                if (wsBatchCount < 5) {
                    console.log('[WS RAW #' + (wsBatchCount + 1) + ']',
                        typeof rawData === 'string' ? rawData.substring(0, 300) : rawData);
                }

                var batch;

                if (typeof rawData === 'string') {
                    try {
                        batch = JSON.parse(rawData);
                    } catch (e) {
                        console.error('[WS] JSON parse error:', e.message);
                        return;
                    }
                } else {
                    batch = rawData;
                }

                wsBatchCount++;

                // Log structure of first batches
                if (wsBatchCount <= 5) {
                    console.log('[WS Batch #' + wsBatchCount + ']', {
                        type: typeof batch,
                        isArray: Array.isArray(batch),
                        keys: batch && typeof batch === 'object' ? Object.keys(batch).slice(0, 10) : [],
                        sample: batch && batch[0] ? batch[0] : (batch && batch.Messages && batch.Messages[0] ? batch.Messages[0] : null)
                    });
                }

                // Process different formats — route through window.* so the
                // parseBatchMessages override + sip-telemetry.js bridge fire.
                var _pbm2 = window.parseBatchMessages || parseBatchMessages;
                if (Array.isArray(batch) && batch.length > 0) {
                    _pbm2(batch);
                }
                else if (batch && Array.isArray(batch.Messages) && batch.Messages.length > 0) {
                    _pbm2(batch.Messages);
                }
                else if (batch && Array.isArray(batch.Data) && batch.Data.length > 0) {
                    _pbm2(batch.Data);
                }
                else if (batch && batch.AssetId !== undefined && batch.AssetAttributeName) {
                    processSingleLiveUpdate(batch);
                }
                else {
                    // Try to find any array in the object
                    if (batch && typeof batch === 'object') {
                        for (var key in batch) {
                            if (Array.isArray(batch[key]) && batch[key].length > 0) {
                                console.log('[WS] Found array at key:', key);
                                _pbm2(batch[key]);
                                return;
                            }
                        }
                    }
                    if (wsBatchCount <= 5) {
                        console.warn('[WS] Unknown format:', batch);
                    }
                }

            } catch (e) {
                console.error('[WS] Message error:', e);
            }
        };

        wsConnection.onclose = function (event) {
            console.log('[WS] Closed:', event.code, event.reason);
            wsIsConnected = false;

            if (event.code !== 1000 && wsReconnectAttempts < MAX_RECONNECT_ATTEMPTS) {
                wsReconnectAttempts++;
                var delay = RECONNECT_DELAY * Math.min(wsReconnectAttempts, 5);
                showWsStatus('reconnecting');
                wsReconnectTimer = setTimeout(function () {
                    connectWebSocket(wsConnParams.siteId, wsConnParams.assetTypeId, wsConnParams.assetIds);
                }, delay);
            } else if (wsReconnectAttempts >= MAX_RECONNECT_ATTEMPTS) {
                showWsStatus('error');
                showError('Connection failed after ' + MAX_RECONNECT_ATTEMPTS + ' attempts.', 'WebSocket');
            } else {
                showWsStatus('error');
            }
        };

        wsConnection.onerror = function (err) {
            console.error('[WS] Error:', err);
            wsIsConnected = false;
            showWsStatus('error');
        };

    } catch (e) {
        console.error('[WS] Connection failed:', e);
        showError('Connection failed', 'WebSocket');
        showWsStatus('error');
    }
}
function fmtTime(d) { if (!d) return '-'; return d.getHours().toString().padStart(2, '0') + ':' + d.getMinutes().toString().padStart(2, '0') + ':' + d.getSeconds().toString().padStart(2, '0'); }

// ================================================================
// OPERATION ID BASED TIMESTAMP HELPER
// Returns TimestampDevice from attributes that have operation IDs
// Normal operation IDs: 1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002
// Reverse operation IDs: 6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002
// These are AssetAttributeId values from the WebSocket JSON data
// ================================================================
var TELEMETRY_NORMAL_OPERATION_IDS = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002];
var TELEMETRY_REVERSE_OPERATION_IDS = [6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];
var TELEMETRY_ALL_OPERATION_IDS = TELEMETRY_NORMAL_OPERATION_IDS.concat(TELEMETRY_REVERSE_OPERATION_IDS);

function getOperationTimestampDevice(asset) {
    if (!asset || !asset.attrs) return null;

    var latestTs = null;
    var latestTime = 0;

    // Iterate through all attributes to find those with operation AssetAttributeIds
    for (var attrName in asset.attrs) {
        var attr = asset.attrs[attrName];
        if (!attr || !attr.TimestampDevice) continue;

        // Skip invalid timestamps
        if (attr.TimestampDevice === '0001-01-01T00:00:00+00:00' ||
            attr.TimestampDevice.indexOf('0001-01-01') !== -1) continue;

        // Check AssetAttributeId (primary) or AttrId (fallback)
        var assetAttrId = parseInt(attr.AssetAttributeId || attr.AttrId || 0);

        if (TELEMETRY_ALL_OPERATION_IDS.indexOf(assetAttrId) !== -1) {
            try {
                var ts = new Date(attr.TimestampDevice).getTime();
                if (ts > latestTime && ts > 0) {
                    latestTime = ts;
                    latestTs = attr.TimestampDevice;
                }
            } catch (e) { }
        }
    }

    return latestTs;
}

// Format TimestampDevice for display (HH:mm:ss)
function fmtTimestampDevice(timestampDevice) {
    if (!timestampDevice) return '-';
    try {
        var d = new Date(timestampDevice);
        if (isNaN(d.getTime())) return '-';
        return d.getHours().toString().padStart(2, '0') + ':' +
            d.getMinutes().toString().padStart(2, '0') + ':' +
            d.getSeconds().toString().padStart(2, '0');
    } catch (e) {
        return '-';
    }
}

// Expose helper functions globally for use in override functions
window.getOperationTimestampDevice = getOperationTimestampDevice;
window.fmtTimestampDevice = fmtTimestampDevice;
window.TELEMETRY_ALL_OPERATION_IDS = TELEMETRY_ALL_OPERATION_IDS;

// ================================================================
// RDPMS SIGNAL VIEW (kept as-is from original)
// ================================================================
function renderRDPMSView() {
    if (typeof isSignalAssetType === 'function' && !isSignalAssetType()) {
        console.warn('[RDPMS] Not a Signal asset type – skipping render');
        return;
    }

    var assetIds = Object.keys(wsLiveData).filter(function (id) {
        var a = wsLiveData[id];
        if (!a) return false;
        // Defense-in-depth: never render an asset not in the bulk response.
        if (typeof isAssetInBulkWhitelist === 'function' && !isAssetInBulkWhitelist(id)) return false;
        if (a.AssetTypeId === undefined || a.AssetTypeId === null || a.AssetTypeId === '') return true;
        return parseInt(a.AssetTypeId) === 2;
    });
    console.log('[RDPMS View] Rendering. Signal assets:', assetIds.length);

    if (assetIds.length === 0) return;

    $('#wsWaiting').remove();

    // ================================================================
    // SIGNAL ASPECT DETECTION - Determines which aspect is currently active
    // ================================================================
    function getSignalAspect(assetId) {
        var asset = wsLiveData[assetId];
        if (!asset) return { aspect: 'INACTIVE', priority: 99, armCount: 0 };

        var attrs = asset.attrs || {};
        var dlRelays = asset.dlRelays || {};
        var threshold = parseFloat(asset.ZeroOffsetValue || 0.1);

        // Helper to get numeric value
        function val(name) {
            if (attrs[name] && attrs[name].Value !== null && attrs[name].Value !== undefined) {
                return parseFloat(attrs[name].Value) || 0;
            }
            return 0;
        }

        // Helper to check if relay is picked up (value = 1)
        function isRelayPickup(relayName) {
            for (var key in dlRelays) {
                if (key.toUpperCase().indexOf(relayName.toUpperCase()) > -1) {
                    return dlRelays[key].isPickup === true || dlRelays[key].value === 1;
                }
            }
            return false;
        }

        // Count arms/routes (AUG, BUG, CUG, DUG, EUG)
        var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
        var armCount = 0;
        for (var r = 0; r < routeNames.length; r++) {
            if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) {
                armCount++;
            }
        }

        // Get current values
        var rgMa = val('RG mA');
        var dgMa = val('DG mA');
        var hgMa = val('HG mA');
        var hhgMa = val('HHG mA');

        // Check relay states
        var recrPickup = isRelayPickup('RECR');
        var decrPickup = isRelayPickup('DECR');
        var hecrPickup = isRelayPickup('HECR');
        var hhecrPickup = isRelayPickup('HHECR');

        // Determine aspect based on formula (same as updateMainSignalLights)
        // Priority: RED (1) > DOUBLE YELLOW (2) > SINGLE YELLOW (3) > GREEN (4) > INACTIVE (99)

        // RED: RECR=1 OR RG mA > threshold
        if (recrPickup || rgMa > threshold) {
            return { aspect: 'RED', priority: 1, armCount: armCount };
        }

        // DOUBLE YELLOW: (HECR=1 & HHECR=1) OR HHG mA > threshold
        if ((hecrPickup && hhecrPickup) || hhgMa > threshold) {
            return { aspect: 'DOUBLE_YELLOW', priority: 2, armCount: armCount };
        }

        // SINGLE YELLOW: HECR=1 OR HG mA > threshold
        if (hecrPickup || hgMa > threshold) {
            return { aspect: 'SINGLE_YELLOW', priority: 3, armCount: armCount };
        }

        // GREEN: DECR=1 OR DG mA > threshold
        if (decrPickup || dgMa > threshold) {
            return { aspect: 'GREEN', priority: 4, armCount: armCount };
        }

        // No aspect matched - INACTIVE (dark signal)
        return { aspect: 'INACTIVE', priority: 99, armCount: armCount };
    }

    // Separate main signals from shunt signals
    var mainSignals = [], shuntSignals = [];
    for (var i = 0; i < assetIds.length; i++) {
        var aid = assetIds[i];
        var name = (wsLiveData[aid].AssetName || '').toLowerCase();
        if (name.indexOf('sh') > -1) {
            shuntSignals.push(aid);
        } else {
            mainSignals.push(aid);
        }
    }


    function getSignalAspectCount(assetId) {
        var asset = wsLiveData[assetId];
        if (!asset) return 0;
        var attrs = asset.attrs || {};
        var count = 0;
        if (attrs['RG mA'] || attrs['RG V']) count++;  // Red
        if (attrs['DG mA'] || attrs['DG V']) count++;  // Green
        if (attrs['HG mA'] || attrs['HG V']) count++;  // Single Yellow
        if (attrs['HHG mA'] || attrs['HHG V']) count++;  // Double Yellow
        if (attrs['PILOT mA'] || attrs['PILOT V'] ||
            attrs['PILOTRoot mA'] || attrs['PILOTRoot V']) count++;  // Pilot
        if (attrs['Co_Hg mA'] || attrs['Co_Hg V']) count++;          // Calling
        var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
        for (var r = 0; r < routeNames.length; r++) {
            if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) count++;
        }
        return count;
    }

    var aspectSort = function (a, b) {
        // Blank-data signals (every value renders as '--') always sink to the
        // bottom, so cards with live readings lead and a dark/no-data card
        // (e.g. SH114) never appears first.
        var blankA = (typeof assetHasNoData === 'function') ? assetHasNoData(a) : false;
        var blankB = (typeof assetHasNoData === 'function') ? assetHasNoData(b) : false;
        if (blankA !== blankB) return blankA ? 1 : -1;

        // Then by aspect count ascending (fewer lights first)
        var countA = getSignalAspectCount(a);
        var countB = getSignalAspectCount(b);
        if (countA !== countB) {
            return countA - countB; // Ascending (fewer aspects first)
        }

        // Tie-break: natural name order
        return (wsLiveData[a].AssetName || '').localeCompare(
            wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }
        );
    };

    mainSignals.sort(aspectSort);
    shuntSignals.sort(aspectSort);
    mainSignals.sort(aspectSort);
    shuntSignals.sort(aspectSort);

    console.log('[RDPMS View] Sorted signals by aspect:',
        mainSignals.map(function (id) {
            return wsLiveData[id].AssetName + ':' + getSignalAspect(id).aspect;
        })
    );


    var $c = $('#divTelemetryLive');

    // Create container only if it doesn't exist
    if ($c.find('#rdpmsMainContainer').length === 0) {
        // ── CRITICAL: When the container is being recreated, the DOM cards
        // are GONE. Reset the build-tracking maps so the upcoming loop
        // actually rebuilds them (instead of skipping with "already built"
        // and producing an empty container). This is what makes the List→Card
        // toggle work without needing a fresh Search.
        rdpmsCardsBuilt = {};
        if (typeof _sigDomCache !== 'undefined') _sigDomCache = {};
        if (typeof _rdpmsDomCache !== 'undefined') _rdpmsDomCache = {};

        var containerHtml = '<div class="rdpms-container">' +
            '<div class="row" id="rdpmsMainContainer"></div>' +
            (shuntSignals.length > 0 ?
                '<hr style="margin:20px 0;border-color:#e2e8f0;">' +
                '<h6 style="padding:0 10px;color:#64748b;margin-bottom:15px;"><i class="fas fa-random"></i> Shunt Signals</h6>' +
                '<div class="row" id="rdpmsShuntContainer"></div>'
                : '<div class="row" id="rdpmsShuntContainer"></div>') +
            '</div>';
        $c.html(containerHtml);
    } else if ($c.find('#rdpmsShuntContainer').length === 0) {
        // Defensive: main container exists but shunt container is missing
        // (could happen if previous cleanup removed only shunt). Append
        // a fresh shunt container so buildShuntSignalCard's
        // $('#rdpmsShuntContainer').append(...) doesn't silently fail.
        var $rdpmsWrap = $c.find('.rdpms-container');
        if ($rdpmsWrap.length === 0) $rdpmsWrap = $c.find('#rdpmsMainContainer').parent();
        if (shuntSignals.length > 0) {
            $rdpmsWrap.append(
                '<hr style="margin:20px 0;border-color:#e2e8f0;">' +
                '<h6 style="padding:0 10px;color:#64748b;margin-bottom:15px;"><i class="fas fa-random"></i> Shunt Signals</h6>' +
                '<div class="row" id="rdpmsShuntContainer"></div>'
            );
        } else {
            $rdpmsWrap.append('<div class="row" id="rdpmsShuntContainer"></div>');
        }
    }

    // ================================================================
    // REORDER EXISTING CARDS based on new sort order
    // ================================================================
    var $mainContainer = $('#rdpmsMainContainer');
    if ($mainContainer.length > 0 && $mainContainer.children().length > 0) {
        // Detach all cards and re-append in sorted order
        var $cards = {};
        $mainContainer.children().each(function () {
            var cardId = $(this).attr('id');
            if (cardId) {
                var assetId = cardId.replace('rdpmsCard_', '');
                $cards[assetId] = $(this).detach();
            }
        });

        // Re-append in sorted order
        for (var mi = 0; mi < mainSignals.length; mi++) {
            var aid = mainSignals[mi];
            if ($cards[aid]) {
                $mainContainer.append($cards[aid]);
            }
        }
    }

    // Process main signals
    for (var mi = 0; mi < mainSignals.length; mi++) {
        var aid = mainSignals[mi];

        // Only build card if it doesn't exist
        if (!rdpmsCardsBuilt[aid] || $('#rdpmsCard_' + aid).length === 0) {
            buildMainSignalCard(aid);
            rdpmsCardsBuilt[aid] = getCardFingerprint(aid);
            // Initial light update
            updateMainSignalLights(aid);
        }
        // Only update lights if this asset was marked as updated
        else if (wsUpdatedAssets[aid]) {
            // Check if structure changed
            if (needsCardRebuild(aid)) {
                rebuildCard(aid);
            } else {
                updateMainSignalLights(aid);
            }
        }
    }

    // Reorder shunt signals container too
    if (shuntSignals.length > 0 && $('#rdpmsShuntContainer').length === 0) {
        var $shuntWrap = $c.find('.rdpms-container');
        if ($shuntWrap.length === 0) $shuntWrap = $('#rdpmsMainContainer').parent();
        if ($shuntWrap.length) {
            $shuntWrap.append(
                '<hr style="margin:20px 0;border-color:#e2e8f0;">' +
                '<h6 style="padding:0 10px;color:#64748b;margin-bottom:15px;">' +
                '<i class="fas fa-random"></i> Shunt Signals</h6>' +
                '<div class="row" id="rdpmsShuntContainer"></div>'
            );
        }
    }

    // Reorder shunt signals container too
    var $shuntContainer = $('#rdpmsShuntContainer');
    if ($shuntContainer.length > 0 && $shuntContainer.children().length > 0) {
        var $shuntCards = {};
        $shuntContainer.children().each(function () {
            var cardId = $(this).attr('id');
            if (cardId) {
                var assetId = cardId.replace('rdpmsCard_', '');
                $shuntCards[assetId] = $(this).detach();
            }
        });

        for (var si = 0; si < shuntSignals.length; si++) {
            var aid = shuntSignals[si];
            if ($shuntCards[aid]) {
                $shuntContainer.append($shuntCards[aid]);
            }
        }
    }

    // Process shunt signals
    for (var si = 0; si < shuntSignals.length; si++) {
        var aid = shuntSignals[si];

        // Only build card if it doesn't exist
        if (!rdpmsCardsBuilt[aid] || $('#rdpmsCard_' + aid).length === 0) {
            buildShuntSignalCard(aid);
            rdpmsCardsBuilt[aid] = getCardFingerprint(aid);
            // Initial light update
            updateShuntSignalLights(aid);
        }
        // Only update lights if this asset was marked as updated
        else if (wsUpdatedAssets[aid]) {
            // Check if structure changed
            if (needsCardRebuild(aid)) {
                rebuildCard(aid);
            } else {
                updateShuntSignalLights(aid);
            }
        }
    }

    // Add shunt section header if needed
    if (shuntSignals.length > 0 &&
        $c.find('#rdpmsShuntContainer').prev('h6').length === 0 &&
        $c.find('#rdpmsShuntContainer').children().length > 0) {
        $('#rdpmsShuntContainer').before(
            '<hr style="margin:20px 0;border-color:#e2e8f0;">' +
            '<h6 style="padding:0 10px;color:#64748b;margin-bottom:15px;">' +
            '<i class="fas fa-random"></i> Shunt Signals</h6>'
        );
    }

    // Clear the updated flags
    wsUpdatedAssets = {};
}
function buildMainSignalCard(assetId) {
    // Invalidate DOM cache -- card is being rebuilt, old element refs are now stale
    if (_sigDomCache) delete _sigDomCache[assetId];
    var asset = wsLiveData[assetId]; if (!asset) return; var attrs = asset.attrs; var name = asset.AssetName || 'Signal';
    var hasRG = !!(attrs['RG mA'] || attrs['RG V']); var hasDG = !!(attrs['DG mA'] || attrs['DG V']); var hasHG = !!(attrs['HG mA'] || attrs['HG V']); var hasHHG = !!(attrs['HHG mA'] || attrs['HHG V']); var hasPilot = !!(attrs['PILOT mA'] || attrs['PILOT V'] || attrs['PILOTRoot mA'] || attrs['PILOTRoot V']);
    var routes = []; var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
    for (var r = 0; r < routeNames.length; r++) { if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) routes.push(routeNames[r]); }
    var hasRoutes = routes.length > 0; var hasCalling = !!(attrs['Co_Hg mA'] || attrs['Co_Hg V']);
    var armPositions = ['rdpms-arm-1', 'rdpms-arm-2', 'rdpms-arm-3', 'rdpms-arm-4'];
    var siteIdForActions = asset.SiteId || $('#drpSite').val();
    var h = '<div class="col-xxl-4 col-xl-6 col-lg-6 col-md-12 col-sm-12 rdpms-signal-card" id="rdpmsCard_' + assetId + '">';
    h += '<div class="card card-border"><div class="card-header-signal bg-secondary"><div class="card-header-left" style="background:var(--primary)"><h6>Signal : ' + name + '<br/><span id="rdpmsTs_' + assetId + '" style="font-size:10px;opacity:0.7;">Updated: </span></h6></div><div class="card-header-right" style="background:var(--primary)"><div class="sig-card-actions"><button class="sig-action-btn sig-action-graph" onclick="fnGetAssetGraph(\'' + siteIdForActions + '\',\'' + assetId + '\')" title="Historical Graph"><i class="fa-solid fa-chart-line"></i></button><button class="sig-action-btn sig-action-circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')" title="Circuit Diagram"><i class="fa-solid fa-project-diagram"></i></button></div></div></div>';
    h += '<div class="card-body-signal"><div class="rdpms-sizeDiv"><div class="rdpms-signal-container">';
    h += '<div class="rdpms-routeSignal' + (hasRoutes ? ' has-routes' : '') + '">';
    if (hasRoutes) {
        h += '<div class="rdpms-centerSignal"><div class="rdpms-white-center light-off" id="rdpmsRootMiddle_' + assetId + '"></div></div>';
        for (var ri = 0; ri < routes.length; ri++) {
            var armClass = (routes.length === 1) ? 'rdpms-arm-single' : armPositions[ri] || armPositions[0];
            h += '<div class="rdpms-Signal ' + armClass + '"><span class="rdpms-signal-label">' + routes[ri] + '</span>';
            for (var d = 1; d <= 4; d++) h += '<div class="rdpms-white-always light-off" id="rdpmsRoute_' + assetId + '_' + routes[ri] + '_' + d + '"></div>';
            h += '</div>';
        }
    }
    if (hasHHG) h += '<div class="rdpms-yellow light-off" id="rdpmsHHG_' + assetId + '"></div>';
    if (hasDG) h += '<div class="rdpms-green light-off" id="rdpmsDG_' + assetId + '"></div>';
    if (hasHG) h += '<div class="rdpms-yellow light-off" id="rdpmsHG_' + assetId + '"></div>';
    if (hasRG) h += '<div class="rdpms-red light-off" id="rdpmsRG_' + assetId + '"></div>';
    h += '</div>';
    if (hasCalling) { h += '<div class="rdpms-routeSignal2"><div class="rdpms-yellow light-off" id="rdpmsCalling_' + assetId + '"></div><span>C</span></div>'; }
    h += '</div></div>';
    h += '<div class="rdpms-route-table"><table>';
    if (hasRG) h += '<tr id="rdpmsTrRG_' + assetId + '" style="display:none"><td id="rdpmsTdRGma_' + assetId + '" data-attr="RG mA"></td><td id="rdpmsTdRGv_' + assetId + '" data-attr="RG V"></td></tr>';
    if (hasDG) h += '<tr id="rdpmsTrDG_' + assetId + '" style="display:none"><td id="rdpmsTdDGma_' + assetId + '" data-attr="DG mA"></td><td id="rdpmsTdDGv_' + assetId + '" data-attr="DG V"></td></tr>';
    if (hasHG) h += '<tr id="rdpmsTrHG_' + assetId + '" style="display:none"><td id="rdpmsTdHGma_' + assetId + '" data-attr="HG mA"></td><td id="rdpmsTdHGv_' + assetId + '" data-attr="HG V"></td></tr>';
    if (hasHHG) h += '<tr id="rdpmsTrHHG_' + assetId + '" style="display:none"><td id="rdpmsTdHHGma_' + assetId + '" data-attr="HHG mA"></td><td id="rdpmsTdHHGv_' + assetId + '" data-attr="HHG V"></td></tr>';
    if (hasPilot) h += '<tr id="rdpmsTrPilot_' + assetId + '" style="display:none"><td id="rdpmsTdPilotma_' + assetId + '" data-attr="PILOT mA"></td><td id="rdpmsTdPilotv_' + assetId + '" data-attr="PILOT V"></td></tr>';
    if (hasCalling) h += '<tr id="rdpmsTrCO_' + assetId + '" style="display:none"><td id="rdpmsTdCOma_' + assetId + '" data-attr="Co_Hg mA"></td><td id="rdpmsTdCOv_' + assetId + '" data-attr="Co_Hg V"></td></tr>';
    if (hasRoutes) h += '<tr id="rdpmsTrRoute_' + assetId + '" style="display:none"><td id="rdpmsTdRoutema_' + assetId + '" data-attr="Route mA"></td><td id="rdpmsTdRoutev_' + assetId + '" data-attr="Route V"></td></tr>';
    // DPR, HPR, HHPR rows - shown based on DataLogger relay conditions
    h += '<tr id="rdpmsTrDPR_' + assetId + '" style="display:none"><td id="rdpmsTdDPRval_' + assetId + '" colspan="2"></td></tr>';
    h += '<tr id="rdpmsTrHPR_' + assetId + '" style="display:none"><td id="rdpmsTdHPRval_' + assetId + '" colspan="2"></td></tr>';
    h += '<tr id="rdpmsTrHHPR_' + assetId + '" style="display:none"><td id="rdpmsTdHHPRval_' + assetId + '" colspan="2"></td></tr>';
    h += '</table></div>';
    h += getDataloggerSectionHTML(assetId);
    h += '</div></div></div>';
    $('#rdpmsMainContainer').append(h);
    $('#rdpmsCard_' + assetId).hide().fadeIn(300);
}

function buildShuntSignalCard(assetId) {
    var asset = wsLiveData[assetId]; if (!asset) return; var name = asset.AssetName || 'Shunt';
    var siteIdForActions = asset.SiteId || $('#drpSite').val();
    var h = '<div class="col-xxl-4 col-xl-6 col-lg-6 col-md-12 col-sm-12 rdpms-signal-card" id="rdpmsCard_' + assetId + '">';
    h += '<div class="card card-border"><div class="card-header-signal" style="background:var(--primary)"><div class="card-header-left"><h6>Signal : ' + name + '<br/><span id="rdpmsTs_' + assetId + '" style="font-size:10px;opacity:0.7;">Updated: </span></h6></div>';
    h += '<div class="card-header-right" style="background:var(--primary)"><div class="sig-card-actions"><button class="sig-action-btn sig-action-graph" onclick="fnGetAssetGraph(\'' + siteIdForActions + '\',\'' + assetId + '\')" title="Historical Graph"><i class="fa-solid fa-chart-line"></i></button><button class="sig-action-btn sig-action-circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')" title="Circuit Diagram"><i class="fa-solid fa-project-diagram"></i></button></div></div></div>';
    h += '<div class="card-body-signal"><div class="rdpms-sizeShuntDiv"><div class="rdpms-shunt"><img class="trianglePng" src="/assets/images/shunt.png" />';
    h += '<div class="rdpms-white light-off" id="rdpmsShuntTop_' + assetId + '"></div><div class="rdpms-white light-off" id="rdpmsShuntRight_' + assetId + '"></div><div class="rdpms-white light-off" id="rdpmsShuntLeft_' + assetId + '"></div></div>';
    h += '<div class="rdpms-route-table"><table>';
    h += '<tr id="rdpmsTrOn_' + assetId + '" style="display:none"><td id="rdpmsTdOnma_' + assetId + '" data-attr="On Aspect mA"></td><td id="rdpmsTdOnv_' + assetId + '" data-attr="On Aspect V"></td></tr>';
    h += '<tr id="rdpmsTrOff_' + assetId + '" style="display:none"><td id="rdpmsTdOffma_' + assetId + '" data-attr="Off Aspect mA"></td><td id="rdpmsTdOffv_' + assetId + '" data-attr="Off Aspect V"></td></tr>';
    h += '<tr id="rdpmsTrShPilot_' + assetId + '" style="display:none"><td id="rdpmsTdShPilotma_' + assetId + '" data-attr="PILOT mA"></td><td id="rdpmsTdShPilotv_' + assetId + '" data-attr="PILOT V"></td></tr>';
    h += '</table></div>';
    h += getDataloggerSectionHTML(assetId);
    h += '</div></div></div></div>';
    $('#rdpmsShuntContainer').append(h);
    $('#rdpmsCard_' + assetId).hide().fadeIn(300);
}

// ================================================================
// REORDER RDPMS CARDS BY ASPECT - Debounced function
// Active signals first (RED > DOUBLE YELLOW > SINGLE YELLOW > GREEN)
// Within same aspect, sort by arm count (route signals first)
// Inactive/dark signals last
// ================================================================
var rdpmsReorderTimer = null;
window.reorderRdpmsCardsByAspect = function () {
    // Debounce: only reorder after 500ms of no updates
    if (rdpmsReorderTimer) {
        clearTimeout(rdpmsReorderTimer);
    }
    rdpmsReorderTimer = setTimeout(function () {
        rdpmsReorderTimer = null;
        doReorderRdpmsCards();
    }, 500);
};

function doReorderRdpmsCards() {
    var $mainContainer = $('#rdpmsMainContainer');
    var $shuntContainer = $('#rdpmsShuntContainer');

    if (!$mainContainer.length) return;

    // Get signal aspect and arm count for sorting
    function getSignalAspect(assetId) {
        var asset = wsLiveData[assetId];
        if (!asset) return { aspect: 'INACTIVE', priority: 99, armCount: 0 };

        var attrs = asset.attrs || {};
        var dlRelays = asset.dlRelays || {};
        var threshold = parseFloat(asset.ZeroOffsetValue || 0.1);

        function val(name) {
            if (attrs[name] && attrs[name].Value !== null && attrs[name].Value !== undefined) {
                return parseFloat(attrs[name].Value) || 0;
            }
            return 0;
        }

        function isRelayPickup(relayName) {
            for (var key in dlRelays) {
                if (key.toUpperCase().indexOf(relayName.toUpperCase()) > -1) {
                    return dlRelays[key].isPickup === true || dlRelays[key].value === 1;
                }
            }
            return false;
        }

        // Count arms/routes (AUG, BUG, CUG, DUG, EUG)
        var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
        var armCount = 0;
        for (var r = 0; r < routeNames.length; r++) {
            if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) {
                armCount++;
            }
        }

        var rgMa = val('RG mA');
        var dgMa = val('DG mA');
        var hgMa = val('HG mA');
        var hhgMa = val('HHG mA');

        var recrPickup = isRelayPickup('RECR');
        var decrPickup = isRelayPickup('DECR');
        var hecrPickup = isRelayPickup('HECR');
        var hhecrPickup = isRelayPickup('HHECR');

        // RED
        if (recrPickup || rgMa > threshold) {
            return { aspect: 'RED', priority: 1, armCount: armCount };
        }
        // DOUBLE YELLOW
        if ((hecrPickup && hhecrPickup) || hhgMa > threshold) {
            return { aspect: 'DOUBLE_YELLOW', priority: 2, armCount: armCount };
        }
        // SINGLE YELLOW
        if (hecrPickup || hgMa > threshold) {
            return { aspect: 'SINGLE_YELLOW', priority: 3, armCount: armCount };
        }
        // GREEN
        if (decrPickup || dgMa > threshold) {
            return { aspect: 'GREEN', priority: 4, armCount: armCount };
        }
        // INACTIVE
        return { aspect: 'INACTIVE', priority: 99, armCount: armCount };
    }

    // Sort function: aspect priority -> arm count -> name
    function getSignalAspectCount(assetId) {
        var asset = wsLiveData[assetId];
        if (!asset) return 0;
        var attrs = asset.attrs || {};
        var count = 0;
        if (attrs['RG mA'] || attrs['RG V']) count++;
        if (attrs['DG mA'] || attrs['DG V']) count++;
        if (attrs['HG mA'] || attrs['HG V']) count++;
        if (attrs['HHG mA'] || attrs['HHG V']) count++;
        if (attrs['PILOT mA'] || attrs['PILOT V'] ||
            attrs['PILOTRoot mA'] || attrs['PILOTRoot V']) count++;
        if (attrs['Co_Hg mA'] || attrs['Co_Hg V']) count++;
        var routeNames = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];
        for (var r = 0; r < routeNames.length; r++) {
            if (attrs[routeNames[r] + ' mA'] || attrs[routeNames[r] + ' V']) count++;
        }
        return count;
    }

    var aspectSort = function (a, b) {
        // Blank-data signals always sink to the bottom (see renderRDPMSView).
        var blankA = (typeof assetHasNoData === 'function') ? assetHasNoData(a) : false;
        var blankB = (typeof assetHasNoData === 'function') ? assetHasNoData(b) : false;
        if (blankA !== blankB) return blankA ? 1 : -1;

        var countA = getSignalAspectCount(a);
        var countB = getSignalAspectCount(b);

        if (countA !== countB) {
            return countA - countB; // Ascending (fewer aspects first)
        }

        // Tie-break: alphabetical by name
        return (wsLiveData[a].AssetName || '').localeCompare(
            wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }
        );
    };
    // Reorder main signals
    if ($mainContainer.children().length > 0) {
        var mainCards = {};
        var mainIds = [];

        $mainContainer.children().each(function () {
            var cardId = $(this).attr('id');
            if (cardId && cardId.indexOf('rdpmsCard_') === 0) {
                var assetId = cardId.replace('rdpmsCard_', '');
                mainCards[assetId] = $(this).detach();
                mainIds.push(assetId);
            }
        });

        mainIds.sort(aspectSort);

        console.log('[RDPMS Reorder] New order:', mainIds.map(function (id) {
            var info = getSignalAspect(id);
            return wsLiveData[id].AssetName + ':' + info.aspect + ':arms=' + info.armCount;
        }));

        for (var i = 0; i < mainIds.length; i++) {
            var aid = mainIds[i];
            if (mainCards[aid]) {
                $mainContainer.append(mainCards[aid]);
            }
        }
    }

    // Reorder shunt signals
    if ($shuntContainer.length > 0 && $shuntContainer.children().length > 0) {
        var shuntCards = {};
        var shuntIds = [];

        $shuntContainer.children().each(function () {
            var cardId = $(this).attr('id');
            if (cardId && cardId.indexOf('rdpmsCard_') === 0) {
                var assetId = cardId.replace('rdpmsCard_', '');
                shuntCards[assetId] = $(this).detach();
                shuntIds.push(assetId);
            }
        });

        shuntIds.sort(aspectSort);

        for (var si = 0; si < shuntIds.length; si++) {
            var said = shuntIds[si];
            if (shuntCards[said]) {
                $shuntContainer.append(shuntCards[said]);
            }
        }
    }
}
// Per-asset DOM element cache -- built once per card, reused every update
var _rdpmsDomCache = {};

function getRdpmsElements(assetId) {
    if (_rdpmsDomCache[assetId]) return _rdpmsDomCache[assetId];

    var id = assetId;
    var c = {
        // Lights
        RG: $('#rdpmsRG_' + id),
        DG: $('#rdpmsDG_' + id),
        HG: $('#rdpmsHG_' + id),
        HHG: $('#rdpmsHHG_' + id),
        Calling: $('#rdpmsCalling_' + id),
        RootMiddle: $('#rdpmsRootMiddle_' + id),

        // Table rows
        trRG: $('#rdpmsTrRG_' + id),
        trDG: $('#rdpmsTrDG_' + id),
        trHG: $('#rdpmsTrHG_' + id),
        trHHG: $('#rdpmsTrHHG_' + id),
        trPilot: $('#rdpmsTrPilot_' + id),
        trCO: $('#rdpmsTrCO_' + id),
        trRoute: $('#rdpmsTrRoute_' + id),
        trDPR: $('#rdpmsTrDPR_' + id),
        trHPR: $('#rdpmsTrHPR_' + id),
        trHHPR: $('#rdpmsTrHHPR_' + id),

        // Value cells
        tdRGma: $('#rdpmsTdRGma_' + id),
        tdRGv: $('#rdpmsTdRGv_' + id),
        tdDGma: $('#rdpmsTdDGma_' + id),
        tdDGv: $('#rdpmsTdDGv_' + id),
        tdHGma: $('#rdpmsTdHGma_' + id),
        tdHGv: $('#rdpmsTdHGv_' + id),
        tdHHGma: $('#rdpmsTdHHGma_' + id),
        tdHHGv: $('#rdpmsTdHHGv_' + id),
        tdPilotma: $('#rdpmsTdPilotma_' + id),
        tdPilotv: $('#rdpmsTdPilotv_' + id),
        tdCOma: $('#rdpmsTdCOma_' + id),
        tdCOv: $('#rdpmsTdCOv_' + id),
        tdRoutema: $('#rdpmsTdRoutema_' + id),
        tdRoutev: $('#rdpmsTdRoutev_' + id),
        tdDPRval: $('#rdpmsTdDPRval_' + id),
        tdHPRval: $('#rdpmsTdHPRval_' + id),
        tdHHPRval: $('#rdpmsTdHHPRval_' + id),

        // Timestamp + card
        ts: $('#rdpmsTs_' + id),
        card: $('#rdpmsCard_' + id),

        // Route lights (prebuilt map)
        routes: (function () {
            var rn = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'], map = {};
            for (var i = 0; i < rn.length; i++) {
                map[rn[i]] = [];
                for (var d = 1; d <= 4; d++) {
                    map[rn[i]].push($('#rdpmsRoute_' + id + '_' + rn[i] + '_' + d));
                }
            }
            return map;
        })()
    };

    _rdpmsDomCache[assetId] = c;
    return c;
}

// Call this when a card is removed/rebuilt to clear stale cache
function clearRdpmsDomCache(assetId) {
    delete _rdpmsDomCache[assetId];
}


// Store last known signal state for each asset (to retain when no condition matches)
var lastSignalState = {};

// ── Per-asset DOM cache: populated once on first call, reused on every update ──
var _sigDomCache = {};
// Route names are fixed - declare once outside the function
var _ROUTE_NAMES = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG'];

// Helper - format value: avoids re-parsing an already-numeric result of val()
function _fv(v) { return (v === 0) ? '0.0' : v.toFixed(2); }

// Helper - read a numeric attr value directly from the attrs map (no parseFloat re-call if already set)
function _attrVal(attrs, name) {
    var a = attrs[name];
    return (a && a.Value !== null && a.Value !== undefined) ? parseFloat(a.Value) : 0;
}

// Build and cache all DOM element references for one assetId
function _buildSigCache(assetId) {
    var c = { id: assetId };
    var p = 'rdpms';
    // Light dots
    c.RG = document.getElementById(p + 'RG_' + assetId);
    c.DG = document.getElementById(p + 'DG_' + assetId);
    c.HG = document.getElementById(p + 'HG_' + assetId);
    c.HHG = document.getElementById(p + 'HHG_' + assetId);
    c.CALL = document.getElementById(p + 'Calling_' + assetId);
    c.ROOT = document.getElementById(p + 'RootMiddle_' + assetId);
    // Table rows
    c.trRG = document.getElementById(p + 'TrRG_' + assetId);
    c.trDG = document.getElementById(p + 'TrDG_' + assetId);
    c.trHG = document.getElementById(p + 'TrHG_' + assetId);
    c.trHHG = document.getElementById(p + 'TrHHG_' + assetId);
    c.trPilot = document.getElementById(p + 'TrPilot_' + assetId);
    c.trCO = document.getElementById(p + 'TrCO_' + assetId);
    c.trRoute = document.getElementById(p + 'TrRoute_' + assetId);
    c.trDPR = document.getElementById(p + 'TrDPR_' + assetId);
    c.trHPR = document.getElementById(p + 'TrHPR_' + assetId);
    c.trHHPR = document.getElementById(p + 'TrHHPR_' + assetId);
    // Table value cells
    c.tdRGma = document.getElementById(p + 'TdRGma_' + assetId);
    c.tdRGv = document.getElementById(p + 'TdRGv_' + assetId);
    c.tdDGma = document.getElementById(p + 'TdDGma_' + assetId);
    c.tdDGv = document.getElementById(p + 'TdDGv_' + assetId);
    c.tdHGma = document.getElementById(p + 'TdHGma_' + assetId);
    c.tdHGv = document.getElementById(p + 'TdHGv_' + assetId);
    c.tdHHGma = document.getElementById(p + 'TdHHGma_' + assetId);
    c.tdHHGv = document.getElementById(p + 'TdHHGv_' + assetId);
    c.tdHPRval = document.getElementById(p + 'TdHPRval_' + assetId);
    c.tdHHPRval = document.getElementById(p + 'TdHHPRval_' + assetId);
    c.tdDPRval = document.getElementById(p + 'TdDPRval_' + assetId);
    c.tdCOma = document.getElementById(p + 'TdCOma_' + assetId);
    c.tdCOv = document.getElementById(p + 'TdCOv_' + assetId);
    c.tdPilotma = document.getElementById(p + 'TdPilotma_' + assetId);
    c.tdPilotv = document.getElementById(p + 'TdPilotv_' + assetId);
    c.tdRoutema = document.getElementById(p + 'TdRoutema_' + assetId);
    c.tdRoutev = document.getElementById(p + 'TdRoutev_' + assetId);
    c.tsEl = document.getElementById(p + 'Ts_' + assetId);
    // Route light arrays [routeName][dotIndex 1..4]
    c.routeLights = {};
    for (var ri = 0; ri < _ROUTE_NAMES.length; ri++) {
        var rn = _ROUTE_NAMES[ri];
        c.routeLights[rn] = [
            null, // index 0 unused - 1-based
            document.getElementById(p + 'Route_' + assetId + '_' + rn + '_1'),
            document.getElementById(p + 'Route_' + assetId + '_' + rn + '_2'),
            document.getElementById(p + 'Route_' + assetId + '_' + rn + '_3'),
            document.getElementById(p + 'Route_' + assetId + '_' + rn + '_4')
        ];
    }
    // Card element for flash
    c.card = document.getElementById('rdpmsCard_' + assetId);
    _sigDomCache[assetId] = c;
    return c;
}

// Inline helpers to toggle light-off class without jQuery overhead
function _lightOn(el) { if (el) el.classList.remove('light-off'); }
function _lightOff(el) { if (el) el.classList.add('light-off'); }
function _show(el) { if (el) el.style.display = ''; }
function _hide(el) { if (el) el.style.display = 'none'; }
function _html(el, h) { if (el && el.innerHTML !== h) el.innerHTML = h; }
function _text(el, t) { if (el && el.textContent !== t) el.textContent = t; }

// Make updateMainSignalLights global so it can be called when DataLogger relays change
function updateMainSignalLights(assetId) {

    var asset = wsLiveData[assetId];
    if (!asset) return;

    var attrs = asset.attrs;
    if (!attrs) return;

    // Use cached DOM refs; build cache on first call for this asset
    var c = _sigDomCache[assetId] || _buildSigCache(assetId);

    //var threshold = parseFloat(asset.ZeroOffsetValue || getZeroOffsetForAsset(assetId) || RDPMS_DEFAULT_THRESHOLD);
    var _zEntry = zeroOffsetCache[assetId];
    if (!_zEntry || !_zEntry.fetched) {
        // Trigger fetch with re-render callback -- no duplicate if already in-flight
        fetchZeroOffsetForAsset(assetId, function (confirmedValue) {
            console.log('[ZeroOffset] Asset ' + assetId + ' re-rendering with confirmed threshold: ' + confirmedValue);
            if (window.updateMainSignalLights) window.updateMainSignalLights(assetId);
        });
        // Do NOT return -- render now with default so signal is not blank
        console.log('[ZeroOffset] Asset ' + assetId + ' -- using default threshold, will re-render when API responds');
    }
    var threshold = (_zEntry && _zEntry.fetched && !isNaN(_zEntry.value))
        ? _zEntry.value
        : parseFloat(asset.ZeroOffsetValue || RDPMS_DEFAULT_THRESHOLD);
    console.log('[ZeroOffset-Guard] Asset ' + assetId +
        ' -- rendering with confirmed threshold: ' + threshold);

    // ==============================
    // READ VALUES (single parseFloat pass each)
    // ==============================
    var rgMa = _attrVal(attrs, 'RG mA'), dgMa = _attrVal(attrs, 'DG mA'),
        hgMa = _attrVal(attrs, 'HG mA'), hhgMa = _attrVal(attrs, 'HHG mA');
    var rgV = _attrVal(attrs, 'RG V'), dgV = _attrVal(attrs, 'DG V'),
        hgV = _attrVal(attrs, 'HG V'), hhgV = _attrVal(attrs, 'HHG V');
    var pilotMa = _attrVal(attrs, 'PILOT mA') || _attrVal(attrs, 'PILOTRoot mA');
    var pilotV = _attrVal(attrs, 'PILOT V') || _attrVal(attrs, 'PILOTRoot V');
    var coHgMa = _attrVal(attrs, 'Co_Hg mA'), coHgV = _attrVal(attrs, 'Co_Hg V');
    var dprVal = _attrVal(attrs, 'DPR'),
        hprVal = _attrVal(attrs, 'HPR'),
        hhprVal = _attrVal(attrs, 'HHPR');


    var rgActive = rgMa > threshold;
    var hhgActive = hhgMa > threshold;
    var hgActive = hgMa > threshold;
    var dgActive = dgMa > threshold;

    // Debug -- open browser console to verify values vs threshold
    console.log('[Signal-Debug] Asset=' + assetId +
        ' | ZeroOffset(threshold)=' + threshold +
        ' | RG mA=' + rgMa + (rgActive ? ' ✓ACTIVE' : ' ✗off') +
        ' | HHG mA=' + hhgMa + (hhgActive ? ' ✓ACTIVE' : ' ✗off') +
        ' | HG mA=' + hgMa + (hgActive ? ' ✓ACTIVE' : ' ✗off') +
        ' | DG mA=' + dgMa + (dgActive ? ' ✓ACTIVE' : ' ✗off'));

    var currentSignal = 'none';

    if (rgActive || hhgActive || hgActive || dgActive) {
        // RED: RG must be active AND must be the highest active mA
        // (guards against residual RG noise when DG/HG is dominant)
        var maxActiveMa = Math.max(
            rgActive ? rgMa : 0,
            hhgActive ? hhgMa : 0,
            hgActive ? hgMa : 0,
            dgActive ? dgMa : 0
        );

        if (rgActive && maxActiveMa === rgMa) {
            currentSignal = 'red';
        }
        // KEY FIX: DOUBLE YELLOW = HHG mA > threshold
        // If both HHG mA=125 AND HG mA=144 are above threshold,
        // HHG being active means the Double Yellow aspect is showing --
        // both HHG and HG lamps are energised.  HG being numerically
        // higher does NOT make it singleYellow.
        else if (hhgActive) {
            currentSignal = 'doubleYellow';
        }
        // SINGLE YELLOW: only HG is active, HHG is NOT above threshold
        else if (hgActive) {
            currentSignal = 'singleYellow';
        }
        // GREEN
        else if (dgActive) {
            currentSignal = 'green';
        }
    }

    console.log('[Signal-Debug] Asset=' + assetId + ' → Aspect=' + currentSignal);

    if (currentSignal !== 'none') lastSignalState[assetId] = currentSignal;

    // ==============================
    // TURN OFF ALL LIGHTS (direct classList, no jQuery)
    // ==============================
    _lightOff(c.RG); _lightOff(c.DG); _lightOff(c.HG); _lightOff(c.HHG);
    _lightOff(c.CALL); _lightOff(c.ROOT);

    for (var r = 0; r < _ROUTE_NAMES.length; r++) {
        var rl = c.routeLights[_ROUTE_NAMES[r]];
        _lightOff(rl[1]); _lightOff(rl[2]); _lightOff(rl[3]); _lightOff(rl[4]);
    }

    _hide(c.trRG); _hide(c.trDG); _hide(c.trHG); _hide(c.trHHG);
    _hide(c.trPilot); _hide(c.trCO); _hide(c.trRoute);
    _hide(c.trDPR); _hide(c.trHPR); _hide(c.trHHPR);

    // ==============================
    // ACTIVATE SIGNAL
    // ==============================
    if (currentSignal === 'red') {
        _lightOn(c.RG); _show(c.trRG);
        _html(c.tdRGma, 'I<sub>SigRG</sub> : <span>' + _fv(rgMa) + ' mA</span>');
        _html(c.tdRGv, 'V<sub>SigRG</sub> : <span>' + _fv(rgV) + ' V</span>');
    }
    else if (currentSignal === 'doubleYellow') {
        _lightOn(c.HG); _lightOn(c.HHG);
        _show(c.trHG);
        _html(c.tdHGma, 'I<sub>SigHG</sub> : <span>' + _fv(hgMa) + ' mA</span>');
        _html(c.tdHGv, 'V<sub>SigHG</sub> : <span>' + _fv(hgV) + ' V</span>');
        _show(c.trHHG);
        _html(c.tdHHGma, 'I<sub>SigHHG</sub> : <span>' + _fv(hhgMa) + ' mA</span>');
        _html(c.tdHHGv, 'V<sub>SigHHG</sub> : <span>' + _fv(hhgV) + ' V</span>');
        _show(c.trHHPR);
        _html(c.tdHHPRval, 'HHPR : <span>' + _fv(hhprVal) + '</span>');
        _show(c.trHPR);
        _html(c.tdHPRval, 'HPR : <span>' + _fv(hprVal) + '</span>');
    }
    else if (currentSignal === 'singleYellow') {
        _lightOn(c.HG); _show(c.trHG);
        _html(c.tdHGma, 'I<sub>SigHG</sub> : <span>' + _fv(hgMa) + ' mA</span>');
        _html(c.tdHGv, 'V<sub>SigHG</sub> : <span>' + _fv(hgV) + ' V</span>');
        _show(c.trHPR);
        _html(c.tdHPRval, 'HPR : <span>' + _fv(hprVal) + '</span>');
    }
    else if (currentSignal === 'green') {
        _lightOn(c.DG); _show(c.trDG);
        _html(c.tdDGma, 'I<sub>SigDG</sub> : <span>' + _fv(dgMa) + ' mA</span>');
        _html(c.tdDGv, 'V<sub>SigDG</sub> : <span>' + _fv(dgV) + ' V</span>');
        _show(c.trDPR);
        _html(c.tdDPRval, 'DPR : <span>' + _fv(dprVal) + '</span>');
        _show(c.trHPR);
        _html(c.tdHPRval, 'HPR : <span>' + _fv(hprVal) + '</span>');
    }

    // ==============================
    // CALLING ON (Independent)
    // ==============================
    if (coHgMa > threshold) {
        _lightOn(c.CALL); _show(c.trCO);
        _html(c.tdCOma, 'I<sub>SigCoHg</sub> : <span>' + _fv(coHgMa) + ' mA</span>');
        _html(c.tdCOv, 'V<sub>SigCoHg</sub> : <span>' + _fv(coHgV) + ' V</span>');
    }

    // ==============================
    // ROUTE INDICATORS
    // ==============================
    var activeRoute = null, activeRouteMa = 0, activeRouteV = 0;

    for (var ri = 0; ri < _ROUTE_NAMES.length; ri++) {
        var rn = _ROUTE_NAMES[ri];
        var rMa = _attrVal(attrs, rn + ' mA');
        if (rMa > threshold) {
            _lightOn(c.ROOT);
            var rl2 = c.routeLights[rn];
            _lightOn(rl2[1]); _lightOn(rl2[2]); _lightOn(rl2[3]); _lightOn(rl2[4]);
            if (!activeRoute) {
                activeRoute = rn;
                activeRouteMa = rMa;
                activeRouteV = _attrVal(attrs, rn + ' V');
            }
        }
    }

    if (activeRoute) {
        _show(c.trRoute);
        _html(c.tdRoutema, 'I<sub>Sig' + activeRoute + '</sub> : <span>' + _fv(activeRouteMa) + ' mA</span>');
        _html(c.tdRoutev, 'V<sub>Sig' + activeRoute + '</sub> : <span>' + _fv(activeRouteV) + ' V</span>');
    }

    // ==============================
    // PILOT LAMP
    // ==============================
    if (pilotMa > threshold) {
        _show(c.trPilot);
        _html(c.tdPilotma, 'I<sub>SigPILOT</sub> : <span>' + _fv(pilotMa) + ' mA</span>');
        _html(c.tdPilotv, 'V<sub>SigPILOT</sub> : <span>' + _fv(pilotV) + ' V</span>');
    }

    // ==============================
    // TIMESTAMP UPDATE (single DOM write, only if changed)
    // ==============================
    var latestTs = '';
    for (var ak in attrs) {
        var ats = attrs[ak].Timestamp;
        if (ats && ats > latestTs) latestTs = ats;
    }
    var tsText;
    if (latestTs) {
        try {
            tsText = 'Updated: ' + new Date(latestTs).toLocaleTimeString('en-IN', {
                hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false
            });
        } catch (e) {
            tsText = 'Updated: ' + fmtTime(asset.lastUpdated);
        }
    } else {
        var rawTs = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '';
        tsText = rawTs ? 'Last: ' + rawTs : '';
    }
    _text(c.tsEl, tsText);

    // ==============================
    // FLASH ONLY CHANGED VALUES (querySelectorAll once per card, not per attr)
    // ==============================
    var changedAttrs = [], attrKey;
    for (attrKey in attrs) {
        if (attrs[attrKey].changed) {
            changedAttrs.push(attrKey);
            attrs[attrKey].changed = false;
        }
    }
    if (changedAttrs.length && c.card) {
        for (var ci = 0; ci < changedAttrs.length; ci++) {
            var el = c.card.querySelector('[data-attr="' + changedAttrs[ci] + '"]');
            if (el) {
                el.classList.remove('rdpms-val-flash');
                void el.offsetWidth; // force reflow to restart animation
                el.classList.add('rdpms-val-flash');
            }
        }
    }

    renderDataloggerBadgesForAsset(assetId);
}
// Expose globally for callers that use window.updateMainSignalLights(...)
window.updateMainSignalLights = updateMainSignalLights;

function updateShuntSignalLights(assetId) {
    var asset = wsLiveData[assetId]; if (!asset) return;
    var attrs = asset.attrs;
    // Get ZeroOffset from GlobalConfig (AssetAttributeId = 30) or use default
    //var threshold = parseFloat(asset.ZeroOffsetValue || getZeroOffsetForAsset(assetId) || RDPMS_DEFAULT_THRESHOLD);
    // Get ZeroOffset using same cache guard as updateMainSignalLights
    var _szEntry = zeroOffsetCache[assetId];
    if (!_szEntry || !_szEntry.fetched) {
        fetchZeroOffsetForAsset(assetId, function () {
            updateShuntSignalLights(assetId);
        });
    }
    var threshold = (_szEntry && _szEntry.fetched && !isNaN(_szEntry.value))
        ? _szEntry.value
        : parseFloat(asset.ZeroOffsetValue || RDPMS_DEFAULT_THRESHOLD);
    function val(name) { return (attrs[name] && attrs[name].Value !== null && attrs[name].Value !== undefined) ? parseFloat(attrs[name].Value) : 0; }
    function fv(v) { return (v === 0) ? '0.0' : parseFloat(v).toFixed(2); }
    var onMa = val('On Aspect mA'), onV = val('On Aspect V'), offMa = val('Off Aspect mA'), offV = val('Off Aspect V');
    var pilotMa = val('PILOT mA') || val('PILOTRoot mA'); var pilotV = val('PILOT V') || val('PILOTRoot V');
    $('#rdpmsShuntTop_' + assetId).addClass('light-off'); $('#rdpmsShuntRight_' + assetId).addClass('light-off'); $('#rdpmsShuntLeft_' + assetId).addClass('light-off');
    $('#rdpmsTrOn_' + assetId).hide(); $('#rdpmsTrOff_' + assetId).hide(); $('#rdpmsTrShPilot_' + assetId).hide();
    if (onMa > threshold) { $('#rdpmsShuntTop_' + assetId).addClass('light-off'); $('#rdpmsShuntRight_' + assetId).removeClass('light-off'); $('#rdpmsShuntLeft_' + assetId).removeClass('light-off'); $('#rdpmsTrOn_' + assetId).show(); $('#rdpmsTdOnma_' + assetId).html('I<sub>ShSig ON</sub> : <span>' + fv(onMa) + ' ma</span>'); }
    else if (offMa > threshold) { $('#rdpmsShuntTop_' + assetId).removeClass('light-off'); $('#rdpmsShuntRight_' + assetId).removeClass('light-off'); $('#rdpmsShuntLeft_' + assetId).addClass('light-off'); $('#rdpmsTrOff_' + assetId).show(); $('#rdpmsTdOffma_' + assetId).html('I<sub>ShSig OFF</sub> : <span>' + fv(offMa) + ' ma</span>'); }
    if (pilotMa > threshold) { $('#rdpmsTrShPilot_' + assetId).show(); $('#rdpmsTdShPilotma_' + assetId).html('I<sub>Sh PILOT</sub> : <span>' + fv(pilotMa) + ' ma</span>'); }
    if (!$('#rdpmsShuntRight_' + assetId).hasClass('light-off') && !$('#rdpmsShuntLeft_' + assetId).hasClass('light-off')) { $('#rdpmsTrOn_' + assetId).show(); $('#rdpmsTdOnv_' + assetId).html('V<sub>ShSig ON</sub> : <span>' + fv(onV) + ' V</span>'); }
    if (!$('#rdpmsShuntTop_' + assetId).hasClass('light-off') && !$('#rdpmsShuntRight_' + assetId).hasClass('light-off')) { $('#rdpmsTrOff_' + assetId).show(); $('#rdpmsTdOffv_' + assetId).html('V<sub>ShSig OFF</sub> : <span>' + fv(offV) + ' V</span>'); }
    if (pilotMa > threshold) { $('#rdpmsTrShPilot_' + assetId).show(); $('#rdpmsTdShPilotv_' + assetId).html('V<sub>Sh PILOT</sub> : <span>' + fv(pilotV) + ' V</span>'); }
    $('#rdpmsCard_' + assetId).show();
    var latestTs = ''; for (var ak in attrs) { if (attrs[ak].Timestamp && attrs[ak].Timestamp > latestTs) latestTs = attrs[ak].Timestamp; }
    if (latestTs) { try { var tsParsed = new Date(latestTs); var tsStr = tsParsed.toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }); $('#rdpmsTs_' + assetId).text('Updated: ' + tsStr); } catch (e) { $('#rdpmsTs_' + assetId).text('Updated: ' + fmtTime(asset.lastUpdated)); } }
    else { var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : ''; $('#rdpmsTs_' + assetId).text(ts ? 'Last: ' + ts : ''); }
    var $card = $('#rdpmsCard_' + assetId + ' .card-border'); $card.removeClass('rdpms-flash'); if ($card[0]) { void $card[0].offsetWidth; $card.addClass('rdpms-flash'); setTimeout(function () { $card.removeClass('rdpms-flash'); }, 1200); }
    renderDataloggerBadgesForAsset(assetId);
}

// ===== BIND VIEWS =====
function fnBindRDPMS() {
    var siteId = $('#drpSite').val(),
        atId = $('#drpAssetType').val(),
        assetIds = getSelectedAssetIds();
    if (!siteId || siteId === '0' || siteId === '') {
        showWarning('Please select a site', 'Validation'); return;
    }
    rdpmsCardsBuilt = {};
    disconnectWebSocket();
    connectWebSocket(siteId, atId, assetIds);
}

// ── Get checked asset IDs from the multi-select dropdown ─────────────────
function getSelectedAssetIds() {
    var ids = [];

    $('#listAssetNumber input:checked').each(function () {
        ids.push(String($(this).val()));
    });

    var total = $('#listAssetNumber input').length;

    // If all assets are selected, return [] so frontend does not reject valid WS assets.
    if (total > 0 && ids.length === total) {
        return [];
    }

    return ids;
}
function fnBindTableFromWebSocket() { var siteId = $('#drpSite').val(), atId = $('#drpAssetType').val(), assetIds = getSelectedAssetIds(); if (!siteId || siteId === '0' || siteId === '') { showWarning('Please select a site', 'Validation'); return; } disconnectWebSocket(); connectWebSocket(siteId, atId, assetIds); $('#downloadContainer').show(); }
function fnBindTableFromAPI() {
    var siteId = $('#drpSite').val(), assetTypeId = $('#drpAssetType').val(), assetIds = getSelectedAssetIds();
    if (!siteId || siteId === '0' || siteId === '') { showWarning('Please select a site', 'Validation'); return; }
    $("#loader").show();
    // Show API status bar
    $('#wsStatusBar').html('<div class="ws-status-bar ws-live" style="background:linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%);border-color:#3b82f6;"><span class="ws-dot live" style="background:#3b82f6;"></span><strong>API</strong> -- REST data</div>').show();
    var apiUrl = (assetIds.length === 1) ? API_BASE_URL + '/' + assetIds[0] : API_BASE_URL + '?siteId=' + siteId;
    $.ajax({
        url: apiUrl, type: 'GET', dataType: 'json',
        success: function (data) { $("#loader").hide(); if (!data || data.length === 0) { $('#divTelemetryLive').html('<div class="text-center text-muted p-5">No data available</div>'); return; } var fd = data; if (assetTypeId && assetTypeId !== '0') fd = fd.filter(function (i) { return i.AssetTypeId == assetTypeId; }); if (assetIds.length > 1) fd = fd.filter(function (i) { return assetIds.indexOf(i.AssetId.toString()) > -1; }); if (fd.length > 0) { renderApiTable(fd, assetTypeId); $('#downloadContainer').show(); showSuccess('Data loaded', 'API'); } else { $('#divTelemetryLive').html('<div class="text-center text-muted p-5">No data for selected filters</div>'); } },
        error: function () { $("#loader").hide(); showError('Failed to fetch data', 'API Error'); }
    });
}
function renderApiTable(data, assetTypeId) {
    var map = {}, attrs = [], attrOrd = {}, attrIdMap = {};
    data.forEach(function (d) {
        //if (!map[d.AssetId]) map[d.AssetId] = { AssetId: d.AssetId, AssetName: d.AssetName, AssetTypeId: d.AssetTypeId, attrs: {} };

        var aid = String(d.AssetId);
        if (!map[aid]) {
            map[aid] = {
                AssetId: d.AssetId,
                AssetName: (typeof getBulkAssetName === 'function' ? getBulkAssetName(aid, d.AssetName) : d.AssetName),
                AssetTypeId: (typeof getBulkAssetTypeId === 'function' ? getBulkAssetTypeId(aid, d.AssetTypeId) : d.AssetTypeId),
                attrs: {}
            };
        }
        //map[d.AssetId].attrs[d.AssetAttributeName] = { Value: d.Value, AttrId: d.AssetAttributeId };
        map[aid].attrs[d.AssetAttributeName] = { Value: d.Value, AttrId: d.AssetAttributeId };
        if (attrs.indexOf(d.AssetAttributeName) === -1) {
            attrs.push(d.AssetAttributeName);
            attrOrd[d.AssetAttributeName] = d.AssetAttributeId;
            attrIdMap[d.AssetAttributeName] = d.AssetAttributeId;
        }
    });
    attrs.sort(function (a, b) { if (parseInt(assetTypeId) === 1) { var dA = assetAttributeMap[attrOrd[a]] || assetAttributeMap[String(attrOrd[a])] || a; var dB = assetAttributeMap[attrOrd[b]] || assetAttributeMap[String(attrOrd[b])] || b; var cA = dA.replace(/\s*\([^)]*\)\s*/g, '').replace(/_/g, ' ').trim().toUpperCase(); var cB = dB.replace(/\s*\([^)]*\)\s*/g, '').replace(/_/g, ' ').trim().toUpperCase(); var sA = 500, sB = 500; for (var si = 0; si < _trackColSeq.length; si++) { var t = _trackColSeq[si].toUpperCase(); if (cA === t || cA.indexOf(t) > -1 || t.indexOf(cA) > -1) sA = si + 1; if (cB === t || cB.indexOf(t) > -1 || t.indexOf(cB) > -1) sB = si + 1; } if (sA !== sB) return sA - sB; } return attrOrd[a] - attrOrd[b]; });
    var tn = 'ASSET'; if (data[0]) { switch (data[0].AssetTypeId) { case 1: tn = 'TRACK'; break; case 2: tn = 'SIGNAL'; break; case 3: tn = 'POINT'; break; } }
    var isTrack = (assetTypeId == 1);
    var h = '<div class="table-responsive"><table class="table table-hover" id="wsLiveTable"><thead><tr><th>' + tn + '</th>';
    // Use assetAttributeMap to get proper alias names
    attrs.forEach(function (a) {
        var attrId = attrIdMap[a];
        var displayName = getAttrDisplayName(a, attrId, null);
        var formattedName = formatAliasName(displayName);
        h += '<th title="' + displayName + '">' + formattedName + '</th>';
    });
    // Add derived value headers for Track assets
    if (isTrack) {
        h += getDerivedHeaders();
    }
    // DataLogger column for Track and Signal
    if (isTrack || parseInt(assetTypeId) === 2) {
        h += '<th style="min-width:100px;">DataLogger</th>';
    }
    h += '</tr></thead><tbody>';
    Object.keys(map).forEach(function (aid) {
        var asset = map[aid];
        h += '<tr><td class="asset-name">' + asset.AssetName + '</td>';
        var ifMa = 0, irMa = 0, tprV = 0;
        attrs.forEach(function (a) {
            var v = asset.attrs[a] ? parseFloat(asset.attrs[a].Value) : NaN;
            var disp = isNaN(v) ? 'N/A' : v.toFixed(2), cls = '';
            if (a === 'If mA') ifMa = v || 0;
            if (a === 'Ir mA') irMa = v || 0;
            if (a === 'TPR V') tprV = v || 0;
            if (a === 'Vr' && ((v > 0.1 && v < 2.5) || v > 4.2)) cls = 'val-danger';
            else if ((a === 'TPR V' || a === 'TPR V (Loc)') && v > 0.1 && v < 20) cls = 'val-danger';
            else if (a === 'Charger mA' && v < 100) cls = 'val-danger';
            else if (a === 'Choke V' && v > 1.8) cls = 'val-danger';
            var _tip = _getValueTooltip(a, v, cls);
            var _marks = _getCellMarkers(cls);
            h += '<td class="' + cls + '"' + (_tip ? ' title="' + _tip + '"' : '') + '>' + disp + _marks + '</td>';
        });
        if (isTrack) {
            // Calculate and add derived values
            var derived = calculateDerivedValues(asset.attrs);
            h += getDerivedCells(derived);
        }
        // DataLogger column for Track and Signal
        if (isTrack || parseInt(assetTypeId) === 2) {
            var dlRelays = asset.dlRelays || {}; var dlHtml = ''; var dlKeys = Object.keys(dlRelays);
            dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; });
            if (dlKeys.length > 0) { for (var di = 0; di < dlKeys.length; di++) { var relay = dlRelays[dlKeys[di]]; var badgeClass = relay.isPickup ? 'pickup' : 'drop'; var badgeText = relay.isPickup ? 'Pickup' : 'Drop'; dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> '; } } else { dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>'; }
            h += '<td class="dl-cell">' + dlHtml + '</td>';
        }
        h += '</tr>';
    });
    h += '</tbody></table></div>'; $('#divTelemetryLive').html(h);
}
function fnBindTable() {
    var mSearchCriteria = { SiteId: $('#drpSite').val(), AssetTypeId: $('#drpAssetType').val() }; var assetIds = getSelectedAssetIds();
    if (assetIds.length > 1) { mSearchCriteria.AssetIds = assetIds; mSearchCriteria.Id = ''; } else if (assetIds.length === 1) { mSearchCriteria.Id = assetIds[0]; mSearchCriteria.AssetIds = []; } else { mSearchCriteria.Id = ''; mSearchCriteria.AssetIds = []; }
    var isValid = true; if (!mSearchCriteria.SiteId || mSearchCriteria.SiteId === '0') { isValid = false; showWarning('Please select a site', 'Validation'); }
    else if (!mSearchCriteria.AssetTypeId || mSearchCriteria.AssetTypeId === '0') { isValid = false; showWarning('Please select an asset type', 'Validation'); }
    if (isValid) { $("#loader").show(); $('#wsStatusBar').html('<div class="ws-status-bar ws-live" style="background:linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%);border-color:#3b82f6;"><span class="ws-dot live" style="background:#3b82f6;"></span><strong>API</strong> -- Server data</div>').show(); $.ajax({ url: '/FRS25/Telemetry/_Table', type: 'POST', contentType: 'application/json', data: JSON.stringify({ SearchCriteria: mSearchCriteria }), success: function (data) { $("#loader").hide(); $('#divTelemetryLive').empty().append(data); $('#downloadContainer').show(); }, error: function () { $("#loader").hide(); showError('Failed to load table data', 'Error'); } }); }
}

function fnSearchView() {
    debugger;
    var siteId = $('#drpSite').val();
    var assetTypeId = $('#drpAssetType').val();
    var selectedAssetIds = (typeof getSelectedAssetIds === 'function') ? getSelectedAssetIds() : [];

    if (!siteId || siteId === '0' || siteId === '') {
        showWarning('Please select a site', 'Validation');
        return;
    }

    if (!assetTypeId || assetTypeId === '0' || assetTypeId === '') {
        showWarning('Please select an asset type', 'Validation');
        return;
    }

    if (!selectedAssetIds || selectedAssetIds.length === 0) {
        // FIX: getSelectedAssetIds() returns [] when ALL assets are checked
        // (to signal "don't filter"). That is a valid selection, not an empty one.
        // Only warn if genuinely no checkboxes are checked.
        var _totalAssets = $('#listAssetNumber input').length;
        var _checkedAssets = $('#listAssetNumber input:checked').length;
        if (_checkedAssets === 0 || _totalAssets === 0) {
            showWarning('Please select at least one asset number', 'Validation');
            return;
        }
    }

    // Mark that user has explicitly searched — unlocks rendering in updateViewMode.
    window._tlSearchInitiated = true;

    var viewType = $('#drpView').val();
    var isSignal = isSignalAssetType();
    var isPoint = isPointAssetType();
    var isIps = isIpsAssetType();

    // Hide Cards container ONLY when we're not currently displaying Cards.
    // If user is on the Cards tab and presses Search, leave Cards visible.
    var _onCards = $('#atCardView').hasClass('at-force-shown');
    if (!_onCards) {
        $('#atCardView').hide().removeClass('at-force-shown').addClass('at-force-hidden');
        $('.at-table-scroll').show().removeClass('at-force-hidden').addClass('at-force-shown');
    }
    $('#trackCardContainer').hide();

    // Gate: Track/Signal/IPS use bulk metadata. Point Machine must stay on old GetAssestBy/GetPMAssetMeta flow.
    if (!isPoint &&
        (!bulkMetadataLoaded || bulkMetadataSiteId !== String(siteId) || bulkMetadataAssetTypeId !== String(assetTypeId))) {
        loadBulkAssetMetadata(siteId, assetTypeId, function () { fnSearchView(); });
        return;
    }

    // Force WebSocket for Signal, Point Machine and IPS.
    if (isSignal || isPoint || isIps) {
        useApiDataSource = false;
        $('#chkDataSource').prop('checked', false);
    }

    if (viewType === 'IPS' || (viewType === 'Table' && isIps)) {
        fnBindIpsGrid();
    } else if (viewType === 'RDPMS') {
        fnBindRDPMS();
    } else if (viewType === 'PointMachine') {
        fnBindPointMachine();
    } else if (viewType === 'Track') {
        $('#trackCardContainer').show();
        fnBindTrackCards();
    } else if (viewType === 'Table') {
        var _tsT = document.querySelector('.at-table-scroll');
        if (_tsT) { _tsT.style.overflow = ''; _tsT.style.maxHeight = ''; _tsT.classList.add('scroll-table-mode'); }

        if (isWebSocketSite(siteId)) {
            if (isSignal) {
                fnBindTableFromWebSocket();
            } else if (isPoint) {
                fnBindPointMachineTable();
            } else if (useApiDataSource) {
                disconnectWebSocket();
                fnBindTableFromAPI();
            } else {
                fnBindTableFromWebSocket();
            }
        } else {
            disconnectWebSocket();
            if (useApiDataSource && !isSignal && !isPoint) {
                fnBindTableFromAPI();
            } else if (isPoint) {
                fnBindPointMachineTable();
            } else {
                fnBindTable();
            }
        }
    }
}

function fnBindTrackCards() {
    console.log('[Cards] fnBindTrackCards called');
    var container = $('#atCardView');
    if (!container.length) {
        console.error('[Cards] #atCardView not found');
        return;
    }
    container.empty();

    var assetIds = (typeof getSortedAssetIds === 'function') ? getSortedAssetIds(true) : Object.keys(wsLiveData);
    if (!assetIds.length) {
        container.html('<div class="text-center p-5" style="color:var(--text-3);">No assets found. Select an Asset Number.</div>');
        return;
    }

    // ── Determine asset type — robust multi-source detection ─────────
    // Priority order:
    //   1. The existing predicate functions (read from #drpAssetType
    //      value AND its selected text — most reliable)
    //   2. window.wsCurrentAssetTypeId  (last WS filter set by Search)
    //   3. first.AssetTypeId             (from WS data, often missing)
    //   4. $('#drpAssetType').val()      (raw select value)
    //
    // The previous code relied on (3) which was undefined in the
    // current data shape, making every type check false and routing
    // every asset to the Track-card fallback regardless of its
    // actual type.
    var first = wsLiveData[assetIds[0]];

    var isSignal = (typeof isSignalAssetType === 'function') && isSignalAssetType();
    var isPoint = (typeof isPointAssetType === 'function') && isPointAssetType();
    var isIps = (typeof isIpsAssetType === 'function') && isIpsAssetType();

    // If none of the predicates fired (rare — e.g. drpAssetType empty
    // because user is mid-pill-rebuild), fall back to the data shape.
    if (!isSignal && !isPoint && !isIps) {
        var atVal = $('#drpAssetType').val();
        var atText = ($('#drpAssetType option:selected').text() || '').toLowerCase();
        isSignal = (atVal === '2' || atText === 'signal');
        isPoint = (atVal === '3' || atText.indexOf('point') !== -1);
        isIps = (atVal === '4' || atText === 'ips');
    }
    // Track is the default when nothing else matches (asset type 1)
    var isTrack = !isSignal && !isPoint && !isIps;

    var atIdLog = (first && first.AssetTypeId) || window.wsCurrentAssetTypeId ||
        $('#drpAssetType').val() || '(none)';
    console.log('[Cards] assetType=', atIdLog,
        'track=', isTrack, 'signal=', isSignal,
        'point=', isPoint, 'ips=', isIps);

    // ── Rich types must NEVER use the generic at-card stubs ──────────
    // atBuildSignalCard / atBuildPmCard only emit placeholder cards
    // ("SIGNAL : name" / "[Wrong asset type]"). fnBindTrackCards is for
    // Track assets only; if it is reached for Signal/Point/IPS (a stray
    // executeUIUpdate call mid view-switch, or any other caller),
    // delegate to the correct rich renderer and flip container
    // visibility so the stub cards are never painted into #atCardView.
    if (isSignal && typeof renderRDPMSView === 'function') {
        console.log('[Cards] Signal type → delegating to renderRDPMSView()');
        container.removeClass('at-force-shown').addClass('at-force-hidden').hide();
        $('.at-table-scroll').removeClass('at-force-hidden').addClass('at-force-shown').show();
        $('#divTelemetryLive').show();
        renderRDPMSView();
        return;
    }
    if (isPoint && typeof renderPointMachineView === 'function') {
        console.log('[Cards] Point type → delegating to renderPointMachineView()');
        container.removeClass('at-force-shown').addClass('at-force-hidden').hide();
        $('.at-table-scroll').removeClass('at-force-hidden').addClass('at-force-shown').show();
        $('#divTelemetryLive').show();
        renderPointMachineView();
        return;
    }
    if (isIps && typeof renderIpsGridView === 'function') {
        console.log('[Cards] IPS type → delegating to renderIpsGridView()');
        container.removeClass('at-force-shown').addClass('at-force-hidden').hide();
        $('.at-table-scroll').removeClass('at-force-hidden').addClass('at-force-shown').show();
        $('#divTelemetryLive').show();
        renderIpsGridView();
        return;
    }

    var html = '<div class="at-cards-grid">';
    for (var i = 0; i < assetIds.length; i++) {
        var aid = assetIds[i];
        if (isSignal && typeof atBuildSignalCard === 'function') {
            html += atBuildSignalCard(aid);
        } else if (isPoint && typeof atBuildPmCard === 'function') {
            html += atBuildPmCard(aid);
        } else if (isIps && typeof buildIpsCard === 'function') {
            html += buildIpsCard(aid);
        } else if (typeof atBuildTrackCard === 'function') {
            html += atBuildTrackCard(aid);
        } else {
            // Fallback
            var name = wsLiveData[aid] ? wsLiveData[aid].AssetName : aid;
            html += '<div class="at-asset-card"><div class="at-card-head"><div class="at-card-name">' + name + '</div></div></div>';
        }
    }
    html += '</div>';
    container.html(html);
    // Use the CSS-class-based visibility system. Plain .show() is a
    // no-op here because the stylesheet has `#atCardView{display:none}`
    // as the baseline and `#atCardView.at-force-shown{display:block !important}`
    // as the override. Without the class, the baseline wins.
    container.removeClass('at-force-hidden').addClass('at-force-shown').show();
    console.log('[Cards] rendered', assetIds.length, 'cards');
}

//}// ================================================================
// POINT MACHINE TABLE BINDING
// Connects WebSocket and renders PM data in table format
// ================================================================
function fnBindPointMachineTable() {
    var siteId = $('#drpSite').val(), atId = $('#drpAssetType').val(), assetIds = getSelectedAssetIds();
    if (!siteId || siteId === '0' || siteId === '') { showWarning('Please select a site', 'Validation'); return; }

    // Reset PM state
    pmCardsBuilt = {};
    pmEventHistory = {};

    disconnectWebSocket();
    connectWebSocket(siteId, atId, assetIds);

    // Set AFTER connectWebSocket — connectWebSocket resets pmTableMode to false
    window.pmTableMode = true;

    // Override render behavior for table mode
    console.log('[PM Table] Binding started. Will render in table format.');
}

// ================================================================
// IPS GRID VIEW -- Bind WebSocket + render helpers
// ================================================================
function fnBindIpsGrid() {
    var siteId = $('#drpSite').val();
    var atId = $('#drpAssetType').val();
    var assetIds = getSelectedAssetIds();

    if (!siteId || siteId === '0' || siteId === '') {
        showWarning('Please select a site', 'Validation');
        return;
    }

    disconnectWebSocket();
    connectWebSocket(siteId, atId, assetIds);

    // Show a waiting placeholder while WS data arrives
    $('#divTelemetryLive').html(
        '<div class="ips-grid-container">' +
        '<div class="ips-card-grid">' +
        '<div class="ips-waiting-card"><i class="fas fa-satellite-dish fa-2x mb-3" style="color:#259dab;display:block;"></i>' +
        'Connecting to live data stream…</div>' +
        '</div></div>'
    );
    $('#downloadContainer').show();
    console.log('[IPS Grid] Binding started. Waiting for WebSocket data.');
}

/**
 * renderIpsGridView()
 * Builds / refreshes the IPS card-grid from wsLiveData.
 * Called on first render AND on each incremental update.
 */
window.renderIpsGridView = function renderIpsGridView() {
    // Guard: only render if IPS is actually selected. Prevents wrong-asset-type
    // cards from appearing during an asset-type switch race.
    if (typeof isIpsAssetType === 'function' && !isIpsAssetType()) {
        console.warn('[IPS View] Not an IPS asset type – skipping render');
        return;
    }

    var assetIds = Object.keys(wsLiveData).filter(function (id) {
        // Defense-in-depth: skip assets not in the bulk response.
        return (typeof isAssetInBulkWhitelist !== 'function') || isAssetInBulkWhitelist(id);
    });
    if (assetIds.length === 0) return;

    $('#wsWaiting').remove();

    // Sort asset IDs by name
    assetIds.sort(function (a, b) {
        return (wsLiveData[a].AssetName || '').localeCompare(
            wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }
        );
    });

    var $c = $('#divTelemetryLive');
    assetIds = blankDataLast(assetIds);
    // ---- Full rebuild ----
    var totalCount = assetIds.length;
    var h = '<div class="ips-grid-container">';
    h += '<div class="ips-grid-header">';
    h += '<h6><i class="fas fa-th" style="color:#259dab;"></i> IPS Live Grid</h6>';
    h += '<span class="ips-grid-badge">' + totalCount + ' Asset' + (totalCount !== 1 ? 's' : '') + '</span>';
    h += '</div>';
    h += '<div class="ips-card-grid" id="ipsCardGrid">';

    for (var i = 0; i < assetIds.length; i++) {
        h += buildIpsCard(assetIds[i]);
    }

    h += '</div></div>';
    $c.html(h);
};

/**
 * buildIpsCard(assetId)
 * Returns HTML string for one IPS asset card.
 */
function buildIpsCard(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return '';

    var name = asset.AssetName || ('Asset ' + assetId);
    var attrs = asset.attrs || {};
    var attrKeys = Object.keys(attrs);

    // Sort attribute keys by AttrOrder if available
    attrKeys.sort(function (a, b) {
        var oA = (attrs[a] && attrs[a].AttrOrder != null) ? parseInt(attrs[a].AttrOrder) : 999;
        var oB = (attrs[b] && attrs[b].AttrOrder != null) ? parseInt(attrs[b].AttrOrder) : 999;
        return oA - oB;
    });

    // Timestamp
    var ts = '--';
    if (typeof window.getOperationTimestampDevice === 'function') {
        var opTs = window.getOperationTimestampDevice(asset);
        if (opTs && typeof window.fmtTimestampDevice === 'function') {
            ts = window.fmtTimestampDevice(opTs);
        }
    }
    if (ts === '--' && asset.lastUpdated && typeof window.fmtTime === 'function') {
        ts = window.fmtTime(asset.lastUpdated);
    }

    var h = '<div class="ips-asset-card" data-ips-id="' + assetId + '">';

    // Card header
    h += '<div class="ips-card-head">';
    h += '<span class="ips-asset-name" title="' + name + '">' + name + '</span>';
    h += '<span class="ips-status-dot"></span>';
    h += '</div>';

    // Attribute rows
    h += '<div class="ips-attr-list">';
    if (attrKeys.length === 0) {
        h += '<div class="ips-attr-row"><span class="ips-attr-name" style="color:#94a3b8;font-style:italic;">Waiting for data…</span></div>';
    } else {
        for (var k = 0; k < attrKeys.length; k++) {
            var an = attrKeys[k];
            var aObj = attrs[an] || {};
            var rawVal = aObj.Value;
            var disp = (rawVal !== null && rawVal !== undefined && rawVal !== '') ? rawVal : '--';
            var valCls = (disp === '--') ? 'ips-attr-val no-data' : 'ips-attr-val';

            // Display name: AliasName → AttrName → raw key
            var label = an;
            if (typeof window.getAttrDisplayName === 'function') {
                label = window.getAttrDisplayName(an, (aObj.AttrId || aObj.AssetAttributeId), assetId) || label;
            }
            if (typeof window.formatAliasName === 'function') {
                label = window.formatAliasName(label) || label;
            }

            var _ipsStaleMap = wsStaleAttrs[assetId] || {};
            var _ipsIsStale = !!_ipsStaleMap[an];
            if (_ipsIsStale) valCls += ' ws-stale-val';
            var _ipsTip = _getValueTooltip(an, parseFloat(rawVal), valCls);
            var _ipsMarks = _getCellMarkers(valCls);

            h += '<div class="ips-attr-row">';
            h += '<span class="ips-attr-name" title="' + label + '">' + label + '</span>';
            h += '<span class="' + valCls + '" data-attr="' + an + '"' + (_ipsTip ? ' title="' + _ipsTip + '"' : '') + '>' + disp + _ipsMarks + '</span>';
            h += '</div>';
        }
    }
    h += '</div>';

    // Card footer -- last update
    h += '<div class="ips-card-footer"><i class="fas fa-clock"></i> ' + ts + '</div>';
    h += '</div>';  // .ips-asset-card
    return h;
}

/**
 * updateIpsGridIncremental(updatedAssetIds)
 * Refreshes only the cards that received new data.
 */
window.updateIpsGridIncremental = function updateIpsGridIncremental(updatedAssetIds) {
    var $grid = $('#ipsCardGrid');
    if ($grid.length === 0) { renderIpsGridView(); return; }

    for (var i = 0; i < updatedAssetIds.length; i++) {
        var aid = updatedAssetIds[i];
        var $card = $grid.find('[data-ips-id="' + aid + '"]');

        if ($card.length === 0) {
            // New asset -- append a fresh card
            $grid.append(buildIpsCard(aid));
            // Update total count badge
            var total = $grid.find('.ips-asset-card').length;
            $('#divTelemetryLive .ips-grid-badge').text(total + ' Asset' + (total !== 1 ? 's' : ''));
        } else {
            // Existing card -- update attribute values in-place + flash
            var asset = wsLiveData[aid];
            if (!asset) continue;
            var attrs = asset.attrs || {};

            var _ipsStaleMap = wsStaleAttrs[aid] || {};
            $card.find('.ips-attr-row .ips-attr-val').each(function () {
                var $v = $(this);
                var an = $v.attr('data-attr');
                if (!an) return;
                var aObj = attrs[an] || {};
                var raw = aObj.Value;
                var disp = (raw !== null && raw !== undefined && raw !== '') ? raw : '--';
                var isStale = !!_ipsStaleMap[an];
                var valCls = (disp === '--') ? 'ips-attr-val no-data' : 'ips-attr-val';
                if (isStale) valCls += ' ws-stale-val';
                var _marks = _getCellMarkers(valCls);
                var _tip = _getValueTooltip(an, parseFloat(raw), valCls);

                var oldText = $v.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
                if (oldText !== String(disp)) {
                    $v.html(disp + _marks).attr('class', valCls).attr('title', _tip || '');
                    if (disp !== '--') {
                        $v.addClass('ips-val-flash');
                        setTimeout(function () { $v.removeClass('ips-val-flash'); }, 1200);
                    }
                } else {
                    // Value unchanged but stale status may have changed
                    $v.toggleClass('ws-stale-val', isStale);
                    $v.find('.ws-stale-marker,.ws-warn-marker').remove();
                    if (_marks) $v.append(_marks);
                    $v.attr('title', _tip || '');
                }
            });

            // Update footer timestamp
            var ts = '--';
            if (typeof window.getOperationTimestampDevice === 'function') {
                var opTs2 = window.getOperationTimestampDevice(asset);
                if (opTs2 && typeof window.fmtTimestampDevice === 'function') ts = window.fmtTimestampDevice(opTs2);
            }
            if (ts === '--' && asset.lastUpdated && typeof window.fmtTime === 'function') ts = window.fmtTime(asset.lastUpdated);
            $card.find('.ips-card-footer').html('<i class="fas fa-clock"></i> ' + ts);

            // Flash the card border
            $card.addClass('ips-card-flash');
            setTimeout(function (c) { return function () { c.removeClass('ips-card-flash'); }; }($card), 1000);
        }
    }
};



var mainGraphChart = null;
var _gC = ['#2563eb', '#dc2626', '#16a34a', '#d97706', '#7c3aed', '#0891b2', '#db2777', '#059669', '#ea580c', '#4f46e5', '#0d9488', '#b45309', '#9333ea', '#65a30d', '#0369a1', '#be185d', '#047857', '#c2410c', '#6d28d9', '#0e7490'];
var _gChecked = {};
var _gColorMap = {};
var _gReqStart = null; // requested start time (ms)
var _gReqEnd = null; // requested end time (ms)

// Derived series definitions -- label, formula-key, unit, color
var _DERIVED_DEFS = [
    { key: 'itcBattCharg', label: 'ITC BATT CHARG', unit: 'mA', color: '#0891b2' },
    { key: 'vtcVarRes', label: 'VTC VAR RES', unit: 'V', color: '#7c3aed' },
    { key: 'rtcChFeedEnd', label: 'RTC CH FEED END', unit: 'Ω', color: '#b45309' },
    { key: 'rtcVarRes', label: 'RTC VAR RES', unit: 'Ω', color: '#be185d' },
    { key: 'vtcTr', label: 'VTC TR', unit: 'V', color: '#0369a1' },
    { key: 'ibalst', label: 'IBALST', unit: 'mA', color: '#059669' },
    { key: 'rrail', label: 'RRAIL', unit: 'Ω', color: '#d97706' }
];

function fnBindGrph() {
    var siteId = $('#drpSite').val(), assetId = $('#drpAsset').val(), assetTypeId = $('#drpAssetType').val();
    var assetName = $('#drpAsset option:selected').text() || assetId;
    var siteName = $('#drpSite option:selected').text() || siteId;
    if (!siteId || siteId === '0') { showWarning('Please select a site', 'Validation'); return; }
    if (!assetTypeId || assetTypeId === '0') { showWarning('Please select an asset type', 'Validation'); return; }
    if (!assetId || assetId === '') { showWarning('Please select an asset', 'Validation'); return; }
    var isSignal = (parseInt(assetTypeId) === 2);
    var isTrack = (parseInt(assetTypeId) === 1);
    _gChecked = {}; _gColorMap = {};

    var g = '<div id="gWrap" style="background:rgba(255,255,255,0.04);backdrop-filter:blur(28px) saturate(160%);-webkit-backdrop-filter:blur(28px) saturate(160%);border:1px solid rgba(255,255,255,0.10);border-radius:16px;overflow:hidden;width:100%;box-shadow:0 8px 32px rgba(0,0,0,0.36);">';

    // ── Header ──
    g += '<div style="background:linear-gradient(135deg,rgba(34,211,238,0.18) 0%,rgba(167,139,250,0.14) 100%);border-bottom:1px solid rgba(255,255,255,0.10);padding:12px 18px;display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px;">';
    g += '<div style="color:rgba(255,255,255,0.96);display:flex;align-items:center;gap:10px;"><i class="fas fa-chart-line" style="font-size:16px;color:#22d3ee;opacity:.9;"></i>'
        + '<div><b style="font-size:15px;letter-spacing:.01em;font-family:\'Bricolage Grotesque\',sans-serif;">' + assetName + '</b>'
        + '<div style="opacity:.6;font-size:11px;margin-top:1px;font-family:\'JetBrains Mono\',monospace;">' + siteName + ' &nbsp;·&nbsp; Live Telemetry Graph</div></div></div>';
    g += '<div style="display:flex;gap:5px;" id="gTB">';
    ['1', '3', '6', '12', '24'].forEach(function (h) { g += '<button class="gtb2' + (h === '24' ? ' active2' : '') + '" data-h="' + h + '">' + h + 'H</button>'; });
    g += '</div></div>';

    // ── Select / Unselect All bar ──
    g += '<div style="background:rgba(52,211,153,0.06);border-bottom:1px solid rgba(255,255,255,0.07);padding:4px 12px;display:flex;align-items:center;gap:6px;">'
        + '<span style="font-size:10px;color:rgba(255,255,255,0.5);font-weight:700;text-transform:uppercase;letter-spacing:.5px;">Attributes:</span>'
        + '<button class="gSelAll2" onclick="window._gSelectAll(true)"><i class="fas fa-check-square" style="font-size:10px;"></i> Select All</button>'
        + '<button class="gSelAll2" onclick="window._gSelectAll(false)"><i class="far fa-square" style="font-size:10px;"></i> Unselect All</button>'
        + '<span style="margin-left:auto;font-size:10px;color:rgba(255,255,255,0.45);font-weight:600;" id="gSelCount"></span>'
        + '</div>';

    // ── Attribute panel ──
    g += '<div style="background:rgba(255,255,255,0.02);border-bottom:1px solid rgba(255,255,255,0.08);padding:0;">';
    g += '<div style="display:flex;flex-wrap:wrap;">';
    // Feed End column
    g += '<div id="gColF" style="flex:1;min-width:160px;padding:7px 10px;border-right:1px solid rgba(255,255,255,0.07);">'
        + '<div style="font-weight:700;color:#34d399;font-size:10px;text-transform:uppercase;letter-spacing:.7px;margin-bottom:5px;display:flex;align-items:center;gap:4px;">'
        + '<i class="fas fa-bolt" style="font-size:9px;"></i>' + (isSignal ? 'Signal Attributes' : 'Feed End') + '</div>'
        + '<div id="gFL"></div></div>';
    // Relay End column
    g += '<div id="gColR" style="flex:1;min-width:160px;padding:7px 10px;border-right:1px solid rgba(255,255,255,0.07);">'
        + '<div style="font-weight:700;color:#60a5fa;font-size:10px;text-transform:uppercase;letter-spacing:.7px;margin-bottom:5px;display:flex;align-items:center;gap:4px;">'
        + '<i class="fas fa-wave-square" style="font-size:9px;"></i>' + (isSignal ? 'Relay Values' : 'Relay End') + '</div>'
        + '<div id="gRL"></div></div>';
    // DataLogger column
    g += '<div id="gColD" style="flex:1;min-width:160px;padding:7px 10px;border-right:1px solid rgba(255,255,255,0.07);">'
        + '<div style="font-weight:700;color:#a78bfa;font-size:10px;text-transform:uppercase;letter-spacing:.7px;margin-bottom:5px;display:flex;align-items:center;gap:4px;">'
        + '<i class="fas fa-toggle-on" style="font-size:9px;"></i>DataLogger</div>'
        + '<div id="gDL"></div></div>';
    // Derived Values column (Track only)
    if (isTrack) {
        g += '<div id="gColDerived" style="flex:1;min-width:160px;padding:7px 10px;">'
            + '<div style="font-weight:700;color:#fbbf24;font-size:10px;text-transform:uppercase;letter-spacing:.7px;margin-bottom:5px;display:flex;align-items:center;gap:4px;">'
            + '<i class="fas fa-calculator" style="font-size:9px;"></i>Derived Values</div>'
            + '<div id="gDerived"></div></div>';
    }
    g += '</div></div>';

    // ── Controls bar ──
    g += '<div style="padding:8px 18px;display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px;border-bottom:1px solid rgba(255,255,255,0.07);background:rgba(0,0,0,0.18);">';
    g += '<div style="display:flex;align-items:center;gap:12px;">';
    g += '<div style="display:flex;align-items:center;gap:6px;"><i class="fas fa-calendar-alt" style="font-size:10px;color:rgba(255,255,255,0.30);"></i><span style="font-size:11.5px;color:rgba(255,255,255,0.40);">Range:</span><b id="gTR" style="color:#22d3ee;font-family:\'JetBrains Mono\',monospace;font-size:12px;"></b></div>';
    g += '<div style="width:1px;height:14px;background:rgba(255,255,255,0.12);"></div>';
    g += '<div style="display:flex;align-items:center;gap:5px;"><i class="fas fa-database" style="font-size:10px;color:rgba(255,255,255,0.30);"></i><b id="gPT" style="color:#22d3ee;font-family:\'JetBrains Mono\',monospace;font-size:12px;">0</b><span style="font-size:11px;color:rgba(255,255,255,0.40);">pts</span></div>';
    g += '</div>';
    g += '<div style="display:flex;align-items:center;gap:5px;">';
    g += '<span style="font-size:10.5px;color:rgba(255,255,255,0.32);font-weight:700;letter-spacing:.5px;font-family:Manrope,sans-serif;margin-right:2px;">FILTER:</span>';
    g += '<button class="gfb2 active2" data-f="all">All</button>';
    g += '<button class="gfb2" data-f="ma">mA</button>';
    g += '<button class="gfb2" data-f="v">V</button>';
    g += '<button class="gfb2" data-f="bin">0/1</button>';
    if (isTrack) g += '<button class="gfb2" data-f="derived">Derived</button>';
    g += '</div></div>';

    // ── Toolbar ──
    g += '<div id="gToolbar" style="display:none;padding:4px 12px;text-align:right;background:rgba(0,0,0,0.14);border-bottom:1px solid rgba(255,255,255,0.07);">';
    g += '<button class="gtool2" onclick="if(mainGraphChart)mainGraphChart.dispatchAction({type:\'restore\'})"><i class="fas fa-undo"></i> Reset Zoom</button>';
    g += '<button class="gtool2" onclick="if(mainGraphChart){var u=mainGraphChart.getDataURL({type:\'png\',pixelRatio:2});var a=document.createElement(\'a\');a.href=u;a.download=\'telemetry_graph.png\';a.click();}"><i class="fas fa-download"></i> Save PNG</button>';
    g += '</div>';

    // ── Chart ──
    g += '<div id="gLD" style="display:flex;flex-direction:column;align-items:center;justify-content:center;height:240px;color:rgba(255,255,255,0.45);gap:12px;background:transparent;">'
        + '<div class="gsp2"></div>'
        + '<span style="font-size:13px;font-weight:600;font-family:Manrope,sans-serif;letter-spacing:.01em;">Loading telemetry data…</span>'
        + '<span style="font-size:11px;color:rgba(255,255,255,0.30);font-family:JetBrains Mono,monospace;">Fetching history from API</span>'
        + '</div>';
    g += '<div id="gCH" style="width:100%;max-width:100%;height:540px;display:none;background:transparent;box-sizing:border-box;"></div>';
    g += '<div id="gEM" style="display:none;text-align:center;padding:60px 20px;color:rgba(255,255,255,0.32);font-size:13px;">'
        + '<i class="fas fa-chart-line" style="font-size:40px;margin-bottom:14px;display:block;opacity:.20;color:#22d3ee;"></i>'
        + '<div style="font-weight:700;font-family:Manrope,sans-serif;color:rgba(255,255,255,0.45);margin-bottom:6px;">No data available</div>'
        + '<div style="font-size:11px;color:rgba(255,255,255,0.22);font-family:JetBrains Mono,monospace;">Try a different time range or check asset connectivity</div>'
        + '</div>';
    g += '</div>';

    // ── Styles ──
    g += '<style id="gStyles2">';
    g += '.gtb2{background:rgba(255,255,255,0.10);color:rgba(255,255,255,0.85);border:1px solid rgba(255,255,255,0.20);border-radius:6px;padding:4px 10px;font-size:11px;font-weight:700;cursor:pointer;transition:all .2s;letter-spacing:.02em;}';
    g += '.gtb2:hover{background:rgba(255,255,255,0.18);color:#fff;}';
    g += '.gtb2.active2{background:#22d3ee;color:#0b1530;border-color:#22d3ee;box-shadow:0 0 12px rgba(34,211,238,0.45);}';
    g += '.gfb2{background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.12);border-radius:6px;padding:3px 9px;font-size:10px;font-weight:600;cursor:pointer;transition:all .18s;}';
    g += '.gfb2:hover{background:rgba(255,255,255,0.12);color:rgba(255,255,255,0.9);}';
    g += '.gfb2.active2{background:rgba(34,211,238,0.18);color:#22d3ee;border-color:rgba(34,211,238,0.4);box-shadow:0 0 8px rgba(34,211,238,0.2);}';
    g += '.gsp2{width:24px;height:24px;border:3px solid rgba(255,255,255,0.12);border-top-color:#22d3ee;border-radius:50%;animation:gs2 .7s linear infinite;}';
    g += '@@keyframes gs2{to{transform:rotate(360deg);}}';
    g += '.gSelAll2{background:rgba(255,255,255,0.06);color:rgba(255,255,255,0.7);border:1px solid rgba(255,255,255,0.12);border-radius:6px;padding:2px 8px;font-size:10px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:3px;transition:all .18s;}';
    g += '.gSelAll2:hover{background:rgba(34,211,238,0.12);border-color:rgba(34,211,238,0.35);color:#22d3ee;}';
    g += '.gAttrRow2{display:flex;align-items:center;padding:2px 4px;border-radius:5px;cursor:pointer;gap:5px;transition:background .12s;user-select:none;margin-bottom:1px;}';
    g += '.gAttrRow2:hover{background:rgba(34,211,238,0.08);}';
    g += '.gAttrRow2 input[type=checkbox]{cursor:pointer;width:13px;height:13px;accent-color:#22d3ee;flex-shrink:0;margin:0;}';
    g += '.gAttrDot2{width:9px;height:9px;border-radius:50%;flex-shrink:0;display:inline-block;box-shadow:0 0 0 1px rgba(255,255,255,0.10);}';
    g += '.gAttrName2{font-size:11.5px;color:rgba(255,255,255,0.72);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500;}';
    g += '.gAttrVal2{font-size:11px;font-weight:700;flex-shrink:0;min-width:34px;text-align:right;font-family:\'JetBrains Mono\',monospace;}';
    g += '.gDlBadge2{display:inline-flex;align-items:center;justify-content:center;padding:1px 6px;border-radius:3px;font-size:9px;font-weight:700;color:#fff;position:relative;overflow:hidden;flex-shrink:0;}';
    g += '.gDlBadge2.pickup{background:#10b981;}';
    g += '.gDlBadge2.drop{background:#f59e0b;}';
    g += '.gDlBadge2::before{content:"";position:absolute;top:0;left:-75%;width:50%;height:100%;background:linear-gradient(120deg,rgba(255,255,255,.1) 0%,rgba(255,255,255,.45) 50%,rgba(255,255,255,.1) 100%);transform:skewX(-20deg);animation:gDlShine 2.5s infinite;}';
    g += '@@keyframes gDlShine{0%{left:-75%;}100%{left:125%;}}';
    g += '.gDerivedRow{display:flex;align-items:center;padding:2px 4px;border-radius:5px;cursor:pointer;gap:5px;transition:background .12s;user-select:none;margin-bottom:1px;}';
    g += '.gDerivedRow:hover{background:rgba(251,191,36,0.08);}';
    g += '.gDerivedRow input[type=checkbox]{cursor:pointer;width:13px;height:13px;accent-color:#fbbf24;flex-shrink:0;margin:0;}';
    g += '.gtool2{background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.12);border-radius:6px;padding:3px 10px;margin-left:5px;cursor:pointer;color:rgba(255,255,255,0.65);font-size:10px;font-weight:600;transition:all .18s;}';
    g += '.gtool2:hover{background:rgba(34,211,238,0.12);color:#22d3ee;border-color:rgba(34,211,238,0.35);}';
    g += '</style>';

    $('#divTelemetryLive').empty().append(g);
    // FIX: Reload attribute map for this asset type to avoid cross-type name collisions
    if (parseInt(wsCurrentAssetTypeId) !== parseInt(assetTypeId)) {
        assetAttributeMap = {}; assetAttributeByName = {}; assetAttributeLoaded = false;
        wsCurrentAssetTypeId = assetTypeId;
        invalidateBulkMetadataCache();
        var _graphSiteId = $('#drpSite').val();
        loadBulkAssetMetadata(_graphSiteId, assetTypeId, null);
    }
    window._gD = null; window._gA = assetId; window._gF = 'all'; window._gIsTrack = isTrack;

    $('#gTB').on('click', '.gtb2', function () {
        $('#gTB .gtb2').removeClass('active2'); $(this).addClass('active2');
        _gLoad(assetId, +$(this).data('h'));
    });
    $(document).off('click.gf').on('click.gf', '.gfb2', function () {
        $('.gfb2').removeClass('active2'); $(this).addClass('active2');
        window._gF = $(this).data('f');
        if (window._gD) _gRender(window._gD, assetId, window._gF);
    });

    _gPop(assetId, null);
    setTimeout(function () { _gLoad(assetId, 24); }, 0);
}

// Select / Unselect All
window._gSelectAll = function (checked) {
    // Toggle all checkboxes in attribute panels
    $('#gFL input[type=checkbox],#gRL input[type=checkbox],#gDL input[type=checkbox],#gDerived input[type=checkbox]').each(function () {
        var key = $(this).data('key');
        if (key !== undefined) {
            _gChecked[key] = checked;
            this.checked = checked;
        }
    });
    _gUpdateSelCount();
    if (window._gD) _gRender(window._gD, window._gA, window._gF || 'all');
};

function _gUpdateSelCount() {
    var total = $('#gFL input[type=checkbox],#gRL input[type=checkbox],#gDL input[type=checkbox],#gDerived input[type=checkbox]').length;
    var checked = $('#gFL input:checked,#gRL input:checked,#gDL input:checked,#gDerived input:checked').length;
    $('#gSelCount').text(checked + ' / ' + total + ' selected');
}

function _gPop(aid, gd) {
    var $f = $('#gFL').empty(), $r = $('#gRL').empty(), $d = $('#gDL').empty(), $dv = $('#gDerived').empty();
    var fb = { 1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)', 5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)', 9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA', 13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA', 154: 'Root V', 155: 'Root mA', 227: 'UG V', 228: 'UG mA', 327: 'HHPR', 328: 'DPR', 329: 'HPR', 337: 'PILOT mA', 499: 'PILOT V', 610: 'Co_Hg mA', 611: 'Co_Hg V' };
    var fK = ['TFC I/P', 'TFC O/P', 'CH FEED', 'VTC FEED', 'ITC FEED'];
    var rK = ['RELAY END', 'VTC TR', '24 DC LOC', '24 DC TPR', 'TPR'];
    var isSignal = (parseInt($('#drpAssetType').val()) === 2);
    function cls(n) { if (isSignal) return 'f'; var u = n.toUpperCase().replace(/\s*\([^)]*\)/g, ''); for (var i = 0; i < fK.length; i++)if (u.indexOf(fK[i]) > -1) return 'f'; for (var j = 0; j < rK.length; j++)if (u.indexOf(rK[j]) > -1) return 'r'; return 'f'; }

    // Analog attribute row (with checkbox)
    function rwAnalog(key, name, val, color) {
        var safeKey = key.replace(/[^a-zA-Z0-9_]/g, '_');
        var chkId = 'gChk_' + safeKey;
        var isChk = (_gChecked[key] !== false);
        return '<label class="gAttrRow2" for="' + chkId + '">'
            + '<input type="checkbox" id="' + chkId + '" data-key="' + key.replace(/"/g, '&quot;') + '" ' + (isChk ? 'checked' : '') + '  onchange="window._gToggle(this)">'
            + '<span class="gAttrDot2" style="background:' + color + '"></span>'
            + '<span class="gAttrName2" title="' + name + '">' + name + '</span>'
            + '<span class="gAttrVal2" style="color:' + color + '">' + val + '</span>'
            + '</label>';
    }

    // DataLogger row -- Pickup/Drop badge matching table view
    function rwDL(dlKey, name, isPickup, color) {
        var safeKey = dlKey.replace(/[^a-zA-Z0-9_]/g, '_');
        var chkId = 'gChk_' + safeKey;
        var isChk = (_gChecked[dlKey] !== false);
        var badgeCls = isPickup ? 'pickup' : 'drop';
        var badgeTxt = isPickup ? 'Pickup' : 'Drop';
        return '<label class="gAttrRow2" for="' + chkId + '">'
            + '<input type="checkbox" id="' + chkId + '" data-key="' + dlKey.replace(/"/g, '&quot;') + '" ' + (isChk ? 'checked' : '') + '  onchange="window._gToggle(this)">'
            + '<span class="gAttrDot2" style="background:' + color + '"></span>'
            + '<span class="gAttrName2" title="' + name + '">' + name + '</span>'
            + '<span class="gDlBadge2 ' + badgeCls + '">' + badgeTxt + '</span>'
            + '</label>';
    }

    // Derived value row
    function rwDerived(key, name, val, color) {
        var safeKey = ('drv_' + key).replace(/[^a-zA-Z0-9_]/g, '_');
        var chkId = 'gChk_' + safeKey;
        var lookupKey = 'DRV:' + key;
        var isChk = (_gChecked[lookupKey] !== false);
        return '<label class="gDerivedRow" for="' + chkId + '">'
            + '<input type="checkbox" id="' + chkId + '" data-key="' + lookupKey + '" ' + (isChk ? 'checked' : '') + '  onchange="window._gToggle(this)">'
            + '<span class="gAttrDot2" style="background:' + color + '"></span>'
            + '<span class="gAttrName2" style="color:#92400e;" title="' + name + '">' + name + '</span>'
            + '<span class="gAttrVal2" style="color:' + color + '">' + val + '</span>'
            + '</label>';
    }

    var ci = 0, a = wsLiveData[aid];

    // ── Analog attrs ──
    if (a && a.attrs && Object.keys(a.attrs).length > 0) {
        for (var k in a.attrs) {
            var at = a.attrs[k], id = parseInt(at.AttrId || at.AssetAttributeId || 0); if (!id) continue;
            var dn = (typeof getAttrDisplayNamePlain === 'function') ? getAttrDisplayNamePlain(k, id) : (fb[id] || k);
            var v = parseFloat(at.Value), vs = isNaN(v) ? '--' : v.toFixed(2);
            var c = _gColorMap[dn] || (_gColorMap[dn] = _gC[ci % _gC.length]); ci++;
            if (_gChecked[dn] === undefined) _gChecked[dn] = true;
            cls(dn) === 'f' ? $f.append(rwAnalog(dn, dn, vs, c)) : $r.append(rwAnalog(dn, dn, vs, c));
        }
    } else if (gd) {
        gd.forEach(function (at) {
            var id = at.AttributeId;
            if (at.Values && at.Values['1'] && at.Values['1'].DataType === 'DataLogger') return;
            var rn = fb[id] || ('Attr ' + id);
            var dn = (typeof getAttrDisplayNamePlain === 'function') ? getAttrDisplayNamePlain(rn, id) : rn;
            var c = _gColorMap[dn] || (_gColorMap[dn] = _gC[ci % _gC.length]); ci++;
            var lv = null, lt = 0;
            for (var key in at.Values) { var e = at.Values[key]; if (!e || !e.Timestamp) continue; var ts = e.Timestamp.TimestampDevice; if (!ts || ts.indexOf('0001') >= 0) continue; var ms = new Date(ts).getTime(); if (ms > lt) { lt = ms; lv = parseFloat(e.Value); } }
            var vs = (lv !== null && !isNaN(lv)) ? lv.toFixed(2) : '--';
            if (_gChecked[dn] === undefined) _gChecked[dn] = true;
            cls(dn) === 'f' ? $f.append(rwAnalog(dn, dn, vs, c)) : $r.append(rwAnalog(dn, dn, vs, c));
        });
    }

    // ── DataLogger panel -- ONLY from wsLiveData.dlRelays (matches table view exactly) ──
    // Never scan history gd for DataLogger entries: history API marks some analog mV
    // sensors as DataType='DataLogger' which are NOT relays and should NOT appear here.
    var dlRendered = false;
    if (a && a.dlRelays && Object.keys(a.dlRelays).length > 0) {
        var dk = Object.keys(a.dlRelays);
        dk.sort(function (x, y) { var rx = a.dlRelays[x], ry = a.dlRelays[y]; return (rx.isPickup && !ry.isPickup) ? -1 : (!rx.isPickup && ry.isPickup) ? 1 : 0; });
        for (var i = 0; i < dk.length; i++) {
            var rl = a.dlRelays[dk[i]];
            var dlKey = 'DL:' + dk[i];
            var dlColor = _gColorMap[dlKey] || (_gColorMap[dlKey] = _gC[ci % _gC.length]); ci++;
            if (_gChecked[dlKey] === undefined) _gChecked[dlKey] = true;
            $d.append(rwDL(dlKey, (rl.displayName || dk[i]), rl.isPickup, dlColor));
        }
        dlRendered = true;
    }
    if (!dlRendered) { $d.append('<span style="color:#cbd5e1;font-size:12px;padding:4px 6px;display:block;">--</span>'); }

    // ── Derived Values panel (Track only) ──
    if (window._gIsTrack && $dv.length) {
        var liveAttrs = (a && a.attrs) || {};
        // FIX: If no live attrs yet (e.g. after asset switch), build a snapshot
        // from the latest values in history data (gd) so derived panel isn't all 0
        var attrsForDerived = liveAttrs;
        if ((!a || Object.keys(liveAttrs).length === 0) && gd && gd.length > 0) {
            var _fb = { 1: 'If mA', 2: 'Ir mA', 3: 'VTC RELAY END(V)', 4: 'Choke V', 5: 'Charger mA', 6: 'VTC 24 DC TPR I/P(V)' };
            var _snapAttrs = {};
            gd.forEach(function (at) {
                var id = at.AttributeId;
                if (!at.Values) return;
                var latestVal = null, latestTs = 0;
                for (var key in at.Values) {
                    var e = at.Values[key];
                    if (!e) continue;
                    var ts = e.Timestamp ? (e.Timestamp.TimestampDevice || e.Timestamp.TimestampLocal) : e.TimestampDevice;
                    if (!ts || ts.indexOf('0001') >= 0) continue;
                    var ms = new Date(ts).getTime();
                    if (ms > latestTs) { latestTs = ms; latestVal = e.Value; }
                }
                if (latestVal === null) return;
                var rn = _fb[id] || ('Attr ' + id);
                _snapAttrs[rn] = { Value: latestVal, AttrId: id, AssetAttributeId: id };
            });
            if (Object.keys(_snapAttrs).length > 0) attrsForDerived = _snapAttrs;
        }
        var dv = (typeof calculateDerivedValues === 'function') ? calculateDerivedValues(attrsForDerived) : {};
        _DERIVED_DEFS.forEach(function (d) {
            var lookupKey = 'DRV:' + d.key;
            if (_gChecked[lookupKey] === undefined) _gChecked[lookupKey] = true;
            var val = dv[d.key];
            var vs = (val !== undefined && val !== null && !isNaN(val)) ? (typeof formatDerivedValue === 'function' ? formatDerivedValue(val) : val.toFixed(2)) : '--';
            $dv.append(rwDerived(d.key, d.label + ' (' + d.unit + ')', vs, d.color));
        });
    }

    // Show/hide columns
    if ($f.children().length === 0) { $('#gColF').hide(); } else { $('#gColF').show(); }
    if ($r.children().length === 0) { $('#gColR').hide(); } else { $('#gColR').show(); }
    if ($d.children().length === 0) { $('#gColD').hide(); } else { $('#gColD').show(); }
    if ($dv && $dv.children().length === 0) { $('#gColDerived').hide(); } else if ($dv) { $('#gColDerived').show(); }
    // Remove right border from last visible column
    $('#gColF,#gColR,#gColD,#gColDerived').css('border-right', '1px solid #e2e8f0');
    $('#gColF,#gColR,#gColD,#gColDerived').filter(':visible:last').css('border-right', 'none');
    _gUpdateSelCount();
}

window._gToggle = function (el) {
    var key = $(el).data('key');
    _gChecked[key] = el.checked;
    _gUpdateSelCount();
    if (window._gD) _gRender(window._gD, window._gA, window._gF || 'all');
};

function _gLoad(aid, hrs) {
    $('#gLD').show(); $('#gCH').hide(); $('#gEM').hide(); $('#gToolbar').hide();
    var endMs = Date.now(), startMs = endMs - (hrs * 3600000);
    _gReqStart = startMs; _gReqEnd = endMs;
    var s = new Date(startMs), e = new Date(endMs);
    var fm = function (d) { return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); };
    // Show REQUESTED range (not data range)
    $('#gTR').text(fm(s) + ' -- ' + fm(e) + ' (' + hrs + 'h)');
    $.ajax({
        url: HISTORY_API_BASE + '?assetId=' + aid + '&startDate=' + formatDateForHistoryApi(s) + '&endDate=' + formatDateForHistoryApi(e),
        type: 'GET', dataType: 'json', timeout: 60000,

        success: function (r) {
            $('#gLD').hide();
            if (r && r.Data && r.Data.length > 0) {
                window._gD = r.Data; $('#gCH').show();
                _gPop(aid, r.Data);
                _gRender(r.Data, aid, window._gF || 'all');
                // FIX: Re-populate attribute panel after a short delay so that
                // DataLogger relay messages (which arrive async via WebSocket) are
                // included. This fixes the blank DataLogger panel after asset switch.
                setTimeout(function () {
                    if (window._gA === aid) _gPop(aid, window._gD);
                }, 1500);
            } else {
                $('#gEM').show(); $('#gPT').text('0');
            }
        },
        error: function () {
            $('#gLD').hide();
            $('#gEM').html('<i class="fas fa-exclamation-triangle" style="color:#f87171;font-size:28px;display:block;margin-bottom:10px;"></i><p style="color:#f87171;font-size:13px;">Failed to load data. Please try again.</p>').show();
        }
    });
}

function _gRender(data, aid, filter) {
    // ── Name maps ──
    var nm = {};
    if (wsLiveData[aid] && wsLiveData[aid].attrs) {
        for (var k in wsLiveData[aid].attrs) {
            var _a = wsLiveData[aid].attrs[k];
            if (_a.AttrId) nm[_a.AttrId] = k;
            if (_a.AssetAttributeId) nm[_a.AssetAttributeId] = k;
        }
    }
    var fb = {
        1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)',
        5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)', 9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA',
        13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA', 25: 'A End - NWKR', 26: 'A End - RWKR',
        27: 'B End - NWKR', 28: 'B End - RWKR', 31: 'Rx1 mV', 32: 'Rx2 mV', 33: 'Tx1 V', 34: 'Tx2 V',
        35: 'Supply V', 36: 'Modem mV', 154: 'Root V', 155: 'Root mA', 227: 'UG V', 228: 'UG mA',
        327: 'HHPR', 328: 'DPR', 329: 'HPR', 337: 'PILOT mA', 499: 'PILOT V', 610: 'Co_Hg mA', 611: 'Co_Hg V'
    };
    var isPM = $('#drpAssetType').val() == '3',
        pmOk = { 25: 1, 26: 1, 27: 1, 28: 1, 212: 1, 213: 1, 214: 1, 215: 1, 216: 1, 217: 1, 218: 1, 219: 1 };

    // ── Step 1: Build per-attribute valueMaps and track all unique timestamps ──
    var allTimestamps = {};   // ts -> true
    var attrMeta = [];        // [{id, rn, dn, valueMap, isBin, isDL}]
    var rawAttrByName = {};   // rn -> valueMap (for derived calc)

    // Build relay whitelist for DL detection
    var knownRelayDisplayNames = {};
    if (wsLiveData[aid] && wsLiveData[aid].dlRelays) {
        Object.keys(wsLiveData[aid].dlRelays).forEach(function (k) {
            var rl = wsLiveData[aid].dlRelays[k];
            var dn2 = rl.displayName || k;
            knownRelayDisplayNames[k] = true;
            knownRelayDisplayNames[k.toLowerCase().trim()] = true;
            knownRelayDisplayNames[dn2] = true;
            knownRelayDisplayNames[dn2.toLowerCase().trim()] = true;
        });
    }
    var hasLiveRelays = Object.keys(knownRelayDisplayNames).length > 0;

    data.forEach(function (at) {
        var id = at.AttributeId;
        if (isPM && !pmOk[id]) return;
        if (!at.Values) return;

        var dlName = dlAssetRoleMap[String(aid) + '_' + String(id)] || null;
        var rn = dlName || nm[id] || fb[id] || ('Attr ' + id);
        var dn = dlName || ((typeof getAttrDisplayNamePlain === 'function') ? getAttrDisplayNamePlain(rn, id) : rn);



        // Collect all valid values into valueMap {ms -> value}
        var valueMap = {};
        var allVals = [];
        for (var key in at.Values) {
            var e = at.Values[key];
            if (!e) continue;
            // Value field
            var rawVal = e.Value;
            if (rawVal === undefined || rawVal === null) continue;
            var val = parseFloat(rawVal);
            if (isNaN(val)) continue;
            // Timestamp -- try multiple paths
            var ts = null;
            if (e.Timestamp) {
                ts = e.Timestamp.TimestampDevice || e.Timestamp.TimestampLocal || e.Timestamp.TimestampChange;
            } else if (e.TimestampDevice) {
                ts = e.TimestampDevice;
            } else if (e.TimestampLocal) {
                ts = e.TimestampLocal;
            }
            if (!ts || ts.indexOf('0001') >= 0) continue;
            var ms = new Date(ts).getTime();
            if (isNaN(ms) || ms <= 0) continue;
            valueMap[ms] = val;
            allVals.push(val);
            allTimestamps[ms] = true;
        }

        if (!Object.keys(valueMap).length) return;

        // Determine if relay (DataLogger binary)
        var historyDL = at.Values['1'] && at.Values['1'].DataType === 'DataLogger';
        var allBinary = allVals.length > 0 && allVals.every(function (v) { return v === 0 || v === 1; });
        var isDL = false;
        if (historyDL && allBinary) {
            if (!hasLiveRelays) {
                isDL = true;
            } else {
                var dnL = dn.toLowerCase().trim(), rnL = rn.toLowerCase().trim();
                isDL = !!(knownRelayDisplayNames[dn] || knownRelayDisplayNames[rn] ||
                    knownRelayDisplayNames[dnL] || knownRelayDisplayNames[rnL]);
            }
        }


        attrMeta.push({ id: id, rn: rn, dn: dn, valueMap: valueMap, isDL: isDL });
        if (!isDL) {
            rawAttrByName[rn] = valueMap;
            if (dn !== rn) rawAttrByName[dn] = valueMap;
            // Inject short-name alias by AttrId so calculateDerivedValues always
            // finds values even when history loads before live data (rn = alias name)
            var _shortByAttrId = { 1: 'If mA', 2: 'Ir mA', 4: 'Choke V', 5: 'Charger mA' };
            if (_shortByAttrId[id] && !rawAttrByName[_shortByAttrId[id]]) {
                rawAttrByName[_shortByAttrId[id]] = valueMap;
            }
        }
    });

    if (!attrMeta.length) {
        $('#gLD').hide(); $('#gCH').hide(); $('#gEM').show(); $('#gPT').text('0'); return;
    }

    // ── Step 2: Build sorted union of all timestamps ──
    var sortedTs = Object.keys(allTimestamps).map(Number).sort(function (a, b) { return a - b; });

    // ── Step 3: Build series using carry-forward last-known-value ──
    var sr = [], tp = 0, ci = 0, maxMA = 0, maxV = 0, hasBin = false, hasMA = false, hasV = false;
    var binSeriesSet = {};
    var seriesDataMap = {};  // seriesName -> sorted [ms,val] of ACTUAL data points

    attrMeta.forEach(function (meta) {
        var dn = meta.dn, rn = meta.rn, isDL = meta.isDL, valueMap = meta.valueMap;
        var lookupKey = isDL ? ('DL:' + dn) : dn;
        if (_gChecked[lookupKey] === false) return;

        var nl = dn.toLowerCase();
        var isMA = (!isDL && (nl.indexOf('ma') > -1 || nl.indexOf('mv') > -1 || nl.indexOf('-c') > -1));

        // Apply type filter
        if (filter === 'ma' && (!isMA || isDL)) return;
        if (filter === 'v' && (isMA || isDL)) return;
        if (filter === 'bin' && !isDL) return;
        if (filter === 'derived') return; // derived handled separately

        var co = _gColorMap[lookupKey] || _gColorMap[dn] || (_gColorMap[lookupKey] = _gC[ci % _gC.length]);
        ci++;

        // Carry-forward: iterate union timestamps
        var points = [];
        var lastVal = null;
        var actualPts = [];  // only real data points for seriesDataMap

        sortedTs.forEach(function (ts) {
            if (valueMap[ts] !== undefined) {
                lastVal = valueMap[ts];
                actualPts.push([ts, lastVal]);
            }
            if (lastVal === null) return; // no value yet
            points.push({
                value: [ts, lastVal],
                symbol: (valueMap[ts] !== undefined && !isDL) ? 'circle' : 'none',
                symbolSize: (valueMap[ts] !== undefined && !isDL) ? 3 : 0
            });
        });

        if (!points.length) return;

        // Track stats
        var actVals = actualPts.map(function (p) { return p[1]; });
        var serMax = actVals.length ? Math.max.apply(null, actVals) : 0;
        if (!isDL) { if (isMA && serMax > maxMA) maxMA = serMax; if (!isMA && serMax > maxV) maxV = serMax; }

        var firstTs = points[0].value[0], lastTs = points[points.length - 1].value[0];
        // (dataMinTs/maxTs not needed -- we use _gReqStart/_gReqEnd for axis)

        tp += actualPts.length;
        var yIdx = isDL ? 2 : (isMA ? 0 : 1);
        if (isDL) hasBin = true; else if (isMA) hasMA = true; else hasV = true;
        if (isDL) binSeriesSet[isDL ? ('[DL] ' + dn) : dn] = true;

        var sName = isDL ? ('[DL] ' + dn) : dn;
        seriesDataMap[sName] = actualPts; // store actual pts for tooltip

        sr.push({
            name: sName,
            type: 'line', smooth: false, step: 'end',
            connectNulls: true,
            showSymbol: true,
            clip: false,
            yAxisIndex: yIdx,
            lineStyle: { width: isDL ? 2 : 1.6, color: co, type: 'solid' },
            itemStyle: { color: co },
            data: points
        });
    });

    // ── Step 4: Derived series (Track only) ──
    if (window._gIsTrack && typeof calculateDerivedValues === 'function' && Object.keys(rawAttrByName).length > 0) {
        // Helper: last known val from a valueMap at time t
        function lkv(vmap, t) {
            var keys = Object.keys(vmap).map(Number).sort(function (a, b) { return a - b; });
            var res = 0;
            for (var i = 0; i < keys.length; i++) { if (keys[i] <= t) res = vmap[keys[i]]; else break; }
            return res;
        }

        // For each timestamp compute derived
        var derivedPts = {};
        _DERIVED_DEFS.forEach(function (d) { derivedPts[d.key] = []; });

        sortedTs.forEach(function (t) {
            var snap = {};
            Object.keys(rawAttrByName).forEach(function (an) {
                snap[an] = { Value: lkv(rawAttrByName[an], t) };
            });
            var dv = calculateDerivedValues(snap);
            _DERIVED_DEFS.forEach(function (def) {
                var v = dv[def.key];
                if (v !== undefined && v !== null && !isNaN(v))
                    derivedPts[def.key].push([t, v]);
            });
        });

        _DERIVED_DEFS.forEach(function (def) {
            var lookupKey = 'DRV:' + def.key;
            if (_gChecked[lookupKey] === false) return;
            if (!derivedPts[def.key].length) return;
            if (filter === 'bin' || filter === 'ma' || filter === 'v') return;

            var pts = derivedPts[def.key];
            var serMax = Math.max.apply(null, pts.map(function (p) { return p[1]; }));
            if (serMax > maxMA) maxMA = serMax;
            hasMA = true;

            var sName = '[D] ' + def.label;
            seriesDataMap[sName] = pts;
            tp += pts.length;

            // Build carry-forward points with dots only at computed timestamps
            var dvMap = {};
            pts.forEach(function (p) { dvMap[p[0]] = p[1]; });
            var dvPoints = [];
            var dvLast = null;
            sortedTs.forEach(function (ts) {
                if (dvMap[ts] !== undefined) { dvLast = dvMap[ts]; }
                if (dvLast === null) return;
                dvPoints.push({
                    value: [ts, dvLast],
                    symbol: (dvMap[ts] !== undefined) ? 'circle' : 'none',
                    symbolSize: (dvMap[ts] !== undefined) ? 3 : 0
                });
            });

            sr.push({
                name: sName,
                type: 'line', smooth: false, step: 'end',
                connectNulls: true, showSymbol: true, clip: false,
                yAxisIndex: 0,
                lineStyle: { width: 1.5, color: def.color, type: 'solid' },
                itemStyle: { color: def.color },
                data: dvPoints
            });
        });
    }

    $('#gPT').text(tp);
    if (!sr.length) { $('#gCH').hide(); $('#gEM').show(); return; }
    $('#gCH').show(); $('#gEM').hide(); $('#gToolbar').show();
    if (mainGraphChart) { mainGraphChart.dispose(); mainGraphChart = null; }
    mainGraphChart = echarts.init(document.getElementById('gCH'), null, { renderer: 'canvas' });

    var gridRight = 20 + (hasV ? 70 : 0) + (hasBin ? 70 : 0);
    var yAxes = [
        // mA axis -- left
        {
            type: 'value', min: 0, max: maxMA > 0 ? parseFloat((maxMA * 1.20).toFixed(2)) : 'dataMax',
            name: 'mA', nameLocation: 'end',
            nameTextStyle: { fontSize: 13, fontWeight: 'bold', color: '#f87171', padding: [0, 0, 4, 0], fontFamily: 'Manrope,sans-serif' },
            axisLabel: { fontSize: 11, color: 'rgba(248,113,113,0.80)', formatter: '{value}', fontFamily: 'JetBrains Mono,monospace', margin: 10 },
            axisLine: { show: true, lineStyle: { color: 'rgba(248,113,113,0.35)', width: 1.5 } },
            axisTick: { show: true, lineStyle: { color: 'rgba(248,113,113,0.25)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(248,113,113,0.06)', type: 'dashed', width: 1 } },
            show: hasMA
        },
        // V axis -- right
        {
            type: 'value', min: 0, max: maxV > 0 ? parseFloat((maxV * 1.20).toFixed(2)) : 'dataMax',
            name: 'V', nameLocation: 'end',
            nameTextStyle: { fontSize: 13, fontWeight: 'bold', color: '#60a5fa', padding: [0, 4, 0, 0], fontFamily: 'Manrope,sans-serif' },
            axisLabel: { fontSize: 11, color: 'rgba(96,165,250,0.80)', formatter: '{value}', fontFamily: 'JetBrains Mono,monospace', margin: 10 },
            axisLine: { show: true, lineStyle: { color: 'rgba(96,165,250,0.35)', width: 1.5 } },
            axisTick: { show: true, lineStyle: { color: 'rgba(96,165,250,0.25)' } },
            splitLine: { show: !hasMA, lineStyle: { color: 'rgba(96,165,250,0.06)', type: 'dashed', width: 1 } },
            offset: 0, show: hasV
        },
        // 0/1 relay axis -- far right
        {
            type: 'value', min: -0.25, max: 1.7, name: '0/1', nameLocation: 'end',
            nameTextStyle: { fontSize: 13, fontWeight: 'bold', color: '#a78bfa', padding: [0, 4, 0, 0], fontFamily: 'Manrope,sans-serif' },
            axisLabel: {
                fontSize: 11, color: 'rgba(167,139,250,0.80)', interval: 0,
                formatter: function (v) { return v === 0 ? '0' : v === 1 ? '1' : ''; },
                fontFamily: 'JetBrains Mono,monospace', margin: 10
            },
            axisLine: { show: true, lineStyle: { color: 'rgba(167,139,250,0.35)', width: 1.5 } },
            axisTick: { show: true, lineStyle: { color: 'rgba(167,139,250,0.25)' } },
            splitLine: { show: false },
            offset: hasV ? 68 : 0, minInterval: 1, show: hasBin
        }
    ];

    mainGraphChart.setOption({
        backgroundColor: 'transparent', animation: false,
        textStyle: { fontFamily: 'Manrope,JetBrains Mono,sans-serif' },
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'line', lineStyle: { color: 'rgba(34,211,238,0.60)', width: 1.5, type: 'dashed' } },
            confine: false,
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(255,255,255,0.12)', borderWidth: 1, padding: [10, 14],
            extraCssText: 'box-shadow:0 8px 32px rgba(0,0,0,.5);border-radius:10px;backdrop-filter:blur(20px);',
            formatter: function (params) {
                if (!params || !params.length) return '';
                var hoverMs = params[0].value[0];
                var d = new Date(hoverMs);
                var tm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
                var hdr = '<div style="font-weight:700;font-size:12px;color:#22d3ee;margin-bottom:8px;padding-bottom:6px;border-bottom:1px solid rgba(255,255,255,0.10);white-space:nowrap;font-family:JetBrains Mono,monospace;display:flex;align-items:center;gap:6px;">'
                    + '<i class="fas fa-clock" style="font-size:10px;opacity:.75;"></i>' + tm + '</div>';

                function lkTooltip(sName) {
                    var pts = seriesDataMap[sName];
                    if (!pts || !pts.length) return null;
                    var lo = 0, hi = pts.length - 1, idx = -1;
                    while (lo <= hi) { var mid = (lo + hi) >> 1; if (pts[mid][0] <= hoverMs) { idx = mid; lo = mid + 1; } else { hi = mid - 1; } }
                    return idx >= 0 ? pts[idx][1] : null;
                }

                var rowItems = [];
                for (var si = 0; si < sr.length; si++) {
                    var s = sr[si];
                    var sName = s.name;
                    var co2 = s.lineStyle && s.lineStyle.color ? s.lineStyle.color : '#999';
                    var val = lkTooltip(sName);
                    var hasData = (val !== null);
                    var displayVal = hasData ? val : 0;
                    var isBinS = !!binSeriesSet[sName];
                    var isDrvS = sName.indexOf('[D] ') === 0;
                    var dispName = sName.replace(/^\[DL\] /, '').replace(/^\[D\] /, '');
                    var valHtml;
                    if (isBinS) {
                        valHtml = '<span style="background:' + (displayVal === 1 ? 'linear-gradient(135deg,#10b981,#059669)' : 'linear-gradient(135deg,#f59e0b,#d97706)') + ';color:#fff;padding:2px 8px;border-radius:4px;font-size:10px;font-weight:700;flex-shrink:0;">' + (displayVal === 1 ? 'Pickup' : 'Drop') + '</span>';
                    } else {
                        valHtml = '<b style="color:' + (hasData ? (isDrvS ? '#fbbf24' : co2) : 'rgba(255,255,255,0.25)') + ';font-size:11px;font-family:JetBrains Mono,monospace;flex-shrink:0;min-width:42px;text-align:right;">' + displayVal.toFixed(2) + '</b>';
                    }
                    rowItems.push(
                        '<div style="display:flex;align-items:center;gap:6px;margin:2px 0;min-width:150px;">' +
                        '<span style="width:9px;height:9px;border-radius:50%;background:' + co2 + ';flex-shrink:0;opacity:' + (hasData ? '1' : '0.25') + ';box-shadow:0 0 4px ' + co2 + ';"></span>' +
                        '<span style="font-size:11px;color:' + (hasData ? 'rgba(255,255,255,0.82)' : 'rgba(255,255,255,0.28)') + ';flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-family:Manrope,sans-serif;" title="' + dispName + '">' + dispName + '</span>' +
                        valHtml + '</div>'
                    );
                }

                if (!rowItems.length) return '';

                var total = rowItems.length;
                var cols = total <= 12 ? 1 : total <= 24 ? 2 : 3;
                var perCol = Math.ceil(total / cols);
                var colWidth = cols === 1 ? '180px' : cols === 2 ? '180px' : '170px';

                var colsHtml = '';
                for (var c = 0; c < cols; c++) {
                    var slice = rowItems.slice(c * perCol, (c + 1) * perCol);
                    colsHtml += '<div style="display:flex;flex-direction:column;min-width:' + colWidth + ';' + (c < cols - 1 ? 'border-right:1px solid rgba(255,255,255,0.08);padding-right:12px;margin-right:12px;' : '') + '">'
                        + slice.join('') + '</div>';
                }

                return hdr + '<div style="display:flex;flex-direction:row;align-items:flex-start;">' + colsHtml + '</div>';
            }
        },
        legend: { show: false }, toolbox: { show: false },
        grid: { left: hasMA ? 70 : 18, right: gridRight, bottom: 60, top: 32, containLabel: false },
        xAxis: [{
            type: 'time', boundaryGap: false,
            min: (_gReqStart !== null ? _gReqStart : undefined),
            max: (_gReqEnd !== null ? Math.min(_gReqEnd, Date.now()) : undefined),
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.18)', width: 1.5 } },
            axisTick: { lineStyle: { color: 'rgba(255,255,255,0.12)' } },
            axisLabel: {
                fontSize: 11, color: 'rgba(255,255,255,0.45)', fontFamily: 'JetBrains Mono,monospace', margin: 12,
                formatter: function (v) { var d = new Date(v); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); }
            },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.05)', width: 1 } }
        }],
        yAxis: yAxes,
        dataZoom: [
            {
                type: 'slider', height: 22, start: 0, end: 100, bottom: 4,
                backgroundColor: 'rgba(255,255,255,0.03)',
                borderColor: 'rgba(255,255,255,0.09)',
                handleStyle: { color: '#22d3ee', borderColor: '#22d3ee', borderWidth: 2 },
                fillerColor: 'rgba(34,211,238,0.10)',
                textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.42)', fontFamily: 'JetBrains Mono,monospace' },
                labelFormatter: function (v) { var d = new Date(v); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); }
            },
            { type: 'inside', zoomOnMouseWheel: true, moveOnMouseMove: true }
        ],
        color: _gC, series: sr
    });
    $(window).off('resize.mg').on('resize.mg', function () { if (mainGraphChart) mainGraphChart.resize(); });
}


function fnBindYard() { var s = { SiteId: $('#drpSite').val(), AssetId: $('#drpAsset').val(), AssetTypeId: $('#drpAssetType').val() }; $("#loader").show(); $.ajax({ url: '/FRS25/Telemetry/_Yard', type: 'POST', contentType: 'application/json', data: JSON.stringify(s), success: function (d) { $("#loader").hide(); $('#divTelemetryLive').empty().append(d); }, error: function () { $("#loader").hide(); showError('Failed to load yard', 'Error'); } }); }

// ================================================================
// CIRCUIT VIEW WITH WEBSOCKET LIVE DATA
// ================================================================
var circuitAssetId = null;
var circuitWsConnected = false;

var _circuitScriptsLoaded = false;

function loadCircuitScriptsOnce(htmlContent, callback) {
    // ── 1. Extract and inject CSS <link> tags into <head> ──────────────────
    // <link> tags inside innerHTML of a <div> are NOT processed by browsers.
    // We must move them to <head> manually.
    var cssRegex = /<link[^>]+href=[\"']([^\"']+)[\"'][^>]*>/gi;
    var csMatch;
    while ((csMatch = cssRegex.exec(htmlContent)) !== null) {
        var href = csMatch[1].replace(/^~\//, '/');
        if (!document.querySelector('link[href="' + href + '"]')) {
            var lnk = document.createElement('link');
            lnk.rel = 'stylesheet';
            lnk.href = href;
            document.head.appendChild(lnk);
        }
    }

    // ── 2. Extract data-circuit-script src URLs ────────────────────────────
    var srcRegex = /<script[^>]+data-circuit-script[^>]+src=["']([^"']+)["'][^>]*><\/script>/gi;
    var scripts = [];
    var match;
    while ((match = srcRegex.exec(htmlContent)) !== null) {
        scripts.push(match[1]);
    }

    // Strip the data-circuit-script tags AND link tags from HTML -- we handle them manually
    var cleanHtml = htmlContent.replace(/<script[^>]+data-circuit-script[^>]*>[\s\S]*?<\/script>/gi, '');
    cleanHtml = cleanHtml.replace(/<link[^>]+rel=["']stylesheet["'][^>]*>/gi, '');

    // Inject the clean HTML (DOM elements only -- NO inline script eval yet)
    // We must eval inline scripts AFTER external scripts finish loading so
    // that App.MainView exists when "appModel = new App.MainView(...)" runs.
    // Circuit partial uses its own white background -- render directly
    $('#divTelemetryLive').empty()[0].innerHTML = cleanHtml;

    function runInlineScripts() {
        // Run any inline <script> blocks in order
        $('#divTelemetryLive').find('script').each(function () {
            if (!this.src) {
                try { window.eval(this.textContent || this.innerText); } catch (e) {
                    console.warn('[Circuit] Inline script eval error:', e);
                }
            }
        });
    }

    if (_circuitScriptsLoaded || scripts.length === 0) {
        // External scripts already in page -- just run inline scripts then callback
        console.log('[Circuit] External scripts already loaded -- running inline scripts');
        runInlineScripts();
        // Small delay to allow $(document).ready inside inline script to fire
        setTimeout(callback, 200);
        return;
    }

    // Load each script sequentially (order matters for JointJS/Backbone dependencies)
    function loadNext(index) {
        if (index >= scripts.length) {
            _circuitScriptsLoaded = true;
            console.log('[Circuit] All circuitsip scripts loaded once');
            // NOW it is safe to eval inline scripts (App, joint etc. are defined)
            runInlineScripts();
            // Allow $(document).ready inside inline script to execute
            setTimeout(callback, 200);
            return;
        }
        var src = scripts[index];
        // Resolve Razor ~ paths to absolute
        src = src.replace(/^~\//, '/');
        if (document.querySelector('script[src="' + src + '"]')) {
            // Already in DOM -- skip
            loadNext(index + 1);
            return;
        }
        var s = document.createElement('script');
        s.src = src;
        s.onload = function () { loadNext(index + 1); };
        s.onerror = function () {
            console.warn('[Circuit] Failed to load:', src);
            loadNext(index + 1);
        };
        document.head.appendChild(s);
    }
    loadNext(0);
}

function fnBindCircuit() {
    var siteId = $('#drpSite').val();
    var atId = $('#drpAssetType').val();
    var aId = $('#drpAsset').val();

    if (!aId || aId === '' || aId === '0') {
        showWarning('Please select an asset', 'Validation');
        return;
    }

    circuitAssetId = aId;

    // Disconnect any existing WebSocket first
    disconnectWebSocket();

    $("#loader").show();

    $.ajax({
        url: '/FRS25/Telemetry/_Circuit',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ assetTypeId: atId, assetId: aId }),
        // FIX: Use text dataType so jQuery does NOT auto-eval <script src> tags.
        // We handle script loading manually via loadCircuitScriptsOnce().
        dataType: 'text',
        success: function (d) {
            $("#loader").hide();

            loadCircuitScriptsOnce(d, function () {
                // Connect WebSocket for live data updates AFTER circuit + scripts are ready
                setTimeout(function () {
                    if (siteId && siteId !== '' && siteId !== '0') {
                        connectCircuitWebSocket(siteId, atId, aId);
                    }
                }, 500);
                console.log('[Circuit] Loaded circuit diagram for asset:', aId);
            });
        },
        error: function () {
            $("#loader").hide();
            showError('Failed to load circuit', 'Error');
        }
    });
}

function connectCircuitWebSocket(siteId, assetTypeId, assetId) {
    // When MQTT (Paho) is the primary live data source for _Circuit.cshtml,
    // we do NOT open a parallel WebSocket (would corrupt parseJson).
    // HOWEVER for Point Machine we still need to push any WS data already
    // accumulated in wsLiveData into the circuit.
    // We do this via a lightweight polling bridge interval.
    if (typeof Paho !== 'undefined' && typeof client !== 'undefined' && client) {
        console.log('[Circuit-WS] MQTT active -- using polling bridge for circuit data');
        wsCurrentAssetTypeId = assetTypeId;
        wsCurrentFilterAssetIds = [assetId];
        if (!wsLiveData[assetId]) {
            wsLiveData[assetId] = { AssetId: assetId, attrs: {}, dlRelays: {}, lastUpdated: new Date() };
        }

        // Clear any previous bridge interval
        if (window._circuitMqttBridgeInterval) {
            clearInterval(window._circuitMqttBridgeInterval);
            window._circuitMqttBridgeInterval = null;
        }

        // Poll every 2 seconds -- push wsLiveData into circuit if data exists
        window._circuitMqttBridgeInterval = setInterval(function () {
            // Stop if circuit view is no longer active
            if (!circuitAssetId || circuitAssetId != assetId) {
                clearInterval(window._circuitMqttBridgeInterval);
                window._circuitMqttBridgeInterval = null;
                return;
            }
            var asset = wsLiveData[assetId];
            if (asset && asset.attrs && Object.keys(asset.attrs).length > 0) {
                updateCircuitFromWebSocket(assetId);
            }
        }, 2000);

        return;
    }

    // Clear MQTT bridge if switching to direct WS
    if (window._circuitMqttBridgeInterval) {
        clearInterval(window._circuitMqttBridgeInterval);
        window._circuitMqttBridgeInterval = null;
    }

    // Reset state but DON'T clear the container (circuit is already loaded)
    wsLiveData = {};
    wsMessageCount = 0;
    wsBatchCount = 0;
    circuitWsConnected = false;

    wsCurrentAssetTypeId = assetTypeId;
    wsCurrentFilterAssetIds = [assetId];

    var wsUrl = buildWebSocketUrl(siteId, assetTypeId, [assetId]);

    // SSL auth — same token-injection as connectWebSocket. Required for WSS.
    if (typeof isSecure !== 'undefined' && isSecure && APP_CONFIG && APP_CONFIG.WebSocketAuthToken) {
        var separator = wsUrl.indexOf('?') > -1 ? '&' : '?';
        wsUrl = wsUrl + separator + 'token=' + encodeURIComponent(APP_CONFIG.WebSocketAuthToken);
        console.log('[Circuit-WS] Auth token appended for SSL');
    }

    console.log('[Circuit-WS] Connecting to:', wsUrl);

    try {
        wsConnection = new WebSocket(wsUrl);

        wsConnection.onopen = function () {
            console.log('[Circuit-WS] Connected successfully for asset:', assetId);
            wsIsConnected = true;
            circuitWsConnected = true;
            wsLastMessageTime = Date.now();
            showWsStatus('connected');

            if ($('#circuitDiagramLiveGif').length) {
                $('#circuitDiagramLiveGif').attr('src', '/assets/images/Live_green.gif');
            }
        };

        wsConnection.onmessage = function (event) {
            wsLastMessageTime = Date.now();

            try {
                var rawData = event.data;
                var batch;

                if (typeof rawData === 'string') {
                    batch = JSON.parse(rawData);
                } else {
                    batch = rawData;
                }

                var messages = [];
                if (Array.isArray(batch)) {
                    messages = batch;
                } else if (batch && batch.Messages) {
                    messages = batch.Messages;
                } else if (batch && batch.Data) {
                    messages = batch.Data;
                } else if (batch && batch.AssetId) {
                    messages = [batch];
                }

                for (var i = 0; i < messages.length; i++) {
                    var msg = messages[i];
                    if (typeof msg === 'string') {
                        try { msg = JSON.parse(msg); } catch (e) { continue; }
                    }
                    if (msg && msg.AssetId) {
                        processCircuitWsMessage(msg);
                    }
                }
            } catch (e) {
                console.warn('[Circuit-WS] Error processing message:', e);
            }
        };

        wsConnection.onclose = function () {
            console.log('[Circuit-WS] Connection closed');
            wsIsConnected = false;
            circuitWsConnected = false;
        };

        wsConnection.onerror = function (err) {
            console.error('[Circuit-WS] Error:', err);
            wsIsConnected = false;
            circuitWsConnected = false;
        };

    } catch (e) {
        console.error('[Circuit-WS] Failed to connect:', e);
    }
}

// Process WebSocket message for Circuit view
function processCircuitWsMessage(d) {
    var aid = d.AssetId;
    var attrName = d.AssetAttributeName;
    var attrId = d.AssetAttributeId;
    var value = d.Value;

    if (!aid || aid != circuitAssetId) return;

    // Initialize asset data if not exists
    if (!wsLiveData[aid]) {
        wsLiveData[aid] = {
            AssetId: aid,
            AssetName: d.AssetName || ('Asset ' + aid),
            AssetTypeId: d.AssetTypeId,
            attrs: {},
            dlRelays: {},
            lastUpdated: new Date()
        };
    }



    // Handle DataLogger type
    if (d.DataType && d.DataType === 'DataLogger') {
        processCircuitDataLogger(aid, attrName, value, d);
        // FIX: For Point Machine, ALSO store in attrs so updateCircuitPointMachine
        // can read NWKR/RWKR values (PM sends them as DataType='DataLogger')
        var _pmTypeId = parseInt($('#drpAssetType').val() || wsCurrentAssetTypeId || 0);
        if (_pmTypeId === 3) {
            if (!wsLiveData[aid]) return;
            if (!wsLiveData[aid].attrs) wsLiveData[aid].attrs = {};
            wsLiveData[aid].attrs[attrName] = {
                Value: value,
                AttrId: d.AssetAttributeId,
                Timestamp: d.TimestampDevice || new Date().toISOString(),
                changed: true
            };
            // Now also trigger the circuit update so PM values render
            updateCircuitFromWebSocket(aid);
        }
        return;
    }

    // Store attribute value
    wsLiveData[aid].attrs[attrName] = {
        Value: value,
        AttrId: attrId,
        Timestamp: d.TimestampDevice || d.TimestampLocal || new Date().toISOString(),
        changed: true
    };
    wsLiveData[aid].lastUpdated = new Date();

    // Store AssetTypeId from message so updateCircuitFromWebSocket can use it
    if (d.AssetTypeId && !wsLiveData[aid].AssetTypeId) {
        wsLiveData[aid].AssetTypeId = d.AssetTypeId;
    }
    // Also keep wsCurrentAssetTypeId in sync
    if (d.AssetTypeId) wsCurrentAssetTypeId = d.AssetTypeId;

    // Update the circuit diagram
    updateCircuitFromWebSocket(aid);
}

// Process DataLogger messages for Circuit
function processCircuitDataLogger(assetId, attrName, value, d) {
    if (!wsLiveData[assetId]) return;

    var isPickup = (parseInt(value) === 1);

    // Extract relay name from attribute name
    var relayName = attrName;

    wsLiveData[assetId].dlRelays[relayName] = {
        value: value,
        isPickup: isPickup,
        displayName: relayName,
        timestamp: d.TimestampDevice || new Date().toISOString()
    };

    wsLiveData[assetId].lastUpdated = new Date();

    // Update circuit with DataLogger data
    updateCircuitDataLoggerFromWebSocket(assetId);
}

// Update Circuit view with WebSocket data
function updateCircuitFromWebSocket(assetId) {
    if (!circuitAssetId || assetId != circuitAssetId) return;

    var asset = wsLiveData[assetId];
    if (!asset || !asset.attrs) return;

    // Update timestamp
    var d = new Date();
    var time = d.getHours() + ':' + d.getMinutes() + ':' + d.getSeconds();
    $('#circuitDiagramLiveTimeStamp').html('Last Update: ' + time);

    // Update live indicator
    if ($('#circuitDiagramLiveGif').length) {
        $('#circuitDiagramLiveGif').attr('src', '/assets/images/Live_green.gif');
    }

    // Check if JointJS parseJson and appModel exist (from _Circuit.cshtml)
    if (typeof parseJson === 'undefined' || !parseJson || !parseJson.cells) {
        console.log('[Circuit-WS] parseJson not available yet');
        return;
    }
    if (typeof appModel === 'undefined' || !appModel || !appModel.graph) {
        console.log('[Circuit-WS] appModel not available yet');
        return;
    }


    var assetTypeId = parseInt($('#drpAssetType').val() || 0);
    if (!assetTypeId || assetTypeId === 0) {
        assetTypeId = parseInt($('#hdnCircuitDiagramAssetTypeId').val() || 0);
    }
    if (!assetTypeId || assetTypeId === 0) {
        assetTypeId = parseInt(wsCurrentAssetTypeId || 0);
    }
    if (!assetTypeId || assetTypeId === 0) {
        // Last resort: detect from asset data
        var _a = wsLiveData[assetId];
        if (_a && _a.AssetTypeId) assetTypeId = parseInt(_a.AssetTypeId);
    }
    var attrs = asset.attrs;

    // Get ZeroOffset from asset or default
    var zeroOffset = 0;
    if (typeof assetJson !== 'undefined' && assetJson && assetJson.ZeroOffsetValue) {
        zeroOffset = parseFloat(assetJson.ZeroOffsetValue) || 0;
    }

    // Update circuit based on asset type
    if (assetTypeId === 1) {
        // Track Circuit
        updateCircuitTrack(attrs);
    } else if (assetTypeId === 2) {
        // Signal Circuit
        updateCircuitSignal(attrs, zeroOffset);
    } else if (assetTypeId === 3) {
        // Point Machine Circuit
        updateCircuitPointMachine(attrs);
    }

    // Apply changes to graph
    try {
        appModel.graph.fromJSON(parseJson);
    } catch (e) {
        console.warn('[Circuit-WS] Error updating graph:', e);
    }

    console.log('[Circuit-WS] Updated circuit for asset:', assetId, '| Attrs:', Object.keys(attrs).length);
}
// Maps incoming live attribute names → label prefixes used inside JointJS
// Track Circuit diagrams stored in the DB. Multiple aliases per attribute so
// that old short-code diagrams ('If mA', 'Vr', etc.) AND modern site-aliased
// diagrams ('ITC FEED END(mA)', 'VTC RELAY END(V)', etc.) both bind.
var TRACK_CIRCUIT_LABEL_MAP = {
    'If mA': ['ITC FEED END(mA)', 'ITC FEED END', 'If mA'],
    'Ir mA': ['ITC RELAY END(mA)', 'ITC RELAY END', 'Ir mA'],
    'Vr': ['VTC RELAY END(V)', 'VTC RELAY END', 'Vr'],
    'Vf': ['VTC FEED END(V)', 'VTC FEED END', 'Vf'],
    'Choke V': ['VTC CH FEED END(V)', 'VTC CH FEED END', 'Choke V'],
    'Charger mA': ['ITC TFC O/P(mA)', 'Charger mA'],
    'TPR V': ['VTC 24 DC TPR I/P(V)', 'VTC 24 DC TPR I/P', 'VTC 24 DC TPR', 'TPR V'],
    'TPR V (Loc)': ['VTC 24 DC LOC(V)', 'VTC 24 DC LOC', 'TPR V (Loc)'],
    'Charger V': ['VTC TFC I/P(V)', 'VTC TFC I/P', 'Charger V'],
    'Charger OP V': ['VTC TFC O/P(V)', 'VTC TFC O/P', 'Charger OP V'],
    'TR V (Relay)': ['VTC TR', 'TR V (Relay)']
};
// Update Track Circuit from WebSocket
function updateCircuitTrack(attrs) {
    $.each(parseJson.cells, function (id, val) {
        if (!val.attrs || !val.attrs.label || !val.attrs.label.text) return;
        if (val.type !== 'examples.Label') return;

        // Normalize: strip the "\n(" that this function inserted on the previous
        // tick (see val.attrs.label.text assignment below). Without this, after
        // the first update the cell text becomes "ITC FEED END\n(mA) : 345.9"
        // and subsequent startsWith("ITC FEED END(mA)") matches all fail.
        var labelText = $.trim(val.attrs.label.text).replace(/\n\(/g, '(');

        for (var attrName in attrs) {
            if (!attrs.hasOwnProperty(attrName)) continue;

            var attrData = attrs[attrName];
            var value = attrData.Value;
            if (value === null || value === undefined) continue;

            var num = parseFloat(value);
            // 1 decimal keeps text short enough to fit inside the diagram box
            var displayValue = isNaN(num) ? value : num.toFixed(1);

            // Step 1 — try the curated alias map first. This is what bridges
            // legacy short-code diagrams ('If mA', 'Vr', ...) to modern
            // site-aliased attribute names ('ITC FEED END(mA)', ...).
            var mappedLabels = TRACK_CIRCUIT_LABEL_MAP[attrName];
            var matched = false;
            if (mappedLabels) {
                for (var mi = 0; mi < mappedLabels.length; mi++) {
                    if (labelText.indexOf(mappedLabels[mi]) === 0) {
                        // Insert \n before ( so the unit wraps inside the box
                        var newText = mappedLabels[mi] + ' : ' + displayValue;
                        val.attrs.label.text = newText.replace(/\(/g, '\n(');
                        //val.attrs.label.fill = '#222138';
                        applyCircuitLabelColor(val, attrName, num);
                        matched = true;
                        break;
                    }
                }
            }
            if (matched) break;

            // Step 2 — fallback heuristic for cells whose label already matches
            // the incoming attr name verbatim (or its de-parenthesized form).
            var cleanAttrName = attrName.replace(/\s*\([^)]*\)/g, '').trim();
            if (labelText.indexOf(cleanAttrName) === 0 || labelText.indexOf(attrName) === 0) {
                val.attrs.label.text = cleanAttrName + ' : ' + displayValue;
                //val.attrs.label.fill = '#222138';
                applyCircuitLabelColor(val, attrName, num);
                break;
            }
        }
    });
}
// Update Signal Circuit from WebSocket
// Store last known circuit signal state (to retain when no data yet)
var lastCircuitSignalState = {};

function updateCircuitSignal(attrs, zeroOffset) {
    // Get current asset's datalogger relays
    var asset = circuitAssetId ? wsLiveData[circuitAssetId] : null;
    var dlRelays = asset ? (asset.dlRelays || {}) : {};

    // Helper to check if a datalogger relay is picked up (value = 1)
    function isRelayPickup(relayName) {
        for (var key in dlRelays) {
            if (key.toUpperCase().indexOf(relayName.toUpperCase()) > -1) {
                return dlRelays[key].isPickup === true || dlRelays[key].value === 1;
            }
        }
        return false;
    }

    // Helper to check if relay data exists
    function hasRelayData(relayName) {
        for (var key in dlRelays) {
            if (key.toUpperCase().indexOf(relayName.toUpperCase()) > -1) {
                return true;
            }
        }
        return false;
    }

    // Get relay conditions from DataLogger
    var hasRECR = isRelayPickup('RECR');   // Red aspect relay
    var hasHECR = isRelayPickup('HECR');   // Yellow aspect relay
    var hasHHECR = isRelayPickup('HHECR'); // Double Yellow aspect relay
    var hasDECR = isRelayPickup('DECR');   // Green aspect relay

    // Check if we have ANY relay data at all
    var hasAnyRelayData = hasRelayData('RECR') || hasRelayData('HECR') || hasRelayData('HHECR') || hasRelayData('DECR');

    // Get mA values from RDPMS
    var rgMa = attrs['RG mA'] ? parseFloat(attrs['RG mA'].Value) || 0 : 0;
    var dgMa = attrs['DG mA'] ? parseFloat(attrs['DG mA'].Value) || 0 : 0;
    var hgMa = attrs['HG mA'] ? parseFloat(attrs['HG mA'].Value) || 0 : 0;
    var hhgMa = attrs['HHG mA'] ? parseFloat(attrs['HHG mA'].Value) || 0 : 0;

    // ================================================================
    // SIGNAL CONDITION LOGIC (Based on notebook formula)
    //
    // PRIORITY LOGIC:
    // 1. If we have relay data (DataLogger), use ONLY relay states to determine signal
    // 2. If NO relay data exists, fall back to mA > ZeroOffset logic
    // ================================================================

    // Determine which signal condition is active
    var currentSignal = 'none';

    if (hasAnyRelayData) {
        // ====== USE RELAY DATA TO DETERMINE SIGNAL ======
        console.log('[Circuit-Signal] Using RELAY-based logic');

        if (hasRECR) {
            currentSignal = 'red';
            console.log('[Circuit-Signal] RED: RECR relay is Pickup');
        }
        else if (hasHECR && hasHHECR) {
            currentSignal = 'doubleYellow';
            console.log('[Circuit-Signal] DOUBLE YELLOW: HECR & HHECR are Pickup');
        }
        else if (hasHECR && !hasHHECR) {
            currentSignal = 'singleYellow';
            console.log('[Circuit-Signal] SINGLE YELLOW: HECR is Pickup, HHECR is not');
        }
        else if (hasDECR) {
            currentSignal = 'green';
            console.log('[Circuit-Signal] GREEN: DECR relay is Pickup');
        }
        else {
            currentSignal = 'none';
            console.log('[Circuit-Signal] NONE: All relays are Drop');
        }
    } else {
        // ====== FALLBACK: USE mA VALUES WHEN NO RELAY DATA ======
        console.log('[Circuit-Signal] Using mA-based logic (no relay data)');

        if (rgMa > zeroOffset) {
            currentSignal = 'red';
            console.log('[Circuit-Signal] RED: RG mA (' + rgMa + ') > threshold (' + zeroOffset + ')');
        }
        else if (hhgMa > zeroOffset) {
            currentSignal = 'doubleYellow';
            console.log('[Circuit-Signal] DOUBLE YELLOW: HHG mA (' + hhgMa + ') > threshold (' + zeroOffset + ')');
        }
        else if (hgMa > zeroOffset) {
            currentSignal = 'singleYellow';
            console.log('[Circuit-Signal] SINGLE YELLOW: HG mA (' + hgMa + ') > threshold (' + zeroOffset + ')');
        }
        else if (dgMa > zeroOffset) {
            currentSignal = 'green';
            console.log('[Circuit-Signal] GREEN: DG mA (' + dgMa + ') > threshold (' + zeroOffset + ')');
        }
        else {
            currentSignal = 'none';
            console.log('[Circuit-Signal] NONE: No mA value > threshold');
        }
    }

    // Check if we have any data
    var hasAnyRelayData = hasRelayData('RECR') || hasRelayData('HECR') || hasRelayData('HHECR') || hasRelayData('DECR');
    var hasAnyMaData = (attrs['RG mA'] && attrs['RG mA'].Value !== null) ||
        (attrs['DG mA'] && attrs['DG mA'].Value !== null) ||
        (attrs['HG mA'] && attrs['HG mA'].Value !== null) ||
        (attrs['HHG mA'] && attrs['HHG mA'].Value !== null);

    if (currentSignal === 'none') {
        if (lastCircuitSignalState[circuitAssetId] && (hasAnyRelayData || hasAnyMaData)) {
            // We have data but no condition matched - all values are below threshold
            // and all relays are dropped - so keep lights OFF
            console.log('[Circuit-Signal] No active condition, all values below threshold - lights OFF');
        } else if (lastCircuitSignalState[circuitAssetId] && !hasAnyRelayData && !hasAnyMaData) {
            // No data received yet - retain last state
            currentSignal = lastCircuitSignalState[circuitAssetId];
            console.log('[Circuit-Signal] No data yet, retaining last state: ' + currentSignal);
        } else {
            console.log('[Circuit-Signal] No active condition and no last state - lights OFF');
        }
    } else if (circuitAssetId) {
        // Store the new state
        lastCircuitSignalState[circuitAssetId] = currentSignal;
    }

    // Determine which lights should be ON based on currentSignal
    var showRed = (currentSignal === 'red');
    var showGreen = (currentSignal === 'green');
    var showYellow = (currentSignal === 'singleYellow' || currentSignal === 'doubleYellow');
    var showDoubleYellow = (currentSignal === 'doubleYellow');

    console.log('[Circuit-Signal] Final State: ' + currentSignal +
        ' | Red:' + showRed + ' Yellow:' + showYellow + ' DblYellow:' + showDoubleYellow + ' Green:' + showGreen);

    // First reset all signal circles to dark
    $.each(parseJson.cells, function (id, val) {
        if (val.type === 'examples.SingleCircle') {
            val.attrs.path.fill = '#33334e';
        }
    });

    // Then update with current values
    $.each(parseJson.cells, function (id, val) {
        if (!val.attrs || !val.attrs.label || !val.attrs.label.text) return;

        var labelText = $.trim(val.attrs.label.text);

        // Update Label values
        if (val.type === 'examples.Label') {
            for (var attrName in attrs) {
                if (!attrs.hasOwnProperty(attrName)) continue;

                var attrData = attrs[attrName];
                var value = attrData.Value;
                if (value === null || value === undefined) continue;

                var num = parseFloat(value);
                var displayValue = isNaN(num) ? value : num.toFixed(2);

                if (labelText.startsWith(attrName) || labelText.startsWith(attrName.replace(/\s*\([^)]*\)/g, '').trim())) {
                    val.attrs.label.text = attrName + ' : ' + displayValue;
                    //val.attrs.label.fill = '#222138';
                    break;
                }
            }
        }

        // Update Signal Circle colors based on the formula conditions
        if (val.type === 'examples.SingleCircle') {
            var circleLabel = val.attrs.label ? val.attrs.label.text : '';

            // RED light - turn on if showRed is true
            if (circleLabel === 'RG mA' || circleLabel === 'RG' || circleLabel.indexOf('RG') === 0) {
                if (showRed) {
                    val.attrs.path.fill = '#FF0E0E'; // Red
                }
            }
            // GREEN light - turn on if showGreen is true
            else if (circleLabel === 'DG mA' || circleLabel === 'DG' || circleLabel.indexOf('DG') === 0) {
                if (showGreen) {
                    val.attrs.path.fill = '#00FF00'; // Green
                }
            }
            // YELLOW light (HG) - turn on if showYellow is true
            else if (circleLabel === 'HG mA' || circleLabel === 'HG' || circleLabel.indexOf('HG') === 0) {
                if (showYellow) {
                    val.attrs.path.fill = '#FFBF00'; // Yellow
                }
            }
            // DOUBLE YELLOW light (HHG) - turn on if showDoubleYellow is true
            else if (circleLabel === 'HHG mA' || circleLabel === 'HHG' || circleLabel.indexOf('HHG') === 0) {
                if (showDoubleYellow) {
                    val.attrs.path.fill = '#FFBF00'; // Yellow
                }
            }
        }
    });
}

// Update Point Machine Circuit from WebSocket
function updateCircuitPointMachine(attrs) {
    function getPMVal(names) {
        for (var i = 0; i < names.length; i++) {
            if (attrs[names[i]] !== undefined && attrs[names[i]] !== null) {
                var v = parseFloat(attrs[names[i]].Value);
                if (!isNaN(v)) return v;
            }
        }
        // Also scan all attrs for name contains match (handles dynamic attribute names)
        for (var attrKey in attrs) {
            if (!attrs.hasOwnProperty(attrKey)) continue;
            var k = attrKey.toLowerCase();
            for (var ni = 0; ni < names.length; ni++) {
                if (k === names[ni].toLowerCase()) {
                    var vv = parseFloat(attrs[attrKey].Value);
                    if (!isNaN(vv)) return vv;
                }
            }
        }
        return null; // null means no data received yet (different from 0)
    }

    var aNWKRVal = getPMVal(['A End - NWKR', 'NWKR A End', 'A_NWKR', 'A End-NWKR', 'AEnd-NWKR']);
    var aRWKRVal = getPMVal(['A End - RWKR', 'RWKR A End', 'A_RWKR', 'A End-RWKR', 'AEnd-RWKR']);
    var bNWKRVal = getPMVal(['B End - NWKR', 'NWKR B End', 'B_NWKR', 'B End-NWKR', 'BEnd-NWKR']);
    var bRWKRVal = getPMVal(['B End - RWKR', 'RWKR B End', 'B_RWKR', 'B End-RWKR', 'BEnd-RWKR']);

    // Also scan attrs directly by name fragment in case attribute names differ slightly
    if (aNWKRVal === null || aRWKRVal === null || bNWKRVal === null || bRWKRVal === null) {
        for (var scanKey in attrs) {
            if (!attrs.hasOwnProperty(scanKey)) continue;
            var scanLower = scanKey.toLowerCase();
            var scanVal = parseFloat(attrs[scanKey].Value);
            if (isNaN(scanVal)) continue;
            if (aNWKRVal === null && scanLower.indexOf('nwkr') !== -1 && (scanLower.indexOf('a') !== -1 || scanLower.indexOf('end') !== -1)) {
                if (scanLower.indexOf('b') === -1 && scanLower.indexOf('loc') === -1) aNWKRVal = scanVal;
            }
            if (aRWKRVal === null && scanLower.indexOf('rwkr') !== -1 && (scanLower.indexOf('a') !== -1 || scanLower.indexOf('end') !== -1)) {
                if (scanLower.indexOf('b') === -1 && scanLower.indexOf('loc') === -1) aRWKRVal = scanVal;
            }
            if (bNWKRVal === null && scanLower.indexOf('nwkr') !== -1 && scanLower.indexOf('b') !== -1) {
                if (scanLower.indexOf('loc') === -1) bNWKRVal = scanVal;
            }
            if (bRWKRVal === null && scanLower.indexOf('rwkr') !== -1 && scanLower.indexOf('b') !== -1) {
                if (scanLower.indexOf('loc') === -1) bRWKRVal = scanVal;
            }
        }
    }

    // Default to 0 if still null (no data yet)
    aNWKRVal = aNWKRVal !== null ? aNWKRVal : 0;
    aRWKRVal = aRWKRVal !== null ? aRWKRVal : 0;
    bNWKRVal = bNWKRVal !== null ? bNWKRVal : 0;
    bRWKRVal = bRWKRVal !== null ? bRWKRVal : 0;

    // Known PM label prefixes -- ONLY update cells that start with these
    var pmLabelPrefixes = [
        'A End - NWKR', 'A End - RWKR', 'B End - NWKR', 'B End - RWKR',
        '(A)NWKR', '(A)RWKR', '(B)NWKR', '(B)RWKR',
        '(A)NW V', '(A)RW V', '(A)NW C', '(A)RW C',
        '(B)NW V', '(B)RW V', '(B)NW C', '(B)RW C'
    ];

    $.each(parseJson.cells, function (id, val) {
        if (!val.attrs || !val.attrs.label || !val.attrs.label.text) return;

        var labelText = $.trim(val.attrs.label.text);

        // Guard against SVG path coordinate strings (no letters = not a label)
        if (!/[a-zA-Z]/.test(labelText)) return;

        // Only process cells whose label matches a known PM prefix
        var isPmLabel = false;
        for (var pi = 0; pi < pmLabelPrefixes.length; pi++) {
            if (labelText.startsWith(pmLabelPrefixes[pi])) { isPmLabel = true; break; }
        }
        // Also allow generic attr name match from live data
        if (!isPmLabel) {
            for (var attrName in attrs) {
                if (!attrs.hasOwnProperty(attrName)) continue;
                var cleanA = attrName.replace(/\s*\([^)]*\)/g, '').trim();
                if (labelText.startsWith(cleanA) && cleanA.length > 2) { isPmLabel = true; break; }
            }
        }
        if (!isPmLabel) return;

        if (val.type === 'examples.Label') {
            var labelLower = labelText.toLowerCase();

            // Match NWKR/RWKR labels -- handle both "A End - NWKR" initial and "(A)NWKR:" updated forms
            var isANWKR = (labelText === 'A End - NWKR' || labelText.startsWith('(A)NWKR') ||
                (labelLower.indexOf('nwkr') !== -1 && labelLower.indexOf('a') !== -1 && labelLower.indexOf('b') === -1 && labelLower.indexOf('loc') === -1));
            var isARWKR = (labelText === 'A End - RWKR' || labelText.startsWith('(A)RWKR') ||
                (labelLower.indexOf('rwkr') !== -1 && labelLower.indexOf('a') !== -1 && labelLower.indexOf('b') === -1 && labelLower.indexOf('loc') === -1));
            var isBNWKR = (labelText === 'B End - NWKR' || labelText.startsWith('(B)NWKR') ||
                (labelLower.indexOf('nwkr') !== -1 && labelLower.indexOf('b') !== -1 && labelLower.indexOf('loc') === -1));
            var isBRWKR = (labelText === 'B End - RWKR' || labelText.startsWith('(B)RWKR') ||
                (labelLower.indexOf('rwkr') !== -1 && labelLower.indexOf('b') !== -1 && labelLower.indexOf('loc') === -1));

            if (isANWKR) {
                val.attrs.label.text = '(A)NWKR: ' + aNWKRVal.toFixed(2);
                val.attrs.label.fill = aNWKRVal > 5 ? '#006400' : '#222138';
            }
            else if (isARWKR) {
                val.attrs.label.text = '(A)RWKR: ' + aRWKRVal.toFixed(2);
                val.attrs.label.fill = aRWKRVal > 5 ? '#006400' : '#222138';
            }
            else if (isBNWKR) {
                val.attrs.label.text = '(B)NWKR: ' + bNWKRVal.toFixed(2);
                val.attrs.label.fill = bNWKRVal > 5 ? '#006400' : '#222138';
            }
            else if (isBRWKR) {
                val.attrs.label.text = '(B)RWKR: ' + bRWKRVal.toFixed(2);
                val.attrs.label.fill = bRWKRVal > 5 ? '#006400' : '#222138';
            }
            else {
                // Update other PM attributes by matching attr name
                for (var attrName in attrs) {
                    if (!attrs.hasOwnProperty(attrName)) continue;
                    if (attrName.toLowerCase().indexOf('nwkr') !== -1 ||
                        attrName.toLowerCase().indexOf('rwkr') !== -1) continue; // Already handled above

                    var attrData = attrs[attrName];
                    var value = attrData.Value;
                    if (value === null || value === undefined) continue;

                    var num = parseFloat(value);
                    var displayValue = isNaN(num) ? value : num.toFixed(2);
                    var cleanAttrName = attrName.replace(/\s*\([^)]*\)/g, '').trim();

                    if (labelText.startsWith(cleanAttrName) && cleanAttrName.length > 2) {
                        val.attrs.label.text = cleanAttrName + ' : ' + displayValue;
                        //val.attrs.label.fill = '#222138';
                        break;
                    }
                }
            }
        }

        // Update Text elements for Point Machine (Current/Voltage/Time from signature data)
        if (val.type === 'examples.Text') {
            if (labelText.startsWith('(A)NW V') || labelText.startsWith('(A)RW V')) {
                if (aNWKRVal > 4.5 && typeof pointMachineDataJsonN !== 'undefined') {
                    val.attrs.label.text = '(A)NW V : ' + parseFloat(pointMachineDataJsonN.A_V_AVERAGE || 0).toFixed(2);
                    val.attrs.label.fill = '#222138';
                } else if (aRWKRVal > 4.5 && typeof pointMachineDataJsonR !== 'undefined') {
                    val.attrs.label.text = '(A)RW V : ' + parseFloat(pointMachineDataJsonR.A_V_AVERAGE || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                }
            }
            else if (labelText.startsWith('(A)NW C') || labelText.startsWith('(A)RW C')) {
                if (aNWKRVal > 4.5 && typeof pointMachineDataJsonN !== 'undefined') {
                    val.attrs.label.text = '(A)NW C : ' + parseFloat(pointMachineDataJsonN.A_C_MAX || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                } else if (aRWKRVal > 4.5 && typeof pointMachineDataJsonR !== 'undefined') {
                    val.attrs.label.text = '(A)RW C : ' + parseFloat(pointMachineDataJsonR.A_C_MAX || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                }
            }
            else if (labelText.startsWith('(B)NW V') || labelText.startsWith('(B)RW V')) {
                if (bNWKRVal > 4.5 && typeof pointMachineDataJsonN !== 'undefined') {
                    val.attrs.label.text = '(B)NW V : ' + parseFloat(pointMachineDataJsonN.B_V_AVERAGE || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                } else if (bRWKRVal > 4.5 && typeof pointMachineDataJsonR !== 'undefined') {
                    val.attrs.label.text = '(B)RW V : ' + parseFloat(pointMachineDataJsonR.B_V_AVERAGE || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                }
            }
            else if (labelText.startsWith('(B)NW C') || labelText.startsWith('(B)RW C')) {
                if (bNWKRVal > 4.5 && typeof pointMachineDataJsonN !== 'undefined') {
                    val.attrs.label.text = '(B)NW C : ' + parseFloat(pointMachineDataJsonN.B_C_MAX || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                } else if (bRWKRVal > 4.5 && typeof pointMachineDataJsonR !== 'undefined') {
                    val.attrs.label.text = '(B)RW C : ' + parseFloat(pointMachineDataJsonR.B_C_MAX || 0).toFixed(2);
                    //    val.attrs.label.fill = '#222138';
                }
            }
        }
    });
}
// Update DataLogger relays in Circuit
function updateCircuitDataLoggerFromWebSocket(assetId) {
    if (!circuitAssetId || assetId != circuitAssetId) return;

    var asset = wsLiveData[assetId];
    if (!asset || !asset.dlRelays) return;

    if (typeof parseJson === 'undefined' || !parseJson || !parseJson.cells) return;
    if (typeof appModel === 'undefined' || !appModel || !appModel.graph) return;

    var dlRelays = asset.dlRelays;

    $.each(parseJson.cells, function (id, val) {
        if (!val.attrs || !val.attrs.label || !val.attrs.label.text) return;

        if (val.type === 'examples.LabelDataLogger') {
            var labelText = $.trim(val.attrs.label.text);

            for (var relayName in dlRelays) {
                if (!dlRelays.hasOwnProperty(relayName)) continue;

                var relay = dlRelays[relayName];
                var displayName = relay.displayName || relayName;

                if (labelText.startsWith(displayName) || labelText.startsWith(relayName)) {
                    // FIX: Value=1 always means Pickup -- removed IsDataLoggerJsonReverse
                    // inversion which was causing Pickup/Drop to show inverted
                    var isPickup = relay.isPickup;

                    if (isPickup) {
                        val.attrs.label.fill = '#54BA4A'; // Green for Pickup
                        val.attrs.label.text = displayName + ' \u2191';
                    } else {
                        val.attrs.label.fill = '#FFAA05'; // Orange for Drop
                        val.attrs.label.text = displayName + ' \u2193';
                    }
                    break;
                }
            }
        }
    });

    // Apply changes to graph
    try {
        appModel.graph.fromJSON(parseJson);
    } catch (e) {
        console.warn('[Circuit-WS] Error updating DataLogger:', e);
    }
}

// Apply color to circuit label based on thresholds
function applyCircuitLabelColor(val, attrName, value) {
    var attrLower = attrName.toLowerCase();

    // Vr threshold: danger if > 0.1 and < 2.5, or > 4.2
    if (attrLower.indexOf('vr') > -1 || attrLower === 'vr' || attrLower.indexOf('vtc relay') > -1) {
        if ((value > 0.1 && value < 2.5) || value > 4.2) {
            val.attrs.label.fill = '#FF0E0E'; // Red for danger
        }
    }
    // TPR V threshold: danger if > 0.1 and < 20
    else if (attrLower.indexOf('tpr') > -1) {
        if (value > 0.1 && value < 20) {
            val.attrs.label.fill = '#FF0E0E';
        }
    }
    // Charger mA threshold: danger if < 100
    else if (attrLower.indexOf('charger ma') > -1 || attrLower.indexOf('itc tfc') > -1) {
        if (value < 100) {
            val.attrs.label.fill = '#FF0E0E';
        }
    }
    // Choke V threshold: danger if > 1.8
    else if (attrLower.indexOf('choke') > -1) {
        if (value > 1.8) {
            val.attrs.label.fill = '#FF0E0E';
        }
    }
}

function formatCircuitValue(value, attrName) {
    if (value === null || value === undefined || value === '') return '-';

    var num = parseFloat(value);
    if (isNaN(num)) return value;

    // Format based on attribute type -- all values to 2 decimal places
    if (attrName.indexOf('mA') > -1 || attrName.indexOf('mV') > -1) {
        return num.toFixed(2);
    } else if (attrName.indexOf('V') > -1) {
        return num.toFixed(2);
    } else {
        return num.toFixed(2);
    }
}

function updateCircuitElement(attrName, value, hasChanged) {
    // Placeholder for DOM-based updates if needed
    return;
}

function applyCircuitValueStyling($element, attrName, value) {
    // Placeholder for styling if needed
    return;
}

// Add CSS for circuit value flash animation (kept for potential future use)
if ($('#circuit-ws-styles').length === 0) {
    var circuitStyles = '<style id="circuit-ws-styles">' +
        '.circuit-value-flash { animation: circuitFlash 1.2s ease-out; }' +
        '@@keyframes circuitFlash { 0% { background-color: #bbf7d0 !important; } 100% { background-color: transparent; } }' +
        '.circuit-timestamp { font-size: 11px; color: #64748b; margin-top: 8px; }' +
        '</style>';
    $('head').append(circuitStyles);
}

// ===== GRAPH MODAL - Using HistoryValue API =====
var rdpmsGraphChart = null;
var rdpmsGraphRequestSeq = 0;
var rdpmsGraphXhr = null;
// Use server-side proxy to avoid mixed content (HTTPS page calling HTTP API)
var HISTORY_API_BASE = '/FRS25/Telemetry/GetHistoryData';

// Helper function to format date for HistoryValue API (ddMMyyyy_HHmmss)
function formatDateForHistoryApi(date) {
    var d = new Date(date);
    var day = String(d.getDate()).padStart(2, '0');
    var month = String(d.getMonth() + 1).padStart(2, '0');
    var year = d.getFullYear();
    var hours = String(d.getHours()).padStart(2, '0');
    var minutes = String(d.getMinutes()).padStart(2, '0');
    var seconds = String(d.getSeconds()).padStart(2, '0');
    return day + month + year + '_' + hours + minutes + seconds;
}

// Helper function to format timestamp for display (HH:mm)
function formatTimeForDisplay(timestamp) {
    try {
        var d = new Date(timestamp);
        if (isNaN(d.getTime())) return '';
        return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    } catch (e) { return ''; }
}

// Helper function to format timestamp for tooltip (HH:mm:ss)
function formatTimeForTooltip(timestamp) {
    try {
        var d = new Date(timestamp);
        if (isNaN(d.getTime())) return '';
        return String(d.getHours()).padStart(2, '0') + ':' +
            String(d.getMinutes()).padStart(2, '0') + ':' +
            String(d.getSeconds()).padStart(2, '0');
    } catch (e) { return ''; }
}
function formatDateTimeForGraphDisplay(timestamp) {
    try {
        var d = new Date(timestamp);
        if (isNaN(d.getTime())) return '';

        return String(d.getDate()).padStart(2, '0') + '/' +
            String(d.getMonth() + 1).padStart(2, '0') + ' ' +
            String(d.getHours()).padStart(2, '0') + ':' +
            String(d.getMinutes()).padStart(2, '0');
    } catch (e) {
        return '';
    }
}

function _graphMonthName(idx) {
    return ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][idx] || '';
}

function _graphXAxisLabel(value, spanMs) {
    var d = new Date(value);
    if (isNaN(d.getTime())) return '';

    var hh = String(d.getHours()).padStart(2, '0');
    var mm = String(d.getMinutes()).padStart(2, '0');
    var ss = String(d.getSeconds()).padStart(2, '0');
    var dd = String(d.getDate()).padStart(2, '0');
    var mon = _graphMonthName(d.getMonth());

    if (spanMs <= 2 * 60 * 60 * 1000) {
        return hh + ':' + mm + ':' + ss;
    }

    // For 24H, show date also; otherwise 15:57 - 15:57 looks confusing.
    return dd + ' ' + mon + '\n' + hh + ':' + mm;
}

function _graphIsVoltageSeries(name) {
    name = String(name || '').toLowerCase();

    return (
        name.indexOf('(v)') > -1 ||
        name.indexOf(' v') > -1 ||
        name.indexOf('voltage') > -1 ||
        name.indexOf('vtc') > -1 ||
        name.indexOf('vac') > -1 ||
        name.indexOf('vdc') > -1
    );
}

function _graphGetEntryTimeMs(entry, axisMin, axisMax) {
    if (!entry) return null;

    var t = entry.Timestamp || {};

    // STRICT: Graph must use TimestampDevice only.
    // Do not fallback to TimestampLocal / TimestampEdgeX / TimestampChange.
    var ts = t.TimestampDevice || entry.TimestampDevice || null;

    if (!ts) return null;

    ts = String(ts);

    if (ts.indexOf('0001') >= 0) return null;

    var ms = new Date(ts).getTime();

    if (isNaN(ms) || ms <= 0) return null;

    return ms;
}
// Apply the From / To custom range from the graph modal toolbar.
// Global on purpose: the Apply button calls this via inline onclick, which fires
// reliably even though the modal shell uses event.stopPropagation(). The assetId
// is read from the overlay's data (set in fnGetAssetGraph).
function rdpmsApplyGraphRange() {
    var assetId = $('#rdpmsGraphOverlay').data('assetId');
    if (!assetId) { if (typeof showWarning === 'function') showWarning('Graph is not ready yet.', 'Graph'); return; }

    var fromVal = $('#graphFromDate').val();
    var toVal = $('#graphToDate').val();

    if (!fromVal || !toVal) {
        showWarning('Please select both From and To date/time.', 'Graph');
        return;
    }

    var s = new Date(fromVal);
    var en = new Date(toVal);

    if (isNaN(s.getTime()) || isNaN(en.getTime())) {
        showWarning('Invalid date/time selected.', 'Graph');
        return;
    }
    if (s.getTime() >= en.getTime()) {
        showWarning('From date/time must be before To date/time.', 'Graph');
        return;
    }

    var spanHours = Math.max(1, Math.round((en.getTime() - s.getTime()) / 3600000));
    loadHistoryGraphData(assetId, spanHours, s, en);
}

function fnGetAssetGraph(siteId, assetId) {
    if (!siteId || !assetId) { showWarning('Missing site or asset info', 'Graph'); return; }

    var assetData = wsLiveData[assetId];
    var assetName = (assetData && assetData.AssetName) ? assetData.AssetName : 'Asset ' + assetId;
    var assetTypeId = $('#drpAssetType').val() || '';
    if (assetData && assetData.AssetTypeId) assetTypeId = assetData.AssetTypeId;

    var modalHtml =
        '<div class="tl-modal-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="tl-modal-shell tl-modal-wide" onclick="event.stopPropagation()">' +
        '<div class="tl-modal-head">' +
        '<div class="tl-modal-head-left">' +
        '<span class="tl-modal-icon tl-modal-icon-graph"><i class="fas fa-chart-line"></i></span>' +
        '<div><div class="tl-modal-title">Historical Graph</div>' +
        '<div class="tl-modal-subtitle">' + assetName + '</div></div>' +
        '</div>' +
        '<div class="tl-modal-head-actions">' +
        '<button class="tl-modal-fullscreen-btn" onclick="toggleModalFullscreen(\'rdpmsGraphOverlay\')" title="Toggle Fullscreen"><i class="fas fa-expand"></i></button>' +
        '<button class="tl-modal-close" onclick="closeGraphModal()" title="Close">&times;</button>' +
        '</div>' +
        '</div>' +
        '<div class="tl-modal-toolbar" style="flex-wrap:wrap;gap:10px;">' +
        '<div class="tl-date-filter" style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;color:#94a3b8;margin:0;">From</label>' +
        '<input type="datetime-local" id="graphFromDate" step="1" onkeydown="if(event.key===\'Enter\'){rdpmsApplyGraphRange();}" style="background:rgba(15,23,42,0.70);border:1px solid rgba(255,255,255,0.18);color:#e2e8f0;border-radius:6px;padding:5px 8px;font-size:12px;color-scheme:dark;" />' +
        '<label style="font-size:12px;color:#94a3b8;margin:0;">To</label>' +
        '<input type="datetime-local" id="graphToDate" step="1" onkeydown="if(event.key===\'Enter\'){rdpmsApplyGraphRange();}" style="background:rgba(15,23,42,0.70);border:1px solid rgba(255,255,255,0.18);color:#e2e8f0;border-radius:6px;padding:5px 8px;font-size:12px;color-scheme:dark;" />' +
        '<button type="button" id="graphApplyRange" onclick="rdpmsApplyGraphRange()" style="background:#259dab;border:none;color:#fff;border-radius:6px;padding:6px 14px;font-size:12px;font-weight:600;cursor:pointer;"><i class="fas fa-filter"></i> Apply</button>' +
        '</div>' +
        '<span class="tl-time-range" id="graphTimeRange"></span>' +
        '</div>' +
        '<div class="tl-modal-body">' +
        '<div id="rdpmsGraphLoading" class="tl-modal-loader"><div class="tl-spinner"></div><span>Loading historical data...</span></div>' +
        '<div id="rdpmsGraphChartDiv" style="width:100%;height:500px;display:none;"></div>' +
        '<div id="rdpmsGraphError" class="tl-modal-error" style="display:none;"><i class="fas fa-exclamation-triangle"></i><span>Failed to load graph data</span></div>' +
        '</div>' +
        '</div></div>';

    $('#rdpmsGraphOverlay').remove();
    $('body').append(modalHtml);
    requestAnimationFrame(function () { $('#rdpmsGraphOverlay').addClass('tl-modal-open'); });
    $('body').addClass('tl-modal-active');

    $('#rdpmsGraphOverlay').data({ assetId: assetId, siteId: siteId, assetTypeId: assetTypeId, assetName: assetName });

    $('#rdpmsGraphOverlay')
        .off('click.rdpmsGraphTime', '.tl-time-pill')
        .on('click.rdpmsGraphTime', '.tl-time-pill', function (e) {
            e.preventDefault();
            e.stopPropagation();

            var $btn = $(this);
            var hours = parseInt($btn.attr('data-hours'), 10);

            if (isNaN(hours) || hours <= 0) return;

            $btn.addClass('active').siblings().removeClass('active');

            // Reset to the rolling last-N-hours window.
            loadHistoryGraphData(assetId, hours);
        });

    // From / To custom range filter is handled by the button's inline
    // onclick="rdpmsApplyGraphRange()" (see global function above). Inline handlers
    // on the target element fire reliably even though the modal shell calls
    // event.stopPropagation(), which had been swallowing delegated/bound handlers.

    // Default: show last 24 hours
    loadHistoryGraphData(assetId, 24);
}

// PM Direction Graph - filters by Normal/Reverse indication IDs
// Normal IDs: 25 (A-NWKR), 27 (B-NWKR), 576, 578
// Reverse IDs: 26 (A-RWKR), 28 (B-RWKR), 577, 579
var PM_NORMAL_IDS = { 25: 1, 27: 1, 576: 1, 578: 1 };
var PM_REVERSE_IDS = { 26: 1, 28: 1, 577: 1, 579: 1 };

// A End indication IDs
var PM_A_END_IDS = { 25: 1, 576: 1, 26: 1, 577: 1 };
// B End indication IDs
var PM_B_END_IDS = { 27: 1, 578: 1, 28: 1, 579: 1 };

// Attribute ID to friendly name mapping for indication voltages
var PM_INDICATION_NAMES = {
    25: 'A End - NWKR',
    26: 'A End - RWKR',
    27: 'B End - NWKR',
    28: 'B End - RWKR',
    576: 'A End - NWKR (Loc)',
    577: 'A End - RWKR (Loc)',
    578: 'B End - NWKR (Loc)',
    579: 'B End - RWKR (Loc)'
};

// Colors for each indication ID
var PM_INDICATION_COLORS = {
    25: '#06b6d4',   // A End NWKR - Cyan
    576: '#0891b2',  // A End NWKR Loc - Dark Cyan
    26: '#ef4444',   // A End RWKR - Red
    577: '#dc2626',  // A End RWKR Loc - Dark Red
    27: '#3b82f6',   // B End NWKR - Blue
    578: '#2563eb',  // B End NWKR Loc - Dark Blue
    28: '#f97316',   // B End RWKR - Orange
    579: '#ea580c'   // B End RWKR Loc - Dark Orange
};

// ================================================================
// VIBRATION DATA MODAL -- CSS (injected dynamically)
// ================================================================
(function injectVibrationStyles() {
    if ($('#vibration-modal-styles').length) return;
    $('head').append('<style id="vibration-modal-styles">' +
        /* ── Overlay: pointer-events:auto blocks ALL mouse interaction with page behind.
           user-select:none prevents text selection on the dim backdrop itself.
           The modal uses stopPropagation so clicks inside stay inside. ── */
        '#vibModalOverlay{position:fixed;top:0;left:0;width:100%;height:100%;z-index:9999;display:flex;align-items:center;justify-content:center;pointer-events:auto;user-select:none;-webkit-user-select:none;-moz-user-select:none;}' +
        /* ── Modal shell ── */
        '.vib-modal{background:#fff;border-radius:12px;width:94%;max-width:1120px;max-height:92vh;display:flex;flex-direction:column;box-shadow:0 24px 72px rgba(4,44,67,0.40);overflow:hidden;pointer-events:auto;user-select:text;-webkit-user-select:text;}' +
        /* ── Header -- site navy gradient ── */
        '.vib-modal-header{background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);padding:14px 20px;display:flex;align-items:center;justify-content:space-between;flex-shrink:0;}' +
        '.vib-modal-header h6{color:#fff;margin:0;font-size:16px;font-weight:700;display:flex;align-items:center;gap:9px;}' +
        '.vib-modal-header h6 i{color:#259dab;}' +
        '.vib-close{background:none;border:none;color:#fff;font-size:20px;line-height:1;cursor:pointer;opacity:.75;padding:3px 7px;border-radius:4px;transition:opacity .15s,background .15s;}' +
        '.vib-close:hover{opacity:1;background:rgba(255,255,255,.14);}' +
        /* ── Sub-bar ── */
        '.vib-modal-subbar{background:#f8fafc;border-bottom:1px solid rgba(255,255,255,0.10);padding:7px 20px;font-size:11.5px;color:#64748b;display:flex;align-items:center;gap:7px;flex-shrink:0;}' +
        '.vib-live-dot{width:8px;height:8px;border-radius:50%;background:#10b981;display:inline-block;flex-shrink:0;animation:vibPulse 2s infinite;}' +
        '@@keyframes vibPulse{0%,100%{opacity:1;}50%{opacity:.3;}}' +
        '.vib-last-ts{margin-left:auto;font-size:11px;color:#94a3b8;}' +
        /* ── Body ── */
        '.vib-modal-body{padding:18px;overflow-y:auto;flex:1;background:#f1f5f9;}' +
        /* ── 4-col card grid ── */
        '.vib-card-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;}' +
        '@@media(max-width:900px){.vib-card-grid{grid-template-columns:repeat(3,1fr);}}' +
        '@@media(max-width:620px){.vib-card-grid{grid-template-columns:repeat(2,1fr);}}' +
        /* ── Individual card ── */
        '.vib-card{background:#fff;border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.07);transition:box-shadow .2s,transform .2s;display:flex;flex-direction:column;}' +
        '.vib-card:hover{box-shadow:0 6px 20px rgba(0,0,0,0.13);transform:translateY(-2px);}' +
        /* ── Card header strip -- site navy ── */
        '.vib-card-head{background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);padding:10px 13px;min-height:48px;display:flex;align-items:center;}' +
        '.vib-attr-name{color:#fff;font-size:12px;font-weight:600;line-height:1.35;word-break:break-word;}' +
        /* ── Card body -- IPS-style label:value row ── */
        '.vib-card-body{padding:13px 13px 10px;flex:1;}' +
        '.vib-val-row{display:flex;align-items:baseline;justify-content:space-between;padding:5px 0;border-bottom:1px dashed #f1f5f9;}' +
        '.vib-val-row:last-child{border-bottom:none;}' +
        '.vib-val-label{font-size:11px;color:#64748b;font-weight:500;}' +
        '.vib-val{font-size:22px;font-weight:800;color:#0d9488;text-align:right;line-height:1.2;}' +
        '.vib-val.vib-null{color:#94a3b8;font-weight:400;font-style:italic;font-size:15px;}' +
        /* ── Card footer -- timestamp ── */
        '.vib-card-footer{background:#f8fafc;border-top:1px solid #f1f5f9;padding:6px 13px;font-size:10px;color:#94a3b8;display:flex;align-items:center;gap:5px;}' +
        '.vib-card-footer i{color:#259dab;font-size:9px;}' +
        /* ── Flash animations ── */
        '.vib-card-flash{animation:vibCardFlash 1.2s ease-out;}' +
        '@@keyframes vibCardFlash{0%{box-shadow:0 0 0 3px rgba(37,157,171,0.5);}100%{box-shadow:0 2px 8px rgba(0,0,0,0.07);}}' +
        '.vib-val-flash{animation:vibValFlash 1.2s ease-out;border-radius:3px;}' +
        '@@keyframes vibValFlash{0%{background:#ccfbf1;}100%{background:transparent;}}' +
        /* ── No-data state ── */
        '.vib-no-data{grid-column:1/-1;text-align:center;padding:60px 20px;color:#94a3b8;font-size:13px;}' +
        '</style>');
})();

// ================================================================
// VIBRATION MODAL
// ================================================================
var _vibModalAssetId = null;

/* Read vibration attrs directly from wsLiveData -- the reliable store.
   Never relies on wsVibrationData which may have stale/prefixed names. */
function _getVibAttrs(aid) {
    var asset = wsLiveData[aid] || wsLiveData[parseInt(aid, 10)];
    if (!asset || !asset.attrs) return null;
    var out = {}, latestTs = null, latestMs = 0;
    for (var k in asset.attrs) {
        if (k.toLowerCase().indexOf('vibration') !== -1) {
            var a = asset.attrs[k];
            var ts = a.TimestampDevice || a.Timestamp || null;
            var ms = ts ? new Date(ts).getTime() : 0;
            if (ms < 0) { ts = null; ms = 0; }
            out[k] = { value: a.Value, timestamp: ts };
            if (ms > latestMs) { latestMs = ms; latestTs = ts; }
        }
    }
    if (!Object.keys(out).length) return null;
    return { lastTimestamp: latestTs, attrs: out };
}

/* Strip sensor prefix and -A/-B end suffix -- same label cleanup as IPS cards */
function _vibLabel(attrName) {
    return attrName
        .replace(/^Vibration_Sensor_/i, '')
        .replace(/[-_]\s*[AB]\s*end\s*$/i, '')
        .replace(/_/g, ' ')
        .trim();
}

/* Derive clean display name -- mirrors exact logic used in buildPmCardWithSeriesInfo:
   read raw AssetName from wsLiveData, then add PT- only if not already present.
   This is the SINGLE source of truth for PT- prefixing. */
function _vibAssetDisplayName(aid) {
    var asset = wsLiveData[aid] || wsLiveData[parseInt(aid, 10)];
    var raw = (asset && asset.AssetName) ? asset.AssetName : ('Asset ' + aid);
    /* Strip any existing PT- prefix first to normalise, then re-add exactly once */
    var clean = raw.replace(/^(PT-)+/i, '');
    return 'PT-' + clean;
}

function fnShowVibrationModal(assetId) {
    _vibModalAssetId = String(assetId);
    var displayName = _vibAssetDisplayName(_vibModalAssetId);

    $('#vibModalOverlay').remove();
    var $overlay = $('<div id="vibModalOverlay"></div>');
    /* Click on backdrop (not modal) closes it */
    $overlay.on('mousedown', function (e) {
        if (e.target === $overlay[0]) fnCloseVibrationModal();
    });

    var modal = $(
        '<div class="vib-modal" onclick="event.stopPropagation()">' +
        '<div class="vib-modal-header">' +
        '<h6><i class="fas fa-wave-square"></i> Vibration Data \u2014 ' + displayName + '</h6>' +
        '<button class="vib-close" onclick="fnCloseVibrationModal()">&#x2715;</button>' +
        '</div>' +
        '<div class="vib-modal-subbar">' +
        '<span class="vib-live-dot"></span>' +
        '<span>Live \u2014 auto-updates from WebSocket</span>' +
        '<span class="vib-last-ts" id="vibLastTs"></span>' +
        '</div>' +
        '<div class="vib-modal-body">' +
        '<div class="vib-card-grid" id="vibCardGrid"></div>' +
        '</div>' +
        '</div>'
    );
    $overlay.append(modal);
    $('body').append($overlay);
    fnUpdateVibrationModal(_vibModalAssetId);

    /* 1-second live poller */
    if (window._vibModalPollTimer) clearInterval(window._vibModalPollTimer);
    window._vibModalPollTimer = setInterval(function () {
        if (!_vibModalAssetId) { clearInterval(window._vibModalPollTimer); window._vibModalPollTimer = null; return; }
        fnUpdateVibrationModal(_vibModalAssetId);
    }, 1000);
}

function fnCloseVibrationModal() {
    _vibModalAssetId = null;
    if (window._vibModalPollTimer) { clearInterval(window._vibModalPollTimer); window._vibModalPollTimer = null; }
    $('#vibModalOverlay').remove();
}

function fnFormatVibTs(ts) {
    if (!ts) return '';
    try {
        var d = new Date(ts);
        if (isNaN(d.getTime()) || d.getFullYear() < 2000) return '';
        var p = function (n) { return n < 10 ? '0' + n : n; };
        return p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds()) +
            ' ' + p(d.getDate()) + '/' + p(d.getMonth() + 1);
    } catch (e) { return ''; }
}

function fnUpdateVibrationModal(assetId) {
    if (!_vibModalAssetId || String(assetId) !== _vibModalAssetId) return;
    var $grid = $('#vibCardGrid');
    if (!$grid.length) return;

    var vd = _getVibAttrs(_vibModalAssetId);
    if (!vd) {
        $grid.html(
            '<div class="vib-no-data">' +
            '<i class="fas fa-wave-square" style="font-size:32px;color:#259dab;opacity:.3;display:block;margin-bottom:12px;"></i>' +
            'No vibration data received yet.<br>' +
            '<small style="color:#b0bec5;">Waiting for WebSocket messages\u2026</small>' +
            '</div>'
        );
        return;
    }

    if (vd.lastTimestamp) {
        $('#vibLastTs').text('Last update: ' + fnFormatVibTs(vd.lastTimestamp));
    }

    var attrNames = Object.keys(vd.attrs).sort();
    for (var i = 0; i < attrNames.length; i++) {
        var attrName = attrNames[i];
        var attr = vd.attrs[attrName];
        var label = _vibLabel(attrName);
        var safeId = attrName.replace(/[^a-zA-Z0-9]/g, '_');
        var cardId = 'vibCard_' + _vibModalAssetId + '_' + safeId;
        var valId = 'vibVal_' + _vibModalAssetId + '_' + safeId;
        var tsId = 'vibTs_' + _vibModalAssetId + '_' + safeId;
        var isNull = (attr.value === null || attr.value === undefined);
        var valDisp = isNull ? '\u2014' : Number(attr.value).toFixed(3);
        var tsDisp = fnFormatVibTs(attr.timestamp);
        var valCls = isNull ? 'vib-val vib-null' : 'vib-val';

        var $card = $('#' + cardId);
        if ($card.length) {
            var $v = $('#' + valId);
            if ($v.text() !== valDisp) {
                $v.text(valDisp).removeClass('vib-val-flash');
                void $v[0].offsetWidth;
                $v.addClass('vib-val-flash');
                $card.removeClass('vib-card-flash');
                void $card[0].offsetWidth;
                $card.addClass('vib-card-flash');
            }
            $('#' + tsId).text(tsDisp);
        } else {
            $grid.append(
                '<div class="vib-card" id="' + cardId + '">' +
                '<div class="vib-card-head">' +
                '<span class="vib-attr-name" title="' + attrName + '">' + label + '</span>' +
                '</div>' +
                '<div class="vib-card-body">' +
                '<div class="vib-val-row">' +
                '<span class="vib-val-label">Value</span>' +
                '<span class="' + valCls + '" id="' + valId + '">' + valDisp + '</span>' +
                '</div>' +
                '</div>' +
                '<div class="vib-card-footer">' +
                '<i class="fas fa-clock"></i>' +
                '<span id="' + tsId + '">' + tsDisp + '</span>' +
                '</div>' +
                '</div>'
            );
        }
    }
}

function fnPmDirGraph(assetId, end) {
    var asset = wsLiveData[assetId];
    if (!asset) { showWarning('No data available', 'Graph'); return; }
    var baseName = asset.AssetName || assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

    // Select filter IDs based on direction (A or B)
    var filterIds = (end === 'A') ? PM_A_END_IDS : PM_B_END_IDS;
    var endLabel = (end === 'A') ? 'A End' : 'B End';

    var modalHtml = '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1100px;width:96%;">' +
        '<div class="rdpms-graph-header" style="background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);">' +
        '<h6 style="color:#fff;margin:0;font-size:18px;font-weight:700;display:flex;align-items:center;gap:10px;">' +
        '<i class="fas fa-chart-line"></i> ' + name + ' -- ' + endLabel + ' Indication Voltages' +
        '</h6>' +
        '<button class="rdpms-graph-close" onclick="closeGraphModal()" style="color:#fff;">&times;</button>' +
        '</div>' +
        '<div class="rdpms-graph-body" style="padding:20px;background:transparent;">' +
        '<div class="d-flex justify-content-between align-items-center mb-3 flex-wrap" style="padding:10px 16px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);border-radius:10px;gap:12px;">' +
        // ── Single date filter (loads the full 24h of the chosen day) ──
        '<div class="d-flex align-items-center" style="gap:8px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;color:rgba(255,255,255,0.72);margin:0;font-weight:600;">Date</label>' +
        '<input type="date" id="pmIndDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:5px 10px;color:#e6edf6;background:rgba(255,255,255,0.06);outline:none;color-scheme:dark;"/>' +
        '<button type="button" id="pmIndLoadBtn" style="background:linear-gradient(135deg,#22d3ee,#0891b2);color:#04222b;border:none;border-radius:6px;padding:6px 14px;font-size:12px;font-weight:700;cursor:pointer;display:inline-flex;align-items:center;gap:5px;">' +
        '<i class="fas fa-search"></i> Load</button>' +
        '</div>' +
        '<span style="font-size:13px;color:rgba(255,255,255,0.55);" id="pmIndTimeRange"></span>' +
        '</div>' +
        '<div style="background:rgba(255,255,255,0.03);border:1px solid rgba(255,255,255,0.07);border-radius:12px;padding:16px;">' +
        '<div id="pmIndLoading" style="display:flex;align-items:center;justify-content:center;height:420px;color:rgba(255,255,255,0.6);gap:10px;">' +
        '<i class="fas fa-spinner fa-spin fa-lg"></i> Loading indication data...</div>' +
        '<div id="pmIndChartDiv" style="width:100%;height:450px;display:none;"></div>' +
        '<div id="pmIndError" style="display:none;text-align:center;padding:50px;color:rgba(255,255,255,0.6);">' +
        '<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No data available</div>' +
        '</div>' +
        '</div></div></div>';

    $('#rdpmsGraphOverlay').remove();
    $('body').append(modalHtml);

    // ── Default the date picker to today (24h window of that day) ──
    function _pmDateStr(d) {
        return d.getFullYear() + '-' +
            String(d.getMonth() + 1).padStart(2, '0') + '-' +
            String(d.getDate()).padStart(2, '0');
    }
    var _pmToday = new Date();
    $('#pmIndDate').val(_pmDateStr(_pmToday)).attr('max', _pmDateStr(_pmToday));

    // Load the full 24-hour span (00:00:00 → 23:59:59) of the picked date.
    function _pmLoadForDate() {
        var ds = $('#pmIndDate').val();
        if (!ds) { showWarning('Please select a date.', 'Validation'); return; }
        var p = ds.split('-');
        var y = parseInt(p[0], 10), mo = parseInt(p[1], 10) - 1, da = parseInt(p[2], 10);
        var dayStart = new Date(y, mo, da, 0, 0, 0, 0);
        var dayEnd = new Date(y, mo, da, 23, 59, 59, 999);
        loadPmIndicationByRange(assetId, dayStart, dayEnd, filterIds, end);
    }

    $('#pmIndLoadBtn').off('click').on('click', _pmLoadForDate);

    // Initial load = today's full day
    _pmLoadForDate();
}

function loadPmIndicationFromApi(assetId, hours, filterIds, end) {
    var $ld = $('#pmIndLoading'), $ch = $('#pmIndChartDiv'), $er = $('#pmIndError'), $tr = $('#pmIndTimeRange');
    $ld.show(); $ch.hide(); $er.hide();

    var endDate = new Date();
    var startDate = new Date(endDate.getTime() - (hours * 60 * 60 * 1000));

    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);

    $tr.text(formatTimeForDisplay(startDate) + ' - ' + formatTimeForDisplay(endDate));

    var apiUrl = HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + startStr + '&endDate=' + endStr;

    $.ajax({
        url: apiUrl,
        type: 'GET',
        dataType: 'json',
        timeout: 30000,
        success: function (response) {
            $ld.hide();
            if (response && response.Data && response.Data.length > 0) {
                $ch.show();
                renderPmIndicationFromApi(response.Data, filterIds, end);
            } else {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>' +
                    'No indication data available for the last ' + hours + ' hour(s)').show();
            }
        },
        error: function (xhr, status, error) {
            $ld.hide();
            $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;color:#ef4444;"></i>' +
                'Failed to load data: ' + (error || 'Connection error')).show();
        }
    });
}

// ── Custom date-range loader (ported from OLD working version) ──
// Same backend as loadPmIndicationFromApi but with caller-supplied start/end dates.
function loadPmIndicationByRange(assetId, startDate, endDate, filterIds, end) {
    var $ld = $('#pmIndLoading'), $ch = $('#pmIndChartDiv'), $er = $('#pmIndError'), $tr = $('#pmIndTimeRange');
    $ld.show(); $ch.hide(); $er.hide();

    function _fmtDT(d) {
        return String(d.getDate()).padStart(2, '0') + '/' +
            String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' +
            String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    }
    $tr.text(_fmtDT(startDate) + ' \u2014 ' + _fmtDT(endDate));

    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);
    var apiUrl = HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + startStr + '&endDate=' + endStr;

    $.ajax({
        url: apiUrl,
        type: 'GET',
        dataType: 'json',
        timeout: 30000,
        success: function (response) {
            $ld.hide();
            if (response && response.Data && response.Data.length > 0) {
                $ch.show();
                renderPmIndicationFromApi(response.Data, filterIds, end);
            } else {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>' +
                    'No indication data available for the selected range.').show();
            }
        },
        error: function (xhr, status, error) {
            $ld.hide();
            $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;color:#ef4444;"></i>' +
                'Failed to load data: ' + (error || 'Connection error')).show();
        }
    });
}

function renderPmIndicationFromApi(data, filterIds, end) {
    var el = document.getElementById('pmIndChartDiv');
    if (!el) return;

    // Define order based on end: NWKR, NWKR(Loc), RWKR, RWKR(Loc)
    var displayOrder = (end === 'A') ? [25, 576, 26, 577] : [27, 578, 28, 579];

    // Colors
    var colorMap = {
        25: '#2563eb',   // NWKR - Blue
        576: '#7c3aed',  // NWKR Loc - Purple
        26: '#059669',   // RWKR - Green
        577: '#0891b2',  // RWKR Loc - Teal
        27: '#2563eb',   // NWKR - Blue
        578: '#7c3aed',  // NWKR Loc - Purple
        28: '#059669',   // RWKR - Green
        579: '#0891b2'   // RWKR Loc - Teal
    };

    // Step 1: Collect ALL unique timestamps from all attributes
    var allTimestamps = {};
    var attrDataMap = {};

    displayOrder.forEach(function (attrId) {
        for (var i = 0; i < data.length; i++) {
            if (data[i].AttributeId === attrId) {
                attrDataMap[attrId] = data[i];

                if (data[i].Values) {
                    for (var key in data[i].Values) {
                        var entry = data[i].Values[key];
                        if (!entry || !entry.Timestamp || entry.Value === undefined) continue;

                        var ts = entry.Timestamp.TimestampDevice;
                        if (!ts || ts.indexOf('0001') >= 0) continue;

                        var timestamp = new Date(ts).getTime();
                        if (isNaN(timestamp) || timestamp <= 0) continue;

                        allTimestamps[timestamp] = true;
                    }
                }
                break;
            }
        }
    });

    // Convert to sorted array
    var sortedTimestamps = Object.keys(allTimestamps).map(function (t) { return parseInt(t); }).sort(function (a, b) { return a - b; });

    if (sortedTimestamps.length === 0) {
        $('#pmIndChartDiv').hide();
        $('#pmIndError').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No indication data found for ' + (end === 'A' ? 'A End' : 'B End')).show();
        return;
    }

    // Step 2: For each attribute, build data array with values at all timestamps
    var series = [];
    var legends = [];

    displayOrder.forEach(function (attrId) {
        var attrData = attrDataMap[attrId];
        if (!attrData) return;

        var displayName = PM_INDICATION_NAMES[attrId] || ('Attr ' + attrId);
        var shortName = displayName.replace('A End - ', '').replace('B End - ', '');

        if (legends.indexOf(shortName) > -1) return;

        var color = colorMap[attrId] || '#64748b';

        // Build a map of timestamp -> value for this attribute
        var valueMap = {};
        var originalTimestamps = {};
        if (attrData.Values) {
            for (var key in attrData.Values) {
                var entry = attrData.Values[key];
                if (!entry || !entry.Timestamp || entry.Value === undefined) continue;

                var ts = entry.Timestamp.TimestampDevice;
                if (!ts || ts.indexOf('0001') >= 0) continue;

                var timestamp = new Date(ts).getTime();
                if (isNaN(timestamp) || timestamp <= 0) continue;

                var value = parseFloat(entry.Value);
                if (!isNaN(value)) {
                    valueMap[timestamp] = value;
                    originalTimestamps[timestamp] = true;
                }
            }
        }

        // Create data points for ALL timestamps, using last known value
        var points = [];
        var lastValue = null;

        sortedTimestamps.forEach(function (ts) {
            if (valueMap[ts] !== undefined) {
                lastValue = valueMap[ts];
            }
            if (lastValue !== null) {
                points.push({
                    value: [ts, lastValue],
                    symbol: originalTimestamps[ts] ? 'circle' : 'none',
                    symbolSize: originalTimestamps[ts] ? 8 : 0
                });
            }
        });

        if (points.length === 0) return;

        legends.push(shortName);

        var isLoc = shortName.indexOf('Loc') > -1;

        series.push({
            name: shortName,
            type: 'line',
            smooth: false,
            step: 'end',
            showSymbol: true,
            lineStyle: {
                width: isLoc ? 2 : 2.5,
                color: color,
                type: isLoc ? [5, 3] : 'solid'
            },
            itemStyle: {
                color: color,
                borderWidth: 2,
                borderColor: '#fff'
            },
            emphasis: {
                scale: 1.5,
                lineStyle: { width: isLoc ? 3 : 4 }
            },
            data: points
        });
    });

    if (series.length === 0) {
        $('#pmIndChartDiv').hide();
        $('#pmIndError').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No indication data found').show();
        return;
    }

    var existingChart = echarts.getInstanceByDom(el);
    if (existingChart) existingChart.dispose();

    var chart = echarts.init(el);

    chart.setOption({
        backgroundColor: 'transparent',
        tooltip: {
            trigger: 'axis',
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(34,211,238,0.30)',
            borderWidth: 1,
            padding: [12, 16],
            textStyle: { color: 'rgba(255,255,255,0.86)', fontSize: 12 },
            formatter: function (params) {
                if (!params || !params.length) return '';
                var t = new Date(params[0].value[0]);
                var timeStr = String(t.getHours()).padStart(2, '0') + ':' +
                    String(t.getMinutes()).padStart(2, '0') + ':' +
                    String(t.getSeconds()).padStart(2, '0');
                var dateStr = String(t.getDate()).padStart(2, '0') + '/' +
                    String(t.getMonth() + 1).padStart(2, '0') + '/' + t.getFullYear();

                var html = '<div style="font-weight:700;margin-bottom:10px;color:#22d3ee;padding-bottom:8px;border-bottom:1px solid rgba(255,255,255,0.10);">' + dateStr + ' ' + timeStr + '</div>';

                // Show all 4 attributes
                params.forEach(function (p) {
                    html += '<div style="display:flex;align-items:center;justify-content:space-between;padding:4px 0;">' +
                        '<div style="display:flex;align-items:center;">' +
                        '<span style="display:inline-block;width:12px;height:3px;border-radius:1px;background:' + p.color + ';margin-right:10px;"></span>' +
                        '<span style="color:rgba(255,255,255,0.72);">' + p.seriesName + '</span></div>' +
                        '<span style="font-weight:700;color:rgba(255,255,255,0.94);margin-left:15px;">' + p.value[1].toFixed(2) + ' V</span></div>';
                });
                return html;
            }
        },
        legend: {
            data: legends,
            top: 8,
            left: 'center',
            textStyle: { fontSize: 12, color: 'rgba(255,255,255,0.72)' },
            itemGap: 20,
            itemWidth: 20,
            itemHeight: 10
        },
        grid: { top: 50, left: 50, right: 15, bottom: 70 },
        toolbox: {
            right: 10,
            top: 5,
            feature: {
                dataZoom: { yAxisIndex: 'none' },
                restore: {},
                saveAsImage: { pixelRatio: 2 }
            },
            iconStyle: { borderColor: 'rgba(255,255,255,0.55)' }, emphasis: { iconStyle: { borderColor: '#22d3ee' } }
        },
        xAxis: {
            type: 'time',
            boundaryGap: false,
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) {
                    var d = new Date(v);
                    return String(d.getDate()).padStart(2, '0') + ' ' + ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getMonth()] + '\n' +
                        String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
                },
                lineHeight: 14
            },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        },
        yAxis: {
            type: 'value',
            name: 'Voltage (V)',
            nameTextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.60)' },
            axisLabel: { fontSize: 10, color: 'rgba(255,255,255,0.55)', formatter: function (v) { return v.toFixed(2); } },
            axisLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        },
        dataZoom: [
            { type: 'slider', height: 22, bottom: 8, borderColor: 'rgba(255,255,255,0.14)', backgroundColor: 'rgba(15,23,42,0.70)', fillerColor: 'rgba(34,211,238,0.20)', handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, textStyle: { fontSize: 10, color: 'rgba(255,255,255,0.50)' } },
            { type: 'inside' }
        ],
        series: series
    });

    $(window).off('resize.pmIndChart').on('resize.pmIndChart', function () { chart.resize(); });

    console.log('[PM Indication] Rendered', series.length, 'series with', sortedTimestamps.length, 'timestamps');
}

function loadPmDirGraph(assetId, hours, filterIds) {
    var $ld = $('#pmGraphLoading'), $ch = $('#pmGraphChartDiv'), $er = $('#pmGraphError'), $tr = $('#pmGraphTimeRange');
    $ld.show(); $ch.hide(); $er.hide();
    var e = new Date(), s = new Date(e - hours * 36e5);
    $tr.text(String(s.getHours()).padStart(2, '0') + ':' + String(s.getMinutes()).padStart(2, '0') + ' -- ' + String(e.getHours()).padStart(2, '0') + ':' + String(e.getMinutes()).padStart(2, '0'));

    $.ajax({
        url: HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + formatDateForHistoryApi(s) + '&endDate=' + formatDateForHistoryApi(e),
        type: 'GET', dataType: 'json', timeout: 30000,
        success: function (r) {
            $ld.hide();
            if (r && r.Data && r.Data.length > 0) {
                $ch.show();
                renderPmDirChart(r.Data, assetId, filterIds);
            } else { $er.html('<i class="fas fa-info-circle"></i> No data for this time range').show(); }
        },
        error: function () { $ld.hide(); $er.html('<i class="fas fa-exclamation-circle"></i> Failed to load').show(); }
    });
}

function renderPmDirChart(data, assetId, filterIds) {
    var fb = { 25: 'A End - NWKR (V)', 26: 'A End - RWKR (V)', 27: 'B End - NWKR (V)', 28: 'B End - RWKR (V)', 576: 'A End - NWKR 2 (V)', 577: 'A End - RWKR 2 (V)', 578: 'B End - NWKR 2 (V)', 579: 'B End - RWKR 2 (V)' };
    var colors = ['#259dab', '#dc3545', '#0d6efd', '#e4b704', '#6f42c1', '#198754', '#fd7e14', '#0dcaf0'];
    var sr = [], lg = [], ci = 0;

    data.forEach(function (at) {
        var id = at.AttributeId;
        if (!filterIds[id]) return;
        if (at.Values && at.Values['1'] && at.Values['1'].DataType === 'DataLogger') return;

        var dn = fb[id] || (typeof getAttrDisplayNamePlain === 'function' ? getAttrDisplayNamePlain('Attr ' + id, id) : 'Attr ' + id);
        var co = colors[ci % colors.length]; ci++;
        var pts = [];
        for (var key in at.Values) {
            var e = at.Values[key]; if (!e || !e.Timestamp) continue;
            var ts = e.Timestamp.TimestampDevice; if (!ts || ts.indexOf('0001') >= 0) continue;
            var ms = new Date(ts).getTime(); if (isNaN(ms) || ms <= 0) continue;
            var val = parseFloat(e.Value); if (isNaN(val)) continue;
            pts.push([ms, val]);
        }
        if (!pts.length) return;
        pts.sort(function (a, b) { return a[0] - b[0]; });
        lg.push(dn);
        sr.push({ name: dn, type: 'line', smooth: true, symbol: 'none', lineStyle: { width: 2.5 }, itemStyle: { color: co }, emphasis: { lineStyle: { width: 3.5 } }, data: pts });
    });

    if (!sr.length) { $('#pmGraphChartDiv').hide(); $('#pmGraphError').html('<i class="fas fa-info-circle fa-2x mb-3" style="display:block;"></i>No data for selected attributes').show(); return; }

    var el = document.getElementById('pmGraphChartDiv');
    var chart = echarts.init(el);

    chart.setOption({
        backgroundColor: 'transparent',
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross', label: { backgroundColor: 'rgba(34,211,238,0.80)' } },
            confine: true,
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(34,211,238,0.30)',
            borderWidth: 1,
            textStyle: { color: 'rgba(255,255,255,0.86)', fontSize: 13 },
            formatter: function (params) {
                if (!params || !params.length) return '';
                var d = new Date(params[0].value[0]);
                var timeStr = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
                var html = '<div style="padding:4px 8px;"><b style="color:#22d3ee;">' + timeStr + '</b><br/>';
                params.forEach(function (p) {
                    html += '<span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' + p.color + ';margin-right:6px;"></span>' + p.seriesName + ': <b>' + p.value[1].toFixed(2) + ' V</b><br/>';
                });
                html += '</div>';
                return html;
            }
        },
        legend: {
            data: lg,
            bottom: 45,
            textStyle: { fontSize: 13, fontWeight: '500', color: 'rgba(255,255,255,0.72)' },
            itemWidth: 20,
            itemHeight: 12,
            itemGap: 20
        },
        grid: { left: 70, right: 30, top: 30, bottom: 100 },
        toolbox: {
            right: 20,
            top: 5,
            feature: {
                dataZoom: { yAxisIndex: 'none', title: { zoom: 'Zoom', back: 'Reset' } },
                restore: { title: 'Reset' },
                saveAsImage: { title: 'Save', pixelRatio: 2 }
            },
            iconStyle: { borderColor: 'rgba(255,255,255,0.55)' }, emphasis: { iconStyle: { borderColor: '#22d3ee' } }
        },
        xAxis: [{
            type: 'time',
            boundaryGap: false,
            axisLabel: {
                fontSize: 12,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) { var d = new Date(v); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); }
            },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        }],
        yAxis: [{
            type: 'value',
            name: 'Voltage (V)',
            nameTextStyle: { fontSize: 13, fontWeight: 'bold', color: '#22d3ee' },
            axisLabel: { fontSize: 12, color: 'rgba(255,255,255,0.55)' },
            axisLine: { show: true, lineStyle: { color: 'rgba(34,211,238,0.45)', width: 2 } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        }],
        dataZoom: [
            { type: 'slider', height: 30, start: 0, end: 100, bottom: 10, borderColor: 'rgba(255,255,255,0.14)', backgroundColor: 'rgba(15,23,42,0.70)', fillerColor: 'rgba(34,211,238,0.20)', handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.50)' } },
            { type: 'inside' }
        ],
        color: colors,
        series: sr
    });

    $(window).off('resize.pmg').on('resize.pmg', function () { chart.resize(); });
}

// Format a Date as the value a <input type="datetime-local" step="1"> expects (local time).
function _toLocalDateTimeInput(d) {
    d = new Date(d);
    var pad = function (n) { return String(n).padStart(2, '0'); };
    return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) +
        'T' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
}

// hours        -> rolling window size (also used for messages/labels)
// startOverride/endOverride (optional) -> explicit From/To range from the filter
function loadHistoryGraphData(assetId, hours, startOverride, endOverride) {
    var $loading = $('#rdpmsGraphLoading');
    var $chartDiv = $('#rdpmsGraphChartDiv');
    var $error = $('#rdpmsGraphError');
    var $timeRange = $('#graphTimeRange');

    hours = parseInt(hours, 10);
    if (isNaN(hours) || hours <= 0) hours = 24;

    var requestId = ++rdpmsGraphRequestSeq;

    if (rdpmsGraphXhr && rdpmsGraphXhr.readyState !== 4) {
        try { rdpmsGraphXhr.abort(); } catch (e) { }
    }

    $loading.show();
    $chartDiv.hide();
    $error.hide();

    if (rdpmsGraphChart) {
        try { rdpmsGraphChart.clear(); } catch (e) { }
    }

    var endDate = endOverride ? new Date(endOverride) : new Date();
    var startDate = startOverride ? new Date(startOverride)
        : new Date(endDate.getTime() - (hours * 60 * 60 * 1000));

    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);

    // Keep the From / To inputs in sync with whatever range is loaded.
    try {
        $('#graphFromDate').val(_toLocalDateTimeInput(startDate));
        $('#graphToDate').val(_toLocalDateTimeInput(endDate));
    } catch (e) { }

    // Show date + time, not only HH:mm.
    $timeRange.text(formatDateTimeForGraphDisplay(startDate) + ' - ' + formatDateTimeForGraphDisplay(endDate));

    $('#rdpmsGraphOverlay').data({
        graphStartDate: startDate,
        graphEndDate: endDate,
        graphHours: hours
    });

    var apiUrl = HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + startStr + '&endDate=' + endStr;

    console.log('[Graph Modal] Fetching history:', {
        assetId: assetId,
        hours: hours,
        startDate: startStr,
        endDate: endStr,
        url: apiUrl
    });

    rdpmsGraphXhr = $.ajax({
        url: apiUrl,
        type: 'GET',
        dataType: 'json',
        timeout: 60000,
        success: function (response) {
            if (requestId !== rdpmsGraphRequestSeq) return;

            $loading.hide();

            if (response && response.Data && response.Data.length > 0) {
                $chartDiv.show();

                renderHistoryChart(response.Data, assetId, hours, startDate, endDate);

                setTimeout(function () {
                    if (rdpmsGraphChart) rdpmsGraphChart.resize();
                }, 100);
            } else {
                $chartDiv.hide();
                $error.html(
                    '<i class="fas fa-info-circle"></i> No historical data available for selected ' +
                    hours + ' hour range.<br/><small class="text-muted">Range: ' +
                    formatDateTimeForGraphDisplay(startDate) + ' - ' +
                    formatDateTimeForGraphDisplay(endDate) + '</small>'
                ).show();
            }
        },
        error: function (xhr, status, error) {
            if (status === 'abort') return;
            if (requestId !== rdpmsGraphRequestSeq) return;

            console.error('[Graph Modal] API Error:', status, error);

            $loading.hide();
            $chartDiv.hide();
            $error.html(
                '<i class="fas fa-exclamation-circle"></i> Failed to load graph data.' +
                '<br/><small class="text-muted">Error: ' + (error || status) + '</small>'
            ).show();
        }
    });
}

//function loadHistoryGraphData(assetId, hours) {
//    var $loading = $('#rdpmsGraphLoading');
//    var $chartDiv = $('#rdpmsGraphChartDiv');
//    var $error = $('#rdpmsGraphError');
//    var $timeRange = $('#graphTimeRange');

//    $loading.show();
//    $chartDiv.hide();
//    $error.hide();

//    // Calculate time range
//    var endDate = new Date();
//    var startDate = new Date(endDate.getTime() - (hours * 60 * 60 * 1000));

//    var startStr = formatDateForHistoryApi(startDate);
//    var endStr = formatDateForHistoryApi(endDate);

//    // Update time range display
//    $timeRange.text(formatTimeForDisplay(startDate) + ' - ' + formatTimeForDisplay(endDate));

//    var apiUrl = HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + startStr + '&endDate=' + endStr;

//    console.log('[Graph Modal] Fetching history from:', apiUrl);

//    $.ajax({
//        url: apiUrl,
//        type: 'GET',
//        dataType: 'json',
//        timeout: 30000,
//        success: function (response) {
//            $loading.hide();

//            if (response && response.Data && response.Data.length > 0) {
//                $chartDiv.show();
//                renderHistoryChart(response.Data, assetId, hours);
//            } else {
//                $error.html('<i class="fas fa-info-circle"></i> No historical data available for the selected time range.<br/><small class="text-muted">Try selecting a different time range.</small>').show();
//            }
//        },
//        error: function (xhr, status, error) {
//            console.error('[Graph Modal] API Error:', status, error);
//            $loading.hide();
//            $error.html('<i class="fas fa-exclamation-circle"></i> Failed to load graph data.<br/><small class="text-muted">Error: ' + (error || status) + '</small>').show();
//        }
//    });
//}

// Get attribute name from wsLiveData or default
function getAttributeName(assetId, attributeId) {
    // First try wsLiveData
    var asset = wsLiveData[assetId];
    if (asset && asset.attrs) {
        for (var attrKey in asset.attrs) {
            var attr = asset.attrs[attrKey];
            if (attr.AttrId == attributeId || attr.AssetAttributeId == attributeId) {
                return attr.AttributeName || attr.AssetAttributeName || attrKey;
            }
        }
    }

    // Fallback mapping based on API documentation
    var fallbackMap = {
        // Track Circuit
        1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)',
        5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)', 7: 'Last Update Feed End', 8: 'Last Update Relay End',
        // Signal - CORRECT MAPPING
        9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA',
        13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA',
        // Point Machine
        19: 'Vibration A end - X', 20: 'Vibration A end - Y', 21: 'Vibration A end - Z',
        22: 'Vibration B end - X', 23: 'Vibration B end - Y', 24: 'Vibration B end - Z',
        25: 'A End - NWKR', 26: 'A End - RWKR', 27: 'B End - NWKR', 28: 'B End - RWKR',
        29: 'Last Updated', 30: 'Zero offset',
        // Track Circuit continued
        31: 'Rx1 mV', 32: 'Rx2 mV', 33: 'Tx1 V', 34: 'Tx2 V', 35: 'Supply V', 36: 'Modem mV',
        // Signal additional
        154: 'Root V', 155: 'Root mA',
        227: 'UG V', 228: 'UG mA',
        // DataLogger relays (will be skipped in graph)
        231: 'RECR', 232: 'HECR', 233: 'DECR', 234: 'HHECR', 235: 'Relay5',
        // PR values
        327: 'HHPR', 328: 'DPR', 329: 'HPR',
        // PILOT
        337: 'PILOT mA', 499: 'PILOT V',
        // Point Machine additional
        576: 'A End - NWKR (Loc)', 577: 'A End - RWKR (Loc)',
        578: 'B End - NWKR (Loc)', 579: 'B End - RWKR (Loc)',
        // Co_Hg
        610: 'Co_Hg mA', 611: 'Co_Hg V',
        // Point Machine voltage/current
        212: 'B End - NW-V', 213: 'B End - NW-C', 214: 'B End - RW-V', 215: 'B End - RW-C',
        216: 'A End - NW-V', 217: 'A End - NW-C', 218: 'A End - RW-V', 219: 'A End - RW-C',
        // Additional relays
        685: 'NWCR (A)', 686: 'NWCR (B)', 687: 'RWCR (A)', 688: 'RWCR (B)',
        689: 'NWKR', 690: 'RWKR'
    };

    return fallbackMap[attributeId] || ('Attr ' + attributeId);
}
function renderHistoryChart(data, assetId, hours, startDate, endDate) {
    var $chartDiv = $('#rdpmsGraphChartDiv');
    $chartDiv.show();

    var el = document.getElementById('rdpmsGraphChartDiv');
    if (!el) {
        console.error('[Graph] Chart div not found');
        return;
    }

    hours = parseInt(hours, 10);
    if (isNaN(hours) || hours <= 0) hours = 24;

    var axisEndDate = endDate ? new Date(endDate) : new Date();
    var axisStartDate = startDate ? new Date(startDate) : new Date(axisEndDate.getTime() - (hours * 60 * 60 * 1000));

    var axisMin = axisStartDate.getTime();
    var axisMax = axisEndDate.getTime();
    var spanMs = Math.max(axisMax - axisMin, 1);

    var pendingSeries = [];
    var legends = [];
    var tooltipSeries = []; // full per-series points for carry-forward hover
    var derivedOperands = []; // raw operand series captured for derived-value calc

    var hasVoltageSeries = false;
    var hasOtherSeries = false;

    var colors = ['#22d3ee', '#f87171', '#60a5fa', '#34d399', '#fbbf24', '#a78bfa', '#fb923c', '#2dd4bf', '#f472b6', '#38bdf8'];
    var colorIdx = 0;

    var assetTypeId = $('#rdpmsGraphOverlay').data('assetTypeId') || $('#drpAssetType').val();
    var isPointMachine = (assetTypeId == 3 || assetTypeId === '3');

    window._gColorMap = window._gColorMap || {};

    function getTimestampDeviceMs(entry) {
        if (!entry) return null;

        var ts = null;

        if (entry.Timestamp && entry.Timestamp.TimestampDevice) {
            ts = entry.Timestamp.TimestampDevice;
        } else if (entry.TimestampDevice) {
            ts = entry.TimestampDevice;
        }

        if (!ts) return null;

        ts = String(ts);

        if (ts.indexOf('0001') >= 0) return null;

        var ms = new Date(ts).getTime();

        if (isNaN(ms) || ms <= 0) return null;

        return ms;
    }

    var liveAttrIdToName = {};
    var liveAsset = wsLiveData[assetId];

    if (liveAsset && liveAsset.attrs) {
        for (var attrKey in liveAsset.attrs) {
            var liveAttr = liveAsset.attrs[attrKey];
            if (!liveAttr) continue;

            var liveAttrId = parseInt(liveAttr.AttrId || liveAttr.AssetAttributeId || liveAttr.AttributeId || 0);
            if (!liveAttrId) continue;

            var displayName = (typeof getAttrDisplayNamePlain === 'function')
                ? getAttrDisplayNamePlain(attrKey, liveAttrId, assetId)
                : attrKey;

            liveAttrIdToName[liveAttrId] = displayName || attrKey;
        }
    }

    if (typeof dlAssetRoleMap !== 'undefined') {
        for (var dlKey in dlAssetRoleMap) {
            if (dlKey.indexOf(String(assetId) + '_') !== 0) continue;

            var dlAttrId = parseInt(dlKey.split('_')[1], 10);
            if (dlAttrId && !liveAttrIdToName[dlAttrId]) {
                liveAttrIdToName[dlAttrId] = dlAssetRoleMap[dlKey];
            }
        }
    }

    data.forEach(function (attrData) {
        if (!attrData || !attrData.Values) return;

        var attrId = parseInt(attrData.AttributeId, 10);
        if (!attrId) return;

        if (isPointMachine && typeof isPmOperationAttrId === 'function' && isPmOperationAttrId(attrId)) return;

        var valueKeys = Object.keys(attrData.Values);
        if (!valueKeys.length) return;

        var firstEntry = attrData.Values['1'] || attrData.Values[valueKeys[0]] || {};
        var attrDataType = firstEntry.DataType || attrData.DataType || '';

        if (typeof shouldIncludeInGraph === 'function' && !shouldIncludeInGraph(attrId, attrDataType)) return;

        var isDataLoggerType = String(attrDataType || '').toLowerCase() === 'datalogger';

        var displayName = '';

        if (isDataLoggerType) {
            if (typeof resolveDataloggerDisplayName === 'function') {
                displayName = resolveDataloggerDisplayName(assetId, attrId, {
                    DataType: attrDataType,
                    AttributeId: attrId,
                    AssetAttributeId: attrId,
                    DataloggerAttributeId: attrId,
                    Value: firstEntry.Value,
                    DataloggerAttribute: firstEntry.DataloggerAttribute
                });
            }

            displayName = displayName || liveAttrIdToName[attrId] || ('Attr ' + attrId);
        } else {
            displayName = liveAttrIdToName[attrId];

            if (!displayName && typeof resolveUserAssetName === 'function') {
                var resolved = resolveUserAssetName(assetId, attrId, attrDataType);
                if (resolved && resolved.name) displayName = resolved.name;
            }

            if (!displayName && typeof getAttributeName === 'function') {
                var rawName = getAttributeName(assetId, attrId);
                displayName = (typeof getAttrDisplayNamePlain === 'function')
                    ? getAttrDisplayNamePlain(rawName, attrId, assetId)
                    : rawName;
            }

            displayName = displayName || ('Attr ' + attrId);
        }

        if (typeof _gChecked !== 'undefined') {
            if (_gChecked[displayName] === false) return;
        }

        var points = [];

        for (var key in attrData.Values) {
            var entry = attrData.Values[key];
            if (!entry) continue;

            var rawVal = entry.Value;
            if (rawVal === undefined || rawVal === null || rawVal === '') continue;

            var val = parseFloat(rawVal);
            if (isNaN(val)) continue;

            // STRICT: graph X-axis must use TimestampDevice only.
            var time = getTimestampDeviceMs(entry);
            if (time === null || time === undefined) continue;

            // Keep only selected hour range.
            if (time < axisMin || time > axisMax) continue;

            points.push([time, val]);
        }

        if (!points.length) return;

        points.sort(function (a, b) { return a[0] - b[0]; });

        // Remove duplicate TimestampDevice points.
        // Duplicate timestamp with different value causes vertical spikes.
        var collapsed = [];
        for (var pi = 0; pi < points.length; pi++) {
            var p = points[pi];

            if (collapsed.length && collapsed[collapsed.length - 1][0] === p[0]) {
                collapsed[collapsed.length - 1] = p;
            } else {
                collapsed.push(p);
            }
        }

        points = collapsed;

        // Capture the raw device samples (operands) so derived-value series can be
        // reconstructed across time with the same calculateDerivedValues() formula.
        derivedOperands.push({ name: displayName, attrId: attrId, points: points.slice() });

        var actualCount = points.length;

        // Carry-forward: change-of-value telemetry only reports on change, so the
        // last known value is still valid until the next change. Hold it flat to
        // the right edge (capped at "now", never into the future).
        var hasSyntheticEnd = false;
        if (points.length) {
            var lastReal = points[points.length - 1];
            var endAnchorMs = Math.min(axisMax, Date.now());
            if (lastReal[0] < endAnchorMs) {
                points.push([endAnchorMs, lastReal[1]]);
                hasSyntheticEnd = true;
            }
        }

        var chartPoints = points.map(function (p, idx) {
            var isSynthetic = hasSyntheticEnd && idx === points.length - 1;
            return {
                value: p,
                symbol: (!isSynthetic && actualCount < 80) ? 'circle' : 'none',
                symbolSize: (!isSynthetic && actualCount < 80) ? 4 : 0
            };
        });

        var isVoltage = _graphIsVoltageSeries(displayName);
        if (isVoltage) hasVoltageSeries = true;
        else hasOtherSeries = true;

        var color = window._gColorMap[displayName] || colors[colorIdx % colors.length];
        window._gColorMap[displayName] = color;
        colorIdx++;

        legends.push(displayName);

        pendingSeries.push({
            name: displayName,
            type: 'line',
            smooth: false,
            step: 'end',          // hold value until the next change (carry-forward)
            connectNulls: false,
            showSymbol: actualCount < 80,
            symbol: 'circle',
            symbolSize: 4,
            lineStyle: { width: 2.2, color: color },
            itemStyle: { color: color },
            emphasis: { lineStyle: { width: 3.5 } },
            data: chartPoints,
            __isVoltage: isVoltage
        });

        // Store the full point list (incl. carry-forward end) for the hover tooltip
        // so every attribute can be shown at any hovered time.
        tooltipSeries.push({
            name: displayName,
            color: color,
            isVoltage: isVoltage,
            points: points
        });
    });

    // ===== DERIVED VALUE SERIES =====
    // Reconstruct the live derived metrics (ITC BATT CHARG, VTC VAR RES, ...) over
    // time: at every sample instant, carry each raw operand forward and run the same
    // calculateDerivedValues() the live tiles use. Metrics whose operands are not
    // present resolve to null and are simply skipped (no empty series).
    if (typeof calculateDerivedValues === 'function' && derivedOperands.length) {
        var _derivedDefs = [
            { key: 'itcBattCharg', name: 'ITC BATT CHARG (mA)' },
            { key: 'vtcVarRes', name: 'VTC VAR RES (V)' },
            { key: 'rtcChFeedEnd', name: 'RTC CH FEED END (\u03A9)' },
            { key: 'rtcVarRes', name: 'RTC VAR RES (\u03A9)' },
            { key: 'vtcTr', name: 'VTC TR (V)' },
            { key: 'ibalst', name: 'IBALST (mA)' },
            { key: 'rrail', name: 'RRAIL (\u03A9)' }
        ];

        var _timeSet = {};
        derivedOperands.forEach(function (op) {
            op.points.forEach(function (p) { _timeSet[p[0]] = true; });
        });
        var _times = Object.keys(_timeSet).map(Number).sort(function (a, b) { return a - b; });

        var _derivedPoints = {};
        _derivedDefs.forEach(function (d) { _derivedPoints[d.key] = []; });

        _times.forEach(function (tm) {
            var attrsAtTime = {};
            derivedOperands.forEach(function (op) {
                var v = null, pts = op.points;
                for (var i = 0; i < pts.length; i++) {
                    if (pts[i][0] <= tm) v = pts[i][1]; else break;
                }
                if (v !== null) {
                    attrsAtTime[op.name] = { Value: v, AttrId: op.attrId, AssetAttributeId: op.attrId };
                }
            });

            var dv;
            try { dv = calculateDerivedValues(attrsAtTime); } catch (e) { dv = null; }
            if (!dv) return;

            _derivedDefs.forEach(function (d) {
                var val = dv[d.key];
                if (val !== null && val !== undefined && !isNaN(val)) {
                    _derivedPoints[d.key].push([tm, val]);
                }
            });
        });

        var _endAnchor = Math.min(axisMax, Date.now());

        _derivedDefs.forEach(function (d) {
            var pts = _derivedPoints[d.key];
            if (!pts.length) return; // operands unavailable -> skip metric

            var dpts = pts.slice();
            var lastD = dpts[dpts.length - 1];
            if (lastD[0] < _endAnchor) dpts.push([_endAnchor, lastD[1]]);

            var dIsVoltage = _graphIsVoltageSeries(d.name);
            if (dIsVoltage) hasVoltageSeries = true; else hasOtherSeries = true;

            var dColor = window._gColorMap[d.name] || colors[colorIdx % colors.length];
            window._gColorMap[d.name] = dColor;
            colorIdx++;

            var dCount = pts.length;
            var dChart = dpts.map(function (p, idx) {
                var isEnd = (idx === dpts.length - 1) && (dpts.length > pts.length);
                return {
                    value: p,
                    symbol: (!isEnd && dCount < 80) ? 'circle' : 'none',
                    symbolSize: (!isEnd && dCount < 80) ? 4 : 0
                };
            });

            legends.push(d.name);

            pendingSeries.push({
                name: d.name,
                type: 'line',
                smooth: false,
                step: 'end',
                connectNulls: false,
                showSymbol: dCount < 80,
                symbol: 'circle',
                symbolSize: 4,
                lineStyle: { width: 2, color: dColor, type: 'dashed' }, // dashed = derived
                itemStyle: { color: dColor },
                emphasis: { lineStyle: { width: 3 } },
                data: dChart,
                __isVoltage: dIsVoltage
            });

            tooltipSeries.push({
                name: d.name,
                color: dColor,
                isVoltage: dIsVoltage,
                points: dpts
            });
        });
    }

    if (!pendingSeries.length) {
        $chartDiv.hide();
        $('#rdpmsGraphError')
            .html('<i class="fas fa-info-circle" style="font-size:24px;display:block;margin-bottom:10px;"></i>No TimestampDevice data available for selected ' + hours + ' hour range')
            .show();
        return;
    }

    var useDualAxis = hasVoltageSeries && hasOtherSeries;
    var finalSeries = [];

    pendingSeries.forEach(function (s) {
        if (useDualAxis && s.__isVoltage) s.yAxisIndex = 1;
        else s.yAxisIndex = 0;

        delete s.__isVoltage;
        finalSeries.push(s);
    });

    if (rdpmsGraphChart) {
        rdpmsGraphChart.dispose();
        rdpmsGraphChart = null;
    }

    $chartDiv.css({ display: 'block', visibility: 'visible' });
    el.offsetHeight;

    rdpmsGraphChart = echarts.init(el);

    // Make the per-series points available to the carry-forward tooltip formatter.
    window._rdpmsTooltipSeries = tooltipSeries;

    var leftAxisName = useDualAxis ? 'mA / Value' : (hasVoltageSeries ? 'V' : 'mA / Value');

    var yAxisConfig = [
        {
            type: 'value',
            name: leftAxisName,
            nameTextStyle: { fontSize: 12, color: '#64748b' },
            axisLabel: { fontSize: 11, color: '#64748b' },
            axisLine: { show: true, lineStyle: { color: '#22d3ee', width: 2 } },
            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
        }
    ];

    if (useDualAxis) {
        yAxisConfig.push({
            type: 'value',
            name: 'V',
            position: 'right',
            nameTextStyle: { fontSize: 12, color: '#a78bfa' },
            axisLabel: { fontSize: 11, color: '#a78bfa' },
            axisLine: { show: true, lineStyle: { color: '#a78bfa', width: 2 } },
            splitLine: { show: false }
        });
    }

    rdpmsGraphChart.setOption({
        backgroundColor: 'transparent',
        animation: false,
        tooltip: {
            trigger: 'axis',
            confine: true,
            axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.45)', width: 1 } },
            backgroundColor: 'rgba(10,26,46,0.96)',
            borderColor: 'rgba(37,157,171,0.4)',
            borderWidth: 1,
            padding: [12, 16],
            textStyle: { color: '#e2e8f0', fontSize: 13 },
            formatter: function (params) {
                if (!params || !params.length) return '';

                // Exact time under the cursor (snap:false keeps it continuous).
                var hoverMs = (params[0].axisValue != null) ? +params[0].axisValue : +params[0].value[0];

                var t = new Date(hoverMs);
                var timeStr = String(t.getDate()).padStart(2, '0') + '/' +
                    String(t.getMonth() + 1).padStart(2, '0') + ' ' +
                    String(t.getHours()).padStart(2, '0') + ':' +
                    String(t.getMinutes()).padStart(2, '0') + ':' +
                    String(t.getSeconds()).padStart(2, '0');

                // Respect legend selection (hidden series stay hidden in the tooltip).
                var selected = {};
                try {
                    var opt = rdpmsGraphChart.getOption();
                    if (opt && opt.legend && opt.legend[0] && opt.legend[0].selected) selected = opt.legend[0].selected;
                } catch (e) { }

                var store = window._rdpmsTooltipSeries || [];
                var html = '<div style="font-weight:600;margin-bottom:8px;color:#22d3ee;">' + timeStr + '</div>';
                var shown = 0;

                // Show EVERY attribute, carrying the last value forward to the hovered time.
                store.forEach(function (s) {
                    if (selected[s.name] === false) return;

                    var pts = s.points || [];
                    var v = null;
                    for (var i = 0; i < pts.length; i++) {
                        if (pts[i][0] <= hoverMs) v = pts[i][1];
                        else break;
                    }
                    if (v === null) return; // no reading yet at this time

                    var suffix = s.isVoltage ? ' V' : '';
                    html += '<div style="display:flex;align-items:center;margin:4px 0;">' +
                        '<span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' +
                        s.color + ';margin-right:10px;"></span>' +
                        '<span style="flex:1;color:#cbd5e1;">' + s.name + '</span>' +
                        '<span style="font-weight:700;margin-left:15px;color:#fff;">' +
                        Number(v).toFixed(2) + suffix +
                        '</span></div>';
                    shown++;
                });

                if (!shown) return '';
                return html;
            }
        },
        legend: {
            data: legends,
            type: 'scroll',
            orient: 'horizontal',
            top: 34,             // own row, BELOW the toolbox icons -> no overlap ever
            left: 10,
            right: 10,
            textStyle: { fontSize: 12, color: '#94a3b8' },
            pageTextStyle: { color: '#94a3b8' },
            pageIconColor: '#259dab',
            pageIconInactiveColor: '#334155',
            itemGap: 16,
            itemWidth: 25,
            itemHeight: 12
        },
        grid: {
            top: 66,             // clear the toolbox row + the legend row
            left: 65,
            right: useDualAxis ? 65 : 30,
            bottom: 78
        },
        toolbox: {
            right: 12,
            top: 6,
            itemGap: 8,
            iconStyle: { borderColor: 'rgba(255,255,255,0.55)' },
            emphasis: { iconStyle: { borderColor: '#22d3ee' } },
            feature: {
                dataZoom: { title: { zoom: 'Zoom', back: 'Reset' } },
                restore: { title: 'Reset' },
                saveAsImage: { title: 'Save', pixelRatio: 2 }
            }
        },
        xAxis: {
            type: 'time',
            min: axisMin,
            max: axisMax,
            boundaryGap: false,
            axisLabel: {
                fontSize: 11,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) {
                    return _graphXAxisLabel(v, spanMs);
                }
            },
            axisLine: { lineStyle: { color: '#1e293b' } },
            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
        },
        yAxis: yAxisConfig,
        dataZoom: [
            {
                type: 'slider',
                height: 28,
                bottom: 8,
                start: 0,
                end: 100,
                filterMode: 'none',
                borderColor: '#1e293b',
                backgroundColor: 'rgba(15,23,42,0.6)',
                fillerColor: 'rgba(37,157,171,0.25)',
                handleStyle: { color: '#259dab', borderColor: '#259dab' },
                textStyle: { color: '#64748b' },
                dataBackground: {
                    lineStyle: { color: '#334155' },
                    areaStyle: { color: 'rgba(37,157,171,0.08)' }
                }
            },
            {
                type: 'inside',
                filterMode: 'none'
            }
        ],
        series: finalSeries
    }, true);

    $(window).off('resize.rdpmsGraph').on('resize.rdpmsGraph', function () {
        if (rdpmsGraphChart) rdpmsGraphChart.resize();
    });

    setTimeout(function () {
        if (rdpmsGraphChart) rdpmsGraphChart.resize();
    }, 150);

    console.log('[Graph] Rendered', finalSeries.length, 'series | hours:', hours, '| timestamp: TimestampDevice only | dualAxis:', useDualAxis);
}//function renderHistoryChart(data, assetId, hours) {
//    var $chartDiv = $('#rdpmsGraphChartDiv');
//    $chartDiv.show();

//    var el = document.getElementById('rdpmsGraphChartDiv');
//    if (!el) {
//        console.error('[Graph] Chart div not found');
//        return;
//    }

//    var series = [];
//    var legends = [];
//    var allValues = [];

//    var colors = ['#22d3ee', '#f87171', '#60a5fa', '#34d399', '#fbbf24', '#a78bfa', '#fb923c', '#2dd4bf', '#f472b6', '#38bdf8'];
//    var colorIdx = 0;

//    var assetTypeId = $('#rdpmsGraphOverlay').data('assetTypeId') || $('#drpAssetType').val();
//    var isPointMachine = (assetTypeId == 3 || assetTypeId === '3');

//    var liveAttrIdToName = {};
//    var liveAsset = wsLiveData[assetId];

//    if (liveAsset && liveAsset.attrs) {
//        for (var attrKey in liveAsset.attrs) {
//            var liveAttr = liveAsset.attrs[attrKey];
//            if (!liveAttr) continue;

//            var liveAttrId = parseInt(liveAttr.AttrId || liveAttr.AssetAttributeId || liveAttr.AttributeId || 0);
//            if (!liveAttrId) continue;

//            var displayName = (typeof getAttrDisplayNamePlain === 'function')
//                ? getAttrDisplayNamePlain(attrKey, liveAttrId, assetId)
//                : attrKey;

//            liveAttrIdToName[liveAttrId] = displayName || attrKey;
//        }
//    }

//    // Add DataLogger mapping from asset-scoped map.
//    // This fixes cases where graph receives AttributeId like 3428/3392 and was showing "Attr 3428".
//    if (typeof dlAssetRoleMap !== 'undefined') {
//        for (var dlKey in dlAssetRoleMap) {
//            if (dlKey.indexOf(String(assetId) + '_') !== 0) continue;

//            var dlId = parseInt(dlKey.split('_')[1]);
//            if (dlId && !liveAttrIdToName[dlId]) {
//                liveAttrIdToName[dlId] = dlAssetRoleMap[dlKey];
//            }
//        }
//    }

//    data.forEach(function (attrData) {
//        if (!attrData) return;

//        var attrId = attrData.AttributeId;
//        if (!attrData.Values) return;

//        var valueKeys = Object.keys(attrData.Values);
//        if (!valueKeys.length) return;

//        var firstEntry = attrData.Values['1'] || attrData.Values[valueKeys[0]] || {};
//        var attrDataType = firstEntry.DataType || attrData.DataType || '';

//        if (!shouldIncludeInGraph(attrId, attrDataType)) return;

//        var isDataLoggerType = String(attrDataType || '').toLowerCase() === 'datalogger';

//        var attrName = '';
//        var displayName = '';

//        if (isDataLoggerType) {
//            displayName = resolveDataloggerDisplayName(assetId, attrId, {
//                DataType: attrDataType,
//                AttributeId: attrId,
//                AssetAttributeId: attrId,
//                DataloggerAttributeId: attrId,
//                Value: firstEntry.Value,
//                DataloggerAttribute: firstEntry.DataloggerAttribute
//            });

//            if (!displayName) {
//                displayName = liveAttrIdToName[attrId] || ('Attr ' + attrId);
//            }

//            attrName = displayName;
//        } else {
//            attrName = liveAttrIdToName[attrId];

//            if (!attrName) {
//                var resolved = resolveUserAssetName(assetId, attrId, attrDataType);
//                if (resolved && resolved.name) {
//                    attrName = resolved.name;
//                }
//            }

//            if (!attrName) {
//                var rawName = getAttributeName(assetId, attrId);
//                attrName = (typeof getAttrDisplayNamePlain === 'function')
//                    ? getAttrDisplayNamePlain(rawName, attrId, assetId)
//                    : rawName;
//            }

//            displayName = attrName;
//        }

//        // Respect graph checkbox state if available.
//        if (typeof _gChecked !== 'undefined') {
//            if (_gChecked[attrName] === false || _gChecked[displayName] === false) return;
//        }

//        // For Point Machine, avoid operation/event IDs. Keep only numeric indication/history data.
//        if (isPointMachine && isPmOperationAttrId(attrId)) return;

//        var points = [];

//        for (var key in attrData.Values) {
//            var entry = attrData.Values[key];
//            if (!entry) continue;

//            var rawVal = entry.Value;
//            if (rawVal === undefined || rawVal === null || rawVal === '') continue;

//            var val = parseFloat(rawVal);
//            if (isNaN(val)) continue;

//            var ts = null;

//            if (entry.Timestamp) {
//                ts = entry.Timestamp.TimestampDevice ||
//                    entry.Timestamp.TimestampLocal ||
//                    entry.Timestamp.TimestampEdgeX ||
//                    entry.Timestamp.TimestampChannel ||
//                    entry.Timestamp.TimestampPeriodic ||
//                    entry.Timestamp.TimestampChange;
//            }

//            ts = ts || entry.TimestampDevice || entry.TimestampLocal || entry.timestamp || entry.TimestampLocal;

//            if (!ts || String(ts).indexOf('0001') >= 0) continue;

//            var time = new Date(ts).getTime();
//            if (isNaN(time) || time <= 0) continue;

//            points.push([time, val]);
//            allValues.push(val);
//        }

//        if (!points.length) return;

//        points.sort(function (a, b) { return a[0] - b[0]; });

//        var color = _gColorMap && _gColorMap[displayName]
//            ? _gColorMap[displayName]
//            : colors[colorIdx % colors.length];

//        if (typeof _gColorMap !== 'undefined') {
//            _gColorMap[displayName] = color;
//        }

//        colorIdx++;

//        legends.push(displayName);

//        series.push({
//            name: displayName,
//            type: 'line',
//            smooth: true,
//            symbol: 'circle',
//            symbolSize: 5,
//            showSymbol: points.length < 50,
//            connectNulls: true,
//            lineStyle: { width: 2.5, color: color },
//            itemStyle: { color: color },
//            emphasis: { lineStyle: { width: 4 } },
//            data: points
//        });
//    });

//    if (!series.length) {
//        $chartDiv.hide();
//        $('#rdpmsGraphError')
//            .html('<i class="fas fa-info-circle" style="font-size:24px;display:block;margin-bottom:10px;"></i>No data available for the selected time range')
//            .show();
//        return;
//    }

//    if (rdpmsGraphChart) {
//        rdpmsGraphChart.dispose();
//        rdpmsGraphChart = null;
//    }

//    $chartDiv.css({ display: 'block', visibility: 'visible' });
//    el.offsetHeight;

//    rdpmsGraphChart = echarts.init(el, 'dark');

//    var minVal = Math.min.apply(null, allValues);
//    var maxVal = Math.max.apply(null, allValues);
//    var range = maxVal - minVal;
//    var padding = range * 0.15 || 5;

//    rdpmsGraphChart.setOption({
//        backgroundColor: 'transparent',
//        tooltip: {
//            trigger: 'axis',
//            backgroundColor: 'rgba(10,26,46,0.96)',
//            borderColor: 'rgba(37,157,171,0.4)',
//            borderWidth: 1,
//            padding: [12, 16],
//            textStyle: { color: '#e2e8f0', fontSize: 13 },
//            formatter: function (params) {
//                if (!params || !params.length) return '';

//                var t = new Date(params[0].value[0]);
//                var timeStr = String(t.getHours()).padStart(2, '0') + ':' +
//                    String(t.getMinutes()).padStart(2, '0') + ':' +
//                    String(t.getSeconds()).padStart(2, '0');

//                var html = '<div style="font-weight:600;margin-bottom:8px;color:#22d3ee;">' + timeStr + '</div>';

//                params.forEach(function (p) {
//                    html += '<div style="display:flex;align-items:center;margin:4px 0;">' +
//                        '<span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' +
//                        p.color + ';margin-right:10px;"></span>' +
//                        '<span style="flex:1;color:#cbd5e1;">' + p.seriesName + '</span>' +
//                        '<span style="font-weight:700;margin-left:15px;color:#fff;">' +
//                        Number(p.value[1]).toFixed(2) +
//                        '</span></div>';
//                });

//                return html;
//            }
//        },
//        legend: {
//            data: legends,
//            type: 'scroll',
//            orient: 'horizontal',
//            top: 8,
//            left: 'center',
//            width: '86%',
//            textStyle: { fontSize: 12, color: '#cbd5e1' },
//            pageTextStyle: { color: '#94a3b8' },
//            pageIconColor: '#22d3ee',
//            pageIconInactiveColor: '#475569',
//            itemGap: 16,
//            itemWidth: 24,
//            itemHeight: 10
//        },
//        grid: {
//            top: legends.length <= 4 ? 54 : 82,
//            left: 65,
//            right: 30,
//            bottom: 78
//        },
//        toolbox: {
//            right: 14,
//            top: 8,
//            feature: {
//                dataZoom: { title: { zoom: 'Zoom', back: 'Reset' } },
//                restore: { title: 'Reset' },
//                saveAsImage: { title: 'Save', pixelRatio: 2 }
//            },
//            iconStyle: { borderColor: 'rgba(255,255,255,0.55)' }, emphasis: { iconStyle: { borderColor: '#22d3ee' } },
//            emphasis: { iconStyle: { borderColor: '#22d3ee' } }
//        },
//        xAxis: {
//            type: 'time',
//            boundaryGap: false,
//            axisLabel: {
//                fontSize: 11,
//                color: '#94a3b8',
//                formatter: function (v) {
//                    var d = new Date(v);
//                    return String(d.getHours()).padStart(2, '0') + ':' +
//                        String(d.getMinutes()).padStart(2, '0');
//                }
//            },
//            axisLine: { lineStyle: { color: '#1e293b' } },
//            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
//        },
//        yAxis: {
//            type: 'value',
//            name: 'Value',
//            nameTextStyle: { fontSize: 12, color: '#64748b' },
//            min: function (value) { return Math.floor(value.min - padding); },
//            max: function (value) { return Math.ceil(value.max + padding); },
//            axisLabel: { fontSize: 11, color: '#64748b' },
//            axisLine: { show: true, lineStyle: { color: '#259dab', width: 2 } },
//            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
//        },
//        dataZoom: [
//            {
//                type: 'slider',
//                height: 28,
//                bottom: 8,
//                start: 0,
//                end: 100,
//                borderColor: '#1e293b',
//                backgroundColor: 'rgba(15,23,42,0.6)',
//                fillerColor: 'rgba(37,157,171,0.25)',
//                handleStyle: { color: '#259dab', borderColor: '#259dab' },
//                textStyle: { color: '#64748b' },
//                dataBackground: {
//                    lineStyle: { color: '#334155' },
//                    areaStyle: { color: 'rgba(37,157,171,0.08)' }
//                }
//            },
//            { type: 'inside' }
//        ],
//        color: colors,
//        series: series
//    });

//    $(window).off('resize.rdpmsGraph').on('resize.rdpmsGraph', function () {
//        if (rdpmsGraphChart) rdpmsGraphChart.resize();
//    });

//    console.log('[Graph] Rendered', series.length, 'series for asset type', assetTypeId);
//}

//function renderHistoryChart(data, assetId, hours) {
//    var $chartDiv = $('#rdpmsGraphChartDiv');
//    $chartDiv.show();

//    var el = document.getElementById('rdpmsGraphChartDiv');
//    if (!el) {
//        console.error('[Graph] Chart div not found');
//        return;
//    }

//    var series = [];
//    var legends = [];
//    var allValues = [];

//    // Color palette
//    var colors = ['#259dab', '#dc3545', '#0d6efd', '#198754', '#ffc107', '#6f42c1', '#fd7e14', '#20c997', '#e83e8c', '#17a2b8'];
//    var colorIdx = 0;

//    // Attributes to show for Point Machine (RDPMS indicators only)
//    var pmAllowedIds = [25, 26, 27, 28, 576, 577, 578, 579, 212, 213, 214, 215, 216, 217, 218, 219];
//    var pmAllowedNames = ['nwkr', 'rwkr', 'nw-v', 'nw-c', 'rw-v', 'rw-c'];

//    // Check if this is Point Machine asset type
//    var assetTypeId = $('#rdpmsGraphOverlay').data('assetTypeId') || $('#drpAssetType').val();
//    var isPointMachine = (assetTypeId == 3 || assetTypeId === '3');

//    // Build AttrId->name from wsLiveData[assetId].attrs -- same source as checkboxes (_gPop).
//    // This gives the EXACT attribute name the user sees in the checkbox panel.
//    // We do NOT use dlRoleNameMap (no AssetId scope -- gives wrong cross-asset names).
//    var liveAttrIdToName = {};
//    var liveAsset = wsLiveData[assetId];
//    if (liveAsset && liveAsset.attrs) {
//        for (var _k in liveAsset.attrs) {
//            var _a = liveAsset.attrs[_k];
//            var _id = parseInt(_a.AttrId || _a.AssetAttributeId || 0);
//            if (_id) liveAttrIdToName[_id] = _k;
//        }
//    }
//    // Also add DataLogger relay names from dlAssetRoleMap (asset-scoped)
//    for (var _dlKey in dlAssetRoleMap) {
//        if (_dlKey.indexOf(String(assetId) + '_') === 0) {
//            var _dlAttrId = parseInt(_dlKey.split('_')[1]);
//            if (_dlAttrId && !liveAttrIdToName[_dlAttrId]) {
//                liveAttrIdToName[_dlAttrId] = dlAssetRoleMap[_dlKey];
//            }
//        }
//    }

//    data.forEach(function (attrData) {
//        var attrId = attrData.AttributeId;

//        // Skip if ALL values are DataLogger (relay) -- we don't plot relays in this graph
//        if (attrData.Values) {
//            var allKeys = Object.keys(attrData.Values);
//            var isDataLogger = allKeys.length > 0 && allKeys.every(function (k) {
//                return attrData.Values[k] && attrData.Values[k].DataType === 'DataLogger';
//            });
//            if (isDataLogger) return;
//        }

//        // Skip if this attrId is not in the checkbox panel for this asset
//        if (!liveAttrIdToName[attrId]) return;

//        // Get name -- same key used in _gChecked
//        var attrName = liveAttrIdToName[attrId];

//        // Skip if user has unchecked this attribute
//        if (_gChecked[attrName] === false) return;

//        var points = [];
//        for (var key in attrData.Values) {
//            var entry = attrData.Values[key];
//            if (!entry || !entry.Timestamp || entry.Value === undefined) continue;

//            var ts = entry.Timestamp.TimestampDevice || entry.Timestamp.TimestampLocal;
//            if (!ts || ts.indexOf('0001') >= 0) continue;

//            var time = new Date(ts).getTime();
//            var val = parseFloat(entry.Value);

//            if (!isNaN(time) && !isNaN(val) && time > 0) {
//                points.push([time, val]);
//                allValues.push(val);
//            }
//        }

//        if (points.length === 0) return;
//        points.sort(function (a, b) { return a[0] - b[0]; });

//        var displayName = attrName;
//        var color = _gColorMap[attrName] || colors[colorIdx % colors.length];
//        _gColorMap[attrName] = color;
//        colorIdx++;

//        legends.push(displayName);
//        series.push({
//            name: displayName,   // ← exact same name shown in checkbox panel
//            type: 'line',
//            smooth: true,
//            symbol: 'circle',
//            symbolSize: 6,
//            showSymbol: points.length < 50,
//            lineStyle: { width: 2.5, color: color },
//            itemStyle: { color: color },
//            emphasis: { lineStyle: { width: 4 } },
//            data: points
//        });
//    });
//    if (series.length === 0) {
//        $chartDiv.hide();
//        $('#rdpmsGraphError').html('<i class="fas fa-info-circle" style="font-size:24px;display:block;margin-bottom:10px;"></i>No data available for the selected time range').show();
//        return;
//    }

//    // Dispose existing chart
//    if (rdpmsGraphChart) {
//        rdpmsGraphChart.dispose();
//        rdpmsGraphChart = null;
//    }

//    // Force visibility and reflow before ECharts init (fixes "Chart div not found")
//    $chartDiv.css({ display: 'block', visibility: 'visible' });
//    el.offsetHeight;
//    rdpmsGraphChart = echarts.init(el, 'dark');

//    // Calculate Y axis range
//    var minVal = Math.min.apply(null, allValues);
//    var maxVal = Math.max.apply(null, allValues);
//    var range = maxVal - minVal;
//    var padding = range * 0.15 || 5;

//    rdpmsGraphChart.setOption({
//        backgroundColor: 'transparent',
//        tooltip: {
//            trigger: 'axis',
//            backgroundColor: 'rgba(10,26,46,0.96)',
//            borderColor: 'rgba(37,157,171,0.4)',
//            borderWidth: 1,
//            padding: [12, 16],
//            textStyle: { color: '#e2e8f0', fontSize: 13 },
//            formatter: function (params) {
//                if (!params || !params.length) return '';
//                var t = new Date(params[0].value[0]);
//                var timeStr = String(t.getHours()).padStart(2, '0') + ':' +
//                    String(t.getMinutes()).padStart(2, '0') + ':' +
//                    String(t.getSeconds()).padStart(2, '0');
//                var html = '<div style="font-weight:600;margin-bottom:8px;color:#22d3ee;">' + timeStr + '</div>';
//                params.forEach(function (p) {
//                    html += '<div style="display:flex;align-items:center;margin:4px 0;">' +
//                        '<span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:' +
//                        p.color + ';margin-right:10px;"></span>' +
//                        '<span style="flex:1;color:#cbd5e1;">' + p.seriesName + '</span>' +
//                        '<span style="font-weight:700;margin-left:15px;color:#fff;">' + p.value[1].toFixed(2) + '</span></div>';
//                });
//                return html;
//            }
//        },
//        legend: {
//            data: legends,
//            type: 'scroll',
//            orient: 'horizontal',
//            top: 10,
//            left: 'center',
//            width: '85%',
//            textStyle: { fontSize: 12, color: '#94a3b8' },
//            pageTextStyle: { color: '#94a3b8' },
//            pageIconColor: '#259dab',
//            pageIconInactiveColor: '#334155',
//            itemGap: 16,
//            itemWidth: 25,
//            itemHeight: 12
//        },
//        grid: {
//            top: legends.length <= 3 ? 45 : legends.length <= 6 ? 70 : legends.length <= 9 ? 95 : 120,
//            left: 65,
//            right: 30,
//            bottom: 70
//        },
//        toolbox: {
//            right: 15,
//            top: 8,
//            iconStyle: { borderColor: 'rgba(255,255,255,0.55)' }, emphasis: { iconStyle: { borderColor: '#22d3ee' } },
//            emphasis: { iconStyle: { borderColor: '#22d3ee' } },
//            feature: {
//                dataZoom: { title: { zoom: 'Zoom', back: 'Reset' } },
//                saveAsImage: { title: 'Save' }
//            }
//        },
//        xAxis: {
//            type: 'time',
//            axisLabel: {
//                fontSize: 11,
//                color: 'rgba(255,255,255,0.55)',
//                formatter: function (v) {
//                    var d = new Date(v);
//                    return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
//                }
//            },
//            axisLine: { lineStyle: { color: '#1e293b' } },
//            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
//        },
//        yAxis: {
//            type: 'value',
//            name: 'Value',
//            nameTextStyle: { fontSize: 12, color: '#64748b' },
//            min: function (value) { return Math.floor(value.min - padding); },
//            max: function (value) { return Math.ceil(value.max + padding); },
//            axisLabel: { fontSize: 11, color: '#64748b' },
//            axisLine: { show: true, lineStyle: { color: '#259dab', width: 2 } },
//            splitLine: { show: true, lineStyle: { color: '#1e293b', type: 'dashed' } }
//        },
//        dataZoom: [
//            {
//                type: 'slider',
//                height: 28,
//                bottom: 8,
//                start: 0,
//                end: 100,
//                borderColor: '#1e293b',
//                backgroundColor: 'rgba(15,23,42,0.6)',
//                fillerColor: 'rgba(37,157,171,0.25)',
//                handleStyle: { color: '#259dab', borderColor: '#259dab' },
//                textStyle: { color: '#64748b' },
//                dataBackground: { lineStyle: { color: '#334155' }, areaStyle: { color: 'rgba(37,157,171,0.08)' } }
//            },
//            { type: 'inside' }
//        ],
//        series: series
//    });

//    $(window).off('resize.rdpmsGraph').on('resize.rdpmsGraph', function () {
//        if (rdpmsGraphChart) rdpmsGraphChart.resize();
//    });

//    console.log('[Graph] Rendered', series.length, 'series for asset type', assetTypeId);
//}

function closeGraphModal(e) {
    if (e && e.target && !$(e.target).hasClass('tl-modal-overlay') && !$(e.target).hasClass('rdpms-graph-overlay')) return;
    if (rdpmsGraphChart) {
        rdpmsGraphChart.dispose();
        rdpmsGraphChart = null;
    }
    if (window._singleArrChartInst) {
        try { window._singleArrChartInst.dispose(); } catch (ex) { }
        window._singleArrChartInst = null;
    }
    $(window).off('resize.rdpmsGraph').off('resize.singleArrChart');
    window._singleArrParams = null;
    $('#rdpmsGraphOverlay').removeClass('tl-modal-open');
    $('body').removeClass('tl-modal-active');
    setTimeout(function () { $('#rdpmsGraphOverlay').remove(); }, 250);
}
$(document).on('keydown', function (e) { if (e.key === 'Escape' && $('#rdpmsGraphOverlay').length) closeGraphModal(); });

// ── Fullscreen toggle for Graph and Circuit modals ──
function toggleModalFullscreen(overlayId) {
    var $overlay = $('#' + overlayId);
    var $shell = $overlay.find('.tl-modal-shell');
    var $btn = $overlay.find('.tl-modal-fullscreen-btn i');

    $shell.toggleClass('tl-modal-fullscreen');

    if ($shell.hasClass('tl-modal-fullscreen')) {
        $btn.removeClass('fa-expand').addClass('fa-compress');
    } else {
        $btn.removeClass('fa-compress').addClass('fa-expand');
    }

    // Resize ECharts if graph modal
    setTimeout(function () {
        if (rdpmsGraphChart) rdpmsGraphChart.resize();
        if (window._singleArrChartInst) window._singleArrChartInst.resize();

        // Resize JointJS paper if circuit modal
        if (window.appModel && window.appModel.paper) {
            var isFullscreen = $shell.hasClass('tl-modal-fullscreen');
            var $body = $shell.find('.tl-modal-body, .tl-circuit-body');
            var $appBody = $shell.find('.app-body');
            var $paperContainer = $shell.find('.paper-container');
            var $scroller = $shell.find('.joint-paper-scroller');

            if (isFullscreen) {
                var headerH = $shell.find('.tl-modal-head').outerHeight() || 60;
                var availH = window.innerHeight - headerH - 20;

                $body.css({ 'height': availH + 'px', 'max-height': 'none', 'overflow': 'hidden' });
                $appBody.css({ 'height': '100%', 'min-height': availH + 'px' });
                $paperContainer.css({ 'height': '100%', 'min-height': availH + 'px', 'width': '100%' });
                $scroller.css({ 'height': availH + 'px', 'width': '100%', 'overflow': 'auto' });

                try {
                    window.appModel.paper.scaleContentToFit({
                        padding: 30,
                        maxScale: 1.5,
                        minScale: 0.3
                    });
                } catch (e) {
                    window.appModel.paper.fitToContent({ padding: 20 });
                }
            } else {
                $body.css({ 'height': '', 'max-height': '', 'overflow': '' });
                $appBody.css({ 'height': '', 'min-height': '' });
                $paperContainer.css({ 'height': '', 'min-height': '', 'width': '' });
                $scroller.css({ 'height': '', 'width': '', 'overflow': 'auto' });

                try {
                    $("input[name=zoom-slider]").val(20).trigger('change');
                } catch (e) { }
                window.appModel.paper.fitToContent({ padding: 20 });
            }
        }
    }, 300);
}
function renderModalChart(data) {
    var DS = [], maxV = 0, legends = [], macLeft = 0, lary = [], rary = [];
    if (data.assetAttributes && data.assetAttributes.length > 0) { $.each(data.assetAttributes, function (i, v) { legends.push(v.Title); var a = { name: v.Title, type: 'line', showSymbol: false, sampling: 'none', hoverAnimation: false, clip: true, smooth: true, lineStyle: { width: 2 } }; if (v.Title.indexOf('V') > -1) { rary.push({ value: v.Title }); a.yAxisIndex = 1; } else { lary.push({ value: v.Title }); a.yAxisIndex = 0; } if (v.Title.indexOf('V') > -1) $.each(v.Data, function (j, d) { var n = parseFloat(d); if (n > maxV) maxV = n; }); var vals = []; $.each(v.Data, function (j, d) { vals.push(d); }); if (v.Title.indexOf('V') <= -1) { var yl = Math.max.apply(Math, vals); if (yl >= macLeft) macLeft = yl; } a.connectNulls = true; a.data = vals; a.datasetIndex = i; DS.push(a); }); }
    var xV = []; $.each(data.DateList, function (i, v) { xV.push(v); });
    if (rdpmsGraphChart) rdpmsGraphChart.dispose();
    rdpmsGraphChart = echarts.init(document.getElementById('rdpmsGraphChartDiv'));
    rdpmsGraphChart.setOption({ tooltip: { trigger: 'axis', axisPointer: { type: 'cross', label: { backgroundColor: 'rgba(34,211,238,0.80)' } }, backgroundColor: 'rgba(5,9,24,0.97)', borderColor: 'rgba(34,211,238,0.30)', textStyle: { color: 'rgba(255,255,255,0.86)' } }, legend: { data: legends, x: 'left', textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.72)' } }, toolbox: { show: true, feature: { dataView: { show: true, readOnly: false }, magicType: { show: true, type: ['line', 'bar'] }, restore: { show: true }, saveAsImage: { show: true } } }, grid: { left: '3%', right: '4%', bottom: '15%', top: '15%', containLabel: true }, xAxis: [{ type: 'category', boundaryGap: false, axisLine: { onZero: false }, data: xV, axisLabel: { fontSize: 10 } }], yAxis: [{ type: 'value', min: 0, max: macLeft || 'dataMax', data: lary, name: 'mA', nameTextStyle: { fontSize: 11 } }, { type: 'value', inverse: false, min: -1, max: maxV + 1, data: rary, name: 'V', nameTextStyle: { fontSize: 11 } }], dataZoom: [{ type: 'slider', height: 30, start: 0, end: 100, bottom: 5 }], color: ["#4bacc6", "#215968", "#717171", "#7366ff", "#cc6600", "#90d474", "#4d4d00", "#00e396", "#00ceba", "#f37995"], series: DS });
    $(window).on('resize.rdpmsGraph', function () { if (rdpmsGraphChart) rdpmsGraphChart.resize(); });
}

// ===== VIEW TYPE OPTIONS -- Updated for PointMachine =====
function isSignalAssetType() { var t = ($('#drpAssetType option:selected').text() || '').trim().toLowerCase(); return (t === 'signal' || $('#drpAssetType').val() === '2'); }
function isPointAssetType() { var t = ($('#drpAssetType option:selected').text() || '').trim().toLowerCase(); return (t.indexOf('point') > -1 || $('#drpAssetType').val() === '3'); }
function isIpsAssetType() { var t = ($('#drpAssetType option:selected').text() || '').trim().toLowerCase(); return (t === 'ips' || t.indexOf('ips') > -1); }

function updateViewTypeOptions() {
    var $drpView = $('#drpView');
    var isSignal = isSignalAssetType();
    var isPoint = isPointAssetType();
    var isIps = isIpsAssetType();
    var $rdpmsOpt = $drpView.find('option[value="RDPMS"]');
    var $ipsOpt = $drpView.find('option[value="IPS"]');
    var currentView = $drpView.val();
    var neutralViews = ['Table', 'Cards'];  // Always available

    if (isIps) {
        if ($rdpmsOpt.length) { if (currentView === 'RDPMS') $drpView.val('Cards'); $rdpmsOpt.remove(); }
        if ($ipsOpt.length === 0) $drpView.find('option[value="Cards"]').after('<option value="IPS">IPS</option>');
        if (currentView === 'Table') $drpView.val('IPS');

    } else if (isSignal) {
        if ($ipsOpt.length) { if (currentView === 'IPS') $drpView.val('Cards'); $ipsOpt.remove(); }
        if ($rdpmsOpt.length === 0) $drpView.find('option[value="Cards"]').after('<option value="RDPMS">RDPMS</option>');
        if (neutralViews.indexOf(currentView) === -1 && currentView !== 'RDPMS') $drpView.val('RDPMS');

    } else if (isPoint) {
        // PM no longer has its own view — it renders through Cards / Table
        if ($rdpmsOpt.length) { if (currentView === 'RDPMS') $drpView.val('Cards'); $rdpmsOpt.remove(); }
        if ($ipsOpt.length) { if (currentView === 'IPS') $drpView.val('Cards'); $ipsOpt.remove(); }
        if (neutralViews.indexOf(currentView) === -1) $drpView.val('Cards');

    } else {
        // Track / generic
        if ($rdpmsOpt.length) { if (currentView === 'RDPMS') $drpView.val('Cards'); $rdpmsOpt.remove(); }
        if ($ipsOpt.length) { if (currentView === 'IPS') $drpView.val('Cards'); $ipsOpt.remove(); }
    }
    updateViewMode();
    updateDataSourceToggleState();
}
function updateDataSourceToggleState() {
    var isSignal = isSignalAssetType();
    var isPoint = isPointAssetType();
    var isIps = isIpsAssetType();
    var $toggle = $('#chkDataSource');
    var $toggleContainer = $('.data-source-toggle');
    var $slider = $toggle.siblings('.slider');

    if (isSignal || isPoint || isIps) {
        // Force WebSocket mode for Signal and Point Machine
        if ($toggle.prop('checked')) {
            $toggle.prop('checked', false);
            useApiDataSource = false;
            $('#lblApi').css('opacity', '0.5');
            $('#lblWs').css('opacity', '1');
        }

        // Disable the toggle
        $toggle.prop('disabled', true);
        $toggleContainer.addClass('disabled-toggle');
        $slider.css({
            'cursor': 'not-allowed',
            'opacity': '0.6'
        });

        // Add disabled indicator badge
        if (!$toggleContainer.find('.ws-only-badge').length) {
            $toggleContainer.append('<span class="ws-only-badge" title="WebSocket only for Signal and Point Machine asset types"><i class="fas fa-lock"></i> WS Only</span>');
        }

        // Update label styling - strike through API
        $('#lblApi').addClass('strikethrough').css({
            'opacity': '0.3',
            'text-decoration': 'line-through'
        });
        $('#lblWs').css({
            'opacity': '1',
            'font-weight': '600'
        });

        // Hide refresh button (API only feature)
        $('#btnRefresh').hide();

        console.log('[DataSource] Toggle DISABLED - WebSocket only for ' + (isSignal ? 'Signal' : 'Point Machine'));
    } else {
        // Re-enable the toggle for other asset types (Track, etc.)
        $toggle.prop('disabled', false);
        $toggleContainer.removeClass('disabled-toggle');
        $slider.css({
            'cursor': 'pointer',
            'opacity': '1'
        });

        // Remove disabled indicator badge
        $toggleContainer.find('.ws-only-badge').remove();

        // Reset label styling
        $('#lblApi').removeClass('strikethrough').css({
            'text-decoration': 'none'
        });
        $('#lblWs').css({
            'font-weight': '500'
        });

        // Restore opacity based on current state
        if (useApiDataSource) {
            $('#lblApi').css('opacity', '1');
            $('#lblWs').css('opacity', '0.5');
            $('#btnRefresh').show();
        } else {
            $('#lblApi').css('opacity', '0.5');
            $('#lblWs').css('opacity', '1');
            $('#btnRefresh').hide();
        }

        console.log('[DataSource] Toggle ENABLED - API available');
    }
}


function updateViewMode() {

    // Always keep multi-select visible
    $('#assetMultiSelectContainer').show();
    $('#assetSingleSelectContainer').hide();

    // Keep your restriction logic
    updateViewTypeRestrictions();

    var viewType = $('#drpView').val();

    // [STOP] Stop any pending render timer
    if (typeof wsRenderTimer !== 'undefined' && wsRenderTimer) {
        clearTimeout(wsRenderTimer);
        wsRenderTimer = null;
    }

    // ── GUARD: Do not render table/cards until user has clicked Search.
    //    Before Search, just clean up and show nothing. ──
    if (!window._tlSearchInitiated) {
        return;
    }

    // [CLEAN] Remove previous rendered view
    $('#wsLiveTable').remove();              // Table view container
    $('#rdpmsMainContainer').remove();       // RDPMS view container
    $('#pointMachineContainer').remove();    // Point Machine view container
    $('#pmMainContainer').remove();          // PM card container (created by renderPointMachineView)

    // Clear main telemetry div
    $('#divTelemetryLive').empty();

    // Reset incremental update cache
    if (typeof wsUpdatedAssets !== 'undefined') {
        wsUpdatedAssets = {};
    }

    // Reset PM card build cache so cards are rebuilt fresh
    if (typeof pmCardsBuilt !== 'undefined') pmCardsBuilt = {};
    if (typeof pmCardsBuilding !== 'undefined') pmCardsBuilding = {};

    // [LAUNCH] Render selected view fresh
    if (viewType === 'Table') {
        renderWsTableFixed();
    }
    else if (viewType === 'RDPMS') {
        renderRDPMSView();
    }
    else if (viewType === 'PointMachine') {
        renderPointMachineView();
    }
}
// ===== DROPDOWN HELPERS =====
function filterAssetDropdown(s) {
    var $items = $('#listAssetNumber .dropdown-item');
    var $nr = $('#ddAssetNumber .dropdown-no-results');
    var search = (s || '').toLowerCase().trim();
    var visibleCount = 0;

    $items.each(function () {
        var $item = $(this);
        var itemText = ($item.data('text') || $item.find('span').text() || '').toString().toLowerCase();
        var itemValue = ($item.data('value') || $item.find('input').val() || '').toString().toLowerCase();

        // Check if search term matches text or value
        var matches = (search === '' || itemText.indexOf(search) !== -1 || itemValue.indexOf(search) !== -1);

        if (matches) {
            $item.removeClass('hidden').show();
            visibleCount++;

            // Highlight matching text
            if (search !== '') {
                var origText = $item.data('text') || $item.find('span').text();
                var highlighted = origText.replace(new RegExp('(' + escapeRegex(s) + ')', 'gi'), '<mark>$1</mark>');
                $item.find('span').html(highlighted);
            } else {
                // Restore original text
                var origText = $item.data('text');
                if (origText) {
                    $item.find('span').text(origText);
                }
            }
        } else {
            $item.addClass('hidden').hide();
        }
    });

    // Show/hide "no results" message
    if (visibleCount === 0 && search !== '') {
        $nr.addClass('show').show();
    } else {
        $nr.removeClass('show').hide();
    }

    // Update Select All checkbox state based on visible items
    updateSelectAllState();
}
function escapeRegex(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }
function selectAllAssets(c) { $('#listAssetNumber .dropdown-item:not(.hidden)').each(function () { $(this).find('input').prop('checked', c); $(this).toggleClass('checked', c); }); updateAssetText(); }
function updateSelectAllState() { var $v = $('#listAssetNumber .dropdown-item:not(.hidden)'), t = $v.length, c = $v.find('input:checked').length; $('#chkAllAssetNumber').prop('checked', t > 0 && t === c); }
function updateAssetText() {
    var $c = $('#listAssetNumber input:checked');
    var n = $c.length;
    var txt = 'All';

    if (n === 1) {
        txt = $c.first().closest('.dropdown-item').data('text');
    } else if (n > 1) {
        txt = n + ' selected';
    }

    $('#txtAssetNumber').text(txt);

    // IMPORTANT: Trigger view type restriction check whenever asset selection changes
    updateViewTypeRestrictions();
}
//function loadAssetNumbersMulti(siteId, assetTypeId) { $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>'); $('#chkAllAssetNumber').prop('checked', false); $('#txtAssetNumber').text('All'); if (!siteId || siteId === '' || siteId === '0' || !assetTypeId || assetTypeId === '' || assetTypeId === '0') return; $('#listAssetNumber').html('<div class="dropdown-empty">Loading...</div>'); loadBulkAssetMetadata(siteId, assetTypeId, function () { var d = bulkAssetsList; if (d && d.length > 0) { var h = ''; $.each(d, function (k, v) { h += '<div class="dropdown-item" data-value="' + v.Id + '" data-text="' + v.Name + '"><input type="checkbox" value="' + v.Id + '"><span>' + v.Name + '</span></div>'; }); $('#listAssetNumber').html(h); if (typeof window._tlAutoLoad === 'function') window._tlAutoLoad({ selectAll: true }); } else { $('#listAssetNumber').html('<div class="dropdown-empty">No assets found</div>'); } }); }
function loadAssetNumbersMulti(siteId, assetTypeId) {
    if (typeof resetPmAssetMeta === 'function') resetPmAssetMeta();

    $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
    $('#chkAllAssetNumber').prop('checked', false);
    $('#txtAssetNumber').text('All');
    wsValidAssetIds = [];

    if (!siteId || siteId === '' || siteId === '0' ||
        !assetTypeId || assetTypeId === '' || assetTypeId === '0') return;

    $('#listAssetNumber').html('<div class="dropdown-empty">Loading...</div>');

    // Point Machine compatibility: use old working GetAssestBy + GetPMAssetMeta path.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip(assetTypeId)) {
        $.ajax({
            url: '/FRS25/Telemetry/GetAssestBy',
            type: 'POST',
            data: JSON.stringify({ siteId: siteId, assetTypeId: assetTypeId }),
            contentType: 'application/json',
            success: function (d) {
                var list = Array.isArray(d) ? d : (d && (d.Data || d.data || d.Result || []));
                if (list && list.length > 0) {
                    var h = '';
                    wsValidAssetIds = [];
                    bulkAssetsList = [];
                    bulkAssetMap = {};

                    $.each(list, function (k, v) {
                        wsValidAssetIds.push(String(v.Id));
                        bulkAssetsList.push({ Id: v.Id, Name: v.Name, SiteId: siteId, AssetTypeId: assetTypeId });
                        // FIX: PM uses GetAssestBy (not GetBulkAssetMetadata), so bulkAssetMap
                        // was never populated → getBulkAssetName() fell through to "Asset <id>",
                        // breaking the PM asset-no binding in card/list views. Populate it here.
                        bulkAssetMap[String(v.Id)] = {
                            Id: v.Id, Name: v.Name, AssetName: v.Name,
                            SiteId: siteId, AssetTypeId: assetTypeId
                        };
                        h += '<div class="dropdown-item" data-value="' + _tlEscHtml(v.Id) + '" data-text="' + _tlEscHtml(v.Name) + '">' +
                            '<input type="checkbox" value="' + _tlEscHtml(v.Id) + '"><span>' + _tlEscHtml(v.Name) + '</span>' +
                            '</div>';
                    });

                    window.bulkAssetsList = bulkAssetsList;
                    window.bulkAssetMap = bulkAssetMap;
                    $('#listAssetNumber').html(h);

                    if (typeof window._tlAutoLoad === 'function') {
                        window._tlAutoLoad({ selectAll: true });
                    } else {
                        $('#listAssetNumber .dropdown-item').each(function () {
                            $(this).find('input').prop('checked', true);
                            $(this).addClass('checked');
                        });
                        $('#chkAllAssetNumber').prop('checked', true);
                        updateAssetText();
                    }

                    if (typeof loadPmAssetMeta === 'function') loadPmAssetMeta(siteId, assetTypeId);

                    Object.keys(wsLiveData || {}).forEach(function (id) {
                        if (wsValidAssetIds.indexOf(String(id)) === -1) {
                            console.log('[Assets] Pruning invalid asset from live data:', id);
                            delete wsLiveData[id];
                            $('#wsLiveTable tbody tr[data-id="' + id + '"]').remove();
                        }
                    });

                    console.log('[Assets] PM whitelist populated:', wsValidAssetIds.length, 'assets for site=' + siteId + ', type=' + assetTypeId);
                } else {
                    wsValidAssetIds = [];
                    bulkAssetsList = [];
                    window.bulkAssetsList = bulkAssetsList;
                    $('#listAssetNumber').html('<div class="dropdown-empty">No assets found</div>');
                }
            },
            error: function () {
                wsValidAssetIds = [];
                $('#listAssetNumber').html('<div class="dropdown-empty">Error loading</div>');
            }
        });
        return;
    }

    loadBulkAssetMetadata(siteId, assetTypeId, function () {
        if (typeof bulkAssetsList !== 'undefined') {
            wsValidAssetIds = [];
            for (var i = 0; i < bulkAssetsList.length; i++) {
                wsValidAssetIds.push(String(bulkAssetsList[i].Id));
            }
        }
        populateAssetDropdownsFromBulk({ autoLoad: true });
    });
}
// Ensure the PM-aware loader is reachable from the view's pill handler.
window.loadAssetNumbersMulti = loadAssetNumbersMulti;

function GetDivisionByZone(zoneId) { if (!zoneId || zoneId === '' || zoneId === '0') { return; } $("#loader").show(); $.ajax({ url: '/FRS25/Telemetry/GetDivisionByZoneId', type: 'POST', data: JSON.stringify({ zoneId: zoneId }), contentType: 'application/json', success: function (d) { $("#loader").hide(); $("#drpDivisions").empty().append('<option value="">All</option>'); if (d && d.length > 0) { $.each(d, function (k, v) { $("#drpDivisions").append('<option value="' + v.Id + '">' + v.Name + '</option>'); }); } $("#drpSite").empty().append('<option value="">All</option>'); $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>'); $('#txtAssetNumber').text('All'); updateViewTypeRestrictions(); }, error: function () { $("#loader").hide(); } }); }
function GetByDivisionId(divId) { if (!divId || divId === '0' || divId === '') return; $("#loader").show(); $.ajax({ url: '/FRS25/Telemetry/GetSiteByDivisionId', type: 'POST', data: JSON.stringify({ divisionId: divId }), contentType: 'application/json', success: function (d) { $("#loader").hide(); $("#drpSite").empty().append('<option value="">All</option>'); if (d && d.length > 0) { $.each(d, function (k, v) { $("#drpSite").append('<option value="' + v.Id + '">' + v.Name + '</option>'); }); } $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>'); $('#txtAssetNumber').text('All'); updateViewTypeRestrictions(); }, error: function () { $("#loader").hide(); console.log('[Site] Error loading sites for division: ' + divId); } }); }
//function GetByAssest(siteId, assetTypeId) { if (!siteId || siteId === '' || siteId === '0' || !assetTypeId || assetTypeId === '' || assetTypeId === '0') return; $("#loader").show(); loadBulkAssetMetadata(siteId, assetTypeId, function () { $("#loader").hide(); $("#drpAsset").empty().append('<option value="">All</option>'); var d = bulkAssetsList; if (d && d.length > 0) { $.each(d, function (k, v) { $("#drpAsset").append('<option value="' + v.Id + '">' + v.Name + '</option>'); }); } }); }
function GetByAssest(siteId, assetTypeId) {
    if (!siteId || siteId === '' || siteId === '0' || !assetTypeId || assetTypeId === '' || assetTypeId === '0') return;

    $('#loader').show();

    // Point Machine compatibility: keep old GetAssestBy dropdown source.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip(assetTypeId)) {
        $.ajax({
            url: '/FRS25/Telemetry/GetAssestBy',
            type: 'POST',
            data: JSON.stringify({ siteId: siteId, assetTypeId: assetTypeId }),
            contentType: 'application/json',
            success: function (d) {
                $('#loader').hide();

                var list = Array.isArray(d) ? d : (d && (d.Data || d.data || d.Result || []));
                $('#drpAsset').empty().append('<option value="">All</option>');
                bulkAssetsList = [];
                bulkAssetMap = {};

                if (list && list.length > 0) {
                    $.each(list, function (k, v) {
                        $('#drpAsset').append('<option value="' + _tlEscHtml(v.Id) + '">' + _tlEscHtml(v.Name) + '</option>');
                        bulkAssetsList.push({ Id: v.Id, Name: v.Name, SiteId: siteId, AssetTypeId: assetTypeId });
                        // FIX: keep bulkAssetMap in sync so PM names resolve (see getBulkAssetName).
                        bulkAssetMap[String(v.Id)] = {
                            Id: v.Id, Name: v.Name, AssetName: v.Name,
                            SiteId: siteId, AssetTypeId: assetTypeId
                        };
                    });
                }

                window.bulkAssetsList = bulkAssetsList;
                window.bulkAssetMap = bulkAssetMap;
                if (typeof loadPmAssetMeta === 'function') loadPmAssetMeta(siteId, assetTypeId);
            },
            error: function () {
                $('#loader').hide();
            }
        });
        return;
    }

    loadBulkAssetMetadata(siteId, assetTypeId, function () {
        $('#loader').hide();
        populateAssetDropdownsFromBulk({ autoLoad: false });
    });
}
// ================================================================
// GET ASSET TYPES BY SITE ID
// Fetches available asset types for the selected site
// ================================================================
function GetAssetTypeBySiteId(siteId) {
    if (!siteId || siteId === '' || siteId === '0') {
        return;
    }

    $("#loader").show();

    $.ajax({
        url: '/FRS25/Telemetry/GetAssetTypeBySiteId',
        type: 'POST',
        data: JSON.stringify({ siteId: siteId }),
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();

            // Clear and rebuild asset type dropdown
            $("#drpAssetType").empty().append('<option value="">All</option>');

            if (data && data.length > 0) {
                // Sort by Name for consistent display
                //data.sort(function (a, b) {
                //    return (a.Name || '').localeCompare(b.Name || '');
                //});
                data.sort(function (a, b) {
                    return a.Id - b.Id;
                });

                $.each(data, function (index, item) {
                    if (item.IsActive) {
                        $("#drpAssetType").append('<option value="' + item.Id + '">' + item.Name + '</option>');
                    }
                });

                console.log('[AssetType] Loaded ' + data.length + ' asset types for site ' + siteId);
            } else {
                console.log('[AssetType] No asset types found for site ' + siteId);
            }

            // Reset dependent dropdowns
            $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
            $('#txtAssetNumber').text('All');
            $("#drpAsset").empty().append('<option value="">All</option>');

            // Update view type restrictions
            updateViewTypeRestrictions();
        },
        error: function (xhr, status, error) {
            $("#loader").hide();
            console.error('[AssetType] Error loading asset types for site ' + siteId + ':', error);
        }
    });
}
function getReportLocationInfo(callback) {
    var zoneTxt = $('#drpZones option:selected').text().trim();
    var divisionTxt = $('#drpDivisions option:selected').text().trim();
    var stationTxt = $('#drpSite option:selected').text().trim();
    var siteId = $('#drpSite').val();
    var zoneIsAll = !zoneTxt || zoneTxt === 'Select' || zoneTxt === 'All';
    var divisionIsAll = !divisionTxt || divisionTxt === 'Select' || divisionTxt === 'All';
    if (!zoneIsAll && !divisionIsAll) { callback({ zone: zoneTxt, division: divisionTxt, station: stationTxt }); return; }
    if (!siteId || siteId === '0' || siteId === '') { callback({ zone: zoneTxt, division: divisionTxt, station: stationTxt }); return; }
    $.ajax({
        url: '/FRS25/Telemetry/GetSiteById?siteId=' + siteId, type: 'GET',
        success: function (data) {
            callback({
                zone: zoneIsAll ? (data.ZoneName || data.Zone || zoneTxt) : zoneTxt,
                division: divisionIsAll ? (data.DivisionName || data.Division || divisionTxt) : divisionTxt,
                station: data.Name || data.SiteName || stationTxt
            });
        },
        error: function () { callback({ zone: zoneTxt, division: divisionTxt, station: stationTxt }); }
    });
}
// ===== DOWNLOAD =====
function fnDownloadExcel() { var table = document.querySelector('#divTelemetryLive table'); if (!table) { showWarning('No data available to download. Please search first.', 'No Data'); return; } var dataRows = table.querySelectorAll('tbody tr'); if (!dataRows || dataRows.length === 0) { showWarning('No data available to download. The table is empty.', 'No Data'); return; } try { $("#loader").show(); var wb = new ExcelJS.Workbook(); wb.creator = 'RDPMS'; wb.created = new Date(); var ws = wb.addWorksheet('Telemetry Live'); var rows = table.querySelectorAll('tr'), td = [], cc = 0; rows.forEach(function (r) { var rd = []; var c = r.querySelectorAll('th, td'); cc = Math.max(cc, c.length); c.forEach(function (cl) { rd.push(cl.innerText.trim()); }); if (rd.length) td.push(rd); }); var tr = ws.addRow(['Telemetry Live Report']); tr.height = 30; ws.mergeCells(1, 1, 1, cc); tr.getCell(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4F46E5' } }; tr.getCell(1).font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 16 }; tr.getCell(1).alignment = { vertical: 'middle', horizontal: 'center' }; var ir = ws.addRow(['Site: ' + ($('#drpSite option:selected').text() || 'All') + ' | Generated: ' + new Date().toLocaleString()]); ir.height = 20; ws.mergeCells(2, 1, 2, cc); ir.getCell(1).font = { italic: true, size: 10, color: { argb: 'FF64748B' } }; ir.getCell(1).alignment = { horizontal: 'center' }; ws.addRow([]); td.forEach(function (r, i) { var er = ws.addRow(r); if (i === 0) { er.eachCell(function (c) { c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } }; c.font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 11 }; c.alignment = { horizontal: 'center', wrapText: true }; }); er.height = 25; } else { er.eachCell(function (c) { c.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: i % 2 === 0 ? 'FFF1F5F9' : 'FFFFFFFF' } }; c.font = { size: 10 }; }); er.height = 20; } }); ws.columns.forEach(function (col) { var ml = 12; col.eachCell({ includeEmpty: true }, function (c) { var l = c.value ? c.value.toString().length : 10; if (l > ml) ml = l; }); col.width = Math.min(ml + 2, 40); }); wb.xlsx.writeBuffer().then(function (b) { saveAs(new Blob([b], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), 'TelemetryLive_' + new Date().toISOString().slice(0, 10) + '.xlsx'); $("#loader").hide(); showSuccess('Excel downloaded!', 'Download'); }); } catch (e) { $("#loader").hide(); showError('Download failed', 'Error'); } }
function fnDownloadCSV() { var table = document.querySelector('#divTelemetryLive table'); if (!table) { showWarning('No data available to download. Please search first.', 'No Data'); return; } var dataRows = table.querySelectorAll('tbody tr'); if (!dataRows || dataRows.length === 0) { showWarning('No data available to download. The table is empty.', 'No Data'); return; } try { var csv = []; table.querySelectorAll('tr').forEach(function (r) { var rd = []; r.querySelectorAll('th, td').forEach(function (c) { rd.push('"' + c.innerText.trim().replace(/"/g, '""') + '"'); }); csv.push(rd.join(',')); }); saveAs(new Blob(['\uFEFF' + csv.join('\n')], { type: 'text/csv;charset=utf-8;' }), 'TelemetryLive_' + new Date().toISOString().slice(0, 10) + '.csv'); showSuccess('CSV downloaded!', 'Download'); } catch (e) { showError('Download failed', 'Error'); } }

// ===== CHART =====
function Demochart(data) {
    if (chart) chart.dispose();
    var DS = [], maxV = 0, legends = [], macLeft = 0, lary = [], rary = [], isVib = false;

    if (data.assetAttributes && data.assetAttributes.length > 0) {
        $.each(data.assetAttributes, function (i, v) {
            if (v.Title.indexOf('Vibration') > -1) isVib = true;
            legends.push(v.Title);

            var a = {
                name: v.Title,
                type: 'line',
                symbol: 'none',
                showSymbol: false,
                smooth: true,
                connectNulls: true,
                lineStyle: { width: 2 },
                sampling: 'lttb',
                hoverAnimation: true,
                clip: true
            };

            if (v.Title.indexOf('V') > -1) {
                rary.push({ value: v.Title });
                a.yAxisIndex = 1;
            } else {
                lary.push({ value: v.Title });
                a.yAxisIndex = 0;
            }

            if (v.Title.indexOf('V') > -1) {
                $.each(v.Data, function (j, d) {
                    var n = parseFloat(d);
                    if (!isNaN(n) && n > maxV) maxV = n;
                });
            }

            var vals = [];
            $.each(v.Data, function (j, d) {
                vals.push(parseFloat(d) || 0);
            });

            if (v.Title.indexOf('V') <= -1) {
                var yl = Math.max.apply(Math, vals.filter(function (x) { return !isNaN(x); }));
                if (yl >= macLeft) macLeft = yl;
            }

            a.data = vals;
            a.datasetIndex = i;
            DS.push(a);
        });
    }

    if (isVib) {
        if (typeof DemoPMVibrationchart === 'function') {
            DemoPMVibrationchart(data);
        }
        return false;
    }

    var xV = [];
    $.each(data.DateList, function (i, v) { xV.push(v); });

    chart = echarts.init(document.getElementById('area-echart'));
    chart.setOption({
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross', label: { backgroundColor: '#6a7985' } }
        },
        legend: { data: legends, x: 'left' },
        toolbox: {
            show: true,
            feature: {
                mark: { show: true },
                dataView: { show: true, readOnly: false },
                magicType: { show: true, type: ['line', 'bar'] },
                restore: { show: true },
                saveAsImage: { show: true }
            }
        },
        grid: { left: '3%', right: '4%', bottom: '15%', containLabel: true },
        xAxis: [{
            type: 'category',
            boundaryGap: false,
            axisLine: { onZero: false },
            axisLabel: {
                rotate: 45,
                fontSize: 10,
                interval: Math.max(0, Math.floor(xV.length / 10) - 1)
            },
            data: xV
        }],
        yAxis: [
            { type: 'value', min: 0, max: Math.ceil(macLeft * 1.1) || 100, data: lary },
            { type: 'value', inverse: false, min: 0, max: Math.ceil(maxV * 1.1) || 50, data: rary }
        ],
        dataZoom: [
            { type: 'slider', height: 30, start: 0, end: 100, bottom: 5 },
            { type: 'inside' }
        ],
        color: ["#dc2626", "#16a34a", "#d97706", "#7c3aed", "#0891b2", "#db2777", "#14b8a6", "#6366f1", "#0284c7", "#f59e0b"],
        series: DS
    });

    window.onresize = chart.resize;
}

// ===== UTILITY FUNCTIONS =====
function fnShowAspectData(assetId) { $("#loader").show(); $.ajax({ url: '/Telemetry/GetZeroOffsetValue', type: 'POST', data: JSON.stringify({ assetId: assetId }), contentType: 'application/json', dataType: 'html', success: function (data) { $("#loader").hide(); $("#model-div-alert").empty().append(data); $("#modalalertlogs").modal('show'); }, error: function () { $("#loader").hide(); showError('Something went wrong!', 'Error'); } }); }
function fnShowDigitalSignal(assetId) { var assetTypeId = 2; $("#loader").show(); $.ajax({ url: '/Site/GetDigitalSignalAttribute', type: 'POST', data: JSON.stringify({ assetTypeId: assetTypeId, assetId: assetId }), contentType: 'application/json', success: function (data) { $("#loader").hide(); $('#modelDigitalSignal').find('.modal-body').empty(); var value = '<table class="table table-bordered table-striped">'; if (data != null && data != undefined && data.length > 0) { $(data).each(function (val, attribute) { var gateContectTypeClassName = 'hdndigital' + assetId + '-' + attribute.Id; value += '<tr><td style="width:50%">' + attribute.Title + '</td><td class="' + gateContectTypeClassName + '" style="width:50%"></td></tr>'; }); } value += '</table>'; $('#modelDigitalSignal').find('.modal-body').append(value); $("#modelDigitalSignal").modal('show'); }, error: function () { $("#loader").hide(); showError('Something went wrong!', 'Error'); } }); }
function fnShowLuxSignal(assetId) { var assetTypeId = 2; $("#loader").show(); $.ajax({ url: '/Site/GetLuxSignalAttribute', type: 'POST', data: JSON.stringify({ assetTypeId: assetTypeId, assetId: assetId }), contentType: 'application/json', success: function (data) { $("#loader").hide(); $('#modelLuxSignal').find('.modal-body').empty(); var value = '<table class="table table-bordered table-striped">'; if (data != null && data != undefined && data.length > 0) { $(data).each(function (val, attribute) { var gateContectTypeClassName = 'hdnLux' + assetId + '-' + attribute.Id; value += '<tr><td style="width:50%">' + attribute.Title + '</td><td class="' + gateContectTypeClassName + '" style="width:50%"></td></tr>'; }); } value += '</table>'; $('#modelLuxSignal').find('.modal-body').append(value); $("#modelLuxSignal").modal('show'); }, error: function () { $("#loader").hide(); showError('Something went wrong!', 'Error'); } }); }
function fnShowDataLoggerEvent(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) { showWarning('No data available yet.', 'DataLogger'); return; }

    var _mn = asset.AssetName || assetId;
    if (asset.AssetTypeId == 3 && _mn.indexOf('PT-') !== 0) _mn = 'PT-' + _mn;

    // ── Latest operation from pmEventHistory ──
    // pmEventHistory rows are pushed in chronological order.
    // Last entry = most recent operation.
    var hist = pmEventHistory && pmEventHistory[assetId];
    var lastRow = (hist && hist.length > 0) ? hist[hist.length - 1] : null;
    var latestOpTs = lastRow ? (lastRow.timestampDevice || null) : null;
    var latestDir = lastRow ? (lastRow.operationType || null) : null;
    // latestDir is 'Normal' or 'Reverse' -- matches getPmStructuredData keys exactly

    // ── Structured PM data (already split by direction / type / metric) ──
    var pm = getPmStructuredData(assetId);

    var html = '<div style="padding:10px;">';
    html += '<h5 style="margin-bottom:4px;">All Values \u2014 ' + _mn + '</h5>';

    if (latestDir && latestOpTs) {
        var dtLabel = '';
        try { dtLabel = new Date(latestOpTs).toLocaleString(); } catch (e) { dtLabel = latestOpTs; }
        html += '<p style="font-size:12px;color:#64748b;margin-top:0;margin-bottom:12px;">'
            + latestDir + ' Operation &nbsp;•&nbsp; ' + dtLabel + '</p>';
    }

    html += '<h6 style="color:#1a6e74;margin-top:12px;">RDPMS Attributes</h6>';
    html += '<table class="table table-bordered table-sm">'
        + '<thead><tr><th>Attribute</th><th>Value</th><th>Last Updated</th></tr></thead><tbody>';

    var rowCount = 0;

    // ── Show only the latest direction's metrics, skip Array ──
    if (pm && latestDir && pm[latestDir]) {
        var dirData = pm[latestDir]; // { AC:{}, AV:{}, BC:{}, BV:{} }
        var typeOrder = ['AC', 'AV', 'BC', 'BV'];
        var typeLabels = { AC: 'A Current', AV: 'A Voltage', BC: 'B Current', BV: 'B Voltage' };
        var metOrder = ['Max', 'Avg', 'Min', 'OperationTime', 'Count'];
        var metLabels = { Max: 'Max', Avg: 'Avg', Min: 'Min', OperationTime: 'Operation Time', Count: 'Count' };
        // 'Array' is intentionally absent from metOrder -- never shown

        for (var ti = 0; ti < typeOrder.length; ti++) {
            var typ = typeOrder[ti];
            if (!dirData[typ]) continue;
            for (var mi = 0; mi < metOrder.length; mi++) {
                var met = metOrder[mi];
                var entry = dirData[typ][met];
                if (!entry) continue;
                var v = parseFloat(entry.value);
                var vs = isNaN(v) ? '\u2014' : v.toFixed(2);
                var ts = entry.timestamp ? new Date(entry.timestamp).toLocaleString() : '-';
                html += '<tr><td>' + typeLabels[typ] + ' - ' + metLabels[met]
                    + '</td><td><b>' + vs + '</b></td><td>' + ts + '</td></tr>';
                rowCount++;
            }
        }
    }

    // ── RDPMS indication attrs (NWKR, RWKR, NW-V …) -- direction-agnostic ──
    if (pm && pm.RDPMS) {
        for (var rk2 in pm.RDPMS) {
            if (!pm.RDPMS.hasOwnProperty(rk2)) continue;
            var rd = pm.RDPMS[rk2];
            var aid2 = parseInt((asset.attrs[rk2] && (asset.attrs[rk2].AttrId || asset.attrs[rk2].AssetAttributeId)) || 0);
            var dn2 = (typeof resolvePmAttrDisplayName === 'function')
                ? resolvePmAttrDisplayName(rk2, aid2) : rk2;
            var v2 = parseFloat(rd.value);
            var vs2 = isNaN(v2) ? '\u2014' : v2.toFixed(2);
            var ts2 = rd.timestamp ? new Date(rd.timestamp).toLocaleString() : '-';
            html += '<tr><td>' + dn2 + '</td><td><b>' + vs2 + '</b></td><td>' + ts2 + '</td></tr>';
            rowCount++;
        }
    }

    if (rowCount === 0) {
        html += '<tr><td colspan="3" style="text-align:center;color:#94a3b8;font-style:italic;">No data available</td></tr>';
    }

    html += '</tbody></table>';

    // ── DataLogger Relays ──
    if (asset.dlRelays && Object.keys(asset.dlRelays).length > 0) {
        html += '<h6 style="color:#7c3aed;margin-top:12px;">DataLogger Relays</h6>';
        html += '<table class="table table-bordered table-sm">'
            + '<thead><tr><th>Relay</th><th>Value</th><th>Status</th><th>Last Updated</th></tr></thead><tbody>';
        var rKeys = Object.keys(asset.dlRelays);
        rKeys.sort(function (a, b) {
            var ra = asset.dlRelays[a], rb = asset.dlRelays[b];
            if (ra.isPickup && !rb.isPickup) return -1;
            if (!ra.isPickup && rb.isPickup) return 1;
            return 0;
        });
        for (var ri = 0; ri < rKeys.length; ri++) {
            var rl = asset.dlRelays[rKeys[ri]];
            var sb = rl.isPickup
                ? '<span class="rdpms-dl-badge pickup shine-button" style="min-width:60px;text-align:center;">Pickup</span>'
                : '<span class="rdpms-dl-badge drop shine-button" style="min-width:60px;text-align:center;">Drop</span>';
            var t3 = rl.timestamp ? new Date(rl.timestamp).toLocaleString() : '-';
            html += '<tr><td>' + (rl.displayName || rKeys[ri]) + '</td><td>' + rl.value
                + '</td><td>' + sb + '</td><td>' + t3 + '</td></tr>';
        }
        html += '</tbody></table>';
    }

    html += '</div>';
    $('#model-div-alert').empty().append(html);
    $('#modalalertlogs').modal('show');
}
function fnGetEventLog(assetId, siteId) {
    // Store for use in the partial view
    window.currentEventLogAssetId = assetId;
    window.currentEventLogSiteId = siteId || $('#drpSite').val();

    $("#loader").show();

    // Call GetEventLog POST action with assetId
    $.ajax({
        url: '/FRS25/Telemetry/GetEventLog',
        type: 'POST',
        data: { assetId: assetId },  // Controller expects: int assetId
        success: function (data) {
            $("#loader").hide();
            $("#model-div-alert").empty().append(data);

            // Show modal with proper Bootstrap handling
            var modalEl = document.getElementById('modalalertlogs');

            try {
                // Bootstrap 5 approach
                if (typeof bootstrap !== 'undefined' &&
                    bootstrap.Modal &&
                    typeof bootstrap.Modal.getInstance === 'function') {
                    var existingModal = bootstrap.Modal.getInstance(modalEl);
                    if (existingModal) {
                        existingModal.dispose();
                    }
                    var modal = new bootstrap.Modal(modalEl, {
                        backdrop: 'static',
                        keyboard: true
                    });
                    modal.show();
                } else if (typeof $ !== 'undefined' && $.fn.modal) {
                    // Bootstrap 4 fallback
                    $('#modalalertlogs').modal({
                        backdrop: 'static',
                        keyboard: true
                    }).modal('show');
                }
            } catch (e) {
                console.warn('Modal init error, using jQuery fallback:', e);
                if (typeof $ !== 'undefined' && $.fn.modal) {
                    $('#modalalertlogs').modal('show');
                }
            }

            // Initialize controls after modal loads
            setTimeout(function () {
                initEventLogControls();
            }, 400);
        },
        error: function (xhr, status, error) {
            $("#loader").hide();
            console.error("[GetEventLog] Error:", error);
            console.error("[GetEventLog] Response:", xhr.responseText);
            if (typeof showError === 'function') {
                showError("Failed to load Event Log", "Error");
            } else {
                alert("Failed to load Event Log: " + error);
            }
        }
    });
}

// ================================================================
// REPLACE the existing initEventLogControls function with this:
// ================================================================
function initEventLogControls() {
    console.log('[EventLog Main] Initializing controls...');

    // The partial view (_EventLog.cshtml) handles its own initialization
    // This is a backup trigger in case the partial's script doesn't run

    setTimeout(function () {
        // Check if partial's init function exists and call it
        if (typeof initializeEventLogControls === 'function') {
            initializeEventLogControls();
        } else {
            // Manual fallback initialization
            initEventLogControlsManual();
        }
    }, 300);
}
function initEventLogControlsManual() {
    console.log('[EventLog] Manual initialization...');

    // Initialize Select2 as SINGLE SELECT
    var $attrSelect = $('#drpDataloggerSearchAttribute');
    if ($attrSelect.length && typeof $.fn.select2 !== 'undefined') {
        if ($attrSelect.hasClass('select2-hidden-accessible')) {
            try { $attrSelect.select2('destroy'); } catch (e) { }
        }

        // Remove multiple attribute if present
        $attrSelect.removeAttr('multiple');

        $attrSelect.select2({
            dropdownParent: $('#modalalertlogs .modal-content'),
            placeholder: 'Select Attribute',
            allowClear: true,
            width: '100%'
        });
    }

    // Initialize Datepickers
    var today = new Date();
    var todayFormatted = today.getDate() + '/' + (today.getMonth() + 1) + '/' + today.getFullYear();

    var $dlDate = $('#txtDataloggerSearchDate');
    if ($dlDate.length && typeof $.fn.datepicker !== 'undefined') {
        try {
            $dlDate.datepicker({
                dateFormat: 'd/m/yy',
                maxDate: new Date(),
                beforeShow: function (input, inst) {
                    setTimeout(function () {
                        inst.dpDiv.css({ 'z-index': 999999 });
                    }, 10);
                }
            });
            $dlDate.datepicker('setDate', today);
        } catch (e) {
            $dlDate.val(todayFormatted);
        }
    }

    var $peDate = $('#txtPointEventSearchDate');
    if ($peDate.length && typeof $.fn.datepicker !== 'undefined') {
        try {
            $peDate.datepicker({
                dateFormat: 'd/m/yy',
                maxDate: new Date(),
                beforeShow: function (input, inst) {
                    setTimeout(function () {
                        inst.dpDiv.css({ 'z-index': 999999 });
                    }, 10);
                }
            });
            $peDate.datepicker('setDate', today);
        } catch (e) {
            $peDate.val(todayFormatted);
        }
    }

    // Setup tab handlers
    initEventLogTabs();
}

// ================================================================
// REPLACE the existing initEventLogTabs function with this:
// ================================================================
function initEventLogTabs() {
    // Remove existing handlers
    $('#modalalertlogs').off('click.eventLogTabs');

    // Add tab click handler
    $('#modalalertlogs').on('click.eventLogTabs', '.nav-tabs .nav-link', function (e) {
        e.preventDefault();
        var $this = $(this);
        var target = $this.attr('href') || $this.data('bs-target') || $this.data('target');

        console.log('[EventLog] Tab clicked:', target);

        // Remove active from all tabs
        $this.closest('.nav-tabs').find('.nav-link').removeClass('active');

        // Find tab content
        var $tabContent = $this.closest('.eventlog-container, .modal-body').find('.tab-content');
        $tabContent.find('.tab-pane').removeClass('show active');

        // Activate clicked tab
        $this.addClass('active');
        $(target).addClass('show active');

        // Re-init Select2 on Datalogger tab
        if (target === '#tabDatalogger') {
            setTimeout(function () {
                var $sel = $('#drpDataloggerSearchAttribute');
                if ($sel.length && !$sel.hasClass('select2-hidden-accessible') && typeof $.fn.select2 !== 'undefined') {
                    $sel.removeAttr('multiple');
                    $sel.select2({
                        dropdownParent: $('#modalalertlogs .modal-content'),
                        placeholder: 'Select Attribute',
                        allowClear: true,
                        width: '100%'
                    });
                }
            }, 100);
        }
    });
}
function closeEventLogModal() {
    var modalEl = document.getElementById('modalalertlogs');
    if (!modalEl) return;

    try {
        // Bootstrap 5
        if (typeof bootstrap !== 'undefined' &&
            bootstrap.Modal &&
            typeof bootstrap.Modal.getInstance === 'function') {
            var modalInstance = bootstrap.Modal.getInstance(modalEl);
            if (modalInstance) {
                modalInstance.hide();
                return;
            }
        }
    } catch (e) {
        console.warn('Bootstrap 5 modal close failed:', e);
    }

    // jQuery Bootstrap fallback
    if (typeof $ !== 'undefined' && $.fn.modal) {
        $('#modalalertlogs').modal('hide');
        return;
    }

    // Manual fallback
    modalEl.classList.remove('show');
    modalEl.style.display = 'none';
    document.body.classList.remove('modal-open');
    var backdrops = document.querySelectorAll('.modal-backdrop');
    backdrops.forEach(function (b) { b.remove(); });
}
function reinitDataloggerControls() {
    var $attrSelect = $('#drpDataloggerSearchAttribute');
    if ($attrSelect.length && typeof $.fn.select2 !== 'undefined') {
        if (!$attrSelect.hasClass('select2-hidden-accessible')) {
            try {
                $attrSelect.select2({
                    dropdownParent: $('#modalalertlogs .modal-content'),
                    placeholder: 'Select Attributes',
                    allowClear: true,
                    width: '100%',
                    closeOnSelect: false
                });
            } catch (e) {
                console.warn('[EventLog] Select2 reinit error:', e);
            }
        }
    }

    // Ensure date has a value
    var $dlDate = $('#txtDataloggerSearchDate');
    if ($dlDate.length && !$dlDate.val()) {
        var today = new Date();
        var todayFormatted = today.getDate() + '/' + (today.getMonth() + 1) + '/' + today.getFullYear();
        $dlDate.val(todayFormatted);
    }
}
if (typeof window.fnSearchDatalogger !== 'function') {
    window.fnSearchDatalogger = function () {
        var searchDate = $('#txtDataloggerSearchDate').val();
        var selectedAttr = $('#drpDataloggerSearchAttribute').val();

        if (!searchDate || searchDate.trim() === '') {
            showWarning('Please select a date', 'Validation');
            return;
        }

        if (!selectedAttr || selectedAttr === '') {
            showWarning('Please select an attribute', 'Validation');
            return;
        }

        var assetId = window.currentEventLogAssetId;
        var siteId = window.currentEventLogSiteId || $('#drpSite').val();

        if (!assetId) {
            showWarning('Asset ID not found', 'Error');
            return;
        }

        console.log('[Datalogger] Searching:', { date: searchDate, attr: selectedAttr, assetId: assetId });

        $('#divSearchDataloggerEvent').html('<div class="text-center p-4"><i class="fas fa-spinner fa-spin fa-2x"></i><p class="mt-2">Loading...</p></div>');

        $.ajax({
            url: '/FRS25/Telemetry/_EventDataLoggerGraph',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                AssetId: assetId,
                SiteId: siteId,
                SearchDate: searchDate,
                SrNumbers: [selectedAttr]
            }),
            success: function (data) {
                if (data) {
                    $('#divSearchDataloggerEvent').html(data);
                } else {
                    $('#divSearchDataloggerEvent').html('<div class="eventlog-empty"><i class="fas fa-inbox"></i><p>No data found</p></div>');
                }
            },
            error: function () {
                $('#divSearchDataloggerEvent').html('<div class="text-center text-danger p-4"><i class="fas fa-exclamation-circle fa-2x"></i><p class="mt-2">Failed to load data</p></div>');
                showError('Failed to load data', 'Error');
            }
        });
    };
}
if (typeof window.fnSearchPointEvent !== 'function') {
    window.fnSearchPointEvent = function () {
        var searchDate = $('#txtPointEventSearchDate').val();

        if (!searchDate || searchDate.trim() === '') {
            showWarning('Please select a date', 'Validation');
            return;
        }

        var assetId = window.currentEventLogAssetId;
        var siteId = window.currentEventLogSiteId || $('#drpSite').val();

        if (!assetId) {
            showWarning('Asset ID not found', 'Error');
            return;
        }

        $('#divSearchPointEvent').html('<div class="text-center p-4"><i class="fas fa-spinner fa-spin fa-2x"></i><p class="mt-2">Loading...</p></div>');

        $.ajax({
            url: '/FRS25/Telemetry/_PointMachineEvent',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                AssetId: assetId,
                SiteId: siteId,
                SearchDate: searchDate
            }),
            success: function (data) {
                if (data) {
                    $('#divSearchPointEvent').html(data);
                } else {
                    $('#divSearchPointEvent').html('<div class="eventlog-empty"><i class="fas fa-inbox"></i><p>No data found</p></div>');
                }
            },
            error: function () {
                $('#divSearchPointEvent').html('<div class="text-center text-danger p-4"><i class="fas fa-exclamation-circle fa-2x"></i><p class="mt-2">Failed to load data</p></div>');
                showError('Failed to load data', 'Error');
            }
        });
    };
}
if (typeof fnUnderMaintenance !== 'function') { function fnUnderMaintenance(assetId, element) { $.ajax({ url: '/FRS25/Telemetry/UnderMaintenance', type: 'POST', data: JSON.stringify({ assetId: assetId }), contentType: 'application/json', success: function (data) { if (data && data.Success) { var $icon = $(element); if ($icon.hasClass('fa-bell')) { $icon.removeClass('fa-bell').addClass('fa-bell-slash'); showSuccess('Maintenance mode enabled', 'Maintenance'); } else { $icon.removeClass('fa-bell-slash').addClass('fa-bell'); showSuccess('Maintenance mode disabled', 'Maintenance'); } } }, error: function () { showError('Failed to update maintenance status', 'Error'); } }); } }

// ===== DATALOGGER PROCESSING =====
function processWsDataloggerAttr(assetId, assetName, attrName, value, timestamp) {
    // FIX-2: Variables must be declared BEFORE use. The original code had
    // storeKey/numVal/isPickup/displayName referenced on lines above their
    // declaration — JS hoisting made them 'undefined', so dlRelays entries
    // were stored with value:undefined on the first write, then silently
    // overwritten by the second (minified) block. If wsLiveData[assetId]
    // didn't exist, the first write would also throw a TypeError.
    if (!wsLiveData[assetId]) return;
    var numVal = parseFloat(value);
    var isPickup = (numVal === 1);
    var displayName = attrName;
    if (assetName && displayName.indexOf(assetName) === 0) {
        displayName = displayName.substring(assetName.length).trim();
    }
    if (!displayName) displayName = attrName;
    if (!wsLiveData[assetId].dlRelays) wsLiveData[assetId].dlRelays = {};

    // Store under displayName key so graph panel dlKey and
    // knownRelayDisplayNames lookup use the same consistent name.
    var storeKey = displayName || attrName;
    var relayObj = { value: numVal, isPickup: isPickup, displayName: displayName, attrName: attrName, timestamp: timestamp || new Date().toISOString() };
    wsLiveData[assetId].dlRelays[storeKey] = relayObj;
    // Keep attrName as alias if different, so old lookups still work.
    if (storeKey !== attrName) { wsLiveData[assetId].dlRelays[attrName] = relayObj; }
    wsLiveData[assetId].lastUpdated = new Date();
    // Update Track table DataLogger column if visible
    var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
    if ($row.length) { var $dlCell = $row.find('td.dl-cell'); if ($dlCell.length) { var dlRelays = wsLiveData[assetId].dlRelays || {}; var dlHtml = ''; var dlKeys = Object.keys(dlRelays); dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; }); if (dlKeys.length > 0) { for (var di = 0; di < dlKeys.length; di++) { var relay = dlRelays[dlKeys[di]]; var badgeClass = relay.isPickup ? 'pickup' : 'drop'; var badgeText = relay.isPickup ? 'Pickup' : 'Drop'; dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> '; } } else { dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>'; } $dlCell.html(dlHtml); } }
    updateMainSignalLights(assetId);
    renderDataloggerBadgesForAsset(assetId);
}
// Make renderDataloggerBadgesForAsset global so it can be called from processWsDataloggerAttr
function renderDataloggerBadgesForAsset(assetId) { var asset = wsLiveData[assetId]; if (!asset || !asset.dlRelays) return; var relayKeys = Object.keys(asset.dlRelays); if (relayKeys.length === 0) return; relayKeys.sort(function (a, b) { var ra = asset.dlRelays[a], rb = asset.dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return (ra.displayName || a).localeCompare(rb.displayName || b); }); var $section = $('#rdpmsDL_' + assetId); if (!$section.length) { var $cardBody = $('#rdpmsCard_' + assetId + ' .card-body-signal'); if (!$cardBody.length) return; $cardBody.append(getDataloggerSectionHTML(assetId)); $section = $('#rdpmsDL_' + assetId); if (!$section.length) return; } var maxInline = 2; var html = ''; for (var i = 0; i < relayKeys.length && i < maxInline; i++) { var relay = asset.dlRelays[relayKeys[i]]; var badgeClass = relay.isPickup ? 'pickup' : 'drop'; var badgeText = relay.isPickup ? 'Pickup' : 'Drop'; html += '<div class="rdpms-dl-item"><h6>' + (relay.displayName || relayKeys[i]) + '</h6><span class="rdpms-dl-badge ' + badgeClass + ' shine-button">' + badgeText + '</span></div>'; } var moreCount = relayKeys.length - maxInline; if (moreCount > 0) { html += '<span id="rdpmsDLToggle_' + assetId + '" onclick="toggleDlMore(\'' + assetId + '\')" style="color:#38bdf8;font-size:10px;cursor:pointer;white-space:nowrap;">+ ' + moreCount + ' more</span>'; } html += '<span class="rdpms-dl-details-btn" onclick="fnShowDataLoggerEvent(\'' + assetId + '\')">Details</span>'; if (moreCount > 0) { html += '<div id="rdpmsDLMore_' + assetId + '" style="display:none;width:100%;">'; for (var j = maxInline; j < relayKeys.length; j++) { var relay = asset.dlRelays[relayKeys[j]]; var badgeClass = relay.isPickup ? 'pickup' : 'drop'; var badgeText = relay.isPickup ? 'Pickup' : 'Drop'; html += '<div class="rdpms-dl-item"><h6>' + (relay.displayName || relayKeys[j]) + '</h6><span class="rdpms-dl-badge ' + badgeClass + ' shine-button">' + badgeText + '</span></div>'; } html += '</div>'; } $section.html(html); $section.closest('.rdpms-datalogger-section').show(); }
// Expose globally for callers that use window.renderDataloggerBadgesForAsset(...)
window.renderDataloggerBadgesForAsset = renderDataloggerBadgesForAsset;
function toggleDlMore(assetId) { var $overflow = $('#rdpmsDLMore_' + assetId); var $toggle = $('#rdpmsDLToggle_' + assetId); if (!$overflow.length) return; if ($overflow.is(':visible')) { $overflow.slideUp(200); $toggle.text('+ ' + $overflow.find('.rdpms-dl-item').length + ' more'); } else { $overflow.slideDown(200); $toggle.text('- less'); } }
function getDataloggerSectionHTML(assetId) { return '<div class="rdpms-datalogger-section" style="display:none;"><div class="rdpms-dl-row" id="rdpmsDL_' + assetId + '"><span style="color:#94a3b8;font-size:10px;">Waiting for relay data...</span></div></div>'; }

// ===== DOCUMENT READY =====
// Store initial dropdown options on page load (for restoring when "All" is selected)
var initialDivisionOptions = '';
var initialSiteOptions = '';
var initialAssetTypeOptions = '';

$(document).ready(function () {
    // Store initial Division, Site, and Asset Type dropdown options
    initialDivisionOptions = $('#drpDivisions').html();
    initialSiteOptions = $('#drpSite').html();
    initialAssetTypeOptions = $('#drpAssetType').html();

    // Initialize view mode and visibility on page load
    updateViewTypeOptions();

    // Initial view type restriction check on page load
    setTimeout(function () {
        updateViewTypeRestrictions();
    }, 500);

    // View type change - optimized for smooth switching
    $('#drpView').on('change', function () {
        var newViewType = $(this).val();
        // Handle view switch to preserve data during transition
        if (typeof handleViewSwitch === 'function') {
            handleViewSwitch(newViewType);
        }
        updateViewMode();
    });

    // Multi-select dropdown toggle
    $(document).on('click', '.select-btn', function (e) {
        e.stopPropagation();
        var targetId = $(this).data('target');
        var $dd = $('#' + targetId);
        var $btn = $(this);
        if ($dd.hasClass('open')) { $dd.removeClass('open'); $btn.removeClass('active'); $('#backdrop').removeClass('show'); }
        else {
            var r = $btn[0].getBoundingClientRect();
            $dd.css({ top: r.bottom + 4, left: r.left, minWidth: r.width }).addClass('open');
            $btn.addClass('active'); $('#backdrop').addClass('show');
        }
    });
    $('#backdrop').on('click', function () {
        $('.filter-dropdown').removeClass('open');
        $('.select-btn').removeClass('active');
        $(this).removeClass('show');
        // Check view type restrictions after dropdown closes
        setTimeout(function () {
            updateViewTypeRestrictions();
        }, 100);
    });



    // ── Asset Number: Select All header (checkbox, label, or row background) ──
    $(document).on('click', '#ddAssetNumber .dropdown-header', function (e) {
        e.stopPropagation();
        var $cb = $('#chkAllAssetNumber');
        if ($(e.target).is('input[type="checkbox"]') || $(e.target).is('label')) {
            setTimeout(function () {
                selectAllAssets($cb.prop('checked'));
            }, 0);
        } else {
            var newState = !$cb.prop('checked');
            $cb.prop('checked', newState);
            selectAllAssets(newState);
        }
    });

    // ── Asset Number: individual item row click ──
    $(document).on('click', '#listAssetNumber .dropdown-item', function (e) {
        e.stopPropagation();
        var $cb = $(this).find('input[type="checkbox"]');
        if (!$(e.target).is('input[type="checkbox"]')) {
            $cb.prop('checked', !$cb.prop('checked'));
        }
        $(this).toggleClass('checked', $cb.prop('checked'));
        updateSelectAllState();
        updateAssetText();
    });

    // Cascade: Zone → Division → Site → Asset Type → Assets
    $('#drpZones').on('change', function () {
        var zoneId = $(this).val();
        if (zoneId && zoneId !== '' && zoneId !== '0') {
            GetDivisionByZone(zoneId);
        } else {
            // Zone set to "All" - restore initial Division, Site, and Asset Type options
            $('#drpDivisions').html(initialDivisionOptions);
            $('#drpSite').html(initialSiteOptions);
            if (initialAssetTypeOptions) {
                $('#drpAssetType').html(initialAssetTypeOptions);
            }
            $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
            $('#txtAssetNumber').text('All');
            $("#drpAsset").empty().append('<option value="">All</option>');
            updateViewTypeRestrictions();
        }
    });
    $('#drpDivisions').on('change', function () {
        var divId = $(this).val();
        if (divId && divId !== '' && divId !== '0') {
            GetByDivisionId(divId);
        } else {
            // Division set to "All" - restore initial Site and Asset Type options
            $('#drpSite').html(initialSiteOptions);
            if (initialAssetTypeOptions) {
                $('#drpAssetType').html(initialAssetTypeOptions);
            }
            $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
            $('#txtAssetNumber').text('All');
            $("#drpAsset").empty().append('<option value="">All</option>');
            updateViewTypeRestrictions();
        }
    });
    $('#drpSite').on('change', function () {
        invalidateBulkMetadataCache();
        var siteId = $(this).val();

        // Reset search guard — user must Search again after changing site
        window._tlSearchInitiated = false;

        // When site is selected, load asset types for that site
        if (siteId && siteId !== '' && siteId !== '0') {
            GetAssetTypeBySiteId(siteId);
        } else {
            // Site set to "All" - restore initial asset type options
            if (initialAssetTypeOptions) {
                $('#drpAssetType').html(initialAssetTypeOptions);
            }
            $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
            $('#txtAssetNumber').text('All');
            $("#drpAsset").empty().append('<option value="">All</option>');
            updateViewTypeRestrictions();
        }
    });

    // No auto-cascade on page load - all dropdowns show "All" by default

    //$('#drpAssetType').on('change', function () {
    //    var siteId = $('#drpSite').val();
    //    var assetTypeId = $(this).val();
    //    if (siteId && siteId !== '' && siteId !== '0' && assetTypeId && assetTypeId !== '' && assetTypeId !== '0') {
    //        GetByAssest(siteId, assetTypeId);
    //        loadAssetNumbersMulti(siteId, assetTypeId);
    //    } else {
    //        $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
    //        $('#txtAssetNumber').text('All');
    //    }
    //    // Update view dropdown options without overriding a compatible user-chosen view
    //    updateViewTypeOptions();
    //    // Also clear stale state from previous asset type so new type starts fresh
    //    wsAttributeNames = [];
    //    wsAttributeOrder = {};
    //    wsCurrentColumns = [];
    //    wsTableInitialized = false;
    //    window.pmTableMode = false;
    //    // Reset the Asset Number selection so previously-picked IDs from a
    //    // different asset type don't filter out everything in the new type.
    //    window.wsCurrentFilterAssetIds = [];
    //    $('#chkAllAssetNumber').prop('checked', false);
    //    $('#txtAssetNumber').text('All');
    //    // Auto-trigger search when Signal, Point, or IPS type is selected and site is available
    //    if ((isSignalAssetType() || isPointAssetType() || isIpsAssetType()) && siteId && siteId !== '0' && siteId !== '') {
    //        setTimeout(function () { fnSearchView(); }, 300);
    //    }
    //    // Make sure the SIP bridge is (re)installed for this site so the SIP view
    //    // continues to receive live WebSocket batches across asset-type changes.
    //    // SipTelemetry.installBridge is idempotent.
    //    if (window.SipTelemetry) {
    //        try {
    //            if (typeof window.SipTelemetry.installBridge === 'function') {
    //                window.SipTelemetry.installBridge();
    //            }
    //            // If layout wasn't loaded for this site yet (e.g. site change preceded
    //            // sip-telemetry.js initialisation), kick it off now.
    //            if (siteId && siteId !== '0' && window.SipTelemetry._state &&
    //                !window.SipTelemetry._state.siteId &&
    //                typeof window.SipTelemetry.connectToSite === 'function') {
    //                window.SipTelemetry.connectToSite(siteId);
    //            }
    //        } catch (e) { console.warn('[SIP-WS] asset-type bridge refresh failed', e); }
    //    }
    //});

    $('#drpAssetType').on('change', function () {
        invalidateBulkMetadataCache();
        $('#atCardView').empty().html('');
        // Aggressive cleanup: remove ALL renderer-specific containers and
        // any other-asset-type artifacts so the new asset type starts from
        // a clean slate. This prevents stale Signal cards from showing up
        // when the user switches to PM, etc.
        $('#pmMainContainer, #rdpmsMainContainer, #rdpmsShuntContainer, ' +
            '.rdpms-container, #ipsGridContainer, .ips-grid-container, ' +
            '#trackCardContainer, #wsLiveTable').remove();
        // Show a clear "Click Search" prompt instead of an empty card container.
        // Before user clicks Search, the data area should be empty/placeholder
        // — NOT show what looks like an empty card view.
        $('#divTelemetryLive').empty().html(
            '<div class="text-center p-5" style="color:var(--text-3);">' +
            '<i class="fas fa-search fa-2x" style="display:block;margin-bottom:10px;opacity:0.6;"></i>' +
            '<div>Select Asset Numbers and click <b>Search</b> to view live data.</div>' +
            '</div>'
        );
        clearSignalAssetInfoCache();
        var siteId = $('#drpSite').val();
        var assetTypeId = $(this).val();

        // ---- RESET ALL STATE ----
        wsAttributeNames = [];
        wsAttributeOrder = {};
        wsCurrentColumns = [];
        wsTableInitialized = false;
        window.pmTableMode = false;
        wsLiveData = {};
        wsUpdatedAssets = {};
        wsStaleAttrs = {};
        _assetInfoListCache = {};
        _signalGroupCache = {};
        rdpmsCardsBuilt = {};
        pmCardsBuilt = {};
        _sigDomCache = {};
        _rdpmsDomCache = {};
        // Clear asset number selection from previous type
        window.wsCurrentFilterAssetIds = [];
        $('#txtAssetNumber').text('All');
        $('#chkAllAssetNumber').prop('checked', false);
        // Reset search guard — user must Search again after changing type
        window._tlSearchInitiated = false;
        // -----
        disconnectWebSocket();
        if (siteId && siteId !== '' && siteId !== '0' && assetTypeId && assetTypeId !== '' && assetTypeId !== '0') {
            GetByAssest(siteId, assetTypeId);
            loadAssetNumbersMulti(siteId, assetTypeId);
        } else {
            $('#listAssetNumber').html('<div class="dropdown-empty">Select Station & Asset Type first</div>');
            $('#txtAssetNumber').text('All');
        }
        updateViewTypeOptions();
        // DO NOT auto-trigger search — user must click Search button
        // after selecting asset numbers. This prevents premature data
        // loading before the user has made their selection.
        // if ((isSignalAssetType() || isPointAssetType() || isIpsAssetType()) && siteId && siteId !== '0' && siteId !== '') {
        //     setTimeout(function () { fnSearchView(); }, 300);
        // }
        // Ensure SIP bridge reinstalled
        if (window.SipTelemetry) {
            try {
                if (typeof window.SipTelemetry.installBridge === 'function') window.SipTelemetry.installBridge();
                if (siteId && siteId !== '0' && window.SipTelemetry._state && !window.SipTelemetry._state.siteId &&
                    typeof window.SipTelemetry.connectToSite === 'function') {
                    window.SipTelemetry.connectToSite(siteId);
                }
            } catch (e) { console.warn('[SIP-WS] asset-type bridge refresh failed', e); }
        }
    });

    // ================================================================
    // AT: CARD VIEW RENDERER
    // Renders wsLiveData assets as glass cards (Track / Signal / PM)
    // ================================================================
    function atRenderCards() {
        if (typeof fnBindTrackCards === 'function') {
            fnBindTrackCards();
        } else {
            var $wrap = $('#atCardView');
            var assetIds = Object.keys(wsLiveData);
            // Defense-in-depth: drop assets not in the bulk response.
            if (typeof isAssetInBulkWhitelist === 'function') {
                assetIds = assetIds.filter(function (id) { return isAssetInBulkWhitelist(id); });
            }
            // Apply user's Asset Number multi-select filter, if any.
            if (typeof window._sipFilteredAssetIds === 'function') {
                assetIds = window._sipFilteredAssetIds(assetIds);
            }
            if (assetIds.length === 0) {
                $wrap.html('<div style="padding:40px;text-align:center;color:var(--text-3);font-family:Manrope,sans-serif;"><i class="fas fa-satellite-dish" style="font-size:28px;display:block;margin-bottom:10px;opacity:.4;"></i>No data yet -- press Search first</div>');
                return;
            }
            assetIds.sort(function (a, b) { return (wsLiveData[a].AssetName || '').localeCompare(wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }); });
            assetIds = blankDataLast(assetIds); // FIX-1: moved OUTSIDE loop (was inside, re-sorting every iteration)
            var atId = parseInt(wsCurrentAssetTypeId || 0);
            var html = '<div class="at-cards-grid">';
            for (var i = 0; i < assetIds.length; i++) {
                var aid = assetIds[i];
                if (atId === 2) html += atBuildSignalCard(aid);
                else if (atId === 3) html += atBuildPmCard(aid);
                else html += atBuildTrackCard(aid);
            }
            html += '</div>';
            $wrap.html(html);
        }
    }

    function atFmtVal(v, dec) {
        if (v === null || v === undefined || v === '') return '-';
        var n = parseFloat(v);
        if (isNaN(n)) return v;
        return n.toFixed(dec !== undefined ? dec : 2);
    }

    var _atCurrentView = $('#drpView').val() || 'Table';


    function _atShowCards(show) {
        var $cv = $('#atCardView');
        var $ts = $('.at-table-scroll');
        if (show) {
            $cv.removeClass('at-force-hidden').addClass('at-force-shown');
            $ts.removeClass('at-force-shown').addClass('at-force-hidden');
        } else {
            $cv.removeClass('at-force-shown').addClass('at-force-hidden');
            $ts.removeClass('at-force-hidden').addClass('at-force-shown');
        }
    }

    // ── Update atSwitchView to handle Cards (single authoritative definition) ──
    window.atSwitchView = function (viewName, el) {
        // Always sync tab highlight
        document.querySelectorAll('#atViewTabBar .at-tab-btn').forEach(function (b) {
            b.classList.toggle('active', b === el);
        });

        // Scroll-table-mode: only constrain height for Table view (actual tabular data)
        var scrollEl = document.querySelector('.at-table-scroll');
        if (scrollEl) {
            scrollEl.classList.toggle('scroll-table-mode', viewName === 'Table');
        }

        var prevView = _atCurrentView;
        var viewChanged = (prevView !== viewName);
        _atCurrentView = viewName;

        if (viewName === 'Cards') {
            // ── Going to Cards ──
            // Empty divTelemetryLive so the table content can't bleed through, and
            // reset DOM-bound caches so the next non-Cards view rebuilds cleanly.
            if (viewChanged) {
                var tlEl = document.getElementById('divTelemetryLive');
                if (tlEl) {
                    tlEl.innerHTML = '<div id="trackCardContainer" class="sites-cards" style="display:none;"></div>';
                }
                if (typeof rdpmsCardsBuilt !== 'undefined') rdpmsCardsBuilt = {};
                if (typeof pmCardsBuilt !== 'undefined') pmCardsBuilt = {};
                if (typeof _sigDomCache !== 'undefined') _sigDomCache = {};
                if (typeof _rdpmsDomCache !== 'undefined') _rdpmsDomCache = {};
                if (typeof wsTableInitialized !== 'undefined') wsTableInitialized = false;
            }
            $('.at-table-card').show();
            _atShowCards(true);  // force atCardView shown, table-scroll hidden
            if (Object.keys(wsLiveData).length > 0) $('#atKpiRow').show();
            $('#trackCardContainer').hide();
            atRenderCards();
            return;
        }

        // ── FRS Advanced (SIP) ─────────────────────────────────────
        // Opens the Station Interlocking Plan in fullscreen using the
        // existing SIP renderer. Layout comes from /Telemetry/GetSipView,
        // and live colours come from wsLiveData via the existing
        // refreshSipView hook. We DO NOT trigger fnSearchView() here --
        // the WebSocket is whatever the user already had streaming, and the
        // SIP just observes wsLiveData. (If the user wants more assets
        // colored, they should pick "All" asset type and Search first.)
        if (viewName === 'FrsAdvanced') {
            var siteIdSel = $('#drpSite').val();
            if (!siteIdSel || String(siteIdSel) === '0' || siteIdSel === '') {
                if (typeof showWarning === 'function') {
                    showWarning('Please select a site first to view the SIP.', 'Site required');
                } else {
                    alert('Please select a site first to view the SIP.');
                }
                // Restore previous active tab (don't leave FRS Advanced highlighted)
                var prevBtn = document.querySelector('#atViewTabBar .at-tab-btn[data-view="' + (prevView || 'Table') + '"]');
                document.querySelectorAll('#atViewTabBar .at-tab-btn').forEach(function (b) {
                    b.classList.toggle('active', b === prevBtn);
                });
                _atCurrentView = prevView;
                return;
            }

            // Stash the previous view so we can restore it when fullscreen closes.
            // Use a closure-bound variable rather than a window global so concurrent
            // clicks don't clobber each other.
            var _frsRestoreView = (prevView && prevView !== 'FrsAdvanced') ? prevView : 'Table';

            // Ensure latest layout for current site (loadSipLayout no-ops if same).
            // The SIP renderer auto-redraws every 3s and on every WS batch, so once
            // loaded, live updates flow naturally.
            if (typeof window.loadSipLayout === 'function') {
                try { window.loadSipLayout(siteIdSel); } catch (e) { console.warn('[FRS] loadSipLayout', e); }
            }
            if (typeof window.sipRefresh === 'function') {
                try { window.sipRefresh(); } catch (e) { /* ignore */ }
            }

            // Open existing SIP fullscreen modal (sets .fullscreen on .sip-card,
            // body overflow hidden, and triggers refreshSipView after 60ms).
            var $sipBtn = $('#sipFullscreenBtn');
            var $sipCard = $('.sip-card');
            if ($sipBtn.length && !$sipCard.hasClass('fullscreen')) {
                $sipBtn.trigger('click');
            } else if (!$sipBtn.length) {
                // Fallback: SIP fullscreen button isn't in the DOM (rare).
                // Just add the class manually so the SIP expands.
                $sipCard.addClass('fullscreen');
                $('body').css('overflow', 'hidden');
                if (typeof window.sipRefresh === 'function') {
                    setTimeout(window.sipRefresh, 60);
                }
            }

            // Watch for fullscreen exit (ESC key, or user clicking the close
            // button). When .fullscreen is removed from .sip-card, restore the
            // previous tab's active state and trigger that view.
            var sipCardEl = $sipCard.get(0);
            if (sipCardEl && !sipCardEl._frsExitObserver) {
                sipCardEl._frsExitObserver = new MutationObserver(function () {
                    if (!sipCardEl.classList.contains('fullscreen') && _atCurrentView === 'FrsAdvanced') {
                        // Fullscreen was just exited while we were on FRS Advanced.
                        var restoreBtn = document.querySelector(
                            '#atViewTabBar .at-tab-btn[data-view="' + _frsRestoreView + '"]'
                        );
                        if (restoreBtn) {
                            restoreBtn.click();
                        } else {
                            // Fallback: restore Table
                            var tableBtn = document.querySelector('#atViewTabBar .at-tab-btn[data-view="Table"]');
                            if (tableBtn) tableBtn.click();
                        }
                    }
                });
                sipCardEl._frsExitObserver.observe(sipCardEl, { attributes: true, attributeFilter: ['class'] });
            }

            return;
        }

        // ── Going to a non-Cards view ──
        // If we're leaving FRS Advanced, close its fullscreen mode first so the
        // page returns to a normal layout before the new view renders.
        if (prevView === 'FrsAdvanced' && $('.sip-card').hasClass('fullscreen')) {
            $('.sip-card').removeClass('fullscreen');
            $('body').css('overflow', '');
            $('#sipFullscreenBtn i').removeClass('fa-compress').addClass('fa-expand');
        }

        // Empty atCardView so any leftover cards can't peek through, and
        // bulletproof-hide it.
        var cardEl = document.getElementById('atCardView');
        if (cardEl) cardEl.innerHTML = '';
        _atShowCards(false);  // force atCardView hidden, table-scroll shown
        $('.at-table-card').show();
        if (Object.keys(wsLiveData).length > 0) $('#atKpiRow').show();

        // Clear stale DOM from prior view (only when the view actually changes)
        if (viewChanged) {
            var $tl = $('#divTelemetryLive');
            if ($tl.length) {
                $tl.empty();
                $tl.append('<div id="trackCardContainer" class="sites-cards" style="display:none;"></div>');
            }
            // Reset DOM-bound caches so the new view rebuilds against the fresh DOM
            if (typeof rdpmsCardsBuilt !== 'undefined') rdpmsCardsBuilt = {};
            if (typeof pmCardsBuilt !== 'undefined') pmCardsBuilt = {};
            if (typeof _sigDomCache !== 'undefined') _sigDomCache = {};
            if (typeof _rdpmsDomCache !== 'undefined') _rdpmsDomCache = {};
            if (typeof wsTableInitialized !== 'undefined') wsTableInitialized = false;
        }

        // Sync drpView dropdown and trigger search
        var sel = document.getElementById('drpView');
        if (sel && sel.value !== viewName) {
            sel.value = viewName;
            $(sel).trigger('change');
        }
        if (typeof fnSearchView === 'function') {
            fnSearchView();
        }
    };

    // On page load: default is Table view → enable table scroll mode
    $(function () {
        var scrollEl = document.querySelector('.at-table-scroll');
        if (scrollEl) scrollEl.classList.add('scroll-table-mode');
        // Ensure atCardView is hidden and atTableScroll is visible on load
        _atShowCards(false);
        _atCurrentView = 'Table';
    });

    // Cards tab always visible -- no asset-type restriction
    // (atRenderCards handles Track/Signal/PM automatically based on wsCurrentAssetTypeId)
    $(document).on('change', '#drpAssetType', function () {
        // If currently on Cards while asset type changes, switch back to Table
        // so stale cards from the old type aren't shown
        if ($('#atCardView').is(':visible')) {
            atSwitchView('Table', document.querySelector('#atViewTabBar .at-tab-btn[data-view="Table"]'));
        }
    });

    // Live-update cards if Cards view is active
    var _atOrigUpdateWsStats = window.updateWsStats;
    if (typeof _atOrigUpdateWsStats === 'function') {
        window.updateWsStats = function () {
            _atOrigUpdateWsStats.apply(this, arguments);
            // Re-render cards on data update if Cards view is visible
            if ($('#atCardView').is(':visible') && Object.keys(wsLiveData).length > 0) {
                atRenderCards();
            }
        };
    }


    // AT: Tab bar sync -- merged with Cards handler (do NOT reassign window.atSwitchView here;
    // the patched version above already handles Cards + calls fnSearchView for other views)
    // Just wire drpView <-> tab bar two-way sync below.
    $('#drpView').on('change.atTab', function () {
        var val = $(this).val();
        document.querySelectorAll('#atViewTabBar .at-tab-btn').forEach(function (b) {
            b.classList.toggle('active', b.dataset.view === val);
        });
    });
    function _syncTabVis() {
        var opts = {};
        $('#drpView option').each(function () { opts[$(this).val()] = true; });
        $('#atViewTabBar .at-tab-btn').each(function () {
            // Cards tab is mandatory for all asset types -- always show it.
            if ($(this).attr('id') === 'atTabCards') { $(this).show(); return; }
            // FRS Advanced (SIP) is also always visible -- it shows the full
            // station schematic for the current site, not asset-type filtered.
            if ($(this).attr('id') === 'atTabFrsAdvanced') { $(this).show(); return; }
            $(this).toggle(!!opts[$(this).data('view')]);
        });
    }
    _syncTabVis();
    var _drp = document.getElementById('drpView');
    if (_drp) new MutationObserver(_syncTabVis).observe(_drp, { childList: true });

    // Clean up WebSocket on page unload
    $(window).on('beforeunload', function () { disconnectWebSocket(); });
});

// ================================================================
// POINT MACHINE VIEW -- REDESIGNED TO MATCH SCREENSHOT
// Direction A (AC/AV = A-end) | Direction B (BC/BV = B-end)
// ================================================================

var PM_BASE_CODES = {
    'Normal': { 'AC': 1000, 'AV': 2000, 'BC': 3000, 'BV': 4000 },
    'Reverse': { 'AC': 6000, 'AV': 7000, 'BC': 8000, 'BV': 9000 }
};
// ================================================================
// POINT MACHINE DIRECTION DETECTION - MATCHING BACKEND FORMULA
// Backend C# Logic:
// - Get MAX value from NWKR attributes (IDs: 25, 27, 576, 578)
// - Get MAX value from RWKR attributes (IDs: 26, 28, 577, 579)
// - Compare: if NWKR_Max > RWKR_Max && NWKR_Max > 10 → NORMAL
// - Compare: if RWKR_Max > NWKR_Max && RWKR_Max > 10 → REVERSE
// ================================================================

// Direction threshold (matching backend)
var PM_DIRECTION_THRESHOLD = 10;

// NWKR (Normal) attribute IDs
var PM_NWKR_IDS = [25, 27, 576, 578];
// RWKR (Reverse) attribute IDs
var PM_RWKR_IDS = [26, 28, 577, 579];

/**
 * Determine Point Machine direction based on NWKR vs RWKR voltage comparison
 * Matches backend C# formula exactly
 @** @param {object} asset - The asset object from wsLiveData
 * @param {object} pm - The structured PM data from getPmStructuredData
 * @returns {object} { direction: 'NORMAL'|'REVERSE', nwkrMax: number, rwkrMax: number, source: string }*@
 */
function determinePmDirection(asset, pm) {
    var result = {
        direction: 'NORMAL',
        nwkrMax: 0,
        rwkrMax: 0,
        source: 'default'
    };

    if (!asset || !asset.attrs) {
        return result;
    }

    var nwkrValues = [];
    var rwkrValues = [];

    // Method 1: Scan asset.attrs for RDPMS data with NWKR/RWKR
    for (var attrName in asset.attrs) {
        if (!asset.attrs.hasOwnProperty(attrName)) continue;

        var attrData = asset.attrs[attrName];
        var attrId = parseInt(attrData.AttrId || attrData.AssetAttributeId || 0);
        var value = parseFloat(attrData.Value);

        if (isNaN(value)) continue;

        // Check by Attribute ID
        if (PM_NWKR_IDS.indexOf(attrId) !== -1) {
            nwkrValues.push(value);
        } else if (PM_RWKR_IDS.indexOf(attrId) !== -1) {
            rwkrValues.push(value);
        }
        // Also check by attribute name pattern
        else {
            var nameLower = attrName.toLowerCase();
            if (nameLower.indexOf('nwkr') !== -1) {
                nwkrValues.push(value);
            } else if (nameLower.indexOf('rwkr') !== -1) {
                rwkrValues.push(value);
            }
        }
    }

    // Method 2: Also scan pm.RDPMS if available
    if (pm && pm.RDPMS) {
        for (var rk in pm.RDPMS) {
            if (!pm.RDPMS.hasOwnProperty(rk)) continue;

            var rdpmsData = pm.RDPMS[rk];
            var rdpmsAttrId = parseInt(rdpmsData.AttrId || 0);
            var rdpmsValue = parseFloat(rdpmsData.value);

            if (isNaN(rdpmsValue)) continue;

            // Check by Attribute ID
            if (PM_NWKR_IDS.indexOf(rdpmsAttrId) !== -1) {
                nwkrValues.push(rdpmsValue);
            } else if (PM_RWKR_IDS.indexOf(rdpmsAttrId) !== -1) {
                rwkrValues.push(rdpmsValue);
            }
            // Also check by name pattern
            else {
                var rkLower = rk.toLowerCase();
                if (rkLower.indexOf('nwkr') !== -1) {
                    nwkrValues.push(rdpmsValue);
                } else if (rkLower.indexOf('rwkr') !== -1) {
                    rwkrValues.push(rdpmsValue);
                }
            }
        }
    }

    // Get MAX values
    result.nwkrMax = nwkrValues.length > 0 ? Math.max.apply(null, nwkrValues) : 0;
    result.rwkrMax = rwkrValues.length > 0 ? Math.max.apply(null, rwkrValues) : 0;

    // Apply backend formula:
    // if NWKR_Max > RWKR_Max && NWKR_Max > 10 → NORMAL
    // if RWKR_Max > NWKR_Max && RWKR_Max > 10 → REVERSE
    if (result.nwkrMax > result.rwkrMax && result.nwkrMax > PM_DIRECTION_THRESHOLD) {
        result.direction = 'NORMAL';
        result.source = 'nwkr_dominant';
    } else if (result.rwkrMax > result.nwkrMax && result.rwkrMax > PM_DIRECTION_THRESHOLD) {
        result.direction = 'REVERSE';
        result.source = 'rwkr_dominant';
    } else if (result.nwkrMax > PM_DIRECTION_THRESHOLD) {
        // NWKR above threshold but not greater than RWKR
        result.direction = 'NORMAL';
        result.source = 'nwkr_threshold';
    } else if (result.rwkrMax > PM_DIRECTION_THRESHOLD) {
        // RWKR above threshold but not greater than NWKR
        result.direction = 'REVERSE';
        result.source = 'rwkr_threshold';
    } else {
        // Both below threshold - use fallback logic
        result.source = 'fallback';

        // Fallback 1: Use OperationTime timestamp comparison
        if (pm) {
            var nAcTs = pm.Normal && pm.Normal.AC && pm.Normal.AC.OperationTime ?
                (pm.Normal.AC.OperationTime.timestamp || '') : '';
            var rAcTs = pm.Reverse && pm.Reverse.AC && pm.Reverse.AC.OperationTime ?
                (pm.Reverse.AC.OperationTime.timestamp || '') : '';

            if (rAcTs && nAcTs) {
                result.direction = rAcTs > nAcTs ? 'REVERSE' : 'NORMAL';
                result.source = 'timestamp_compare';
            } else if (rAcTs && !nAcTs) {
                result.direction = 'REVERSE';
                result.source = 'reverse_ts_only';
            } else if (nAcTs && !rAcTs) {
                result.direction = 'NORMAL';
                result.source = 'normal_ts_only';
            } else {
                // Fallback 2: Compare OperationTime values
                var nAcOT = pm.Normal && pm.Normal.AC && pm.Normal.AC.OperationTime ?
                    parseFloat(pm.Normal.AC.OperationTime.value) || 0 : 0;
                var rAcOT = pm.Reverse && pm.Reverse.AC && pm.Reverse.AC.OperationTime ?
                    parseFloat(pm.Reverse.AC.OperationTime.value) || 0 : 0;

                if (rAcOT > nAcOT && rAcOT > 0) {
                    result.direction = 'REVERSE';
                    result.source = 'optime_compare';
                } else {
                    result.direction = 'NORMAL';
                    result.source = 'default_normal';
                }
            }
        }
    }

    console.log('[PM Direction] Asset:', asset.AssetId,
        '| NWKR Max:', result.nwkrMax.toFixed(2),
        '| RWKR Max:', result.rwkrMax.toFixed(2),
        '| Threshold:', PM_DIRECTION_THRESHOLD,
        '| Direction:', result.direction,
        '| Source:', result.source);

    return result;
}

/**
 * Simplified direction check - returns just direction string
 */
function getPmDirectionSimple(assetId) {
    var asset = wsLiveData[assetId];
    var pm = typeof getPmStructuredData === 'function' ? getPmStructuredData(assetId) : null;
    var result = determinePmDirection(asset, pm);
    return result.direction;
}

/**
 * Check if direction is Reverse
 */
function isPmReverse(assetId) {
    return getPmDirectionSimple(assetId) === 'REVERSE';
}

var PM_OFFSETS = { 'Array': 1, 'Avg': 2, 'Min': 3, 'Max': 4, 'OperationTime': 5, 'Count': 6 };

// ================================================================
// POINT MACHINE OPERATION ID DEFINITIONS
// Normal/Reverse indication and operation IDs for row insertion
// ================================================================
// Normal indication IDs (voltage reading indicators)
var PM_NORMAL_INDICATION_IDS = [25, 27, 576, 578];
// Reverse indication IDs (voltage reading indicators)
var PM_REVERSE_INDICATION_IDS = [26, 28, 577, 579];

// Normal operation IDs - triggers new Normal row when unique TimestampDevice
var PM_NORMAL_OPERATION_IDS = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002];
// Reverse operation IDs - triggers new Reverse row when unique TimestampDevice
var PM_REVERSE_OPERATION_IDS = [6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];

// Combined for easy lookup
var PM_ALL_OPERATION_IDS = PM_NORMAL_OPERATION_IDS.concat(PM_REVERSE_OPERATION_IDS);

// Track last processed TimestampDevice per asset for operation-triggered rows
var pmLastOperationTimestamp = {};
var pmLastOperationType = {};
function pmTsToSecMs(ts) {
    if (!ts) return 0;
    try {
        var ms = new Date(ts).getTime();
        return (ms > 0) ? Math.floor(ms / 1000) * 1000 : 0;
    } catch (e) { return 0; }
}
var pmCardsBuilt = {};
var pmChartInstances = {};
var pmEventHistory = {};
var PM_MAX_HISTORY = 20;

// ================================================================
// PM OPERATION HELPER FUNCTIONS
// ================================================================
function isPmOperationId(attrId) {
    var id = parseInt(attrId);
    return PM_ALL_OPERATION_IDS.indexOf(id) !== -1;
}

function getPmOperationType(attrId) {
    var id = parseInt(attrId);
    if (PM_NORMAL_OPERATION_IDS.indexOf(id) !== -1) return 'Normal';
    if (PM_REVERSE_OPERATION_IDS.indexOf(id) !== -1) return 'Reverse';
    return null;
}


function isPmUniqueOperationTimestamp(assetId, timestampDevice, operationType) {
    if (!timestampDevice || timestampDevice === '0001-01-01T00:00:00+00:00') return false;

    var key = assetId + '_' + operationType;
    var lastTs = pmLastOperationTimestamp[key];
    var lastType = pmLastOperationType[assetId]; // track last direction for this asset

    // ── Operation-type changed (e.g. Normal→Reverse) ──
    // Even if the device sends the same timestamp in the same second,
    // a direction change is always a genuinely new operation row.
    if (lastType && lastType !== operationType) {
        pmLastOperationTimestamp[key] = timestampDevice;
        pmLastOperationType[assetId] = operationType;
        console.log('[PM-Timestamp] Asset ' + assetId + ' operation TYPE changed: '
            + lastType + ' → ' + operationType + ' | ts: ' + timestampDevice);
        return true;
    }

    if (!lastTs) {
        // First ever timestamp for this asset+direction → new row
        pmLastOperationTimestamp[key] = timestampDevice;
        pmLastOperationType[assetId] = operationType;
        return true;
    }

    try {
        var lastTime = pmTsToSecMs(lastTs);
        var newTime = pmTsToSecMs(timestampDevice);

        if (newTime > lastTime) {
            // Strictly newer → new row, advance tracker
            pmLastOperationTimestamp[key] = timestampDevice;
            pmLastOperationType[assetId] = operationType;
            console.log('[PM-Timestamp] Asset ' + assetId + ' ' + operationType +
                ' -- new timestamp: ' + timestampDevice + ' (was: ' + lastTs + ')');
            return true;
        }
        // Same or older → update existing row (not a new row)
        return false;
    } catch (e) {
        console.warn('[PM-Timestamp] Parse error:', e);
        return false;
    }
}


function processPmOperationMessage(assetId, operationType, timestampDevice) {
    if (!operationType || !timestampDevice) return false;
    if (timestampDevice.indexOf('0001-01-01') !== -1) return false;
    var tsMs = pmTsToSecMs(timestampDevice);
    if (!tsMs) return false;

    if (!pmEventHistory[assetId]) pmEventHistory[assetId] = [];
    var hist = pmEventHistory[assetId];

    // ── UNIQUE TIMESTAMPDEVICE RULE ──
    // One row per unique second. Search ALL history for this exact second.
    // If found → UPDATE that row in-place (regardless of direction).
    // If not found → NEW row.
    var existingIdx = -1;
    for (var h = hist.length - 1; h >= 0; h--) {
        if (hist[h].timestampDevice &&
            pmTsToSecMs(hist[h].timestampDevice) === tsMs) {
            existingIdx = h;
            break;
        }
    }
    var isNew = (existingIdx === -1);

    console.log('[PM] ' + (isNew ? 'NEW ROW' : 'UPDATE ROW') + ' | ' + operationType + ' | ts:' + timestampDevice);

    // Always write history (even before card is built) so rows are ready on first render
    buildPmOperationRow(assetId, operationType, timestampDevice, isNew, existingIdx);
    return true;
}

function buildPmOperationRow(assetId, operationType, timestampDevice, isNew, existingIdx) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var baseName = asset.AssetName || assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;
    var pm = getPmStructuredData(assetId);
    if (!pm) return;

    // Which ends are visible
    var showA = true, showB = true;
    if (window.pmEndInfo && window.pmEndInfo[assetId]) {
        var ends = window.pmEndInfo[assetId];
        showA = ends.hasA || (!ends.hasA && !ends.hasB);
        showB = ends.hasB || (!ends.hasA && !ends.hasB);
    }

    // Format timestamp -- parse ISO parts directly to honour device timezone offset
    var dateStr = '--';
    if (timestampDevice) {
        try {
            var isoMatch = timestampDevice.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})/);
            if (isoMatch) {
                dateStr = isoMatch[1] + '-' + isoMatch[2] + '-' + isoMatch[3] + ' '
                    + isoMatch[4] + ':' + isoMatch[5] + ':' + isoMatch[6];
            } else {
                var dt = new Date(timestampDevice);
                dateStr = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' '
                    + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
            }
        } catch (e) { }
    }

    var isReverse = (operationType === 'Reverse');
    var dirText = isReverse ? 'Reverse Operation' : 'Normal Operation';
    var useDir = isReverse ? 'Reverse' : 'Normal';

    var aAcMax = pm[useDir].AC.Max ? pmFmt(pm[useDir].AC.Max.value) : '--';
    var aAcAvg = pm[useDir].AC.Avg ? pmFmt(pm[useDir].AC.Avg.value) : '--';
    var aAcCount = pm[useDir].AC.Count ? pmFmtInt(pm[useDir].AC.Count.value) : '--';
    var aAcOTVal = pm[useDir].AC.OperationTime ? pmFmtInt(pm[useDir].AC.OperationTime.value) : '--';
    var aAvAvg = pm[useDir].AV.Avg ? pmFmt(pm[useDir].AV.Avg.value, 2) : '--';

    var bBcMax = pm[useDir].BC.Max ? pmFmt(pm[useDir].BC.Max.value) : '--';
    var bBcAvg = pm[useDir].BC.Avg ? pmFmt(pm[useDir].BC.Avg.value) : '--';
    var bBcCount = pm[useDir].BC.Count ? pmFmtInt(pm[useDir].BC.Count.value) : '--';
    var bBcOTVal = pm[useDir].BC.OperationTime ? pmFmtInt(pm[useDir].BC.OperationTime.value) : '--';
    var bBvAvg = pm[useDir].BV.Avg ? pmFmt(pm[useDir].BV.Avg.value, 2) : '--';

    var totalOT = 0;
    if (showA) totalOT += (parseInt(aAcOTVal) || 0);
    if (showB) totalOT += (parseInt(bBcOTVal) || 0);

    var currentRow = {
        date: dateStr, name: name, dir: dirText,
        aAcMax: aAcMax, aAcAvg: aAcAvg, aAcCount: aAcCount, aAcOT: aAcOTVal, aAvAvg: aAvAvg,
        bBcMax: bBcMax, bBcAvg: bBcAvg, bBcCount: bBcCount, bBcOT: bBcOTVal, bBvAvg: bBvAvg,
        totalOT: totalOT, showA: showA, showB: showB,
        operationType: operationType, timestampDevice: timestampDevice, hasRealData: true
    };

    if (!pmEventHistory[assetId]) pmEventHistory[assetId] = [];

    var cardReady = ($('#drpView').val() === 'PointMachine' && pmCardsBuilt[assetId]);

    if (isNew) {
        // ── New unique timestamp → new history entry ──
        pmEventHistory[assetId].push(currentRow);
        if (pmEventHistory[assetId].length > PM_MAX_HISTORY) {
            pmEventHistory[assetId].shift();
        }
        console.log('[PM Build] NEW row | ' + operationType + ' | ts:' + timestampDevice
            + ' | total rows:' + pmEventHistory[assetId].length);
        if (cardReady) {
            prependPmOperationRow(assetId, currentRow, showA, showB);
        }
    } else {
        // ── Same timestamp already in history → update values in-place ──
        var oldTs = (existingIdx !== -1 && pmEventHistory[assetId][existingIdx])
            ? pmEventHistory[assetId][existingIdx].timestampDevice : null;
        if (existingIdx !== -1) {
            pmEventHistory[assetId][existingIdx] = currentRow;
        } else {
            pmEventHistory[assetId].push(currentRow);
        }
        console.log('[PM Build] UPDATE row | ' + operationType + ' | ts:' + timestampDevice);
        if (cardReady) {
            updatePmRowInPlace(assetId, currentRow, showA, showB, oldTs);
        }
    }
}    // ================================================================
// PREPEND A NEW <tr> AT THE TOP OF THE TABLE
// Called only when a genuinely new operation event arrives.
// ================================================================
function prependPmOperationRow(assetId, row, showA, showB) {
    var $tbody = $('#pmTbody_' + assetId);
    if (!$tbody.length) {
        // Table not in DOM yet -- do a full rebuild from history
        refreshPmOperationTable(assetId, pmEventHistory[assetId] || [], showA, showB);
        return;
    }

    var dirPrefix = (row.operationType === 'Normal') ? 'N' : 'R';
    var html = '<tr data-ts="' + (row.timestampDevice || '') + '" data-optype="' + (row.operationType || '') + '" class="pm-val-flash-anim">';
    html += '<td class="pm-date-cell">' + row.date + '</td>';
    html += '<td>' + pmEsc(row.name) + '</td>';
    html += '<td>' + row.dir + '</td>';
    html += '<td>'
        + '<a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + dirPrefix + '_AC\')">C</a>'
        + ' / '
        + '<a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + dirPrefix + '_AV\')">V</a>'
        + '</td>';
    html += '<td><a href="javascript:void(0)">C</a> / <a href="javascript:void(0)">V</a> T(' + row.totalOT + ')</td>';
    if (row.showA !== false && showA) {
        html += '<td class="col-a"><span class="pm-current-val">' + row.aAcMax + ' / ' + row.aAcAvg + ' (' + row.aAcCount + ')</span></td>';
        html += '<td class="col-a"><span class="pm-voltage-val">' + row.aAvAvg + '</span></td>';
        html += '<td class="col-a">' + row.aAcOT + '</td>';
    }
    if (row.showB !== false && showB) {
        html += '<td class="col-b"><span class="pm-current-val-b">' + row.bBcMax + ' / ' + row.bBcAvg + ' (' + row.bBcCount + ')</span></td>';
        html += '<td class="col-b"><span class="pm-voltage-val">' + row.bBvAvg + '</span></td>';
        html += '<td class="col-b">' + row.bBcOT + '</td>';
    }
    html += '</tr>';

    $tbody.prepend(html);

    // Remove flash class after animation completes
    setTimeout(function () {
        $tbody.find('tr[data-ts="' + row.timestampDevice + '"][data-optype="' + row.operationType + '"]')
            .removeClass('pm-val-flash-anim');
    }, 1200);

    // If history was trimmed, remove the last <tr> from the DOM too
    if (pmEventHistory[assetId] && $tbody.find('tr').length > PM_MAX_HISTORY) {
        $tbody.find('tr:last').remove();
    }

    console.log('[PM-Prepend] ✓ Row prepended | ' + row.operationType + ' | ts: ' + row.timestampDevice);
}

// ================================================================
// UPDATE EXISTING PM ROW IN-PLACE -- no table rebuild, no flicker
// Finds the <tr> by operationType (and optionally oldTs when the
// timestamp changed) then patches date + all value cells.
// oldTs = previous timestampDevice stored on the row (may be null
//         if this is the very first update for this direction).
// ================================================================
function updatePmRowInPlace(assetId, row, showA, showB, oldTs) {
    var $tbody = $('#pmTbody_' + assetId);
    if (!$tbody.length) return;

    // Try to find the <tr> by the OLD timestamp first (timestamp may have changed),
    // then fall back to finding by operationType alone.
    var $tr;
    if (oldTs) {
        $tr = $tbody.find('tr[data-ts="' + oldTs + '"][data-optype="' + row.operationType + '"]');
    }
    if (!$tr || !$tr.length) {
        $tr = $tbody.find('tr[data-optype="' + row.operationType + '"]').first();
    }
    if (!$tr.length) {
        // Row not in DOM at all -- fall back to full rebuild
        console.log('[PM-InPlace] Row not found in DOM, falling back to full refresh');
        refreshPmOperationTable(assetId, pmEventHistory[assetId] || [], showA, showB);
        return;
    }

    // Always update data-ts so future lookups use the current timestamp
    $tr.attr('data-ts', row.timestampDevice);

    // Column order: 0=Date, 1=Name, 2=Dir, 3=Chart links, 4=OT total, 5+ A/B values
    // Update date cell (col 0) -- timestamp may have changed
    $tr.find('td').eq(0).text(row.date);

    var ci = 4;
    $tr.find('td').eq(ci).html('<a href="javascript:void(0)">C</a> / <a href="javascript:void(0)">V</a> T(' + row.totalOT + ')');
    ci++;

    if (showA && row.showA !== false) {
        $tr.find('td').eq(ci).find('span.pm-current-val').text(row.aAcMax + ' / ' + row.aAcAvg + ' (' + row.aAcCount + ')'); ci++;
        $tr.find('td').eq(ci).find('span.pm-voltage-val').text(row.aAvAvg); ci++;
        $tr.find('td').eq(ci).text(row.aAcOT); ci++;
    }

    if (showB && row.showB !== false) {
        $tr.find('td').eq(ci).find('span.pm-current-val-b').text(row.bBcMax + ' / ' + row.bBcAvg + ' (' + row.bBcCount + ')'); ci++;
        $tr.find('td').eq(ci).find('span.pm-voltage-val').text(row.bBvAvg); ci++;
        $tr.find('td').eq(ci).text(row.bBcOT); ci++;
    }

    // Flash updated cells
    $tr.find('td').slice(4).addClass('pm-val-flash-anim');
    setTimeout(function () { $tr.find('td').removeClass('pm-val-flash-anim'); }, 1200);

    console.log('[PM-InPlace] ✓ Row updated | ' + row.operationType + ' | ts: ' + row.timestampDevice);
}

// ================================================================
// REFRESH PM OPERATION TABLE FROM HISTORY (full rebuild)
// Used on initial render or when a DOM row is missing.
// Renders ALL history entries sorted newest-first -- no deduplication.
// ================================================================
function refreshPmOperationTable(assetId, hist, showA, showB) {
    var $tbody = $('#pmTbody_' + assetId);
    if (!$tbody.length) return;

    // Sort newest first
    var sorted = hist.slice().sort(function (a, b) {
        return pmTsToSecMs(b.timestampDevice) - pmTsToSecMs(a.timestampDevice);
    });

    var rows = '';
    for (var i = 0; i < sorted.length; i++) {
        var r = sorted[i];
        var dirPrefix = (r.operationType === 'Normal') ? 'N' : 'R';
        rows += '<tr data-ts="' + (r.timestampDevice || '') + '" data-optype="' + (r.operationType || '') + '"'
            + (i === 0 ? ' class="pm-val-flash-anim"' : '') + '>';
        rows += '<td class="pm-date-cell">' + r.date + '</td>';
        rows += '<td>' + pmEsc(r.name) + '</td>';
        rows += '<td>' + r.dir + '</td>';
        rows += '<td>'
            + '<a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + dirPrefix + '_AC\')">C</a>'
            + ' / '
            + '<a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + dirPrefix + '_AV\')">V</a>'
            + '</td>';
        rows += '<td><a href="javascript:void(0)">C</a> / <a href="javascript:void(0)">V</a> T(' + r.totalOT + ')</td>';
        if (r.showA !== false && showA) {
            rows += '<td class="col-a"><span class="pm-current-val">' + r.aAcMax + ' / ' + r.aAcAvg + ' (' + r.aAcCount + ')</span></td>';
            rows += '<td class="col-a"><span class="pm-voltage-val">' + r.aAvAvg + '</span></td>';
            rows += '<td class="col-a">' + r.aAcOT + '</td>';
        }
        if (r.showB !== false && showB) {
            rows += '<td class="col-b"><span class="pm-current-val-b">' + r.bBcMax + ' / ' + r.bBcAvg + ' (' + r.bBcCount + ')</span></td>';
            rows += '<td class="col-b"><span class="pm-voltage-val">' + r.bBvAvg + '</span></td>';
            rows += '<td class="col-b">' + r.bBcOT + '</td>';
        }
        rows += '</tr>';
    }
    if (rows) $tbody.html(rows);
}

function parsePmAttrName(attrName) {
    if (!attrName) return null;
    var m = attrName.match(/^0?(\d+)-PM-0?(\d+)-(\w+)$/);
    return m ? { assetId: parseInt(m[1]), attrId: parseInt(m[2]), metric: m[3] } : null;
}

function decodePmAttribute(attrId) {
    var id = parseInt(attrId);
    if (isNaN(id) || id < 1000 || id > 9006) return null;
    var dirs = ['Normal', 'Reverse'], types = ['AC', 'AV', 'BC', 'BV'];
    var metrics = ['Array', 'Avg', 'Min', 'Max', 'OperationTime', 'Count'];
    for (var di = 0; di < dirs.length; di++) {
        for (var ti = 0; ti < types.length; ti++) {
            var base = PM_BASE_CODES[dirs[di]][types[ti]];
            var off = id - base;
            if (off >= 1 && off <= 6) return { direction: dirs[di], type: types[ti], metric: metrics[off - 1] };
        }
    }
    return null;
}

function getPmStructuredData(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return null;
    var r = { Normal: { AC: {}, AV: {}, BC: {}, BV: {} }, Reverse: { AC: {}, AV: {}, BC: {}, BV: {} }, RDPMS: {}, DataLogger: {} };
    for (var an in asset.attrs) {
        if (!asset.attrs.hasOwnProperty(an)) continue;
        var ad = asset.attrs[an];

        // PRIMARY: decode from the numeric code the WS actually delivers.
        // The display name is usually a friendly alias and won't match the
        // "X-PM-code-metric" pattern, which is why name-only parsing leaves
        // every metric unclassified -> blank cells in card AND list views.
        var numId = parseInt(ad.AssetAttributeId || ad.AttrId || 0);
        var d = numId ? decodePmAttribute(numId) : null;

        // FALLBACK: legacy encoded names like "12-PM-1004-Max"
        if (!d) { var p = parsePmAttrName(an); if (p) d = decodePmAttribute(p.attrId); }

        if (d) {
            r[d.direction][d.type][d.metric] = { value: ad.Value, timestamp: ad.Timestamp, changed: ad.changed, attrName: an };
        } else {
            // Carry AttrId/AssetAttributeId so NWKR/RWKR direction detection works
            r.RDPMS[an] = { value: ad.Value, timestamp: ad.Timestamp, changed: ad.changed, AttrId: ad.AttrId, AssetAttributeId: ad.AssetAttributeId };
        }
    }
    if (asset.dlRelays) { for (var rk in asset.dlRelays) { if (asset.dlRelays.hasOwnProperty(rk)) r.DataLogger[rk] = asset.dlRelays[rk]; } }
    return r;
}

function pmFmt(v, dec) { if (v === null || v === undefined || v === '') return '-'; var n = parseFloat(v); return isNaN(n) ? v : n.toFixed(dec !== undefined ? dec : 2); }
function pmFmtInt(v) { if (v === null || v === undefined || v === '') return '-'; var n = parseFloat(v); return isNaN(n) ? v : Math.round(n).toString(); }
function pmEsc(t) { if (!t) return ''; var d = document.createElement('div'); d.appendChild(document.createTextNode(t)); return d.innerHTML; }
function pmPad2(n) { return n < 10 ? '0' + n : '' + n; }

// ================================================================
// RENDER POINT MACHINE VIEW
// ================================================================
function renderPointMachineView() {
    // Guard: only render if Point Machine is actually selected.
    if (typeof isPointAssetType === 'function' && !isPointAssetType()) {
        console.warn('[PM View] Not a Point Machine asset type – skipping render');
        return;
    }

    var ids = Object.keys(wsLiveData).filter(function (id) {
        // Defense-in-depth: skip assets not in the bulk/GetAssestBy response.
        return (typeof isAssetInBulkWhitelist !== 'function') || isAssetInBulkWhitelist(id);
    });
    console.log('[PM View] Rendering. Assets:', ids.length, 'Data:', wsLiveData);

    if (ids.length === 0) {
        console.log('[PM View] No assets to render yet');
        return;
    }

    $('#wsWaiting').remove();

    ids.sort(function (a, b) {
        return (wsLiveData[a].AssetName || '').localeCompare(
            wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }
        );
    });

    var $c = $('#divTelemetryLive');

    if ($c.find('#pmMainContainer').length === 0) {
        console.log('[PM View] Creating container');

        // DOM was recreated, so force card rebuild.
        pmCardsBuilt = {};
        if (typeof pmCardsBuilding !== 'undefined') pmCardsBuilding = {};

        $c.html('<div id="pmMainContainer" class="col-12 p-0 p-md-2"></div>');
    }

    // Build/update cards once. The previous newer file had a nested duplicate loop here.
    for (var i = 0; i < ids.length; i++) {
        var aid = ids[i];

        if (!pmCardsBuilt[aid] || $('#pmCard_' + aid).length === 0) {
            console.log('[PM View] Building card for:', aid, wsLiveData[aid].AssetName);
            pmCardsBuilt[aid] = false;
            buildPmCard(aid);
            pmCardsBuilt[aid] = true;
        }

        updatePmCard(aid);
    }

    wsUpdatedAssets = {};
    $('#downloadContainer').show();
}


// ================================================================
// Debug helper - call this from console to check state
// ================================================================
function debugWsState() {
    console.log('=== WebSocket Debug State ===');
    console.log('Connected:', wsIsConnected);
    console.log('Batches received:', wsBatchCount);
    console.log('Messages processed:', wsMessageCount);
    console.log('Assets in memory:', Object.keys(wsLiveData).length);
    console.log('Asset data:', wsLiveData);
    console.log('Attributes:', wsAttributeNames);
    console.log('Current filters:', {
        assetType: wsCurrentAssetTypeId,
        assetIds: wsCurrentFilterAssetIds
    });
    console.log('PM Cards built:', Object.keys(pmCardsBuilt));
    console.log('RDPMS Cards built:', Object.keys(rdpmsCardsBuilt));
    console.log('View type:', $('#drpView').val());
    return {
        connected: wsIsConnected,
        batches: wsBatchCount,
        messages: wsMessageCount,
        assets: Object.keys(wsLiveData).length,
        data: wsLiveData
    };
}

// Make it available globally
window.debugWsState = debugWsState;

console.log('[WS Data Fix] Loaded. Call debugWsState() in console to check state.');


// ================================================================
// HELPER: Check if A-end or B-end has data
// ================================================================
function pmEndHasData(pm, end) {
    // end = 'A' or 'B'
    // Check both Normal and Reverse directions for this end
    if (!pm) return false;

    var current = end === 'A' ? 'AC' : 'BC';
    var voltage = end === 'A' ? 'AV' : 'BV';

    // Check Normal direction
    var hasNormalCurrent = pm.Normal[current] && (
        pm.Normal[current].Max || pm.Normal[current].Avg ||
        pm.Normal[current].OperationTime || pm.Normal[current].Array
    );
    var hasNormalVoltage = pm.Normal[voltage] && (
        pm.Normal[voltage].Max || pm.Normal[voltage].Avg || pm.Normal[voltage].Array
    );

    // Check Reverse direction
    var hasReverseCurrent = pm.Reverse[current] && (
        pm.Reverse[current].Max || pm.Reverse[current].Avg ||
        pm.Reverse[current].OperationTime || pm.Reverse[current].Array
    );
    var hasReverseVoltage = pm.Reverse[voltage] && (
        pm.Reverse[voltage].Max || pm.Reverse[voltage].Avg || pm.Reverse[voltage].Array
    );

    return hasNormalCurrent || hasNormalVoltage || hasReverseCurrent || hasReverseVoltage;
}

// ================================================================
// HELPER: Get available ends for asset
// ================================================================
function pmGetAvailableEnds(pm) {
    var ends = { hasA: false, hasB: false, onlyA: false, onlyB: false, hasBoth: false };

    ends.hasA = pmEndHasData(pm, 'A');
    ends.hasB = pmEndHasData(pm, 'B');
    ends.hasBoth = ends.hasA && ends.hasB;
    ends.onlyA = ends.hasA && !ends.hasB;
    ends.onlyB = ends.hasB && !ends.hasA;

    return ends;
}

// Store IsSeriesOperation status for each asset
var pmSeriesOperationStatus = {};

// Fetch asset details to check IsSeriesOperation from Telemetry controller
function fetchAssetSeriesOperation(assetId, callback) {
    // If already fetched, use cached value
    if (pmSeriesOperationStatus[assetId] !== undefined) {
        if (callback) callback(pmSeriesOperationStatus[assetId]);
        return;
    }

    // AJAX lookup is disabled — but we MUST still fire the callback, otherwise
    // buildPmCardWithSeriesInfo() never runs and the card (header + tbody) is
    // never created, leaving #pmMainContainer empty. Default to false
    // (no "New Combine" column) and call back synchronously.
    pmSeriesOperationStatus[assetId] = false;
    if (callback) callback(false);
}
// BUILD PM CARD -- DYNAMIC BASED ON AVAILABLE DATA
// Shows only ends (A/B) that have data
// ================================================================
// Track cards currently being built (to prevent race conditions)
var pmCardsBuilding = {};

function buildPmCard(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    // Prevent duplicate cards - check if card already exists
    if ($('#pmCard_' + assetId).length > 0) {
        console.log('[PM] Card already exists for asset:', assetId);
        return;
    }

    // Prevent concurrent builds for same asset (race condition prevention)
    if (pmCardsBuilding[assetId]) {
        console.log('[PM] Card already building for asset:', assetId);
        return;
    }
    pmCardsBuilding[assetId] = true;

    // Fetch IsSeriesOperation status first, then build the card
    fetchAssetSeriesOperation(assetId, function (isSeriesOp) {
        buildPmCardWithSeriesInfo(assetId, isSeriesOp);
        delete pmCardsBuilding[assetId]; // Clear building flag
    });
}

function buildPmCardWithSeriesInfo(assetId, showCombineColumn) {

    var asset = wsLiveData[assetId];
    if (!asset) return;

    // Double-check to prevent duplicate cards (callback might fire multiple times)
    if ($('#pmCard_' + assetId).length > 0) {
        console.log('[PM] Card already exists (in callback) for asset:', assetId);
        return;
    }

    // Add PT- prefix to Point Machine asset name
    var baseName = asset.AssetName || assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;
    var siteId = asset.SiteId || $('#drpSite').val();

    // Store showCombineColumn for later use in row building
    if (!window.pmCombineColumnStatus) window.pmCombineColumnStatus = {};
    window.pmCombineColumnStatus[assetId] = showCombineColumn;

    // Get PM structured data to check which ends have data
    var pm = getPmStructuredData(assetId);
    var ends = pm ? pmGetAvailableEnds(pm) : { hasA: true, hasB: true, hasBoth: true };

    // Store ends info for later updates
    if (!window.pmEndInfo) window.pmEndInfo = {};
    window.pmEndInfo[assetId] = ends;

    var h = '';

    // ── CARD WRAPPER ──
    h += '<div class="pm-card-wrapper" id="pmCard_' + assetId + '">';

    // ── HEADER BAR (dark teal) ──
    h += '<div class="pm-card-header">';
    h += '<div class="pm-card-header-left">';
    h += '<h4>POINT MACHINE : ' + pmEsc(name) + '</h4>';
    h += '<div class="pm-index-score">Index Score: 7</div>';
    h += '</div>';
    h += '<div class="pm-card-header-right">';
    h += '<button class="btn-event-log" onclick="fnGetEventLog(\'' + assetId + '\')"><i class="fas fa-bell"></i> Event Log</button>';
    h += '</div>';
    h += '</div>';

    // ── IRS / TWS dots — built from PM metadata immediately ──
    var _pmMetaA = (typeof getPmEndMeta === 'function') ? getPmEndMeta(assetId, 'A', baseName) : { label: 'PT-' + baseName + 'A IRS', dotColor: '#dc8b33' };
    var _pmMetaB = (typeof getPmEndMeta === 'function') ? getPmEndMeta(assetId, 'B', baseName) : { label: 'PT-' + baseName + 'B IRS', dotColor: '#dc8b33' };
    var _pmMetaObj = (typeof pmAssetMetaCache !== 'undefined') ? pmAssetMetaCache[String(assetId)] : null;
    var _pmIsHalf = _pmMetaObj ? _pmMetaObj.isHalf : false;

    h += '<div class="pm-badges-row" id="pmBadges_' + assetId + '">';

    h += '<span class="pm-irs-badge" style="display:inline-flex;align-items:center;' +
        'gap:5px;margin-right:10px;padding:3px 8px;background:rgba(255,255,255,0.06);' +
        'border:1px solid rgba(255,255,255,0.12);border-radius:14px;">' +
        '<span style="width:14px;height:14px;border-radius:50%;flex-shrink:0;display:inline-block;' +
        'background:' + _pmMetaA.dotColor + ';box-shadow:0 0 6px ' + _pmMetaA.dotColor + '80;"></span>' +
        '<span class="pm-irs-badge">' + pmEsc(_pmMetaA.label) + '</span></span>';

    if (!_pmIsHalf) {
        h += '<span class="pm-irs-badge" style="display:inline-flex;align-items:center;' +
            'gap:5px;margin-right:10px;padding:3px 8px;background:rgba(255,255,255,0.06);' +
            'border:1px solid rgba(255,255,255,0.12);border-radius:14px;">' +
            '<span style="width:14px;height:14px;border-radius:50%;flex-shrink:0;display:inline-block;' +
            'background:' + _pmMetaB.dotColor + ';box-shadow:0 0 6px ' + _pmMetaB.dotColor + '80;"></span>' +
            '<span class="pm-irs-badge">' + pmEsc(_pmMetaB.label) + '</span></span>';
    }

    // Relay badges injected here by renderPmBadges.
    h += '<span id="pmRelayBadges_' + assetId + '" ' +
        'style="display:inline-flex;align-items:center;flex-wrap:wrap;gap:4px;"></span>';

    h += '</div>';

    // ── ACTIONS ROW (Download / See More) ──
    h += '<div style="display:flex;justify-content:flex-end;padding:8px 16px;gap:8px;border-bottom:1px solid rgba(255,255,255,0.07);background:rgba(0,0,0,0.10);">';
    h += '<button class="pm-btn-download" onclick="pmDownloadCard(\'' + assetId + '\')">Download</button>';
    h += '<button class="pm-btn-seemore" onclick="pmToggleSeeMore(\'' + assetId + '\')">See More</button>';
    h += '</div>';

    // ── DATA TABLE - DYNAMIC BASED ON AVAILABLE ENDS ──
    h += '<div class="table-responsive" style="overflow-x:auto;">';
    h += '<table class="pm-data-table">';

    // Calculate column spans based on available ends
    var endColSpan = 3; // Each end has 3 columns (Current, Voltage, Time)
    var totalEndCols = (ends.hasA ? endColSpan : 0) + (ends.hasB ? endColSpan : 0);
    if (totalEndCols === 0) totalEndCols = 6; // Default to show both if no data yet

    var showA = ends.hasA || (!ends.hasA && !ends.hasB); // Show A if it has data or if neither has data yet
    var showB = ends.hasB || (!ends.hasA && !ends.hasB); // Show B if it has data or if neither has data yet

    // THEAD ROW 1: Name merged cell + Direction headers
    h += '<thead>';
    h += '<tr>';
    h += '<td colspan="' + (showCombineColumn ? '4' : '3') + '" rowspan="2" class="text-center pm-name-merged-cell">' + pmEsc(name) + '</td>';

    // Direction A header (only if A has data)
    if (showA) {
        h += '<td colspan="3" class="pm-dir-header-cell col-a" id="pmDirHeaderA_' + assetId + '">';
        h += '<span class="d-flex align-items-center">';
        h += 'Direction A : <span id="pmDirLabelA_' + assetId + '">' + pmEsc(name) + '</span> ';
        h += '<span class="pm-dir-badge normal" id="pmDirBadgeA_' + assetId + '">POINT IN NORMAL</span>';
        h += ' <i class="fas fa-chart-line ms-auto" style="cursor:pointer;color:#259dab;font-size:14px;" onclick="fnPmDirGraph(\'' + assetId + '\',\'A\')" title="Historical Graph"></i>';
        h += ' <i class="fas fa-wave-square" style="cursor:pointer;color:#7c3aed;font-size:14px;margin-left:6px;" onclick="fnShowVibrationModal(\'' + assetId + '\')" title="Vibration Data"></i>';
        h += '</span>';
        h += '</td>';
    }

    // Direction B header (only if B has data)
    if (showB) {
        h += '<td colspan="3" class="pm-dir-header-cell col-b" id="pmDirHeaderB_' + assetId + '">';
        h += '<span class="d-flex align-items-center">';
        h += 'Direction B : <span id="pmDirLabelB_' + assetId + '">' + pmEsc(name) + '</span> ';
        h += '<span class="pm-dir-badge normal" id="pmDirBadgeB_' + assetId + '">POINT IN NORMAL</span>';
        h += ' <i class="fas fa-chart-line ms-auto" style="cursor:pointer;color:#259dab;font-size:14px;" onclick="fnPmDirGraph(\'' + assetId + '\',\'B\')" title="Historical Graph"></i>';
        h += ' <i class="fas fa-wave-square" style="cursor:pointer;color:#7c3aed;font-size:14px;margin-left:6px;" onclick="fnShowVibrationModal(\'' + assetId + '\')" title="Vibration Data"></i>';
        h += '</span>';
        h += '</td>';
    }
    h += '</tr>';

    // THEAD ROW 2: Voltage sub-headers for each direction
    h += '<tr>';
    // A-end voltages (only if A has data)
    if (showA) {
        h += '<td colspan="3" class="pm-voltage-subheader col-a" id="pmVoltHeaderA_' + assetId + '">';
        h += '<span class="d-flex align-items-center pm-volt-row">';
        //h += '<span>V <small>PT NWKR</small> <strong id="pmVnwkrA_' + assetId + '">--</strong> V</span>';
        //h += '<span class="ms-auto">V <small>PT 24 DC LOC N/R</small> <strong id="pmVdcA_' + assetId + '">--</strong> V</span>';
        h += '<span>V <small>PT <span id="pmKrLabelA_' + assetId + '">NWKR</span></small> <strong id="pmVnwkrA_' + assetId + '">--</strong> V</span>';
        h += '<span class="ms-auto">V <small>PT 24 DC LOC <span id="pmDirLocLabelA_' + assetId + '">N/R</span></small> <strong id="pmVdcA_' + assetId + '">--</strong> V</span>';
        h += '</span></td>';
    }
    // B-end voltages (only if B has data)
    if (showB) {
        h += '<td colspan="3" class="pm-voltage-subheader col-b" id="pmVoltHeaderB_' + assetId + '">';
        h += '<span class="d-flex align-items-center pm-volt-row">';
        h += '<span>V <small>PT <span id="pmKrLabelB_' + assetId + '">NWKR</span></small> <strong id="pmVnwkrB_' + assetId + '">--</strong> V</span>';
        h += '<span class="ms-auto">V <small>PT 24 DC LOC <span id="pmDirLocLabelB_' + assetId + '">N/R</span></small> <strong id="pmVdcB_' + assetId + '">--</strong> V</span>';
        h += '</span></td>';
    }
    h += '</tr>';
    h += '</thead>';

    // THEAD: Column headers
    h += '<thead><tr>';
    h += '<th class="pm-th">Date</th>';
    h += '<th class="pm-th">Name</th>';
    h += '<th class="pm-th">Direction</th>';

    // New Combine column - only show if IsSeriesOperation is 1
    if (showCombineColumn) {
        h += '<th class="pm-th">New Combine</th>';
    }

    // A-end columns (only if A has data)
    if (showA) {
        h += '<th class="pm-th col-a" id="pmThAC_' + assetId + '">';
        h += '<span class="pm-avg-label">I<small>PT N/R</small>(Avg) : <span id="pmIptAvgA_' + assetId + '">--</span></span><br/>';
        h += 'A Current(Max/Avg)</th>';

        h += '<th class="pm-th col-a" id="pmThAV_' + assetId + '">';
        h += '<span class="pm-avg-label">V<small>PT110 DC LOC N/R</small>(Avg) : <span id="pmVptAvgA_' + assetId + '">--</span></span><br/>';
        h += 'A V<sub>PT110 DC LOC</sub></th>';

        h += '<th class="pm-th col-a" id="pmThAT_' + assetId + '">';
        h += '<span class="pm-avg-label">T<small>PT N/R</small>(Avg) : <span id="pmTptAvgA_' + assetId + '">--</span></span><br/>';
        h += 'A T<sub>PT</sub> (ms)</th>';
    }

    // B-end columns (only if B has data)
    if (showB) {
        h += '<th class="pm-th col-b" id="pmThBC_' + assetId + '">';
        h += '<span class="pm-avg-label">I<small>PT N/R</small>(Avg) : <span id="pmIptAvgB_' + assetId + '">--</span></span><br/>';
        h += 'B Current(Max/Avg)</th>';

        h += '<th class="pm-th col-b" id="pmThBV_' + assetId + '">';
        h += '<span class="pm-avg-label">V<small>PT110 DC LOC N/R</small>(Avg) : <span id="pmVptAvgB_' + assetId + '">--</span></span><br/>';
        h += 'B V<sub>PT110 DC LOC</sub></th>';

        h += '<th class="pm-th col-b" id="pmThBT_' + assetId + '">';
        h += '<span class="pm-avg-label">T<small>PT N/R</small>(Avg) : <span id="pmTptAvgB_' + assetId + '">--</span></span><br/>';
        h += 'B T<sub>PT</sub> (ms)</th>';
    }

    h += '</tr></thead>';
    h += '<tbody id="pmTbody_' + assetId + '" data-show-a="' + (showA ? '1' : '0') + '" data-show-b="' + (showB ? '1' : '0') + '" data-show-combine="' + (showCombineColumn ? '1' : '0') + '"></tbody>';
    h += '</table></div>';

    // ── SEE MORE: Waveform Charts (hidden) - ENHANCED LAYOUT ──
    h += '<div id="pmSeeMore_' + assetId + '" class="pm-seemore-section" style="display:none;">';
    h += '<div class="pm-seemore-header"><h6>Waveform Charts -- <span id="pmWaveDir_' + assetId + '">Current Operation</span></h6></div>';
    h += '<div class="pm-waveform-grid">';
    // Charts will be filtered by direction when rendered
    var chartNames = [];
    var dirs = ['N', 'R']; // Both directions - will be filtered at render time
    if (showA) {
        chartNames.push({ key: 'N_AC', label: 'Normal -- A Current (A)', dir: 'N' });
        chartNames.push({ key: 'N_AV', label: 'Normal -- A Voltage (V)', dir: 'N' });
        chartNames.push({ key: 'R_AC', label: 'Reverse -- A Current (A)', dir: 'R' });
        chartNames.push({ key: 'R_AV', label: 'Reverse -- A Voltage (V)', dir: 'R' });
    }
    if (showB) {
        chartNames.push({ key: 'N_BC', label: 'Normal -- B Current (A)', dir: 'N' });
        chartNames.push({ key: 'N_BV', label: 'Normal -- B Voltage (V)', dir: 'N' });
        chartNames.push({ key: 'R_BC', label: 'Reverse -- B Current (A)', dir: 'R' });
        chartNames.push({ key: 'R_BV', label: 'Reverse -- B Voltage (V)', dir: 'R' });
    }
    for (var ci = 0; ci < chartNames.length; ci++) {
        h += '<div class="pm-wave-box" data-dir="' + chartNames[ci].dir + '"><div class="pm-chart-wrap"><div id="pmWave_' + assetId + '_' + chartNames[ci].key + '"></div></div></div>';
    }
    h += '</div></div>';

    h += '</div>'; // pm-card-wrapper

    $('#pmMainContainer').append(h);
    $('#pmCard_' + assetId).hide().fadeIn(400);
}

function updatePmCard(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var $card = $('#pmCard_' + assetId);
    if (!$card.length) return;

    var pm = getPmStructuredData(assetId);
    if (!pm) return;

    // Debug: Log all RDPMS attributes to see what's available
    console.log('[PM Debug] Asset:', assetId, 'RDPMS attrs:', pm.RDPMS);

    // 1. Render badges
    if (typeof renderPmBadges === 'function') {
        renderPmBadges(assetId, pm.DataLogger);
    }

    // 2. First extract NWKR/RWKR values from RDPMS to determine direction
    // ================================================================
    // DIRECTION FORMULA (matching backend C# logic):
    // - Get MAX value from all NWKR attributes (IDs: 25, 27, 576, 578)
    // - Get MAX value from all RWKR attributes (IDs: 26, 28, 577, 579)
    // - If NWKR_Max > RWKR_Max AND NWKR_Max > 10 → NORMAL
    // - If RWKR_Max > NWKR_Max AND RWKR_Max > 10 → REVERSE
    // - Else → NORMAL (default)
    // ================================================================
    var PM_DIRECTION_THRESHOLD = 10.0; // Threshold for direction detection

    // Collect all NWKR and RWKR values
    var nwkrValues = { A: [], B: [] };  // Normal Working Relay values
    var rwkrValues = { A: [], B: [] };  // Reverse Working Relay values

    // NWKR Attribute IDs (Normal indicators)
    var NWKR_IDS = [25, 27, 576, 578];
    // RWKR Attribute IDs (Reverse indicators)
    var RWKR_IDS = [26, 28, 577, 579];

    // A-End IDs
    var A_END_NWKR_IDS = [25, 576];   // A End NWKR, A End NWKR (Loc)
    var A_END_RWKR_IDS = [26, 577];   // A End RWKR, A End RWKR (Loc)
    // B-End IDs
    var B_END_NWKR_IDS = [27, 578];   // B End NWKR, B End NWKR (Loc)
    var B_END_RWKR_IDS = [28, 579];   // B End RWKR, B End RWKR (Loc)

    // Pre-scan RDPMS for NWKR/RWKR values
    var preScanVoltages = {
        A_NWKR: null, A_RWKR: null, A_NWKR_Loc: null, A_RWKR_Loc: null,
        B_NWKR: null, B_RWKR: null, B_NWKR_Loc: null, B_RWKR_Loc: null
    };

    for (var rk in pm.RDPMS) {
        if (!pm.RDPMS.hasOwnProperty(rk)) continue;
        var attrData = pm.RDPMS[rk];
        var rv = parseFloat(attrData.value);
        if (isNaN(rv)) continue;
        var rkLow = rk.toLowerCase();
        var attrId = parseInt(attrData.AssetAttributeId || attrData.AttrId) || 0;

        // Collect values by AttrId for accurate direction detection
        // A End - NWKR (AttrId: 25)
        if (attrId === 25 || (rkLow === 'a end - nwkr')) {
            preScanVoltages.A_NWKR = rv;
            nwkrValues.A.push(rv);
        }
        // A End - NWKR (Loc) (AttrId: 576)
        else if (attrId === 576 || (rkLow === 'a end - nwkr (loc)')) {
            preScanVoltages.A_NWKR_Loc = rv;
            nwkrValues.A.push(rv);
        }
        // A End - RWKR (AttrId: 26)
        else if (attrId === 26 || (rkLow === 'a end - rwkr')) {
            preScanVoltages.A_RWKR = rv;
            rwkrValues.A.push(rv);
        }
        // A End - RWKR (Loc) (AttrId: 577)
        else if (attrId === 577 || (rkLow === 'a end - rwkr (loc)')) {
            preScanVoltages.A_RWKR_Loc = rv;
            rwkrValues.A.push(rv);
        }
        // B End - NWKR (AttrId: 27)
        else if (attrId === 27 || (rkLow === 'b end - nwkr')) {
            preScanVoltages.B_NWKR = rv;
            nwkrValues.B.push(rv);
        }
        // B End - NWKR (Loc) (AttrId: 578)
        else if (attrId === 578 || (rkLow === 'b end - nwkr (loc)')) {
            preScanVoltages.B_NWKR_Loc = rv;
            nwkrValues.B.push(rv);
        }
        // B End - RWKR (AttrId: 28)
        else if (attrId === 28 || (rkLow === 'b end - rwkr')) {
            preScanVoltages.B_RWKR = rv;
            rwkrValues.B.push(rv);
        }
        // B End - RWKR (Loc) (AttrId: 579)
        else if (attrId === 579 || (rkLow === 'b end - rwkr (loc)')) {
            preScanVoltages.B_RWKR_Loc = rv;
            rwkrValues.B.push(rv);
        }
        // Flexible matching fallback
        else if (rkLow.indexOf('a end') > -1 || rkLow.indexOf('a-end') > -1) {
            if (rkLow.indexOf('nwkr') > -1) {
                if (rkLow.indexOf('loc') > -1) {
                    preScanVoltages.A_NWKR_Loc = rv;
                } else {
                    preScanVoltages.A_NWKR = rv;
                }
                nwkrValues.A.push(rv);
            } else if (rkLow.indexOf('rwkr') > -1) {
                if (rkLow.indexOf('loc') > -1) {
                    preScanVoltages.A_RWKR_Loc = rv;
                } else {
                    preScanVoltages.A_RWKR = rv;
                }
                rwkrValues.A.push(rv);
            }
        }
        else if (rkLow.indexOf('b end') > -1 || rkLow.indexOf('b-end') > -1) {
            if (rkLow.indexOf('nwkr') > -1) {
                if (rkLow.indexOf('loc') > -1) {
                    preScanVoltages.B_NWKR_Loc = rv;
                } else {
                    preScanVoltages.B_NWKR = rv;
                }
                nwkrValues.B.push(rv);
            } else if (rkLow.indexOf('rwkr') > -1) {
                if (rkLow.indexOf('loc') > -1) {
                    preScanVoltages.B_RWKR_Loc = rv;
                } else {
                    preScanVoltages.B_RWKR = rv;
                }
                rwkrValues.B.push(rv);
            }
        }
    }

    console.log('[PM Direction] Pre-scan voltages:', preScanVoltages);
    console.log('[PM Direction] NWKR values - A:', nwkrValues.A, '| B:', nwkrValues.B);
    console.log('[PM Direction] RWKR values - A:', rwkrValues.A, '| B:', rwkrValues.B);
    console.log('[PM Direction] Threshold:', PM_DIRECTION_THRESHOLD);

    // ================================================================
    // DIRECTION DETECTION FOR A-END
    // Formula: Compare MAX(NWKR values) vs MAX(RWKR values)
    // If NWKR_Max > RWKR_Max AND NWKR_Max > 10 → NORMAL
    // If RWKR_Max > NWKR_Max AND RWKR_Max > 10 → REVERSE
    // ================================================================
    var A_NWKR_Max = nwkrValues.A.length > 0 ? Math.max.apply(null, nwkrValues.A) : 0;
    var A_RWKR_Max = rwkrValues.A.length > 0 ? Math.max.apply(null, rwkrValues.A) : 0;

    var dirA = 'NORMAL'; // Default
    if (A_NWKR_Max > A_RWKR_Max && A_NWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirA = 'NORMAL';
        console.log('[PM Direction] A-End: NORMAL (NWKR_Max=' + A_NWKR_Max.toFixed(2) + ' > RWKR_Max=' + A_RWKR_Max.toFixed(2) + ' AND > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (A_RWKR_Max > A_NWKR_Max && A_RWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirA = 'REVERSE';
        console.log('[PM Direction] A-End: REVERSE (RWKR_Max=' + A_RWKR_Max.toFixed(2) + ' > NWKR_Max=' + A_NWKR_Max.toFixed(2) + ' AND > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (A_NWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirA = 'NORMAL';
        console.log('[PM Direction] A-End: NORMAL (NWKR_Max=' + A_NWKR_Max.toFixed(2) + ' > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (A_RWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirA = 'REVERSE';
        console.log('[PM Direction] A-End: REVERSE (RWKR_Max=' + A_RWKR_Max.toFixed(2) + ' > ' + PM_DIRECTION_THRESHOLD + ')');
    } else {
        // Fallback: use OperationTime if NWKR/RWKR not above threshold
        var nAcOT = pm.Normal.AC.OperationTime ? parseFloat(pm.Normal.AC.OperationTime.value) || 0 : 0;
        var rAcOT = pm.Reverse.AC.OperationTime ? parseFloat(pm.Reverse.AC.OperationTime.value) || 0 : 0;
        var nAcTs = pm.Normal.AC.OperationTime ? (pm.Normal.AC.OperationTime.timestamp || '') : '';
        var rAcTs = pm.Reverse.AC.OperationTime ? (pm.Reverse.AC.OperationTime.timestamp || '') : '';

        if (rAcTs && nAcTs) {
            dirA = (rAcTs > nAcTs && rAcOT > 0) ? 'REVERSE' : 'NORMAL';
        } else {
            dirA = (A_RWKR_Max >= A_NWKR_Max && A_RWKR_Max > 0) ? 'REVERSE' : 'NORMAL';
        }
        console.log('[PM Direction] A-End: ' + dirA + ' (fallback - both below threshold ' + PM_DIRECTION_THRESHOLD + ')');
    }

    // ================================================================
    // DIRECTION DETECTION FOR B-END
    // Same formula as A-End
    // ================================================================
    var B_NWKR_Max = nwkrValues.B.length > 0 ? Math.max.apply(null, nwkrValues.B) : 0;
    var B_RWKR_Max = rwkrValues.B.length > 0 ? Math.max.apply(null, rwkrValues.B) : 0;

    var dirB = 'NORMAL'; // Default
    if (B_NWKR_Max > B_RWKR_Max && B_NWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirB = 'NORMAL';
        console.log('[PM Direction] B-End: NORMAL (NWKR_Max=' + B_NWKR_Max.toFixed(2) + ' > RWKR_Max=' + B_RWKR_Max.toFixed(2) + ' AND > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (B_RWKR_Max > B_NWKR_Max && B_RWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirB = 'REVERSE';
        console.log('[PM Direction] B-End: REVERSE (RWKR_Max=' + B_RWKR_Max.toFixed(2) + ' > NWKR_Max=' + B_NWKR_Max.toFixed(2) + ' AND > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (B_NWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirB = 'NORMAL';
        console.log('[PM Direction] B-End: NORMAL (NWKR_Max=' + B_NWKR_Max.toFixed(2) + ' > ' + PM_DIRECTION_THRESHOLD + ')');
    } else if (B_RWKR_Max > PM_DIRECTION_THRESHOLD) {
        dirB = 'REVERSE';
        console.log('[PM Direction] B-End: REVERSE (RWKR_Max=' + B_RWKR_Max.toFixed(2) + ' > ' + PM_DIRECTION_THRESHOLD + ')');
    } else {
        // Fallback: use OperationTime if NWKR/RWKR not above threshold
        var nBcOT = pm.Normal.BC.OperationTime ? parseFloat(pm.Normal.BC.OperationTime.value) || 0 : 0;
        var rBcOT = pm.Reverse.BC.OperationTime ? parseFloat(pm.Reverse.BC.OperationTime.value) || 0 : 0;
        var nBcTs = pm.Normal.BC.OperationTime ? (pm.Normal.BC.OperationTime.timestamp || '') : '';
        var rBcTs = pm.Reverse.BC.OperationTime ? (pm.Reverse.BC.OperationTime.timestamp || '') : '';

        if (rBcTs && nBcTs) {
            dirB = (rBcTs > nBcTs && rBcOT > 0) ? 'REVERSE' : 'NORMAL';
        } else {
            dirB = (B_RWKR_Max >= B_NWKR_Max && B_RWKR_Max > 0) ? 'REVERSE' : 'NORMAL';
        }
        console.log('[PM Direction] B-End: ' + dirB + ' (fallback - both below threshold ' + PM_DIRECTION_THRESHOLD + ')');
    }

    // Update direction badges
    var $badgeA = $('#pmDirBadgeA_' + assetId);
    if ($badgeA.length) $badgeA.text('POINT IN ' + dirA).removeClass('normal reverse').addClass(dirA === 'NORMAL' ? 'normal' : 'reverse');

    var $badgeB = $('#pmDirBadgeB_' + assetId);
    if ($badgeB.length) $badgeB.text('POINT IN ' + dirB).removeClass('normal reverse').addClass(dirB === 'NORMAL' ? 'normal' : 'reverse');

    // 3. Get voltage values from RDPMS
    // Structure based on your data:
    // "A End - RWKR" (AttrId: 26) = Reverse Relay voltage
    // "A End - RWKR (Loc)" (AttrId: 577) = Reverse Local voltage
    // "A End - NWKR" (AttrId: 25) = Normal Relay voltage
    // "A End - NWKR (Loc)" (AttrId: 576) = Normal Local voltage
    // Same pattern for B End

    // Initialize voltage values
    var voltages = {
        // A End
        A_NWKR: null,      // Normal Relay (AttrId: 25)
        A_NWKR_Loc: null,  // Normal Local (AttrId: 576)
        A_RWKR: null,      // Reverse Relay (AttrId: 26)
        A_RWKR_Loc: null,  // Reverse Local (AttrId: 577)
        // B End
        B_NWKR: null,      // Normal Relay
        B_NWKR_Loc: null,  // Normal Local
        B_RWKR: null,      // Reverse Relay
        B_RWKR_Loc: null   // Reverse Local
    };

    // Search through RDPMS attributes
    for (var rk in pm.RDPMS) {
        if (!pm.RDPMS.hasOwnProperty(rk)) continue;

        var attrData = pm.RDPMS[rk];
        var rv = parseFloat(attrData.value);
        if (isNaN(rv)) continue;

        var rkLow = rk.toLowerCase();
        // FIX: was `attrData.AttrId` (raw — could be a string from JSON), so strict
        // equality checks like `attrId === 25` silently failed for string "25".
        // Use parseInt on AssetAttributeId || AttrId, matching the pre-scan at line
        // 13175, so all downstream `=== 25 / 26 / 27 …` comparisons are numeric.
        var attrId = parseInt(attrData.AssetAttributeId || attrData.AttrId) || 0;

        console.log('[PM Voltage] Checking:', rk, '| AttrId:', attrId, '| Value:', rv);

        // ===== A END VOLTAGES =====
        // Check by exact name pattern or AttrId

        // A End - NWKR (Normal Relay) - AttrId: 25
        if ((rkLow === 'a end - nwkr') || attrId === 25) {
            voltages.A_NWKR = rv;
            console.log('[PM] ✓ A End NWKR (Normal Relay):', rv);
        }
        // A End - NWKR (Loc) (Normal Local) - AttrId: 576
        else if ((rkLow === 'a end - nwkr (loc)') || attrId === 576) {
            voltages.A_NWKR_Loc = rv;
            console.log('[PM] ✓ A End NWKR Loc (Normal Local):', rv);
        }
        // A End - RWKR (Reverse Relay) - AttrId: 26
        else if ((rkLow === 'a end - rwkr') || attrId === 26) {
            voltages.A_RWKR = rv;
            console.log('[PM] ✓ A End RWKR (Reverse Relay):', rv);
        }
        // A End - RWKR (Loc) (Reverse Local) - AttrId: 577
        else if ((rkLow === 'a end - rwkr (loc)') || attrId === 577) {
            voltages.A_RWKR_Loc = rv;
            console.log('[PM] ✓ A End RWKR Loc (Reverse Local):', rv);
        }

        // ===== B END VOLTAGES =====
        // B End - NWKR (Normal Relay)
        else if ((rkLow === 'b end - nwkr') || attrId === 27) {
            voltages.B_NWKR = rv;
            console.log('[PM] ✓ B End NWKR (Normal Relay):', rv);
        }
        // B End - NWKR (Loc) (Normal Local)
        else if ((rkLow === 'b end - nwkr (loc)') || attrId === 578) {
            voltages.B_NWKR_Loc = rv;
            console.log('[PM] ✓ B End NWKR Loc (Normal Local):', rv);
        }
        // B End - RWKR (Reverse Relay)
        else if ((rkLow === 'b end - rwkr') || attrId === 28) {
            voltages.B_RWKR = rv;
            console.log('[PM] ✓ B End RWKR (Reverse Relay):', rv);
        }
        // B End - RWKR (Loc) (Reverse Local)
        else if ((rkLow === 'b end - rwkr (loc)') || attrId === 579) {
            voltages.B_RWKR_Loc = rv;
            console.log('[PM] ✓ B End RWKR Loc (Reverse Local):', rv);
        }

        // ===== FLEXIBLE MATCHING (fallback) =====
        else {
            // A End patterns
            if (rkLow.indexOf('a end') > -1 || rkLow.indexOf('a-end') > -1) {
                if (rkLow.indexOf('nwkr') > -1 && rkLow.indexOf('loc') > -1) {
                    voltages.A_NWKR_Loc = rv;
                    console.log('[PM] ✓ A End NWKR Loc (flexible):', rv);
                } else if (rkLow.indexOf('nwkr') > -1) {
                    voltages.A_NWKR = rv;
                    console.log('[PM] ✓ A End NWKR (flexible):', rv);
                } else if (rkLow.indexOf('rwkr') > -1 && rkLow.indexOf('loc') > -1) {
                    voltages.A_RWKR_Loc = rv;
                    console.log('[PM] ✓ A End RWKR Loc (flexible):', rv);
                } else if (rkLow.indexOf('rwkr') > -1) {
                    voltages.A_RWKR = rv;
                    console.log('[PM] ✓ A End RWKR (flexible):', rv);
                }
            }
            // B End patterns
            else if (rkLow.indexOf('b end') > -1 || rkLow.indexOf('b-end') > -1) {
                if (rkLow.indexOf('nwkr') > -1 && rkLow.indexOf('loc') > -1) {
                    voltages.B_NWKR_Loc = rv;
                    console.log('[PM] ✓ B End NWKR Loc (flexible):', rv);
                } else if (rkLow.indexOf('nwkr') > -1) {
                    voltages.B_NWKR = rv;
                    console.log('[PM] ✓ B End NWKR (flexible):', rv);
                } else if (rkLow.indexOf('rwkr') > -1 && rkLow.indexOf('loc') > -1) {
                    voltages.B_RWKR_Loc = rv;
                    console.log('[PM] ✓ B End RWKR Loc (flexible):', rv);
                } else if (rkLow.indexOf('rwkr') > -1) {
                    voltages.B_RWKR = rv;
                    console.log('[PM] ✓ B End RWKR (flexible):', rv);
                }
            }
        }
    }

    console.log('[PM Voltages Object]:', voltages);

    // 4. Determine which voltages to display based on direction
    // For A-end: if direction is NORMAL, show NWKR values; if REVERSE, show RWKR values
    // For B-end: same logic

    var displayA_Relay, displayA_Loc, displayB_Relay, displayB_Loc;

    if (dirA === 'NORMAL') {
        displayA_Relay = voltages.A_NWKR;
        displayA_Loc = voltages.A_NWKR_Loc;
    } else {
        displayA_Relay = voltages.A_RWKR;
        displayA_Loc = voltages.A_RWKR_Loc;
    }

    if (dirB === 'NORMAL') {
        displayB_Relay = voltages.B_NWKR;
        displayB_Loc = voltages.B_NWKR_Loc;
    } else {
        displayB_Relay = voltages.B_RWKR;
        displayB_Loc = voltages.B_RWKR_Loc;
    }

    // Fallback: If direction-specific value is null/0, try the other direction
    if ((displayA_Relay === null || displayA_Relay === 0) && voltages.A_RWKR !== null && voltages.A_RWKR !== 0) {
        displayA_Relay = voltages.A_RWKR;
    }
    if ((displayA_Relay === null || displayA_Relay === 0) && voltages.A_NWKR !== null && voltages.A_NWKR !== 0) {
        displayA_Relay = voltages.A_NWKR;
    }

    if ((displayA_Loc === null || displayA_Loc === 0) && voltages.A_RWKR_Loc !== null && voltages.A_RWKR_Loc !== 0) {
        displayA_Loc = voltages.A_RWKR_Loc;
    }
    if ((displayA_Loc === null || displayA_Loc === 0) && voltages.A_NWKR_Loc !== null && voltages.A_NWKR_Loc !== 0) {
        displayA_Loc = voltages.A_NWKR_Loc;
    }

    if ((displayB_Relay === null || displayB_Relay === 0) && voltages.B_RWKR !== null && voltages.B_RWKR !== 0) {
        displayB_Relay = voltages.B_RWKR;
    }
    if ((displayB_Relay === null || displayB_Relay === 0) && voltages.B_NWKR !== null && voltages.B_NWKR !== 0) {
        displayB_Relay = voltages.B_NWKR;
    }

    if ((displayB_Loc === null || displayB_Loc === 0) && voltages.B_RWKR_Loc !== null && voltages.B_RWKR_Loc !== 0) {
        displayB_Loc = voltages.B_RWKR_Loc;
    }
    if ((displayB_Loc === null || displayB_Loc === 0) && voltages.B_NWKR_Loc !== null && voltages.B_NWKR_Loc !== 0) {
        displayB_Loc = voltages.B_NWKR_Loc;
    }

    // Format display values
    var nwkrValA = (displayA_Relay !== null) ? displayA_Relay.toFixed(2) : '--';
    var locValA = (displayA_Loc !== null) ? displayA_Loc.toFixed(2) : '--';
    var nwkrValB = (displayB_Relay !== null) ? displayB_Relay.toFixed(2) : '--';
    var locValB = (displayB_Loc !== null) ? displayB_Loc.toFixed(2) : '--';


    // Update voltage header displays + fix label to match actual direction
    // dirA/dirB = 'NORMAL' or 'REVERSE' -- label must say NWKR or RWKR accordingly
    $('#pmKrLabelA_' + assetId).text(dirA === 'REVERSE' ? 'RWKR' : 'NWKR');
    $('#pmDirLocLabelA_' + assetId).text(dirA === 'REVERSE' ? 'Reverse' : 'Normal');
    $('#pmKrLabelB_' + assetId).text(dirB === 'REVERSE' ? 'RWKR' : 'NWKR');
    $('#pmDirLocLabelB_' + assetId).text(dirB === 'REVERSE' ? 'Reverse' : 'Normal');

    $('#pmVnwkrA_' + assetId).text(nwkrValA);
    $('#pmVdcA_' + assetId).text(locValA);
    $('#pmVnwkrB_' + assetId).text(nwkrValB);
    $('#pmVdcB_' + assetId).text(locValB);

    console.log('[PM Voltage Display] Direction A:', dirA, '| A Relay:', nwkrValA, '| A Loc:', locValA);
    console.log('[PM Voltage Display] Direction B:', dirB, '| B Relay:', nwkrValB, '| B Loc:', locValB);

    // 5. Update Avg values in column headers
    var aDirKey = dirA === 'REVERSE' ? 'Reverse' : 'Normal';
    var bDirKey = dirB === 'REVERSE' ? 'Reverse' : 'Normal';

    $('#pmIptAvgA_' + assetId).text(pm[aDirKey].AC.Avg ? pmFmt(pm[aDirKey].AC.Avg.value) : '--');
    $('#pmVptAvgA_' + assetId).text(pm[aDirKey].AV.Avg ? pmFmt(pm[aDirKey].AV.Avg.value, 1) : '--');
    $('#pmTptAvgA_' + assetId).text(pm[aDirKey].AC.OperationTime ? pmFmtInt(pm[aDirKey].AC.OperationTime.value) : '--');

    $('#pmIptAvgB_' + assetId).text(pm[bDirKey].BC.Avg ? pmFmt(pm[bDirKey].BC.Avg.value) : '--');
    $('#pmVptAvgB_' + assetId).text(pm[bDirKey].BV.Avg ? pmFmt(pm[bDirKey].BV.Avg.value, 1) : '--');
    $('#pmTptAvgB_' + assetId).text(pm[bDirKey].BC.OperationTime ? pmFmtInt(pm[bDirKey].BC.OperationTime.value) : '--');

    // 6. Build data row
    if (typeof buildPmDataRow === 'function') {
        buildPmDataRow(assetId, pm);
    }

    // 7. Flash animation
    $card.removeClass('pm-card-flash-anim');
    if ($card[0]) {
        void $card[0].offsetWidth;
        $card.addClass('pm-card-flash-anim');
        setTimeout(function () { $card.removeClass('pm-card-flash-anim'); }, 1200);
    }
}


console.log('[TelemetryLive Complete Fix] Loaded with AliasName support.');


// ================================================================
// BUILD DATA ROW -- 11 columns matching Image 2 layout
// Date | Name | Direction | New Combine (if IsSeriesOperation) | A IPT | A VPT | A TPT | B IPT | B VPT | B TPT
// Each row = one direction event (Normal or Reverse)
// A-end = AC(current) + AV(voltage), B-end = BC(current) + BV(voltage)
// ================================================================
function buildPmDataRow(assetId, pm) {
    var asset = wsLiveData[assetId];
    if (!asset) return; // Safety check

    // Add PT- prefix to Point Machine asset name
    var baseName = asset.AssetName || assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

    // Get which ends are shown in the table
    var $tbody = $('#pmTbody_' + assetId);
    var showA = $tbody.attr('data-show-a') === '1';
    var showB = $tbody.attr('data-show-b') === '1';
    var showCombine = $tbody.attr('data-show-combine') === '1';

    // Fallback: check pmEndInfo
    if (!showA && !showB && window.pmEndInfo && window.pmEndInfo[assetId]) {
        var ends = window.pmEndInfo[assetId];
        showA = ends.hasA || (!ends.hasA && !ends.hasB);
        showB = ends.hasB || (!ends.hasA && !ends.hasB);
    }

    // Fallback: check pmCombineColumnStatus
    if (!showCombine && window.pmCombineColumnStatus && window.pmCombineColumnStatus[assetId]) {
        showCombine = true;
    }

    // Default to both if nothing specified
    if (!showA && !showB) {
        showA = true;
        showB = true;
    }

    // Get latest timestamp ONLY from operation IDs (not indication IDs)
    // Normal operation IDs: 1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002
    // Reverse operation IDs: 6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002
    var PM_OPERATION_IDS = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002,
        6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];
    var ts = '';
    var latestTime = 0;
    for (var ak in asset.attrs) {
        var attrData = asset.attrs[ak];
        var assetAttrId = parseInt(attrData.AssetAttributeId || attrData.AttrId || 0);

        // Only consider timestamps from operation IDs
        if (PM_OPERATION_IDS.indexOf(assetAttrId) !== -1) {
            var t = attrData.TimestampDevice || attrData.Timestamp;
            if (t && t !== '0001-01-01T00:00:00+00:00' && t.indexOf('0001-01-01') === -1) {
                try {
                    var tTime = new Date(t).getTime();
                    if (tTime > latestTime) {
                        latestTime = tTime;
                        ts = t;
                    }
                } catch (e) { }
            }
        }
    }
    var dateStr = '--';
    if (ts) {
        try {
            var dt = new Date(ts);
            dateStr = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' ' + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
        } catch (e) { }
    }

    // Determine direction using centralized function (matching backend formula)
    var asset = wsLiveData[assetId];
    var dirResult = determinePmDirection(asset, pm);
    var isReverse = (dirResult.direction === 'REVERSE');
    var dirText = isReverse ? 'Reverse Operation' : 'Normal Operation';
    var useDir = isReverse ? 'Reverse' : 'Normal';

    // A-end values: AC (current), AV (voltage)
    var aAcMax = pm[useDir].AC.Max ? pmFmt(pm[useDir].AC.Max.value) : '--';
    var aAcAvg = pm[useDir].AC.Avg ? pmFmt(pm[useDir].AC.Avg.value) : '--';
    var aAcCount = pm[useDir].AC.Count ? pmFmtInt(pm[useDir].AC.Count.value) : '--';
    var aAcOTVal = pm[useDir].AC.OperationTime ? pmFmtInt(pm[useDir].AC.OperationTime.value) : '--';
    var aAvAvg = pm[useDir].AV.Avg ? pmFmt(pm[useDir].AV.Avg.value, 2) : '--';

    // B-end values: BC (current), BV (voltage)
    var bBcMax = pm[useDir].BC.Max ? pmFmt(pm[useDir].BC.Max.value) : '--';
    var bBcAvg = pm[useDir].BC.Avg ? pmFmt(pm[useDir].BC.Avg.value) : '--';
    var bBcCount = pm[useDir].BC.Count ? pmFmtInt(pm[useDir].BC.Count.value) : '--';
    var bBcOTVal = pm[useDir].BC.OperationTime ? pmFmtInt(pm[useDir].BC.OperationTime.value) : '--';
    var bBvAvg = pm[useDir].BV.Avg ? pmFmt(pm[useDir].BV.Avg.value, 2) : '--';

    // Total OT for Combine column
    var totalOT = 0;
    if (showA) totalOT += (parseInt(aAcOTVal) || 0);
    if (showB) totalOT += (parseInt(bBcOTVal) || 0);

    // Initialize history if not exists
    if (!pmEventHistory[assetId]) {
        pmEventHistory[assetId] = [];
    }
    var hist = pmEventHistory[assetId];

    // Create current row data object
    var hasRealData = (showA && (aAcMax !== '--' || aAcAvg !== '--')) ||
        (showB && (bBcMax !== '--' || bBcAvg !== '--'));
    var currentRow = {
        date: dateStr,
        name: name,
        dir: dirText,
        operationType: useDir,
        timestampDevice: ts,
        aAcMax: aAcMax,
        aAcAvg: aAcAvg,
        aAcCount: aAcCount,
        aAcOT: aAcOTVal,
        aAvAvg: aAvAvg,
        bBcMax: bBcMax,
        bBcAvg: bBcAvg,
        bBcCount: bBcCount,
        bBcOT: bBcOTVal,
        bBvAvg: bBvAvg,
        totalOT: totalOT,
        showA: showA,
        showB: showB,
        hasRealData: hasRealData
    };

    // ── UNIQUE TIMESTAMPDEVICE RULE ──
    // One row per unique second -- same logic as processPmOperationMessage.
    // If this exact second already exists in history → update values in-place.
    // If not found → add new row only if it has real data.
    var tsBdMs = ts ? pmTsToSecMs(ts) : 0;
    var bdIdx = -1;
    if (tsBdMs > 0) {
        for (var bhi = hist.length - 1; bhi >= 0; bhi--) {
            if (hist[bhi].timestampDevice &&
                pmTsToSecMs(hist[bhi].timestampDevice) === tsBdMs) {
                bdIdx = bhi;
                break;
            }
        }
    }

    if (bdIdx !== -1) {
        // Already in history -- refresh values only, never add a new row
        hist[bdIdx].date = dateStr;
        hist[bdIdx].aAcMax = aAcMax; hist[bdIdx].aAcAvg = aAcAvg;
        hist[bdIdx].aAcCount = aAcCount; hist[bdIdx].aAcOT = aAcOTVal;
        hist[bdIdx].aAvAvg = aAvAvg;
        hist[bdIdx].bBcMax = bBcMax; hist[bdIdx].bBcAvg = bBcAvg;
        hist[bdIdx].bBcCount = bBcCount; hist[bdIdx].bBcOT = bBcOTVal;
        hist[bdIdx].bBvAvg = bBvAvg; hist[bdIdx].totalOT = totalOT;
    } else if (hasRealData && tsBdMs > 0) {
        // Real data arrived — drop any placeholder seed rows added before it
        for (var si = hist.length - 1; si >= 0; si--) {
            if (!hist[si].hasRealData) hist.splice(si, 1);
        }
        hist.push(currentRow);
        if (hist.length > PM_MAX_HISTORY) hist.shift();
    } else if (hist.length === 0 && hasRealData) {
        // Only seed a row when it actually carries data
        hist.push(currentRow);
    }

    // Build tbody HTML from history (newest first)
    var rows = '';
    for (var i = hist.length - 1; i >= 0; i--) {
        var r = hist[i];
        var isLatest = (i === hist.length - 1);
        rows += '<tr' + (isLatest ? ' class="pm-val-flash-anim"' : '') + '>';
        // Col 1: Date
        rows += '<td class="pm-date-cell">' + r.date + '</td>';
        // Col 2: Name
        rows += '<td>' + pmEsc(r.name) + '</td>';
        // Col 3: Direction
        rows += '<td>' + r.dir + '</td>';
        // Col 4: New Combine (chart links + total OT) - only if showCombine is true
        if (showCombine) {
            rows += '<td><a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + (r.dir === 'Normal Operation' ? 'N' : 'R') + '_AC\')">C</a> / <a href="javascript:void(0)" onclick="pmShowChart(\'' + assetId + '\',\'' + (r.dir === 'Normal Operation' ? 'N' : 'R') + '_AV\')">V</a> T(' + r.totalOT + ')</td>';
        }

        // Direction prefix for chart type
        var dirPrefix = (r.dir === 'Normal Operation') ? 'N' : 'R';

        // Check if values have real data based on count > 0 (not "--" and not "0")
        // A count of "0" or "--" means no array data to show
        var hasAcData = r.aAcCount && r.aAcCount !== '--' && parseInt(r.aAcCount) > 0;
        var hasAvData = r.aAvAvg && r.aAvAvg !== '--' && r.aAvAvg !== '';
        var hasBcData = r.bBcCount && r.bBcCount !== '--' && parseInt(r.bBcCount) > 0;
        var hasBvData = r.bBvAvg && r.bBvAvg !== '--' && r.bBvAvg !== '';

        // A-end columns (only if A is shown)
        if (showA) {
            // Col: A Current (Max/Avg)(Count) - Clickable only if count > 0
            if (hasAcData) {
                rows += '<td class="col-a"><a href="javascript:void(0)" class="pm-current-link" onclick="pmShowSingleArrayGraph(\'' + assetId + '\',\'' + dirPrefix + '\',\'AC\')" title="Click to view A Current waveform"><span class="pm-current-val">' + r.aAcMax + ' / ' + r.aAcAvg + ' (' + r.aAcCount + ')</span></a></td>';
            } else {
                // Show non-clickable value (use actual values if available, else "--")
                var acDisplay = (r.aAcMax && r.aAcMax !== '--') ? (r.aAcMax + ' / ' + r.aAcAvg + ' (' + r.aAcCount + ')') : '-- / -- (0)';
                rows += '<td class="col-a"><span class="pm-current-val pm-no-data" style="cursor:default;opacity:0.6;">' + acDisplay + '</span></td>';
            }
            // Col: A VPT110 DC LOC - Clickable only if data exists
            if (hasAvData) {
                rows += '<td class="col-a"><a href="javascript:void(0)" class="pm-voltage-link" onclick="pmShowSingleArrayGraph(\'' + assetId + '\',\'' + dirPrefix + '\',\'AV\')" title="Click to view A Voltage waveform"><span class="pm-voltage-val">' + r.aAvAvg + '</span></a></td>';
            } else {
                rows += '<td class="col-a"><span class="pm-voltage-val pm-no-data" style="cursor:default;opacity:0.6;">' + (r.aAvAvg || '--') + '</span></td>';
            }
            // Col: A TPT (ms)
            rows += '<td class="col-a">' + r.aAcOT + '</td>';
        }

        // B-end columns (only if B is shown)
        if (showB) {
            // Col: B Current (Max/Avg)(Count) - Clickable only if count > 0
            if (hasBcData) {
                rows += '<td class="col-b"><a href="javascript:void(0)" class="pm-current-link" onclick="pmShowSingleArrayGraph(\'' + assetId + '\',\'' + dirPrefix + '\',\'BC\')" title="Click to view B Current waveform"><span class="pm-current-val-b">' + r.bBcMax + ' / ' + r.bBcAvg + ' (' + r.bBcCount + ')</span></a></td>';
            } else {
                var bcDisplay = (r.bBcMax && r.bBcMax !== '--') ? (r.bBcMax + ' / ' + r.bBcAvg + ' (' + r.bBcCount + ')') : '-- / -- (0)';
                rows += '<td class="col-b"><span class="pm-current-val-b pm-no-data" style="cursor:default;opacity:0.6;">' + bcDisplay + '</span></td>';
            }
            // Col: B VPT110 DC LOC - Clickable only if data exists
            if (hasBvData) {
                rows += '<td class="col-b"><a href="javascript:void(0)" class="pm-voltage-link" onclick="pmShowSingleArrayGraph(\'' + assetId + '\',\'' + dirPrefix + '\',\'BV\')" title="Click to view B Voltage waveform"><span class="pm-voltage-val">' + r.bBvAvg + '</span></a></td>';
            } else {
                rows += '<td class="col-b"><span class="pm-voltage-val pm-no-data" style="cursor:default;opacity:0.6;">' + (r.bBvAvg || '--') + '</span></td>';
            }
            // Col: B TPT (ms)
            rows += '<td class="col-b">' + r.bBcOT + '</td>';
        }
        rows += '</tr>';
    }

    // Only update DOM if we have rows (prevent clearing)
    if ($tbody.length && rows) {
        $tbody.html(rows);
    }
}

// Show single array graph (AC, AV, BC, BV) with TimestampDevice on X-axis
// direction: 'N' or 'R', type: 'AC', 'AV', 'BC', 'BV'

// Helper: convert '#rrggbb' -> 'rgba(r,g,b,a)' -- ECharts v4 compatible
// ECharts v4 does NOT support 8-char hex (#rrggbbaa), must use rgba()
function _hexToRgba(hex, alpha) {
    if (!hex || hex.charAt(0) !== '#') return hex;
    var h = hex.replace('#', '');
    if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
    var r = parseInt(h.substring(0, 2), 16);
    var g = parseInt(h.substring(2, 4), 16);
    var b = parseInt(h.substring(4, 6), 16);
    return 'rgba(' + r + ',' + g + ',' + b + ',' + (alpha !== undefined ? alpha : 1) + ')';
}

function pmShowSingleArrayGraph(assetId, direction, type) {
    var asset = wsLiveData[assetId];
    var baseName = asset ? (asset.AssetName || assetId) : assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

    // Calculate Attribute ID
    // Base: AC=1000/6000, AV=2000/7000, BC=3000/8000, BV=4000/9000, Array offset=1
    var baseMap = {
        'AC': direction === 'R' ? 6000 : 1000,
        'AV': direction === 'R' ? 7000 : 2000,
        'BC': direction === 'R' ? 8000 : 3000,
        'BV': direction === 'R' ? 9000 : 4000
    };
    var attrId = baseMap[type] + 1; // +1 for Array

    var dirLabel = direction === 'R' ? 'Reverse' : 'Normal';
    var typeLabels = {
        'AC': 'A Current (A)',
        'AV': 'A Voltage (V)',
        'BC': 'B Current (A)',
        'BV': 'B Voltage (V)'
    };
    var units = { 'AC': 'A', 'AV': 'V', 'BC': 'A', 'BV': 'V' };
    var colors = { 'AC': '#2563eb', 'AV': '#059669', 'BC': '#dc2626', 'BV': '#d97706' };

    var title = name + ' -- ' + dirLabel + ' ' + typeLabels[type];
    var unit = units[type];
    var color = colors[type];

    // Show loading modal
    var modalHtml = '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1200px;width:96%;">' +
        '<div class="rdpms-graph-header" style="background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);">' +
        '<h6 style="color:#fff;margin:0;font-size:18px;font-weight:700;display:flex;align-items:center;gap:10px;">' +
        '<i class="fas fa-chart-line"></i> ' + title + '</h6>' +
        '<button class="rdpms-graph-close" onclick="closeGraphModal()" style="color:#fff;">&times;</button>' +
        '</div>' +
        '<div class="rdpms-graph-body" style="padding:20px;background:transparent;">' +
        '<div class="d-flex justify-content-between align-items-center flex-wrap" style="padding:10px 16px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);border-radius:10px;gap:12px;margin-bottom:12px;">' +
        '<div class="d-flex align-items-center" style="gap:8px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;color:rgba(255,255,255,0.72);margin:0;font-weight:600;">Date</label>' +
        '<input type="date" id="singleArrDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:5px 10px;color:#e6edf6;background:rgba(255,255,255,0.06);outline:none;color-scheme:dark;"/>' +
        '<button type="button" id="singleArrLoadBtn" style="background:linear-gradient(135deg,#22d3ee,#0891b2);color:#04222b;border:none;border-radius:6px;padding:6px 14px;font-size:12px;font-weight:700;cursor:pointer;display:inline-flex;align-items:center;gap:5px;">' +
        '<i class="fas fa-search"></i> Load</button>' +
        '</div>' +
        '<span style="font-size:13px;font-weight:600;color:rgba(255,255,255,0.78);display:inline-flex;align-items:center;gap:6px;">' +
        '<i class="fas fa-clock" style="color:#22d3ee;font-size:13px;"></i><span id="singleArrTimeRange">Loading...</span></span>' +
        '</div>' +
        '<div id="singleArrLoading" style="display:flex;align-items:center;justify-content:center;height:450px;color:rgba(255,255,255,0.6);gap:10px;">' +
        '<i class="fas fa-spinner fa-spin fa-lg"></i> Loading array data...</div>' +
        '<div id="singleArrChartDiv" style="width:100%;height:500px;display:none;background:#0a1228;border-radius:10px;"></div>' +
        '<div id="singleArrError" style="display:none;text-align:center;padding:50px;color:#fb7185;"></div>' +
        '</div></div></div>';

    $('#rdpmsGraphOverlay').remove();
    $('body').append(modalHtml);

    // Store params
    window._singleArrParams = { assetId: assetId, attrId: attrId, title: title, unit: unit, color: color, type: type };

    // ── Date filter: default to today, load that day's full 24h window ──
    function _saDateStr(d) {
        return d.getFullYear() + '-' +
            String(d.getMonth() + 1).padStart(2, '0') + '-' +
            String(d.getDate()).padStart(2, '0');
    }
    var _saToday = new Date();
    $('#singleArrDate').val(_saDateStr(_saToday)).attr('max', _saDateStr(_saToday));

    function _saLoadForDate() {
        var ds = $('#singleArrDate').val();
        if (!ds) { showWarning('Please select a date.', 'Validation'); return; }
        var p = ds.split('-');
        var y = parseInt(p[0], 10), mo = parseInt(p[1], 10) - 1, da = parseInt(p[2], 10);
        var dayStart = new Date(y, mo, da, 0, 0, 0, 0);
        var dayEnd = new Date(y, mo, da, 23, 59, 59, 999);
        loadSingleArrayData(dayStart, dayEnd);
    }

    $('#singleArrLoadBtn').off('click').on('click', _saLoadForDate);

    // Initial load = today's full day
    _saLoadForDate();
}

function loadSingleArrayData(startDate, endDate) {
    var p = window._singleArrParams;
    if (!p) return;

    var $ld = $('#singleArrLoading'), $ch = $('#singleArrChartDiv'), $er = $('#singleArrError'), $tr = $('#singleArrTimeRange');
    $ld.show(); $ch.hide(); $er.hide();

    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);

    // 24-hour range label for the selected date
    function _saFmt(d) {
        return String(d.getDate()).padStart(2, '0') + '/' +
            String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' +
            String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    }
    $tr.text(_saFmt(startDate) + ' \u2014 ' + _saFmt(endDate));

    var apiUrl = HISTORY_API_BASE + '?assetId=' + p.assetId + '&startDate=' + startStr + '&endDate=' + endStr;

    $.ajax({
        url: apiUrl,
        type: 'GET',
        dataType: 'json',
        timeout: 30000,
        success: function (response) {
            $ld.hide();

            if (!response || !response.Data || response.Data.length === 0) {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No data available for the selected date').show();
                return;
            }

            // Find the array data - Value should be a comma-separated string
            var operations = [];
            var seenTimestamps = {}; // De-duplicate by timestamp

            response.Data.forEach(function (item) {
                if (item.AttributeId === p.attrId && item.Values) {
                    for (var key in item.Values) {
                        var entry = item.Values[key];
                        if (!entry || !entry.Value) continue;

                        // Check if Value is a string (array data is comma-separated)
                        var valueStr = entry.Value;
                        if (typeof valueStr !== 'string') continue;

                        // Skip if it doesn't look like an array (no commas)
                        if (valueStr.indexOf(',') === -1) continue;

                        var ts = entry.Timestamp ? entry.Timestamp.TimestampDevice : null;
                        if (!ts || ts.indexOf('0001') >= 0) continue;

                        var timestamp = new Date(ts).getTime();
                        if (isNaN(timestamp) || timestamp <= 0) continue;

                        // De-duplicate by timestamp (same timestamp = same operation)
                        if (seenTimestamps[timestamp]) continue;
                        seenTimestamps[timestamp] = true;

                        var arr = valueStr.split(',').map(function (v) { return parseFloat(v.trim()); });
                        // Filter out NaN values
                        arr = arr.filter(function (v) { return !isNaN(v); });

                        if (arr.length > 0) {
                            operations.push({ timestamp: timestamp, array: arr, tsStr: ts });
                        }
                    }
                }
            });

            if (operations.length === 0) {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No array data found for Attribute ID: ' + p.attrId + '<br><small>Array values should be comma-separated strings</small>').show();
                return;
            }

            // Sort by timestamp
            operations.sort(function (a, b) { return a.timestamp - b.timestamp; });

            console.log('[PM] Found', operations.length, 'unique operations for attr', p.attrId);

            $ch.show();
            if (operations.length > 0) {
                var firstOp = new Date(operations[0].timestamp);
                var lastOp = new Date(operations[operations.length - 1].timestamp);
                var fmtOp = function (d) {
                    return String(d.getDate()).padStart(2, '0') + '/' +
                        String(d.getMonth() + 1).padStart(2, '0') + ' ' +
                        String(d.getHours()).padStart(2, '0') + ':' +
                        String(d.getMinutes()).padStart(2, '0') + ':' +
                        String(d.getSeconds()).padStart(2, '0');
                };
                var timeLabel = operations.length === 1
                    ? fmtOp(firstOp)
                    : fmtOp(firstOp) + '  →  ' + fmtOp(lastOp);
                $('#singleArrTimeRange').text(timeLabel);
            }
            renderSingleArrayByTimestamp(operations, p.title, p.unit, p.color);
        },
        error: function (xhr, status, error) {
            $ld.hide();
            $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;"></i>Failed to load data: ' + error).show();
        }
    });
}

function renderSingleArrayByTimestamp(operations, title, unit, color) {
    var el = document.getElementById('singleArrChartDiv');
    if (!el) return;

    // FIX 9: Guard empty operations
    if (!operations || operations.length === 0) {
        el.style.display = 'flex';
        el.style.alignItems = 'center';
        el.style.justifyContent = 'center';
        el.innerHTML = '<div style="color:#94a3b8;font-size:14px;padding:40px;">No waveform data available</div>';
        return;
    }

    // FIX 7: Dispose previous chart instance cleanly
    var existingChart = echarts.getInstanceByDom(el);
    if (existingChart) existingChart.dispose();
    if (window._singleArrChartInst) {
        try { window._singleArrChartInst.dispose(); } catch (e2) { }
    }

    var _singleArrChart = echarts.init(el);
    window._singleArrChartInst = _singleArrChart; // stored so closeGraphModal can dispose it

    // Each sample is 20 ms apart
    var sampleIntervalMs = 20;
    var series = [];
    var legends = [];

    operations.forEach(function (op, idx) {
        // FIX 10: Guard null/missing array
        if (!op || !op.array || op.array.length === 0) return;

        var baseTime = op.timestamp;
        var data = op.array.map(function (val, i) {
            return [baseTime + (i * sampleIntervalMs), val];
        });

        var t = new Date(op.timestamp);
        var timeLabel = String(t.getHours()).padStart(2, '0') + ':' +
            String(t.getMinutes()).padStart(2, '0') + ':' +
            String(t.getSeconds()).padStart(2, '0');
        var label = title + (operations.length > 1 ? ' (' + timeLabel + ')' : '');

        // FIX 5: Use rgba() not hex alpha append -- ECharts v4 doesn't support #rrggbbaa
        var opColor = idx === 0 ? color : _hexToRgba(color, idx === 1 ? 0.70 : 0.45);

        // FIX 4: symbolSize as static value not function (ECharts v4 safe)
        var dotSize = op.array.length <= 80 ? 5 : op.array.length <= 160 ? 4 : 3;

        legends.push(label);
        series.push({
            name: label,
            type: 'line',
            smooth: false,
            symbol: 'circle',
            symbolSize: dotSize,
            showSymbol: op.array.length <= 200,  // hide dots on dense data for performance
            lineStyle: {
                width: idx === 0 ? 2.5 : 1.8,
                color: opColor,
                type: idx > 0 ? 'dashed' : 'solid'  // FIX 3: first series solid, others dashed
            },
            // FIX 5b: areaStyle uses rgba() not hex alpha
            areaStyle: idx === 0 ? {
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: _hexToRgba(color, 0.25) },
                    { offset: 1, color: _hexToRgba(color, 0.02) }
                ])
            } : null,
            data: data
        });
    });

    // Guard: if all ops were skipped (all null/empty arrays)
    if (series.length === 0) {
        el.innerHTML = '<div style="display:flex;align-items:center;justify-content:center;height:100%;color:#94a3b8;font-size:14px;">No valid waveform data</div>';
        return;
    }

    _singleArrChart.setOption({
        backgroundColor: '#0a1228',
        title: {
            text: title,
            subtext: series.length + ' operation(s) plotted',
            left: 'center',
            top: 10,
            textStyle: { fontSize: 16, fontWeight: '700', color: 'rgba(255,255,255,0.94)' },
            subtextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.50)' }
        },
        tooltip: {
            trigger: 'axis',
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: color,
            borderWidth: 1,
            padding: [12, 16],
            formatter: function (params) {
                if (!params || !params.length) return '';
                var t = new Date(params[0].value[0]);
                var timeStr = String(t.getHours()).padStart(2, '0') + ':' +
                    String(t.getMinutes()).padStart(2, '0') + ':' +
                    String(t.getSeconds()).padStart(2, '0') + '.' +
                    String(t.getMilliseconds()).padStart(3, '0');
                var dateStr = String(t.getDate()).padStart(2, '0') + '/' +
                    String(t.getMonth() + 1).padStart(2, '0');
                var html = '<div style="font-weight:600;margin-bottom:8px;border-bottom:1px solid rgba(255,255,255,0.10);padding-bottom:6px;">' +
                    dateStr + ' ' + timeStr + '</div>';
                params.forEach(function (p) {
                    if (p.value !== undefined && p.value[1] !== undefined) {
                        html += '<div style="display:flex;align-items:center;padding:3px 0;">' +
                            '<span style="display:inline-block;width:10px;height:3px;background:' + p.color + ';margin-right:8px;border-radius:2px;"></span>' +
                            '<span style="flex:1;font-size:11px;color:rgba(255,255,255,0.62);">Value:</span>' +
                            '<span style="font-weight:700;margin-left:10px;color:rgba(255,255,255,0.94);">' +
                            p.value[1].toFixed(2) + ' ' + unit + '</span></div>';
                    }
                });
                return html;
            }
        },
        legend: {
            data: legends,
            top: 50,
            left: 'center',
            textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.72)' },
            itemGap: 15,
            type: 'scroll'
        },
        grid: { top: legends.length > 1 ? 90 : 70, left: 60, right: 30, bottom: 80 },
        toolbox: {
            right: 15,
            top: 10,
            feature: {
                dataZoom: { yAxisIndex: 'none' },
                restore: {},
                saveAsImage: { pixelRatio: 2 }
            }
        },
        xAxis: {
            type: 'time',
            name: 'Time (ms)',
            nameLocation: 'center',
            nameGap: 32,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.60)' },
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) {
                    var d = new Date(v);
                    return String(d.getHours()).padStart(2, '0') + ':' +
                        String(d.getMinutes()).padStart(2, '0') + ':' +
                        String(d.getSeconds()).padStart(2, '0');
                }
            },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        },
        yAxis: {
            type: 'value',
            name: unit,
            nameLocation: 'middle',
            nameGap: 45,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.60)' },
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) { return v.toFixed(2); }
            },
            axisLine: { show: true, lineStyle: { color: color, width: 2 } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        },
        dataZoom: [
            {
                type: 'slider',
                height: 22,
                bottom: 10,
                start: 0,
                end: 100,
                borderColor: 'rgba(255,255,255,0.14)',
                backgroundColor: 'rgba(15,23,42,0.70)',
                // FIX 5: rgba not hex alpha
                fillerColor: _hexToRgba(color, 0.12),
                handleStyle: { color: color }
            },
            { type: 'inside' }
        ],
        series: series
    });

    // FIX 8: Named handler so closeGraphModal can remove it
    $(window).off('resize.singleArrChart').on('resize.singleArrChart', function () {
        if (window._singleArrChartInst) window._singleArrChartInst.resize();
    });
}

// ================================================================
// DATALOGGER BADGES -- Pickup/Drop badges in header
// ================================================================
function renderPmBadges(assetId, dlData) {
    // IRS/TWS dots are static — only update the relay-badges span.
    var $relaySpan = $('#pmRelayBadges_' + assetId);

    // Fallback: if an older card structure exists without relay span.
    if (!$relaySpan.length) {
        var $row = $('#pmBadges_' + assetId);
        if ($row.length) {
            $row.append(
                '<span id="pmRelayBadges_' + assetId + '" ' +
                'style="display:inline-flex;align-items:center;flex-wrap:wrap;gap:4px;"></span>'
            );
            $relaySpan = $('#pmRelayBadges_' + assetId);
        }
    }

    if (!$relaySpan.length) return;

    var keys = Object.keys(dlData || {});
    if (keys.length === 0) {
        $relaySpan.html('');
        return;
    }

    keys.sort(function (a, b) {
        var ra = dlData[a], rb = dlData[b];
        if (ra.isPickup && !rb.isPickup) return -1;
        if (!ra.isPickup && rb.isPickup) return 1;
        return (ra.displayName || a).localeCompare(rb.displayName || b);
    });

    var h = '';
    for (var i = 0; i < keys.length; i++) {
        var relay = dlData[keys[i]];
        var dn = relay.displayName || keys[i];
        var badgeCls = relay.isPickup ? 'pickup' : 'drop';
        var badgeTxt = relay.isPickup ? 'Pickup' : 'Drop';

        h += '<span class="pm-relay-badge">' +
            '<span class="relay-name">' + pmEsc(dn) + '</span>' +
            '<span class="rdpms-dl-badge ' + badgeCls + ' shine-button" ' +
            'style="padding:1px 6px;font-size:10px;">' + badgeTxt + '</span>' +
            '</span>';
    }

    h += '<span class="pm-detail-badge" onclick="fnShowDataLoggerEvent(\'' + assetId + '\')">Detail</span>';

    $relaySpan.html(h);
}

// ================================================================
// WAVEFORM CHARTS
// ================================================================
function updatePmWaveforms(assetId, pm) {
    if (!pm) pm = getPmStructuredData(assetId);
    if (!pm) return;

    var cfgs = [
        { dir: 'Normal', k: 'N_AC', type: 'AC', color: '#259dab', label: 'Normal -- A Current (A)', unit: 'A' },
        { dir: 'Normal', k: 'N_AV', type: 'AV', color: '#0d6efd', label: 'Normal -- A Voltage (V)', unit: 'V' },
        { dir: 'Normal', k: 'N_BC', type: 'BC', color: '#e4b704', label: 'Normal -- B Current (A)', unit: 'A' },
        { dir: 'Normal', k: 'N_BV', type: 'BV', color: '#6f42c1', label: 'Normal -- B Voltage (V)', unit: 'V' },
        { dir: 'Reverse', k: 'R_AC', type: 'AC', color: '#dc3545', label: 'Reverse -- A Current (A)', unit: 'A' },
        { dir: 'Reverse', k: 'R_AV', type: 'AV', color: '#198754', label: 'Reverse -- A Voltage (V)', unit: 'V' },
        { dir: 'Reverse', k: 'R_BC', type: 'BC', color: '#fd7e14', label: 'Reverse -- B Current (A)', unit: 'A' },
        { dir: 'Reverse', k: 'R_BV', type: 'BV', color: '#0dcaf0', label: 'Reverse -- B Voltage (V)', unit: 'V' }
    ];

    for (var ci = 0; ci < cfgs.length; ci++) {
        (function (c) {
            var canId = 'pmWave_' + assetId + '_' + c.k;
            var $wrap = $('#' + canId).parent();

            if (!$wrap.length) return;

            var arr = pm[c.dir][c.type]['Array'];
            var vals = [];
            if (arr && arr.value && typeof arr.value === 'string' && arr.value.trim() !== '') {
                vals = arr.value.split(',').map(function (v) {
                    var num = parseFloat(v.trim());
                    return isNaN(num) ? 0 : num;
                });
            }
            if (!vals.length) { vals = [0]; }

            // Get timestamp from the operation data
            var opTime = pm[c.dir]['OperationTime'];
            var startTimestamp = null;
            if (opTime && opTime.timestamp) {
                startTimestamp = new Date(opTime.timestamp).getTime();
            }

            // Sample interval in milliseconds (typically 20ms per sample for PM data)
            var sampleIntervalMs = 20;

            // Replace canvas with div for ECharts
            var divId = 'pmEChart_' + assetId + '_' + c.k;
            $wrap.empty();
            $wrap.append('<div><span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:' + c.color + ';margin-right:8px;vertical-align:middle;box-shadow:0 0 6px ' + c.color + '80;"></span>' + c.label + '</div>');
            $wrap.append('<div id="' + divId + '" style="width:100%;height:360px;padding:4px 8px 0 8px;box-sizing:border-box;"></div>');

            var el = document.getElementById(divId);
            if (!el) return;

            // Destroy existing
            if (pmChartInstances[canId]) {
                try { pmChartInstances[canId].dispose(); } catch (e) { }
                pmChartInstances[canId] = null;
            }

            var chart = echarts.init(el);
            pmChartInstances[canId] = chart;

            // Calculate statistics for display
            var minVal = Math.min.apply(null, vals);
            var maxVal = Math.max.apply(null, vals);
            var avgVal = vals.reduce(function (a, b) { return a + b; }, 0) / vals.length;

            var chartColor = c.color;
            var chartUnit = c.unit;
            var chartLabel = c.label;

            // Prepare data with time or elapsed time
            var chartData = [];
            var totalDurationMs = (vals.length - 1) * sampleIntervalMs; // last sample index × 20ms
            var totalDurationSec = (totalDurationMs / 1000).toFixed(2);

            if (startTimestamp) {
                for (var i = 0; i < vals.length; i++) {
                    chartData.push([startTimestamp + (i * sampleIntervalMs), vals[i]]);
                }
            }

            // Format start time for display
            var startTimeStr = '';
            if (startTimestamp) {
                var st = new Date(startTimestamp);
                startTimeStr = String(st.getHours()).padStart(2, '0') + ':' +
                    String(st.getMinutes()).padStart(2, '0') + ':' +
                    String(st.getSeconds()).padStart(2, '0');
            }


            var totalSamples = vals.length;

            // Step 1 -- decide label precision: use 1 decimal (0.0s) unless total
            // duration < 1s AND samples are very few, then use 2 decimals (0.02s)
            var useHighRes = (totalDurationMs < 1000 && totalSamples <= 60);
            function fmtSec(ms) {
                return useHighRes
                    ? (ms / 1000).toFixed(2) + 's'
                    : (ms / 1000).toFixed(2) + 's';
            }

            var desiredLabels = 8;
            var rawStep = Math.ceil(totalSamples / desiredLabels);
            var stepSamples = Math.max(1, rawStep);


            var xCategories = [];
            var xAxisLabels = [];
            var lastLabel = null;

            for (var i = 0; i < totalSamples; i++) {
                var ms = i * sampleIntervalMs;
                var isStep = (i % stepSamples === 0) || (i === totalSamples - 1);
                var labelStr = isStep ? fmtSec(ms) : '';

                // Suppress if this formatted text was already shown
                if (labelStr !== '' && labelStr === lastLabel) {
                    labelStr = '';
                }
                if (labelStr !== '') lastLabel = labelStr;

                xCategories.push(ms);       // raw ms stored as category value
                xAxisLabels.push(labelStr); // '' = hidden, 'X.Xs' = shown
            }

            // ── DOT INTERVAL ────────────────────────────────────────────────────────
            // Plot a clearly visible marker at EVERY 20 ms sample.
            // Size stays prominent across all waveform lengths.
            var dotSymbolSize = totalSamples <= 60 ? 9 :
                totalSamples <= 120 ? 8 :
                    totalSamples <= 200 ? 7 : 6;

            // Build chartDataLine -- every point gets a clearly visible dot at its 20 ms slot
            var chartDataLine = [];
            for (var i = 0; i < totalSamples; i++) {
                if (startTimestamp) {
                    chartDataLine.push({
                        value: [startTimestamp + (i * sampleIntervalMs), vals[i]],
                        symbol: 'circle',
                        symbolSize: dotSymbolSize
                    });
                } else {
                    chartDataLine.push({
                        value: vals[i],
                        symbol: 'circle',
                        symbolSize: dotSymbolSize
                    });
                }
            }

            chart.setOption({
                backgroundColor: 'transparent',
                tooltip: {
                    trigger: 'axis',
                    axisPointer: {
                        type: 'line',
                        lineStyle: { color: chartColor, width: 1.5, type: 'dashed' }
                    },
                    backgroundColor: 'rgba(5,9,24,0.97)',
                    borderColor: chartColor,
                    borderWidth: 1.5,
                    borderRadius: 10,
                    padding: [10, 16],
                    textStyle: { color: '#f1f5f9', fontSize: 12 },
                    extraCssText: 'box-shadow: 0 8px 24px rgba(0,0,0,0.18);',
                    formatter: function (params) {
                        if (!params || !params.length) return '';
                        var p = params[0];
                        var timeDisplay = '';
                        if (startTimestamp) {
                            var xVal = p.value[0];
                            var t = new Date(xVal);
                            var elapsedMs = xVal - startTimestamp;
                            timeDisplay = String(t.getHours()).padStart(2, '0') + ':' +
                                String(t.getMinutes()).padStart(2, '0') + ':' +
                                String(t.getSeconds()).padStart(2, '0') + '.' +
                                String(t.getMilliseconds()).padStart(3, '0') +
                                ' <span style="color:#94a3b8;">(+' + elapsedMs + ' ms)</span>';
                        } else {
                            // p.dataIndex gives us the exact sample index → convert to ms
                            var sampleIdx = p.dataIndex !== undefined ? p.dataIndex : 0;
                            var ms = sampleIdx * sampleIntervalMs;
                            timeDisplay = (ms / 1000).toFixed(2) + ' s <span style="color:#94a3b8;">(' + ms + ' ms)</span>';
                        }
                        var yVal = startTimestamp ? p.value[1] : (typeof p.value === 'object' ? p.value[1] : p.value);
                        return '<div style="min-width:180px;">' +
                            '<div style="display:flex;align-items:center;gap:6px;margin-bottom:8px;padding-bottom:6px;border-bottom:1px solid rgba(255,255,255,0.12);">' +
                            '<span style="display:inline-block;width:8px;height:8px;border-radius:50%;background:' + chartColor + ';flex-shrink:0;"></span>' +
                            '<span style="font-size:11px;color:#94a3b8;">' + timeDisplay + '</span></div>' +
                            '<div style="display:flex;align-items:baseline;justify-content:space-between;gap:16px;">' +
                            '<span style="color:#94a3b8;font-size:11px;">' + p.seriesName.split('--').pop().trim() + '</span>' +
                            '<span style="font-weight:700;font-size:16px;color:' + chartColor + ';">' + parseFloat(yVal).toFixed(2) + ' <span style="font-size:11px;font-weight:500;color:#94a3b8;">' + chartUnit + '</span></span></div></div>';
                    }
                },
                grid: { left: 58, right: 20, top: 40, bottom: 68, containLabel: false },
                title: {
                    text: '↓ ' + minVal.toFixed(2) + '   ↑ ' + maxVal.toFixed(2) + '   ~ ' + avgVal.toFixed(2) + ' ' + chartUnit + '   ·   Δt 20 ms',
                    left: 'center',
                    top: 4,
                    textStyle: { fontSize: 11, fontWeight: '400', color: 'rgba(255,255,255,0.62)', fontFamily: 'inherit' }
                },
                xAxis: {
                    type: startTimestamp ? 'time' : 'category',
                    data: startTimestamp ? undefined : xCategories,
                    name: startTimestamp
                        ? 'Time  (start ' + startTimeStr + ')'
                        : 'Elapsed Time -- ' + totalDurationSec + ' sec total',
                    nameLocation: 'center',
                    nameGap: 36,
                    nameTextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.55)', fontWeight: '500' },
                    boundaryGap: false,
                    axisLabel: {
                        fontSize: 11,
                        color: 'rgba(255,255,255,0.55)',
                        fontWeight: '500',
                        margin: 10,
                        interval: startTimestamp ? null : 0,
                        formatter: startTimestamp ? function (v) {
                            var d = new Date(v);
                            return String(d.getHours()).padStart(2, '0') + ':' +
                                String(d.getMinutes()).padStart(2, '0') + ':' +
                                String(d.getSeconds()).padStart(2, '0');
                        } : function (v, idx) {
                            return (xAxisLabels && xAxisLabels[idx] !== undefined)
                                ? xAxisLabels[idx] : '';
                        }
                    },
                    splitLine: { show: false },
                    axisLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.16)', width: 1.5 } },
                    axisTick: { show: true, alignWithLabel: true, length: 4, lineStyle: { color: 'rgba(255,255,255,0.14)' } }
                },
                yAxis: {
                    type: 'value',
                    name: chartUnit,
                    nameLocation: 'end',
                    nameGap: 8,
                    nameTextStyle: { fontSize: 12, color: chartColor, fontWeight: '700', padding: [0, 0, 0, 48] },
                    axisLabel: {
                        fontSize: 11,
                        color: 'rgba(255,255,255,0.55)',
                        formatter: function (v) { return v.toFixed(2); },
                        margin: 10
                    },
                    axisLine: { show: false },
                    axisTick: { show: false },
                    splitLine: { show: true, lineStyle: { color: '#f1f5f9', width: 1, type: 'solid' } }
                },
                dataZoom: [
                    {
                        type: 'slider',
                        height: 24,
                        bottom: 8,
                        start: 0,
                        end: 100,
                        borderColor: 'transparent',
                        backgroundColor: 'rgba(15,23,42,0.70)',
                        fillerColor: chartColor + '30',
                        handleStyle: { color: chartColor, borderColor: 'rgba(255,255,255,0.80)', borderWidth: 2, shadowBlur: 4, shadowColor: chartColor + '60' },
                        handleSize: '80%',
                        textStyle: { fontSize: 10, color: '#94a3b8' },
                        dataBackground: {
                            lineStyle: { color: chartColor + '50', width: 1 },
                            areaStyle: { color: chartColor + '15' }
                        },
                        moveHandleSize: 6
                    },
                    { type: 'inside', zoomOnMouseWheel: true, moveOnMouseMove: true }
                ],
                toolbox: {
                    right: 12,
                    top: 4,
                    itemSize: 14,
                    itemGap: 8,
                    feature: {
                        dataZoom: { yAxisIndex: 'none', title: { zoom: 'Zoom', back: 'Reset' } },
                        restore: { title: 'Reset' },
                        saveAsImage: { title: 'Save PNG', pixelRatio: 2 }
                    },
                    iconStyle: { borderColor: 'rgba(255,255,255,0.55)', borderWidth: 1 },
                    emphasis: { iconStyle: { borderColor: chartColor } }
                },
                series: [{
                    name: chartLabel,
                    type: 'line',
                    smooth: false,
                    showSymbol: true,
                    showAllSymbol: true,
                    // Do NOT use 'sampling' -- every 20 ms point must be rendered as-is
                    itemStyle: {
                        color: chartColor,
                        borderColor: 'rgba(5,9,24,0.94)',
                        borderWidth: 2.5,
                        shadowBlur: 5,
                        shadowColor: chartColor + 'aa'
                    },
                    lineStyle: { width: 1.5, color: chartColor, cap: 'round', join: 'round', type: 'dashed', dashOffset: 4 },
                    areaStyle: {
                        color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                            { offset: 0, color: chartColor + '55' },
                            { offset: 0.4, color: chartColor + '20' },
                            { offset: 1, color: chartColor + '00' }
                        ])
                    },
                    emphasis: {
                        disabled: false,
                        itemStyle: {
                            color: '#ffffff',
                            borderColor: chartColor,
                            borderWidth: 3,
                            shadowBlur: 14,
                            shadowColor: chartColor + 'cc',
                            shadowOffsetX: 0,
                            shadowOffsetY: 0
                        },
                        lineStyle: { width: 2 }
                    },
                    data: chartDataLine
                }]
            });

            // Handle resize
            $(window).off('resize.pmChart_' + canId).on('resize.pmChart_' + canId, function () {
                if (pmChartInstances[canId]) {
                    pmChartInstances[canId].resize();
                }
            });
        })(cfgs[ci]);
    }
}
function pmShowChart(assetId, chartType) {
    var asset = wsLiveData[assetId]; if (!asset) return;
    var siteId = asset.SiteId || $('#drpSite').val();
    var pm = getPmStructuredData(assetId);

    // Check if this asset has Combine column (IsSeriesOperation = 1)
    var isCombineMode = window.pmCombineColumnStatus && window.pmCombineColumnStatus[assetId];

    // For Combine mode, fetch from History API to get both A and B arrays
    if (isCombineMode && (chartType === 'N_AC' || chartType === 'R_AC' || chartType === 'N_AV' || chartType === 'R_AV')) {
        var isReverse = (chartType.charAt(0) === 'R');
        var isCurrent = (chartType.indexOf('_AC') > -1 || chartType.indexOf('_AV') > -1) && chartType.indexOf('_AC') > -1;
        showCombinedArrayGraph(assetId, isReverse, isCurrent);
        return;
    }

    // Standard single-array chart (non-combine mode)
    if (!pm) { fnGetAssetGraph(siteId, assetId); return; }
    var dirMap = { 'N_AC': 'Normal', 'N_AV': 'Normal', 'N_BC': 'Normal', 'N_BV': 'Normal', 'R_AC': 'Reverse', 'R_AV': 'Reverse', 'R_BC': 'Reverse', 'R_BV': 'Reverse' };
    var typeMap = { 'N_AC': 'AC', 'N_AV': 'AV', 'N_BC': 'BC', 'N_BV': 'BV', 'R_AC': 'AC', 'R_AV': 'AV', 'R_BC': 'BC', 'R_BV': 'BV' };
    var dir = dirMap[chartType] || 'Normal', type = typeMap[chartType] || 'AC';
    var arr = pm[dir][type]['Array'];
    if (!arr || !arr.value) { fnGetAssetGraph(siteId, assetId); return; }
    var title = dir + ' -- ' + type + ' : ' + (asset.AssetName || assetId);
    var vals = arr.value.split(',').map(function (v) { return parseFloat(v.trim()); });
    var lbls = []; for (var i = 0; i < vals.length; i++) lbls.push(i);
    var mh = '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1100px;">' +
        '<div class="rdpms-graph-header" style="background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);">' +
        '<h6><i class="fas fa-chart-line"></i> ' + title + '</h6>' +
        '<button class="rdpms-graph-close" onclick="closeGraphModal()">&times;</button></div>' +
        '<div class="rdpms-graph-body" style="padding:24px;">' +
        '<div class="pm-modal-chart-card" style="border-radius:12px;padding:20px;">' +
        '<div class="pm-modal-chart-summary" style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px;padding-bottom:12px;">' +
        '<span style="font-size:14px;color:rgba(255,255,255,0.62);"><strong>Samples:</strong> ' + vals.length + ' points</span>' +
        '<span style="font-size:14px;color:rgba(255,255,255,0.62);"><strong>Min:</strong> ' + Math.min.apply(null, vals).toFixed(2) + ' | <strong>Max:</strong> ' + Math.max.apply(null, vals).toFixed(2) + ' | <strong>Avg:</strong> ' + (vals.reduce(function (a, b) { return a + b; }, 0) / vals.length).toFixed(2) + '</span>' +
        '</div>' +
        '<canvas id="pmModalChart" style="width:100%;height:480px;"></canvas>' +
        '</div></div></div></div>';
    $('#rdpmsGraphOverlay').remove(); $('body').append(mh);
    new Chart(document.getElementById('pmModalChart').getContext('2d'), {
        type: 'line',
        data: {
            labels: lbls,
            datasets: [{
                label: title,
                data: vals,
                borderColor: '#259dab',
                backgroundColor: 'rgba(37,157,171,0.15)',
                borderWidth: 2.5,
                pointRadius: 0,
                fill: true,
                tension: 0.3
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            legend: { display: true, labels: { fontSize: 14, fontStyle: 'bold', fontColor: 'rgba(255,255,255,0.72)' } },
            scales: {
                xAxes: [{ ticks: { maxTicksLimit: 20, fontSize: 12, fontColor: 'rgba(255,255,255,0.55)' }, gridLines: { color: 'rgba(255,255,255,0.06)', zeroLineColor: 'rgba(255,255,255,0.14)' }, scaleLabel: { display: true, labelString: 'Sample Index', fontSize: 13, fontColor: 'rgba(255,255,255,0.60)' } }],
                yAxes: [{ ticks: { fontSize: 12, fontColor: 'rgba(255,255,255,0.55)' }, gridLines: { color: 'rgba(255,255,255,0.06)', zeroLineColor: 'rgba(255,255,255,0.14)' }, scaleLabel: { display: true, labelString: 'Value', fontSize: 13, fontColor: 'rgba(255,255,255,0.60)' } }]
            },
            tooltips: {
                backgroundColor: 'rgba(5,9,24,0.97)',
                titleFontColor: '#22d3ee',
                bodyFontColor: 'rgba(255,255,255,0.86)',
                borderColor: '#259dab',
                borderWidth: 1,
                callbacks: {
                    title: function (items) { return 'Sample: ' + items[0].index; },
                    label: function (item) { return 'Value: ' + item.yLabel.toFixed(2); }
                }
            }
        }
    });
}

// Combined Array Graph for Series Operation
// Normal Current: AC(1001) + BC(3001), Normal Voltage: AV(2001) + BV(4001)
// Reverse Current: AC(6001) + BC(8001), Reverse Voltage: AV(7001) + BV(9001)
function showCombinedArrayGraph(assetId, isReverse, isCurrent) {
    var asset = wsLiveData[assetId];
    var baseName = asset ? (asset.AssetName || assetId) : assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

    // Calculate Attribute IDs based on formula:
    // Base: AC=1000/6000, AV=2000/7000, BC=3000/8000, BV=4000/9000
    // Array offset = 1
    var aAttrId, bAttrId, aLabel, bLabel, unit;

    if (isCurrent) {
        aAttrId = isReverse ? 6001 : 1001;  // AC Array
        bAttrId = isReverse ? 8001 : 3001;  // BC Array
        aLabel = 'A Current (A)';
        bLabel = 'B Current (A)';
        unit = 'A';
    } else {
        aAttrId = isReverse ? 7001 : 2001;  // AV Array
        bAttrId = isReverse ? 9001 : 4001;  // BV Array
        aLabel = 'A Voltage (V)';
        bLabel = 'B Voltage (V)';
        unit = 'V';
    }

    var direction = isReverse ? 'Reverse' : 'Normal';
    var title = name + ' -- ' + direction + ' Combined ' + (isCurrent ? 'Current' : 'Voltage');

    // Show loading modal
    var modalHtml = '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1200px;width:96%;">' +
        '<div class="rdpms-graph-header" style="background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);">' +
        '<h6 style="color:#fff;margin:0;font-size:18px;font-weight:700;display:flex;align-items:center;gap:10px;">' +
        '<i class="fas fa-chart-area"></i> ' + title + '</h6>' +
        '<button class="rdpms-graph-close" onclick="closeGraphModal()" style="color:#fff;">&times;</button>' +
        '</div>' +
        '<div class="rdpms-graph-body" style="padding:20px;background:transparent;">' +
        '<div class="d-flex justify-content-between align-items-center mb-3 flex-wrap" style="padding:10px 16px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);border-radius:10px;gap:12px;">' +
        '<div class="d-flex align-items-center" style="gap:8px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;color:rgba(255,255,255,0.72);margin:0;font-weight:600;">Date</label>' +
        '<input type="date" id="combineDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:5px 10px;color:#e6edf6;background:rgba(255,255,255,0.06);outline:none;color-scheme:dark;"/>' +
        '<button type="button" id="combineLoadBtn" style="background:linear-gradient(135deg,#22d3ee,#0891b2);color:#04222b;border:none;border-radius:6px;padding:6px 14px;font-size:12px;font-weight:700;cursor:pointer;display:inline-flex;align-items:center;gap:5px;">' +
        '<i class="fas fa-search"></i> Load</button>' +
        '</div>' +
        '<span style="font-size:13px;color:rgba(255,255,255,0.55);" id="combineTimeRange"></span>' +
        '</div>' +
        '<div id="combineChartLoading" style="display:flex;align-items:center;justify-content:center;height:450px;color:rgba(255,255,255,0.6);gap:10px;">' +
        '<i class="fas fa-spinner fa-spin fa-lg"></i> Loading combined array data...</div>' +
        '<div id="combineChartDiv" style="width:100%;height:500px;display:none;background:#0a1228;border-radius:10px;"></div>' +
        '<div id="combineChartError" style="display:none;text-align:center;padding:50px;color:#fb7185;"></div>' +
        '</div></div></div>';

    $('#rdpmsGraphOverlay').remove();
    $('body').append(modalHtml);

    // Store params
    window._combineParams = { assetId: assetId, aAttrId: aAttrId, bAttrId: bAttrId, aLabel: aLabel, bLabel: bLabel, unit: unit, title: title, isCurrent: isCurrent };

    // ── Date filter: default to today, load that day's full 24h window ──
    function _coDateStr(d) {
        return d.getFullYear() + '-' +
            String(d.getMonth() + 1).padStart(2, '0') + '-' +
            String(d.getDate()).padStart(2, '0');
    }
    var _coToday = new Date();
    $('#combineDate').val(_coDateStr(_coToday)).attr('max', _coDateStr(_coToday));

    function _coLoadForDate() {
        var ds = $('#combineDate').val();
        if (!ds) { showWarning('Please select a date.', 'Validation'); return; }
        var p = ds.split('-');
        var y = parseInt(p[0], 10), mo = parseInt(p[1], 10) - 1, da = parseInt(p[2], 10);
        var dayStart = new Date(y, mo, da, 0, 0, 0, 0);
        var dayEnd = new Date(y, mo, da, 23, 59, 59, 999);
        loadCombinedArrayData(dayStart, dayEnd);
    }

    $('#combineLoadBtn').off('click').on('click', _coLoadForDate);

    // Initial load = today's full day
    _coLoadForDate();
}

function loadCombinedArrayData(startDate, endDate) {
    var p = window._combineParams;
    if (!p) return;

    var $ld = $('#combineChartLoading'), $ch = $('#combineChartDiv'), $er = $('#combineChartError'), $tr = $('#combineTimeRange');
    $ld.show(); $ch.hide(); $er.hide();

    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);

    function _coFmt(d) {
        return String(d.getDate()).padStart(2, '0') + '/' +
            String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' +
            String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    }
    $tr.text(_coFmt(startDate) + ' \u2014 ' + _coFmt(endDate));

    var apiUrl = HISTORY_API_BASE + '?assetId=' + p.assetId + '&startDate=' + startStr + '&endDate=' + endStr;

    $.ajax({
        url: apiUrl,
        type: 'GET',
        dataType: 'json',
        timeout: 30000,
        success: function (response) {
            $ld.hide();

            if (!response || !response.Data || response.Data.length === 0) {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No data available for the selected date').show();
                return;
            }

            // Collect all arrays with their TimestampDevice
            var aOperations = [];  // [{timestamp, array}]
            var bOperations = [];
            var aSeenTs = {};  // De-duplicate by timestamp
            var bSeenTs = {};

            response.Data.forEach(function (item) {
                if (item.AttributeId === p.aAttrId && item.Values) {
                    for (var key in item.Values) {
                        var entry = item.Values[key];
                        if (!entry || !entry.Value) continue;

                        var valueStr = entry.Value;
                        if (typeof valueStr !== 'string' || valueStr.indexOf(',') === -1) continue;

                        var ts = entry.Timestamp ? entry.Timestamp.TimestampDevice : null;
                        if (!ts || ts.indexOf('0001') >= 0) continue;

                        var timestamp = new Date(ts).getTime();
                        if (isNaN(timestamp) || timestamp <= 0) continue;

                        // De-duplicate
                        if (aSeenTs[timestamp]) continue;
                        aSeenTs[timestamp] = true;

                        var arr = valueStr.split(',').map(function (v) { return parseFloat(v.trim()); });
                        arr = arr.filter(function (v) { return !isNaN(v); });
                        if (arr.length > 0) {
                            aOperations.push({ timestamp: timestamp, array: arr, tsStr: ts });
                        }
                    }
                }
                if (item.AttributeId === p.bAttrId && item.Values) {
                    for (var key in item.Values) {
                        var entry = item.Values[key];
                        if (!entry || !entry.Value) continue;

                        var valueStr = entry.Value;
                        if (typeof valueStr !== 'string' || valueStr.indexOf(',') === -1) continue;

                        var ts = entry.Timestamp ? entry.Timestamp.TimestampDevice : null;
                        if (!ts || ts.indexOf('0001') >= 0) continue;

                        var timestamp = new Date(ts).getTime();
                        if (isNaN(timestamp) || timestamp <= 0) continue;

                        // De-duplicate
                        if (bSeenTs[timestamp]) continue;
                        bSeenTs[timestamp] = true;

                        var arr = valueStr.split(',').map(function (v) { return parseFloat(v.trim()); });
                        arr = arr.filter(function (v) { return !isNaN(v); });
                        if (arr.length > 0) {
                            bOperations.push({ timestamp: timestamp, array: arr, tsStr: ts });
                        }
                    }
                }
            });

            if (aOperations.length === 0 && bOperations.length === 0) {
                $er.html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;"></i>No array data found for IDs: ' + p.aAttrId + ', ' + p.bAttrId + '<br><small>Array values should be comma-separated strings</small>').show();
                return;
            }

            // Sort by timestamp
            aOperations.sort(function (a, b) { return a.timestamp - b.timestamp; });
            bOperations.sort(function (a, b) { return a.timestamp - b.timestamp; });

            console.log('[PM] Combined: A=' + aOperations.length + ' ops, B=' + bOperations.length + ' ops');

            $ch.show();
            renderCombinedArrayByTimestamp(aOperations, bOperations, p.aLabel, p.bLabel, p.unit, p.title, p.isCurrent);
        },
        error: function (xhr, status, error) {
            $ld.hide();
            $er.html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;"></i>Failed to load data: ' + error).show();
        }
    });
}

function renderCombinedArrayByTimestamp(aOperations, bOperations, aLabel, bLabel, unit, title, isCurrent) {
    var el = document.getElementById('combineChartDiv');
    if (!el) return;

    var existingChart = echarts.getInstanceByDom(el);
    if (existingChart) existingChart.dispose();

    var chart = echarts.init(el);

    // Color scheme
    var aColor = isCurrent ? '#2563eb' : '#059669';
    var bColor = isCurrent ? '#dc2626' : '#d97706';

    var series = [];
    var legends = [];

    // Each sample in array is 20ms apart (standard PM sample interval)
    var sampleIntervalMs = 20;

    // Add A operations - each operation's array plotted at its TimestampDevice
    aOperations.forEach(function (op, idx) {
        var baseTime = op.timestamp;
        var data = op.array.map(function (val, i) {
            return [baseTime + (i * sampleIntervalMs), val];
        });

        var t = new Date(op.timestamp);
        var timeLabel = String(t.getHours()).padStart(2, '0') + ':' + String(t.getMinutes()).padStart(2, '0') + ':' + String(t.getSeconds()).padStart(2, '0');
        var label = aLabel + (aOperations.length > 1 ? ' (' + timeLabel + ')' : '');

        if (legends.indexOf(label) === -1) legends.push(label);

        series.push({
            name: label,
            type: 'line',
            smooth: true,
            symbol: 'none',
            lineStyle: { width: 2.5, color: aColor, type: idx > 0 ? 'dashed' : 'solid' },
            areaStyle: idx === 0 ? {
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: aColor + '40' },
                    { offset: 1, color: aColor + '05' }
                ])
            } : null,
            data: data
        });
    });

    // Add B operations
    bOperations.forEach(function (op, idx) {
        var baseTime = op.timestamp;
        var data = op.array.map(function (val, i) {
            return [baseTime + (i * sampleIntervalMs), val];
        });

        var t = new Date(op.timestamp);
        var timeLabel = String(t.getHours()).padStart(2, '0') + ':' + String(t.getMinutes()).padStart(2, '0') + ':' + String(t.getSeconds()).padStart(2, '0');
        var label = bLabel + (bOperations.length > 1 ? ' (' + timeLabel + ')' : '');

        if (legends.indexOf(label) === -1) legends.push(label);

        series.push({
            name: label,
            type: 'line',
            smooth: true,
            symbol: 'none',
            lineStyle: { width: 2.5, color: bColor, type: idx > 0 ? 'dashed' : 'solid' },
            areaStyle: idx === 0 ? {
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: bColor + '40' },
                    { offset: 1, color: bColor + '05' }
                ])
            } : null,
            data: data
        });
    });

    chart.setOption({
        backgroundColor: '#0a1228',
        title: {
            text: title,
            subtext: 'A: ' + aOperations.length + ' operation(s), B: ' + bOperations.length + ' operation(s)',
            left: 'center',
            top: 10,
            textStyle: { fontSize: 16, fontWeight: '700', color: 'rgba(255,255,255,0.94)' },
            subtextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.50)' }
        },
        tooltip: {
            trigger: 'axis',
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(255,255,255,0.12)',
            borderWidth: 1,
            padding: [12, 16],
            formatter: function (params) {
                if (!params || !params.length) return '';
                var t = new Date(params[0].value[0]);
                var timeStr = String(t.getHours()).padStart(2, '0') + ':' +
                    String(t.getMinutes()).padStart(2, '0') + ':' +
                    String(t.getSeconds()).padStart(2, '0') + '.' +
                    String(t.getMilliseconds()).padStart(3, '0');
                var dateStr = String(t.getDate()).padStart(2, '0') + '/' + String(t.getMonth() + 1).padStart(2, '0');

                var html = '<div style="font-weight:600;margin-bottom:8px;border-bottom:1px solid rgba(255,255,255,0.10);padding-bottom:6px;">' + dateStr + ' ' + timeStr + '</div>';
                params.forEach(function (p) {
                    if (p.value !== undefined && p.value[1] !== undefined && !isNaN(p.value[1])) {
                        html += '<div style="display:flex;align-items:center;padding:3px 0;">' +
                            '<span style="display:inline-block;width:10px;height:3px;background:' + p.color + ';margin-right:8px;"></span>' +
                            '<span style="flex:1;font-size:11px;">' + p.seriesName + ':</span>' +
                            '<span style="font-weight:600;margin-left:10px;">' + p.value[1].toFixed(2) + ' ' + unit + '</span></div>';
                    }
                });
                return html;
            }
        },
        legend: {
            data: legends,
            top: 50,
            left: 'center',
            textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.72)' },
            itemGap: 15,
            type: 'scroll'
        },
        grid: { top: 90, left: 55, right: 25, bottom: 75 },
        toolbox: {
            right: 15,
            top: 10,
            feature: {
                dataZoom: { yAxisIndex: 'none' },
                restore: {},
                saveAsImage: { pixelRatio: 2 }
            }
        },
        xAxis: {
            type: 'time',
            name: 'Time (TimestampDevice)',
            nameLocation: 'center',
            nameGap: 30,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.60)' },
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.55)',
                formatter: function (v) {
                    var d = new Date(v);
                    return String(d.getHours()).padStart(2, '0') + ':' +
                        String(d.getMinutes()).padStart(2, '0') + ':' +
                        String(d.getSeconds()).padStart(2, '0');
                }
            },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        },
        yAxis: {
            type: 'value',
            name: unit,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.60)' },
            axisLabel: { fontSize: 10, color: 'rgba(255,255,255,0.55)', formatter: function (v) { return v.toFixed(2); } },
            axisLine: { show: true, lineStyle: { color: '#e2e8f0' } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        },
        dataZoom: [
            { type: 'slider', height: 22, bottom: 8, start: 0, end: 100, borderColor: 'rgba(255,255,255,0.14)', backgroundColor: 'rgba(15,23,42,0.70)', fillerColor: 'rgba(34,211,238,0.20)', handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, textStyle: { color: 'rgba(255,255,255,0.50)' } },
            { type: 'inside' }
        ],
        series: series
    });

    $(window).off('resize.combineChart').on('resize.combineChart', function () { chart.resize(); });
}

var pmSeeMoreRendering = {}; // Add this at the top with other PM variables

function pmToggleSeeMore(assetId) {
    var $s = $('#pmSeeMore_' + assetId);
    var $btn = $('#pmCard_' + assetId + ' .pm-btn-seemore');

    if ($s.is(':visible')) {
        $s.slideUp(300);
        $btn.text('See More');
        pmSeeMoreRendering[assetId] = false;
    } else {
        // Determine current direction from badge
        var $badgeA = $('#pmDirBadgeA_' + assetId);
        var dirText = $badgeA.length ? $badgeA.text().trim() : '';
        var curDir = (dirText.indexOf('REVERSE') > -1) ? 'R' : 'N';
        var dirLabel = (curDir === 'R') ? 'Reverse Operation' : 'Normal Operation';
        $('#pmWaveDir_' + assetId).text(dirLabel);

        // Show only current direction's charts, hide others
        $s.find('.pm-wave-box').each(function () {
            if ($(this).data('dir') === curDir) {
                $(this).show();
            } else {
                $(this).hide();
            }
        });

        $s.slideDown(300, function () {
            if (pmSeeMoreRendering[assetId]) return;
            pmSeeMoreRendering[assetId] = true;

            setTimeout(function () {
                updatePmWaveforms(assetId);
                pmSeeMoreRendering[assetId] = false;
            }, 150);
        });
        $btn.text('See Less');
    }
}

function pmDownloadCard(assetId) {
    var asset = wsLiveData[assetId]; if (!asset) { showWarning('No data available', 'Download'); return; }
    var pm = getPmStructuredData(assetId); if (!pm) return;
    // Add PT- prefix to Point Machine asset name
    var baseName = asset.AssetName || assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;
    var csv = ['"Point Machine","' + name + '"', '"Asset ID","' + assetId + '"', '"Downloaded","' + new Date().toLocaleString() + '"', '', '"Direction","Type","Avg","Min","Max","OperationTime(ms)","Count"'];
    var dirs = ['Normal', 'Reverse'], types = ['AC', 'AV', 'BC', 'BV'];
    for (var di = 0; di < dirs.length; di++) for (var ti = 0; ti < types.length; ti++) {
        var td = pm[dirs[di]][types[ti]];
        csv.push('"' + dirs[di] + '","' + types[ti] + '","' + (td.Avg ? td.Avg.value : '') + '","' + (td.Min ? td.Min.value : '') + '","' + (td.Max ? td.Max.value : '') + '","' + (td.OperationTime ? td.OperationTime.value : '') + '","' + (td.Count ? td.Count.value : '') + '"');
    }
    csv.push('', '"RDPMS Attribute","Value"');
    for (var rk in pm.RDPMS) csv.push('"' + rk + '","' + (pm.RDPMS[rk].value || '') + '"');
    csv.push('', '"DataLogger Relay","Value","Status"');
    for (var dk in pm.DataLogger) { var dl = pm.DataLogger[dk]; csv.push('"' + (dl.displayName || dk) + '","' + dl.value + '","' + (dl.isPickup ? 'Pickup' : 'Drop') + '"'); }
    try { saveAs(new Blob(['\uFEFF' + csv.join('\n')], { type: 'text/csv;charset=utf-8;' }), 'PointMachine_' + name + '_' + new Date().toISOString().slice(0, 10) + '.csv'); showSuccess('Downloaded!', 'CSV'); }
    catch (e) { showError('Download failed', 'Error'); }
}

function fnBindPointMachine() {
    var siteId = $('#drpSite').val(), atId = $('#drpAssetType').val(), assetIds = getSelectedAssetIds();
    if (!siteId || siteId === '0' || siteId === '') { showWarning('Please select a site', 'Validation'); return; }
    pmCardsBuilt = {}; pmEventHistory = {};
    for (var ck in pmChartInstances) { if (pmChartInstances[ck]) try { pmChartInstances[ck].destroy(); } catch (e) { } }
    pmChartInstances = {};
    disconnectWebSocket();
    connectWebSocket(siteId, atId, assetIds);
}

// ================================================================
// POINT MACHINE TABLE VIEW - Displays PM data in table format
// Same columns as Point Machine card view
// ================================================================
function renderPointMachineTableView() {

    var assetIds = Object.keys(wsLiveData);
    console.log('[PM Table] Rendering. Assets:', assetIds.length);

    if (assetIds.length === 0) return;
    $('#wsWaiting').remove();

    // Sort assets naturally
    assetIds.sort(function (a, b) {
        return (wsLiveData[a].AssetName || '').localeCompare(
            wsLiveData[b].AssetName || '', undefined, { numeric: true, sensitivity: 'base' }
        );
    });

    var $c = $('#divTelemetryLive');

    // Build table HTML
    var h = '<div class="table-responsive" style="max-height:72vh;overflow-y:auto;">';
    h += '<table class="table mb-0" id="wsLiveTable">';
    assetIds = blankDataLast(assetIds);
    // Determine which ends to show based on all assets
    var globalHasA = false, globalHasB = false;
    for (var i = 0; i < assetIds.length; i++) {
        var pm = getPmStructuredData(assetIds[i]);
        if (pm) {
            var ends = pmGetAvailableEnds(pm);
            if (ends.hasA) globalHasA = true;
            if (ends.hasB) globalHasB = true;
        }
    }
    // Default to both if no data
    if (!globalHasA && !globalHasB) {
        globalHasA = true;
        globalHasB = true;
    }

    // Table Header - Aurora classes only; avoid inline !important so theme remains authoritative
    h += '<thead><tr>';
    h += '<th class="pm-table-head-main" style="min-width:120px;">Point Machine</th>';
    h += '<th class="pm-table-head-main">Direction</th>';

    // A-end columns (only if any asset has A data)
    if (globalHasA) {
        h += '<th class="col-a pm-table-head-a">A Current<br/>(Max/Avg)</th>';
        h += '<th class="col-a pm-table-head-a">A Voltage<br/>(Avg)</th>';
        h += '<th class="col-a pm-table-head-a">A Op Time<br/>(ms)</th>';
        h += '<th class="col-a pm-table-head-a">A Count</th>';
        h += '<th class="col-a pm-table-head-a">A Op Date</th>';
    }

    // B-end columns (only if any asset has B data)
    if (globalHasB) {
        h += '<th class="col-b pm-table-head-b">B Current<br/>(Max/Avg)</th>';
        h += '<th class="col-b pm-table-head-b">B Voltage<br/>(Avg)</th>';
        h += '<th class="col-b pm-table-head-b">B Op Time<br/>(ms)</th>';
        h += '<th class="col-b pm-table-head-b">B Count</th>';
        h += '<th class="col-b pm-table-head-b">B Op Date</th>';
    }

    h += '<th class="pm-table-head-main" style="min-width:240px;">DataLogger</th>';
    h += '<th class="pm-table-head-main">Last Update</th>';
    h += '</tr></thead>';

    // Table Body
    h += '<tbody>';
    for (var ri = 0; ri < assetIds.length; ri++) {
        var aid = assetIds[ri];
        var asset = wsLiveData[aid];
        if (!asset) continue;

        var pm = getPmStructuredData(aid);
        // Add PT- prefix to Point Machine asset name
        var baseName = asset.AssetName || aid;
        var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

        // Determine direction using NWKR vs RWKR voltage comparison (matching backend formula)
        // Backend formula:
        // - Get MAX value from NWKR attributes (IDs: 25, 27, 576, 578)
        // - Get MAX value from RWKR attributes (IDs: 26, 28, 577, 579)
        // - if NWKR_Max > RWKR_Max && NWKR_Max > 10 → NORMAL
        // - if RWKR_Max > NWKR_Max && RWKR_Max > 10 → REVERSE
        var dirResult = determinePmDirection(asset, pm);
        var isReverse = (dirResult.direction === 'REVERSE');
        var useDir = isReverse ? 'Reverse' : 'Normal';
        var dirText = isReverse ? 'Reverse' : 'Normal';

        // Get values for current direction
        var aAcMax = '--', aAcAvg = '--', aAcOT = '--', aAcCount = '--', aAvAvg = '--', aOpDate = '--';
        var bBcMax = '--', bBcAvg = '--', bBcOT = '--', bBcCount = '--', bBvAvg = '--', bOpDate = '--';

        // Operation IDs for timestamp lookup
        var PM_NORMAL_OP_IDS = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002];
        var PM_REVERSE_OP_IDS = [6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];

        if (pm) {
            aAcMax = pm[useDir].AC.Max ? pmFmt(pm[useDir].AC.Max.value) : '--';
            aAcAvg = pm[useDir].AC.Avg ? pmFmt(pm[useDir].AC.Avg.value) : '--';
            aAcOT = pm[useDir].AC.OperationTime ? pmFmtInt(pm[useDir].AC.OperationTime.value) : '--';
            aAcCount = pm[useDir].AC.Count ? pmFmtInt(pm[useDir].AC.Count.value) : '--';
            aAvAvg = pm[useDir].AV.Avg ? pmFmt(pm[useDir].AV.Avg.value, 2) : '--';

            // A-end Operation Date - get from operation IDs only
            var aOpIds = (useDir === 'Normal') ? PM_NORMAL_OP_IDS : PM_REVERSE_OP_IDS;
            var aLatestTs = null;
            var aLatestTime = 0;
            for (var aak in asset.attrs) {
                var aAttr = asset.attrs[aak];
                var aAttrId = parseInt(aAttr.AssetAttributeId || aAttr.AttrId || 0);
                if (aOpIds.indexOf(aAttrId) !== -1) {
                    var aT = aAttr.TimestampDevice || aAttr.Timestamp;
                    if (aT && aT.indexOf('0001-01-01') === -1) {
                        try {
                            var aTime = new Date(aT).getTime();
                            if (aTime > aLatestTime) {
                                aLatestTime = aTime;
                                aLatestTs = aT;
                            }
                        } catch (e) { }
                    }
                }
            }
            if (aLatestTs) {
                try {
                    var dt = new Date(aLatestTs);
                    aOpDate = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' ' + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
                } catch (e) { aOpDate = '--'; }
            }

            bBcMax = pm[useDir].BC.Max ? pmFmt(pm[useDir].BC.Max.value) : '--';
            bBcAvg = pm[useDir].BC.Avg ? pmFmt(pm[useDir].BC.Avg.value) : '--';
            bBcOT = pm[useDir].BC.OperationTime ? pmFmtInt(pm[useDir].BC.OperationTime.value) : '--';
            bBcCount = pm[useDir].BC.Count ? pmFmtInt(pm[useDir].BC.Count.value) : '--';
            bBvAvg = pm[useDir].BV.Avg ? pmFmt(pm[useDir].BV.Avg.value, 2) : '--';

            // B-end Operation Date - get from operation IDs only
            var bOpIds = (useDir === 'Normal') ? PM_NORMAL_OP_IDS : PM_REVERSE_OP_IDS;
            var bLatestTs = null;
            var bLatestTime = 0;
            for (var bak in asset.attrs) {
                var bAttr = asset.attrs[bak];
                var bAttrId = parseInt(bAttr.AssetAttributeId || bAttr.AttrId || 0);
                if (bOpIds.indexOf(bAttrId) !== -1) {
                    var bT = bAttr.TimestampDevice || bAttr.Timestamp;
                    if (bT && bT.indexOf('0001-01-01') === -1) {
                        try {
                            var bTime = new Date(bT).getTime();
                            if (bTime > bLatestTime) {
                                bLatestTime = bTime;
                                bLatestTs = bT;
                            }
                        } catch (e) { }
                    }
                }
            }
            if (bLatestTs) {
                try {
                    var dt = new Date(bLatestTs);
                    bOpDate = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' ' + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
                } catch (e) { bOpDate = '--'; }
            }
        }

        // Direction badge class
        var dirClass = dirText === 'Reverse' ? 'pm-dir-badge reverse' : 'pm-dir-badge normal';

        h += '<tr data-id="' + aid + '">';
        h += '<td class="asset-name"><strong>' + pmEsc(name) + '</strong></td>';
        h += '<td><span class="' + dirClass + '" style="padding:3px 8px;border-radius:4px;font-size:11px;">' + dirText + '</span></td>';

        // A-end data columns
        if (globalHasA) {
            h += '<td class="col-a"><span class="pm-current-val">' + aAcMax + ' / ' + aAcAvg + '</span></td>';
            h += '<td class="col-a">' + aAvAvg + '</td>';
            h += '<td class="col-a">' + aAcOT + '</td>';
            h += '<td class="col-a">' + aAcCount + '</td>';
            h += '<td class="col-a" style="font-size:11px;">' + aOpDate + '</td>';
        }

        // B-end data columns
        if (globalHasB) {
            h += '<td class="col-b"><span class="pm-current-val-b">' + bBcMax + ' / ' + bBcAvg + '</span></td>';
            h += '<td class="col-b">' + bBvAvg + '</td>';
            h += '<td class="col-b">' + bBcOT + '</td>';
            h += '<td class="col-b">' + bBcCount + '</td>';
            h += '<td class="col-b" style="font-size:11px;">' + bOpDate + '</td>';
        }

        // DataLogger column
        var dlRelays = asset.dlRelays || {};
        var dlHtml = '';
        var dlKeys = Object.keys(dlRelays);
        dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; });
        if (dlKeys.length > 0) {
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 5px;font-size:9px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        h += '<td class="dl-cell">' + dlHtml + '</td>';

        var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
        h += '<td class="time-cell">' + ts + '</td>';
        h += '</tr>';
    }
    h += '</tbody></table></div>';

    $c.html(h);
    $('#downloadContainer').show();
    wsUpdatedAssets = {};

    console.log('[PM Table] Rendered', assetIds.length, 'assets. ShowA:', globalHasA, 'ShowB:', globalHasB);
}

// Update PM Table view incrementally (cell-level updates)
function updatePointMachineTableCell(assetId, attrName, value, hasChanged) {
    var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
    if (!$row.length) {
        // Row doesn't exist, need full render
        wsUpdatedAssets[assetId] = true;
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                var vt = $('#drpView').val();
                if (vt === 'Table' && isPointAssetType()) {
                    renderPointMachineTableView();
                }
            }, 200);
        }
        return;
    }

    // Get PM data and update specific cells
    var pm = getPmStructuredData(assetId);
    if (!pm) return;

    // Determine direction using NWKR vs RWKR voltage comparison (matching backend formula)
    var asset = wsLiveData[assetId];
    var dirResult = determinePmDirection(asset, pm);
    var isReverse = (dirResult.direction === 'REVERSE');
    var useDir = isReverse ? 'Reverse' : 'Normal';

    // Update direction cell
    var dirText = isReverse ? 'Reverse' : 'Normal';
    var dirClass = isReverse ? 'pm-dir-badge reverse' : 'pm-dir-badge normal';
    $row.find('td:eq(1) span').attr('class', dirClass).text(dirText);

    // Update values
    var $cells = $row.find('td');
    var cellIdx = 2; // Start after name and direction

    // A-end values
    if ($row.find('td.col-a').length > 0) {
        var aAcMax = pm[useDir].AC.Max ? pmFmt(pm[useDir].AC.Max.value) : '--';
        var aAcAvg = pm[useDir].AC.Avg ? pmFmt(pm[useDir].AC.Avg.value) : '--';
        var aAvAvg = pm[useDir].AV.Avg ? pmFmt(pm[useDir].AV.Avg.value, 2) : '--';
        var aAcOT = pm[useDir].AC.OperationTime ? pmFmtInt(pm[useDir].AC.OperationTime.value) : '--';
        var aAcCount = pm[useDir].AC.Count ? pmFmtInt(pm[useDir].AC.Count.value) : '--';

        // A-end date from operation IDs only
        var PM_NORMAL_OP_IDS = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002];
        var PM_REVERSE_OP_IDS = [6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];
        var aOpIds = (useDir === 'Normal') ? PM_NORMAL_OP_IDS : PM_REVERSE_OP_IDS;
        var aOpDate = '--';
        var aLatestTs = null;
        var aLatestTime = 0;
        if (asset && asset.attrs) {
            for (var aak in asset.attrs) {
                var aAttr = asset.attrs[aak];
                var aAttrId = parseInt(aAttr.AssetAttributeId || aAttr.AttrId || 0);
                if (aOpIds.indexOf(aAttrId) !== -1) {
                    var aT = aAttr.TimestampDevice || aAttr.Timestamp;
                    if (aT && aT.indexOf('0001-01-01') === -1) {
                        try {
                            var aTime = new Date(aT).getTime();
                            if (aTime > aLatestTime) {
                                aLatestTime = aTime;
                                aLatestTs = aT;
                            }
                        } catch (e) { }
                    }
                }
            }
        }
        if (aLatestTs) {
            try {
                var dt = new Date(aLatestTs);
                aOpDate = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' ' + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
            } catch (e) { aOpDate = '--'; }
        }

        $cells.eq(cellIdx).find('.pm-current-val').text(aAcMax + ' / ' + aAcAvg);
        $cells.eq(cellIdx + 1).text(aAvAvg);
        $cells.eq(cellIdx + 2).text(aAcOT);
        $cells.eq(cellIdx + 3).text(aAcCount);
        $cells.eq(cellIdx + 4).text(aOpDate);
        cellIdx += 5;
    }

    // B-end values
    if ($row.find('td.col-b').length > 0) {
        var bBcMax = pm[useDir].BC.Max ? pmFmt(pm[useDir].BC.Max.value) : '--';
        var bBcAvg = pm[useDir].BC.Avg ? pmFmt(pm[useDir].BC.Avg.value) : '--';
        var bBvAvg = pm[useDir].BV.Avg ? pmFmt(pm[useDir].BV.Avg.value, 2) : '--';
        var bBcOT = pm[useDir].BC.OperationTime ? pmFmtInt(pm[useDir].BC.OperationTime.value) : '--';
        var bBcCount = pm[useDir].BC.Count ? pmFmtInt(pm[useDir].BC.Count.value) : '--';

        // B-end date from operation IDs only
        var PM_NORMAL_OP_IDS_B = [1001, 1002, 1004, 1005, 2002, 3002, 3004, 3005, 4002];
        var PM_REVERSE_OP_IDS_B = [6001, 6002, 6004, 6005, 7002, 8002, 8004, 8005, 9002];
        var bOpIds = (useDir === 'Normal') ? PM_NORMAL_OP_IDS_B : PM_REVERSE_OP_IDS_B;
        var bOpDate = '--';
        var bLatestTs = null;
        var bLatestTime = 0;
        var assetB = wsLiveData[assetId];
        if (assetB && assetB.attrs) {
            for (var bak in assetB.attrs) {
                var bAttr = assetB.attrs[bak];
                var bAttrId = parseInt(bAttr.AssetAttributeId || bAttr.AttrId || 0);
                if (bOpIds.indexOf(bAttrId) !== -1) {
                    var bT = bAttr.TimestampDevice || bAttr.Timestamp;
                    if (bT && bT.indexOf('0001-01-01') === -1) {
                        try {
                            var bTime = new Date(bT).getTime();
                            if (bTime > bLatestTime) {
                                bLatestTime = bTime;
                                bLatestTs = bT;
                            }
                        } catch (e) { }
                    }
                }
            }
        }
        if (bLatestTs) {
            try {
                var dt = new Date(bLatestTs);
                bOpDate = dt.getFullYear() + '-' + pmPad2(dt.getMonth() + 1) + '-' + pmPad2(dt.getDate()) + ' ' + pmPad2(dt.getHours()) + ':' + pmPad2(dt.getMinutes()) + ':' + pmPad2(dt.getSeconds());
            } catch (e) { bOpDate = '--'; }
        }

        $cells.filter('.col-b').eq(0).find('.pm-current-val-b').text(bBcMax + ' / ' + bBcAvg);
        $cells.filter('.col-b').eq(1).text(bBvAvg);
        $cells.filter('.col-b').eq(2).text(bBcOT);
        $cells.filter('.col-b').eq(3).text(bBcCount);
        $cells.filter('.col-b').eq(4).text(bOpDate);
    }

    // Update timestamp
    var asset = wsLiveData[assetId];
    var ts = asset && asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
    $row.find('td.time-cell').text(ts);

    // Update DataLogger cell
    var $dlCell = $row.find('td.dl-cell');
    if ($dlCell.length && asset) {
        var dlRelays = asset.dlRelays || {};
        var dlHtml = '';
        var dlKeys = Object.keys(dlRelays);
        dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; });
        if (dlKeys.length > 0) {
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 5px;font-size:9px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        $dlCell.html(dlHtml);
    }

    // Flash animation if changed
    if (hasChanged) {
        $row.addClass('ws-row-new');
        setTimeout(function () {
            $row.removeClass('ws-row-new');
        }, 1200);
    }
}

console.log('[PM] Point Machine view loaded (dynamic ends + table view support).');

$('#modalalertlogs').on('hidden.bs.modal', function () {
    var $select = $('#drpDataloggerSearchAttribute');
    if ($select.hasClass('select2-hidden-accessible')) {
        try { $select.select2('destroy'); } catch (e) { }
    }
});

// ESC key to close modal
$(document).on('keydown', function (e) {
    if (e.key === 'Escape' && $('#modalalertlogs').hasClass('show')) {
        closeEventLogModal();
    }
});

console.log('[TelemetryLive EventLog Fix] Loaded.');

function updateViewTypeRestrictions() {
    // No multi-asset restriction anymore — Graph and Circuit are per-asset icons
    // on each card/row, so they're always implicitly single-asset.
    $('#drpView').removeClass('has-disabled-options');
    var $msg = $('#viewTypeRestrictionMsg');
    if ($msg.length) $msg.removeClass('show').hide();
}

// Store the initial asset order once established
var wsAssetOrder = [];
var wsAssetOrderInitialized = false;

// Natural sort function for consistent ordering
function naturalSort(a, b) {
    var nameA = (wsLiveData[a] && wsLiveData[a].AssetName) ? wsLiveData[a].AssetName : 'Asset ' + a;
    var nameB = (wsLiveData[b] && wsLiveData[b].AssetName) ? wsLiveData[b].AssetName : 'Asset ' + b;

    // Extract numeric parts for proper sorting
    var numA = nameA.match(/\d+/g);
    var numB = nameB.match(/\d+/g);

    if (numA && numB) {
        var numValA = parseInt(numA[numA.length - 1]) || 0;
        var numValB = parseInt(numB[numB.length - 1]) || 0;
        if (numValA !== numValB) {
            return numValA - numValB;
        }
    }

    return nameA.localeCompare(nameB, undefined, { numeric: true, sensitivity: 'base' });
}

// Get sorted asset IDs maintaining consistent order
function getSortedAssetIds(force) {
    var currentIds = Object.keys(wsLiveData);

    // Use the force flag to always re-sort when requested (e.g., after asset type change)
    if (force || !wsAssetOrderInitialized || currentIds.length !== wsAssetOrder.length) {
        // Ensure naturalSort exists; if not, provide a fallback
        if (typeof naturalSort !== 'function') {
            naturalSort = function (a, b) {
                var nameA = (wsLiveData[a] && wsLiveData[a].AssetName) ? wsLiveData[a].AssetName : 'Asset ' + a;
                var nameB = (wsLiveData[b] && wsLiveData[b].AssetName) ? wsLiveData[b].AssetName : 'Asset ' + b;
                var numA = nameA.match(/\d+/g);
                var numB = nameB.match(/\d+/g);
                if (numA && numB) {
                    var numValA = parseInt(numA[numA.length - 1]) || 0;
                    var numValB = parseInt(numB[numB.length - 1]) || 0;
                    if (numValA !== numValB) return numValA - numValB;
                }
                return nameA.localeCompare(nameB, undefined, { numeric: true, sensitivity: 'base' });
            };
        }
        currentIds.sort(naturalSort);
        wsAssetOrder = currentIds.slice();
        wsAssetOrderInitialized = true;
    }

    // Add any new assets that aren't in the order yet
    for (var i = 0; i < currentIds.length; i++) {
        if (wsAssetOrder.indexOf(currentIds[i]) === -1) {
            wsAssetOrder.push(currentIds[i]);
        }
    }

    // Filter to only include assets that still exist
    var existing = wsAssetOrder.filter(function (id) {
        return wsLiveData[id] !== undefined;
    });

    // Defense-in-depth: drop any asset not present in the bulk response so
    // garbage WS assets never reach the table / PM renderers.
    if (typeof isAssetInBulkWhitelist === 'function') {
        existing = existing.filter(function (id) { return isAssetInBulkWhitelist(id); });
    }

    // Apply user's Asset Number multi-select filter, if any.
    if (typeof window._sipFilteredAssetIds === 'function') {
        existing = window._sipFilteredAssetIds(existing);
    }
    //    return existing;
    return blankDataLast(existing);
}
// Track if initial render is complete
var wsInitialRenderComplete = false;
var wsTableStructureBuilt = false;
var wsCurrentTableColumns = [];

// REPLACE: processSingleLiveUpdate - Update single cell without re-rendering
function processSingleLiveUpdateFixed(d) {

    var aid = d.AssetId;
    var attrName = d.AssetAttributeName;
    var attrId = d.AssetAttributeId;

    if (!aid || !attrName) return;

    // Apply filters
    var atFilter = wsCurrentAssetTypeId;
    var aidFilter = wsCurrentFilterAssetIds;

    if (atFilter && atFilter !== '0' && atFilter !== '' && d.AssetTypeId != atFilter) return;
    // FIX: was > 1 — same off-by-one as main processItemsInternal
    if (aidFilter && aidFilter.length > 0 && aidFilter.indexOf(aid.toString()) === -1 && aidFilter[0] !== '' && aidFilter[0] !== '0') return;

    // ── BULK WHITELIST GUARD ── (see processItemsInternal for rationale)
    if (typeof isAssetInBulkWhitelist === 'function' && !isAssetInBulkWhitelist(aid)) return;

    wsMessageCount++;

    // Initialize asset if not exists
    var isNewAsset = !wsLiveData[aid];
    if (isNewAsset) {
        wsLiveData[aid] = {
            AssetId: aid,
            AssetName: (typeof getBulkAssetName === 'function' ? getBulkAssetName(aid, d.AssetName) : (d.AssetName || ('Asset ' + aid))),
            AssetTypeId: (typeof getBulkAssetTypeId === 'function' ? getBulkAssetTypeId(aid, d.AssetTypeId) : d.AssetTypeId),
            SiteId: (typeof getBulkSiteId === 'function' ? getBulkSiteId(aid, d.SiteId) : d.SiteId),
            attrs: {},
            dlRelays: {},
            lastUpdated: new Date()
        };
    }

    // Handle DataLogger type
    if (d.DataType && d.DataType === 'DataLogger') {
        // Use TimestampDevice as primary timestamp
        var timestamp = d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString();

        // Check if this is newer data
        var existingAttr = wsLiveData[aid].attrs[attrName];
        if (existingAttr && existingAttr.TimestampDevice && d.TimestampDevice) {
            var existingTs = new Date(existingAttr.TimestampDevice).getTime();
            var newTs = new Date(d.TimestampDevice).getTime();
            if (newTs > 0 && newTs <= existingTs) {
                return; // Skip older data
            }
        }

        if (typeof processWsDataloggerAttr === 'function') {
            processWsDataloggerAttr(
                aid,
                (typeof getBulkAssetName === 'function' ? getBulkAssetName(aid, d.AssetName) : d.AssetName),
                attrName,
                d.Value,
                timestamp,
                attrId || d.AssetAttributeId || d.AttrId || d.DataloggerAttributeId || attrName
            );
        }

        // FIX: Mark asset as updated for DataLogger messages
        wsLiveData[aid].lastUpdated = new Date();
        wsUpdatedAssets[aid] = true;

        updateWsStats();

        // Update badges only, not full card
        var viewType = $('#drpView').val();
        if (viewType === 'PointMachine' && pmCardsBuilt[aid]) {
            renderPmBadges(aid, wsLiveData[aid].dlRelays || {});
        }
        if (viewType === 'RDPMS' && rdpmsCardsBuilt[aid]) {
            renderDataloggerBadgesForAsset(aid);
        }

        // FIX: Also update DataLogger column in Table view
        if (viewType === 'Table') {
            if (typeof updateDataLoggerColumnInTable === 'function') {
                updateDataLoggerColumnInTable(aid);
            }
        }

        return;
    }

    // ===== TIMESTAMP DEVICE CHECK =====
    // Only process if TimestampDevice is newer than existing (ensures LIVE data only)
    var existingAttr = wsLiveData[aid].attrs[attrName];
    var incomingTimestampDevice = d.TimestampDevice || null;

    //if (existingAttr && existingAttr.TimestampDevice && incomingTimestampDevice) {
    //    var existingDeviceTs = new Date(existingAttr.TimestampDevice).getTime();
    //    var newDeviceTs = new Date(incomingTimestampDevice).getTime();

    //    if (newDeviceTs <= existingDeviceTs) {
    //        return; // Skip older or same timestamp data - always show live
    //    }
    //}
    if (existingAttr && existingAttr.TimestampDevice && incomingTimestampDevice) {
        var existingDeviceTs = new Date(existingAttr.TimestampDevice).getTime();
        var newDeviceTs = new Date(incomingTimestampDevice).getTime();

        if (newDeviceTs < existingDeviceTs) {
            return; // Skip strictly older data only
        }
        // Same timestamp allowed -- value may have changed (aspect transition drop to 0)
    }
    // Track if value changed
    var prevVal = existingAttr ? existingAttr.Value : undefined;
    var hasChanged = (prevVal !== undefined && prevVal !== d.Value);

    // Update attribute value - USE TimestampDevice as primary
    wsLiveData[aid].attrs[attrName] = {
        Value: d.Value,
        AttrId: attrId,
        AssetAttributeId: d.AssetAttributeId || attrId,  // Store AssetAttributeId for operation ID matching
        Timestamp: d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString(),
        TimestampDevice: d.TimestampDevice || null,
        TimestampLocal: d.TimestampLocal || null,
        TimestampEdgeX: d.TimestampEdgeX || null,
        Source: d.DataSource || 'WS',
        changed: hasChanged,
        prevValue: prevVal
    };
    wsLiveData[aid].lastUpdated = new Date();

    // Track new attribute columns
    var isNewColumn = false;
    if (wsAttributeNames.indexOf(attrName) === -1) {
        isNewColumn = true;
        wsAttributeNames.push(attrName);
        wsAttributeOrder[attrName] = attrId || 9999;
        wsAttributeNames.sort(function (a, b) {
            if (parseInt(wsCurrentAssetTypeId) === 1) {
                var ta = getTrackSortOrder(a), tb = getTrackSortOrder(b);
                if (ta !== tb) return ta - tb;
            }
            return (wsAttributeOrder[a] || 9999) - (wsAttributeOrder[b] || 9999);
        });
    }

    updateWsStats();

    var viewType = $('#drpView').val();

    // ====== IPS VIEW: Card-grid incremental update ======
    if (viewType === 'IPS' || isIpsAssetType()) {
        wsUpdatedAssets[aid] = true;
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                var $grid = $('#ipsCardGrid');
                if ($grid.length > 0) {
                    if (typeof window.updateIpsGridIncremental === 'function') {
                        window.updateIpsGridIncremental(Object.keys(wsUpdatedAssets));
                    }
                } else {
                    renderIpsGridView();
                }
                wsTableStructureBuilt = true;
            }, 300);
        }
        return;
    }

    // ====== TABLE VIEW: Cell-level update (NO FULL REFRESH) ======
    if (viewType === 'Table') {
        // Signal → grouped tables (separate render path; not the single
        // #wsLiveTable). Build/refresh via the grouped renderer so the
        // GetAssetInfoListData fetch + per-signature tables actually run.
        if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
            wsUpdatedAssets[aid] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    if (typeof updateSignalGroupedTables === 'function') {
                        updateSignalGroupedTables(Object.keys(wsUpdatedAssets));
                    } else if (typeof renderSignalGroupedTables === 'function') {
                        renderSignalGroupedTables();
                    }
                    wsUpdatedAssets = {};
                }, 300);
            }
            return;
        }

        // Check if this is Point Machine + Table view
        if (isPointAssetType() || window.pmTableMode) {
            var $table = $('#wsLiveTable');

            if ($table.length > 0) {
                // Update PM table cell
                updatePointMachineTableCell(aid, attrName, d.Value, hasChanged);
                return;
            }

            // Initial build for PM table
            wsUpdatedAssets[aid] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    renderPointMachineTableView();
                }, 300);
            }
            return;
        }

        var $table = $('#wsLiveTable');

        // If table exists and no new columns, do cell-level update
        if ($table.length > 0 && !isNewColumn && !isNewAsset) {
            updateTableCellOnly(aid, attrName, d.Value, hasChanged, d.TimestampLocal || d.TimestampChange);
            return;
        }

        // If new asset or new column, add incrementally
        if ($table.length > 0 && isNewAsset && !isNewColumn) {
            addNewTableRow(aid);
            return;
        }

        // Only rebuild table if structure changed (new columns)
        if (isNewColumn && wsTableStructureBuilt) {
            // Queue a single rebuild, don't do it for every message
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    rebuildTableStructure();
                }, 500);
            }
            return;
        }

        // Initial build
        if (!wsTableStructureBuilt) {
            wsUpdatedAssets[aid] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    renderWsTableFixed();
                }, 300);
            }
        }
        return;
    }

    // ====== RDPMS VIEW: Update lights only (NO FULL REFRESH) ======
    if (viewType === 'RDPMS') {
        if (rdpmsCardsBuilt[aid]) {
            // Just update lights - NO card rebuild
            var assetName = (wsLiveData[aid].AssetName || '').toLowerCase();
            if (assetName.indexOf('sh') > -1) {
                updateShuntSignalLightsOnly(aid, attrName, d.Value);
            } else {
                updateMainSignalLightsOnly(aid, attrName, d.Value);
            }
            return;
        }

        // New asset - build card once
        if (isNewAsset) {
            wsUpdatedAssets[aid] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    buildNewRDPMSCards();
                }, 300);
            }
        }
        return;
    }

    // ====== POINT MACHINE VIEW: Update card data only (NO FULL REFRESH) ======
    if (viewType === 'PointMachine') {
        // NOTE: PM row create/update handled exclusively by processItemsInternal
        // pendingPmOps flush. Never fire here -- this path fires per-attribute,
        // before the batch is complete, causing incomplete rows and duplicates.

        if (pmCardsBuilt[aid]) {
            updatePmCardDataOnly(aid, attrName, d.Value, hasChanged);
            return;
        }

        // New asset - build card
        if (isNewAsset) {
            wsUpdatedAssets[aid] = true;
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    buildNewPMCards();
                }, 300);
            }
        }
        return;
    }

    // ====== CIRCUIT VIEW: Update circuit diagram values ======
    if (viewType === 'Circuit') {
        if (typeof updateCircuitFromWebSocket === 'function') {
            updateCircuitFromWebSocket(aid);
        }
        return;
    }
}

// ================================================================
// TABLE VIEW - CELL-LEVEL UPDATE (NO FULL REFRESH)
// ================================================================

function updateTableCellOnly(assetId, attrName, value, hasChanged, timestamp) {
    if (isIpsAssetType()) return; // IPS uses card grid, not table cells
    var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
    if (!$row.length) return;

    // Find cell by attribute name
    var colIdx = wsAttributeNames.indexOf(attrName);
    if (colIdx === -1) return;

    var cellIdx = colIdx + 1; // +1 for asset name column
    var $cells = $row.find('td');
    if (cellIdx >= $cells.length) return;

    var $cell = $($cells[cellIdx]);

    // Format value
    var num = parseFloat(value);
    var disp = '', cls = '';

    if (value === null || value === undefined || value === '') {
        disp = '-';
        cls = 'val-na';
    } else if (isNaN(num)) {
        disp = value;
    } else {
        if (num === 0) disp = '0.0';
        else if (Math.abs(num) >= 100) disp = num.toFixed(2);
        else disp = num.toFixed(2);

        // Danger styling
        if (attrName === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'val-danger';
        else if ((attrName === 'TPR V' || attrName === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'val-danger';
        else if (attrName === 'Charger mA' && num < 100) cls = 'val-danger';
        else if (attrName === 'Choke V' && num > 1.8) cls = 'val-danger';
    }

    // Stale check for this cell
    var _cellStaleMap = wsStaleAttrs[assetId] || {};
    var _cellIsStale = !!_cellStaleMap[attrName];
    if (_cellIsStale) cls += (cls ? ' ' : '') + 'ws-stale-val';

    // Only update if value actually changed
    var currentText = $cell.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
    if (currentText !== disp) {
        // Apply flash animation for changed values
        if (hasChanged) {
            cls += ' ws-cell-flash val-changed-flash';
        }

        var _tip = _getValueTooltip(attrName, num, cls);
        var _marks = _getCellMarkers(cls);
        $cell.attr('class', cls).attr('title', _tip || '').html(disp + _marks);
        // Remove flash after animation
        if (hasChanged) {
            setTimeout(function () {
                $cell.removeClass('ws-cell-flash val-changed-flash');
            }, 1200);
        }
    } else {
        // Value unchanged but stale status may have changed
        var _updCls = $cell.attr('class') || '';
        _updCls = _updCls.replace(/\bws-stale-val\b/g, '').trim();
        if (_cellIsStale) _updCls += ' ws-stale-val';
        $cell.attr('class', _updCls);
        $cell.find('.ws-stale-marker').remove();
        $cell.find('.ws-warn-marker').remove();
        var _marks2 = _getCellMarkers(_updCls);
        if (_marks2) $cell.append(_marks2);
        var _tip2 = _getValueTooltip(attrName, num, _updCls);
        $cell.attr('title', _tip2 || '');
    }

    // Update timestamp cell
    var _tsAsset2 = wsLiveData[assetId];
    var ts = (_tsAsset2 && _tsAsset2.lastUpdated) ? fmtTime(_tsAsset2.lastUpdated) : fmtTime(new Date());
    var $tsCell3 = $row.find('td[data-attr="LastUpdate"]');
    if (!$tsCell3.length) $tsCell3 = $row.find('td.time-cell');
    if ($tsCell3.length) $tsCell3.text(ts);

    // Update derived values for Track
    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    if (assetTypeId === 1) {
        updateDerivedValuesForRow(assetId, $row);
    }
}
function addNewTableRow(assetId) {
    if (isIpsAssetType()) return; // IPS uses card grid, not table rows
    var $tbody = $('#wsLiveTable tbody');
    if (!$tbody.length) return;

    var asset = wsLiveData[assetId];
    if (!asset) return;

    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    var isTrack = (assetTypeId === 1);

    // Build row HTML
    var h = '<tr data-id="' + assetId + '" class="ws-row-new">';
    h += '<td class="asset-name">' + (asset.AssetName || 'Asset ' + assetId) + '</td>';

    for (var ai = 0; ai < wsAttributeNames.length; ai++) {
        var an = wsAttributeNames[ai];
        var ad = asset.attrs[an];
        var raw = ad ? ad.Value : null;
        var cls = '', disp = '';

        if (raw === null || raw === undefined || raw === '') {
            disp = '-'; cls = 'val-na';
        } else {
            var num = parseFloat(raw);
            if (isNaN(num)) { disp = raw; }
            else {
                if (num === 0) disp = '0.0';
                else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                else disp = num.toFixed(2);
            }
        }
        var _tip = _getValueTooltip(an, (typeof num !== 'undefined' ? num : parseFloat(raw)), cls);
        h += '<td class="' + cls + '" data-attr="' + an + '"' + (_tip ? ' title="' + _tip + '"' : '') + '>' + disp + '</td>';
    }

    if (isTrack) {
        var derived = calculateDerivedValues(asset.attrs);
        h += getDerivedCells(derived);
    }

    // DataLogger column for Track and Signal
    if (isTrack || parseInt(wsCurrentAssetTypeId) === 2) {
        var dlRelays = asset.dlRelays || {};
        var dlHtml = '';
        var dlKeys = Object.keys(dlRelays);
        dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; });
        if (dlKeys.length > 0) {
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        h += '<td class="dl-cell">' + dlHtml + '</td>';
    }

    var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
    h += '<td class="time-cell">' + ts + '</td>';
    // Action buttons
    var nrSiteId = asset.SiteId || $('#drpSite').val();
    h += '<td class="tl-asset-actions-cell">' +
        '<span class="tl-asset-actions">' +
        '<button type="button" class="tl-asset-action tl-aa-graph" title="Open Graph" onclick="fnGetAssetGraph(\'' + nrSiteId + '\',\'' + assetId + '\')">' +
        '<i class="fa-solid fa-chart-line"></i></button>' +
        '<button type="button" class="tl-asset-action tl-aa-circuit" title="Open Circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')">' +
        '<i class="fa-solid fa-project-diagram"></i></button>' +
        '</span></td>';
    h += '</tr>';

    // Insert in sorted position
    var inserted = false;
    var sortedIds = getSortedAssetIds();
    var newIdx = sortedIds.indexOf(assetId);

    $tbody.find('tr').each(function (i) {
        var rowId = $(this).data('id');
        var rowIdx = sortedIds.indexOf(String(rowId));
        if (rowIdx > newIdx) {
            $(this).before(h);
            inserted = true;
            return false;
        }
    });

    if (!inserted) {
        $tbody.append(h);
    }

    // Remove new row animation after delay
    setTimeout(function () {
        $('#wsLiveTable tbody tr[data-id="' + assetId + '"]').removeClass('ws-row-new');
    }, 1500);
}
function rebuildTableStructure() {
    // For IPS, rebuild the card grid instead of the table
    if (isIpsAssetType()) { renderIpsGridView(); return; }
    // This is called only when column structure changes
    // Preserve existing data, just rebuild with new columns
    var assetIds = getSortedAssetIds();
    if (assetIds.length === 0) return;

    wsCurrentTableColumns = wsAttributeNames.slice();
    renderWsTableFixed();
}
function updateDerivedValuesForRow(assetId, $row) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var derived = calculateDerivedValues(asset.attrs);
    var $derivedCells = $row.find('td.derived-val');

    if ($derivedCells.length >= 7) {
        $derivedCells.eq(0).text(formatDerivedValue(derived.itcBattCharg));
        $derivedCells.eq(1).text(formatDerivedValue(derived.vtcVarRes));
        $derivedCells.eq(2).text(formatDerivedValue(derived.rtcChFeedEnd));
        $derivedCells.eq(3).text(formatDerivedValue(derived.rtcVarRes));
        $derivedCells.eq(4).text(formatDerivedValue(derived.vtcTr));
        $derivedCells.eq(5).text(formatDerivedValue(derived.ibalst));
        $derivedCells.eq(6).text(formatDerivedValue(derived.rrail));
    }

    // Update DataLogger column
    var $dlCell = $row.find('td.dl-cell');
    if ($dlCell.length) {
        var dlRelays = asset.dlRelays || {};
        var dlKeys = Object.keys(dlRelays);
        var dlHtml = '';
        if (dlKeys.length > 0) {
            for (var di = 0; di < dlKeys.length; di++) {
                var relay = dlRelays[dlKeys[di]];
                var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
            }
        } else {
            dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
        }
        $dlCell.html(dlHtml);
    }
}
function updateMainSignalLightsOnly(assetId, attrName, value) {

    // Instead of updating just one light based on mA > threshold,
    // call the full signal light logic which considers all conditions
    // (RECR/HECR/HHECR/DECR relay states AND mA > ZeroOffset)
    // This ensures the priority order is maintained:
    // 1. RED (RECR or RG mA > offset)
    // 2. DOUBLE YELLOW (HECR && HHECR or HHG mA > offset)
    // 3. SINGLE YELLOW (HECR && !HHECR or HG mA > offset)
    // 4. GREEN (DECR or DG mA > offset)


    if (typeof updateMainSignalLights === 'function') {
        updateMainSignalLights(assetId);
    }

    // Update data table row (cell content only, row visibility handled above)
    updateSignalDataTable(assetId, attrName, value);

    // Update timestamp
    var ts = fmtTime(new Date());
    $('#rdpmsTs_' + assetId).text('Updated: ' + ts);

    // Flash the value that changed
    flashSignalValue(assetId, attrName);

    // Reorder signal cards by aspect in real-time (HG→DG, HG→HHG, HHG→RG)
    if (typeof window.reorderRdpmsCardsByAspect === 'function') {
        window.reorderRdpmsCardsByAspect();
    }
}
function updateShuntSignalLightsOnly(assetId, attrName, value) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var _szEntry = zeroOffsetCache[assetId];
    if (!_szEntry || !_szEntry.fetched) {
        fetchZeroOffsetForAsset(assetId, function () {
            updateShuntSignalLightsOnly(assetId, attrName, value);
        });
    }
    var threshold = (_szEntry && _szEntry.fetched && !isNaN(_szEntry.value))
        ? _szEntry.value
        : parseFloat(asset.ZeroOffsetValue || RDPMS_DEFAULT_THRESHOLD); var numVal = parseFloat(value) || 0;

    // Update shunt lights based on On/Off aspect
    if (attrName === 'On Aspect mA') {
        var $top = $('#rdpmsShuntTop_' + assetId);
        var $right = $('#rdpmsShuntRight_' + assetId);
        var $left = $('#rdpmsShuntLeft_' + assetId);

        if (numVal > threshold) {
            $top.addClass('light-off');
            $right.removeClass('light-off');   // ON
            $left.removeClass('light-off');
        } else {
            $top.addClass('light-off');
            $right.addClass('light-off');      // ← OFF -- this was missing
            $left.addClass('light-off');
            $('#rdpmsTrOn_' + assetId).hide();
        }
    } else if (attrName === 'Off Aspect mA') {
        var $top = $('#rdpmsShuntTop_' + assetId);
        var $right = $('#rdpmsShuntRight_' + assetId);
        var $left = $('#rdpmsShuntLeft_' + assetId);

        if (numVal > threshold) {
            $top.removeClass('light-off');
            $right.removeClass('light-off');
            $left.addClass('light-off');
        }
    }

    // Update data table
    updateSignalDataTable(assetId, attrName, value);

    // Update timestamp
    var ts = fmtTime(new Date());
    $('#rdpmsTs_' + assetId).text('Updated: ' + ts);
}

function updateSignalDataTable(assetId, attrName, value) {
    var numVal = parseFloat(value) || 0;
    var fv = numVal === 0 ? '0.0' : numVal.toFixed(2);

    // Map attribute names to table cell IDs
    var cellMap = {
        'RG mA': 'rdpmsTdRGma_',
        'RG V': 'rdpmsTdRGv_',
        'DG mA': 'rdpmsTdDGma_',
        'DG V': 'rdpmsTdDGv_',
        'HG mA': 'rdpmsTdHGma_',
        'HG V': 'rdpmsTdHGv_',
        'HHG mA': 'rdpmsTdHHGma_',
        'HHG V': 'rdpmsTdHHGv_',
        'PILOT mA': 'rdpmsTdPilotma_',
        'PILOT V': 'rdpmsTdPilotv_',
        'Co_Hg mA': 'rdpmsTdCOma_',
        'Co_Hg V': 'rdpmsTdCOv_',
        'On Aspect mA': 'rdpmsTdOnma_',
        'On Aspect V': 'rdpmsTdOnv_',
        'Off Aspect mA': 'rdpmsTdOffma_',
        'Off Aspect V': 'rdpmsTdOffv_'
    };

    if (cellMap[attrName]) {
        var $cell = $('#' + cellMap[attrName] + assetId);
        if ($cell.length) {
            var unit = attrName.indexOf('V') > -1 ? ' V' : ' mA';
            var label = attrName.replace(' mA', '').replace(' V', '');
            var isStale = !!(wsStaleAttrs[assetId] && wsStaleAttrs[assetId][attrName]);
            var _rdpmsCls = isStale ? 'ws-stale-val' : '';
            var _marks = _getCellMarkers(_rdpmsCls);
            $cell.html('I<sub>Sig' + label + '</sub> : <span class="rdpms-val-flash">' + fv + unit + '</span>' + _marks);
            $cell.toggleClass('ws-stale-val', isStale);
        }
    }
}
function flashSignalValue(assetId, attrName) {
    var $card = $('#rdpmsCard_' + assetId);
    if (!$card.length) return;

    // Flash specific value cells -- remove first to force reflow, then re-add to restart animation
    $card.find('.rdpms-val-flash').each(function () {
        var el = this;
        el.classList.remove('rdpms-flash');
        void el.offsetWidth; // force reflow so animation restarts
        el.classList.add('rdpms-flash');
    });
}
function buildNewRDPMSCards() {
    var assetIds = Object.keys(wsUpdatedAssets);
    var newCardsAdded = false;

    for (var i = 0; i < assetIds.length; i++) {
        var aid = assetIds[i];
        if (rdpmsCardsBuilt[aid]) continue;

        var name = (wsLiveData[aid].AssetName || '').toLowerCase();
        if (name.indexOf('sh') > -1) {
            buildShuntSignalCard(aid);
            updateShuntSignalLights(aid);
        } else {
            buildMainSignalCard(aid);
            updateMainSignalLights(aid);
        }
        rdpmsCardsBuilt[aid] = getCardFingerprint(aid);
        newCardsAdded = true;
    }

    wsUpdatedAssets = {};

    // Trigger reorder after new cards are added
    if (newCardsAdded && typeof window.reorderRdpmsCardsByAspect === 'function') {
        window.reorderRdpmsCardsByAspect();
    }
}
function updatePmCardDataOnly(assetId, attrName, value, hasChanged) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var pm = getPmStructuredData(assetId);
    if (!pm) return;

    // Parse attribute to determine what to update
    var parsed = parsePmAttrName(attrName);
    if (!parsed) {
        // RDPMS attribute - update voltage displays
        updatePmVoltageDisplay(assetId, attrName, value);
        return;
    }

    var decoded = decodePmAttribute(parsed.attrId);
    if (!decoded) return;

    // Update specific table cell based on decoded attribute
    updatePmTableCell(assetId, decoded, value, hasChanged);

    // Update header averages
    updatePmHeaderAverages(assetId, pm);

    // Flash animation on the card
    var $card = $('#pmCard_' + assetId);
    if ($card.length && hasChanged) {
        $card[0].classList.remove('pm-card-flash-anim');
        void $card[0].offsetWidth; // force reflow so animation restarts
        $card[0].classList.add('pm-card-flash-anim');
    }
}

function updatePmVoltageDisplay(assetId, attrName, value) {
    var numVal = parseFloat(value);
    if (isNaN(numVal)) return;

    var formattedVal = numVal.toFixed(2);
    var attrLower = attrName.toLowerCase();

    // Update voltage header displays based on attribute name
    if (attrLower.indexOf('a end') > -1 || attrLower.indexOf('a-end') > -1) {
        if (attrLower.indexOf('nwkr') > -1 && attrLower.indexOf('loc') === -1) {
            $('#pmVnwkrA_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('nwkr') > -1 && attrLower.indexOf('loc') > -1) {
            $('#pmVdcA_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('rwkr') > -1 && attrLower.indexOf('loc') === -1) {
            $('#pmVnwkrA_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('rwkr') > -1 && attrLower.indexOf('loc') > -1) {
            $('#pmVdcA_' + assetId).text(formattedVal);
        }
    } else if (attrLower.indexOf('b end') > -1 || attrLower.indexOf('b-end') > -1) {
        if (attrLower.indexOf('nwkr') > -1 && attrLower.indexOf('loc') === -1) {
            $('#pmVnwkrB_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('nwkr') > -1 && attrLower.indexOf('loc') > -1) {
            $('#pmVdcB_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('rwkr') > -1 && attrLower.indexOf('loc') === -1) {
            $('#pmVnwkrB_' + assetId).text(formattedVal);
        } else if (attrLower.indexOf('rwkr') > -1 && attrLower.indexOf('loc') > -1) {
            $('#pmVdcB_' + assetId).text(formattedVal);
        }
    }
}

function updatePmTableCell(assetId, decoded, value, hasChanged) {
    // This updates specific cells in the PM data table
    // based on direction (Normal/Reverse) and type (AC/AV/BC/BV)

    var $tbody = $('#pmTbody_' + assetId);
    if (!$tbody.length) return;

    // For simplicity, trigger a row update
    var pm = getPmStructuredData(assetId);
    if (pm) {
        buildPmDataRow(assetId, pm);
    }
}

function updatePmHeaderAverages(assetId, pm) {
    // Determine current direction using NWKR vs RWKR voltage comparison (matching backend formula)
    var asset = wsLiveData[assetId];
    var dirResult = determinePmDirection(asset, pm);
    var isReverse = (dirResult.direction === 'REVERSE');
    var dirA = isReverse ? 'Reverse' : 'Normal';
    var dirB = isReverse ? 'Reverse' : 'Normal';

    // Update A-end averages
    $('#pmIptAvgA_' + assetId).text(pm[dirA].AC.Avg ? pmFmt(pm[dirA].AC.Avg.value) : '--');
    $('#pmVptAvgA_' + assetId).text(pm[dirA].AV.Avg ? pmFmt(pm[dirA].AV.Avg.value, 1) : '--');
    $('#pmTptAvgA_' + assetId).text(pm[dirA].AC.OperationTime ? pmFmtInt(pm[dirA].AC.OperationTime.value) : '--');

    // Update B-end averages
    $('#pmIptAvgB_' + assetId).text(pm[dirB].BC.Avg ? pmFmt(pm[dirB].BC.Avg.value) : '--');
    $('#pmVptAvgB_' + assetId).text(pm[dirB].BV.Avg ? pmFmt(pm[dirB].BV.Avg.value, 1) : '--');
    $('#pmTptAvgB_' + assetId).text(pm[dirB].BC.OperationTime ? pmFmtInt(pm[dirB].BC.OperationTime.value) : '--');

    // Update direction badges
    $('#pmDirBadgeA_' + assetId)
        .text('POINT IN ' + dirA.toUpperCase())
        .removeClass('normal reverse')
        .addClass(dirA === 'Normal' ? 'normal' : 'reverse');

    $('#pmDirBadgeB_' + assetId)
        .text('POINT IN ' + dirB.toUpperCase())
        .removeClass('normal reverse')
        .addClass(dirB === 'Normal' ? 'normal' : 'reverse');
}

function buildNewPMCards() {
    var assetIds = Object.keys(wsUpdatedAssets);

    for (var i = 0; i < assetIds.length; i++) {
        var aid = assetIds[i];
        if (pmCardsBuilt[aid]) continue;

        buildPmCard(aid);
        pmCardsBuilt[aid] = true;
        updatePmCard(aid);
    }

    wsUpdatedAssets = {};
}

// ================================================================
// FIXED RENDER FUNCTION - Maintains order and structure
// ================================================================

// ================================================================
// FIXED RENDER FUNCTION - Calls Signal Grouped Tables for Signal assets
// ================================================================

function renderWsTableFixed() {
    // IPS asset type always renders as a card grid -- never as a table
    if (isIpsAssetType()) { renderIpsGridView(); return; }

    // Point Machine → PM-specific table
    if (window.pmTableMode || isPointAssetType()) { renderPmTableView(); return; }

    // SIGNAL → grouped tables (THIS IS THE KEY FIX)
    if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
        console.log('[WS Table] Signal asset type detected - calling renderSignalGroupedTables');
        if (typeof renderSignalGroupedTables === 'function') {
            renderSignalGroupedTables();
        } else {
            console.error('[WS Table] renderSignalGroupedTables is not defined!');
        }
        return;
    }

    var assetIds = getSortedAssetIds();
    console.log('[WS Table Fixed] Rendering. Assets:', assetIds.length);

    if (assetIds.length === 0) return;
    $('#wsWaiting').remove();

    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    var isTrack = (assetTypeId === 1);
    var typeName = 'ASSET';
    if (wsAttributeNames.length === 0 && Object.keys(wsLiveData).length > 0) {
        syncAttributeNamesFromLiveData();
    }
    var first = wsLiveData[assetIds[0]];
    if (first && first.AssetTypeId) {
        switch (parseInt(first.AssetTypeId)) {
            case 1: typeName = 'TRACK'; break;
            case 2: typeName = 'SIGNAL'; break;
            case 3: typeName = 'POINT'; break;
            default:
                var _tn2 = (first.AssetTypeName || '').toUpperCase();
                if (_tn2.indexOf('IPS') > -1) typeName = 'IPS';
                break;
        }
    }

    var $c = $('#divTelemetryLive');

    // Build table header
    var h = '<div class="table-responsive" style="max-height:72vh;overflow-y:auto;">';
    h += '<table class="table mb-0" id="wsLiveTable"><thead><tr>';
    h += '<th style="min-width:90px;">' + typeName + '</th>';

    // Build headers with AliasName
    for (var ci = 0; ci < wsAttributeNames.length; ci++) {
        var attrName = wsAttributeNames[ci];
        var attrId = wsAttributeIds && wsAttributeIds[attrName] ? wsAttributeIds[attrName] : null;
        if (!attrId) {
            for (var assetKey in wsLiveData) {
                var assetData = wsLiveData[assetKey];
                if (assetData && assetData.attrs && assetData.attrs[attrName] && assetData.attrs[attrName].AttrId) {
                    attrId = assetData.attrs[attrName].AttrId;
                    if (wsAttributeIds) wsAttributeIds[attrName] = attrId;
                    break;
                }
            }
        }
        var displayName = getAttrDisplayName(attrName, attrId, null);
        var formattedName = formatAliasName(displayName);
        h += '<th data-col="' + attrName + '" title="' + displayName + '">' + formattedName + '</th>';
    }
    // Add derived value headers for Track assets
    if (isTrack && typeof getDerivedHeaders === 'function') {
        h += getDerivedHeaders();
    }
    // DataLogger column for Track and Signal
    if (isTrack || parseInt(wsCurrentAssetTypeId) === 2) {
        h += '<th style="min-width:100px;">DataLogger</th>';
    }
    h += '<th style="min-width:80px;">Last Update</th><th style="min-width:70px;">Actions</th></tr></thead><tbody>';
    // Build rows in sorted order
    for (var ri = 0; ri < assetIds.length; ri++) {
        var aid = assetIds[ri];
        var asset = wsLiveData[aid];
        if (!asset) continue;

        h += '<tr data-id="' + aid + '">';
        h += '<td class="asset-name">' + (asset.AssetName || 'Asset ' + aid) + '</td>';

        for (var ai = 0; ai < wsAttributeNames.length; ai++) {
            var an = wsAttributeNames[ai];
            var ad = asset.attrs[an];
            var raw = ad ? ad.Value : null;
            var cls = '', disp = '';

            if (raw === null || raw === undefined || raw === '') {
                disp = '-'; cls = 'val-na';
            } else {
                var num = parseFloat(raw);
                if (isNaN(num)) { disp = raw; }
                else {
                    if (num === 0) disp = '0.0';
                    else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                    else disp = num.toFixed(2);

                    // Danger styling
                    if (an === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'val-danger';
                    else if ((an === 'TPR V' || an === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'val-danger';
                    else if (an === 'Charger mA' && num < 100) cls = 'val-danger';
                    else if (an === 'Choke V' && num > 1.8) cls = 'val-danger';
                }
            }
            h += '<td class="' + cls + '" data-attr="' + an + '">' + disp + '</td>';
        }

        if (isTrack && typeof calculateDerivedValues === 'function') {
            var derived = calculateDerivedValues(asset.attrs);
            h += getDerivedCells(derived);
        }

        // DataLogger column for Track and Signal
        if (isTrack || parseInt(wsCurrentAssetTypeId) === 2) {
            var dlRelays = asset.dlRelays || {};
            var dlHtml = '';
            var dlKeys = Object.keys(dlRelays);
            dlKeys.sort(function (a, b) { var ra = dlRelays[a], rb = dlRelays[b]; if (ra.isPickup && !rb.isPickup) return -1; if (!ra.isPickup && rb.isPickup) return 1; return 0; });
            if (dlKeys.length > 0) {
                for (var di = 0; di < dlKeys.length; di++) {
                    var relay = dlRelays[dlKeys[di]];
                    var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                    var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                    dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + '" style="margin:1px;padding:2px 6px;font-size:10px;">' + (relay.displayName || dlKeys[di]) + ': ' + badgeText + '</span> ';
                }
            } else {
                dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
            }
            h += '<td class="dl-cell">' + dlHtml + '</td>';
        }

        var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
        h += '<td class="time-cell">' + ts + '</td>';
        // Action buttons
        var rSiteId = asset.SiteId || $('#drpSite').val();
        h += '<td class="tl-asset-actions-cell">' +
            '<span class="tl-asset-actions">' +
            '<button type="button" class="tl-asset-action tl-aa-graph" title="Open Graph" onclick="fnGetAssetGraph(\'' + rSiteId + '\',\'' + aid + '\')">' +
            '<i class="fa-solid fa-chart-line"></i></button>' +
            '<button type="button" class="tl-asset-action tl-aa-circuit" title="Open Circuit" onclick="fnGetAssetCircuit(\'' + aid + '\')">' +
            '<i class="fa-solid fa-project-diagram"></i></button>' +
            '</span></td>';
        h += '</tr>';
    }

    h += '</tbody></table></div>';
    $c.html(h);

    wsTableStructureBuilt = true;
    wsCurrentTableColumns = wsAttributeNames.slice();
    wsUpdatedAssets = {};

    $('#downloadContainer').show();
}
// ================================================================
// FIXED PROCESS ITEMS - Batch processing with incremental updates
// ================================================================

function processItemsFixed(items) {
    var atFilter = wsCurrentAssetTypeId;
    var aidFilter = wsCurrentFilterAssetIds;
    var processedCount = 0;
    var newAssetsAdded = false;
    var newColumnsAdded = false;

    for (var i = 0; i < items.length; i++) {
        var d = items[i];
        var aid = d.AssetId;
        var attrName = d.AssetAttributeName;
        var attrId = d.AssetAttributeId;

        if (!aid || !attrName) continue;

        // Apply filters
        if (atFilter && atFilter !== '0' && atFilter !== '' && d.AssetTypeId) {
            if (String(d.AssetTypeId) !== String(atFilter)) continue;
        }

        // FIX: same off-by-one as main processItemsInternal — was > 1, now > 0
        if (aidFilter && aidFilter.length > 0 && aidFilter[0] !== '' && aidFilter[0] !== '0') {
            if (aidFilter.indexOf(String(aid)) === -1) continue;
        }

        // ── BULK WHITELIST GUARD ── (see processItemsInternal for rationale)
        if (typeof isAssetInBulkWhitelist === 'function' && !isAssetInBulkWhitelist(aid)) {
            continue;
        }

        wsMessageCount++;
        processedCount++;

        // Check if new asset
        var isNewAsset = !wsLiveData[aid];
        if (isNewAsset) {
            newAssetsAdded = true;
            wsLiveData[aid] = {
                AssetId: aid,
                AssetName: (typeof getBulkAssetName === 'function' ? getBulkAssetName(aid, d.AssetName) : (d.AssetName || ('Asset ' + aid))),
                AssetTypeId: (typeof getBulkAssetTypeId === 'function' ? getBulkAssetTypeId(aid, d.AssetTypeId) : d.AssetTypeId),
                SiteId: (typeof getBulkSiteId === 'function' ? getBulkSiteId(aid, d.SiteId) : d.SiteId),
                attrs: {},
                dlRelays: {},
                lastUpdated: new Date()
            };
        }

        // Handle DataLogger
        if (d.DataType && d.DataType === 'DataLogger') {
            // Use TimestampDevice as primary
            var timestamp = d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString();

            // Check if this is newer data
            var existingAttr = wsLiveData[aid].attrs[attrName];
            if (existingAttr && existingAttr.TimestampDevice && d.TimestampDevice) {
                var existingTs = new Date(existingAttr.TimestampDevice).getTime();
                var newTs = new Date(d.TimestampDevice).getTime();
                if (newTs > 0 && newTs <= existingTs) {
                    continue; // Skip older data
                }
            }

            if (typeof processWsDataloggerAttr === 'function') {
                processWsDataloggerAttr(aid, d.AssetName, attrName, d.Value, timestamp);
            }

            // FIX: Mark asset as updated and increment counter for DataLogger messages
            // This ensures subsequent signal values are properly processed
            wsMessageCount++;
            processedCount++;
            wsLiveData[aid].lastUpdated = new Date();
            wsUpdatedAssets[aid] = true;

            continue;
        }

        // Check if new column
        if (wsAttributeNames.indexOf(attrName) === -1) {
            newColumnsAdded = true;
            wsAttributeNames.push(attrName);
            wsAttributeOrder[attrName] = attrId || 9999;
            if (typeof wsAttributeIds !== 'undefined') {
                wsAttributeIds[attrName] = attrId;
            }
        }

        // ===== TIMESTAMP DEVICE CHECK =====
        // Only process if TimestampDevice is newer (ensures LIVE data only)
        var existingAttr = wsLiveData[aid].attrs[attrName];
        var incomingTimestampDevice = d.TimestampDevice || null;

        if (existingAttr && existingAttr.TimestampDevice && incomingTimestampDevice) {
            var existingDeviceTs = new Date(existingAttr.TimestampDevice).getTime();
            var newDeviceTs = new Date(incomingTimestampDevice).getTime();

            if (newDeviceTs < existingDeviceTs) {
                continue; // Skip strictly older data only
            }
            // Allow same-timestamp messages through -- value may have changed (e.g. aspect drop to 0)
        }
        // Track value change
        var prevVal = existingAttr ? existingAttr.Value : undefined;
        var hasChanged = (prevVal !== undefined && prevVal !== d.Value);

        // Update attribute - USE TimestampDevice as primary
        wsLiveData[aid].attrs[attrName] = {
            Value: d.Value,
            AttrId: attrId,
            AssetAttributeId: d.AssetAttributeId || attrId,  // Store AssetAttributeId for operation ID matching
            Timestamp: d.TimestampDevice || d.TimestampLocal || d.TimestampChange || new Date().toISOString(),
            TimestampDevice: d.TimestampDevice || null,
            TimestampLocal: d.TimestampLocal || null,
            TimestampEdgeX: d.TimestampEdgeX || null,
            Source: d.DataSource || 'WS',
            changed: hasChanged,
            prevValue: prevVal
        };
        wsLiveData[aid].lastUpdated = new Date();
        wsUpdatedAssets[aid] = true;
    }

    //if (processedCount === 0) return;
    if (processedCount === 0) {
        // Even if no new data was processed in this batch, still trigger
        // signal light update if we are in RDPMS view -- previous data in
        // wsLiveData is valid and lights may need to reflect current state
        var _vt = $('#drpView').val();
        if (_vt === 'RDPMS' && $('#rdpmsMainContainer').length > 0) {
            var _ids = Object.keys(rdpmsCardsBuilt);
            for (var _i = 0; _i < _ids.length; _i++) {
                var _uid = _ids[_i];
                var _name = (wsLiveData[_uid] && wsLiveData[_uid].AssetName || '').toLowerCase();
                if (_name.indexOf('sh') > -1) {
                    updateShuntSignalLights(_uid);
                } else {
                    updateMainSignalLights(_uid);
                }
            }
        }
        return;
    }

    // Sort attributes
    wsAttributeNames.sort(function (a, b) {
        if (parseInt(wsCurrentAssetTypeId) === 1) {
            var ta = getTrackSortOrder(a), tb = getTrackSortOrder(b);
            if (ta !== tb) return ta - tb;
        }
        return (wsAttributeOrder[a] || 9999) - (wsAttributeOrder[b] || 9999);
    });

    updateWsStats();

    var viewType = $('#drpView').val();

    // Handle based on view type and what changed
    if (viewType === 'IPS' || isIpsAssetType()) {
        // IPS: render card grid (debounced)
        var $grid = $('#ipsCardGrid');
        if ($grid.length > 0) {
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    if (typeof window.updateIpsGridIncremental === 'function') {
                        window.updateIpsGridIncremental(Object.keys(wsUpdatedAssets));
                    }
                    wsUpdatedAssets = {};
                }, 300);
            }
        } else {
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    renderIpsGridView();
                    wsUpdatedAssets = {};
                }, 300);
            }
        }
        return;
    }

    // Inside processItemsFixed function, find the Table view section and update it:

    if (viewType === 'Table') {
        // ── Signal → grouped tables
        if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
            // FIX-10: If no assets were updated in this batch AND the grouped
            // table already exists, skip the render — nothing changed.
            // A full render is only needed when:
            //  (a) there are actual updates, or
            //  (b) the table hasn't been built yet (.sig-group-wrap missing)
            var _hasUpdates = Object.keys(wsUpdatedAssets).length > 0;
            var _tableExists = $('#divTelemetryLive .sig-group-wrap').length > 0;
            if (!_hasUpdates && _tableExists) {
                return; // nothing to do
            }
            console.log('[SigGroup:processItemsFixed] Signal+Table detected, scheduling grouped render');
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    var $wrap = $('#divTelemetryLive .sig-group-wrap');
                    if ($wrap.length > 0 && typeof updateSignalGroupedTables === 'function') {
                        updateSignalGroupedTables(Object.keys(wsUpdatedAssets));
                    } else if (typeof renderSignalGroupedTables === 'function') {
                        renderSignalGroupedTables();
                    } else {
                        console.error('[SigGroup] renderSignalGroupedTables is not defined!');
                    }
                    wsUpdatedAssets = {};
                }, 300);
            }
            return;
        }


        // Check if this is Point Machine + Table view
        if (isPointAssetType() || window.pmTableMode) {
            var $table = $('#wsLiveTable');

            if ($table.length > 0) {
                // Incremental updates for PM table
                var updatedIds = Object.keys(wsUpdatedAssets);
                for (var ui = 0; ui < updatedIds.length; ui++) {
                    var uid = updatedIds[ui];
                    var $row = $table.find('tbody tr[data-id="' + uid + '"]');

                    if ($row.length) {
                        updatePointMachineTableCell(uid, null, null, true);
                    } else {
                        // New asset - need full render
                        if (!wsRenderTimer) {
                            wsRenderTimer = setTimeout(function () {
                                wsRenderTimer = null;
                                renderPointMachineTableView();
                            }, 300);
                        }
                    }
                }
                wsUpdatedAssets = {};
                return;
            }

            // Initial render for PM table
            if (!wsRenderTimer) {
                wsRenderTimer = setTimeout(function () {
                    wsRenderTimer = null;
                    renderPointMachineTableView();
                }, 300);
            }
            return;
        }

        var $table = $('#wsLiveTable');

        if ($table.length > 0 && !newColumnsAdded) {
            // Incremental updates only
            var updatedIds = Object.keys(wsUpdatedAssets);
            for (var ui = 0; ui < updatedIds.length; ui++) {
                var uid = updatedIds[ui];
                var $row = $table.find('tbody tr[data-id="' + uid + '"]');

                if ($row.length) {
                    // Update existing row cells
                    updateExistingRowCells(uid, $row);
                } else if (newAssetsAdded) {
                    // Add new row
                    addNewTableRow(uid);
                }
            }
            wsUpdatedAssets = {};
            return;
        }

        // Need full rebuild only if columns changed or initial render
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                renderWsTableFixed();
            }, 300);
        }
        return;
    }

    if (viewType === 'RDPMS') {
        var $container = $('#rdpmsMainContainer');

        if ($container.length > 0) {
            var updatedIds = Object.keys(wsUpdatedAssets);
            var newCardsAdded = false;

            for (var ui = 0; ui < updatedIds.length; ui++) {
                var uid = updatedIds[ui];
                if (rdpmsCardsBuilt[uid]) {
                    // Update lights only
                    var assetName = (wsLiveData[uid].AssetName || '').toLowerCase();
                    if (assetName.indexOf('sh') > -1) {
                        updateShuntSignalLights(uid);
                    } else {
                        updateMainSignalLights(uid);
                    }
                } else {
                    // Build new card
                    var assetName = (wsLiveData[uid].AssetName || '').toLowerCase();
                    if (assetName.indexOf('sh') > -1) {
                        buildShuntSignalCard(uid);
                        updateShuntSignalLights(uid);
                    } else {
                        buildMainSignalCard(uid);
                        updateMainSignalLights(uid);
                    }
                    rdpmsCardsBuilt[uid] = getCardFingerprint(uid);
                    newCardsAdded = true;
                }
            }
            wsUpdatedAssets = {};

            // Trigger reorder after processing updates
            if (typeof window.reorderRdpmsCardsByAspect === 'function') {
                window.reorderRdpmsCardsByAspect();
            }
            return;
        }

        // Initial render
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                renderRDPMSView();
            }, 300);
        }
        return;
    }

    if (viewType === 'PointMachine') {
        var $container = $('#pmMainContainer');

        if ($container.length > 0) {
            var updatedIds = Object.keys(wsUpdatedAssets);
            for (var ui = 0; ui < updatedIds.length; ui++) {
                var uid = updatedIds[ui];
                if (pmCardsBuilt[uid]) {
                    updatePmCard(uid);
                } else {
                    buildPmCard(uid);
                    pmCardsBuilt[uid] = true;
                    updatePmCard(uid);
                }
            }
            wsUpdatedAssets = {};
            return;
        }

        // Initial render
        if (!wsRenderTimer) {
            wsRenderTimer = setTimeout(function () {
                wsRenderTimer = null;
                renderPointMachineView();
            }, 300);
        }
        return;
    }
}

function updateExistingRowCells(assetId, $row) {
    var asset = wsLiveData[assetId];
    if (!asset) return;

    var assetTypeId = parseInt(wsCurrentAssetTypeId) || 0;
    var isTrack = (assetTypeId === 1);
    var isSignal = (assetTypeId === 2);
    var ifMa = 0, irMa = 0, tprV = 0;

    for (var ai = 0; ai < wsAttributeNames.length; ai++) {
        var an = wsAttributeNames[ai];
        var ad = asset.attrs[an];
        var $cell = $row.find('td[data-attr="' + an + '"]');
        // Positional fallback: col 0 = asset name, attrs start at col 1.
        // FIX-3: was ai + 2 (off-by-one — actions are at the END, not col 1).
        if (!$cell.length) $cell = $row.find('td').eq(ai + 1);
        if (!$cell.length) continue;

        var raw = ad ? ad.Value : null;
        var cls = '', disp = '';

        if (raw === null || raw === undefined || raw === '') {
            disp = '-'; cls = 'val-na';
        } else {
            var num = parseFloat(raw);
            if (isNaN(num)) { disp = raw; }
            else {
                if (num === 0) disp = '0.0';
                else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                else disp = num.toFixed(2);

                if (an === 'If mA') ifMa = num;
                else if (an === 'Ir mA') irMa = num;
                else if (an === 'TPR V') tprV = num;

                // FIX-5b: Use getValueColorClass for consistency with buildTableRow
                var attrId = ad ? (ad.AssetAttributeId || ad.AttrId) : null;
                if (typeof getValueColorClass === 'function') {
                    cls = getValueColorClass(attrId, num);
                }
            }
        }

        // Stale check
        var _5bStaleMap = wsStaleAttrs[assetId] || {};
        var _5bIsStale = !!_5bStaleMap[an];
        if (_5bIsStale) cls += (cls ? ' ' : '') + 'ws-stale-val';

        // Only update if changed
        var _5bText = $cell.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
        if (_5bText !== disp) {
            if (ad && ad.changed) {
                cls += ' ws-cell-flash val-changed-flash';
                ad.changed = false;
                (function ($c) {
                    setTimeout(function () {
                        $c.removeClass('ws-cell-flash val-changed-flash');
                    }, 1200);
                })($cell);
            }
            var _5bTip = _getValueTooltip(an, (typeof num !== 'undefined' ? num : NaN), cls);
            var _5bMarks = _getCellMarkers(cls);
            $cell.attr('class', cls).attr('title', _5bTip || '').html(disp + _5bMarks);
        }
    }

    // Update derived values for Track
    if (isTrack && typeof calculateDerivedValues === 'function') {
        updateDerivedValuesForRow(assetId, $row);
    }

    // FIX: Update DataLogger column for Track and Signal assets
    if ((isTrack || isSignal) && typeof window.updateDataLoggerColumnInTable === 'function') {
        window.updateDataLoggerColumnInTable(assetId);
    }

    // Update timestamp
    var ts = asset.lastUpdated ? fmtTime(asset.lastUpdated) : '--';
    var $tsCell4 = $row.find('td[data-attr="LastUpdate"]');
    if (!$tsCell4.length) $tsCell4 = $row.find('td.time-cell');
    if ($tsCell4.length) $tsCell4.text(ts);
}

// ================================================================
// OVERRIDE ORIGINAL FUNCTIONS
// ================================================================

// Store original functions for reference
var _originalProcessSingleLiveUpdate = typeof processSingleLiveUpdate === 'function' ? processSingleLiveUpdate : null;
var _originalProcessItems = typeof processItems === 'function' ? processItems : null;
var _originalRenderWsTable = typeof renderWsTable === 'function' ? renderWsTable : null;

// Override with fixed versions
processSingleLiveUpdate = processSingleLiveUpdateFixed;
processItems = processItemsFixed;
renderWsTable = renderWsTableFixed;

console.log('[TelemetryLive Fixes] Applied:');
console.log('  1. Signal sequence ordering - Assets now maintain consistent sorted order');
console.log('  2. Incremental updates - Only changed cells update, no full page refresh');
console.log('  3. Table structure preserved - Rows added/updated individually');
console.log('[PM] Point Machine view loaded (dynamic ends + table view support).');

// ================================================================
// WEBSOCKET STABILITY FIX - PREVENTS UNWANTED RECONNECTIONS
// 
// ROOT CAUSE: The heartbeat timers were reconnecting the WebSocket
// when no new messages arrived within 30-60 seconds. But an idle 
// server (no data changes) is NORMAL — it doesn't mean the 
// connection is broken.
//
// FIX: Only reconnect when the socket's readyState is actually
// CLOSED or CLOSING — never because of message silence alone.
//
// PASTE THIS AT THE VERY END OF TelemetryLive.js
// (It overrides the problematic IIFE and heartbeat logic)
// ================================================================
(function applyWebSocketStabilityFix() {
    'use strict';

    console.log('[WS-StableFix] Applying WebSocket stability patch...');

    // ──────────────────────────────────────────────────────────────
    // 1. CONFIGURATION
    // ──────────────────────────────────────────────────────────────
    var WS_HEARTBEAT_CHECK_INTERVAL = 15000;   // Check every 15 seconds
    var WS_RECONNECT_BASE_DELAY = 3000;    // Start at 3s
    var WS_RECONNECT_MAX_DELAY = 30000;   // Cap at 30s
    var WS_MAX_RECONNECT_ATTEMPTS = 10;

    // ──────────────────────────────────────────────────────────────
    // 2. INTERNAL STATE (private to this closure)
    // ──────────────────────────────────────────────────────────────
    var _isConnecting = false;   // Prevents concurrent connect calls
    var _isManualDisconnect = false;   // Set by disconnectWebSocket()
    var _heartbeatTimer = null;
    var _safetyTimer = null;
    var _reconnectTimer = null;

    // ──────────────────────────────────────────────────────────────
    // 3. HELPER: Check if socket is truly alive
    //    The ONLY reliable test — readyState === WebSocket.OPEN
    // ──────────────────────────────────────────────────────────────
    function isSocketAlive() {
        return window.wsConnection &&
            window.wsConnection.readyState === WebSocket.OPEN;
    }

    // ──────────────────────────────────────────────────────────────
    // 4. HELPER: Clear ALL reconnect/heartbeat/safety timers
    // ──────────────────────────────────────────────────────────────
    function clearAllTimers() {
        if (_heartbeatTimer) { clearInterval(_heartbeatTimer); _heartbeatTimer = null; }
        if (_safetyTimer) { clearInterval(_safetyTimer); _safetyTimer = null; }
        if (_reconnectTimer) { clearTimeout(_reconnectTimer); _reconnectTimer = null; }

        // Also clear the timers set by original code (they may still be running)
        if (window.wsHeartbeatInterval) { clearInterval(window.wsHeartbeatInterval); window.wsHeartbeatInterval = null; }
        if (window.wsSafetyInterval) { clearInterval(window.wsSafetyInterval); window.wsSafetyInterval = null; }
        if (window.wsReconnectTimer) { clearTimeout(window.wsReconnectTimer); window.wsReconnectTimer = null; }
    }

    // ──────────────────────────────────────────────────────────────
    // 5. HELPER: Schedule a reconnect with exponential back-off
    // ──────────────────────────────────────────────────────────────
    function scheduleReconnect() {
        if (_isManualDisconnect) {
            console.log('[WS-StableFix] Manual disconnect — will NOT reconnect.');
            return;
        }

        var attempts = window.wsReconnectAttempts || 0;
        if (attempts >= WS_MAX_RECONNECT_ATTEMPTS) {
            console.error('[WS-StableFix] Max reconnect attempts (' + WS_MAX_RECONNECT_ATTEMPTS + ') reached. Click Search to retry.');
            if (typeof showWsStatus === 'function') showWsStatus('error');
            if (typeof showError === 'function') showError('Connection lost. Click Search to reconnect.', 'WebSocket');
            return;
        }

        var delay = Math.min(
            WS_RECONNECT_BASE_DELAY * Math.pow(1.5, attempts),
            WS_RECONNECT_MAX_DELAY
        );

        window.wsReconnectAttempts = attempts + 1;

        console.log('[WS-StableFix] Reconnecting in ' + (delay / 1000).toFixed(1) + 's  (attempt ' + window.wsReconnectAttempts + '/' + WS_MAX_RECONNECT_ATTEMPTS + ')');
        if (typeof showWsStatus === 'function') showWsStatus('reconnecting');

        _reconnectTimer = setTimeout(function () {
            _reconnectTimer = null;
            window.connectWebSocket(
                window.wsConnParams.siteId,
                window.wsConnParams.assetTypeId,
                window.wsConnParams.assetIds
            );
        }, delay);
    }

    // ──────────────────────────────────────────────────────────────
    // 6. HEARTBEAT: Only reconnects when the socket is ACTUALLY dead
    //    — never because of message silence on an idle server
    // ──────────────────────────────────────────────────────────────
    function startHeartbeat() {
        clearAllTimers();

        _heartbeatTimer = setInterval(function () {
            if (!window.wsIsConnected) return;  // Already disconnected, skip

            if (isSocketAlive()) {
                // Socket is OPEN — server may simply have no new data.
                // This is completely normal. Do nothing.
                return;
            }

            // Socket is CLOSED / CLOSING / CONNECTING — genuinely broken
            console.warn('[WS-StableFix] Heartbeat detected dead socket (readyState=' +
                (window.wsConnection ? window.wsConnection.readyState : 'null') + ')');

            window.wsIsConnected = false;
            _isConnecting = false;

            // Clean up the dead socket reference
            if (window.wsConnection) {
                try {
                    window.wsConnection.onopen = null;
                    window.wsConnection.onmessage = null;
                    window.wsConnection.onclose = null;
                    window.wsConnection.onerror = null;
                    window.wsConnection.close();
                } catch (e) { /* already closed */ }
                window.wsConnection = null;
            }

            scheduleReconnect();
        }, WS_HEARTBEAT_CHECK_INTERVAL);
    }

    // ──────────────────────────────────────────────────────────────
    // 7. SAFETY RENDER: If data exists but UI hasn't rendered yet
    //    (runs only a few times after connect, then stops)
    // ──────────────────────────────────────────────────────────────
    function startSafetyRender() {
        if (_safetyTimer) { clearInterval(_safetyTimer); _safetyTimer = null; }

        var safetyChecks = 0;
        var MAX_SAFETY_CHECKS = 10;  // Stop after ~30 seconds

        _safetyTimer = setInterval(function () {
            safetyChecks++;

            // Stop checking once the waiting message is gone or we hit the limit
            if ($('#wsWaiting').length === 0 || safetyChecks >= MAX_SAFETY_CHECKS) {
                clearInterval(_safetyTimer);
                _safetyTimer = null;
                return;
            }

            // Data exists but UI still shows "Waiting…" — force one render
            if (Object.keys(window.wsLiveData || {}).length > 0) {
                console.log('[WS-StableFix] Safety render: data exists but UI not rendered.');
                var viewType = $('#drpView').val();
                if (viewType === 'Table') { if (typeof renderWsTable === 'function') renderWsTable(); }
                else if (viewType === 'RDPMS') { if (typeof renderRDPMSView === 'function') renderRDPMSView(); }
                else if (viewType === 'PointMachine') { if (typeof renderPointMachineView === 'function') renderPointMachineView(); }
                else if (viewType === 'IPS') { if (typeof renderIpsGridView === 'function') renderIpsGridView(); }
                else if (viewType === 'Cards') {
                    // 'Cards' is the default mode for PM/Signal/IPS. Dispatch
                    // to the asset-type-appropriate rich renderer (same logic
                    // as executeUIUpdate's 'Cards' branch).
                    if (typeof isSignalAssetType === 'function' && isSignalAssetType()) {
                        if (typeof renderRDPMSView === 'function') renderRDPMSView();
                    } else if (typeof isPointAssetType === 'function' && isPointAssetType()) {
                        if (typeof renderPointMachineView === 'function') renderPointMachineView();
                    } else if (typeof isIpsAssetType === 'function' && isIpsAssetType()) {
                        if (typeof renderIpsGridView === 'function') renderIpsGridView();
                    } else if (typeof fnBindTrackCards === 'function') {
                        fnBindTrackCards();
                    }
                }

                // Stop after first successful trigger
                clearInterval(_safetyTimer);
                _safetyTimer = null;
            }
        }, 3000);
    }

    // ──────────────────────────────────────────────────────────────
    // 8. OVERRIDE: disconnectWebSocket
    //    Sets the manual-disconnect flag so heartbeat won't reconnect
    // ──────────────────────────────────────────────────────────────
    var _origDisconnect = window.disconnectWebSocket;

    window.disconnectWebSocket = function () {
        console.log('[WS-StableFix] disconnectWebSocket() called');
        _isManualDisconnect = true;
        _isConnecting = false;
        clearAllTimers();
        stopStalePoll();
        // Also clear queue timers from original code
        if (window.wsQueueProcessTimer) { clearTimeout(window.wsQueueProcessTimer); window.wsQueueProcessTimer = null; }
        if (window.wsUIUpdateTimer) { clearTimeout(window.wsUIUpdateTimer); window.wsUIUpdateTimer = null; }
        if (window._circuitMqttBridgeInterval) { clearInterval(window._circuitMqttBridgeInterval); window._circuitMqttBridgeInterval = null; }

        // Reset queue state
        window.wsMessageQueue = [];
        window.wsIsProcessingQueue = false;
        window.wsPendingUIUpdate = false;
        window.wsStreamingActive = false;
        window.wsViewSwitchPending = false;
        window.wsRAFScheduled = false;

        // Close the socket cleanly
        if (window.wsConnection) {
            window.wsConnection.onopen = null;
            window.wsConnection.onmessage = null;
            window.wsConnection.onclose = null;
            window.wsConnection.onerror = null;
            try { window.wsConnection.close(1000); } catch (e) { /* already closed */ }
            window.wsConnection = null;
        }
        window.wsIsConnected = false;
        stopStalePoll();

        $('#wsStatusBar').hide().empty();
    };

    // ──────────────────────────────────────────────────────────────
    // 9. OVERRIDE: connectWebSocket  (THE KEY FIX)
    //
    //    • Idempotency: if already connected to the same URL, skip.
    //    • Prevents concurrent connect calls.
    //    • Heartbeat only checks readyState, never message timestamps.
    //    • Does NOT reset wsLiveData unless the URL actually changes.
    // ──────────────────────────────────────────────────────────────
    window.connectWebSocket = function (siteId, assetTypeId, assetIds) {

        var newUrl = buildWebSocketUrl(siteId, assetTypeId, assetIds);

        // ── FIX: Upgrade ws:// → wss:// on HTTPS pages (mixed-content protection) ──
        if (window.location.protocol === 'https:' && newUrl.indexOf('ws://') === 0) {
            newUrl = newUrl.replace('ws://', 'wss://');
        }

        // ── FIX: Append auth token for secure connections ──
        var _isSecurePage = (typeof isSecure !== 'undefined') ? isSecure : (window.location.protocol === 'https:');
        if (_isSecurePage && window.APP_CONFIG && window.APP_CONFIG.WebSocketAuthToken) {
            var _sep = newUrl.indexOf('?') > -1 ? '&' : '?';
            newUrl = newUrl + _sep + 'token=' + encodeURIComponent(window.APP_CONFIG.WebSocketAuthToken);
        }

        // ── IDEMPOTENCY: Already connected to the same URL? Do nothing. ──
        if (window.wsConnection &&
            window.wsConnection.url === newUrl &&
            (window.wsConnection.readyState === WebSocket.OPEN ||
                window.wsConnection.readyState === WebSocket.CONNECTING)) {

            console.log('[WS-StableFix] Already connected to', newUrl, '— skipping.');
            // Just sync params so reconnect logic can reuse them
            window.wsConnParams = { siteId: siteId, assetTypeId: assetTypeId, assetIds: assetIds || [] };
            _isConnecting = false;
            return;
        }

        // ── Prevent concurrent connects ──
        if (_isConnecting) {
            console.log('[WS-StableFix] Connection already in progress — skipping.');
            return;
        }
        _isConnecting = true;
        _isManualDisconnect = false;

        // ── Disconnect previous socket cleanly ──
        clearAllTimers();
        if (window.wsConnection) {
            window.wsConnection.onopen = null;
            window.wsConnection.onmessage = null;
            window.wsConnection.onclose = null;
            window.wsConnection.onerror = null;
            try { window.wsConnection.close(1000); } catch (e) { /* ok */ }
            window.wsConnection = null;
        }
        window.wsIsConnected = false;

        // ── Reset state ──
        window.wsLiveData = {};
        window.wsAttributeNames = [];
        window.wsAttributeOrder = {};
        window.wsMessageCount = 0;
        window.wsBatchCount = 0;
        window.wsUpdatedAssets = {};
        window.wsStaleAttrs = {};
        window.rdpmsCardsBuilt = {};
        window.pmCardsBuilt = {};
        window.pmEventHistory = {};
        window.pmLastOperationTimestamp = {};
        window.pmLastOperationType = {};
        if (window._sigDomCache) window._sigDomCache = {};
        if (window._rdpmsDomCache) window._rdpmsDomCache = {};

        // Reset stale/signal caches and initial-load gate
        _assetInfoListCache = {};
        _signalGroupCache = {};
        _sigAssetInfoCache = {};
        _wsInitialLoadComplete = false;
        if (_wsInitialLoadTimer) { clearTimeout(_wsInitialLoadTimer); _wsInitialLoadTimer = null; }
        if (_staleBatchTimer) { clearTimeout(_staleBatchTimer); _staleBatchTimer = null; }
        _stalePendingSet = {};
        if (_sigRenderTimer) { clearTimeout(_sigRenderTimer); _sigRenderTimer = null; }
        _sigRenderActive = false;

        // Reset queue state
        window.wsMessageQueue = [];
        window.wsIsProcessingQueue = false;
        window.wsLastProcessedTime = 0;
        window.wsLastUIUpdateTime = 0;
        window.wsPendingUIUpdate = false;
        window.wsStreamingActive = false;
        window.wsViewSwitchPending = false;
        window.wsRAFScheduled = false;
        window.wsProcessedPerSecond = 0;
        window.wsMessagesThisSecond = 0;
        window.wsLastSecondTime = Date.now();

        window.wsCurrentColumns = [];
        window.wsTableInitialized = false;
        window.pmTableMode = false;

        window.wsCurrentAssetTypeId = assetTypeId;
        window.wsCurrentFilterAssetIds = assetIds || [];
        window.wsLastMessageTime = Date.now();
        window.wsReconnectAttempts = 0;
        window.wsConnParams = { siteId: siteId, assetTypeId: assetTypeId, assetIds: assetIds || [] };

        console.log('[WS-StableFix] Connecting to:', newUrl);

        // ── SIP-only detection (assetTypeId null/empty = background SIP connection,
        //    user hasn't clicked Search yet). Set gate to block onmessage processing
        //    and suppress all visible UI side-effects. ──
        var _isSipOnly = (!assetTypeId || assetTypeId === '' || assetTypeId === '0' || assetTypeId === 0);
        window._wsSipOnlyMode = _isSipOnly;

        if (!_isSipOnly && typeof showWsStatus === 'function') showWsStatus('connecting');

        try {
            window.wsConnection = new WebSocket(newUrl);

            // ── onopen ──────────────────────────────────────────
            window.wsConnection.onopen = function () {
                console.log('[WS-StableFix] ✓ Connected.');
                _isConnecting = false;
                window.wsIsConnected = true;
                window.wsReconnectAttempts = 0;
                window.wsLastMessageTime = Date.now();

                if (!_isSipOnly) {
                    if (typeof showWsStatus === 'function') showWsStatus('connected');
                    if (typeof showSuccess === 'function') showSuccess('Live data streaming started', 'WebSocket Connected');
                } else {
                    console.log('[WS-StableFix] SIP-only — UI updates suppressed');
                }

                // Only overwrite container for data views (not Circuit/Graph/Yard).
                // Also skip when this is a SIP-only connection (user hasn't searched yet).
                var currentView = $('#drpView').val();
                if (!_isSipOnly && currentView !== 'Circuit' && currentView !== 'Graph' && currentView !== 'Yard') {
                    $('#divTelemetryLive').html(
                        '<div class="text-center p-5" id="wsWaiting">' +
                        '<i class="fas fa-satellite-dish fa-2x mb-3" style="color:#10b981;"></i>' +
                        '<div style="color:#64748b;">Connected! Waiting for live data...</div>' +
                        '</div>'
                    );
                }

                // Start heartbeat (readyState-only, no message-timer reconnects)
                startHeartbeat();

                // Start safety render (stops itself after first successful render)
                if (!_isSipOnly) startSafetyRender();
            };

            // ── onmessage ───────────────────────────────────────
            window.wsConnection.onmessage = function (event) {
                window.wsLastMessageTime = Date.now();

                // ── GATE: SIP-only connections (no asset type) must not process
                //    data into wsLiveData or trigger any rendering. The SIP
                //    schematic uses its own separate WebSocket via
                //    SipTelemetry.connectToSite(). ──
                if (window._wsSipOnlyMode) return;

                try {
                    var rawData = event.data;
                    var batch;

                    if (typeof rawData === 'string') {
                        batch = JSON.parse(rawData);
                    } else {
                        batch = rawData;
                    }

                    window.wsBatchCount++;

                    // Log first few batches for debugging
                    if (window.wsBatchCount <= 3) {
                        console.log('[WS-StableFix] Batch #' + window.wsBatchCount, {
                            type: typeof batch,
                            isArray: Array.isArray(batch),
                            keys: (batch && typeof batch === 'object') ? Object.keys(batch).slice(0, 5) : []
                        });
                    }

                    // Route to existing parsers — go through window.* so the
                    // parseBatchMessages override (DataLogger-Fix at line ~13686)
                    // is invoked, which in turn feeds window.processItemsInternal
                    // and the sip-telemetry.js bridge.
                    var _pbm = window.parseBatchMessages || parseBatchMessages;
                    if (Array.isArray(batch) && batch.length > 0) {
                        _pbm(batch);
                    } else if (batch && Array.isArray(batch.Messages) && batch.Messages.length > 0) {
                        _pbm(batch.Messages);
                    } else if (batch && Array.isArray(batch.Data) && batch.Data.length > 0) {
                        _pbm(batch.Data);
                    } else if (batch && batch.AssetId !== undefined && batch.AssetAttributeName) {
                        processSingleLiveUpdate(batch);
                    } else if (batch && typeof batch === 'object') {
                        // Try to find any array property
                        for (var key in batch) {
                            if (Array.isArray(batch[key]) && batch[key].length > 0) {
                                _pbm(batch[key]);
                                return;
                            }
                        }
                    }
                } catch (e) {
                    console.error('[WS-StableFix] Message processing error:', e);
                }
            };

            // ── onclose ─────────────────────────────────────────
            window.wsConnection.onclose = function (event) {
                console.log('[WS-StableFix] Connection closed. code=' + event.code + ' reason=' + (event.reason || '(none)'));
                _isConnecting = false;
                window.wsIsConnected = false;

                // Clean close (1000) or manual disconnect → don't reconnect
                if (event.code === 1000 || _isManualDisconnect) {
                    console.log('[WS-StableFix] Clean/manual close — not reconnecting.');
                    if (typeof showWsStatus === 'function') showWsStatus('error');
                    clearAllTimers();
                    return;
                }

                // Auth failures → don't reconnect
                if (event.code === 1008 || event.code === 4001 || event.code === 4003) {
                    console.error('[WS-StableFix] Auth failure — not reconnecting.');
                    if (typeof showWsStatus === 'function') showWsStatus('error');
                    if (typeof showError === 'function') showError('Authentication failed.', 'WebSocket');
                    clearAllTimers();
                    return;
                }

                // Unexpected close → schedule reconnect
                scheduleReconnect();
            };

            // ── onerror ─────────────────────────────────────────
            window.wsConnection.onerror = function (err) {
                console.error('[WS-StableFix] Socket error:', err);
                // Don't set wsIsConnected=false here — let onclose handle it
                // (onerror is always followed by onclose)
            };

        } catch (e) {
            console.error('[WS-StableFix] Failed to create WebSocket:', e);
            _isConnecting = false;
            if (typeof showWsStatus === 'function') showWsStatus('error');
            if (typeof showError === 'function') showError('Connection failed: ' + e.message, 'WebSocket');
        }
    };

    // ──────────────────────────────────────────────────────────────
    // 10. DISABLE the old stability IIFE's heartbeat if it's running
    //     (it was set by the previous override at the bottom of the file)
    // ──────────────────────────────────────────────────────────────
    // The old IIFE stored its heartbeat in wsHeartbeatInterval — clear it
    if (window.wsHeartbeatInterval) {
        clearInterval(window.wsHeartbeatInterval);
        window.wsHeartbeatInterval = null;
    }
    if (window.wsSafetyInterval) {
        clearInterval(window.wsSafetyInterval);
        window.wsSafetyInterval = null;
    }

    // ──────────────────────────────────────────────────────────────
    // 11. DONE
    // ──────────────────────────────────────────────────────────────
    console.log('[WS-StableFix] ✓ Patch applied successfully.');
    console.log('[WS-StableFix]   • Heartbeat checks readyState only (never message timestamps).');
    console.log('[WS-StableFix]   • Idle server = no reconnect.');
    console.log('[WS-StableFix]   • Duplicate connect calls are blocked.');
    console.log('[WS-StableFix]   • Exponential back-off: 3s → 30s max.');

})();



console.log('[DataLogger-Fix] Initializing DataLogger Pickup/Drop fix...');

// ================================================================
// FIX 1: Enhanced processWsDataloggerAttr function
// ================================================================
//window.processWsDataloggerAttr = function (assetId, assetName, attrName, value, timestamp) {
//    // Ensure asset exists
//    if (!window.wsLiveData[assetId]) {
//        console.log('[DataLogger-Fix] Asset not found, creating:', assetId);
//        window.wsLiveData[assetId] = {
//            AssetId: assetId,
//            AssetName: assetName || ('Asset ' + assetId),
//            attrs: {},
//            dlRelays: {},
//            lastUpdated: new Date()
//        };
//    }

//    // Parse value - handle string, number, float
//    var numVal = parseFloat(value);
//    if (isNaN(numVal)) numVal = 0;

//    // Determine Pickup (1) or Drop (0)
//    var isPickup = (numVal === 1 || numVal === 1.0);

//    // Create display name - remove asset name prefix if present
//    var displayName = attrName;
//    if (assetName && displayName.indexOf(assetName) === 0) {
//        displayName = displayName.substring(assetName.length).trim();
//        // Remove leading dash or underscore if present
//        if (displayName.charAt(0) === '-' || displayName.charAt(0) === '_') {
//            displayName = displayName.substring(1).trim();
//        }
//    }
//    if (!displayName) displayName = attrName;

//    // Initialize dlRelays object if not exists
//    if (!window.wsLiveData[assetId].dlRelays) {
//        window.wsLiveData[assetId].dlRelays = {};
//    }

//    // Store relay data
//    window.wsLiveData[assetId].dlRelays[attrName] = {
//        value: numVal,
//        isPickup: isPickup,
//        displayName: displayName,
//        timestamp: timestamp || new Date().toISOString(),
//        rawAttrName: attrName
//    };

//    window.wsLiveData[assetId].lastUpdated = new Date();

//    console.log('[DataLogger-Fix] Processed:', {
//        assetId: assetId,
//        attrName: attrName,
//        displayName: displayName,
//        value: numVal,
//        isPickup: isPickup
//    });

//    // FIX: Immediately update ALL views when DataLogger changes
//    var viewType = $('#drpView').val();

//    // Update Table view DataLogger column
//    if (viewType === 'Table') {
//        if (typeof window.updateDataLoggerColumnInTable === 'function') {
//            window.updateDataLoggerColumnInTable(assetId);
//        }
//    }

//    // Update RDPMS view badges IMMEDIATELY
//    if (viewType === 'RDPMS') {
//        if (typeof window.renderDataloggerBadgesForAsset === 'function') {
//            window.renderDataloggerBadgesForAsset(assetId);
//        }
//        // Also update signal lights as they may depend on relay states
//        if (typeof window.updateMainSignalLights === 'function') {
//            window.updateMainSignalLights(assetId);
//        }
//        // Trigger debounced reorder of RDPMS cards by aspect
//        if (typeof window.reorderRdpmsCardsByAspect === 'function') {
//            window.reorderRdpmsCardsByAspect();
//        }
//    }

//    // Update PointMachine view badges
//    if (viewType === 'PointMachine') {
//        if (typeof window.renderPmBadges === 'function' && window.wsLiveData[assetId]) {
//            window.renderPmBadges(assetId, window.wsLiveData[assetId].dlRelays || {});
//        }
//    }

//    // FIX: Update Circuit view DataLogger -- was never called for Track/Signal circuit
//    if (viewType === 'Circuit') {
//        if (typeof updateCircuitDataLoggerFromWebSocket === 'function') {
//            updateCircuitDataLoggerFromWebSocket(assetId);
//        }
//    }

//    return true;
//};
window.processWsDataloggerAttr = function (assetId, assetName, attrName, value, timestamp, roleOrAttrId) {
    // ── BULK WHITELIST GUARD ──
    // Never create an asset from a DataLogger frame if it wasn't in the bulk
    // response. Updating an asset that already exists is fine (it passed the
    // guard when first created); only block creation of brand-new garbage ids.
    if (typeof isAssetInBulkWhitelist === 'function' &&
        !isAssetInBulkWhitelist(assetId) &&
        !(window.wsLiveData && window.wsLiveData[assetId])) {
        return;
    }

    var resolvedAssetName = (typeof getBulkAssetName === 'function')
        ? getBulkAssetName(assetId, assetName)
        : (assetName || ('Asset ' + assetId));

    if (!window.wsLiveData[assetId]) {
        window.wsLiveData[assetId] = {
            AssetId: assetId,
            AssetName: resolvedAssetName,
            AssetTypeId: (typeof getBulkAssetTypeId === 'function') ? getBulkAssetTypeId(assetId, null) : null,
            SiteId: (typeof getBulkSiteId === 'function') ? getBulkSiteId(assetId, null) : null,
            attrs: {},
            dlRelays: {},
            lastUpdated: new Date()
        };
    } else {
        window.wsLiveData[assetId].AssetName = resolvedAssetName;

        if (!window.wsLiveData[assetId].AssetTypeId && typeof getBulkAssetTypeId === 'function') {
            window.wsLiveData[assetId].AssetTypeId = getBulkAssetTypeId(assetId, window.wsLiveData[assetId].AssetTypeId);
        }

        if (!window.wsLiveData[assetId].SiteId && typeof getBulkSiteId === 'function') {
            window.wsLiveData[assetId].SiteId = getBulkSiteId(assetId, window.wsLiveData[assetId].SiteId);
        }
    }

    var numVal = parseFloat(value);
    if (isNaN(numVal)) numVal = 0;

    var isPickup = (numVal === 1 || numVal === 1.0);

    var displayName = (typeof resolveBulkDataloggerName === 'function')
        ? resolveBulkDataloggerName(assetId, roleOrAttrId || attrName, attrName)
        : attrName;

    if (!displayName && typeof _tlStripAssetPrefix === 'function') {
        displayName = _tlStripAssetPrefix(assetId, resolvedAssetName, attrName);
    }

    if (!displayName) displayName = attrName;

    if (!window.wsLiveData[assetId].dlRelays) {
        window.wsLiveData[assetId].dlRelays = {};
    }

    var storeKey = displayName || attrName;

    var relayObj = {
        value: numVal,
        isPickup: isPickup,
        displayName: displayName,
        timestamp: timestamp || new Date().toISOString(),
        rawAttrName: attrName,
        attrName: attrName,
        role: roleOrAttrId ? String(roleOrAttrId) : ''
    };

    window.wsLiveData[assetId].dlRelays[storeKey] = relayObj;

    function addAliasKey(aliasKey) {
        aliasKey = _tlMetaStr(aliasKey);

        if (!aliasKey || aliasKey === storeKey) return;

        try {
            delete window.wsLiveData[assetId].dlRelays[aliasKey];

            Object.defineProperty(window.wsLiveData[assetId].dlRelays, aliasKey, {
                value: relayObj,
                enumerable: false,
                configurable: true,
                writable: true
            });
        } catch (e) {
            window.wsLiveData[assetId].dlRelays[aliasKey] = relayObj;
        }
    }

    addAliasKey(attrName);
    addAliasKey(roleOrAttrId);

    window.wsLiveData[assetId].lastUpdated = new Date();

    var viewType = $('#drpView').val();

    if (viewType === 'Table' && typeof window.updateDataLoggerColumnInTable === 'function') {
        window.updateDataLoggerColumnInTable(assetId);
    }

    if (viewType === 'RDPMS') {
        if (typeof window.renderDataloggerBadgesForAsset === 'function') window.renderDataloggerBadgesForAsset(assetId);
        if (typeof window.updateMainSignalLights === 'function') window.updateMainSignalLights(assetId);
        if (typeof window.reorderRdpmsCardsByAspect === 'function') window.reorderRdpmsCardsByAspect();
    }

    if (viewType === 'PointMachine') {
        if (typeof window.renderPmBadges === 'function' && window.wsLiveData[assetId]) {
            window.renderPmBadges(assetId, window.wsLiveData[assetId].dlRelays || {});
        }
    }

    if (viewType === 'Circuit' && typeof updateCircuitDataLoggerFromWebSocket === 'function') {
        updateCircuitDataLoggerFromWebSocket(assetId);
    }

    return true;
};
// ================================================================
// FIX 2: Function to update DataLogger column in Table view
// Made global so it can be called from earlier processSingleLiveUpdate
// ================================================================
window.updateDataLoggerColumnInTable = function (assetId) {
    var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
    if (!$row.length) return;

    var $dlCell = $row.find('td.dl-cell');
    if (!$dlCell.length) {
        // Find DataLogger column by position - it's the second to last column (before Last Update)
        var $cells = $row.find('td');
        var totalCells = $cells.length;

        // DataLogger column is at position: totalCells - 2 (second to last, before time-cell)
        // Only look for it if we have enough columns (Track view with derived columns)
        if (totalCells >= 3) {
            var $timeCell = $cells.last();
            // Check if last cell is the time cell
            if ($timeCell.hasClass('time-cell') || $timeCell.attr('data-attr') === 'LastUpdate') {
                // DataLogger cell is second to last
                var $potentialDlCell = $cells.eq(totalCells - 2);
                // Verify it's not an attribute cell (doesn't have data-attr) or already has dl content
                if (!$potentialDlCell.attr('data-attr') ||
                    $potentialDlCell.find('.rdpms-dl-badge').length > 0 ||
                    $potentialDlCell.hasClass('dl-cell')) {
                    $dlCell = $potentialDlCell;
                    $potentialDlCell.addClass('dl-cell');
                }
            }
        }
    }

    if (!$dlCell.length) return;

    var asset = window.wsLiveData[assetId];
    if (!asset || !asset.dlRelays) return;

    var dlRelays = asset.dlRelays;
    var dlKeys = Object.keys(dlRelays);

    if (dlKeys.length === 0) {
        $dlCell.html('<span style="color:#94a3b8;font-size:10px;">--</span>');
        return;
    }

    // Build badges HTML
    var dlHtml = '';
    for (var di = 0; di < dlKeys.length; di++) {
        var relay = dlRelays[dlKeys[di]];
        var badgeClass = relay.isPickup ? 'pickup' : 'drop';
        var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
        var displayName = relay.displayName || dlKeys[di];

        dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + ' shine-button" ' +
            'style="margin:1px;padding:2px 6px;font-size:10px;" ' +
            'title="' + displayName + ': ' + badgeText + ' (Value: ' + relay.value + ')">' +
            displayName + ': ' + badgeText + '</span> ';
    }

    $dlCell.html(dlHtml);

    // Add flash animation
    $dlCell.addClass('ws-cell-flash');
    setTimeout(function () {
        $dlCell.removeClass('ws-cell-flash');
    }, 1200);
};

// ================================================================
// FIX 3: Enhanced parseBatchMessages to detect DataLogger type
// ================================================================
var _originalParseBatchMessages = window.parseBatchMessages;

window.parseBatchMessages = function (messages) {
    if (!messages || !Array.isArray(messages)) {
        console.warn('[DataLogger-Fix] Invalid messages array:', messages);
        return;
    }

    var items = [];
    var dataLoggerItems = [];

    for (var i = 0; i < messages.length; i++) {
        var item = messages[i];

        // Handle string items (JSON encoded)
        if (typeof item === 'string') {
            try {
                item = JSON.parse(item);
            } catch (e) {
                console.warn('[DataLogger-Fix] Failed to parse string item');
                continue;
            }

            // Double-encoded string
            if (typeof item === 'string') {
                try {
                    item = JSON.parse(item);
                } catch (e2) {
                    continue;
                }
            }
        }

        // Check if this is a DataLogger message
        if (item && item.DataType === 'DataLogger') {
            dataLoggerItems.push(item);
            continue;
        }

        // Valid message with AssetId and AssetAttributeName
        if (item && item.AssetId !== undefined && item.AssetAttributeName) {
            items.push(item);
        }
        // Nested Messages array
        else if (item && Array.isArray(item.Messages)) {
            for (var j = 0; j < item.Messages.length; j++) {
                var sub = item.Messages[j];
                if (typeof sub === 'string') {
                    try { sub = JSON.parse(sub); } catch (e3) { continue; }
                    if (typeof sub === 'string') {
                        try { sub = JSON.parse(sub); } catch (e4) { continue; }
                    }
                }
                if (sub && sub.DataType === 'DataLogger') {
                    dataLoggerItems.push(sub);
                } else if (sub && sub.AssetId !== undefined && sub.AssetAttributeName) {
                    items.push(sub);
                }
            }
        }
        // Nested Data array
        else if (item && Array.isArray(item.Data)) {
            for (var k = 0; k < item.Data.length; k++) {
                var dataItem = item.Data[k];
                if (typeof dataItem === 'string') {
                    try { dataItem = JSON.parse(dataItem); } catch (e5) { continue; }
                }
                if (dataItem && dataItem.DataType === 'DataLogger') {
                    dataLoggerItems.push(dataItem);
                } else if (dataItem && dataItem.AssetId !== undefined && dataItem.AssetAttributeName) {
                    items.push(dataItem);
                }
            }
        }
    }

    // Process DataLogger items FIRST
    if (dataLoggerItems.length > 0) {
        console.log('[DataLogger-Fix] Processing', dataLoggerItems.length, 'DataLogger items');
        processDataLoggerItems(dataLoggerItems);
    }

    // Process regular items + feed into processItemsInternal so SIP bridge sees them
    if (items.length > 0) {
        if (typeof window.processItemsInternal === 'function') {
            window.processItemsInternal(items);
        }
        // Also schedule UI update via processItems (which calls queueWsMessages + sipRefresh)
        if (typeof window.processItems === 'function') {
            window.processItems(items);
        }
    }
};

// ================================================================
// FIX 4: Process DataLogger items specifically
// ================================================================
function processDataLoggerItems(items) {
    for (var i = 0; i < items.length; i++) {
        var d = items[i];

        var assetId = d.AssetId;
        var assetName = d.AssetName;
        var attrName = d.AssetAttributeName;
        var value = d.Value;
        var timestamp = d.TimestampLocal || d.TimestampEvent || d.TimestampChange || new Date().toISOString();

        if (!assetId || !attrName) continue;

        // Apply filters
        var atFilter = window.wsCurrentAssetTypeId;
        var aidFilter = window.wsCurrentFilterAssetIds;

        if (atFilter && atFilter !== '0' && atFilter !== '' && d.AssetTypeId) {
            if (String(d.AssetTypeId) !== String(atFilter)) continue;
        }

        // FIX: was > 1 — same off-by-one as main processItemsInternal
        if (aidFilter && aidFilter.length > 0 && aidFilter[0] !== '' && aidFilter[0] !== '0') {
            if (aidFilter.indexOf(String(assetId)) === -1) continue;
        }

        // Process the DataLogger attribute
        window.processWsDataloggerAttr(
            assetId,
            assetName,
            attrName,
            value,
            timestamp,
            d.AssetAttributeId || d.AttrId || d.DataloggerAttributeId || attrName
        );
        // Mark asset as updated
        if (!window.wsUpdatedAssets) window.wsUpdatedAssets = {};
        window.wsUpdatedAssets[assetId] = true;
    }

    // Update stats
    if (typeof window.updateWsStats === 'function') {
        window.updateWsStats();
    }
}

// ================================================================
// FIX 5: Enhanced processSingleLiveUpdate for DataLogger
// ================================================================
var _originalProcessSingleLiveUpdate = window.processSingleLiveUpdate;

window.processSingleLiveUpdate = function (d) {
    // Check if this is a DataLogger message
    if (d && d.DataType === 'DataLogger') {
        var assetId = d.AssetId;
        var assetName = d.AssetName;
        var attrName = d.AssetAttributeName;
        var value = d.Value;
        var timestamp = d.TimestampLocal || d.TimestampEvent || d.TimestampChange || new Date().toISOString();

        if (!assetId || !attrName) return;

        // Apply filters
        var atFilter = window.wsCurrentAssetTypeId;
        var aidFilter = window.wsCurrentFilterAssetIds;

        if (atFilter && atFilter !== '0' && atFilter !== '' && d.AssetTypeId) {
            if (String(d.AssetTypeId) !== String(atFilter)) return;
        }

        // FIX: was > 1 — same off-by-one as main processItemsInternal
        if (aidFilter && aidFilter.length > 0 && aidFilter[0] !== '' && aidFilter[0] !== '0') {
            if (aidFilter.indexOf(String(assetId)) === -1) return;
        }

        // Process DataLogger
        window.processWsDataloggerAttr(assetId, assetName, attrName, value, timestamp);

        // Update stats
        window.wsMessageCount = (window.wsMessageCount || 0) + 1;
        if (typeof window.updateWsStats === 'function') {
            window.updateWsStats();
        }

        // For PM view, trigger card update for badges
        var viewType = $('#drpView').val();
        if (viewType === 'PointMachine' && window.pmCardsBuilt && window.pmCardsBuilt[assetId]) {
            if (typeof window.renderPmBadges === 'function') {
                window.renderPmBadges(assetId, window.wsLiveData[assetId].dlRelays || {});
            }
        }
        // For RDPMS view, update badges
        if (viewType === 'RDPMS' && window.rdpmsCardsBuilt && window.rdpmsCardsBuilt[assetId]) {
            if (typeof window.renderDataloggerBadgesForAsset === 'function') {
                window.renderDataloggerBadgesForAsset(assetId);
            }
        }

        return;
    }

    // Call original function for non-DataLogger messages
    if (typeof _originalProcessSingleLiveUpdate === 'function') {
        _originalProcessSingleLiveUpdate.apply(this, arguments);
    }
};

// ================================================================
// FIX 6: Ensure DataLogger column exists in Track table
// ================================================================
var _originalBuildTableRow = window.buildTableRow;

if (typeof _originalBuildTableRow === 'function') {
    window.buildTableRow = function (assetId, isTrack, isNew) {
        var asset = window.wsLiveData[assetId];
        if (!asset) return '';

        // Check if Point Machine (AssetTypeId = 3) to add PT- prefix
        var isPointMachine = (parseInt(asset.AssetTypeId) === 3 || parseInt(window.wsCurrentAssetTypeId) === 3);
        var displayName = asset.AssetName || 'Asset ' + assetId;
        if (isPointMachine && displayName && displayName.indexOf('PT-') !== 0) {
            displayName = 'PT-' + displayName;
        }

        var h = '<tr data-id="' + assetId + '"' + (isNew ? ' class="ws-row-new"' : '') + '>';
        h += '<td class="asset-name">' + displayName + '</td>';

        // Add attribute columns
        var wsAttributeNames = window.wsAttributeNames || [];
        for (var ai = 0; ai < wsAttributeNames.length; ai++) {
            var an = wsAttributeNames[ai];
            var ad = asset.attrs ? asset.attrs[an] : null;
            var raw = ad ? ad.Value : null;
            var cls = '', disp = '';

            if (raw === null || raw === undefined || raw === '') {
                disp = '-'; cls = 'val-na';
            } else {
                var num = parseFloat(raw);
                if (isNaN(num)) { disp = raw; }
                else {
                    if (num === 0) disp = '0.0';
                    else if (Math.abs(num) >= 100) disp = num.toFixed(2);
                    else disp = num.toFixed(2);

                    // Danger styling
                    if (an === 'Vr' && ((num > 0.1 && num < 2.5) || num > 4.2)) cls = 'val-danger';
                    else if ((an === 'TPR V' || an === 'TPR V (Loc)') && num > 0.1 && num < 20) cls = 'val-danger';
                    else if (an === 'Charger mA' && num < 100) cls = 'val-danger';
                    else if (an === 'Choke V' && num > 1.8) cls = 'val-danger';
                }
                if (ad && ad.changed && isNew) {
                    cls += ' ws-cell-flash val-changed-flash';
                    ad.changed = false;
                }
            }
            h += '<td class="' + cls + '" data-attr="' + an + '">' + disp + '</td>';
        }

        // Add derived values for Track
        if (isTrack) {
            if (typeof window.calculateDerivedValues === 'function' && typeof window.getDerivedCells === 'function') {
                var derived = window.calculateDerivedValues(asset.attrs);
                h += window.getDerivedCells(derived);
            }

            // Add DataLogger column with Pickup/Drop badges
            var dlRelays = asset.dlRelays || {};
            var dlHtml = '';
            var dlKeys = Object.keys(dlRelays);

            if (dlKeys.length > 0) {
                for (var di = 0; di < dlKeys.length; di++) {
                    var relay = dlRelays[dlKeys[di]];
                    var badgeClass = relay.isPickup ? 'pickup' : 'drop';
                    var badgeText = relay.isPickup ? 'Pickup' : 'Drop';
                    var displayName = relay.displayName || dlKeys[di];

                    dlHtml += '<span class="rdpms-dl-badge ' + badgeClass + ' shine-button" ' +
                        'style="margin:1px;padding:2px 6px;font-size:10px;">' +
                        displayName + ': ' + badgeText + '</span> ';
                }
            } else {
                dlHtml = '<span style="color:#94a3b8;font-size:10px;">--</span>';
            }
            h += '<td class="dl-cell">' + dlHtml + '</td>';
        }

        // Use TimestampDevice from operation ID attributes for date display
        var operationTs = typeof window.getOperationTimestampDevice === 'function' ? window.getOperationTimestampDevice(asset) : null;
        var ts = operationTs ? (typeof window.fmtTimestampDevice === 'function' ? window.fmtTimestampDevice(operationTs) : '--') :
            (asset.lastUpdated ? (typeof window.fmtTime === 'function' ? window.fmtTime(asset.lastUpdated) : '--') : '--');
        h += '<td class="time-cell" data-attr="LastUpdate">' + ts + '</td>';
        // Action buttons
        var oSiteId = asset.SiteId || $('#drpSite').val();
        h += '<td class="tl-asset-actions-cell">' +
            '<span class="tl-asset-actions">' +
            '<button type="button" class="tl-asset-action tl-aa-graph" title="Open Graph" onclick="fnGetAssetGraph(\'' + oSiteId + '\',\'' + assetId + '\')">' +
            '<i class="fa-solid fa-chart-line"></i></button>' +
            '<button type="button" class="tl-asset-action tl-aa-circuit" title="Open Circuit" onclick="fnGetAssetCircuit(\'' + assetId + '\')">' +
            '<i class="fa-solid fa-project-diagram"></i></button>' +
            '</span></td>';
        h += '</tr>';

        return h;
    };
}

// ================================================================
// FIX 7: Add CSS for DataLogger badges if not present
// ================================================================
if ($('#datalogger-badge-styles').length === 0) {
    var badgeStyles = `
        <style id="datalogger-badge-styles">
            .rdpms-dl-badge {
                display: inline-block;
                padding: 2px 6px;
                font-size: 10px;
                font-weight: 700;
                line-height: 1.3;
                color: #fff;
                text-align: center;
                white-space: nowrap;
                border-radius: 3px;
                position: relative;
                overflow: hidden;
                cursor: default;
            }
            .rdpms-dl-badge.pickup {
                background-color: #54ba4a;
            }
            .rdpms-dl-badge.drop {
                background-color: #ffaa05;
            }
            .rdpms-dl-badge.shine-button::before {
                content: '';
                position: absolute;
                top: 0;
                left: -75%;
                width: 50%;
                height: 100%;
                background: linear-gradient(120deg, rgba(255,255,255,0.2) 0%, rgba(255,255,255,0.6) 50%, rgba(255,255,255,0.2) 100%);
                transform: skewX(-20deg);
                animation: dlBadgeShine 2s infinite;
            }
            @@keyframes dlBadgeShine {
                0% { left: -75%; }
                100% { left: 125%; }
            }
            .dl-cell {
                white-space: nowrap;
                min-width: 100px;
            }
        </style>
    `;
    $('head').append(badgeStyles);
}

console.log('[DataLogger-Fix] ✓ DataLogger Pickup/Drop fix applied');
console.log('[DataLogger-Fix] - DataType="DataLogger" messages now processed correctly');
console.log('[DataLogger-Fix] - Value=1 shows Pickup (green badge)');
console.log('[DataLogger-Fix] - Value=0 shows Drop (orange badge)');

// ================================================================
// SIGNAL FORMULA UPDATE LOG
// ================================================================
console.log('[Signal-Formula] ✓ Signal condition logic updated based on notebook formula');
console.log('[Signal-Formula] Signal Conditions Priority (RDPMS View & Circuit View):');
console.log('[Signal-Formula] 1. RED: if RECR=1 OR (RG mA > ZeroOffset) → Show RG mA, RG V');
console.log('[Signal-Formula] 2. DOUBLE YELLOW: if (HECR=1 & HHECR=1) OR (HHG mA > ZeroOffset) → Show HG mA, HG V, HHG mA, HHG V, HHPR, HPR');
console.log('[Signal-Formula] 3. SINGLE YELLOW: if (HECR=1 & !HHECR) OR (HG mA > ZeroOffset) → Show HG mA, HG V, HPR');
console.log('[Signal-Formula] 4. GREEN: if DECR=1 OR (DG mA > ZeroOffset) → Show DG mA, DG V, DPR, HPR');
console.log('[Signal-Formula] DataType: RDPMS for mA/V values, DataLogger for relay states (RECR/HECR/HHECR/DECR)');
console.log('[Signal-Formula] ZeroOffset fetched from /Site/GetSignalAspectData API (mGlobalConfigs where AssetAttributeId=30)');
console.log('[Signal-Formula] Circuit View now uses same logic - checks relay conditions (DataLogger) + mA > ZeroOffset');

// ================================================================
// POINT MACHINE DIRECTION FIX LOG
// ================================================================
console.log('[PM-Direction] ✓ Point Machine direction logic updated');
console.log('[PM-Direction] Direction determination: NWKR > 5 = NORMAL, RWKR > 5 = REVERSE');
console.log('[PM-Direction] Threshold: 5.0 V');
console.log('[PM-Direction] Fallback: Uses OperationTime comparison if NWKR/RWKR not decisive');

// ================================================================
// HISTORY GRAPH API LOG
// ================================================================
console.log('[History-Graph] ✓ Graph View now uses server-side proxy (GetHistoryData)');
console.log('[History-Graph] API: /FRS25/Telemetry/GetHistoryData (proxy to HistoryValue API)');
console.log('[History-Graph] Parameters: AssetId, StartDate (ddMMyyyy_HHmmss), EndDate (ddMMyyyy_HHmmss)');
console.log('[History-Graph] Default: Past 1 hour from current time');
console.log('[History-Graph] Time ranges: 1h, 3h, 6h, 12h, 24h selectable');
console.log('[History-Graph] Timestamp used: TimestampDevice (primary) → TimestampLocal → TimestampEdgeX');
console.log('[History-Graph] Old _Graph API removed - now directly calls HistoryValue API');

// ================================================================
// TIMESTAMP DEVICE - LIVE DATA ONLY
// ================================================================
console.log('[Timestamp] ✓ Using TimestampDevice as primary timestamp source');
console.log('[Timestamp] Priority: TimestampDevice → TimestampLocal → TimestampChange → now()');
console.log('[Timestamp] Logic: Only process data if TimestampDevice > existing TimestampDevice');
console.log('[Timestamp] Result: Always shows LIVE data, ignores old/duplicate messages');

// ================================================================
// POINT MACHINE OPERATION-BASED ROW INSERTION
// ================================================================
console.log('[PM-Operation] ✓ Point Machine operation-based row insertion loaded');
console.log('[PM-Operation] Normal Indication IDs:', PM_NORMAL_INDICATION_IDS.join(', '));
console.log('[PM-Operation] Reverse Indication IDs:', PM_REVERSE_INDICATION_IDS.join(', '));
console.log('[PM-Operation] Normal Operation IDs:', PM_NORMAL_OPERATION_IDS.join(', '));
console.log('[PM-Operation] Reverse Operation IDs:', PM_REVERSE_OPERATION_IDS.join(', '));
console.log('[PM-Operation] Logic: New row inserted when operation ID has unique TimestampDevice');
console.log('[PM-Operation] FIX: Full history preserved -- same ts+direction updates in-place, new ts adds new row, no rows ever deleted');

// ================================================================
// CIRCUIT VIEW WITH WEBSOCKET LIVE DATA
// ================================================================
console.log('[Circuit-WS] ✓ Circuit view WebSocket live data enabled');
console.log('[Circuit-WS] Circuit diagram loads via AJAX, then WebSocket provides live updates');
console.log('[Circuit-WS] Supported: Track (If mA, Ir mA, Vr, Charger V, Charger mA, TPR V)');
console.log('[Circuit-WS] Supported: Signal (RG/DG/HG/HHG mA & V, signal light colors)');
console.log('[Circuit-WS] Supported: Point Machine (NWKR, RWKR values)');
console.log('[Circuit-WS] Supported: DataLogger relays (Pickup/Drop indicators)');

// ================================================================
// TELEMETRY LIVE DATE DISPLAY - OPERATION ID BASED TIMESTAMP
// ================================================================
console.log('[Telemetry-Date] ✓ Date display now uses TimestampDevice from operation ID attributes ONLY');
console.log('[Telemetry-Date] Normal Operation IDs:', TELEMETRY_NORMAL_OPERATION_IDS.join(', '));
console.log('[Telemetry-Date] Reverse Operation IDs:', TELEMETRY_REVERSE_OPERATION_IDS.join(', '));
console.log('[Telemetry-Date] EXCLUDES Indication IDs: 25, 26, 27, 28, 576, 577, 578, 579');
console.log('[Telemetry-Date] Logic: Picks up LATEST TimestampDevice from operation IDs only');

// ================================================================
// POINT MACHINE ASSET NAME PREFIX
// ================================================================
console.log('[PM-Name] ✓ Point Machine assets now prefixed with "PT-"');
console.log('[PM-Name] Logic: If AssetTypeId=3 (Point Machine), add PT- before asset name');

// ================================================================
// WEBSOCKET OPTIMIZED STREAMING SYSTEM v2
// ================================================================
console.log('[WS-Stream] ✓ Optimized streaming system enabled');
console.log('[WS-Stream] Features:');
console.log('[WS-Stream] - requestAnimationFrame for smooth 60fps rendering');
console.log('[WS-Stream] - Asset-type specific UI intervals (Track: 15fps, Signal/PM: 30fps)');
console.log('[WS-Stream] - View switch handling preserves data during transitions');
console.log('[WS-Stream] - Batch size:', WS_QUEUE_BATCH_SIZE, 'messages per cycle');
console.log('[WS-Stream] - Process interval:', WS_PROCESS_INTERVAL, 'ms (~120fps data processing)');
console.log('[WS-Stream] - Track UI:', WS_TRACK_UI_INTERVAL, 'ms | Signal UI:', WS_SIGNAL_UI_INTERVAL, 'ms | PM UI:', WS_PM_UI_INTERVAL, 'ms');
console.log('[WS-Stream] - Status bar shows: Msgs/s, Queue size, Asset count');

// ================================================================
// FIX: scripts.js updateClock null guard
// updateClock in scripts.js (line 315) crashes if its target DOM
// element doesn't exist (e.g. when Circuit view replaces the content).
// We patch it here AFTER scripts.js has loaded to add a null-check.
// ================================================================
(function patchUpdateClock() {
    if (typeof updateClock === 'function') {
        var _origUpdateClock = updateClock;
        window.updateClock = function () {
            try { _origUpdateClock.apply(this, arguments); } catch (e) { /* element not in DOM -- safe to ignore */ }
        };
        console.log('[Clock-Fix] updateClock patched with null guard');
    } else {
        // scripts.js may not be loaded yet -- retry once
        setTimeout(function () {
            if (typeof updateClock === 'function') {
                var _origUpdateClock2 = updateClock;
                window.updateClock = function () {
                    try { _origUpdateClock2.apply(this, arguments); } catch (e) { /* safe */ }
                };
                console.log('[Clock-Fix] updateClock patched with null guard (deferred)');
            }
        }, 2000);
    }
})();

// ================================================================
// FIX: SVG <path> attribute error -- suppress JointJS path parse
// errors caused by numeric-only label text being set as SVG path 'd'.
// The guard in updateCircuitPointMachine (!/[a-zA-Z]/.test) handles
// new updates, but this silences any residual console noise.
// ================================================================
(function suppressSvgPathErrors() {
    var _origSetAttr = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function (name, value) {
        if (name === 'd' && typeof value === 'string' && value.length > 0 &&
            !/^[Mm]/.test(value.trim()) && /^\d/.test(value.trim())) {
            // Skip malformed path data -- don't propagate to DOM
            console.debug('[SVG-Fix] Skipped malformed path d attr:', value.substring(0, 30));
            return;
        }
        return _origSetAttr.apply(this, arguments);
    };
})();



// ══════════════════════════════════════════════════════════════════
// SIP floating-pill mode
// Default: small floating pill in the corner.
// Click expand → reuse existing #sipFullscreenBtn handler.
// Esc / exit fullscreen → automatically returns to floating pill.
// ══════════════════════════════════════════════════════════════════
(function initSipFloating() {
    // Enable on every load. To opt out, remove this line.
    document.body.classList.add('sip-floating');

    var $card = $('.sip-card');
    if (!$card.length) return;

    // Ensure controls are visible in floating mode (existing code hides
    // them until a site is selected; we want the expand button always
    // available so the user can pop it open even before data arrives).
    $('#sipControls').show();

    // Track whether any WS data has been seen — affects the pulse dot
    function syncDataState() {
        var hasData = (typeof wsLiveData === 'object' && wsLiveData &&
            Object.keys(wsLiveData).length > 0);
        $card.toggleClass('sip-no-data', !hasData);
    }
    syncDataState();
    // Re-check whenever the SIP renderer fires (it runs after every WS batch)
    var _origSipRefresh = window.sipRefresh;
    window.sipRefresh = function () {
        try { if (typeof _origSipRefresh === 'function') _origSipRefresh.apply(this, arguments); }
        finally { syncDataState(); }
    };

    // Esc key restores floating from fullscreen (the existing
    // sipFullscreenBtn handler already toggles .fullscreen on Esc, but
    // we double-bind for safety).
    $(document).on('keydown.sipFloating', function (e) {
        if (e.key === 'Escape' && $card.hasClass('fullscreen')) {
            $('#sipFullscreenBtn').trigger('click');
        }
    });

    // Make the entire pill clickable to expand (not just the button).
    $card.on('click.sipExpand', function (e) {
        if ($card.hasClass('fullscreen')) return;     // already expanded
        if ($(e.target).closest('button').length) return; // let buttons handle themselves
        $('#sipFullscreenBtn').trigger('click');
    });
})();





// ══════════════════════════════════════════════════════════════════
// SIP inline mini-card mode
// Default state: 260×96 card with thumbnail + asset count + live stats.
// Click anywhere → expands via existing #sipFullscreenBtn handler.
// ══════════════════════════════════════════════════════════════════
(function initSipMini() {
    document.body.classList.add('sip-mini-mode');

    var $card = $('.sip-card');
    if (!$card.length) return;

    // The Expand button is always available; the rest of the controls
    // (Compare, Clear) are hidden by CSS while collapsed.
    $('#sipControls').show();

    // Refresh footer stats: asset count + last update info
    function syncFooter() {
        var assetCount = 0;
        if (typeof wsLiveData === 'object' && wsLiveData) {
            assetCount = Object.keys(wsLiveData).length;
        }
        $('#sipMiniAssetCount').text(assetCount);

        var $card = $('.sip-card');
        $card.toggleClass('sip-no-data', assetCount === 0);

        // Optional: surface Msg/s and last-update from the WS status bar
        var statsTxt = 'live';
        var $msgs = $('#wsMsgPerSec');
        var $upd = $('#wsLastUpdate');
        if ($msgs.length || $upd.length) {
            var parts = [];
            if ($msgs.length) parts.push(($msgs.text() || '0') + ' msg/s');
            if ($upd.length && $upd.text()) parts.push($upd.text());
            if (parts.length) statsTxt = parts.join(' · ');
        }
        $('#sipMiniStats').text(statsTxt);
    }
    syncFooter();
    setInterval(syncFooter, 2000);   // every 2s keeps the footer fresh

    // Click anywhere on the mini card → expand (existing handler does the work)
    $card.on('click.sipMiniExpand', function (e) {
        if ($card.hasClass('fullscreen')) return;
        if ($(e.target).closest('button').length) return;   // let buttons act on their own
        $('#sipFullscreenBtn').trigger('click');
    });

    // Esc returns to mini mode from fullscreen
    $(document).on('keydown.sipMini', function (e) {
        if (e.key === 'Escape' && $card.hasClass('fullscreen')) {
            $('#sipFullscreenBtn').trigger('click');
        }
    });
})();
// ============================================================
// ISSUE #3 — Card / Grid icon-toggle
// Wraps the existing atSwitchView('Cards' | 'Table', ...) so the
// compact icon toggle in the Live Asset Data header and the
// #atViewTabBar tabs stay in sync — both drive the same renderer.
// ============================================================
(function initViewModeToggle() {
    function setMode(mode) {
        $('.tl-vmode-btn')
            .removeClass('active')
            .attr('aria-pressed', 'false')
            .filter('[data-vmode="' + mode + '"]')
            .addClass('active')
            .attr('aria-pressed', 'true');
    }

    // Click → drive atSwitchView (which finds the matching tab btn and
    // runs the standard switch path; no duplicate render logic).
    $(document).on('click', '.tl-vmode-btn', function () {
        var mode = $(this).attr('data-vmode');
        if ($(this).hasClass('active')) return;
        setMode(mode);

        var tabBtn = document.querySelector('#atViewTabBar .at-tab-btn[data-view="' + mode + '"]');
        if (tabBtn && typeof atSwitchView === 'function') {
            atSwitchView(mode, tabBtn);
        }
    });

    // Keep the icon-toggle in sync when the user picks the same view
    // from the tab strip (or anywhere else that calls atSwitchView).
    var bar = document.getElementById('atViewTabBar');
    if (bar && window.MutationObserver) {
        new MutationObserver(function () {
            var $active = $('#atViewTabBar .at-tab-btn.active').first();
            var v = $active.attr('data-view');
            if (v === 'Cards' || v === 'Table') setMode(v);
        }).observe(bar, { subtree: true, attributes: true, attributeFilter: ['class'] });
    }
})();

// ============================================================
// Per-asset Graph / Circuit / EventLog actions
// Graph and Circuit are no longer view-type tabs; they open
// inline on top of the current Cards/List view, with a Back
// button to restore the previous view.
// ============================================================

window._tlPrevView = null;          // 'Cards' | 'Table' — what we'll restore on Back
window._tlOverlayActive = null;     // 'Graph' | 'Circuit' — current overlay, null = none

function tlBuildAssetActions(assetId) {
    return (
        '<span class="tl-asset-actions" data-asset-id="' + assetId + '">' +
        '<button type="button" class="tl-asset-action tl-aa-graph"' +
        ' title="Open Graph"' + ' onclick="tlOpenAssetView(\'Graph\',\'' + assetId + '\')">' +
        '<i class="fa-solid fa-chart-line"></i>' +
        '</button>' +
        '<button type="button" class="tl-asset-action tl-aa-circuit"' +
        ' title="Open Circuit"' + ' onclick="tlOpenAssetView(\'Circuit\',\'' + assetId + '\')">' +
        '<i class="fa-solid fa-project-diagram"></i>' +
        '</button>' +
        '</span>'
    );
}

function tlOpenAssetView(mode, assetId) {
    // Remember where we came from so Back can restore it
    window._tlPrevView = $('#drpView').val() || 'Table';
    window._tlOverlayActive = mode;

    // Single-asset scope: write the selection so fnBindGrph / fnBindCircuit pick it up
    $('#drpAsset').val(assetId);

    // Hide tabs, show back-bar with context
    $('#atViewTabBar').hide();
    var assetName = (wsLiveData[assetId] && wsLiveData[assetId].AssetName) ? wsLiveData[assetId].AssetName : assetId;
    $('#tlBackTo').text(window._tlPrevView === 'Cards' ? 'Cards' : 'List');
    $('#tlBackContext').html('<strong>' + mode + '</strong> · ' + assetName);
    $('#tlBackBar').show();

    if (mode === 'Graph' && typeof fnBindGrph === 'function') fnBindGrph();
    if (mode === 'Circuit' && typeof fnBindCircuit === 'function') fnBindCircuit();
}

function tlReturnFromAssetView() {
    var restore = window._tlPrevView || 'Table';
    window._tlOverlayActive = null;
    window._tlPrevView = null;

    if (typeof disconnectWebSocket === 'function') disconnectWebSocket();

    loadBulkAssetMetadata(siteId, assetTypeId, function () {
        if (typeof connectWebSocket === 'function') {
            connectWebSocket(siteId, assetTypeId, assetIds);
        }
        var mode = $('.tl-vmode-btn.active').attr('data-vmode') || 'Cards';
        console.log('[AdvSearch] dispatching mode=' + mode + ' for ' + assetIds.length + ' asset(s)');
        window._tlApplyViewMode(mode, { dataAlreadyOpen: false });
    });

}

// Expose for inline onclick attributes
window.tlOpenAssetView = tlOpenAssetView;
window.tlReturnFromAssetView = tlReturnFromAssetView;
window.tlBuildAssetActions = tlBuildAssetActions;

// ════════════════════════════════════════════════════════════════════
// ASSET-NUMBER FILTER  +  TWO-VIEW LAYOUT  (Cards + List only)
// ────────────────────────────────────────────────────────────────────
// This block:
//  1. Filters the asset list shown in Cards and Table views by the
//     user's Asset Number multi-select (#listAssetNumber checkboxes).
//     The WS itself stays connected to the full site/type stream;
//     filtering is purely render-side so toggling is instant.
//  2. Forces the active view list to {Cards, Table} for Signal and
//     PointMachine asset types (removes the separate RDPMS / IPS tabs
//     because the user wants ONLY Card + List for both). Cards already
//     render the RDPMS-style rich layout via atBuildSignalCard /
//     atBuildPmCard, so no info is lost.
//  3. Shows the #atViewTabBar tab strip once a site + asset type are
//     chosen so the user can flip Card <-> List.
// Nothing here removes or rewrites existing renderers — it only
// composes on top of them.
// ════════════════════════════════════════════════════════════════════
(function applyAssetNumberFilterAndTwoViewLayout() {
    'use strict';

    // ── 1. Render-side asset-ID filter ──────────────────────────────
    // The functions atRenderCards (IIFE-scope) and renderWsTable (global)
    // were patched DIRECTLY at their definitions to respect the
    // _sipFilteredAssetIds() helper below. Nothing to wrap on window here.
    window._sipFilteredAssetIds = function (assetIds) {
        var ids = window.wsCurrentFilterAssetIds;
        if (!ids || !ids.length) return assetIds;
        var real = {};
        var hasReal = false;
        for (var i = 0; i < ids.length; i++) {
            var v = String(ids[i] || '').trim();
            if (v !== '' && v !== '0') { real[v] = true; hasReal = true; }
        }
        if (!hasReal) return assetIds;
        var out = [];
        for (var j = 0; j < assetIds.length; j++) {
            if (real[String(assetIds[j])]) out.push(assetIds[j]);
        }
        return out;
    };

    // ── 2. Force "Cards + Table only" for Signal & PointMachine ─────
    var _origUpdateViewTypeOptions = window.updateViewTypeOptions;
    if (typeof _origUpdateViewTypeOptions === 'function') {
        window.updateViewTypeOptions = function () {
            // Run the original logic first so it does its housekeeping
            _origUpdateViewTypeOptions.apply(this, arguments);

            // Then, for Signal / PM, force the option list to {Table, Cards}.
            // (The original adds RDPMS for Signal; we strip it here so the
            // tab strip only shows the two views the user asked for.)
            try {
                if ((typeof isSignalAssetType === 'function' && isSignalAssetType()) ||
                    (typeof isPointAssetType === 'function' && isPointAssetType())) {
                    var $drpView = $('#drpView');
                    $drpView.find('option[value="RDPMS"]').remove();
                    $drpView.find('option[value="IPS"]').remove();
                    var cur = $drpView.val();
                    if (cur !== 'Table' && cur !== 'Cards') $drpView.val('Cards');
                }
            } catch (e) { console.warn('[TwoView] option-trim failed', e); }
        };
    }

    // ── 3. Reveal the tab strip once site + type are chosen ─────────
    function syncTabBarVisibility() {
        var siteOk = $('#drpSite').val() && $('#drpSite').val() !== '0';
        var atOk = $('#drpAssetType').val() && $('#drpAssetType').val() !== '0';
        $('#atViewTabBar').toggle(!!(siteOk && atOk));
    }
    $(document).on('change', '#drpSite, #drpAssetType', syncTabBarVisibility);
    // Sync after pill clicks (drpAssetType .change is triggered, so above catches it).
    $(syncTabBarVisibility);

    // ── 4. If an RDPMS view ever gets requested somewhere, redirect to Cards.
    //    (Some code paths still call atSwitchView('RDPMS', …) or set drpView.) ─
    var _origAtSwitchView = window.atSwitchView;
    if (typeof _origAtSwitchView === 'function') {
        window.atSwitchView = function (viewName, el) {
            if (viewName === 'RDPMS' || viewName === 'IPS' || viewName === 'PointMachine') {
                viewName = 'Cards';
                el = document.querySelector('#atViewTabBar .at-tab-btn[data-view="Cards"]') || el;
            }
            return _origAtSwitchView.call(this, viewName, el);
        };
    }
})();


// ================================================================
// ADVANCE TELEMETRY — Card / List dispatcher (final)
// ----------------------------------------------------------------
// #atViewTabBar has been removed from the markup. The single
// control for view mode is now the .tl-vmode-btn icon toggle in
// the Live Asset Data header (Card icon | List icon).
//
// This block:
//   1. Defines _tlApplyViewMode(mode, opts) — an asset-type-aware
//      renderer that maps {Cards|List} × {Signal|Track|Point|IPS}
//      to the right binder OR direct renderer.
//   2. Wires .tl-vmode-btn clicks → dispatcher (icon = repaint only,
//      no WebSocket churn).
//   3. Defines fnAdvSearch() for the Search button (Search = open
//      WebSocket + render in the active mode).
//   4. Wraps executeUIUpdate so Track / PM Cards auto-refresh as
//      live data arrives (those branches aren't in the original
//      executeUIUpdate's switch).
//   5. Provides container guards. renderWsTable() and
//      renderRDPMSView() both replace #divTelemetryLive.innerHTML,
//      which destroys #trackCardContainer. Before any Track/PM
//      Cards render we recreate it if missing.
//
// View mapping:
//   Signal + Card  → renderRDPMSView (rich aspect-light layout)
//   Signal + List  → renderWsTable   (standard WS table)
//   Track  + Card  → fnBindTrackCards (#trackCardContainer)
//   Track  + List  → renderWsTable
//   Point  + Card  → fnBindTrackCards (handles PM internally)
//   Point  + List  → renderPmTableView
//   IPS    + Card  → renderIpsGridView
//   IPS    + List  → renderIpsGridView (it always shows as a grid)
// ================================================================

(function () {
    'use strict';

    // ── Rebuild #trackCardContainer if a prior renderer wiped it ───
    function _ensureTrackCardContainer() {
        if ($('#trackCardContainer').length === 0) {
            var $tl = $('#divTelemetryLive');
            if ($tl.length) {
                $tl.html('<div id="trackCardContainer" class="sites-cards"></div>');
                console.log('[ViewMode] #trackCardContainer rebuilt');
            }
        }
    }

    // ── Visibility helpers — bypass the at-force-* class system ────
    // so we own visibility cleanly without fighting any leftover
    // classes from the old atSwitchView() path.
    function _showOnly(which) {
        // which: 'atCardView' | 'trackCardContainer' | 'divTelemetryLive' | 'none'
        if (which === 'atCardView') {
            $('#atCardView').show().removeClass('at-force-hidden').addClass('at-force-shown');
            $('#trackCardContainer').hide();
        } else if (which === 'trackCardContainer') {
            _ensureTrackCardContainer();
            $('#atCardView').hide().removeClass('at-force-shown').addClass('at-force-hidden');
            $('#trackCardContainer').show();
        } else if (which === 'divTelemetryLive') {
            // RDPMS, table, IPS, PM table all paint into #divTelemetryLive.
            // The element is always visible — we just hide siblings.
            $('#atCardView').hide().removeClass('at-force-shown').addClass('at-force-hidden');
            $('#trackCardContainer').hide();
        } else {
            $('#atCardView').hide();
            $('#trackCardContainer').hide();
        }
        // The work-area card always visible
        $('.at-table-card').show();
        $('.at-table-scroll').show().removeClass('at-force-hidden').addClass('at-force-shown');
    }

    function _setScrollMode(enable) {
        var el = document.querySelector('.at-table-scroll');
        if (!el) return;
        if (enable) el.classList.add('scroll-table-mode');
        else el.classList.remove('scroll-table-mode');
    }

    function _emptyState(target, html) {
        $(target).html(
            '<div style="padding:40px;text-align:center;color:#94a3b8;font-family:Manrope,sans-serif;">' +
            html + '</div>'
        );
    }

    // ── Shared dispatcher ──────────────────────────────────────────
    window._tlApplyViewMode = function (mode, opts) {
        opts = opts || {};
        if (mode !== 'Cards' && mode !== 'Table') mode = 'Cards';
        var isList = (mode === 'Table');
        $('#drpView').val(mode);

        // Sync icon toggle
        $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
        $('.tl-vmode-btn[data-vmode="' + (isList ? 'Table' : 'Cards') + '"]')
            .addClass('active').attr('aria-pressed', 'true');
        window._atCurrentView = isList ? 'Table' : 'Cards';

        var hasLive = (Object.keys(window.wsLiveData || {}).length > 0);

        // ── LIST MODE ─────────────────────────────────────────────
        if (isList) {
            // CSS-class-based visibility — see #atCardView.at-force-*
            // rules at top of <style>. Plain .hide()/.show() is not
            // enough because the baseline `#atCardView { display:none }`
            // rule keeps the container hidden even after .show().
            $('#atCardView').removeClass('at-force-shown').addClass('at-force-hidden').hide();
            $('#trackCardContainer').hide();
            $('.at-table-scroll').removeClass('at-force-hidden').addClass('at-force-shown').show();
            $('#divTelemetryLive').show();
            if (!hasLive) {
                $('#divTelemetryLive').html('<div class="text-center p-5">No data yet. Select Asset Numbers.</div>');
                return;
            }
            // Render table
            var assetTypeId = $('#drpAssetType').val();
            var isPoint = (assetTypeId == 3);
            if (isPoint && typeof renderPmTableView === 'function') renderPmTableView();
            else if (typeof renderWsTable === 'function') renderWsTable();
            return;
        }

        // ── CARDS MODE ───────────────────────────────────────────
        // Dispatch by asset type to the asset-type-appropriate RICH
        // renderer. The previous code routed everything through
        // fnBindTrackCards → atBuildSignalCard, which is just the
        // generic "<div>SIGNAL: name</div>" stub. That's why Signal
        // cards weren't showing the proper RDPMS aspect-light layout.
        //
        // Signal       → renderRDPMSView()         (#divTelemetryLive)
        // PointMachine → renderPointMachineView()  (#divTelemetryLive)
        // IPS          → renderIpsGridView()       (#divTelemetryLive)
        // Track/other  → fnBindTrackCards()        (#atCardView)
        $('#trackCardContainer').hide();

        var _isSignalCard = (typeof isSignalAssetType === 'function' && isSignalAssetType());
        var _isPointCard = (typeof isPointAssetType === 'function' && isPointAssetType());
        var _isIpsCard = (typeof isIpsAssetType === 'function' && isIpsAssetType());

        // If still undecided, consult the currently selected asset type value
        if (!_isSignalCard && !_isPointCard && !_isIpsCard) {
            var atVal = $('#drpAssetType').val();
            var atText = ($('#drpAssetType option:selected').text() || '').toLowerCase();
            _isSignalCard = (atVal === '2' || atText === 'signal');
            _isPointCard = (atVal === '3' || atText.indexOf('point') !== -1);
            _isIpsCard = (atVal === '4' || atText === 'ips');
        }

        if (_isSignalCard || _isPointCard || _isIpsCard) {
            // Rich renderers all paint into #divTelemetryLive, so flip
            // visibility the OTHER way from the Track/Cards path.
            $('#atCardView').removeClass('at-force-shown').addClass('at-force-hidden').hide();
            $('.at-table-scroll').removeClass('at-force-hidden').addClass('at-force-shown').show();
            $('#divTelemetryLive').show();

            // ── DEFENSIVE CLEANUP ─────────────────────────────────────
            // Remove containers AND clear build-tracking state from OTHER
            // asset-type renderers so we never accidentally show stale
            // Signal cards in a PM view (or vice versa). Each renderer
            // only creates its own container if it doesn't exist, so stale
            // containers from the previous asset type can persist if
            // .empty() wasn't called between switches. Also, the
            // build-tracking maps (rdpmsCardsBuilt, pmCardsBuilt) must be
            // cleared so the NEW renderer doesn't think OLD cards are
            // already built (skipping the build loop and showing nothing).
            if (_isSignalCard) {
                $('#pmMainContainer, #ipsGridContainer, .ips-grid-container').remove();
                if (typeof pmCardsBuilt !== 'undefined') pmCardsBuilt = {};
                if (typeof pmCardsBuilding !== 'undefined') pmCardsBuilding = {};
            } else if (_isPointCard) {
                $('#rdpmsMainContainer, #rdpmsShuntContainer, .rdpms-container, #ipsGridContainer, .ips-grid-container').remove();
                if (typeof rdpmsCardsBuilt !== 'undefined') rdpmsCardsBuilt = {};
                if (typeof _sigDomCache !== 'undefined') _sigDomCache = {};
                if (typeof _rdpmsDomCache !== 'undefined') _rdpmsDomCache = {};
            } else if (_isIpsCard) {
                $('#pmMainContainer, #rdpmsMainContainer, #rdpmsShuntContainer, .rdpms-container').remove();
                if (typeof rdpmsCardsBuilt !== 'undefined') rdpmsCardsBuilt = {};
                if (typeof pmCardsBuilt !== 'undefined') pmCardsBuilt = {};
                if (typeof _sigDomCache !== 'undefined') _sigDomCache = {};
                if (typeof _rdpmsDomCache !== 'undefined') _rdpmsDomCache = {};
            }
            // The Track-card container belongs to #atCardView (now hidden);
            // remove it too so the executeUIUpdate wrapper's :visible check
            // can't accidentally fire fnBindTrackCards for rich types.
            $('#trackCardContainer').remove();

            // Show waiting placeholder if no data yet. The renderer will
            // OVERWRITE this placeholder once data arrives.
            if (!hasLive) {
                $('#divTelemetryLive').html(
                    '<div class="text-center p-5" id="wsWaiting" style="color:var(--text-3);">' +
                    '<i class="fas fa-satellite-dish fa-2x" style="display:block;margin-bottom:10px;color:#10b981;"></i>' +
                    '<div>Waiting for live data...</div>' +
                    '</div>'
                );
            }

            if (typeof syncAttributeNamesFromLiveData === 'function') syncAttributeNamesFromLiveData();

            // ALWAYS call the renderer. Each renderer gracefully handles
            // the empty case (early-returns without changing the DOM), so
            // the waiting placeholder stays visible until data arrives.
            // When executeUIUpdate later runs for incoming data, the SAME
            // renderer will run again and paint over the placeholder.
            if (_isSignalCard && typeof renderRDPMSView === 'function') {
                console.log('[ViewMode] Signal Card → renderRDPMSView()');
                renderRDPMSView();
            } else if (_isPointCard && typeof renderPointMachineView === 'function') {
                console.log('[ViewMode] Point Card → renderPointMachineView()');
                renderPointMachineView();
            } else if (_isIpsCard && typeof renderIpsGridView === 'function') {
                console.log('[ViewMode] IPS Card → renderIpsGridView()');
                renderIpsGridView();
            } else {
                // Renderer missing — fall back to fnBindTrackCards
                console.warn('[ViewMode] Rich renderer missing — falling back to fnBindTrackCards');
                $('#divTelemetryLive').hide();
                $('.at-table-scroll').removeClass('at-force-shown').addClass('at-force-hidden').hide();
                $('#atCardView').removeClass('at-force-hidden').addClass('at-force-shown').show();
                if (typeof fnBindTrackCards === 'function') fnBindTrackCards();
            }
            return;
        }

        // ── Track / other → generic at-card view ──────────────────
        $('#divTelemetryLive').hide();
        $('.at-table-scroll').removeClass('at-force-shown').addClass('at-force-hidden').hide();
        $('#atCardView').removeClass('at-force-hidden').addClass('at-force-shown').show();

        if (!hasLive) {
            $('#atCardView').html('<div class="text-center p-5" style="color:var(--text-3);"><i class="fas fa-satellite-dish fa-2x" style="display:block;margin-bottom:10px;"></i>Select Asset Numbers to view live cards.</div>');
            return;
        }

        if (typeof syncAttributeNamesFromLiveData === 'function') syncAttributeNamesFromLiveData();

        if (typeof fnBindTrackCards === 'function') {
            fnBindTrackCards();
        } else {
            console.error('[Cards] fnBindTrackCards not found');
            $('#atCardView').html('<div class="text-center p-5">Card renderer missing.</div>');
        }
    };
    // Generic fallback card renderer (if none of the above match)
    function renderGenericCards() {
        var container = $('#atCardView');
        container.empty();
        var assetIds = (typeof getSortedAssetIds === 'function') ? getSortedAssetIds() : Object.keys(wsLiveData);
        if (!assetIds.length) {
            container.html('<div class="text-center p-5">No assets</div>');
            return;
        }
        var html = '<div class="at-cards-grid">';
        for (var i = 0; i < assetIds.length; i++) {
            var aid = assetIds[i];
            var a = wsLiveData[aid];
            var name = a ? (a.AssetName || aid) : aid;
            html += '<div class="at-asset-card"><div class="at-card-head"><div class="at-card-name">' + name + '</div></div><div class="at-card-foot">No attributes yet</div></div>';
        }
        html += '</div>';
        container.html(html);
    }
    // ── Icon-toggle click handler ──────────────────────────────────
    //$(document).off('click', '.tl-vmode-btn');
    //$(document).on('click.tlvmode', '.tl-vmode-btn', function () {
    //    var $this = $(this);
    //    var mode = $this.attr('data-vmode') || 'Cards';
    //    if ($this.hasClass('active')) return;
    //    console.log('[ViewMode] icon clicked → ' + mode);
    //    window._tlApplyViewMode(mode, { dataAlreadyOpen: true });
    //});
    $(document).off('click', '.tl-vmode-btn');
    $(document).on('click.tlvmode', '.tl-vmode-btn', function () {
        var $this = $(this);
        var mode = $this.attr('data-vmode') || 'Cards';
        if ($this.hasClass('active')) return;
        console.log('[ViewMode] icon clicked → ' + mode);

        // Always flip the active class so the UI reflects intent.
        $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
        $this.addClass('active').attr('aria-pressed', 'true');
        window._atCurrentView = (mode === 'Table') ? 'Table' : 'Cards';

        // Sync drpView and trigger change so handleViewSwitch + updateViewMode fire
        $('#drpView').val(mode).trigger('change');

        // If wsLiveData has data from a previous Search, repaint it in
        // the new view mode (Card ↔ List). If empty (no Search yet),
        // leave the "Click Search" prompt visible.
        var hasLive = (Object.keys(window.wsLiveData || {}).length > 0);
        if (hasLive) {
            window._tlApplyViewMode(mode, { dataAlreadyOpen: true });
        }
    });
    // ── Search button handler ──────────────────────────────────────
    window.fnAdvSearch = function () {
        var siteId = $('#drpSite').val();
        var assetTypeId = $('#drpAssetType').val();
        var assetIds = (typeof getSelectedAssetIds === 'function') ? getSelectedAssetIds() : [];

        if (!siteId || siteId === '0' || siteId === '') {
            if (typeof showWarning === 'function') showWarning('Please select a station.', 'Validation');
            return;
        }
        if (!assetTypeId || assetTypeId === '0' || assetTypeId === '') {
            if (typeof showWarning === 'function') showWarning('Please click an Asset Type pill first.', 'Validation');
            return;
        }
        if (!assetIds || assetIds.length === 0) {
            // FIX: getSelectedAssetIds() returns [] when ALL are checked.
            var _totalAA = $('#listAssetNumber input').length;
            var _checkedAA = $('#listAssetNumber input:checked').length;
            if (_checkedAA === 0 || _totalAA === 0) {
                if (typeof showWarning === 'function') showWarning('Please select at least one Asset Number.', 'Validation');
                return;
            }
        }

        // Keep global filter state in sync so processItemsInternal etc.
        // see the right asset type and asset IDs.
        window.wsCurrentFilterAssetIds = assetIds;
        if (typeof wsCurrentAssetTypeId !== 'undefined') wsCurrentAssetTypeId = assetTypeId;

        // Mark that user has explicitly searched — unlocks rendering in updateViewMode
        window._tlSearchInitiated = true;

        //if (typeof disconnectWebSocket === 'function') disconnectWebSocket();
        //// Open a new WebSocket with the selected site, asset type, and asset IDs
        //if (typeof connectWebSocket === 'function') {
        //    connectWebSocket(siteId, assetTypeId, assetIds);
        //}


        //var mode = $('.tl-vmode-btn.active').attr('data-vmode') || 'Cards';
        //console.log('[AdvSearch] dispatching mode=' + mode + ' for ' + assetIds.length + ' asset(s)');
        //window._tlApplyViewMode(mode, { dataAlreadyOpen: false });

        if (typeof disconnectWebSocket === 'function') disconnectWebSocket();

        loadBulkAssetMetadata(siteId, assetTypeId, function () {
            if (typeof connectWebSocket === 'function') {
                connectWebSocket(siteId, assetTypeId, assetIds);
            }
            var mode = $('.tl-vmode-btn.active').attr('data-vmode') || 'Cards';
            console.log('[AdvSearch] dispatching mode=' + mode + ' for ' + assetIds.length + ' asset(s)');
            window._tlApplyViewMode(mode, { dataAlreadyOpen: false });
        });



    };

    // ── Live-update hook for Track/PM Cards ────────────────────────
    // The original executeUIUpdate only handles Table/IPS/RDPMS in its
    // switch. For Track/PM Cards we re-render the track container as
    // updates flow in.
    if (typeof window.executeUIUpdate === 'function') {
        var _origExecuteUIUpdate = window.executeUIUpdate;
        window.executeUIUpdate = function () {
            try { _origExecuteUIUpdate.apply(this, arguments); }
            catch (e) { console.error('[ViewMode] inner executeUIUpdate', e); }

            // Only refresh Track cards if (a) the trackCardContainer is visible
            // AND (b) the current asset type is actually Track/generic. Without
            // the asset-type guard, this wrapper would render Signal/PM/IPS
            // data using the GENERIC card stubs (atBuildSignalCard etc.) instead
            // of the rich RDPMS / PM / IPS layouts — which looks like "wrong
            // asset type in card view".
            var _isSignal = (typeof isSignalAssetType === 'function' && isSignalAssetType());
            var _isPoint = (typeof isPointAssetType === 'function' && isPointAssetType());
            var _isIps = (typeof isIpsAssetType === 'function' && isIpsAssetType());
            var _isRichType = _isSignal || _isPoint || _isIps;

            if (!_isRichType &&
                $('#trackCardContainer').is(':visible') &&
                Object.keys(window.wsLiveData || {}).length > 0 &&
                typeof fnBindTrackCards === 'function') {
                try { fnBindTrackCards(); }
                catch (e) { console.warn('[ViewMode] live track refresh', e); }
            }
        };
        console.log('[ViewMode] executeUIUpdate wrapped — Track/PM cards auto-refresh');
    }

    console.log('[ViewMode] Card/List dispatcher installed (final, container-safe, atViewTabBar-free)');
})();


/*circuit*/

// Global variable to store active circuit WebSocket for modal (if any)
var _modalCircuitWs = null;
var _modalCircuitAssetId = null;

function showAssetGraph(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) {
        showWarning('No data available for this asset', 'Graph');
        return;
    }
    var assetName = asset.AssetName || 'Asset ' + assetId;
    $('#modalAssetName').text(assetName);
    $('#modalDetailType').text('Graph');
    $('#modalIcon').removeClass('fa-project-diagram').addClass('fa-chart-line');

    var $modalBody = $('#assetDetailModalBody');
    $modalBody.html('<div class="text-center py-5" style="color: #94a3b8;"><i class="fas fa-spinner fa-spin fa-2x mb-3" style="color: #22d3ee;"></i><p>Loading graph data...</p></div>');

    // Create a temporary container inside the modal
    $modalBody.html('<div id="modalGraphContainer" style="width:100%; height:70vh;"></div>');

    // Temporarily store the original chart reference to avoid conflict
    var savedGraphChart = mainGraphChart;
    mainGraphChart = null;

    // Clone the graph rendering logic for a single asset
    renderAssetGraphInContainer(assetId, $('#modalGraphContainer')[0], function () {
        // After rendering, ensure resize works on modal show
        $('#assetDetailModal').on('shown.bs.modal', function () {
            if (window.modalGraphChart && typeof window.modalGraphChart.resize === 'function')
                window.modalGraphChart.resize();
        });
    });

    $('#assetDetailModal').modal('show');

    // Restore main graph chart reference when modal is closed
    $('#assetDetailModal').one('hidden.bs.modal', function () {
        mainGraphChart = savedGraphChart;
        if (window.modalGraphChart) {
            window.modalGraphChart.dispose();
            window.modalGraphChart = null;
        }
    });
}

function renderAssetGraphInContainer(assetId, containerEl, callback) {
    // This function replicates the core logic of _gLoad and _gRender
    // but uses a fresh ECharts instance inside the container.
    var endMs = Date.now();
    var startMs = endMs - (24 * 3600000); // default 24h range
    var startDate = new Date(startMs);
    var endDate = new Date(endMs);
    var startStr = formatDateForHistoryApi(startDate);
    var endStr = formatDateForHistoryApi(endDate);

    $.ajax({
        url: HISTORY_API_BASE + '?assetId=' + assetId + '&startDate=' + startStr + '&endDate=' + endStr,
        type: 'GET',
        dataType: 'json',
        timeout: 60000,
        success: function (response) {
            if (response && response.Data && response.Data.length > 0) {
                // Use the existing _gRender function but with a temporary chart instance
                var assetData = response.Data;
                // Determine asset type from wsLiveData
                var asset = wsLiveData[assetId];
                var isTrack = (asset && asset.AssetTypeId == 1) || (parseInt($('#drpAssetType').val()) === 1);
                // We need to set up a temporary graph context.
                // Since _gRender uses global variables (_gChecked, _gColorMap, etc.),
                // we can use a copy of those but for simplicity we'll reuse the same global
                // but ensure we only render one series.
                // To avoid polluting, we create a shallow copy of the necessary state.
                var savedChecked = jQuery.extend({}, _gChecked);
                var savedColorMap = jQuery.extend({}, _gColorMap);
                var savedF = window._gF;
                window._gF = 'all';

                // Simulate a lightweight graph environment
                var tmpChart = echarts.init(containerEl);
                window.modalGraphChart = tmpChart;

                // Call the existing _gRender with a flag to use the modal chart
                _gRenderForModal(assetData, assetId, tmpChart, isTrack);

                // Restore globals
                window._gF = savedF;
                // Optionally keep color map for this session
                if (callback) callback();
            } else {
                $(containerEl).html('<div class="text-center py-5" style="color: #94a3b8;">No historical data available for this asset.</div>');
                if (callback) callback();
            }
        },
        error: function () {
            $(containerEl).html('<div class="text-center py-5" style="color: #ef4444;">Failed to load graph data.</div>');
            if (callback) callback();
        }
    });
}

// Lightweight version of _gRender that uses a provided chart instance
function _gRenderForModal(data, aid, chartInstance, isTrack) {
    // Copy the core logic of _gRender but simplified to render all available attributes
    // without the filter UI. Use the same attribute extraction as _gRender.
    // For brevity, we can call the original _gRender if we temporarily replace
    // the global mainGraphChart, but that's risky. Instead, we'll implement a minimal version.
    // Since the original _gRender is long, we'll assume we can reuse it by temporarily
    // setting mainGraphChart = chartInstance and then calling _gRender, then restoring.
    // But _gRender also manipulates DOM elements (like #gColF, etc.) which are not in modal.
    // Therefore we need a standalone render function.

    // Given the complexity, a pragmatic approach is to reuse the existing modal graph
    // function `fnGetAssetGraph` which already works and opens its own modal.
    // But the user wants a single modal with icons. Instead, we can simply call
    // `fnGetAssetGraph(siteId, assetId)` which already opens a modal with the graph.
    // That's simpler and uses existing working code.

    // So we can replace the whole renderAssetGraphInContainer with:
    var siteId = wsLiveData[assetId].SiteId || $('#drpSite').val();
    fnGetAssetGraph(siteId, assetId);
    if (callback) callback();
}

// Similarly for circuit
function showAssetCircuit(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) {
        showWarning('No data available for this asset', 'Circuit');
        return;
    }
    var assetName = asset.AssetName || 'Asset ' + assetId;
    $('#modalAssetName').text(assetName);
    $('#modalDetailType').text('Circuit');
    $('#modalIcon').removeClass('fa-chart-line').addClass('fa-project-diagram');

    var $modalBody = $('#assetDetailModalBody');
    $modalBody.html('<div class="text-center py-5" style="color: #94a3b8;"><i class="fas fa-spinner fa-spin fa-2x mb-3" style="color: #22d3ee;"></i><p>Loading circuit diagram...</p></div>');

    // Load circuit via AJAX and inject into modal body
    var assetTypeId = asset.AssetTypeId || $('#drpAssetType').val();
    $.ajax({
        url: '/FRS25/Telemetry/_Circuit',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ assetTypeId: assetTypeId, assetId: assetId }),
        dataType: 'text',
        success: function (html) {
            $modalBody.html(html);
            // After injecting, we need to initialize the circuit scripts and appModel
            // but the circuit partial expects to find its own DOM elements and runs its own script.
            // However, scripts inside are not executed when inserted via .html().
            // So we need to extract and evaluate scripts manually.
            var $scripts = $(html).filter('script').add($(html).find('script'));
            var $content = $(html).filter(function () { return this.tagName !== 'SCRIPT'; });
            $modalBody.html($content);
            $scripts.each(function () {
                var scriptContent = this.textContent || this.innerText;
                if (scriptContent) {
                    try { eval(scriptContent); } catch (e) { console.warn(e); }
                }
            });
            // The circuit's own WebSocket will be set up inside its $(document).ready.
            // We also need to ensure that when the modal is closed, the circuit WebSocket is disconnected
            // to avoid conflicts. We'll store the current circuitAssetId and disconnect on modal close.
            if (window.circuitAssetId) {
                _modalCircuitAssetId = window.circuitAssetId;
            }
            // Adjust paper size after modal shown
            $('#assetDetailModal').one('shown.bs.modal', function () {
                if (window.appModel && window.appModel.paper) {
                    window.appModel.paper.fitToContent({ padding: 20 });
                }
            });
        },
        error: function () {
            $modalBody.html('<div class="text-center py-5" style="color: #ef4444;">Failed to load circuit diagram.</div>');
        }
    });

    $('#assetDetailModal').modal('show');

    // Clean up circuit resources when modal closes
    $('#assetDetailModal').one('hidden.bs.modal', function () {
        if (_modalCircuitAssetId && typeof disconnectWebSocket === 'function') {
            disconnectWebSocket();
            _modalCircuitAssetId = null;
        }
        if (window.circuitAssetId) {
            window.circuitAssetId = null;
        }
        // Clear any leftover parseJson and appModel references to avoid memory leaks
        if (window.parseJson) window.parseJson = null;
        if (window.appModel) window.appModel = null;
    });
}


function fnGetAssetCircuit(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) {
        showWarning('No data available', 'Circuit');
        return;
    }
    var siteId = asset.SiteId || $('#drpSite').val();
    var assetTypeId = asset.AssetTypeId || $('#drpAssetType').val();
    var assetName = asset.AssetName || assetId;

    // ── CRITICAL: Set global so circuit live-sync functions find the asset ──
    circuitAssetId = assetId;
    $('#drpAsset').val(assetId);

    var modalHtml =
        '<div class="tl-modal-overlay" id="rdpmsCircuitOverlay" onclick="closeCircuitModal(event)">' +
        '<div class="tl-modal-shell tl-modal-wide" onclick="event.stopPropagation()">' +
        '<div class="tl-modal-head">' +
        '<div class="tl-modal-head-left">' +
        '<span class="tl-modal-icon tl-modal-icon-circuit"><i class="fas fa-project-diagram"></i></span>' +
        '<div><div class="tl-modal-title">Circuit Diagram</div>' +
        '<div class="tl-modal-subtitle">' + assetName + ' <span class="tl-live-badge"><span class="tl-live-dot-sm"></span> LIVE</span></div></div>' +
        '</div>' +
        '<div class="tl-modal-head-actions">' +
        '<button class="tl-modal-fullscreen-btn" onclick="toggleModalFullscreen(\'rdpmsCircuitOverlay\')" title="Toggle Fullscreen"><i class="fas fa-expand"></i></button>' +
        '<button class="tl-modal-close" onclick="closeCircuitModal()" title="Close">&times;</button>' +
        '</div>' +
        '</div>' +
        '<div class="tl-modal-body tl-circuit-body">' +
        '<div id="rdpmsCircuitLoading" class="tl-modal-loader"><div class="tl-spinner"></div><span>Loading circuit diagram...</span></div>' +
        '<div id="rdpmsCircuitContent" class="tl-circuit-frame" style="display:none;"></div>' +
        '<div id="rdpmsCircuitError" class="tl-modal-error" style="display:none;"><i class="fas fa-exclamation-triangle"></i><span>Failed to load circuit diagram</span></div>' +
        '</div>' +
        '</div></div>';

    $('#rdpmsCircuitOverlay').remove();
    $('body').append(modalHtml);
    requestAnimationFrame(function () { $('#rdpmsCircuitOverlay').addClass('tl-modal-open'); });
    $('body').addClass('tl-modal-active');
    if (typeof disconnectWebSocket === 'function') disconnectWebSocket();

    $.ajax({
        url: '/FRS25/Telemetry/_Circuit',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify({ assetTypeId: assetTypeId, assetId: assetId }),
        dataType: 'text',
        success: function (htmlContent) {
            $('#rdpmsCircuitLoading').hide();
            var $content = $('#rdpmsCircuitContent');

            // ── Inject CSS links into <head> ──
            var cssRx = /<link[^>]+href=["']([^"']+)["'][^>]*>/gi, cm;
            while ((cm = cssRx.exec(htmlContent)) !== null) {
                var href = cm[1].replace(/^~\//, '/');
                if (!document.querySelector('link[href="' + href + '"]')) {
                    var lnk = document.createElement('link');
                    lnk.rel = 'stylesheet'; lnk.href = href;
                    document.head.appendChild(lnk);
                }
            }
            // ── Extract external circuit script URLs ──
            var srcRx = /<script[^>]+data-circuit-script[^>]+src=["']([^"']+)["'][^>]*><\/script>/gi;
            var scripts = [], m;
            while ((m = srcRx.exec(htmlContent)) !== null) scripts.push(m[1]);

            // ── Strip tags we handle manually ──
            var cleanHtml = htmlContent
                .replace(/<script[^>]+data-circuit-script[^>]*>[\s\S]*?<\/script>/gi, '')
                .replace(/<link[^>]+rel=["']stylesheet["'][^>]*>/gi, '');

            $content[0].innerHTML = cleanHtml;
            $content.show();

            // Hide the redundant _Circuit.cshtml header row (our modal head is enough)
            $content.find('.col-lg-12[style*="border-bottom"]').css('border-bottom', '3px solid #259dab');
            $content.find('.col-lg-12 > .row').first().hide();

            function runInlineScripts() {
                $content.find('script').each(function () {
                    if (!this.src) {
                        try { window.eval(this.textContent || this.innerText); } catch (e) {
                            console.warn('[Circuit Modal] script error:', e);
                        }
                    }
                });
            }

            function postInit() {
                if (window.appModel && window.appModel.paper) {
                    window.appModel.paper.fitToContent({ padding: 20 });
                }
                // Connect WebSocket for live circuit data
                if (siteId && siteId !== '' && siteId !== '0') {
                    if (typeof connectCircuitWebSocket === 'function') {
                        connectCircuitWebSocket(siteId, assetTypeId, assetId);
                    }
                }
                console.log('[Circuit Modal] Ready — asset:', assetId);
            }

            if (_circuitScriptsLoaded || scripts.length === 0) {
                runInlineScripts();
                setTimeout(postInit, 300);
            } else {
                (function loadNext(i) {
                    if (i >= scripts.length) {
                        _circuitScriptsLoaded = true;
                        runInlineScripts();
                        setTimeout(postInit, 300);
                        return;
                    }
                    var src = scripts[i].replace(/^~\//, '/');
                    if (document.querySelector('script[src="' + src + '"]')) { loadNext(i + 1); return; }
                    var s = document.createElement('script');
                    s.src = src;
                    s.onload = function () { loadNext(i + 1); };
                    s.onerror = function () { loadNext(i + 1); };
                    document.head.appendChild(s);
                })(0);
            }
        },
        error: function () {
            $('#rdpmsCircuitLoading').hide();
            $('#rdpmsCircuitError').show();
        }
    });
}

function closeCircuitModal(e) {
    if (e && e.target && !$(e.target).hasClass('tl-modal-overlay')) return;
    if (typeof disconnectWebSocket === 'function') disconnectWebSocket();
    circuitAssetId = null;
    if (window.appModel) window.appModel = null;
    if (window.parseJson) window.parseJson = null;
    if (window._circuitMqttBridgeInterval) {
        clearInterval(window._circuitMqttBridgeInterval);
        window._circuitMqttBridgeInterval = null;
    }
    $('#rdpmsCircuitOverlay').removeClass('tl-modal-open');
    $('body').removeClass('tl-modal-active');
    setTimeout(function () { $('#rdpmsCircuitOverlay').remove(); }, 250);
}
$(document).on('keydown', function (e) { if (e.key === 'Escape' && $('#rdpmsCircuitOverlay').length) closeCircuitModal(); });

// ================================================================
// AUTO-LOAD (selection-driven) — shared entry used by Index.cshtml
// and loadAssetNumbersMulti(). Replaces the manual Search button.
// ================================================================
(function () {
    'use strict';
    var _tlLoadTimer = null;

    window._tlAutoLoad = function (opts) {
        opts = opts || {};

        // Optionally select every asset number (default "all selected").
        if (opts.selectAll) {
            $('#chkAllAssetNumber').prop('checked', true);
            if (typeof selectAllAssets === 'function') {
                selectAllAssets(true);
            } else {
                $('#listAssetNumber .dropdown-item').addClass('checked')
                    .find('input[type="checkbox"]').prop('checked', true);
            }
        }

        // Debounced load (coalesces rapid changes / double binds).
        if (_tlLoadTimer) clearTimeout(_tlLoadTimer);
        _tlLoadTimer = setTimeout(function () {
            _tlLoadTimer = null;
            var siteId = $('#drpSite').val();
            var atId = $('#drpAssetType').val();
            var ids = (typeof getSelectedAssetIds === 'function') ? getSelectedAssetIds() : [];
            if (!siteId || siteId === '0' || siteId === '') return;
            if (!atId || atId === '0' || atId === '') return;
            if (!ids || ids.length === 0) {
                // FIX: getSelectedAssetIds() returns [] when ALL are checked.
                var _tAL = $('#listAssetNumber input').length;
                var _cAL = $('#listAssetNumber input:checked').length;
                if (_cAL === 0 || _tAL === 0) return;
            }
            window.wsCurrentFilterAssetIds = ids;
            if (typeof window.fnAdvSearch === 'function') window.fnAdvSearch();
        }, 300);
    };
})();

function tlOpenTelemetryAssetPopup(assetId, source) {
    assetId = String(assetId || '').trim();
    if (!assetId || typeof window.OpenSipAssetPopupFromCell !== 'function') return;

    var asset = window.wsLiveData && window.wsLiveData[assetId];
    if (!asset) return;

    var assetName = asset.AssetName || ('Asset ' + assetId);
    var siteId = asset.SiteId || $('#drpSite').val();
    var assetTypeId = asset.AssetTypeId || $('#drpAssetType').val() || window.wsCurrentAssetTypeId;
    var assetTypeName = asset.AssetTypeName || '';

    window.OpenSipAssetPopupFromCell({
        id: assetId,
        assetId: assetId,
        AssetId: assetId,
        assetName: assetName,
        AssetName: assetName,
        siteId: siteId,
        SiteId: siteId,
        assetTypeId: assetTypeId,
        AssetTypeId: assetTypeId,
        stencilType: assetTypeName,
        type: assetTypeName,
        attrs: {
            label: { text: assetName },
            live: asset.attrs || {}
        },
        telemetryAsset: asset
    }, {
        source: source || 'telemetry-live-click',
        assetId: assetId,
        AssetId: assetId,
        assetName: assetName,
        AssetName: assetName,
        siteId: siteId,
        SiteId: siteId,
        assetTypeId: assetTypeId,
        AssetTypeId: assetTypeId
    });
}

window.tlOpenTelemetryAssetPopup = tlOpenTelemetryAssetPopup;

$(document)
    .off('click.tlAssetPopup', '.at-card-name, #wsLiveTable td.asset-name')
    .on('click.tlAssetPopup', '.at-card-name, #wsLiveTable td.asset-name', function () {
        var assetId = $(this).closest('.at-asset-card').attr('data-id');

        if (!assetId) {
            assetId = $(this).closest('tr[data-id]').attr('data-id');
        }

        tlOpenTelemetryAssetPopup(assetId, 'telemetry-live-click');
    });



// ================================================================
// BULK METADATA PLACEHOLDER ATTRIBUTE FIX
// Shows every configured assetAttributes entry even when WebSocket
// never sends a value for that attribute. Missing value renders as '-'.
// Put this block at the VERY END of telemetrylive.js, after all overrides.
// ================================================================
var bulkExpectedAttrMap = typeof bulkExpectedAttrMap !== 'undefined' ? bulkExpectedAttrMap : {};
var bulkAttrKeyByAssetAttrId = typeof bulkAttrKeyByAssetAttrId !== 'undefined' ? bulkAttrKeyByAssetAttrId : {};
var bulkAttributeUnion = typeof bulkAttributeUnion !== 'undefined' ? bulkAttributeUnion : [];
var bulkAssetNameById = typeof bulkAssetNameById !== 'undefined' ? bulkAssetNameById : {};
var bulkExpectedSchemaReady = false;
var bulkPlaceholderRenderTimer = null;

function tlBulkStr(v) {
    return (v === null || v === undefined) ? '' : String(v);
}

function tlBulkFirstNonEmpty() {
    for (var i = 0; i < arguments.length; i++) {
        var v = arguments[i];
        if (v !== null && v !== undefined && String(v).trim() !== '') return v;
    }
    return '';
}

function tlBulkGetAssetName(assetId, fallbackName) {
    var aid = tlBulkStr(assetId);

    return tlBulkFirstNonEmpty(
        bulkAssetNameById[aid],
        (typeof bulkAssetMap !== 'undefined' && bulkAssetMap && bulkAssetMap[aid])
            ? (bulkAssetMap[aid].Name || bulkAssetMap[aid].AssetName)
            : '',
        fallbackName,
        'Asset ' + aid
    );
}

function tlBulkRegisterExpectedAttr(assetId, attrId, attrKey, attrLabel, sequence) {
    var aid = tlBulkStr(assetId);
    var key = tlBulkStr(attrKey).trim();
    var label = tlBulkStr(attrLabel || attrKey).trim();
    var id = tlBulkStr(attrId).trim();

    if (!aid || !key) return;

    if (!bulkExpectedAttrMap[aid]) bulkExpectedAttrMap[aid] = [];

    var exists = false;

    for (var i = 0; i < bulkExpectedAttrMap[aid].length; i++) {
        if (bulkExpectedAttrMap[aid][i].key === key) {
            exists = true;
            break;
        }
    }

    if (!exists) {
        bulkExpectedAttrMap[aid].push({
            key: key,
            label: label || key,
            attrId: id,
            sequence: sequence
        });
    }

    if (id) {
        bulkAttrKeyByAssetAttrId[aid + '_' + id] = key;
        if (!bulkAttrKeyByAssetAttrId['*_' + id]) bulkAttrKeyByAssetAttrId['*_' + id] = key;

        if (typeof wsAttributeIds !== 'undefined') wsAttributeIds[key] = id;

        if (typeof assetAttributeMap !== 'undefined') {
            assetAttributeMap[id] = label || key;
            var nId = parseInt(id);
            if (!isNaN(nId)) assetAttributeMap[nId] = label || key;
        }
    }

    if (typeof assetAttributeByName !== 'undefined') {
        assetAttributeByName[key] = label || key;
        if (label) assetAttributeByName[label] = label;
    }

    if (typeof wsAttributeOrder !== 'undefined') {
        var seq = parseInt(sequence);
        var idSeq = parseInt(id);
        wsAttributeOrder[key] = !isNaN(seq) ? seq : (!isNaN(idSeq) ? idSeq : 9999);
    }

    if (bulkAttributeUnion.indexOf(key) === -1) bulkAttributeUnion.push(key);
}

function tlBulkSortAttributeUnion() {
    bulkAttributeUnion.sort(function (a, b) {
        if (parseInt(wsCurrentAssetTypeId) === 1 && typeof getTrackSortOrder === 'function') {
            var ta = getTrackSortOrder(a), tb = getTrackSortOrder(b);
            if (ta !== tb) return ta - tb;
        }

        var oa = (typeof wsAttributeOrder !== 'undefined' && wsAttributeOrder[a]) ? wsAttributeOrder[a] : 9999;
        var ob = (typeof wsAttributeOrder !== 'undefined' && wsAttributeOrder[b]) ? wsAttributeOrder[b] : 9999;

        if (oa !== ob) return oa - ob;

        return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
    });
}

function tlBulkRebuildExpectedSchemaFromLoadedMaps() {
    bulkExpectedAttrMap = {};
    bulkAttrKeyByAssetAttrId = {};
    bulkAttributeUnion = [];
    bulkAssetNameById = {};

    var currentAssetTypeId = parseInt(wsCurrentAssetTypeId || $('#drpAssetType').val() || 0);

    if (typeof bulkAssetsList !== 'undefined' && bulkAssetsList && bulkAssetsList.length) {
        for (var i = 0; i < bulkAssetsList.length; i++) {
            var ba = bulkAssetsList[i];
            if (!ba || ba.Id === undefined || ba.Id === null) continue;

            bulkAssetNameById[tlBulkStr(ba.Id)] = tlBulkFirstNonEmpty(
                ba.Name,
                ba.AssetName,
                'Asset ' + ba.Id
            );
        }
    }

    // Main source: this map is already filled from GetBulkAssetMetadata asset.assetAttributes.
    if (typeof userAssetSimpleMap !== 'undefined' && userAssetSimpleMap) {
        for (var sk in userAssetSimpleMap) {
            if (!userAssetSimpleMap.hasOwnProperty(sk)) continue;

            var entry = userAssetSimpleMap[sk];
            if (!entry) continue;

            var parts = sk.split('_');
            if (parts.length < 2) continue;

            var aid = parts[0];
            var attrId = parts.slice(1).join('_');

            var entryAssetTypeId = parseInt(entry.assetTypeId || entry.AssetTypeId || 0);
            if (currentAssetTypeId > 0 && entryAssetTypeId > 0 && entryAssetTypeId !== currentAssetTypeId) continue;

            var attrKey = tlBulkFirstNonEmpty(
                entry.attributeName,
                entry.AttributeName,
                entry.title,
                entry.Title,
                entry.name,
                entry.AliasName
            );

            var attrLabel = tlBulkFirstNonEmpty(
                entry.name,
                entry.AliasName,
                entry.aliasName,
                attrKey
            );

            var sequence = tlBulkFirstNonEmpty(entry.sequence, entry.Sequence, attrId);

            if (entry.assetName || entry.AssetName) {
                bulkAssetNameById[aid] = tlBulkFirstNonEmpty(
                    entry.assetName,
                    entry.AssetName,
                    bulkAssetNameById[aid]
                );
            }

            tlBulkRegisterExpectedAttr(aid, attrId, attrKey, attrLabel, sequence);
        }
    }

    // Fallback for signal grouped table cache.
    if (typeof _sigAssetInfoCache !== 'undefined' && _sigAssetInfoCache) {
        for (var aid2 in _sigAssetInfoCache) {
            if (!_sigAssetInfoCache.hasOwnProperty(aid2)) continue;

            var ce = _sigAssetInfoCache[aid2];
            if (!ce || !ce.attrs || !ce.attrs.length) continue;

            for (var c = 0; c < ce.attrs.length; c++) {
                tlBulkRegisterExpectedAttr(aid2, '', ce.attrs[c], ce.attrs[c], c + 1);
            }
        }
    }

    tlBulkSortAttributeUnion();

    if (bulkAttributeUnion.length > 0) {
        wsAttributeNames = bulkAttributeUnion.slice();

        if (typeof wsCurrentColumns !== 'undefined') wsCurrentColumns = [];
        if (typeof wsCurrentTableColumns !== 'undefined') wsCurrentTableColumns = [];
        if (typeof wsTableInitialized !== 'undefined') wsTableInitialized = false;
        if (typeof wsTableStructureBuilt !== 'undefined') wsTableStructureBuilt = false;
        if (typeof wsAssetOrderInitialized !== 'undefined') wsAssetOrderInitialized = false;
    }

    bulkExpectedSchemaReady = true;

    window.bulkExpectedAttrMap = bulkExpectedAttrMap;
    window.bulkAttrKeyByAssetAttrId = bulkAttrKeyByAssetAttrId;
    window.bulkAttributeUnion = bulkAttributeUnion;

    return bulkAttributeUnion.length;
}

function tlBulkGetCurrentAssetIdsForPlaceholders() {
    var selected = [];
    var filter = wsCurrentFilterAssetIds || [];

    for (var i = 0; i < filter.length; i++) {
        var fid = tlBulkStr(filter[i]);
        if (fid && fid !== '0') selected.push(fid);
    }

    if (selected.length > 0) return selected;

    if (typeof bulkAssetsList !== 'undefined' && bulkAssetsList && bulkAssetsList.length) {
        for (var j = 0; j < bulkAssetsList.length; j++) {
            if (bulkAssetsList[j] && bulkAssetsList[j].Id !== undefined && bulkAssetsList[j].Id !== null) {
                selected.push(tlBulkStr(bulkAssetsList[j].Id));
            }
        }
    }

    return selected;
}

function tlBulkEnsureAssetShell(assetId) {
    var aid = tlBulkStr(assetId);
    if (!aid) return null;

    var assetName = tlBulkGetAssetName(aid, wsLiveData[aid] ? wsLiveData[aid].AssetName : '');

    var assetTypeId = (typeof bulkAssetMap !== 'undefined' && bulkAssetMap && bulkAssetMap[aid] && bulkAssetMap[aid].AssetTypeId)
        ? bulkAssetMap[aid].AssetTypeId
        : (wsLiveData[aid] ? wsLiveData[aid].AssetTypeId : (wsCurrentAssetTypeId || $('#drpAssetType').val()));

    var siteId = (typeof bulkAssetMap !== 'undefined' && bulkAssetMap && bulkAssetMap[aid] && bulkAssetMap[aid].SiteId)
        ? bulkAssetMap[aid].SiteId
        : (wsLiveData[aid] ? wsLiveData[aid].SiteId : $('#drpSite').val());

    if (!wsLiveData[aid]) {
        wsLiveData[aid] = {
            AssetId: aid,
            AssetName: assetName,
            AssetTypeId: assetTypeId,
            SiteId: siteId,
            attrs: {},
            dlRelays: {},
            lastUpdated: null,
            ZeroOffsetValue: (typeof getZeroOffsetForAsset === 'function') ? getZeroOffsetForAsset(aid) : undefined,
            _fromBulkMetadataOnly: true
        };
    } else {
        wsLiveData[aid].AssetName = assetName || wsLiveData[aid].AssetName;
        wsLiveData[aid].AssetTypeId = wsLiveData[aid].AssetTypeId || assetTypeId;
        wsLiveData[aid].SiteId = wsLiveData[aid].SiteId || siteId;
        wsLiveData[aid].attrs = wsLiveData[aid].attrs || {};
        wsLiveData[aid].dlRelays = wsLiveData[aid].dlRelays || {};
    }

    return wsLiveData[aid];
}

function tlBulkSeedMissingAttributesForAsset(assetId) {
    var aid = tlBulkStr(assetId);
    var expected = bulkExpectedAttrMap[aid];

    if (!expected || !expected.length) return 0;

    var asset = tlBulkEnsureAssetShell(aid);
    if (!asset) return 0;

    var added = 0;
    var sigAttrs = [];

    for (var i = 0; i < expected.length; i++) {
        var e = expected[i];
        if (!e || !e.key) continue;

        sigAttrs.push(e.key);

        // If older code stored value under AliasName, link it to real schema key.
        if (e.label && e.label !== e.key && asset.attrs[e.label] && !asset.attrs[e.key]) {
            asset.attrs[e.key] = asset.attrs[e.label];
        }

        if (!asset.attrs[e.key]) {
            asset.attrs[e.key] = {
                Value: null,
                AttrId: e.attrId || null,
                AssetAttributeId: e.attrId || null,
                Timestamp: null,
                TimestampDevice: null,
                TimestampLocal: null,
                TimestampEdgeX: null,
                Source: 'BulkMeta',
                changed: false,
                prevValue: undefined,
                missingFromWebSocket: true
            };

            added++;
        } else {
            if (!asset.attrs[e.key].AttrId && e.attrId) asset.attrs[e.key].AttrId = e.attrId;
            if (!asset.attrs[e.key].AssetAttributeId && e.attrId) asset.attrs[e.key].AssetAttributeId = e.attrId;
        }
    }

    if (typeof _sigAssetInfoCache !== 'undefined' && sigAttrs.length > 0) {
        _sigAssetInfoCache[aid] = {
            loaded: true,
            loading: false,
            attrs: sigAttrs.slice(),
            pending: []
        };
    }

    return added;
}

function tlBulkEnsurePlaceholdersForCurrentFilter() {
    // Point Machine uses its own schema; do not seed PM from bulk placeholders.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip($('#drpAssetType').val() || wsCurrentAssetTypeId)) return 0;
    if (!bulkExpectedSchemaReady) tlBulkRebuildExpectedSchemaFromLoadedMaps();

    var ids = tlBulkGetCurrentAssetIdsForPlaceholders();
    var added = 0;

    for (var i = 0; i < ids.length; i++) {
        added += tlBulkSeedMissingAttributesForAsset(ids[i]);
    }

    // Also seed any asset that already came from WebSocket.
    for (var aid in wsLiveData) {
        if (wsLiveData.hasOwnProperty(aid)) {
            added += tlBulkSeedMissingAttributesForAsset(aid);
        }
    }

    if (bulkAttributeUnion.length > 0) {
        wsAttributeNames = bulkAttributeUnion.slice();
    }

    return added;
}

function tlBulkResolveWsAttributeKey(assetId, attrName, attrId, dataType) {
    if (String(dataType || '').toLowerCase() === 'datalogger') return attrName;

    var aid = tlBulkStr(assetId);
    var id = tlBulkStr(attrId);
    var raw = tlBulkStr(attrName);

    if (id) {
        var exact = bulkAttrKeyByAssetAttrId[aid + '_' + id];
        if (exact) return exact;

        var globalKey = bulkAttrKeyByAssetAttrId['*_' + id];
        if (globalKey) return globalKey;
    }

    var expected = bulkExpectedAttrMap[aid] || [];

    for (var i = 0; i < expected.length; i++) {
        if (expected[i].key === raw || expected[i].label === raw) return expected[i].key;
    }

    return raw;
}

function tlBulkNormalizeIncomingItem(d) {
    if (!d || d.AssetId === undefined || !d.AssetAttributeName) return d;

    var normalizedKey = tlBulkResolveWsAttributeKey(
        d.AssetId,
        d.AssetAttributeName,
        d.AssetAttributeId,
        d.DataType
    );

    var assetName = tlBulkGetAssetName(d.AssetId, d.AssetName);

    if (normalizedKey === d.AssetAttributeName && assetName === d.AssetName) return d;

    var copy = {};

    for (var k in d) {
        if (d.hasOwnProperty(k)) copy[k] = d[k];
    }

    copy.RawAssetAttributeName = d.AssetAttributeName;
    copy.AssetAttributeName = normalizedKey;
    copy.AssetName = assetName;

    return copy;
}

function tlBulkNormalizeIncomingItems(items) {
    if (!items || !items.length) return items;

    var out = new Array(items.length);

    for (var i = 0; i < items.length; i++) {
        out[i] = tlBulkNormalizeIncomingItem(items[i]);
    }

    return out;
}

function tlBulkRenderCurrentViewFromPlaceholders() {
    // Point Machine view is rendered by dedicated PM renderer, not bulk placeholders.
    if (typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip($('#drpAssetType').val() || wsCurrentAssetTypeId)) return;
    if (!bulkExpectedSchemaReady && !bulkMetadataLoaded) return;
    if (window._wsSipOnlyMode) return;

    tlBulkEnsurePlaceholdersForCurrentFilter();

    if (Object.keys(wsLiveData).length === 0) return;

    var viewType = $('#drpView').val();

    if (viewType === 'Graph' || viewType === 'Circuit' || viewType === 'Yard') return;

    try {
        if (viewType === 'Table') {
            if (typeof renderWsTable === 'function') renderWsTable();
        } else if (viewType === 'RDPMS') {
            if (typeof renderRDPMSView === 'function') renderRDPMSView();
        } else if (viewType === 'PointMachine') {
            if (typeof renderPointMachineView === 'function') renderPointMachineView();
        } else if (viewType === 'Cards') {
            if (typeof fnBindTrackCards === 'function') fnBindTrackCards();
        }
    } catch (e) {
        console.warn('[BulkMetaMissingAttr] placeholder render skipped:', e);
    }
}

function tlBulkSchedulePlaceholderRender(delayMs) {
    if (bulkPlaceholderRenderTimer) clearTimeout(bulkPlaceholderRenderTimer);

    bulkPlaceholderRenderTimer = setTimeout(function () {
        bulkPlaceholderRenderTimer = null;
        tlBulkRenderCurrentViewFromPlaceholders();
    }, delayMs || 700);
}

(function applyBulkMetadataMissingAttributeFix() {
    if (window.__bulkMetadataMissingAttributeFixApplied) return;
    window.__bulkMetadataMissingAttributeFixApplied = true;

    // Wrap bulk loader: after GetBulkAssetMetadata fills userAssetSimpleMap,
    // build expected schema and seed blank placeholders.
    if (typeof loadBulkAssetMetadata === 'function') {
        var _origLoadBulkAssetMetadata = loadBulkAssetMetadata;

        loadBulkAssetMetadata = function (siteId, assetTypeId, callback) {
            return _origLoadBulkAssetMetadata.call(this, siteId, assetTypeId, function () {
                tlBulkRebuildExpectedSchemaFromLoadedMaps();
                tlBulkEnsurePlaceholdersForCurrentFilter();

                if (callback) callback();
            });
        };

        window.loadBulkAssetMetadata = loadBulkAssetMetadata;
    }

    if (typeof invalidateBulkMetadataCache === 'function') {
        var _origInvalidateBulkMetadataCache = invalidateBulkMetadataCache;

        invalidateBulkMetadataCache = function () {
            bulkExpectedAttrMap = {};
            bulkAttrKeyByAssetAttrId = {};
            bulkAttributeUnion = [];
            bulkAssetNameById = {};
            bulkExpectedSchemaReady = false;

            return _origInvalidateBulkMetadataCache.apply(this, arguments);
        };

        window.invalidateBulkMetadataCache = invalidateBulkMetadataCache;
    }

    // After connectWebSocket resets wsLiveData, restore metadata placeholders.
    if (typeof connectWebSocket === 'function') {
        var _origConnectWebSocketBulkMissing = connectWebSocket;

        connectWebSocket = function (siteId, assetTypeId, assetIds) {
            var result = _origConnectWebSocketBulkMissing.apply(this, arguments);

            if (assetTypeId && assetTypeId !== '0' &&
                !(typeof isPointMachineAssetTypeForBulkSkip === 'function' && isPointMachineAssetTypeForBulkSkip(assetTypeId))) {
                if (typeof loadBulkAssetMetadata === 'function') {
                    loadBulkAssetMetadata(siteId, assetTypeId, function () {
                        tlBulkEnsurePlaceholdersForCurrentFilter();
                        tlBulkSchedulePlaceholderRender(900);
                    });
                } else {
                    tlBulkEnsurePlaceholdersForCurrentFilter();
                    tlBulkSchedulePlaceholderRender(900);
                }
            }

            return result;
        };

        window.connectWebSocket = connectWebSocket;
    }

    // Make syncAttributeNames use bulk schema, not only first WS asset.
    if (typeof syncAttributeNamesFromLiveData === 'function') {
        var _origSyncAttributeNamesFromLiveData = syncAttributeNamesFromLiveData;

        syncAttributeNamesFromLiveData = function () {
            if (!bulkExpectedSchemaReady) tlBulkRebuildExpectedSchemaFromLoadedMaps();

            if (bulkAttributeUnion.length > 0) {
                var changed = JSON.stringify(wsAttributeNames) !== JSON.stringify(bulkAttributeUnion);

                wsAttributeNames = bulkAttributeUnion.slice();

                if (changed) {
                    if (typeof wsCurrentColumns !== 'undefined') wsCurrentColumns = [];
                    if (typeof wsCurrentTableColumns !== 'undefined') wsCurrentTableColumns = [];
                    if (typeof wsTableInitialized !== 'undefined') wsTableInitialized = false;
                    if (typeof wsTableStructureBuilt !== 'undefined') wsTableStructureBuilt = false;
                }

                return changed;
            }

            return _origSyncAttributeNamesFromLiveData.apply(this, arguments);
        };

        window.syncAttributeNamesFromLiveData = syncAttributeNamesFromLiveData;
    }

    // Wrap inbound processors so WS values update the seeded schema key by AssetAttributeId.
    if (typeof window.processItemsInternal === 'function') {
        var _origProcessItemsInternalBulkMissing = window.processItemsInternal;

        window.processItemsInternal = function (items) {
            tlBulkEnsurePlaceholdersForCurrentFilter();
            return _origProcessItemsInternalBulkMissing.call(this, tlBulkNormalizeIncomingItems(items));
        };

        if (typeof processItemsInternal !== 'undefined') processItemsInternal = window.processItemsInternal;
    }

    if (typeof processItems === 'function') {
        var _origProcessItemsBulkMissing = processItems;

        processItems = function (items) {
            tlBulkEnsurePlaceholdersForCurrentFilter();
            return _origProcessItemsBulkMissing.call(this, tlBulkNormalizeIncomingItems(items));
        };

        window.processItems = processItems;
    }

    if (typeof processSingleLiveUpdate === 'function') {
        var _origProcessSingleLiveUpdateBulkMissing = processSingleLiveUpdate;

        processSingleLiveUpdate = function (d) {
            tlBulkEnsurePlaceholdersForCurrentFilter();
            return _origProcessSingleLiveUpdateBulkMissing.call(this, tlBulkNormalizeIncomingItem(d));
        };

        window.processSingleLiveUpdate = processSingleLiveUpdate;
    }

    // Renderers: always seed before table/card/list builds.
    function wrapRenderer(name) {
        if (typeof window[name] !== 'function') return;

        var original = window[name];

        window[name] = function () {
            tlBulkEnsurePlaceholdersForCurrentFilter();
            return original.apply(this, arguments);
        };
    }

    wrapRenderer('renderWsTable');
    wrapRenderer('renderWsTableFixed');
    wrapRenderer('renderPmTableView');
    wrapRenderer('renderRDPMSView');
    wrapRenderer('renderPointMachineView');
    wrapRenderer('fnBindTrackCards');
    wrapRenderer('renderSignalGroupedTables');

    // Also update identifier bindings where existing code calls direct names.
    if (typeof window.renderWsTable === 'function') renderWsTable = window.renderWsTable;
    if (typeof window.renderWsTableFixed === 'function') renderWsTableFixed = window.renderWsTableFixed;
    if (typeof window.renderPmTableView === 'function') renderPmTableView = window.renderPmTableView;
    if (typeof window.renderRDPMSView === 'function') renderRDPMSView = window.renderRDPMSView;
    if (typeof window.renderPointMachineView === 'function') renderPointMachineView = window.renderPointMachineView;
    if (typeof window.fnBindTrackCards === 'function') fnBindTrackCards = window.fnBindTrackCards;
    if (typeof window.renderSignalGroupedTables === 'function') renderSignalGroupedTables = window.renderSignalGroupedTables;

    console.log('[BulkMetaMissingAttr] Applied — configured attributes now render as - when WS value is missing.');
})();
// ================================================================
// TELEMETRY-LIVE BINDING FIXES v4 — Clean patch
// Applied at file-end so all base functions are defined first.
//
// ROOT CAUSE of "data not binding":
//   Attributes MUST stay keyed by their raw WS name (e.g. "If mA")
//   in wsLiveData and wsAttributeNames. Alias resolution to
//   "ITC FEED END(mA)" must happen ONLY at display time via
//   getAttrDisplayName(). Previous patches that renamed keys at
//   storage time broke the streaming pipeline because the next WS
//   batch writes data under the raw name while the table reads
//   the (now stale) alias key.
//
// FIXES APPLIED:
//   A — Numeric-id registration in assetAttributeMap
//   B — resolveBulkDataloggerName matches DataloggerAssetName
//   C — processItemsInternal forwards roleOrAttrId for DL items
//   D — scheduleUIUpdate after DL-only batches
//   E — Deduplicate DL relay keys (raw vs display)
//   F — getAttrDisplayName per-asset alias priority
//   G — aria-hidden popup fix (inert attribute)
// ================================================================

(function applyTelemetryBindingFixesV4() {
    'use strict';

    if (window.__telemetryBindingFixesV4Applied) return;
    window.__telemetryBindingFixesV4Applied = true;

    function _s(v) { return (v === null || v === undefined) ? '' : String(v).trim(); }
    function _n(v) { var n = parseInt(v, 10); return isNaN(n) ? null : n; }

    // ================================================================
    // FIX-A  Ensure assetAttributeMap has numeric keys too.
    //        GetBulkAssetMetadata stores assetAttributeMap["1"] = alias
    //        but WS sends AssetAttributeId as number 1.
    //        getAttrDisplayName → getBulkAliasName → bulkAliasByAttrId
    //        only checks string keys.  assetAttributeMap however is
    //        also checked directly with numeric keys.
    // ================================================================
    function reindexBulkAliasByNumericId() {
        if (typeof assetAttributeMap === 'undefined') return;

        // From bulkAliasByAttrId (string keys → aliases)
        if (typeof bulkAliasByAttrId !== 'undefined') {
            for (var idKey in bulkAliasByAttrId) {
                if (!bulkAliasByAttrId.hasOwnProperty(idKey)) continue;
                var alias = bulkAliasByAttrId[idKey];
                if (!alias) continue;
                var n = _n(idKey);
                if (n !== null && !assetAttributeMap[n]) {
                    assetAttributeMap[n] = alias;
                }
            }
        }

        // Also from assetAttributeMap's own string keys
        for (var k in assetAttributeMap) {
            if (!assetAttributeMap.hasOwnProperty(k)) continue;
            var numK = _n(k);
            if (numK !== null && !assetAttributeMap[numK]) {
                assetAttributeMap[numK] = assetAttributeMap[k];
            }
        }
    }

    try { reindexBulkAliasByNumericId(); } catch (e) { }

    // Hook loadBulkAssetMetadata to re-index after each load
    var _origLBM = window.loadBulkAssetMetadata;
    if (typeof _origLBM === 'function') {
        window.loadBulkAssetMetadata = function (siteId, assetTypeId, callback) {
            return _origLBM.call(this, siteId, assetTypeId, function () {
                try { reindexBulkAliasByNumericId(); } catch (e) { }
                if (callback) callback();
            });
        };
        if (typeof loadBulkAssetMetadata !== 'undefined') {
            loadBulkAssetMetadata = window.loadBulkAssetMetadata;
        }
    }

    // ================================================================
    // FIX-B  resolveBulkDataloggerName: scan by DataloggerAssetName
    //
    //        mAssetInfoDataloggers example:
    //          { Id: 282, DataloggerAttributeId: 6,
    //            DataloggerAttribute: "TPR",
    //            DataloggerAssetName: "1_2TPR" }
    //
    //        WS sends: AssetAttributeName = "1_2TPR"
    //        Existing resolver only matched by role id, not by the
    //        DataloggerAssetName text. This fix scans entries for
    //        a text match against dataloggerAssetName AND
    //        dataloggerAttribute fields.
    // ================================================================
    var _origResolveBDN = window.resolveBulkDataloggerName;
    window.resolveBulkDataloggerName = function (assetId, roleOrAttrId, rawName) {
        // Try existing logic first
        var existing = '';
        if (_origResolveBDN && typeof _origResolveBDN === 'function') {
            try { existing = _origResolveBDN.call(this, assetId, roleOrAttrId, rawName); } catch (e) { }
        }
        if (existing && existing !== rawName && !/^attr\s+\d+$/i.test(existing)) {
            return existing;
        }

        var aid = _s(assetId);
        var prefix = aid + '_';
        var rawLc = _s(rawName).toLowerCase();
        var roleLc = _s(roleOrAttrId).toLowerCase();

        // Scan bulkDataloggerMap
        if (typeof bulkDataloggerMap !== 'undefined') {
            for (var k in bulkDataloggerMap) {
                if (!bulkDataloggerMap.hasOwnProperty(k)) continue;
                if (aid && k.indexOf(prefix) !== 0) continue;
                var e = bulkDataloggerMap[k];
                if (!e || !e.name) continue;

                var dlAsset = _s(e.dataloggerAssetName).toLowerCase();
                var dlAttr = _s(e.dataloggerAttribute || e.attributeName).toLowerCase();

                if (rawLc && dlAsset && dlAsset === rawLc) return e.name;
                if (rawLc && dlAttr && dlAttr === rawLc) return e.name;
                if (roleLc && dlAsset && dlAsset === roleLc) return e.name;
                if (roleLc && dlAttr && dlAttr === roleLc) return e.name;
            }
        }

        // Scan userAssetDataloggerMap
        if (typeof userAssetDataloggerMap !== 'undefined') {
            for (var uk in userAssetDataloggerMap) {
                if (!userAssetDataloggerMap.hasOwnProperty(uk)) continue;
                if (aid && uk.indexOf(prefix) !== 0) continue;
                var ue = userAssetDataloggerMap[uk];
                if (!ue || !ue.name) continue;

                var udlAsset = _s(ue.dataloggerAssetName).toLowerCase();
                var udlAttr = _s(ue.dataloggerAttribute || ue.attributeName).toLowerCase();

                if (rawLc && udlAsset && udlAsset === rawLc) return ue.name;
                if (rawLc && udlAttr && udlAttr === rawLc) return ue.name;
                if (roleLc && udlAsset && udlAsset === roleLc) return ue.name;
                if (roleLc && udlAttr && udlAttr === roleLc) return ue.name;
            }
        }

        return existing || rawName || '';
    };
    if (typeof resolveBulkDataloggerName !== 'undefined') {
        resolveBulkDataloggerName = window.resolveBulkDataloggerName;
    }

    // ================================================================
    // FIX-C  processItemsInternal: intercept DataLogger items to
    //        pass AssetAttributeId as roleOrAttrId (6th arg) and
    //        seed runtime DL maps for instant future lookups.
    //
    //        CRITICAL: Non-DataLogger items pass through UNCHANGED
    //        to the original processItemsInternal. NO renaming of
    //        wsAttributeNames or wsLiveData keys.
    // ================================================================
    var _origPII = window.processItemsInternal;
    window.processItemsInternal = function (items) {
        if (!items || !Array.isArray(items)) {
            return _origPII.call(this, items);
        }

        var nonDL = [];
        var dlItems = [];

        for (var i = 0; i < items.length; i++) {
            var d = items[i];
            if (d && d.DataType === 'DataLogger') {
                dlItems.push(d);
            } else {
                nonDL.push(d);
            }
        }

        // Process DataLogger items with roleOrAttrId context
        for (var j = 0; j < dlItems.length; j++) {
            var dl = dlItems[j];
            if (!dl.AssetId || !dl.AssetAttributeName) continue;

            // Apply same filters as original processItemsInternal
            var atFilter = window.wsCurrentAssetTypeId;
            var aidFilter = window.wsCurrentFilterAssetIds;

            if (atFilter && atFilter !== '0' && atFilter !== '' && dl.AssetTypeId) {
                if (_s(dl.AssetTypeId) !== _s(atFilter)) continue;
            }
            // FIX: was > 1 — same off-by-one as main processItemsInternal
            if (aidFilter && aidFilter.length > 0 && aidFilter[0] !== '' && aidFilter[0] !== '0') {
                if (aidFilter.map(_s).indexOf(_s(dl.AssetId)) === -1) continue;
            }

            var ts = dl.TimestampDevice || dl.TimestampLocal || dl.TimestampChange || new Date().toISOString();

            // DataLogger id must come from Role.
            // If WS does not send Role, resolve Role from GetBulkAssetMetadata using AssetAttributeName.
            var roleId = dl.Value || dl.value || '';

            if (!roleId && typeof window.resolveBulkDataloggerRole === 'function') {
                roleId = window.resolveBulkDataloggerRole(dl.AssetId, dl.AssetAttributeName, dl.AssetAttributeName);
            }

            // Last fallback is raw name only, not Id / DataloggerAttributeId.
            if (!roleId) roleId = dl.AssetAttributeName;

            // Seed bulkDataloggerMap at runtime so next message resolves instantly
            var resolvedName = '';
            try {
                resolvedName = window.resolveBulkDataloggerName(dl.AssetId, roleId, dl.AssetAttributeName) || '';
            } catch (e) { }

            if (resolvedName && resolvedName !== dl.AssetAttributeName) {
                var dlAid = _s(dl.AssetId);
                var rawKey = _s(dl.AssetAttributeName);
                var seedEntry = {
                    name: resolvedName,
                    attributeName: resolvedName,
                    dataloggerAttribute: resolvedName,
                    dataloggerAssetName: rawKey,
                    assetName: dl.AssetName || ''
                };
                if (typeof bulkDataloggerMap !== 'undefined') {
                    bulkDataloggerMap[dlAid + '_' + rawKey] = seedEntry;
                }
                if (typeof userAssetDataloggerMap !== 'undefined') {
                    userAssetDataloggerMap[dlAid + '_' + rawKey] = seedEntry;
                }
                if (typeof dlAssetRoleMap !== 'undefined') {
                    dlAssetRoleMap[dlAid + '_' + rawKey] = resolvedName;
                }
                if (typeof dlRoleNameMap !== 'undefined') {
                    dlRoleNameMap[rawKey] = resolvedName;
                }
            }

            // Call processWsDataloggerAttr with roleOrAttrId
            if (typeof window.processWsDataloggerAttr === 'function') {
                window.processWsDataloggerAttr(
                    dl.AssetId,
                    dl.AssetName,
                    dl.AssetAttributeName,
                    dl.Value,
                    ts,
                    roleId
                );
            }

            // FIX-E  Deduplicate DL relay keys
            if (resolvedName && resolvedName !== dl.AssetAttributeName) {
                try {
                    var liveAsset = window.wsLiveData[dl.AssetId];
                    if (liveAsset && liveAsset.dlRelays) {
                        var rawAttr = dl.AssetAttributeName;
                        // If the raw key exists and points to same data as display key, remove raw
                        if (liveAsset.dlRelays.hasOwnProperty(rawAttr) &&
                            liveAsset.dlRelays.hasOwnProperty(resolvedName)) {
                            // Make raw key non-enumerable so it doesn't show as duplicate pill
                            try {
                                var relayRef = liveAsset.dlRelays[resolvedName];
                                delete liveAsset.dlRelays[rawAttr];
                                Object.defineProperty(liveAsset.dlRelays, rawAttr, {
                                    value: relayRef,
                                    enumerable: false,
                                    configurable: true,
                                    writable: true
                                });
                            } catch (e2) {
                                delete liveAsset.dlRelays[rawAttr];
                            }
                        }
                    }
                } catch (e) { }
            }

            if (!window.wsUpdatedAssets) window.wsUpdatedAssets = {};
            window.wsUpdatedAssets[dl.AssetId] = true;
        }

        // Pass non-DataLogger items to original — UNTOUCHED
        if (nonDL.length > 0) {
            _origPII.call(this, nonDL);
        }

        // FIX-D  If DL items were processed, schedule UI update
        if (dlItems.length > 0) {
            try {
                window.wsPendingUIUpdate = true;
                if (typeof scheduleUIUpdate === 'function') {
                    scheduleUIUpdate(false);
                } else if (typeof triggerUIUpdate === 'function') {
                    triggerUIUpdate();
                }
            } catch (e) { }
        }
    };
    if (typeof processItemsInternal !== 'undefined') {
        processItemsInternal = window.processItemsInternal;
    }

    // ================================================================
    // FIX-D (continued)  Patch processDataLoggerItems to schedule
    //        UI update after processing DL items so cards/table
    //        refresh with updated relay badges.
    // ================================================================
    var _origProcDLI = window.processDataLoggerItems;
    if (typeof _origProcDLI === 'function') {
        window.processDataLoggerItems = function (items) {
            _origProcDLI.call(this, items);
            try {
                window.wsPendingUIUpdate = true;
                if (typeof scheduleUIUpdate === 'function') {
                    scheduleUIUpdate(false);
                } else if (typeof triggerUIUpdate === 'function') {
                    triggerUIUpdate();
                }
            } catch (e) { }
        };
    }

    // ================================================================
    // FIX-F  getAttrDisplayName: per-asset + attrId lookup takes
    //        priority over global map to avoid cross-asset alias
    //        pollution.
    // ================================================================
    var _origGADN = window.getAttrDisplayName;
    window.getAttrDisplayName = function (attrName, attrId, assetId) {
        var aid = _s(assetId);
        var idKey = _s(attrId);

        // 1. Per-asset + attrId from bulk (most specific)
        if (aid && idKey && typeof bulkAliasByAssetAttrId !== 'undefined') {
            var perAsset = bulkAliasByAssetAttrId[aid + '_' + idKey];
            if (perAsset) return perAsset;
        }

        // 2. Per-asset + attrName from bulk
        if (aid && attrName && typeof bulkAliasByAssetAttrName !== 'undefined') {
            var nk = String(attrName).trim().toUpperCase();
            var perAssetName = bulkAliasByAssetAttrName[aid + '_' + nk];
            if (perAssetName) return perAssetName;
        }

        // 3. Global attrId from assetAttributeMap (covers numeric keys from FIX-A)
        if (idKey && typeof assetAttributeMap !== 'undefined') {
            var byStrId = assetAttributeMap[idKey];
            if (byStrId) return byStrId;
            var numId = _n(idKey);
            if (numId !== null && assetAttributeMap[numId]) return assetAttributeMap[numId];
        }

        // 4. Delegate to original for remaining fallbacks
        if (_origGADN && typeof _origGADN === 'function') {
            return _origGADN.call(this, attrName, attrId, assetId);
        }

        return attrName || '';
    };
    if (typeof getAttrDisplayName !== 'undefined') {
        getAttrDisplayName = window.getAttrDisplayName;
    }

    // ================================================================
    // FIX-G  SIP Asset Popup: aria-hidden / inert fix
    //
    //        Browser warning:
    //          "Blocked aria-hidden on a focused element because its
    //           descendant retained focus."
    //        The overlay uses display:none to hide, but something
    //        also sets aria-hidden="true". When shown, the close
    //        button can receive focus while aria-hidden is still on
    //        the ancestor.
    //
    //        Fix: use the `inert` attribute when hidden, remove it
    //        when shown. Never set aria-hidden on a focusable ancestor.
    // ================================================================
    function fixSipPopupAccessibility() {
        var overlay = document.getElementById('sipAssetPopupOverlay');
        if (!overlay) return;

        // Remove any existing aria-hidden — use inert instead
        overlay.removeAttribute('aria-hidden');

        // The popup uses the CSS class 'sap-show' to toggle display:flex,
        // NOT inline style.display. Check the class to determine visibility.
        var isVisible = overlay.classList.contains('sap-show');
        if (!isVisible) {
            overlay.setAttribute('inert', '');
        } else {
            overlay.removeAttribute('inert');
        }

        // Observe class changes (sap-show toggling) instead of style changes
        var observer = new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var m = mutations[i];
                if (m.type === 'attributes') {
                    if (m.attributeName === 'class') {
                        var shown = overlay.classList.contains('sap-show');
                        if (shown) {
                            overlay.removeAttribute('inert');
                            overlay.removeAttribute('aria-hidden');
                        } else {
                            overlay.setAttribute('inert', '');
                            overlay.removeAttribute('aria-hidden');
                        }
                    }
                    // Prevent external code from setting aria-hidden
                    if (m.attributeName === 'aria-hidden') {
                        overlay.removeAttribute('aria-hidden');
                    }
                }
            }
        });

        observer.observe(overlay, {
            attributes: true,
            attributeFilter: ['class', 'aria-hidden']
        });

        console.log('[TL-Fix-G] SIP popup aria-hidden fix applied (watching class for sap-show)');
    }

    // Run after DOM is ready and after any popup creation
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        setTimeout(fixSipPopupAccessibility, 500);
    } else {
        document.addEventListener('DOMContentLoaded', function () {
            setTimeout(fixSipPopupAccessibility, 500);
        });
    }

    // Also observe body for the overlay being created dynamically
    var bodyObserver = new MutationObserver(function (mutations) {
        for (var i = 0; i < mutations.length; i++) {
            if (mutations[i].addedNodes) {
                for (var j = 0; j < mutations[i].addedNodes.length; j++) {
                    var node = mutations[i].addedNodes[j];
                    if (node.id === 'sipAssetPopupOverlay' ||
                        (node.querySelector && node.querySelector('#sipAssetPopupOverlay'))) {
                        setTimeout(fixSipPopupAccessibility, 50);
                        bodyObserver.disconnect();
                        return;
                    }
                }
            }
        }
    });

    if (document.body) {
        bodyObserver.observe(document.body, { childList: true, subtree: false });
    }

    // ================================================================
    // DIAGNOSTIC: Console verification helper
    // Usage: window.tlVerifyBinding(assetId)
    // ================================================================
    window.tlVerifyBinding = function (assetId) {
        console.group('[TL-Verify] Binding check for asset', assetId);
        var aid = _s(assetId);

        // Bulk loaded?
        console.log('bulkMetadataLoaded:', typeof bulkMetadataLoaded !== 'undefined' ? bulkMetadataLoaded : '?');

        // assetAttributeMap numeric keys
        var sampleIds = [1, 2, 3, 4, 5, 6];
        sampleIds.forEach(function (id) {
            var str = typeof assetAttributeMap !== 'undefined' ? (assetAttributeMap[id] || assetAttributeMap[String(id)] || 'NOT FOUND') : 'N/A';
            console.log('assetAttributeMap[' + id + ']:', str);
        });

        // wsLiveData attrs (raw keys)
        if (typeof wsLiveData !== 'undefined' && wsLiveData[assetId]) {
            var attrKeys = Object.keys(wsLiveData[assetId].attrs || {});
            console.log('wsLiveData[' + assetId + '].attrs keys:', attrKeys);
            console.log('wsLiveData[' + assetId + '].dlRelays:', wsLiveData[assetId].dlRelays);
        } else {
            console.warn('wsLiveData[' + assetId + '] not found');
        }

        // wsAttributeNames
        console.log('wsAttributeNames:', typeof wsAttributeNames !== 'undefined' ? wsAttributeNames : 'N/A');

        // Datalogger map entries
        if (typeof bulkDataloggerMap !== 'undefined') {
            var dlEntries = [];
            for (var k in bulkDataloggerMap) {
                if (k.indexOf(aid + '_') === 0) dlEntries.push({ key: k, name: bulkDataloggerMap[k].name });
            }
            console.log('bulkDataloggerMap for asset:', dlEntries);
        }

        // Test display name resolution
        if (typeof wsAttributeNames !== 'undefined') {
            wsAttributeNames.forEach(function (rawN) {
                var attrId = typeof wsAttributeIds !== 'undefined' ? wsAttributeIds[rawN] : null;
                var display = (typeof getAttrDisplayName === 'function')
                    ? getAttrDisplayName(rawN, attrId, assetId)
                    : rawN;
                if (display !== rawN) {
                    console.log('  "' + rawN + '" (id=' + attrId + ') → "' + display + '"');
                }
            });
        }

        console.groupEnd();
    };

    console.log('[TL-Fix-v4] Applied: A(NumericId) B(DLAssetName) C(roleOrAttrId) D(UIUpdate) E(DedupKeys) F(PerAssetAlias) G(AriaHidden-ClassWatch)');
})();
