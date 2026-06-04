// ================================================================
// DYNAMIC SIP — BUILT FROM wsLiveData (WebSocket)
// ================================================================
// INTEGRATION INSTRUCTIONS:
//
// STEP 1 — In TelemetryLive.cshtml, FIND the <section class="sip-card"> block
//           (from "<!-- SIP — Station Interlocking Plan" to "</section>")
//           and REPLACE the entire <section> with this single div:
//
//   <section class="sip-card" aria-label="Station yard schematic">
//     <div class="sip-card-head">
//       <div class="sip-title-row">
//         <div class="sip-title"><i class="fa-solid fa-diagram-project"></i>
//           Station Schematic — <span id="sipSiteName">Select a site</span>
//         </div>
//         <div class="sip-sub">Real-time from WebSocket · click asset to jump to card</div>
//       </div>
//       <div class="sip-controls" id="sipControls" style="display:none;">
//         <button class="sip-clear" id="sipRefreshBtn" onclick="sipBuildFromData()">
//           <i class="fa-solid fa-rotate"></i> Refresh
//         </button>
//       </div>
//     </div>
//     <div class="sip-canvas" id="sipCanvas">
//       <div id="sipWaiting" style="display:flex;align-items:center;justify-content:center;
//            height:160px;color:rgba(255,255,255,0.35);font-size:13px;gap:10px;">
//         <i class="fas fa-diagram-project" style="font-size:20px;opacity:0.4;"></i>
//         Run a search to load the schematic
//       </div>
//     </div>
//     <div class="sip-legend" id="sipLegend" style="display:none;">
//       <span class="legend-item"><span class="legend-swatch"></span>Track — clear</span>
//       <span class="legend-item"><span class="legend-swatch signal"></span>Signal — green</span>
//       <span class="legend-item"><span class="legend-swatch signal y"></span>Yellow</span>
//       <span class="legend-item"><span class="legend-swatch signal r"></span>Red</span>
//       <span class="legend-item"><span class="legend-swatch shunt"></span>Shunt</span>
//       <span class="legend-item"><span class="legend-swatch pm"></span>Point Machine</span>
//     </div>
//   </section>
//
// STEP 2 — Paste this entire JS file content at the END of your existing <script> block
//           in TelemetryLive.cshtml, just before </script>
//
// STEP 3 — Done. The SIP rebuilds automatically whenever wsLiveData changes.
// ================================================================

// ── LAYOUT CONFIG ──────────────────────────────────────────────
var SIP_CFG = {
    SVG_W: 2000,   // scrollable SVG width
    SVG_H: 280,    // SVG height — grows dynamically
    ROW_H: 54,     // vertical space per asset row
    COL_W: 110,    // horizontal space per asset column
    PAD_TOP: 40,     // top padding before first row
    PAD_LEFT: 80,     // left padding (lane labels)
    BLOCK_W: 90,     // track block width
    BLOCK_H: 26,     // track block height
    SIG_RX: 22,     // signal ellipse x-radius
    SIG_RY: 13,     // signal ellipse y-radius
    PM_SIZE: 36,     // point machine hexagon size
    REFRESH_MS: 800     // live-update interval (ms)
};

// ── Asset type IDs (match your backend) ───────────────────────
var SIP_TYPE = {
    TRACK: 1,
    SIGNAL: 2,
    POINT: 3,
    IPS: 4   // IPS shown as small square badge
};

// ── State → colors (aurora dark theme) ────────────────────────
var SIP_COLORS = {
    track: {
        ok: { fill: 'rgba(52,211,153,0.14)', stroke: 'rgba(52,211,153,0.55)', text: '#34d399' },
        warn: { fill: 'rgba(251,191,36,0.14)', stroke: 'rgba(251,191,36,0.55)', text: '#fbbf24' },
        bad: { fill: 'rgba(251,113,133,0.18)', stroke: 'rgba(251,113,133,0.60)', text: '#fb7185' },
        unknown: { fill: 'rgba(255,255,255,0.04)', stroke: 'rgba(255,255,255,0.20)', text: 'rgba(255,255,255,0.4)' }
    },
    signal: {
        green: { fill: 'rgba(52,211,153,0.22)', stroke: '#34d399', glow: 'rgba(52,211,153,0.5)' },
        yellow: { fill: 'rgba(251,191,36,0.22)', stroke: '#fbbf24', glow: 'rgba(251,191,36,0.5)' },
        doubleYellow: { fill: 'rgba(251,191,36,0.22)', stroke: '#fbbf24', glow: 'rgba(251,191,36,0.5)' },
        red: { fill: 'rgba(251,113,133,0.25)', stroke: '#fb7185', glow: 'rgba(251,113,133,0.5)' },
        dark: { fill: 'rgba(255,255,255,0.04)', stroke: 'rgba(255,255,255,0.18)', glow: 'none' }
    },
    pm: {
        normal: { fill: 'rgba(34,211,238,0.20)', stroke: 'rgba(34,211,238,0.65)', text: '#22d3ee' },
        reverse: { fill: 'rgba(251,191,36,0.20)', stroke: 'rgba(251,191,36,0.65)', text: '#fbbf24' },
        unknown: { fill: 'rgba(255,255,255,0.06)', stroke: 'rgba(255,255,255,0.22)', text: 'rgba(255,255,255,0.5)' }
    },
    shunt: {
        on: { fill: 'rgba(251,191,36,0.22)', stroke: '#fbbf24' },
        off: { fill: 'rgba(52,211,153,0.18)', stroke: '#34d399' },
        dark: { fill: 'rgba(167,139,250,0.18)', stroke: 'rgba(167,139,250,0.55)' }
    }
};

// ── Internal state ─────────────────────────────────────────────
var sipAssetMap = {};   // assetId → { id, name, type, svgId }
var sipLiveTimer = null;
var sipLastData = {};   // assetId → last rendered state string (for diff)
var sipSvgEl = null; // reference to the <svg> element
var sipTooltipEl = null;

// ── MAIN ENTRY: build the SIP from current wsLiveData ──────────
function sipBuildFromData() {
    var canvas = document.getElementById('sipCanvas');
    if (!canvas) return;

    var assetIds = Object.keys(wsLiveData);
    if (assetIds.length === 0) {
        canvas.innerHTML = '<div id="sipWaiting" style="display:flex;align-items:center;'
            + 'justify-content:center;height:160px;color:rgba(255,255,255,0.35);'
            + 'font-size:13px;gap:10px;">'
            + '<i class="fas fa-diagram-project" style="font-size:20px;opacity:0.4;"></i>'
            + 'Waiting for WebSocket data…</div>';
        return;
    }

    // Update site name in header
    var siteName = $('#drpSite option:selected').text() || 'Unknown Site';
    var assetTypeName = $('#drpAssetType option:selected').text() || '';
    var headerEl = document.getElementById('sipSiteName');
    if (headerEl) headerEl.textContent = siteName + (assetTypeName ? ' · ' + assetTypeName : '');

    // Show controls + legend
    var ctrlEl = document.getElementById('sipControls');
    if (ctrlEl) ctrlEl.style.display = '';
    var legEl = document.getElementById('sipLegend');
    if (legEl) legEl.style.display = '';

    // Group assets by type
    var groups = { track: [], signal: [], shunt: [], pm: [], ips: [], other: [] };
    sipAssetMap = {};

    assetIds.forEach(function (id) {
        var a = wsLiveData[id];
        if (!a) return;
        var typeId = parseInt(a.AssetTypeId || wsCurrentAssetTypeId || 0);
        var name = (a.AssetName || '').trim();
        var nameLower = name.toLowerCase();

        var entry = { id: id, name: name, typeId: typeId, assetTypeName: a.AssetTypeName || '' };
        sipAssetMap[id] = entry;

        if (typeId === SIP_TYPE.TRACK) {
            groups.track.push(entry);
        } else if (typeId === SIP_TYPE.SIGNAL) {
            if (nameLower.indexOf('sh') > -1) groups.shunt.push(entry);
            else groups.signal.push(entry);
        } else if (typeId === SIP_TYPE.POINT) {
            groups.pm.push(entry);
        } else if (typeId === SIP_TYPE.IPS) {
            groups.ips.push(entry);
        } else {
            // Fallback: detect by name pattern
            if (nameLower.indexOf('sh') > -1 && (nameLower.match(/^\d/) || nameLower.indexOf('sh') === 0)) {
                groups.shunt.push(entry);
            } else if (nameLower.match(/^[a-z]\d/) || nameLower.startsWith('s')) {
                groups.signal.push(entry);
            } else if (nameLower.indexOf('pt') > -1 || nameLower.indexOf('pm') > -1) {
                groups.pm.push(entry);
            } else {
                groups.track.push(entry);
            }
        }
    });

    // Sort each group alphabetically by name (natural sort)
    var natSort = function (a, b) {
        return a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
    };
    Object.keys(groups).forEach(function (k) { groups[k].sort(natSort); });

    // Build SVG
    var svgHtml = sipRenderSVG(groups);
    canvas.innerHTML = svgHtml;

    // Cache references
    sipSvgEl = canvas.querySelector('svg');
    sipTooltipEl = canvas.querySelector('#sipDynTooltip');

    // Attach hover/click events
    sipAttachEvents();

    // Start live update polling
    sipStartLiveUpdates();

    console.log('[SIP] Built from wsLiveData:', {
        track: groups.track.length,
        signal: groups.signal.length,
        shunt: groups.shunt.length,
        pm: groups.pm.length
    });
}

// ── SVG RENDERER ───────────────────────────────────────────────
function sipRenderSVG(groups) {
    // Calculate layout dimensions
    var rows = [];
    var svgH = SIP_CFG.PAD_TOP;

    // ROW ORDER: Tracks first (grouped as lane rows), then signals row, then PMs row, then shunts row
    // If only one type, just show that type cleanly

    // Determine how many rows we need
    var maxTrackCols = Math.max(groups.track.length, 1);
    var trackRows = Math.ceil(groups.track.length / 20); // max 20 tracks per row
    var tracksPerRow = Math.ceil(groups.track.length / Math.max(trackRows, 1));

    // Build row definitions
    if (groups.track.length > 0) {
        for (var tr = 0; tr < trackRows; tr++) {
            rows.push({ type: 'track', items: groups.track.slice(tr * tracksPerRow, (tr + 1) * tracksPerRow), label: 'TRACK' });
        }
    }
    if (groups.signal.length > 0) {
        var sigRows = Math.ceil(groups.signal.length / 20);
        var sigPer = Math.ceil(groups.signal.length / Math.max(sigRows, 1));
        for (var sr = 0; sr < sigRows; sr++) {
            rows.push({ type: 'signal', items: groups.signal.slice(sr * sigPer, (sr + 1) * sigPer), label: 'SIGNAL' });
        }
    }
    if (groups.shunt.length > 0) {
        var shRows = Math.ceil(groups.shunt.length / 20);
        var shPer = Math.ceil(groups.shunt.length / Math.max(shRows, 1));
        for (var shr = 0; shr < shRows; shr++) {
            rows.push({ type: 'shunt', items: groups.shunt.slice(shr * shPer, (shr + 1) * shPer), label: 'SHUNT' });
        }
    }
    if (groups.pm.length > 0) {
        var pmRows = Math.ceil(groups.pm.length / 16);
        var pmPer = Math.ceil(groups.pm.length / Math.max(pmRows, 1));
        for (var pr = 0; pr < pmRows; pr++) {
            rows.push({ type: 'pm', items: groups.pm.slice(pr * pmPer, (pr + 1) * pmPer), label: 'PT MACHINE' });
        }
    }
    if (groups.ips.length > 0) {
        rows.push({ type: 'ips', items: groups.ips, label: 'IPS' });
    }
    if (groups.other.length > 0) {
        rows.push({ type: 'other', items: groups.other, label: 'OTHER' });
    }

    // Calculate total SVG height
    var totalH = SIP_CFG.PAD_TOP + rows.length * (SIP_CFG.ROW_H + 20) + 40;
    var maxCols = 0;
    rows.forEach(function (r) { if (r.items.length > maxCols) maxCols = r.items.length; });
    var totalW = Math.max(SIP_CFG.PAD_LEFT + maxCols * SIP_CFG.COL_W + 40, 800);

    // Begin SVG
    var s = '<svg id="sipDynSvg" viewBox="0 0 ' + totalW + ' ' + totalH + '" '
        + 'xmlns="http://www.w3.org/2000/svg" style="width:' + totalW + 'px;height:' + totalH + 'px;display:block;">';

    // Background grid
    s += '<defs>'
        + '<pattern id="sipG" width="40" height="40" patternUnits="userSpaceOnUse">'
        + '<path d="M40 0L0 0 0 40" fill="none" stroke="rgba(255,255,255,0.022)" stroke-width="0.5"/>'
        + '</pattern>'
        + '</defs>';
    s += '<rect width="' + totalW + '" height="' + totalH + '" fill="url(#sipG)"/>';

    // Render each row
    var y = SIP_CFG.PAD_TOP;
    rows.forEach(function (row, rowIdx) {
        s += sipRenderRow(row, y, totalW);
        y += SIP_CFG.ROW_H + 20;
    });

    // Tooltip element (starts hidden)
    s += '<g id="sipDynTooltip" style="pointer-events:none;display:none;">'
        + '<rect id="sipTTBg" x="0" y="0" width="160" height="48" rx="6" '
        + 'fill="rgba(5,9,24,0.97)" stroke="rgba(34,211,238,0.35)" stroke-width="1"/>'
        + '<text id="sipTTLine1" x="8" y="18" font-family="JetBrains Mono,monospace" '
        + 'font-size="11" font-weight="700" fill="rgba(255,255,255,0.96)"></text>'
        + '<text id="sipTTLine2" x="8" y="34" font-family="JetBrains Mono,monospace" '
        + 'font-size="10" fill="rgba(255,255,255,0.55)"></text>'
        + '<text id="sipTTLine3" x="8" y="46" font-family="JetBrains Mono,monospace" '
        + 'font-size="9" fill="rgba(34,211,238,0.7)"></text>'
        + '</g>';

    s += '</svg>';
    return s;
}

// ── Render one row of assets ───────────────────────────────────
function sipRenderRow(row, y, totalW) {
    var s = '';
    var rowMidY = y + SIP_CFG.ROW_H / 2;

    // Row background stripe
    s += '<rect x="0" y="' + y + '" width="' + totalW + '" height="' + SIP_CFG.ROW_H
        + '" fill="rgba(255,255,255,0.012)"/>';

    // Lane label (left margin)
    s += '<text x="' + (SIP_CFG.PAD_LEFT - 8) + '" y="' + rowMidY
        + '" font-family="JetBrains Mono,monospace" font-size="9" font-weight="700" '
        + 'fill="rgba(255,255,255,0.28)" letter-spacing="0.08em" text-anchor="end" '
        + 'dominant-baseline="central">' + row.label + '</text>';

    // Horizontal rail line connecting assets
    if (row.type === 'track' && row.items.length > 1) {
        var railX1 = SIP_CFG.PAD_LEFT;
        var railX2 = SIP_CFG.PAD_LEFT + (row.items.length - 1) * SIP_CFG.COL_W + SIP_CFG.BLOCK_W;
        s += '<line x1="' + railX1 + '" y1="' + rowMidY + '" x2="' + railX2 + '" y2="' + rowMidY
            + '" stroke="rgba(255,255,255,0.12)" stroke-width="1.5" stroke-dasharray="4 3"/>';
    }

    // Render each asset in the row
    row.items.forEach(function (entry, colIdx) {
        var x = SIP_CFG.PAD_LEFT + colIdx * SIP_CFG.COL_W;
        switch (row.type) {
            case 'track': s += sipRenderTrack(entry, x, rowMidY); break;
            case 'signal': s += sipRenderSignal(entry, x, rowMidY); break;
            case 'shunt': s += sipRenderShunt(entry, x, rowMidY); break;
            case 'pm': s += sipRenderPM(entry, x, rowMidY); break;
            case 'ips': s += sipRenderIPS(entry, x, rowMidY); break;
            default: s += sipRenderTrack(entry, x, rowMidY); break;
        }
    });

    return s;
}

// ── Render TRACK CIRCUIT block ─────────────────────────────────
function sipRenderTrack(entry, x, cy) {
    var state = sipGetTrackState(entry.id);
    var col = SIP_COLORS.track[state] || SIP_COLORS.track.unknown;
    var blockY = cy - SIP_CFG.BLOCK_H / 2;
    var id = 'sip_' + entry.id;

    // Shorten label if too long
    var label = sipShortName(entry.name, 10);

    var s = '<g id="' + id + '" data-assetid="' + entry.id + '" data-type="track" '
        + 'style="cursor:pointer;" class="sip-dyn-asset">';
    s += '<rect x="' + x + '" y="' + blockY + '" width="' + SIP_CFG.BLOCK_W + '" height="' + SIP_CFG.BLOCK_H + '" '
        + 'rx="13" fill="' + col.fill + '" stroke="' + col.stroke + '" stroke-width="1.2"/>';
    s += '<text x="' + (x + SIP_CFG.BLOCK_W / 2) + '" y="' + cy + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="9.5" font-weight="600" '
        + 'fill="' + col.text + '" text-anchor="middle" dominant-baseline="central">'
        + sipEscXml(label) + '</text>';
    s += '</g>';
    return s;
}

// ── Render SIGNAL ellipse ──────────────────────────────────────
function sipRenderSignal(entry, x, cy) {
    var aspect = sipGetSignalAspect(entry.id);
    var col = SIP_COLORS.signal[aspect] || SIP_COLORS.signal.dark;
    var cx = x + SIP_CFG.SIG_RX;
    var id = 'sip_' + entry.id;
    var label = sipShortName(entry.name, 6);
    var glow = col.glow !== 'none'
        ? 'filter:drop-shadow(0 0 5px ' + col.glow + ');' : '';

    var s = '<g id="' + id + '" data-assetid="' + entry.id + '" data-type="signal" '
        + 'style="cursor:pointer;' + glow + '" class="sip-dyn-asset">';
    s += '<ellipse cx="' + cx + '" cy="' + cy + '" rx="' + SIP_CFG.SIG_RX + '" ry="' + SIP_CFG.SIG_RY + '" '
        + 'fill="' + col.fill + '" stroke="' + col.stroke + '" stroke-width="1.4"/>';
    s += '<text x="' + cx + '" y="' + cy + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="9" font-weight="700" '
        + 'fill="rgba(255,255,255,0.90)" text-anchor="middle" dominant-baseline="central">'
        + sipEscXml(label) + '</text>';

    // Double-yellow indicator (second ellipse top)
    if (aspect === 'doubleYellow') {
        s += '<ellipse cx="' + cx + '" cy="' + (cy - SIP_CFG.SIG_RY - 5) + '" '
            + 'rx="' + (SIP_CFG.SIG_RX * 0.7) + '" ry="' + (SIP_CFG.SIG_RY * 0.7) + '" '
            + 'fill="rgba(251,191,36,0.35)" stroke="#fbbf24" stroke-width="1"/>';
    }

    s += '</g>';
    return s;
}

// ── Render SHUNT SIGNAL ellipse ───────────────────────────────
function sipRenderShunt(entry, x, cy) {
    var aspect = sipGetShuntAspect(entry.id);
    var col = SIP_COLORS.shunt[aspect] || SIP_COLORS.shunt.dark;
    var cx = x + SIP_CFG.SIG_RX;
    var id = 'sip_' + entry.id;
    var label = sipShortName(entry.name, 8);

    var s = '<g id="' + id + '" data-assetid="' + entry.id + '" data-type="shunt" '
        + 'style="cursor:pointer;" class="sip-dyn-asset">';
    s += '<ellipse cx="' + cx + '" cy="' + cy + '" rx="' + SIP_CFG.SIG_RX + '" ry="' + SIP_CFG.SIG_RY + '" '
        + 'fill="' + col.fill + '" stroke="' + col.stroke + '" stroke-width="1.4"/>';
    s += '<text x="' + cx + '" y="' + cy + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="8.5" font-weight="600" '
        + 'fill="rgba(255,255,255,0.85)" text-anchor="middle" dominant-baseline="central">'
        + sipEscXml(label) + '</text>';
    s += '</g>';
    return s;
}

// ── Render POINT MACHINE hexagon ───────────────────────────────
function sipRenderPM(entry, x, cy) {
    var dir = sipGetPmDirection(entry.id);
    var col = SIP_COLORS.pm[dir] || SIP_COLORS.pm.unknown;
    var cx = x + SIP_CFG.PM_SIZE / 2 + 5;
    var hs = SIP_CFG.PM_SIZE / 2;
    var id = 'sip_' + entry.id;
    var label = sipShortName(entry.name.replace(/^PT-/i, ''), 9);

    // Hexagon points
    var pts = [
        (cx - hs * 0.5) + ',' + (cy - hs),
        (cx + hs * 0.5) + ',' + (cy - hs),
        (cx + hs) + ',' + cy,
        (cx + hs * 0.5) + ',' + (cy + hs),
        (cx - hs * 0.5) + ',' + (cy + hs),
        (cx - hs) + ',' + cy
    ].join(' ');

    var glow = dir === 'normal'
        ? 'filter:drop-shadow(0 0 5px rgba(34,211,238,0.5));'
        : 'filter:drop-shadow(0 0 5px rgba(251,191,36,0.5));';

    var s = '<g id="' + id + '" data-assetid="' + entry.id + '" data-type="pm" '
        + 'style="cursor:pointer;' + glow + '" class="sip-dyn-asset">';
    s += '<polygon points="' + pts + '" fill="' + col.fill + '" stroke="' + col.stroke + '" stroke-width="1.4"/>';
    s += '<text x="' + cx + '" y="' + (cy - 4) + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="7.5" font-weight="700" '
        + 'fill="' + col.text + '" text-anchor="middle" dominant-baseline="central">'
        + sipEscXml(label) + '</text>';
    // Direction badge
    s += '<text x="' + cx + '" y="' + (cy + 7) + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="7" font-weight="600" '
        + 'fill="' + (dir === 'normal' ? '#22d3ee' : '#fbbf24') + '" text-anchor="middle" dominant-baseline="central">'
        + (dir === 'normal' ? 'NOR' : dir === 'reverse' ? 'REV' : '---') + '</text>';
    s += '</g>';
    return s;
}

// ── Render IPS badge ───────────────────────────────────────────
function sipRenderIPS(entry, x, cy) {
    var id = 'sip_' + entry.id;
    var label = sipShortName(entry.name, 8);
    var bw = 72, bh = 22;
    var by = cy - bh / 2;

    var s = '<g id="' + id + '" data-assetid="' + entry.id + '" data-type="ips" '
        + 'style="cursor:pointer;" class="sip-dyn-asset">';
    s += '<rect x="' + x + '" y="' + by + '" width="' + bw + '" height="' + bh + '" '
        + 'rx="4" fill="rgba(167,139,250,0.16)" stroke="rgba(167,139,250,0.5)" stroke-width="1"/>';
    s += '<text x="' + (x + bw / 2) + '" y="' + cy + '" '
        + 'font-family="JetBrains Mono,monospace" font-size="9" '
        + 'fill="rgba(167,139,250,0.9)" text-anchor="middle" dominant-baseline="central">'
        + sipEscXml(label) + '</text>';
    s += '</g>';
    return s;
}

// ── STATE HELPERS (reuse existing functions when available) ────

function sipGetTrackState(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset || !asset.attrs) return 'unknown';

    var attrs = asset.attrs;

    // Check DL relay for TPR pickup (clear) / drop (occupied)
    var dlRelays = asset.dlRelays || {};
    for (var rk in dlRelays) {
        if (rk.toUpperCase().indexOf('TPR') > -1) {
            return dlRelays[rk].isPickup ? 'ok' : 'bad';
        }
    }

    // Vr check
    var vrAttr = attrs['Vr'] || attrs['VTC RELAY END(V)'] || attrs['ITC RELAY END(mA)'];
    if (vrAttr && vrAttr.Value !== null && vrAttr.Value !== undefined) {
        var v = parseFloat(vrAttr.Value);
        if (!isNaN(v) && ((v > 0.1 && v < 2.5) || v > 4.2)) return 'warn';
    }

    // TPR voltage attribute
    var tprAttr = attrs['TPR V'] || attrs['VTC 24 DC TPR I/P(V)'];
    if (tprAttr && tprAttr.Value !== null) {
        var tv = parseFloat(tprAttr.Value);
        if (!isNaN(tv) && tv > 0.1 && tv < 20) return 'bad';
    }

    if (Object.keys(attrs).length > 0) return 'ok';
    return 'unknown';
}

function sipGetSignalAspect(assetId) {
    // Delegate to main function if available
    if (typeof window.updateMainSignalLights === 'function' && wsLiveData[assetId]) {
        // Use the existing zeroOffset + relay logic via internal helper
        var asset = wsLiveData[assetId];
        if (!asset || !asset.attrs) return 'dark';

        var zEntry = zeroOffsetCache[assetId];
        var threshold = (zEntry && zEntry.fetched && !isNaN(zEntry.value))
            ? zEntry.value : RDPMS_DEFAULT_THRESHOLD;

        function mAVal(n) {
            var a = asset.attrs[n];
            return (a && a.Value !== null && a.Value !== undefined) ? (parseFloat(a.Value) || 0) : 0;
        }
        function dlPickup(relay) {
            var dl = asset.dlRelays || {};
            for (var k in dl) {
                if (k.toUpperCase().indexOf(relay.toUpperCase()) > -1) return dl[k].isPickup === true;
            }
            return false;
        }

        var hasRECR = dlPickup('RECR'), hasHECR = dlPickup('HECR');
        var hasHHECR = dlPickup('HHECR'), hasDECR = dlPickup('DECR');
        var hasAnyDL = Object.keys(asset.dlRelays || {}).length > 0;

        if (hasAnyDL) {
            if (hasRECR) return 'red';
            if (hasHECR && hasHHECR) return 'doubleYellow';
            if (hasHECR) return 'yellow';
            if (hasDECR) return 'green';
            return 'dark';
        }

        var rgMa = mAVal('RG mA'), dgMa = mAVal('DG mA');
        var hgMa = mAVal('HG mA'), hhgMa = mAVal('HHG mA');

        if (rgMa > threshold) return 'red';
        if (hhgMa > threshold) return 'doubleYellow';
        if (hgMa > threshold) return 'yellow';
        if (dgMa > threshold) return 'green';
    }
    return 'dark';
}

function sipGetShuntAspect(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset || !asset.attrs) return 'dark';
    var zEntry = zeroOffsetCache[assetId];
    var threshold = (zEntry && zEntry.fetched && !isNaN(zEntry.value))
        ? zEntry.value : RDPMS_DEFAULT_THRESHOLD;
    function mAVal(n) {
        var a = asset.attrs[n];
        return (a && a.Value !== null) ? (parseFloat(a.Value) || 0) : 0;
    }
    if (mAVal('On Aspect mA') > threshold) return 'on';
    if (mAVal('Off Aspect mA') > threshold) return 'off';
    return 'dark';
}

function sipGetPmDirection(assetId) {
    var asset = wsLiveData[assetId];
    if (!asset) return 'unknown';
    if (typeof determinePmDirection === 'function') {
        var pm = (typeof getPmStructuredData === 'function') ? getPmStructuredData(assetId) : null;
        var res = determinePmDirection(asset, pm);
        return res.direction === 'REVERSE' ? 'reverse' : 'normal';
    }
    // Fallback: scan attrs for NWKR/RWKR
    var nwkrMax = 0, rwkrMax = 0;
    for (var k in (asset.attrs || {})) {
        var kl = k.toLowerCase(), v = parseFloat((asset.attrs[k] || {}).Value || 0);
        if (kl.indexOf('nwkr') > -1) nwkrMax = Math.max(nwkrMax, v);
        if (kl.indexOf('rwkr') > -1) rwkrMax = Math.max(rwkrMax, v);
    }
    if (nwkrMax > rwkrMax && nwkrMax > 5) return 'normal';
    if (rwkrMax > nwkrMax && rwkrMax > 5) return 'reverse';
    return 'unknown';
}

// ── LIVE UPDATE — only redraw changed elements ─────────────────
function sipLiveUpdate() {
    if (!sipSvgEl) return;

    var assetIds = Object.keys(sipAssetMap);
    assetIds.forEach(function (assetId) {
        var entry = sipAssetMap[assetId];
        if (!entry) return;

        var el = sipSvgEl.getElementById('sip_' + assetId);
        if (!el) return;

        var stateKey = sipGetStateKey(assetId, entry.typeId || entry.type);
        if (stateKey === sipLastData[assetId]) return; // no change
        sipLastData[assetId] = stateKey;

        // Re-render the element in-place
        var rowType = el.getAttribute('data-type');
        var x = parseFloat(el.getBoundingClientRect ? 0 : 0); // we'll parse from existing element

        // Get position from the element's first child rect/ellipse/polygon
        var shape = el.querySelector('rect,ellipse,polygon');
        if (!shape) return;

        var posX = parseFloat(shape.getAttribute('x') || shape.getAttribute('cx') || 0);
        var posY = parseFloat(shape.getAttribute('y') || shape.getAttribute('cy') || 0);
        var cy = rowType === 'track' ? posY + SIP_CFG.BLOCK_H / 2 :
            rowType === 'signal' ? posY :
                rowType === 'shunt' ? posY :
                    rowType === 'pm' ? posY : posY;

        var newHtml = '';
        switch (rowType) {
            case 'track': newHtml = sipRenderTrack(entry, posX, cy); break;
            case 'signal': newHtml = sipRenderSignal(entry, posX, cy); break;
            case 'shunt': newHtml = sipRenderShunt(entry, posX, cy); break;
            case 'pm': newHtml = sipRenderPM(entry, posX, cy); break;
        }

        if (newHtml) {
            var tmp = document.createElementNS('http://www.w3.org/2000/svg', 'g');
            tmp.innerHTML = newHtml;
            var newEl = tmp.firstElementChild;
            if (newEl) {
                // Flash transition
                newEl.style.transition = 'opacity 0.3s';
                newEl.style.opacity = '0.6';
                el.parentNode.replaceChild(newEl, el);
                setTimeout(function () { newEl.style.opacity = '1'; }, 50);
            }
        }
    });

    // Update live counter badge
    sipUpdateBadge();
}

function sipGetStateKey(assetId, typeId) {
    var t = parseInt(typeId);
    if (t === SIP_TYPE.TRACK) return 'track:' + sipGetTrackState(assetId);
    if (t === SIP_TYPE.POINT) return 'pm:' + sipGetPmDirection(assetId);
    var name = (wsLiveData[assetId] && wsLiveData[assetId].AssetName || '').toLowerCase();
    if (name.indexOf('sh') > -1) return 'shunt:' + sipGetShuntAspect(assetId);
    return 'sig:' + sipGetSignalAspect(assetId);
}

// ── EVENTS — tooltip & click ───────────────────────────────────
function sipAttachEvents() {
    if (!sipSvgEl) return;

    var assets = sipSvgEl.querySelectorAll('.sip-dyn-asset');
    assets.forEach(function (g) {
        g.addEventListener('mouseenter', sipOnHover);
        g.addEventListener('mousemove', sipOnMove);
        g.addEventListener('mouseleave', sipOnLeave);
        g.addEventListener('click', sipOnClick);
    });
}

function sipOnHover(e) {
    var g = e.currentTarget;
    var assetId = g.getAttribute('data-assetid');
    var type = g.getAttribute('data-type');
    if (!assetId || !sipTooltipEl) return;

    var asset = wsLiveData[assetId];
    var name = asset ? (asset.AssetName || assetId) : assetId;

    var line2 = '', line3 = '';

    if (type === 'track') {
        var st = sipGetTrackState(assetId);
        line2 = 'State: ' + st.toUpperCase();
        if (asset && asset.attrs) {
            var vrA = asset.attrs['Vr'] || asset.attrs['VTC RELAY END(V)'];
            if (vrA && vrA.Value !== null) line3 = 'Vr ' + parseFloat(vrA.Value).toFixed(2) + 'V';
            var ifA = asset.attrs['If mA'] || asset.attrs['ITC FEED END(mA)'];
            if (ifA && ifA.Value !== null) line3 += (line3 ? '  ·  ' : '') + 'If ' + parseFloat(ifA.Value).toFixed(0) + 'mA';
        }
    } else if (type === 'signal') {
        var asp = sipGetSignalAspect(assetId);
        var aspLabels = { red: 'RED', yellow: 'YELLOW', doubleYellow: 'DBL YELLOW', green: 'GREEN', dark: 'DARK' };
        line2 = 'Aspect: ' + (aspLabels[asp] || asp);
        if (asset && asset.attrs) {
            var mAMap = { red: 'RG mA', yellow: 'HG mA', doubleYellow: 'HHG mA', green: 'DG mA' };
            var mAKey = mAMap[asp];
            if (mAKey && asset.attrs[mAKey] && asset.attrs[mAKey].Value !== null) {
                line3 = mAKey + ' ' + parseFloat(asset.attrs[mAKey].Value).toFixed(1);
            }
        }
    } else if (type === 'shunt') {
        var sha = sipGetShuntAspect(assetId);
        line2 = sha === 'on' ? 'ON Aspect active' : sha === 'off' ? 'OFF Aspect active' : 'DARK';
    } else if (type === 'pm') {
        var dir = sipGetPmDirection(assetId);
        line2 = 'Direction: ' + dir.toUpperCase();
        if (asset && asset.attrs) {
            var nwkrA = asset.attrs['A End - NWKR'] || asset.attrs['B End - NWKR'];
            var rwkrA = asset.attrs['A End - RWKR'] || asset.attrs['B End - RWKR'];
            if (nwkrA && nwkrA.Value !== null) line3 = 'NWKR ' + parseFloat(nwkrA.Value).toFixed(1) + 'V';
            if (rwkrA && rwkrA.Value !== null) line3 += (line3 ? '  RWKR ' : 'RWKR ') + parseFloat(rwkrA.Value).toFixed(1) + 'V';
        }
    }

    if (asset && asset.lastUpdated && typeof fmtTime === 'function') {
        line3 = (line3 ? line3 + '  @' : '@') + fmtTime(asset.lastUpdated);
    }

    var tt = sipTooltipEl;
    var l1 = tt.querySelector('#sipTTLine1');
    var l2 = tt.querySelector('#sipTTLine2');
    var l3 = tt.querySelector('#sipTTLine3');
    var bg = tt.querySelector('#sipTTBg');

    if (l1) l1.textContent = name;
    if (l2) l2.textContent = line2;
    if (l3) l3.textContent = line3;

    // Adjust background width to content
    var maxLen = Math.max(name.length, line2.length, line3.length);
    var ttW = Math.max(130, maxLen * 6.5 + 16);
    var ttH = line3 ? 52 : 40;
    if (bg) { bg.setAttribute('width', ttW); bg.setAttribute('height', ttH); }
    if (l2) l2.setAttribute('y', ttH < 50 ? 28 : 28);
    if (l3) l3.setAttribute('y', ttH < 50 ? 40 : 44);

    tt.style.display = '';
}

function sipOnMove(e) {
    if (!sipTooltipEl || !sipSvgEl) return;
    var svgRect = sipSvgEl.getBoundingClientRect();
    var svgX = (e.clientX - svgRect.left) * (sipSvgEl.viewBox.baseVal.width / svgRect.width);
    var svgY = (e.clientY - svgRect.top) * (sipSvgEl.viewBox.baseVal.height / svgRect.height);
    var tt = sipTooltipEl;
    var bg = tt.querySelector('#sipTTBg');
    var ttW = bg ? parseFloat(bg.getAttribute('width') || 160) : 160;
    var ttH = bg ? parseFloat(bg.getAttribute('height') || 48) : 48;
    // Keep tooltip inside viewbox
    var tx = Math.min(svgX + 12, sipSvgEl.viewBox.baseVal.width - ttW - 10);
    var ty = Math.min(svgY - ttH - 8, sipSvgEl.viewBox.baseVal.height - ttH - 5);
    if (ty < 5) ty = svgY + 14;
    tt.setAttribute('transform', 'translate(' + tx + ',' + ty + ')');
}

function sipOnLeave() {
    if (sipTooltipEl) sipTooltipEl.style.display = 'none';
}

function sipOnClick(e) {
    var g = e.currentTarget || e.target.closest('.sip-dyn-asset');
    if (!g) return;
    var assetId = g.getAttribute('data-assetid');
    var type = g.getAttribute('data-type');
    if (!assetId) return;

    // Highlight clicked element
    var allAssets = sipSvgEl ? sipSvgEl.querySelectorAll('.sip-dyn-asset') : [];
    allAssets.forEach(function (el) { el.style.opacity = '0.45'; });
    g.style.opacity = '1';
    setTimeout(function () {
        allAssets.forEach(function (el) { el.style.opacity = '1'; });
    }, 2000);

    // Scroll to the asset's card/row in the main content area
    var viewType = $('#drpView').val();
    if (viewType === 'Table') {
        var $row = $('#wsLiveTable tbody tr[data-id="' + assetId + '"]');
        if ($row.length) {
            $row[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
            $row.addClass('sip-flash-row');
            setTimeout(function () { $row.removeClass('sip-flash-row'); }, 1800);
        }
    } else if (viewType === 'RDPMS') {
        var $card = $('#rdpmsCard_' + assetId);
        if ($card.length) $card[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
    } else if (viewType === 'PointMachine') {
        var $pmCard = $('#pmCard_' + assetId);
        if ($pmCard.length) $pmCard[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
}

// ── Live badge on SIP header ───────────────────────────────────
function sipUpdateBadge() {
    var $head = $('.sip-card-head .sip-title').first();
    if (!$head.length) return;
    $head.find('.sip-ws-badge').remove();

    var total = Object.keys(sipAssetMap).length;
    if (total === 0) return;

    var liveCount = 0;
    Object.keys(sipAssetMap).forEach(function (id) {
        if (wsLiveData[id] && wsLiveData[id].lastUpdated) liveCount++;
    });

    var badge = '<span class="sip-ws-badge" style="'
        + 'display:inline-flex;align-items:center;gap:4px;'
        + 'padding:2px 7px;border-radius:999px;margin-left:8px;'
        + 'background:rgba(52,211,153,0.14);border:1px solid rgba(52,211,153,0.30);'
        + 'color:#34d399;font-size:10px;font-weight:600;font-family:JetBrains Mono,monospace;">'
        + '<span style="width:5px;height:5px;border-radius:50%;background:#34d399;'
        + 'animation:atWsDot 1.8s ease-in-out infinite;flex-shrink:0;"></span>'
        + liveCount + '/' + total
        + '</span>';
    $head.append(badge);
}

// ── Start / stop live polling ──────────────────────────────────
function sipStartLiveUpdates() {
    if (sipLiveTimer) clearInterval(sipLiveTimer);
    sipLiveTimer = setInterval(sipLiveUpdate, SIP_CFG.REFRESH_MS);
}
function sipStopLiveUpdates() {
    if (sipLiveTimer) { clearInterval(sipLiveTimer); sipLiveTimer = null; }
}

// ── Utility helpers ────────────────────────────────────────────
function sipShortName(name, maxLen) {
    if (!name) return '?';
    if (name.length <= maxLen) return name;
    return name.substring(0, maxLen - 1) + '…';
}
function sipEscXml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// ── Flash CSS for table rows (used in sipOnClick) ──────────────
if (!document.getElementById('sip-dyn-styles')) {
    var style = document.createElement('style');
    style.id = 'sip-dyn-styles';
    style.textContent =
        '.sip-dyn-asset { transition: opacity 0.25s; }'
        + '.sip-flash-row { background:rgba(34,211,238,0.15)!important; '
        + '  outline:2px solid rgba(34,211,238,0.45); outline-offset:-2px; '
        + '  transition:background 1.8s ease; }'
        + '@keyframes sipFadeIn { from{opacity:0} to{opacity:1} }'
        + '.sip-dyn-asset { animation: sipFadeIn 0.35s ease; }';
    document.head.appendChild(style);
}

// ── Hook: rebuild SIP whenever wsLiveData gets new assets ──────
// Called by the existing updateWsStats() on every batch
var _sipOrigUpdateStats = window.updateWsStats;
window.updateWsStats = function () {
    if (typeof _sipOrigUpdateStats === 'function') {
        _sipOrigUpdateStats.apply(this, arguments);
    }
    // Rebuild only when asset count changes (new search or new assets arriving)
    var currentCount = Object.keys(wsLiveData).length;
    if (currentCount !== window._sipLastAssetCount) {
        window._sipLastAssetCount = currentCount;
        if (currentCount > 0) {
            clearTimeout(window._sipRebuildTimer);
            window._sipRebuildTimer = setTimeout(sipBuildFromData, 400);
        }
    }
};

// ── Hook: rebuild when Search button clicked ───────────────────
var _sipOrigSearch = window.fnSearchView;
window.fnSearchView = function () {
    // Reset SIP state for new search
    sipLastData = {};
    sipAssetMap = {};
    sipStopLiveUpdates();
    window._sipLastAssetCount = 0;

    // Show waiting state
    var canvas = document.getElementById('sipCanvas');
    if (canvas) {
        canvas.innerHTML = '<div id="sipWaiting" style="display:flex;align-items:center;'
            + 'justify-content:center;height:160px;color:rgba(255,255,255,0.35);'
            + 'font-size:13px;gap:10px;">'
            + '<i class="fas fa-circle-notch fa-spin" style="font-size:16px;opacity:0.5;"></i>'
            + 'Loading schematic…</div>';
    }

    if (typeof _sipOrigSearch === 'function') {
        _sipOrigSearch.apply(this, arguments);
    }
};

// ── Hook: stop polling on disconnect ──────────────────────────
var _sipOrigDisconnect = window.disconnectWebSocket;
window.disconnectWebSocket = function () {
    sipStopLiveUpdates();
    if (typeof _sipOrigDisconnect === 'function') {
        _sipOrigDisconnect.apply(this, arguments);
    }
};

console.log('[SIP-Dynamic] Loaded — SIP will auto-build from wsLiveData on search');
