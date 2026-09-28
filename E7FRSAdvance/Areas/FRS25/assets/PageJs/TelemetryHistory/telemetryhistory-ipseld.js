/* =============================================================================
 * telemetryhistory-ipseld.js — IPS and ELD pivoted history view.
 *
 * Ported from the web project (E7MRIV2Web) Telemetry History view. Kept out of
 * Index.cshtml for the same reason as the vibration module: that view is
 * already ~320KB and Razor's WriteLiteral chunking hits the csc CS1647 limit,
 * which 500s the page with no useful error.
 *
 * What it does
 * ------------
 * Track / Signal / Point Machine render one table per asset. IPS and ELD are
 * read across assets instead — the question is "what did every asset read at
 * this instant", not "what did this asset do over time". So these two types get
 * one shared table pivoted the other way: row = timestamp, column = asset.
 *
 * Client-side only: it reuses parseChartData's already-resolved columns
 * (whitelisted, alias-named, DataLogger-classified, union timestamps) rather
 * than re-deriving them, so it stays consistent with the graph.
 *
 * Load order: a classic <script> AFTER the view's inline script and AFTER
 * telemetryhistory-compat.js, which supplies isIpsHistoryAssetType /
 * isEldHistoryAssetType / formatTimestampWithBadge / the pager helpers.
 *
 * Entry points used by the renderHistoryTable dispatch in Index.cshtml:
 *   renderEldHistoryPivot(assetResults)
 *   renderIpsHistoryPivot(assetResults)
 * ========================================================================== */


    // ============================================================
    // ELD / IPS — PIVOTED SINGLE-TABLE VIEW (row = timestamp, column = asset)
    // ============================================================
    // Client-side only: reuses parseChartData's already-resolved columns
    // (whitelisted, alias-named, DataLogger-classified, union timestamps)
    // and re-shapes them into one shared table instead of per-asset blocks.
    // No new API calls; no change to how Track/Signal/PM/Vibration render.

    var ipsViewMode = 'voltage'; // 'voltage' | 'current' | 'digital'

    /*
     * Fixed report identity columns.
     *
     * Point Machine / Track / Signal reports carry these on every row
     * (fnDownloadExcel / CSV / PDF). IPS, ELD and Vibration used to carry the
     * same information only in the merged banner above the header, so their
     * files did not line up with the rest of the reports. These two helpers are
     * the single source of truth for the header labels and the per-row values.
     */
    function _thReportFixedHeaders() {
        return ['Zone', 'Division', 'Station', 'Vendor Name', 'Asset Type'];
    }

    function _thReportFixedValues(filterValues) {
        var fv = filterValues || {};
        return [
            fv.zone || '',
            fv.division || '',
            fv.station || '',
            fv.vendorName || 'Energy7',
            fv.assetType || ''
        ];
    }

    /*
     * IPS per-tab payload cache.
     *
     * Switching the Voltage / Current / Digital tab re-runs the search scoped
     * to that tab's attribute ids (see setIpsView), so lastAssetResults only
     * ever holds ONE bucket. The download therefore produced a single sheet
     * for whichever tab happened to be open.
     *
     * Each tab's payload is remembered here as it is rendered, so the export
     * can write one sheet per tab the user has actually visited -- with no
     * extra API calls. The cache is keyed by the current search (assets, site,
     * asset type, date/time range); any change to those invalidates it, so a
     * stale bucket from a previous search can never be exported.
     */
    var _ipsBucketResults = {};
    var _ipsBucketResultsKey = '';

    function _ipsCurrentSearchKey() {
        var ids = [];
        try {
            if (typeof msGetValues === 'function') {
                ids = (msGetValues('asset') || []).slice().sort();
            }
        } catch (e) { ids = []; }
        return [
            ids.join(','),
            $('#drpSite').val() || '',
            ($('#drpAssetType option:selected').text() || '').trim(),
            $('#txtFromDate').val() || '',
            $('#txtFromTime').val() || '',
            $('#txtToDate').val() || '',
            $('#txtToTime').val() || ''
        ].join('|');
    }

    function _ipsRememberBucketResults(mode, assetResults) {
        if (!mode || !assetResults || !assetResults.length) { return; }
        var key = _ipsCurrentSearchKey();
        if (key !== _ipsBucketResultsKey) {
            _ipsBucketResults = {};
            _ipsBucketResultsKey = key;
        }
        _ipsBucketResults[mode] = assetResults;
    }

    function _ipsRememberedBucketResults(mode) {
        if (_ipsCurrentSearchKey() !== _ipsBucketResultsKey) { return null; }
        return _ipsBucketResults[mode] || null;
    }

    // Pivot pagination is CLIENT-SIDE: it slices the union of timestamps
    // already loaded into assetResults. It does not fetch further server
    // pages -- see the note in renderEldHistoryPivot below.
    var _pivotPage = 1;
    var _pivotPageSize = 50;

    function pivotGotoPage(n) {
        _pivotPage = (n < 1) ? 1 : n;
        if (!lastAssetResults) { return; }
        if (isEldHistoryAssetType()) { renderEldHistoryPivot(lastAssetResults); }
        else { renderIpsHistoryPivot(lastAssetResults); }
    }

    function onPivotPageSizeChange(el) {
        var v = parseInt($(el).val(), 10);
        if (isNaN(v) || v <= 0) { return; }
        _pivotPageSize = v;
        pivotGotoPage(1);
    }

    // Prev / page number / Next. Unlike the cursor-paged server tables the
    // row total here is genuinely known, so it is shown.
    function _pivotPaginationHtml(page, totalPages, totalRows, from, to) {
        var h = '<div class="px-2 pb-2 pt-1">';
        h += '<div class="d-flex justify-content-between align-items-center flex-wrap">';
        h += '<div class="d-flex align-items-center flex-wrap">';
        h += '<div class="th-pagesize" style="display:inline-flex;align-items:center;margin-right:14px;">'
            + '<label style="font-size:12px;margin:0 6px 0 0;white-space:nowrap;">Rows / page</label>'
            + '<select class="form-control form-control-sm" style="width:auto;display:inline-block;" onchange="onPivotPageSizeChange(this)">'
            + _thPageSizeOptionsHtml(_pivotPageSize)
            + '</select></div>';
        h += '<div class="showing-records">Showing ' + from + ' to ' + to + ' of ' + totalRows + ' records</div>';
        h += '</div><nav><ul class="pagination">';
        h += '<li class="page-item ' + (page <= 1 ? 'disabled' : '') + '">'
            + '<a class="page-link" href="javascript:void(0);"'
            + (page <= 1 ? '' : ' onclick="pivotGotoPage(' + (page - 1) + ')"')
            + '><i class="fas fa-chevron-left"></i> Previous</a></li>';
        h += '<li class="page-item active"><a class="page-link" '
            + 'style="cursor:default;pointer-events:none;">' + page + '</a></li>';
        h += '<li class="page-item ' + (page >= totalPages ? 'disabled' : '') + '">'
            + '<a class="page-link" href="javascript:void(0);"'
            + (page >= totalPages ? '' : ' onclick="pivotGotoPage(' + (page + 1) + ')"')
            + '>Next <i class="fas fa-chevron-right"></i></a></li>';
        h += '</ul></nav></div></div>';
        return h;
    }

    function _ipsTabAttrIds(assetId) {
        if (typeof isIpsHistoryAssetType !== 'function' || !isIpsHistoryAssetType()) { return ''; }
        if (selectedViewType === 'Graph') { return ''; }
        if (typeof ASSET_INFO_MAP === 'undefined' || typeof ipsViewMode !== 'string') { return ''; }
        var _infoList = [];
        var _idsForAttr = String(assetId).split(',');
        for (var _ai = 0; _ai < _idsForAttr.length; _ai++) {
            var _oneId = _idsForAttr[_ai].trim();
            if (!_oneId) { continue; }
            var _oneList = ASSET_INFO_MAP[_oneId] || ASSET_INFO_MAP[parseInt(_oneId, 10)] || [];
            // An asset with NO metadata cannot be scoped; a non-empty attrIds
            // filter would drop its columns from EVERY page. Fail OPEN: request
            // all columns so its live data is always returned; _ipsClassifyColumn
            // still routes each to the right tab client-side.
            if (!_oneList.length) { return ''; }
            for (var _aj = 0; _aj < _oneList.length; _aj++) { _infoList.push(_oneList[_aj]); }
        }
        var _bucketIds = [];
        for (var _k = 0; _k < _infoList.length; _k++) {
            var _it = _infoList[_k];
            if (!_it) { continue; }
            var _rt = String(_it.RoleType || '').toLowerCase().trim();
            if (ipsViewMode === 'digital') {
                if (_rt !== 'd') { continue; }
                var _dId = _it.AssetAttributeId;
                if (_dId !== undefined && _dId !== null && _dId !== '') { _bucketIds.push(_dId); }
            } else {
                if (_rt === 'd') { continue; }
                var _alias = String(_it.AliasName || _it.CanonicalName || _it.AttributeName || '').toUpperCase();
                if (ipsViewMode === 'voltage' && _alias.indexOf('VIPS') === -1) { continue; }
                if (ipsViewMode === 'current' && _alias.indexOf('IIPS') === -1) { continue; }
                if (_it.AssetAttributeId !== undefined && _it.AssetAttributeId !== null && _it.AssetAttributeId !== '') {
                    _bucketIds.push(_it.AssetAttributeId);
                }
            }
        }
        return _bucketIds.length ? _bucketIds.join(',') : '';
    }


    // Slice the pivot's rows for the current page, clamping the page if the
    // underlying data shrank (tab switch, new search with fewer rows).
    function _pivotSlice(rows) {
        var totalRows = rows.length;
        var totalPages = Math.ceil(totalRows / _pivotPageSize) || 1;
        if (_pivotPage > totalPages) { _pivotPage = totalPages; }
        if (_pivotPage < 1) { _pivotPage = 1; }
        var start = (_pivotPage - 1) * _pivotPageSize;
        var end = Math.min(start + _pivotPageSize, totalRows);
        return {
            rows: rows.slice(start, end),
            page: _pivotPage, totalPages: totalPages, totalRows: totalRows,
            from: totalRows ? start + 1 : 0, to: end
        };
    }

    // DataLogger columns name the asset and the attribute identically, so a
    // second header row would just repeat the first. Compared on alphanumerics
    // only, so punctuation/spacing differences don't force a redundant row.
    function _pivotNamesMatch(a, b) {
        var norm = function (s) { return String(s || '').toUpperCase().replace(/[^A-Z0-9]+/g, ''); };
        var x = norm(a), y = norm(b);
        return !!x && x === y;
    }

    // Canonical (Title) name is used for classification -- NOT the alias
    // shown as the column's top header -- matching the existing separation
    // in this file (Title for calculations/matching, AliasName for display).
    // AliasName carries the IIPS / VIPS tokens (AttributeName does not), so
    // classification AND the displayed header both use the alias. parseChartData
    // stamps col.aliasName; fall back only if it's genuinely absent.
    function _ipsAliasName(col) {
        if (!col) return '';
        return col.aliasName || col.displayName || col.name || '';
    }

    function _ipsCanonicalName(col) {
        if (!col) return '';
        var canonical = (typeof getHistoryCanonicalAttrName === 'function')
            ? getHistoryCanonicalAttrName(col.assetId, col.attrId, !!col.isDataLogger, col.displayName)
            : '';
        if (canonical) return canonical;
        return (typeof getAttrTitle === 'function') ? getAttrTitle(col.attrId) : (col.displayName || '');
    }

    // DataLogger takes priority: a relay attribute is structurally digital
    // regardless of name. Otherwise classify on the ALIAS name, where the
    // tokens live: "VIPS..." = voltage, "IIPS..." = current. VIPS is tested
    // first so it isn't swallowed by the IIPS check.
    // ---- BATT CHARGING / BATT DISCHARGING grouping ------------------------
    // Ported from telemetrylive.js (ipsGroupCurrentRows / _ipsGroupChargingSeries
    // / _ipsSumPointSeries). Every "BATT CHARGING n" / "BATT DISCHARGING n"
    // asset on the IPS Current tab collapses into exactly TWO summed columns:
    // one "Batt Charging" and one "Batt Discharging", each carrying the sum of
    // ALL member attributes at each timestamp. The individual attributes are
    // never shown on their own. DISCHARGING is checked first since
    // "DISCHARGING" also contains "CHARGING".
    // Accepts BOTH the spelled-out form used by the telemetry API's column names
    // ("IPS Batterry discharging Current 2") and the abbreviated form used by the
    // GetUserAssetInfo AliasName ("IIPS BATT DISCHAR-1 110 DC"). The substring
    // test could only ever see the first, so an alias never resolved a bucket.
    //
    // Word-bounded on purpose: a bare indexOf('CHAR') would swallow "Charger mA"
    // and "Charger V", which are ordinary IPS attributes and must NOT join a
    // battery group. \b still matches "CHAR-2" because "-" is a non-word char.
    // DISCHAR is tested first since CHAR is a substring of it.
    function _ipsChargingBucket(assetName) {
        var up = String(assetName || '').toUpperCase();
        if (/\bDISCHAR(GING|GE|GED)?\b/.test(up)) { return 'discharging'; }
        if (/\bCHAR(GING|GE|GED)?\b/.test(up)) { return 'charging'; }
        return null;
    }

    function _ipsChargingBaseName(assetName) {
        return String(assetName || '').replace(/[\s\-_]*\d+\s*$/, '').trim()
            || String(assetName || '');
    }

    // Remove the per-asset serial from an IPS battery ATTRIBUTE name.
    //
    //   "IIPS BATT CHAR-2 110 DC"          -> "IIPS BATT CHAR 110 DC"
    //   "IIPS BATT DISCHAR-1 110 DC"       -> "IIPS BATT DISCHAR 110 DC"
    //   "IPS Batterry charging Current 2"  -> "IPS Batterry charging Current"
    //
    // Two serial forms exist and only ONE is applied per name:
    //   BOUND   -- an index tied to a word by - or _ ("CHAR-2"). Removed
    //              wherever it appears, including mid-name.
    //   TRAILING-- a bare index at the end ("... Current 2"). Removed only when
    //              no bound form was found, so a name that already lost its
    //              "-2" cannot then lose a genuine trailing number as well.
    //
    // A free-standing number that is PART of the name -- the 110 in "110 DC" --
    // is neither bound nor trailing, so it survives. That is the whole reason
    // this strips a serial instead of diffing tokens across members: a token
    // diff would have discarded "CHAR" (the only differing token) and kept
    // "110 DC", which is backwards.
    function _ipsStripSerial(name) {
        var s = String(name || '');
        var bound = s.replace(/[-_]\s*\d+(?=\s|$)/g, '');
        if (bound !== s) { s = bound; }
        else { s = s.replace(/\s+\d+\s*$/, ''); }
        return s.replace(/\s{2,}/g, ' ').trim();
    }

    // AliasName for ONE member, read from the GetUserAssetInfo response
    // (ASSET_INFO_MAP) via the existing getAssetInfoAttrEntry lookup, keyed by
    // the member's own assetId + attribute id.
    //
    // This is the authoritative source and the parsed column is not. For an
    // analog attribute loadAssetInfoBySite already collapses AttributeName to
    // "aliasName || canonicalName", so col.displayName is a COPY of the alias
    // when one exists and silently becomes the canonical name when it does not
    // -- two different name shapes in one field. Reading AliasName directly
    // keeps every member on the same shape, which is what the common-part
    // calculation depends on.
    function _ipsMemberAlias(col) {
        if (!col) { return ''; }
        var entry = (typeof getAssetInfoAttrEntry === 'function')
            ? getAssetInfoAttrEntry(col.assetId, col.attrId, !!col.isDataLogger)
            : null;
        var alias = entry ? String(entry.AliasName || '').trim() : '';
        if (alias) { return alias; }
        // Only when the site's asset info has no entry for this attribute.
        return String(col.aliasName || col.displayName || col.apiName || '').trim();
    }

    // One attribute label for the whole Batt Charging / Batt Discharging group.
    // Members are the SAME attribute on different battery units, so once the
    // serial is gone they are identical -- bind that single name. The token
    // fallback below only runs if a site genuinely names its members
    // differently; it is a safety net, not the expected path.
    function _ipsCommonAttrName(names) {
        var cleaned = (names || [])
            .map(function (n) { return _ipsStripSerial(n); })
            .filter(function (n) { return !!n; });

        if (!cleaned.length) { return ''; }

        var i, allSame = true;
        for (i = 1; i < cleaned.length; i++) {
            if (cleaned[i].toUpperCase() !== cleaned[0].toUpperCase()) {
                allSame = false;
                break;
            }
        }
        if (allSame) { return cleaned[0]; }

        // ---- fallback: keep only the tokens EVERY member shares ----
        var lists = cleaned.map(function (n) { return n.split(/\s+/); });
        var same = function (a, b) {
            return String(a).toUpperCase() === String(b).toUpperCase();
        };

        var head = [], j;
        for (i = 0; i < lists[0].length; i++) {
            var hTok = lists[0][i], hOk = true;
            for (j = 1; j < lists.length; j++) {
                if (i >= lists[j].length || !same(lists[j][i], hTok)) { hOk = false; break; }
            }
            if (!hOk) { break; }
            head.push(hTok);
        }

        var tail = [], k, m;
        for (k = 1; k <= lists[0].length - head.length; k++) {
            var tTok = lists[0][lists[0].length - k], tOk = true;
            for (m = 1; m < lists.length; m++) {
                if (k > lists[m].length - head.length ||
                    !same(lists[m][lists[m].length - k], tTok)) { tOk = false; break; }
            }
            if (!tOk) { break; }
            tail.unshift(tTok);
        }

        return head.concat(tail).join(' ');
    }

    // Sum multiple series at EACH timestamp in the union.
    //
    // Two cases, and they are NOT the same:
    //
    //  a) The member HAS a row at this timestamp. The request is fillGaps=true,
    //     so the API has already levelled every attribute of that asset onto the
    //     row -- take the value exactly as reported. An explicit null here means
    //     genuinely no reading and contributes nothing.
    //
    //  b) The member has NO row at this timestamp. Members come from DIFFERENT
    //     assets (BATT CHARGING-1..n), each with its own timestamp grid, and
    //     fillGaps only aligns WITHIN one asset's response. The member is still
    //     drawing its last reported level, so carry it forward -- otherwise the
    //     total silently drops that member on every timestamp minted by a
    //     sibling asset.
    function _ipsSumEntrySeries(entryLists) {
        // Index each member's entries by exact timestamp (ms) for O(1) lookup.
        // Last write wins if a member somehow has two entries at the same ms.
        var maps = entryLists.map(function (entries) {
            var m = {};
            (entries || []).forEach(function (e) {
                if (e && e.ts) { m[e.ts.getTime()] = e.value; }
            });
            return m;
        });

        var tsSet = {};
        entryLists.forEach(function (entries) {
            (entries || []).forEach(function (e) {
                if (e && e.ts) { tsSet[e.ts.getTime()] = e.ts; }
            });
        });
        var allMs = Object.keys(tsSet).map(Number).sort(function (a, b) { return a - b; });
        var merged = [];
                for (var i = 0; i < allMs.length; i++) {
            var key = allMs[i];
            var ts = tsSet[key];
            var sum = 0, hasValue = false;
            for (var j = 0; j < maps.length; j++) {
                // Case (a) exact row -> as reported; case (b) no row -> LOCF.
                var v = maps[j].hasOwnProperty(key)
                    ? maps[j][key]
                    : ((typeof getValueForTimestamp === 'function')
                        ? getValueForTimestamp(entryLists[j], ts)
                        : null);
            //    var num = (v !== null && v !== undefined) ? parseFloat(v) : NaN;
            //    if (!isNaN(num)) { sum += num; hasValue = true; }
            //}
                //merged.push({ ts: ts, value: hasValue ? sum : null });

                var num = (v !== null && v !== undefined) ? parseFloat(v) : NaN;
                if (!isNaN(num)) {
                    // Pre-calculation clamp: a battery current/voltage can never
                    // physically be <= 0, so each member reading is floored to 0
                    // before it enters the sum (e.g. -4 contributes 0, not -4).
                    if (num <= 0) { num = 0; }
                    sum += num; hasValue = true;
                }
            }
            // Post-calculation clamp: never emit a negative (or -0) total.
            merged.push({ ts: ts, value: hasValue ? (sum <= 0 ? 0 : sum) : null });
        }
        return merged;
    }

    // Replaces per-asset BATT CHARGING / BATT DISCHARGING columns (IPS
    // Current tab only) in parsed.columns/parsed.attributeData with ONE
    // combined column per bucket -- charging and discharging -- summed. Mutates
    // `parsed` in place. Called once after parseChartData() -- the table
    // pivot and the graph both consume that same output, so one call here
    // covers both surfaces (mirrors the PM column-filter pattern already
    // used in renderChart).
    function _ipsGroupChargingColumns(parsed) {
        if (!parsed || !parsed.columns || !parsed.columns.length) { return; }
        var groups = {};
        var toRemove = {};

        parsed.columns.forEach(function (col) {
            if (!col || col.isDataLogger) { return; }
            if (_ipsClassifyColumn(col) !== 'current') { return; }

            // AliasName from GetUserAssetInfo is checked FIRST -- it is the
            // per-attribute source of truth and now resolves, since Patch 6
            // recognises its abbreviated CHAR / DISCHAR tokens. The remaining
            // sources are fallbacks for a site whose asset info is incomplete:
            // apiName is the telemetry response's own column name, assetName is
            // the coarsest signal (one asset holds many attributes).
            var _bucketFromAlias = _ipsChargingBucket(_ipsMemberAlias(col));
            var _bucketFromDisplay = _ipsChargingBucket(col.displayName) || _ipsChargingBucket(col.apiName);
            var _bucketFromAsset = _ipsChargingBucket(col.assetName);
            var bucket = _bucketFromAlias || _bucketFromDisplay || _bucketFromAsset;
            if (!bucket) { return; }

            /*
             * ONE group per bucket -- nothing finer.
             *
             * The key used to carry the attribute (asset-named case) or the
             * assetId (attribute-named case). With BATT CHARGING-1..n each
             * reporting more than one current attribute, that produced one
             * "BATT CHARGING" column PER ATTRIBUTE instead of a single summed
             * one. Requirement: every current attribute of every CHARGING
             * asset sums into a single "Batt Charging" column, and likewise
             * for DISCHARGING -- so the bucket alone is the key.
             */
            var groupKey = bucket;
            if (!groups[groupKey]) {
                groups[groupKey] = {
                    bucket: bucket,
                    members: []
                };
            }
            groups[groupKey].members.push(col);
            toRemove[col.compositeKey] = true;
        });

        var groupKeys = Object.keys(groups);
        if (!groupKeys.length) { return; }

        groupKeys.forEach(function (groupKey) {
            var group = groups[groupKey];
            var entryLists = group.members.map(function (c) { return parsed.attributeData[c.compositeKey] || []; });
            var merged = _ipsSumEntrySeries(entryLists);

            var newKey = 'battgrp::' + groupKey;
            var first = group.members[0];
            parsed.attributeData[newKey] = merged;

            /*
             * Resolve the unit from a REAL member column while its genuine
             * assetId is still available, and stamp it on the group.
             *
             * The group's own assetId is synthetic ("battgrp::current"), so
             * getHistoryCanonicalAttrName cannot resolve it and getAttrUnit
             * fell back to a name with no unit token ("BATT CHARGING" /
             * "IIPS BATT CHAR 110 DC") -> "V". The chart's mA filter then
             * dropped the summed current series entirely.
             */
            var _grpUnit = (typeof getAttrUnit === 'function')
                ? getAttrUnit(first)
                : 'mA';

            /*
             * Same reasoning as _grpUnit above: resolve the IPS tab bucket
             * from a REAL member while its genuine assetId and attribute
             * names are still available, and stamp it on the group.
             *
             * Needed because the group carries NO attribute name any more --
             * _ipsClassifyColumn scans aliasName / displayName for the
             * IIPS / VIPS token, so a column named only "Batt Charging" would
             * classify as null and disappear from the Current tab.
             */
            var _grpBucket = (typeof _ipsClassifyColumn === 'function')
                ? (_ipsClassifyColumn(first) || 'current')
                : 'current';

            // The column aggregates every member attribute of every member
            // asset, so no single attribute name describes it. All three name
            // fields carry the asset-level name only.
            /*
              * Same reasoning as _grpUnit above: resolve the IPS tab bucket
              * from a REAL member while its genuine assetId and attribute
              * names are still available, and stamp it on the group.
              *
              * Needed because the group carries NO attribute name any more --
              * _ipsClassifyColumn scans aliasName / displayName for the
              * IIPS / VIPS token, so a column named only "Batt Charging" would
              * classify as null and disappear from the Current tab.
              */
            var _grpBucket = (typeof _ipsClassifyColumn === 'function')
                ? (_ipsClassifyColumn(first) || 'current')
                : 'current';

            // assetName stays the bucket label (matches the dropdown item and
            // keeps _ipsClassifyColumn's _isBattGroup guard intact). The two
            // ATTRIBUTE name fields carry the part every member's attribute name
            // has in common, so the pivot sub-header names what was summed
            // instead of repeating the asset label.
            // Asset-level label straight from the member's own AssetName, so the
            // pivot header, the asset dropdown and GetUserAssetInfo all agree.
            // Falls back to the bucket label if the member carries no assetName.
            var _groupAssetName = String(first.assetName || '').trim() ||
                ((group.bucket === 'discharging') ? 'Batt Discharging' : 'Batt Charging');

            // Label = the common part of the members' AliasName values, taken
            // from GetUserAssetInfo -- not from the parsed column.
            //
            // A member whose alias declares the OTHER bucket is excluded before
            // the common part is computed. Without this guard one stray member
            // makes CHAR / DISCHAR the only differing token, the diff drops it,
            // and BOTH columns end up labelled "IIPS BATT 110 DC".
            var _groupAliases = group.members
                .map(_ipsMemberAlias)
                .filter(function (n) {
                    var b = _ipsChargingBucket(n);
                    return !b || b === group.bucket;
                });

            var _groupAttrName = _ipsCommonAttrName(_groupAliases) || _groupAssetName;

            parsed.columns.push({
                assetId: 'battgrp::' + group.bucket,
                assetName: _groupAssetName,
                attrId: first.attrId,
                compositeKey: newKey,
                isDataLogger: false,
                displayName: _groupAttrName,
                aliasName: _groupAttrName,
                isGroup: true,
                forcedUnit: _grpUnit,
                ipsBucket: _grpBucket
            });
        });

        // Drop the individual per-asset members -- the combined column
        // replaces them, matching the flat Live table which shows only the
        // aggregated BATT CHARGING / BATT DISCHARGING row.
        parsed.columns = parsed.columns.filter(function (c) { return !toRemove[c.compositeKey]; });
    }

    /*
     * Resolve which IPS tab an asset belongs to WITHOUT needing any data,
     * using its declared attributes in ASSET_INFO_MAP. Same rules as
     * _ipsClassifyColumn: DataLogger role -> digital, else the AliasName
     * token VIPS -> voltage / IIPS -> current.
     *
     * Returns { voltage:bool, current:bool, digital:bool } because one asset
     * can legitimately declare attributes in more than one bucket.
     */
    function _pivotAssetBuckets(assetId) {
        var out = { voltage: false, current: false, digital: false };
        var list = (typeof ASSET_INFO_MAP !== 'undefined')
            ? (ASSET_INFO_MAP[String(assetId)] || ASSET_INFO_MAP[assetId])
            : null;
        if (!list || !list.length) { return out; }

        for (var i = 0; i < list.length; i++) {
            var it = list[i];
            if (!it) { continue; }
            var rt = String(it.RoleType || '').toLowerCase().trim();
            if (rt === 'd') { out.digital = true; continue; }
            var alias = String(it.AliasName || it.CanonicalName || it.AttributeName || '').toUpperCase();
            if (alias.indexOf('VIPS') > -1) { out.voltage = true; }
            else if (alias.indexOf('IIPS') > -1) { out.current = true; }
        }

        if (!out.voltage && !out.current) {
            var _names = [];
            if (typeof assetNameMap !== 'undefined') {
                var _an = String(assetNameMap[String(assetId)] || assetNameMap[assetId] || '').toUpperCase();
                if (_an) { _names.push(_an); }
            }
            for (var j = 0; j < (list || []).length; j++) {
                var _it2 = list[j];
                if (!_it2) { continue; }
                var _a2 = String(_it2.AliasName || _it2.CanonicalName || _it2.AttributeName || '').toUpperCase();
                if (_a2) { _names.push(_a2); }
            }
            for (var k = 0; k < _names.length; k++) {
                var _n = _names[k];
                if (/\bMA\b/.test(_n) || /\bAMP(S|ERE|ERES)?\b/.test(_n) || /\bCURRENT\b/.test(_n)) {
                    out.current = true;
                }
                if (/\bVDC\b/.test(_n) || /\bVOLT(S|AGE)?\b/.test(_n)) {
                    out.voltage = true;
                }
            }
        }

        return out;
    }
    /*
     * Selected assets that returned NO data contribute no column, so they
     * silently disappear from the pivot. Add a placeholder column for each
     * missing asset so every selected asset is represented; its cells resolve
     * to "-" because no attributeData entry exists for the synthetic key.
     *
     * classifyAs: the tab currently being rendered ('voltage' | 'current' |
     * 'digital'). A placeholder is emitted ONLY when the asset's OWN declared
     * attributes belong to that tab -- previously the active tab was stamped
     * onto every data-less asset, so e.g. "ABS-FAN 24 V" (AliasName
     * "VIPS DC BLOCK TEL UP", a voltage asset) also appeared under Current and
     * Digital.
     */
    function _pivotAddMissingAssetColumns(parsed, classifyAs) {
        if (!parsed || !Array.isArray(parsed.columns)) { return; }

        var present = {};
        parsed.columns.forEach(function (c) {
            if (c && c.assetId !== undefined && c.assetId !== null) {
                present[String(c.assetId)] = true;
            }
        });

        // Selected assets come from the same dropdown the search reads.
        $('#listAsset .cb-asset:checked').each(function () {
            var aid = String($(this).val() || '');
            if (!aid || present[aid]) { return; }

            var buckets = _pivotAssetBuckets(aid);

            /*
             * ELD ('digital' with no IPS tabs) keeps the old behaviour: every
             * selected asset gets a column. For IPS, only place the asset in
             * the tab its own metadata says it belongs to. If metadata is
             * unavailable (empty ASSET_INFO_MAP), fall back to showing it so
             * the asset is never silently lost.
             */
            var hasAnyBucket = buckets.voltage || buckets.current || buckets.digital;
            if (hasAnyBucket && classifyAs && !buckets[classifyAs]) { return; }

            var aName = (typeof assetNameMap !== 'undefined' && assetNameMap[aid])
                ? assetNameMap[aid]
                : ('Asset ' + aid);

            parsed.columns.push({
                assetId: aid,
                assetName: aName,
                attrId: null,
                // Synthetic key with no attributeData entry -> every cell "-".
                compositeKey: 'nodata::' + aid,
                isDataLogger: (classifyAs === 'digital'),
                displayName: aName,
                aliasName: aName,
                isPlaceholder: true,
                placeholderBucket: classifyAs || null
            });
            present[aid] = true;
        });
    }

    function _ipsClassifyColumn(col) {
        if (!col) return null;

        // HIGHEST PRIORITY: the ASSET NAME is the most authoritative signal of
        // which tab a column belongs to. If it explicitly says Voltage / Current
        // (e.g. "IPS Input Voltage"), honour that FIRST -- before the placeholder
        // bucket, the group ipsBucket, or any VIPS/IIPS attribute alias. This is
        // why "IPS Input Voltage" was leaking into the Current tab: it reached
        // this function either as a placeholder stamped with the active tab, or
        // as a real column whose attribute alias carried an IIPS/current token,
        // and neither path ever looked at the asset name's own "Voltage" word.
        // Current is tested before Voltage so a name like "...CURRENT..." never
        // mis-matches a trailing V. Battery-group sum columns ("Batt Charging" /
        // "Batt Discharging") carry no Voltage/Current word here, so they fall
        // through to the ipsBucket branch below unchanged.
        var _nm = String(col.assetName || col.displayName || '').toUpperCase();
        // Skip the summed battery groups -- "Batt Charging" / "Batt Discharging"
        // are current-tab columns and carry no Voltage/Current word anyway, but
        // guard explicitly so a future rename can't misroute them here.
        var _isBattGroup = _nm.indexOf('CHARGING') > -1 || _nm.indexOf('DISCHARGING') > -1;
        if (_nm && !_isBattGroup) {
            if (/\bMA\b/.test(_nm) || /\bAMP(S|ERE|ERES)?\b/.test(_nm) ||
                /\bCURRENT\b/.test(_nm)) {
                return 'current';
            }
            if (/\bVDC\b/.test(_nm) || /\bVOLT(S|AGE)?\b/.test(_nm)) {
                return 'voltage';
            }
        }

        // Placeholders carry their bucket explicitly so an empty asset still
        // shows up under the tab the user is looking at.
        if (col && col.isPlaceholder) {
            return col.placeholderBucket || null;
        }
        // Summed BATT CHARGING / DISCHARGING groups likewise: they carry no
        // attribute name to scan, so the bucket resolved from a real member
        // at group-build time is authoritative.
        if (col && col.ipsBucket) {
            return col.ipsBucket;
        }
        if (col.isDataLogger) return 'digital';
        var names = _ipsClassifyNames(col);
        var i, n;

        // OVERRIDE: the ASSET NAME is the most authoritative signal of intent.
        // If it explicitly says Voltage / Current, honour that BEFORE the
        // VIPS/IIPS scan below -- otherwise an asset named "IPS Input Voltage"
        // whose attributes happen to carry an IIPS alias would be pulled into
        // the Current tab by the PRIMARY loop (which scans every name candidate
        // and returns on the first VIPS/IIPS hit, regardless of the asset name).
        var _assetNm = String(col.assetName || '').toUpperCase();
        if (_assetNm) {
            // Current tested before Voltage so "... CURRENT ..." never
            // mis-reads a trailing "V". mA / AMP / CURRENT => current;
            // VOLT / VOLTAGE / VDC => voltage.
            if (/\bMA\b/.test(_assetNm) || /\bAMP(S|ERE|ERES)?\b/.test(_assetNm) ||
                /\bCURRENT\b/.test(_assetNm)) {
                return 'current';
            }
            if (/\bVDC\b/.test(_assetNm) || /\bVOLT(S|AGE)?\b/.test(_assetNm)) {
                return 'voltage';
            }
        }

        // PRIMARY: the VIPS / IIPS token. VIPS is tested before IIPS on each
        // candidate so it isn't swallowed by the IIPS check.
        for (i = 0; i < names.length; i++) {
            n = names[i];
            if (n.indexOf('VIPS') > -1) { return 'voltage'; }  // "VIPS BATT..." = voltage
            if (n.indexOf('IIPS') > -1) { return 'current'; }  // "IIPS BATT..." = current
        }

        /*
         * SECONDARY: explicit unit token. Reached only when no name carried a
         * VIPS/IIPS prefix -- previously those columns returned null and were
         * omitted from all three tabs. Canonical IPS names such as "If mA" or
         * "Charger V" classify correctly here, so the column lands on the tab
         * that matches its unit instead of disappearing.
         *
         * mA is tested before V so "BATT mA V-BUS" still reads as current.
         */
        for (i = 0; i < names.length; i++) {
            n = names[i];
            if (/\(\s*MA\s*\)/.test(n) || /\bMA\b/.test(n) || /\bMV\b/.test(n) ||
                /\bAMP(S|ERE|ERES)?\b/.test(n) || /\bCURRENT\b/.test(n)) {
                return 'current';
            }
            if (/\(\s*V\s*\)/.test(n) || /\bV\b/.test(n) || /\bVDC\b/.test(n) ||
                /\bVOLT(S|AGE)?\b/.test(n)) {
                return 'voltage';
            }
        }

        return null; // genuinely not an IPS attribute -- omitted from every tab
    }

    /*
     * Every name we can reach for a column, upper-cased, most authoritative
     * first.
     *
     * AliasName is where the VIPS / IIPS tokens live, but it is NOT always
     * populated. parseChartData only stamps col.aliasName when
     * GetUserAssetInfo resolved that attribute; when it did not, aliasName is
     * '' and displayName falls back to getAttrDisplayName(), which can return
     * the canonical Title with no VIPS/IIPS prefix. _ipsClassifyColumn then
     * returned null and the column was hidden from EVERY tab -- which is why
     * IIPS current attributes went missing from the Current tab in the graph.
     */
    function _ipsClassifyNames(col) {
        var names = [];
        var push = function (v) {
            var s = String(v == null ? '' : v).toUpperCase().trim();
            if (s) { names.push(s); }
        };

        push(col.assetName);
        push(col.aliasName);
        push(col.displayName);
        push(col.name);

        var entry = (typeof getAssetInfoAttrEntry === 'function')
            ? getAssetInfoAttrEntry(col.assetId, col.attrId, !!col.isDataLogger)
            : null;
        if (entry) {
            push(entry.AliasName);
            push(entry.AttributeName);
            push(entry.CanonicalName);
        }

        if (col.attrId !== null && col.attrId !== undefined) {
            if (typeof getAttrAlias === 'function') { push(getAttrAlias(col.attrId)); }
            if (typeof getAttrTitle === 'function') { push(getAttrTitle(col.attrId)); }
        }

        return names;
    }

    function _pivotEsc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    // ---- ELD: Date & Time + one column per asset's DataLogger relay
    // attribute. Pickup (green, .dl-pickup) / Drop (red, .dl-drop) --
    // reuses the exact classes already used for DataLogger cells elsewhere
    // in this file, so no new CSS is introduced.
    function renderEldHistoryPivot(assetResults) {
        // Server-side pagination: keyed by the result's assetId (comma list OR
        // single asset -- walk is off, so ELD paginates server-side like IPS).
        var _eldSrvKey = null;
        if (Array.isArray(assetResults) && typeof _dashServerPagerState !== 'undefined') {
            for (var _esk = 0; _esk < assetResults.length; _esk++) {
                var _ecand = assetResults[_esk] ? String(assetResults[_esk].assetId) : '';
                if (_ecand && _dashServerPagerState[_ecand]) { _eldSrvKey = _ecand; break; }
            }
        }
        var _eldSrvSt = _eldSrvKey ? _dashServerPagerState[_eldSrvKey] : null;

        var parsed = parseChartData(assetResults);
        // ELD is a single flat table -- DataLogger bucket.
        _pivotAddMissingAssetColumns(parsed, 'digital');
        /*
         * "No readings in this window" is NOT "nothing to show". The relay
         * columns are already known from ASSET_INFO_MAP (the call above), so
         * fall through and render the table with its headers and an empty
         * body: the user keeps the column layout and can see WHICH relays
         * were searched. The bare-message state below still covers the only
         * case with genuinely nothing to draw -- no relay columns at all.
         */

        var seenAsset = {};
        var cols = [];
        parsed.columns.forEach(function (col) {
            if (!col.isDataLogger) { return; }
            if (seenAsset[col.assetId]) { return; } // first DL attribute wins per asset
            seenAsset[col.assetId] = true;
            cols.push(col);
        });

        if (!cols.length) {
            $('#divTelemetryHistory').html(
                '<div class="text-center text-muted p-5"><i class="fas fa-inbox fa-3x mb-3"></i><br/>No ELD relay data found for the selected assets.</div>');
            $('#divPagination').hide();
            return;
        }

                // Newest first, then take the current client-side page. NOTE: this
        // pages only what a search already loaded (one server page per
        // asset) -- it does not walk further cursors.
        //
        // ELD-only same-second dedup:
        // When "To Date" is set to the current day, the API returns a LAST
        // edge record whose TimestampLocal is clamped to the request end
        // time. That clamped ms usually differs from the preceding real
        // TimestampDevice by only a few milliseconds, so both survive the
        // getTime()-keyed dedup in parseChartData. Since the row formatter
        // prints down to seconds only, the user sees TWO pivot rows with
        // the same visible timestamp. Collapse to one row per second here
        // (keeping the latest ms within each second, so no state change is
        // lost) before paging.
                var _eldRowTimestamps = (function () {
            // Value-aware same-second dedup:
            //   - Group every timestamp by wall-clock second.
            //   - If every timestamp in a group has the same value across
            //     every column, keep only the newest ms (legacy behaviour --
            //     this quietly drops the synthetic window-end edge whose
            //     values match the preceding real row).
            //   - If any column's value changed within the second, keep
            //     every distinct-value timestamp so a fast state transition
            //     (e.g. 16:45:07.100 Drop -> 16:45:07.900 Pickup) is
            //     preserved. The render loop below prints millisecond
            //     precision for these rows so the user can see the gap.
            function _rowSig(ts) {
                var parts = [];
                for (var c = 0; c < cols.length; c++) {
                    var v = getValueForTimestamp(
                        parsed.attributeData[cols[c].compositeKey], ts);
                    parts.push(v === null || v === undefined ? '' : String(v));
                }
                return parts.join('|');
            }
            var groups = {};
            parsed.timestamps.forEach(function (ts) {
                if (!ts) return;
                var ms = ts.getTime();
                if (isNaN(ms)) return;
                var sec = Math.floor(ms / 1000);
                if (!groups[sec]) { groups[sec] = []; }
                groups[sec].push(ts);
            });
            var out = [];
            Object.keys(groups).forEach(function (k) {
                var g = groups[k];
                if (g.length <= 1) { out.push(g[0]); return; }
                g.sort(function (a, b) { return a.getTime() - b.getTime(); });
                var firstSig = _rowSig(g[0]);
                var allSame = true;
                for (var i = 1; i < g.length; i++) {
                    if (_rowSig(g[i]) !== firstSig) { allSame = false; break; }
                }
                if (allSame) { out.push(g[g.length - 1]); return; }
                out.push(g[0]);
                var lastSig = firstSig;
                for (var j = 1; j < g.length; j++) {
                    var s = _rowSig(g[j]);
                    if (s !== lastSig) { out.push(g[j]); lastSig = s; }
                }
            });
            out.sort(function (a, b) { return a.getTime() - b.getTime(); });
            return out;
        })();

        var slice;
        if (_eldSrvSt) {
            var _eSrvRows = _eldRowTimestamps.slice().reverse();
            slice = {
                rows: _eSrvRows,
                page: _eldSrvSt.currentPage || 1,
                totalPages: _eldSrvSt.totalPages || 1,
                totalRows: (_eldSrvSt.totalRecords > 0) ? _eldSrvSt.totalRecords : _eSrvRows.length,
                from: 0, to: 0
            };
        } else {
            slice = _pivotSlice(_eldRowTimestamps.slice().reverse());
        }

        var h = '<div class="history-table-wrapper"><table class="history-table" style="width:auto;min-width:100%;"><thead><tr>';
        h += '<th style="min-width:160px;">Date &amp; Time</th>';
        cols.forEach(function (col) {
            h += '<th>' + _pivotEsc(col.assetName || col.displayName) + '</th>';
        });
        h += '</tr></thead><tbody>';

        // Blank table rather than a blank panel -- headers stay, one honest row.
        if (!slice.rows.length) {
            h += '<tr><td colspan="' + (cols.length + 1) +
                '" class="text-center text-muted" style="padding:24px;">' +
                'No history data found for the selected period.</td></tr>';
        }

                        slice.rows.forEach(function (ts) {
            h += '<tr><td>' + formatTimestampWithBadge(ts) + '</td>';
            cols.forEach(function (col) {
                var raw = getValueForTimestamp(parsed.attributeData[col.compositeKey], ts);
                var num = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
                if (isNaN(num)) { h += '<td>-</td>'; }
                else if (num === 1) { h += '<td class="dl-pickup">Pickup</td>'; }
                else { h += '<td class="dl-drop">Drop</td>'; }
            });
            h += '</tr>';
        });
        h += '</tbody></table></div>';
        if (!_eldSrvSt) {
            h += _pivotPaginationHtml(slice.page, slice.totalPages, slice.totalRows, slice.from, slice.to);
        }

        $('#divTelemetryHistory').html(h);

        if (_eldSrvSt) {
            _dashOverrideLegacyPagerHtml(_eldSrvSt);
            var _eRange = _dashShowingRange(_eldSrvSt);
            var _eFrom = _eRange.from;
            var _eTo = _eRange.to;
            $('#showingRecords').text(_eldSrvSt.totalRecords
                ? ('Showing ' + _eFrom + ' to ' + _eTo + ' of ' + _eldSrvSt.totalRecords + ' records')
                : ('Showing ' + _eFrom + ' to ' + _eTo + (_eldSrvSt.hasNextPage ? ' (more available)' : ' records')));
        } else {
            $('#divPagination').hide();
            $('#dataInfo').html('<span class="text-muted" style="font-size:12px;"><i class="fas fa-table text-primary"></i> ' +
                cols.length + ' asset(s) &middot; ' + slice.totalRows + ' row(s)</span>');
        }
    }

    // ---- IPS: same pivot, split into Voltage / Current / Digital tabs.
    // Each column carries a 2-row header: display name (top), canonical
    // classification name (bottom) -- matching the reference layout.
    // Reuses the .pm-toggle-btn styling already defined for the Point
    // Machine tab bar, so no new CSS is introduced here either.
    /*
      * The Voltage / Current / Digital tab bar, shared by BOTH the pivot table
      * and the graph.
      *
      * The graph filters its columns by `ipsViewMode` but used to render no tab
      * bar of its own, so the only way to change the mode was the table's
      * toggle. A user who opened Graph view directly was pinned to the default
      * 'voltage' bucket, and every IIPS current attribute was filtered out with
      * no visible control to get it back.
      *
      * `domId` differs per surface so the table's and the graph's bars never
      * collide on a duplicate element id.
      */
    function _ipsViewTabsHtml(domId) {
        return '<div id="' + (domId || 'ipsViewToggle') + '" ' +
            'style="display:flex;align-items:center;gap:4px;background:#f1f5f9;' +
            'border:1px solid #e2e8f0;border-radius:8px;padding:3px;margin-bottom:10px;width:max-content;">' +
            ['voltage', 'current', 'digital'].map(function (mode) {
                var label = mode === 'voltage' ? 'IPS Voltage'
                    : mode === 'current' ? 'IPS Current' : 'IPS Digital';
                var active = (mode === ipsViewMode);
                return '<button type="button" class="pm-toggle-btn' + (active ? ' active' : '') +
                    '" onclick="setIpsView(\'' + mode + '\')">' + label + '</button>';
            }).join('') +
            '</div>';
    }

    //function setIpsView(mode) {
    //    ipsViewMode = mode;
    //    _pivotPage = 1; // each tab has its own row set -- start at the top
    //    if (!lastAssetResults) { return; }

    //    /*
    //     * Re-render the surface the user is actually looking at. Previously
    //     * this always called the table renderer, so clicking a tab from the
    //     * graph updated `ipsViewMode` but left the chart drawn from the old
    //     * bucket.
    //     *
    //     * skipFullRangeLoad = true: lastAssetResults already holds the full
    //     * graph dataset, so switching tabs must not trigger another fetch.
    //     */
    //    // The graph ignores ipsViewMode entirely (it filters by unit chips),
    //    // so the tab bar exists only on the table and this only ever needs to
    //    // re-render the pivot.
    //    renderIpsHistoryPivot(lastAssetResults);
    //}


    function setIpsView(mode) {
        var _prevMode = ipsViewMode;
        ipsViewMode = mode;
        _pivotPage = 1; // each tab has its own row set -- start at the top

        /*
         * Mirror setPmView's tab-change pattern: on a REAL bucket switch,
         * clear the current view and refire the search so the API is called
         * scoped to just this tab's attributes. attrIds is built from
         * ASSET_INFO_MAP inside fnSearchHistory (see the IPS branch on the
         * $.ajax `data` block below in this file). Without this, tab
         * switching only re-rendered the cached full-range payload and no
         * API request fired for the tab's attribute set -- which is why an
         * "empty" bucket showed timestamps sourced from the OTHER two.
         *
         * Graph view has its own unit-chip filter and does not read
         * ipsViewMode, so a Graph-side click just updates the state and
         * lets the chart's own filter handle it. Do NOT refetch there --
         * a re-fetch under Graph would drop the full-range dataset the
         * chart's other chips (mA / V / 0-1) still need.
         */
        if (
            _prevMode !== mode &&
            selectedViewType !== 'Graph' &&
            typeof isIpsHistoryAssetType === 'function' &&
            isIpsHistoryAssetType() &&
            typeof fnSearchHistory === 'function'
        ) {
            clearHistoryView();
            fnSearchHistory();
            return;
        }

        if (!lastAssetResults) { return; }
        renderIpsHistoryPivot(lastAssetResults);
    }

    // IPS pivot only. getValueForTimestamp returns an EXACT-match value even
    // when that value is null, which short-circuits its own carry-forward.
    // The summed Batt Charging / Discharging series legitimately holds nulls
    // (no member reported a numeric at that instant), so the row was dropped
    // from _visibleTs and the cell printed "-". Here a null is treated as
    // "not present" and the last changed value is carried forward instead.
    function _ipsLastKnownValue(entries, targetTs) {
        if (!entries || !entries.length) { return null; }
        var targetTime = targetTs.getTime();
        var best = null;
        for (var i = 0; i < entries.length; i++) {
            var t = entries[i].ts.getTime();
            if (t > targetTime) { break; }
            var v = entries[i].value;
            if (v !== null && v !== undefined && v !== '') { best = v; }
        }
        return best;
    }

    function renderIpsHistoryPivot(assetResults) {
        // Server-side pagination: keyed by the result's assetId (comma list OR
        // single asset -- walk is off, so single-asset paginates server-side too).
        var _ipsSrvKey = null;
        if (Array.isArray(assetResults) && typeof _dashServerPagerState !== 'undefined') {
            for (var _sk = 0; _sk < assetResults.length; _sk++) {
                var _cand = assetResults[_sk] ? String(assetResults[_sk].assetId) : '';
                if (_cand && _dashServerPagerState[_cand]) { _ipsSrvKey = _cand; break; }
            }
        }
        var _ipsSrvSt = _ipsSrvKey ? _dashServerPagerState[_ipsSrvKey] : null;

        // Keep this tab's payload so the download can emit a sheet per tab.
        _ipsRememberBucketResults(ipsViewMode, assetResults);
        var parsed = parseChartData(assetResults);
        // Every selected asset gets a column for the CURRENT tab, whether or
        // not it returned data.
        _pivotAddMissingAssetColumns(parsed, ipsViewMode);
        _ipsGroupChargingColumns(parsed);

        var cols = [];
        parsed.columns.forEach(function (col) {
            if (_ipsClassifyColumn(col) === ipsViewMode) { cols.push(col); }
        });

        var tabHtml = _ipsViewTabsHtml('ipsViewToggle');

        /*
         * Bail ONLY when there are no columns at all -- that is the single
         * case with nothing to draw.
         *
         * This used to bail on !parsed.timestamps.length too, replacing the
         * seven real relay headers with a two-column stub ("Date & Time |
         * IPS Digital"). No readings in the window is not the same as nothing
         * to show: the columns are already resolved above (real ones plus the
         * placeholders _pivotAddMissingAssetColumns minted), so the table
         * renders with its proper headers and an empty body instead -- the
         * user keeps the layout and can see WHICH assets were searched.
         */
        if (!cols.length) {
            // Keep the tab bar visible AND render a real table skeleton with
            // a "No data" row -- so the user can switch to another tab
            // (voltage / current / digital) even when the current tab has
            // no data. The tab click handler (setIpsView) still fires
            // normally and will re-search for the newly selected tab.
            var _emptyHeader = ipsViewMode === 'voltage' ? 'IPS Voltage'
                : ipsViewMode === 'current' ? 'IPS Current' : 'IPS Digital';
            $('#divTelemetryHistory').html(
                tabHtml +
                '<div class="history-table-wrapper">' +
                '<table class="history-table" style="width:auto;min-width:100%;">' +
                '<thead><tr>' +
                '<th style="min-width:160px;vertical-align:middle;">Date &amp; Time</th>' +
                '<th>' + _emptyHeader + '</th>' +
                '</tr></thead>' +
                '<tbody><tr>' +
                '<td colspan="2" class="text-center text-muted" style="padding:40px 20px;">' +
                '<i class="fas fa-inbox fa-2x" style="display:block;margin-bottom:8px;"></i>' +
                'No ' + ipsViewMode + ' data found for the selected assets.' +
                '</td>' +
                '</tr></tbody>' +
                '</table></div>'
            );
            $('#divPagination').hide();
            $('#dataInfo').html('<span class="text-muted" style="font-size:12px;">' +
                '<i class="fas fa-table text-primary"></i> 0 asset(s) &middot; 0 row(s)</span>');
            return;
        }

        // Second header row carries the canonical attribute name. On the
        // Digital tab (and anywhere else the two names are the same string)
        // it would just repeat the first row, so it is dropped entirely.
        var showSubHeader = false;
        for (var ci = 0; ci < cols.length; ci++) {
            if (!_pivotNamesMatch(cols[ci].assetName || cols[ci].displayName, _ipsAliasName(cols[ci]))) {
                showSubHeader = true;
                break;
            }
        }

        // Build rows only for timestamps where at least ONE visible column of
        // this tab has a real (non-null) value. Otherwise timestamps carried in
        // by an asset that belongs to a DIFFERENT tab (e.g. "IPS Input Voltage"
        // on the Current tab) would mint all-dash rows, and rows where Batt
        // Charging / Discharging actually changed would sit among empty ones.
        // A row survives whenever Charging OR Discharging (or any shown column)
        // has a value at that timestamp -- which is exactly "show the timestamp
        // when charging/discharging is showing".
        var _visibleTs = parsed.timestamps.filter(function (ts) {
            for (var _ci = 0; _ci < cols.length; _ci++) {
                var _raw = _ipsLastKnownValue(parsed.attributeData[cols[_ci].compositeKey], ts);
                if (_raw !== null && _raw !== undefined && _raw !== '' &&
                    !isNaN(parseFloat(_raw))) {
                    return true;
                }
            }
            return false;
        });

        var slice;
        if (_ipsSrvSt) {
            var _srvRows = _visibleTs.slice().reverse();
            slice = {
                rows: _srvRows,
                page: _ipsSrvSt.currentPage || 1,
                totalPages: _ipsSrvSt.totalPages || 1,
                totalRows: (_ipsSrvSt.totalRecords > 0) ? _ipsSrvSt.totalRecords : _srvRows.length,
                from: 0, to: 0
            };
        } else {
            slice = _pivotSlice(_visibleTs.slice().reverse());
        }

        var h = tabHtml + '<div class="history-table-wrapper"><table class="history-table" style="width:auto;min-width:100%;"><thead>';
        h += '<tr><th' + (showSubHeader ? ' rowspan="2"' : '') + ' style="min-width:160px;vertical-align:middle;">Date &amp; Time</th>';
        cols.forEach(function (col) {
            h += '<th>' + _pivotEsc(col.assetName || col.displayName) + '</th>';
        });
        h += '</tr>';
        if (showSubHeader) {
            h += '<tr>';
            cols.forEach(function (col) {
                h += '<th style="font-size:11px;font-weight:500;opacity:.85;">' + _pivotEsc(_ipsAliasName(col)) + '</th>';
            });
            h += '</tr>';
        }
        h += '</thead><tbody>';

        // Real table, empty body -- headers stay, one row explains why.
        if (!slice.rows.length) {
            h += '<tr><td colspan="' + (cols.length + 1) +
                '" class="text-center text-muted" style="padding:40px 20px;">' +
                '<i class="fas fa-inbox fa-2x" style="display:block;margin-bottom:8px;"></i>' +
                'No ' + ipsViewMode + ' data found for the selected period.' +
                '</td></tr>';
        }

        slice.rows.forEach(function (ts) {
            h += '<tr><td>' + formatTimestampWithBadge(ts) + '</td>';
            cols.forEach(function (col) {
                var raw = _ipsLastKnownValue(parsed.attributeData[col.compositeKey], ts);
                if (ipsViewMode === 'digital') {
                    var num = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
                    if (isNaN(num)) { h += '<td>-</td>'; }
                    else if (num === 1) { h += '<td class="dl-pickup">Pickup</td>'; }
                    else { h += '<td class="dl-drop">Drop</td>'; }
                                } else {
                    var v = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
                    // Battery voltage/current can never physically be <= 0; a negative
                    // reading is a sensor artefact. Clamp to 0 so the pivot never
                    // shows an impossible value. Digital and ELD are on different
                    // code paths and are not affected.
                    if (!isNaN(v) && v <= 0) { v = 0; }
                    h += '<td>' + (isNaN(v) ? '-' : v.toFixed(2)) + '</td>';
                }
            });
            h += '</tr>';
        });
        h += '</tbody></table></div>';
        // Server-paged union uses the SAME pagination UI as every other table
        // (#divPagination / #paginationUl, rows-per-page selector, "Showing X to
        // Y of Z rows"), so NO inline pager is appended here. _pivotPaginationHtml
        // stays only for the non-server fallback (walk off => normally unused).
        if (!_ipsSrvSt) {
            h += _pivotPaginationHtml(slice.page, slice.totalPages, slice.totalRows, slice.from, slice.to);
        }

        $('#divTelemetryHistory').html(h);

        if (_ipsSrvSt) {
            // Standard shared pager -- Previous / current page / Next, the
            // rows-per-page selector and the "Showing X to Y of Z rows" line,
            // byte-for-byte the same control the PM / Track tables use. Its
            // buttons call renderTablePage(n), which is wrapped to route through
            // _dashLoadServerPage(csvKey, n) (csvKey resolved from
            // lastAssetResults[0], the single union entry).
            _dashOverrideLegacyPagerHtml(_ipsSrvSt);
            // _dashOverrideLegacyPagerHtml drives #paginationUl + #dataInfo, but
            // the #showingRecords line inside the pager bar is normally filled by
            // the legacy single-table renderer, which does not run in the pivot
            // flow -- so set it here with the same math for a matching bar.
            var _srvRange = _dashShowingRange(_ipsSrvSt);
            var _srvFrom = _srvRange.from;
            var _srvTo = _srvRange.to;
            $('#showingRecords').text(_ipsSrvSt.totalRecords
                ? ('Showing ' + _srvFrom + ' to ' + _srvTo + ' of ' + _ipsSrvSt.totalRecords + ' records')
                : ('Showing ' + _srvFrom + ' to ' + _srvTo + (_ipsSrvSt.hasNextPage ? ' (more available)' : ' records')));
        } else {
            $('#divPagination').hide();
            $('#dataInfo').html('<span class="text-muted" style="font-size:12px;"><i class="fas fa-table text-primary"></i> ' +
                cols.length + ' asset(s) &middot; ' + slice.totalRows + ' row(s)</span>');
        }
    }

    /*
     * IPS / ELD export -- MUST match the pivoted table on screen (row =
     * timestamp, column = asset), not the generic one-worksheet-per-asset
     * layout. Rebuilds the exact same column set and cell values the
     * renderers use, so the file mirrors what the user sees.
     *
     * Returns { headers: [...], rows: [[...]] }.
     */
    /*
     * Build ONE grid for a single bucket. `bucket` is 'voltage' | 'current' |
     * 'digital' for IPS, or null for ELD (single flat DataLogger table).
     *
     * parseChartData is run per bucket because _pivotAddMissingAssetColumns
     * and _ipsGroupChargingColumns MUTATE the parsed object -- sharing one
     * parse across buckets would leak placeholder and grouped columns between
     * tabs.
     */
    function _pivotBuildOneGrid(assetResults, bucket) {
        var isEld = (bucket === null);
        var parsed = parseChartData(assetResults);

        _pivotAddMissingAssetColumns(parsed, isEld ? 'digital' : bucket);
        if (!isEld) { _ipsGroupChargingColumns(parsed); }

        var cols = [];
        if (isEld) {
            var seenAsset = {};
            parsed.columns.forEach(function (col) {
                if (!col.isDataLogger) { return; }
                if (seenAsset[col.assetId]) { return; }
                seenAsset[col.assetId] = true;
                cols.push(col);
            });
        } else {
            parsed.columns.forEach(function (col) {
                if (_ipsClassifyColumn(col) === bucket) { cols.push(col); }
            });
        }

        var headers = ['Date & Time'];
        cols.forEach(function (col) {
            var name = col.assetName || col.displayName || '';
            // IPS shows the alias underneath the asset name on screen; fold it
            // into one header cell so a flat sheet keeps the same information.
            if (!isEld) {
                var alias = _ipsAliasName(col);
                if (alias && !_pivotNamesMatch(name, alias)) { name = name + ' (' + alias + ')'; }
            }
            headers.push(name);
        });

        var isDigital = isEld || bucket === 'digital';

        /*
         * ROW AXIS = only the timestamps THIS bucket actually reported at.
         *
         * parsed.timestamps is the union across every attribute in the
         * payload. On screen that is harmless: the table's payload was
         * narrowed by attrIds to one bucket, so the union IS the bucket's own
         * axis. The export's all-tabs prefetch uses GetHistoryData, which has
         * no attrIds parameter and deliberately returns EVERY attribute so one
         * call can serve all three sheets -- so the Current grid inherited
         * voltage and digital timestamps too, and carry-forward filled a value
         * at each one. 61 real readings rendered as 8403 repeated rows.
         *
         * Filtering to each column's OWN entry timestamps reproduces the
         * on-screen axis exactly, and is a no-op for an already-filtered
         * payload (every union timestamp comes from a bucket attribute).
         * Summed Batt Charging / Discharging groups are covered: their merged
         * series carries the union of its members' timestamps.
         */
        var _bucketTsSet = {};
        cols.forEach(function (col) {
            var _entries = parsed.attributeData[col.compositeKey];
            if (!_entries || !_entries.length) { return; }
            for (var _ei = 0; _ei < _entries.length; _ei++) {
                if (_entries[_ei] && _entries[_ei].ts) {
                    _bucketTsSet[_entries[_ei].ts.getTime()] = true;
                }
            }
        });
        var _gridTimestamps = parsed.timestamps.filter(function (ts) {
            return !!_bucketTsSet[ts.getTime()];
        });

        var rows = [];
        // Newest-first, same order as the table.
        _gridTimestamps.slice().reverse().forEach(function (ts) {
            var row = [formatTimestamp(ts)];
            //cols.forEach(function (col) {
            //    var raw = getValueForTimestamp(parsed.attributeData[col.compositeKey], ts);
            //    if (isDigital) {
            //        var num = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
            //        row.push(isNaN(num) ? '-' : (num === 1 ? 'Pickup' : 'Drop'));
            //    } else {
            //        var v = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
            //        row.push(isNaN(v) ? '-' : v.toFixed(2));
            //    }
            //});
            //rows.push(row);

            /*
             * A timestamp is only worth a report row when at least one asset
             * actually reported in this bucket. Without this the DataLogger /
             * Digital sheet filled up with page after page of timestamps whose
             * every cell was '-', because the timestamp axis is shared across
             * all attributes of the loaded payload.
             */
            var hasValue = false;
            cols.forEach(function (col) {
                var raw = _ipsLastKnownValue(parsed.attributeData[col.compositeKey], ts);
                if (isDigital) {
                    var num = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
                    if (!isNaN(num)) { hasValue = true; }
                    row.push(isNaN(num) ? '-' : (num === 1 ? 'Pickup' : 'Drop'));
                                } else {
                    var v = (raw !== null && raw !== undefined) ? parseFloat(raw) : NaN;
                    // Mirrors the pivot's on-screen clamp so the exported sheet
                    // matches the visible IPS Voltage/Current table exactly.
                    if (!isNaN(v) && v <= 0) { v = 0; }
                    if (!isNaN(v)) { hasValue = true; }
                    row.push(isNaN(v) ? '-' : v.toFixed(2));
                }
            });
            if (hasValue) { rows.push(row); }
        });

        return {
            headers: headers,
            rows: rows,
            isEld: isEld,
            bucket: bucket,
            label: isEld ? 'ELD'
                : (bucket === 'voltage' ? 'IPS Voltage'
                    : bucket === 'current' ? 'IPS Current' : 'IPS Digital'),
            sheet: isEld ? 'ELD'
                : (bucket === 'voltage' ? 'IPS_Voltage'
                    : bucket === 'current' ? 'IPS_Current' : 'IPS_Digital')
        };
    }

    /*
     * IPS / ELD export -- MUST match the pivoted table on screen (row =
     * timestamp, column = asset).
     *
     * IPS exports ALL THREE tabs (Voltage / Current / Digital) in one file, not
     * just the tab currently open. ELD returns its single grid.
     *
     * Returns { isEld: bool, grids: [ {headers, rows, label, sheet, ...}, ... ] }.
     * Grids with no columns beyond Date & Time are dropped so an empty tab does
     * not produce a blank sheet.
     */
    function _pivotBuildExportGrid(assetResults) {
        var isEld = isEldHistoryAssetType();

        if (isEld) {
            return { isEld: true, grids: [_pivotBuildOneGrid(assetResults, null)] };
        }

        var grids = [];
        ['voltage', 'current', 'digital'].forEach(function (bucket) {
            /* Every bucket sheet is built from its full-range per-tab payload
             * re-fetched by _ipsFetchAllTabsForExport (which now covers ALL tabs,
             * active included). Each was fetched PER ASSET, so columns carry real
             * assetIds and no explode is needed. _pivotBuildOneGrid filters. */
            var src = _ipsRememberedBucketResults(bucket);


            /*
             * ALWAYS emit the sheet -- an empty one if necessary.
             *
             * This used to `return` when the bucket had no payload, and to
             * drop the grid when it had no columns. Both exits were silent, so
             * a report that came back missing its Current tab gave no clue
             * whether the prefetch returned nothing, or the columns failed to
             * classify. An empty sheet that says "No data for this period."
             * is honest; a missing sheet is not, and the report is supposed to
             * carry all three tabs regardless of which one was open.
             *
             * The console line below names which of the two happened, so the
             * next download diagnoses itself.
             */
            var g = _pivotBuildOneGrid(src || [], bucket);
            if (!src || !src.length) {
                console.warn('[IPS export] "' + bucket + '" had NO payload ' +
                    '(bucket cache empty and it is not the active tab) -- empty sheet written.');
            } else if (g.headers.length <= 1) {
                console.warn('[IPS export] "' + bucket + '" had a payload from ' + src.length +
                    ' asset(s) but produced NO columns -- check _ipsClassifyColumn -- empty sheet written.');
            } else if (!g.rows.length) {
                console.warn('[IPS export] "' + bucket + '" produced ' + (g.headers.length - 1) +
                    ' column(s) but NO rows -- payload carried no readings in the window.');
            }
            grids.push(g);
        });


        return { isEld: false, grids: grids };
    }

    function _pivotIsExportMode() {
        return (typeof isIpsHistoryAssetType === 'function' &&
            typeof isEldHistoryAssetType === 'function' &&
            (isIpsHistoryAssetType() || isEldHistoryAssetType()));
    }



/* ===== IPS / ELD PIVOT EXPORT (pivot-only subset of the source export block) ===== */

    function _pivotWriteFile(kind, grid) {
        // grid = { isEld, grids:[ {headers, rows, label, sheet, ...}, ... ] }
        // IPS carries one entry per tab (Voltage / Current / Digital);
        // ELD carries a single entry.
        var grids = grid.grids || [];
        var stamp = formatDateDDMMYYYY(new Date()).replace(/\//g, '-');
        var baseName = (grid.isEld ? 'ELD_History_' : 'IPS_History_') + stamp;
        var totalRowCount = grids.reduce(function (n, g) { return n + g.rows.length; }, 0);

        getReportFilterValues(function (fv) {
            var infoText = 'Zone: ' + fv.zone
                + '   |   Division: ' + fv.division
                + '   |   Station: ' + fv.station
                + '   |   Asset Type: ' + fv.assetType
                + '   |   Period: '
                + convertDateToddmmyyyy($('#txtFromDate').val()) + ' ' + ($('#txtFromTime').val() || '00:00')
                + ' to '
                + convertDateToddmmyyyy($('#txtToDate').val()) + ' ' + ($('#txtToTime').val() || '23:59')
                + '   |   Generated: ' + formatDateTimeDDMMYYYY(new Date());

            /*
             * Zone / Division / Station / Vendor Name / Asset Type now ride on
             * every row, exactly as they do in the Point Machine, Track and
             * Signal reports, instead of only appearing in the banner above.
             */
            var fixedHeaders = _thReportFixedHeaders();
            var fixedValues = _thReportFixedValues(fv);

            try {
                if (kind === 'csv') {
                    var esc = function (v) {
                        var s = (v === null || v === undefined) ? '' : String(v);
                        return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
                    };
                    var lines = [];
                    lines.push(esc('Telemetry History Report'));
                    lines.push(esc(infoText));
                    grids.forEach(function (g) {
                        lines.push('');
                        lines.push(esc('=== ' + g.label + ' ==='));
                        lines.push(['S.No'].concat(fixedHeaders, g.headers).map(esc).join(','));
                        if (!g.rows.length) {
                            lines.push(esc('No data for this period.'));
                        }
                        g.rows.forEach(function (r, i) {
                            lines.push([i + 1].concat(fixedValues, r).map(esc).join(','));
                        });
                    });
                    saveAs(new Blob(['\uFEFF' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' }),
                        baseName + '.csv');
                    showSuccess('CSV downloaded — ' + grids.length + ' section(s), ' + totalRowCount + ' row(s).', 'Download Complete');
                    return;
                }

                if (kind === 'pdf') {
                    // Match the guarded construction used by the other PDF
                    // exporters on this screen: depending on the build loaded,
                    // jsPDF lives at window.jspdf.jsPDF (UMD v2) or window.jsPDF
                    // (older builds). Bare `jspdf.jsPDF` throws on the latter.
                    if (typeof window.jspdf === 'undefined' && typeof window.jsPDF === 'undefined') {
                        showError('PDF library not loaded.', 'Download Error');
                        return;
                    }
                    var _jsPDFCtor = window.jspdf ? window.jspdf.jsPDF : window.jsPDF;
                    var doc = new _jsPDFCtor({ orientation: 'landscape', unit: 'pt', format: 'a3' });
                    grids.forEach(function (g, gi) {
                        if (gi > 0) { doc.addPage(); }
                        doc.setFontSize(13);
                        doc.text('Telemetry History Report - ' + g.label, 30, 30);
                        doc.setFontSize(7.5);
                        doc.text(infoText, 30, 46);
                        if (!g.rows.length) {
                            doc.setFontSize(9);
                            doc.text('No data for this period.', 30, 70);
                            return;
                        }
                        doc.autoTable({
                            head: [['S.No'].concat(fixedHeaders, g.headers)],
                            body: g.rows.map(function (r, i) {
                                return [i + 1].concat(fixedValues, r);
                            }),
                            startY: 58,
                            styles: { fontSize: 6.5, cellPadding: 2, overflow: 'linebreak' },
                            headStyles: { fillColor: [67, 56, 202], textColor: 255, fontStyle: 'bold', fontSize: 6.5 },
                            alternateRowStyles: { fillColor: [248, 250, 252] },
                            margin: { left: 20, right: 20 }
                        });
                    });
                    doc.save(baseName + '.pdf');
                    showSuccess('PDF downloaded — ' + grids.length + ' page(s), ' + totalRowCount + ' row(s).', 'Download Complete');
                    return;
                }

                // ---- Excel (default) ----
                var wb = new ExcelJS.Workbook();
                wb.creator = 'RDPMS';
                wb.created = new Date();

                // One worksheet per tab.
                grids.forEach(function (g) {
                    var headers = g.headers;
                    var rows = g.rows;
                    var label = g.label;
                    var totalCols = headers.length + 1 + fixedHeaders.length; // S.No + fixed identity columns
                    var ws = wb.addWorksheet(g.sheet);

                    // Row 1: Title
                    var r1 = ws.addRow(['Telemetry History Report - ' + label]);
                r1.height = 32;
                ws.mergeCells(1, 1, 1, totalCols);
                var c1 = r1.getCell(1);
                c1.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4338CA' } };
                c1.font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 15, name: 'Segoe UI' };
                c1.alignment = { vertical: 'middle', horizontal: 'center' };

                // Row 2: Filter info
                var r2 = ws.addRow([infoText]);
                r2.height = 18;
                ws.mergeCells(2, 1, 2, totalCols);
                var c2 = r2.getCell(1);
                c2.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FFEEF2FF' } };
                c2.font = { italic: true, size: 9, color: { argb: 'FF3730A3' }, name: 'Segoe UI' };
                c2.alignment = { horizontal: 'center', vertical: 'middle' };

                // Row 3: Spacer
                ws.addRow([]).height = 4;

                // Row 4: Header
                var hdr = ws.addRow(['S.No'].concat(fixedHeaders, headers));
                hdr.height = 30;
                hdr.eachCell({ includeEmpty: true }, function (cell) {
                    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4338CA' } };
                    cell.font = { bold: true, size: 9.5, color: { argb: 'FFFFFFFF' }, name: 'Segoe UI' };
                    cell.alignment = { horizontal: 'center', vertical: 'middle', wrapText: true };
                    cell.border = {
                        bottom: { style: 'thin', color: { argb: 'FF3730A3' } },
                        right: { style: 'hair', color: { argb: 'FF6366F1' } }
                    };
                });

                    // A visited tab with no readings still gets its sheet, so the
                    // file shows that the tab was checked rather than silently
                    // omitting it.
                    if (!rows.length) {
                        var emptyRow = ws.addRow(['No data for this period.']);
                        ws.mergeCells(emptyRow.number, 1, emptyRow.number, totalCols);
                        var ec = emptyRow.getCell(1);
                        ec.font = { italic: true, size: 10, color: { argb: 'FF64748B' }, name: 'Segoe UI' };
                        ec.alignment = { horizontal: 'center', vertical: 'middle' };
                        emptyRow.height = 22;
                    }

                    // Data rows
                    rows.forEach(function (r, ri) {
                        var exRow = ws.addRow([ri + 1].concat(fixedValues, r));
                    var isEven = ri % 2 === 0;
                    exRow.height = 17;
                    exRow.eachCell({ includeEmpty: true }, function (cell, colNum) {
                        // S.No + the identity columns + Date & Time
                        var isFixed = colNum <= (2 + fixedHeaders.length);
                        var val = cell.value ? cell.value.toString() : '';
                        var isNumeric = !isFixed && val !== '' && val !== '-' && !isNaN(val.replace(/,/g, ''));
                        cell.fill = {
                            type: 'pattern', pattern: 'solid',
                            fgColor: {
                                argb: isFixed
                                    ? (isEven ? 'FFE8F0FE' : 'FFF0F7FF')
                                    : (isEven ? 'FFF8FAFC' : 'FFFFFFFF')
                            }
                        };
                        cell.font = {
                            size: 10, name: 'Segoe UI', bold: isFixed && colNum > 1,
                            color: { argb: isFixed ? 'FF1E3A5F' : 'FF1E293B' }
                        };
                        cell.alignment = {
                            horizontal: colNum === 1 ? 'center' : isNumeric ? 'right' : (isFixed ? 'center' : 'left'),
                            vertical: 'middle'
                        };
                        cell.border = {
                            bottom: { style: 'hair', color: { argb: 'FFE2E8F0' } },
                            right: { style: 'hair', color: { argb: 'FFE2E8F0' } }
                        };
                    });
                });

                // Auto-fit widths
                ws.columns.forEach(function (col, idx) {
                    var colNum = idx + 1;
                    var maxLen = 0;
                    col.eachCell({ includeEmpty: false }, function (cell) {
                        var l = cell.value ? cell.value.toString().length : 0;
                        if (l > maxLen) maxLen = l;
                    });
                    var minW = colNum === 1 ? 7
                        : colNum <= 1 + fixedHeaders.length ? 14
                            : colNum === 2 + fixedHeaders.length ? 20
                                : 12;
                    col.width = Math.min(Math.max(maxLen + 1, minW), 30);
                });
                });   // end grids.forEach

                wb.xlsx.writeBuffer().then(function (buffer) {
                    saveAs(new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
                        baseName + '.xlsx');
                    showSuccess('Excel downloaded — ' + grids.length + ' sheet(s), ' + totalRowCount + ' row(s).', 'Download Complete');
                });
            } catch (err) {
                console.error('[Pivot export] failed:', err);
                showError('Failed to download ' + (kind || 'file') + '.', 'Download Error');
            }
        });
    }

    /*
       * Per-bucket attribute-id whitelist for one asset.
       *
       * The same split fnSearchHistory applies when it builds _payload.attrIds
       * for the on-screen tab (~line 7440):
       *
              *   RoleType 'd'                -> digital  (AssetAttributeId)
       *   RoleType != 'd', alias VIPS -> voltage
       *   RoleType != 'd', alias IIPS -> current
       *
       * Returns [] when ASSET_INFO_MAP has nothing for this asset; the caller
       * then omits attrIds entirely and the server returns everything -- the
       * same fail-open the search uses.
       */
    function _ipsBucketAttrIds(assetId, bucket) {
        var list = (typeof ASSET_INFO_MAP !== 'undefined' && ASSET_INFO_MAP)
            ? (ASSET_INFO_MAP[String(assetId)] || ASSET_INFO_MAP[assetId] || [])
            : [];
        var ids = [];
        for (var i = 0; i < list.length; i++) {
            var it = list[i];
            if (!it) { continue; }
            var rt = String(it.RoleType || '').toLowerCase().trim();
            if (bucket === 'digital') {
                if (rt !== 'd') { continue; }
                var dId = it.AssetAttributeId;
                if (dId !== undefined && dId !== null && dId !== '') { ids.push(dId); }
            } else {
                if (rt === 'd') { continue; }
                var alias = String(it.AliasName || it.CanonicalName || it.AttributeName || '').toUpperCase();
                if (bucket === 'voltage' && alias.indexOf('VIPS') === -1) { continue; }
                if (bucket === 'current' && alias.indexOf('IIPS') === -1) { continue; }
                if (it.AssetAttributeId !== undefined && it.AssetAttributeId !== null &&
                    it.AssetAttributeId !== '') {
                    ids.push(it.AssetAttributeId);
                }
            }
        }
        return ids;
    }

    /*
     * Fill the per-tab cache for every IPS bucket the user has NOT opened.
     *
     * _ipsRememberBucketResults only records a bucket as renderIpsHistoryPivot
     * paints it, so a download taken from the Voltage tab produced a
     * Voltage-only file; the Current / Digital sheets appeared only after the
     * user had visited those tabs. The download must not depend on navigation.
     *
     * Each missing bucket is fetched through the SAME path its tab uses on
     * screen: GetHistoryDataDashboard, that bucket's attrIds, cursor-walked,
     * parsed with bindBothEdges=true. The first version of this helper took a
     * shortcut through GetHistoryData (/api/HistoryValue), betting that one
     * unfiltered call could serve all three sheets. It could not:
     *
     *   - no attrIds -> the grid's row axis became the union of EVERY
     *     attribute's timestamps, so 61 real Current readings rendered as
     *     8403 carry-forward rows;
     *   - different attribute set -> the Digital sheet got its placeholder
     *     headers and no relay data at all;
     *   - no edge binding -> the window-start snapshot was missing, so
     *     Batt Discharging read "-" on every row.
     *
     * One request per (bucket, asset) is more calls than one per asset, but it
     * is the only way the sheet is guaranteed to equal the tab.
     *
     * The ACTIVE tab is untouched: it still exports from lastAssetResults,
     * which the ELD/IPS page walk already loaded whole-range.
     *
     * ELD (single grid) and a missing pager state both short-circuit to
     * onDone, so the previous behaviour is preserved in every other case.
     */
    function _ipsFetchAllTabsForExport(onDone) {
        if (typeof isIpsHistoryAssetType !== 'function' || !isIpsHistoryAssetType()) {
            onDone();
            return;
        }

        // Server-side pagination: the on-screen tab AND any visited-tab cache
        // now hold only ONE page, so none of them can stand in for a full sheet.
        // Re-fetch the full range for EVERY tab, including the active one.
        var missing = ['voltage', 'current', 'digital'];

        /*
         * ASSET LIST: the full selection, NOT _dashServerPagerState.
         *
         * These IPS assets carry ONE attribute each -- asset 43505 IS
         * "ABS-FAN 24 V" (alias VIPS DC BLOCK TEL UP) -- so the tabs differ by
         * ASSET, not merely by attribute. The search only ever fetches the
         * active tab's assets, so on the Voltage tab _dashServerPagerState
         * held 21 voltage assets and none of the current/digital ones. Asking
         * those 21 for their current data returned their voltage attribute,
         * _ipsClassifyColumn threw it out as 'voltage', and the Current sheet
         * was left with nothing but placeholder headers.
         *
         * (_dashServerPagerState is also never cleared, so it accumulates
         * assets from every earlier search in the session -- 21 entries for a
         * 2-asset search.)
         *
         * Read the same checkbox list the search and
         * _pivotAddMissingAssetColumns read, and take the window from any
         * pager entry: every entry of the current search shares it.
         */
        var _anyState = null;
        for (var _sk in _dashServerPagerState) {
            if (_dashServerPagerState.hasOwnProperty(_sk) && _dashServerPagerState[_sk]) {
                _anyState = _dashServerPagerState[_sk];
                break;
            }
        }
        if (!_anyState) {
            console.warn('[IPS export] no search window available -- non-active tabs will be empty.');
            onDone();
            return;
        }

        /*
 * ONE union request PER TAB (not per asset). DashboardHistory accepts a
 * comma list of AssetIds and computes the union grid server-side -- the
 * SAME request the on-screen tab uses -- so the report row count matches
 * the view exactly. Per-asset walking could not: each asset was capped at
 * IPS_TAB_MAX_PAGES, so a frequently-changing member (Batt Charging, a
 * busy 110V point) truncated the sheet and the report showed far fewer
 * rows than the view (e.g. Current stopped days short of the window).
 *
 * attrIds is the UNION of the tab's bucket ids across its assets, sent so
 * (a) the row axis is this tab's timestamps only -- an UNFILTERED union
 *     inflates rows to the union of EVERY attribute's timestamps -- and
 * (b) the response carries a per-column assetId (as the view's search
 *     does), which parseChartData (_ipsExplodeUnionAssetResults) then
 *     splits back per asset at build time. Assets are scoped by
 * _pivotAssetBuckets, the same rule as the on-screen placeholder columns
 * (fail-open for an asset with no metadata).
 */
        var jobs = [];
        missing.forEach(function (b) {
            var ids = [];
            var attrIdSet = {};
            $('#listAsset .cb-asset:checked').each(function () {
                var aid = String($(this).val() || '');
                if (!aid) { return; }
                var bk = (typeof _pivotAssetBuckets === 'function') ? _pivotAssetBuckets(aid) : null;
                var known = bk && (bk.voltage || bk.current || bk.digital);
                if (known && !bk[b]) { return; }   // asset not in this bucket
                ids.push(aid);
                var one = (typeof _ipsBucketAttrIds === 'function') ? _ipsBucketAttrIds(aid, b) : [];
                for (var _oi = 0; _oi < one.length; _oi++) { attrIdSet[String(one[_oi])] = true; }
            });
            if (!ids.length) { return; }
            jobs.push({ bucket: b, csvKey: ids.join(','), attrIds: Object.keys(attrIdSet) });
        });
        if (!jobs.length) {
            console.warn('[IPS export] no selected asset declares attributes for ' +
                missing.join('/') + ' -- those sheets will be empty.');
            onDone();
            return;
        }

        var byBucket = {};

        var IPS_TAB_POOL = 3;
        // ONE union stream per tab, so this cap bounds a whole tab, not one asset.
        // 1200 pages x 500 tsLimit = 600k timestamps (beyond an Excel sheet), so a
        // full day/month of a busy tab is never silently truncated the way the old
        // 40-page-per-asset cap was.
        var IPS_TAB_MAX_PAGES = 1200;
        var active = 0, finished = 0, total = jobs.length;

        function runJob(job) {
            var accum = {}, page = 1, cursor = '', guard = 0;

            function flush() {
                var attrs = [];
                for (var k in accum) { if (accum.hasOwnProperty(k)) { attrs.push(accum[k]); } }
                console.log('[IPS export] ' + job.bucket + ' union (' + job.csvKey + ') -> ' +
                    attrs.length + ' attribute(s) over ' + guard + ' page(s)' +
                    (job.attrIds.length ? (', attrIds=' + job.attrIds.join(',')) : ', no attrIds filter'));
                // ONE comma-keyed result for the whole tab; parseChartData
                // (_ipsExplodeUnionAssetResults) splits it per asset at build time,
                // then _pivotBuildOneGrid classifies + sums (Batt Charging /
                // Discharging) exactly as the on-screen tab does.
                byBucket[job.bucket] = [{
                    assetId: job.csvKey, assetName: '', attributes: attrs, serverPaging: false
                }];
                active--; finished++;
                pump();
            }

            function fetchPage() {
                var data = {
                    assetId: job.csvKey,
                    startDate: _anyState.startDate,
                    endDate: _anyState.endDate,
                    page: page,
                    cursor: cursor,
                    fillGaps: true,
                    // Ask for the whole result in one page rather than the
                    // clamped 500: totalRecords is what the search already
                    // told us is in this window, so the export stops walking
                    // pages and stops truncating at 500. Falls back to 500
                    // only when the count is unknown.
                    pageSize: (_anyState && _anyState.totalRecords > 0)
                        ? _anyState.totalRecords : 500
                };
                if (job.attrIds.length) { data.attrIds = job.attrIds.join(','); }

                $.ajax({
                    url: '/FRS25/TelemetryHistory/GetHistoryDataDashboard',
                    type: 'GET', dataType: 'json', timeout: 300000, data: data
                }).done(function (response) {
                    if (response && response.error) {
                        console.warn('[IPS export] ' + job.bucket + ' union: API error -- ' + response.error);
                    }
                    if (response && !response.error) {
                        // bindBothEdges=true mirrors the search's own call so the
                        // window-start (firstRow) / window-end (lastRow) edges are
                        // present, matching the on-screen pivot.
                        var pageAttrs = getTelemetryHistoryNativeData(
                            response, job.csvKey, _anyState.startDate, _anyState.endDate,
                            'DashboardHistory', undefined, true);
                        _exportMergeAttrs(accum, pageAttrs, job.csvKey);
                        guard++;
                        var nc = response.nextCursor;
                        if (!!response.hasNextPage && nc && nc !== cursor && guard < IPS_TAB_MAX_PAGES) {
                            page++; cursor = nc; fetchPage(); return;
                        }
                    }
                    flush();
                }).fail(function (xhr) {
                    console.warn('[IPS export] ' + job.bucket + ' union: request failed (HTTP ' +
                        ((xhr && xhr.status) || '?') + ').');
                    // Keep whatever pages already arrived; one failure must not
                    // abort the report.
                    flush();
                });
            }

            fetchPage();
        }

        function pump() {
            if (finished >= total && active === 0) {
                missing.forEach(function (b) {
                    if (byBucket[b] && byBucket[b].length) {
                        _ipsRememberBucketResults(b, byBucket[b]);
                    }
                });
                onDone();
                return;
            }
            while (active < IPS_TAB_POOL && jobs.length > 0) {
                active++;
                runJob(jobs.shift());
            }
        }

        _showExportToast('Preparing report…');
        pump();
    }

    /*
     * Re-fetch the FULL range for ELD before an export. ELD now paginates
     * server-side, so lastAssetResults holds only ONE page. Walks every cursor
     * page PER ASSET (single-asset requests -- no comma union, so each column
     * carries a real assetId and needs no explode) and returns one assetResult
     * per asset. Mirrors the IPS per-tab prefetch.
     */
    function _eldFetchAllForExport(onDone) {
        var st = null;
        for (var _k in _dashServerPagerState) {
            if (_dashServerPagerState.hasOwnProperty(_k) && _dashServerPagerState[_k]) { st = _dashServerPagerState[_k]; break; }
        }
        var ids = [];
        $('#listAsset .cb-asset:checked').each(function () {
            var v = String($(this).val() || ''); if (v) { ids.push(v); }
        });
        if (!ids.length && typeof validAssetIds !== 'undefined' && Array.isArray(validAssetIds)) { ids = validAssetIds.slice(); }
        if (!st || !ids.length) { onDone(lastAssetResults || []); return; }

        var results = [];
        var POOL = 3, queue = ids.slice(), active = 0, finished = 0, total = ids.length;
        var MAX_EXPORT_PAGES = 200;
        _showExportToast('Preparing report…');

        function fetchOne(assetId) {
            var accum = {}, page = 1, cursor = '', guard = 0;
            function flush() {
                var attrs = [];
                for (var kk in accum) { if (accum.hasOwnProperty(kk)) { attrs.push(accum[kk]); } }
                results.push({
                    assetId: assetId,
                    assetName: (typeof assetNameMap !== 'undefined' && (assetNameMap[assetId] || assetNameMap[parseInt(assetId, 10)]))
                        ? (assetNameMap[assetId] || assetNameMap[parseInt(assetId, 10)]) : ('Asset ' + assetId),
                    attributes: attrs, serverPaging: false
                });
                active--; finished++; pump();
            }
            function fetchPage() {
                $.ajax({
                    url: '/FRS25/TelemetryHistory/GetHistoryDataDashboard',
                    type: 'GET', dataType: 'json', timeout: 300000,
                    // pageSize = the window's own record count, not a clamped
                    // 500, so the ELD export writes every row it was told
                    // exists instead of the first page of them.
                    data: {
                        assetId: assetId, startDate: st.startDate, endDate: st.endDate,
                        page: page, cursor: cursor, fillGaps: true,
                        pageSize: (st && st.totalRecords > 0) ? st.totalRecords : 500
                    }
                }).done(function (response) {
                    if (response && !response.error) {
                        var pageAttrs = getTelemetryHistoryNativeData(response, assetId, st.startDate, st.endDate, 'DashboardHistory', undefined, true);
                        _exportMergeAttrs(accum, pageAttrs, assetId);
                        guard++;
                        var nc = response.nextCursor;
                        if (!!response.hasNextPage && nc && nc !== cursor && guard < MAX_EXPORT_PAGES) { page++; cursor = nc; fetchPage(); return; }
                    }
                    flush();
                }).fail(function () { flush(); });
            }
            fetchPage();
        }
        function pump() {
            if (finished >= total && active === 0) { onDone(results); return; }
            while (active < POOL && queue.length > 0) { active++; fetchOne(queue.shift()); }
        }
        pump();
    }

    function _pivotExport(kind) {
        // On-screen grid is one server page now, so re-fetch the FULL range before
        // building. Both paths fetch PER ASSET (no comma union -> no explode):
        // IPS via the proven per-tab prefetch (now ALL tabs), ELD via _eldFetchAllForExport.
        if (typeof isEldHistoryAssetType === 'function' && isEldHistoryAssetType()) {
            _eldFetchAllForExport(function (eldResults) { _pivotExportBuild(kind, eldResults); });
            return;
        }
        _ipsFetchAllTabsForExport(function () { _pivotExportBuild(kind); });
    }

    function _pivotExportBuild(kind, fullResults) {
            var grid = _pivotBuildExportGrid(fullResults || lastAssetResults || []);
        if (!grid.grids || !grid.grids.length) {
            _hideExportToast();
            showWarning('No data to export.', 'Download');
            return;
        }
        /*
         * A tab with columns but no readings still gets a sheet, but a file in
         * which EVERY tab is empty is not worth downloading.
         */
        var _anyRows = grid.grids.some(function (g) { return g.rows.length > 0; });
        if (!_anyRows) {
            _hideExportToast();
            showWarning('No data to export.', 'Download');
            return;
        }
        _showExportToast('Generating report…');
        setTimeout(function () {
            try { _pivotWriteFile(kind, grid); }
            finally { _hideExportToast(); }
        }, 30);
    }

    /*
     * Walk every remaining vibration page into st.pageCache before exporting.
     *
     * _vibCollectAllCachedRows already merges every CACHED page, but a page
     * only enters the cache when the user navigates to it (or when
     * _vibPrefetchNext pulls the next one). Downloading from page 1 therefore
     * produced a one-page report; from page 2, a two-page report.
     *
     * Cursor pagination means page N+1's cursor arrives with page N, so the
     * walk is strictly sequential per asset, starting at page 1 (already
     * cached, so it costs no request). background=true fetches into pageCache
     * WITHOUT touching st.result / st.hasNextPage, so the page on screen and
     * its pager are left exactly as the user left them.
     */


/* =============================================================================
 * Export entry points — WRAP, never replace. Same reasoning as the vibration
 * module: only the pivot exporters were ported, and the page's existing
 * fnDownloadExcel / fnDownloadCSV are wrapped so IPS and ELD are intercepted
 * while every other asset type keeps the exporter it already had.
 *
 * This wrapper is installed after the vibration module's, so vibration mode is
 * checked first. The two modes are mutually exclusive anyway (vibration is a
 * Point Machine tab), but the order keeps that explicit.
 * ========================================================================== */
(function () {
    'use strict';
    var W = window;

    function wrap(name, kind) {
        var prev = W[name];
        if (typeof prev !== 'function') return;
        if (prev.__pivotWrapped) return;

        var wrapped = function () {
            try {
                if (typeof _pivotIsExportMode === 'function' && _pivotIsExportMode() &&
                    typeof _pivotExport === 'function') {
                    _pivotExport(kind);
                    return;
                }
            } catch (e) {
                if (W.console) console.error('[IPS/ELD export] failed; using the standard export:', e);
            }
            return prev.apply(this, arguments);
        };
        wrapped.__pivotWrapped = true;
        W[name] = wrapped;
    }

    $(function () {
        wrap('fnDownloadExcel', 'excel');
        wrap('fnDownloadCSV', 'csv');
    });
})();
