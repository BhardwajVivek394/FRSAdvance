/* ==========================================================================
 *  SIP Replay -- station yard playback for Telemetry Live (v618.0)
 *  ------------------------------------------------------------------------
 *  Plays the yard back from the DashboardHistory API through the SAME rule
 *  engine the live view uses (sip-telemetry.js 616 rules), on the smooth
 *  vector yard drawn by sip-library.js.
 *
 *  DATA
 *    GET /FRS25/TelemetryHistory/GetDashboardHistoryData
 *        ?assetId&startDate=ddMMyyyy_HHmmss&endDate&pageSize(tsLimit)
 *        &sort=asc&fillGaps=true&page&cursor
 *    -> { columns[{tagId,attrId,name,dataType}], rows[{ts,v[]}],
 *         nextCursor, hasNextPage }   (cursor pagination, see
 *         TelemetryHistoryController PAGINATION CONTRACT)
 *    Assets of the site: /FRS25/Telemetry/GetUserAssetInfoDatalogger?siteId
 *    (only assets that match a SIP cell are fetched).
 *
 *  LIMIT FOLLOWS PLAYBACK
 *    Each asset keeps rows buffered only up to T + horizon, where
 *    horizon = max(60 s, 45 s x speed). tsLimit (rows per request) starts
 *    from the window length and is then re-sized per asset from the
 *    observed row density so one page covers about one horizon:
 *        tsLimit = clamp(ceil(rowsPerSec x horizon x 1.25), 50, 500)
 *    Faster playback -> bigger pages; slow playback -> small, quick pages.
 *    If playback reaches the edge of the buffer it waits ("Buffering").
 *
 *  PLAYBACK
 *    Whole-yard play / pause / seek / speed; previous / next event.
 *    Events = DataLogger relay changes in the history (whole window, known
 *    as pages arrive) + yard state changes seen while playing.
 *    Click an asset in the yard -> its analog and relay values at the
 *    cursor, each with its graph over the window; click a graph to seek.
 *
 *  POINT MACHINES use DashboardPMHistoryIntegration (indication + relays,
 *  same columnar/cursor contract); on error they fall back to DashboardHistory.
 *
 *  ALERTS  (/FRS25/Telemetry/GetSipReplayAlerts, exact window)
 *    Red pins on the timeline and red rows in the event list. Clicking an
 *    alert replays AROUND it: window = alert - before .. alert + after,
 *    asset selected, 1x, playing. "Follow alerts" (whole-yard play): when
 *    the cursor reaches an alert's before-window, its asset is selected so
 *    the analog panel shows the lead-up.
 *
 *  PUBLIC: window.SipReplay = { open, close, isOpen, onCellClick, onReplayEnded, _r }
 * ========================================================================== */

(function () {
    'use strict';

    if (!window.SIP || !window.SipTelemetry || !window.SipTelemetry.replay) {
        console.error('[sip-replay] load sip-library.js and sip-telemetry.js (v618+) first.');
        return;
    }
    var RT = window.SipTelemetry.replay;

    var HISTORY_URL = '/FRS25/TelemetryHistory/GetDashboardHistoryData';
    var PM_HISTORY_URL = '/FRS25/TelemetryHistory/GetDashboardPMIntegrationData';
    var ALERTS_URL = '/FRS25/Telemetry/GetSipReplayAlerts';
    var PM_CELL_TYPES = { 'examples.PointMachine': 1, 'examples.PointMachine1': 1 };
    var ASSETS_URL = '/FRS25/Telemetry/GetUserAssetInfoDatalogger?siteId=';
    var MAX_CONCURRENT = 4;
    var TS_MIN = 50;
    var TS_MAX = 500;
    var SPEEDS = [0.25, 1, 4, 16, 60];   /* v618.4: segmented like the SL1 SIP prototype */
    var UI_MS = 120;

    var R = {
        open: false, active: false, gen: 0,
        siteId: null, start: 0, end: 0, T: 0, appliedT: -1,
        playing: false, speed: 4, buffering: false,
        assets: [], byName: {},
        dlEvents: [], seenEvents: [], eventsDirty: true, events: [],
        selected: null, selectedCol: null,
        inflight: 0, lastFrame: 0, lastUi: 0, raf: null,
        assetCache: {}, status: '',
        alerts: [], follow: true, pre: 120000, post: 60000, lastFollow: -1, pendingAround: null
    };

    /* ======================================================================
       SMALL HELPERS
       ====================================================================== */
    function pad(n) { return (n < 10 ? '0' : '') + n; }
    function fmtApi(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + pad(d.getMonth() + 1) + d.getFullYear() + '_' +
            pad(d.getHours()) + pad(d.getMinutes()) + pad(d.getSeconds());
    }
    function fmtClock(ms) {
        var d = new Date(ms);
        return pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    function fmtDay(ms) {
        var d = new Date(ms);
        return pad(d.getDate()) + '/' + pad(d.getMonth() + 1) + ' ' + fmtClock(ms);
    }
    function toLocalInput(ms) {
        var d = new Date(ms);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()) + 'T' +
            pad(d.getHours()) + ':' + pad(d.getMinutes()) + ':' + pad(d.getSeconds());
    }
    function fromLocalInput(v) {
        var t = new Date(v).getTime();
        return isNaN(t) ? 0 : t;
    }
    function clamp(v, a, b) { return Math.max(a, Math.min(b, v)); }
    function esc(s) {
        return String(s == null ? '' : s).replace(/&/g, '&amp;').replace(/</g, '&lt;')
            .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }
    function tsOf(v) {
        if (v == null) return NaN;
        if (typeof v === 'number') return v;
        return new Date(v).getTime();
    }
    function num(v) {
        if (v === null || v === undefined || v === '') return null;
        var n = parseFloat(v);
        return isNaN(n) ? null : n;
    }
    function pick(o, keys) {
        if (!o) return undefined;
        for (var i = 0; i < keys.length; i++) {
            if (o[keys[i]] !== undefined) return o[keys[i]];
        }
        return undefined;
    }
    function byId(id) { return document.getElementById(id); }

    /* horizon (ms of data kept ahead of the cursor) follows playback speed */
    function horizonMs() { return Math.max(60, 45 * R.speed) * 1000; }

    function initialTsLimit() {
        var windowSec = (R.end - R.start) / 1000;
        var aheadSec = Math.min(windowSec, horizonMs() / 1000);
        /* assume ~1 row per 5 s until the first page tells us the real density */
        return clamp(Math.ceil(aheadSec / 5), TS_MIN, TS_MAX);
    }

    /* ======================================================================
       ASSET LIST  (site assets that exist on the SIP)
       ====================================================================== */
    function loadSiteAssets(siteId, cb) {
        if (R.assetCache[siteId]) { cb(R.assetCache[siteId]); return; }
        $.ajax({
            url: ASSETS_URL + encodeURIComponent(siteId),
            type: 'GET',
            dataType: 'json',
            success: function (resp) {
                var data = Array.isArray(resp) ? resp : (resp && (resp.Data || resp.data || resp.Result || resp.result)) || [];
                var byId = {};
                for (var i = 0; i < data.length; i++) {
                    var it = data[i] || {};
                    var id = it.AssetId != null ? String(it.AssetId) : '';
                    var name = String(it.AssetName || '').trim();
                    if (id && name && !byId[id]) byId[id] = name;
                }
                /* fallback / top-up: whatever the live page already knows */
                var ld = window.wsLiveData || {};
                for (var k in ld) {
                    if (!ld.hasOwnProperty(k) || byId[k]) continue;
                    var e = ld[k];
                    if (e && e.AssetName) byId[k] = String(e.AssetName).trim();
                }
                var list = [];
                for (var aid in byId) if (byId.hasOwnProperty(aid)) list.push({ id: aid, name: byId[aid] });
                R.assetCache[siteId] = list;
                cb(list);
            },
            error: function () { cb([]); }
        });
    }

    function newAsset(id, name, isPM) {
        return {
            id: id, name: name, isPM: !!isPM, url: isPM ? PM_HISTORY_URL : HISTORY_URL, cols: null, rows: [], page: 1, cursor: '', done: false,
            loading: false, desc: null, tsLimit: initialTsLimit(), ptr: -1, err: '',
            sparkKey: '', spark: {}
        };
    }
    function lastT(a) { return a.rows.length ? a.rows[a.rows.length - 1].t : -Infinity; }
    function firstT(a) { return a.rows.length ? a.rows[0].t : Infinity; }

    /* ======================================================================
       FETCH  (cursor pagination, horizon-limited prefetch)
       ====================================================================== */
    function needsData(a) {
        if (a.done || a.loading) return false;
        if (!a.rows.length) return true;
        if (a.desc) return true;                 /* newest-first upstream: need the whole window */
        return lastT(a) < R.T + horizonMs();
    }
    function pump() {
        if (!R.active) return;
        var want = [];
        for (var i = 0; i < R.assets.length; i++) if (needsData(R.assets[i])) want.push(R.assets[i]);
        want.sort(function (x, y) { return lastT(x) - lastT(y); });
        for (var j = 0; j < want.length && R.inflight < MAX_CONCURRENT; j++) fetchPage(want[j]);
        updateStatus();
    }
    function fetchPage(a) {
        var gen = R.gen;
        a.loading = true;
        R.inflight++;
        $.ajax({
            url: a.url,
            type: 'GET',
            dataType: 'json',
            data: {
                assetId: a.id,
                startDate: fmtApi(R.start),
                endDate: fmtApi(R.end),
                page: a.page,
                pageSize: a.tsLimit,
                sort: 'asc',
                fillGaps: 'true',
                cursor: a.cursor || ''
            },
            success: function (resp) {
                if (gen !== R.gen) return;
                R.inflight--;
                a.loading = false;
                if ((!resp || resp.error) && a.url === PM_HISTORY_URL && !a.rows.length) {
                    /* PM integration not available for this asset: use DashboardHistory */
                    a.url = HISTORY_URL;
                    a.page = 1;
                    a.cursor = '';
                } else if (!resp || resp.error) {
                    a.err = (resp && resp.error) || 'empty response';
                    a.done = true;
                } else {
                    absorbPage(a, resp);
                }
                afterPage();
            },
            error: function () {
                if (gen !== R.gen) return;
                R.inflight--;
                a.loading = false;
                if (a.url === PM_HISTORY_URL && !a.rows.length) {
                    a.url = HISTORY_URL;
                    a.page = 1;
                    a.cursor = '';
                } else {
                    a.err = 'request failed';
                    a.done = true;
                }
                afterPage();
            }
        });
    }

    function colKey(c) { return String(c.attrId) + '|' + String(c.dtype) + '|' + String(c.name); }

    function absorbPage(a, resp) {
        var payload = resp;
        var inner = resp.Data || resp.data || resp.Result || resp.result || resp.Payload || resp.payload;
        if (inner && (inner.columns || inner.Columns)) payload = inner;
        var cols = payload.columns || payload.Columns || [];
        var rows = payload.rows || payload.Rows || [];

        /* ---- columns: first page defines the order; later pages are mapped ---- */
        var pageCols = [];
        for (var i = 0; i < cols.length; i++) {
            var c = cols[i] || {};
            var attrId = c.attrId !== undefined ? c.attrId : c.AttrId;
            var dtype = String(c.dataType || c.DataType || 'RDPMS');
            var name = String(c.name || c.Name || '');
            pageCols.push({
                attrId: attrId, dtype: dtype, name: name,
                isDL: dtype.toLowerCase() === 'datalogger',
                skip: (attrId === null || attrId === undefined) /* computed columns (PM Direction) */
            });
        }
        if (!a.cols) a.cols = pageCols;
        var map = [];
        var known = {};
        for (var k = 0; k < a.cols.length; k++) known[colKey(a.cols[k])] = k;
        for (var m = 0; m < pageCols.length; m++) {
            var key = colKey(pageCols[m]);
            if (known[key] === undefined) {
                a.cols.push(pageCols[m]);
                known[key] = a.cols.length - 1;
            }
            map.push(known[key]);
        }

        /* ---- rows ---- */
        var out = [];
        for (var r = 0; r < rows.length; r++) {
            var row = rows[r];
            if (!row) continue;
            var t = tsOf(row.ts || row.Ts);
            if (isNaN(t)) continue;
            var src = row.v || row.V || [];
            var v = new Array(a.cols.length);
            for (var ci = 0; ci < map.length; ci++) v[map[ci]] = src[ci];
            out.push({ t: t, ts: row.ts || row.Ts, v: v });
        }
        if (out.length > 1 && a.desc === null) a.desc = out[0].t > out[out.length - 1].t;
        if (a.desc === null && out.length) a.desc = false;

        /* density -> next page size, so one page ~ one playback horizon */
        if (out.length > 1) {
            var span = Math.abs(out[out.length - 1].t - out[0].t) / 1000;
            if (span > 0) {
                var perSec = out.length / span;
                a.tsLimit = clamp(Math.ceil(perSec * (horizonMs() / 1000) * 1.25), TS_MIN, TS_MAX);
            }
        }

        if (a.desc) {
            a.rows = a.rows.concat(out);
            a.rows.sort(function (x, y) { return x.t - y.t; });
        } else {
            for (var q = 0; q < out.length; q++) a.rows.push(out[q]);
        }

        var nextCursor = pick(resp, ['nextCursor', 'NextCursor']);
        if (nextCursor === undefined) nextCursor = pick(payload, ['nextCursor', 'NextCursor']);
        var hasNext = pick(resp, ['hasNextPage', 'HasNextPage']);
        if (hasNext === undefined) hasNext = pick(payload, ['hasNextPage', 'HasNextPage']);
        if (hasNext === undefined) hasNext = rows.length >= a.tsLimit;
        a.page++;
        a.cursor = nextCursor || '';
        if (!hasNext || !nextCursor || !rows.length) a.done = true;

        rebuildDlEvents(a);
        a.sparkKey = '';
    }

    function afterPage() {
        R.eventsDirty = true;
        if (R.buffering && covered(R.T)) {
            R.buffering = false;
            applyAt(R.T, false);
        } else if (!R.playing) {
            applyAt(R.T, false);
        }
        drawTimelineMarks();
        renderPanel(true);
        pump();
    }

    /* ======================================================================
       EVENTS
       ====================================================================== */
    function rebuildDlEvents(a) {
        a.dl = [];
        if (!a.cols) return;
        for (var c = 0; c < a.cols.length; c++) {
            var col = a.cols[c];
            if (!col.isDL || col.skip) continue;
            var prev = null;
            for (var r = 0; r < a.rows.length; r++) {
                var val = num(a.rows[r].v[c]);
                if (val === null) continue;
                var on = val >= 0.5;
                if (prev !== null && on !== prev) {
                    a.dl.push({ t: a.rows[r].t, asset: a.name, text: col.name + (on ? ' picked up' : ' dropped'), kind: 'relay' });
                }
                prev = on;
            }
        }
    }
    function allEvents() {
        if (!R.eventsDirty) return R.events;
        var ev = R.alerts.slice();
        for (var i = 0; i < R.assets.length; i++) if (R.assets[i].dl) ev = ev.concat(R.assets[i].dl);
        ev = ev.concat(R.seenEvents);
        ev.sort(function (x, y) { return x.t - y.t; });
        R.events = ev;
        R.eventsDirty = false;
        return ev;
    }

    /* Yard state of an asset as shown on the SIP (for "seen" events) */
    function describeAsset(name) {
        var cells = RT.findCells(name);
        var parts = [];
        for (var i = 0; i < cells.length; i++) {
            var c = cells[i];
            var at = c.attrs || {};
            var d = '';
            if (/Track/.test(c.type)) {
                var st = (at.path && (at.path.stroke || at.path.fill)) || '';
                d = window.SIP.isLit(st) ? 'occupied' : 'clear';
            } else if (c.type === 'examples.Signal' || c.type === 'examples.SignalShunt') {
                var lit = (at.signal && at.signal.lit) || '';
                d = lit ? 'aspect ' + lit : 'no aspect';
            } else if (/PointMachine/.test(c.type)) {
                var f = String((at.circle1 && at.circle1.fill) || '').toLowerCase();
                d = f === '#22d142' ? 'Normal' : f === '#ffd400' ? 'Reverse' : '';
            } else if (/Shaunt/.test(c.type)) {
                d = at.shuntLive ? 'shunt ' + at.shuntLive : '';
            } else if (/Signal(d?)(90|45)/.test(c.type)) {
                var lf = (at.circle1 && at.circle1.fill) || '';
                if (window.SIP.isLit(lf)) d = String((at.label && at.label.text) || '').split(/\s+/)[1] || 'lit';
            }
            if (d && parts.indexOf(d) === -1) parts.push(d);
        }
        return parts.join(', ');
    }

    /* ======================================================================
       APPLY  (cursor -> latest row per asset -> rule engine)
       ====================================================================== */
    function rowIndexAt(a, t) {
        var rows = a.rows;
        if (!rows.length || rows[0].t > t) return -1;
        var lo = 0, hi = rows.length - 1;
        while (lo < hi) {
            var mid = (lo + hi + 1) >> 1;
            if (rows[mid].t <= t) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
    function itemsFor(a, row) {
        var items = [];
        for (var c = 0; c < a.cols.length; c++) {
            var col = a.cols[c];
            if (col.skip) continue;
            var v = row.v[c];
            if (v === null || v === undefined || v === '') continue;
            items.push({
                AssetName: a.name,
                AssetId: a.id,
                AssetAttributeName: col.name,
                AssetAttributeId: col.attrId,
                DataType: col.dtype,
                Value: v,
                TimestampDevice: row.ts,
                IsFresh: true,
                BroadcastKind: 'history'
            });
        }
        return items;
    }
    function applyAt(t, trackChanges) {
        if (!R.active) return;
        var back = t < R.appliedT;
        if (back) {
            RT.reset();
            for (var i = 0; i < R.assets.length; i++) R.assets[i].ptr = -1;
        }
        var items = [];
        var fedNames = [];
        for (var j = 0; j < R.assets.length; j++) {
            var a = R.assets[j];
            if (!a.cols) continue;
            var idx = rowIndexAt(a, t);
            if (idx < 0 || idx === a.ptr) continue;
            var hadState = a.ptr >= 0;
            a.ptr = idx;
            var it = itemsFor(a, a.rows[idx]);
            if (it.length) {
                items = items.concat(it);
                if (hadState) fedNames.push(a.name);   /* first state of an asset is not a change */
            }
        }
        var before = null;
        if (trackChanges && fedNames.length) {
            before = {};
            for (var b = 0; b < fedNames.length; b++) before[fedNames[b]] = describeAsset(fedNames[b]);
        }
        if (items.length) RT.feed(items);
        if (before) {
            for (var n = 0; n < fedNames.length; n++) {
                var nm = fedNames[n];
                var after = describeAsset(nm);
                if (after && after !== before[nm]) {
                    R.seenEvents.push({ t: t, asset: nm, text: after, kind: 'state' });
                    R.eventsDirty = true;
                }
            }
        }
        R.appliedT = t;
    }
    function covered(t) {
        for (var i = 0; i < R.assets.length; i++) {
            var a = R.assets[i];
            if (a.done) continue;
            if (!a.rows.length) return false;
            if (a.desc) return false;
            if (lastT(a) < t) return false;
        }
        return true;
    }
    function bufferedUntil() {
        var m = R.end;
        for (var i = 0; i < R.assets.length; i++) {
            var a = R.assets[i];
            if (a.done) continue;
            m = Math.min(m, a.rows.length && !a.desc ? lastT(a) : R.start);
        }
        return clamp(m, R.start, R.end);
    }

    /* ======================================================================
       PLAY LOOP
       ====================================================================== */
    function frame(now) {
        R.raf = null;
        if (!R.active) return;
        if (R.playing) {
            var dt = R.lastFrame ? (now - R.lastFrame) : 0;
            var next = Math.min(R.end, R.T + dt * R.speed);
            if (!covered(next)) {
                R.buffering = true;
                next = Math.max(R.T, Math.min(next, bufferedUntil()));
                pump();
            } else {
                R.buffering = false;
            }
            R.T = next;
            applyAt(R.T, true);
            followAlerts();
            if (R.T >= R.end) setPlaying(false);
            if (R.T + horizonMs() / 2 > bufferedUntil()) pump();
        }
        R.lastFrame = now;
        if (now - R.lastUi > UI_MS) {
            R.lastUi = now;
            renderClock();
            renderPanel(false);
            renderEvents(false);
        }
        if (R.playing) R.raf = requestAnimationFrame(frame);
    }
    function kick() {
        if (!R.raf) {
            R.lastFrame = 0;
            R.raf = requestAnimationFrame(frame);
        }
    }
    function setPlaying(on) {
        R.playing = !!on && R.active;
        if (R.playing && R.T >= R.end) {
            R.T = R.start;
        }
        var b = byId('sipRpPlay');
        if (b) {
            b.innerHTML = R.playing ? '<i class="fas fa-pause"></i>' : '<i class="fas fa-play"></i>';
            b.setAttribute('aria-label', R.playing ? 'Pause' : 'Play');
        }
        if (R.playing) kick();
        else { renderClock(); renderPanel(false); renderEvents(true); }
    }
    function seek(t) {
        R.T = clamp(t, R.start, R.end);
        applyAt(R.T, false);
        if (!covered(R.T)) { R.buffering = true; pump(); }
        renderClock();
        renderPanel(false);
        renderEvents(true);
    }
    function jumpEvent(dir) {
        var ev = allEvents();
        var target = null;
        if (dir > 0) {
            for (var i = 0; i < ev.length; i++) if (ev[i].t > R.T + 500) { target = ev[i]; break; }
        } else {
            for (var j = ev.length - 1; j >= 0; j--) if (ev[j].t < R.T - 500) { target = ev[j]; break; }
        }
        if (target) {
            if (target.asset) selectAsset(target.asset);
            seek(target.t);
        }
    }

    /* ======================================================================
       START / STOP
       ====================================================================== */
    function loadWindow() {
        var s = fromLocalInput((byId('sipRpFrom') || {}).value);
        var e = fromLocalInput((byId('sipRpTo') || {}).value);
        if (!s || !e || e <= s) { setStatusText('Pick a valid From / To window.'); return; }
        if (e > Date.now()) e = Date.now();
        if (e <= s) { setStatusText('The window is in the future -- pick an earlier From.'); return; }
        var siteId = RT.siteId();
        if (!siteId) { setStatusText('Select a site first.'); return; }
        if (!RT.begin()) { setStatusText('Schematic not loaded yet.'); return; }

        R.gen++;
        R.active = true;
        R.siteId = siteId;
        R.start = s;
        R.end = e;
        R.T = s;
        R.appliedT = -1;
        R.assets = [];
        R.byName = {};
        R.seenEvents = [];
        R.alerts = [];
        R.lastFollow = -1;
        R.eventsDirty = true;
        R.inflight = 0;
        R.buffering = false;
        setPlaying(false);
        setStatusText('Loading site assets...');
        var gen = R.gen;
        loadSiteAssets(siteId, function (list) {
            if (gen !== R.gen) return;
            var seen = {};
            for (var i = 0; i < list.length; i++) {
                var it = list[i];
                if (seen[it.id]) continue;
                var cellsOf = RT.findCells(it.name);
                if (!cellsOf.length) continue;
                seen[it.id] = 1;
                var isPM = false;
                for (var ci = 0; ci < cellsOf.length; ci++) if (PM_CELL_TYPES[cellsOf[ci].type]) isPM = true;
                var a = newAsset(it.id, it.name, isPM);
                R.assets.push(a);
                R.byName[it.name] = a;
            }
            if (!R.assets.length) {
                setStatusText('No SIP assets found for this site.');
                return;
            }
            drawTimeline();
            pump();
            renderPanel(true);
            loadAlerts(gen);
            if (R.pendingAround) {
                var pa = R.pendingAround;
                R.pendingAround = null;
                if (pa.asset) selectAsset(pa.asset);
                setSpeed(1);
                setPlaying(true);
            }
        });
    }

    /* ---- alerts of the window ------------------------------------------- */
    /* v618.5 -- same request as the Alerts page (AlertsController.GetAlertList):
       $.param(FRSAlertLister) with SearchCriteria.SiteIds / AlertStatus /
       AlertTypeId / FromDate / ToDate (yyyy-MM-dd, as the date filters send)
       and Pager.Skip / PageSize. Pages until a page returns fewer than
       PageSize rows, then keeps only this site and the exact replay window. */
    var ALERT_PAGE = 100;
    var ALERT_MAX_PAGES = 20;
    function fmtYmd(ms) {
        var d = new Date(ms);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }
    function loadAlerts(gen) {
        R.alerts = [];
        var all = [];
        function finish() {
            if (gen !== R.gen) return;
            var seen = {};
            var out = [];
            for (var i = 0; i < all.length; i++) {
                var x = all[i] || {};
                if (x.id != null && seen[x.id]) continue;
                if (x.id != null) seen[x.id] = 1;
                if (x.siteId != null && String(x.siteId) !== String(R.siteId)) continue;
                var t = tsOf(x.setTime);
                if (isNaN(t) || t < R.start || t > R.end) continue;
                out.push({
                    t: t, asset: String(x.assetName || '').trim(), id: x.id,
                    text: (x.alertType ? x.alertType + ': ' : '') + (x.causeCode || x.description || 'alert') +
                        (x.acknowledged ? ' (ack)' : ''),
                    kind: 'alert'
                });
            }
            out.sort(function (p, q) { return p.t - q.t; });
            R.alerts = out;
            R.eventsDirty = true;
            drawTimelineMarks();
        }
        function page(i) {
            $.ajax({
                url: ALERTS_URL,
                type: 'POST',
                contentType: 'application/x-www-form-urlencoded',
                dataType: 'json',
                data: $.param({
                    SearchCriteria: {
                        AlertStatus: 0,
                        AlertTypeId: 0,
                        SiteIds: [String(R.siteId)],
                        FromDate: fmtYmd(R.start),
                        ToDate: fmtYmd(R.end)
                    },
                    Pager: { Skip: i * ALERT_PAGE, PageSize: ALERT_PAGE }
                }),
                success: function (resp) {
                    if (gen !== R.gen) return;
                    var list = (resp && resp.alerts) || [];
                    all = all.concat(list);
                    var raw = (resp && resp.rawCount != null) ? resp.rawCount : list.length;
                    if (raw >= ALERT_PAGE && i + 1 < ALERT_MAX_PAGES) page(i + 1);
                    else finish();
                },
                error: function () { finish(); }
            });
        }
        page(0);
    }
    function replayAround(alert) {
        var from = alert.t - R.pre;
        var to = Math.min(Date.now(), alert.t + R.post);
        byId('sipRpFrom').value = toLocalInput(from);
        byId('sipRpTo').value = toLocalInput(to);
        R.pendingAround = { asset: alert.asset };
        loadWindow();
    }
    function setSpeed(s) {
        R.speed = s;
        var seg = byId('sipRpSpeed');
        if (!seg) return;
        var bs = seg.querySelectorAll('button[data-rp-speed]');
        for (var i = 0; i < bs.length; i++) bs[i].setAttribute('aria-pressed', String(+bs[i].getAttribute('data-rp-speed') === s));
    }
    /* whole-yard play: select the asset of an alert when its lead-up starts */
    function followAlerts() {
        if (!R.follow || !R.alerts.length) return;
        for (var i = 0; i < R.alerts.length; i++) {
            var al = R.alerts[i];
            if (R.T >= al.t - R.pre && R.T <= al.t + R.post) {
                if (R.lastFollow !== i) {
                    R.lastFollow = i;
                    if (al.asset) selectAsset(al.asset);
                }
                return;
            }
        }
    }
    function exitReplay() {
        R.gen++;
        R.active = false;
        setPlaying(false);
        R.assets = [];
        R.byName = {};
        R.selected = null;
        RT.end();
    }

    /* ======================================================================
       UI
       ====================================================================== */
    /* v618.2: colours are CSS variables -- dark by default, light when the
       page behind the schematic card is light (class rp-light, set from the
       measured background colour by syncRpTheme). */
    var CSS = '' +
        '.sip-rp{--rp-text:#E6EDF5;--rp-muted:#8D9CB2;--rp-panel:rgba(255,255,255,.03);--rp-field:#121E2F;--rp-rule:#22324A;' +
        '--rp-accent:#3BC9DB;--rp-on:#04161A;--rp-hover:rgba(59,201,219,.08);--rp-sel:rgba(59,201,219,.14);' +
        '--rp-trace:#DCE6F2;--rp-dl:#3BC9DB;--rp-cur:#F6C445;--rp-state:#F6C445;--rp-alert:#FF5A4E;--rp-plot:rgba(255,255,255,.02);}' +
        '.sip-rp.rp-light{--rp-text:#13202E;--rp-muted:#5A6878;--rp-panel:#FFFFFF;' +
        '--rp-field:#FFFFFF;--rp-rule:#D6DDE5;--rp-accent:#0E8FA3;--rp-on:#FFFFFF;--rp-hover:rgba(14,143,163,.07);--rp-sel:rgba(14,143,163,.13);' +
        '--rp-trace:#13202E;--rp-dl:#0E8FA3;--rp-cur:#C77700;--rp-state:#9A5B00;--rp-alert:#D0251B;--rp-plot:#F6F8FA;}' +
        '.sip-card.sip-replaying .sip-canvas svg{min-height:0 !important;}' +
        '.sip-rp{display:none;flex:none;margin-top:8px;border-top:1px solid var(--rp-rule);color:var(--rp-text);font:12.5px/1.4 system-ui,-apple-system,"Segoe UI",sans-serif;}' +
        '.sip-card.sip-replaying .sip-rp{display:flex;flex-direction:column;gap:8px;max-height:46vh;}' +
        '.sip-rp-row{display:flex;flex-wrap:wrap;align-items:center;gap:8px;padding:0 4px;}' +
        '.sip-rp label{color:var(--rp-text);}' +
        '.sip-rp input[type=datetime-local],.sip-rp select{background:var(--rp-field);color:var(--rp-text);border:1px solid var(--rp-rule);border-radius:6px;padding:4px 6px;font:inherit;}' +
        '.sip-rp:not(.rp-light) input[type=datetime-local]{color-scheme:dark;}' +
        '.sip-rp-btn{background:var(--rp-field);color:var(--rp-text);border:1px solid var(--rp-rule);border-radius:6px;padding:4px 10px;font:inherit;cursor:pointer;}' +
        '.sip-rp-btn:hover{border-color:var(--rp-accent);}' +
        '.sip-rp-btn.pri{background:var(--rp-accent);border-color:var(--rp-accent);color:var(--rp-on);font-weight:600;}' +
        '.sip-rp-play{width:36px;height:36px;border-radius:50%;border:none;background:var(--rp-accent);color:var(--rp-on);cursor:pointer;flex:none;}' +
        '.sip-rp-tl{position:relative;flex:1 1 320px;min-width:200px;height:48px;}' +
        '.sip-rp-pins{position:absolute;left:8px;right:8px;top:0;height:20px;}' +
        '.sip-rp-pin{position:absolute;top:0;transform:translateX(-50%);border:none;border-radius:3px;background:var(--rp-alert);color:#fff;' +
        'font:600 11px/1 system-ui,sans-serif;padding:4px 6px;cursor:pointer;white-space:nowrap;}' +
        '.sip-rp-pin::after{content:"";position:absolute;left:50%;bottom:-5px;transform:translateX(-50%);border:5px solid transparent;border-bottom:none;border-top-color:var(--rp-alert);}' +
        '.sip-rp .sip-seg{display:inline-flex;padding:2px;border-radius:9px;background:var(--rp-hover);gap:2px;border:1px solid var(--rp-rule);}' +
        '.sip-rp .sip-seg button{height:28px;min-width:36px;padding:0 9px;border:none;border-radius:7px;background:transparent;color:var(--rp-text);font:inherit;cursor:pointer;}' +
        '.sip-rp .sip-seg button[aria-pressed="true"]{background:var(--rp-accent);color:var(--rp-on);font-weight:600;}' +
        '.sip-rp-tl input{position:absolute;left:0;right:0;bottom:0;width:100%;margin:0;accent-color:var(--rp-accent);cursor:pointer;}' +
        '.sip-rp-buf{position:absolute;left:0;bottom:8px;height:4px;border-radius:2px;background:var(--rp-sel);pointer-events:none;}' +
        '.sip-rp-ticks{position:absolute;left:8px;right:8px;top:24px;height:9px;pointer-events:none;}' +
        '.sip-rp-ticks b{position:absolute;top:0;width:1px;height:9px;background:var(--rp-muted);opacity:.6;}' +
        '.sip-rp-ticks b.s{background:var(--rp-state);opacity:.9;}' +
        '.sip-rp-ticks b.a{background:var(--rp-alert);opacity:1;width:3px;height:12px;top:-3px;}' +
        '.sip-rp-clock{font:600 18px/1 "Barlow Condensed",system-ui,sans-serif;font-variant-numeric:tabular-nums;min-width:132px;text-align:right;color:var(--rp-text);}' +
        '.sip-rp-status{color:var(--rp-muted);}' +
        '.sip-rp-dock{display:grid;grid-template-columns:minmax(220px,300px) minmax(0,1fr);gap:8px;min-height:0;flex:1 1 auto;}' +
        '.sip-rp-col{background:var(--rp-panel);border:1px solid var(--rp-rule);border-radius:8px;min-height:0;display:flex;flex-direction:column;}' +
        '.sip-rp-col h4{margin:0;padding:7px 10px;font:600 13px/1.2 system-ui,sans-serif;color:var(--rp-text);border-bottom:1px solid var(--rp-rule);display:flex;gap:8px;align-items:center;}' +
        '.sip-rp-list{list-style:none;margin:0;padding:0;overflow:auto;min-height:0;}' +
        '.sip-rp-list li{display:grid;grid-template-columns:62px 1fr;gap:6px;padding:4px 10px;border-bottom:1px solid var(--rp-rule);cursor:pointer;color:var(--rp-text);}' +
        '.sip-rp-list li:hover{background:var(--rp-hover);}' +
        '.sip-rp-list li.now{background:var(--rp-sel);}' +
        '.sip-rp-list time{color:var(--rp-muted);font-variant-numeric:tabular-nums;}' +
        '.sip-rp-list li.s span{color:var(--rp-state);}' +
        '.sip-rp-list li.a span{color:var(--rp-alert);font-weight:600;}' +
        '.sip-rp-list li.a time{color:var(--rp-alert);}' +
        '.sip-rp-attrs{overflow:auto;min-height:0;padding:4px 0;}' +
        '.sip-rp-attr{display:grid;grid-template-columns:minmax(120px,190px) 86px minmax(0,1fr);gap:8px;align-items:center;padding:3px 10px;cursor:pointer;}' +
        '.sip-rp-attr:hover{background:var(--rp-hover);}' +
        '.sip-rp-attr.on{background:var(--rp-sel);}' +
        '.sip-rp-attr .n{color:var(--rp-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-rp-attr .v{text-align:right;font-weight:600;font-variant-numeric:tabular-nums;color:var(--rp-text);}' +
        '.sip-rp-attr svg{display:block;width:100%;height:26px;}' +
        '.sip-rp-big{padding:4px 10px 8px;color:var(--rp-muted);}' +
        '.sip-rp-big b{color:var(--rp-text);}' +
        '.sip-rp-big svg{display:block;width:100%;height:150px;cursor:crosshair;}' +
        '.sip-rp .rp-trace{stroke:var(--rp-trace);}' +
        '.sip-rp .rp-dl{stroke:var(--rp-dl);}' +
        '.sip-rp .rp-cur{stroke:var(--rp-cur);}' +
        '.sip-rp .rp-plot{fill:var(--rp-plot);}' +
        '.sip-rp .rp-err{color:var(--rp-alert);}' +
        '.sip-rp-empty{color:var(--rp-muted);padding:12px;}' +
        '#sipReplayBtn.on{background:var(--rp-accent,#3BC9DB);color:#fff;}' +
        '@media (max-width:900px){.sip-rp-dock{grid-template-columns:1fr;}}';

    function ensureUi() {
        if (byId('sipReplay')) return true;
        var card = byId('sipCardSection');
        var canvas = byId('sipCanvas');
        if (!card || !canvas) return false;

        if (!byId('sipReplayCss')) {
            var st = document.createElement('style');
            st.id = 'sipReplayCss';
            st.textContent = CSS;
            document.head.appendChild(st);
        }
        var now = Date.now();
        var box = document.createElement('div');
        box.className = 'sip-rp';
        box.id = 'sipReplay';
        box.innerHTML =
            '<div class="sip-rp-row">' +
            '  <label>From <input type="datetime-local" step="1" id="sipRpFrom" value="' + toLocalInput(now - 3600000) + '"></label>' +
            '  <label>To <input type="datetime-local" step="1" id="sipRpTo" value="' + toLocalInput(now) + '"></label>' +
            '  <button type="button" class="sip-rp-btn" data-rp-preset="15">15 min</button>' +
            '  <button type="button" class="sip-rp-btn" data-rp-preset="60">1 h</button>' +
            '  <button type="button" class="sip-rp-btn" data-rp-preset="360">6 h</button>' +
            '  <button type="button" class="sip-rp-btn" data-rp-preset="today">Today</button>' +
            '  <button type="button" class="sip-rp-btn pri" id="sipRpLoad">Load replay</button>' +
            '  <label>Before alert <select id="sipRpPre"><option value="60000">1 min</option><option value="120000" selected>2 min</option><option value="300000">5 min</option><option value="600000">10 min</option></select></label>' +
            '  <label>After <select id="sipRpPost"><option value="30000">30 s</option><option value="60000" selected>1 min</option><option value="180000">3 min</option><option value="300000">5 min</option></select></label>' +
            '  <label><input type="checkbox" id="sipRpFollow" checked> Follow alerts</label>' +
            '  <span class="sip-rp-status" id="sipRpStatus">Choose a window and load.</span>' +
            '  <span style="flex:1"></span>' +
            '  <button type="button" class="sip-rp-btn" id="sipRpExit"><i class="fas fa-tower-broadcast"></i> Back to live</button>' +
            '</div>' +
            '<div class="sip-rp-row">' +
            '  <button type="button" class="sip-rp-btn" id="sipRpPrev" title="Previous event"><i class="fas fa-backward-step"></i></button>' +
            '  <button type="button" class="sip-rp-play" id="sipRpPlay" aria-label="Play"><i class="fas fa-play"></i></button>' +
            '  <button type="button" class="sip-rp-btn" id="sipRpNext" title="Next event"><i class="fas fa-forward-step"></i></button>' +
            '  <div class="sip-rp-tl"><div class="sip-rp-pins" id="sipRpPins"></div><div class="sip-rp-ticks" id="sipRpTicks"></div><div class="sip-rp-buf" id="sipRpBuf"></div>' +
            '    <input type="range" id="sipRpSeek" min="0" max="1000" value="0" step="1" aria-label="Replay time"></div>' +
            '  <span class="sip-rp-status">Speed</span><div class="sip-seg" id="sipRpSpeed" role="group" aria-label="Playback speed">' + SPEEDS.map(function (s) {
                return '<button type="button" data-rp-speed="' + s + '" aria-pressed="' + (s === R.speed) + '">' +
                    (s === 0.25 ? '&frac14;' : s) + '&times;</button>';
            }).join('') + '</div>' +
            '  <span class="sip-rp-clock" id="sipRpClock">--:--:--</span>' +
            '</div>' +
            '<div class="sip-rp-dock">' +
            '  <div class="sip-rp-col"><h4>Events <span class="sip-rp-status" id="sipRpEvCount"></span></h4><ul class="sip-rp-list" id="sipRpEvents"></ul></div>' +
            '  <div class="sip-rp-col"><h4 id="sipRpSelHead">Analog and relay values</h4><div class="sip-rp-big" id="sipRpBig"></div><div class="sip-rp-attrs" id="sipRpAttrs"><div class="sip-rp-empty">Click any track, point or signal in the yard to see its values at the cursor.</div></div></div>' +
            '</div>';
        var after = byId('sipLegend2') || canvas;   /* v618.4: keep the legend right under the yard */
        after.parentNode.insertBefore(box, after.nextSibling);

        box.addEventListener('click', function (e) {
            var pin = e.target.closest('[data-rp-pin]');
            if (pin) {
                var pa = R.alerts[+pin.getAttribute('data-rp-pin')];
                if (pa) replayAround(pa);
                return;
            }
            var p = e.target.closest('[data-rp-preset]');
            if (p) { applyPreset(p.getAttribute('data-rp-preset')); return; }
            var li = e.target.closest('[data-rp-t]');
            if (li && li.getAttribute('data-rp-alert') !== null) {
                var ai = +li.getAttribute('data-rp-alert');
                if (R.alerts[ai]) replayAround(R.alerts[ai]);
                return;
            }
            if (li) {
                var nm = li.getAttribute('data-rp-asset');
                if (nm) selectAsset(nm);
                seek(+li.getAttribute('data-rp-t'));
                return;
            }
            var at = e.target.closest('[data-rp-col]');
            if (at) {
                R.selectedCol = +at.getAttribute('data-rp-col');
                renderPanel(true);
            }
        });
        byId('sipRpLoad').addEventListener('click', loadWindow);
        byId('sipRpExit').addEventListener('click', function () { close(); });
        byId('sipRpPlay').addEventListener('click', function () {
            if (!R.active) { loadWindow(); return; }
            setPlaying(!R.playing);
        });
        byId('sipRpPrev').addEventListener('click', function () { jumpEvent(-1); });
        byId('sipRpNext').addEventListener('click', function () { jumpEvent(1); });
        byId('sipRpPre').addEventListener('change', function () { R.pre = +this.value || 120000; });
        byId('sipRpPost').addEventListener('change', function () { R.post = +this.value || 60000; });
        byId('sipRpFollow').addEventListener('change', function () { R.follow = !!this.checked; R.lastFollow = -1; });
        byId('sipRpSpeed').addEventListener('click', function (e) {
            var b = e.target.closest('button[data-rp-speed]');
            if (!b) return;
            setSpeed(+b.getAttribute('data-rp-speed') || 1);
            pump();
        });
        byId('sipRpSeek').addEventListener('input', function () {
            if (!R.active) return;
            seek(R.start + (+this.value / 1000) * (R.end - R.start));
        });
        byId('sipRpBig').addEventListener('click', function (e) {
            var svg = e.target.closest('svg');
            if (!svg || !R.active) return;
            var r = svg.getBoundingClientRect();
            seek(R.start + clamp((e.clientX - r.left) / r.width, 0, 1) * (R.end - R.start));
        });
        return true;
    }

    /* v618.3: light / dark from the real page background around the card */
    function syncRpTheme() {
        var box = byId('sipReplay');
        if (!box) return;
        var light = (window.SipTelemetry && typeof window.SipTelemetry.pageIsLight === 'function')
            ? window.SipTelemetry.pageIsLight() : false;
        box.classList.toggle('rp-light', !!light);
    }
    window.addEventListener('sip-theme', syncRpTheme);

    function ensureButton() {
        var ctr = byId('sipControls');
        if (!ctr || byId('sipReplayBtn')) return;
        var b = document.createElement('button');
        b.type = 'button';
        b.id = 'sipReplayBtn';
        b.className = 'compare-toggle';
        b.title = 'Replay the yard from history';
        b.innerHTML = '<i class="fas fa-clock-rotate-left"></i> Replay';
        b.addEventListener('click', function (e) {
            e.stopPropagation();
            if (R.open) close(); else open();
        });
        var fs = byId('sipFullscreenBtn');
        ctr.insertBefore(b, fs || null);
    }

    function applyPreset(p) {
        var now = Date.now();
        var from;
        if (p === 'today') {
            var d = new Date(now);
            d.setHours(0, 0, 0, 0);
            from = d.getTime();
        } else {
            from = now - (+p) * 60000;
        }
        byId('sipRpFrom').value = toLocalInput(from);
        byId('sipRpTo').value = toLocalInput(now);
    }

    function setStatusText(t) {
        R.status = t;
        var el = byId('sipRpStatus');
        if (el) el.textContent = t;
    }
    function updateStatus() {
        if (!R.active) return;
        var done = 0, rows = 0, err = 0;
        for (var i = 0; i < R.assets.length; i++) {
            var a = R.assets[i];
            if (a.done) done++;
            if (a.err) err++;
            rows += a.rows.length;
        }
        var txt = R.assets.length + ' assets, ' + rows + ' rows';
        if (R.buffering) txt += ', buffering to ' + fmtClock(R.T);
        else if (R.inflight) txt += ', loading';
        else txt += ', buffered to ' + fmtClock(bufferedUntil());
        if (err) txt += ', ' + err + ' failed';
        setStatusText(txt);
    }

    function drawTimeline() {
        var seekEl = byId('sipRpSeek');
        if (seekEl) seekEl.value = 0;
        drawTimelineMarks();
        renderClock();
    }
    function drawTimelineMarks() {
        var ticks = byId('sipRpTicks');
        var buf = byId('sipRpBuf');
        if (!ticks || !R.active) return;
        var span = (R.end - R.start) || 1;
        var ev = allEvents();
        var step = Math.max(1, Math.ceil(ev.length / 400));
        var html = '';
        var pins = byId('sipRpPins');
        if (pins) {
            var ph = '';
            for (var ai = 0; ai < R.alerts.length; ai++) {
                var al = R.alerts[ai];
                ph += '<button type="button" class="sip-rp-pin" data-rp-pin="' + ai + '" title="' + esc(fmtClock(al.t) + ' ' + al.asset + ' ' + al.text) +
                    ' -- replay around it" style="left:' + (((al.t - R.start) / span) * 100).toFixed(2) + '%">' + esc(al.asset || 'Alert') + '</button>';
            }
            pins.innerHTML = ph;
        }
        for (var i = 0; i < ev.length; i += step) {
            if (ev[i].kind === 'alert') continue;
            html += '<b class="' + (ev[i].kind === 'state' ? 's' : ev[i].kind === 'alert' ? 'a' : '') + '" style="left:' +
                (((ev[i].t - R.start) / span) * 100).toFixed(2) + '%"></b>';
        }
        ticks.innerHTML = html;
        if (buf) buf.style.width = (((bufferedUntil() - R.start) / span) * 100).toFixed(2) + '%';
        var cnt = byId('sipRpEvCount');
        if (cnt) cnt.textContent = ev.length ? '(' + ev.length + ')' : '';
        renderEvents(true);
        updateStatus();
    }
    function renderClock() {
        var c = byId('sipRpClock');
        if (c) c.textContent = R.active ? fmtDay(R.T) : '--:--:--';
        var s = byId('sipRpSeek');
        if (s && R.active) s.value = Math.round(((R.T - R.start) / ((R.end - R.start) || 1)) * 1000);
        var buf = byId('sipRpBuf');
        if (buf && R.active) buf.style.width = (((bufferedUntil() - R.start) / ((R.end - R.start) || 1)) * 100).toFixed(2) + '%';
        if (R.buffering) updateStatus();
    }

    var _evRenderedFor = -1;
    function renderEvents(force) {
        var ul = byId('sipRpEvents');
        if (!ul) return;
        var ev = allEvents();
        if (!ev.length) {
            ul.innerHTML = '<li style="cursor:default"><time></time><span class="sip-rp-status">' +
                (R.active ? 'No relay or state changes yet.' : 'Load a window to replay.') + '</span></li>';
            return;
        }
        /* show the 120 events around the cursor, newest at the bottom */
        var idx = 0;
        while (idx < ev.length && ev[idx].t <= R.T) idx++;
        if (!force && idx === _evRenderedFor) return;
        _evRenderedFor = idx;
        var from = Math.max(0, idx - 80);
        var to = Math.min(ev.length, idx + 40);
        var html = '';
        for (var i = from; i < to; i++) {
            var e = ev[i];
            var aIdx = e.kind === 'alert' ? R.alerts.indexOf(e) : -1;
            html += '<li data-rp-t="' + e.t + '" data-rp-asset="' + esc(e.asset) + '"' +
                (aIdx >= 0 ? ' data-rp-alert="' + aIdx + '" title="Replay around this alert"' : '') + ' class="' +
                (e.kind === 'state' ? 's' : e.kind === 'alert' ? 'a' : '') + (i === idx - 1 ? ' now' : '') + '">' +
                '<time>' + fmtClock(e.t) + '</time><span><b>' + esc(e.asset) + '</b> ' + esc(e.text) + '</span></li>';
        }
        ul.innerHTML = html;
        var nowEl = ul.querySelector('li.now');
        if (nowEl && nowEl.scrollIntoView) nowEl.scrollIntoView({ block: 'nearest' });
    }

    /* ---- analog / relay panel for the selected asset -------------------- */
    function selectAsset(name) {
        if (!name) return;
        var a = R.byName[name];
        if (!a) {
            for (var k in R.byName) {
                if (R.byName.hasOwnProperty(k) && k.toUpperCase().replace(/[^A-Z0-9]/g, '') === String(name).toUpperCase().replace(/[^A-Z0-9]/g, '')) { a = R.byName[k]; break; }
            }
        }
        if (!a) return;
        if (R.selected !== a) R.selectedCol = null;
        R.selected = a;
        if (window.SipTelemetry && window.SipTelemetry.highlight) window.SipTelemetry.highlight(a.name, true);
        renderPanel(true);
    }

    function sparkFor(a, c, W, H) {
        var key = W + 'x' + H + ':' + a.rows.length;
        a.spark = a.spark || {};
        if (a.sparkKey !== String(a.rows.length)) { a.spark = {}; a.sparkKey = String(a.rows.length); }
        var ck = c + '@' + key;
        if (a.spark[ck]) return a.spark[ck];
        var span = (R.end - R.start) || 1;
        var lo = Infinity, hi = -Infinity;
        for (var r = 0; r < a.rows.length; r++) {
            var v = num(a.rows[r].v[c]);
            if (v === null) continue;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }
        if (lo === Infinity) { a.spark[ck] = { d: '', lo: null, hi: null }; return a.spark[ck]; }
        if (hi === lo) { hi = lo + 1; lo = lo - 1; }
        var step = Math.max(1, Math.floor(a.rows.length / (W * 1.5)));
        var d = '';
        for (var i = 0; i < a.rows.length; i += step) {
            var vv = num(a.rows[i].v[c]);
            if (vv === null) continue;
            var x = ((a.rows[i].t - R.start) / span) * W;
            var y = H - 2 - ((vv - lo) / (hi - lo)) * (H - 4);
            d += (d ? 'L' : 'M') + x.toFixed(1) + ' ' + y.toFixed(1);
        }
        a.spark[ck] = { d: d, lo: lo, hi: hi };
        return a.spark[ck];
    }

    function valueText(a, c) {
        if (a.ptr < 0) return '-';
        var v = a.rows[a.ptr].v[c];
        var col = a.cols[c];
        var n = num(v);
        if (n === null) return '-';
        if (col.isDL) return n >= 0.5 ? 'Up' : 'Down';
        return Math.abs(n) >= 100 ? n.toFixed(1) : n.toFixed(2);
    }

    var _panelSig = '';
    function renderPanel(force) {
        var host = byId('sipRpAttrs');
        var big = byId('sipRpBig');
        var head = byId('sipRpSelHead');
        if (!host) return;
        var a = R.selected;
        if (!a || !R.active) {
            if (force) {
                if (head) head.textContent = 'Analog and relay values';
                if (big) big.innerHTML = '';
                host.innerHTML = '<div class="sip-rp-empty">Click any track, point or signal in the yard to see its values at the cursor.</div>';
            }
            return;
        }
        var span = (R.end - R.start) || 1;
        var cx = ((R.T - R.start) / span);
        var sig = a.name + '|' + a.rows.length + '|' + a.ptr + '|' + R.selectedCol + '|' + (a.cols ? a.cols.length : 0);
        if (!force && sig === _panelSig) {
            /* only the cursor moved */
            var curs = host.querySelectorAll('line.rp-cur');
            for (var q = 0; q < curs.length; q++) { curs[q].setAttribute('x1', (cx * 200).toFixed(1)); curs[q].setAttribute('x2', (cx * 200).toFixed(1)); }
            var bc = big ? big.querySelector('line.rp-cur') : null;
            if (bc) { bc.setAttribute('x1', (cx * 1000).toFixed(1)); bc.setAttribute('x2', (cx * 1000).toFixed(1)); }
            return;
        }
        _panelSig = sig;
        var state = describeAsset(a.name);
        if (head) head.innerHTML = esc(a.name) + (state ? ' <span class="sip-rp-status">' + esc(state) + '</span>' : '') +
            (a.err ? ' <span class="rp-err">' + esc(a.err) + '</span>' : '');
        if (!a.cols || !a.rows.length) {
            host.innerHTML = '<div class="sip-rp-empty">' + (a.done ? 'No history rows in this window.' : 'Loading history...') + '</div>';
            if (big) big.innerHTML = '';
            return;
        }
        var order = [];
        for (var c = 0; c < a.cols.length; c++) if (!a.cols[c].skip && !a.cols[c].isDL) order.push(c);
        for (var c2 = 0; c2 < a.cols.length; c2++) if (!a.cols[c2].skip && a.cols[c2].isDL) order.push(c2);
        if (R.selectedCol === null && order.length) R.selectedCol = order[0];
        var html = '';
        for (var i = 0; i < order.length; i++) {
            var ci = order[i];
            var sp = sparkFor(a, ci, 200, 26);
            html += '<div class="sip-rp-attr' + (ci === R.selectedCol ? ' on' : '') + '" data-rp-col="' + ci + '">' +
                '<span class="n" title="' + esc(a.cols[ci].name) + '">' + esc(a.cols[ci].name) + '</span>' +
                '<span class="v">' + valueText(a, ci) + '</span>' +
                '<svg viewBox="0 0 200 26" preserveAspectRatio="none" aria-hidden="true">' +
                '<path class="' + (a.cols[ci].isDL ? 'rp-dl' : 'rp-trace') + '" d="' + sp.d + '" fill="none" stroke-width="1.2" vector-effect="non-scaling-stroke"/>' +
                '<line class="rp-cur" x1="' + (cx * 200).toFixed(1) + '" x2="' + (cx * 200).toFixed(1) + '" y1="0" y2="26" stroke-width="1" vector-effect="non-scaling-stroke"/>' +
                '</svg></div>';
        }
        host.innerHTML = html || '<div class="sip-rp-empty">No attributes in this history.</div>';

        if (big && R.selectedCol !== null && a.cols[R.selectedCol]) {
            var bsp = sparkFor(a, R.selectedCol, 1000, 150);
            var col = a.cols[R.selectedCol];
            var lbl = function (v) { return v === null ? '' : (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2)); };
            big.innerHTML =
                '<div style="display:flex;justify-content:space-between;margin-bottom:2px"><span><b>' + esc(col.name) +
                '</b> over the window (click to seek)</span><span>' + fmtDay(R.start) + ' to ' + fmtDay(R.end) + '</span></div>' +
                '<svg viewBox="0 0 1000 150" preserveAspectRatio="none" role="img" aria-label="' + esc(col.name) + ' history">' +
                '<rect class="rp-plot" x="0" y="0" width="1000" height="150"/>' +
                '<path class="' + (col.isDL ? 'rp-dl' : 'rp-trace') + '" d="' + bsp.d + '" fill="none" stroke-width="1.6" vector-effect="non-scaling-stroke"/>' +
                '<line class="rp-cur" x1="' + (cx * 1000).toFixed(1) + '" x2="' + (cx * 1000).toFixed(1) + '" y1="0" y2="150" stroke-width="1.5" vector-effect="non-scaling-stroke"/>' +
                '</svg>' +
                '<div style="display:flex;justify-content:space-between"><span>min ' + lbl(bsp.lo) + '</span><span>max ' + lbl(bsp.hi) + '</span></div>';
        } else if (big) {
            big.innerHTML = '';
        }
    }

    /* ======================================================================
       OPEN / CLOSE
       ====================================================================== */
    function open() {
        if (!ensureUi()) return;
        var card = byId('sipCardSection');
        if (card && !card.classList.contains('fullscreen')) {
            var fs = byId('sipFullscreenBtn');
            if (fs) fs.click();
        }
        if (card) card.classList.add('sip-replaying');
        R.open = true;
        syncRpTheme();
        setTimeout(syncRpTheme, 250);
        var b = byId('sipReplayBtn');
        if (b) b.classList.add('on');
        renderEvents(true);
        renderPanel(true);
    }
    function close() {
        if (R.active) exitReplay();
        R.open = false;
        var card = byId('sipCardSection');
        if (card) card.classList.remove('sip-replaying');
        var b = byId('sipReplayBtn');
        if (b) b.classList.remove('on');
        if (window.SipTelemetry && window.SipTelemetry.highlight) window.SipTelemetry.highlight('');
    }

    function boot() {
        ensureButton();
        /* #sipControls is shown/filled by the host after a site loads */
        var tries = 0;
        var t = setInterval(function () {
            ensureButton();
            if (byId('sipReplayBtn') || ++tries > 60) clearInterval(t);
        }, 1000);
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();

    window.SipReplay = {
        open: open,
        close: close,
        isOpen: function () { return R.open; },
        onCellClick: function (cell, assetName) { selectAsset(assetName); },
        onReplayEnded: function () {
            /* site changed or live resumed from elsewhere */
            if (R.active) {
                R.gen++;
                R.active = false;
                setPlaying(false);
                setStatusText('Replay ended (site changed).');
            }
        },
        _r: R
    };
})();
