/* =====================================================================
 * TELEMETRY LIVE — INTERACTIVE GRAPH VIEWS  (v617 + ported v616 behaviour)
 *
 * Load AFTER telemetrylive.js, telemetrylive-graphs.js (and the *616 files).
 *
 * Adds a three-way view switch to every history graph on the page:
 *   Overlay     – the existing v617 chart, unchanged.
 *   Stacked     – NEW "lane board": one lane per attribute on a single
 *                 synchronised time axis, HTML lane gutter with a live
 *                 cursor read-out (no floating tooltip), min/max/avg per
 *                 lane, relay lanes shown as Pickup bands, lane density
 *                 S/M/L, hide / focus per lane.  (v616 used white lanes
 *                 with an axis per lane and an "Av" label — replaced.)
 *   Individual  – NEW small-multiples grid: one chart card per attribute
 *                 with its own stats, live value, expand, PNG and hide;
 *                 cursor + zoom optionally synchronised across cards.
 *
 * Surfaces wired:
 *   grph  – Graph view (fnBindGrph / _gLoad / _gRender) in #divTelemetryLive
 *   modal – per-asset Historical Graph overlay (fnGetAssetGraph /
 *           loadHistoryGraphData / renderHistoryChart)
 *   ips   – IPS History Graph (telemetrylive-graphs.js, ipsG / _ipsRender);
 *           its old v616-style Stacked mode is replaced by the lane board.
 *
 * Ported from v616 telemetrylive.js:
 *   - _gToLocalInput / _gSyncDateInputs / _gLoadWindow / _gLoadRange
 *     (10825-10885, 11438-11547): custom From/To range for the Graph view,
 *     future dates blocked (max refreshed every 30 s + clamp on change).
 *   - _gUpdatePanelValuesOnlyFromWebSocket / formatLivePanelValue
 *     (11599-11800): attribute panel values refresh in place from the
 *     WebSocket store, floored at 0.
 *
 * Every value is floored with tlZeroFloor (<= 0 -> 0).
 * No shared file is modified; everything is done by wrapping globals.
 * ===================================================================== */
(function (window, $) {
    'use strict';
    if (!$ || window.TLGV) return;

    var TLGV = window.TLGV = { version: '1.0.1', ctx: {} };

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------
    function zf(v) {
        if (typeof window.tlZeroFloor === 'function') return window.tlZeroFloor(v);
        var n = parseFloat(v); if (isNaN(n)) return v; return n <= 0 ? 0 : n;
    }
    function num(v) { var n = parseFloat(v); return isNaN(n) ? null : n; }
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function pad(n) { return String(n).padStart(2, '0'); }
    var MON = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    function fmtAxis(ms, span) {
        var d = new Date(ms);
        var hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
        if (span > 30 * 3600000) return pad(d.getDate()) + ' ' + MON[d.getMonth()] + '\n' + hm;
        return hm;
    }
    function fmtFull(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + ' ' + MON[d.getMonth()] + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    function toLocalInput(ms) {
        var d = new Date(ms);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' + pad(d.getHours()) + ':' + pad(d.getMinutes());
    }
    function hexA(hex, a) {
        var h = String(hex || '').trim();
        if (h.indexOf('rgb') === 0) return h.replace(/rgba?\(([^)]+)\)/, function (m, inner) {
            var p = inner.split(','); return 'rgba(' + p[0] + ',' + p[1] + ',' + p[2] + ',' + a + ')';
        });
        h = h.replace('#', '');
        if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
        var r = parseInt(h.substr(0, 2), 16), g = parseInt(h.substr(2, 2), 16), b = parseInt(h.substr(4, 2), 16);
        if (isNaN(r)) return 'rgba(34,211,238,' + a + ')';
        return 'rgba(' + r + ',' + g + ',' + b + ',' + a + ')';
    }
    function warn(msg) {
        if (typeof window.showWarning === 'function') window.showWarning(msg, 'Graph');
        else if (window.console) console.warn('[TLGV]', msg);
    }
    function niceMax(v) {
        if (!(v > 0)) return 1;
        var m = v * 1.15, p = Math.pow(10, Math.floor(Math.log10(m))), f = m / p;
        var nf = f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10;
        return nf * p;
    }
    function loadPrefs() {
        try { return JSON.parse(window.localStorage.getItem('tlgv.prefs.v1')) || {}; } catch (e) { return {}; }
    }
    function savePrefs() {
        try { window.localStorage.setItem('tlgv.prefs.v1', JSON.stringify(prefs)); } catch (e) { /* ignore */ }
    }
    var prefs = loadPrefs();
    prefs.mode = prefs.mode || {};
    prefs.density = prefs.density || 'm';
    prefs.cols = prefs.cols || 'auto';
    if (prefs.sync === undefined) prefs.sync = true;

    // value at time t (carry-forward) — pts sorted asc
    function valueAt(pts, t) {
        if (!pts || !pts.length || t < pts[0][0]) return null;
        var lo = 0, hi = pts.length - 1, idx = -1;
        while (lo <= hi) { var mid = (lo + hi) >> 1; if (pts[mid][0] <= t) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
        return idx >= 0 ? pts[idx][1] : null;
    }
    // collapse consecutive equal values (keeps first of run + final point)
    function compress(pts) {
        var out = [];
        for (var i = 0; i < pts.length; i++) {
            var p = pts[i];
            if (out.length && out[out.length - 1][0] === p[0]) { out[out.length - 1] = p; continue; }
            if (out.length > 1 && out[out.length - 1][1] === p[1] && out[out.length - 2][1] === p[1]) { out[out.length - 1] = p; continue; }
            out.push(p);
        }
        return out;
    }

    function computeStats(s, xMin, xMax) {
        var st = { count: s.actual.length, min: null, max: null, avg: null, last: null, lastT: null, pickups: 0, drops: 0, onPct: null };
        var pts = s.pts;
        if (!pts.length) return st;
        var lastA = s.actual.length ? s.actual[s.actual.length - 1] : pts[pts.length - 1];
        st.last = lastA[1]; st.lastT = lastA[0];
        var end = Math.min(xMax || Date.now(), Date.now());
        var start = (xMin != null) ? xMin : pts[0][0];
        if (s.kind === 'bin') {
            var prev = null, on = 0, tot = 0;
            for (var i = 0; i < pts.length; i++) {
                var v = pts[i][1] >= 0.5 ? 1 : 0;
                if (prev !== null && v !== prev) { if (v === 1) st.pickups++; else st.drops++; }
                prev = v;
                var t0 = Math.max(pts[i][0], start), t1 = Math.min(i + 1 < pts.length ? pts[i + 1][0] : end, end);
                if (t1 > t0) { tot += t1 - t0; if (v === 1) on += t1 - t0; }
            }
            st.onPct = tot > 0 ? (on * 100 / tot) : null;
            return st;
        }
        var src = s.actual.length ? s.actual : pts;
        var mn = Infinity, mx = -Infinity;
        for (var j = 0; j < src.length; j++) { var x = src[j][1]; if (x < mn) mn = x; if (x > mx) mx = x; }
        st.min = mn; st.max = mx;
        // time-weighted average across the visible window
        var area = 0, span = 0;
        for (var k = 0; k < pts.length; k++) {
            var a0 = Math.max(pts[k][0], start), a1 = Math.min(k + 1 < pts.length ? pts[k + 1][0] : end, end);
            if (a1 > a0) { area += pts[k][1] * (a1 - a0); span += a1 - a0; }
        }
        if (span > 0) st.avg = area / span;
        else { var sum = 0; src.forEach(function (p) { sum += p[1]; }); st.avg = sum / src.length; }
        return st;
    }
    function pickupBands(pts, end) {
        var bands = [], startOn = null;
        for (var i = 0; i < pts.length; i++) {
            var on = pts[i][1] >= 0.5;
            if (on && startOn === null) startOn = pts[i][0];
            if (!on && startOn !== null) { bands.push([startOn, pts[i][0]]); startOn = null; }
        }
        if (startOn !== null) bands.push([startOn, end]);
        return bands;
    }
    function unitFor(name, kind) {
        if (kind === 'bin') return '0/1';
        var n = String(name || '');
        var m = n.match(/\((mA|mV|V|Ω|A)\)\s*$/);
        if (m) return m[1];
        if (/\bmV\b/i.test(n)) return 'mV';
        if (/\bmA\b/i.test(n)) return 'mA';
        if (kind === 'ma') return 'mA';
        if (kind === 'v') return 'V';
        if (/\bV\b/.test(n)) return 'V';
        return '';
    }
    function kindLabel(kind) {
        return { ma: 'Current', v: 'Voltage', bin: 'Relay', drv: 'Derived', other: 'Value' }[kind] || 'Value';
    }
    function fmtVal(v, kind) {
        if (v === null || v === undefined || isNaN(v)) return '—';
        if (kind === 'bin') return v >= 0.5 ? 'Pickup' : 'Drop';
        return Number(zf(v)).toFixed(2);
    }
    function valHtml(v, kind, unit) {
        if (kind === 'bin') {
            if (v === null || v === undefined || isNaN(v)) return '<span class="tlgv-na">—</span>';
            return '<span class="tlgv-badge ' + (v >= 0.5 ? 'up' : 'dn') + '">' + (v >= 0.5 ? 'Pickup' : 'Drop') + '</span>';
        }
        return '<b>' + fmtVal(v, kind) + '</b>' + (unit && v !== null && v !== undefined ? '<i>' + esc(unit) + '</i>' : '');
    }

    // ------------------------------------------------------------------
    // model adapters  ->  { xMin, xMax, series:[{key,name,sub,color,kind,unit,pts,actual,live}] }
    // ------------------------------------------------------------------
    function finishSeries(list, xMin, xMax) {
        var end = Math.min(xMax != null ? xMax : Date.now(), Date.now());
        list.forEach(function (s) {
            s.pts = s.pts.filter(function (p) { return p && !isNaN(p[0]) && p[1] !== null && !isNaN(p[1]); })
                .map(function (p) { return [p[0], Number(zf(p[1]))]; })
                .sort(function (a, b) { return a[0] - b[0]; });
            s.actual = (s.actual || s.pts).map(function (p) { return [p[0], Number(zf(p[1]))]; });
            s.pts = compress(s.pts);
            if (s.pts.length && s.pts[s.pts.length - 1][0] < end) s.pts.push([end, s.pts[s.pts.length - 1][1]]);
            if (!s.unit) s.unit = unitFor(s.name, s.kind);
            s.stats = computeStats(s, xMin, xMax);
        });
        return { xMin: xMin, xMax: end, series: list.filter(function (s) { return s.pts.length; }) };
    }

    function fromGraphView() {
        var ch = window.mainGraphChart;
        if (!ch || !ch.getOption) return null;
        var opt = ch.getOption() || {};
        var defs = window._DERIVED_DEFS || [];
        var out = [];
        (opt.series || []).forEach(function (s, i) {
            var raw = String(s.name || ('Series ' + i));
            var isDL = raw.indexOf('[DL] ') === 0, isDrv = raw.indexOf('[D] ') === 0;
            var name = raw.replace(/^\[DL\] /, '').replace(/^\[D\] /, '');
            var kind = isDL ? 'bin' : isDrv ? 'drv' : (s.yAxisIndex === 0 ? 'ma' : 'v');
            var unit = '';
            if (isDrv) defs.forEach(function (d) { if (d.label === name) unit = d.unit; });
            var pts = [], actual = [];
            (s.data || []).forEach(function (d) {
                var v = (d && d.value) ? d.value : d;
                if (!v || v.length < 2) return;
                var p = [Number(v[0]), Number(v[1])];
                pts.push(p);
                if (isDL || (d && d.symbolSize > 0)) actual.push(p);
            });
            if (isDL) actual = compress(actual);
            out.push({
                key: raw, name: name, sub: '', color: (s.lineStyle && s.lineStyle.color) || (s.itemStyle && s.itemStyle.color) || '#22d3ee',
                kind: kind, unit: unit, pts: pts, actual: actual,
                live: { src: 'ws', aid: window._gA, name: name, dl: isDL }
            });
        });
        var xMin = (window._gReqStart != null) ? window._gReqStart : null;
        var xMax = (window._gReqEnd != null) ? window._gReqEnd : Date.now();
        return finishSeries(out, xMin, xMax);
    }

    function fromModal() {
        var store = window._rdpmsTooltipSeries || [];
        var ch = window.rdpmsGraphChart, opt = null;
        try { opt = ch && ch.getOption ? ch.getOption() : null; } catch (e) { opt = null; }
        var xa = opt && opt.xAxis ? (opt.xAxis[0] || opt.xAxis) : {};
        var dashed = {};
        if (opt && opt.series) opt.series.forEach(function (s) { if (s.lineStyle && s.lineStyle.type === 'dashed') dashed[s.name] = true; });
        var aid = $('#rdpmsGraphOverlay').data('assetId');
        var xMax = (typeof xa.max === 'number') ? xa.max : Date.now();
        var xMin = (typeof xa.min === 'number') ? xa.min : null;
        var endAnchor = Math.min(xMax, Date.now());
        var out = store.map(function (s) {
            var pts = (s.points || []).map(function (p) { return [Number(p[0]), Number(p[1])]; });
            var actual = pts.slice();
            if (actual.length > 1 && actual[actual.length - 1][0] >= endAnchor - 1000 &&
                actual[actual.length - 1][1] === actual[actual.length - 2][1]) actual.pop();
            var allBin = actual.length > 0 && actual.every(function (p) { return p[1] === 0 || p[1] === 1; });
            var looksAnalog = /\b(m?A|m?V)\b|\((mA|V|mV|Ω)\)/i.test(s.name || '');
            var kind = dashed[s.name] ? 'drv' : (allBin && !looksAnalog) ? 'bin' : (s.isVoltage ? 'v' : (/\bmA\b/i.test(s.name) ? 'ma' : 'other'));
            return {
                key: s.name, name: s.name, sub: '', color: s.color || '#22d3ee', kind: kind, unit: '',
                pts: pts, actual: actual, live: { src: 'ws', aid: aid, name: s.name, dl: kind === 'bin' }
            };
        });
        return finishSeries(out, xMin, xMax);
    }

    function fromIps() {
        var g = window.ipsG;
        if (!g || !g.series) return null;
        var out = [], seen = {};
        (g.order || []).forEach(function (key) {
            if (seen[key]) return; seen[key] = true;
            var s = g.series[key];
            if (!s || g.checked[key] === false || !s.points || !s.points.length) return;
            var label = (typeof window.ipsSeriesLabel === 'function') ? window.ipsSeriesLabel(s) : s.name;
            var pts = s.points.map(function (p) { return Array.isArray(p) ? [Number(p[0]), Number(p[1])] : [Number(p.time), Number(p.value)]; });
            var name = String(s.name || label);
            var kind = s.isBin ? 'bin' : (/^\s*I|\bmA\b|CURRENT/i.test(name) ? 'ma' : (/^\s*V|\bV\b|VOLT/i.test(name) ? 'v' : 'other'));
            out.push({
                key: key, name: label, sub: (label !== s.assetName && (g.assetIds || []).length > 1) ? '' : '',
                color: s.color || '#22d3ee', kind: kind, unit: (kind === 'bin' ? '0/1' : (/\bmA\b/i.test(name) ? 'mA' : '')),
                pts: pts, actual: pts.slice(0, Math.max(1, pts.length - 1)),
                live: { src: 'ips', aid: s.assetId, attrId: s.attrId, members: s.members || s.memberKeys || null }
            });
        });
        return finishSeries(out, g.start || null, g.end || Date.now());
    }

    // ------------------------------------------------------------------
    // live values from the WebSocket store
    // ------------------------------------------------------------------
    function liveIndex(aid) {
        var store = window.wsLiveData || {};
        var a = store[aid] || store[parseInt(aid, 10)];
        var idx = { byName: {}, byId: {}, dl: {} };
        if (!a) return idx;
        if (a.attrs) {
            Object.keys(a.attrs).forEach(function (k) {
                var at = a.attrs[k]; if (!at) return;
                var id = parseInt(at.AttrId || at.AssetAttributeId || 0, 10);
                var v = num(at.Value);
                idx.byName[k] = v;
                if (id) idx.byId[id] = v;
                try {
                    if (typeof window.getAttrDisplayNamePlain === 'function') {
                        var dn = window.getAttrDisplayNamePlain(k, id, aid);
                        if (dn) idx.byName[dn] = v;
                        var dn2 = window.getAttrDisplayNamePlain(k, id);
                        if (dn2) idx.byName[dn2] = v;
                    }
                } catch (e) { /* ignore */ }
            });
        }
        if (a.dlRelays) {
            Object.keys(a.dlRelays).forEach(function (k) {
                var r = a.dlRelays[k]; if (!r) return;
                idx.dl[k] = r.isPickup ? 1 : 0;
                if (r.displayName) idx.dl[r.displayName] = r.isPickup ? 1 : 0;
            });
        }
        return idx;
    }
    function liveValueFor(s, cache) {
        var L = s.live; if (!L) return null;
        var aid = L.aid; if (aid === undefined || aid === null) return null;
        var idx = cache[aid] || (cache[aid] = liveIndex(aid));
        if (L.src === 'ips') { var v = idx.byId[parseInt(L.attrId, 10)]; return v === undefined ? null : v; }
        if (L.dl) { var d = idx.dl[L.name]; return d === undefined ? null : d; }
        var x = idx.byName[L.name];
        return x === undefined ? null : x;
    }

    // ------------------------------------------------------------------
    // styles
    // ------------------------------------------------------------------
    function injectStyles() {
        if (document.getElementById('tlgv-styles')) return;
        var css = [
            '.tlgv-bar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 12px;padding:8px 14px;background:var(--at-g1,rgba(255,255,255,.04));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.10));font-family:Manrope,system-ui,sans-serif;}',
            '.tlgv-seg{display:inline-flex;background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:10px;padding:3px;gap:2px;}',
            '.tlgv-seg button{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:6px;padding:5px 12px;border-radius:7px;font-size:11.5px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));transition:background .18s,color .18s;}',
            '.tlgv-seg button:hover{color:var(--at-t1,#fff);background:var(--at-g2,rgba(255,255,255,.06));}',
            '.tlgv-seg button.on{background:linear-gradient(135deg,var(--at-brand,#22d3ee),var(--at-brand-deep,#0ea5b7));color:#04121c;box-shadow:0 0 14px var(--at-brand-glow,rgba(34,211,238,.45));}',
            '.tlgv-seg button:focus-visible{outline:2px solid var(--at-brand,#22d3ee);outline-offset:1px;}',
            '.tlgv-lbl{font-size:10px;font-weight:800;letter-spacing:.6px;text-transform:uppercase;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-grp{display:inline-flex;align-items:center;gap:6px;}',
            '.tlgv-mini button{padding:4px 9px;font-size:10.5px;}',
            '.tlgv-search{background:rgba(0,0,0,.28);border:1px solid var(--at-edge,rgba(255,255,255,.10));border-radius:8px;color:var(--at-t1,#fff);font-size:11.5px;padding:5px 10px;width:170px;outline:none;}',
            '.tlgv-search:focus{border-color:var(--at-brand,#22d3ee);}',
            '.tlgv-btn{all:unset;cursor:pointer;display:inline-flex;align-items:center;gap:5px;padding:5px 10px;border-radius:8px;font-size:11px;font-weight:700;color:var(--at-t2,rgba(255,255,255,.72));background:var(--at-g1,rgba(255,255,255,.04));border:1px solid var(--at-edge,rgba(255,255,255,.10));}',
            '.tlgv-btn:hover{color:var(--at-brand,#22d3ee);border-color:rgba(34,211,238,.4);}',
            '.tlgv-chk{display:inline-flex;align-items:center;gap:5px;font-size:11px;color:var(--at-t2,rgba(255,255,255,.72));cursor:pointer;margin:0;}',
            '.tlgv-chk input{accent-color:var(--at-brand,#22d3ee);margin:0;}',
            '.tlgv-spacer{flex:1;}',
            '.tlgv-alt-only,.tlgv-stk-only,.tlgv-ind-only{display:none!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-alt-only,.tlgv-bar[data-mode="individual"] .tlgv-alt-only{display:inline-flex!important;}',
            '.tlgv-bar[data-mode="stacked"] .tlgv-stk-only{display:inline-flex!important;}',
            '.tlgv-bar[data-mode="individual"] .tlgv-ind-only{display:inline-flex!important;}',
            '.tlgv-host{position:relative;width:100%;}',
            '.tlgv-empty{padding:48px 16px;text-align:center;color:var(--at-t3,rgba(255,255,255,.5));font-size:13px;}',
            /* stacked lane board */
            '.tlgv-cursor{display:flex;align-items:center;gap:10px;padding:6px 14px;font-size:11px;color:var(--at-t3,rgba(255,255,255,.5));border-bottom:1px solid var(--at-edge,rgba(255,255,255,.08));background:rgba(0,0,0,.18);}',
            '.tlgv-cursor b{font-family:"JetBrains Mono",monospace;color:var(--at-brand,#22d3ee);font-size:12px;}',
            '.tlgv-cursor .tlgv-live-pill{display:inline-flex;align-items:center;gap:5px;padding:1px 8px;border-radius:99px;background:rgba(52,211,153,.12);color:#34d399;font-weight:700;}',
            '.tlgv-stack{position:relative;width:100%;}',
            '.tlgv-stack-chart{position:absolute;inset:0;}',
            '.tlgv-gutter{position:absolute;left:0;top:0;bottom:0;pointer-events:none;}',
            '.tlgv-lane{position:absolute;left:8px;right:8px;display:flex;flex-direction:column;justify-content:center;gap:2px;padding:4px 8px 4px 12px;border-radius:10px;background:linear-gradient(90deg,rgba(255,255,255,.055),rgba(255,255,255,.015));border:1px solid var(--at-edge,rgba(255,255,255,.08));pointer-events:auto;overflow:hidden;box-sizing:border-box;transition:border-color .18s;}',
            '.tlgv-lane:hover{border-color:rgba(34,211,238,.35);}',
            '.tlgv-lane.tlgv-lane-active{border-color:var(--lane-c,#22d3ee);background:linear-gradient(90deg,rgba(34,211,238,.14),rgba(255,255,255,.02));}',
            '.tlgv-lane::before{content:"";position:absolute;left:0;top:0;bottom:0;width:4px;background:var(--lane-c,#22d3ee);box-shadow:0 0 10px var(--lane-c,#22d3ee);}',
            '.tlgv-lane-top{display:flex;align-items:center;gap:6px;min-width:0;}',
            '.tlgv-lane-name{flex:1;min-width:0;font-size:11.5px;font-weight:700;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-unit{flex-shrink:0;font-size:9px;font-weight:800;letter-spacing:.4px;padding:1px 5px;border-radius:4px;background:rgba(255,255,255,.08);color:var(--at-t2,rgba(255,255,255,.72));}',
            '.tlgv-lane-val{display:flex;align-items:baseline;gap:4px;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-lane-val b{font-size:16px;color:var(--lane-c,#22d3ee);font-weight:700;}',
            '.tlgv-lane-val i{font-style:normal;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-lane-sub{font-size:9.5px;color:var(--at-t3,rgba(255,255,255,.5));white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-lane-act{display:inline-flex;gap:2px;opacity:0;transition:opacity .15s;}',
            '.tlgv-lane:hover .tlgv-lane-act,.tlgv-card:hover .tlgv-lane-act,.tlgv-lane-act:focus-within{opacity:1;}',
            '.tlgv-ico{all:unset;cursor:pointer;width:20px;height:20px;display:inline-flex;align-items:center;justify-content:center;border-radius:5px;color:var(--at-t3,rgba(255,255,255,.5));font-size:10px;}',
            '.tlgv-ico:hover{background:rgba(34,211,238,.15);color:var(--at-brand,#22d3ee);}',
            '.tlgv-ico:focus-visible{outline:2px solid var(--at-brand,#22d3ee);}',
            '.tlgv-dens-s .tlgv-lane-sub{display:none;}',
            '.tlgv-dens-s .tlgv-lane{flex-direction:row;align-items:center;justify-content:space-between;}',
            '.tlgv-dens-s .tlgv-lane-val b{font-size:13px;}',
            '.tlgv-badge{display:inline-block;padding:1px 8px;border-radius:5px;font-size:10px;font-weight:800;color:#04121c;font-family:Manrope,sans-serif;}',
            '.tlgv-badge.up{background:#34d399;}.tlgv-badge.dn{background:#fbbf24;}',
            '.tlgv-na{color:var(--at-t4,rgba(255,255,255,.34));}',
            '.tlgv-flash{animation:tlgvFlash 1s ease-out;}',
            '@keyframes tlgvFlash{0%{text-shadow:0 0 10px currentColor;}100%{text-shadow:none;}}',
            /* individual grid */
            '.tlgv-grid{display:grid;gap:12px;padding:12px;grid-template-columns:repeat(auto-fill,minmax(360px,1fr));}',
            '.tlgv-grid.c1{grid-template-columns:1fr;}.tlgv-grid.c2{grid-template-columns:repeat(2,minmax(0,1fr));}.tlgv-grid.c3{grid-template-columns:repeat(3,minmax(0,1fr));}',
            '.tlgv-card{position:relative;display:flex;flex-direction:column;border-radius:14px;background:linear-gradient(180deg,rgba(255,255,255,.06),rgba(255,255,255,.02));border:1px solid var(--at-edge,rgba(255,255,255,.10));overflow:hidden;min-width:0;transition:border-color .18s,box-shadow .18s;}',
            '.tlgv-card:hover{border-color:rgba(34,211,238,.35);box-shadow:0 8px 26px rgba(0,0,0,.35);}',
            '.tlgv-card::before{content:"";position:absolute;left:0;right:0;top:0;height:3px;background:var(--lane-c,#22d3ee);}',
            '.tlgv-card.is-max{grid-column:1/-1;}',
            '.tlgv-card.is-flash{box-shadow:0 0 0 2px var(--at-brand,#22d3ee);}',
            '.tlgv-card-head{display:flex;align-items:center;gap:8px;padding:10px 12px 4px;min-width:0;}',
            '.tlgv-card-dot{width:10px;height:10px;border-radius:50%;background:var(--lane-c,#22d3ee);box-shadow:0 0 8px var(--lane-c,#22d3ee);flex-shrink:0;}',
            '.tlgv-card-title{flex:1;min-width:0;}',
            '.tlgv-card-title b{display:block;font-size:12.5px;color:var(--at-t1,#fff);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}',
            '.tlgv-card-title small{display:block;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-card-live{display:flex;align-items:baseline;gap:4px;font-family:"JetBrains Mono",monospace;}',
            '.tlgv-card-live b{font-size:18px;color:var(--lane-c,#22d3ee);}',
            '.tlgv-card-live i{font-style:normal;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));}',
            '.tlgv-dot-live{width:7px;height:7px;border-radius:50%;background:#34d399;box-shadow:0 0 6px #34d399;animation:tlgvPulse 2s infinite;align-self:center;}',
            '.tlgv-dot-live.off{background:var(--at-t4,rgba(255,255,255,.34));box-shadow:none;animation:none;}',
            '@keyframes tlgvPulse{0%,100%{opacity:1;}50%{opacity:.35;}}',
            '.tlgv-card-stats{display:flex;flex-wrap:wrap;gap:4px 12px;padding:2px 12px 6px;font-size:10px;color:var(--at-t3,rgba(255,255,255,.5));font-family:"JetBrains Mono",monospace;}',
            '.tlgv-card-stats span b{color:var(--at-t1,#fff);font-weight:600;}',
            '.tlgv-card-chart{height:180px;width:100%;}',
            '.tlgv-card.is-max .tlgv-card-chart{height:380px;}',
            '.tlgv-hiddenlist{display:inline-flex;}',
            '@media (max-width:700px){.tlgv-grid,.tlgv-grid.c2,.tlgv-grid.c3{grid-template-columns:1fr;padding:8px;}.tlgv-search{width:120px;}.tlgv-lane-sub{display:none;}.tlgv-lane-val b{font-size:12px;}}',
            '@media (prefers-reduced-motion:reduce){.tlgv-dot-live,.tlgv-flash{animation:none;}}'
        ].join('\n');
        var st = document.createElement('style');
        st.id = 'tlgv-styles';
        st.appendChild(document.createTextNode(css));
        document.head.appendChild(st);
    }

    // ------------------------------------------------------------------
    // context registry
    // ------------------------------------------------------------------
    function makeCtx(id, cfg) {
        var c = TLGV.ctx[id] || { id: id, charts: [], hidden: {}, search: '', model: null, cursor: null };
        c.cfg = cfg;
        c.group = 'tlgv-' + id;
        TLGV.ctx[id] = c;
        return c;
    }
    function getMode(id) {
        var c = TLGV.ctx[id];
        if (c && c.cfg && c.cfg.getMode) return c.cfg.getMode();
        return prefs.mode[id] || 'overlay';
    }
    function setMode(id, mode) {
        var c = TLGV.ctx[id];
        if (c && c.cfg && c.cfg.setMode) c.cfg.setMode(mode);
        else { prefs.mode[id] = mode; savePrefs(); }
    }
    function disposeCharts(c) {
        (c.charts || []).forEach(function (ch) { try { ch.dispose(); } catch (e) { /* ignore */ } });
        c.charts = [];
        if (c.io) { try { c.io.disconnect(); } catch (e) { } c.io = null; }
        if (c.ro) { try { c.ro.disconnect(); } catch (e) { } c.ro = null; }
    }

    function barHtml(id, extraLeft) {
        var m = getMode(id);
        var hiddenN = Object.keys((TLGV.ctx[id] || {}).hidden || {}).length;
        return '<div class="tlgv-bar" data-tlgv="' + id + '" data-mode="' + m + '">' +
            '<span class="tlgv-lbl">View</span>' +
            '<div class="tlgv-seg" role="group" aria-label="Graph view">' +
            '<button type="button" data-tlgv-mode="overlay" class="' + (m === 'overlay' ? 'on' : '') + '" aria-pressed="' + (m === 'overlay') + '" title="All attributes on one chart"><i class="fas fa-layer-group"></i> Overlay</button>' +
            '<button type="button" data-tlgv-mode="stacked" class="' + (m === 'stacked' ? 'on' : '') + '" aria-pressed="' + (m === 'stacked') + '" title="One lane per attribute on a shared time axis"><i class="fas fa-bars-staggered"></i> Stacked</button>' +
            '<button type="button" data-tlgv-mode="individual" class="' + (m === 'individual' ? 'on' : '') + '" aria-pressed="' + (m === 'individual') + '" title="A separate chart for every attribute"><i class="fas fa-table-cells-large"></i> Individual</button>' +
            '</div>' +
            (extraLeft || '') +
            '<span class="tlgv-grp tlgv-stk-only"><span class="tlgv-lbl">Lanes</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Lane height">' +
            ['s', 'm', 'l'].map(function (d) { return '<button type="button" data-tlgv-dens="' + d + '" class="' + (prefs.density === d ? 'on' : '') + '">' + d.toUpperCase() + '</button>'; }).join('') +
            '</div></span>' +
            '<span class="tlgv-grp tlgv-ind-only"><span class="tlgv-lbl">Columns</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Columns">' +
            ['auto', '1', '2', '3'].map(function (d) { return '<button type="button" data-tlgv-cols="' + d + '" class="' + (String(prefs.cols) === d ? 'on' : '') + '">' + (d === 'auto' ? 'Auto' : d) + '</button>'; }).join('') +
            '</div></span>' +
            '<span class="tlgv-spacer"></span>' +
            '<span class="tlgv-grp tlgv-alt-only"><input type="search" class="tlgv-search" data-tlgv-search placeholder="Filter attributes…" aria-label="Filter attributes" value="' + esc((TLGV.ctx[id] || {}).search || '') + '"/></span>' +
            '<label class="tlgv-chk tlgv-alt-only" title="Zoom / pan all charts together (hover stays on one attribute)"><input type="checkbox" data-tlgv-sync ' + (prefs.sync ? 'checked' : '') + '/> Sync zoom</label>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-unhide style="' + (hiddenN ? '' : 'display:none!important') + '"><i class="fas fa-eye"></i> Show hidden (<span data-tlgv-hn>' + hiddenN + '</span>)</button>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-reset><i class="fas fa-undo"></i> Reset zoom</button>' +
            '<button type="button" class="tlgv-btn tlgv-alt-only" data-tlgv-png><i class="fas fa-download"></i> PNG</button>' +
            '</div>';
    }
    function syncBar(id) {
        var m = getMode(id);
        var $bar = $('.tlgv-bar[data-tlgv="' + id + '"]');
        $bar.attr('data-mode', m);
        $bar.find('[data-tlgv-mode]').each(function () {
            var on = $(this).attr('data-tlgv-mode') === m;
            $(this).toggleClass('on', on).attr('aria-pressed', on ? 'true' : 'false');
        });
        var hn = Object.keys((TLGV.ctx[id] || {}).hidden || {}).length;
        $bar.find('[data-tlgv-hn]').text(hn);
        $bar.find('[data-tlgv-unhide]').attr('style', hn ? '' : 'display:none!important');
    }

    // ------------------------------------------------------------------
    // apply: decide overlay vs alternative view for a context
    // ------------------------------------------------------------------
    function apply(id, rebuildModel) {
        var c = TLGV.ctx[id]; if (!c || !c.cfg) return;
        var host = c.cfg.host(), overlay = c.cfg.overlay();
        if (!host || !host.length) return;
        syncBar(id);
        var m = getMode(id);
        if (m === 'overlay') {
            disposeCharts(c);
            host.empty().hide();
            if (c.cfg.showOverlay) c.cfg.showOverlay(true);
            else if (overlay) overlay.show();
            if (c.cfg.overlayChart) { var oc = c.cfg.overlayChart(); if (oc) setTimeout(function () { try { oc.resize(); } catch (e) { } }, 30); }
            return;
        }
        if (!window.echarts) return;
        if (rebuildModel || !c.model) c.model = c.cfg.model();
        if (!c.model) { host.hide(); return; }
        if (c.cfg.showOverlay) c.cfg.showOverlay(false);
        else if (overlay) overlay.hide();
        host.show();
        if (m === 'stacked') renderStacked(c, host);
        else renderIndividual(c, host);
        ensureLiveTimer();
    }
    TLGV.apply = apply;

    function visibleSeries(c) {
        var q = String(c.search || '').trim().toLowerCase();
        return c.model.series.filter(function (s) {
            if (c.hidden[s.key]) return false;
            if (q && String(s.name).toLowerCase().indexOf(q) < 0) return false;
            return true;
        });
    }

    function tooltipBase() {
        return {
            backgroundColor: 'rgba(5,9,24,0.97)', borderColor: 'rgba(255,255,255,0.12)', borderWidth: 1,
            padding: [8, 12], textStyle: { color: 'rgba(255,255,255,0.9)', fontSize: 12 },
            extraCssText: 'box-shadow:0 8px 32px rgba(0,0,0,.5);border-radius:10px;'
        };
    }
    function seriesFor(s, xi, yi, end, dens) {
        var isBin = s.kind === 'bin';
        var o = {
            name: s.name, type: 'line', step: 'end', xAxisIndex: xi, yAxisIndex: yi,
            showSymbol: !isBin && s.actual.length <= 40, symbol: 'circle', symbolSize: 4,
            connectNulls: true, animation: false,
            lineStyle: { width: isBin ? 1.4 : 1.8, color: s.color, type: s.kind === 'drv' ? 'dashed' : 'solid' },
            itemStyle: { color: s.color },
            areaStyle: {
                color: {
                    type: 'linear', x: 0, y: 0, x2: 0, y2: 1,
                    colorStops: [{ offset: 0, color: hexA(s.color, isBin ? 0.30 : 0.32) }, { offset: 1, color: hexA(s.color, 0.02) }]
                }
            },
            emphasis: { disabled: true },
            data: s.pts
        };
        if (isBin) {
            o.markArea = {
                silent: true,
                itemStyle: { color: 'rgba(52,211,153,0.10)' },
                data: pickupBands(s.pts, end).map(function (b) { return [{ xAxis: b[0] }, { xAxis: b[1] }]; })
            };
        } else if (s.stats && s.stats.avg !== null && dens !== 's') {
            o.markLine = {
                silent: true, symbol: 'none', animation: false,
                lineStyle: { color: s.color, type: 'dotted', width: 1, opacity: 0.6 },
                label: { show: false },
                data: [{ yAxis: Number(zf(s.stats.avg)) }]
            };
        }
        return o;
    }
    function statsLine(s) {
        var st = s.stats || {};
        if (s.kind === 'bin') {
            return '↑ ' + st.pickups + ' pickups · ↓ ' + st.drops + ' drops' + (st.onPct !== null ? ' · ' + st.onPct.toFixed(0) + '% up' : '');
        }
        if (st.min === null) return 'no samples';
        return 'min ' + fmtVal(st.min) + ' · max ' + fmtVal(st.max) + ' · avg ' + fmtVal(st.avg);
    }

    // ------------------------------------------------------------------
    // STACKED — lane board
    // ------------------------------------------------------------------
    var DENS = { s: 46, m: 78, l: 124 };
    function renderStacked(c, host) {
        disposeCharts(c);
        var list = visibleSeries(c);
        var model = c.model;
        if (!list.length) {
            host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden lanes.</div>');
            return;
        }
        var narrow = (host.width() || 900) < 640;
        var GW = narrow ? 132 : 210;
        var laneH = DENS[prefs.density] || DENS.m, gap = 12, top = 10, bottom = 64;
        var H = top + list.length * laneH + (list.length - 1) * gap + bottom;
        var span = (model.xMax || Date.now()) - (model.xMin || (list[0].pts[0] || [Date.now()])[0]);

        var lanes = '';
        list.forEach(function (s, i) {
            var y = top + i * (laneH + gap);
            lanes += '<div class="tlgv-lane" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + ';top:' + y + 'px;height:' + laneH + 'px;width:' + (GW - 16) + 'px;" title="' + esc(s.name) + ' — ' + kindLabel(s.kind) + '">' +
                '<div class="tlgv-lane-top"><span class="tlgv-lane-name">' + esc(s.name) + '</span>' +
                (s.unit ? '<span class="tlgv-unit">' + esc(s.unit) + '</span>' : '') +
                '<span class="tlgv-lane-act">' +
                '<button type="button" class="tlgv-ico" data-tlgv-focus="' + esc(s.key) + '" title="Open as individual chart" aria-label="Open ' + esc(s.name) + ' as individual chart"><i class="fas fa-up-right-and-down-left-from-center"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide lane" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                '</span></div>' +
                '<div class="tlgv-lane-val" data-tlgv-val>' + valHtml(s.stats.last, s.kind, s.unit) + '</div>' +
                '<div class="tlgv-lane-sub">' + esc(statsLine(s)) + '</div>' +
                '</div>';
        });
        host.html(
            '<div class="tlgv-cursor" data-tlgv-cursor><i class="fas fa-crosshairs"></i> <span data-tlgv-cursor-txt><span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span></span>' +
            '<span class="tlgv-spacer"></span><span>' + list.length + ' lane' + (list.length > 1 ? 's' : '') + ' · hover a lane to read that lane \u00b7 drag to pan \u00b7 Ctrl+wheel to zoom</span></div>' +
            '<div class="tlgv-stack tlgv-dens-' + prefs.density + '" style="height:' + H + 'px"><div class="tlgv-stack-chart"></div><div class="tlgv-gutter" style="width:' + GW + 'px">' + lanes + '</div></div>'
        );
        var el = host.find('.tlgv-stack-chart')[0];
        var chart = window.echarts.init(el, null, { renderer: 'canvas' });
        c.charts.push(chart);

        var grids = [], xs = [], ys = [], series = [], titles = [], allX = [];
        var left = GW + 44, right = 18;
        list.forEach(function (s, i) {
            var y = top + i * (laneH + gap), last = i === list.length - 1, isBin = s.kind === 'bin';
            grids.push({
                left: left, right: right, top: y, height: laneH, show: true,
                backgroundColor: i % 2 ? 'rgba(255,255,255,0.018)' : 'rgba(255,255,255,0.036)',
                borderColor: 'rgba(255,255,255,0.06)', borderWidth: 1
            });
            xs.push({
                type: 'time', gridIndex: i, min: model.xMin, max: model.xMax, boundaryGap: false,
                axisLine: { show: last, lineStyle: { color: 'rgba(255,255,255,0.18)' } },
                axisTick: { show: last }, splitNumber: narrow ? 4 : 8,
                axisLabel: { show: last, color: 'rgba(255,255,255,0.5)', fontSize: 10, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } },
                splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.045)' } },
                // pointer is drawn only in the lane under the mouse (no cross-lane link)
                axisPointer: { label: { show: false } }   // time + lane name are shown in the cursor header
            });
            var yMax = isBin ? 1 : niceMax(s.stats.max);
            ys.push({
                type: 'value', gridIndex: i, min: 0, max: yMax, splitNumber: prefs.density === 'l' ? 3 : 1,
                axisLine: { show: false }, axisTick: { show: false },
                axisLabel: {
                    color: hexA(s.color, 0.85), fontSize: 9, fontFamily: 'JetBrains Mono,monospace', margin: 6,
                    showMinLabel: false,   // "0" of one lane would collide with the max label of the next
                    formatter: isBin ? function (v) { return v === 1 ? 'P' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); }
                },
                splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.05)', type: 'dashed' } },
                axisPointer: { show: false }
            });
            series.push(seriesFor(s, i, i, model.xMax, prefs.density));
            titles.push({ show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: y + 4, textStyle: { color: s.color, fontSize: 11, fontWeight: 700 } });
            allX.push(i);
        });
        chart.setOption({
            backgroundColor: 'transparent', animation: false,
            title: titles,
            grid: grids, xAxis: xs, yAxis: ys, series: series,
            tooltip: { trigger: 'axis', showContent: false, axisPointer: { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.75)', width: 1, type: 'dashed' } } },
            axisPointer: { link: [], snap: false },
            dataZoom: [
                { type: 'inside', xAxisIndex: allX, filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseWheel: false, moveOnMouseMove: true, minValueSpan: 60000 },
                {
                    type: 'slider', xAxisIndex: allX, filterMode: 'none', height: 20, bottom: 6, left: left, right: right, minValueSpan: 60000,
                    backgroundColor: 'rgba(255,255,255,0.03)', borderColor: 'rgba(255,255,255,0.09)', fillerColor: 'rgba(34,211,238,0.12)',
                    handleStyle: { color: '#22d3ee', borderColor: '#22d3ee' }, moveHandleStyle: { color: 'rgba(34,211,238,.5)' },
                    textStyle: { color: 'rgba(255,255,255,0.45)', fontSize: 10 }, labelFormatter: function (v) { return fmtFull(v); },
                    dataBackground: { lineStyle: { color: 'rgba(255,255,255,0.15)' }, areaStyle: { color: 'rgba(255,255,255,0.04)' } }
                }
            ]
        }, true);

        var readouts = {}, $lanes = {};
        host.find('.tlgv-lane').each(function () {
            var k = $(this).attr('data-key');
            $lanes[k] = $(this);
            readouts[k] = $(this).find('[data-tlgv-val]');
        });
        var $cur = host.find('[data-tlgv-cursor-txt]');
        var raf = 0, pending = null;
        c.cursor = null; c.cursorKey = null;
        // Only the hovered lane follows the cursor; every other lane keeps its latest/live value.
        function paint(p) {
            var prevKey = c.cursorKey;
            if (!p) {
                c.cursor = null; c.cursorKey = null;
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                $cur.html('<span class="tlgv-live-pill"><span class="tlgv-dot-live"></span> Latest</span>');
                updateLive(c, true);
                return;
            }
            var s = list[p.idx]; if (!s) return;
            c.cursor = p.t; c.cursorKey = s.key;
            if (prevKey !== s.key) {
                host.find('.tlgv-lane-active').removeClass('tlgv-lane-active');
                if ($lanes[s.key]) $lanes[s.key].addClass('tlgv-lane-active');
                if (prevKey !== null) updateLive(c, true);   // restore the lane we just left
            }
            $cur.html('<b>' + fmtFull(p.t) + '</b> <span style="color:' + esc(s.color) + ';font-weight:700;margin-left:6px;">' + esc(s.name) + '</span>');
            if (readouts[s.key]) readouts[s.key].html(valHtml(valueAt(s.pts, p.t), s.kind, s.unit));
        }
        function schedule(p) {
            pending = p;
            if (!raf) raf = requestAnimationFrame(function () { raf = 0; paint(pending); });
        }
        chart.on('updateAxisPointer', function (e) {
            var info = (e && e.axesInfo) || [];
            var hit = null;
            for (var i = 0; i < info.length; i++) {
                if (info[i].axisDim === 'x') { hit = info[i]; break; }
            }
            if (!hit) { schedule(null); return; }
            var t = Number(hit.value);
            if (isNaN(t)) return;
            schedule({ t: t, idx: hit.axisIndex });
        });
        chart.getZr().on('globalout', function () { schedule(null); });
        c.exportPng = function () {
            chart.setOption({ title: titles.map(function (t) { return $.extend({}, t, { show: true }); }) });
            var url = chart.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            chart.setOption({ title: titles });
            download(url, 'telemetry_stacked.png');
        };
        c.resetZoom = function () { chart.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); };
        observeResize(c, host[0]);
        if (c.cfg.onRendered) c.cfg.onRendered(chart);
    }

    // ------------------------------------------------------------------
    // INDIVIDUAL — one chart card per attribute
    // ------------------------------------------------------------------
    function renderIndividual(c, host) {
        disposeCharts(c);
        var list = visibleSeries(c);
        var model = c.model;
        if (!list.length) {
            host.html('<div class="tlgv-empty"><i class="fas fa-filter"></i> No attributes match. Clear the filter or show hidden charts.</div>');
            return;
        }
        var span = (model.xMax || Date.now()) - (model.xMin || list[0].pts[0][0]);
        var cols = prefs.cols === 'auto' ? '' : ' c' + prefs.cols;
        var h = '<div class="tlgv-grid' + cols + '">';
        list.forEach(function (s) {
            var st = s.stats || {};
            var stats = s.kind === 'bin'
                ? '<span>Pickups <b>' + st.pickups + '</b></span><span>Drops <b>' + st.drops + '</b></span><span>Up <b>' + (st.onPct !== null ? st.onPct.toFixed(0) + '%' : '—') + '</b></span><span>Events <b>' + st.count + '</b></span>'
                : '<span>Min <b>' + fmtVal(st.min) + '</b></span><span>Max <b>' + fmtVal(st.max) + '</b></span><span>Avg <b>' + fmtVal(st.avg) + '</b></span><span>Samples <b>' + st.count + '</b></span>';
            h += '<div class="tlgv-card" data-key="' + esc(s.key) + '" style="--lane-c:' + esc(s.color) + '">' +
                '<div class="tlgv-card-head"><span class="tlgv-card-dot"></span>' +
                '<div class="tlgv-card-title"><b title="' + esc(s.name) + '">' + esc(s.name) + '</b><small>' + kindLabel(s.kind) + (s.unit ? ' · ' + esc(s.unit) : '') + '</small></div>' +
                '<div class="tlgv-card-live" title="Live value (WebSocket) or last sample"><span class="tlgv-dot-live off" data-tlgv-dot></span><span data-tlgv-val>' + valHtml(st.last, s.kind, s.unit) + '</span></div>' +
                '<span class="tlgv-lane-act">' +
                '<button type="button" class="tlgv-ico" data-tlgv-max title="Expand / collapse" aria-label="Expand ' + esc(s.name) + '"><i class="fas fa-expand"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-cardpng title="Save PNG" aria-label="Save ' + esc(s.name) + ' as PNG"><i class="fas fa-download"></i></button>' +
                '<button type="button" class="tlgv-ico" data-tlgv-hide="' + esc(s.key) + '" title="Hide chart" aria-label="Hide ' + esc(s.name) + '"><i class="fas fa-eye-slash"></i></button>' +
                '</span></div>' +
                '<div class="tlgv-card-stats">' + stats + '</div>' +
                '<div class="tlgv-card-chart" role="img" aria-label="' + esc(s.name) + ' history chart"></div></div>';
        });
        h += '</div>';
        host.html(h);

        var byKey = {};
        list.forEach(function (s) { byKey[s.key] = s; });
        c.cardCharts = {};

        function initCard(card) {
            if (card.__tlgvInit) return;
            card.__tlgvInit = true;
            var s = byKey[card.getAttribute('data-key')]; if (!s) return;
            var el = card.querySelector('.tlgv-card-chart');
            var ch = window.echarts.init(el, null, { renderer: 'canvas' });
            var isBin = s.kind === 'bin';
            var tt = tooltipBase();
            tt.trigger = 'axis';
            tt.axisPointer = { type: 'line', snap: false, lineStyle: { color: 'rgba(34,211,238,0.7)', type: 'dashed' } };
            tt.formatter = function (params) {
                if (!params || !params.length) return '';
                var t = params[0].axisValue != null ? Number(params[0].axisValue) : Number(params[0].value[0]);
                var v = valueAt(s.pts, t);
                return '<div style="font-family:JetBrains Mono,monospace;color:#22d3ee;font-weight:700;margin-bottom:4px;">' + fmtFull(t) + '</div>' +
                    '<div style="display:flex;align-items:center;gap:8px;"><span style="width:9px;height:9px;border-radius:50%;background:' + s.color + '"></span>' +
                    '<span style="color:rgba(255,255,255,.75)">' + esc(s.name) + '</span><b style="margin-left:10px;color:#fff;">' + fmtVal(v, s.kind) + (isBin || v === null ? '' : ' ' + esc(s.unit)) + '</b></div>';
            };
            ch.setOption({
                backgroundColor: 'transparent', animation: false,
                title: { show: false, text: s.name + (s.unit ? ' (' + s.unit + ')' : ''), left: 8, top: 2, textStyle: { color: s.color, fontSize: 12 } },
                grid: { left: 46, right: 14, top: 12, bottom: 28 },
                tooltip: tt,
                xAxis: {
                    type: 'time', min: model.xMin, max: model.xMax, splitNumber: 4,
                    axisLabel: { color: 'rgba(255,255,255,0.45)', fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace', hideOverlap: true, formatter: function (v) { return fmtAxis(v, span); } },
                    axisLine: { lineStyle: { color: 'rgba(255,255,255,0.15)' } },
                    splitLine: { show: true, lineStyle: { color: 'rgba(255,255,255,0.045)' } }
                },
                yAxis: {
                    type: 'value', min: 0, max: isBin ? 1 : niceMax(s.stats.max), splitNumber: isBin ? 1 : 3,
                    axisLabel: {
                        color: 'rgba(255,255,255,0.45)', fontSize: 9.5, fontFamily: 'JetBrains Mono,monospace',
                        formatter: isBin ? function (v) { return v === 1 ? 'Pickup' : v === 0 ? 'Drop' : ''; } : function (v) { return v >= 1000 ? (v / 1000).toFixed(1) + 'k' : String(+v.toFixed(2)); }
                    },
                    splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)', type: 'dashed' } }
                },
                dataZoom: [{ type: 'inside', filterMode: 'none', zoomOnMouseWheel: 'ctrl', moveOnMouseMove: true, minValueSpan: 60000 }],
                series: [seriesFor(s, 0, 0, model.xMax, 'm')]
            }, true);
            // Hover/tooltip stays inside this card. "Sync zoom" only mirrors zoom/pan to the other cards.
            ch.on('datazoom', function () {
                if (!prefs.sync || c.zoomSyncing) return;
                var dz = (ch.getOption().dataZoom || [])[0];
                if (!dz) return;
                c.zoomSyncing = true;
                try {
                    c.charts.forEach(function (o) {
                        if (o !== ch && !o.isDisposed()) o.dispatchAction({ type: 'dataZoom', start: dz.start, end: dz.end });
                    });
                } finally { c.zoomSyncing = false; }
            });
            if (prefs.sync && c.charts.length) {
                var dz0 = (c.charts[0].getOption().dataZoom || [])[0];
                if (dz0 && (dz0.start > 0 || dz0.end < 100)) ch.dispatchAction({ type: 'dataZoom', start: dz0.start, end: dz0.end });
            }
            c.charts.push(ch);
            c.cardCharts[s.key] = ch;
        }

        var cards = host.find('.tlgv-card').toArray();
        if ('IntersectionObserver' in window && cards.length > 8) {
            c.io = new IntersectionObserver(function (entries) {
                entries.forEach(function (en) { if (en.isIntersecting) { initCard(en.target); c.io && c.io.unobserve(en.target); } });
            }, { rootMargin: '200px' });
            cards.forEach(function (cd, i) { if (i < 6) initCard(cd); else c.io.observe(cd); });
        } else {
            cards.forEach(initCard);
        }

        c.exportPng = function () {
            // make sure every card is drawn, then compose one image
            cards.forEach(initCard);
            var first = c.charts[0]; if (!first) return;
            c.charts.forEach(function (ch) { ch.group = c.group; ch.setOption({ title: { show: true }, grid: { top: 26 } }); });
            window.echarts.connect(c.group);
            var url = first.getConnectedDataURL ? first.getConnectedDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' }) : first.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            c.charts.forEach(function (ch) { ch.setOption({ title: { show: false }, grid: { top: 12 } }); ch.group = ''; });
            window.echarts.disconnect(c.group);   // connected only while composing the image, never for hover
            download(url, 'telemetry_individual.png');
        };
        c.resetZoom = function () { c.charts.forEach(function (ch) { ch.dispatchAction({ type: 'dataZoom', start: 0, end: 100 }); }); };
        observeResize(c, host[0]);
        updateLive(c, true);
    }

    function download(url, name) {
        var a = document.createElement('a'); a.href = url; a.download = name;
        document.body.appendChild(a); a.click(); document.body.removeChild(a);
    }
    function observeResize(c, el) {
        if (!('ResizeObserver' in window)) return;
        var t = 0;
        c.ro = new ResizeObserver(function () {
            clearTimeout(t);
            t = setTimeout(function () { (c.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); }, 60);
        });
        c.ro.observe(el);
    }

    // ------------------------------------------------------------------
    // live refresh (values only, 2 s)
    // ------------------------------------------------------------------
    function updateLive(c, force) {
        if (!c || !c.model || !c.cfg) return;
        var host = c.cfg.host(); if (!host || !host.is(':visible')) return;
        var cache = {};
        var m = getMode(c.id);
        c.model.series.forEach(function (s) {
            var lv = liveValueFor(s, cache);
            var isLive = lv !== null && lv !== undefined;
            var v = isLive ? lv : s.stats.last;
            var sel = '[data-key="' + (window.CSS && CSS.escape ? CSS.escape(s.key) : s.key.replace(/"/g, '\\"')) + '"]';
            var $item = host.find((m === 'stacked' ? '.tlgv-lane' : '.tlgv-card') + sel);
            if (!$item.length) return;
            if (m === 'stacked' && c.cursorKey && c.cursorKey === s.key) return;   // hovered lane shows the cursor value
            var $val = $item.find('[data-tlgv-val]');
            var html = valHtml(v, s.kind, s.unit);
            if ($val.html() !== html) {
                $val.html(html);
                if (!force) { $val.removeClass('tlgv-flash'); void $val[0].offsetWidth; $val.addClass('tlgv-flash'); }
            }
            $item.find('[data-tlgv-dot]').toggleClass('off', !isLive);
        });
    }
    var liveTimer = null;
    function ensureLiveTimer() {
        if (liveTimer) return;
        liveTimer = setInterval(function () {
            var any = false;
            Object.keys(TLGV.ctx).forEach(function (id) {
                var c = TLGV.ctx[id];
                if (!c.cfg) return;
                var host = c.cfg.host();
                if (!host || !host.length || !document.body.contains(host[0])) return;
                any = true;
                if (getMode(id) !== 'overlay') updateLive(c, false);
            });
            if (window._gA && document.getElementById('gWrap')) { any = true; updateGraphPanelValues(String(window._gA)); }
            if (!any) { clearInterval(liveTimer); liveTimer = null; }
        }, 2000);
    }
    TLGV.updateLive = function (id) { if (TLGV.ctx[id]) updateLive(TLGV.ctx[id], true); };

    // v616 _gUpdatePanelValuesOnlyFromWebSocket — values only, zero floored
    function updateGraphPanelValues(aid) {
        if (!aid || String(window._gA || '') !== aid || !document.getElementById('gWrap')) return;
        var idx = liveIndex(aid);
        $('#gFL .gAttrRow2, #gRL .gAttrRow2').each(function () {
            var key = $(this).find('input[type=checkbox]').data('key');
            if (key === undefined) return;
            var v = idx.byName[key];
            if (v === undefined || v === null) return;
            var txt = Number(zf(v)).toFixed(2);
            var $v = $(this).find('.gAttrVal2');
            if ($v.text() !== txt) { $v.text(txt); $v.removeClass('tlgv-flash'); void $v[0].offsetWidth; $v.addClass('tlgv-flash'); }
        });
        $('#gDL .gAttrRow2').each(function () {
            var key = String($(this).find('input[type=checkbox]').data('key') || '');
            if (key.indexOf('DL:') !== 0) return;
            var st = idx.dl[key.substring(3)];
            if (st === undefined) return;
            var $b = $(this).find('.gDlBadge2');
            var want = st ? 'pickup' : 'drop';
            if (!$b.hasClass(want)) $b.removeClass('pickup drop').addClass(want).text(st ? 'Pickup' : 'Drop');
        });
        if (window._gIsTrack && typeof window.calculateDerivedValues === 'function') {
            var store = window.wsLiveData || {};
            var a = store[aid];
            if (a && a.attrs && Object.keys(a.attrs).length) {
                var dv = {};
                try { dv = window.calculateDerivedValues(a.attrs) || {}; } catch (e) { dv = {}; }
                $('#gDerived .gDerivedRow').each(function () {
                    var key = String($(this).find('input[type=checkbox]').data('key') || '').replace(/^DRV:/, '');
                    var v = dv[key];
                    if (v === undefined || v === null || isNaN(v)) return;
                    var txt = (typeof window.formatDerivedValue === 'function') ? window.formatDerivedValue(v) : Number(zf(v)).toFixed(2);
                    var $v = $(this).find('.gAttrVal2');
                    if ($v.text() !== txt) $v.text(txt);
                });
            }
        }
    }
    TLGV.updateGraphPanelValues = updateGraphPanelValues;
    window._gUpdatePanelValuesOnlyFromWebSocket = window._gUpdatePanelValuesOnlyFromWebSocket || updateGraphPanelValues;

    // ------------------------------------------------------------------
    // delegated UI events
    // ------------------------------------------------------------------
    function ctxOf(el) {
        var $b = $(el).closest('.tlgv-bar');
        if ($b.length) return $b.attr('data-tlgv');
        var $h = $(el).closest('[data-tlgv-host]');
        return $h.length ? $h.attr('data-tlgv-host') : null;
    }
    // Bound on each bar / host element (NOT on document): the modal shell calls
    // event.stopPropagation(), which would swallow document-level delegation.
    function bindUi(root) {
      $(root)
        .off('.tlgv')
        .on('click.tlgv', '[data-tlgv-mode]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id) return;
            var mode = $(this).attr('data-tlgv-mode');
            if (getMode(id) === mode) return;
            setMode(id, mode);
            apply(id, true);
        })
        .on('click.tlgv', '[data-tlgv-dens]', function (e) {
            e.preventDefault(); e.stopPropagation();
            prefs.density = $(this).attr('data-tlgv-dens'); savePrefs();
            $('[data-tlgv-dens]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-dens') === prefs.density); });
            var id = ctxOf(this); if (id) apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-cols]', function (e) {
            e.preventDefault(); e.stopPropagation();
            prefs.cols = $(this).attr('data-tlgv-cols'); savePrefs();
            $('[data-tlgv-cols]').each(function () { $(this).toggleClass('on', $(this).attr('data-tlgv-cols') === String(prefs.cols)); });
            var id = ctxOf(this); if (id) apply(id, false);
        })
        .on('input.tlgv', '[data-tlgv-search]', function (e) {
            e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            var el = this;
            clearTimeout(el.__t);
            el.__t = setTimeout(function () { TLGV.ctx[id].search = el.value; apply(id, false); }, 220);
        })
        .on('change.tlgv', '[data-tlgv-sync]', function (e) {
            e.stopPropagation();
            prefs.sync = this.checked; savePrefs();
            $('[data-tlgv-sync]').prop('checked', prefs.sync);
            var id = ctxOf(this); if (!id) return;
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-hide]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            TLGV.ctx[id].hidden[$(this).attr('data-tlgv-hide')] = true;
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-unhide]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id || !TLGV.ctx[id]) return;
            TLGV.ctx[id].hidden = {};
            apply(id, false);
        })
        .on('click.tlgv', '[data-tlgv-reset]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var c = TLGV.ctx[ctxOf(this)]; if (c && c.resetZoom) c.resetZoom();
        })
        .on('click.tlgv', '[data-tlgv-png]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var c = TLGV.ctx[ctxOf(this)]; if (c && c.exportPng) c.exportPng();
        })
        .on('click.tlgv', '[data-tlgv-focus]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var id = ctxOf(this); if (!id) return;
            var key = $(this).attr('data-tlgv-focus');
            setMode(id, 'individual');
            apply(id, false);
            var c = TLGV.ctx[id];
            var $card = c.cfg.host().find('.tlgv-card').filter(function () { return $(this).attr('data-key') === key; });
            if ($card.length) {
                $card.addClass('is-max is-flash');
                if ($card[0].scrollIntoView) $card[0].scrollIntoView({ block: 'center', behavior: 'smooth' });
                setTimeout(function () { $card.removeClass('is-flash'); var ch = c.cardCharts && c.cardCharts[key]; if (ch) ch.resize(); }, 900);
            }
        })
        .on('click.tlgv', '[data-tlgv-max]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card');
            $card.toggleClass('is-max');
            $(this).find('i').toggleClass('fa-expand fa-compress');
            var c = TLGV.ctx[ctxOf(this)];
            var ch = c && c.cardCharts && c.cardCharts[$card.attr('data-key')];
            if (ch) setTimeout(function () { ch.resize(); }, 30);
        })
        .on('click.tlgv', '[data-tlgv-cardpng]', function (e) {
            e.preventDefault(); e.stopPropagation();
            var $card = $(this).closest('.tlgv-card');
            var c = TLGV.ctx[ctxOf(this)];
            var key = $card.attr('data-key');
            var ch = c && c.cardCharts && c.cardCharts[key];
            if (!ch) return;
            ch.setOption({ title: { show: true }, grid: { top: 26 } });
            var url = ch.getDataURL({ type: 'png', pixelRatio: 2, backgroundColor: '#0e1530' });
            ch.setOption({ title: { show: false }, grid: { top: 12 } });
            download(url, String(key).replace(/[^\w.-]+/g, '_') + '.png');
        });
    }
    TLGV.bindUi = bindUi;
    $(window).off('resize.tlgv').on('resize.tlgv', function () {
        Object.keys(TLGV.ctx).forEach(function (id) { (TLGV.ctx[id].charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); });
    });

    // ==================================================================
    // SURFACE 1 — Graph view (fnBindGrph / _gLoad / _gRender)
    // ==================================================================
    makeCtx('grph', {
        host: function () { return $('#gAlt'); },
        overlay: function () { return $('#gCH'); },
        overlayChart: function () { return window.mainGraphChart; },
        showOverlay: function (on) {
            if (on) { if (window._gD) { $('#gCH').show(); $('#gToolbar').show(); } }
            else { $('#gCH').hide(); $('#gToolbar').hide(); }
        },
        model: fromGraphView
    });

    function gRangeHtml() {
        return '<span class="tlgv-grp" id="gRangeBox" data-tlgv-host="grph">' +
            '<span class="tlgv-lbl">From</span><input type="datetime-local" id="gFrom" class="tlgv-search" style="width:auto;color-scheme:dark;" aria-label="From date and time"/>' +
            '<span class="tlgv-lbl">To</span><input type="datetime-local" id="gTo" class="tlgv-search" style="width:auto;color-scheme:dark;" aria-label="To date and time"/>' +
            '<button type="button" class="tlgv-btn" id="gApply"><i class="fas fa-search"></i> Load</button></span>';
    }
    function gSyncDateInputs(startMs, endMs) {
        if (startMs != null) $('#gFrom').val(toLocalInput(startMs));
        if (endMs != null) $('#gTo').val(toLocalInput(Math.min(endMs, Date.now())));
    }
    function gRefreshMax() { var n = toLocalInput(Date.now()); $('#gFrom,#gTo').attr('max', n); }
    function gClampFuture(sel) {
        var el = $(sel), v = el.val(); if (!v) return;
        var d = new Date(v);
        if (!isNaN(d.getTime()) && d.getTime() > Date.now()) { el.val(toLocalInput(Date.now())); warn('Future dates are not allowed.'); }
    }
    window._gToLocalInput = window._gToLocalInput || toLocalInput;
    window._gSyncDateInputs = window._gSyncDateInputs || gSyncDateInputs;

    // v616 _gLoadWindow: explicit [start,end] loader (same request/response handling as v617 _gLoad)
    var gLoadSeq = 0;
    function gLoadWindow(aid, startMs, endMs) {
        var seq = ++gLoadSeq;
        endMs = Math.min(endMs, Date.now());
        window._gReqStart = startMs; window._gReqEnd = endMs;
        $('#gLD').show(); $('#gCH').hide(); $('#gEM').hide(); $('#gToolbar').hide(); $('#gAlt').hide();
        var s = new Date(startMs), e = new Date(endMs);
        var f = function (d) { return pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + ' ' + pad(d.getHours()) + ':' + pad(d.getMinutes()); };
        var spanH = Math.max(1, Math.round((endMs - startMs) / 3600000));
        $('#gTR').text(f(s) + ' — ' + f(e) + ' (' + spanH + 'h)');
        gSyncDateInputs(startMs, endMs);
        $('#gTB .gtb2').removeClass('active2');
        $.ajax({
            url: window.HISTORY_API_BASE + '?assetId=' + aid + '&startDate=' + window.formatDateForHistoryApi(s) + '&endDate=' + window.formatDateForHistoryApi(e),
            type: 'GET', dataType: 'json', timeout: 60000,
            success: function (r) {
                if (seq !== gLoadSeq || String(window._gA) !== String(aid)) return;
                $('#gLD').hide();
                if (r && r.Data && r.Data.length > 0) {
                    window._gD = r.Data; $('#gCH').show();
                    window._gPop(aid, r.Data);
                    window._gRender(r.Data, aid, window._gF || 'all');
                    updateGraphPanelValues(String(aid));
                    setTimeout(function () {
                        if (String(window._gA) === String(aid)) { window._gPop(aid, window._gD); updateGraphPanelValues(String(aid)); }
                    }, 1500);
                } else {
                    window._gD = null;
                    $('#gEM').show(); $('#gPT').text('0'); $('#gAlt').hide();
                }
            },
            error: function (xhr, status) {
                if (status === 'abort' || seq !== gLoadSeq) return;
                $('#gLD').hide();
                $('#gEM').html('<i class="fas fa-exclamation-triangle" style="color:#f87171;font-size:28px;display:block;margin-bottom:10px;"></i><p style="color:#f87171;font-size:13px;">Failed to load data. Please try again.</p>').show();
            }
        });
    }
    window._gLoadWindow = gLoadWindow;
    window._gLoadRange = function (aid, s, e) { gLoadWindow(aid, s, e); };

    if (typeof window.fnBindGrph === 'function') {
        var _origBindGrph = window.fnBindGrph;
        window.fnBindGrph = function () {
            var r = _origBindGrph.apply(this, arguments);
            try { decorateGraphView(); } catch (e) { if (window.console) console.error('[TLGV] Graph view decorate failed', e); }
            return r;
        };
    }
    function decorateGraphView() {
        if (!$('#gWrap').length || $('#gWrap .tlgv-bar').length) return;
        injectStyles();
        var c = TLGV.ctx.grph;
        c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
        $('#gCH').before(barHtml('grph', gRangeHtml()));
        $('#gCH').after('<div id="gAlt" class="tlgv-host" data-tlgv-host="grph" style="display:none;"></div>');
        bindUi($('#gWrap .tlgv-bar')); bindUi($('#gAlt'));
        gRefreshMax();
        gSyncDateInputs(Date.now() - 24 * 3600000, Date.now());
        if (window._gMaxTimer) clearInterval(window._gMaxTimer);
        window._gMaxTimer = setInterval(function () {
            if (!$('#gFrom').length) { clearInterval(window._gMaxTimer); window._gMaxTimer = null; return; }
            gRefreshMax();
        }, 30000);
        $('#gFrom').off('.gfut').on('change.gfut blur.gfut', function () { gClampFuture('#gFrom'); });
        $('#gTo').off('.gfut').on('change.gfut blur.gfut', function () { gClampFuture('#gTo'); });
        $('#gFrom,#gTo').off('keydown.gfut').on('keydown.gfut', function (e) { if (e.key === 'Enter') $('#gApply').trigger('click'); });
        $('#gApply').off('click').on('click', function () {
            var fv = $('#gFrom').val(), tv = $('#gTo').val();
            if (!fv || !tv) { warn('Please pick both From and To date/time.'); return; }
            var sMs = new Date(fv).getTime(), eMs = new Date(tv).getTime();
            if (isNaN(sMs) || isNaN(eMs)) { warn('Invalid date/time selected.'); return; }
            var now = Date.now();
            if (sMs > now || eMs > now + 60000) { warn('Future dates are not allowed.'); return; }
            if (sMs >= eMs) { warn('From date/time must be earlier than To date/time.'); return; }
            gLoadWindow(window._gA, sMs, eMs);
        });
        ensureLiveTimer();
    }
    TLGV.decorateGraphView = decorateGraphView;

    if (typeof window._gLoad === 'function') {
        var _origGLoad = window._gLoad;
        window._gLoad = function (aid, hrs) {
            gLoadSeq++; // supersede any custom-range request still in flight
            $('#gAlt').hide();
            var r = _origGLoad.apply(this, arguments);
            gSyncDateInputs(window._gReqStart, window._gReqEnd);
            return r;
        };
    }
    if (typeof window._gRender === 'function') {
        var _origGRender = window._gRender;
        window._gRender = function (data, aid, filter) {
            var r = _origGRender.apply(this, arguments);
            try {
                if ($('#gAlt').length) {
                    if (!window.mainGraphChart || $('#gEM').css('display') !== 'none') { disposeCharts(TLGV.ctx.grph); $('#gAlt').empty().hide(); }
                    else apply('grph', true);
                }
            } catch (e) { if (window.console) console.error('[TLGV] Graph view render failed', e); }
            return r;
        };
    }

    // ==================================================================
    // SURFACE 2 — per-asset Historical Graph modal
    // ==================================================================
    makeCtx('modal', {
        host: function () { return $('#rdpmsGraphAlt'); },
        overlay: function () { return $('#rdpmsGraphChartDiv'); },
        overlayChart: function () { return window.rdpmsGraphChart; },
        model: fromModal
    });
    if (typeof window.fnGetAssetGraph === 'function') {
        var _origAssetGraph = window.fnGetAssetGraph;
        window.fnGetAssetGraph = function (siteId, assetId) {
            // decorate BEFORE the first load finishes: the original kicks the load
            // synchronously, the response arrives later.
            var r = _origAssetGraph.apply(this, arguments);
            try {
                var $ov = $('#rdpmsGraphOverlay');
                if ($ov.length && !$ov.find('.tlgv-bar').length) {
                    injectStyles();
                    var c = TLGV.ctx.modal;
                    c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
                    var pills = '<span class="tlgv-grp"><span class="tlgv-lbl">Last</span><div class="tlgv-seg tlgv-mini" role="group" aria-label="Quick range">' +
                        ['1', '3', '6', '12', '24'].map(function (hh) { return '<button type="button" class="tl-time-pill' + (hh === '24' ? ' on active' : '') + '" data-hours="' + hh + '">' + hh + 'H</button>'; }).join('') +
                        '</div></span>';
                    $ov.find('.tl-modal-toolbar').after(barHtml('modal', pills));
                    $ov.find('#rdpmsGraphChartDiv').after('<div id="rdpmsGraphAlt" class="tlgv-host" data-tlgv-host="modal" style="display:none;"></div>');
                    bindUi($ov.find('.tlgv-bar')); bindUi($ov.find('#rdpmsGraphAlt'));
                    $ov.find('#graphFromDate,#graphToDate').attr('max', toLocalInput(Date.now()));
                    // quick-range pills: bound directly (shell stops propagation)
                    $ov.find('.tlgv-bar .tl-time-pill').on('click.tlgvpill', function (ev) {
                        ev.preventDefault(); ev.stopPropagation();
                        var hrs = parseInt($(this).attr('data-hours'), 10);
                        $(this).addClass('on active').siblings().removeClass('on active');
                        if (hrs > 0) window.loadHistoryGraphData(assetId, hrs);
                    });
                    $ov.find('#graphApplyRange').on('click.tlgvpill', function () { $ov.find('.tlgv-bar .tl-time-pill').removeClass('on active'); });
                }
            } catch (e) { if (window.console) console.error('[TLGV] modal decorate failed', e); }
            return r;
        };
    }
    if (typeof window.loadHistoryGraphData === 'function') {
        var _origLoadHist = window.loadHistoryGraphData;
        window.loadHistoryGraphData = function () {
            var c = TLGV.ctx.modal;
            disposeCharts(c);
            $('#rdpmsGraphAlt').empty().hide();
            // v616 parity: never request a window that ends in the future
            var args = Array.prototype.slice.call(arguments);
            if (args[3] && new Date(args[3]).getTime() > Date.now()) args[3] = new Date();
            return _origLoadHist.apply(this, args);
        };
    }
    if (typeof window.renderHistoryChart === 'function') {
        var _origRenderHist = window.renderHistoryChart;
        window.renderHistoryChart = function () {
            var r = _origRenderHist.apply(this, arguments);
            try { if ($('#rdpmsGraphAlt').length && window.rdpmsGraphChart) apply('modal', true); }
            catch (e) { if (window.console) console.error('[TLGV] modal render failed', e); }
            return r;
        };
    }
    if (typeof window.closeGraphModal === 'function') {
        var _origCloseGraph = window.closeGraphModal;
        window.closeGraphModal = function (e) {
            if (e && e.target && !$(e.target).hasClass('tl-modal-overlay') && !$(e.target).hasClass('rdpms-graph-overlay')) return _origCloseGraph.apply(this, arguments);
            disposeCharts(TLGV.ctx.modal);
            TLGV.ctx.modal.model = null;
            return _origCloseGraph.apply(this, arguments);
        };
    }
    if (typeof window.toggleModalFullscreen === 'function') {
        var _origFs = window.toggleModalFullscreen;
        window.toggleModalFullscreen = function () {
            var r = _origFs.apply(this, arguments);
            setTimeout(function () { (TLGV.ctx.modal.charts || []).forEach(function (ch) { try { ch.resize(); } catch (e) { } }); }, 120);
            return r;
        };
    }

    // ==================================================================
    // SURFACE 3 — IPS History Graph (telemetrylive-graphs.js)
    // ==================================================================
    makeCtx('ips', {
        host: function () { return $('#ipsgAlt'); },
        overlay: function () { return $('#ipsgChart'); },
        overlayChart: function () { return window.ipsG && window.ipsG.chart; },
        showOverlay: function (on) {
            if (on) { $('#ipsgChart').show(); $('.ipsg-toolbar').show(); }
            else { $('#ipsgChart').hide(); $('.ipsg-toolbar').hide(); }
        },
        getMode: function () { return (window.ipsG && window.ipsG.mode) || 'overlay'; },
        setMode: function (m) { if (window.ipsG) window.ipsG.mode = m; },
        model: fromIps
    });
    if (typeof window._ipsBuildShell === 'function') {
        var _origIpsShell = window._ipsBuildShell;
        window._ipsBuildShell = function () {
            var r = _origIpsShell.apply(this, arguments);
            try {
                injectStyles();
                var c = TLGV.ctx.ips;
                c.hidden = {}; c.search = ''; c.model = null; disposeCharts(c);
                // replace the v616-style Overlay/Stacked bar with the shared bar
                $('.ipsg-modebar').replaceWith(barHtml('ips'));
                $('#ipsgChart').after('<div id="ipsgAlt" class="tlgv-host" data-tlgv-host="ips" style="display:none;"></div>');
                bindUi($('.tlgv-bar[data-tlgv="ips"]')); bindUi($('#ipsgAlt'));
            } catch (e) { if (window.console) console.error('[TLGV] IPS decorate failed', e); }
            return r;
        };
    }
    if (typeof window._ipsRender === 'function') {
        var _origIpsRender = window._ipsRender;
        window._ipsRender = function () {
            var g = window.ipsG;
            if (!g || !$('#ipsgAlt').length) return _origIpsRender.apply(this, arguments);
            var m = g.mode;
            if (m === 'stacked' || m === 'individual') {
                // draw the (hidden) overlay so ipsG.chart and the empty state stay correct
                g.mode = 'overlay';
                var r;
                try { r = _origIpsRender.apply(this, arguments); } finally { g.mode = m; }
                if ($('#ipsgEmpty').is(':visible')) { disposeCharts(TLGV.ctx.ips); $('#ipsgAlt').hide(); return r; }
                apply('ips', true);
                return r;
            }
            disposeCharts(TLGV.ctx.ips);
            $('#ipsgAlt').empty().hide();
            $('.ipsg-toolbar').show();
            syncBar('ips');
            return _origIpsRender.apply(this, arguments);
        };
    }
    if (typeof window.ipsSavePng === 'function') {
        var _origIpsPng = window.ipsSavePng;
        window.ipsSavePng = function () {
            var c = TLGV.ctx.ips;
            if (window.ipsG && window.ipsG.mode !== 'overlay' && c.exportPng) return c.exportPng();
            return _origIpsPng.apply(this, arguments);
        };
    }

    TLGV._internals = { fromGraphView: fromGraphView, fromModal: fromModal, fromIps: fromIps, computeStats: computeStats, valueAt: valueAt, pickupBands: pickupBands, prefs: prefs };
})(window, window.jQuery);
