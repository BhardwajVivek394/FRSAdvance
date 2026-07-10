/* ═══════════════════════════════════════════════════════════════════════════
   SIP Asset Detail Popup — sip-asset-popup-live.js  (v2 — design-fix)
   ─────────────────────────────────────────────────────────────────────────
   Self-contained popup for the Telemetry-Live page.
   Uses the EXACT same element IDs, CSS classes, and variable names as
   Index.cshtml so the design is pixel-identical.

   If the static #sipAssetPopupOverlay element already exists in the page
   (i.e. we're on the SIP editor page), we reuse it.
   If it does NOT exist (telemetry-live page), we inject both the CSS and
   the HTML dynamically — copied verbatim from Index.cshtml.

   Live values come from wsLiveData (WebSocket), refreshed every 3 s.

   USAGE — add ONE script tag to the telemetry-live Razor view:
     <script src="~/Areas/FRS25/assets/sip/js/sip-asset-popup-live.js"></script>
   ═══════════════════════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    var _ready = false;
    var _refreshTimer = null;
    var _graphRefreshTimer = null;
    var _graphPoints = [];          // (legacy) kept for compat — main store is _graphSeriesData
    var _graphAttrKey = '';         // (legacy) first selected attr wsLiveData key
    var _graphAlias = '';           // (legacy) first selected alias
    var _graphAttrId = '';          // (legacy) first selected attr Id
    var _graphSeriesData = {};      // attrId → points[] (multi-series)
    var _graphAllAttrs = [];        // [{id,title,alias}] — full attribute list for checkboxes
    var _graphSelectedIds = [];     // attrIds currently checked
    var _chartGeo = null;           // { pad, gw, gh, W, H, series, baseImage }
    var _chartHoverBound = false;   // prevent double-binding hover events
    var _assetId = null;
    var _assetName = '';
    var _assetType = '';
    var _siteId = '';
    var _alertIds = [];
    var _lastLiveSignature = '';
    var _activeTab = 'live';
    var _isPopupOpen = false;
    var _refreshInProgress = false;
    var _historyUrl =
        (window.SipAssetPopupConfig && window.SipAssetPopupConfig.historyUrl)
            ? window.SipAssetPopupConfig.historyUrl
            : '/FRS25/Telemetry/GetHistoryData';

    var _graphRequestNo = 0;
    function formatHistoryControllerDate(dateValue, timeValue) {
        if (!dateValue) dateValue = today();
        var parts = String(dateValue).split('-');
        if (parts.length !== 3) return '';
        var yyyy = parts[0], mm = parts[1], dd = parts[2];
        var time = String(timeValue || '00:00:00').replace(/:/g, '');
        return dd + mm + yyyy + '_' + time;
    }

    function escapeRegExp(s) {
        return String(s || '').replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    }

    function cleanAliasName(rawName) {
        var name = String(rawName || '').trim();
        if (!name) return '';
        try {
            if (typeof window.getAttrDisplayName === 'function') {
                var mapped = window.getAttrDisplayName(name, null);
                if (mapped && mapped !== name) name = mapped;
            }
        } catch (e) { }
        if (_assetName) {
            name = name.replace(new RegExp('^0*' + escapeRegExp(_assetName) + '[-_\\s]*', 'i'), '');
        }
        name = name.replace(/^\d{4,8}[-_][A-Z]+[-_]\d{4,8}[-_]/i, '');
        name = name.replace(/[_]+/g, ' ').replace(/[-]+/g, ' ').replace(/\s+/g, ' ').trim();
        return name || rawName;
    }

    function parseNumber(v) {
        if (v == null) return null;
        if (typeof v === 'object' && v.Value != null) v = v.Value;
        var n = parseFloat(String(v).replace(/,/g, '').trim());
        return isNaN(n) ? null : n;
    }

    function formatTimeOnly(v) {
        if (!v) return '';
        var s = String(v).trim();
        var m = s.match(/(\d{1,2}):(\d{2})(?::(\d{2}))?/);
        if (m) return ('0' + m[1]).slice(-2) + ':' + m[2];
        var d = new Date(s);
        if (!isNaN(d.getTime())) return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
        return s;
    }

    function getSelectedGraphAttribute() {
        var sel = el('sipAssetParamSelect');
        if (!sel || !sel.value) return { attrId: '', title: '', alias: '' };
        var opt = sel.options[sel.selectedIndex];
        return {
            attrId: sel.value,                                        // numeric Id or "dl_N"
            title: opt ? (opt.getAttribute('data-title') || '') : '', // wsLiveData key ("Vr", "TPR V")
            alias: opt ? opt.text : sel.value                         // AliasName ("VTC RELAY END(V)")
        };
    }
    /* ── tiny helpers ─────────────────────────────────────────────── */
    function el(id) { return document.getElementById(id); }

    /* ── Console logging ────────────────────────────────────────────────
       Popup asset-resolution and data-binding decisions are logged with
       the [sip-popup] prefix. Set window.SIP_DEBUG = false to silence. */
    function sapLog() {
        if (window.SIP_DEBUG === false) return;
        console.log.apply(console, ['[sip-popup]'].concat([].slice.call(arguments)));
    }
    function sapWarn() {
        if (window.SIP_DEBUG === false) return;
        console.warn.apply(console, ['[sip-popup]'].concat([].slice.call(arguments)));
    }
    var _sapWarnOnce = {};
    function sapWarnOnce(key) {
        if (_sapWarnOnce[key]) return;
        _sapWarnOnce[key] = 1;
        sapWarn.apply(null, [].slice.call(arguments, 1));
    }
    function esc(v) { var d = document.createElement('div'); d.textContent = (v == null ? '' : v); return d.innerHTML; }

    function sapNodeExists(node) {
        return !!(node && document.documentElement && document.documentElement.contains(node));
    }

    function sapIsOverlayVisible() {
        var ov = el('sipAssetPopupOverlay');
        return !!(ov && sapNodeExists(ov) && ov.classList.contains('sap-show'));
    }

    function sapSafeCall(label, fn) {
        try {
            if (typeof fn === 'function') return fn();
        } catch (ex) {
            if (window.console && console.warn) {
                console.warn('[sip-popup-live] ' + label + ' failed:', ex);
            }
        }
        return null;
    }

    function sapSafeRefreshLive(reason) {
        if (_refreshInProgress) return;
        var grid = el('sipAssetTelemetryGrid');
        if (!grid || !sapNodeExists(grid)) {
            _ready = false;
            return;
        }

        _refreshInProgress = true;
        try {
            refreshLive();
        } catch (ex) {
            if (window.console && console.warn) {
                console.warn('[sip-popup-live] refreshLive failed' + (reason ? ' (' + reason + ')' : '') + ':', ex);
            }
        } finally {
            _refreshInProgress = false;
        }
    }
    function first() { for (var i = 0; i < arguments.length; i++) { var v = arguments[i]; if (v !== undefined && v !== null && v !== '') return v; } return ''; }
    function sapAttrIdOf(obj) {
        if (!obj || typeof obj !== 'object') return '';
        return obj.AttrId || obj.AssetAttributeId || obj.AttributeId || '';
    }

    function sapAliasName(rawKey, attrObj) {
        var aid = String(_assetId || '');
        var attrId = String(sapAttrIdOf(attrObj) || '').trim();
        if (typeof window.tlResolveDisplayAlias === 'function') {
            var common = window.tlResolveDisplayAlias(
                aid,
                rawKey,
                attrId,
                attrObj && attrObj.DataType,
                attrObj
            );
            // tlResolveDisplayAlias echoes rawKey back when nothing matched —
            // only accept it as a REAL resolution when it differs from rawKey,
            // otherwise fall through to the userAssetSimpleMap scan below.
            if (common && String(common).trim().toUpperCase() !==
                String(rawKey || '').trim().toUpperCase()) return common;
        }
        // Asset attribute: Id -> AliasName
        var map = window.userAssetSimpleMap || {};
        var exactKey = aid + '_' + attrId;

        if (attrId && map[exactKey]) {
            return map[exactKey].AliasName || map[exactKey].name || rawKey;
        }

        var rawNorm = String(rawKey || '').trim().toUpperCase();

        for (var k in map) {
            if (!map.hasOwnProperty(k)) continue;
            if (aid && k.indexOf(aid + '_') !== 0) continue;

            var e = map[k] || {};
            var title = String(e.title || e.Title || e.attributeName || e.AttributeName || '').trim().toUpperCase();
            var alias = String(e.AliasName || e.name || '').trim();

            if (title && title === rawNorm) {
                return alias || rawKey;
            }
        }

        return rawKey;
    }
    function fmtNow() { var d = new Date(); return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2); }
    function today() { var d = new Date(); return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2); }

    /* ── type icon SVGs ──────────────────────────────────────────── */
    var ICONS = {
        Track: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="6" width="20" height="12" rx="2"/><path d="M6 12h4"/><path d="M14 12h4"/></svg>',
        Signal: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="6" r="3"/><circle cx="12" cy="14" r="3"/><line x1="12" y1="17" x2="12" y2="22"/></svg>',
        Point: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 20L20 4"/><circle cx="12" cy="12" r="3"/></svg>',
        default: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>'
    };
    function iconFor(t) {
        t = String(t || '');
        if (t.indexOf('Track') >= 0) return ICONS.Track;
        if (t.indexOf('Signal') >= 0) return ICONS.Signal;
        if (t.indexOf('Point') >= 0) return ICONS.Point;
        return ICONS.default;
    }
    function normalizeAlertIds(v) {
        if (!v) return [];

        if (Array.isArray(v)) {
            return v
                .map(function (x) { return parseInt(x, 10); })
                .filter(function (x) { return !isNaN(x) && x > 0; });
        }

        if (typeof v === 'string') {
            return v
                .split(',')
                .map(function (x) { return parseInt(x.trim(), 10); })
                .filter(function (x) { return !isNaN(x) && x > 0; });
        }

        return [];
    }
    /* ═══════════════════════════════════════════════════════════════
       CSS INJECTION — exact copy of Index.cshtml <style> lines 36–960
       Only injected if the page does NOT already have these styles.
       ═══════════════════════════════════════════════════════════════ */
    function injectCSSIfNeeded() {
        /* If Index.cshtml's <style> already provided the rules, skip */
        if (document.querySelector('style[data-sap-popup-css]')) return;
        var overlay = el('sipAssetPopupOverlay');
        if (overlay) {
            var cs = window.getComputedStyle(overlay);
            /* A quick test: if the CSS variables are already set, the page has our styles */
            if (cs.getPropertyValue('--sap-accent-cyan').trim()) return;
        }

        var s = document.createElement('style');
        s.setAttribute('data-sap-popup-css', '1');
        s.textContent = [
            "#sipAssetPopupOverlay{--sap-bg-panel:#0f1629;--sap-bg-card:#141b2f;--sap-bg-row-alt:#111827;--sap-bg-row-hover:#1a2340;--sap-bg-header:#0c1220;--sap-border:#1e2a45;--sap-border-glow:#2a3a5c;--sap-text-primary:#e2e8f0;--sap-text-secondary:#8b9dc3;--sap-text-muted:#5a6a8a;--sap-accent-cyan:#00d4ff;--sap-accent-green:#22c55e;--sap-accent-yellow:#f59e0b;--sap-accent-orange:#f97316;--sap-accent-red:#ef4444;--sap-accent-blue:#3b82f6;--sap-gradient-header:linear-gradient(135deg,#1a2744 0%,#0f1629 100%);--sap-shadow-card:0 4px 24px rgba(0,0,0,0.4);--sap-radius:8px;--sap-radius-sm:4px;--sap-radius-lg:12px;position:fixed;inset:0;z-index:9999999;display:none;align-items:center;justify-content:center;padding:20px;background:rgba(5,8,18,0.72);backdrop-filter:blur(6px);font-family:'IBM Plex Sans','Plus Jakarta Sans',sans-serif;color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay.sap-show{display:flex;}",
            "#sipAssetPopupOverlay *{box-sizing:border-box;margin:0;padding:0;}",
            "#sipAssetPopupOverlay .sap-popup{width:1240px;max-width:96vw;height:min(780px,92vh);max-height:92vh;background:var(--sap-bg-panel);border:1px solid var(--sap-border);border-radius:var(--sap-radius-lg);box-shadow:var(--sap-shadow-card),0 0 60px rgba(0,212,255,0.06);display:flex;flex-direction:column;overflow:hidden;animation:sapSlideUp .25s cubic-bezier(.16,1,.3,1);}",
            "@keyframes sapSlideUp{from{opacity:0;transform:translateY(20px) scale(.985)}to{opacity:1;transform:translateY(0) scale(1)}}",
            "#sipAssetPopupOverlay .sap-popup-header{background:var(--sap-gradient-header);padding:12px 20px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--sap-border);flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-popup-header-left{display:flex;align-items:center;gap:10px;min-width:0;}",
            "#sipAssetPopupOverlay .sap-asset-icon{width:36px;height:36px;border-radius:var(--sap-radius);background:linear-gradient(135deg,rgba(0,212,255,0.15),rgba(0,229,160,0.1));border:1px solid rgba(0,212,255,0.25);display:flex;align-items:center;justify-content:center;flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-asset-icon svg{width:18px;height:18px;color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-asset-name{font-size:17px;font-weight:600;letter-spacing:-0.01em;color:#fff;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}",
            "#sipAssetPopupOverlay .sap-asset-location{font-size:12px;color:var(--sap-text-secondary);margin-top:1px;}",
            "#sipAssetPopupOverlay .sap-header-actions{display:flex;align-items:center;gap:8px;}",
            "#sipAssetPopupOverlay .sap-maint-mode-btn,#sipAssetPopupOverlay .sap-close-btn,#sipAssetPopupOverlay .sap-btn,#sipAssetPopupOverlay .sap-filter-apply,#sipAssetPopupOverlay .sap-filter-clear,#sipAssetPopupOverlay .sap-range-btn,#sipAssetPopupOverlay .sap-m-btn{font-family:inherit;cursor:pointer;transition:all .15s;}",
            "#sipAssetPopupOverlay .sap-maint-mode-btn{font-size:12px;font-weight:600;padding:7px 16px;border-radius:20px;border:1px solid rgba(245,158,11,0.4);background:rgba(245,158,11,0.1);color:var(--sap-accent-yellow);display:flex;align-items:center;gap:6px;}",
            "#sipAssetPopupOverlay .sap-maint-mode-btn:hover{background:rgba(245,158,11,0.2);border-color:rgba(245,158,11,0.6);}",
            "#sipAssetPopupOverlay .sap-maint-mode-btn.sap-active-mode{background:rgba(245,158,11,0.2);border-color:var(--sap-accent-yellow);box-shadow:0 0 12px rgba(245,158,11,0.2);}",
            "#sipAssetPopupOverlay .sap-maint-mode-btn .sap-wrench{width:14px;height:14px;}",
            "#sipAssetPopupOverlay .sap-close-btn{width:32px;height:32px;border-radius:var(--sap-radius);border:1px solid var(--sap-border);background:rgba(255,255,255,0.03);color:var(--sap-text-secondary);display:flex;align-items:center;justify-content:center;}",
            "#sipAssetPopupOverlay .sap-close-btn:hover{background:rgba(239,68,68,0.15);color:var(--sap-accent-red);border-color:rgba(239,68,68,0.3);}",
            "#sipAssetPopupOverlay .sap-tabs-bar{display:flex;align-items:center;padding:0 20px;background:var(--sap-bg-header);border-bottom:1px solid var(--sap-border);gap:2px;flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-tab{font-size:13px;font-weight:500;padding:11px 16px;color:var(--sap-text-muted);cursor:pointer;position:relative;transition:color .2s;user-select:none;border:none;background:none;font-family:inherit;}",
            "#sipAssetPopupOverlay .sap-tab:hover{color:var(--sap-text-secondary);}",
            "#sipAssetPopupOverlay .sap-tab.sap-active{color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-tab.sap-active::after{content:'';position:absolute;bottom:0;left:12px;right:12px;height:2px;background:var(--sap-accent-cyan);border-radius:2px 2px 0 0;}",
            "#sipAssetPopupOverlay .sap-tab-alarm{color:var(--sap-accent-red)!important;display:flex;align-items:center;gap:5px;}",
            "#sipAssetPopupOverlay .sap-dot{width:6px;height:6px;border-radius:50%;background:var(--sap-accent-red);animation:sapPulseDot 2s infinite;}",
            "@keyframes sapPulseDot{0%,100%{opacity:1}50%{opacity:.3}}",
            "#sipAssetPopupOverlay .sap-tab-content{display:none;flex:1;overflow:hidden;min-height:0;}",
            "#sipAssetPopupOverlay .sap-tab-content.sap-active{display:flex;flex-direction:column;min-height:0;overflow:hidden;}",
            "#sipAssetPopupOverlay .sap-tab-content::-webkit-scrollbar{width:5px;}",
            "#sipAssetPopupOverlay .sap-tab-content::-webkit-scrollbar-thumb{background:var(--sap-border);border-radius:4px;}",
            "#sipAssetPopupOverlay .sap-telemetry-header{display:flex;align-items:center;justify-content:space-between;padding:12px 20px;background:linear-gradient(90deg,rgba(0,212,255,0.06),transparent);border-bottom:1px solid var(--sap-border);flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-telemetry-title{font-size:13px;font-weight:600;color:var(--sap-accent-cyan);display:flex;align-items:center;gap:8px;text-transform:uppercase;letter-spacing:0.06em;}",
            "#sipAssetPopupOverlay .sap-live-dot{width:7px;height:7px;background:var(--sap-accent-green);border-radius:50%;box-shadow:0 0 6px rgba(34,197,94,0.6);animation:sapPulseDot 1.5s infinite;}",
            "#sipAssetPopupOverlay .sap-bell-icon{width:32px;height:32px;border-radius:var(--sap-radius);background:rgba(0,212,255,0.08);border:1px solid rgba(0,212,255,0.15);display:flex;align-items:center;justify-content:center;color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-telemetry-grid{display:grid;grid-template-columns:1fr 1fr;flex:1;min-height:180px;}",
            "#sipAssetPopupOverlay .sap-telemetry-col{display:flex;flex-direction:column;}",
            "#sipAssetPopupOverlay .sap-telemetry-col:first-child{border-right:1px solid var(--sap-border);}",
            "#sipAssetPopupOverlay .sap-telem-row{display:flex;justify-content:space-between;align-items:center;gap:18px;padding:11px 20px;border-bottom:1px solid rgba(30,42,69,0.5);transition:background .12s;}",
            "#sipAssetPopupOverlay .sap-telem-row:hover{background:var(--sap-bg-row-hover);}",
            "#sipAssetPopupOverlay .sap-telem-row:nth-child(even){background:var(--sap-bg-row-alt);}",
            "#sipAssetPopupOverlay .sap-telem-row:nth-child(even):hover{background:var(--sap-bg-row-hover);}",
            "#sipAssetPopupOverlay .sap-telem-label{font-size:13px;color:var(--sap-text-secondary);font-weight:500;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}",
            "#sipAssetPopupOverlay .sap-telem-value{font-family:'JetBrains Mono',monospace;font-size:13px;font-weight:500;color:var(--sap-text-primary);text-align:right;word-break:break-word;}",
            "#sipAssetPopupOverlay .sap-value-ok{color:var(--sap-accent-green)!important;font-family:'IBM Plex Sans',sans-serif!important;font-weight:600!important;display:inline-flex;align-items:center;gap:6px;}",
            "#sipAssetPopupOverlay .sap-value-ok::before{content:'';width:8px;height:8px;border-radius:50%;background:var(--sap-accent-green);box-shadow:0 0 8px rgba(34,197,94,0.5);}",
            "#sipAssetPopupOverlay .sap-value-warn{color:var(--sap-accent-yellow)!important;}",
            "#sipAssetPopupOverlay .sap-value-danger{color:var(--sap-accent-red)!important;}",
            "#sipAssetPopupOverlay .sap-empty-state{grid-column:1/-1;padding:28px 20px;color:var(--sap-text-muted);font-size:13px;display:flex;align-items:center;justify-content:center;text-align:center;line-height:1.5;}",
            "#sipAssetPopupOverlay .sap-analytics-bar,#sipAssetPopupOverlay .sap-graph-bar,#sipAssetPopupOverlay .sap-event-bar{display:flex;align-items:center;gap:14px;padding:12px 20px;border-bottom:1px solid var(--sap-border);flex-shrink:0;flex-wrap:wrap;}",
            "#sipAssetPopupOverlay .sap-filter-group{display:flex;align-items:center;gap:6px;}",
            "#sipAssetPopupOverlay .sap-filter-label{font-size:11px;color:var(--sap-text-muted);font-weight:500;white-space:nowrap;}",
            "#sipAssetPopupOverlay .sap-date-input,#sipAssetPopupOverlay .sap-graph-select{font-family:inherit;font-size:11px;padding:5px 10px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius-sm);color:var(--sap-text-primary);outline:none;color-scheme:dark;}",
            "#sipAssetPopupOverlay .sap-filter-apply{font-size:11px;font-weight:500;padding:5px 14px;border-radius:var(--sap-radius-sm);border:1px solid rgba(0,212,255,0.3);background:rgba(0,212,255,0.1);color:var(--sap-accent-cyan);display:flex;align-items:center;gap:4px;}",
            "#sipAssetPopupOverlay .sap-filter-clear,#sipAssetPopupOverlay .sap-range-btn{font-size:11px;font-weight:500;padding:5px 10px;border-radius:var(--sap-radius-sm);border:1px solid var(--sap-border);background:transparent;color:var(--sap-text-muted);}",
            "#sipAssetPopupOverlay .sap-filter-clear:hover,#sipAssetPopupOverlay .sap-range-btn:hover{color:var(--sap-text-secondary);border-color:var(--sap-border-glow);}",
            "#sipAssetPopupOverlay .sap-range-btn.sap-active{background:rgba(0,212,255,0.1);color:var(--sap-accent-cyan);border-color:rgba(0,212,255,0.3);}",
            "#sipAssetPopupOverlay .sap-graph-range-btns{display:flex;gap:4px;margin-left:auto;}",
            "#sipAssetPopupOverlay .sap-panel-body{flex:1;padding:16px 20px;overflow-y:auto;}",
            "#sipAssetPopupOverlay .sap-section-tag{padding:8px 0 10px;font-size:10px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;display:flex;align-items:center;gap:8px;color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-section-tag::after{content:'';flex:1;height:1px;background:linear-gradient(90deg,rgba(0,212,255,.25),transparent);}",
            "#sipAssetPopupOverlay .sap-section-tag.sap-failure{color:var(--sap-accent-red);}",
            "#sipAssetPopupOverlay .sap-section-tag.sap-failure::after{background:linear-gradient(90deg,rgba(239,68,68,.25),transparent);}",
            "#sipAssetPopupOverlay .sap-cause-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(140px,1fr));gap:8px;}",
            "#sipAssetPopupOverlay .sap-cause-card{background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:6px;overflow:hidden;}",
            "#sipAssetPopupOverlay .sap-cc-head{padding:6px 8px;font-size:10px;font-weight:600;color:#fff;line-height:1.3;min-height:32px;display:flex;align-items:center;background:linear-gradient(135deg,rgba(0,212,255,0.18),rgba(0,212,255,0.04));border-bottom:1px solid rgba(0,212,255,0.12);}",
            "#sipAssetPopupOverlay .sap-cc-head.sap-f{background:linear-gradient(135deg,rgba(239,68,68,0.2),rgba(239,68,68,0.06));border-bottom:1px solid rgba(239,68,68,0.15);}",
            "#sipAssetPopupOverlay .sap-cc-body{padding:5px 8px 7px;display:flex;align-items:center;justify-content:space-between;gap:8px;}",
            "#sipAssetPopupOverlay .sap-cc-count{font-family:'JetBrains Mono',monospace;font-size:18px;font-weight:700;}",
            "#sipAssetPopupOverlay .sap-cc-count.sap-hot{color:var(--sap-accent-red);}",
            "#sipAssetPopupOverlay .sap-cc-code{font-family:'JetBrains Mono',monospace;font-size:8px;color:var(--sap-text-muted);text-align:right;line-height:1.25;word-break:break-word;}",
            "#sipAssetPopupOverlay .sap-attr-filter-wrap{position:relative;}",
            "#sipAssetPopupOverlay .sap-attr-filter-btn{font-family:inherit;font-size:11px;font-weight:500;padding:5px 12px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius-sm);color:var(--sap-text-secondary);cursor:pointer;display:flex;align-items:center;gap:6px;white-space:nowrap;}",
            "#sipAssetPopupOverlay .sap-attr-filter-btn:hover{border-color:var(--sap-border-glow);color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay .sap-attr-filter-btn.sap-open{border-color:rgba(0,212,255,0.45);color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-attr-dropdown{position:absolute;top:calc(100% + 4px);left:0;z-index:9999;min-width:220px;max-width:320px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius);box-shadow:0 8px 24px rgba(0,0,0,0.45);display:none;flex-direction:column;}",
            "#sipAssetPopupOverlay .sap-attr-dropdown.sap-open{display:flex;}",
            "#sipAssetPopupOverlay .sap-attr-dd-header{display:flex;align-items:center;gap:6px;padding:8px 10px;border-bottom:1px solid var(--sap-border);flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-attr-dd-header span{font-size:10px;font-weight:600;color:var(--sap-text-muted);text-transform:uppercase;letter-spacing:.05em;flex:1;}",
            "#sipAssetPopupOverlay .sap-attr-dd-header button{font-size:10px;font-weight:500;padding:2px 8px;border-radius:3px;border:1px solid var(--sap-border);background:transparent;color:var(--sap-text-muted);cursor:pointer;}",
            "#sipAssetPopupOverlay .sap-attr-dd-header button:hover{color:var(--sap-accent-cyan);border-color:rgba(0,212,255,0.35);}",
            "#sipAssetPopupOverlay .sap-attr-dd-list{max-height:220px;overflow-y:auto;padding:4px 0;}",
            "#sipAssetPopupOverlay .sap-attr-dd-list::-webkit-scrollbar{width:4px;}",
            "#sipAssetPopupOverlay .sap-attr-dd-list::-webkit-scrollbar-thumb{background:var(--sap-border);border-radius:2px;}",
            "#sipAssetPopupOverlay .sap-attr-cb-row{display:flex;align-items:center;gap:8px;padding:5px 12px;cursor:pointer;transition:background .15s;}",
            "#sipAssetPopupOverlay .sap-attr-cb-row:hover{background:rgba(255,255,255,0.04);}",
            "#sipAssetPopupOverlay .sap-attr-cb-row input[type=checkbox]{accent-color:var(--sap-accent-cyan);width:13px;height:13px;flex-shrink:0;cursor:pointer;}",
            "#sipAssetPopupOverlay .sap-attr-cb-label{font-size:11px;color:var(--sap-text-secondary);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;cursor:pointer;}",
            "#sipAssetPopupOverlay .sap-attr-cb-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-chart-wrap{flex:1 1 auto;padding:0 20px 16px;min-height:0;overflow:hidden;display:flex;flex-direction:column;}",
            "#sipAssetPopupOverlay .sap-chart-area{flex:0 0 clamp(460px,calc(92vh - 240px),620px);height:clamp(460px,calc(92vh - 240px),620px);min-height:460px;max-height:620px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius);position:relative;overflow:hidden;}",
            "#sipAssetPopupOverlay .sap-chart-canvas{position:absolute;inset:0;display:block;width:100%!important;height:100%!important;max-width:100%;max-height:100%;}",
            "#sipAssetPopupOverlay .sap-event-log{padding:0 20px;flex:1;overflow-y:auto;}",
            "#sipAssetPopupOverlay .sap-event-row{display:flex;align-items:flex-start;gap:12px;padding:10px 0;border-bottom:1px solid rgba(30,42,69,0.4);}",
            "#sipAssetPopupOverlay .sap-event-time{font-family:'JetBrains Mono',monospace;font-size:11px;color:var(--sap-text-muted);white-space:nowrap;padding-top:2px;min-width:130px;}",
            "#sipAssetPopupOverlay .sap-event-dot{width:8px;height:8px;border-radius:50%;flex-shrink:0;margin-top:5px;background:var(--sap-accent-blue);}",
            "#sipAssetPopupOverlay .sap-event-dot.sap-warn{background:var(--sap-accent-yellow);}",
            "#sipAssetPopupOverlay .sap-event-dot.sap-error{background:var(--sap-accent-red);}",
            "#sipAssetPopupOverlay .sap-event-dot.sap-success{background:var(--sap-accent-green);}",
            "#sipAssetPopupOverlay .sap-event-text{font-size:13px;color:var(--sap-text-primary);line-height:1.5;}",
            "#sipAssetPopupOverlay .sap-code{font-family:'JetBrains Mono',monospace;font-size:11px;background:rgba(0,212,255,0.08);padding:1px 6px;border-radius:3px;color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-alarm-banner{display:flex;align-items:center;gap:12px;padding:14px 16px;background:rgba(239,68,68,0.08);border:1px solid rgba(239,68,68,0.2);border-radius:var(--sap-radius);margin-bottom:16px;}",
            "#sipAssetPopupOverlay .sap-alarm-banner-icon{width:36px;height:36px;border-radius:50%;background:rgba(239,68,68,0.15);display:flex;align-items:center;justify-content:center;flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-alarm-banner-text{font-size:13px;color:var(--sap-accent-red);font-weight:500;line-height:1.5;}",
            "#sipAssetPopupOverlay .sap-alarm-banner-text span{font-weight:400;color:var(--sap-text-secondary);display:block;font-size:12px;}",
            "#sipAssetPopupOverlay .sap-alarm-table{width:100%;border-collapse:collapse;}",
            "#sipAssetPopupOverlay .sap-alarm-table th{font-size:11px;font-weight:600;color:var(--sap-text-muted);text-transform:uppercase;letter-spacing:.05em;padding:10px 14px;text-align:left;border-bottom:1px solid var(--sap-border);}",
            "#sipAssetPopupOverlay .sap-alarm-table td{font-size:12px;padding:10px 14px;border-bottom:1px solid rgba(30,42,69,0.4);color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay .sap-alarm-status{display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:500;padding:3px 10px;border-radius:12px;background:rgba(239,68,68,0.12);color:var(--sap-accent-red);}",
            "#sipAssetPopupOverlay .sap-alarm-status.sap-cleared{background:rgba(34,197,94,0.12);color:var(--sap-accent-green);}",
            "#sipAssetPopupOverlay .sap-popup-footer{padding:10px 20px;border-top:1px solid var(--sap-border);display:flex;align-items:center;justify-content:space-between;background:var(--sap-bg-header);flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-footer-meta{font-size:11px;color:var(--sap-text-muted);display:flex;gap:16px;flex-wrap:nowrap;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0;}",
            "#sipAssetPopupOverlay .sap-footer-actions{display:flex;gap:8px;flex-shrink:0;}",
            "#sipAssetPopupOverlay .sap-btn{font-size:12px;font-weight:500;padding:7px 16px;border-radius:var(--sap-radius-sm);border:1px solid var(--sap-border);background:transparent;color:var(--sap-text-secondary);display:flex;align-items:center;gap:6px;}",
            "#sipAssetPopupOverlay .sap-btn:hover{border-color:var(--sap-border-glow);color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay .sap-btn-primary{background:rgba(0,212,255,0.1);border-color:rgba(0,212,255,0.3);color:var(--sap-accent-cyan);}",
            "#sipAssetPopupOverlay .sap-maint-modal-bg{display:none;position:fixed;inset:0;z-index:5010;background:rgba(5,8,18,0.6);backdrop-filter:blur(4px);align-items:center;justify-content:center;}",
            "#sipAssetPopupOverlay .sap-maint-modal-bg.sap-show{display:flex;}",
            "#sipAssetPopupOverlay .sap-maint-modal{width:440px;max-width:92vw;background:var(--sap-bg-panel);border:1px solid var(--sap-border);border-radius:var(--sap-radius-lg);box-shadow:var(--sap-shadow-card);padding:24px;animation:sapSlideUp .25s ease;}",
            "#sipAssetPopupOverlay .sap-maint-modal h3{font-size:16px;font-weight:600;color:#fff;margin-bottom:4px;display:flex;align-items:center;gap:8px;}",
            "#sipAssetPopupOverlay .sap-maint-modal .sap-sub{font-size:12px;color:var(--sap-text-muted);margin-bottom:20px;}",
            "#sipAssetPopupOverlay .sap-maint-field{margin-bottom:14px;}",
            "#sipAssetPopupOverlay .sap-maint-field label{display:block;font-size:11px;font-weight:600;color:var(--sap-text-secondary);text-transform:uppercase;letter-spacing:.04em;margin-bottom:5px;}",
            "#sipAssetPopupOverlay .sap-maint-field input,#sipAssetPopupOverlay .sap-maint-field textarea,#sipAssetPopupOverlay .sap-maint-field select{width:100%;font-family:inherit;font-size:13px;padding:8px 12px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius-sm);color:var(--sap-text-primary);outline:none;color-scheme:dark;}",
            "#sipAssetPopupOverlay .sap-maint-field textarea{resize:vertical;min-height:60px;}",
            "#sipAssetPopupOverlay .sap-maint-row{display:flex;gap:12px;}",
            "#sipAssetPopupOverlay .sap-maint-row .sap-maint-field{flex:1;}",
            "#sipAssetPopupOverlay .sap-maint-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:20px;}",
            "#sipAssetPopupOverlay .sap-m-btn{font-size:13px;font-weight:500;padding:8px 20px;border-radius:var(--sap-radius-sm);border:1px solid var(--sap-border);background:transparent;color:var(--sap-text-secondary);}",
            "#sipAssetPopupOverlay .sap-m-btn:hover{border-color:var(--sap-border-glow);color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay .sap-m-btn.sap-primary{background:rgba(245,158,11,0.15);border-color:rgba(245,158,11,0.4);color:var(--sap-accent-yellow);}",
            "@media(max-width:760px){#sipAssetPopupOverlay{padding:10px;}#sipAssetPopupOverlay .sap-popup{height:92vh;max-height:92vh;}#sipAssetPopupOverlay .sap-chart-area{flex-basis:calc(92vh - 260px);height:calc(92vh - 260px);min-height:320px;max-height:none;}#sipAssetPopupOverlay .sap-telemetry-grid{grid-template-columns:1fr;}#sipAssetPopupOverlay .sap-telemetry-col:first-child{border-right:none;}#sipAssetPopupOverlay .sap-tabs-bar{overflow-x:auto;padding:0 10px;}#sipAssetPopupOverlay .sap-popup-footer{align-items:flex-start;flex-direction:column;gap:10px;}}"
        ].join('\n');
        document.head.appendChild(s);
    }

    /* ═══════════════════════════════════════════════════════════════
       HTML INJECTION — exact copy of Index.cshtml lines 1326–1460
       Only injected if #sipAssetPopupOverlay doesn't exist yet.
       ═══════════════════════════════════════════════════════════════ */
    function isValidSapPopupOverlay(overlay) {
        return !!(
            overlay &&
            overlay.querySelector('.sap-popup') &&
            overlay.querySelector('.sap-tab') &&
            overlay.querySelector('.sap-tab-content') &&
            overlay.querySelector('#sipAssetTelemetryGrid')
        );
    }

    function injectHTMLIfNeeded() {
        var existing = el('sipAssetPopupOverlay');

        // Remove old telemetry fallback popup / wrong HTML using same ID.
        if (existing) {
            if (isValidSapPopupOverlay(existing)) return;

            try {
                existing.parentNode.removeChild(existing);
            } catch (e) { }

            var oldStyle = el('sipAssetPopupStyle');
            if (oldStyle && oldStyle.parentNode) {
                oldStyle.parentNode.removeChild(oldStyle);
            }
        }

        var WRENCH = '<svg class="sap-wrench" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z"/></svg>';
        var CLOSE = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><path d="M18 6L6 18M6 6l12 12"/></svg>';
        var BELL = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 01-3.46 0"/></svg>';
        var ALARM_TRI = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>';
        var DL = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>';
        var RPT = '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
        var td = today();

        var div = document.createElement('div');
        div.className = 'sip-asset-popup-overlay';
        div.id = 'sipAssetPopupOverlay';
        div.setAttribute('aria-hidden', 'true');
        div.innerHTML =
            '<div class="sap-popup" role="dialog" aria-modal="true">' +
            '<div class="sap-popup-header"><div class="sap-popup-header-left"><div class="sap-asset-icon" id="sipAssetIcon">' + ICONS.Track + '</div><div style="min-width:0"><div class="sap-asset-name" id="sipAssetPopupAssetName">Selected Asset</div><div class="sap-asset-location" id="sipAssetPopupAssetLocation">\u2014</div></div></div><div class="sap-header-actions"><button type="button" class="sap-maint-mode-btn" id="sipAssetMaintBtn">' + WRENCH + ' Maintenance Mode</button><button type="button" class="sap-close-btn" id="sipAssetPopupClose">' + CLOSE + '</button></div></div>' +
            '<div class="sap-tabs-bar"><button type="button" class="sap-tab sap-active" data-sap-tab="live">Live</button><button type="button" class="sap-tab" data-sap-tab="analytics">Alert Analytics</button><button type="button" class="sap-tab" data-sap-tab="graph">Graph</button><button type="button" class="sap-tab" data-sap-tab="eventlog">Event Log</button><button type="button" class="sap-tab sap-tab-alarm" data-sap-tab="alarm" id="sipAlarmTab"><span class="sap-dot"></span> Alarm</button></div>' +
            /* LIVE */
            '<div class="sap-tab-content sap-active" id="sap-tab-live"><div class="sap-telemetry-header"><div class="sap-telemetry-title"><span class="sap-live-dot"></span><span id="sipAssetTelemetryTitle">Live Telemetry</span></div><div class="sap-bell-icon">' + BELL + '</div></div><div class="sap-telemetry-grid" id="sipAssetTelemetryGrid"><div class="sap-empty-state">Click an asset on the SIP canvas to load its live attribute values.</div></div></div>' +
            /* ANALYTICS */
            '<div class="sap-tab-content" id="sap-tab-analytics"><div class="sap-analytics-bar"><div class="sap-filter-group"><span class="sap-filter-label">From</span><input type="date" class="sap-date-input" id="sipAssetAnalyticsFrom" value="' + td + '"></div><div class="sap-filter-group"><span class="sap-filter-label">To</span><input type="date" class="sap-date-input" id="sipAssetAnalyticsTo" value="' + td + '"></div><button type="button" class="sap-filter-apply">Apply</button><button type="button" class="sap-filter-clear">\u2715 Clear</button></div><div class="sap-panel-body" id="sipAssetAnalyticsBody"><div class="sap-empty-state">Select an asset to view alert analytics.</div></div></div>' +
            /* GRAPH */
            '<div class="sap-tab-content" id="sap-tab-graph"><div class="sap-graph-bar"><div class="sap-attr-filter-wrap"><button type="button" class="sap-attr-filter-btn" id="sipAttrFilterBtn">Attributes <span id="sipAttrCountBadge" style="font-size:10px;opacity:.75;">(all)</span> &#9660;</button><div class="sap-attr-dropdown" id="sipAttrDropdown"><div class="sap-attr-dd-header"><span>Plot Attributes</span><button type="button" id="sipAttrSelectAll">All</button><button type="button" id="sipAttrClearAll">None</button></div><div class="sap-attr-dd-list" id="sipAttrList"><div style="padding:10px 12px;font-size:11px;color:var(--sap-text-muted);">Loading…</div></div></div></div><div class="sap-filter-group"><span class="sap-filter-label">Date</span><input type="date" class="sap-date-input" id="sipAssetGraphDate" value="' + td + '"></div><span class="sap-filter-label" id="sipGraphTimeRange" style="margin-left:auto;font-family:JetBrains Mono,monospace;font-size:11px;color:var(--sap-accent-cyan);"></span></div><div class="sap-chart-wrap"><div class="sap-chart-area"><canvas class="sap-chart-canvas" id="sipAssetChartCanvas"></canvas></div></div></div>' +
            /* EVENTLOG */
            '<div class="sap-tab-content" id="sap-tab-eventlog"><div class="sap-event-bar"><div class="sap-filter-group"><span class="sap-filter-label">From</span><input type="date" class="sap-date-input" id="sipAssetEventFrom" value="' + td + '"></div><div class="sap-filter-group"><span class="sap-filter-label">To</span><input type="date" class="sap-date-input" id="sipAssetEventTo" value="' + td + '"></div><button type="button" class="sap-filter-apply">Apply</button><button type="button" class="sap-filter-clear">\u2715 Clear</button></div><div class="sap-event-log" id="sipAssetEventLog"><div class="sap-empty-state">Select an asset to view events.</div></div></div>' +
            /* ALARM */
            '<div class="sap-tab-content" id="sap-tab-alarm"><div class="sap-panel-body"><div class="sap-alarm-banner"><div class="sap-alarm-banner-icon">' + ALARM_TRI + '</div><div class="sap-alarm-banner-text" id="sipAssetAlarmBannerText">Alarm summary<span>Live alarm binding can be connected to your alarm endpoint.</span></div></div><table class="sap-alarm-table"><thead><tr><th>Cause Code</th><th>Raised At</th><th>Duration</th><th>Severity</th><th>Status</th></tr></thead><tbody id="sipAssetAlarmRows"><tr><td colspan="5" style="color:var(--sap-text-muted)">No alarms loaded yet.</td></tr></tbody></table></div></div>' +
            /* FOOTER */
            '<div class="sap-popup-footer"><div class="sap-footer-meta"><span id="sipAssetLastSync">Last sync: \u2014</span><span id="sipAssetFooterType">Type: \u2014</span><span>FRS: 25</span></div><div class="sap-footer-actions"><button type="button" class="sap-btn">' + DL + ' Export</button><button type="button" class="sap-btn sap-btn-primary">' + RPT + ' SIP Report</button></div></div>' +
            '</div>' +
            /* MAINTENANCE MODAL */
            '<div class="sap-maint-modal-bg" id="sipAssetMaintModal"><div class="sap-maint-modal"><h3><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z"/></svg>Create Maintenance Mode</h3><div class="sap-sub" id="sipAssetMaintSub">Put selected asset into maintenance mode.</div><div class="sap-maint-row"><div class="sap-maint-field"><label>Start</label><input type="datetime-local" id="sipAssetMaintStart"></div><div class="sap-maint-field"><label>End</label><input type="datetime-local" id="sipAssetMaintEnd"></div></div><div class="sap-maint-field"><label>Reason</label><select><option>Scheduled Maintenance</option><option>Emergency Repair</option><option>Inspection</option><option>Component Replacement</option><option>Other</option></select></div><div class="sap-maint-field"><label>Maintainer</label><input type="text" placeholder="Name or employee ID"></div><div class="sap-maint-field"><label>Remarks</label><textarea placeholder="Additional notes..."></textarea></div><div class="sap-maint-actions"><button type="button" class="sap-m-btn" id="sipAssetMaintCancel">Cancel</button><button type="button" class="sap-m-btn sap-primary" id="sipAssetMaintActivate">Activate</button></div></div></div>';

        document.body.appendChild(div);
    }

    function wireEvents() {
        var overlay = el('sipAssetPopupOverlay');
        if (!overlay) return;

        // Prevent duplicate event binding
        if (overlay._sapEventsBound) return;
        overlay._sapEventsBound = true;

        // ── Close popup button ─────────────────────────────
        var closeBtn = el('sipAssetPopupClose');
        if (closeBtn) {
            closeBtn.addEventListener('click', function () {
                closePopup();
            });
        }

        // ── Close popup when clicking outside modal box ─────
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                closePopup();
            }
        });

        // ── Close popup with ESC key ────────────────────────
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && overlay.classList.contains('sap-show')) {
                closePopup();
            }
        });

        overlay.addEventListener('click', function (e) {
            var tab = e.target && e.target.closest ? e.target.closest('.sap-tab') : null;

            if (!tab || !overlay.contains(tab)) return;

            e.preventDefault();
            e.stopPropagation();

            var tabName = tab.getAttribute('data-sap-tab');
            activateTab(tabName);
        });

        // ── Graph attribute checkbox dropdown ──────────────
        var attrFilterBtn = el('sipAttrFilterBtn');
        var attrDropdown  = el('sipAttrDropdown');
        if (attrFilterBtn && attrDropdown) {
            attrFilterBtn.addEventListener('click', function (e) {
                e.stopPropagation();
                var open = attrDropdown.classList.toggle('sap-open');
                attrFilterBtn.classList.toggle('sap-open', open);
            });
            document.addEventListener('click', function (e) {
                var wrap = el('sipAttrDropdown');
                if (wrap && !wrap.contains(e.target) && e.target !== el('sipAttrFilterBtn')) {
                    wrap.classList.remove('sap-open');
                    var btn = el('sipAttrFilterBtn');
                    if (btn) btn.classList.remove('sap-open');
                }
            });
        }
        var selAllBtn  = el('sipAttrSelectAll');
        var clrAllBtn  = el('sipAttrClearAll');
        if (selAllBtn) selAllBtn.addEventListener('click', function () { setAllGraphCheckboxes(true);  });
        if (clrAllBtn) clrAllBtn.addEventListener('click', function () { setAllGraphCheckboxes(false); });

        var graphDate = el('sipAssetGraphDate');
        if (graphDate) {
            graphDate.addEventListener('change', function () {
                loadGraphHistory();
            });
        }

        // ── Graph hover tooltip ─────────────────────────────
        wireChartHover();

        // ── Alert Analytics Apply / Clear buttons ───────────
        var analyticsTab = el('sap-tab-analytics');

        if (analyticsTab) {
            var applyBtn = analyticsTab.querySelector('.sap-filter-apply');
            var clearBtn = analyticsTab.querySelector('.sap-filter-clear');

            if (applyBtn) {
                applyBtn.addEventListener('click', function () {
                    loadSipAssetAlertAnalytics();
                });
            }

            if (clearBtn) {
                clearBtn.addEventListener('click', function () {
                    if (el('sipAssetAnalyticsFrom')) {
                        el('sipAssetAnalyticsFrom').value = today();
                    }

                    if (el('sipAssetAnalyticsTo')) {
                        el('sipAssetAnalyticsTo').value = today();
                    }

                    loadSipAssetAlertAnalytics();
                });
            }
        }

        // ── Maintenance Mode button ─────────────────────────
        // ── Event Log Apply / Clear buttons ──────────────────
        var eventTab = el('sap-tab-eventlog');
        if (eventTab) {
            var evApply = eventTab.querySelector('.sap-filter-apply');
            var evClear = eventTab.querySelector('.sap-filter-clear');
            if (evApply) evApply.addEventListener('click', function () { loadEventLog(); });
            if (evClear) {
                evClear.addEventListener('click', function () {
                    if (el('sipAssetEventFrom')) el('sipAssetEventFrom').value = today();
                    if (el('sipAssetEventTo')) el('sipAssetEventTo').value = today();
                    loadEventLog();
                });
            }
        }

        // ── Maintenance Mode button ─────────────────────────
        var maintBtn = el('sipAssetMaintBtn');
        var maintModal = el('sipAssetMaintModal');
        var maintCancel = el('sipAssetMaintCancel');
        var maintActivate = el('sipAssetMaintActivate');

        if (maintBtn && maintModal) {
            maintBtn.addEventListener('click', function () {
                maintModal.classList.add('sap-show');
            });
        }

        if (maintCancel && maintModal) {
            maintCancel.addEventListener('click', function () {
                maintModal.classList.remove('sap-show');
            });
        }

        if (maintActivate && maintModal && maintBtn) {
            maintActivate.addEventListener('click', function () {
                maintModal.classList.remove('sap-show');
                maintBtn.classList.add('sap-active-mode');
                maintBtn.innerHTML =
                    '<svg class="sap-wrench" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                    '<path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z"/>' +
                    '</svg>' +
                    ' Maint. Mode Active';
            });
        }

        // ── Close maintenance modal when clicking outside ───
        if (maintModal) {
            maintModal.addEventListener('click', function (e) {
                if (e.target === maintModal) {
                    maintModal.classList.remove('sap-show');
                }
            });
        }
    }

    /* ═══════════════════════════════════════════════════════════════
       INIT — ensure everything is ready (idempotent)
       ═══════════════════════════════════════════════════════════════ */
    function ensureReady() {
        var overlayNow = el('sipAssetPopupOverlay');

        // If the page/tab switch removed or replaced the popup DOM, rebuild/re-wire it.
        if (_ready) {
            if (overlayNow && sapNodeExists(overlayNow) && overlayNow.querySelector('.sap-tab') && overlayNow.querySelector('.sap-tab-content')) {
                if (!overlayNow._sapEventsBound) wireEvents();
                return;
            }
            _ready = false;
        }

        if (!document.querySelector('link[href*="IBM+Plex+Sans"]')) {
            var lk = document.createElement('link'); lk.rel = 'stylesheet';
            lk.href = 'https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap';
            document.head.appendChild(lk);
        }

        injectCSSIfNeeded();
        injectHTMLIfNeeded();
        wireEvents();

        overlayNow = el('sipAssetPopupOverlay');
        _ready = !!(overlayNow && sapNodeExists(overlayNow));
    }

    /* ── tabs ─────────────────────────────────────────────────── */
    function activateTab(name) {
        ensureReady();

        name = String(name || 'live').toLowerCase();

        var ov = el('sipAssetPopupOverlay');
        if (!ov || !sapNodeExists(ov)) {
            _ready = false;
            return false;
        }

        var targetContent = el('sap-tab-' + name);
        if (!targetContent || !sapNodeExists(targetContent)) {
            // Bad / missing tab node should not break the popup.
            name = 'live';
            targetContent = el('sap-tab-live');
            if (!targetContent || !sapNodeExists(targetContent)) return false;
        }

        _activeTab = name;

        var tabs = ov.querySelectorAll('.sap-tab');
        for (var ti = 0; ti < tabs.length; ti++) {
            var t = tabs[ti];
            if (!sapNodeExists(t)) continue;
            t.classList.toggle('sap-active', t.getAttribute('data-sap-tab') === name);
        }

        var contents = ov.querySelectorAll('.sap-tab-content');
        for (var ci = 0; ci < contents.length; ci++) {
            var c = contents[ci];
            if (!sapNodeExists(c)) continue;
            c.classList.toggle('sap-active', c.id === 'sap-tab-' + name);
        }

        // Stop graph work and cancel pending graph responses when leaving Graph.
        if (name !== 'graph') {
            clearInterval(_graphRefreshTimer);
            _graphRefreshTimer = null;
            _graphRequestNo++;
            _chartGeo = null;
        }

        if (name === 'live') {
            // Force rebuild on return to Live tab so hidden-tab DOM never appears frozen.
            _lastLiveSignature = '';
            sapSafeRefreshLive('tab-live');
            return true;
        }

        if (name === 'analytics') {
            setTimeout(function () {
                if (_activeTab === 'analytics' && sapIsOverlayVisible()) sapSafeCall('load analytics', loadSipAssetAlertAnalytics);
            }, 30);
            return true;
        }

        if (name === 'graph') {
            _graphPoints = []; _graphSeriesData = {};
            clearInterval(_graphRefreshTimer);
            setTimeout(function () {
                if (_activeTab === 'graph' && sapIsOverlayVisible()) {
                    wireChartHover();
                    sapSafeCall('load graph history', loadGraphHistory);
                    _graphRefreshTimer = setInterval(function () {
                        if (_activeTab === 'graph' && sapIsOverlayVisible()) sapSafeCall('append live graph', appendLiveToGraph);
                    }, 3000);
                }
            }, 120);
            return true;
        }

        if (name === 'eventlog') {
            setTimeout(function () {
                if (_activeTab === 'eventlog' && sapIsOverlayVisible()) sapSafeCall('load event log', loadEventLog);
            }, 30);
            return true;
        }

        if (name === 'alarm') {
            setTimeout(function () {
                if (_activeTab === 'alarm' && sapIsOverlayVisible()) sapSafeCall('load active alarms', loadActiveAlarms);
            }, 30);
            return true;
        }

        return true;
    }

    /* ═══════════════════════════════════════════════════════════════
       LIVE TELEMETRY — read from wsLiveData (WebSocket)
       ═══════════════════════════════════════════════════════════════ */
    function findLiveAsset() {
        var ld = window.wsLiveData || {};
        var aid = String(_assetId || '').trim();
        var targetName = normAssetName(_assetName);
        var targetLoose = looseAssetName(_assetName);

        function normAssetName(v) {
            return String(v == null ? '' : v).trim().toUpperCase();
        }

        /* Loose key: strip everything except A-Z0-9 so SIP labels like
           "S 5" / "S-5" still resolve the DB asset named "S5".          */
        function looseAssetName(v) {
            return normAssetName(v).replace(/[^A-Z0-9]+/g, '');
        }

        function wrapSipState(id, value) {
            if (!value) return null;
            if (value.attrs || value.AssetName || value.assetName) {
                return { id: id || '', d: value, via: 'sip-state' };
            }
            return {
                id: id || '',
                d: {
                    attrs: value,
                    AssetName: _assetName,
                    SiteId: _siteId
                },
                via: 'sip-state'
            };
        }

        // 1. Exact AssetId from SIP metadata / sourceInfo.
        if (aid && ld[aid]) return { id: aid, d: ld[aid], via: 'wsLiveData:assetId' };

        // 2. AssetName match from wsLiveData — exact first, then loose.
        if (targetName) {
            var looseHit = null;
            for (var id in ld) {
                if (!ld.hasOwnProperty(id)) continue;
                var row = ld[id] || {};
                var nm = normAssetName(row.AssetName || row.assetName || row.Name || row.name);
                if (!nm) continue;
                if (nm === targetName) return { id: id, d: row, via: 'wsLiveData:exact-name' };
                if (!looseHit && targetLoose && looseAssetName(nm) === targetLoose) {
                    looseHit = { id: id, d: row, via: 'wsLiveData:loose-name' };
                }
            }
            if (looseHit) return looseHit;
        }

        // 3. SIP bridge state by AssetId or AssetName.
        var svMap = window.SipTelemetry &&
            window.SipTelemetry._state &&
            window.SipTelemetry._state.assetValues
            ? window.SipTelemetry._state.assetValues
            : null;

        if (svMap) {
            if (aid && svMap[aid]) {
                var byId = wrapSipState(aid, svMap[aid]);
                if (byId) return byId;
            }

            if (_assetName && svMap[_assetName]) {
                var byName = wrapSipState('', svMap[_assetName]);
                if (byName) return byName;
            }

            if (targetName) {
                var svLooseHit = null;
                for (var sid in svMap) {
                    if (!svMap.hasOwnProperty(sid)) continue;
                    var sv = svMap[sid];
                    var svName = normAssetName(
                        (sv && (sv.AssetName || sv.assetName || sv.Name || sv.name)) || sid
                    );
                    if (!svName) continue;
                    if (svName === targetName) {
                        var byScan = wrapSipState(sid, sv);
                        if (byScan) return byScan;
                    }
                    if (!svLooseHit && targetLoose && looseAssetName(svName) === targetLoose) {
                        svLooseHit = wrapSipState(sid, sv);
                    }
                }
                if (svLooseHit) return svLooseHit;
            }
        }

        // 4. Metadata fallback: lets popup open with configured rows as '-' even before WS arrives.
        if (aid && window.bulkAssetMap && window.bulkAssetMap[aid]) {
            var meta = window.bulkAssetMap[aid] || {};
            return {
                id: aid,
                d: {
                    attrs: {},
                    AssetName: meta.Name || meta.AssetName || _assetName,
                    AssetTypeId: meta.AssetTypeId || _assetType,
                    SiteId: meta.SiteId || _siteId
                },
                via: 'bulkAssetMap:metadata-only'
            };
        }

        return null;
    }

    /* Resolve an asset id from bulkAssetMap by (loose) name. Used so the
       popup never falls back to "show everything" when the SIP cell did
       not carry a registry assetId.                                      */
    function resolveAssetIdByName(name) {
        if (!name || !window.bulkAssetMap) return '';
        var want = String(name).trim().toUpperCase().replace(/[^A-Z0-9]+/g, '');
        if (!want) return '';
        for (var id in window.bulkAssetMap) {
            if (!window.bulkAssetMap.hasOwnProperty(id)) continue;
            var m = window.bulkAssetMap[id] || {};
            var nm = String(m.Name || m.AssetName || '').trim().toUpperCase().replace(/[^A-Z0-9]+/g, '');
            if (nm && nm === want) return String(id);
        }
        return '';
    }

    //function renderTelemRow(item) {
    //    debugger;
    //    var label = item && item.label ? item.label : '';
    //    var rawKey = item && item.rawKey ? item.rawKey : label;
    //    var raw = item ? item.value : null;

    //    var display = (raw !== null && raw !== undefined && raw !== '') ? String(raw) : '--';

    //    if (display === 'Ok' || display === 'ok' || display === 'true') {
    //        return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) + '</span><span class="sap-value-ok">Ok</span></div>';
    //    }

    //    var num = parseFloat(display);
    //    if (!isNaN(num)) display = num % 1 === 0 ? String(num) : num.toFixed(2);

    //    var cls = 'sap-telem-value';
    //    var stale = (window.wsStaleAttrs && window.wsStaleAttrs[_assetId] &&
    //        (window.wsStaleAttrs[_assetId][rawKey] || window.wsStaleAttrs[_assetId][label]));

    //    if (stale) cls += ' sap-value-stale';

    //    return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) + '</span><span class="' + cls + '">' + esc(display) + '</span></div>';
    //}

    //function renderTelemRow(item) {

    //    var label = item && item.label ? item.label : '';
    //    var rawKey = item && item.rawKey ? item.rawKey : label;
    //    var raw = item ? item.value : null;
    //    var attrId = item && item.attrId ? String(item.attrId).trim() : '';

    //    var display = (raw !== null && raw !== undefined && raw !== '') ? String(raw).trim() : '--';

    //    var stale = (window.wsStaleAttrs && window.wsStaleAttrs[_assetId] &&
    //        (window.wsStaleAttrs[_assetId][rawKey] || window.wsStaleAttrs[_assetId][label]));

    //    var cls = 'sap-telem-value';
    //    if (stale) cls += ' sap-value-stale';

    //    // Check only attributes bound from DataLogger map
    //    var isDataLoggerAttr = false;
    //    var dlMap = window.userAssetDataloggerMap || {};
    //    var aid = String(_assetId || '');
    //    var prefix = aid + '_';

    //    var labelNorm = String(label || '').trim().toUpperCase();
    //    var rawKeyNorm = String(rawKey || '').trim().toUpperCase();
    //    var attrIdNorm = String(attrId || '').trim().toUpperCase();

    //    for (var dk in dlMap) {
    //        if (!dlMap.hasOwnProperty(dk)) continue;
    //        if (aid && dk.indexOf(prefix) !== 0) continue;

    //        var dlEntry = dlMap[dk] || {};
    //        var dlRoleId = String(dk).substring(String(dk).lastIndexOf('_') + 1).trim().toUpperCase();

    //        var dlName = String(dlEntry.name || dlEntry.Name || '').trim().toUpperCase();
    //        var dlAttrName = String(dlEntry.attributeName || dlEntry.AttributeName || '').trim().toUpperCase();
    //        var dlAlias = String(dlEntry.aliasName || dlEntry.AliasName || '').trim().toUpperCase();

    //        if (
    //            (attrIdNorm && attrIdNorm === dlRoleId) ||
    //            (rawKeyNorm && (rawKeyNorm === dlRoleId || rawKeyNorm === dlName || rawKeyNorm === dlAttrName || rawKeyNorm === dlAlias)) ||
    //            (labelNorm && (labelNorm === dlRoleId || labelNorm === dlName || labelNorm === dlAttrName || labelNorm === dlAlias))
    //        ) {
    //            isDataLoggerAttr = true;
    //            break;
    //        }
    //    }

    //    // Apply Drop/Pickup only for DataLogger-bound binary attributes
    //    if (isDataLoggerAttr && display === '0') {
    //        return '<div class="sap-telem-row">' +
    //            '<span class="sap-telem-label">' + esc(label) + '</span>' +
    //            '<span class="' + cls + '" style="color:var(--sap-accent-yellow);font-weight:700;">↓ Drop</span>' +
    //            '</div>';
    //    }

    //    if (isDataLoggerAttr && display === '1') {
    //        return '<div class="sap-telem-row">' +
    //            '<span class="sap-telem-label">' + esc(label) + '</span>' +
    //            '<span class="' + cls + '" style="color:var(--sap-accent-green);font-weight:700;">↑ Pickup</span>' +
    //            '</div>';
    //    }

    //    if (display === 'Ok' || display === 'ok' || display === 'true') {
    //        return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) + '</span><span class="sap-value-ok">Ok</span></div>';
    //    }

    //    var num = parseFloat(display);
    //    if (!isNaN(num)) display = num % 1 === 0 ? String(num) : num.toFixed(2);

    //    return '<div class="sap-telem-row">' +
    //        '<span class="sap-telem-label">' + esc(label) + '</span>' +
    //        '<span class="' + cls + '">' + esc(display) + '</span>' +
    //        '</div>';
    //}
    function renderTelemRow(item) {
        var label = item && item.label ? item.label : '';
        var rawKey = item && item.rawKey ? item.rawKey : label;
        var raw = item ? item.value : null;
        var isDL = !!(item && item.isDatalogger);

        var display = (raw !== null && raw !== undefined && raw !== '') ? String(raw).trim() : '--';

        var cls = 'sap-telem-value';
        var stale = (window.wsStaleAttrs && window.wsStaleAttrs[_assetId] &&
            (window.wsStaleAttrs[_assetId][rawKey] || window.wsStaleAttrs[_assetId][label]));

        if (stale) cls += ' sap-value-stale';

        var n = parseFloat(display);
        var isBinary = !isNaN(n) && (n === 0 || n === 1);

        if (isDL && isBinary && n === 0) {
            return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) +
                '</span><span class="' + cls + '" style="color:var(--sap-accent-yellow);font-weight:700;">&#8595; Drop</span></div>';
        }

        if (isDL && isBinary && n === 1) {
            return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) +
                '</span><span class="' + cls + '" style="color:var(--sap-accent-green);font-weight:700;">&#8593; Pickup</span></div>';
        }

        if (display === 'Ok' || display === 'ok' || display === 'true') {
            return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) +
                '</span><span class="sap-value-ok">Ok</span></div>';
        }

        if (!isNaN(n)) display = n % 1 === 0 ? String(n) : n.toFixed(2);

        return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) +
            '</span><span class="' + cls + '">' + esc(display) + '</span></div>';
    }
    function refreshLive() {
        var f = findLiveAsset();
        var attrs = {};
        var ra = null;
        var liveAsset = null;

        if (f && f.d) {
            _assetId = f.id || _assetId;
            liveAsset = f.d;
            ra = f.d.attrs || f.d;

            if (!_assetName && (f.d.AssetName || f.d.assetName || f.d.Name)) {
                _assetName = f.d.AssetName || f.d.assetName || f.d.Name;
            }
            if (!_siteId && (f.d.SiteId || f.d.siteId)) {
                _siteId = f.d.SiteId || f.d.siteId;
            }
        }

        var aid = String(_assetId || '').trim();

        /* The cell may carry only a label, no registry assetId. Resolve the
           id from bulkAssetMap by name so the configured-attribute loops
           below stay scoped to THIS asset only.                            */
        if (!aid && _assetName) {
            aid = resolveAssetIdByName(_assetName);
            if (aid) {
                _assetId = aid;
                sapLog('Resolved assetId ' + aid + ' for "' + _assetName + '" via bulkAssetMap name match.');
            }
        }

        /* CONCEPT: the popup shows ONLY the clicked asset.
           Previously, when aid was empty the simpleMap / dlMap loops lost
           their prefix filter and dumped EVERY asset's configured attributes
           into the grid (e.g. a signal popup also showing track attrs).
           Now: no resolved assetId ⇒ configured loops are skipped entirely
           and only the asset's own live snapshot rows are shown.           */
        if (!aid) {
            sapWarnOnce('noaid:' + String(_assetName || ''),
                'Asset "' + _assetName + '" has no resolvable assetId — showing only live ' +
                'snapshot values. Bind the SIP cell to a registry asset to see configured ' +
                'attribute and DataLogger (TPR) placeholders.');
        }

        var prefix = aid + '_';

        /* Self-heal: if alias metadata still isn't loaded (user never
           searched), retry now — the load callback re-renders this grid. */
        sapEnsureAliasMetadata();

        var dlMap = window.userAssetDataloggerMap || {};
        var simpleMap = window.userAssetSimpleMap || {};
        var dlMeta = {};
        var dlNameIndex = {};
        var dlLookup = {};

        function norm(v) {
            return String(v == null ? '' : v).trim().toUpperCase();
        }

        function cleanId(v) {
            var s = String(v == null ? '' : v).trim();
            if (!s || s === '0' || /^null$/i.test(s) || /^undefined$/i.test(s)) return '';
            return s.replace(/^dl_/i, '');
        }

        function addDlLookup(v, dlId) {
            var n = norm(v);
            if (n) dlLookup[n] = String(dlId);
        }

        function addMetaCandidate(meta, v) {
            var id = cleanId(v);
            if (!id) return;
            meta.candidates[id] = true;
            addDlLookup(id, meta.id);
            addDlLookup('DL_' + id, meta.id);
            addDlLookup('dl_' + id, meta.id);
        }

        function getValue(x) {
            if (x && typeof x === 'object') {
                if (x.Value !== undefined) return x.Value;
                if (x.value !== undefined) return x.value;
                if (x.CurrentValue !== undefined) return x.CurrentValue;
                if (x.currentValue !== undefined) return x.currentValue;
                if (x.Status !== undefined) return x.Status;
                if (x.status !== undefined) return x.status;
                if (x.isPickup !== undefined) return x.isPickup ? 1 : 0;
                if (x.IsPickup !== undefined) return x.IsPickup ? 1 : 0;
            }

            return x;
        }

        function getEntryField(entry) {
            for (var i = 1; i < arguments.length; i++) {
                var key = arguments[i];
                if (entry && entry[key] !== undefined && entry[key] !== null && String(entry[key]).trim() !== '') {
                    return entry[key];
                }
            }
            return '';
        }

        // Build canonical DL metadata once per real DataloggerAttribute.
        // userAssetDataloggerMap may contain many lookup keys for the same relay
        // (DataloggerAttributeId, Value id, DB Id, name, DataloggerAssetName).
        // This block collapses those aliases into one visible row.
        for (var dk in dlMap) {
            if (!dlMap.hasOwnProperty(dk)) continue;
            /* Strict scoping: only THIS asset's DataLogger rows. When aid is
               unknown we must not fall through to all assets.              */
            if (!aid || dk.indexOf(prefix) !== 0) continue;

            var dlEntry = dlMap[dk] || {};
            var keySuffix = cleanId(dk.substring(prefix.length));

            var canonicalId = cleanId(getEntryField(
                dlEntry,
                'DataloggerAttributeId',
                'dataloggerAttributeId',
                'DlAttributeId',
                'dlAttributeId'
            ));

            var dlName = getEntryField(
                dlEntry,
                'DataloggerAttribute',
                'dataloggerAttribute',
                'attributeName',
                'AttributeName',
                'name',
                'Name',
                'aliasName',
                'AliasName'
            );

            var dlAssetName = getEntryField(
                dlEntry,
                'DataloggerAssetName',
                'dataloggerAssetName',
                'assetName',
                'AssetName'
            );

            var nameKey = norm(dlName) + '|' + norm(dlAssetName);

            if (!canonicalId && nameKey !== '|') canonicalId = dlNameIndex[nameKey] || '';
            if (!canonicalId) canonicalId = keySuffix || norm(dlName).replace(/\s+/g, '_');
            if (!canonicalId) continue;

            if (nameKey !== '|' && dlNameIndex[nameKey] && dlNameIndex[nameKey] !== canonicalId) {
                canonicalId = dlNameIndex[nameKey];
            }

            if (!dlMeta[canonicalId]) {
                dlMeta[canonicalId] = {
                    id: canonicalId,
                    name: dlName || ('DL ' + canonicalId),
                    assetName: dlAssetName || '',
                    candidates: {}
                };
            }

            if (nameKey !== '|') dlNameIndex[nameKey] = canonicalId;

            var meta = dlMeta[canonicalId];

            if (!meta.name && dlName) meta.name = dlName;
            if (!meta.assetName && dlAssetName) meta.assetName = dlAssetName;

            addMetaCandidate(meta, canonicalId);
            addMetaCandidate(meta, keySuffix);

            [
                'DataloggerAttributeId', 'dataloggerAttributeId',
                'DlAttributeId', 'dlAttributeId',
                'dataloggerValueId', 'DataloggerValueId',
                'Value', 'value', 'Id', 'id', 'Role', 'role',
                'AssetAttributeId', 'assetAttributeId',
                'AttributeId', 'attributeId',
                'SrNo', 'srNo'
            ].forEach(function (field) {
                addMetaCandidate(meta, dlEntry[field]);
            });

            addDlLookup(meta.name, meta.id);
            addDlLookup(meta.assetName, meta.id);
            addDlLookup(dlEntry.DataloggerAttribute, meta.id);
            addDlLookup(dlEntry.dataloggerAttribute, meta.id);
            addDlLookup(dlEntry.DataloggerAssetName, meta.id);
            addDlLookup(dlEntry.dataloggerAssetName, meta.id);
        }

        function getDlIdFromLiveKeyOrObject(key, obj) {
            var keyNorm = norm(key);
            if (dlLookup[keyNorm]) return dlLookup[keyNorm];

            if (obj && typeof obj === 'object') {
                var directIds = [
                    obj.DataloggerAttributeId,
                    obj.dataloggerAttributeId,
                    obj.DlAttributeId,
                    obj.dlAttributeId,
                    obj.dataloggerValueId,
                    obj.DataloggerValueId,
                    obj.Role,
                    obj.role,
                    obj.SrNo,
                    obj.srNo,
                    obj.Id,
                    obj.id
                ];

                for (var di = 0; di < directIds.length; di++) {
                    var directNorm = norm(directIds[di]);
                    if (directNorm && dlLookup[directNorm]) return dlLookup[directNorm];
                }

                var names = [
                    obj.DataloggerAttribute,
                    obj.dataloggerAttribute,
                    obj.DataloggerAssetName,
                    obj.dataloggerAssetName,
                    obj.name,
                    obj.Name,
                    obj.attributeName,
                    obj.AttributeName
                ];

                for (var i = 0; i < names.length; i++) {
                    var n = norm(names[i]);
                    if (n && dlLookup[n]) return dlLookup[n];
                }

                // Only use generic AttributeId/AttrId collision-prone fields when the
                // message explicitly says DataLogger. This prevents analog AssetAttributeId=6
                // from being mistaken as TPR DataLogger role 6.
                var marker = String(
                    obj.DataType ||
                    obj.dataType ||
                    obj.DataSource ||
                    obj.dataSource ||
                    obj.Source ||
                    obj.source ||
                    ''
                );

                if (/DATALOGGER|\bDL\b/i.test(marker)) {
                    var markerIds = [obj.AttrId, obj.AssetAttributeId, obj.AttributeId, obj.attrId, obj.assetAttributeId, obj.attributeId];

                    for (var mi = 0; mi < markerIds.length; mi++) {
                        var markerNorm = norm(markerIds[mi]);
                        if (markerNorm && dlLookup[markerNorm]) return dlLookup[markerNorm];
                    }

                    if (dlLookup[keyNorm]) return dlLookup[keyNorm];
                }
            }

            return '';
        }

        function findDlValue(meta) {
            var candidates = ['DL_' + meta.id, 'dl_' + meta.id, meta.id, meta.name, meta.assetName];

            for (var cid in meta.candidates) {
                if (meta.candidates.hasOwnProperty(cid)) {
                    candidates.push(cid);
                    candidates.push('DL_' + cid);
                    candidates.push('dl_' + cid);
                }
            }

            /* ── DataLogger relay sources, in priority order ──────────────
               FIX: in wsLiveData the dlRelays object lives on the asset
               ENTRY itself (sibling of .attrs), but `ra` here is the .attrs
               object — so the old `ra.dlRelays` lookup never found anything
               and TPR rows showed '--' even with live relay data.

               1. liveAsset.dlRelays   — the resolved wsLiveData entry
               2. ra.dlRelays          — kept for flat/bridged snapshots
               3. dlRelays + attrs of the wsLiveData entry whose AssetName
                  matches the configured DataloggerAssetName (relays are
                  often logged under a separate datalogger asset)
               4. ra                   — flat SIP bridge snapshot values    */
            var sources = [];
            if (liveAsset && (liveAsset.dlRelays || liveAsset.DlRelays || liveAsset.DLRelays)) {
                sources.push(liveAsset.dlRelays || liveAsset.DlRelays || liveAsset.DLRelays);
            }
            if (ra && (ra.dlRelays || ra.DlRelays || ra.DLRelays)) {
                sources.push(ra.dlRelays || ra.DlRelays || ra.DLRelays);
            }
            if (meta.assetName && window.wsLiveData) {
                var wantDlAsset = norm(meta.assetName).replace(/[^A-Z0-9]+/g, '');
                if (wantDlAsset) {
                    for (var wid in window.wsLiveData) {
                        if (!window.wsLiveData.hasOwnProperty(wid)) continue;
                        var wEntry = window.wsLiveData[wid] || {};
                        var wName = norm(wEntry.AssetName || wEntry.assetName || '').replace(/[^A-Z0-9]+/g, '');
                        if (!wName || wName !== wantDlAsset) continue;
                        if (wEntry.dlRelays) sources.push(wEntry.dlRelays);
                        if (wEntry.attrs) sources.push(wEntry.attrs);
                        break;
                    }
                }
            }
            sources.push(ra);

            function matchesCandidate(v) {
                var vn = norm(v);
                if (!vn) return false;

                for (var i = 0; i < candidates.length; i++) {
                    if (vn === norm(candidates[i])) return true;
                }

                return false;
            }

            function scanSource(src) {
                if (!src || typeof src !== 'object') return { found: false };

                if (Array.isArray(src)) {
                    for (var a = 0; a < src.length; a++) {
                        var ar = scanSource(src[a]);
                        if (ar.found) return ar;
                    }
                    return { found: false };
                }

                for (var i = 0; i < candidates.length; i++) {
                    var c = candidates[i];
                    if (c && src.hasOwnProperty(c)) {
                        return { found: true, value: getValue(src[c]) };
                    }
                }

                for (var k in src) {
                    if (!src.hasOwnProperty(k)) continue;

                    var child = src[k];

                    if (matchesCandidate(k)) {
                        return { found: true, value: getValue(child) };
                    }

                    if (child && typeof child === 'object') {
                        if (
                            matchesCandidate(child.DataloggerAttributeId) ||
                            matchesCandidate(child.dataloggerAttributeId) ||
                            matchesCandidate(child.DlAttributeId) ||
                            matchesCandidate(child.dlAttributeId) ||
                            matchesCandidate(child.dataloggerValueId) ||
                            matchesCandidate(child.DataloggerValueId) ||
                            matchesCandidate(child.Role) ||
                            matchesCandidate(child.role) ||
                            matchesCandidate(child.Id) ||
                            matchesCandidate(child.id) ||
                            matchesCandidate(child.DataloggerAttribute) ||
                            matchesCandidate(child.dataloggerAttribute) ||
                            matchesCandidate(child.DataloggerAssetName) ||
                            matchesCandidate(child.dataloggerAssetName) ||
                            matchesCandidate(child.name) ||
                            matchesCandidate(child.Name) ||
                            matchesCandidate(child.attributeName) ||
                            matchesCandidate(child.AttributeName)
                        ) {
                            return { found: true, value: getValue(child) };
                        }

                        var marker = String(child.DataType || child.dataType || child.Source || child.source || '');
                        if (/DATALOGGER|\bDL\b/i.test(marker) &&
                            (matchesCandidate(child.AttrId) ||
                                matchesCandidate(child.AssetAttributeId) ||
                                matchesCandidate(child.AttributeId))) {
                            return { found: true, value: getValue(child) };
                        }
                    }
                }

                return { found: false };
            }

            for (var s = 0; s < sources.length; s++) {
                var res = scanSource(sources[s]);
                if (res.found) return res.value;
            }

            sapWarnOnce('dlmiss:' + aid + ':' + meta.id,
                'DataLogger "' + meta.name + '" (id ' + meta.id + ') of asset "' + _assetName +
                '": no live value found in dlRelays/attrs of any source — showing "--". ' +
                'Configured DataloggerAssetName: "' + (meta.assetName || '—') + '".');
            return '--';
        }

        function hasExistingAttr(attrId, label) {
            var id = String(attrId || '').trim();
            var lbl = norm(label);

            for (var k in attrs) {
                if (!attrs.hasOwnProperty(k)) continue;
                var row = attrs[k] || {};
                if (id && !row.isDatalogger && String(row.attrId || '').trim() === id) return true;
                if (lbl && !row.isDatalogger && norm(row.label) === lbl) return true;
            }

            return false;
        }

        function findAssetAttrValue(meta) {
            if (!ra || typeof ra !== 'object') return { found: false, value: '--', rawKey: meta.title || meta.alias || '' };

            var candidates = [
                meta.title,
                meta.alias,
                meta.attributeName,
                meta.name,
                meta.id
            ];

            for (var i = 0; i < candidates.length; i++) {
                var c = candidates[i];
                if (c && ra.hasOwnProperty(c)) {
                    return { found: true, value: getValue(ra[c]), rawKey: c };
                }
            }

            var id = String(meta.id || '').trim();

            if (id) {
                for (var k in ra) {
                    if (!ra.hasOwnProperty(k)) continue;
                    var obj = ra[k];
                    if (String(sapAttrIdOf(obj) || '').trim() === id) {
                        return { found: true, value: getValue(obj), rawKey: k };
                    }
                }
            }

            return { found: false, value: '--', rawKey: meta.title || meta.alias || '' };
        }

        // True when `name` matches a configured AliasName for THIS asset
        // (or a dictionary alias). Used to allow rows whose WS key already
        // IS the alias (e.g. server sends "TPR" and alias is "TPR").
        // True when `name` matches a configured AliasName for THIS asset
        // (or a dictionary alias). Used to allow rows whose WS key already
        // IS the alias (e.g. server sends "TPR" and alias is "TPR").
        function isConfiguredAliasName(name) {
            var n = norm(name);
            if (!n) return false;
            if (window.assetAttributeByName && window.assetAttributeByName[name]) return true;
            for (var ck in simpleMap) {
                if (!simpleMap.hasOwnProperty(ck)) continue;
                if (aid && ck.indexOf(prefix) !== 0) continue;
                var ce = simpleMap[ck] || {};
                if (norm(ce.AliasName || ce.name || ce.Name) === n) return true;
            }
            return false;
        }

        /* Does the loaded metadata actually cover THIS asset? Only then is
           it safe to hide unresolved raw rows — their values re-surface on
           the alias-labelled placeholder rows built below. If metadata does
           NOT cover the asset yet, we must still show the live data. */
        var hasMetaForAsset = false;
        if (aid) {
            for (var mk in simpleMap) {
                if (simpleMap.hasOwnProperty(mk) && mk.indexOf(prefix) === 0) { hasMetaForAsset = true; break; }
            }
            if (!hasMetaForAsset) {
                for (var mk2 in dlMap) {
                    if (dlMap.hasOwnProperty(mk2) && mk2.indexOf(prefix) === 0) { hasMetaForAsset = true; break; }
                }
            }
        }

        // Add normal live attributes, but skip DataLogger live keys.
        // AliasName is preferred. Raw WebSocket keys are hidden ONLY when
        // alias-labelled placeholder rows will carry the same values.
        // The popup must NEVER be empty while live data exists.
        if (ra && typeof ra === 'object') {
            for (var k in ra) {
                if (!ra.hasOwnProperty(k)) continue;
                if (/^(AssetName|AssetTypeId|SiteId|lastUpdated|dlRelays|__type)$/i.test(k)) continue;

                var obj = ra[k];

                var dlIdFromLive = getDlIdFromLiveKeyOrObject(k, obj);
                if (dlIdFromLive) continue;

                var aliasLabel = sapAliasName(k, obj);
                var resolved = (aliasLabel && norm(aliasLabel) !== norm(k)) || isConfiguredAliasName(k);

                if (!resolved) {
                    // Numeric key = DataLogger role/attribute id from the WS.
                    // Try the DL name chain once more; if it still can't be
                    // named, hide it — a bare number is meaningless to the
                    // user, and the DL placeholder loop will render it with
                    // its proper name once the datalogger map loads.
                    if (/^\d+$/.test(String(k).trim())) {
                        var dlName2 = '';
                        try {
                            if (typeof window.resolveBulkDataloggerName === 'function')
                                dlName2 = window.resolveBulkDataloggerName(aid, k, k);
                        } catch (e) { }
                        if (dlName2 && !/^\d+$/.test(String(dlName2).trim())) {
                            aliasLabel = dlName2;
                            attrs[k] = {
                                rawKey: k, label: aliasLabel, value: getValue(obj),
                                attrId: sapAttrIdOf(obj), isDatalogger: true
                            };
                        } else {
                            sapWarnOnce('dlnum:' + k,
                                'DataLogger role ' + k + ' has no name in userAssetDataloggerMap/dlRoleNameMap — hiding row until metadata loads.');
                        }
                        continue;
                    }
                    if (hasMetaForAsset) {
                        // Metadata covers this asset — the placeholder loop
                        // below shows this value under its real AliasName.
                        continue;
                    }
                    // No metadata for this asset yet: show the value anyway
                    // with a cleaned label (asset prefix / encoded IDs
                    // stripped). The next refresh after metadata arrives
                    // replaces these with proper AliasName rows.
                    aliasLabel = cleanAliasName(k);
                    sapWarnOnce('rawattr:' + k,
                        'Alias metadata not loaded yet — showing "' + k +
                        '" as "' + aliasLabel + '" until GetBulkAssetMetadata arrives.');
                }

                attrs[k] = {
                    rawKey: k,
                    label: aliasLabel,
                    value: getValue(obj),
                    attrId: sapAttrIdOf(obj),
                    isDatalogger: false
                };
            }
        }

        // Add configured asset attributes as placeholders when WebSocket has not sent them.
        for (var sk in simpleMap) {
            if (!simpleMap.hasOwnProperty(sk)) continue;
            /* Strict scoping: only THIS asset's configured attributes. When
               aid is unknown we must not fall through to all assets (this
               was what made a signal popup also list track attributes).    */
            if (!aid || sk.indexOf(prefix) !== 0) continue;

            var sEntry = simpleMap[sk] || {};
            var sAttrId = String(
                sEntry.Id ||
                sEntry.id ||
                sEntry.AttrId ||
                sEntry.attrId ||
                sk.substring(prefix.length)
            ).trim();

            var sAlias = sEntry.AliasName || sEntry.aliasName || sEntry.name || sEntry.Name || ('Attr ' + sAttrId);
            var sTitle = sEntry.Title || sEntry.title || sEntry.attributeName || sEntry.AttributeName || sAlias;

            if (!sAttrId && !sAlias) continue;
            if (hasExistingAttr(sAttrId, sAlias)) continue;

            var found = findAssetAttrValue({
                id: sAttrId,
                title: sTitle,
                alias: sAlias,
                attributeName: sEntry.attributeName || sEntry.AttributeName || '',
                name: sEntry.name || sEntry.Name || ''
            });

            attrs['AA_' + (sAttrId || norm(sAlias))] = {
                rawKey: found.rawKey || sTitle,
                label: sAlias,
                value: found.value,
                attrId: sAttrId,
                isDatalogger: false
            };
        }

        // Add DataLogger rows once only.
        for (var id in dlMeta) {
            if (!dlMeta.hasOwnProperty(id)) continue;

            attrs['DL_' + id] = {
                rawKey: 'DL_' + id,
                label: dlMeta[id].name,
                value: findDlValue(dlMeta[id]),
                attrId: id,
                isDatalogger: true
            };
        }

        var keys = Object.keys(attrs);

        // Final visible-row dedupe. Prefer a row with a real value over a placeholder.
        var chosenByKey = {};
        var dedupedKeys = [];

        function hasRealValue(row) {
            var v = row ? row.value : null;
            return !(v === null || v === undefined || v === '' || v === '--' || v === '-');
        }

        function rowKey(row) {
            if (!row) return '';
            if (row.isDatalogger) return 'D:' + norm(row.label);
            var id = String(row.attrId || '').trim();
            if (id) return 'A:' + id;
            return 'L:' + norm(row.label || row.rawKey);
        }

        keys.forEach(function (k) {
            var row = attrs[k];
            var rk = rowKey(row);
            if (!rk) return;

            if (!chosenByKey[rk]) {
                chosenByKey[rk] = k;
                dedupedKeys.push(k);
                return;
            }

            var oldKey = chosenByKey[rk];
            var oldRow = attrs[oldKey];

            if (!hasRealValue(oldRow) && hasRealValue(row)) {
                chosenByKey[rk] = k;
                var idx = dedupedKeys.indexOf(oldKey);
                if (idx >= 0) dedupedKeys[idx] = k;
            }
        });

        keys = dedupedKeys;

        if (window.wsAttributeNames && window.wsAttributeNames.length) {
            var ord = [];

            window.wsAttributeNames.forEach(function (n) {
                if (keys.indexOf(n) >= 0) ord.push(n);
            });

            keys.forEach(function (k) {
                if (ord.indexOf(k) < 0) ord.push(k);
            });

            keys = ord;
        } else {
            keys.sort(function (a, b) {
                var ar = attrs[a] || {};
                var br = attrs[b] || {};

                if (!!ar.isDatalogger !== !!br.isDatalogger) {
                    return ar.isDatalogger ? 1 : -1;
                }

                var ai = parseInt(ar.attrId, 10);
                var bi = parseInt(br.attrId, 10);

                if (!isNaN(ai) && !isNaN(bi) && ai !== bi) return ai - bi;
                return String(ar.label || a).localeCompare(String(br.label || b), undefined, {
                    numeric: true,
                    sensitivity: 'base'
                });
            });
        }

        var grid = el('sipAssetTelemetryGrid');
        if (!grid) return;

        if (!keys.length) {
            var emptyHtml = '<div class="sap-empty-state">Waiting for WebSocket data\u2026</div>';
            if (grid.innerHTML !== emptyHtml) grid.innerHTML = emptyHtml;
            return;
        }

        var signature = keys.map(function (k) {
            var r = attrs[k] || {};
            return [
                r.isDatalogger ? 'D' : 'A',
                r.attrId || '',
                r.label || '',
                r.value === undefined || r.value === null ? '' : String(r.value)
            ].join(':');
        }).join('|');

        if (_lastLiveSignature === signature) {
            populateGraphDropdown();

            var tsSame = el('sipAssetLastSync');
            if (tsSame) tsSame.textContent = 'Last sync: ' + fmtNow();

            return;
        }

        _lastLiveSignature = signature;

        /* Summary — logged only when the visible data actually changed */
        (function () {
            var nDl = 0, nLive = 0, nPlaceholder = 0;
            for (var ki = 0; ki < keys.length; ki++) {
                var rw = attrs[keys[ki]] || {};
                if (rw.isDatalogger) nDl++;
                else if (hasRealValue(rw)) nLive++;
                else nPlaceholder++;
            }
            sapLog('Live grid updated for "' + _assetName + '" (assetId ' + (aid || '—') + '): ' +
                nLive + ' live attr(s), ' + nPlaceholder + ' placeholder(s), ' + nDl + ' DataLogger row(s).');
        })();

        var half = Math.ceil(keys.length / 2);

        grid.innerHTML =
            '<div class="sap-telemetry-col">' +
            keys.slice(0, half).map(function (k) { return renderTelemRow(attrs[k]); }).join('') +
            '</div>' +
            '<div class="sap-telemetry-col">' +
            keys.slice(half).map(function (k) { return renderTelemRow(attrs[k]); }).join('') +
            '</div>';

        populateGraphDropdown();

        var ts = el('sipAssetLastSync');
        if (ts) ts.textContent = 'Last sync: ' + fmtNow();
    }
    /* ═══════════════════════════════════════════════════════════════
       GRAPH DROPDOWN — populated from GetBulkAssetMetadata cache,
       NOT limited to attributes currently arriving from WebSocket.

       Sources (already cached by telemetrylive.js loadBulkAssetMetadata):
         • window.userAssetSimpleMap  [assetId_attrId] → { name (AliasName) }
         • window.userAssetDataloggerMap [assetId_dlRoleId] → { name }
         • Fallback: wsLiveData keys (if metadata not loaded yet)

       Each <option> carries:
         value          = attribute Id  (e.g. "3")  or "dl_6" for DataLogger
         data-title     = Title / wsLiveData key  ("Vr", "TPR")
         text (visible) = AliasName  ("VTC RELAY END(V)", "TPR")
       ═══════════════════════════════════════════════════════════════ */
    //function populateGraphDropdown() {
    //    var sel = el('sipAssetParamSelect');
    //    if (!sel) return;
    //    var oldValue = sel.value;
    //    var aid = String(_assetId || '');

    //    var opts = [];   // { id, title, alias, seq }

    //    /* ── 1. assetAttributes from userAssetSimpleMap ── */
    //    var uMap = window.userAssetSimpleMap || {};
    //    var prefix = aid + '_';
    //    for (var key in uMap) {
    //        if (!uMap.hasOwnProperty(key)) continue;
    //        if (key.indexOf(prefix) !== 0) continue;
    //        var entry = uMap[key];
    //        var attrId = key.substring(prefix.length);
    //        opts.push({
    //            id: attrId,
    //            title: entry.attributeName || entry.AttributeName || entry.name || '',
    //            alias: entry.name || entry.AliasName || ('Attr ' + attrId),
    //            seq: 0
    //        });
    //    }

    //    /* ── 2. mAssetInfoDataloggers from userAssetDataloggerMap ── */
    //    var dlMap = window.userAssetDataloggerMap || {};
    //    for (var dlKey in dlMap) {
    //        if (!dlMap.hasOwnProperty(dlKey)) continue;
    //        if (dlKey.indexOf(prefix) !== 0) continue;
    //        var dlEntry = dlMap[dlKey];
    //        var dlRoleId = dlKey.substring(prefix.length);
    //        opts.push({
    //            id: 'dl_' + dlRoleId,
    //            title: dlEntry.attributeName || dlEntry.name || '',
    //            alias: dlEntry.name || dlEntry.attributeName || ('DL ' + dlRoleId),
    //            seq: 9000 + parseInt(dlRoleId) || 9999
    //        });
    //    }

    //    /* ── 3. Fallback: wsLiveData keys if metadata not loaded yet ── */
    //    if (opts.length === 0) {
    //        var f = findLiveAsset();
    //        if (f && f.d) {
    //            var ra = f.d.attrs || f.d;
    //            for (var k in ra) {
    //                if (!ra.hasOwnProperty(k)) continue;
    //                if (/^(AssetName|AssetTypeId|SiteId|lastUpdated|dlRelays|__type)$/.test(k)) continue;
    //                var aObj = ra[k];
    //                var aId = (aObj && (aObj.AttrId || aObj.AssetAttributeId)) || '';
    //                opts.push({
    //                    id: String(aId || k),
    //                    title: k,
    //                    alias: sapAliasName(k, aObj),
    //                    seq: 0
    //                });
    //            }
    //        }
    //    }

    //    /* Deduplicate by id */
    //    var seen = {};
    //    opts = opts.filter(function (o) {
    //        if (seen[o.id]) return false;
    //        seen[o.id] = true;
    //        return true;
    //    });

    //    /* Sort: sensor attributes first (by seq/alias), then DataLogger */
    //    opts.sort(function (a, b) {
    //        if (a.seq !== b.seq) return a.seq - b.seq;
    //        return a.alias.localeCompare(b.alias);
    //    });

    //    /* Build signature to avoid unnecessary DOM thrashing */
    //    var sig = opts.map(function (o) { return o.id; }).join('|');
    //    if (sel.getAttribute('data-key-signature') === sig) return;

    //    sel.innerHTML = opts.map(function (o) {
    //        return '<option value="' + esc(o.id) + '" data-title="' + esc(o.title) + '">' + esc(o.alias) + '</option>';
    //    }).join('');
    //    sel.setAttribute('data-key-signature', sig);

    //    if (oldValue && opts.some(function (o) { return o.id === oldValue; })) {
    //        sel.value = oldValue;
    //    }
    //}

    /* ── Graph series colours ─────────────────────────── */
    var GRAPH_COLORS = ['#00d4ff','#ff6b35','#4ade80','#f59e0b','#a78bfa','#f472b6','#34d399','#fb923c'];

    /* ── Collect all plottable attributes for checkboxes ─ */
    function buildGraphAttrList() {
        var aid = String(_assetId || '').trim();
        var prefix = aid + '_';
        var opts = [];
        var seen = {};

        function cleanId(v) {
            var s = String(v == null ? '' : v).trim();
            if (!s || s === '0' || /^null$/i.test(s) || /^undefined$/i.test(s)) return '';
            return s.replace(/^dl_/i, '');
        }
        function addOption(o) {
            if (!o || !o.id) return;
            var key = String(o.id).trim().toUpperCase();
            if (seen[key]) {
                if ((!seen[key].title && o.title) || /^Attr\s/i.test(seen[key].alias || '')) {
                    seen[key].title = o.title || seen[key].title;
                    seen[key].alias = o.alias || seen[key].alias;
                }
                return;
            }
            seen[key] = o;
            opts.push(o);
        }

        // 1. Asset attributes
        var uMap = window.userAssetSimpleMap || {};
        for (var key in uMap) {
            if (!uMap.hasOwnProperty(key)) continue;
            if (aid && key.indexOf(prefix) !== 0) continue;
            var entry = uMap[key] || {};
            var attrId = cleanId(entry.Id || entry.id || entry.AttrId || entry.attrId || key.substring(prefix.length));
            if (!attrId) continue;
            addOption({ id: attrId, title: entry.title || entry.Title || entry.attributeName || entry.AttributeName || '', alias: entry.AliasName || entry.aliasName || entry.name || entry.Name || ('Attr ' + attrId), seq: parseInt(attrId, 10) || 0 });
        }

        // 2. DataLogger attributes
        var dlMap = window.userAssetDataloggerMap || {};
        var dlNameIndex = {};
        function norm(v) { return String(v == null ? '' : v).trim().toUpperCase(); }
        for (var dlKey in dlMap) {
            if (!dlMap.hasOwnProperty(dlKey)) continue;
            if (aid && dlKey.indexOf(prefix) !== 0) continue;
            var dlEntry = dlMap[dlKey] || {};
            var dlId = cleanId(dlEntry.DataloggerAttributeId || dlEntry.dataloggerAttributeId || dlEntry.DlAttributeId || dlEntry.dlAttributeId);
            var dlName = dlEntry.DataloggerAttribute || dlEntry.dataloggerAttribute || dlEntry.attributeName || dlEntry.AttributeName || dlEntry.name || dlEntry.Name || '';
            var dlAssetName = dlEntry.DataloggerAssetName || dlEntry.dataloggerAssetName || '';
            var nameKey = norm(dlName) + '|' + norm(dlAssetName);
            if (!dlId && nameKey !== '|') dlId = dlNameIndex[nameKey] || '';
            if (!dlId) dlId = cleanId(dlKey.substring(prefix.length));
            if (!dlId) continue;
            if (nameKey !== '|' && dlNameIndex[nameKey] && dlNameIndex[nameKey] !== dlId) dlId = dlNameIndex[nameKey];
            if (nameKey !== '|') dlNameIndex[nameKey] = dlId;
            addOption({ id: 'dl_' + dlId, title: dlName || dlAssetName || ('DL ' + dlId), alias: dlName || dlAssetName || ('DL ' + dlId), seq: 9000 + (parseInt(dlId, 10) || 999) });
        }

        // 3. Fallback: wsLiveData keys
        if (opts.length === 0) {
            var f = findLiveAsset();
            if (f && f.d) {
                var ra = f.d.attrs || f.d;
                for (var k in ra) {
                    if (!ra.hasOwnProperty(k)) continue;
                    if (/^(AssetName|AssetTypeId|SiteId|lastUpdated|dlRelays|__type)$/i.test(k)) continue;
                    var aObj = ra[k];
                    var aId = cleanId(aObj && (aObj.AttrId || aObj.AssetAttributeId || aObj.AttributeId));
                    addOption({ id: aId || k, title: k, alias: sapAliasName(k, aObj), seq: parseInt(aId, 10) || 0 });
                }
            }
        }

        opts.sort(function (a, b) {
            if (a.seq !== b.seq) return a.seq - b.seq;
            return String(a.alias).localeCompare(String(b.alias), undefined, { numeric: true, sensitivity: 'base' });
        });
        return opts;
    }

    /* ── Render checkboxes into dropdown; select all by default ── */
    function populateGraphCheckboxes() {
        var list = el('sipAttrList');
        if (!list) return;

        var opts = buildGraphAttrList();
        var sig  = opts.map(function (o) { return o.id + ':' + o.alias; }).join('|');
        if (list.getAttribute('data-cb-sig') === sig) {
            _graphAllAttrs = opts;
            syncSelectedIdsFromDom();

            // Popup close can reset _graphSelectedIds/_graphAllAttrs while the
            // dropdown DOM still carries the old signature. Restore state from DOM.
            if (!_graphSelectedIds.length && opts.length) {
                var boxes = list.querySelectorAll('input[type=checkbox]');
                for (var bi = 0; bi < boxes.length; bi++) boxes[bi].checked = true;
                syncSelectedIdsFromDom();
            }

            updateAttrCountBadge();
            return;
        }
        list.setAttribute('data-cb-sig', sig);

        // Remember previously checked ids (if user has already interacted)
        var hadUserSel = _graphSelectedIds.length > 0;
        var prevChecked = {};
        _graphSelectedIds.forEach(function (id) { prevChecked[id] = true; });

        _graphAllAttrs = opts;

        if (!opts.length) {
            list.innerHTML = '<div style="padding:10px 12px;font-size:11px;color:var(--sap-text-muted);">No attributes found.</div>';
            _graphSelectedIds = [];
            updateAttrCountBadge();
            return;
        }

        var html = '';
        opts.forEach(function (o, i) {
            var color = GRAPH_COLORS[i % GRAPH_COLORS.length];
            var checked = hadUserSel ? (prevChecked[o.id] ? ' checked' : '') : ' checked';
            html += '<label class="sap-attr-cb-row">'
                  + '<input type="checkbox" value="' + esc(o.id) + '" data-title="' + esc(o.title) + '"' + checked + ' onchange="window._sipGraphCbChange()">'
                  + '<span class="sap-attr-cb-dot" style="background:' + color + ';"></span>'
                  + '<span class="sap-attr-cb-label" title="' + esc(o.alias) + '">' + esc(o.alias) + '</span>'
                  + '</label>';
        });
        list.innerHTML = html;

        // Sync _graphSelectedIds from DOM
        syncSelectedIdsFromDom();
        updateAttrCountBadge();

        // Trigger chart reload if graph tab is active
        if (_activeTab === 'graph' && sapIsOverlayVisible()) {
            loadGraphHistory();
        }
    }

    /* Legacy shim — kept so any call to populateGraphDropdown() still works */
    function populateGraphDropdown() { populateGraphCheckboxes(); }

    /* Global handler for checkbox change (called via inline onchange) */
    window._sipGraphCbChange = function () {
        syncSelectedIdsFromDom();
        updateAttrCountBadge();
        if (_activeTab === 'graph' && sapIsOverlayVisible()) loadGraphHistory();
    };

    function syncSelectedIdsFromDom() {
        var list = el('sipAttrList');
        if (!list) return;
        var boxes = list.querySelectorAll('input[type=checkbox]');
        _graphSelectedIds = [];
        for (var i = 0; i < boxes.length; i++) {
            if (boxes[i].checked) _graphSelectedIds.push(boxes[i].value);
        }
        // Keep legacy compat vars pointing at first selected
        var first = _graphAllAttrs.filter(function (a) { return _graphSelectedIds.indexOf(a.id) >= 0; })[0];
        _graphAttrId  = first ? first.id    : '';
        _graphAttrKey = first ? first.title : '';
        _graphAlias   = first ? first.alias : '';
    }

    function setAllGraphCheckboxes(checked) {
        var list = el('sipAttrList');
        if (!list) return;
        var boxes = list.querySelectorAll('input[type=checkbox]');
        for (var i = 0; i < boxes.length; i++) boxes[i].checked = checked;
        window._sipGraphCbChange();
    }

    function updateAttrCountBadge() {
        var badge = el('sipAttrCountBadge');
        if (!badge) return;
        var total = _graphAllAttrs.length;
        var sel   = _graphSelectedIds.length;
        badge.textContent = total === 0 ? '' : (sel === total ? '(all)' : '(' + sel + '/' + total + ')');
    }
    /* ═══════════════════════════════════════════════════════════════
       SERVER FETCHES
       Only Alert Analytics is live now.
       Other tabs remain static to avoid 404 URL errors.
       ═══════════════════════════════════════════════════════════════ */

    function loadSipAssetAlertAnalytics() {
        var body = el('sipAssetAnalyticsBody');
        if (!body) return;

        var from = el('sipAssetAnalyticsFrom') ? el('sipAssetAnalyticsFrom').value : '';
        var to = el('sipAssetAnalyticsTo') ? el('sipAssetAnalyticsTo').value : '';

        if (!_siteId) {
            body.innerHTML = '<div class="sap-empty-state">Site not found for alert analytics.</div>';
            return;
        }

        body.innerHTML = '<div class="sap-empty-state">Loading alert analytics...</div>';

        $.ajax({
            url: '/FRS25/Telemetry/GetSipAssetAlertAnalytics',
            type: 'POST',
            data: {
                siteId: _siteId || 0,

                // Keep selected asset filter if you want asset-specific analytics.
                // If you want full-site analytics, change assetId to 0 and assetName to ''.
                assetId: _assetId || 0,
                assetName: _assetName || '',

                fromDate: from || '',
                toDate: to || '',
                alertIds: _alertIds.join(',')
            },
            success: function (res) {
                if (!res || res.success === false) {
                    body.innerHTML = '<div class="sap-empty-state">No alert analytics found.</div>';
                    return;
                }

                renderSipAssetAlertAnalytics(res);
            },
            error: function (xhr) {
                body.innerHTML =
                    '<div class="sap-empty-state">Failed to load alert analytics. HTTP ' +
                    xhr.status +
                    '</div>';
            }
        });
    }
    function renderSipAssetAlertAnalytics(res) {
        var body = el('sipAssetAnalyticsBody');
        if (!body) return;

        var predictiveCauses = res.predictiveCauses || [];
        var failureCauses = res.failureCauses || [];

        var predCount = res.pred || 0;
        var failCount = res.fail || 0;

        var html = '';

        html += renderSipCauseSection('PREDICTIVE', predCount, predictiveCauses, false);
        html += renderSipCauseSection('FAILURE', failCount, failureCauses, true);

        if (!predictiveCauses.length && !failureCauses.length) {
            html = '<div class="sap-empty-state">No alert analytics found for selected date.</div>';
        }

        body.innerHTML = html;
    }

    function renderSipCauseSection(title, totalCount, causes, isFailure) {
        var html = '';

        var sectionClass = isFailure ? 'sap-section-tag sap-failure' : 'sap-section-tag';
        var headClass = isFailure ? 'sap-cc-head sap-f' : 'sap-cc-head';
        var countClass = isFailure ? 'sap-cc-count sap-hot' : 'sap-cc-count';

        html += '<div class="' + sectionClass + '">' +
            esc(title) + ' (' + totalCount + ')' +
            '</div>';

        if (!causes || !causes.length) {
            html += '<div class="sap-empty-state" style="min-height:auto;padding:14px 10px;">' +
                'No ' + esc(title.toLowerCase()) + ' cause-code data found.' +
                '</div>';
            return html;
        }

        html += '<div class="sap-cause-grid" style="margin-bottom:16px;">';

        causes.forEach(function (c) {
            var ids = c.alertIds || [];

            html +=
                '<div class="sap-cause-card" data-alertids="' + esc(ids.join(',')) + '">' +
                '<div class="' + headClass + '">' + esc(c.code || '-') + '</div>' +
                '<div class="sap-cc-body">' +
                '<span class="' + countClass + '">' + (c.count || 0) + '</span>' +
                '<span class="sap-cc-code">' + esc(c.code || '-') + '</span>' +
                '</div>' +
                '</div>';
        });

        html += '</div>';

        return html;
    }
    function bindAssetGroupCards(assetGroups) {
        var body = el('sipAssetAnalyticsBody');
        if (!body) return;

        body.querySelectorAll('.sap-asset-group-card').forEach(function (card) {
            card.addEventListener('click', function () {
                body.querySelectorAll('.sap-asset-group-card').forEach(function (c) {
                    c.style.borderColor = '';
                    c.style.boxShadow = '';
                });

                card.style.borderColor = 'var(--sap-accent-cyan)';
                card.style.boxShadow = '0 0 12px rgba(0,212,255,0.18)';

                var index = parseInt(card.getAttribute('data-index'), 10);
                var group = assetGroups[index];

                if (group) {
                    renderAssetGroupCauseBreakdown(group);
                }
            });
        });
    }

    function renderAssetGroupCauseBreakdown(group) {
        var target = el('sipAssetCauseBreakdown');
        if (!target) return;

        var causes = group.causes || [];

        var html = '';

        html += '<div class="sap-section-tag">' +
            esc(group.assetType || 'Unknown') +
            ' — Cause Code Breakdown' +
            '</div>';

        if (!causes.length) {
            html += '<div class="sap-empty-state">No cause-code data found for this asset group.</div>';
            target.innerHTML = html;
            return;
        }

        html += '<div class="sap-cause-grid">';

        causes.forEach(function (c) {
            html +=
                '<div class="sap-cause-card" data-alertids="' + esc((c.alertIds || []).join(',')) + '">' +
                '<div class="sap-cc-head">' + esc(c.code || '-') + '</div>' +
                '<div class="sap-cc-body">' +
                '<span class="sap-cc-count">' + (c.total || 0) + '</span>' +
                '<span class="sap-cc-code">P:' + (c.pred || 0) + ' / F:' + (c.fail || 0) + '</span>' +
                '</div>' +
                '</div>';
        });

        html += '</div>';

        target.innerHTML = html;
    }

    /* ═══════════════════════════════════════════════════════════════
       EVENT LOG TAB — alert history + maintenance mode timeline
       Calls:
         POST /FRS25/FRSAlert/_List  →  mFRSAlerts[]
         POST /FRS25/MaintenanceMode/DownloadMaintenanceMode → mMaintenanceModes[]
       ═══════════════════════════════════════════════════════════════ */
    var _eventReqNo = 0;

    function loadEventLog() {
        var logEl = el('sipAssetEventLog');
        if (!logEl) return;

        if (!_assetId) {
            logEl.innerHTML = '<div class="sap-empty-state">No asset selected.</div>';
            return;
        }

        var from = (el('sipAssetEventFrom') || {}).value || today();
        var to = (el('sipAssetEventTo') || {}).value || today();
        var reqNo = ++_eventReqNo;

        logEl.innerHTML = '<div class="sap-empty-state">Loading event log\u2026</div>';

        var assetTypeId = 0;
        try {
            var f = findLiveAsset();
            if (f && f.d) assetTypeId = parseInt(f.d.AssetTypeId || 0);
        } catch (e) { }

        /* Single combined call → returns both Alerts[] and MaintenanceModes[] */
        $.ajax({
            url: '/FRS25/Telemetry/GetSipEventLog',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                AssetId: parseInt(_assetId),
                SiteId: _siteId ? parseInt(_siteId) : 0,
                AssetTypeId: assetTypeId || 0,
                FromDate: from + 'T00:00:00',
                ToDate: to + 'T23:59:59',
                Take: 200
            }),
            dataType: 'json',
            timeout: 30000,
            success: function (res) {
                if (reqNo !== _eventReqNo) return;
                if (!res || !res.Success) {
                    logEl.innerHTML = '<div class="sap-empty-state">' + esc(res && res.Message ? res.Message : 'No data returned.') + '</div>';
                    return;
                }
                renderEventLog(logEl, res.Alerts || [], res.MaintenanceModes || []);
            },
            error: function (xhr) {
                if (reqNo !== _eventReqNo) return;
                logEl.innerHTML = '<div class="sap-empty-state">Failed to load event log. HTTP ' + xhr.status + '</div>';
            }
        });
    }

    function renderEventLog(container, alerts, maintModes) {
        /* ── Build unified timeline: alerts + maint mode toggles ── */
        var events = [];

        /* Alert events */
        for (var i = 0; i < alerts.length; i++) {
            var a = alerts[i];
            var incDt = a.IncidentDateTime || a.SetTimestamp || a.incidentDateTime || a.setTimestamp || '';
            var code = a.CauseCode || a.causeCode || a.AlertCode || a.alertCode || '';
            var status = parseInt(a.AlertStatus || a.alertStatus || 0);
            var severity = a.Severity || a.severity || '';
            events.push({
                time: parseEventDate(incDt),
                rawTime: incDt,
                type: 'alert',
                dotClass: status === 1 ? 'sap-error' : status === 2 ? 'sap-warn' : 'sap-success',
                text: code,
                detail: severity ? (' \u2014 Severity: ' + severity) : '',
                statusLabel: status === 1 ? 'Active' : 'Cleared'
            });
        }

        /* Maintenance mode events */
        for (var m = 0; m < maintModes.length; m++) {
            var mm = maintModes[m];
            var activeTime = mm.ActiveTime || mm.activeTime || '';
            var inactiveTime = mm.InActiveTime || mm.inActiveTime || '';
            var isActive = mm.IsMaintenceMode;

            if (activeTime) {
                events.push({
                    time: parseEventDate(activeTime),
                    rawTime: activeTime,
                    type: 'maint',
                    dotClass: 'sap-warn',
                    text: 'Maintenance Mode Activated',
                    detail: '',
                    statusLabel: 'Active'
                });
            }
            if (inactiveTime && inactiveTime.indexOf('0001') < 0) {
                events.push({
                    time: parseEventDate(inactiveTime),
                    rawTime: inactiveTime,
                    type: 'maint',
                    dotClass: 'sap-success',
                    text: 'Maintenance Mode Deactivated',
                    detail: '',
                    statusLabel: 'Inactive'
                });
            }
        }

        /* Sort newest first */
        events.sort(function (a, b) { return b.time.getTime() - a.time.getTime(); });

        if (!events.length) {
            container.innerHTML = '<div class="sap-empty-state">No events found for this period.</div>';
            return;
        }

        /* Count maintenance mode toggles */
        var maintActiveCount = 0;
        var maintInactiveCount = 0;
        events.forEach(function (e) {
            if (e.type === 'maint' && e.statusLabel === 'Active') maintActiveCount++;
            if (e.type === 'maint' && e.statusLabel === 'Inactive') maintInactiveCount++;
        });
        var alertCount = alerts.length;

        /* Summary header */
        var html = '<div style="padding:12px 20px 0;font-size:12px;color:var(--sap-text-secondary);">' +
            '<span style="color:var(--sap-accent-cyan);font-weight:600;">' + alertCount + '</span> alert(s), ' +
            '<span style="color:var(--sap-accent-yellow);font-weight:600;">' + maintActiveCount + '</span> maintenance activation(s), ' +
            '<span style="color:var(--sap-accent-green);font-weight:600;">' + maintInactiveCount + '</span> deactivation(s)' +
            '</div>';

        /* Timeline rows */
        for (var j = 0; j < events.length; j++) {
            var ev = events[j];
            var timeStr = fmtEventTime(ev.time);
            var icon = ev.type === 'maint'
                ? '<svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z"/></svg> '
                : '';
            html += '<div class="sap-event-row">' +
                '<span class="sap-event-time">' + esc(timeStr) + '</span>' +
                '<span class="sap-event-dot ' + ev.dotClass + '"></span>' +
                '<span class="sap-event-text">' + icon +
                (ev.type === 'alert' ? 'Alert \u2014 <span class="sap-code">' + esc(ev.text) + '</span>' + esc(ev.detail) + ' [' + ev.statusLabel + ']'
                    : esc(ev.text)) +
                '</span></div>';
        }

        container.innerHTML = html;
    }

    function parseEventDate(raw) {
        if (!raw) return new Date(0);
        /* Handle .NET /Date(...)/ format */
        var m = String(raw).match(/\/Date\((-?\d+)/);
        if (m) return new Date(parseInt(m[1]));
        var d = new Date(raw);
        return isNaN(d.getTime()) ? new Date(0) : d;
    }

    function fmtEventTime(d) {
        if (!d || isNaN(d.getTime()) || d.getTime() === 0) return '--';
        var dd = ('0' + d.getDate()).slice(-2);
        var mm = ('0' + (d.getMonth() + 1)).slice(-2);
        var hh = ('0' + d.getHours()).slice(-2);
        var mi = ('0' + d.getMinutes()).slice(-2);
        var ss = ('0' + d.getSeconds()).slice(-2);
        return dd + '/' + mm + ' ' + hh + ':' + mi + ':' + ss;
    }

    /* ═══════════════════════════════════════════════════════════════
       ALARM TAB — active alerts for the asset
       Calls: POST /FRS25/Telemetry/GetSipActiveAlarms
              → { Alarms: [{ CauseCode, RaisedAt, DurationDisplay, Severity }] }
       ═══════════════════════════════════════════════════════════════ */
    var _alarmReqNo = 0;

    function loadActiveAlarms() {
        var banner = el('sipAssetAlarmBannerText');
        var tbody = el('sipAssetAlarmRows');
        if (!tbody) return;

        if (!_assetId) {
            tbody.innerHTML = '<tr><td colspan="5" style="color:var(--sap-text-muted)">No asset selected.</td></tr>';
            return;
        }

        var reqNo = ++_alarmReqNo;
        tbody.innerHTML = '<tr><td colspan="5" style="color:var(--sap-text-muted)">Loading\u2026</td></tr>';

        var assetTypeId = 0;
        try {
            var f = findLiveAsset();
            if (f && f.d) assetTypeId = parseInt(f.d.AssetTypeId || 0);
        } catch (e) { }

        $.ajax({
            url: '/FRS25/Telemetry/GetSipActiveAlarms',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({
                AssetId: parseInt(_assetId),
                SiteId: _siteId ? parseInt(_siteId) : 0,
                AssetTypeId: assetTypeId || 0
            }),
            dataType: 'json',
            timeout: 30000,
            success: function (res) {
                if (reqNo !== _alarmReqNo) return;
                if (!res || !res.Success) {
                    tbody.innerHTML = '<tr><td colspan="5" style="color:var(--sap-text-muted)">' + esc(res && res.Message ? res.Message : 'No data.') + '</td></tr>';
                    return;
                }
                renderActiveAlarms(banner, tbody, res.Alarms || [], res.ActiveCount || 0);
            },
            error: function (xhr) {
                if (reqNo !== _alarmReqNo) return;
                tbody.innerHTML = '<tr><td colspan="5" style="color:var(--sap-accent-red)">Failed to load alarms. HTTP ' + xhr.status + '</td></tr>';
            }
        });
    }

    function renderActiveAlarms(banner, tbody, alarms, activeCount) {
        /* ── Banner ── */
        if (banner) {
            if (activeCount > 0) {
                banner.innerHTML = activeCount + ' Active Alarm' + (activeCount > 1 ? 's' : '') +
                    ' on ' + esc(_assetName) +
                    '<span>Requires immediate attention from maintenance team</span>';
            } else {
                banner.innerHTML = 'No Active Alarms on ' + esc(_assetName) +
                    '<span>All systems operating within normal parameters.</span>';
            }
        }

        if (!alarms.length) {
            tbody.innerHTML = '<tr><td colspan="5" style="color:var(--sap-text-muted)">No active alarms.</td></tr>';
            return;
        }

        var rows = '';
        for (var i = 0; i < alarms.length; i++) {
            var a = alarms[i];

            /* Raised At */
            var raised = parseEventDate(a.RaisedAt);
            var raisedStr = raised.getTime() > 0 ? fmtAlarmDateTime(raised) : '--';

            /* Duration — server provides preformatted, but recalculate for live accuracy */
            var duration = a.DurationDisplay || '\u2014';
            if (raised.getTime() > 0) {
                var diffMs = Date.now() - raised.getTime();
                if (diffMs > 0) {
                    var h = Math.floor(diffMs / 3600000);
                    var m = Math.floor((diffMs % 3600000) / 60000);
                    var s = Math.floor((diffMs % 60000) / 1000);
                    duration = ('0' + h).slice(-2) + ':' + ('0' + m).slice(-2) + ':' + ('0' + s).slice(-2);
                }
            }

            /* Severity color */
            var sevLower = String(a.Severity || '').toLowerCase();
            var sevColor = sevLower === 'high' || sevLower === 'critical'
                ? 'var(--sap-accent-red)'
                : sevLower === 'medium' ? 'var(--sap-accent-yellow)'
                    : sevLower === 'low' ? 'var(--sap-accent-orange)'
                        : 'var(--sap-text-secondary)';

            rows += '<tr>' +
                '<td style="font-family:JetBrains Mono,monospace;font-size:12px;color:var(--sap-accent-cyan)">' + esc(a.CauseCode || '') + '</td>' +
                '<td>' + esc(raisedStr) + '</td>' +
                '<td style="font-family:JetBrains Mono,monospace">' + duration + '</td>' +
                '<td><span style="color:' + sevColor + '">' + esc(a.Severity || '--') + '</span></td>' +
                '<td><span class="sap-alarm-status">\u25CF Active</span></td>' +
                '</tr>';
        }

        tbody.innerHTML = rows;
    }

    function fmtAlarmDateTime(d) {
        if (!d || isNaN(d.getTime()) || d.getTime() === 0) return '--';
        var dd = ('0' + d.getDate()).slice(-2);
        var mm = ('0' + (d.getMonth() + 1)).slice(-2);
        var yy = d.getFullYear();
        var hh = ('0' + d.getHours()).slice(-2);
        var mi = ('0' + d.getMinutes()).slice(-2);
        return dd + '/' + mm + '/' + yy + ' ' + hh + ':' + mi;
    }

    function loadGraphHistory() {
        if (_activeTab !== 'graph' || !sapIsOverlayVisible()) return;

        var canvas = el('sipAssetChartCanvas');
        if (!canvas || !sapNodeExists(canvas)) return;

        /* Ensure checkboxes are populated first */
        if (!_graphAllAttrs.length) populateGraphCheckboxes();

        if (!_graphSelectedIds.length) {
            drawGraphMessage('Select at least one attribute to plot.');
            return;
        }

        if (!_assetId || parseInt(_assetId, 10) <= 0) {
            drawGraphMessage('Asset ID not available — select an asset with live data.');
            return;
        }

        /* ── Always 24 hours. Today → 00:00 to now. Past date → full day. ── */
        var graphDateVal = el('sipAssetGraphDate') ? el('sipAssetGraphDate').value : today();
        if (!graphDateVal) graphDateVal = today();

        var isToday = (graphDateVal === today());
        var now = new Date();
        var startDate = new Date(graphDateVal + 'T00:00:00');
        var endDate = isToday ? now : new Date(graphDateVal + 'T23:59:59');

        var rangeLabel = el('sipGraphTimeRange');
        if (rangeLabel) {
            rangeLabel.textContent =
                ('0' + startDate.getHours()).slice(-2) + ':' + ('0' + startDate.getMinutes()).slice(-2) +
                ' – ' +
                ('0' + endDate.getHours()).slice(-2) + ':' + ('0' + endDate.getMinutes()).slice(-2) +
                (isToday ? ' (live)' : '');
        }

        var startStr = formatHistoryControllerDate(fmtDateParts(startDate), fmtTimeParts(startDate));
        var endStr   = formatHistoryControllerDate(fmtDateParts(endDate),   fmtTimeParts(endDate));

        var reqNo = ++_graphRequestNo;
        var selCount = _graphSelectedIds.length;
        drawGraphMessage('Loading ' + selCount + ' attribute' + (selCount > 1 ? 's' : '') + '…');

        $.ajax({
            url: _historyUrl,
            type: 'GET',
            dataType: 'json',
            timeout: 30000,
            data: {
                assetId: parseInt(_assetId, 10),
                startDate: startStr,
                endDate: endStr
            },
            success: function (res) {
                if (reqNo !== _graphRequestNo || _activeTab !== 'graph' || !sapIsOverlayVisible()) return;

                /* Build a series for every selected attribute */
                _graphSeriesData = {};
                var seriesList = [];

                _graphAllAttrs.forEach(function (attr, i) {
                    if (_graphSelectedIds.indexOf(attr.id) < 0) return;
                    var pts = extractAttributePoints(res, attr.id, attr.title, attr.alias);
                    _graphSeriesData[attr.id] = pts;
                    seriesList.push({
                        id:     attr.id,
                        title:  attr.title,
                        alias:  attr.alias,
                        points: pts,
                        color:  GRAPH_COLORS[i % GRAPH_COLORS.length]
                    });
                });

                /* Legacy compat: keep _graphPoints = first series points */
                _graphPoints = seriesList.length ? seriesList[0].points : [];

                var hasData = seriesList.some(function (s) { return s.points.length > 0; });
                if (!hasData) {
                    drawGraphMessage('No data found for the selected attribute(s) on this date.');
                    return;
                }

                drawHistoryChart(seriesList);
            },
            error: function (xhr) {
                if (reqNo !== _graphRequestNo || _activeTab !== 'graph' || !sapIsOverlayVisible()) return;
                drawGraphMessage('Failed to load graph data. HTTP ' + xhr.status);
            }
        });
    }
    /* ── LIVE APPEND: read current WS values and add to all cached series ── */
    function appendLiveToGraph() {
        if (_activeTab !== 'graph' || !sapIsOverlayVisible()) return;
        if (!_graphSelectedIds.length) return;

        /* Only live-append if viewing today */
        var graphDateVal = el('sipAssetGraphDate') ? el('sipAssetGraphDate').value : today();
        if (graphDateVal && graphDateVal !== today()) return;

        var f = findLiveAsset();
        if (!f || !f.d) return;

        var liveAttrs = f.d.attrs || f.d;
        var now      = new Date();
        var timeStr  = ('0' + now.getHours()).slice(-2) + ':' + ('0' + now.getMinutes()).slice(-2);
        var nowMs    = now.getTime();
        var appended = false;

        var seriesList = [];
        _graphAllAttrs.forEach(function (attr, i) {
            if (_graphSelectedIds.indexOf(attr.id) < 0) return;
            var pts = _graphSeriesData[attr.id] || [];

            /* Read live value by wsLiveData key (attr.title) */
            var raw = liveAttrs[attr.title];
            var val = parseNumber(raw);
            if (val != null) {
                var last = pts[pts.length - 1];
                if (!last || last.value !== val || (nowMs - (last.sortKey || 0)) >= 10000) {
                    pts.push({ time: timeStr, rawTime: now.toISOString(), value: val, sortKey: nowMs });
                    appended = true;
                }
            }
            _graphSeriesData[attr.id] = pts;
            seriesList.push({ id: attr.id, title: attr.title, alias: attr.alias, points: pts, color: GRAPH_COLORS[i % GRAPH_COLORS.length] });
        });

        if (!appended && seriesList.every(function (s) { return !s.points.length; })) return;

        /* Legacy compat */
        _graphPoints = seriesList.length ? seriesList[0].points : [];

        /* Update time range label */
        var rangeLabel = el('sipGraphTimeRange');
        if (rangeLabel) rangeLabel.textContent = '00:00 – ' + timeStr + ' (live)';

        if (window.__sipGraphRAF) cancelAnimationFrame(window.__sipGraphRAF);
        window.__sipGraphRAF = requestAnimationFrame(function () {
            window.__sipGraphRAF = null;
            drawHistoryChart(seriesList);
        });
    }
    /* ── Date/time formatting helpers for the controller endpoint ── */
    function fmtDateParts(d) {
        return d.getFullYear() + '-' + ('0' + (d.getMonth() + 1)).slice(-2) + '-' + ('0' + d.getDate()).slice(-2);
    }
    function fmtTimeParts(d) {
        return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2);
    }

    /* ═══════════════════════════════════════════════════════════════
       RESPONSE PARSING — handles the ACTUAL GetHistoryData format:

       {
         Data: [
           {
             AttributeId: 25,
             Values: {
               "key0": {
                 Timestamp: { TimestampDevice: "...", TimestampLocal: "..." },
                 Value: 23.5,
                 DataType: "PointMachine"
               }, ...
             }
           }, ...
         ]
       }

       Also handles flat-row arrays and CSV for forward-compatibility.
       ═══════════════════════════════════════════════════════════════ */

    /* Build a map: AttrId → live attribute key name from wsLiveData */
    function buildAttrIdMap() {
        var map = {};
        var asset = (window.wsLiveData || {})[_assetId];
        if (asset && asset.attrs) {
            for (var k in asset.attrs) {
                if (!asset.attrs.hasOwnProperty(k)) continue;
                var a = asset.attrs[k];
                var aid = parseInt(a.AttrId || a.AssetAttributeId || 0);
                if (aid) map[aid] = k;
            }
        }
        /* Also check dlAssetRoleMap (DataLogger relay names) */
        if (typeof window.dlAssetRoleMap !== 'undefined') {
            for (var dk in window.dlAssetRoleMap) {
                if (dk.indexOf(String(_assetId) + '_') === 0) {
                    var did = parseInt(dk.split('_')[1]);
                    if (did && !map[did]) map[did] = window.dlAssetRoleMap[dk];
                }
            }
        }
        return map;
    }

    /* Fallback attribute ID → name (mirrors telemetrylive.js getAttributeName) */
    var _fallbackAttrNames = {
        1: 'ITC FEED END(mA)', 2: 'ITC RELAY END(mA)', 3: 'VTC RELAY END(V)', 4: 'VTC CH FEED END(V)',
        5: 'ITC TFC O/P(mA)', 6: 'VTC 24 DC TPR I/P(V)',
        9: 'RG V', 10: 'RG mA', 11: 'DG V', 12: 'DG mA',
        13: 'HG V', 14: 'HG mA', 15: 'HHG V', 16: 'HHG mA',
        25: 'A End - NWKR', 26: 'A End - RWKR', 27: 'B End - NWKR', 28: 'B End - RWKR',
        31: 'Rx1 mV', 32: 'Rx2 mV', 33: 'Tx1 V', 34: 'Tx2 V', 35: 'Supply V', 36: 'Modem mV',
        154: 'Root V', 155: 'Root mA', 227: 'UG V', 228: 'UG mA',
        337: 'PILOT mA', 499: 'PILOT V',
        576: 'A End - NWKR (Loc)', 577: 'A End - RWKR (Loc)',
        578: 'B End - NWKR (Loc)', 579: 'B End - RWKR (Loc)',
        610: 'Co_Hg mA', 611: 'Co_Hg V'
    };

    function resolveAttrId(attrId, liveMap) {
        if (liveMap[attrId]) return liveMap[attrId];
        if (_fallbackAttrNames[attrId]) return _fallbackAttrNames[attrId];
        if (typeof window.getAttributeName === 'function') return window.getAttributeName(_assetId, attrId);
        return 'Attr ' + attrId;
    }

    function extractAttributePoints(res, attrId, attrTitle, aliasName) {
        if (!res) return [];

        /* ── FORMAT 1: { Data: [ { AttributeId, Values: {...} } ] } ── */
        var dataArr = res.Data || res.data;
        if (Array.isArray(dataArr) && dataArr.length && dataArr[0] && dataArr[0].Values !== undefined) {
            return extractFromServerFormat(dataArr, attrId, attrTitle, aliasName);
        }

        /* ── FORMAT 2: flat row array ── */
        var attrKey = attrTitle || aliasName || '';
        if (Array.isArray(res)) return extractFromFlatRows(res, attrKey, aliasName);
        if (Array.isArray(res.data)) return extractFromFlatRows(res.data, attrKey, aliasName);
        if (Array.isArray(res.Data)) return extractFromFlatRows(res.Data, attrKey, aliasName);
        if (Array.isArray(res.rows)) return extractFromFlatRows(res.rows, attrKey, aliasName);

        /* ── FORMAT 3: CSV string ── */
        if (typeof res === 'string') return extractFromCsv(res, attrKey, aliasName);
        if (typeof (res.csv || res.Csv) === 'string') return extractFromCsv(res.csv || res.Csv, attrKey, aliasName);

        return [];
    }

    /* Parse the server's nested { AttributeId, Values: {} } format.
       Matches by NUMERIC AttributeId directly — no name guessing needed. */
    function extractFromServerFormat(dataArr, attrId, attrTitle, aliasName) {
        var numericId = parseInt(attrId, 10);   // e.g. 3
        var isDL = String(attrId).indexOf('dl_') === 0;
        var dlRoleId = isDL ? parseInt(String(attrId).replace('dl_', ''), 10) : NaN;
        var points = [];

        /* Also build name-based fallback for non-numeric attrId (legacy) */
        var liveMap = buildAttrIdMap();
        var titleLow = String(attrTitle || '').toLowerCase();
        var aliasLow = String(aliasName || '').toLowerCase();

        for (var i = 0; i < dataArr.length; i++) {
            var attrBlock = dataArr[i];
            var blockId = parseInt(attrBlock.AttributeId || attrBlock.attributeId || 0);

            /* ── PRIMARY: match by numeric Id ── */
            var matched = false;
            if (!isNaN(numericId) && numericId > 0 && blockId === numericId) {
                matched = true;
            }
            /* ── DataLogger: match dl role id ── */
            if (!matched && isDL && !isNaN(dlRoleId) && blockId === dlRoleId) {
                matched = true;
            }
            /* ── FALLBACK: match by resolved name (for legacy / CSV rows) ── */
            if (!matched && (titleLow || aliasLow)) {
                var resolvedName = resolveAttrId(blockId, liveMap);
                var resolvedAlias = cleanAliasName(resolvedName);
                if ((titleLow && String(resolvedName).toLowerCase() === titleLow) ||
                    (aliasLow && String(resolvedAlias).toLowerCase() === aliasLow)) {
                    matched = true;
                }
            }

            if (!matched) continue;

            /* Skip pure DataLogger relay blocks (binary 0/1 — not graphable) */
            var vals = attrBlock.Values || attrBlock.values || {};
            var vKeys = Object.keys(vals);
            if (!isDL && vKeys.length > 0) {
                var isAllDL = vKeys.every(function (k) {
                    return vals[k] && String(vals[k].DataType || '').toLowerCase() === 'datalogger';
                });
                if (isAllDL) continue;
            }

            /* Extract time → value pairs */
            for (var vk in vals) {
                if (!vals.hasOwnProperty(vk)) continue;
                var entry = vals[vk];
                if (!entry) continue;

                var ts = '';
                if (entry.Timestamp) {
                    ts = entry.Timestamp.TimestampDevice || entry.Timestamp.TimestampLocal || '';
                } else {
                    ts = entry.timestamp || entry.TimestampDevice || '';
                }

                if (!ts || ts.indexOf('0001') >= 0) continue;

                var val = parseNumber(entry.Value != null ? entry.Value : entry.value);
                if (val == null) continue;

                points.push({
                    time: formatTimeOnly(ts),
                    rawTime: ts,
                    value: val,
                    sortKey: new Date(ts).getTime() || 0
                });
            }
        }

        /* Sort by timestamp */
        points.sort(function (a, b) { return a.sortKey - b.sortKey; });

        return points;
    }

    /* Parse flat-row format: [{ Timestamp, AttributeName, Value }, ...] */
    function extractFromFlatRows(rows, attrKey, aliasName) {
        var points = [];
        if (!rows || !rows.length) return points;

        var attrKeyLow = String(attrKey || '').toLowerCase();
        var aliasLow = String(aliasName || '').toLowerCase();

        rows.forEach(function (r) {
            if (!r) return;

            var rawTime =
                r.Timestamp || r.TimeStamp || r.timestamp ||
                r.TimestampDevice || r.timestampDevice ||
                r.Date || r.date || r.CurrentDate || r.currentDate ||
                r.Time || r.time || '';

            var rowAttr =
                r.AttributeName || r.attributeName ||
                r.AssetAttributeName || r.assetAttributeName ||
                r.Attribute || r.attribute || '';

            var val = null;

            if (rowAttr) {
                var rowAlias = cleanAliasName(rowAttr);
                if (String(rowAttr).toLowerCase() === attrKeyLow ||
                    String(rowAlias).toLowerCase() === aliasLow) {
                    val = parseNumber(r.Value || r.value || r.CurrentValue || r.currentValue);
                }
            }

            /* Wide row format: { Date: ..., "Charger V": 112 } */
            if (val == null && r[attrKey] != null) val = parseNumber(r[attrKey]);
            if (val == null && r[aliasName] != null) val = parseNumber(r[aliasName]);

            if (val == null) return;

            points.push({
                time: formatTimeOnly(rawTime),
                rawTime: rawTime,
                value: val
            });
        });

        return points;
    }

    /* Parse CSV string */
    function extractFromCsv(csv, attrKey, aliasName) {
        csv = String(csv || '').trim();
        if (!csv) return [];

        var lines = csv.split(/\r?\n/).filter(function (x) { return x.trim() !== ''; });
        if (lines.length < 2) return [];

        var headers = splitCsvLine(lines[0]).map(function (h) { return h.trim(); });
        var dateIdx = findHeaderIndex(headers, ['Date', 'Timestamp', 'TimeStamp', 'TimestampDevice', 'Time', 'CurrentDate']);
        if (dateIdx < 0) dateIdx = 0;

        var attrIdx = findBestAttributeColumn(headers, attrKey, aliasName);
        if (attrIdx < 0) return [];

        var points = [];
        for (var i = 1; i < lines.length; i++) {
            var cols = splitCsvLine(lines[i]);
            var n = parseNumber(cols[attrIdx]);
            if (n == null) continue;
            points.push({ time: formatTimeOnly(cols[dateIdx] || ''), rawTime: cols[dateIdx] || '', value: n });
        }
        return points;
    }

    function splitCsvLine(line) {
        var out = [], cur = '', inside = false;
        line = String(line || '');
        for (var i = 0; i < line.length; i++) {
            var ch = line[i];
            if (ch === '"') { if (inside && line[i + 1] === '"') { cur += '"'; i++; } else { inside = !inside; } }
            else if (ch === ',' && !inside) { out.push(cur); cur = ''; }
            else { cur += ch; }
        }
        out.push(cur);
        return out;
    }

    function findHeaderIndex(headers, names) {
        for (var i = 0; i < headers.length; i++) {
            var h = String(headers[i]).trim().toLowerCase();
            for (var j = 0; j < names.length; j++) {
                if (h === String(names[j]).trim().toLowerCase()) return i;
            }
        }
        return -1;
    }

    function findBestAttributeColumn(headers, attrKey, aliasName) {
        var attrLow = String(attrKey || '').trim().toLowerCase();
        var aliasLow = String(aliasName || '').trim().toLowerCase();
        for (var i = 0; i < headers.length; i++) {
            if (String(headers[i]).trim().toLowerCase() === attrLow) return i;
        }
        for (var j = 0; j < headers.length; j++) {
            if (cleanAliasName(headers[j]).toLowerCase() === aliasLow) return j;
        }
        for (var k = 0; k < headers.length; k++) {
            var h = String(headers[k]).toLowerCase();
            if (attrLow && h.indexOf(attrLow) >= 0) return k;
            if (aliasLow && h.indexOf(aliasLow) >= 0) return k;
        }
        return -1;
    }

    function lockSapChartHeight(host) {
        if (!host || !host.style) return;

        var vh = window.innerHeight || document.documentElement.clientHeight || 720;
        var h = Math.round(Math.max(460, Math.min(620, vh - 240)));

        if (vh < 680) {
            h = Math.round(Math.max(320, vh - 260));
        }

        host.style.height = h + 'px';
        host.style.minHeight = h + 'px';
        host.style.maxHeight = h + 'px';
        host.style.flex = '0 0 ' + h + 'px';
    }

    function drawGraphMessage(message) {
        _chartGeo = null;  // disable hover tooltip
        var canvas = el('sipAssetChartCanvas');
        if (!canvas || !sapNodeExists(canvas) || !canvas.parentElement) return;

        var host = canvas.parentElement;
        lockSapChartHeight(host);

        var ctx = canvas.getContext('2d');
        var dpr = window.devicePixelRatio || 1;
        var rect = host.getBoundingClientRect();
        if (!rect || rect.width <= 0 || rect.height <= 0) return;

        var W = Math.floor(rect.width);
        var H = Math.floor(rect.height);
        var needW = Math.round(W * dpr);
        var needH = Math.round(H * dpr);

        // Important: do not write rect.height + 'px' to the canvas style.
        // That creates a parent/canvas height feedback loop during sync.
        canvas.style.width = '100%';
        canvas.style.height = '100%';

        if (canvas.width !== needW || canvas.height !== needH) {
            canvas.width = needW;
            canvas.height = needH;
        }

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, W, H);

        ctx.fillStyle = '#5a6a8a';
        ctx.font = '13px IBM Plex Sans, Arial, sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(message, W / 2, H / 2);
    }

    function drawHistoryChart(seriesList) {
        /* Accept legacy call signature: drawHistoryChart(points, alias) */
        if (Array.isArray(seriesList) && seriesList.length && !seriesList[0].hasOwnProperty('points')) {
            var legacyAlias = arguments[1] || 'Value';
            seriesList = [{ id: 'legacy', alias: legacyAlias, points: seriesList, color: GRAPH_COLORS[0] }];
        }
        if (!Array.isArray(seriesList)) seriesList = [];

        var canvas = el('sipAssetChartCanvas');
        if (!canvas || !sapNodeExists(canvas) || !canvas.parentElement || _activeTab !== 'graph') return;

        var ctx = canvas.getContext('2d');
        var dpr = window.devicePixelRatio || 1;
        var host = canvas.parentElement;
        lockSapChartHeight(host);

        canvas.style.width = '100%';
        canvas.style.height = '100%';

        var rect = host.getBoundingClientRect();
        if (!rect || rect.width <= 0 || rect.height <= 0) return;

        var W = Math.floor(rect.width);
        var H = Math.floor(rect.height);
        var needW = Math.round(W * dpr);
        var needH = Math.round(H * dpr);
        if (canvas.width !== needW || canvas.height !== needH) {
            canvas.width = needW;
            canvas.height = needH;
        } else {
            ctx.setTransform(1, 0, 0, 1, 0, 0);
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        }
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        /* Legend height: 18 px per row, max 3 cols */
        var visibleSeries = seriesList.filter(function (s) { return s.points && s.points.length > 0; });
        var legendCols    = Math.min(3, visibleSeries.length);
        var legendRows    = visibleSeries.length > 0 ? Math.ceil(visibleSeries.length / legendCols) : 0;
        var legendH       = legendRows > 0 ? legendRows * 18 + 8 : 0;

        var pad = { top: 34, right: 24, bottom: 42 + legendH, left: 58 };
        var gw  = W - pad.left - pad.right;
        var gh  = H - pad.top  - pad.bottom;

        ctx.clearRect(0, 0, W, H);

        /* ── Y range model ─────────────────────────────────────
           Single attribute  : raw/global Y-axis.
           Multiple attributes: per-series normalized Y-axis so every selected
           attribute shows its own up/down step movement. Raw values remain in
           tooltip and legend range. A common raw Y-axis cannot represent mixed
           units/ranges such as TPR(0/1), current(mA), voltage(V), etc. */
        var isMultiScale = visibleSeries.length > 1;
        var allVals = [];
        visibleSeries.forEach(function (s) {
            s.points.forEach(function (p) {
                if (p && p.value !== null && p.value !== undefined && !isNaN(p.value)) allVals.push(p.value);
            });
        });

        var minV = allVals.length ? Math.min.apply(null, allVals) : 0;
        var maxV = allVals.length ? Math.max.apply(null, allVals) : 1;
        if (minV === maxV) { minV = minV - 1; maxV = maxV + 1; }
        var range = maxV - minV;
        minV = minV - range * 0.08;
        maxV = maxV + range * 0.08;

        /* Prepare individual scales for multi-attribute mode. */
        visibleSeries.forEach(function (s) {
            var vals = [];
            (s.points || []).forEach(function (p) {
                if (p && p.value !== null && p.value !== undefined && !isNaN(p.value)) vals.push(p.value);
            });
            var lo = vals.length ? Math.min.apply(null, vals) : 0;
            var hi = vals.length ? Math.max.apply(null, vals) : 1;
            s._rawMin = lo;
            s._rawMax = hi;

            if (lo === hi) {
                lo = lo - 1;
                hi = hi + 1;
            } else {
                var sr = hi - lo;
                lo = lo - sr * 0.08;
                hi = hi + sr * 0.08;
            }
            s._sapScale = { min: lo, max: hi };
        });

        /* ── Title ── */
        ctx.fillStyle = '#e2e8f0';
        ctx.font = '600 13px IBM Plex Sans, Arial, sans-serif';
        ctx.textAlign = 'left';
        var titleText = visibleSeries.length === 1
            ? (visibleSeries[0].alias || 'Attribute')
            : (visibleSeries.length + ' Attributes · Multi-scale');
        ctx.fillText(titleText, pad.left, 18);

        /* ── Record count ── */
        var totalPts = seriesList.reduce(function (n, s) {
            return Math.max(n, (s.points ? s.points.length : 0));
        }, 0);
        ctx.fillStyle = '#5a6a8a';
        ctx.font = '10px IBM Plex Sans, Arial, sans-serif';
        ctx.textAlign = 'right';
        ctx.fillText(totalPts + ' records', W - pad.right, 18);

        /* ── Grid lines + Y-axis labels ── */
        ctx.strokeStyle = 'rgba(90,106,138,0.28)';
        ctx.lineWidth = 1;
        for (var i = 0; i <= 4; i++) {
            var gy  = pad.top + (gh / 4) * i;
            var val = maxV - ((maxV - minV) * (i / 4));
            ctx.beginPath();
            ctx.moveTo(pad.left, gy);
            ctx.lineTo(pad.left + gw, gy);
            ctx.stroke();
            ctx.fillStyle = '#5a6a8a';
            ctx.font = '10px JetBrains Mono, monospace';
            ctx.textAlign = 'right';
            var yLabel = isMultiScale ? ((4 - i) * 25 + '%') : formatGraphValue(val);
            ctx.fillText(yLabel, pad.left - 8, gy + 3);
        }

        /* ── X-axis time labels (use first non-empty series for ticks) ── */
        var refPts = (visibleSeries[0] && visibleSeries[0].points) || [];
        if (refPts.length) {
            ctx.fillStyle = '#5a6a8a';
            ctx.font = '10px JetBrains Mono, monospace';
            ctx.textAlign = 'center';
            var tickCount = Math.min(7, refPts.length);
            for (var t = 0; t < tickCount; t++) {
                var idx = tickCount === 1 ? 0 : Math.round((refPts.length - 1) * (t / (tickCount - 1)));
                var tx  = pad.left + (gw * idx / Math.max(1, refPts.length - 1));
                ctx.fillText(refPts[idx].time || '', tx, H - legendH - 16);
            }
        }

        function px(pts, index) { return pad.left + (gw * index / Math.max(1, pts.length - 1)); }
        function pyGlobal(v) {
            var span = (maxV - minV) || 1;
            return pad.top + gh - ((v - minV) / span) * gh;
        }
        function yForSeries(s, v) {
            if (!isMultiScale) return pyGlobal(v);
            var sc = (s && s._sapScale) ? s._sapScale : { min: minV, max: maxV };
            var span = (sc.max - sc.min) || 1;
            return pad.top + gh - ((v - sc.min) / span) * gh;
        }

        /* ── Draw each series as a step-line ── */
        visibleSeries.forEach(function (s) {
            var pts = s.points || [];
            if (!pts.length) return;

            ctx.beginPath();
            for (var si = 0; si < pts.length; si++) {
                var sx = px(pts, si);
                var sy = yForSeries(s, pts[si].value);
                if (si === 0) {
                    ctx.moveTo(sx, sy);
                } else {
                    /* Step graph: horizontal part must use the SAME scale as the vertical point. */
                    ctx.lineTo(sx, yForSeries(s, pts[si - 1].value));
                    ctx.lineTo(sx, sy);
                }
            }
            ctx.lineTo(pad.left + gw, yForSeries(s, pts[pts.length - 1].value));
            ctx.strokeStyle = s.color;
            ctx.lineWidth = visibleSeries.length === 1 ? 2 : 1.5;
            ctx.stroke();

            /* Fill for single-series only (too noisy with multiple) */
            if (visibleSeries.length === 1) {
                var grad = ctx.createLinearGradient(0, pad.top, 0, pad.top + gh);
                grad.addColorStop(0, hexAlpha(s.color, 0.14));
                grad.addColorStop(1, hexAlpha(s.color, 0.00));
                ctx.lineTo(pad.left + gw, pad.top + gh);
                ctx.lineTo(pad.left, pad.top + gh);
                ctx.closePath();
                ctx.fillStyle = grad;
                ctx.fill();
            }

            /* Glow dot on last value */
            var last = pts[pts.length - 1];
            var lx = pad.left + gw;
            var ly = yForSeries(s, last.value);
            ctx.beginPath();
            ctx.arc(lx, ly, 4, 0, Math.PI * 2);
            ctx.fillStyle = s.color;
            ctx.fill();
            if (visibleSeries.length === 1) {
                ctx.beginPath();
                ctx.arc(lx, ly, 8, 0, Math.PI * 2);
                ctx.strokeStyle = hexAlpha(s.color, 0.35);
                ctx.lineWidth = 1.5;
                ctx.stroke();
                ctx.fillStyle = s.color;
                ctx.font = '600 11px JetBrains Mono, monospace';
                ctx.textAlign = 'right';
                ctx.fillText(formatGraphValue(last.value), lx - 14, ly + 4);
            }
        });

        /* ── Legend (below chart) ── */
        if (legendRows > 0) {
            var legY   = H - legendH + 6;
            var colW   = (W - pad.left - pad.right) / legendCols;
            visibleSeries.forEach(function (s, li) {
                var col = li % legendCols;
                var row = Math.floor(li / legendCols);
                var lx2 = pad.left + col * colW;
                var ly2 = legY + row * 18;
                ctx.beginPath();
                ctx.moveTo(lx2, ly2 + 6);
                ctx.lineTo(lx2 + 16, ly2 + 6);
                ctx.strokeStyle = s.color;
                ctx.lineWidth = 2;
                ctx.stroke();
                ctx.fillStyle = '#8b9dc3';
                ctx.font = '10px IBM Plex Sans, Arial, sans-serif';
                ctx.textAlign = 'left';
                var maxLegW = colW - 26;
                var aliasText = s.alias || s.id;
                if (isMultiScale) aliasText += ' [' + formatGraphValue(s._rawMin) + '–' + formatGraphValue(s._rawMax) + ']';
                ctx.fillText(aliasText, lx2 + 22, ly2 + 10);
            });
        }

        /* ── Save geometry for hover tooltip ── */
        _chartGeo = {
            pad: pad, gw: gw, gh: gh, W: W, H: H,
            minV: minV, maxV: maxV,
            multiScale: isMultiScale,
            series: visibleSeries,
            /* Legacy compat */
            points: visibleSeries.length ? visibleSeries[0].points : [],
            alias:  visibleSeries.length ? visibleSeries[0].alias  : '',
            px: function (index) { return px(visibleSeries.length ? visibleSeries[0].points : [], index); },
            py: pyGlobal,
            yForSeries: function (s, v) { return yForSeries(s, v); },
            baseImage: ctx.getImageData(0, 0, canvas.width, canvas.height)
        };
    }

    /* ── Helper: hex colour + alpha as rgba ── */
    function hexAlpha(hex, alpha) {
        var r = parseInt(hex.slice(1,3),16), g = parseInt(hex.slice(3,5),16), b = parseInt(hex.slice(5,7),16);
        return 'rgba(' + r + ',' + g + ',' + b + ',' + alpha + ')';
    }
    /* ═══════════════════════════════════════════════════════════════
       HOVER TOOLTIP — crosshair + value/time bubble on mousemove
       ═══════════════════════════════════════════════════════════════ */
    function wireChartHover() {
        var tryBind = function () {
            var canvas = el('sipAssetChartCanvas');
            if (!canvas || !sapNodeExists(canvas)) return;
            if (canvas._sapHoverBound) return;

            canvas._sapHoverBound = true;
            _chartHoverBound = true;

            canvas.addEventListener('mousemove', function (e) {
                var _cg = _chartGeo; var _hasPts = _cg && ((_cg.series && _cg.series.some(function(s){return s.points&&s.points.length;})) || (_cg.points && _cg.points.length)); if (!_hasPts || _activeTab !== 'graph') return;
                if (!sapNodeExists(canvas)) return;
                var rect = canvas.getBoundingClientRect();
                var mx = e.clientX - rect.left;
                drawTooltipOverlay(canvas, mx);
            });

            canvas.addEventListener('mouseleave', function () {
                if (!_chartGeo || !_chartGeo.baseImage || _activeTab !== 'graph') return;
                var canvas2 = el('sipAssetChartCanvas');
                if (!canvas2 || !sapNodeExists(canvas2)) return;
                var ctx = canvas2.getContext('2d');
                ctx.putImageData(_chartGeo.baseImage, 0, 0);
            });
        };

        var c = el('sipAssetChartCanvas');
        if (c && sapNodeExists(c)) tryBind();
        else setTimeout(tryBind, 500);
    }

    function drawTooltipOverlay(canvas, mouseX) {
        var g = _chartGeo;
        if (!g || _activeTab !== 'graph' || !canvas || !sapNodeExists(canvas)) return;

        var allSeries = g.series || (g.points && g.points.length ? [{ points: g.points, alias: g.alias || '', color: '#00d4ff' }] : []);
        if (!allSeries.length) return;

        var ctx = canvas.getContext('2d');
        var dpr = window.devicePixelRatio || 1;

        ctx.putImageData(g.baseImage, 0, 0);
        ctx.save();
        ctx.scale(dpr, dpr);

        var pad = g.pad;
        if (mouseX < pad.left || mouseX > pad.left + g.gw) { ctx.restore(); return; }

        /* For each series find the nearest point by x */
        var hits = [];
        allSeries.forEach(function (s) {
            var pts = s.points || [];
            if (!pts.length) return;
            var ratio = (mouseX - pad.left) / g.gw;
            var idx = Math.round(ratio * (pts.length - 1));
            if (idx < 0) idx = 0;
            if (idx >= pts.length) idx = pts.length - 1;
            var ptx = pad.left + (g.gw * idx / Math.max(1, pts.length - 1));
            var pty = (typeof g.yForSeries === 'function') ? g.yForSeries(s, pts[idx].value) : g.py(pts[idx].value);
            hits.push({ s: s, idx: idx, pt: pts[idx], ptx: ptx, pty: pty });
        });

        if (!hits.length) { ctx.restore(); return; }

        /* Vertical crosshair at the first series' x (representative) */
        var refX = hits[0].ptx;
        ctx.strokeStyle = 'rgba(0,212,255,0.35)';
        ctx.lineWidth = 1;
        ctx.setLineDash([4, 3]);
        ctx.beginPath();
        ctx.moveTo(refX, pad.top);
        ctx.lineTo(refX, pad.top + g.gh);
        ctx.stroke();
        ctx.setLineDash([]);

        /* Draw a dot per series */
        hits.forEach(function (h) {
            ctx.beginPath();
            ctx.arc(h.ptx, h.pty, 4, 0, Math.PI * 2);
            ctx.fillStyle = h.s.color;
            ctx.fill();
        });

        /* ── Tooltip box: list all series values ── */
        var timeText = hits[0].pt.time || '';
        var lines = hits.map(function (h) {
            return { text: (h.s.alias || '') + ': ' + formatGraphValue(h.pt.value), color: h.s.color };
        });

        ctx.font = '600 11px JetBrains Mono, monospace';
        var maxTW = 0;
        lines.forEach(function (l) { var tw = ctx.measureText(l.text).width; if (tw > maxTW) maxTW = tw; });
        ctx.font = '10px JetBrains Mono, monospace';
        var tw2 = ctx.measureText(timeText).width;
        maxTW = Math.max(maxTW, tw2);

        var boxW = maxTW + 20;
        var boxH = lines.length * 16 + 22;
        var bx   = refX + 14;
        var by   = hits[0].pty - boxH - 8;
        if (bx + boxW > pad.left + g.gw) bx = refX - boxW - 14;
        if (by < pad.top) by = hits[0].pty + 12;

        ctx.fillStyle = 'rgba(15,22,41,0.92)';
        ctx.strokeStyle = 'rgba(0,212,255,0.3)';
        ctx.lineWidth = 1;
        roundRect(ctx, bx, by, boxW, boxH, 6);

        /* Time row */
        ctx.fillStyle = '#8b9dc3';
        ctx.font = '10px JetBrains Mono, monospace';
        ctx.textAlign = 'left';
        ctx.fillText(timeText, bx + 10, by + 14);

        /* Series rows */
        lines.forEach(function (l, li) {
            ctx.fillStyle = l.color;
            ctx.font = '600 11px JetBrains Mono, monospace';
            ctx.fillText(l.text, bx + 10, by + 14 + (li + 1) * 16);
        });

        ctx.restore();
    }
    function roundRect(ctx, x, y, w, h, r) {
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + w - r, y);
        ctx.quadraticCurveTo(x + w, y, x + w, y + r);
        ctx.lineTo(x + w, y + h - r);
        ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
        ctx.lineTo(x + r, y + h);
        ctx.quadraticCurveTo(x, y + h, x, y + h - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
    }

    function formatGraphValue(v) {
        if (Math.abs(v) >= 1000) return Math.round(v).toString();
        if (Math.abs(v) >= 100) return v.toFixed(1);
        return v.toFixed(2);
    }

    /* ═══════════════════════════════════════════════════════════════
       OPEN / CLOSE
       ═══════════════════════════════════════════════════════════════ */
    function setText(id, t) { var e = el(id); if (e) e.textContent = t; }
    function setHtml(id, h) { var e = el(id); if (e) e.innerHTML = h; }
    /* Load GetBulkAssetMetadata for this asset's type if the AliasName maps
       don't cover it yet. Guesses the assetTypeId from the #drpAssetType
       dropdown using the popup's type text (Signal / Track / Point…). */
    /* Load GetBulkAssetMetadata for this asset's type if the AliasName maps
       don't cover it yet. Safe: never fires when a Search already loaded
       metadata (loadBulkAssetMetadata RESETS all global alias maps), and
       tries each (site, type) combination at most once. Called from both
       openPopup and every refreshLive tick, so it self-heals when the
       #drpAssetType options load late. */
    var _sapMetaLoading = false;
    var _sapMetaTried = {};   // siteId_typeId → true

    function sapEnsureAliasMetadata() {
        if (_sapMetaLoading) return;
        if (typeof window.loadBulkAssetMetadata !== 'function') return;

        // A user-initiated Search already populated the maps — do NOT
        // reload (the loader wipes every global map on each call).
        if (window.bulkMetadataLoaded) return;

        var siteId = _siteId || (el('drpSite') ? el('drpSite').value : '');
        if (!siteId || siteId === '0') return;

        var aid = String(_assetId || '').trim();
        var map = window.userAssetSimpleMap || {};
        var dl = window.userAssetDataloggerMap || {};
        if (aid) {
            for (var k in map) { if (map.hasOwnProperty(k) && k.indexOf(aid + '_') === 0) return; }
            for (var k2 in dl) { if (dl.hasOwnProperty(k2) && k2.indexOf(aid + '_') === 0) return; }
        }

        // Confident type match from the #drpAssetType dropdown only.
        var dd = document.getElementById('drpAssetType');
        if (!dd || !dd.options || !dd.options.length) return;

        var want = String(_assetType || '').toUpperCase();
        var typeId = '';
        for (var i = 0; i < dd.options.length; i++) {
            var v = dd.options[i].value;
            if (!v || v === '0') continue;
            var t = String(dd.options[i].text || '').toUpperCase();
            if ((want.indexOf('SIGNAL') >= 0 && t.indexOf('SIGNAL') >= 0) ||
                (want.indexOf('TRACK') >= 0 && t.indexOf('TRACK') >= 0) ||
                (want.indexOf('POINT') >= 0 && t.indexOf('POINT') >= 0) ||
                (want && (want === t || want.indexOf(t) >= 0))) {
                typeId = v;
                break;
            }
        }
        if (!typeId) return;

        var tk = siteId + '_' + typeId;
        if (_sapMetaTried[tk]) return;
        _sapMetaTried[tk] = true;
        _sapMetaLoading = true;

        sapLog('Alias metadata not loaded — fetching GetBulkAssetMetadata (site ' +
            siteId + ', type ' + typeId + ') for "' + (_assetName || aid || '?') + '"…');
        try {
            window.loadBulkAssetMetadata(siteId, typeId, function () {
                _sapMetaLoading = false;
                sapSafeRefreshLive('alias-metadata-loaded');
            });
        } catch (e) { _sapMetaLoading = false; }
    }
    function openPopup(cellOrCtx, sourceInfo) {
        ensureReady();

        var c = cellOrCtx || {};
        var s = sourceInfo || {};

        function readPath(obj, path) {
            if (!obj || !path) return '';
            var parts = String(path).split('.');
            var cur = obj;

            for (var i = 0; i < parts.length; i++) {
                if (cur == null) return '';
                cur = cur[parts[i]];
            }

            return cur == null ? '' : cur;
        }

        function readCtx(obj, names) {
            if (!obj) return '';

            for (var i = 0; i < names.length; i++) {
                var n = names[i];

                var direct = readPath(obj, n);
                if (direct !== '') return direct;

                try {
                    if (typeof obj.get === 'function') {
                        var gv = obj.get(n);
                        if (gv !== undefined && gv !== null && gv !== '') return gv;
                    }
                } catch (e1) { }

                try {
                    if (typeof obj.prop === 'function') {
                        var pv = obj.prop(n);
                        if (pv !== undefined && pv !== null && pv !== '') return pv;
                    }
                } catch (e2) { }

                try {
                    if (typeof obj.attr === 'function') {
                        var av = obj.attr(n);
                        if (av !== undefined && av !== null && av !== '') return av;
                    }
                } catch (e3) { }
            }

            return '';
        }

        var pickedAssetId = first(
            readCtx(s, ['assetId', 'AssetId', 'id', 'Id']),
            readCtx(c, ['assetId', 'AssetId', 'attrs.assetId', 'attrs.AssetId', 'attributes.assetId', 'attributes.AssetId', 'data.assetId', 'data.AssetId'])
        );

        var pickedAssetName = first(
            readCtx(s, ['assetName', 'AssetName', 'name', 'Name']),
            readCtx(c, [
                'assetName', 'AssetName', 'name', 'Name',
                'attrs.label.text',
                'attributes.attrs.label.text',
                'label',
                'Label'
            ])
        );

        var pickedType = first(
            readCtx(s, ['stencilType', 'StencilType', 'assetType', 'AssetType', 'type', 'Type']),
            readCtx(c, ['stencilType', 'StencilType', 'assetType', 'AssetType', 'type', 'Type'])
        );

        var pickedSiteId = first(
            readCtx(s, ['siteId', 'SiteId']),
            readCtx(c, ['siteId', 'SiteId', 'attrs.siteId', 'attrs.SiteId', 'attributes.siteId', 'attributes.SiteId'])
        );

        _assetId = pickedAssetId || '';
        _assetName = pickedAssetName || '';
        _assetType = pickedType || '';
        _siteId = pickedSiteId || '';
        _alertIds = normalizeAlertIds(first(readCtx(c, ['alertIds', 'AlertIds']), readCtx(s, ['alertIds', 'AlertIds']), ''));

        var bulkMeta = (_assetId && window.bulkAssetMap) ? window.bulkAssetMap[String(_assetId)] : null;

        if (bulkMeta) {
            if (!_assetName || _assetName === readCtx(c, ['id']) || _assetName === readCtx(c, ['attributes.id'])) {
                _assetName = bulkMeta.Name || bulkMeta.AssetName || _assetName;
            }

            if (!_siteId && bulkMeta.SiteId) _siteId = bulkMeta.SiteId;
            if (!_assetType && bulkMeta.AssetTypeName) _assetType = bulkMeta.AssetTypeName;
            if (!_assetType && bulkMeta.AssetTypeId) _assetType = 'Asset Type ' + bulkMeta.AssetTypeId;
        }

        if (!_assetName) _assetName = first(readCtx(c, ['id', 'attributes.id']), 'Selected Asset');

        setHtml('sipAssetAnalyticsBody', '<div class="sap-empty-state">Click Apply or open this tab to view alert analytics.</div>');

        if (!_siteId) {
            var h = el('hdnSiteId') || el('drpSite');
            if (h) _siteId = h.value || '';
        }

        var fnd = findLiveAsset();
        if (fnd) {
            _assetId = fnd.id || _assetId;

            if (fnd.d) {
                if (!_assetName || _assetName === 'Selected Asset') {
                    _assetName = fnd.d.AssetName || fnd.d.assetName || fnd.d.Name || _assetName;
                }
                if (!_siteId) _siteId = fnd.d.SiteId || fnd.d.siteId || _siteId;
                if (!_assetType) _assetType = fnd.d.AssetTypeName || fnd.d.assetTypeName || _assetType;
            }
        }

        sapLog('Popup opened — cell label: "' + (pickedAssetName || '—') +
            '", resolved asset: "' + _assetName + '" (assetId ' + (_assetId || '—') +
            ', type ' + (_assetType || '—') + ', site ' + (_siteId || '—') + ')' +
            (fnd ? ' via ' + (fnd.via || 'unknown source') : ' — NO live/metadata source matched yet'));

        _lastLiveSignature = '';

        var iconEl = el('sipAssetIcon'); if (iconEl) iconEl.innerHTML = iconFor(_assetType);
        setText('sipAssetPopupAssetName', _assetName);

        var loc = '';
        var ds = document.getElementById('drpSite');
        if (ds && ds.selectedIndex >= 0) loc = ds.options[ds.selectedIndex].text;

        setText('sipAssetPopupAssetLocation', loc || ('Site ' + (_siteId || '\u2014')));
        setText('sipAssetTelemetryTitle', 'Live Telemetry \u2014 ' + _assetName);
        setText('sipAssetFooterType', 'Type: ' + (_assetType || '\u2014'));
        setText('sipAssetMaintSub', 'Put ' + _assetName + ' into maintenance mode to suppress alerts during scheduled work.');
        setHtml('sipAssetAlarmBannerText', esc(_assetName) + ' alarm summary<span>Live alarm binding can be connected to your alarm endpoint.</span>');

        var overlay = el('sipAssetPopupOverlay');
        if (!overlay || !sapNodeExists(overlay)) {
            _ready = false;
            ensureReady();
            overlay = el('sipAssetPopupOverlay');
        }

        if (!overlay) return;

        _isPopupOpen = true;
        overlay.classList.add('sap-show');
        overlay.removeAttribute('inert');
        overlay.setAttribute('aria-hidden', 'false');

        // Ensure AliasName metadata (GetBulkAssetMetadata) is loaded even if
        // the user never clicked Search — otherwise rows would only be able
        // to show raw WebSocket names, which we no longer display.
        sapEnsureAliasMetadata();

        activateTab('live');

        clearInterval(_refreshTimer);
        _refreshTimer = setInterval(function () {
            if (_isPopupOpen && sapIsOverlayVisible()) sapSafeRefreshLive('interval');
        }, 3000);
    }

    function closePopup() {
        _isPopupOpen = false;
        _activeTab = 'live';
        _graphRequestNo++;
        _eventReqNo++;
        _alarmReqNo++;

        var ov = el('sipAssetPopupOverlay');
        if (ov) { ov.classList.remove('sap-show'); ov.setAttribute('inert', ''); ov.setAttribute('aria-hidden', 'true'); }
        var mm = el('sipAssetMaintModal'); if (mm) mm.classList.remove('sap-show');
        clearInterval(_refreshTimer);
        clearInterval(_graphRefreshTimer); _graphRefreshTimer = null;
        _chartGeo = null;

        var graphList = el('sipAttrList');
        if (graphList) {
            graphList.removeAttribute('data-cb-sig');
            graphList.innerHTML = '<div style="padding:10px 12px;font-size:11px;color:var(--sap-text-muted);">Loading…</div>';
        }

        _graphPoints = []; _graphAttrKey = ''; _graphAlias = ''; _graphAttrId = '';
        _graphSeriesData = {}; _graphSelectedIds = []; _graphAllAttrs = [];
        _lastLiveSignature = '';
        _assetId = null; _assetName = '';
    }

    /* ═══════════════════════════════════════════════════════════════
       REGISTER — the function sip-telemetry.js is looking for
       ═══════════════════════════════════════════════════════════════ */
    window.OpenSipAssetPopupFromCell = function (cellOrCtx, sourceInfo) {
        openPopup(cellOrCtx, sourceInfo);
        return true;
    };
    window.SipAssetPopupLive = { open: openPopup, close: closePopup, refresh: function () { sapSafeRefreshLive('manual'); } };
    console.log('[sip-popup-live] window.OpenSipAssetPopupFromCell registered (v2).');
})();
