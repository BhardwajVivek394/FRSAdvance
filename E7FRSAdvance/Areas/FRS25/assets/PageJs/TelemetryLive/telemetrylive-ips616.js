/* =============================================================================
 * telemetrylive-ips616.js — IPS live view features from v616, ported to v617
 * (Aurora)
 *
 * Load order: AFTER telemetrylive.js, telemetrylive-graphs.js and
 * telemetrylive-pmvibration.js (classic <script>). Nothing in those files is
 * edited; global functions are replaced on window, and every caller looks
 * them up by name when it runs.
 *
 * v616 line numbers refer to v616 TelemetryLive/telemetrylive.js.
 *
 *  I1  One display rule for cards and table: numbers to 2 decimals,
 *      values <= 0 shown as 0, text passed through
 *      (ipsFormatDisplayValue) ........................................ v616 9334-9366
 *  I2  Σ sum rows on BATT CHARGING / DISCHARGING cards
 *      (ensureIpsCardSumStyles, ipsCardAttrLabel, ipsCardBattBucket,
 *      ipsCardSumKey, buildIpsCardSumModels, ipsCardSumDisplay,
 *      buildIpsCardSumRowsHtml, updateIpsCardSums; buildIpsCard and
 *      updateIpsGridIncremental rebuild a card when its rows change) . v616 9368-9770
 *  I3  Voltage / Current / Digital tabs on the flat table
 *      (ipsClassifyTab, ipsApplyTab) .................................. v616 9842-9854, 10510-10540
 *  I4  BATT CHARGING / DISCHARGING banks summed into one row that
 *      expands to the per-bank rows (ipsGroupCurrentRows,
 *      ipsGroupIsExpanded, ipsExpandRowsForDisplay) ................... v616 10148-10309
 *  I5  Row model: relay (Digital) rows first, Pickup/Drop badges,
 *      replay-stale "!" marks (buildIpsTableRowModels) ................ v616 9856-10146
 *      v617 keeps relays in asset.dlRelays (not in attrs), so the
 *      Digital rows are built from dlRelays.
 *  I6  Downloads carry only the active tab, with banks summed
 *      (ipsActiveTabLabel, ipsViewSuffix, ipsActiveTabSlug,
 *      ipsExportRowsForActiveTab); the live-table scrape skips hidden
 *      rows and labels the section with the tab name ................ v616 10311-10345, 20628, 21968, 22500
 *      NOTE: window.buildIpsTableRowModels() now returns these export
 *      rows (v617's PDF export calls it); pass {all: true} for every row.
 *  Flat table renderer / updater (ipsRenderTable, ipsBuildTableHtml,
 *  ipsUpdateTable) .................................................... v616 10354-10492
 * ========================================================================== */

(function () {
    'use strict';

    if (typeof window === 'undefined' || typeof window.jQuery === 'undefined') return;
    if (window.__ips616Installed) return;
    window.__ips616Installed = true;

    var $ = window.jQuery;
    var W = window;
    var IPS616 = W.IPS616 = W.IPS616 || {};
    var EMPTY = '—';

    function fn(name) { return typeof W[name] === 'function'; }
    function liveData() { return W.wsLiveData || {}; }
    function esc(s) {
        return String(s === null || s === undefined ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function zf(v) {
        if (typeof W.tlZeroFloor === 'function') return W.tlZeroFloor(v);
        var n = parseFloat(v);
        return isNaN(n) ? v : (n <= 0 ? 0 : n);
    }
    function natural(a, b) {
        return String(a || '').localeCompare(String(b || ''), undefined, { numeric: true, sensitivity: 'base' });
    }

    /* ---- I1 ------------------------------------------------------------- */
    function ipsFormatDisplayValue(rawValue, emptyText) {
        if (rawValue === null || rawValue === undefined || String(rawValue).trim() === '') return emptyText;
        var n = parseFloat(rawValue);
        if (isNaN(n)) return rawValue;
        return Number(zf(n)).toFixed(2);
    }
    W.ipsFormatDisplayValue = ipsFormatDisplayValue;

    if (!fn('ipsBattBaseAttrName')) {
        W.ipsBattBaseAttrName = function (label) {
            return String(label || '')
                .replace(/(DISCHARGING|DISCHARGER|DISCHARGE|DISCHAR|CHARGING|CHARGER|CHARGE|CHAR)[\-_]\s*\d+/gi, '$1')
                .replace(/\s{2,}/g, ' ').trim();
        };
    }

    /* ---- styles (Aurora tokens) ------------------------------------------ */
    function ensureStyles() {
        if (document.getElementById('ips616-styles')) return;
        var css =
            '.ips-tabbar{display:flex;gap:6px;flex-wrap:wrap;margin:0 2px 10px;}' +
            '.ips-tab-btn{appearance:none;border:1px solid var(--at-edge-s,rgba(255,255,255,.18));background:var(--at-g1,rgba(255,255,255,.04));' +
            'color:var(--at-t2,rgba(255,255,255,.72));font-size:13px;font-weight:600;padding:7px 14px;border-radius:var(--at-r-sm,8px);cursor:pointer;' +
            'display:inline-flex;align-items:center;gap:6px;transition:background .15s ease,color .15s ease;font-family:var(--at-font-body,inherit);}' +
            '.ips-tab-btn:hover{background:var(--at-g3,rgba(255,255,255,.08));color:var(--at-t1,#fff);}' +
            '.ips-tab-btn.active{background:rgba(34,211,238,.16);border-color:var(--at-brand,#22d3ee);color:var(--at-brand,#22d3ee);}' +
            '.ips-tab-btn .ips-tab-count{font-size:11px;padding:1px 7px;border-radius:999px;background:var(--at-g3,rgba(255,255,255,.08));color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.ips-tab-empty{padding:22px 12px;text-align:center;color:var(--at-t4,rgba(255,255,255,.34));font-size:13px;' +
            'border:1px dashed var(--at-edge,rgba(255,255,255,.10));border-radius:var(--at-r-md,12px);margin-top:8px;}' +
            'table.ips-live-table tbody tr.ips-row-hidden{display:none;}' +
            'table.ips-live-table tbody tr.ips-row-group{background:rgba(34,211,238,.07);}' +
            'table.ips-live-table tbody tr.ips-row-group td.ips-aname{color:var(--at-brand,#22d3ee);}' +
            'table.ips-live-table tbody tr.ips-row-expandable{cursor:pointer;}' +
            'table.ips-live-table tbody tr.ips-row-expandable:hover{background:rgba(34,211,238,.12);}' +
            'table.ips-live-table tbody tr.ips-child-collapsed{display:none;}' +
            'table.ips-live-table tbody tr.ips-row-child{background:var(--at-g1,rgba(255,255,255,.03));}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-aname{font-weight:500;color:var(--at-t3,rgba(255,255,255,.5));padding-left:28px;position:relative;}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-aname::before{content:"\\21B3";position:absolute;left:12px;color:var(--at-t4,rgba(255,255,255,.34));}' +
            'table.ips-live-table tbody tr.ips-row-child td.ips-val{font-weight:600;color:var(--at-t2,rgba(255,255,255,.72));}' +
            '.ips-caret{display:inline-block;width:0;height:0;margin-right:7px;vertical-align:middle;border-left:5px solid var(--at-brand,#22d3ee);' +
            'border-top:4px solid transparent;border-bottom:4px solid transparent;transition:transform .15s ease;}' +
            'tr.ips-group-open .ips-caret{transform:rotate(90deg);}' +
            '.ips-child-badge{display:inline-block;margin-left:6px;background:var(--at-brand-deep,#0ea5b7);color:#fff;font-size:11px;font-weight:700;' +
            'line-height:1;padding:2px 7px;border-radius:999px;vertical-align:middle;}' +
            'table.ips-live-table td.ips-val .rdpms-dl-badge{font-size:11px;}' +
            // replay-stale "!" marker (same look as the signal port)
            '.ips-live-table td.ips-val.ws-value-server-stale{color:var(--at-t3,rgba(255,255,255,.5))!important;}' +
            '.ips-live-table td.ips-val.ws-value-server-stale::before{content:"!";display:inline-flex;align-items:center;justify-content:center;' +
            'width:14px;height:14px;margin-right:4px;border-radius:50%;background:var(--at-t4,rgba(255,255,255,.34));color:var(--at-bg0,#060914);' +
            'font-size:10px;font-weight:800;line-height:14px;vertical-align:middle;}' +
            // Σ sum rows on cards
            '.ips-attr-row.ips-sum-row{border-top:1px dashed var(--at-edge-s,rgba(255,255,255,.18));margin-top:4px;padding-top:6px;}' +
            '.ips-attr-row.ips-sum-row .ips-attr-name{font-weight:700;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-row.ips-sum-row .ips-sum-sigma{font-weight:700;margin-right:5px;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-row.ips-sum-row .ips-sum-count{display:inline-block;margin-left:6px;background:var(--at-brand-deep,#0ea5b7);color:#fff;' +
            'font-size:10px;font-weight:700;line-height:1;padding:2px 6px;border-radius:999px;vertical-align:middle;}' +
            '.ips-attr-val.ips-sum-val{font-weight:800;color:var(--at-brand,#22d3ee);}' +
            '.ips-attr-val.ips-sum-val.no-data{color:var(--at-t4,rgba(255,255,255,.34));font-weight:400;}' +
            '.ips-card-head .tl-asset-action{margin-left:auto;margin-right:6px;}';
        var st = document.createElement('style');
        st.id = 'ips616-styles';
        st.appendChild(document.createTextNode(css));
        (document.head || document.documentElement).appendChild(st);
    }
    W.ensureIpsCardSumStyles = ensureStyles;
    ensureStyles();

    /* ---- labels / asset filter ------------------------------------------ */
    function plainLabel(assetId, attrKey, aObj) {
        aObj = aObj || {};
        var label = aObj.AliasName || aObj.AttrName || attrKey;
        var id = aObj.AttrId || aObj.AssetAttributeId;
        if (fn('getAttrDisplayNamePlain')) label = W.getAttrDisplayNamePlain(attrKey, id, assetId) || label;
        else if (fn('getAttrDisplayName')) label = W.getAttrDisplayName(attrKey, id, assetId) || label;
        return String(label).replace(/<[^>]*>/g, '').trim() || attrKey;
    }
    function ipsCardAttrLabel(attrKey, attrObject, assetId) { return plainLabel(assetId, attrKey, attrObject); }
    W.ipsCardAttrLabel = ipsCardAttrLabel;

    function ipsAssetIds() {
        var LD = liveData();
        return Object.keys(LD).filter(function (id) {
            if (!LD[id]) return false;
            if (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(id)) return false;
            return !fn('assetMatchesSelectedType') || W.assetMatchesSelectedType(id);
        });
    }
    function assetTs(asset) {
        var ts = EMPTY;
        if (fn('getOperationTimestampDevice')) {
            var op = W.getOperationTimestampDevice(asset);
            if (op && fn('fmtTimestampDevice')) ts = W.fmtTimestampDevice(op);
        }
        if (ts === EMPTY && asset.lastUpdated && fn('fmtTime')) ts = W.fmtTime(asset.lastUpdated);
        return ts;
    }
    function isDlAttr(asset, key, aObj) {
        return String((aObj && aObj.DataType) || '').trim().toLowerCase() === 'datalogger' ||
            Object.prototype.hasOwnProperty.call((asset && asset.dlRelays) || {}, key);
    }
    function sortedAttrKeys(attrs) {
        var keys = Object.keys(attrs || {});
        keys.sort(function (a, b) {
            var oA = (attrs[a] && attrs[a].AttrOrder != null) ? parseInt(attrs[a].AttrOrder, 10) : 999;
            var oB = (attrs[b] && attrs[b].AttrOrder != null) ? parseInt(attrs[b].AttrOrder, 10) : 999;
            return oA - oB;
        });
        return keys;
    }

    /* ---- I3 / I5  row models --------------------------------------------- */
    function ipsClassifyTab(isDataLogger, attrKey, attrLabel) {
        if (isDataLogger) return 'digital';
        var hay = (String(attrKey || '') + ' ' + String(attrLabel || '')).toUpperCase();
        if (hay.indexOf('IIPS') !== -1) return 'current';
        return 'voltage';
    }
    W.ipsClassifyTab = ipsClassifyTab;

    function staleInfo(aObj) {
        var stale = fn('isPmReplayStale') ? W.isPmReplayStale(aObj) === true : false;
        var title = '';
        if (stale) {
            if (fn('buildPmStaleTitle')) title = W.buildPmStaleTitle(aObj);
            else if (fn('_wsStaleTitle')) title = W._wsStaleTitle(aObj).replace(/&#0?39;/g, "'").replace(/&amp;/g, '&');
            else title = 'Replay value (IsFresh = false)';
        }
        return { isStale: stale, staleTitle: title };
    }

    function buildRowsAll() {
        var rows = [];
        var LD = liveData();
        var ids = ipsAssetIds();
        for (var i = 0; i < ids.length; i++) {
            var aid = ids[i];
            var asset = LD[aid];
            var name = asset.AssetName || ('Asset ' + aid);
            var attrs = asset.attrs || {};
            var relays = asset.dlRelays || {};
            var ts = assetTs(asset);
            var keys = sortedAttrKeys(attrs);
            var relayKeys = Object.keys(relays);
            if (!keys.length && !relayKeys.length) {
                rows.push({ assetId: aid, assetName: name, attr: EMPTY, attrKey: '', value: EMPTY, ts: ts, dlClass: '',
                    isDataLogger: false, ipsTab: 'voltage', isStale: false, staleTitle: '' });
                continue;
            }
            var seenRelay = {};
            for (var k = 0; k < keys.length; k++) {
                var key = keys[k];
                var aObj = attrs[key] || {};
                var isDL = isDlAttr(asset, key, aObj);
                if (isDL) seenRelay[key] = true;
                var label = plainLabel(aid, key, aObj);
                var value = ipsFormatDisplayValue(aObj.Value, isDL ? '-' : EMPTY);
                var norm = String(value).trim().toLowerCase();
                var st = isDL ? { isStale: false, staleTitle: '' } : staleInfo(aObj);
                rows.push({
                    assetId: aid, assetName: name, attr: label, attrKey: key, value: value, ts: ts,
                    dlClass: norm === 'pickup' ? 'pickup' : (norm === 'drop' ? 'drop' : ''),
                    isDataLogger: isDL, ipsTab: ipsClassifyTab(isDL, key, label),
                    isStale: st.isStale, staleTitle: st.staleTitle
                });
            }
            for (var r = 0; r < relayKeys.length; r++) {
                var rk = relayKeys[r];
                if (seenRelay[rk]) continue;
                var relay = relays[rk] || {};
                var state = relay.isNull ? '-' : (relay.isPickup ? 'Pickup' : 'Drop');
                rows.push({
                    assetId: aid, assetName: name, attr: String(relay.displayName || rk), attrKey: rk, value: state, ts: ts,
                    dlClass: relay.isNull ? '' : (relay.isPickup ? 'pickup' : 'drop'),
                    isDataLogger: true, ipsTab: 'digital', isStale: false, staleTitle: ''
                });
            }
        }
        rows.sort(function (a, b) {
            if (a.isDataLogger !== b.isDataLogger) return a.isDataLogger ? -1 : 1;
            return natural(a.assetName, b.assetName) || natural(a.attr, b.attr);
        });
        return rows;
    }

    /* ---- I4  BATT grouping ----------------------------------------------- */
    var IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME = false;
    function ipsGroupCurrentRows(rows) {
        if (!rows || !rows.length) return rows;
        var groups = {}, out = [];
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var bucket = null;
            if (r && r.ipsTab === 'current' && !r.isDataLogger) {
                var up = String(r.assetName || '').toUpperCase();
                if (up.indexOf('DISCHARGING') !== -1) bucket = 'discharging';
                else if (up.indexOf('CHARGING') !== -1) bucket = 'charging';
            }
            if (bucket === null) { out.push(r); continue; }
            var base = String(r.assetName || '').replace(/[\s\-_]*\d+\s*$/, '').trim() || String(r.assetName || '');
            var groupAttr = W.ipsBattBaseAttrName(r.attr);
            var key = bucket + '||' + groupAttr + '||' + base;
            var num = parseFloat(r.value);
            var g = groups[key];
            if (!g) {
                g = { _agg: true, assetId: 'ipsgrp::' + key, assetName: base,
                    attr: IPS_BATT_GROUP_KEEP_FIRST_ATTR_NAME ? r.attr : groupAttr, attrKey: groupAttr,
                    sum: isFinite(num) ? num : 0, count: isFinite(num) ? 1 : 0, ts: r.ts, members: [] };
                groups[key] = g;
                out.push(g);
            } else {
                if (isFinite(num)) { g.sum += num; g.count++; }
                if (String(r.ts).length === String(g.ts).length && String(r.ts) > String(g.ts)) g.ts = r.ts;
            }
            g.members.push(r);
        }
        for (var j = 0; j < out.length; j++) {
            var o = out[j];
            if (!o || !o._agg) continue;
            out[j] = {
                assetId: o.assetId, assetName: o.assetName, attr: o.attr, attrKey: o.attrKey,
                value: o.count > 0 ? ipsFormatDisplayValue(o.sum, EMPTY) : EMPTY, ts: o.ts, dlClass: '',
                isDataLogger: false, ipsTab: 'current', isStale: false, staleTitle: '', isGroup: true, members: o.members
            };
        }
        return out;
    }
    W.ipsGroupCurrentRows = ipsGroupCurrentRows;

    var expandedGroups = {};
    function ipsGroupIsExpanded(id) { return !!(id && expandedGroups[id]); }
    W.ipsGroupIsExpanded = ipsGroupIsExpanded;
    function ipsExpandRowsForDisplay(rows) {
        if (!rows || !rows.length) return rows || [];
        var flat = [];
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            flat.push(r);
            if (r && r.isGroup && r.members && r.members.length) {
                for (var m = 0; m < r.members.length; m++) {
                    var mr = r.members[m];
                    flat.push({
                        assetId: mr.assetId, assetName: mr.assetName, attr: mr.attr, attrKey: mr.attrKey,
                        value: mr.value, ts: mr.ts, dlClass: mr.dlClass || '', isDataLogger: false, ipsTab: 'current',
                        isStale: !!mr.isStale, staleTitle: mr.staleTitle || '', isChild: true, parentId: r.assetId
                    });
                }
            }
        }
        return flat;
    }
    W.ipsExpandRowsForDisplay = ipsExpandRowsForDisplay;

    /* ---- I6  active tab / export ----------------------------------------- */
    function tableMode() {
        if (fn('ipsTableMode')) return !!W.ipsTableMode();
        return $('#drpView').val() === 'Table';
    }
    function activeTab() {
        return (W.ipsActiveTab === 'current' || W.ipsActiveTab === 'digital') ? W.ipsActiveTab : 'voltage';
    }
    function tabLabel(t) { return t === 'current' ? 'IPS Current' : (t === 'digital' ? 'IPS Digital' : 'IPS Voltage'); }
    function ipsActiveTabLabel() { return tableMode() ? tabLabel(activeTab()) : ''; }
    function ipsViewSuffix() { var l = ipsActiveTabLabel(); return l ? '   |   View: ' + l : ''; }
    function ipsActiveTabSlug() { var l = ipsActiveTabLabel(); return l ? l.replace(/^IPS\s+/, '') + '_' : ''; }
    function ipsExportRowsForActiveTab() {
        var all = ipsGroupCurrentRows(buildRowsAll());
        if (!tableMode()) return all;
        var t = activeTab();
        return all.filter(function (r) { return r && (r.ipsTab || 'voltage') === t; });
    }
    W.ipsActiveTabLabel = ipsActiveTabLabel;
    W.ipsViewSuffix = ipsViewSuffix;
    W.ipsActiveTabSlug = ipsActiveTabSlug;
    W.ipsExportRowsForActiveTab = ipsExportRowsForActiveTab;
    W.buildIpsTableRowModels = function (opts) {
        return (opts && opts.all) ? buildRowsAll() : ipsExportRowsForActiveTab();
    };
    IPS616.buildRowsAll = buildRowsAll;

    /* ---- flat table ------------------------------------------------------ */
    function valueCellHtml(r) {
        var noData = (r.value === EMPTY);
        return '<td class="ips-val' + (noData ? ' no-data' : '') + (r.isStale ? ' ws-value-server-stale' : '') + '"' +
            (r.isStale ? ' title="' + esc(r.staleTitle) + '"' : '') + '>' +
            (r.dlClass ? '<span class="rdpms-dl-badge ' + r.dlClass + '">' + esc(r.value) + '</span>' : esc(r.value)) + '</td>';
    }
    function numberRows(rows) {
        var counters = { voltage: 0, current: 0, digital: 0 };
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var t = (r.ipsTab === 'current' || r.ipsTab === 'digital') ? r.ipsTab : 'voltage';
            r._tab = t;
            r._sno = r.isChild ? '' : String(++counters[t]);
        }
        return counters;
    }
    function displayRows() { return ipsExpandRowsForDisplay(ipsGroupCurrentRows(buildRowsAll())); }
    function assetCount() { return ipsAssetIds().length; }

    function ipsRenderTable() {
        if (fn('ensureIpsTableStyles')) W.ensureIpsTableStyles();
        ensureStyles();
        var $c = $('#divTelemetryLive');
        var rows = displayRows();
        if (!rows.length) {
            $c.html('<div class="ips-table-wrap"><div class="text-center text-muted p-5">' +
                '<i class="fas fa-satellite-dish fa-2x mb-3" style="color:#259dab;display:block;"></i>Waiting for live data…</div></div>');
            return;
        }
        var counts = numberRows(rows);
        var tab = activeTab();
        var n = assetCount();
        var h = '<div class="ips-table-wrap">';
        h += '<div class="ips-table-head"><h6><i class="fas fa-table" style="color:#259dab;"></i> IPS Live Grid</h6>' +
            '<span class="ips-count">' + n + ' Asset' + (n !== 1 ? 's' : '') + '</span></div>';
        h += '<div class="ips-tabbar" role="tablist">';
        [['voltage', 'fa-bolt'], ['current', 'fa-wave-square'], ['digital', 'fa-toggle-on']].forEach(function (t) {
            h += '<button type="button" class="ips-tab-btn" role="tab" data-ipstab="' + t[0] + '"><i class="fas ' + t[1] + '"></i> ' +
                tabLabel(t[0]) + ' <span class="ips-tab-count">' + counts[t[0]] + '</span></button>';
        });
        h += '</div>';
        h += '<div class="ips-table-scroll"><table id="ipsLiveTable" class="ips-live-table" data-export-label="' + esc(tabLabel(tab)) + '"><thead><tr>' +
            '<th class="ips-c-sno" style="width:64px;">S.No</th><th>Asset Name</th><th>Attribute</th>' +
            '<th class="ips-c-val" style="width:140px;">Live Value</th><th class="ips-c-ts" style="width:120px;">Timestamp</th>' +
            '</tr></thead><tbody>';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var cls = [];
            if (r._tab !== tab) cls.push('ips-row-hidden');
            var extra = '';
            var aname = esc(r.assetName);
            if (r.isChild) {
                cls.push('ips-row-child');
                if (!ipsGroupIsExpanded(r.parentId)) cls.push('ips-child-collapsed');
                extra = ' data-parent="' + esc(r.parentId) + '"';
            } else if (r.isGroup) {
                cls.push('ips-row-group');
                if (r.members && r.members.length) {
                    cls.push('ips-row-expandable');
                    if (ipsGroupIsExpanded(r.assetId)) cls.push('ips-group-open');
                    extra = ' data-ips-group="' + esc(r.assetId) + '" aria-expanded="' + ipsGroupIsExpanded(r.assetId) + '"';
                    aname = '<span class="ips-caret" aria-hidden="true"></span>' + aname +
                        ' <span class="ips-child-badge">' + r.members.length + '</span>';
                }
            }
            h += '<tr data-ips-id="' + esc(r.assetId) + '" data-attr="' + esc(r.attrKey) + '" data-ips-tab="' + r._tab + '"' + extra +
                (cls.length ? ' class="' + cls.join(' ') + '"' : '') + '>' +
                '<td class="ips-sno">' + r._sno + '</td>' +
                '<td class="ips-aname">' + aname + '</td>' +
                '<td class="ips-attr">' + esc(r.attr) + '</td>' +
                valueCellHtml(r) +
                '<td class="ips-ts">' + esc(r.ts) + '</td></tr>';
        }
        h += '</tbody></table></div>';
        ['voltage', 'current', 'digital'].forEach(function (t) {
            h += '<div class="ips-tab-empty" data-ips-tab="' + t + '" style="display:none;">No ' + tabLabel(t) + ' readings.</div>';
        });
        h += '</div>';
        $c.html(h);
        ipsApplyTab(tab);
        if (fn('syncDownloadVisibility')) W.syncDownloadVisibility(); else $('#downloadContainer').show();
    }

    // Plain table used by the download when no live table is on screen.
    function ipsBuildTableHtml(rows) {
        rows = rows || ipsExportRowsForActiveTab();
        if (!rows.length) return '';
        var label = ipsActiveTabLabel() || 'IPS Live Grid';
        var h = '<div class="ips-table-wrap"><div class="ips-table-scroll"><table class="ips-live-table" data-export-label="' + esc(label) + '"><thead><tr>' +
            '<th>S.No</th><th>Asset Name</th><th>Attribute</th><th>Live Value</th><th>Timestamp</th></tr></thead><tbody>';
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            h += '<tr><td class="ips-sno">' + (i + 1) + '</td><td class="ips-aname">' + esc(r.assetName) + '</td>' +
                '<td class="ips-attr">' + esc(r.attr) + '</td><td class="ips-val">' + esc(r.value) + '</td>' +
                '<td class="ips-ts">' + esc(r.ts) + '</td></tr>';
        }
        return h + '</tbody></table></div></div>';
    }

    function ipsUpdateTable() {
        var table = document.getElementById('ipsLiveTable');
        if (!table || !table.tBodies.length) { ipsRenderTable(); return; }
        var rows = displayRows();
        var trs = table.tBodies[0].rows;
        if (trs.length !== rows.length) { ipsRenderTable(); return; }
        for (var c = 0; c < rows.length; c++) {
            if (trs[c].getAttribute('data-ips-id') !== String(rows[c].assetId) ||
                trs[c].getAttribute('data-attr') !== String(rows[c].attrKey)) { ipsRenderTable(); return; }
        }
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            var $val = $(trs[i]).find('td.ips-val');
            if ($val.text() !== String(r.value)) {
                if (r.dlClass) $val.html('<span class="rdpms-dl-badge ' + r.dlClass + '">' + esc(r.value) + '</span>');
                else $val.text(r.value);
                if (r.value === EMPTY) $val.addClass('no-data');
                else {
                    $val.removeClass('no-data').addClass('ips-val-flash');
                    (function ($v) { setTimeout(function () { $v.removeClass('ips-val-flash'); }, 1200); })($val);
                }
            }
            var was = $val.hasClass('ws-value-server-stale');
            if (r.isStale && !was) $val.addClass('ws-value-server-stale');
            else if (!r.isStale && was) $val.removeClass('ws-value-server-stale');
            if (r.isStale) { if ($val.attr('title') !== r.staleTitle) $val.attr('title', r.staleTitle); }
            else if (was) $val.removeAttr('title');
            var $ts = $(trs[i]).find('td.ips-ts');
            if ($ts.text() !== String(r.ts)) $ts.text(r.ts);
        }
    }

    function ipsApplyTab(tab) {
        tab = (tab === 'current' || tab === 'digital') ? tab : 'voltage';
        W.ipsActiveTab = tab;
        var table = document.getElementById('ipsLiveTable');
        if (!table) return;
        var $wrap = $(table).closest('.ips-table-wrap');
        $wrap.find('.ips-tab-btn').each(function () {
            var on = $(this).attr('data-ipstab') === tab;
            $(this).toggleClass('active', on).attr('aria-selected', on ? 'true' : 'false');
        });
        var counts = { voltage: 0, current: 0, digital: 0 };
        $(table).find('tbody tr[data-ips-id]').each(function () {
            var t = $(this).attr('data-ips-tab') || 'voltage';
            counts[t] = (counts[t] || 0) + 1;
            $(this).toggleClass('ips-row-hidden', t !== tab);
        });
        ['voltage', 'current', 'digital'].forEach(function (t) {
            $wrap.find('.ips-tab-empty[data-ips-tab="' + t + '"]').css('display', (t === tab && !counts[t]) ? 'block' : 'none');
        });
        table.setAttribute('data-export-label', tabLabel(tab));
    }

    W.ipsRenderTable = ipsRenderTable;
    W.ipsBuildTableHtml = ipsBuildTableHtml;
    W.ipsUpdateTable = ipsUpdateTable;
    W.ipsApplyTab = ipsApplyTab;

    if (!W._ips616TabHandlerBound) {
        W._ips616TabHandlerBound = true;
        $(document).on('click.ips616', '.ips-tab-btn', function () { ipsApplyTab($(this).attr('data-ipstab')); });
        $(document).on('click.ips616', 'table.ips-live-table tr.ips-row-expandable', function () {
            var gid = $(this).attr('data-ips-group');
            if (!gid) return;
            var open = !ipsGroupIsExpanded(gid);
            if (open) expandedGroups[gid] = true; else delete expandedGroups[gid];
            $(this).toggleClass('ips-group-open', open).attr('aria-expanded', open ? 'true' : 'false');
            $(this).closest('table').find('tr.ips-row-child').each(function () {
                if ($(this).attr('data-parent') === gid) $(this).toggleClass('ips-child-collapsed', !open);
            });
        });
    }

    // Download scrape of the live table: skip rows of other tabs and the
    // per-bank breakdown (v616 exports the summed rows of the active tab).
    if (fn('tlCollectExportSections')) {
        var origCollect = W.tlCollectExportSections;
        W.tlCollectExportSections = function (scope) {
            var parked = [];
            try {
                ((scope && scope.tables) || []).forEach(function (tbl) {
                    if (!tbl.classList || !tbl.classList.contains('ips-live-table')) return;
                    tbl.querySelectorAll('tbody tr.ips-row-hidden, tbody tr.ips-row-child').forEach(function (tr) {
                        var mark = document.createComment('ips616');
                        tr.parentNode.replaceChild(mark, tr);
                        parked.push([mark, tr]);
                    });
                    var n = 0;
                    tbl.querySelectorAll('tbody tr td.ips-sno').forEach(function (td) { td.setAttribute('data-ips616-sno', td.textContent); td.textContent = String(++n); });
                });
                return origCollect.apply(this, arguments);
            } finally {
                parked.forEach(function (p) { if (p[0].parentNode) p[0].parentNode.replaceChild(p[1], p[0]); });
                ((scope && scope.tables) || []).forEach(function (tbl) {
                    if (!tbl.querySelectorAll) return;
                    tbl.querySelectorAll('td[data-ips616-sno]').forEach(function (td) {
                        td.textContent = td.getAttribute('data-ips616-sno');
                        td.removeAttribute('data-ips616-sno');
                    });
                });
            }
        };
    }

    /* ---- I2  card grid sums ---------------------------------------------- */
    var IPS_CARD_SUM_ONLY_CURRENT = true;
    var IPS_CARD_SUM_FALLBACK_ANY_NUMERIC = true;
    var IPS_CARD_SUM_MIN_MEMBERS = 2;

    function ipsCardBattBucket(asset, assetId) {
        if (!asset) return null;
        var up = String(asset.AssetName || '').toUpperCase();
        if (up.indexOf('DISCHARG') !== -1) return 'discharging';
        if (up.indexOf('CHARG') !== -1) return 'charging';
        var attrs = asset.attrs || {};
        var keys = Object.keys(attrs);
        for (var i = 0; i < keys.length; i++) {
            var l = (keys[i] + ' ' + plainLabel(assetId, keys[i], attrs[keys[i]])).toUpperCase();
            if (l.indexOf('DISCHAR') !== -1) return 'discharging';
            if (l.indexOf('CHAR') !== -1) return 'charging';
        }
        return null;
    }
    function ipsCardSumKey(label) {
        return String(label || '').toUpperCase().replace(/[^A-Z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    }
    function buildIpsCardSumModels(assetId) {
        var asset = liveData()[assetId];
        if (!asset || !ipsCardBattBucket(asset, assetId)) return [];
        var attrs = asset.attrs || {};
        var keys = sortedAttrKeys(attrs);
        function collect(currentOnly) {
            var groups = {}, order = [];
            for (var k = 0; k < keys.length; k++) {
                var an = keys[k];
                var aObj = attrs[an] || {};
                if (isDlAttr(asset, an, aObj)) continue;
                var label = plainLabel(assetId, an, aObj);
                if (currentOnly && ipsClassifyTab(false, an, label) !== 'current') continue;
                var num = parseFloat(ipsFormatDisplayValue(aObj.Value, EMPTY));
                var base = W.ipsBattBaseAttrName(label);
                var key = ipsCardSumKey(base);
                if (!key) continue;
                var g = groups[key];
                if (!g) { g = groups[key] = { key: key, label: base, sum: 0, count: 0, members: 0 }; order.push(g); }
                g.members++;
                if (isFinite(num)) { g.sum += num; g.count++; }
            }
            return order.filter(function (g) { return g.members >= IPS_CARD_SUM_MIN_MEMBERS; });
        }
        var models = collect(IPS_CARD_SUM_ONLY_CURRENT);
        if (!models.length && IPS_CARD_SUM_ONLY_CURRENT && IPS_CARD_SUM_FALLBACK_ANY_NUMERIC) models = collect(false);
        return models;
    }
    function ipsCardSumDisplay(m) {
        if (!m || m.count === 0) return EMPTY;
        return ipsFormatDisplayValue(m.sum, EMPTY);
    }
    function buildIpsCardSumRowsHtml(assetId) {
        var models = buildIpsCardSumModels(assetId);
        var h = '';
        for (var i = 0; i < models.length; i++) {
            var m = models[i];
            var disp = ipsCardSumDisplay(m);
            var title = 'Sum of ' + m.members + ' bank reading' + (m.members !== 1 ? 's' : '') + ' — ' + m.label;
            h += '<div class="ips-attr-row ips-sum-row" data-sum-key="' + esc(m.key) + '">' +
                '<span class="ips-attr-name" title="' + esc(title) + '"><span class="ips-sum-sigma">Σ</span>' + esc(m.label) +
                '<span class="ips-sum-count">' + m.members + '</span></span>' +
                '<span class="ips-attr-val ips-sum-val' + (disp === EMPTY ? ' no-data' : '') + '" data-sum="' + esc(m.key) + '">' + disp + '</span></div>';
        }
        return h;
    }
    function updateIpsCardSums($card, assetId) {
        var models = buildIpsCardSumModels(assetId);
        var $rows = $card.find('.ips-attr-row.ips-sum-row');
        if ($rows.length !== models.length) return false;
        for (var i = 0; i < models.length; i++) {
            var m = models[i];
            var $v = $card.find('.ips-sum-val').filter(function () { return this.getAttribute('data-sum') === m.key; });
            if (!$v.length) return false;
            var disp = ipsCardSumDisplay(m);
            if ($v.text() !== String(disp)) {
                $v.text(disp).toggleClass('no-data', disp === EMPTY);
                if (disp !== EMPTY) {
                    $v.addClass('ips-val-flash');
                    (function (el) { setTimeout(function () { el.removeClass('ips-val-flash'); }, 1200); })($v);
                }
            }
        }
        return true;
    }
    W.ipsCardBattBucket = ipsCardBattBucket;
    W.ipsCardSumKey = ipsCardSumKey;
    W.buildIpsCardSumModels = buildIpsCardSumModels;
    W.ipsCardSumDisplay = ipsCardSumDisplay;
    W.buildIpsCardSumRowsHtml = buildIpsCardSumRowsHtml;
    W.updateIpsCardSums = updateIpsCardSums;

    function staleMarks(assetId, an, rawVal, valCls) {
        var map = (W.wsStaleAttrs && W.wsStaleAttrs[assetId]) || {};
        if (map[an]) valCls += ' ws-stale-val';
        return {
            cls: valCls,
            tip: fn('_getValueTooltip') ? (W._getValueTooltip(an, parseFloat(rawVal), valCls) || '') : '',
            marks: fn('_getCellMarkers') ? W._getCellMarkers(valCls) : ''
        };
    }

    // v617 card (Aurora) + 616 formatting, Avg button and Σ rows.
    W.buildIpsCard = function (assetId) {
        var asset = liveData()[assetId];
        if (!asset) return '';
        ensureStyles();
        var name = asset.AssetName || ('Asset ' + assetId);
        var attrs = asset.attrs || {};
        var keys = sortedAttrKeys(attrs);
        var ts = assetTs(asset);
        var h = '<div class="ips-asset-card" data-ips-id="' + esc(assetId) + '">';
        h += '<div class="ips-card-head"><span class="ips-asset-name" title="' + esc(name) + '">' + esc(name) + '</span>' +
            '<button type="button" class="tl-asset-action tl-aa-avg" title="Avg values" onclick="fnShowFRSAttributeRangeHistory(\'' + esc(assetId) + '\')">' +
            '<i class="fa-solid fa-chart-column"></i></button><span class="ips-status-dot"></span></div>';
        h += '<div class="ips-attr-list">';
        if (!keys.length) {
            h += '<div class="ips-attr-row"><span class="ips-attr-name" style="color:var(--at-t4,rgba(255,255,255,0.34));font-style:italic;">Waiting for data…</span></div>';
        } else {
            for (var k = 0; k < keys.length; k++) {
                var an = keys[k];
                var aObj = attrs[an] || {};
                var disp = ipsFormatDisplayValue(aObj.Value, EMPTY);
                var label = plainLabel(assetId, an, aObj);
                var labelHtml = fn('formatAliasName') ? (W.formatAliasName(label) || esc(label)) : esc(label);
                var s = staleMarks(assetId, an, aObj.Value, disp === EMPTY ? 'ips-attr-val no-data' : 'ips-attr-val');
                h += '<div class="ips-attr-row"><span class="ips-attr-name" title="' + esc(label) + '">' + labelHtml + '</span>' +
                    '<span class="' + s.cls + '" data-attr="' + esc(an) + '"' + (s.tip ? ' title="' + esc(s.tip) + '"' : '') + '>' +
                    esc(disp) + s.marks + '</span></div>';
            }
            h += buildIpsCardSumRowsHtml(assetId);
        }
        h += '</div>';
        h += '<div class="ips-card-footer"><i class="fas fa-clock"></i> ' + esc(ts) + '</div></div>';
        return h;
    };

    W.updateIpsGridIncremental = function (updatedAssetIds) {
        var $grid = $('#ipsCardGrid');
        if (!$grid.length) { if (fn('renderIpsGridView')) W.renderIpsGridView(); return; }
        var ids = (updatedAssetIds && updatedAssetIds.length) ? updatedAssetIds : Object.keys(liveData());
        var LD = liveData();
        for (var i = 0; i < ids.length; i++) {
            var aid = String(ids[i]);
            if (fn('isAssetInBulkWhitelist') && !W.isAssetInBulkWhitelist(aid)) continue;
            if (fn('assetMatchesSelectedType') && !W.assetMatchesSelectedType(aid)) continue;
            var asset = LD[aid];
            if (!asset) continue;
            var $card = $grid.find('.ips-asset-card').filter(function () { return this.getAttribute('data-ips-id') === aid; });
            if (!$card.length) {
                $grid.append(W.buildIpsCard(aid));
                var total = $grid.find('.ips-asset-card').length;
                $('#divTelemetryLive .ips-grid-badge').text(total + ' Asset' + (total !== 1 ? 's' : ''));
                continue;
            }
            var attrs = asset.attrs || {};
            var rendered = $card.find('.ips-attr-row:not(.ips-sum-row) .ips-attr-val[data-attr]').length;
            if (rendered !== Object.keys(attrs).length || !updateIpsCardSums($card, aid)) {
                $card.replaceWith(W.buildIpsCard(aid));
                continue;
            }
            $card.find('.ips-attr-row:not(.ips-sum-row) .ips-attr-val[data-attr]').each(function () {
                var $v = $(this);
                var an = $v.attr('data-attr');
                var aObj = attrs[an] || {};
                var disp = ipsFormatDisplayValue(aObj.Value, EMPTY);
                var s = staleMarks(aid, an, aObj.Value, disp === EMPTY ? 'ips-attr-val no-data' : 'ips-attr-val');
                var old = $v.clone().children('.ws-stale-marker,.ws-warn-marker').remove().end().text();
                if (old !== String(disp)) {
                    $v.html(esc(disp) + s.marks).attr('class', s.cls).attr('title', s.tip);
                    if (disp !== EMPTY) {
                        $v.addClass('ips-val-flash');
                        (function (el) { setTimeout(function () { el.removeClass('ips-val-flash'); }, 1200); })($v);
                    }
                } else {
                    $v.attr('class', s.cls).attr('title', s.tip);
                    $v.find('.ws-stale-marker,.ws-warn-marker').remove();
                    if (s.marks) $v.append(s.marks);
                }
            });
            $card.find('.ips-card-footer').html('<i class="fas fa-clock"></i> ' + esc(assetTs(asset)));
            $card.addClass('ips-card-flash');
            setTimeout(function (c) { return function () { c.removeClass('ips-card-flash'); }; }($card), 1000);
        }
    };

    IPS616.version = '616-port-1';
    console.log('[IPS616] IPS v616 features installed');
})();
