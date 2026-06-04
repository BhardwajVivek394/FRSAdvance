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
    var _assetId = null;
    var _assetName = '';
    var _assetType = '';
    var _siteId = '';

    /* ── tiny helpers ─────────────────────────────────────────────── */
    function el(id) { return document.getElementById(id); }
    function esc(v) { var d = document.createElement('div'); d.textContent = (v == null ? '' : v); return d.innerHTML; }
    function first() { for (var i = 0; i < arguments.length; i++) { var v = arguments[i]; if (v !== undefined && v !== null && v !== '') return v; } return ''; }
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
            "#sipAssetPopupOverlay{--sap-bg-panel:#0f1629;--sap-bg-card:#141b2f;--sap-bg-row-alt:#111827;--sap-bg-row-hover:#1a2340;--sap-bg-header:#0c1220;--sap-border:#1e2a45;--sap-border-glow:#2a3a5c;--sap-text-primary:#e2e8f0;--sap-text-secondary:#8b9dc3;--sap-text-muted:#5a6a8a;--sap-accent-cyan:#00d4ff;--sap-accent-green:#22c55e;--sap-accent-yellow:#f59e0b;--sap-accent-orange:#f97316;--sap-accent-red:#ef4444;--sap-accent-blue:#3b82f6;--sap-gradient-header:linear-gradient(135deg,#1a2744 0%,#0f1629 100%);--sap-shadow-card:0 4px 24px rgba(0,0,0,0.4);--sap-radius:8px;--sap-radius-sm:4px;--sap-radius-lg:12px;position:fixed;inset:0;z-index:5000;display:none;align-items:center;justify-content:center;padding:20px;background:rgba(5,8,18,0.72);backdrop-filter:blur(6px);font-family:'IBM Plex Sans','Plus Jakarta Sans',sans-serif;color:var(--sap-text-primary);}",
            "#sipAssetPopupOverlay.sap-show{display:flex;}",
            "#sipAssetPopupOverlay *{box-sizing:border-box;margin:0;padding:0;}",
            "#sipAssetPopupOverlay .sap-popup{width:1060px;max-width:97vw;max-height:70vh;background:var(--sap-bg-panel);border:1px solid var(--sap-border);border-radius:var(--sap-radius-lg);box-shadow:var(--sap-shadow-card),0 0 60px rgba(0,212,255,0.06);display:flex;flex-direction:column;overflow:hidden;animation:sapSlideUp .25s cubic-bezier(.16,1,.3,1);}",
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
            "#sipAssetPopupOverlay .sap-tab-content{display:none;flex:1;overflow-y:auto;min-height:0;}",
            "#sipAssetPopupOverlay .sap-tab-content.sap-active{display:flex;flex-direction:column;}",
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
            "#sipAssetPopupOverlay .sap-chart-wrap{flex:1;padding:0 20px 20px;min-height:0;}",
            "#sipAssetPopupOverlay .sap-chart-area{height:100%;min-height:240px;background:var(--sap-bg-card);border:1px solid var(--sap-border);border-radius:var(--sap-radius);position:relative;overflow:hidden;}",
            "#sipAssetPopupOverlay .sap-chart-canvas{width:100%;height:100%;}",
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
            "#sipAssetPopupOverlay .sap-footer-meta{font-size:11px;color:var(--sap-text-muted);display:flex;gap:16px;flex-wrap:wrap;}",
            "#sipAssetPopupOverlay .sap-footer-actions{display:flex;gap:8px;}",
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
            "@media(max-width:760px){#sipAssetPopupOverlay{padding:10px;}#sipAssetPopupOverlay .sap-popup{max-height:88vh;}#sipAssetPopupOverlay .sap-telemetry-grid{grid-template-columns:1fr;}#sipAssetPopupOverlay .sap-telemetry-col:first-child{border-right:none;}#sipAssetPopupOverlay .sap-tabs-bar{overflow-x:auto;padding:0 10px;}#sipAssetPopupOverlay .sap-popup-footer{align-items:flex-start;flex-direction:column;gap:10px;}}"
        ].join('\n');
        document.head.appendChild(s);
    }

    /* ═══════════════════════════════════════════════════════════════
       HTML INJECTION — exact copy of Index.cshtml lines 1326–1460
       Only injected if #sipAssetPopupOverlay doesn't exist yet.
       ═══════════════════════════════════════════════════════════════ */
    function injectHTMLIfNeeded() {
        if (el('sipAssetPopupOverlay')) return;

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
            '<div class="sap-tab-content" id="sap-tab-graph"><div class="sap-graph-bar"><select class="sap-graph-select" id="sipAssetParamSelect"><option>Live Value</option></select><div class="sap-filter-group"><span class="sap-filter-label">Date</span><input type="date" class="sap-date-input" id="sipAssetGraphDate" value="' + td + '"></div><div class="sap-graph-range-btns"><button type="button" class="sap-range-btn" data-sap-range="1h">1H</button><button type="button" class="sap-range-btn sap-active" data-sap-range="6h">6H</button><button type="button" class="sap-range-btn" data-sap-range="12h">12H</button><button type="button" class="sap-range-btn" data-sap-range="24h">24H</button></div></div><div class="sap-chart-wrap"><div class="sap-chart-area"><canvas class="sap-chart-canvas" id="sipAssetChartCanvas"></canvas></div></div></div>' +
            /* EVENTLOG */
            '<div class="sap-tab-content" id="sap-tab-eventlog"><div class="sap-event-bar"><div class="sap-filter-group"><span class="sap-filter-label">From</span><input type="date" class="sap-date-input" id="sipAssetEventFrom" value="' + td + '"></div><div class="sap-filter-group"><span class="sap-filter-label">To</span><input type="date" class="sap-date-input" id="sipAssetEventTo" value="' + td + '"></div><button type="button" class="sap-filter-apply">Apply</button><button type="button" class="sap-filter-clear">\u2715 Clear</button></div><div class="sap-event-log" id="sipAssetEventLog"><div class="sap-empty-state">Select an asset to view events.</div></div></div>' +
            /* ALARM */
            '<div class="sap-tab-content" id="sap-tab-alarm"><div class="sap-panel-body"><div class="sap-alarm-banner"><div class="sap-alarm-banner-icon">' + ALARM_TRI + '</div><div class="sap-alarm-banner-text" id="sipAssetAlarmBannerText">Alarm summary<span>Live alarm binding can be connected to your alarm endpoint.</span></div></div><table class="sap-alarm-table"><thead><tr><th>Alarm Code</th><th>Raised At</th><th>Duration</th><th>Severity</th><th>Status</th></tr></thead><tbody id="sipAssetAlarmRows"><tr><td colspan="5" style="color:var(--sap-text-muted)">No alarms loaded yet.</td></tr></tbody></table></div></div>' +
            /* FOOTER */
            '<div class="sap-popup-footer"><div class="sap-footer-meta"><span id="sipAssetLastSync">Last sync: \u2014</span><span id="sipAssetFooterType">Type: \u2014</span><span>FRS: 25</span></div><div class="sap-footer-actions"><button type="button" class="sap-btn">' + DL + ' Export</button><button type="button" class="sap-btn sap-btn-primary">' + RPT + ' SIP Report</button></div></div>' +
            '</div>' +
            /* MAINTENANCE MODAL */
            '<div class="sap-maint-modal-bg" id="sipAssetMaintModal"><div class="sap-maint-modal"><h3><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z"/></svg>Create Maintenance Mode</h3><div class="sap-sub" id="sipAssetMaintSub">Put selected asset into maintenance mode.</div><div class="sap-maint-row"><div class="sap-maint-field"><label>Start</label><input type="datetime-local" id="sipAssetMaintStart"></div><div class="sap-maint-field"><label>End</label><input type="datetime-local" id="sipAssetMaintEnd"></div></div><div class="sap-maint-field"><label>Reason</label><select><option>Scheduled Maintenance</option><option>Emergency Repair</option><option>Inspection</option><option>Component Replacement</option><option>Other</option></select></div><div class="sap-maint-field"><label>Maintainer</label><input type="text" placeholder="Name or employee ID"></div><div class="sap-maint-field"><label>Remarks</label><textarea placeholder="Additional notes..."></textarea></div><div class="sap-maint-actions"><button type="button" class="sap-m-btn" id="sipAssetMaintCancel">Cancel</button><button type="button" class="sap-m-btn sap-primary" id="sipAssetMaintActivate">Activate</button></div></div></div>';

        document.body.appendChild(div);
    }

    /* ═══════════════════════════════════════════════════════════════
       WIRE UI EVENTS — tabs, close, maintenance modal, chart
       ═══════════════════════════════════════════════════════════════ */
    function wireEvents() {
        var overlay = el('sipAssetPopupOverlay');
        if (!overlay) return;

        el('sipAssetPopupClose').addEventListener('click', closePopup);
        overlay.addEventListener('click', function (e) { if (e.target === overlay) closePopup(); });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && overlay.classList.contains('sap-show')) closePopup();
        });

        overlay.querySelectorAll('.sap-tab').forEach(function (t) {
            t.addEventListener('click', function () { activateTab(t.getAttribute('data-sap-tab')); });
        });

        overlay.querySelectorAll('.sap-range-btn').forEach(function (b) {
            b.addEventListener('click', function () {
                overlay.querySelectorAll('.sap-range-btn').forEach(function (x) { x.classList.remove('sap-active'); });
                b.classList.add('sap-active'); drawChart();
            });
        });
        var pSel = el('sipAssetParamSelect');
        if (pSel) pSel.addEventListener('change', drawChart);

        var mb = el('sipAssetMaintBtn');
        if (mb) mb.addEventListener('click', function () { el('sipAssetMaintModal').classList.add('sap-show'); });
        var mc = el('sipAssetMaintCancel');
        if (mc) mc.addEventListener('click', function () { el('sipAssetMaintModal').classList.remove('sap-show'); });
        var ma = el('sipAssetMaintActivate');
        if (ma) ma.addEventListener('click', function () { el('sipAssetMaintModal').classList.remove('sap-show'); mb.classList.add('sap-active-mode'); });
        var mm = el('sipAssetMaintModal');
        if (mm) mm.addEventListener('click', function (e) { if (e.target === mm) mm.classList.remove('sap-show'); });
    }

    /* ═══════════════════════════════════════════════════════════════
       INIT — ensure everything is ready (idempotent)
       ═══════════════════════════════════════════════════════════════ */
    function ensureReady() {
        if (_ready) return;
        if (!document.querySelector('link[href*="IBM+Plex+Sans"]')) {
            var lk = document.createElement('link'); lk.rel = 'stylesheet';
            lk.href = 'https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@300;400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap';
            document.head.appendChild(lk);
        }
        injectCSSIfNeeded();
        injectHTMLIfNeeded();
        wireEvents();
        _ready = true;
    }

    /* ── tabs ─────────────────────────────────────────────────── */
    function activateTab(name) {
        var ov = el('sipAssetPopupOverlay');
        ov.querySelectorAll('.sap-tab').forEach(function (t) { t.classList.toggle('sap-active', t.getAttribute('data-sap-tab') === name); });
        ov.querySelectorAll('.sap-tab-content').forEach(function (c) { c.classList.toggle('sap-active', c.id === 'sap-tab-' + name); });
        if (name === 'graph') setTimeout(drawChart, 80);
    }

    /* ═══════════════════════════════════════════════════════════════
       LIVE TELEMETRY — read from wsLiveData (WebSocket)
       ═══════════════════════════════════════════════════════════════ */
    function findLiveAsset() {
        var ld = window.wsLiveData;
        if (!ld) return null;
        if (_assetId && ld[_assetId]) return { id: _assetId, d: ld[_assetId] };
        for (var id in ld) {
            if (ld.hasOwnProperty(id) && ld[id].AssetName === _assetName) return { id: id, d: ld[id] };
        }
        if (window.SipTelemetry && window.SipTelemetry._state && window.SipTelemetry._state.assetValues) {
            var sv = window.SipTelemetry._state.assetValues[_assetName];
            if (sv) return { id: '', d: { attrs: sv, AssetName: _assetName } };
        }
        return null;
    }

    function renderTelemRow(label, raw) {
        var display = (raw !== null && raw !== undefined && raw !== '') ? String(raw) : '--';
        if (display === 'Ok' || display === 'ok' || display === 'true')
            return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) + '</span><span class="sap-value-ok">Ok</span></div>';
        var num = parseFloat(display);
        if (!isNaN(num)) display = num % 1 === 0 ? String(num) : num.toFixed(2);
        var cls = 'sap-telem-value';
        var stale = (window.wsStaleAttrs && window.wsStaleAttrs[_assetId] && window.wsStaleAttrs[_assetId][label]);
        if (stale) cls += ' sap-value-stale';
        return '<div class="sap-telem-row"><span class="sap-telem-label">' + esc(label) + '</span><span class="' + cls + '">' + esc(display) + '</span></div>';
    }

    function refreshLive() {
        var f = findLiveAsset();
        var attrs = {};
        if (f && f.d) {
            _assetId = f.id || _assetId;
            var ra = f.d.attrs || f.d;
            for (var k in ra) {
                if (!ra.hasOwnProperty(k)) continue;
                if (/^(AssetName|AssetTypeId|SiteId|lastUpdated|dlRelays|__type)$/.test(k)) continue;
                var v = ra[k];
                attrs[k] = (v && typeof v === 'object' && 'Value' in v) ? v.Value : v;
            }
        }
        var keys = Object.keys(attrs);
        if (window.wsAttributeNames && window.wsAttributeNames.length) {
            var ord = [];
            window.wsAttributeNames.forEach(function (n) { if (n in attrs) ord.push(n); });
            keys.forEach(function (k) { if (ord.indexOf(k) < 0) ord.push(k); });
            keys = ord;
        }
        var grid = el('sipAssetTelemetryGrid');
        if (!grid) return;
        if (!keys.length) { grid.innerHTML = '<div class="sap-empty-state">Waiting for WebSocket data\u2026</div>'; return; }
        var half = Math.ceil(keys.length / 2);
        grid.innerHTML =
            '<div class="sap-telemetry-col">' + keys.slice(0, half).map(function (k) { return renderTelemRow(k, attrs[k]); }).join('') + '</div>' +
            '<div class="sap-telemetry-col">' + keys.slice(half).map(function (k) { return renderTelemRow(k, attrs[k]); }).join('') + '</div>';

        var sel = el('sipAssetParamSelect');
        if (sel && sel.options.length !== keys.length) sel.innerHTML = keys.map(function (k) { return '<option>' + esc(k) + '</option>'; }).join('');
        var ts = el('sipAssetLastSync');
        if (ts) ts.textContent = 'Last sync: ' + fmtNow();
    }

    /* ═══════════════════════════════════════════════════════════════
       SERVER FETCHES — events, alarms, analytics
       ═══════════════════════════════════════════════════════════════ */
    /* ═══════════════════════════════════════════════════════════════
       SERVER ENDPOINTS — placeholder stubs, wire one-by-one later.
       Uncomment & point to real URLs when the API is ready.
       ─────────────────────────────────────────────────────────────
       function post(url, data, cb) { ... }
       function fetchEvents()   { post('/Sip/GetAssetEvents',   ...) }
       function fetchAlarms()   { post('/Sip/GetAssetAlarms',   ...) }
       function fetchAnalytics(){ post('/Sip/GetAssetAlertAnalytics', ...) }
       ═══════════════════════════════════════════════════════════════ */

    /* ═══════════════════════════════════════════════════════════════
       CHART
       ═══════════════════════════════════════════════════════════════ */
    function drawChart() {
        var canvas = el('sipAssetChartCanvas'); if (!canvas) return;
        var ctx = canvas.getContext('2d'), dpr = window.devicePixelRatio || 1;
        var rect = canvas.parentElement.getBoundingClientRect();
        canvas.width = rect.width * dpr; canvas.height = rect.height * dpr;
        canvas.style.width = rect.width + 'px'; canvas.style.height = rect.height + 'px';
        ctx.scale(dpr, dpr);
        var W = rect.width, H = rect.height, pad = { top: 24, right: 20, bottom: 36, left: 52 };
        var gw = W - pad.left - pad.right, gh = H - pad.top - pad.bottom;
        ctx.clearRect(0, 0, W, H);
        var sel = el('sipAssetParamSelect'), param = sel ? sel.value : '', bv = 100;
        var f = findLiveAsset();
        if (f && f.d && f.d.attrs && f.d.attrs[param]) {
            var rv = f.d.attrs[param]; var pv = (rv && typeof rv === 'object') ? rv.Value : rv;
            if (pv !== null && !isNaN(parseFloat(pv))) bv = parseFloat(pv);
        }
        var pts = 96, data = [];
        for (var i = 0; i < pts; i++) data.push(bv + Math.sin(i * 0.15) * bv * 0.04 + (Math.random() - 0.5) * bv * 0.02);
        var minV = Math.min.apply(null, data) - Math.abs(bv) * 0.02, maxV = Math.max.apply(null, data) + Math.abs(bv) * 0.02;
        ctx.strokeStyle = 'rgba(30,42,69,0.6)'; ctx.lineWidth = 1;
        for (var g = 0; g <= 4; g++) { var gy = pad.top + (gh / 4) * g; ctx.beginPath(); ctx.moveTo(pad.left, gy); ctx.lineTo(pad.left + gw, gy); ctx.stroke(); ctx.fillStyle = '#5a6a8a'; ctx.font = '10px JetBrains Mono,monospace'; ctx.textAlign = 'right'; ctx.fillText((maxV - (maxV - minV) * (g / 4)).toFixed(1), pad.left - 8, gy + 3); }
        var hrs = ['00:00', '04:00', '08:00', '12:00', '16:00', '20:00', '24:00'];
        ctx.fillStyle = '#5a6a8a'; ctx.font = '10px JetBrains Mono,monospace'; ctx.textAlign = 'center';
        hrs.forEach(function (h, hi) { ctx.fillText(h, pad.left + (gw / 6) * hi, H - pad.bottom + 18); });
        var grad = ctx.createLinearGradient(0, pad.top, 0, pad.top + gh);
        grad.addColorStop(0, 'rgba(0,212,255,0.12)'); grad.addColorStop(1, 'rgba(0,212,255,0)');
        ctx.beginPath();
        data.forEach(function (v, di) { var dx = pad.left + (gw / (pts - 1)) * di, dy = pad.top + gh - ((v - minV) / (maxV - minV)) * gh; di === 0 ? ctx.moveTo(dx, dy) : ctx.lineTo(dx, dy); });
        ctx.lineTo(pad.left + gw, pad.top + gh); ctx.lineTo(pad.left, pad.top + gh); ctx.closePath(); ctx.fillStyle = grad; ctx.fill();
        ctx.beginPath();
        data.forEach(function (v, di) { var dx = pad.left + (gw / (pts - 1)) * di, dy = pad.top + gh - ((v - minV) / (maxV - minV)) * gh; di === 0 ? ctx.moveTo(dx, dy) : ctx.lineTo(dx, dy); });
        ctx.strokeStyle = '#00d4ff'; ctx.lineWidth = 2; ctx.stroke();
        var lx = pad.left + gw, ly = pad.top + gh - ((data[pts - 1] - minV) / (maxV - minV)) * gh;
        ctx.beginPath(); ctx.arc(lx, ly, 4, 0, Math.PI * 2); ctx.fillStyle = '#00d4ff'; ctx.fill();
        ctx.beginPath(); ctx.arc(lx, ly, 8, 0, Math.PI * 2); ctx.strokeStyle = 'rgba(0,212,255,0.3)'; ctx.lineWidth = 1; ctx.stroke();
    }

    /* ═══════════════════════════════════════════════════════════════
       OPEN / CLOSE
       ═══════════════════════════════════════════════════════════════ */
    function setText(id, t) { var e = el(id); if (e) e.textContent = t; }
    function setHtml(id, h) { var e = el(id); if (e) e.innerHTML = h; }

    function openPopup(cellOrCtx, sourceInfo) {
        ensureReady();
        var c = cellOrCtx || {}, s = sourceInfo || {};
        _assetName = first(c.assetName, c.AssetName, s.assetName, s.AssetName, c.attrs && c.attrs.label && c.attrs.label.text, c.id, 'Selected Asset');
        _assetType = first(c.stencilType, c.StencilType, c.type, c.Type, s.stencilType, s.type, '');
        _assetId = first(c.assetId, c.AssetId, s.assetId, s.AssetId, '');
        _siteId = first(c.siteId, c.SiteId, s.siteId, s.SiteId, '');
        if (!_siteId) { var h = el('hdnSiteId') || el('drpSite'); if (h) _siteId = h.value || ''; }
        if (!_assetId) { var fnd = findLiveAsset(); if (fnd) _assetId = fnd.id; }

        var iconEl = el('sipAssetIcon'); if (iconEl) iconEl.innerHTML = iconFor(_assetType);
        setText('sipAssetPopupAssetName', _assetName);
        var loc = ''; var ds = document.getElementById('drpSite');
        if (ds && ds.selectedIndex >= 0) loc = ds.options[ds.selectedIndex].text;
        setText('sipAssetPopupAssetLocation', loc || ('Site ' + (_siteId || '\u2014')));
        setText('sipAssetTelemetryTitle', 'Live Telemetry \u2014 ' + _assetName);
        setText('sipAssetFooterType', 'Type: ' + (_assetType || '\u2014'));
        setText('sipAssetMaintSub', 'Put ' + _assetName + ' into maintenance mode to suppress alerts during scheduled work.');
        setHtml('sipAssetAlarmBannerText', esc(_assetName) + ' alarm summary<span>Live alarm binding can be connected to your alarm endpoint.</span>');

        refreshLive();
        activateTab('live');
        el('sipAssetPopupOverlay').classList.add('sap-show');
        el('sipAssetPopupOverlay').setAttribute('aria-hidden', 'false');

        /* Server endpoints (events, alarms, analytics) not wired yet.
           Will be added one-by-one once the API is ready. */

        clearInterval(_refreshTimer);
        _refreshTimer = setInterval(refreshLive, 3000);
    }

    function closePopup() {
        var ov = el('sipAssetPopupOverlay');
        if (ov) { ov.classList.remove('sap-show'); ov.setAttribute('aria-hidden', 'true'); }
        var mm = el('sipAssetMaintModal'); if (mm) mm.classList.remove('sap-show');
        clearInterval(_refreshTimer); _assetId = null; _assetName = '';
    }

    /* ═══════════════════════════════════════════════════════════════
       REGISTER — the function sip-telemetry.js is looking for
       ═══════════════════════════════════════════════════════════════ */
    window.OpenSipAssetPopupFromCell = function (cellOrCtx, sourceInfo) {
        openPopup(cellOrCtx, sourceInfo);
        return true;
    };
    window.SipAssetPopupLive = { open: openPopup, close: closePopup, refresh: refreshLive };
    console.log('[sip-popup-live] window.OpenSipAssetPopupFromCell registered (v2).');
})();
