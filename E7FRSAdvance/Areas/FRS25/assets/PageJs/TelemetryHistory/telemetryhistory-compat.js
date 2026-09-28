/* =============================================================================
 * telemetryhistory-compat.js — helpers the v616 Telemetry History features
 * expect, implemented against FRS Advance's own page.
 *
 * Load as a classic <script> AFTER the inline script in
 * Areas/FRS25/Views/TelemetryHistory/Index.cshtml, and BEFORE the feature
 * modules (-vibration.js, -ipseld.js) that call into these.
 *
 * Why this file exists
 * --------------------
 * The vibration and IPS/ELD renderers were written against the web project
 * (E7MRIV2Web). They call a handful of helpers that FRS Advance's history page
 * never grew. Two of those — the pager helpers — cannot be copied across,
 * because the two forks model server pagination differently:
 *
 *   web project : _dashServerPagerState[assetId]  (keyed by asset id)
 *   FRS Advance : serverSideState[assetIndex]     (parallel to the asset array)
 *
 * So the pager helpers below are reimplementations that read FRS Advance's
 * serverSideState, not ports. Everything else is a faithful port with the web's
 * hardcoded palette swapped for this page's dark-first convention.
 *
 * Nothing here runs on its own; it only defines globals.
 * ========================================================================== */
(function () {
    'use strict';

    if (window.__thCompatApplied) return;
    window.__thCompatApplied = true;

    var W = window;
    var $ = W.jQuery;
    if (typeof $ !== 'function') {
        if (W.console) console.warn('[th-compat] jQuery missing; nothing applied');
        return;
    }

    function pageSizeDefault() {
        return (typeof W.TH_PAGE_SIZE === 'number' && W.TH_PAGE_SIZE > 0) ? W.TH_PAGE_SIZE : 50;
    }

    /* ======================================================================
     * Asset-type gates
     *
     * Deliberately name-based rather than id-based: IPS asset-type ids differ
     * between databases, so a hardcoded id silently disables the view at some
     * sites. ELD keeps its id check because 31 is stable and some sites label
     * the type with a longer name.
     * ==================================================================== */
    function isIpsHistoryAssetType() {
        var name = $.trim($('#drpAssetType option:selected').text() || '').toUpperCase();
        return name === 'IPS' || name.indexOf('IPS ') === 0 || name.indexOf(' IPS') > -1;
    }

    function isEldHistoryAssetType() {
        var id = parseInt($('#drpAssetType').val(), 10) || 0;
        var name = $.trim($('#drpAssetType option:selected').text() || '').toUpperCase();
        return id === 31 || name === 'ELD' ||
            name.indexOf('ELD ') === 0 || name.indexOf(' ELD') > -1;
    }

    function isPmHistoryAssetType() {
        var name = $.trim($('#drpAssetType option:selected').text() || '').toUpperCase();
        return name === 'POINT MACHINE' || name === 'POINT_MACHINE';
    }

    W.isIpsHistoryAssetType = isIpsHistoryAssetType;
    W.isEldHistoryAssetType = isEldHistoryAssetType;
    W.isPmHistoryAssetType = isPmHistoryAssetType;

    /* ======================================================================
     * Attribute naming
     * ==================================================================== */

    // The name the API considers canonical for an attribute, used for column
    // ordering. Falls back through GetUserAssetInfo, then the GetAssetAttributes
    // title map, then whatever the caller already had.
    function getHistoryCanonicalAttrName(assetId, attrId, isDataLogger, fallbackName) {
        if (typeof W.getAssetInfoAttrEntry === 'function') {
            var info = W.getAssetInfoAttrEntry(assetId, attrId, isDataLogger);
            if (info) {
                var canonical = String(info.CanonicalName || '').trim();
                if (canonical) return canonical;
            }
        }

        if (!isDataLogger && W.assetAttributeMap) {
            var attr = W.assetAttributeMap[attrId] || W.assetAttributeMap[String(attrId)];
            if (attr && attr.title) return attr.title;
        }

        return String(fallbackName || '').trim();
    }
    W.getHistoryCanonicalAttrName = getHistoryCanonicalAttrName;

    /* ======================================================================
     * Timestamp cell
     *
     * The web project inks this badge with flat hex (#fef3c7 / #92400e /
     * #fcd34d). This page is dark-first with a body[data-aurora="light"]
     * override, so the badge carries a class and the colours live in
     * telemetryhistory-vibration.css instead of being inlined here.
     * ==================================================================== */
    function formatTimestampWithBadge(date) {
        var s = (typeof W.formatTimestamp === 'function') ? W.formatTimestamp(date) : String(date || '');
        if (date && date._isStartRow) {
            return '<div class="th-ts-stack">' +
                '<span>' + s + '</span>' +
                '<span class="th-last-updated-badge">' +
                '<i class="fas fa-history" aria-hidden="true"></i>Last Updated</span>' +
                '</div>';
        }
        return s;
    }
    W.formatTimestampWithBadge = formatTimestampWithBadge;

    /* ======================================================================
     * Rows-per-page selector
     *
     * FRS Advance offers 10/25/50/100 (TH_PAGE_SIZE_OPTIONS); the web project
     * offers 25/50/100/200/500. Follow this page's own options so the ported
     * views match the selectors already on screen.
     * ==================================================================== */
    function _thPageSizeOptionsHtml(currentSize) {
        var sz = (typeof currentSize === 'number' && currentSize > 0) ? currentSize : pageSizeDefault();
        var opts = W.TH_PAGE_SIZE_OPTIONS || [10, 25, 50, 100];
        var h = '';
        for (var i = 0; i < opts.length; i++) {
            h += '<option value="' + opts[i] + '"' + (opts[i] === sz ? ' selected' : '') + '>' + opts[i] + '</option>';
        }
        return h;
    }
    W._thPageSizeOptionsHtml = _thPageSizeOptionsHtml;

    /* ======================================================================
     * Pager helpers — reimplemented against serverSideState
     *
     * The upstream API is cursor-paginated: page N+1 is reachable only by
     * replaying page N's nextCursor. Numbered page buttons would therefore
     * advertise pages that can never be selected, so these render
     * Previous / current / Next and drive Next purely off hasNextPage plus a
     * cursor actually being held for the next page.
     * ==================================================================== */

    /* ======================================================================
     * _dashServerPagerState — pagination state keyed by ASSET ID
     *
     * The ported IPS/ELD pivot reads this map; FRS Advance keeps the same
     * facts in `serverSideState`, an ARRAY indexed by asset position, and
     * names the current page `page` rather than `currentPage`. The pivot's
     * `typeof _dashServerPagerState !== 'undefined'` guards meant it simply
     * found nothing, so the pivot rendered page 1 with a dead pager.
     *
     * This is a projection, not a second source of truth: the view calls
     * _dashSyncPagerState() after it fills serverSideState, and this rebuilds
     * the map from it. The union's key is the literal comma-joined id, which
     * is exactly what the pivot looks itself up by.
     * ==================================================================== */
    W._dashServerPagerState = W._dashServerPagerState || {};

    function _dashSyncPagerState(states) {
        var map = {};
        (states || []).forEach(function (st) {
            if (!st || st.assetId === undefined || st.assetId === null) return;
            // The export path re-fetches every page from this state alone, so
            // it must carry the search window too. Without startDate/endDate
            // the export fired with undefined dates and the upstream rejected
            // it as an invalid request.
            var _p = W.LAST_SEARCH_PARAMS || {};
            map[String(st.assetId)] = {
                assetId: st.assetId,
                startDate: st.startDate || _p.startDate || '',
                endDate: st.endDate || _p.endDate || '',
                // Both spellings: ported code reads currentPage, this page's
                // own code reads page.
                currentPage: st.page || 1,
                page: st.page || 1,
                pageSize: st.pageSize || pageSizeDefault(),
                totalRecords: st.totalRecords || 0,
                totalPages: st.totalPages ||
                    (st.pageSize ? Math.ceil((st.totalRecords || 0) / st.pageSize) : 0),
                hasNextPage: !!st.hasNextPage,
                nextCursor: st.nextCursor || null,
                cursorByPage: st.cursorByPage || {},
                pageRowCount: st.pageRowCount || 0,
                inFlight: st.inFlight || 0
            };
        });
        W._dashServerPagerState = map;
        return map;
    }
    W._dashSyncPagerState = _dashSyncPagerState;

    // First and last row numbers shown on the current page.
    function _dashShowingRange(st) {
        var size = (st && st.pageSize) || pageSizeDefault();
        // currentPage is the ported spelling, page is this page's own.
        var page = (st && (st.currentPage > 0 ? st.currentPage : st.page) > 0)
            ? (st.currentPage > 0 ? st.currentPage : st.page) : 1;
        var from = ((page - 1) * size) + 1;
        var rows = Math.max(0, (st && st.pageRowCount) || 0);
        if (rows > size) rows = size;
        var to = from + Math.max(0, rows - 1);
        if (to < from) to = from;
        if (st && st.totalRecords && to > st.totalRecords) to = st.totalRecords;
        return { from: from, to: to };
    }
    W._dashShowingRange = _dashShowingRange;

    // Repaint the single shared pager (#paginationUl / #dataInfo) that the
    // pivoted IPS/ELD and vibration views use, as opposed to the per-asset
    // pagers the normal table renders.
    function _dashOverrideLegacyPagerHtml(st) {
        if (!st) return;
        var currentPage = (st.currentPage > 0 ? st.currentPage : st.page) > 0
            ? (st.currentPage > 0 ? st.currentPage : st.page) : 1;
        var totalRecords = st.totalRecords || 0;
        var inFlight = !!st.inFlight;

        var prevPage = currentPage - 1;
        var nextPage = currentPage + 1;
        var prevDisabled = (currentPage <= 1 || inFlight);
        var nextDisabled = (!st.hasNextPage || inFlight ||
            !(st.cursorByPage && st.cursorByPage[nextPage]));

        var h = '';
        h += '<li class="page-item ' + (prevDisabled ? 'disabled' : '') + '">' +
            '<a class="page-link" href="javascript:void(0);"' +
            (prevDisabled ? '' : ' onclick="renderTablePage(' + prevPage + ')"') +
            '><i class="fas fa-chevron-left"></i> Previous</a></li>';

        h += '<li class="page-item active">' +
            '<a class="page-link" style="cursor:default;pointer-events:none;">' + currentPage + '</a></li>';

        h += '<li class="page-item ' + (nextDisabled ? 'disabled' : '') + '">' +
            '<a class="page-link" href="javascript:void(0);"' +
            (nextDisabled ? '' : ' onclick="renderTablePage(' + nextPage + ')"') +
            '>Next <i class="fas fa-chevron-right"></i></a></li>';

        $('#paginationUl').html(h);
        $('#divPagination').show();
        $('#thPageSize').prop('disabled', inFlight);

        var range = _dashShowingRange(st);
        var msg = totalRecords
            ? ('Showing ' + range.from + ' to ' + range.to + ' of ' + totalRecords + ' rows')
            : ('Showing ' + range.from + ' to ' + range.to + (st.hasNextPage ? ' (more available)' : ' rows'));
        $('#dataInfo').html('<span class="text-muted" style="font-size:12px;">' +
            '<i class="fas fa-table text-primary"></i> ' + msg + '</span>');

        var $bar = $('#pageLoadBar');
        if (inFlight) {
            $('#pageLoadBarText').html('<i class="fas fa-download"></i> Loading page ' + inFlight + '…');
            $bar.show();
        } else {
            $bar.hide();
        }
    }
    W._dashOverrideLegacyPagerHtml = _dashOverrideLegacyPagerHtml;

    /* ======================================================================
     * View reset
     *
     * Called before a new Search so a ported view never renders into DOM that
     * still belongs to the previous asset selection.
     * ==================================================================== */
    function clearHistoryView() {
        $('#divTelemetryHistory').html('');
        $('#pageLoadBar').hide();
        $('#divPagination').hide();
        $('#divChartContainer').hide().html('');
        $('#downloadContainer').hide();
        $('#pmViewToggle').hide();
        $('#dataInfo').html('');

        if (W.thCaches) W.thCaches = [];
        if (W.thCurrentPages) W.thCurrentPages = [];
        W.serverSideState = [];
        W.pmDirectionByTs = {};

        // Vibration module state, cleared only when the module is loaded.
        if (typeof W.PM_VIB_BULK_LOADING !== 'undefined') W.PM_VIB_BULK_LOADING = false;
        if (typeof W._vibResetTrackCauseCache === 'function') {
            try { W._vibResetTrackCauseCache(); } catch (e) { }
        }
    }
    W.clearHistoryView = clearHistoryView;

    /* ======================================================================
     * Graph-module helpers
     * ==================================================================== */

    // True when these results carry real table rows, as opposed to the
    // graph's full-range payload (which is flagged isGraphPayload).
    function _thIsRealTablePayload(results) {
        if (!results || !results.length || results.isGraphPayload) {
            return false;
        }
        for (var i = 0; i < results.length; i++) {
            if (results[i]
                && results[i].attributes
                && results[i].attributes.length) {
                return true;
            }
        }
        return false;
    }

    function pmHistoryFindNearestWaveform(
        entries,
        rowTs,
        maxDistanceMs
    ) {
        if (
            !entries ||
            !entries.length ||
            !rowTs
        ) {
            return null;
        }

        var targetMs = rowTs.getTime();

        /*
         * PERFORMANCE: distance filter FIRST, parse LAST.
         *
         * This previously called pmHistoryParseWaveValues() on EVERY entry and
         * then threw all but the nearest away -- measured at 160,000 waveform
         * parses to fill 800 cells, ~21 s per asset, and a single 124.9 s
         * main-thread block across a 6-asset export. Timestamp distance is
         * integer math and needs no parsing, so candidates are ranked by
         * distance first.
         *
         * The original's fallback is preserved exactly: an entry whose parsed
         * waveform yields fewer than 2 values is SKIPPED and the next-nearest
         * is tried, rather than aborting. In practice the first candidate wins,
         * so this parses 1 entry instead of N.
         */
        var candidates = [];

        for (
            var i = 0;
            i < entries.length;
            i++
        ) {
            var entry = entries[i];

            if (!entry || !entry.ts) {
                continue;
            }

            var timestampMs =
                entry.ts.getTime();

            var distanceMs =
                Math.abs(
                    timestampMs -
                    targetMs
                );

            if (distanceMs <= maxDistanceMs) {
                candidates.push({
                    entry: entry,
                    timestampMs: timestampMs,
                    distanceMs: distanceMs
                });
            }
        }

        if (!candidates.length) {
            return null;
        }

        candidates.sort(function (a, b) {
            return a.distanceMs - b.distanceMs;
        });

        for (
            var c = 0;
            c < candidates.length;
            c++
        ) {
            var values =
                pmHistoryParseWaveValues(
                    candidates[c].entry.value
                );

            if (values.length < 2) {
                continue;
            }

            return {
                timestampMs: candidates[c].timestampMs,
                distanceMs: candidates[c].distanceMs,
                values: values
            };
        }

        return null;
    }

    /*
     * The source's getTelemetryHistoryNativeData is a ~2100-line adapter that
     * converts the API's columnar response into the attribute-major shape the
     * renderers expect. FRS Advance already has its own converter for exactly
     * that — _transformDashboardColumnar, which the table view has been using
     * all along — so this maps the source's call signature onto it instead of
     * carrying a second copy of the same logic that could drift from the first.
     */
    function getTelemetryHistoryNativeData(response, assetId, startDate, endDate, source) {
        if (!response) return [];

        // Legacy HistoryValue shape: already attribute-major.
        if ($.isArray(response)) return response;

        var inner = response.Data || response.data || response.Result || response.result;
        if ($.isArray(inner)) return inner;

        var cols = response.columns || response.Columns;
        var rows = response.rows || response.Rows;
        if ($.isArray(cols) && $.isArray(rows) && typeof W._transformDashboardColumnar === 'function') {
            return W._transformDashboardColumnar(cols, rows, assetId);
        }

        return [];
    }

    // Waveform sample decoding, used by pmHistoryFindNearestWaveform above.
    function pmHistoryParseWaveValues(rawValue) {
        if (
            rawValue === undefined ||
            rawValue === null
        ) {
            return [];
        }

        var values = rawValue;

        if (!Array.isArray(values)) {
            var text = $.trim(String(values));

            if (!text) {
                return [];
            }

            if (
                text.charAt(0) === '[' &&
                text.charAt(text.length - 1) === ']'
            ) {
                try {
                    values = JSON.parse(text);
                } catch (ignoreJsonError) {
                    values = text.split(',');
                }
            } else {
                if (text.indexOf(',') === -1) {
                    return [];
                }

                values = text.split(',');
            }
        }

        return values
            .map(function (value) {
                return parseFloat(
                    $.trim(String(value))
                );
            })
            .filter(function (value) {
                return !isNaN(value);
            });
    }

    // Merge one page of attribute-major records into an accumulator. The
    // exports walk every cursor page before writing a file, so each page's
    // attributes have to fold into one set rather than replace the last.
    function _exportMergeAttrs(accum, pageAttrs, assetId) {
        if (!Array.isArray(pageAttrs)) return;
        for (var i = 0; i < pageAttrs.length; i++) {
            var attr = pageAttrs[i];
            if (!attr || attr.AttributeId === undefined || attr.AttributeId === null) continue;
            var dt = 'RDPMS';
            if (attr.Values) {
                for (var k in attr.Values) {
                    if (attr.Values.hasOwnProperty(k) && attr.Values[k] && attr.Values[k].DataType) { dt = attr.Values[k].DataType; break; }
                }
            }
            var aid = (attr.AssetId !== undefined && attr.AssetId !== null) ? attr.AssetId : assetId;
            var key = String(aid) + '_' + String(attr.AttributeId) + '_' + dt;
            var ex = accum[key];
            if (!ex) { accum[key] = attr; continue; }
            if (!ex.Values) ex.Values = {};
            if (!attr.Values) continue;
            var ni = 1;
            for (var ek in ex.Values) { if (ex.Values.hasOwnProperty(ek)) { var n = parseInt(ek, 10); if (!isNaN(n) && n >= ni) ni = n + 1; } }
            for (var nk in attr.Values) {
                if (attr.Values.hasOwnProperty(nk) && attr.Values[nk]) { var e = attr.Values[nk]; e.Id = ni; ex.Values[String(ni)] = e; ni++; }
            }
        }
    }

    // ---- Non-blocking export progress toast ------------------------------
    // Replaces the full-page modal ($('#loader')) during report preparation
    // so the user can keep using the page while the file is assembled in the
    // background. Fixed, corner-anchored, dismissible; updates a live count.
    var _exportToastActive = false;


    /* ======================================================================
     * IPS / ELD union splitter
     *
     * IPS and ELD searches send every selected asset as ONE comma-separated
     * AssetId and get one combined payload back. That arrives as a single
     * result whose assetId is the literal "43431,43432,...", which no
     * per-asset lookup can match. Split it into one entry per real asset,
     * keyed off each attribute's own AssetId. No-op on normal results.
     * ==================================================================== */
    function _ipsExplodeUnionAssetResults(assetResults) {
        if (!Array.isArray(assetResults)) { return assetResults; }
        var _needs = false;
        for (var _i = 0; _i < assetResults.length; _i++) {
            if (assetResults[_i] && String(assetResults[_i].assetId).indexOf(',') > -1) { _needs = true; break; }
        }
        if (!_needs) { return assetResults; }
        var out = [];
        var byId = {};
        assetResults.forEach(function (ar) {
            if (!ar) { return; }
            if (String(ar.assetId).indexOf(',') === -1) { out.push(ar); return; }
            (ar.attributes || []).forEach(function (attr) {
                if (!attr) { return; }
                var rid = (attr.AssetId !== undefined && attr.AssetId !== null &&
                    String(attr.AssetId).indexOf(',') === -1) ? String(attr.AssetId) : null;
                if (rid === null) { return; }
                var e = byId[rid];
                if (!e) {
                    e = {
                        assetId: (typeof attr.AssetId === 'number') ? attr.AssetId : rid,
                        assetName: (typeof assetNameMap !== 'undefined' &&
                            (assetNameMap[rid] || assetNameMap[parseInt(rid, 10)]))
                            ? (assetNameMap[rid] || assetNameMap[parseInt(rid, 10)]) : ('Asset ' + rid),
                        attributes: [], serverPaging: ar.serverPaging === true
                    };
                    byId[rid] = e; out.push(e);
                }
                e.attributes.push(attr);
            });
        });
        return out;
    }
    W._ipsExplodeUnionAssetResults = _ipsExplodeUnionAssetResults;

    W.pmHistoryParseWaveValues = pmHistoryParseWaveValues;
    W._exportMergeAttrs = _exportMergeAttrs;
    W._thIsRealTablePayload = _thIsRealTablePayload;
    W.pmHistoryFindNearestWaveform = pmHistoryFindNearestWaveform;
    W.getTelemetryHistoryNativeData = getTelemetryHistoryNativeData;

    if (W.console) console.log('[th-compat] v616 history helpers applied');
})();
