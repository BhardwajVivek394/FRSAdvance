/* ==========================================================================
 *  SIP Telemetry — Live View  (v617.6 — performance + 616 parity rewrite)
 *  ------------------------------------------------------------------------
 *  Live binding engine for the SIP3 yard schematic (sip-library.js renderer).
 *
 *  WHAT IT DOES
 *  ────────────
 *  1. Fetches the saved SIP layout from /Telemetry/GetSipView (cached per site,
 *     coalesced so a site change never fires two fetches / two builds).
 *  2. Renders it ONCE into #sipCanvas, then keeps a cellId → <g> DOM index.
 *  3. Subscribes to live telemetry in BRIDGE MODE (wraps
 *     window.processItemsInternal / parseBatchMessages of telemetrylive.js) and
 *     opens its own site-wide socket only when the host has no site-wide stream.
 *  4. Absorbs every item into a per-asset store (values, IsFresh, timestamps,
 *     DataLogger relays) with O(1) alias resolution (memoised) and O(1)
 *     asset → cell lookup (memoised candidate expansion).
 *  5. Re-evaluates ONLY the touched cells with 616-equivalent rules:
 *       • Signal aspect  = telemetrylive.js computeSignalState (RDPMS mA fresh &
 *         > ZeroOffset, most-restrictive wins; DataLogger ECR fallback per lamp;
 *         IsFresh authoritative, replay-without-IsFresh usable, 15 s ts group).
 *       • Point machine  = energised KR (max NWKR/RWKR > PM_DIRECTION_THRESHOLD
 *         = 10) wins; both energised → freshest indication; DL NWKR/RWKR relay
 *         fallback; nothing energised → neutral.
 *       • Track          = TPR DataLogger relay (pickup = clear, drop = occupied),
 *         else TPR V (< 1 V = occupied), else Vr / Choke bands.
 *       • Values ≤ 0 are floored to 0 (tlZeroFloor); stale (IsFresh=false)
 *         values cannot light a lamp and mark the cell with .sip-stale.
 *     When telemetrylive.js owns the wsLiveData entry, computeSignalState /
 *     pmEnergisedDirection / resolvePmDirectionLikeTable are delegated to.
 *  6. Patches only the dirty <g> cells (and the rail-bed overlay for tracks)
 *     inside one requestAnimationFrame — no full re-render per message.
 *
 *  PUBLIC API (unchanged)
 *  ──────────────────────
 *    SipTelemetry.connectToSite(siteId)   SipTelemetry.disconnect()
 *    SipTelemetry.refresh()               SipTelemetry.highlight(query)
 *    SipTelemetry.applyPayload(payload)   SipTelemetry.installBridge()
 *    SipTelemetry.diagnose()              SipTelemetry.simulate(asset, attr, v, reset)
 *    SipTelemetry._state
 *  Set window.SIP_DEBUG = true for per-cell decision logs, false to silence all.
 * ========================================================================== */

(function () {
    'use strict';

    if (!window.SIP) {
        console.error('[sip-telemetry] window.SIP missing — load sip-library.js first.');
        return;
    }

    /* =========================================================================
       CONSTANTS
       ========================================================================= */
    var WS_BASE = (window.APP_CONFIG && window.APP_CONFIG.WebSocketBaseUrl
        ? window.APP_CONFIG.WebSocketBaseUrl
        : (window.location.protocol === 'https:'
            ? 'wss://proxy.energy7.org:8081'
            : 'ws://proxy.energy7.org:8081')) + '/subscribe/liveValue';

    var MAX_RECONNECT = 15;
    var RECONNECT_MS = 3000;
    var HEARTBEAT_MS = 30000;
    var WATCHDOG_TICK_MS = 3000;
    var WATCHDOG_SILENT_MS = 6000;
    var RESYNC_MS = 3000;
    var ALERT_POLL_MS = 30000;
    var LAYOUT_CACHE_TTL_MS = 10 * 60 * 1000;
    var DEDUP_TTL_MS = 10000;
    var ALIAS_MEMO_TTL_MS = 5000;

    /* Thresholds — mirror telemetrylive.js (616) */
    var ZERO_OFFSET_DEFAULT = 5.0;          // RDPMS_DEFAULT_THRESHOLD
    var TRACK_OCC_THR = 1.0;                // TPR V below this = occupied
    var PM_DIR_THR_DEFAULT = 10;            // PM_DIRECTION_THRESHOLD
    var SIGNAL_TS_REL_WINDOW_MS = 15 * 1000; // IsFresh-missing: latest mA group margin
    var REPLAY_STALE_MS = 3 * 60 * 1000;    // replay w/o IsFresh: display-stale age
    var PM_BLINK_MS = 6000;

    var PM_NWKR_IDS = [25, 27, 576, 578];
    var PM_RWKR_IDS = [26, 28, 577, 579];

    /* Colours — same palette as sip-library.js */
    var OFF_GREY = '#3c4260';
    var PM_OFF = '#d4d4d4';
    var C_RED = '#FF2E2E';
    var C_YELLOW = '#FFD400';
    var C_GREEN = '#22D142';

    /* ── Console logging ──────────────────────────────────────────────────
       slog  — verbose per-cell decisions, ONLY when window.SIP_DEBUG === true
       sinfo — lifecycle (layout loaded, sockets), unless SIP_DEBUG === false
       swarn — warnings, unless SIP_DEBUG === false                          */
    function slog() {
        if (window.SIP_DEBUG !== true) return;
        console.log.apply(console, ['[sip-telemetry]'].concat([].slice.call(arguments)));
    }
    function sinfo() {
        if (window.SIP_DEBUG === false) return;
        console.log.apply(console, ['[sip-telemetry]'].concat([].slice.call(arguments)));
    }
    function swarn() {
        if (window.SIP_DEBUG === false) return;
        console.warn.apply(console, ['[sip-telemetry]'].concat([].slice.call(arguments)));
    }
    var _warnOnceKeys = {};
    function swarnOnce(key) {
        if (_warnOnceKeys[key]) return;
        _warnOnceKeys[key] = 1;
        swarn.apply(null, [].slice.call(arguments, 1));
    }
    function clearWarnOnce(prefix) {
        if (!prefix) { _warnOnceKeys = {}; return; }
        for (var k in _warnOnceKeys) {
            if (_warnOnceKeys.hasOwnProperty(k) && k.indexOf(prefix) === 0) delete _warnOnceKeys[k];
        }
    }

    /* ── Asset type maps ─────────────────────────────────────────────────── */
    var COMPOSITE_SIGNAL = { 'examples.Signal': 1, 'examples.SignalShunt': 1 };
    var LAMP_SIGNAL = {
        'examples.Signald90': 1, 'examples.Signal90': 1,
        'examples.Signal45': 1, 'examples.Signald45': 1
    };
    var TRACK_STROKE = {
        'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1,
        'examples.Track5': 1, 'examples.Track6': 1
    };
    var RAIL_LAYER_TRACK = { 'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1 };
    var TRACK_FILL = { 'examples.Track3': 1, 'examples.Track4': 1 };
    var PM_TYPES = { 'examples.PointMachine': 1, 'examples.PointMachine1': 1 };
    var SHUNT_TYPES = { 'examples.Shaunt': 1, 'examples.Shaunt2': 1, 'examples.Shaunt3': 1 };
    var ROUTE_CALLING_TYPES = { 'examples.RouteCallingSignal': 1 };
    var BUSBAR_TYPES = { 'examples.BusBar': 1 };
    var AXLE_TYPES = { 'examples.AxleCounter': 1 };
    var GATE_TYPES = { 'examples.Gate': 1 };
    var LAMP_COLOUR = {
        'examples.Signald90': C_RED, 'examples.Signal90': C_GREEN,
        'examples.Signal45': C_YELLOW, 'examples.Signald45': C_YELLOW
    };
    var LAMP_TAG = {
        'examples.Signald90': 'RG', 'examples.Signal45': 'HG',
        'examples.Signal90': 'DG', 'examples.Signald45': 'HHG'
    };

    /* =========================================================================
       INTERNAL STATE
       ========================================================================= */
    var state = {
        siteId: null,
        gen: 0,                 // site generation — stale async replies are ignored
        loading: false,
        cells: [],
        byLabel: {}, byPrefix: {}, byNorm: {}, byNormPrefix: {},
        cellById: {},
        findMemo: {},           // assetName → cells[]  (candidate expansion memo)
        viewBox: '0 0 2000 740',
        assetValues: {},        // assetName → { alias: number }   (public / popup)
        assets: {},             // assetName → rich record (attrs / relays / freshness)
        nameById: {},           // AssetId → assetName
        zeroOffset: {},         // assetName → threshold (from WS ZeroOffsetValue)
        highlight: '',
        renderPending: false,
        dirty: {},              // cellId → cell awaiting DOM patch
        needFullRender: false,
        domCells: {},           // cellId → <g class="sip-live-cell">
        domRail: {},            // cellId → <g class="sip-rail-occ">
        svgEl: null,
        ws: null,
        wsGen: 0,
        wsReconnTimer: null,
        wsReconnCount: 0,
        wsHeartTimer: null,
        wsLastMsgAt: 0,
        msgCount: 0,
        bridgeInstalled: false,
        origPII: null,
        origPBM: null,
        diag: { recv: 0, items: 0, hits: 0, noMatch: 0, recent: [], unmatched: [], patches: 0, fullRenders: 0 },
        simNoEnrich: {},
        pmLast: {},
        lastItemAt: 0,
        _watchdog: null,
        _watchdogSiteId: null,
        _resyncTimer: null,
        _dedupTimer: null,
        _bufferedAssets: {},
        _bufferingSince: 0,
        layoutCache: {}         // siteId → { data, at }
    };

    /* ── Duplicate-message guard (bridge + own socket may deliver the same
          packet). O(1) per item; the map is swept on a timer, not per item. */
    var _seen = {};
    var _seenCount = 0;
    function dedupKey(d) {
        return (d.AssetId || d.AssetName || '') + '|' +
            (d.AssetAttributeId || d.EdgeXAttributeId || d.AssetAttributeName || '') + '|' +
            (d.TimestampDevice || d.TimestampLocal || d.TimestampEdgeX || d.TimestampChange || '') + '|' + d.Value;
    }
    function isDuplicate(d, now) {
        var k = dedupKey(d);
        var t = _seen[k];
        if (t && (now - t) < DEDUP_TTL_MS) return true;
        if (!t) _seenCount++;
        _seen[k] = now;
        if (_seenCount > 20000) sweepSeen(now);
        return false;
    }
    function sweepSeen(now) {
        now = now || Date.now();
        var next = {}, n = 0;
        for (var k in _seen) {
            if (_seen.hasOwnProperty(k) && (now - _seen[k]) < DEDUP_TTL_MS) { next[k] = _seen[k]; n++; }
        }
        _seen = next; _seenCount = n;
    }

    /* ── DOM refs ────────────────────────────────────────────────────────── */
    var canvasEl, statusEl, siteSelectEl;
    var assetClickDown = null;

    /* =========================================================================
       SMALL HELPERS
       ========================================================================= */
    function _normLabel(v) { return String(v == null ? '' : v).toUpperCase().replace(/[^A-Z0-9]+/g, ''); }
    function _normKey(v) { return String(v == null ? '' : v).toUpperCase().replace(/<[^>]*>/g, '').replace(/[^A-Z0-9]+/g, ''); }
    function zeroFloor(v) {
        if (typeof window.tlZeroFloor === 'function') { var f = window.tlZeroFloor(v); var nf = parseFloat(f); return isNaN(nf) ? NaN : nf; }
        var n = parseFloat(v);
        if (isNaN(n)) return NaN;
        return n <= 0 ? 0 : n;
    }
    function tsMs(v) {
        if (!v) return 0;
        var t = new Date(v).getTime();
        return isNaN(t) ? 0 : t;
    }
    /* 616 _aliasTokenMatch — alias as a whole token inside key (not flanked by A-Z) */
    function tokenMatch(keyNorm, aliasNorm) {
        if (!aliasNorm || !keyNorm) return false;
        var from = 0;
        while (true) {
            var idx = keyNorm.indexOf(aliasNorm, from);
            if (idx < 0) return false;
            var before = idx > 0 ? keyNorm.charAt(idx - 1) : '';
            var after = (idx + aliasNorm.length < keyNorm.length) ? keyNorm.charAt(idx + aliasNorm.length) : '';
            var beforeOk = (before === '') || (before < 'A' || before > 'Z');
            var afterOk = (after === '') || (after < 'A' || after > 'Z');
            if (beforeOk && afterOk) return true;
            from = idx + 1;
        }
    }
    function pushSample(arr, v, cap) { arr.push(v); while (arr.length > cap) arr.shift(); }
    function escHtml(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
    function hasClass(node, className) {
        return !!(node && node.classList && node.classList.contains(className));
    }

    /* =========================================================================
       SECTION 1 — INITIALISATION & HOST DETECTION
       ========================================================================= */
    function init() {
        var sipCard = document.querySelector('section.sip-card, .sip-card');
        var divTL = document.getElementById('divTelemetryLive');
        var drpSite = document.getElementById('drpSite');

        if (sipCard && drpSite) initCardMode(sipCard, drpSite);
        else if (divTL && drpSite) initIntegratedMode(divTL, drpSite);
        else initStandaloneMode();
    }

    function initStandaloneMode() {
        canvasEl = document.getElementById('sipCanvas');
        wireCanvasAssetPopupClick();
        statusEl = document.getElementById('wsStatus');
        siteSelectEl = document.getElementById('siteSelect');
        var searchEl = document.getElementById('assetSearch');
        if (siteSelectEl) siteSelectEl.addEventListener('change', function () { connectToSite(siteSelectEl.value); });
        if (searchEl) searchEl.addEventListener('input', function () { highlight(searchEl.value); });
        setStatus('Idle — select a site', '');
        renderPlaceholder('Select a site to view live SIP.');
    }

    function initIntegratedMode(divTL, drpSite) {
        siteSelectEl = drpSite;
        canvasEl = null;
        if (!drpSite._sipBound) {
            drpSite.addEventListener('change', function () {
                var sid = drpSite.value;
                if (sid && sid !== '0') activate(divTL, drpSite); else deactivate(divTL);
            });
            drpSite._sipBound = true;
        }
        setTimeout(function () {
            var sid = drpSite.value;
            if (sid && sid !== '0') activate(divTL, drpSite);
        }, 700);
    }

    function initCardMode(sipCard, drpSite) {
        siteSelectEl = drpSite;
        canvasEl = document.getElementById('sipCanvas');
        wireCanvasAssetPopupClick();
        statusEl = document.getElementById('wsStatus')
            || document.querySelector('.sip-card-head .sip-sub') || null;

        wireCardFullscreen(sipCard);

        /* telemetrylive.js drives connectToSite(sid) itself ~400 ms after a
           site change (after it opened its site-wide socket). Only bind our
           own listener when no host page is present; connectToSite()
           coalesces anyway, so a double call never double-fetches.         */
        if (!drpSite._sipBound && !hostPresent()) {
            drpSite.addEventListener('change', function () {
                var sid = drpSite.value;
                if (sid && sid !== '0') connectToSite(sid); else disconnect();
            });
            drpSite._sipBound = true;
        }
        setTimeout(function () {
            var sid = drpSite.value;
            if (sid && sid !== '0') connectToSite(sid);
        }, 700);
    }

    function activate(divTL, drpSite) {
        ['#atCardView', '#atKpiRow', '#trackCardContainer', '.at-table-scroll'].forEach(function (s) {
            var n = document.querySelector(s); if (n) n.style.display = 'none';
        });
        divTL.innerHTML =
            '<div id="sipTelWrap" style="position:relative;height:55vh;min-height:380px;' +
            'background:#08101c;border-radius:8px;overflow:hidden;">' +
            '<div style="position:absolute;top:10px;right:14px;z-index:10;display:flex;gap:8px;align-items:center;">' +
            '<span id="wsStatus" style="padding:6px 12px;border-radius:999px;font-size:12px;font-weight:600;' +
            'background:rgba(148,163,184,0.18);color:#cbd5e1;border:1px solid rgba(148,163,184,0.32);">Idle</span>' +
            '<button id="sipFsBtn" type="button" title="Fullscreen (Esc)" ' +
            'style="background:rgba(34,211,238,0.18);color:#67e8f9;border:1px solid rgba(34,211,238,0.42);' +
            'border-radius:6px;width:32px;height:32px;cursor:pointer;font-size:14px;' +
            'display:inline-flex;align-items:center;justify-content:center;">' +
            '<i class="fas fa-expand"></i></button>' +
            '</div>' +
            '<div id="sipCanvas" style="position:absolute;inset:0;overflow:hidden;"></div>' +
            '</div>';
        canvasEl = document.getElementById('sipCanvas');
        state.domCells = {}; state.domRail = {}; state.svgEl = null;
        wireCanvasAssetPopupClick();
        statusEl = document.getElementById('wsStatus');
        var wrap = document.getElementById('sipTelWrap');
        var btn = document.getElementById('sipFsBtn');
        if (btn) {
            btn.addEventListener('click', function () {
                var fs = wrap.classList.toggle('sip-tel-fs');
                wrap.style.cssText = fs
                    ? 'position:fixed;inset:0;height:100vh;width:100vw;z-index:9999;background:#08101c;'
                    : 'position:relative;height:55vh;min-height:380px;background:#08101c;border-radius:8px;overflow:hidden;';
                document.body.style.overflow = fs ? 'hidden' : '';
                var icon = btn.querySelector('i');
                if (icon) icon.className = fs ? 'fas fa-compress' : 'fas fa-expand';
            });
        }
        connectToSite(drpSite.value);
    }

    function deactivate(divTL) {
        closeSockets();
        if (divTL) {
            divTL.innerHTML = '';
            ['#atCardView', '#atKpiRow', '#trackCardContainer', '.at-table-scroll'].forEach(function (s) {
                var n = document.querySelector(s); if (n) n.style.display = '';
            });
        }
        canvasEl = statusEl = null;
        state.domCells = {}; state.domRail = {}; state.svgEl = null;
    }

    /* Fullscreen toggle for the card host. Also keeps the button icon in sync
       (telemetrylive.js expects fa-expand ⇆ fa-compress) and lets the
       browser re-layout the SVG (viewBox scales automatically).            */
    function setCardFullscreen(sipCard, on) {
        sipCard.classList.toggle('fullscreen', !!on);
        document.body.style.overflow = on ? 'hidden' : '';
        var icon = document.querySelector('#sipFullscreenBtn i');
        if (icon) icon.className = on ? 'fas fa-compress' : 'fas fa-expand';
        var btn = document.getElementById('sipFullscreenBtn');
        if (btn) btn.setAttribute('title', on ? 'Exit full screen (Esc)' : 'Full screen');
    }
    function wireCardFullscreen(sipCard) {
        var fsBtn = document.getElementById('sipFullscreenBtn');
        if (fsBtn && !fsBtn._sipFsBound) {
            fsBtn.onclick = null;
            fsBtn.addEventListener('click', function (e) {
                if (e && e.stopPropagation) e.stopPropagation();
                setCardFullscreen(sipCard, !sipCard.classList.contains('fullscreen'));
            });
            fsBtn._sipFsBound = true;
        }
        if (!document._sipEscBound) {
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && sipCard.classList.contains('fullscreen')) setCardFullscreen(sipCard, false);
            });
            document._sipEscBound = true;
        }
    }

    /* =========================================================================
       ASSET CLICK → POPUP  (delegated from #sipCanvas; cells are patched in
       place so listeners on the canvas survive every update)
       ========================================================================= */
    function wireCanvasAssetPopupClick() {
        if (!canvasEl || canvasEl._sipAssetPopupClickBound) return;
        canvasEl.addEventListener('pointerdown', onSipAssetPointerDown, true);
        canvasEl.addEventListener('pointerup', onSipAssetPointerUp, true);
        canvasEl._sipAssetPopupClickBound = true;
    }
    function onSipAssetPointerDown(evt) {
        var g = findSipLiveCellGroup(evt.target);
        if (!g) { assetClickDown = null; return; }
        assetClickDown = { x: evt.clientX, y: evt.clientY, id: g.getAttribute('data-cell-id') || '', group: g };
    }
    function onSipAssetPointerUp(evt) {
        if (!assetClickDown) return;
        var g = findSipLiveCellGroup(evt.target) || assetClickDown.group;
        if (!g) { assetClickDown = null; return; }
        var upId = g.getAttribute('data-cell-id') || '';
        var moved = Math.abs(evt.clientX - assetClickDown.x) > 6 || Math.abs(evt.clientY - assetClickDown.y) > 6;
        var sameAsset = String(upId) === String(assetClickDown.id);
        assetClickDown = null;
        if (moved || !sameAsset) return;
        var cell = state.cellById[upId] || null;
        if (!cell) { swarn('clicked SVG asset but no matching cell found:', upId); return; }
        evt.preventDefault();
        evt.stopPropagation();
        openSipAssetPopupForCell(cell, evt);
    }
    function findSipLiveCellGroup(node) {
        while (node && node !== canvasEl && node.nodeType === 1) {
            if (hasClass(node, 'sip-live-cell') && node.getAttribute('data-cell-id')) return node;
            node = node.parentNode;
        }
        return null;
    }
    function getCellLabel(cell) {
        return String((cell && cell.attrs && cell.attrs.label && cell.attrs.label.text) || '').trim();
    }
    /* Reverse lookup: which live asset name does this cell belong to? */
    function getCellAssetName(cell) {
        var label = getCellLabel(cell);
        if (!label) return '';
        if (cell._sipAsset && state.assets[cell._sipAsset]) return cell._sipAsset;
        if (LAMP_SIGNAL[cell.type] || !state.byLabel[label]) {
            var prefix = label.split(/\s+/)[0];
            if (prefix && state.assetValues[prefix]) return prefix;
        }
        if (state.assetValues[label]) return label;
        var nk = _normLabel(label);
        for (var an in state.assetValues) {
            if (state.assetValues.hasOwnProperty(an) && _normLabel(an) === nk) return an;
        }
        return label.split(/\s+/)[0] || label;
    }

    /* ═══════════════════════════════════════════════════════════════
       SELF-CONTAINED FALLBACK POPUP (used only when sip-asset-popup.js
       is not loaded). Unchanged behaviour.
       ═══════════════════════════════════════════════════════════════ */
    var _popupEl = null;
    var _popupTimer = null;
    var _popupAssetName = '';
    window.SIP_ALARM_CONFIG = window.SIP_ALARM_CONFIG || { endpoint: '', method: 'POST' };

    function sipAlarmState(html) { var grid = document.getElementById('stpGrid'); if (grid) grid.innerHTML = html; }
    function sipShowAssetLive() {
        var t = document.getElementById('stpTitle'); if (t) t.textContent = 'Live Telemetry';
        var ab = document.getElementById('stpAlarmBtn'); if (ab) ab.style.display = '';
        var lb = document.getElementById('stpLiveBtn'); if (lb) lb.style.display = 'none';
        refreshPopupGrid();
        clearInterval(_popupTimer);
        _popupTimer = setInterval(refreshPopupGrid, 3000);
    }
    function sipShowAssetAlarms() {
        clearInterval(_popupTimer);
        var t = document.getElementById('stpTitle'); if (t) t.textContent = 'Alarms — ' + _popupAssetName;
        var ab = document.getElementById('stpAlarmBtn'); if (ab) ab.style.display = 'none';
        var lb = document.getElementById('stpLiveBtn'); if (lb) lb.style.display = '';
        var cfg = window.SIP_ALARM_CONFIG || {};
        if (!cfg.endpoint) {
            sipAlarmState('<div class="stp-empty">⚠ Alarm endpoint not configured.<br><span style="font-size:11px;color:#5a6a8a;">Set window.SIP_ALARM_CONFIG.endpoint to enable live alarms.</span></div>');
            return;
        }
        sipAlarmState('<div class="stp-empty">Loading alarms…</div>');
        sipFetchAssetAlarms(_popupAssetName, function (res) {
            if (!res || !res.ok) {
                sipAlarmState('<div class="stp-empty">Unable to load alarms right now.<br><span style="font-size:11px;color:#5a6a8a;">' + escHtml((res && res.error) || 'Request failed') + '</span></div>');
                return;
            }
            var list = res.alarms || [];
            if (list.length === 0) { sipAlarmState('<div class="stp-empty">✓ No active alarms for this asset.</div>'); return; }
            var h = '';
            for (var i = 0; i < list.length; i++) {
                var a = list[i] || {};
                var sev = escHtml(String(a.Severity || a.severity || 'INFO'));
                var msg = escHtml(String(a.Message || a.message || a.Description || '—'));
                var ts = escHtml(String(a.Timestamp || a.timestamp || a.Time || ''));
                h += '<div class="stp-row"><span class="stp-lbl">' + sev + (ts ? ' · ' + ts : '') + '</span><span class="stp-val warn">' + msg + '</span></div>';
            }
            sipAlarmState(h);
        });
    }
    function sipFetchAssetAlarms(assetName, cb) {
        var cfg = window.SIP_ALARM_CONFIG || {};
        if (!cfg.endpoint) { cb({ ok: false, configured: false, error: 'not configured' }); return; }
        var method = (cfg.method || 'POST').toUpperCase();
        var opts = { method: method, headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' } };
        if (method !== 'GET') opts.body = JSON.stringify({ assetName: assetName });
        var url = cfg.endpoint + (method === 'GET' ? ((cfg.endpoint.indexOf('?') > -1 ? '&' : '?') + 'assetName=' + encodeURIComponent(assetName)) : '');
        var done = false;
        var to = setTimeout(function () { if (!done) { done = true; cb({ ok: false, configured: true, error: 'timeout' }); } }, 8000);
        fetch(url, opts).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (d) {
            if (done) return; done = true; clearTimeout(to);
            var arr = Array.isArray(d) ? d : (d && (d.alarms || d.Alarms || d.data)) || [];
            cb({ ok: true, configured: true, alarms: arr });
        }).catch(function (e) {
            if (done) return; done = true; clearTimeout(to);
            cb({ ok: false, configured: true, error: (e && e.message) || 'error' });
        });
    }

    function ensurePopupDOM() {
        if (_popupEl) return _popupEl;
        if (!document.getElementById('sipTelPopupCSS')) {
            var css = document.createElement('style');
            css.id = 'sipTelPopupCSS';
            css.textContent =
                '#sipTelPopup{position:fixed;inset:0;z-index:999999;display:none;align-items:center;justify-content:center;background:rgba(5,8,18,.72);backdrop-filter:blur(6px);font-family:"IBM Plex Sans","Plus Jakarta Sans",system-ui,sans-serif;color:#e2e8f0;}' +
                '#sipTelPopup.open{display:flex;}' +
                '#sipTelPopup *{box-sizing:border-box;margin:0;padding:0;}' +
                '.stp-box{width:960px;max-width:96vw;max-height:75vh;background:#0f1629;border:1px solid #1e2a45;border-radius:12px;box-shadow:0 4px 24px rgba(0,0,0,.4),0 0 60px rgba(0,212,255,.06);display:flex;flex-direction:column;overflow:hidden;animation:stpSlide .25s ease;}' +
                '@keyframes stpSlide{from{opacity:0;transform:translateY(18px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}' +
                '.stp-head{background:linear-gradient(135deg,#1a2744,#0f1629);padding:14px 20px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #1e2a45;}' +
                '.stp-head-left{display:flex;align-items:center;gap:12px;min-width:0;}' +
                '.stp-icon{width:36px;height:36px;border-radius:8px;background:linear-gradient(135deg,rgba(0,212,255,.15),rgba(0,229,160,.1));border:1px solid rgba(0,212,255,.25);display:flex;align-items:center;justify-content:center;flex-shrink:0;color:#00d4ff;}' +
                '.stp-icon svg{width:18px;height:18px;}' +
                '.stp-name{font-size:17px;font-weight:600;color:#fff;}' +
                '.stp-sub{font-size:12px;color:#8b9dc3;margin-top:2px;}' +
                '.stp-type{font-family:"JetBrains Mono",monospace;font-size:10px;color:#00d4ff;background:rgba(0,212,255,.08);padding:2px 8px;border-radius:4px;margin-left:6px;}' +
                '.stp-close{width:32px;height:32px;border-radius:8px;border:1px solid #1e2a45;background:rgba(255,255,255,.03);color:#8b9dc3;display:flex;align-items:center;justify-content:center;cursor:pointer;font-size:18px;}' +
                '.stp-close:hover{background:rgba(239,68,68,.15);color:#ef4444;border-color:rgba(239,68,68,.3);}' +
                '.stp-bar{padding:10px 20px;background:#0c1220;border-bottom:1px solid #1e2a45;display:flex;align-items:center;gap:8px;font-size:13px;font-weight:600;color:#00d4ff;text-transform:uppercase;letter-spacing:.06em;}' +
                '.stp-dot{width:7px;height:7px;background:#22c55e;border-radius:50%;box-shadow:0 0 6px rgba(34,197,94,.6);animation:stpPulse 1.5s infinite;}' +
                '@keyframes stpPulse{0%,100%{opacity:1}50%{opacity:.3}}' +
                '.stp-grid{display:grid;grid-template-columns:1fr 1fr;flex:1;overflow-y:auto;min-height:180px;}' +
                '.stp-col{display:flex;flex-direction:column;}' +
                '.stp-col:first-child{border-right:1px solid #1e2a45;}' +
                '.stp-row{display:flex;justify-content:space-between;align-items:center;gap:16px;padding:10px 20px;border-bottom:1px solid rgba(30,42,69,.5);transition:background .1s;}' +
                '.stp-row:hover{background:#1a2340;}' +
                '.stp-row:nth-child(even){background:#111827;}' +
                '.stp-row:nth-child(even):hover{background:#1a2340;}' +
                '.stp-lbl{font-size:13px;color:#8b9dc3;font-weight:500;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}' +
                '.stp-val{font-family:"JetBrains Mono",monospace;font-size:13px;font-weight:500;color:#e2e8f0;text-align:right;white-space:nowrap;}' +
                '.stp-val.warn{color:#f59e0b!important;}' +
                '.stp-val.ok{color:#22c55e!important;font-family:inherit!important;font-weight:600!important;}' +
                '.stp-empty{grid-column:1/-1;padding:32px 20px;color:#5a6a8a;font-size:13px;text-align:center;}' +
                '.stp-foot{padding:10px 20px;border-top:1px solid #1e2a45;display:flex;align-items:center;justify-content:space-between;background:#0c1220;font-size:11px;color:#5a6a8a;}' +
                '.stp-foot-r{display:flex;gap:8px;}' +
                '.stp-btn{font-family:inherit;font-size:12px;font-weight:500;padding:6px 16px;border-radius:4px;border:1px solid #1e2a45;background:transparent;color:#8b9dc3;cursor:pointer;display:flex;align-items:center;gap:6px;}' +
                '.stp-btn:hover{border-color:#2a3a5c;color:#e2e8f0;}' +
                '.stp-btn.pri{background:rgba(0,212,255,.1);border-color:rgba(0,212,255,.3);color:#00d4ff;}' +
                '@media(max-width:700px){.stp-grid{grid-template-columns:1fr;}.stp-col:first-child{border-right:none;}}';
            document.head.appendChild(css);
        }
        var ov = document.createElement('div');
        ov.id = 'sipTelPopup';
        ov.innerHTML =
            '<div class="stp-box">' +
            '<div class="stp-head">' +
            '<div class="stp-head-left">' +
            '<div class="stp-icon" id="stpIcon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/></svg></div>' +
            '<div><div class="stp-name" id="stpName">—</div><div class="stp-sub" id="stpSub">—</div></div>' +
            '<span class="stp-type" id="stpType">—</span>' +
            '</div>' +
            '<button class="stp-close" id="stpClose">✕</button>' +
            '</div>' +
            '<div class="stp-bar"><span class="stp-dot"></span><span id="stpTitle">Live Telemetry</span></div>' +
            '<div class="stp-grid" id="stpGrid"><div class="stp-empty">Click an asset to view live attributes</div></div>' +
            '<div class="stp-foot">' +
            '<span id="stpSync">—</span>' +
            '<div class="stp-foot-r"><button class="stp-btn" id="stpAlarmBtn">⚠ Alarms</button><button class="stp-btn pri" id="stpLiveBtn" style="display:none;">◀ Live</button><button class="stp-btn" id="stpCloseBtn">Close</button></div>' +
            '</div>' +
            '</div>';
        document.body.appendChild(ov);
        var closeBtn = document.getElementById('stpClose');
        var closeFoot = document.getElementById('stpCloseBtn');
        function doClose() { ov.classList.remove('open'); clearInterval(_popupTimer); }
        if (closeBtn) closeBtn.addEventListener('click', doClose);
        if (closeFoot) closeFoot.addEventListener('click', doClose);
        var alarmBtn = document.getElementById('stpAlarmBtn');
        var liveBtn = document.getElementById('stpLiveBtn');
        if (alarmBtn) alarmBtn.addEventListener('click', function () { sipShowAssetAlarms(); });
        if (liveBtn) liveBtn.addEventListener('click', function () { sipShowAssetLive(); });
        ov.addEventListener('click', function (e) { if (e.target === ov) doClose(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && ov.classList.contains('open')) doClose(); });
        _popupEl = ov;
        return ov;
    }

    function fmtVal(raw) {
        if (raw === null || raw === undefined || raw === '') return '--';
        var n = parseFloat(raw);
        if (isNaN(n)) return escHtml(String(raw));
        return n % 1 === 0 ? String(n) : n.toFixed(2);
    }

    function sipFindLiveAssetByName(assetName) {
        var target = String(assetName || '').trim();
        if (!target) return null;
        var hit = hostEntryByName(target);
        if (hit) return { id: String(hit.id || hit.asset.AssetId || ''), asset: hit.asset };
        if (window.bulkAssetMap) {
            for (var bid in window.bulkAssetMap) {
                if (!window.bulkAssetMap.hasOwnProperty(bid)) continue;
                var b = window.bulkAssetMap[bid];
                var bnm = String((b && (b.Name || b.AssetName)) || '').trim();
                if (bnm === target || _normLabel(bnm) === _normLabel(target)) {
                    return { id: String(bid), asset: window.wsLiveData ? window.wsLiveData[bid] : null };
                }
            }
        }
        return null;
    }
    function sipRelayDisplayValue(relay) {
        if (!relay) return '—';
        if (relay.isPickup === true || relay.IsPickup === true) return 'Pickup';
        if (relay.isPickup === false || relay.IsPickup === false) return 'Drop';
        var raw = relay.value; if (raw === undefined) raw = relay.Value;
        var n = parseFloat(raw);
        if (!isNaN(n)) return n === 1 ? 'Pickup' : 'Drop';
        return String(raw || '—');
    }
    function sipFindRelayByMeta(dlRelays, meta, fallbackKey) {
        if (!dlRelays) return null;
        var keys = [];
        if (meta) {
            keys.push(meta.name, meta.Name, meta.attributeName, meta.AttributeName,
                meta.dataloggerAttribute, meta.DataloggerAttribute, meta.role, meta.Role);
        }
        keys.push(fallbackKey);
        function norm(v) { return String(v == null ? '' : v).trim().toUpperCase(); }
        for (var i = 0; i < keys.length; i++) {
            var k = String(keys[i] || '').trim();
            if (k && dlRelays[k]) return dlRelays[k];
        }
        for (var rk in dlRelays) {
            if (!dlRelays.hasOwnProperty(rk)) continue;
            var r = dlRelays[rk] || {};
            var rn = norm(rk);
            var rd = norm(r.displayName || r.name || r.attrName || r.AttributeName);
            for (var j = 0; j < keys.length; j++) {
                var wanted = norm(keys[j]);
                if (wanted && (rn === wanted || rd === wanted)) return r;
            }
        }
        return null;
    }
    function sipBuildAliasValueMap(assetName) {
        var out = {};
        var found = sipFindLiveAssetByName(assetName);
        if (!found || !found.id) return out;
        var aid = String(found.id);
        var live = found.asset || {};
        var attrs = live.attrs || {};
        var dlRelays = live.dlRelays || {};
        var prefix = aid + '_';
        var simpleMap = window.userAssetSimpleMap || {};
        for (var sk in simpleMap) {
            if (!simpleMap.hasOwnProperty(sk) || sk.indexOf(prefix) !== 0) continue;
            var sm = simpleMap[sk] || {};
            var rawTitle = sm.attributeName || sm.AttributeName || sm.title || sm.Title || '';
            var alias = sm.AliasName || sm.aliasName || sm.name || rawTitle;
            if (!alias) continue;
            var attrObj = null;
            if (rawTitle && attrs[rawTitle]) attrObj = attrs[rawTitle];
            else if (alias && attrs[alias]) attrObj = attrs[alias];
            else if (typeof window.getStoredAttr === 'function') attrObj = window.getStoredAttr(aid, rawTitle || alias);
            out[alias] = attrObj && attrObj.Value !== undefined ? attrObj.Value : '—';
        }
        var dlMap = window.userAssetDataloggerMap || {};
        for (var dk in dlMap) {
            if (!dlMap.hasOwnProperty(dk) || dk.indexOf(prefix) !== 0) continue;
            var dm = dlMap[dk] || {};
            var dlLabel = dm.name || dm.Name || dm.dataloggerAttribute || dm.DataloggerAttribute || dm.attributeName || dm.AttributeName;
            if (!dlLabel) continue;
            var relay = sipFindRelayByMeta(dlRelays, dm, dk.substring(prefix.length));
            out[dlLabel] = sipRelayDisplayValue(relay);
        }
        for (var ak in attrs) {
            if (!attrs.hasOwnProperty(ak)) continue;
            var obj = attrs[ak];
            var val = obj && typeof obj === 'object' && 'Value' in obj ? obj.Value : obj;
            var attrId = obj ? (obj.AttrId || obj.AssetAttributeId || obj.AttributeId) : '';
            var label = resolveAttrName(ak, attrId, aid, obj && obj.DataType);
            if (!out.hasOwnProperty(label)) out[label] = val;
        }
        return out;
    }
    function refreshPopupGrid() {
        var grid = document.getElementById('stpGrid');
        if (!grid || !_popupAssetName) return;
        var vals = sipBuildAliasValueMap(_popupAssetName);
        if (!Object.keys(vals).length) vals = state.assetValues[_popupAssetName] || {};
        var keys = Object.keys(vals);
        if (window.wsAttributeNames && window.wsAttributeNames.length) {
            var ordered = [];
            window.wsAttributeNames.forEach(function (n) { if (n in vals) ordered.push(n); });
            keys.forEach(function (k) { if (ordered.indexOf(k) < 0) ordered.push(k); });
            keys = ordered;
        }
        if (!keys.length) { grid.innerHTML = '<div class="stp-empty">No live attribute values received for this asset yet.</div>'; return; }
        var half = Math.ceil(keys.length / 2);
        var renderRow = function (k) {
            var v = fmtVal(vals[k]);
            if (v === 'Pickup') return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="stp-val ok">↑ Pickup</span></div>';
            if (v === 'Drop') return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="stp-val warn">↓ Drop</span></div>';
            if (v === 'Ok' || v === 'ok') return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="stp-val ok">Ok</span></div>';
            var cls = 'stp-val';
            var num = parseFloat(v);
            if (!isNaN(num) && num < 0) cls += ' warn';
            return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="' + cls + '">' + escHtml(v) + '</span></div>';
        };
        grid.innerHTML = '<div class="stp-col">' + keys.slice(0, half).map(renderRow).join('') + '</div>' +
            '<div class="stp-col">' + keys.slice(half).map(renderRow).join('') + '</div>';
        var sync = document.getElementById('stpSync');
        if (sync) { var d = new Date(); sync.textContent = 'Last sync: ' + ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2); }
    }
    var ICON_SVG = {
        Track: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 12h4"/><path d="M14 12h4"/></svg>',
        Signal: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="6" r="3"/><circle cx="12" cy="14" r="3"/><line x1="12" y1="17" x2="12" y2="22"/></svg>',
        Point: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 20L20 4"/><circle cx="12" cy="12" r="3"/></svg>',
        Default: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>'
    };

    function openSipAssetPopupForCell(cell, evt) {
        var assetName = getCellAssetName(cell);
        var label = getCellLabel(cell);
        var rec = state.assets[assetName];
        var assetId = (rec && rec.id) || '';
        slog('SIP asset clicked:', assetName, cell.type, assetId);

        var ctx = {
            source: 'sip-telemetry', siteId: state.siteId,
            assetName: assetName, assetId: assetId, label: label,
            cellId: cell.id, cellType: cell.type, cell: cell,
            liveValues: state.assetValues[assetName] || {}
        };
        if (typeof window.SipAssetPopupLive === 'object' && typeof window.SipAssetPopupLive.open === 'function') {
            window.SipAssetPopupLive.open(cell, ctx);
            return;
        }
        if (typeof window.OpenSipAssetPopupFromCell === 'function') {
            try { if (window.OpenSipAssetPopupFromCell(cell, ctx) !== false) return; }
            catch (ex) { swarn('External popup failed, using built-in:', ex.message); }
        }

        var ov = ensurePopupDOM();
        _popupAssetName = assetName || label || cell.id || '—';
        var iconEl = document.getElementById('stpIcon');
        var t = cell.type || '';
        if (iconEl) iconEl.innerHTML = t.indexOf('Track') >= 0 ? ICON_SVG.Track : t.indexOf('Signal') >= 0 ? ICON_SVG.Signal : t.indexOf('Point') >= 0 ? ICON_SVG.Point : ICON_SVG.Default;
        var nameEl = document.getElementById('stpName'); if (nameEl) nameEl.textContent = _popupAssetName;
        var subEl = document.getElementById('stpSub'); if (subEl) subEl.textContent = 'Site: ' + (state.siteId || '—');
        var typeEl = document.getElementById('stpType'); if (typeEl) typeEl.textContent = cell.type ? cell.type.replace('examples.', '') : '—';
        var titleEl = document.getElementById('stpTitle'); if (titleEl) titleEl.textContent = 'Live Telemetry — ' + _popupAssetName;
        refreshPopupGrid();
        ov.classList.add('open');
        clearInterval(_popupTimer);
        _popupTimer = setInterval(refreshPopupGrid, 3000);
    }

    /* =========================================================================
       SECTION 2 — SITE LOAD + LAYOUT
       ========================================================================= */
    function connectToSite(rawSiteId, opts) {
        opts = opts || {};
        var siteId = parseInt(rawSiteId, 10);

        if (!siteId || isNaN(siteId)) {
            state.gen++;
            closeSockets();
            resetSiteState(null);
            setStatus('No site selected', '');
            renderPlaceholder('Select a site to view live SIP.');
            return;
        }

        /* Coalesce: same site already loaded / loading ⇒ just make sure the
           feed is up. (The card host and telemetrylive.js both call us.) */
        if (!opts.force && state.siteId === siteId && (state.loading || state.cells.length)) {
            if (!state.loading) ensureFeed(siteId);
            return;
        }

        var gen = ++state.gen;
        closeSockets();
        resetSiteState(siteId);
        state.loading = true;
        setStatus('Loading layout…', 'loading');

        var cached = !opts.force && state.layoutCache[siteId];
        if (cached && (Date.now() - cached.at) < LAYOUT_CACHE_TTL_MS) {
            onLayoutData(cached.data, siteId, gen, true);
            return;
        }
        fetchSipView(siteId, function (err, data) {
            if (gen !== state.gen) return;              // user moved on — ignore
            if (err) {
                state.loading = false;
                setStatus('Failed to load SIP', 'error');
                renderPlaceholder('Could not reach /Telemetry/GetSipView.');
                return;
            }
            onLayoutData(data, siteId, gen, false);
        });
    }

    function resetSiteState(siteId) {
        state.siteId = siteId;
        state.loading = false;
        state.cells = [];
        state.byLabel = {}; state.byPrefix = {}; state.byNorm = {}; state.byNormPrefix = {};
        state.cellById = {}; state.findMemo = {};
        state.assetValues = {};
        state.assets = {};
        state.nameById = {};
        state.zeroOffset = {};
        state.simNoEnrich = {};
        state.pmLast = {};
        state._bufferedAssets = {};
        state._bufferingSince = 0;
        state.lastItemAt = 0;
        state.msgCount = 0;
        state.dirty = {}; state.needFullRender = false;
        state.domCells = {}; state.domRail = {}; state.svgEl = null;
        state.diag.recv = state.diag.items = state.diag.hits = state.diag.noMatch = 0;
        state.diag.patches = state.diag.fullRenders = 0;
        _seen = {}; _seenCount = 0;
        _aliasMemo = {};
        clearPmBlinkTimers();
        clearWarnOnce('nomatch:');
        clearWarnOnce('sig-noattr:');
        clearWarnOnce('buffering-');
    }

    function fetchSipView(siteId, cb) {
        var url = '/Telemetry/GetSipView';
        var body = JSON.stringify({ siteId: siteId });
        if (window.jQuery && typeof window.jQuery.ajax === 'function') {
            window.jQuery.ajax({
                url: url, type: 'POST', data: body, contentType: 'application/json',
                success: function (data) { cb(null, data); },
                error: function (xhr) { cb(xhr || new Error('ajax error')); }
            });
            return;
        }
        fetch(url, { method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: body })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (d) { cb(null, d); })
            .catch(function (e) { cb(e); });
    }

    function onLayoutData(data, siteId, gen, fromCache) {
        state.loading = false;
        if (!data || !data.Id || data.Id <= 0) {
            setStatus('No SIP layout for site ' + siteId, 'warn');
            renderPlaceholder('No SIP layout saved for this site.');
            return;
        }
        var raw = data.SipView1 || data.SipView;
        if (!raw) {
            setStatus('Empty SIP payload', 'warn');
            renderPlaceholder('Site record exists but SIP payload is empty.');
            return;
        }
        var layout;
        try { layout = typeof raw === 'string' ? JSON.parse(raw) : raw; } catch (e) {
            setStatus('Cannot parse SIP JSON', 'error');
            renderPlaceholder('Saved SIP JSON is malformed.');
            return;
        }
        var cells = Array.isArray(layout) ? layout
            : (layout && Array.isArray(layout.cells)) ? layout.cells : null;
        if (!cells) {
            setStatus('Unrecognised SIP shape', 'error');
            renderPlaceholder('Saved SIP has an unrecognised shape.');
            return;
        }
        if (!fromCache) state.layoutCache[siteId] = { data: data, at: Date.now() };

        /* Deep-copy so the cached layout is never mutated by live state. */
        state.cells = JSON.parse(JSON.stringify(cells));
        var resetN = sanitizeLiveBaseline(state.cells);
        state.viewBox = autoViewBox(state.cells);
        buildIndex();
        renderAll();
        sinfo('Layout loaded' + (fromCache ? ' (cache)' : '') + ': ' + state.cells.length +
            ' cells, ' + resetN + ' editor preview state(s) cleared.');

        /* Apply everything absorbed while the layout was loading. */
        var preAssets = Object.keys(state.assets).length;
        if (preAssets) {
            var r = reevalAll(true);
            slog('Evaluated ' + preAssets + ' buffered asset(s): ' + r.matched + ' matched, ' + r.unmatched + ' unmatched.');
        }
        state._bufferedAssets = {};
        state._bufferingSince = 0;
        clearWarnOnce('buffering-');

        startAlertFlash();
        ensureFeed(siteId);
        startSharedResync();
    }

    /* Re-evaluate every known asset (layout just loaded, or shared resync). */
    function reevalAll(warnUnmatched) {
        var matched = 0, unmatched = 0;
        for (var name in state.assets) {
            if (!state.assets.hasOwnProperty(name)) continue;
            var cells = findCells(name);
            if (!cells.length) {
                unmatched++;
                if (warnUnmatched) {
                    pushSample(state.diag.unmatched, name, 20);
                    swarnOnce('nomatch:' + name, 'No SIP cell matches asset "' + name + '" — telemetry for it is ignored.');
                }
                continue;
            }
            matched++;
            evalAsset(name, cells);
        }
        flushDirty();
        return { matched: matched, unmatched: unmatched };
    }

    /* ── Shared resync: absorb host-owned wsLiveData entries that changed
          since our last pass (values that arrived before the layout, or
          through a path we do not bridge). O(entries) with a cheap stamp
          check; evaluates only assets whose host entry actually changed. */
    function resyncFromShared() {
        var ld = window.wsLiveData;
        if (!ld || !state.cells.length) return;
        var now = Date.now();
        for (var id in ld) {
            if (!ld.hasOwnProperty(id)) continue;
            var e = ld[id];
            if (!e || e.__sipMirror || !e.AssetName) continue;
            var name = String(e.AssetName).trim();
            if (!name) continue;
            var rec = assetRec(name, id, e.AssetTypeId);
            if (!syncFromHost(rec, e, now)) continue;
            var cells = findCells(name);
            if (cells.length) evalAsset(name, cells);
        }
        flushDirty();
    }
    function startSharedResync() {
        if (state._resyncTimer) clearInterval(state._resyncTimer);
        resyncFromShared();
        state._resyncTimer = setInterval(resyncFromShared, RESYNC_MS);
    }

    /* ── sanitizeLiveBaseline — clear editor preview states so nothing is
          lit/occupied until real telemetry proves it. */
    function sanitizeLiveBaseline(cells) {
        var n = 0;
        for (var i = 0; i < cells.length; i++) {
            var c = cells[i];
            if (!c || !c.type) continue;
            c.attrs = c.attrs || {};
            var touched = false;
            if (COMPOSITE_SIGNAL[c.type]) {
                if (c.attrs.lit) { c.attrs.lit = ''; touched = true; }
                if (c.attrs.signal && c.attrs.signal.lit) { c.attrs.signal.lit = ''; touched = true; }
                if (c.type === 'examples.SignalShunt') {
                    c.attrs.signal = c.attrs.signal || {};
                    if (c.attrs.signal.shuntState !== 'OFF') { c.attrs.signal.shuntState = 'OFF'; touched = true; }
                    if (c.attrs.shuntLive) { c.attrs.shuntLive = ''; touched = true; }
                }
                if (c.attrs.route && (c.attrs.route.active || c.attrs.route.activeRoutes)) {
                    c.attrs.route.active = ''; c.attrs.route.activeRoutes = ''; touched = true;
                }
            } else if (ROUTE_CALLING_TYPES[c.type]) {
                if (c.attrs.route && (c.attrs.route.active || c.attrs.route.activeRoutes)) {
                    c.attrs.route.active = ''; c.attrs.route.activeRoutes = ''; touched = true;
                }
            } else if (LAMP_SIGNAL[c.type]) {
                c.attrs.circle1 = c.attrs.circle1 || {};
                if (c.attrs.circle1.fill !== OFF_GREY) { c.attrs.circle1.fill = OFF_GREY; touched = true; }
            } else if (TRACK_STROKE[c.type]) {
                c.attrs.path = c.attrs.path || {};
                if (c.attrs.path.stroke !== OFF_GREY) { c.attrs.path.stroke = OFF_GREY; touched = true; }
            } else if (TRACK_FILL[c.type]) {
                c.attrs.path = c.attrs.path || {};
                if (c.attrs.path.fill !== OFF_GREY) { c.attrs.path.fill = OFF_GREY; touched = true; }
            } else if (PM_TYPES[c.type]) {
                c.attrs.circle1 = c.attrs.circle1 || {};
                c.attrs.body = c.attrs.body || {};
                if (c.attrs.circle1.fill !== PM_OFF) { c.attrs.circle1.fill = PM_OFF; touched = true; }
                if (c.attrs.body.stroke !== OFF_GREY) { c.attrs.body.stroke = OFF_GREY; touched = true; }
                if (c.attrs.pmBlink) { delete c.attrs.pmBlink; touched = true; }
            } else if (SHUNT_TYPES[c.type]) {
                if (c.attrs.shuntLive) { c.attrs.shuntLive = ''; touched = true; }
            }
            c._sipStale = false;
            if (touched) n++;
        }
        return n;
    }

    function autoViewBox(cells) {
        if (!cells.length) return '0 0 2000 740';
        var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (var i = 0; i < cells.length; i++) {
            var c = cells[i];
            var x = (c.position && c.position.x) || 0, y = (c.position && c.position.y) || 0;
            var w = (c.size && c.size.width) || 60, h = (c.size && c.size.height) || 60;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w; if (y + h > maxY) maxY = y + h;
        }
        var pad = 80;
        return (minX - pad) + ' ' + (minY - pad) + ' ' + (maxX - minX + 2 * pad) + ' ' + (maxY - minY + 2 * pad);
    }

    /* ── Cell index ──────────────────────────────────────────────────────
       byLabel["S13"] / byPrefix["S18"] (lamp cells "S18 RG") plus normalised
       variants, and a per-cell "clean" key (PT-/IRS/TWS/A-B suffix stripped)
       so findCells() is a handful of hash lookups, memoised per asset name. */
    function cleanPmLabel(v) {
        return String(v || '')
            .replace(/^(PT-)+/i, '')
            .replace(/\s+/g, ' ')
            .replace(/\b[AB]\s*(IRS|TWS)\b/ig, '')
            .replace(/\b(IRS|TWS)\b/ig, '')
            .replace(/[AB]$/i, '')
            .trim();
    }
    function buildIndex() {
        state.byLabel = {}; state.byPrefix = {}; state.byNorm = {}; state.byNormPrefix = {};
        state.byClean = {}; state.cellById = {}; state.findMemo = {};
        for (var i = 0; i < state.cells.length; i++) {
            var c = state.cells[i];
            if (!c) continue;
            if (c.id == null) c.id = 'sip-' + i;
            state.cellById[String(c.id)] = c;
            var raw = c.attrs && c.attrs.label && c.attrs.label.text;
            if (raw == null || raw === '') continue;
            var lbl = String(raw).trim();
            var prefix = lbl.split(/\s+/)[0];
            (state.byLabel[lbl] = state.byLabel[lbl] || []).push(c);
            var nk = _normLabel(lbl);
            if (nk) (state.byNorm[nk] = state.byNorm[nk] || []).push(c);
            var ck = _normLabel(cleanPmLabel(lbl));
            if (ck && ck !== nk) (state.byClean[ck] = state.byClean[ck] || []).push(c);
            if (!COMPOSITE_SIGNAL[c.type] && prefix && prefix !== lbl) {
                (state.byPrefix[prefix] = state.byPrefix[prefix] || []).push(c);
                var np = _normLabel(prefix);
                if (np) (state.byNormPrefix[np] = state.byNormPrefix[np] || []).push(c);
            }
        }
    }

    function findCells(assetName) {
        var raw = String(assetName || '').trim();
        if (!raw) return [];
        var memo = state.findMemo[raw];
        if (memo) return memo;

        var out = [];
        function add(list) {
            if (!list) return;
            for (var i = 0; i < list.length; i++) if (out.indexOf(list[i]) === -1) out.push(list[i]);
        }
        function addByName(name) {
            if (!name) return;
            add(state.byLabel[name]);
            add(state.byPrefix[name]);
            var nk = _normLabel(name);
            if (!nk) return;
            add(state.byNorm[nk]);
            add(state.byNormPrefix[nk]);
            add(state.byClean[nk]);
        }
        addByName(raw);
        var stripped = raw.replace(/^(PT-)+/i, '').trim();
        addByName(stripped);
        addByName('PT-' + stripped);
        var base = cleanPmLabel(stripped);
        if (base && base !== stripped) { addByName(base); addByName('PT-' + base); }

        for (var i = 0; i < out.length; i++) if (!out[i]._sipAsset) out[i]._sipAsset = raw;
        state.findMemo[raw] = out;
        return out;
    }

    /* =========================================================================
       SECTION 3 — FEED  (bridge-first; own socket only when needed)
       ========================================================================= */
    function hostPresent() {
        return typeof window.connectWebSocket === 'function' || typeof window.processItemsInternal === 'function';
    }
    function ensureFeed(siteId) {
        tryBridge();
        startFeedWatchdog(siteId);
        if (hostHasSiteWideStream(siteId)) {
            setStatus('Live (shared stream)', 'ok');
            slog('Site ' + siteId + ': host has a site-wide stream — SIP socket deferred to watchdog.');
        } else if (hostPresent()) {
            /* telemetrylive.js opens its own site-wide socket right after a
               site change; give it a moment before opening a second stream. */
            setStatus('Waiting for live stream…', 'loading');
            setTimeout(function () {
                if (state.siteId !== siteId) return;
                if (!hostHasSiteWideStream(siteId)) openOwnSocket(siteId);
                else setStatus('Live (shared stream)', 'ok');
            }, 1200);
        } else {
            openOwnSocket(siteId);
        }
    }

    /* True when telemetrylive.js holds an open/connecting socket subscribed
       to the WHOLE site (…/{siteId}/all). Asset-type filtered URLs do not
       count — they would starve the SIP of other asset types.            */
    function hostHasSiteWideStream(siteId) {
        var c = window.wsConnection;
        if (!c) return false;
        if (c.readyState !== 0 && c.readyState !== 1) return false;
        var u = String(c.url || '').split('?')[0];
        return new RegExp('/subscribe/liveValue/' + String(siteId) + '/all$').test(u);
    }

    function startFeedWatchdog(siteId) {
        stopFeedWatchdog();
        state._watchdogSiteId = siteId;
        state._watchdog = setInterval(function () {
            tryBridge();
            if (state.ws && (state.ws.readyState === 0 || state.ws.readyState === 1)) return;
            var silentMs = Date.now() - (state.lastItemAt || 0);
            if (hostHasSiteWideStream(siteId) && silentMs < WATCHDOG_SILENT_MS) return;
            if (!hostHasSiteWideStream(siteId)) slog('Host has no site-wide stream — opening SIP socket for site ' + siteId + '.');
            else swarn('No SIP telemetry for ' + Math.round(silentMs / 1000) + 's — opening dedicated site-wide socket for site ' + siteId + '.');
            openOwnSocket(siteId);
        }, WATCHDOG_TICK_MS);
    }
    function stopFeedWatchdog() {
        if (state._watchdog) { clearInterval(state._watchdog); state._watchdog = null; }
        state._watchdogSiteId = null;
    }

    /* ── Bridge: wrap telemetrylive.js entry points exactly once ─────────── */
    function tryBridge() {
        var cur = window.processItemsInternal;
        if (typeof cur !== 'function') return false;
        if (cur._sipBridged) { state.bridgeInstalled = true; return true; }
        /* Host reassigned processItemsInternal after our install ⇒ re-wrap. */
        var orig = cur;
        state.origPII = orig;
        var bridgedPII = function (items) {
            var r = orig.apply(this, arguments);
            if (state.cells.length > 0 || state.loading) feedItems(items);
            return r;
        };
        bridgedPII._sipBridged = true;
        window.processItemsInternal = bridgedPII;

        var curPBM = window.parseBatchMessages;
        if (typeof curPBM === 'function' && !curPBM._sipBridged) {
            var origPBM = curPBM;
            state.origPBM = origPBM;
            var bridgedPBM = function (messages) {
                var r = origPBM.apply(this, arguments);
                if ((state.cells.length > 0 || state.loading) && messages && messages.length) {
                    var dlItems = [];
                    for (var i = 0; i < messages.length; i++) {
                        var m = messages[i];
                        if (typeof m === 'string') { try { m = JSON.parse(m); } catch (e) { continue; } }
                        if (m && m.DataType === 'DataLogger') dlItems.push(m);
                    }
                    if (dlItems.length) feedItems(dlItems);
                }
                return r;
            };
            bridgedPBM._sipBridged = true;
            window.parseBatchMessages = bridgedPBM;
        }
        state.bridgeInstalled = true;
        return true;
    }
    function removeBridge() {
        if (!state.bridgeInstalled) return;
        if (window.processItemsInternal && window.processItemsInternal._sipBridged && typeof state.origPII === 'function')
            window.processItemsInternal = state.origPII;
        if (window.parseBatchMessages && window.parseBatchMessages._sipBridged && typeof state.origPBM === 'function')
            window.parseBatchMessages = state.origPBM;
        state.origPII = state.origPBM = null;
        state.bridgeInstalled = false;
    }

    /* ── Own WebSocket (single owner, generation-guarded) ─────────────────── */
    function openOwnSocket(siteId) {
        if (state.ws && (state.ws.readyState === 0 || state.ws.readyState === 1)) return;
        if (state.siteId !== siteId) return;
        var url = WS_BASE + '/' + siteId + '/all';
        if (window.location.protocol === 'https:' && url.indexOf('ws://') === 0) url = url.replace('ws://', 'wss://');
        var secure = (typeof window.isSecure !== 'undefined') ? window.isSecure : (window.location.protocol === 'https:');
        if (secure && window.APP_CONFIG && window.APP_CONFIG.WebSocketAuthToken) {
            url += (url.indexOf('?') > -1 ? '&' : '?') + 'token=' + encodeURIComponent(window.APP_CONFIG.WebSocketAuthToken);
        }
        sinfo('SIP socket →', url.replace(/token=[^&]+/, 'token=***'));
        setStatus('Connecting…', 'loading');

        var gen = ++state.wsGen;
        var ws;
        try { ws = new WebSocket(url); }
        catch (e) {
            console.error('[sip-telemetry] WS failed:', e);
            setStatus('Connection failed', 'error');
            schedReconn(siteId); return;
        }
        state.ws = ws;
        ws.onopen = function () {
            if (gen !== state.wsGen) return;
            state.wsReconnCount = 0;
            state.wsLastMsgAt = Date.now();
            setStatus('Live · 0 msgs', 'ok');
            startHeartbeat();
        };
        ws.onmessage = function (ev) {
            if (gen !== state.wsGen) return;
            state.wsLastMsgAt = Date.now();
            var payload;
            try {
                payload = JSON.parse(ev.data);
                if (typeof payload === 'string') payload = JSON.parse(payload);
            } catch (e) { return; }
            applyPayload(payload);
        };
        ws.onclose = function () {
            if (gen !== state.wsGen) return;
            stopHeartbeat();
            state.ws = null;
            if (state.siteId === siteId && state.wsReconnCount < MAX_RECONNECT) schedReconn(siteId);
            else setStatus('Disconnected', 'error');
        };
        ws.onerror = function (e) { if (gen === state.wsGen) swarn('ws error', e); };
    }
    function schedReconn(siteId) {
        if (state.wsReconnTimer) clearTimeout(state.wsReconnTimer);
        state.wsReconnCount++;
        setStatus('Reconnecting (' + state.wsReconnCount + '/' + MAX_RECONNECT + ')…', 'warn');
        state.wsReconnTimer = setTimeout(function () {
            state.wsReconnTimer = null;
            if (state.siteId === siteId) openOwnSocket(siteId);
        }, RECONNECT_MS);
    }
    function startHeartbeat() {
        stopHeartbeat();
        state.wsHeartTimer = setInterval(function () {
            if (Date.now() - state.wsLastMsgAt > HEARTBEAT_MS) {
                swarn('heartbeat timeout — reconnecting');
                if (state.ws) try { state.ws.close(); } catch (e) { }
            }
        }, HEARTBEAT_MS / 2);
    }
    function stopHeartbeat() {
        if (state.wsHeartTimer) { clearInterval(state.wsHeartTimer); state.wsHeartTimer = null; }
    }
    function closeSockets() {
        removeBridge();
        stopFeedWatchdog();
        stopAlertFlash();
        if (state._resyncTimer) { clearInterval(state._resyncTimer); state._resyncTimer = null; }
        if (state.wsReconnTimer) { clearTimeout(state.wsReconnTimer); state.wsReconnTimer = null; }
        stopHeartbeat();
        state.wsGen++;
        if (state.ws) {
            var ws = state.ws;
            state.ws = null;
            ws.onopen = ws.onmessage = ws.onclose = ws.onerror = null;
            try { ws.close(1000); } catch (e) { }
        }
        if (_renderRaf) { (window.cancelAnimationFrame || clearTimeout)(_renderRaf); _renderRaf = null; }
        state.renderPending = false;
        state.dirty = {};
        state.wsReconnCount = 0;
        clearPmBlinkTimers();
        _seen = {}; _seenCount = 0;
    }

    /* =========================================================================
       SECTION 4 — TELEMETRY ABSORPTION
       ========================================================================= */
    function applyPayload(payload) {
        var items = null;
        if (Array.isArray(payload)) items = payload;
        else if (payload && Array.isArray(payload.Messages)) items = payload.Messages;
        else if (payload && Array.isArray(payload.items)) items = payload.items;
        else if (payload && Array.isArray(payload.data)) items = payload.data;
        else if (payload && Array.isArray(payload.payload)) items = payload.payload;
        else if (payload && payload.AssetName) items = [payload];
        if (items && items.length) feedItems(items);
    }

    /* Per-asset record: exact keys plus a normalised-key index for O(1)
       alias-tolerant lookups by the rule engine. */
    function assetRec(name, id, typeId) {
        var r = state.assets[name];
        if (!r) {
            r = state.assets[name] = {
                name: name, id: null, typeId: null,
                attrs: {}, attrsNorm: {}, dl: {}, dlNorm: {},
                zeroOffset: null, lastAt: 0, hostStamp: null, hostScanAt: 0
            };
        }
        if (id != null && id !== '' && r.id == null) { r.id = String(id); state.nameById[r.id] = name; }
        if (typeId != null && r.typeId == null) r.typeId = typeId;
        return r;
    }
    function putAttr(rec, alias, raw, attrId, num, ts, tsLocal, hasFresh, fresh, kind, dataType, now, fromHost) {
        var obj = rec.attrs[alias];
        if (obj && obj.ts && ts && ts < obj.ts) return null;   // strictly older sample — keep newest
        if (fromHost && obj && obj.ts && !ts) return null;     // host copy without a timestamp never outranks a timed sample
        obj = {
            name: alias, raw: raw, attrId: attrId != null ? parseInt(attrId, 10) || 0 : 0,
            Value: num, ts: ts || 0, tsLocal: tsLocal || 0,
            hasFresh: !!hasFresh, fresh: hasFresh ? !!fresh : null,
            kind: kind || '', dataType: dataType || '', at: now
        };
        rec.attrs[alias] = obj;
        rec.attrsNorm[_normKey(alias)] = obj;
        if (raw && raw !== alias) rec.attrsNorm[_normKey(raw)] = obj;
        rec.lastAt = now;
        var bag = state.assetValues[rec.name] || (state.assetValues[rec.name] = {});
        setSnapshotValue(bag, alias, raw, num);
        return obj;
    }
    function putRelay(rec, name, raw, role, num, ts, now) {
        var obj = { name: name, raw: raw, role: role, value: num, isPickup: num === 1, ts: ts || 0, at: now };
        rec.dl[name] = obj;
        rec.dlNorm[_normKey(name)] = obj;
        if (raw && raw !== name) rec.dlNorm[_normKey(raw)] = obj;
        rec.lastAt = now;
        var bag = state.assetValues[rec.name] || (state.assetValues[rec.name] = {});
        setSnapshotValue(bag, name, raw, num);
        return obj;
    }
    function setSnapshotValue(snapshot, aliasName, rawName, value) {
        snapshot[aliasName] = value;
        if (rawName && rawName !== aliasName) {
            try { Object.defineProperty(snapshot, rawName, { value: value, enumerable: false, configurable: true, writable: true }); }
            catch (e) { snapshot[rawName] = value; }
        }
    }

    function feedItems(items) {
        state.diag.recv++;
        if (!items || !items.length || !state.siteId) return;
        var now = Date.now();
        state.lastItemAt = now;
        var touched = {};

        /* ── PHASE 1: ABSORB ─────────────────────────────────────────────── */
        for (var i = 0; i < items.length; i++) {
            var d = items[i];
            if (!d) continue;
            if (typeof d === 'string') {
                try { d = JSON.parse(d); } catch (e) { continue; }
                if (typeof d === 'string') { try { d = JSON.parse(d); } catch (e2) { continue; } }
            }
            if (!d || !d.AssetName) continue;
            if (isDuplicate(d, now)) continue;

            var assetName = String(d.AssetName).trim();
            var rawAttr = String(d.AssetAttributeName || d.AttributeName || '').trim();
            var assetId = d.AssetId;
            var attrId = d.AssetAttributeId || d.EdgeXAttributeId;
            var dataType = String(d.DataType || '').toLowerCase();
            var isDL = dataType === 'datalogger';

            var num = zeroFloor(d.Value);                 // 616 tlZeroFloor: ≤ 0 → 0
            if (isNaN(num)) continue;

            var attrName = isDL ? resolveDlName(d, rawAttr, attrId, assetId) : resolveAttrName(rawAttr, attrId, assetId, dataType, now);
            if (state.diag.recent.length < 30 || (state.diag.recv & 15) === 0)
                pushSample(state.diag.recent, { assetName: assetName, attr: attrName, rawAttr: rawAttr, value: d.Value }, 30);

            var rec = assetRec(assetName, assetId, d.AssetTypeId);
            var tsDev = tsMs(d.TimestampDevice || d.Timestamp);
            var tsLoc = tsMs(d.TimestampLocal || d.TimestampEdgeX);
            var rawFresh = (d.IsFresh !== undefined && d.IsFresh !== null) ? d.IsFresh : d.isFresh;
            var hasFresh = !(rawFresh === undefined || rawFresh === null);
            var fresh = (rawFresh === true || rawFresh === 'true' || rawFresh === 1 || rawFresh === '1');
            var kind = String(d.BroadcastKind || '').toLowerCase();

            if (isDL) putRelay(rec, attrName, rawAttr, d.Role, num, tsDev || tsLoc, now);
            else putAttr(rec, attrName, rawAttr, attrId, num, tsDev, tsLoc, hasFresh, fresh, kind, dataType, now);

            if (d.ZeroOffsetValue != null) {
                var zo = parseFloat(d.ZeroOffsetValue);
                if (!isNaN(zo)) { rec.zeroOffset = zo; state.zeroOffset[assetName] = zo; }
            }
            touched[assetName] = true;
            mirrorToShared(d, assetName, assetId, attrName, rawAttr, attrId, num, isDL, tsDev, hasFresh, fresh, rawFresh, kind);
        }

        /* ── LAYOUT-LOAD RACE GUARD: values are absorbed above; cells are
              evaluated as soon as the layout is in (onLayoutData). */
        if (!state.cells.length) {
            for (var bn in touched) if (touched.hasOwnProperty(bn)) state._bufferedAssets[bn] = true;
            if (!state._bufferingSince) state._bufferingSince = now;
            if (now - state._bufferingSince > 8000) {
                swarnOnce('buffering-stalled', 'SIP layout still not loaded after 8s while telemetry is streaming — check GetSipView for site ' + state.siteId + '.');
            } else if (!_warnOnceKeys['buffering-info']) {
                _warnOnceKeys['buffering-info'] = 1;
                slog('Buffering telemetry until the SIP layout finishes loading (normal on site select).');
            }
            return;
        }

        /* ── PHASE 2: EVALUATE touched assets only ───────────────────────── */
        var hit = 0, noCell = 0, n = 0;
        for (var name in touched) {
            if (!touched.hasOwnProperty(name)) continue;
            n++;
            var cells = findCells(name);
            if (!cells.length) {
                noCell++;
                pushSample(state.diag.unmatched, name, 20);
                swarnOnce('nomatch:' + name, 'No SIP cell matches asset "' + name + '" — telemetry for it is ignored. Check the cell label spelling in the SIP editor.');
                continue;
            }
            if (state.simNoEnrich[name]) delete state.simNoEnrich[name];
            else syncFromHost(state.assets[name], null, now);
            if (evalAsset(name, cells)) hit++;
        }
        state.diag.items += items.length;
        state.diag.hits += hit;
        state.diag.noMatch += noCell;

        if (hasDirty()) {
            state.msgCount++;
            setStatus('Live · ' + state.msgCount + ' upd · ' + state.diag.recv + ' msg', 'ok');
            requestRender();
        } else if (state.diag.noMatch > 10 && state.diag.hits === 0) {
            setStatus('RX ' + state.diag.recv + ' · 0 matched (check labels)', 'warn');
        }
    }

    /* Gap-fill mirror into the SHARED window.wsLiveData store (in the shape
       telemetrylive.js consumes) so the asset popup and computeSignalState can
       see SIP-only assets. Never touches a Telemetry-Live-owned entry.      */
    function mirrorToShared(d, assetName, assetId, attrName, rawAttr, attrId, num, isDL, tsDev, hasFresh, fresh, rawFresh, kind) {
        if (assetId == null || assetId === '') return;
        try {
            var ld = window.wsLiveData || (window.wsLiveData = {});
            var wid = String(assetId);
            var we = ld[wid];
            if (!we) {
                we = ld[wid] = {
                    __sipMirror: true, AssetId: assetId, AssetName: assetName,
                    AssetTypeId: (d.AssetTypeId != null ? d.AssetTypeId : 2),
                    SiteId: d.SiteId, attrs: {}, dlRelays: {}, lastUpdated: new Date()
                };
            }
            if (!we.__sipMirror) return;
            we.AssetName = assetName;
            if (d.AssetTypeId != null) we.AssetTypeId = d.AssetTypeId;
            if (isDL) {
                we.dlRelays = we.dlRelays || {};
                we.dlRelays[attrName] = { value: num, isPickup: num === 1, name: attrName, displayName: attrName, rawAttrName: rawAttr, role: d.Role, timestamp: d.TimestampDevice || d.TimestampLocal || null };
            } else {
                we.attrs = we.attrs || {};
                var ao = {
                    Value: num, AttrId: attrId, AssetAttributeId: attrId, AssetId: assetId,
                    AssetAttributeName: rawAttr, DataType: d.DataType,
                    Timestamp: d.TimestampDevice || d.TimestampLocal || null,
                    TimestampDevice: d.TimestampDevice || null, TimestampLocal: d.TimestampLocal || null,
                    TimestampEdgeX: d.TimestampEdgeX || null,
                    IsFresh: hasFresh ? fresh : null, RawIsFresh: hasFresh ? rawFresh : null, HasIsFresh: hasFresh,
                    BroadcastKind: kind
                };
                we.attrs[rawAttr] = ao;
                if (attrName && attrName !== rawAttr) we.attrs[attrName] = ao;
            }
            if (d.ZeroOffsetValue != null && !isNaN(parseFloat(d.ZeroOffsetValue))) we.ZeroOffsetValue = parseFloat(d.ZeroOffsetValue);
            we.lastUpdated = new Date();
        } catch (e) { /* best effort */ }
    }

    /* ── Attribute name resolution (memoised, TTL + map identity) ────────── */
    var _aliasMemo = {};
    var _aliasMemoRefA = null, _aliasMemoRefB = null;
    function resolveAttrName(rawName, attrId, assetId, dataType, now) {
        var mA = window.userAssetSimpleMap || null, mB = window.userAssetDataloggerMap || null;
        if (mA !== _aliasMemoRefA || mB !== _aliasMemoRefB) { _aliasMemo = {}; _aliasMemoRefA = mA; _aliasMemoRefB = mB; }
        now = now || Date.now();
        var key = assetId + '|' + attrId + '|' + rawName + '|' + dataType;
        var hit = _aliasMemo[key];
        if (hit && (now - hit.at) < ALIAS_MEMO_TTL_MS) return hit.v;
        var v = resolveAttrNameRaw(rawName, attrId, assetId, dataType);
        _aliasMemo[key] = { v: v, at: now };
        return v;
    }
    function resolveAttrNameRaw(rawName, attrId, assetId, dataType) {
        if (typeof window.tlResolveDisplayAlias === 'function') {
            var common = window.tlResolveDisplayAlias(assetId, rawName, attrId, dataType, null);
            if (common) return common;
        }
        if (String(dataType || '').toLowerCase() !== 'datalogger') {
            if (typeof window.getBulkAliasName === 'function') {
                var bulkAlias = window.getBulkAliasName(assetId, rawName, attrId);
                if (bulkAlias) return bulkAlias;
            }
            var sm = window.userAssetSimpleMap;
            if (sm && assetId != null) {
                var direct = sm[String(assetId) + '_' + String(attrId)];
                if (direct && direct.name) return direct.name;
            }
            if (typeof window.getAttrDisplayName === 'function') {
                var dn = window.getAttrDisplayName(rawName, attrId, assetId);
                if (dn) return dn;
            }
            return rawName;
        }
        var dm = window.userAssetDataloggerMap;
        if (dm) {
            var e2 = dm[String(assetId) + '_' + String(attrId)];
            if (e2 && e2.name) return e2.name;
        }
        return rawName;
    }
    /* DataLogger name — 616 order: DataloggerAttribute field → map[asset_Role]
       → map[asset_attrId] → dlRoleNameMap[Role] → raw name. Numeric-only
       names are never used as labels when a Role name exists.            */
    function resolveDlName(d, rawAttr, attrId, assetId) {
        var direct = d.DataloggerAttribute;
        if (direct && String(direct).trim()) return String(direct).trim();
        var role = (d.Role != null && d.Role !== '') ? String(d.Role) : '';
        var aid = String(assetId || '');
        var dm = window.userAssetDataloggerMap;
        if (dm) {
            var e = (role && dm[aid + '_' + role]) || dm[aid + '_' + String(attrId)] || (dm[aid + '_' + rawAttr]);
            if (e && e.name) return e.name;
        }
        if (typeof window.tlResolveDisplayAlias === 'function') {
            var common = window.tlResolveDisplayAlias(assetId, rawAttr, attrId || role, 'DataLogger', d);
            if (common && !/^\d+$/.test(common)) return common;
        }
        var rm = window.dlRoleNameMap;
        if (rm) {
            var rn = (role && rm[role]) || rm[String(attrId)];
            if (rn) return rn;
        }
        if (rawAttr && !/^\d+$/.test(rawAttr)) return rawAttr;
        return rawAttr || role || String(attrId || '');
    }

    /* ── Host entry lookup (by id, else lazy name scan at most every 3 s) ── */
    function hostEntryFor(rec, now) {
        var ld = window.wsLiveData;
        if (!ld) return null;
        if (rec.id && ld[rec.id]) return ld[rec.id];
        now = now || Date.now();
        if (now - rec.hostScanAt < 3000) return null;
        rec.hostScanAt = now;
        var nk = _normLabel(rec.name);
        for (var id in ld) {
            if (!ld.hasOwnProperty(id)) continue;
            var e = ld[id];
            if (!e || !e.AssetName) continue;
            var nm = String(e.AssetName).trim();
            if (nm === rec.name || _normLabel(nm) === nk) {
                if (rec.id == null) { rec.id = String(id); state.nameById[rec.id] = rec.name; }
                return e;
            }
        }
        return null;
    }
    /* → { id, asset } or null. Never mutates host entries. */
    function hostEntryByName(name) {
        var rec = state.assets[name];
        if (rec) { var e = hostEntryFor(rec, Date.now()); return e ? { id: rec.id, asset: e } : null; }
        var ld = window.wsLiveData; if (!ld) return null;
        var nk = _normLabel(name);
        for (var id in ld) {
            if (!ld.hasOwnProperty(id)) continue;
            var x = ld[id];
            if (x && x.AssetName && (String(x.AssetName).trim() === name || _normLabel(x.AssetName) === nk)) return { id: id, asset: x };
        }
        return null;
    }
    function hostOwns(rec) {
        var e = rec && rec.id && window.wsLiveData ? window.wsLiveData[rec.id] : null;
        return (e && !e.__sipMirror) ? e : null;
    }

    /* Copy a host-owned wsLiveData entry into our record when its
       lastUpdated stamp changed. Returns true when something was synced. */
    function syncFromHost(rec, e, now) {
        if (!rec) return false;
        e = e || hostEntryFor(rec, now);
        if (!e || e.__sipMirror) return false;
        var stamp = e.lastUpdated ? (+new Date(e.lastUpdated)) : 0;
        if (rec.hostStamp === stamp) return false;
        rec.hostStamp = stamp;
        var attrs = e.attrs;
        if (attrs) {
            for (var k in attrs) {
                if (!attrs.hasOwnProperty(k)) continue;
                var a = attrs[k];
                if (!a) continue;
                var v = zeroFloor(a.Value);
                if (isNaN(v)) continue;
                var attrId = a.AttrId || a.AssetAttributeId || a.AttributeId;
                var alias = resolveAttrName(k, attrId, rec.id, a.DataType, now);
                var hasFresh = !!(a.HasIsFresh || (a.RawIsFresh !== undefined && a.RawIsFresh !== null) || (a.IsFresh !== undefined && a.IsFresh !== null));
                var fresh = a.IsFresh === true || a.RawIsFresh === true || a.RawIsFresh === 'true' || a.RawIsFresh === 1;
                putAttr(rec, alias, k, attrId, v, tsMs(a.TimestampDevice || a.Timestamp), tsMs(a.TimestampLocal || a.TimestampEdgeX),
                    hasFresh, fresh, String(a.BroadcastKind || '').toLowerCase(), a.DataType, now, true);
            }
        }
        var dl = e.dlRelays;
        if (dl) {
            for (var dk in dl) {
                if (!dl.hasOwnProperty(dk)) continue;
                var r = dl[dk];
                if (!r) continue;
                var rv = parseFloat(r.value !== undefined ? r.value : r.Value);
                if (isNaN(rv)) rv = (r.isPickup === true || r.IsPickup === true) ? 1 : 0;
                putRelay(rec, r.displayName || r.name || dk, r.rawAttrName || r.attrName || dk, r.role, rv, tsMs(r.timestamp), now);
            }
        }
        if (e.ZeroOffsetValue != null && !isNaN(parseFloat(e.ZeroOffsetValue))) rec.zeroOffset = parseFloat(e.ZeroOffsetValue);
        return true;
    }

    /* =========================================================================
       SECTION 5 — 616 RULE ENGINE
       ========================================================================= */
    /* Threshold — same source priority as telemetrylive.js getSignalThreshold /
       getZeroOffsetForAsset: zeroOffsetCache (API) → wsLiveData.ZeroOffsetValue
       → WS item ZeroOffsetValue → RDPMS_DEFAULT_THRESHOLD.                */
    function thresholdFor(rec) {
        if (rec && rec.id) {
            var zc = window.zeroOffsetCache && window.zeroOffsetCache[rec.id];
            if (zc && zc.fetched) { var cz = parseFloat(zc.value); if (!isNaN(cz)) return cz; }
            var e = window.wsLiveData && window.wsLiveData[rec.id];
            if (e) { var z = parseFloat(e.ZeroOffsetValue); if (!isNaN(z) && z > 0) return z; }
        }
        if (rec && rec.zeroOffset != null) return rec.zeroOffset;
        return (typeof window.RDPMS_DEFAULT_THRESHOLD === 'number') ? window.RDPMS_DEFAULT_THRESHOLD : ZERO_OFFSET_DEFAULT;
    }

    var SIGNAL_MA_ALIASES = {
        RG: ['RG mA', 'ISIG RG', 'ISIG_RG', 'RG Current', 'RG I', 'R mA'],
        DG: ['DG mA', 'ISIG DG', 'ISIG_DG', 'DG Current', 'DG I', 'G mA'],
        HG: ['HG mA', 'ISIG HG', 'ISIG_HG', 'HG Current', 'HG I', 'H mA'],
        HHG: ['HHG mA', 'ISIG HHG', 'ISIG_HHG', 'HHG Current', 'HHG I']
    };
    var RELAY_ALIASES = {
        RECR: ['RECR', 'RCR', 'RR', 'RED CR', 'R E CR'],
        DECR: ['DECR', 'DCR', 'DR', 'DE CR'],
        HECR: ['HECR', 'HCR', 'HR', 'HE CR'],
        HHECR: ['HHECR', 'HHCR', 'HHR', 'HHE CR']
    };
    var _normAliasCache = {};
    function normAliases(list, key) {
        var c = _normAliasCache[key];
        if (c) return c;
        c = [];
        for (var i = 0; i < list.length; i++) c.push(_normKey(list[i]));
        _normAliasCache[key] = c;
        return c;
    }
    function attrByAliases(rec, list, key) {
        var norms = normAliases(list, key);
        for (var i = 0; i < norms.length; i++) { var a = rec.attrsNorm[norms[i]]; if (a) return a; }
        return null;
    }
    function relayPickup(rec, relayName) {
        var list = normAliases(RELAY_ALIASES[relayName] || [relayName], 'relay:' + relayName);
        for (var i = 0; i < list.length; i++) { var d = rec.dlNorm[list[i]]; if (d) return d.isPickup === true || d.value === 1; }
        for (var k in rec.dlNorm) {
            if (!rec.dlNorm.hasOwnProperty(k)) continue;
            for (var j = 0; j < list.length; j++) {
                if (tokenMatch(k, list[j])) { var r = rec.dlNorm[k]; return r.isPickup === true || r.value === 1; }
            }
        }
        /* Relays that arrived as plain numeric attrs (e.g. simulate / legacy) */
        for (var n = 0; n < list.length; n++) { var a = rec.attrsNorm[list[n]]; if (a) return a.Value >= 0.5; }
        return false;
    }
    function relayPresent(rec, relayName) {
        var list = normAliases(RELAY_ALIASES[relayName] || [relayName], 'relay:' + relayName);
        for (var i = 0; i < list.length; i++) if (rec.dlNorm[list[i]] || rec.attrsNorm[list[i]]) return true;
        for (var k in rec.dlNorm) { if (rec.dlNorm.hasOwnProperty(k)) for (var j = 0; j < list.length; j++) if (tokenMatch(k, list[j])) return true; }
        return false;
    }

    /* Display-stale (UI only): server IsFresh=false, or replay w/o IsFresh older than 3 min. */
    function attrDisplayStale(a) {
        if (!a) return false;
        if (a.hasFresh) return a.fresh !== true;
        if (a.kind === 'replay' && a.tsLocal) return (Date.now() - a.tsLocal) > REPLAY_STALE_MS;
        return false;
    }

    /* 616 computeSignalState on our own record. Returns
       { aspect: 'RED'|'DOUBLE_YELLOW'|'SINGLE_YELLOW'|'GREEN'|'INACTIVE', stale, hasLamps } */
    function computeSignalState616(rec, thr) {
        var lamps = {
            RG: attrByAliases(rec, SIGNAL_MA_ALIASES.RG, 'ma:RG'),
            DG: attrByAliases(rec, SIGNAL_MA_ALIASES.DG, 'ma:DG'),
            HG: attrByAliases(rec, SIGNAL_MA_ALIASES.HG, 'ma:HG'),
            HHG: attrByAliases(rec, SIGNAL_MA_ALIASES.HHG, 'ma:HHG')
        };
        var latestTs = 0, name;
        for (name in lamps) if (lamps[name] && lamps[name].ts > latestTs) latestTs = lamps[name].ts;

        function effectiveFresh(a) {
            if (!a) return false;
            if (a.hasFresh) return a.fresh === true;                     // server flag authoritative
            if (a.kind === 'replay') return true;                        // replay w/o IsFresh: aspect by value
            if (a.ts <= 0) return false;
            return !(latestTs > 0 && (latestTs - a.ts) > SIGNAL_TS_REL_WINDOW_MS);
        }
        var F = {}, A = {}, present = 0, staleCount = 0;
        for (name in lamps) {
            var a = lamps[name];
            F[name] = effectiveFresh(a);
            A[name] = !!(a && F[name] && a.Value > thr);
            if (a) { present++; if (attrDisplayStale(a)) staleCount++; }
        }
        var recr = relayPickup(rec, 'RECR'), decr = relayPickup(rec, 'DECR');
        var hecr = relayPickup(rec, 'HECR'), hhecr = relayPickup(rec, 'HHECR');

        var cand = [];
        /* Tier 1 — fresh RDPMS currents above threshold, most restrictive wins */
        if (A.RG) cand.push({ aspect: 'RED', p: 1, v: lamps.RG.Value });
        if (A.HHG) cand.push({ aspect: 'DOUBLE_YELLOW', p: 2, v: lamps.HHG.Value });
        if (A.HG) cand.push({ aspect: 'SINGLE_YELLOW', p: 3, v: lamps.HG.Value });
        if (A.DG) cand.push({ aspect: 'GREEN', p: 4, v: lamps.DG.Value });
        var chosen = pickBest(cand);
        /* Tier 2 — DataLogger fallback, only when its own mA is not fresh */
        if (!chosen) {
            cand = [];
            if (!F.RG && recr) cand.push({ aspect: 'RED', p: 1, v: 0 });
            if (!F.HHG && hecr && hhecr) cand.push({ aspect: 'DOUBLE_YELLOW', p: 2, v: 0 });
            if (!F.HG && hecr && !hhecr) cand.push({ aspect: 'SINGLE_YELLOW', p: 3, v: 0 });
            if (!F.DG && decr) cand.push({ aspect: 'GREEN', p: 4, v: 0 });
            chosen = pickBest(cand);
        }
        /* Tier 3 — any no-flag current above threshold (IsFresh missing only) */
        if (!chosen) {
            cand = [];
            if (lamps.RG && !lamps.RG.hasFresh && lamps.RG.Value > thr) cand.push({ aspect: 'RED', p: 1, v: lamps.RG.Value });
            if (lamps.HHG && !lamps.HHG.hasFresh && lamps.HHG.Value > thr) cand.push({ aspect: 'DOUBLE_YELLOW', p: 2, v: lamps.HHG.Value });
            if (lamps.HG && !lamps.HG.hasFresh && lamps.HG.Value > thr) cand.push({ aspect: 'SINGLE_YELLOW', p: 3, v: lamps.HG.Value });
            if (lamps.DG && !lamps.DG.hasFresh && lamps.DG.Value > thr) cand.push({ aspect: 'GREEN', p: 4, v: lamps.DG.Value });
            chosen = pickBest(cand);
        }
        var aspect = chosen ? chosen.aspect : 'INACTIVE';
        var hasRelays = relayPresent(rec, 'RECR') || relayPresent(rec, 'DECR') || relayPresent(rec, 'HECR') || relayPresent(rec, 'HHECR');
        return {
            aspect: aspect,
            hasLamps: present > 0 || hasRelays,
            /* stale mark: nothing lit and every present RDPMS lamp is display-stale */
            stale: aspect === 'INACTIVE' && present > 0 && staleCount === present
        };
    }
    function pickBest(c) {
        if (!c.length) return null;
        c.sort(function (a, b) { return (a.p - b.p) || ((b.v || 0) - (a.v || 0)); });
        return c[0];
    }

    /* Prefer the host engine when telemetrylive.js owns this asset's entry. */
    function resolveSignalState(rec, thr) {
        var e = hostOwns(rec);
        if (e && typeof window.computeSignalState === 'function') {
            try {
                var ss = (typeof window.getSignalState === 'function') ? window.getSignalState(rec.id) : window.computeSignalState(rec.id);
                if (ss && ss.aspect) {
                    var lamps = ss.lamps || {}, present = 0, stale = 0;
                    for (var k in lamps) { if (lamps.hasOwnProperty(k) && lamps[k] && lamps[k].present) { present++; if (lamps[k].staleForDisplay) stale++; } }
                    return { aspect: ss.aspect, hasLamps: present > 0 || !!(e.dlRelays && Object.keys(e.dlRelays).length), stale: ss.aspect === 'INACTIVE' && present > 0 && stale === present, source: 'host' };
                }
            } catch (ex) { /* fall through */ }
        }
        return computeSignalState616(rec, thr);
    }

    function aspectToLit(aspect, lampsStr) {
        var lamps = String(lampsStr || '').toUpperCase();
        if (aspect === 'RED') return 'R';                                    // a red aspect is never dark
        if (aspect === 'SINGLE_YELLOW') return lamps.indexOf('Y') !== -1 ? 'Y' : '';
        if (aspect === 'GREEN') return lamps.indexOf('G') !== -1 ? 'G' : '';
        if (aspect === 'DOUBLE_YELLOW') {
            var lit = '';
            if (lamps.indexOf('Y') !== -1) lit += 'Y';
            if (lamps.indexOf('X') !== -1) lit += 'X';
            return lit;
        }
        return '';
    }
    var ASPECT_TAG = { RED: 'RG', DOUBLE_YELLOW: 'HHG', SINGLE_YELLOW: 'HG', GREEN: 'DG' };

    /* ── Route / calling lamps: route mA > ZeroOffset (or relay pickup) ─── */
    function routeLabelsForCell(cell) {
        var route = (cell.attrs && cell.attrs.route) || {};
        if (cell._sipRouteLabels && cell._sipRouteSrc === route.labels) return cell._sipRouteLabels;
        var raw = route.labels || route.routes || route.routeLabels || 'AUG,BUG,CUG,DUG,EUG';
        if (Array.isArray(raw)) raw = raw.join(',');
        var seen = {}, out = [];
        String(raw || '').toUpperCase().split(/[\s,|/]+/).forEach(function (x) {
            x = String(x || '').trim();
            if (x && !seen[x]) { seen[x] = true; out.push(x); }
        });
        cell._sipRouteSrc = route.labels;
        cell._sipRouteLabels = out.length ? out : ['AUG', 'BUG', 'CUG', 'DUG'];
        return cell._sipRouteLabels;
    }
    function maFor(rec, base) {
        var b = _normKey(base);
        var a = rec.attrsNorm[b + 'MA'] || rec.attrsNorm[b + 'CURRENT'] || rec.attrsNorm[b + 'I'];
        if (a) return a;
        for (var k in rec.attrsNorm) {
            if (!rec.attrsNorm.hasOwnProperty(k)) continue;
            if (k.indexOf('MA') !== -1 && tokenMatch(k, b)) return rec.attrsNorm[k];
        }
        return null;
    }
    function usable(a, thr) { return !!(a && !(a.hasFresh && a.fresh !== true) && a.Value > thr); }
    function computeRouteCallingActive(rec, thr, cell) {
        var labels = routeLabelsForCell(cell);
        var active = [];
        for (var i = 0; i < labels.length; i++) {
            var rn = labels[i];
            if (usable(maFor(rec, rn), thr) || relayPickup(rec, rn)) active.push(rn);
        }
        var co = rec.attrsNorm['COHGMA'] || rec.attrsNorm['CALLINGMA'] || rec.attrsNorm['CMA'] || maFor(rec, 'COHG');
        var coRelay = rec.dlNorm['COHG'] || rec.dlNorm['CALLING'] || rec.dlNorm['CALL'];
        if (usable(co, thr) || (coRelay && coRelay.isPickup)) active.push('C');
        return active.join(',');
    }

    /* ── Point machine — 616 resolvePmDirectionLikeTable ────────────────── */
    function pmThreshold() {
        return (typeof window.PM_DIRECTION_THRESHOLD === 'number') ? window.PM_DIRECTION_THRESHOLD : PM_DIR_THR_DEFAULT;
    }
    function pmSide(a) {
        var id = a.attrId;
        if (PM_NWKR_IDS.indexOf(id) !== -1) return 'N';
        if (PM_RWKR_IDS.indexOf(id) !== -1) return 'R';
        var nk = _normKey(a.name);
        if (nk.indexOf('NWKR') !== -1) return 'N';
        if (nk.indexOf('RWKR') !== -1) return 'R';
        var rk = _normKey(a.raw);
        if (rk.indexOf('NWKR') !== -1) return 'N';
        if (rk.indexOf('RWKR') !== -1) return 'R';
        return '';
    }
    function computePM616(rec) {
        var thr = pmThreshold();
        var maxN = 0, maxR = 0, latN = null, latR = null, present = 0, staleCount = 0;
        for (var k in rec.attrs) {
            if (!rec.attrs.hasOwnProperty(k)) continue;
            var a = rec.attrs[k];
            var side = pmSide(a);
            if (!side) continue;
            present++;
            if (attrDisplayStale(a)) staleCount++;
            if (side === 'N') { if (a.Value > maxN) maxN = a.Value; if (!latN || a.ts > latN.ts) latN = a; }
            else { if (a.Value > maxR) maxR = a.Value; if (!latR || a.ts > latR.ts) latR = a; }
        }
        var stale = present > 0 && staleCount === present;
        /* 1. energised KR wins (pmEnergisedDirection) */
        var nEn = maxN > thr, rEn = maxR > thr;
        if (nEn && !rEn) return { pos: 'NORMAL', stale: stale, src: 'energised' };
        if (rEn && !nEn) return { pos: 'REVERSE', stale: stale, src: 'energised' };
        /* 2. both energised → freshest indication (determinePmDirectionByFreshness) */
        if (nEn && rEn) {
            var nTs = latN ? latN.ts : -1, rTs = latR ? latR.ts : -1;
            if (rTs > nTs) return { pos: (latR.Value > thr) ? 'REVERSE' : 'NORMAL', stale: stale, src: 'freshness' };
            return { pos: (latN.Value > thr) ? 'NORMAL' : 'REVERSE', stale: stale, src: 'freshness' };
        }
        /* 3. DataLogger NWKR / RWKR relay (resolvePmPosition) */
        var nw = null, rw = null;
        for (var dk in rec.dlNorm) {
            if (!rec.dlNorm.hasOwnProperty(dk)) continue;
            if (dk.indexOf('NWKR') !== -1) nw = rec.dlNorm[dk];
            else if (dk.indexOf('RWKR') !== -1) rw = rec.dlNorm[dk];
        }
        if (nw && nw.isPickup && !(rw && rw.isPickup)) return { pos: 'NORMAL', stale: stale, src: 'datalogger' };
        if (rw && rw.isPickup && !(nw && nw.isPickup)) return { pos: 'REVERSE', stale: stale, src: 'datalogger' };
        /* 4. nothing energised → neutral (no phantom direction on the schematic) */
        return { pos: 'UNKNOWN', stale: stale, src: 'none' };
    }
    /* Host delegation: pmEnergisedDirection (616 energised-KR rule) is always
       consulted for a host-owned asset; resolvePmDirectionLikeTable only when
       some indication exists locally (it defaults to 'Normal' on no data, and
       the schematic must stay neutral then). */
    function resolvePM(rec) {
        var local = computePM616(rec);
        var e = hostOwns(rec);
        if (e) {
            try {
                if (typeof window.pmEnergisedDirection === 'function') {
                    var en = window.pmEnergisedDirection(e);
                    if (en === 'Reverse' || en === 'Normal') { local.pos = (en === 'Reverse') ? 'REVERSE' : 'NORMAL'; local.src = 'host-energised'; return local; }
                }
                if (local.pos !== 'UNKNOWN' && typeof window.resolvePmDirectionLikeTable === 'function') {
                    var dir = window.resolvePmDirectionLikeTable(rec.id);
                    if (dir === 'Reverse' || dir === 'Normal') { local.pos = dir === 'Reverse' ? 'REVERSE' : 'NORMAL'; local.src = 'host-table'; }
                }
            } catch (ex) { /* keep local */ }
        }
        return local;
    }

    /* ── Track — TPR relay (pickup = clear) → TPR V → Vr / Choke bands ──── */
    var TPR_V_NORMS = ['TPRV', 'TPRVLOC', 'TPRVOLTAGE', 'VTC24DCTPRIPV', 'VTC24DCTPRIP', 'TPRVRELAY'];
    function computeOccupied616(rec) {
        var relay = rec.dlNorm['TPR'] || rec.dlNorm['TPRRELAY'] || rec.dlNorm['TPRDL'];
        if (!relay) {
            for (var k in rec.dlNorm) { if (rec.dlNorm.hasOwnProperty(k) && tokenMatch(k, 'TPR')) { relay = rec.dlNorm[k]; break; } }
        }
        if (relay) return { occ: !(relay.isPickup || relay.value >= 0.5), stale: false, src: 'relay' };
        /* relay delivered as a numeric attribute (simulate / legacy 0-1 flag) */
        var flag = rec.attrsNorm['TPR'] || rec.attrsNorm['TPRRELAY'];
        if (flag && (flag.Value === 0 || flag.Value === 1)) return { occ: flag.Value < 0.5, stale: attrDisplayStale(flag), src: 'relay-attr' };

        var tprV = null;
        for (var i = 0; i < TPR_V_NORMS.length && !tprV; i++) tprV = rec.attrsNorm[TPR_V_NORMS[i]] || null;
        if (!tprV) for (var k2 in rec.attrsNorm) { if (rec.attrsNorm.hasOwnProperty(k2) && k2.indexOf('TPR') !== -1 && /V$|VLOC$|IPV$/.test(k2)) { tprV = rec.attrsNorm[k2]; break; } }
        if (tprV) return { occ: tprV.Value < TRACK_OCC_THR, stale: attrDisplayStale(tprV), src: 'tprv' };

        var vr = rec.attrsNorm['VR'] || rec.attrsNorm['VRELAY'] || rec.attrsNorm['VTCRELAYENDV'];
        if (vr && ((vr.Value > 0.1 && vr.Value < 2.5) || vr.Value > 4.2)) return { occ: true, stale: attrDisplayStale(vr), src: 'vr' };
        var ck = rec.attrsNorm['CHOKEV'];
        if (ck && ck.Value > 1.8) return { occ: true, stale: attrDisplayStale(ck), src: 'choke' };
        return { occ: false, stale: false, src: 'none' };
    }
    function hasTrackAttrs(rec) {
        for (var k in rec.attrsNorm) if (rec.attrsNorm.hasOwnProperty(k) && (k.indexOf('TPR') !== -1 || k === 'VR' || k.indexOf('CHOKE') !== -1)) return true;
        for (var d in rec.dlNorm) if (rec.dlNorm.hasOwnProperty(d) && d.indexOf('TPR') !== -1) return true;
        return false;
    }

    /* ── Shunt — On/Off Aspect mA > threshold (ON wins), relay fallback ──── */
    function computeShuntAspect(rec, thr) {
        var on = rec.attrsNorm['ONASPECTMA'] || rec.attrsNorm['ISHSIGON'] || rec.attrsNorm['SHSIGONMA'] || maFor(rec, 'ONASPECT');
        var off = rec.attrsNorm['OFFASPECTMA'] || rec.attrsNorm['ISHSIGOFF'] || rec.attrsNorm['SHSIGOFFMA'] || maFor(rec, 'OFFASPECT');
        if (usable(on, thr)) return 'ON';
        if (usable(off, thr)) return 'OFF';
        if (!on && !off) {
            if (relayPickup(rec, 'HR')) return 'ON';
            if (rec.dlNorm['OFFECR'] && rec.dlNorm['OFFECR'].isPickup) return 'OFF';
        }
        return 'NONE';
    }

    function computeBusBar(rec) {
        var dc = rec.attrsNorm['24V'] || rec.attrsNorm['DCV'] || rec.attrsNorm['DCVOLTAGE'];
        var ac = rec.attrsNorm['110V'] || rec.attrsNorm['ACV'] || rec.attrsNorm['ACVOLTAGE'];
        var dcV = dc ? dc.Value : null, acV = ac ? ac.Value : null;
        return { dcV: dcV, acV: acV, lowDC: (dcV != null && dcV > 0 && dcV < 20), lowAC: (acV != null && acV > 0 && acV < 110) };
    }

    /* =========================================================================
       SECTION 6 — CELL RE-EVALUATION (marks dirty cells; no DOM here)
       ========================================================================= */
    var _pmBlinkTimers = {};
    function schedulePmBlinkClear(cell) {
        if (_pmBlinkTimers[cell.id]) clearTimeout(_pmBlinkTimers[cell.id]);
        _pmBlinkTimers[cell.id] = setTimeout(function () {
            delete _pmBlinkTimers[cell.id];
            if (cell.attrs && cell.attrs.pmBlink) { delete cell.attrs.pmBlink; markDirty(cell); requestRender(); }
        }, PM_BLINK_MS);
    }
    function clearPmBlinkTimers() {
        for (var k in _pmBlinkTimers) if (_pmBlinkTimers.hasOwnProperty(k)) clearTimeout(_pmBlinkTimers[k]);
        _pmBlinkTimers = {};
    }
    function markDirty(cell) { state.dirty[cell.id] = cell; }
    function hasDirty() { for (var k in state.dirty) if (state.dirty.hasOwnProperty(k)) return true; return false; }
    function setStale(cell, stale) {
        stale = !!stale;
        if (cell._sipStale !== stale) { cell._sipStale = stale; return true; }
        return false;
    }

    function evalAsset(name, cells) {
        var rec = state.assets[name];
        if (!rec) return false;
        var any = false;
        for (var i = 0; i < cells.length; i++) {
            if (reeval(cells[i], rec)) { markDirty(cells[i]); any = true; }
        }
        return any;
    }

    function reeval(cell, rec) {
        cell.attrs = cell.attrs || {};
        var name = rec.name;
        var changed = false;

        /* ── Composite signal (examples.Signal / SignalShunt) ── */
        if (COMPOSITE_SIGNAL[cell.type]) {
            var thr = thresholdFor(rec);
            var ss = resolveSignalState(rec, thr);
            var sigA = cell.attrs.signal || (cell.attrs.signal = {});
            var lamps = String(sigA.lamps || 'RYG');
            if (ss.aspect === 'INACTIVE' && !ss.hasLamps && rec.lastAt) {
                swarnOnce('sig-noattr:' + name, 'Signal "' + name + '": telemetry received but NO aspect attribute matched (expected RG/HG/HHG/DG mA or RECR/HECR/HHECR/DECR relays). Lamps held OFF.');
            }
            var newLit = aspectToLit(ss.aspect, lamps);
            var curLit = String(sigA.lit != null ? sigA.lit : (cell.attrs.lit || ''));
            if (curLit !== newLit) {
                sigA.lit = newLit;
                if (cell.type === 'examples.SignalShunt') cell.attrs.lit = newLit;
                changed = true;
                slog('Signal ' + name + ' → ' + ss.aspect + ' (lit "' + newLit + '", thr=' + thr + ')');
            }
            if (cell.type === 'examples.SignalShunt') {
                var sh = computeShuntAspect(rec, thr);
                var shLive = (sh === 'ON' || sh === 'OFF') ? sh : '';
                var shState = sh === 'ON' ? 'PROCEED' : sh === 'OFF' ? 'DIVERGE_RIGHT' : 'OFF';
                if (String(cell.attrs.shuntLive || '') !== shLive || sigA.shuntState !== shState) {
                    cell.attrs.shuntLive = shLive; sigA.shuntState = shState; changed = true;
                }
            }
            if (cell.attrs.route && (cell.attrs.route.enabled === true || String(cell.attrs.route.enabled).toLowerCase() === 'true')) {
                var newActive = computeRouteCallingActive(rec, thr, cell);
                var curActive = String(cell.attrs.route.active || cell.attrs.route.activeRoutes || '');
                if (curActive !== newActive) { cell.attrs.route.active = newActive; cell.attrs.route.activeRoutes = ''; changed = true; }
            }
            if (setStale(cell, ss.stale)) changed = true;
            return changed;
        }

        /* ── Standalone Route / Calling Signal ── */
        if (ROUTE_CALLING_TYPES[cell.type]) {
            cell.attrs.route = cell.attrs.route || {};
            var rcActive = computeRouteCallingActive(rec, thresholdFor(rec), cell);
            var rcCur = String(cell.attrs.route.active || cell.attrs.route.activeRoutes || '');
            if (rcCur !== rcActive) { cell.attrs.route.active = rcActive; cell.attrs.route.activeRoutes = ''; return true; }
            return false;
        }

        /* ── Legacy per-lamp signal cells ("S18 RG") ── */
        if (LAMP_SIGNAL[cell.type]) {
            var ss2 = resolveSignalState(rec, thresholdFor(rec));
            var lbl = getCellLabel(cell).split(/\s+/);
            var cellTag = lbl.length >= 2 ? lbl[1].toUpperCase() : LAMP_TAG[cell.type];
            var tag = ASPECT_TAG[ss2.aspect] || '';
            var shouldLit = tag && (tag === cellTag || (tag === 'HHG' && cellTag === 'HG'));
            var newFill = shouldLit ? (LAMP_COLOUR[cell.type] || C_RED) : OFF_GREY;
            cell.attrs.circle1 = cell.attrs.circle1 || {};
            if (cell.attrs.circle1.fill !== newFill) { cell.attrs.circle1.fill = newFill; changed = true; }
            if (setStale(cell, ss2.stale)) changed = true;
            return changed;
        }

        /* ── Track ── */
        if (TRACK_STROKE[cell.type] || TRACK_FILL[cell.type]) {
            var oc = computeOccupied616(rec);
            if (!oc.occ && oc.src === 'none' && rec.lastAt && !hasTrackAttrs(rec)) {
                swarnOnce('trk-noattr:' + name, 'Track "' + name + '": data received but no TPR/Vr/Choke attrs matched.');
            }
            var col = oc.occ ? C_RED : OFF_GREY;
            cell.attrs.path = cell.attrs.path || {};
            var prop = TRACK_STROKE[cell.type] ? 'stroke' : 'fill';
            if (cell.attrs.path[prop] !== col) {
                cell.attrs.path[prop] = col; changed = true;
                slog('Track ' + name + ' → ' + (oc.occ ? 'OCCUPIED' : 'CLEAR') + ' (' + oc.src + ')');
            }
            if (setStale(cell, oc.stale)) changed = true;
            return changed;
        }

        /* ── Point Machine ── */
        if (PM_TYPES[cell.type]) {
            var pm = resolvePM(rec);
            var pos = pm.pos;
            var pf = pos === 'NORMAL' ? C_GREEN : pos === 'REVERSE' ? C_YELLOW : PM_OFF;
            var bs = pos === 'REVERSE' ? C_YELLOW : OFF_GREY;
            cell.attrs.circle1 = cell.attrs.circle1 || {};
            cell.attrs.body = cell.attrs.body || {};
            var prevPos = state.pmLast[cell.id];
            if (prevPos && prevPos !== pos && (prevPos === 'NORMAL' || prevPos === 'REVERSE') && (pos === 'NORMAL' || pos === 'REVERSE')) {
                cell.attrs.pmBlink = true;
                schedulePmBlinkClear(cell);
                changed = true;
                slog('Point machine ' + name + ' OPERATED: ' + prevPos + ' → ' + pos);
            }
            if (pos === 'NORMAL' || pos === 'REVERSE') state.pmLast[cell.id] = pos;
            if (cell.attrs.circle1.fill !== pf) {
                cell.attrs.circle1.fill = pf; cell.attrs.circle1.stroke = pf; changed = true;
                slog('Point machine ' + name + ' → ' + pos + ' (' + pm.src + ')');
            }
            if (cell.attrs.body.stroke !== bs) { cell.attrs.body.stroke = bs; changed = true; }
            if (setStale(cell, pm.stale)) changed = true;
            return changed;
        }

        /* ── Shunt signal ── */
        if (SHUNT_TYPES[cell.type]) {
            var thr3 = thresholdFor(rec);
            var shAspect = computeShuntAspect(rec, thr3);
            var newState = (shAspect === 'ON' || shAspect === 'OFF') ? shAspect : '';
            if (String(cell.attrs.shuntLive || '') !== newState) { cell.attrs.shuntLive = newState; changed = true; }
            var pilot = rec.attrsNorm['PILOTMA'] || rec.attrsNorm['PILOTROOTMA'];
            cell.attrs.shunt = cell.attrs.shunt || {};
            if (pilot && cell.attrs.shunt.pilotMa !== pilot.Value) { cell.attrs.shunt.pilotMa = pilot.Value; changed = true; }
            cell.attrs.body = cell.attrs.body || {};
            if (cell.attrs.body.fill && cell.attrs.body.fill !== '#5B6168') { cell.attrs.body.fill = '#5B6168'; changed = true; }
            return changed;
        }

        /* ── Bus Bar — live voltage in label, red when low ── */
        if (BUSBAR_TYPES[cell.type]) {
            var bb = computeBusBar(rec);
            if (bb.dcV == null && bb.acV == null) return false;
            var baseName = getCellLabel(cell);
            var parts = baseName.split(' || ');
            var coreName = parts.length >= 3 ? parts[1] : parts.length === 2 ? (isNaN(parseFloat(parts[0])) ? parts[0] : parts[1]) : baseName;
            var newLabel;
            if (bb.dcV != null && bb.dcV > 0 && bb.acV != null && bb.acV > 0) newLabel = bb.dcV.toFixed(1) + ' || ' + coreName + ' || ' + bb.acV.toFixed(1) + 'v';
            else if (bb.dcV != null && bb.dcV > 0) newLabel = bb.dcV.toFixed(1) + ' || ' + coreName;
            else if (bb.acV != null && bb.acV > 0) newLabel = coreName + ' || ' + bb.acV.toFixed(1) + 'v';
            else newLabel = coreName;
            var lf = (bb.lowDC || bb.lowAC) ? C_RED : '#ffffff';
            cell.attrs.label = cell.attrs.label || {};
            if (cell.attrs.label.text !== newLabel) { cell.attrs.label.text = newLabel; changed = true; }
            if (cell.attrs.label.fill !== lf) { cell.attrs.label.fill = lf; changed = true; }
            return changed;
        }

        /* ── Axle Counter ── */
        if (AXLE_TYPES[cell.type]) {
            var ax = rec.attrsNorm['AXLEV'] || rec.attrsNorm['AXLV'] || rec.attrsNorm['COUNT'];
            var axOcc = !!(ax && ax.Value > 0.1);
            var axStroke = axOcc ? C_RED : OFF_GREY;
            cell.attrs.path = cell.attrs.path || {};
            if (cell.attrs.path.stroke !== axStroke) { cell.attrs.path.stroke = axStroke; return true; }
            return false;
        }
        if (GATE_TYPES[cell.type]) return false;
        return false;
    }

    /* =========================================================================
       SECTION 7 — RENDERING  (full build once; per-cell patches in one rAF)
       ========================================================================= */
    var _renderRaf = null;
    var SVG_STYLE =
        '.sip-live-cell,.sip-live-cell .sip-asset{cursor:pointer;pointer-events:all;}' +
        '.sip-rail-layer,.sip-stand-layer,.sip-breaker-layer,.grid{pointer-events:none;}' +
        '.sip-dim{opacity:.28;}' +
        '.sip-highlight{filter:drop-shadow(0 0 6px rgba(34,211,238,.9));}' +
        '.sip-stale{opacity:.5;}' +
        '.sip-alert-flash{animation:sipAlertPulse 1.2s ease-in-out infinite;filter:drop-shadow(0 0 8px var(--sip-alert-color,#7366ff));}' +
        '@keyframes sipAlertPulse{0%,100%{opacity:1;}50%{opacity:.55;filter:drop-shadow(0 0 2px transparent);}}';

    function cellClass(c) {
        var cls = 'sip-live-cell';
        if (state.highlight) {
            var lbl = getCellLabel(c).toLowerCase();
            cls += lbl.indexOf(state.highlight.toLowerCase()) !== -1 ? ' sip-highlight' : ' sip-dim';
        }
        if (c._sipStale) cls += ' sip-stale';
        if (c._sipAlert) cls += ' sip-alert-flash';
        return cls;
    }
    function cellStyle(c) {
        return 'cursor:pointer;pointer-events:all;' + (c._sipAlert ? '--sip-alert-color:' + c._sipAlert + ';' : '');
    }

    function requestRender() {
        if (state.renderPending) return;
        state.renderPending = true;
        var raf = window.requestAnimationFrame || function (fn) { return setTimeout(fn, 16); };
        _renderRaf = raf(function () {
            _renderRaf = null;
            state.renderPending = false;
            if (state.needFullRender || !state.svgEl || !canvasEl || !canvasEl.contains(state.svgEl)) renderAll();
            else flushDirty();
        });
    }
    function flushDirty() {
        if (!canvasEl) { state.dirty = {}; return; }
        if (!state.svgEl || !canvasEl.contains(state.svgEl)) { if (state.cells.length) renderAll(); return; }
        var dirty = state.dirty;
        state.dirty = {};
        var n = 0;
        for (var id in dirty) {
            if (!dirty.hasOwnProperty(id)) continue;
            if (patchCell(dirty[id])) n++; else { state.needFullRender = true; }
        }
        state.diag.patches += n;
        if (state.needFullRender) { state.needFullRender = false; renderAll(); }
    }
    function patchCell(c) {
        var g = state.domCells[c.id];
        if (!g) return false;
        g.innerHTML = SIP.renderCell(c);
        g.setAttribute('class', cellClass(c));
        g.setAttribute('style', cellStyle(c));
        var lbl = getCellLabel(c);
        if (g.getAttribute('data-cell-label') !== lbl) g.setAttribute('data-cell-label', lbl);
        if (RAIL_LAYER_TRACK[c.type]) patchRail(c);
        return true;
    }
    /* Rail-bed occupied overlay (drawn by SIP.renderRailLayer as a hidden
       group per track cell) — toggled without rebuilding the layer.       */
    function patchRail(c) {
        var r = state.domRail[c.id];
        if (!r) return;
        var stroke = (c.attrs && c.attrs.path && c.attrs.path.stroke) || OFF_GREY;
        var lit = typeof SIP.isLit === 'function' ? SIP.isLit(stroke) : (String(stroke).toLowerCase() === C_RED.toLowerCase());
        r.style.display = lit ? '' : 'none';
        if (lit) {
            var rects = r.querySelectorAll('[data-occ-fill]');
            for (var i = 0; i < rects.length; i++) rects[i].setAttribute('fill', stroke);
        }
    }

    function renderAll() {
        if (!canvasEl) return;
        if (!state.cells.length) { renderPlaceholder('No cells to render.'); return; }
        state.dirty = {};
        state.diag.fullRenders++;
        var railLayer = typeof SIP.renderRailLayer === 'function' ? SIP.renderRailLayer(state.cells) : '';
        var standLayer = typeof SIP.renderStandLayer === 'function' ? SIP.renderStandLayer(state.cells) : '';
        var breakerLayer = typeof SIP.renderBreakerLayer === 'function' ? SIP.renderBreakerLayer(state.cells) : '';
        var parts = new Array(state.cells.length);
        for (var i = 0; i < state.cells.length; i++) {
            var c = state.cells[i];
            parts[i] = '<g class="' + cellClass(c) + '" data-cell-id="' + escHtml(c.id) +
                '" data-cell-type="' + escHtml(c.type) + '" data-cell-label="' + escHtml(getCellLabel(c)) +
                '" style="' + cellStyle(c) + '">' + SIP.renderCell(c) + '</g>';
        }
        canvasEl.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + state.viewBox + '" class="sip-yard" ' +
            'preserveAspectRatio="xMidYMid meet" style="width:100%;height:100%;">' +
            '<defs><style>' + SVG_STYLE + '</style>' +
            '<linearGradient id="sipBg" x1="0" y1="0" x2="0" y2="1">' +
            '<stop offset="0%" stop-color="#0c1530"/><stop offset="55%" stop-color="#08101c"/><stop offset="100%" stop-color="#060a14"/>' +
            '</linearGradient></defs>' +
            '<rect width="100%" height="100%" fill="url(#sipBg)"/>' +
            railLayer + standLayer + breakerLayer + parts.join('') +
            '</svg>';
        indexDom();
    }
    function indexDom() {
        state.domCells = {}; state.domRail = {};
        state.svgEl = canvasEl ? canvasEl.querySelector('svg') : null;
        if (!state.svgEl) return;
        var gs = state.svgEl.querySelectorAll('g.sip-live-cell[data-cell-id]');
        for (var i = 0; i < gs.length; i++) state.domCells[gs[i].getAttribute('data-cell-id')] = gs[i];
        var rs = state.svgEl.querySelectorAll('[data-rail-cell]');
        for (var j = 0; j < rs.length; j++) state.domRail[rs[j].getAttribute('data-rail-cell')] = rs[j];
    }
    function renderPlaceholder(msg) {
        if (!canvasEl) return;
        state.svgEl = null; state.domCells = {}; state.domRail = {};
        canvasEl.innerHTML =
            '<div style="display:flex;align-items:center;justify-content:center;height:100%;color:#64748b;font-size:14px;font-family:system-ui;">' +
            escHtml(msg) + '</div>';
    }

    /* =========================================================================
       SECTION 8 — STATUS + HIGHLIGHT
       ========================================================================= */
    function setStatus(text, cls) {
        if (!statusEl) return;
        if (statusEl._sipText === text) return;
        statusEl._sipText = text;
        statusEl.textContent = text;
        /* keep the host's own classes (e.g. .sip-sub); only swap the ws-* state */
        var keep = String(statusEl.className || '').split(/\s+/).filter(function (c) { return c && c.indexOf('ws-') !== 0; });
        keep.push('ws-status');
        if (cls) keep.push('ws-' + cls);
        statusEl.className = keep.join(' ');
    }
    function highlight(query) {
        state.highlight = (query || '').trim();
        if (!state.svgEl) return;
        for (var i = 0; i < state.cells.length; i++) {
            var c = state.cells[i], g = state.domCells[c.id];
            if (g) g.setAttribute('class', cellClass(c));
        }
    }

    /* =========================================================================
       SECTION 9 — DIAGNOSTICS + SIMULATE
       ========================================================================= */
    function diagnose() {
        var ws = state.ws;
        var wsState = !ws ? 'NONE' : ws.readyState === 0 ? 'CONNECTING' : ws.readyState === 1 ? 'OPEN' : ws.readyState === 2 ? 'CLOSING' : 'CLOSED';
        var info = {
            mode: state.bridgeInstalled ? (state.ws ? 'bridge + own socket' : 'bridge (sharing telemetrylive.js WS)') : (state.ws ? 'standalone' : 'idle'),
            siteId: state.siteId,
            'cells loaded': state.cells.length,
            'labels': Object.keys(state.byLabel).length,
            'assets seen': Object.keys(state.assets).length,
            'WS state': wsState,
            'msgs recv': state.diag.recv,
            'items': state.diag.items,
            'hits': state.diag.hits,
            'no-match': state.diag.noMatch,
            'cell patches': state.diag.patches,
            'full renders': state.diag.fullRenders
        };
        console.log('[sip-telemetry] DIAGNOSE'); console.table(info);
        if (!state.cells.length) console.warn('No layout — select a site first.');
        if (state.diag.unmatched.length) {
            console.warn('Assets in WS not found in SIP labels:'); console.table(state.diag.unmatched);
            console.log('Known SIP labels:', Object.keys(state.byLabel).sort());
        }
        if (state.diag.recent.length) { console.log('Last 5 messages:'); console.table(state.diag.recent.slice(-5)); }
        return info;
    }

    function sipPmAttrIdForName(attrName) {
        var n = _normKey(attrName);
        if (n.indexOf('BENDNWKRLOC') > -1 || n.indexOf('BNWKRLOC') > -1) return 578;
        if (n.indexOf('BENDRWKRLOC') > -1 || n.indexOf('BRWKRLOC') > -1) return 579;
        if (n.indexOf('AENDNWKRLOC') > -1 || n.indexOf('ANWKRLOC') > -1 || n.indexOf('NWKRLOC') > -1) return 576;
        if (n.indexOf('AENDRWKRLOC') > -1 || n.indexOf('ARWKRLOC') > -1 || n.indexOf('RWKRLOC') > -1) return 577;
        if (n.indexOf('AENDNWKR') > -1 || n.indexOf('ANWKR') > -1 || n.indexOf('NWKRA') > -1) return 25;
        if (n.indexOf('AENDRWKR') > -1 || n.indexOf('ARWKR') > -1 || n.indexOf('RWKRA') > -1) return 26;
        if (n.indexOf('BENDNWKR') > -1 || n.indexOf('BNWKR') > -1 || n.indexOf('NWKRB') > -1) return 27;
        if (n.indexOf('BENDRWKR') > -1 || n.indexOf('BRWKR') > -1 || n.indexOf('RWKRB') > -1) return 28;
        if (n.indexOf('NWKR') > -1) return 25;
        if (n.indexOf('RWKR') > -1) return 26;
        return null;
    }
    function sipOppositePmAttrName(attrName) {
        if (/NWKR/i.test(attrName)) return attrName.replace(/NWKR/i, 'RWKR');
        if (/RWKR/i.test(attrName)) return attrName.replace(/RWKR/i, 'NWKR');
        return '';
    }
    /* Inject a fake telemetry message for testing (values ACCUMULATE like the
       real feed; pass reset=true to evaluate the value in isolation).
         SipTelemetry.simulate('S13','RECR',1,true)         → S13 RED
         SipTelemetry.simulate('C-18T','TPR',0,true)        → track occupied
         SipTelemetry.simulate('60','A End - NWKR',23,true) → PM normal        */
    function simulate(assetName, attrName, value, reset) {
        if (!state.cells.length) { console.warn('[sip-telemetry] No layout — select a site first.'); return; }
        assetName = String(assetName || '').trim();
        attrName = String(attrName || '').trim();
        if (!assetName || !attrName) return;
        if (reset) {
            delete state.assetValues[assetName];
            delete state.assets[assetName];
            state.simNoEnrich[assetName] = true;
        }
        var ts = new Date().toISOString();
        var isPm = /NWKR|RWKR/i.test(attrName);
        var isRelay = /^(RECR|DECR|HECR|HHECR|TPR|HR|OFFECR|NWKR|RWKR)$/i.test(attrName) && (value === 0 || value === 1);
        var items = [{
            AssetName: assetName, AssetAttributeName: attrName,
            AssetAttributeId: isPm ? sipPmAttrIdForName(attrName) : null, AssetId: 0,
            Value: value, DataType: isRelay ? 'DataLogger' : (isPm ? 'PointMachine' : 'Simulated'),
            TimestampDevice: ts, IsFresh: true, BroadcastKind: 'live'
        }];
        if (isPm && !isRelay) {
            var opp = sipOppositePmAttrName(attrName);
            if (opp) items.push({ AssetName: assetName, AssetAttributeName: opp, AssetAttributeId: sipPmAttrIdForName(opp), AssetId: 0, Value: 0, DataType: 'PointMachine', TimestampDevice: ts, IsFresh: true, BroadcastKind: 'live' });
        }
        feedItems(items);
    }

    /* =========================================================================
       SECTION 10 — ACTIVE ALERT FLASH  (class toggles, deterministic colours)
       ========================================================================= */
    var ALERT_FLASH_COLOURS = ['#7366ff', '#a927f9', '#4bacc6', '#215968', '#b75d32', '#31d0c6'];
    var alertFlashTimer = null;
    var alertFlashData = [];
    function startAlertFlash() {
        stopAlertFlash();
        fetchActiveAlerts();
        alertFlashTimer = setInterval(fetchActiveAlerts, ALERT_POLL_MS);
    }
    function stopAlertFlash() {
        if (alertFlashTimer) { clearInterval(alertFlashTimer); alertFlashTimer = null; }
        alertFlashData = [];
        applyAlertFlash();
    }
    function fetchActiveAlerts() {
        if (!state.siteId || !window.jQuery) return;
        var gen = state.gen;
        window.jQuery.ajax({
            url: '/FRS25/Telemetry/GetListActiveAlerts',
            type: 'POST',
            data: JSON.stringify({ mSmsLog: { SiteId: state.siteId, AssetTypeId: 0 } }),
            contentType: 'application/json',
            success: function (data) {
                if (gen !== state.gen) return;
                alertFlashData = (data && data.mSMSLogs) || [];
                applyAlertFlash();
            },
            error: function () { /* silent */ }
        });
    }
    function alertColourFor(name) {
        var h = 0;
        for (var i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) | 0;
        return ALERT_FLASH_COLOURS[Math.abs(h) % ALERT_FLASH_COLOURS.length];
    }
    function applyAlertFlash() {
        if (!state.cells.length) return;
        var active = {};
        for (var i = 0; i < alertFlashData.length; i++) {
            var al = alertFlashData[i];
            if (al && al.IsSmsLogActive && al.AssetName) active[String(al.AssetName).trim()] = true;
        }
        for (var j = 0; j < state.cells.length; j++) {
            var c = state.cells[j];
            var lbl = getCellLabel(c);
            var col = active[lbl] ? alertColourFor(lbl) : '';
            if ((c._sipAlert || '') !== col) { c._sipAlert = col; markDirty(c); }
        }
        if (hasDirty()) requestRender();
    }

    /* =========================================================================
       BOOT + PUBLIC API
       ========================================================================= */
    function disconnect() {
        state.gen++;
        closeSockets();
        state.siteId = null;
        state.loading = false;
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    window.SipTelemetry = {
        connectToSite: connectToSite,
        applyPayload: applyPayload,
        disconnect: disconnect,
        refresh: function () { if (state.siteId) connectToSite(state.siteId, { force: true }); },
        highlight: highlight,
        diagnose: diagnose,
        simulate: simulate,
        installBridge: tryBridge,
        removeBridge: removeBridge,
        startAlertFlash: startAlertFlash,
        stopAlertFlash: stopAlertFlash,
        resync: resyncFromShared,
        _state: state
    };
    /* telemetrylive.js FRS-Advanced view calls loadSipLayout(siteId) — no-op when same site. */
    if (typeof window.loadSipLayout !== 'function') window.loadSipLayout = function (siteId) { connectToSite(siteId); };
})();
