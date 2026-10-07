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
 *
 *  v618.0 -- smooth-vector re-skin hooks (themed background, point gap
 *  layer + patchGap) and REPLAY MODE (SipTelemetry.replay, driven by
 *  sip-replay.js): while replay is on, live items are ignored, the host
 *  (wsLiveData) is neither read nor written, and the same 616 rule engine
 *  evaluates history rows fed through feedItems(items, true).
 *
 *  v618.30 -- POST→SIGNAL BINDING (bindPostsToSignals): layouts that put the
 *  asset name on a text-only Signal Post (examples.Post/Post1) instead of on
 *  the signal shape now simulate -- each unlabelled Signal/SignalShunt/Shaunt
 *  adopts the nearest named post (type-aware, distance-capped, greedy). Shapes
 *  that carry their own label are never touched (self-label stays the override).
 *
 *  v618.31 -- CLICK/POPUP FIXES + ALL-TYPES METADATA. getCellAssetName resolves
 *  _sipAsset before the empty-label bail-out (post-bound signals now report
 *  their bound name on click), and the external popup call is try/caught so a
 *  popup error can't swallow the open. loadSipAllTypeMetadata pre-loads
 *  GetBulkAssetMetadata for EVERY asset type of the site into a standalone
 *  cache (window.sipMetaAll) so the asset popup shows AliasName-labelled rows
 *  and DataLogger relays for any asset type, regardless of the classic grid's
 *  single-type selection (sip-asset-popup.js refreshLive reads it as a
 *  fallback). The classic telemetrylive grid's single-type maps are untouched.
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
    var POST_TYPES = { 'examples.Post': 1, 'examples.Post1': 1 };  // text-only name posts
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
        view: null,             // {x,y,w,h} current zoom/pan viewBox (numeric)
        fit: null,              // {x,y,w,h} fit-to-content view (zoom baseline)
        needFit: false,         // request a re-fit on the next ensureFit()
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
        domGap: {},             // cellId -> <g data-pm-gap> (v618.0 point gap layer)
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

    /* v618.0 replay mode flag + saved live store (see SECTION 11). */
    var _replayOn = false;
    var _replaySaved = null;

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
        wireViewerInteractions();

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
        state.domCells = {}; state.domRail = {}; state.domGap = {}; state.svgEl = null;
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
                updateZoomControlsVisibility();                     // zoom controls: full screen only
                state.needFit = true; setTimeout(ensureFit, 60);   // re-fit to the new size
            });
        }
        wireViewerInteractions();
        connectToSite(drpSite.value);
    }

    function deactivate(divTL) {
        closeSockets();
        if (state._onResize) { window.removeEventListener('resize', state._onResize); state._onResize = null; }
        clearTimeout(state._fitT);
        if (divTL) {
            divTL.innerHTML = '';
            ['#atCardView', '#atKpiRow', '#trackCardContainer', '.at-table-scroll'].forEach(function (s) {
                var n = document.querySelector(s); if (n) n.style.display = '';
            });
        }
        canvasEl = statusEl = null;
        state.domCells = {}; state.domRail = {}; state.domGap = {}; state.svgEl = null;
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
        updateZoomControlsVisibility();                     // zoom controls: full screen only
        state.needFit = true; setTimeout(ensureFit, 80);   // re-fit to the new size
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
        if (_replayOn && window.SipReplay && typeof window.SipReplay.onCellClick === 'function') {
            window.SipReplay.onCellClick(cell, getCellAssetName(cell));
            renderInspector();
            return;
        }
        /* v618.18: full screen -> show the asset in the INSPECTOR panel (its
           "Details" button opens the asset popup); small card -> popup */
        if (inspectorVisible()) {
            inspectSelect(cell);
            return;
        }
        openSipAssetPopupForCell(cell, evt);
    }

    /* ======================================================================
       v618.18 INSPECTOR -- live values of the selected asset in the empty
       top-right area of the full-screen SIP. Live: values from the SIP live
       store, health from TlHealthView.classify (safe range, bar), relays.
       Replay: SipReplay.inspectorHtml() -- values at the cursor timestamp,
       small trend per reading, graph of the chosen reading.
       ====================================================================== */
    var INSP_CSS = '' +
        '.sip-insp{position:absolute;right:14px;z-index:6;width:min(360px,34%);max-height:58%;display:flex;flex-direction:column;' +
        'background:var(--in-bg);color:var(--in-text);border:1px solid var(--in-edge);border-radius:12px;box-shadow:0 10px 30px rgba(0,0,0,.18);' +
        'font:12.5px/1.35 system-ui,-apple-system,"Segoe UI",sans-serif;overflow:hidden;}' +
        '.sip-card:not(.fullscreen) .sip-insp{display:none;}' +
        '.sip-insp-bar{display:flex;align-items:center;gap:8px;padding:7px 10px;border-bottom:1px solid var(--in-edge);}' +
        '.sip-insp-bar b{flex:1;font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:var(--in-muted);}' +
        '.sip-insp-bar button{border:1px solid var(--in-edge);background:transparent;color:var(--in-text);border-radius:6px;height:24px;padding:0 8px;font:inherit;font-size:11.5px;cursor:pointer;}' +
        '.sip-insp-bar button:hover{border-color:var(--in-accent);}' +
        '.sip-insp-body{overflow:auto;padding:8px 10px 10px;}' +
        '.sip-insp-head{display:flex;align-items:baseline;gap:8px;flex-wrap:wrap;margin-bottom:6px;}' +
        '.sip-insp-head b{font-size:16px;}' +
        '.sip-insp-st{color:var(--in-muted);font-size:12px;}' +
        '.sip-insp-time{margin-left:auto;font-variant-numeric:tabular-nums;color:var(--in-muted);font-size:12px;}' +
        '.sip-insp-empty{color:var(--in-muted);padding:6px 0;}' +
        '.sip-insp-rows{display:flex;flex-direction:column;gap:1px;}' +
        '.sip-insp-row{display:grid;grid-template-columns:minmax(0,1fr) 78px 92px;align-items:center;gap:8px;padding:3px 6px;border-radius:6px;cursor:pointer;}' +
        '.sip-insp-row:hover{background:var(--in-hover);}' +
        '.sip-insp-row.on{box-shadow:inset 0 0 0 1.5px var(--in-accent);}' +
        '.sip-insp-row.st-low{background:rgba(224,161,0,.14);} .sip-insp-row.st-high{background:rgba(229,72,77,.14);}' +
        '.sip-insp-row .n{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:var(--in-muted);}' +
        '.sip-insp-row .v{text-align:right;font-weight:700;font-variant-numeric:tabular-nums;}' +
        '.sip-insp-row.st-low .v{color:#C98A00;} .sip-insp-row.st-high .v{color:#E5484D;}' +
        '.sip-insp-row svg{display:block;width:100%;height:22px;}' +
        '.sip-insp-bar2{position:relative;height:10px;}' +
        '.sip-insp-bar2 i{position:absolute;left:0;right:0;top:4px;height:2px;background:var(--in-edge);}' +
        '.sip-insp-bar2 em{display:none;}' +
        /* v618.29: same gauge as the Health view -- amber low end, green safe middle, red high end */
        '.sip-insp-bar2 i{height:4px !important;top:3px !important;border-radius:3px;background:linear-gradient(90deg,rgba(224,161,0,.55) 0%,rgba(31,157,85,.38) 22%,rgba(31,157,85,.38) 78%,rgba(229,72,77,.45) 100%) !important;}' +
        '.sip-insp-bar2 u{width:4px !important;margin-left:-2px !important;top:-1px !important;height:12px !important;}' +
        '.sip-insp-bar2 u{position:absolute;top:0;width:3px;height:10px;margin-left:-1.5px;border-radius:2px;background:var(--in-text);}' +
        '.sip-insp-row.st-low u{background:#E0A100;} .sip-insp-row.st-high u{background:#E5484D;}' +
        '.sip-insp-relays{display:flex;flex-wrap:wrap;gap:4px;margin:4px 0 6px;}' +
        '.sip-insp-relays span{font-size:11px;font-weight:700;padding:1px 7px;border-radius:4px;border:1px solid var(--in-edge);}' +
        '.sip-insp-relays .up{color:#1F9D55;border-color:rgba(31,157,85,.5);background:rgba(31,157,85,.10);} .sip-insp-relays .dn{color:#B88700;border-color:rgba(234,179,8,.6);background:rgba(234,179,8,.14);}' +
        '.sip-insp .ins-tr{stroke:var(--in-text);} .sip-insp .ins-dl{stroke:var(--in-accent);} .sip-insp .ins-cur{stroke:#F6A500;}' +
        '.sip-insp-graph{margin-top:8px;border-top:1px solid var(--in-edge);padding-top:6px;}' +
        '.sip-insp-gh,.sip-insp-gf{display:flex;justify-content:space-between;gap:8px;color:var(--in-muted);font-size:11.5px;}' +
        '.sip-insp-gh b{color:var(--in-text);}' +
        '.sip-insp-graph svg{display:block;width:100%;height:90px;background:var(--in-hover);border-radius:6px;}' +
        /* v618.19 tabs */
        '.sip-insp{width:320px;max-height:calc(100% - 20px);border-radius:12px;right:8px;}' +
        '.sip-insp .sip-insp-bar{padding:6px 8px;gap:6px;}' +
        '.sip-insp .sip-insp-bar b{font-size:14px;font-weight:800;}' +
        '.sip-insp .sip-insp-bar button{height:22px;padding:0 7px;font-size:11px;}' +
        '.sip-insp .sip-insp-body{padding:6px 8px 8px;}' +
        '.sip-insp-chips{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:5px;}' +
        '.sip-insp-list{display:flex;flex-direction:column;gap:1px;}' +
        '.sip-insp-li{display:grid;grid-template-columns:minmax(0,1fr) 66px 70px;gap:6px;align-items:center;padding:4px 6px 4px 9px;border-radius:6px;font-size:12px;position:relative;}' +
        '.sip-insp-li:nth-child(odd){background:var(--in-hover);}' +
        '.sip-insp-li::before{content:"";position:absolute;left:2px;top:5px;bottom:5px;width:3px;border-radius:2px;background:rgba(31,157,85,.55);}' +
        '.sip-insp-li.st-low::before,.sip-insp-li.st-near::before{background:#E0A100;} .sip-insp-li.st-high::before{background:#E5484D;} .sip-insp-li.st-none::before{background:var(--in-edge);}' +
        '.sip-insp-sumrow{display:flex;align-items:center;gap:8px;margin-bottom:6px;color:var(--in-text);}' +
        '.sip-insp-sumrow .sip-insp-chips{margin:0;}' +
        '.sip-insp-sect span{font-weight:600;color:var(--in-muted);margin-left:4px;}' +
        '.sip-insp-ico{width:24px;height:24px;border-radius:7px;display:inline-flex;align-items:center;justify-content:center;background:var(--in-hover);color:var(--in-accent);font-size:12px;}' +
        '.sip-insp .sip-insp-bar{background:linear-gradient(180deg,var(--in-hover),transparent);}' +
        '.sip-insp-li .n{color:var(--in-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-insp-li .v{text-align:right;font-weight:700;font-variant-numeric:tabular-nums;}' +
        '.sip-insp-li .v i{font-style:normal;font-size:9.5px;}' +
        '.sip-insp-li.st-low{background:rgba(224,161,0,.12);} .sip-insp-li.st-low .v{color:#C98A00;} .sip-insp-li.st-low u{background:#E0A100;}' +
        '.sip-insp-li.st-high{background:rgba(229,72,77,.12);} .sip-insp-li.st-high .v{color:#E5484D;} .sip-insp-li.st-high u{background:#E5484D;}' +
        '.sip-insp-li.st-near{box-shadow:inset 0 0 0 1px rgba(224,161,0,.45);} .sip-insp-li.stale{opacity:.6;}' +
        '.sip-insp-live{width:8px;height:8px;border-radius:50%;background:#22C55E;box-shadow:0 0 0 3px rgba(34,197,94,.25);animation:sipInspPulse 2s infinite;}' +
        '@keyframes sipInspPulse{50%{box-shadow:0 0 0 6px rgba(34,197,94,0);}}' +
        '.sip-insp-hero{display:flex;align-items:flex-start;gap:10px;margin-bottom:8px;}' +
        '.sip-insp-hero .nm{font-size:22px;font-weight:800;letter-spacing:.02em;}' +
        '.sip-insp-hero .sub{display:flex;flex-wrap:wrap;gap:6px;margin-top:4px;}' +
        '.sip-insp-chip{display:inline-flex;align-items:center;font-size:11.5px;font-weight:700;padding:2px 8px;border-radius:999px;border:1px solid var(--in-edge);color:var(--in-muted);}' +
        '.sip-insp-chip.ok{color:#1F9D55;border-color:rgba(31,157,85,.45);background:rgba(31,157,85,.08);}' +
        '.sip-insp-chip.bad{color:#E5484D;border-color:rgba(229,72,77,.45);background:rgba(229,72,77,.08);}' +
        '.sip-insp-chip.warn{color:#B88700;border-color:rgba(234,179,8,.55);background:rgba(234,179,8,.10);}' +
        '.sip-insp-kpis4{display:grid;grid-template-columns:repeat(4,1fr);gap:6px;margin-bottom:8px;}' +
        '.sip-insp-kpis4 div{border:1px solid var(--in-edge);border-radius:10px;padding:6px 8px;}' +
        '.sip-insp-kpis4 b{display:block;font-size:18px;} .sip-insp-kpis4 span{font-size:11px;color:var(--in-muted);}' +
        '.sip-insp-kpis4 .bad b{color:#E5484D;} .sip-insp-kpis4 .warn b{color:#B88700;}' +
        '.sip-insp-relays.big span{font-size:12px;padding:3px 9px;}' +
        '.sip-insp-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:6px;}' +
        '.sip-insp-tile{border:1px solid var(--in-edge);border-radius:10px;padding:7px 9px 6px;}' +
        '.sip-insp-tile .lb{font-size:11.5px;color:var(--in-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-insp-tile .vl{font-size:19px;font-weight:800;font-variant-numeric:tabular-nums;margin:1px 0 3px;display:flex;align-items:baseline;gap:6px;}' +
        '.sip-insp-tile .vl i{font-style:normal;font-size:10.5px;font-weight:700;}' +
        '.sip-insp-tile .rg{font-size:10.5px;color:var(--in-muted);margin-top:2px;}' +
        '.sip-insp-tile.st-low{border-color:rgba(224,161,0,.6);background:rgba(224,161,0,.08);} .sip-insp-tile.st-low .vl{color:#C98A00;}' +
        '.sip-insp-tile.st-high{border-color:rgba(229,72,77,.6);background:rgba(229,72,77,.08);} .sip-insp-tile.st-high .vl{color:#E5484D;}' +
        '.sip-insp-tile.st-near{border-color:rgba(224,161,0,.45);} .sip-insp-tile.stale{opacity:.6;}' +
        '.sip-insp-tile.st-low u{background:#E0A100;} .sip-insp-tile.st-high u{background:#E5484D;}' +
        '.sip-insp-allhead{display:flex;align-items:baseline;gap:8px;margin-bottom:6px;} .sip-insp-allhead b{font-size:15px;} .sip-insp-allhead span{color:var(--in-muted);font-size:12px;}' +
        '.sip-insp-cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:6px;}' +
        '.sip-insp-card{border:1px solid var(--in-edge);border-radius:10px;padding:2px 4px 5px;}' +
        '.sip-insp-card.bad{border-color:rgba(229,72,77,.5);}' +
        '.sip-insp-mini{display:flex;justify-content:space-between;gap:8px;padding:1px 6px;font-size:12px;border-radius:4px;}' +
        '.sip-insp-mini .n{color:var(--in-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;} .sip-insp-mini .v{font-weight:700;font-variant-numeric:tabular-nums;}' +
        '.sip-insp-mini.st-low{background:rgba(224,161,0,.12);} .sip-insp-mini.st-low .v{color:#C98A00;}' +
        '.sip-insp-mini.st-high{background:rgba(229,72,77,.12);} .sip-insp-mini.st-high .v{color:#E5484D;}' +
        '.sip-insp-bar b{color:var(--in-text);font-size:14px;letter-spacing:.02em;text-transform:none;}' +
        '.sip-insp-upd{color:var(--in-muted);font-size:11px;font-variant-numeric:tabular-nums;}' +
        '.sip-insp-tabs{display:flex;gap:2px;padding:4px 6px 0;border-bottom:1px solid var(--in-edge);overflow-x:auto;}' +
        '.sip-insp-tabs button{border:none;background:transparent;color:var(--in-muted);font:inherit;font-size:12px;font-weight:600;padding:6px 9px;cursor:pointer;border-bottom:2px solid transparent;white-space:nowrap;}' +
        '.sip-insp-tabs button[aria-selected="true"]{color:var(--in-text);border-bottom-color:var(--in-accent);}' +
        '.sip-insp-tools{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:6px;}' +
        '.sip-insp-tools button,.sip-insp-tools input,.sip-insp-chips button{border:1px solid var(--in-edge);background:transparent;color:var(--in-text);border-radius:6px;height:26px;padding:0 8px;font:inherit;font-size:11.5px;cursor:pointer;}' +
        '.sip-insp-tools button[aria-pressed="true"],.sip-insp-chips button[aria-pressed="true"]{background:var(--in-accent);border-color:var(--in-accent);color:#04161A;font-weight:600;}' +
        '.sip-insp-chips{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:6px;}' +
        '.sip-insp-graph svg{height:150px;}' +
        '.sip-insp-row .v i{font-style:normal;font-size:10px;}' +
        '.sip-insp-sum{font-size:12px;font-weight:600;margin:2px 0 6px;} .sip-insp-sum.ok{color:#1F9D55;} .sip-insp-sum.bad{color:#E5484D;}' +
        '.sip-insp-kpis{display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-bottom:8px;}' +
        '.sip-insp-kpis .k{border:1px solid var(--in-edge);border-radius:8px;padding:6px 10px;display:flex;align-items:baseline;gap:8px;}' +
        '.sip-insp-kpis b{font-size:22px;} .sip-insp-kpis .p b{color:#E0A100;} .sip-insp-kpis .f b{color:#E5484D;}' +
        '.sip-insp-kpis span{color:var(--in-muted);font-size:12px;}' +
        '.sip-insp-sect{font-size:11px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;color:var(--in-muted);margin:8px 0 4px;}' +
        '.sip-insp-cause{display:grid;grid-template-columns:minmax(0,1.3fr) 1fr 34px;gap:8px;align-items:center;padding:2px 0;}' +
        '.sip-insp-cause .n{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-insp-cause .b{height:8px;border-radius:4px;background:var(--in-hover);overflow:hidden;}' +
        '.sip-insp-cause .b i{display:block;height:100%;border-radius:4px;} .sip-insp-cause .b i.f{background:#E5484D;} .sip-insp-cause .b i.p{background:#E0A100;}' +
        '.sip-insp-cause .c{text-align:right;font-weight:700;font-variant-numeric:tabular-nums;}' +
        '.sip-insp-tl{list-style:none;margin:0;padding:0;}' +
        '.sip-insp-tl li{display:grid;grid-template-columns:64px minmax(0,1fr) auto;gap:8px;align-items:center;padding:5px 2px 5px 8px;border-bottom:1px solid var(--in-edge);box-shadow:inset 3px 0 0 var(--in-edge);}' +
        '.sip-insp-tl li.act{box-shadow:inset 3px 0 0 #E5484D;} .sip-insp-tl li.clr{box-shadow:inset 3px 0 0 #1F9D55;} .sip-insp-tl li.mnt{box-shadow:inset 3px 0 0 #E0A100;}' +
        '.sip-insp-tl time{color:var(--in-muted);font-variant-numeric:tabular-nums;font-size:12px;}' +
        '.sip-insp-tl small{color:var(--in-muted);}' +
        '.sip-insp-tl em{font-style:normal;font-size:11px;font-weight:700;padding:1px 7px;border-radius:4px;border:1px solid var(--in-edge);}' +
        '.sip-insp-tl li.act em{color:#E5484D;border-color:rgba(229,72,77,.5);} .sip-insp-tl li.clr em{color:#1F9D55;border-color:rgba(31,157,85,.5);} .sip-insp-tl li.mnt em{color:#C98A00;border-color:rgba(224,161,0,.5);}' +
        '.sip-insp-banner{border-radius:8px;padding:8px 10px;font-weight:700;margin-bottom:6px;}' +
        '.sip-insp-banner.ok{background:rgba(31,157,85,.12);color:#1F9D55;} .sip-insp-banner.bad{background:rgba(229,72,77,.12);color:#E5484D;}' +
        /* v618.20 all-assets list + Pickup / Drop */
        '.sip-insp-asset{border:1px solid var(--in-edge);border-radius:8px;padding:2px 4px 4px;margin-bottom:6px;}' +
        '.sip-insp-ah{display:flex;align-items:center;gap:8px;width:100%;border:none;background:transparent;color:var(--in-text);font:inherit;padding:5px 4px;cursor:pointer;text-align:left;}' +
        '.sip-insp-ah b{font-size:13px;} .sip-insp-ah span{color:var(--in-muted);font-size:11.5px;} .sip-insp-ah i{margin-left:auto;font-style:normal;color:var(--in-muted);font-size:10px;}' +
        '.sip-insp-ah:hover b{color:var(--in-accent);}' +
        '.sip-insp-row.sm{padding:1px 6px;grid-template-columns:minmax(0,1fr) 78px 84px;}' +
        '.sip-insp-row.sm svg{height:18px;}' +
        '.sip-insp .rp-pk{color:#1F9D55;font-weight:700;} .sip-insp .rp-dr{color:#B88700;font-weight:700;}';
    var insp = { name: null, cellId: null, min: false, timer: null, pressing: false, tab: 'live', cache: {}, frame: '', graphCol: null, graphHours: 6 };
    /* keep the yard visible: the canvas gives up the panel's width on the
       right while the panel is open (the SVG re-fits to the narrower box) */
    function inspReserve(on) {
        /* v618.24: the yard is NOT shifted any more (panel floats, draggable) */
        on = false;
        var canvas = document.getElementById('sipCanvas');
        if (!canvas) return;
        var want = on ? '336px' : '';
        if (canvas.style.marginRight !== want) {
            canvas.style.marginRight = want;
            try { window.dispatchEvent(new Event('resize')); } catch (e) { /* old browser */ }
        }
    }
    function inspectorVisible() {
        var card = document.getElementById('sipCardSection');
        return !!(card && card.classList.contains('fullscreen'));
    }
    function inspectSelect(cell) {
        var nm = getCellAssetName(cell);
        if (nm !== insp.name) { insp.graphCol = null; insp.frame = ''; }
        insp.name = nm;
        insp.cellId = cell.id;
        insp.min = false;
        if (typeof highlight === 'function') highlight(insp.name, true);
        if (window.TlHealthView && typeof window.TlHealthView.ensureRanges === 'function') {
            try { window.TlHealthView.ensureRanges(); } catch (e) { /* optional */ }
        }
        renderInspector();
    }
    function ensureInspector() {
        var canvas = document.getElementById('sipCanvas');
        if (!canvas || !canvas.parentNode) return null;
        var host = canvas.parentNode;
        if (window.getComputedStyle(host).position === 'static') host.style.position = 'relative';
        if (!document.getElementById('sipInspCss')) {
            var st = document.createElement('style');
            st.id = 'sipInspCss';
            st.textContent = INSP_CSS;
            document.head.appendChild(st);
        }
        var el = document.getElementById('sipInspector');
        if (!el) {
            el = document.createElement('aside');
            el.id = 'sipInspector';
            el.className = 'sip-insp';
            el.setAttribute('aria-label', 'Asset values');
            host.appendChild(el);
            /* never rebuild between pointerdown and click (the click would be lost) */
            el.addEventListener('pointerdown', function () { insp.pressing = true; });
            /* v618.24: drag by the title bar to move the panel off any part of the yard */
            el.addEventListener('pointerdown', function (e) {
                if (!e.target.closest || !e.target.closest('.sip-insp-bar') || e.target.closest('button')) return;
                var r0 = el.getBoundingClientRect(), p0 = el.offsetParent ? el.offsetParent.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
                var dx = e.clientX - r0.left, dy = e.clientY - r0.top;
                function mv(ev) {
                    var x = Math.max(0, Math.min(p0.width - r0.width, ev.clientX - p0.left - dx));
                    var y = Math.max(0, Math.min(p0.height - 40, ev.clientY - p0.top - dy));
                    insp.pos = { x: x, y: y };
                    el.style.left = x + 'px';
                    el.style.top = y + 'px';
                    el.style.right = 'auto';
                }
                function up() { document.removeEventListener('pointermove', mv); document.removeEventListener('pointerup', up); }
                document.addEventListener('pointermove', mv);
                document.addEventListener('pointerup', up);
                e.preventDefault();
            });
            document.addEventListener('pointerup', function () { setTimeout(function () { insp.pressing = false; }, 0); }, true);
            el.addEventListener('change', function (e) {
                var di = e.target && e.target.getAttribute && e.target.getAttribute('data-insp-day');
                if (di) { insp[di] = e.target.value || inspTodayYmd(); inspLoadTab(true); }
            });
            el.addEventListener('click', function (e) {
                var t = e.target;
                if (!t.closest) return;
                e.stopPropagation();
                if (t.closest('[data-insp-min]')) { insp.min = !insp.min; renderInspector(); return; }
                var ah = t.closest('[data-insp-asset]');
                if (ah) {
                    var an = ah.getAttribute('data-insp-asset');
                    if (_replayOn && window.SipReplay && typeof window.SipReplay.inspectorSelect === 'function') { window.SipReplay.inspectorSelect(an); renderInspector(); return; }
                    var cs = findCells(an);
                    if (cs.length) inspectSelect(cs[0]);
                    return;
                }
                if (t.closest('[data-insp-all]')) {
                    if (_replayOn && window.SipReplay && typeof window.SipReplay.inspectorSelect === 'function') window.SipReplay.inspectorSelect(null);
                    renderInspector();
                    return;
                }
                var tb = t.closest('[data-insp-tab]');
                if (tb) { insp.tab = tb.getAttribute('data-insp-tab'); renderInspector(); return; }
                if (t.closest('[data-insp-reload]')) { inspLoadTab(true); return; }
                var hb = t.closest('[data-insp-hours]');
                if (hb) { insp.graphHours = +hb.getAttribute('data-insp-hours') || 6; inspLoadGraph(true); return; }
                var gi = t.closest('[data-insp-gi]');
                if (gi) { insp.graphCol = +gi.getAttribute('data-insp-gi'); inspSetBody(inspGraphHtml()); return; }
                var gc = t.closest('[data-insp-gcol]');
                if (gc && false) {
                    /* live row -> its graph */
                    var want = gc.getAttribute('data-insp-gcol');
                    insp.tab = 'graph';
                    insp.graphWant = want;
                    renderInspector();
                    return;
                }
                if (t.closest('[data-insp-clear]')) { insp.name = null; insp.cellId = null; insp.frame = ''; if (typeof highlight === 'function') highlight(''); renderInspector(); return; }
                if (t.closest('[data-insp-open]')) {
                    var c = state.cellById[insp.cellId];
                    if (c) openSipAssetPopupForCell(c, e);
                    return;
                }
                var col = t.closest('[data-insp-col]');
                if (col && window.SipReplay && typeof window.SipReplay.inspectorPick === 'function') {
                    window.SipReplay.inspectorPick(+col.getAttribute('data-insp-col'));
                    renderInspector();
                }
            });
        }
        if (insp.pos) { el.style.left = insp.pos.x + 'px'; el.style.top = insp.pos.y + 'px'; el.style.right = 'auto'; }
        else el.style.top = (canvas.offsetTop + 10) + 'px';
        return el;
    }
    function inspPalette(el) {
        var T = SIP._theme || {};
        var dark = (T.glowOpacity || 0) > 0;
        el.style.setProperty('--in-bg', dark ? 'rgba(14,24,40,.93)' : 'rgba(255,255,255,.96)');
        el.style.setProperty('--in-text', dark ? '#E6EDF5' : '#13202E');
        el.style.setProperty('--in-muted', dark ? '#9FB0C6' : '#5A6878');
        el.style.setProperty('--in-edge', dark ? 'rgba(255,255,255,.14)' : 'rgba(15,23,42,.12)');
        el.style.setProperty('--in-hover', dark ? 'rgba(255,255,255,.05)' : 'rgba(15,23,42,.04)');
        el.style.setProperty('--in-accent', T.select || '#3BC9DB');
    }
    function inspFmt(v) {
        if (v === null || v === undefined || isNaN(v)) return '--';
        var a = Math.abs(v);
        return a >= 1000 ? v.toFixed(0) : a >= 100 ? v.toFixed(1) : v.toFixed(2);
    }
    /* ---- v618.19: tabs = the asset popup's tabs, inside the panel ------- */
    var INSP_TABS = [['live', 'Live'], ['graph', 'Graph'], ['alerts', 'Alert analytics'], ['events', 'Event log'], ['alarms', 'Alarms']];
    var INSP_TTL = 60000;
    function inspTodayYmd() {
        var d = new Date();
        return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2);
    }
    function inspRec() { return insp.name ? state.assets[insp.name] : null; }
    function inspCache() {
        var k = insp.name || '';
        return insp.cache[k] || (insp.cache[k] = {});
    }
    function relayChip(name, up) {
        return '<span class="' + (up ? 'up' : 'dn') + '">' + escapeHtml(name) + ' ' + (up ? '&uarr; Pickup' : '&darr; Drop') + '</span>';
    }
    /* v618.22 -- INSPECTOR = LIVE VALUES ONLY (history / graphs live in the
       replay dock). Selected asset: header with state, KPIs, relays and one
       tile per reading (value, Low / High, safe range, bar). Nothing
       selected: every asset as a compact card. */
    function liveState(name) {
        var cs = findCells(name), out = [];
        for (var i = 0; i < cs.length; i++) {
            var c = cs[i], at = c.attrs || {}, d = '';
            if (/Track/.test(c.type)) {
                var st = (at.path && (at.path.stroke || at.path.fill)) || '';
                d = (typeof SIP.isLit === 'function' && SIP.isLit(st)) ? 'Occupied' : 'Clear';
            } else if (/Signal|Shunt/.test(c.type)) {
                var lit = (at.signal && at.signal.lit) || '';
                d = lit ? 'Aspect ' + lit : '';
            } else if (/PointMachine/.test(c.type)) {
                var f = String((at.circle1 && at.circle1.fill) || '').toLowerCase();
                d = f === '#22d142' ? 'Normal' : f === '#ffd400' ? 'Reverse' : '';
            }
            if (d && out.indexOf(d) === -1) out.push(d);
        }
        return out.join(' / ');
    }
    /* v618.23: one entry per attribute (the same attribute can arrive under
       its raw name and its AliasName) -- AliasName label, newest sample wins */
    function liveReadings(rec) {
        var cls = (window.TlHealthView && typeof window.TlHealthView.classify === 'function') ? window.TlHealthView.classify : null;
        var meta = window.userAssetSimpleMap || {};
        var by = {};
        Object.keys(rec.attrs || {}).forEach(function (k) {
            var o = rec.attrs[k] || {};
            var m = (o.attrId && rec.id) ? meta[rec.id + '_' + o.attrId] : null;
            var label = String((m && (m.name || m.AliasName)) || o.name || k).replace(/<[^>]*>/g, '').trim();
            var key = o.attrId ? 'id' + o.attrId : 'n' + label.toUpperCase().replace(/[^A-Z0-9]/g, '');
            var prev = by[key];
            if (prev && (prev.o.ts || 0) > (o.ts || 0)) return;
            if (prev && label.length < prev.label.length && !(m && m.name)) label = prev.label;
            by[key] = { o: o, label: label };
        });
        /* raw-name and alias entries without attrId pointing at the same value */
        var seen = {};
        return Object.keys(by).map(function (key) {
            var e = by[key], o = e.o;
            var v = (o.Value === null || o.Value === undefined) ? null : parseFloat(o.Value);
            var c = cls ? cls(rec.id, e.label, v) : { st: 'ok' };
            return { k: e.label, v: v, c: c, stale: o.fresh === false };
        }).filter(function (r) {
            var sk = r.k.toUpperCase();
            if (seen[sk]) return false;
            seen[sk] = 1;
            return true;
        }).sort(function (a, b) { return a.k.localeCompare(b.k, undefined, { numeric: true }); });
    }
    function liveAllHtml() {
        var names = Object.keys(state.assets).filter(function (n) { return findCells(n).length; })
            .sort(function (a, b) { return a.localeCompare(b, undefined, { numeric: true }); });
        if (!names.length) return '<div class="sip-insp-empty">Waiting for live data. Click any track, point or signal in the yard for its live values.</div>';
        var h = '<div class="sip-insp-allhead"><b>All assets</b><span>' + names.length + ' live</span></div><div class="sip-insp-cards">';
        names.forEach(function (n) {
            var rec = state.assets[n];
            var rd = liveReadings(rec);
            var bad = rd.filter(function (r) { return r.c.st === 'low' || r.c.st === 'high'; }).length;
            var st = liveState(n);
            h += '<div class="sip-insp-card' + (bad ? ' bad' : '') + '"><button type="button" class="sip-insp-ah" data-insp-asset="' + escapeHtml(n) + '" title="Show this asset">' +
                '<b>' + escapeHtml(n) + '</b>' + (st ? '<span class="sip-insp-chip ' + stateCls(st) + '">' + escapeHtml(st) + '</span>' : '') +
                (bad ? '<span class="sip-insp-chip bad">' + bad + ' out</span>' : '') + '<i>&#9654;</i></button>';
            var rl = Object.keys(rec.dl || {});
            if (rl.length) { h += '<div class="sip-insp-relays">'; rl.forEach(function (k) { h += relayChip(k, rec.dl[k].isPickup); }); h += '</div>'; }
            rd.forEach(function (r) {
                h += '<div class="sip-insp-mini st-' + (r.c.st || 'ok') + '"><span class="n" title="' + escapeHtml(r.k) + '">' + escapeHtml(r.k) + '</span><span class="v">' + inspFmt(r.v) + '</span></div>';
            });
            h += '</div>';
        });
        return h + '</div>';
    }
    function stateCls(st) {
        return /Occupied/.test(st) ? 'bad' : /Clear|Normal/.test(st) ? 'ok' : /Reverse/.test(st) ? 'warn' : /Aspect R/.test(st) ? 'bad' : /Aspect (Y|YY)/.test(st) ? 'warn' : /Aspect G/.test(st) ? 'ok' : '';
    }
    /* v618.24 POINT MACHINE: only what the PM table / cards show --
       per end A / B for the last operated direction: IPT N/R(A) Max + Avg,
       VPT 110 DC LOC N/R(V), TPT N/R(ms), VPT 24 DC LOC N/R(V), VPT N/R(V);
       relays NWKR / RWKR / NWCR / RWCR. Uses the PM table's own model
       (buildPmTableRowModel) when the page has this asset, otherwise the
       same attribute ids from the SIP live store:
         Normal  A: I 1004 Max / 1002 Avg / 1005 TPT, V 2002;  B: I 3004 / 3002 / 3005, V 4002
         Reverse A: I 6004 / 6002 / 6005, V 7002;            B: I 8004 / 8002 / 8005, V 9002
         VPT 24 DC LOC: A 576 N / 577 R, B 578 N / 579 R;   VPT: A 25 N / 26 R, B 27 N / 28 R */
    function isPmAsset(name) {
        var cs = findCells(name);
        for (var i = 0; i < cs.length; i++) if (/PointMachine/.test(cs[i].type)) return true;
        return false;
    }
    function pmAttrById(rec) {
        var by = {};
        Object.keys(rec.attrs || {}).forEach(function (k) {
            var o = rec.attrs[k] || {};
            var id = o.attrId || 0;
            if (!id) { var m = /-(\d{2,5})-/.exec(k) || /^(\d{2,5})$/.exec(k); if (m) id = parseInt(m[1], 10); }
            if (id && (!by[id] || (o.ts || 0) >= (by[id].ts || 0))) by[id] = o;
        });
        return by;
    }
    function pmReadings(rec, dirHint) {
        var cls = (window.TlHealthView && typeof window.TlHealthView.classify === 'function') ? window.TlHealthView.classify : null;
        var out = [];
        var push = function (label, v) {
            var n = (v === null || v === undefined || v === '') ? null : parseFloat(String(v).replace(/[^0-9.\-]/g, ''));
            if (n !== null && isNaN(n)) n = null;
            out.push({ k: label, v: n, c: cls ? cls(rec.id, label, n) : { st: 'ok' }, stale: false });
        };
        var m = null;
        if (rec.id && typeof window.buildPmTableRowModel === 'function' && window.wsLiveData && window.wsLiveData[rec.id]) {
            try { m = window.buildPmTableRowModel(rec.id); } catch (e) { m = null; }
        }
        var DEF = [['iptMax', 'IPT N/R(A) Max'], ['iptAvg', 'IPT N/R(A) Avg'], ['vpt110Avg', 'VPT 110 DC LOC N/R(V)'], ['tpt', 'TPT N/R(ms)'], ['locIndication', 'VPT 24 DC LOC N/R(V)'], ['krIndication', 'VPT N/R(V)']];
        if (m) {
            ['A', 'B'].forEach(function (E) {
                var f = m[E]; if (!f) return;
                var any = DEF.some(function (d) { return f[d[0]] && f[d[0]].present; });
                if (!any) return;
                DEF.forEach(function (d) { var en = f[d[0]] || {}; push(E + ' ' + d[1], en.present ? en.text : null); });
            });
            return { rows: out, dir: (m.operation && m.operation.direction) || dirHint || '', when: m.operationDate || '' };
        }
        var by = pmAttrById(rec);
        var val = function (id) { var o = by[id]; return o ? o.Value : null; };
        var tsOf = function (ids) { var t = 0; ids.forEach(function (id) { if (by[id] && (by[id].ts || 0) > t) t = by[id].ts; }); return t; };
        var dir = dirHint;
        if (dir !== 'Normal' && dir !== 'Reverse') dir = tsOf([6002, 6004, 8002, 8004]) > tsOf([1002, 1004, 3002, 3004]) ? 'Reverse' : 'Normal';
        var R = dir === 'Reverse';
        var ends = { A: R ? [6004, 6002, 7002, 6005, 577, 26] : [1004, 1002, 2002, 1005, 576, 25], B: R ? [8004, 8002, 9002, 8005, 579, 28] : [3004, 3002, 4002, 3005, 578, 27] };
        ['A', 'B'].forEach(function (E) {
            var ids = ends[E];
            if (!ids.some(function (id) { return by[id]; })) return;
            DEF.forEach(function (d, i) { push(E + ' ' + d[1], val(ids[i])); });
        });
        return { rows: out, dir: dir, when: '' };
    }
    function liveInspectorHtml() {
        var rec = inspRec();
        if (!rec) return '<div class="sip-insp-empty">No live data for this asset yet.</div>';
        var pm = isPmAsset(insp.name) ? pmReadings(rec, liveState(insp.name)) : null;
        var rd = pm ? pm.rows : liveReadings(rec);
        var bad = rd.filter(function (r) { return r.c.st === 'low' || r.c.st === 'high'; }).length;
        var near = rd.filter(function (r) { return r.c.st === 'near'; }).length;
        var st = liveState(insp.name);
        var rl = Object.keys(rec.dl || {});
        if (pm) {
            /* PM relays: only NWKR / RWKR / NWCR / RWCR (Combined / A End / B End names accepted) */
            var want = ['NWKR', 'RWKR', 'NWCR', 'RWCR'], keep = [];
            want.forEach(function (w) {
                for (var i = 0; i < rl.length; i++) {
                    var nk = rl[i].toUpperCase().replace(/[^A-Z]/g, '');
                    if (nk === w || nk === 'COMBINED' + w || nk.slice(-4) === w) { if (keep.indexOf(rl[i]) === -1) keep.push(rl[i]); break; }
                }
            });
            rl = keep;
        }
        /* v618.25: summary strip -- health ring + state + range chips */
        var score = Math.max(0, 100 - bad * 18 - near * 6);
        var ringCol = bad >= 3 ? '#E5484D' : bad ? '#E0A100' : '#22A55B';
        var C = 2 * Math.PI * 15;
        var h = '<div class="sip-insp-sumrow"><svg width="40" height="40" viewBox="0 0 40 40" aria-hidden="true">' +
            '<circle cx="20" cy="20" r="15" fill="none" stroke="var(--in-edge)" stroke-width="4"></circle>' +
            '<circle cx="20" cy="20" r="15" fill="none" stroke="' + ringCol + '" stroke-width="4" stroke-linecap="round" stroke-dasharray="' + (score / 100 * C).toFixed(1) + ' ' + C.toFixed(1) + '" transform="rotate(-90 20 20)"></circle>' +
            '<text x="20" y="24" text-anchor="middle" font-size="11" font-weight="800" fill="currentColor">' + score + '</text></svg>' +
            '<div class="sip-insp-chips">' + (st ? '<span class="sip-insp-chip ' + stateCls(st) + '">' + escapeHtml(st) + '</span>' : '') +
            (pm && pm.when && pm.when !== '--' ? '<span class="sip-insp-chip">Last op ' + escapeHtml(pm.when) + '</span>' : '') +
            '<span class="sip-insp-chip ' + (bad ? 'bad' : 'ok') + '">' + (bad ? bad + ' outside' : 'In range') + '</span>' +
            (near ? '<span class="sip-insp-chip warn">' + near + ' near limit</span>' : '') + '</div></div>';
        if (rl.length) {
            h += '<div class="sip-insp-sect">Relays</div><div class="sip-insp-relays">';
            rl.forEach(function (k) { h += relayChip(k, rec.dl[k].isPickup); });
            h += '</div>';
        }
        if (!rd.length) return h + '<div class="sip-insp-empty">No analog readings.</div>';
        h += '<div class="sip-insp-sect">Readings <span>' + rd.length + '</span></div><div class="sip-insp-list">';
        rd.forEach(function (r) {
            var g = r.c.range;
            h += '<div class="sip-insp-li st-' + (r.c.st || 'ok') + (r.stale ? ' stale' : '') + '" title="' + escapeHtml(r.k) +
                (g ? ' | safe ' + inspFmt(g.min) + ' - ' + inspFmt(g.max) : '') + (r.stale ? ' | stale' : '') + '">' +
                '<span class="n">' + escapeHtml(r.k) + '</span>' +
                '<span class="v">' + inspFmt(r.v) + (r.c.st === 'low' ? ' <i>L</i>' : r.c.st === 'high' ? ' <i>H</i>' : '') + '</span>' +
                (g ? '<span class="sip-insp-bar2"><i></i><em></em><u style="left:' + r.c.pos.toFixed(1) + '%"></u></span>' : '<span></span>') +
                '</div>';
        });
        return h + '</div>';
    }

    /* Graph: DashboardHistory for this asset (newest samples of the period),
       chosen reading drawn with its safe band. */
    function inspLoadGraph(force) {
        var rec = inspRec(), C = inspCache();
        var hours = insp.graphHours || 6;
        if (!rec || !rec.id) { inspSetBody('<div class="sip-insp-empty">Asset id not known yet -- wait for live data.</div>'); return; }
        if (!force && C.graph && C.graph.hours === hours && Date.now() - C.graph.at < INSP_TTL) { inspSetBody(inspGraphHtml()); return; }
        inspSetBody('<div class="sip-insp-empty">Loading graph...</div>');
        var now = Date.now(), name = insp.name;
        var f = function (ms) {
            var d = new Date(ms), p2 = function (n) { return (n < 10 ? '0' : '') + n; };
            return p2(d.getDate()) + p2(d.getMonth() + 1) + d.getFullYear() + '_' + p2(d.getHours()) + p2(d.getMinutes()) + p2(d.getSeconds());
        };
        $.ajax({
            url: '/FRS25/TelemetryHistory/GetDashboardHistoryData', type: 'GET', dataType: 'json',
            data: { assetId: rec.id, startDate: f(now - hours * 3600000), endDate: f(now), page: 1, pageSize: 2000, sort: 'desc', fillGaps: 'true', cursor: '' },
            success: function (resp) {
                if (insp.name !== name) return;
                var pl = resp || {};
                var inner = pl.Data || pl.data;
                if (inner && (inner.columns || inner.Columns)) pl = inner;
                var cols = (pl.columns || pl.Columns || []).map(function (c) {
                    return { name: String(c.name || c.Name || ''), dl: String(c.dataType || c.DataType || '').toLowerCase() === 'datalogger', skip: (c.attrId === null || c.attrId === undefined) && (c.AttrId === null || c.AttrId === undefined) };
                });
                var rows = (pl.rows || pl.Rows || []).map(function (r) { return { t: new Date(r.ts || r.Ts).getTime(), v: r.v || r.V || [] }; })
                    .filter(function (r) { return !isNaN(r.t); }).sort(function (a, b) { return a.t - b.t; });
                inspCache().graph = { at: Date.now(), hours: hours, cols: cols, rows: rows };
                if (insp.tab === 'graph') inspSetBody(inspGraphHtml());
            },
            error: function (x) { if (insp.name === name && insp.tab === 'graph') inspSetBody('<div class="sip-insp-empty">Graph could not be loaded (HTTP ' + (x && x.status) + ').</div>'); }
        });
    }
    function inspGraphHtml() {
        var G = inspCache().graph;
        var h = '<div class="sip-insp-tools">' + [1, 6, 24].map(function (hh) {
            return '<button type="button" data-insp-hours="' + hh + '" aria-pressed="' + ((insp.graphHours || 6) === hh) + '">' + hh + ' h</button>';
        }).join('') + '<button type="button" data-insp-reload="1" title="Reload">&#8635;</button></div>';
        if (!G || !G.rows.length) return h + '<div class="sip-insp-empty">No history in this period.</div>';
        var idx = [];
        for (var i = 0; i < G.cols.length; i++) if (!G.cols[i].skip) idx.push(i);
        if (insp.graphWant) {
            var wk = String(insp.graphWant).toUpperCase().replace(/[^A-Z0-9]/g, '');
            for (var w = 0; w < idx.length; w++) if (String(G.cols[idx[w]].name).toUpperCase().replace(/[^A-Z0-9]/g, '') === wk) insp.graphCol = idx[w];
            insp.graphWant = null;
        }
        if (insp.graphCol === null || insp.graphCol === undefined || G.cols[insp.graphCol] === undefined) insp.graphCol = idx.length ? idx[0] : 0;
        h += '<div class="sip-insp-chips">' + idx.map(function (ci) {
            return '<button type="button" data-insp-gi="' + ci + '" aria-pressed="' + (ci === insp.graphCol) + '">' + escapeHtml(G.cols[ci].name) + '</button>';
        }).join('') + '</div>';
        var col = G.cols[insp.graphCol];
        var pts = [];
        for (var r = 0; r < G.rows.length; r++) {
            var v = parseFloat(G.rows[r].v[insp.graphCol]);
            if (!isNaN(v)) pts.push({ t: G.rows[r].t, v: v });
        }
        if (pts.length < 2) return h + '<div class="sip-insp-empty">Not enough samples for ' + escapeHtml(col.name) + '.</div>';
        var lo = Infinity, hi = -Infinity;
        pts.forEach(function (p) { lo = Math.min(lo, p.v); hi = Math.max(hi, p.v); });
        var mn = lo, mx = hi;
        var cls = (window.TlHealthView && typeof window.TlHealthView.classify === 'function') ? window.TlHealthView.classify : null;
        var rec = inspRec();
        var rg = cls ? cls(rec && rec.id, col.name, pts[pts.length - 1].v).range : null;
        if (rg) { lo = Math.min(lo, rg.min); hi = Math.max(hi, rg.max); }
        if (hi === lo) { hi += 1; lo -= 1; }
        var pad = (hi - lo) * 0.08; lo -= pad; hi += pad;
        var t0 = pts[0].t, t1 = pts[pts.length - 1].t, W = 400, H = 150;
        var X = function (t) { return ((t - t0) / ((t1 - t0) || 1)) * W; }, Y = function (v) { return H - ((v - lo) / (hi - lo)) * H; };
        var d = '';
        pts.forEach(function (p, k) {
            if (col.dl && k) d += 'L' + X(p.t).toFixed(1) + ' ' + Y(pts[k - 1].v).toFixed(1);
            d += (k ? 'L' : 'M') + X(p.t).toFixed(1) + ' ' + Y(p.v).toFixed(1);
        });
        var tm = function (ms) { var x = new Date(ms); return ('0' + x.getHours()).slice(-2) + ':' + ('0' + x.getMinutes()).slice(-2); };
        h += '<div class="sip-insp-graph"><div class="sip-insp-gh"><b>' + escapeHtml(col.name) + '</b><span>now ' + inspFmt(pts[pts.length - 1].v) + '</span></div>' +
            '<svg viewBox="0 0 ' + W + ' ' + H + '" preserveAspectRatio="none" role="img" aria-label="' + escapeHtml(col.name) + ' history">' +
            (rg ? '<rect x="0" y="' + Y(rg.max).toFixed(1) + '" width="' + W + '" height="' + (Y(rg.min) - Y(rg.max)).toFixed(1) + '" fill="rgba(31,157,85,.16)"></rect>' : '') +
            '<path class="' + (col.dl ? 'ins-dl' : 'ins-tr') + '" d="' + d + '" fill="none" stroke-width="1.6" vector-effect="non-scaling-stroke"></path></svg>' +
            '<div class="sip-insp-gf"><span>min ' + inspFmt(mn) + '</span><span>' + tm(t0) + ' to ' + tm(t1) +
            (rg ? ' | safe ' + inspFmt(rg.min) + '-' + inspFmt(rg.max) : '') + '</span><span>max ' + inspFmt(mx) + '</span></div></div>';
        return h;
    }

    /* Alert analytics (same API as the popup): predictive / failure by cause code */
    function inspLoadAlerts(force) {
        var rec = inspRec(), C = inspCache(), day = insp.alertDay || inspTodayYmd(), name = insp.name;
        if (!force && C.alerts && C.alerts.day === day && Date.now() - C.alerts.at < INSP_TTL) { inspSetBody(inspAlertsHtml()); return; }
        inspSetBody('<div class="sip-insp-empty">Loading alert analytics...</div>');
        $.ajax({
            url: '/FRS25/Telemetry/GetSipAssetAlertAnalytics', type: 'POST',
            data: { siteId: state.siteId || 0, assetId: (rec && rec.id) || 0, assetName: name || '', fromDate: day, toDate: day, alertIds: '' },
            success: function (res) {
                if (insp.name !== name) return;
                inspCache().alerts = { at: Date.now(), day: day, res: res || {} };
                if (insp.tab === 'alerts') inspSetBody(inspAlertsHtml());
            },
            error: function (x) { if (insp.name === name && insp.tab === 'alerts') inspSetBody('<div class="sip-insp-empty">Alert analytics could not be loaded (HTTP ' + (x && x.status) + ').</div>'); }
        });
    }
    function inspDayTools(key) {
        var v = insp[key] || inspTodayYmd();
        return '<div class="sip-insp-tools"><input type="date" data-insp-day="' + key + '" value="' + v + '" max="' + inspTodayYmd() + '" aria-label="Date">' +
            '<button type="button" data-insp-reload="1" title="Reload">&#8635;</button></div>';
    }
    function inspAlertsHtml() {
        var A = inspCache().alerts;
        var h = inspDayTools('alertDay');
        if (!A) return h;
        var r = A.res || {};
        var pc = r.predictiveCauses || [], fc = r.failureCauses || [];
        h += '<div class="sip-insp-kpis"><div class="k p"><b>' + (r.pred || 0) + '</b><span>Predictive</span></div>' +
            '<div class="k f"><b>' + (r.fail || 0) + '</b><span>Failure</span></div></div>';
        var sect = function (title, list, f) {
            if (!list.length) return '';
            var max = Math.max.apply(null, list.map(function (c) { return c.count || 0; })) || 1;
            return '<div class="sip-insp-sect">' + title + '</div>' + list.map(function (c) {
                return '<div class="sip-insp-cause"><span class="n">' + escapeHtml(c.code || '-') + '</span>' +
                    '<span class="b"><i class="' + (f ? 'f' : 'p') + '" style="width:' + Math.max(4, ((c.count || 0) / max) * 100).toFixed(0) + '%"></i></span>' +
                    '<span class="c">' + (c.count || 0) + '</span></div>';
            }).join('');
        };
        var body = sect('Failure causes', fc, true) + sect('Predictive causes', pc, false);
        return h + (body || '<div class="sip-insp-empty">No alerts on this day.</div>');
    }

    /* Event log (same API as the popup): alerts + maintenance mode, newest first */
    function inspLoadEvents(force) {
        var rec = inspRec(), C = inspCache(), day = insp.eventDay || inspTodayYmd(), name = insp.name;
        if (!rec || !rec.id) { inspSetBody(inspDayTools('eventDay') + '<div class="sip-insp-empty">Asset id not known yet.</div>'); return; }
        if (!force && C.events && C.events.day === day && Date.now() - C.events.at < INSP_TTL) { inspSetBody(inspEventsHtml()); return; }
        inspSetBody('<div class="sip-insp-empty">Loading event log...</div>');
        $.ajax({
            url: '/FRS25/Telemetry/GetSipEventLog', type: 'POST', contentType: 'application/json', dataType: 'json', timeout: 30000,
            data: JSON.stringify({ AssetId: parseInt(rec.id, 10), SiteId: parseInt(state.siteId || 0, 10), AssetTypeId: parseInt(rec.typeId || 0, 10) || 0,
                FromDate: day + 'T00:00:00', ToDate: day + 'T23:59:59', Take: 200 }),
            success: function (res) {
                if (insp.name !== name) return;
                inspCache().events = { at: Date.now(), day: day, res: res || {} };
                if (insp.tab === 'events') inspSetBody(inspEventsHtml());
            },
            error: function (x) { if (insp.name === name && insp.tab === 'events') inspSetBody('<div class="sip-insp-empty">Event log could not be loaded (HTTP ' + (x && x.status) + ').</div>'); }
        });
    }
    function inspTime(raw) {
        if (!raw) return '';
        var d = new Date(String(raw).replace(' ', 'T'));
        return isNaN(d.getTime()) ? String(raw) : ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2);
    }
    function inspEventsHtml() {
        var E = inspCache().events;
        var h = inspDayTools('eventDay');
        if (!E) return h;
        var r = E.res || {};
        if (r.Success === false) return h + '<div class="sip-insp-empty">' + escapeHtml(r.Message || 'No data.') + '</div>';
        var ev = [];
        (r.Alerts || []).forEach(function (a) {
            var t = a.IncidentDateTime || a.SetTimestamp || a.incidentDateTime || a.setTimestamp || '';
            var st = parseInt(a.AlertStatus || a.alertStatus || 0, 10);
            ev.push({ t: new Date(String(t).replace(' ', 'T')).getTime() || 0, raw: t, kind: st === 1 ? 'act' : 'clr',
                text: (a.CauseCode || a.causeCode || a.AlertCode || a.alertCode || 'Alert'), tag: st === 1 ? 'Active' : 'Cleared' });
        });
        (r.MaintenanceModes || []).forEach(function (m) {
            var on = m.ActiveTime || m.activeTime, off = m.InActiveTime || m.inActiveTime;
            if (on) ev.push({ t: new Date(String(on).replace(' ', 'T')).getTime() || 0, raw: on, kind: 'mnt', text: 'Maintenance mode on', tag: 'Maint.' });
            if (off && String(off).indexOf('0001') < 0) ev.push({ t: new Date(String(off).replace(' ', 'T')).getTime() || 0, raw: off, kind: 'mnt', text: 'Maintenance mode off', tag: 'Maint.' });
        });
        ev.sort(function (a, b) { return b.t - a.t; });
        if (!ev.length) return h + '<div class="sip-insp-empty">No events on this day.</div>';
        return h + '<ul class="sip-insp-tl">' + ev.map(function (e) {
            return '<li class="' + e.kind + '"><time>' + escapeHtml(inspTime(e.raw)) + '</time><span>' + escapeHtml(e.text) + '</span><em>' + e.tag + '</em></li>';
        }).join('') + '</ul>';
    }

    /* Active alarms (same API as the popup) */
    function inspLoadAlarms(force) {
        var rec = inspRec(), C = inspCache(), name = insp.name;
        if (!rec || !rec.id) { inspSetBody('<div class="sip-insp-empty">Asset id not known yet.</div>'); return; }
        if (!force && C.alarms && Date.now() - C.alarms.at < INSP_TTL) { inspSetBody(inspAlarmsHtml()); return; }
        inspSetBody('<div class="sip-insp-empty">Loading alarms...</div>');
        $.ajax({
            url: '/FRS25/Telemetry/GetSipActiveAlarms', type: 'POST', contentType: 'application/json', dataType: 'json', timeout: 30000,
            data: JSON.stringify({ AssetId: parseInt(rec.id, 10), SiteId: parseInt(state.siteId || 0, 10), AssetTypeId: parseInt(rec.typeId || 0, 10) || 0 }),
            success: function (res) {
                if (insp.name !== name) return;
                inspCache().alarms = { at: Date.now(), res: res || {} };
                if (insp.tab === 'alarms') inspSetBody(inspAlarmsHtml());
            },
            error: function (x) { if (insp.name === name && insp.tab === 'alarms') inspSetBody('<div class="sip-insp-empty">Alarms could not be loaded (HTTP ' + (x && x.status) + ').</div>'); }
        });
    }
    function inspAlarmsHtml() {
        var A = inspCache().alarms;
        var h = '<div class="sip-insp-tools"><button type="button" data-insp-reload="1" title="Reload">&#8635; Refresh</button></div>';
        if (!A) return h;
        var r = A.res || {};
        if (r.Success === false) return h + '<div class="sip-insp-empty">' + escapeHtml(r.Message || 'No data.') + '</div>';
        var list = r.Alarms || [], n = r.ActiveCount || list.length || 0;
        h += '<div class="sip-insp-banner ' + (n ? 'bad' : 'ok') + '">' + (n ? n + ' active alarm' + (n > 1 ? 's' : '') + ' -- needs attention' : 'No active alarms') + '</div>';
        if (!list.length) return h;
        return h + '<ul class="sip-insp-tl">' + list.map(function (a) {
            return '<li class="act"><time>' + escapeHtml(inspTime(a.RaisedAt)) + '</time><span>' + escapeHtml(a.CauseCode || 'Alarm') +
                (a.DurationDisplay ? ' <small>' + escapeHtml(a.DurationDisplay) + '</small>' : '') + '</span><em>' + escapeHtml(a.Severity || 'Active') + '</em></li>';
        }).join('') + '</ul>';
    }

    function inspSetBody(html) {
        var b = document.querySelector('#sipInspector .sip-insp-body');
        if (!b) return;
        var sc = b.scrollTop;
        b.innerHTML = html;
        b.scrollTop = sc;
    }
    function inspLoadTab(force) {
        if (insp.tab === 'graph') inspLoadGraph(force);
        else if (insp.tab === 'alerts') inspLoadAlerts(force);
        else if (insp.tab === 'events') inspLoadEvents(force);
        else if (insp.tab === 'alarms') inspLoadAlarms(force);
        else inspSetBody(liveInspectorHtml());
    }
    function renderInspector() {
        if (insp.pressing) return;
        if (!inspectorVisible()) {
            var old = document.getElementById('sipInspector');
            if (old) old.style.display = 'none';
            inspReserve(false);
            return;
        }
        var el = ensureInspector();
        if (!el) return;
        if (!insp.name) {
            /* v618.23: nothing selected -> no panel, yard uses the full width */
            el.style.display = 'none';
            insp.frame = '';
            inspReserve(false);
            return;
        }
        el.style.display = '';
        inspReserve(!insp.min);
        inspPalette(el);
        var replay = _replayOn && window.SipReplay && typeof window.SipReplay.inspectorHtml === 'function';
        if (replay) {
            /* v618.21: during a replay the asset values live in the replay dock
               (all assets + history), so the yard panel steps aside */
            insp.frame = '';
            el.style.display = 'none';
            inspReserve(false);
            return;
        }
        if (replay) {
            var prev = el.querySelector('.sip-insp-body');
            var scroll = prev ? prev.scrollTop : 0;
            el.innerHTML = '<div class="sip-insp-bar"><b>Replay values</b><button type="button" data-insp-min="1">' + (insp.min ? '+' : '&minus;') + '</button></div>' +
                (insp.min ? '' : '<div class="sip-insp-body">' + window.SipReplay.inspectorHtml() + '</div>');
            var nb = el.querySelector('.sip-insp-body');
            if (nb && scroll) nb.scrollTop = scroll;
            return;
        }
        var rec = inspRec();
        var frame = [insp.name, insp.tab, insp.min].join('|');
        if (frame !== insp.frame) {
            insp.frame = frame;
            var cell0 = state.cellById[insp.cellId] || {};
            var ico = /Track/.test(cell0.type || '') ? 'fa-grip-lines' : /PointMachine/.test(cell0.type || '') ? 'fa-code-branch' : /Signal|Shunt/.test(cell0.type || '') ? 'fa-traffic-light' : 'fa-microchip';
            el.innerHTML = '<div class="sip-insp-bar"><span class="sip-insp-ico"><i class="fa-solid ' + ico + '"></i></span><span class="sip-insp-live"></span><b>' + escapeHtml(insp.name) + '</b>' +
                '<span class="sip-insp-upd" id="sipInspUpd"></span>' +
                '<button type="button" data-insp-open="1" title="Open the asset popup (graph, alerts, events, alarms)">Details</button>' +
                '<button type="button" data-insp-clear="1" title="Close" aria-label="Close">&times;</button>' +
                '<button type="button" data-insp-min="1" title="' + (insp.min ? 'Expand' : 'Collapse') + '">' + (insp.min ? '+' : '&minus;') + '</button></div>' +
                (insp.min ? '' : '<div class="sip-insp-body">' + liveInspectorHtml() + '</div>');
        } else if (insp.name && !insp.min) {
            inspSetBody(liveInspectorHtml());
        }
        var up = document.getElementById('sipInspUpd');
        if (up) up.textContent = rec && rec.lastAt ? new Date(rec.lastAt).toLocaleTimeString() : '';
    }
    setInterval(function () {
        if (document.hidden) return;
        if (inspectorVisible()) renderInspector();
        else { var o = document.getElementById('sipInspector'); if (o) o.style.display = 'none'; }
    }, 400);
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
        /* Post-bound shapes carry no own label — their asset name lives in
           _sipAsset (set by bindPostsToSignals). Resolve that BEFORE the
           empty-label bail-out, or clicking such a signal yields no asset and
           the popup can't open. */
        if (!label) return cell._sipAsset || '';
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
            try { window.SipAssetPopupLive.open(cell, ctx); return; }
            catch (ex0) { swarn('SipAssetPopupLive.open failed, falling back:', ex0 && ex0.message); }
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

        if (_replayOn) replayEnd(true);
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
        state.domCells = {}; state.domRail = {}; state.domGap = {}; state.svgEl = null;
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
        state.needFit = true; state.view = null;   // fit-to-content on (re)load
        buildIndex();
        bindPostsToSignals();
        loadSipAllTypeMetadata(siteId);
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
        if (_replayOn) return;
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

    /* ── Post → signal binding ───────────────────────────────────────────
       Some layouts (e.g. SECR / G CABIN) put the asset name on a text-only
       Signal Post (examples.Post / Post1) instead of on the signal shape
       itself. A Post renders only a text pill (sip-library renderPost) and
       can never show an aspect, so such signals never lit.

       For every DRIVABLE shape (Signal / SignalShunt / Shaunt*) that has NO
       label of its own, adopt the name of the nearest named Post and
       register the shape under that name so live telemetry drives it. The
       pick is type-aware (shunt discs prefer SH* posts, main signals prefer
       non-SH posts), greedy by distance, and capped so a shape with no post
       nearby stays unbound. Shapes that already carry their own label are
       left completely untouched — sites that self-label their signals (e.g.
       Gahlota) are therefore unaffected, and a label typed directly on a
       shape remains the authoritative override. */
    var POSTBIND_MAX_DIST = 250;   // px between shape centre and post centre
    var POSTBIND_TYPE_PENALTY = 140; // cost added when shape/post class differ
    function cellCenter(c) {
        var p = (c && c.position) || {}, s = (c && c.size) || {};
        return { x: (p.x || 0) + (s.width || 0) / 2, y: (p.y || 0) + (s.height || 0) / 2 };
    }
    function isShuntName(n) { return /^SH/i.test(String(n == null ? '' : n).replace(/[^A-Za-z0-9]/g, '')); }
    function registerCellName(cell, name) {
        cell._sipAsset = name;
        (state.byLabel[name] = state.byLabel[name] || []);
        if (state.byLabel[name].indexOf(cell) === -1) state.byLabel[name].push(cell);
        var nk = _normLabel(name);
        if (nk) {
            (state.byNorm[nk] = state.byNorm[nk] || []);
            if (state.byNorm[nk].indexOf(cell) === -1) state.byNorm[nk].push(cell);
        }
    }
    function bindPostsToSignals() {
        var posts = [], shapes = [], i, c;
        for (i = 0; i < state.cells.length; i++) {
            c = state.cells[i]; if (!c) continue;
            if (POST_TYPES[c.type]) {
                var nm = getCellLabel(c);
                if (nm) posts.push({ name: nm, shunt: isShuntName(nm), c: cellCenter(c), used: false });
            } else if ((COMPOSITE_SIGNAL[c.type] || SHUNT_TYPES[c.type]) && !getCellLabel(c)) {
                shapes.push({ cell: c, shunt: !!SHUNT_TYPES[c.type], c: cellCenter(c) });
            }
        }
        if (!posts.length || !shapes.length) return;

        var pairs = [], si, pi;
        for (si = 0; si < shapes.length; si++) {
            for (pi = 0; pi < posts.length; pi++) {
                var dx = shapes[si].c.x - posts[pi].c.x, dy = shapes[si].c.y - posts[pi].c.y;
                var d = Math.sqrt(dx * dx + dy * dy);
                if (d > POSTBIND_MAX_DIST) continue;
                pairs.push({ si: si, pi: pi, cost: (shapes[si].shunt === posts[pi].shunt) ? d : d + POSTBIND_TYPE_PENALTY });
            }
        }
        pairs.sort(function (a, b) { return a.cost - b.cost; });

        var shapeUsed = {}, bound = 0;
        for (i = 0; i < pairs.length; i++) {
            var pr = pairs[i];
            if (shapeUsed[pr.si] || posts[pr.pi].used) continue;
            shapeUsed[pr.si] = 1; posts[pr.pi].used = true;
            registerCellName(shapes[pr.si].cell, posts[pr.pi].name);
            bound++;
            slog('Post-bind: ' + shapes[pr.si].cell.type + ' ' + shapes[pr.si].cell.id + ' -> "' + posts[pr.pi].name + '"');
        }
        if (bound) { state.findMemo = {}; sinfo('Post→signal binding: ' + bound + ' unlabelled shape(s) bound to the nearest named post.'); }
    }

    /* ── SIP all-asset-types metadata cache ──────────────────────────────
       The classic telemetrylive grid loads GetBulkAssetMetadata for ONE
       asset type at a time and wipes its alias / DataLogger maps on every
       type change. The SIP schematic shows ALL types at once, so clicking a
       signal while the "Track" pill is selected finds no signal metadata and
       the asset popup can only show raw snapshot values (no AliasName labels,
       no TPR/NWKR relay rows).

       Here we pre-load metadata for EVERY asset type of the site once, into a
       standalone, never-wiped store (window.sipMetaAll) that the popup reads
       as a fallback. It never touches the classic single-type globals, so the
       telemetry grid is unaffected. Point Machine (type 3) uses a different
       endpoint in the classic code and is skipped here (PM rows fall back to
       the live snapshot). Cached per site. */
    window.sipMetaAll = window.sipMetaAll || { siteId: null, simple: {}, dl: {}, assets: {}, zeroOffset: {}, loaded: false, pending: 0 };
    var PM_ASSET_TYPE_ID = '3';
    function sipMetaPost(url, body) {
        return fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); });
    }
    function sipMetaMergeBulk(resp) {
        if (!resp || !resp.mAssets) return;
        var M = window.sipMetaAll, assets = resp.mAssets, n = 0;
        for (var bi = 0; bi < assets.length; bi++) {
            var asset = assets[bi];
            if (!asset || !asset.Id) continue;
            var aid = String(asset.Id);
            M.assets[aid] = { Id: asset.Id, Name: asset.Name, SiteId: asset.SiteId, AssetTypeId: asset.AssetTypeId };
            var off = parseFloat(asset.ZeroOffsetValue);
            if (!isNaN(off) && off > 0) M.zeroOffset[aid] = off;
            var attrs = asset.assetAttributes || asset.AssetAttributes || [];
            for (var j = 0; j < attrs.length; j++) {
                var attr = attrs[j];
                var attrId = String(attr.Id || attr.AssetAttributeId || attr.AttributeId || '').trim();
                var alias = String(attr.AliasName || '').trim();
                var rawName = String(attr.Title || attr.AttributeName || attr.Name || alias || '').trim();
                var disp = alias || rawName;
                if (!attrId || !disp) continue;
                var sk = aid + '_' + attrId;
                if (!M.simple[sk]) M.simple[sk] = {
                    name: disp, aliasName: disp, AliasName: disp,
                    attributeName: rawName, AttributeName: rawName,
                    assetName: asset.Name || '', assetTypeId: asset.AssetTypeId, siteId: asset.SiteId,
                    multiplication: attr.Multiplication, absolute: attr.Absolute,
                    minValue: attr.MinValue, maxValue: attr.MaxValue
                };
            }
            var dls = asset.mAssetInfoDataloggers || asset.MAssetInfoDataloggers || [];
            for (var k = 0; k < dls.length; k++) {
                var dl = dls[k];
                if (!dl) continue;
                var role = String(dl.Value || dl.value || '').trim();
                if (!role || role === '0' || role.toLowerCase() === 'null') continue;
                var dlName = String(dl.DataloggerAttribute || dl.dataloggerAttribute || '').trim()
                    || String(dl.DataloggerAssetName || dl.dataloggerAssetName || '').trim()
                    || String(dl.AttributeName || dl.Name || ('DL ' + role)).trim();
                if (!dlName) continue;
                var dk = aid + '_' + role;
                if (!M.dl[dk]) M.dl[dk] = {
                    role: role, Role: role, dataloggerRole: role,
                    name: dlName, Name: dlName,
                    attributeName: dl.DataloggerAttribute || dlName, AttributeName: dl.DataloggerAttribute || dlName,
                    dataloggerAttribute: dl.DataloggerAttribute || '', DataloggerAttribute: dl.DataloggerAttribute || '',
                    dataloggerAssetName: dl.DataloggerAssetName || '', DataloggerAssetName: dl.DataloggerAssetName || '',
                    dataloggerAttributeId: dl.Value || null, DataloggerAttributeId: dl.Value || null,
                    dataloggerValueId: dl.Value || null, sourceId: dl.Id || null,
                    contactType: dl.ContactType || '', assetName: asset.Name, AssetName: asset.Name,
                    assetTypeId: asset.AssetTypeId, siteId: asset.SiteId
                };
            }
            n++;
        }
        return n;
    }
    function loadSipAllTypeMetadata(siteId) {
        siteId = String(siteId || '');
        if (!siteId || siteId === '0') return;
        var M = window.sipMetaAll;
        if (M.siteId === siteId && (M.loaded || M.pending > 0)) return;   // cached / in-flight
        window.sipMetaAll = M = { siteId: siteId, simple: {}, dl: {}, assets: {}, zeroOffset: {}, loaded: false, pending: 0 };
        sipMetaPost('/FRS25/Telemetry/GetAssetTypeBySiteId', { siteId: siteId }).then(function (types) {
            if (window.sipMetaAll !== M) return;                         // site changed meanwhile
            var list = (types || []).filter(function (t) {
                return t && t.IsActive && String(t.Id) !== PM_ASSET_TYPE_ID
                    && !(typeof window.isHiddenAssetTypeName === 'function' && window.isHiddenAssetTypeName(t.Name));
            });
            if (!list.length) { M.loaded = true; return; }
            M.pending = list.length;
            list.forEach(function (t) {
                sipMetaPost('/FRS25/Telemetry/GetBulkAssetMetadata',
                    { SearchCriteria: { SiteId: siteId, AssetTypeId: parseInt(t.Id, 10) } })
                    .then(function (resp) { if (window.sipMetaAll === M) sipMetaMergeBulk(resp); })
                    .catch(function (e) { swarnOnce('sipmeta-type-' + t.Id, 'SIP metadata: type ' + t.Id + ' failed:', e && e.message); })
                    .then(function () {
                        if (window.sipMetaAll !== M) return;
                        if (--M.pending <= 0) {
                            M.loaded = true;
                            sinfo('SIP all-type metadata ready — ' + Object.keys(M.assets).length + ' assets, ' +
                                Object.keys(M.simple).length + ' attrs, ' + Object.keys(M.dl).length + ' dataloggers.');
                        }
                    });
            });
        }).catch(function (e) { swarnOnce('sipmeta-types', 'SIP metadata: GetAssetTypeBySiteId failed:', e && e.message); });
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

    function feedItems(items, fromReplay) {
        if (_replayOn && !fromReplay) return;          // v618.0: live is paused during replay
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
            if (!fromReplay && isDuplicate(d, now)) continue;

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
            if (!fromReplay) mirrorToShared(d, assetName, assetId, attrName, rawAttr, attrId, num, isDL, tsDev, hasFresh, fresh, rawFresh, kind);
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

        if (fromReplay) {
            if (hasDirty()) requestRender();
            return;
        }
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
        if (_replayOn) return null;
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
        if (_replayOn) return null;
        var e = rec && rec.id && window.wsLiveData ? window.wsLiveData[rec.id] : null;
        return (e && !e.__sipMirror) ? e : null;
    }

    /* Copy a host-owned wsLiveData entry into our record when its
       lastUpdated stamp changed. Returns true when something was synced. */
    function syncFromHost(rec, e, now) {
        if (!rec || _replayOn) return false;
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
    // 'RR' / 'DR' / 'HR' / 'HHR' removed: they are SEPARATE relays, not nicknames for
    // RECR / DECR / HECR / HHECR. When an asset reports both (e.g. HECR=0 and HR=1) the
    // alias lookup returned the wrong relay's state and painted a phantom SINGLE_YELLOW
    // over the correct GREEN. The spaced spellings (e.g. 'R E CR', 'HE CR') are kept -
    // they are genuine variants of the real relay names. Matches host isSignalRelayPickup
    // in telemetrylive.js and e7mriv2web parity.
    var RELAY_ALIASES = {
        RECR: ['RECR', 'RCR', 'RED CR', 'R E CR'],
        DECR: ['DECR', 'DCR', 'DE CR'],
        HECR: ['HECR', 'HCR', 'HE CR'],
        HHECR: ['HHECR', 'HHCR', 'HHE CR']
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
        /* S-35 two-aspect resolver + RG fail-safe — PARITY with the host
           computeSignalState (telemetrylive.js). In SIP-only mode the host
           store is empty so resolveSignalState falls back here; without this,
           a 2-aspect RG+HG head whose RG mA is energised but server-flagged
           IsFresh=false (and no RECR) would render dark in the schematic while
           the Signal Card / Live view shows RED. Runs only when nothing above
           resolved, so it can never override a real aspect. */
        if (!chosen) {
            var rgOn = !!(lamps.RG && lamps.RG.Value > thr);   // energised RG current (ignores fresh flag)
            var hgOn = !!(lamps.HG && lamps.HG.Value > thr);
            var rgEnergised = rgOn || recr;                    // current OR relay pickup
            var hgEnergised = hgOn || hecr;
            if (lamps.RG && lamps.HG && !lamps.DG && !lamps.HHG) {   // 2-aspect head
                if (rgEnergised) chosen = { aspect: 'RED', p: 1, v: 0 };
                else if (hgEnergised) chosen = { aspect: 'SINGLE_YELLOW', p: 3, v: 0 };
            }
            if (!chosen && rgEnergised) chosen = { aspect: 'RED', p: 1, v: 0 };   // any head: energised RG never dark
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
        '.sip-highlight{filter:drop-shadow(0 0 4px rgba(59,201,219,.95)) drop-shadow(0 0 10px rgba(59,201,219,.55));}' +
        '.sip-stale{opacity:.5;}' +
        '.sip-pm-gap-layer{pointer-events:none;}' +
        /* v618.14: failure (alertTypeId 2) flashing during replay */
        '.sip-fail-flash{animation:sipFailBlink .9s steps(2,jump-none) infinite;filter:drop-shadow(0 0 5px rgba(255,59,48,.95)) drop-shadow(0 0 12px rgba(255,59,48,.6));}' +
        '@keyframes sipFailBlink{50%{opacity:.28;}}' +
        '@media (prefers-reduced-motion:reduce){.sip-fail-flash{animation:none;}}' +
        '.sip-alert-flash{animation:sipAlertPulse 1.2s ease-in-out infinite;filter:drop-shadow(0 0 8px var(--sip-alert-color,#7366ff));}' +
        '@keyframes sipAlertPulse{0%,100%{opacity:1;}50%{opacity:.55;filter:drop-shadow(0 0 2px transparent);}}';

    function cellClass(c) {
        var cls = 'sip-live-cell';
        if (state.highlight) {
            var lbl = getCellLabel(c).toLowerCase();
            if (lbl.indexOf(state.highlight.toLowerCase()) !== -1) cls += ' sip-highlight';
            else if (!state.highlightMarkOnly) cls += ' sip-dim';
        }
        if (c._sipStale) cls += ' sip-stale';
        if (c._sipFail) cls += ' sip-fail-flash';
        if (c._sipAlert && !_replayOn) cls += ' sip-alert-flash';   // v618.0: no live alert flash over a replay
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
        if (PM_TYPES[c.type]) patchGap(c);
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

    /* v618.0: point gap (unset leg) overlay, patched in place per PM cell. */
    function patchGap(c) {
        var g = state.domGap[c.id];
        if (!g || typeof SIP.renderPointGap !== 'function') return;
        g.innerHTML = SIP.renderPointGap(c);
    }

    function renderAll() {
        try { renderStationBar(); } catch (e0) { /* v618.15 station bar is optional */ }
        if (!canvasEl) return;
        if (!state.cells.length) { renderPlaceholder('No cells to render.'); return; }
        state.dirty = {};
        state.diag.fullRenders++;
        var railLayer = typeof SIP.renderRailLayer === 'function' ? SIP.renderRailLayer(state.cells) : '';
        var standLayer = typeof SIP.renderStandLayer === 'function' ? SIP.renderStandLayer(state.cells) : '';
        var breakerLayer = typeof SIP.renderBreakerLayer === 'function' ? SIP.renderBreakerLayer(state.cells) : '';
        var gapLayer = typeof SIP.renderPointGapLayer === 'function' ? SIP.renderPointGapLayer(state.cells) : '';
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
            '<defs><style>' + SVG_STYLE + '</style></defs>' +
            (typeof SIP.renderBackground === 'function' ? SIP.renderBackground() : '<rect width="100%" height="100%" fill="#0E1828"/>') +
            '<g class="sip-content">' +
            railLayer + standLayer + breakerLayer + parts.join('') + gapLayer +
            '</g>' +
            '</svg>';
        indexDom();
        ensureFit();
    }
    function indexDom() {
        state.domCells = {}; state.domRail = {}; state.domGap = {};
        state.svgEl = canvasEl ? canvasEl.querySelector('svg') : null;
        if (!state.svgEl) return;
        var gs = state.svgEl.querySelectorAll('g.sip-live-cell[data-cell-id]');
        for (var i = 0; i < gs.length; i++) state.domCells[gs[i].getAttribute('data-cell-id')] = gs[i];
        var rs = state.svgEl.querySelectorAll('[data-rail-cell]');
        for (var j = 0; j < rs.length; j++) state.domRail[rs[j].getAttribute('data-rail-cell')] = rs[j];
        var gs2 = state.svgEl.querySelectorAll('[data-pm-gap]');
        for (var k = 0; k < gs2.length; k++) state.domGap[gs2[k].getAttribute('data-pm-gap')] = gs2[k];
    }
    /* =========================================================================
       VIEWER FIT + ZOOM / PAN
       Fit-to-content on load (no wasted band at the top), plus wheel / drag /
       button zoom. Works purely on the SVG viewBox, so live DOM patches are
       untouched and the current zoom/pan survives every re-render.
       ========================================================================= */
    var SIP_MAX_ZOOM = 12;   // max magnification over the fit view
    var SIP_MIN_ZOOM = 1;    // fit is the most zoomed-out view (never over-zoom out)

    /* Fallback bbox from cell positions when getBBox is unavailable. */
    function cellsBBox() {
        var cells = state.cells;
        if (!cells || !cells.length) return null;
        var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (var i = 0; i < cells.length; i++) {
            var c = cells[i];
            var x = (c.position && c.position.x) || 0, y = (c.position && c.position.y) || 0;
            var w = (c.size && c.size.width) || 60, h = (c.size && c.size.height) || 60;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w; if (y + h > maxY) maxY = y + h;
        }
        if (minX === Infinity) return null;
        var p = 30;
        return { x: minX - p, y: minY - p, w: (maxX - minX) + 2 * p, h: (maxY - minY) + 2 * p };
    }

    /* True drawn bounds of the schematic (excludes the full-bleed background
       rect, which lives outside g.sip-content), so an empty band saved into the
       layout can't leave dead space. Falls back to the cell-position bbox. */
    function contentBBox() {
        if (state.svgEl) {
            var g = state.svgEl.querySelector('g.sip-content');
            if (g && g.getBBox) {
                try {
                    var b = g.getBBox();
                    if (b && b.width > 1 && b.height > 1) {
                        var pad = Math.max(18, Math.min(b.width, b.height) * 0.04);
                        return { x: b.x - pad, y: b.y - pad, w: b.width + 2 * pad, h: b.height + 2 * pad };
                    }
                } catch (e) { /* not laid out yet */ }
            }
        }
        return cellsBBox();
    }

    /* Content bbox expanded on its short axis to match the canvas aspect ratio,
       so preserveAspectRatio="xMidYMid meet" fills the canvas with NO letterbox
       and the yard is centred with balanced margins. */
    function computeFitView() {
        var bb = contentBBox();
        if (!bb || !canvasEl) return null;
        var rect = canvasEl.getBoundingClientRect();
        var cw = rect.width || 1, ch = rect.height || 1;
        var canvasAR = cw / ch, boxAR = bb.w / bb.h;
        var x = bb.x, y = bb.y, w = bb.w, h = bb.h;
        if (boxAR > canvasAR) { var nh = bb.w / canvasAR; y -= (nh - h) / 2; h = nh; }
        else { var nw = bb.h * canvasAR; x -= (nw - w) / 2; w = nw; }
        return { x: x, y: y, w: w, h: h };
    }

    function applyViewBoxOnly() {
        if (!state.view) return;
        var v = state.view;
        state.viewBox = v.x + ' ' + v.y + ' ' + v.w + ' ' + v.h;
        if (state.svgEl) state.svgEl.setAttribute('viewBox', state.viewBox);
    }

    /* Establish the fit view once per load / resize; otherwise keep the user's
       current zoom/pan across re-renders. Called at the end of renderAll. */
    function ensureFit() {
        if (!canvasEl) return;
        if (state.needFit || !state.view) {
            var fv = computeFitView();
            if (!fv) return;
            state.fit = fv;
            state.view = { x: fv.x, y: fv.y, w: fv.w, h: fv.h };
            state.needFit = false;
        }
        applyViewBoxOnly();
    }

    function fitView() { state.needFit = true; ensureFit(); }

    /* Exact client→SVG mapping (handles any preserveAspectRatio). */
    function clientToSvg(cx, cy) {
        var svg = state.svgEl;
        if (svg && svg.getScreenCTM) {
            try {
                var m = svg.getScreenCTM();
                if (m) { var pt = svg.createSVGPoint(); pt.x = cx; pt.y = cy; var q = pt.matrixTransform(m.inverse()); return { x: q.x, y: q.y }; }
            } catch (e) { }
        }
        var rect = canvasEl.getBoundingClientRect(), v = state.view;
        return { x: v.x + (cx - rect.left) / (rect.width || 1) * v.w, y: v.y + (cy - rect.top) / (rect.height || 1) * v.h };
    }

    /* Keep part of the yard on-screen so it can't be panned into the void. */
    function clampView() {
        if (!state.view || !state.fit) return;
        var v = state.view, f = state.fit, m;
        if (v.x > f.x + f.w - (m = v.w * 0.2)) v.x = f.x + f.w - m;
        if (v.x + v.w < f.x + (m = v.w * 0.2)) v.x = f.x + m - v.w;
        if (v.y > f.y + f.h - (m = v.h * 0.2)) v.y = f.y + f.h - m;
        if (v.y + v.h < f.y + (m = v.h * 0.2)) v.y = f.y + m - v.h;
    }

    function zoomAt(factor, cx, cy) {
        if (!state.view || !state.fit) return;
        var v = state.view, fit = state.fit;
        var curScale = fit.w / v.w;
        var newScale = Math.max(SIP_MIN_ZOOM, Math.min(SIP_MAX_ZOOM, curScale * factor));
        if (Math.abs(newScale - curScale) < 1e-4) return;
        var nw = fit.w / newScale, nh = fit.h / newScale;
        var p = clientToSvg(cx, cy);
        var fx = (p.x - v.x) / v.w, fy = (p.y - v.y) / v.h;
        state.view = { x: p.x - fx * nw, y: p.y - fy * nh, w: nw, h: nh };
        clampView();
        applyViewBoxOnly();
    }

    function zoomByCenter(factor) {
        if (!canvasEl) return;
        var rect = canvasEl.getBoundingClientRect();
        zoomAt(factor, rect.left + rect.width / 2, rect.top + rect.height / 2);
    }

    function wireZoomPan() {
        if (!canvasEl || canvasEl._sipZoomPanBound) return;
        canvasEl._sipZoomPanBound = true;

        canvasEl.addEventListener('wheel', function (e) {
            if (!state.view || !isViewerFullscreen()) return;   // zoom only in full screen
            if (e.target && e.target.closest && e.target.closest('.sip-insp, .sip-replay')) return; // let overlays scroll
            e.preventDefault();
            zoomAt(e.deltaY < 0 ? 1.12 : 1 / 1.12, e.clientX, e.clientY);
        }, { passive: false });

        var pan = null;
        canvasEl.addEventListener('pointerdown', function (e) {
            if (!state.view || !isViewerFullscreen()) return;   // pan only in full screen
            if (e.pointerType === 'mouse' && e.button !== 0) return;
            if (e.target && e.target.closest && e.target.closest('button, .sip-insp, .sip-replay, #wsStatus, a, input, select')) return;
            pan = { x0: e.clientX, y0: e.clientY, vx: state.view.x, vy: state.view.y, moved: false, id: e.pointerId };
        }, false);

        canvasEl.addEventListener('pointermove', function (e) {
            if (!pan) return;
            var dx = e.clientX - pan.x0, dy = e.clientY - pan.y0;
            if (!pan.moved && Math.abs(dx) < 5 && Math.abs(dy) < 5) return;
            if (!pan.moved) { pan.moved = true; assetClickDown = null; try { canvasEl.setPointerCapture(pan.id); } catch (e2) { } canvasEl.style.cursor = 'grabbing'; }
            var rect = canvasEl.getBoundingClientRect(), v = state.view;
            state.view = { x: pan.vx - dx / (rect.width || 1) * v.w, y: pan.vy - dy / (rect.height || 1) * v.h, w: v.w, h: v.h };
            clampView();
            applyViewBoxOnly();
        }, false);

        function endPan() {
            if (pan) { try { canvasEl.releasePointerCapture(pan.id); } catch (e2) { } }
            pan = null;
            if (canvasEl) canvasEl.style.cursor = '';
        }
        canvasEl.addEventListener('pointerup', endPan, false);
        canvasEl.addEventListener('pointercancel', endPan, false);
    }

    /* True only while the SIP schematic is shown full screen (card mode or the
       activate() fallback). Zoom/pan are enabled only here. */
    function isViewerFullscreen() {
        var card = document.getElementById('sipCardSection') || document.querySelector('section.sip-card, .sip-card');
        if (card && card.classList.contains('fullscreen')) return true;
        var wrap = document.getElementById('sipTelWrap');
        if (wrap && wrap.classList.contains('sip-tel-fs')) return true;
        return false;
    }

    /* Show / hide the zoom controls — only visible in full screen. */
    function updateZoomControlsVisibility() {
        if (state._zoomBar) state._zoomBar.style.display = isViewerFullscreen() ? 'flex' : 'none';
    }

    /* Floating zoom controls (minus / fit / plus). Appended to the canvas HOST
       so they survive every re-render. Hidden unless the schematic is full
       screen (per request: zoom works only in full screen). */
    function installViewerZoomControls() {
        var host = canvasEl && canvasEl.parentNode;
        if (!host || host._sipZoomCtrls) return;
        host._sipZoomCtrls = true;
        var cs = window.getComputedStyle(host);
        if (cs && cs.position === 'static') host.style.position = 'relative';
        if (!document.getElementById('sipZoomCtrlCss')) {
            var zs = document.createElement('style'); zs.id = 'sipZoomCtrlCss';
            zs.textContent = '.sip-zoom-ctrls button:hover{background:rgba(255,255,255,0.10);color:#fff;}' +
                '.sip-zoom-ctrls button:active{transform:scale(0.93);}' +
                '.sip-zoom-ctrls .sip-zoom-sep{width:1px;height:18px;background:rgba(148,163,184,0.25);}';
            document.head.appendChild(zs);
        }
        var bar = document.createElement('div');
        bar.className = 'sip-zoom-ctrls';
        bar.style.cssText = 'position:absolute;bottom:16px;right:16px;z-index:40;display:none;align-items:center;gap:1px;' +
            'background:rgba(17,24,39,0.72);border:1px solid rgba(148,163,184,0.22);border-radius:10px;padding:3px;' +
            'box-shadow:0 2px 10px rgba(0,0,0,0.28);';
        var bs = 'background:transparent;color:#cbd5e1;border:none;border-radius:7px;width:34px;height:34px;cursor:pointer;' +
            'font-size:13px;display:inline-flex;align-items:center;justify-content:center;transition:background .12s,color .12s,transform .08s;';
        bar.innerHTML =
            '<button type="button" id="sipZoomOut" title="Zoom out" aria-label="Zoom out" style="' + bs + '"><i class="fas fa-minus"></i></button>' +
            '<span class="sip-zoom-sep"></span>' +
            '<button type="button" id="sipZoomFit" title="Fit to screen" aria-label="Fit to screen" style="' + bs + '"><i class="fas fa-compress-arrows-alt"></i></button>' +
            '<span class="sip-zoom-sep"></span>' +
            '<button type="button" id="sipZoomIn" title="Zoom in" aria-label="Zoom in" style="' + bs + '"><i class="fas fa-plus"></i></button>';
        host.appendChild(bar);
        state._zoomBar = bar;
        var g = function (id) { return document.getElementById(id); };
        if (g('sipZoomOut')) g('sipZoomOut').addEventListener('click', function () { zoomByCenter(1 / 1.25); });
        if (g('sipZoomFit')) g('sipZoomFit').addEventListener('click', function () { fitView(); });
        if (g('sipZoomIn')) g('sipZoomIn').addEventListener('click', function () { zoomByCenter(1.25); });
        updateZoomControlsVisibility();
    }

    /* Wire zoom/pan + floating controls + a resize re-fit, for whichever host
       mode is active (card mode or the activate() fallback). */
    function wireViewerInteractions() {
        wireZoomPan();
        installViewerZoomControls();
        if (!state._onResize) {
            state._onResize = function () { state.needFit = true; clearTimeout(state._fitT); state._fitT = setTimeout(ensureFit, 120); };
            window.addEventListener('resize', state._onResize);
        }
    }

    function renderPlaceholder(msg) {
        if (!canvasEl) return;
        state.svgEl = null; state.domCells = {}; state.domRail = {}; state.domGap = {};
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
    function highlight(query, markOnly) {
        state.highlight = (query || '').trim();
        state.highlightMarkOnly = !!markOnly;   /* v618.3: replay selection marks, never dims the yard */
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
       SECTION 11 -- REPLAY MODE (v618.0)
       -------------------------------------------------------------------------
       sip-replay.js drives this. begin() parks the live store, end() restores
       it and re-syncs from the shared live data. While on, feedItems only
       accepts replay items (fromReplay = true) and the host is isolated.
       ========================================================================= */
    function replayClearStore() {
        state.assets = {};
        state.assetValues = {};
        state.pmLast = {};
        clearPmBlinkTimers();
        sanitizeLiveBaseline(state.cells);
        state.needFullRender = true;
        requestRender();
    }
    function replayBegin() {
        if (_replayOn) { replayClearStore(); return true; }
        if (!state.cells.length) return false;
        _replaySaved = { assets: state.assets, assetValues: state.assetValues };
        _replayOn = true;
        replayClearStore();
        setStatus('Replay', 'replay');
        return true;
    }
    /* v618.14: mark the cells of these asset names as FAILED (flashing).
       Only touches the class attribute of changed cells. */
    var _failSet = {};
    function replaySetFaults(names) {
        var want = {};
        for (var i = 0; i < (names || []).length; i++) {
            var cs = findCells(names[i]);
            for (var j = 0; j < cs.length; j++) want[cs[j].id] = cs[j];
        }
        var k, c, g;
        for (k in _failSet) {
            if (!_failSet.hasOwnProperty(k) || want[k]) continue;
            c = _failSet[k];
            c._sipFail = false;
            g = state.domCells[c.id];
            if (g) g.setAttribute('class', cellClass(c));
        }
        for (k in want) {
            if (!want.hasOwnProperty(k) || _failSet[k]) continue;
            c = want[k];
            c._sipFail = true;
            g = state.domCells[c.id];
            if (g) g.setAttribute('class', cellClass(c));
        }
        _failSet = want;
    }
    function replayEnd(silent) {
        if (!_replayOn) return;
        replaySetFaults([]);
        _replayOn = false;
        if (_replaySaved) {
            state.assets = _replaySaved.assets || {};
            state.assetValues = _replaySaved.assetValues || {};
        }
        _replaySaved = null;
        state.pmLast = {};
        clearPmBlinkTimers();
        sanitizeLiveBaseline(state.cells);
        reevalAll(false);
        resyncFromShared();
        state.needFullRender = true;
        requestRender();
        if (!silent) setStatus('Live', 'ok');
        if (window.SipReplay && typeof window.SipReplay.onReplayEnded === 'function') {
            try { window.SipReplay.onReplayEnded(); } catch (e) { /* ignore */ }
        }
    }
    function replayFeed(items) {
        if (!_replayOn || !items || !items.length) return;
        feedItems(items, true);
    }
    var replayApi = {
        begin: replayBegin,
        end: function () { replayEnd(false); },
        reset: function () { if (_replayOn) replayClearStore(); },
        feed: replayFeed,
        isOn: function () { return _replayOn; },
        siteId: function () { return state.siteId; },
        cells: function () { return state.cells; },
        findCells: findCells,
        assetNameOf: getCellAssetName,
        record: function (name) { return state.assets[name] || null; },
        setStatus: setStatus,
        setFaults: replaySetFaults
    };

    /* =========================================================================
       BOOT + PUBLIC API
       ========================================================================= */
    function disconnect() {
        state.gen++;
        closeSockets();
        state.siteId = null;
        state.loading = false;
    }
    /* v618.3 -- SIP palette: user choice (Auto / Day / Control room / EI VDU),
       kept in localStorage 'sipTheme'. Auto = Day when the page around the
       schematic is light, Control room when it is dark (measured from the
       actual background colour, so it works with any theme attribute).
       window.SIP_THEME, when set by a page, wins. */
    var THEME_KEY = 'sipTheme';
    var THEME_LABELS = { auto: 'Auto', day: 'Day', night: 'Control room', vdu: 'EI VDU' };
    function themePref() {
        try { var v = window.localStorage.getItem(THEME_KEY); if (THEME_LABELS[v]) return v; } catch (e) { /* storage blocked */ }
        return 'auto';
    }
    function bgLuminance(el) {
        while (el && el.nodeType === 1) {
            var c = window.getComputedStyle(el).backgroundColor || '';
            var m = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/.exec(c);
            if (m && (m[4] === undefined || parseFloat(m[4]) > 0.5)) {
                return (0.2126 * m[1] + 0.7152 * m[2] + 0.0722 * m[3]) / 255;
            }
            el = el.parentNode;
        }
        return 1;
    }
    function pageIsLight() {
        var card = document.getElementById('sipCardSection') || document.body;
        return bgLuminance(card) > 0.5;
    }
    function appSipTheme() {
        if (window.SIP_THEME) return window.SIP_THEME;
        var p = themePref();
        if (p !== 'auto') return p;
        return pageIsLight() ? 'day' : 'night';
    }
    var _lastPageLight = null;
    function syncAppTheme(force) {
        if (typeof SIP.setTheme !== 'function') return;
        /* v618.17: when the page itself switches Light <-> Dark, the schematic
           follows it again (a palette picked earlier is dropped), so every SIP
           part -- yard, legend, station bar, replay, asset popup -- matches. */
        var lightNow = pageIsLight();
        if (_lastPageLight !== null && lightNow !== _lastPageLight) {
            try { window.localStorage.removeItem(THEME_KEY); } catch (e0) { /* storage blocked */ }
            force = true;
        }
        _lastPageLight = lightNow;
        var want = appSipTheme();
        var light = lightNow;
        document.documentElement.setAttribute('data-sip-page', light ? 'light' : 'dark');
        if (!force && typeof SIP.themeName === 'function' && SIP.themeName() === want) {
            try { window.dispatchEvent(new CustomEvent('sip-theme', { detail: { theme: want, pageLight: light } })); } catch (e) { /* old browser */ }
            return;
        }
        SIP.setTheme(want);
        if (canvasEl) {
            canvasEl.setAttribute('data-sip-theme', want);
            canvasEl.style.background = SIP._theme && SIP._theme.canvas ? SIP._theme.canvas : '';
        }
        if (state.cells.length) {
            state.needFullRender = true;
            requestRender();
        }
        paintThemeUi();
        try { window.dispatchEvent(new CustomEvent('sip-theme', { detail: { theme: want, pageLight: light } })); } catch (e2) { /* old browser */ }
    }
    function setThemePref(name) {
        try { window.localStorage.setItem(THEME_KEY, THEME_LABELS[name] ? name : 'auto'); } catch (e) { /* storage blocked */ }
        syncAppTheme(true);
    }
    /* v618.4 -- palette as a segmented control (Control room | EI VDU | Day,
       like the SL1 SIP prototype) + a legend row under the schematic. Until
       the user picks one, the auto choice is shown as selected. */
    var SEG_ORDER = ['night', 'vdu', 'day'];
    var SIP_UI_CSS = '' +
        '.sip-seg{display:inline-flex;padding:2px;border-radius:9px;background:rgba(127,140,160,.18);gap:2px;}' +
        '.sip-seg button{height:26px;padding:0 10px;border:none;border-radius:7px;background:transparent;color:inherit;' +
        'font:inherit;font-size:12px;cursor:pointer;white-space:nowrap;opacity:.8;}' +
        '.sip-seg button:hover{opacity:1;}' +
        '.sip-seg button[aria-pressed="true"]{background:#3BC9DB;color:#04161A;font-weight:600;opacity:1;}' +
        '.sip-legend2{display:flex;flex-wrap:wrap;align-items:center;gap:4px 16px;padding:6px 6px 2px;font-size:12px;}' +
        '.sip-legend2 span{display:inline-flex;align-items:center;gap:6px;white-space:nowrap;}' +
        '.sip-legend2 i{display:inline-block;width:22px;height:5px;border-radius:3px;}' +
        '.sip-legend2 i.dot{width:9px;height:9px;border-radius:50%;}' +
        '.sip-legend2 i.sip-lg-fail{box-shadow:0 0 6px rgba(255,59,48,.9);animation:sipFailBlink .9s steps(2,jump-none) infinite;}' +
        '.sip-card:not(.fullscreen) .sip-legend2{display:none;}';
    function ensureSipUiCss() {
        if (document.getElementById('sipUiCss')) return;
        var st = document.createElement('style');
        st.id = 'sipUiCss';
        st.textContent = SIP_UI_CSS;
        document.head.appendChild(st);
    }
    function ensureThemeSelect() {
        var ctr = document.getElementById('sipControls');
        if (!ctr || document.getElementById('sipThemeSeg')) return;
        ensureSipUiCss();
        var seg = document.createElement('div');
        seg.id = 'sipThemeSeg';
        seg.className = 'sip-seg';
        seg.setAttribute('role', 'group');
        seg.setAttribute('aria-label', 'Schematic colours');
        for (var i = 0; i < SEG_ORDER.length; i++) {
            var b = document.createElement('button');
            b.type = 'button';
            b.setAttribute('data-sip-theme', SEG_ORDER[i]);
            b.textContent = THEME_LABELS[SEG_ORDER[i]];
            seg.appendChild(b);
        }
        seg.addEventListener('click', function (e) {
            e.stopPropagation();
            var t = e.target.closest ? e.target.closest('button[data-sip-theme]') : null;
            if (t) setThemePref(t.getAttribute('data-sip-theme'));
        });
        ctr.insertBefore(seg, ctr.firstChild);
        ensureLegend();
        paintThemeUi();
    }
    function ensureLegend() {
        if (document.getElementById('sipLegend2')) return;
        var canvas = document.getElementById('sipCanvas');
        if (!canvas || !canvas.parentNode) return;
        var lg = document.createElement('div');
        lg.id = 'sipLegend2';
        lg.className = 'sip-legend2';
        canvas.parentNode.insertBefore(lg, canvas.nextSibling);
    }
    /* v618.15 -- STATION NAME BAR (EI VDU style: "< DN [ STATION | CODE ] UP >")
       above the yard. Name / code / zone / division from GetSiteById (the
       page's cached fetchSiteDetails), falling back to the site dropdown text
       "NAME - CODE". Clicking DN / UP swaps the sides (kept per site). */
    var STN_CSS = '' +
        '.sip-stn{display:flex;align-items:center;justify-content:center;gap:14px;padding:8px 10px 6px;flex:none;}' +
        '.sip-stn-dir{display:inline-flex;align-items:center;gap:6px;border:none;background:transparent;cursor:pointer;' +
        'font:700 15px/1 "Barlow Condensed","Arial Narrow",system-ui,sans-serif;letter-spacing:.06em;padding:4px 6px;}' +
        '.sip-stn-dir i{font-style:normal;font-size:18px;}' +
        '.sip-stn-box{display:inline-flex;align-items:center;gap:10px;padding:5px 18px;border:2px solid;border-radius:3px;' +
        'font:700 20px/1 "Barlow Condensed","Arial Narrow",system-ui,sans-serif;letter-spacing:.22em;text-transform:uppercase;}' +
        '.sip-stn-box .cd{font-size:15px;letter-spacing:.12em;padding-left:10px;border-left:1px solid;opacity:.85;}' +
        '.sip-stn-sub{font:600 11px/1 system-ui,sans-serif;letter-spacing:.08em;text-transform:uppercase;opacity:.7;}' +
        '.sip-card:not(.fullscreen) .sip-stn{padding:4px 6px 2px;gap:8px;}' +
        '.sip-card:not(.fullscreen) .sip-stn-box{font-size:13px;padding:3px 10px;letter-spacing:.16em;}' +
        '.sip-card:not(.fullscreen) .sip-stn-box .cd{font-size:11px;}' +
        '.sip-card:not(.fullscreen) .sip-stn-dir{font-size:11px;} .sip-card:not(.fullscreen) .sip-stn-sub{display:none;}';
    var _stn = { siteId: null, name: '', code: '', sub: '' };
    function stnSide() {
        try { return window.localStorage.getItem('sipUpSide_' + (state.siteId || '')) === 'left' ? 'left' : 'right'; } catch (e) { return 'right'; }
    }
    function renderStationBar() {
        var canvas = document.getElementById('sipCanvas');
        if (!canvas || !canvas.parentNode) return;
        if (!document.getElementById('sipStnCss')) {
            var st = document.createElement('style');
            st.id = 'sipStnCss';
            st.textContent = STN_CSS;
            document.head.appendChild(st);
        }
        var bar = document.getElementById('sipStationBar');
        if (!bar) {
            bar = document.createElement('div');
            bar.id = 'sipStationBar';
            bar.className = 'sip-stn';
            bar.setAttribute('aria-label', 'Station');
            canvas.parentNode.insertBefore(bar, canvas);
            bar.addEventListener('click', function (e) {
                if (!e.target.closest || !e.target.closest('.sip-stn-dir')) return;
                e.stopPropagation();
                try { window.localStorage.setItem('sipUpSide_' + (state.siteId || ''), stnSide() === 'left' ? 'right' : 'left'); } catch (x) { /* ignore */ }
                renderStationBar();
            });
        }
        var sid = String(state.siteId || (siteSelectEl && siteSelectEl.value) || '');
        if (sid && _stn.siteId !== sid) {
            _stn.siteId = sid;
            var txt = siteSelectEl && siteSelectEl.options && siteSelectEl.selectedIndex >= 0 ? String(siteSelectEl.options[siteSelectEl.selectedIndex].text || '') : '';
            var parts = txt.split(' - ');
            _stn.name = (parts[0] || '').trim();
            _stn.code = (parts[1] || '').trim();
            _stn.sub = '';
            if (typeof window.fetchSiteDetails === 'function') {
                window.fetchSiteDetails(sid, function (d) {
                    if (!d || _stn.siteId !== sid) return;
                    _stn.name = String(d.Name || d.SiteName || _stn.name || '').trim();
                    _stn.code = String(d.StationCode || d.Description || _stn.code || '').trim();
                    var z = String(d.ZoneName || d.Zone || '').trim(), dv = String(d.DivisionName || d.Division || '').trim();
                    _stn.sub = [z, dv].filter(function (x) { return x; }).join(' / ');
                    renderStationBar();
                });
            }
        }
        var T = SIP._theme || {};
        bar.style.background = T.canvas || '';
        bar.style.display = _stn.name ? '' : 'none';
        var arrow = T.select || '#3BC9DB', ink = T.labelHot || '#E6EDF5';
        var left = stnSide() === 'left' ? 'UP' : 'DN', right = left === 'UP' ? 'DN' : 'UP';
        bar.innerHTML =
            '<button type="button" class="sip-stn-dir" title="Swap DN / UP" style="color:' + arrow + '"><i>&#9664;</i>' + left + '</button>' +
            '<span class="sip-stn-box" style="color:' + ink + ';border-color:' + ink + '">' +
            '<span class="nm">' + escapeHtml(_stn.name) + '</span>' + (_stn.code ? '<span class="cd" style="border-color:' + ink + '">' + escapeHtml(_stn.code) + '</span>' : '') + '</span>' +
            '<button type="button" class="sip-stn-dir" title="Swap DN / UP" style="color:' + arrow + '">' + right + '<i>&#9654;</i></button>' +
            (_stn.sub ? '<span class="sip-stn-sub" style="color:' + ink + '">' + escapeHtml(_stn.sub) + '</span>' : '');
    }
    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    /* swatches always in the colours of the palette on screen */
    function paintThemeUi() {
        try { renderStationBar(); } catch (e) { /* bar is optional */ }
        var cur = (typeof SIP.themeName === 'function') ? SIP.themeName() : 'night';
        var seg = document.getElementById('sipThemeSeg');
        if (seg) {
            var bs = seg.querySelectorAll('button[data-sip-theme]');
            for (var i = 0; i < bs.length; i++) {
                bs[i].setAttribute('aria-pressed', String(bs[i].getAttribute('data-sip-theme') === cur));
            }
        }
        var lg = document.getElementById('sipLegend2');
        var T = SIP._theme || {};
        if (lg) {
            lg.innerHTML =
                '<span><i style="background:' + T.free + '"></i>Free</span>' +
                '<span><i style="background:' + T.occ + '"></i>Occupied</span>' +
                '<span><i class="dot" style="background:' + T.pmN + '"></i>Point normal</span>' +
                '<span><i class="dot" style="background:' + T.pmR + '"></i><i style="background:' + T.route + ';width:16px"></i>Point reverse</span>' +
                '<span><i class="dot" style="background:' + T.lampR + '"></i><i class="dot" style="background:' + T.lampY + '"></i>' +
                '<i class="dot" style="background:' + T.lampG + '"></i>Signal aspect</span>' +
                '<span><i class="sip-lg-fail" style="background:' + T.occ + '"></i>Failed (flashing)</span>';
        }
    }
    function watchAppTheme() {
        syncAppTheme(true);
        ensureThemeSelect();
        var tries = 0;
        var t = setInterval(function () {
            ensureThemeSelect();
            if (document.getElementById('sipThemeSeg') || ++tries > 60) clearInterval(t);
        }, 1000);
        if (typeof MutationObserver !== 'function') return;
        var pending = null;
        var mo = new MutationObserver(function () {
            if (pending) return;
            pending = setTimeout(function () { pending = null; syncAppTheme(false); }, 60);
        });
        var opts = { attributes: true, attributeFilter: ['data-aurora', 'data-theme', 'class', 'style'] };
        mo.observe(document.documentElement, opts);
        if (document.body) mo.observe(document.body, opts);
        var card = document.getElementById('sipCardSection');
        if (card) mo.observe(card, { attributes: true, attributeFilter: ['class'] });
    }

    function bootSip() {
        init();
        watchAppTheme();
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', bootSip);
    else bootSip();

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
        replay: replayApi,
        syncTheme: syncAppTheme,
        setThemePref: setThemePref,
        themePref: themePref,
        pageIsLight: pageIsLight,
        _state: state
    };
    /* telemetrylive.js FRS-Advanced view calls loadSipLayout(siteId) — no-op when same site. */
    if (typeof window.loadSipLayout !== 'function') window.loadSipLayout = function (siteId) { connectToSite(siteId); };
})();
