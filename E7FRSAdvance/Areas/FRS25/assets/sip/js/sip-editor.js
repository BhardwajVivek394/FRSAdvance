/* ==========================================================================
 *  SIP Editor — Aurora Edition (rewritten 15-05-2026)
 *  ------------------------------------------------------------------------
 *  A vanilla-JS yard editor that works DIRECTLY on the legacy Rappid cell
 *  shape ({ type, position, size, attrs, ... }) — every state mutation,
 *  every render and every public getLayout()/setLayout() call uses that
 *  one canonical shape.
 *
 *  Public API (called from Index.cshtml)
 *  -------------------------------------
 *    SipEditor.loadPreset('blank' | 'sample')
 *    SipEditor.undo() / redo()
 *    SipEditor.openImport() / exportLayoutJs() / exportSvg()
 *    SipEditor.closeModal() / copyModal() / downloadModal()
 *    SipEditor.duplicateSelected() / deleteSelected()
 *    SipEditor.toggleFullscreen()
 *    SipEditor.getLayout()       → { cells: [...] }    [OLD RAPPID FORMAT]
 *    SipEditor.setLayout(layout) → accepts { cells: [...] }
 *                                  OR a [{ type, props }, ...] array
 *                                    (auto-converted on the way in)
 *    SipEditor.refreshAssetRegistry()
 *    SipEditor.loadAssetsForType(type, callback)
 *
 *  Depends on:  sip-library.js  (must load first — defines window.SIP)
 *
 *  Internal state shape:
 *    state = {
 *      cells       : [<Rappid cell>, …],
 *      selectedId  : string | null,
 *      viewBox     : "0 0 W H",
 *      history     : [<deep snapshot>, …],
 *      historyIdx  : int,
 *      snap        : { enabled: bool, size: int }
 *    }
 * ========================================================================== */

(function () {
    'use strict';

    if (!window.SIP) {
        console.error('[sip-editor] window.SIP missing — load sip-library.js first.');
        return;
    }

    /* ------------------------------------------------------------------------ *
     *  STATE
     * ------------------------------------------------------------------------ */
    const state = {
        cells: [],
        selectedId: null,
        selectedIds: [],        // multi-selection (includes selectedId as the primary)
        viewBox: '0 0 2000 740',
        history: [],
        historyIdx: -1,
        snap: { enabled: true, size: 10 }
    };

    let canvasEl, gridEl, toolboxEl, inspectorEl, statusEls;

    /* ------------------------------------------------------------------------ *
     *  HELPERS
     * ------------------------------------------------------------------------ */
    function deepClone(x) { return JSON.parse(JSON.stringify(x)); }
    function snap(v) { return state.snap.enabled ? Math.round(v / state.snap.size) * state.snap.size : v; }
    function $(sel, root) { return (root || document).querySelector(sel); }
    function $$(sel, root) { return Array.from((root || document).querySelectorAll(sel)); }
    function cellById(id) { return state.cells.find(c => c.id === id); }
    function selectedCell() { return state.selectedId ? cellById(state.selectedId) : null; }

    /* Visible bounding box of a cell's ICON (what the user sees), which can
       differ from cell.size. Shunts auto-size their body to 44-64px, so the
       selection overlay / handles must track the body, not the tiny cell.size,
       otherwise every handle is crammed into a spot that doesn't match the icon. */
    function cellBox(cell) {
        const x = cell.position.x, y = cell.position.y;
        const w = cell.size.width, h = cell.size.height;
        const t = cell.type;
        if (t === 'examples.Shaunt' || t === 'examples.Shaunt2' || t === 'examples.Shaunt3') {
            const bw = Math.max(44, Math.min(w * 1.5, 64));
            const bh = bw * (23 / 25);
            return { x: x + (w - bw) / 2, y: y + (h - bh) / 2, w: bw, h: bh };
        }
        return { x: x, y: y, w: w, h: h };
    }

    /* Stand geometry for the selected signal/shunt — the attach point (ax,ay),
       the stand TIP (ex,ey), whether it has an adjustable L arm, and the vertical
       factor (corners reach 0.6*len). Mirrors renderStand8 so the drag handle
       sits on the real stand tip and stretches it accurately. */
    const _STAND_CELL_TYPES = { 'examples.Signal': 1, 'examples.SignalShunt': 1, 'examples.RouteCallingSignal': 1, 'examples.Shaunt': 1, 'examples.Shaunt2': 1, 'examples.Shaunt3': 1 };
    function standGeom(cell) {
        if (!cell || !_STAND_CELL_TYPES[cell.type]) return null;
        const sg = (cell.attrs && cell.attrs.signal) || {};
        const sPos = sg.standPos || ((sg.stand === 'none') ? 'none' : 'B');
        if (sPos === 'none') return null;
        const b = cellBox(cell);
        const x = b.x, y = b.y, x2 = b.x + b.w, y2 = b.y + b.h, mx = b.x + b.w / 2, my = b.y + b.h / 2;
        if (sPos === 'FREE') {
            // Both ends freely placed (offsets from the body centre).
            const ax0 = mx + (+sg.standAx || 0), ay0 = my + (+sg.standAy || 0);
            const ex0 = mx + (+sg.standEx || 0), ey0 = my + (+sg.standEy || 0);
            return { sPos: 'FREE', free: true, ax: ax0, ay: ay0, ex: ex0, ey: ey0, hasArm: true, hasAttach: true, bodyCx: mx, bodyCy: my };
        }
        const len = Math.max(8, +sg.standLength || 24);
        const arm = Math.max(0, +sg.standArm || Math.max(10, len * 0.45));
        const drop = String(sg.standDrop || 'down').toLowerCase();
        const vSign = (drop === 'up' || drop === 'top') ? -1 : 1;
        const off = Math.max(0, Math.min(b.h, +sg.standAttachOff || 0));   // attach shift along the body
        let ax, ay, ex, ey, hasArm = false, vFactor = 1, hasAttach = false, attachBase = 0;
        switch (sPos) {
            case 'T': ax = mx; ay = y + off; ex = mx; ey = ay - len; hasAttach = true; attachBase = y; break;
            case 'B': ax = mx; ay = y2 - off; ex = mx; ey = ay + len; hasAttach = true; attachBase = y2; break;
            case 'C': ax = mx; ay = y2 - off; ex = mx + arm; ey = ay + vSign * len; hasArm = true; hasAttach = true; attachBase = y2; break;
            case 'L': ax = x; ay = my; ex = x - arm; ey = my + vSign * len; hasArm = true; break;
            case 'R': ax = x2; ay = my; ex = x2 + arm; ey = my + vSign * len; hasArm = true; break;
            case 'TL': ax = x; ay = y; ex = x - arm; ey = y - len; hasArm = true; break;
            case 'TR': ax = x2; ay = y; ex = x2 + arm; ey = y - len; hasArm = true; break;
            case 'BL': ax = x; ay = y2; ex = x - arm; ey = y2 + len; hasArm = true; break;
            case 'BR': ax = x2; ay = y2; ex = x2 + arm; ey = y2 + len; hasArm = true; break;
            default: ax = mx; ay = y2; ex = mx; ey = y2 + len;
        }
        return { sPos: sPos, ax: ax, ay: ay, ex: ex, ey: ey, hasArm: hasArm, vFactor: vFactor, hasAttach: hasAttach, attachBase: attachBase, bodyH: b.h };
    }

    /* ---- multi-selection helpers (selectedIds) ---------------------------- */
    function isSelected(id) { return state.selectedIds.indexOf(id) !== -1; }
    function toggleInSelection(id) {
        const i = state.selectedIds.indexOf(id);
        if (i === -1) { state.selectedIds.push(id); state.selectedId = id; }
        else { state.selectedIds.splice(i, 1); state.selectedId = state.selectedIds.length ? state.selectedIds[state.selectedIds.length - 1] : null; }
    }
    function clearSelection() { state.selectedIds = []; state.selectedId = null }
    function selectAll() {
        if (!state.cells.length) { toast('Nothing to select.'); return; }
        state.selectedIds = state.cells.map(c => c.id);
        state.selectedId = state.selectedIds[state.selectedIds.length - 1];
        render();
        toast('Selected all ' + state.selectedIds.length + ' element' + (state.selectedIds.length === 1 ? '' : 's') + ' — drag any one to move them together');
    }

    /* ---- zoom (button + programmatic). factor < 1 zooms IN, > 1 zooms OUT.
       Anchors on a screen point (defaults to the canvas centre). ------------- */
    function zoomBy(factor, ox, oy) {
        const svg = canvasEl && canvasEl.querySelector('svg');
        const rect = svg ? svg.getBoundingClientRect() : (canvasEl ? canvasEl.getBoundingClientRect() : null);
        if (!rect) return;
        const vb = parseViewBox(state.viewBox);
        const cx = (ox != null) ? ox : rect.left + rect.width / 2;
        const cy = (oy != null) ? oy : rect.top + rect.height / 2;
        const wx = vb.x + (cx - rect.left) * (vb.w / (rect.width || 1));
        const wy = vb.y + (cy - rect.top) * (vb.h / (rect.height || 1));
        let nw = vb.w * factor, nh = vb.h * factor;
        nw = Math.max(40, Math.min(40000, nw));
        nh = Math.max(40, Math.min(40000, nh));
        const nx = wx - (wx - vb.x) * (nw / vb.w);
        const ny = wy - (wy - vb.y) * (nh / vb.h);
        state.viewBox = Math.round(nx) + ' ' + Math.round(ny) + ' ' + Math.round(nw) + ' ' + Math.round(nh);
        const vbInput = $('#viewbox-input'); if (vbInput) vbInput.value = state.viewBox;
        render();
    }

    /* Dashed box around a group of selected cells (no resize handles). */
    function renderGroupBox(ids) {
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity, n = 0;
        for (const id of ids) {
            const c = cellById(id); if (!c) continue;
            const x = c.position.x, y = c.position.y, w = c.size.width, h = c.size.height;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w; if (y + h > maxY) maxY = y + h; n++;
        }
        if (!n || minX === Infinity) return '';
        const pad = 6;
        return `<g class="selection-overlay"><rect x="${minX - pad}" y="${minY - pad}" ` +
            `width="${maxX - minX + pad * 2}" height="${maxY - minY + pad * 2}" ` +
            `fill="rgba(34,211,238,0.06)" stroke="#22d3ee" stroke-width="1.5" stroke-dasharray="8,5" pointer-events="none"/></g>`;
    }

    /* Floating zoom / fit / select-all buttons over the canvas. Appended to the
       canvas HOST (not #canvas, whose innerHTML is replaced every render). */
    function installEditorZoomControls() {
        const host = canvasEl && canvasEl.parentNode;
        if (!host || host._sipZoomCtrls) return;
        host._sipZoomCtrls = true;
        const cs = window.getComputedStyle(host);
        if (cs && cs.position === 'static') host.style.position = 'relative';
        const bar = document.createElement('div');
        bar.className = 'sip-ed-zoom';
        bar.style.cssText = 'position:absolute;bottom:16px;right:16px;z-index:30;display:flex;gap:6px;align-items:center;';
        const bs = 'background:rgba(15,23,42,0.88);color:#67e8f9;border:1px solid rgba(34,211,238,0.42);border-radius:8px;height:34px;cursor:pointer;line-height:1;display:inline-flex;align-items:center;justify-content:center;font-family:inherit;';
        bar.innerHTML =
            '<button type="button" id="sipEdSelAll" title="Select all (Ctrl+A)" style="' + bs + 'padding:0 12px;font-size:12px;font-weight:600;">Select all</button>' +
            '<button type="button" id="sipEdZoomOut" title="Zoom out" style="' + bs + 'width:34px;font-size:20px;">&#8722;</button>' +
            '<button type="button" id="sipEdZoomFit" title="Fit all to view" style="' + bs + 'padding:0 10px;font-size:12px;font-weight:600;">Fit</button>' +
            '<button type="button" id="sipEdZoomIn" title="Zoom in" style="' + bs + 'width:34px;font-size:20px;">&#43;</button>';
        host.appendChild(bar);
        const g = id => document.getElementById(id);
        if (g('sipEdZoomIn')) g('sipEdZoomIn').addEventListener('click', () => zoomBy(0.8));
        if (g('sipEdZoomOut')) g('sipEdZoomOut').addEventListener('click', () => zoomBy(1.25));
        if (g('sipEdZoomFit')) g('sipEdZoomFit').addEventListener('click', () => fitAll());
        if (g('sipEdSelAll')) g('sipEdSelAll').addEventListener('click', () => selectAll());
    }

    /* ------------------------------------------------------------------------ *
     *  SIGNAL → BACKGROUND AUTO-FIT
     *  ------------------------------------------------------------------------
     *  When a signal lamp is dropped (newly spawned, or finished being dragged)
     *  and its centre lands inside the bounds of an examples.SignalBackground
     *  cell, we resize the lamp to match the background's height and centre
     *  it vertically. The user's horizontal position is preserved (just
     *  clamped inside the background), so multiple lamps can be lined up
     *  along one background.
     * ------------------------------------------------------------------------ */
    const LAMP_TYPES_FOR_SNAP = {
        'examples.Signald90': 1,
        'examples.Signal45': 1,
        'examples.Signal90': 1,
        'examples.Signald45': 1
    };

    function backgroundUnderCell(cell) {
        if (!cell || !LAMP_TYPES_FOR_SNAP[cell.type]) return null;
        const cx = cell.position.x + cell.size.width / 2;
        const cy = cell.position.y + cell.size.height / 2;
        let best = null, bestZ = -Infinity;
        for (const b of state.cells) {
            if (b.id === cell.id) continue;
            if (b.type !== 'examples.SignalBackground') continue;
            const bx = b.position.x, by = b.position.y;
            const bw = b.size.width, bh = b.size.height;
            if (cx >= bx && cx <= bx + bw && cy >= by && cy <= by + bh) {
                const z = b.z || 0;
                if (z >= bestZ) { best = b; bestZ = z; }
            }
        }
        return best;
    }

    function fitLampToBackground(lamp, bg) {
        const pad = 3;                                  // breathing room top/bottom
        const targetH = Math.max(8, bg.size.height - pad * 2);
        const targetW = targetH;                        // square footprint = circular lamp
        lamp.size.width = targetW;
        lamp.size.height = targetH;
        // Centre Y on the background tray
        lamp.position.y = bg.position.y + (bg.size.height - targetH) / 2;
        // Keep X where the user dropped, but clamp inside the bg horizontally
        const cx = lamp.position.x + targetW / 2;
        const minCx = bg.position.x + targetW / 2;
        const maxCx = bg.position.x + bg.size.width - targetW / 2;
        const clampedX = Math.max(minCx, Math.min(maxCx, cx)) - targetW / 2;
        lamp.position.x = snap(clampedX);
        // Make sure the lamp draws above its tray
        lamp.z = (bg.z || 0) + 1;
    }

    function maybeSnapToBackground(cell) {
        const bg = backgroundUnderCell(cell);
        if (bg) { fitLampToBackground(cell, bg); return true; }
        return false;
    }

    /* Map a label-position direction to a preset (dx, dy) nudge, scaled to the
     * cell's size. dx/dy are offsets applied on top of each renderer's default
     * label anchor (which for most assets sits just below the asset), so the
     * presets push the label clearly toward the chosen side. */
    function labelDirOffset(dir, cell) {
        const w = (cell.size && cell.size.width) || 40;
        const h = (cell.size && cell.size.height) || 24;
        const hx = Math.round(w / 2 + 10);   // horizontal reach past the edge
        const up = Math.round(h + 18);        // lift above the asset
        const dn = 6;                          // small extra drop below default
        switch (dir) {
            case 'above': return { dx: 0, dy: -up };
            case 'below': return { dx: 0, dy: dn };
            case 'left': return { dx: -hx, dy: -Math.round(h / 2) };
            case 'right': return { dx: hx, dy: -Math.round(h / 2) };
            case 'above-left': return { dx: -hx, dy: -up };
            case 'above-right': return { dx: hx, dy: -up };
            case 'below-left': return { dx: -hx, dy: dn };
            case 'below-right': return { dx: hx, dy: dn };
            default: return { dx: 0, dy: 0 };
        }
    }

    function parseViewBox(vb) {
        const a = String(vb || state.viewBox).split(/\s+/).map(Number);
        return { x: a[0] || 0, y: a[1] || 0, w: a[2] || 2000, h: a[3] || 740 };
    }

    function clientToWorld(evt) {
        const svg = canvasEl.querySelector('svg');
        if (!svg) return { x: 0, y: 0 };
        const rect = svg.getBoundingClientRect();
        const vb = parseViewBox(state.viewBox);
        const sx = vb.w / rect.width;
        const sy = vb.h / rect.height;
        return {
            x: vb.x + (evt.clientX - rect.left) * sx,
            y: vb.y + (evt.clientY - rect.top) * sy
        };
    }

    function toast(msg) {
        const t = document.getElementById('toast');
        if (!t) return;
        t.textContent = msg;
        t.classList.add('show');
        clearTimeout(t._tid);
        t._tid = setTimeout(() => t.classList.remove('show'), 2200);
    }

    /* ------------------------------------------------------------------------ *
     *  HISTORY (undo / redo)
     * ------------------------------------------------------------------------ */
    function pushHistory() {
        // Drop any "future" history after the current cursor before pushing.
        state.history = state.history.slice(0, state.historyIdx + 1);
        state.history.push(JSON.stringify({ cells: state.cells, viewBox: state.viewBox }));
        state.historyIdx = state.history.length - 1;
        if (state.history.length > 200) {
            state.history.shift();
            state.historyIdx--;
        }
        refreshHistoryButtons();
    }
    function undo() {
        if (state.historyIdx <= 0) return;
        state.historyIdx--;
        applyHistory();
    }
    function redo() {
        if (state.historyIdx >= state.history.length - 1) return;
        state.historyIdx++;
        applyHistory();
    }
    function applyHistory() {
        const snapdata = JSON.parse(state.history[state.historyIdx]);
        state.cells = snapdata.cells;
        state.viewBox = snapdata.viewBox;
        state.selectedId = null; state.selectedIds = [];
        const vbInput = $('#viewbox-input');
        if (vbInput) vbInput.value = state.viewBox;
        render();
        refreshHistoryButtons();
    }
    function refreshHistoryButtons() {
        const u = $('#undo-btn'), r = $('#redo-btn');
        if (u) u.disabled = state.historyIdx <= 0;
        if (r) r.disabled = state.historyIdx >= state.history.length - 1;
    }

    /* ------------------------------------------------------------------------ *
     *  RENDER — paint the whole canvas from state.cells
     * ------------------------------------------------------------------------ */
    function render(skipInspector) {
        if (typeof SIP.prepareRailContext === 'function') SIP.prepareRailContext(state.cells);   // ← ADD THIS LINE

        const fragments = state.cells.map(c => {
            const inner = SIP.renderCell(c);
            const sel = (isSelected(c.id) || c.id === state.selectedId) ? ' selected' : '';
            return `<g data-eid="${c.id}" class="editable${sel}">${inner}</g>`;
        });

        const vb = parseViewBox(state.viewBox);
        const grid = makeGrid(vb);
        // Rail layer sits BETWEEN grid and individual cells so the continuous
        // rail passes behind tick marks / labels and curves stay on top.
        const railLayer = (typeof SIP.renderRailLayer === 'function')
            ? SIP.renderRailLayer(state.cells)
            : '';
        const standLayer = (typeof SIP.renderStandLayer === 'function')
            ? SIP.renderStandLayer(state.cells)
            : '';
        const breakerLayer = (typeof SIP.renderBreakerLayer === 'function')
            ? SIP.renderBreakerLayer(state.cells)
            : '';
        const pmConnLayer = (typeof SIP.renderPMConnectorLayer === 'function')
            ? SIP.renderPMConnectorLayer(state.cells)
            : '';
        // Group box (dashed, no handles) for a multi-selection; resize handles
        // for a single selection.
        const sel = (state.selectedIds.length > 1)
            ? renderGroupBox(state.selectedIds)
            : (state.selectedId ? renderSelectionBox(cellById(state.selectedId)) : '');

        canvasEl.innerHTML =
            `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${state.viewBox}" ` +
            `class="sip-yard" preserveAspectRatio="xMidYMid meet">` +
            /* v618.0: themed flat canvas (SIP.renderBackground) */
            ((typeof SIP.renderBackground === 'function') ? SIP.renderBackground() : `<rect width="100%" height="100%" fill="#0E1828"/>`) +
            grid +
            railLayer +
            pmConnLayer +
            standLayer +
            breakerLayer +
            fragments.join('') +
            sel +
            `</svg>`;

        placeLabelHandle();
        if (!skipInspector) renderInspector();
        updateStatusBar();
        refreshHistoryButtons();
    }

    /* Parse an SVG markup fragment into REAL SVG-namespaced nodes. Setting
       innerHTML / insertAdjacentHTML directly on an SVG element parses in the
       HTML namespace in many browsers (elements don't render / aren't
       clickable). Wrapping the fragment in <svg> inside a plain <div> uses the
       lenient HTML parser's foreign-content mode — the SAME path the full
       render() uses (canvasEl is a div) — so the children come out correctly
       SVG-namespaced and it tolerates the same markup the full render does. */
    const _SVG_NS = 'http://www.w3.org/2000/svg';
    function _parseSvgWrap(markup) {
        try {
            const tmp = document.createElement('div');
            tmp.innerHTML = '<svg xmlns="' + _SVG_NS + '">' + markup + '</svg>';
            return tmp.querySelector('svg');   // children are SVG-namespaced nodes
        } catch (e) { return null; }
    }
    function _replaceSvgChildren(el, markup) {
        const wrap = _parseSvgWrap(markup);
        if (!wrap) return false;
        while (el.firstChild) el.removeChild(el.firstChild);
        const kids = [];
        for (let i = 0; i < wrap.childNodes.length; i++) kids.push(wrap.childNodes[i]);
        for (const k of kids) el.appendChild(document.importNode(k, true));
        return true;
    }

    /* Live, in-place update of ONE cell + the selection overlay during a drag /
       resize / rotate — instead of rebuilding the whole SVG (grid, rails, every
       cell) each mouse-move, which caused heavy flicker. A full render() runs
       once on mouse-up to reconcile everything. */
    function renderCellLive(c) {
        const svg = canvasEl && canvasEl.querySelector('svg');
        if (!svg || !c) { render(true); return; }
        const g = svg.querySelector('g.editable[data-eid="' + cssEsc(c.id) + '"]');
        if (!g) { render(true); return; }
        if (!_replaceSvgChildren(g, SIP.renderCell(c))) { render(true); return; }
        const ov = svg.querySelector('.selection-overlay');
        if (ov && ov.parentNode) ov.parentNode.removeChild(ov);
        const box = (state.selectedIds.length > 1) ? renderGroupBox(state.selectedIds)
            : renderSelectionBox(c);
        if (box) {
            const w = _parseSvgWrap(box);
            const node = w && w.firstElementChild;
            if (node) svg.appendChild(document.importNode(node, true));
        }
    }

    /* Live update of SEVERAL cells + the group box (for group drag). */
    function renderCellsLive(ids) {
        const svg = canvasEl && canvasEl.querySelector('svg');
        if (!svg) { render(true); return; }
        for (const id of ids) {
            const c = cellById(id); if (!c) continue;
            const g = svg.querySelector('g.editable[data-eid="' + cssEsc(id) + '"]');
            if (!g || !_replaceSvgChildren(g, SIP.renderCell(c))) { render(true); return; }
        }
        const ov = svg.querySelector('.selection-overlay');
        if (ov && ov.parentNode) ov.parentNode.removeChild(ov);
        const box = (state.selectedIds.length > 1) ? renderGroupBox(state.selectedIds)
            : (state.selectedId ? renderSelectionBox(cellById(state.selectedId)) : '');
        if (box) {
            const w = _parseSvgWrap(box);
            const node = w && w.firstElementChild;
            if (node) svg.appendChild(document.importNode(node, true));
        }
    }

    /* After the SVG is in the DOM, find the SELECTED cell's actual label glyph
     * and lay an interactive highlight + grab handle directly over it. This
     * makes "drag the label" land exactly on the real text wherever each
     * renderer happens to draw it (below, beside, centred…), instead of an
     * assumed anchor — so the label tracks the pointer 1:1. */
    function placeLabelHandle() {
        if (state.selectedIds.length > 1) return;   // no label handle during a group selection
        if (!state.selectedId) return;
        const c = cellById(state.selectedId);
        if (!c) return;
        const txt = c.attrs && c.attrs.label && c.attrs.label.text;
        if (txt == null || String(txt) === '') return;

        const svg = canvasEl.querySelector('svg');
        const overlay = canvasEl.querySelector('.selection-overlay');
        if (!svg || !overlay) return;

        const grp = svg.querySelector(`g.editable[data-eid="${cssEsc(state.selectedId)}"]`);
        if (!grp) return;

        // Prefer the composite label group (badge/pill + text) so the handle
        // wraps the whole label unit. Fall back to the bare <text> glyph.
        let target = grp.querySelector('g.sip-label');
        if (!target) {
            const want = String(txt);
            const texts = grp.querySelectorAll('text');
            for (const t of texts) {
                if (t.textContent === want) { target = t; }   // last match = top fill glyph
            }
        }
        if (!target) return;

        let bb;
        try { bb = target.getBBox(); } catch (e) { return; }
        if (!bb || (!bb.width && !bb.height)) return;

        const pad = 3;
        const NS = 'http://www.w3.org/2000/svg';
        // Highlight box around the actual label
        const hl = document.createElementNS(NS, 'rect');
        hl.setAttribute('x', bb.x - pad);
        hl.setAttribute('y', bb.y - pad);
        hl.setAttribute('width', bb.width + pad * 2);
        hl.setAttribute('height', bb.height + pad * 2);
        hl.setAttribute('rx', '3');
        hl.setAttribute('fill', 'rgba(251,191,36,0.12)');
        hl.setAttribute('stroke', '#fbbf24');
        hl.setAttribute('stroke-width', '1');
        hl.setAttribute('stroke-dasharray', '4,3');
        hl.setAttribute('class', 'label-handle');
        hl.setAttribute('data-handle', 'label');
        hl.setAttribute('style', 'cursor:move');
        overlay.appendChild(hl);

        // Small grab dot at the label's top-right for a clear affordance
        const dot = document.createElementNS(NS, 'circle');
        dot.setAttribute('cx', bb.x + bb.width + pad);
        dot.setAttribute('cy', bb.y - pad);
        dot.setAttribute('r', '4');
        dot.setAttribute('fill', '#0f172a');
        dot.setAttribute('stroke', '#fbbf24');
        dot.setAttribute('stroke-width', '2');
        dot.setAttribute('class', 'label-handle');
        dot.setAttribute('data-handle', 'label');
        dot.setAttribute('style', 'cursor:move');
        overlay.appendChild(dot);
    }

    function cssEsc(s) {
        return String(s).replace(/["\\]/g, '\\$&');
    }

    function makeGrid(vb) {
        const step = 50;
        const MINOR = 'rgba(127,197,255,0.05)';
        const MAJOR = 'rgba(127,197,255,0.12)';
        let lines = '';
        for (let x = Math.ceil(vb.x / step) * step; x < vb.x + vb.w; x += step) {
            const major = x % 200 === 0;
            lines += `<line x1="${x}" y1="${vb.y}" x2="${x}" y2="${vb.y + vb.h}" stroke="${major ? MAJOR : MINOR}" stroke-width="1"/>`;
        }
        for (let y = Math.ceil(vb.y / step) * step; y < vb.y + vb.h; y += step) {
            const major = y % 200 === 0;
            lines += `<line x1="${vb.x}" y1="${y}" x2="${vb.x + vb.w}" y2="${y}" stroke="${major ? MAJOR : MINOR}" stroke-width="1"/>`;
        }
        return `<g class="grid" pointer-events="none">${lines}</g>`;
    }

    function renderSelectionBox(cell) {
        if (!cell) return '';
        const _b = cellBox(cell);   // visible icon box (handles align with what's drawn)
        const x = _b.x, y = _b.y, w = _b.w, h = _b.h;
        const x2 = x + w, y2 = y + h;
        const mx = x + w / 2, my = y + h / 2;
        // Keep handles a constant ON-SCREEN size at any zoom so they are always
        // easy to grab. u = viewBox units per screen pixel (meet-fit scale).
        let u = 1;
        try {
            const _vb = parseViewBox(state.viewBox);
            const _rc = canvasEl.getBoundingClientRect();
            const _sc = Math.min((_rc.width || 1) / _vb.w, (_rc.height || 1) / _vb.h);
            if (_sc > 0 && isFinite(_sc)) u = 1 / _sc;
        } catch (e) { }
        const R = 6 * u;      // visible handle dot (~6px)
        // Resize-handle grab area: generous, but capped to the cell size so the
        // 8 side/corner targets don't overlap on small stencils (accurate pick).
        const HIT = Math.max(6 * u, Math.min(14 * u, w * 0.42, h * 0.42));
        const HIT_ISO = 14 * u;   // isolated handles (rotate / stand) stay big
        const SW = 1.6 * u;   // stroke width

        // Each handle gets a data-handle="<dir>" used by the mousedown router.
        let svg = `<g class="selection-overlay">`;
        // Dashed bounding box (passive)
        svg += `<rect x="${x - 2 * u}" y="${y - 2 * u}" width="${w + 4 * u}" height="${h + 4 * u}" ` +
            `fill="none" stroke="#22d3ee" stroke-width="${SW}" stroke-dasharray="${6 * u},${4 * u}" pointer-events="none"/>`;
        // 8 resize handles — every side + corner. Big transparent hit target so
        // you can grab near the dot, with a small visible dot on top. Shunts are
        // a fixed-size symbol (body auto-sized), so they get no resize handles —
        // just a clean box + move + rotate + stand handle.
        const _isShunt = (cell.type === 'examples.Shaunt' || cell.type === 'examples.Shaunt2' || cell.type === 'examples.Shaunt3');
        if (!_isShunt) {
            const handles = [
                ['nw', x, y, 'nwse-resize'], ['n', mx, y, 'ns-resize'], ['ne', x2, y, 'nesw-resize'],
                ['e', x2, my, 'ew-resize'], ['se', x2, y2, 'nwse-resize'], ['s', mx, y2, 'ns-resize'],
                ['sw', x, y2, 'nesw-resize'], ['w', x, my, 'ew-resize']
            ];
            for (const hdl of handles) {
                svg += `<circle class="resize-handle" data-handle="${hdl[0]}" cx="${hdl[1]}" cy="${hdl[2]}" r="${HIT}" ` +
                    `fill="transparent" style="cursor:${hdl[3]}"/>`;
                svg += `<circle cx="${hdl[1]}" cy="${hdl[2]}" r="${R}" fill="#0f172a" stroke="#22d3ee" stroke-width="${SW}" pointer-events="none"/>`;
            }
        }
        // Stand-length handle (AMBER) — adjusts only the stand's drop, separate
        // from the blue head-resize handles. Shown for signals that have a stand.
        const _sgm = standGeom(cell);
        if (_sgm) {
            const _cur = _sgm.hasArm ? 'move' : 'ns-resize';   // L stands stretch in 2D
            svg += `<line x1="${_sgm.ax}" y1="${_sgm.ay}" x2="${_sgm.ex}" y2="${_sgm.ey}" stroke="#f59e0b" stroke-width="${SW}" stroke-dasharray="${3 * u},${3 * u}" pointer-events="none"/>`;
            // Amber = stand TIP (stretch height / arm).
            svg += `<circle class="stand-handle" data-handle="stand" cx="${_sgm.ex}" cy="${_sgm.ey}" r="${HIT_ISO}" fill="transparent" style="cursor:${_cur}"/>`;
            svg += `<rect x="${_sgm.ex - R}" y="${_sgm.ey - R}" width="${2 * R}" height="${2 * R}" rx="${2 * u}" fill="#0f172a" stroke="#f59e0b" stroke-width="${SW}" pointer-events="none"/>`;
            // Green = ATTACH point (drag along the body to attach at centre / lower).
            if (_sgm.hasAttach) {
                svg += `<circle class="attach-handle" data-handle="attach" cx="${_sgm.ax}" cy="${_sgm.ay}" r="${HIT_ISO}" fill="transparent" style="cursor:ns-resize"/>`;
                svg += `<circle cx="${_sgm.ax}" cy="${_sgm.ay}" r="${R}" fill="#0f172a" stroke="#22c55e" stroke-width="${SW}" pointer-events="none"/>`;
            }
        }
        // Rotate handle (also a big hit target), on a stalk above top-centre.
        const rotY = y - 26 * u;
        const rotWrap = (cell.angle && Math.abs(cell.angle) > 0.01) ? `<g transform="rotate(${cell.angle} ${mx} ${my})">` : '<g>';
        svg += rotWrap +
            `<line x1="${mx}" y1="${y}" x2="${mx}" y2="${rotY}" stroke="#22d3ee" stroke-width="${SW}" pointer-events="none"/>` +
            `<circle class="rotate-handle" data-handle="rotate" cx="${mx}" cy="${rotY}" r="${HIT_ISO}" fill="transparent" style="cursor:grab"/>` +
            `<circle cx="${mx}" cy="${rotY}" r="${R + 1 * u}" fill="#0f172a" stroke="#22d3ee" stroke-width="${SW}" pointer-events="none"/>` +
            `<text x="${mx}" y="${rotY + 3.4 * u}" text-anchor="middle" font-size="${10 * u}" fill="#22d3ee" ` +
            `pointer-events="none" style="font-family:system-ui;">↻</text>` +
            `</g>`;
        svg += `</g>`;
        return svg;
    }

    function updateStatusBar() {
        const ec = $('#element-count');
        const sl = $('#selection-label');
        if (ec) ec.textContent = state.cells.length + ' element' + (state.cells.length === 1 ? '' : 's');
        if (sl) {
            if (state.selectedIds.length > 1) sl.textContent = state.selectedIds.length + ' selected';
            else if (!state.selectedId) sl.textContent = 'No selection';
            else {
                const c = selectedCell();
                const s = c && SIP.spec(c.type);
                sl.textContent = c ? ('Selected: ' + (s ? s.label : c.type)) : 'No selection';
            }
        }
    }

    /* ------------------------------------------------------------------------ *
     *  TOOLBOX — build the asset palette
     * ------------------------------------------------------------------------ */
    /* ---- Recent-types tracker (for "Recently Used" section) ---- */
    const recentTypes = [];

    function buildToolbox() {
        if (!toolboxEl) return;

        /* ---- Search box at the top ---- */
        let html = `<div class="field" style="margin-bottom:10px;">` +
            `<input type="text" id="toolbox-search" placeholder="Search assets…" /></div>`;

        /* ---- "Recently Used" section (shows after first use) ---- */
        if (recentTypes.length > 0) {
            html += `<div class="tb-group" data-tbgroup="recent">`;
            html += `<div class="tb-group-h" style="color:#22d3ee;">Recently Used</div>`;
            html += `<div class="tb-group-items">`;
            for (const type of recentTypes) {
                const s = SIP.spec(type);
                if (!s) continue;
                html += `<button class="tb-item" data-type="${type}" title="${s.label}">` +
                    `<div class="tb-icon">${s.icon()}</div>` +
                    `<div class="tb-name">${s.label}</div>` +
                    `</button>`;
            }
            html += `</div></div>`;
        }

        /* ---- Standard groups (same as before) ---- */
        for (const grp of SIP.GROUPS) {
            const items = SIP.PALETTE.filter(t => {
                const s = SIP.spec(t);
                return s && s.group === grp.key && !s.hidden;
            });
            if (!items.length) continue;
            html += `<div class="tb-group" data-tbgroup="${grp.key}">`;
            html += `<div class="tb-group-h">${grp.label}</div>`;
            html += `<div class="tb-group-items">`;
            for (const type of items) {
                const s = SIP.spec(type);
                html += `<button class="tb-item" data-type="${type}" title="${s.label}">` +
                    `<div class="tb-icon">${s.icon()}</div>` +
                    `<div class="tb-name">${s.label}</div>` +
                    `</button>`;
            }
            html += `</div></div>`;
        }
        toolboxEl.innerHTML = html;

        /* ---- Wire click/drag on each toolbox button ---- */
        toolboxEl.querySelectorAll('.tb-item').forEach(btn => {
            btn.addEventListener('mousedown', (e) => {
                e.preventDefault();
                startToolboxDrag(btn.getAttribute('data-type'), e);
            });
        });

        /* ---- Wire the search filter ---- */
        const searchInput = toolboxEl.querySelector('#toolbox-search');
        if (searchInput) {
            searchInput.addEventListener('input', function () {
                const q = this.value.toLowerCase().trim();
                toolboxEl.querySelectorAll('.tb-item').forEach(btn => {
                    const name = (btn.getAttribute('title') || '').toLowerCase();
                    btn.style.display = (!q || name.indexOf(q) !== -1) ? '' : 'none';
                });
                // Hide groups where ALL items are hidden
                toolboxEl.querySelectorAll('.tb-group').forEach(grp => {
                    const visible = grp.querySelectorAll('.tb-item:not([style*="display: none"])');
                    grp.style.display = visible.length ? '' : 'none';
                });
            });
        }
    }

    /* ---- Drag-from-toolbox: press, drag ghost, release on canvas ---- */
    let toolboxDrag = null;

    function startToolboxDrag(type, evt) {
        const spec = SIP.spec(type);
        if (!spec) return;

        // Create a small ghost element that follows the cursor
        const ghost = document.createElement('div');
        ghost.innerHTML = spec.icon();
        ghost.style.cssText =
            'position:fixed;pointer-events:none;opacity:0.75;z-index:9999;' +
            'width:56px;height:36px;filter:brightness(1.5);';
        ghost.style.left = (evt.clientX - 28) + 'px';
        ghost.style.top = (evt.clientY - 18) + 'px';
        document.body.appendChild(ghost);
        toolboxDrag = { type: type, ghostEl: ghost, moved: false, startX: evt.clientX, startY: evt.clientY };

        function onMove(e) {
            ghost.style.left = (e.clientX - 28) + 'px';
            ghost.style.top = (e.clientY - 18) + 'px';
            // Mark as moved if the user dragged more than 5px
            if (Math.abs(e.clientX - toolboxDrag.startX) > 5 ||
                Math.abs(e.clientY - toolboxDrag.startY) > 5) {
                toolboxDrag.moved = true;
            }
        }
        function onUp(e) {
            document.removeEventListener('mousemove', onMove);
            document.removeEventListener('mouseup', onUp);
            ghost.remove();

            // Check if released over the canvas
            const svgEl = canvasEl ? canvasEl.querySelector('svg') : null;
            if (svgEl && toolboxDrag.moved) {
                const rect = svgEl.getBoundingClientRect();
                if (e.clientX >= rect.left && e.clientX <= rect.right &&
                    e.clientY >= rect.top && e.clientY <= rect.bottom) {
                    // Place at the drop position
                    addCellAtPosition(type, e);
                    toolboxDrag = null;
                    return;
                }
            }
            // Fallback: click (no drag or dropped outside canvas) → add at center
            addCellFromToolbox(type);
            toolboxDrag = null;
        }
        document.addEventListener('mousemove', onMove);
        document.addEventListener('mouseup', onUp);
    }

    /* Place a new cell at the exact mouse position on the canvas */
    function addCellAtPosition(type, evt) {
        const pos = clientToWorld(evt);
        const c = SIP.makeCell(type, snap(pos.x), snap(pos.y));
        c.z = (state.cells.reduce((m, cc) => Math.max(m, cc.z || 0), 0) + 1);
        state.cells.push(c);
        const snapped = maybeSnapToBackground(c);
        state.selectedId = c.id; state.selectedIds = [c.id];
        trackRecentType(type);
        pushHistory();
        render();
        toast('Added ' + SIP.spec(type).label + (snapped ? ' (snapped to background)' : ''));
    }

    /* Track recently used types for the "Recently Used" toolbox section */
    function trackRecentType(type) {
        const idx = recentTypes.indexOf(type);
        if (idx > -1) recentTypes.splice(idx, 1);  // remove duplicate
        recentTypes.unshift(type);                   // add to front
        if (recentTypes.length > 5) recentTypes.pop(); // max 5
        buildToolbox();                               // refresh toolbox to show updated "Recent"
    }

    function addCellFromToolbox(type) {
        const vb = parseViewBox(state.viewBox);
        const cx = snap(vb.x + vb.w / 2);
        const cy = snap(vb.y + vb.h / 2);
        const c = SIP.makeCell(type, cx, cy);
        c.z = (state.cells.reduce((m, cc) => Math.max(m, cc.z || 0), 0) + 1);
        state.cells.push(c);
        // If a signal lamp was spawned on top of a SignalBackground that's
        // already at canvas centre, auto-fit it to the tray.
        const snapped = maybeSnapToBackground(c);
        state.selectedId = c.id; state.selectedIds = [c.id];
        trackRecentType(type);
        pushHistory();
        render();
        toast('Added ' + SIP.spec(type).label + (snapped ? ' (snapped to background)' : ''));
    }

    /* ------------------------------------------------------------------------ *
     *  INSPECTOR — properties of the selected cell
     * ------------------------------------------------------------------------ */
    let assetRegistryCache = {}; // stencilType → [{id, name}]

    function renderInspector() {
        if (!inspectorEl) return;
        const c = selectedCell();
        if (!c) {
            inspectorEl.innerHTML = `<p class="muted">Nothing selected.<br/><br/>Click an element on the canvas, or add one from the toolbox.</p>`;
            return;
        }
        const s = SIP.spec(c.type);
        const labelTxt = (c.attrs && c.attrs.label && c.attrs.label.text != null)
            ? String(c.attrs.label.text) : '';
        const labelFill = (c.attrs && c.attrs.label && c.attrs.label.fill) || '';
        const labelSize = (c.attrs && c.attrs.label && c.attrs.label.fontSize) || '';

        let html = '';
        html += `<div class="insp-row"><b>Type</b><span>${s ? s.label : c.type}</span></div>`;
        html += `<div class="insp-row"><b>Stencil</b><code>${c.type}</code></div>`;
        html += `<div class="field"><label>Asset name (label)</label>` +
            `<input type="text" id="insp-label" value="${escAttr(labelTxt)}" placeholder="e.g. 3T1, S18 RG, 06"/></div>`;
        html += `<div class="field"><label>Asset (from registry)</label>` +
            `<select id="insp-asset-select"><option value="">— loading —</option></select></div>`;
        html += `<div class="insp-grid">`;
        html += `<div class="field"><label>X</label><input type="number" id="insp-x" value="${c.position.x}" step="${state.snap.size}"/></div>`;
        html += `<div class="field"><label>Y</label><input type="number" id="insp-y" value="${c.position.y}" step="${state.snap.size}"/></div>`;
        html += `<div class="field"><label>Width</label><input type="number" id="insp-w" value="${c.size.width}" step="10"/></div>`;
        html += `<div class="field"><label>Height</label><input type="number" id="insp-h" value="${c.size.height}" step="10"/></div>`;
        html += `</div>`;
        html += `<div class="insp-grid">`;
        html += `<div class="field"><label>Label colour</label><input type="text" id="insp-lf" value="${escAttr(labelFill)}" placeholder="#d7d7d7"/></div>`;
        html += `<div class="field"><label>Label size</label><input type="number" id="insp-ls" value="${labelSize || 14}" min="6" max="40"/></div>`;
        html += `</div>`;
        const labelDx = (c.attrs && c.attrs.label && c.attrs.label.dx) || 0;
        const labelDy = (c.attrs && c.attrs.label && c.attrs.label.dy) || 0;
        const labelDir = (c.attrs && c.attrs.label && c.attrs.label.dir) || 'custom';
        const _dirOpt = (val, txt) => `<option value="${val}" ${labelDir === val ? 'selected' : ''}>${txt}</option>`;
        html += `<div class="field"><label>Label position</label>` +
            `<select id="insp-ldir">` +
            _dirOpt('custom', 'Custom (drag / offsets)') +
            _dirOpt('above', 'Above') +
            _dirOpt('below', 'Below') +
            _dirOpt('left', 'Left') +
            _dirOpt('right', 'Right') +
            _dirOpt('above-left', 'Above-left') +
            _dirOpt('above-right', 'Above-right') +
            _dirOpt('below-left', 'Below-left') +
            _dirOpt('below-right', 'Below-right') +
            `</select></div>`;
        html += `<div class="insp-grid">`;
        html += `<div class="field"><label>Label X offset</label><input type="number" id="insp-ldx" value="${labelDx}" step="1"/></div>`;
        html += `<div class="field"><label>Label Y offset</label><input type="number" id="insp-ldy" value="${labelDy}" step="1"/></div>`;
        html += `</div>`;
        html += `<div style="display:flex;align-items:center;gap:8px;margin:2px 0 6px;">` +
            `<button type="button" id="insp-lreset" style="background:rgba(255,46,46,0.12);` +
            `border:1px solid rgba(255,46,46,0.4);color:#ff8a8a;border-radius:6px;` +
            `padding:4px 10px;cursor:pointer;font-size:12px;">Reset label position</button>` +
            `<span style="font-size:11px;color:#64748b;">or drag the label on the canvas</span></div>`;

        /* ---- Composite-signal-only fields (Signal composite + Shunts) ---- */
        const SIG_STAND_TYPES = {
            'examples.Signal': 1,
            'examples.SignalShunt': 1,
            'examples.RouteCallingSignal': 1,
            'examples.Shaunt': 1,
            'examples.Shaunt2': 1,
            'examples.Shaunt3': 1
        };
        if (SIG_STAND_TYPES[c.type]) {
            const sigProps = (c.attrs && c.attrs.signal) || {};
            const standLen = +sigProps.standLength || 30;
            html += `<div class="tb-group-h" style="margin-top:14px">` +
                (c.type === 'examples.Signal' ? 'Signal' :
                    c.type === 'examples.SignalShunt' ? 'Signal + Shunt' :
                        c.type === 'examples.RouteCallingSignal' ? 'Route / Calling Signal' :
                            c.type === 'examples.Shaunt' ? 'Shunt · Proceed' :
                                c.type === 'examples.Shaunt2' ? 'Shunt · Diverge' :
                                    c.type === 'examples.Shaunt3' ? 'Shunt · Off' :
                                        'Shunt') +
                `</div>`;

            /* Lamp/lit controls apply to main signal and combined signal+shunt */
            if (c.type === 'examples.Signal' || c.type === 'examples.SignalShunt') {
                const defaultLamps = c.type === 'examples.SignalShunt' ? 'RYG' : 'BBB';
                const lampsStr = (typeof sigProps.lamps === 'string' && sigProps.lamps) || defaultLamps;
                const litStr = (typeof sigProps.lit === 'string' && sigProps.lit) || ((c.attrs && c.attrs.lit) || '');

                const LAMP_COLORS = { B: '#aaaaaa', R: '#FF2E2E', Y: '#FFD400', G: '#22D142', X: '#c8a800' };
                const LAMP_NAMES = { B: 'Blank', R: 'Red', Y: 'Yellow', G: 'Green', X: 'Dbl Yellow' };
                const LAMP_ORDER = ['B', 'R', 'Y', 'G', 'X'];

                html += `<div class="insp-grid">` +
                    `<div class="field"><label>No. of main aspects</label>` +
                    `<input type="number" id="insp-sig-aspect-count" value="${Math.max(1, lampsStr.length)}" min="1" max="8" step="1"/></div>` +
                    `<div class="field"><label>Aspect code</label>` +
                    `<input type="text" id="insp-sig-lamps" value="${escAttr(lampsStr)}" placeholder="BRYG"/></div>` +
                    `</div>`;

                html += `<div class="field"><label>Lamps (click to cycle, + add, − remove)</label>`;
                html += `<div id="insp-lamp-row" style="display:flex;gap:6px;align-items:center;flex-wrap:wrap;margin-top:4px;">`;
                for (let li = 0; li < lampsStr.length; li++) {
                    const ch = lampsStr.charAt(li).toUpperCase();
                    const col = LAMP_COLORS[ch] || LAMP_COLORS['B'];
                    html += `<div class="sip-lamp-dot" data-idx="${li}" data-kind="${ch}" ` +
                        `style="width:28px;height:28px;border-radius:50%;background:${col};` +
                        `border:2px solid rgba(255,255,255,0.3);cursor:pointer;` +
                        `display:flex;align-items:center;justify-content:center;` +
                        `font-size:10px;font-weight:700;color:#0a0f1e;" ` +
                        `title="${LAMP_NAMES[ch] || ch}">` +
                        `${ch}</div>`;
                }
                html += `<div id="insp-lamp-add" style="width:24px;height:24px;border-radius:50%;` +
                    `background:rgba(34,211,238,0.15);border:1px dashed rgba(34,211,238,0.5);` +
                    `cursor:pointer;display:flex;align-items:center;justify-content:center;` +
                    `font-size:14px;color:#22d3ee;" title="Add lamp">+</div>`;
                if (lampsStr.length > 1) {
                    html += `<div id="insp-lamp-remove" style="width:24px;height:24px;border-radius:50%;` +
                        `background:rgba(255,46,46,0.12);border:1px dashed rgba(255,46,46,0.4);` +
                        `cursor:pointer;display:flex;align-items:center;justify-content:center;` +
                        `font-size:14px;color:#FF6666;" title="Remove last lamp">−</div>`;
                }
                html += `</div></div>`;

                html += `<div class="field"><label>Lit aspects</label>` +
                    `<input type="text" id="insp-sig-lit" value="${escAttr(litStr)}" placeholder="(none — e.g. R, RG, Y)"/></div>`;

                if (c.type === 'examples.SignalShunt') {
                    const lampGapVal = +sigProps.lampGap || 7;
                    const shuntGapVal = +sigProps.shuntGap || 2;
                    const shuntSizeVal = +sigProps.shuntSize || 42;
                    const shuntSideVal = String(sigProps.shuntSide || 'right').toLowerCase();
                    const shuntStateVal = String(sigProps.shuntState || sigProps.shuntVariant || sigProps.shuntLit || 'PROCEED').toUpperCase();
                    function _selected(v, cur) { return String(v).toUpperCase() === String(cur).toUpperCase() ? 'selected' : ''; }
                    function _selectedSide(v, cur) { return String(v).toLowerCase() === String(cur).toLowerCase() ? 'selected' : ''; }

                    html += `<div class="insp-grid">` +
                        `<div class="field"><label>Main lamp gap</label><input type="number" id="insp-combined-lamp-gap" value="${lampGapVal}" min="2" max="30" step="1"/></div>` +
                        `<div class="field"><label>Signal-shunt gap</label><input type="number" id="insp-combined-shunt-gap" value="${shuntGapVal}" min="0" max="80" step="1"/></div>` +
                        `</div>`;

                    html += `<div class="insp-grid">` +
                        `<div class="field"><label>Shunt position</label><select id="insp-combined-shunt-side">` +
                        `<option value="right" ${_selectedSide('right', shuntSideVal)}>Right of main</option>` +
                        `<option value="left" ${_selectedSide('left', shuntSideVal)}>Left of main</option>` +
                        `<option value="top" ${_selectedSide('top', shuntSideVal)}>Above main</option>` +
                        `<option value="bottom" ${_selectedSide('bottom', shuntSideVal)}>Below main</option>` +
                        `<option value="none" ${_selectedSide('none', shuntSideVal)}>No shunt</option>` +
                        `</select></div>` +
                        `<div class="field"><label>Shunt size</label><input type="number" id="insp-combined-shunt-size" value="${shuntSizeVal}" min="24" max="110" step="1"/></div>` +
                        `</div>`;

                    html += `<div class="insp-grid">` +
                        `<div class="field"><label>Shunt combination</label><select id="insp-combined-shunt-state">` +
                        `<option value="OFF" ${_selected('OFF', shuntStateVal)}>Off / all dark</option>` +
                        `<option value="PROCEED" ${_selected('PROCEED', shuntStateVal)}>Proceed · BL + BR</option>` +
                        `<option value="DIVERGE_RIGHT" ${_selected('DIVERGE_RIGHT', shuntStateVal)}>Diverge right · Top + BR</option>` +
                        `<option value="DIVERGE_LEFT" ${_selected('DIVERGE_LEFT', shuntStateVal)}>Diverge left · Top + BL</option>` +
                        `<option value="TOP" ${_selected('TOP', shuntStateVal)}>Top only</option>` +
                        `<option value="BL" ${_selected('BL', shuntStateVal)}>Bottom-left only</option>` +
                        `<option value="BR" ${_selected('BR', shuntStateVal)}>Bottom-right only</option>` +
                        `<option value="ALL" ${_selected('ALL', shuntStateVal)}>All three</option>` +
                        `</select></div>` +
                        `<div class="field"><label>Custom shunt code</label><input type="text" id="insp-combined-shunt-code" value="${escAttr(shuntStateVal)}" placeholder="OFF / PROCEED / T,BL,BR"/></div>` +
                        `</div>`;
                }
            }

            /* ---- Rotation (quick buttons + number input) ---- */
            const curAngle = c.angle || 0;
            html += `<div class="field"><label>Rotate</label>`;
            html += `<div style="display:flex;gap:4px;align-items:center;margin-top:2px;">`;
            const rotBtns = [0, 45, 90, 135, 180, 225, 270, 315];
            for (const deg of rotBtns) {
                const active = Math.round(curAngle) === deg;
                html += `<button class="sip-rot-btn" data-deg="${deg}" ` +
                    `style="padding:3px 6px;border-radius:4px;border:1px solid ${active ? '#22d3ee' : 'rgba(255,255,255,0.15)'};` +
                    `background:${active ? 'rgba(34,211,238,0.18)' : 'rgba(255,255,255,0.05)'};` +
                    `color:${active ? '#22d3ee' : '#ccc'};cursor:pointer;font-size:11px;font-weight:600;">${deg}°</button>`;
            }
            html += `<input type="number" id="insp-angle" value="${curAngle}" min="0" max="359" step="1" ` +
                `style="width:52px;margin-left:4px;" title="Free rotation"/>`;
            html += `</div></div>`;

            /* ---- Keep label upright (readable in the actual direction) ---- */
            const keepUpright = !(c.attrs && c.attrs.label && c.attrs.label.keepUpright === false);
            html += `<div class="field" style="display:flex;align-items:center;gap:8px;">` +
                `<input type="checkbox" id="insp-label-upright" ${keepUpright ? 'checked' : ''} style="width:auto;">` +
                `<label for="insp-label-upright" style="margin:0;">Keep label readable when rotated</label></div>` +
                `<small class="muted">On: label text stays upright / un-mirrored at any rotation. Off: label rotates with the asset.</small>`;

            /* ---- Stand position: visual compass grid (3×3) ---- *
             *  The 3×3 grid represents 8 attachment positions around the
             *  signal body + centre = none.
             *
             *   TL  T  TR
             *    L  ×  R
             *   BL  B  BR
             *
             *  Clicking a cell sets `standPos`. Centre (×) removes the stand.
             */
            const curStandPos = sigProps.standPos ||
                (function () {
                    /* backward compat: derive from old properties */
                    var m = sigProps.stand || 'bottom';
                    if (m === 'none') return 'none';
                    var s = sigProps.standSide || 'center';
                    if (m === 'top') return s === 'left' ? 'TL' : s === 'right' ? 'TR' : 'T';
                    if (m === 'bottom') return s === 'left' ? 'BL' : s === 'right' ? 'BR' : 'B';
                    return 'B';
                })();
            const COMPASS = [
                ['TL', 'T', 'TR'],
                ['L', 'none', 'R'],
                ['BL', 'B', 'BR']
            ];
            const COMPASS_LABEL = {
                TL: '↖', T: '↑', TR: '↗',
                L: '←', none: '×', R: '→',
                BL: '↙', B: '↓', BR: '↘'
            };
            html += `<div class="field"><label>Stand attach</label>`;
            html += `<div id="insp-compass" style="display:inline-grid;grid-template-columns:repeat(3,30px);` +
                `gap:2px;margin-top:4px;">`;
            for (const row of COMPASS) {
                for (const pos of row) {
                    const active = pos === curStandPos;
                    html += `<div class="sip-compass-cell" data-pos="${pos}" ` +
                        `style="width:30px;height:30px;border-radius:4px;` +
                        `display:flex;align-items:center;justify-content:center;` +
                        `cursor:pointer;font-size:14px;font-weight:700;` +
                        `border:1.5px solid ${active ? '#22d3ee' : 'rgba(255,255,255,0.12)'};` +
                        `background:${active ? 'rgba(34,211,238,0.18)' : 'rgba(255,255,255,0.04)'};` +
                        `color:${active ? '#22d3ee' : '#888'};" ` +
                        `title="${pos === 'none' ? 'No stand' : 'Stand at ' + pos}">${COMPASS_LABEL[pos]}</div>`;
                }
            }
            html += `</div></div>`;

            /* Centre L-stand: an L-shaped stand attached at the signal's bottom
               centre (vertical drop + horizontal foot). Height = "Stand vertical",
               foot length = "L arm size" below. The compass centre (×) removes it. */
            html += `<div class="field" style="margin-top:6px;"><label>Centre stand</label>` +
                `<div style="display:flex;gap:6px;margin-top:2px;flex-wrap:wrap;">` +
                `<button type="button" id="insp-stand-center" ` +
                `style="padding:5px 12px;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;` +
                `border:1.5px solid ${curStandPos === 'C' ? '#22d3ee' : 'rgba(255,255,255,0.15)'};` +
                `background:${curStandPos === 'C' ? 'rgba(34,211,238,0.18)' : 'rgba(255,255,255,0.05)'};` +
                `color:${curStandPos === 'C' ? '#22d3ee' : '#ccc'};">L-stand at centre</button>` +
                `<button type="button" id="insp-stand-free" ` +
                `style="padding:5px 12px;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;` +
                `border:1.5px solid ${curStandPos === 'FREE' ? '#22c55e' : 'rgba(255,255,255,0.15)'};` +
                `background:${curStandPos === 'FREE' ? 'rgba(34,197,94,0.18)' : 'rgba(255,255,255,0.05)'};` +
                `color:${curStandPos === 'FREE' ? '#22c55e' : '#ccc'};" title="Drag the green attach point and the amber tip to shape the L freely">Free L-stand (drag ends)</button>` +
                `</div></div>`;

            /* Stand size */
            const standArm = +sigProps.standArm || 14;
            const standDrop = String(sigProps.standDrop || 'down').toLowerCase();
            const signalLabelGap = +sigProps.labelGap || ((c.type === 'examples.Shaunt' || c.type === 'examples.Shaunt2' || c.type === 'examples.Shaunt3') ? 8 : 6);
            html += `<div class="insp-grid">` +
                `<div class="field"><label>Stand vertical</label>` +
                `<input type="number" id="insp-sig-stand-len" value="${standLen}" min="0" max="400" step="2"/></div>` +
                `<div class="field"><label>L arm size</label>` +
                `<input type="number" id="insp-sig-stand-arm" value="${standArm}" min="0" max="200" step="1"/></div>` +
                `</div>`;
            html += `<div class="field"><label>L drop direction</label>` +
                `<select id="insp-sig-stand-drop">` +
                `<option value="down" ${standDrop !== 'up' ? 'selected' : ''}>Down</option>` +
                `<option value="up" ${standDrop === 'up' ? 'selected' : ''}>Up</option>` +
                `</select></div>`;
            html += `<div class="field"><label>Label gap</label>` +
                `<input type="number" id="insp-sig-label-gap" value="${signalLabelGap}" min="0" max="60" step="1"/></div>`;

            /* Route / Calling editor.  For examples.Signal this is optional;
               for examples.RouteCallingSignal it is always enabled. */
            if (c.type === 'examples.RouteCallingSignal' || c.type === 'examples.Signal') {
                const routeProps = (c.attrs && c.attrs.route) || {};
                const isRouteCell = c.type === 'examples.RouteCallingSignal';
                const routeEnabled = isRouteCell || routeProps.enabled === true || routeProps.enabled === 'true';
                const labelsVal = routeProps.labels || routeProps.routes || routeProps.routeLabels || 'AUG,BUG,CUG,DUG';
                const activeVal = routeProps.active || routeProps.activeRoutes || '';
                const routeSideVal = routeProps.routeSide || 'top';
                const callingSideVal = routeProps.callingSide || 'bottom';
                const callingOn = routeProps.calling !== false && routeProps.calling !== 'false';
                const callingLabel = routeProps.callingLabel || 'C';
                const routeLightSizeVal = routeProps.dotSize || routeProps.lightSize || 3.8;
                const armSpacingVal = routeProps.armSpacing || 22;
                const armLengthVal = routeProps.armLength || 22;
                const routeLabelSizeVal = routeProps.labelSize || 8.5;
                const routeLabelGapVal = routeProps.labelGap || 8;
                function _sideOpts(cur) {
                    const opts = [
                        ['top', 'Top'], ['bottom', 'Bottom'], ['left', 'Left'], ['right', 'Right']
                    ];
                    return opts.map(function (o) {
                        return '<option value="' + o[0] + '" ' + (String(cur) === o[0] ? 'selected' : '') + '>' + o[1] + '</option>';
                    }).join('');
                }
                function _callSideOpts(cur) {
                    const opts = [
                        ['opposite', 'Opposite route'], ['top', 'Top'], ['bottom', 'Bottom'], ['left', 'Left'], ['right', 'Right']
                    ];
                    return opts.map(function (o) {
                        return '<option value="' + o[0] + '" ' + (String(cur) === o[0] ? 'selected' : '') + '>' + o[1] + '</option>';
                    }).join('');
                }
                html += `<div class="tb-group-h" style="margin-top:14px">Route / Calling</div>`;
                if (!isRouteCell) {
                    html += `<div class="field" style="display:flex;align-items:center;gap:8px;">` +
                        `<input type="checkbox" id="insp-route-enabled" ${routeEnabled ? 'checked' : ''} style="width:auto;">` +
                        `<label for="insp-route-enabled" style="margin:0;">Show route/calling on this main signal</label></div>`;
                }
                html += `<div class="field"><label>Route aspects above main signal</label>` +
                    `<div style="display:flex;gap:6px;align-items:center;">` +
                    `<input type="text" id="insp-route-labels" value="${escAttr(labelsVal)}" placeholder="AUG,BUG,CUG,DUG" style="flex:1;"/>` +
                    `<button type="button" id="insp-route-add" class="tb" style="padding:4px 9px;white-space:nowrap;" title="Add one route aspect above the signal">+ Add</button>` +
                    `<button type="button" id="insp-route-remove" class="tb" style="padding:4px 9px;" title="Remove last route aspect">−</button>` +
                    `</div><small class="muted">Each aspect is a small independent line above the main signal with one white lamp and editable label.</small></div>`;
                html += `<div class="field"><label>Active route/call for preview</label>` +
                    `<input type="text" id="insp-route-active" value="${escAttr(activeVal)}" placeholder="AUG or C or AUG,C"/></div>`;
                const routeSideLabel = isRouteCell ? 'Route side' : 'Route attach side';
                const callingSideLabel = isRouteCell ? 'Calling side' : 'Calling attach side';
                html += `<div class="insp-grid">` +
                    `<div class="field"><label>${routeSideLabel}</label><select id="insp-route-side">${_sideOpts(routeSideVal)}</select></div>` +
                    `<div class="field"><label>${callingSideLabel}</label><select id="insp-calling-side">${_callSideOpts(callingSideVal)}</select></div>` +
                    `</div>`;
                html += `<div class="insp-grid">` +
                    `<div class="field"><label>Calling label</label><input type="text" id="insp-calling-label" value="${escAttr(callingLabel)}" maxlength="6"/></div>` +
                    `<div class="field" style="display:flex;align-items:center;gap:8px;margin-top:20px;">` +
                    `<input type="checkbox" id="insp-calling-enabled" ${callingOn ? 'checked' : ''} style="width:auto;">` +
                    `<label for="insp-calling-enabled" style="margin:0;">Calling</label></div>` +
                    `</div>`;
                html += `<div class="insp-grid">` +
                    `<div class="field"><label>Route light size</label><input type="number" id="insp-route-light-size" value="${routeLightSizeVal}" min="2.4" max="10" step="0.2"/></div>` +
                    `<div class="field"><label>Small arm length</label><input type="number" id="insp-route-arm-length" value="${armLengthVal}" min="8" max="120" step="2"/></div>` +
                    `</div>`;
                html += `<div class="insp-grid">` +
                    `<div class="field"><label>Route label size</label><input type="number" id="insp-route-label-size" value="${routeLabelSizeVal}" min="6" max="18" step="0.5"/></div>` +
                    `<div class="field"><label>Route label gap</label><input type="number" id="insp-route-label-gap" value="${routeLabelGapVal}" min="0" max="40" step="1"/></div>` +
                    `</div>`;
                const attachGapVal = routeProps.attachGap || 6;
                html += `<div class="insp-grid">` +
                    `<div class="field"><label>Aspect gap</label>` +
                    `<input type="number" id="insp-route-arm-spacing" value="${armSpacingVal}" min="12" max="90" step="1"/></div>` +
                    `<div class="field"><label>Attach gap</label>` +
                    `<input type="number" id="insp-route-attach-gap" value="${attachGapVal}" min="0" max="80" step="1"/></div>` +
                    `</div>`;
            }
        }

        /* ---- Point-machine-only fields ---- */
        const PM_EDIT_TYPES = { 'examples.PointMachine': 1, 'examples.PointMachine1': 1 };
        if (PM_EDIT_TYPES[c.type]) {
            const pmProps = (c.attrs && c.attrs.pm) || {};
            const pmLabelSide = String(pmProps.labelSide || (c.attrs && c.attrs.label && c.attrs.label.side) || 'auto').toLowerCase();
            const pmLabelOffset = Math.max(0, +pmProps.labelOffset || 0);
            const pmIndOffset = +(pmProps.indOffset) || 0;
            const pmIndRadius = +(pmProps.indRadius) || 0;   // 0 = auto
            const sideIsOpp = (pmLabelSide === 'opposite' || pmLabelSide === 'reverse' || pmLabelSide === 'flip');
            html += `<div class="tb-group-h" style="margin-top:14px">Point Machine</div>`;
            html += `<div class="field"><label>Name (label) side</label>` +
                `<select id="insp-pm-label-side">` +
                `<option value="auto" ${!sideIsOpp ? 'selected' : ''}>Auto — with indicator circle</option>` +
                `<option value="opposite" ${sideIsOpp ? 'selected' : ''}>Opposite — reverse side (avoids overlap)</option>` +
                `</select>` +
                `<small class="muted">Use "Opposite" when the PT name overlaps the track or indicator.</small></div>`;
            html += `<div class="field"><label>Label extra gap (px)</label>` +
                `<input type="number" id="insp-pm-label-offset" value="${pmLabelOffset}" min="0" max="60" step="1"/></div>`;
            html += `<div class="field"><label>Indicator circle position (up / down)</label>` +
                `<input type="range" id="insp-pm-ind-offset" value="${pmIndOffset}" min="-60" max="60" step="1" style="width:100%;"/>` +
                `<div style="display:flex;align-items:center;gap:8px;margin-top:4px;">` +
                `<input type="number" id="insp-pm-ind-offset-num" value="${pmIndOffset}" min="-120" max="120" step="1" style="width:70px;"/>` +
                `<button type="button" id="insp-pm-ind-reset" style="background:rgba(255,46,46,0.12);` +
                `border:1px solid rgba(255,46,46,0.4);color:#ff8a8a;border-radius:6px;padding:3px 9px;cursor:pointer;font-size:12px;">Reset</button>` +
                `<span class="muted" style="font-size:11px;">− up · + down</span></div></div>`;
            html += `<div class="field"><label>Indicator circle radius (px)</label>` +
                `<input type="range" id="insp-pm-ind-radius" value="${pmIndRadius || 12}" min="4" max="40" step="0.5" style="width:100%;"/>` +
                `<div style="display:flex;align-items:center;gap:8px;margin-top:4px;">` +
                `<input type="number" id="insp-pm-ind-radius-num" value="${pmIndRadius || ''}" min="4" max="40" step="0.5" style="width:70px;" placeholder="auto"/>` +
                `<button type="button" id="insp-pm-ind-radius-reset" style="background:rgba(255,46,46,0.12);` +
                `border:1px solid rgba(255,46,46,0.4);color:#ff8a8a;border-radius:6px;padding:3px 9px;cursor:pointer;font-size:12px;">Auto</button>` +
                `<span class="muted" style="font-size:11px;">blank / Auto = size-based</span></div></div>`;
            /* ---- Operate effect (normal ⇆ reverse animation) ---- */
            const pmEffect = String(pmProps.effect || 'all').toLowerCase();
            const _fxOpt = (v, t) => `<option value="${v}" ${pmEffect === v ? 'selected' : ''}>${t}</option>`;
            html += `<div class="field"><label>Operate effect (while throwing N ⇆ R)</label>` +
                `<select id="insp-pm-effect">` +
                _fxOpt('all', 'All — pulse + ripple + energy flow') +
                _fxOpt('pulse', 'Pulse — indicator blink (classic)') +
                _fxOpt('ripple', 'Ripple — sonar rings from indicator') +
                _fxOpt('flow', 'Flow — energy dashes along the blade') +
                `</select>` +
                `<small class="muted">Flow direction follows the target position: Normal → with the diagonal, Reverse → against it.</small></div>`;
            html += `<div style="display:flex;align-items:center;gap:8px;margin:2px 0 6px;">` +
                `<button type="button" id="insp-pm-test-n" class="tb" style="font-size:12px;padding:4px 10px;">▶ Test Normal</button>` +
                `<button type="button" id="insp-pm-test-r" class="tb" style="font-size:12px;padding:4px 10px;">▶ Test Reverse</button>` +
                `<span class="muted" style="font-size:11px;">3&nbsp;s preview on canvas</span></div>`;
        }

        html += `<div class="insp-actions">`;
        html += `<button class="tb" onclick="SipEditor.duplicateSelected()">Duplicate</button>`;
        html += `<button class="tb danger" onclick="SipEditor.deleteSelected()">Delete</button>`;
        html += `</div>`;
        inspectorEl.innerHTML = html;

        // Wire up the inputs
        bindInspectorInput('insp-label', v => {
            c.attrs = c.attrs || {};
            c.attrs.label = c.attrs.label || {};
            c.attrs.label.text = v;
        });
        bindInspectorInput('insp-lf', v => {
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            c.attrs.label.fill = v || undefined;
        });
        bindInspectorInputNum('insp-ls', v => {
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            if (v) c.attrs.label.fontSize = v;
        });
        bindInspectorInputNum('insp-ldx', v => {
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            c.attrs.label.dx = v || 0;
            c.attrs.label.dir = 'custom';
        });
        bindInspectorInputNum('insp-ldy', v => {
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            c.attrs.label.dy = v || 0;
            c.attrs.label.dir = 'custom';
        });
        const ldirEl = $('#insp-ldir');
        if (ldirEl) {
            ldirEl.addEventListener('change', () => {
                c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
                const dir = ldirEl.value;
                c.attrs.label.dir = dir;
                if (dir !== 'custom') {
                    const off = labelDirOffset(dir, c);
                    c.attrs.label.dx = off.dx;
                    c.attrs.label.dy = off.dy;
                }
                render();
                pushHistory();
            });
        }
        const lresetEl = $('#insp-lreset');
        if (lresetEl) {
            lresetEl.addEventListener('click', () => {
                c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
                c.attrs.label.dx = 0;
                c.attrs.label.dy = 0;
                c.attrs.label.dir = 'custom';
                render();
                pushHistory();
            });
        }
        bindInspectorInputNum('insp-x', v => { c.position.x = v; });
        bindInspectorInputNum('insp-y', v => { c.position.y = v; });
        bindInspectorInputNum('insp-w', v => { c.size.width = v; });
        bindInspectorInputNum('insp-h', v => { c.size.height = v; });

        /* ---- Point Machine inspector bindings ---- */
        if (PM_EDIT_TYPES[c.type]) {
            const ensurePm = () => {
                c.attrs = c.attrs || {};
                c.attrs.pm = c.attrs.pm || {};
                return c.attrs.pm;
            };
            const pmSideEl = $('#insp-pm-label-side');
            if (pmSideEl) {
                pmSideEl.addEventListener('change', () => {
                    ensurePm().labelSide = pmSideEl.value;
                    render();
                    pushHistory();
                });
            }
            bindInspectorInputNum('insp-pm-label-offset', v => {
                ensurePm().labelOffset = Math.max(0, Math.min(60, v));
            });
            // Indicator circle up/down — slider and number input stay in sync,
            // both write attrs.pm.indOffset (− up / + down along the PM line).
            const setIndOffset = (v, fromSlider) => {
                const clamped = Math.max(-120, Math.min(120, v));
                ensurePm().indOffset = clamped;
                const slider = $('#insp-pm-ind-offset');
                const num = $('#insp-pm-ind-offset-num');
                if (slider && !fromSlider) slider.value = Math.max(-60, Math.min(60, clamped));
                if (num && fromSlider) num.value = clamped;
                render(true);
            };
            const pmIndSlider = $('#insp-pm-ind-offset');
            if (pmIndSlider) {
                pmIndSlider.addEventListener('input', () => { const v = parseFloat(pmIndSlider.value); if (!isNaN(v)) setIndOffset(v, true); });
                pmIndSlider.addEventListener('change', () => { render(); pushHistory(); });
            }
            const pmIndNum = $('#insp-pm-ind-offset-num');
            if (pmIndNum) {
                pmIndNum.addEventListener('input', () => { const v = parseFloat(pmIndNum.value); if (!isNaN(v)) setIndOffset(v, false); });
                pmIndNum.addEventListener('change', () => { render(); pushHistory(); });
            }
            const pmIndReset = $('#insp-pm-ind-reset');
            if (pmIndReset) {
                pmIndReset.addEventListener('click', () => {
                    ensurePm().indOffset = 0;
                    const slider = $('#insp-pm-ind-offset'); if (slider) slider.value = 0;
                    const num = $('#insp-pm-ind-offset-num'); if (num) num.value = 0;
                    render();
                    pushHistory();
                });
            }
            // Indicator circle RADIUS — slider and number input stay in sync,
            // both write attrs.pm.indRadius (0 / blank = auto size-based).
            const setIndRadius = (v, fromSlider) => {
                const clamped = Math.max(4, Math.min(40, v));
                ensurePm().indRadius = clamped;
                const slider = $('#insp-pm-ind-radius');
                const num = $('#insp-pm-ind-radius-num');
                if (slider && !fromSlider) slider.value = clamped;
                if (num && fromSlider) num.value = clamped;
                render(true);
            };
            const pmRadSlider = $('#insp-pm-ind-radius');
            if (pmRadSlider) {
                pmRadSlider.addEventListener('input', () => { const v = parseFloat(pmRadSlider.value); if (!isNaN(v)) setIndRadius(v, true); });
                pmRadSlider.addEventListener('change', () => { render(); pushHistory(); });
            }
            const pmRadNum = $('#insp-pm-ind-radius-num');
            if (pmRadNum) {
                pmRadNum.addEventListener('input', () => {
                    if (pmRadNum.value === '') { delete ensurePm().indRadius; render(true); return; }
                    const v = parseFloat(pmRadNum.value); if (!isNaN(v)) setIndRadius(v, false);
                });
                pmRadNum.addEventListener('change', () => { render(); pushHistory(); });
            }
            const pmRadReset = $('#insp-pm-ind-radius-reset');
            if (pmRadReset) {
                pmRadReset.addEventListener('click', () => {
                    delete ensurePm().indRadius;
                    const slider = $('#insp-pm-ind-radius'); if (slider) slider.value = 12;
                    const num = $('#insp-pm-ind-radius-num'); if (num) num.value = '';
                    render();
                    pushHistory();
                });
            }
            /* ---- Operate effect select + 3s test preview ---- */
            const pmFxEl = $('#insp-pm-effect');
            if (pmFxEl) {
                pmFxEl.addEventListener('change', () => {
                    ensurePm().effect = pmFxEl.value;
                    render();
                    pushHistory();
                });
            }
            const runPmTest = (state) => {
                c.attrs = c.attrs || {};
                c.attrs.pmBlink = true;
                c.attrs.pmState = state;
                render();
                if (c._pmTestTimer) clearTimeout(c._pmTestTimer);
                c._pmTestTimer = setTimeout(() => {
                    delete c.attrs.pmBlink;
                    delete c.attrs.pmState;
                    delete c._pmTestTimer;
                    render();
                }, 3000);
            };
            const pmTestN = $('#insp-pm-test-n');
            if (pmTestN) pmTestN.addEventListener('click', () => runPmTest('N'));
            const pmTestR = $('#insp-pm-test-r');
            if (pmTestR) pmTestR.addEventListener('click', () => runPmTest('R'));
        }

        /* ---- Signal / Shunt inspector bindings ---- */
        const SIG_STAND_BIND_TYPES = {
            'examples.Signal': 1,
            'examples.SignalShunt': 1,
            'examples.RouteCallingSignal': 1,
            'examples.Shaunt': 1,
            'examples.Shaunt2': 1,
            'examples.Shaunt3': 1
        };
        if (SIG_STAND_BIND_TYPES[c.type]) {
            const ensureSig = () => {
                c.attrs = c.attrs || {};
                c.attrs.signal = c.attrs.signal || {};
                return c.attrs.signal;
            };
            /* Lamp/lit controls exist for main signal and combined signal+shunt */
            if (c.type === 'examples.Signal' || c.type === 'examples.SignalShunt') {
                function cleanLampCode(v) {
                    var out = String(v || '').toUpperCase().replace(/[^BRYGX]/g, '');
                    return out || 'B';
                }
                function fitSignalWidthForAspects(count) {
                    count = Math.max(1, Math.min(8, parseInt(count, 10) || 1));
                    var baseH = (c.size && c.size.height) || (c.type === 'examples.SignalShunt' ? 80 : 24);
                    var lampD = Math.max(10, c.type === 'examples.SignalShunt' ? Math.min(32, baseH * 0.46 - 10) : baseH - 6);
                    var minW = Math.ceil((count * lampD + (count + 1) * 8) / 10) * 10;
                    if (c.type === 'examples.SignalShunt') {
                        var sp0 = (c.attrs && c.attrs.signal) || {};
                        var shSide = String(sp0.shuntSide || 'right').toLowerCase();
                        var shSize = Math.max(24, Math.min(110, +sp0.shuntSize || 42));
                        var shGap = Math.max(0, +sp0.shuntGap || 2);
                        if (shSide === 'left' || shSide === 'right') {
                            minW += shSize + shGap + 20;
                        } else if (shSide === 'top' || shSide === 'bottom') {
                            minW = Math.max(minW, shSize + 20);
                            if (c.size && c.size.height < 100) c.size.height = 100;
                        }
                    }
                    if (!c.size) c.size = { width: minW, height: c.type === 'examples.SignalShunt' ? 80 : 24 };
                    if (c.size.width < minW) c.size.width = minW;
                }
                function resizeLampCode(code, count) {
                    code = cleanLampCode(code);
                    count = Math.max(1, Math.min(8, parseInt(count, 10) || code.length || 1));
                    if (code.length > count) return code.substring(0, count);
                    while (code.length < count) code += 'B';
                    return code;
                }
                bindInspectorInputNum('insp-sig-aspect-count', v => {
                    const sp = ensureSig();
                    sp.lamps = resizeLampCode(sp.lamps || (c.type === 'examples.SignalShunt' ? 'RYG' : 'BBB'), v);
                    fitSignalWidthForAspects(sp.lamps.length);
                });
                bindInspectorInput('insp-sig-lamps', v => {
                    const sp = ensureSig();
                    sp.lamps = cleanLampCode(v);
                    fitSignalWidthForAspects(sp.lamps.length);
                });

                /* ---- Visual lamp editor: wire click handlers ---- */
                const LAMP_CYCLE = ['B', 'R', 'Y', 'G', 'X'];
                const lampRow = $('#insp-lamp-row');
                if (lampRow) {
                    lampRow.querySelectorAll('.sip-lamp-dot').forEach(dot => {
                        dot.addEventListener('click', () => {
                            const sp = ensureSig();
                            let lamps = String(sp.lamps || (c.type === 'examples.SignalShunt' ? 'RYG' : 'BBB')).split('');
                            const idx = parseInt(dot.getAttribute('data-idx'), 10);
                            const curKind = dot.getAttribute('data-kind');
                            const nextIdx = (LAMP_CYCLE.indexOf(curKind) + 1) % LAMP_CYCLE.length;
                            lamps[idx] = LAMP_CYCLE[nextIdx];
                            sp.lamps = lamps.join('');
                            render();
                            pushHistory();
                        });
                    });
                    const addBtn = $('#insp-lamp-add');
                    if (addBtn) {
                        addBtn.addEventListener('click', () => {
                            const sp = ensureSig();
                            sp.lamps = (sp.lamps || (c.type === 'examples.SignalShunt' ? 'RYG' : 'BBB')) + 'B';
                            fitSignalWidthForAspects(sp.lamps.length);
                            render();
                            pushHistory();
                        });
                    }
                    const removeBtn = $('#insp-lamp-remove');
                    if (removeBtn) {
                        removeBtn.addEventListener('click', () => {
                            const sp = ensureSig();
                            if (sp.lamps && sp.lamps.length > 1) {
                                sp.lamps = sp.lamps.slice(0, -1);
                                render();
                                pushHistory();
                            }
                        });
                    }
                }

                bindInspectorInput('insp-sig-lit', v => {
                    const sp = ensureSig();
                    sp.lit = String(v || '').toUpperCase().replace(/[^BRYGX]/g, '');
                    if (c.type === 'examples.SignalShunt') c.attrs.lit = sp.lit;
                });
                bindInspectorInputNum('insp-combined-lamp-gap', v => {
                    const sp = ensureSig();
                    sp.lampGap = Math.max(2, Math.min(30, v));
                });
                bindInspectorInputNum('insp-combined-shunt-gap', v => {
                    const sp = ensureSig();
                    sp.shuntGap = Math.max(0, Math.min(80, v));
                });
                bindInspectorInputNum('insp-combined-shunt-size', v => {
                    const sp = ensureSig();
                    sp.shuntSize = Math.max(24, Math.min(110, v));
                    fitSignalWidthForAspects(String(sp.lamps || 'RYG').length);
                });
                const shuntSideEl = $('#insp-combined-shunt-side');
                if (shuntSideEl) {
                    shuntSideEl.addEventListener('change', () => {
                        const sp = ensureSig();
                        sp.shuntSide = shuntSideEl.value;
                        fitSignalWidthForAspects(String(sp.lamps || 'RYG').length);
                        render();
                        pushHistory();
                    });
                }
                const shuntStateEl = $('#insp-combined-shunt-state');
                if (shuntStateEl) {
                    shuntStateEl.addEventListener('change', () => {
                        const sp = ensureSig();
                        sp.shuntState = String(shuntStateEl.value || 'OFF').toUpperCase();
                        render();
                        pushHistory();
                    });
                }
                bindInspectorInput('insp-combined-shunt-code', v => {
                    const sp = ensureSig();
                    sp.shuntState = String(v || 'OFF').toUpperCase();
                });
            }

            /* ---- Rotation buttons + free-angle input ---- */
            $$('.sip-rot-btn').forEach(btn => {
                btn.addEventListener('click', () => {
                    c.angle = parseInt(btn.getAttribute('data-deg'), 10);
                    render();
                    pushHistory();
                });
            });
            bindInspectorInputNum('insp-angle', v => {
                c.angle = ((v % 360) + 360) % 360;
            });

            /* ---- Keep label upright toggle ---- */
            const uprightEl = $('#insp-label-upright');
            if (uprightEl) {
                uprightEl.addEventListener('change', () => {
                    c.attrs = c.attrs || {};
                    c.attrs.label = c.attrs.label || {};
                    if (uprightEl.checked) {
                        delete c.attrs.label.keepUpright;   // default = upright
                    } else {
                        c.attrs.label.keepUpright = false;  // rotate with asset
                    }
                    render();
                    pushHistory();
                });
            }

            /* ---- Compass grid: stand position ---- */
            const compassEl = $('#insp-compass');
            if (compassEl) {
                compassEl.querySelectorAll('.sip-compass-cell').forEach(cell2 => {
                    cell2.addEventListener('click', () => {
                        const sp = ensureSig();
                        sp.standPos = cell2.getAttribute('data-pos');
                        /* Clear legacy properties so resolveStandPos uses standPos */
                        delete sp.stand;
                        delete sp.standSide;
                        delete sp.standArmBefore;
                        delete sp.signalSide;
                        render();
                        pushHistory();
                    });
                });
            }

            /* Centre L-stand button */
            const standCenterBtn = $('#insp-stand-center');
            if (standCenterBtn) {
                standCenterBtn.addEventListener('click', () => {
                    const sp = ensureSig();
                    sp.standPos = 'C';
                    delete sp.stand; delete sp.standSide; delete sp.standArmBefore; delete sp.signalSide;
                    render();
                    pushHistory();
                });
            }
            /* Free L-stand button — attach + tip both draggable; seed sensible defaults */
            const standFreeBtn = $('#insp-stand-free');
            if (standFreeBtn) {
                standFreeBtn.addEventListener('click', () => {
                    const sp = ensureSig();
                    const bb = cellBox(c);
                    sp.standPos = 'FREE';
                    // Always (re)centre: attach on the CENTRE of the body, tip up above it.
                    sp.standAx = 0; sp.standAy = 0;
                    sp.standEx = 0; sp.standEy = -(Math.round(bb.h / 2) + 60);
                    delete sp.stand; delete sp.standSide; delete sp.standArmBefore; delete sp.signalSide;
                    render();
                    pushHistory();
                });
            }

            /* Stand size */
            bindInspectorInputNum('insp-sig-stand-len', v => {
                const sp = ensureSig();
                sp.standLength = v;
            });
            bindInspectorInputNum('insp-sig-stand-arm', v => {
                const sp = ensureSig();
                sp.standArm = v;
            });
            const standDropEl = $('#insp-sig-stand-drop');
            if (standDropEl) {
                standDropEl.addEventListener('change', () => {
                    const sp = ensureSig();
                    sp.standDrop = standDropEl.value;
                    render();
                    pushHistory();
                });
            }
            bindInspectorInputNum('insp-sig-label-gap', v => {
                const sp = ensureSig();
                sp.labelGap = Math.max(0, Math.min(60, v));
            });

            /* Route / Calling bindings */
            if (c.type === 'examples.RouteCallingSignal' || c.type === 'examples.Signal') {
                const ensureRoute = () => {
                    c.attrs = c.attrs || {};
                    c.attrs.route = c.attrs.route || {};
                    return c.attrs.route;
                };
                const ROUTE_POOL = ['AUG', 'BUG', 'CUG', 'DUG', 'EUG', 'FUG', 'HUG', 'JUG', 'KUG', 'LUG'];
                function getRouteLabels() {
                    const rp = ensureRoute();
                    return String(rp.labels || 'AUG,BUG,CUG,DUG').split(/[\s,|/]+/)
                        .map(x => String(x || '').trim().toUpperCase())
                        .filter((x, i, a) => x && a.indexOf(x) === i);
                }
                const routeEnabledEl = $('#insp-route-enabled');
                if (routeEnabledEl) {
                    routeEnabledEl.addEventListener('change', () => {
                        const rp = ensureRoute();
                        rp.enabled = routeEnabledEl.checked;
                        render();
                        pushHistory();
                    });
                }
                bindInspectorInput('insp-route-labels', v => {
                    const rp = ensureRoute();
                    rp.labels = String(v || '').toUpperCase();
                    if (c.type === 'examples.RouteCallingSignal') rp.enabled = true;
                });
                const routeAdd = $('#insp-route-add');
                if (routeAdd) {
                    routeAdd.addEventListener('click', () => {
                        const rp = ensureRoute();
                        const labels = getRouteLabels();
                        let next = ROUTE_POOL.find(x => labels.indexOf(x) === -1) || ('R' + (labels.length + 1));
                        labels.push(next);
                        rp.labels = labels.join(',');
                        rp.enabled = true;
                        if (!rp.routeSide) rp.routeSide = 'top';
                        if (!rp.callingSide) rp.callingSide = 'bottom';
                        render();
                        pushHistory();
                    });
                }
                const routeRemove = $('#insp-route-remove');
                if (routeRemove) {
                    routeRemove.addEventListener('click', () => {
                        const rp = ensureRoute();
                        const labels = getRouteLabels();
                        if (labels.length > 1) labels.pop();
                        rp.labels = labels.join(',');
                        render();
                        pushHistory();
                    });
                }
                bindInspectorInput('insp-route-active', v => {
                    const rp = ensureRoute();
                    rp.active = String(v || '').toUpperCase();
                });
                const routeSide = $('#insp-route-side');
                if (routeSide) {
                    routeSide.addEventListener('change', () => {
                        const rp = ensureRoute();
                        rp.routeSide = routeSide.value;
                        render();
                        pushHistory();
                    });
                }
                const callingSide = $('#insp-calling-side');
                if (callingSide) {
                    callingSide.addEventListener('change', () => {
                        const rp = ensureRoute();
                        rp.callingSide = callingSide.value;
                        render();
                        pushHistory();
                    });
                }
                const callingEnabled = $('#insp-calling-enabled');
                if (callingEnabled) {
                    callingEnabled.addEventListener('change', () => {
                        const rp = ensureRoute();
                        rp.calling = callingEnabled.checked;
                        render();
                        pushHistory();
                    });
                }
                bindInspectorInput('insp-calling-label', v => {
                    const rp = ensureRoute();
                    rp.callingLabel = String(v || 'C').toUpperCase();
                });
                bindInspectorInputNum('insp-route-light-size', v => {
                    const rp = ensureRoute();
                    rp.dotSize = Math.max(2.4, Math.min(10, v));
                    rp.lightSize = rp.dotSize;
                    rp.dotCount = 1;
                });
                bindInspectorInputNum('insp-route-arm-length', v => {
                    const rp = ensureRoute();
                    rp.armLength = v;
                });
                bindInspectorInputNum('insp-route-label-size', v => {
                    const rp = ensureRoute();
                    rp.labelSize = Math.max(6, Math.min(18, v));
                });
                bindInspectorInputNum('insp-route-label-gap', v => {
                    const rp = ensureRoute();
                    rp.labelGap = Math.max(0, Math.min(40, v));
                });
                bindInspectorInputNum('insp-route-arm-spacing', v => {
                    const rp = ensureRoute();
                    rp.armSpacing = v;
                });
                bindInspectorInputNum('insp-route-attach-gap', v => {
                    const rp = ensureRoute();
                    rp.attachGap = v;
                    rp.callingGap = v;
                });
            }
        }

        // Asset dropdown — pull from registry (server) or harvest from current yard.
        loadAssetsForType(c.type, function (list) {
            const sel = $('#insp-asset-select');
            if (!sel) return;
            const harvested = harvestLocalAssetNames(c.type);
            const merged = mergeAssetLists(list, harvested);
            let opts = '<option value="">— pick existing —</option>';
            for (const a of merged) {
                const v = escAttr(a.name);
                opts += `<option value="${v}" ${a.name === labelTxt ? 'selected' : ''}>${escapeXml(a.name)}</option>`;
            }
            sel.innerHTML = opts;
            sel.addEventListener('change', () => {
                const lbl = $('#insp-label');
                if (lbl) {
                    lbl.value = sel.value;
                    c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
                    c.attrs.label.text = sel.value;
                    render();
                    pushHistory();
                }
            });
        });
    }

    function bindInspectorInput(id, setter) {
        const el = $('#' + id);
        if (!el) return;
        el.addEventListener('input', () => { setter(el.value); render(true); });
        el.addEventListener('change', () => { render(); pushHistory(); });
    }
    function bindInspectorInputNum(id, setter) {
        const el = $('#' + id);
        if (!el) return;
        el.addEventListener('input', () => { const v = parseFloat(el.value); if (!isNaN(v)) { setter(v); render(true); } });
        el.addEventListener('change', () => { render(); pushHistory(); });
    }
    function escAttr(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
    }
    function escapeXml(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function harvestLocalAssetNames(type) {
        const seen = new Set();
        const out = [];
        for (const c of state.cells) {
            if (c.type !== type) continue;
            const t = c.attrs && c.attrs.label && c.attrs.label.text;
            if (t && !seen.has(t)) { seen.add(t); out.push({ id: t, name: t }); }
        }
        return out;
    }
    function mergeAssetLists(a, b) {
        const seen = new Set();
        const out = [];
        for (const list of [a || [], b || []]) {
            for (const x of list) {
                if (!x || !x.name) continue;
                if (seen.has(x.name)) continue;
                seen.add(x.name);
                out.push(x);
            }
        }
        return out.sort((p, q) => p.name.localeCompare(q.name));
    }

    function refreshAssetRegistry() { assetRegistryCache = {}; }

    function loadAssetsForType(type, cb) {
        if (assetRegistryCache[type]) { cb(assetRegistryCache[type]); return; }
        if (typeof window.SipAssetSource === 'function') {
            window.SipAssetSource(type, function (list) {
                assetRegistryCache[type] = list || [];
                cb(assetRegistryCache[type]);
            });
        } else {
            cb([]);
        }
    }

    /* ------------------------------------------------------------------------ *
     *  INTERACTION — select / drag / delete
     * ------------------------------------------------------------------------ */
    let drag = null;    // { id, offX, offY, moved }   — moving an existing cell
    let resize = null;  // { id, handle, ox, oy, ow, oh, moved } — resizing
    let rotate = null;  // { id, cx, cy, moved } — rotating via the rotate handle
    let standDrag = null; // { id, ... } — dragging the stand TIP (height / arm)
    let attachDrag = null; // { id, base, bodyH, moved } — dragging the stand ATTACH point
    let labelDrag = null; // { id, startX, startY, baseDx, baseDy, moved } — moving a label
    let canvasPan = null; // { startX, startY, vb, moved } — left-drag panning the view

    function onCanvasMouseDown(evt) {
        let node = evt.target;
        // (a0) Was it the rotate handle?
        if (node && node.classList && node.classList.contains('rotate-handle')) {
            const c = selectedCell();
            if (!c) return;
            rotate = {
                id: c.id,
                cx: c.position.x + c.size.width / 2,
                cy: c.position.y + c.size.height / 2,
                moved: false
            };
            evt.preventDefault();
            return;
        }
        // (a0a) Was it the stand ATTACH handle? (moves where the stand connects
        // to the body — centre / lower — without touching the head.)
        if (node && node.classList && node.classList.contains('attach-handle')) {
            const c = selectedCell();
            if (!c) return;
            const sgm = standGeom(c);
            if (!sgm || !sgm.hasAttach) return;
            attachDrag = { id: c.id, base: sgm.attachBase, bodyH: sgm.bodyH, free: !!sgm.free, cx: sgm.bodyCx, cy: sgm.bodyCy, moved: false };
            evt.preventDefault();
            return;
        }
        // (a0b) Was it the stand-length handle? (adjusts the stand only, not the head)
        if (node && node.classList && node.classList.contains('stand-handle')) {
            const c = selectedCell();
            if (!c) return;
            const sgm = standGeom(c);
            if (!sgm) return;
            standDrag = {
                id: c.id,
                ax: sgm.ax, ay: sgm.ay,
                hasArm: sgm.hasArm,
                vFactor: sgm.vFactor || 1,
                free: !!sgm.free, cx: sgm.bodyCx, cy: sgm.bodyCy,
                moved: false
            };
            evt.preventDefault();
            return;
        }
        // (a) Was it a resize handle?
        if (node && node.classList && node.classList.contains('resize-handle')) {
            const dir = node.getAttribute('data-handle');
            const c = selectedCell();
            if (!c) return;
            resize = {
                id: c.id, handle: dir,
                ox: c.position.x, oy: c.position.y,
                ow: c.size.width, oh: c.size.height,
                moved: false
            };
            evt.preventDefault();
            return;
        }
        // (a2) Was it the draggable label handle?
        if (node && node.classList && node.classList.contains('label-handle')) {
            const c = selectedCell();
            if (!c) return;
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            const start = clientToWorld(evt);
            labelDrag = {
                id: c.id,
                startX: start.x, startY: start.y,
                baseDx: +(c.attrs.label.dx || 0),
                baseDy: +(c.attrs.label.dy || 0),
                moved: false
            };
            evt.preventDefault();
            return;
        }
        // (b) Otherwise look up the editable group as before
        while (node && node !== canvasEl) {
            if (node.classList && node.classList.contains('editable')) break;
            node = node.parentNode;
        }
        if (!node || node === canvasEl) {
            // Empty space: left-drag pans the view; a plain click clears selection
            // (handled on mouseup when no drag occurred).
            canvasPan = { startX: evt.clientX, startY: evt.clientY, vb: parseViewBox(state.viewBox), moved: false };
            evt.preventDefault();
            return;
        }
        const eid = node.getAttribute('data-eid');

        // Shift-click toggles an element in/out of the multi-selection.
        if (evt.shiftKey) {
            toggleInSelection(eid);
            render();
            evt.preventDefault();
            return;
        }

        // Clicking an element that is part of a multi-selection starts a GROUP
        // drag that moves every selected element together.
        if (isSelected(eid) && state.selectedIds.length > 1) {
            state.selectedId = eid;
            const gs = clientToWorld(evt);
            drag = { group: true, ids: state.selectedIds.slice(), base: {}, startX: gs.x, startY: gs.y, moved: false, clickEid: eid };
            for (const id of drag.ids) { const gc = cellById(id); if (gc) drag.base[id] = { x: gc.position.x, y: gc.position.y }; }
            render();
            evt.preventDefault();
            return;
        }

        // Otherwise: single select + drag that one element.
        state.selectedId = eid;
        state.selectedIds = [eid];
        render();

        const c = cellById(eid);
        if (!c) return;
        const start = clientToWorld(evt);
        drag = {
            id: eid,
            offX: start.x - c.position.x,
            offY: start.y - c.position.y,
            moved: false
        };
        evt.preventDefault();
    }
    function onCanvasMouseMove(evt) {
        // Cursor readout always
        const w = clientToWorld(evt);
        const cc = $('#cursor-coords');
        if (cc) cc.textContent = Math.round(w.x) + ',' + Math.round(w.y);

        // ROTATE (grabbed the rotate handle) — absolute angle from the pointer
        // around the cell centre, so it is correct at any current rotation.
        if (rotate) {
            const c = cellById(rotate.id);
            if (!c) return;
            let ang = Math.atan2(w.y - rotate.cy, w.x - rotate.cx) * 180 / Math.PI + 90;
            const step = evt.shiftKey ? 1 : 15;   // Shift = fine (1°), else snap to 15°
            ang = ((Math.round(ang / step) * step) % 360 + 360) % 360;
            if (ang !== (c.angle || 0)) { c.angle = ang; rotate.moved = true; renderCellLive(c); }
            return;
        }

        // STAND ATTACH — drag the green handle to move where the stand connects
        // to the body (attach at centre / lower).
        if (attachDrag) {
            const c = cellById(attachDrag.id);
            if (!c) return;
            c.attrs = c.attrs || {}; c.attrs.signal = c.attrs.signal || {};
            const sig = c.attrs.signal;
            let ch = false;
            if (attachDrag.free) {
                // Lock the attach X to the body centre so the vertical leg always
                // stays centred on the shunt; only the vertical position moves.
                const ay = Math.round(w.y - attachDrag.cy);
                if ((sig.standAx || 0) !== 0 || ay !== (sig.standAy || 0)) { sig.standAx = 0; sig.standAy = ay; ch = true; }
            } else {
                const off = Math.max(0, Math.min(attachDrag.bodyH, snap(Math.abs(w.y - attachDrag.base))));
                if (off !== (sig.standAttachOff || 0)) { sig.standAttachOff = off; ch = true; }
            }
            if (ch) { attachDrag.moved = true; renderCellLive(c); }
            return;
        }

        // STAND STRETCH — drag the amber tip handle to change the stand drop
        // (height) and, for L-shaped stands, the arm (horizontal reach).
        if (standDrag) {
            const c = cellById(standDrag.id);
            if (!c) return;
            c.attrs = c.attrs || {}; c.attrs.signal = c.attrs.signal || {};
            const sig = c.attrs.signal;
            let ch = false;
            if (standDrag.free) {
                const ex = Math.round(w.x - standDrag.cx), ey = Math.round(w.y - standDrag.cy);
                if (ex !== (sig.standEx || 0) || ey !== (sig.standEy || 0)) { sig.standEx = ex; sig.standEy = ey; ch = true; }
            } else {
                const len = Math.max(8, snap(Math.abs(w.y - standDrag.ay) / (standDrag.vFactor || 1)));
                if (len !== (sig.standLength || 0)) { sig.standLength = len; ch = true; }
                if (standDrag.hasArm) {
                    const arm = Math.max(0, snap(Math.abs(w.x - standDrag.ax)));
                    if (arm !== (sig.standArm || 0)) { sig.standArm = arm; ch = true; }
                }
            }
            if (ch) { standDrag.moved = true; renderCellLive(c); }
            return;
        }

        // LABEL DRAG has priority — just nudges the label offset
        if (labelDrag) {
            const c = cellById(labelDrag.id);
            if (!c) return;
            c.attrs = c.attrs || {}; c.attrs.label = c.attrs.label || {};
            const ndx = Math.round(labelDrag.baseDx + (w.x - labelDrag.startX));
            const ndy = Math.round(labelDrag.baseDy + (w.y - labelDrag.startY));
            if (ndx !== (c.attrs.label.dx || 0) || ndy !== (c.attrs.label.dy || 0)) {
                c.attrs.label.dx = ndx;
                c.attrs.label.dy = ndy;
                c.attrs.label.dir = 'custom';
                labelDrag.moved = true;
                render(true);
            }
            return;
        }

        // RESIZE has priority over drag
        if (resize) {
            const c = cellById(resize.id);
            if (!c) return;
            const MIN = 20;
            let nx = resize.ox, ny = resize.oy, nw = resize.ow, nh = resize.oh;

            // Right side dragging east?  handles e/ne/se
            if (/e/.test(resize.handle)) {
                nw = Math.max(MIN, snap(w.x - resize.ox));
            }
            // Left side dragging west?  handles w/nw/sw
            if (/w/.test(resize.handle)) {
                const right = resize.ox + resize.ow;
                nx = Math.min(snap(w.x), right - MIN);
                nw = right - nx;
            }
            // Bottom side?  handles s/se/sw
            if (/^s|s$/.test(resize.handle)) {
                nh = Math.max(MIN, snap(w.y - resize.oy));
            }
            // Top side?  handles n/ne/nw
            if (/^n|n$/.test(resize.handle)) {
                const bottom = resize.oy + resize.oh;
                ny = Math.min(snap(w.y), bottom - MIN);
                nh = bottom - ny;
            }
            if (nx !== c.position.x || ny !== c.position.y ||
                nw !== c.size.width || nh !== c.size.height) {
                c.position.x = nx;
                c.position.y = ny;
                c.size.width = nw;
                c.size.height = nh;
                resize.moved = true;
                renderCellLive(c);   // in-place update — no full rebuild, no flicker
            }
            return;
        }

        // CANVAS PAN (left-drag on empty space) — cheap: update the viewBox attr
        // directly, no full re-render until mouseup.
        if (canvasPan) {
            const pdx = evt.clientX - canvasPan.startX, pdy = evt.clientY - canvasPan.startY;
            if (!canvasPan.moved && Math.abs(pdx) < 4 && Math.abs(pdy) < 4) return;
            canvasPan.moved = true;
            const svg = canvasEl.querySelector('svg');
            const rect = svg ? svg.getBoundingClientRect() : canvasEl.getBoundingClientRect();
            const pvb = canvasPan.vb;
            const nx = pvb.x - pdx * (pvb.w / (rect.width || 1));
            const ny = pvb.y - pdy * (pvb.h / (rect.height || 1));
            state.viewBox = Math.round(nx) + ' ' + Math.round(ny) + ' ' + Math.round(pvb.w) + ' ' + Math.round(pvb.h);
            if (svg) svg.setAttribute('viewBox', state.viewBox);
            const vbInput = $('#viewbox-input'); if (vbInput) vbInput.value = state.viewBox;
            return;
        }

        // GROUP DRAG — move every selected element by the same (snapped) delta.
        if (drag && drag.group) {
            const dxs = snap(w.x - drag.startX);
            const dys = snap(w.y - drag.startY);
            let changed = false;
            for (const id of drag.ids) {
                const gc = cellById(id); if (!gc) continue;
                const b = drag.base[id]; if (!b) continue;
                const nx = b.x + dxs, ny = b.y + dys;
                if (nx !== gc.position.x || ny !== gc.position.y) { gc.position.x = nx; gc.position.y = ny; changed = true; }
            }
            if (changed) { drag.moved = true; renderCellsLive(drag.ids); }
            return;
        }

        // DRAG (original behaviour)
        if (!drag) return;
        const c = cellById(drag.id);
        if (!c) return;
        const nx = snap(w.x - drag.offX);
        const ny = snap(w.y - drag.offY);
        if (nx !== c.position.x || ny !== c.position.y) {
            c.position.x = nx;
            c.position.y = ny;
            drag.moved = true;
            renderCellLive(c);   // in-place update — no full rebuild, no flicker
        }
    }

    function onCanvasMouseUp() {
        // One full render() at the end reconciles rails / inspector / label
        // handle after the in-place live updates during the drag.
        let changed = false;
        if (rotate && rotate.moved) { pushHistory(); changed = true; }
        if (resize && resize.moved) { pushHistory(); changed = true; }
        if (standDrag && standDrag.moved) { pushHistory(); changed = true; }
        if (attachDrag && attachDrag.moved) { pushHistory(); changed = true; }
        if (labelDrag && labelDrag.moved) { pushHistory(); changed = true; }
        if (drag && drag.group) {
            if (drag.moved) { pushHistory(); changed = true; }
            else if (drag.clickEid) {
                // Clicked (no drag) on a member of the group → reduce to that one.
                state.selectedIds = [drag.clickEid];
                state.selectedId = drag.clickEid;
                changed = true;
            }
        } else if (drag && drag.moved) {
            // If the cell being dragged is a signal lamp and we let go over
            // a SignalBackground, snap-fit it to the tray.
            const c = cellById(drag.id);
            if (c) maybeSnapToBackground(c);
            pushHistory();
            changed = true;
        }
        if (canvasPan) {
            if (canvasPan.moved) pushHistory();          // panned → remember viewBox
            else if (state.selectedIds.length || state.selectedId) { clearSelection(); changed = true; } // plain click → deselect
        }
        drag = null;
        resize = null;
        rotate = null;
        standDrag = null;
        attachDrag = null;
        labelDrag = null;
        canvasPan = null;
        if (changed) render();
    }

    function onKey(evt) {
        if (evt.target && /^(INPUT|TEXTAREA|SELECT)$/.test(evt.target.tagName)) return;
        if (evt.key === 'Delete' || evt.key === 'Backspace') {
            if (state.selectedIds.length || state.selectedId) { deleteSelected(); evt.preventDefault(); }
        } else if (evt.key === 'Escape') {
            if (state.selectedIds.length || state.selectedId) { clearSelection(); render(); }
        } else if ((evt.ctrlKey || evt.metaKey) && evt.key.toLowerCase() === 'a') {
            evt.preventDefault(); selectAll();
        } else if ((evt.ctrlKey || evt.metaKey) && evt.key.toLowerCase() === 'z') {
            evt.preventDefault();
            if (evt.shiftKey) redo(); else undo();
        } else if ((evt.ctrlKey || evt.metaKey) && evt.key.toLowerCase() === 'y') {
            evt.preventDefault(); redo();
        } else if ((evt.ctrlKey || evt.metaKey) && evt.key.toLowerCase() === 'd') {
            evt.preventDefault(); duplicateSelected();
        }
    }

    function deleteSelected() {
        const ids = state.selectedIds.length ? state.selectedIds.slice()
            : (state.selectedId ? [state.selectedId] : []);
        if (!ids.length) return;
        const del = {};
        ids.forEach(id => { del[id] = true; });
        state.cells = state.cells.filter(c => !del[c.id]);
        clearSelection();
        pushHistory();
        render();
    }
    function duplicateSelected() {
        const c = selectedCell();
        if (!c) return;
        const copy = deepClone(c);
        copy.id = SIP._uid();
        copy.position.x += 20;
        copy.position.y += 20;
        copy.z = (state.cells.reduce((m, cc) => Math.max(m, cc.z || 0), 0) + 1);
        state.cells.push(copy);
        state.selectedId = copy.id;
        state.selectedIds = [copy.id];
        pushHistory();
        render();
    }

    /* ------------------------------------------------------------------------ *
     *  IMPORT / EXPORT MODAL
     * ------------------------------------------------------------------------ */
    let modalMode = 'export';

    function openImport() {
        modalMode = 'import';
        $('#modal-title').textContent = 'IMPORT';
        $('#modal-hint').textContent = 'Paste OLD-Rappid { "cells": [...] } JSON, then click Import.';
        $('#modal-text').value = JSON.stringify({ cells: state.cells }, null, 2);
        $('#modal-action').textContent = 'Import';
        $('#modal').classList.add('show');
    }
    function exportLayoutJs() {
        modalMode = 'export-js';
        $('#modal-title').textContent = 'EXPORT  ·  .JS';
        $('#modal-hint').textContent = 'CommonJS module — exports the cells array verbatim.';
        const code =
            `// Generated by SipEditor.\n` +
            `// Paste this into a .js file alongside sip-library.js & call SIP.renderYard({ viewBox, cells }).\n` +
            `const cells = ${JSON.stringify(state.cells, null, 2)};\n` +
            `const yardSvg = SIP.renderYard({ viewBox: '${state.viewBox}', cells });\n` +
            `module.exports = { yardSvg, cells };\n`;
        $('#modal-text').value = code;
        $('#modal-action').textContent = 'Download';
        $('#modal').classList.add('show');
    }
    function exportSvg() {
        modalMode = 'export-svg';
        $('#modal-title').textContent = 'EXPORT  ·  .SVG';
        $('#modal-hint').textContent = 'A standalone SVG of the current yard.';
        $('#modal-text').value = SIP.renderYard({ viewBox: state.viewBox, cells: state.cells });
        $('#modal-action').textContent = 'Download';
        $('#modal').classList.add('show');
    }
    function closeModal() { $('#modal').classList.remove('show'); }
    function copyModal() {
        const ta = $('#modal-text');
        ta.select();
        try { document.execCommand('copy'); toast('Copied to clipboard.'); }
        catch (e) { toast('Copy failed.'); }
    }
    function downloadModal() {
        const t = $('#modal-text').value;
        if (modalMode === 'import') {
            try {
                const parsed = JSON.parse(t);
                setLayout(parsed);
                toast('Imported ' + state.cells.length + ' cells.');
                closeModal();
            } catch (e) {
                toast('Invalid JSON: ' + e.message);
            }
            return;
        }
        const ext = modalMode === 'export-svg' ? 'svg' : 'js';
        const mime = modalMode === 'export-svg' ? 'image/svg+xml' : 'text/javascript';
        const blob = new Blob([t], { type: mime });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'sip-yard.' + ext;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }

    /* ------------------------------------------------------------------------ *
     *  PRESETS (Blank / Sample)
     * ------------------------------------------------------------------------ */
    function loadPreset(name) {
        if (name === 'sample' || name === 'arnetha') {
            state.cells = samplePreset();
            state.viewBox = '0 0 1400 400';
        } else {
            state.cells = [];
            state.viewBox = '0 0 2000 740';
        }
        state.selectedId = null; state.selectedIds = [];
        const vbInput = $('#viewbox-input');
        if (vbInput) vbInput.value = state.viewBox;
        pushHistory();
        render();
    }

    function samplePreset() {
        // A tiny demonstration yard — two parallel tracks, one point, one signal.
        const cells = [];
        cells.push(Object.assign(SIP.makeCell('examples.Track1', 400, 160), { size: { width: 600, height: 100 } }));
        cells[cells.length - 1].attrs.label.text = '3T1';
        cells.push(Object.assign(SIP.makeCell('examples.Track1', 400, 320), { size: { width: 600, height: 100 } }));
        cells[cells.length - 1].attrs.label.text = '5T';
        cells.push(SIP.makeCell('examples.PointMachine', 720, 240));
        cells[cells.length - 1].attrs.label.text = '06';
        cells.push(SIP.makeCell('examples.Signald90', 900, 100));
        cells[cells.length - 1].attrs.label.text = 'S18 RG';
        cells.push(SIP.makeCell('examples.Signal90', 950, 100));
        cells[cells.length - 1].attrs.label.text = 'S18 DG';
        cells.push(SIP.makeCell('examples.Signal45', 1000, 100));
        cells[cells.length - 1].attrs.label.text = 'S18 HG';
        cells.push(SIP.makeCell('examples.BusBar', 1200, 50));
        cells[cells.length - 1].attrs.label.text = 'B1';
        return cells;
    }

    /* ------------------------------------------------------------------------ *
     *  FIT ALL — auto-zoom to show every element
     * ------------------------------------------------------------------------ */
    function fitAll() {
        if (!state.cells.length) {
            toast('No elements to fit.');
            return;
        }
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const c of state.cells) {
            const x = (c.position && c.position.x) || 0;
            const y = (c.position && c.position.y) || 0;
            const w = (c.size && c.size.width) || 60;
            const h = (c.size && c.size.height) || 60;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w;
            if (y + h > maxY) maxY = y + h;
        }
        const pad = 80;
        state.viewBox = Math.floor(minX - pad) + ' ' + Math.floor(minY - pad) + ' ' +
            Math.ceil(maxX - minX + pad * 2) + ' ' + Math.ceil(maxY - minY + pad * 2);
        const vbInput = $('#viewbox-input');
        if (vbInput) vbInput.value = state.viewBox;
        pushHistory();
        render();
        toast('Fitted to ' + state.cells.length + ' elements');
    }

    /* ------------------------------------------------------------------------ *
     *  FULLSCREEN
     * ------------------------------------------------------------------------ */
    function toggleFullscreen() {
        const app = document.querySelector('.sip-app');
        if (!app) return;
        app.classList.toggle('sip-fullscreen');
        setTimeout(render, 50);
    }

    /* ------------------------------------------------------------------------ *
     *  PUBLIC: getLayout / setLayout — OLD RAPPID FORMAT
     * ------------------------------------------------------------------------ */
    function getLayout() {
        // Return a CLEAN { cells: [...] } object that drops straight into the
        // advancesipview.SipView1 column. The shape is byte-for-byte the same as
        // the original Rappid `app.graph.toJSON()` output, so Sview.cshtml reads
        // it with zero changes.
        return { cells: deepClone(state.cells) };
    }

    function setLayout(layout) {
        if (!layout) return;

        // Accept three shapes:
        //   1. { cells: [...] }   — canonical OLD Rappid format (preferred)
        //   2. [<cell>, …]        — bare array of Rappid cells
        //   3. [{type, props}, …] — NEW Aurora-editor element shape; convert
        let cells = null;
        if (Array.isArray(layout)) {
            // Heuristic: cells have `position`; new elements have `props`.
            if (layout.length === 0) {
                cells = [];
            } else if (layout[0].position || layout[0].size || layout[0].attrs) {
                cells = layout;
            } else if (layout[0].props || layout[0].type) {
                cells = convertNewElementsToCells(layout);
            } else {
                cells = layout; // last-ditch
            }
        } else if (layout.cells && Array.isArray(layout.cells)) {
            cells = layout.cells;
        } else if (layout.elements && Array.isArray(layout.elements)) {
            cells = convertNewElementsToCells(layout.elements);
        } else {
            console.warn('[sip-editor] setLayout: unrecognised payload shape, ignoring.');
            return;
        }

        // Make sure every cell has an id and a minimal attrs.label.
        state.cells = cells.map(function (c) {
            const c2 = deepClone(c);
            if (!c2.id) c2.id = SIP._uid();
            c2.attrs = c2.attrs || {};
            c2.attrs.label = c2.attrs.label || { text: '' };
            // Fill in defaults from the spec where they're missing (so first render
            // doesn't fail on an unknown asset).
            const s = SIP.spec(c2.type);
            if (s) {
                for (const k of Object.keys(s.attrs)) {
                    c2.attrs[k] = Object.assign({}, s.attrs[k], c2.attrs[k] || {});
                }
                if (!c2.size) c2.size = Object.assign({}, s.size);
            } else if (!c2.size) {
                c2.size = { width: 100, height: 100 };
            }
            if (!c2.position) c2.position = { x: 0, y: 0 };
            return c2;
        });

        // Auto-fit viewBox to the loaded cells. The legacy editor saved real-world
        // yards spanning x:0..2700 and y:0..1100; our default 0 0 2000 740 viewBox
        // would crop the right half. Compute the actual extent and resize so the
        // operator can see everything on load.
        if (state.cells.length > 0) {
            let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
            for (const c of state.cells) {
                const x = (c.position && c.position.x) || 0;
                const y = (c.position && c.position.y) || 0;
                const w = (c.size && c.size.width) || 60;
                const h = (c.size && c.size.height) || 60;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x + w > maxX) maxX = x + w;
                if (y + h > maxY) maxY = y + h;
            }
            const pad = 80;
            const vbX = Math.floor(minX - pad);
            const vbY = Math.floor(minY - pad);
            const vbW = Math.ceil((maxX - minX) + pad * 2);
            const vbH = Math.ceil((maxY - minY) + pad * 2);
            state.viewBox = vbX + ' ' + vbY + ' ' + vbW + ' ' + vbH;
            const vbInput = $('#viewbox-input');
            if (vbInput) vbInput.value = state.viewBox;
        }

        state.selectedId = null; state.selectedIds = [];
        pushHistory();
        render();
    }

    /* ------------------------------------------------------------------------ *
     *  NEW-format → OLD cells converter (used only by setLayout's input
     *  detection, so a save from an older iteration of this editor still loads)
     * ------------------------------------------------------------------------ */
    function convertNewElementsToCells(els) {
        if (!Array.isArray(els)) return [];
        const cells = [];
        const xoverByKey = {};
        const pointByKey = {};
        for (const e of els) {
            if (!e || !e.type) continue;
            const k = (e.assetName || '') + '::' + (e.stencilType || '');
            if (e.type === 'crossover') xoverByKey[k] = e;
            else if (e.type === 'point') pointByKey[k] = e;
        }
        const emittedPM = {};

        const LAMP_TYPE = {
            R: 'examples.Signald90', r: 'examples.Signald90',
            G: 'examples.Signal90', g: 'examples.Signal90',
            Y: 'examples.Signal45', y: 'examples.Signal45',
            X: 'examples.Signald45', x: 'examples.Signald45',
            W: 'examples.Signald90'
        };
        const LAMP_SUFFIX = { R: 'RG', r: 'RG', G: 'DG', g: 'DG', Y: 'HG', y: 'HG', X: 'HHG', x: 'HHG', W: 'RG' };

        function newCell(type, x, y, w, h, attrs) {
            return {
                type, position: { x, y }, size: { width: w, height: h },
                angle: 0, id: SIP._uid(), z: cells.length + 1, attrs
            };
        }

        for (const e of els) {
            const p = e.props || {};
            switch (e.type) {
                case 'rail':
                    // Skipped — sections render the rail.
                    break;
                case 'section': {
                    const stencil = e.stencilType || 'examples.Track1';
                    const w = +p.width || (stencil === 'examples.Track' ? 300 : stencil === 'examples.Track2' ? 400 : 100);
                    const lbl = p.label != null ? String(p.label) : (e.assetName || 'T');
                    cells.push(newCell(stencil, (+p.x || 0) - w / 2, (+p.y || 0) - 50, w, 100, {
                        path: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3 },
                        label: { text: lbl, fill: '#d7d7d7', fontSize: 14, fontWeight: 'bold' }
                    }));
                    break;
                }
                case 'crossover':
                case 'point': {
                    const k = (e.assetName || '') + '::' + (e.stencilType || '');
                    if (emittedPM[k]) break;
                    const co = xoverByKey[k], pt = pointByKey[k];
                    const stencil = (co && co.stencilType) || (pt && pt.stencilType) || 'examples.PointMachine';
                    let x, y, w, h;
                    if (co) {
                        const cp = co.props || {};
                        x = Math.min(+cp.x1, +cp.x2);
                        y = Math.min(+cp.y1, +cp.y2);
                        w = Math.max(60, Math.abs(+cp.x2 - +cp.x1));
                        h = Math.max(60, Math.abs(+cp.y2 - +cp.y1));
                    } else {
                        const pp = pt.props || {};
                        x = (+pp.x || 0) - 50; y = (+pp.y || 0) - 30; w = 100; h = 60;
                    }
                    const lbl = (pt && pt.props && pt.props.label) || (co && co.props && co.props.label) || e.assetName || '';
                    const isMirror = stencil === 'examples.PointMachine1';
                    cells.push(newCell(stencil, x, y, w, h, {
                        body: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 3 },
                        circle1: { cx: isMirror ? 54 : 35, cy: isMirror ? 50 : 54, r: 6, stroke: 'black', fill: '#d4d4d4' },
                        label: { text: lbl, fill: '#ffffff', fontSize: 14, fontWeight: 'bold' }
                    }));
                    emittedPM[k] = true;
                    break;
                }
                case 'signal': {
                    const lamps = String(p.lamps || 'R');
                    const prefix = (p.label != null && p.label !== '')
                        ? String(p.label).trim()
                        : String(e.assetName || '').replace(/\s*\(.*\)\s*$/, '').trim();
                    const spacing = 50;
                    const startX = (+p.x || 0) - ((lamps.length - 1) / 2) * spacing;
                    for (let i = 0; i < lamps.length; i++) {
                        const ch = lamps.charAt(i);
                        const t = LAMP_TYPE[ch] || 'examples.Signald90';
                        const lx = startX + i * spacing;
                        const txt = prefix ? (prefix + ' ' + (LAMP_SUFFIX[ch] || '')) : '';
                        cells.push(newCell(t, lx - 50, (+p.y || 0) - 30, 100, 60, {
                            circle1: { cx: 10, cy: 10, r: 10, stroke: 'black', fill: '#d4d4d4' },
                            path1: { type: 'path', fill: 'none', stroke: '#6a7596', strokeWidth: 1 },
                            label: { text: txt, fill: '#d7d7d7', fontSize: 11, fontWeight: 'bold' }
                        }));
                    }
                    break;
                }
                case 'shunt': {
                    const stencil = e.stencilType || (String(p.state) === '2' ? 'examples.Shaunt2' : 'examples.Shaunt');
                    cells.push(newCell(stencil, (+p.x || 0) - 50, (+p.y || 0) - 50, 100, 100, {
                        body: { type: 'Path', fill: 'none', stroke: '#6a7596', strokeWidth: 3 },
                        label: { text: p.label || e.assetName || '', fill: '#d7d7d7', fontSize: 14, fontWeight: 'bold' }
                    }));
                    break;
                }
                case 'sidingLabel': {
                    const stencil = e.stencilType || 'examples.Post';
                    const sz = (stencil === 'examples.BusBar')
                        ? { w: 100, h: 25 }
                        : { w: 100, h: 100 };
                    cells.push(newCell(stencil, (+p.x || 0) - sz.w / 2, (+p.y || 0) - sz.h / 2, sz.w, sz.h, {
                        label: { text: p.text || e.assetName || '', fill: '#d7d7d7', fontSize: 16, fontWeight: 'bold' }
                    }));
                    break;
                }
                case 'topLine':
                case 'bottomLine': {
                    const stencil = e.stencilType ||
                        (e.type === 'topLine' ? 'examples.TopLine' : 'examples.BottomLine');
                    // Default L-bracket size if length isn't specified
                    const w = +p.width || 37;
                    const h = +p.length || +p.height || (e.type === 'topLine' ? 49 : 56);
                    cells.push(newCell(stencil, (+p.x || 0), (+p.y || 0), w, h, {
                        path: { type: 'path', fill: 'none', stroke: '#606060', strokeWidth: 4 }
                    }));
                    break;
                }
            }
        }
        return cells;
    }

    /* ------------------------------------------------------------------------ *
     *  RIGHT-CLICK CONTEXT MENU — place assets at click position
     * ------------------------------------------------------------------------ *
     *  Right-click on empty canvas → shows asset picker grouped by category
     *  Right-click on an element   → shows element actions + asset picker
     *  Click an asset item         → places it at the right-click world position
     * ------------------------------------------------------------------------ */
    let ctxMenu = null;       // the floating <div> element
    let ctxWorldPos = null;   // { x, y } world coordinates where user right-clicked

    function showContextMenu(evt) {
        evt.preventDefault();
        closeContextMenu();

        // Compute world position of the click
        ctxWorldPos = clientToWorld(evt);

        // Check if right-clicking on an existing element
        let clickedCell = null;
        let node = evt.target;
        while (node && node !== canvasEl) {
            if (node.classList && node.classList.contains('editable')) {
                const eid = node.getAttribute('data-eid');
                clickedCell = cellById(eid);
                break;
            }
            node = node.parentNode;
        }

        // Build the menu HTML
        let html = '';

        // If clicked on an element, show element actions first
        if (clickedCell) {
            state.selectedId = clickedCell.id;
            state.selectedIds = [clickedCell.id];
            render();
            const spec = SIP.spec(clickedCell.type);
            html += `<div class="ctx-header">${spec ? spec.label : clickedCell.type}</div>`;
            html += `<div class="ctx-item" data-action="duplicate"><span class="ctx-key">Ctrl+D</span>Duplicate</div>`;
            html += `<div class="ctx-item ctx-danger" data-action="delete"><span class="ctx-key">Del</span>Delete</div>`;
            html += `<div class="ctx-sep"></div>`;
        }

        // Asset picker — grouped by category (click-to-expand accordion)
        html += `<div class="ctx-header">Place asset here</div>`;
        for (const grp of SIP.GROUPS) {
            const items = SIP.PALETTE.filter(t => {
                const s = SIP.spec(t);
                return s && s.group === grp.key && !s.hidden;
            });
            if (!items.length) continue;

            html += `<div class="ctx-group" data-grp="${grp.key}">`;
            html += `<div class="ctx-group-label" data-toggle="${grp.key}">${grp.label}<span class="ctx-arrow">+</span></div>`;
            html += `<div class="ctx-subitems" id="ctx-sub-${grp.key}" style="display:none;">`;
            for (const type of items) {
                const s = SIP.spec(type);
                html += `<div class="ctx-item ctx-subitem" data-place="${type}">${s.label}</div>`;
            }
            html += `</div></div>`;
        }

        // Create the menu element
        ctxMenu = document.createElement('div');
        ctxMenu.className = 'sip-ctx-menu';
        ctxMenu.innerHTML = html;

        // Position near the mouse (keep within viewport)
        const mw = 240;
        let mx = evt.clientX;
        let my = evt.clientY;
        if (mx + mw > window.innerWidth - 10) mx = window.innerWidth - mw - 10;
        if (my + 350 > window.innerHeight - 10) my = Math.max(10, window.innerHeight - 360);
        ctxMenu.style.left = mx + 'px';
        ctxMenu.style.top = my + 'px';

        document.body.appendChild(ctxMenu);

        // Wire group toggle (click to expand/collapse)
        ctxMenu.querySelectorAll('.ctx-group-label[data-toggle]').forEach(lbl => {
            lbl.addEventListener('click', function () {
                const key = this.getAttribute('data-toggle');
                const sub = document.getElementById('ctx-sub-' + key);
                const arrow = this.querySelector('.ctx-arrow');
                if (!sub) return;
                const isOpen = sub.style.display !== 'none';
                // Close all other groups first
                ctxMenu.querySelectorAll('.ctx-subitems').forEach(s => { s.style.display = 'none'; });
                ctxMenu.querySelectorAll('.ctx-arrow').forEach(a => { a.textContent = '+'; });
                // Toggle this one
                if (!isOpen) {
                    sub.style.display = 'block';
                    if (arrow) arrow.textContent = '\u2212';  // minus sign
                    // Recheck if menu goes below viewport, scroll into view
                    var menuRect = ctxMenu.getBoundingClientRect();
                    if (menuRect.bottom > window.innerHeight - 10) {
                        ctxMenu.style.top = Math.max(10, window.innerHeight - menuRect.height - 10) + 'px';
                    }
                }
            });
        });

        // Wire asset placement click handlers
        ctxMenu.querySelectorAll('.ctx-item[data-place]').forEach(item => {
            item.addEventListener('click', () => {
                const type = item.getAttribute('data-place');
                if (type && ctxWorldPos) {
                    const c = SIP.makeCell(type, snap(ctxWorldPos.x), snap(ctxWorldPos.y));
                    c.z = (state.cells.reduce((m, cc) => Math.max(m, cc.z || 0), 0) + 1);
                    state.cells.push(c);
                    maybeSnapToBackground(c);
                    state.selectedId = c.id; state.selectedIds = [c.id];
                    trackRecentType(type);
                    pushHistory();
                    render();
                    toast('Added ' + SIP.spec(type).label);
                }
                closeContextMenu();
            });
        });
        // Wire element action click handlers
        ctxMenu.querySelectorAll('.ctx-item[data-action]').forEach(item => {
            item.addEventListener('click', () => {
                const action = item.getAttribute('data-action');
                if (action === 'duplicate') duplicateSelected();
                else if (action === 'delete') deleteSelected();
                closeContextMenu();
            });
        });

        // Close on click outside or Escape
        setTimeout(() => {
            document.addEventListener('mousedown', onCtxOutsideClick);
            document.addEventListener('keydown', onCtxEscape);
        }, 10);
    }

    function closeContextMenu() {
        if (ctxMenu && ctxMenu.parentNode) {
            ctxMenu.parentNode.removeChild(ctxMenu);
        }
        ctxMenu = null;
        ctxWorldPos = null;
        document.removeEventListener('mousedown', onCtxOutsideClick);
        document.removeEventListener('keydown', onCtxEscape);
    }

    function onCtxOutsideClick(evt) {
        if (ctxMenu && !ctxMenu.contains(evt.target)) {
            closeContextMenu();
        }
    }
    function onCtxEscape(evt) {
        if (evt.key === 'Escape') closeContextMenu();
    }

    /* ------------------------------------------------------------------------ *
     *  GENERAL WIRE-UP
     * ------------------------------------------------------------------------ */
    function init() {
        canvasEl = document.getElementById('canvas');
        toolboxEl = document.getElementById('toolbox-grid');
        inspectorEl = document.getElementById('inspector-body');
        statusEls = {
            coords: document.getElementById('cursor-coords'),
            count: document.getElementById('element-count'),
            sel: document.getElementById('selection-label')
        };

        if (!canvasEl || !toolboxEl) {
            console.error('[sip-editor] required DOM elements missing.');
            return;
        }

        buildToolbox();

        // Pointer interaction
        canvasEl.addEventListener('mousedown', onCanvasMouseDown);
        document.addEventListener('mousemove', onCanvasMouseMove);
        document.addEventListener('mouseup', onCanvasMouseUp);
        document.addEventListener('keydown', onKey);
        installEditorZoomControls();

        // Right-click context menu
        canvasEl.addEventListener('contextmenu', showContextMenu);

        // ---- ZOOM: scroll-wheel zooms around cursor position ----
        canvasEl.addEventListener('wheel', function (evt) {
            evt.preventDefault();
            const vb = parseViewBox(state.viewBox);
            const cursor = clientToWorld(evt);
            const scale = evt.deltaY > 0 ? 1.12 : 0.89;   // zoom out / zoom in

            const newW = vb.w * scale;
            const newH = vb.h * scale;
            // Keep the cursor point stationary on screen
            const newX = cursor.x - (cursor.x - vb.x) * scale;
            const newY = cursor.y - (cursor.y - vb.y) * scale;

            state.viewBox = Math.round(newX) + ' ' + Math.round(newY) + ' ' +
                Math.round(newW) + ' ' + Math.round(newH);
            const vbInput = $('#viewbox-input');
            if (vbInput) vbInput.value = state.viewBox;
            render();
        }, { passive: false });

        // ---- PAN: middle-button drag moves the viewport ----
        let panState = null;
        canvasEl.addEventListener('mousedown', function (evt) {
            if (evt.button === 1) {  // middle mouse button
                evt.preventDefault();
                panState = {
                    startX: evt.clientX,
                    startY: evt.clientY,
                    vb: parseViewBox(state.viewBox)
                };
            }
        });
        document.addEventListener('mousemove', function (evt) {
            if (!panState) return;
            const svgEl = canvasEl.querySelector('svg');
            if (!svgEl) return;
            const rect = svgEl.getBoundingClientRect();
            const vb = panState.vb;
            const dx = (evt.clientX - panState.startX) * (vb.w / rect.width);
            const dy = (evt.clientY - panState.startY) * (vb.h / rect.height);
            state.viewBox = Math.round(vb.x - dx) + ' ' + Math.round(vb.y - dy) +
                ' ' + vb.w + ' ' + vb.h;
            const vbInput = $('#viewbox-input');
            if (vbInput) vbInput.value = state.viewBox;
            render();
        });
        document.addEventListener('mouseup', function (evt) {
            if (panState && evt.button === 1) {
                pushHistory();
                panState = null;
            }
        });

        // Snap controls
        const snapToggle = $('#snap-toggle');
        if (snapToggle) snapToggle.addEventListener('change', () => { state.snap.enabled = snapToggle.checked; });
        const snapSize = $('#snap-size');
        if (snapSize) snapSize.addEventListener('change', () => {
            const v = parseInt(snapSize.value, 10); if (v > 0) state.snap.size = v;
        });

        // ViewBox
        const vb = $('#viewbox-input');
        if (vb) {
            vb.value = state.viewBox;
            vb.addEventListener('change', () => {
                const parts = vb.value.trim().split(/\s+/).map(Number);
                if (parts.length === 4 && parts.every(n => !isNaN(n))) {
                    state.viewBox = parts.join(' ');
                    render();
                    pushHistory();
                }
            });
        }

        // Initial snapshot for undo
        pushHistory();
        render();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    /* ------------------------------------------------------------------------ *
     *  Public façade
     * ------------------------------------------------------------------------ */
    window.SipEditor = {
        loadPreset, undo, redo,
        openImport, exportLayoutJs, exportSvg,
        closeModal, copyModal, downloadModal,
        duplicateSelected, deleteSelected,
        toggleFullscreen, fitAll, selectAll, zoomBy,
        getLayout, setLayout,
        refreshAssetRegistry, loadAssetsForType
    };
})();
