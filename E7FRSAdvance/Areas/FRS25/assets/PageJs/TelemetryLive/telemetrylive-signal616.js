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
        return '<div class="card-header-right" style="background:var(--primary)"><div class="sig-card-actions">' +
            '<button class="sig-action-btn sig-action-graph" onclick="fnGetAssetGraph(\'' + s + '\',\'' + a + '\')" title="Historical Graph"><i class="fa-solid fa-chart-line"></i></button>' +
            '<button class="sig-action-btn sig-action-circuit" onclick="fnGetAssetCircuit(\'' + a + '\')" title="Circuit Diagram"><i class="fa-solid fa-project-diagram"></i></button>' +
            '<button class="sig-action-btn sig-action-avg" onclick="fnShowFRSAttributeRangeHistory(\'' + a + '\')" title="Avg values"><i class="fa-solid fa-chart-column"></i></button>' +
            '</div></div>';
    }
    var CARD_COL = 'col-12 col-xxl-4 col-xl-6 col-lg-6 col-md-12 col-sm-12 rdpms-signal-card';

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
        h += '<div class="card card-border"><div class="card-header-signal bg-secondary"><div class="card-header-left" style="background:var(--primary)"><h6>Signal : ' +
            esc(asset.AssetName || 'Signal') + '<br/><span id="rdpmsTs_' + id + '" style="font-size:10px;opacity:0.7;">Updated: </span></h6></div>' +
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
        h += '<div class="card card-border"><div class="card-header-signal" style="background:var(--primary)"><div class="card-header-left"><h6>Signal : ' +
            esc(asset.AssetName || 'Shunt') + '<br/><span id="rdpmsTs_' + id + '" style="font-size:10px;opacity:0.7;">Updated: </span></h6></div>' +
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
