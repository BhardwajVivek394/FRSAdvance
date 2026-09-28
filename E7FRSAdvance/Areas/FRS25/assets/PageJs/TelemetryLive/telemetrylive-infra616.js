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
