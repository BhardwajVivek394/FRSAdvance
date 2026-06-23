/* ==========================================================================
 *  SIP Telemetry — Live View  (rewritten 22-05-2026)
 *  ------------------------------------------------------------------------
 *  Single-file live binding engine for the new SIP3 yard view.
 *
 *  WHAT IT DOES
 *  ────────────
 *  1. Fetches the saved SIP layout from /Telemetry/GetSipView for the current site.
 *  2. Renders it into #sipCanvas using SIP.renderCell() from sip-library.js.
 *  3. Subscribes to live telemetry in BRIDGE MODE — instead of opening a second
 *     WebSocket it monkey-patches window.processItemsInternal so every batch
 *     from the existing telemetrylive.js socket is also piped here.
 *  4. Resolves encoded attribute names (e.g. "01655-PM-02003-Min") to human
 *     names ("A End - NWKR") via window.getAttrDisplayName / userAssetSimpleMap.
 *  5. Mutates cell.attrs in-place, then re-renders via requestAnimationFrame.
 *
 *  ASSET TYPE SUPPORT
 *  ──────────────────
 *  examples.Signal          → attrs.signal.lit + attrs.route.active for main/route/calling lights
 *  examples.Signald90/45/90 → attrs.circle1.fill = lit-colour | OFF_GREY
 *  examples.Track (1-6)     → attrs.path.stroke  = #ff0000 | OFF_GREY; TPR DataLogger 0 = occupied
 *  examples.Track3/4        → attrs.path.fill    = #ff0000 | OFF_GREY
 *  examples.PointMachine(1) → attrs.circle1.fill = NORMAL|REVERSE|PM_OFF
 *  examples.Shaunt(2/3)     → attrs.body.fill    = lit | OFF_GREY
 *  examples.SignalShunt     → attrs.lit           = 'R'|'Y'|'G'|''
 *
 *  WS MESSAGE SHAPE  (from your actual WebSocket feed)
 *  ────────────────────────────────────────────────────
 *  MessageType: "Batch"
 *  Messages: [ "{\"AssetId\":1655,\"AssetName\":\"60\",
 *               \"AssetAttributeId\":2003,
 *               \"AssetAttributeName\":\"01655-PM-02003-Min\",
 *               \"Value\":0.0, \"DataType\":\"PointMachine\", ...}", ... ]
 *
 *  PUBLIC API
 *  ──────────
 *    SipTelemetry.connectToSite(siteId)
 *    SipTelemetry.disconnect()
 *    SipTelemetry.refresh()
 *    SipTelemetry.highlight(query)
 *    SipTelemetry.diagnose()        — call from DevTools console
 *    SipTelemetry.simulate(assetName, attrName, value)
 *    SipTelemetry._state            — full internal state (debug)
 * ========================================================================== */

(function () {
    'use strict';

    /* ── Guard ────────────────────────────────────────────────────────────── */
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

    /* Thresholds — mirrors telemetrylive.js */
    var ZERO_OFFSET_DEFAULT = 5.0;
    var TRACK_OCC_THR = 1.0;
    var PM_IND_THR = 4.5;

    /* Point-machine operate blink — indicator blinks this long after a
       NORMAL ⇆ REVERSE transition is detected from live data. */
    var PM_BLINK_MS = 6000;

    /* ── Console logging ──────────────────────────────────────────────────
       All SIP live-state decisions are logged with the [sip-telemetry]
       prefix. Set window.SIP_DEBUG = false in DevTools to silence. */
    function slog() {
        if (window.SIP_DEBUG === false) return;
        console.log.apply(console, ['[sip-telemetry]'].concat([].slice.call(arguments)));
    }
    function swarn() {
        if (window.SIP_DEBUG === false) return;
        console.warn.apply(console, ['[sip-telemetry]'].concat([].slice.call(arguments)));
    }
    var _warnOnceKeys = {};
    /* warn only once per key — keeps the console readable on 1s streams */
    function swarnOnce(key) {
        if (_warnOnceKeys[key]) return;
        _warnOnceKeys[key] = 1;
        swarn.apply(null, [].slice.call(arguments, 1));
    }
    /* forget previous one-time warnings (e.g. on site change a fresh layout
       deserves fresh diagnostics) — optionally scoped by key prefix */
    function clearWarnOnce(prefix) {
        if (!prefix) { _warnOnceKeys = {}; return; }
        for (var k in _warnOnceKeys) {
            if (_warnOnceKeys.hasOwnProperty(k) && k.indexOf(prefix) === 0) delete _warnOnceKeys[k];
        }
    }

    /* Colours — same palette as sip-library.js */
    var OFF_GREY = '#3c4260';
    var PM_OFF = '#d4d4d4';
    var C_RED = '#FF2E2E';
    var C_YELLOW = '#FFD400';
    var C_GREEN = '#22D142';

    /* ── Asset type maps ─────────────────────────────────────────────────── */
    var COMPOSITE_SIGNAL = {
        'examples.Signal': 1,
        /* NOTE: examples.SignalBackground intentionally excluded — it is a
           decorative container (grey pill) with no live signal state.
           Including it caused phantom attrs.signal.lit writes on background
           cells that the renderer ignores. */
        'examples.SignalShunt': 1
    };
    var LAMP_SIGNAL = {
        'examples.Signald90': 1,   // Red
        'examples.Signal90': 1,   // Green
        'examples.Signal45': 1,   // Yellow
        'examples.Signald45': 1    // Double-yellow
    };
    var TRACK_STROKE = {
        'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1,
        'examples.Track5': 1, 'examples.Track6': 1
    };
    var TRACK_FILL = {
        'examples.Track3': 1, 'examples.Track4': 1
    };
    var PM_TYPES = {
        'examples.PointMachine': 1, 'examples.PointMachine1': 1
    };
    var SHUNT_TYPES = {
        'examples.Shaunt': 1, 'examples.Shaunt2': 1, 'examples.Shaunt3': 1
    };
    var ROUTE_CALLING_TYPES = {
        'examples.RouteCallingSignal': 1
    };
    var BUSBAR_TYPES = {
        'examples.BusBar': 1
    };
    var AXLE_TYPES = {
        'examples.AxleCounter': 1
    };
    var GATE_TYPES = {
        'examples.Gate': 1
    };

    /* ── Lamp type → colour ──────────────────────────────────────────────── */
    var LAMP_COLOUR = {
        'examples.Signald90': C_RED,
        'examples.Signal90': C_GREEN,
        'examples.Signal45': C_YELLOW,
        'examples.Signald45': C_YELLOW
    };

    /* =========================================================================
       INTERNAL STATE
       ========================================================================= */
    var state = {
        siteId: null,
        cells: [],
        byLabel: {},   // label → [cell, ...]
        byPrefix: {},   // prefix → [cell, ...]  (legacy per-lamp)
        viewBox: '0 0 2000 740',
        assetValues: {},   // assetName → { attrName: numericValue }
        zeroOffset: {},   // assetName → threshold
        highlight: '',
        renderPending: false,
        ws: null,
        wsReconnTimer: null,
        wsReconnCount: 0,
        wsHeartTimer: null,
        wsLastMsgAt: 0,
        msgCount: 0,
        bridgeInstalled: false,
        origPII: null,  // original processItemsInternal
        origPBM: null,  // original parseBatchMessages
        diag: {
            recv: 0,
            items: 0,
            hits: 0,
            noMatch: 0,
            recent: [],     // ring-30
            unmatched: []      // ring-20
        },
        simNoEnrich: {},    // assetName -> true; simulation reset skips wsLiveData merge once
        pmLast: {},         // cell.id -> 'NORMAL' | 'REVERSE' (for operate-blink detection)
        lastItemAt: 0,      // ms timestamp of last telemetry item (feed watchdog)
        _watchdog: null,    // interval handle for the site-feed watchdog
        _watchdogSiteId: null
    };

    /* ── DOM refs (resolved in init()) ───────────────────────────────────── */
    var canvasEl, statusEl, siteSelectEl;

    /* ── Asset popup click state ──────────────────────────────────────────── */
    var assetClickDown = null;

    /* =========================================================================
       SECTION 1 — INITIALISATION & HOST DETECTION
       ========================================================================= */

    function init() {
        /* Three host modes:
         *  standalone   — TelemetryLive.cshtml: #sipCanvas, #wsStatus, #siteSelect
         *  integrated   — telemetrylive.js dashboard: #divTelemetryLive, #drpSite
         *  card         — same page but .sip-card section exists                 */
        var sipCard = document.querySelector('section.sip-card, .sip-card');
        var divTL = document.getElementById('divTelemetryLive');
        var drpSite = document.getElementById('drpSite');

        if (sipCard && drpSite) {
            initCardMode(sipCard, drpSite);
        } else if (divTL && drpSite) {
            initIntegratedMode(divTL, drpSite);
        } else {
            initStandaloneMode();
        }
    }

    function initStandaloneMode() {
        canvasEl = document.getElementById('sipCanvas');
        wireCanvasAssetPopupClick();
        statusEl = document.getElementById('wsStatus');
        siteSelectEl = document.getElementById('siteSelect');
        var searchEl = document.getElementById('assetSearch');

        if (siteSelectEl) {
            siteSelectEl.addEventListener('change', function () {
                connectToSite(siteSelectEl.value);
            });
        }
        if (searchEl) {
            searchEl.addEventListener('input', function () {
                highlight(searchEl.value);
            });
        }
        setStatus('Idle — select a site', '');
        renderPlaceholder('Select a site to view live SIP.');
    }

    function initIntegratedMode(divTL, drpSite) {
        siteSelectEl = drpSite;
        canvasEl = null;   // injected on activate

        /* Listen for the site dropdown change that tells us to activate */
        if (!drpSite._sipBound) {
            drpSite.addEventListener('change', function () {
                var sid = drpSite.value;
                if (sid && sid !== '0') activate(divTL, drpSite);
                else deactivate(divTL);
            });
            drpSite._sipBound = true;
        }

        /* Auto-activate if a site is already selected */
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
            || document.querySelector('.sip-card-head .sip-sub')
            || null;

        wireCardFullscreen(sipCard);

        if (!drpSite._sipBound) {
            drpSite.addEventListener('change', function () {
                var sid = drpSite.value;
                if (sid && sid !== '0') connectToSite(sid);
                else disconnect();
            });
            drpSite._sipBound = true;
        }

        setTimeout(function () {
            var sid = drpSite.value;
            if (sid && sid !== '0') connectToSite(sid);
        }, 700);
    }

    function activate(divTL, drpSite) {
        /* Inject our canvas into #divTelemetryLive */
        ['#atCardView', '#atKpiRow', '#trackCardContainer', '.at-table-scroll'].forEach(function (s) {
            var n = document.querySelector(s);
            if (n) n.style.display = 'none';
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
                if (icon) { icon.className = fs ? 'fas fa-compress' : 'fas fa-expand'; }
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
    }

    function wireCardFullscreen(sipCard) {
        var fsBtn = document.getElementById('sipFullscreenBtn');
        if (fsBtn && !fsBtn._sipFsBound) {
            fsBtn.onclick = null;
            fsBtn.addEventListener('click', function () {
                var fs = sipCard.classList.toggle('fullscreen');
                document.body.style.overflow = fs ? 'hidden' : '';
            });
            fsBtn._sipFsBound = true;
        }
        if (!document._sipEscBound) {
            document.addEventListener('keydown', function (e) {
                if (e.key === 'Escape' && sipCard.classList.contains('fullscreen')) {
                    sipCard.classList.remove('fullscreen');
                    document.body.style.overflow = '';
                }
            });
            document._sipEscBound = true;
        }
    }

    /* =========================================================================
       ASSET CLICK → POPUP  (Live SIP View)
       =========================================================================
       This view re-renders SVG with canvasEl.innerHTML on every telemetry update.
       Therefore listeners must be delegated from #sipCanvas, not attached to
       individual SVG nodes.
       ------------------------------------------------------------------------- */

    function wireCanvasAssetPopupClick() {
        if (!canvasEl || canvasEl._sipAssetPopupClickBound) return;

        canvasEl.addEventListener('pointerdown', onSipAssetPointerDown, true);
        canvasEl.addEventListener('pointerup', onSipAssetPointerUp, true);
        canvasEl._sipAssetPopupClickBound = true;

        console.log('[sip-telemetry] asset popup click handler bound on #' + (canvasEl.id || '(canvas)'));
    }

    function onSipAssetPointerDown(evt) {
        var g = findSipLiveCellGroup(evt.target);
        if (!g) {
            assetClickDown = null;
            return;
        }

        assetClickDown = {
            x: evt.clientX,
            y: evt.clientY,
            id: g.getAttribute('data-cell-id') || g.getAttribute('data-id') || '',
            group: g
        };
    }

    function onSipAssetPointerUp(evt) {
        if (!assetClickDown) return;

        var g = findSipLiveCellGroup(evt.target) || assetClickDown.group;
        if (!g) {
            assetClickDown = null;
            return;
        }

        var upId = g.getAttribute('data-cell-id') || g.getAttribute('data-id') || '';
        var moved = Math.abs(evt.clientX - assetClickDown.x) > 6 ||
            Math.abs(evt.clientY - assetClickDown.y) > 6;
        var sameAsset = String(upId) === String(assetClickDown.id);

        assetClickDown = null;

        if (moved || !sameAsset) return;

        var cell = cellByRenderedId(upId);
        if (!cell) {
            console.warn('[sip-telemetry] clicked SVG asset but no matching cell found:', upId);
            return;
        }

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

    function hasClass(node, className) {
        return !!(node && node.classList && node.classList.contains(className));
    }

    function cellByRenderedId(id) {
        if (!id) return null;
        for (var i = 0; i < state.cells.length; i++) {
            if (String(state.cells[i].id) === String(id)) return state.cells[i];
        }
        return null;
    }

    function getCellLabel(cell) {
        return String((cell && cell.attrs && cell.attrs.label && cell.attrs.label.text) || '').trim();
    }

    function getCellAssetName(cell) {
        var label = getCellLabel(cell);
        if (!label) return '';

        if (LAMP_SIGNAL[cell.type] || !state.byLabel[label]) {
            var prefix = label.split(/\s+/)[0];
            if (prefix && state.assetValues[prefix]) return prefix;
        }
        if (state.assetValues[label]) return label;

        /* NEW — normalized reverse lookup: cell "S14" → live asset "S-14" */
        var nk = _normLabel(label);
        for (var an in state.assetValues) {
            if (state.assetValues.hasOwnProperty(an) && _normLabel(an) === nk) return an;
        }
        return label.split(/\s+/)[0] || label;
    }

    /* ═══════════════════════════════════════════════════════════════
       SELF-CONTAINED ASSET POPUP — shows live telemetry on click.
       Creates its own overlay HTML, CSS, and event wiring.
       Zero dependency on sip-asset-popup.js or telemetrylive.js.
       ═══════════════════════════════════════════════════════════════ */
    var _popupEl = null;
    var _popupTimer = null;
    var _popupAssetName = '';

    function ensurePopupDOM() {
        if (_popupEl) return _popupEl;

        /* ── Inject CSS ── */
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

        /* ── Inject HTML ── */
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
            '<div class="stp-foot-r"><button class="stp-btn" id="stpCloseBtn">Close</button></div>' +
            '</div>' +
            '</div>';
        document.body.appendChild(ov);

        /* ── Wire close events ── */
        var closeBtn = document.getElementById('stpClose');
        var closeFoot = document.getElementById('stpCloseBtn');
        function doClose() { ov.classList.remove('open'); clearInterval(_popupTimer); }
        if (closeBtn) closeBtn.addEventListener('click', doClose);
        if (closeFoot) closeFoot.addEventListener('click', doClose);
        ov.addEventListener('click', function (e) { if (e.target === ov) doClose(); });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && ov.classList.contains('open')) doClose();
        });

        _popupEl = ov;
        return ov;
    }

    function fmtVal(raw) {
        if (raw === null || raw === undefined || raw === '') return '--';
        var n = parseFloat(raw);
        if (isNaN(n)) return escHtml(String(raw));
        return n % 1 === 0 ? String(n) : n.toFixed(2);
    }

    function refreshPopupGrid() {
        var grid = document.getElementById('stpGrid');
        if (!grid || !_popupAssetName) return;

        /* Pull live values from state.assetValues or wsLiveData */
        var vals = state.assetValues[_popupAssetName] || {};
        if (!Object.keys(vals).length && window.wsLiveData) {
            for (var id in wsLiveData) {
                if (wsLiveData[id] && wsLiveData[id].AssetName === _popupAssetName && wsLiveData[id].attrs) {
                    var a = wsLiveData[id].attrs;
                    for (var k in a) {
                        if (!a.hasOwnProperty(k)) continue;

                        var obj = a[k];
                        var val = (obj && typeof obj === 'object' && 'Value' in obj) ? obj.Value : obj;
                        var attrId = obj ? (obj.AttrId || obj.AssetAttributeId || obj.AttributeId) : '';
                        var label = resolveAttrName(k, attrId, id, obj && obj.DataType);

                        vals[label] = val;
                    }
                    break;
                }
            }
        }

        var keys = Object.keys(vals);
        /* Respect wsAttributeNames ordering if available */
        if (window.wsAttributeNames && wsAttributeNames.length) {
            var ordered = [];
            wsAttributeNames.forEach(function (n) { if (n in vals) ordered.push(n); });
            keys.forEach(function (k) { if (ordered.indexOf(k) < 0) ordered.push(k); });
            keys = ordered;
        }

        if (!keys.length) { grid.innerHTML = '<div class="stp-empty">No live attribute values received for this asset yet.</div>'; return; }

        var half = Math.ceil(keys.length / 2);
        var renderRow = function (k) {
            var v = fmtVal(vals[k]);
            var cls = 'stp-val';
            if (v === 'Ok' || v === 'ok') return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="stp-val ok">Ok</span></div>';
            var num = parseFloat(v);
            if (!isNaN(num) && num < 0) cls += ' warn';
            return '<div class="stp-row"><span class="stp-lbl">' + escHtml(k) + '</span><span class="' + cls + '">' + escHtml(v) + '</span></div>';
        };
        grid.innerHTML =
            '<div class="stp-col">' + keys.slice(0, half).map(renderRow).join('') + '</div>' +
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

        console.log('[sip-telemetry] SIP asset clicked:', assetName, cell.type);

        /* ── If sip-asset-popup.js provided the full popup, use it ── */
        if (typeof window.SipAssetPopupLive === 'object' && typeof window.SipAssetPopupLive.open === 'function') {
            window.SipAssetPopupLive.open(cell, {
                source: 'sip-telemetry', siteId: state.siteId,
                assetName: assetName, label: label,
                cellId: cell.id, cellType: cell.type, cell: cell,
                liveValues: state.assetValues[assetName] || {}
            });
            return;
        }

        /* ── Otherwise use our built-in popup ── */
        var ov = ensurePopupDOM();
        _popupAssetName = assetName || label || cell.id || '—';

        /* Header */
        var iconEl = document.getElementById('stpIcon');
        var t = cell.type || '';
        if (iconEl) iconEl.innerHTML = t.indexOf('Track') >= 0 ? ICON_SVG.Track : t.indexOf('Signal') >= 0 ? ICON_SVG.Signal : t.indexOf('Point') >= 0 ? ICON_SVG.Point : ICON_SVG.Default;
        var nameEl = document.getElementById('stpName');
        if (nameEl) nameEl.textContent = _popupAssetName;
        var subEl = document.getElementById('stpSub');
        if (subEl) subEl.textContent = 'Site: ' + (state.siteId || '—');
        var typeEl = document.getElementById('stpType');
        if (typeEl) typeEl.textContent = cell.type ? cell.type.replace('examples.', '') : '—';
        var titleEl = document.getElementById('stpTitle');
        if (titleEl) titleEl.textContent = 'Live Telemetry — ' + _popupAssetName;

        /* Grid */
        refreshPopupGrid();

        /* Show */
        ov.classList.add('open');

        /* Auto-refresh every 3s */
        clearInterval(_popupTimer);
        _popupTimer = setInterval(refreshPopupGrid, 3000);

        /* Also fire the old global in case anything else needs it */
        if (typeof window.OpenSipAssetPopupFromCell === 'function') {
            try {
                window.OpenSipAssetPopupFromCell(cell, {
                    source: 'sip-telemetry', siteId: state.siteId,
                    assetName: assetName, label: label
                });
                /* If the external popup opened, close our built-in one */
                var extOv = document.getElementById('sipAssetPopupOverlay');
                if (extOv && (extOv.classList.contains('sap-show') || extOv.style.display === 'flex')) {
                    ov.classList.remove('open');
                    clearInterval(_popupTimer);
                }
            } catch (ex) {
                console.warn('[sip-telemetry] External popup failed, using built-in:', ex.message);
            }
        }
    }


    /* =========================================================================
       SECTION 2 — SITE LOAD + LAYOUT
       ========================================================================= */

    function connectToSite(rawSiteId) {
        var siteId = parseInt(rawSiteId, 10);
        closeSockets();
        removeBridge();

        if (!siteId || isNaN(siteId)) {
            state.siteId = null;
            state.cells = [];
            state.byLabel = {}; state.byPrefix = {};
            setStatus('No site selected', '');
            renderPlaceholder('Select a site to view live SIP.');
            return;
        }

        state.siteId = siteId;
        state.assetValues = {};
        state.simNoEnrich = {};
        state._bufferedAssets = {};
        state.lastItemAt = 0;
        /* fresh site ⇒ fresh diagnostics — old unmatched/no-attr warnings
           belong to the previous layout */
        clearWarnOnce('nomatch:');
        clearWarnOnce('sig-noattr:');
        clearWarnOnce('buffering-preload');
        state.msgCount = 0;
        state.diag.recv = state.diag.items = state.diag.hits = state.diag.noMatch = 0;
        setStatus('Loading layout…', 'loading');

        $.ajax({
            url: '/Telemetry/GetSipView',
            type: 'POST',
            data: JSON.stringify({ siteId: siteId }),
            contentType: 'application/json',
            success: function (data) {
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
                try { layout = JSON.parse(raw); } catch (e) {
                    setStatus('Cannot parse SIP JSON', 'error');
                    renderPlaceholder('Saved SIP JSON is malformed.');
                    return;
                }

                /* Accept both { cells:[…] } and a bare […] array */
                var cells = Array.isArray(layout) ? layout
                    : (layout && Array.isArray(layout.cells)) ? layout.cells
                        : null;

                if (!cells) {
                    setStatus('Unrecognised SIP shape', 'error');
                    renderPlaceholder('Saved SIP has an unrecognised shape.');
                    return;
                }

                state.cells = cells;

                /* ── LIVE BASELINE RESET ─────────────────────────────────
                   The saved layout may carry editor PREVIEW states (e.g. a
                   signal saved with lit='G'). On the live page nothing may
                   appear lit/occupied until real telemetry proves it —
                   otherwise a signal with NO data shows a phantom green. */
                var resetN = sanitizeLiveBaseline(state.cells);
                slog('Layout loaded: ' + state.cells.length + ' cells. ' +
                    'Live baseline applied — ' + resetN + ' cell(s) had editor preview ' +
                    'states cleared (no data ⇒ lamps OFF, tracks grey, PM neutral).');

                state.viewBox = autoViewBox(cells);
                buildIndex();
                render();

                /* Evaluate every snapshot absorbed while the layout was still
                   loading (and anything already merged in wsLiveData) so the
                   schematic paints with live state on FIRST render instead of
                   waiting for the next WS frame per asset.                  */
                var preAssets = Object.keys(state.assetValues).length;
                if (preAssets) {
                    var evalRes = reevalAllAbsorbed();
                    slog('Layout ready — evaluated ' + preAssets + ' asset snapshot(s) buffered during load: ' +
                        evalRes.matched + ' matched cell(s), ' + evalRes.unmatched + ' unmatched.');
                }
                state._bufferedAssets = {};

                startAlertFlash();
                openSocket(siteId);
            },
            error: function () {
                setStatus('Failed to load SIP', 'error');
                renderPlaceholder('Could not reach /Telemetry/GetSipView.');
            }
        });
    }

    /* ── reevalAllAbsorbed ───────────────────────────────────────────────
       Walks every absorbed asset snapshot, enriches from wsLiveData and
       re-evaluates the matching cells. Used right after the layout loads
       so telemetry that raced ahead of GetSipView is applied immediately.
       Unmatched warnings are only meaningful HERE (index exists now).    */
    function reevalAllAbsorbed() {
        var matched = 0, unmatched = 0, dirty = false;
        for (var assetName in state.assetValues) {
            if (!state.assetValues.hasOwnProperty(assetName)) continue;
            var snapshot = state.assetValues[assetName];
            enrichFromWsLiveData(assetName, snapshot);
            var cells = findCells(assetName);
            if (!cells.length) {
                unmatched++;
                pushSample(state.diag.unmatched, assetName, 20);
                swarnOnce('nomatch:' + assetName,
                    'No SIP cell matches asset "' + assetName + '" — telemetry for it is ignored. ' +
                    'Check the cell label spelling in the SIP editor.');
                continue;
            }
            matched++;
            for (var i = 0; i < cells.length; i++) {
                if (reeval(cells[i], assetName, snapshot)) dirty = true;
            }
        }
        if (dirty) requestRender();
        return { matched: matched, unmatched: unmatched };
    }

    /* ── sanitizeLiveBaseline ────────────────────────────────────────────
       Clears every live-driven visual attribute on freshly loaded cells so
       the schematic starts from a truthful "no data yet" state:
         • composite / shunt signals → all lamps OFF
         • route & calling indicators → none active
         • legacy lamp cells → grey
         • tracks → grey (not occupied)
         • point machines → neutral indicator, no blink
       Telemetry then lights things up ONLY when a condition is proven. */
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
                if (c.attrs.route && (c.attrs.route.active || c.attrs.route.activeRoutes)) {
                    c.attrs.route.active = '';
                    c.attrs.route.activeRoutes = '';
                    touched = true;
                }
            } else if (ROUTE_CALLING_TYPES[c.type]) {
                if (c.attrs.route && (c.attrs.route.active || c.attrs.route.activeRoutes)) {
                    c.attrs.route.active = '';
                    c.attrs.route.activeRoutes = '';
                    touched = true;
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

            if (touched) n++;
        }
        return n;
    }

    function autoViewBox(cells) {
        if (!cells.length) return '0 0 2000 740';
        var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        cells.forEach(function (c) {
            var x = (c.position && c.position.x) || 0, y = (c.position && c.position.y) || 0;
            var w = (c.size && c.size.width) || 60, h = (c.size && c.size.height) || 60;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w;
            if (y + h > maxY) maxY = y + h;
        });
        var pad = 80;
        return (minX - pad) + ' ' + (minY - pad) + ' ' + (maxX - minX + 2 * pad) + ' ' + (maxY - minY + 2 * pad);
    }

    /* Build two lookup indexes:
     *  byLabel["S13"]       → [composite signal cell]
     *  byLabel["C-18T"]     → [track cell]
     *  byPrefix["S18"]      → [all lamp cells whose label starts "S18 …"]   */
    function buildIndex() {
        state.byLabel = {};
        state.byPrefix = {};
        state.byNorm = {};        // NEW: normalized label  → cells
        state.byNormPrefix = {};  // NEW: normalized prefix → cells
        state.cells.forEach(function (c) {
            var raw = c.attrs && c.attrs.label && c.attrs.label.text;
            if (raw == null || raw === '') return;
            var lbl = String(raw).trim();
            var prefix = lbl.split(/\s+/)[0];

            (state.byLabel[lbl] = state.byLabel[lbl] || []).push(c);

            var nk = _normLabel(lbl);                                    // "S  2" → "S2"
            if (nk) (state.byNorm[nk] = state.byNorm[nk] || []).push(c);

            if (!COMPOSITE_SIGNAL[c.type] && prefix && prefix !== lbl) {
                (state.byPrefix[prefix] = state.byPrefix[prefix] || []).push(c);
                var np = _normLabel(prefix);
                if (np) (state.byNormPrefix[np] = state.byNormPrefix[np] || []).push(c);
            }
        });
    }

    /* =========================================================================
       SECTION 3 — WEBSOCKET  (Bridge-first, standalone fallback)
       =========================================================================
       BRIDGE MODE: monkey-patch window.processItemsInternal so every batch
       that flows through the existing telemetrylive.js pipeline ALSO feeds
       our telemetry resolver. No second WebSocket is opened.

       STANDALONE MODE: open our own WS when bridge is not available.         */

    function openSocket(siteId) {
        /* ── SITE-SELECTION-DRIVEN FEED ──────────────────────────────────
           The SIP live feed must depend ONLY on the selected site — never
           on the asset-type / Search selection in Telemetry Live.

           Old behaviour: if telemetrylive.js was present, we went
           "bridge-only" and waited for ITS pipeline. But that pipeline only
           runs after the user searches an asset type (e.g. Signal), so the
           SIP stayed silent until a signal was selected.

           New behaviour:
             1. Install the bridge as a SUPPLEMENT (so Search-filtered
                streams also reach the SIP) — never as the only source.
             2. If the host page already holds a SITE-WIDE socket
                (…/{siteId}/all — not asset-type filtered), rely on it:
                its SIP-only gate forwards every frame to applyPayload.
             3. Otherwise open our OWN dedicated site-wide socket NOW —
                no waiting, no dependency on any selection.
             4. A watchdog keeps checking: if telemetry goes silent and the
                host socket disappears or becomes asset-filtered (user
                clicked Search), we open our own socket so tracks/PMs/
                signals on the SIP keep updating.                          */
        tryBridge();
        startFeedWatchdog(siteId);

        if (hostHasSiteWideStream(siteId)) {
            setStatus('Live (host site stream)', 'ok');
            slog('Site ' + siteId + ': host page already streams site-wide WS — SIP fed via its SIP-only gate' +
                (state.bridgeInstalled ? ' + bridge for filtered views.' : '.'));
            return;
        }

        slog('Site ' + siteId + ': no site-wide WS found (host socket ' +
            (window.wsConnection ? 'is asset-filtered: ' + String(window.wsConnection.url || '') : 'absent') +
            ') — opening dedicated SIP socket. Feed is driven purely by site selection.');
        openOwnSocket(siteId);
    }

    /* True when the HOST page (telemetrylive.js) holds an open/connecting
       WebSocket subscribed to the WHOLE site (…/{siteId}/all). An asset-
       type-filtered URL (…/{siteId}/{typeId}/all) or single-asset URL does
       NOT count — those starve the SIP of other asset types.             */
    function hostHasSiteWideStream(siteId) {
        var c = window.wsConnection;
        if (!c) return false;
        if (c.readyState !== 0 && c.readyState !== 1) return false;  // CONNECTING / OPEN only
        var u = String(c.url || '').split('?')[0];
        return new RegExp('/subscribe/liveValue/' + String(siteId) + '/all$').test(u);
    }

    /* ── Feed watchdog ────────────────────────────────────────────────────
       Re-checks every 8s. If no telemetry item has reached the SIP for 20s
       AND we have no own socket AND the host has no site-wide stream
       (missing, closed, or replaced by an asset-filtered URL after Search),
       open the dedicated site-wide socket. Also retries the bridge install
       in case telemetrylive.js loaded late.                              */
    var WATCHDOG_TICK_MS = 8000;
    var WATCHDOG_SILENT_MS = 20000;
    function startFeedWatchdog(siteId) {
        stopFeedWatchdog();
        state._watchdogSiteId = siteId;
        state._watchdog = setInterval(function () {
            tryBridge();   // no-op if already installed
            if (state.ws && (state.ws.readyState === 0 || state.ws.readyState === 1)) return;
            var silentMs = Date.now() - (state.lastItemAt || 0);
            if (silentMs < WATCHDOG_SILENT_MS) return;
            if (hostHasSiteWideStream(siteId)) return;   // site stream exists; data may simply be quiet
            swarn('No SIP telemetry for ' + Math.round(silentMs / 1000) + 's and the host WS is ' +
                (window.wsConnection ? 'asset-filtered (' + String(window.wsConnection.url || '').split('?')[0] + ')' : 'absent') +
                ' — opening dedicated site-wide socket for site ' + siteId + '.');
            openOwnSocket(siteId);
        }, WATCHDOG_TICK_MS);
    }
    function stopFeedWatchdog() {
        if (state._watchdog) { clearInterval(state._watchdog); state._watchdog = null; }
        state._watchdogSiteId = null;
    }

    /* ── Bridge installation ─────────────────────────────────────────────── */
    function tryBridge() {
        if (state.bridgeInstalled) return true;
        var orig = window.processItemsInternal;
        if (typeof orig !== 'function') return false;

        state.origPII = orig;
        // Mark our wrapper so a later tryBridge() call can tell it's already us
        // (defends against accidental double-install if telemetrylive.js
        // reassigns window.processItemsInternal mid-stream).
        var bridgedPII = function (items) {
            orig.apply(this, arguments);
            if (state.cells.length > 0) feedItems(items);
        };
        bridgedPII._sipBridged = true;
        window.processItemsInternal = bridgedPII;

        /* Also hook parseBatchMessages for DataLogger items */
        var origPBM = window.parseBatchMessages;
        if (typeof origPBM === 'function') {
            state.origPBM = origPBM;
            var bridgedPBM = function (messages) {
                origPBM.apply(this, arguments);
                if (state.cells.length > 0 && messages && messages.length) {
                    var dlItems = [];
                    for (var i = 0; i < messages.length; i++) {
                        var m = messages[i];
                        if (typeof m === 'string') { try { m = JSON.parse(m); } catch (e) { continue; } }
                        if (m && m.DataType === 'DataLogger') dlItems.push(m);
                    }
                    if (dlItems.length) feedItems(dlItems);
                }
            };
            bridgedPBM._sipBridged = true;
            window.parseBatchMessages = bridgedPBM;
        }

        state.bridgeInstalled = true;
        return true;
    }

    function removeBridge() {
        if (state._bridgeWaitTimer) {
            clearInterval(state._bridgeWaitTimer);
            state._bridgeWaitTimer = null;
        }
        if (!state.bridgeInstalled) return;
        // Only restore originals if the current window.* is still OUR wrapper.
        // If something else has wrapped us further, leave the chain alone --
        // it'll keep calling our orig anyway.
        if (window.processItemsInternal && window.processItemsInternal._sipBridged &&
            typeof state.origPII === 'function') {
            window.processItemsInternal = state.origPII;
        }
        if (window.parseBatchMessages && window.parseBatchMessages._sipBridged &&
            typeof state.origPBM === 'function') {
            window.parseBatchMessages = state.origPBM;
        }
        state.origPII = state.origPBM = null;
        state.bridgeInstalled = false;
    }

    /* ── Standalone WebSocket ────────────────────────────────────────────── */
    function openOwnSocket(siteId) {
        /* Idempotency — watchdog and openSocket may both call this */
        if (state.ws && (state.ws.readyState === 0 || state.ws.readyState === 1)) {
            return;
        }
        var url = WS_BASE + '/' + siteId + '/all';

        // ── FIX: Upgrade ws:// → wss:// on HTTPS pages ──
        if (window.location.protocol === 'https:' && url.indexOf('ws://') === 0) {
            url = url.replace('ws://', 'wss://');
        }

        // ── FIX: Append auth token for secure connections ──
        var _isSecurePage = (typeof isSecure !== 'undefined') ? isSecure : (window.location.protocol === 'https:');
        if (_isSecurePage && window.APP_CONFIG && window.APP_CONFIG.WebSocketAuthToken) {
            var sep = url.indexOf('?') > -1 ? '&' : '?';
            url = url + sep + 'token=' + encodeURIComponent(window.APP_CONFIG.WebSocketAuthToken);
        }

        console.log('[sip-telemetry] Standalone WS →', url.replace(/token=[^&]+/, 'token=***'));
        setStatus('Connecting…', 'loading');

        try { state.ws = new WebSocket(url); }
        catch (e) {
            console.error('[sip-telemetry] WS failed:', e);
            setStatus('Connection failed', 'error');
            schedReconn(siteId); return;
        }

        state.ws.onopen = function () {
            state.wsReconnCount = 0;
            state.wsLastMsgAt = Date.now();
            setStatus('Live · 0 msgs', 'ok');
            startHeartbeat();
        };

        state.ws.onmessage = function (ev) {
            state.wsLastMsgAt = Date.now();
            var payload;
            try {
                payload = JSON.parse(ev.data);
                if (typeof payload === 'string') payload = JSON.parse(payload);
            } catch (e) { return; }
            applyPayload(payload);
        };

        state.ws.onclose = function () {
            stopHeartbeat();
            if (state.siteId && state.wsReconnCount < MAX_RECONNECT)
                schedReconn(siteId);
            else setStatus('Disconnected', 'error');
        };

        state.ws.onerror = function (e) { console.warn('[sip-telemetry] ws error', e); };
    }

    function schedReconn(siteId) {
        state.wsReconnCount++;
        setStatus('Reconnecting (' + state.wsReconnCount + '/' + MAX_RECONNECT + ')…', 'warn');
        state.wsReconnTimer = setTimeout(function () { openOwnSocket(siteId); }, RECONNECT_MS);
    }

    function startHeartbeat() {
        stopHeartbeat();
        state.wsHeartTimer = setInterval(function () {
            if (Date.now() - state.wsLastMsgAt > HEARTBEAT_MS) {
                console.warn('[sip-telemetry] heartbeat timeout — reconnecting');
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
        if (state.wsReconnTimer) { clearTimeout(state.wsReconnTimer); state.wsReconnTimer = null; }
        stopHeartbeat();
        if (state.ws) {
            state.ws.onopen = state.ws.onmessage = state.ws.onclose = state.ws.onerror = null;
            try { state.ws.close(1000); } catch (e) { }
            state.ws = null;
        }
        state.wsReconnCount = 0;
    }

    /* =========================================================================
       SECTION 4 — TELEMETRY APPLICATION
       ========================================================================= */

    /* Entry point for BOTH bridge and standalone paths.
     * Accepts:  raw array of item objects  OR  the batch envelope object     */
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

    /* feedItems — normalise, resolve attribute names, absorb into snapshots,
     * then re-evaluate every affected cell.                                  */
    function feedItems(items) {
        state.diag.recv++;
        if (!items || !items.length) return;
        state.lastItemAt = Date.now();   // feed watchdog liveness marker

        var touched = {};   // assetName → true

        /* ── PHASE 1: ABSORB ─────────────────────────────────────────────── */
        for (var i = 0; i < items.length; i++) {
            var d = items[i];
            if (!d) continue;
            /* Messages array items may be JSON-stringified (your server does this) */
            if (typeof d === 'string') {
                try { d = JSON.parse(d); } catch (e) { continue; }
                if (typeof d === 'string') {
                    try { d = JSON.parse(d); } catch (e) { continue; }
                }
            }
            if (!d || !d.AssetName) continue;

            var assetName = String(d.AssetName).trim();
            var rawAttr = String(d.AssetAttributeName || '').trim();
            var assetId = d.AssetId;
            var attrId = d.AssetAttributeId || d.EdgeXAttributeId;
            var dataType = String(d.DataType || '').toLowerCase();
            var rawValue = d.Value;

            /* Resolve encoded attribute name → human display name
             * "01655-PM-02003-Min"  →  "A End - NWKR"
             * Uses telemetrylive.js maps when available.                    */
            var attrName = resolveAttrName(rawAttr, attrId, assetId, dataType);

            pushSample(state.diag.recent,
                { assetName: assetName, attr: attrName, rawAttr: rawAttr, value: rawValue }, 30);

            var numVal = parseFloat(rawValue);
            if (isNaN(numVal)) continue;

            var bag = state.assetValues[assetName] || (state.assetValues[assetName] = {});
            setSnapshotValue(bag, attrName, rawAttr, numVal);
            touched[assetName] = true;

            if (d.ZeroOffsetValue != null && !isNaN(parseFloat(d.ZeroOffsetValue)))
                state.zeroOffset[assetName] = parseFloat(d.ZeroOffsetValue);
        }

        /* ── PHASE 2: ENRICH + RE-EVALUATE ──────────────────────────────── */

        /* ── LAYOUT-LOAD RACE GUARD ──────────────────────────────────────
           On site selection the WS stream and the GetSipView layout AJAX
           start in parallel. Frames that land BEFORE the layout returns
           would find an empty cell index and falsely warn "no SIP cell
           matches" for every asset. Their values are already absorbed in
           PHASE 1 (state.assetValues), so just buffer here — loadSip runs
           a full re-evaluation pass the moment the layout is in.          */
        if (!state.cells.length) {
            state._bufferedAssets = state._bufferedAssets || {};
            for (var bn in touched) {
                if (touched.hasOwnProperty(bn)) state._bufferedAssets[bn] = true;
            }
            swarnOnce('buffering-preload',
                'Telemetry is arriving before the SIP layout finished loading — ' +
                'buffering values; they will be evaluated as soon as the layout is in.');
            return;
        }

        var dirty = false;
        var batchHit = 0, batchMiss = 0, batchNoCell = 0;

        for (var assetName in touched) {
            if (!touched.hasOwnProperty(assetName)) continue;
            var snapshot = state.assetValues[assetName];

            /* Enrich snapshot from wsLiveData (bridge mode) — wsLiveData has
             * the full merged history; our snapshot may only have this batch.
             * Simulation with reset=true skips this once so test values render
             * in isolation instead of being overwritten by old wsLiveData. */
            if (state.simNoEnrich && state.simNoEnrich[assetName]) {
                delete state.simNoEnrich[assetName];
            } else {
                enrichFromWsLiveData(assetName, snapshot);
            }

            var cells = findCells(assetName);
            if (!cells.length) {
                batchNoCell++;
                pushSample(state.diag.unmatched, assetName, 20);
                swarnOnce('nomatch:' + assetName,
                    'No SIP cell matches asset "' + assetName + '" — telemetry for it is ignored. ' +
                    'Check the cell label spelling in the SIP editor.');
                continue;
            }

            var changedAny = false;
            for (var ci = 0; ci < cells.length; ci++) {
                if (reeval(cells[ci], assetName, snapshot)) {
                    changedAny = true;
                    dirty = true;
                }
            }
            if (changedAny) batchHit++; else batchMiss++;
        }

        state.diag.items += items.length;
        state.diag.hits += batchHit;
        state.diag.noMatch += batchNoCell;

        if (dirty) {
            state.msgCount++;
            setStatus('Live · ' + state.msgCount + ' upd · ' + state.diag.recv + ' msg', 'ok');
            requestRender();
        } else if (state.diag.noMatch > 10 && state.diag.hits === 0) {
            setStatus('RX ' + state.diag.recv + ' · 0 matched (check labels)', 'warn');
        }
    }
    function setSnapshotValue(snapshot, aliasName, rawName, value) {
        snapshot[aliasName] = value;

        // Keep raw WebSocket name only as non-enumerable fallback for internal matching.
        // This prevents popup/grid display from showing raw AssetAttributeName.
        if (rawName && rawName !== aliasName) {
            try {
                Object.defineProperty(snapshot, rawName, {
                    value: value,
                    enumerable: false,
                    configurable: true,
                    writable: true
                });
            } catch (e) {
                // Last fallback; normal UI Object.keys() should still mainly show AliasName.
                snapshot[rawName] = value;
            }
        }
    }

    /* ── Attribute name resolution ───────────────────────────────────────── */
    function resolveAttrName(rawName, attrId, assetId, dataType) {
        // For normal asset attributes, display AliasName from GetBulkAssetMetadata only.
        if (String(dataType || '').toLowerCase() !== 'datalogger') {
            if (typeof window.getBulkAliasName === 'function') {
                var bulkAlias = window.getBulkAliasName(assetId, rawName, attrId);
                if (bulkAlias) return bulkAlias;
            }

            if (typeof window.userAssetSimpleMap !== 'undefined') {
                var rawNorm = String(rawName || '').trim().toUpperCase();

                for (var k in window.userAssetSimpleMap) {
                    if (!window.userAssetSimpleMap.hasOwnProperty(k)) continue;
                    if (String(assetId || '') && k.indexOf(String(assetId) + '_') !== 0) continue;

                    var e = window.userAssetSimpleMap[k];
                    if (!e || !e.name) continue;

                    var eRaw = String(e.attributeName || e.AttributeName || e.title || e.Title || '').trim().toUpperCase();
                    var eAlias = String(e.name || e.AliasName || '').trim().toUpperCase();

                    if (eRaw === rawNorm || eAlias === rawNorm) {
                        return e.name; // AliasName from bulk
                    }
                }
            }

            if (typeof window.getAttrDisplayName === 'function') {
                var dn = window.getAttrDisplayName(rawName, attrId, assetId);
                if (dn) return dn;
            }

            return rawName;
        }

        // DataLogger uses mAssetInfoDataloggers.DataloggerAttribute from bulk.
        if (typeof window.userAssetDataloggerMap !== 'undefined') {
            var e2 = window.userAssetDataloggerMap[String(assetId) + '_' + String(attrId)];
            if (e2 && e2.name) return e2.name;
        }

        return rawName;
    }
    /* Merge wsLiveData attrs into our snapshot so full history is available */
    function enrichFromWsLiveData(assetName, snapshot) {
        if (!window.wsLiveData) return;
        for (var wid in window.wsLiveData) {
            if (!window.wsLiveData.hasOwnProperty(wid)) continue;
            var wEntry = window.wsLiveData[wid];
            if (!wEntry || String(wEntry.AssetName || '').trim() !== assetName) continue;
            if (wEntry.attrs) {
                for (var ak in wEntry.attrs) {
                    if (!wEntry.attrs.hasOwnProperty(ak)) continue;

                    var aObj = wEntry.attrs[ak];
                    var av = parseFloat(aObj && aObj.Value);
                    if (isNaN(av)) continue;

                    var attrId = aObj ? (aObj.AttrId || aObj.AssetAttributeId || aObj.AttributeId) : '';
                    var aliasName = resolveAttrName(ak, attrId, wid, aObj && aObj.DataType);

                    setSnapshotValue(snapshot, aliasName, ak, av);
                }
            }
            /* DataLogger relay states are stored in wsLiveData.dlRelays, not attrs.
               Add them to the SIP snapshot so TPR=0 can occupy the track and
               signal relay/calling aliases can participate in matching. */
            if (wEntry.dlRelays) {
                for (var dk in wEntry.dlRelays) {
                    if (!wEntry.dlRelays.hasOwnProperty(dk)) continue;
                    var rObj = wEntry.dlRelays[dk];
                    if (!rObj) continue;
                    var rv = parseFloat(rObj.value);
                    if (isNaN(rv)) continue;
                    var rDisplay = rObj.displayName || dk;
                    setSnapshotValue(snapshot, rDisplay, rObj.rawAttrName || rObj.attrName || dk, rv);
                    if (rObj.role) setSnapshotValue(snapshot, rDisplay, rObj.role, rv);
                }
            }
            if (wEntry.ZeroOffsetValue != null && !isNaN(parseFloat(wEntry.ZeroOffsetValue)))
                state.zeroOffset[assetName] = parseFloat(wEntry.ZeroOffsetValue);
            break;
        }
    }

    /* ── Cell finder ─────────────────────────────────────────────────────── */
    function findCells(assetName) {
        var out = [];
        var direct = state.byLabel[assetName];
        if (direct) for (var i = 0; i < direct.length; i++) out.push(direct[i]);
        var prefix = state.byPrefix[assetName];
        if (prefix) for (var j = 0; j < prefix.length; j++) if (out.indexOf(prefix[j]) === -1) out.push(prefix[j]);

        /* NEW — tolerant fallback: "S-14" ⇆ "S14", "SH-37" ⇆ "SH37", "S  2" ⇆ "S2" */
        if (!out.length) {
            var nk = _normLabel(assetName);
            var nd = (state.byNorm && state.byNorm[nk]) || [];
            for (var a = 0; a < nd.length; a++) if (out.indexOf(nd[a]) === -1) out.push(nd[a]);
            var np = (state.byNormPrefix && state.byNormPrefix[nk]) || [];
            for (var b = 0; b < np.length; b++) if (out.indexOf(np[b]) === -1) out.push(np[b]);
        }
        return out;
    }

    /* =========================================================================
       SECTION 5 — SIGNAL / TRACK / PM LOGIC
       ========================================================================= */
    function _normLabel(v) {
        return String(v == null ? '' : v).toUpperCase().replace(/[^A-Z0-9]+/g, '');
    }
    function _sipNormName(v) {
        return String(v == null ? '' : v).toUpperCase().replace(/<[^>]*>/g, '')
            .replace(/[^A-Z0-9]+/g, '');
    }

    function relayPicked(s, names) {
        for (var i = 0; i < names.length; i++) { var v = s[names[i]]; if (v != null && v >= 0.5) return true; }
        var wanted = {};
        for (var w = 0; w < names.length; w++) wanted[_sipNormName(names[w])] = true;
        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            if (wanted[_sipNormName(k)] && s[k] != null && s[k] >= 0.5) return true;
        }
        return false;
    }
    function valOf(s, names) {
        for (var i = 0; i < names.length; i++) { var v = s[names[i]]; if (v != null && !isNaN(v)) return v; }
        var wanted = {};
        for (var w = 0; w < names.length; w++) wanted[_sipNormName(names[w])] = true;
        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            if (wanted[_sipNormName(k)] && s[k] != null && !isNaN(s[k])) return s[k];
        }
        return null;
    }
    function valOfPrefix(s, prefixes) {
        var keys = Object.keys(s).sort();   // sort for deterministic matching
        for (var i = 0; i < keys.length; i++) {
            var k = _sipNormName(keys[i]);
            for (var j = 0; j < prefixes.length; j++)
                if (k.indexOf(_sipNormName(prefixes[j])) !== -1) return s[keys[i]];
        }
        return null;
    }

    function valueByBaseAndUnit(s, baseName, unitName) {
        var base = _sipNormName(baseName);
        var unit = _sipNormName(unitName || '');
        var best = null;
        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            var nk = _sipNormName(k);
            if (nk.indexOf(base) === -1) continue;
            if (unit && nk.indexOf(unit) === -1) continue;
            var v = parseFloat(s[k]);
            if (!isNaN(v)) {
                best = v;
                if (nk === base + unit || nk === base) break;
            }
        }
        return best;
    }
    /* Threshold — identical source priority to updateMainSignalLights():
 * zeroOffsetCache (API) → wsLiveData.ZeroOffsetValue → RDPMS_DEFAULT_THRESHOLD */
    function thresholdFor(assetName) {
        if (window.wsLiveData) {
            for (var id in wsLiveData) {
                if (!wsLiveData.hasOwnProperty(id)) continue;
                var e = wsLiveData[id];
                if (!e || String(e.AssetName || '').trim() !== assetName) continue;
                if (window.zeroOffsetCache && zeroOffsetCache[id] && zeroOffsetCache[id].fetched) {
                    var cz = parseFloat(zeroOffsetCache[id].value);
                    if (!isNaN(cz)) return cz;
                }
                var z = parseFloat(e.ZeroOffsetValue);
                if (!isNaN(z)) return z;
                break;
            }
        }
        if (state.zeroOffset[assetName] != null) return state.zeroOffset[assetName];
        return (typeof window.RDPMS_DEFAULT_THRESHOLD !== 'undefined')
            ? window.RDPMS_DEFAULT_THRESHOLD : ZERO_OFFSET_DEFAULT;
    }

    /* Signal aspect — same priority used by telemetrylive.js updateMainSignalLights():
       analog mA values first, then relay fallback when mA values are absent. */
    function computeAspect(s, thr) {
        var rgMa = valOf(s, ['RG mA', 'R mA']);
        var dgMa = valOf(s, ['DG mA', 'G mA']);
        var hgMa = valOf(s, ['HG mA', 'H mA']);
        var hhgMa = valOf(s, ['HHG mA']);

        var rgActive = rgMa != null && rgMa > thr;
        var hhgActive = hhgMa != null && hhgMa > thr;
        var hgActive = hgMa != null && hgMa > thr;
        var dgActive = dgMa != null && dgMa > thr;

        if (rgActive || hhgActive || hgActive || dgActive) {
            var maxActiveMa = Math.max(
                rgActive ? rgMa : 0,
                hhgActive ? hhgMa : 0,
                hgActive ? hgMa : 0,
                dgActive ? dgMa : 0
            );
            if (rgActive && maxActiveMa === rgMa) return 'RG';
            if (hhgActive) return 'HHG';
            if (hgActive) return 'HG';
            if (dgActive) return 'DG';
        }

        if (relayPicked(s, ['RECR', 'RED CR', 'R E CR'])) return 'RG';
        if (relayPicked(s, ['HECR', 'HE CR']) && relayPicked(s, ['HHECR', 'HHE CR'])) return 'HHG';
        if (relayPicked(s, ['HECR', 'HE CR'])) return 'HG';
        if (relayPicked(s, ['DECR', 'DE CR'])) return 'DG';
        return 'OFF';
    }

    /* Convert aspect → lit string for composite signal (attrs.signal.lit).
       The renderer accepts multiple chars, so HHG can light Y + X together. */
    function aspectToLit(aspect, lampsStr) {
        if (!aspect || aspect === 'OFF') return '';
        var lamps = String(lampsStr || '').toUpperCase();
        if (aspect === 'RG') return lamps.indexOf('R') !== -1 ? 'R' : '';
        if (aspect === 'HG') return lamps.indexOf('Y') !== -1 ? 'Y' : '';
        if (aspect === 'DG') return lamps.indexOf('G') !== -1 ? 'G' : '';
        if (aspect === 'HHG') {
            var lit = '';
            if (lamps.indexOf('Y') !== -1) lit += 'Y';
            if (lamps.indexOf('X') !== -1) lit += 'X';
            return lit || (lamps.indexOf('Y') !== -1 ? 'Y' : '');
        }
        return '';
    }

    function getTprRelayDropValue(s) {
        var exact = valOf(s, ['TPR', 'TPR Relay', 'TPR DL', 'TPR-DL']);
        if (exact != null && (exact === 0 || exact === 1)) return exact;

        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            var nk = _sipNormName(k);
            if (nk.indexOf('TPR') === -1) continue;
            // Do not treat analog voltage/current names as a relay unless the name is clearly a relay/pickup/drop item.
            var analogLike = (nk.indexOf('V') !== -1 || nk.indexOf('MA') !== -1 || nk.indexOf('CURRENT') !== -1 || nk.indexOf('VOLT') !== -1);
            var relayLike = (nk === 'TPR' || nk.indexOf('RELAY') !== -1 || nk.indexOf('PICKUP') !== -1 || nk.indexOf('DROP') !== -1 || nk.indexOf('DL') !== -1);
            var v = parseFloat(s[k]);
            if (!isNaN(v) && (v === 0 || v === 1) && (!analogLike || relayLike)) return v;
        }
        return null;
    }

    /* Track occupancy — TPR datalogger value 0 means occupied. */
    function computeOccupied(s) {
        var tprRelay = getTprRelayDropValue(s);
        if (tprRelay != null) return tprRelay < 0.5;

        var tprV = valOf(s, ['TPR V', 'TPR V (Loc)', 'TPRV', 'TPR Voltage', 'VTC 24 DC TPR I/P(V)', 'VTC 24 DC TPR I/P']);
        if (tprV == null) tprV = valueByBaseAndUnit(s, 'TPR', 'V');
        if (tprV != null) return tprV < TRACK_OCC_THR;

        var vr = valOf(s, ['Vr', 'VR', 'V Relay']);
        if (vr != null && ((vr > 0.1 && vr < 2.5) || vr > 4.2)) return true;
        var ck = valOf(s, ['Choke V', 'ChokeV']);
        if (ck != null && ck > 1.8) return true;
        return false;
    }

    /* Diagnostic: check whether the snapshot has any recognized track attrs */
    var _TRACK_ATTR_RE = /(tpr|tprv|vr|v\s?relay|choke)/i;
    function hasTrackAttrs(s) {
        var keys = Object.keys(s);
        for (var i = 0; i < keys.length; i++) {
            if (_TRACK_ATTR_RE.test(keys[i])) return true;
        }
        return false;
    }

    /* Diagnostic: does the snapshot contain ANY recognizable signal-aspect
       attribute (aspect currents or ECR relays)? When telemetry arrives for
       a signal but none of these match, the signal MUST stay all-OFF and we
       warn so the attribute mapping can be corrected. */
    function hasSignalAttrs(s) {
        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            var nk = _sipNormName(k);
            if (nk.indexOf('RGMA') !== -1 || nk.indexOf('DGMA') !== -1 ||
                nk.indexOf('HGMA') !== -1 || nk.indexOf('HHGMA') !== -1 ||
                nk.indexOf('RECR') !== -1 || nk.indexOf('DECR') !== -1 ||
                nk.indexOf('HECR') !== -1 || nk.indexOf('HHECR') !== -1) return true;
        }
        return false;
    }

    function routeLabelsForCell(cell) {
        var route = (cell.attrs && cell.attrs.route) || {};
        var raw = route.labels || route.routes || route.routeLabels || 'AUG,BUG,CUG,DUG,EUG';
        if (Array.isArray(raw)) raw = raw.join(',');
        var seen = {}, out = [];
        String(raw || '').toUpperCase().split(/[\s,|/]+/).forEach(function (x) {
            x = String(x || '').trim();
            if (x && !seen[x]) { seen[x] = true; out.push(x); }
        });
        return out.length ? out : ['AUG', 'BUG', 'CUG', 'DUG'];
    }

    function routeMaValue(s, routeName) {
        var rn = String(routeName || '').toUpperCase();
        var v = valOf(s, [rn + ' mA', rn + ' MA', rn + '_mA', rn + '-mA']);
        if (v != null) return v;
        for (var k in s) {
            if (!s.hasOwnProperty(k)) continue;
            var nk = _sipNormName(k);
            if (nk.indexOf(rn) !== -1 && nk.indexOf('MA') !== -1) {
                var nv = parseFloat(s[k]);
                if (!isNaN(nv)) return nv;
            }
        }
        return null;
    }

    function computeRouteCallingActive(s, thr, cell) {
        var labels = routeLabelsForCell(cell);
        var active = [];
        for (var i = 0; i < labels.length; i++) {
            var rn = labels[i];
            var rMa = routeMaValue(s, rn);
            var rRelay = valOf(s, [rn]);
            if ((rMa != null && rMa > thr) || (rRelay != null && rRelay >= 0.5)) {
                active.push(rn);
            }
        }

        var coHgMa = valOf(s, ['Co_Hg mA', 'Co_HG mA', 'CoHg mA', 'CO HG mA', 'Calling mA', 'CALLING mA', 'C mA']);
        if (coHgMa == null) coHgMa = valueByBaseAndUnit(s, 'COHG', 'MA');
        var coRelay = valOf(s, ['Co_Hg', 'Co_HG', 'CoHg', 'CALLING', 'CALL', 'C']);
        if ((coHgMa != null && coHgMa > thr) || (coRelay != null && coRelay >= 0.5)) {
            active.push('C');
        }
        return active.join(',');
    }

    /* Point Machine position — A End wins, mirrors old Sview.cshtml */
    function computePM(s) {
        var aEN = valOf(s, ['A End - NWKR', 'ANWKR', 'A_NWKR']);
        var aER = valOf(s, ['A End - RWKR', 'ARWKR', 'A_RWKR']);
        var bEN = valOf(s, ['B End - NWKR', 'BNWKR', 'B_NWKR']);
        var bER = valOf(s, ['B End - RWKR', 'BRWKR', 'B_RWKR']);
        var nw = valOfPrefix(s, ['NWKR']);
        var rw = valOfPrefix(s, ['RWKR']);
        var normalV = aEN != null ? aEN : bEN != null ? bEN : nw;
        var reverseV = aER != null ? aER : bER != null ? bER : rw;
        if (normalV != null && normalV > PM_IND_THR) return 'NORMAL';
        if (reverseV != null && reverseV > PM_IND_THR) return 'REVERSE';
        return 'UNKNOWN';
    }

    /* Shunt aspect — EXACT mirror of updateShuntSignalLights():
 *   On Aspect mA  > threshold → ON   (left + right lamps lit)
 *   Off Aspect mA > threshold → OFF  (top + right lamps lit)
 *   neither                   → NONE (all lamps dim)
 * Relay fallback (HR / OFFECR) only when no aspect currents exist. */
    function computeShuntAspect(s, thr) {
        var onMa = valOf(s, ['On Aspect mA', 'ON Aspect mA', 'OnAspect mA', 'IShSig ON', 'ShSig ON mA']);
        if (onMa == null) onMa = valueByBaseAndUnit(s, 'ONASPECT', 'MA');
        var offMa = valOf(s, ['Off Aspect mA', 'OFF Aspect mA', 'OffAspect mA', 'IShSig OFF', 'ShSig OFF mA']);
        if (offMa == null) offMa = valueByBaseAndUnit(s, 'OFFASPECT', 'MA');

        // Reference parity (telemetrylive.js): ON wins over OFF when both exceed thr.
        if (onMa != null && onMa > thr) return 'ON';
        if (offMa != null && offMa > thr) return 'OFF';
        if (onMa == null && offMa == null) {
            if (relayPicked(s, ['HR'])) return 'ON';
            if (relayPicked(s, ['OFFECR', 'OFF ECR'])) return 'OFF';
        }
        return 'NONE';
    }

    /* Bus Bar — returns { dcV, acV, lowDC, lowAC } for label + colour update.
     * Mirrors old Sview.cshtml BindBusBarSimulation logic. */
    function computeBusBar(s) {
        var dcV = valOf(s, ['24 V', '24V', 'DC V', 'DCV', 'DC Voltage']);
        var acV = valOf(s, ['110V', '110 V', 'AC V', 'ACV', 'AC Voltage']);
        return {
            dcV: dcV,
            acV: acV,
            lowDC: (dcV != null && dcV > 0 && dcV < 20),
            lowAC: (acV != null && acV > 0 && acV < 110)
        };
    }

    /* Axle Counter — occupied when relay dropped or voltage anomaly */
    function computeAxleOccupied(s) {
        var v = valOf(s, ['Axle V', 'AXL V', 'Count']);
        if (v != null && v > 0.1) return true;
        return false;
    }

    /* =========================================================================
       SECTION 6 — CELL RE-EVALUATION
       ========================================================================= */

    /* Per-cell timers that clear the PM operate-blink flag */
    var _pmBlinkTimers = {};
    function schedulePmBlinkClear(cell) {
        if (_pmBlinkTimers[cell.id]) clearTimeout(_pmBlinkTimers[cell.id]);
        _pmBlinkTimers[cell.id] = setTimeout(function () {
            delete _pmBlinkTimers[cell.id];
            if (cell.attrs && cell.attrs.pmBlink) {
                delete cell.attrs.pmBlink;
                requestRender();
            }
        }, PM_BLINK_MS);
    }

    function reeval(cell, assetName, s) {
        cell.attrs = cell.attrs || {};
        var thr = thresholdFor(assetName);
        /* ── Composite signal (examples.Signal / SignalShunt) ── */
        if (COMPOSITE_SIGNAL[cell.type]) {
            var aspect = computeAspect(s, thr);
            var changedSig = false;

            /* Telemetry arrived but NOTHING matched a known aspect attribute.
               Aspect resolves to OFF → all lamps dark (never a phantom green).
               Warn once so the attribute naming can be fixed at the source. */
            if (aspect === 'OFF' && Object.keys(s).length > 0 && !hasSignalAttrs(s)) {
                swarnOnce('sig-noattr:' + assetName,
                    'Signal "' + assetName + '": telemetry received but NO aspect attribute matched ' +
                    '(expected RG/HG/HHG/DG mA or RECR/HECR/HHECR/DECR relays). ' +
                    'All lamps held OFF. Snapshot keys:', Object.keys(s));
                pushSample(state.diag.unmatched,
                    '⚠ Signal ' + assetName + ': data received but no aspect attrs matched', 20);
            }

            if (cell.type === 'examples.SignalShunt') {
                /* SignalShunt uses attrs.lit directly (same char as composite) */
                var litChar = aspectToLit(aspect, 'RYG');   // always has R,Y,G
                var cur = String((cell.attrs && cell.attrs.lit) || '');
                if (cur !== litChar) {
                    cell.attrs.lit = litChar;
                    changedSig = true;
                    slog('Signal ' + assetName + ' aspect → ' + aspect +
                        ' (lit "' + litChar + '", thr=' + thr + ')');
                }
                return changedSig;
            }

            /* examples.Signal — write to attrs.signal.lit */
            var sigA = cell.attrs.signal || {};
            var lamps = String(sigA.lamps || 'RYG');
            var newLit = aspectToLit(aspect, lamps);
            var curLit = String(sigA.lit || '');
            if (curLit !== newLit) {
                cell.attrs.signal = cell.attrs.signal || {};
                cell.attrs.signal.lit = newLit;
                changedSig = true;
                slog('Signal ' + assetName + ' aspect → ' + aspect +
                    ' (lit "' + newLit + '" of lamps "' + lamps + '", thr=' + thr + ')');
            }

            /* Attached Route / Calling on main signal — mirrors telemetrylive.js:
               route mA > ZeroOffset lights that route; Co_Hg mA > ZeroOffset lights C. */
            if (cell.attrs.route && (cell.attrs.route.enabled === true || String(cell.attrs.route.enabled).toLowerCase() === 'true')) {
                var newActive = computeRouteCallingActive(s, thr, cell);
                var curActive = String(cell.attrs.route.active || cell.attrs.route.activeRoutes || '');
                if (curActive !== newActive) {
                    cell.attrs.route.active = newActive;
                    changedSig = true;
                }
            }
            return changedSig;
        }

        /* ── Standalone Route / Calling Signal ── */
        if (ROUTE_CALLING_TYPES[cell.type]) {
            cell.attrs.route = cell.attrs.route || {};
            var rcActive = computeRouteCallingActive(s, thr, cell);
            var rcCur = String(cell.attrs.route.active || cell.attrs.route.activeRoutes || '');
            if (rcCur !== rcActive) { cell.attrs.route.active = rcActive; return true; }
            return false;
        }

        /* ── Legacy per-lamp signal cells ── */
        if (LAMP_SIGNAL[cell.type]) {
            var aspect2 = computeAspect(s, thr);
            /* Map cell type to aspect tag */
            var tagMap = {
                'examples.Signald90': 'RG',
                'examples.Signal45': 'HG',
                'examples.Signal90': 'DG',
                'examples.Signald45': 'HHG'
            };
            /* Also read tag from label ("S18 RG" → "RG") */
            var lbl = ((cell.attrs.label && cell.attrs.label.text) || '').trim().split(/\s+/);
            var cellTag = lbl.length >= 2 ? lbl[1].toUpperCase() : tagMap[cell.type];
            var shouldLit = (aspect2 === cellTag) || (aspect2 === 'HHG' && (cellTag === 'HG' || cellTag === 'HHG'));
            var litCol = LAMP_COLOUR[cell.type] || C_RED;
            var newFill = shouldLit ? litCol : OFF_GREY;
            cell.attrs.circle1 = cell.attrs.circle1 || {};
            if (cell.attrs.circle1.fill !== newFill) { cell.attrs.circle1.fill = newFill; return true; }
            return false;
        }

        /* ── Track (stroke-based: Track, Track1, Track2) ── */
        if (TRACK_STROKE[cell.type]) {
            var occ = computeOccupied(s);
            if (!occ && Object.keys(s).length > 0 && !hasTrackAttrs(s)) {
                pushSample(state.diag.unmatched,
                    '⚠ Track ' + assetName + ': data received but no TPR/Vr/Choke attrs matched', 20);
            }
            var ns = occ ? C_RED : OFF_GREY;
            cell.attrs.path = cell.attrs.path || {};
            if (cell.attrs.path.stroke !== ns) {
                cell.attrs.path.stroke = ns;
                slog('Track ' + assetName + ' → ' + (occ ? 'OCCUPIED (red)' : 'CLEAR (grey)'));
                return true;
            }
            return false;
        }

        /* ── Track (fill-based: Track3, Track4 — curved variants) ── */
        if (TRACK_FILL[cell.type]) {
            var occ2 = computeOccupied(s);
            if (!occ2 && Object.keys(s).length > 0 && !hasTrackAttrs(s)) {
                pushSample(state.diag.unmatched,
                    '⚠ Track ' + assetName + ': data received but no TPR/Vr/Choke attrs matched', 20);
            }
            var nf = occ2 ? C_RED : OFF_GREY;
            cell.attrs.path = cell.attrs.path || {};
            if (cell.attrs.path.fill !== nf) {
                cell.attrs.path.fill = nf;
                slog('Track ' + assetName + ' → ' + (occ2 ? 'OCCUPIED (red)' : 'CLEAR (grey)'));
                return true;
            }
            return false;
        }

        /* ── Point Machine ── */
        if (PM_TYPES[cell.type]) {
            var pos = computePM(s);
            var pf = pos === 'NORMAL' ? C_GREEN : pos === 'REVERSE' ? C_YELLOW : PM_OFF;
            /* Also set body.stroke — old Sview.cshtml set body.stroke = #FFFF00 for reverse,
               #3c4260 for normal. Mirrors that behaviour. */
            var bs = pos === 'REVERSE' ? C_YELLOW : OFF_GREY;
            cell.attrs.circle1 = cell.attrs.circle1 || {};
            cell.attrs.body = cell.attrs.body || {};
            var changed = false;

            /* ── Operate blink ──────────────────────────────────────────
               When live data shows the machine moving from one PROVEN
               position to the other (NORMAL ⇆ REVERSE), blink the
               indicator for PM_BLINK_MS so the operation is visible.
               First-ever data and UNKNOWN states never blink. */
            var prevPos = state.pmLast[cell.id];
            if (prevPos && prevPos !== pos &&
                (prevPos === 'NORMAL' || prevPos === 'REVERSE') &&
                (pos === 'NORMAL' || pos === 'REVERSE')) {
                cell.attrs.pmBlink = true;
                schedulePmBlinkClear(cell);
                changed = true;
                slog('Point machine ' + assetName + ' OPERATED: ' + prevPos + ' → ' + pos +
                    ' — indicator blinking for ' + (PM_BLINK_MS / 1000) + 's');
            }
            if (pos === 'NORMAL' || pos === 'REVERSE') state.pmLast[cell.id] = pos;

            if (cell.attrs.circle1.fill !== pf) {
                cell.attrs.circle1.fill = pf;
                cell.attrs.circle1.stroke = pf;
                changed = true;
                slog('Point machine ' + assetName + ' position → ' + pos);
            }
            if (cell.attrs.body.stroke !== bs) { cell.attrs.body.stroke = bs; changed = true; }
            return changed;
        }

        /* ── Shunt signal — drive the LAMPS, not the body fill ── */
        if (SHUNT_TYPES[cell.type]) {
            var shAspect = computeShuntAspect(s, thr);
            var newState = (shAspect === 'ON' || shAspect === 'OFF') ? shAspect : '';
            var shChanged = false;
            if (String(cell.attrs.shuntLive || '') !== newState) {
                cell.attrs.shuntLive = newState;
                shChanged = true;
            }
            /* PILOT is a measured value only — it never lights a lamp, but the
               asset popup surfaces it (mirrors telemetrylive.js I_Sh PILOT row). */
            var pilotMa = valOf(s, ['PILOT mA', 'PILOTRoot mA']);
            if (pilotMa == null) pilotMa = valueByBaseAndUnit(s, 'PILOT', 'MA');
            cell.attrs.shunt = cell.attrs.shunt || {};
            if (pilotMa != null && cell.attrs.shunt.pilotMa !== pilotMa) {
                cell.attrs.shunt.pilotMa = pilotMa;
                shChanged = true;
            }
            /* Neutralise any red body fill written by the old logic */
            cell.attrs.body = cell.attrs.body || {};
            if (cell.attrs.body.fill && cell.attrs.body.fill !== '#5B6168') {
                cell.attrs.body.fill = '#5B6168';
                shChanged = true;
            }
            return shChanged;
        }

        /* ── Bus Bar — show live voltage in label, red when low ── */
        if (BUSBAR_TYPES[cell.type]) {
            var bb = computeBusBar(s);
            if (bb.dcV == null && bb.acV == null) return false;

            /* Build label text: "dcV || Name || acVv"  (mirrors old BindBusBarSimulation) */
            var baseName = ((cell.attrs.label && cell.attrs.label.text) || '').trim();
            /* Strip previous dynamic values — keep only the core name */
            var parts = baseName.split(' || ');
            var coreName = parts.length >= 3 ? parts[1]
                : parts.length === 2 ? (isNaN(parseFloat(parts[0])) ? parts[0] : parts[1])
                    : baseName;
            var newLabel = '';
            if (bb.dcV != null && bb.dcV > 0 && bb.acV != null && bb.acV > 0) {
                newLabel = parseFloat(bb.dcV).toFixed(1) + ' || ' + coreName + ' || ' + parseFloat(bb.acV).toFixed(1) + 'v';
            } else if (bb.dcV != null && bb.dcV > 0) {
                newLabel = parseFloat(bb.dcV).toFixed(1) + ' || ' + coreName;
            } else if (bb.acV != null && bb.acV > 0) {
                newLabel = coreName + ' || ' + parseFloat(bb.acV).toFixed(1) + 'v';
            } else {
                newLabel = coreName;
            }

            var newFill = (bb.lowDC || bb.lowAC) ? C_RED : '#ffffff';

            cell.attrs.label = cell.attrs.label || {};
            var changed = false;
            if (cell.attrs.label.text !== newLabel) { cell.attrs.label.text = newLabel; changed = true; }
            if (cell.attrs.label.fill !== newFill) { cell.attrs.label.fill = newFill; changed = true; }
            return changed;
        }

        /* ── Axle Counter — colour path.stroke on occupancy ── */
        if (AXLE_TYPES[cell.type]) {
            var axOcc = computeAxleOccupied(s);
            var axStroke = axOcc ? C_RED : OFF_GREY;
            cell.attrs.path = cell.attrs.path || {};
            if (cell.attrs.path.stroke !== axStroke) { cell.attrs.path.stroke = axStroke; return true; }
            return false;
        }

        /* ── Gate — no live data binding in old system (was commented out),
              stub here for future use ── */
        if (GATE_TYPES[cell.type]) {
            /* Gate telemetry not yet defined — leave unchanged */
            return false;
        }

        return false;
    }

    /* =========================================================================
       SECTION 7 — RENDERING
       ========================================================================= */

    function requestRender() {
        if (state.renderPending) return;
        state.renderPending = true;
        (window.requestAnimationFrame || function (fn) { setTimeout(fn, 16); })(function () {
            state.renderPending = false;
            render();
        });
    }

    function render() {
        if (!canvasEl) return;
        if (!state.cells.length) { renderPlaceholder('No cells to render.'); return; }

        /* sip-library layer functions */
        var railLayer = typeof SIP.renderRailLayer === 'function' ? SIP.renderRailLayer(state.cells) : '';
        var standLayer = typeof SIP.renderStandLayer === 'function' ? SIP.renderStandLayer(state.cells) : '';
        var breakerLayer = typeof SIP.renderBreakerLayer === 'function' ? SIP.renderBreakerLayer(state.cells) : '';

        var hl = state.highlight ? state.highlight.toLowerCase() : '';
        var hasAlerts = alertFlashData.length > 0;
        var frags = '';
        for (var i = 0; i < state.cells.length; i++) {
            var c = state.cells[i];
            var inner = SIP.renderCell(c);
            var cls = 'sip-live-cell';
            var cellLabel = (c.attrs && c.attrs.label && c.attrs.label.text) || '';
            if (hl) {
                var lbl = cellLabel.toLowerCase();
                cls += lbl.indexOf(hl) !== -1 ? ' sip-highlight' : ' sip-dim';
            }
            /* Active-alert flash overlay — mirrors old fnShowActiveAlertSimulation() */
            var flashStyle = '';
            if (hasAlerts) {
                var flashCol = getAlertFlashColour(cellLabel.trim());
                if (flashCol) {
                    flashStyle = ' filter:drop-shadow(0 0 6px ' + flashCol + ');';
                }
            }
            frags += '<g class="' + cls + '" data-cell-id="' + escHtml(c.id) + '" data-cell-type="' + escHtml(c.type) + '" data-cell-label="' + escHtml(cellLabel) + '" style="cursor:pointer;pointer-events:all;' + flashStyle + '">' + inner + '</g>';
        }

        canvasEl.innerHTML =
            '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + state.viewBox + '" ' +
            'class="sip-yard" preserveAspectRatio="xMidYMid meet" style="width:100%;height:100%;">' +
            '<defs>' +
            '<style>' +
            //'.sip-live-cell,.sip-live-cell .sip-asset{cursor:pointer;pointer-events:all;}' +
            //'.sip-rail-layer,.sip-stand-layer,.sip-breaker-layer,.grid{pointer-events:none;}' +
            '.sip-live-cell,.sip-live-cell .sip-asset{cursor:pointer;pointer-events:all;}' +
            '.sip-rail-layer,.sip-stand-layer,.sip-breaker-layer,.grid{pointer-events:none;}' +
            '</style>' +
            '<linearGradient id="sipBg" x1="0" y1="0" x2="0" y2="1">' +
            '<stop offset="0%"  stop-color="#0c1530"/>' +
            '<stop offset="55%" stop-color="#08101c"/>' +
            '<stop offset="100%" stop-color="#060a14"/>' +
            '</linearGradient>' +
            '</defs>' +
            '<rect width="100%" height="100%" fill="url(#sipBg)"/>' +
            railLayer + standLayer + breakerLayer + frags +
            '</svg>';
    }

    function renderPlaceholder(msg) {
        if (!canvasEl) return;
        canvasEl.innerHTML =
            '<div style="display:flex;align-items:center;justify-content:center;' +
            'height:100%;color:#64748b;font-size:14px;font-family:system-ui;">' +
            escHtml(msg) + '</div>';
    }

    /* =========================================================================
       SECTION 8 — STATUS + HIGHLIGHT
       ========================================================================= */

    function setStatus(text, cls) {
        if (!statusEl) return;
        statusEl.textContent = text;
        statusEl.className = 'ws-status' + (cls ? ' ws-' + cls : '');
    }

    function highlight(query) {
        state.highlight = (query || '').trim();
        render();
    }

    /* =========================================================================
       SECTION 9 — DIAGNOSTICS + SIMULATE
       ========================================================================= */

    function diagnose() {
        var ws = state.ws;
        var wsState = !ws ? 'NONE'
            : ws.readyState === 0 ? 'CONNECTING'
                : ws.readyState === 1 ? 'OPEN'
                    : ws.readyState === 2 ? 'CLOSING' : 'CLOSED';

        var info = {
            mode: state.bridgeInstalled ? 'bridge (sharing telemetrylive.js WS)' : (state.ws ? 'standalone' : 'idle'),
            siteId: state.siteId,
            'cells loaded': state.cells.length,
            'labels': Object.keys(state.byLabel).length,
            'prefixes': Object.keys(state.byPrefix).length,
            'WS state': wsState,
            'msgs recv': state.diag.recv,
            'items': state.diag.items,
            'hits': state.diag.hits,
            'no-match': state.diag.noMatch
        };
        console.log('[sip-telemetry] DIAGNOSE'); console.table(info);

        if (!state.cells.length)
            console.warn('No layout — select a site first.');

        if (state.diag.unmatched.length) {
            console.warn('Assets in WS not found in SIP labels:');
            console.table(state.diag.unmatched);
            console.log('Known SIP labels:', Object.keys(state.byLabel).sort());
        }

        if (state.diag.recent.length) {
            console.log('Last 5 messages:');
            console.table(state.diag.recent.slice(-5));
        }
        return info;
    }

    /* Inject a fake telemetry message for testing.
     *
     * IMPORTANT: Values ACCUMULATE in the asset snapshot (just like real WS).
     * Pass reset=true (4th arg) to clear previous values for this asset first,
     * so the new value is evaluated in isolation.
     *
     *   SipTelemetry.simulate('S13','RECR',1, true)       → S13 R lamp RED
     *   SipTelemetry.simulate('S13','HECR',1, true)       → S13 Y lamp YELLOW
     *   SipTelemetry.simulate('S13','DECR',1, true)       → S13 G lamp GREEN
     *   SipTelemetry.simulate('C-18T','TPR',0, true)      → TPR drop; track occupied (red)
     *   SipTelemetry.simulate('C-18T','TPR',1, true)      → TPR pickup; track clear
     *   SipTelemetry.simulate('S13','AUG mA',12, true)    → AUG route lights on
     *   SipTelemetry.simulate('S13','Co_Hg mA',12, true)  → calling C lights on
     *   SipTelemetry.simulate('60','A End - NWKR',23, true)  → PM normal (green)
     *   SipTelemetry.simulate('60','A End - RWKR',23, true)  → PM reverse (yellow)
     *   SipTelemetry.simulate('SH114','HR',1, true)       → shunt lit (red)
     *
     * Without reset, calling RECR=1 then HECR=1 keeps RECR=1 in the snapshot
     * and Red wins (computeAspect checks Red first). This matches real WS
     * behaviour where the server sends explicit 0 values for dropped relays.
     */
    function simulate(assetName, attrName, value, reset) {
        if (!state.cells.length) { console.warn('[sip-telemetry] No layout — select a site first.'); return; }
        if (reset) {
            if (state.assetValues[assetName]) delete state.assetValues[assetName];
            state.simNoEnrich[assetName] = true;
        }
        feedItems([{
            AssetName: assetName, AssetAttributeName: attrName,
            AssetAttributeId: null, AssetId: 0,
            Value: value, DataType: 'Simulated',
            TimestampDevice: new Date().toISOString()
        }]);
    }

    /* =========================================================================
       SECTION 10 — ACTIVE ALERT FLASH
       Mirrors old fnShowActiveAlertSimulation() from Sview.cshtml.
       Fetches active alerts and flashes colours on matched SIP cells.
       ========================================================================= */

    var ALERT_FLASH_COLOURS = ['#7366ff', '#a927f9', '#4bacc6', '#215968', '#b75d32', '#31d0c6'];
    var alertFlashTimer = null;
    var alertFlashRenderTimer = null;
    var alertFlashData = [];     // array of { AssetName, IsSmsLogActive }

    function startAlertFlash() {
        stopAlertFlash();
        fetchActiveAlerts();
        // Re-fetch every 30s
        alertFlashTimer = setInterval(fetchActiveAlerts, 30000);
    }

    function stopAlertFlash() {
        if (alertFlashTimer) { clearInterval(alertFlashTimer); alertFlashTimer = null; }
        if (alertFlashRenderTimer) { clearInterval(alertFlashRenderTimer); alertFlashRenderTimer = null; }
        alertFlashData = [];
    }

    function fetchActiveAlerts() {
        if (!state.siteId) return;
        $.ajax({
            url: '/FRS25/Telemetry/GetListActiveAlerts',
            type: 'Post',
            data: JSON.stringify({ mSmsLog: { SiteId: state.siteId, AssetTypeId: 0 } }),
            contentType: 'application/json',
            success: function (data) {
                if (data && data.mSMSLogs) {
                    alertFlashData = data.mSMSLogs;
                    /* Start a re-render tick so the flash blink is visible */
                    if (alertFlashData.length > 0 && !alertFlashRenderTimer) {
                        alertFlashRenderTimer = setInterval(function () {
                            if (state.cells.length > 0) render();
                        }, 600);
                    } else if (alertFlashData.length === 0 && alertFlashRenderTimer) {
                        clearInterval(alertFlashRenderTimer);
                        alertFlashRenderTimer = null;
                    }
                }
            },
            error: function () { /* silent */ }
        });
    }

    /* Called during render — returns flash colour or null.
     * Uses a time-based toggle (every 600ms) so the flash is visible. */
    function getAlertFlashColour(assetName) {
        if (!alertFlashData.length) return null;
        var now = Date.now();
        // Only flash on the "on" phase of a 1.2s blink cycle
        if (Math.floor(now / 600) % 2 !== 0) return null;
        for (var i = 0; i < alertFlashData.length; i++) {
            var al = alertFlashData[i];
            if (al.IsSmsLogActive && String(al.AssetName || '').trim() === assetName) {
                return ALERT_FLASH_COLOURS[Math.floor(Math.random() * ALERT_FLASH_COLOURS.length)];
            }
        }
        return null;
    }

    /* =========================================================================
       HELPERS
       ========================================================================= */
    function pushSample(arr, v, cap) { arr.push(v); while (arr.length > cap) arr.shift(); }
    function escHtml(s) {
        return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    /* =========================================================================
       BOOT
       ========================================================================= */
    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', init);
    else
        init();

    /* =========================================================================
       PUBLIC API
       ========================================================================= */
    window.SipTelemetry = {
        connectToSite: connectToSite,
        applyPayload: applyPayload,
        disconnect: function () { closeSockets(); removeBridge(); },
        refresh: function () { if (state.siteId) connectToSite(state.siteId); },
        highlight: highlight,
        diagnose: diagnose,
        simulate: simulate,
        installBridge: tryBridge,
        removeBridge: removeBridge,
        startAlertFlash: startAlertFlash,
        stopAlertFlash: stopAlertFlash,
        _state: state
    };

})();
