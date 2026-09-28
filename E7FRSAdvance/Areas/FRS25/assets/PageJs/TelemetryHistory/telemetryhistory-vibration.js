/* =============================================================================
 * telemetryhistory-vibration.js — Point Machine vibration history.
 *
 * Ported from the web project (E7MRIV2Web) Telemetry History view, where this
 * lived inline in Index.cshtml. It is kept as a separate file here on purpose:
 * FRS Advance's Index.cshtml is already ~320KB, and Razor emits each markup run
 * as one WriteLiteral that CodeDom re-splits into 80-char chunks — past roughly
 * 4200 chunks csc fails with CS1647 and the page 500s with no useful message.
 * Adding ~200KB of inline script would have walked straight into that.
 *
 * Load order: a classic <script> AFTER the view's inline script and AFTER
 * telemetryhistory-compat.js, which supplies the helpers this file calls
 * (_thPageSizeOptionsHtml, formatTimestampWithBadge, clearHistoryView, ...).
 *
 * Declarations are top-level on purpose, exactly as they were in the source
 * view: renderPmVibrationHistory and friends are looked up as globals by the
 * renderHistoryTable dispatch and by the export code.
 *
 * Server endpoints used (added to TelemetryHistoryController for this port):
 *   GetPMHistoryVibration        -> /api/DashboardPMHistoryVibration
 *   GetVibrationConfig           -> VibrationConfig/SiteId/{siteId}
 *
 * Styling lives in Areas/FRS25/assets/css/telemetryhistory-vibration.css,
 * re-themed from the source's light-mint palette onto this page's dark-first
 * convention with a body[data-aurora="light"] override.
 * ========================================================================== */

    // ================================================================
    // POINT MACHINE — VIBRATION HISTORY RENDERER
    // ================================================================
    // Renders a dedicated vibration table for the "Vibration Data" tab.
    // Purely additive: called only when pmViewMode === 'vibration' via a
    // guard at the top of renderHistoryTable(). Any failure falls back to
    // the normal renderer so no existing PM/Signal path can be broken.
    //
    // Layout:
    //   Asset Name | Date & Time | Operation Type | 9 vibration columns
    //   Each attribute column has 2 header rows (name + "Event").
    //   Each Operation Type cell has 2 stacked lines:
    //       row 1 — operation label (Normal / Reverse / —)
    //       row 2 — PT | TPR lights (green if PM event within ±3min)
    //
    // Detection rules (per user spec + screenshot AttrIds):
    //   Vibration attr    → AttributeName contains "vibration"  OR
    //                       AttributeId ∈ VIB_ATTR_ID_MAP
    //   Which end (A/B)   → derived from AttributeName tokens
    //                       ("a end", "b end", "(a)", "(b)", trailing " a"/" b")
    //   Which column      → priority: numeric AttrId map → name-based matcher
    //   Operation type    → any Combined-NWCR (AttrId 221) with Value === 1
    //                       within ±3min → "Normal", light PT.
    //                     any Combined-RWCR (AttrId 222) with Value === 1
    //                       within ±3min → "Reverse", light PT.
    //   Row grouping      → keyed by (assetId, end, floor(TimestampDevice/sec))
    //                       so simultaneous XYZ readings merge into one row.
    // ================================================================

    // AttrIds from the user's screenshot (screenshot shows the "A" side; the
    // matcher below also derives end from AttributeName so the SAME map works
    // if identical AttrIds are re-used on B end. If B end has DIFFERENT IDs,
    // the name-based matcher below still catches them correctly.)
    // ============================================================
    // VIBRATION ATTRIBUTE ID → (end, column) MAP
    // ------------------------------------------------------------
    // The Telemetry History API returns AttributeId only (no name),
    // so end (A/B) and column identity are BOTH resolved from the
    // numeric AttrId here — the ASSET_INFO_MAP name lookup is used
    // only as a soft fallback.
    //
    // A-end IDs come from the user's screenshot. B-end IDs default
    // to (A-end + 100) — an assumption used until verified. If your
    // B-end IDs use a different offset, edit the B-end entries
    // below and the fix is complete.
    //
    // To find the real B-end IDs when data loads, run in console:
    //     debugVibHistoryMapping(lastAssetResults[0].assetId)
    // Any row with column=null / end=null is a candidate that needs
    // adding to this table.
    // ============================================================
  // ============================================================
    // VIBRATION METADATA — mirrors live page's approach
    // ------------------------------------------------------------
    // The History API returns only AttributeId (no name), and the
    // SAME AttributeId is used for BOTH A end and B end. Live
    // disambiguates via a separate endpoint:
    //   /FRS25/Telemetry/GetBulkAssetMetadata
    // which returns per-asset attribute Titles containing an
    // "-A end" or "-B end" suffix. We call the same endpoint here
    // and cache results in PM_VIB_BULK_META for the current site.
    //
    // Lookup path:
    //   1. PM_VIB_BULK_META[assetId].byId[assetAttributeId]  → { title, aliasName }
    //   2. Regex on title to extract end (A/B) — same rules as live's
    //      pmVibrationResolveEnd()
    //   3. Regex on stripped title to identify one of 9 columns
    // ============================================================
    var PM_VIB_BULK_META = {};                // { assetId → { byId: { attrId → {title, aliasName} } } }
    var PM_VIB_BULK_LOADED_SITE = null;       // string; last site ID that finished loading
    var PM_VIB_BULK_LOADING = false;          // in-flight guard

    // Attempt to resolve current site ID from common history-page selectors.
    // Falls back through several candidates so we don't need to know the
    // exact ID assigned in this Razor view.
    function _vibGetSiteId() {
        var candidates = ['#drpSite', '#ddlSite', '#drpStation', '#ddlStation', '#txtSiteId'];
        for (var i = 0; i < candidates.length; i++) {
            var v = $(candidates[i]).val();
            if (v != null && String(v).trim() !== '' && String(v).trim() !== '0') return String(v).trim();
        }
        // Globals commonly used on this page
        if (typeof siteId !== 'undefined' && siteId) return String(siteId);
        if (typeof currentSiteId !== 'undefined' && currentSiteId) return String(currentSiteId);
        if (typeof selectedSiteId !== 'undefined' && selectedSiteId) return String(selectedSiteId);
        return null;
    }

    // Register one attribute entry into the bulk metadata cache.
    // Only vibration entries are kept — this mirrors live's
    // isPointMachineVibrationAttributeName filter.
    function _vibRegisterMetaAttr(assetId, attr) {
        if (!attr) return;
        var id = String(attr.Id != null ? attr.Id :
                        (attr.AssetAttributeId != null ? attr.AssetAttributeId : '')).trim();
        var title = String(attr.Title || attr.AttributeName || '').trim();
        var alias = String(attr.AliasName || '').trim();
        var probe = (title + ' ' + alias).toLowerCase();

        var isVib = probe.indexOf('vibration') !== -1 ||
                    probe.indexOf('accel') !== -1 ||
                    /\bvib\b/.test(probe) ||
                    /full\s*band/.test(probe) ||
                    /\bhf\s*rms\b/.test(probe) ||
                    /pk[\s\-_]*pk/.test(probe);
        if (!isVib) return;

        if (!PM_VIB_BULK_META[assetId]) PM_VIB_BULK_META[assetId] = { byId: {} };
        var entry = { id: id, title: title, aliasName: alias };
        if (id) PM_VIB_BULK_META[assetId].byId[id] = entry;
    }

    // Fetch bulk vibration metadata for a site — SAME endpoint live uses.
    // Idempotent: if already loaded for this site, calls onDone immediately.
    function loadPmVibBulkMetadata(siteId, onDone) {
        onDone = onDone || function () { };
        if (!siteId) { onDone(); return; }
        if (PM_VIB_BULK_LOADED_SITE === String(siteId)) { onDone(); return; }
        if (PM_VIB_BULK_LOADING) {
            var poll = setInterval(function () {
                if (!PM_VIB_BULK_LOADING) { clearInterval(poll); onDone(); }
            }, 100);
            return;
        }
        PM_VIB_BULK_LOADING = true;

        $.ajax({
            url: '/FRS25/Telemetry/GetBulkAssetMetadata',
            type: 'POST',
            contentType: 'application/json',
            dataType: 'json',
            data: JSON.stringify({ SearchCriteria: { SiteId: siteId, AssetTypeId: 3 } }),
            success: function (response) {
                PM_VIB_BULK_META = {};
                var assets = (response && response.success === true && Array.isArray(response.mAssets)) ? response.mAssets : [];
                for (var i = 0; i < assets.length; i++) {
                    var asset = assets[i];
                    if (!asset || asset.Id == null) continue;
                    var attrs = Array.isArray(asset.assetAttributes) ? asset.assetAttributes : [];
                    for (var j = 0; j < attrs.length; j++) {
                        _vibRegisterMetaAttr(String(asset.Id), attrs[j]);
                    }
                }
                PM_VIB_BULK_LOADED_SITE = String(siteId);
                PM_VIB_BULK_LOADING = false;
                console.log('[Vib History Bulk] Loaded site=' + siteId +
                    ', assets with vibration=' + Object.keys(PM_VIB_BULK_META).length);
                onDone();
            },
            error: function (xhr, status, err) {
                console.error('[Vib History Bulk] Load failed:', status, err);
                PM_VIB_BULK_LOADING = false;
                onDone();
            }
        });
    }

    // Look up { title, aliasName } for an (asset, attrId).
    function _vibLookupMeta(assetId, attrId) {
        var m = PM_VIB_BULK_META[String(assetId)];
        if (!m) return null;
        return m.byId[String(attrId)] || null;
    }

    // Robust end resolver — mirrors live's pmVibrationResolveEnd().
    // Accepts title strings like "Vibration_Sensor_X_HF_RMS_Accel_G-A end-Max".
    function _vibResolveEndFromTitle(title) {
        if (!title) return null;
        var raw = String(title).trim()
            .replace(/-(Max|Avg|Array|Min|Count|OperationTime)$/i, '');
        if (/(?:^|[\s_-])A[\s_-]*END(?:[\s_-]*A[\s_-]*END)?$/i.test(raw)) return 'A';
        if (/(?:^|[\s_-])B[\s_-]*END(?:[\s_-]*B[\s_-]*END)?$/i.test(raw)) return 'B';
        // Fallback — try the older token-based detector on the same string
        return _vibDetectEnd(raw);
    }


    // Exact vibration DB AttributeId mapping received from
// GetBulkAssetMetadata.
//
// Do not derive these 11 displayed columns only from the attribute title.
// For example, IDs 691/693/695 use "RMS_Vel_inps" in metadata but must
// populate the Full Band RMS Accel columns shown in the table.
var VIB_ATTR_ID_MAP = {
    // A-end
    691: 'x_fullband',
    693: 'y_fullband',
    695: 'z_fullband',

    692: 'x_hf',
    694: 'y_hf',
    696: 'z_hf',

    698: 'x_pkpk',
    699: 'y_pkpk',
    700: 'z_pkpk',

    697: 'temperature',
    719: 'motor_run',

    // B-end (same columns)
    957: 'x_fullband',
    959: 'y_fullband',
    961: 'z_fullband',

    958: 'x_hf',
    960: 'y_hf',
    962: 'z_hf',

    964: 'x_pkpk',
    965: 'y_pkpk',
    966: 'z_pkpk',

    963: 'temperature',
    985: 'motor_run'
};


    var PM_VIB_SCALAR_COLUMN_KEYS = [
        'temperature',
        'motor_run'
    ];

/*
 * DataTypes accepted for the scalar columns above.
 */
var PM_VIB_SCALAR_ALLOWED_DATATYPES = [
    'vibration',
    'rdpms',
    'unknown',
    ''
];

/*
 * RDPMS reports on its own cycle, so a scalar reading rarely carries the
 * exact TimestampDevice of a vibration waveform event. The closest reading
 * inside this window is bound to the row.
 */
var PM_VIB_SCALAR_MATCH_TOLERANCE_MS = 15 * 60 * 1000;



    // DataLogger AttrIds used only for Vibration History event matching.
var VIB_NWCR_ATTR_ID = 282;   // Combined-NWCR pickup = Normal operation
var VIB_RWCR_ATTR_ID = 283;   // Combined-RWCR pickup = Reverse operation

     /*
     * No longer used by PT/Operation matching -- both the on-screen table
     * and the CSV export now use PM_VIB_CAUSE_WINDOW_MS (+/- 1 minute) so
     * the PT light matches the same window as TPR. Left declared in case
     * anything else still references it.
     */
    var VIB_OP_WINDOW_MS = 3 * 60 * 1000;

    // Column spec — labels match the screenshot the user provided
    // ---- Axis / metric matcher helpers ----
    // Robust to a wide range of naming conventions:
    //   "X Full Band RMS Accel (G)"        (spec)
    //   "X_Full_Band_RMS_A_end"            (underscored)
    //   "XFullBandRMS"                     (CamelCase, no separators)
    //   "AccelX A End Full Band"           (axis at end)
    //   "Vibration_A_end_X_FullBand"       (axis in middle)
    function _vibHasAxis(n, axis) {
        // Match axis letter as: standalone word, camel-boundary, or paren wrapped.
        // n is already lower-cased with punctuation flattened to spaces by _vibStripEnd,
        // BUT for camel-case we also want to catch "accelx" → axis 'x' at the tail
        // of "accel". Two-pronged check:
        var a = axis.toLowerCase();
        if (new RegExp('(^|[^a-z])' + a + '($|[^a-z])').test(n)) return true;
        // Camel/glued: axis letter appears at end of a token like "accelx"
        if (new RegExp('[a-z]' + a + '(?![a-z])').test(n)) return true;
        return false;
    }
    function _vibHasFullBand(n) { return /full/.test(n) || /\bband\b/.test(n) || /fullband/.test(n); }
    function _vibHasHF(n)       { return /\bhf\b/.test(n) || /high\s*freq/.test(n) || /highfreq/.test(n) || /\bh\s*f\b/.test(n); }
    function _vibHasPkPk(n)     { return /\bpk\b/.test(n) || /pk\s*pk/.test(n) || /pkpk/.test(n) || /peak/.test(n) || /p\s*p\b/.test(n); }

    // Column spec — labels match the screenshot the user provided
    var VIB_COLUMNS = [
    {
        key: 'x_fullband',
        attrId: 691,
        label: 'X Full Band<br/>RMS Accel (G)',
        why: 'Primary health trend',
        match: function (n) {
            return _vibHasAxis(n, 'x') &&
                (_vibHasFullBand(n) || /rms\s*vel/.test(n)) &&
                !_vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'y_fullband',
        attrId: 693,
        label: 'Y Full Band<br/>RMS Accel (G)',
        why: 'Primary health trend',
        match: function (n) {
            return _vibHasAxis(n, 'y') &&
                (_vibHasFullBand(n) || /rms\s*vel/.test(n)) &&
                !_vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'z_fullband',
        attrId: 695,
        label: 'Z Full Band<br/>RMS Accel (G)',
        why: 'Primary health trend',
        match: function (n) {
            return _vibHasAxis(n, 'z') &&
                (_vibHasFullBand(n) || /rms\s*vel/.test(n)) &&
                !_vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'x_hf',
        attrId: 692,
        label: 'X HF RMS<br/>Accel (G)',
        why: 'Early bearing fault',
        match: function (n) {
            return _vibHasAxis(n, 'x') &&
                _vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'y_hf',
        attrId: 694,
        label: 'Y HF RMS<br/>Accel (G)',
        why: 'Early bearing fault',
        match: function (n) {
            return _vibHasAxis(n, 'y') &&
                _vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'z_hf',
        attrId: 696,
        label: 'Z HF RMS<br/>Accel (G)',
        why: 'Early bearing fault',
        match: function (n) {
            return _vibHasAxis(n, 'z') &&
                _vibHasHF(n) &&
                !_vibHasPkPk(n);
        }
    },
    {
        key: 'x_pkpk',
        attrId: 698,
        label: 'X Pk-Pk<br/>Accel (G)',
        why: 'Impact/jam detection',
        match: function (n) {
            return _vibHasAxis(n, 'x') && _vibHasPkPk(n);
        }
    },
    {
        key: 'y_pkpk',
        attrId: 699,
        label: 'Y Pk-Pk<br/>Accel (G)',
        why: 'Impact/jam detection',
        match: function (n) {
            return _vibHasAxis(n, 'y') && _vibHasPkPk(n);
        }
    },
    {
        key: 'z_pkpk',
        attrId: 700,
        label: 'Z Pk-Pk<br/>Accel (G)',
        why: 'Impact/jam detection',
        match: function (n) {
            return _vibHasAxis(n, 'z') && _vibHasPkPk(n);
        }
    },
        {
            key: 'temperature',
            attrId: 697,
        label: 'Temperature<br/>(°C)',
        why: 'Overload/seized bearing',

        /*
         * Scalar channel. The device reports a single reading; there is no
         * waveform Array and no backend Avg / Max for this attribute.
         */
        scalar: true,

        match: function (n) {
            return /temperature|\btemp\b/.test(n);
        }
    },

    {
        key: 'motor_run',
        attrId: 719,
        label: 'Motor Run<br/>Flag',
        why: 'Filter idle vs operation',

        /*
         * Scalar ON / OFF flag. Rendered as Run / Stop, not as Max / Avg.
         */
        scalar: true,
        flag: true,

            match: function (n) {
                return /motor\s*run|run\s*flag|motor.*flag/.test(n);
            }
        }
    ];

    function _vibIsScalarColumn(columnKey) {
    return PM_VIB_SCALAR_COLUMN_KEYS.indexOf(columnKey) !== -1;
}

function _vibIsFlagColumn(columnKey) {
    for (var i = 0; i < VIB_COLUMNS.length; i++) {
        if (VIB_COLUMNS[i].key === columnKey) {
            return VIB_COLUMNS[i].flag === true;
        }
    }

    return false;
}

function _vibIsScalarDataTypeAllowed(dataType) {
    return PM_VIB_SCALAR_ALLOWED_DATATYPES.indexOf(
        String(dataType || '').toLowerCase()
    ) !== -1;
}

/*
 * Preference order when several synthetic variants of the same scalar
 * attribute arrive for one timestamp. The plain value wins.
 */
function _vibScalarRank(item) {
    if (!item) {
        return 99;
    }

    if (item.synthType === 'base') {
        return 0;
    }

    if (item.synthType === 'avg') {
        return 1;
    }

    if (item.synthType === 'max') {
        return 2;
    }

    return 3;
}

/*
 * Motor Run Flag display text.
 */
function _vibFormatFlagValue(value) {
    if (
        value === null ||
        value === undefined ||
        value === ''
    ) {
        return null;
    }

    var raw = String(value).trim().toLowerCase();

    if (
        raw === '1' ||
        raw === 'true' ||
        raw === 'on' ||
        raw === 'run' ||
        raw === 'running' ||
        raw === 'yes'
    ) {
        return 'Run';
    }

    if (
        raw === '0' ||
        raw === 'false' ||
        raw === 'off' ||
        raw === 'stop' ||
        raw === 'stopped' ||
        raw === 'no'
    ) {
        return 'Stop';
    }

    var numericValue = Number(value);

    if (!isNaN(numericValue)) {
        return numericValue > 0 ? 'Run' : 'Stop';
    }

    return String(value);
}

/*
 * Single display string for a scalar event bundle.
 * Used by the table cell and by the Excel / CSV / PDF reports.
 */
function _vibFormatScalarValue(eventBundle) {
    if (!eventBundle) {
        return '';
    }

    if (_vibIsFlagColumn(eventBundle.columnKey)) {
        return _vibFormatFlagValue(eventBundle.value) || '';
    }

    var numericValue = Number(eventBundle.value);

    if (
        eventBundle.value === null ||
        eventBundle.value === undefined ||
        eventBundle.value === '' ||
        isNaN(numericValue)
    ) {
        return eventBundle.value === null ||
            eventBundle.value === undefined
            ? ''
            : String(eventBundle.value);
    }

    /*
     * The Temperature column header is °C.
     *
     * Some devices publish the attribute as Fahrenheit
     * (Vibration_Sensor_Temperature_F). Convert only in that case, exactly
     * as Telemetry Live does, so both screens agree.
     */
    if (eventBundle.columnKey === 'temperature') {
        var sourceName =
            String(
                eventBundle.sourceName || ''
            );

        var isFahrenheit =
            /(?:^|[_\s-])TEMPERATURE[_\s-]*F(?:$|[_\s-])/i.test(
                sourceName
            ) ||
            /(?:^|[_\s-])TEMP[_\s-]*F(?:$|[_\s-])/i.test(
                sourceName
            );

        var celsiusValue =
            isFahrenheit
                ? ((numericValue - 32) * 5 / 9)
                : numericValue;

        return celsiusValue.toFixed(1);
    }

    return _vibFormatEventNumber(numericValue);
}

/*
 * Value-only cell for Temperature and Motor Run Flag.
 *
 * There is no waveform behind these attributes, so no graph button is
 * rendered.
 */
function _vibBuildScalarCell(eventBundle) {
    var displayText =
        _vibFormatScalarValue(eventBundle);

    if (displayText === '') {
        return (
            '<td class="pm-vh-value-cell">' +
            '<div class="pm-vh-event-line pm-vh-event-empty">' +
            '&mdash;' +
            '</div>' +
            '</td>'
        );
    }

    var titleText =
        (eventBundle.sourceName || '') +
        (eventBundle.scalarTimestampMs
            ? ' \u2014 read at ' +
            _vibFormatEventDateTime(eventBundle.scalarTimestampMs)
            : '');

    if (_vibIsFlagColumn(eventBundle.columnKey)) {
        var flagClass =
            displayText === 'Run'
                ? 'pm-vh-scalar-flag-run'
                : (
                    displayText === 'Stop'
                        ? 'pm-vh-scalar-flag-stop'
                        : 'pm-vh-scalar-flag-unknown'
                );

        return (
            '<td class="pm-vh-value-cell" title="' +
            _vibEsc(titleText) +
            '">' +
            '<div class="pm-vh-event-line pm-vh-scalar-line">' +
            '<span class="pm-vh-scalar-flag ' +
            flagClass +
            '">' +
            _vibEsc(displayText) +
            '</span>' +
            '</div>' +
            '</td>'
        );
    }

    return (
        '<td class="pm-vh-value-cell" title="' +
        _vibEsc(titleText) +
        '">' +
        '<div class="pm-vh-event-line pm-vh-scalar-line">' +
        '<span class="pm-vh-scalar-value">' +
        _vibEsc(displayText) +
        '</span>' +
        '</div>' +
        '</td>'
    );
}

    function _vibPickScalarForTimestamp(
        scalarItems,
        targetTimestampMs
    ) {
        if (
            !scalarItems ||
            !scalarItems.length ||
            targetTimestampMs === null ||
            targetTimestampMs === undefined
        ) {
            return null;
        }

        var best = null;
        var bestDifference = null;

        for (
            var i = 0;
            i < scalarItems.length;
            i++
        ) {
            var item = scalarItems[i];

            if (
                !item ||
                item.timestampMs === null ||
                item.timestampMs === undefined
            ) {
                continue;
            }

            var difference =
                Math.abs(
                    item.timestampMs -
                    targetTimestampMs
                );

            if (
                difference >
                PM_VIB_SCALAR_MATCH_TOLERANCE_MS
            ) {
                continue;
            }

            if (best === null) {
                best = item;
                bestDifference = difference;

                continue;
            }

            if (difference < bestDifference) {
                best = item;
                bestDifference = difference;

                continue;
            }

            if (difference !== bestDifference) {
                continue;
            }

            /*
             * Same distance: prefer the reading at or before the row.
             */
            var itemIsBefore =
                item.timestampMs <=
                targetTimestampMs;

            var bestIsBefore =
                best.timestampMs <=
                targetTimestampMs;

            if (itemIsBefore && !bestIsBefore) {
                best = item;

                continue;
            }

            if (!itemIsBefore && bestIsBefore) {
                continue;
            }

            /*
             * Still tied: prefer the plain value.
             */
            if (
                _vibScalarRank(item) <
                _vibScalarRank(best)
            ) {
                best = item;
            }
        }

        return best;
    }


    // ---- Helpers ----

    // Lookup an attribute's canonical display name via ASSET_INFO_MAP.
    // Matches both analog attrs (AssetAttributeId) and DataLogger attrs (Role).
    function _vibAttrName(assetId, attrId) {
        if (typeof ASSET_INFO_MAP === 'undefined') return '';
        var list = ASSET_INFO_MAP[String(assetId)] || ASSET_INFO_MAP[assetId] || [];
        for (var i = 0; i < list.length; i++) {
            var it = list[i];
            if (!it) continue;
            if (String(it.AssetAttributeId) === String(attrId) ||
                String(it.Role) === String(attrId)) {
                return it.AttributeName || it.CanonicalName || it.AliasName || '';
            }
        }
        return '';
    }

    // Normalise a raw attribute name into stable lower-case tokens.
    function _vibNormalize(name) {
        if (!name) return '';
        return String(name)
            .toLowerCase()
            .replace(/^vibration[_\s-]*sensor[_\s-]*/i, ' ')
            .replace(/[_\-()/.]+/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }

    function _vibDetectEnd(rawName) {
        var n = _vibNormalize(rawName);
        if (/\ba end\b/.test(n) || /\bend a\b/.test(n) || /\baend\b/.test(n)) return 'A';
        if (/\bb end\b/.test(n) || /\bend b\b/.test(n) || /\bbend\b/.test(n)) return 'B';
        if (/\s+a\s*$/.test(n)) return 'A';
        if (/\s+b\s*$/.test(n)) return 'B';
        return null;
    }

    function _vibStripEnd(rawName) {
        return _vibNormalize(rawName)
            .replace(/\ba end\b/g, ' ').replace(/\bb end\b/g, ' ')
            .replace(/\bend a\b/g, ' ').replace(/\bend b\b/g, ' ')
            .replace(/\baend\b/g, ' ').replace(/\bbend\b/g, ' ')
            .replace(/\s+a\s*$/, ' ').replace(/\s+b\s*$/, ' ')
            .replace(/\s+/g, ' ').trim();
    }

    // Resolve a column key. Prefer known AttrId map, fall back to name matcher.
    function _vibResolveColumn(attrId, rawName) {
    var numericId = parseInt(attrId, 10);

    // Primary binding: exact DB AssetAttribute.Id.
    if (!isNaN(numericId) && VIB_ATTR_ID_MAP[numericId]) {
        return VIB_ATTR_ID_MAP[numericId];
    }

    // Fallback only for future attributes whose IDs are not yet registered.
    var stripped = _vibStripEnd(rawName);

    for (var i = 0; i < VIB_COLUMNS.length; i++) {
        if (VIB_COLUMNS[i].match(stripped)) {
            return VIB_COLUMNS[i].key;
        }
    }

    return null;
}

    function _vibFmtVal(v) {
        if (v === null || v === undefined || v === '') return { text: '—', empty: true };
        var n = Number(v);
        if (isNaN(n)) return { text: String(v), empty: false };
        return { text: n.toFixed(3), empty: false };
    }
    function _vibFormatEventDateTime(timestampMs) {
        if (
            timestampMs === null ||
            timestampMs === undefined ||
            isNaN(timestampMs)
        ) {
            return '—';
        }

        var date = new Date(timestampMs);

        function pad(value) {
            return ('0' + value).slice(-2);
        }

        return (
            pad(date.getDate()) +
            '-' +
            pad(date.getMonth() + 1) +
            '-' +
            date.getFullYear() +
            ' ' +
            pad(date.getHours()) +
            ':' +
            pad(date.getMinutes()) +
            ':' +
            pad(date.getSeconds())
        );
    }

    function _vibFormatEventNumber(value) {
        if (
            value === null ||
            value === undefined ||
            isNaN(Number(value))
        ) {
            return '—';
        }

        var numericValue = Number(value);

        if (Math.abs(numericValue) >= 100) {
            return numericValue.toFixed(1);
        }

        return numericValue.toFixed(3);
    }
    function _vibFmtDate(ms) {
        if (!ms) return { d: '—', t: '' };
        var d = new Date(ms);
        if (isNaN(d.getTime()) || d.getFullYear() < 2000) return { d: '—', t: '' };
        var p = function (n) { return n < 10 ? '0' + n : n; };
        return {
            d: p(d.getDate()) + '/' + p(d.getMonth() + 1) + '/' + d.getFullYear(),
            t: p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds())
        };
    }

    function _vibEsc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }

    // Collect NWCR / RWCR pickup timestamps for an asset. Returns two
    // ascending-sorted arrays of milliseconds.
    function _vibCollectPickups(asset) {
        var norm = [], rev = [];
        var attrs = asset.attributes || [];
        for (var i = 0; i < attrs.length; i++) {
            var a = attrs[i];
            var id = parseInt(a.AttributeId, 10);
            if (id !== VIB_NWCR_ATTR_ID && id !== VIB_RWCR_ATTR_ID) continue;
            var vals = a.Values || {};
            for (var k in vals) {
                if (!vals.hasOwnProperty(k)) continue;
                var e = vals[k];
                if (!e || !e.Timestamp) continue;
                var ms = new Date(e.Timestamp.TimestampDevice).getTime();
                if (isNaN(ms) || ms <= 0) continue;
                var yr = new Date(ms).getFullYear();
                if (yr <= 1) continue;
                if (Number(e.Value) !== 1) continue;   // only "pickup" (1)
                (id === VIB_NWCR_ATTR_ID ? norm : rev).push(ms);
            }
        }
        norm.sort(function (a, b) { return a - b; });
        rev.sort(function (a, b) { return a - b; });
        return { normal: norm, reverse: rev };
    }

    // Any element of `sortedArr` within ±window of `ts`? Linear scan is fine
    // here — arrays are small (one entry per PM operation event).
    function _vibHasEventInWindow(sortedArr, ts, window) {
        for (var i = 0; i < sortedArr.length; i++) {
            if (Math.abs(sortedArr[i] - ts) <= window) return true;
        }
        return false;
    }
    // pointAssetId -> [trackAssetId, ...]
    var PM_TRACK_MAP = {};

    // trackAssetId -> AssetName (e.g. "103/104T"), read from
    // mPointTrackMappings. Used to label the TPR light with the Track's
    // name instead of its bare numeric id once it lights up.
    var PM_TRACK_NAME_MAP = {};

    // REPLACE WITH
    // trackAssetId -> sorted array of TPR drop timestamps (ms)
    var PM_VIB_TRACK_DROPS = {};

    // trackAssetId -> 'loading' | 'done' | 'error'
    var PM_VIB_TRACK_STATE = {};

    /*
     * The vibration fetch uses roletype=v (vibration-only), which excludes
     * the Combined-NWCR/RWCR DataLogger attributes the PT light needs
     * entirely -- they are never in that response, no matter which
     * attribute id is used to look for them. So they are fetched
     * separately here, exactly like the Track TPR fetch below.
     *
     * assetId (the Point Machine itself) -> { normal: [ms...], reverse: [ms...] }
     */
    var PM_VIB_OP_PICKUPS = {};

    // assetId -> 'loading' | 'done' | 'error'
    var PM_VIB_OP_STATE = {};

    // Date range of the last Search, reused for the background Track calls.
    var PM_VIB_LAST_RANGE = null;

    // Cancels stale background work when a new Search starts.
    var PM_VIB_TRACK_RUN_ID = 0;

    /*
     * +/- 1 minute. Used for the TPR light, the PT light, and the
     * Operation Type direction label -- all three now share this single
     * window (see VIB_OP_WINDOW_MS above for why it's no longer used).
     */
    var PM_VIB_CAUSE_WINDOW_MS = 60 * 1000;

    // Concurrent background Track requests.
    var PM_VIB_TRACK_CONCURRENCY = 2;

    function _vibResetTrackCauseCache() {
        PM_VIB_TRACK_DROPS = {};
        PM_VIB_TRACK_STATE = {};
        PM_VIB_OP_PICKUPS = {};
        PM_VIB_OP_STATE = {};
        PM_VIB_TRACK_RUN_ID++;
    }

    /*
     * Read mPointTrackMappings off one asset row of the GetAssestBy response.
     *
     * Row shape returned by the API:
     *
     *   { Id, AssetId (the Point Machine), TrackAssetId, AssetName, IsChecked }
     *
     * TrackAssetId is the Track. AssetId is the Point Machine itself, so it
     * must NOT be used as a fallback here. IsChecked is only meaningful in
     * the Site setup screen — the list endpoint returns it as false for every
     * row, so every returned row is treated as an active mapping.
     */
    function _vibCapturePointTrackMapping(assetRow) {
        if (!assetRow) {
            return;
        }

        var mappings =
            assetRow.mPointTrackMappings ||
            assetRow.MPointTrackMappings ||
            assetRow.PointTrackMappings ||
            null;

        if (!mappings || !mappings.length) {
            return;
        }

        var pointAssetId =
            String(
                assetRow.Id !== undefined
                    ? assetRow.Id
                    : assetRow.id
            );

        var trackIds = [];

        for (var i = 0; i < mappings.length; i++) {
            var mapping = mappings[i];

            if (!mapping) {
                continue;
            }

            var trackId =
                mapping.TrackAssetId !== undefined &&
                    mapping.TrackAssetId !== null
                    ? mapping.TrackAssetId
                    : (
                        mapping.trackAssetId !== undefined
                            ? mapping.trackAssetId
                            : null
                    );

            var numericTrackId =
                parseInt(trackId, 10);

            if (
                isNaN(numericTrackId) ||
                numericTrackId <= 0
            ) {
                continue;
            }

            /*
             * Guard against a self-reference: AssetId on the mapping row is
             * the Point Machine, never the Track.
             */
          if (
                String(numericTrackId) ===
                pointAssetId
            ) {
                continue;
            }

            var trackAssetName =
                mapping.AssetName !== undefined &&
                    mapping.AssetName !== null
                    ? mapping.AssetName
                    : (
                        mapping.assetName !== undefined
                            ? mapping.assetName
                            : ''
                    );

            if (trackAssetName) {
                PM_TRACK_NAME_MAP[String(numericTrackId)] =
                    trackAssetName;
            }

            if (
                trackIds.indexOf(
                    String(numericTrackId)
                ) === -1
            ) {
                trackIds.push(
                    String(numericTrackId)
                );
            }
        }

        if (trackIds.length) {
            PM_TRACK_MAP[pointAssetId] = trackIds;
        }
    }

    /*
     * DataLogger TPR AttributeId(s) of a Track asset.
     *
     * ASSET_INFO_MAP is keyed by asset id for the whole site, so the Track
     * asset is already in it. DataLogger entries carry RoleType 'd' and the
     * history AttributeId in Role.
     */
    function _vibGetTrackTprAttributeIds(trackAssetId) {
        var list =
            ASSET_INFO_MAP[String(trackAssetId)] ||
            ASSET_INFO_MAP[trackAssetId] ||
            [];

        var ids = [];

        for (var i = 0; i < list.length; i++) {
            var entry = list[i];

            if (
                !entry ||
                entry.RoleType !== 'd'
            ) {
                continue;
            }

            var names = [
                entry.AttributeName,
                entry.CanonicalName,
                entry.AliasName
            ];

            var isTpr = false;

            for (var n = 0; n < names.length; n++) {
                if (!names[n]) {
                    continue;
                }

                /*
                 * Whole-word TPR so RECR / HECR / DECR are not matched.
                 * Covers "TPR" and "TPR (Loc)".
                 */
                if (
                    /(^|[^A-Z0-9])TPR([^A-Z0-9]|$)/i.test(
                        String(names[n])
                    )
                ) {
                    isTpr = true;
                    break;
                }
            }

            if (!isTpr) {
                continue;
            }

            /*
             * The Track history response labels the TPR relay by its
             * AttributeId (e.g. 6), NOT by the relay Role number (e.g. 680).
             * ASSET_INFO_MAP carries both, and which one the API echoes
             * varies, so accept EITHER as a candidate id. Matching an extra
             * id is harmless -- the DataType gate in _vibExtractTprDrops is
             * what actually admits a value.
             *
             * Taking only Role is why drops[] came back empty and the TPR
             * light never switched on.
             */
            var candidateIds = [
                parseInt(entry.AssetAttributeId, 10),
                parseInt(entry.Role, 10)
            ];

            for (var ci = 0; ci < candidateIds.length; ci++) {
                var cid = candidateIds[ci];
                if (!isNaN(cid) && ids.indexOf(cid) === -1) {
                    ids.push(cid);
                }
            }
        }

        return ids;
    }

    /*
     * TPR drop timestamps from one Track history response.
     * A drop is a DataLogger TPR value of 0.
     */
    function _vibExtractTprDrops(
        trackAssetId,
        attributes
    ) {
        var tprIds =
            _vibGetTrackTprAttributeIds(
                trackAssetId
            );

        var drops = [];

        if (!tprIds.length) {
            /*
             * The Track has no DataLogger attribute named TPR in
             * ASSET_INFO_MAP, so nothing can be matched. Dump what the Track
             * does expose to make the misconfiguration obvious.
             */
            console.warn(
                '[TrackCause] Track ' +
                trackAssetId +
                ': no DataLogger TPR attribute found in ASSET_INFO_MAP. ' +
                'DataLogger attributes on this Track:',
                (
                    ASSET_INFO_MAP[String(trackAssetId)] ||
                    []
                )
                    .filter(function (entry) {
                        return entry &&
                            entry.RoleType === 'd';
                    })
                    .map(function (entry) {
                        return entry.Role +
                            ' = ' +
                            entry.AttributeName;
                    })
            );

            return drops;
        }

        if (
            !attributes ||
            !attributes.length
        ) {
            return drops;
        }

        for (
            var i = 0;
            i < attributes.length;
            i++
        ) {
            var attr = attributes[i];

            if (!attr) {
                continue;
            }

            var attrId =
                parseInt(
                    attr.AttributeId,
                    10
                );

            if (
                tprIds.indexOf(attrId) === -1
            ) {
                continue;
            }

            var values = attr.Values || {};

            for (var key in values) {
                if (
                    !values.hasOwnProperty(key)
                ) {
                    continue;
                }

                var entry = values[key];

                if (
                    !entry ||
                    !entry.Timestamp
                ) {
                    continue;
                }

                /*
                 * AttributeId 6 is shared by TWO attributes on a Track asset:
                 *   DataType "DataLogger" -> "TPR"    (relay, 0/1)  <-- this
                 *   DataType "RDPMS"      -> "TPR V"  (analog volts)
                 * Without this gate the analog series is also scanned, and a
                 * 0.0 V reading would be recorded as a relay drop.
                 */
                var entryDataType =
                    String(entry.DataType || '').toLowerCase();

                if (entryDataType !== 'datalogger') {
                    continue;
                }

                /*
                 * Drop only. A pickup (1) is the healthy state.
                 */
                if (Number(entry.Value) !== 0) {
                    continue;
                }

                var ms =
                    _vibValidTimestampMs(
                        entry.Timestamp.TimestampDevice
                    );

                if (ms === null) {
                    ms =
                        _vibValidTimestampMs(
                            entry.Timestamp.TimestampLocal
                        );
                }

                if (ms === null) {
                    continue;
                }

                drops.push(ms);
            }
        }

        drops.sort(
            function (left, right) {
                return left - right;
            }
        );

        return drops;
    }

    /*
     * Track ids needed by the vibration tables currently on screen.
     */
    function _vibCollectTrackIdsToAnalyse(assetResults) {
        var wanted = [];

        for (
            var i = 0;
            i < (assetResults || []).length;
            i++
        ) {
            var asset = assetResults[i];

            if (!asset) {
                continue;
            }

            var trackIds =
                PM_TRACK_MAP[
                String(asset.assetId)
                ] || [];

            for (
                var t = 0;
                t < trackIds.length;
                t++
            ) {
                var trackId = trackIds[t];

                if (
                    PM_VIB_TRACK_STATE[trackId]
                ) {
                    continue;
                }

                if (
                    wanted.indexOf(trackId) === -1
                ) {
                    wanted.push(trackId);
                }
            }
        }

        return wanted;
    }

    /*
     * Switch on the TPR light of every row whose timestamp sits within
     * PM_VIB_CAUSE_WINDOW_MS of a TPR drop on one of its Tracks.
     *
     * Runs against the DOM already on screen, so nothing is re-rendered.
     */
    function _vibApplyTrackCauseToDom() {
        var $lights =
            $('#divTelemetryHistory')
                .find('[data-vib-tpr]');

        if (!$lights.length) {
            return;
        }

        $lights.each(function () {
            var $light = $(this);

            var assetId =
                String(
                    $light.attr('data-vib-asset') || ''
                );

            var rowTimestamp =
                parseInt(
                    $light.attr('data-vib-ts'),
                    10
                );

            if (isNaN(rowTimestamp)) {
                return;
            }

            var trackIds =
                PM_TRACK_MAP[assetId] || [];

            if (!trackIds.length) {
                $light.attr(
                    'title',
                    'No Track is mapped to this Point Machine'
                );

                return;
            }

            var pending = false;
            var matchedTrackId = null;
            var matchedOffsetMs = null;

            for (
                var t = 0;
                t < trackIds.length;
                t++
            ) {
                var trackId = trackIds[t];
                var state = PM_VIB_TRACK_STATE[trackId];

                if (
                    state !== 'done' &&
                    state !== 'error'
                ) {
                    pending = true;

                    continue;
                }

                var drops =
                    PM_VIB_TRACK_DROPS[trackId] || [];

                for (
                    var d = 0;
                    d < drops.length;
                    d++
                ) {
                    var offset =
                        Math.abs(
                            drops[d] - rowTimestamp
                        );

                    if (
                        offset >
                        PM_VIB_CAUSE_WINDOW_MS
                    ) {
                        continue;
                    }

                    if (
                        matchedOffsetMs === null ||
                        offset < matchedOffsetMs
                    ) {
                        matchedOffsetMs = offset;
                        matchedTrackId = trackId;
                    }
                }
            }

            /*
             * The name line sits below .pm-vh-op-lights, as a sibling in
             * the same op cell -- not inside the TPR badge itself, so it
             * gets the full column width to truncate/center in rather than
             * fighting the PT/TPR badges for space.
             */
            var $nameLine =
                $light
                    .closest('.pm-vh-op-cell')
                    .find('.pm-vh-track-name');

            if (matchedTrackId !== null) {
                $light.addClass('on');

                /*
                 * Prefer the Track's AssetName (from mPointTrackMappings,
                 * e.g. "103/104T"); fall back to the raw id if the mapping
                 * response never carried a name for this Track.
                 */
                var matchedTrackName =
                    PM_TRACK_NAME_MAP[matchedTrackId] ||
                    ('Track ' + matchedTrackId);

                $light.attr(
                    'data-vib-track-name',
                    matchedTrackName
                );

                $nameLine
                    .text(matchedTrackName)
                    .attr('title', matchedTrackName);

                $light.attr(
                    'title',
                    'Track shorting — ' +
                    matchedTrackName +
                    ' TPR dropped ' +
                    Math.round(
                        matchedOffsetMs / 1000
                    ) +
                    ' second(s) from this vibration'
                );

                return;
            }

            $light.removeClass('on');

            $light.removeAttr('data-vib-track-name');

            $nameLine
                .text('')
                .attr('title', '');

            $light.attr(
                'title',
                pending
                    ? 'Checking related Track TPR…'
                    : (
                        'No Track TPR drop within 1 minute ' +
                        'before or after this vibration'
                    )
            );
        });
    }

    /*
     * Background entry point. Called after the vibration table is rendered.
     * Never blocks the table and never re-renders it.
     */
    function _vibStartTrackCauseAnalysis(assetResults) {
        if (!PM_VIB_LAST_RANGE) {
            return;
        }

        var trackIds =
            _vibCollectTrackIdsToAnalyse(
                assetResults
            );

        /*
         * Paint whatever is already cached (repeat searches, second render
         * of the same Search) before firing any request.
         */
        _vibApplyTrackCauseToDom();

        if (!trackIds.length) {
            return;
        }

        var runId = PM_VIB_TRACK_RUN_ID;
        var nextIndex = 0;
        var active = 0;

        trackIds.forEach(function (trackId) {
            PM_VIB_TRACK_STATE[trackId] = 'loading';
        });

        function pump() {
            if (runId !== PM_VIB_TRACK_RUN_ID) {
                return;
            }

            while (
                active < PM_VIB_TRACK_CONCURRENCY &&
                nextIndex < trackIds.length
            ) {
                fetchTrack(
                    trackIds[nextIndex]
                );

                nextIndex++;
            }
        }

        function finishTrack(trackId, drops, failed) {
            if (runId !== PM_VIB_TRACK_RUN_ID) {
                return;
            }

            PM_VIB_TRACK_DROPS[trackId] =
                drops || [];

            PM_VIB_TRACK_STATE[trackId] =
                failed ? 'error' : 'done';

            active--;

            _vibApplyTrackCauseToDom();

            pump();
        }

        function fetchTrack(trackId) {
            active++;

            $.ajax({
                url: '/FRS25/TelemetryHistory/GetHistoryData',
                type: 'GET',

                data: {
                    assetId: trackId,
                    startDate: PM_VIB_LAST_RANGE.startDate,
                    endDate: PM_VIB_LAST_RANGE.endDate
                },

                dataType: 'json',
                timeout: 60000
            }).done(function (response) {
                var data = [];

                if (
                    response &&
                    !response.error
                ) {
                    data =
                        (
                            response.Data ||
                            response.data
                        ) ||
                        (
                            Array.isArray(response)
                                ? response
                                : []
                        );
                }

                var drops =
                    _vibExtractTprDrops(
                        trackId,
                        data || []
                    );

                console.log(
                    '[TrackCause] Track ' +
                    trackId +
                    ': ' +
                    drops.length +
                    ' TPR drop(s) in range.'
                );

                finishTrack(
                    trackId,
                    drops,
                    false
                );
            }).fail(function () {
                console.warn(
                    '[TrackCause] Track ' +
                    trackId +
                    ': history request failed. ' +
                    'TPR light left off.'
                );

             finishTrack(
                    trackId,
                    [],
                    true
                );
            });
        }

        pump();
    }

    /*
     * Point Machine assetIds needed by the vibration tables currently on
     * screen -- one entry per distinct Point Machine displayed, skipping
     * any already resolved or in flight.
     */
    function _vibCollectPmAssetIdsToAnalyse(assetResults) {
        var wanted = [];

        for (
            var i = 0;
            i < (assetResults || []).length;
            i++
        ) {
            var asset = assetResults[i];

            if (!asset) {
                continue;
            }

            var assetId = String(asset.assetId);

            if (PM_VIB_OP_STATE[assetId]) {
                continue;
            }

            if (wanted.indexOf(assetId) === -1) {
                wanted.push(assetId);
            }
        }

        return wanted;
    }

    /*
     * Switch on the PT light -- and set the Operation Type label/tooltip --
     * of every row whose timestamp sits within PM_VIB_CAUSE_WINDOW_MS
     * (+/- 1 minute, same window as TPR) of a Combined-NWCR/RWCR pickup on
     * its own Point Machine.
     *
     * Runs against the DOM already on screen, so nothing is re-rendered.
     */
        function _vibApplyPmOperationToDom() {
        var $lights =
            $('#divTelemetryHistory')
                .find('[data-vib-pt]');

        if (!$lights.length) {
            return;
        }

        $lights.each(function () {
            var $light = $(this);

            var assetId =
                String(
                    $light.attr('data-vib-asset') || ''
                );

            var rowTimestamp =
                parseInt(
                    $light.attr('data-vib-ts'),
                    10
                );

            if (isNaN(rowTimestamp)) {
                return;
            }

            var state = PM_VIB_OP_STATE[assetId];

            if (
                state !== 'done' &&
                state !== 'error'
            ) {
                return;
            }

            var $cell =
                $light.closest('.pm-vh-op-cell');

            var $valueDiv =
                $cell.find('.pm-vh-op-value');

            var pickups =
                PM_VIB_OP_PICKUPS[assetId] ||
                { normal: [], reverse: [] };

            var matchedOperation =
                _vibFindNearestPmOperation(
                    pickups,
                    rowTimestamp,
                    PM_VIB_CAUSE_WINDOW_MS
                );

            if (matchedOperation) {
                $light.addClass('on');

                var operationLabel =
                    matchedOperation.direction === 'Reverse'
                        ? 'Reverse'
                        : 'Normal';

                var operationClass =
                    matchedOperation.direction === 'Reverse'
                        ? 'op-reverse'
                        : 'op-normal';

                $valueDiv
                    .attr(
                        'class',
                        'pm-vh-op-value ' + operationClass
                    )
                    .text(operationLabel);

                var operationTitle =
                    matchedOperation.relayName +
                    ' pickup ' +
                    Math.round(
                        matchedOperation.differenceMs / 1000
                    ) +
                    ' second(s) ' +
                    matchedOperation.relation +
                    ' vibration';

                $cell.attr('title', operationTitle);
                $light.attr('title', operationTitle);

                return;
            }

            $light.removeClass('on');

            $valueDiv
                .attr('class', 'pm-vh-op-value op-none')
                .text('—');

            var noneTitle =
                state === 'error'
                    ? 'Point Machine operation check failed for this range'
                    : (
                        'No NWCR/RWCR pickup found within ' +
                        '3 minutes before or after vibration'
                    );

            $cell.attr('title', noneTitle);
            $light.attr('title', noneTitle);
        });
    }

    /*
     * Background entry point for the PT light. Called after the vibration
     * table is rendered, alongside _vibStartTrackCauseAnalysis. Never
     * blocks the table and never re-renders it.
     *
     * Fetches each displayed Point Machine's own full history (no
     * roletype filter, unlike the vibration fetch) so the Combined-NWCR/
     * RWCR DataLogger pickups the PT light needs are actually present in
     * the response.
     */
    function _vibStartPmOperationAnalysis(assetResults) {
        if (!PM_VIB_LAST_RANGE) {
            return;
        }

        var assetIds =
            _vibCollectPmAssetIdsToAnalyse(
                assetResults
            );

        /*
         * Paint whatever is already cached (repeat searches, second render
         * of the same Search) before firing any request.
         */
        _vibApplyPmOperationToDom();

        if (!assetIds.length) {
            return;
        }

        var runId = PM_VIB_TRACK_RUN_ID;
        var nextIndex = 0;
        var active = 0;

        assetIds.forEach(function (assetId) {
            PM_VIB_OP_STATE[assetId] = 'loading';
        });

        function pump() {
            if (runId !== PM_VIB_TRACK_RUN_ID) {
                return;
            }

            while (
                active < PM_VIB_TRACK_CONCURRENCY &&
                nextIndex < assetIds.length
            ) {
                fetchPmAsset(
                    assetIds[nextIndex]
                );

                nextIndex++;
            }
        }

        function finishPmAsset(assetId, pickups, failed) {
            if (runId !== PM_VIB_TRACK_RUN_ID) {
                return;
            }

            PM_VIB_OP_PICKUPS[assetId] =
                pickups || { normal: [], reverse: [] };

            PM_VIB_OP_STATE[assetId] =
                failed ? 'error' : 'done';

            active--;

            _vibApplyPmOperationToDom();

            pump();
        }

        function fetchPmAsset(assetId) {
            active++;

            $.ajax({
                url: '/FRS25/TelemetryHistory/GetHistoryData',
                type: 'GET',

                data: {
                    assetId: assetId,
                    startDate: PM_VIB_LAST_RANGE.startDate,
                    endDate: PM_VIB_LAST_RANGE.endDate
                },

                dataType: 'json',
                timeout: 60000
            }).done(function (response) {
                var data = [];

                if (
                    response &&
                    !response.error
                ) {
                    data =
                        (
                            response.Data ||
                            response.data
                        ) ||
                        (
                            Array.isArray(response)
                                ? response
                                : []
                        );
                }

                var pickups =
                    _vibCollectPickups(
                        { attributes: data || [] }
                    );

                console.log(
                    '[PmOperation] Asset ' +
                    assetId +
                    ': ' +
                    pickups.normal.length +
                    ' Normal / ' +
                    pickups.reverse.length +
                    ' Reverse pickup(s) in range.'
                );

                finishPmAsset(
                    assetId,
                    pickups,
                    false
                );
            }).fail(function () {
                console.warn(
                    '[PmOperation] Asset ' +
                    assetId +
                    ': history request failed. ' +
                    'PT light left off.'
                );

                finishPmAsset(
                    assetId,
                    { normal: [], reverse: [] },
                    true
                );
            });
        }

        pump();
    }


    /*
 * Find the CR pickup nearest to the vibration TimestampDevice.
 *
 * A pickup is considered related to the vibration when it is:
 * - up to 3 minutes before the vibration; or
 * - up to 3 minutes after the vibration.
 *
 * When Normal and Reverse pickups are both present in the window,
 * the closest pickup is selected instead of always preferring Reverse.
 */
function _vibFindNearestPmOperation(
    pickups,
    vibrationTimestamp,
    windowMs
) {
    var nearest = null;

    function inspect(
        timestamps,
        direction,
        relayName
    ) {
        timestamps = timestamps || [];

        for (
            var i = 0;
            i < timestamps.length;
            i++
        ) {
            var pickupTimestamp =
                timestamps[i];

            var differenceMs =
                Math.abs(
                    pickupTimestamp -
                    vibrationTimestamp
                );

            if (differenceMs > windowMs) {
                continue;
            }

            /*
             * Select the closest CR pickup.
             *
             * If two pickups are equally close, use the later pickup
             * so that the result remains deterministic.
             */
            if (
                nearest === null ||
                differenceMs < nearest.differenceMs ||
                (
                    differenceMs ===
                        nearest.differenceMs &&
                    pickupTimestamp >
                        nearest.pickupTimestamp
                )
            ) {
                nearest = {
                    direction: direction,
                    relayName: relayName,
                    pickupTimestamp:
                        pickupTimestamp,
                    vibrationTimestamp:
                        vibrationTimestamp,
                    differenceMs:
                        differenceMs,

                    relation:
                        pickupTimestamp <
                        vibrationTimestamp
                            ? 'before'
                            : (
                                pickupTimestamp >
                                vibrationTimestamp
                                    ? 'after'
                                    : 'same'
                            )
                };
            }
        }
    }

    inspect(
        pickups && pickups.normal,
        'Normal',
        'NWCR'
    );

    inspect(
        pickups && pickups.reverse,
        'Reverse',
        'RWCR'
    );

    return nearest;
}



    function _vibCloneObject(source) {
    var target = {};
    source = source || {};

    for (var key in source) {
        if (source.hasOwnProperty(key)) {
            target[key] = source[key];
        }
    }

    return target;
}

function _vibValuesEqual(a, b) {
    if (
        (a === null || a === undefined || a === '') &&
        (b === null || b === undefined || b === '')
    ) {
        return true;
    }

    var aNumber = Number(a);
    var bNumber = Number(b);

    if (!isNaN(aNumber) && !isNaN(bNumber)) {
        return aNumber === bNumber;
    }

    return String(a) === String(b);
}

    /*
     * Waveform records used by vibration-table click handlers.
     *
     * Key format:
     *   assetId|end|columnKey|timestampDeviceMs
     */
var PM_VIB_EVENT_MAP = {};

    /*
     * Match Array, Avg and Max using TimestampDevice only.
     *
     * A narrow tolerance accommodates small persistence/API timing
     * differences without merging adjacent vibration events.
     */
var PM_VIB_DERIVED_MATCH_WINDOW_MS = 2000;

/*
 * A normal scalar value can arrive on a different device cycle.
 * It may be associated with a vibration event only within this window.
 */
var PM_VIB_SCALAR_MATCH_WINDOW_MS = 30000;


function _vibValidTimestampMs(value) {
    if (!value) {
        return null;
    }

    var ms = new Date(value).getTime();

    if (isNaN(ms) || ms <= 0) {
        return null;
    }

    var year = new Date(ms).getFullYear();

    return year > 1 ? ms : null;
}

/*
 * Used to group Array, Avg and Max from the same vibration event.
 *
 * TimestampEvent is preferred because all six derived tags are emitted
 * from the same processed VIB_TREND message.
 */
    /*
     * Single timestamp source for all vibration processing.
     *
     * Vibration Array, Avg, Max, table event datetime, scalar matching and
     * graph anchoring must use TimestampDevice only.
     *
     * No fallback to TimestampEvent, TimestampEdgeX or TimestampLocal is
     * allowed because mixing timestamp domains can associate Avg/Max with
     * the wrong waveform event.
     */
    function _vibGetDeviceTimestampMs(entry) {
        if (
            !entry ||
            !entry.Timestamp
        ) {
            return null;
        }

        return _vibValidTimestampMs(
            entry.Timestamp.TimestampDevice
        );
    }

function _vibResolveSyntheticAttributeId(attributeId) {
    var id = parseInt(attributeId, 10);

    if (isNaN(id)) {
        return null;
    }

    /*
     * Vibration synthetic IDs follow fixed 1000 offsets.
     */
    if (id >= 5000) {
        return {
            baseId: id - 5000,
            type: 'count'
        };
    }

    if (id >= 4000) {
        return {
            baseId: id - 4000,
            type: 'operationTime'
        };
    }

    if (id >= 3000) {
        return {
            baseId: id - 3000,
            type: 'max'
        };
    }

    if (id >= 2000) {
        return {
            baseId: id - 2000,
            type: 'min'
        };
    }

    if (id >= 1000) {
        return {
            baseId: id - 1000,
            type: 'avg'
        };
    }

    return {
        baseId: id,
        type: 'base'
    };
}

function _vibParseWaveformValues(rawValue) {
    if (Array.isArray(rawValue)) {
        return rawValue
            .map(Number)
            .filter(function (value) {
                return !isNaN(value);
            });
    }

    if (
        rawValue === null ||
        rawValue === undefined ||
        rawValue === ''
    ) {
        return [];
    }

    return String(rawValue)
        .split(',')
        .map(function (value) {
            return Number(String(value).trim());
        })
        .filter(function (value) {
            return !isNaN(value);
        });
}

function _vibIsWaveformValue(rawValue) {
    return (
        Array.isArray(rawValue) ||
        (
            typeof rawValue === 'string' &&
            rawValue.indexOf(',') !== -1
        )
    );
}


    /*
     * Finds one derived vibration record having the exact same
     * TimestampDevice as the waveform Array.
     *
     * No nearest-time or tolerance matching is used.
     */
    function _vibFindExactDeviceTimestampEntry(
        entries,
        timestampDeviceMs
    ) {
        if (
            !entries ||
            !entries.length ||
            timestampDeviceMs === null ||
            timestampDeviceMs === undefined
        ) {
            return null;
        }

        for (var i = 0; i < entries.length; i++) {
            var item = entries[i];

            if (
                !item ||
                item.used === true
            ) {
                continue;
            }

            if (
                item.timestampMs ===
                timestampDeviceMs
            ) {
                return item;
            }
        }

        return null;
    }

function _vibFindNearestEntry(
    entries,
    targetMs,
    timestampSelector,
    toleranceMs
) {
    if (!entries || !entries.length || targetMs === null) {
        return null;
    }

    var nearest = null;

    for (var i = 0; i < entries.length; i++) {
        var candidate = entries[i];

        if (!candidate || candidate.used === true) {
            continue;
        }

        var candidateMs =
            timestampSelector(candidate.entry);

        if (candidateMs === null) {
            continue;
        }

        var differenceMs =
            Math.abs(candidateMs - targetMs);

        if (differenceMs > toleranceMs) {
            continue;
        }

        if (
            nearest === null ||
            differenceMs < nearest.differenceMs ||
            (
                differenceMs === nearest.differenceMs &&
                candidateMs > nearest.timestampMs
            )
        ) {
            nearest = {
                item: candidate,
                timestampMs: candidateMs,
                differenceMs: differenceMs
            };
        }
    }

    return nearest;
}

function _vibCalculateArrayAverage(values) {
    if (!values || !values.length) {
        return null;
    }

    var total = 0;

    for (var i = 0; i < values.length; i++) {
        total += values[i];
    }

    return total / values.length;
}

function _vibCalculateArrayMax(values) {
    if (!values || !values.length) {
        return null;
    }

    return Math.max.apply(null, values);
}


  // Collect and group all vibration data for a single asset.
    // Rows keyed by (end, floor(ts/sec)) so simultaneous X/Y/Z readings
    // for the same end at the same second merge into a single row.
//    function _vibCollectAssetRows(asset) {
//    var attrs = asset.attributes || [];
//    var changes = [];

//    /*
//     * Build a flat chronological change list from the 11 configured
//     * vibration attributes.
//     *
//     * Binding priority:
//     * 1. AttributeId from history response.
//     * 2. GetBulkAssetMetadata title only for the A/B-end information.
//     * 3. Attribute-name matching only as a fallback.
//     */
//    for (var i = 0; i < attrs.length; i++) {
//        var a = attrs[i];

//        if (!a) {
//            continue;
//        }

//        var attrIdRaw = a.AttributeId;
//        var attrId = parseInt(attrIdRaw, 10);

//        /*
//         * Resolve the displayed table column primarily from the exact
//         * database AssetAttribute.Id.
//         */
//        var col = _vibResolveColumn(
//            attrIdRaw,
//            ''
//        );

//        /*
//         * Ignore all non-vibration attributes. Only IDs present in
//         * VIB_ATTR_ID_MAP or attributes matched by the fallback resolver
//         * will continue.
//         */
//        if (!col) {
//            continue;
//        }

//        var meta =
//            typeof _vibLookupMeta === 'function'
//                ? _vibLookupMeta(
//                    asset.assetId,
//                    attrIdRaw
//                )
//                : null;

//        var name =
//            meta && meta.title
//                ? meta.title
//                : _vibAttrName(
//                    asset.assetId,
//                    attrIdRaw
//                );

//        /*
//         * Resolve A End or B End from the metadata title.
//         */
//        var end =
//            meta && meta.title
//                ? _vibResolveEndFromTitle(
//                    meta.title
//                )
//                : _vibDetectEnd(name);

//        /*
//         * The currently configured vibration IDs belong to A End in the
//         * supplied metadata. Keep this fallback so the data is not discarded
//         * merely because the title suffix could not be parsed.
//         *
//         * When B-End vibration IDs are configured, their metadata title
//         * should contain "-B end" and the resolver above will return B.
//         */
//        if (
//            !end &&
//            !isNaN(attrId) &&
//            VIB_ATTR_ID_MAP[attrId]
//        ) {
//            end = 'A';
//        }

//        if (!end) {
//            continue;
//        }

//        var vals = a.Values || {};

//        for (var k in vals) {
//            if (!vals.hasOwnProperty(k)) {
//                continue;
//            }

//            var entry = vals[k];

//            if (
//                !entry ||
//                !entry.Timestamp
//            ) {
//                continue;
//            }

//            var timestampDevice =
//                entry.Timestamp.TimestampDevice;

//            var timestampMs =
//                new Date(
//                    timestampDevice
//                ).getTime();

//            /*
//             * Reject invalid/default timestamps.
//             */
//            if (
//                isNaN(timestampMs) ||
//                timestampMs <= 0
//            ) {
//                continue;
//            }

//            if (
//                new Date(
//                    timestampMs
//                ).getFullYear() < 2000
//            ) {
//                continue;
//            }

//            /*
//             * WebSocket/event waveform records can contain a comma-separated
//             * sample array, for example:
//             *
//             * "0.01,0.012,0.01,..."
//             *
//             * The history table requires one scalar value per column.
//             * Therefore, array/waveform entries must not overwrite the scalar
//             * MQTT/RDPMS value for that attribute.
//             */
//           /*
// * Vibration values must be selected by AttributeId and value shape.
// *
// * Do not reject a value only because DataType is "Unknown".
// * The History API can return valid scalar vibration values with
// * DataType = Unknown.
// *
// * Skip only comma-separated waveform/sample-array values.
// */
//var rawValue = entry.Value;

//var isCommaSeparatedWaveform =
//    typeof rawValue === 'string' &&
//    rawValue.indexOf(',') !== -1;

//if (
//    rawValue === null ||
//    rawValue === undefined ||
//    rawValue === '' ||
//    isCommaSeparatedWaveform
//) {
//    continue;
//}

//var numericValue = Number(rawValue);

//if (isNaN(numericValue)) {
//    continue;
//}

//            changes.push({
//                ts: timestampMs,
//                end: end,
//                col: col,
//                value: numericValue,
//                name: name || (
//                    'Attribute ' + attrId
//                ),
//                attrId: attrId,
//                valueId:
//                    entry.Id !== null &&
//                    entry.Id !== undefined
//                        ? Number(entry.Id)
//                        : 0
//            });
//        }
//    }

//    /*
//     * Oldest first because the latest-value state must be built
//     * chronologically.
//     */
//    changes.sort(function (a, b) {
//        if (a.ts !== b.ts) {
//            return a.ts - b.ts;
//        }

//        if (a.end !== b.end) {
//            return a.end < b.end
//                ? -1
//                : 1;
//        }

//        if (a.attrId !== b.attrId) {
//            return a.attrId - b.attrId;
//        }

//        return a.valueId - b.valueId;
//    });

//    /*
//     * The history API can return duplicate records having:
//     * - same AttributeId
//     * - same TimestampDevice
//     * - same Value
//     *
//     * but different TimestampLocal values.
//     *
//     * Such records must not produce duplicate table rows.
//     */
//    var uniqueChanges = [];
//    var seenChanges = {};

//    for (
//        var changeIndex = 0;
//        changeIndex < changes.length;
//        changeIndex++
//    ) {
//        var change = changes[changeIndex];

//        var duplicateKey =
//            change.end +
//            '|' +
//            change.col +
//            '|' +
//            change.ts +
//            '|' +
//            String(change.value);

//        if (seenChanges[duplicateKey]) {
//            continue;
//        }

//        seenChanges[duplicateKey] = true;
//        uniqueChanges.push(change);
//    }

//    /*
//     * Store the latest known value of every displayed vibration column
//     * independently for A End and B End.
//     */
//    var stateByEnd = {
//        A: {
//            values: {},
//            srcNames: {}
//        },
//        B: {
//            values: {},
//            srcNames: {}
//        }
//    };

//    var rows = [];
//    var index = 0;

//    /*
//     * Process all changes in TimestampDevice order.
//     *
//     * When multiple attributes have exactly the same TimestampDevice:
//     * - apply all changed values first;
//     * - then create only one combined snapshot row for that end.
//     */
//    while (index < uniqueChanges.length) {
//        var currentTimestamp =
//            uniqueChanges[index].ts;

//        var changedEnds = {};

//        while (
//            index < uniqueChanges.length &&
//            uniqueChanges[index].ts ===
//                currentTimestamp
//        ) {
//            var currentChange =
//                uniqueChanges[index];

//            var endState =
//                stateByEnd[currentChange.end];

//            var previousValue =
//                endState.values[
//                    currentChange.col
//                ];

//            var currentValue =
//                currentChange.value;

//            /*
//             * Create a row only when the value actually changes.
//             * Duplicate values at later local-processing timestamps do not
//             * create an additional row.
//             */
//            if (
//                !_vibValuesEqual(
//                    previousValue,
//                    currentValue
//                )
//            ) {
//                endState.values[
//                    currentChange.col
//                ] = currentValue;

//                endState.srcNames[
//                    currentChange.col
//                ] = currentChange.name;

//                changedEnds[
//                    currentChange.end
//                ] = true;
//            }

//            index++;
//        }

//        /*
//         * Generate one state snapshot per changed end.
//         *
//         * The snapshot contains:
//         * - the newly changed value;
//         * - all latest known values of the other vibration columns;
//         * - current TimestampDevice as the row timestamp.
//         */
//        var ends = ['A', 'B'];

//        for (
//            var endIndex = 0;
//            endIndex < ends.length;
//            endIndex++
//        ) {
//            var currentEnd =
//                ends[endIndex];

//            if (!changedEnds[currentEnd]) {
//                continue;
//            }

//            rows.push({
//                ts: currentTimestamp,
//                tsExact: currentTimestamp,
//                end: currentEnd,
//                values: _vibCloneObject(
//                    stateByEnd[
//                        currentEnd
//                    ].values
//                ),
//                srcNames: _vibCloneObject(
//                    stateByEnd[
//                        currentEnd
//                    ].srcNames
//                )
//            });
//        }
//    }

//    /*
//     * Display the latest TimestampDevice row first.
//     */
//    rows.sort(function (a, b) {
//        if (b.ts !== a.ts) {
//            return b.ts - a.ts;
//        }

//        return a.end < b.end
//            ? -1
//            : (
//                a.end > b.end
//                    ? 1
//                    : 0
//            );
//    });

//    return rows;
//}

    function _vibCollectAssetRows(asset) {
        var attrs = asset.attributes || [];
        var assetId = asset.assetId;

        /*
         * Events grouped by:
         *
         *   End | BaseAttributeId
         *
         * Array, Avg and Max are subsequently correlated using the exact
         * TimestampDevice.
         */
        var eventGroups = {};

        function getEventGroup(
            end,
            columnKey,
            baseAttributeId
        ) {
            var groupKey =
                end +
                '|' +
                baseAttributeId;

            if (!eventGroups[groupKey]) {
                eventGroups[groupKey] = {
                    end: end,
                    columnKey: columnKey,
                    baseAttributeId:
                        baseAttributeId,
                    arrays: [],
                    avgs: [],
                    maxes: [],

                    /*
                     * Plain values for Temperature and Motor Run Flag.
                     */
                    scalars: []
                };

            }

            return eventGroups[groupKey];
        }

        /*
         * Collect only DataType = Vibration records.
         */
        for (var attrIndex = 0;
            attrIndex < attrs.length;
            attrIndex++) {

            var attr = attrs[attrIndex];

            if (!attr) {
                continue;
            }

            var synthetic =
                _vibResolveSyntheticAttributeId(
                    attr.AttributeId
                );

            if (!synthetic) {
                continue;
            }

            var baseAttributeId =
                synthetic.baseId;

            var meta =
                _vibLookupMeta(
                    assetId,
                    baseAttributeId
                );

            var sourceTitle =
                meta && meta.title
                    ? meta.title
                    : (
                        attr.AttributeName ||
                        attr.Name ||
                        ''
                    );

            var end =
                _vibResolveEndFromTitle(
                    sourceTitle
                ) ||
                _vibDetectEnd(
                    sourceTitle
                );

            if (
                end !== 'A' &&
                end !== 'B'
            ) {
                continue;
            }

            var columnKey =
                _vibResolveColumn(
                    baseAttributeId,
                    sourceTitle
                );

            if (!columnKey) {
                continue;
            }

            var values =
                attr.Values || {};

            for (var valueKey in values) {
                if (
                    !values.hasOwnProperty(
                        valueKey
                    )
                ) {
                    continue;
                }

                var entry =
                    values[valueKey];

                if (
                    !entry ||
                    !entry.Timestamp
                ) {
                    continue;
                }

                /*
                 * This table must use only DataType = Vibration.
                 */
                var entryDataType =
                    String(
                        entry.DataType || ''
                    ).toLowerCase();

                var isScalarColumn =
                    _vibIsScalarColumn(
                        columnKey
                    );

                if (isScalarColumn) {
                    /*
                     * Temperature and Motor Run Flag arrive on the RDPMS
                     * packet, so DataType is "RDPMS" (or "Unknown" / blank)
                     * rather than "Vibration". They bind by AttributeId.
                     */
                    if (
                        !_vibIsScalarDataTypeAllowed(
                            entryDataType
                        )
                    ) {
                        continue;
                    }
                } else if (
                    entryDataType !== 'vibration'
                ) {
                    /*
                     * RoleType='v' tags (dataType "Vibration") are written
                     * ONLY when a real VIB_TREND message arrives. The 'c'
                     * twin sharing the same AttributeId (e.g. 710 CrestFactor,
                     * 720 PkAccel) is periodic RDPMS telemetry and must never
                     * mint a vibration row -- on live data those twins minted
                     * 148 of 150 rows.
                     */
                    /*
                     * Every waveform column must still use only
                     * DataType = Vibration.
                     */
                    continue;

                }

                /*
                 * TimestampDevice is the only timestamp allowed for
                 * vibration correlation and row creation.
                 */
                var timestampDeviceMs =
                    _vibGetDeviceTimestampMs(
                        entry
                    );

                if (
                    timestampDeviceMs === null
                ) {
                    continue;
                }

                var rawValue =
                    entry.Value;

                var eventGroup =
                    getEventGroup(
                        end,
                        columnKey,
                        baseAttributeId
                    );
     var eventItem = {
                    entry: entry,
                    value: rawValue,
                    timestampMs:
                        timestampDeviceMs,
                    sourceName:
                        sourceTitle,
                    synthType:
                        synthetic.type,
                    dataType:
                        entryDataType,
                    used: false
                };

                /*
                 * Temperature and Motor Run Flag carry only a value.
                 * No waveform Array, no Avg and no Max are transmitted for
                 * them, so they are collected separately and bound to the
                 * vibration rows afterwards.
                 */
                if (isScalarColumn) {
                    if (
                        rawValue === null ||
                        rawValue === undefined ||
                        rawValue === ''
                    ) {
                        continue;
                    }

                    eventGroup.scalars.push(
                        eventItem
                    );

                    continue;
                }

                /*
                 * Base attribute containing a comma-separated value is
                 * the waveform Array.
                 */

                if (
                    synthetic.type === 'base' &&
                    _vibIsWaveformValue(
                        rawValue
                    )
                ) {
                    eventGroup.arrays.push(
                        eventItem
                    );

                    continue;
                }

                if (
                    synthetic.type === 'avg'
                ) {
                    eventGroup.avgs.push(
                        eventItem
                    );

                    continue;
                }

                if (
                    synthetic.type === 'max'
                ) {
                    eventGroup.maxes.push(
                        eventItem
                    );
                }
            }
        }

        /*
         * One row is created for each unique waveform Array
         * TimestampDevice.
         *
         * A-End and B-End values sharing the same TimestampDevice are
         * placed in the same row.
         */
        var rowsByTimestamp = {};

        function getTimestampRow(
            timestampDeviceMs
        ) {
            var timestampKey =
                String(timestampDeviceMs);

            if (!rowsByTimestamp[timestampKey]) {
                rowsByTimestamp[timestampKey] = {
                    ts: timestampDeviceMs,

                    events: {
                        A: {},
                        B: {}
                    }
                };
            }

            return rowsByTimestamp[
                timestampKey
            ];
        }

        for (var groupKey in eventGroups) {

            if (
                !eventGroups.hasOwnProperty(
                    groupKey
                )
            ) {
                continue;
            }

            var group =
                eventGroups[groupKey];

            group.arrays.sort(
                function (left, right) {
                    return (
                        left.timestampMs || 0
                    ) - (
                            right.timestampMs || 0
                        );
                }
            );

            for (
                var arrayIndex = 0;
                arrayIndex <
                group.arrays.length;
                arrayIndex++
            ) {
                var arrayItem =
                    group.arrays[arrayIndex];

                var timestampDeviceMs =
                    arrayItem.timestampMs;

                var waveformValues =
                    _vibParseWaveformValues(
                        arrayItem.value
                    );

                if (
                    !waveformValues.length
                ) {
                    continue;
                }

                /*
                 * Avg and Max must have the exact same TimestampDevice
                 * as this waveform Array.
                 */
                var avgItem =
                    _vibFindExactDeviceTimestampEntry(
                        group.avgs,
                        timestampDeviceMs
                    );

                var maxItem =
                    _vibFindExactDeviceTimestampEntry(
                        group.maxes,
                        timestampDeviceMs
                    );

                /*
                 * Do not display an incomplete event.
                 *
                 * The requirement is to show backend-generated Max and
                 * Average belonging to the same Array TimestampDevice.
                 */
                if (
                    !avgItem ||
                    !maxItem
                ) {
                    continue;
                }

                avgItem.used = true;
                maxItem.used = true;

                var averageValue =
                    Number(
                        avgItem.value
                    );

                var maximumValue =
                    Number(
                        maxItem.value
                    );

                if (
                    isNaN(averageValue) ||
                    isNaN(maximumValue)
                ) {
                    continue;
                }

                var eventKey =
                    String(assetId) +
                    '|' +
                    group.end +
                    '|' +
                    group.baseAttributeId +
                    '|' +
                    timestampDeviceMs;

                var eventBundle = {
                    key: eventKey,
                    assetId: assetId,

                    assetName:
                        asset.assetName ||
                        (
                            'Asset ' +
                            assetId
                        ),

                    end:
                        group.end,

                    columnKey:
                        group.columnKey,

                    baseAttributeId:
                        group.baseAttributeId,

                    sourceName:
                        arrayItem.sourceName,

                    /*
                     * Array TimestampDevice.
                     */
                    timestampMs:
                        timestampDeviceMs,

                    /*
                     * Waveform Array having the exact same
                     * TimestampDevice as Avg and Max.
                     */
                    values:
                        waveformValues,

                    avg:
                        averageValue,

                    max:
                        maximumValue
                };

                var row =
                    getTimestampRow(
                        timestampDeviceMs
                    );

                row.events[
                    group.end
                ][
                    group.columnKey
                ] = eventBundle;

                PM_VIB_EVENT_MAP[
                    eventKey
                ] = eventBundle;
            }
        }

        /*
           * SCALAR BINDING (Temperature / Motor Run Flag).
           *
           * A scalar reading is CONTEXT for a vibration event, not an event.
           * Minting a row at the scalar's own TimestampDevice filled the table
           * with rows carrying nothing but a temperature -- no waveform, no
           * Max / Avg, no graph button -- which is not what this tab is for.
           *
           * This loop therefore binds ONLY into rows a real VIB_TREND waveform
           * already created; it never calls getTimestampRow, so no row can be
           * minted by a scalar.
           *
           * The match is nearest-reading-within-tolerance
           * (PM_VIB_SCALAR_MATCH_TOLERANCE_MS, 15 min) rather than exact
           * TimestampDevice: the periodic RDPMS scalar packet and the VIB_TREND
           * message do not share an eventTime, so exact matching would leave the
           * Temperature and Motor Run columns permanently empty.
           */
        var _waveformRowKeys = Object.keys(rowsByTimestamp);

        for (var scalarGroupKey in eventGroups) {
            if (!eventGroups.hasOwnProperty(scalarGroupKey)) { continue; }

            var scalarGroup = eventGroups[scalarGroupKey];

            if (!scalarGroup ||
                !scalarGroup.scalars ||
                !scalarGroup.scalars.length) { continue; }

            if (!_vibIsScalarColumn(scalarGroup.columnKey)) { continue; }

            for (var _rk = 0; _rk < _waveformRowKeys.length; _rk++) {
                var scalarRow = rowsByTimestamp[_waveformRowKeys[_rk]];
                if (!scalarRow) { continue; }

                if (!scalarRow.events[scalarGroup.end]) {
                    scalarRow.events[scalarGroup.end] = {};
                }

                // A real event at this timestamp already placed this column.
                if (scalarRow.events[scalarGroup.end][scalarGroup.columnKey]) {
                    continue;
                }

                var pickedScalar =
                    _vibPickScalarForTimestamp(
                        scalarGroup.scalars,
                        scalarRow.ts
                    );

                if (!pickedScalar) { continue; }

                scalarRow.events[scalarGroup.end][scalarGroup.columnKey] = {
                    key:
                        String(assetId) + '|' +
                        scalarGroup.end + '|' +
                        scalarGroup.baseAttributeId + '|' +
                        pickedScalar.timestampMs,

                    assetId: assetId,

                    assetName:
                        asset.assetName || ('Asset ' + assetId),

                    end: scalarGroup.end,
                    columnKey: scalarGroup.columnKey,
                    baseAttributeId: scalarGroup.baseAttributeId,
                    sourceName: pickedScalar.sourceName,

                    /*
                     * The ROW's event time, not the reading's. buildEndDateCell
                     * takes the newest timestampMs on an end, so carrying the
                     * scalar's own time here would drag that end's Date & Time
                     * up to 15 minutes past the vibration event. The reading's
                     * true time is kept alongside for the cell tooltip.
                     */
                    timestampMs: scalarRow.ts,
                    scalarTimestampMs: pickedScalar.timestampMs,

                    scalar: true,
                    value: pickedScalar.value,
                    dataType: pickedScalar.dataType
                };
            }
        }

        var rows = [];

        for (
            var timestampKey in
            rowsByTimestamp
        ) {
            if (
                rowsByTimestamp.hasOwnProperty(
                    timestampKey
                )
            ) {
                rows.push(
                    rowsByTimestamp[
                    timestampKey
                    ]
                );
            }
        }

        /*
         * Latest vibration TimestampDevice first.
         */
        rows.sort(
            function (left, right) {
                return right.ts - left.ts;
            }
        );

        return rows;
    }

    /*
    * Newest device timestamp among ONE end's events on a row -- the same value
    * buildEndDateCell renders, and the timestamp that end's Operation Type,
    * PT light and TPR light must be matched against. Null when the end is
    * empty on this row.
    */
    function _vibEndTs(row, endKey) {
        var evs = (row && row.events && row.events[endKey])
            ? row.events[endKey]
            : null;

        if (!evs) { return null; }

        var bestTs = null;

        for (var ek in evs) {
            if (!evs.hasOwnProperty(ek) || !evs[ek]) { continue; }

            var ets = evs[ek].timestampMs;
            if (ets === undefined || ets === null) { ets = evs[ek].ts; }
            if (ets === undefined || ets === null) { continue; }

            var ems = (typeof ets === 'number')
                ? ets
                : ((ets instanceof Date) ? ets.getTime() : new Date(ets).getTime());

            if (isNaN(ems)) { continue; }
            if (bestTs === null || ems > bestTs) { bestTs = ems; }
        }

        return bestTs;
    }

    /*
     * Does this row carry a genuine vibration EVENT on this end?
     *
     * A bound Temperature or Motor Run reading is context for an event, not an
     * event, so a row whose only entry for this end is a scalar does NOT belong
     * to that end's tab -- otherwise the B tab would list every A-End event
     * that happened to have a B-End temperature nearby.
     */
    function _vibRowHasEndEvent(row, endKey) {
        var evs = (row && row.events && row.events[endKey])
            ? row.events[endKey]
            : null;

        if (!evs) { return false; }

        for (var ek in evs) {
            if (!evs.hasOwnProperty(ek) || !evs[ek]) { continue; }
            if (evs[ek].scalar !== true) { return true; }
        }

        return false;
    }

    function _vibFilterRowsForEnd(rows, endKey) {
        var out = [];
        if (!rows) { return out; }

        for (var i = 0; i < rows.length; i++) {
            if (_vibRowHasEndEvent(rows[i], endKey)) { out.push(rows[i]); }
        }

        return out;
    }

    // ---- HTML builders ----

    /*
     * Header for ONE end. 14 columns:
     *
     *   Asset Name | Operation Type | Date & Time | 11 metrics
     *
     * The two-row shape is kept -- row 1 spans the end group so the tab and
     * the header agree on which end you are reading.
     */
    function _vibBuildTheadForEnd(endKey) {
        var endLabel = (endKey === 'B') ? 'B End' : 'A End';
        var h = '<thead>';

        h += '<tr>';

        h +=
            '<th rowspan="2" class="pm-vh-th-primary pm-vh-th-asset">' +
            'Asset Name' +
            '</th>';

        h +=
            '<th rowspan="2" class="pm-vh-th-primary pm-vh-th-op">' +
            'Operation Type' +
            '</th>';

        h +=
            '<th colspan="' + (VIB_COLUMNS.length + 1) + '" ' +
            'class="pm-vh-th-primary pm-vh-th-end-group">' +
            endLabel +
            '</th>';

        h += '</tr>';

        h += '<tr>';

        h +=
            '<th class="pm-vh-th-primary pm-vh-th-date">' +
            'Date &amp; Time' +
            '</th>';

        for (var i = 0; i < VIB_COLUMNS.length; i++) {
            h +=
                '<th class="pm-vh-th-primary pm-vh-th-metric" ' +
                'title="' + _vibEsc(VIB_COLUMNS[i].why) + '">' +
                VIB_COLUMNS[i].label +
                '</th>';
        }

        h += '</tr>';
        h += '</thead>';

        return h;
    }
    function _vibBuildRow(
        assetDisplayName,
        row,
        pickups,
        assetId,
        endKey
    ) {
        endKey = (endKey === 'B') ? 'B' : 'A';

        /*
         * This end's own device time. A and B fire ~1 s apart, so binding the
         * lights and the Operation Type to the row key would give both tabs the
         * A-End answer. Everything below hangs off endTs.
         */
        var endTs = _vibEndTs(row, endKey);
        if (endTs === null) { endTs = row.ts; }
        var dateParts =
            _vibFmtDate(
                row.ts
            );

     // REPLACE WITH
        /*
         * PT light = the vibration coincided with a Point Machine
         * NWCR/RWCR pickup (Normal/Reverse operation).
         *
         * `pickups` here comes from the vibration-role-only fetch and can
         * never contain Combined-NWCR/RWCR (see roletype=v note above
         * _vibCollectPickups), so it is not used to decide this at render
         * time. The Point Machine's own history is fetched separately in
         * the background and _vibApplyPmOperationToDom patches this light
         * plus the Operation Type label once that resolves -- exactly the
         * same pending-then-patch pattern the TPR light already uses.
         */
        var operationLabel = '—';
        var operationClass = 'op-none';
        var ptClass = 'pm-vh-light';

        var ptHooks =
            ' data-vib-pt="1"' +
            ' data-vib-asset="' +
            _vibEsc(
                assetId === undefined ||
                    assetId === null
                    ? ''
                    : assetId
            ) +
            '"' +
            ' data-vib-ts="' +
            _vibEsc(endTs) +
            '"' +
            ' data-vib-end="' + endKey + '"';

        /*
         * Existing TPR behaviour retained.
         */
        /*
         * TPR light = the vibration was caused by track shorting.
         *
         * It is switched on by _vibApplyTrackCauseToDom once the related
         * Track history has been fetched in the background, so it always
         * starts off here. The data attributes are what that pass matches on.
         */
        var tprClass = 'pm-vh-light';

        var tprHooks =
            ' data-vib-tpr="1"' +
            ' data-vib-asset="' +
            _vibEsc(
                assetId === undefined ||
                    assetId === null
                    ? ''
                    : assetId
            ) +
            '"' +
            ' data-vib-ts="' +
            _vibEsc(endTs) +
            '"' +
            ' data-vib-end="' + endKey + '"';


        var operationTitle =
            'Checking related Point Machine operation…';

        var h = '<tr>';

        /*
         * Asset Name appears once because A/B are now column groups.
         */
        h +=
            '<td class="pm-vh-asset-cell">' +
            '<div class="pm-vh-asset-name">' +
            '<i class="fas fa-cog"></i>' +
            '<strong>' +
            _vibEsc(
                assetDisplayName
            ) +
            '</strong>' +
            '</div>' +
            '</td>';

        /*
         * Row date is the waveform Array TimestampDevice.
         */
      h +=
          '<td class="pm-vh-op-cell" title="' +
            _vibEsc(
                operationTitle
            ) +
            '">' +

            '<div class="pm-vh-op-value ' +
            operationClass +
            '">' +
            operationLabel +
            '</div>' +

            '<div class="pm-vh-op-lights">' +

            '<span class="' +
            ptClass +
            '"' +
            ptHooks +
            ' title="Checking related Point Machine operation…">' +
            '<span class="pm-vh-light-dot"></span>' +
            'PT' +
            '</span>' +

            '<span class="' +
            tprClass +
            '"' +
            tprHooks +
            ' title="Checking related Track TPR…">' +
            '<span class="pm-vh-light-dot"></span>' +
            'TPR' +
            '</span>' +

            '</div>' +

            '<div class="pm-vh-track-name" title=""></div>' +

            '</td>';

        /*
         * Creates one Max / Avg / graph cell.
         */
       function buildEventCell(
            eventBundle
        ) {
            if (!eventBundle) {
                return (
                    '<td class="pm-vh-value-cell">' +
                    '<div class="pm-vh-event-line pm-vh-event-empty">' +
                    '&mdash;' +
                    '</div>' +
                    '</td>'
                );
            }

            /*
             * Temperature and Motor Run Flag: value only.
             */
            if (eventBundle.scalar === true) {
                return _vibBuildScalarCell(
                    eventBundle
                );
            }


            var maxText =
                _vibFormatEventNumber(
                    eventBundle.max
                );

            var avgText =
                _vibFormatEventNumber(
                    eventBundle.avg
                );

            return (
                '<td class="pm-vh-value-cell" title="' +
                _vibEsc(
                    eventBundle.sourceName || ''
                ) +
                '">' +

                '<button type="button" ' +
                'class="pm-vh-event-line pm-vh-event-btn" ' +
                'onclick="pmOpenVibrationHistoryGraph(\'' +
                _vibEsc(
                    eventBundle.key
                ) +
                '\')" ' +
                'title="Open vibration waveform">' +

                '<span class="pm-vh-event-values">' +

                '<span class="pm-vh-event-stat">' +
                '<strong>Max</strong>' +
                '<span>' +
                _vibEsc(
                    maxText
                ) +
                '</span>' +
                '</span>' +

                '<span class="pm-vh-event-stat">' +
                '<strong>Avg</strong>' +
                '<span>' +
                _vibEsc(
                    avgText
                ) +
                '</span>' +
                '</span>' +

                '</span>' +

                '<i class="fas fa-line-chart pm-vh-event-chart-icon"></i>' +

                '</button>' +

                '</td>'
            );
        }

      /*
       * Per-end date cell. A End uses the row's own key timestamp (the
       * waveform Array TimestampDevice the row was minted from). B End
       * uses the newest TimestampDevice among that row's B-End events,
       * which is the instant the B-End sensor actually reported; it may
       * differ from A End. Falls back to an em-dash when the end has no
       * events on this row.
       */
      function buildEndDateCell(endKey) {
          var evs = (row.events && row.events[endKey]) ? row.events[endKey] : null;
          var bestTs = null;
          if (evs) {
              for (var ek in evs) {
                  if (!evs.hasOwnProperty(ek) || !evs[ek]) { continue; }
                  /*
                   * Event bundles carry the device timestamp as `timestampMs`
                   * (already epoch ms) -- both the waveform bundle and the
                   * scalar (Temperature / Motor Run) bundle use that name.
                   * `.ts` does not exist on an event and always read undefined,
                   * which is why every end-date cell rendered "—".
                   */
                  var ets = evs[ek].timestampMs;
                  if (ets === undefined || ets === null) { ets = evs[ek].ts; }
                  if (ets === undefined || ets === null) { continue; }
                  var ems = (typeof ets === 'number')
                      ? ets
                      : ((ets instanceof Date) ? ets.getTime() : new Date(ets).getTime());
                  if (isNaN(ems)) { continue; }
                  if (bestTs === null || ems > bestTs) { bestTs = ems; }
              }
          }
          if (bestTs === null) {
              return '<td class="pm-vh-date-cell">' +
                  '<div class="pm-vh-event-line pm-vh-event-empty">&mdash;</div>' +
                  '</td>';
          }
          var dp = _vibFmtDate(new Date(bestTs));
          return '<td class="pm-vh-date-cell">' +
              '<span>' + dp.d + '</span>' +
              '<span>' + dp.t + '</span>' +
              '</td>';
      }

        /*
           * One end only -- this row belongs to the active tab, and the other
           * end's columns are not in this table at all.
           */
        h += buildEndDateCell(endKey);

        for (var cIndex = 0; cIndex < VIB_COLUMNS.length; cIndex++) {
            var columnKey = VIB_COLUMNS[cIndex].key;

            var eventBundle =
                (row.events && row.events[endKey])
                    ? row.events[endKey][columnKey]
                    : null;

            h += buildEventCell(eventBundle);
        }

        h += '</tr>';

        return h;
    }
    // Same Previous / current-page / Next pager (and loading rail) used by the
    // Operation and Indication tabs, keyed to the standard pagination_<idx> /
    // paginationUl_<idx> / showingRecords_<idx> / pageLoadBar_<idx> ids so the
    // shared _dashOverrideAssetPagerHtml / onThPageSizeChangeSel code drives it
    // without any vibration-specific branching.
    /*
      * Fully client-side: every page is already in memory, so unlike the
      * cursor-paged tabs the true total IS known and is shown.
      */
    function _vibPaginationBarHtml(idx, page, totalPages, totalRows, from, to, totalLabel) {
        var html = '';
        // totalLabel lets the caller pass a running "N+" total (cursor paging)
        // so the pager's "of Z" matches the header count exactly. Falls back to
        // the numeric total when not supplied.
        var _ofText = (totalLabel !== undefined && totalLabel !== null) ? totalLabel : totalRows;
        html += '<div class="px-2 pb-2 pt-1" id="pagination_' + idx + '">';
        html += '<div class="d-flex justify-content-between align-items-center flex-wrap">';
        html += '<div class="d-flex align-items-center flex-wrap">';
        html += '<div class="th-pagesize" style="display:inline-flex;align-items:center;margin-right:14px;">'
            + '<label style="font-size:12px;margin:0 6px 0 0;white-space:nowrap;">Rows / page</label>'
            + '<select class="form-control form-control-sm" style="width:auto;display:inline-block;" onchange="onVibPageSizeChange(this,' + idx + ')">'
            + _thPageSizeOptionsHtml((_vibGetState(_vibAssetIdByIdx(idx)) || {}).pageSize || _vibPageSize)
            + '</select></div>';
        html += '<div class="showing-records">'
            + (totalRows
                ? ('Showing ' + from + '&ndash;' + to + ' of ' + _ofText
                    + ' record' + (totalRows === 1 ? '' : 's'))
                : 'No records')
            + ' &middot; Page ' + page + ' of ' + totalPages + '</div>';
        html += '</div>';
        html += '<nav><ul class="pagination">';
        html += '<li class="page-item ' + (page <= 1 ? 'disabled' : '') + '">'
            + '<a class="page-link" href="javascript:void(0);"'
            + (page <= 1 ? '' : ' onclick="vibGotoPage(' + idx + ',' + (page - 1) + ')"')
            + '><i class="fas fa-chevron-left"></i> Previous</a></li>';
        html += '<li class="page-item active"><a class="page-link" '
            + 'style="cursor:default;pointer-events:none;">' + page + '</a></li>';
        html += '<li class="page-item ' + (page >= totalPages ? 'disabled' : '') + '">'
            + '<a class="page-link" href="javascript:void(0);"'
            + (page >= totalPages ? '' : ' onclick="vibGotoPage(' + idx + ',' + (page + 1) + ')"')
            + '>Next <i class="fas fa-chevron-right"></i></a></li>';
        html += '</ul></nav>';
        html += '</div></div>';
        return html;
    }
    /*
    * Tab strip for the ends this asset actually has. A single-end Point
    * Machine still gets its one tab: it names which end you are looking at,
    * which the old merged header only implied.
    */
    function _vibBuildEndTabsHtml(assetIdx, endsAvail, activeEnd, rowsByEnd, st) {
        if (!endsAvail || !endsAvail.length) { return ''; }

        var pending = !!(st && (st.walking || st.walkCapped));
        var h = '<div class="pm-vh-tabs" role="tablist">';

        for (var i = 0; i < endsAvail.length; i++) {
            var endKey = endsAvail[i];
            var isActive = (endKey === activeEnd);
            var count = (rowsByEnd[endKey] || []).length;

            h +=
                '<button type="button" role="tab"' +
                ' class="pm-vh-tab' + (isActive ? ' is-active' : '') + '"' +
                ' aria-selected="' + (isActive ? 'true' : 'false') + '"' +
                ' onclick="vibSetEnd(' + assetIdx + ',\'' + endKey + '\')">' +
                endKey + ' End' +
                '</button>';
        }

        h += '</div>';

        return h;
    }
    function _vibBuildAssetTable(asset, assetIdx) {
        // Cleared up-front: the early `return ''` paths below mean "no table
        // for this asset", and the report must see that as an absent key.
        delete PM_VIB_RENDERED_ENDS[String(asset.assetId)];

        var assetName = asset.assetName || ('Asset ' + asset.assetId);
        var displayName = (assetName.indexOf('PT-') === 0) ? assetName : 'PT-' + assetName;

        var pickups = _vibCollectPickups(asset);

        /*
        * Rows come from EVERY cached server page, filtered to genuine
        * vibration events by _vibCollectAssetRows, and are sliced into display
        * pages further down. A server page is a window of raw device
        * timestamps and carries no fixed number of events, so server page N
        * and display page N are unrelated -- slicing here is what makes the
        * count and "Page X of Y" describe rows the user can actually see.
        */
        var allRows = _vibCollectAllCachedRows(asset);

        // Which ends to bind, from GetUserAssetInfo, UNIONed with ends that
        // actually carry data on this page -- so wrong/missing metadata can
        // never hide an end that has real data.
        //
        // NOTE: _vibAssetEnds is declared one scope deeper than this function,
        // so the bare identifier is NOT in scope here -- `typeof _vibAssetEnds`
        // silently yields 'undefined' and this used to fall back to both-ends.
        // Always go through the window handle.
        var _vibEndsFn = (typeof window !== 'undefined' && window._vibAssetEnds)
            ? window._vibAssetEnds
            : ((typeof _vibAssetEnds === 'function') ? _vibAssetEnds : null);
        var _cfgEnds = _vibEndsFn
            ? _vibEndsFn(asset.assetId)
            : { a: true, b: true, any: true, known: false };
        var _dataHasA = false, _dataHasB = false;
        for (var _ri = 0; _ri < allRows.length; _ri++) {
            var _ev = allRows[_ri] && allRows[_ri].events;
            if (_ev) {
                if (_ev.A && Object.keys(_ev.A).length) { _dataHasA = true; }
                if (_ev.B && Object.keys(_ev.B).length) { _dataHasB = true; }
            }
            if (_dataHasA && _dataHasB) { break; }
        }
        var _showA, _showB;

        if (_cfgEnds.strict) {
            /*
             * VibrationA / VibrationB came from GetAssestBy, which is the asset
             * master: an end flagged null has no sensor, so it is never bound
             * even if this page carries stray rows for it. Both flags null ->
             * no vibration table for this Point Machine.
             */
            _showA = !!_cfgEnds.a;
            _showB = !!_cfgEnds.b;
            if (!_showA && !_showB) { return ''; }
        } else {
            _showA = _cfgEnds.a || _dataHasA;
            _showB = _cfgEnds.b || _dataHasB;
            // Config positively says this asset has NO vibration sensor and there
            // is no data either -> render nothing for it (skip the whole table).
            if (_cfgEnds.known && !_cfgEnds.any && !_dataHasA && !_dataHasB) {
                return '';
            }
            if (!_showA && !_showB) { return ''; }
        }

        var _ends = { a: _showA, b: _showB };

        // Published for the report -- see PM_VIB_RENDERED_ENDS.
        PM_VIB_RENDERED_ENDS[String(asset.assetId)] = _ends;

        // Per-asset paging state -- each table pages independently.
        var _vSt = _vibGetState(asset.assetId) || {};
        var _vSize = _vSt.pageSize || _vibPageSize;

        /*
         * ACTIVE END.
         *
         * Default to A when the asset has it, otherwise B. A stored choice that
         * this asset cannot honour (config changed, or the range holds no data
         * for that end) falls back rather than rendering an empty tab.
         */
        var _endsAvail = [];
        if (_ends.a) { _endsAvail.push('A'); }
        if (_ends.b) { _endsAvail.push('B'); }

        var _activeEnd = _vSt.end;
        if (_endsAvail.indexOf(_activeEnd) < 0) { _activeEnd = _endsAvail[0]; }
        _vSt.end = _activeEnd;

        /*
         * Row counts per end, for the tab labels. Cheap -- allRows is already
         * in memory and the filter is a single pass per end.
         */
        var _rowsByEnd = {};
        for (var _ei = 0; _ei < _endsAvail.length; _ei++) {
            _rowsByEnd[_endsAvail[_ei]] =
                _vibFilterRowsForEnd(allRows, _endsAvail[_ei]);
        }

        var endRows = _rowsByEnd[_activeEnd] || [];

        /*
         * COUNT / PAGER -- over the ACTIVE TAB's rows.
         *
         * Each end pages independently: switching tabs must not drop you on
         * page 7 of an end that only has three pages.
         */
        if (!_vSt.pageByEnd) { _vSt.pageByEnd = { A: 1, B: 1 }; }

        var totalRows = endRows.length;
        var totalPages = Math.max(1, Math.ceil(totalRows / _vSize));

        var _vPage = _vSt.pageByEnd[_activeEnd] || 1;
        if (_vPage > totalPages) { _vPage = totalPages; }
        if (_vPage < 1) { _vPage = 1; }
        _vSt.pageByEnd[_activeEnd] = _vPage;

        var _sliceStart = (_vPage - 1) * _vSize;
        var rows = endRows.slice(_sliceStart, _sliceStart + _vSize);

        var pageRowCount = rows.length;
        var showFrom = pageRowCount ? (_sliceStart + 1) : 0;
        var showTo = _sliceStart + pageRowCount;

        var _walkPending = !!_vSt.walking || !!_vSt.walkCapped;
        var _totalLabel = totalRows + (_walkPending ? '+' : '');
        var _vibPageForBar = _vPage;

        var head =
            '<div class="pm-vh-wrap" id="vibWrap_' + assetIdx + '">' +
            '<div class="pm-vh-head">' +
            '<h6>' +
            '<span class="pm-vh-title-icon"><i class="fas fa-area-chart"></i></span>' +
            _vibEsc(displayName) + ' — Vibration Data' +
            '</h6>' +
            _vibBuildEndTabsHtml(assetIdx, _endsAvail, _activeEnd, _rowsByEnd, _vSt) +
            '<span class="pm-vh-meta">' +
            (totalRows
                ? ('Showing ' + showFrom + '\u2013' + showTo +
                    ' of ' + _totalLabel + ' vibration event' +
                    (totalRows === 1 ? '' : 's'))
                : 'No vibration events') +
            (_vSt.walking ? ' \u00b7 scanning\u2026' : '') +
            '</span>' +            '</div>';

        // NO DATA: still render the real table with its column headers for the
        // end(s) this asset actually has a sensor on, plus one full-width
        // 'no data' row. The user can see WHICH attributes exist even when the
        // selected range returned nothing.
        if (rows.length === 0) {
            // Asset Name + Operation Type + Date & Time + 11 metrics.
            var _emptyCols = 3 + VIB_COLUMNS.length;

            return head +
                '<div class="pm-vh-scroll"><table class="pm-vh-table">' +
                _vibBuildTheadForEnd(_activeEnd) +
                '<tbody>' +
                '<tr><td colspan="' + _emptyCols + '" class="pm-vh-empty-row" ' +
                'style="text-align:center;padding:26px 12px;color:#6b7280;">' +
                '<i class="fas fa-area-chart" style="opacity:.45;margin-right:8px;"></i>' +
                'No vibration events for the selected date range' +
                '</td></tr>' +
                '</tbody></table></div></div>' +
                _vibPaginationBarHtml(assetIdx, 1, 1, 0, 0, 0);
        }

        var body = '<div class="pm-vh-scroll"><table class="pm-vh-table">' +
            _vibBuildTheadForEnd(_activeEnd) +
            '<tbody>';
        for (var i = 0; i < rows.length; i++) {
            body += _vibBuildRow(displayName, rows[i], pickups, asset.assetId, _activeEnd);
        }
        body += '</tbody></table></div></div>';

        // totalPages comes from the server (raw-page count) so "Page X of Y"
        // is stable and Next enables/disables correctly.
        return head + body + _vibPaginationBarHtml(
            assetIdx, _vibPageForBar, totalPages, totalRows, showFrom, showTo, _totalLabel);
    }

    // Renders inline. No metadata pre-fetch: _vibResolveColumn binds by
    // VIB_ATTR_ID_MAP (A end 691-700/719, B end 957-966/985) and the
    // DashboardHistory response already carries the attribute title in
    // columns[].name -> AttributeName (line 6263), which is all
    // _vibResolveEndFromTitle needs for the A/B end decision.
    function renderPmVibrationHistory(assetResults) {
        if (!assetResults || !assetResults.length) {
            $('#divTelemetryHistory').html(
                '<div class="pm-vh-empty">' +
                '<i class="fas fa-inbox"></i>' +
                '<strong>No data</strong>' +
                '<span>Run a search to load vibration history.</span>' +
                '</div>'
            );
            return;
        }

        /*
        * GetBulkAssetMetadata removed (site 161, 384 attributes, both ends):
        * end and column resolution were byte-identical with and without it,
        * and columns[].name was never blank. It cost 1,233 KB / ~4.0 s and
        * gated first paint. _vibLookupMeta at line 11753 now returns null and
        * falls through to attr.AttributeName, which is the same string.
      */

        _vibRenderInternal(assetResults);
    }

    /*
     * VIBRATION TAB ONLY -- fetch-all, then paginate client-side.
     *
     * The upstream mints a row whenever ANY visible column has an
     * observation. DashboardPMHistoryVibration's whitelist includes the
     * periodic RDPMS 'c' twins (710/711/712 CrestFactor, 720/721/722
     * PkAccel), so most rows are sensor chatter, not events: measured on
     * live data, 2 genuine events across 150 minted rows (~1.3%). Server
     * page N therefore has no relationship to display page N, and
     * totalRecords counts rows the grid never shows.
     *
     * Real events are only the RoleType='v' tags (dataType "Vibration"),
     * which _vibCollectAssetRows already isolates. Volumes are small
     * (~2300 minted rows => ~31 events), so ONE cursor walk at the
     * documented max tsLimit=500 costs ~5 requests per asset and then
     * every count is exact with zero reconciliation.
     *
     * Deliberately does NOT touch _dashServerPagerState: the vibration tab
     * opts out of server paging entirely.
     */
    var _vibPage = 1;          // display page = server page (1:1)
    var _vibPageSize = 50;          // DISPLAY rows per page (client-side slice)

    /*
     * Server fetch size, decoupled from the display page size. Display pages
     * are sliced out of the merged cache, so the fetch should use the
     * documented tsLimit maximum and cost the fewest round-trips. The
     * controller caps this at MaxTsLimit = 500.
     */
    var _VIB_WALK_PAGE_SIZE = 500;

    /*
     * Hard stop on the background walk: 40 x 500 = 20,000 raw device
     * timestamps per asset. Reaching it is rare (a very wide range); the pager
     * then shows the total as "N+" rather than claiming an exact figure.
     */
    var _VIB_WALK_MAX_PAGES = 40;
    var _vibIncrementalBound = false;   // reset each Search; true after first table binds
    var _vibAllAssetResults = null;   // ONE page's data, per asset (not all pages)

    // Per-search paging state. Vibration now uses NORMAL server pagination via
    // DashboardHistory&roletype=v (server returns ONLY vibration rows), so a
    // page is fetched on demand rather than walking the whole range up front.
    // Single-asset only (the vibration tab shows one PT at a time); the specs
    // array's first entry drives paging.
    /*
      * PER-ASSET paging state. Every selected Point Machine is fetched and
      * paged independently -- N assets means N cursors advancing at their own
      * rates, so a single shared cursor could never represent them all.
      *
      * _vibState[assetId] = {
      *     spec:         { assetId, startDate, endDate },
      *     cursorByPage: { 1:'' , 2:'<cursor>' , ... },
      *     page:         current page number,
      *     pageSize:     rows per page for THIS asset,
      *     hasNextPage:  from the last response,
      *     inFlight:     guards double-clicks,
      *     result:       the last fetched asset-result object
      * }
      */
    var _vibState = {};
    var _vibAssetOrder = [];          // render order, mirrors selection order

    /*
     * PM_VIB_RENDERED_ENDS[assetId] = { a: bool, b: bool } -- the end groups
     * _vibBuildAssetTable actually bound for that Point Machine, after the
     * config/data union and the strict VibrationA/VibrationB rules.
     *
     * The Excel / CSV / PDF report reads this so it emits the SAME column
     * groups the user is looking at. An absent key means the table rendered
     * nothing for that asset (no vibration sensor) -- the report skips it too.
     */
    var PM_VIB_RENDERED_ENDS = {};

    function _vibGetState(assetId) {
        return _vibState[String(assetId)] || null;
    }

    function _vibAssetIdByIdx(idx) {
        return _vibAssetOrder[idx];
    }

    // ROLE-FILTERED vibration fetch: DashboardHistory with roletype=v. If this
    // ever returns a shape without the vibration derived attrs, the table will
    // render blank -- the console line below is the 10-second check for that.
    // ROLE-FILTERED vibration fetch: DashboardHistory with roletype=v, ONE
    // asset per call. If this ever returns a shape without the vibration
    // derived attrs, the table renders blank -- the console line below is the
    // 10-second check for that.
    // Page cache + background prefetch aware. `background` fetches a page into
    // st.pageCache WITHOUT disturbing the currently-shown page, so Next can be
    // served from cache instantly while the user reads the current page.
    function _vibFetchPage(assetId, pageNum, onDone, background) {
        var st = _vibGetState(assetId);
        if (!st) { onDone(null); return; }
        if (!st.pageCache) { st.pageCache = {}; }

        // Served from cache -> instant page turn, no network round-trip.
        if (st.pageCache[pageNum]) {
            if (!background) {
                st.result = st.pageCache[pageNum];
                st.hasNextPage = !!st.pageCache[pageNum]._hasNextPage;
            }
            onDone(st.pageCache[pageNum]);
            return;
        }

        var spec = st.spec;
        var cursor = (st.cursorByPage[pageNum] != null) ? st.cursorByPage[pageNum] : '';
        if (!background) { st.inFlight = true; }
        $.ajax({
            url: '/FRS25/TelemetryHistory/GetHistoryDataDashboard',
            type: 'GET',
            dataType: 'json',
            data: {
                assetId: spec.assetId,
                startDate: spec.startDate,
                endDate: spec.endDate,
                page: pageNum,
                cursor: cursor,
                // VIBRATION must NOT gap-fill. One VIB_TREND event writes
                // Array+Avg+Min+Max+OperationTime atomically under one
                // TimestampDevice. With fillGaps=true that single real event is
                // carried forward onto every row minted by other tags, so an
                // A-End or B-End slot with no real observation at a timestamp
                // shows a phantom value. fillGaps=false returns only genuine
                // events, so each end binds strictly to the attribute value
                // present at that exact TimestampDevice.
                fillGaps: false,
                sort: 'desc',
                // Walk size, NOT the display page size -- display pages are
                // sliced client-side from the merged cache.
                pageSize: st.fetchSize || _VIB_WALK_PAGE_SIZE,
                roletype: 'v',   // server returns ONLY vibration rows
                attrIds: '697,719,963,985'   // union: Temperature + Motor Run Flag (RDPMS), A/B end
            },
            success: function (response) {
                if (!background) { st.inFlight = false; }
                var attrs = [];
                var hasNext = false;
                // Grand total for THIS window, straight from the server envelope
                // (roletype=v => one row per vibration event, so totalRecords IS
                // the true event count -- not the per-page count).
                var totalRecords = null;
                if (response && !response.error) {
                    var pageAttrs = getTelemetryHistoryNativeData(
                        response, spec.assetId, spec.startDate, spec.endDate,
                        'DashboardHistory');
                    for (var i = 0; i < pageAttrs.length; i++) { attrs.push(pageAttrs[i]); }
                    hasNext = !!response.hasNextPage;
                    if (typeof response.totalRecords === 'number' && response.totalRecords >= 0) {
                        totalRecords = response.totalRecords;
                    }
                    if (response.hasNextPage && response.nextCursor) {
                        st.cursorByPage[pageNum + 1] = response.nextCursor;
                    }
                    if (!attrs.length && response.rows && response.rows.length) {
                        console.warn('[Vibration] roletype=v returned rows but no vibration attributes were parsed for asset ' + spec.assetId + ' -- check response shape.');
                    }
                }
                var result = {
                    assetId: spec.assetId,
                    assetName: (typeof assetNameMap !== 'undefined' && assetNameMap[spec.assetId])
                        ? assetNameMap[spec.assetId]
                        : ('Asset ' + spec.assetId),
                    attributes: attrs,
                    pageTimestamps: (response && Array.isArray(response.pageTimestamps)) ? response.pageTimestamps : [],
                    serverPaging: true,
                    vibrationApi: true,
                    _hasNextPage: hasNext,
                    _totalRecords: totalRecords
                };
                st.pageCache[pageNum] = result;
                if (!background) {
                    st.result = result;
                    st.hasNextPage = hasNext;
                    if (totalRecords !== null) { st.totalRecords = totalRecords; }
                    // Server pages by RAW timestamps; keep its page count so the
                    // vibration pager can show a stable "Page X of Y".
                    if (response && typeof response.totalPages === 'number' && response.totalPages > 0) {
                        st.totalPages = response.totalPages;
                    }
                }
                onDone(result);
            },
            error: function () {
                if (!background) { st.inFlight = false; st.hasNextPage = false; }
                onDone(null);
            }
        });
    }

    // Re-render every asset table from the results currently held in state.
    function _vibRenderAll() {
        _vibRenderSlots();
        for (var i = 0; i < _vibAssetOrder.length; i++) {
            _vibBindOneAsset(_vibAssetOrder[i]);
        }
        _vibSyncMirrors();
        _vibScheduleBackgroundAnalysis(_vibCollectResults());
    }

    // ---- Slot model -------------------------------------------------------
    // #divTelemetryHistory holds exactly one <div id="vibSlot_<i>"> per entry
    // of _vibAssetOrder, created once per Search. A slot is filled, replaced or
    // emptied on its own; no code path rebuilds the whole container from a
    // partial result set, which is what previously left only one table.
    function _vibCollectResults() {
        var out = [];
        for (var i = 0; i < _vibAssetOrder.length; i++) {
            var st = _vibGetState(_vibAssetOrder[i]);
            if (st && st.result) { out.push(st.result); }
        }
        return out;
    }

    function _vibSyncMirrors() {
        var results = _vibCollectResults();
        lastAssetResults = results;
        _vibAllAssetResults = results;
        historyIncrementalRenderPendingResults = results;
        _historyIncrementalFirstRenderDone = true;
        return results;
    }

    function _vibSlotPlaceholder(assetId, message, spin) {
        var nm = (typeof assetNameMap !== 'undefined' && assetNameMap[assetId])
            ? assetNameMap[assetId] : ('Asset ' + assetId);
        if (String(nm).indexOf('PT-') !== 0) { nm = 'PT-' + nm; }
        return '<div class="pm-vh-wrap">' +
            '<div class="pm-vh-head"><h6>' +
            '<span class="pm-vh-title-icon"><i class="fas fa-area-chart"></i></span>' +
            _vibEsc(nm) + ' \u2014 Vibration Data' +
            '</h6></div>' +
            '<div class="pm-vh-empty">' +
            (spin ? '<i class="fas fa-spinner fa-spin"></i>' : '<i class="fas fa-area-chart"></i>') +
            '<span>' + _vibEsc(message) + '</span>' +
            '</div></div>';
    }

    // Create the empty slot skeleton for the current _vibAssetOrder.
    function _vibRenderSlots() {
        var html = '';
        for (var i = 0; i < _vibAssetOrder.length; i++) {
            html += '<div class="pm-vh-slot" id="vibSlot_' + i + '">' +
                _vibSlotPlaceholder(_vibAssetOrder[i], 'Loading vibration data\u2026', true) +
                '</div>';
        }
        $('#divTelemetryHistory').html(html);
        $('#divPagination').hide();
    }

    // Pagination render: swap ONLY the asset that paged, in place, instead of
    // rebuilding all tables (which froze the page on every Next click). The
    // other tables' DOM -- and their already-painted TPR/PT lights -- are left
    // untouched. The operation-type / TPR check for the new page is handed to
    // the existing idle scheduler, scoped to just this asset, so it runs in the
    // background and never blocks the click. Memoised GetHistoryData calls are
    // not repeated. Falls back to a full render if the DOM anchor is missing.
    // Pagination render: refill ONLY this asset's slot. Because the wrap and
    // its pager both live inside the slot, replacing the slot's html swaps
    // both at once -- no stale pager, no duplicate pagination bar.
    function _vibRenderOneAsset(assetId) {
        var st = _vibGetState(assetId);
        if (!st || !st.result) { return; }
        if (!_vibBindOneAsset(assetId)) { return; }
        _vibScheduleBackgroundAnalysis([st.result]);
    }

    // Bind ONE asset's table the moment its own response arrives -- appended in
    // selection order, WITHOUT re-rendering the tables already on screen. asset
    // 1 paints as soon as its response lands, asset 2 when its own lands, etc.
    // Fill ONE asset's slot. Never touches any other slot and never empties
    // the container. Returns true when a real table was rendered.
    function _vibBindOneAsset(assetId) {
        var idx = _vibAssetOrder.indexOf(String(assetId));
        if (idx < 0) {
            console.warn('[Vibration] bind skipped: ' + assetId + ' not in _vibAssetOrder');
            return false;
        }

        var $slot = $('#vibSlot_' + idx);
        if (!$slot.length) {
            // Slot skeleton missing (something else replaced the container).
            // Rebuild it, then restore every asset already fetched so this
            // recovery can never drop tables that were previously bound.
            _vibRenderSlots();
            for (var _k = 0; _k < _vibAssetOrder.length; _k++) {
                var _oid = _vibAssetOrder[_k];
                if (String(_oid) === String(assetId)) { continue; }
                var _ost = _vibGetState(_oid);
                if (!_ost || !_ost.result) { continue; }
                var _ohtml = _vibBuildAssetTable(_ost.result, _k);
                if (_ohtml) { $('#vibSlot_' + _k).html(_ohtml); }
            }
            $slot = $('#vibSlot_' + idx);
            if (!$slot.length) { return false; }
        }

        var st = _vibGetState(assetId);
        if (!st || !st.result) {
            $slot.html(_vibSlotPlaceholder(assetId,
                'No response for this asset \u2014 the history request failed.', false));
            _vibSyncMirrors();
            return false;
        }

        var html = _vibBuildAssetTable(st.result, idx);
        if (!html) {
            var _e = (typeof window !== 'undefined' && window._vibAssetEnds)
                ? window._vibAssetEnds(assetId) : null;
            console.warn('[Vibration] empty table for ' + assetId +
                ' ends=' + JSON.stringify(_e) +
                ' attrs=' + ((st.result.attributes || []).length));
            $slot.html(_vibSlotPlaceholder(assetId,
                'No vibration attributes available for this asset.', false));
            _vibSyncMirrors();
            return false;
        }

        $slot.html(html);
        _vibSyncMirrors();
        return true;
    }

    // Warm the NEXT page in the background so a later Next click is instant.
    // Fetches into st.pageCache only -- never disturbs the shown page.
    function _vibPrefetchNext(assetId) {
        var st = _vibGetState(assetId);
        if (!st || !st.hasNextPage) { return; }
        var next = (st.page || 1) + 1;
        if (st.cursorByPage[next] == null) { return; }
        if (st.pageCache && st.pageCache[next]) { return; }
        _vibFetchPage(assetId, next, function () { }, true);
    }
    /*
    * Walk the remaining cursor pages for one asset in the background.
    *
    * Display pages are slices of every cached page, so an exact "of N" is only
    * possible once the range has been pulled. Page 1 is already on screen when
    * this starts, so the walk is invisible: each completed page re-binds that
    * asset's table with a larger, exact total. Capped at _VIB_WALK_MAX_PAGES so
    * a very wide range cannot fire an unbounded request chain.
    */
    function _vibWalkRest(assetId) {
        var st = _vibGetState(assetId);
        if (!st || st.walking || st.walkDone) { return; }

        st.walking = true;

        function stop(s, capped) {
            s.walking = false;
            s.walkDone = true;
            s.walkCapped = !!capped;
            _vibRenderOneAsset(assetId);
        }

        function step(nextPage, pagesFetched) {
            var s = _vibGetState(assetId);
            if (!s) { return; }

            if (!s.hasNextPage || s.cursorByPage[nextPage] == null) {
                stop(s, false);
                return;
            }

            if (pagesFetched >= _VIB_WALK_MAX_PAGES) {
                stop(s, true);
                return;
            }

            _vibFetchPage(assetId, nextPage, function (res) {
                var s2 = _vibGetState(assetId);
                if (!s2) { return; }

                if (!res) { stop(s2, true); return; }

                // background:true leaves st.hasNextPage alone, so read it off
                // the cached page result instead.
                s2.hasNextPage = !!res._hasNextPage;

                // No re-render mid-walk: the slice on screen does not change,
                // and repainting would restart the PT / TPR analysis on every
                // page. stop() renders once when the walk completes.
                step(nextPage + 1, pagesFetched + 1);
            }, true);
        }

        step((st.page || 1) + 1, 0);
    }
    // Pager callbacks are keyed by TABLE INDEX (the pager markup only knows
    // its own index), which maps to an assetId via _vibAssetOrder.
    function vibGotoPage(assetIdx, n) {
        var assetId = _vibAssetIdByIdx(assetIdx);
        var st = _vibGetState(assetId);
        if (!st) { return; }
        if (n < 1) { n = 1; }
        // A display page is a slice of the merged cache, so turning a page is
        // no longer a request and needs no cursor guard.
        // _vibBuildAssetTable clamps n to the real page count.
        if (!st.pageByEnd) { st.pageByEnd = { A: 1, B: 1 }; }
        st.pageByEnd[st.end || 'A'] = n;
        _vibRenderOneAsset(assetId);
    }

    /*
     * Switch the active end tab. Each end keeps its own page, so coming back
     * lands where you left rather than on page 1.
     */
    function vibSetEnd(assetIdx, endKey) {
        if (endKey !== 'A' && endKey !== 'B') { return; }

        var assetId = _vibAssetIdByIdx(assetIdx);
        var st = _vibGetState(assetId);

        if (!st || st.end === endKey) { return; }

        st.end = endKey;
        _vibRenderOneAsset(assetId);
    }

    function onVibPageSizeChange(el, assetIdx) {
        var v = parseInt($(el).val(), 10);
        if (isNaN(v) || v <= 0) { return; }
        var assetId = _vibAssetIdByIdx(assetIdx);
        var st = _vibGetState(assetId);
        if (!st) { return; }
        st.pageSize = v;
        // Rows per page is shared by both tabs, so both restart at page 1.
        st.pageByEnd = { A: 1, B: 1 };
        // Rows per page no longer maps to a server request, so the cursor chain
        // and the page cache stay valid -- this is a pure re-slice.
        _vibRenderOneAsset(assetId);
    }

    /*
 * Background TPR / PM-CR analysis scheduler.
 *
 * _vibRenderInternal previously kicked both analyses off with
 * setTimeout(..., 0). That yields to the event loop but NOT to paint, so
 * the work landed in the same frame as the freshly built table: the two
 * Start functions each run a full [data-vib-tpr] / [data-vib-pt] DOM scan
 * over every rendered row, and on the first render of a Search they also
 * issue full-range GetHistoryData requests. The result was a visible
 * freeze on every Next click.
 *
 * Three changes, all scheduling only -- no analysis logic is altered:
 *   1. Debounced. Rapid Next clicks cancel the previous schedule, so
 *      paging through five pages runs the scan once, not five times.
 *   2. Delayed past paint, so the new page appears before any scanning.
 *   3. requestIdleCallback where available, so the scan waits for a free
 *      main thread instead of competing with rendering. The 2000 ms
 *      timeout guarantees it still runs on a busy page.
 *
 * The memoisation in PM_VIB_TRACK_STATE / PM_VIB_OP_STATE is untouched, so
 * network calls still happen at most once per Search per track/asset, and
 * cached lights still repaint via the _vibApply*ToDom calls at the top of
 * each Start function.
 */
    var _vibAnalysisTimer = null;

    function _vibScheduleBackgroundAnalysis(assetResults) {
        if (_vibAnalysisTimer) {
            clearTimeout(_vibAnalysisTimer);
            _vibAnalysisTimer = null;
        }

        function runAnalysis() {
            try {
                _vibStartTrackCauseAnalysis(assetResults);
            } catch (trackCauseError) {
                console.warn('[TrackCause] Analysis skipped:', trackCauseError);
            }

            try {
                _vibStartPmOperationAnalysis(assetResults);
            } catch (pmOperationError) {
                console.warn('[PmOperation] Analysis skipped:', pmOperationError);
            }
        }

        _vibAnalysisTimer = setTimeout(function () {
            _vibAnalysisTimer = null;

            if (typeof window.requestIdleCallback === 'function') {
                window.requestIdleCallback(runAnalysis, { timeout: 2000 });
            } else {
                runAnalysis();
            }
        }, 150);
    }

    function _vibRenderInternal(assetResults) {
        /*
         * Prevent stale graph buttons from opening waveform data belonging
         * to a previous search.
         */
        PM_VIB_EVENT_MAP = {};

        // No server-pager seeding: the vibration tab is client-paged from a
        // complete in-memory set, so there is no cursor state to sync with.
        _vibAllAssetResults = assetResults;

        // Render through the SAME slot model the incremental path uses, and
        // key every index to _vibAssetOrder so pager callbacks (vibGotoPage
        // uses the index) always map back to the right asset.
        if (!_vibAssetOrder || !_vibAssetOrder.length) {
            _vibAssetOrder = [];
            for (var _o = 0; _o < assetResults.length; _o++) {
                _vibAssetOrder.push(String(assetResults[_o].assetId));
            }
        }
        var byId = {};
        for (var _r = 0; _r < assetResults.length; _r++) {
            byId[String(assetResults[_r].assetId)] = assetResults[_r];
        }
        var html = '';
        for (var i = 0; i < _vibAssetOrder.length; i++) {
            var aid = _vibAssetOrder[i];
            var res = byId[String(aid)];
            html += '<div class="pm-vh-slot" id="vibSlot_' + i + '">' +
                (res ? _vibBuildAssetTable(res, i)
                    : _vibSlotPlaceholder(aid, 'No data for this asset.', false)) +
                '</div>';
        }
        $('#divTelemetryHistory').html(html);
        _vibIncrementalBound = true;

        // Pager markup is emitted fully populated by _vibBuildAssetTable --
        // no post-render override needed, and no server pager state exists
        // for this tab.

       /*
         * The vibration table is now on screen. Work out which rows were
         * caused by track shorting, and which coincide with a Point
         * Machine NWCR/RWCR pickup, in the background — the related
         * history is fetched afterwards and only the PT/TPR lights (plus
         * the Operation Type label) are patched, so neither Search nor
         * this render waits for it.
         */
        _vibScheduleBackgroundAnalysis(assetResults);

    }

    // Console debug helper — dumps mapping decisions for an asset so
    // WS→column mismatches can be diagnosed without code changes.
    window.debugVibHistoryMapping = function (assetId) {
        if (!lastAssetResults) { console.warn('No assetResults loaded'); return; }
        var asset = null;
        for (var i = 0; i < lastAssetResults.length; i++) {
            if (String(lastAssetResults[i].assetId) === String(assetId)) { asset = lastAssetResults[i]; break; }
        }
        if (!asset) { console.warn('Asset', assetId, 'not found'); return; }
        console.group('[Vib History Debug] Asset', assetId, asset.assetName || '');
        var out = [];
        console.log('Bulk metadata loaded for site:', PM_VIB_BULK_LOADED_SITE);
        console.log('Assets with vibration entries:', Object.keys(PM_VIB_BULK_META).length);
        (asset.attributes || []).forEach(function (a) {
            var meta = (typeof _vibLookupMeta === 'function')
                ? _vibLookupMeta(assetId, a.AttributeId) : null;
            var bulkTitle = meta ? meta.title : '';
            var name = bulkTitle || _vibAttrName(assetId, a.AttributeId);
            var lower = (name || '').toLowerCase();
            var isNamed =
                lower.indexOf('vibration') !== -1 ||
                lower.indexOf('accel') !== -1 ||
                /\bvib\b/.test(lower);
            if (!bulkTitle && !isNamed) return;
            var resolvedEnd = bulkTitle ? _vibResolveEndFromTitle(bulkTitle) : _vibDetectEnd(name);
            var resolvedCol = null;
            if (name) {
                var strippedD = _vibStripEnd(name);
                for (var vi = 0; vi < VIB_COLUMNS.length; vi++) {
                    if (VIB_COLUMNS[vi].match(strippedD)) { resolvedCol = VIB_COLUMNS[vi].key; break; }
                }
            }
            out.push({
                attrId: a.AttributeId,
                name: name || '(no name)',
                bulkTitleFound: !!bulkTitle,
                end: resolvedEnd || '',
                column: resolvedCol || '',
                sampleCount: Object.keys(a.Values || {}).length
            });
        });
        console.table(out);
        console.groupEnd();
        return out;
    };


/* ===== VIBRATION HISTORY EXPORT (vibration-only subset of the source block) ===== */

    // ============================================================================
    // VIBRATION HISTORY EXPORT
    //
    // Isolated from the existing Operational and Indication export implementation.
    // Uses the same _vibCollectAssetRows() model used by the vibration table.
    // ============================================================================

    function _vibIsExportMode() {
        return (
            pmViewMode === 'vibration' &&
            selectedViewType === 'Table'
        );
    }

    /*
     * The report is the union of every page the table pages through, so it can
     * only match the screen once the background walk has finished. While any
     * asset is still walking, the merged cache is short of rows the table will
     * shortly count -- downloading then produces a report that disagrees with
     * the total on screen.
     *
     * Returns the number of assets still walking; 0 means the report and the
     * table describe the same rows.
     */
    function _vibExportPendingAssets() {
        var pending = 0;

        for (var i = 0; i < _vibAssetOrder.length; i++) {
            var st = _vibGetState(_vibAssetOrder[i]);
            if (st && st.walking) { pending++; }
        }

        return pending;
    }
    function _vibExportCappedAssets() {
        for (var i = 0; i < _vibAssetOrder.length; i++) {
            var st = _vibGetState(_vibAssetOrder[i]);
            if (st && st.walkCapped) { return true; }
        }
        return false;
    }
    /*
  * Nearest Track TPR drop to a vibration timestamp, mirroring the on-screen
  * TPR light logic in _vibApplyTrackCauseToDom(). Returns the matched Track
  * (id + name + offset) or null when no drop falls inside the window.
  */
    function _vibFindNearestTrackDrop(
        assetId,
        vibrationTimestamp,
        windowMs
    ) {
        var trackIds = PM_TRACK_MAP[String(assetId)] || [];

        if (!trackIds.length) {
            return null;
        }

        var matchedTrackId = null;
        var matchedOffsetMs = null;

        for (var t = 0; t < trackIds.length; t++) {
            var trackId = trackIds[t];
            var drops = PM_VIB_TRACK_DROPS[trackId] || [];

            for (var d = 0; d < drops.length; d++) {
                var offset = Math.abs(drops[d] - vibrationTimestamp);

                if (offset > windowMs) {
                    continue;
                }

                if (matchedOffsetMs === null || offset < matchedOffsetMs) {
                    matchedOffsetMs = offset;
                    matchedTrackId = trackId;
                }
            }
        }

        if (matchedTrackId === null) {
            return null;
        }

        return {
            trackId: matchedTrackId,
            trackName:
                PM_TRACK_NAME_MAP[matchedTrackId] ||
                ('Track ' + matchedTrackId),
            offsetMs: matchedOffsetMs
        };
    }

    /*
     * Operation Type / PT / TPR text for one vibration row, matching what the
     * on-screen table shows once its background analysis has completed.
     *
     *   Point Machine CR operation (NWCR/RWCR pickup near the vibration):
     *       Operation Type = Point Machine direction (Normal / Reverse)
     *       PT             = 'Pickup'
     *
     *   Track shorting (TPR drop near the vibration):
     *       Operation Type = 'Track Shorting'
     *       PT             = '<Track asset name> Drop'
     *       TPR            = '<Track asset name>'
     *
     * The same +/- 1-minute window (PM_VIB_CAUSE_WINDOW_MS) used by the table's
     * PT and TPR lights is used here so the report and the screen agree.
     */
    function _vibGetExportOperation(
        asset,
        timestampDeviceMs
    ) {
        var assetId = String(asset.assetId);

        /*
         * PREFERRED: read exactly what the table shows for this row. The
         * Operation Type / PT / TPR values are patched into the DOM by
         * _vibApplyPmOperationToDom / _vibApplyTrackCauseToDom once the
         * background analysis resolves, so reading them back guarantees the
         * report matches the screen regardless of cache timing or keying.
         */
        var fromDom =
            _vibReadOperationFromDom(assetId, timestampDeviceMs);

        if (fromDom) {
            return fromDom;
        }

        /*
         * FALLBACK (row not currently rendered): compute from the same caches
         * the table uses. Point Machine operation pickups are fetched in the
         * background into PM_VIB_OP_PICKUPS (the vibration roletype=v fetch
         * never carries the Combined-NWCR/RWCR attributes); fall back to the
         * asset's own attributes only when that cache is not yet populated.
         */
        var pickups =
            PM_VIB_OP_PICKUPS[assetId] ||
            _vibCollectPickups(asset);

        var matchedOperation =
            _vibFindNearestPmOperation(
                pickups,
                timestampDeviceMs,
                PM_VIB_CAUSE_WINDOW_MS
            );

        var trackDrop =
            _vibFindNearestTrackDrop(
                assetId,
                timestampDeviceMs,
                PM_VIB_CAUSE_WINDOW_MS
            );

        var operation = '';
        var pt = '';
        var tpr = '';

        if (matchedOperation) {
            // Point Machine CR operation -> update PT only.
            operation = matchedOperation.direction || 'Normal';

            var relayName = matchedOperation.relayName || '';
            var relation = matchedOperation.relation || '';

            pt = (relayName ? relayName + ' - ' : '') +
                'Pickup' +
                (relation ? ' (' + relation + ')' : '');
        } else if (trackDrop) {
            // TPR drop (track shorting) -> update TPR only.
            operation = 'Track Shorting';
            tpr = trackDrop.trackName + ' Drop';
        }

        return {
            operation: operation,
            pt: pt,
            tpr: tpr
        };
    }




    /*
 * Read the Operation Type / PT / TPR a rendered vibration row is currently
 * showing, keyed by the same data-vib-asset / data-vib-ts hooks the table
 * writes onto its PT and TPR lights. Returns null when the row is not on
 * screen (e.g. a different page), so the caller can fall back to the cache.
 */
    function _vibReadOperationFromDom(assetId, timestampDeviceMs) {
        if (typeof $ !== 'function') {
            return null;
        }

        var $scope = $('#divTelemetryHistory');
        if (!$scope.length) {
            return null;
        }

        var selector =
            '[data-vib-asset="' + assetId + '"]' +
            '[data-vib-ts="' + timestampDeviceMs + '"]';

        var $hook = $scope.find(selector).first();
        if (!$hook.length) {
            return null;
        }

        var $cell = $hook.closest('.pm-vh-op-cell');
        if (!$cell.length) {
            return null;
        }

        var opText =
            $.trim($cell.find('.pm-vh-op-value').first().text() || '');

        if (opText === '\u2014' || opText === '-') {
            opText = '';
        }

        var $pt = $cell.find('[data-vib-pt]').first();
        var $tpr = $cell.find('[data-vib-tpr]').first();

        var ptOn = $pt.hasClass('on');
        var tprOn = $tpr.hasClass('on');

        /*
         * Analysis has not resolved this row yet: the hooks are rendered
         * up-front with both lights off and the label at "—", so reading them
         * back would export blanks while off-screen rows -- which have no hook
         * and use the cache path -- come out populated. Fall through instead.
         */
        if (!ptOn && !tprOn && !opText) {
            return null;
        }

        /*
         * Background analysis has not resolved this row yet. The PT / TPR
         * hooks are rendered up-front with both lights off and the label at
         * "—" (see ptHooks / tprHooks in _vibBuildRow), so reading them back
         * here would export an empty Operation Type / PT / TPR while the
         * off-screen rows -- which have no hook and fall through to the
         * cache path -- come out populated. Return null so this row uses the
         * same cache path.
         */
        if (!ptOn && !tprOn && !opText) {
            return null;
        }

        var trackName =
            $.trim($cell.find('.pm-vh-track-name').first().text() || '') ||
            $.trim($tpr.attr('data-vib-track-name') || '');

        var operation = '';
        var pt = '';
        var tpr = '';

        if (ptOn) {
            // Point Machine CR operation -> update PT only.
            operation = opText || 'Normal';

            var ptTitle = $.trim($pt.attr('title') || '');
            var domRelay = '';
            var domRelation = '';

            var relayMatch = ptTitle.match(/^(NWCR|RWCR)\b/i);
            var relationMatch = ptTitle.match(/\b(before|after|same)\b/i);

            if (relayMatch) { domRelay = relayMatch[1].toUpperCase(); }
            if (relationMatch) { domRelation = relationMatch[1].toLowerCase(); }

            pt = (domRelay ? domRelay + ' - ' : '') +
                'Pickup' +
                (domRelation ? ' (' + domRelation + ')' : '');
        } else if (tprOn) {
            // Track shorting -> update TPR only.
            operation = 'Track Shorting';
            tpr = trackName ? (trackName + ' Drop') : 'Drop';
        } else if (opText) {
            // Direction resolved but PT hook not flagged; still show it.
            operation = opText;
        }

        return {
            operation: operation,
            pt: pt,
            tpr: tpr
        };
    }

    /*
     * Format one vibration event for report cells.
     */
    function _vibGetExportEventText(
        eventBundle
    ) {
        if (!eventBundle) {
            return '';
        }

        /*
         * Temperature and Motor Run Flag export as a single value, matching
         * how they are shown on screen.
         */
        if (eventBundle.scalar === true) {
            return _vibFormatScalarValue(
                eventBundle
            );
        }

        var maxText =
            _vibFormatEventNumber(
                eventBundle.max
            );

        var avgText =
            _vibFormatEventNumber(
                eventBundle.avg
            );

        return (
            'Max: ' +
            maxText +
            ' | Avg: ' +
            avgText
        );
    }

    /*
     * Build the common export header.
     *
     * The same columns are used by Excel, CSV and PDF so all three reports
     * remain aligned with the vibration table.
     */
    /*
 * The on-screen table renders VIB_COLUMNS[i].label as HTML, so its "<br/>"
 * shows as a line break (e.g. "X Full Band RMS Accel (G)"). Reports are
 * plain text, so strip the markup here to bind the SAME attribute name the
 * user sees in the table instead of a literal "<br/>".
 */
    function _vibCleanLabel(label) {
        return String(label == null ? '' : label)
            .replace(/<br\s*\/?>/gi, ' ')   // on-screen line break
            .replace(/<[^>]*>/g, ' ')       // any other stray markup
            .replace(/&amp;/g, '&')
            .replace(/&nbsp;/gi, ' ')
            .replace(/\s+/g, ' ')
            .trim();
    }
    /*
     * Newest device timestamp among one end's events on a row, formatted for a
     * plain-text report. Mirrors buildEndDateCell() inside _vibBuildRow: event
     * bundles carry the device time as `timestampMs` -- the waveform bundle AND
     * the scalar (Temperature / Motor Run) bundle both use that name, while
     * `.ts` does not exist on an event and is only a defensive fallback.
     * Returns '-' when this end has no events on the row.
     */
    function _vibGetExportEndDateText(row, endKey) {
        var evs = (row && row.events && row.events[endKey]) ? row.events[endKey] : null;
        var bestTs = null;
        if (evs) {
            for (var ek in evs) {
                if (!evs.hasOwnProperty(ek) || !evs[ek]) { continue; }
                var ets = evs[ek].timestampMs;
                if (ets === undefined || ets === null) { ets = evs[ek].ts; }
                if (ets === undefined || ets === null) { continue; }
                var ems = (typeof ets === 'number')
                    ? ets
                    : ((ets instanceof Date) ? ets.getTime() : new Date(ets).getTime());
                if (isNaN(ems)) { continue; }
                if (bestTs === null || ems > bestTs) { bestTs = ems; }
            }
        }
        if (bestTs === null) { return '-'; }
        var dp = _vibFmtDate(new Date(bestTs));
        return (dp.d + ' ' + dp.t).trim();
    }
    /*
    * Column order mirrors _vibBuildThead:
    *
    *   Asset Name | Operation Type | A End (Date & Time + 11) | B End (Date & Time + 11)
    *
    * PT and TPR are the two lights inside the table's Operation cell. A report
    * cannot render lights, so they follow Operation Type as text.
    *
    * `ends` comes from PM_VIB_RENDERED_ENDS, so an A-only Point Machine gets
    * an A-only sheet -- no 12 empty B columns.
    */
    /*
    * Header for ONE end's table. The report now carries a separate table per
    * end, so a table only ever holds its own end's columns:
    *
    *   S.No | Asset Name | Date & Time | Operation Type | PT | TPR | 11 metrics
    *
    * The metric labels keep the "A End - " / "B End - " prefix so a row stays
    * self-describing when it is copied out of the sheet and away from the
    * section title. There is one Date & Time column because there is one end.
    */
    function _vibBuildExportHeaders(endKey) {
        endKey = (endKey === 'B') ? 'B' : 'A';

        var headers = [
            'S.No',
            'Asset Name',
            'Date & Time',
            'Operation Type',
            'PT',
            'TPR'
        ];

        for (var i = 0; i < VIB_COLUMNS.length; i++) {
            headers.push(
                endKey + ' End - ' + _vibCleanLabel(VIB_COLUMNS[i].label)
            );
        }

        return headers;
    }

    /*
      * Insert the fixed identity columns straight after S.No, so the vibration
      * report reads like every other report on this screen:
      *
      *   S.No | Zone | Division | Station | Vendor Name | Asset Type |
      *   Asset Name | Date & Time | Operation Type | PT | TPR | 22 metrics
      *
      * _vibBuildExportHeaders / _vibBuildExportRows are left untouched so the
      * on-screen table and any other caller keep their existing shape.
      */
    function _vibDecorateExportHeaders(headers) {
        return [headers[0]].concat(_thReportFixedHeaders(), headers.slice(1));
    }

    function _vibDecorateExportRows(rows, filterValues) {
        var fixed = _thReportFixedValues(filterValues);
        return rows.map(function (row) {
            return [row[0]].concat(fixed, row.slice(1));
        });
    }

    /*
     * Build export rows directly from the same source used by the screen.
     *
     * One export row represents one vibration Array TimestampDevice.
     */
    /*
 * Collect vibration rows for export across ALL cached pages for an asset.
 *
 * _vibBuildExportRows previously called _vibCollectAssetRows(asset) where
 * `asset` was the CURRENT PAGE's result from lastAssetResults. That meant
 * only page 1 (or whichever page was on screen) was ever exported.
 *
 * This helper merges every page stored in _vibState[assetId].pageCache into
 * a single deduplicated, timestamp-sorted row list so the download always
 * contains the full fetched history regardless of which page is displayed.
 *
 * Pages that have not been fetched yet (user never navigated to them) are
 * not included — we export what has been loaded, which matches what the
 * user has seen.
 */
    function _vibCollectAllCachedRows(asset) {
        var assetId = String(asset.assetId);
        var st = _vibState && _vibState[assetId];

        // No state recorded (e.g. old-style non-paged render) — fall back to
        // the current page only, which is the previous behaviour.
        if (!st || !st.pageCache) {
            return _vibCollectAssetRows(asset);
        }

        // Merge attributes from every cached page into a temporary asset object.
        var mergedAttrs = [];
        var seenPages = Object.keys(st.pageCache).map(Number).sort(function (a, b) { return a - b; });

        for (var pi = 0; pi < seenPages.length; pi++) {
            var pageResult = st.pageCache[seenPages[pi]];
            if (!pageResult || !Array.isArray(pageResult.attributes)) { continue; }
            for (var ai = 0; ai < pageResult.attributes.length; ai++) {
                mergedAttrs.push(pageResult.attributes[ai]);
            }
        }

        if (!mergedAttrs.length) {
            // Nothing cached at all — use whatever is in the passed asset object.
            return _vibCollectAssetRows(asset);
        }

        var mergedAsset = {
            assetId: asset.assetId,
            assetName: asset.assetName,
            attributes: mergedAttrs
        };

        var allRows = _vibCollectAssetRows(mergedAsset);

        /*
         * Deduplicate by timestamp: merging pages can produce duplicate rows
         * when the same TimestampDevice appears in two adjacent page responses
         * (carry-forward context rows shared between pages).
         */
        var seen = {};
        var uniqueRows = [];
        for (var ri = 0; ri < allRows.length; ri++) {
            var tsKey = String(allRows[ri].ts);
            if (!seen[tsKey]) {
                seen[tsKey] = true;
                uniqueRows.push(allRows[ri]);
            }
        }

        // Keep newest-first order (same as _vibCollectAssetRows).
        uniqueRows.sort(function (a, b) { return b.ts - a.ts; });
        return uniqueRows;
    }

    /*
     * Build export rows directly from the same source used by the screen.
     *
     * One export row represents one vibration Array TimestampDevice.
     */
    function _vibBuildExportRows() {
        var exportRows = [];
        var serialNumber = 1;

        if (
            !lastAssetResults ||
            !lastAssetResults.length
        ) {
            return exportRows;
        }

        for (
            var assetIndex = 0;
            assetIndex < lastAssetResults.length;
            assetIndex++
        ) {
            var asset =
                lastAssetResults[assetIndex];

            if (!asset) {
                continue;
            }

            var rawAssetName =
                asset.assetName ||
                (
                    'Asset ' +
                    asset.assetId
                );

            var displayAssetName =
                rawAssetName.indexOf('PT-') === 0
                    ? rawAssetName
                    : 'PT-' + rawAssetName;

            /*
             * Collect rows from ALL cached pages so the export contains the
             * full history, not just the currently-displayed page.
             */
            var rows =
                _vibCollectAllCachedRows(asset);

            for (
                var rowIndex = 0;
                rowIndex < rows.length;
                rowIndex++
            ) {
                var row =
                    rows[rowIndex];

                if (!row) {
                    continue;
                }

                var dateParts =
                    _vibFmtDate(row.ts);

                var operation =
                    _vibGetExportOperation(
                        asset,
                        row.ts
                    );

                var exportRow = [
                    serialNumber++,
                    displayAssetName,
                    dateParts.d + ' ' + dateParts.t,
                    operation.operation,
                    operation.pt,
                    operation.tpr
                ];

                /*
                 * A-End values.
                 */
                for (
                    var aColumnIndex = 0;
                    aColumnIndex < VIB_COLUMNS.length;
                    aColumnIndex++
                ) {
                    var aKey =
                        VIB_COLUMNS[
                            aColumnIndex
                        ].key;

                    var aEvent =
                        row.events &&
                            row.events.A
                            ? row.events.A[aKey]
                            : null;

                    exportRow.push(
                        _vibGetExportEventText(
                            aEvent
                        )
                    );
                }

                /*
                   * Pairs with the 'B End - Date & Time' header added in
                   * _vibBuildExportHeaders. Must stay immediately BEFORE the
                   * B-End metric loop -- one position out and all 11 B columns
                   * shift. Same placement as _vibBuildExportRowsByAsset.
                   */
                exportRow.push(_vibGetExportEndDateText(row, 'B'));

                /*
                 * B-End values.
                 */
                for (
                    var bColumnIndex = 0;
                    bColumnIndex < VIB_COLUMNS.length;
                    bColumnIndex++
                ) {
                    var bKey =
                        VIB_COLUMNS[
                            bColumnIndex
                        ].key;

                    var bEvent =
                        row.events &&
                            row.events.B
                            ? row.events.B[bKey]
                            : null;

                    exportRow.push(
                        _vibGetExportEventText(
                            bEvent
                        )
                    );
                }

                exportRows.push(exportRow);
            }
        }

        return exportRows;
    }

    /*
     * Build export rows grouped by asset — one bucket per PT.
     *
     * Serial numbers restart at 1 within each bucket.  The Excel exporter
     * uses this to create one worksheet tab per asset without any extra API
     * calls; the CSV and PDF exporters use it to write per-asset sections.
     */
    function _vibBuildExportRowsByAsset() {
        var buckets = [];

        if (!lastAssetResults || !lastAssetResults.length) {
            return buckets;
        }

        for (var assetIndex = 0; assetIndex < lastAssetResults.length; assetIndex++) {
            var asset = lastAssetResults[assetIndex];
            if (!asset) { continue; }

            /*
             * The ends this asset's table bound. Absent key = no table on
             * screen for this Point Machine, so no sheet in the report.
             */
            var ends = PM_VIB_RENDERED_ENDS[String(asset.assetId)];
            if (!ends || (!ends.a && !ends.b)) { continue; }

            var rawAssetName =
                asset.assetName ||
                ('Asset ' + asset.assetId);

            var displayAssetName =
                rawAssetName.indexOf('PT-') === 0
                    ? rawAssetName
                    : 'PT-' + rawAssetName;

            var assetRows = _vibCollectAllCachedRows(asset);

            /*
             * ONE TABLE PER END, matching the A End / B End tabs: a separate
             * sheet in Excel, a separate section in CSV, a separate page in the
             * PDF. Serial numbering restarts in each table, because each one is
             * its own table.
             *
             * The column header does NOT change -- every table carries the full
             * fixed header, so the end that is not this table's end simply
             * leaves its columns empty.
             */
            var _endOrder = [];
            if (ends.a) { _endOrder.push('A'); }
            if (ends.b) { _endOrder.push('B'); }

            for (var eI = 0; eI < _endOrder.length; eI++) {
                var endKey = _endOrder[eI];
                var endRows = _vibFilterRowsForEnd(assetRows, endKey);

                var exportRows = [];
                var serialNumber = 1;

                for (var rowIndex = 0; rowIndex < endRows.length; rowIndex++) {
                    var row = endRows[rowIndex];
                    if (!row) { continue; }

                    var endTs = _vibEndTs(row, endKey);
                    if (endTs === null) { endTs = row.ts; }

                    var operation = _vibGetExportOperation(asset, endTs);
                    var dateText = _vibGetExportEndDateText(row, endKey);

                    var exportRow = [
                        serialNumber++,
                        displayAssetName,
                        dateText,
                        operation.operation,
                        operation.pt,
                        operation.tpr
                    ];

                    // This end's 11 metrics. The other end has its own table.
                    for (var cI = 0; cI < VIB_COLUMNS.length; cI++) {
                        var eventBundle =
                            (row.events && row.events[endKey])
                                ? row.events[endKey][VIB_COLUMNS[cI].key]
                                : null;

                        exportRow.push(_vibGetExportEventText(eventBundle));
                    }

                    exportRows.push(exportRow);
                }

                buckets.push({
                    label: displayAssetName + ' - ' + endKey + ' End',
                    assetName: displayAssetName,
                    end: endKey,
                    rows: exportRows
                });
            }
        }

        return buckets;
    }
    /*
     * Create a safe filename using the selected date range.
     */
    function _vibGetExportFileName(
        extension
    ) {
        var fromDate =
            $('#txtFromDate').val() || '';

        var toDate =
            $('#txtToDate').val() || '';

        var datePart =
            '';

        if (fromDate || toDate) {
            datePart =
                '_' +
                String(fromDate).replace(
                    /[^0-9A-Za-z-]/g,
                    '-'
                ) +
                '_to_' +
                String(toDate).replace(
                    /[^0-9A-Za-z-]/g,
                    '-'
                );
        }

        return (
            'Telemetry_History_Vibration' +
            datePart +
            '.' +
            extension
        );
    }

    /*
     * Common vibration-report metadata.
     */
    function _vibGetExportMeta(
        filterValues
    ) {
        return [
            [
                'Zone',
                filterValues.zone || ''
            ],
            [
                'Division',
                filterValues.division || ''
            ],
            [
                'Station',
                filterValues.station || ''
            ],
            //[
            //    'Coverage',
            //    _vibExportCappedAssets()
            //        ? 'PARTIAL - scan limit reached; later events in this ' +
            //        'period are not included'
            //        : 'Complete for the period below'
            //],
            [
                'Generated Date',
                formatDateTimeDDMMYYYY(
                    new Date()
                )
            ],
            [
                'Period From',
                convertDateToddmmyyyy(
                    $('#txtFromDate').val()
                ) +
                ' ' +
                (
                    $('#txtFromTime').val() ||
                    '00:00'
                )
            ],
            [
                'Period To',
                convertDateToddmmyyyy(
                    $('#txtToDate').val()
                ) +
                ' ' +
                (
                    $('#txtToTime').val() ||
                    '23:59'
                )
            ]
        ];
    }

    function fnDownloadVibrationExcel() {
        if (
            typeof ExcelJS === 'undefined'
        ) {
            showError(
                'Excel library not loaded.',
                'Download Error'
            );
            return;
        }

        if (
            !lastAssetResults ||
            !lastAssetResults.length
        ) {
            showWarning(
                'Please search vibration history first.',
                'Download'
            );
            return;
        }

        // Report must match the table. Blocked, not silently partial.
        if (_vibExportPendingAssets() > 0) {
            showWarning(
                'Still scanning the selected range. Wait for the record count ' +
                'to stop showing "+", then download.',
                'Download'
            );
            return;
        }

        $('#loader').show();

        getReportFilterValues(
            function (fv) {
                try {
                    // Headers are per-asset now: an A-only PT gets an A-only
                    // sheet, matching its table.
                    // One bucket per asset END, so the header is that end's
                    // 11 metric columns only -- no columns for the other end.
                    var assetBuckets =
                        _vibBuildExportRowsByAsset().map(function (b) {
                            return {
                                label: b.label,
                                headers: _vibDecorateExportHeaders(
                                    _vibBuildExportHeaders(b.end)
                                ),
                                rows: _vibDecorateExportRows(b.rows, fv)
                            };
                        }).filter(function (b) { return b.rows.length > 0; });

                    if (!assetBuckets.length) {
                        $('#loader').hide();

                        showWarning(
                            'No vibration data found for export.',
                            'Download'
                        );

                        return;
                    }

                    var workbook =
                        new ExcelJS.Workbook();

                    workbook.creator =
                        'RDPMS';

                    workbook.created =
                        new Date();

                    var _usedSheetNames = {};

                    assetBuckets.forEach(function (bucket) {
                        var headers = bucket.headers;

                        // One Excel tab per asset END — sheet name max 31 chars, no
                        // special chars. Long names can collide after the slice, and
                        // ExcelJS throws on a duplicate, so suffix instead.
                        var _baseSheetName =
                            bucket.label
                                .replace(/[^a-zA-Z0-9 _-]/g, '')
                                .slice(0, 31) || 'Sheet';

                        var sheetName = _baseSheetName;
                        var _dupIndex = 2;

                        while (_usedSheetNames[sheetName.toLowerCase()]) {
                            var _suffix = ' (' + _dupIndex + ')';
                            sheetName =
                                _baseSheetName.slice(0, 31 - _suffix.length) + _suffix;
                            _dupIndex++;
                        }

                        _usedSheetNames[sheetName.toLowerCase()] = true;
                        var rows = bucket.rows;
                        var sheet = workbook.addWorksheet(sheetName);

                    /*
                     * Report title.
                     */
                    sheet.mergeCells(
                        1,
                        1,
                        1,
                        headers.length
                    );

                    var titleCell =
                        sheet.getCell(1, 1);

                    titleCell.value =
                        'Telemetry History - Vibration Data';

                    titleCell.font = {
                        bold: true,
                        size: 15,
                        color: {
                            argb: 'FFFFFFFF'
                        }
                    };

                    titleCell.alignment = {
                        horizontal: 'center',
                        vertical: 'middle'
                    };

                    titleCell.fill = {
                        type: 'pattern',
                        pattern: 'solid',
                        fgColor: {
                            argb: 'FF0F766E'
                        }
                    };

                    sheet.getRow(1).height = 26;

                    var meta =
                        _vibGetExportMeta(fv);

                    for (
                        var metaIndex = 0;
                        metaIndex < meta.length;
                        metaIndex++
                    ) {
                        var excelRowNumber =
                            metaIndex + 2;

                        sheet.getCell(
                            excelRowNumber,
                            1
                        ).value =
                            meta[metaIndex][0];

                        sheet.getCell(
                            excelRowNumber,
                            2
                        ).value =
                            meta[metaIndex][1];

                        sheet.getCell(
                            excelRowNumber,
                            1
                        ).font = {
                            bold: true
                        };

                        sheet.mergeCells(
                            excelRowNumber,
                            2,
                            excelRowNumber,
                            headers.length
                        );
                    }

                    /*
                     * Header row.
                     */
                    var headerRowNumber = 8;

                    var headerRow =
                        sheet.getRow(
                            headerRowNumber
                        );

                    headerRow.values =
                        headers;

                    headerRow.height = 42;

                    headerRow.eachCell(
                        function (cell) {
                            cell.font = {
                                bold: true,
                                color: {
                                    argb: 'FFFFFFFF'
                                },
                                size: 9
                            };

                            cell.fill = {
                                type: 'pattern',
                                pattern: 'solid',
                                fgColor: {
                                    argb: 'FF115E59'
                                }
                            };

                            cell.alignment = {
                                horizontal: 'center',
                                vertical: 'middle',
                                wrapText: true
                            };

                            cell.border = {
                                top: {
                                    style: 'thin',
                                    color: {
                                        argb: 'FF94A3B8'
                                    }
                                },
                                left: {
                                    style: 'thin',
                                    color: {
                                        argb: 'FF94A3B8'
                                    }
                                },
                                bottom: {
                                    style: 'thin',
                                    color: {
                                        argb: 'FF94A3B8'
                                    }
                                },
                                right: {
                                    style: 'thin',
                                    color: {
                                        argb: 'FF94A3B8'
                                    }
                                }
                            };
                        }
                    );

                    /*
                     * Data rows.
                     */
                    for (
                        var dataIndex = 0;
                        dataIndex < rows.length;
                        dataIndex++
                    ) {
                        var addedRow =
                            sheet.addRow(
                                rows[dataIndex]
                            );

                        addedRow.height = 32;

                        addedRow.eachCell(
                            function (cell) {
                                cell.alignment = {
                                    horizontal: 'center',
                                    vertical: 'middle',
                                    wrapText: true
                                };

                                cell.font = {
                                    size: 9
                                };

                                cell.border = {
                                    top: {
                                        style: 'thin',
                                        color: {
                                            argb: 'FFD5E7DF'
                                        }
                                    },
                                    left: {
                                        style: 'thin',
                                        color: {
                                            argb: 'FFD5E7DF'
                                        }
                                    },
                                    bottom: {
                                        style: 'thin',
                                        color: {
                                            argb: 'FFD5E7DF'
                                        }
                                    },
                                    right: {
                                        style: 'thin',
                                        color: {
                                            argb: 'FFD5E7DF'
                                        }
                                    }
                                };
                            }
                        );

                        if (
                            dataIndex % 2 === 1
                        ) {
                            addedRow.eachCell(
                                function (cell) {
                                    cell.fill = {
                                        type: 'pattern',
                                        pattern: 'solid',
                                        fgColor: {
                                            argb: 'FFF3FAF7'
                                        }
                                    };
                                }
                            );
                        }
                    }

                    /*
                     * Fixed columns.
                     */
                        // S.No | Zone | Division | Station | Vendor Name |
                        // Asset Type | Asset Name | Operation Type | PT | TPR |
                        // <first end> Date & Time
                        sheet.getColumn(1).width = 8;
                        sheet.getColumn(2).width = 14;
                        sheet.getColumn(3).width = 14;
                        sheet.getColumn(4).width = 16;
                        sheet.getColumn(5).width = 14;
                        sheet.getColumn(6).width = 16;
                        sheet.getColumn(7).width = 18;
                        sheet.getColumn(8).width = 17;
                        sheet.getColumn(9).width = 18;
                        sheet.getColumn(10).width = 18;
                        sheet.getColumn(11).width = 20;

                    /*
                     * 22 vibration metric columns.
                     */
                    for (
                        var columnIndex = 12;
                        columnIndex <= headers.length;
                        columnIndex++
                    ) {
                        sheet.getColumn(
                            columnIndex
                        ).width = 24;
                    }

                        sheet.autoFilter = {
                            from: {
                                row: headerRowNumber,
                                column: 1
                            },
                            to: {
                                row: headerRowNumber,
                                column: headers.length
                            }
                        };
                    }); // end assetBuckets.forEach

                    workbook.xlsx
                        .writeBuffer()
                        .then(
                            function (buffer) {
                                saveAs(
                                    new Blob(
                                        [buffer],
                                        {
                                            type:
                                                'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
                                        }
                                    ),
                                    _vibGetExportFileName(
                                        'xlsx'
                                    )
                                );

                                $('#loader').hide();

                                showSuccess(
                                    'Vibration Excel downloaded.',
                                    'Download Complete'
                                );
                            }
                        )
                        .catch(
                            function (error) {
                                $('#loader').hide();

                                console.error(
                                    '[Vibration Excel Export]',
                                    error
                                );

                                showError(
                                    'Failed to download vibration Excel.',
                                    'Download Error'
                                );
                            }
                        );
                } catch (error) {
                    $('#loader').hide();

                    console.error(
                        '[Vibration Excel Export]',
                        error
                    );

                    showError(
                        'Failed to download vibration Excel.',
                        'Download Error'
                    );
                }
            }
        );
    }

    // ============================================================
    // Download ALL pages (not just the visible one)
    //
    // Table data is cursor-paginated on the server, so thCaches only
    // holds the current page. Before a download we walk every cursor
    // page for the current search, merge them, and rebuild a full
    // per-asset cache set that the existing Excel/CSV/PDF exporters
    // consume unchanged.
    // ============================================================

    // Module-scoped thCaches accessor (the export fns shadow thCaches
    // with a local, so they can't read the global by name).

    function _showExportToast(msg) {
        var $t = $('#exportProgressToast');
        if (!$t.length) {
            if (!$('#exportToastStyle').length) {
                $('head').append(
                    '<style id="exportToastStyle">' +
                    '@@keyframes expToastIn{from{transform:translateY(12px);opacity:0}to{transform:translateY(0);opacity:1}}' +
                    '#exportProgressToast{position:fixed;right:22px;bottom:22px;z-index:20000;min-width:280px;max-width:360px;' +
                    'background:#fff;border:1px solid #e2e8f0;border-left:4px solid #4338ca;border-radius:10px;' +
                    'box-shadow:0 8px 28px rgba(15,23,42,.18);padding:14px 16px;animation:expToastIn .18s ease-out;font-size:13px;color:#1e293b;}' +
                    '#exportProgressToast .ept-head{display:flex;align-items:center;gap:9px;font-weight:600;color:#4338ca;margin-bottom:4px;}' +
                    '#exportProgressToast .ept-sub{font-size:12px;color:#64748b;line-height:1.4;}' +
                    '#exportProgressToast .ept-bar{height:3px;border-radius:3px;margin-top:10px;background:#eef2f7;overflow:hidden;position:relative;}' +
                    '#exportProgressToast .ept-fill{position:absolute;top:0;left:0;height:100%;width:100%;border-radius:3px;' +
                    'background:linear-gradient(90deg,transparent,#4338ca,transparent);background-size:220px 100%;animation:htlShimmer 1.15s linear infinite;}' +
                    '</style>'
                );
            }
            $('body').append(
                '<div id="exportProgressToast" role="status" aria-live="polite">' +
                '<div class="ept-head">' +
                '<span class="spinner-border spinner-border-sm" style="width:14px;height:14px;border-width:2px;"></span>' +
                '<span>Preparing report…</span></div>' +
                '<div class="ept-sub" id="exportToastSub"></div>' +
                '<div class="ept-bar"><div class="ept-fill"></div></div>' +
                '</div>'
            );
        }
        $('#exportToastSub').text(msg || 'This can take a moment — you can keep working; we\'ll pop the download up when it\'s ready.');
        $('#exportProgressToast').show();
        _exportToastActive = true;
    }

    function _hideExportToast() {
        $('#exportProgressToast').remove();
        _exportToastActive = false;
    }

    // Fetch every non-PM asset's FULL range in ONE HistoryValue call each
    // (GetHistoryData -> /api/HistoryValue). The response's attribute-major
    // "Values" shape is exactly what parseTableData consumes, so no reshape
    // is needed. Runs a 3-wide pool; any asset that errors or times out
    // falls back to that asset's cursor walk so a wide range degrades
    // gracefully instead of failing the whole report.
    // Fetch ONE DashboardHistory page (edges only) for an asset. Returns the
    // column list plus firstRow / lastRow via the callback. pageSize=1 keeps the
    // probe cheap -- we only need the envelope's edge members, not its rows.

    function _vibFetchAllPagesForExport(onDone) {
        if (typeof _vibIsExportMode !== 'function' || !_vibIsExportMode()) { onDone(); return; }

        var order = (_vibAssetOrder || []).slice();
        if (!order.length) { onDone(); return; }

        var VIB_MAX_EXPORT_PAGES = 200;   // hard stop; mirrors the cursor guards elsewhere
        var idx = 0;

        function nextAsset() {
            if (idx >= order.length) { _hideExportToast(); onDone(); return; }
            idx++;
            walk(order[idx - 1], 1);
        }

        function walk(assetId, pageNum) {
            var st = _vibGetState(assetId);
            if (!st || pageNum > VIB_MAX_EXPORT_PAGES) { nextAsset(); return; }
            // Page > 1 with no known cursor cannot be requested -- stop this
            // asset rather than re-fetching page 1 in a loop.
            if (pageNum > 1 && (!st.cursorByPage || st.cursorByPage[pageNum] == null)) {
                nextAsset();
                return;
            }

            _vibFetchPage(assetId, pageNum, function (res) {
                if (!res || !res._hasNextPage) { nextAsset(); return; }
                walk(assetId, pageNum + 1);
            }, true);
        }

        _showExportToast('Preparing report…');
        nextAsset();
    }


    function fnDownloadVibrationCSV() {
        if (
            !lastAssetResults ||
            !lastAssetResults.length
        ) {
            showWarning(
                'Please search vibration history first.',
                'Download'
            );
            return;
        }

        // Report must match the table. Blocked, not silently partial.
        if (_vibExportPendingAssets() > 0) {
            showWarning(
                'Still scanning the selected range. Wait for the record count ' +
                'to stop showing "+", then download.',
                'Download'
            );
            return;
        }

        $('#loader').show();

        getReportFilterValues(
            function (fv) {
                try {
                    var escapeCsv = function (v) {
                        return '"' + String(v == null ? '' : v).replace(/"/g, '""') + '"';
                    };

                    // Headers are per-asset now: an A-only PT gets an A-only
                    // sheet, matching its table.
                    // One bucket per asset END, so the header is that end's
                    // 11 metric columns only -- no columns for the other end.
                    var assetBuckets =
                        _vibBuildExportRowsByAsset().map(function (b) {
                            return {
                                label: b.label,
                                headers: _vibDecorateExportHeaders(
                                    _vibBuildExportHeaders(b.end)
                                ),
                                rows: _vibDecorateExportRows(b.rows, fv)
                            };
                        }).filter(function (b) { return b.rows.length > 0; });

                    if (!assetBuckets.length) {
                        $('#loader').hide();

                        showWarning(
                            'No vibration data found for export.',
                            'Download'
                        );

                        return;
                    }

                    var csvLines = [];

                    // Report title
                    csvLines.push(escapeCsv('Telemetry History - Vibration Data'));

                    // Meta rows (Zone / Division / Station / Generated Date / Period)
                    _vibGetExportMeta(fv).forEach(function (pair) {
                        csvLines.push([escapeCsv(pair[0]), escapeCsv(pair[1])].join(','));
                    });

                    csvLines.push('');

                    // One section per asset
                    assetBuckets.forEach(function (bucket) {
                        var headers = bucket.headers;

                        csvLines.push('');
                        csvLines.push(escapeCsv('=== ' + bucket.label + ' ==='));
                        csvLines.push('');
                        csvLines.push(headers.map(escapeCsv).join(','));
                        bucket.rows.forEach(function (row) {
                            csvLines.push(row.map(escapeCsv).join(','));
                        });
                    });

                    /*
                     * UTF-8 BOM keeps Excel-compatible CSV labels readable.
                     */
                    var csvContent =
                        '\uFEFF' +
                        csvLines.join('\r\n');

                    saveAs(
                        new Blob(
                            [csvContent],
                            {
                                type:
                                    'text/csv;charset=utf-8;'
                            }
                        ),
                        _vibGetExportFileName(
                            'csv'
                        )
                    );

                    $('#loader').hide();

                    showSuccess(
                        'Vibration CSV downloaded.',
                        'Download Complete'
                    );
                } catch (error) {
                    $('#loader').hide();

                    console.error(
                        '[Vibration CSV Export]',
                        error
                    );

                    showError(
                        'Failed to download vibration CSV.',
                        'Download Error'
                    );
                }
            }
        );
    }




/* =============================================================================
 * Export entry points — WRAP, never replace.
 *
 * The source view shipped its own fnDownloadExcel / fnDownloadCSV that branch
 * to vibration first and otherwise run the WEB project's generic exporter.
 * Taking those wholesale would have swapped out FRS Advance's own Track /
 * Signal / Point Machine exports, which have their own column shape. So only
 * the vibration-specific exporters were ported, and the page's existing entry
 * points are wrapped: vibration mode is intercepted, everything else falls
 * through to the exporter that was already there.
 * ========================================================================== */
(function () {
    'use strict';
    var W = window;

    function wrap(name, vibFn) {
        var prev = W[name];
        if (typeof prev !== 'function') return;          // nothing to wrap yet
        if (prev.__vibWrapped) return;                   // idempotent

        var wrapped = function () {
            try {
                if (typeof _vibIsExportMode === 'function' && _vibIsExportMode() &&
                    typeof W[vibFn] === 'function') {
                    // Cursor-paginated: the cache holds one page, so walk every
                    // page before writing the file.
                    if (typeof _vibFetchAllPagesForExport === 'function') {
                        _vibFetchAllPagesForExport(function () { W[vibFn](); });
                    } else {
                        W[vibFn]();
                    }
                    return;
                }
            } catch (e) {
                if (W.console) console.error('[PM Vibration export] failed; using the standard export:', e);
            }
            return prev.apply(this, arguments);
        };
        wrapped.__vibWrapped = true;
        W[name] = wrapped;
    }

    $(function () {
        wrap('fnDownloadExcel', 'fnDownloadVibrationExcel');
        wrap('fnDownloadCSV', 'fnDownloadVibrationCSV');
    });
})();
