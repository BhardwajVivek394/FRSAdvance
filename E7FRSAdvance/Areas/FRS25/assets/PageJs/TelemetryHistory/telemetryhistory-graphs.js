/* =============================================================================
 * telemetryhistory-graphs.js — the v616 Telemetry History graph.
 *
 * Ported from the web project (E7MRIV2Web). This REPLACES the page's own
 * renderChart rather than wrapping it, which is the point of the port: the
 * request was to match v616's graphs, and this file is that implementation.
 *
 * What it brings over the version it replaces
 *   - Full-range loading. The table is cursor-paginated and holds one page;
 *     the graph walks every page first (_loadCompleteHistoryGraph) so the plot
 *     covers the whole searched window instead of the visible page.
 *   - Point Machine gets its own two-tab graph, Operational and Indication,
 *     driven from the operation events rather than raw samples.
 *   - IPS gets a stacked-lane mode (applyIpsHistoryStackedMode).
 *   - Y axis recomputes against the zoom window rather than the full series,
 *     so zooming in stops flattening the trace against a full-range maximum.
 *
 * Load order: a classic <script> AFTER the view's inline script (it overrides
 * functions defined there), AFTER telemetryhistory-compat.js (helpers), and
 * AFTER telemetryhistory-ipseld.js, which owns _ipsGroupChargingColumns that
 * the IPS stacked mode calls.
 *
 * Two deliberate omissions from the source block:
 *   - getReportFilterValues: the source's is callback-style, this page's is
 *     synchronous and its exporters read the return value. Nothing in the
 *     ported graph code calls it, so the page's own version is left standing.
 *   - The "Razor literal split" marker comments: those exist to keep the source
 *     view under the csc CS1647 literal-chain limit. A standalone .js file has
 *     no WriteLiteral chain, and `@*...*@` is not valid JavaScript.
 * ========================================================================== */

    // =====================================================================
    // POINT MACHINE TWO-TAB GRAPH (Telemetry History)
    // When Point Machine + Graph view is selected, render the same two-tab
    // graph as the Live page: Indication (both A & B ends) + Operation Event
    // (both directions, separate A/B current/voltage). Built directly from the
    // already-fetched attributes (indication ids + waveform array ids).
    // =====================================================================
    var HGPM_IND_NAMES = { 25: 'VPT - NWKR', 26: 'VPT - RWKR', 27: 'VPT - NWKR', 28: 'VPT - RWKR', 576: 'VPT 24 DC LOC N', 577: 'VPT 24 DC LOC R', 578: 'VPT 24 DC LOC N', 579: 'VPT 24 DC LOC R' };
    var HGPM_IND_META = { 25: { end: 'A', color: '#2563eb', loc: false }, 576: { end: 'A', color: '#7c3aed', loc: true }, 26: { end: 'A', color: '#059669', loc: false }, 577: { end: 'A', color: '#0891b2', loc: true }, 27: { end: 'B', color: '#dc2626', loc: false }, 578: { end: 'B', color: '#d97706', loc: true }, 28: { end: 'B', color: '#db2777', loc: false }, 579: { end: 'B', color: '#6366f1', loc: true } };
    var HGPM_IND_ORDER = [25, 576, 26, 577, 27, 578, 28, 579];
    var HGPM_OP_ATTR_MAP = { 1001: { dir: 'N', type: 'AC' }, 2001: { dir: 'N', type: 'AV' }, 3001: { dir: 'N', type: 'BC' }, 4001: { dir: 'N', type: 'BV' }, 6001: { dir: 'R', type: 'AC' }, 7001: { dir: 'R', type: 'AV' }, 8001: { dir: 'R', type: 'BC' }, 9001: { dir: 'R', type: 'BV' } };

    function _hgPmIndName(id) { return HGPM_IND_META[id].end + ' End - ' + (HGPM_IND_NAMES[id] || ('Attr ' + id)); }
    function _hgPmDirBadge(dir) {
        if (dir === 'R') return '<span style="display:inline-block;font-size:9px;font-weight:700;padding:1px 6px;border-radius:4px;background:#fde7cf;color:#9a5b00;">Reverse</span>';
        return '<span style="display:inline-block;font-size:9px;font-weight:700;padding:1px 6px;border-radius:4px;background:#d7f0ec;color:#0f5f65;">Normal</span>';
    }

    function renderHistoryPmTabs(assetResults) {
        var asset = assetResults[0] || {};
        var attrs = asset.attributes || asset.Data || asset.data || [];
        var name = asset.assetName || ('PT-' + (asset.assetId || ''));
        window.hgPm = { attrs: attrs, name: name, assetId: (asset.assetId !== undefined ? asset.assetId : null), fromMs: null, toMs: null, opType: 'AC', opEvents: [], opSel: -1, indChart: null, opChart: null, tab: 'ind' };

        if (!document.getElementById('hgPmStyles')) {
            var st = document.createElement('style'); st.id = 'hgPmStyles';
            st.textContent = '.hgpm-optype{padding:6px 12px;font-size:12px;font-weight:600;border-radius:6px;cursor:pointer;border:1px solid #d1dce8;background:#fff;color:#475569;}.hgpm-optype.on{background:#1a6e74;color:#fff;border-color:#1a6e74;}';
            document.head.appendChild(st);
        }

        var h = '';
        h += '<div style="display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;padding:10px 14px;background:linear-gradient(135deg,#042c43,#0a4a6e);border-radius:8px 8px 0 0;">';
        h += '<span style="color:#fff;font-size:14px;font-weight:700;"><i class="fas fa-line-chart" style="margin-right:6px;opacity:.85;"></i>' + name + ' - Point Machine Graph</span>';
        h += '<div id="hgPmRange"></div>';
        h += '</div>';
        h += '<div style="background:#fff;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;padding:12px 14px;">';
        h += '<div style="display:flex;gap:6px;margin-bottom:10px;border-bottom:2px solid #e2e8f0;">';
        h += '<button type="button" id="hgPmTabBtnInd" onclick="hgPmSwitchTab(\'ind\')" style="border:none;background:none;padding:9px 18px;font-size:13px;font-weight:700;color:#1a6e74;border-bottom:3px solid #1a6e74;cursor:pointer;margin-bottom:-2px;"><i class="fas fa-area-chart"></i> Indication</button>';
        h += '<button type="button" id="hgPmTabBtnOp" onclick="hgPmSwitchTab(\'op\')" style="border:none;background:none;padding:9px 18px;font-size:13px;font-weight:700;color:#64748b;border-bottom:3px solid transparent;cursor:pointer;margin-bottom:-2px;"><i class="fas fa-bolt"></i> Operation Event</button>';
        h += '</div>';
        h += '<div id="hgPmTabInd">';
        h += '<div style="font-size:12px;color:#64748b;margin:2px 0 8px;"><i class="fas fa-info-circle"></i> Indication voltages - A End & B End</div>';
        h += '<div id="hgPmIndChart" style="width:100%;height:460px;"></div>';
        h += '<div id="hgPmIndEmpty" style="display:none;text-align:center;padding:50px;color:#64748b;"><i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No indication data in the selected range.</div>';
        h += '</div>';
        h += '<div id="hgPmTabOp" style="display:none;">';
        h += '<div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-bottom:10px;">';
        h += '<span style="font-size:12px;font-weight:600;color:#475569;">Signal:</span>';
        h += '<button type="button" class="hgpm-optype on" data-type="AC" onclick="hgPmSetOpType(\'AC\')">A Current</button>';
        h += '<button type="button" class="hgpm-optype" data-type="AV" onclick="hgPmSetOpType(\'AV\')">A Voltage</button>';
        h += '<button type="button" class="hgpm-optype" data-type="BC" onclick="hgPmSetOpType(\'BC\')">B Current</button>';
        h += '<button type="button" class="hgpm-optype" data-type="BV" onclick="hgPmSetOpType(\'BV\')">B Voltage</button>';
        h += '<span id="hgPmOpCount" style="font-size:11px;color:#64748b;margin-left:auto;"></span>';
        h += '</div>';
        h += '<div style="display:flex;gap:14px;align-items:stretch;">';
        h += '<div style="flex:0 0 240px;background:#fff;border:1px solid #e2e8f0;border-radius:10px;display:flex;flex-direction:column;max-height:520px;">';
        h += '<div style="padding:11px 14px;border-bottom:1px solid #e2e8f0;font-size:13px;font-weight:700;color:#1a6e74;"><i class="fas fa-clock-o"></i> Operation Events <span id="hgPmOpListCount" style="color:#64748b;font-weight:500;"></span></div>';
        h += '<div id="hgPmOpList" style="overflow-y:auto;flex:1;padding:6px;"></div>';
        h += '</div>';
        h += '<div style="flex:1;min-width:0;background:#fff;border:1px solid #e2e8f0;border-radius:10px;padding:14px;">';
        h += '<div style="display:flex;align-items:flex-start;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:8px;">';
        h += '<div id="hgPmOpSel" style="font-size:13px;color:#475569;font-weight:600;min-height:18px;"></div>';
        h += '<div id="hgPmOpStats" style="display:none;align-items:center;gap:8px;flex-wrap:wrap;"></div>';
        h += '</div>';
        h += '<div id="hgPmOpChart" style="width:100%;height:480px;display:none;"></div>';
        h += '<div id="hgPmOpEmpty" style="text-align:center;padding:50px;color:#64748b;"><i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>Select a date-time from the list to view its operation waveform.</div>';
        h += '</div>';
        h += '</div>';
        h += '</div>';
        h += '</div>';
        $('#divChartContainer').empty().html(h);

        // Seed the From/To pickers from the current search range (fall back to midnight->now),
        // block future dates, and wire the Load button — mirrors the Telemetry Live PM graph.
        // Date range is taken from the top-level filter (txtFromDate/txtToDate).
        (function () {
            function pad(n) { return ('0' + n).slice(-2); }
            function fmtLocal(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes()); }

            var gFromD = $('#txtFromDate').val(), gFromT = $('#txtFromTime').val() || '00:00';
            var gToD = $('#txtToDate').val(), gToT = $('#txtToTime').val() || '23:59';
            var now = new Date();
            var fromVal = gFromD ? (gFromD + 'T' + gFromT) : fmtLocal(new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0));
            var toVal = gToD ? (gToD + 'T' + gToT) : fmtLocal(now);

            var _fd = new Date(fromVal), _td = new Date(toVal);
            hgPm.fromMs = isNaN(_fd.getTime()) ? null : _fd.getTime();
            hgPm.toMs = isNaN(_td.getTime()) ? null : _td.getTime();
        })();

        hgPmRenderIndication();
        hgPmBuildOpEvents();
    }

    // Keep only the Point Machine attributes the two-tab graph needs. Mirrors the
    // Point Machine filtering used by fnSearchHistory so a re-query returns the same shape.
    function hgPmFilterAttrs(data, assetId) {
        if (!data || !data.length) return [];
        var seenKeys = {};
        var dedupedData = data.filter(function (attr) {
            if (!attr) return false;
            var dataType = 'RDPMS';
            if (attr.Values) {
                for (var k in attr.Values) {
                    if (attr.Values.hasOwnProperty(k) && attr.Values[k] && attr.Values[k].DataType) { dataType = attr.Values[k].DataType; break; }
                }
            }
            var key = String(attr.AssetId) + '_' + String(attr.AttributeId) + '_' + dataType;
            if (seenKeys[key]) return false;
            seenKeys[key] = true;
            return true;
        });

        var PM_RDPMS_IND_IDS = [25, 26, 27, 28, 576, 577, 578, 579];
        var PM_WAVEFORM_ARRAY_IDS = [1001, 6001, 2001, 7001, 3001, 8001, 4001, 9001];
        var PM_ARRAY_ATTR_IDS = [1001, 6001, 3001, 8001];

        return dedupedData.filter(function (attr) {
            var dataType = 'RDPMS';
            if (attr.Values) {
                for (var k in attr.Values) {
                    if (attr.Values.hasOwnProperty(k) && attr.Values[k] && attr.Values[k].DataType) { dataType = attr.Values[k].DataType; break; }
                }
            }
            var _lookupAid = (attr.AssetId !== undefined && attr.AssetId !== null) ? attr.AssetId : assetId;
            if (dataType === 'RDPMS' && _lookupAid !== undefined && _lookupAid !== null) {
                var _aiCheckList = (typeof ASSET_INFO_MAP !== 'undefined' && ASSET_INFO_MAP[String(_lookupAid)]) || [];
                for (var _aic = 0; _aic < _aiCheckList.length; _aic++) {
                    var _aiChk = _aiCheckList[_aic];
                    if (_aiChk && _aiChk.RoleType === 'd' && String(_aiChk.Role) === String(attr.AttributeId)) { dataType = 'DataLogger'; break; }
                }
            }
            if (dataType === 'DataLogger') {
                var dlAttrId = parseInt(attr.AttributeId, 10);
                return (typeof PM_COMBINED_DATALOGGER_IDS !== 'undefined') && PM_COMBINED_DATALOGGER_IDS.indexOf(dlAttrId) !== -1;
            }
            var pmAttrId = parseInt(attr.AttributeId, 10);
            if (PM_ARRAY_ATTR_IDS.indexOf(pmAttrId) !== -1) return true;
            if (PM_WAVEFORM_ARRAY_IDS.indexOf(pmAttrId) !== -1) return true;
            if (dataType === 'PointMachine') return true;
            return PM_RDPMS_IND_IDS.indexOf(pmAttrId) !== -1;
        });
    }

    // Re-query GetHistoryData using the top-level filter date range.
    function hgPmReload() {
        if (!window.hgPm) return;

        var fDate = $('#txtFromDate').val(), fTime = $('#txtFromTime').val() || '00:00';
        var tDate = $('#txtToDate').val(), tTime = $('#txtToTime').val() || '23:59';
        var fv = fDate ? (fDate + 'T' + fTime) : null;
        var tv = tDate ? (tDate + 'T' + tTime) : null;
        var fd = fv ? new Date(fv) : null, td = tv ? new Date(tv) : null;
        if (!fd || !td || isNaN(fd.getTime()) || isNaN(td.getTime())) { showWarning('Please select From Date and To Date in the filter above.', 'Validation'); return; }
        if (fd >= td) { showWarning('From date must be before To date.', 'Validation'); return; }

        hgPm.fromMs = fd.getTime();
        hgPm.toMs = td.getTime();

        var startDate = formatDateTimeForAPI(fDate, fTime);
        var endDate = formatDateTimeForAPI(tDate, tTime);

        var assetId = hgPm.assetId;
        if (assetId === undefined || assetId === null) { showWarning('Unable to determine the asset for this graph.', 'Error'); return; }

        // Keep both Point Machine graph tabs aligned with the current filter:
        // Integration supplies Indication/Datalogger, Operation supplies event
        // rows, and OperationArray supplies waveform samples. All sources are
        // fetched page-by-page in their native API format.
        _loadCompletePmHistoryGraph(
            [{ assetId: assetId, assetName: hgPm.name }],
            startDate,
            endDate,
            function (assets) {
                var fullAsset = assets[0] || {};
                hgPm.attrs = hgPmFilterAttrs(fullAsset.attributes || [], assetId);
                hgPmRenderIndication();
                hgPmBuildOpEvents();
            },
            function (message) {
                showWarning(message || 'Failed to load Point Machine graph data.', 'Graph Data');
            }
        );
    }

    function hgPmSwitchTab(tab) {
        if (!window.hgPm) return;
        hgPm.tab = tab;
        if (tab === 'op') {
            $('#hgPmTabInd').hide(); $('#hgPmTabOp').show();
            $('#hgPmTabBtnInd').css({ color: '#64748b', 'border-bottom-color': 'transparent' });
            $('#hgPmTabBtnOp').css({ color: '#1a6e74', 'border-bottom-color': '#1a6e74' });
            if (hgPm.opChart) setTimeout(function () { hgPm.opChart.resize(); }, 30);
        } else {
            $('#hgPmTabOp').hide(); $('#hgPmTabInd').show();
            $('#hgPmTabBtnOp').css({ color: '#64748b', 'border-bottom-color': 'transparent' });
            $('#hgPmTabBtnInd').css({ color: '#1a6e74', 'border-bottom-color': '#1a6e74' });
            if (hgPm.indChart) setTimeout(function () { hgPm.indChart.resize(); }, 30);
        }
    }

    //function hgPmRenderIndication() {
    //    var el = document.getElementById('hgPmIndChart'); if (!el) return;
    //    var data = (window.hgPm && hgPm.attrs) || [];
    //    var attrDataMap = {}, allTs = {};
    //    HGPM_IND_ORDER.forEach(function (id) {
    //        for (var i = 0; i < data.length; i++) {
    //            if (data[i].AttributeId === id) {
    //                attrDataMap[id] = data[i];
    //                if (data[i].Values) {
    //                    for (var k in data[i].Values) {
    //                        var en = data[i].Values[k];
    //                        if (!en || !en.Timestamp || en.Value === undefined) continue;
    //                        var ts = en.Timestamp.TimestampDevice;
    //                        if (!ts || ts.indexOf('0001') >= 0) continue;
    //                        var t = new Date(ts).getTime();
    //                        if (isNaN(t) || t <= 0) continue;
    //                        allTs[t] = true;
    //                    }
    //                }
    //                break;
    //            }
    //        }
    //    });
    //    var sortedTs = Object.keys(allTs).map(function (x) { return parseInt(x); }).sort(function (a, b) { return a - b; });
    //    if (!sortedTs.length) { $('#hgPmIndChart').hide(); $('#hgPmIndEmpty').show(); return; }
    //    $('#hgPmIndEmpty').hide(); $('#hgPmIndChart').show();
    //    var axisMin = sortedTs[0], axisMax = sortedTs[sortedTs.length - 1];

    //    var series = [], legends = [];
    //    HGPM_IND_ORDER.forEach(function (id) {
    //        var ad = attrDataMap[id]; if (!ad) return;
    //        var meta = HGPM_IND_META[id]; var label = _hgPmIndName(id);
    //        var valueMap = {}, orig = {};
    //        if (ad.Values) {
    //            for (var k in ad.Values) {
    //                var en = ad.Values[k];
    //                if (!en || !en.Timestamp || en.Value === undefined) continue;
    //                var ts = en.Timestamp.TimestampDevice;
    //                if (!ts || ts.indexOf('0001') >= 0) continue;
    //                var t = new Date(ts).getTime();
    //                if (isNaN(t) || t <= 0) continue;
    //                var v = parseFloat(en.Value);
    //                if (!isNaN(v)) { valueMap[t] = v; orig[t] = true; }
    //            }
    //        }
    //        var points = [], last = null;
    //        sortedTs.forEach(function (t) {
    //            if (valueMap[t] !== undefined) last = valueMap[t];
    //            if (last !== null) points.push({ value: [t, last], symbol: orig[t] ? 'circle' : 'none', symbolSize: orig[t] ? 7 : 0 });
    //        });
    //        if (!points.length) return;
    //        legends.push(label);
    //        series.push({ name: label, type: 'line', step: 'end', smooth: false, connectNulls: true, showSymbol: false, clip: false, lineStyle: { width: meta.loc ? 1.8 : 2.4, color: meta.color, type: meta.loc ? [5, 3] : 'solid' }, itemStyle: { color: meta.color }, emphasis: { disabled: true }, data: points });
    //    });

    //    var ex = echarts.getInstanceByDom(el); if (ex) ex.dispose();
    //    hgPm.indChart = echarts.init(el);
    //    hgPm.indChart.setOption({
    //        backgroundColor: '#fff', animation: false,
    //        tooltip: {
    //            trigger: 'axis', axisPointer: { type: 'cross' },
    //            formatter: function (ps) {
    //                if (!ps || !ps.length) return '';
    //                var d = new Date(ps[0].value[0]);
    //                var t = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
    //                var dateStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear();
    //                var vmap = {};
    //                ps.forEach(function (p) { if (p.value && p.value[1] != null) vmap[p.seriesName] = p.value[1]; });
    //                var html = '<div style="font-weight:700;color:#0f5f65;margin-bottom:6px;border-bottom:1px solid #e2e8f0;padding-bottom:4px;">' + dateStr + ' ' + t + '</div>';
    //                HGPM_IND_ORDER.forEach(function (id) {
    //                    var nm = _hgPmIndName(id);
    //                    var val = (vmap[nm] != null) ? Number(vmap[nm]).toFixed(2) : '0.00';
    //                    html += '<div style="display:flex;align-items:center;gap:6px;padding:2px 0;"><span style="width:9px;height:9px;border-radius:50%;background:' + HGPM_IND_META[id].color + ';flex-shrink:0;"></span><span style="flex:1;white-space:nowrap;">' + nm + '</span><b style="margin-left:12px;">' + val + ' V</b></div>';
    //                });
    //                return html;
    //            }
    //        },
    //        legend: { type: 'plain', data: legends, top: 8, left: 'center', width: '96%', icon: 'roundRect', itemGap: 18, itemWidth: 22, itemHeight: 8, padding: [4, 8], textStyle: { fontSize: 11, color: '#334155' } },
    //        grid: { left: 56, right: 24, top: 96, bottom: 60, containLabel: true },
    //        xAxis: { type: 'time', min: axisMin, max: axisMax, axisLabel: { formatter: function (v) { var d = new Date(v); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); } }, splitLine: { show: true, lineStyle: { color: '#f1f5f9', type: 'dashed' } } },
    //        yAxis: { type: 'value', name: 'Voltage (V)', scale: true, splitLine: { show: true, lineStyle: { color: '#f0f0f0', type: 'dashed' } } },
    //        dataZoom: [{ type: 'slider', height: 30, bottom: 6 }, { type: 'inside' }],
    //        series: series
    //    });
    //    // Axis tooltip can still trigger a blur on peer series in some ECharts 5 builds.
    //    // Intercept the highlight and immediately downplay so no line ever fades out.
    //    hgPm.indChart.off('highlight');
    //    hgPm.indChart.on('highlight', function () { try { hgPm.indChart.dispatchAction({ type: 'downplay' }); } catch (e) { } });
    //    $(window).off('resize.hgpmind').on('resize.hgpmind', function () { if (hgPm.indChart) hgPm.indChart.resize(); });
    //}
    // Actual unsnapped X-axis time below the mouse pointer.
    // Used only by the Point Machine indication graph tooltip.
    var hgPmActualHoverMs = null;
    function hgPmRenderIndication() {
        var el = document.getElementById('hgPmIndChart'); if (!el) return;
        var data = (window.hgPm && hgPm.attrs) || [];

        // ── Selected filter window (from #txtFromDate/#txtToDate via hgPm) ──
        var startDate = (window.hgPm && hgPm.fromMs != null) ? new Date(hgPm.fromMs) : null;
        var endDate   = (window.hgPm && hgPm.toMs   != null) ? new Date(hgPm.toMs)   : null;

        // Collect only actual timestamps within the selected filter range.
        // Do not pin an older record to the selected From Date.
        var attrDataMap = {};
        var allTs = {};

        var selectedStartMs =
            startDate && !isNaN(startDate.getTime())
                ? startDate.getTime()
                : null;

        var selectedEndMs =
            endDate && !isNaN(endDate.getTime())
                ? endDate.getTime()
                : null;

        HGPM_IND_ORDER.forEach(function (id) {
            for (var i = 0; i < data.length; i++) {
                if (parseInt(data[i].AttributeId, 10) !== parseInt(id, 10)) {
                    continue;
                }

                attrDataMap[id] = data[i];

                if (data[i].Values) {
                    for (var k in data[i].Values) {
                        if (!data[i].Values.hasOwnProperty(k)) {
                            continue;
                        }

                        var en = data[i].Values[k];

                        if (
                            !en ||
                            !en.Timestamp ||
                            en.Value === undefined
                        ) {
                            continue;
                        }

                        var ts =
                            en.Timestamp.TimestampDevice ||
                            en.Timestamp.TimestampLocal;

                        if (
                            !ts ||
                            String(ts).indexOf('0001') >= 0
                        ) {
                            continue;
                        }

                        var t = new Date(ts).getTime();

                        if (
                            isNaN(t) ||
                            t <= 0
                        ) {
                            continue;
                        }

                        // Strict From Date and To Date filtering.
                        if (
                            selectedStartMs !== null &&
                            t < selectedStartMs
                        ) {
                            continue;
                        }

                        if (
                            selectedEndMs !== null &&
                            t > selectedEndMs
                        ) {
                            continue;
                        }

                        allTs[t] = true;
                    }
                }

                break;
            }
        });

        var sortedTs = Object.keys(allTs)
            .map(function (x) {
                return parseInt(x, 10);
            })
            .sort(function (a, b) {
                return a - b;
            });

        /*
         * Add only the selected boundary, not the previous record date.
         * Previous attribute values may be seeded at this boundary so
         * every indication series has the correct initial state.
         */
        if (selectedStartMs !== null) {
            if (
                sortedTs.length === 0 ||
                sortedTs[0] !== selectedStartMs
            ) {
                sortedTs.unshift(selectedStartMs);
            }
        }

        // Densify to 30s intervals — prevents "empty viewport = blank chart" during zoom.
        var FILL_INTERVAL_MS = 30 * 1000;
        if (sortedTs.length >= 2) {
            var _dens = [sortedTs[0]];
            for (var _di = 1; _di < sortedTs.length; _di++) {
                var _gap = sortedTs[_di] - sortedTs[_di - 1];
                if (_gap > FILL_INTERVAL_MS) {
                    var _steps = Math.ceil(_gap / FILL_INTERVAL_MS);
                    var _stepSize = _gap / _steps;
                    for (var _si = 1; _si < _steps; _si++) _dens.push(Math.round(sortedTs[_di - 1] + _si * _stepSize));
                }
                _dens.push(sortedTs[_di]);
            }
            sortedTs = _dens;
        }
        if (endDate && sortedTs.length) {
            var _lastRealTs = sortedTs[sortedTs.length - 1];
            var _endMs = endDate.getTime();
            if (_endMs - _lastRealTs > FILL_INTERVAL_MS) {
                var _tailSteps = Math.ceil((_endMs - _lastRealTs) / FILL_INTERVAL_MS);
                var _tailStep  = (_endMs - _lastRealTs) / _tailSteps;
                for (var _ti = 1; _ti <= _tailSteps; _ti++) sortedTs.push(Math.round(_lastRealTs + _ti * _tailStep));
            }
        }

        if (!sortedTs.length) { $('#hgPmIndChart').hide(); $('#hgPmIndEmpty').show(); return; }
        $('#hgPmIndEmpty').hide(); $('#hgPmIndChart').show();

        // X-axis anchored to selected filter (fallback to data range with pad).
        var dataMinTs = sortedTs[0];
        var dataMaxTs = sortedTs[sortedTs.length - 1];
        var dataPad = Math.max((dataMaxTs - dataMinTs) * 0.02, 60000);
        var axisMin = startDate ? startDate.getTime() : (dataMinTs - dataPad);
        var axisMax = endDate   ? endDate.getTime()   : (dataMaxTs + dataPad);

        // Per-series sorted list of REAL receive timestamps — tooltip uses this
        // to pin the header/row time to the last actual change until a new value arrives.
        hgPm.seriesOrigMap = {};
        hgPm.seriesBoundaryMap = {};

        var series = [], legends = [];
        var maxY = 0;
        HGPM_IND_ORDER.forEach(function (id) {
            var ad = attrDataMap[id]; if (!ad) return;
            var meta = HGPM_IND_META[id]; var label = _hgPmIndName(id);
            var valueMap = {};
            var orig = {};
            var origTsSorted = [];

            /*
             * Keep the latest actual value before the selected From Date.
             * It is used only as the initial state at the selected boundary.
             *
             * The original timestamp remains available for the tooltip, but
             * no graph point is plotted outside the selected date range.
             */
            var boundaryValue = null;
            var boundaryActualTs = null;

            if (ad.Values) {
                for (var k in ad.Values) {
                    if (!ad.Values.hasOwnProperty(k)) {
                        continue;
                    }

                    var en = ad.Values[k];

                    if (
                        !en ||
                        !en.Timestamp ||
                        en.Value === undefined
                    ) {
                        continue;
                    }

                    var ts =
                        en.Timestamp.TimestampDevice ||
                        en.Timestamp.TimestampLocal;

                    if (
                        !ts ||
                        String(ts).indexOf('0001') >= 0
                    ) {
                        continue;
                    }

                    var t = new Date(ts).getTime();
                    var v = parseFloat(en.Value);

                    if (
                        isNaN(t) ||
                        t <= 0 ||
                        isNaN(v)
                    ) {
                        continue;
                    }

                    /*
                     * Latest value before From Date.
                     * Do not plot it at its old timestamp.
                     */
                    if (
                        selectedStartMs !== null &&
                        t < selectedStartMs
                    ) {
                        if (
                            boundaryActualTs === null ||
                            t > boundaryActualTs
                        ) {
                            boundaryActualTs = t;
                            boundaryValue = v;
                        }

                        continue;
                    }

                    // Reject values after the selected To Date.
                    if (
                        selectedEndMs !== null &&
                        t > selectedEndMs
                    ) {
                        continue;
                    }

                    valueMap[t] = v;
                    orig[t] = true;
                    origTsSorted.push(t);
                }
            }

            /*
             * Seed the selected From Date with the latest previous state.
             * This keeps RWKR/LOC series visible even if no new value was
             * received during the selected range.
             */
            if (
                selectedStartMs !== null &&
                boundaryValue !== null
            ) {
                if (valueMap[selectedStartMs] === undefined) {
                    valueMap[selectedStartMs] = boundaryValue;
                }

                /*
                 * Do not mark the boundary point as an actual new reading.
                 * Its actual change timestamp remains boundaryActualTs.
                 */
                orig[selectedStartMs] = false;
            }

            /*
             * Include both previous and in-range actual timestamps.
             * Tooltip uses these timestamps to display the last real change.
             */
            /*
  * Keep the previous actual timestamp separately.
  * Do not mix a timestamp outside the selected range with
  * the in-range change timestamp collection.
  */
            hgPm.seriesBoundaryMap[label] =
                boundaryActualTs;

            /*
 * Keep only actual change timestamps inside the selected filter.
 * Previous-date timestamps must never enter tooltip calculations.
 */
            origTsSorted =
                origTsSorted.filter(function (timestamp) {
                    var value =
                        Number(timestamp);

                    if (isNaN(value)) {
                        return false;
                    }

                    if (
                        selectedStartMs !== null &&
                        value < selectedStartMs
                    ) {
                        return false;
                    }

                    if (
                        selectedEndMs !== null &&
                        value > selectedEndMs
                    ) {
                        return false;
                    }

                    return true;
                });

            origTsSorted.sort(function (a, b) {
                return a - b;
            });

            hgPm.seriesOrigMap[label] =
                origTsSorted;

            var points = [];
            var last = null;
            var lastActualChangeTs = boundaryActualTs;

            sortedTs.forEach(function (t) {
                if (valueMap[t] !== undefined) {
                    last = valueMap[t];

                    /*
                     * Update the actual change time only for a real reading.
                     * A seeded From Date value keeps its earlier actual time.
                     */
                    if (orig[t] === true) {
                        lastActualChangeTs = t;
                    }

                    if (last > maxY) {
                        maxY = last;
                    }
                }

                if (last !== null) {
                    points.push({
                        value: [t, last],

                        /*
                         * Keep actual change timestamp in the data point.
                         * The third value does not affect graph rendering.
                         */
                        actualChangeTs: lastActualChangeTs,

                        symbol: orig[t] ? 'circle' : 'none',
                        symbolSize: orig[t] ? 7 : 0
                    });
                }
            });
            if (!points.length) return;

            // Right-edge carry-forward to endDate.
            if (last !== null && endDate) {
                var rightEdgeMs = endDate.getTime();
                var lastPointTs = points[points.length - 1].value[0];
                if (lastPointTs < rightEdgeMs) {
                    points.push({
                        value: [rightEdgeMs, last],
                        actualChangeTs: lastActualChangeTs,
                        symbol: 'none',
                        symbolSize: 0
                    });
                }
            }

            legends.push(label);
            series.push({
                name: label, type: 'line', step: 'end', smooth: false, connectNulls: true,
                showSymbol: false, clip: false, z: 2,
                // Zoom-safe rendering (matches Telemetry Live PM graph).
                sampling: 'none', hoverAnimation: false, animation: false,
                progressive: 0, progressiveThreshold: 100000, large: false,
                lineStyle: { width: meta.loc ? 1.8 : 2.4, color: meta.color, type: meta.loc ? [5, 3] : 'solid', opacity: 0.95 },
                itemStyle: { color: meta.color, borderWidth: 2, borderColor: '#fff' },
                emphasis: { disabled: true },
                blur: { lineStyle: { opacity: 0.95 }, itemStyle: { opacity: 1 } },
                data: points
            });
        });

        var ex = echarts.getInstanceByDom(el); if (ex) ex.dispose();
        hgPm.indChart = echarts.init(el);
        hgPm.indChart.setOption({
            backgroundColor: '#fff',
            animation: false,
            animationDurationUpdate: 0,
            stateAnimation: { duration: 0 },
            tooltip: {
                trigger: 'axis',
                triggerOn: 'mousemove',
                transitionDuration: 0,
                alwaysShowContent: false,
                confine: true,

                backgroundColor: '#fff',
                borderColor: '#e2e8f0',
                borderWidth: 1,
                padding: [12, 16],

                textStyle: {
                    color: '#334155',
                    fontSize: 12
                },

                axisPointer: {
                    type: 'cross',
                    snap: false,

                    lineStyle: {
                        color: '#94a3b8',
                        width: 1,
                        type: 'dashed'
                    },

                    crossStyle: {
                        color: '#94a3b8',
                        width: 1,
                        type: 'dashed'
                    },

                    label: {
                        show: true,

                        formatter: function (params) {
                            if (params.axisDimension === 'x') {
                                var pointerDate =
                                    new Date(params.value);

                                if (
                                    isNaN(
                                        pointerDate.getTime()
                                    )
                                ) {
                                    return '';
                                }

                                return (
                                    String(
                                        pointerDate.getDate()
                                    ).padStart(2, '0') +
                                    '/' +
                                    String(
                                        pointerDate.getMonth() + 1
                                    ).padStart(2, '0') +
                                    '/' +
                                    pointerDate.getFullYear() +
                                    ' ' +
                                    String(
                                        pointerDate.getHours()
                                    ).padStart(2, '0') +
                                    ':' +
                                    String(
                                        pointerDate.getMinutes()
                                    ).padStart(2, '0') +
                                    ':' +
                                    String(
                                        pointerDate.getSeconds()
                                    ).padStart(2, '0')
                                );
                            }

                            var yValue =
                                Number(params.value);

                            return isNaN(yValue)
                                ? ''
                                : yValue.toFixed(2) + ' V';
                        }
                    }
                },

                formatter: function (ps) {
                    if (!ps || !ps.length) {
                        return '';
                    }

                    /*
 * Return the latest actual change timestamp for this series
 * at or before the current cursor position.
 */
                    function getLastActualChangeTs(seriesName, cursorTime) {
                        var timestamps =
                            hgPm.seriesOrigMap &&
                                hgPm.seriesOrigMap[seriesName]
                                ? hgPm.seriesOrigMap[seriesName]
                                : [];

                        /*
                         * When the graph uses a value carried from before the selected
                         * range, show the selected From Date instead of the old date.
                         */
                        var hasBoundaryValue =
                            hgPm.seriesBoundaryMap &&
                            hgPm.seriesBoundaryMap[seriesName] !== undefined &&
                            hgPm.seriesBoundaryMap[seriesName] !== null;

                        var lastChangeTs =
                            hasBoundaryValue && selectedStartMs !== null
                                ? selectedStartMs
                                : null;

                        /*
                         * Replace the boundary time only when an actual change exists
                         * inside the selected filter and before the hovered position.
                         */
                        for (var i = 0; i < timestamps.length; i++) {
                            var actualTs =
                                Number(timestamps[i]);

                            if (
                                isNaN(actualTs) ||
                                (
                                    selectedStartMs !== null &&
                                    actualTs < selectedStartMs
                                )
                            ) {
                                continue;
                            }

                            if (
                                selectedEndMs !== null &&
                                actualTs > selectedEndMs
                            ) {
                                continue;
                            }

                            if (actualTs <= cursorTime) {
                                lastChangeTs = actualTs;
                            } else {
                                break;
                            }
                        }

                        return lastChangeTs;
                    }
                    function formatActualChangeDateTime(timestamp) {
                        if (
                            timestamp === null ||
                            timestamp === undefined ||
                            isNaN(Number(timestamp))
                        ) {
                            return '';
                        }

                        var changeDate =
                            new Date(Number(timestamp));

                        if (isNaN(changeDate.getTime())) {
                            return '';
                        }

                        return (
                            String(changeDate.getDate()).padStart(2, '0') +
                            '/' +
                            String(changeDate.getMonth() + 1).padStart(2, '0') +
                            '/' +
                            changeDate.getFullYear() +
                            ' ' +
                            String(changeDate.getHours()).padStart(2, '0') +
                            ':' +
                            String(changeDate.getMinutes()).padStart(2, '0') +
                            ':' +
                            String(changeDate.getSeconds()).padStart(2, '0')
                        );
                    }

                    /*
                     * axisValue represents the current continuously
                     * moving X-axis crosshair location because snap=false.
                     */
                    var cursorMs =
                        ps[0].axisValue !== undefined &&
                            ps[0].axisValue !== null
                            ? Number(ps[0].axisValue)
                            : (
                                ps[0].value
                                    ? Number(ps[0].value[0])
                                    : null
                            );

                    /*
                     * Use the separately captured pointer value when
                     * ECharts supplies a nearest-series timestamp instead.
                     */
                    if (
                        hgPmActualHoverMs !== null &&
                        hgPmActualHoverMs !== undefined &&
                        !isNaN(Number(hgPmActualHoverMs))
                    ) {
                        cursorMs =
                            Number(hgPmActualHoverMs);
                    }

                    if (
                        cursorMs === null ||
                        isNaN(cursorMs)
                    ) {
                        return '';
                    }

                    // Keep displayed hover time within the selected filter.
                    if (
                        selectedStartMs !== null &&
                        cursorMs < selectedStartMs
                    ) {
                        cursorMs = selectedStartMs;
                    }

                    if (
                        selectedEndMs !== null &&
                        cursorMs > selectedEndMs
                    ) {
                        cursorMs = selectedEndMs;
                    }

                    /*
 * Tooltip/modal header must show the latest actual attribute
 * change at or before the hovered position.
 *
 * The crosshair still moves continuously using cursorMs, but
 * this header time remains unchanged between two real changes.
 */
                    var popupChangeMs = null;

                    ps.forEach(function (p) {
                        if (!p || !p.seriesName) {
                            return;
                        }

                        var seriesChangeMs =
                            getLastActualChangeTs(
                                p.seriesName,
                                cursorMs
                            );

                        if (
                            seriesChangeMs !== null &&
                            seriesChangeMs !== undefined &&
                            !isNaN(Number(seriesChangeMs)) &&
                            (
                                popupChangeMs === null ||
                                Number(seriesChangeMs) > popupChangeMs
                            )
                        ) {
                            popupChangeMs =
                                Number(seriesChangeMs);
                        }
                    });

                    /*
                     * When no real change exists before the hovered point, use the
                     * selected start only as a safe fallback.
                     */
                    if (popupChangeMs === null) {
                        popupChangeMs =
                            selectedStartMs !== null
                                ? selectedStartMs
                                : cursorMs;
                    }

                    /*
                     * Never display a popup date outside the selected filter,
                     * even when the initial graph value came from an older record.
                     */
                    if (
                        selectedStartMs !== null &&
                        popupChangeMs < selectedStartMs
                    ) {
                        popupChangeMs =
                            selectedStartMs;
                    }

                    if (
                        selectedEndMs !== null &&
                        popupChangeMs > selectedEndMs
                    ) {
                        popupChangeMs =
                            selectedEndMs;
                    }

                    var popupChangeDate =
                        new Date(popupChangeMs);

                    var hh =
                        String(
                            popupChangeDate.getHours()
                        ).padStart(2, '0');

                    var mm =
                        String(
                            popupChangeDate.getMinutes()
                        ).padStart(2, '0');

                    var ss =
                        String(
                            popupChangeDate.getSeconds()
                        ).padStart(2, '0');

                    var dd =
                        String(
                            popupChangeDate.getDate()
                        ).padStart(2, '0');

                    var month =
                        String(
                            popupChangeDate.getMonth() + 1
                        ).padStart(2, '0');

                    var year =
                        popupChangeDate.getFullYear();

                    var html =
                        '<div style="' +
                        'font-weight:700;' +
                        'color:#0f5f65;' +
                        'margin-bottom:6px;' +
                        'border-bottom:1px solid #e2e8f0;' +
                        'padding-bottom:4px;">' +
                        dd + '/' + month + '/' + year +
                        ' ' +
                        hh + ':' + mm + ':' + ss +
                        '</div>';

var hoveredSeriesMap = {};

ps.forEach(function (p) {
    if (
        !p ||
        !p.value ||
        p.value[1] === null ||
        p.value[1] === undefined
    ) {
        return;
    }

    hoveredSeriesMap[p.seriesName] = {
        value: Number(p.value[1]),
        color: p.color || '#64748b'
    };
});

HGPM_IND_ORDER.forEach(function (attributeId) {
    var seriesName =
        _hgPmIndName(attributeId);

    var hoveredSeries =
        hoveredSeriesMap[seriesName];

    /*
     * Do not print an attribute that was not returned by the API
     * or for which no graph series was created.
     */
    if (!hoveredSeries) {
        return;
    }

    var numericValue =
        hoveredSeries.value;

    if (isNaN(numericValue)) {
        return;
    }

    var seriesColor =
        hoveredSeries.color ||
        HGPM_IND_META[attributeId].color;

    var lastActualChangeTs =
        getLastActualChangeTs(
            seriesName,
            cursorMs
        );

    var lastActualChangeText =
        formatActualChangeDateTime(
            lastActualChangeTs
        );

    html +=
        '<div style="' +
        'display:flex;' +
        'align-items:center;' +
        'gap:6px;' +
        'padding:2px 0;">' +

        '<span style="' +
        'width:9px;' +
        'height:9px;' +
        'border-radius:50%;' +
        'background:' + seriesColor + ';' +
        'flex-shrink:0;"></span>' +

        '<span style="' +
        'flex:1;' +
        'white-space:nowrap;">' +
        seriesName +

        (
            lastActualChangeText
                ? (
                    ' <span style="' +
                    'color:#64748b;' +
                    'font-size:10px;' +
                    'font-weight:400;' +
                    'margin-left:5px;">' +
                    '@@ ' +
                    lastActualChangeText +
                    '</span>'
                )
                : ''
        ) +

        '</span>' +

        '<b style="' +
        'margin-left:12px;' +
        'white-space:nowrap;">' +
        numericValue.toFixed(2) +
        ' V</b>' +

        '</div>';
});
                    return html;
                }
            },
            legend: { type: 'plain', data: legends, top: 8, left: 'center', width: '96%', icon: 'roundRect', itemGap: 18, itemWidth: 22, itemHeight: 8, padding: [4, 8], textStyle: { fontSize: 11, color: '#334155' } },
            grid: { left: 56, right: 24, top: 96, bottom: 90, containLabel: true },
            xAxis: {
                type: 'time',
                boundaryGap: false,
                min: axisMin,
                max: axisMax,
                axisLabel: {
                    rotate: 20,
                    margin: 12,
                    formatter: function (v) {
                        var spanMs = axisMax - axisMin;
                        var d = new Date(v);
                        var hh = String(d.getHours()).padStart(2, '0');
                        var mm = String(d.getMinutes()).padStart(2, '0');
                        var ss = String(d.getSeconds()).padStart(2, '0');
                        var dd = String(d.getDate()).padStart(2, '0');
                        var mon = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][d.getMonth()];
                        if (spanMs <= 2 * 3600 * 1000)       return hh + ':' + mm + ':' + ss;
                        else if (spanMs <= 24 * 3600 * 1000) return hh + ':' + mm;
                        else                                  return dd + ' ' + mon + '\n' + hh + ':' + mm;
                    }
                },
                splitLine: { show: true, lineStyle: { color: '#f1f5f9', type: 'dashed' } }
            },
            yAxis: {
                type: 'value', name: 'Voltage (V)',
                min: 0,
                max: maxY > 0 ? Math.ceil(maxY * 1.15) : 30,
                splitLine: { show: true, lineStyle: { color: '#f0f0f0', type: 'dashed' } }
            },
            dataZoom: [
                { type: 'slider', height: 22, bottom: 8, start: 0, end: 100, filterMode: 'none', minValueSpan: 60 * 1000 },
                { type: 'inside', filterMode: 'none', minValueSpan: 60 * 1000, throttle: 70, zoomOnMouseWheel: true, moveOnMouseMove: false, moveOnMouseWheel: false }
            ],
            series: series
        });
        // Capture the actual X-axis pointer position continuously.
        // This avoids tooltip time snapping to the nearest history data point.
        hgPm.indChart.off('updateAxisPointer');

        hgPm.indChart.on('updateAxisPointer', function (event) {
            var pointerValue = null;

            if (
                event &&
                event.axesInfo &&
                event.axesInfo.length
            ) {
                for (var i = 0; i < event.axesInfo.length; i++) {
                    var axisInfo = event.axesInfo[i];

                    if (
                        axisInfo &&
                        (
                            axisInfo.axisDim === 'x' ||
                            axisInfo.axisDimension === 'x'
                        ) &&
                        axisInfo.value !== undefined &&
                        axisInfo.value !== null
                    ) {
                        pointerValue = Number(axisInfo.value);
                        break;
                    }
                }
            }

            if (
                pointerValue !== null &&
                !isNaN(pointerValue)
            ) {
                hgPmActualHoverMs = pointerValue;
            }
        });

        hgPm.indChart.getZr().off('globalout');

        hgPm.indChart.getZr().on('globalout', function () {
            hgPmActualHoverMs = null;
        });
        $(window).off('resize.hgpmind').on('resize.hgpmind', function () { if (hgPm.indChart) hgPm.indChart.resize(); });
    }

    //function hgPmBuildOpEvents() {
    //    var data = (window.hgPm && hgPm.attrs) || [];
    //    var byTs = {};
    //    data.forEach(function (item) {
    //        var m = HGPM_OP_ATTR_MAP[item.AttributeId];
    //        if (!m || !item.Values) return;
    //        for (var key in item.Values) {
    //            var en = item.Values[key];
    //            if (!en || typeof en.Value !== 'string' || en.Value.indexOf(',') === -1) continue;
    //            var tsRaw = en.Timestamp ? en.Timestamp.TimestampDevice : null;
    //            if (!tsRaw || tsRaw.indexOf('0001') >= 0) continue;
    //            var ms = new Date(tsRaw).getTime();
    //            if (isNaN(ms) || ms <= 0) continue;
    //            var arr = en.Value.split(',').map(function (v) { return parseFloat(v.trim()); }).filter(function (v) { return !isNaN(v); });
    //            if (!arr.length) continue;
    //            if (!byTs[ms]) byTs[ms] = { ts: ms, tsStr: tsRaw, dir: m.dir, arrays: {} };
    //            if (!byTs[ms].arrays[m.type]) byTs[ms].arrays[m.type] = arr;
    //        }
    //    });
    //    var events = Object.keys(byTs).map(function (k) { return byTs[k]; });
    //    // Keep only operation events whose timestamp falls inside the From/To filter.
    //    var _fromMs = (window.hgPm && hgPm.fromMs != null) ? hgPm.fromMs : null;
    //    var _toMs = (window.hgPm && hgPm.toMs != null) ? hgPm.toMs : null;
    //    if (_fromMs != null && _toMs != null) {
    //        events = events.filter(function (ev) { return ev.ts >= _fromMs && ev.ts <= _toMs; });
    //    }
    //    events.sort(function (a, b) { return b.ts - a.ts; });
    //    hgPm.opEvents = events; hgPm.opSel = -1;
    //    hgPmRenderOpList();
    //    $('#hgPmOpListCount').text('(' + events.length + ')');
    //    if (!events.length) { $('#hgPmOpChart').hide(); $('#hgPmOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No operation events in the selected range.').show(); return; }
    //    hgPmShowOpEvent(0);
    //}


    function hgPmBuildOpEvents() {
        var data =
            (window.hgPm && hgPm.attrs) || [];

        var rawEventsByTimestamp = {};

        // Waveform entries indexed by array AttributeId:
        //
        // Normal:
        // 1001 = A Current
        // 2001 = A Voltage
        // 3001 = B Current
        // 4001 = B Voltage
        //
        // Reverse:
        // 6001 = A Current
        // 7001 = A Voltage
        // 8001 = B Current
        // 9001 = B Voltage
        var waveformEntriesByAttributeId = {};

        function getHistoryTimestamp(entry) {
            if (!entry) return null;

            var timestampRaw = null;

            if (entry.Timestamp) {
                timestampRaw =
                    entry.Timestamp.TimestampDevice ||
                    entry.Timestamp.TimestampLocal ||
                    null;
            }

            if (!timestampRaw) {
                timestampRaw =
                    entry.TimestampDevice ||
                    entry.TimestampLocal ||
                    null;
            }

            if (
                !timestampRaw ||
                String(timestampRaw).indexOf('0001') >= 0
            ) {
                return null;
            }

            var timestampMs =
                new Date(timestampRaw).getTime();

            if (
                isNaN(timestampMs) ||
                timestampMs <= 0
            ) {
                return null;
            }

            return {
                raw: timestampRaw,
                ms: timestampMs,
                date: new Date(timestampMs)
            };
        }

        function getOperationDirection(attributeId) {
            attributeId =
                parseInt(attributeId, 10);

            if (isNaN(attributeId)) {
                return null;
            }

            // Same direction ranges used by the operational table.
            if (
                attributeId >= 1000 &&
                attributeId < 6000
            ) {
                return 'N';
            }

            if (
                attributeId >= 6000 &&
                attributeId < 10000
            ) {
                return 'R';
            }

            return null;
        }

        // ============================================================
        // Step 1:
        // Collect all PointMachine operation timestamps and waveform
        // array entries.
        // ============================================================
        data.forEach(function (item) {
            if (!item || !item.Values) return;

            var attributeId =
                parseInt(item.AttributeId, 10);

            var direction =
                getOperationDirection(attributeId);

            if (!direction) return;

            var waveformMeta =
                HGPM_OP_ATTR_MAP[attributeId] ||
                null;

            for (var key in item.Values) {
                if (!item.Values.hasOwnProperty(key)) {
                    continue;
                }

                var entry =
                    item.Values[key];

                if (!entry) continue;

                var dataType =
                    String(entry.DataType || '')
                        .trim()
                        .toLowerCase();

                // Same event source used by the operational table.
                if (dataType !== 'pointmachine') {
                    continue;
                }

                var timestamp =
                    getHistoryTimestamp(entry);

                if (!timestamp) continue;

                var rawEvent =
                    rawEventsByTimestamp[
                    timestamp.ms
                    ];

                if (!rawEvent) {
                    rawEvent = {
                        ts: timestamp.ms,
                        tsStr: timestamp.raw,
                        hasNormal: false,
                        hasReverse: false
                    };

                    rawEventsByTimestamp[
                        timestamp.ms
                    ] = rawEvent;
                }

                if (direction === 'N') {
                    rawEvent.hasNormal = true;
                } else {
                    rawEvent.hasReverse = true;
                }

                // Store waveform in the same entry shape expected by
                // pmHistoryFindNearestWaveform().
                if (
                    waveformMeta &&
                    (
                        Array.isArray(entry.Value) ||
                        (
                            typeof entry.Value === 'string' &&
                            entry.Value.indexOf(',') !== -1
                        )
                    )
                ) {
                    if (
                        !waveformEntriesByAttributeId[
                        attributeId
                        ]
                    ) {
                        waveformEntriesByAttributeId[
                            attributeId
                        ] = [];
                    }

                    waveformEntriesByAttributeId[
                        attributeId
                    ].push({
                        ts: timestamp.date,
                        value: entry.Value
                    });
                }
            }
        });

        // Keep waveform entries chronologically ordered.
        for (
            var waveformAttributeId in
            waveformEntriesByAttributeId
        ) {
            if (
                !waveformEntriesByAttributeId
                    .hasOwnProperty(
                        waveformAttributeId
                    )
            ) {
                continue;
            }

            waveformEntriesByAttributeId[
                waveformAttributeId
            ].sort(function (a, b) {
                return (
                    a.ts.getTime() -
                    b.ts.getTime()
                );
            });
        }

        // ============================================================
        // Companion operation statistics, taken from the SAME attribute
        // IDs the operational (Operation Event) table uses. For a
        // waveform base id B (1001..9001):
        //     B+1 = Avg (…002),  B+3 = Max (…004),  B+4 = OperationTime (…005)
        // Operation Time is reported only on the CURRENT attribute of an
        // end (1005/6005 for A, 3005/8005 for B); the Voltage OperationTime
        // (…005 of 2001/4001/7001/9001) is always 0, so the table sources
        // TPT from the current id regardless of which signal is shown.
        // Values are keyed by exact TimestampDevice so each stat binds to
        // its own operation instead of a neighbour's.
        // ============================================================
        var pmStatByKey = {};   // (attributeId + '|' + timestampMs) -> Number
        data.forEach(function (item) {
            if (!item || !item.Values) return;

            var statAttributeId = parseInt(item.AttributeId, 10);
            if (isNaN(statAttributeId)) return;

            // Only the scalar companions: …002 (Avg), …004 (Max),
            // …005 (OperationTime). Their base is id - slot + 1.
            var slot = statAttributeId % 1000;
            if (slot !== 2 && slot !== 4 && slot !== 5) return;

            var base = statAttributeId - slot + 1;   // 1002 -> 1001, 6005 -> 6001
            if (!getOperationDirection(base)) return; // keep only 1000..9999 PM bases

            for (var statKey in item.Values) {
                if (!item.Values.hasOwnProperty(statKey)) continue;

                var statEntry = item.Values[statKey];
                if (!statEntry) continue;

                var statDataType = String(statEntry.DataType || '')
                    .trim()
                    .toLowerCase();
                if (statDataType !== 'pointmachine') continue;

                var statTimestamp = getHistoryTimestamp(statEntry);
                if (!statTimestamp) continue;

                var statNumber = Number(statEntry.Value);
                if (isNaN(statNumber)) continue;

                pmStatByKey[statAttributeId + '|' + statTimestamp.ms] =
                    statNumber;
            }
        });

        var rawEvents =
            Object.keys(rawEventsByTimestamp)
                .map(function (key) {
                    var event =
                        rawEventsByTimestamp[key];

                    if (
                        event.hasReverse &&
                        !event.hasNormal
                    ) {
                        event.dir = 'R';
                    } else if (
                        event.hasNormal &&
                        !event.hasReverse
                    ) {
                        event.dir = 'N';
                    } else {
                        event.dir = 'U';
                    }

                    return event;
                })
                .sort(function (a, b) {
                    return a.ts - b.ts;
                });

        // ============================================================
        // Step 2:
        // Apply the exact same 120-second operation grouping used by
        // the table.
        // ============================================================
        //var PM_MERGE_WINDOW_MS =
        //    120 * 1000;

        //var representativeByTimestamp = {};

        //var previousDirection = null;
        //var previousTimestamp = null;
        //var representativeTimestamp = null;

        //rawEvents.forEach(function (event) {
        //    if (event.dir === 'U') {
        //        representativeByTimestamp[
        //            event.ts
        //        ] = event.ts;

        //        previousDirection = null;
        //        previousTimestamp = null;
        //        representativeTimestamp = null;

        //        return;
        //    }

        //    var gap =
        //        previousTimestamp !== null
        //            ? event.ts -
        //            previousTimestamp
        //            : Infinity;

        //    if (
        //        event.dir !== previousDirection ||
        //        gap > PM_MERGE_WINDOW_MS
        //    ) {
        //        representativeTimestamp =
        //            event.ts;
        //    }

        //    representativeByTimestamp[
        //        event.ts
        //    ] = representativeTimestamp;

        //    previousDirection = event.dir;
        //    previousTimestamp = event.ts;
        //});

        var representativeByTimestamp = {};

        /*
         * Keep every detected operation timestamp independent.
         *
         * Do not merge consecutive Normal or Reverse operations.
         * All existing operation detection, direction mapping,
         * waveform matching and graph rendering remain unchanged.
         */
        rawEvents.forEach(function (event) {
            representativeByTimestamp[
                event.ts
            ] = event.ts;
        });

        var groupedEventsByTimestamp = {};

        rawEvents.forEach(function (rawEvent) {
            var representativeMs =
                representativeByTimestamp[
                rawEvent.ts
                ];

            if (
                representativeMs === undefined ||
                representativeMs === null
            ) {
                representativeMs =
                    rawEvent.ts;
            }

            if (
                !groupedEventsByTimestamp[
                representativeMs
                ]
            ) {
                var representativeEvent =
                    rawEventsByTimestamp[
                    representativeMs
                    ] || rawEvent;

                groupedEventsByTimestamp[
                    representativeMs
                ] = {
                    ts: representativeMs,
                    tsStr:
                        representativeEvent.tsStr ||
                        rawEvent.tsStr,
                    dir:
                        representativeEvent.dir ||
                        rawEvent.dir,
                    arrays: {},
                    arrayTimestampMs: {},
                    arrayDistanceMs: {},
                    arrayDirection: {}
                };
            }
        });

        // ============================================================
        // Step 3:
        // Attach each waveform using the same table lookup:
        //
        // 1. Search preferred operation direction.
        // 2. Search nearest waveform within 120 seconds.
        // 3. When unavailable, search the opposite direction.
        // ============================================================
        var waveformAttributeIds = {
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

        var events =
            Object.keys(groupedEventsByTimestamp)
                .map(function (key) {
                    return groupedEventsByTimestamp[key];
                });

        events.forEach(function (event) {
            var rowTimestamp =
                new Date(event.ts);

            var preferredDirection =
                event.dir === 'R'
                    ? 'R'
                    : 'N';

            var oppositeDirection =
                preferredDirection === 'R'
                    ? 'N'
                    : 'R';

            ['AC', 'AV', 'BC', 'BV']
                .forEach(function (type) {
                    var preferredAttributeId =
                        waveformAttributeIds[
                        preferredDirection
                        ][type];

                    /*
                      * EXACT TimestampDevice only -- tolerance 0, the same gate
                      * the operational table uses (see line 17696). The old
                      * +/-120 s window let an event borrow a neighbouring
                      * operation's trace, so every event advertised AC/AV/BC/BV
                      * and the graph disagreed with the table, which showed "-".
                      */
                    var waveform =
                        pmHistoryFindNearestWaveform(
                            waveformEntriesByAttributeId[
                            preferredAttributeId
                            ] || [],
                            rowTimestamp,
                            0
                        );

                    var matchedDirection =
                        preferredDirection;

                    /*
                     * No opposite-direction fallback. A Reverse operation has
                     * Reverse waveforms (6001-9001) or it has none; plotting a
                     * Normal trace under a "Reverse" title is wrong, not a
                     * graceful degradation. The comment claiming the table does
                     * this was incorrect -- the table matches exactly.
                     */
                    if (!waveform) return;

                    event.arrays[type] =
                        waveform.values;

                    event.arrayTimestampMs[type] =
                        waveform.timestampMs;

                    event.arrayDistanceMs[type] =
                        waveform.distanceMs;

                    event.arrayDirection[type] =
                        matchedDirection;

                    // Bind the operation-tab statistics for this exact
                    // operation (same TimestampDevice as the waveform).
                    if (!event.statAvg) {
                        event.statAvg = {};
                        event.statMax = {};
                        event.statOpMs = {};
                    }

                    // Avg / Max come from THIS signal's own attribute:
                    //   Current -> …002 / …004, Voltage -> …002 / …004
                    var statBaseId =
                        waveformAttributeIds[matchedDirection][type];
                    var statTsKey = '|' + waveform.timestampMs;

                    var avgValue = pmStatByKey[(statBaseId + 1) + statTsKey];
                    var maxValue = pmStatByKey[(statBaseId + 3) + statTsKey];

                    // Operation Time is per END, always read from the end's
                    // CURRENT OperationTime id, exactly like the table's TPT:
                    //   A end -> 1005 (N) / 6005 (R), B end -> 3005 / 8005.
                    var endCurrentBaseId =
                        (type.charAt(0) === 'B')
                            ? waveformAttributeIds[matchedDirection].BC
                            : waveformAttributeIds[matchedDirection].AC;
                    var opMsValue =
                        pmStatByKey[(endCurrentBaseId + 4) + statTsKey];

                    if (avgValue !== undefined) event.statAvg[type] = avgValue;
                    if (maxValue !== undefined) event.statMax[type] = maxValue;
                    if (opMsValue !== undefined) event.statOpMs[type] = opMsValue;
                });
        });

        // ============================================================
        // Step 4:
        // Keep only events inside the selected From/To range.
        // ============================================================
        var fromMs =
            (
                window.hgPm &&
                hgPm.fromMs !== null &&
                hgPm.fromMs !== undefined
            )
                ? hgPm.fromMs
                : null;

        var toMs =
            (
                window.hgPm &&
                hgPm.toMs !== null &&
                hgPm.toMs !== undefined
            )
                ? hgPm.toMs
                : null;

        if (
            fromMs !== null &&
            toMs !== null
        ) {
            events = events.filter(
                function (event) {
                    return (
                        event.ts >= fromMs &&
                        event.ts <= toMs
                    );
                }
            );
        }

        events.sort(function (a, b) {
            return b.ts - a.ts;
        });

        hgPm.opEvents = events;
        hgPm.opSel = -1;

        hgPmRenderOpList();

        $('#hgPmOpListCount')
            .text(
                '(' + events.length + ')'
            );

        if (!events.length) {
            $('#hgPmOpChart').hide();

            $('#hgPmOpEmpty')
                .html(
                    '<i class="fas fa-info-circle fa-2x" ' +
                    'style="display:block;margin-bottom:12px;' +
                    'color:#94a3b8;"></i>' +
                    'No operation events in the selected range.'
                )
                .show();

            return;
        }

        hgPmShowOpEvent(0);
    }


    function hgPmRenderOpList() {
        var html = '';
        (hgPm.opEvents || []).forEach(function (ev, i) {
            var d = new Date(ev.ts);
            var dateStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear();
            var timeStr = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
            var active = (i === hgPm.opSel);
            //var av = ['AC', 'AV', 'BC', 'BV'].filter(function (t) { return ev.arrays[t]; }).join(' / ');
            //html += '<div onclick="hgPmShowOpEvent(' + i + ')" style="padding:8px 10px;border-radius:7px;cursor:pointer;margin-bottom:4px;border:1px solid ' + (active ? '#1a6e74' : '#eef2f7') + ';background:' + (active ? '#e6f4f4' : '#fff') + ';">' +
            //    '<div style="display:flex;align-items:center;justify-content:space-between;gap:6px;"><span style="font-size:12px;font-weight:700;color:#1a6e74;">' + timeStr + '</span>' + _hgPmDirBadge(ev.dir) + '</div>' +
            //    '<div style="font-size:10px;color:#94a3b8;">' + dateStr + '</div>' +
            //    (av ? '<div style="font-size:9px;color:#cbd5e1;margin-top:2px;">' + av + '</div>' : '') +
            //    '</div>';

            var av =
                ['AC', 'AV', 'BC', 'BV']
                    .filter(function (type) {
                        return (
                            ev.arrays &&
                            ev.arrays[type]
                        );
                    })
                    .join(' / ');

            var waveformText = av
                ? av
                : 'No waveform captured';

            var waveformColor = av
                ? '#94a3b8'
                : '#d97706';

            html +=
                '<div onclick="hgPmShowOpEvent(' + i + ')" ' +
                'style="padding:8px 10px;border-radius:7px;' +
                'cursor:pointer;margin-bottom:4px;border:1px solid ' +
                (active ? '#1a6e74' : '#eef2f7') +
                ';background:' +
                (active ? '#e6f4f4' : '#fff') +
                ';">' +

                '<div style="display:flex;align-items:center;' +
                'justify-content:space-between;gap:6px;">' +

                '<span style="font-size:12px;font-weight:700;' +
                'color:#1a6e74;">' +
                timeStr +
                '</span>' +

                _hgPmDirBadge(ev.dir) +

                '</div>' +

                '<div style="font-size:10px;color:#94a3b8;">' +
                dateStr +
                '</div>' +

                '<div style="font-size:9px;color:' +
                waveformColor +
                ';margin-top:2px;">' +
                waveformText +
                '</div>' +

                '</div>';
        });
        $('#hgPmOpList').html(html);
    }

    function hgPmSetOpType(type) {
        if (!window.hgPm) return;
        hgPm.opType = type;
        $('.hgpm-optype').removeClass('on');
        $('.hgpm-optype[data-type="' + type + '"]').addClass('on');
        if (hgPm.opSel >= 0) hgPmShowOpEvent(hgPm.opSel);
    }

    //function hgPmShowOpEvent(index) {
    //    var ev = (hgPm.opEvents || [])[index]; if (!ev) return;
    //    hgPm.opSel = index; hgPmRenderOpList();
    //    var type = hgPm.opType || 'AC';
    //    var arr = ev.arrays[type];
    //    var labels = { AC: 'A Current', AV: 'A Voltage', BC: 'B Current', BV: 'B Voltage' };
    //    var units = { AC: 'A', AV: 'V', BC: 'A', BV: 'V' };
    //    var colors = { AC: '#2563eb', AV: '#059669', BC: '#dc2626', BV: '#d97706' };
    //    var dirLabel = (ev.dir === 'R') ? 'Reverse' : 'Normal';
    //    var d = new Date(ev.ts);
    //    var whenStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
    //    $('#hgPmOpSel').html(_hgPmDirBadge(ev.dir) + ' <span style="margin-left:6px;">' + labels[type] + '</span>  |  <span style="color:#1a6e74;">' + whenStr + '</span>');
    //    if (!arr || !arr.length) {
    //        $('#hgPmOpChart').hide();
    //        $('#hgPmOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No ' + labels[type] + ' waveform captured for this operation.').show();
    //        return;
    //    }
    //    $('#hgPmOpEmpty').hide(); $('#hgPmOpChart').show();
    //    hgPmRenderArray(ev, arr, hgPm.name + ' - ' + dirLabel + ' ' + labels[type], units[type], colors[type]);
    //}


    function hgPmShowOpEvent(index) {
        var ev = (hgPm.opEvents || [])[index]; if (!ev) return;
        hgPm.opSel = index; hgPmRenderOpList();

        var labels = { AC: 'A Current', AV: 'A Voltage', BC: 'B Current', BV: 'B Voltage' };
        var units = { AC: 'A', AV: 'V', BC: 'A', BV: 'V' };
        var colors = { AC: '#2563eb', AV: '#059669', BC: '#dc2626', BV: '#d97706' };

        // FIX: Show buttons only for the end(s) actually captured for THIS
        // operation event. If only B End was captured at this timestamp, the
        // A Current / A Voltage buttons are hidden (previously they were
        // always visible and clicking them produced a "no data" empty state).
        // Also auto-switch the selected type when the current one has no data
        // for this event, so the graph always renders for a valid end.
        var availableTypes = ['AC', 'AV', 'BC', 'BV'].filter(function (t) {
            return ev.arrays && ev.arrays[t] && ev.arrays[t].length;
        });

        $('.hgpm-optype').each(function () {
            var btnType = $(this).attr('data-type');
            if (availableTypes.indexOf(btnType) === -1) {
                $(this).hide().removeClass('on');
            } else {
                $(this).show();
            }
        });

        var type = hgPm.opType || 'AC';
        if (availableTypes.length > 0 && availableTypes.indexOf(type) === -1) {
            type = availableTypes[0];
            hgPm.opType = type;
            $('.hgpm-optype').removeClass('on');
            $('.hgpm-optype[data-type="' + type + '"]').addClass('on');
        }

        var arr = ev.arrays[type];
        var dirLabel = (ev.dir === 'R') ? 'Reverse' : 'Normal';
        var d = new Date(ev.ts);
        var whenStr = String(d.getDate()).padStart(2, '0') + '/' + String(d.getMonth() + 1).padStart(2, '0') + '/' + d.getFullYear() + ' ' + String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0') + ':' + String(d.getSeconds()).padStart(2, '0');
        $('#hgPmOpSel').html(_hgPmDirBadge(ev.dir) + ' <span style="margin-left:6px;">' + (labels[type] || '') + '</span>  |  <span style="color:#1a6e74;">' + whenStr + '</span>');

        if (!availableTypes.length) {
            // No end was captured for this operation — show a single, clear
            // message instead of a per-type "no waveform" hint.
            $('#hgPmOpChart').hide();
            $('#hgPmOpStats').hide().empty();
            $('#hgPmOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No A End or B End waveform was captured for this operation.').show();
            return;
        }

        if (!arr || !arr.length) {
            $('#hgPmOpChart').hide();
            $('#hgPmOpStats').hide().empty();
            $('#hgPmOpEmpty').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:#94a3b8;"></i>No ' + labels[type] + ' waveform captured for this operation.').show();
            return;
        }
        $('#hgPmOpEmpty').hide(); $('#hgPmOpChart').show();
        hgPmRenderOpStats(ev, type, arr, units[type], colors[type]);
        hgPmRenderArray(ev, arr, hgPm.name + ' - ' + dirLabel + ' ' + labels[type], units[type], colors[type]);
    }

    // Renders the Operation Time / Avg / Max summary shown at the top-right,
    // above the Operation Event graph, for the currently selected direction +
    // end + Current/Voltage (ev + type).
    //
    // Values come from the same attribute IDs the operation table uses, bound
    // to this exact operation in hgPmBuildOpEvents:
    //   Avg            = …002 of the selected signal
    //   Max            = …004 of the selected signal
    //   Operation Time = …005 of the end's CURRENT id (A: 1005/6005, B: 3005/8005)
    // They are NOT recomputed from the raw waveform. Only when a companion
    // value is genuinely absent does this fall back to deriving it from the
    // trace so the box is never blank.
    function hgPmRenderOpStats(ev, type, arr, unit, color) {
        var $stats = $('#hgPmOpStats');
        if (!$stats.length) return;

        var n = (arr && arr.length) || 0;
        if (!n) { $stats.hide().empty(); return; }

        var avg = (ev && ev.statAvg && ev.statAvg[type] !== undefined)
            ? Number(ev.statAvg[type])
            : null;
        var mx = (ev && ev.statMax && ev.statMax[type] !== undefined)
            ? Number(ev.statMax[type])
            : null;
        var opMs = (ev && ev.statOpMs && ev.statOpMs[type] !== undefined)
            ? Number(ev.statOpMs[type])
            : null;

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

        // Match the operation table's TPT column: whole milliseconds, no
        // decimals and no seconds conversion (e.g. 2860 ms, not 2.86 s).
        var opTxt = Math.round(opMs) + ' ms';

        function _pill(label, val, valColor) {
            return '<span style="display:inline-flex;flex-direction:column;align-items:center;' +
                'padding:3px 10px;border-radius:6px;background:#f0f9ff;' +
                'border:1px solid #bae6fd;line-height:1.3;min-width:80px;text-align:center;">' +
                '<span style="font-size:9px;font-weight:700;letter-spacing:.5px;' +
                'text-transform:uppercase;color:#64748b;white-space:nowrap;">' + label + '</span>' +
                '<span style="font-size:13px;font-weight:800;color:' +
                (valColor || '#1e293b') + ';white-space:nowrap;">' + val + '</span>' +
                '</span>';
        }

        var isVoltage = (type === 'AV' || type === 'BV');
        $stats.html(
            (isVoltage
                ? _pill('AVG', avg.toFixed(2) + ' ' + unit, color)
                : _pill('MAX/AVG', mx.toFixed(2) + ' / ' + avg.toFixed(2) + ' ' + unit, color)) +
            _pill('OPERATION TIME', opTxt, '#1a6e74')
        ).css({ display: 'flex', gap: '6px', 'align-items': 'center' });
    }

    function hgPmRenderArray(ev, arr, title, unit, color) {
        var el = document.getElementById('hgPmOpChart'); if (!el) return;
        var ex = echarts.getInstanceByDom(el); if (ex) ex.dispose();
        hgPm.opChart = echarts.init(el);
        var step = 20;
        var data = arr.map(function (v, i) { return { value: [i * step, v], _absTs: ev.ts + i * step }; });
        var maxMs = (arr.length - 1) * step;
        hgPm.opChart.setOption({
            backgroundColor: '#fff',
            title: { text: title, subtext: arr.length + ' samples - dt 20 ms - span ' + maxMs + ' ms', left: 'center', top: 8, textStyle: { fontSize: 15, fontWeight: '700', color: '#1e293b' }, subtextStyle: { fontSize: 11, color: '#64748b' } },
            tooltip: {
                trigger: 'axis',
                formatter: function (ps) {
                    if (!ps || !ps.length) return '';
                    var ms = ps[0].value[0];
                    var html = '<div style="font-weight:600;margin-bottom:6px;">' + ms + ' ms</div>';
                    ps.forEach(function (p) {
                        if (!p.value || p.value[1] == null) return;
                        html += '<div style="display:flex;align-items:center;gap:6px;"><span style="width:10px;height:3px;background:' + p.color + ';"></span><span>' + (p.seriesName || 'Value') + '</span><b style="margin-left:8px;">' + Number(p.value[1]).toFixed(2) + ' ' + unit + '</b></div>';
                    });
                    return html;
                }
            },
            grid: { left: 56, right: 24, top: 70, bottom: 60, containLabel: true },
            xAxis: { type: 'value', name: 'Elapsed Time (ms)', nameLocation: 'center', nameGap: 30, min: 0, max: maxMs > 0 ? maxMs : null, axisLabel: { formatter: function (v) { return v + ' ms'; } }, splitLine: { show: true, lineStyle: { color: '#f1f5f9' } } },
            yAxis: { type: 'value', name: unit, scale: true, splitLine: { lineStyle: { color: '#f0f0f0', type: 'dashed' } } },
            dataZoom: [{ type: 'slider', height: 26, bottom: 8 }, { type: 'inside' }],
            series: [{ name: title, type: 'line', smooth: false, symbol: 'circle', symbolSize: arr.length <= 80 ? 5 : 3, showSymbol: true, lineStyle: { width: 2.5, color: color }, areaStyle: { color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [{ offset: 0, color: color + '33' }, { offset: 1, color: color + '05' }]) }, itemStyle: { color: color }, data: data }]
        });
        $(window).off('resize.hgpmop').on('resize.hgpmop', function () { if (hgPm.opChart) hgPm.opChart.resize(); });
    }

    /*
 * Returns the latest valid History value for one graph-panel attribute.
 *
 * The lookup uses the exact compositeKey created by parseChartData(),
 * preventing one asset or attribute from borrowing another series value.
 */
    function getHistoryGraphPanelValue(parsed, column) {
        if (
            !parsed ||
            !parsed.attributeData ||
            !column ||
            !column.compositeKey
        ) {
            return '—';
        }

        var entries =
            parsed.attributeData[column.compositeKey] || [];

        if (!entries.length) {
            return '—';
        }

        var selectedRange =
            typeof getSelectedRange === 'function'
                ? getSelectedRange()
                : null;

        var endMs =
            selectedRange &&
                selectedRange.endMs !== undefined &&
                selectedRange.endMs !== null
                ? Number(selectedRange.endMs)
                : Number.POSITIVE_INFINITY;

        var latestEntry = null;
        var latestTimestamp = -1;

        for (var index = 0; index < entries.length; index++) {
            var entry = entries[index];

            if (!entry) {
                continue;
            }

            var entryTimestamp =
                entry.ts instanceof Date
                    ? entry.ts.getTime()
                    : new Date(entry.ts).getTime();

            if (
                !isFinite(entryTimestamp) ||
                entryTimestamp > endMs ||
                entryTimestamp < latestTimestamp
            ) {
                continue;
            }

            if (
                entry.value === undefined ||
                entry.value === null ||
                entry.value === ''
            ) {
                continue;
            }

            latestEntry = entry;
            latestTimestamp = entryTimestamp;
        }

        /*
         * If no point lies before the selected end time, retain the latest
         * valid point returned by the API as a safe fallback.
         */
        if (!latestEntry) {
            for (
                var reverseIndex = entries.length - 1;
                reverseIndex >= 0;
                reverseIndex--
            ) {
                var reverseEntry = entries[reverseIndex];

                if (
                    reverseEntry &&
                    reverseEntry.value !== undefined &&
                    reverseEntry.value !== null &&
                    reverseEntry.value !== ''
                ) {
                    latestEntry = reverseEntry;
                    break;
                }
            }
        }

        if (!latestEntry) {
            return '—';
        }

        var rawValue = latestEntry.value;

        /*
         * DataLogger attributes are shown as Pickup/Drop rather than 1/0.
         */
        if (column.isDataLogger) {
            var binaryValue =
                String(rawValue).trim().toLowerCase();

            if (
                binaryValue === '1' ||
                binaryValue === 'true' ||
                binaryValue === 'pickup' ||
                binaryValue === 'up'
            ) {
                return 'Pickup';
            }

            if (
                binaryValue === '0' ||
                binaryValue === 'false' ||
                binaryValue === 'drop' ||
                binaryValue === 'down'
            ) {
                return 'Drop';
            }

            return String(rawValue);
        }

        var numericValue =
            Number(rawValue);

        if (!isFinite(numericValue)) {
            return String(rawValue);
        }

        /*
         * Keep integers clean and limit long decimal values.
         */
        if (Math.floor(numericValue) === numericValue) {
            return String(numericValue);
        }

        return numericValue
            .toFixed(2)
            .replace(/\.?0+$/, '');
    }


    // =====================================================================
    // HISTORY GRAPH — FULL RANGE PAGE LOADER
    // =====================================================================
    // Table paging deliberately loads one page at a time. A graph, however,
    // must represent the complete selected range, so it follows nextCursor
    // until the API reports the final page and then invokes the existing
    // graph renderer with the merged data. This path is graph-only.
    var _historyGraphLoadKey = null;
    var _historyGraphLoadedKey = null;
    var _historyGraphResults = {};

    function _historyGraphRequestInfo() {
        var type = ($('#drpAssetType option:selected').text() || '').trim().toUpperCase();
        var isPm = type === 'POINT MACHINE' || type === 'POINT_MACHINE';
        if (isPm && pmViewMode === 'operational') {
            return { url: '/FRS25/TelemetryHistory/GetPMHistoryOperation', source: 'DashboardPMHistoryOperation' };
        }
        if (isPm && pmViewMode === 'indication') {
            return { url: '/FRS25/TelemetryHistory/GetPMHistoryIntegration', source: 'DashboardPMHistoryIntegration' };
        }
        if (isPm && pmViewMode === 'vibration') {
            return { url: '/FRS25/TelemetryHistory/GetPMHistoryVibration', source: 'DashboardPMHistoryVibration', fillGaps: false };
        }
        return { url: '/FRS25/TelemetryHistory/GetHistoryDataDashboard', source: 'DashboardHistory' };
    }

    function _historyGraphDataType(attribute) {
        if (!attribute || !attribute.Values) return 'RDPMS';
        for (var valueKey in attribute.Values) {
            if (attribute.Values.hasOwnProperty(valueKey) && attribute.Values[valueKey] && attribute.Values[valueKey].DataType) {
                return attribute.Values[valueKey].DataType;
            }
        }
        return 'RDPMS';
    }

    function _historyGraphMergeAttributes(target, pageAttributes) {
        var byKey = {};
        target.forEach(function (attribute) {
            if (!attribute) return;
            var type = _historyGraphDataType(attribute);
            byKey[String(attribute.AttributeId) + '_' + type] = attribute;
        });

        (pageAttributes || []).forEach(function (attribute) {
            if (!attribute) return;
            var type = _historyGraphDataType(attribute);
            var key = String(attribute.AttributeId) + '_' + type;
            var existing = byKey[key];
            if (!existing) {
                target.push(attribute);
                byKey[key] = attribute;
                return;
            }

            existing.Values = existing.Values || {};
            var nextId = Object.keys(existing.Values).length + 1;
            Object.keys(attribute.Values || {}).forEach(function (valueKey) {
                var entry = attribute.Values[valueKey];
                if (!entry) return;
                entry.Id = nextId;
                existing.Values[String(nextId)] = entry;
                nextId++;
            });
        });
    }

    function _loadHistoryGraphSource(assetId, requestInfo, startDate, endDate, done, failed) {
        var attributes = [];
        var page = 1;
        var cursor = '';
        var requests = 0;

        function loadPage() {
            if (++requests > 500) {
                failed('The graph data API returned too many pages.');
                return;
            }

            $.ajax({
                url: requestInfo.url,
                type: 'GET',
                dataType: 'json',
                timeout: 120000,
                data: {
                    assetId: assetId,
                    startDate: startDate,
                    endDate: endDate,
                    page: page,
                    cursor: cursor,
                    fillGaps: requestInfo.fillGaps !== false
                }
            }).done(function (response) {
                if (!response || response.error) {
                    failed((response && response.error) || 'Unable to load graph data.');
                    return;
                }

                _historyGraphMergeAttributes(
                    attributes,
                    getTelemetryHistoryNativeData(response, assetId, startDate, endDate, requestInfo.source)
                );

                if (response.hasNextPage && (response.nextCursor || response.page)) {
                    cursor = response.nextCursor || '';
                    page = (response.page || page) + 1;
                    loadPage();
                    return;
                }

                done(attributes);
            }).fail(function () {
                failed('Unable to load all pages for the graph.');
            });
        }

        loadPage();
    }

    function _loadCompletePmHistoryGraph(assetResults, startDate, endDate, done, failed) {
        var sources = pmViewMode === 'vibration'
            ? [
                { url: '/FRS25/TelemetryHistory/GetPMHistoryVibration', source: 'DashboardPMHistoryVibration', fillGaps: false }
            ]
            : [
                { url: '/FRS25/TelemetryHistory/GetPMHistoryIntegration', source: 'DashboardPMHistoryIntegration' },
                { url: '/FRS25/TelemetryHistory/GetPMHistoryOperation', source: 'DashboardPMHistoryOperation' },
                { url: '/FRS25/TelemetryHistory/GetPMHistoryOperationArray', source: 'DashboardPMHistoryOperationArray', fillGaps: false }
            ];
        /*
         * PERFORMANCE: assets are independent, so they are fetched through a
         * bounded pool instead of strictly one-after-another.
         *
         * The previous loop called loadNextAsset() from inside each response
         * handler, so total time was the SUM of every round-trip -- with 27
         * IPS assets that is 27 serial whole-range calls. Results are written
         * to their ORIGINAL index so the chart's series order is unchanged.
         *
         * 6 is deliberate: enough to hide latency, below the browser's ~6
         * connections-per-host limit so requests aren't queued anyway, and
         * gentle enough not to stampede the proxy.
         */
        var GRAPH_ASSET_POOL = 6;
        var completeAssets = [];
        var _slots = new Array(assetResults.length);
        var assetIndex = 0;
        var _graphActive = 0;
        var _graphFinished = 0;
        var _graphTotal = assetResults.length;
        var _graphDone = false;

        function _graphSettle() {
            if (_graphDone) { return; }
            if (_graphFinished < _graphTotal || _graphActive > 0) { return; }
            _graphDone = true;
            // Drop empty slots (skipped assets) but keep selection order.
            for (var s = 0; s < _slots.length; s++) {
                if (_slots[s]) { completeAssets.push(_slots[s]); }
            }
            done(completeAssets);
        }

        function loadNextAsset() {
            if (assetIndex >= assetResults.length) {
                _graphSettle();
                return;
            }

            var asset = assetResults[assetIndex++];
            if (!asset || asset.assetId === undefined || asset.assetId === null) {
                loadNextAsset();
                return;
            }

            var attributes = [];
            var sourceIndex = 0;
            function loadNextSource() {
                if (sourceIndex >= sources.length) {
                    completeAssets.push({
                        assetId: asset.assetId,
                        assetName: asset.assetName || ('Asset ' + asset.assetId),
                        attributes: attributes,
                        serverPaging: false
                    });
                    loadNextAsset();
                    return;
                }

                _loadHistoryGraphSource(
                    asset.assetId,
                    sources[sourceIndex++],
                    startDate,
                    endDate,
                    function (sourceAttributes) {
                        _historyGraphMergeAttributes(attributes, sourceAttributes);
                        loadNextSource();
                    },
                    failed
                );
            }
            loadNextSource();
        }

        loadNextAsset();
    }

    function _loadCompleteHistoryGraph(assetResults, done, failed) {
        var fromDate = $('#txtFromDate').val();
        var fromTime = $('#txtFromTime').val() || '00:00';
        var toDate = $('#txtToDate').val();
        var toTime = $('#txtToTime').val() || '23:59';
        var startDate = formatDateTimeForAPI(fromDate, fromTime);
        var endDate = formatDateTimeForAPI(toDate, toTime);

        //var selectedType = ($('#drpAssetType option:selected').text() || '').trim().toUpperCase();
        //if (selectedType === 'POINT MACHINE' || selectedType === 'POINT_MACHINE') {
        //    _loadCompletePmHistoryGraph(assetResults, startDate, endDate, done, failed);
        //    return;
        //}

        ///*
        // * Graph binding (non-PM): use the legacy HistoryValue API via
        // * GetHistoryData, as the previous Index.cshtml did. It returns the
        // * complete range in ONE call in the attribute-major shape the chart
        // * pipeline already consumes, so no cursor paging and no native
        // * adaptation are needed. The request uses the selected filter dates
        // * unchanged (the one-day-prior shift applies to the table only).
        // */

        /*
         * Graph binding (ALL asset types, Point Machine included): use the
         * HistoryValue API via GetHistoryData.
         *
         * It returns the complete range in ONE call per asset, already in the
         * attribute-major shape the chart pipeline consumes -- no cursor
         * paging, no native adaptation. The request uses the selected filter
         * dates unchanged.
         *
         * Point Machine previously branched to _loadCompletePmHistoryGraph,
         * which merged three Dashboard PM endpoints (Integration + Operation +
         * OperationArray), or one for vibration. That function is left defined
         * but unreachable, so restoring the branch is a one-line revert.
         */

        /*
         * PERFORMANCE: assets are independent, so they are fetched through a
         * bounded pool instead of strictly one-after-another.
         *
         * The previous loop called loadNextAsset() from inside each response
         * handler, so total time was the SUM of every round-trip -- with 27
         * IPS assets that is 27 serial whole-range calls. Results are written
         * to their ORIGINAL index so the chart's series order is unchanged.
         *
         * 6 matches the browser's per-host connection limit: more would just
         * queue, fewer would leave latency unhidden.
         */
        var GRAPH_ASSET_POOL = 6;
        var completeAssets = [];
        var _slots = new Array(assetResults.length);
        var assetIndex = 0;
        var _graphActive = 0;
        var _graphFinished = 0;
        var _graphTotal = assetResults.length;
        var _graphDone = false;

        function _graphSettle() {
            if (_graphDone) { return; }
            if (_graphFinished < _graphTotal || _graphActive > 0) { return; }
            _graphDone = true;
            // Drop empty slots (skipped assets) but keep selection order.
            for (var s = 0; s < _slots.length; s++) {
                if (_slots[s]) { completeAssets.push(_slots[s]); }
            }
            done(completeAssets);
        }

        function loadNextAsset() {
            if (assetIndex >= assetResults.length) {
                _graphSettle();
                return;
            }

            var _slotIdx = assetIndex;
            var inputAsset = assetResults[assetIndex++];
            if (!inputAsset || inputAsset.assetId === undefined || inputAsset.assetId === null) {
                _graphFinished++;
                loadNextAsset();
                return;
            }

            _graphActive++;
            $.ajax({
                url: '/FRS25/TelemetryHistory/GetHistoryData',
                type: 'GET',
                dataType: 'json',
                timeout: 300000,
                data: {
                    assetId: inputAsset.assetId,
                    startDate: startDate,
                    endDate: endDate
                }
            }).done(function (response) {
                // Old-file contract: HistoryValue may answer with Data, data,
                // or a bare array. Degrade a bad shape or an error payload to
                // an empty asset instead of failing the whole graph -- failing
                // here used to fall back to the 50-row table page and plot a
                // partial graph.
                var entries = (response && (response.Data || response.data))
                    || (Array.isArray(response) ? response : []);
                if ((response && response.error) || !Array.isArray(entries)) {
                    entries = [];
                }

                // Write to the asset's ORIGINAL index -- with a pool the
                // responses arrive out of order, and push() would scramble
                // the chart's series order.
                _slots[_slotIdx] = {
                    assetId: inputAsset.assetId,
                    assetName: inputAsset.assetName || ('Asset ' + inputAsset.assetId),
                    attributes: entries,
                    serverPaging: false
                };

                _graphActive--;
                _graphFinished++;
                _pumpGraphPool();
            }).fail(function () {
                // One asset failing must not abort the whole graph now that
                // several are in flight: record it empty and carry on.
                _slots[_slotIdx] = {
                    assetId: inputAsset.assetId,
                    assetName: inputAsset.assetName || ('Asset ' + inputAsset.assetId),
                    attributes: [],
                    serverPaging: false
                };
                _graphActive--;
                _graphFinished++;
                _pumpGraphPool();
            });
        }

        function _pumpGraphPool() {
            if (_graphDone) { return; }
            while (_graphActive < GRAPH_ASSET_POOL && assetIndex < assetResults.length) {
                loadNextAsset();
            }
            _graphSettle();
        }

        if (!_graphTotal) { done([]); return; }
        _pumpGraphPool();
    }

    function _historyGraphKey(assetResults) {
        var ids = (assetResults || []).map(function (asset) { return asset && asset.assetId; }).filter(function (id) { return id !== undefined && id !== null; }).join(',');
        return [
            ids,
            $('#txtFromDate').val(), $('#txtFromTime').val(),
            $('#txtToDate').val(), $('#txtToTime').val(),
            ($('#drpAssetType option:selected').text() || '').trim(),
            pmViewMode
        ].join('|');
    }

    function renderChart(assetResults, viewType, skipFullRangeLoad) {
        var graphKey = _historyGraphKey(assetResults);
        if (!skipFullRangeLoad && _historyGraphLoadedKey === graphKey && _historyGraphResults[graphKey]) {
            // Switching Table → Graph with unchanged filters must reuse the
            // complete graph dataset, not the table's current 50-row page.
            assetResults = _historyGraphResults[graphKey];
            skipFullRangeLoad = true;
        }
        if (!skipFullRangeLoad && _historyGraphLoadedKey !== graphKey) {
            if (_historyGraphLoadKey === graphKey) return;

            _historyGraphLoadKey = graphKey;
            $('#divChartContainer').html('<div class="text-center text-muted p-5"><i class="fas fa-spinner fa-spin fa-2x mb-3"></i><br/>Loading complete graph history...</div>');
            _loadCompleteHistoryGraph(
                assetResults || [],
                function (completeAssets) {
                    _historyGraphLoadKey = null;
                    _historyGraphLoadedKey = graphKey;
                    // NEW
                    _historyGraphResults[graphKey] = completeAssets;
                    /*
                     * Graph view no longer runs the table's Dashboard load, so
                     * lastAssetResults would otherwise hold the id-only stubs
                     * and exports would be empty. Point it at the real data.
                     */
                    if (completeAssets) {
                        /*
                         * Tag the array (not the assets) so renderByViewType
                         * can tell graph data from table data.
                         */
                        completeAssets.isGraphPayload = true;
                    }
                    if (completeAssets && completeAssets.length) {
                        /*
                         * Keep the Table payload before repointing, so that
                         * switching back to Table restores the dataset that
                         * carries operationApi / pageTimestamps. Checked
                         * before the repoint below, while lastAssetResults
                         * still holds the old value.
                         */
                        if (_thIsRealTablePayload(lastAssetResults)) {
                            lastTableAssetResults = lastAssetResults;
                        }
                        lastAssetResults = completeAssets;
                    }
                    renderChart(completeAssets, viewType, true);
                },
                function (message) {
                    _historyGraphLoadKey = null;
                    showWarning(message, 'Graph Data');
                    // Preserve the previously working graph if a later page fails.
                    renderChart(assetResults, viewType, true);
                }
            );
            return;
        }
        var _pmSelTop = $('#drpAssetType option:selected').text().trim().toUpperCase();
        if ((_pmSelTop === 'POINT MACHINE' || _pmSelTop === 'POINT_MACHINE') && assetResults && assetResults.length) {
            renderHistoryPmTabs(assetResults);
            return;
        }
        chartHiddenSeries = {};
        chartActiveFilter = 'All';
        if (historyEChart) { historyEChart.dispose(); historyEChart = null; }

        //var parsed = parseChartData(assetResults);
        //if (!parsed.timestamps.length || !parsed.columns.length) { renderNoData(); return; }

        //var derivedCols = buildDerivedCols(parsed);
        //var allCols = parsed.columns.concat(derivedCols);

        var parsed = parseChartData(assetResults);
        if (!parsed.timestamps.length || !parsed.columns.length) { renderNoData(); return; }

        // ===== IPS: match the table -- filter to the active tab, then group =====
        // The GRAPH does not split IPS by the table's Voltage / Current /
        // Digital tabs. Every IPS column is kept here and the existing unit
        // filter bar does the selecting instead:
        //
        //   All  -> voltage + current + digital together
        //   mA   -> current only  (getAttrUnit: IIPS -> 'mA')
        //   V    -> voltage only  (getAttrUnit: VIPS -> 'V')
        //   0/1  -> digital only  (getAttrUnit: isDataLogger -> '0/1')
        //
        // Filtering by ipsViewMode here removed two of the three buckets from
        // parsed.columns before the chips were ever built, so 'All' could show
        // only one bucket and the other two chips matched nothing.
        //
        // Grouping still runs: it only touches current-classified columns,
        // which is independent of which chip is active.
        if (typeof isIpsHistoryAssetType === 'function' && isIpsHistoryAssetType()) {
            _ipsGroupChargingColumns(parsed);
        }
        // ===== END IPS GROUPING =====

        // ===== POINT MACHINE: filter chart columns to match the table =====
        var _pmSelType = $('#drpAssetType option:selected').text().trim().toUpperCase();
        var _isPMChart = (_pmSelType === 'POINT MACHINE' || _pmSelType === 'POINT_MACHINE');
        if (_isPMChart) {
            var _colOrder = (pmViewMode === 'operational') ? PM_OP_COL_ORDER : PM_IND_COL_ORDER;

            // Build per-asset assetGroups from parsed.columns
            var _pmAssetMap = {}, _pmAssetGroups = [];
            parsed.columns.forEach(function (c) {
                if (!_pmAssetMap[c.assetId]) {
                    _pmAssetMap[c.assetId] = true;
                    _pmAssetGroups.push({ assetId: c.assetId, assetName: c.assetName });
                }
            });

            // Reuse the same filtering logic the table uses
            var _pmOrdered = buildPMOrderedColumns(parsed.columns, _colOrder, null, _pmAssetGroups);

            // Drop "missing" placeholders (those have no data) and keep only real columns
            parsed.columns = _pmOrdered.filter(function (c) { return !c.missing && c.compositeKey; });
        }
        // ===== END POINT MACHINE FILTER =====

        var derivedCols = buildDerivedCols(parsed);
        var allCols = parsed.columns.concat(derivedCols);

        // IPS: show analog (VIPS/IIPS) before digital (DataLogger) in both the
        // attribute checkbox panel and the hover tooltip.
        if (typeof isIpsHistoryAssetType === 'function' && isIpsHistoryAssetType()) {
            allCols.sort(function (a, b) {
                var aD = a.isDataLogger ? 1 : 0;
                var bD = b.isDataLogger ? 1 : 0;
                return aD - bD;
            });
        }

        var colorMap = {};
        allCols.forEach(function (col, idx) { colorMap[col.compositeKey] = _hgGC[idx % _hgGC.length]; });

        var multiAsset = assetResults.length > 1;
        _hgParsed = parsed;
        _hgDerivedCols = derivedCols;
        _hgAllCols = allCols;

        var $container = $('#divChartContainer').empty();

        // ── Header with 1H / 3H / 6H / 12H / 24H buttons ──
        //var qzBtns = ['1H', '3H', '6H', '12H', '24H'].map(function (t) {
        //    return '<button type="button" class="hg-qzoom-btn" data-qz="' + t + '">' + t + '</button>';
        //}).join('');

        // Build asset name list (like Telemetry Live title)
        var _assetNames = [];
        (assetResults || []).forEach(function (a) {
            if (a && a.assetName) _assetNames.push(a.assetName);
        });
        var _assetTitle = _assetNames.length ? _assetNames.join(', ') : '';

                var isIpsHistoryGraph =
            typeof isIpsHistoryAssetType === 'function' &&
            isIpsHistoryAssetType();

        var isEldHistoryGraph =
            typeof isEldHistoryAssetType === 'function' &&
            isEldHistoryAssetType();

        var supportsStackedHistoryGraph =
            isIpsHistoryGraph ||
            isEldHistoryGraph;

        /*
         * Match Telemetry Live:
         * multiple IPS/ELD assets initially use Stacked mode;
         * a single asset initially uses Overlay mode.
         */
        if (supportsStackedHistoryGraph) {
            historyIpsGraphMode =
                assetResults.length > 1
                    ? 'stacked'
                    : historyIpsGraphMode || 'overlay';
        }

        var historyModeControls = '';

        if (supportsStackedHistoryGraph) {
            historyModeControls =
                '<div class="hg-mode-group">' +

                '<button type="button" ' +
                'class="hg-mode-btn' +
                (historyIpsGraphMode === 'overlay' ? ' active' : '') +
                '" data-mode="overlay">' +
                '<i class="fas fa-clone"></i> Overlay' +
                '</button>' +

                '<button type="button" ' +
                'class="hg-mode-btn' +
                (historyIpsGraphMode === 'stacked' ? ' active' : '') +
                '" data-mode="stacked">' +
                '<i class="fas fa-bars"></i> Stacked' +
                '</button>' +

                '</div>';
        }

        // No Voltage / Current / Digital tab bar on the graph -- the unit
        // filter bar (All / mA / V / 0/1) below is the only selector here.
        // The tab bar stays on the pivot table only.

        $container.append(
            '<div style="' +
            'display:flex;' +
            'align-items:center;' +
            'justify-content:space-between;' +
            'gap:12px;' +
            'padding:7px 12px;' +
            'background:linear-gradient(135deg,#042c43,#0a4a6e);' +
            'border-radius:8px 8px 0 0;">' +

            '<span style="' +
            'color:#fff;' +
            'font-size:13px;' +
            'font-weight:700;' +
            'white-space:nowrap;">' +

            '<i class="fas fa-line-chart" ' +
            'style="margin-right:6px;font-size:11px;opacity:.85;"></i>' +

            'Telemetry History Graph' +
            '</span>' +

            '<div style="' +
            'display:flex;' +
            'align-items:center;' +
            'justify-content:flex-end;' +
            'gap:10px;' +
            'flex-wrap:wrap;">' +

            historyModeControls +

            '<span style="color:#a7f3d0;font-size:10.5px;">' +
            parsed.columns.length +
            ' series &nbsp;&middot;&nbsp; ' +
            parsed.timestamps.length +
            ' pts' +
            '</span>' +

            '</div>' +
            '</div>'
        );

        // Graph-local From Date / To Date filter.
        // It updates the existing history filters and reuses fnSearchHistory(),
        // keeping the table, graph and exports on the same date range.
        function _hgToLocalInput(ms) {
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

        var _hgRange = getSelectedRange();
        var _hgNow = Date.now();

        var _hgFromMs = _hgRange
            ? _hgRange.startMs
            : new Date(
                new Date().setHours(0, 0, 0, 0)
            ).getTime();

        var _hgToMs = _hgRange
            ? Math.min(_hgRange.endMs, _hgNow)
            : _hgNow;

        var _hgMax = _hgToLocalInput(_hgNow);

        //$container.append(
        //    '<div style="display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding:7px 12px;background:#eef6f7;border:1px solid #d1e6e8;border-top:none;">' +
        //    '<span style="font-size:10px;color:#0f5f65;font-weight:700;text-transform:uppercase;letter-spacing:.5px;"><i class="far fa-calendar" style="margin-right:4px;"></i>Date Range:</span>' +
        //    '<label style="font-size:11px;color:#475569;display:flex;align-items:center;gap:4px;margin:0;">From Date <input type="datetime-local" id="hgFromDateTime" max="' + _hgMax + '" style="font-size:11px;padding:3px 6px;border:1px solid #cbd5e1;border-radius:4px;background:#fff;"></label>' +
        //    '<label style="font-size:11px;color:#475569;display:flex;align-items:center;gap:4px;margin:0;">To Date <input type="datetime-local" id="hgToDateTime" max="' + _hgMax + '" style="font-size:11px;padding:3px 6px;border:1px solid #cbd5e1;border-radius:4px;background:#fff;"></label>' +
        //    '<button type="button" id="hgDateRangeLoad" style="background:#1a6e74;color:#fff;border:1px solid #1a6e74;border-radius:4px;padding:4px 12px;font-size:11px;font-weight:600;cursor:pointer;"><i class="fas fa-search" style="font-size:10px;"></i> Load</button>' +
        //    '</div>'
        //);

        //$container.find('#hgFromDateTime').val(
        //    _hgToLocalInput(_hgFromMs)
        //);

        //$container.find('#hgToDateTime').val(
        //    _hgToLocalInput(_hgToMs)
        //);

        //$container.find('#hgDateRangeLoad').on('click', function () {
        //    var fromVal = $container
        //        .find('#hgFromDateTime')
        //        .val();

        //    var toVal = $container
        //        .find('#hgToDateTime')
        //        .val();

        //    var fromMs = fromVal
        //        ? new Date(fromVal).getTime()
        //        : NaN;

        //    var toMs = toVal
        //        ? new Date(toVal).getTime()
        //        : NaN;

        //    var nowMs = Date.now();

        //    if (isNaN(fromMs) || isNaN(toMs)) {
        //        showWarning(
        //            'Please select valid From Date and To Date.',
        //            'Validation'
        //        );
        //        return;
        //    }

        //    if (fromMs > nowMs || toMs > nowMs) {
        //        showWarning(
        //            'Future dates are not allowed.',
        //            'Validation'
        //        );
        //        return;
        //    }

        //    if (fromMs >= toMs) {
        //        showWarning(
        //            'From Date must be earlier than To Date.',
        //            'Validation'
        //        );
        //        return;
        //    }

        //    var fromDate = new Date(fromMs);
        //    var toDate = new Date(toMs);

        //    var pad = function (n) {
        //        return String(n).padStart(2, '0');
        //    };

        //    $('#txtFromDate').val(
        //        fromDate.getFullYear() + '-' +
        //        pad(fromDate.getMonth() + 1) + '-' +
        //        pad(fromDate.getDate())
        //    );

        //    $('#txtFromTime').val(
        //        pad(fromDate.getHours()) + ':' +
        //        pad(fromDate.getMinutes())
        //    );

        //    $('#txtToDate').val(
        //        toDate.getFullYear() + '-' +
        //        pad(toDate.getMonth() + 1) + '-' +
        //        pad(toDate.getDate())
        //    );

        //    $('#txtToTime').val(
        //        pad(toDate.getHours()) + ':' +
        //        pad(toDate.getMinutes())
        //    );

        //    fnSearchHistory();
        //});

        // ── Select All / Unselect All + counter bar ──
        $container.append(
            '<div style="display:flex;align-items:center;justify-content:space-between;padding:8px 12px;background:#f1f5f9;border:1px solid #e2e8f0;border-top:none;">' +
            '<div style="display:flex;align-items:center;gap:8px;">' +
            '<span style="font-size:10.5px;font-weight:700;color:#475569;text-transform:uppercase;letter-spacing:.5px;">Attributes:</span>' +
            '<button type="button" class="hg-sel-btn" id="hgSelectAll"><i class="fas fa-check-square"></i> Select All</button>' +
            '<button type="button" class="hg-sel-btn" id="hgUnselectAll"><i class="far fa-square"></i> Unselect All</button>' +
            '</div>' +
            '<span class="hg-attr-counter" id="hgAttrCounter" style="font-size:11px;font-weight:600;color:#64748b;"></span>' +
            '</div>'
        );

        // ── Attribute checkboxes ──
        var $chips = $('<div style="display:flex;flex-wrap:wrap;gap:10px 14px;padding:10px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-top:none;"></div>');

        allCols.forEach(function (col) {
            var co = colorMap[col.compositeKey];

            // PM: prefix "A End - " / "B End - " so duplicate labels (e.g., IPT N/R (A))
            // are distinguishable in the chip list
            var _grpPrefix = '';
            if (col.group === 'A') _grpPrefix = 'A End - ';
            else if (col.group === 'B') _grpPrefix = 'B End - ';

            var _dispLabel = _grpPrefix + col.displayName;
            // A group column's assetName IS its display name -- appending it
            // would read "Batt Charging — Batt Charging".
            var label = (multiAsset && !col.isGroup) ? (col.assetName + ' — ' + _dispLabel) : _dispLabel;
            var unit = getAttrUnit(col);
            var panelValue =
                getHistoryGraphPanelValue(
                    parsed,
                    col
                );

            var $row = $(
                '<label class="hg-chk-row" ' +
                'data-key="' + col.compositeKey + '" ' +
                'data-unit="' + unit + '" ' +

                'style="' +
                'display:inline-flex;' +
                'align-items:center;' +
                'gap:6px;' +
                'cursor:pointer;' +
                'user-select:none;' +
                'font-size:11.5px;' +
                'color:#1e293b;' +
                'min-width:230px;' +
                'padding:3px 7px;' +
                'border:1px solid #e2e8f0;' +
                'border-radius:5px;' +
                'background:#ffffff;' +
                '">' +

                '<input type="checkbox" checked ' +
                'style="' +
                'width:13px;' +
                'height:13px;' +
                'accent-color:#4f46e5;' +
                'cursor:pointer;' +
                'margin:0;' +
                'flex-shrink:0;' +
                '">' +

                '<span style="' +
                'width:9px;' +
                'height:9px;' +
                'border-radius:50%;' +
                'background:' + co + ';' +
                'display:inline-block;' +
                'box-shadow:0 0 0 1px rgba(0,0,0,.08);' +
                'flex-shrink:0;' +
                '">' +
                '</span>' +

                '<span style="' +
                'overflow:hidden;' +
                'text-overflow:ellipsis;' +
                'white-space:nowrap;' +
                'flex:1;' +
                'min-width:0;' +
                '" title="' + label + '">' +

                label +

                (
                    col.isDerived
                        ? ' <span style="' +
                        'font-size:9px;' +
                        'color:#d97706;' +
                        'font-weight:700;' +
                        '">[D]</span>'
                        : ''
                ) +

                '</span>' +

                '</span>' +

                '</label>'
            );
            $chips.append($row);
        });
        $container.append($chips);

        // ── Filter bar: All / mA / V / 0/1 / Derived ──
        //var filterBtns = ['All', 'mA', 'V', '0/1', 'Derived'].map(function (f) {
        //    return '<button type="button" class="hg-filter-btn' + (f === 'All' ? ' active' : '') + '" data-f="' + f + '">' + f + '</button>';
        //}).join('');
        var _filterUnits = ['All', 'mA', 'V', '0/1'];
        if (_hgDerivedCols && _hgDerivedCols.length) _filterUnits.push('Derived');  // Track only
        var filterBtns = _filterUnits.map(function (f) {
            return '<button type="button" class="hg-filter-btn' + (f === 'All' ? ' active' : '') + '" data-f="' + f + '">' + f + '</button>';
        }).join('');
        $container.append(
            '<div style="display:flex;justify-content:flex-start;align-items:center;gap:6px;padding:6px 12px;background:#f8fafc;border:1px solid #e2e8f0;border-top:none;">' +
            '<span style="font-size:10.5px;font-weight:700;color:#64748b;text-transform:uppercase;margin-right:4px;">Filter:</span>' +
            '<div class="hg-filter-group">' + filterBtns + '</div>' +
            '</div>'
        );

        // ── Chart div ──
        $container.append('<div id="hgChartDiv" style="width:100%;height:380px;background:#fff;border:1px solid #e2e8f0;border-top:none;border-radius:0 0 8px 8px;"></div>');

        // ── Wire events ──
        $container.on('change', '.hg-chk-row input[type=checkbox]', function () {
            var key = $(this).closest('.hg-chk-row').data('key');
            chartHiddenSeries[key] = !this.checked;
            updateAttrCounter();
            if (_hgRenderFn) _hgRenderFn();
        });

        $container.find('#hgSelectAll').on(
            'click',
            function () {
                toggleAllAttrs(true);
            }
        );

        $container.find('#hgUnselectAll').on(
            'click',
            function () {
                toggleAllAttrs(false);
            }
        );

        /*
         * IPS-only Overlay/Stacked mode switch.
         * Reuses the already-loaded History data; no API request is repeated.
         */
        $container
            .find('.hg-mode-btn')
            .off('click.historyIpsMode')
            .on(
                'click.historyIpsMode',
                function () {
                    var requestedMode =
                        String(
                            $(this).data('mode') || ''
                        ).toLowerCase();

                    if (
                        requestedMode !== 'overlay' &&
                        requestedMode !== 'stacked'
                    ) {
                        return;
                    }

                    if (
                        historyIpsGraphMode === requestedMode
                    ) {
                        return;
                    }

                    historyIpsGraphMode =
                        requestedMode;

                    $container
                        .find('.hg-mode-btn')
                        .removeClass('active');

                    $(this)
                        .addClass('active');

                    if (_hgRenderFn) {
                        _hgRenderFn();
                    }
                }
            );



        $container.find('.hg-filter-btn').on('click', function () {
            var unit = $(this).data('f');
            chartActiveFilter = unit;
            $container.find('.hg-filter-btn').removeClass('active');
            $(this).addClass('active');
            $container.find('.hg-chk-row').each(function () {
                var $row = $(this);
                //var match = (unit === 'All') || ($row.data('unit') === unit);
                var match = (unit === 'All') || (unit === 'Derived' ? String($row.data('key') || '').indexOf('_derived_') > -1 : $row.data('unit') === unit);
                $row.css('display', match ? 'inline-flex' : 'none');
                var key = $row.data('key');
                if (!match) chartHiddenSeries[key] = true;
                else chartHiddenSeries[key] = !$row.find('input[type=checkbox]').prop('checked');
            });
            updateAttrCounter();
            if (_hgRenderFn) _hgRenderFn();
        });

        updateAttrCounter();

        _hgRenderFn = function () {
            /*
             * Refresh the panel values from the same loaded History dataset.
             * This is required after filter, selection or mode changes.
             */
            $('#divChartContainer .hAttrVal[data-key]').each(function () {
                var $valueElement =
                    $(this);

                var compositeKey =
                    String(
                        $valueElement.data('key') || ''
                    );

                if (!compositeKey) {
                    return;
                }

                var matchedColumn =
                    parsed.columns.filter(
                        function (column) {
                            return (
                                column &&
                                String(
                                    column.compositeKey
                                ) === compositeKey
                            );
                        }
                    )[0];

                if (!matchedColumn) {
                    return;
                }

                $valueElement.text(
                    getHistoryGraphPanelValue(
                        parsed,
                        matchedColumn
                    )
                );
            });

            _hgDrawECharts(
                parsed,
                colorMap,
                multiAsset
            );
        };
        _hgRenderFn();

        $('#dataInfo').html('');
    }

    function toggleAllAttrs(checked) {
        $('#divChartContainer .hg-chk-row').each(function () {
            var $row = $(this);
            if ($row.css('display') === 'none') return; // respect filter bar
            var key = $row.data('key');
            chartHiddenSeries[key] = !checked;
            $row.find('input[type=checkbox]').prop('checked', checked);
        });
        updateAttrCounter();
        if (_hgRenderFn) _hgRenderFn();
    }

    function updateAttrCounter() {
        var $rows = $('#divChartContainer .hg-chk-row').filter(function () { return $(this).css('display') !== 'none'; });
        var total = $rows.length;
        var active = $rows.find('input[type=checkbox]:checked').length;
        $('#hgAttrCounter').text(active + ' / ' + total + ' selected');
    }

    /*
 * IPS History stacked renderer.
 *
 * One visible series receives one independent grid, X axis and Y axis.
 * Zoom remains common across lanes, but hover/tooltip is lane-specific.
 *
 * `actualPointsByName` is the seriesDataMap built by the caller: series name ->
 * that attribute's REAL change samples ([ms, value] pairs, ascending). It is NOT
 * the same as series.data, which also carries a plotted point at every other
 * attribute's timestamp so the step line spans the window. The tooltip's
 * "changed" time must come from the change samples, otherwise every carry-
 * forward point would read as a fresh change.
 */
    function applyIpsHistoryStackedMode(
        chartInstance,
        sourceSeries,
        startMs,
        endMs,
        actualPointsByName
    ) {
        if (
            !chartInstance ||
            !sourceSeries ||
            !sourceSeries.length
        ) {
            return false;
        }

        var laneHeight = 90;
        var laneGap = 34;
        var topPadding = 34;
        var bottomPadding = 76;

        var laneCount =
            sourceSeries.length;

        var requiredHeight =
            topPadding +
            laneCount * laneHeight +
            Math.max(0, laneCount - 1) * laneGap +
            bottomPadding;

        $('#hgChartDiv').css(
            'height',
            Math.max(500, requiredHeight) + 'px'
        );

        /*
         * The chart was initialized before its final stacked height was known.
         * Resize it before applying the lane layout.
         */
        chartInstance.resize();

        var grids = [];
        var xAxes = [];
        var yAxes = [];
        var stackedSeries = [];
        var laneGraphics = [];

        /*
         * Stable series ID -> clean attribute-only tooltip name.
         *
         * Kept local to the IPS stacked renderer so it cannot affect any other
         * Telemetry History graph.
         */
               var stackedAttributeNameMap = {};
        var stackedBinarySeriesMap = {};

        /*
         * Stable series ID -> that lane's OWN change samples.
         *
         * Each stacked lane is an independent graph with its own X axis, so its
         * changed date & time is resolved from its own samples alone. Nothing is
         * shared, merged or maxed across lanes: two assets whose values changed
         * at different instants keep two different timestamps.
         */
        var stackedLaneChangeDataMap = {};

        /*
         * Value in effect at `cursorMs` on ONE lane, plus the timestamp at
         * which that value actually began -- the same "changed date & time"
         * the table column shows.
         *
         * Binary-search the last change sample at or before the cursor, then
         * walk back over repeated identical readings: a historian that records
         * 24.50 at 03:20, 03:21 and 03:22 changed once, at 03:20.
         *
         * Scoped to a single lane's samples on purpose -- lanes are never
         * combined, so one lane's change can never move another lane's time.
         */
        function getStackedLaneValueAtTime(
            seriesId,
            cursorMs
        ) {
            var pts =
                stackedLaneChangeDataMap[
                String(seriesId || '')
                ];

            if (
                !pts ||
                !pts.length ||
                isNaN(cursorMs)
            ) {
                return {
                    value: null,
                    changeTime: null
                };
            }

            var low = 0;
            var high = pts.length - 1;
            var foundIndex = -1;

            while (low <= high) {
                var middle = (low + high) >> 1;

                var pointTime =
                    Number(
                        pts[middle] &&
                        pts[middle][0]
                    );

                if (
                    !isNaN(pointTime) &&
                    pointTime <= cursorMs
                ) {
                    foundIndex = middle;
                    low = middle + 1;
                }
                else {
                    high = middle - 1;
                }
            }

            if (foundIndex < 0) {
                /*
                 * Cursor sits left of this lane's first sample. step:'end'
                 * draws the line from pts[0], so report that opening value to
                 * match the drawn line, but leave the time null rather than
                 * pinning the header to a timestamp in the future.
                 */
                var firstVal =
                    Number(
                        pts[0] &&
                        pts[0][1]
                    );

                return {
                    value: isNaN(firstVal) ? null : firstVal,
                    changeTime: null
                };
            }

            var currentValue =
                Number(
                    pts[foundIndex][1]
                );

            if (isNaN(currentValue)) {
                return {
                    value: null,
                    changeTime: null
                };
            }

            var changeIndex = foundIndex;

            while (changeIndex > 0) {
                var previousValue =
                    Number(
                        pts[changeIndex - 1][1]
                    );

                if (
                    isNaN(previousValue) ||
                    previousValue !== currentValue
                ) {
                    break;
                }

                changeIndex--;
            }

            var changeTime =
                Number(
                    pts[changeIndex][0]
                );

            return {
                value: currentValue,

                changeTime:
                    isNaN(changeTime)
                        ? null
                        : changeTime
            };
        }

        function formatStackedLaneDateTime(timestamp) {
            var date = new Date(Number(timestamp));

            if (isNaN(date.getTime())) {
                return '';
            }

            var pad = function (n) {
                return String(n).padStart(2, '0');
            };

            return (
                pad(date.getDate()) + '-' +
                pad(date.getMonth() + 1) + '-' +
                date.getFullYear() + ' ' +
                pad(date.getHours()) + ':' +
                pad(date.getMinutes()) + ':' +
                pad(date.getSeconds())
            );
        }

        function escapeStackedTooltipText(value) {
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

        function getStackedAxisLabelFormatter(
            isBinarySeries
        ) {
            if (!isBinarySeries) {
                return function (value) {
                    return value;
                };
            }

            return function (value) {
                if (Number(value) === 1) {
                    return '{pickup|Pickup}';
                }

                if (Number(value) === 0) {
                    return '{drop|Drop}';
                }

                return '';
            };
        }

        for (
            var laneIndex = 0;
            laneIndex < sourceSeries.length;
            laneIndex++
        ) {
            var originalSeries =
                sourceSeries[laneIndex];

            var laneTop =
                topPadding +
                laneIndex *
                (
                    laneHeight +
                    laneGap
                );

                        var laneColor =
                originalSeries.lineStyle &&
                    originalSeries.lineStyle.color
                    ? originalSeries.lineStyle.color
                    : '#475569';

            var laneIsBinary =
                !!originalSeries.isBinarySeries;

            var laneLabel =
                String(
                    originalSeries.attributeName ||
                    originalSeries.name ||
                    ''
                )
                    .replace(/^\[R\]\s*/, '')
                    .replace(/^\[DL\]\s*/, '')
                    .replace(/^\[D\]\s*/, '')
                    .trim();

            grids.push({
                left: 185,
                right: 34,
                top: laneTop,
                height: laneHeight,
                containLabel: false
            });

            laneGraphics.push({
                type: 'group',
                left: 10,
                top:
                    laneTop +
                    Math.max(
                        4,
                        Math.floor(laneHeight / 2) - 11
                    ),
                silent: true,

                children: [
                    {
                        type: 'circle',

                        shape: {
                            cx: 5,
                            cy: 10,
                            r: 4
                        },

                        style: {
                            fill: laneColor
                        }
                    },
                    {
                        type: 'text',
                        left: 15,
                        top: 0,

                        style: {
                            text: laneLabel,
                            fill: laneColor,
                            font: '600 10px sans-serif',
                            width: 150,
                            overflow: 'truncate',
                            ellipsis: '…',
                            lineHeight: 20
                        }
                    }
                ]
            });

            xAxes.push({
                type: 'time',
                gridIndex: laneIndex,
                min:
                    startMs !== undefined &&
                        startMs !== null
                        ? startMs
                        : null,

                max:
                    endMs !== undefined &&
                        endMs !== null
                        ? endMs
                        : null,

                boundaryGap: false,

                axisPointer: {
                    show: true,
                    snap: false,
                    label: {
                        show: laneIndex === laneCount - 1
                    }
                },

                axisLine: {
                    show: true,
                    lineStyle: {
                        color: '#cbd5e1'
                    }
                },

                axisTick: {
                    show:
                        laneIndex ===
                        laneCount - 1
                },

                axisLabel: {
                    show:
                        laneIndex ===
                        laneCount - 1,

                    color: '#64748b',

                    formatter: function (value) {
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

                splitLine: {
                    show: true,
                    lineStyle: {
                        color: '#f1f5f9',
                        type: 'dashed'
                    }
                }
            });

            yAxes.push({
                type: 'value',
                gridIndex: laneIndex,
                scale: true,

                /*
 * IPS lane name is shown as a horizontal graphic on the left.
 * Do not draw the long rotated Y-axis name.
 */
                name: '',

                 axisLabel: {
                    color: '#64748b',
                    fontSize: 9,

                    formatter:
                        getStackedAxisLabelFormatter(
                            laneIsBinary
                        ),

                    rich: {
                        pickup: {
                            color: '#10b981',
                            fontWeight: 700
                        },

                        drop: {
                            color: '#f59e0b',
                            fontWeight: 700
                        }
                    }
                },

                axisLine: {
                    show: true,
                    lineStyle: {
                        color: '#cbd5e1'
                    }
                },

                splitLine: {
                    show: true,
                    lineStyle: {
                        color: '#f1f5f9',
                        type: 'dashed'
                    }
                }
            });

            /*
             * Clone only the top-level series object.
             * The original data array is reused without modifying it.
             */
            var laneSeriesId =
                originalSeries.id ||
                (
                    'history-ips-lane-' +
                    laneIndex
                );

            stackedAttributeNameMap[
                String(laneSeriesId)
            ] =
                String(
                    originalSeries.attributeName ||
                    originalSeries.name ||
                    ''
                ).trim();

            stackedBinarySeriesMap[
                String(laneSeriesId)
            ] =
                laneIsBinary;

            /*
             * Keyed by series.name because that is the seriesDataMap key the
             * caller builds (it stays unique across assets in multi-asset mode,
             * while attributeName does not).
             */
            stackedLaneChangeDataMap[
                String(laneSeriesId)
            ] =
                (
                    actualPointsByName &&
                    actualPointsByName[originalSeries.name]
                ) || [];

            var laneSeries =
                $.extend(
                    {},
                    originalSeries,
                    {
                        xAxisIndex: laneIndex,
                        yAxisIndex: laneIndex,

                        /*
                         * Stable ID ensures equal attribute names from different
                         * assets remain separate series.
                         */
                        id:
                            laneSeriesId,

                        emphasis: {
                            disabled: false,
                            focus: 'series'
                        },

                        data:
                            originalSeries.data || []
                    }
                );

            stackedSeries.push(
                laneSeries
            );
        }

        var allXAxisIndexes = [];

        for (
            var axisIndex = 0;
            axisIndex < laneCount;
            axisIndex++
        ) {
            allXAxisIndexes.push(
                axisIndex
            );
        }

        chartInstance.setOption(
            {
                backgroundColor: '#ffffff',
                animation: false,

                /*
                 * Do not add:
                 * axisPointer: { link: [{ xAxisIndex: 'all' }] }
                 *
                 * Every lane must hover independently.
                 */
                                tooltip: {
                    trigger: 'axis',
                    confine: true,

                    axisPointer: {
                        type: 'cross',
                        snap: false,

                        /*
                         * Cross-pointer draws a label on BOTH axes. Format any
                         * millisecond-timestamp value as DD-MM-YYYY HH:MM:SS so
                         * the Y-axis crosshair label (which otherwise inherits an
                         * HH:MM:SS-only default) also carries the date.
                         */
                        label: {
                            formatter: function (params) {
                                var raw = params && params.value;
                                var num = Number(raw);

                                if (isFinite(num) && num > 1e12) {
                                    var d = new Date(num);
                                    var pad = function (n) {
                                        return String(n).padStart(2, '0');
                                    };
                                    return pad(d.getDate()) + '-' +
                                        pad(d.getMonth() + 1) + '-' +
                                        d.getFullYear() + ' ' +
                                        pad(d.getHours()) + ':' +
                                        pad(d.getMinutes()) + ':' +
                                        pad(d.getSeconds());
                                }

                                return raw === undefined || raw === null ? '' : String(raw);
                            }
                        }
                    },

                    formatter: function (params) {
                        if (
                            !params ||
                            !params.length
                        ) {
                            return '';
                        }

                        /*
                         * Lanes are drawn on separate grids with unlinked X-axis
                         * pointers, so ECharts normally supplies only the hovered
                         * lane. Each entry is still rendered as a self-contained
                         * block with its OWN changed date & time -- no shared
                         * header, nothing maxed across lanes -- so if a build ever
                         * hands back more than one lane the timestamps stay
                         * independent instead of collapsing into one.
                         */
                        var blocks = [];

                        /*
                         * One block per LANE, never per data point.
                         *
                         * With trigger:'axis' ECharts can hand back several
                         * entries for the same series -- a lane whose step line
                         * carries both a real sample and an edge anchor near the
                         * cursor returns two params with the same seriesId, which
                         * rendered the attribute twice in the popup. The lookup
                         * below is a function of the lane and the cursor only, so
                         * the second entry could only ever repeat the first.
                         */
                        var seenSeriesIds = {};

                        for (
                            var paramIndex = 0;
                            paramIndex < params.length;
                            paramIndex++
                        ) {
                            var point =
                                params[paramIndex];

                            if (!point) continue;

                            var seriesId =
                                String(
                                    point.seriesId || ''
                                );

                            /*
                             * Every lane here is given an explicit id, so the
                             * fallbacks only stop a build that omits seriesId
                             * from collapsing genuinely different lanes into one.
                             */
                            var dedupeKey =
                                seriesId ||
                                String(point.seriesName || '') ||
                                ('idx:' + paramIndex);

                            if (seenSeriesIds[dedupeKey]) {
                                continue;
                            }

                            seenSeriesIds[dedupeKey] = true;

                            var pointValue =
                                point.value || [];

                            /*
                             * axisValue is this lane's own unsnapped pointer
                             * position (snap:false). Fall back to the plotted
                             * point's timestamp only when it is unavailable.
                             */
                            var cursorMs =
                                point.axisValue !== undefined &&
                                    point.axisValue !== null &&
                                    !isNaN(Number(point.axisValue))
                                    ? Number(point.axisValue)
                                    : Number(pointValue[0]);

                            if (!isFinite(cursorMs)) {
                                continue;
                            }

                            var lookup =
                                getStackedLaneValueAtTime(
                                    seriesId,
                                    cursorMs
                                );

                            var isBinaryLane =
                                !!stackedBinarySeriesMap[
                                seriesId
                                ];

                            /*
                             * Prefer the change-sample lookup. Fall back to the
                             * plotted value so a lane with no change samples in
                             * the window still reads as it is drawn.
                             */
                            var value =
                                lookup.value !== null &&
                                    lookup.value !== undefined
                                    ? lookup.value
                                    : pointValue[1];

                            /*
                             * Value rendering is unchanged from before: binary
                             * lanes read Pickup/Drop, analog lanes print the
                             * reading as-is.
                             */
                            var displayValue =
                                value !== undefined &&
                                    value !== null
                                    ? (
                                        isBinaryLane
                                            ? (
                                                Number(value) === 1
                                                    ? 'Pickup'
                                                    : 'Drop'
                                            )
                                            : value
                                    )
                                    : '—';

                            /*
                             * The changed time -- when this lane's current value
                             * actually began. Until the next change is reached the
                             * header stays pinned there even though the crosshair
                             * keeps moving, which is what the table column shows.
                             * Before this lane's first sample there is no change to
                             * report, so fall back to the cursor time.
                             */
                            var changeMs =
                                lookup.changeTime !== null &&
                                    lookup.changeTime !== undefined &&
                                    !isNaN(Number(lookup.changeTime))
                                    ? Number(lookup.changeTime)
                                    : cursorMs;

                            var dateText =
                                formatStackedLaneDateTime(
                                    changeMs
                                );

                            if (!dateText) {
                                continue;
                            }

                            var laneName =
                                stackedAttributeNameMap[
                                seriesId
                                ] ||
                                point.seriesName ||
                                '';

                            blocks.push(
                                '<div style="' +
                                (
                                    blocks.length
                                        ? (
                                            'margin-top:7px;' +
                                            'padding-top:7px;' +
                                            'border-top:1px solid #e2e8f0;'
                                        )
                                        : ''
                                ) +
                                '">' +

                                '<div style="' +
                                'font-size:11px;' +
                                'font-weight:600;' +
                                'color:#0f5f65;' +
                                'margin-bottom:6px;' +
                                'white-space:nowrap;">' +

                                '<i class="fas fa-clock-o" style="' +
                                'font-size:9px;' +
                                'margin-right:4px;"></i>' +

                                escapeStackedTooltipText(dateText) +

                                '</div>' +

                                '<div style="' +
                                'display:flex;' +
                                'align-items:center;' +
                                'gap:7px;">' +

                                '<span style="' +
                                'width:8px;' +
                                'height:8px;' +
                                'border-radius:50%;' +
                                'background:' +
                                escapeStackedTooltipText(point.color) +
                                ';display:inline-block;"></span>' +

                                '<span>' +
                                escapeStackedTooltipText(laneName) +
                                '</span>' +

                                '<strong style="margin-left:8px;">' +
                                escapeStackedTooltipText(displayValue) +
                                '</strong>' +

                                '</div>' +

                                '</div>'
                            );
                        }

                        if (!blocks.length) {
                            return '';
                        }

                        return blocks.join('');
                    }
                },

                legend: {
                    show: false
                },

                grid: grids,
                xAxis: xAxes,
                yAxis: yAxes,
                series: stackedSeries,
                graphic: laneGraphics,

                dataZoom: [
                    {
                        type: 'inside',
                        xAxisIndex:
                            allXAxisIndexes,

                        filterMode: 'none',
                        throttle: 60
                    },
                    {
                        type: 'slider',
                        xAxisIndex:
                            allXAxisIndexes,

                        filterMode: 'none',
                        left: 185,
                        right: 34,
                        bottom: 22,
                        height: 24
                    }
                ]
            },
            {
                notMerge: true,
                lazyUpdate: false
            }
        );

        return true;
    }



        function parseHistoryGraphNumericValue(
        rawValue,
        isDataLogger
    ) {
        if (
            rawValue === null ||
            rawValue === undefined ||
            rawValue === ''
        ) {
            return null;
        }

        var numericValue =
            Number(rawValue);

        if (isFinite(numericValue)) {
            return numericValue;
        }

        if (isDataLogger) {
            var state =
                String(rawValue)
                    .trim()
                    .toLowerCase();

            if (
                state === 'pickup' ||
                state === 'up' ||
                state === 'true' ||
                state === 'on'
            ) {
                return 1;
            }

            if (
                state === 'drop' ||
                state === 'down' ||
                state === 'false' ||
                state === 'off'
            ) {
                return 0;
            }
        }

        return null;
    }


    function _hgDrawECharts(parsed, colorMap, multiAsset) {
        if (historyEChart) {
            historyEChart.dispose();
            historyEChart = null;
        }

        var el =
            document.getElementById(
                'hgChartDiv'
            );

        if (!el) return;

        /*
         * IPS-only graph behaviour.
         *
         * Keep this decision local to the History graph renderer so no other
         * asset-type graph or existing History functionality is changed.
         */
        var selectedHistoryAssetTypeName =
            $.trim(
                String(
                    $('#drpAssetType option:selected')
                        .text() || ''
                )
            ).toUpperCase();

        var isIpsHistoryGraph =
            selectedHistoryAssetTypeName === 'IPS' ||
            selectedHistoryAssetTypeName.indexOf('IPS ') === 0 ||
            selectedHistoryAssetTypeName.indexOf(' IPS') > -1;

        // Pull the selected datetime range from the filter inputs so the
        // chart window matches the table window. (Same purpose as _gReqStart/_gReqEnd in the live graph.)
        var selectedRange = getSelectedRange();
        var _reqStart = selectedRange ? selectedRange.startMs : null;
        var _reqEnd = selectedRange ? selectedRange.endMs : null;

        // The "Last Updated" anchor is a GLOBAL pre-start timestamp: on IPS it comes from
        // change-of-state attributes (MAINS_FAIL, SMPS_FAIL, 50_DOD ...) whose last change can be
        // far before the selected start. Dragging the axis back to it squeezes continuously-logged
        // analog attributes (SMR-1..5) into the right-hand edge. Keep the axis on the selected
        // window (same as the Point Machine graph) and let the anchor point be clamped instead.
        var _lastUpdatedMs = (parsed && parsed.lastUpdatedMs) ? parsed.lastUpdatedMs : null;
        var _anchorClampMs = (_lastUpdatedMs !== null && _reqStart !== null && _lastUpdatedMs < _reqStart)
            ? _reqStart
            : null;

        // Map the History page's chartActiveFilter ('All'|'mA'|'V'|'0/1'|'Derived')
        // to the same internal vocabulary used by the live graph ('all'|'ma'|'v'|'bin'|'derived').
        var filter = 'all';
        if (chartActiveFilter === 'mA') filter = 'ma';
        else if (chartActiveFilter === 'V') filter = 'v';
        else if (chartActiveFilter === '0/1') filter = 'bin';
        else if (chartActiveFilter === 'Derived') filter = 'derived';

        // Sorted union of all timestamps in ms
        var sortedTs = parsed.timestamps.map(function (d) { return d.getTime(); });
        sortedTs.sort(function (a, b) { return a - b; });

        var sr = [], ci = 0;
        var maxMA = 0, maxV = 0, maxOhm = 0;
        var hasMA = false, hasV = false, hasBin = false, hasOhm = false;
        var binSeriesSet = {}, seriesDataMap = {};

        var iterCols = (_hgAllCols && _hgAllCols.length) ? _hgAllCols : parsed.columns;

        // Pre-compute derived values per (assetId, ts) when derived cols exist
        var derivedCache = {};
        if (_hgDerivedCols && _hgDerivedCols.length) {
            var assetsSeen = {};
            _hgDerivedCols.forEach(function (dc) { assetsSeen[dc.assetId] = true; });
            Object.keys(assetsSeen).forEach(function (aid) {
                parsed.timestamps.forEach(function (ts) {
                    derivedCache[aid + '|' + ts.getTime()] =
                        calculateDerivedForRow(parsed.attributeData, parsed.columns, aid, ts);
                });
            });
        }

        iterCols.forEach(function (col) {
            if (chartHiddenSeries[col.compositeKey]) return;

            var entries;
            if (col.isDerived) {
                entries = [];
                parsed.timestamps.forEach(function (ts) {
                    var d = derivedCache[col.assetId + '|' + ts.getTime()];
                    if (d && d[col.attrId] !== undefined && !isNaN(d[col.attrId])) {
                        entries.push({ ts: ts, value: d[col.attrId] });
                    }
                });
            } else {
                entries = parsed.attributeData[col.compositeKey] || [];
            }
            if (!entries.length) return;

                        // Build valueMap {ms -> float}
            var valueMap = {}, allVals = [];

            entries.forEach(function (e) {
                var ms = e.ts.getTime();

                var v =
                    parseHistoryGraphNumericValue(
                        e.value,
                        !!col.isDataLogger
                    );

                if (v !== null) {
                    valueMap[ms] = v;
                    allVals.push(v);
                }
            });
            if (!Object.keys(valueMap).length) return;

            // Binary detection: DataLogger AND all values are 0 or 1
            var allBinary = allVals.length > 0 && allVals.every(function (v) { return v === 0 || v === 1; });
            var isBin = col.isDataLogger && allBinary;
            var isDerived = !!col.isDerived;

            // Unit classification — drives axis routing AND filter chips
            var unit = getAttrUnit(col);
            var isMA = (unit === 'mA');
            var isOhm = (unit === 'Ω');
            // V = everything analog that isn't mA or Ω (incl. unrecognised derived)
            var isV = !isBin && !isMA && !isOhm;

            // Apply type filter (chip bar at the top of the chart)
            if (filter === 'ma' && !isMA) return;
            if (filter === 'v' && !isV) return;
            if (filter === 'bin' && !isBin) return;
            if (filter === 'derived' && !isDerived) return;
            // Note: 'all' lets everything through

            var co = colorMap[col.compositeKey] || _hgGC[ci % _hgGC.length];

            // === Carry-forward time-based points, anchored to the visible window ===
            // Mirrors the live graph: keep [ts, val] pairs, clamp negatives to 0 for analog display,
            // anchor left edge to _reqStart (seeded with last known pre-window value if any),
            // anchor right edge to _reqEnd so the line spans the full window.
            var msKeys = Object.keys(valueMap).map(Number).sort(function (a, b) { return a - b; });
            var lastVal = null;
            var points = [];
            var actualPts = [];

            // Seed left edge with last known pre-window value (if any) so the line starts at _reqStart
            if (_reqStart !== null) {
                for (var pk = 0; pk < msKeys.length; pk++) {
                    if (msKeys[pk] <= _reqStart) lastVal = valueMap[msKeys[pk]];
                    else break;
                }
                if (lastVal !== null) {
                    var seedPlot =
                        (!isBin && lastVal < 0)
                            ? 0
                            : lastVal;

                    points.push({
                        value: [_reqStart, seedPlot],
                        symbol: 'none',
                        symbolSize: 0
                    });

                    /*
                     * The IPS tooltip reads only seriesDataMap/actualPts.
                     *
                     * Add the carried-forward starting value there also, otherwise hovering
                     * before the first in-range IPS sample will incorrectly show no value.
                     *
                     * This is IPS-only. Existing History graph semantics remain unchanged for
                     * every other asset type.
                     */
                    //if (isIpsHistoryGraph) {
                    //    actualPts.push([
                    //        _reqStart,
                    //        lastVal
                    //    ]);
                    //}
                    if (isIpsHistoryGraph) {
                        actualPts.push([
                            _reqStart,
                            (lastVal <= 0 ? 0 : lastVal)
                        ]);
                    }
                }
            }

            // Walk the union; carry-forward. EXCEPTION -- summed Batt Charging /
            // Discharging (col.isGroup): walk only THEIR OWN change timestamps
            // (msKeys), so neither carries a point onto the other's instant.
            (col.isGroup ? msKeys : sortedTs).forEach(function (ts) {
                if (_reqStart !== null && ts < _reqStart) {
                    if (valueMap[ts] !== undefined) lastVal = valueMap[ts];
                    return;
                }
                if (_reqEnd !== null && ts > _reqEnd) return;
                //if (valueMap[ts] !== undefined) {
                //    lastVal = valueMap[ts];
                //    actualPts.push([ts, lastVal]);
                //}
                if (valueMap[ts] !== undefined) {
                    lastVal = valueMap[ts];
                    // Graph tooltip mirrors the table's <= 0 -> 0 clamp for IPS.
                    actualPts.push([ts, (isIpsHistoryGraph && lastVal <= 0) ? 0 : lastVal]);
                }
                if (lastVal === null) return;
                var plotVal = (!isBin && lastVal < 0) ? 0 : lastVal;
                points.push({
                    value: [ts, plotVal],
                    symbol: (valueMap[ts] !== undefined && !isBin) ? 'circle' : 'none',
                    symbolSize: (valueMap[ts] !== undefined && !isBin) ? 3 : 0
                });
            });

            // Anchor right edge so the line runs to the end of the window
            if (lastVal !== null && _reqEnd !== null) {
                var lastTs = points.length ? points[points.length - 1].value[0] : -1;
                if (lastTs < _reqEnd) {
                    var endPlot = (!isBin && lastVal < 0) ? 0 : lastVal;
                    points.push({ value: [_reqEnd, endPlot], symbol: 'none', symbolSize: 0 });
                }
            }

            if (!actualPts.length && !points.length) return;

            // Use ALL plotted values (carry-forward window included), not just actualPts.
            // Otherwise a "Last Updated" anchor series whose only in-window value is the
            // pre-window seed reports serMax=0 and the axis under-scales.
            var serMax = 0;
            for (var _pi = 0; _pi < points.length; _pi++) {
                var _pv = (points[_pi] && points[_pi].value) ? points[_pi].value[1] : null;
                if (_pv !== null && _pv !== undefined && !isNaN(_pv) && _pv > serMax) serMax = _pv;
            }
            if (!isBin) {
                if (isMA && serMax > maxMA) maxMA = serMax;
                if (isOhm && serMax > maxOhm) maxOhm = serMax;   // ← new bucket
                if (isV && serMax > maxV) maxV = serMax;
            }
            if (isBin) hasBin = true;
            else if (isMA) hasMA = true;
            else if (isOhm) hasOhm = true;
            else hasV = true;

            // Prefix group (A End / B End) for PM columns so names are unique
            // (otherwise A End "IPT N/R (A)" and B End "IPT N/R (A)" collide in seriesDataMap)
            var _grpPrefix = '';
            if (col.group === 'A') _grpPrefix = 'A End - ';
            else if (col.group === 'B') _grpPrefix = 'B End - ';

            var _baseName = _grpPrefix + col.displayName;
            var sName = (multiAsset && !col.isGroup) ? (_baseName + ' [' + col.assetName + ']') : _baseName;
            if (isBin) { binSeriesSet[sName] = true; sName = '[R] ' + sName; }
            if (isDerived) { sName = '[D] ' + sName; }
            seriesDataMap[sName] = actualPts;
            ci++;

            // yAxisIndex: matches live graph's pattern
            //   bin -> 2 ; when filter is 'all' force analog (mA+V) onto axis 0 so they share one scale ;
            //   otherwise mA -> 0, V -> 1.
            // Axis routing:
            //   binary → 2 ; filter 'all' → everything analog onto 0 (shared scale)
            //   else mA → 0 ; V → 1 ; Ω → 0 (no dedicated Ω axis, rides with mA)
            var yIdx;
            if (isBin) yIdx = 2;
            else if (filter === 'all') yIdx = 0;
            else if (isMA) yIdx = 0;
            else if (isOhm) yIdx = 0;
            else yIdx = 1;
            sr.push({
                name: sName,

                /*
                 * Clean attribute-only name used by IPS tooltip.
                 *
                 * Keep series.name unchanged because it is also used as the unique
                 * multi-asset lookup key in seriesDataMap.
                 */
                attributeName:
                    String(
                        col.displayName || sName
                    ).trim(),

                isDataLogger:
                    !!col.isDataLogger,

                isBinarySeries:
                    isBin,

                type: 'line',
                sampling: 'none',
                hoverAnimation: false,
                clip: true,
                connectNulls: true,
                smooth: false,
                step: 'end',
                showSymbol: true,
                symbolSize: isBin ? 0 : 4,
                yAxisIndex: yIdx,
                lineStyle: { width: isBin ? 2 : 1.8, color: co, type: 'solid' },
                itemStyle: { color: co },
                emphasis: { focus: 'none' },
                data: points
            });
        });

        if (!sr.length) {
            $('#hgChartDiv').html('<div style="display:flex;align-items:center;justify-content:center;height:100%;color:#94a3b8;font-size:13px;">' +
                '<i class="fas fa-line-chart" style="margin-right:8px;opacity:.3;font-size:20px;"></i>All series hidden</div>');
            return;
        }

        historyEChart = echarts.init(el, null, { renderer: 'canvas' });

        // ===== Y-axes — dynamic max that scales with data magnitude =====
        //
        // Previously the axis was snapped to fixed multiples (100 for mA, 50 for V).
        // That works for ~500-range telemetry but breaks for higher-range attributes:
        // a sensor that swings up to 2000 mA still got an axis snapped to a 100-step
        // grid, and zooming in / out never re-evaluated. Now the snap step itself
        // scales with the data's order of magnitude, so:
        //
        //   max 8     → axis 10        (step 1)
        //   max 87    → axis 90        (step 10)
        //   max 540   → axis 600       (step 50)
        //   max 1180  → axis 1200      (step 100)
        //   max 2150  → axis 2200      (step 200)
        //   max 18400 → axis 20000     (step 1000)
        //
        // Plus 10% headroom so the line never touches the top of the panel.
        function _niceAxisMax(rawMax) {
            if (!rawMax || rawMax <= 0) return 'dataMax';
            var padded = rawMax * 1.10;                            // 10% headroom
            var mag = Math.pow(10, Math.floor(Math.log10(padded))); // order of magnitude
            var norm = padded / mag;                                // 1.0 — 9.99
            var step;
            if (norm <= 1) step = 0.2 * mag;
            else if (norm <= 2) step = 0.5 * mag;
            else if (norm <= 5) step = 1 * mag;
            else step = 2 * mag;
            return Math.ceil(padded / step) * step;
        }

        // When filter='all', axis 0 holds mA + V + Ω together; pick the largest.
        // Otherwise axis 0 is mA-only, axis 1 is V-only, Ω rides axis 0 either way.
        var axis0Raw = (filter === 'all')
            ? Math.max(maxMA, maxV, maxOhm)
            : Math.max(maxMA, maxOhm);
        var mAmax = _niceAxisMax(axis0Raw);
        var Vmax = _niceAxisMax(maxV);
        var hasAnalog = hasMA || hasV || hasOhm;

        /*
         * IPS and ELD display DataLogger states as Pickup/Drop.
         * Other asset types retain their existing 0/1 axis.
         */
        var useNamedDlStatusAxis =
            typeof supportsHistoryStackedGraph === 'function' &&
            supportsHistoryStackedGraph();

        var yAxes = [
            {
                // LEFT — mA  (primary analog axis)
                type: 'value', min: 0, max: mAmax,
                name: '', nameLocation: 'end',
                nameTextStyle: { fontSize: 11, fontWeight: 'bold', color: '#4bacc6', padding: [0, 0, 4, 0] },
                axisLabel: { fontSize: 10, color: '#475569', formatter: '{value}' },
                axisLine: { show: hasMA || (!hasV && !hasBin), lineStyle: { color: '#94a3b8', width: 1.5 } },
                axisTick: { show: hasMA || (!hasV && !hasBin) },
                splitLine: { show: true, lineStyle: { color: '#f1f5f9', type: 'dashed', width: 1 } },
                show: hasMA || (!hasV && !hasBin)
            },
            {
                // LEFT (offset) — V  (only shown when user explicitly filters to V)
                type: 'value', min: 0, max: Vmax,
                name: 'V', nameLocation: 'end',
                nameTextStyle: { fontSize: 11, fontWeight: 'bold', color: '#cc6600', padding: [0, 0, 4, 0] },
                axisLabel: { fontSize: 10, color: '#cc6600', formatter: '{value}' },
                axisLine: { show: hasV, lineStyle: { color: '#cc6600', width: 1.5 } },
                axisTick: { show: hasV },
                splitLine: { show: !hasMA && hasV, lineStyle: { color: 'rgba(204,102,0,.10)', type: 'dashed', width: 1 } },
                position: 'left', offset: hasMA ? 55 : 0,
                show: hasV
            },
                        {
                // LEFT (offset) — binary DataLogger status
                type: 'value',
                min: 0,
                max: 1.2,

                position: 'left',

                            offset:
                                (hasMA ? 55 : 0) +
                                (hasV ? 55 : 0),

                name:
                    useNamedDlStatusAxis
                        ? 'DL Status'
                        : '0/1',

                nameLocation: 'end',

                nameTextStyle: {
                    fontSize: 11,
                    fontWeight: 'bold',
                    color: '#7c3aed',
                    padding: [0, 0, 0, 40]
                },

                axisLabel: {
                    show: hasBin,
                    fontSize: 10,
                    color: '#7c3aed',
                    interval: 0,

                    formatter: function (value) {
                        /*
                         * Preserve existing 0/1 labels for all other
                         * asset types.
                         */
                        if (!useNamedDlStatusAxis) {
                            return Number(value) === 0
                                ? '0'
                                : Number(value) === 1
                                    ? '1'
                                    : '';
                        }

                        return Number(value) === 1
                            ? '{pickup|Pickup}'
                            : Number(value) === 0
                                ? '{drop|Drop}'
                                : '';
                    },

                    rich: {
                        pickup: {
                            color: '#10b981',
                            fontWeight: 700
                        },

                        drop: {
                            color: '#f59e0b',
                            fontWeight: 700
                        }
                    }
                },

                axisLine: {
                    show: hasBin,
                    lineStyle: {
                        color: '#7c3aed',
                        width: 1.5
                    }
                },

                axisTick: {
                    show: hasBin
                },

                splitLine: {
                    show: false
                },

                minInterval: 1,
                show: hasBin
            }
        ];
        /*
 * Actual unsnapped IPS crosshair timestamp.
 * Local to this History chart render.
 */
        var historyIpsActualHoverMs =
            null;

        /*
         * IPS History only:
         * render each selected attribute in an independent stacked lane.
         *
         * Non-IPS charts continue through the existing renderer below.
         */
                var useIpsStackedMode =
            typeof supportsHistoryStackedGraph === 'function' &&
            supportsHistoryStackedGraph() &&
            historyIpsGraphMode === 'stacked';

        if (useIpsStackedMode) {
            applyIpsHistoryStackedMode(
                historyEChart,
                sr,
                _reqStart,
                _reqEnd,
                seriesDataMap
            );

            return;
        }

        /*
         * Restore the normal fixed height after switching from Stacked to Overlay.
         */
        $('#hgChartDiv').css(
            'height',
            '500px'
        );

        historyEChart.resize();


        historyEChart.setOption({
            backgroundColor: '#ffffff',
            animation: false,
                       tooltip: {
                trigger: 'axis',
                axisPointer: {
                    type: 'cross',

                    /*
                     * Do not intentionally snap the pointer to a historian sample.
                     * IPS also captures the real pointer timestamp below because some
                     * ECharts versions can still return a nearest-series axisValue.
                     */
                    snap: false,

                    /*
                     * Cross-pointer draws a label on BOTH axes. The X-axis label
                     * already renders as DD-MM-YYYY HH:MM:SS via xAxis.axisPointer,
                     * but the tooltip's cross also renders a Y-axis crosshair label
                     * whose default (for a numeric value that happens to be a
                     * millisecond timestamp) prints only HH:MM:SS.
                     *
                     * Intercept every crosshair label the tooltip renders and, if
                     * the value looks like a real millisecond timestamp (after the
                     * year 2001), format it as DD-MM-YYYY HH:MM:SS. Everything else
                     * (real Y-values, small numbers) is passed through untouched.
                     */
                    label: {
                        formatter: function (params) {
                            var raw = params && params.value;
                            var num = Number(raw);

                            if (isFinite(num) && num > 1e12) {
                                var d = new Date(num);
                                var pad = function (n) {
                                    return String(n).padStart(2, '0');
                                };
                                return pad(d.getDate()) + '-' +
                                    pad(d.getMonth() + 1) + '-' +
                                    d.getFullYear() + ' ' +
                                    pad(d.getHours()) + ':' +
                                    pad(d.getMinutes()) + ':' +
                                    pad(d.getSeconds());
                            }

                            return raw === undefined || raw === null ? '' : String(raw);
                        }
                    },

                    //label: {
                    //    backgroundColor: '#6a7985',

                    //    /*
                    //     * Crosshair time label must show the LAST CHANGED time
                    //     * (step-hold), not the continuously moving cursor time.
                    //     * It stays pinned at the most recent actual change across
                    //     * all series until the cursor crosses the next change.
                    //     */
                    //    formatter: function (_axp) {
                    //        // Y-axis label keeps the raw numeric value.
                    //        if (_axp.axisDimension !== 'x') {
                    //            return (_axp.value === undefined ||
                    //                _axp.value === null)
                    //                ? ''
                    //                : ('' + _axp.value);
                    //        }

                    //        var _cur = Number(_axp.value);
                    //        if (isNaN(_cur)) {
                    //            return '';
                    //        }

                    //        /*
                    //         * Find, across every plotted series, the latest actual
                    //         * change timestamp at or before the cursor. Repeated equal
                    //         * readings are walked back so the time reflects where the
                    //         * current value actually began.
                    //         */
                    //        var _pinned = null;

                    //        for (var _sn in seriesDataMap) {
                    //            var _pts = seriesDataMap[_sn];
                    //            if (!_pts || !_pts.length) {
                    //                continue;
                    //            }

                    //            var _lo = 0;
                    //            var _hi = _pts.length - 1;
                    //            var _fi = -1;

                    //            while (_lo <= _hi) {
                    //                var _mid = (_lo + _hi) >> 1;
                    //                var _pt = Number(_pts[_mid][0]);

                    //                if (!isNaN(_pt) && _pt <= _cur) {
                    //                    _fi = _mid;
                    //                    _lo = _mid + 1;
                    //                } else {
                    //                    _hi = _mid - 1;
                    //                }
                    //            }

                    //            if (_fi < 0) {
                    //                continue;
                    //            }

                    //            var _cv = Number(_pts[_fi][1]);
                    //            var _ci = _fi;

                    //            while (_ci > 0) {
                    //                var _pv = Number(_pts[_ci - 1][1]);
                    //                if (isNaN(_pv) || _pv !== _cv) {
                    //                    break;
                    //                }
                    //                _ci--;
                    //            }

                    //            var _ct = Number(_pts[_ci][0]);
                    //            if (!isNaN(_ct) &&
                    //                (_pinned === null || _ct > _pinned)) {
                    //                _pinned = _ct;
                    //            }
                    //        }

                    //        var _show = (_pinned !== null) ? _pinned : _cur;
                    //        var _d = new Date(_show);
                    //        if (isNaN(_d.getTime())) {
                    //            return '';
                    //        }

                    //        return (
                    //            String(_d.getDate()).padStart(2, '0') + '/' +
                    //            String(_d.getMonth() + 1).padStart(2, '0') + '/' +
                    //            _d.getFullYear() + ' ' +
                    //            String(_d.getHours()).padStart(2, '0') + ':' +
                    //            String(_d.getMinutes()).padStart(2, '0') + ':' +
                    //            String(_d.getSeconds()).padStart(2, '0')
                    //        );
                    //    }
                    //}

                    label: {
                        backgroundColor: '#6a7985',

                        /*
                         * Crosshair label behaviour (two axes):
                         *
                         * X-axis (horizontal crosshair line):
                         *   Shows the CONTINUOUSLY MOVING cursor timestamp so the
                         *   user can see exactly where the pointer is at all times.
                         *   The tooltip modal header is separately responsible for
                         *   showing the pinned last-change time.
                         *
                         * Y-axis (vertical crosshair line):
                         *   Shows the live cursor timestamp formatted as HH:MM:SS
                         *   so it updates continuously as the mouse moves along the
                         *   time axis.  The raw numeric axis value is omitted here
                         *   because the Y-axis tick labels already carry it.
                         */
                        formatter: function (_axp) {
                            var _cur = Number(_axp.value);
                            if (isNaN(_cur)) {
                                return (_axp.value === undefined || _axp.value === null)
                                    ? ''
                                    : ('' + _axp.value);
                            }

                            // ── Y-axis (vertical crosshair) ──────────────────
                            // Show a continuously-updating HH:MM:SS timestamp so
                            // the user can read the exact cursor time from the
                            // vertical line even while the tooltip modal is frozen.
                            if (_axp.axisDimension !== 'x') {
                                // _cur on a Y-axis is a numeric sensor value, not a
                                // timestamp.  We need the live X-axis position instead.
                                // ECharts passes the coordinated crosshair value via the
                                // shared axisPointer state; read it from the closure
                                // variable that updateAxisPointer already maintains.
                                var _liveMs = (typeof _hgLiveCursorMs !== 'undefined' &&
                                    _hgLiveCursorMs !== null &&
                                    !isNaN(Number(_hgLiveCursorMs)))
                                    ? Number(_hgLiveCursorMs)
                                    : null;

                                if (_liveMs === null) {
                                    // Fallback: show the raw numeric value
                                    return ('' + Number(_cur).toFixed(2));
                                }

                                var _yd = new Date(_liveMs);
                                if (isNaN(_yd.getTime())) {
                                    return ('' + Number(_cur).toFixed(2));
                                }

                                return (
                                    String(_yd.getHours()).padStart(2, '0') + ':' +
                                    String(_yd.getMinutes()).padStart(2, '0') + ':' +
                                    String(_yd.getSeconds()).padStart(2, '0')
                                );
                            }

                            // ── X-axis (horizontal crosshair) ────────────────
                            // Show the RAW continuously-moving cursor time.
                            // The tooltip modal header (formatter below) is the one
                            // that stays pinned at the last actual change timestamp.
                            var _xd = new Date(_cur);
                            if (isNaN(_xd.getTime())) {
                                return '';
                            }

                            return (
                                String(_xd.getDate()).padStart(2, '0') + '/' +
                                String(_xd.getMonth() + 1).padStart(2, '0') + '/' +
                                _xd.getFullYear() + ' ' +
                                String(_xd.getHours()).padStart(2, '0') + ':' +
                                String(_xd.getMinutes()).padStart(2, '0') + ':' +
                                String(_xd.getSeconds()).padStart(2, '0')
                            );
                        }
                    }
                },
                confine: false,
                backgroundColor: 'rgba(255,255,255,0.98)',
                borderColor: '#e2e8f0', borderWidth: 1, padding: [10, 14],
                extraCssText: 'box-shadow:0 6px 24px rgba(0,0,0,.18);border-radius:10px;',
                formatter: function (params) {
                    if (!params || !params.length) {
                        return '';
                    }

                    var first =
                        params[0] || {};

                    /*
                     * ECharts may provide axisValue/value[0] snapped to the nearest point.
                     *
                     * For IPS, always prefer the continuously moving, unsnapped X-axis
                     * position captured from updateAxisPointer.
                     *
                     * For all other asset types, retain the existing formatter timestamp.
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

                    //var hoverMs =
                    //    isIpsHistoryGraph &&
                    //        historyIpsActualHoverMs !== null &&
                    //        historyIpsActualHoverMs !== undefined &&
                    //        !isNaN(Number(historyIpsActualHoverMs))
                    //        ? Number(historyIpsActualHoverMs)
                    //        : formatterHoverMs;

                    /*
 * Prefer the continuously-tracked live cursor position for ALL
 * asset types so the tooltip modal and Y-axis crosshair label
 * always read from the same source and stay in sync.
 *
 * Previously only IPS used _hgLiveCursorMs / historyIpsActualHoverMs.
 * For all other asset types, formatterHoverMs (ECharts snapped point)
 * was used — but formatterHoverMs is the nearest plotted point, which
 * can jump ahead of the real cursor position, while _hgLiveCursorMs
 * is updated by updateAxisPointer every mouse-move and is always the
 * true unsnapped X-axis position.
 *
 * Priority order:
 *   1. _hgLiveCursorMs      — live unsnapped cursor (all types)
 *   2. historyIpsActualHoverMs — IPS-specific fallback (same value
 *      for IPS since updateAxisPointer writes both)
 *   3. formatterHoverMs      — ECharts snapped fallback
 */
                    var hoverMs =
                        (_hgLiveCursorMs !== null &&
                            _hgLiveCursorMs !== undefined &&
                            !isNaN(Number(_hgLiveCursorMs)))
                            ? Number(_hgLiveCursorMs)
                            : (
                                isIpsHistoryGraph &&
                                    historyIpsActualHoverMs !== null &&
                                    historyIpsActualHoverMs !== undefined &&
                                    !isNaN(Number(historyIpsActualHoverMs))
                                    ? Number(historyIpsActualHoverMs)
                                    : formatterHoverMs
                            );

                    if (isNaN(hoverMs)) {
                        return '';
                    }

                    /*
                     * Return:
                     *   value      = latest historian value at or before the cursor
                     *   changeTime = actual timestamp where that current value began
                     *
                     * No future reading can ever be selected.
                     */
                    function getHistoryValueAtTime(
                        seriesName,
                        cursorMs
                    ) {
                        var pts =
                            seriesDataMap[seriesName];

                        if (!pts || !pts.length) {
                            return {
                                value: null,
                                changeTime: null
                            };
                        }

                        var low = 0;
                        var high =
                            pts.length - 1;

                        var foundIndex = -1;

                        while (low <= high) {
                            var middle =
                                (low + high) >> 1;

                            var pointTime =
                                Number(
                                    pts[middle][0]
                                );

                            if (
                                !isNaN(pointTime) &&
                                pointTime <= cursorMs
                            ) {
                                foundIndex = middle;
                                low = middle + 1;
                            }
                            else {
                                high = middle - 1;
                            }
                        }

                        if (foundIndex < 0) {
                            /*
                             * Cursor is left of this series' first in-window sample.
                             * With step:'end' the line starts from pts[0] — report
                             * the opening value so the tooltip matches the drawn line
                             * instead of printing '—'.
                             * Only applies to IPS carry-forward series; a series with
                             * no points at all already returned above.
                             */
                            var firstVal = Number(pts[0][1]);
                            if (isNaN(firstVal)) {
                                return {
                                    value: null,
                                    changeTime: null
                                };
                            }
                            return {
                                value: firstVal,
                                changeTime: null   // cursor is BEFORE this sample: report the
                                // opening value so the tooltip matches the
                                // drawn line, but never let a future
                                // timestamp pin the header forward.
                            };
                        }

                        var currentValue =
                            Number(
                                pts[foundIndex][1]
                            );

                        if (isNaN(currentValue)) {
                            return {
                                value: null,
                                changeTime: null
                            };
                        }

                        var changeIndex =
                            foundIndex;

                        /*
                         * Repeated readings with the same numeric value are not new changes.
                         *
                         * Example:
                         * 03:20 = 24.50
                         * 03:21 = 24.50
                         * 03:22 = 24.50
                         *
                         * The last-changed time remains 03:20.
                         */
                        while (changeIndex > 0) {
                            var previousValue =
                                Number(
                                    pts[changeIndex - 1][1]
                                );

                            if (
                                isNaN(previousValue) ||
                                previousValue !== currentValue
                            ) {
                                break;
                            }

                            changeIndex--;
                        }

                        var changeTime =
                            Number(
                                pts[changeIndex][0]
                            );

                        return {
                            value: currentValue,

                            changeTime:
                                isNaN(changeTime)
                                    ? null
                                    : changeTime
                        };
                    }

                    function escapeHistoryTooltipText(value) {
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

                    var rowItems = [];

                    /*
                     * IPS header:
                     * show the latest applicable actual change time among all displayed
                     * attributes. It remains unchanged until a new value actually begins.
                     */
                    var latestApplicableChangeMs =
                        null;

                    for (
                        var seriesIndex = 0;
                        seriesIndex < sr.length;
                        seriesIndex++
                    ) {
                        var series =
                            sr[seriesIndex];

                        if (!series) continue;

                        var seriesName =
                            series.name;

                        var lookup =
                            getHistoryValueAtTime(
                                seriesName,
                                hoverMs
                            );

                        var value =
                            lookup.value;

                        //if (
                        //    isIpsHistoryGraph &&
                        //    lookup.changeTime !== null &&
                        //    lookup.changeTime !== undefined &&
                        //    !isNaN(Number(lookup.changeTime)) &&
                        //    (
                        //        latestApplicableChangeMs === null ||
                        //        Number(lookup.changeTime) >
                        //        latestApplicableChangeMs
                        //    )
                        //) {
                        //    latestApplicableChangeMs =
                        //        Number(lookup.changeTime);
                        //}

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

                        var lineColor =
                            series.lineStyle &&
                                series.lineStyle.color
                                ? series.lineStyle.color
                                : '#999';

                        var rawName;

                        /*
                         * IPS hover must show only the attribute name.
                         *
                         * Example:
                         * Existing series name:
                         *   VIPS DC AXLE C [VIPS DC AXLE C]
                         *
                         * Tooltip display:
                         *   VIPS DC AXLE C
                         *
                         * Non-IPS graph names remain unchanged.
                         */
                        if (
                            isIpsHistoryGraph &&
                            series.attributeName
                        ) {
                            rawName =
                                String(
                                    series.attributeName
                                ).trim();
                        }
                        else {
                            rawName =
                                String(seriesName || '')
                                    .replace(/^\[R\] /, '')
                                    .replace(/^\[DL\] /, '')
                                    .replace(/^\[D\] /, '');
                        }

                        var safeName =
                            escapeHistoryTooltipText(
                                rawName
                            );

                        var safeColor =
                            escapeHistoryTooltipText(
                                lineColor
                            );

                         /*
                         * Use the authoritative flag assigned while creating
                         * the series. Name matching remains only as a backward-
                         * compatible fallback.
                         */
                        var isBinarySeries =
                            !!series.isBinarySeries ||
                            !!binSeriesSet[seriesName] ||
                            !!binSeriesSet[rawName];

                        var isDerivedSeries =
                            String(seriesName || '')
                                .indexOf('[D] ') === 0;

                        var valueHtml;

                                               /*
                         * Keep IPS analog values numeric.
                         * Show IPS DataLogger values as Pickup/Drop.
                         */
                        if (
                            isIpsHistoryGraph &&
                            !isBinarySeries
                        ) {
                            valueHtml =
                                value === null
                                    ? (
                                        '<b style="' +
                                        'color:#94a3b8;' +
                                        'font-size:10px;' +
                                        'flex-shrink:0;' +
                                        'min-width:52px;' +
                                        'text-align:right;">' +
                                        '\u2014' +
                                        '</b>'
                                    )
                                    : (
                                        '<b style="' +
                                        'color:#0f172a;' +
                                        'font-size:10px;' +
                                        'flex-shrink:0;' +
                                        'min-width:52px;' +
                                        'text-align:right;' +
                                        'font-variant-numeric:tabular-nums;">' +
                                        Number(value).toFixed(2) +
                                        '</b>'
                                    );
                        }
                        else {
                            /*
                             * Existing non-IPS History tooltip behaviour is retained.
                             */
                            var displayValue =
                                value === null
                                    ? 0
                                    : Number(value);

                            if (isNaN(displayValue)) {
                                displayValue = 0;
                            }

                            valueHtml =
                                isBinarySeries
                                    ? (
                                        '<span style="' +
                                        'background:' +
                                        (
                                            displayValue === 1
                                                ? '#10b981'
                                                : '#f59e0b'
                                        ) +
                                        ';color:#fff;' +
                                        'padding:1px 6px;' +
                                        'border-radius:3px;' +
                                        'font-size:9px;' +
                                        'font-weight:700;' +
                                        'flex-shrink:0;">' +
                                        (
                                            displayValue === 1
                                                ? 'Pickup'
                                                : 'Drop'
                                        ) +
                                        '</span>'
                                    )
                                    : (
                                        '<b style="' +
                                        'color:' +
                                        (
                                            isDerivedSeries
                                                ? '#b45309'
                                                : '#1e293b'
                                        ) +
                                        ';font-size:10px;' +
                                        'flex-shrink:0;' +
                                        'min-width:36px;' +
                                        'text-align:right;">' +
                                        displayValue.toFixed(2) +
                                        '</b>'
                                    );
                        }

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
                            ';flex-shrink:0;">' +
                            '</span>' +

                            '<span style="' +
                            'font-size:10px;' +
                            'color:#475569;' +
                            'flex:1;' +
                            'min-width:0;' +
                            'overflow:hidden;' +
                            'text-overflow:ellipsis;' +
                            'white-space:nowrap;"' +
                            ' title="' +
                            safeName +
                            '">' +
                            safeName +
                            '</span>' +

                            valueHtml +

                            '</div>'
                        );
                    }

                    if (!rowItems.length) {
                        return '';
                    }

                    ///*
                    // * IPS shows the last actual change time until the next change is reached.
                    // *
                    // * Other asset types retain the normal cursor/formatter time.
                    // */
                    //var displayTimestamp =
                    //    isIpsHistoryGraph &&
                    //        latestApplicableChangeMs !== null &&
                    //        latestApplicableChangeMs !== undefined &&
                    //        !isNaN(Number(latestApplicableChangeMs))
                    //        ? Number(latestApplicableChangeMs)
                    //        : Number(hoverMs);

                    /*
 * All asset types show the last actual change time until
 * the next change is reached (step-hold behaviour).
 * The crosshair still moves continuously but the tooltip
 * header time stays pinned at the most recent change.
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

                    var date =
                        new Date(
                            displayTimestamp
                        );

                    if (isNaN(date.getTime())) {
                        return '';
                    }

                    var dateText =
                        String(
                            date.getDate()
                        ).padStart(2, '0') +
                        '-' +
                        String(
                            date.getMonth() + 1
                        ).padStart(2, '0') +
                        '-' +
                        date.getFullYear();

                    var timeText =
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
                        ).padStart(2, '0');

                    var lastUpdatedBadge =
                        !isIpsHistoryGraph &&
                            _lastUpdatedMs !== null &&
                            displayTimestamp === _lastUpdatedMs
                            ? (
                                '<span style="' +
                                'display:inline-block;' +
                                'background:#fef3c7;' +
                                'color:#92400e;' +
                                'border:1px solid #fcd34d;' +
                                'border-radius:3px;' +
                                'padding:1px 6px;' +
                                'font-size:9px;' +
                                'font-weight:700;' +
                                'letter-spacing:.3px;' +
                                'margin-left:8px;">' +

                                '<i class="fas fa-history" style="' +
                                'font-size:8px;' +
                                'margin-right:3px;">' +
                                '</i>' +

                                'Last Updated' +

                                '</span>'
                            )
                            : '';

                    var headerHtml =
                        '<div style="' +
                        'font-weight:700;' +
                        'font-size:11px;' +
                        'color:#0f5f65;' +
                        'margin-bottom:6px;' +
                        'padding-bottom:5px;' +
                        'border-bottom:1px solid #e2e8f0;' +
                        'white-space:nowrap;">' +

                        '<i class="fas fa-calendar" style="' +
                        'font-size:9px;' +
                        'margin-right:4px;">' +
                        '</i>' +

                        dateText +

                        '<i class="fas fa-clock-o" style="' +
                        'font-size:9px;' +
                        'margin-right:4px;' +
                        'margin-left:8px;">' +
                        '</i>' +

                        timeText +

                        lastUpdatedBadge +

                        '</div>';

                    /*
                     * IPS can have many attributes. Match Telemetry Live by showing them in
                     * two balanced columns with a bounded tooltip height.
                     */
                    if (isIpsHistoryGraph) {
                        var rowsPerColumn =
                            Math.ceil(
                                rowItems.length / 2
                            );

                        var firstColumn =
                            rowItems.slice(
                                0,
                                rowsPerColumn
                            );

                        var secondColumn =
                            rowItems.slice(
                                rowsPerColumn
                            );

                        return (
                            headerHtml +

                            '<div style="' +
                            'display:flex;' +
                            'flex-direction:row;' +
                            'align-items:flex-start;' +
                            'gap:10px;' +
                            'padding-right:2px;">' +

                            '<div style="' +
                            'display:flex;' +
                            'flex-direction:column;' +
                            'width:210px;' +
                            'min-width:210px;' +
                            (
                                secondColumn.length
                                    ? (
                                        'padding-right:10px;' +
                                        'border-right:1px solid #e2e8f0;'
                                    )
                                    : ''
                            ) +
                            '">' +

                            firstColumn.join('') +

                            '</div>' +

                            (
                                secondColumn.length
                                    ? (
                                        '<div style="' +
                                        'display:flex;' +
                                        'flex-direction:column;' +
                                        'width:210px;' +
                                        'min-width:210px;">' +

                                        secondColumn.join('') +

                                        '</div>'
                                    )
                                    : ''
                            ) +

                            '</div>'
                        );
                    }

                    /*
                     * Existing non-IPS adaptive column layout.
                     */
                    var total =
                        rowItems.length;

                    var columnCount =
                        total <= 12
                            ? 1
                            : (
                                total <= 24
                                    ? 2
                                    : 3
                            );

                    var itemsPerColumn =
                        Math.ceil(
                            total / columnCount
                        );

                    var columnWidth =
                        columnCount === 3
                            ? '170px'
                            : '180px';

                    var columnsHtml = '';

                    for (
                        var columnIndex = 0;
                        columnIndex < columnCount;
                        columnIndex++
                    ) {
                        var columnRows =
                            rowItems.slice(
                                columnIndex * itemsPerColumn,
                                (columnIndex + 1) *
                                itemsPerColumn
                            );

                        columnsHtml +=
                            '<div style="' +
                            'display:flex;' +
                            'flex-direction:column;' +
                            'min-width:' +
                            columnWidth +
                            ';' +
                            (
                                columnIndex <
                                    columnCount - 1
                                    ? (
                                        'border-right:1px solid #f0f0f0;' +
                                        'padding-right:10px;' +
                                        'margin-right:10px;'
                                    )
                                    : ''
                            ) +
                            '">' +

                            columnRows.join('') +

                            '</div>';
                    }

                    return (
                        headerHtml +

                        '<div style="' +
                        'display:flex;' +
                        'flex-direction:row;' +
                        'align-items:flex-start;">' +

                        columnsHtml +

                        '</div>'
                    );
                }
            },
            legend: { show: false },
            toolbox: { show: false },
            stateAnimation: { duration: 0 },
            emphasis: { focus: 'none' },
            // Grid: top:35 clears the toolbar; bottom:13% leaves room for the 40px-tall slider
            grid: {
                left: '4%',
                right: '5%',
                top: 35,
                bottom: '13%',
                containLabel: true
            },
            xAxis: [{
                type: 'time',
                boundaryGap: false,

                axisPointer: {
                    snap: false
                },
                min: (_reqStart !== null ? _reqStart : undefined),
                max: (_reqEnd !== null ? _reqEnd : undefined),
                axisLine: { show: true, onZero: false, lineStyle: { color: '#64748b', width: 1.5 } },
                axisTick: { show: true, lineStyle: { color: '#64748b' } },
                axisLabel: {
                    // History keeps date+time labels (DD-MM\nHH:MM) so multi-day windows stay readable
                    fontSize: 11, color: '#475569',
                    formatter: function (v) {
                        var d = new Date(v);
                        var hh = String(d.getHours()).padStart(2, '0');
                        var mm = String(d.getMinutes()).padStart(2, '0');
                        var dd = String(d.getDate()).padStart(2, '0');
                        var mo = String(d.getMonth() + 1).padStart(2, '0');
                        return '{d|' + dd + '-' + mo + '}\n{t|' + hh + ':' + mm + '}';
                    },
                    rich: {
                        d: { fontSize: 9, color: '#0f5f65', fontWeight: 700, lineHeight: 12 },
                        t: { fontSize: 10, color: '#475569', lineHeight: 12 }
                    }
                },
                splitLine: { show: true, lineStyle: { color: '#f1f5f9', width: 1 } }
            }],
            yAxis: yAxes,
            // dataZoom: matches live graph — slider height 40, teal handle, bottom:0
            dataZoom: [
                {
                    type: 'slider', height: 40, start: 0, end: 100, bottom: 0,
                    filterMode: 'none',
                    startValue: _reqStart, endValue: _reqEnd,
                    minValueSpan: 60 * 1000,
                    borderColor: '#e2e8f0',
                    handleStyle: { color: '#4bacc6', borderColor: '#215968' },
                    fillerColor: 'rgba(75,172,198,.15)',
                    textStyle: { fontSize: 11, color: '#475569' },
                    labelFormatter: function (v) {
                        var d = new Date(v);
                        var dd = String(d.getDate()).padStart(2, '0');
                        var mo = String(d.getMonth() + 1).padStart(2, '0');
                        var hh = String(d.getHours()).padStart(2, '0');
                        var mm = String(d.getMinutes()).padStart(2, '0');
                        return dd + '-' + mo + ' ' + hh + ':' + mm;
                    }
                },
                {
                    type: 'inside', filterMode: 'none',
                    startValue: _reqStart, endValue: _reqEnd,
                    minValueSpan: 60 * 1000,
                    zoomOnMouseWheel: true, moveOnMouseMove: true
                }
            ],
            color: _hgGC,
            series: sr
        });


        /*
 * IPS-only unsnapped pointer tracking.
 *
 * ECharts tooltip params may contain the nearest plotted point. Capturing the
 * actual X-axis pointer prevents the new IPS value from appearing before the
 * cursor reaches its real TimestampDevice.
 */
        //var historyIpsActualHoverMs =
        //    null;

        //historyEChart.off(
        //    'updateAxisPointer'
        //);

        //historyEChart.on(
        //    'updateAxisPointer',
        //    function (event) {
        //        if (!isIpsHistoryGraph) {
        //            return;
        //        }

        //        var axesInfo =
        //            event &&
        //            event.axesInfo;

        //        if (
        //            !axesInfo ||
        //            axesInfo.length === 0
        //        ) {
        //            return;
        //        }

        //        var pointerValue =
        //            NaN;

        //        /*
        //         * Explicitly locate the X/time axis.
        //         * Never use a Y-axis measurement as a timestamp.
        //         */
        //        for (
        //            var axisIndex = 0;
        //            axisIndex < axesInfo.length;
        //            axisIndex++
        //        ) {
        //            var axisInfo =
        //                axesInfo[axisIndex];

        //            if (
        //                axisInfo &&
        //                (
        //                    axisInfo.axisDimension === 'x' ||
        //                    axisInfo.axisDim === 'x'
        //                )
        //            ) {
        //                pointerValue =
        //                    Number(
        //                        axisInfo.value
        //                    );

        //                break;
        //            }
        //        }

        //        /*
        //         * Compatibility fallback for an ECharts version that omits the
        //         * axisDimension property.
        //         */
        //        if (
        //            isNaN(pointerValue) &&
        //            axesInfo[0]
        //        ) {
        //            pointerValue =
        //                Number(
        //                    axesInfo[0].value
        //                );
        //        }

        //        if (!isNaN(pointerValue)) {
        //            historyIpsActualHoverMs =
        //                pointerValue;
        //        }
        //    }
        //);

        //var historyIpsRenderer =
        //    historyEChart.getZr();

        //if (historyIpsRenderer) {
        //    historyIpsRenderer.off(
        //        'globalout'
        //    );

        //    historyIpsRenderer.on(
        //        'globalout',
        //        function () {
        //            historyIpsActualHoverMs =
        //                null;
        //        }
        //    );
        //}

        var historyIpsActualHoverMs =
            null;

        /*
         * _hgLiveCursorMs — continuously-updated X-axis cursor position (ms).
         *
         * Updated on every updateAxisPointer event for ALL asset types.
         * Read by the Y-axis crosshair label formatter so it can display the
         * live HH:MM:SS cursor time instead of the raw sensor value.
         * Cleared to null when the cursor leaves the chart (globalout).
         *
         * The tooltip modal header is NOT affected by this variable — it
         * continues to show the pinned last-change timestamp.
         */
        var _hgLiveCursorMs = null;

        historyEChart.off(
            'updateAxisPointer'
        );

        historyEChart.on(
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
                 * Explicitly locate the X/time axis.
                 * Never use a Y-axis measurement as a timestamp.
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
                            Number(
                                axisInfo.value
                            );

                        break;
                    }
                }

                /*
                 * Compatibility fallback for an ECharts version that omits the
                 * axisDimension property.
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
                    /*
                     * _hgLiveCursorMs — always updated for ALL asset types.
                     * Used by the Y-axis crosshair label formatter to display a
                     * continuously-moving HH:MM:SS timestamp as the cursor moves.
                     * This is separate from the tooltip modal header, which
                     * remains pinned at the last actual change timestamp.
                     */
                    _hgLiveCursorMs = pointerValue;

                    /*
                     * IPS-only: also update historyIpsActualHoverMs so the
                     * tooltip formatter can use the unsnapped cursor position.
                     */
                    if (isIpsHistoryGraph) {
                        historyIpsActualHoverMs = pointerValue;
                    }
                }
            }
        );

        var historyIpsRenderer =
            historyEChart.getZr();

        if (historyIpsRenderer) {
            historyIpsRenderer.off(
                'globalout'
            );

            historyIpsRenderer.on(
                'globalout',
                function () {
                    /*
                     * Clear both tracking variables when the cursor leaves the chart.
                     */
                    _hgLiveCursorMs = null;
                    historyIpsActualHoverMs = null;
                }
            );
        }

        $(window).off('resize.hg').on('resize.hg', function () { if (historyEChart) historyEChart.resize(); });

        // ── Dynamic y-axis on zoom ─────────────────────────────────────────
        // When the user moves the slider or wheel-zooms, recompute axis max
        // from ONLY the points inside the new visible window. This is what
        // makes a 2000-value spike show fully when zoomed to its time range,
        // and what lets a quiet 200-value window get a tight 250 axis instead
        // of being squashed by a far-away 2000 spike elsewhere.
        function _recomputeYAxisFromWindow() {
            if (!historyEChart) return;
            var opt = historyEChart.getOption();
            if (!opt || !opt.dataZoom || !opt.dataZoom.length) return;

            // Resolve the active window in ms from the slider
            var dz = opt.dataZoom[0];
            var winLo, winHi;
            if (dz.startValue !== undefined && dz.endValue !== undefined) {
                winLo = dz.startValue;
                winHi = dz.endValue;
            } else if (sortedTs.length) {
                var first = sortedTs[0], last = sortedTs[sortedTs.length - 1];
                winLo = first + (last - first) * ((dz.start || 0) / 100);
                winHi = first + (last - first) * ((dz.end || 100) / 100);
            } else {
                return;
            }

            // Walk every analog series, find max within [winLo, winHi]
            var newMaxMA = 0, newMaxV = 0, newMaxOhm = 0;
            for (var name in seriesDataMap) {
                if (binSeriesSet[name]) continue;  // skip 0/1 series
                var pts = seriesDataMap[name];
                if (!pts || !pts.length) continue;

                // Reuse the same classifier the build loop uses — by clean name
                var rawName = name.replace(/^\[R\] /, '').replace(/^\[D\] /, '');
                var pseudoCol = { displayName: rawName, isDataLogger: false, isDerived: false };
                var u = getAttrUnit(pseudoCol);
                var isMA = (u === 'mA');
                var isOhm = (u === 'Ω');

                for (var i = 0; i < pts.length; i++) {
                    var t = pts[i][0], v = pts[i][1];
                    if (t < winLo || t > winHi) continue;
                    if (v === null || isNaN(v)) continue;
                    if (isMA) { if (v > newMaxMA) newMaxMA = v; }
                    else if (isOhm) { if (v > newMaxOhm) newMaxOhm = v; }
                    else { if (v > newMaxV) newMaxV = v; }
                }
            }

            // Same axis-routing rule as the initial build
            var axis0Raw = (filter === 'all')
                ? Math.max(newMaxMA, newMaxV, newMaxOhm)
                : Math.max(newMaxMA, newMaxOhm);
            var axis0Max = _niceAxisMax(axis0Raw);
            var axis1Max = _niceAxisMax(newMaxV);

            historyEChart.setOption({
                yAxis: [
                    { max: axis0Max },
                    { max: axis1Max },
                    {} // axis 2 (0/1) — leave untouched
                ]
            }, { lazyUpdate: true });
        }

        historyEChart.off('dataZoom');
        historyEChart.on('dataZoom', function () {
            // Debounce so wheel-zoom doesn't fire 50 setOptions per second
            if (window._hgZoomTimer) clearTimeout(window._hgZoomTimer);
            window._hgZoomTimer = setTimeout(_recomputeYAxisFromWindow, 80);
        });
    }




    function renderNoData() {
        $('#divChartContainer').html(
            '<div style="display:flex;align-items:center;justify-content:center;height:200px;' +
            'color:#94a3b8;font-size:13px;border:1px solid #e2e8f0;border-radius:8px;background:#fff;">' +
            '<i class="fas fa-line-chart" style="margin-right:10px;font-size:24px;opacity:.3;"></i>No data available</div>');
    }


    /* getReportFilterValues() is deliberately NOT ported here.
     *
     * The source's version is callback-style — it resolves zone/division via
     * GetSiteById when the filters sit on "All". FRS Advance's is synchronous
     * and returns its value directly, and this page's Excel/CSV exporters read
     * that return value. Letting the callback version win would have made every
     * existing export read `undefined`.
     *
     * Nothing in the ported graph code calls it (it belonged to the source's
     * export block, which was not ported), so the page's own version stands.
     */

