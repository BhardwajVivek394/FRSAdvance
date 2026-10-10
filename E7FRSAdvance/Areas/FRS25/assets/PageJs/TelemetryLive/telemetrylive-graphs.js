/* =====================================================================
 * TELEMETRY LIVE — IPS GRAPH + POINT MACHINE GRAPH
 *
 * Ported from E7MRIV2Web's telemetry live page and adapted to FRS Advance:
 *   - metadata comes from the bulk-metadata layer (loadBulkAssetMetadata /
 *     getBulkAliasName / getBulkAssetName) instead of the user-asset-info maps
 *   - the chrome is themed with the Aurora --at-* tokens, not the light skin
 *   - entry points are wired to the per-asset overlay (fnGetAssetGraph)
 *     as well as the Graph view type
 *
 * Loaded after telemetrylive.js, so every helper it calls is already defined.
 *
 * Layout:
 *   fnBindIpsGraph()  -> IPS history graph (1..n assets, overlay / stacked)
 *   fnBindPmGraph()   -> Point Machine graph (single asset, A/B end, op events)
 * State lives in window.ipsG and window.pmG.
 * ===================================================================== */

// ---- Small helpers the ported blocks rely on -------------------------
// Negative sensor readings are display artefacts; floor them at zero.
if (typeof window.tlZeroFloor !== 'function') {
    window.tlZeroFloor = function (v) {
        var n = parseFloat(v);
        if (isNaN(n)) return v;
        return (n <= 0) ? 0 : n;
    };
}
var tlZeroFloor = window.tlZeroFloor;

/*
 * Strip the bank number out of an IPS battery attribute name so every bank of
 * one base attribute groups together:
 *   IIPS BATT CHAR-1 110 DC      -> IIPS BATT CHAR 110 DC
 *   IIPS BATT DISCHAR-6 110 DC   -> IIPS BATT DISCHAR 110 DC
 *   IIPS BATT CHARGING_2 110 DC  -> IIPS BATT CHARGING 110 DC
 */
if (typeof window.ipsBattBaseAttrName !== 'function') {
    window.ipsBattBaseAttrName = function (attrLabel) {
        var text = String(attrLabel || '');
        text = text.replace(
            /(DISCHARGING|DISCHARGER|DISCHARGE|DISCHAR|CHARGING|CHARGER|CHARGE|CHAR)[\-_]\s*\d+/gi,
            '$1'
        );
        return text.replace(/\s{2,}/g, ' ').trim();
    };
}
var ipsBattBaseAttrName = window.ipsBattBaseAttrName;

// IPS GRAPH — History API graph for IPS, works for 1 OR many assets.
// Self-contained and simple so it is easy to debug:
//   fnBindIpsGraph()  -> builds the UI shell, then loads data
//   _ipsLoad()        -> fetches GetHistoryData for each selected asset
//   _ipsBuildSeries() -> turns the API response into plot series
//   _ipsPanel()       -> draws the attribute checkboxes
//   _ipsRender()      -> draws the ECharts line chart
// All state lives in one object: window.ipsG  (easy to inspect in console)
// =====================================================================
var ipsG = window.ipsG = {
    hours: 24,      // current time range
    assetIds: [],   // selected asset ids
    raw: {},        // assetId -> History API "Data" array
    series: {},     // key "assetId||attrId" -> { assetId, attrId, name, assetName, color, points, isBin }
    order: [],      // series keys in colour-assignment order
    checked: {},    // key -> true/false (is the series shown)
    start: 0, end: 0,
    fromMs: 0, toMs: 0,

    // One selected asset uses overlay.
    // Multiple selected assets use stacked lanes by default.
    mode: 'overlay',

    // Detect when the selected IPS asset list changes.
    selectionKey: '',

    chart: null,
    reqId: 0,       // increments each load; stale responses are ignored
    xhrs: []        // in-flight requests, so we can cancel them
};

// Same colour list the normal graph uses.
var IPS_COLORS = (typeof _gC !== 'undefined' && _gC.length) ? _gC : [
    '#4bacc6', '#215968', '#717171', '#7366ff', '#cc6600', '#90d474', '#4d4d00',
    '#00e396', '#00ceba', '#f37995', '#0891b2', '#db2777', '#059669', '#ea580c'
];

// Selected asset ids, with empty / "0" removed.
function ipsSelectedIds() {
    var ids = getSelectedAssetIds() || [], out = [];
    for (var i = 0; i < ids.length; i++) {
        var v = ids[i];
        if (v && v !== '0') out.push(String(v));
    }
    return out;
}

// Asset display name (works without a WebSocket): bulk metadata is the
// canonical source in FRS Advance; fall back to the dropdown text, then WS.
function ipsAssetName(assetId) {
    var t = $('#listAssetNumber .dropdown-item[data-value="' + assetId + '"]').data('text');
    var wsName = (wsLiveData[assetId] && wsLiveData[assetId].AssetName)
        ? wsLiveData[assetId].AssetName
        : null;

    if (typeof getBulkAssetName === 'function') {
        var bulkName = getBulkAssetName(assetId, t ? String(t) : wsName);
        if (bulkName && bulkName !== ('Asset ' + assetId)) return bulkName;
    }

    if (t) return String(t);
    if (wsName) return wsName;
    return 'Asset ' + assetId;
}

// Attribute display name for a given asset + attribute id.
function ipsAttrName(assetId, attrId, dataType, rawName) {
    var idStr = String(attrId);
    var exactKey = String(assetId) + '_' + idStr;

    // FRS Advance keeps aliases in the bulk-metadata maps, so ask those first —
    // they are the same source the table and card renderers display from, which
    // keeps the graph legend consistent with the rest of the page.
    if (typeof getBulkAliasName === 'function') {
        var bulkAlias = getBulkAliasName(assetId, rawName, attrId);
        if (bulkAlias) return bulkAlias;
    }

    // Resolve the alias from THIS selected asset first.
    // This is important when multiple IPS assets contain the
    // same AssetAttributeId but use different aliases.
    if (
        typeof userAssetSimpleMap !== 'undefined' &&
        userAssetSimpleMap[exactKey] &&
        userAssetSimpleMap[exactKey].name
    ) {
        return userAssetSimpleMap[exactKey].name;
    }

    if (
        typeof userAssetDataloggerMap !== 'undefined' &&
        userAssetDataloggerMap[exactKey] &&
        userAssetDataloggerMap[exactKey].name
    ) {
        return userAssetDataloggerMap[exactKey].name;
    }

    if (typeof resolveUserAssetName === 'function') {
        var resolved = resolveUserAssetName(
            assetId,
            attrId,
            dataType
        );

        if (resolved && resolved.name) {
            return resolved.name;
        }
    }

    // Use the History API name only after checking
    // the selected asset's metadata.
    if (rawName !== undefined && rawName !== null) {
        var historyName = String(rawName).trim();

        if (
            historyName &&
            historyName !== idStr &&
            !/^attr\s*\d+$/i.test(historyName)
        ) {
            return historyName;
        }
    }

    if (typeof getAttrDisplayName === 'function') {
        var displayName = getAttrDisplayName(
            null,
            attrId
        );

        if (
            displayName &&
            displayName !== idStr
        ) {
            return displayName;
        }
    }

    if (
        typeof assetAttributeMap !== 'undefined' &&
        assetAttributeMap[attrId]
    ) {
        return assetAttributeMap[attrId];
    }

    if (
        typeof fallbackAliasById !== 'undefined' &&
        fallbackAliasById[attrId]
    ) {
        return fallbackAliasById[attrId];
    }

    return 'Attr ' + idStr;
}
function ipsKey(assetId, attrId) { return assetId + '||' + attrId; }

// ---- ENTRY POINT (called from fnSearchView when view = Graph + IPS) ----
function fnBindIpsGraph() {
    var siteId = $('#drpSite').val();

    if (!siteId || siteId === '0') {
        showWarning(
            'Please select a site',
            'Validation'
        );
        return;
    }

    var selectedIds = ipsSelectedIds();

    if (selectedIds.length === 0) {
        showWarning(
            'Please select at least one asset',
            'Validation'
        );
        return;
    }

    var selectionKey = selectedIds
        .slice()
        .sort()
        .join(',');

    // Reset checkbox state only when the selected IPS
    // asset list actually changes.
    if (ipsG.selectionKey !== selectionKey) {
        ipsG.checked = {};

        // One asset works like Track/Signal overlay.
        // Multiple assets use independent stacked lanes.
        ipsG.mode =
            selectedIds.length > 1
                ? 'stacked'
                : 'overlay';

        ipsG.selectionKey = selectionKey;
    }

    ipsG.assetIds = selectedIds;

    _ipsBuildShell();

    // FRS Advance's loadUserAssetInfo() is the adapter over the bulk-metadata
    // layer (it caches per site+type and routes Point Machine to its own path),
    // so every IPS attribute gets the same alias the table and cards show.
    loadUserAssetInfo(
        siteId,
        function () {
            _ipsLoad();
        }
    );
}

// ---- Build the static UI (header, time buttons, panel, chart div) ----
function _ipsBuildShell() {
    _ipsInjectStyles();
    var ids = ipsG.assetIds;
    var siteName = ($('#drpSite option:selected').text() || '').trim();
    var title = (ids.length === 1) ? ipsAssetName(ids[0]) : (ids.length + ' IPS Assets');

    var h = '<div class="ipsg-wrap">';
    h += '<div class="ipsg-head">';
    h += '<div class="ipsg-title"><i class="fas fa-chart-line"></i><div><b>' + title + '</b>' +
        '<div class="ipsg-sub">' + siteName + ' · IPS History Graph</div></div></div>';
    h += '<div id="ipsgRange" style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;font-weight:600;color:#fff;margin:0;">From Date:</label>' +
        '<input type="datetime-local" id="ipsgFromDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:4px 8px;color:rgba(255,255,255,0.96);background:rgba(255,255,255,0.04);outline:none;"/>' +
        '<label style="font-size:12px;font-weight:600;color:#fff;margin:0;">To Date:</label>' +
        '<input type="datetime-local" id="ipsgToDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:4px 8px;color:rgba(255,255,255,0.96);background:rgba(255,255,255,0.04);outline:none;"/>' +
        '<button id="ipsgLoadBtn" class="ipsg-tb on" style="cursor:pointer;"><i class="fas fa-sync-alt"></i> Load</button>' +
        '</div></div>';

    // Single asset can use Overlay or Stacked.
    // Multiple assets start in Stacked mode but can
    // still be switched to Overlay by the user.
    h += '<div class="ipsg-modebar">' +
        '<span class="ipsg-mode-label">View Mode:</span>' +

        '<button type="button" ' +
        'class="ipsg-mode-btn' +
        (ipsG.mode === 'overlay' ? ' active' : '') +
        '" data-mode="overlay">' +
        '<i class="fas fa-layer-group"></i> Overlay' +
        '</button>' +

        '<button type="button" ' +
        'class="ipsg-mode-btn' +
        (ipsG.mode === 'stacked' ? ' active' : '') +
        '" data-mode="stacked">' +
        '<i class="fas fa-bars"></i> Stacked' +
        '</button>' +

        '</div>';

    h += '<div class="ipsg-selbar">' +
        '<span class="ipsg-lbl">Series:</span>' +
        '<button class="ipsg-sa" onclick="ipsSelectAll(true)">Select All</button>' +
        '<button class="ipsg-sa" onclick="ipsSelectAll(false)">Unselect All</button>' +
        '<span id="ipsgCount" class="ipsg-count"></span></div>';

    h += '<div id="ipsgPanel" class="ipsg-panel"></div>';

    h += '<div class="ipsg-toolbar">' +
        '<button class="ipsg-tool" onclick="if(ipsG.chart)ipsG.chart.dispatchAction({type:\'restore\'})"><i class="fas fa-undo"></i> Reset Zoom</button>' +
        '<button class="ipsg-tool" onclick="ipsSavePng()"><i class="fas fa-download"></i> Save PNG</button></div>';

    h += '<div id="ipsgLoad" class="ipsg-load"><div class="ipsg-spin"></div> Loading telemetry data…</div>';
    h += '<div id="ipsgChart" class="ipsg-chart"></div>';
    h += '<div id="ipsgEmpty" class="ipsg-empty">No data available for this time range</div>';
    h += '</div>';

    $('#divTelemetryLive').empty().append(h);

    function _ipsFmtLocal(ms) {
        var d = new Date(ms);

        var pad = function (n) {
            return String(n).padStart(2, '0');
        };

        return d.getFullYear() + '-' +
            pad(d.getMonth() + 1) + '-' +
            pad(d.getDate()) + 'T' +
            pad(d.getHours()) + ':' +
            pad(d.getMinutes());
    }

    var nowMs = Date.now();

    if (!ipsG.toMs) {
        ipsG.toMs = nowMs;
    }

    if (!ipsG.fromMs) {
        var midnight = new Date(nowMs);
        midnight.setHours(0, 0, 0, 0);
        ipsG.fromMs = midnight.getTime();
    }

    $('#ipsgFromDate').val(
        _ipsFmtLocal(ipsG.fromMs)
    );

    $('#ipsgToDate').val(
        _ipsFmtLocal(ipsG.toMs)
    );

    $('#ipsgFromDate,#ipsgToDate').attr(
        'max',
        _ipsFmtLocal(nowMs)
    );

    $('#ipsgLoadBtn').off('click').on('click', function () {
        var fromMs = new Date(
            $('#ipsgFromDate').val()
        ).getTime();

        var toMs = new Date(
            $('#ipsgToDate').val()
        ).getTime();

        var currentMs = Date.now();

        if (isNaN(fromMs) || isNaN(toMs)) {
            showWarning(
                'Please select valid From Date and To Date.',
                'Validation'
            );
            return;
        }

        if (fromMs > currentMs || toMs > currentMs) {
            showWarning(
                'Future dates are not allowed.',
                'Validation'
            );
            return;
        }

        if (fromMs >= toMs) {
            showWarning(
                'From Date must be earlier than To Date.',
                'Validation'
            );
            return;
        }

        ipsG.fromMs = fromMs;
        ipsG.toMs = toMs;

        _ipsLoad();
    });

    $('.ipsg-mode-btn')
        .off('click.ipsgmode')
        .on('click.ipsgmode', function () {
            var mode = String(
                $(this).data('mode') || 'overlay'
            );

            if (
                mode !== 'overlay' &&
                mode !== 'stacked'
            ) {
                return;
            }

            if (ipsG.mode === mode) {
                return;
            }

            ipsG.mode = mode;

            $('.ipsg-mode-btn')
                .removeClass('active');

            $(this).addClass('active');

            // Reuse already loaded History data.
            // No extra API request is required.
            _ipsRender();
        });
}

// ---- Fetch GetHistoryData for every selected asset, then draw ----
function _ipsAbort() {
    if (ipsG.xhrs) ipsG.xhrs.forEach(function (x) { try { x.abort(); } catch (e) { } });
    ipsG.xhrs = [];
}

function _ipsLoad() {
    $('#ipsgLoad').show(); $('#ipsgChart').hide(); $('#ipsgEmpty').hide();

    ipsG.end = ipsG.toMs || Date.now();

    if (ipsG.end > Date.now()) {
        ipsG.end = Date.now();
    }

    if (!ipsG.fromMs) {
        var midnight = new Date(ipsG.end);
        midnight.setHours(0, 0, 0, 0);
        ipsG.fromMs = midnight.getTime();
    }

    ipsG.start = ipsG.fromMs;

    var s = new Date(ipsG.start);
    var e = new Date(ipsG.end);

    // Cancel anything still running from a previous load, and stamp this load
    // with a token so late responses from the old load are ignored.
    _ipsAbort();
    var myReq = ++ipsG.reqId;
    ipsG.raw = {};

    var ids = ipsG.assetIds.slice();
    var next = 0, done = 0, active = 0;
    var MAX_CONCURRENT = 6;   // don't fire all asset requests at once

    function finish() {
        if (myReq !== ipsG.reqId) return;   // a newer load replaced us
        $('#ipsgLoad').hide();
        _ipsBuildSeries();
        if (ipsG.order.length === 0) { $('#ipsgEmpty').show(); return; }
        _ipsPanel();
        $('#ipsgChart').show();
        _ipsRender();
    }

    function pump() {
        while (active < MAX_CONCURRENT && next < ids.length) {
            (function (aid) {
                active++;
                var url = HISTORY_API_BASE + '?assetId=' + aid +
                    '&startDate=' + formatDateForHistoryApi(s) +
                    '&endDate=' + formatDateForHistoryApi(e);
                var xhr = $.ajax({ url: url, type: 'GET', dataType: 'json', timeout: 60000 });
                ipsG.xhrs.push(xhr);
                xhr.then(function (r) {
                    if (myReq === ipsG.reqId) ipsG.raw[aid] = (r && r.Data) ? r.Data : [];
                }, function () {
                    if (myReq === ipsG.reqId) ipsG.raw[aid] = [];   // failure/abort -> empty
                }).always(function () {
                    active--; done++;
                    if (myReq !== ipsG.reqId) return;               // stale load, stop
                    if (done >= ids.length) finish(); else pump();  // start the next one
                });
            })(ids[next++]);
        }
    }

    if (ids.length === 0) { finish(); return; }
    pump();
}

// Robust timestamp -> ms. Handles ISO dates AND the "ddMMyyyy_HHmmss" form.
function ipsParseTime(ts) {
    if (!ts) return NaN;
    var t = new Date(ts).getTime();
    if (!isNaN(t)) return t;
    var m = String(ts).match(/^(\d{2})(\d{2})(\d{4})_(\d{2})(\d{2})(\d{2})$/);
    if (m) {
        return new Date(+m[3], (+m[2]) - 1, +m[1], +m[4], +m[5], +m[6]).getTime();
    }
    return NaN;
}

// ---- Robust timestamp parsing (ISO or ddMMyyyy_HHmmss; device then local) ----
function ipsParseTime(tsDevice, tsLocal) {
    function toMs(ts) {
        if (!ts) return NaN;
        if (String(ts).indexOf('0001') >= 0) return NaN;   // .NET "empty" date
        var t = new Date(ts).getTime();                     // ISO / standard
        if (!isNaN(t)) return t;
        var m = String(ts).match(/^(\d{2})(\d{2})(\d{4})_(\d{2})(\d{2})(\d{2})$/);  // ddMMyyyy_HHmmss
        if (m) return new Date(+m[3], (+m[2]) - 1, +m[1], +m[4], +m[5], +m[6]).getTime();
        return NaN;
    }
    var d = toMs(tsDevice);                  // prefer device time
    return !isNaN(d) ? d : toMs(tsLocal);    // fall back to local time
}

function ipsIsBinaryAttribute(
    assetId,
    attributeId,
    firstValue,
    attributeRow
) {
    firstValue = firstValue || {};
    attributeRow = attributeRow || {};

    var dataType = String(
        firstValue.DataType ||
        attributeRow.DataType ||
        ''
    ).trim().toLowerCase();

    // DataType from History API is authoritative.
    // When it is present, do not allow metadata fallback
    // to override it.
    if (dataType) {
        return (
            dataType === 'datalogger' ||
            dataType === 'digital' ||
            dataType === 'binary' ||
            dataType === 'boolean' ||
            dataType === 'bool' ||
            dataType === 'bit'
        );
    }

    // DataType is missing. Only now check optional ValueType.
    var valueType = String(
        firstValue.ValueType ||
        firstValue.DataValueType ||
        attributeRow.ValueType ||
        attributeRow.DataValueType ||
        ''
    ).trim().toLowerCase();

    if (valueType) {
        return (
            valueType === 'digital' ||
            valueType === 'binary' ||
            valueType === 'boolean' ||
            valueType === 'bool' ||
            valueType === 'bit'
        );
    }

    // DataType and ValueType were not provided.
    // Use exact asset-specific metadata only as a fallback.
    var metadataKey =
        String(assetId || '') +
        '_' +
        String(attributeId || '');

    if (
        typeof userAssetDataloggerMap !== 'undefined' &&
        userAssetDataloggerMap[metadataKey]
    ) {
        return true;
    }

    return false;
}

// ---- Turn the API response into plottable series, grouped by asset ----
// ---- BATT CHARGING / BATT DISCHARGING grouping (graph) -------------------
// Mirrors ipsGroupCurrentRows(), which the flat IPS table uses to combine
// multiple "BATT CHARGING n" / "BATT DISCHARGING n" assets into a single
// aggregated row per attribute. Here we do the equivalent for the History
// graph: multiple per-asset time series that belong to the same bucket
// (charging or discharging), the same attribute, and the same base asset
// name (asset name with a trailing number stripped) are summed into one
// combined line instead of being plotted separately.
function _ipsChargingBucket(assetName) {
    var up = String(assetName || '').toUpperCase();
    if (up.indexOf('DISCHARGING') !== -1) { return 'discharging'; }
    if (up.indexOf('CHARGING') !== -1) { return 'charging'; }
    return null;
}

function _ipsChargingBaseName(assetName) {
    return String(assetName || '')
        .replace(/[\s\-_]*\d+\s*$/, '')
        .trim() || String(assetName || '');
}

// Sum multiple step-held time series (last known value carried forward,
// same semantics _ipsBuildSeries already applies at the range boundaries)
// into one combined series across their shared timeline.
function _ipsSumPointSeries(pointSeriesList) {
    var timeSet = {};

    pointSeriesList.forEach(function (points) {
        points.forEach(function (p) {
            timeSet[p[0]] = true;
        });
    });

    var times = Object.keys(timeSet)
        .map(Number)
        .sort(function (a, b) { return a - b; });

    var pointers = pointSeriesList.map(function () { return 0; });
    var lastValues = pointSeriesList.map(function () { return null; });
    var merged = [];

    times.forEach(function (t) {
        pointSeriesList.forEach(function (points, idx) {
            while (
                pointers[idx] < points.length &&
                points[pointers[idx]][0] <= t
            ) {
                lastValues[idx] = points[pointers[idx]][1];
                pointers[idx]++;
            }
        });

        var sum = 0;
        var hasValue = false;

        lastValues.forEach(function (v) {
            if (v !== null && v !== undefined && !isNaN(v)) {
                sum += v;
                hasValue = true;
            }
        });

        merged.push([t, hasValue ? sum : 0]);
    });

    return merged;
}

// Replace per-asset BATT CHARGING / BATT DISCHARGING series in
// ipsG.series / ipsG.order with one combined series per bucket +
// attribute + base asset name.
function _ipsGroupChargingSeries() {
    var groups = {};

    ipsG.order.forEach(function (key) {
        var series = ipsG.series[key];
        if (!series) { return; }

        var bucket = _ipsChargingBucket(series.assetName);
        if (!bucket) { return; }

        var baseName = _ipsChargingBaseName(series.assetName);
        // Same bank-less attribute key the flat table uses, so the graph
        // collapses BATT CHAR-1..n into one line instead of attrId-per-bank.
        var groupAttrName = ipsBattBaseAttrName(series.name);
        var groupKey = bucket + '||' + groupAttrName + '||' + baseName;

        if (!groups[groupKey]) {
            groups[groupKey] = {
                bucket: bucket,
                baseName: baseName,
                attrId: series.attrId,
                attrName: (window.IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME === true) ? series.name : groupAttrName,
                memberKeys: []
            };
        }

        groups[groupKey].memberKeys.push(key);
    });

    Object.keys(groups).forEach(function (groupKey) {
        var group = groups[groupKey];

        var memberSeries = group.memberKeys
            .map(function (k) { return ipsG.series[k]; })
            .filter(Boolean);

        if (memberSeries.length === 0) { return; }

        var mergedPoints = _ipsSumPointSeries(
            memberSeries.map(function (s) { return s.points; })
        );

        var isBin = memberSeries.every(function (s) { return s.isBin; });

        var newKey = 'battgrp::' + groupKey;
        var colorSource = memberSeries[0];

        ipsG.series[newKey] = {
            key: newKey,

            // Synthetic id so this combined series never collides with
            // a real selected asset (used by ipsSeriesLabel/assetAttrCount).
            assetId: 'battgrp::' + group.bucket,
            attrId: group.attrId,

            name: group.attrName,

            assetName:
                group.baseName ||
                (
                    group.bucket === 'discharging'
                        ? 'BATT DISCHARGING'
                        : 'BATT CHARGING'
                ),

            color: colorSource ? colorSource.color : IPS_COLORS[0],

            points: mergedPoints,
            isBin: isBin,
            isGroup: true
        };

        // Drop the individual per-asset series — the combined series
        // replaces them, matching the flat table which only shows the
        // aggregated BATT CHARGING / BATT DISCHARGING row.
        group.memberKeys.forEach(function (k) {
            delete ipsG.series[k];
            delete ipsG.checked[k];
        });

        ipsG.order.push(newKey);

        if (ipsG.checked[newKey] === undefined) {
            ipsG.checked[newKey] = true;
        }
    });
}

// ---- Turn the API response into plottable series, grouped by asset ----
function _ipsBuildSeries() {
    ipsG.series = {};
    ipsG.order = [];

    var colorIndex = 0;

    // Keep series grouped in the same order as the
    // selected IPS assets.
    ipsG.assetIds.forEach(function (assetId) {
        var historyRows =
            ipsG.raw[assetId] || [];

        historyRows.forEach(function (attributeRow) {
            if (
                !attributeRow ||
                attributeRow.AttributeId === undefined ||
                attributeRow.AttributeId === null ||
                !attributeRow.Values
            ) {
                return;
            }

            var values = attributeRow.Values;
            var valueKeys = Object.keys(values);

            if (valueKeys.length === 0) {
                return;
            }

            var firstValue =
                values['1'] ||
                values[valueKeys[0]] ||
                {};

            var dataType = String(
                firstValue.DataType || ''
            );

            var rawName =
                firstValue.AssetAttributeName ||
                firstValue.AttributeName ||
                attributeRow.AttributeName ||
                '';

            var sourcePoints = [];

            // IPS attributes are measurements.
            // Even when all samples happen to be 0 or 1, they must remain numeric values.
            // Do not convert IPS data into On/Off or Pickup/Drop states.
            var isBinary = false;

            var sequence = 0;

            valueKeys.forEach(function (valueKey) {
                var entry = values[valueKey];

                if (!entry) {
                    return;
                }

                var timestamp =
                    entry.Timestamp || {};

                // Device time first, then local time.
                var time = ipsParseTime(
                    timestamp.TimestampDevice ||
                    entry.TimestampDevice,

                    timestamp.TimestampLocal ||
                    entry.TimestampLocal
                );

                if (
                    isNaN(time) ||
                    time <= 0
                ) {
                    return;
                }

                var value = parseFloat(
                    entry.Value
                );

                if (isNaN(value)) {
                    return;
                }

                sourcePoints.push({
                    time: time,
                    value: value,
                    sequence: sequence++
                });

                if (
                    value !== 0 &&
                    value !== 1
                ) {
                    isBinary = false;
                }
            });

            // Do not plot an artificial zero line when
            // the History API returned no valid samples.
            if (sourcePoints.length === 0) {
                return;
            }

            sourcePoints.sort(function (a, b) {
                if (a.time !== b.time) {
                    return a.time - b.time;
                }

                // Preserve source order when multiple values
                // have the same TimestampDevice.
                return a.sequence - b.sequence;
            });

            var points = [];
            var lastBeforeStart = null;

            for (
                var pointIndex = 0;
                pointIndex < sourcePoints.length;
                pointIndex++
            ) {
                var point =
                    sourcePoints[pointIndex];

                if (point.time < ipsG.start) {
                    lastBeforeStart =
                        point.value;
                    continue;
                }

                if (point.time > ipsG.end) {
                    break;
                }

                points.push([
                    point.time,
                    tlZeroFloor(point.value)
                ]);
            }

            // Carry the preceding state/value to the
            // requested left boundary.
            if (lastBeforeStart !== null) {
                points.unshift([
                    ipsG.start,
                    tlZeroFloor(lastBeforeStart)
                ]);
            }
            else if (
                points.length > 0 &&
                points[0][0] > ipsG.start
            ) {
                points.unshift([
                    ipsG.start,
                    points[0][1]
                ]);
            }

            if (points.length === 0) {
                return;
            }

            // Extend the last value to the right boundary.
            var lastPoint =
                points[points.length - 1];

            if (lastPoint[0] < ipsG.end) {
                points.push([
                    ipsG.end,
                    lastPoint[1]
                ]);
            }

            var key = ipsKey(
                assetId,
                attributeRow.AttributeId
            );

            // The History API can return more than one row for the
            // same AssetId + AttributeId. ECharts series IDs must be unique.
            //
            // Merge the points into the existing series instead of
            // adding the same key to ipsG.order again.
            if (ipsG.series[key]) {
                var existingSeries =
                    ipsG.series[key];

                var mergedPoints =
                    existingSeries.points
                        .concat(points)
                        .sort(function (a, b) {
                            return a[0] - b[0];
                        });

                // Remove only exact duplicate [timestamp, value] points.
                // Different values at the same TimestampDevice are preserved.
                var uniquePoints = [];
                var pointSeen = {};

                for (
                    var mergeIndex = 0;
                    mergeIndex < mergedPoints.length;
                    mergeIndex++
                ) {
                    var mergedPoint =
                        mergedPoints[mergeIndex];

                    if (
                        !mergedPoint ||
                        mergedPoint.length < 2
                    ) {
                        continue;
                    }

                    var pointSignature =
                        String(mergedPoint[0]) +
                        '||' +
                        String(mergedPoint[1]);

                    if (pointSeen[pointSignature]) {
                        continue;
                    }

                    pointSeen[pointSignature] = true;

                    uniquePoints.push(
                        mergedPoint
                    );
                }

                existingSeries.points =
                    uniquePoints;

                // It remains binary only when both copies contain
                // exclusively 0/1 values.
                existingSeries.isBin =
                    existingSeries.isBin &&
                    isBinary;

                // Do not push the duplicate key into ipsG.order.
                return;
            }

            ipsG.series[key] = {
                key: key,
                assetId: String(assetId),
                attrId: attributeRow.AttributeId,

                name: ipsAttrName(
                    assetId,
                    attributeRow.AttributeId,
                    dataType,
                    rawName
                ),

                assetName:
                    ipsAssetName(assetId),

                color:
                    IPS_COLORS[
                    colorIndex %
                    IPS_COLORS.length
                    ],

                points: points,
                isBin: isBinary
            };

            colorIndex++;

            // Add every series key only once.
            ipsG.order.push(key);

            if (
                ipsG.checked[key] ===
                undefined
            ) {
                ipsG.checked[key] = true;
            }
        });
    });

    // Combine BATT CHARGING / BATT DISCHARGING series across assets,
    // the same way the flat IPS table groups them (ipsGroupCurrentRows).
    _ipsGroupChargingSeries();

    // Final order safety: every AssetId + AttributeId
    // series key must occur exactly once.
    var uniqueOrder = [];
    var orderSeen = {};

    ipsG.order.forEach(function (key) {
        if (orderSeen[key]) {
            return;
        }

        if (!ipsG.series[key]) {
            return;
        }

        orderSeen[key] = true;
        uniqueOrder.push(key);
    });

    ipsG.order = uniqueOrder;

    // Remove checkbox states belonging to assets or
    // attributes that are no longer selected.
    Object.keys(ipsG.checked)
        .forEach(function (key) {
            if (!ipsG.series[key]) {
                delete ipsG.checked[key];
            }
        });

    // Used by ipsSeriesLabel().
    ipsG.assetAttrCount = {};

    ipsG.order.forEach(function (key) {
        var series =
            ipsG.series[key];

        if (!series) {
            return;
        }

        ipsG.assetAttrCount[
            series.assetId
        ] =
            (
                ipsG.assetAttrCount[
                series.assetId
                ] || 0
            ) + 1;
    });
}
// Consistent series label used by BOTH the chart legend and the panel so they never
// disagree. Single asset -> attribute name. Multiple assets -> asset name, plus the
// attribute name when that asset has more than one attribute selected.
function ipsSeriesLabel(sv) {
    var many = ipsG.assetIds.length > 1;
    var multiAttr = ipsG.assetAttrCount && ipsG.assetAttrCount[sv.assetId] > 1;
    if (!many) return sv.name;                                  // single asset -> attribute
    return sv.assetName + (multiAttr ? ' · ' + sv.name : '');   // asset [· attribute]
}

// ---- Draw the attribute checkbox panel (grouped by asset when many) ----
function _ipsPanel() {
    var many = ipsG.assetIds.length > 1;
    var html = '<div class="ipsg-attrs">';

    // One compact chip per series (asset · attribute), no per-asset blocks.
    ipsG.order.forEach(function (k) {
        var sv = ipsG.series[k], on = ipsG.checked[k] !== false;
        var label = ipsSeriesLabel(sv);
        var last = sv.points.length ? sv.points[sv.points.length - 1][1] : null;
        var valTxt = (last == null) ? '—' : tlZeroFloor(Number(last)).toFixed(2);
        html += '<label class="ipsg-attr">' +
            '<input type="checkbox" data-key="' + k + '" ' + (on ? 'checked' : '') + ' onchange="ipsToggle(this)">' +
            '<span class="ipsg-dot" style="background:' + sv.color + '"></span>' +
            '<span class="ipsg-an">' + label + '</span></label>';
    });
    html += '</div>';

    $('#ipsgPanel').html(html);
    _ipsCount();
}

function _ipsCount() {
    var total = ipsG.order.length;
    var on = ipsG.order.filter(function (k) { return ipsG.checked[k] !== false; }).length;
    $('#ipsgCount').text(on + ' / ' + total + ' shown');
}

// ---- Checkbox handlers (global so the inline onclick/onchange can find them) ----
function ipsToggle(el) { ipsG.checked[$(el).data('key')] = el.checked; _ipsCount(); _ipsRender(); }
function ipsSelectAll(on) {
    ipsG.order.forEach(function (k) { ipsG.checked[k] = on; });
    $('#ipsgPanel input[type=checkbox]').prop('checked', on);
    _ipsCount(); _ipsRender();
}
function ipsSelectAsset(aid, on) {
    ipsG.order.forEach(function (k) { if (ipsG.series[k].assetId === String(aid)) ipsG.checked[k] = on; });
    $('#ipsgPanel .ipsg-group[data-asset="' + aid + '"] input[type=checkbox]').prop('checked', on);
    _ipsCount(); _ipsRender();
}
function ipsSavePng() {
    if (!ipsG.chart) return;
    var u = ipsG.chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
    var a = document.createElement('a'); a.href = u; a.download = 'ips_graph.png'; a.click();
}

// ---- Draw the chart ----
function _ipsRender() {
    var element =
        document.getElementById(
            'ipsgChart'
        );

    if (
        !element ||
        typeof echarts === 'undefined'
    ) {
        return;
    }

    var visibleKeys = [];
    var visibleKeySeen = {};

    ipsG.order.forEach(function (key) {
        // Defensive protection against stale or previously-created
        // duplicate keys in ipsG.order.
        if (visibleKeySeen[key]) {
            return;
        }

        var series =
            ipsG.series[key];

        if (
            ipsG.checked[key] === false ||
            !series ||
            !series.points ||
            series.points.length === 0
        ) {
            return;
        }

        visibleKeySeen[key] = true;

        visibleKeys.push(key);
    });

    if (ipsG.chart) {
        ipsG.chart.dispose();
        ipsG.chart = null;
    }

    if (visibleKeys.length === 0) {
        $('#ipsgChart').hide();
        $('#ipsgEmpty').show();
        return;
    }

    $('#ipsgEmpty').hide();
    $('#ipsgChart').show();

    var stacked =
        ipsG.mode === 'stacked';

    var laneHeight = 90;
    var laneGap = 34;
    var topPadding = 35;
    var bottomPadding = 70;

    // Increase chart height only in stacked mode.
    if (stacked) {
        var requiredHeight =
            topPadding +
            (
                visibleKeys.length *
                laneHeight
            ) +
            (
                Math.max(
                    0,
                    visibleKeys.length - 1
                ) *
                laneGap
            ) +
            bottomPadding;

        $('#ipsgChart').css(
            'height',
            Math.max(
                500,
                requiredHeight
            ) + 'px'
        );
    }
    else {
        $('#ipsgChart').css(
            'height',
            '500px'
        );
    }

    ipsG.chart = echarts.init(
        element,
        null,
        {
            renderer: 'canvas'
        }
    );

    // IPS-only actual crosshair position.
    // Kept local to _ipsRender so it cannot affect other graph types.
    var ipsActualHoverMs = null;

    function formatTooltip(params) {
        if (
            !params ||
            params.length === 0
        ) {
            return '';
        }

        var first =
            params[0] || {};

        /*
         * ECharts may supply a timestamp snapped to a nearby data point.
         * Prefer the continuously moving IPS axis-pointer timestamp.
         */
        var formatterHoverMs =
            Number(
                first.axisValue !== undefined &&
                    first.axisValue !== null
                    ? first.axisValue
                    : (
                        first.value &&
                            first.value.length
                            ? first.value[0]
                            : NaN
                    )
            );

        /*
 * Prefer the real unsnapped X-axis cursor position.
 *
 * first.axisValue can be snapped by ECharts to the next plotted
 * timestamp, which causes a future IPS value to appear before the
 * pointer reaches its actual change time.
 */
        var hoverMs =
            ipsActualHoverMs !== null &&
                ipsActualHoverMs !== undefined &&
                !isNaN(Number(ipsActualHoverMs))
                ? Number(ipsActualHoverMs)
                : formatterHoverMs;

        if (isNaN(hoverMs)) {
            return '';
        }

        function readIpsPointTime(point) {
            var rawTime =
                point &&
                    point.value &&
                    point.value.length
                    ? point.value[0]
                    : (
                        point &&
                            point.length
                            ? point[0]
                            : null
                    );

            var numericTime =
                Number(rawTime);

            return isNaN(numericTime)
                ? null
                : numericTime;
        }

        function readIpsPointValue(point) {
            var rawValue =
                point &&
                    point.value &&
                    point.value.length > 1
                    ? point.value[1]
                    : (
                        point &&
                            point.length > 1
                            ? point[1]
                            : null
                    );

            var numericValue =
                Number(rawValue);

            return isNaN(numericValue)
                ? null
                : numericValue;
        }

        /*
         * Resolve the value active at the cursor and the actual timestamp at
         * which that current value began.
         */
        function getIpsValueAtTime(
            series,
            hoverTime
        ) {
            if (
                !series ||
                !series.points ||
                series.points.length === 0
            ) {
                return {
                    value: null,
                    changeTime: null
                };
            }

            var points =
                series.points;

            var low = 0;
            var high =
                points.length - 1;

            var foundIndex = -1;

            /*
             * Find the latest actual reading at or before the cursor.
             * A future IPS value can never appear before its timestamp.
             */
            while (low <= high) {
                var middle =
                    (low + high) >> 1;

                var pointTime =
                    readIpsPointTime(
                        points[middle]
                    );

                if (
                    pointTime !== null &&
                    pointTime <= hoverTime
                ) {
                    foundIndex = middle;
                    low = middle + 1;
                }
                else {
                    high = middle - 1;
                }
            }

            if (foundIndex < 0) {
                return {
                    value: null,
                    changeTime: null
                };
            }

            var currentValue =
                readIpsPointValue(
                    points[foundIndex]
                );

            if (currentValue === null) {
                return {
                    value: null,
                    changeTime: null
                };
            }

            var changeIndex =
                foundIndex;

            /*
             * Repeated samples of the same value are not new changes.
             *
             * Example:
             * 01:00 -> 24.50
             * 01:30 -> 24.50
             * 02:00 -> 24.50
             *
             * The applicable change time remains 01:00.
             */
            while (changeIndex > 0) {
                var previousValue =
                    readIpsPointValue(
                        points[changeIndex - 1]
                    );

                if (
                    previousValue === null ||
                    previousValue !== currentValue
                ) {
                    break;
                }

                changeIndex--;
            }

            return {
                value: currentValue,

                changeTime:
                    readIpsPointTime(
                        points[changeIndex]
                    )
            };
        }

        function escapeIpsTooltipText(value) {
            return String(
                value === undefined ||
                    value === null
                    ? ''
                    : value
            )
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        }

        /*
         * Overlay:
         * show every checked IPS attribute.
         *
         * Stacked:
         * show only the attribute for the hovered graph lane.
         */
        var tooltipKeys = [];

        if (stacked) {
            var hoveredSeriesKey =
                first.seriesId !== undefined &&
                    first.seriesId !== null
                    ? String(first.seriesId)
                    : '';

            if (
                hoveredSeriesKey &&
                ipsG.series[hoveredSeriesKey]
            ) {
                tooltipKeys.push(
                    hoveredSeriesKey
                );
            }

            /*
             * Fallback through the hovered series name.
             */
            if (tooltipKeys.length === 0) {
                var hoveredSeriesName =
                    String(
                        first.seriesName || ''
                    );

                for (
                    var keyIndex = 0;
                    keyIndex < visibleKeys.length;
                    keyIndex++
                ) {
                    var fallbackKey =
                        visibleKeys[keyIndex];

                    var fallbackSeries =
                        ipsG.series[fallbackKey];

                    if (
                        fallbackSeries &&
                        ipsSeriesLabel(
                            fallbackSeries
                        ) === hoveredSeriesName
                    ) {
                        tooltipKeys.push(
                            fallbackKey
                        );

                        break;
                    }
                }
            }

            /*
             * Final defensive fallback. Since the stacked series are built in
             * visibleKeys order, seriesIndex can identify the hovered lane.
             */
            if (
                tooltipKeys.length === 0 &&
                first.seriesIndex !== undefined &&
                first.seriesIndex !== null
            ) {
                var hoveredIndex =
                    Number(first.seriesIndex);

                if (
                    !isNaN(hoveredIndex) &&
                    hoveredIndex >= 0 &&
                    visibleKeys[hoveredIndex]
                ) {
                    tooltipKeys.push(
                        visibleKeys[hoveredIndex]
                    );
                }
            }
        }
        else {
            tooltipKeys =
                visibleKeys.slice();
        }

        if (tooltipKeys.length === 0) {
            return '';
        }

        var rowItems = [];

        /*
         * Shared tooltip header:
         * use the most recent applicable actual change time among the displayed
         * IPS attributes.
         */
        var latestApplicableChangeMs =
            null;

        for (
            var tooltipIndex = 0;
            tooltipIndex < tooltipKeys.length;
            tooltipIndex++
        ) {
            var seriesKey =
                tooltipKeys[tooltipIndex];

            var series =
                ipsG.series[seriesKey];

            if (!series) {
                continue;
            }

            var lookup =
                getIpsValueAtTime(
                    series,
                    hoverMs
                );

            var value =
                lookup.value;

            if (
                lookup.changeTime !== null &&
                lookup.changeTime !== undefined &&
                !isNaN(Number(lookup.changeTime)) &&
                (
                    latestApplicableChangeMs === null ||
                    Number(lookup.changeTime) >
                    latestApplicableChangeMs
                )
            ) {
                latestApplicableChangeMs =
                    Number(lookup.changeTime);
            }

            /*
             * IPS values are always numeric measurements.
             * No On/Off or Pickup/Drop conversion is performed.
             */
            var displayValue =
                value === null
                    ? '\u2014'
                    : tlZeroFloor(Number(value)).toFixed(2);

            var seriesName =
                ipsSeriesLabel(series);

            var safeSeriesName =
                escapeIpsTooltipText(
                    seriesName
                );

            var safeColor =
                escapeIpsTooltipText(
                    series.color || 'rgba(255,255,255,0.50)'
                );

            rowItems.push(
                '<div style="' +
                'display:flex;' +
                'align-items:center;' +
                'gap:6px;' +
                'min-height:20px;' +
                'padding:1px 0;' +
                'white-space:nowrap;">' +

                '<span style="' +
                'width:8px;' +
                'height:8px;' +
                'border-radius:50%;' +
                'background:' +
                safeColor +
                ';' +
                'flex-shrink:0;">' +
                '</span>' +

                '<span style="' +
                'font-size:10px;' +
                'color:rgba(255,255,255,0.72);' +
                'overflow:hidden;' +
                'text-overflow:ellipsis;' +
                'flex:1;' +
                'min-width:0;"' +
                ' title="' +
                safeSeriesName +
                '">' +
                safeSeriesName +
                '</span>' +

                '<b style="' +
                'font-size:10px;' +
                'color:#0f172a;' +
                'font-variant-numeric:tabular-nums;' +
                'min-width:52px;' +
                'text-align:right;' +
                'flex-shrink:0;">' +
                displayValue +
                '</b>' +

                '</div>'
            );
        }

        if (rowItems.length === 0) {
            return '';
        }

        /*
         * Calculate the tooltip header only after all displayed series have
         * been resolved. This prevents the previous displayTimestamp and
         * latestApplicableChangeMs ReferenceErrors.
         */
        /*
 * Show the timestamp where the currently displayed value last changed.
 *
 * The header remains on this change time while the cursor moves through
 * the carried-forward section. It updates only when the cursor reaches
 * the next actual IPS value change.
 */
        var displayTimestamp =
            latestApplicableChangeMs !== null &&
                latestApplicableChangeMs !== undefined &&
                !isNaN(Number(latestApplicableChangeMs))
                ? Number(latestApplicableChangeMs)
                : Number(hoverMs);

        if (isNaN(displayTimestamp)) {
            return '';
        }

        if (
            displayTimestamp === null ||
            displayTimestamp === undefined ||
            isNaN(Number(displayTimestamp))
        ) {
            return '';
        }

        var date =
            new Date(
                Number(displayTimestamp)
            );

        if (isNaN(date.getTime())) {
            return '';
        }

        var headerHtml =
            '<div style="' +
            'font-weight:700;' +
            'font-size:11px;' +
            'color:#22d3ee;' +
            'margin-bottom:6px;' +
            'padding-bottom:5px;' +
            'border-bottom:1px solid rgba(255,255,255,0.10);' +
            'white-space:nowrap;">' +

            '<i class="fas fa-clock" style="' +
            'font-size:9px;' +
            'margin-right:4px;">' +
            '</i>' +

            String(
                date.getHours()
            ).padStart(2, '0') +
            ':' +

            String(
                date.getMinutes()
            ).padStart(2, '0') +
            ':' +

            String(
                date.getSeconds()
            ).padStart(2, '0') +

            '</div>';

        /*
         * Stacked mode:
         * only the hovered lane is displayed.
         */
        if (stacked) {
            return (
                headerHtml +
                '<div style="' +
                'width:220px;' +
                'max-width:220px;">' +
                rowItems.join('') +
                '</div>'
            );
        }

        /*
         * Overlay mode:
         * all selected IPS attributes appear in two balanced columns.
         */
        var rowsPerColumn =
            Math.ceil(
                rowItems.length / 2
            );

        var firstColumnRows =
            rowItems.slice(
                0,
                rowsPerColumn
            );

        var secondColumnRows =
            rowItems.slice(
                rowsPerColumn
            );

        var columnsHtml =
            '<div style="' +
            'display:flex;' +
            'flex-direction:row;' +
            'align-items:flex-start;' +
            //'gap:10px;' +
            //'max-height:320px;' +
            //'overflow-y:auto;' +
            //'overflow-x:hidden;' +
            //'padding-right:2px;">' +
            'gap:10px;' +
            'overflow:visible;' +
            'padding-right:2px;">' +

            '<div style="' +
            'display:flex;' +
            'flex-direction:column;' +
            'width:210px;' +
            'min-width:210px;' +
            (
                secondColumnRows.length
                    ? (
                        'padding-right:10px;' +
                        'border-right:1px solid rgba(255,255,255,0.10);'
                    )
                    : ''
            ) +
            '">' +
            firstColumnRows.join('') +
            '</div>' +

            (
                secondColumnRows.length
                    ? (
                        '<div style="' +
                        'display:flex;' +
                        'flex-direction:column;' +
                        'width:210px;' +
                        'min-width:210px;">' +
                        secondColumnRows.join('') +
                        '</div>'
                    )
                    : ''
            ) +

            '</div>';

        return (
            headerHtml +
            columnsHtml
        );
    }

    var option = {
        backgroundColor: 'transparent',
        animation: false,

        // Aurora is a dark surface: set the default text colour once so axis
        // labels, axis names and the legend are legible without per-item colours.
        textStyle: { color: 'rgba(255,255,255,0.72)' },

        stateAnimation: {
            duration: 0
        },

        tooltip: {
            trigger: 'axis',

            axisPointer: {
                type: 'cross',
                snap: false,
                label: { backgroundColor: 'rgba(34,211,238,0.80)' }
            },

            confine: true,

            backgroundColor:
                'rgba(5,9,24,0.97)',

            borderColor:
                'rgba(34,211,238,0.30)',

            borderWidth: 1,

            textStyle: { color: 'rgba(255,255,255,0.86)' },

            padding: [8, 10],

            extraCssText:
                'box-shadow:0 6px 24px rgba(0,0,0,0.35);' +
                'border-radius:8px;' +
                'max-width:470px;',

            formatter: formatTooltip
        }
    };

    // =====================================================
    // OVERLAY MODE
    // One IPS asset uses this mode by default.
    // Multiple assets can also be manually switched here.
    // =====================================================
    if (!stacked) {
        // IPS attributes always use their real numeric measurement scale.
        var hasBinary = false;

        var usedNames = {};
        var legends = [];
        var chartSeries = [];

        visibleKeys.forEach(function (key) {
            var series =
                ipsG.series[key];

            var label =
                ipsSeriesLabel(series);

            if (usedNames[label]) {
                label +=
                    ' #' +
                    series.attrId;
            }

            usedNames[label] = true;
            legends.push(label);

            chartSeries.push({
                id: key,
                name: label,
                type: 'line',

                smooth: false,
                step: 'end',
                connectNulls: true,
                sampling: 'none',
                hoverAnimation: false,

                showSymbol:
                    series.points.length <= 30,

                symbol: 'circle',
                symbolSize: 4,

                yAxisIndex: 0,

                lineStyle: {
                    width: 1.8,
                    color: series.color
                },

                itemStyle: {
                    color:
                        series.color
                },

                data:
                    series.points
            });
        });

        option.legend = {
            type: 'scroll',
            data: legends,
            top: 6,

            textStyle: {
                fontSize: 11
            }
        };

        option.grid = {
            left: 64,
            right:
                hasBinary
                    ? 64
                    : 28,

            top: 50,
            bottom: 92,
            containLabel: true
        };

        option.xAxis = {
            type: 'time',
            min: ipsG.start,
            max: ipsG.end,
            boundaryGap: false,
            axisPointer: { snap: false },

            splitLine: {
                show: true,

                lineStyle: {
                    color: 'rgba(255,255,255,0.06)',
                    type: 'dashed'
                }
            },

            axisLabel: {
                formatter:
                    function (value) {
                        var date =
                            new Date(value);

                        return String(
                            date.getHours()
                        ).padStart(2, '0') +
                            ':' +
                            String(
                                date.getMinutes()
                            ).padStart(2, '0');
                    }
            }
        };

        option.yAxis = [
            {
                type: 'value',
                name: 'Value',

                // Do not force zero into the scale.
                // Small IPS changes remain visible.
                scale: true,

                splitLine: {
                    show: true,

                    lineStyle: {
                        color: 'rgba(255,255,255,0.06)',
                        type: 'dashed'
                    }
                }
            },

            {
                type: 'value',
                name: 'State',
                position: 'right',

                min: 0,
                max: 1,
                interval: 1,

                show: hasBinary,

                axisLabel: {
                    formatter:
                        function (value) {
                            return value === 1
                                ? 'On'
                                : (
                                    value === 0
                                        ? 'Off'
                                        : ''
                                );
                        }
                },

                splitLine: {
                    show: false
                }
            }
        ];

        // filterMode:none preserves the preceding point
        // required by a step/carry-forward line.
        option.dataZoom = [
            {
                type: 'slider',
                height: 32,
                bottom: 8,
                start: 0,
                end: 100,
                filterMode: 'none'
            },

            {
                type: 'inside',
                filterMode: 'none',
                zoomOnMouseWheel: true,
                moveOnMouseMove: true
            }
        ];

        option.series =
            chartSeries;
    }

    // =====================================================
    // STACKED MODE
    // Multiple IPS assets use this mode by default.
    // Every selected series receives its own Y-axis scale.
    // =====================================================
    else {
        var grids = [];
        var xAxes = [];
        var yAxes = [];
        var stackedSeries = [];
        var xAxisIndexes = [];

        visibleKeys.forEach(
            function (key, index) {
                var series =
                    ipsG.series[key];

                var top =
                    topPadding +
                    index *
                    (
                        laneHeight +
                        laneGap
                    );

                var isLast =
                    index ===
                    visibleKeys.length - 1;

                xAxisIndexes.push(
                    index
                );

                grids.push({
                    left: 82,
                    right: 28,
                    top: top,
                    height: laneHeight,
                    containLabel: true
                });

                xAxes.push({
                    type: 'time',
                    gridIndex: index,

                    min: ipsG.start,
                    max: ipsG.end,
                    boundaryGap: false,

                    axisPointer: {
                        snap: false,
                        label: {
                            show: true,
                            fontSize: 9,
                            formatter: function (p) {
                                var d = new Date(p.value);
                                return String(d.getHours()).padStart(2, '0') + ':' +
                                    String(d.getMinutes()).padStart(2, '0') + ':' +
                                    String(d.getSeconds()).padStart(2, '0');
                            }
                        }
                    },

                    axisLabel: {
                        show: isLast,

                        formatter:
                            function (value) {
                                var date =
                                    new Date(value);

                                return String(
                                    date.getHours()
                                ).padStart(2, '0') +
                                    ':' +
                                    String(
                                        date.getMinutes()
                                    ).padStart(2, '0');
                            }
                    },

                    axisTick: {
                        show: isLast
                    },

                    splitLine: {
                        show: true,

                        lineStyle: {
                            color: 'rgba(255,255,255,0.06)',
                            type: 'dashed'
                        }
                    }
                });

                var yAxis = {
                    type: 'value',
                    gridIndex: index,

                    axisPointer: {
                        show: true,
                        label: {
                            show: true,
                            fontSize: 9,
                            backgroundColor: 'rgba(34,211,238,0.80)',
                            formatter: function (p) {
                                return tlZeroFloor(Number(p.value)).toFixed(2);
                            }
                        }
                    },

                    // Each analog IPS series receives
                    // its own actual value scale.
                    // Every IPS lane is a numeric measurement lane.
                    scale: true,

                    name:
                        ipsSeriesLabel(series),

                    nameLocation:
                        'middle',

                    nameGap: 55,
                    nameRotate: 90,

                    nameTextStyle: {
                        color: series.color,
                        fontSize: 10,
                        fontWeight: 600
                    },

                    axisLine: {
                        show: true,

                        lineStyle: {
                            color: series.color
                        }
                    },

                    axisLabel: {
                        fontSize: 9
                    },

                    splitLine: {
                        show: true,

                        lineStyle: {
                            color: 'rgba(255,255,255,0.06)',
                            type: 'dashed'
                        }
                    }
                };

                //if (series.isBin) {
                //    yAxis.min = 0;
                //    yAxis.max = 1;
                //    yAxis.interval = 1;

                //    yAxis.axisLabel.formatter =
                //        function (value) {
                //            return value === 1
                //                ? 'On'
                //                : (
                //                    value === 0
                //                        ? 'Off'
                //                        : ''
                //                );
                //        };
                //}

                yAxes.push(yAxis);

                stackedSeries.push({
                    id: key,

                    name:
                        ipsSeriesLabel(series),

                    type: 'line',

                    xAxisIndex: index,
                    yAxisIndex: index,

                    smooth: false,
                    step: 'end',
                    connectNulls: true,
                    sampling: 'none',
                    hoverAnimation: false,

                    showSymbol:
                        series.points.length <= 30,

                    symbol: 'circle',
                    symbolSize: 4,

                    lineStyle: {
                        width: 1.8,
                        color: series.color
                    },

                    itemStyle: {
                        color: series.color
                    },

                    data:
                        series.points
                });
            }
        );

        option.legend = {
            show: false
        };

        option.grid = grids;
        option.xAxis = xAxes;
        option.yAxis = yAxes;

        // One slider controls all stacked IPS lanes.
        option.dataZoom = [
            {
                type: 'slider',
                xAxisIndex:
                    xAxisIndexes,

                height: 32,
                bottom: 8,
                start: 0,
                end: 100,

                filterMode:
                    'none'
            },

            {
                type: 'inside',

                xAxisIndex:
                    xAxisIndexes,

                filterMode:
                    'none',

                zoomOnMouseWheel:
                    true,

                moveOnMouseMove:
                    true
            }
        ];

        option.series =
            stackedSeries;
    }

    ipsG.chart.setOption(
        option,
        true
    );

    /*
     * Capture the real unsnapped IPS X-axis pointer position.
     *
     * This is local to the IPS chart and does not modify the common Track,
     * Signal, Point Machine or DataLogger graph handlers.
     */
    ipsG.chart.off(
        'updateAxisPointer'
    );

    ipsG.chart.on(
        'updateAxisPointer',
        function (event) {
            var axesInfo =
                event &&
                event.axesInfo;

            if (
                !axesInfo ||
                axesInfo.length === 0
            ) {
                return;
            }

            var pointerValue =
                NaN;

            /*
             * In stacked mode, updateAxisPointer can contain both X-axis
             * time and Y-axis measurement information.
             *
             * Read only the X-axis value. Otherwise a Y value may be
             * incorrectly stored as the hover timestamp.
             */
            for (
                var axisIndex = 0;
                axisIndex < axesInfo.length;
                axisIndex++
            ) {
                var axisInfo =
                    axesInfo[axisIndex];

                if (
                    axisInfo &&
                    (
                        axisInfo.axisDimension === 'x' ||
                        axisInfo.axisDim === 'x'
                    )
                ) {
                    pointerValue =
                        Number(axisInfo.value);

                    break;
                }
            }

            /*
             * Compatibility fallback for ECharts versions that do not
             * provide axisDimension in the event.
             */
            if (
                isNaN(pointerValue) &&
                axesInfo[0]
            ) {
                pointerValue =
                    Number(
                        axesInfo[0].value
                    );
            }

            if (!isNaN(pointerValue)) {
                ipsActualHoverMs =
                    pointerValue;
            }
        }
    );

    var ipsZr =
        ipsG.chart.getZr();

    if (ipsZr) {
        ipsZr.off(
            'globalout'
        );

        ipsZr.on(
            'globalout',
            function () {
                ipsActualHoverMs =
                    null;
            }
        );
    }

    $(window)
        .off('resize.ipsg')
        .on('resize.ipsg', function () {
            if (ipsG.chart) {
                ipsG.chart.resize();
            }
        });
}
// ---- Styles for the IPS graph ----
/*
 * Chrome for both graphs (ipsg-* is shared by the PM graph too).
 * Themed with the Aurora --at-* tokens so the panel matches the rest of
 * FRS Advance in dark mode; the token fallbacks keep light mode sane.
 */
function _ipsInjectStyles() {
    if (document.getElementById('ipsgStyles')) return;
    var css =
        '.ipsg-wrap{background:var(--at-bg2,#0e1530);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:14px;box-shadow:0 4px 20px rgba(0,0,0,.22);width:100%;overflow:hidden;}' +
        '.ipsg-head{background:linear-gradient(135deg,rgba(34,211,238,.16),rgba(34,211,238,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));padding:12px 18px;display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:8px;}' +
        '.ipsg-title{color:var(--at-t1,rgba(255,255,255,.96));display:flex;align-items:center;gap:10px;}' +
        '.ipsg-title i{color:var(--at-brand,#22d3ee);}' +
        '.ipsg-title b{font-size:15px;letter-spacing:.01em;}' +
        '.ipsg-sub{color:var(--at-t3,rgba(255,255,255,.50));font-size:12px;margin-top:1px;}' +

        '.ipsg-modebar{background:var(--at-g1,rgba(255,255,255,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));padding:6px 12px;display:flex;justify-content:flex-end;align-items:center;gap:6px;}' +
        '.ipsg-mode-label{font-size:10px;color:var(--at-t3,rgba(255,255,255,.50));font-weight:700;text-transform:uppercase;letter-spacing:.5px;}' +
        '.ipsg-mode-btn{background:var(--at-g1,rgba(255,255,255,.04));color:var(--at-t2,rgba(255,255,255,.72));border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:4px;padding:3px 10px;font-size:10px;font-weight:600;cursor:pointer;}' +
        '.ipsg-mode-btn:hover{background:var(--at-g2,rgba(255,255,255,.06));color:var(--at-t1,#fff);}' +
        '.ipsg-mode-btn.active{background:var(--at-brand,#22d3ee);color:var(--at-bg2,#0e1530);border-color:var(--at-brand,#22d3ee);}' +

        '.ipsg-tb{background:var(--at-g1,rgba(255,255,255,.04));color:var(--at-t2,rgba(255,255,255,.72));border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:4px;padding:4px 10px;font-size:11px;font-weight:700;cursor:pointer;transition:all .2s;letter-spacing:.02em;margin-left:5px;}' +
        '.ipsg-tb:hover{background:var(--at-g2,rgba(255,255,255,.06));color:var(--at-t1,#fff);}' +
        '.ipsg-tb.on{background:var(--at-brand,#22d3ee);color:var(--at-bg2,#0e1530);border-color:var(--at-brand,#22d3ee);}' +

        '.ipsg-selbar{background:var(--at-g1,rgba(255,255,255,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));padding:4px 12px;display:flex;align-items:center;gap:6px;}' +
        '.ipsg-lbl{font-size:10px;color:var(--at-t3,rgba(255,255,255,.50));font-weight:700;text-transform:uppercase;letter-spacing:.5px;}' +
        '.ipsg-sa{background:var(--at-g1,rgba(255,255,255,.04));color:var(--at-t2,rgba(255,255,255,.72));border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:4px;padding:2px 8px;font-size:10px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:3px;transition:all .18s;margin-left:5px;}' +
        '.ipsg-sa:hover{background:var(--at-g2,rgba(255,255,255,.06));border-color:var(--at-brand,#22d3ee);color:var(--at-brand,#22d3ee);}' +
        '.ipsg-count{margin-left:auto;font-size:10px;color:var(--at-t3,rgba(255,255,255,.50));font-weight:600;}' +

        '.ipsg-panel{background:transparent;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));max-height:150px;overflow-y:auto;overflow-x:hidden;}' +
        '.ipsg-panel::-webkit-scrollbar{width:8px;}' +
        '.ipsg-panel::-webkit-scrollbar-thumb{background:var(--at-edge,rgba(255,255,255,.18));border-radius:4px;}' +
        '.ipsg-panel::-webkit-scrollbar-thumb:hover{background:var(--at-t3,rgba(255,255,255,.35));}' +
        '.ipsg-panel::-webkit-scrollbar-track{background:transparent;}' +
        // Series names are "<asset> · <attribute>", which needs far more room
        // than the old 140px track gave — every chip was ellipsised to
        // "SMR-1 · IPS ..." and the value was squeezed against it.
        '.ipsg-attrs{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:4px 6px;padding:6px 8px;}' +
        '.ipsg-attr{display:inline-flex;align-items:center;gap:6px;cursor:pointer;padding:3px 8px;border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:6px;background:var(--at-g1,rgba(255,255,255,.04));user-select:none;transition:border-color .15s,background .15s;min-width:0;min-height:24px;}' +
        '.ipsg-attr:hover{border-color:var(--at-brand,#22d3ee);background:var(--at-g2,rgba(255,255,255,.06));}' +
        '.ipsg-attr input{width:11px;height:11px;accent-color:var(--at-brand,#22d3ee);margin:0;flex-shrink:0;}' +
        '.ipsg-dot{width:7px;height:7px;border-radius:50%;flex-shrink:0;box-shadow:0 0 0 1px rgba(0,0,0,.25);}' +
        '.ipsg-an{font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));font-weight:500;flex:1 1 auto;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.ipsg-val{font-size:11px;font-weight:700;color:var(--at-brand,#22d3ee);font-family:var(--at-font-mono,monospace);font-variant-numeric:tabular-nums;flex:0 0 auto;min-width:44px;text-align:right;}' +

        '.ipsg-toolbar{padding:4px 12px;text-align:right;background:transparent;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));}' +
        '.ipsg-tool{background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:4px;padding:3px 10px;margin-left:5px;cursor:pointer;color:var(--at-t2,rgba(255,255,255,.72));font-size:10px;font-weight:600;transition:all .18s;}' +
        '.ipsg-tool:hover{background:var(--at-g2,rgba(255,255,255,.06));color:var(--at-brand,#22d3ee);}' +

        // Scale with the viewport instead of a hard 500px, so the chart is not
        // a letterbox on a laptop nor wasted space on a control-room display.
        '.ipsg-chart{width:100%;height:clamp(360px,52vh,640px);display:none;background:transparent;}' +
        '.ipsg-load{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;height:180px;color:var(--at-t3,rgba(255,255,255,.50));font-size:12px;font-weight:500;}' +
        '.ipsg-empty{display:none;text-align:center;padding:24px 20px;color:var(--at-t3,rgba(255,255,255,.50));font-size:12px;}' +
        '.ipsg-spin{width:24px;height:24px;border:3px solid var(--at-edge,rgba(255,255,255,.12));border-top-color:var(--at-brand,#22d3ee);border-radius:50%;animation:ipsgspin .7s linear infinite;}' +
        '@keyframes ipsgspin{to{transform:rotate(360deg);}}' +

        // Date inputs sit on the header gradient -- keep them readable in both themes.
        '#ipsgRange label,#pmgRange label{color:var(--at-t2,rgba(255,255,255,.72)) !important;}' +
        '#ipsgFromDate,#ipsgToDate,#pmgFromDate,#pmgToDate{background:var(--at-g1,rgba(255,255,255,.06)) !important;' +
        'border:1px solid var(--at-edge,rgba(255,255,255,.14)) !important;color:var(--at-t1,rgba(255,255,255,.96)) !important;}' +

        'body[data-aurora="light"] .ipsg-wrap{box-shadow:0 2px 12px rgba(0,0,0,.10);}' +

        // Responsive: stack the header controls and give the series chips the
        // full width once the panel is too narrow for two columns.
        '@media (max-width:900px){' +
        '.ipsg-head{flex-direction:column;align-items:stretch;}' +
        '#ipsgRange,#pmgRange{justify-content:flex-start;}' +
        '.ipsg-attrs{grid-template-columns:repeat(auto-fill,minmax(190px,1fr));}' +
        '}' +
        '@media (max-width:600px){' +
        '.ipsg-attrs{grid-template-columns:1fr;}' +
        '.ipsg-modebar{justify-content:flex-start;flex-wrap:wrap;}' +
        '.ipsg-selbar{flex-wrap:wrap;}' +
        '.ipsg-count{margin-left:0;width:100%;}' +
        '.ipsg-chart{height:clamp(300px,60vh,480px);}' +
        '}';
    var st = document.createElement('style');
    st.id = 'ipsgStyles'; st.textContent = css;
    document.head.appendChild(st);
}
// ===================== END IPS GRAPH =====================

// Make sure the IPS graph functions are reachable as globals
// (so the inline onclick="" handlers and fnSearchView can find them).
window.fnBindIpsGraph = fnBindIpsGraph;
window.ipsToggle = ipsToggle;
window.ipsSelectAll = ipsSelectAll;
window.ipsSelectAsset = ipsSelectAsset;
window.ipsSavePng = ipsSavePng;
console.log('[IPS Graph] functions registered. typeof fnBindIpsGraph =', typeof fnBindIpsGraph);





// =====================================================================
// POINT MACHINE GRAPH — History API graph for a single PM asset.
// PM has many attributes per asset (like Track). This plots them on one
// chart with an A End / B End / Both toggle. Reuses the IPS graph helpers
// (ipsAttrName / ipsAssetName / IPS_COLORS) and the ipsg-* CSS.
// State lives in window.pmG.
// =====================================================================
var pmG = window.pmG = {
    hours: 24, assetId: null, endFilter: 'A',
    raw: [], series: {}, order: [], checked: {},
    start: 0, endMs: 0, chart: null, reqId: 0, xhr: null,
    fromMs: 0, toMs: 0,
    tab: 'ind', opType: 'AC', opEvents: [], opSel: -1, opLoaded: false, opChart: null
};

function pmgParseTime(tsDevice, tsLocal) {
    function toMs(ts) {
        if (!ts) return NaN;
        if (String(ts).indexOf('0001') >= 0) return NaN;
        var t = new Date(ts).getTime();
        if (!isNaN(t)) return t;
        var m = String(ts).match(/^(\d{2})(\d{2})(\d{4})_(\d{2})(\d{2})(\d{2})$/);
        if (m) return new Date(+m[3], (+m[2]) - 1, +m[1], +m[4], +m[5], +m[6]).getTime();
        return NaN;
    }
    var d = toMs(tsDevice);
    return !isNaN(d) ? d : toMs(tsLocal);
}

function pmgEnd(name) {
    var n = (name || '').toUpperCase();
    if (n.indexOf('A END') >= 0 || n.indexOf('(A)') >= 0 || n.indexOf('A_') === 0) return 'A';
    if (n.indexOf('B END') >= 0 || n.indexOf('(B)') >= 0 || n.indexOf('B_') === 0) return 'B';
    return '';
}

function _pmgPlotName(name, attrId) {
    var s = (name == null) ? '' : String(name);
    s = s.replace(/<[^>]*>/g, '').replace(/&nbsp;/gi, ' ').replace(/\s+/g, ' ').trim();
    return s || ('Attr ' + attrId);
}

function _pmgVisible(k) {
    var e = pmG.series[k].end;
    return pmG.endFilter === 'Both' || !e || e === pmG.endFilter;
}

function fnBindPmGraph() {
    var siteId = $('#drpSite').val();
    if (!siteId || siteId === '0') { showWarning('Please select a site', 'Validation'); return; }
    var ids = (getSelectedAssetIds() || []).filter(function (v) { return v && v !== '0'; });
    if (ids.length === 0) { showWarning('Please select an asset', 'Validation'); return; }

    pmG.assetId = ids[0];   // PM graph is single-asset
    _pmgShell();
    loadUserAssetInfo(siteId, function () { _pmgLoad(); });

    // Bind live values above the chart labels from the WebSocket (same as the
    // Signal/Track graphs). The Graph branch disconnected WS, so reconnect this
    // single PM asset; the onopen handler leaves the Graph DOM untouched.
    try {
        connectWebSocket(siteId, $('#drpAssetType').val(), [pmG.assetId]);
        pmgUpdateLiveStrip();
    } catch (ePmgWs) { console.warn('[PMG] live WS connect failed', ePmgWs); }
}

function _pmgShell() {
    pmG.tab = 'ind'; pmG.opLoaded = false; pmG.opSel = -1; pmG.opEvents = [];
    if (typeof _ipsInjectStyles === 'function') _ipsInjectStyles();
    if (!document.getElementById('pmgStyles')) {
        var st = document.createElement('style');
        st.id = 'pmgStyles';
        st.textContent = '.pmg-end.on{background:var(--at-brand,#22d3ee);color:var(--at-bg2,#0e1530);border-color:var(--at-brand,#22d3ee);}' +
            '.pmg-optype.on{background:var(--at-brand,#22d3ee);color:var(--at-bg2,#0e1530);border-color:var(--at-brand,#22d3ee);}' +
            '.pmg-val{font-size:11px;font-weight:700;color:var(--at-brand,#22d3ee);margin-left:4px;flex-shrink:0;}' +
            '.pmg-tabs{border-bottom:2px solid var(--at-edge,rgba(255,255,255,.10));}' +
            '.pmg-tab-btn{border:none;background:none;padding:9px 18px;font-size:13px;font-weight:700;cursor:pointer;margin-bottom:-2px;' +
            'color:var(--at-t3,rgba(255,255,255,.50));border-bottom:3px solid transparent;}' +
            '.pmg-tab-btn.on{color:var(--at-brand,#22d3ee);border-bottom-color:var(--at-brand,#22d3ee);}' +
            '.pmg-hint{font-size:12px;color:var(--at-t3,rgba(255,255,255,.50));margin:2px 0 8px;}' +
            '.pmg-pane{background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:10px;}' +
            '.pmg-pane-head{padding:11px 14px;border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));font-size:13px;font-weight:700;color:var(--at-brand,#22d3ee);}' +
            '.pmg-muted{color:var(--at-t3,rgba(255,255,255,.50));font-weight:500;}' +
            '.pmg-opsel{font-size:13px;color:var(--at-t2,rgba(255,255,255,.72));font-weight:600;min-height:18px;}' +
            '.pmg-opempty{text-align:center;padding:50px;color:var(--at-t3,rgba(255,255,255,.50));}' +
            /* operation events list: neutral text, colour only on the direction bar + badge */
            '.pmg-ev{position:relative;padding:8px 10px 8px 14px;border-radius:8px;cursor:pointer;margin-bottom:5px;border:1px solid var(--at-edge,rgba(255,255,255,.10));background:var(--at-g1,rgba(255,255,255,.03));transition:background .12s,border-color .12s;}' +
            '.pmg-ev::before{content:"";position:absolute;left:5px;top:8px;bottom:8px;width:3px;border-radius:2px;background:#22C55E;}' +
            '.pmg-ev.rev::before{background:#F59E0B;}' +
            '.pmg-ev:hover{border-color:rgba(100,116,139,.45);}' +
            '.pmg-ev.on{border-color:#0EA5B7;background:rgba(14,165,183,.10);box-shadow:0 0 0 1px #0EA5B7 inset;}' +
            '.pmg-ev-top{display:flex;align-items:center;justify-content:space-between;gap:6px;}' +
            '.pmg-ev-time{font-size:13px;font-weight:700;color:var(--at-t1,#fff);font-variant-numeric:tabular-nums;letter-spacing:.01em;}' +
            '.pmg-ev-date{font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));margin-top:1px;}' +
            '.pmg-ev-av{display:flex;flex-wrap:wrap;gap:3px;margin-top:5px;}' +
            '.pmg-ev-av span{font-size:10px;font-weight:600;padding:0 5px;border-radius:4px;border:1px solid var(--at-edge,rgba(255,255,255,.10));color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.pmg-dir{display:inline-block;font-size:10px;font-weight:700;padding:1px 8px;border-radius:999px;}' +
            '.pmg-dir.nor{background:rgba(34,197,94,.16);color:#86EFAC;} .pmg-dir.rev{background:rgba(245,158,11,.18);color:#FCD34D;}' +
            'body[data-aurora="light"] .pmg-dir.nor{background:#DCFCE7;color:#166534;} body[data-aurora="light"] .pmg-dir.rev{background:#FEF3C7;color:#92400E;}' +
            'body[data-aurora="light"] .pmg-ev{background:#fff;} body[data-aurora="light"] .pmg-ev.on{background:#ECFEFF;border-color:#0891B2;box-shadow:0 0 0 1px #0891B2 inset;}';
        document.head.appendChild(st);
    }

    var siteName = ($('#drpSite option:selected').text() || '').trim();
    var title = ipsAssetName(pmG.assetId);

    var h = '<div class="ipsg-wrap">';
    h += '<div class="ipsg-head"><div class="ipsg-title"><i class="fas fa-chart-line"></i><div><b>' + title + '</b>' +
        '<div class="ipsg-sub">' + siteName + ' · Point Machine Graph</div></div></div>';
    h += '<div id="pmgRange" style="display:flex;align-items:center;gap:8px;flex-wrap:wrap;">' +
        '<label style="font-size:12px;font-weight:600;color:#fff;margin:0;">From Date:</label>' +
        '<input type="datetime-local" id="pmgFromDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:4px 8px;color:rgba(255,255,255,0.96);background:rgba(255,255,255,0.04);outline:none;"/>' +
        '<label style="font-size:12px;font-weight:600;color:#fff;margin:0;">To Date:</label>' +
        '<input type="datetime-local" id="pmgToDate" style="font-size:12px;border:1px solid rgba(255,255,255,0.14);border-radius:6px;padding:4px 8px;color:rgba(255,255,255,0.96);background:rgba(255,255,255,0.04);outline:none;"/>' +
        '<button id="pmgLoadBtn" class="ipsg-tb on" style="cursor:pointer;"><i class="fas fa-sync-alt"></i> Load</button>' +
        '</div></div>';

    h += '<div class="pmg-tabs" style="display:flex;gap:6px;margin:2px 0 10px;">' +
        '<button type="button" id="pmgTabBtnInd" class="pmg-tab-btn on" onclick="pmgSwitchTab(\'ind\')"><i class="fas fa-wave-square"></i> Indication</button>' +
        '<button type="button" id="pmgTabBtnOp" class="pmg-tab-btn" onclick="pmgSwitchTab(\'op\')"><i class="fas fa-bolt"></i> Operation Event</button>' +
        '</div>';
    h += '<div id="pmgTabInd">';
    h += '<div class="pmg-hint"><i class="fas fa-info-circle"></i> Indication voltages - A End & B End</div>';
    h += '<div class="ipsg-toolbar">' +
        '<button class="ipsg-tool" onclick="if(pmG.chart)pmG.chart.dispatchAction({type:\'restore\'})"><i class="fas fa-undo"></i> Reset Zoom</button>' +
        '<button class="ipsg-tool" onclick="pmgSavePng()"><i class="fas fa-download"></i> Save PNG</button></div>';
    h += '<div id="pmgLoad" class="ipsg-load"><div class="ipsg-spin"></div> Loading telemetry data…</div>';
    h += '<div id="pmgLiveStrip" style="display:flex;flex-wrap:wrap;gap:8px;margin:0 0 10px;"></div>';
    h += '<div id="pmgChart" class="ipsg-chart"></div>';
    h += '<div id="pmgEmpty" class="ipsg-empty">No data available for this time range</div>';
    h += '</div>';
    h += '</div>';
    h += '<div id="pmgTabOp" style="display:none;">';
    h += '<div class="ipsg-selbar">' +
        '<span class="ipsg-lbl">Signal:</span>' +
        '<button class="ipsg-sa pmg-optype on" data-type="AC" onclick="pmgSetOpType(\'AC\')">A Current</button>' +
        '<button class="ipsg-sa pmg-optype" data-type="AV" onclick="pmgSetOpType(\'AV\')">A Voltage</button>' +
        '<button class="ipsg-sa pmg-optype" data-type="BC" onclick="pmgSetOpType(\'BC\')">B Current</button>' +
        '<button class="ipsg-sa pmg-optype" data-type="BV" onclick="pmgSetOpType(\'BV\')">B Voltage</button>' +
        '<span id="pmgOpCount" class="ipsg-count"></span></div>';
    h += '<div style="display:flex;gap:14px;align-items:stretch;margin-top:8px;">';
    h += '<div class="pmg-pane" style="flex:0 0 240px;display:flex;flex-direction:column;max-height:520px;">' +
        '<div class="pmg-pane-head"><i class="fas fa-clock"></i> Operation Events <span id="pmgOpListCount" class="pmg-muted"></span></div>' +
        '<div id="pmgOpList" style="overflow-y:auto;flex:1;padding:6px;"></div>' +
        '</div>';
    h += '<div class="pmg-pane" style="flex:1;min-width:0;padding:14px;">' +
        '<div style="display:flex;align-items:flex-start;justify-content:space-between;gap:10px;flex-wrap:wrap;margin-bottom:8px;">' +
        '<div id="pmgOpSel" class="pmg-opsel"></div>' +
        '<div id="pmgOpStats" style="display:none;"></div>' +
        '</div>' +
        '<div id="pmgOpLoad" class="ipsg-load" style="display:none;"><div class="ipsg-spin"></div> Loading operation data...</div>' +
        '<div id="pmgOpChart" style="width:100%;height:480px;display:none;"></div>' +
        '<div id="pmgOpEmpty" class="pmg-opempty"><i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;opacity:.5;"></i>Select a date-time from the list to view its operation waveform.</div>' +
        '</div>';
    h += '</div>';
    h += '</div>';

    $('#divTelemetryLive').empty().append(h);

    function _pmgFmtLocal(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0') + 'T' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); }
    (function () {
        var now = new Date();
        var midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0);
        if (!pmG.fromMs) pmG.fromMs = midnight.getTime();
        if (!pmG.toMs) pmG.toMs = now.getTime();
        $('#pmgFromDate').val(_pmgFmtLocal(new Date(pmG.fromMs)));
        $('#pmgToDate').val(_pmgFmtLocal(new Date(pmG.toMs)));
    })();

    // Block future dates: live max attr + clamp typed values back to "now".
    function _pmgRefreshMax() {
        var nowStr = _pmgFmtLocal(new Date());
        $('#pmgFromDate').attr('max', nowStr);
        $('#pmgToDate').attr('max', nowStr);
    }
    _pmgRefreshMax();
    if (window._pmgMaxTimer) clearInterval(window._pmgMaxTimer);
    window._pmgMaxTimer = setInterval(_pmgRefreshMax, 30000);

    function _pmgClampFuture(sel) {
        var el = $(sel), v = el.val();
        if (!v) return;
        var d = new Date(v), now = new Date();
        if (!isNaN(d.getTime()) && d > now) { el.val(_pmgFmtLocal(now)); }
    }
    $('#pmgFromDate').off('change.pmgfut blur.pmgfut').on('change.pmgfut blur.pmgfut', function () { _pmgClampFuture('#pmgFromDate'); });
    $('#pmgToDate').off('change.pmgfut blur.pmgfut').on('change.pmgfut blur.pmgfut', function () { _pmgClampFuture('#pmgToDate'); });

    $('#pmgLoadBtn').off('click').on('click', function () {
        var fv = $('#pmgFromDate').val(), tv = $('#pmgToDate').val();
        var fd = fv ? new Date(fv) : null, td = tv ? new Date(tv) : null;
        if (!fd || !td || isNaN(fd.getTime()) || isNaN(td.getTime())) { showWarning('Please enter valid From and To dates.', 'Validation'); return; }
        // Reject future dates even if typed past the clamp.
        var _now = new Date();
        if (fd > _now || td > _now) { showWarning('Future dates are not allowed.', 'Validation'); return; }
        if (fd >= td) { showWarning('From date must be before To date.', 'Validation'); return; }
        pmG.fromMs = fd.getTime(); pmG.toMs = td.getTime();
        if (pmG.tab === 'op') { pmG.opLoaded = false; pmgLoadOpEvents(); } else { _pmgLoad(); }
    });
}

function _pmgLoad() {
    $('#pmgLoad').show(); $('#pmgChart').hide(); $('#pmgEmpty').hide();

    var _nowMs = Date.now();
    pmG.endMs = pmG.toMs || _nowMs;
    pmG.start = pmG.fromMs || (pmG.endMs - 24 * 3600000);
    var s = new Date(pmG.start), e = new Date(pmG.endMs);

    var myReq = ++pmG.reqId;
    if (pmG.xhr) { try { pmG.xhr.abort(); } catch (x) { } }

    var url = HISTORY_API_BASE + '?assetId=' + pmG.assetId +
        '&startDate=' + formatDateForHistoryApi(s) + '&endDate=' + formatDateForHistoryApi(e);

    pmG.xhr = $.ajax({ url: url, type: 'GET', dataType: 'json', timeout: 60000 });
    pmG.xhr.then(function (r) {
        if (myReq === pmG.reqId) {
            pmG.raw = _pmgExtractRows(r);
        }
    }, function () {
        if (myReq === pmG.reqId) {
            pmG.raw = [];
        }
    }).always(function () {
        if (myReq !== pmG.reqId) return;
        $('#pmgLoad').hide();
        pmgRenderIndicationBoth(pmG.raw, pmG.start, pmG.endMs);
    });
}

function _pmgBuildSeries() {
    pmG.series = {}; pmG.order = []; var ci = 0;
    (pmG.raw || []).forEach(function (at) {
        if (!at.Values) return;
        var fk = Object.keys(at.Values)[0];
        var first = at.Values['1'] || at.Values[fk];
        var dt = first ? first.DataType : '';

        var points = [], isBin = true;
        for (var k in at.Values) {
            var en = at.Values[k];
            if (!en || !en.Timestamp || en.Value === undefined || en.Value === null) continue;
            var t = pmgParseTime(en.Timestamp.TimestampDevice, en.Timestamp.TimestampLocal);
            var v = parseFloat(en.Value);
            if (isNaN(t) || isNaN(v) || t <= 0) continue;
            points.push([t, tlZeroFloor(v)]);
            if (v !== 0 && v !== 1) isBin = false;
        }
        if (points.length === 0) return;
        points.sort(function (a, b) { return a[0] - b[0]; });

        var rawName = at.AttributeName || at.AttributeTitle || at.AliasName || at.Title ||
            at.Name || (first && (first.AttributeName || first.AliasName || first.Title)) || null;
        var nm = _pmgPlotName(ipsAttrName(pmG.assetId, at.AttributeId, dt, rawName), at.AttributeId);
        var key = pmG.assetId + '||' + at.AttributeId;

        // Show only real attributes of the selected asset: an id that still resolves to
        // "Attr <id>" and is not a known PM operation code isn't a configured attribute — skip it.
        if (pmG.hideUnknown !== false && /^Attr\s*\d+$/i.test(nm) &&
            !(typeof isPmOperationId === 'function' && isPmOperationId(at.AttributeId))) {
            return;
        }

        pmG.series[key] = {
            attrId: at.AttributeId, name: nm, end: pmgEnd(nm),
            color: IPS_COLORS[ci % IPS_COLORS.length], points: points, isBin: isBin
        };
        pmG.series[key] = {
            attrId: at.AttributeId, name: nm, end: pmgEnd(nm),
            color: IPS_COLORS[ci % IPS_COLORS.length], points: points, isBin: isBin
        };
        ci++; pmG.order.push(key);
        if (pmG.checked[key] === undefined) pmG.checked[key] = true;
    });
    Object.keys(pmG.checked).forEach(function (k) { if (!pmG.series[k]) delete pmG.checked[k]; });
}

function _pmgPanel() {
    var html = '<div class="ipsg-attrs">';
    pmG.order.forEach(function (k) {
        if (!_pmgVisible(k)) return;
        var sv = pmG.series[k], on = pmG.checked[k] !== false;
        var last = sv.points.length ? sv.points[sv.points.length - 1][1] : null;
        var val = (last == null) ? '—' : tlZeroFloor(Number(last)).toFixed(2);
        html += '<label class="ipsg-attr" title="' + sv.name + '">' +
            '<input type="checkbox" data-key="' + k + '" ' + (on ? 'checked' : '') + ' onchange="pmgToggle(this)">' +
            '<span class="ipsg-dot" style="background:' + sv.color + '"></span>' +
            '<span class="ipsg-an">' + sv.name + '</span>' +
            '<span class="pmg-val">' + val + '</span></label>';
    });
    html += '</div>';
    $('#pmgPanel').html(html);
    _pmgCount();
}

function _pmgCount() {
    var t = 0, on = 0;
    pmG.order.forEach(function (k) {
        if (_pmgVisible(k)) { t++; if (pmG.checked[k] !== false) on++; }
    });
    $('#pmgCount').text(on + ' / ' + t + ' shown');
}

function pmgToggle(el) { pmG.checked[$(el).data('key')] = el.checked; _pmgCount(); _pmgRender(); }
function pmgSelectAll(on) {
    pmG.order.forEach(function (k) { if (_pmgVisible(k)) pmG.checked[k] = on; });
    $('#pmgPanel input[type=checkbox]').prop('checked', on);
    _pmgCount(); _pmgRender();
}
function pmgSetEnd(end) {
    pmG.endFilter = end;
    $('.pmg-end').removeClass('on');
    $('.pmg-end[data-end="' + end + '"]').addClass('on');
    _pmgPanel(); _pmgRender();
}
function pmgSavePng() {
    if (!pmG.chart) return;
    var u = pmG.chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
    var a = document.createElement('a'); a.href = u; a.download = 'pm_graph.png'; a.click();
}

// Live values above the labels — refresh from the WebSocket. Mirrors the
// Signal/Track graph's _gUpdatePanelValuesOnlyFromWebSocket. The ONLY DOM
// mutation is the strip text. Each indication is resolved by AttributeId AND by
// the live alias name, because the WS stream may key indications either way —
// the same lookup the proven updateCircuitPointMachine() uses.
var PMG_LIVE_NAMES = {
    25: ['A End - NWKR', 'NWKR A End', 'A_NWKR'],
    26: ['A End - RWKR', 'RWKR A End', 'A_RWKR'],
    27: ['B End - NWKR', 'NWKR B End', 'B_NWKR'],
    28: ['B End - RWKR', 'RWKR B End', 'B_RWKR'],
    576: ['A End - NWKR (Loc)'],
    577: ['A End - RWKR (Loc)'],
    578: ['B End - NWKR (Loc)'],
    579: ['B End - RWKR (Loc)']
};
function pmgUpdateLiveStrip() {
    var host = document.getElementById('pmgLiveStrip');
    if (!host || typeof pmG === 'undefined' || !pmG.assetId) return;
    if (typeof PMG_IND_META === 'undefined' || typeof PMG_IND_ORDER === 'undefined') return;

    var asset = (typeof wsLiveData !== 'undefined') ? wsLiveData[pmG.assetId] : null;
    var attrs = (asset && asset.attrs) ? asset.attrs : {};

    // Index live attrs by id and by lowercased name for robust resolution.
    var byId = {}, byName = {};
    for (var nm in attrs) {
        if (!attrs.hasOwnProperty(nm)) continue;
        var a = attrs[nm]; if (!a) continue;
        byName[String(nm).toLowerCase()] = a;
        var aid = parseInt(a.AttrId != null ? a.AttrId : a.AssetAttributeId, 10);
        if (isNaN(aid)) continue;
        var prev = byId[aid];
        if (!prev) { byId[aid] = a; continue; }
        var pt = prev.TimestampDevice ? new Date(prev.TimestampDevice).getTime() : 0;
        var nt = a.TimestampDevice ? new Date(a.TimestampDevice).getTime() : 0;
        if (nt >= pt) byId[aid] = a;
    }

    function resolve(id) {
        if (byId[id]) return byId[id];
        var names = PMG_LIVE_NAMES[id] || [];
        for (var i = 0; i < names.length; i++) {
            var hit = byName[names[i].toLowerCase()];
            if (hit) return hit;
        }
        return null;
    }

    var html = '';
    PMG_IND_ORDER.forEach(function (id) {
        var meta = PMG_IND_META[id];
        var a = resolve(id);
        var val = '\u2014';
        if (a && a.Value !== undefined && a.Value !== null && a.Value !== '') {
            var v = parseFloat(a.Value);
            val = isNaN(v) ? String(a.Value) : tlZeroFloor(v).toFixed(2);
        }
        html += '<div style="flex:1 1 150px;min-width:140px;border:1px solid rgba(255,255,255,0.10);border-radius:8px;padding:6px 10px;background:rgba(255,255,255,0.04);">' +
            '<div style="display:flex;align-items:center;gap:6px;font-size:10px;font-weight:700;color:rgba(255,255,255,0.72);">' +
            '<span style="width:9px;height:9px;border-radius:50%;background:' + meta.color + ';flex-shrink:0;"></span>' + _pmgIndName(id) + '</div>' +
            '<div style="font-size:16px;font-weight:800;color:#0f172a;margin-top:2px;">' + val +
            '<span style="font-size:10px;font-weight:600;color:rgba(255,255,255,0.50);margin-left:3px;">V</span></div>' +
            '</div>';
    });
    host.innerHTML = html;
}
window.pmgUpdateLiveStrip = pmgUpdateLiveStrip;

function _pmgRender() {
    var el = document.getElementById('pmgChart');
    if (!el || typeof echarts === 'undefined') return;

    var sr = [], legends = [], used = {}, hasBin = false;
    pmG.order.forEach(function (k) {
        if (pmG.checked[k] === false || !_pmgVisible(k)) return;
        var sv = pmG.series[k];
        var nm = sv.name;
        if (used[nm]) {
            nm = sv.end ? (nm + ' (' + sv.end + ')') : (nm + ' #' + sv.attrId);
            if (used[nm]) nm = sv.name + ' #' + sv.attrId;
        }
        used[nm] = 1;
        var dense = sv.points.length > 30;
        if (sv.isBin) hasBin = true;
        legends.push(nm);
        sr.push({
            name: nm, type: 'line', smooth: false,
            yAxisIndex: sv.isBin ? 1 : 0, step: 'end',
            symbol: 'circle', symbolSize: 4, showSymbol: !dense,
            sampling: dense ? 'lttb' : undefined,
            lineStyle: { width: 1.5, color: sv.color, opacity: sv.isBin ? 0.6 : 0.9 },
            itemStyle: { color: sv.color },
            emphasis: { disabled: true },
            blur: { lineStyle: { opacity: sv.isBin ? 0.6 : 0.9 }, itemStyle: { opacity: 1 } },
            data: sv.points
        });
    });

    if (pmG.chart) { pmG.chart.dispose(); pmG.chart = null; }
    if (sr.length === 0) { $('#pmgChart').hide(); $('#pmgEmpty').show(); return; }
    $('#pmgEmpty').hide(); $('#pmgChart').show();

    var yAxes = [
        {
            type: 'value', name: 'Value', position: 'left', scale: true,
            nameTextStyle: { color: 'rgba(255,255,255,0.50)' },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            axisLabel: { color: 'rgba(255,255,255,0.50)' },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        }
    ];
    if (hasBin) {
        yAxes.push({
            type: 'value', name: '0 / 1', position: 'right', min: 0, max: 1, interval: 1,
            nameTextStyle: { color: 'rgba(255,255,255,0.50)' },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            axisLabel: {
                color: 'rgba(255,255,255,0.50)',
                formatter: function (v) { return v === 1 ? 'On' : (v === 0 ? 'Off' : ''); }
            },
            splitLine: { show: false }
        });
    }

    pmG.chart = echarts.init(el);
    pmG.chart.setOption({
        backgroundColor: 'transparent', animation: false, color: IPS_COLORS,
        textStyle: { color: 'rgba(255,255,255,0.72)' },
        stateAnimation: { duration: 0 },
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross', label: { backgroundColor: 'rgba(34,211,238,0.80)' } },
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(34,211,238,0.30)',
            borderWidth: 1,
            textStyle: { color: 'rgba(255,255,255,0.86)' },
            formatter: function (ps) {
                if (!ps || !ps.length) return '';
                var d = new Date(ps[0].value[0]);
                var t = String(d.getHours()).padStart(2, '0') + ':' +
                    String(d.getMinutes()).padStart(2, '0') + ':' +
                    String(d.getSeconds()).padStart(2, '0');
                var rows = ps.slice().sort(function (a, b) { return (b.value[1] || 0) - (a.value[1] || 0); });
                var html = '<div style="font-weight:700;color:#22d3ee;margin-bottom:4px;">' + t + '</div>';
                rows.forEach(function (p) {
                    var v = (p.value && p.value[1] != null) ? Number(p.value[1]).toFixed(2) : '—';
                    html += '<div style="display:flex;align-items:center;gap:6px;">' +
                        '<span style="width:9px;height:9px;border-radius:50%;background:' + p.color + ';flex-shrink:0;"></span>' +
                        '<span style="flex:1;white-space:nowrap;">' + p.seriesName + '</span>' +
                        '<b style="margin-left:10px;">' + v + '</b></div>';
                });
                return html;
            }
        },
        legend: { type: 'scroll', data: legends, top: 6, textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.72)' } },
        grid: { left: 56, right: hasBin ? 56 : 24, top: 50, bottom: 56, containLabel: true },
        xAxis: {
            type: 'time', min: pmG.start, max: pmG.endMs,
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)' } },
            axisLabel: {
                color: 'rgba(255,255,255,0.50)',
                formatter: function (v) {
                    var d = new Date(v);
                    return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
                }
            }
        },
        yAxis: yAxes,
        dataZoom: [{
            type: 'slider', height: 34, bottom: 4,
            borderColor: 'rgba(255,255,255,0.14)', backgroundColor: (typeof tlTc === 'function' ? tlTc('rgba(15,23,42,0.70)', 'rgba(241,245,249,0.95)') : 'rgba(15,23,42,0.70)'),
            fillerColor: 'rgba(34,211,238,0.20)',
            handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' },
            textStyle: { fontSize: 10, color: 'rgba(255,255,255,0.50)' }
        }, { type: 'inside' }],
        series: sr
    });

    $(window).off('resize.pmg').on('resize.pmg', function () { if (pmG.chart) pmG.chart.resize(); });
}

// ---------------------------------------------------------------------
// POINT MACHINE GRAPH - INDICATION TAB (A + B ends, correct nomenclature)
// Names come from PM_INDICATION_NAMES, qualified by end. The tooltip always
// lists every configured attribute; any without a value at the hovered time
// shows 0.00 V. Range is controlled by the From / To date pickers.
// ---------------------------------------------------------------------
var PMG_IND_META = {
    25: { end: 'A', color: '#2563eb', loc: false },
    576: { end: 'A', color: '#7c3aed', loc: true },
    26: { end: 'A', color: '#059669', loc: false },
    577: { end: 'A', color: '#0891b2', loc: true },
    27: { end: 'B', color: '#dc2626', loc: false },
    578: { end: 'B', color: '#d97706', loc: true },
    28: { end: 'B', color: '#db2777', loc: false },
    579: { end: 'B', color: '#6366f1', loc: true }
};
var PMG_IND_ORDER = [25, 576, 26, 577, 27, 578, 28, 579];

function _pmgIndName(id) {
    var base = (typeof PM_INDICATION_NAMES !== 'undefined' && PM_INDICATION_NAMES[id]) ? PM_INDICATION_NAMES[id] : ('Attr ' + id);
    return PMG_IND_META[id].end + ' End - ' + base;
}

// Normalize Point Machine history response exactly before plotting.
function _pmgAttrId(row) {
    if (!row) return 0;

    var raw = (row.AttributeId !== undefined &&
        row.AttributeId !== null)
        ? row.AttributeId
        : row.AssetAttributeId;

    var id = parseInt(raw, 10);
    return isNaN(id) ? 0 : id;
}

function _pmgEntryTime(entry) {
    if (!entry) return NaN;

    var timestamp = entry.Timestamp || {};

    return pmgParseTime(
        timestamp.TimestampDevice || entry.TimestampDevice,
        timestamp.TimestampLocal || entry.TimestampLocal
    );
}

function _pmgExtractRows(response) {
    if (!response) return [];

    var rows = Array.isArray(response)
        ? response
        : (
            response.Data ||
            response.data ||
            response.Result ||
            response.result ||
            []
        );

    if (!Array.isArray(rows) && rows) {
        rows =
            rows.Data ||
            rows.data ||
            rows.attributes ||
            rows.Attributes ||
            [];
    }

    // Support an optional single-asset response envelope.
    if (Array.isArray(rows) &&
        rows.length === 1 &&
        rows[0] &&
        rows[0].AttributeId === undefined) {

        var nestedRows =
            rows[0].attributes ||
            rows[0].Attributes ||
            rows[0].Data ||
            rows[0].data;

        if (Array.isArray(nestedRows)) {
            rows = nestedRows;
        }
    }

    return Array.isArray(rows) ? rows : [];
}

function pmgRenderIndicationBoth(data, startMs, endMs) {
    var el = document.getElementById('pmgChart');
    if (!el) return;
    data = data || [];

    var attrDataMap = {}, allTs = {};
    PMG_IND_ORDER.forEach(function (id) {
        for (var i = 0; i < data.length; i++) {
            if (_pmgAttrId(data[i]) === id) {
                attrDataMap[id] = data[i];
                if (data[i].Values) {
                    for (var k in data[i].Values) {
                        var en = data[i].Values[k];
                        if (!en || !en.Timestamp || en.Value === undefined) continue;
                        var t = _pmgEntryTime(en);

                        if (isNaN(t) || t <= 0) continue;
                        if (endMs && t > endMs) continue;

                        if (startMs && t < startMs) {
                            t = startMs;
                        }

                        allTs[t] = true;
                    }
                }
                break;
            }
        }
    });
    var sortedTs = Object.keys(allTs).map(function (x) { return parseInt(x); }).sort(function (a, b) { return a - b; });

    var series = [], legends = [], colorByName = {};
    PMG_IND_ORDER.forEach(function (id) {
        var ad = attrDataMap[id];
        if (!ad) return;
        var meta = PMG_IND_META[id];
        var label = _pmgIndName(id);
        var valueMap = {}, orig = {};
        if (ad.Values) {
            for (var k in ad.Values) {
                var en = ad.Values[k];
                if (!en || !en.Timestamp || en.Value === undefined) continue;
                var t = _pmgEntryTime(en);

                if (isNaN(t) || t <= 0) continue;
                if (endMs && t > endMs) continue;

                var isB = startMs && t < startMs;

                if (isB) {
                    t = startMs;
                }

                var v = parseFloat(en.Value);
                if (!isNaN(v)) {
                    if (!valueMap[t]) {
                        valueMap[t] = [];
                    }

                    if (isB) {
                        // Only one carry-forward boundary value is required.
                        valueMap[t][0] = v;
                    } else {
                        // Preserve every value with the same TimestampDevice.
                        valueMap[t].push(v);
                        orig[t] = true;
                    }
                }
            }
        }
        var points = [], last = null;

        sortedTs.forEach(function (t) {
            var valuesAtTime = valueMap[t];

            if (valuesAtTime && valuesAtTime.length) {
                for (var valueIndex = 0; valueIndex < valuesAtTime.length; valueIndex++) {
                    last = valuesAtTime[valueIndex];

                    points.push({
                        value: [t, last],
                        symbol: orig[t] ? 'circle' : 'none',
                        symbolSize: orig[t] ? 7 : 0
                    });
                }

                return;
            }

            if (last !== null) {
                points.push({
                    value: [t, last],
                    symbol: 'none',
                    symbolSize: 0
                });
            }
        });
        if (!points.length) return;
        if (last !== null && endMs) {
            var lp = points[points.length - 1].value[0];
            if (lp < endMs) points.push({ value: [endMs, last], symbol: 'none', symbolSize: 0 });
        }
        legends.push(label); colorByName[label] = meta.color;
        series.push({
            name: label, type: 'line', step: 'end', smooth: false,
            connectNulls: true, showSymbol: false, clip: false,
            lineStyle: { width: meta.loc ? 1.8 : 2.4, color: meta.color, type: 'solid', opacity: 0.95 },
            itemStyle: { color: meta.color },
            emphasis: { disabled: true },
            blur: { lineStyle: { opacity: 0.95 }, itemStyle: { opacity: 1 } },
            data: points
        });
    });

    //// Keep an immutable copy of the complete series data. The zoom handler
    //// uses this source to insert a carry-forward point at the visible left edge
    //// without permanently trimming or mutating the full history range.
    //var baseSeriesData = {};
    //var maxY = 0;

    //for (var baseIndex = 0; baseIndex < series.length; baseIndex++) {
    //    var baseSeries = series[baseIndex];
    //    var baseSeriesId = 'pmg_ind_' + baseIndex;

    //    baseSeries.id = baseSeriesId;
    //    baseSeriesData[baseSeriesId] = baseSeries.data.slice();

    //    for (
    //        var basePointIndex = 0;
    //        basePointIndex < baseSeries.data.length;
    //        basePointIndex++
    //    ) {
    //        var basePoint = baseSeries.data[basePointIndex];
    //        var basePair = basePoint && basePoint.value
    //            ? basePoint.value
    //            : basePoint;

    //        if (!basePair || basePair.length < 2) continue;

    //        var baseValue = parseFloat(basePair[1]);

    //        if (!isNaN(baseValue) && baseValue > maxY) {
    //            maxY = baseValue;
    //        }
    //    }
    //}

    //// Fix the Y-axis against the complete dataset rather than the zoomed subset.
    //var fixedYMax = maxY > 0
    //    ? Math.max(30, Math.ceil(maxY * 1.15))
    //    : 30;

    //if (pmG.chart) {
    //    pmG.chart.dispose();
    //    pmG.chart = null;
    //}

    //if (!series.length) {
    //    $('#pmgChart').hide();
    //    $('#pmgEmpty').show();
    //    return;
    //}
    //$('#pmgEmpty').hide(); $('#pmgChart').show();



    if (pmG.chart) { pmG.chart.dispose(); pmG.chart = null; }

    if (!series.length) {
        $('#pmgChart').hide();
        $('#pmgEmpty').show();
        return;
    }

    $('#pmgEmpty').hide();
    $('#pmgChart').show();




    pmG.chart = echarts.init(el);
    pmG.chart.setOption({
        backgroundColor: 'transparent',
        animation: false,
        textStyle: { color: 'rgba(255,255,255,0.72)' },
        stateAnimation: { duration: 0 },
        tooltip: {
            trigger: 'axis',
            axisPointer: { type: 'cross', label: { backgroundColor: 'rgba(34,211,238,0.80)' } },
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: 'rgba(34,211,238,0.30)',
            borderWidth: 1,
            textStyle: { color: 'rgba(255,255,255,0.86)' },
            formatter: function (ps) {
                if (!ps || !ps.length) return '';
                var d = new Date(ps[0].value[0]);
                var t = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
                var dateStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear();
                var vmap = {};
                ps.forEach(function (p) { if (p.value && p.value[1] != null) vmap[p.seriesName] = p.value[1]; });
                var html = '<div style="font-weight:700;color:#22d3ee;margin-bottom:6px;border-bottom:1px solid rgba(255,255,255,0.10);padding-bottom:4px;">' + dateStr + ' ' + t + '</div>';
                PMG_IND_ORDER.forEach(function (id) {
                    var nm = _pmgIndName(id);
                    if (vmap[nm] == null) return;   // legend-unselected/hidden → omit from tooltip
                    var val = Number(tlZeroFloor(vmap[nm])).toFixed(2);
                    html += '<div style="display:flex;align-items:center;gap:6px;padding:2px 0;">' +
                        '<span style="width:9px;height:9px;border-radius:50%;background:' + PMG_IND_META[id].color + ';flex-shrink:0;"></span>' +
                        '<span style="flex:1;white-space:nowrap;">' + nm + '</span>' +
                        '<b style="margin-left:12px;">' + val + ' V</b></div>';
                });
                return html;
            }
        },
        legend: {
            type: 'plain', data: legends, top: 8, left: 'center', width: '96%',
            icon: 'roundRect', itemGap: 18, itemWidth: 22, itemHeight: 8,
            padding: [4, 8], textStyle: { fontSize: 11, color: 'rgba(255,255,255,0.72)', padding: [0, 2, 0, 0] }
        },
        grid: { left: 56, right: 24, top: 96, bottom: 60, containLabel: true },
        xAxis: {
            type: 'time', min: startMs, max: endMs,
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            axisLabel: {
                color: 'rgba(255,255,255,0.50)',
                formatter: function (v) { var d = new Date(v); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); }
            },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        },
        yAxis: {
            type: 'value',
            name: 'Voltage (V)',
            scale: true,
            nameTextStyle: { color: 'rgba(255,255,255,0.50)' },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.16)' } },
            axisLabel: { color: 'rgba(255,255,255,0.50)' },
            splitLine: {
                show: true,
                lineStyle: {
                    color: 'rgba(255,255,255,0.06)',
                    type: 'dashed'
                }
            }
        },

        // Preserve the complete step-line data while zooming.
        // This prevents ECharts from removing the point immediately
        // before the visible range and breaking the horizontal line.
        dataZoom: [
            {
                type: 'slider',
                height: 30,
                bottom: 6,
                filterMode: 'none',
                borderColor: 'rgba(255,255,255,0.14)',
                backgroundColor: (typeof tlTc === 'function' ? tlTc('rgba(15,23,42,0.70)', 'rgba(241,245,249,0.95)') : 'rgba(15,23,42,0.70)'),
                fillerColor: 'rgba(34,211,238,0.20)',
                handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' },
                textStyle: { fontSize: 10, color: 'rgba(255,255,255,0.50)' }
            },
            {
                type: 'inside',
                filterMode: 'none'
            }
        ],

        series: series
    });

    // Preserve the existing hover behaviour.
    pmG.chart.off('highlight');
    pmG.chart.on('highlight', function () {
        pmG.chart.dispatchAction({ type: 'downplay' });
    });

    $(window)
        .off('resize.pmg')
        .on('resize.pmg', function () {
            if (pmG.chart) {
                pmG.chart.resize();
            }
        });
}

// ---------------------------------------------------------------------
// POINT MACHINE GRAPH - INDICATION TAB (A + B ends, correct nomenclature)

// ---------------------------------------------------------------------
// POINT MACHINE GRAPH - OPERATION EVENT TAB (both directions; A/B separate)
// Lists every operation date-time (Normal AND Reverse) in descending order
// over the From/To range. Signal toggles: A Current / A Voltage / B Current /
// B Voltage. Clicking an event updates the header (direction + timestamp).
// ---------------------------------------------------------------------
function pmgSwitchTab(tab) {
    pmG.tab = tab;
    if (tab === 'op') {
        $('#pmgTabInd').hide(); $('#pmgTabOp').show();
        $('#pmgTabBtnInd').removeClass('on');
        $('#pmgTabBtnOp').addClass('on');
        if (!pmG.opLoaded) pmgLoadOpEvents();
        else if (pmG.opChart) setTimeout(function () { pmG.opChart.resize(); }, 30);
    } else {
        $('#pmgTabOp').hide(); $('#pmgTabInd').show();
        $('#pmgTabBtnOp').removeClass('on');
        $('#pmgTabBtnInd').addClass('on');
        if (pmG.chart) setTimeout(function () { pmG.chart.resize(); }, 30);
    }
}

function pmgSetOpType(type) {
    pmG.opType = type;
    $('.pmg-optype').removeClass('on');
    $('.pmg-optype[data-type="' + type + '"]').addClass('on');
    if (pmG.opSel >= 0) pmgShowOpEvent(pmG.opSel);
}

var PMG_OP_ATTR_MAP = {
    1001: { dir: 'N', type: 'AC' }, 2001: { dir: 'N', type: 'AV' }, 3001: { dir: 'N', type: 'BC' }, 4001: { dir: 'N', type: 'BV' },
    6001: { dir: 'R', type: 'AC' }, 7001: { dir: 'R', type: 'AV' }, 8001: { dir: 'R', type: 'BC' }, 9001: { dir: 'R', type: 'BV' }
};

// Scalar operation stats shown as MAX/AVG + OPERATION TIME pills, matching
// Telemetry History. Same attribute IDs the operation table uses:
//   Avg            = base + 2   (…002)
//   Max            = base + 4   (…004)
//   Operation Time = …005 of the END's CURRENT id (A: 1005/6005, B: 3005/8005)
// so A Voltage borrows the A-end current time and B Voltage the B-end time.
var PMG_STAT_AVG_IDS = { N: { AC: 1002, AV: 2002, BC: 3002, BV: 4002 }, R: { AC: 6002, AV: 7002, BC: 8002, BV: 9002 } };
var PMG_STAT_MAX_IDS = { N: { AC: 1004, AV: 2004, BC: 3004, BV: 4004 }, R: { AC: 6004, AV: 7004, BC: 8004, BV: 9004 } };
var PMG_STAT_OPTIME_IDS = { N: { AC: 1005, AV: 1005, BC: 3005, BV: 3005 }, R: { AC: 6005, AV: 6005, BC: 8005, BV: 8005 } };
var PMG_STAT_ID_SET = (function () {
    var set = {};
    [PMG_STAT_AVG_IDS, PMG_STAT_MAX_IDS, PMG_STAT_OPTIME_IDS].forEach(function (m) {
        ['N', 'R'].forEach(function (d) { for (var t in m[d]) { set[m[d][t]] = true; } });
    });
    return set;
})();

function pmgLoadOpEvents() {
    if (!pmG.assetId) return;

    pmG.opLoaded = true;
    pmG.opEvents = [];
    pmG.opSel = -1;

    $('#pmgOpLoad').show();
    $('#pmgOpChart').hide();
    $('#pmgOpEmpty').hide();

    $('#pmgOpList').html(
        '<div style="padding:14px;color:rgba(255,255,255,0.45);' +
        'font-size:12px;text-align:center;">' +
        '<i class="fas fa-spinner fa-spin"></i> Loading...' +
        '</div>'
    );

    $('#pmgOpListCount').text('');
    $('#pmgOpCount').text('');
    $('#pmgOpSel').text('');

    var endDate =
        pmG.toMs
            ? new Date(pmG.toMs)
            : new Date();

    var startDate =
        pmG.fromMs
            ? new Date(pmG.fromMs)
            : new Date(
                endDate.getTime() -
                24 * 60 * 60 * 1000
            );

    var url =
        HISTORY_API_BASE +
        '?assetId=' +
        encodeURIComponent(pmG.assetId) +
        '&startDate=' +
        encodeURIComponent(
            formatDateForHistoryApi(startDate)
        ) +
        '&endDate=' +
        encodeURIComponent(
            formatDateForHistoryApi(endDate)
        ) +
        // e7mriv2web: operation-event fetch = PM op columns (incl. …001
        // waveform arrays), no window edges, per-operation grouping
        '&_pmop=1';

    $.ajax({
        url: url,
        type: 'GET',
        dataType: 'json',
        timeout: 150000,

        success: function (response) {
            $('#pmgOpLoad').hide();

            var data =
                response &&
                    Array.isArray(response.Data)
                    ? response.Data
                    : [];

            if (!data.length) {
                $('#pmgOpList').html('');

                $('#pmgOpEmpty')
                    .html(
                        '<i class="fas fa-info-circle fa-2x" ' +
                        'style="display:block;margin-bottom:12px;' +
                        'color:rgba(255,255,255,0.45);"></i>' +
                        'No operation data for the selected range.'
                    )
                    .show();

                return;
            }

            var rangeStartMs =
                startDate.getTime();

            var rangeEndMs =
                endDate.getTime();

            //var mergeWindowMs =
            //    120 * 1000;
            var mergeWindowMs = 0;
            /*
 * Operation attributes belonging to one physical operation may
 * differ slightly in TimestampDevice, but must never borrow data
 * from the next operation.
 */
            var waveformSearchWindowMs =
                10 * 1000;

            var normalOperationIds =
                typeof PM_NORMAL_OPERATION_IDS !==
                    'undefined'
                    ? PM_NORMAL_OPERATION_IDS.slice()
                    : [
                        1001, 1002, 1004, 1005,
                        2002,
                        3002, 3004, 3005,
                        4002
                    ];

            var reverseOperationIds =
                typeof PM_REVERSE_OPERATION_IDS !==
                    'undefined'
                    ? PM_REVERSE_OPERATION_IDS.slice()
                    : [
                        6001, 6002, 6004, 6005,
                        7002,
                        8002, 8004, 8005,
                        9002
                    ];

            var waveformIds = {
                N: {
                    AC: 1001,
                    AV: 2001,
                    BC: 3001,
                    BV: 4001
                },
                R: {
                    AC: 6001,
                    AV: 7001,
                    BC: 8001,
                    BV: 9001
                }
            };

            var rawOperationMap = {};
            var waveformEntriesById = {};
            var statEntriesById = {};

            function getEntryTimestamp(entry) {
                if (!entry) return null;

                var timestamp =
                    entry.Timestamp || {};

                var timestampRaw =
                    timestamp.TimestampDevice ||
                    entry.TimestampDevice ||
                    timestamp.TimestampLocal ||
                    entry.TimestampLocal ||
                    null;

                if (
                    !timestampRaw ||
                    String(timestampRaw)
                        .indexOf('0001') >= 0
                ) {
                    return null;
                }

                var timestampMs =
                    new Date(timestampRaw)
                        .getTime();

                if (
                    isNaN(timestampMs) ||
                    timestampMs <= 0
                ) {
                    return null;
                }

                return {
                    raw: timestampRaw,
                    ms: timestampMs
                };
            }

            function getOperationDirection(
                attributeId
            ) {
                var id =
                    parseInt(attributeId, 10);

                if (isNaN(id)) {
                    return null;
                }

                if (
                    normalOperationIds
                        .indexOf(id) !== -1
                ) {
                    return 'N';
                }

                if (
                    reverseOperationIds
                        .indexOf(id) !== -1
                ) {
                    return 'R';
                }

                var waveformMeta =
                    PMG_OP_ATTR_MAP[id];

                return waveformMeta
                    ? waveformMeta.dir
                    : null;
            }

            function parseWaveform(value) {
                if (
                    typeof value !== 'string' ||
                    value.indexOf(',') === -1
                ) {
                    return null;
                }

                var array =
                    value.split(',')
                        .map(function (item) {
                            return parseFloat(
                                String(item).trim()
                            );
                        })
                        .filter(function (item) {
                            return !isNaN(item);
                        });

                return array.length
                    ? array
                    : null;
            }

            // ====================================================
            // Step 1:
            // Create operation markers from every configured
            // Point Machine operation attribute.
            //
            // Waveform presence is not required.
            // ====================================================
            data.forEach(function (item) {
                if (!item || !item.Values) {
                    return;
                }

                var attributeId =
                    parseInt(
                        item.AttributeId,
                        10
                    );

                if (isNaN(attributeId)) {
                    return;
                }

                var direction =
                    getOperationDirection(
                        attributeId
                    );

                if (!direction) {
                    return;
                }

                var waveformMeta =
                    PMG_OP_ATTR_MAP[
                    attributeId
                    ] || null;

                for (var key in item.Values) {
                    if (
                        !Object.prototype
                            .hasOwnProperty.call(
                                item.Values,
                                key
                            )
                    ) {
                        continue;
                    }

                    var entry =
                        item.Values[key];

                    if (!entry) continue;
                    var entryDataType =
                        String(
                            entry.DataType || ''
                        )
                            .trim()
                            .toLowerCase();

                    /*
                     * Operation Event markers and waveforms must come only
                     * from persisted PointMachine operation history.
                     */
                    if (
                        entryDataType &&
                        entryDataType !== 'pointmachine'
                    ) {
                        continue;
                    }
                    var timestamp =
                        getEntryTimestamp(entry);

                    if (!timestamp) continue;

                    if (
                        timestamp.ms <
                        rangeStartMs ||
                        timestamp.ms >
                        rangeEndMs
                    ) {
                        continue;
                    }

                    // Deduplicate attributes having the same
                    // direction and timestamp.
                    var rawEventKey =
                        direction +
                        '_' +
                        timestamp.ms;

                    if (
                        !rawOperationMap[
                        rawEventKey
                        ]
                    ) {
                        rawOperationMap[
                            rawEventKey
                        ] = {
                            ts: timestamp.ms,
                            tsStr:
                                timestamp.raw,
                            dir: direction
                        };
                    }

                    // Store waveform separately so it can later
                    // be attached by nearest timestamp.
                    if (waveformMeta) {
                        var waveform =
                            parseWaveform(
                                entry.Value
                            );

                        if (!waveform) continue;

                        if (
                            !waveformEntriesById[
                            attributeId
                            ]
                        ) {
                            waveformEntriesById[
                                attributeId
                            ] = [];
                        }

                        waveformEntriesById[
                            attributeId
                        ].push({
                            ts: timestamp.ms,
                            tsStr:
                                timestamp.raw,
                            array: waveform
                        });
                    }

                    // Capture scalar operation stats (Avg/Max/OperationTime)
                    // keyed by attribute id; matched to each event later by
                    // nearest timestamp, exactly like the waveforms above.
                    if (PMG_STAT_ID_SET[attributeId]) {
                        var _statVal = parseFloat(entry.Value);
                        if (!isNaN(_statVal)) {
                            if (!statEntriesById[attributeId]) {
                                statEntriesById[attributeId] = [];
                            }
                            statEntriesById[attributeId].push({
                                ts: timestamp.ms,
                                value: _statVal
                            });
                        }
                    }
                }
            });

            var rawEvents =
                Object.keys(rawOperationMap)
                    .map(function (key) {
                        return rawOperationMap[key];
                    })
                    .sort(function (a, b) {
                        return a.ts - b.ts;
                    });

            // ====================================================
            // Step 2:
            // Group consecutive timestamps belonging to the same
            // direction and operation.
            //
            // Use the latest timestamp as the event timestamp,
            // matching Telemetry Live PM table behaviour.
            // ====================================================
            var groupedEvents = [];

            rawEvents.forEach(function (rawEvent) {
                var currentGroup =
                    groupedEvents.length
                        ? groupedEvents[
                        groupedEvents.length - 1
                        ]
                        : null;

                var canMerge =
                    currentGroup &&
                    currentGroup.dir ===
                    rawEvent.dir &&
                    (
                        rawEvent.ts -
                        currentGroup.lastTs
                    ) <= mergeWindowMs;

                if (!canMerge) {
                    groupedEvents.push({
                        ts: rawEvent.ts,
                        tsStr:
                            rawEvent.tsStr,
                        firstTs:
                            rawEvent.ts,
                        lastTs:
                            rawEvent.ts,
                        dir:
                            rawEvent.dir,
                        arrays: {},
                        arrayTimestampMs: {},
                        arrayDistanceMs: {},
                        arrayDirection: {}
                    });

                    return;
                }

                currentGroup.lastTs =
                    rawEvent.ts;

                // Use the latest operation attribute timestamp,
                // as the live table does for a direction batch.
                if (
                    rawEvent.ts >=
                    currentGroup.ts
                ) {
                    currentGroup.ts =
                        rawEvent.ts;

                    currentGroup.tsStr =
                        rawEvent.tsStr;
                }
            });

            function findNearestWaveform(
                entries,
                targetMs,
                maximumDistanceMs
            ) {
                if (
                    !entries ||
                    !entries.length
                ) {
                    return null;
                }

                var best = null;

                for (
                    var index = 0;
                    index < entries.length;
                    index++
                ) {
                    var entry =
                        entries[index];

                    var distance =
                        Math.abs(
                            entry.ts -
                            targetMs
                        );

                    if (
                        distance >
                        maximumDistanceMs
                    ) {
                        continue;
                    }

                    if (
                        !best ||
                        distance <
                        best.distance
                    ) {
                        best = {
                            array:
                                entry.array,
                            timestampMs:
                                entry.ts,
                            timestampRaw:
                                entry.tsStr,
                            distance:
                                distance
                        };
                    }
                }

                return best;
            }

            function findNearestStat(
                entries,
                targetMs,
                maximumDistanceMs
            ) {
                if (!entries || !entries.length) {
                    return null;
                }

                var best = null;

                for (
                    var index = 0;
                    index < entries.length;
                    index++
                ) {
                    var entry = entries[index];

                    var distance =
                        Math.abs(entry.ts - targetMs);

                    if (distance > maximumDistanceMs) {
                        continue;
                    }

                    if (!best || distance < best.distance) {
                        best = {
                            value: entry.value,
                            distance: distance
                        };
                    }
                }

                return best ? best.value : null;
            }

            // Sort waveform entries once.
            for (
                var waveformAttributeId in
                waveformEntriesById
            ) {
                if (
                    !Object.prototype
                        .hasOwnProperty.call(
                            waveformEntriesById,
                            waveformAttributeId
                        )
                ) {
                    continue;
                }

                waveformEntriesById[
                    waveformAttributeId
                ].sort(function (a, b) {
                    return a.ts - b.ts;
                });
            }

            // ====================================================
            // Step 3:
            // Attach AC/AV/BC/BV using nearest-timestamp matching.
            //
            // Preferred direction first, then opposite direction,
            // matching the table waveform lookup behaviour.
            // ====================================================
            groupedEvents.forEach(
                function (event) {
                    var preferredDirection =
                        event.dir === 'R'
                            ? 'R'
                            : 'N';

                    event.statAvg = event.statAvg || {};
                    event.statMax = event.statMax || {};
                    event.statOpMs = event.statOpMs || {};



                    ['AC', 'AV', 'BC', 'BV']
                        .forEach(function (type) {
                            var preferredId =
                                waveformIds[
                                preferredDirection
                                ][type];

                            var waveform =
                                findNearestWaveform(
                                    waveformEntriesById[
                                    preferredId
                                    ] || [],
                                    event.ts,
                                    waveformSearchWindowMs
                                );

                            /*
                             * Do not borrow a waveform from the opposite direction.
                             *
                             * A Normal event may use only Normal waveform IDs.
                             * A Reverse event may use only Reverse waveform IDs.
                             *
                             * When this end/type did not record data for the operation,
                             * leave it absent so the UI displays no-data correctly.
                             */
                            if (!waveform) {
                                return;
                            }

                            event.arrays[type] =
                                waveform.array;

                            event.arrayTimestampMs[type] =
                                waveform.timestampMs;

                            event.arrayDistanceMs[type] =
                                waveform.distance;

                            event.arrayDirection[type] =
                                preferredDirection;

                            var _avgHit = findNearestStat(
                                statEntriesById[PMG_STAT_AVG_IDS[preferredDirection][type]] || [],
                                event.ts,
                                waveformSearchWindowMs
                            );
                            var _maxHit = findNearestStat(
                                statEntriesById[PMG_STAT_MAX_IDS[preferredDirection][type]] || [],
                                event.ts,
                                waveformSearchWindowMs
                            );
                            var _opHit = findNearestStat(
                                statEntriesById[PMG_STAT_OPTIME_IDS[preferredDirection][type]] || [],
                                event.ts,
                                waveformSearchWindowMs
                            );

                            if (_avgHit !== null) { event.statAvg[type] = _avgHit; }
                            if (_maxHit !== null) { event.statMax[type] = _maxHit; }
                            if (_opHit !== null) { event.statOpMs[type] = _opHit; }
                        });
                }
            );

            var events =
                groupedEvents.filter(
                    function (event) {
                        var insideRange =
                            event.ts >= rangeStartMs &&
                            event.ts <= rangeEndMs;

                        if (!insideRange) {
                            return false;
                        }

                        /*
                         * Do not show a timestamp created only by an
                         * unrelated/scalar operation attribute.
                         *
                         * At least one actual operation waveform must be
                         * present for AC, AV, BC or BV.
                         */
                        return !!(
                            event.arrays.AC ||
                            event.arrays.AV ||
                            event.arrays.BC ||
                            event.arrays.BV
                        );
                    }
                );

            events.sort(function (a, b) {
                return b.ts - a.ts;
            });

            pmG.opEvents = events;
            pmG.opSel = -1;

            pmgRenderOpList();

            $('#pmgOpListCount')
                .text(
                    '(' +
                    events.length +
                    ')'
                );

            $('#pmgOpCount')
                .text(
                    events.length +
                    ' event' +
                    (
                        events.length === 1
                            ? ''
                            : 's'
                    )
                );

            if (!events.length) {
                $('#pmgOpEmpty')
                    .html(
                        '<i class="fas fa-info-circle fa-2x" ' +
                        'style="display:block;margin-bottom:12px;' +
                        'color:rgba(255,255,255,0.45);"></i>' +
                        'No operation events found in the selected range.'
                    )
                    .show();

                return;
            }

            pmgShowOpEvent(0);
        },

        error: function () {
            $('#pmgOpLoad').hide();
            $('#pmgOpList').html('');

            $('#pmgOpEmpty')
                .html(
                    '<i class="fas fa-exclamation-circle fa-2x" ' +
                    'style="display:block;margin-bottom:12px;' +
                    'color:#ef4444;"></i>' +
                    'Failed to load operation data.'
                )
                .show();
        }
    });
}

//function pmgLoadOpEvents() {
//    if (!pmG.assetId) return;
//    pmG.opLoaded = true;
//    pmG.opEvents = []; pmG.opSel = -1;
//    $('#pmgOpLoad').show(); $('#pmgOpChart').hide(); $('#pmgOpEmpty').hide();
//    $('#pmgOpList').html('<div style="padding:14px;color:rgba(255,255,255,0.45);font-size:12px;text-align:center;"><i class="fas fa-spinner fa-spin"></i> Loading...</div>');
//    $('#pmgOpListCount').text(''); $('#pmgOpSel').text('');

//    var e = pmG.toMs ? new Date(pmG.toMs) : new Date();
//    var s = pmG.fromMs ? new Date(pmG.fromMs) : new Date(e.getTime() - 24 * 3600000);
//    var url = HISTORY_API_BASE + '?assetId=' + pmG.assetId + '&startDate=' + formatDateForHistoryApi(s) + '&endDate=' + formatDateForHistoryApi(e);

//    $.ajax({
//        url: url, type: 'GET', dataType: 'json', timeout: 60000,
//        success: function (r) {
//            $('#pmgOpLoad').hide();
//            var data = (r && r.Data) ? r.Data : [];
//            if (!data.length) {
//                $('#pmgOpList').html('');
//                $('#pmgOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:rgba(255,255,255,0.45);"></i>No operation data for the selected range.').show();
//                return;
//            }
//            // Client-side range bounds — the API sometimes returns events outside
//            // the requested window, so enforce the selected range here too.
//            var _rangeStartMs = s.getTime();
//            var _rangeEndMs = e.getTime();

//            var byTs = {};
//            data.forEach(function (item) {
//                var m = PMG_OP_ATTR_MAP[item.AttributeId];
//                if (!m || !item.Values) return;
//                for (var key in item.Values) {
//                    var en = item.Values[key];
//                    if (!en || typeof en.Value !== 'string' || en.Value.indexOf(',') === -1) continue;
//                    var tsRaw = en.Timestamp ? en.Timestamp.TimestampDevice : null;
//                    if (!tsRaw || tsRaw.indexOf('0001') >= 0) continue;
//                    var ms = new Date(tsRaw).getTime();
//                    if (isNaN(ms) || ms <= 0) continue;
//                    // Drop anything outside the selected date range.
//                    if (ms < _rangeStartMs || ms > _rangeEndMs) continue;
//                    var arr = en.Value.split(',').map(function (v) { return parseFloat(v.trim()); }).filter(function (v) { return !isNaN(v); });
//                    if (!arr.length) continue;
//                    if (!byTs[ms]) byTs[ms] = { ts: ms, tsStr: tsRaw, dir: m.dir, arrays: {} };
//                    if (!byTs[ms].arrays[m.type]) byTs[ms].arrays[m.type] = arr;
//                }
//            });
//            var events = Object.keys(byTs).map(function (k) { return byTs[k]; });
//            events.sort(function (a, b) { return b.ts - a.ts; });
//            pmG.opEvents = events;
//            pmgRenderOpList();
//            $('#pmgOpListCount').text('(' + events.length + ')');
//            if (!events.length) {
//                $('#pmgOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:rgba(255,255,255,0.45);"></i>No operation events found in the selected range.').show();
//                return;
//            }
//            pmgShowOpEvent(0);
//        },
//        error: function () {
//            $('#pmgOpLoad').hide(); $('#pmgOpList').html('');
//            $('#pmgOpEmpty').html('<i class="fas fa-exclamation-circle fa-2x" style="display:block;margin-bottom:12px;color:#ef4444;"></i>Failed to load operation data.').show();
//        }
//    });
//}

function _pmgDirBadge(dir) {
    if (dir === 'R') return '<span class="pmg-dir rev">Reverse</span>';
    return '<span class="pmg-dir nor">Normal</span>';
}

function pmgRenderOpList() {
    var html = '';
    (pmG.opEvents || []).forEach(function (ev, i) {
        var d = new Date(ev.ts);
        var dateStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear();
        var timeStr = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
        var active = (i === pmG.opSel);
        var av = ['AC', 'AV', 'BC', 'BV'].filter(function (type) {
            return (
                ev.arrays &&
                Array.isArray(ev.arrays[type]) &&
                ev.arrays[type].length > 0
            );
        });

        html += '<div onclick="pmgShowOpEvent(' + i + ')" class="pmg-ev' + (ev.dir === 'R' ? ' rev' : '') + (active ? ' on' : '') + '">' +
            '<div class="pmg-ev-top"><span class="pmg-ev-time">' + timeStr + '</span>' + _pmgDirBadge(ev.dir) + '</div>' +
            '<div class="pmg-ev-date">' + dateStr + '</div>' +
            (av.length ? '<div class="pmg-ev-av">' + av.map(function (t) { return '<span>' + t + '</span>'; }).join('') + '</div>' : '') +
            '</div>';
    });
    $('#pmgOpList').html(html);
}

//function pmgShowOpEvent(index) {
//    var ev = (pmG.opEvents || [])[index];
//    if (!ev) return;
//    pmG.opSel = index;
//    pmgRenderOpList();

//    var type = pmG.opType || 'AC';
//    var arr = ev.arrays[type];
//    var typeLabels = { 'AC': 'A Current', 'AV': 'A Voltage', 'BC': 'B Current', 'BV': 'B Voltage' };
//    var units = { 'AC': 'A', 'AV': 'V', 'BC': 'A', 'BV': 'V' };
//    var colors = { 'AC': '#2563eb', 'AV': '#059669', 'BC': '#dc2626', 'BV': '#d97706' };
//    var dirLabel = (ev.dir === 'R') ? 'Reverse' : 'Normal';
//    var nm = (typeof ipsAssetName === 'function') ? ipsAssetName(pmG.assetId) : ('PT-' + pmG.assetId);

//    var d = new Date(ev.ts);
//    var whenStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' +
//        String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
//    $('#pmgOpSel').html(_pmgDirBadge(ev.dir) + ' <span style="margin-left:6px;">' + typeLabels[type] + '</span>  |  <span style="color:#22d3ee;">' + whenStr + '</span>');

//    if (!arr || !arr.length) {
//        $('#pmgOpChart').hide();
//        $('#pmgOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:rgba(255,255,255,0.45);"></i>No ' + typeLabels[type] + ' waveform captured for this operation.').show();
//        return;
//    }
//    $('#pmgOpEmpty').hide(); $('#pmgOpChart').show();
//    var title = nm + ' - ' + dirLabel + ' ' + typeLabels[type];
//    renderSingleArrayByTimestamp([{ timestamp: ev.ts, array: arr, tsStr: ev.tsStr }], title, units[type], colors[type], 'pmgOpChart');
//    var elc = document.getElementById('pmgOpChart');
//    pmG.opChart = elc ? echarts.getInstanceByDom(elc) : null;
//}


// Renders the MAX/AVG and OPERATION TIME summary pills above the Operation
// Event graph, matching Telemetry History. Values come from the operation
// attributes bound per event in pmgLoadOpEvents (Avg …002, Max …004,
// Operation Time …005 of the end's CURRENT id). They are NOT recomputed from
// the waveform; only a genuinely missing companion value falls back to the
// trace so a box is never blank. Voltage signals show AVG only.
function pmgRenderOpStats(ev, type, arr, unit, color) {
    var $stats = $('#pmgOpStats');
    if (!$stats.length) return;

    var n = (arr && arr.length) || 0;
    if (!n) { $stats.hide().empty(); return; }

    var avg = (ev && ev.statAvg && ev.statAvg[type] !== undefined)
        ? Number(ev.statAvg[type]) : null;
    var mx = (ev && ev.statMax && ev.statMax[type] !== undefined)
        ? Number(ev.statMax[type]) : null;
    var opMs = (ev && ev.statOpMs && ev.statOpMs[type] !== undefined)
        ? Number(ev.statOpMs[type]) : null;

    // Fallbacks (used only when a companion attribute was missing).
    if (avg === null || mx === null) {
        var sum = 0, wmax = -Infinity, cnt = 0;
        for (var i = 0; i < n; i++) {
            var v = Number(arr[i]);
            if (isNaN(v)) continue;
            sum += v;
            if (v > wmax) wmax = v;
            cnt++;
        }
        if (cnt) {
            if (avg === null) avg = sum / cnt;
            if (mx === null) mx = wmax;
        }
    }
    if (opMs === null) opMs = (n - 1) * 20;   // plotted span as last resort

    if (avg === null || isNaN(avg)) avg = 0;
    if (mx === null || isNaN(mx)) mx = 0;
    if (isNaN(opMs)) opMs = 0;

    var opTxt = Math.round(opMs) + ' ms';

    function _pill(label, val, valColor) {
        return '<span style="display:inline-flex;flex-direction:column;align-items:center;' +
            'padding:3px 10px;border-radius:6px;background:rgba(34,211,238,0.10);' +
            'border:1px solid rgba(34,211,238,0.28);line-height:1.3;min-width:80px;text-align:center;">' +
            '<span style="font-size:9px;font-weight:700;letter-spacing:.5px;' +
            'text-transform:uppercase;color:rgba(255,255,255,0.50);white-space:nowrap;">' + label + '</span>' +
            '<span style="font-size:13px;font-weight:800;color:' +
            (valColor || 'rgba(255,255,255,0.96)') + ';white-space:nowrap;">' + val + '</span>' +
            '</span>';
    }

    var isVoltage = (type === 'AV' || type === 'BV');
    $stats.html(
        (isVoltage
            ? _pill('AVG', avg.toFixed(2) + ' ' + unit, color)
            : _pill('MAX/AVG', mx.toFixed(2) + ' / ' + avg.toFixed(2) + ' ' + unit, color)) +
        _pill('OPERATION TIME', opTxt, '#22d3ee')
    ).css({ display: 'flex', gap: '6px', 'align-items': 'center' });
}

function pmgShowOpEvent(index) {
    var ev = (pmG.opEvents || [])[index];

    if (!ev) {
        return;
    }

    pmG.opSel = index;
    pmgRenderOpList();

    var labels = {
        AC: 'A Current',
        AV: 'A Voltage',
        BC: 'B Current',
        BV: 'B Voltage'
    };

    var units = {
        AC: 'A',
        AV: 'V',
        BC: 'A',
        BV: 'V'
    };

    var colors = {
        AC: '#2563eb',
        AV: '#059669',
        BC: '#dc2626',
        BV: '#d97706'
    };

    /*
     * Show only the waveform types actually captured
     * for the selected operation event.
     *
     * This is based on History hgPmShowOpEvent().
     */
    var availableTypes = [
        'AC',
        'AV',
        'BC',
        'BV'
    ].filter(function (type) {
        return (
            ev.arrays &&
            Array.isArray(ev.arrays[type]) &&
            ev.arrays[type].length > 0
        );
    });

    $('.pmg-optype').each(function () {
        var buttonType =
            $(this).attr('data-type');

        if (
            availableTypes.indexOf(
                buttonType
            ) === -1
        ) {
            $(this)
                .hide()
                .removeClass('on');
        } else {
            $(this).show();
        }
    });

    /*
     * Keep the currently selected type when it is
     * available for this event.
     *
     * Otherwise automatically select the first available
     * waveform so the user never sees an unnecessary
     * empty graph.
     */
    var type = pmG.opType || 'AC';

    if (
        availableTypes.length > 0 &&
        availableTypes.indexOf(type) === -1
    ) {
        type = availableTypes[0];
        pmG.opType = type;

        $('.pmg-optype')
            .removeClass('on');

        $('.pmg-optype[data-type="' + type + '"]')
            .addClass('on');
    }

    var arr =
        ev.arrays &&
        ev.arrays[type];

    var directionLabel =
        ev.dir === 'R'
            ? 'Reverse'
            : 'Normal';

    var assetName =
        typeof ipsAssetName === 'function'
            ? ipsAssetName(pmG.assetId)
            : (
                String(pmG.name || '').trim() ||
                ('PT-' + pmG.assetId)
            );

    var eventDate =
        new Date(ev.ts);

    var validEventDate =
        !isNaN(eventDate.getTime());

    var whenStr =
        validEventDate
            ? (
                String(
                    eventDate.getDate()
                ).padStart(2, '0') +
                '/' +
                String(
                    eventDate.getMonth() + 1
                ).padStart(2, '0') +
                '/' +
                eventDate.getFullYear() +
                ' ' +
                String(
                    eventDate.getHours()
                ).padStart(2, '0') +
                ':' +
                String(
                    eventDate.getMinutes()
                ).padStart(2, '0') +
                ':' +
                String(
                    eventDate.getSeconds()
                ).padStart(2, '0')
            )
            : 'Invalid Date & Time';

    $('#pmgOpSel').html(
        _pmgDirBadge(ev.dir) +
        (
            availableTypes.length > 0
                ? (
                    ' <span style="margin-left:6px;">' +
                    (labels[type] || '') +
                    '</span>'
                )
                : ''
        ) +
        ' &nbsp;|&nbsp; ' +
        '<span style="color:#22d3ee;">' +
        whenStr +
        '</span>'
    );

    /*
     * The event remains visible in the Date & Time list
     * even when no waveform was captured.
     */
    if (!availableTypes.length) {
        $('#pmgOpStats').hide().empty();

        if (
            pmG.opChart &&
            typeof pmG.opChart.dispose ===
            'function'
        ) {
            pmG.opChart.dispose();
            pmG.opChart = null;
        }

        $('#pmgOpChart').hide();

        $('#pmgOpEmpty')
            .html(
                '<i class="fas fa-info-circle fa-2x" ' +
                'style="display:block;margin-bottom:12px;' +
                'color:rgba(255,255,255,0.45);"></i>' +
                'No A End or B End waveform was captured ' +
                'for this operation.'
            )
            .show();

        return;
    }

    if (!arr || !arr.length) {
        $('#pmgOpStats').hide().empty();

        $('#pmgOpChart').hide();

        $('#pmgOpEmpty')
            .html(
                '<i class="fas fa-info-circle fa-2x" ' +
                'style="display:block;margin-bottom:12px;' +
                'color:rgba(255,255,255,0.45);"></i>' +
                'No ' +
                labels[type] +
                ' waveform captured for this operation.'
            )
            .show();

        return;
    }

    $('#pmgOpEmpty').hide();
    $('#pmgOpChart').show();

    pmgRenderOpStats(ev, type, arr, units[type], colors[type]);

    var title =
        assetName +
        ' - ' +
        directionLabel +
        ' ' +
        labels[type];

    renderSingleArrayByTimestamp(
        [
            {
                timestamp: ev.ts,
                array: arr,
                tsStr:
                    ev.tsStr ||
                    ev.timestampDevice ||
                    ev.ts
            }
        ],
        title,
        units[type],
        colors[type],
        'pmgOpChart'
    );

    var chartElement =
        document.getElementById(
            'pmgOpChart'
        );

    pmG.opChart =
        chartElement &&
            typeof echarts !== 'undefined'
            ? echarts.getInstanceByDom(
                chartElement
            )
            : null;

    /*
     * The chart container was hidden before rendering.
     * Resize after it becomes visible.
     */
    if (
        pmG.opChart &&
        typeof pmG.opChart.resize ===
        'function'
    ) {
        setTimeout(function () {
            if (pmG.opChart) {
                pmG.opChart.resize();
            }
        }, 30);
    }
}
// ===================== END POINT MACHINE GRAPH =====================



/* =====================================================================
 * ENTRY POINTS — wire the two graph panels into FRS Advance's view flow.
 *
 * FRS Advance drives views from the .tl-vmode segmented control through
 * _tlApplyViewMode(), and from fnSearchView() for the drpView value. Both
 * are wrapped here (rather than edited in telemetrylive.js) so this port
 * stays in one file.
 *
 * Routing for the Graph view:
 *   IPS asset type          -> fnBindIpsGraph()  (1..n assets)
 *   Point Machine asset type-> fnBindPmGraph()   (single asset, A/B + events)
 *   anything else           -> fnBindGrph()      (FRS Advance's own graph)
 * ===================================================================== */
(function () {
    'use strict';

    // Put the panel container in the right state for a full-width graph:
    // cards hidden, the scroll host shown but not in fixed-height table mode.
    function _tlgPrepareGraphSurface() {
        $('#atCardView').removeClass('at-force-shown').addClass('at-force-hidden').hide();
        $('#trackCardContainer').hide();
        $('.at-table-scroll')
            .removeClass('at-force-hidden')
            .addClass('at-force-shown')
            .show();

        var scrollEl = document.querySelector('.at-table-scroll');
        if (scrollEl) {
            // The graph sizes itself; the table's clamped scroll box would crop it.
            scrollEl.classList.remove('scroll-table-mode');
            scrollEl.style.overflow = '';
            scrollEl.style.maxHeight = '';
        }
        $('#divTelemetryLive').show();
    }

    // Tear the graph panel down and hand the container back to the list views.
    function _tlgLeaveGraphSurface() {
        try {
            if (window.ipsG && ipsG.chart) { ipsG.chart.dispose(); ipsG.chart = null; }
            if (window.pmG && pmG.chart) { pmG.chart.dispose(); pmG.chart = null; }
            if (window.pmG && pmG.opChart) { pmG.opChart.dispose(); pmG.opChart = null; }
        } catch (e) { /* chart already gone */ }

        $(window).off('resize.ipsg resize.pmg');
        if (window._pmgMaxTimer) { clearInterval(window._pmgMaxTimer); window._pmgMaxTimer = null; }

        var $tl = $('#divTelemetryLive');
        if ($tl.length) {
            $tl.empty();
            // The list renderers expect this container to exist.
            $tl.append('<div id="trackCardContainer" class="sites-cards" style="display:none;"></div>');
        }

        var scrollEl = document.querySelector('.at-table-scroll');
        if (scrollEl) scrollEl.classList.add('scroll-table-mode');
    }

    // Dispatch the Graph view to the renderer that matches the asset type.
    function tlgBindGraphView() {
        var siteId = $('#drpSite').val();
        if (!siteId || siteId === '0' || siteId === '') {
            showWarning('Please select a site', 'Validation');
            return false;
        }

        _tlgPrepareGraphSurface();

        var isIps = (typeof isIpsAssetType === 'function') && isIpsAssetType();
        var isPoint = (typeof isPointAssetType === 'function') && isPointAssetType();

        if (isIps) { fnBindIpsGraph(); return true; }
        if (isPoint) { fnBindPmGraph(); return true; }

        // Other asset types keep FRS Advance's existing single-asset graph.
        var assetIds = (typeof getSelectedAssetIds === 'function') ? (getSelectedAssetIds() || []) : [];
        if (assetIds.length > 0 && assetIds[0] !== '' && assetIds[0] !== '0') {
            $('#drpAsset').val(assetIds[0]);
        }
        if (typeof fnBindGrph === 'function') { fnBindGrph(); return true; }

        showInfo('Graph view is not available for this asset type.', 'View Type');
        return false;
    }
    window.tlgBindGraphView = tlgBindGraphView;

    // ── fnSearchView: add the Graph branch ────────────────────────────
    // fnSearchView() validates the filters and loads metadata before it
    // dispatches, so wrap it rather than duplicating that work: let the
    // original run for every other view and intercept only Graph.
    var _tlgOrigSearchView = window.fnSearchView;
    if (typeof _tlgOrigSearchView === 'function') {
        window.fnSearchView = function () {
            if (($('#drpView').val() || '') !== 'Graph') {
                return _tlgOrigSearchView.apply(this, arguments);
            }

            var siteId = $('#drpSite').val();
            var assetTypeId = $('#drpAssetType').val();
            if (!siteId || siteId === '0' || siteId === '') {
                showWarning('Please select a site', 'Validation');
                return;
            }
            if (!assetTypeId || assetTypeId === '0' || assetTypeId === '') {
                showWarning('Please select an asset type', 'Validation');
                return;
            }

            window._tlSearchInitiated = true;
            return tlgBindGraphView();
        };
    }

    // ── _tlApplyViewMode: accept 'Graph' alongside Cards / Table ──────
    // The stock dispatcher coerces anything that is not Cards or Table to
    // Cards, so Graph has to be handled before it is called.
    var _tlgOrigApplyViewMode = window._tlApplyViewMode;
    if (typeof _tlgOrigApplyViewMode === 'function') {
        window._tlApplyViewMode = function (mode, opts) {
            if (mode !== 'Graph') {
                // Leaving Graph: drop the panel and put the scroll host back
                // into table mode before the stock dispatcher repaints.
                if (window._atCurrentView === 'Graph') _tlgLeaveGraphSurface();
                return _tlgOrigApplyViewMode.apply(this, arguments);
            }

            $('#drpView').val('Graph');
            $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
            $('.tl-vmode-btn[data-vmode="Graph"]').addClass('active').attr('aria-pressed', 'true');
            window._atCurrentView = 'Graph';

            if (!$('#drpSite').val() || $('#drpSite').val() === '0') {
                _tlgPrepareGraphSurface();
                $('#divTelemetryLive').html(
                    '<div class="text-center p-5">Select a Station and Asset Numbers, then press Search.</div>'
                );
                return;
            }
            return tlgBindGraphView();
        };
    }

    // ── .tl-vmode Graph button ───────────────────────────────────────
    // telemetrylive.js handles .tl-vmode-btn with a handler delegated on
    // document, and it only repaints when wsLiveData already holds something.
    // The graphs read the History API instead, so they must render even with
    // no live data. Binding straight to the button runs in the target phase,
    // before document's delegated handler sees the event in the bubble phase,
    // so stopPropagation() keeps that one from firing at all.
    $(function () {
        $('.tl-vmode-btn[data-vmode="Graph"]').on('click.tlgraphbtn', function (e) {
            e.stopPropagation();
            if ($(this).hasClass('active')) return;
            window._tlApplyViewMode('Graph', { dataAlreadyOpen: true });
        });

        // Leaving Graph for Cards / Table: the stock handler only repaints when
        // wsLiveData is populated, so clear the panel here regardless. No
        // stopPropagation -- the stock handler still needs to run.
        $('.tl-vmode-btn').not('[data-vmode="Graph"]').on('click.tlgraphbtn', function () {
            if (window._atCurrentView === 'Graph') {
                _tlgLeaveGraphSurface();
                window._atCurrentView = $(this).attr('data-vmode');
            }
        });

        // v618.33: switching asset type always comes back to Card view. Only the
        // view state is reset here (no repaint) -- the core type-change handler
        // clears the data and the next Search renders the active (Cards) view.
        $('#drpAssetType').on('change.tlgraphbtn', function () {
            if (window._atCurrentView === 'Graph') _tlgLeaveGraphSurface();
            window._atCurrentView = 'Cards';
            $('#drpView').val('Cards');
            $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
            $('.tl-vmode-btn[data-vmode="Cards"]').addClass('active').attr('aria-pressed', 'true');
        });
    });

    // The overlay Back button restores the previous view; make sure leaving
    // Graph puts the scroll host back into table mode for the list views.
    var _tlgOrigReturn = window.tlReturnFromAssetView;
    if (typeof _tlgOrigReturn === 'function') {
        window.tlReturnFromAssetView = function () {
            var scrollEl = document.querySelector('.at-table-scroll');
            if (scrollEl && ($('#drpView').val() || '') !== 'Graph') {
                scrollEl.classList.add('scroll-table-mode');
            }
            return _tlgOrigReturn.apply(this, arguments);
        };
    }

    console.log('[TL Graphs] IPS + Point Machine graph panels registered.');
})();
