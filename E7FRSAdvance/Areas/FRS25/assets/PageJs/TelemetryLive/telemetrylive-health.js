/* ==========================================================================
 *  Telemetry Live -- HEALTH VIEW  (v618.7)
 *  ------------------------------------------------------------------------
 *  A separate view in "Live Asset Data" (4th button next to Card / Grid /
 *  Graph) built from the Live Asset Data health mockups:
 *    - one card per asset, worst-first or name order, filter + search
 *    - every live reading on its SAFE-RANGE bar (low / high / near limit)
 *    - health score ring + status (Healthy / Watch / Fault / No data)
 *    - track circuits: TPR picked-up / dropped pill, CIRCUIT VIEW with live
 *      values where they are measured (charger -> feed -> rails -> relay ->
 *      TPR), relay-end readings not flagged low while the track is occupied
 *    - click any reading: its recent trend (DashboardHistory, last 500
 *      samples, safe band shaded)
 *
 *  It only READS what the page already has -- nothing in the existing views,
 *  WebSocket or search flow is changed:
 *    wsLiveData[assetId].attrs / .dlRelays     live values (WebSocket)
 *    userAssetSimpleMap[assetId_attrId]        AliasName, Title, Min/Max scale
 *    getValueColorClass(attrId, value)         existing RDPMS danger rules
 *    /FRS25/Telemetry/GetFRSAttributeRangeBySiteId   per-asset safe ranges
 *    /FRS25/TelemetryHistory/GetDashboardHistoryData trend on demand
 *  Safe range = FRS attribute range for (asset, attribute) when the site has
 *  one; otherwise the RDPMS default band for that attribute (tooltip says
 *  which). Readings without either show their value on the metadata scale.
 *
 *  The view button is NOT a .tl-vmode-btn (the core handlers own that
 *  class); it toggles .tl-health-on on .at-table-card, which hides the
 *  current view's containers and shows #tlHealthView. Clicking Card / Grid /
 *  Graph leaves the Health view and repaints that view as usual.
 * ========================================================================== */

(function () {
    'use strict';
    var $ = window.jQuery;
    if (!$) return;

    var REFRESH_MS = 1000;
    var TREND_URL = '/FRS25/TelemetryHistory/GetDashboardHistoryData';
    var RANGE_URL = '/FRS25/Telemetry/GetFRSAttributeRangeBySiteId?siteId=';
    var TREND_TTL_MS = 60000;

    /* RDPMS default safe bands (by attribute Title), used when the site has
       no FRS attribute range for that asset / attribute. */
    var DEFAULT_RANGES = {
        IFMA: { min: 250, max: 500 },
        IRMA: { min: 210, max: 400 },
        VR: { min: 2.1, max: 5.2 },
        CHOKEV: { min: 0.6, max: 1.4 },
        TPRV: { min: 21, max: 32 },
        TPRVLOC: { min: 23, max: 40 },
        VF: { min: 2.5, max: 6 },
        CHARGERMA: { min: 100, max: 2500 },
        CHARGERV: { min: 90, max: 125 },
        CHARGEROPV: { min: 4.5, max: 10 }
    };
    var TRACK_ORDER = ['CHARGERV', 'CHARGEROPV', 'CHARGERMA', 'IFMA', 'VF', 'CHOKEV', 'VR', 'IRMA', 'TPRV', 'TPRVLOC'];
    var OCC_SUPPRESS = { VR: 1, IRMA: 1, TPRV: 1 };   /* low is normal while a train is on the track */

    var H = {
        on: false, timer: null, prevMode: 'Cards',
        ranges: {}, rangesSite: null, rangeLogged: false,
        sig: '', els: {},
        sort: 'worst', filter: 'all', q: '',
        circuitAll: false, circuit: {}, trendKey: {}, trend: {}
    };

    /* ======================================================================
       HELPERS
       ====================================================================== */
    function norm(s) { return String(s == null ? '' : s).replace(/<[^>]*>/g, '').toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function strip(s) { return String(s == null ? '' : s).replace(/<[^>]*>/g, '').trim(); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
    function num(v) {
        if (v === null || v === undefined || v === '') return null;
        var n = parseFloat(v);
        if (isNaN(n)) return null;
        return (typeof window.tlZeroFloor === 'function') ? window.tlZeroFloor(n) : n;
    }
    function pickNum(o, keys) {
        for (var i = 0; i < keys.length; i++) {
            if (o && o[keys[i]] !== undefined && o[keys[i]] !== null && o[keys[i]] !== '') {
                var n = parseFloat(o[keys[i]]);
                if (!isNaN(n)) return n;
            }
        }
        return null;
    }
    function fmt(v) {
        if (v === null || v === undefined) return '--';
        var a = Math.abs(v);
        return a >= 1000 ? v.toFixed(0) : a >= 100 ? v.toFixed(1) : v.toFixed(2);
    }
    function pad(n) { return (n < 10 ? '0' : '') + n; }
    function fmtApi(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + pad(d.getMonth() + 1) + d.getFullYear() + '_' + pad(d.getHours()) + pad(d.getMinutes()) + pad(d.getSeconds());
    }
    function fmtClock(ms) { var d = new Date(ms); return pad(d.getHours()) + ':' + pad(d.getMinutes()); }
    function live() { return window.wsLiveData || {}; }
    function siteId() { return String($('#drpSite').val() || ''); }
    function assetTypeId() { return parseInt(window.wsCurrentAssetTypeId || $('#drpAssetType').val() || 0, 10) || 0; }
    function isTrackType() { return assetTypeId() === 1; }

    /* ======================================================================
       SAFE RANGES  (FRS attribute range per asset, else RDPMS default)
       ====================================================================== */
    function loadRanges() {
        var sid = siteId();
        if (!sid || sid === '0' || H.rangesSite === sid) return;
        H.rangesSite = sid;
        H.ranges = {};
        $.ajax({
            url: RANGE_URL + encodeURIComponent(sid),
            type: 'GET',
            dataType: 'json',
            success: function (rows) {
                if (H.rangesSite !== sid) return;
                var map = {};
                var n = 0;
                if (rows && rows.length) {
                    if (!H.rangeLogged && window.console) {
                        H.rangeLogged = true;
                        console.log('[Health] FRS attribute range fields:', Object.keys(rows[0] || {}).join(', '));
                    }
                    for (var i = 0; i < rows.length; i++) {
                        var r = rows[i] || {};
                        if (r.AssetId === null || r.AssetId === undefined) continue;
                        var name = r.AttributeName || r.AttributeTitle || r.Title || r.Name || '';
                        var mn = pickNum(r, ['MinValue', 'MinimumValue', 'LowerLimit', 'LowerValue', 'MinRange', 'RangeMin', 'LowValue', 'Min']);
                        var mx = pickNum(r, ['MaxValue', 'MaximumValue', 'UpperLimit', 'UpperValue', 'MaxRange', 'RangeMax', 'HighValue', 'Max']);
                        if (mn === null && mx === null) continue;
                        map[String(r.AssetId) + '|' + norm(name)] = {
                            min: mn, max: mx,
                            avg: pickNum(r, ['AverageValue', 'AvgValue', 'Average']),
                            src: 'FRS range'
                        };
                        n++;
                    }
                }
                H.ranges = map;
                if (window.console) console.log('[Health] safe ranges for site ' + sid + ': ' + n);
                H.sig = '';
                if (H.on) render(true);
            },
            error: function () { /* defaults are used */ }
        });
    }
    function rangeFor(aid, title, alias) {
        var r = H.ranges[aid + '|' + norm(title)] || H.ranges[aid + '|' + norm(alias)];
        if (r && (r.min !== null || r.max !== null)) return r;
        var d = DEFAULT_RANGES[norm(title)] || DEFAULT_RANGES[norm(alias)];
        return d ? { min: d.min, max: d.max, avg: null, src: 'RDPMS default' } : null;
    }

    /* ======================================================================
       READ ONE ASSET FROM THE LIVE STORE
       ====================================================================== */
    function trackOccupied(e, rows) {
        var dl = e.dlRelays || {};
        for (var k in dl) {
            if (!dl.hasOwnProperty(k)) continue;
            if (norm(dl[k].displayName || k).indexOf('TPR') !== -1) return !dl[k].isPickup;
        }
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].key === 'TPRV' && rows[i].v !== null) return rows[i].v < 1;
        }
        return null;
    }
    function readAsset(aid) {
        var e = live()[aid] || {};
        var attrs = e.attrs || {};
        var meta = window.userAssetSimpleMap || {};
        var byName = window.assetAttributeByName || {};
        var rows = [];
        for (var k in attrs) {
            if (!attrs.hasOwnProperty(k)) continue;
            var a = attrs[k] || {};
            var attrId = String(a.AttrId || a.AssetAttributeId || '');
            var m = meta[aid + '_' + attrId] || {};
            var title = strip(m.attributeName || m.AttributeName || k);
            var alias = strip(m.name || m.AliasName || byName[k] || k);
            var v = num(a.Value);
            rows.push({
                k: k, key: norm(title), attrId: attrId, title: title, alias: alias,
                label: title !== alias ? title : alias, sub: title !== alias ? alias : '',
                v: v, stale: a.IsFresh === false,
                scaleMin: num(m.minValue), scaleMax: num(m.maxValue),
                range: rangeFor(aid, title, alias)
            });
        }
        if (isTrackType()) {
            rows.sort(function (x, y) {
                var ix = TRACK_ORDER.indexOf(x.key), iy = TRACK_ORDER.indexOf(y.key);
                if (ix < 0) ix = 99;
                if (iy < 0) iy = 99;
                return ix - iy || x.label.localeCompare(y.label);
            });
        } else {
            rows.sort(function (x, y) { return x.label.localeCompare(y.label, undefined, { numeric: true }); });
        }
        var occ = isTrackType() ? trackOccupied(e, rows) : null;
        var outs = 0, near = 0, stale = 0, have = 0;
        for (var i = 0; i < rows.length; i++) {
            var r = rows[i];
            r.st = 'none';
            if (r.v === null) continue;
            have++;
            if (r.stale) stale++;
            var g = r.range;
            if (g && g.min !== null && g.max !== null && g.max > g.min) {
                var span = g.max - g.min;
                if (r.v < g.min) r.st = (occ && OCC_SUPPRESS[r.key]) ? 'occ' : 'low';
                else if (r.v > g.max) r.st = 'high';
                else if (r.v < g.min + span * 0.08 || r.v > g.max - span * 0.08) r.st = 'near';
                else r.st = 'ok';
                r.lo = g.min - span * 0.3;
                r.hi = g.max + span * 0.3;
                r.bandL = 18;
                r.bandW = 64;
            } else {
                var dangerCls = (typeof window.getValueColorClass === 'function') ? window.getValueColorClass(r.attrId, r.v) : '';
                r.st = dangerCls === 'val-danger' ? ((occ && OCC_SUPPRESS[r.key]) ? 'occ' : 'high') : 'ok';
                r.lo = r.scaleMin !== null ? r.scaleMin : 0;
                r.hi = (r.scaleMax !== null && r.scaleMax > r.lo) ? r.scaleMax : Math.max(r.lo + 1, r.v * 1.5);
                r.bandL = null;
            }
            if (r.st === 'low' || r.st === 'high') outs++;
            else if (r.st === 'near') near++;
            r.pos = Math.max(1, Math.min(99, ((r.v - r.lo) / (r.hi - r.lo)) * 100));
        }
        var status = !have ? 'nodata' : outs >= 3 ? 'fault' : outs ? 'watch' : 'ok';
        var score = !have ? 0 : Math.max(0, Math.round(100 - outs * 18 - near * 6 - stale * 4));
        var relays = [];
        var dl = e.dlRelays || {};
        for (var dk in dl) {
            if (dl.hasOwnProperty(dk)) relays.push({ name: dl[dk].displayName || dk, up: !!dl[dk].isPickup });
        }
        return {
            aid: aid, name: e.AssetName || ('Asset ' + aid), rows: rows, occ: occ, outs: outs, near: near,
            stale: stale, have: have, status: status, score: score, relays: relays,
            sev: (status === 'fault' ? 3000 : status === 'watch' ? 2000 : status === 'nodata' ? 0 : 1000) + outs * 50 + near * 10 + stale
        };
    }
    function assetIds() {
        var ld = live();
        var at = assetTypeId();
        var ids = [];
        for (var aid in ld) {
            if (!ld.hasOwnProperty(aid)) continue;
            var e = ld[aid];
            if (!e || !e.attrs) continue;
            if (at && e.AssetTypeId && parseInt(e.AssetTypeId, 10) !== at) continue;
            ids.push(aid);
        }
        return ids;
    }

    /* ======================================================================
       CSS
       ====================================================================== */
    var CSS = '' +
        '.at-table-card.tl-health-on #atCardView,.at-table-card.tl-health-on .at-table-scroll{display:none !important;}' +
        '#tlHealthView{display:none;padding:14px 16px 18px;box-sizing:border-box;width:100%;color:var(--at-t1,inherit);}' +
        '.at-table-card.tl-health-on #tlHealthView{display:block;}' +
        '.tl-vmode-hbtn{width:28px;height:26px;display:inline-flex;align-items:center;justify-content:center;background:transparent;' +
        'border:1px solid transparent;border-radius:6px;color:var(--text-3,#8D9CB2);font-size:12px;cursor:pointer;padding:0;}' +
        '.tl-vmode-hbtn:hover{color:var(--text-1,inherit);background:var(--glass-2,rgba(127,140,160,.15));}' +
        '.tl-vmode-hbtn.active{background:linear-gradient(180deg,rgba(34,211,238,0.20),rgba(34,211,238,0.08));border-color:rgba(34,211,238,0.40);color:var(--brand,#22d3ee);}' +
        '.hv-bar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 14px;margin-bottom:12px;}' +
        '.hv-chip{display:inline-flex;align-items:center;gap:6px;font-size:12.5px;font-weight:600;padding:4px 10px;border-radius:14px;border:1px solid var(--at-edge,rgba(127,140,160,.3));}' +
        '.hv-chip i{width:8px;height:8px;border-radius:50%;display:inline-block;}' +
        '.hv-seg{display:inline-flex;padding:2px;border-radius:9px;border:1px solid var(--at-edge,rgba(127,140,160,.3));background:var(--at-g1,transparent);gap:2px;}' +
        '.hv-seg button{height:26px;padding:0 10px;border:none;border-radius:7px;background:transparent;color:var(--at-t2,inherit);font:inherit;font-size:12px;cursor:pointer;}' +
        '.hv-seg button[aria-pressed="true"]{background:var(--brand,#22d3ee);color:#04161A;font-weight:600;}' +
        '.hv-search{height:30px;border-radius:15px;border:1px solid var(--at-edge,rgba(127,140,160,.3));background:var(--at-g1,transparent);color:var(--at-t1,inherit);padding:0 12px;font:inherit;font-size:12.5px;min-width:180px;}' +
        '.hv-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(330px,1fr));gap:14px;align-items:start;}' +
        '.hv-card{border:1px solid var(--at-edge,rgba(127,140,160,.25));border-radius:14px;background:var(--at-g1,rgba(127,140,160,.05));padding:12px 14px 12px;}' +
        '.hv-card.st-fault{box-shadow:inset 0 3px 0 #E5484D;border-color:rgba(229,72,77,.45);}' +
        '.hv-card.st-watch{box-shadow:inset 0 3px 0 #E0A100;border-color:rgba(224,161,0,.45);}' +
        '.hv-head{display:flex;align-items:center;gap:10px;}' +
        '.hv-pill{display:inline-flex;align-items:center;height:26px;padding:0 11px;border-radius:13px;font-weight:700;font-size:13.5px;color:#fff;background:#64748b;white-space:nowrap;}' +
        '.hv-pill.clear{background:#1F9D55;} .hv-pill.occ{background:#DC2626;}' +
        '.hv-ring{flex:none;}' +
        '.hv-ttl{flex:1;min-width:0;}' +
        '.hv-status{font-size:12.5px;font-weight:700;}' +
        '.hv-status.ok{color:#1F9D55;} .hv-status.watch{color:#C98A00;} .hv-status.fault{color:#E5484D;} .hv-status.nodata{color:var(--at-t3,#8D9CB2);}' +
        '.hv-sum{font-size:11.5px;color:var(--at-t3,#8D9CB2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-tools{display:flex;gap:4px;}' +
        '.hv-tool{width:28px;height:28px;border-radius:7px;border:1px solid var(--at-edge,rgba(127,140,160,.3));background:transparent;color:var(--at-t2,inherit);cursor:pointer;font-size:12px;}' +
        '.hv-tool[aria-pressed="true"]{color:var(--brand,#22d3ee);border-color:var(--brand,#22d3ee);}' +
        '.hv-relays{display:flex;flex-wrap:wrap;gap:4px;margin:8px 0 2px;}' +
        '.hv-relay{font-size:11px;font-weight:700;padding:1px 7px;border-radius:4px;border:1px solid var(--at-edge,rgba(127,140,160,.3));}' +
        '.hv-relay.up{color:#1F9D55;border-color:rgba(31,157,85,.45);} .hv-relay.dn{color:#E5484D;border-color:rgba(229,72,77,.45);}' +
        '.hv-rows{margin-top:8px;display:flex;flex-direction:column;gap:1px;}' +
        '.hv-row{display:grid;grid-template-columns:minmax(0,1fr) 74px 44px 84px;align-items:center;gap:8px;padding:3px 6px;border-radius:6px;cursor:pointer;}' +
        '.hv-row:hover{background:rgba(127,140,160,.10);}' +
        '.hv-row.sel{box-shadow:inset 0 0 0 1.5px var(--brand,#22d3ee);}' +
        '.hv-row.st-low{background:rgba(224,161,0,.12);} .hv-row.st-high{background:rgba(229,72,77,.12);}' +
        '.hv-row .lb{font-size:12px;color:var(--at-t2,inherit);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-row .lb small{color:var(--at-t3,#8D9CB2);margin-left:4px;}' +
        '.hv-row .vl{text-align:right;font-weight:700;font-size:12.5px;font-variant-numeric:tabular-nums;}' +
        '.hv-row.st-low .vl,.hv-row.st-low .fl{color:#C98A00;} .hv-row.st-high .vl,.hv-row.st-high .fl{color:#E5484D;}' +
        '.hv-row .fl{font-size:10.5px;font-weight:700;color:var(--at-t3,#8D9CB2);white-space:nowrap;}' +
        '.hv-row.stale .vl{opacity:.55;}' +
        '.hv-bar2{position:relative;height:12px;}' +
        '.hv-bar2 .tr{position:absolute;left:0;right:0;top:5px;height:2px;border-radius:2px;background:rgba(127,140,160,.30);}' +
        '.hv-bar2 .bd{position:absolute;top:3px;height:6px;border-radius:3px;background:rgba(31,157,85,.30);}' +
        '.hv-bar2 .mk{position:absolute;top:0;width:3px;height:12px;border-radius:2px;background:var(--at-t1,#13202E);margin-left:-1.5px;}' +
        '.hv-row.st-low .mk{background:#E0A100;} .hv-row.st-high .mk{background:#E5484D;}' +
        '.hv-circ{margin-top:8px;border:1px solid var(--at-edge,rgba(127,140,160,.25));border-radius:10px;padding:6px 8px;}' +
        '.hv-circ svg{display:block;width:100%;height:auto;}' +
        '.hv-circ text{font-family:inherit;}' +
        '.hv-trend{margin-top:8px;border:1px solid var(--at-edge,rgba(127,140,160,.25));border-radius:10px;padding:6px 8px 4px;}' +
        '.hv-trend .th{display:flex;justify-content:space-between;font-size:11.5px;color:var(--at-t3,#8D9CB2);}' +
        '.hv-trend .th b{color:var(--at-t1,inherit);}' +
        '.hv-trend svg{display:block;width:100%;height:90px;}' +
        '.hv-empty{padding:40px;text-align:center;color:var(--at-t3,#8D9CB2);}';

    function ensureCss() {
        if (document.getElementById('tlHealthCss')) return;
        var st = document.createElement('style');
        st.id = 'tlHealthCss';
        st.textContent = CSS;
        document.head.appendChild(st);
    }

    /* ======================================================================
       ENTRY BUTTON + CONTAINER
       ====================================================================== */
    function ensureUi() {
        ensureCss();
        var group = document.querySelector('.at-table-card .tl-vmode');
        if (group && !document.getElementById('tlHealthBtn')) {
            var b = document.createElement('button');
            b.type = 'button';
            b.id = 'tlHealthBtn';
            b.className = 'tl-vmode-hbtn';
            b.title = 'Health view';
            b.setAttribute('aria-pressed', 'false');
            b.innerHTML = '<i class="fa-solid fa-heart-pulse"></i>';
            b.addEventListener('click', function (e) {
                e.preventDefault();
                e.stopPropagation();
                if (H.on) return;
                enter();
            });
            group.appendChild(b);
        }
        var card = document.querySelector('.at-table-card');
        if (card && !document.getElementById('tlHealthView')) {
            var v = document.createElement('div');
            v.id = 'tlHealthView';
            v.setAttribute('aria-live', 'off');
            card.appendChild(v);
            v.addEventListener('click', onViewClick);
            v.addEventListener('input', function (e) {
                if (e.target && e.target.id === 'hvSearch') { H.q = e.target.value || ''; H.sig = ''; render(true); }
            });
        }
        return !!(card && document.getElementById('tlHealthView'));
    }

    function enter() {
        if (!ensureUi()) return;
        H.prevMode = $('.tl-vmode-btn.active').attr('data-vmode') || H.prevMode || 'Cards';
        $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
        $('#tlHealthBtn').addClass('active').attr('aria-pressed', 'true');
        $('.at-table-card').addClass('tl-health-on');
        H.on = true;
        H.sig = '';
        loadRanges();
        render(true);
        if (H.timer) clearInterval(H.timer);
        H.timer = setInterval(function () { if (H.on && !document.hidden) render(false); }, REFRESH_MS);
    }
    function leave() {
        if (!H.on) return;
        H.on = false;
        if (H.timer) { clearInterval(H.timer); H.timer = null; }
        $('.at-table-card').removeClass('tl-health-on');
        $('#tlHealthBtn').removeClass('active').attr('aria-pressed', 'false');
    }
    /* Card / Grid / Graph buttons: leave Health first (capture phase, so the
       core click handlers then run normally and repaint their view). */
    document.addEventListener('click', function (e) {
        var b = e.target && e.target.closest ? e.target.closest('.tl-vmode-btn') : null;
        if (b && H.on) leave();
    }, true);
    $(document).on('change', '#drpSite', function () { H.rangesSite = null; H.trend = {}; H.sig = ''; if (H.on) loadRanges(); });
    $(document).on('change', '#drpAssetType', function () { H.sig = ''; H.trendKey = {}; });

    /* ======================================================================
       RENDER
       ====================================================================== */
    var RING_C = 2 * Math.PI * 19;
    var STATUS_TEXT = { ok: 'Healthy', watch: 'Watch', fault: 'Fault', nodata: 'No data' };
    var STATUS_COL = { ok: '#22A55B', watch: '#E0A100', fault: '#E5484D', nodata: '#94A3B8' };

    function ringSvg(A) {
        return '<svg class="hv-ring" width="40" height="40" viewBox="0 0 48 48" aria-hidden="true">' +
            '<circle cx="24" cy="24" r="19" fill="none" stroke="rgba(127,140,160,.22)" stroke-width="5"></circle>' +
            '<circle data-hv="ring" cx="24" cy="24" r="19" fill="none" stroke="' + STATUS_COL[A.status] + '" stroke-width="5" stroke-linecap="round" ' +
            'stroke-dasharray="' + (A.score / 100 * RING_C).toFixed(1) + ' ' + RING_C.toFixed(1) + '" transform="rotate(-90 24 24)"></circle>' +
            '<text data-hv="score" x="24" y="29" text-anchor="middle" font-size="14" font-weight="700" fill="currentColor">' + (A.have ? A.score : '-') + '</text></svg>';
    }
    function summaryText(A) {
        if (!A.have) return 'Waiting for live values';
        var p = [];
        if (A.outs) p.push(A.outs + ' outside safe range');
        if (A.near) p.push(A.near + ' near a limit');
        if (A.stale) p.push(A.stale + ' stale');
        return p.length ? p.join(', ') : 'All readings inside safe range';
    }
    function flagText(r) {
        return r.st === 'low' ? '&darr; Low' : r.st === 'high' ? '&uarr; High' : r.st === 'near' ? 'Near' : r.st === 'occ' ? 'Occ.' : '';
    }
    function rowTip(r) {
        var g = r.range;
        var t = r.label + (r.sub ? ' (' + r.sub + ')' : '') + ': ' + fmt(r.v);
        if (g && g.min !== null && g.max !== null) t += ' | safe ' + fmt(g.min) + ' to ' + fmt(g.max) + ' (' + g.src + ')' + (g.avg !== null && g.avg !== undefined ? ' | avg ' + fmt(g.avg) : '');
        if (r.st === 'occ') t += ' | low while the track is occupied (normal)';
        if (r.stale) t += ' | stale';
        return t;
    }
    function rowHtml(A, r) {
        return '<div class="hv-row st-' + r.st + (r.stale ? ' stale' : '') + (H.trendKey[A.aid] === r.k ? ' sel' : '') +
            '" data-hv-row="' + esc(A.aid) + '" data-hv-k="' + esc(r.k) + '" title="' + esc(rowTip(r)) + '">' +
            '<span class="lb">' + esc(r.label) + (r.sub ? '<small>' + esc(r.sub) + '</small>' : '') + '</span>' +
            '<span class="vl">' + fmt(r.v) + '</span>' +
            '<span class="fl">' + flagText(r) + '</span>' +
            '<span class="hv-bar2"><span class="tr"></span>' +
            (r.bandL !== null && r.bandL !== undefined ? '<span class="bd" style="left:' + r.bandL + '%;width:' + r.bandW + '%"></span>' : '') +
            (r.v !== null ? '<span class="mk" style="left:' + r.pos.toFixed(1) + '%"></span>' : '') + '</span></div>';
    }

    /* ---- circuit view (track circuits) ---------------------------------- */
    function cv(A, key) {
        for (var i = 0; i < A.rows.length; i++) if (A.rows[i].key === key) return A.rows[i];
        return null;
    }
    function cvText(A, key, x, y, anchor, label) {
        var r = cv(A, key);
        var col = !r ? 'currentColor' : r.st === 'low' ? '#C98A00' : r.st === 'high' ? '#E5484D' : 'currentColor';
        return '<text x="' + x + '" y="' + y + '" text-anchor="' + anchor + '" font-size="10" opacity=".65" fill="currentColor">' + label + '</text>' +
            '<text x="' + x + '" y="' + (y + 13) + '" text-anchor="' + anchor + '" font-size="12" font-weight="700" fill="' + col + '">' + (r ? fmt(r.v) : '--') + '</text>';
    }
    function circuitSvg(A) {
        var ifr = cv(A, 'IFMA'), irr = cv(A, 'IRMA');
        var ballast = (ifr && irr && ifr.v !== null && irr.v !== null) ? fmt(ifr.v - irr.v) : '--';
        var tprCol = A.occ === true ? '#E5484D' : A.occ === false ? '#22A55B' : '#94A3B8';
        return '<svg viewBox="0 0 420 200" role="img" aria-label="Track circuit with live values">' +
            '<rect x="8" y="58" width="64" height="54" rx="9" fill="none" stroke="currentColor" stroke-opacity=".35"></rect>' +
            '<text x="40" y="80" text-anchor="middle" font-size="11" font-weight="700" fill="currentColor">Charger</text>' +
            '<path d="M28 92h24M33 98h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"></path>' +
            '<path d="M72 76H112V70M104 58h16v12h-16zM112 58V40H150" stroke="currentColor" stroke-opacity=".5" stroke-width="1.6" fill="none"></path>' +
            '<path d="M150 40c4 -8 8 8 12 0s8 8 12 0s8 8 12 0" stroke="currentColor" stroke-opacity=".5" stroke-width="1.6" fill="none"></path>' +
            '<path d="M186 40H226V116M72 98H94V140H140" stroke="currentColor" stroke-opacity=".5" stroke-width="1.6" fill="none"></path>' +
            '<path d="M140 116H330M140 140H330" stroke="' + (A.occ ? '#E5484D' : 'currentColor') + '" stroke-width="3.2" stroke-linecap="round"></path>' +
            '<path d="M150 112v32M174 112v32M198 112v32M222 112v32M246 112v32M270 112v32M294 112v32M318 112v32" stroke="currentColor" stroke-opacity=".22" stroke-width="3"></path>' +
            '<path d="M330 116H352V80M330 140H372V80" stroke="currentColor" stroke-opacity=".5" stroke-width="1.6" fill="none"></path>' +
            '<rect x="342" y="52" width="40" height="28" rx="5" fill="none" stroke="currentColor" stroke-width="1.6"></rect>' +
            '<circle cx="362" cy="30" r="8" fill="' + tprCol + '"></circle>' +
            '<text x="362" y="14" text-anchor="middle" font-size="10.5" font-weight="700" fill="currentColor">TPR</text>' +
            cvText(A, 'CHARGERV', 40, 124, 'middle', 'Input V') +
            cvText(A, 'CHARGEROPV', 40, 152, 'middle', 'Output V') +
            cvText(A, 'CHARGERMA', 40, 180, 'middle', 'Current mA') +
            cvText(A, 'IFMA', 98, 18, 'middle', 'Feed I') +
            cvText(A, 'VF', 150, 74, 'middle', 'Feed V') +
            cvText(A, 'CHOKEV', 168, 18, 'middle', 'Choke') +
            '<text x="235" y="166" text-anchor="middle" font-size="10" opacity=".65" fill="currentColor">Ballast I (If - Ir)</text>' +
            '<text x="235" y="180" text-anchor="middle" font-size="12" font-weight="700" fill="currentColor">' + ballast + '</text>' +
            cvText(A, 'VR', 300, 74, 'middle', 'Relay V') +
            cvText(A, 'IRMA', 300, 18, 'middle', 'Relay I') +
            cvText(A, 'TPRV', 410, 108, 'end', 'TPR V') +
            cvText(A, 'TPRVLOC', 410, 162, 'end', 'Loc 24 V') +
            '</svg>';
    }

    /* ---- trend ----------------------------------------------------------- */
    function trendHtml(A) {
        var k = H.trendKey[A.aid];
        if (!k) return '';
        var r = null;
        for (var i = 0; i < A.rows.length; i++) if (A.rows[i].k === k) r = A.rows[i];
        if (!r) return '';
        var T = H.trend[A.aid];
        if (!T || (Date.now() - T.at) > TREND_TTL_MS) fetchTrend(A.aid);
        var head = '<div class="th"><span><b>' + esc(r.label) + '</b> recent trend</span><span>' +
            (T && T.from ? fmtClock(T.from) + ' to ' + fmtClock(T.to) : 'loading...') + '</span></div>';
        if (!T || !T.cols) return '<div class="hv-trend">' + head + '</div>';
        var ci = -1;
        for (var c = 0; c < T.cols.length; c++) {
            if (String(T.cols[c].attrId) === String(r.attrId) || norm(T.cols[c].name) === r.key || norm(T.cols[c].name) === norm(r.alias)) { ci = c; break; }
        }
        if (ci < 0) return '<div class="hv-trend">' + head + '<div class="th">No history for this reading in the last 24 h.</div></div>';
        var pts = [];
        for (var p = 0; p < T.rows.length; p++) {
            var v = num(T.rows[p].v[ci]);
            if (v !== null) pts.push({ t: T.rows[p].t, v: v });
        }
        if (pts.length < 2) return '<div class="hv-trend">' + head + '<div class="th">Not enough samples.</div></div>';
        var lo = Infinity, hi = -Infinity;
        for (var q = 0; q < pts.length; q++) { lo = Math.min(lo, pts[q].v); hi = Math.max(hi, pts[q].v); }
        var g = r.range;
        if (g && g.min !== null) lo = Math.min(lo, g.min);
        if (g && g.max !== null) hi = Math.max(hi, g.max);
        if (hi === lo) { hi += 1; lo -= 1; }
        var pad2 = (hi - lo) * 0.08; lo -= pad2; hi += pad2;
        var t0 = pts[0].t, t1 = pts[pts.length - 1].t || (t0 + 1);
        var W = 400, Hh = 90;
        var X = function (t) { return ((t - t0) / ((t1 - t0) || 1)) * W; };
        var Y = function (v) { return Hh - ((v - lo) / (hi - lo)) * Hh; };
        var d = '';
        for (var s = 0; s < pts.length; s++) d += (s ? 'L' : 'M') + X(pts[s].t).toFixed(1) + ' ' + Y(pts[s].v).toFixed(1);
        var band = (g && g.min !== null && g.max !== null)
            ? '<rect x="0" y="' + Y(g.max).toFixed(1) + '" width="' + W + '" height="' + (Y(g.min) - Y(g.max)).toFixed(1) + '" fill="rgba(31,157,85,.16)"></rect>' : '';
        var col = r.st === 'low' ? '#E0A100' : r.st === 'high' ? '#E5484D' : 'var(--brand,#22d3ee)';
        return '<div class="hv-trend">' + head +
            '<svg viewBox="0 0 ' + W + ' ' + Hh + '" preserveAspectRatio="none" aria-hidden="true">' + band +
            '<path d="' + d + '" fill="none" stroke="' + col + '" stroke-width="1.6" vector-effect="non-scaling-stroke"></path></svg>' +
            '<div class="th"><span>min ' + fmt(Math.min.apply(null, pts.map(function (z) { return z.v; }))) + '</span><span>' +
            (g && g.min !== null && g.max !== null ? 'safe ' + fmt(g.min) + ' to ' + fmt(g.max) : '') + '</span><span>max ' +
            fmt(Math.max.apply(null, pts.map(function (z) { return z.v; }))) + '</span></div></div>';
    }
    function fetchTrend(aid) {
        var cur = H.trend[aid];
        if (cur && cur.loading) return;
        H.trend[aid] = { loading: true, at: Date.now(), cols: cur ? cur.cols : null, rows: cur ? cur.rows : [], from: cur ? cur.from : 0, to: cur ? cur.to : 0 };
        var now = Date.now();
        $.ajax({
            url: TREND_URL,
            type: 'GET',
            dataType: 'json',
            data: { assetId: aid, startDate: fmtApi(now - 24 * 3600000), endDate: fmtApi(now), page: 1, pageSize: 500, sort: 'desc', fillGaps: 'true', cursor: '' },
            success: function (resp) {
                var payload = resp || {};
                var inner = payload.Data || payload.data;
                if (inner && (inner.columns || inner.Columns)) payload = inner;
                var cols = (payload.columns || payload.Columns || []).map(function (c) {
                    return { attrId: c.attrId !== undefined ? c.attrId : c.AttrId, name: c.name || c.Name || '' };
                });
                var rows = (payload.rows || payload.Rows || []).map(function (r) {
                    return { t: new Date(r.ts || r.Ts).getTime(), v: r.v || r.V || [] };
                }).filter(function (r) { return !isNaN(r.t); });
                rows.sort(function (a, b) { return a.t - b.t; });
                H.trend[aid] = { loading: false, at: Date.now(), cols: cols, rows: rows,
                    from: rows.length ? rows[0].t : 0, to: rows.length ? rows[rows.length - 1].t : 0 };
                H.sig = '';
                if (H.on) render(true);
            },
            error: function () {
                H.trend[aid] = { loading: false, at: Date.now(), cols: [], rows: [] };
                H.sig = '';
                if (H.on) render(true);
            }
        });
    }

    /* ---- card ------------------------------------------------------------ */
    function cardHtml(A) {
        var track = isTrackType();
        var pillCls = track ? (A.occ === true ? 'occ' : A.occ === false ? 'clear' : '') : '';
        var html = '<article class="hv-card st-' + A.status + '" data-hv-card="' + esc(A.aid) + '">' +
            '<div class="hv-head">' + ringSvg(A) +
            '<div class="hv-ttl"><span class="hv-pill ' + pillCls + '" title="' + (track ? (A.occ === true ? 'TPR dropped (occupied)' : A.occ === false ? 'TPR picked up (clear)' : 'TPR state unknown') : '') + '">' + esc(A.name) + '</span>' +
            '<div><span class="hv-status ' + A.status + '">' + STATUS_TEXT[A.status] + '</span> <span class="hv-sum">' + esc(summaryText(A)) + '</span></div></div>' +
            '<div class="hv-tools">' +
            (track ? '<button type="button" class="hv-tool" data-hv-circ="' + esc(A.aid) + '" aria-pressed="' + !!(H.circuitAll || H.circuit[A.aid]) + '" title="Circuit view"><i class="fa-solid fa-diagram-project"></i></button>' : '') +
            '</div></div>';
        if (A.relays.length) {
            html += '<div class="hv-relays">';
            for (var i = 0; i < A.relays.length; i++) {
                html += '<span class="hv-relay ' + (A.relays[i].up ? 'up' : 'dn') + '">' + esc(A.relays[i].name) + ' ' + (A.relays[i].up ? '&uarr;' : '&darr;') + '</span>';
            }
            html += '</div>';
        }
        if (track && (H.circuitAll || H.circuit[A.aid])) html += '<div class="hv-circ">' + circuitSvg(A) + '</div>';
        html += '<div class="hv-rows">';
        for (var j = 0; j < A.rows.length; j++) html += rowHtml(A, A.rows[j]);
        if (!A.rows.length) html += '<div class="hv-sum">No live readings yet.</div>';
        html += '</div>' + trendHtml(A) + '</article>';
        return html;
    }

    function render(force) {
        var host = document.getElementById('tlHealthView');
        if (!host || !H.on) return;
        var ids = assetIds();
        var list = ids.map(readAsset);
        var counts = { fault: 0, watch: 0, ok: 0, nodata: 0 };
        for (var i = 0; i < list.length; i++) counts[list[i].status]++;
        var q = H.q.trim().toUpperCase();
        var shown = list.filter(function (A) {
            if (q && A.name.toUpperCase().indexOf(q) === -1) return false;
            if (H.filter === 'attn') return A.status === 'fault' || A.status === 'watch';
            if (H.filter === 'occ') return A.occ === true;
            return true;
        });
        shown.sort(H.sort === 'worst'
            ? function (a, b) { return (b.sev - a.sev) || a.name.localeCompare(b.name, undefined, { numeric: true }); }
            : function (a, b) { return a.name.localeCompare(b.name, undefined, { numeric: true }); });

        /* cheap signature: only rebuild DOM when something visible changed */
        var sig = H.sort + H.filter + q + H.circuitAll + JSON.stringify(H.circuit) + JSON.stringify(H.trendKey) + '|' +
            shown.map(function (A) {
                return A.aid + ':' + A.status + A.score + A.occ + A.relays.map(function (r) { return r.up ? 1 : 0; }).join('') + ':' +
                    A.rows.map(function (r) { return r.k + '=' + fmt(r.v) + r.st + (r.stale ? 's' : ''); }).join(',');
            }).join(';');
        if (!force && sig === H.sig) return;
        H.sig = sig;

        var seg = function (key, val, label) {
            return '<button type="button" data-hv-' + key + '="' + val + '" aria-pressed="' + (H[key] === val) + '">' + label + '</button>';
        };
        var bar = '<div class="hv-bar">' +
            '<span class="hv-chip"><i style="background:#E5484D"></i>Fault ' + counts.fault + '</span>' +
            '<span class="hv-chip"><i style="background:#E0A100"></i>Watch ' + counts.watch + '</span>' +
            '<span class="hv-chip"><i style="background:#22A55B"></i>Healthy ' + counts.ok + '</span>' +
            (counts.nodata ? '<span class="hv-chip"><i style="background:#94A3B8"></i>No data ' + counts.nodata + '</span>' : '') +
            '<span style="flex:1"></span>' +
            '<div class="hv-seg" role="group" aria-label="Filter">' + seg('filter', 'all', 'All') + seg('filter', 'attn', 'Needs attention') +
            (isTrackType() ? seg('filter', 'occ', 'Occupied') : '') + '</div>' +
            '<div class="hv-seg" role="group" aria-label="Sort">' + seg('sort', 'worst', 'Worst first') + seg('sort', 'name', 'Name') + '</div>' +
            (isTrackType() ? '<button type="button" class="hv-tool" data-hv-circall="1" aria-pressed="' + H.circuitAll + '" title="Circuit view on all cards" style="width:auto;padding:0 10px">' +
                '<i class="fa-solid fa-diagram-project"></i> Circuits</button>' : '') +
            '<input type="search" id="hvSearch" class="hv-search" placeholder="Find asset" aria-label="Find asset" value="' + esc(H.q) + '">' +
            '</div>';

        var body;
        if (!ids.length) {
            body = '<div class="hv-empty"><i class="fas fa-satellite-dish" style="font-size:22px;opacity:.5"></i><br>' +
                'Select site and asset type, then Search to start live data.</div>';
        } else if (!shown.length) {
            body = '<div class="hv-empty">Nothing matches this filter.</div>';
        } else {
            body = '<div class="hv-grid">' + shown.map(cardHtml).join('') + '</div>';
        }
        /* keep focus / caret in the search box across rebuilds */
        var act = document.activeElement;
        var hadSearch = act && act.id === 'hvSearch';
        var caret = hadSearch ? act.selectionStart : 0;
        host.innerHTML = bar + body;
        if (hadSearch) {
            var s = document.getElementById('hvSearch');
            if (s) { s.focus(); try { s.setSelectionRange(caret, caret); } catch (e) { /* ignore */ } }
        }
    }

    function onViewClick(e) {
        var t = e.target;
        var b = t.closest ? t.closest('[data-hv-filter],[data-hv-sort],[data-hv-circ],[data-hv-circall],[data-hv-row]') : null;
        if (!b) return;
        if (b.hasAttribute('data-hv-filter')) H.filter = b.getAttribute('data-hv-filter');
        else if (b.hasAttribute('data-hv-sort')) H.sort = b.getAttribute('data-hv-sort');
        else if (b.hasAttribute('data-hv-circall')) { H.circuitAll = !H.circuitAll; H.circuit = {}; }
        else if (b.hasAttribute('data-hv-circ')) {
            var a = b.getAttribute('data-hv-circ');
            if (H.circuitAll) { H.circuitAll = false; }
            H.circuit[a] = !H.circuit[a];
        } else if (b.hasAttribute('data-hv-row')) {
            var aid = b.getAttribute('data-hv-row');
            var k = b.getAttribute('data-hv-k');
            H.trendKey[aid] = (H.trendKey[aid] === k) ? null : k;
        }
        H.sig = '';
        render(true);
    }

    /* ======================================================================
       BOOT
       ====================================================================== */
    function boot() {
        ensureUi();
        var tries = 0;
        var t = setInterval(function () {
            if (ensureUi() || ++tries > 30) clearInterval(t);
        }, 1000);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();

    window.TlHealthView = { enter: enter, leave: leave, isOn: function () { return H.on; }, render: function () { H.sig = ''; render(true); }, _h: H };
})();
