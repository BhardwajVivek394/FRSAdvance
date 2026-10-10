/* ── SIP View — yard-diagram replay ─────────────────────────────────────────
   Self-contained module (window.SIP). Mounted into #sipView by index.html and
   kicked off from switchTab('sip').

   All SIP APIs now go through ELDAIAnalysisController (URLs injected as
   window.SIP_ENDPOINTS by index.cshtml) instead of hitting /api/eld/... directly:
     GetSipSites       -> api/eld/sip/sites
     GetSipLayout      -> api/eld/sip/layout/{site}   (GET load + POST save)
     GetSipList        -> api/eld/list?site_id=
     GetSipLogs        -> api/eld/logs?eld_id=&start_date=&end_date=
     GetSipChange      -> api/eld/change/{eld}?at=&before_min=&after_min=&full=true
     GetSipTrackCircuit-> api/eld/sip/track-circuit/{eld}?asset_id=&at=&...
     GetSipPmOperations-> api/eld/sip/pm-operations/{site}/{asset}?at=
   Runtime view is pure vector SVG; the sipview PNG is only an optional tracing
   underlay in edit mode.                                                        */
window.SIP = (function () {
    const NS = 'http://www.w3.org/2000/svg';
    const PRE_SEC = 180, POST_SEC = 5;              // window: pivot-180s .. pivot+5s
    const WIN_MS = (PRE_SEC + POST_SEC) * 1000;

    const S = {
        inited: false, sites: [], site: null, layout: null,
        elds: [], eldId: null, eldName: null,
        transitions: [], pivotIso: null, win: null, bind: {},
        t: 0,                                           // 0..1 scrub position
        playing: false, raf: 0, lastFrame: 0, speed: 1, playDur: 16,
        editing: false, sel: null, dirty: false, drag: null, showRef: false,
        savedPositions: {},
        seq: 0,          // bumped on every new load; stale async responses self-cancel
        opSeq: 0,        // separate guard for the point-machine operations fetch
        eventsChrono: [],// all triggers oldest->recent (the continuous replay timeline)
        evi: 0,          // index of the event currently playing
        evWins: {},      // cache of loaded windows by event index (avoids re-fetch / lag)
    };

    const $ = s => document.querySelector(s);
    const svgEl = (tag, attrs) => { const e = document.createElementNS(NS, tag); for (const k in (attrs || {})) e.setAttribute(k, attrs[k]); return e; };
    const norm = s => (s || '').toString().trim().toUpperCase().replace(/\s+/g, '');
    const num = v => (typeof v === 'number' ? v : (v == null || v === '' ? null : (isNaN(+v) ? null : +v)));
    const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

    // All SIP APIs go through ELDAIAnalysisController (URLs injected as window.SIP_ENDPOINTS).
    const EP = (window.SIP_ENDPOINTS || {});
    const qp = o => Object.keys(o).filter(k => o[k] !== undefined && o[k] !== null && o[k] !== '')
        .map(k => `${encodeURIComponent(k)}=${encodeURIComponent(o[k])}`).join('&');

    // ── init / controls ─────────────────────────────────────────────────────
    async function init() {
        if (!S.inited) { buildShell(); S.inited = true; await loadSites(); }
    }

    function buildShell() {
        const v = $('#sipView');
        v.innerHTML = `
      <div class="sip-controls">
        <label>Site<select id="sipSite"></select></label>
        <label>From<input type="datetime-local" id="sipFrom"></label>
        <label>To<input type="datetime-local" id="sipTo"></label>
        <label class="grow">ELD
          <select id="sipEld"><option value="">— select ELD —</option></select></label>
        <span class="sip-spacer"></span>
        <button class="sip-btn" id="sipRefBtn" title="Show the original sipview image faintly (editor aid)">Reference</button>
        <button class="sip-btn" id="sipFitBtn" title="Zoom to fit the whole yard">Fit</button>
        <button class="sip-btn" id="sipEditBtn" title="Move signals/points/tracks and save the layout">✎ Edit layout</button>
      </div>
      <div class="sip-body">
        <div class="sip-left">
          <div class="sip-panel">
            <h3>Trigger timestamps <span id="sipTsCount" class="off"></span></h3>
            <div class="sip-ts-list" id="sipTsList"><div class="sip-hint">Pick an ELD to see its triggers.</div></div>
          </div>
          <div class="sip-panel">
            <h3>Effected assets <span id="sipGearHdr" class="off"></span></h3>
            <div id="sipGearList"><div class="sip-hint">Pick a timestamp to load the −180s…+5s window.</div></div>
          </div>
        </div>
        <div class="sip-right">
          <div class="sip-stage-wrap">
            <div class="sip-stage" id="sipStage">
              <svg id="sipSvg" viewBox="0 0 1360 380" preserveAspectRatio="xMidYMid meet"></svg>
            </div>
            <div class="sip-transport">
              <button class="sip-play" id="sipPlay" title="Play / pause">▶</button>
              <div class="sip-timeline" id="sipTimeline" title="Every ELD event in series (oldest → recent). Each segment = one trigger's −180s…+5s window; red tick = the ELD moment. Click a segment, or press play to run through them all."></div>
              <span class="sip-speed">Speed
                <select id="sipSpeed" style="font-size:11px;padding:3px 5px;border-radius:6px;background:var(--panel,#0f1626);color:#dce6f5;border:1px solid var(--line,#26324a)">
                  <option value="0.5">0.5×</option><option value="1" selected>1×</option>
                  <option value="2">2×</option><option value="4">4×</option><option value="8">8×</option>
                </select></span>
              <span class="sip-tlabel" id="sipTLabel">—</span>
            </div>
            <div class="sip-editbar" id="sipEditBar">
              <span>Editing <b id="sipEditSite"></b> — drag elements on the map.</span>
              <span class="sep"></span>
              <span id="sipSelInfo">No element selected.</span>
              <label style="display:flex;gap:4px;align-items:center">x<input type="number" id="sipSelX" step="1"></label>
              <label style="display:flex;gap:4px;align-items:center">y<input type="number" id="sipSelY" step="1"></label>
              <span class="sep"></span>
              <button class="sip-btn" id="sipRefBtn2">Toggle reference</button>
              <span class="sip-spacer"></span>
              <span id="sipDirty"></span>
              <span id="sipChangedList" class="sip-changed-list">No position changes.</span>
              <button class="sip-btn primary" id="sipSaveBtn">💾 Save layout</button>
            </div>
          </div>
        </div>
      </div>`;

        // wire controls
        $('#sipSite').onchange = onSiteChange;
        $('#sipEld').onchange = onEldChange;
        $('#sipFrom').onchange = () => { if (S.eldId) loadTransitions(); };
        $('#sipTo').onchange = () => { if (S.eldId) loadTransitions(); };
        $('#sipFitBtn').onclick = fitAll;
        $('#sipRefBtn').onclick = toggleRef;
        $('#sipRefBtn2').onclick = toggleRef;
        $('#sipEditBtn').onclick = toggleEdit;
        $('#sipSaveBtn').onclick = saveLayout;
        $('#sipPlay').onclick = togglePlay;
        $('#sipSpeed').onchange = e => { S.speed = +e.target.value; };
        $('#sipSelX').onchange = onSelCoordEdit;
        $('#sipSelY').onchange = onSelCoordEdit;

        // default range: last 7 days → now
        const toLocal = dt => new Date(dt.getTime() - dt.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
        const now = new Date();
        $('#sipTo').value = toLocal(now);
        const from = new Date(now); from.setMonth(from.getMonth() - 6);   // default: last 6 months
        $('#sipFrom').value = toLocal(from);

        // sticky offsets: keep the controls bar under the page header, and pin the
        // map just below the controls (so scrolling the asset list never hides the
        // controls or shoves the map to the very top).
        const setStickyTops = () => {
            const hdr = document.querySelector('header');
            const hH = hdr ? hdr.offsetHeight : 54;
            const ctr = document.querySelector('#sipView .sip-controls');
            const cH = ctr ? ctr.offsetHeight : 58;
            const view = $('#sipView');
            view.style.setProperty('--sip-header', hH + 'px');
            view.style.setProperty('--sip-sticky', (hH + cH + 8) + 'px');
        };
        setStickyTops();
        window.addEventListener('resize', setStickyTops);
        setTimeout(setStickyTops, 60);   // after layout settles

        // editor drag on the svg
        const svg = $('#sipSvg');
        svg.addEventListener('pointerdown', onPointerDown);
        svg.addEventListener('pointermove', onPointerMove);
        svg.addEventListener('pointerup', onPointerUp);
        svg.addEventListener('pointerleave', onPointerUp);
        // click a mapped asset on the yard (replay mode) → open its circuit
        svg.addEventListener('click', e => {
            if (S.editing) return;
            const g = e.target.closest('.el-group'); if (!g) return;
            const rec = S.nodes[g.getAttribute('data-id')]; if (!rec) return;
            openCircuitForAsset(rec.el.asset);
        });
    }

    // open the circuit for a yard element by its asset name (matches the effected gear)
    function openCircuitForAsset(asset) {
        const gears = S.gearsRendered || [];
        const gi = gears.findIndex(g => norm(g.asset_name) === norm(asset) && g.asset_id != null
            && ['TRACK', 'SIGNAL', 'POINT MACHINE'].includes((g.asset_type || '').toUpperCase()));
        if (gi >= 0) openCircuit(gi);
    }

    async function loadSites() {
        debugger;
        try {
            const d = await (await fetch(EP.sites)).json();
            S.sites = d.sites || [];
            $('#sipSite').innerHTML = S.sites.map(s => `<option value="${s.id}">${s.name}</option>`).join('');
            if (S.sites.length) {
                // reopen on the LAST-used site (so a saved layout is what you see again,
                // instead of silently snapping back to the first site)
                const last = localStorage.getItem('sipSite');
                $('#sipSite').value = (last && S.sites.some(s => String(s.id) === last)) ? last : String(S.sites[0].id);
                await onSiteChange();
            }
        } catch (e) { $('#sipTsList').innerHTML = `<div class="sip-hint">Sites load error: ${e}</div>`; }
    }

    async function onSiteChange() {
        S.site = +$('#sipSite').value;
        try { localStorage.setItem('sipSite', String(S.site)); } catch (e) { /* private mode */ }
        pause();
        await Promise.all([loadLayout(), loadElds()]);
        drawMap();
        clearWindow();
    }

    async function loadLayout() {
        const d = await (await fetch(`${EP.layout}?site=${encodeURIComponent(S.site)}`)).json();
        S.layout = d;
        S.savedPositions = positionSnapshot(d.elements);
        const vb = d.viewBox || [0, 0, 1360, 380];
        $('#sipSvg').setAttribute('viewBox', vb.join(' '));
        S.fullVB = vb.slice();
    }

    async function loadElds() {
        const d = await (await fetch(`${EP.list}?site_id=${encodeURIComponent(S.site)}`)).json();
        S.elds = (d.elds || []).slice().sort((a, b) => (a.eld_name || '').localeCompare(b.eld_name || ''));
        const cur = $('#sipEld').value;
        $('#sipEld').innerHTML = '<option value="">— select ELD —</option>' +
            S.elds.map(e => `<option value="${e.eld_id}">${e.eld_name}</option>`).join('');
        if (cur && S.elds.some(e => String(e.eld_id) === cur)) $('#sipEld').value = cur;
    }

    async function onEldChange() {
        S.eldId = +$('#sipEld').value || null;
        S.eldName = (S.elds.find(e => e.eld_id === S.eldId) || {}).eld_name || '';
        clearWindow();
        if (!S.eldId) { $('#sipTsList').innerHTML = '<div class="sip-hint">Pick an ELD to see its triggers.</div>'; $('#sipTsCount').textContent = ''; return; }
        await loadTransitions();
    }

    async function loadTransitions() {
        const mine = ++S.seq;
        const from = $('#sipFrom').value, to = $('#sipTo').value;
        const p = { eld_id: S.eldId };
        if (S.site) p.site_id = S.site;
        if (from) p.start_date = new Date(from).toISOString();
        if (to) p.end_date = new Date(to).toISOString();
        const url = `${EP.logs}?${qp(p)}`;
        $('#sipTsList').innerHTML = '<div class="sip-hint">Loading triggers…</div>';
        try {
            const d = await (await fetch(url)).json();
            if (mine !== S.seq) return;   // superseded by a newer selection
            S.transitions = (d.logs || []).slice().sort((a, b) => (b.timestamp || '').localeCompare(a.timestamp || ''));  // recent first (list)
            S.eventsChrono = S.transitions.slice().reverse();   // oldest → recent (the continuous timeline)
            S.evWins = {}; S.evi = 0;
            renderTimestamps();
            renderTimeline();
        } catch (e) { $('#sipTsList').innerHTML = `<div class="sip-hint">Triggers error: ${e}</div>`; }
    }

    function renderTimestamps() {
        const list = $('#sipTsList');
        $('#sipTsCount').textContent = S.transitions.length ? `(${S.transitions.length})` : '';
        if (!S.transitions.length) { list.innerHTML = '<div class="sip-hint">No triggers in this range.</div>'; return; }
        const n = S.eventsChrono.length;
        list.innerHTML = S.transitions.map((t, j) =>
            `<div class="sip-ts" data-ci="${n - 1 - j}">
         <span>${fmtTs(t.timestamp)}</span>
         <span class="tstate ${t.state}">${t.state}</span>
       </div>`).join('');   // data-ci = index into eventsChrono (oldest->recent)
        list.querySelectorAll('.sip-ts').forEach(el => el.onclick = () => gotoEvent(+el.dataset.ci, false));
    }

    // ── continuous replay timeline: every event in series, oldest → recent ───
    function renderTimeline() {
        const tl = $('#sipTimeline'); if (!tl) return;
        const evs = S.eventsChrono || [];
        tl.innerHTML = evs.map((ev, i) =>
            `<div class="sip-ev" data-i="${i}" title="${fmtTs(ev.timestamp)} · ${ev.state}">
         <div class="sip-ev-fill"></div><span class="sip-ev-eld"></span></div>`).join('');
        tl.querySelectorAll('.sip-ev').forEach(el => el.onclick = () => gotoEvent(+el.dataset.i, false));
        paintTimeline();
    }
    function paintTimeline() {   // played=done, current=partial fill, future=empty
        const tl = $('#sipTimeline'); if (!tl) return;
        tl.querySelectorAll('.sip-ev').forEach((el, i) => {
            el.classList.toggle('active', i === S.evi);
            el.classList.toggle('done', i < S.evi);
            const f = el.querySelector('.sip-ev-fill');
            if (f) f.style.width = (i < S.evi ? 100 : i === S.evi ? S.t * 100 : 0) + '%';
        });
    }

    function clearWindow() {
        S.seq++;   // cancel any in-flight window/transition load
        closeCircuit();
        S.win = null; S.bind = {}; S.pivotIso = null; pause();
        S.eventsChrono = []; S.evWins = {}; S.evi = 0; S.t = 0;
        const tl = $('#sipTimeline'); if (tl) tl.innerHTML = '';
        $('#sipGearList').innerHTML = '<div class="sip-hint">Pick a trigger (or press ▶) to run the −180s…+5s window.</div>';
        $('#sipGearHdr').textContent = '';
        $('#sipTLabel').textContent = '—';
        if (S.layout) applyStates(1);  // neutral
    }

    // ── event windows: lazy-load + cache + prefetch-next (playback never lags) ──
    async function loadEventWindow(i) {
        const ev = (S.eventsChrono || [])[i]; if (!ev) return null;
        if (S.evWins[i]) return S.evWins[i];   // cached
        const at = isoNoTz(ev.timestamp);
        const url = `${EP.change}?${qp({ eld_id: S.eldId, at, before_min: PRE_SEC / 60, after_min: POST_SEC / 60, full: true })}`;
        const w = await (await fetch(url)).json();
        S.evWins[i] = w;
        return w;
    }
    function prefetch(i) { if (S.eventsChrono[i] && !S.evWins[i]) loadEventWindow(i).catch(() => { }); }

    function applyWindow(w, ev) {
        S.win = w;
        S.pivotMs = Date.parse(ev.timestamp);        // set BEFORE renderGears (per-sample offsets)
        S.startMs = S.pivotMs - PRE_SEC * 1000;
        S.endMs = S.pivotMs + POST_SEC * 1000;
        buildBindings(w);
        renderGears(w);
        drawMap();
        autoZoom();
    }

    // go to event i (chronological); optionally start playing it
    async function gotoEvent(i, autoplay) {
        if (i < 0 || i >= (S.eventsChrono || []).length) return;
        pause();
        S.evi = i; S.t = 0;
        const ev = S.eventsChrono[i];
        S.pivotIso = ev.timestamp;
        document.querySelectorAll('#sipTsList .sip-ts').forEach(el => el.classList.toggle('active', +el.dataset.ci === i));
        paintTimeline();
        $('#sipGearList').innerHTML = '<div class="sip-hint">Loading window…</div>';
        const mine = ++S.seq;
        try {
            const w = await loadEventWindow(i);
            if (mine !== S.seq) return;   // superseded by a newer selection
            applyWindow(w, ev);
            setT(0);
            prefetch(i + 1);              // preload the NEXT event so auto-advance is instant
            if (autoplay) play();
        } catch (e) { $('#sipGearList').innerHTML = `<div class="sip-hint">Window error: ${e}</div>`; }
    }

    function buildBindings(w) {
        const bind = {};
        (w.gears || []).forEach(g => {
            const attrs = {};
            (g.attributes || []).forEach(a => {
                const arr = (a.series || []).map(p => [Date.parse(p.ts), num(p.value)])
                    .filter(p => p[0] && p[1] != null).sort((x, y) => x[0] - y[0]);
                if (arr.length) attrs[a.name] = { arr, changed: !!a.changed, threshold: a.threshold };
            });
            bind[norm(g.asset_name)] = { gear: g, attrs, changed: !!g.changed, type: g.asset_type };
        });
        S.bind = bind;
    }

    // value of any attr whose name matches `re`, resolved at time t (ms); max abs across matches
    function valAt(b, re, tms) {
        if (!b) return null;
        let best = null;
        for (const name in b.attrs) {
            if (!re.test(name)) continue;
            const v = resolve(b.attrs[name].arr, tms);
            if (v == null) continue;
            if (best == null || Math.abs(v) > Math.abs(best)) best = v;
        }
        return best;
    }
    function resolve(arr, tms) {
        if (!arr || !arr.length) return null;
        if (tms <= arr[0][0]) return arr[0][1];
        let lo = 0, hi = arr.length - 1, ans = arr[0][1];
        while (lo <= hi) { const mid = (lo + hi) >> 1; if (arr[mid][0] <= tms) { ans = arr[mid][1]; lo = mid + 1; } else hi = mid - 1; }
        return ans;
    }

    // semantic state of a layout element at scrub-time tms
    function elemState(el, tms) {
        const b = S.bind[norm(el.asset)];
        if (el.kind === 'track') {
            if (!b) return { cls: 'st-nodata' };
            const tpr = valAt(b, /TPR/i, tms);
            if (tpr != null) return { cls: tpr < 0.5 ? 'st-occupied' : 'st-clear', flag: b.changed };
            const chg = valAt(b, /CHARGER|V\b/i, tms);
            if (chg != null) return { cls: Math.abs(chg) < 70 ? 'st-down' : 'st-clear', flag: b.changed };
            return { cls: 'st-nodata', flag: b.changed };
        }
        if (el.kind === 'point') {
            if (!b) return { cls: 'pt-mid' };
            const nw = valAt(b, /NWKR/i, tms), rw = valAt(b, /RWKR/i, tms);
            const nOn = nw != null && Math.abs(nw) >= 5, rOn = rw != null && Math.abs(rw) >= 5;
            let cls = 'pt-mid';
            if (nOn && !rOn) cls = 'pt-normal'; else if (rOn && !nOn) cls = 'pt-reverse';
            return { cls, flag: b.changed };
        }
        if (el.kind === 'signal') {
            if (!b) return { cls: 'asp-off' };
            // The datalogger CR relay (0/1) is AUTHORITATIVE for the lamp: it reflects
            // an aspect change/dip (e.g. RECR 1->0->1) that the analog aspect voltage
            // (RG V, which can sit at ~110V throughout) does not. Fall back to the
            // aspect voltage only when the relay attribute is absent for that aspect.
            const aspOn = (relayRe, voltRe) => {
                const rv = valAt(b, relayRe, tms);
                if (rv != null) return Math.abs(rv) >= 0.5;
                const vv = voltRe ? valAt(b, voltRe, tms) : null;
                return vv != null && Math.abs(vv) >= 70;
            };
            const green = aspOn(/^DECR/i, /^DG V/i);
            const dy = aspOn(/^HHECR/i, /^HHG V/i);
            const yel = aspOn(/^HECR/i, /^HG V/i);         // ^HECR excludes HHECR / Co-HECR
            const call = aspOn(/^(UECR|Co-HECR|OFFECR)/i, null);  // proceed / calling-on / shunt-OFF
            const red = aspOn(/^(RECR|ONECR)/i, /^RG V/i);        // stop (ONECR = shunt-ON)
            let cls = 'asp-off';
            if (green) cls = 'asp-green'; else if (dy) cls = 'asp-dyellow'; else if (yel) cls = 'asp-yellow';
            else if (call) cls = 'asp-callon'; else if (red) cls = 'asp-red';
            return { cls, flag: b.changed };
        }
        return { cls: '' };
    }

    // ── SVG rendering ───────────────────────────────────────────────────────
    function drawMap() {
        const svg = $('#sipSvg'); if (!svg || !S.layout) return;
        while (svg.firstChild) svg.removeChild(svg.firstChild);
        const vb0 = S.layout.viewBox || [0, 0, 1360, 380];
        // reference underlay as an SVG <image> filling the viewBox → it scales EXACTLY
        // with element coordinates, so dragging onto a feature is pixel-accurate.
        if (S.layout.reference_image) {
            const img = svgEl('image', { class: 'sip-ref-svg', x: vb0[0], y: vb0[1], width: vb0[2], height: vb0[3], preserveAspectRatio: 'none' });
            img.setAttribute('href', S.layout.reference_image);
            img.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', S.layout.reference_image);
            svg.appendChild(img);
        }
        // subtle grid (light theme)
        const g0 = svgEl('g', { opacity: '0.5' });
        const vb = S.layout.viewBox || [0, 0, 1360, 380];
        for (let x = 0; x <= vb[2]; x += 40) g0.appendChild(svgEl('line', { x1: x, y1: 0, x2: x, y2: vb[3], stroke: '#cfd8e4', 'stroke-width': .5 }));
        for (let y = 0; y <= vb[3]; y += 40) g0.appendChild(svgEl('line', { x1: 0, y1: y, x2: vb[2], y2: y, stroke: '#cfd8e4', 'stroke-width': .5 }));
        svg.appendChild(g0);

        S.nodes = {};
        (S.layout.elements || []).forEach(el => {
            const node = drawElement(el);
            if (node) { svg.appendChild(node); S.nodes[el.id] = { el, node }; }
        });
    }

    function drawElement(el) {
        const g = svgEl('g', { class: 'el-group', 'data-id': el.id });
        if (el.kind === 'track') {
            const w = el.w || 70, x1 = el.x - w / 2, x2 = el.x + w / 2;
            g.appendChild(svgEl('line', { class: 'el-track', x1, y1: el.y, x2, y2: el.y }));
            // label pill
            const lg = svgEl('g', { class: 'el-tracklabel' });
            const tw = Math.max(26, (el.asset.length * 6) + 10);
            lg.appendChild(svgEl('rect', { x: el.x - tw / 2, y: el.y - 8, width: tw, height: 15, rx: 7 }));
            const tx = svgEl('text', { x: el.x, y: el.y }); tx.textContent = el.asset; lg.appendChild(tx);
            g.appendChild(lg);
        } else if (el.kind === 'point') {
            const s = el.span || 34;
            g.setAttribute('transform', `translate(${el.x},${el.y})`);
            g.appendChild(svgEl('line', { class: 'pt-leg', x1: -s / 2, y1: 0, x2: s / 2, y2: 0 }));           // through route
            g.appendChild(svgEl('line', { class: 'pt-blade', x1: -s / 2, y1: 0, x2: s / 2, y2: 0 }));          // blade (rotates)
            g.appendChild(svgEl('circle', { class: 'pt-body', cx: 0, cy: 0, r: 6 }));
            const tx = svgEl('text', { x: 0, y: -12 }); tx.textContent = el.asset.replace('PT-', ''); g.appendChild(tx);
        } else if (el.kind === 'signal') {
            g.setAttribute('transform', `translate(${el.x},${el.y})`);
            const dir = el.face === 'L' ? -1 : 1, r = el.stype === 'shunt' ? 4.5 : 6;
            g.appendChild(svgEl('line', { class: 'sig-post', x1: 0, y1: 0, x2: dir * 9, y2: 0 }));
            // second lamp — only lit for DOUBLE-YELLOW, so it reads as two yellows (≠ single yellow)
            g.appendChild(svgEl('circle', { class: 'sig-lamp2', cx: dir * (9 + r + r * 2.15), cy: 0, r: r * 0.9 }));
            g.appendChild(svgEl('circle', { class: 'sig-lamp', cx: dir * (9 + r), cy: 0, r }));
            const tx = svgEl('text', { x: dir * (9 + r), y: -11 }); tx.textContent = el.asset; g.appendChild(tx);
        } else { // marker
            g.setAttribute('transform', `translate(${el.x},${el.y})`);
            g.appendChild(svgEl('circle', { cx: 0, cy: 0, r: 7 }));
            const tx = svgEl('text', { x: 0, y: 0 }); tx.textContent = el.asset; g.appendChild(tx);
            g.setAttribute('class', 'el-group el-marker');
        }
        // selection box + flag ring (bbox filled after append; use a generous ring)
        g.appendChild(svgEl('circle', { class: 'el-flag-ring', cx: el.kind === 'track' ? el.x : 0, cy: el.kind === 'track' ? el.y : 0, r: el.kind === 'track' ? (el.w || 70) / 2 + 8 : 15 }));
        g.appendChild(svgEl('rect', { class: 'el-sel-box', x: (el.kind === 'track' ? el.x - (el.w || 70) / 2 : -18) - 2, y: (el.kind === 'track' ? el.y : 0) - 16, width: (el.kind === 'track' ? (el.w || 70) : 36) + 4, height: 32 }));
        const base = el.kind === 'track' ? 'el-group el-track-g' : el.kind === 'point' ? 'el-group el-point' : el.kind === 'signal' ? 'el-group el-signal' : 'el-group el-marker';
        g.setAttribute('class', base);
        return g;
    }

    // ── apply states at scrub time ───────────────────────────────────────────
    function applyStates(frac) {
        if (!S.nodes) return;
        const tms = (S.startMs != null) ? (S.startMs + frac * WIN_MS) : null;
        for (const id in S.nodes) {
            const { el, node } = S.nodes[id];
            const st = (tms != null) ? elemState(el, tms) : { cls: el.kind === 'signal' ? 'asp-off' : el.kind === 'point' ? 'pt-mid' : 'st-nodata' };
            // reset state classes
            node.classList.remove('st-clear', 'st-occupied', 'st-down', 'st-nodata',
                'pt-normal', 'pt-reverse', 'pt-mid', 'asp-red', 'asp-yellow', 'asp-dyellow', 'asp-green', 'asp-callon', 'asp-off', 'flagged');
            if (st.cls) node.classList.add(st.cls);
            // propagate track state to its label pill for the coloured pill look
            if (el.kind === 'track') {
                const lbl = node.querySelector('.el-tracklabel');
                if (lbl) { lbl.classList.remove('st-occupied', 'st-nodata'); if (st.cls === 'st-occupied') lbl.classList.add('st-occupied'); else if (st.cls === 'st-nodata') lbl.classList.add('st-nodata'); }
                const ln = node.querySelector('.el-track'); if (ln) { ln.classList.remove('st-clear', 'st-occupied', 'st-down', 'st-nodata'); ln.classList.add(st.cls); }
            }
            // point blade rotation
            if (el.kind === 'point') {
                const blade = node.querySelector('.pt-blade');
                if (blade) blade.style.transform = st.cls === 'pt-reverse' ? 'rotate(-22deg)' : st.cls === 'pt-normal' ? 'rotate(0deg)' : 'rotate(-11deg)';
            }
            if (st.flag && !S.editing) node.classList.add('flagged');
        }
        // time label
        if (tms != null) {
            const off = Math.round((tms - S.pivotMs) / 1000);
            $('#sipTLabel').innerHTML = `<b>${new Date(tms).toLocaleTimeString()}</b> · <span class="off">${off >= 0 ? '+' : ''}${off}s</span>`;
        }
    }

    // ── transport ────────────────────────────────────────────────────────────
    function setT(frac) { S.t = clamp(frac, 0, 1); paintTimeline(); applyStates(S.t); }
    function togglePlay() { S.playing ? pause() : play(); }
    function play() {
        // press ▶ with nothing loaded → start the series from the current (or first) event
        if (!S.win) { if ((S.eventsChrono || []).length) gotoEvent(S.evi || 0, true); return; }
        S.playing = true; $('#sipPlay').textContent = '⏸⏸';
        if (S.t >= 1) S.t = 0;
        S.lastFrame = performance.now();
        cancelAnimationFrame(S.raf); S.raf = requestAnimationFrame(step);
    }
    function pause() { S.playing = false; if ($('#sipPlay')) $('#sipPlay').textContent = '▶'; cancelAnimationFrame(S.raf); }
    function step(now) {
        if (!S.playing) return;
        const dt = (now - S.lastFrame) / 1000; S.lastFrame = now;
        S.t += (dt / S.playDur) * S.speed;
        if (S.t >= 1) {
            setT(1);
            if (S.evi < (S.eventsChrono.length - 1)) { pause(); gotoEvent(S.evi + 1, true); return; }  // auto-advance to next event (prefetched → instant)
            pause(); return;   // end of the last event
        }
        setT(S.t);
        S.raf = requestAnimationFrame(step);
    }

    // ── zoom ───────────────────────────────────────────────────────────────
    function fitAll() { const vb = S.fullVB || [0, 0, 1360, 380]; animateVB(vb); }
    function autoZoom() {
        // bbox of elements that have data (changed ones weighted), else fit all
        const pts = [];
        (S.layout.elements || []).forEach(el => {
            const b = S.bind[norm(el.asset)]; if (!b) return;
            const w = el.kind === 'track' ? (el.w || 70) / 2 : 20;
            pts.push([el.x - w, el.y - 20], [el.x + w, el.y + 20]);
        });
        if (pts.length < 2) { fitAll(); return; }
        let minx = 1e9, miny = 1e9, maxx = -1e9, maxy = -1e9;
        pts.forEach(p => { minx = Math.min(minx, p[0]); miny = Math.min(miny, p[1]); maxx = Math.max(maxx, p[0]); maxy = Math.max(maxy, p[1]); });
        const pad = 40; minx -= pad; miny -= pad; maxx += pad; maxy += pad;
        // keep aspect ratio of the full viewBox
        const fvb = S.fullVB || [0, 0, 1360, 380], ar = fvb[2] / fvb[3];
        let w = maxx - minx, h = maxy - miny;
        if (w / h > ar) h = w / ar; else w = h * ar;
        const cx = (minx + maxx) / 2, cy = (miny + maxy) / 2;
        animateVB([cx - w / 2, cy - h / 2, w, h]);
    }
    function animateVB(target) {
        const svg = $('#sipSvg');
        const cur = (svg.getAttribute('viewBox') || (S.fullVB || [0, 0, 1360, 380]).join(' ')).split(/\s+/).map(Number);
        const t0 = performance.now(), dur = 450;
        (function anim(now) {
            const k = clamp((now - t0) / dur, 0, 1), e = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;
            const vb = cur.map((c, i) => c + (target[i] - c) * e);
            svg.setAttribute('viewBox', vb.join(' '));
            if (k < 1) requestAnimationFrame(anim);
        })(t0);
    }

    // ── left gear/attribute list ─────────────────────────────────────────────
    function renderGears(w) {
        const box = $('#sipGearList');
        const gears = (w.gears || []).slice();
        // power supply as a pseudo-gear at the end
        if (w.power_supply && (w.power_supply.attributes || []).length)
            gears.push({ asset_name: w.power_supply.name, asset_type: 'POWER SUPPLY (IPS)', attributes: w.power_supply.attributes, changed: (w.power_supply.attributes || []).some(a => a.changed) });
        S.gearsRendered = gears;   // openCircuit(gi) looks up the track's asset here
        $('#sipGearHdr').textContent = gears.length ? `(${gears.length})` : '';
        if (!gears.length) { box.innerHTML = '<div class="sip-hint">No mapped assets for this ELD.</div>'; return; }
        box.innerHTML = gears.map((g, gi) => {
            const attrs = (g.attributes || []);
            const rows = attrs.map(a => {
                const ev = a.end_value;
                const val = (ev == null ? '—' : round(ev));
                const thr = a.threshold;
                const series = (a.series || []).filter(p => p.ts != null && p.value != null);
                // full −180s…+5s timestamped values; highlight samples that cross the threshold
                let prevUp = null;
                const spts = series.map(p => {
                    const v = num(p.value);
                    const up = (thr != null && v != null) ? Math.abs(v) >= thr : null;
                    const cross = (prevUp != null && up != null && up !== prevUp);
                    prevUp = up;
                    return `<div class="sip-sp ${cross ? 'cross' : ''}">
              <span class="spt">${fmtClock(p.ts)}</span>
              <span class="spo">${offSec(p.ts)}</span>
              <span class="spv">${round(v)}</span></div>`;
                }).join('');
                const seriesHtml = series.length
                    ? `<div class="sip-series">${spts}</div>`
                    : '<div class="sip-series off">no samples in window</div>';
                return `<div class="sip-attr-block ${a.changed ? 'changed' : ''}">
            <div class="sip-attr ${a.changed ? 'changed' : ''}">
              <span class="an">${a.name}${a.changed ? '<span class="flag">⚑</span>' : ''}</span>
              <span class="av">${val}<span class="off"> now · ${series.length} pts</span></span>
            </div>${seriesHtml}</div>`;
            }).join('') || '<div class="sip-attr"><span class="an off">no relevant attributes in window</span></div>';
            const atype = (g.asset_type || '').toUpperCase();
            const hasCircuit = g.asset_id != null && (atype === 'TRACK' || atype === 'SIGNAL' || atype === 'POINT MACHINE');
            const kind = atype === 'TRACK' ? 'track' : atype === 'POINT MACHINE' ? 'point'
                : (/^SH/i.test(g.asset_name || '') ? 'shunt' : 'signal');
            return `<div class="sip-gear ${g.changed ? 'flagged' : ''}${hasCircuit ? ' is-track' : ''}" data-gi="${gi}">
        <div class="sip-gear-head"><span class="gdot"></span><span class="gname">${g.asset_name}</span>
          <span class="sip-spacer"></span>
          ${hasCircuit ? `<button class="cir-open" data-gi="${gi}" title="Open the ${kind} circuit schematic for ${g.asset_name}">⤢ circuit</button>` : ''}
          <span class="gtype">${g.asset_type}</span></div>
        <div class="sip-gear-body">${rows}</div></div>`;
        }).join('');
        box.querySelectorAll('.sip-gear-head').forEach(h => h.onclick = (e) => {
            if (e.target.closest('.cir-open')) return;   // circuit button has its own handler
            h.parentElement.classList.toggle('open');
        });
        box.querySelectorAll('.cir-open').forEach(b => b.onclick = (e) => { e.stopPropagation(); openCircuit(+b.dataset.gi); });
    }
    const round = v => (v == null ? '—' : Math.round(v * 100) / 100);

    // ── reference underlay ───────────────────────────────────────────────────
    function toggleRef() {
        S.showRef = !S.showRef;
        $('#sipStage').classList.toggle('show-ref', S.showRef);
        $('#sipRefBtn').classList.toggle('active', S.showRef);
    }

    // ── editor ───────────────────────────────────────────────────────────────
    function toggleEdit() {
        S.editing = !S.editing;
        pause();
        $('#sipStage').classList.toggle('editing', S.editing);
        $('#sipEditBar').classList.toggle('on', S.editing);
        $('#sipEditBtn').classList.toggle('active', S.editing);
        $('#sipEditBtn').textContent = S.editing ? '✓ Done editing' : '✎ Edit layout';
        $('#sipEditSite').textContent = (S.layout && S.layout.site_name) || '';
        if (S.editing) { selectEl(null); applyStates(1); if (!S.showRef) toggleRef(); }
        else { if (S.showRef) toggleRef(); applyStates(S.t); }
    }

    function svgPoint(evt) {
        const svg = $('#sipSvg'), pt = svg.createSVGPoint();
        pt.x = evt.clientX; pt.y = evt.clientY;
        return pt.matrixTransform(svg.getScreenCTM().inverse());
    }
    function onPointerDown(e) {
        if (!S.editing) return;
        const g = e.target.closest('.el-group'); if (!g) { selectEl(null); return; }
        const id = g.getAttribute('data-id'); const rec = S.nodes[id]; if (!rec) return;
        selectEl(id);
        const p = svgPoint(e);
        S.drag = { id, dx: p.x - rec.el.x, dy: p.y - rec.el.y };
        g.setPointerCapture && g.setPointerCapture(e.pointerId);
    }
    function onPointerMove(e) {
        if (!S.editing || !S.drag) return;
        const rec = S.nodes[S.drag.id]; if (!rec) return;
        const p = svgPoint(e);
        rec.el.x = Math.round(p.x - S.drag.dx); rec.el.y = Math.round(p.y - S.drag.dy);
        positionNode(rec);
        reflectSel(rec.el);
        markDirty();
    }
    function onPointerUp(e) { S.drag = null; }

    function positionNode(rec) {
        const el = rec.el, node = rec.node;
        if (el.kind === 'track') {
            const w = el.w || 70;
            node.querySelector('.el-track').setAttribute('x1', el.x - w / 2);
            node.querySelector('.el-track').setAttribute('x2', el.x + w / 2);
            node.querySelector('.el-track').setAttribute('y1', el.y);
            node.querySelector('.el-track').setAttribute('y2', el.y);
            const lbl = node.querySelector('.el-tracklabel');
            const tw = Math.max(26, (el.asset.length * 6) + 10);
            lbl.querySelector('rect').setAttribute('x', el.x - tw / 2);
            lbl.querySelector('rect').setAttribute('y', el.y - 8);
            const tx = lbl.querySelector('text'); tx.setAttribute('x', el.x); tx.setAttribute('y', el.y);
            node.querySelector('.el-flag-ring').setAttribute('cx', el.x);
            node.querySelector('.el-flag-ring').setAttribute('cy', el.y);
            const sb = node.querySelector('.el-sel-box'); sb.setAttribute('x', el.x - w / 2 - 2); sb.setAttribute('y', el.y - 16);
        } else {
            node.setAttribute('transform', `translate(${el.x},${el.y})`);
        }
    }

    function selectEl(id) {
        S.sel = id;
        for (const k in S.nodes) S.nodes[k].node.classList.toggle('sel-el', k === id);
        const rec = id && S.nodes[id];
        $('#sipSelInfo').textContent = rec ? `${rec.el.asset} · ${rec.el.kind}` : 'No element selected.';
        reflectSel(rec ? rec.el : null);
    }
    function reflectSel(el) { $('#sipSelX').value = el ? el.x : ''; $('#sipSelY').value = el ? el.y : ''; }
    function positionSnapshot(elements) {
        return Object.fromEntries((elements || []).map(el => [el.id, { x: el.x, y: el.y }]));
    }
    function renderChangedList() {
        const changed = (S.layout?.elements || []).filter(el => {
            const saved = S.savedPositions[el.id];
            return saved && (saved.x !== el.x || saved.y !== el.y);
        });
        const list = $('#sipChangedList');
        list.textContent = changed.length
            ? `Changed: ${changed.map(el => `${el.asset} (x:${el.x}, y:${el.y})`).join(' · ')}`
            : 'No position changes.';
        list.classList.toggle('has-changes', changed.length > 0);
        list.title = changed.length ? 'Current unsaved positions' : '';
    }
    function onSelCoordEdit() {
        if (!S.sel) return; const rec = S.nodes[S.sel]; if (!rec) return;
        const x = +$('#sipSelX').value, y = +$('#sipSelY').value;
        if (!isNaN(x)) rec.el.x = x; if (!isNaN(y)) rec.el.y = y;
        positionNode(rec); markDirty();
    }
    function markDirty() {
        S.dirty = true;
        $('#sipDirty').innerHTML = '<span class="dirty">● unsaved</span>';
        renderChangedList();
    }

    async function saveLayout() {
        if (!S.layout) return;
        $('#sipSaveBtn').disabled = true; $('#sipSaveBtn').textContent = 'Saving…';
        try {
            const elements = (S.layout.elements || []).map(el => S.nodes?.[el.id]?.el || el);
            const body = { ...S.layout, elements };
            const r = await fetch(`${EP.saveLayout}?site=${encodeURIComponent(S.site)}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
            const d = await r.json();
            if (!r.ok) throw new Error(d.detail || 'save failed');
            const savedById = Object.fromEntries((d.elements || []).map(el => [el.id, el]));
            Object.values(S.nodes || {}).forEach(rec => {
                if (savedById[rec.el.id]) rec.el = savedById[rec.el.id];
            });
            S.layout = d; S.savedPositions = positionSnapshot(d.elements); S.dirty = false;
            renderChangedList();
            $('#sipDirty').innerHTML = '<span style="color:#5bd99a;font-weight:700">✓ saved</span>';
            setTimeout(() => { if (!S.dirty) $('#sipDirty').innerHTML = ''; }, 2500);
        } catch (e) { $('#sipDirty').innerHTML = `<span style="color:#ff8b8b">save error: ${e.message || e}</span>`; }
        finally { $('#sipSaveBtn').disabled = false; $('#sipSaveBtn').textContent = '💾 Save layout'; }
    }

    // ── time helpers ─────────────────────────────────────────────────────────
    function fmtTs(s) { if (!s) return '—'; try { return new Date(s).toLocaleString([], { month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }); } catch { return s; } }
    function fmtClock(s) { if (!s) return '—'; try { return new Date(s).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }); } catch { return s; } }
    function offSec(s) { if (S.pivotMs == null || !s) return ''; const o = Math.round((Date.parse(s) - S.pivotMs) / 1000); return (o >= 0 ? '+' : '') + o + 's'; }
    function isoNoTz(s) { // /api/eld/change wants a naive ISO (device local); strip zone
        if (!s) return s;
        const m = s.match(/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})/);
        return m ? m[1] : s;
    }

    // ── ASSET CIRCUIT schematics ───────────────────────────────────────────────
    //  Fixed per-asset-type templates (TRACK, SIGNAL). Each `box` auto-binds to the
    //  asset attribute whose name matches one of its `attr` candidates; the inline
    //  value is the sample CLOSEST to the ELD trigger (0s), and hovering lists the
    //  whole series. Positions are data-driven in a template's own `vb` space —
    //  tweak x/y here to move a box, nothing else.
    const CIRCUITS = {
        TRACK: {
            vb: [0, 165, 1160, 775],   // crop the empty top (content starts ~y180)
            zones: [
                { x: 20, y: 320, w: 425, h: 610, label: 'LOCATION BOX   AC-9', lx: 40, ly: 905 },
                { x: 465, y: 320, w: 400, h: 610, label: 'LOCATION BOX:  AC-11', lx: 480, ly: 905 },
                { x: 940, y: 320, w: 210, h: 610, label: 'RELAY ROOM', lx: 995, ly: 348 },
            ],
            rails: [], wires: [], statics: [],
            // all rails / ties / RE-bonds / TLJB fuses / choke / battery / charger /
            // resistors / U-G connectors / colored 24V·110V arrows drawn here (matches
            // the real track-circuit diagram); value boxes below sit on top.
            specials: [{ type: 'trackDecor' }],
            boxes: [
                // AC-9 feed side
                { x: 105, y: 410, label: 'RTC VAR RES (ohm)', attr: ['RTC VAR RES'] },
                { x: 105, y: 475, label: 'VTC VAR RES (V)', attr: ['VTC VAR RES'] },
                { x: 368, y: 430, label: 'VTC CH FEED END (V)', attr: ['Choke V', 'VTC CH FEED END'] },
                { x: 360, y: 486, label: 'ITC FEED END (mA)', attr: ['If mA', 'ITC FEED END'] },
                { x: 356, y: 546, label: 'VTC FEED END (V)', attr: ['Vf', 'VTC FEED END'] },
                { x: 105, y: 606, label: 'ITC TFC O/P (mA)', attr: ['Charger mA', 'ITC TFC O/P'] },
                { x: 368, y: 636, label: 'VTC TFC O/P (V)', attr: ['Charger OP V', 'VTC TFC O/P'] },
                { x: 105, y: 682, label: 'VTC TFC I/P (V)', attr: ['Charger V', 'VTC TFC I/P'] },
                // on the rails
                { x: 485, y: 256, label: 'IBLAST', attr: ['IBLAST', 'I Blast'], wide: true },
                // AC-11 relay side
                { x: 565, y: 382, label: 'VTC RELAY END (V)', attr: ['Vr', 'VTC RELAY END'] },
                { x: 570, y: 434, label: 'ITC RELAY END (mA)', attr: ['Ir mA', 'ITC RELAY END'] },
                { x: 806, y: 438, label: 'TR V (Relay)', attr: ['TR V (Relay)', 'TR V'] },
                { x: 675, y: 858, label: 'VTC 24 DC LOC (V)', attr: ['TPR V (Loc)', 'VTC 24 DC LOC'] },        // analog
                { x: 1045, y: 858, label: 'VTC 24 DC TPR I/P (V)', attr: ['TPR V', 'TPR', 'VTC 24 DC TPR I/P'], wide: true },  // analog TPR (verified ~25.7)
                // relay room — DIGITAL datalogger TPR (0/1) → UP/DOWN, NOT the analog TPR
                { x: 1045, y: 388, label: 'TPR', attr: ['TPR'], status: true },
            ],
        },

        // ── SIGNAL: RELAY ROOM (CR relays) → LOCATION BOX AC-10 (proving relays) →
        //    SIGNAL UNIT → aspect head → per-aspect ISIG (mA) / VSIG (V) readings.
        //    NOTE: main signals only — shunt signals get their own template later.
        SIGNAL: {
            vb: [1520, 640],
            zones: [
                { x: 225, y: 55, w: 605, h: 525, label: 'RELAY ROOM', lx: 470, ly: 80 },
                { x: 855, y: 55, w: 140, h: 525, label: 'LOCATION BOX  AC-10', lx: 862, ly: 45 },
            ],
            rails: [], wires: [], statics: [],
            // 4 aspect circuits (red Bx110V feed + black Nx110V return, resistor squiggle,
            // return arrow, AC-10 relay coils, SIGNAL UNIT line) drawn in drawSignalDecor
            specials: [
                { type: 'signalDecor' },
                {
                    type: 'signalHead', cx: 1150, top: 70, aspects: [
                        { key: 'HHG', cy: 128, on: '#f5b301', re: '^HHG V' },
                        { key: 'DG', cy: 253, on: '#1fb866', re: '^DG V' },
                        { key: 'HG', cy: 378, on: '#f5b301', re: '^HG V' },
                        { key: 'RG', cy: 508, on: '#ef3e3e', re: '^RG V' },
                    ]
                },
            ],
            boxes: [
                // relay states — each UNIQUE relay shown ONCE (0/1 → UP/DOWN); the ladder
                // flowchart (repeated contacts + brackets) is drawn in drawSignalDecor.
                { x: 345, y: 100, label: 'HR', attr: ['HR'], status: true },
                { x: 452, y: 100, label: 'DR', attr: ['DR'], status: true },
                { x: 556, y: 100, label: 'HHR', attr: ['HHR'], status: true },
                { x: 452, y: 350, label: 'UECR', attr: ['UECR'], status: true },
                { x: 600, y: 460, label: 'Co-HECR', attr: ['Co-HECR'], status: true },
                { x: 780, y: 100, label: 'HHECR', attr: ['HHECR'], status: true },
                { x: 780, y: 225, label: 'DECR', attr: ['DECR'], status: true },
                { x: 780, y: 350, label: 'HECR', attr: ['HECR'], status: true },
                { x: 780, y: 536, label: 'RECR', attr: ['RECR'], status: true },
                // per-aspect ISIG (mA) / VSIG (V) — right of the signal head
                { x: 1360, y: 108, label: 'ISIG HHG', attr: ['HHG mA', 'ISIG HHG'] },
                { x: 1360, y: 150, label: 'VSIG HHG', attr: ['HHG V', 'VSIG HHG'] },
                { x: 1360, y: 233, label: 'ISIG DG', attr: ['DG mA', 'ISIG DG'] },
                { x: 1360, y: 275, label: 'VSIG DG', attr: ['DG V', 'VSIG DG'] },
                { x: 1360, y: 358, label: 'ISIG HG', attr: ['HG mA', 'ISIG HG'] },
                { x: 1360, y: 400, label: 'VSIG HG', attr: ['HG V', 'VSIG HG'] },
                { x: 1360, y: 488, label: 'ISIG RG', attr: ['RG mA', 'ISIG RG'] },
                { x: 1360, y: 530, label: 'VSIG RG', attr: ['RG V', 'VSIG RG'] },
            ],
        },

        // ── SHUNT SIGNAL: OFF / ON / PILOT aspects → LED lit sig + current-regulating
        //    unit; each aspect has ISHSIG (mA) / VSHSIG (V). Plus VSHSIG HPR at bottom.
        SHUNT: {
            vb: [1320, 780],
            zones: [], rails: [], wires: [], statics: [],
            // all-black ladder: 6 aspect lines, OFFECR/ONECR relays, HR contacts, CRU
            // contact glyphs, 3 LED lamps (lit by VSHSIG≥70), bottom HPR — drawShuntDecor
            specials: [{ type: 'shuntDecor' }],
            boxes: [
                // relays (0/1) — each once
                { x: 236, y: 141, label: 'OFFECR', attr: ['OFFECR'], status: true },
                { x: 224, y: 321, label: 'ONECR', attr: ['ONECR'], status: true },
                { x: 372, y: 600, label: 'HR', attr: ['HR'], status: true },
                // per-aspect ISHSIG (mA) / VSHSIG (V) — right of each LED
                { x: 1200, y: 72, label: 'ISHSIG OFF', attr: ['Off Aspect mA', 'ISHSIG OFF'], wide: true },
                { x: 1200, y: 128, label: 'VSHSIG OFF', attr: ['Off Aspect V', 'VSHSIG OFF'], wide: true },
                { x: 1200, y: 252, label: 'ISHSIG ON', attr: ['On Aspect mA', 'ISHSIG ON'], wide: true },
                { x: 1200, y: 322, label: 'VSHSIG ON', attr: ['On Aspect V', 'VSHSIG ON'], wide: true },
                { x: 1200, y: 432, label: 'ISHSIG PILOT', attr: ['PILOT mA', 'ISHSIG PILOT'], wide: true },
                { x: 1200, y: 502, label: 'VSHSIG PILOT', attr: ['PILOT V', 'VSHSIG PILOT'], wide: true },
                { x: 905, y: 650, label: 'VSHSIG HPR', attr: ['Sh-HPR', 'VSHSIG HPR'], wide: true },
            ],
        },

        // ── POINT MACHINE: RELAY ROOM (NWKR/RWKR) → A-END / B-END location boxes +
        //    motors. KR + KR(LOC) voltages bind by name; plus a live OPERATIONS table
        //    (per-op A/B current·voltage·throw-time + averages) via pm_operation2.
        POINT: {
            vb: [1120, 720],
            opTable: true,
            zones: [
                { x: 20, y: 40, w: 275, h: 645, label: 'RELAY ROOM', lx: 108, ly: 30 },
                { x: 315, y: 270, w: 135, h: 400, label: 'A END  Location Box', lx: 326, ly: 300 },
                { x: 575, y: 30, w: 145, h: 480, label: 'B END  Location Box', lx: 586, ly: 55 },
            ],
            rails: [], wires: [], statics: [],
            // feeds (B-24V red / N-24V navy / B-110V orange / N-110V navy), location-box
            // fuse contacts, 'A'/'B' END motors with N/F·R/F coils — drawPointDecor
            specials: [{ type: 'pointDecor' }],
            boxes: [
                // KR sense voltages + DIGITAL status (real status name is "Combined-NWKR/RWKR")
                { x: 150, y: 360, label: '(A)NWKR', attr: ['A End - NWKR'] },
                { x: 160, y: 388, label: 'NWKR', attr: ['Combined-NWKR', 'NWKR'], status: true },
                { x: 150, y: 416, label: '(B)NWKR', attr: ['B End - NWKR'] },
                { x: 150, y: 468, label: '(A)RWKR', attr: ['A End - RWKR'] },
                { x: 160, y: 496, label: 'RWKR', attr: ['Combined-RWKR', 'RWKR'], status: true },
                { x: 150, y: 524, label: '(B)RWKR', attr: ['B End - RWKR'] },
                // combined CR relays (status)
                { x: 250, y: 590, label: 'NWCR', attr: ['Combined-NWCR'], status: true },
                { x: 250, y: 638, label: 'RWCR', attr: ['Combined-RWCR'], status: true },
                // location-box sense voltages
                { x: 398, y: 360, label: '(A)NWKR(LOC)', attr: ['A End - NWKR (Loc)'], wide: true },
                { x: 398, y: 468, label: '(A)RWKR(LOC)', attr: ['A End - RWKR (Loc)'], wide: true },
                { x: 655, y: 110, label: '(B)RWKR(LOC)', attr: ['B End - RWKR (Loc)'], wide: true },
                { x: 655, y: 160, label: '(B)NWKR(LOC)', attr: ['B End - NWKR (Loc)'], wide: true },
            ],
        },
    };
    const keyOf = s => (s || '').toString().toUpperCase().replace(/[^A-Z0-9]/g, '');

    let CIR = null;   // { win, index(bound attrs), pivotMs, boundNames:Set }

    function ensureCircuitDom() {
        if ($('#sipCircuit')) return;
        const wrap = document.querySelector('#sipView .sip-stage-wrap');
        const c = document.createElement('div');
        c.id = 'sipCircuit';
        c.className = 'sip-circuit';
        c.innerHTML = `
      <div class="cir-bar">
        <button class="sip-btn" id="cirBack">◀ Back to yard</button>
        <span class="cir-title" id="cirTitle">Track circuit</span>
        <span class="sip-spacer"></span>
        <span class="cir-when" id="cirWhen"></span>
      </div>
      <div class="cir-stage"><div class="cir-canvas">
        <svg id="cirSvg" preserveAspectRatio="xMidYMid meet"></svg>
        <div class="cir-chips" id="cirChips"></div></div></div>
      <div class="cir-ops" id="cirOps"></div>
      <div class="cir-extra" id="cirExtra"></div>
      <div class="cir-tip" id="cirTip"></div>`;
        wrap.appendChild(c);
        $('#cirBack').onclick = closeCircuit;
    }

    const isShuntName = g => /^SH/i.test(g.asset_name || '');
    const kindOf = g => {
        const at = (g.asset_type || '').toUpperCase();
        if (at === 'TRACK') return 'track';
        if (at === 'POINT MACHINE') return 'point';
        return isShuntName(g) ? 'shunt' : 'signal';
    };
    const tplFor = g => ({ track: CIRCUITS.TRACK, point: CIRCUITS.POINT, shunt: CIRCUITS.SHUNT, signal: CIRCUITS.SIGNAL })[kindOf(g)];

    async function openCircuit(gi) {
        const g = (S.gearsRendered || [])[gi];
        if (!g || g.asset_id == null || !S.pivotIso) return;
        const tpl = tplFor(g);
        const kind = kindOf(g);
        ensureCircuitDom();
        $('#sipCircuit').classList.add('on');   // opens BELOW the yard (both stay reachable)
        // scroll down to the freshly-opened circuit; the yard remains above (scroll up)
        requestAnimationFrame(() => { const c = $('#sipCircuit'); if (c) c.scrollIntoView({ behavior: 'smooth', block: 'start' }); });
        $('#cirTitle').textContent = `${g.asset_name} — ${kind} circuit`;
        $('#cirWhen').textContent = 'Loading…';
        $('#cirChips').innerHTML = '';
        $('#cirExtra').innerHTML = '';
        const mine = ++S.seq;
        const at = isoNoTz(S.pivotIso);
        const url = `${EP.trackCircuit}?${qp({ eld_id: S.eldId, asset_id: g.asset_id, at, before_min: PRE_SEC / 60, after_min: POST_SEC / 60 })}`;
        try {
            const d = await (await fetch(url)).json();
            if (mine !== S.seq) return;   // superseded
            renderCircuit(d, tpl);
        } catch (e) { $('#cirWhen').textContent = 'load error: ' + e; }
        // point machines also get a live operations table below the schematic
        if (tpl.opTable) loadOps(g); else { const ob = $('#cirOps'); if (ob) ob.innerHTML = ''; }
    }

    async function loadOps(g) {
        const box = $('#cirOps'); if (!box) return;
        box.innerHTML = `<div class="cir-ops-h">Operations</div><div class="sip-hint">Loading operations…</div>`;
        const mine = ++S.opSeq;
        const at = isoNoTz(S.pivotIso) || '';
        try {
            const d = await (await fetch(`${EP.pmOperations}?${qp({ site: S.site, asset_id: g.asset_id, at })}`)).json();
            if (mine !== S.opSeq) return;
            renderOps(d);
        } catch (e) { box.innerHTML = `<div class="cir-ops-h">Operations</div><div class="sip-hint">operations error: ${e}</div>`; }
    }

    function renderOps(d) {
        const box = $('#cirOps'); if (!box) return;
        const ops = d.operations || [], av = d.averages;
        const hdr = `<div class="cir-ops-h">Operations near trigger${ops.length ? ` <span class="off">(${ops.length})</span>` : ''}</div>`;
        if (!ops.length) { box.innerHTML = hdr + `<div class="sip-hint">${d.error ? 'No operation data — ' + d.error : 'No operations near this trigger.'}</div>`; return; }
        const c = (o, s, k) => { const v = o[s] && o[s][k]; return v == null ? '—' : v; };
        const fmtn = v => v == null ? '—' : v;
        const offLbl = o => o.off_s == null ? '' : (o.off_s >= 0 ? '+' : '') + o.off_s + 's';
        const rows = ops.map(o => `<tr class="${o.near ? 'near' : ''}">
        <td class="d">${fmtTs(o.time)}${o.near ? ' <span class="trg">◀ trigger</span>' : ''}<span class="o">${offLbl(o)}</span></td>
        <td><span class="dir ${(o.direction || '').toLowerCase()}">${o.direction || '—'}</span></td>
        <td>${c(o, 'a', 'current')}</td><td>${c(o, 'a', 'voltage')}</td><td class="t">${c(o, 'a', 'time_ms')}</td>
        <td>${c(o, 'b', 'current')}</td><td>${c(o, 'b', 'voltage')}</td><td class="t">${c(o, 'b', 'time_ms')}</td>
      </tr>`).join('');
        const avRow = av ? `<tr class="avg">
        <td class="d">Average</td><td>${av.count} ops</td>
        <td>${fmtn(av.a.current)}</td><td>${fmtn(av.a.voltage)}</td><td class="t">${fmtn(av.a.time_ms)}</td>
        <td>${fmtn(av.b.current)}</td><td>${fmtn(av.b.voltage)}</td><td class="t">${fmtn(av.b.time_ms)}</td></tr>` : '';
        box.innerHTML = hdr + `<div class="cir-ops-scroll"><table class="cir-ops-t">
      <thead><tr><th>Date</th><th>Direction</th><th>A&nbsp;Current</th><th>A&nbsp;Voltage</th><th>A&nbsp;Time&nbsp;(ms)</th>
        <th>B&nbsp;Current</th><th>B&nbsp;Voltage</th><th>B&nbsp;Time&nbsp;(ms)</th></tr></thead>
      <tbody>${avRow}${rows}</tbody></table></div>`;
    }

    function closeCircuit() {
        const c = $('#sipCircuit'); if (c) c.classList.remove('on');
        const tip = $('#cirTip'); if (tip) tip.classList.remove('on');
        window.scrollTo({ top: 0, behavior: 'smooth' });   // back up to the yard
    }

    function renderCircuit(d, tpl) {
        const VB = tpl.vb;
        // vb is [w,h] OR a full [minX,minY,w,h] viewBox (lets a template crop empty margins)
        const [VX, VY, VW, VH] = VB.length === 4 ? VB : [0, 0, VB[0], VB[1]];
        const attrs = d.attributes || [];
        // index name -> LIST: the historian can emit TWO attributes with the SAME name
        // (verified: a track has both "TPR" digital 0/1 AND "TPR" analog volts). A box
        // picks the right one by digital/analog — a STATUS box wants the 0/1 variant, a
        // value box the analog one; richest series breaks ties.
        const index = {};
        attrs.forEach(a => { const k = keyOf(a.name); (index[k] = index[k] || []).push(a); });
        const maxAbs = a => { let m = 0; for (const p of (a.series || [])) { const v = num(p.value); if (v != null && Math.abs(v) > m) m = Math.abs(v); } return m; };
        const isDigital = a => maxAbs(a) <= 1.5;   // 0/1 datalogger (a relay stuck at 0 is still digital → DOWN, not "—")
        const pickAttr = (list, wantDigital) => {
            if (!list || !list.length) return null;
            const pool = wantDigital ? list.filter(isDigital) : list.filter(a => !isDigital(a));
            const use = pool.length ? pool : (wantDigital ? [] : list);   // status box with no 0/1 variant → none (better "—" than analog-as-UP)
            return use.length ? use.slice().sort((a, b) => attrScore(b) - attrScore(a))[0] : null;
        };
        const boundObjs = new Set();   // exclude bound attrs from "Other readings" (by identity, since names can repeat)
        // no fuzzy match (exact name only) so "A End - NWKR" can't swallow "A End - NWKR (Loc)"
        const findAttr = (cands, wantDigital) => {
            for (const c of (Array.isArray(cands) ? cands : [cands])) {
                const got = pickAttr(index[keyOf(c)], wantDigital);
                if (got) return got;
            }
            return null;
        };
        CIR = { pivotMs: S.pivotMs, vb: VB };
        $('#cirWhen').innerHTML = `@ <b>${fmtClock(d.pivot)}</b> · closest-to-trigger values · ${attrs.length} readings`;

        // ── static SVG: zones, rails, wires, labels ───────────────────────────
        const svg = $('#cirSvg');
        svg.setAttribute('viewBox', `${VX} ${VY} ${VW} ${VH}`);
        const canvas = document.querySelector('#sipCircuit .cir-canvas');
        if (canvas) canvas.style.aspectRatio = `${VW} / ${VH}`;
        while (svg.firstChild) svg.removeChild(svg.firstChild);
        (tpl.zones || []).forEach(z => {
            svg.appendChild(svgEl('rect', { class: 'cir-zone', x: z.x, y: z.y, width: z.w, height: z.h, rx: 10 }));
            const t = svgEl('text', { class: 'cir-zone-lbl', x: z.lx, y: z.ly }); t.textContent = z.label; svg.appendChild(t);
        });
        (tpl.rails || []).forEach(r => svg.appendChild(svgEl('line', { class: 'cir-rail ' + (r.cls || ''), x1: r.x1, y1: r.y1, x2: r.x2, y2: r.y2 })));
        (tpl.wires || []).forEach(w => svg.appendChild(svgEl('path', { class: 'cir-wire ' + (w.cls || ''), d: w.d })));
        (tpl.statics || []).forEach(s => { const t = svgEl('text', { class: 'cir-static ' + (s.cls || ''), x: s.x, y: s.y }); t.textContent = s.label; svg.appendChild(t); });
        (tpl.specials || []).forEach(sp => {
            if (sp.type === 'signalHead') drawSignalHead(svg, sp, attrs, CIR.pivotMs);
            else if (sp.type === 'lamp') drawLamp(svg, sp, attrs, CIR.pivotMs);
            else if (sp.type === 'trackDecor') drawTrackDecor(svg);
            else if (sp.type === 'signalDecor') drawSignalDecor(svg);
            else if (sp.type === 'shuntDecor') drawShuntDecor(svg, attrs, CIR.pivotMs);
            else if (sp.type === 'pointDecor') drawPointDecor(svg);
        });

        // ── value chips (HTML, positioned by % of the viewBox) ────────────────
        const chips = $('#cirChips'); chips.innerHTML = '';
        (tpl.boxes || []).forEach(bx => {
            const a = findAttr(bx.attr, !!bx.status);   // status box → digital 0/1 variant
            const chip = document.createElement('div');
            chip.className = 'cir-box' + (bx.status ? ' status' : '') + (bx.wide ? ' wide' : '') + (a ? '' : ' nodata');
            chip.style.left = ((bx.x - VX) / VW * 100) + '%';
            chip.style.top = ((bx.y - VY) / VH * 100) + '%';
            let valHtml = '<span class="cv off">—</span>';
            if (a) {
                boundObjs.add(a);
                const arr = seriesArr(a);
                const v = resolve(arr, CIR.pivotMs);
                const n = (a.series || []).length;
                if (attrChanged(a)) chip.classList.add('changed');   // the attribute that moved → spot it instantly
                if (bx.status) {
                    const up = v != null && Math.abs(v) >= 0.5;
                    valHtml = `<span class="cv ${v == null ? 'off' : up ? 'up' : 'down'}">${v == null ? '—' : up ? 'UP' : 'DOWN'}</span>`;
                } else {
                    valHtml = `<span class="cv">${v == null ? '—' : round(v)}</span>`;
                }
                chip.dataset.n = n;
            }
            chip.innerHTML = `<span class="cl">${bx.label}</span>${valHtml}`;
            if (a) { chip.onmouseenter = e => showTip(e, a); chip.onmousemove = moveTip; chip.onmouseleave = hideTip; }
            chips.appendChild(chip);
        });

        // ── any attribute NOT bound to a box → "other readings" (nothing hidden) ────
        // changed ones float to the top so a moving reading is never buried.
        const extra = attrs.filter(a => !boundObjs.has(a))
            .sort((a, b) => (attrChanged(b) ? 1 : 0) - (attrChanged(a) ? 1 : 0));
        const ex = $('#cirExtra');
        if (extra.length) {
            const nch = extra.filter(attrChanged).length;
            ex.innerHTML = `<div class="cir-extra-h">Other readings (${extra.length})${nch ? ` · <span class="chg">${nch} changed</span>` : ''}</div>` +
                `<div class="cir-extra-grid">` + extra.map((a, i) => {
                    const v = resolve(seriesArr(a), CIR.pivotMs);
                    return `<div class="cir-ex${attrChanged(a) ? ' changed' : ''}" data-i="${i}"><span class="el">${a.name}</span><span class="ev">${v == null ? '—' : round(v)}</span></div>`;
                }).join('') + `</div>`;
            ex.querySelectorAll('.cir-ex').forEach(el => {
                const a = extra[+el.dataset.i];
                if (!a) return;
                el.onmouseenter = e => showTip(e, a); el.onmousemove = moveTip; el.onmouseleave = hideTip;
            });
        } else ex.innerHTML = '';
    }

    function seriesArr(a) {
        return (a.series || []).map(p => [Date.parse(p.ts), num(p.value)])
            .filter(p => p[0] && p[1] != null).sort((x, y) => x[0] - y[0]);
    }

    // an attribute CHANGED in the window if it logged any real in-window sample.
    // (the datalogger only records on change; the synthetic window-start point is
    // flagged `carried`, so any NON-carried point = a genuine change at the trigger.)
    function attrChanged(a) { return (a.series || []).some(p => p && !p.carried); }

    // richness score for de-duping same-named attributes: prefer the one that
    // actually moved (more in-window points), then the longer series overall.
    function attrScore(a) {
        const s = a.series || [];
        return s.filter(p => p && !p.carried).length * 100000 + s.length;
    }

    // drop exact duplicate (ts,value) samples the historian sometimes emits
    function dedupPts(pts) {
        const seen = new Set();
        return pts.filter(p => { const k = p.ts + '|' + p.value; if (seen.has(k)) return false; seen.add(k); return true; });
    }

    // value of any attribute whose NAME matches `re`, resolved at time tms (max |·|)
    function cirVal(attrs, re, tms) {
        let best = null;
        for (const a of attrs) {
            if (!re.test(a.name)) continue;
            const v = resolve(seriesArr(a), tms);
            if (v == null) continue;
            if (best == null || Math.abs(v) > Math.abs(best)) best = v;
        }
        return best;
    }

    // signal aspect head: 4 lamps; lamp lit when its aspect voltage ≥ 70V at trigger
    function drawSignalHead(svg, sp, attrs, tms) {
        const cx = sp.cx, top = sp.top, r = 30;
        const asp = sp.aspects, first = asp[0].cy - 44, last = asp[asp.length - 1].cy + 44;
        svg.appendChild(svgEl('rect', { class: 'cir-lampbody', x: cx - 42, y: first, width: 84, height: last - first, rx: 20 }));
        svg.appendChild(svgEl('rect', { class: 'cir-post', x: cx - 8, y: last, width: 16, height: 90 }));
        svg.appendChild(svgEl('rect', { class: 'cir-post', x: cx - 34, y: last + 90, width: 68, height: 22 }));
        asp.forEach(a => {
            const v = cirVal(attrs, new RegExp(a.re, 'i'), tms);
            const lit = v != null && Math.abs(v) >= 70;
            const c = svgEl('circle', { class: 'cir-lamp', cx, cy: a.cy, r });
            c.setAttribute('fill', lit ? a.on : '#2a3040');
            if (lit) c.setAttribute('filter', 'drop-shadow(0 0 7px ' + a.on + ')');
            svg.appendChild(c);
        });
    }

    // single LED-lit shunt lamp: lit when its aspect voltage ≥ 70V at the trigger
    function drawLamp(svg, sp, attrs, tms) {
        const v = cirVal(attrs, new RegExp(sp.re, 'i'), tms);
        const lit = v != null && Math.abs(v) >= 70;
        svg.appendChild(svgEl('rect', { class: 'cir-lampbody', x: sp.cx - 30, y: sp.cy - 22, width: 40, height: 44, rx: 8 }));
        const c = svgEl('circle', { class: 'cir-lamp', cx: sp.cx + 6, cy: sp.cy, r: 17 });
        c.setAttribute('fill', lit ? (sp.on || '#eef2f7') : '#2a3040');
        if (lit) c.setAttribute('filter', 'drop-shadow(0 0 6px ' + (sp.on || '#fff') + ')');
        svg.appendChild(c);
    }

    // ── TRACK CIRCUIT decor — rails, ties, RE-bonds, TLJB fuses, choke coil, battery,
    //    charger, (+)(−) resistors, U/G-cable feed-throughs, colored 24V/110V arrows,
    //    and the red(feed)/navy(return) wiring, drawn to match the real diagram.
    function drawTrackDecor(svg) {
        const RED = '#d23b3b', NAVY = '#1e2a44', BLUE = '#4a6fa5', BROWN = '#7a5230', TIE = '#c4ccd8';
        const add = (t, a) => { const e = svgEl(t, a); svg.appendChild(e); return e; };
        const W = (d, c, w) => add('path', { d, fill: 'none', stroke: c, 'stroke-width': w || 4, 'stroke-linejoin': 'round', 'stroke-linecap': 'round' });
        const T = (x, y, s, cls) => { const e = svgEl('text', { class: 'cir-static ' + (cls || ''), x, y }); e.textContent = s; svg.appendChild(e); };
        const fuse = (cx, y1, y2, c) => { add('line', { x1: cx, y1, x2: cx, y2, stroke: c, 'stroke-width': 2 }); add('rect', { x: cx - 7, y: (y1 + y2) / 2 - 14, width: 14, height: 28, rx: 6, fill: '#fff', stroke: c, 'stroke-width': 2 }); add('circle', { cx, cy: y1, r: 4, fill: c }); add('circle', { cx, cy: y2, r: 4, fill: c }); };
        const res = (cx, y1, y2, c) => { add('line', { x1: cx, y1, x2: cx, y2, stroke: c, 'stroke-width': 2 }); add('rect', { x: cx - 6, y: (y1 + y2) / 2 - 15, width: 12, height: 30, fill: '#fff', stroke: c, 'stroke-width': 2 }); add('circle', { cx, cy: y1, r: 4, fill: c }); add('circle', { cx, cy: y2, r: 4, fill: c }); };
        const conn = (cx, cy, c) => { add('path', { d: `M ${cx - 7},${cy - 9} a 9 9 0 0 0 0 18`, fill: 'none', stroke: c, 'stroke-width': 2 }); add('path', { d: `M ${cx + 7},${cy - 9} a 9 9 0 0 1 0 18`, fill: 'none', stroke: c, 'stroke-width': 2 }); add('circle', { cx: cx - 7, cy, r: 3, fill: c }); add('circle', { cx: cx + 7, cy, r: 3, fill: c }); };
        const arrow = (x1, y, x2, c) => { add('line', { x1, y1: y, x2: x2 - 13, y2: y, stroke: c, 'stroke-width': 5 }); add('path', { d: `M ${x2 - 17},${y - 9} L ${x2},${y} L ${x2 - 17},${y + 9} Z`, fill: c }); };
        const coil = (cx, y0, c) => { let d = `M ${cx},${y0}`; for (let i = 0; i < 4; i++) d += ' a 8 6.5 0 0 1 0 13'; W(d, c, 3); };
        const tljb = (cx) => { add('rect', { x: cx - 31, y: 300, width: 62, height: 22, rx: 4, fill: NAVY }); const e = svgEl('text', { class: 'cir-static', x: cx, y: 311, style: 'fill:#fff' }); e.textContent = 'TLJB'; svg.appendChild(e); };

        // rails + sleeper ties + rail labels
        const RX1 = 158, RX2 = 902, RY1 = 222, RY2 = 292;
        for (let x = 182; x <= 878; x += 26) { if ((x > 196 && x < 268) || (x > 652 && x < 724)) continue; add('rect', { x: x - 3, y: RY1 + 9, width: 7, height: RY2 - RY1 - 18, rx: 2, fill: TIE }); }
        add('line', { x1: RX1, y1: RY1, x2: RX2, y2: RY1, stroke: NAVY, 'stroke-width': 5 });   // Rail 1 (top) = black/navy
        add('line', { x1: RX1, y1: RY2, x2: RX2, y2: RY2, stroke: RED, 'stroke-width': 5 });    // Rail 2 (bottom) = red
        T(120, RY1 + 5, 'Rail 1', 'rail'); T(120, RY2 + 18, 'Rail 2', 'rail');

        // RE bonds (bracket + arrows down to the rails)
        [232, 690].forEach(cx => {
            T(cx, 190, 'RE Bond', 'tag');
            W(`M ${cx - 30},206 L ${cx - 30},${RY1 - 3}`, NAVY, 1.5); add('path', { d: `M ${cx - 34},${RY1 - 8} L ${cx - 30},${RY1 - 1} L ${cx - 26},${RY1 - 8} Z`, fill: NAVY });
            W(`M ${cx + 30},206 L ${cx + 30},${RY2 - 3}`, NAVY, 1.5); add('path', { d: `M ${cx + 26},${RY2 - 8} L ${cx + 30},${RY2 - 1} L ${cx + 34},${RY2 - 8} Z`, fill: NAVY });
            W(`M ${cx - 30},206 L ${cx + 30},206`, NAVY, 1.5);
        });

        // ── AC-9 feed side: TLJB → fuses → choke → charger → (N)/(P) ──
        tljb(232);
        fuse(212, 324, 384, RED); fuse(252, 324, 384, NAVY);
        W('M212,292 L212,300', RED, 2); W('M252,222 L252,300', NAVY, 2);   // red fuse↔red rail(btm), navy fuse↔navy rail(top)
        // red feed down through the choke to the charger, then to (P)
        W('M212,384 L212,406', RED, 3); coil(212, 406, RED); W('M212,458 L212,616', RED, 3);
        T(300, 392, 'Choke', 'fix');
        add('rect', { x: 185, y: 618, width: 92, height: 30, rx: 5, fill: '#e6eaf1', stroke: '#9aa7bb' }); T(231, 636, 'Charger', 'fix');
        // battery symbol on the tap between choke and charger
        (function (cx, cy) { add('line', { x1: cx - 14, y1: cy - 11, x2: cx - 14, y2: cy + 11, stroke: NAVY, 'stroke-width': 2 }); add('line', { x1: cx - 6, y1: cy - 6, x2: cx - 6, y2: cy + 6, stroke: NAVY, 'stroke-width': 4 }); add('line', { x1: cx + 2, y1: cy - 11, x2: cx + 2, y2: cy + 11, stroke: NAVY, 'stroke-width': 2 }); add('line', { x1: cx + 10, y1: cy - 6, x2: cx + 10, y2: cy + 6, stroke: NAVY, 'stroke-width': 4 }); })(238, 552);
        T(238, 526, 'Battery', 'fix');
        // navy feeder + (N)(P) fuses at the bottom
        W('M252,384 L252,730', NAVY, 3); W('M212,648 L212,730', RED, 3);
        fuse(212, 730, 786, RED); fuse(258, 730, 786, NAVY);
        T(180, 754, '(N)', 'fix'); T(340, 754, '(P) 110V. AC', 'fix');
        // down to the U/G cable at the very bottom (blue 110V AC + brown)
        W('M212,786 L212,878 L1120,878', BROWN, 4); arrow(1080, 878, 1150, BROWN);
        W('M258,786 L258,912 L1120,912', BLUE, 4); arrow(1080, 912, 1150, BLUE);

        // ── AC-11 relay side: TLJB → fuses → (+)(−) resistors → U/G cable → 24V out ──
        tljb(690);
        fuse(668, 324, 384, RED); fuse(710, 324, 384, NAVY);
        W('M668,292 L668,300', RED, 2); W('M710,222 L710,300', NAVY, 2);
        // resistor bank (two +/- pairs)
        res(540, 596, 656, RED); res(600, 596, 656, NAVY); res(672, 596, 656, RED); res(726, 596, 656, NAVY);
        T(514, 626, '(+)', 'tag'); T(628, 626, '(−)', 'tag'); T(650, 626, '(+)', 'tag'); T(760, 626, '(−)', 'tag');
        // red feed from fuses to resistor tops; red return bus to the U/G connectors → B-24V
        W('M668,384 L668,560 L540,560 L540,596', RED, 3); W('M540,560 L672,560 L672,596', RED, 3);
        W('M540,656 L540,700 L828,700', RED, 3);
        // navy bus from resistors → N-24V
        W('M600,656 L600,672 L828,672', NAVY, 3); W('M726,656 L726,672', NAVY, 3);
        W('M710,384 L710,672', NAVY, 3);
        T(762, 712, '24V. DC FROM R/R', 'tag');
        // bottom 24V pair to the LOC / TPR-I/P boxes and out via U/G
        W('M675,790 L675,760 L828,760', NAVY, 3); W('M828,800 L1045,800 L1045,830', NAVY, 3);

        // U/G cable feed-throughs (two columns) + labels
        [672, 700, 760, 800].forEach(y => { conn(842, y, NAVY); conn(966, y, NAVY); });
        T(902, 662, 'U/G CABLE', 'tag'); T(902, 752, 'U/G CABLE', 'tag');
        W('M852,672 L956,672', NAVY, 3); W('M852,700 L956,700', RED, 3); W('M852,760 L956,760', NAVY, 3); W('M852,800 L956,800', NAVY, 3);
        // 24V DC outputs into the relay room
        arrow(978, 672, 1150, NAVY); T(1090, 664, 'N-24V', 'io');
        arrow(978, 700, 1150, RED); T(1090, 692, 'B-24V', 'io');
        T(1082, 872, 'Bx110V', 'io'); T(1082, 906, 'Nx110V', 'io');

        // Track Relay (blue) between the relay-end boxes
        add('rect', { x: 626, y: 476, width: 130, height: 34, rx: 8, fill: '#2f6fd6' });
        const tr = svgEl('text', { class: 'cir-static', x: 691, y: 496, style: 'fill:#fff;font-size:13px;font-weight:800' }); tr.textContent = 'Track Relay'; svg.appendChild(tr);
        // relay coil symbol under the right TLJB (feeds TR V (Relay))
        add('rect', { x: 702, y: 402, width: 16, height: 26, rx: 3, fill: '#fff', stroke: NAVY, 'stroke-width': 2 }); add('line', { x1: 706, y1: 408, x2: 714, y2: 408, stroke: NAVY }); add('line', { x1: 706, y1: 414, x2: 714, y2: 414, stroke: NAVY }); add('line', { x1: 706, y1: 420, x2: 714, y2: 420, stroke: NAVY });
    }

    // ── SIGNAL decor — 4 aspect circuits: red Bx110V feed (resistor squiggle) +
    //    black Nx110V return (left arrow), AC-10 relay coils, SIGNAL UNIT line.
    function drawSignalDecor(svg) {
        const RED = '#d23b3b', NAVY = '#1e2a44';
        const add = (t, a) => { const e = svgEl(t, a); svg.appendChild(e); return e; };
        const W = (d, c, w) => add('path', { d, fill: 'none', stroke: c, 'stroke-width': w || 3, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' });
        const T = (x, y, s, cls) => { const e = svgEl('text', { class: 'cir-static ' + (cls || ''), x, y }); e.textContent = s; svg.appendChild(e); };
        const squig = (x, y, c) => W(`M ${x},${y} l 5,-6 l 6,12 l 6,-12 l 6,12 l 6,-12 l 5,6`, c, 2);
        const arrowL = (x, y, c) => add('path', { d: `M ${x + 13},${y - 7} L ${x},${y} L ${x + 13},${y + 7} Z`, fill: c });
        const coil = (x, y, c) => { W(`M ${x},${y - 10} L ${x + 11},${y} L ${x},${y + 10}`, c, 2); W(`M ${x + 18},${y - 10} a 10 10 0 1 0 0 20`, c, 2); };
        const RLX = 248, ACX = 856, HEADX = 1092;
        [[100, 158, 128], [225, 283, 253], [350, 408, 378], [478, 536, 508]].forEach(([fy, ry, hy]) => {
            T(RLX - 4, fy - 9, 'Bx110V', 'io'); squig(RLX, fy, RED); W(`M ${RLX + 33},${fy} L ${ACX},${fy}`, RED, 3);
            T(RLX - 4, ry + 16, 'Nx110V', 'io'); W(`M ${RLX + 14},${ry} L ${ACX},${ry}`, NAVY, 3); arrowL(RLX, ry, NAVY);
            coil(ACX + 6, fy, NAVY); coil(ACX + 6, ry, NAVY);
            W(`M ${ACX + 42},${fy} L ${HEADX},${hy}`, RED, 2); W(`M ${ACX + 42},${ry} L ${HEADX},${hy}`, NAVY, 2);
        });
        // relay-ladder brackets (series-contact loops) — the flowchart look
        const brk = (x1, x2, y, dy) => W(`M ${x1},${y} L ${x1},${y + dy} L ${x2},${y + dy} L ${x2},${y}`, NAVY, 1.6);
        brk(415, 600, 118, 30);    // HHPR feed: DR–DECR series loop
        brk(415, 590, 158, -30);   // HHPR return loop
        brk(400, 545, 372, 34);    // HPR feed: DECR–DR loop
        brk(360, 660, 500, 34);    // RG return: DECR–HHECR–HECR loop
        T(300, 122, 'DECR', 'fix'); T(470, 200, 'DR', 'fix'); T(575, 200, 'DECR', 'fix');   // repeated contacts (labels only)
        T(922, 150, 'HHPR', 'fix'); T(922, 275, 'DPR', 'fix'); T(922, 400, 'HPR', 'fix');
        add('line', { x1: 1018, y1: 65, x2: 1018, y2: 560, stroke: '#9aa7bb', 'stroke-width': 1.4, 'stroke-dasharray': '5 4' });
        T(1018, 50, 'SIGNAL UNIT', 'tag');
    }

    // ── SHUNT decor — all-black ladder: 6 aspect lines (OFF/OFFN/ON/ONN/PILOTN×2),
    //    OFFECR/ONECR relays (R2/R1), HR contacts, current-regulating-unit contact
    //    glyphs, 3 LED lamps (lit by VSHSIG≥70) + labels, bottom B24→HPR circuit.
    function drawShuntDecor(svg, attrs, tms) {
        const NAVY = '#1e2a44';
        const add = (t, a) => { const e = svgEl(t, a); svg.appendChild(e); return e; };
        const W = (d, w) => add('path', { d, fill: 'none', stroke: NAVY, 'stroke-width': w || 3, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' });
        const T = (x, y, s, cls) => { const e = svgEl('text', { class: 'cir-static ' + (cls || ''), x, y }); e.textContent = s; svg.appendChild(e); };
        const dot = (x, y) => add('circle', { cx: x, cy: y, r: 6, fill: NAVY });
        const noc = (x, y) => { add('path', { d: `M ${x - 15},${y - 7} L ${x - 5},${y} L ${x - 15},${y + 7} Z`, fill: NAVY }); add('path', { d: `M ${x + 15},${y - 7} L ${x + 5},${y} L ${x + 15},${y + 7} Z`, fill: NAVY }); };
        const dash = x => add('line', { x1: x, y1: 40, x2: x, y2: 560, stroke: '#9aa7bb', 'stroke-width': 1.4, 'stroke-dasharray': '5 4' });
        const led = (cx, cy, lit) => {
            add('rect', { x: cx - 34, y: cy - 16, width: 26, height: 32, rx: 3, fill: '#fff', stroke: NAVY, 'stroke-width': 2 });
            add('rect', { x: cx - 10, y: cy - 20, width: 16, height: 40, rx: 2, fill: '#fff', stroke: NAVY, 'stroke-width': 2 });
            const dm = add('path', { d: `M ${cx + 6},${cy - 20} a 22 20 0 1 1 0 40 Z`, stroke: NAVY, 'stroke-width': 2 });
            dm.setAttribute('fill', lit ? '#eef2f7' : '#1c2230'); if (lit) dm.setAttribute('filter', 'drop-shadow(0 0 6px #fff)');
        };
        const X0 = 130, XEND = 990;
        [['OFF', 70], ['OFFN', 140], ['ON', 250], ['ONN', 320], ['PILOTN', 430], ['PILOTN', 500]].forEach(([lbl, y]) => {
            W(`M ${X0},${y} L ${XEND},${y}`, 3); T(500, y - 9, lbl, 'fix'); T(330, y - 9, 'HR', 'ecr');
            noc(648, y); dot(716, y); noc(886, y);
        });
        T(55, 62, 'BX110V (SIG-W)', 'io'); T(55, 132, 'Nx110V (SIG-W)', 'io');
        T(60, 596, 'B24 (EXT-W)', 'io'); T(60, 700, 'B24 (EXT-W)', 'io');
        dot(X0, 140); dot(X0, 600); dot(X0, 700);
        // OFFECR / ONECR relays (R2/R1) + branch wiring
        add('rect', { x: 198, y: 128, width: 78, height: 26, rx: 3, fill: '#fff', stroke: NAVY, 'stroke-width': 2 }); T(206, 122, 'R2', 'tag'); T(262, 122, 'R1', 'tag');
        add('rect', { x: 185, y: 308, width: 78, height: 26, rx: 3, fill: '#fff', stroke: NAVY, 'stroke-width': 2 }); T(193, 302, 'R2', 'tag'); T(249, 302, 'R1', 'tag');
        W(`M ${X0},140 L 198,140`, 2); W('M276,141 L320,141 L320,70', 2); W('M276,148 L300,148 L300,140', 2);
        W('M237,154 L237,308', 2); W('M263,334 L263,250 L300,250', 2); W('M224,334 L224,500 L360,500', 2);
        W('M360,430 L360,470', 2);
        // dashed boundaries: current-regulating-unit + LED unit
        [690, 915, 935, 1145].forEach(dash);
        // 3 LED lamps + labels (lit when the aspect voltage ≥ 70V)
        [['Off', 105, '^Off Aspect V'], ['On', 285, '^On Aspect V'], ['PILOT', 465, '^PILOT V']].forEach(([g, cy, re]) => {
            const v = cirVal(attrs, new RegExp(re, 'i'), tms);
            led(1015, cy, v != null && Math.abs(v) >= 70);
            T(1015, cy - 42, 'LED LIT SIG', 'fix'); T(1040, cy + 44, 'Current Regulating Unit', 'tag');
        });
        // bottom B24 → HPR circuit
        W(`M ${X0},600 L 610,600 L 660,650 L 886,650`, 3); W(`M ${X0},700 L 560,700 L 610,650`, 3);
        T(330, 596, 'HR', 'ecr'); T(330, 696, 'HR', 'ecr'); noc(648, 650); dot(716, 650);
    }

    // ── POINT decor — feeds (B-24V red / N-24V navy / B-110V orange / N-110V navy),
    //    location-box fuse contacts, 'A'/'B' END motors with N/F·R/F coils + ammeter.
    function drawPointDecor(svg) {
        const RED = '#d23b3b', NAVY = '#1e2a44', ORANGE = '#e8892b';
        const add = (t, a) => { const e = svgEl(t, a); svg.appendChild(e); return e; };
        const W = (d, c, w) => add('path', { d, fill: 'none', stroke: c, 'stroke-width': w || 3, 'stroke-linecap': 'round', 'stroke-linejoin': 'round' });
        const T = (x, y, s, cls) => { const e = svgEl('text', { class: 'cir-static ' + (cls || ''), x, y }); e.textContent = s; svg.appendChild(e); };
        const squig = (x, y, c) => W(`M ${x},${y} l 5,-6 l 6,12 l 6,-12 l 5,6`, c, 2);
        const arrowL = (x, y, c) => add('path', { d: `M ${x + 13},${y - 7} L ${x},${y} L ${x + 13},${y + 7} Z`, fill: c });
        const fuse = (x, y) => { add('rect', { x: x - 6, y: y - 11, width: 12, height: 22, rx: 5, fill: '#fff', stroke: NAVY, 'stroke-width': 2 }); add('circle', { cx: x, cy: y - 11, r: 3.5, fill: NAVY }); add('circle', { cx: x, cy: y + 11, r: 3.5, fill: NAVY }); };
        const coilH = (x, y, c) => { let d = `M ${x},${y}`; for (let i = 0; i < 4; i++) d += ' a 6.5 8 0 0 1 13 0'; W(d, c, 2.5); };
        const motor = (cx, cy, lbl) => { add('circle', { cx, cy, r: 16, fill: '#fff', stroke: NAVY, 'stroke-width': 2 }); const m = svgEl('text', { class: 'cir-static', x: cx, y: cy, style: 'fill:#1e2a44;font-size:13px;font-weight:800' }); m.textContent = 'M'; svg.appendChild(m); add('rect', { x: cx - 8, y: cy + 16, width: 16, height: 7, fill: NAVY }); T(cx, cy - 26, lbl, 'fix'); };
        const amm = (x, y) => { add('circle', { cx: x, cy: y, r: 9, fill: '#fff', stroke: ORANGE, 'stroke-width': 1.5 }); const t = svgEl('text', { class: 'cir-static', x, y, style: 'fill:#e8892b;font-size:9px;font-weight:800' }); t.textContent = 'A'; svg.appendChild(t); };

        // feed lines
        T(52, 64, 'B-24V', 'io'); squig(70, 68, RED); W('M100,68 L 1005,68', RED, 3);
        T(52, 96, 'N-24V', 'io'); W('M70,92 L 1005,92', NAVY, 3); arrowL(58, 92, NAVY);
        T(52, 181, 'B-110V', 'io'); squig(70, 185, ORANGE); W('M100,185 L 855,185', ORANGE, 3);
        T(52, 276, 'N-110V', 'io'); W('M70,280 L 855,280', NAVY, 3); arrowL(58, 280, NAVY);
        // relay-room contact labels
        T(200, 181, 'A End - NWCR', 'fix'); T(210, 222, 'A End - RWCR', 'fix');
        T(210, 277, 'A End - NWCR', 'fix'); T(210, 310, 'A End - RWCR', 'fix');
        T(205, 596, 'NWPR / NWCZR', 'fix'); T(210, 640, 'RWPR / RWCZR', 'fix');
        // location-box fuse contacts on each crossing wire
        [340, 400, 470, 540].forEach(y => fuse(382, y));
        [92, 150, 210, 300, 360, 430].forEach(y => fuse(647, y));
        // dashed inner motor boundary (like the reference)
        add('line', { x1: 470, y1: 260, x2: 470, y2: 300, stroke: '#9aa7bb', 'stroke-width': 1.4, 'stroke-dasharray': '5 4' });
        // motors + N/F · R/F coils + ammeter
        motor(470, 235, "'A' END MOTOR"); coilH(505, 216, ORANGE); T(535, 208, 'N/F', 'ecr'); coilH(505, 250, ORANGE); T(535, 266, 'R/F', 'ecr'); amm(575, 235);
        motor(858, 34, "'B' END MOTOR"); coilH(818, 210, ORANGE); T(848, 202, 'N/F', 'ecr'); coilH(818, 244, ORANGE); T(848, 260, 'R/F', 'ecr'); amm(888, 227);
        // a few connecting runs (approx): 24V loop to B-END motor, 110V to A-END motor
        W('M1005,68 L1005,42 L905,42 L905,20', RED, 3); W('M1005,92 L1020,92 L1020,240 L905,240', NAVY, 2);
        W('M855,185 L905,185 L905,235 L590,235', ORANGE, 2);
    }

    // hover tooltip: the full −180s…+5s series for one attribute
    function showTip(e, a) {
        const tip = $('#cirTip'); if (!tip) return;
        const pts = dedupPts((a.series || []).filter(p => p.ts != null && p.value != null));
        const rows = pts.map(p => {
            const off = (CIR && CIR.pivotMs != null) ? Math.round((Date.parse(p.ts) - CIR.pivotMs) / 1000) : null;
            const near = (off != null && Math.abs(off) <= 0);
            return `<div class="tr ${near ? 'now' : ''}"><span class="tt">${fmtClock(p.ts)}</span>
        <span class="to">${off == null ? '' : (off >= 0 ? '+' : '') + off + 's'}</span>
        <span class="tv">${round(num(p.value))}</span></div>`;
        }).join('') || '<div class="tr off">no samples in window</div>';
        tip.innerHTML = `<div class="th">${a.name} <span class="off">· ${pts.length} pts</span></div>${rows}`;
        tip.classList.add('on'); moveTip(e);
    }
    function moveTip(e) {
        const tip = $('#cirTip'); if (!tip || !tip.classList.contains('on')) return;
        const host = $('#sipCircuit').getBoundingClientRect();
        let x = e.clientX - host.left + 14, y = e.clientY - host.top + 14;
        const tw = tip.offsetWidth, th = tip.offsetHeight;
        if (x + tw > host.width) x = e.clientX - host.left - tw - 14;
        if (y + th > host.height) y = host.height - th - 8;
        tip.style.left = Math.max(6, x) + 'px'; tip.style.top = Math.max(6, y) + 'px';
    }
    function hideTip() { const tip = $('#cirTip'); if (tip) tip.classList.remove('on'); }

    return { init };
})();