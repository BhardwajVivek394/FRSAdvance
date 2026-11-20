/* ==========================================================================
 *  Telemetry Live -- HEALTH VIEW  (v618.7)
 *  ------------------------------------------------------------------------
 *  A separate view in "Live Asset Data" (4th button next to Card / Grid /
 *  Graph) built from the Live Asset Data health mockups:
 *    - one card per asset, worst-first or name order, filter + search
 *    - every live reading on its SAFE-RANGE bar (low / high / near limit)
 *    - health score ring + status (Healthy / Watch / Fault / No data)
 *    - track circuits: TPR picked-up / dropped pill, relay-end readings not
 *      flagged low while the track is occupied
 *    - CIRCUIT per asset, per attribute (v618.14): drawn here in the style of
 *      the Telemetry Live circuit view (track: rails / TLJB / feed end /
 *      relay end / relay room; other types: one box per attribute / relay),
 *      boxes coloured by health, any number open, live each refresh
 *    - derived track values (calculateDerivedValues: ITC BATT CHARG, IBALST,
 *      VTC VAR RES, RTC CH FEED END, RTC VAR RES, VTC TR, RRAIL)
 *    - Cards | Table: the table shows every reading as a column with its
 *      health bar; click a cell to open circuit + trend under the row
 *    - trend (graph) inside the card: click any reading
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

    /* Safe bands come only from the FRS Attribute Range API (MinSafeValue /
       MaxSafeValue / AverageValue) -- no hard-coded defaults. */
    var TRACK_ORDER = ['CHARGERV', 'CHARGEROPV', 'CHARGERMA', 'IFMA', 'VF', 'CHOKEV', 'VR', 'IRMA', 'TPRV', 'TPRVLOC'];
    var OCC_SUPPRESS = { VR: 1, IRMA: 1, TPRV: 1 };   /* low is normal while a train is on the track */

    var H = {
        on: false, timer: null, prevMode: 'Cards',
        ranges: {}, rangesSite: null, rangeLogged: false,
        sig: '', els: {},
        sort: 'worst', filter: 'all', q: '',
        trendKey: {}, trend: {}, ipsTab: 'all', ipsTableTab: 'all', gopen: {}, circ: {}, circOff: {}, circAll: false, view: 'cards', open: {}
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
    /* IPS battery current in amperes: 3 decimals, same as the IPS cards / table
       (fmt's 2 decimals turned 0.003 A into 0.00) */
    function fmtA(v) { return (v === null || v === undefined || isNaN(v)) ? '--' : Number(v).toFixed(3); }
    function fmtR(r, v) { if (v === undefined) v = r ? r.v : null; return r && r.amp ? fmtA(v) : fmt(v); }
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
    /* Selected type: the dropdown first (same as the IPS cards'
       assetMatchesSelectedType). wsCurrentAssetTypeId can be overwritten by
       other paths (e.g. the circuit view stores the last message's type), which
       made the Health view filter out every asset of the selected type. */
    function assetTypeId() {
        if (H.forType) return H.forType;   /* describe(aid): the asset's own type, not the page filter */
        return parseInt($('#drpAssetType').val() || window.wsCurrentAssetTypeId || 0, 10) || 0;
    }
    /* An asset's OWN type. The drawer can be opened for any asset (e.g. from the
       SIP, which shows every type at once), so the page's selected type is only
       the last resort. Server metadata first: wsLiveData.AssetTypeId may be
       missing on WS messages or back-filled from the page filter. */
    function assetTypeOf(aid, typeHint) {
        aid = String(aid || '');
        var sm = window.sipMetaAll && window.sipMetaAll.assets && window.sipMetaAll.assets[aid];
        var bm = window.bulkAssetMap && window.bulkAssetMap[aid];
        var lv = live()[aid];
        /* typeHint = the caller's own knowledge (e.g. the SIP shape clicked); it
           beats the live record, which may have been back-filled from the page filter */
        var t = (sm && sm.AssetTypeId) || (bm && bm.AssetTypeId) || typeHint || (lv && lv.AssetTypeId) ||
            $('#drpAssetType').val() || window.wsCurrentAssetTypeId || 0;
        return parseInt(t, 10) || 0;
    }
    window.tlAssetTypeOf = assetTypeOf;
    function isTrackType() { return window.tlAssetTypeKind(assetTypeId()) === 'track'; }

    /* ======================================================================
       SAFE RANGES  (FRS attribute range per asset, else RDPMS default)
       ====================================================================== */
    /* the page's site, else the asset's own site (drawer / SIP opened with no site picked) */
    function rangeSite(aid) {
        var s = siteId();
        if ((s && s !== '0') || !aid) return s;
        aid = String(aid);
        var sm = window.sipMetaAll && window.sipMetaAll.assets && window.sipMetaAll.assets[aid];
        var bm = window.bulkAssetMap && window.bulkAssetMap[aid];
        var lv = live()[aid] || {};
        return String((sm && sm.SiteId) || (bm && bm.SiteId) || lv.SiteId || '');
    }
    function loadRanges(aid) {
        var sid = rangeSite(aid);
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
                        /* API fields (Domain.FRSAttributeRange): MinSafeValue / MaxSafeValue / AverageValue / MinFailValue */
                        var mn = pickNum(r, ['MinSafeValue', 'MinValue', 'Min']);
                        var mx = pickNum(r, ['MaxSafeValue', 'MaxValue', 'Max']);
                        var av = pickNum(r, ['AverageValue', 'AvgValue', 'Average']);
                        if (mn === null && mx === null && av === null) continue;
                        var rg = { min: mn, max: mx, avg: av, fail: pickNum(r, ['MinFailValue']), src: 'FRS range' };
                        var aat = r.AssetAttributeId;
                        if (aat !== null && aat !== undefined && aat !== '' && aat !== 0) map[String(r.AssetId) + '_' + aat] = rg;   /* attribute id first */
                        if (name) map[String(r.AssetId) + '|' + norm(name)] = rg;
                        n++;
                    }
                }
                H.ranges = map;
                if (window.console) console.log('[Health] safe ranges for site ' + sid + ': ' + n);
                H.sig = '';
                if (H.on) render(true);
            },
            error: function () { if (H.rangesSite === sid) H.rangesSite = null; /* retry on the next call */ }
        });
    }
    function rangeFor(aid, title, alias, attrId) {
        return (attrId ? H.ranges[aid + '_' + attrId] : null) ||
            H.ranges[aid + '|' + norm(title)] || H.ranges[aid + '|' + norm(alias)] || null;
    }

    /* ======================================================================
       READ ONE ASSET FROM THE LIVE STORE
       ====================================================================== */
    /* { occ, src }: src 'relay' = configured TPR DataLogger relay, 'tprv' = worked out
       from the TPR V reading (< 1 V = occupied). The drawer labels the two differently:
       a TPR V state used to be shown as "TPR Pickup / Drop" on tracks with no TPR relay. */
    function trackOccupiedInfo(e, rows, aid) {
        var dl = e.dlRelays || {};
        for (var k in dl) {
            if (!dl.hasOwnProperty(k)) continue;
            if (norm(dl[k].displayName || k).indexOf('TPR') === -1) continue;
            if (typeof window.tlRelayConfigured === 'function' && !window.tlRelayConfigured(aid, k, dl[k])) continue;
            if (dl[k].isNull) continue;
            return { occ: !dl[k].isPickup, src: 'relay' };
        }
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].key === 'TPRV' && rows[i].v !== null) return { occ: rows[i].v < 1, src: 'tprv' };
        }
        return null;
    }
    /* v618.17 POINT MACHINE: only the readings the PM table / cards show
       (window.buildPmTableRowModel, same model as the PM table):
       per end A / B -- IPT N/R(A) Max + Avg, VPT 110 DC LOC N/R(V),
       TPT N/R(ms), VPT 24 DC LOC N/R(V), VPT N/R(V); DataLogger NWKR /
       RWKR / NWCR / RWCR; direction + last operation time. */
    var PM_DEFS = [['iptMax', 'IPT N/R(A) Max'], ['iptAvg', 'IPT N/R(A) Avg'], ['vpt110Avg', 'VPT 110 DC LOC N/R(V)'],
        ['tpt', 'TPT N/R(ms)'], ['locIndication', 'VPT 24 DC LOC N/R(V)'], ['krIndication', 'VPT N/R(V)']];
    function isPmType() { return window.tlAssetTypeKind(assetTypeId()) === 'point'; }
    function isSignalType() { return window.tlAssetTypeKind(assetTypeId()) === 'signal'; }

    /* ---- Signal: aspect-wise layout (v616 _signalColSeq) ------------------
       Each reading maps to its aspect group in the 616 sequence
       RG, DG, HG, HHG, PILOT, Calling-on, Root, Routes AUG..EUG, On / Off
       aspect, then the proving relays (DPR, HPR, HHPR, route / calling-on
       HPR), and inside a group current (I / mA) before voltage (V), so
       "ISIG RG" and "VSIG RG" always sit side by side. */
    var SIG_LAMPS = ['RG', 'DG', 'HG', 'HHG'];
    var SIG_LAMP_COL = { RG: '#EF4444', DG: '#22C55E', HG: '#F5B70A', HHG: '#F5B70A' };
    function sigInfo(label) {
        var n = strip(label).toUpperCase().replace(/\s+/g, ' ');
        var q = 2, rest = n, m;
        if ((m = /^([IV])(RO|CO)?SIG\b\s*(.*)$/.exec(n))) {           /* ISIG RG, IROSIG (BUG), VCOSIG HPR */
            q = m[1] === 'I' ? 0 : 1;
            rest = (m[2] ? m[2] + ' ' : '') + m[3];
        } else if ((m = /^(.+?)\s*\(?(MA|V)\)?$/.exec(n))) {          /* 616 names: RG mA, Co_Hg V */
            q = m[2] === 'MA' ? 0 : 1;
            rest = m[1];
        }
        var paren = (/\(([A-Z0-9_\- ]+)\)/.exec(rest) || [])[1] || '';
        var core = rest.replace(/\([^)]*\)/g, '').replace(/[_\-]/g, ' ').replace(/\s+/g, ' ').trim();
        var routeOf = function (s) { var r = /\b([A-E])U(G|PR)\b/.exec(s); return r ? r[1] : ''; };
        /* proving relays: DPR / HPR / HHPR, route HPR (xUPR), calling-on HPR, Sh-HPR */
        if (/PR$/.test(core) || /UPR$/.test(paren)) {
            var rt = routeOf(paren), sub;
            if (/^RO\b/.test(core) || rt) sub = 20 + ('ABCDE'.indexOf(rt) + 1);
            else if (/^CO\b/.test(core)) sub = 30;
            else sub = { DPR: 1, HPR: 2, HHPR: 3 }[core.replace(/^SH ?/, '')] || 9;
            return { g: 'PR', name: 'Proving', col: '', o: 100 + sub, q: q };
        }
        var li = SIG_LAMPS.indexOf(core);
        if (li !== -1) return { g: core, name: core, col: SIG_LAMP_COL[core], o: li, q: q };
        if (/^PILOT/.test(core)) return { g: 'PILOT', name: 'Pilot', col: '', o: 4, q: q };
        if (/^CO\b/.test(core)) return { g: 'CO', name: 'Calling-on', col: '#F5B70A', o: 5, q: q };
        if (/^ROOT/.test(core)) return { g: 'ROOT', name: 'Root', col: '', o: 6, q: q };
        var route = routeOf(paren) || routeOf(core);
        if (route || /^RO\b/.test(core)) return { g: 'RO' + route, name: 'Route' + (route ? ' ' + route + 'UG' : ''), col: '', o: 7 + ('ABCDE'.indexOf(route) + 1) / 10, q: q };
        if (/^ON ASPECT/.test(core)) return { g: 'ONASP', name: 'On aspect', col: '', o: 8, q: q };
        if (/^OFF ASPECT/.test(core)) return { g: 'OFFASP', name: 'Off aspect', col: '', o: 8.5, q: q };
        return { g: 'OTHER', name: 'Other', col: '', o: 200, q: q };
    }
    function sigCmp(x, y) {
        return (x.sg.o - y.sg.o) || (x.sg.q - y.sg.q) || x.label.localeCompare(y.label, undefined, { numeric: true });
    }
    /* signals with the same aspect make-up share one section, labelled like 616:
       "3 Aspect + Route + Calling-on", "Shunt Signal" */
    function sigSections(shown) {
        var by = {}, out = [];
        shown.forEach(function (A) {
            var gs = {}, lamps = 0, route = false, co = false;
            A.rows.forEach(function (r) {
                var s = r.sg || sigInfo(r.label);
                if (gs[s.g]) return;
                gs[s.g] = 1;
                if (SIG_LAMPS.indexOf(s.g) !== -1) lamps++;
                if (s.g.indexOf('RO') === 0 && s.g !== 'ROOT') route = true;
                if (s.g === 'CO') co = true;
            });
            var shunt = /^\s*SH\b|^\s*SH[\s\-_]?\d/i.test(A.name);
            var label = shunt ? 'Shunt Signal'
                : (lamps ? lamps + ' Aspect' : 'Signal') + (route ? ' + Route' : '') + (co ? ' + Calling-on' : '');
            var key = (shunt ? 'SH|' : '') + Object.keys(gs).sort().join(',');
            if (!by[key]) { by[key] = { key: key, label: label, shunt: shunt, lamps: lamps, route: route, co: co, list: [] }; out.push(by[key]); }
            by[key].list.push(A);
        });
        out.sort(function (a, b) {
            return (a.shunt - b.shunt) || (a.lamps - b.lamps) || (a.route - b.route) || (a.co - b.co) || a.key.localeCompare(b.key);
        });
        return out;
    }
    /* Signal groups in ONE continuous grid (same group order) so a small group
       no longer leaves the rest of its row empty; each card carries its group
       tag and one summary line above lists the groups with their counts. */
    function sigGridHtml(sections) {
        var sum = '<div class="hv-grpsum">' + sections.map(function (S) {
            return '<span><b>' + esc(S.label) + '</b> ' + S.list.length + '</span>';
        }).join('<i aria-hidden="true">&middot;</i>') + '</div>';
        var cards = '';
        sections.forEach(function (S) {
            S.list.forEach(function (A) { cards += cardHtml(A, S.label); });
        });
        return sum + '<div class="hv-grid">' + cards + '</div>';
    }
    function secHead(S) {
        return '<div class="hv-sec"><b>' + esc(S.label) + '</b><span>' + S.list.length + ' signal' + (S.list.length > 1 ? 's' : '') + '</span></div>';
    }
    /* v618.25 IPS: readings split into Voltage / Current / Digital (same rule as
       the IPS table tabs: ipsClassifyTab -- IIPS = current, DataLogger = digital) */
    function isIpsType() { try { return typeof window.isIpsAssetType === 'function' && !!window.isIpsAssetType(); } catch (e) { return false; } }
    function ipsTabOf(aid, key, label) {
        var a = live()[aid] || {};
        if (a.dlRelays && a.dlRelays[key]) return 'digital';
        if (typeof window.ipsClassifyTab === 'function') { try { return window.ipsClassifyTab(false, key, label, a.AssetName); } catch (e) { /* fall through */ } }
        return /IIPS/i.test(String(key) + ' ' + String(label)) ? 'current' : 'voltage';
    }
    function pmNum(t) { var m = /-?\d+(\.\d+)?/.exec(String(t == null ? '' : t)); return m ? parseFloat(m[0]) : null; }
    /* The attributes behind each PM_DEFS reading, as [Normal, Reverse] ids per end.
       IPT and TPT both come from the end's current attribute. */
    var PM_ATTR_IDS = {
        A: { iptMax: [217, 219], iptAvg: [217, 219], tpt: [217, 219], vpt110Avg: [216, 218], locIndication: [576, 577], krIndication: [25, 26] },
        B: { iptMax: [213, 215], iptAvg: [213, 215], tpt: [213, 215], vpt110Avg: [212, 214], locIndication: [578, 579], krIndication: [27, 28] }
    };
    /* Only what this asset has: the end must exist and either direction's attribute
       must be mapped. Metadata not loaded yet leaves the reading in (pmAssetHasAttr). */
    function pmHasReading(aid, E, key) {
        if (typeof window.pmAssetHasEnd === 'function' && !window.pmAssetHasEnd(aid, E)) return false;
        if (typeof window.pmAssetHasAttr !== 'function') return true;
        var ids = PM_ATTR_IDS[E][key];
        return window.pmAssetHasAttr(aid, ids[0]) || window.pmAssetHasAttr(aid, ids[1]);
    }
    function readPmModel(aid) {
        if (!isPmType() || typeof window.buildPmTableRowModel !== 'function') return null;
        var m = null;
        try { m = window.buildPmTableRowModel(aid); } catch (x) { m = null; }
        if (!m) return null;
        var rows = [];
        ['A', 'B'].forEach(function (E) {
            var f = m[E];
            if (!f) return;
            /* a reading with a live value is always shown (same as the PM table): a
               site may map the end's current under other ids than PM_ATTR_IDS (e.g.
               B end without 213 / 215 while IPT / TPT still arrive); the id check
               only hides readings that have no value */
            var defs = PM_DEFS.filter(function (d) { return (f[d[0]] && f[d[0]].present) || pmHasReading(aid, E, d[0]); });
            var any = false;
            for (var i = 0; i < defs.length; i++) if (f[defs[i][0]] && f[defs[i][0]].present) any = true;
            if (!any) return;
            var rv = (m.operation && m.operation.direction === 'Reverse') ? 1 : 0;
            defs.forEach(function (d) {
                var en = f[d[0]] || {};
                var label = E + ' End ' + d[1];
                /* safe range by the end's channel id for the operation's direction (was by name only) */
                /* TPT shares the current channel id but is in ms: never the IPT range */
                var rid = (d[0] !== 'tpt' && PM_ATTR_IDS[E][d[0]]) ? PM_ATTR_IDS[E][d[0]][rv] : '';
                rows.push({
                    k: 'PM.' + E + '.' + d[0], key: norm(label), attrId: '', title: label, alias: label, label: label, sub: '',
                    v: en.present ? num(pmNum(en.text)) : null, stale: false, scaleMin: null, scaleMax: null,
                    range: rangeFor(aid, label, label, rid) || rangeFor(aid, label, label)
                });
            });
        });
        var relays = [];
        ['NWKR', 'RWKR', 'NWCR', 'RWCR'].forEach(function (k) {
            var r = m.datalogger && m.datalogger[k];
            if (r) relays.push({ name: k, up: !!r.isPickup, combined: /^combined/i.test(String(r.displayName || '')) });
        });
        return { rows: rows, relays: relays, dir: (m.operation && m.operation.direction) || '', when: m.operationDate || '' };
    }
    function readAsset(aid) {
        var e = live()[aid] || {};
        var attrs = e.attrs || {};
        var meta = window.userAssetSimpleMap || {};
        var byName = window.assetAttributeByName || {};
        var pmm = readPmModel(aid);
        var rows = pmm ? pmm.rows : [];
        /* one row per attribute: the SIP mirror (sip-telemetry.js mirrorToShared) and the
           bulk seed store the same attribute object under its raw name AND its alias */
        var seenObj = [];
        for (var k in (pmm ? {} : attrs)) {
            if (!attrs.hasOwnProperty(k)) continue;
            if (attrs[k] && typeof attrs[k] === 'object') {
                if (seenObj.indexOf(attrs[k]) !== -1) continue;
                seenObj.push(attrs[k]);
            }
            var a = attrs[k] || {};
            var attrId = String(a.AttrId || a.AssetAttributeId || '');
            /* userAssetSimpleMap holds only the page's selected type; the SIP's
               all-types store covers an asset of any other type */
            var m = meta[aid + '_' + attrId] ||
                (window.sipMetaAll && window.sipMetaAll.simple && window.sipMetaAll.simple[aid + '_' + attrId]) || {};
            var title = strip(m.attributeName || m.AttributeName || k);
            /* v618.39: API AliasName (GetUserAssetInfo, any asset type) before the raw WebSocket key */
            var apiAlias = typeof window.tlAliasOf === 'function' ? window.tlAliasOf(aid, attrId, title) || window.tlAliasOf(aid, '', k) : '';
            var alias = strip(m.aliasName || m.AliasName || apiAlias || m.name || byName[k] || k);
            var v = num(a.Value);
            var rg = rangeFor(aid, title, alias, attrId), lbl = alias || title;
            var smin = num(m.minValue), smax = num(m.maxValue), isAmp = false;
            /* IPS battery current (CHARGING / DISCHARGING): same value as the IPS cards and
               table -- amperes (raw / IPS_BATT_MA_PER_AMP) -- with the safe range / scale
               converted alike and "(A)" on the label. The Health view showed the raw value. */
            if (!pmm && isIpsType() && v !== null && typeof window.ipsCardDisplayValue === 'function' && typeof window.ipsFormatDisplayValue === 'function') {
                var dv = window.ipsCardDisplayValue(aid, k, a), rawDv = window.ipsFormatDisplayValue(a.Value, '');
                var div = parseFloat(window.IPS_BATT_MA_PER_AMP);
                if (dv !== rawDv && !isNaN(parseFloat(dv)) && div > 0) {
                    v = num(parseFloat(dv)); isAmp = true;
                    var sc =function (x) { return (x === null || x === undefined || isNaN(x)) ? x : x / div; };
                    if (rg) rg = { min: sc(rg.min), max: sc(rg.max), avg: sc(rg.avg), fail: sc(rg.fail), src: rg.src };
                    smin = sc(smin); smax = sc(smax);
                    if (typeof window.ipsBattAmpLabel === 'function') lbl = window.ipsBattAmpLabel(lbl);
                }
            }
            /* a reading shown in amperes IS a current (the IPS card rule) even when its
               alias dropped the IIPS marker; a DataLogger attribute is Digital */
            var tabOf = isAmp ? 'current' : ipsTabOf(aid, k, title + ' ' + alias + ' ' + k);
            if (String(a.DataType || '').trim().toLowerCase() === 'datalogger') tabOf = 'digital';
            rows.push({
                k: k, key: norm(title), attrId: attrId, title: title, alias: alias,
                label: lbl, sub: '',   /* v618.16: AliasName only, everywhere */
                v: v, amp: isAmp, stale: a.IsFresh === false, ipsTab: tabOf,
                scaleMin: smin, scaleMax: smax,
                range: rg
            });
        }
        /* IPS: data-logger relays are the DIGITAL readings (e7mriv2web IPS
           Digital tab) -- add them as rows (Pickup / Drop) so the Digital tab
           of the table and cards shows values, not just relay pills. */
        if (!pmm && isIpsType() && e.dlRelays) {
            var haveRow = {};
            rows.forEach(function (x) { haveRow[x.k] = 1; });
            Object.keys(e.dlRelays).forEach(function (rk) {
                var rl = e.dlRelays[rk];
                if (!rl || haveRow[rk]) return;
                var nm = strip(rl.displayName || rk);
                rows.push({
                    k: rk, key: norm(nm), attrId: '', title: nm, alias: nm, label: nm, sub: '',
                    v: rl.isNull ? null : (rl.isPickup ? 1 : 0), relay: true, up: !!rl.isPickup,
                    stale: false, ipsTab: 'digital', scaleMin: null, scaleMax: null, range: null
                });
            });
            /* an attribute that IS a data-logger relay belongs to Digital too */
            rows.forEach(function (x) {
                var ad = attrs[x.k];
                if (!x.relay && ad && String(ad.DataType || '').toLowerCase() === 'datalogger') x.ipsTab = 'digital';
            });
        }
        /* Configured-only binding (Track, same rule as the Telemetry Live table,
           cards and drawer): keep only RDPMS attributes the asset's
           GetBulkAssetMetadata assetAttributes lists. tlAssetHasRdpmsAttr
           returns true for assets without bulk config / non-track types. */
        var cfgHasRdpms = typeof window.tlAssetHasRdpms !== 'function' || window.tlAssetHasRdpms(aid);
        var cfgHasDl = typeof window.tlAssetHasDatalogger !== 'function' || window.tlAssetHasDatalogger(aid);
        if (!pmm && typeof window.tlAssetHasRdpmsAttr === 'function') {
            rows = rows.filter(function (x) {
                if (x.relay) return cfgHasDl;
                return window.tlAssetHasRdpmsAttr(aid, x.k, x.attrId) || window.tlAssetHasRdpmsAttr(aid, x.title, x.attrId);
            });
        }
        if (isTrackType()) {
            rows.sort(function (x, y) {
                var ix = TRACK_ORDER.indexOf(x.key), iy = TRACK_ORDER.indexOf(y.key);
                if (ix < 0) ix = 99;
                if (iy < 0) iy = 99;
                return ix - iy || x.label.localeCompare(y.label);
            });
        } else if (!pmm && isSignalType()) {
            rows.forEach(function (x) { x.sg = sigInfo(x.label); });
            rows.sort(sigCmp);
        } else if (!pmm) {
            rows.sort(function (x, y) { return x.label.localeCompare(y.label, undefined, { numeric: true }); });
        }
        var occInfo = isTrackType() ? trackOccupiedInfo(e, rows, aid) : null;
        var occ = occInfo ? occInfo.occ : null, occSrc = occInfo ? occInfo.src : null;
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
                r.noBar = (r.scaleMax === null);   /* no range and no scale: a bar would mean nothing */
            }
            if (r.st === 'low' || r.st === 'high') outs++;
            else if (r.st === 'near') near++;
            r.pos = Math.max(1, Math.min(99, ((r.v - r.lo) / (r.hi - r.lo)) * 100));
        }
        var relays = pmm ? pmm.relays : [];
        var dl = pmm ? {} : (cfgHasDl ? (e.dlRelays || {}) : {});   /* no relays for an asset with no DL configured */
        for (var dk in dl) {
            if (!dl.hasOwnProperty(dk)) continue;
            /* only the relays this asset's config lists (drawer from the SIP included) */
            if (typeof window.tlRelayConfigured === 'function' && !window.tlRelayConfigured(aid, dk, dl[dk])) continue;
            relays.push({ name: dl[dk].displayName || dk, up: !!dl[dk].isPickup });
        }
        /* DL-only asset (no RDPMS configured): its relays ARE its live data, so
           it is not "No data" -- healthy while it reports, no readings or
           calculated values to show. */
        var dlOnly = !pmm && isTrackType() && !cfgHasRdpms && relays.length > 0;
        if (dlOnly) have = relays.length;
        var status = !have ? 'nodata' : outs >= 3 ? 'fault' : outs ? 'watch' : 'ok';
        var score = !have ? 0 : Math.max(0, Math.round(100 - outs * 18 - near * 6 - stale * 4));
        var derived = [];
        if (isTrackType() && cfgHasRdpms && typeof window.calculateDerivedValues === 'function') {
            var dv = null;
            try { dv = window.calculateDerivedValues(attrs, aid); } catch (ex) { dv = null; }
            if (dv) {
                /* [calc field, key, label, FRS AssetAttributeId (API: ITC BATT CHARG 585, VTC VAR RES 586,
                   RTC CH FEED END 587, RTC VAR RES 588, IBlAST 684, RRAIL 590)] */
                var DL = [['itcBattCharg', 'ITCBATTCHARG', 'ITC BATT CHARG(mA)', 585], ['vtcVarRes', 'VTCVARRES', 'VTC VAR RES(V)', 586],
                    ['rtcChFeedEnd', 'RTCCHFEEDEND', 'RTC CH FEED END(Ohm)', 587], ['rtcVarRes', 'RTCVARRES', 'RTC VAR RES(Ohm)', 588],
                    ['vtcTr', 'VTCTR', 'VTC TR(V)', ''], ['ibalst', 'IBALST', 'IBALST(mA)', 684], ['rrail', 'RRAIL', 'RRAIL(Ohm)', 590]];
                for (var di = 0; di < DL.length; di++) {
                    var dvv = dv[DL[di][0]];
                    derived.push({ k: '#' + DL[di][1], key: DL[di][1], label: DL[di][2], alias: DL[di][2], sub: '',
                        v: (dvv === null || dvv === undefined || isNaN(dvv)) ? null : num(dvv), st: 'none', derived: true,
                        range: rangeFor(aid, DL[di][1], DL[di][2], DL[di][3]) });
                }
            }
        }
        return {
            aid: aid, name: e.AssetName || ('Asset ' + aid), rows: rows, derived: derived, occ: occ, occSrc: occSrc, outs: outs, near: near,
            pmDir: pmm ? pmm.dir : '', pmWhen: pmm ? pmm.when : '',
            stale: stale, have: have, status: status, score: score, relays: relays, dlOnly: dlOnly,
            sev: (status === 'fault' ? 3000 : status === 'watch' ? 2000 : status === 'nodata' ? 0 : 1000) + outs * 50 + near * 10 + stale
        };
    }
    function assetIds() {
        var ld = live();
        var at = assetTypeId();
        var ids = [];
        var ips = isIpsType();
        for (var aid in ld) {
            if (!ld.hasOwnProperty(aid)) continue;
            var e = ld[aid];
            if (!e || !e.attrs) continue;
            /* only assets the asset API returned: an entry created straight from the
               WebSocket (incl. the SIP schematic's mirror) is never a card on its own */
            if (typeof window.isAssetInBulkWhitelist === 'function' && !window.isAssetInBulkWhitelist(aid)) continue;
            if (ips) {
                /* IPS: exactly the assets the IPS cards show (bulk whitelist +
                   assetMatchesSelectedType, which also falls back to the bulk
                   metadata type) so the Health view never drops one */
                if (typeof window.isAssetInBulkWhitelist === 'function' && !window.isAssetInBulkWhitelist(aid)) continue;
                if (typeof window.assetMatchesSelectedType === 'function' && !window.assetMatchesSelectedType(aid)) continue;
            } else if (at && e.AssetTypeId && parseInt(e.AssetTypeId, 10) !== at) continue;
            ids.push(aid);
        }
        return ids;
    }

    /* ======================================================================
       CSS
       ====================================================================== */
    var CSS = '' +
        '#tlHealthView .hv-rv.up{color:var(--e7-green, #10b981) !important;font-weight:700;} #tlHealthView .hv-rv.dn{color:var(--e7-amber, #f59e0b) !important;font-weight:700;}' +
        '.at-table-card.tl-health-on #atCardView,.at-table-card.tl-health-on .at-table-scroll{display:none !important;}' +
        '#tlHealthView{display:none;padding:14px 16px 18px;box-sizing:border-box;width:100%;color:var(--e7-heading,inherit);}' +
        '.at-table-card.tl-health-on #tlHealthView{display:block;}' +
        '.tl-vmode-hbtn{width:28px;height:26px;display:inline-flex;align-items:center;justify-content:center;background:transparent;' +
        'border:1px solid transparent;border-radius:6px;color:var(--text-3,#8D9CB2);font-size:12px;cursor:pointer;padding:0;}' +
        '.tl-vmode-hbtn:hover{color:var(--text-1,inherit);background:var(--glass-2,rgba(127,140,160,.15));}' +
        '.tl-vmode-hbtn.active{background:linear-gradient(180deg,rgba(34,211,238,0.20),rgba(34,211,238,0.08));border-color:var(--e7-accent, rgba(34,211,238,0.40));color:var(--brand,#22d3ee);}' +
        '.hv-bar{display:flex;flex-wrap:wrap;align-items:center;gap:8px 14px;margin-bottom:12px;}' +
        '.hv-chip{display:inline-flex;align-items:center;gap:6px;font-size:12.5px;font-weight:600;padding:4px 10px;border-radius:14px;border:1px solid var(--e7-border,rgba(127,140,160,.3));}' +
        '.hv-chip i{width:8px;height:8px;border-radius:50%;display:inline-block;}' +
        '.hv-seg{display:inline-flex;padding:2px;border-radius:9px;border:1px solid var(--e7-border,rgba(127,140,160,.3));background:var(--e7-surface-2,transparent);gap:2px;}' +
        '.hv-seg button{height:26px;padding:0 10px;border:none;border-radius:7px;background:transparent;color:var(--e7-text-2,inherit);font:inherit;font-size:12px;cursor:pointer;}' +
        '.hv-seg button[aria-pressed="true"]{background:var(--brand,#22d3ee);color:#04161A;font-weight:600;}' +
        '.hv-search{height:30px;border-radius:15px;border:1px solid var(--e7-border,rgba(127,140,160,.3));background:var(--e7-surface-2,transparent);color:var(--e7-heading,inherit);padding:0 12px;font:inherit;font-size:12.5px;min-width:180px;}' +
        '.hv-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(330px,1fr));gap:14px;align-items:start;}' +
        '.hv-card{border:1px solid var(--e7-border,rgba(127,140,160,.25));border-radius:14px;background:var(--e7-surface-2,rgba(127,140,160,.05));padding:12px 14px 12px;}' +
        '.hv-card.st-fault{box-shadow:inset 0 3px 0 #E5484D;border-color:rgba(229,72,77,.45);}' +
        '.hv-card.st-watch{box-shadow:inset 0 3px 0 #E0A100;border-color:rgba(224,161,0,.45);}' +
        '.hv-head{display:flex;align-items:center;gap:10px;}' +
        '.hv-pill{display:inline-flex;align-items:center;height:26px;padding:0 11px;border-radius:13px;font-weight:700;font-size:13.5px;color:#fff;background:#64748b;white-space:nowrap;}' +
        '.hv-pill.clear{background:#1F9D55;} .hv-pill.occ{background:#DC2626;} .hv-pill.rev{background:#D97706;}' +
        '.hv-ring{flex:none;}' +
        '.hv-ttl{flex:1;min-width:0;}' +
        '.hv-status{font-size:12.5px;font-weight:700;}' +
        '.hv-status.ok{color:var(--e7-green,#1F9D55);} .hv-status.watch{color:var(--e7-amber,#C98A00);} .hv-status.fault{color:var(--e7-red,#E5484D);} .hv-status.nodata{color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-sum{font-size:11.5px;color:var(--e7-text-3,#8D9CB2);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-tools{display:flex;gap:4px;}' +
        '.hv-tool{width:28px;height:28px;border-radius:7px;border:1px solid var(--e7-border,rgba(127,140,160,.3));background:transparent;color:var(--e7-text-2,inherit);cursor:pointer;font-size:12px;}' +
        '.hv-tool[aria-pressed="true"]{color:var(--brand,#22d3ee);border-color:var(--brand,#22d3ee);}' +
        '.hv-relays{display:flex;flex-wrap:wrap;gap:4px;margin:8px 0 2px;}' +
        '.hv-relay{font-size:11px;font-weight:700;padding:1px 7px;border-radius:4px;border:1px solid var(--e7-border,rgba(127,140,160,.3));}' +
        '.hv-relay.up{color:var(--e7-green,#1F9D55);border-color:rgba(31,157,85,.45);background:rgba(31,157,85,.08);} .hv-relay.dn{color:var(--e7-amber,#B88700);border-color:rgba(234,179,8,.6);background:rgba(234,179,8,.12);}' +
        '.hv-rows{margin-top:8px;display:flex;flex-direction:column;gap:1px;}' +
        '.hv-row{display:grid;grid-template-columns:minmax(0,1fr) 74px 44px 84px;align-items:center;gap:8px;padding:3px 6px;border-radius:6px;cursor:pointer;}' +
        '.hv-row:hover{background:rgba(127,140,160,.10);}' +
        '.hv-row.sel{box-shadow:inset 0 0 0 1.5px var(--brand,#22d3ee);}' +
        '.hv-row .lb{font-size:12px;color:var(--e7-text-2,inherit);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-row .lb small{color:var(--e7-text-3,#8D9CB2);margin-left:4px;}' +
        '.hv-row .vl{text-align:right;font-weight:700;font-size:12.5px;font-variant-numeric:tabular-nums;}' +
        '.hv-row .fl{font-size:10.5px;font-weight:700;color:var(--e7-text-3,#8D9CB2);white-space:nowrap;}' +
        '.hv-bar2{position:relative;height:12px;}' +
        '.hv-bar2 .tr{position:absolute;left:0;right:0;top:5px;height:2px;border-radius:2px;background:rgba(127,140,160,.30);}' +
        '.hv-bar2 .bd{position:absolute;top:3px;height:6px;border-radius:3px;background:rgba(31,157,85,.30);}' +
        '.hv-bar2 .mk{position:absolute;top:0;width:3px;height:12px;border-radius:2px;background:var(--e7-heading,#13202E);margin-left:-1.5px;}' +
        '.hv-card.hv-wide{grid-column:span 2;}' +
        '@media (max-width:1100px){.hv-card.hv-wide{grid-column:1 / -1;}}' +
        '.hv-circ{margin-top:8px;border-radius:10px;overflow:hidden;border:1px solid var(--e7-border,rgba(127,140,160,.25));}' +
        '.hv-circ svg{display:block;width:100%;height:auto;font-family:inherit;}' +
        '.hv-circ-head{display:flex;flex-wrap:wrap;align-items:center;gap:6px 10px;padding:6px 10px;font-size:12px;border-bottom:1px solid var(--e7-border,rgba(127,140,160,.25));}' +
        '.hv-circ-live{display:inline-flex;align-items:center;gap:4px;padding:1px 7px;border-radius:4px;background:#16A34A;color:#fff;font-weight:700;font-size:11px;}' +
        '.hv-circ-live i{width:7px;height:7px;border-radius:50%;background:var(--e7-surface, #fff);}' +
        '.hv-circ-where,.hv-circ-ts{color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-circ-zoom{margin-left:auto;display:inline-flex;gap:4px;}' +
        '.hv-circ-zoom button{min-width:28px;height:24px;padding:0 6px;border:1px solid var(--e7-border,rgba(127,140,160,.45));border-radius:4px;background:transparent;color:inherit;font-weight:700;cursor:pointer;}' +
        '.hv-circ-pan{overflow:auto;}' +
        '.hv-circ .hv-zoomed svg{height:auto!important;width:100%!important;}' +
        '.hv-derived{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:2px 14px;margin-top:6px;padding-top:6px;border-top:1px solid var(--e7-border,rgba(127,140,160,.25));font-size:11.5px;}' +
        '.hv-derived span{display:flex;justify-content:space-between;gap:6px;color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-derived em{font-style:normal;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-derived b{color:var(--e7-heading,inherit);font-variant-numeric:tabular-nums;}' +
        '.hv-tblwrap{overflow:auto;border:1px solid var(--e7-border,rgba(127,140,160,.25));border-radius:12px;max-height:75vh;}' +
        '.hv-tbl{border-collapse:separate;border-spacing:0;width:100%;font-size:12.5px;}' +
        '.hv-tbl th,.hv-tbl td{padding:4px 6px;border-bottom:1px solid var(--e7-border,rgba(127,140,160,.18));text-align:left;vertical-align:middle;white-space:nowrap;}' +
        '.hv-circ svg text{font-family:inherit;}' +
        '.hv-tbl thead th{position:sticky;top:0;z-index:2;background:var(--hv-bg,#fff);font-weight:600;color:var(--e7-text-2,inherit);vertical-align:bottom;}' +
        '.hv-tbl thead th small{display:block;font-weight:500;font-size:10.5px;color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-tbl thead th small.rg{color:var(--e7-green,#1F9D55);}' +
        '.hv-tbl th.dv,.hv-tbl td.dv{color:var(--e7-text-3,#8D9CB2);font-variant-numeric:tabular-nums;}' +
        '.hv-tbl .hv-sticky{position:sticky;left:0;z-index:1;background:var(--hv-bg,#fff);}' +
        '.hv-tbl thead .hv-sticky{z-index:3;}' +
        '.hv-tbl tr.st-fault > th.hv-sticky{box-shadow:inset 3px 0 0 #E5484D;}' +
        '.hv-tbl tr.st-watch > th.hv-sticky{box-shadow:inset 3px 0 0 #E0A100;}' +
        '.hv-tbl .hv-x{width:22px;height:22px;border:none;background:transparent;color:var(--e7-text-2,inherit);cursor:pointer;margin-right:4px;}' +
        '.hv-tbl tr.open .hv-x i{transform:rotate(90deg);}' +
        '.hv-hcell{display:flex;align-items:center;gap:6px;}' +
        '.hv-rl .hv-relay{margin-right:3px;}' +
        '.hv-cell{display:block;width:100%;min-width:86px;border:none;border-radius:6px;padding:3px 6px;background:transparent;color:inherit;font:inherit;text-align:left;cursor:pointer;}' +
        '.hv-cell .v{display:flex;justify-content:space-between;gap:4px;font-weight:700;font-variant-numeric:tabular-nums;}' +
        '.hv-cell .v i{font-style:normal;font-size:10px;}' +
        '.hv-cell.st-near{box-shadow:inset 0 0 0 1px rgba(224,161,0,.45);}' +
        '.hv-cell.sel{box-shadow:inset 0 0 0 1.5px var(--brand,#22d3ee);}' +
        '.hv-cell .hv-bar2{display:block;height:8px;margin-top:2px;} .hv-cell .hv-bar2 .tr{top:3px;} .hv-cell .hv-bar2 .bd{top:2px;height:4px;} .hv-cell .hv-bar2 .mk{height:8px;}' +
        '.hv-na{color:var(--e7-text-3,#8D9CB2);}' +
        /* v618.29 gauge + reading tiles */
        '.hv-g{position:relative;height:10px;margin:6px 0 2px;}' +
        '.hv-g .tr{position:absolute;left:0;right:0;top:3px;height:4px;border-radius:3px;background:linear-gradient(90deg,rgba(224,161,0,.55) 0%,rgba(31,157,85,.38) 22%,rgba(31,157,85,.38) 78%,rgba(229,72,77,.45) 100%);}' +
        '.hv-g .av{position:absolute;top:1px;width:1.5px;height:8px;margin-left:-.75px;background:rgba(100,116,139,.85);}' +
        '.hv-g .mk{position:absolute;top:-2px;width:5px;height:14px;margin-left:-2.5px;border-radius:2px;background:var(--e7-heading,#13202E);box-shadow:0 0 0 1.5px var(--hv-bg,#fff);}' +
        '.hv-g .mk.st-near{background:#E0A100;}' +
        '.hv-gl{display:flex;justify-content:space-between;font:10px "JetBrains Mono",monospace;color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-tiles{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:6px;}' +
        '.hv-tile{border:1px solid var(--e7-border,rgba(127,140,160,.25));border-radius:10px;padding:7px 9px 6px;cursor:pointer;background:var(--e7-surface-2,transparent);}' +
        '.hv-tile:hover{border-color:var(--brand,#22d3ee);}' +
        '.hv-tile.sel{box-shadow:0 0 0 1.5px var(--brand,#22d3ee);}' +
        '.hv-tile .hd{display:flex;align-items:center;gap:6px;}' +
        '.hv-tile .lb{flex:1;font-size:12px;color:var(--e7-text-2,inherit);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.hv-tile .bg{font:700 10px "JetBrains Mono",monospace;padding:1px 6px;border-radius:4px;background:rgba(127,140,160,.15);color:var(--e7-text-2,inherit);}' +
        '.hv-tile .vl{font:700 18px "JetBrains Mono",monospace;margin-top:2px;color:var(--e7-heading,inherit);font-variant-numeric:tabular-nums;}' +
        '.hv-tile .sl{font-size:11px;font-weight:600;color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-tile.st-near{border-color:rgba(224,161,0,.55);background:rgba(224,161,0,.07);}' +
        '.hv-tile.st-near .bg{background:rgba(224,161,0,.22);color:var(--e7-amber,#9A6A00);}' +
        '.hv-tile .hv-stale-ic{flex:none;font-size:12px;color:var(--e7-amber,#B47B00);margin:1px 4px 0 auto;cursor:help;}' +
        '.hv-tile .hv-stale-ic + .bg{margin-left:0;}' +
        /* Signal aspect-wise (616): aspect blocks in the card, sections, table aspect row */
        '.hv-asps{margin-top:8px;display:flex;flex-direction:column;gap:6px;}' +
        '.hv-agrp th span i{display:inline-block;width:8px;height:8px;border-radius:50%;box-shadow:0 0 0 2px rgba(127,140,160,.18);}' +
        '.hv-tiles.hv-pair{grid-template-columns:repeat(2,minmax(0,1fr));}' +
        '.hv-sec{display:flex;align-items:center;gap:8px;margin:16px 2px 8px;}' +
        '.hv-sec:first-child{margin-top:4px;}' +
        '.hv-grpsum{display:flex;flex-wrap:wrap;align-items:center;gap:4px 8px;margin:4px 2px 10px;font-size:12px;color:var(--e7-text-2,inherit);}' +
        '.hv-grpsum b{font-weight:700;color:var(--e7-heading,inherit);}' +
        '.hv-grpsum i{font-style:normal;opacity:.5;}' +
        '.hv-grptag{display:inline-block;vertical-align:middle;margin-left:6px;max-width:150px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:10.5px;font-weight:600;padding:2px 8px;border-radius:999px;background:rgba(127,140,160,.15);color:var(--e7-text-2,inherit);}' +
        '.hv-sec b{font-size:13px;font-weight:700;color:var(--e7-heading,inherit);}' +
        '.hv-sec span{font-size:10.5px;font-weight:600;padding:2px 9px;border-radius:999px;background:rgba(127,140,160,.15);color:var(--e7-text-2,inherit);}' +
        '.hv-tbl thead tr.hv-agrp th{position:static;text-align:center;font-size:10.5px;font-weight:700;letter-spacing:.4px;text-transform:uppercase;border-bottom:0;padding-bottom:0;}' +
        '.hv-tbl thead tr.hv-agrp th span{display:inline-flex;align-items:center;gap:5px;padding:2px 10px;border-radius:999px;background:rgba(127,140,160,.10);}' +
        '.hv-cell .hv-g{margin:4px 0 0;}' +
        '.hv-dir{display:inline-flex;align-items:center;gap:3px;font-size:11.5px;font-weight:800;padding:1px 8px;border-radius:999px;margin-left:4px;}' +
        '.hv-dir.nor{color:var(--e7-green,#1F9D55);background:rgba(31,157,85,.12);border:1px solid rgba(31,157,85,.45);}' +
        '.hv-dir.rev{color:var(--e7-amber,#B88700);background:rgba(234,179,8,.14);border:1px solid rgba(234,179,8,.55);}' +
        '.hv-when{display:block;font-size:10.5px;color:var(--e7-text-3,#8D9CB2);margin-top:2px;}' +
        /* v618.28 Graph view hover: value held since / until */
        '.tlgv-since{font-size:10px;color:var(--e7-text-3,#8D9CB2);margin-top:1px;font-family:"JetBrains Mono",monospace;}' +
        '.tlgv-since-bar{margin-left:10px;color:var(--e7-text-2,inherit);font-size:11.5px;}' +
        '.hv-tabs{display:flex;gap:2px;border-bottom:1px solid var(--e7-border,rgba(127,140,160,.25));margin-bottom:8px;}' +
        '.hv-tabs button{border:none;background:transparent;color:var(--e7-text-2,inherit);font:inherit;font-weight:600;font-size:13px;padding:7px 14px;cursor:pointer;border-bottom:2px solid transparent;}' +
        '.hv-tabs button[aria-selected="true"]{color:var(--e7-heading,inherit);border-bottom-color:var(--brand,#22d3ee);}' +
        '.hv-tbl tr.hv-grp > th,.hv-tbl tr.hv-grp > td{background:rgba(127,140,160,.08);}' +
        '.hv-tbl tr.hv-grp.open .hv-x i{transform:rotate(90deg);}' +
        '.hv-gsub{display:block;font-size:10.5px;color:var(--e7-text-3,#8D9CB2);margin:2px 0 0 30px;font-weight:500;}' +
        '.hv-sumcell b{font-variant-numeric:tabular-nums;} .hv-sumcell small{margin-left:6px;color:var(--e7-text-3,#8D9CB2);font-size:10.5px;}' +
        '.hv-tbl tr.hv-child > th.hv-sticky{padding-left:22px;}' +
        '.hv-tbl tr.hv-child > th,.hv-tbl tr.hv-child > td{background:rgba(127,140,160,.03);}' +
        '.hv-exp > td{background:var(--e7-surface-2,rgba(127,140,160,.05));white-space:normal;}' +
        '.hv-expin{display:flex;flex-wrap:wrap;gap:12px;padding:6px 4px 10px;position:sticky;left:0;box-sizing:border-box;}' +
        '.hv-expc{flex:2 1 520px;min-width:0;} .hv-expt{flex:1 1 320px;min-width:0;display:flex;}' +
        /* v618.25: trend as tall as the circuit next to it */
        '.hv-expin{align-items:stretch;}' +
        '.hv-expt .hv-trend{flex:1;display:flex;flex-direction:column;margin-top:8px;}' +
        '.hv-expt .hv-trend svg{flex:1;height:auto;min-height:200px;}' +
        /* v618.30: circuit + trend sized to fit inside the table viewport (row + panel visible together) */
        '.hv-expc{flex:3 1 520px;} .hv-expt{flex:2 1 320px;}' +
        '.hv-expc .hv-circ{background:var(--hv-bg,#fff);}' +
        '.hv-expc .hv-circ svg{width:100%;height:clamp(260px,calc(75vh - 170px),520px);}' +
        '.hv-expt .hv-trend{box-sizing:border-box;height:clamp(260px,calc(75vh - 170px),520px);flex:1 1 auto;}' +
        '.hv-expt .hv-trend svg{min-height:0;}' +
        '.hv-trend{margin-top:8px;border:1px solid var(--e7-border,rgba(127,140,160,.25));border-radius:10px;padding:6px 8px 4px;}' +
        '.hv-trend .th{display:flex;justify-content:space-between;font-size:11.5px;color:var(--e7-text-3,#8D9CB2);}' +
        '.hv-trend .th b{color:var(--e7-heading,inherit);}' +
        '.hv-trend svg{display:block;width:100%;height:90px;}' +
        '.hv-empty{padding:40px;text-align:center;color:var(--e7-text-3,#8D9CB2);}' +
        /* IPS: reading-kind counts on the toggle, Voltage / Current / Digital sections, Σ tiles + battery totals */
        '.hv-seg .hv-segn{font-size:10.5px;font-weight:700;opacity:.75;margin-left:2px;}' +
        '.hv-ipsec{margin-top:8px;}' +
        '.hv-ipsec-h{display:flex;align-items:center;gap:6px;font-size:10.5px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:var(--e7-text-3,#8D9CB2);margin:0 0 5px;}' +
        '.hv-ipsec-h small{font-weight:700;padding:0 6px;border-radius:999px;background:rgba(127,140,160,.15);letter-spacing:0;}' +
        '.hv-ipsec[data-ipsec="voltage"] .hv-ipsec-h i{color:var(--e7-amber,#E0A100);}' +
        '.hv-ipsec[data-ipsec="current"] .hv-ipsec-h i{color:var(--brand,#22d3ee);}' +
        '.hv-ipsec[data-ipsec="digital"] .hv-ipsec-h i{color:var(--e7-green,#22A55B);}' +
        '.hv-tile.hv-sumtile{cursor:default;border-style:dashed;border-color:var(--brand,#22d3ee);background:rgba(34,211,238,.06);}' +
        '.hv-tile.hv-sumtile .lb{font-weight:800;color:var(--e7-heading,inherit);}' +
        '.hv-tile.hv-sumtile .vl small,.hv-ipssum-t b small{font-size:11px;font-weight:600;opacity:.75;}' +
        '.hv-ipssum{border:1px solid var(--e7-border,rgba(127,140,160,.25));border-radius:14px;background:var(--e7-surface-2,rgba(127,140,160,.05));padding:10px 12px;margin-bottom:12px;}' +
        '.hv-ipssum-h{font-size:12px;font-weight:800;color:var(--e7-heading,inherit);margin-bottom:8px;display:flex;align-items:center;gap:7px;}' +
        '.hv-ipssum-row{display:grid;grid-template-columns:repeat(auto-fill,minmax(230px,1fr));gap:8px;}' +
        '.hv-ipssum-t{display:grid;grid-template-columns:1fr auto;grid-template-areas:"k b" "n b" "a c";gap:1px 10px;align-items:center;border-radius:10px;padding:8px 10px;border:1px solid var(--e7-border,rgba(127,140,160,.25));border-left:4px solid #22A55B;background:var(--e7-surface,transparent);}' +
        '.hv-ipssum-t.discharging{border-left-color:#D97706;}' +
        '.hv-ipssum-t .k{grid-area:k;font-size:10px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:#22A55B;}' +
        '.hv-ipssum-t.discharging .k{color:#D97706;}' +
        '.hv-ipssum-t .n{grid-area:n;font-size:12.5px;font-weight:800;color:var(--e7-heading,inherit);overflow-wrap:anywhere;}' +
        '.hv-ipssum-t .a{grid-area:a;font-size:11px;color:var(--e7-text-3,#8D9CB2);overflow-wrap:anywhere;}' +
        '.hv-ipssum-t b{grid-area:b;font-size:18px;font-weight:800;font-variant-numeric:tabular-nums;color:var(--e7-heading,inherit);white-space:nowrap;}' +
        '.hv-ipssum-t .c{grid-area:c;font-size:10.5px;color:var(--e7-text-3,#8D9CB2);white-space:nowrap;text-align:right;}';

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
            b.innerHTML = '<i class="fa-solid fa-gauge-high"></i>';   /* v618.8: was fa-heart-pulse */
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

    function enter(asGrid) {
        if (!ensureUi()) return;
        H.grid = !!asGrid;
        if (H.grid) {
            /* v618.18: Grid view shows the health table (Grid button stays active) */
            H.view = 'table';
            $('#tlHealthBtn').removeClass('active').attr('aria-pressed', 'false');
            $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
            $('.tl-vmode-btn[data-vmode="Table"]').addClass('active').attr('aria-pressed', 'true');
        } else {
            H.prevMode = $('.tl-vmode-btn.active').attr('data-vmode') || H.prevMode || 'Cards';
            $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
            $('#tlHealthBtn').addClass('active').attr('aria-pressed', 'true');
        }
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
        H.grid = false;
        if (H.timer) { clearInterval(H.timer); H.timer = null; }
        $('.at-table-card').removeClass('tl-health-on');
        $('#tlHealthBtn').removeClass('active').attr('aria-pressed', 'false');
    }
    /* Card / Grid / Graph buttons: leave Health first (capture phase, so the
       core click handlers then run normally and repaint their view). */
    document.addEventListener('click', function (e) {
        var b = e.target && e.target.closest ? e.target.closest('.tl-vmode-btn') : null;
        if (!b) return;
        if (H.on) leave();
        /* v618.18: Grid (Table) = health table. The core Grid still renders
           underneath (hidden), so Export / downloads keep working. "Classic
           grid" in the toolbar shows the old table. */
        if (b.getAttribute('data-vmode') === 'Table' && !H.classic) {
            setTimeout(function () { enter(true); }, 0);
        }
    }, true);
    $(function () {
        setTimeout(function () {
            if (!H.on && !H.classic && $('.tl-vmode-btn.active').attr('data-vmode') === 'Table') enter(true);
        }, 1500);
    });
    $(document).on('change', '#drpSite', function () { H.rangesSite = null; H.trend = {}; H.sig = ''; if (H.on) loadRanges(); });
    $(document).on('change', '#drpAssetType', function () {
        H.sig = ''; H.trendKey = {};
        /* v618.33: asset type change returns to Card view */
        H.open = {}; H.classic = false;
        if (H.on) leave();
        $('.tl-vmode-btn').removeClass('active').attr('aria-pressed', 'false');
        $('.tl-vmode-btn[data-vmode="Cards"]').addClass('active').attr('aria-pressed', 'true');
    });

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
    /* v618.28: point machine direction badge (cards + table) */
    function dirChip(A) {
        if (!A.pmDir) return '';
        return ' <span class="hv-dir ' + (A.pmDir === 'Reverse' ? 'rev' : 'nor') + '">' + (A.pmDir === 'Reverse' ? '&#8634; ' : '&#8635; ') + esc(A.pmDir) + '</span>';
    }
    function pmPill(A) { return A.pmDir === 'Reverse' ? 'rev' : A.pmDir === 'Normal' ? 'clear' : ''; }
    function summaryText(A) {
        if (A.pmDir && A.pmWhen && A.pmWhen !== '--') {
            var base = A.have ? (A.outs ? A.outs + ' outside safe range' : 'Last operation') : 'Waiting for an operation';
            return base + ': ' + A.pmDir + ' at ' + A.pmWhen;
        }
        if (!A.have) return 'Waiting for live values';
        if (A.dlOnly) {   /* DataLogger-only track: state of its relays */
            return A.relays.map(function (r) {
                var tpr = /^TPR$/i.test(String(r.name || '').replace(/[^A-Za-z]/g, ''));
                return tpr ? (r.up ? 'Track clear' : 'Track occupied') : (r.name + (r.up ? ' Pickup' : ' Drop'));
            }).join(', ');
        }
        var p = [];
        if (A.outs) p.push(A.outs + ' outside safe range');
        if (A.near) p.push(A.near + ' near a limit');
        if (A.stale) p.push(A.stale + ' stale');
        return p.length ? p.join(', ') : 'All readings inside safe range';
    }
    /* reading text: relays as Pickup / Drop, numbers formatted */
    function dispV(r) { return r.relay ? (r.v === null ? '--' : (r.up ? 'Pickup' : 'Drop')) : fmtR(r); }
    function flagText(r) {
        /* no Low / High flag on a reading (no meaning for the user) */
        return r.st === 'near' ? 'Near' : r.st === 'occ' ? 'Occ.' : '';
    }
    function rowTip(r) {
        var g = r.range;
        var t = r.label + (r.sub ? ' (' + r.sub + ')' : '') + ': ' + dispV(r);
        if (g && g.min !== null && g.max !== null) t += ' | safe ' + fmtR(r, g.min) + ' to ' + fmtR(r, g.max) + ' (' + g.src + ')' + (g.avg !== null && g.avg !== undefined ? ' | avg ' + fmtR(r, g.avg) : '');
        if (r.st === 'occ') t += ' | low while the track is occupied (normal)';
        if (r.stale) t += ' | stale';
        return t;
    }
    /* v618.29 GAUGE -- the reading on its safe range, as in the alert
       "Live data" view: gradient track (amber low end, green safe middle,
       red high end) from safe MIN to safe MAX, marker clamped to the ends
       when outside, FRS average tick, min / avg / max labels underneath. */
    function gaugePos(r) {
        var g = r.range;
        if (!g || g.min === null || g.max === null || !(g.max > g.min) || r.v === null) return null;
        return Math.max(0, Math.min(100, ((r.v - g.min) / (g.max - g.min)) * 100));
    }
    function gaugeHtml(r, labels) {
        var g = r.range, p = gaugePos(r);
        /* no safe range configured: nothing under the value (the old "no safe range" caption said nothing useful) */
        if (p === null) return '';
        var avg = (g.avg !== null && g.avg !== undefined && g.max > g.min) ? Math.max(0, Math.min(100, ((g.avg - g.min) / (g.max - g.min)) * 100)) : null;
        return '<div class="hv-g"><span class="tr"></span>' +
            (avg !== null ? '<span class="av" style="left:' + avg.toFixed(1) + '%"></span>' : '') +
            '<span class="mk st-' + r.st + '" style="left:' + p.toFixed(1) + '%"></span></div>' +
            (labels ? '<div class="hv-gl"><span>' + fmt(g.min) + '</span><span>' + (avg !== null ? 'avg ' + fmt(g.avg) : '') + '</span><span>' + fmt(g.max) + '</span></div>' : '');
    }
    function statusLine(r) {
        var g = r.range;
        if (r.relay) return r.v === null ? 'no value' : 'relay ' + (r.up ? 'picked up' : 'dropped');
        if (r.v === null) return 'no value';
        /* no range, or below / above it: no status text (LOW / HIGH lines removed) */
        if (!g || g.min === null || g.max === null) return '';
        if (r.st === 'low' || r.st === 'high') return '';
        if (r.st === 'occ') return 'low -- track occupied';
        if (r.st === 'near') return 'near ' + (r.v - g.min < g.max - r.v ? 'low' : 'high') + ' limit';
        if (g.avg !== null && g.avg !== undefined && g.avg !== 0) {
            var d = Math.round(((r.v - g.avg) / Math.abs(g.avg)) * 100);
            if (Math.abs(d) >= 3) return Math.abs(d) + '% ' + (d > 0 ? 'above' : 'below') + ' normal';
        }
        return 'at normal';
    }
    function rowHtml(A, r) {
        var badge = r.st === 'near' ? 'NEAR' : r.st === 'occ' ? 'OCC.' : '';
        /* stale = clock icon at full strength (was: whole tile faded to 60%) */
        var staleIc = r.stale ? '<i class="fa-regular fa-clock hv-stale-ic" title="Stale: server marked this reading old (IsFresh = false)" aria-label="Stale reading"></i>' : '';
        return '<div class="hv-tile st-' + r.st + (r.stale ? ' stale' : '') + (H.trendKey[A.aid] === r.k ? ' sel' : '') +
            '" data-hv-row="' + esc(A.aid) + '" data-hv-k="' + esc(r.k) + '" title="' + esc(rowTip(r)) + ' -- click for trend">' +
            '<div class="hd"><span class="lb">' + esc(r.label) + '</span>' + staleIc + (badge ? '<span class="bg">' + badge + '</span>' : '') + '</div>' +
            '<div class="vl' + (r.relay ? ' hv-rv ' + (r.up ? 'up' : 'dn') : '') + '">' + dispV(r) + '</div>' +
            (function (t) { return t ? '<div class="sl">' + esc(t) + '</div>' : ''; })(statusLine(r)) +
            gaugeHtml(r, true) + '</div>';
    }

    /* ---- circuit (v618.14) ---------------------------------------------
       Drawn here, per asset and per attribute, in the style of the Telemetry
       Live circuit view (navy board, cyan rail 1 / pink rail 2, TLJB boxes,
       dashed attribute boxes "ALIAS : value", Charger, Track Relay, Relay
       room / TPR). The DB circuit (_Circuit partial) is one diagram for the
       whole asset type with page-wide ids, so it cannot be shown per card;
       this one can -- any number of cards, live every refresh.
       Boxes take the health colour of their reading (low amber, high red). */
    function circuitAllowed() {
        var at = assetTypeId();
        if (typeof window.isCircuitUnsupportedAssetType === 'function') {
            try { return !window.isCircuitUnsupportedAssetType(at); } catch (e) { /* fall through */ }
        }
        return true;
    }
    function pageLight() {
        if (window.SipTelemetry && typeof window.SipTelemetry.pageIsLight === 'function') {
            try { return !!window.SipTelemetry.pageIsLight(); } catch (e) { /* fall through */ }
        }
        var n = document.querySelector('.at-table-card') || document.body;
        while (n && n.nodeType === 1) {
            var c = window.getComputedStyle(n).backgroundColor || '';
            var m = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/.exec(c);
            if (m && (m[4] === undefined || parseFloat(m[4]) > 0.5)) return (0.2126 * m[1] + 0.7152 * m[2] + 0.0722 * m[3]) / 255 > 0.5;
            n = n.parentNode;
        }
        return true;
    }
    /* v618.16: the real (opaque) background behind the Health view, so sticky
       table header / asset column match the page theme instead of a token
       that is dark in light mode */
    function cardBg() {
        var n = document.getElementById('tlHealthView') || document.querySelector('.at-table-card');
        while (n && n.nodeType === 1) {
            var c = window.getComputedStyle(n).backgroundColor || '';
            var m = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/.exec(c);
            if (m && (m[4] === undefined || parseFloat(m[4]) > 0.5)) return 'rgb(' + m[1] + ',' + m[2] + ',' + m[3] + ')';
            n = n.parentNode;
        }
        return pageLight() ? '#FFFFFF' : '#212934';   /* --e7-surface (dark) */
    }
    function cpal() {
        return pageLight() ? {
            bg: '#F4F7FB', panel: '#FFFFFF', panelEdge: '#D3DCE6', text: '#13202E', muted: '#5A6878', box: '#8A97A6',
            cyan: '#0284C7', pink: '#E11D48', yellow: '#B45309', green: '#16A34A', relay: '#1D4ED8', sleeper: '#475569',
            tljb: '#38BDF8', tljbText: '#0B2540', low: '#C98A00', high: '#DC2626', drop: '#CA8A04'
        } : {
            bg: '#1d242e', panel: '#212934', panelEdge: '#36424f',   /* Energy7 dark slate (was navy) */ text: '#E6EDF5', muted: '#8FA3BF', box: '#6B7F9E',
            cyan: '#38BDF8', pink: '#FB5A7C', yellow: '#FACC15', green: '#22C55E', relay: '#2563EB', sleeper: '#1E293B',
            tljb: '#38BDF8', tljbText: '#0B2540', low: '#F5B70A', high: '#FF5A4E', drop: '#FACC15'
        };
    }
    function findRow(A, key) {
        for (var i = 0; i < A.rows.length; i++) if (A.rows[i].key === key) return A.rows[i];
        for (var j = 0; j < (A.derived || []).length; j++) if (A.derived[j].key === key) return A.derived[j];
        return null;
    }
    function cBox(P, x, y, w, h, r, fallbackLabel, unit) {
        var st = r ? r.st : 'none';
        var col = P.box;   /* low / high: no colour (attribute boxes stay neutral) */
        var tcol = P.text;
        var label = r ? (r.alias || r.label) : fallbackLabel;
        var val = r && r.v !== null && r.v !== undefined ? fmt(r.v) : '--';
        return '<g>' +
            '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="3" fill="none" stroke="' + col + '" stroke-width="' + 1.2 + '" stroke-dasharray="4 3"></rect>' +
            '<text x="' + (x + w / 2) + '" y="' + (y + 13) + '" text-anchor="middle" font-size="10.5" font-weight="700" fill="' + tcol + '">' + esc(label) + '</text>' +
            '<text x="' + (x + w / 2) + '" y="' + (y + h - 6) + '" text-anchor="middle" font-size="12.5" font-weight="700" fill="' + tcol + '">: ' + val + (unit ? ' ' + unit : '') + '</text>' +
            (r ? '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" fill="transparent" data-hv-row="' + esc(A_ID) + '" data-hv-k="' + esc(r.k) + '" style="cursor:pointer"><title>' + esc(r.label + ' -- click for trend') + '</title></rect>' : '') +
            '</g>';
    }
    var A_ID = '';
    /* ---- v618.28 circuits in the Telemetry Live circuit-view style ------
       Track: rails + RE bonds, TLJB, feed location box (LCBOXFEED), relay-end
       location box (LCBOXRELAY), relay room with TPR, 24 V / 110 V feeds.
       Signal: relay room lines per aspect (Bx110V / Nx110V) with relay
       contacts, location box with proving relays, signal unit with lamps and
       ISIG / VSIG boxes. Point machine: relay room (B/N-24V, B/N-110V,
       NWKR / RWKR / NWCR / RWCR), A / B end location boxes and motors, and
       the A / B operation table with direction.
       Light = paper (cream panels, red / navy wires); dark = navy variant. */
    function cpal2() {
        return pageLight() ? {
            bg: '#FFFFFF', paper: '#FBF6EA', edge: '#E3D9C3', ink: '#1F2A44', red: '#C0392B', navy: '#2B3A67', orange: '#E67E22', brown: '#7A4E2D',
            text: '#1F2937', muted: '#6B7280', box: '#4B5563', blue: '#2F6FD6', grey: '#9AA5B1', green: '#16A34A', drop: '#CA8A04',
            high: '#DC2626', low: '#C98A00', lampOff: '#3A3F55', head: '#232838', sleeper: '#C9CED6'
        } : {
            bg: '#1d242e', paper: '#212934', edge: '#36424f', ink: '#CBD5E1', red: '#F05252', navy: '#8EA2D8', orange: '#F59E0B', brown: '#C08457',
            text: '#E5E7EB', muted: '#94A3B8', box: '#94A3B8', blue: '#3B82F6', grey: '#64748B', green: '#22C55E', drop: '#FACC15',
            high: '#FF5A4E', low: '#F5B70A', lampOff: '#2B3044', head: '#151A28', sleeper: '#2A3550'
        };
    }
    function cb2(P, x, y, w, h, r, label, unit) {
        var st = r ? r.st : 'none';
        var col = P.box;   /* low / high: no colour */
        var tc = P.text;
        var lb = r ? (r.alias || r.label) : label;
        var val = r && r.v !== null && r.v !== undefined ? fmt(r.v) : '--';
        return '<g><rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" fill="' + P.bg + '" fill-opacity=".55" stroke="' + col + '" stroke-width="' + 1 + '" stroke-dasharray="3 2"></rect>' +
            '<text x="' + (x + w / 2) + '" y="' + (y + h / 2 - 2) + '" text-anchor="middle" font-size="9.5" font-weight="700" fill="' + tc + '">' + esc(lb) + '</text>' +
            '<text x="' + (x + w / 2) + '" y="' + (y + h / 2 + 10) + '" text-anchor="middle" font-size="10" font-weight="700" fill="' + tc + '">: ' + val + (unit ? ' ' + unit : '') + '</text>' +
            (r ? '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" fill="transparent" data-hv-row="' + esc(A_ID) + '" data-hv-k="' + esc(r.k) + '" style="cursor:pointer"><title>' + esc((r.alias || r.label) + ' -- click for trend') + '</title></rect>' : '') + '</g>';
    }
    function panel2(P, x, y, w, h) { return '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" fill="' + P.paper + '" stroke="' + P.edge + '"></rect>'; }
    function arrowR(P, x1, x2, y, col, w) {
        return '<line x1="' + x1 + '" y1="' + y + '" x2="' + (x2 - 6) + '" y2="' + y + '" stroke="' + col + '" stroke-width="' + (w || 3) + '"></line>' +
            '<path d="M' + (x2 - 12) + ' ' + (y - 7) + 'L' + x2 + ' ' + y + 'L' + (x2 - 12) + ' ' + (y + 7) + '" fill="none" stroke="' + col + '" stroke-width="' + (w || 3) + '" stroke-linejoin="round"></path>';
    }
    function txt2(x, y, s, size, col, weight, anchor) {
        return '<text x="' + x + '" y="' + y + '" font-size="' + (size || 11) + '" font-weight="' + (weight || 600) + '" fill="' + col + '"' + (anchor ? ' text-anchor="' + anchor + '"' : '') + '>' + s + '</text>';
    }
    function relayTag(P, x, y, name, up) {
        var col = up ? P.green : P.drop;
        var w = Math.max(44, String(name).length * 7 + 22);
        return '<g><rect x="' + x + '" y="' + (y - 11) + '" width="' + w + '" height="16" rx="2" fill="' + P.bg + '" stroke="' + col + '"></rect>' +
            txt2(x + 5, y + 1, esc(name) + ' ' + (up ? '&#8593;' : '&#8595;'), 10, col, 700) + '</g>';
    }

    /* ---- v618.40 shared circuit symbols (616 reference set) ---------------
       fuse, terminal link, ")(" cable plug / U-G connector, relay contact,
       relay coil, choke, adjustable resistor and split-field DC motor --
       used by the track, signal and point machine circuits below.
       state: true = picked up (green), false = dropped (amber), else muted. */
    function stCol(P, up) { return up === true ? P.green : up === false ? P.drop : P.muted; }
    function stArrow(up) { return up === true ? ' &#8593;' : up === false ? ' &#8595;' : ''; }
    function symFuseV(P, x, y, col, fill) {
        return '<rect x="' + (x - 5) + '" y="' + (y - 11) + '" width="10" height="22" rx="4" fill="' + (fill || P.paper) + '" stroke="' + col + '" stroke-width="2"></rect>' +
            '<circle cx="' + x + '" cy="' + (y - 11) + '" r="2.4" fill="' + col + '"></circle><circle cx="' + x + '" cy="' + (y + 11) + '" r="2.4" fill="' + col + '"></circle>' +
            '<line x1="' + x + '" y1="' + (y - 7) + '" x2="' + x + '" y2="' + (y + 7) + '" stroke="' + col + '" stroke-width="1.2"></line>';
    }
    function symFuseH(P, x, y, col, fill) {
        return '<rect x="' + (x - 11) + '" y="' + (y - 5) + '" width="22" height="10" rx="4" fill="' + (fill || P.paper) + '" stroke="' + col + '" stroke-width="2"></rect>' +
            '<circle cx="' + (x - 11) + '" cy="' + y + '" r="2.4" fill="' + col + '"></circle><circle cx="' + (x + 11) + '" cy="' + y + '" r="2.4" fill="' + col + '"></circle>' +
            '<line x1="' + (x - 7) + '" y1="' + y + '" x2="' + (x + 7) + '" y2="' + y + '" stroke="' + col + '" stroke-width="1.2"></line>';
    }
    function symLink(P, x, y) {
        return '<rect x="' + (x - 14) + '" y="' + (y - 4.5) + '" width="28" height="9" rx="4.5" fill="' + P.ink + '"></rect>' +
            '<circle cx="' + (x - 8) + '" cy="' + y + '" r="2.4" fill="' + P.bg + '"></circle><circle cx="' + (x + 8) + '" cy="' + y + '" r="2.4" fill="' + P.bg + '"></circle>';
    }
    function symPlug(P, x, y, col, fill, heavy) {
        return '<rect x="' + (x - 6) + '" y="' + (y - 4) + '" width="12" height="8" fill="' + (fill || P.bg) + '"></rect>' +
            '<path d="M' + (x - 8) + ' ' + (y - 8) + 'Q' + (x - 1) + ' ' + y + ' ' + (x - 8) + ' ' + (y + 8) + 'M' + (x + 8) + ' ' + (y - 8) + 'Q' + (x + 1) + ' ' + y + ' ' + (x + 8) + ' ' + (y + 8) + '" fill="none" stroke="' + (col || P.ink) + '" stroke-width="' + (heavy ? 2.8 : 1.8) + '" stroke-linecap="round"></path>';
    }
    /* front contact on a horizontal wire; the label sits above (or below) */
    function symContact(P, x, y, name, up, fill, below) {
        var c = stCol(P, up);
        return '<g><title>' + esc(name + (up === true ? ' picked up' : up === false ? ' dropped' : ' state unknown')) + '</title>' +
            '<rect x="' + (x - 8) + '" y="' + (y - 3) + '" width="16" height="6" fill="' + (fill || P.paper) + '"></rect>' +
            '<path d="M' + (x - 6) + ' ' + (y - 6) + 'V' + (y + 6) + 'M' + (x + 6) + ' ' + (y - 6) + 'V' + (y + 6) + '" stroke="' + c + '" stroke-width="2.2"></path>' +
            (up === true ? '<path d="M' + (x - 6) + ' ' + y + 'H' + (x + 6) + '" stroke="' + c + '" stroke-width="1.6"></path>' : '') +
            txt2(x, below ? y + 17 : y - 9, esc(name) + stArrow(up), 9, c, 700, 'middle') + '</g>';
    }
    /* relay coil / ECR box drawn over the wire, coloured by state */
    function symCoil(P, x, y, w, name, up, solid) {
        var c = stCol(P, up);
        return '<g><title>' + esc(name + (up === true ? ' picked up' : up === false ? ' dropped' : ' state unknown')) + '</title>' +
            '<rect x="' + x + '" y="' + (y - 10) + '" width="' + w + '" height="20" rx="3" fill="' + (solid ? c : P.paper) + '" stroke="' + c + '" stroke-width="1.8"></rect>' +
            txt2(x + w / 2, y + 4, esc(name) + (solid ? '' : stArrow(up)), 10, solid ? '#FFFFFF' : c, 800, 'middle') + '</g>';
    }
    function symChokeV(P, x, y) {
        return '<rect x="' + (x - 6) + '" y="' + (y - 12) + '" width="12" height="24" fill="' + P.paper + '" stroke="' + P.ink + '" stroke-width="1.8"></rect>' +
            '<path d="M' + (x - 3) + ' ' + (y - 6) + 'H' + (x + 3) + 'M' + (x - 3) + ' ' + y + 'H' + (x + 3) + 'M' + (x - 3) + ' ' + (y + 6) + 'H' + (x + 3) + '" stroke="' + P.ink + '" stroke-width="1.6"></path>';
    }
    /* adjustable resistor: coil on the lead with an arrow through it */
    function symVarResV(P, x, y, col) {
        var d = 'M' + x + ' ' + (y - 18);
        for (var i = 0; i < 4; i++) d += 'a6 4.5 0 1 1 0 9';
        return '<rect x="' + (x - 3) + '" y="' + (y - 18) + '" width="6" height="36" fill="' + P.paper + '"></rect>' +
            '<path d="' + d + '" fill="none" stroke="' + col + '" stroke-width="2"></path>' +
            arrowTo(P, x - 12, y + 15, x + 14, y - 15, col, 1.6);
    }
    /* split-field DC motor: N/F and R/F field windings into armature "A";
       wires arrive at (x-45, y-16) and (x-45, y+16), common leaves at x+11 */
    function symMotor(P, x, y, col) {
        var nf = 'M' + (x - 45) + ' ' + (y - 16) + 'a4 4 0 0 1 8 0a4 4 0 0 1 8 0a4 4 0 0 1 8 0L' + (x - 9) + ' ' + (y - 6);
        var rf = 'M' + (x - 45) + ' ' + (y + 16) + 'a4 4 0 0 0 8 0a4 4 0 0 0 8 0a4 4 0 0 0 8 0L' + (x - 9) + ' ' + (y + 6);
        return '<path d="' + nf + rf + '" fill="none" stroke="' + col + '" stroke-width="2"></path>' +
            '<circle cx="' + x + '" cy="' + y + '" r="11" fill="' + P.paper + '" stroke="' + col + '" stroke-width="2"></circle>' +
            txt2(x, y + 4, 'A', 11, col, 800, 'middle') +
            txt2(x - 33, y - 24, 'N/F', 9, col, 700, 'middle') + txt2(x - 33, y + 33, 'R/F', 9, col, 700, 'middle');
    }
    function arrowTo(P, x1, y1, x2, y2, col, w, dash) {
        var a = Math.atan2(y2 - y1, x2 - x1), h = 7;
        return '<line x1="' + x1 + '" y1="' + y1 + '" x2="' + x2 + '" y2="' + y2 + '" stroke="' + col + '" stroke-width="' + (w || 1.2) + '"' + (dash ? ' stroke-dasharray="' + dash + '"' : '') + '></line>' +
            '<path d="M' + (x2 - h * Math.cos(a - 0.45)).toFixed(1) + ' ' + (y2 - h * Math.sin(a - 0.45)).toFixed(1) + 'L' + x2 + ' ' + y2 + 'L' + (x2 - h * Math.cos(a + 0.45)).toFixed(1) + ' ' + (y2 - h * Math.sin(a + 0.45)).toFixed(1) + '" fill="none" stroke="' + col + '" stroke-width="' + (w || 1.2) + '"></path>';
    }
    function arrowHeadL(x, y, col) { return '<path d="M' + (x + 12) + ' ' + (y - 7) + 'L' + x + ' ' + y + 'L' + (x + 12) + ' ' + (y + 7) + '" fill="none" stroke="' + col + '" stroke-width="2.4" stroke-linejoin="round"></path>'; }
    function dot(x, y, col) { return '<circle cx="' + x + '" cy="' + y + '" r="3" fill="' + col + '"></circle>'; }
    /* relay state by name from the asset's DataLogger relays (exact, then suffix) */
    function relayState(A, name) {
        var k = norm(name), rs = A.relays || [], i;
        for (i = 0; i < rs.length; i++) if (norm(rs[i].name) === k) return !!rs[i].up;
        for (i = 0; i < rs.length; i++) { var n = norm(rs[i].name); if (n.length > k.length && n.slice(-k.length) === k) return !!rs[i].up; }
        var dl = (live()[A.aid] || {}).dlRelays || {};
        for (var dk in dl) {
            if (!dl.hasOwnProperty(dk) || !dl[dk]) continue;
            var dn = norm(dl[dk].displayName || dk);
            if (dn === k || (dn.length > k.length && dn.slice(-k.length) === k)) return dl[dk].isNull ? undefined : !!dl[dk].isPickup;
        }
        return undefined;
    }
    /* raw live value by AssetAttributeId (PM N / R readings) */
    function attrById(aid, id) {
        var at = (live()[aid] || {}).attrs || {};
        for (var k in at) {
            if (!at.hasOwnProperty(k) || !at[k]) continue;
            if (parseInt(at[k].AttrId || at[k].AssetAttributeId || 0, 10) === id) { var v = num(at[k].Value); return v === null ? '--' : fmt(v); }
        }
        return '--';
    }

    function trackCircuitSvg(A) {
        var P = cpal2();
        A_ID = A.aid;
        var ib = findRow(A, 'IBALST');
        var s = '<svg viewBox="0 0 1000 610" role="img" aria-label="Track circuit ' + esc(A.name) + '"><rect width="1000" height="610" fill="' + P.bg + '"></rect>';
        /* rails + sleepers + RE bonds */
        for (var x = 132; x <= 850; x += 30) s += '<rect x="' + x + '" y="62" width="7" height="46" rx="1.5" fill="' + P.sleeper + '"></rect>';
        var rail1 = A.occ ? P.red : P.navy;
        s += '<line x1="110" y1="72" x2="880" y2="72" stroke="' + rail1 + '" stroke-width="5"></line>' +
            '<line x1="110" y1="72" x2="190" y2="72" stroke="' + P.red + '" stroke-width="5"></line><line x1="800" y1="72" x2="880" y2="72" stroke="' + P.red + '" stroke-width="5"></line>' +
            '<line x1="110" y1="100" x2="880" y2="100" stroke="' + P.red + '" stroke-width="5"></line>' +
            txt2(480, 60, 'Rail 1', 13, P.ink, 800, 'middle') + txt2(480, 125, 'Rail 2', 13, P.ink, 800, 'middle') +
            '<rect x="420" y="78" width="120" height="16" fill="' + P.bg + '"></rect>' + txt2(480, 91, 'IBALST : ' + (ib && ib.v !== null ? fmt(ib.v) : '--'), 11, P.ink, 800, 'middle');
        /* rail bond loops at the lead connections, RE Bond arrows onto them */
        [[228, 72], [248, 100], [588, 72], [608, 100]].forEach(function (b) {
            s += '<path d="M' + (b[0] - 12) + ' ' + b[1] + 'q0 13 12 13q12 0 12 -13" fill="none" stroke="' + P.ink + '" stroke-width="1.6"></path>';
        });
        s += txt2(205, 36, 'RE Bond', 9.5, P.muted, 600, 'middle') + arrowTo(P, 212, 40, 222, 66, P.muted, 1) +
            txt2(625, 36, 'RE Bond', 9.5, P.muted, 600, 'middle') + arrowTo(P, 618, 40, 596, 66, P.muted, 1);
        /* panels */
        s += panel2(P, 130, 158, 280, 400) + panel2(P, 430, 158, 250, 400) + panel2(P, 700, 158, 210, 400) +
            txt2(805, 150, 'RELAY ROOM', 12, P.ink, 700, 'middle') +
            txt2(140, 520, 'LOCATION BOX', 11, P.ink, 600) + txt2(140, 538, 'LCBOXFEED', 12, P.ink, 800) +
            txt2(440, 540, 'LOCATION BOX: <tspan font-weight="800">LCBOXRELAY</tspan>', 11, P.ink, 600);
        /* TLJBs + drops from the rails */
        s += '<path d="M228 72V128M248 100V128M588 72V128M608 100V128" stroke="' + P.ink + '" stroke-width="2"></path>' +
            '<rect x="208" y="128" width="60" height="20" fill="' + P.head + '"></rect>' + txt2(238, 142, 'TLJB', 10.5, '#FFFFFF', 700, 'middle') +
            '<rect x="568" y="128" width="60" height="20" fill="' + P.head + '"></rect>' + txt2(598, 142, 'TLJB', 10.5, '#FFFFFF', 700, 'middle');
        /* feed end: red + navy feed, adjustable resistor, choke, battery, charger, 110 V AC */
        s += '<path d="M228 148V470M248 148V385" stroke="' + P.red + '" stroke-width="2.4" fill="none"></path>' +
            '<path d="M248 148V170" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            symFuseV(P, 228, 166, P.red) + symFuseV(P, 248, 166, P.navy) +
            symVarResV(P, 228, 214, P.red) +
            symChokeV(P, 248, 214) + txt2(258, 200, 'Choke', 9.5, P.ink, 600) +
            '<path d="M218 344h20M222 350h12" stroke="' + P.ink + '" stroke-width="2.4"></path>' + txt2(244, 348, 'Battery', 9.5, P.ink, 600) +
            '<rect x="200" y="385" width="80" height="24" fill="' + P.grey + '"></rect>' + txt2(240, 401, 'Charger', 11, '#1F2937', 700, 'middle') +
            '<path d="M218 409V470M262 409V470" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            '<rect x="213" y="452" width="10" height="18" rx="3" fill="none" stroke="' + P.red + '" stroke-width="2"></rect><rect x="257" y="452" width="10" height="18" rx="3" fill="none" stroke="' + P.red + '" stroke-width="2"></rect>' +
            txt2(196, 466, '(N)', 10, P.ink, 600, 'end') + txt2(272, 466, '(P) 110V AC', 10, P.ink, 600);
        s += cb2(P, 134, 170, 88, 30, findRow(A, 'RTCVARRES'), 'RTC VAR RES', 'Ohm') +
            cb2(P, 134, 206, 88, 30, findRow(A, 'VTCVARRES'), 'VTC VAR RES', 'V') +
            cb2(P, 134, 242, 88, 30, findRow(A, 'RTCCHFEEDEND'), 'RTC CH FEED END', 'Ohm') +
            cb2(P, 134, 278, 88, 30, findRow(A, 'ITCBATTCHARG'), 'ITC BATT CHARG', 'mA') +
            cb2(P, 134, 318, 88, 30, findRow(A, 'CHARGERMA'), 'ITC TFC O/P(mA)', '') +
            cb2(P, 134, 418, 88, 30, findRow(A, 'CHARGERV'), 'VTC TFC I/P(V)', '') +
            cb2(P, 296, 176, 106, 30, findRow(A, 'CHOKEV'), 'VTC CH FEED END(V)', '') +
            cb2(P, 296, 212, 106, 30, findRow(A, 'IFMA'), 'ITC FEED END(mA)', '') +
            cb2(P, 296, 248, 106, 30, findRow(A, 'VF'), 'VTC FEED END(V)', '') +
            cb2(P, 296, 390, 106, 30, findRow(A, 'CHARGEROPV'), 'VTC TFC O/P(V)', '');
        /* relay end: drops with fuses, fuse before the track relay, track relay */
        s += '<path d="M588 148V262M608 148V262" stroke="' + P.red + '" stroke-width="2.4"></path>' +
            symFuseV(P, 588, 166, P.red) + symFuseV(P, 608, 166, P.red) + symFuseV(P, 608, 238, P.red) +
            '<rect x="540" y="262" width="116" height="26" fill="' + P.blue + '"></rect>' + txt2(598, 280, 'Track Relay', 12, '#FFFFFF', 700, 'middle') +
            cb2(P, 440, 168, 120, 30, findRow(A, 'VR'), 'VTC RELAY END(V)', '') +
            cb2(P, 440, 204, 120, 30, findRow(A, 'IRMA'), 'ITC RELAY END(mA)', '') +
            cb2(P, 618, 200, 58, 30, findRow(A, 'VTCTR'), 'TR V (Relay)', '') +
            cb2(P, 440, 240, 90, 30, findRow(A, 'RRAIL'), 'RRAIL', 'Ohm');
        /* 24 V DC loop: B-24V / N-24V from the relay room through the four
           location-box fuses (+)(-)(+)(-) and back to the TPR -- separate from
           the track relay leads */
        s += '<path d="M455 334V305H505V334M455 356V500M505 356V440H880V188H865" fill="none" stroke="' + P.red + '" stroke-width="2.4"></path>' +
            '<path d="M480 334V318H530V334M480 356V470M530 356V455H895V200H865" fill="none" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            symFuseV(P, 455, 345, P.red) + symFuseV(P, 480, 345, P.navy) + symFuseV(P, 505, 345, P.red) + symFuseV(P, 530, 345, P.navy) +
            txt2(455, 298, '(+)', 9, P.ink, 700, 'middle') + txt2(480, 298, '(&#8722;)', 9, P.ink, 700, 'middle') +
            txt2(505, 298, '(+)', 9, P.ink, 700, 'middle') + txt2(530, 298, '(&#8722;)', 9, P.ink, 700, 'middle') +
            txt2(555, 425, '24V DC FROM R/R', 9.5, P.ink, 600) +
            cb2(P, 550, 360, 120, 30, findRow(A, 'TPRVLOC'), 'VTC 24 DC LOC(V)', '');
        /* relay room: TPR + feeds out */
        var tprUp = A.occ === false, tprCol = A.occ === true ? P.drop : A.occ === false ? P.green : P.muted;
        s += '<rect x="745" y="176" width="120" height="36" fill="' + P.paper + '" stroke="' + P.box + '" stroke-dasharray="3 2"></rect>' +
            txt2(795, 200, 'TPR', 15, tprCol, 800, 'middle') +
            txt2(828, 201, A.occ === null || A.occ === undefined ? '' : (tprUp ? '&#8593;' : '&#8595;'), 17, tprCol, 800, 'middle') +
            txt2(805, 228, A.occ === true ? 'Drop (occupied)' : A.occ === false ? 'Pickup (clear)' : '', 10, P.muted, 600, 'middle') +
            cb2(P, 720, 250, 150, 30, findRow(A, 'TPRV'), 'VTC 24 DC TPR I/P(V)', '') +
            arrowR(P, 480, 960, 470, P.navy, 3) + txt2(925, 460, 'N-24V', 10.5, P.ink, 700, 'end') +
            arrowR(P, 455, 960, 500, P.red, 3) + txt2(925, 492, 'B-24V', 10.5, P.ink, 700, 'end') +
            arrowR(P, 248, 960, 572, P.brown, 3) + txt2(925, 564, 'Bx110V', 10.5, P.ink, 700, 'end') +
            arrowR(P, 228, 960, 596, P.navy, 3) + txt2(925, 590, 'Nx110V', 10.5, P.ink, 700, 'end') +
            '<path d="M228 470V596M248 470V572" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            symFuseH(P, 905, 572, P.brown, P.bg) +
            txt2(560, 588, 'U/G CABLE', 10.5, P.ink, 600, 'middle');
        /* U/G cable plug / socket where the 24 V and 110 V lines enter the relay room */
        [[440, P.red], [455, P.navy], [470, P.navy], [500, P.red], [572, P.brown], [596, P.navy]].forEach(function (u) { s += symPlug(P, 690, u[0], P.ink, P.bg, true); });
        s += txt2(690, 428, 'U/G', 8.5, P.ink, 700, 'middle') + txt2(690, 520, 'U/G CABLE', 8.5, P.ink, 700, 'middle');
        return s + '</svg>';
    }

    /* signal: aspects from ISIG / VSIG readings, top -> bottom HHG, DG, HG, RG.
       Each aspect is fed Bx110V -> fuse -> relay contacts (series and parallel
       branches, drawn on the 616 S-39 pattern) -> ECR -> location box ")(" ->
       lamp, and returns on Nx110V through its own contacts.
       A string is a series contact, an array of arrays is a set of parallel
       branches. Verify against the station's signal control circuit. */
    var SIG_WIRING = {
        HHG: { b: ['HR', [['DR', 'HHR'], ['DR', 'DECR']]], n: ['HR', [['DR', 'HHR'], ['DR', 'DECR']]], ecr: 'HHECR' },
        DG: { b: ['HR', 'HHR', 'DR'], n: ['HR', 'HHR', 'DR'], ecr: 'DECR' },
        GG: { b: ['HR', 'DR'], n: ['HR', 'DR'], ecr: 'DECR' },
        HG: { b: [[['HR'], ['DECR', 'DR']]], n: [[['DECR', 'DR'], ['HR']]], ecr: 'HECR' },
        YG: { b: ['HR'], n: ['HR'], ecr: 'HECR' },
        RG: { b: [[['HR'], ['DECR', 'HHECR', 'HECR']]], n: [[['DECR', 'HHECR', 'HECR'], ['HR']]], ecr: 'RECR' }
    };
    function sigChain(P, A, items, x0, y, wireCol) {
        var s = '', x = x0, CW = 60;
        items.forEach(function (it) {
            if (typeof it === 'string') { s += symContact(P, x + CW / 2, y, it, relayState(A, it)); x += CW; return; }
            var w = 0;
            it.forEach(function (br) { w = Math.max(w, br.length * CW); });
            w += 20;
            it.forEach(function (br, bi) {
                var by = y + bi * 18;
                if (bi) s += '<path d="M' + (x + 4) + ' ' + y + 'V' + by + 'H' + (x + w - 4) + 'V' + y + '" fill="none" stroke="' + wireCol + '" stroke-width="2"></path>' + dot(x + 4, y, wireCol) + dot(x + w - 4, y, wireCol);
                br.forEach(function (c, k) { s += symContact(P, x + 10 + CW / 2 + k * CW, by, c, relayState(A, c), P.paper, bi > 0); });
            });
            x += w;
        });
        return s;
    }
    function signalCircuitSvg(A) {
        var P = cpal2();
        A_ID = A.aid;
        var byKey = {};
        A.rows.forEach(function (r) { byKey[norm(r.alias || r.label)] = r; byKey[r.key] = byKey[r.key] || r; });
        var asp = [];
        A.rows.forEach(function (r) {
            var m = /^\s*[IV]SIG\s+([A-Z]+)\s*$/i.exec(r.alias || r.label || '');
            if (m && asp.indexOf(m[1].toUpperCase()) === -1 && !/PR$/.test(m[1].toUpperCase())) asp.push(m[1].toUpperCase());
        });
        var ORDER = ['HHG', 'DG', 'GG', 'HG', 'YG', 'RG'];
        asp.sort(function (a, b) { var ia = ORDER.indexOf(a), ib = ORDER.indexOf(b); return (ia < 0 ? 9 : ia) - (ib < 0 ? 9 : ib); });
        if (!asp.length) return genericCircuitSvg(A);
        var lampCol = function (a) { return /RG$/.test(a) ? '#EF4444' : /[DG]G$/.test(a) && a !== 'HHG' ? '#22C55E' : '#F5B70A'; };
        var get = function (pre, a) { return byKey[norm(pre + ' ' + a)] || byKey[norm(pre + a)] || null; };
        var proving = A.rows.filter(function (r) { return /[IV]SIG\s+[A-Z]*PR$|PR\s*$/i.test(r.alias || r.label || ''); });
        var others = A.rows.filter(function (r) { return !/^\s*[IV]SIG\s+[A-Z]+\s*$/i.test(r.alias || r.label || '') && proving.indexOf(r) === -1; });
        var rowH = 140, top = 60, NOFF = 66, body = asp.length * rowH;
        var H = top + body + 70 + (others.length ? Math.ceil(others.length / 4) * 40 + 40 : 0);
        var s = '<svg viewBox="0 0 1000 ' + H + '" role="img" aria-label="Signal circuit ' + esc(A.name) + '"><rect width="1000" height="' + H + '" fill="' + P.bg + '"></rect>';
        s += panel2(P, 20, 40, 520, body) + txt2(280, 32, 'RELAY ROOM', 11, P.ink, 700, 'middle') +
            panel2(P, 560, 40, 130, body) + txt2(625, 32, 'LOCATION BOX', 10, P.ink, 700, 'middle') +
            '<line x1="705" y1="24" x2="705" y2="' + (40 + body) + '" stroke="' + P.ink + '" stroke-dasharray="3 3"></line>' + txt2(712, 32, 'SIGNAL UNIT', 10, P.ink, 700);
        /* signal head, post and base plate */
        var headH = body - 30;
        s += '<rect x="745" y="50" width="96" height="' + headH + '" rx="40" fill="' + P.head + '"></rect>' +
            '<rect x="784" y="' + (50 + headH) + '" width="18" height="60" fill="' + P.head + '"></rect>' +
            '<rect x="763" y="' + (110 + headH) + '" width="60" height="12" rx="2" fill="' + P.head + '"></rect>';
        asp.forEach(function (a, i) {
            var y = top + i * rowH, ny = y + NOFF, cy = y + 33;
            var I = get('ISIG', a), V = get('VSIG', a);
            var lit = (I && I.v !== null && I.v > 30) || (V && V.v !== null && V.v > 50);
            var wr = SIG_WIRING[a] || { b: ['HR'], n: ['HR'], ecr: '' };
            /* feed and return end at the signal unit (no arrowheads into the lamp) */
            s += '<path d="M30 ' + y + 'H745" stroke="' + P.red + '" stroke-width="2.4"></path>' + txt2(34, y - 8, 'Bx110V', 9.5, P.ink, 700) +
                '<path d="M745 ' + ny + 'H36" stroke="' + P.navy + '" stroke-width="2.4"></path>' + arrowHeadL(32, ny, P.navy) +
                txt2(34, ny + 16, 'Nx110V', 9.5, P.ink, 700);
            s += symFuseH(P, 52, y, P.red);
            s += sigChain(P, A, wr.b, 74, y, P.red) + sigChain(P, A, wr.n, 74, ny, P.navy);
            /* the aspect's ECR at the end of the relay room */
            if (wr.ecr) s += symCoil(P, 448, y, 78, wr.ecr, relayState(A, wr.ecr));
            /* location box terminals */
            s += symPlug(P, 576, y, P.ink, P.paper) + symPlug(P, 576, ny, P.ink, P.paper);
            /* lamp */
            s += '<circle cx="793" cy="' + cy + '" r="27" fill="' + (lit ? lampCol(a) : P.lampOff) + '"' + (lit ? ' style="filter:drop-shadow(0 0 8px ' + lampCol(a) + ')"' : '') + '></circle>' +
                cb2(P, 860, y - 2, 128, 26, I, 'ISIG ' + a, '') + cb2(P, 860, y + 34, 128, 26, V, 'VSIG ' + a, '');
            /* proving relay of this aspect: HHG -> HHPR, DG -> DPR, HG -> HPR, RG -> RPR */
            var want = norm(a.replace(/G$/, '') + 'PR'), pr = null;
            proving.forEach(function (q) { var n = norm(q.alias || q.label); if (n.slice(-want.length) === want && (!pr || n.length < norm(pr.alias || pr.label).length)) pr = q; });
            if (pr) s += cb2(P, 588, y + 17, 96, 32, pr, pr.alias || pr.label, '');
        });
        if (others.length) {
            var oy = top + body + 100;
            s += txt2(20, oy - 8, 'OTHER READINGS', 10, P.muted, 700);
            others.forEach(function (r, k) { s += cb2(P, 20 + (k % 4) * 245, oy + Math.floor(k / 4) * 40, 235, 32, r, r.alias || r.label, ''); });
        }
        return s + '</svg>';
    }

    /* point machine (616 PT pattern): relay room, A / B end location boxes
       and motor boxes, closed 24 V detection loops B-24V -> B end -> A end ->
       NWKR / RWKR coils -> N-24V, and the 110 V operation circuit through
       NWCR / RWCR (B end motor) and NWPR/NWCZR / RWPR/RWCZR (A end motor)
       to the split-field motors, common return to N-110V. */
    function pmCircuitSvg(A) {
        var P = cpal2();
        A_ID = A.aid;
        var lab = {};
        A.rows.forEach(function (r) { lab[r.label] = r; });
        var v = function (e, d) { var r = lab[e + ' End ' + d]; return r && r.v !== null && r.v !== undefined ? fmt(r.v) : '--'; };
        var rl = {};
        A.relays.forEach(function (r) { rl[String(r.name).toUpperCase().replace(/[^A-Z]/g, '').slice(-4)] = r; });
        var up = function (k) { return rl[k] ? !!rl[k].up : relayState(A, k); };
        var upAny = function (a, b) { var x = relayState(A, a); return x === undefined ? relayState(A, b) : x; };
        var W = 1060, Hh = 660;
        var s = '<svg viewBox="0 0 ' + W + ' ' + Hh + '" role="img" aria-label="Point machine circuit ' + esc(A.name) + '"><rect width="' + W + '" height="' + Hh + '" fill="' + P.bg + '"></rect>';
        var dash = function (x, y, w, h) { return '<rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" fill="' + P.paper + '" fill-opacity=".5" stroke="' + P.box + '" stroke-dasharray="5 4"></rect>'; };
        s += panel2(P, 30, 50, 300, 590) + txt2(180, 40, 'RELAY ROOM', 13, P.ink, 800, 'middle') +
            panel2(P, 400, 340, 120, 300) + txt2(460, 318, 'A END', 13, P.ink, 800, 'middle') + txt2(460, 333, 'Location Box', 10, P.ink, 600, 'middle') +
            dash(530, 340, 100, 300) + txt2(580, 333, "'A' END MOTOR", 9.5, P.ink, 700, 'middle') +
            panel2(P, 640, 60, 120, 360) + txt2(700, 50, 'B END', 13, P.ink, 800, 'middle') + txt2(700, 76, 'Location Box', 10, P.ink, 600, 'middle') +
            dash(770, 60, 100, 360) + txt2(820, 52, "'B' END MOTOR", 9.5, P.ink, 700, 'middle');
        /* 24 V detection: B-24V bus -> B end -> A end -> NWKR / RWKR coils -> N-24V */
        s += txt2(44, 84, 'B-24V', 11, P.ink, 700) + '<path d="M44 90H840" stroke="' + P.red + '" stroke-width="2.4"></path>' +
            '<path d="M810 90V130H560V380H262M840 90V150H590V430H262" fill="none" stroke="' + P.red + '" stroke-width="2.4"></path>' + dot(810, 90, P.red) +
            txt2(66, 124, 'N-24V', 11, P.ink, 700) + '<path d="M192 380H60M192 430H60M60 430V110H36" fill="none" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            arrowHeadL(32, 110, P.navy) + dot(60, 380, P.navy);
        var aNk = attrById(A.aid, 25), aRk = attrById(A.aid, 26), bNk = attrById(A.aid, 27), bRk = attrById(A.aid, 28);
        s += symCoil(P, 192, 380, 70, 'NWKR', up('NWKR'), true) + symCoil(P, 192, 430, 70, 'RWKR', up('RWKR'), true) +
            txt2(192, 366, 'A VPT NWKR: ' + aNk, 9, P.ink, 700) + txt2(192, 404, 'B VPT NWKR: ' + bNk, 9, P.ink, 700) +
            txt2(192, 416, 'A VPT RWKR: ' + aRk, 9, P.ink, 700) + txt2(192, 454, 'B VPT RWKR: ' + bRk, 9, P.ink, 700);
        /* 110 V operation: B-110V -> fuse -> NWCR (N/F) / RWCR (R/F) -> B end motor;
           NWPR/NWCZR (N/F) / RWPR/RWCZR (R/F) -> A end motor */
        s += txt2(44, 194, 'B-110V', 11, P.ink, 700) +
            '<path d="M44 200H780V244H790M120 200V230H775V276H790M100 200V552H545M100 520H545" fill="none" stroke="' + P.orange + '" stroke-width="2.4"></path>' +
            dot(100, 200, P.orange) + dot(120, 200, P.orange) + dot(100, 520, P.orange) + symFuseH(P, 76, 200, P.orange) +
            symContact(P, 200, 200, 'A End - NWCR', up('NWCR')) + symContact(P, 200, 230, 'A End - RWCR', up('RWCR'), P.paper, true) +
            symContact(P, 200, 520, 'NWPR/NWCZR', upAny('NWPR', 'NWCZR')) + symContact(P, 200, 552, 'RWPR/RWCZR', upAny('RWPR', 'RWCZR'), P.paper, true);
        /* common returns to N-110V, through the return contacts */
        s += txt2(44, 294, 'N-110V', 11, P.ink, 700) +
            '<path d="M846 260H862V300H36M150 300V322H250V300M601 536H615V600H70V300M150 600V622H250V600" fill="none" stroke="' + P.navy + '" stroke-width="2.4"></path>' +
            arrowHeadL(32, 300, P.navy) + dot(70, 300, P.navy) + dot(150, 300, P.navy) + dot(250, 300, P.navy) + dot(150, 600, P.navy) + dot(250, 600, P.navy) +
            symContact(P, 200, 300, 'A End - NWCR', up('NWCR')) + symContact(P, 200, 322, 'A End - RWCR', up('RWCR'), P.paper, true) +
            symContact(P, 200, 600, 'NWPR/NWCZR', upAny('NWPR', 'NWCZR')) + symContact(P, 200, 622, 'RWPR/RWCZR', upAny('RWPR', 'RWCZR'), P.paper, true);
        /* split-field motors */
        s += symMotor(P, 835, 260, P.ink) + symMotor(P, 590, 536, P.ink);
        /* terminal links on every wire through both location boxes */
        [90, 130, 150, 200, 230, 300].forEach(function (y) { s += symLink(P, 700, y); });
        [380, 430, 520, 552, 600].forEach(function (y) { s += symLink(P, 460, y); });
        /* end location readings, 24 V split into N and R */
        s += txt2(460, 398, 'A VPT 24 DC LOC', 8.5, P.green, 700, 'middle') +
            txt2(460, 414, 'N: ' + attrById(A.aid, 576) + '  R: ' + attrById(A.aid, 577), 9.5, P.ink, 800, 'middle') +
            txt2(460, 470, 'A IPT Max / Avg', 8.5, P.muted, 700, 'middle') + txt2(460, 486, v('A', 'IPT N/R(A) Max') + ' / ' + v('A', 'IPT N/R(A) Avg') + ' A', 10.5, P.ink, 800, 'middle') +
            txt2(700, 170, 'B VPT 24 DC LOC', 8.5, P.green, 700, 'middle') +
            txt2(700, 186, 'N: ' + attrById(A.aid, 578) + '  R: ' + attrById(A.aid, 579), 9.5, P.ink, 800, 'middle') +
            txt2(700, 256, 'B IPT Max / Avg', 8.5, P.muted, 700, 'middle') + txt2(700, 272, v('B', 'IPT N/R(A) Max') + ' / ' + v('B', 'IPT N/R(A) Avg') + ' A', 10.5, P.ink, 800, 'middle');
        /* operation table with relay state pills */
        var tx = 636, ty = 480, cw = [28, 66, 54, 44, 54, 48, 30, 30, 30, 30], tw = cw.reduce(function (a, b) { return a + b; }, 0);
        var hd = [['End', ''], ['IPT N/R', 'Max/Avg'], ['VPT 110', 'DC LOC N/R'], ['TPT N/R', '(ms)'], ['VPT 24', 'DC LOC N/R'], ['VPT N/R', '(V)'], ['NWKR', ''], ['RWKR', ''], ['NWCR', ''], ['RWCR', '']];
        var dir = A.pmDir || '';
        s += '<rect x="' + tx + '" y="' + (ty - 26) + '" width="' + tw + '" height="122" fill="' + P.paper + '" stroke="' + P.box + '" stroke-dasharray="3 2"></rect>' +
            txt2(tx + 6, ty - 10, esc(A.name), 10, P.ink, 800) +
            (dir ? '<rect x="' + (tx + tw - 92) + '" y="' + (ty - 22) + '" width="86" height="16" rx="8" fill="' + (dir === 'Reverse' ? P.drop : P.green) + '"></rect>' + txt2(tx + tw - 49, ty - 10, esc(dir), 10, '#FFFFFF', 800, 'middle') : '');
        var cx = tx;
        hd.forEach(function (h, i) { s += txt2(cx + 3, ty + 6, h[0], 8, P.muted, 700) + (h[1] ? txt2(cx + 3, ty + 16, h[1], 7.5, P.muted, 600) : ''); cx += cw[i]; });
        /* A combined relay serves both ends: its cell spans rows A and B (rowspan). */
        var combinedDl = (A.relays || []).some(function (r) { return r.combined; });
        var dlX = tx + cw.slice(0, 6).reduce(function (a, b) { return a + b; }, 0);
        ['A', 'B'].forEach(function (E, j) {
            var y = ty + 40 + j * 26, vals = [E, v(E, 'IPT N/R(A) Max') + '/' + v(E, 'IPT N/R(A) Avg'), v(E, 'VPT 110 DC LOC N/R(V)'), v(E, 'TPT N/R(ms)'), v(E, 'VPT 24 DC LOC N/R(V)'), v(E, 'VPT N/R(V)')];
            var c2 = tx;
            s += '<line x1="' + tx + '" y1="' + (y - 16) + '" x2="' + ((combinedDl && j) ? dlX : (tx + tw)) + '" y2="' + (y - 16) + '" stroke="' + P.edge + '"></line>';
            vals.forEach(function (t, i) { s += txt2(c2 + 3, y, t, 9.5, P.ink, i ? 600 : 800); c2 += cw[i]; });
            var pillY = combinedDl ? y + 13 : y;
            if (!j) ['NWKR', 'RWKR', 'NWCR', 'RWCR'].forEach(function (k) {
                var u = up(k), pc = stCol(P, u);
                s += '<rect x="' + (c2 + 1) + '" y="' + (pillY - 10) + '" width="28" height="13" rx="6.5" fill="' + pc + '"></rect>' +
                    txt2(c2 + 15, pillY, u === true ? 'Pickup' : u === false ? 'Drop' : '--', 7.5, '#FFFFFF', 800, 'middle');
                c2 += 30;
            });
        });
        if (combinedDl) s += '<line x1="' + dlX + '" y1="' + (ty + 24) + '" x2="' + dlX + '" y2="' + (ty + 96) + '" stroke="' + P.edge + '"></line>';
        /* callouts from the table to the motors */
        s += arrowTo(P, tx + 90, ty - 26, 832, 294, P.blue, 1.4, '2 3') + arrowTo(P, tx, ty + 40, 614, 538, P.blue, 1.4, '2 3');
        return s + '</svg>';
    }

    /* any other asset type: source box + bus + one box per attribute / relay */
    function genericCircuitSvg(A) {
        var P = cpal();
        A_ID = A.aid;
        var items = A.rows.slice();
        var cols = 4, cw = 236, bh = 40, gap = 16;
        var nRows = Math.ceil((items.length + (A.relays || []).length) / cols) || 1;
        var H2 = 110 + nRows * (bh + gap) + 10;
        var typeName = strip($('#drpAssetType option:selected').text() || 'Asset');
        var s = '<svg viewBox="0 0 1000 ' + H2 + '" role="img" aria-label="Circuit ' + esc(A.name) + '">' +
            '<rect x="0" y="0" width="1000" height="' + H2 + '" rx="10" fill="' + P.bg + '"></rect>' +
            '<rect x="20" y="18" width="190" height="46" rx="5" fill="' + P.relay + '"></rect>' +
            '<text x="115" y="38" text-anchor="middle" font-size="12" font-weight="700" fill="#FFFFFF">' + esc(typeName) + '</text>' +
            '<text x="115" y="56" text-anchor="middle" font-size="13" font-weight="700" fill="#FFFFFF">' + esc(A.name) + '</text>' +
            '<line x1="115" y1="64" x2="115" y2="86" stroke="' + P.cyan + '" stroke-width="2.4"></line>' +
            '<line x1="40" y1="86" x2="960" y2="86" stroke="' + P.cyan + '" stroke-width="3" stroke-linecap="round"></line>';
        var idx = 0;
        for (var i = 0; i < items.length; i++, idx++) {
            var cx = 26 + (idx % cols) * (cw + 8), cy = 110 + Math.floor(idx / cols) * (bh + gap);
            s += '<line x1="' + (cx + cw / 2) + '" y1="86" x2="' + (cx + cw / 2) + '" y2="' + cy + '" stroke="' + P.cyan + '" stroke-opacity=".45" stroke-width="1.4"></line>' +
                cBox(P, cx, cy, cw, bh, items[i], items[i].label, '');
        }
        var rl = A.relays || [];
        for (var j = 0; j < rl.length; j++, idx++) {
            var rx = 26 + (idx % cols) * (cw + 8), ry = 110 + Math.floor(idx / cols) * (bh + gap);
            s += '<line x1="' + (rx + cw / 2) + '" y1="86" x2="' + (rx + cw / 2) + '" y2="' + ry + '" stroke="' + P.cyan + '" stroke-opacity=".45" stroke-width="1.4"></line>' +
                '<rect x="' + rx + '" y="' + ry + '" width="' + cw + '" height="' + bh + '" rx="4" fill="' + P.panel + '" stroke="' + P.panelEdge + '"></rect>' +
                '<circle cx="' + (rx + 18) + '" cy="' + (ry + bh / 2) + '" r="7" fill="' + (rl[j].up ? P.green : P.drop) + '"></circle>' +
                '<text x="' + (rx + 34) + '" y="' + (ry + bh / 2 + 4) + '" font-size="12.5" font-weight="700" fill="' + P.text + '">' + esc(rl[j].name) + ' ' + (rl[j].up ? '&#8593; Pickup' : '&#8595; Drop') + '</text>';
        }
        return s + '</svg>';
    }
    function circuitHtml(A) {
        var at = assetTypeId();
        var svg = isTrackType() ? trackCircuitSvg(A) : at === 2 ? signalCircuitSvg(A) : at === 3 ? pmCircuitSvg(A) : genericCircuitSvg(A);
        /* v618.40: asset title block (LIVE, type / asset, last update) + - / Fit / + zoom */
        var e = live()[A.aid] || {}, lu = e.lastUpdated ? new Date(e.lastUpdated) : null;
        var ts = lu && !isNaN(lu.getTime()) ? pad(lu.getHours()) + ':' + pad(lu.getMinutes()) + ':' + pad(lu.getSeconds()) : '--';
        var where = [strip($('#drpSite option:selected').text()), strip($('#drpAssetType option:selected').text())].filter(function (t) { return t && !/^select/i.test(t); }).join(' · ');
        var z = (H.zoom && H.zoom[A.aid]) || 1;
        return '<div class="hv-circ">' +
            '<div class="hv-circ-head"><span class="hv-circ-live"><i></i>LIVE</span><b>' + esc(A.name) + '</b>' +
            (where ? '<span class="hv-circ-where">' + esc(where) + '</span>' : '') +
            '<span class="hv-circ-ts">Last Update: ' + ts + '</span>' +
            '<span class="hv-circ-zoom"><button type="button" data-hv-zoom="-1" data-hv-zaid="' + esc(A.aid) + '" title="Zoom out">&#8722;</button>' +
            '<button type="button" data-hv-zoom="0" data-hv-zaid="' + esc(A.aid) + '" title="Fit">Fit</button>' +
            '<button type="button" data-hv-zoom="1" data-hv-zaid="' + esc(A.aid) + '" title="Zoom in">+</button></span></div>' +
            '<div class="hv-circ-pan"><div class="hv-circ-in' + (z !== 1 ? ' hv-zoomed' : '') + '" style="width:' + Math.round(z * 100) + '%">' + svg + '</div></div></div>';
    }
    function circOpen(aid) { return H.circAll ? !H.circOff[aid] : !!H.circ[aid]; }

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
        /* history is raw mA; the reading is shown in A -> scale the trend alike */
        var ampDiv = r.amp ? (parseFloat(window.IPS_BATT_MA_PER_AMP) || 1000) : 1;
        for (var p = 0; p < T.rows.length; p++) {
            var v = num(T.rows[p].v[ci]);
            if (v !== null) pts.push({ t: T.rows[p].t, v: v / ampDiv });
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
            '<div class="th"><span>min ' + fmtR(r, Math.min.apply(null, pts.map(function (z) { return z.v; }))) + '</span><span>' +
            (g && g.min !== null && g.max !== null ? 'safe ' + fmtR(r, g.min) + ' to ' + fmtR(r, g.max) : '') + '</span><span>max ' +
            fmtR(r, Math.max.apply(null, pts.map(function (z) { return z.v; }))) + '</span></div></div>';
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
    function cardHtml(A, grpLabel) {
        if (typeof grpLabel !== 'string') grpLabel = '';   /* Array.map passes the index */
        var track = isTrackType();
        var pillCls = track ? (A.occ === true ? 'occ' : A.occ === false ? 'clear' : '') : pmPill(A);
        var co = circOpen(A.aid) && circuitAllowed();
        var html = '<article class="hv-card st-' + A.status + (co ? ' hv-wide' : '') + '" data-hv-card="' + esc(A.aid) + '">' +
            '<div class="hv-head">' + ringSvg(A) +
            '<div class="hv-ttl"><span class="hv-pill ' + pillCls + '" title="' + (track ? (A.occ === true ? 'TPR Drop (occupied)' : A.occ === false ? 'TPR Pickup (clear)' : 'TPR state unknown') : '') + '">' + esc(A.name) + '</span>' +
            (grpLabel ? '<span class="hv-grptag" title="' + esc(grpLabel) + '">' + esc(grpLabel) + '</span>' : '') +
            '<div><span class="hv-status' + A.status + '">' + STATUS_TEXT[A.status] + '</span>' + dirChip(A) + ' <span class="hv-sum">' + esc(summaryText(A)) + '</span></div></div>' +
            '<div class="hv-tools">' +
            (circuitAllowed() ? '<button type="button" class="hv-tool" data-hv-circ="' + esc(A.aid) + '" aria-pressed="' + co +
                '" title="' + (co ? 'Hide circuit' : 'Show circuit') + '"><i class="fa-solid fa-project-diagram"></i></button>' : '') +
            '</div></div>';
        var ips = isIpsType();
        /* IPS: the data-logger relays are already the Digital tiles below, so no pill row */
        if (A.relays.length && !ips) {
            html += '<div class="hv-relays">';
            for (var i = 0; i < A.relays.length; i++) {
                html += '<span class="hv-relay ' + (A.relays[i].up ? 'up' : 'dn') + '">' + esc(A.relays[i].name) + ' ' + (A.relays[i].up ? '&uarr; Pickup' : '&darr; Drop') + '</span>';
            }
            html += '</div>';
        }
        if (co) html += circuitHtml(A);
        if (isSignalType() && A.rows.length) {
            /* aspect-wise: one block per aspect, current and voltage side by side */
            html += '<div class="hv-asps">';
            for (var a = 0; a < A.rows.length;) {
                var sg = A.rows[a].sg || sigInfo(A.rows[a].label), tiles = '';
                while (a < A.rows.length && (A.rows[a].sg || sigInfo(A.rows[a].label)).g === sg.g) tiles += rowHtml(A, A.rows[a++]);
                html += '<div class="hv-asp"><div class="hv-tiles hv-pair">' + tiles + '</div></div>';
            }
            html += '</div>';
        } else if (ips) {
            html += ipsCardBodyHtml(A);
        } else {
            if (!A.dlOnly) {   /* DL-only: the relay pills above are its readings */
                html += '<div class="hv-rows hv-tiles">';
                for (var j = 0; j < A.rows.length; j++) html += rowHtml(A, A.rows[j]);
                if (!A.rows.length) html += '<div class="hv-sum">No live readings yet.</div>';
                html += '</div>';
            }
        }
        if (A.derived && A.derived.length) {
            html += '<div class="hv-derived">';
            for (var d = 0; d < A.derived.length; d++) {
                html += '<span><em>' + esc(A.derived[d].label) + '</em><b>' + fmt(A.derived[d].v) + '</b></span>';
            }
            html += '</div>';
        }
        html += trendHtml(A) + '</article>';
        return html;
    }

    /* ---- IPS card body: readings segregated Voltage / Current / Digital ----
       Same classification as the IPS table tabs. On a battery charging /
       discharging asset the Current section opens with the Σ of its bank
       readings (buildIpsCardSumModels -- the exact sum the IPS card shows). */
    var IPS_SECTIONS = [['voltage', 'Voltage', 'fa-bolt'], ['current', 'Current', 'fa-wave-square'], ['digital', 'Digital', 'fa-toggle-on']];
    function ipsCardSumTiles(A) {
        if (typeof window.buildIpsCardSumModels !== 'function') return '';
        var models = [];
        try { models = window.buildIpsCardSumModels(A.aid) || []; } catch (e) { models = []; }
        return models.map(function (m) {
            var disp = typeof window.ipsCardSumDisplay === 'function' ? window.ipsCardSumDisplay(m) : fmt(m.sum);
            var noVal = !m.count || disp === '—' || disp === '--';
            return '<div class="hv-tile hv-sumtile' + (noVal ? ' st-none' : '') + '" title="' + esc('Sum of ' + m.members + ' bank readings -- ' + m.label) + '">' +
                '<div class="hd"><span class="lb">&Sigma; ' + esc(m.label) + '</span></div>' +
                '<div class="vl">' + esc(noVal ? '--' : disp) + (m.ampere && !noVal ? ' <small>A</small>' : '') + '</div>' +
                '<div class="sl">sum of ' + m.members + ' bank' + (m.members > 1 ? 's' : '') + '</div></div>';
        }).join('');
    }
    function ipsCardBodyHtml(A) {
        var by = { voltage: [], current: [], digital: [] };
        A.rows.forEach(function (r) { (by[r.ipsTab] || by.voltage).push(r); });
        var tab = H.ipsTab || 'all';
        var sums = (tab === 'all' || tab === 'current') ? ipsCardSumTiles(A) : '';
        var h = '', any = false;
        IPS_SECTIONS.forEach(function (S) {
            if (tab !== 'all' && tab !== S[0]) return;
            var list = by[S[0]], extra = S[0] === 'current' ? sums : '';
            if (!list.length && !extra) return;
            any = true;
            h += '<div class="hv-ipsec" data-ipsec="' + S[0] + '"><div class="hv-ipsec-h"><i class="fa-solid ' + S[2] + '"></i> ' + S[1] +
                ' <small>' + list.length + '</small></div><div class="hv-rows hv-tiles">' + extra;
            for (var j = 0; j < list.length; j++) h += rowHtml(A, list[j]);
            h += '</div></div>';
        });
        return any ? h : '<div class="hv-rows hv-tiles"><div class="hv-sum">No live readings yet.</div></div>';
    }
    /* Battery bank totals across assets (BATT CHARGING n / BATT DISCHARGING n):
       the group rows of the IPS Live Grid (ipsGroupCurrentRows), so the Health
       cards show the same Σ, in amperes, as the IPS table. */
    function ipsBattSummaryHtml() {
        if (typeof window.ipsBuildFullRows !== 'function') return '';
        var rows = [];
        try { rows = window.ipsBuildFullRows() || []; } catch (e) { rows = []; }
        var groups = rows.filter(function (r) { return r && r.isGroup; });
        if (!groups.length) return '';
        var q = String(H.q || '').trim().toUpperCase();
        if (q) groups = groups.filter(function (g) {
            if (String(g.assetName || '').toUpperCase().indexOf(q) !== -1) return true;
            return (g.members || []).some(function (m) { return String(m.assetName || '').toUpperCase().indexOf(q) !== -1; });
        });
        if (!groups.length) return '';
        var bucketOf = function (g) {
            var t = (String(g.assetName || '') + ' ' + String(g.assetId || '')).toUpperCase();
            return t.indexOf('DISCHARG') !== -1 ? 'discharging' : 'charging';
        };
        groups.sort(function (a, b) {
            var x = bucketOf(a), y = bucketOf(b);
            return x === y ? String(a.assetName).localeCompare(String(b.assetName), undefined, { numeric: true }) : (x === 'charging' ? -1 : 1);
        });
        return '<div class="hv-ipssum" role="group" aria-label="Battery current totals"><div class="hv-ipssum-h"><i class="fa-solid fa-car-battery"></i> Battery current &middot; &Sigma; of banks</div><div class="hv-ipssum-row">' +
            groups.map(function (g) {
                var b = bucketOf(g), n = (g.members || []).length;
                var val = String(g.value == null ? '' : g.value), noVal = !val || val === '—' || val === '--';
                return '<div class="hv-ipssum-t ' + b + '" title="' + esc('Sum of ' + n + ' reading' + (n !== 1 ? 's' : '') + ' -- ' + g.assetName + ' / ' + g.attr) + '">' +
                    '<span class="k">' + (b === 'charging' ? 'Charging' : 'Discharging') + '</span>' +
                    '<span class="n">' + esc(g.assetName) + '</span><span class="a">' + esc(g.attr) + '</span>' +
                    '<b>' + esc(noVal ? '--' : val) + (noVal ? '' : ' <small>A</small>') + '</b>' +
                    '<span class="c">&Sigma; ' + n + ' reading' + (n !== 1 ? 's' : '') + '</span></div>';
            }).join('') + '</div></div>';
    }

    /* ---- table view (v618.14) -------------------------------------------- */
    /* Signal: one table per aspect make-up (616 renderSignalAspectTables) */
    function tableHtml(shown) {
        if (!isSignalType()) return tableHtmlOne(shown);
        return sigSections(shown).map(function (S) { return secHead(S) + tableHtmlOne(S.list); }).join('');
    }
    function tableHtmlOne(shown) {
        var cols = [];
        var seen = {};
        var sig = isSignalType();
        shown.forEach(function (A) {
            A.rows.concat(A.derived || []).forEach(function (r) {
                if (seen[r.k]) return;
                seen[r.k] = 1;
                cols.push({ k: r.k, key: r.key, label: r.label, sub: r.sub, derived: !!r.derived, range: r.range, sg: r.sg });
            });
        });
        if (isPmType()) cols.sort(function () { return 0; });
        else if (sig) cols.sort(function (x, y) {
            if (x.derived !== y.derived) return x.derived ? 1 : -1;
            return sigCmp({ sg: x.sg || sigInfo(x.label), label: x.label }, { sg: y.sg || sigInfo(y.label), label: y.label });
        });
        else cols.sort(function (x, y) {
            if (x.derived !== y.derived) return x.derived ? 1 : -1;
            var ix = TRACK_ORDER.indexOf(x.key), iy = TRACK_ORDER.indexOf(y.key);
            if (ix < 0) ix = 99;
            if (iy < 0) iy = 99;
            return ix - iy || x.label.localeCompare(y.label);
        });
        var h = '<div class="hv-tblwrap"><table class="hv-tbl"><thead>';
        if (false && sig) {   /* aspect group header row (hv-agrp) removed on request */
            /* aspect header: one cell spanning each aspect's current + voltage columns
               (Asset / Health / Relays below it); this row scrolls, the column row stays */
            h += '<tr class="hv-agrp"><th colspan="3"></th>';
            for (var gi = 0; gi < cols.length;) {
                var g0 = cols[gi].sg || sigInfo(cols[gi].label), span = 0;
                while (gi < cols.length && (cols[gi].sg || sigInfo(cols[gi].label)).g === g0.g) { span++; gi++; }
                h += '<th colspan="' + span + '"><span><i style="background:' + (g0.col || 'var(--e7-text-3,#8D9CB2)') + '"></i>' + esc(g0.name) + '</span></th>';
            }
            h += '</tr>';
        }
        h += '<tr>' +
            '<th scope="col" class="hv-sticky">Asset</th><th scope="col">Health</th>' + (isPmType() ? '<th scope="col">Direction</th>' : '') + '<th scope="col">Relays</th>';
        cols.forEach(function (c) {
            var g = c.range;
            /* Safe range moved from a second header line into the tooltip: the
               "23.00 TO 40.00" line crowded the header (cells still flag
               out-of-range values). */
            var rgTip = (g && g.min !== null && g.max !== null) ? ' title="' + esc(c.label + ' -- safe range ' + fmt(g.min) + ' to ' + fmt(g.max)) + '"' : '';
            h += '<th scope="col" class="' + (c.derived ? 'dv' : '') + '"' + rgTip + '><div>' + esc(c.label) + '</div>' +
                (c.sub ? '<small>' + esc(c.sub) + '</small>' : '') + '</th>';
        });
        h += '</tr></thead><tbody>';
        function assetRow(A, child) {
            var map = {};
            A.rows.concat(A.derived || []).forEach(function (r) { map[r.k] = r; });
            var track = isTrackType();
            var pillCls = track ? (A.occ === true ? 'occ' : A.occ === false ? 'clear' : '') : pmPill(A);
            h += '<tr data-hv-aid="' + esc(A.aid) + '" class="st-' + A.status + (H.open[A.aid] ? ' open' : '') + (child ? ' hv-child' : '') + '">' +
                '<th scope="row" class="hv-sticky"><button type="button" class="hv-x" data-hv-open="' + esc(A.aid) + '" aria-expanded="' + !!H.open[A.aid] + '" title="Circuit and trend">' +
                '<i class="fa-solid fa-chevron-right"></i></button><span class="hv-pill ' + pillCls + '">' + esc(A.name) + '</span></th>' +
                '<td><div class="hv-hcell">' + ringSvg(A).replace('width="40" height="40"', 'width="30" height="30"') +
                '<span class="hv-status ' + A.status + '">' + STATUS_TEXT[A.status] + '</span></div></td>' +
                (isPmType() ? '<td>' + (dirChip(A) || '<span class="hv-na">--</span>') + (A.pmWhen && A.pmWhen !== '--' ? '<small class="hv-when">' + esc(A.pmWhen) + '</small>' : '') + '</td>' : '') + '<td class="hv-rl">';
            A.relays.forEach(function (rl) { h += '<span class="hv-relay ' + (rl.up ? 'up' : 'dn') + '">' + esc(rl.name) + ' ' + (rl.up ? '&uarr; Pickup' : '&darr; Drop') + '</span>'; });
            h += '</td>';
            cols.forEach(function (c) {
                var r = map[c.k];
                if (!r) { h += '<td class="hv-na">--</td>'; return; }
                if (r.derived) { h += '<td class="dv">' + fmt(r.v) + '</td>'; return; }
                h += '<td><button type="button" class="hv-cell st-' + r.st + (H.open[A.aid] && H.trendKey[A.aid] === r.k ? ' sel' : '') +
                    '" data-hv-cell="' + esc(A.aid) + '" data-hv-k="' + esc(r.k) + '" title="' + esc(rowTip(r)) + '">' +
                    '<span class="v' + (r.relay ? ' hv-rv ' + (r.up ? 'up' : 'dn') : '') + '">' + dispV(r) + ' <i>' + flagText(r) + '</i></span>' +
                    gaugeHtml(r, false) + '</button></td>';
            });
            h += '</tr>';
            if (H.open[A.aid]) {
                h += '<tr class="hv-exp"><td colspan="' + (cols.length + 3 + (isPmType() ? 1 : 0)) + '"><div class="hv-expin">' +
                    (circuitAllowed() ? '<div class="hv-expc">' + circuitHtml(A) + '</div>' : '') +
                    '<div class="hv-expt">' + (trendHtml(A) || '<div class="hv-sum" style="padding:10px">Click a reading in this row for its trend.</div>') + '</div>' +
                    '</div></td></tr>';
            }
        }
        /* v618.26 IPS: battery banks (asset names with CHARGING / DISCHARGING,
           same rule as the IPS table) -> one SUM row per bucket + base name,
           every column = sum of the banks' values; the expand button lists
           each bank with its own values. */
        var groups = isIpsType() ? ipsBattGroups(shown) : null;
        var done = {};
        shown.forEach(function (A) {
            if (done[A.aid]) return;
            var g = groups && groups.byAid[A.aid];
            if (!g) { assetRow(A, false); return; }
            g.members.forEach(function (m) { done[m.aid] = 1; });
            var open = !!H.gopen[g.key];
            var worst = g.members.reduce(function (w, m) { return m.sev > w.sev ? m : w; }, g.members[0]);
            h += '<tr class="hv-grp st-' + worst.status + (open ? ' open' : '') + '">' +
                '<th scope="row" class="hv-sticky"><button type="button" class="hv-x" data-hv-gopen="' + esc(g.key) + '" aria-expanded="' + open + '" title="Show each bank">' +
                '<i class="fa-solid fa-chevron-right"></i></button><span class="hv-pill ' + (g.bucket === 'charging' ? 'clear' : 'rev') + '">&Sigma; ' + esc(g.base) + '</span>' +
                '<small class="hv-gsub">' + (g.bucket === 'charging' ? 'Charging' : 'Discharging') + ' &middot; ' + g.members.length + ' banks</small></th>' +
                '<td><span class="hv-status ' + worst.status + '">' + STATUS_TEXT[worst.status] + '</span></td><td class="hv-rl"></td>';
            cols.forEach(function (c) {
                var sum = 0, n = 0, amp = false;
                g.members.forEach(function (m) {
                    var r = null;
                    m.rows.concat(m.derived || []).forEach(function (x) { if (x.k === c.k) r = x; });
                    if (r && !r.relay && r.v !== null && r.v !== undefined && !isNaN(r.v)) { sum += r.v; n++; if (r.amp) amp = true; }
                });
                h += n ? '<td class="hv-sumcell" title="Sum of ' + n + ' bank' + (n > 1 ? 's' : '') + '"><b>' + (amp ? fmtA(sum) : fmt(sum)) + '</b><small>&Sigma; ' + n + '</small></td>' : '<td class="hv-na">--</td>';
            });
            h += '</tr>';
            if (open) g.members.forEach(function (m) { assetRow(m, true); });
        });
        return h + '</tbody></table></div>';
    }
    function ipsBattGroups(shown) {
        var by = {}, byAid = {};
        shown.forEach(function (A) {
            var up = String(A.name || '').toUpperCase();
            var bucket = up.indexOf('DISCHARGING') !== -1 ? 'discharging' : up.indexOf('CHARGING') !== -1 ? 'charging' : null;
            if (!bucket) return;
            var base = String(A.name || '').replace(/[\s\-_]*\d+\s*$/, '').trim() || String(A.name || '');
            var key = bucket + '|' + base;
            (by[key] = by[key] || { key: key, bucket: bucket, base: base, members: [] }).members.push(A);
        });
        Object.keys(by).forEach(function (k) {
            var g = by[k];
            if (g.members.length < 2) return;
            g.members.sort(function (a, b) { return a.name.localeCompare(b.name, undefined, { numeric: true }); });
            g.members.forEach(function (m) { byAid[m.aid] = g; });
        });
        return { byAid: byAid };
    }

    function render(force) {
        var host = document.getElementById('tlHealthView');
        if (!host || !H.on) return;
        var ids = assetIds();
        var list = ids.map(readAsset);
        var counts = { fault: 0, watch: 0, ok: 0, nodata: 0 };
        for (var i = 0; i < list.length; i++) counts[list[i].status]++;
        /* Table mode for IPS uses the flat 616 grid (self-segregating), so the per-tab
           row filter only applies to the CARD view. */
        var ipsFlatTable = H.view === 'table' && isIpsType() && typeof window.ipsBuildTableHtml === 'function';
        var ipsTabNow = ipsFlatTable ? 'all' : H.ipsTab;
        /* IPS cards: how many assets carry each kind of reading (shown on the
           Voltage / Current / Digital buttons) and the battery bank totals,
           both taken BEFORE the tab filter below trims the rows */
        var ipsCnt = null, ipsSumHtml = '';
        if (isIpsType() && !ipsFlatTable) {
            ipsCnt = { all: list.length, voltage: 0, current: 0, digital: 0 };
            list.forEach(function (A) {
                var has = {};
                A.rows.forEach(function (r) { has[r.ipsTab || 'voltage'] = 1; });
                for (var t in has) if (ipsCnt.hasOwnProperty(t)) ipsCnt[t]++;
            });
            if (H.view !== 'table' && (ipsTabNow === 'all' || ipsTabNow === 'current')) ipsSumHtml = ipsBattSummaryHtml();
        }
        if (isIpsType() && !ipsFlatTable && ipsTabNow && ipsTabNow !== 'all') {
            list.forEach(function (A) {
                A.rows = A.rows.filter(function (r) { return (r.ipsTab || 'voltage') === ipsTabNow; });
                if (ipsTabNow !== 'digital') A.relays = [];
            });
            /* 616 parity (IPS card tabs): a tab lists only the assets that HAVE readings
               of that kind. Before, every asset stayed as an empty card, so the
               Voltage / Current / Digital toggle looked like it did nothing. */
            list = list.filter(function (A) { return A.rows.length > 0 || (A.relays && A.relays.length > 0); });
        }
        var q = H.q.trim().toUpperCase();
        /* "Find asset" filters `shown` (cards / non-IPS table), but the IPS flat grid
           (Table view) is built from window.ipsBuildFullRows() and bypassed that filter.
           Match asset name here -- the same rule as the card view -- so Find asset works
           in the IPS Table view too. Returns null when the builder is absent (old
           fallback). */
        var ipsGridRows = function () {
            if (typeof window.ipsBuildFullRows !== 'function') return null;
            var _rows = window.ipsBuildFullRows();
            return q ? _rows.filter(function (r) { return r && r.assetName && String(r.assetName).toUpperCase().indexOf(q) !== -1; }) : _rows;
        };
        var shown = list.filter(function (A) {
            if (q && A.name.toUpperCase().indexOf(q) === -1) return false;
            if (H.filter === 'attn') return A.status === 'fault' || A.status === 'watch';
            if (H.filter === 'occ') return A.occ === true;
            return true;
        });
        /* Group by what the asset carries: assets with readings (RDPMS, with or
           without DataLogger) first, DataLogger-only assets after them, so the
           two card shapes are not interleaved. Name / worst order inside a group. */
        var grp = function (A) { return A.dlOnly ? 1 : 0; };
        shown.sort(H.sort === 'worst'
            ? function (a, b) { return (grp(a) - grp(b)) || (b.sev - a.sev) || a.name.localeCompare(b.name, undefined, { numeric: true }); }
            : function (a, b) { return (grp(a) - grp(b)) || a.name.localeCompare(b.name, undefined, { numeric: true }); });

        /* cheap signature: only rebuild DOM when something visible changed */
        var sig = H.view + (H.ipsTab || '') + (H.ipsTableTab || '') + ipsSumHtml + JSON.stringify(H.gopen) + H.sort + H.filter + q + JSON.stringify(H.trendKey) + JSON.stringify(H.circ) + JSON.stringify(H.circOff) + H.circAll +
            JSON.stringify(H.open) + (pageLight() ? 'L' : 'D') + '|' +
            shown.map(function (A) {
                return A.aid + ':' + A.status + A.score + A.occ + A.relays.map(function (r) { return r.up ? 1 : 0; }).join('') + ':' +
                    /* raw value, not fmt(): small changes (0.003 -> 0.000 A) rounded to the same
                       text, the signature never changed and the IPS table stopped updating live */
                    A.rows.map(function (r) { return r.k + '=' + String(r.v) + r.st + (r.stale ? 's' : ''); }).join(',');
            }).join(';');
        if (!force && sig === H.sig) return;
        H.sig = sig;

        var ipsCntTxt = function (t) { return ipsCnt ? ' <small class="hv-segn">' + (ipsCnt[t] || 0) + '</small>' : ''; };
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
            (isIpsType() && H.view !== 'table' ? '<div class="hv-seg" role="group" aria-label="IPS readings">' + seg('ipsTab', 'all', 'All' + ipsCntTxt('all')) + seg('ipsTab', 'voltage', 'Voltage' + ipsCntTxt('voltage')) +
                seg('ipsTab', 'current', 'Current' + ipsCntTxt('current')) + seg('ipsTab', 'digital', 'Digital' + ipsCntTxt('digital')) + '</div>' : '') +
            '<div class="hv-seg" role="group" aria-label="Sort">' + seg('sort', 'worst', 'Worst first') + seg('sort', 'name', 'Name') + '</div>' +
            (H.grid ? '<button type="button" class="hv-tool" data-hv-classic="1" title="Show the classic grid table" style="width:auto;padding:0 10px">Classic grid</button>' :
                '<div class="hv-seg" role="group" aria-label="Layout">' + seg('view', 'cards', '<i class="fa-solid fa-table-cells-large"></i> Cards') + seg('view', 'table', '<i class="fa-solid fa-table"></i> Table') + '</div>') +
            (circuitAllowed() && H.view === 'cards' ? '<button type="button" class="hv-tool" data-hv-circall="1" aria-pressed="' + H.circAll + '" title="Circuit on all cards" style="width:auto;padding:0 10px">' +
                '<i class="fa-solid fa-project-diagram"></i> Circuits</button>' : '') +
            '<input type="search" id="hvSearch" class="hv-search" placeholder="Find asset" aria-label="Find asset" value="' + esc(H.q) + '">' +
            '</div>';

        var body;
        if (!ids.length) {
            body = '<div class="hv-empty"><i class="fas fa-satellite-dish" style="font-size:22px;opacity:.5"></i><br>' +
                'Select site and asset type, then Search to start live data.</div>';
        } else if (!shown.length && !(H.view === 'table' && isIpsType() && typeof (window.ipsBuildFullTableHtml || window.ipsBuildTableHtml) === 'function')) {
            body = ipsCnt && ipsTabNow && ipsTabNow !== 'all' && !q
                ? '<div class="hv-empty">No ' + esc(ipsTabNow) + ' readings for the selected IPS assets.</div>'
                : '<div class="hv-empty">Nothing matches this filter.</div>';
        } else {
            /* IPS Table: the classic flat, segregated "IPS Live Grid" (616 parity) --
               one row per asset x attribute, Voltage / Current / Digital tabs, battery
               current grouped and summed in A -- instead of a wide relay matrix with
               blank cells. Reuses the exact same builder as the Table view. */
            /* the FULL tabbed builder (plain per-tab S.No via CSS counter, width-matched
               headers) -- NOT window.ipsBuildTableHtml, which ext overrides with an
               export-only table (static S.No the counter turns into "01 / 010") */
            var ipsBuildFlat = window.ipsBuildFullTableHtml || window.ipsBuildTableHtml;
            var ipsFlat = H.view === 'table' && isIpsType() && typeof ipsBuildFlat === 'function';
            /* the flat grid owns its tab in window.ipsActiveTab (its own tab bar + the
               delegated handler drive it); mirror it into H.ipsTableTab so it survives
               the health view's live re-renders */
            if (ipsFlat) {
                if (typeof window.ensureIpsTableStyles === 'function') { try { window.ensureIpsTableStyles(); } catch (e) { } }
                var _ipt = (window.ipsActiveTab === 'current' || window.ipsActiveTab === 'digital') ? window.ipsActiveTab
                    : (H.ipsTableTab === 'current' || H.ipsTableTab === 'digital') ? H.ipsTableTab : 'voltage';
                H.ipsTableTab = _ipt;
                try { window.ipsActiveTab = _ipt; } catch (e) { }
            }
            /* pass always-flattened rows (BATT members included) so the grid's group
               rows are expandable with real children -- the builder's own expander is
               the ext one, gated off by IPS_SHOW_BANK_BREAKUP */
            var _ipsRowsForGrid = ipsGridRows();
            body = ipsFlat
                ? (ipsBuildFlat(_ipsRowsForGrid || undefined) || '<div class="hv-empty">No IPS readings.</div>')
                : H.view === 'table'
                    ? tableHtml(shown)
                    : isSignalType()
                        ? sigGridHtml(sigSections(shown))
                        : ipsSumHtml + '<div class="hv-grid">' + shown.map(cardHtml).join('') + '</div>';
        }
        /* IPS flat grid: on a live tick that did not change the grid's STRUCTURE
           (same assets / attributes / tab), refresh value + timestamp cells in
           place instead of rebuilding the table. A full innerHTML rebuild every
           second destroyed the table and the user's group-expand (BATT CHARGING /
           DISCHARGING) never stayed open. */
        if (ipsFlat && typeof window.ipsBuildFullRows === 'function') {
            var _ipsTbl = host.querySelector('#ipsLiveTable');
            if (_ipsTbl && _ipsTbl.tBodies && _ipsTbl.tBodies[0]) {
                var _ipsRows = ipsGridRows() || [];
                var _trs = _ipsTbl.tBodies[0].rows;
                var _struct = (H.ipsTableTab || 'voltage') + '::' + _ipsRows.map(function (r) { return String(r.assetId) + '|' + String(r.attrKey); }).join(',');
                if (_trs.length === _ipsRows.length && H._ipsFlatStruct === _struct) {
                    for (var _ri = 0; _ri < _ipsRows.length; _ri++) {
                        var _r = _ipsRows[_ri], _tr = _trs[_ri];
                        if (_tr.getAttribute('data-ips-id') !== String(_r.assetId) || _tr.getAttribute('data-attr') !== String(_r.attrKey)) { H._ipsFlatStruct = null; break; }
                        var _vc = _tr.querySelector('.ips-val'); var _tc = _tr.querySelector('.ips-ts');
                        if (_vc) {
                            var _vt = String(_r.value == null ? '' : _r.value);
                            if (_vc.textContent !== _vt) { _vc.innerHTML = _r.dlClass ? '<span class="rdpms-dl-badge ' + _r.dlClass + '">' + esc(_vt) + '</span>' : esc(_vt); }
                        }
                        if (_tc && _tc.textContent !== String(_r.ts == null ? '' : _r.ts)) { _tc.textContent = String(_r.ts == null ? '' : _r.ts); }
                    }
                    if (H._ipsFlatStruct === _struct) { H.sig = sig; return; }  /* updated in place; skip the full rebuild */
                }
                H._ipsFlatStruct = _struct;   /* structure changed -> fall through to a full rebuild */
            }
        } else {
            H._ipsFlatStruct = null;
        }
        /* keep focus / caret in the search box across rebuilds */
        var act = document.activeElement;
        var hadSearch = act && act.id === 'hvSearch';
        var caret = hadSearch ? act.selectionStart : 0;
        var wrap = host.querySelector('.hv-tblwrap');
        var sx = wrap ? wrap.scrollLeft : 0;
        var sy = wrap ? wrap.scrollTop : 0;   /* v618.30: keep vertical position across live rebuilds */
        host.style.setProperty('--hv-bg', cardBg());
        host.innerHTML = bar + body;
        /* flat IPS grid: show the tab the health view is on (its own tab bar drives it) */
        if (ipsFlat && typeof window.ipsApplyTab === 'function') {
            try { window.ipsApplyTab((H.ipsTableTab === 'current' || H.ipsTableTab === 'digital') ? H.ipsTableTab : 'voltage'); } catch (e) { }
            /* record the freshly built structure so the next live tick updates in place */
            if (typeof window.ipsBuildFullRows === 'function') {
                try {
                    H._ipsFlatStruct = (H.ipsTableTab || 'voltage') + '::' +
                        (ipsGridRows() || []).map(function (r) { return String(r.assetId) + '|' + String(r.attrKey); }).join(',');
                } catch (e) { H._ipsFlatStruct = null; }
            }
        }
        var wrap2 = host.querySelector('.hv-tblwrap');
        if (wrap2) {
            /* expanded row content stays in view while the table scrolls sideways */
            var ew = Math.max(320, wrap2.clientWidth - 16);
            var exps = wrap2.querySelectorAll('.hv-expin');
            for (var ei = 0; ei < exps.length; ei++) exps[ei].style.width = ew + 'px';
            if (sx) wrap2.scrollLeft = sx;
            if (sy) wrap2.scrollTop = sy;
            /* v618.30: bring a just-opened row to the top, just under the sticky header */
            if (H.scrollTo) {
                var tr = wrap2.querySelector('tr[data-hv-aid="' + String(H.scrollTo).replace(/"/g, '\\"') + '"]');
                H.scrollTo = null;
                if (tr) {
                    var th = wrap2.querySelector('thead');
                    wrap2.scrollTop += tr.getBoundingClientRect().top - wrap2.getBoundingClientRect().top - (th ? th.offsetHeight : 0);
                    var wr = wrap2.getBoundingClientRect();
                    if (wr.top < 0 || wr.top > window.innerHeight * 0.5) wrap2.scrollIntoView({ block: 'start', behavior: 'smooth' });
                }
            }
        }
        if (hadSearch) {
            var s = document.getElementById('hvSearch');
            if (s) { s.focus(); try { s.setSelectionRange(caret, caret); } catch (e) { /* ignore */ } }
        }
    }

    function onViewClick(e) {
        var t = e.target;
        var cl = t.closest ? t.closest('[data-hv-classic]') : null;
        if (cl) { H.classic = true; leave(); $('.tl-vmode-btn[data-vmode="Table"]').addClass('active').attr('aria-pressed', 'true'); return; }
        var zb = t.closest ? t.closest('[data-hv-zoom]') : null;
        if (zb) {
            var zaid = zb.getAttribute('data-hv-zaid'), zd = parseInt(zb.getAttribute('data-hv-zoom'), 10);
            H.zoom = H.zoom || {};
            var nz = zd === 0 ? 1 : Math.max(1, Math.min(3, ((H.zoom[zaid] || 1) + zd * 0.25)));
            H.zoom[zaid] = nz;
            var zin = zb.closest('.hv-circ') && zb.closest('.hv-circ').querySelector('.hv-circ-in');
            if (zin) { zin.style.width = Math.round(nz * 100) + '%'; zin.classList.toggle('hv-zoomed', nz !== 1); }
            e.preventDefault(); e.stopPropagation();
            return;
        }
        var b = t.closest ? t.closest('[data-hv-ipstt],[data-hv-gopen],[data-hv-ipstab],[data-hv-filter],[data-hv-sort],[data-hv-view],[data-hv-circ],[data-hv-circall],[data-hv-open],[data-hv-cell],[data-hv-row]') : null;
        if (!b) return;
        if (b.hasAttribute('data-hv-ipstt')) H.ipsTableTab = b.getAttribute('data-hv-ipstt');
        else if (b.hasAttribute('data-hv-gopen')) { var gk = b.getAttribute('data-hv-gopen'); H.gopen[gk] = !H.gopen[gk]; }
        else if (b.hasAttribute('data-hv-ipstab')) { H.ipsTab = b.getAttribute('data-hv-ipstab'); tagIpsCards(); }
        else if (b.hasAttribute('data-hv-filter')) H.filter = b.getAttribute('data-hv-filter');
        else if (b.hasAttribute('data-hv-sort')) H.sort = b.getAttribute('data-hv-sort');
        else if (b.hasAttribute('data-hv-view')) H.view = b.getAttribute('data-hv-view');
        else if (b.hasAttribute('data-hv-circall')) { H.circAll = !H.circAll; H.circ = {}; H.circOff = {}; }
        else if (b.hasAttribute('data-hv-circ')) {
            var ca = b.getAttribute('data-hv-circ');
            if (H.circAll) H.circOff[ca] = !H.circOff[ca];
            else H.circ[ca] = !H.circ[ca];
        } else if (b.hasAttribute('data-hv-open')) {
            /* v618.30: one expanded row at a time; the opened row scrolls to the top */
            var oa = b.getAttribute('data-hv-open');
            if (H.open[oa]) H.open[oa] = false;
            else { H.open = {}; H.open[oa] = true; H.scrollTo = oa; }
        } else if (b.hasAttribute('data-hv-cell')) {
            var cA = b.getAttribute('data-hv-cell');
            var cK = b.getAttribute('data-hv-k');
            if (H.open[cA] && H.trendKey[cA] === cK) { H.open[cA] = false; }
            else {
                if (!H.open[cA]) { H.open = {}; H.scrollTo = cA; }
                H.open[cA] = true; H.trendKey[cA] = cK;
            }
        } else if (b.hasAttribute('data-hv-row')) {
            var aid = b.getAttribute('data-hv-row');
            var k = b.getAttribute('data-hv-k');
            H.trendKey[aid] = (H.trendKey[aid] === k) ? null : k;
        }
        H.sig = '';
        render(true);
    }

    /* v618.25 -- IPS CARD VIEW: All | Voltage | Current | Digital filter in
       the IPS grid header; every attribute row is tagged after each render. */
    var IPSF_CSS = '' +
        '.ipsf-seg{display:inline-flex;gap:2px;padding:2px;border-radius:9px;border:1px solid var(--e7-border,rgba(127,140,160,.3));margin:0 8px;}' +
        '.ipsf-seg button{all:unset;cursor:pointer;font-size:11.5px;padding:3px 10px;border-radius:7px;color:var(--e7-text-2,inherit);}' +
        '.ipsf-seg button[aria-pressed="true"]{background:var(--brand,#22d3ee);color:#04161A;font-weight:600;}' +
        '#ipsCardGrid[data-ipsf="voltage"] [data-ipst]:not([data-ipst="voltage"]),' +
        '#ipsCardGrid[data-ipsf="current"] [data-ipst]:not([data-ipst="current"]),' +
        '#ipsCardGrid[data-ipsf="digital"] [data-ipst]:not([data-ipst="digital"]){display:none !important;}' +
        '#ipsCardGrid .ips-asset-card.ipsf-hide{display:none !important;}' +
        '#ipsCardGrid .ipsf-empty{grid-column:1/-1;padding:28px;text-align:center;opacity:.65;font-size:13px;}' +
        '.ips-relay-val{font-weight:700;font-size:11.5px;padding:1px 8px;border-radius:999px;border:1.5px solid currentColor;}' +
        '.ips-relay-val.pickup{color:var(--e7-green, #10b981);} .ips-relay-val.drop{color:var(--e7-amber, #f59e0b);}';
    function tagIpsCards() {
        var grid = document.getElementById('ipsCardGrid');
        if (!grid) return;
        if (!document.getElementById('ipsfCss')) {
            var st = document.createElement('style'); st.id = 'ipsfCss'; st.textContent = IPSF_CSS; document.head.appendChild(st);
        }
        var head = document.querySelector('.ips-grid-header');
        if (head && !head.querySelector('.ipsf-seg')) {
            var sg = document.createElement('div');
            sg.className = 'ipsf-seg';
            sg.setAttribute('role', 'group');
            sg.setAttribute('aria-label', 'IPS readings');
            sg.innerHTML = [['all', 'All'], ['voltage', 'Voltage'], ['current', 'Current'], ['digital', 'Digital']].map(function (t) {
                return '<button type="button" data-ipsf="' + t[0] + '">' + t[1] + '</button>';
            }).join('');
            sg.addEventListener('click', function (e) {
                var bt = e.target.closest ? e.target.closest('[data-ipsf]') : null;
                if (!bt) return;
                H.ipsTab = bt.getAttribute('data-ipsf');
                tagIpsCards();
                H.sig = '';
            });
            var badge = head.querySelector('.ips-grid-badge');
            if (badge) head.insertBefore(sg, badge); else head.appendChild(sg);
        }
        var segBtns = document.querySelectorAll('.ipsf-seg [data-ipsf]');
        for (var i = 0; i < segBtns.length; i++) segBtns[i].setAttribute('aria-pressed', String(segBtns[i].getAttribute('data-ipsf') === (H.ipsTab || 'all')));
        grid.setAttribute('data-ipsf', H.ipsTab || 'all');
        var rows = grid.querySelectorAll('.ips-attr-row');
        for (var r = 0; r < rows.length; r++) {
            var row = rows[r];
            if (row.closest('.ipsv2-sum')) { row.setAttribute('data-ipst', 'current'); continue; }
            var v = row.querySelector('[data-attr]');
            var card = row.closest('.ips-asset-card');
            if (!v || !card) continue;
            var nm = row.querySelector('.ips-attr-name');
            row.setAttribute('data-ipst', ipsTabOf(card.getAttribute('data-ips-id'), v.getAttribute('data-attr'), nm ? nm.getAttribute('title') || nm.textContent : ''));
        }
        var sums = grid.querySelectorAll('.ipsv2-sum');
        for (var q = 0; q < sums.length; q++) sums[q].setAttribute('data-ipst', 'current');
        /* A tab lists only the assets that HAVE readings of that kind (as the
           e7mriv2web IPS tabs do) -- an asset with nothing in the tab is hidden
           instead of showing as an empty card. Each button shows its count. */
        var tab = H.ipsTab || 'all', cards = grid.querySelectorAll('.ips-asset-card'), shown = 0;
        var cnt = { all: cards.length, voltage: 0, current: 0, digital: 0 };
        for (var c = 0; c < cards.length; c++) {
            var has = {};
            var tagged = cards[c].querySelectorAll('[data-ipst]');
            for (var t = 0; t < tagged.length; t++) has[tagged[t].getAttribute('data-ipst')] = true;
            for (var kk in has) if (cnt.hasOwnProperty(kk)) cnt[kk]++;
            var match = tab === 'all' || !!has[tab];
            cards[c].classList.toggle('ipsf-hide', !match);
            if (match) shown++;
        }
        for (var b = 0; b < segBtns.length; b++) {
            var key = segBtns[b].getAttribute('data-ipsf'), lbl = { all: 'All', voltage: 'Voltage', current: 'Current', digital: 'Digital' }[key];
            var txt = lbl + ' (' + (cnt[key] || 0) + ')';
            if (segBtns[b].textContent !== txt) segBtns[b].textContent = txt;
        }
        var empty = grid.querySelector('.ipsf-empty');
        if (!shown && cards.length) {
            if (!empty) { empty = document.createElement('div'); empty.className = 'ipsf-empty'; grid.appendChild(empty); }
            empty.textContent = 'No ' + tab + ' readings for the selected IPS assets.';
        } else if (empty) empty.parentNode.removeChild(empty);
    }
    function hookIps() {
        ['renderIpsGridView', 'updateIpsGridIncremental'].forEach(function (fname) {
            var f = window[fname];
            if (typeof f !== 'function' || f.__ipsf) return;
            var w = function () { var r = f.apply(this, arguments); try { tagIpsCards(); } catch (e) { /* optional */ } return r; };
            for (var k in f) if (Object.prototype.hasOwnProperty.call(f, k)) w[k] = f[k];
            w.__ipsf = true;
            window[fname] = w;
        });
    }

    /* ======================================================================
       v618.27 / Energy7 THEMED CHARTS -- every ECharts chart on Telemetry Live
       (IPS / PM / RDPMS / Graph view / waveforms) was written for the old
       navy "Aurora Glass" theme: white text / grid on a navy background.
       echarts.init is wrapped once: every setOption is translated to the
       Energy7 theme tokens (computed by tlThemePalette) in BOTH schemes --
       white / dark-ink text -> --e7-heading/text/text-2/text-3, faint grid
       -> --e7-grid-line, axis lines -> --e7-border-2, navy backgrounds ->
       transparent / --e7-surface, tooltips -> surface / border / text.
       Series colours are left alone (only legacy white ink is fixed in light
       mode). On a light/dark toggle the chrome colours of every live chart
       are re-applied from the raw colours (colour leaves only -- no data,
       ranges or other settings are re-sent).
       ====================================================================== */
    function pageIsLightNow() {
        var a = (document.body && document.body.getAttribute('data-aurora')) || document.documentElement.getAttribute('data-aurora') || '';
        return String(a).toLowerCase() === 'light';
    }
    var DARK_BG = /^(#0a1228|#0e1530|#0b1220|#060914|#0a0f24|#0f172a|#111827|#0b1426|#0a1a2e|#0c1228|#080c1c|#0a0f1a|#0d1220|#04121c|#0f2233|#0b1530)$/i;
    var DARK_RGBA = /^rgba\((?:15,23,42|5,9,24|6,9,20|2,6,18|10,18,40|14,21,48|11,21,48|10,15,36),([\d.]+)\)$/;
    var TEXTY = /label|text|title|name|legend/i;
    var COLOUR_STR = /^(#[0-9a-f]{3,8}|rgba?\([^)]*\)|white|transparent)$/i;
    var TOKEN_ROLE = {};   /* computed token value -> palette role, both schemes */
    function chartPal() {
        var P = (typeof window.tlThemePalette === 'function') ? window.tlThemePalette() : null;
        if (P) {
            for (var r in P) if (Object.prototype.hasOwnProperty.call(P, r) && typeof P[r] === 'string') {
                var key = P[r].replace(/\s+/g, '').toLowerCase();
                if (!TOKEN_ROLE[key]) TOKEN_ROLE[key] = r;
            }
        }
        return P;
    }
    function inkRole(P, a, texty) {
        if (texty) return a >= 0.85 ? P.heading : a >= 0.6 ? P.text : a >= 0.45 ? P.text2 : a >= 0.3 ? P.text3 : P.text4;
        return a >= 0.3 ? P.text3 : a >= 0.1 ? P.axis : P.grid;
    }
    function themeColour(v, key, path, P) {
        if (typeof v !== 'string' || !P) return v;
        var s = v.replace(/\s+/g, '').toLowerCase();
        var inSeries = path[0] === 'series';
        if (inSeries && !P.light) return v;                     /* dark mode: series untouched */
        var isBg = /background/i.test(key || '');
        var inZoom = path.indexOf('dataZoom') !== -1;
        var inTip = path.indexOf('tooltip') !== -1;
        var texty = TEXTY.test(key || '');
        for (var i = 0; i < path.length && !texty; i++) if (TEXTY.test(path[i])) texty = true;
        var w = /^rgba?\(255,255,255(?:,([\d.]+))?\)$/.exec(s);
        var isWhite = !!w || s === '#fff' || s === '#ffffff' || s === 'white';
        var wa = w && w[1] !== undefined ? parseFloat(w[1]) : 1;
        var dk = /^rgba\((?:15,23,42|11,21,48),([\d.]+)\)$/.exec(s);
        if (isBg) {
            var dr = DARK_RGBA.exec(s);
            if (DARK_BG.test(s) || (dr && parseFloat(dr[1]) >= 0.4) || (isWhite && wa >= 0.9) || /^rgba\(241,245,249,/.test(s) || s === '#f8fafc' || s === '#f1f5f9') {
                if (inTip) return P.surface;
                if (inZoom) return P.surface2;
                return path.length <= 1 ? 'transparent' : P.surface;
            }
            if (isWhite && wa <= 0.2) return P.surface2;
            return v;
        }
        if (inSeries) {                                       /* light mode: legacy white ink only */
            if (isWhite && key === 'borderColor') return P.surface;
            if (isWhite) return inkRole(P, wa, texty);
            return v;
        }
        if (isWhite) return inkRole(P, wa, texty);
        if (dk) return inkRole(P, parseFloat(dk[1]), texty);
        if (s === '#1e293b' || s === '#334155' || s === '#0f172a' || s === '#0b1530') return texty ? P.heading : (path.indexOf('splitLine') !== -1 ? P.grid : P.axis);
        if (s === '#e2e8f0' || s === '#cbd5e1' || s === '#e6edf6') return texty ? P.text : P.grid;
        if (s === '#f1f5f9' || s === '#f8fafc') return texty ? P.heading : P.grid;
        if (s === '#94a3b8' || s === '#64748b' || s === '#8b98a8') return texty ? P.text3 : P.axis;
        if (s === '#475569') return texty ? P.text2 : P.axis;
        var role = TOKEN_ROLE[s];
        if (role && role !== 'light' && P[role]) return P[role];  /* a token value from the other scheme */
        return v;
    }
    function themeTooltip(t, P) {
        if (Array.isArray(t)) return t.map(function (x) { return themeTooltip(x, P); });
        if (!t || typeof t !== 'object' || Object.getPrototypeOf(t) !== Object.prototype) return t;
        var out = {};
        for (var k in t) if (Object.prototype.hasOwnProperty.call(t, k)) out[k] = t[k];
        out.backgroundColor = P.surface;
        out.borderColor = P.border;
        var ts = {};
        if (t.textStyle && typeof t.textStyle === 'object') for (var j in t.textStyle) if (Object.prototype.hasOwnProperty.call(t.textStyle, j)) ts[j] = t.textStyle[j];
        ts.color = P.text;
        out.textStyle = ts;
        if (t.axisPointer && typeof t.axisPointer === 'object') out.axisPointer = themeOption(t.axisPointer, 'axisPointer', ['tooltip', 'axisPointer'], P);
        return out;
    }
    function themeOption(o, key, path, P) {
        if (o === null || typeof o !== 'object') return themeColour(o, key || '', path, P);
        if (key === 'data' || key === 'graphic') return o;   /* never copy data */
        if (key === 'tooltip') return themeTooltip(o, P);
        if (Array.isArray(o)) {
            var arr = new Array(o.length);
            for (var i = 0; i < o.length; i++) arr[i] = (typeof o[i] === 'object' && o[i] !== null) ? themeOption(o[i], key, path, P) : themeColour(o[i], key || '', path, P);
            return arr;
        }
        if (Object.getPrototypeOf(o) !== Object.prototype) return o;
        var out = {};
        for (var k in o) {
            if (!Object.prototype.hasOwnProperty.call(o, k)) continue;
            var v = o[k];
            out[k] = (typeof v === 'function') ? v : themeOption(v, k, path.concat([k]), P);
        }
        return out;
    }
    /* Colour leaves of the chrome components (no series, data, ranges ...) -- re-applied on a theme toggle */
    var CHROME_KEYS = ['backgroundColor', 'title', 'legend', 'tooltip', 'xAxis', 'yAxis', 'dataZoom', 'visualMap', 'axisPointer', 'textStyle', 'radar', 'polar', 'angleAxis', 'radiusAxis', 'singleAxis'];
    function colourTree(o) {
        if (typeof o === 'string') return COLOUR_STR.test(o.replace(/\s+/g, '')) ? o : undefined;
        if (!o || typeof o !== 'object') return undefined;
        if (Array.isArray(o)) {
            var any = false, arr = o.map(function (x) { var c = colourTree(x); if (c !== undefined) { any = true; return c; } return {}; });
            return any ? arr : undefined;
        }
        if (Object.getPrototypeOf(o) !== Object.prototype) return undefined;
        var out = {}, has = false;
        for (var k in o) {
            if (!Object.prototype.hasOwnProperty.call(o, k) || k === 'data' || k === 'formatter') continue;
            var c = colourTree(o[k]);
            if (c !== undefined) { out[k] = c; has = true; }
        }
        return has ? out : undefined;
    }
    function mergeTree(dst, src) {
        if (Array.isArray(src)) {
            var a = Array.isArray(dst) ? dst : [];
            for (var i = 0; i < src.length; i++) a[i] = (src[i] && typeof src[i] === 'object') ? mergeTree(a[i], src[i]) : src[i];
            return a;
        }
        if (src && typeof src === 'object') {
            var d = (dst && typeof dst === 'object' && !Array.isArray(dst)) ? dst : {};
            for (var k in src) if (Object.prototype.hasOwnProperty.call(src, k)) d[k] = (src[k] && typeof src[k] === 'object') ? mergeTree(d[k], src[k]) : src[k];
            return d;
        }
        return src;
    }
    var THEMED = [];
    function rememberChrome(ch, opt, notMerge) {
        if (notMerge || !ch.__tlChrome) ch.__tlChrome = {};
        for (var i = 0; i < CHROME_KEYS.length; i++) {
            var k = CHROME_KEYS[i];
            if (!Object.prototype.hasOwnProperty.call(opt, k)) continue;
            var c = colourTree(opt[k]);
            if (k === 'tooltip' && c === undefined) c = {};
            if (c !== undefined) ch.__tlChrome[k] = mergeTree(ch.__tlChrome[k], c);
        }
    }
    function rethemeAll() {
        var P = chartPal();
        if (!P) return;
        THEMED = THEMED.filter(function (ch) { return ch && !(ch.isDisposed && ch.isDisposed()); });
        THEMED.forEach(function (ch) {
            if (!ch.__tlChrome || !ch.__tlSetOption) return;
            try { ch.__tlSetOption.call(ch, themeOption(ch.__tlChrome, '', [], P)); } catch (e) { }
        });
    }
    function hookECharts() {
        var ec = window.echarts;
        if (!ec || ec.__tlLight) return;
        var init = ec.init;
        ec.init = function () {
            var ch = init.apply(this, arguments);
            if (!ch || ch.__tlSetOption) return ch;          /* existing instance: already wrapped */
            var so = ch.setOption;
            ch.__tlSetOption = so;
            THEMED = THEMED.filter(function (c) { return c && !(c.isDisposed && c.isDisposed()); });
            THEMED.push(ch);
            ch.setOption = function (opt) {
                var args = Array.prototype.slice.call(arguments);
                if (opt && typeof opt === 'object') {
                    var nm = args[1] === true || (args[1] && typeof args[1] === 'object' && args[1].notMerge);
                    try { rememberChrome(ch, opt, nm); } catch (e) { }
                    var P = chartPal();
                    if (P) { try { args[0] = themeOption(opt, '', [], P); } catch (e) { args[0] = opt; } }
                }
                return so.apply(this, args);
            };
            return ch;
        };
        ec.__tlLight = true;
        if (typeof window.tlOnThemeChange === 'function') window.tlOnThemeChange(rethemeAll);
    }
    hookECharts();

    /* ======================================================================
       BOOT
       ====================================================================== */
    function boot() {
        ensureUi();
        hookIps();
        hookECharts();
        setTimeout(hookIps, 2000);
        var tries = 0;
        var t = setInterval(function () {
            if (ensureUi() || ++tries > 30) clearInterval(t);
        }, 1000);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();

    /* v618.18: health of one reading, for the SIP inspector panel */
    function classify(aid, label, v, attrId) {
        if (v === null || v === undefined || isNaN(v)) return { st: 'none' };
        loadRanges(aid);
        var g = rangeFor(String(aid || ''), label, label, attrId);
        if (!g || g.min === null || g.max === null || !(g.max > g.min)) return { st: 'ok', range: null };
        var span = g.max - g.min;
        var st = v < g.min ? 'low' : v > g.max ? 'high' : (v < g.min + span * 0.08 || v > g.max - span * 0.08) ? 'near' : 'ok';
        return { st: st, range: g, pos: Math.max(0, Math.min(100, ((v - g.min) / span) * 100)) };
    }
    window.TlHealthView = {
        enter: enter, leave: leave, isOn: function () { return H.on; }, render: function () { H.sig = ''; render(true); },
        classify: classify, ensureRanges: function () { loadRanges(); }, _h: H,
        /* v618.30: one asset's readings, derived values, relays, health -- for the asset drawer */
        describe: function (aid, typeHint) {
            loadRanges(aid);
            var prev = H.forType;
            H.forType = assetTypeOf(aid, typeHint);   /* track / PM / signal rules follow the asset, not the page */
            try { return readAsset(String(aid)); } finally { H.forType = prev; }
        }
    };
})();
