/* ==========================================================================
 *  SIP Asset Library — Aurora Edition (rewritten 15-05-2026)
 *  ------------------------------------------------------------------------
 *  Renders every old-Rappid asset type (examples.Track, examples.PointMachine,
 *  examples.Signal*, examples.Shaunt, examples.BusBar, …) using the new
 *  Aurora SVG visuals from sip/*.svg.
 *
 *  >>>  IMPORTANT  <<<
 *  This library works DIRECTLY on the legacy Rappid cell shape:
 *
 *      { type, position:{x,y}, size:{width,height}, angle, z, id, attrs:{…} }
 *
 *  So the editor's getLayout() returns exactly { cells: [...] } and that JSON
 *  drops straight into the advancesipview.SipView1 column — same shape every
 *  existing site already uses, same shape Sview.cshtml already consumes.
 *
 *  Public API
 *  ----------
 *    SIP.GROUPS                  – toolbox group definitions
 *    SIP.PALETTE                 – every paintable asset, in toolbox order
 *    SIP.spec(type)              – lookup descriptor for a Rappid type
 *    SIP.makeCell(type, x, y)    – build a fresh default cell (post-drag)
 *    SIP.renderCell(cell)        – cell  → SVG fragment (for editor canvas)
 *    SIP.renderIcon(type)        – type  → mini SVG (toolbox preview)
 *    SIP.renderYard({viewBox, cells, background?, className?})
 *                                 – build the full <svg>…</svg> for export
 *
 *  Cell `attrs` defaults are populated by makeCell() with EXACTLY the selectors
 *  the Sview.cshtml runtime mutates (path / body / circle1 / path1 / label),
 *  so the first MQTT tick never trips on a missing nested object.
 * ========================================================================== */

(function () {
    'use strict';

    /* ------------------------------------------------------------------------ *
     *  Theme — every colour lives here so a re-skin is a single edit. Mirrors
     *  the CSS variables in sip-editor.css.
     * ------------------------------------------------------------------------ */
    const T = {
        railBase: '#7E7E7E',
        railEdge: '#ffffff',
        sleeper: '#515151',

        sectionClear: '#22D142',
        sectionOcc: '#FF2E2E',
        sectionRoute: '#22d3ee',
        sectionEdge: 'rgba(255,255,255,0.18)',
        sectionText: '#ffffff',

        signalBody: '#606060',
        lampOuter: '#4C4C4C',
        lampOff: '#ffffff',
        lampRed: '#FF2E2E',
        lampYellow: '#FFD400',
        lampGreen: '#22D142',

        pmBody: '#5B6168',
        pmDot: '#ffffff',
        pmDotDim: 'rgba(255,255,255,0.25)',

        shuntBody: '#5B6168',

        busBar: '#9aa8c4',
        bbLabel: '#ffffff',

        routeLine: '#606060',

        siding: '#e8edf6',

        labelDefault: '#d7d7d7',
        labelHi: '#FFC919',
        labelFont: "'Plus Jakarta Sans', 'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    };

    /* ------------------------------------------------------------------------ *
     *  Small helpers
     * ------------------------------------------------------------------------ */
    function escapeXml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function uid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            const r = Math.random() * 16 | 0;
            return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }
    function attrLabel(cell) { return (cell.attrs && cell.attrs.label) || {}; }
    function attrLabelText(cell) {
        const l = attrLabel(cell);
        return l.text != null ? String(l.text) : '';
    }
    function attrLabelFill(cell) {
        return attrLabel(cell).fill || T.labelDefault;
    }
    function attrLabelSize(cell) {
        return attrLabel(cell).fontSize || 14;
    }

    /* Set of fill values the runtime uses to indicate a LIT lamp / occupied
     * track. Anything else (the OLD shape defaults #d4d4d4 / #3c4260, white,
     * grey, transparent, missing) is treated as OFF. */
    const LIT_FILLS = new Set([
        '#ff0000', '#ff2e2e', '#ffd400', '#22d142', '#00ff62', '#008633',
        'red', 'yellow', 'green', '#ff2828'
    ]);
    function isLit(colour) {
        if (!colour) return false;
        return LIT_FILLS.has(String(colour).toLowerCase().replace(/\s/g, ''));
    }

    /* ------------------------------------------------------------------------ *
     *  RENDERERS
     * ------------------------------------------------------------------------ */

    /* --- Track / rail strip (track.svg style) ------------------------------- */
    function renderRailStrip(x, cy, w, h) {
        h = Math.max(6, h);
        let svg = `<rect x="${x}" y="${cy - h / 2}" width="${w}" height="${h}" fill="${T.railBase}"/>`;
        svg += `<line x1="${x}" y1="${cy - h / 2 + 0.5}" x2="${x + w}" y2="${cy - h / 2 + 0.5}" stroke="${T.railEdge}" stroke-width="1"/>`;
        svg += `<line x1="${x}" y1="${cy + h / 2 - 0.5}" x2="${x + w}" y2="${cy + h / 2 - 0.5}" stroke="${T.railEdge}" stroke-width="1"/>`;
        const innerTop = cy - h / 2 + 1;
        const innerH = Math.max(1, h - 2);
        for (let i = 1; i + 1 <= w; i += 3) {
            svg += `<rect x="${x + i}" y="${innerTop}" width="1" height="${innerH}" fill="${T.sleeper}"/>`;
        }
        return svg;
    }

    /* --- Track / rail cell — rail strip + green pill label -------------------- */
    function renderTrack(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 60;
        const h = cell.size.height || 60;

        const labelAttrs = (cell.attrs && cell.attrs.label) || {};
        const labelTxt = labelAttrs.text != null ? String(labelAttrs.text) : '';
        const labelSize = +labelAttrs.fontSize || 13;

        const pathStroke = (cell.attrs && cell.attrs.path && cell.attrs.path.stroke) || '#3c4260';
        const occupied = isLit(pathStroke);
        const pillFill = occupied ? '#FF2E2E' : '#22A042';
        const pillStroke = occupied ? '#a01818' : '#1a8033';
        const railColor = occupied ? pathStroke : T.railBase;

        const cy = y + h / 2;
        const railH = Math.max(8, Math.min(14, h * 0.14));

        let svg = `<g class="sip-asset sip-track" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="transparent"/>`;

        /* ---- Rail strip (grey body + white edge lines + sleeper cross-marks) ---- */
        svg += `<rect x="${x}" y="${cy - railH / 2}" width="${w}" height="${railH}" fill="${railColor}"/>`;
        svg += `<line x1="${x}" y1="${cy - railH / 2 + 0.5}" x2="${x + w}" y2="${cy - railH / 2 + 0.5}" stroke="${T.railEdge}" stroke-width="1"/>`;
        svg += `<line x1="${x}" y1="${cy + railH / 2 - 0.5}" x2="${x + w}" y2="${cy + railH / 2 - 0.5}" stroke="${T.railEdge}" stroke-width="1"/>`;
        const innerTop = cy - railH / 2 + 1;
        const innerH = Math.max(1, railH - 2);
        for (let i = 4; i + 1 <= w; i += Math.max(3, Math.round(w / 80))) {
            svg += `<rect x="${x + i}" y="${innerTop}" width="1" height="${innerH}" fill="${T.sleeper}"/>`;
        }

        /* ---- Merge divider marks at left and right edges ---- *
         *  Short vertical yellow ticks at each end of the track section.
         *  These create visible dividers where track sections meet. */
        const divH = railH + 6;
        svg += `<line x1="${x}" y1="${cy - divH / 2}" x2="${x}" y2="${cy + divH / 2}" ` +
            `stroke="#FFC919" stroke-width="2" opacity="0.7" stroke-linecap="round"/>`;
        svg += `<line x1="${x + w}" y1="${cy - divH / 2}" x2="${x + w}" y2="${cy + divH / 2}" ` +
            `stroke="#FFC919" stroke-width="2" opacity="0.7" stroke-linecap="round"/>`;

        if (labelTxt) {
            const pillH = Math.max(20, labelSize + 8);
            const padX = 10;
            const approxTextW = Math.max(24, labelTxt.length * labelSize * 0.62);
            const pillW = approxTextW + 2 * padX;
            const px = x + (w - pillW) / 2;
            const py = cy - railH / 2 - 6 - pillH;

            svg += `<g class="sip-label">`;
            svg += `<rect x="${px}" y="${py}" width="${pillW}" height="${pillH}" ` +
                `rx="${pillH / 2}" ry="${pillH / 2}" ` +
                `fill="${pillFill}" stroke="${pillStroke}" stroke-width="1.2"/>`;
            svg += `<text x="${px + pillW / 2}" y="${py + pillH / 2 + labelSize / 3}" ` +
                `fill="#ffffff" font-family="${T.labelFont}" ` +
                `font-size="${labelSize}" font-weight="700" ` +
                `text-anchor="middle">${escapeXml(labelTxt)}</text>`;
            svg += `</g>`;
        }

        svg += `</g>`;
        return svg;
    }

    /* --- Curved track (Track3 / Track4) ------------------------------------- */
    function renderTrackCurve(cell, dir) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width;
        const h = cell.size.height;
        const path = dir === 'right'
            ? `M ${x} ${y + h / 2}  C ${x + w * 0.3} ${y + h / 2}, ${x + w * 0.7} ${y + h / 2 + 18}, ${x + w} ${y + h / 2 + 18}`
            : `M ${x + w} ${y + h / 2}  C ${x + w * 0.7} ${y + h / 2}, ${x + w * 0.3} ${y + h / 2 + 18}, ${x} ${y + h / 2 + 18}`;
        return `<g class="sip-asset sip-track-curve" data-id="${cell.id}" data-type="${cell.type}">` +
            `<path d="${path}" fill="none" stroke="${T.railBase}" stroke-width="7" stroke-linecap="round"/>` +
            `<path d="${path}" fill="none" stroke="${T.sleeper}" stroke-width="1" stroke-dasharray="1,2"/>` +
            `</g>`;
    }

    /* --- Crossover diagonal ------------------------------------------------- */
    function renderCrossover(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width;
        const h = cell.size.height;
        const x1 = x, y1 = y;
        const x2 = x + w, y2 = y + h;
        const dx = x2 - x1, dy = y2 - y1;
        const len = Math.hypot(dx, dy);
        const ang = Math.atan2(dy, dx) * 180 / Math.PI;

        let svg = `<g class="sip-asset sip-crossover" data-id="${cell.id}" data-type="${cell.type}"`;
        svg += ` transform="translate(${x1} ${y1}) rotate(${ang})">`;
        svg += renderRailStrip(0, 0, len, 9);
        svg += `</g>`;
        return svg;
    }

    /* --- Point machine — diagonal rail strip with centred indicator circle -- *
     *  Draws a crossover-style diagonal track strip (matching crossover.svg)
     *  with the point-machine indicator circle placed at the EXACT centre
     *  of the bounding box, showing the point's Normal/Reverse status.
     *
     *  Normal variant: diagonal goes bottom-left → top-right
     *  Mirror variant: diagonal goes top-left   → bottom-right
     * ----------------------------------------------------------------------- */
    function renderPointMachine(cell, mirror) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 60;

        const bodyAttrs = (cell.attrs && cell.attrs.body) || {};
        const strokeC = bodyAttrs.stroke || '#3c4260';
        const bodyColor = isLit(strokeC)
            ? strokeC
            : (strokeC === '#3c4260' || strokeC === '#6a7596' ? '#8aa3c0' : strokeC);

        const c1 = (cell.attrs && cell.attrs.circle1) || {};
        const indFill = c1.fill || '#d4d4d4';
        const indLit = isLit(indFill);

        const labelAttrs = (cell.attrs && cell.attrs.label) || {};
        const labelTxt = labelAttrs.text != null ? String(labelAttrs.text) : '';
        const labelFill = labelAttrs.fill && labelAttrs.fill !== 'transparent'
            ? labelAttrs.fill
            : '#FFC919';

        /* PM display options (set from the SIP editor inspector):
           attrs.pm = {
               labelSide:   'auto' | 'opposite'   — 'opposite' puts the name on
                            the REVERSE side of the diagonal (away from the
                            indicator circle) to avoid overlapping it,
               labelOffset: extra px gap pushing the label away from the rail
           }
           Legacy fallback: attrs.label.side is also honoured.            */
        const pmProps = (cell.attrs && cell.attrs.pm) || {};
        const pmLabelSide = String(pmProps.labelSide || labelAttrs.side || 'auto').toLowerCase();
        const pmLabelExtra = Math.max(0, +pmProps.labelOffset || 0);

        /* Operate blink — set by sip-telemetry.js when live data shows a
           NORMAL ⇆ REVERSE transition; cleared automatically after a few
           seconds. Rendered as an SVG opacity pulse on the lit indicator. */
        const pmBlink = !!(cell.attrs && cell.attrs.pmBlink);

        /* ---- Diagonal rail strip geometry ---- */
        let x1, y1, x2, y2;
        if (mirror) {
            // top-left → bottom-right
            x1 = x; y1 = y;
            x2 = x + w; y2 = y + h;
        } else {
            // bottom-left → top-right
            x1 = x; y1 = y + h;
            x2 = x + w; y2 = y;
        }
        const dx = x2 - x1;
        const dy = y2 - y1;
        const len = Math.hypot(dx, dy);
        const ang = Math.atan2(dy, dx);          // angle in radians
        const angDeg = ang * 180 / Math.PI;
        const railW = Math.max(7, Math.min(12, h * 0.12));  // rail strip width

        // Normal perpendicular for edge lines
        const nx = -Math.sin(ang) * railW / 2;
        const ny = Math.cos(ang) * railW / 2;

        let svg = `<g class="sip-asset sip-pm" data-id="${cell.id}" data-type="${cell.type}">`;

        /* ---- Rail body (grey fill between two edge lines) ---- */
        const bodyPoly = [
            [x1 + nx, y1 + ny],
            [x2 + nx, y2 + ny],
            [x2 - nx, y2 - ny],
            [x1 - nx, y1 - ny]
        ].map(p => p[0] + ',' + p[1]).join(' ');
        svg += `<polygon points="${bodyPoly}" fill="#7E7E7E"/>`;

        /* ---- White edge lines (top and bottom of rail strip) ---- */
        svg += `<line x1="${x1 + nx}" y1="${y1 + ny}" x2="${x2 + nx}" y2="${y2 + ny}" ` +
            `stroke="white" stroke-width="1" opacity="0.7"/>`;
        svg += `<line x1="${x1 - nx}" y1="${y1 - ny}" x2="${x2 - nx}" y2="${y2 - ny}" ` +
            `stroke="white" stroke-width="1" opacity="0.7"/>`;

        /* ---- Yellow centre merge line (route indication) ---- */
        svg += `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" ` +
            `stroke="#FFC919" stroke-width="1.5" opacity="0.8" stroke-linecap="round"/>`;
        /* ---- Sleeper cross-marks along the diagonal ---- */
        const sleeperGap = Math.max(3, len / 36);
        const sleeperHalf = railW * 0.55;
        const snx = Math.cos(ang);    // unit direction along rail
        const sny = Math.sin(ang);
        const pnx = -sny;             // perpendicular to rail
        const pny = snx;
        for (let t = sleeperGap; t < len - sleeperGap * 0.5; t += sleeperGap) {
            const cx = x1 + snx * t;
            const cy = y1 + sny * t;
            svg += `<line x1="${cx - pnx * sleeperHalf}" y1="${cy - pny * sleeperHalf}" ` +
                `x2="${cx + pnx * sleeperHalf}" y2="${cy + pny * sleeperHalf}" ` +
                `stroke="#515151" stroke-width="1"/>`;
        }

        /* ---- Glow when the body is lit (occupied / route set) ---- */
        if (isLit(strokeC)) {
            svg += `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" ` +
                `stroke="${strokeC}" stroke-width="${railW + 6}" opacity="0.2" stroke-linecap="round"/>`;
        }

        /* ---- Indicator circle — centred but OFFSET below the diagonal ---- *
         *  The circle sits next to the rail strip on the lower/outer side,
         *  not on top of the track. We offset perpendicular to the diagonal
         *  by enough distance to clear the rail body completely.
         * ------------------------------------------------------------------- */
        const indR = Math.max(10, Math.min(14, Math.min(w, h) * 0.14));
        const baseOffset = railW / 2 + indR + 3;  // clear the rail edge + circle radius + gap

        /* Perpendicular unit vector to the diagonal direction. */
        const perpX = -Math.sin(ang);
        const perpY = Math.cos(ang);
        /* Choose the sign so the circle goes DOWNWARD / to the outer side
           (below the diagonal track strip) by default. */
        const sign = perpY > 0 ? 1 : -1;

        /* User adjustment (SIP editor inspector): attrs.pm.indOffset shifts the
           indicator circle along the perpendicular of the PM line. Positive
           moves it further DOWN / outward, negative moves it UP / inward (and
           past the line to the other side for large negative values). */
        const indAdjust = +(pmProps.indOffset) || 0;
        const offsetDist = baseOffset + indAdjust;

        const indCx = x + w / 2 + perpX * offsetDist * sign;
        const indCy = y + h / 2 + perpY * offsetDist * sign;

        const blinkAnim = pmBlink && indLit
            ? `<animate attributeName="opacity" values="1;0.12;1" dur="0.7s" repeatCount="indefinite"/>`
            : '';

        /* Static dark ring stays solid; the lit parts (glow + fill + shine)
           are grouped so the blink pulses them together. */
        svg += `<circle cx="${indCx}" cy="${indCy}" r="${indR + 1.5}" fill="#0a0f1e" stroke="#22d3ee" stroke-width="1.8"/>`;
        svg += `<g class="sip-pm-ind${pmBlink && indLit ? ' sip-pm-blink' : ''}">`;
        if (indLit) {
            svg += `<circle cx="${indCx}" cy="${indCy}" r="${indR + 6}" fill="${indFill}" opacity="0.15"/>`;
            svg += `<circle cx="${indCx}" cy="${indCy}" r="${indR + 3}" fill="${indFill}" opacity="0.30"/>`;
        }
        svg += `<circle cx="${indCx}" cy="${indCy}" r="${indR}" ` +
            `fill="${indLit ? indFill : '#d4d4d4'}"/>`;
        if (indLit) {
            svg += `<ellipse cx="${indCx - indR * 0.3}" cy="${indCy - indR * 0.3}" ` +
                `rx="${indR * 0.3}" ry="${indR * 0.18}" fill="white" opacity="0.55"/>`;
        }
        svg += blinkAnim;
        svg += `</g>`;

        /* ---- Label (rotated along the yellow merge line, like "101/102") ---- */
        if (labelTxt) {
            const midX = (x1 + x2) / 2;
            const midY = (y1 + y2) / 2;
            /* Offset the label slightly to the side of the diagonal so it
               doesn't overlap the yellow line or the circle.
               labelSide 'opposite' flips the label to the REVERSE side of the
               diagonal (away from the indicator circle) — used when the
               default side overlaps neighbouring tracks/assets. */
            const labelSign = (pmLabelSide === 'opposite' || pmLabelSide === 'reverse' || pmLabelSide === 'flip')
                ? -sign : sign;
            const labelOff = railW + 6 + pmLabelExtra;
            const lx = midX + perpX * labelOff * labelSign;
            const ly = midY + perpY * labelOff * labelSign;
            /* Rotate text to follow the diagonal angle.
               Ensure text is always readable (not upside-down). */
            let rotDeg = angDeg;
            if (rotDeg > 90) rotDeg -= 180;
            if (rotDeg < -90) rotDeg += 180;
            svg += `<text x="${lx}" y="${ly}" fill="#0a0f1e" stroke="#0a0f1e" stroke-width="3" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="800" ` +
                `text-anchor="middle" dominant-baseline="central" paint-order="stroke" ` +
                `transform="rotate(${rotDeg.toFixed(1)} ${lx} ${ly})">${escapeXml(labelTxt)}</text>`;
            svg += `<text x="${lx}" y="${ly}" fill="${labelFill}" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="800" ` +
                `text-anchor="middle" dominant-baseline="central" ` +
                `transform="rotate(${rotDeg.toFixed(1)} ${lx} ${ly})">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Signal lamps ------------------------------------------------------- */

    /* ------------------------------------------------------------------------ *
     *  Stick / mast for signals & shunts
     *
     *  Real SIP yards show signals & shunts attached to their rail via an
     *  L-shaped connector:
     *      • a short vertical drop from the rail bed to the signal level
     *      • a short horizontal piece joining the back of the signal box
     *      • a small finial stem + cap on the far side of the head
     *  The L auto-flips depending on whether the cell sits above or below its
     *  nearest rail.
     *
     *  We need to know where the rails are. _railContext is set by
     *  renderRailLayer() before any per-cell render runs, so by the time
     *  renderStick() is called the rail y-centres are known.
     *
     *  Geometry inputs:
     *      headBox = {x, y, w, h}   — bounding box of the visible signal/shunt
     *                                  head (NOT the cell bounding box)
     *      backSide = 'left' | 'right' — which side of the head the post
     *                                    attaches to (i.e. the side facing
     *                                    away from the direction of travel)
     * ------------------------------------------------------------------------ */
    let _railContext = { bands: [] };

    function renderStick(headBox, backSide) {
        const bands = _railContext.bands || [];
        if (!bands.length) return '';

        const hcy = headBox.y + headBox.h / 2;
        // Find the rail band closest to this head vertically.
        let nearest = bands[0];
        let bestDist = Math.abs(bands[0].cy - hcy);
        for (let i = 1; i < bands.length; i++) {
            const d = Math.abs(bands[i].cy - hcy);
            if (d < bestDist) { nearest = bands[i]; bestDist = d; }
        }
        // If the head is too far from any rail (> 200 px), skip the stick.
        if (bestDist > 200) return '';

        const railCy = nearest.cy;
        // Determine orientation: is the head ABOVE or BELOW the rail?
        const headAbove = hcy < railCy;

        // The vertical drop runs from the rail edge to the level of the
        // back-of-box top/bottom — NOT all the way to the head centre.
        const railEdgeY = headAbove ? railCy - 9 : railCy + 9;   // rail bed half-height = 9
        const dropEndY = headAbove ? headBox.y - 4 : headBox.y + headBox.h + 4;

        // Horizontal joiner runs from the drop x over to the back side of the head.
        // backSide='right' means the post hangs on the right edge of the head.
        const backX = backSide === 'right' ? headBox.x + headBox.w + 4 : headBox.x - 4;
        // The drop sits at backX (no extra horizontal needed if backX == drop x),
        // but in practice we offset the drop slightly so the L is visible.
        const dropX = backX;

        const STK = '#7a7a7a';
        let svg = `<g class="sip-stick" pointer-events="none">`;
        // Vertical drop
        svg += `<line x1="${dropX}" y1="${railEdgeY}" x2="${dropX}" y2="${dropEndY}" ` +
            `stroke="${STK}" stroke-width="2.5" stroke-linecap="round"/>`;
        // Tiny horizontal stub joining the post to the head back edge
        const stubX2 = backSide === 'right' ? headBox.x + headBox.w : headBox.x;
        svg += `<line x1="${dropX}" y1="${dropEndY}" x2="${stubX2}" y2="${dropEndY}" ` +
            `stroke="${STK}" stroke-width="2.5" stroke-linecap="round"/>`;

        // Finial — small stem + cap dot on the FAR side of the head
        // (opposite the post). Mounted at the top edge if head is above rail,
        // bottom edge if head is below.
        const finialX = headBox.x + headBox.w / 2;
        const finialEdge = headAbove ? headBox.y : headBox.y + headBox.h;
        const finialTip = headAbove ? finialEdge - 10 : finialEdge + 10;
        const finialCap = headAbove ? finialEdge - 12 : finialEdge + 12;
        svg += `<line x1="${finialX}" y1="${finialEdge}" x2="${finialX}" y2="${finialTip}" ` +
            `stroke="${STK}" stroke-width="2" stroke-linecap="round"/>`;
        svg += `<circle cx="${finialX}" cy="${finialCap}" r="2.5" fill="${STK}"/>`;

        svg += `</g>`;
        return svg;
    }

    function renderSignalLamp(cell, _ignored, kind) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 30;
        const h = cell.size.height || 30;

        const c1 = (cell.attrs && cell.attrs.circle1) || {};
        const fillSaved = c1.fill || '#d4d4d4';
        const lit = isLit(fillSaved);

        /* No slot/tray rect — the visible "gray pill" behind a 3-lamp signal
           is now its own cell (examples.SignalBackground) that the user drops
           and sizes independently. A lamp is purely the circle, centred in
           its cell, scaled to ~70% of the smaller dimension. */
        const lcx = x + w / 2;
        const lcy = y + h / 2;
        const r = Math.max(4, Math.min(w, h) * 0.35);

        let svg = `<g class="sip-asset sip-signal" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<circle cx="${lcx}" cy="${lcy}" r="${r + 1.5}" fill="none" stroke="#2a2a2a" stroke-width="1"/>`;

        if (lit) {
            let litColour;
            switch (kind) {
                case 'R': litColour = '#FF2E2E'; break;
                case 'G': litColour = '#22D142'; break;
                case 'Y': litColour = '#FFD400'; break;
                case 'X': litColour = '#FFD400'; break;
                default: litColour = fillSaved;
            }
            svg += `<circle cx="${lcx}" cy="${lcy}" r="${r + 4}" fill="${litColour}" opacity="0.18"/>`;
            svg += `<circle cx="${lcx}" cy="${lcy}" r="${r + 2}" fill="${litColour}" opacity="0.35"/>`;
            svg += `<circle cx="${lcx}" cy="${lcy}" r="${r}"     fill="${litColour}"/>`;
            svg += `<ellipse cx="${lcx - r * 0.35}" cy="${lcy - r * 0.4}" rx="${r * 0.35}" ry="${r * 0.22}" fill="white" opacity="0.45"/>`;
        } else {
            /* unlit / blank — the kind-specific symbol is faintly colored
               (rgba ~ 0.55 opacity of its aspect colour) so each lamp still
               identifies its aspect type at a glance, the way real signaling
               diagrams hint at lamp colour through the lens marking. */
            const KIND_TINT = {
                R: 'rgba(255, 46, 46, 0.62)',
                G: 'rgba(34, 209, 66, 0.62)',
                Y: 'rgba(255, 212, 0, 0.7)',
                X: 'rgba(255, 176, 32, 0.7)'
            };
            const sym = KIND_TINT[kind] || '#3a3a3a';
            svg += `<circle cx="${lcx}" cy="${lcy}" r="${r}" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="1.5"/>`;
            svg += `<circle cx="${lcx}" cy="${lcy}" r="${r - 1.5}" fill="none" stroke="rgba(0,0,0,0.12)" stroke-width="1"/>`;
            if (kind === 'R') {
                svg += `<line x1="${lcx - r + 1}" y1="${lcy - 1.4}" x2="${lcx + r - 1}" y2="${lcy - 1.4}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
                svg += `<line x1="${lcx - r + 1}" y1="${lcy + 1.4}" x2="${lcx + r - 1}" y2="${lcy + 1.4}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
            } else if (kind === 'G') {
                svg += `<line x1="${lcx}" y1="${lcy - r + 1}" x2="${lcx}" y2="${lcy + r - 1}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
            } else if (kind === 'Y') {
                const d = r * 0.72;
                svg += `<line x1="${lcx - d}" y1="${lcy - d}" x2="${lcx + d}" y2="${lcy + d}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
            } else if (kind === 'X') {
                const d = r * 0.72;
                const off = 2.6;
                svg += `<line x1="${lcx - d}" y1="${lcy - d + off}" x2="${lcx + d}" y2="${lcy + d + off}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
                svg += `<line x1="${lcx - d}" y1="${lcy - d - off}" x2="${lcx + d}" y2="${lcy + d - off}" stroke="${sym}" stroke-width="1.8" stroke-linecap="round"/>`;
            }
        }

        const lblAttr = (cell.attrs && cell.attrs.label) || {};
        const lblText = lblAttr.text != null ? String(lblAttr.text) : '';
        /* Same transparent-respect rule as before — keeps 3-aspect signals
           from stacking three labels on top of each other. */
        const lblIsVisible = !!lblText && lblAttr.fill !== 'transparent';
        if (lblIsVisible) {
            const lblFill = lblAttr.fill || 'rgba(220,228,245,0.85)';
            const lblSize = +lblAttr.fontSize || 14;
            const ly = lcy + r + lblSize + 4;
            svg += `<text x="${lcx}" y="${ly}" fill="#0a0f1e" stroke="#0a0f1e" stroke-width="3" ` +
                `font-family="${T.labelFont}" font-size="${lblSize}" font-weight="700" ` +
                `text-anchor="middle" paint-order="stroke">${escapeXml(lblText)}</text>`;
            svg += `<text x="${lcx}" y="${ly}" fill="${lblFill}" ` +
                `font-family="${T.labelFont}" font-size="${lblSize}" font-weight="700" ` +
                `text-anchor="middle">${escapeXml(lblText)}</text>`;
        }

        // Stick / mast — only on the labelled lamp (the visible head).
        if (lblIsVisible) {
            svg += renderStick({ x: x, y: y, w: w, h: h }, 'right');
        }

        svg += `</g>`;
        return svg;
    }

    /* --- Signal background (separate draggable tray) ----------------------- *
     *  Mirrors signal_Background.svg — a flat rounded gray pill at #606060.
     *  Used as the "tray" behind one or more lamp circles. Fully resizable;
     *  the renderer rescales rx with size so it always reads as a pill.
     * ----------------------------------------------------------------------- */
    function renderSignalBackground(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 54;
        const h = cell.size.height || 27;
        const rx = Math.min(6, Math.min(w, h) / 4);

        const lblAttr = (cell.attrs && cell.attrs.label) || {};
        const lblText = lblAttr.text != null ? String(lblAttr.text) : '';
        const lblVisible = !!lblText && lblAttr.fill !== 'transparent';

        let svg = `<g class="sip-asset sip-sig-bg" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" ry="${rx}" fill="#606060"/>`;
        if (lblVisible) {
            const fill = lblAttr.fill || T.labelDefault;
            const size = +lblAttr.fontSize || 12;
            svg += `<text x="${x + w / 2}" y="${y + h + size + 4}" fill="${fill}" ` +
                `font-family="${T.labelFont}" font-size="${size}" font-weight="600" ` +
                `text-anchor="middle">${escapeXml(lblText)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Signal (composite) ------------------------------------------------ *
     *  ONE cell that contains: background pill + N lamps + optional stand.
     *  Driven entirely by props (cell.attrs.signal) so the user adds/removes
     *  lamps by editing a single string.
     *
     *  attrs.signal = {
     *      lamps:        'BBB',     // string of lamp kinds — one char per lamp.
     *                               //   B = blank, R = red, Y = yellow,
     *                               //   G = green, X = double-yellow
     *      stand:        'bottom',  // 'none' | 'top' | 'bottom'
     *      standLength:  30         // px length of the vertical drop of the
     *                               // stand (the horizontal arm is fixed at
     *                               // half the signal width).
     *  }
     *
     *  Lamps are auto-spaced with EQUAL gaps around and between, scaled to
     *  fill the cell width.  Lamp diameter is derived from cell height so
     *  resizing the bounding box updates everything proportionally.
     * ----------------------------------------------------------------------- */
    const SIGNAL_LAMP_LIT = {
        R: '#FF2E2E', G: '#22D142', Y: '#FFD400', X: '#FFD400'
    };

    /* ------------------------------------------------------------------------ *
     *  resolveStandPos  — backward-compat mapper
     *  -------------------------------------------------------------------------
     *  Old format:  stand:'top'|'bottom', standSide:'left'|'center'|'right'
     *  New format:  standPos: 'none'|'T'|'B'|'L'|'R'|'TL'|'TR'|'BL'|'BR'
     *
     *  If the new `standPos` is already set we use it directly.
     *  Otherwise we derive it from the legacy properties so old layouts
     *  render correctly without any data migration.
     * ----------------------------------------------------------------------- */
    function resolveStandPos(sigProps) {
        if (sigProps.standPos) return sigProps.standPos;
        const m = sigProps.stand || 'bottom';
        if (m === 'none') return 'none';
        const s = sigProps.standSide || 'center';
        if (m === 'top') return s === 'left' ? 'TL' : s === 'right' ? 'TR' : 'T';
        if (m === 'bottom') return s === 'left' ? 'BL' : s === 'right' ? 'BR' : 'B';
        return 'B';
    }

    /* ------------------------------------------------------------------------ *
     *  renderStand8  — draw an L-shaped stand at any of 8 anchor positions
     *  -------------------------------------------------------------------------
     *  box   = { x, y, w, h }   bounding box of the signal / shunt body
     *  pos   = 'T'|'B'|'L'|'R'|'TL'|'TR'|'BL'|'BR'
     *  len   = total reach (px)
     *  bendR = corner rounding radius (default 4)
     *
     *  Side positions (T/B/L/R) produce a STRAIGHT line from the centre of
     *  that edge outward.
     *  Corner positions (TL/TR/BL/BR) produce an L-shaped path: a short
     *  segment along the near edge, then a 90° bend outward — the classic
     *  signal-stand bracket. The bend ratio splits the total length 40:60
     *  (first leg shorter) for a visually pleasing elbow.
     * ----------------------------------------------------------------------- */
    function renderStand8(box, pos, len, bendR, opts) {
        if (!pos || pos === 'none') return '';
        opts = opts || {};
        len = Math.max(8, len || 30);
        bendR = bendR != null ? bendR : 4;
        const bx = box.x, by = box.y, bw = box.w, bh = box.h;
        const mx = bx + bw / 2, my = by + bh / 2;

        /*
         * L/R stands must attach from the exact centre of the signal/shunt side.
         * The horizontal leg is independently adjustable through signal.standArm;
         * the vertical drop uses signal.standLength. This gives a real L-bracket
         * instead of a loose line from the corner/bottom of the symbol.
         */
        const armLen = Math.max(0, +opts.standArm || Math.max(10, len * 0.45));
        const dropMode = String(opts.standDrop || opts.drop || 'down').toLowerCase();
        const vSign = (dropMode === 'up' || dropMode === 'top') ? -1 : 1;

        let pts;  // array of [x,y] waypoints
        switch (pos) {
            /* ---- Side centres ---- */
            case 'T': pts = [[mx, by], [mx, by - len]]; break;
            case 'B': pts = [[mx, by + bh], [mx, by + bh + len]]; break;

            /* ---- Left / Right: L-bracket from exact side centre ---- */
            case 'L': pts = [[bx, my], [bx - armLen, my], [bx - armLen, my + vSign * len]]; break;
            case 'R': pts = [[bx + bw, my], [bx + bw + armLen, my], [bx + bw + armLen, my + vSign * len]]; break;

            /* ---- Corners: legacy L-shaped bracket ---- */
            case 'TL': pts = [[bx, by], [bx - len * 0.4, by], [bx - len * 0.4, by - len * 0.6]]; break;
            case 'TR': pts = [[bx + bw, by], [bx + bw + len * 0.4, by], [bx + bw + len * 0.4, by - len * 0.6]]; break;
            case 'BL': pts = [[bx, by + bh], [bx - len * 0.4, by + bh], [bx - len * 0.4, by + bh + len * 0.6]]; break;
            case 'BR': pts = [[bx + bw, by + bh], [bx + bw + len * 0.4, by + bh], [bx + bw + len * 0.4, by + bh + len * 0.6]]; break;
            default: return '';
        }

        /* Build SVG path — for 3-point paths add a rounded corner at the bend */
        let d;
        if (pts.length === 2) {
            d = `M ${pts[0][0]} ${pts[0][1]} L ${pts[1][0]} ${pts[1][1]}`;
        } else {
            const A = pts[0], B = pts[1], C = pts[2];
            const r = Math.min(bendR, Math.hypot(B[0] - A[0], B[1] - A[1]) * 0.45,
                Math.hypot(C[0] - B[0], C[1] - B[1]) * 0.45);
            const d1 = [B[0] - A[0], B[1] - A[1]];
            const l1 = Math.hypot(d1[0], d1[1]) || 1;
            const u1 = [d1[0] / l1, d1[1] / l1];
            const d2 = [C[0] - B[0], C[1] - B[1]];
            const l2 = Math.hypot(d2[0], d2[1]) || 1;
            const u2 = [d2[0] / l2, d2[1] / l2];
            const arcStart = [B[0] - u1[0] * r, B[1] - u1[1] * r];
            const arcEnd = [B[0] + u2[0] * r, B[1] + u2[1] * r];
            d = `M ${A[0]} ${A[1]} L ${arcStart[0]} ${arcStart[1]} ` +
                `Q ${B[0]} ${B[1]} ${arcEnd[0]} ${arcEnd[1]} ` +
                `L ${C[0]} ${C[1]}`;
        }

        const endPt = pts[pts.length - 1];
        return `<path d="${d}" stroke="#b8b8b8" stroke-width="1.5" ` +
            `fill="none" stroke-linecap="round" stroke-linejoin="round"/>` +
            `<circle cx="${endPt[0]}" cy="${endPt[1]}" r="2" fill="#b8b8b8"/>`;
    }

    /* ------------------------------------------------------------------------ *
     *  labelSideFromStand  — choose the best label anchor opposite the stand
     *  Returns { lx, ly, anchor } for the <text> element.
     * ----------------------------------------------------------------------- */
    function labelSideFromStand(standPos, x, y, w, h, lblSize, labelGap) {
        const pad = Math.max(0, +labelGap || 6);
        switch (standPos) {
            case 'B': case 'BL': case 'BR':
                return { lx: x + w + pad, ly: y + h / 2, anchor: 'start' };
            case 'T': case 'TL': case 'TR':
                return { lx: x - pad, ly: y + h / 2, anchor: 'end' };
            case 'L':
                return { lx: x + w + pad, ly: y + h / 2, anchor: 'start' };
            case 'R':
                return { lx: x - pad, ly: y + h / 2, anchor: 'end' };
            default:
                return { lx: x + w + pad, ly: y + h / 2, anchor: 'start' };
        }
    }

    function renderSignalComposite(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 90;
        const h = cell.size.height || 24;

        /* props with safe defaults */
        const sigProps = (cell.attrs && cell.attrs.signal) || {};
        const lampsStr = typeof sigProps.lamps === 'string' && sigProps.lamps.length ? sigProps.lamps : 'BBB';
        const standPos = resolveStandPos(sigProps);
        const standLen = Math.max(8, +sigProps.standLength || 30);

        /* lit-state map */
        const litStr = typeof sigProps.lit === 'string' ? sigProps.lit.toUpperCase() : '';
        const litSet = {};
        for (let i = 0; i < litStr.length; i++) litSet[litStr[i]] = 1;

        const lamps = String(lampsStr).toUpperCase().split('');
        const n = Math.max(1, lamps.length);

        let svg = `<g class="sip-asset sip-signal-composite" data-id="${cell.id}" data-type="${cell.type}">`;

        /* 1. Stand — drawn FIRST so it sits behind the pill */
        svg += renderStand8({ x: x, y: y, w: w, h: h }, standPos, standLen, 4, sigProps);

        /* 2. Background pill */
        const rx = Math.min(h / 2 - 1, 6);
        svg += `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" ry="${rx}" fill="#606060"/>`;

        /* 3. Lamps with equal padding (n+1 gaps total) */
        const lampD = Math.max(6, h - 6);
        const totalLamps = n * lampD;
        const totalGap = Math.max(0, w - totalLamps);
        const gap = totalGap / (n + 1);
        const r = lampD / 2;

        for (let i = 0; i < n; i++) {
            const cx = x + gap * (i + 1) + lampD * i + r;
            const cy = y + h / 2;
            const kind = lamps[i];
            const isLitLamp = !!litSet[kind];
            const litColour = SIGNAL_LAMP_LIT[kind];

            if (isLitLamp && litColour) {
                svg += `<circle cx="${cx}" cy="${cy}" r="${r + 2.5}" fill="${litColour}" opacity="0.18"/>`;
                svg += `<circle cx="${cx}" cy="${cy}" r="${r + 1}"   fill="${litColour}" opacity="0.35"/>`;
                svg += `<circle cx="${cx}" cy="${cy}" r="${r}"       fill="${litColour}"/>`;
                svg += `<ellipse cx="${cx - r * 0.35}" cy="${cy - r * 0.4}" rx="${r * 0.35}" ry="${r * 0.22}" fill="white" opacity="0.45"/>`;
            } else {
                svg += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="1.2"/>`;
                if (kind === 'R') {
                    svg += `<line x1="${cx - r + 1}" y1="${cy - 1.2}" x2="${cx + r - 1}" y2="${cy - 1.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                    svg += `<line x1="${cx - r + 1}" y1="${cy + 1.2}" x2="${cx + r - 1}" y2="${cy + 1.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'G') {
                    svg += `<line x1="${cx}" y1="${cy - r + 1}" x2="${cx}" y2="${cy + r - 1}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'Y') {
                    const d = r * 0.72;
                    svg += `<line x1="${cx - d}" y1="${cy - d}" x2="${cx + d}" y2="${cy + d}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'X') {
                    const d = r * 0.72;
                    svg += `<line x1="${cx - d}" y1="${cy - d + 2.2}" x2="${cx + d}" y2="${cy + d + 2.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                    svg += `<line x1="${cx - d}" y1="${cy - d - 2.2}" x2="${cx + d}" y2="${cy + d - 2.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                }
            }
        }

        /* 4. Optional Route / Calling attachment — disabled by default.
           When enabled on a main signal, route and calling indicators are
           physically attached to the selected edge of the main signal body,
           not drawn as a loose/overlapping module. This keeps 2/3/4/5-aspect
           heads compact while allowing route/calling on any side. */
        const routeProps = (cell.attrs && cell.attrs.route) || null;
        if (routeProps && boolRouteProp(routeProps.enabled, false)) {
            svg += renderAttachedRouteCallingToSignal({ x: x, y: y, w: w, h: h }, normaliseRouteCallingConfig(cell, true));
        }

        /* 5. Label — auto-positioned opposite the stand */
        const lblAttr = (cell.attrs && cell.attrs.label) || {};
        const lblText = lblAttr.text != null ? String(lblAttr.text) : '';
        if (lblText && lblAttr.fill !== 'transparent') {
            const lblFill = lblAttr.fill || '#dcd7d7';
            const lblSize = +lblAttr.fontSize || 12;
            const lp = labelSideFromStand(standPos, x, y, w, h, lblSize, sigProps.labelGap);
            svg += `<text x="${lp.lx}" y="${lp.ly}" fill="${lblFill}" ` +
                `font-family="${T.labelFont}" font-size="${lblSize}" font-weight="700" ` +
                `text-anchor="${lp.anchor}" dominant-baseline="central">${escapeXml(lblText)}</text>`;
        }

        svg += `</g>`;
        return svg;
    }

    /* --- Signal post -------------------------------------------------------- */
    function renderPost(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 100;
        const labelTxt = attrLabelText(cell);
        const fill = attrLabel(cell).fill || T.siding;
        const size = attrLabel(cell).fontSize || 16;

        const padX = Math.max(10, Math.min(20, w * 0.15));
        const pillH = Math.max(22, Math.min(36, h * 0.45));
        const pillY = y + (h - pillH) / 2;

        let svg = `<g class="sip-asset sip-post" data-id="${cell.id}" data-type="${cell.type}">`;
        if (labelTxt) {
            svg += `<rect x="${x + padX}" y="${pillY}" width="${w - 2 * padX}" height="${pillH}" ` +
                `rx="${pillH / 2}" ry="${pillH / 2}" ` +
                `fill="rgba(34,211,238,0.08)" stroke="rgba(34,211,238,0.45)" stroke-width="1.2"/>`;
            svg += `<text x="${x + w / 2}" y="${pillY + pillH / 2 + size / 3}" fill="${fill}" ` +
                `font-family="${T.labelFont}" font-size="${size}" font-weight="700" ` +
                `text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        } else {
            svg += `<rect x="${x + padX}" y="${pillY}" width="${w - 2 * padX}" height="${pillH}" ` +
                `rx="${pillH / 2}" ry="${pillH / 2}" ` +
                `fill="none" stroke="rgba(255,255,255,0.25)" stroke-width="1" stroke-dasharray="3,3"/>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Shunt signal — three variants -------------------------------------- *
     *  Variant 1 (examples.Shaunt)  → dim TOP,  lit BL,  lit BR    [proceed]
     *  Variant 2 (examples.Shaunt2) → lit TOP,  dim BL,  lit BR    [diverge right]
     *  Variant 3 (examples.Shaunt3) → dim TOP,  dim BL,  dim BR    [blank / off]
     * ------------------------------------------------------------------------ */
    function renderShunt(cell, variant) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 25;
        const h = cell.size.height || 23;

        const bodyAttrs = (cell.attrs && cell.attrs.body) || {};
        const bodyFill = bodyAttrs.fill || '#5B6168';

        const labelAttrs = (cell.attrs && cell.attrs.label) || {};
        const labelTxt = labelAttrs.text != null ? String(labelAttrs.text) : '';

        // Live telemetry override: ON → proceed lamps lit (blinking), OFF → all dim.
        // Falls back to the stencil's static variant when no live state is present.
        const live = cell.attrs ? cell.attrs.shuntLive : undefined;
        let v = variant;
        let blink = '';
        if (live !== undefined) {
            v = live === 'ON' ? 1 : live === 'OFF' ? 2 : 3;
        }

        const bw = Math.max(44, Math.min(w * 1.5, 64));
        const bh = bw * (23 / 25);
        const bx = x + (w - bw) / 2;
        const by = y + (h - bh) / 2;
        const sx = bw / 25;
        const sy = bh / 23;

        let svg = `<g class="sip-asset sip-shunt" data-id="${cell.id}" data-type="${cell.type}">`;

        if (isLit(bodyFill)) {
            svg += `<ellipse cx="${bx + bw / 2}" cy="${by + bh / 2}" rx="${bw * 0.7}" ry="${bh * 0.7}" ` +
                `fill="${bodyFill}" opacity="0.22"/>`;
        }

        const pathD = `M ${bx + 7.91188 * sx} ${by + 0.180471 * sy} ` +
            `C ${bx + 5.77625 * sx} ${by + 0.73042 * sy} ${bx + 4.28312 * sx} ${by + 2.8874 * sy} ${bx + 2.14 * sx} ${by + 8.52086 * sy} ` +
            `C ${bx + 0.436875 * sx} ${by + 12.9975 * sy} ${bx + 0.005625 * sx} ${by + 15.0254 * sy} ${bx + 0.003125 * sx} ${by + 18.5649 * sy} ` +
            `L ${bx} ${by + bh} L ${bx + bw / 2} ${by + bh} L ${bx + bw} ${by + bh} ` +
            `L ${bx + bw} ${by + 17.5017 * sy} L ${bx + bw} ${by + 12.0041 * sy} ` +
            `L ${bx + 21.7913 * sx} ${by + 9.09038 * sy} ` +
            `C ${bx + 20.0269 * sx} ${by + 7.48825 * sy} ${bx + 17.2844 * sx} ${by + 4.90306 * sy} ${bx + 15.6975 * sx} ${by + 3.34558 * sy} ` +
            `C ${bx + 12.8669 * sx} ${by + 0.567087 * sy} ${bx + 10.3638 * sx} ${by - 0.450839 * sy} ${bx + 7.91188 * sx} ${by + 0.180471 * sy} Z`;
        svg += `<path d="${pathD}" fill="#3a3e44"/>`;
        const inset = `<g transform="translate(${bx + bw / 2} ${by + bh / 2}) scale(0.96) translate(${-(bx + bw / 2)} ${-(by + bh / 2)})">` +
            `<path d="${pathD}" fill="${bodyFill}"/></g>`;
        svg += inset;

        // ── Indicator dots ──
        // dot positions:
        //   top  cx=9.5  cy=7.5
        //   BL   cx=7.5  cy=17.5
        //   BR   cx=17.5 cy=17.5
        // v1 = proceed         → top dim, BL lit, BR lit
        // v2 = diverge right   → top lit, BL dim, BR lit
        // v3 = blank / off     → all dim
        const dotR = Math.max(3.8, 3.8 * Math.min(sx, sy));
        const dotPositions = [
            { cx: bx + 9.5 * sx, cy: by + 7.5 * sy, lit: v === 2 },
            { cx: bx + 7.5 * sx, cy: by + 17.5 * sy, lit: v === 1 },
            { cx: bx + 17.5 * sx, cy: by + 17.5 * sy, lit: v === 1 || v === 2 }
        ];
        for (const d of dotPositions) {
            if (d.lit) {
                svg += `<circle${blink} cx="${d.cx}" cy="${d.cy}" r="${dotR + 1.8}" fill="white" opacity="0.18"/>`;
                svg += `<circle${blink} cx="${d.cx}" cy="${d.cy}" r="${dotR}" fill="white"/>`;
                //svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR + 1.8}" fill="white" opacity="0.18"/>`;
                //svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR}" fill="white"/>`;
                svg += `<ellipse cx="${d.cx - dotR * 0.35}" cy="${d.cy - dotR * 0.4}" rx="${dotR * 0.35}" ry="${dotR * 0.22}" fill="white" opacity="0.6"/>`;
            } else {
                svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR}" fill="white" fill-opacity="0.08" stroke="white" stroke-opacity="0.25" stroke-width="0.6"/>`;
            }
        }

        // Stand — drawn from cell.attrs.signal props, same model as the
        // composite signal so all stand-bearing elements behave consistently.
        // Falls back to the old auto-stick only when no signal props are set
        // (preserves legacy behaviour for data saved before this change).
        const sigProps = (cell.attrs && cell.attrs.signal) || null;

        if (labelTxt) {
            const lFill = (labelAttrs.fill && labelAttrs.fill !== 'transparent')
                ? labelAttrs.fill
                : 'rgba(220,228,245,0.85)';
            const sigSide = (cell.attrs && cell.attrs.signal && cell.attrs.signal.signalSide) || 'up';
            const isRight = (sigSide === 'up' || sigSide === 'right');
            // Wider default gap so the SHxx label clears the triangle body /
            // stand. A user-set labelGap still overrides this.
            const lblGap = Math.max(4, +(sigProps && sigProps.labelGap) || 12);
            const lx = isRight ? bx + bw + lblGap : bx - lblGap;
            const anchor = isRight ? 'start' : 'end';
            const ly = by + bh / 2 + 5;
            svg += `<text x="${lx}" y="${ly}" fill="#0a0f1e" stroke="#0a0f1e" stroke-width="3" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="700" ` +
                `text-anchor="${anchor}" dominant-baseline="middle" paint-order="stroke">${escapeXml(labelTxt)}</text>`;
            svg += `<text x="${lx}" y="${ly}" fill="${lFill}" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="700" ` +
                `text-anchor="${anchor}" dominant-baseline="middle">${escapeXml(labelTxt)}</text>`;
        }
        if (sigProps) {
            const sPos = resolveStandPos(sigProps);
            const sLen = Math.max(8, +sigProps.standLength || 20);
            svg += renderStand8({ x: bx, y: by, w: bw, h: bh }, sPos, sLen, 4, sigProps);
        } else {
            // Legacy path: rail-snapping auto-stick for old saved shunts.
            svg += renderStick({ x: bx, y: by, w: bw, h: bh }, 'right');
        }

        svg += `</g>`;
        return svg;
    }

    /* --- Combined Signal + Shunt (S/SH44, S/C46 style) ---------------------- *
     *  3-aspect signal head (R/Y/G in one body) + shunt triangle right next
     *  to it, sharing one L-stick to the rail. Designed for ONE saved cell —
     *  no fragile multi-cell grouping logic needed.
     *
     *  The 3 lamp colours light up based on attrs.lit (e.g. 'R', 'Y', 'G',
     *  'X', or '' for all-off). Default = all three drawn unlit with marker
     *  strokes so the cell reads as "S/SH this is a combined signal" even
     *  when no telemetry has arrived yet.
     *
     *  Shunt variant is fixed at v1 (top dim, BL+BR lit) — same as the
     *  standalone Shaunt — because that's the most common combined pattern.
     * ------------------------------------------------------------------------ */
    function normaliseCombinedShuntSide(value) {
        const side = String(value || 'right').toLowerCase().trim();
        return /^(left|right|top|bottom|none)$/.test(side) ? side : 'right';
    }

    function normaliseCombinedShuntState(value) {
        let raw = String(value == null || value === '' ? 'PROCEED' : value).toUpperCase().trim();
        raw = raw.replace(/[\s\-]+/g, '_');

        const alias = {
            '0': 'OFF', '3': 'OFF', 'FALSE': 'OFF', 'DROP': 'OFF', 'DOWN': 'OFF', 'BLANK': 'OFF', 'DARK': 'OFF',
            '1': 'PROCEED', 'TRUE': 'PROCEED', 'PICKUP': 'PROCEED', 'P': 'PROCEED', 'ON': 'PROCEED',
            'BOTH': 'PROCEED', 'BOTTOM': 'PROCEED', 'BOTTOM_BOTH': 'PROCEED', 'BOTH_BOTTOM': 'PROCEED',
            'BLBR': 'PROCEED', 'BRBL': 'PROCEED', 'BL_BR': 'PROCEED', 'BR_BL': 'PROCEED',
            '2': 'DIVERGE_RIGHT', 'DIVERGE': 'DIVERGE_RIGHT', 'DIVERGE_R': 'DIVERGE_RIGHT', 'DR': 'DIVERGE_RIGHT',
            'RIGHT_DIVERGE': 'DIVERGE_RIGHT', 'TOP_RIGHT': 'DIVERGE_RIGHT', 'TOP_BR': 'DIVERGE_RIGHT',
            'TBR': 'DIVERGE_RIGHT', 'T_BR': 'DIVERGE_RIGHT', 'BR_TOP': 'DIVERGE_RIGHT',
            'DIVERGE_L': 'DIVERGE_LEFT', 'DL': 'DIVERGE_LEFT', 'LEFT_DIVERGE': 'DIVERGE_LEFT',
            'TOP_LEFT': 'DIVERGE_LEFT', 'TOP_BL': 'DIVERGE_LEFT', 'TBL': 'DIVERGE_LEFT', 'T_BL': 'DIVERGE_LEFT', 'BL_TOP': 'DIVERGE_LEFT',
            'T': 'TOP', 'TOP_ONLY': 'TOP',
            'L': 'BL', 'LEFT': 'BL', 'BOTTOM_LEFT': 'BL', 'BL_ONLY': 'BL',
            'R': 'BR', 'RIGHT': 'BR', 'BOTTOM_RIGHT': 'BR', 'BR_ONLY': 'BR',
            'FULL': 'ALL', 'ALL_ON': 'ALL'
        };
        raw = alias[raw] || raw;

        const set = { top: false, bl: false, br: false, code: raw };
        function applyToken(token) {
            token = alias[token] || token;
            switch (token) {
                case 'OFF': break;
                case 'PROCEED': set.bl = true; set.br = true; break;
                case 'DIVERGE_RIGHT': set.top = true; set.br = true; break;
                case 'DIVERGE_LEFT': set.top = true; set.bl = true; break;
                case 'TOP': set.top = true; break;
                case 'BL': set.bl = true; break;
                case 'BR': set.br = true; break;
                case 'ALL': set.top = true; set.bl = true; set.br = true; break;
            }
        }

        const known = ['OFF', 'PROCEED', 'DIVERGE_RIGHT', 'DIVERGE_LEFT', 'TOP', 'BL', 'BR', 'ALL'];
        if (known.indexOf(raw) >= 0) {
            applyToken(raw);
            return set;
        }

        // Custom dot-combination support: T,BL,BR / TOP+BR / BL|BR etc.
        raw.split(/[,+|/;]+/).forEach(function (tok) {
            tok = String(tok || '').toUpperCase().trim();
            if (tok) applyToken(tok);
        });
        return set;
    }

    function renderCombinedShuntSymbol(shX, shY, shW, shH, dotSet, bodyFill) {
        dotSet = dotSet || { top: false, bl: true, br: true };
        bodyFill = bodyFill || '#5B6168';
        const sx = shW / 25;
        const sy = shH / 23;
        const pathD = `M ${shX + 7.91188 * sx} ${shY + 0.180471 * sy} ` +
            `C ${shX + 5.77625 * sx} ${shY + 0.73042 * sy} ${shX + 4.28312 * sx} ${shY + 2.8874 * sy} ${shX + 2.14 * sx} ${shY + 8.52086 * sy} ` +
            `C ${shX + 0.436875 * sx} ${shY + 12.9975 * sy} ${shX + 0.005625 * sx} ${shY + 15.0254 * sy} ${shX + 0.003125 * sx} ${shY + 18.5649 * sy} ` +
            `L ${shX} ${shY + shH} L ${shX + shW / 2} ${shY + shH} L ${shX + shW} ${shY + shH} ` +
            `L ${shX + shW} ${shY + 17.5017 * sy} L ${shX + shW} ${shY + 12.0041 * sy} ` +
            `L ${shX + 21.7913 * sx} ${shY + 9.09038 * sy} ` +
            `C ${shX + 20.0269 * sx} ${shY + 7.48825 * sy} ${shX + 17.2844 * sx} ${shY + 4.90306 * sy} ${shX + 15.6975 * sx} ${shY + 3.34558 * sy} ` +
            `C ${shX + 12.8669 * sx} ${shY + 0.567087 * sy} ${shX + 10.3638 * sx} ${shY - 0.450839 * sy} ${shX + 7.91188 * sx} ${shY + 0.180471 * sy} Z`;
        let svg = '';
        svg += `<path d="${pathD}" fill="#3a3e44"/>`;
        svg += `<g transform="translate(${shX + shW / 2} ${shY + shH / 2}) scale(0.96) translate(${-(shX + shW / 2)} ${-(shY + shH / 2)})">` +
            `<path d="${pathD}" fill="${bodyFill}"/></g>`;

        const dotR = Math.max(3.4, 3.4 * Math.min(sx, sy));
        const dots = [
            { cx: shX + 9.5 * sx, cy: shY + 7.5 * sy, lit: !!dotSet.top },
            { cx: shX + 7.5 * sx, cy: shY + 17.5 * sy, lit: !!dotSet.bl },
            { cx: shX + 17.5 * sx, cy: shY + 17.5 * sy, lit: !!dotSet.br }
        ];
        for (const d of dots) {
            if (d.lit) {
                svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR + 1.5}" fill="white" opacity="0.18"/>`;
                svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR}" fill="white"/>`;
                svg += `<ellipse cx="${d.cx - dotR * 0.35}" cy="${d.cy - dotR * 0.4}" rx="${dotR * 0.35}" ry="${dotR * 0.22}" fill="white" opacity="0.6"/>`;
            } else {
                svg += `<circle cx="${d.cx}" cy="${d.cy}" r="${dotR}" fill="white" fill-opacity="0.08" stroke="white" stroke-opacity="0.25" stroke-width="0.6"/>`;
            }
        }
        return svg;
    }

    function renderSignalShunt(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 200;
        const h = cell.size.height || 80;

        const sigProps = (cell.attrs && cell.attrs.signal) || {};
        const lampsStr = (typeof sigProps.lamps === 'string' && sigProps.lamps.length) ? sigProps.lamps : 'RYG';
        const lampsClean = String(lampsStr).toUpperCase().replace(/[^BRYGX]/g, '') || 'B';
        const lamps = lampsClean.split('');
        const n = Math.max(1, lamps.length);
        const litAttr = String((sigProps.lit != null ? sigProps.lit : ((cell.attrs && cell.attrs.lit) || '')) || '').toUpperCase();
        const litSet = {};
        for (let i = 0; i < litAttr.length; i++) litSet[litAttr[i]] = 1;

        const labelAttrs = (cell.attrs && cell.attrs.label) || {};
        const labelTxt = labelAttrs.text != null ? String(labelAttrs.text) : '';
        const labelFill = (labelAttrs.fill && labelAttrs.fill !== 'transparent')
            ? labelAttrs.fill : 'rgba(220,228,245,0.85)';
        const labelSize = +labelAttrs.fontSize || 14;

        /* Main + shunt are now fully combinable:
           - main signal supports any 1..8 lamp code through attrs.signal.lamps
           - shunt can be placed left/right/top/bottom/none
           - shunt dot state supports every practical 3-dot combination through
             attrs.signal.shuntState: OFF, PROCEED, DIVERGE_RIGHT,
             DIVERGE_LEFT, TOP, BL, BR, ALL, or a custom T,BL,BR code. */
        const sigH = Math.max(24, Math.min(42, h * 0.46));
        const lampD = Math.max(12, sigH - 10);
        const lampGap = Math.max(4, +sigProps.lampGap || 7);
        const sigW = Math.max(34, n * lampD + (n + 1) * lampGap);
        const shuntSide = normaliseCombinedShuntSide(sigProps.shuntSide);
        const hasShunt = shuntSide !== 'none';
        const defaultShW = Math.max(36, Math.min(50, sigH * 1.22));
        const shW = Math.max(24, Math.min(110, +sigProps.shuntSize || defaultShW));
        const shH = shW * (23 / 25);
        const shuntGap = Math.max(0, +sigProps.shuntGap || 2);
        const maxH = Math.max(sigH, shH);

        let sigX, sigY, shX, shY, totalW, totalH, contentX, contentY;
        if (!hasShunt) {
            totalW = sigW; totalH = sigH;
            contentX = x + (w - totalW) / 2;
            contentY = y + (h - totalH) / 2;
            sigX = contentX; sigY = contentY;
        } else if (shuntSide === 'left' || shuntSide === 'right') {
            totalW = sigW + shuntGap + shW;
            totalH = maxH;
            contentX = x + (w - totalW) / 2;
            contentY = y + (h - totalH) / 2;
            if (shuntSide === 'left') {
                shX = contentX;
                sigX = contentX + shW + shuntGap;
            } else {
                sigX = contentX;
                shX = contentX + sigW + shuntGap;
            }
            sigY = contentY + (totalH - sigH) / 2;
            shY = contentY + (totalH - shH) / 2;
        } else {
            totalW = Math.max(sigW, shW);
            totalH = sigH + shuntGap + shH;
            contentX = x + (w - totalW) / 2;
            contentY = y + (h - totalH) / 2;
            if (shuntSide === 'top') {
                shY = contentY;
                sigY = contentY + shH + shuntGap;
            } else {
                sigY = contentY;
                shY = contentY + sigH + shuntGap;
            }
            sigX = contentX + (totalW - sigW) / 2;
            shX = contentX + (totalW - shW) / 2;
        }

        let svg = `<g class="sip-asset sip-sigshunt" data-id="${cell.id}" data-type="${cell.type}">`;

        // ── Variable-aspect main signal head ──
        svg += `<rect x="${sigX}" y="${sigY}" width="${sigW}" height="${sigH}" rx="5" fill="#3a3a3a"/>`;
        svg += `<rect x="${sigX + 1}" y="${sigY + 1}" width="${sigW - 2}" height="${sigH - 2}" rx="4" fill="#606060"/>`;
        const litMap = { R: '#FF2E2E', Y: '#FFD400', G: '#22D142', X: '#FFD400' };
        const r = lampD / 2;
        for (let i = 0; i < n; i++) {
            const kind = lamps[i] || 'B';
            const cx = sigX + lampGap * (i + 1) + lampD * i + r;
            const cy = sigY + sigH / 2;
            const isLitLamp = !!litSet[kind] || (kind === 'Y' && !!litSet.X);
            if (isLitLamp && litMap[kind]) {
                const col = litMap[kind] || '#FFD400';
                svg += `<circle cx="${cx}" cy="${cy}" r="${r + 4}" fill="${col}" opacity="0.18"/>`;
                svg += `<circle cx="${cx}" cy="${cy}" r="${r + 2}" fill="${col}" opacity="0.35"/>`;
                svg += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="${col}"/>`;
                svg += `<ellipse cx="${cx - r * 0.35}" cy="${cy - r * 0.4}" rx="${r * 0.35}" ry="${r * 0.22}" fill="white" opacity="0.45"/>`;
            } else {
                svg += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="1.2"/>`;
                if (kind === 'R') {
                    svg += `<line x1="${cx - r + 1}" y1="${cy - 1.4}" x2="${cx + r - 1}" y2="${cy - 1.4}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                    svg += `<line x1="${cx - r + 1}" y1="${cy + 1.4}" x2="${cx + r - 1}" y2="${cy + 1.4}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'Y') {
                    const d = r * 0.72;
                    svg += `<line x1="${cx - d}" y1="${cy - d}" x2="${cx + d}" y2="${cy + d}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'G') {
                    svg += `<line x1="${cx}" y1="${cy - r + 1}" x2="${cx}" y2="${cy + r - 1}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                } else if (kind === 'X') {
                    const d = r * 0.72;
                    svg += `<line x1="${cx - d}" y1="${cy - d + 2.2}" x2="${cx + d}" y2="${cy + d + 2.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                    svg += `<line x1="${cx - d}" y1="${cy - d - 2.2}" x2="${cx + d}" y2="${cy + d - 2.2}" stroke="#3a3a3a" stroke-width="1.4"/>`;
                }
            }
        }

        // ── Shunt triangle with every 3-dot combination ──
        if (hasShunt) {
            const shuntState = sigProps.shuntState || sigProps.shuntVariant || sigProps.shuntLit || (cell.attrs && (cell.attrs.shuntState || cell.attrs.shuntLit)) || 'PROCEED';
            const dotSet = normaliseCombinedShuntState(shuntState);
            svg += renderCombinedShuntSymbol(shX, shY, shW, shH, dotSet, '#5B6168');
        }

        // ── Centre-attached L stand for combined signal + shunt ──
        const standHeadBox = { x: contentX, y: contentY, w: totalW, h: totalH };
        const sPos = resolveStandPos(sigProps);
        const sLen = Math.max(8, +sigProps.standLength || 30);
        svg += renderStand8(standHeadBox, sPos, sLen, 4, sigProps);

        // ── Label ──
        if (labelTxt) {
            const labelGap = Math.max(4, +sigProps.labelGap || 6);
            const lp = labelSideFromStand(sPos, standHeadBox.x, standHeadBox.y, standHeadBox.w, standHeadBox.h, labelSize, labelGap);
            svg += `<text x="${lp.lx}" y="${lp.ly}" fill="#0a0f1e" stroke="#0a0f1e" stroke-width="3" ` +
                `font-family="${T.labelFont}" font-size="${labelSize}" font-weight="700" ` +
                `text-anchor="${lp.anchor}" dominant-baseline="central" paint-order="stroke">${escapeXml(labelTxt)}</text>`;
            svg += `<text x="${lp.lx}" y="${lp.ly}" fill="${labelFill}" ` +
                `font-family="${T.labelFont}" font-size="${labelSize}" font-weight="700" ` +
                `text-anchor="${lp.anchor}" dominant-baseline="central">${escapeXml(labelTxt)}</text>`;
        }

        svg += `</g>`;
        return svg;
    }



    /* --- Route / Calling signal module -------------------------------------- *
     *  Compact SIP route indicator model based on the telemetry live signal
     *  structure: route arms are named AUG/BUG/CUG/DUG/EUG and calling is the
     *  independent Co_Hg/C lamp.  This renderer is deliberately self-contained
     *  so it can be dropped as a standalone asset, or enabled inside the main
     *  composite signal without changing the saved legacy JSON shape.
     * ------------------------------------------------------------------------ */
    const ROUTE_LABEL_POOL = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG', 'FUG', 'HUG', 'JUG'];

    function parseRouteLabels(value) {
        if (Array.isArray(value)) value = value.join(',');
        value = String(value == null ? '' : value).trim();
        if (!value) value = 'AUG,BUG,CUG,DUG';
        const seen = {};
        return value.split(/[\s,|/]+/).map(function (x) {
            return String(x || '').trim().toUpperCase();
        }).filter(function (x) {
            if (!x || seen[x]) return false;
            seen[x] = true;
            return true;
        });
    }

    function clampRouteNumber(v, fallback, min, max) {
        const n = parseFloat(v);
        if (isNaN(n)) return fallback;
        return Math.max(min, Math.min(max, n));
    }

    function boolRouteProp(v, fallback) {
        if (v === undefined || v === null || v === '') return fallback;
        if (typeof v === 'boolean') return v;
        const s = String(v).toLowerCase().trim();
        return !(s === 'false' || s === '0' || s === 'no' || s === 'off');
    }

    function parseRouteActiveSet(value) {
        const out = {};
        String(value == null ? '' : value).toUpperCase().split(/[\s,|/]+/).forEach(function (x) {
            x = x.trim();
            if (x) out[x] = true;
        });
        return out;
    }

    function routeOppositeSide(side) {
        switch (side) {
            case 'top': return 'bottom';
            case 'bottom': return 'top';
            case 'left': return 'right';
            case 'right': return 'left';
            default: return 'bottom';
        }
    }

    function routeSideUnit(side) {
        switch (side) {
            case 'bottom': return { ax: 0, ay: 1, px: 1, py: 0, labelDx: 0, labelDy: 14, anchor: 'middle' };
            case 'left': return { ax: -1, ay: 0, px: 0, py: 1, labelDx: -10, labelDy: 4, anchor: 'end' };
            case 'right': return { ax: 1, ay: 0, px: 0, py: 1, labelDx: 10, labelDy: 4, anchor: 'start' };
            case 'top':
            default: return { ax: 0, ay: -1, px: 1, py: 0, labelDx: 0, labelDy: -9, anchor: 'middle' };
        }
    }

    function normaliseRouteCallingConfig(cell, forceEnabled) {
        const route = (cell.attrs && cell.attrs.route) || {};
        const labels = parseRouteLabels(route.labels || route.routes || route.routeLabels);
        const side = String(route.routeSide || 'top').toLowerCase();
        const safeSide = /^(top|bottom|left|right)$/.test(side) ? side : 'top';
        let callingSide = String(route.callingSide || 'bottom').toLowerCase();
        if (callingSide === 'opposite') callingSide = routeOppositeSide(safeSide);
        if (!/^(top|bottom|left|right)$/.test(callingSide)) callingSide = 'bottom';
        return {
            enabled: forceEnabled || boolRouteProp(route.enabled, false),
            labels: labels,
            routeSide: safeSide,
            callingSide: callingSide,
            calling: boolRouteProp(route.calling, true),
            callingLabel: String(route.callingLabel || 'C'),
            active: String(route.active || route.activeRoutes || ''),
            dotCount: 1, // legacy field retained, simple route uses exactly one light per aspect
            dotSize: clampRouteNumber(route.dotSize || route.lightSize, 3.8, 2.4, 10),
            labelSize: clampRouteNumber(route.labelSize, 8.5, 6, 18),
            labelGap: clampRouteNumber(route.labelGap, 8, 0, 50),
            armSpacing: clampRouteNumber(route.armSpacing, 22, 10, 90),
            armLength: clampRouteNumber(route.armLength, 22, 8, 120),
            attachGap: clampRouteNumber(route.attachGap, 6, 0, 100),
            callingGap: clampRouteNumber(route.callingGap, route.attachGap || 6, 0, 100),
            compact: boolRouteProp(route.compact, true)
        };
    }

    function routeEdgePoint(box, sideName, lateral) {
        const side = routeSideUnit(sideName);
        const x = box.x, y = box.y, w = box.w, h = box.h;
        let sx, sy;
        switch (sideName) {
            case 'bottom': sx = x + w / 2; sy = y + h; break;
            case 'left': sx = x; sy = y + h / 2; break;
            case 'right': sx = x + w; sy = y + h / 2; break;
            case 'top':
            default: sx = x + w / 2; sy = y; break;
        }
        sx += side.px * lateral;
        sy += side.py * lateral;
        return { sx: sx, sy: sy, ax: side.ax, ay: side.ay, px: side.px, py: side.py };
    }

    function routeLabelPoint(sideName, x, y, gap) {
        gap = Math.max(0, +gap || 0);
        switch (sideName) {
            case 'bottom': return { x: x, y: y + gap, anchor: 'middle' };
            case 'left': return { x: x - gap, y: y + 4, anchor: 'end' };
            case 'right': return { x: x + gap, y: y + 4, anchor: 'start' };
            case 'top':
            default: return { x: x, y: y - gap, anchor: 'middle' };
        }
    }

    function renderSimpleRouteLamp(cx, cy, r, lit, colour) {
        colour = colour || '#ffffff';
        if (lit) {
            return `<circle cx="${cx}" cy="${cy}" r="${r + 3}" fill="${colour}" opacity="0.18">` +
                `<animate attributeName="opacity" values="0.18;0.03;0.18" dur="0.8s" repeatCount="indefinite"/>` +
                `</circle>` +
                `<circle cx="${cx}" cy="${cy}" r="${r + 1.3}" fill="${colour}" opacity="0.45">` +
                `<animate attributeName="opacity" values="0.45;0.12;0.45" dur="0.8s" repeatCount="indefinite"/>` +
                `</circle>` +
                `<circle cx="${cx}" cy="${cy}" r="${r}" fill="${colour}" stroke="${colour}" stroke-width="0.8">` +
                `<animate attributeName="opacity" values="1;0.25;1" dur="0.8s" repeatCount="indefinite"/>` +
                `</circle>`;
        }
        return `<circle cx="${cx}" cy="${cy}" r="${r}" fill="#0a0f1e" ` +
            `stroke="${colour}" stroke-opacity="0.68" stroke-width="0.9"/>` +
            `<circle cx="${cx}" cy="${cy}" r="${Math.max(1.3, r * 0.34)}" fill="${colour}" fill-opacity="0.16"/>`;
    }

    function renderRouteArmsOnBox(box, cfg, sideName, origin) {
        const labels = cfg.labels.length ? cfg.labels : ['AUG'];
        const n = labels.length;
        const activeSet = parseRouteActiveSet(cfg.active);
        const allRoutesActive = !!(activeSet.ALL || activeSet.ROUTE || activeSet.ROUTES || activeSet['*']);
        sideName = /^(top|bottom|left|right)$/.test(sideName) ? sideName : 'top';

        const side = routeSideUnit(sideName);
        const mid = (n - 1) / 2;
        const spacing = Math.max(10, +cfg.armSpacing || 22);
        const lineLen = Math.max(8, +cfg.armLength || 22);
        const attachGap = Math.max(0, +cfg.attachGap || 6);
        const dotR = Math.max(2.4, +cfg.dotSize || 3.8);
        const labelSize = Math.max(6, +cfg.labelSize || 8.5);
        const labelGap = Math.max(0, +cfg.labelGap || 8);
        const baseCol = '#9aa8c4';
        let svg = `<g class="sip-route-top-arms" pointer-events="none">`;

        /* Railway SIP-style route indicator:
           no centre hub, no fan cluster, no route box. Each route aspect is a
           small independent arm above the main signal: line + single lamp + label.
           When origin is supplied (attached-to-signal mode), each arm line starts
           from the centre of the main signal body instead of the box edge. */
        for (let i = 0; i < n; i++) {
            const lbl = labels[i];
            const lit = allRoutesActive || !!activeSet[lbl];
            const lateral = (i - mid) * spacing;
            const p = routeEdgePoint(box, sideName, lateral);
            const jx = p.sx + p.ax * attachGap;
            const jy = p.sy + p.ay * attachGap;
            const ex = p.sx + p.ax * (attachGap + lineLen);
            const ey = p.sy + p.ay * (attachGap + lineLen);
            const stroke = lit ? '#ffffff' : baseCol;

            /* origin override: route lines start from signal centre when attached */
            const startX = origin ? origin.x : p.sx;
            const startY = origin ? origin.y : p.sy;

            svg += `<line x1="${startX}" y1="${startY}" x2="${ex}" y2="${ey}" ` +
                `stroke="${stroke}" stroke-width="1.45" stroke-linecap="round" opacity="${lit ? '1' : '0.76'}">` +
                (lit ? `<animate attributeName="opacity" values="1;0.28;1" dur="0.8s" repeatCount="indefinite"/>` : '') +
                `</line>`;
            svg += renderSimpleRouteLamp(ex, ey, dotR, lit, '#ffffff');

            const lp = routeLabelPoint(sideName, ex, ey, dotR + labelGap);
            svg += `<text x="${lp.x}" y="${lp.y}" fill="${lit ? '#ffffff' : '#d7d7d7'}" ` +
                `font-family="${T.labelFont}" font-size="${labelSize}" font-weight="800" ` +
                `text-anchor="${lp.anchor}" dominant-baseline="central">${escapeXml(lbl)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    function renderSimpleCallingLineOnBox(box, cfg, sideName, origin) {
        sideName = /^(top|bottom|left|right)$/.test(sideName) ? sideName : 'bottom';
        const side = routeSideUnit(sideName);
        const callGap = Math.max(0, +cfg.callingGap || +cfg.attachGap || 6);
        const callLen = Math.max(12, (+cfg.callingLength || Math.max(18, (+cfg.armLength || 22) * 0.82)));
        const start = routeEdgePoint(box, sideName, 0);
        const endX = start.sx + side.ax * (callGap + callLen);
        const endY = start.sy + side.ay * (callGap + callLen);
        /* origin override: calling line starts from signal centre when attached */
        const lineStartX = origin ? origin.x : start.sx;
        const lineStartY = origin ? origin.y : start.sy;
        let svg = `<g class="sip-calling-below-line" pointer-events="none">`;
        svg += `<line x1="${lineStartX}" y1="${lineStartY}" x2="${endX}" y2="${endY}" ` +
            `stroke="#9aa8c4" stroke-width="1.25" stroke-linecap="round" opacity="0.72"/>`;
        svg += renderCallingAttachment(endX, endY, cfg, sideName);
        svg += `</g>`;
        return svg;
    }

    function renderRouteCallingGraphic(cx, cy, cfg, opts) {
        opts = opts || {};
        /* Standalone Route/Calling stencil uses the same simple SIP model as the
           main-signal attachment. A small notional centre line is used only for
           geometry; nothing fan-shaped or multi-dot is drawn. */
        const labels = cfg.labels.length ? cfg.labels : ['AUG'];
        const span = Math.max(54, (labels.length - 1) * Math.max(10, +cfg.armSpacing || 22) + 24);
        const box = { x: cx - span / 2, y: cy - 3, w: span, h: 6 };
        let svg = `<g class="sip-route-call-module sip-route-simple sip-route-railway-style">`;
        svg += renderRouteArmsOnBox(box, cfg, cfg.routeSide || 'top');
        if (cfg.calling && opts.includeCalling !== false) {
            svg += renderSimpleCallingLineOnBox(box, cfg, cfg.callingSide || 'bottom');
        }
        svg += `</g>`;
        return svg;
    }

    function renderCallingAttachment(cx, cy, cfg, sideName) {
        const activeSet = parseRouteActiveSet(cfg.active);
        const callLit = !!(activeSet.C || activeSet.CALL || activeSet.CALLING || activeSet.COHG || activeSet.CO_HG);
        const r = Math.max(5.2, (+cfg.dotSize || 3.8) + 1.6);
        const label = String(cfg.callingLabel || 'C').toUpperCase();
        let svg = `<g class="sip-calling-simple">`;
        if (callLit) {
            svg += `<circle cx="${cx}" cy="${cy}" r="${r + 3.5}" fill="#ffffff" opacity="0.18">` +
                `<animate attributeName="opacity" values="0.18;0.03;0.18" dur="0.8s" repeatCount="indefinite"/>` +
                `</circle>`;
            svg += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="#ffffff" stroke="#ffffff" stroke-width="1.1">` +
                `<animate attributeName="opacity" values="1;0.25;1" dur="0.8s" repeatCount="indefinite"/>` +
                `</circle>`;
            svg += `<text x="${cx}" y="${cy}" fill="#0a0f1e" font-family="${T.labelFont}" ` +
                `font-size="${Math.max(8, r * 1.15)}" font-weight="900" text-anchor="middle" dominant-baseline="central">${escapeXml(label)}</text>`;
        } else {
            svg += `<circle cx="${cx}" cy="${cy}" r="${r}" fill="#0a0f1e" stroke="#ffffff" stroke-opacity="0.78" stroke-width="1.1"/>`;
            svg += `<text x="${cx}" y="${cy}" fill="#ffffff" fill-opacity="0.88" font-family="${T.labelFont}" ` +
                `font-size="${Math.max(8, r * 1.15)}" font-weight="900" text-anchor="middle" dominant-baseline="central">${escapeXml(label)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    function renderAttachedRouteCallingToSignal(box, cfg) {
        /* ------------------------------------------------------------------ *
         *  Radial fan layout — reference: live telemetry signal style.       *
         *  Route arms radiate from the centre of the main signal body like   *
         *  spokes of a wheel, fanning out toward the configured route side.  *
         *  Calling drops straight down from centre, always below the signal. *
         * ------------------------------------------------------------------ */
        const cx = box.x + box.w / 2;
        const cy = box.y + box.h / 2;

        const labels = cfg.labels.length ? cfg.labels : ['AUG'];
        const n = labels.length;
        const activeSet = parseRouteActiveSet(cfg.active);
        const allRoutesActive = !!(activeSet.ALL || activeSet.ROUTE || activeSet.ROUTES || activeSet['*']);

        const armLen = Math.max(8, +cfg.armLength || 22);
        const dotR = Math.max(2.4, +cfg.dotSize || 3.8);
        const labelSz = Math.max(6, +cfg.labelSize || 8.5);
        const labelGap = Math.max(0, +cfg.labelGap || 8);
        const baseCol = '#9aa8c4';

        /* Base angle: direction the fan is centred on (degrees, 0 = right) */
        const routeSide = /^(top|bottom|left|right)$/.test(cfg.routeSide) ? cfg.routeSide : 'top';
        let baseDeg;
        switch (routeSide) {
            case 'bottom': baseDeg = 90; break;
            case 'left': baseDeg = 180; break;
            case 'right': baseDeg = 0; break;
            case 'top':
            default: baseDeg = -90; break;
        }

        /* Fan spread — scales with arm count, capped at 150° */
        const totalSpread = n > 1 ? Math.min(150, (n - 1) * 35) : 0;

        let svg = `<g class="sip-route-call-attached sip-route-call-simple sip-route-main-attached">`;

        /* ── Route arms (radial fan from signal centre) ── */
        for (let i = 0; i < n; i++) {
            const lbl = labels[i];
            const lit = allRoutesActive || !!activeSet[lbl];

            /* Angle for this arm — evenly spaced within the fan arc */
            const offset = n > 1 ? ((i / (n - 1)) - 0.5) * totalSpread : 0;
            const angleDeg = baseDeg + offset;
            const angleRad = angleDeg * Math.PI / 180;

            /* Arm endpoint */
            const ex = cx + Math.cos(angleRad) * armLen;
            const ey = cy + Math.sin(angleRad) * armLen;
            const stroke = lit ? '#ffffff' : baseCol;

            /* Arm line from signal centre → endpoint */
            svg += `<line x1="${cx}" y1="${cy}" x2="${ex}" y2="${ey}" ` +
                `stroke="${stroke}" stroke-width="1.45" stroke-linecap="round" opacity="${lit ? '1' : '0.76'}">` +
                (lit ? `<animate attributeName="opacity" values="1;0.28;1" dur="0.8s" repeatCount="indefinite"/>` : '') +
                `</line>`;

            /* Lamp dot at the tip */
            svg += renderSimpleRouteLamp(ex, ey, dotR, lit, '#ffffff');

            /* Label beyond the dot, pushed outward along the same angle */
            const lx = ex + Math.cos(angleRad) * (dotR + labelGap);
            const ly = ey + Math.sin(angleRad) * (dotR + labelGap);

            /* Text anchor depends on which direction the arm points */
            const cosA = Math.cos(angleRad);
            let anchor;
            if (cosA > 0.3) anchor = 'start';   /* arm points right */
            else if (cosA < -0.3) anchor = 'end';      /* arm points left  */
            else anchor = 'middle';   /* arm points up/down */

            svg += `<text x="${lx}" y="${ly}" fill="${lit ? '#ffffff' : '#d7d7d7'}" ` +
                `font-family="${T.labelFont}" font-size="${labelSz}" font-weight="800" ` +
                `text-anchor="${anchor}" dominant-baseline="central">${escapeXml(lbl)}</text>`;
        }

        /* ── Calling — straight down from centre, always below the signal ── */
        if (cfg.calling) {
            const callGap = Math.max(0, +cfg.callingGap || +cfg.attachGap || 6);
            const callLen = Math.max(12, (+cfg.callingLength || Math.max(18, armLen * 0.82)));
            /* End Y starts from the bottom edge of the signal + gap + length */
            const callEndY = box.y + box.h + callGap + callLen;

            svg += `<line x1="${cx}" y1="${cy}" x2="${cx}" y2="${callEndY}" ` +
                `stroke="#9aa8c4" stroke-width="1.25" stroke-linecap="round" opacity="0.72"/>`;
            svg += renderCallingAttachment(cx, callEndY, cfg, 'bottom');
        }

        svg += `</g>`;
        return svg;
    }

    function renderRouteCallingSignal(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 150;
        const h = cell.size.height || 95;
        const cx = x + w / 2;
        const cy = y + h / 2;
        const cfg = normaliseRouteCallingConfig(cell, true);
        const sigProps = (cell.attrs && cell.attrs.signal) || {};
        const standPos = resolveStandPos(sigProps);
        const standLen = Math.max(8, +sigProps.standLength || 24);
        const labelAttrs = (cell.attrs && cell.attrs.label) || {};
        const labelTxt = labelAttrs.text != null ? String(labelAttrs.text) : '';

        let svg = `<g class="sip-asset sip-route-calling sip-route-calling-simple-asset" data-id="${cell.id}" data-type="${cell.type}">`;
        // Transparent hit area keeps the asset easy to select without drawing a big box.
        svg += `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="transparent"/>`;
        svg += renderStand8({ x: x, y: y, w: w, h: h }, standPos, standLen, 4, sigProps);
        svg += renderRouteCallingGraphic(cx, cy, cfg, { includeCalling: true });

        if (labelTxt && labelAttrs.fill !== 'transparent') {
            const fill = labelAttrs.fill || T.labelDefault;
            const size = +labelAttrs.fontSize || 12;
            svg += `<text x="${x + w / 2}" y="${y + h + size + 4}" fill="${fill}" ` +
                `font-family="${T.labelFont}" font-size="${size}" font-weight="800" ` +
                `text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Axle Counter ------------------------------------------------------- */
    function renderAxleCounter(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 100;
        const labelTxt = attrLabelText(cell);

        const cx1 = x + w * 0.30, cy1 = y + h * 0.30;
        const cx2 = x + w * 0.70, cy2 = y + h * 0.70;
        const r = Math.max(5, Math.min(w, h) * 0.10);

        let svg = `<g class="sip-asset sip-ax" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<line x1="${cx1}" y1="${cy1}" x2="${cx2}" y2="${cy2}" ` +
            `stroke="#8aa3c0" stroke-width="3" stroke-linecap="round"/>`;
        svg += `<line x1="${cx1}" y1="${cy1}" x2="${cx2}" y2="${cy2}" ` +
            `stroke="white" stroke-width="1" stroke-linecap="round" opacity="0.3"/>`;
        svg += `<circle cx="${cx1}" cy="${cy1}" r="${r + 1}" fill="#0a0f1e" stroke="#22d3ee" stroke-width="1.5"/>`;
        svg += `<circle cx="${cx1}" cy="${cy1}" r="${r - 2}" fill="#f4f4f4"/>`;
        svg += `<circle cx="${cx2}" cy="${cy2}" r="${r + 1}" fill="#0a0f1e" stroke="#22d3ee" stroke-width="1.5"/>`;
        svg += `<circle cx="${cx2}" cy="${cy2}" r="${r - 2}" fill="#f4f4f4"/>`;
        if (labelTxt) {
            const ly = cy2 + r + 18;
            svg += `<text x="${cx2}" y="${ly}" fill="#0a0f1e" stroke="#0a0f1e" stroke-width="3" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="700" ` +
                `text-anchor="middle" paint-order="stroke">${escapeXml(labelTxt)}</text>`;
            svg += `<text x="${cx2}" y="${ly}" fill="${T.labelDefault}" ` +
                `font-family="${T.labelFont}" font-size="14" font-weight="700" ` +
                `text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Gate --------------------------------------------------------------- */
    function renderGate(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 100;
        const labelTxt = attrLabelText(cell);
        const cx = x + w / 2;

        let svg = `<g class="sip-asset sip-gate" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<line x1="${cx - 6}" y1="${y + 6}" x2="${cx - 6}" y2="${y + h - 6}" stroke="${T.signalBody}" stroke-width="2"/>`;
        svg += `<line x1="${cx + 6}" y1="${y + 6}" x2="${cx + 6}" y2="${y + h - 6}" stroke="${T.signalBody}" stroke-width="2"/>`;
        for (let i = 0; i < 4; i++) {
            const yy = y + 12 + i * (h - 24) / 3;
            svg += `<line x1="${cx - 18}" y1="${yy}" x2="${cx + 18}" y2="${yy}" stroke="${T.signalBody}" stroke-width="1.5"/>`;
        }
        if (labelTxt) {
            svg += `<text x="${cx}" y="${y + h + 14}" fill="${T.labelDefault}" ` +
                `font-family="${T.labelFont}" font-size="12" font-weight="700" text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Bus Bar ------------------------------------------------------------ */
    function renderBusBar(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 25;
        const labelTxt = attrLabelText(cell);
        const fill = attrLabel(cell).fill || T.bbLabel;

        let svg = `<g class="sip-asset sip-bb" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<rect x="${x}" y="${y + h / 2 - 4}" width="${Math.max(40, w)}" height="8" rx="2" fill="${T.busBar}"/>`;
        if (labelTxt) {
            svg += `<text x="${x + Math.max(40, w) / 2}" y="${y + h / 2 + 22}" fill="${fill}" ` +
                `font-family="${T.labelFont}" font-size="12" font-weight="800" text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Arrow -------------------------------------------------------------- */
    function renderArrow(cell, dir) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 100;
        const h = cell.size.height || 100;
        const cy = y + h / 2;
        const labelTxt = attrLabelText(cell);

        const pts = (dir === 'right')
            ? `${x + 4},${cy - 4} ${x + w - 14},${cy - 4} ${x + w - 14},${cy - 10} ${x + w - 4},${cy} ${x + w - 14},${cy + 10} ${x + w - 14},${cy + 4} ${x + 4},${cy + 4}`
            : `${x + w - 4},${cy - 4} ${x + 14},${cy - 4} ${x + 14},${cy - 10} ${x + 4},${cy} ${x + 14},${cy + 10} ${x + 14},${cy + 4} ${x + w - 4},${cy + 4}`;
        let svg = `<g class="sip-asset sip-arrow" data-id="${cell.id}" data-type="${cell.type}">`;
        svg += `<polygon points="${pts}" fill="${T.signalBody}"/>`;
        if (labelTxt) {
            svg += `<text x="${x + w / 2}" y="${y + h + 14}" fill="${T.labelDefault}" ` +
                `font-family="${T.labelFont}" font-size="12" font-weight="700" text-anchor="middle">${escapeXml(labelTxt)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* --- Top/Bottom route line --------------------------------------------- */
    function renderTopLine(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 37;
        const h = cell.size.height || 49;
        return `<g class="sip-asset sip-line" data-id="${cell.id}" data-type="${cell.type}">` +
            `<path d="M ${x + 2} ${y + h} L ${x + 2} ${y + 4} Q ${x + 2} ${y + 2}, ${x + 4} ${y + 2} L ${x + w} ${y + 2}" ` +
            `stroke="${T.routeLine}" stroke-width="4" fill="none"/>` +
            `</g>`;
    }
    function renderBottomLine(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 37;
        const h = cell.size.height || 56;
        return `<g class="sip-asset sip-line" data-id="${cell.id}" data-type="${cell.type}">` +
            `<path d="M ${x + w - 2} ${y} L ${x + w - 2} ${y + h - 4} Q ${x + w - 2} ${y + h - 2}, ${x + w - 4} ${y + h - 2} L ${x} ${y + h - 2}" ` +
            `stroke="${T.routeLine}" stroke-width="4" fill="none"/>` +
            `</g>`;
    }

    /* --- Track breaker (insulated rail joint) ------------------------------ *
     *  A short vertical bar that marks the boundary between two track
     *  circuits / sections. Drawn perpendicular to the rail so it remains
     *  visible against both the rail body and any section pill.
     *
     *  Geometry:                    ┃   ← yellow bar
     *      ───────────────●────────────── rail
     *                    cx,cy
     * -------------------------------------------------------------------- */
    function renderTrackBreaker(cell) {
        const x = cell.position.x;
        const y = cell.position.y;
        const w = cell.size.width || 12;
        const h = cell.size.height || 26;
        const cx = x + w / 2;
        const cy = y + h / 2;
        const barH = Math.max(18, h);
        const barW = 3;

        const lblAttr = (cell.attrs && cell.attrs.label) || {};
        const lblText = lblAttr.text != null ? String(lblAttr.text) : '';
        const lblVisible = !!lblText && lblAttr.fill !== 'transparent';

        let svg = `<g class="sip-asset sip-breaker" data-id="${cell.id}" data-type="${cell.type}">`;
        // dark backing makes the bar pop even when it lands on top of a pill
        svg += `<rect x="${cx - barW / 2 - 1}" y="${cy - barH / 2 - 1}" width="${barW + 2}" height="${barH + 2}" fill="#0a0f1e"/>`;
        svg += `<rect x="${cx - barW / 2}"     y="${cy - barH / 2}"     width="${barW}"     height="${barH}"     fill="#FFD400"/>`;
        if (lblVisible) {
            const fill = lblAttr.fill || '#dcd7d7';
            const sz = +lblAttr.fontSize || 11;
            svg += `<text x="${cx}" y="${cy + barH / 2 + sz + 2}" fill="${fill}" ` +
                `font-family="${T.labelFont}" font-size="${sz}" font-weight="600" ` +
                `text-anchor="middle">${escapeXml(lblText)}</text>`;
        }
        svg += `</g>`;
        return svg;
    }

    /* ------------------------------------------------------------------------ *
     *  ASSET REGISTRY
     * ------------------------------------------------------------------------ */
    const ASSETS = {
        'examples.Track': {
            label: 'Track (short)', group: 'track',
            size: { width: 300, height: 100 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderTrack,
            icon: function () {
                return `<svg viewBox="0 0 60 24"><rect x="2" y="10" width="56" height="4" fill="${T.railBase}"/>` +
                    `<line x1="2" y1="10" x2="58" y2="10" stroke="#fff" stroke-width="0.4"/>` +
                    `<line x1="2" y1="14" x2="58" y2="14" stroke="#fff" stroke-width="0.4"/>` +
                    Array.from({ length: 18 }, (_, i) => `<rect x="${4 + i * 3}" y="11" width="1" height="2" fill="${T.sleeper}"/>`).join('') +
                    `</svg>`;
            }
        },
        'examples.Track1': {
            label: 'Track (long)', group: 'track',
            size: { width: 200, height: 100 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderTrack,
            icon: function () { return ASSETS['examples.Track'].icon(); }
        },
        'examples.Track2': {
            label: 'Track (extra-long)', group: 'track',
            size: { width: 400, height: 100 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderTrack,
            icon: function () { return ASSETS['examples.Track'].icon(); }
        },
        'examples.Track3': {
            hidden: true, label: 'Curve (left)', group: 'point',
            size: { width: 100, height: 100 },
            attrs: {
                path: { type: 'path', fill: T.railBase, stroke: 'none', strokeWidth: 0, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderTrackCurve(cell, 'left'); },
            icon: function () {
                return `<svg viewBox="0 0 60 24"><path d="M 2 8 C 20 8, 40 18, 58 18" stroke="${T.railBase}" stroke-width="5" fill="none" stroke-linecap="round"/></svg>`;
            }
        },
        'examples.Track4': {
            hidden: true, label: 'Curve (right)', group: 'point',
            size: { width: 100, height: 100 },
            attrs: {
                path: { type: 'path', fill: T.railBase, stroke: 'none', strokeWidth: 0, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderTrackCurve(cell, 'right'); },
            icon: function () {
                return `<svg viewBox="0 0 60 24"><path d="M 58 8 C 40 8, 20 18, 2 18" stroke="${T.railBase}" stroke-width="5" fill="none" stroke-linecap="round"/></svg>`;
            }
        },
        'examples.TrackBreaker': {
            hidden: true, label: 'Track Breaker', group: 'track',
            size: { width: 12, height: 26 },
            attrs: {
                label: { text: '', fill: 'transparent', fontSize: 11, fontWeight: 'bold' }
            },
            render: renderTrackBreaker,
            icon: function () {
                return `<svg viewBox="0 0 60 24">` +
                    `<rect x="2"  y="10" width="56" height="4" fill="${T.railBase}"/>` +
                    `<line x1="2"  y1="10" x2="58" y2="10" stroke="#fff" stroke-width="0.4"/>` +
                    `<line x1="2"  y1="14" x2="58" y2="14" stroke="#fff" stroke-width="0.4"/>` +
                    Array.from({ length: 18 }, (_, i) => `<rect x="${4 + i * 3}" y="11" width="1" height="2" fill="${T.sleeper}"/>`).join('') +
                    `<rect x="28" y="3"  width="4" height="18" fill="#0a0f1e"/>` +
                    `<rect x="29" y="4"  width="2" height="16" fill="#FFD400"/>` +
                    `</svg>`;
            }
        },
        'examples.PointMachine': {
            label: 'Point Machine', group: 'point',
            size: { width: 100, height: 60 },
            attrs: {
                body: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                circle1: { cx: 35, cy: 54, r: 6, stroke: 'black', fill: '#d4d4d4' },
                label: { text: '', fill: T.labelHi, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderPointMachine(cell, false); },
            icon: function () {
                // Normal (/) — circle below-right of the diagonal
                return `<svg viewBox="0 0 60 36">` +
                    `<polygon points="4,28 56,6 58,8 6,30" fill="${T.railBase}"/>` +
                    `<line x1="4" y1="28" x2="56" y2="6" stroke="white" stroke-width="0.5" opacity="0.7"/>` +
                    `<line x1="6" y1="30" x2="58" y2="8" stroke="white" stroke-width="0.5" opacity="0.7"/>` +
                    `<circle cx="38" cy="25" r="5.5" fill="#0a0f1e" stroke="#22d3ee" stroke-width="0.8"/>` +
                    `<circle cx="38" cy="25" r="4" fill="${T.pmDot}"/>` +
                    `</svg>`;
            }
        },
        'examples.PointMachine1': {
            label: 'Point Machine (mirror)', group: 'point',
            size: { width: 100, height: 60 },
            attrs: {
                body: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                circle1: { cx: 54, cy: 50, r: 6, stroke: 'black', fill: '#d4d4d4' },
                label: { text: '', fill: T.labelHi, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderPointMachine(cell, true); },
            icon: function () {
                // Mirror (\) — circle below-left of the diagonal
                return `<svg viewBox="0 0 60 36">` +
                    `<polygon points="4,6 56,28 58,26 6,4" fill="${T.railBase}"/>` +
                    `<line x1="4" y1="6" x2="56" y2="28" stroke="white" stroke-width="0.5" opacity="0.7"/>` +
                    `<line x1="6" y1="4" x2="58" y2="26" stroke="white" stroke-width="0.5" opacity="0.7"/>` +
                    `<circle cx="22" cy="25" r="5.5" fill="#0a0f1e" stroke="#22d3ee" stroke-width="0.8"/>` +
                    `<circle cx="22" cy="25" r="4" fill="${T.pmDot}"/>` +
                    `</svg>`;
            }
        },
        'examples.RouteCallingSignal': {
            label: 'Route / Calling Signal', group: 'signal',
            size: { width: 150, height: 95 },
            attrs: {
                signal: { standPos: 'R', standLength: 24, standArm: 14, standDrop: 'down', labelGap: 6 },
                route: {
                    enabled: true,
                    labels: 'AUG,BUG,CUG,DUG',
                    active: '',
                    routeSide: 'top',
                    calling: true,
                    callingSide: 'bottom',
                    callingLabel: 'C',
                    dotCount: 1,
                    dotSize: 3.8,
                    labelSize: 8.5,
                    labelGap: 8,
                    armSpacing: 22,
                    armLength: 22,
                    compact: true
                },
                label: { text: '', fill: T.labelDefault, fontSize: 12, fontWeight: 'bold' }
            },
            render: renderRouteCallingSignal,
            icon: function () {
                return `<svg viewBox="0 0 80 54" xmlns="http://www.w3.org/2000/svg">` +
                    `<line x1="18" y1="30" x2="62" y2="30" stroke="#9aa8c4" stroke-width="1.1" opacity="0.7"/>` +
                    `<line x1="24" y1="30" x2="24" y2="13" stroke="#9aa8c4" stroke-width="1.3"/>` +
                    `<line x1="40" y1="30" x2="40" y2="13" stroke="#9aa8c4" stroke-width="1.3"/>` +
                    `<line x1="56" y1="30" x2="56" y2="13" stroke="#9aa8c4" stroke-width="1.3"/>` +
                    `<circle cx="24" cy="13" r="3.2" fill="#fff"/><circle cx="40" cy="13" r="3.2" fill="#0a0f1e" stroke="#fff"/><circle cx="56" cy="13" r="3.2" fill="#0a0f1e" stroke="#fff"/>` +
                    `<text x="24" y="6" font-family="${T.labelFont}" font-size="6" fill="#d7d7d7" text-anchor="middle" font-weight="800">AUG</text>` +
                    `<text x="40" y="6" font-family="${T.labelFont}" font-size="6" fill="#d7d7d7" text-anchor="middle" font-weight="800">BUG</text>` +
                    `<text x="56" y="6" font-family="${T.labelFont}" font-size="6" fill="#d7d7d7" text-anchor="middle" font-weight="800">CUG</text>` +
                    `<line x1="40" y1="30" x2="40" y2="45" stroke="#9aa8c4" stroke-width="1.1"/>` +
                    `<circle cx="40" cy="45" r="5" fill="#0a0f1e" stroke="#fff"/><text x="40" y="46" font-family="${T.labelFont}" font-size="7" fill="#fff" font-weight="900" text-anchor="middle">C</text>` +
                    `</svg>`;
            }
        },
        'examples.Signal': {
            label: 'Signal', group: 'signal',
            size: { width: 90, height: 24 },
            attrs: {
                signal: { lamps: 'BBB', standPos: 'R', standLength: 30, standArm: 14, standDrop: 'down', lit: '', labelGap: 6 },
                route: {
                    enabled: false,
                    labels: 'AUG,BUG,CUG,DUG',
                    active: '',
                    routeSide: 'top',
                    calling: true,
                    callingSide: 'bottom',
                    callingLabel: 'C',
                    dotCount: 1,
                    dotSize: 3.8,
                    labelSize: 8.5,
                    labelGap: 8,
                    armSpacing: 22,
                    armLength: 22,
                    attachGap: 6,
                    callingGap: 6,
                    compact: true
                },
                label: { text: '', fill: 'transparent', fontSize: 12, fontWeight: 'bold' }
            },
            render: renderSignalComposite,
            icon: function () {
                /* Toolbox tile preview — 3 blank lamps in a pill with a tiny stand. */
                return `<svg viewBox="0 0 60 30" xmlns="http://www.w3.org/2000/svg">` +
                    `<line x1="30" y1="20" x2="30" y2="28" stroke="#b8b8b8" stroke-width="1.5" stroke-linecap="round"/>` +
                    `<rect x="4"  y="8" width="52" height="12" rx="3" ry="3" fill="#606060"/>` +
                    `<circle cx="13" cy="14" r="3.6" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.6"/>` +
                    `<circle cx="30" cy="14" r="3.6" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.6"/>` +
                    `<circle cx="47" cy="14" r="3.6" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.6"/>` +
                    `</svg>`;
            }
        },
        'examples.SignalBackground': {
            hidden: true, label: 'Signal Background', group: 'signal',
            size: { width: 54, height: 27 },
            attrs: {
                body: { type: 'rect', fill: '#606060', stroke: 'none', strokeWidth: 0 },
                label: { text: '', fill: 'transparent', fontSize: 12, fontWeight: 'bold' }
            },
            render: renderSignalBackground,
            icon: function () {
                return `<svg viewBox="0 0 60 24">` +
                    `<rect x="3" y="6"  width="54" height="12" rx="3" ry="3" fill="#606060"/>` +
                    `<circle cx="13" cy="12" r="3.5" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.8"/>` +
                    `<circle cx="30" cy="12" r="3.5" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.8"/>` +
                    `<circle cx="47" cy="12" r="3.5" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="0.8"/>` +
                    `</svg>`;
            }
        },
        'examples.Signald90': {
            label: 'Signal — Red (RG)', group: 'signal',
            size: { width: 30, height: 30 },
            attrs: {
                circle1: { cx: 10, cy: 10, r: 10, stroke: 'black', fill: '#d4d4d4' },
                path1: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 11, fontWeight: 'bold' }
            },
            render: function (cell) { return renderSignalLamp(cell, null, 'R'); },
            icon: function () { return signalLampIcon(T.lampRed, 'R'); }
        },
        'examples.Signal90': {
            label: 'Signal — Green (DG)', group: 'signal',
            size: { width: 30, height: 30 },
            attrs: {
                circle1: { cx: 10, cy: 10, r: 10, stroke: 'black', fill: '#d4d4d4' },
                path1: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 11, fontWeight: 'bold' }
            },
            render: function (cell) { return renderSignalLamp(cell, null, 'G'); },
            icon: function () { return signalLampIcon(T.lampGreen, 'G'); }
        },
        'examples.Signal45': {
            label: 'Signal — Yellow (HG)', group: 'signal',
            size: { width: 30, height: 30 },
            attrs: {
                circle1: { cx: 10, cy: 10, r: 10, stroke: 'black', fill: '#d4d4d4' },
                path1: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 11, fontWeight: 'bold' }
            },
            render: function (cell) { return renderSignalLamp(cell, null, 'Y'); },
            icon: function () { return signalLampIcon(T.lampYellow, 'Y'); }
        },
        'examples.Signald45': {
            label: 'Signal — Double Yellow (HHG)', group: 'signal',
            size: { width: 30, height: 30 },
            attrs: {
                circle1: { cx: 10, cy: 10, r: 10, stroke: 'black', fill: '#d4d4d4' },
                path1: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                path2: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 11, fontWeight: 'bold' }
            },
            render: function (cell) { return renderSignalLamp(cell, null, 'X'); },
            icon: function () { return signalLampIcon(T.lampYellow, 'X'); }
        },
        'examples.Post': {
            label: 'Signal Post / Label', group: 'signal',
            size: { width: 60, height: 60 },
            attrs: {
                label: { text: 'NEW', fill: T.siding, fontSize: 16, fontWeight: 'bold' }
            },
            render: renderPost,
            icon: function () {
                return `<svg viewBox="0 0 60 24"><text x="30" y="16" text-anchor="middle" font-family="${T.labelFont}" font-size="12" fill="${T.siding}" font-weight="700">S18</text></svg>`;
            }
        },
        'examples.Post1': {
            label: 'Signal Post (alt)', group: 'signal',
            size: { width: 60, height: 60 },
            attrs: {
                label: { text: 'NEW', fill: T.siding, fontSize: 16, fontWeight: 'bold' }
            },
            render: renderPost,
            icon: function () { return ASSETS['examples.Post'].icon(); }
        },
        'examples.Shaunt': {
            label: 'Shunt 3-Asp · Proceed', group: 'shunt',
            size: { width: 30, height: 30 },
            attrs: {
                signal: { standPos: 'R', standLength: 22, standArm: 12, standDrop: 'down', signalSide: 'up', labelGap: 8 },
                body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderShunt(cell, 1); },
            icon: function () {
                // v1 — top dim, BL lit, BR lit — PROCEED
                return `<svg viewBox="0 0 56 36">` +
                    `<g transform="translate(8 1) scale(0.96)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="#3a3e44"/>` +
                    `<g transform="translate(12.5 11.5) scale(0.96) translate(-12.5 -11.5)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="${T.shuntBody}"/>` +
                    `</g></g>` +
                    `<circle cx="19.4" cy="8.2" r="2.2" fill="white" fill-opacity="0.12" stroke="white" stroke-opacity="0.25" stroke-width="0.5"/>` +
                    `<circle cx="15.5" cy="18.5" r="2.2" fill="white"/>` +
                    `<circle cx="23.4" cy="18.5" r="2.2" fill="white"/>` +
                    `<text x="28" y="32" font-family="${T.labelFont}" font-size="7" fill="#22D142" font-weight="700">PROCEED</text>` +
                    `</svg>`;
            }
        },
        'examples.Shaunt2': {
            label: 'Shunt 3-Asp · Diverge', group: 'shunt',
            size: { width: 30, height: 30 },
            attrs: {
                signal: { standPos: 'R', standLength: 22, standArm: 12, standDrop: 'down', signalSide: 'up', labelGap: 8 },
                body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderShunt(cell, 2); },
            icon: function () {
                // v2 — top lit, BL DIM, BR lit — DIVERGE
                return `<svg viewBox="0 0 56 36">` +
                    `<g transform="translate(8 1) scale(0.96)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="#3a3e44"/>` +
                    `<g transform="translate(12.5 11.5) scale(0.96) translate(-12.5 -11.5)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="${T.shuntBody}"/>` +
                    `</g></g>` +
                    `<circle cx="19.4" cy="8.2" r="2.2" fill="white"/>` +
                    `<circle cx="15.5" cy="18.5" r="2.2" fill="white" fill-opacity="0.12" stroke="white" stroke-opacity="0.25" stroke-width="0.5"/>` +
                    `<circle cx="23.4" cy="18.5" r="2.2" fill="white"/>` +
                    `<text x="28" y="32" font-family="${T.labelFont}" font-size="7" fill="#FFD400" font-weight="700">DIVERGE</text>` +
                    `</svg>`;
            }
        },
        'examples.Shaunt3': {
            label: 'Shunt 3-Asp · Off', group: 'shunt',
            size: { width: 30, height: 30 },
            attrs: {
                signal: { standPos: 'R', standLength: 22, standArm: 12, standDrop: 'down', signalSide: 'up', labelGap: 8 },
                body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderShunt(cell, 3); },
            icon: function () {
                // v3 — all three dim — OFF
                return `<svg viewBox="0 0 56 36">` +
                    `<g transform="translate(8 1) scale(0.96)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="#3a3e44"/>` +
                    `<g transform="translate(12.5 11.5) scale(0.96) translate(-12.5 -11.5)">` +
                    `<path d="M 7.9 0.2 C 5.8 0.7 4.3 2.9 2.1 8.5 C 0.4 13 0 15 0 18.6 L 0 23 L 25 23 L 25 17.5 L 25 12 L 21.8 9.1 C 20 7.5 17.3 4.9 15.7 3.3 C 12.9 0.6 10.4 -0.5 7.9 0.2 Z" fill="${T.shuntBody}"/>` +
                    `</g></g>` +
                    `<circle cx="19.4" cy="8.2" r="2.2" fill="white" fill-opacity="0.12" stroke="white" stroke-opacity="0.25" stroke-width="0.5"/>` +
                    `<circle cx="15.5" cy="18.5" r="2.2" fill="white" fill-opacity="0.12" stroke="white" stroke-opacity="0.25" stroke-width="0.5"/>` +
                    `<circle cx="23.4" cy="18.5" r="2.2" fill="white" fill-opacity="0.12" stroke="white" stroke-opacity="0.25" stroke-width="0.5"/>` +
                    `<text x="28" y="32" font-family="${T.labelFont}" font-size="7" fill="#888" font-weight="700">OFF</text>` +
                    `</svg>`;
            }
        },
        'examples.SignalShunt': {
            label: 'Signal + Shunt (combined)', group: 'signal',
            size: { width: 200, height: 80 },
            attrs: {
                signal: { lamps: 'RYG', standPos: 'R', standLength: 30, standArm: 14, standDrop: 'down', signalSide: 'up', lit: '', lampGap: 7, shuntGap: 2, shuntSide: 'right', shuntState: 'PROCEED', shuntSize: 42, labelGap: 6 },
                lit: '',            // '' | 'R' | 'Y' | 'G' | 'X'
                body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderSignalShunt,
            icon: function () {
                return `<svg viewBox="0 0 80 28">` +
                    `<rect x="2" y="4" width="46" height="20" rx="3" fill="#3a3a3a"/>` +
                    `<rect x="3" y="5" width="44" height="18" rx="2" fill="#606060"/>` +
                    `<circle cx="12" cy="14" r="5" fill="#FF2E2E"/>` +
                    `<circle cx="25" cy="14" r="5" fill="#FFD400"/>` +
                    `<circle cx="38" cy="14" r="5" fill="#22D142"/>` +
                    // mini shunt triangle
                    `<path d="M 56 7 L 68 7 Q 76 7 78 14 Q 76 22 68 22 L 56 22 Q 55 22 55 21 L 55 8 Q 55 7 56 7 Z" fill="#5B6168"/>` +
                    `<circle cx="60" cy="12" r="1.8" fill="white"/>` +
                    `<circle cx="68" cy="12" r="1.8" fill="white"/>` +
                    `<circle cx="64" cy="18" r="1.8" fill="white"/>` +
                    `</svg>`;
            }
        },
        'examples.AxleCounter': {
            label: 'Axle Counter', group: 'shunt',
            size: { width: 30, height: 30 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3, pointerEvents: 'bounding-box' },
                circle1: { cx: 36, cy: 65, r: 5, stroke: '#6a7596', fill: 'white' },
                circle2: { cx: 0, cy: 100, r: 5, stroke: '#6a7596', fill: 'white' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderAxleCounter,
            icon: function () {
                return `<svg viewBox="0 0 50 24">` +
                    `<line x1="35" y1="6" x2="10" y2="20" stroke="${T.signalBody}" stroke-width="2"/>` +
                    `<circle cx="35" cy="6" r="3" fill="${T.lampOff}" stroke="${T.signalBody}"/>` +
                    `<circle cx="10" cy="20" r="3" fill="${T.lampOff}" stroke="${T.signalBody}"/>` +
                    `</svg>`;
            }
        },
        'examples.Gate': {
            label: 'Gate', group: 'shunt',
            size: { width: 60, height: 60 },
            attrs: {
                body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 1, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderGate,
            icon: function () {
                return `<svg viewBox="0 0 50 24">` +
                    `<line x1="22" y1="4" x2="22" y2="20" stroke="${T.signalBody}" stroke-width="2"/>` +
                    `<line x1="28" y1="4" x2="28" y2="20" stroke="${T.signalBody}" stroke-width="2"/>` +
                    `<line x1="14" y1="8"  x2="36" y2="8"  stroke="${T.signalBody}" stroke-width="1"/>` +
                    `<line x1="14" y1="12" x2="36" y2="12" stroke="${T.signalBody}" stroke-width="1"/>` +
                    `<line x1="14" y1="16" x2="36" y2="16" stroke="${T.signalBody}" stroke-width="1"/>` +
                    `</svg>`;
            }
        },
        'examples.BusBar': {
            label: 'Bus Bar', group: 'busbar',
            size: { width: 100, height: 25 },
            attrs: {
                label: { text: 'B1', fill: T.bbLabel, fontSize: 14, fontWeight: 'bold' }
            },
            render: renderBusBar,
            icon: function () {
                return `<svg viewBox="0 0 60 24"><rect x="6" y="8" width="48" height="6" rx="2" fill="${T.busBar}"/>` +
                    `<text x="30" y="22" text-anchor="middle" font-family="${T.labelFont}" font-size="9" font-weight="800" fill="${T.bbLabel}">B1</text></svg>`;
            }
        },
        'examples.RightArrow': {
            label: 'Arrow →', group: 'busbar',
            size: { width: 100, height: 30 },
            attrs: {
                path: { type: 'path', fill: T.signalBody, stroke: T.signalBody, strokeWidth: 0, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderArrow(cell, 'right'); },
            icon: function () {
                return `<svg viewBox="0 0 60 24"><polygon points="6,10 42,10 42,6 54,12 42,18 42,14 6,14" fill="${T.signalBody}"/></svg>`;
            }
        },
        'examples.LeftArrow': {
            label: 'Arrow ←', group: 'busbar',
            size: { width: 100, height: 30 },
            attrs: {
                path: { type: 'path', fill: T.signalBody, stroke: T.signalBody, strokeWidth: 0, pointerEvents: 'bounding-box' },
                label: { text: '', fill: T.labelDefault, fontSize: 14, fontWeight: 'bold' }
            },
            render: function (cell) { return renderArrow(cell, 'left'); },
            icon: function () {
                return `<svg viewBox="0 0 60 24"><polygon points="54,10 18,10 18,6 6,12 18,18 18,14 54,14" fill="${T.signalBody}"/></svg>`;
            }
        },
        /* ----- Signal stands (L-brackets connecting signal heads to track) ----- *
         *   examples.TopLine    — signal sits ABOVE track, stand drops DOWN
         *   examples.BottomLine — signal sits BELOW track, stand rises UP
         *   Both use position.x/y as the bounding-box origin and size.width/height
         *   as the L-bracket extents. Renderers are renderTopLine / renderBottomLine
         *   above.
         * ----------------------------------------------------------------------- */
        'examples.TopLine': {
            hidden: true, label: 'Signal Stand ↑', group: 'signal',
            size: { width: 37, height: 49 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: T.routeLine, strokeWidth: 4, pointerEvents: 'bounding-box' }
            },
            render: renderTopLine,
            icon: function () {
                return `<svg viewBox="0 0 32 32">` +
                    `<path d="M 6 28 L 6 8 Q 6 6 8 6 L 26 6" ` +
                    `stroke="${T.routeLine}" stroke-width="3.5" fill="none" ` +
                    `stroke-linecap="round" stroke-linejoin="round"/>` +
                    `</svg>`;
            }
        },
        'examples.BottomLine': {
            hidden: true, label: 'Signal Stand ↓', group: 'signal',
            size: { width: 37, height: 56 },
            attrs: {
                path: { type: 'path', fill: 'none', stroke: T.routeLine, strokeWidth: 4, pointerEvents: 'bounding-box' }
            },
            render: renderBottomLine,
            icon: function () {
                return `<svg viewBox="0 0 32 32">` +
                    `<path d="M 26 4 L 26 24 Q 26 26 24 26 L 6 26" ` +
                    `stroke="${T.routeLine}" stroke-width="3.5" fill="none" ` +
                    `stroke-linecap="round" stroke-linejoin="round"/>` +
                    `</svg>`;
            }
        }
    };

    function signalLampIcon(_colour, kind) {
        let svg = `<svg viewBox="0 0 60 32">` +
            `<rect x="20" y="2" width="20" height="28" rx="7" fill="#3a3a3a"/>` +
            `<rect x="21" y="3" width="18" height="26" rx="6" fill="#606060"/>` +
            `<circle cx="30" cy="16" r="9.5" fill="none" stroke="#2a2a2a" stroke-width="0.8"/>` +
            `<circle cx="30" cy="16" r="7" fill="#f4f4f4" stroke="#2a2a2a" stroke-width="1"/>` +
            `<circle cx="30" cy="16" r="5.8" fill="none" stroke="rgba(0,0,0,0.12)" stroke-width="0.8"/>`;
        if (kind === 'R') {
            svg += `<line x1="24" y1="15" x2="36" y2="15" stroke="#3a3a3a" stroke-width="1.2"/>` +
                `<line x1="24" y1="17" x2="36" y2="17" stroke="#3a3a3a" stroke-width="1.2"/>`;
        } else if (kind === 'G') {
            svg += `<line x1="30" y1="10" x2="30" y2="22" stroke="#3a3a3a" stroke-width="1.2"/>`;
        } else if (kind === 'Y') {
            svg += `<line x1="25" y1="11" x2="35" y2="21" stroke="#3a3a3a" stroke-width="1.2"/>`;
        } else if (kind === 'X') {
            svg += `<line x1="25" y1="12" x2="35" y2="22" stroke="#3a3a3a" stroke-width="1.2"/>` +
                `<line x1="25" y1="10" x2="35" y2="20" stroke="#3a3a3a" stroke-width="1.2"/>`;
        }
        return svg + `</svg>`;
    }

    /* ------------------------------------------------------------------------ *
     *  Toolbox groups
     * ------------------------------------------------------------------------ */
    const GROUPS = [
        { key: 'track', label: 'Track' },
        { key: 'point', label: 'Track & Point Machine' },
        { key: 'signal', label: 'Signal' },
        { key: 'shunt', label: 'Shunt / Axle / Gate' },
        { key: 'busbar', label: 'Bus Bar / Arrow' }
    ];

    /* ------------------------------------------------------------------------ *
     *  Public helpers
     * ------------------------------------------------------------------------ */
    function spec(type) { return ASSETS[type] || null; }

    function makeCell(type, x, y) {
        const s = ASSETS[type];
        if (!s) throw new Error('Unknown SIP asset type: ' + type);
        return {
            type: type,
            position: { x: x - s.size.width / 2, y: y - s.size.height / 2 },
            size: { width: s.size.width, height: s.size.height },
            angle: 0,
            id: uid(),
            z: 1,
            attrs: JSON.parse(JSON.stringify(s.attrs))
        };
    }

    /* Universal per-asset label nudge.
     * Every renderer draws the user label as a <text>…</text> whose body is the
     * cell's label string. This shifts ONLY those label glyphs by the user-set
     * pixel offset (attrs.label.dx / attrs.label.dy) so any asset's label can be
     * repositioned from the inspector without touching each renderer. Asset
     * graphics and non-label text (lamp letters, etc.) are left untouched. */
    function applyLabelOffset(inner, cell) {
        const lbl = (cell.attrs && cell.attrs.label) || {};
        const dx = +lbl.dx || 0;
        const dy = +lbl.dy || 0;
        if (!dx && !dy) return inner;
        const txt = lbl.text != null ? String(lbl.text) : '';
        if (txt === '') return inner;

        // Preferred path: renderers that draw a composite label (pill/badge +
        // text) wrap it in <g class="sip-label">…</g>. Translate that whole
        // group so the badge and text move together. Wrap the group's contents
        // in a translate so the marker class stays on the outer <g>.
        if (inner.indexOf('class="sip-label"') !== -1) {
            return inner.replace(/(<g class="sip-label">)([\s\S]*?)(<\/g>)/g,
                (m, open, body, close) =>
                    open + '<g transform="translate(' + dx + ' ' + dy + ')">' + body + '</g>' + close);
        }

        // Fallback: plain-text labels (no badge) — shift just the <text> glyphs.
        const body = escapeXml(txt).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        const re = new RegExp('(<text\\b[^>]*>)(' + body + ')(</text>)', 'g');
        return inner.replace(re, (m) =>
            '<g transform="translate(' + dx + ' ' + dy + ')">' + m + '</g>');
    }

    function renderCell(cell) {
        const s = ASSETS[cell.type];
        let inner;
        if (!s) {
            const x = cell.position.x, y = cell.position.y;
            const w = (cell.size && cell.size.width) || 60;
            const h = (cell.size && cell.size.height) || 60;
            inner = `<g class="sip-asset sip-unknown" data-id="${cell.id}" data-type="${cell.type}">` +
                `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="rgba(255,46,46,0.2)" stroke="${T.lampRed}" stroke-dasharray="4,4"/>` +
                `<text x="${x + w / 2}" y="${y + h / 2}" text-anchor="middle" dominant-baseline="middle" fill="${T.lampRed}" font-family="${T.labelFont}" font-size="11">${escapeXml(cell.type)}</text>` +
                `</g>`;
        } else {
            inner = s.render(cell);
        }
        inner = applyLabelOffset(inner, cell);
        if (cell.angle && Math.abs(cell.angle) > 0.01) {
            const w = (cell.size && cell.size.width) || 60;
            const h = (cell.size && cell.size.height) || 60;
            const cx = (cell.position && cell.position.x || 0) + w / 2;
            const cy = (cell.position && cell.position.y || 0) + h / 2;
            return `<g transform="rotate(${cell.angle} ${cx} ${cy})">${inner}</g>`;
        }
        return inner;
    }

    function renderIcon(type) {
        const s = ASSETS[type];
        return s ? s.icon() : '';
    }

    function renderYard(opts) {
        opts = opts || {};
        const viewBox = opts.viewBox || '0 0 2000 740';
        const cells = opts.cells || [];
        const background = opts.background !== false;
        const className = opts.className || 'sip-yard';
        let bg = '';
        if (background) {
            bg = `<defs>` +
                `<linearGradient id="sipBg" x1="0" y1="0" x2="0" y2="1">` +
                `<stop offset="0%"   stop-color="#0c1530"/>` +
                `<stop offset="55%"  stop-color="#08101c"/>` +
                `<stop offset="100%" stop-color="#060a14"/>` +
                `</linearGradient>` +
                `</defs>` +
                `<rect width="100%" height="100%" fill="url(#sipBg)"/>`;
        }
        prepareRailContext(cells);
        const railLayer = renderRailLayer(cells);
        const pmConnLayer = renderPMConnectorLayer(cells);
        const standLayer = renderStandLayer(cells);
        const breakerLayer = renderBreakerLayer(cells);
        const body = cells.map(renderCell).join('\n');
        return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}" class="${className}" preserveAspectRatio="xMidYMid meet">` +
            bg + railLayer + pmConnLayer + standLayer + breakerLayer + body +
            `</svg>`;
    }

    /* ------------------------------------------------------------------------ *
     *  RAIL LAYER
     * ------------------------------------------------------------------------ */
    const TRACK_TYPES = { 'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1 };

    function renderRailLayer(cells) {
        if (!Array.isArray(cells) || cells.length === 0) return '';

        const Y_TOL = 40;
        const items = [];
        for (const c of cells) {
            if (!c || !TRACK_TYPES[c.type]) continue;
            const x = c.position.x;
            const y = c.position.y;
            const w = (c.size && c.size.width) || 60;
            const h = (c.size && c.size.height) || 60;
            const stroke = (c.attrs && c.attrs.path && c.attrs.path.stroke) || '#3c4260';
            items.push({
                cy: y + h / 2,
                xLeft: x,
                xRight: x + w,
                stroke: stroke,
                lit: isLit(stroke)
            });
        }
        if (!items.length) return '';

        items.sort(function (a, b) { return a.cy - b.cy || a.xLeft - b.xLeft; });
        const bands = [];
        let cur = null;
        for (const it of items) {
            if (!cur || (it.cy - cur.maxY) > Y_TOL) {
                cur = { items: [it], minY: it.cy, maxY: it.cy };
                bands.push(cur);
            } else {
                cur.items.push(it);
                cur.maxY = it.cy;
            }
        }

        // Publish band y-centres for renderStick() to find the nearest rail.
        _railContext.bands = bands.map(function (b) {
            const ys = b.items.map(function (i) { return i.cy; });
            return { cy: Math.round(ys[Math.floor(ys.length / 2)]) };
        });

        const patId = 'sipSleepers_' + Math.floor(Math.random() * 1e9).toString(36);
        const BED_H = 18;
        const SLEEP_W = 3;
        const SLEEP_STEP = 6;

        let svg = '<g class="sip-rail-layer" pointer-events="none">';
        svg += '<defs>' +
            '<pattern id="' + patId + '" x="0" y="0" width="' + SLEEP_STEP + '" height="' + BED_H + '" patternUnits="userSpaceOnUse">' +
            '<rect x="0" y="0" width="' + SLEEP_W + '" height="' + BED_H + '" fill="' + T.sleeper + '"/>' +
            '</pattern>' +
            '</defs>';

        for (const band of bands) {
            band.items.sort(function (a, b) { return a.xLeft - b.xLeft; });
            const ys = band.items.map(function (i) { return i.cy; });
            const cy = Math.round(ys[Math.floor(ys.length / 2)]);
            const minX = band.items[0].xLeft - 8;
            const maxX = band.items[band.items.length - 1].xRight + 8;
            const top = cy - BED_H / 2;

            svg += '<rect x="' + minX + '" y="' + top + '" width="' + (maxX - minX) + '" height="' + BED_H + '" ' +
                'fill="' + T.railBase + '"/>';
            svg += '<rect x="' + minX + '" y="' + top + '" width="' + (maxX - minX) + '" height="' + BED_H + '" ' +
                'fill="url(#' + patId + ')" opacity="0.55"/>';
            svg += '<line x1="' + minX + '" y1="' + top + '" x2="' + maxX + '" y2="' + top + '" ' +
                'stroke="' + T.railEdge + '" stroke-width="1.2"/>';
            svg += '<line x1="' + minX + '" y1="' + (top + BED_H) + '" x2="' + maxX + '" y2="' + (top + BED_H) + '" ' +
                'stroke="' + T.railEdge + '" stroke-width="1.2"/>';

            for (const it of band.items) {
                if (it.lit) {
                    svg += '<rect x="' + it.xLeft + '" y="' + (top - 3) + '" ' +
                        'width="' + (it.xRight - it.xLeft) + '" height="' + (BED_H + 6) + '" ' +
                        'fill="' + it.stroke + '" opacity="0.25" rx="2"/>';
                    svg += '<rect x="' + it.xLeft + '" y="' + top + '" ' +
                        'width="' + (it.xRight - it.xLeft) + '" height="' + BED_H + '" ' +
                        'fill="' + it.stroke + '" opacity="0.85"/>';
                    svg += '<rect x="' + it.xLeft + '" y="' + top + '" ' +
                        'width="' + (it.xRight - it.xLeft) + '" height="' + BED_H + '" ' +
                        'fill="url(#' + patId + ')" opacity="0.30"/>';
                }
            }
        }
        svg += '</g>';
        return svg;
    }

    /* ------------------------------------------------------------------------ *
     *  prepareRailContext — call BEFORE renderCell loops, so the signal/shunt
     *  L-sticks know where the rail bands are. The editor should invoke this
     *  once per render frame.
     * ------------------------------------------------------------------------ */
    function prepareRailContext(cells) {
        if (!Array.isArray(cells) || !cells.length) {
            _railContext.bands = [];
            return;
        }
        const Y_TOL = 40;
        const ys = [];
        for (const c of cells) {
            if (!c || !TRACK_TYPES[c.type]) continue;
            const cy = c.position.y + ((c.size && c.size.height) || 60) / 2;
            ys.push(cy);
        }
        if (!ys.length) { _railContext.bands = []; return; }
        ys.sort(function (a, b) { return a - b; });
        const bands = [];
        let cur = [ys[0]];
        for (let i = 1; i < ys.length; i++) {
            if (ys[i] - cur[cur.length - 1] > Y_TOL) {
                bands.push({ cy: Math.round(cur[Math.floor(cur.length / 2)]) });
                cur = [ys[i]];
            } else {
                cur.push(ys[i]);
            }
        }
        bands.push({ cy: Math.round(cur[Math.floor(cur.length / 2)]) });
        _railContext.bands = bands;
    }

    /* ------------------------------------------------------------------------ *
     *  STAND LAYER  —  auto-draws L-bracket stands for every signal group
     *  ------------------------------------------------------------------------
     *  Old-format data stores each signal as 2–3 separate aspect cells
     *  (examples.Signald90 = R, examples.Signal45 = Y, examples.Signal90 = G).
     *  This pre-pass:
     *    1. Clusters track cells into horizontal rail bands (same logic as
     *       renderRailLayer).
     *    2. Groups signal aspect cells by their label prefix (e.g. "S18 RG",
     *       "S18 HG", "S18 DG" → group "S18").
     *    3. Picks the rail band closest to each group's centroid.
     *    4. Emits one L-bracket SVG path linking the signal head to the rail.
     *
     *  No mutation of cells.  No new cells in the saved layout.  The stand is
     *  purely a render-time decoration so the same data renders consistently
     *  in the editor and the live telemetry view.
     *
     *  Toolbox-placed examples.TopLine / examples.BottomLine cells are still
     *  rendered separately (as ordinary cells) — they are how the user adds
     *  manual stands where the auto-layer hasn't.
     * ------------------------------------------------------------------------ */
    const LAMP_TYPES_FOR_STAND = {
        'examples.Signald90': 1,
        'examples.Signal45': 1,
        'examples.Signal90': 1,
        'examples.Signald45': 1
    };

    function renderStandLayer(cells) {
        if (!Array.isArray(cells) || cells.length === 0) return '';

        const TRACK_TYPES_LOCAL = { 'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1 };
        const Y_TOL = 40;

        /* ---- 1. Rail bands from track Y centres -------------------------- */
        const ys = [];
        for (const c of cells) {
            if (!c || !TRACK_TYPES_LOCAL[c.type]) continue;
            const h = (c.size && +c.size.height) || 0;
            const y = (c.position && +c.position.y) || 0;
            ys.push(y + h / 2);
        }
        if (!ys.length) return '';
        ys.sort((a, b) => a - b);

        const bands = [];
        let cur = { values: [ys[0]], max: ys[0] };
        for (let i = 1; i < ys.length; i++) {
            if (ys[i] - cur.max <= Y_TOL) { cur.values.push(ys[i]); cur.max = ys[i]; }
            else { bands.push(cur); cur = { values: [ys[i]], max: ys[i] }; }
        }
        bands.push(cur);
        for (const b of bands) {
            let s = 0; for (const v of b.values) s += v;
            b.mean = Math.round(s / b.values.length);
        }
        function snapBand(y) {
            let best = bands[0], bd = Math.abs(y - bands[0].mean);
            for (const b of bands) {
                const d = Math.abs(y - b.mean);
                if (d < bd) { bd = d; best = b; }
            }
            return best.mean;
        }

        /* ---- 2. Group signal aspect cells by label prefix --------------- */
        const groups = {};
        for (const c of cells) {
            if (!c || !LAMP_TYPES_FOR_STAND[c.type]) continue;
            const lbl = c.attrs && c.attrs.label && c.attrs.label.text ? String(c.attrs.label.text) : '';
            const prefix = lbl ? lbl.split(/\s+/)[0] : '';
            if (!prefix) continue;
            const w = (c.size && +c.size.width) || 60;
            const h = (c.size && +c.size.height) || 60;
            const cx = ((c.position && +c.position.x) || 0) + w / 2;
            const cy = ((c.position && +c.position.y) || 0) + h / 2;
            const g = groups[prefix] || (groups[prefix] = { xs: [], ys: [], minX: Infinity, maxX: -Infinity });
            g.xs.push(cx); g.ys.push(cy);
            if (cx < g.minX) g.minX = cx;
            if (cx > g.maxX) g.maxX = cx;
        }

        /* ---- 3. Emit one L-bracket per group ---------------------------- */
        let svg = '<g class="sip-stand-layer" style="pointer-events:none">';
        for (const prefix in groups) {
            const g = groups[prefix];
            let sx = 0, sy = 0;
            for (const x of g.xs) sx += x;
            for (const y of g.ys) sy += y;
            const gx = sx / g.xs.length;
            const gy = sy / g.ys.length;
            const railY = snapBand(gy);
            const delta = railY - gy;          // +ve = signal above rail
            if (Math.abs(delta) < 8) continue;  // signal already on rail — no stand needed

            /* Choose the L-bracket geometry. Stand starts at one extreme x of
               the signal group (outermost aspect) and bends toward the rail. */
            let cornerX, armStartX, armY, dropEndY;
            if (delta > 0) {
                /* Signal above rail → arm runs RIGHT off the rightmost aspect,
                   then drops DOWN to the rail. */
                armStartX = g.maxX + 18;        // step out past the signal head
                cornerX = armStartX + 16;     // short horizontal arm
                armY = gy + 4;
                dropEndY = railY - 4;
            } else {
                /* Signal below rail → arm runs LEFT off the leftmost aspect,
                   then rises UP to the rail. */
                armStartX = g.minX - 18;
                cornerX = armStartX - 16;
                armY = gy - 4;
                dropEndY = railY + 4;
            }
            const r = 4;
            const armSign = (cornerX > armStartX) ? 1 : -1;
            const dropSign = (dropEndY > armY) ? 1 : -1;
            const cornerIn = cornerX - armSign * r;
            const cornerOut = armY + dropSign * r;

            const d = 'M ' + armStartX + ' ' + armY + ' ' +
                'L ' + cornerIn + ' ' + armY + ' ' +
                'Q ' + cornerX + ' ' + armY + ' ' +
                cornerX + ' ' + cornerOut + ' ' +
                'L ' + cornerX + ' ' + dropEndY;

            svg += '<path d="' + d + '" stroke="' + T.routeLine +
                '" stroke-width="4" fill="none" stroke-linecap="round" ' +
                'stroke-linejoin="round" data-stand-for="' + prefix + '"/>';
        }
        svg += '</g>';
        return svg;
    }

    /* ------------------------------------------------------------------------ *
     *  BREAKER LAYER  —  auto-draws insulated rail joints at every rail end
     *  ------------------------------------------------------------------------
     *  Mirrors renderRailLayer's band-clustering, then emits one yellow
     *  vertical bar at the leftmost xLeft and one at the rightmost xRight
     *  of each band — i.e. the physical tips of the rail.
     *
     *  Manually-placed examples.TrackBreaker cells are detected first and
     *  any auto-breaker within NEAR_PX of one is suppressed so the user's
     *  placement always wins.
     *
     *  Like the stand/rail layers, this is purely a render-time decoration
     *  — nothing is written to the saved layout.
     * ------------------------------------------------------------------------ */
    function renderBreakerLayer(cells) {
        if (!Array.isArray(cells) || cells.length === 0) return '';

        const TRACK_TYPES_LOCAL = { 'examples.Track': 1, 'examples.Track1': 1, 'examples.Track2': 1 };
        const Y_TOL = 40;
        const NEAR_PX = 18;

        /* 1. Collect manual breakers so we don't duplicate them. */
        const manualBreakers = [];
        for (const c of cells) {
            if (!c || c.type !== 'examples.TrackBreaker') continue;
            const w = (c.size && +c.size.width) || 12;
            const h = (c.size && +c.size.height) || 26;
            manualBreakers.push({
                cx: (c.position && +c.position.x || 0) + w / 2,
                cy: (c.position && +c.position.y || 0) + h / 2
            });
        }
        function manualNear(x, y) {
            for (const m of manualBreakers) {
                if (Math.abs(m.cx - x) < NEAR_PX && Math.abs(m.cy - y) < NEAR_PX) return true;
            }
            return false;
        }

        /* 2. Cluster track cells into rail bands (same as renderRailLayer). */
        const items = [];
        for (const c of cells) {
            if (!c || !TRACK_TYPES_LOCAL[c.type]) continue;
            const x = (c.position && +c.position.x) || 0;
            const y = (c.position && +c.position.y) || 0;
            const w = (c.size && +c.size.width) || 60;
            const h = (c.size && +c.size.height) || 60;
            items.push({ cy: y + h / 2, xLeft: x, xRight: x + w });
        }
        if (!items.length) return '';

        items.sort(function (a, b) { return a.cy - b.cy || a.xLeft - b.xLeft; });
        const bands = [];
        let cur = null;
        for (const it of items) {
            if (!cur || (it.cy - cur.maxY) > Y_TOL) {
                cur = { items: [it], maxY: it.cy };
                bands.push(cur);
            } else {
                cur.items.push(it);
                cur.maxY = it.cy;
            }
        }

        /* 3. Emit a breaker at each rail tip, matching the +/-8 padding that
              renderRailLayer uses so the bar sits at the visible rail edge. */
        const barW = 3;
        const barH = 26;
        let svg = '<g class="sip-breaker-layer" pointer-events="none">';
        for (const band of bands) {
            band.items.sort(function (a, b) { return a.xLeft - b.xLeft; });
            const ys = band.items.map(function (i) { return i.cy; });
            const cy = Math.round(ys[Math.floor(ys.length / 2)]);
            const minX = band.items[0].xLeft - 8;
            const maxX = band.items[band.items.length - 1].xRight + 8;

            const ends = [
                { x: minX, y: cy },
                { x: maxX, y: cy }
            ];
            for (const e of ends) {
                if (manualNear(e.x, e.y)) continue;
                svg += '<rect x="' + (e.x - barW / 2 - 1) + '" y="' + (e.y - barH / 2 - 1) + '" ' +
                    'width="' + (barW + 2) + '" height="' + (barH + 2) + '" fill="#0a0f1e"/>';
                svg += '<rect x="' + (e.x - barW / 2) + '" y="' + (e.y - barH / 2) + '" ' +
                    'width="' + barW + '" height="' + barH + '" fill="#FFD400"/>';
            }
        }
        svg += '</g>';
        return svg;
    }

    /* ------------------------------------------------------------------------ *
     *  POINT-MACHINE CONNECTOR LAYER
     *  ------------------------------------------------------------------------
     *  Auto-draws rail-stub extensions at each endpoint of every PointMachine
     *  diagonal so the diverging track visually attaches to the horizontal
     *  rail band it branches from / merges into.
     *
     *  For each PM cell we:
     *    1. Compute the two endpoints of the diagonal (p1, p2).
     *    2. Find the nearest horizontal rail-band for each endpoint.
     *    3. If the endpoint is close-ish vertically (within PM_ATTACH_TOL),
     *       draw a short horizontal rail stub from the endpoint to the
     *       rail band's centreline Y, extending outward from the PM.
     *
     *  The stub is styled identically to the rail layer (grey body, white
     *  edge lines, sleeper cross-marks) so it reads as a seamless continuation
     *  of the track bed.
     *
     *  Like the stand/breaker layers this is purely render-time decoration —
     *  nothing is written to the saved layout.
     * ------------------------------------------------------------------------ */
    const PM_TYPES = {
        'examples.PointMachine': 'normal',
        'examples.PointMachine1': 'mirror'
    };

    function renderPMConnectorLayer(cells) {
        if (!Array.isArray(cells) || cells.length === 0) return '';

        /* ---- 1. Build rail bands (same logic as renderRailLayer) ---------- */
        const Y_TOL = 40;
        const trackYs = [];
        for (const c of cells) {
            if (!c || !TRACK_TYPES[c.type]) continue;
            const cy = (c.position.y || 0) + ((c.size && c.size.height) || 60) / 2;
            trackYs.push(cy);
        }
        if (!trackYs.length) return '';
        trackYs.sort(function (a, b) { return a - b; });

        const bands = [];
        var cur = [trackYs[0]];
        for (var i = 1; i < trackYs.length; i++) {
            if (trackYs[i] - cur[cur.length - 1] > Y_TOL) {
                bands.push({ cy: Math.round(cur[Math.floor(cur.length / 2)]) });
                cur = [trackYs[i]];
            } else {
                cur.push(trackYs[i]);
            }
        }
        bands.push({ cy: Math.round(cur[Math.floor(cur.length / 2)]) });

        /* Also collect per-band horizontal extents so stubs don't overshoot
           the rail. We store minX / maxX for each band. */
        const bandExtents = bands.map(function () { return { minX: Infinity, maxX: -Infinity }; });
        for (const c of cells) {
            if (!c || !TRACK_TYPES[c.type]) continue;
            var cx = (c.position.x || 0);
            var cw = (c.size && c.size.width) || 60;
            var cy = cx;  // reuse var
            cy = (c.position.y || 0) + ((c.size && c.size.height) || 60) / 2;
            // Find which band
            var bestIdx = 0, bestD = Math.abs(bands[0].cy - cy);
            for (var bi = 1; bi < bands.length; bi++) {
                var d = Math.abs(bands[bi].cy - cy);
                if (d < bestD) { bestIdx = bi; bestD = d; }
            }
            if (cx < bandExtents[bestIdx].minX) bandExtents[bestIdx].minX = cx;
            if (cx + cw > bandExtents[bestIdx].maxX) bandExtents[bestIdx].maxX = cx + cw;
        }

        function findNearestBand(py) {
            var best = 0, bd = Math.abs(bands[0].cy - py);
            for (var j = 1; j < bands.length; j++) {
                var d = Math.abs(bands[j].cy - py);
                if (d < bd) { bd = d; best = j; }
            }
            return { idx: best, cy: bands[best].cy, dist: bd };
        }

        /* ---- 2. For each PM, compute endpoints and draw stubs ------------ */
        var PM_ATTACH_TOL = 120;   // max vertical distance to attempt attachment
        var STUB_OVERSHOOT = 12;   // extra px past the rail edge for overlap
        var BED_H = 18;            // must match renderRailLayer's BED_H

        var svg = '<g class="sip-pm-connector-layer" pointer-events="none">';
        var drewAnything = false;

        for (const c of cells) {
            if (!c || !PM_TYPES[c.type]) continue;
            var x = (c.position && c.position.x) || 0;
            var y = (c.position && c.position.y) || 0;
            var w = (c.size && c.size.width) || 100;
            var h = (c.size && c.size.height) || 60;
            var mirror = PM_TYPES[c.type] === 'mirror';

            /* Diagonal endpoint coordinates (same logic as renderPointMachine) */
            var x1, y1, x2, y2;
            if (mirror) {
                x1 = x; y1 = y;            // top-left
                x2 = x + w; y2 = y + h;    // bottom-right
            } else {
                x1 = x; y1 = y + h;        // bottom-left
                x2 = x + w; y2 = y;        // top-right
            }

            /* Process each endpoint */
            var endpoints = [
                { px: x1, py: y1, side: 'start' },
                { px: x2, py: y2, side: 'end' }
            ];

            for (var ei = 0; ei < endpoints.length; ei++) {
                var ep = endpoints[ei];
                var nb = findNearestBand(ep.py);
                if (nb.dist > PM_ATTACH_TOL) continue;
                if (nb.dist < 3) continue;   // already touching — no stub needed

                var railCy = nb.cy;
                var ext = bandExtents[nb.idx];

                /* Direction the stub extends horizontally: outward from the PM
                   bounding box, toward the rail. */
                var stubDir;   // +1 = rightward,  -1 = leftward
                if (ep.side === 'start') {
                    // Start endpoint is at x1. Stub goes LEFT (away from x2).
                    stubDir = (x2 > x1) ? -1 : 1;
                } else {
                    // End endpoint is at x2. Stub goes RIGHT (away from x1).
                    stubDir = (x2 > x1) ? 1 : -1;
                }

                /* Stub geometry:
                   We draw a short diagonal segment from the PM endpoint down/up
                   to the rail band centreline, continuing the angle briefly,
                   then a small horizontal cap on the rail band. */
                var epX = ep.px;
                var epY = ep.py;
                var railY = railCy;
                var deltaY = railY - epY;

                /* Compute how much horizontal travel is needed at the PM's
                   angle to cover the vertical delta. */
                var diagDx = x2 - x1;
                var diagDy = y2 - y1;
                var diagLen = Math.hypot(diagDx, diagDy);
                if (diagLen < 1) continue;

                /* Unit vector along the PM diagonal direction.
                   Pick the direction away from the PM centre. */
                var ux = diagDx / diagLen;
                var uy = diagDy / diagLen;
                if (ep.side === 'start') { ux = -ux; uy = -uy; }

                /* Parametric: we need uy * t = deltaY  →  t = deltaY / uy
                   If uy is near zero the endpoint is already at rail level. */
                var stubEndX, stubEndY;
                if (Math.abs(uy) > 0.05) {
                    var t = deltaY / uy;
                    if (t < 0) continue;  // rail is behind us — wrong direction
                    stubEndX = epX + ux * t;
                    stubEndY = railY;
                } else {
                    /* Diagonal is nearly horizontal — just run a short horizontal
                       stub to the rail. */
                    stubEndX = epX + stubDir * Math.abs(deltaY);
                    stubEndY = railY;
                }

                /* Rail strip width along the stub (matches PM's railW calc) */
                var railW = Math.max(7, Math.min(12, h * 0.12));
                var ang = Math.atan2(stubEndY - epY, stubEndX - epX);
                var segLen = Math.hypot(stubEndX - epX, stubEndY - epY);
                if (segLen < 2) continue;

                /* Perpendicular offsets for the rail body polygon */
                var pnx = -Math.sin(ang) * railW / 2;
                var pny = Math.cos(ang) * railW / 2;

                /* === Draw the stub === */
                drewAnything = true;

                /* Body polygon */
                var poly = [
                    [epX + pnx, epY + pny],
                    [stubEndX + pnx, stubEndY + pny],
                    [stubEndX - pnx, stubEndY - pny],
                    [epX - pnx, epY - pny]
                ].map(function (p) { return p[0] + ',' + p[1]; }).join(' ');
                svg += '<polygon points="' + poly + '" fill="#7E7E7E"/>';

                /* White edge lines */
                svg += '<line x1="' + (epX + pnx) + '" y1="' + (epY + pny) +
                    '" x2="' + (stubEndX + pnx) + '" y2="' + (stubEndY + pny) +
                    '" stroke="white" stroke-width="1" opacity="0.7"/>';
                svg += '<line x1="' + (epX - pnx) + '" y1="' + (epY - pny) +
                    '" x2="' + (stubEndX - pnx) + '" y2="' + (stubEndY - pny) +
                    '" stroke="white" stroke-width="1" opacity="0.7"/>';

                /* Yellow centreline (matches PM's merge line) */
                svg += '<line x1="' + epX + '" y1="' + epY +
                    '" x2="' + stubEndX + '" y2="' + stubEndY +
                    '" stroke="#FFC919" stroke-width="1.5" opacity="0.8" stroke-linecap="round"/>';

                /* Sleeper cross-marks */
                var sleeperGap = Math.max(3, segLen / 12);
                var sleeperHalf = railW * 0.55;
                var sux = Math.cos(ang);
                var suy = Math.sin(ang);
                var spx = -suy;
                var spy = sux;
                for (var st = sleeperGap; st < segLen - sleeperGap * 0.5; st += sleeperGap) {
                    var scx = epX + sux * st;
                    var scy = epY + suy * st;
                    svg += '<line x1="' + (scx - spx * sleeperHalf) + '" y1="' + (scy - spy * sleeperHalf) +
                        '" x2="' + (scx + spx * sleeperHalf) + '" y2="' + (scy + spy * sleeperHalf) +
                        '" stroke="#515151" stroke-width="1"/>';
                }

                /* Junction dot — a small circle at the rail-band end to
                   visually "anchor" the connection point on the rail. */
                svg += '<circle cx="' + stubEndX + '" cy="' + stubEndY +
                    '" r="' + (railW / 2 + 1) + '" fill="#7E7E7E" stroke="white" stroke-width="0.8"/>';
            }
        }
        svg += '</g>';
        return drewAnything ? svg : '';
    }

    /* ------------------------------------------------------------------------ *
     *  Export
     * ------------------------------------------------------------------------ */
    const SIP = {
        GROUPS, PALETTE: Object.keys(ASSETS), ASSETS,
        spec, makeCell, renderCell, renderIcon, renderYard,
        renderRailLayer, prepareRailContext, renderStandLayer, renderBreakerLayer, renderPMConnectorLayer,
        _uid: uid, _theme: T
    };

    if (typeof module !== 'undefined' && module.exports) module.exports = { SIP };
    if (typeof window !== 'undefined') window.SIP = SIP;

})();
