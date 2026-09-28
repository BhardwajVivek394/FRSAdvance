/* =====================================================================
 * TELEMETRY LIVE — POINT MACHINE VIBRATION
 *
 * Ported from E7MRIV2Web's telemetry live page and adapted to FRS Advance.
 * Adds, on top of the vibration modal telemetrylive.js already has:
 *   - per-end (A End / B End) resolution driven by the VibrationA /
 *     VibrationB flags returned by GetAssestBy
 *   - metric resolution by AssetAttributeId with alias fallback
 *   - the collapsible vibration table under the Point Machine views
 *   - the per-cell event waveform graph
 *   - vibration rows for the Excel / CSV / PDF exports
 *
 * Loaded after telemetrylive.js. Two functions here deliberately replace
 * the earlier definitions in that file:
 *   fnUpdateVibrationModal  — now throttled, and skips zero/blank readings
 *   applyPmVibrationEndVisibility — now a no-op (ends are separate rows)
 * Everything else is additive.
 *
 * The vibration metadata cache is kept separate from the page's own maps
 * (userAssetSimpleMap, userAssetDataloggerMap, assetAttributeMap, ...) so
 * this cannot disturb the existing renderers.
 * ===================================================================== */

// Negative sensor readings are display artefacts; floor them at zero.
// Also defined in telemetrylive-graphs.js — whichever loads first wins.
if (typeof window.tlZeroFloor !== 'function') {
    window.tlZeroFloor = function (v) {
        var n = parseFloat(v);
        if (isNaN(n)) return v;
        return (n <= 0) ? 0 : n;
    };
}
var tlZeroFloor = window.tlZeroFloor;

// ── State, end flags and column configuration ────────────────────────
// ── Vibration data store: keyed by AssetId ──────────────────────────────
// wsVibrationData[assetId] = {assetName, lastTimestamp, attrs: {attrName: {value, timestamp} } }
//var wsVibrationData = {};
//var wsAttributeOrder = {};


// wsVibrationData is declared in telemetrylive.js; this module only writes to it.

/*
 * Point Machine vibration table UI state.
 * Persisted across re-renders so the collapsed/expanded choice
 * survives WebSocket-driven row rebuilds.
 * Default is collapsed: the table stays hidden behind the toggle.
 */
var pmVibrationTablePanelOpen = false;

/*
 * Separate metadata cache used only by the Point Machine
 * vibration table.
 *
 * It does not modify:
 * - userAssetSimpleMap
 * - userAssetDataloggerMap
 * - assetAttributeMap
 * - assetAttributeByName
 * - zeroOffsetCache
 * - bulkAssetsList
 */
var pmVibrationBulkMetadata = {};
var pmVibrationBulkLoadedSiteId = null;
var pmVibrationBulkLoadingSiteId = null;

/*
 * Vibration end availability (A End / B End).
 *
 * GetAssestBy now returns VibrationA and VibrationB per asset:
 *   true         -> that end carries a vibration sensor, build it
 *   null / false -> that end has no sensor, do not build it
 *
 * When the payload carries neither column (older API), the asset is flagged
 * "unknown" and both ends stay enabled, so existing stations do not regress.
 */
var pmVibrationEndFlags = {};

function pmVibrationFlagIsTrue(value) {
    if (value === null || value === undefined) return false;
    if (value === true) return true;
    if (typeof value === 'number') return value !== 0;

    var text = String(value).trim().toLowerCase();

    return (
        text === 'true' ||
        text === '1' ||
        text === 'y' ||
        text === 'yes'
    );
}

function resetPmVibrationEndFlags() {
    pmVibrationEndFlags = {};
}

/*
 * Indexes VibrationA / VibrationB from the GetAssestBy payload.
 * Called on every site / asset-type change, so the map is rebuilt, not merged.
 */
function setPmVibrationEndFlagsFromAssetList(assetList) {
    resetPmVibrationEndFlags();

    if (!assetList || !assetList.length) return;

    for (var flagIndex = 0; flagIndex < assetList.length; flagIndex++) {
        var flagRow = assetList[flagIndex];

        if (!flagRow || flagRow.Id === null || flagRow.Id === undefined) continue;

        var rawA = (flagRow.VibrationA !== undefined) ? flagRow.VibrationA : flagRow.vibrationA;
        var rawB = (flagRow.VibrationB !== undefined) ? flagRow.VibrationB : flagRow.vibrationB;

        pmVibrationEndFlags[String(flagRow.Id)] = {
            known: (rawA !== undefined || rawB !== undefined),
            A: pmVibrationFlagIsTrue(rawA),
            B: pmVibrationFlagIsTrue(rawB)
        };
    }
}

/*
 * True when the given end must be built for this asset.
 * Unknown asset or older payload -> true (legacy behaviour).
 */
function pmVibrationEndEnabled(assetId, endName) {
    var flags = pmVibrationEndFlags[String(assetId)];

    if (!flags || !flags.known) return true;

    return (endName === 'B') ? flags.B : flags.A;
}

/*
 * True when at least one asset currently in the table carries this end.
 * Drives whether the whole A End / B End column group is shown.
 */
function pmVibrationEndVisible(endName) {
    var visibleIds = pmVibrationGetSelectedAssetIds();

    for (var visibleIndex = 0; visibleIndex < visibleIds.length; visibleIndex++) {
        if (pmVibrationEndEnabled(visibleIds[visibleIndex], endName)) return true;
    }

    return false;
}

/*
 * True when this point machine carries a vibration sensor on either end.
 * VibrationA and VibrationB both null/false means no sensor at all, so the
 * asset gets no row - the same rule the History page applies when it decides
 * whether to build a vibration table for a PM.
 *
 * An asset GetAssestBy never answered for stays enabled (legacy behaviour),
 * because pmVibrationEndEnabled returns true for an unknown asset.
 */
function pmVibrationAssetHasEnabledEnd(assetId) {
    return (
        pmVibrationEndEnabled(assetId, 'A') ||
        pmVibrationEndEnabled(assetId, 'B')
    );
}

/*
 * Selected point machines that should appear in the vibration table at all.
 */
function pmVibrationGetRenderableAssetIds() {
    return pmVibrationGetSelectedAssetIds().filter(
        pmVibrationAssetHasEnabledEnd
    );
}

/*
 * Collapses the A End or B End column group when no asset in view has it.
 * Header and body cells already carry pm-vibration-column-a /
 * pm-vibration-column-b, so no HTML restructuring is needed.
 */
function applyPmVibrationEndVisibility() {
    // No-op: A End / B End are now separate stacked ROWS in the body, built only
    // for the ends an asset actually has (VibrationA / VibrationB). Kept as a stub.
    return;
}

/*
 * Point Machine vibration table configuration.
 *
 * Important:
 * - Existing vibration icon/modal functionality remains unchanged.
 * - WebSocket AssetAttributeId values can vary by station/configuration.
 * - AssetAttributeName is therefore the primary binding key.
 * - Event values are intentionally kept separate and currently display "—".
 */
//var PM_VIBRATION_TABLE_COLUMNS = [
//    {
//        key: 'xFullBandRms',
//        title: 'X Full Band RMS Accel (G)',
//        address: '40034',
//        aliases: [
//            'Vibration_Sensor_X_FB_RMS_Accel_G',
//            'Vibration_Sensor_X_Full_Band_RMS_Accel_G',
//            'X_FB_RMS_Accel_G',
//            'X_Full_Band_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'yFullBandRms',
//        title: 'Y Full Band RMS Accel (G)',
//        address: '40035',
//        aliases: [
//            'Vibration_Sensor_Y_FB_RMS_Accel_G',
//            'Vibration_Sensor_Y_Full_Band_RMS_Accel_G',
//            'Y_FB_RMS_Accel_G',
//            'Y_Full_Band_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'zFullBandRms',
//        title: 'Z Full Band RMS Accel (G)',
//        address: '40036',
//        aliases: [
//            'Vibration_Sensor_Z_FB_RMS_Accel_G',
//            'Vibration_Sensor_Z_Full_Band_RMS_Accel_G',
//            'Z_FB_RMS_Accel_G',
//            'Z_Full_Band_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'xHfRms',
//        title: 'X HF RMS Accel (G)',
//        address: '40002',
//        fallbackIds: [692],
//        aliases: [
//            'Vibration_Sensor_X_HF_RMS_Accel_G',
//            'X_HF_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'yHfRms',
//        title: 'Y HF RMS Accel (G)',
//        address: '40004',
//        fallbackIds: [694],
//        aliases: [
//            'Vibration_Sensor_Y_HF_RMS_Accel_G',
//            'Y_HF_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'zHfRms',
//        title: 'Z HF RMS Accel (G)',
//        address: '40006',
//        fallbackIds: [696],
//        aliases: [
//            'Vibration_Sensor_Z_HF_RMS_Accel_G',
//            'Z_HF_RMS_Accel_G'
//        ]
//    },
//    {
//        key: 'xPkPk',
//        title: 'X Pk-Pk Accel (G)',
//        address: '40008',
//        fallbackIds: [698],
//        aliases: [
//            'Vibration_Sensor_X_FB_PkPk_Accel_G',
//            'X_FB_PkPk_Accel_G',
//            'X_PkPk_Accel_G'
//        ]
//    },
//    {
//        key: 'yPkPk',
//        title: 'Y Pk-Pk Accel (G)',
//        address: '40009',
//        fallbackIds: [699],
//        aliases: [
//            'Vibration_Sensor_Y_FB_PkPk_Accel_G',
//            'Y_FB_PkPk_Accel_G',
//            'Y_PkPk_Accel_G'
//        ]
//    },
//    {
//        key: 'zPkPk',
//        title: 'Z Pk-Pk Accel (G)',
//        shortTitle: 'Z Pk-Pk',
//        unit: 'G',
//        address: '40010',
//        fallbackIds: [700],
//        aliases: [
//            'Vibration_Sensor_Z_FB_PkPk_Accel_G',
//            'Vibration_Sensor_Z_FB_Pk_Pk_Accel_G',
//            'Vibration_Sensor_Z_PkPk_Accel_G',
//            'Z_FB_PkPk_Accel_G',
//            'Z_PkPk_Accel_G'
//        ]
//    },
//    {
//        key: 'temperature',
//        title: 'Temperature (°C)',
//        shortTitle: 'Temperature',
//        unit: '°C',
//        address: '40043',

//        // Current first WebSocket snapshot uses AssetAttributeId 697.
//        // Name matching remains primary because IDs can differ by site.
//        fallbackIds: [697],

//        type: 'temperature',

//        aliases: [
//            'Vibration_Sensor_Temperature_F',
//            'Vibration_Sensor_Temperature_C',
//            'Vibration_Sensor_Temperature',
//            'Vibration_Temperature_F',
//            'Vibration_Temperature_C',
//            'Temperature_F',
//            'Temperature_C',
//            'Temperature'
//        ]
//    },
//    {
//        key: 'motorRunFlag',
//        title: 'Motor Run Flag',
//        shortTitle: 'Motor Run',
//        unit: '',
//        address: '40029',

//        // Current first WebSocket snapshot uses AssetAttributeId 719.
//        fallbackIds: [719],

//        type: 'flag',

//        aliases: [
//            'Vibration_Sensor_Motor_Run_Flag',
//            'Vibration_Sensor_MotorRunFlag',
//            'Vibration_Motor_Run_Flag',
//            'Motor_Run_Flag',
//            'MotorRunFlag',
//            'Motor Run Flag'
//        ]
//    }
//];

var PM_VIBRATION_TABLE_COLUMNS = [
    {
        //key: 'xFullBandRms',
        //title: 'X Full Band RMS Accel (G)',
        //shortTitle: 'X Full Band RMS Accel (G)',
        //unit: 'G',
        //address: '40034',
        //fallbackIds: [691],
        key: 'xFullBandRms',
        title: 'X Full Band RMS Accel (G)',
        shortTitle: 'X Full Band RMS Accel (G)',
        unit: 'G',
        address: '40034',
        fallbackIds: [691],
        bEndFallbackIds: [957],
        aliases: [
            'Vibration_Sensor_X_FB_RMS_Accel_G',
            'Vibration_Sensor_X_Full_Band_RMS_Accel_G',
            'X_FB_RMS_Accel_G',
            'X_Full_Band_RMS_Accel_G'
        ]
    },
    {
        //key: 'yFullBandRms',
        //title: 'Y Full Band RMS Accel (G)',
        //shortTitle: 'Y Full Band RMS Accel (G)',
        //unit: 'G',
        //address: '40035',
        //fallbackIds: [693],
        key: 'yFullBandRms',
        title: 'Y Full Band RMS Accel (G)',
        shortTitle: 'Y Full Band RMS Accel (G)',
        unit: 'G',
        address: '40035',
        fallbackIds: [693],
        bEndFallbackIds: [959],
        aliases: [
            'Vibration_Sensor_Y_FB_RMS_Accel_G',
            'Vibration_Sensor_Y_Full_Band_RMS_Accel_G',
            'Y_FB_RMS_Accel_G',
            'Y_Full_Band_RMS_Accel_G'
        ]
    },
    {
        //key: 'zFullBandRms',
        //title: 'Z Full Band RMS Accel (G)',
        //shortTitle: 'Z Full Band RMS Accel (G)',
        //unit: 'G',
        //address: '40036',
        //fallbackIds: [695],
        key: 'zFullBandRms',
        title: 'Z Full Band RMS Accel (G)',
        shortTitle: 'Z Full Band RMS Accel (G)',
        unit: 'G',
        address: '40036',
        fallbackIds: [695],
        bEndFallbackIds: [961],
        aliases: [
            'Vibration_Sensor_Z_FB_RMS_Accel_G',
            'Vibration_Sensor_Z_Full_Band_RMS_Accel_G',
            'Z_FB_RMS_Accel_G',
            'Z_Full_Band_RMS_Accel_G'
        ]
    },
    {
        //key: 'xHfRms',
        //title: 'X HF RMS Accel (G)',
        //shortTitle: 'X HF RMS Accel (G)',
        //unit: 'G',
        //address: '40002',
        //fallbackIds: [692],
        key: 'xHfRms',
        title: 'X HF RMS Accel (G)',
        shortTitle: 'X HF RMS Accel (G)',
        unit: 'G',
        address: '40002',
        fallbackIds: [692],
        bEndFallbackIds: [958],
        aliases: [
            'Vibration_Sensor_X_HF_RMS_Accel_G',
            'X_HF_RMS_Accel_G'
        ]
    },
    {
        //key: 'yHfRms',
        //title: 'Y HF RMS Accel (G)',
        //shortTitle: 'Y HF RMS Accel (G)',
        //unit: 'G',
        //address: '40004',
        //fallbackIds: [694],
        key: 'yHfRms',
        title: 'Y HF RMS Accel (G)',
        shortTitle: 'Y HF RMS Accel (G)',
        unit: 'G',
        address: '40004',
        fallbackIds: [694],
        bEndFallbackIds: [960],
        aliases: [
            'Vibration_Sensor_Y_HF_RMS_Accel_G',
            'Y_HF_RMS_Accel_G'
        ]
    },
    {
        //key: 'zHfRms',
        //title: 'Z HF RMS Accel (G)',
        //shortTitle: 'Z HF RMS Accel (G)',
        //unit: 'G',
        //address: '40006',
        //fallbackIds: [696],
        key: 'zHfRms',
        title: 'Z HF RMS Accel (G)',
        shortTitle: 'Z HF RMS Accel (G)',
        unit: 'G',
        address: '40006',
        fallbackIds: [696],
        bEndFallbackIds: [962],
        aliases: [
            'Vibration_Sensor_Z_HF_RMS_Accel_G',
            'Z_HF_RMS_Accel_G'
        ]
    },
    {
        //key: 'xPkPk',
        //title: 'X Pk-Pk Accel (G)',
        //shortTitle: 'X Pk-Pk Accel (G)',
        //unit: 'G',
        //address: '40008',
        //fallbackIds: [698],
        key: 'xPkPk',
        title: 'X Pk-Pk Accel (G)',
        shortTitle: 'X Pk-Pk Accel (G)',
        unit: 'G',
        address: '40008',
        fallbackIds: [698],
        bEndFallbackIds: [964],
        aliases: [
            'Vibration_Sensor_X_FB_PkPk_Accel_G',
            'Vibration_Sensor_X_FB_Pk_Pk_Accel_G',
            'Vibration_Sensor_X_PkPk_Accel_G',
            'X_FB_PkPk_Accel_G',
            'X_PkPk_Accel_G'
        ]
    },
    {
        //key: 'yPkPk',
        //title: 'Y Pk-Pk Accel (G)',
        //shortTitle: 'Y Pk-Pk Accel (G)',
        //unit: 'G',
        //address: '40009',
        //fallbackIds: [699],
        key: 'yPkPk',
        title: 'Y Pk-Pk Accel (G)',
        shortTitle: 'Y Pk-Pk Accel (G)',
        unit: 'G',
        address: '40009',
        fallbackIds: [699],
        bEndFallbackIds: [965],
        aliases: [
            'Vibration_Sensor_Y_FB_PkPk_Accel_G',
            'Vibration_Sensor_Y_FB_Pk_Pk_Accel_G',
            'Vibration_Sensor_Y_PkPk_Accel_G',
            'Y_FB_PkPk_Accel_G',
            'Y_PkPk_Accel_G'
        ]
    },
    {
        //key: 'zPkPk',
        //title: 'Z Pk-Pk Accel (G)',
        //shortTitle: 'Z Pk-Pk Accel (G)',
        //unit: 'G',
        //address: '40010',
        //fallbackIds: [700],
        key: 'zPkPk',
        title: 'Z Pk-Pk Accel (G)',
        shortTitle: 'Z Pk-Pk Accel (G)',
        unit: 'G',
        address: '40010',
        fallbackIds: [700],
        bEndFallbackIds: [966],
        aliases: [
            'Vibration_Sensor_Z_FB_PkPk_Accel_G',
            'Vibration_Sensor_Z_FB_Pk_Pk_Accel_G',
            'Vibration_Sensor_Z_PkPk_Accel_G',
            'Z_FB_PkPk_Accel_G',
            'Z_PkPk_Accel_G'
        ]
    },
    {
        key: 'temperature',
        title: 'Temperature (°C)',
        shortTitle: 'Temperature (°C)',
        unit: '°C',
        //address: '40043',
        //fallbackIds: [697],
        //type: 'temperature',
        address: '40043',
        fallbackIds: [697],
        bEndFallbackIds: [963],
        type: 'temperature',
        aliases: [
            'Vibration_Sensor_Temperature_F',
            'Vibration_Sensor_Temperature_C',
            'Vibration_Sensor_Temperature',
            'Vibration_Temperature_F',
            'Vibration_Temperature_C',
            'Temperature_F',
            'Temperature_C',
            'Temperature'
        ]
    },
    {
        key: 'motorRunFlag',
        title: 'Motor Run Flag',
        shortTitle: 'Motor Run Flag',
        unit: '',
        //address: '40029',
        //fallbackIds: [719],
        //type: 'flag',
        address: '40029',
        fallbackIds: [719],
        bEndFallbackIds: [985],
        type: 'flag',
        aliases: [
            'Vibration_Sensor_Motor_Run_Flag',
            'Vibration_Sensor_MotorRunFlag',
            'Vibration_Motor_Run_Flag',
            'Motor_Run_Flag',
            'MotorRunFlag',
            'Motor Run Flag'
        ]
    }
];

/*
 * Scalar vibration columns.
 *
 * Temperature and Motor Run Flag are transmitted on the RDPMS packet, not
 * on the vibration waveform packet. Depending on the device profile their
 * attribute name may therefore arrive WITHOUT the "Vibration_Sensor_"
 * prefix (for example "Temperature_C-A end" or "Motor_Run_Flag-B end").
 *
 * The prefix gates below must not drop them, otherwise the Temperature and
 * Motor Run Flag cells stay empty for both ends.
 */
var PM_VIBRATION_SCALAR_COLUMN_KEYS = [
    'temperature',
    'motorRunFlag'
];

/*
 * True when the attribute is one of the scalar vibration columns, matched
 * by AssetAttributeId first and by configured alias second.
 */
function pmVibrationIsScalarAttribute(
    attributeName,
    attributeData
) {
    attributeData = attributeData || {};

    var attrId =
        parseInt(
            attributeData.AssetAttributeId ||
            attributeData.assetAttributeId ||
            attributeData.EdgeXAttributeId ||
            attributeData.edgeXAttributeId ||
            attributeData.AttrId ||
            attributeData.attrId ||
            0,
            10
        );

    var candidateNames = [
        attributeName,
        attributeData.AssetAttributeName,
        attributeData.AttributeName,
        attributeData.attributeName
    ];

    for (
        var columnIndex = 0;
        columnIndex < PM_VIBRATION_TABLE_COLUMNS.length;
        columnIndex++
    ) {
        var column =
            PM_VIBRATION_TABLE_COLUMNS[columnIndex];

        if (
            PM_VIBRATION_SCALAR_COLUMN_KEYS.indexOf(
                column.key
            ) === -1
        ) {
            continue;
        }

        //if (
        //    attrId > 0 &&
        //    column.fallbackIds &&
        //    column.fallbackIds.indexOf(attrId) !== -1
        //) {
        //    return true;
        //}

        //var aliases = column.aliases || [];

        if (
            attrId > 0 &&
            column.fallbackIds &&
            column.fallbackIds.indexOf(attrId) !== -1
        ) {
            return true;
        }

        if (
            attrId > 0 &&
            column.bEndFallbackIds &&
            column.bEndFallbackIds.indexOf(attrId) !== -1
        ) {
            return true;
        }

        var aliases = column.aliases || [];

        for (
            var nameIndex = 0;
            nameIndex < candidateNames.length;
            nameIndex++
        ) {
            var sourceName =
                candidateNames[nameIndex];

            if (!sourceName) continue;

            var normalizedName =
                pmVibrationNormalize(
                    pmVibrationStripEnd(
                        sourceName
                    )
                );

            for (
                var aliasIndex = 0;
                aliasIndex < aliases.length;
                aliasIndex++
            ) {
                if (
                    normalizedName ===
                    pmVibrationNormalize(
                        aliases[aliasIndex]
                    )
                ) {
                    return true;
                }
            }
        }
    }

    return false;
}







// ================================================================
// POINT MACHINE VIBRATION TABLE HELPERS
// ================================================================

function pmVibrationNormalize(value) {
    return String(value == null ? '' : value)
        .trim()
        .toUpperCase()
        .replace(/FULL[\s_-]*BAND/g, 'FB')
        .replace(/PEAK[\s_-]*TO[\s_-]*PEAK/g, 'PKPK')
        .replace(/PEAK[\s_-]*PEAK/g, 'PKPK')
        .replace(/PK[\s_-]*PK/g, 'PKPK')
        .replace(/[^A-Z0-9]/g, '');
}

function pmVibrationStripEnd(attributeName) {
    return String(attributeName || '')
        .replace(/[\s_-]*A[\s_-]*END(?:[\s_-]*A[\s_-]*END)?$/i, '')
        .replace(/[\s_-]*B[\s_-]*END(?:[\s_-]*B[\s_-]*END)?$/i, '')
        .replace(/[\s_-]+$/, '')
        .trim();
}

//function pmVibrationResolveEnd(
//    attributeName
//) {
//    var raw =
//        String(attributeName || '')
//            .trim();

//    if (!raw) return '';

//    // Strip trailing event-kind suffix (-Max, -Avg, -Array, -Min, -Count, -OperationTime)
//    // so "Vibration_Sensor_X_HF_RMS_Accel_G-A end-Max" resolves end correctly.
//    raw = raw.replace(/-(Max|Avg|Array|Min|Count|OperationTime)$/i, '');

//    if (
//        /(?:^|[\s_-])A[\s_-]*END(?:[\s_-]*A[\s_-]*END)?$/i.test(
//            raw
//        )
//    ) {
//        return 'A';
//    }

//    if (
//        /(?:^|[\s_-])B[\s_-]*END(?:[\s_-]*B[\s_-]*END)?$/i.test(
//            raw
//        )
//    ) {
//        return 'B';
//    }

//    return '';
//}

function pmVibrationResolveEnd(
    attributeName,
    attributeData
) {
    var raw =
        String(attributeName || '')
            .trim();

    if (raw) {
        // Strip trailing event-kind suffix (-Max, -Avg, -Array, -Min, -Count, -OperationTime)
        // so "Vibration_Sensor_X_HF_RMS_Accel_G-A end-Max" resolves end correctly.
        var rawNoSuffix =
            raw.replace(/-(Max|Avg|Array|Min|Count|OperationTime)$/i, '');

        if (
            /(?:^|[\s_-])A[\s_-]*END(?:[\s_-]*A[\s_-]*END)?$/i.test(
                rawNoSuffix
            )
        ) {
            return 'A';
        }

        if (
            /(?:^|[\s_-])B[\s_-]*END(?:[\s_-]*B[\s_-]*END)?$/i.test(
                rawNoSuffix
            )
        ) {
            return 'B';
        }
    }

    /*
     * Name-based resolution found no "A End"/"B End" suffix.
     * Fall back to resolving the end from the numeric AssetAttributeId,
     * since A-end and B-end sensors report on separate, distinct IDs
     * (see PM_VIBRATION_TABLE_COLUMNS fallbackIds / bEndFallbackIds).
     */
    attributeData = attributeData || {};

    var attrId =
        parseInt(
            attributeData.AssetAttributeId ||
            attributeData.assetAttributeId ||
            attributeData.EdgeXAttributeId ||
            attributeData.edgeXAttributeId ||
            attributeData.AttrId ||
            attributeData.attrId ||
            0,
            10
        );

    if (attrId > 0) {
        for (
            var columnIndex = 0;
            columnIndex < PM_VIBRATION_TABLE_COLUMNS.length;
            columnIndex++
        ) {
            var column =
                PM_VIBRATION_TABLE_COLUMNS[columnIndex];

            if (
                column.bEndFallbackIds &&
                column.bEndFallbackIds.indexOf(attrId) !== -1
            ) {
                return 'B';
            }

            if (
                column.fallbackIds &&
                column.fallbackIds.indexOf(attrId) !== -1
            ) {
                return 'A';
            }
        }
    }

    return '';
}

function pmVibrationResolveMetric(
    attributeName,
    attributeData,
    assetId
) {
    attributeData =
        attributeData || {};

    /*
     * Look up the configured vibration attribute for this
     * exact asset.
     *
     * This map contains only Vibration_Sensor_* entries.
     */
    var bulkMetadata =
        getPointMachineVibrationBulkAttribute(
            assetId ||
            attributeData.AssetId ||
            attributeData.assetId,
            attributeData,
            attributeName
        );

    /*
     * Check the metadata Title/AliasName first, then retain
     * the existing WebSocket-name fallback.
     */
    var candidateNames = [];

    if (bulkMetadata) {
        if (bulkMetadata.title) {
            candidateNames.push(
                bulkMetadata.title
            );
        }

        if (bulkMetadata.aliasName) {
            candidateNames.push(
                bulkMetadata.aliasName
            );
        }
    }

    candidateNames.push(
        attributeName,
        attributeData.AssetAttributeName,
        attributeData.AttributeName,
        attributeData.attributeName
    );

    for (
        var candidateIndex = 0;
        candidateIndex <
        candidateNames.length;
        candidateIndex++
    ) {
        var sourceName =
            candidateNames[
            candidateIndex
            ];

        if (!sourceName) continue;

        var normalizedName =
            pmVibrationNormalize(
                pmVibrationStripEnd(
                    sourceName
                )
            );

        for (
            var columnIndex = 0;
            columnIndex <
            PM_VIBRATION_TABLE_COLUMNS.length;
            columnIndex++
        ) {
            var column =
                PM_VIBRATION_TABLE_COLUMNS[
                columnIndex
                ];

            var aliases =
                column.aliases || [];

            for (
                var aliasIndex = 0;
                aliasIndex <
                aliases.length;
                aliasIndex++
            ) {
                if (
                    normalizedName ===
                    pmVibrationNormalize(
                        aliases[aliasIndex]
                    )
                ) {
                    return column;
                }
            }
        }
    }

    /*
     * Preserve the existing hardcoded ID fallback.
     */
    var attrId =
        parseInt(
            attributeData.AssetAttributeId ||
            attributeData.assetAttributeId ||
            attributeData.EdgeXAttributeId ||
            attributeData.edgeXAttributeId ||
            attributeData.AttrId ||
            attributeData.attrId ||
            0,
            10
        );

    for (
        var fallbackIndex = 0;
        fallbackIndex <
        PM_VIBRATION_TABLE_COLUMNS.length;
        fallbackIndex++
    ) {
        var fallbackColumn =
            PM_VIBRATION_TABLE_COLUMNS[
            fallbackIndex
            ];

        if (
            attrId > 0 &&
            fallbackColumn.fallbackIds &&
            fallbackColumn.fallbackIds.indexOf(
                attrId
            ) !== -1
        ) {
            return fallbackColumn;
        }

        if (
            attrId > 0 &&
            fallbackColumn.bEndFallbackIds &&
            fallbackColumn.bEndFallbackIds.indexOf(
                attrId
            ) !== -1
        ) {
            return fallbackColumn;
        }
    }

    return null;
}

function pmVibrationGetValidTimestamp(
    rawData
) {
    rawData = rawData || {};

    var raw =
        rawData.raw || {};

    var candidates = [
        rawData.TimestampDevice,
        rawData.timestampDevice,

        raw.TimestampDevice,

        rawData.TimestampLocal,
        rawData.timestampLocal,

        raw.TimestampLocal,

        rawData.TimestampEdgeX,
        rawData.timestampEdgeX,

        raw.TimestampEdgeX,

        rawData.Timestamp,
        rawData.timestamp
    ];

    for (
        var i = 0;
        i < candidates.length;
        i++
    ) {
        var timestamp =
            candidates[i];

        if (!timestamp) continue;

        var text =
            String(timestamp);

        if (
            text.indexOf(
                '0001-01-01'
            ) !== -1
        ) {
            continue;
        }

        var milliseconds =
            new Date(timestamp)
                .getTime();

        if (
            !isNaN(milliseconds) &&
            milliseconds > 0
        ) {
            return {
                value: timestamp,
                milliseconds:
                    milliseconds
            };
        }
    }

    return {
        value: null,
        milliseconds: 0
    };
}

function pmVibrationFormatMetricValue(
    column,
    valueData
) {
    if (!valueData) {
        return {
            text: '—',
            rawValue: null,
            stateClass: 'is-empty'
        };
    }

    var value = valueData.value;

    if (
        value === null ||
        value === undefined ||
        String(value).trim() === ''
    ) {
        return {
            text: '—',
            rawValue: value,
            stateClass: 'is-empty'
        };
    }

    column = column || {};

    /*
     * Motor Run Flag:
     * 1/true/on/running = Running
     * 0/false/off/idle = Idle
     */
    if (column.type === 'flag') {
        var normalizedFlag =
            String(value)
                .trim()
                .toLowerCase();

        var isRunning =
            normalizedFlag === '1' ||
            normalizedFlag === 'true' ||
            normalizedFlag === 'on' ||
            normalizedFlag === 'run' ||
            normalizedFlag === 'running';

        var isIdle =
            normalizedFlag === '0' ||
            normalizedFlag === 'false' ||
            normalizedFlag === 'off' ||
            normalizedFlag === 'idle' ||
            normalizedFlag === 'stop' ||
            normalizedFlag === 'stopped';

        if (isRunning) {
            return {
                text: 'Running',
                rawValue: value,
                stateClass: 'is-running'
            };
        }

        if (isIdle) {
            return {
                text: 'Idle',
                rawValue: value,
                stateClass: 'is-idle'
            };
        }

        return {
            text: String(value),
            rawValue: value,
            stateClass: 'is-unknown'
        };
    }

    var numericValue =
        parseFloat(value);

    if (isNaN(numericValue)) {
        return {
            text: String(value),
            rawValue: value,
            stateClass: 'has-text'
        };
    }

    /*
     * Screenshot requires Temperature in °C.
     *
     * Current WebSocket name is:
     * Vibration_Sensor_Temperature_F-A end
     *
     * Convert only attributes explicitly identified as Fahrenheit.
     * A Temperature_C value is displayed directly.
     */
    if (column.type === 'temperature') {
        var sourceName =
            String(
                valueData.attributeName ||
                ''
            ).toUpperCase();

        var isFahrenheit =
            /(?:^|[_\s-])TEMPERATURE[_\s-]*F(?:$|[_\s-])/i.test(
                sourceName
            ) ||
            /(?:^|[_\s-])TEMP[_\s-]*F(?:$|[_\s-])/i.test(
                sourceName
            );

        var celsiusValue =
            isFahrenheit
                ? (
                    (numericValue - 32) *
                    5 / 9
                )
                : numericValue;

        return {
            text:
                celsiusValue.toFixed(1) +
                ' °C',

            rawValue: value,
            stateClass: 'is-temperature'
        };
    }

    return {
        text: tlZeroFloor(numericValue).toFixed(3),
        rawValue: value,
        stateClass: 'is-measurement'
    };
}

function pmVibrationPad2(value) {
    return value < 10 ? '0' + value : String(value);
}

function pmVibrationFormatDateTime(timestamp) {
    if (!timestamp) return '—';

    var date = new Date(timestamp);

    if (isNaN(date.getTime())) return '—';

    return (
        pmVibrationPad2(date.getDate()) +
        '-' +
        pmVibrationPad2(date.getMonth() + 1) +
        '-' +
        date.getFullYear() +
        ' ' +
        pmVibrationPad2(date.getHours()) +
        ':' +
        pmVibrationPad2(date.getMinutes()) +
        ':' +
        pmVibrationPad2(date.getSeconds())
    );
}

function pmVibrationEscape(value) {
    if (typeof escapeHtml === 'function') {
        return escapeHtml(value);
    }

    return String(value == null ? '' : value)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}


// ── Vibration-only bulk metadata cache ───────────────────────────────
function isPointMachineVibrationAttributeName(name) {
    return String(name || '')
        .trim()
        .toLowerCase()
        .indexOf('vibration_sensor_') === 0;
}

function resetPointMachineVibrationBulkMetadata() {
    pmVibrationBulkMetadata = {};
    pmVibrationBulkLoadedSiteId = null;
    pmVibrationBulkLoadingSiteId = null;
}

function registerPointMachineVibrationBulkAttribute(
    assetId,
    attribute
) {
    if (!attribute) return;

    var aid =
        String(assetId || '').trim();

    if (!aid) return;

    var attributeId =
        String(
            attribute.Id != null
                ? attribute.Id
                : (
                    attribute.AssetAttributeId != null
                        ? attribute.AssetAttributeId
                        : ''
                )
        ).trim();

    var title =
        String(
            attribute.Title ||
            attribute.AttributeName ||
            ''
        ).trim();

    var aliasName =
        String(
            attribute.AliasName ||
            ''
        ).trim();

    /*
     * Reject every normal Point Machine attribute.
     * Only Vibration_Sensor_* metadata is retained.
     */
    if (
        !isPointMachineVibrationAttributeName(title) &&
        !isPointMachineVibrationAttributeName(aliasName)
    ) {
        return;
    }

    if (!pmVibrationBulkMetadata[aid]) {
        pmVibrationBulkMetadata[aid] = {
            byId: {},
            byName: {}
        };
    }

    var entry = {
        id: attributeId,
        title: title,
        aliasName: aliasName
    };

    if (attributeId) {
        pmVibrationBulkMetadata[aid]
            .byId[attributeId] = entry;
    }

    if (title) {
        pmVibrationBulkMetadata[aid]
            .byName[
            pmVibrationNormalize(title)
        ] = entry;
    }

    if (aliasName) {
        pmVibrationBulkMetadata[aid]
            .byName[
            pmVibrationNormalize(aliasName)
        ] = entry;
    }
}

function getPointMachineVibrationBulkAttribute(
    assetId,
    attributeData,
    attributeName
) {
    var aid =
        String(assetId || '').trim();

    if (!aid) return null;

    var assetMetadata =
        pmVibrationBulkMetadata[aid];

    if (!assetMetadata) return null;

    attributeData =
        attributeData || {};

    var candidateIds = [
        attributeData.AssetAttributeId,
        attributeData.assetAttributeId,
        attributeData.AttributeId,
        attributeData.attributeId,
        attributeData.AttrId,
        attributeData.attrId
    ];

    for (
        var idIndex = 0;
        idIndex < candidateIds.length;
        idIndex++
    ) {
        var id =
            String(
                candidateIds[idIndex] == null
                    ? ''
                    : candidateIds[idIndex]
            ).trim();

        if (
            id &&
            assetMetadata.byId[id]
        ) {
            return assetMetadata.byId[id];
        }
    }

    var candidateNames = [
        attributeName,
        attributeData.AssetAttributeName,
        attributeData.AttributeName,
        attributeData.attributeName
    ];

    for (
        var nameIndex = 0;
        nameIndex < candidateNames.length;
        nameIndex++
    ) {
        var normalizedName =
            pmVibrationNormalize(
                candidateNames[nameIndex]
            );

        if (
            normalizedName &&
            assetMetadata.byName[
            normalizedName
            ]
        ) {
            return assetMetadata.byName[
                normalizedName
            ];
        }
    }

    return null;
}

function loadPointMachineVibrationBulkMetadata(
    siteId,
    callback
) {
    siteId =
        $.trim(
            String(siteId || '')
        );

    if (!siteId || siteId === '0') {
        if (callback) callback();
        return;
    }

    /*
     * Cache hit for the same station.
     */
    if (
        pmVibrationBulkLoadedSiteId === siteId
    ) {
        if (callback) callback();
        return;
    }

    /*
     * Avoid duplicate simultaneous requests.
     */
    if (
        pmVibrationBulkLoadingSiteId === siteId
    ) {
        setTimeout(function () {
            loadPointMachineVibrationBulkMetadata(
                siteId,
                callback
            );
        }, 100);

        return;
    }

    pmVibrationBulkLoadingSiteId =
        siteId;

    var payload = {
        SearchCriteria: {
            SiteId: siteId,
            AssetTypeId: 3
        }
    };

    console.log(
        '[PM Vibration Bulk] Fetching metadata for site=' +
        siteId
    );

    $.ajax({
        url:
            '/FRS25/Telemetry/GetBulkAssetMetadata',

        type: 'POST',

        contentType:
            'application/json',

        dataType:
            'json',

        data:
            JSON.stringify(payload),

        success: function (response) {
            /*
             * Clear only the vibration metadata cache.
             * No existing Point Machine map is touched.
             */
            pmVibrationBulkMetadata = {};

            var assets = [];

            if (
                response &&
                response.success === true &&
                Array.isArray(response.mAssets)
            ) {
                assets =
                    response.mAssets;
            }

            for (
                var assetIndex = 0;
                assetIndex < assets.length;
                assetIndex++
            ) {
                var asset =
                    assets[assetIndex];

                if (
                    !asset ||
                    asset.Id == null
                ) {
                    continue;
                }

                var attributes =
                    Array.isArray(
                        asset.assetAttributes
                    )
                        ? asset.assetAttributes
                        : [];

                for (
                    var attrIndex = 0;
                    attrIndex <
                    attributes.length;
                    attrIndex++
                ) {
                    registerPointMachineVibrationBulkAttribute(
                        asset.Id,
                        attributes[attrIndex]
                    );
                }
            }

            pmVibrationBulkLoadedSiteId =
                siteId;

            pmVibrationBulkLoadingSiteId =
                null;

            console.log(
                '[PM Vibration Bulk] Loaded site=' +
                siteId +
                ', vibration assets=' +
                Object.keys(
                    pmVibrationBulkMetadata
                ).length
            );

            if (callback) callback();
        },

        error: function (
            xhr,
            status,
            error
        ) {
            pmVibrationBulkLoadingSiteId =
                null;

            console.warn(
                '[PM Vibration Bulk] Request failed',
                {
                    siteId: siteId,
                    status: status,
                    error: error,
                    responseText:
                        xhr &&
                        xhr.responseText
                }
            );

            /*
             * Do not stop normal Point Machine loading
             * when vibration metadata fails.
             */
            if (callback) callback();
        }
    });
}

// ── Modal refresh, vibration table and event graph ───────────────────

/*
 * Leading+trailing throttle. The WebSocket calls this once per vibration
 * attribute (~20 per burst) and each call forces a synchronous reflow per
 * changed card. The first call still paints immediately; the rest coalesce.
 */
var _vibModalLastRun = 0;
var _vibModalTrailTimer = null;
var _VIB_MODAL_MIN_MS = 300;

function fnUpdateVibrationModal(assetId) {
    if (!_vibModalAssetId || String(assetId) !== _vibModalAssetId) return;

    var now = (Date.now ? Date.now() : new Date().getTime());
    var sinceLast = now - _vibModalLastRun;

    if (sinceLast >= _VIB_MODAL_MIN_MS) {
        _vibModalLastRun = now;
        _fnUpdateVibrationModalNow(assetId);
        return;
    }

    if (_vibModalTrailTimer) return;

    _vibModalTrailTimer = setTimeout(function () {
        _vibModalTrailTimer = null;
        if (!_vibModalAssetId) return;
        _vibModalLastRun = (Date.now ? Date.now() : new Date().getTime());
        _fnUpdateVibrationModalNow(_vibModalAssetId);
    }, _VIB_MODAL_MIN_MS - sinceLast);
}

function _fnUpdateVibrationModalNow(assetId) {
    if (!_vibModalAssetId || String(assetId) !== _vibModalAssetId) return;
    var $grid = $('#vibCardGrid');
    if (!$grid.length) return;

    var vd = _getVibAttrs(_vibModalAssetId);
    if (!vd) {
        $grid.html(
            '<div class="vib-no-data">' +
            '<i class="fas fa-wave-square" style="font-size:32px;color:#22d3ee;opacity:.3;display:block;margin-bottom:12px;"></i>' +
            'No vibration data received yet.<br>' +
            '<small style="color:rgba(255,255,255,0.45);">Waiting for WebSocket messages\u2026</small>' +
            '</div>'
        );
        return;
    }

    if (vd.lastTimestamp) {
        $('#vibLastTs').text('Last update: ' + fnFormatVibTs(vd.lastTimestamp));
    }

    var attrNames = Object.keys(vd.attrs).sort();
    for (var i = 0; i < attrNames.length; i++) {
        var attrName = attrNames[i];
        var attr = vd.attrs[attrName];
        var label = _vibLabel(attrName);
        var safeId = attrName.replace(/[^a-zA-Z0-9]/g, '_');
        var cardId = 'vibCard_' + _vibModalAssetId + '_' + safeId;
        var valId = 'vibVal_' + _vibModalAssetId + '_' + safeId;
        var tsId = 'vibTs_' + _vibModalAssetId + '_' + safeId;
        //var isNull = (attr.value === null || attr.value === undefined);
        //var valDisp = isNull ? '\u2014' : Number(attr.value).toFixed(3);
        //var tsDisp = fnFormatVibTs(attr.timestamp);
        //var valCls = isNull ? 'vib-val vib-null' : 'vib-val';
        var isNull = (attr.value === null || attr.value === undefined || attr.value === '' || Number(attr.value) === 0);
        if (isNull) continue;
        /*var valDisp = Number(attr.value).toFixed(3);*/
        var valDisp = tlZeroFloor(Number(attr.value)).toFixed(3);
        var tsDisp = fnFormatVibTs(attr.timestamp);
        var valCls = 'vib-val';

        var $card = $('#' + cardId);
        if ($card.length) {
            var $v = $('#' + valId);
            if ($v.text() !== valDisp) {
                $v.text(valDisp).removeClass('vib-val-flash');
                void $v[0].offsetWidth;
                $v.addClass('vib-val-flash');
                $card.removeClass('vib-card-flash');
                void $card[0].offsetWidth;
                $card.addClass('vib-card-flash');
            }
            $('#' + tsId).text(tsDisp);
        } else {
            $grid.append(
                '<div class="vib-card" id="' + cardId + '">' +
                '<div class="vib-card-head">' +
                '<span class="vib-attr-name" title="' + attrName + '">' + label + '</span>' +
                '</div>' +
                '<div class="vib-card-body">' +
                '<div class="vib-val-row">' +
                '<span class="vib-val-label">Value</span>' +
                '<span class="' + valCls + '" id="' + valId + '">' + valDisp + '</span>' +
                '</div>' +
                '</div>' +
                '<div class="vib-card-footer">' +
                '<i class="fas fa-clock"></i>' +
                '<span id="' + tsId + '">' + tsDisp + '</span>' +
                '</div>' +
                '</div>'
            );
        }
    }
}



// ================================================================
// POINT MACHINE VIBRATION TABLE
// Additive view below existing PM Table and PM card view.
// ================================================================

function pmVibrationGetSelectedAssetIds() {
    /*
     * Use wsLiveData as the authoritative asset list.
     *
     * Reason:
     * wsLiveData is populated by every active WebSocket ingest path.
     * wsVibrationData is retained for the existing vibration modal,
     * but the new table must not depend on that secondary store.
     */
    var assetIds = Object.keys(wsLiveData || {});

    if (
        wsCurrentFilterAssetIds &&
        wsCurrentFilterAssetIds.length > 0 &&
        wsCurrentFilterAssetIds[0] !== '' &&
        wsCurrentFilterAssetIds[0] !== '0'
    ) {
        var selectedIds =
            wsCurrentFilterAssetIds.map(String);

        assetIds = assetIds.filter(function (assetId) {
            return selectedIds.indexOf(
                String(assetId)
            ) !== -1;
        });
    }

    if (
        wsValidAssetIds &&
        wsValidAssetIds.length > 0
    ) {
        assetIds = assetIds.filter(function (assetId) {
            return wsValidAssetIds.indexOf(
                String(assetId)
            ) !== -1;
        });
    }

    assetIds = assetIds.filter(function (assetId) {
        var asset =
            wsLiveData[String(assetId)] ||
            wsLiveData[assetId];

        if (!asset) return false;

        return (
            parseInt(
                asset.AssetTypeId ||
                wsCurrentAssetTypeId ||
                0,
                10
            ) === 3
        );
    });

    assetIds.sort(function (leftId, rightId) {
        var leftAsset =
            wsLiveData[String(leftId)] ||
            wsLiveData[leftId] ||
            {};

        var rightAsset =
            wsLiveData[String(rightId)] ||
            wsLiveData[rightId] ||
            {};

        return String(
            leftAsset.AssetName || ''
        ).localeCompare(
            String(
                rightAsset.AssetName || ''
            ),
            undefined,
            {
                numeric: true,
                sensitivity: 'base'
            }
        );
    });

    return assetIds;
}

function pmVibrationBuildEndModel(
    assetId,
    endName
) {
    var liveAsset =
        wsLiveData[String(assetId)] ||
        wsLiveData[assetId];

    var model = {
        assetId: String(assetId),

        assetName:
            liveAsset &&
                liveAsset.AssetName
                ? liveAsset.AssetName
                : ('Asset ' + assetId),

        end: endName,

        timestamp: null,
        timestampMilliseconds: 0,

        values: {}
    };

    for (
        var initializeIndex = 0;
        initializeIndex <
        PM_VIBRATION_TABLE_COLUMNS.length;
        initializeIndex++
    ) {
        model.values[
            PM_VIBRATION_TABLE_COLUMNS[
                initializeIndex
            ].key
        ] = null;
    }

    /*
     * An end with VibrationA / VibrationB null is not built at all:
     * the empty model keeps every metric null, so the row, the modal
     * and the Excel / CSV / PDF export all show that end as blank.
     */
    if (
        !liveAsset ||
        !liveAsset.attrs ||
        !pmVibrationEndEnabled(assetId, endName)
    ) {
        return model;
    }

    var attrs = liveAsset.attrs;
    var attributeNames =
        Object.keys(attrs);

    for (
        var attributeIndex = 0;
        attributeIndex <
        attributeNames.length;
        attributeIndex++
    ) {
        var attributeName =
            attributeNames[
            attributeIndex
            ];

        var attribute =
            attrs[attributeName];

        if (!attribute) continue;

        var actualName =
            attribute.AssetAttributeName ||
            attribute.AttributeName ||
            attribute.attrName ||
            attributeName;

        /*
         * Fast rejection:
         * ignore operation, relay, waveform, DataLogger,
         * kurtosis, crest factor and other PM attributes.
         */
        if (
            String(actualName)
                .toLowerCase()
                .indexOf(
                    'vibration_sensor_'
                ) !== 0 &&
            /*
             * Keep Temperature and Motor Run Flag even when they arrive on
             * the RDPMS packet without the Vibration_Sensor_ prefix.
             */
            !pmVibrationIsScalarAttribute(
                actualName,
                attribute
            )
        ) {
            continue;
        }


        /*
 * Max, Avg, Min, Array, Count and OperationTime are event
 * attributes. They must not replace the actual live value.
 */
        if (
            /[\s_-]*(Max|Avg|Min|Array|Count|OperationTime)$/i.test(
                String(actualName)
            )
        ) {
            continue;
        }

        //var resolvedEnd =
        //    pmVibrationResolveEnd(
        //        actualName
        //    );

        var resolvedEnd =
            pmVibrationResolveEnd(
                actualName,
                attribute
            );

        if (resolvedEnd !== endName) {
            continue;
        }

        var metric =
            pmVibrationResolveMetric(
                actualName,
                attribute,
                assetId
            );

        if (!metric) continue;

        var timestampInfo =
            pmVibrationGetValidTimestamp(
                attribute
            );

        /*
         * storeWsAttribute keeps the live reading as Value.
         * The lower-case value fallback preserves compatibility
         * with the old wsVibrationData shape.
         */
        var rawValue =
            attribute.Value !== undefined
                ? attribute.Value
                : attribute.value;

        var incomingHasValue =
            rawValue !== null &&
            rawValue !== undefined &&
            String(rawValue).trim() !== '';

        var existingValue =
            model.values[metric.key];

        var existingHasValue =
            existingValue &&
            existingValue.value !== null &&
            existingValue.value !== undefined &&
            String(
                existingValue.value
            ).trim() !== '';

        var existingTimestamp =
            existingValue
                ? existingValue
                    .timestampMilliseconds || 0
                : 0;

        /*
         * Latest value wins.
         * A blank value cannot erase a valid existing reading.
         */
        var shouldUse =
            !existingValue ||
            (
                incomingHasValue &&
                (
                    !existingHasValue ||
                    timestampInfo.milliseconds >=
                    existingTimestamp
                )
            );

        if (shouldUse) {
            model.values[metric.key] = {
                value: rawValue,

                timestamp:
                    timestampInfo.value,

                timestampMilliseconds:
                    timestampInfo.milliseconds,

                attributeName:
                    actualName,

                attribute:
                    attribute
            };
        }

        if (
            incomingHasValue &&
            timestampInfo.milliseconds >
            model.timestampMilliseconds
        ) {
            model.timestampMilliseconds =
                timestampInfo.milliseconds;

            model.timestamp =
                timestampInfo.value;
        }
    }

    return model;
}

function pmVibrationCellHtml(
    assetId,
    endName,
    column,
    valueData
) {
    var formatted =
        pmVibrationFormatMetricValue(
            column,
            valueData
        );

    var displayValue =
        formatted.text;

    var hasValue =
        displayValue !== '—';

    var metricTypeClass =
        column.type
            ? ' pm-vibration-type-' +
            pmVibrationEscape(
                column.type
            )
            : '';

    var valueStateClass =
        formatted.stateClass
            ? ' ' +
            pmVibrationEscape(
                formatted.stateClass
            )
            : '';

    var sourceTitle =
        valueData &&
            valueData.attributeName
            ? ' title="' +
            pmVibrationEscape(
                valueData.attributeName
            ) +
            '"'
            : '';

    return (
        '<td class="pm-vibration-value-cell ' +
        (
            endName === 'A'
                ? 'pm-vibration-column-a'
                : 'pm-vibration-column-b'
        ) +
        (
            hasValue
                ? ' has-vibration-value'
                : ' no-vibration-value'
        ) +
        metricTypeClass +
        valueStateClass +
        '"' +

        ' data-vibration-asset="' +
        pmVibrationEscape(assetId) +
        '"' +

        ' data-vibration-end="' +
        pmVibrationEscape(endName) +
        '"' +

        ' data-vibration-key="' +
        pmVibrationEscape(column.key) +
        '"' +

        sourceTitle +
        '>' +

        '<div class="pm-vibration-live-reading" style="display:flex;align-items:center;justify-content:center;gap:6px;">' +
        (
            hasValue
                ? '<span class="pm-vibration-reading-dot" style="width:7px;height:7px;border-radius:50%;background:#16a34a;flex:0 0 auto;"></span>'
                : ''
        ) +
        '<strong style="' +
        (
            hasValue
                ? 'font-size:14px;font-weight:700;color:rgba(255,255,255,0.96);font-variant-numeric:tabular-nums;letter-spacing:.2px;'
                : 'font-size:13px;font-weight:500;color:rgba(255,255,255,0.45);'
        ) +
        '">' +
        pmVibrationEscape(displayValue) +
        '</strong>' +
        '</div>' +

        (function () {
            /*
             * Temperature and Motor Run Flag are scalar RDPMS readings.
             * They have no waveform Array and no backend Max / Avg, so the
             * event line is not rendered for them at all.
             */
            if (
                PM_VIBRATION_SCALAR_COLUMN_KEYS.indexOf(
                    column.key
                ) !== -1
            ) {
                return '';
            }

            // Look up Max/Avg by baseAttrId + end + eventKind
            var dbId =
                endName === 'B'
                    ? (column.bEndFallbackIds ? column.bEndFallbackIds[0] : null)
                    : (column.fallbackIds ? column.fallbackIds[0] : null);
            var evMax = null, evAvg = null;
            if (dbId && wsVibrationData[assetId] && wsVibrationData[assetId].attrs) {
                var _attrs = wsVibrationData[assetId].attrs;
                for (var _k in _attrs) {
                    if (!_attrs.hasOwnProperty(_k)) continue;
                    var _a = _attrs[_k];
                    if (parseInt(_a.baseAttrId) !== dbId) continue;
                    if (_a.end !== endName) continue;
                    if (_a.eventKind === 'max') evMax = _a.value;
                    else if (_a.eventKind === 'avg') evAvg = _a.value;
                }
            }

            if (evMax !== null || evAvg !== null) {
                /*var maxTxt = (evMax !== null && evMax !== '' && !isNaN(parseFloat(evMax))) ? parseFloat(evMax).toFixed(3) : '—';*/
                var maxTxt = (evMax !== null && evMax !== '' && !isNaN(parseFloat(evMax))) ? tlZeroFloor(parseFloat(evMax)).toFixed(3) : '—';
                /*var avgTxt = (evAvg !== null && evAvg !== '' && !isNaN(parseFloat(evAvg))) ? parseFloat(evAvg).toFixed(3) : '—';*/
                var avgTxt = (evAvg !== null && evAvg !== '' && !isNaN(parseFloat(evAvg))) ? tlZeroFloor(parseFloat(evAvg)).toFixed(3) : '—';
                return '<div class="pm-vibration-event-reading" style="margin-top:3px;font-size:10.5px;line-height:1.3;color:rgba(255,255,255,0.45);text-align:center;">' +
                    '<span style="color:rgba(255,255,255,0.45);">Pk / Avg&nbsp;</span>' +
                    '<a href="javascript:void(0)" onclick="pmShowVibrationEventGraph(\'' + assetId + '\',' + dbId + ',\'' + pmVibrationEscape(column.shortTitle) + '\',\'' + endName + '\')" ' +
                    'style="font-weight:600;color:#22d3ee;text-decoration:underline;cursor:pointer;" title="Click to view waveform">' +
                    pmVibrationEscape(maxTxt + ' / ' + avgTxt) + '</a></div>';
            }
            return '<div class="pm-vibration-event-reading" style="margin-top:3px;font-size:10.5px;color:rgba(255,255,255,0.45);text-align:center;">—</div>';
        })() +

        '</td>'
    );
}

function pmShowVibrationEventGraph(assetId, dbId, colTitle, endName) {
    var asset = wsLiveData[String(assetId)];
    var baseName = asset ? (asset.AssetName || assetId) : assetId;
    var name = (baseName.indexOf('PT-') === 0) ? baseName : 'PT-' + baseName;

    //// Find the -Array entry for this baseAttrId + end from wsVibrationData
    //var arrayValue = null;
    //if (wsVibrationData[assetId] && wsVibrationData[assetId].attrs) {
    //    var _attrs = wsVibrationData[assetId].attrs;
    //    for (var _k in _attrs) {
    //        if (!_attrs.hasOwnProperty(_k)) continue;
    //        var _a = _attrs[_k];
    //        if (parseInt(_a.baseAttrId) !== parseInt(dbId)) continue;
    //        if (_a.end !== endName) continue;
    //        if (_a.eventKind === 'array' && _a.value) {
    //            arrayValue = _a.value;
    //            break;
    //        }
    //    }
    //}


    // Find the -Array entry for this baseAttrId + end from wsVibrationData.
    // The Max / Avg readings and the Array timestamp are picked up in the same
    // pass, only to fill the modal's Date & Time / Max / Avg header. The
    // waveform itself is still bound exactly as before.
    var arrayValue = null;
    var arrayAttr = null;
    var evMaxValue = null;
    var evAvgValue = null;
    if (wsVibrationData[assetId] && wsVibrationData[assetId].attrs) {
        var _attrs = wsVibrationData[assetId].attrs;
        for (var _k in _attrs) {
            if (!_attrs.hasOwnProperty(_k)) continue;
            var _a = _attrs[_k];
            if (parseInt(_a.baseAttrId) !== parseInt(dbId)) continue;
            if (_a.end !== endName) continue;

            if (_a.eventKind === 'array' && _a.value) {
                if (!arrayValue) { arrayValue = _a.value; arrayAttr = _a; }
            } else if (_a.eventKind === 'max') {
                evMaxValue = _a.value;
            } else if (_a.eventKind === 'avg') {
                evAvgValue = _a.value;
            }
        }
    }

    // Header timestamp = the Array event's device timestamp, falling back to
    // the asset's last vibration frame.
    var eventTimestampMs = null;
    if (typeof pmVibrationGetValidTimestamp === 'function') {
        var _arrTs = pmVibrationGetValidTimestamp(arrayAttr || {});
        if (_arrTs && _arrTs.milliseconds) { eventTimestampMs = _arrTs.milliseconds; }

        if (!eventTimestampMs && wsVibrationData[assetId] && wsVibrationData[assetId].lastTimestamp) {
            var _lastTs = pmVibrationGetValidTimestamp({
                timestamp: wsVibrationData[assetId].lastTimestamp
            });
            if (_lastTs && _lastTs.milliseconds) { eventTimestampMs = _lastTs.milliseconds; }
        }
    }




    //var title = name + ' — ' + endName + ' End — ' + colTitle;
    //var modalHtml =
    //    '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
    //    '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1200px;width:96%;">' +
    //    '<div class="rdpms-graph-header" style="background:linear-gradient(135deg,#042c43 0%,#0a4a6e 100%);">' +
    //    '<h6 style="color:#fff;margin:0;font-size:18px;font-weight:700;display:flex;align-items:center;gap:10px;">' +
    //    '<i class="fas fa-wave-square"></i> ' + title + '</h6>' +
    //    '<button class="rdpms-graph-close" onclick="closeGraphModal()" style="color:#fff;">&times;</button>' +
    //    '</div>' +
    //    '<div class="rdpms-graph-body" style="padding:20px;background:rgba(255,255,255,0.04);">' +
    //    '<div id="vibArrayChartDiv" style="width:100%;height:500px;"></div>' +
    //    '<div id="vibArrayError" style="display:none;text-align:center;padding:50px;color:#ef4444;"></div>' +
    //    '</div></div></div>';

    var title = name + ' — ' + endName + ' End — ' + colTitle;

    // Engineering unit for the header, Y axis and tooltip, read off the
    // column title.
    var vibUnit = (function (label) {
        var t = String(label || '').toLowerCase();
        if (t.indexOf('temperature') !== -1 || t.indexOf('temp') !== -1) { return '\u00B0C'; }
        if (t.indexOf('velocity') !== -1 || t.indexOf('vel') !== -1) { return 'mm/s'; }
        if (t.indexOf('displacement') !== -1 || t.indexOf('disp') !== -1) { return '\u00B5m'; }
        if (t.indexOf('crest') !== -1 || t.indexOf('factor') !== -1) { return ''; }
        if (t.indexOf('accel') !== -1 || t.indexOf('rms') !== -1 ||
            t.indexOf('full band') !== -1 || t.indexOf('high frequency') !== -1 ||
            t.indexOf('hf') !== -1) {
            return 'G';
        }
        return '';
    })(colTitle);

    // dd-MM-yyyy HH:mm:ss, same format as the history header.
    var dateText = (function (ms) {
        if (ms === null || ms === undefined || isNaN(ms)) { return '\u2014'; }
        var d = new Date(Number(ms));
        if (isNaN(d.getTime()) || d.getFullYear() <= 1) { return '\u2014'; }
        function pad(v) { return ('0' + v).slice(-2); }
        return pad(d.getDate()) + '-' + pad(d.getMonth() + 1) + '-' + d.getFullYear() +
            ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    })(eventTimestampMs);

    // 3 decimals, zero-floored, so the header agrees with the Max / Avg cell
    // that opened this graph.
    function vibHeaderNumber(value) {
        if (value === null || value === undefined || String(value).trim() === '' ||
            isNaN(parseFloat(value))) {
            return '\u2014';
        }
        var n = parseFloat(value);
        if (typeof tlZeroFloor === 'function') { n = parseFloat(tlZeroFloor(n)); }
        if (isNaN(n)) { return '\u2014'; }
        return (Math.abs(n) >= 100) ? n.toFixed(1) : n.toFixed(3);
    }

    var maxText = vibHeaderNumber(evMaxValue);
    var avgText = vibHeaderNumber(evAvgValue);
    var unitSuffix = vibUnit ? ' ' + vibUnit : '';
    var spanStyle = 'display:inline-flex;align-items:center;gap:5px;white-space:nowrap;';

    var summaryHtml =
        '<div class="rdpms-graph-time pm-vib-graph-summary" style="display:flex;flex-wrap:wrap;' +
        'gap:8px 22px;align-items:center;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.10);' +
        'border-radius:8px;padding:10px 14px;margin-bottom:12px;font-size:13px;color:rgba(255,255,255,0.72);">' +

        '<span style="' + spanStyle + '">' +
        '<i class="far fa-clock" style="color:#22d3ee;"></i>' +
        '<strong style="color:#22d3ee;">Date &amp; Time:</strong> ' +
        pmVibrationEscape(dateText) + '</span>' +

        '<span style="' + spanStyle + '">' +
        '<strong style="color:#22d3ee;">Max:</strong> ' +
        pmVibrationEscape(maxText + (maxText === '—' ? '' : unitSuffix)) + '</span>' +

        '<span style="' + spanStyle + '">' +
        '<strong style="color:#22d3ee;">Avg:</strong> ' +
        pmVibrationEscape(avgText + (avgText === '—' ? '' : unitSuffix)) + '</span>' +

        '</div>';

    var modalHtml =
        '<div class="rdpms-graph-overlay" id="rdpmsGraphOverlay" onclick="closeGraphModal(event)">' +
        '<div class="rdpms-graph-modal" onclick="event.stopPropagation()" style="max-width:1200px;width:96%;">' +
        // No inline colours here: FRS Advance already themes .rdpms-graph-header
        // and .rdpms-graph-close, so the event graph matches the other modals.
        '<div class="rdpms-graph-header">' +
        '<h6><i class="fas fa-wave-square"></i> ' + title + '</h6>' +
        '<button class="rdpms-graph-close" onclick="closeGraphModal()">&times;</button>' +
        '</div>' +
        '<div class="rdpms-graph-body" style="padding:20px;background:rgba(255,255,255,0.04);">' +
        summaryHtml +
        '<div id="vibArrayChartDiv" style="width:100%;height:500px;"></div>' +
        '<div id="vibArrayError" style="display:none;text-align:center;padding:50px;color:#ef4444;"></div>' +
        '</div></div></div>';

    $('#rdpmsGraphOverlay').remove();
    $('body').append(modalHtml);

    if (!arrayValue || typeof arrayValue !== 'string' || arrayValue.indexOf(',') === -1) {
        $('#vibArrayChartDiv').hide();
        $('#vibArrayError').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:rgba(255,255,255,0.45);"></i>No array data received yet for this attribute.').show();
        return;
    }

    var arr = arrayValue.split(',').map(function (v) { return parseFloat(v.trim()); }).filter(function (v) { return !isNaN(v); });
    if (!arr.length) {
        $('#vibArrayChartDiv').hide();
        $('#vibArrayError').html('<i class="fas fa-info-circle fa-2x" style="display:block;margin-bottom:12px;color:rgba(255,255,255,0.45);"></i>Array is empty.').show();
        return;
    }

    //// Plot value vs sample index — no timestamps, no time interval
    //var chart = echarts.init(document.getElementById('vibArrayChartDiv'));
    //var xData = arr.map(function (_, i) { return String(i + 1); });
    //chart.setOption({
    //    backgroundColor: '#fff',
    //    title: {
    //        text: title,
    //        subtext: arr.length + ' samples',
    //        left: 'center',
    //        top: 10,
    //        textStyle: { fontSize: 16, fontWeight: '700', color: 'rgba(255,255,255,0.96)' },
    //        subtextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.50)' }
    //    },
    //    tooltip: {
    //        trigger: 'axis',
    //        formatter: function (params) {
    //            if (!params || !params.length) return '';
    //            var p = params[0];
    //            return 'Sample #' + p.axisValue + '<br/><strong>' + p.value + '</strong>';
    //        }
    //    },
    //    grid: { left: '8%', right: '4%', bottom: '10%', top: '18%' },
    //    xAxis: {
    //        type: 'category',
    //        data: xData,
    //        name: 'Sample Index',
    //        nameLocation: 'middle',
    //        nameGap: 30,
    //        axisLabel: { fontSize: 11, color: 'rgba(255,255,255,0.50)' }
    //    },
    //    yAxis: {
    //        type: 'value',
    //        name: 'Value',
    //        nameLocation: 'middle',
    //        nameGap: 45,
    //        axisLabel: { fontSize: 11, color: 'rgba(255,255,255,0.50)' },
    //        splitLine: { lineStyle: { color: 'rgba(255,255,255,0.10)' } }
    //    },
    //    series: [{
    //        type: 'line',
    //        data: arr,
    //        smooth: false,
    //        symbol: 'circle',
    //        symbolSize: arr.length <= 80 ? 5 : arr.length <= 160 ? 4 : 3,
    //        showSymbol: true,
    //        lineStyle: { width: 2.5, color: '#22d3ee' },
    //        itemStyle: { color: '#22d3ee' },
    //        areaStyle: {
    //            color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
    //                { offset: 0, color: 'rgba(26,110,116,0.25)' },
    //                { offset: 1, color: 'rgba(26,110,116,0.02)' }
    //            ])
    //        }
    //    }]
    //});
    //window.addEventListener('resize', function () { chart.resize(); });


    // Same waveform array, same lookup — only the presentation changes, so
    // the graph matches the Telemetry History vibration graph.
    // Single reusable instance: the previous waveform is released before a new
    // one is drawn, instead of leaking one chart per open.
    if (window._vibArrayChart) {
        try { window._vibArrayChart.dispose(); } catch (ignore) { }
        window._vibArrayChart = null;
    }
    var chart = echarts.init(document.getElementById('vibArrayChartDiv'));
    window._vibArrayChart = chart;

    var vibColor = '#22d3ee';
    var sampleIntervalMs = 20;
    var maxDurationMs = Math.max(0, (arr.length - 1) * sampleIntervalMs);

    // vibUnit was resolved above, with the modal header.

    // Round the X axis tick step up to a readable value (~7 ticks).
    var niceSteps = [20, 40, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000];
    var rawStep = (maxDurationMs || 20) / 7;
    var xAxisStepMs = niceSteps[niceSteps.length - 1];
    for (var _s = 0; _s < niceSteps.length; _s++) {
        if (niceSteps[_s] >= rawStep) { xAxisStepMs = niceSteps[_s]; break; }
    }

    // Sample index expressed as elapsed time. No new data is bound — the
    // n-th sample is simply labelled n x 20 ms.
    var xData = arr.map(function (_, i) { return i * sampleIntervalMs; });

    chart.setOption({
        backgroundColor: 'transparent',
        title: {
            text: title,
            subtext: arr.length + ' samples  \u00B7  \u0394t ' + sampleIntervalMs +
                ' ms  \u00B7  span ' + maxDurationMs + ' ms',
            left: 'center',
            top: 10,
            textStyle: { fontSize: 16, fontWeight: '700', color: 'rgba(255,255,255,0.96)' },
            subtextStyle: { fontSize: 11, color: 'rgba(255,255,255,0.50)' }
        },
        tooltip: {
            trigger: 'axis',
            backgroundColor: 'rgba(5,9,24,0.97)',
            borderColor: vibColor,
            borderWidth: 1,
            padding: [12, 16],
            formatter: function (params) {
                if (!params || !params.length) return '';

                var elapsedMs = params[0].value[0];
                var html = '<div style="font-weight:600;margin-bottom:8px;' +
                    'border-bottom:1px solid rgba(255,255,255,0.10);padding-bottom:6px;">' +
                    elapsedMs + ' ms <span style="color:rgba(255,255,255,0.50);font-weight:400;">(' +
                    (elapsedMs / 1000).toFixed(3) + ' s)</span></div>';

                params.forEach(function (point) {
                    if (!point.value || point.value[1] === undefined) return;

                    html += '<div style="display:flex;align-items:center;padding:3px 0;">' +
                        '<span style="display:inline-block;width:10px;height:3px;background:' +
                        point.color + ';margin-right:8px;"></span>' +
                        '<span style="flex:1;font-size:11px;">' +
                        (point.seriesName || 'Value') + ':</span>' +
                        '<span style="font-weight:600;margin-left:10px;">' +
                        Number(point.value[1]).toFixed(2) +
                        (vibUnit ? ' ' + vibUnit : '') + '</span></div>';
                });

                return html;
            }
        },
        legend: {
            data: [title],
            top: 50,
            left: 'center',
            textStyle: { fontSize: 11 },
            itemGap: 15,
            type: 'scroll'
        },
        grid: { top: 90, left: 55, right: 25, bottom: 75 },
        toolbox: {
            right: 15,
            top: 10,
            feature: {
                dataZoom: { yAxisIndex: 'none' },
                restore: {},
                saveAsImage: { pixelRatio: 2 }
            }
        },
        xAxis: {
            type: 'value',
            name: 'Elapsed Time (ms)',
            nameLocation: 'center',
            nameGap: 30,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.50)' },
            min: 0,
            max: maxDurationMs > 0 ? maxDurationMs : null,
            interval: xAxisStepMs,
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.50)',
                formatter: function (value) { return value + ' ms'; }
            },
            axisLine: { lineStyle: { color: 'rgba(255,255,255,0.10)' } },
            splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.06)' } }
        },
        yAxis: {
            type: 'value',
            name: vibUnit,
            nameTextStyle: { fontSize: 12, fontWeight: '600', color: 'rgba(255,255,255,0.50)' },
            axisLabel: {
                fontSize: 10,
                color: 'rgba(255,255,255,0.50)',
                formatter: function (value) { return Number(value).toFixed(2); }
            },
            axisLine: { show: true, lineStyle: { color: vibColor, width: 2 } },
            splitLine: { lineStyle: { color: 'rgba(255,255,255,0.06)', type: 'dashed' } }
        },
        dataZoom: [
            {
                type: 'slider',
                height: 22,
                bottom: 8,
                start: 0,
                end: 100,
                borderColor: 'rgba(255,255,255,0.10)',
                fillerColor: vibColor + '20',
                handleStyle: { color: vibColor }
            },
            { type: 'inside' }
        ],
        series: [{
            name: title,
            type: 'line',
            data: arr.map(function (v, i) { return [xData[i], v]; }),
            smooth: false,
            symbol: 'circle',
            symbolSize: arr.length <= 80 ? 5 : arr.length <= 160 ? 4 : 3,
            showSymbol: true,
            showAllSymbol: true,
            lineStyle: { width: 2.5, color: vibColor, type: 'dashed' },
            itemStyle: { color: vibColor },
            areaStyle: {
                color: new echarts.graphic.LinearGradient(0, 0, 0, 1, [
                    { offset: 0, color: vibColor + '40' },
                    { offset: 1, color: vibColor + '05' }
                ])
            }
        }]
    });
    $(window).off('resize.vibArray').on('resize.vibArray', function () {
        if (window._vibArrayChart) { window._vibArrayChart.resize(); }
    });
}
/*
 * Per-end display name for the vibration table.
 *
 * A point machine that drives both ends is stored under one combined name
 * ("115/116"), which used to render as a single "PT-115/116" cell spanning
 * the A End and B End rows. Each end is its own physical machine, so each
 * row now carries its own name: A End -> PT-115, B End -> PT-116.
 *
 * Resolution order:
 *   1. AliasDirectionA / AliasDirectionB from GetPMAssetMeta (authoritative,
 *      same source getPmEndMeta uses for the card labels).
 *   2. A combined "115/116" style name split on / - \ or & .
 *   3. Single-end machine (or nothing to split): the plain name.
 *   4. Two ends but one unsplittable name: suffix the end letter so the two
 *      rows stay distinguishable now that the End column is gone.
 */
function pmVibrationEndDisplayName(
    assetId,
    endName,
    assetName,
    endCount
) {
    var baseName =
        String(assetName || ('Asset ' + assetId))
            .trim()
            .replace(/^(PT-)+/i, '');

    var meta = pmAssetMetaCache[String(assetId)];

    var alias = meta
        ? String(
            (endName === 'A' ? meta.aliasA : meta.aliasB) || ''
        ).trim().replace(/^(PT-)+/i, '')
        : '';

    if (alias) {
        return 'PT-' + alias;
    }

    /*
     * "115/116", "115 & 116" -> A takes the first part, B the second.
     * A hyphen is only treated as a separator between two plain numbers
     * ("115-116"), never inside names such as "A-12" where the hyphen is
     * part of the name itself.
     */
    var pair =
        /^\s*([^\/\\&]+?)\s*[\/\\&]\s*([^\/\\&]+?)\s*$/.exec(baseName) ||
        /^\s*(\d+)\s*[-–—]\s*(\d+)\s*$/.exec(baseName);

    if (pair) {
        return 'PT-' + ((endName === 'B') ? pair[2] : pair[1]);
    }

    if (endCount > 1) {
        return 'PT-' + baseName + endName;
    }

    return 'PT-' + baseName;
}

function pmVibrationAssetRowHtml(
    assetId
) {
    if (!pmVibrationAssetHasEnabledEnd(assetId)) return '';

    // Ends that actually carry a sensor (VibrationA / VibrationB from
    // GetAssestBy). Each is its OWN row -- B stacked BELOW A -- so a machine
    // with one end shows one row and no blank cells for the missing end.
    var ends = [];
    if (pmVibrationEndEnabled(assetId, 'A')) ends.push('A');
    if (pmVibrationEndEnabled(assetId, 'B')) ends.push('B');
    if (!ends.length) return '';

    var html = '';
    for (var endIndex = 0; endIndex < ends.length; endIndex++) {
        var endName = ends[endIndex];
        var model = pmVibrationBuildEndModel(assetId, endName);

        // Each end gets its OWN name cell (no rowspan, no "PT-115/116"):
        // A End -> PT-115, B End -> PT-116.
        var assetName = pmVibrationEndDisplayName(
            assetId,
            endName,
            model.assetName,
            ends.length
        );

        var endClass = (endName === 'A') ? 'pm-vibration-column-a' : 'pm-vibration-column-b';

        html +=
            '<tr class="pm-vibration-asset-row pm-vibration-end-row ' + endClass + '"' +
            ' data-vibration-asset-row="' + pmVibrationEscape(String(assetId)) + '"' +
            ' data-vibration-end="' + endName + '">';

        html +=
            '<td class="pm-vibration-asset-cell">' +
            '<div class="pm-vibration-asset-name">' +
            '<i class="fas fa-cog"></i>' +
            '<strong>' + pmVibrationEscape(assetName) + '</strong>' +
            '</div>' +
            '</td>';

        html +=
            '<td class="pm-vibration-time-cell ' + endClass + '">' +
            '<span class="pm-vibration-time-value">' +
            pmVibrationEscape(pmVibrationFormatDateTime(model.timestamp)) +
            '</span>' +
            '</td>';

        for (var colIndex = 0; colIndex < PM_VIBRATION_TABLE_COLUMNS.length; colIndex++) {
            var column = PM_VIBRATION_TABLE_COLUMNS[colIndex];
            html += pmVibrationCellHtml(String(assetId), endName, column, model.values[column.key]);
        }

        html += '</tr>';
    }

    return html;
}
/*
 * True when a single point machine asset currently carries at least one
 * live vibration reading. Mirrors the exact filters used by
 * pmVibrationBuildEndModel so "has data" is true precisely when a real
 * value would appear in the table.
 */
function pmVibrationAssetHasData(assetId) {
    var liveAsset =
        wsLiveData[String(assetId)] ||
        wsLiveData[assetId];

    if (!liveAsset || !liveAsset.attrs) {
        return false;
    }

    var attrs = liveAsset.attrs;

    for (var name in attrs) {
        if (!attrs.hasOwnProperty(name)) continue;

        var attribute = attrs[name];
        if (!attribute) continue;

        var actualName =
            attribute.AssetAttributeName ||
            attribute.AttributeName ||
            attribute.attrName ||
            name;

        // Only live vibration_sensor_* attributes qualify.
        if (
            String(actualName)
                .toLowerCase()
                .indexOf('vibration_sensor_') !== 0
        ) {
            continue;
        }

        // Event attributes (Max/Avg/Min/Array/Count/OperationTime) don't count.
        if (
            /[\s_-]*(Max|Avg|Min|Array|Count|OperationTime)$/i.test(
                String(actualName)
            )
        ) {
            continue;
        }

        //// Must resolve to a real end and a configured metric column.
        //if (!pmVibrationResolveEnd(actualName)) continue;
        //if (!pmVibrationResolveMetric(actualName, attribute, assetId)) continue;

        // Must resolve to a real end and a configured metric column.
        var resolvedEndName = pmVibrationResolveEnd(actualName, attribute);
        if (!resolvedEndName) continue;

        // An end switched off by VibrationA / VibrationB never counts as data.
        if (!pmVibrationEndEnabled(assetId, resolvedEndName)) continue;

        if (!pmVibrationResolveMetric(actualName, attribute, assetId)) continue;

        var rawValue =
            attribute.Value !== undefined
                ? attribute.Value
                : attribute.value;

        if (
            rawValue !== null &&
            rawValue !== undefined &&
            String(rawValue).trim() !== ''
        ) {
            return true;
        }
    }

    return false;
}

/*
 * True when ANY selected point machine has live vibration data.
 * Drives whether the vibration section (and its toggle button) is shown.
 */
function pmVibrationHasAnyData() {
    var assetIds = pmVibrationGetSelectedAssetIds();

    for (var i = 0; i < assetIds.length; i++) {
        if (pmVibrationAssetHasData(assetIds[i])) {
            return true;
        }
    }

    return false;
}

/*
 * Show/hide the vibration table body via the header toggle button.
 * Updates the DOM in place (no full rebuild) and records the choice in
 * pmVibrationTablePanelOpen so later re-renders keep the same state.
 */
function pmVibrationToggleTable() {
    pmVibrationTablePanelOpen = !pmVibrationTablePanelOpen;

    var $body = $('#pmVibrationCollapseBody');
    var $btn = $('#pmVibrationToggleBtn');

    // aria-expanded carries the state; the open/closed colours come from
    // .pm-vibration-toggle-btn[aria-expanded="true"] in the stylesheet.
    if (pmVibrationTablePanelOpen) {
        $body.css('display', 'block');
        $btn
            .attr('aria-expanded', 'true')
            .html(
                '<i class="fas fa-eye-slash"></i>' +
                '<span>Hide Vibration Data</span>'
            );
    } else {
        $body.css('display', 'none');
        $btn
            .attr('aria-expanded', 'false')
            .html(
                '<i class="fas fa-eye"></i>' +
                '<span>Show Vibration Data</span>'
            );
    }
}

function buildPointMachineVibrationTableHtml() {
    /*
     * Renderable, not merely selected: a PM with no vibration sensor on
     * either end contributes no row. When that leaves nothing, the existing
     * empty-state branch below handles it.
     */
    var assetIds =
        pmVibrationGetRenderableAssetIds();


    var metricsCount =
        PM_VIBRATION_TABLE_COLUMNS.length;

    var totalColumns =
        1 +            // Asset Name (per end -- A and B are separate rows)
        1 +            // Date & Time
        metricsCount;  // one shared metric set (ends are stacked rows now)

    var currentViewType =
        String(
            $('#drpView').val() || ''
        );

    var isPointMachineCardView =
        currentViewType === 'PointMachine';

    var vibrationViewClass =
        isPointMachineCardView
            ? ' pm-vibration-pointmachine-view'
            : ' pm-vibration-table-view';

    var vibrationSectionTitle =
        isPointMachineCardView
            ? 'Vibration Condition'
            : 'Point Machine Vibration Monitoring';

    // Ends are stacked rows carrying their own asset name now, not a grouped
    // A End / B End column pair, so the description no longer claims grouping.
    var vibrationSectionDescription =
        isPointMachineCardView
            ? 'Live A End and B End vibration measurements'
            : 'Live vibration condition values';

    var html =
        '<section id="pmVibrationTableSection" class="pm-vibration-section' +
        vibrationViewClass +
        '">' +

        '<div class="pm-vibration-section-header">' +

        '<div class="pm-vibration-section-title">' +
        '<h6>' +
        '<span class="pm-vibration-title-icon">' +
        '<i class="fas fa-wave-square"></i>' +
        '</span>' +
        '<span>' +
        pmVibrationEscape(
            vibrationSectionTitle
        ) +
        '</span>' +
        '</h6>' +
        '<p>' +
        pmVibrationEscape(
            vibrationSectionDescription
        ) +
        '</p>' +
        '</div>' +

        '<div class="pm-vibration-header-meta">' +

        '<span class="pm-vibration-live-badge">' +
        '<span class="pm-vibration-live-dot"></span>' +
        'WebSocket Live' +
        '</span>' +

        '<span class="pm-vibration-metric-count">' +
        '<i class="fas fa-layer-group"></i>' +
        metricsCount +
        ' Parameters / End' +
        '</span>' +

        // Toggle button. The section is only rendered when vibration data
        // exists, so this button appears only when there is data to show.
        '<button type="button" id="pmVibrationToggleBtn"' +
        ' class="pm-vibration-toggle-btn"' +
        ' onclick="pmVibrationToggleTable()"' +
        ' aria-expanded="' +
        (pmVibrationTablePanelOpen ? 'true' : 'false') +
        '"' +
        // Colours come from .pm-vibration-toggle-btn (and its
        // [aria-expanded="true"] state) in telemetrylive-pmvibration.css.
        ' style="display:inline-flex;align-items:center;gap:6px;' +
        'cursor:pointer;font-weight:600;font-size:12px;line-height:1;' +
        'padding:7px 12px;border-radius:6px;transition:all .15s ease;">' +
        '<i class="fas ' +
        (pmVibrationTablePanelOpen ? 'fa-eye-slash' : 'fa-eye') +
        '"></i>' +
        '<span>' +
        (pmVibrationTablePanelOpen
            ? 'Hide Vibration Data'
            : 'Show Vibration Data') +
        '</span>' +
        '</button>' +

        '</div>' +
        '</div>' +

        '<div class="pm-vibration-collapse-body" id="pmVibrationCollapseBody"' +
        ' style="display:' +
        (pmVibrationTablePanelOpen ? 'block' : 'none') +
        ';">' +
        '<div class="pm-vibration-table-scroll">' +
        // data-export-label names the sheet / section in the Excel, CSV and
        // PDF exports, which scrape the rendered tables (tlCollectExportSections).
        '<table class="pm-vibration-table pm-vibration-stacked-layout" data-export-label="Vibration">' +
        '<thead>' +

        /*
         * A End / B End are separate stacked ROWS in the body now, so the header
         * is ONE shared column set -- no side-by-side A/B super-header.
         *
         * Header row 1: Asset Name | Date & Time | <metric names>
         * Header row 2: (Event sub-header under each metric)
         *
         * There is no "End" column: each end carries its OWN asset name
         * (A End -> PT-115, B End -> PT-116), so the end is already implied
         * by the name shown on that row.
         */
        '<tr class="pm-vibration-name-header">' +

        '<th rowspan="2" class="pm-vibration-asset-header">' +
        '<span class="pm-vibration-header-icon">' +
        '<i class="fas fa-train"></i>' +
        '</span>' +
        'Asset Name' +
        '</th>' +

        '<th rowspan="2" class="pm-vibration-time-header">' +
        'Date &amp; Time' +
        '</th>';

    for (
        var headerIndex = 0;
        headerIndex < metricsCount;
        headerIndex++
    ) {
        var headerColumn =
            PM_VIBRATION_TABLE_COLUMNS[
            headerIndex
            ];

        html +=
            '<th class="pm-vibration-metric-header"' +
            ' data-register-address="' +
            pmVibrationEscape(
                headerColumn.address
            ) +
            '"' +
            ' title="Reference address: ' +
            pmVibrationEscape(
                headerColumn.address
            ) +
            '">' +
            '<span class="pm-vibration-axis-label">' +
            '<strong>' +
            pmVibrationEscape(
                headerColumn.shortTitle ||
                headerColumn.title
            ) +
            '</strong>' +
            (
                headerColumn.unit
                    ? '<small>' +
                    pmVibrationEscape(
                        headerColumn.unit
                    ) +
                    '</small>'
                    : ''
            ) +
            '</span>' +
            '</th>';
    }

    html +=
        '</tr>' +

        /*
         * Header row 2: Event sub-header, aligned under each metric column.
         */
        '<tr class="pm-vibration-event-header">';

    for (
        var eventIndex = 0;
        eventIndex < metricsCount;
        eventIndex++
    ) {
        var eventColumn =
            PM_VIBRATION_TABLE_COLUMNS[
            eventIndex
            ];

        html +=
            '<th>' +
            (
                PM_VIBRATION_SCALAR_COLUMN_KEYS.indexOf(
                    eventColumn.key
                ) !== -1
                    ? ''
                    : '<span>Event</span>'
            ) +
            '</th>';
    }

    html +=
        '</tr>' +
        '</thead>' +
        '<tbody>';

    if (assetIds.length === 0) {
        html +=
            '<tr>' +
            '<td colspan="' +
            totalColumns +
            '" class="pm-vibration-empty-cell">' +

            '<div class="pm-vibration-empty-state">' +
            '<i class="fas fa-wave-square"></i>' +
            '<strong>Waiting for vibration measurements</strong>' +
            '<span>Values will appear automatically when received from WebSocket.</span>' +
            '</div>' +

            '</td>' +
            '</tr>';
    } else {
        for (
            var assetIndex = 0;
            assetIndex < assetIds.length;
            assetIndex++
        ) {
            html +=
                pmVibrationAssetRowHtml(
                    assetIds[assetIndex]
                );
        }
    }

    html +=
        '</tbody>' +
        '</table>' +
        '</div>' +
        '</div>' +
        '</section>';

    return html;
}

var _pmVibRenderTimer = null;
function renderPointMachineVibrationTable() {
    // Coalesce bursty WebSocket-driven calls into at most one repaint per
    // ~250ms. The whole table is rebuilt on each render, so calling it on every
    // live frame thrashed layout and made the page lag. The trailing timer runs
    // _pmVibrationRenderNow against the latest data, so nothing is missed.
    if (_pmVibRenderTimer) { return; }
    _pmVibRenderTimer = setTimeout(function _pmVibRenderTick() {
        _pmVibRenderTimer = null;

        // Defer, never drop: a full replaceWith of the section behind an open
        // chart relayouts the whole document. Retried until the overlay closes.
        if (_pmVibrationRepaintBlocked()) {
            _pmVibRenderTimer = setTimeout(_pmVibRenderTick, 500);
            return;
        }

        _pmVibrationRenderNow();
    }, 250);
}
function _pmVibrationRenderNow() {
    var assetTypeId =
        parseInt(
            wsCurrentAssetTypeId ||
            $('#drpAssetType').val() ||
            0,
            10
        );

    if (assetTypeId !== 3) return;

    var viewType =
        $('#drpView').val();

    /*
     * FRS Advance drives Point Machine from the Cards / Table pair (and still
     * accepts the older 'PointMachine' value), so all three mount the section.
     * Graph, RDPMS and IPS must not.
     */
    if (
        viewType !== 'Table' &&
        viewType !== 'Cards' &&
        viewType !== 'PointMachine'
    ) {
        return;
    }

    /*
     * Only surface the vibration section (and its toggle button) when at
     * least one point machine actually has live vibration data. When nothing
     * has data, remove any stale section so neither table nor toggle shows.
     */
    if (!pmVibrationHasAnyData()) {
        $('#pmVibrationTableSection').remove();
        return;
    }

    var tableHtml =
        buildPointMachineVibrationTableHtml();

    var $existingSection =
        $('#pmVibrationTableSection');

    if ($existingSection.length) {
        $existingSection.replaceWith(
            tableHtml
        );

        applyPmVibrationEndVisibility();

        return;
    }

    /*
     * Both Point Machine Table and PointMachine card views
     * render inside #divTelemetryLive.
     */
    var $telemetryContainer =
        $('#divTelemetryLive');

    if (!$telemetryContainer.length) {
        return;
    }

    $telemetryContainer.append(
        tableHtml
    );

    applyPmVibrationEndVisibility();
}

/*
 * Vibration rows repaint from a coalesced queue instead of once per WebSocket
 * message. A sensor pushes ~10 attributes per end, so the per-message call
 * rebuilt the same rows a dozen times per batch, each rebuild costing an HTML
 * re-parse plus a forced reflow. Ids are queued here and flushed once per
 * _PM_VIB_FLUSH_MS against the latest data, so no reading is lost.
 */
var _pmVibPendingRows = {};
var _pmVibRowsTimer = null;
var _PM_VIB_FLUSH_MS = 250;

/*
 * True while a graph / event-log / vibration modal is open. Repainting the
 * table underneath an open chart forces a full-document reflow on every frame,
 * which is what makes clicking a graph link feel frozen. The first build is
 * never blocked, so the section still appears normally; only live repaints are
 * deferred, and they resume on their own once the overlay closes.
 */
function _pmVibrationRepaintBlocked() {
    if (!document.getElementById('pmVibrationTableSection')) return false;
    if (document.hidden) return true;
    if (document.getElementById('rdpmsGraphOverlay')) return true;
    if (document.getElementById('vibModalOverlay')) return true;
    if (document.querySelector('.rdpms-graph-overlay')) return true;
    var eventLogModal = document.getElementById('modalalertlogs');
    if (eventLogModal && $(eventLogModal).hasClass('show')) return true;
    return false;
}

function _pmVibrationFlushRows() {
    _pmVibRowsTimer = null;

    if (_pmVibrationRepaintBlocked()) {
        _pmVibRowsTimer = setTimeout(_pmVibrationFlushRows, 500);
        return;
    }

    var queuedIds = Object.keys(_pmVibPendingRows);
    _pmVibPendingRows = {};

    if (!queuedIds.length) return;

    _pmVibrationUpdateRowsNow(queuedIds);
}

function updatePointMachineVibrationRows(assetIds) {
    assetIds = assetIds || [];

    for (var queueIndex = 0; queueIndex < assetIds.length; queueIndex++) {
        _pmVibPendingRows[String(assetIds[queueIndex])] = true;
    }

    if (_pmVibRowsTimer) return;

    _pmVibRowsTimer = setTimeout(_pmVibrationFlushRows, _PM_VIB_FLUSH_MS);
}

function _pmVibrationUpdateRowsNow(
    assetIds
) {
    var assetTypeId =
        parseInt(
            wsCurrentAssetTypeId ||
            $('#drpAssetType').val() ||
            0,
            10
        );

    if (assetTypeId !== 3) return;

    var viewType =
        $('#drpView').val();

    /*
     * FRS Advance drives Point Machine from the Cards / Table pair (and still
     * accepts the older 'PointMachine' value), so all three mount the section.
     * Graph, RDPMS and IPS must not.
     */
    if (
        viewType !== 'Table' &&
        viewType !== 'Cards' &&
        viewType !== 'PointMachine'
    ) {
        return;
    }

    if (
        !$('#pmVibrationTableSection').length
    ) {
        renderPointMachineVibrationTable();
        return;
    }

    assetIds = assetIds || [];

    for (
        var assetIndex = 0;
        assetIndex < assetIds.length;
        assetIndex++
    ) {
        var assetId =
            String(assetIds[assetIndex]);

        /*
         * A point machine with no vibration sensor on either end has no row
         * by design. Skipping it matters here: without this, its WebSocket
         * messages would miss the row lookup below and trigger a full section
         * rebuild on every single message.
         */
        if (!pmVibrationAssetHasEnabledEnd(assetId)) {
            continue;
        }

        /*
 * A two-end machine has TWO rows sharing one data-vibration-asset-row
 * id (A End and B End), and pmVibrationAssetRowHtml() returns BOTH as a
 * single string. jQuery replaces EVERY matched element with its own
 * copy of that string, so 2 rows became 4, then 8, then 16 on each
 * update — the same asset repeating down the table.
 *
 * Replace the FIRST matched row with the fresh pair, then drop the
 * leftovers. Single-end machines are unaffected: slice(1) is empty.
 * This also self-heals rows already duplicated on screen.
 */
        var $existingRows = $(
            '#pmVibrationTableSection [data-vibration-asset-row="' +
            assetId +
            '"]'
        );

        /*
         * First vibration/live message for an asset:
         * rebuild the section so the selected asset is inserted
         * in the correct sorted position.
         */
        if (!$existingRows.length) {
            renderPointMachineVibrationTable();
            return;
        }

        var $staleRows = $existingRows.slice(1);

        $existingRows.first().replaceWith(
            pmVibrationAssetRowHtml(
                assetId
            )
        );

        $staleRows.remove();
    }

    applyPmVibrationEndVisibility();
}




// ── Export rows (Excel / CSV / PDF) ──────────────────────────────────
function getPmVibrationMetricHeaders() {
    return PM_VIBRATION_TABLE_COLUMNS.map(function (column) {
        return column.shortTitle ||
            column.title ||
            column.key;
    });
}

/*
 * Total vibration report columns — STACKED layout, same as the screen:
 *
 * Asset Name | Date & Time | <one shared set of parameters>
 *
 * A End and B End are separate ROWS (each carrying its own asset name,
 * A End -> PT-115, B End -> PT-116), so there is no side-by-side A/B
 * column group and no "End" column. Mirrors buildPointMachineVibrationTableHtml.
 */
function getPmVibrationExportColumnCount() {
    return 1 +
        1 +
        PM_VIBRATION_TABLE_COLUMNS.length;
}

/*
 * Report header row 1 — the table's first header row.
 */
function getPmVibrationExportHeaderRow() {
    return ['Asset Name', 'Date & Time']
        .concat(
            getPmVibrationMetricHeaders()
        );
}

/*
 * Report header row 2 — the "Event" sub-header the table prints under every
 * metric. Temperature and Motor Run Flag are scalar readings with no Max/Avg,
 * so their sub-header stays blank exactly as on screen.
 * Blank leading cells are intentional for CSV, which cannot merge cells.
 */
function getPmVibrationExportEventHeaderRow() {
    var row = ['', ''];

    for (
        var eventIndex = 0;
        eventIndex < PM_VIBRATION_TABLE_COLUMNS.length;
        eventIndex++
    ) {
        row.push(
            PM_VIBRATION_SCALAR_COLUMN_KEYS.indexOf(
                PM_VIBRATION_TABLE_COLUMNS[eventIndex].key
            ) !== -1
                ? ''
                : 'Event'
        );
    }

    return row;
}

function getPmVibrationEventValues(
    assetId,
    endName,
    column
) {
    var result = {
        max: null,
        avg: null
    };

    var assetData =
        wsVibrationData[String(assetId)] ||
        wsVibrationData[assetId];

    if (
        !assetData ||
        !assetData.attrs
    ) {
        return result;
    }

    //var fallbackIds =
    //    column &&
    //        Array.isArray(column.fallbackIds)
    //        ? column.fallbackIds.map(function (id) {
    //            return parseInt(id, 10);
    //        })
    //        : [];

    var isBEnd =
        String(endName || '').toUpperCase() === 'B';

    var rawFallbackIds =
        column
            ? (
                isBEnd
                    ? column.bEndFallbackIds
                    : column.fallbackIds
            )
            : null;

    var fallbackIds =
        Array.isArray(rawFallbackIds)
            ? rawFallbackIds.map(function (id) {
                return parseInt(id, 10);
            })
            : [];

    var attrs = assetData.attrs;

    for (var attrName in attrs) {
        if (!attrs.hasOwnProperty(attrName)) {
            continue;
        }

        var attr = attrs[attrName];

        if (!attr) continue;

        if (
            String(attr.end || '').toUpperCase() !==
            String(endName || '').toUpperCase()
        ) {
            continue;
        }

        var metricMatches =
            attr.metricKey &&
            column &&
            String(attr.metricKey) ===
            String(column.key);

        var baseId =
            parseInt(
                attr.baseAttrId,
                10
            );

        var idMatches =
            !isNaN(baseId) &&
            fallbackIds.indexOf(baseId) !== -1;

        if (
            !metricMatches &&
            !idMatches
        ) {
            continue;
        }

        var eventKind =
            String(
                attr.eventKind || ''
            ).toLowerCase();

        if (eventKind === 'max') {
            result.max = attr.value;
        } else if (eventKind === 'avg') {
            result.avg = attr.value;
        }
    }

    return result;
}

function pmVibrationExportText(
    column,
    valueData
) {
    var formatted =
        pmVibrationFormatMetricValue(
            column,
            valueData
        );

    return formatted &&
        formatted.text
        ? formatted.text
        : '—';
}

function pmVibrationExportEventText(
    column,
    value
) {
    if (
        value === null ||
        value === undefined ||
        String(value).trim() === ''
    ) {
        return '—';
    }

    return pmVibrationExportText(
        column,
        {
            value: value,
            attributeName: ''
        }
    );
}

/*
 * UI cell format:
 *
 * Live: <current value>
 * Event: <max> / <average>
 *
 * The current value and Event Max/Avg remain in the same parameter cell,
 * matching the visible vibration table.
 */
function pmVibrationBuildReportCell(
    column,
    liveValue,
    eventValues
) {
    var liveText =
        pmVibrationExportText(
            column,
            liveValue
        );

    /*
     * Temperature and Motor Run Flag have no event Max / Avg, so the
     * report cell carries the value alone, matching the screen.
     */
    if (
        PM_VIBRATION_SCALAR_COLUMN_KEYS.indexOf(
            column.key
        ) !== -1
    ) {
        return liveText;
    }

    var maxText =
        pmVibrationExportEventText(
            column,
            eventValues
                ? eventValues.max
                : null
        );

    var avgText =
        pmVibrationExportEventText(
            column,
            eventValues
                ? eventValues.avg
                : null
        );

    return (
        'Live: ' + liveText +
        '\nEvent: ' +
        maxText +
        ' / ' +
        avgText
    );
}

/*
 * One export row represents ONE END of one Point Machine, exactly like the
 * on-screen vibration table: A End and B End are stacked rows, each carrying
 * its own asset name (A End -> PT-115, B End -> PT-116) and its own
 * Date & Time, sharing a single set of parameter columns.
 *
 * Row selection mirrors buildPointMachineVibrationTableHtml /
 * pmVibrationAssetRowHtml so the report contains the same rows, in the same
 * order, as the table on screen:
 *   - only RENDERABLE assets (selected AND carrying a sensor on some end)
 *   - only ends switched on by VibrationA / VibrationB
 */
function buildPmVibrationExportRows(loc) {
    var rows = [];

    /*
     * The section is only drawn on screen when at least one point machine
     * has a live vibration reading (_pmVibrationRenderNow). Apply the same
     * gate here, so a report can never carry a table of dashes that the user
     * never saw — the callers then print their "no vibration data" notice.
     */
    if (!pmVibrationHasAnyData()) {
        return rows;
    }

    var assetIds =
        pmVibrationGetRenderableAssetIds();

    for (
        var assetIndex = 0;
        assetIndex < assetIds.length;
        assetIndex++
    ) {
        var assetId =
            String(
                assetIds[assetIndex]
            );

        // Ends that actually carry a sensor — the same test the table row
        // builder uses, so an asset with one end produces one report row.
        var ends = [];

        if (pmVibrationEndEnabled(assetId, 'A')) ends.push('A');
        if (pmVibrationEndEnabled(assetId, 'B')) ends.push('B');

        if (!ends.length) continue;

        for (
            var endIndex = 0;
            endIndex < ends.length;
            endIndex++
        ) {
            var endName = ends[endIndex];

            var model =
                pmVibrationBuildEndModel(
                    assetId,
                    endName
                );

            // Per-end name, resolved exactly as the table cell does:
            // AliasDirectionA / AliasDirectionB first, then a combined
            // "115/116" split, then the plain name.
            var assetName =
                pmVibrationEndDisplayName(
                    assetId,
                    endName,
                    model.assetName,
                    ends.length
                );

            var cells = [];

            for (
                var columnIndex = 0;
                columnIndex <
                PM_VIBRATION_TABLE_COLUMNS.length;
                columnIndex++
            ) {
                var column =
                    PM_VIBRATION_TABLE_COLUMNS[
                    columnIndex
                    ];

                var eventValues =
                    getPmVibrationEventValues(
                        assetId,
                        endName,
                        column
                    );

                cells.push(
                    pmVibrationBuildReportCell(
                        column,
                        model.values[column.key],
                        eventValues
                    )
                );
            }

            rows.push({
                zone:
                    loc && loc.zone
                        ? loc.zone
                        : '',

                division:
                    loc && loc.division
                        ? loc.division
                        : '',

                station:
                    loc && loc.station
                        ? loc.station
                        : '',

                assetId: assetId,
                end: endName,
                assetName: assetName,

                timestamp:
                    pmVibrationFormatDateTime(
                        model.timestamp
                    ),

                cells: cells
            });
        }
    }

    return rows;
}

function pmVibrationExportRowArray(row) {
    return [
        row.assetName,
        row.timestamp
    ]
        .concat(
            row.cells
        );
}
