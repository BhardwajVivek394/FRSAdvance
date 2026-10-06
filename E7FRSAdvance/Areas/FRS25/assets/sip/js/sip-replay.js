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
 *  v618.11: tsLimit = 10000, page 1 (whole window per asset in one call,
 *  cursor only if the window is longer); rows bound in chunks of 1500
 *  (12 ms slices) so playback starts while the rest is still binding.
 *  The text below describes the pre-618.11 adaptive sizing.
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
    /* v618.11 -- one big page per asset (page=1, tsLimit up to 10000) instead
       of many small pages; rows are bound to the timeline in CHUNKS so the
       browser stays responsive while ~10000 x N rows are converted. */
    var REPLAY_PAGE = 10000;
    var CHUNK_ROWS = 1500;      /* rows converted per slice              */
    var CHUNK_BUDGET_MS = 12;   /* time per slice before yielding a frame */
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
        alerts: [], follow: true, pre: 120000, post: 60000, lastFollow: -1, pendingAround: null,
        mode: 'yard', aroundIdx: -1, lo: 0, hi: 0, loop: false, lastApply: 0, failKey: '', yardWin: null
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

    /* v618.14 view range: whole loaded window, or the around-alert slice */
    function vlo() { return (R.mode === 'alert' && R.hi > R.lo) ? R.lo : R.start; }
    function vhi() { return (R.mode === 'alert' && R.hi > R.lo) ? R.hi : R.end; }

    /* horizon (ms of data kept ahead of the cursor) follows playback speed */
    function horizonMs() { return Math.max(60, 45 * R.speed) * 1000; }

    function initialTsLimit() {
        return REPLAY_PAGE;
    }
    function initialTsLimitAdaptive() {   /* pre-618.11 sizing, kept for reference */
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
                var typeOf = {};
                for (var i = 0; i < data.length; i++) {
                    var it = data[i] || {};
                    var id = it.AssetId != null ? String(it.AssetId) : '';
                    var name = String(it.AssetName || '').trim();
                    if (id && name && !byId[id]) byId[id] = name;
                    if (id && it.AssetTypeId != null && typeOf[id] == null) typeOf[id] = it.AssetTypeId;
                }
                /* fallback / top-up: whatever the live page already knows */
                var ld = window.wsLiveData || {};
                for (var k in ld) {
                    if (!ld.hasOwnProperty(k) || byId[k]) continue;
                    var e = ld[k];
                    if (e && e.AssetName) byId[k] = String(e.AssetName).trim();
                    if (e && e.AssetTypeId != null && typeOf[k] == null) typeOf[k] = e.AssetTypeId;
                }
                var list = [];
                for (var aid in byId) if (byId.hasOwnProperty(aid)) list.push({ id: aid, name: byId[aid], typeId: typeOf[aid] });
                R.assetCache[siteId] = list;
                cb(list);
            },
            error: function () { cb([]); }
        });
    }

    /* v618.9 -- METADATA for name resolution, independent of what the user
       searched on the page: GetBulkAssetMetadata per asset type on the SIP
       (never for Point Machine -- the page forbids that call).
         dlByRole[Value] / dlByAttrId[DataloggerAttributeId] / dlByName[DataloggerAssetName]
           -> DataloggerAttribute (e.g. "TPR")
         titles[attrId] -> Title (e.g. "If mA") */
    var META_URL = '/FRS25/Telemetry/GetBulkAssetMetadata';
    function normKey(s) { return String(s == null ? '' : s).toUpperCase().replace(/[^A-Z0-9]/g, ''); }
    function loadMeta(siteId, typeIds, cb) {
        R.metaCache = R.metaCache || {};
        var todo = [];
        for (var i = 0; i < typeIds.length; i++) {
            var t = parseInt(typeIds[i], 10);
            if (!t || t === 3) continue;
            if (!R.metaCache[siteId + '|' + t]) todo.push(t);
        }
        if (!todo.length) { cb(); return; }
        var left = todo.length;
        todo.forEach(function (t) {
            $.ajax({
                url: META_URL, type: 'POST', contentType: 'application/json', dataType: 'json',
                data: JSON.stringify({ SearchCriteria: { SiteId: siteId, AssetTypeId: t } }),
                success: function (resp) {
                    var assets = (resp && resp.mAssets) || [];
                    R.meta = R.meta || {};
                    for (var a = 0; a < assets.length; a++) {
                        var as = assets[a] || {};
                        var m = R.meta[String(as.Id)] = { titles: {}, dlByRole: {}, dlByAttrId: {}, dlByName: {} };
                        var at = as.assetAttributes || as.AssetAttributes || [];
                        for (var j = 0; j < at.length; j++) if (at[j] && at[j].Id != null) m.titles[String(at[j].Id)] = at[j].Title || at[j].AliasName || '';
                        var dl = as.mAssetInfoDataloggers || as.MAssetInfoDataloggers || [];
                        for (var k = 0; k < dl.length; k++) {
                            var d = dl[k] || {};
                            var nm = String(d.DataloggerAttribute || '').trim();
                            if (!nm) continue;
                            if (d.Value != null && d.Value !== '') m.dlByRole[String(d.Value).trim()] = nm;
                            if (d.DataloggerAttributeId != null) m.dlByAttrId[String(d.DataloggerAttributeId)] = nm;
                            if (d.DataloggerAssetName) m.dlByName[normKey(d.DataloggerAssetName)] = nm;
                        }
                    }
                    R.metaCache[siteId + '|' + t] = true;
                    if (--left === 0) cb();
                },
                error: function () { if (--left === 0) cb(); }
            });
        });
    }
    function dlNameFor(a, col) {
        var m = (R.meta || {})[String(a.id)];
        var raw = String(col.name || '').trim();
        if (m) {
            var hit = m.dlByRole[raw] || m.dlByAttrId[String(col.attrId)] || m.dlByName[normKey(raw)];
            if (hit) return { name: hit, role: /^\d+$/.test(raw) ? raw : '' };
        }
        return { name: /^\d+$/.test(raw) ? '' : raw, role: /^\d+$/.test(raw) ? raw : '' };
    }

    function newAsset(id, name, isPM, typeId) {
        return {
            id: id, name: name, isPM: !!isPM, typeId: typeId != null ? typeId : null, seeded: false, seeding: false, seedT: null, url: isPM ? PM_HISTORY_URL : HISTORY_URL, cols: null, rows: [], page: 1, cursor: '', done: false,
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
        if (!a.seeded) return !a.seeding;
        if (a.done || a.loading || a.binding) return false;
        if (!a.rows.length) return true;
        if (a.desc) return true;                 /* newest-first upstream: need the whole window */
        return lastT(a) < R.T + horizonMs();
    }
    function pump() {
        if (!R.active) return;
        var want = [];
        for (var i = 0; i < R.assets.length; i++) if (needsData(R.assets[i])) want.push(R.assets[i]);
        want.sort(function (x, y) { return lastT(x) - lastT(y); });
        for (var j = 0; j < want.length && R.inflight < MAX_CONCURRENT; j++) {
            if (!want[j].seeded) fetchSeed(want[j]);
            else fetchPage(want[j]);
        }
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

    /* v618.9 SEED -- DashboardHistory fills gaps only INSIDE the requested
       window, so a relay / value that last changed before "From" is empty at
       the start and the yard looked blank. One extra call per asset asks for
       the newest row of the 24 h before From (sort=desc, pageSize=1,
       fillGaps=true) and applies it AT From. */
    function fetchSeed(a) {
        var gen = R.gen;
        a.seeding = true;
        R.inflight++;
        $.ajax({
            url: a.url, type: 'GET', dataType: 'json',
            data: {
                assetId: a.id, startDate: fmtApi(R.start - 24 * 3600000), endDate: fmtApi(R.start),
                page: 1, pageSize: 1, sort: 'desc', fillGaps: 'true', cursor: ''
            },
            success: function (resp) {
                if (gen !== R.gen) return;
                R.inflight--;
                a.seeding = false;
                a.seeded = true;
                if (resp && !resp.error) absorbPage(a, resp, true);
                afterPage();
            },
            error: function () {
                if (gen !== R.gen) return;
                R.inflight--;
                a.seeding = false;
                a.seeded = true;
                afterPage();
            }
        });
    }

    function colKey(c) { return String(c.attrId) + '|' + String(c.dtype) + '|' + String(c.name); }

    function absorbPage(a, resp, isSeed) {
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
        if (!isSeed && !a.desc && rows.length > CHUNK_ROWS) {
            /* v618.11: big ascending page -> bind in chunks (see chunkPump) */
            var nc = pick(resp, ['nextCursor', 'NextCursor']);
            if (nc === undefined) nc = pick(payload, ['nextCursor', 'NextCursor']);
            var hn = pick(resp, ['hasNextPage', 'HasNextPage']);
            if (hn === undefined) hn = pick(payload, ['hasNextPage', 'HasNextPage']);
            if (hn === undefined) hn = rows.length >= a.tsLimit;
            a.binding = true;
            CHUNK_Q.push({ a: a, rows: rows, map: map, i: 0, gen: R.gen, next: nc || '', hasNext: !!hn });
            chunkPump();
            return;
        }
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
        if (isSeed) {
            if (!out.length) return;
            var newest = out[0];
            for (var sx = 1; sx < out.length; sx++) if (out[sx].t > newest.t) newest = out[sx];
            a.seedT = newest.t;
            newest.t = Math.min(newest.t, R.start);
            if (!a.rows.length || a.rows[0].t > newest.t) a.rows.unshift(newest);
            rebuildDlEvents(a);
            a.sparkKey = '';
            return;
        }
        if (out.length > 1 && a.desc === null) a.desc = out[0].t > out[out.length - 1].t;
        if (a.desc === null && out.length) a.desc = false;

        /* v618.11: page size stays REPLAY_PAGE (no density re-sizing) */

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

    /* ---- chunked binding -------------------------------------------------- */
    var CHUNK_Q = [];
    var chunkTimer = null;
    function chunkPump() {
        if (chunkTimer) return;
        chunkTimer = setTimeout(function () {
            chunkTimer = null;
            var t0 = Date.now();
            var touched = false;
            while (CHUNK_Q.length && (Date.now() - t0) < CHUNK_BUDGET_MS) {
                var job = CHUNK_Q[0];
                if (job.gen !== R.gen) { CHUNK_Q.shift(); continue; }
                var a = job.a;
                var end = Math.min(job.rows.length, job.i + CHUNK_ROWS);
                for (var r = job.i; r < end; r++) {
                    var row = job.rows[r];
                    if (!row) continue;
                    var t = tsOf(row.ts || row.Ts);
                    if (isNaN(t)) continue;
                    var src = row.v || row.V || [];
                    var v = new Array(a.cols.length);
                    for (var ci = 0; ci < job.map.length; ci++) v[job.map[ci]] = src[ci];
                    a.rows.push({ t: t, ts: row.ts || row.Ts, v: v });
                }
                job.i = end;
                touched = true;
                if (job.i >= job.rows.length) {
                    CHUNK_Q.shift();
                    var f0 = tsOf(job.rows[0] && (job.rows[0].ts || job.rows[0].Ts));
                    var f1 = tsOf(job.rows[job.rows.length - 1] && (job.rows[job.rows.length - 1].ts || job.rows[job.rows.length - 1].Ts));
                    if (f0 > f1) {   /* upstream ignored sort=asc */
                        a.desc = true;
                        a.rows.sort(function (x, y) { return x.t - y.t; });
                    }
                    if (a.desc === null) a.desc = false;
                    a.page++;
                    a.cursor = job.next;
                    if (!job.hasNext || !job.next || !job.rows.length) a.done = true;
                    a.binding = false;
                    rebuildDlEvents(a);
                }
                a.sparkKey = '';
            }
            if (touched) afterPage();
            if (CHUNK_Q.length) chunkPump();
        }, 0);
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
                    a.dl.push({ t: a.rows[r].t, asset: a.name, text: col.name + (on ? ' \u2191 Pickup' : ' \u2193 Drop'), up: on, kind: 'relay' });
                }
                prev = on;
            }
        }
    }
    function allEvents() {
        if (!R.eventsDirty) return R.events;
        var ev = [];
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
            var item = {
                AssetName: a.name,
                AssetId: a.id,
                AssetTypeId: a.typeId,
                AssetAttributeName: col.name,
                AssetAttributeId: col.attrId,
                DataType: col.dtype,
                Value: v,
                TimestampDevice: row.ts,
                IsFresh: true,
                BroadcastKind: 'history'
            };
            if (col.isDL) {
                var dn = col.dlName || (col.dlName = dlNameFor(a, col));
                if (dn.name) item.DataloggerAttribute = dn.name;
                if (dn.role) item.Role = dn.role;
            } else {
                var mt = (R.meta || {})[String(a.id)];
                var title = mt && mt.titles[String(col.attrId)];
                if (title && !col.name) item.AssetAttributeName = title;
            }
            items.push(item);
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
        applyFaults();
    }
    /* v618.14: failure alerts (alertTypeId 2) active at T flash on the yard */
    function activeFailures(t) {
        var names = [];
        for (var i = 0; i < R.alerts.length; i++) {
            var a = R.alerts[i];
            if (!a.fail || !a.asset) continue;
            var endT = a.reset !== null ? a.reset : R.end;
            if (t >= a.t && t <= endT && names.indexOf(a.asset) === -1) names.push(a.asset);
        }
        return names;
    }
    function applyFaults() {
        if (!R.active) return;
        var names = activeFailures(R.T);
        var key = names.join('|');
        if (key === R.failKey) return;
        R.failKey = key;
        if (typeof RT.setFaults === 'function') RT.setFaults(names);
        var b = byId('sipRpFaults');
        if (b) {
            b.textContent = names.length ? names.length + ' active failure' + (names.length > 1 ? 's' : '') : 'No active failures';
            b.classList.toggle('on', names.length > 0);
            b.title = names.join(', ');
        }
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
            var next = Math.min(vhi(), R.T + dt * R.speed);
            if (!covered(next)) {
                R.buffering = true;
                next = Math.max(R.T, Math.min(next, bufferedUntil()));
                pump();
            } else {
                R.buffering = false;
            }
            R.T = next;
            /* v618.14: rule engine fed at most ~16 times a second; the
               clock / cursor still move every frame (smooth, less work) */
            var atEnd = R.T >= vhi();
            if (atEnd || now - R.lastApply >= 60) {
                R.lastApply = now;
                applyAt(R.T, true);
                if (R.mode === 'yard') followAlerts();
            }
            if (atEnd) {
                if (R.mode === 'alert' && R.loop) { R.T = vlo(); applyAt(R.T, false); }
                else setPlaying(false);
            }
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
        if (R.playing && R.T >= vhi()) {
            R.T = vlo();
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
        R.T = clamp(t, vlo(), vhi());
        applyAt(R.T, false);
        if (!covered(R.T)) { R.buffering = true; pump(); }
        renderClock();
        renderPanel(false);
        renderEvents(true);
    }
    /* v618.14: prev / next alert. Whole yard: jump to the alert's lead-up.
       Around alert: move to the previous / next alert's window. */
    function jumpAlert(dir) {
        if (!R.alerts.length) return;
        var i;
        if (R.mode === 'alert' && R.aroundIdx >= 0) {
            i = R.aroundIdx + dir;
            if (i < 0 || i >= R.alerts.length) return;
            openAround(i, R.playing);
            return;
        }
        var target = -1;
        if (dir > 0) {
            for (i = 0; i < R.alerts.length; i++) if (R.alerts[i].t - R.pre > R.T + 500) { target = i; break; }
        } else {
            for (i = R.alerts.length - 1; i >= 0; i--) if (R.alerts[i].t - R.pre < R.T - 500) { target = i; break; }
        }
        if (target < 0) return;
        if (R.alerts[target].asset) selectAsset(R.alerts[target].asset);
        seek(Math.max(R.start, R.alerts[target].t - R.pre));
    }
    /* Around alert: if the slice lies inside the loaded window, just restrict
       playback (no reload, instant); otherwise reload that slice. */
    function openAround(idx, play) {
        var al = R.alerts[idx];
        if (!al) return;
        var from = al.t - R.pre, to = Math.min(Date.now(), al.t + R.post);
        R.aroundIdx = idx;
        R.aroundId = al.id;
        setMode('alert');
        if (R.active && from >= R.start && to <= R.end) {
            R.lo = from;
            R.hi = to;
            if (al.asset) selectAsset(al.asset);
            setSpeed(1);
            seek(from);
            drawTimelineMarks();
            renderAlerts();
            if (play) setPlaying(true);
            return;
        }
        R.yardWin = R.yardWin || (R.active ? { from: R.start, to: R.end } : null);
        byId('sipRpFrom').value = toLocalInput(from);
        byId('sipRpTo').value = toLocalInput(to);
        R.pendingAround = { asset: al.asset, idx: idx };
        loadWindow();
    }
    function setMode(m) {
        R.mode = m;
        var seg = byId('sipRpMode');
        if (seg) {
            var bs = seg.querySelectorAll('button[data-rp-mode]');
            for (var i = 0; i < bs.length; i++) bs[i].setAttribute('aria-pressed', String(bs[i].getAttribute('data-rp-mode') === m));
        }
    }
    function toWholeYard() {
        setMode('yard');
        R.aroundIdx = -1;
        if (R.yardWin && (R.yardWin.from !== R.start || R.yardWin.to !== R.end)) {
            byId('sipRpFrom').value = toLocalInput(R.yardWin.from);
            byId('sipRpTo').value = toLocalInput(R.yardWin.to);
            R.yardWin = null;
            loadWindow();
            return;
        }
        R.lo = R.hi = 0;
        drawTimelineMarks();
        renderAlerts();
        renderClock();
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
        CHUNK_Q.length = 0;
        R.seenEvents = [];
        R.alerts = [];
        R.failKey = '';
        if (typeof RT.setFaults === 'function') RT.setFaults([]);
        if (!R.pendingAround) { setMode('yard'); R.aroundIdx = -1; R.lo = R.hi = 0; }
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
                var a = newAsset(it.id, it.name, isPM, it.typeId);
                R.assets.push(a);
                R.byName[it.name] = a;
            }
            if (!R.assets.length) {
                setStatusText('No SIP assets found for this site.');
                return;
            }
            drawTimeline();
            setStatusText('Loading asset metadata...');
            var types = [];
            for (var ti = 0; ti < R.assets.length; ti++) {
                var tt = R.assets[ti].typeId;
                if (tt != null && types.indexOf(tt) === -1) types.push(tt);
            }
            loadMeta(siteId, types.length ? types : [1, 2], function () {
                if (gen !== R.gen) return;
                pump();
            });
            renderPanel(true);
            loadAlerts(gen);
            if (R.pendingAround) {
                var pa = R.pendingAround;
                R.pendingAround = null;
                setMode('alert');
                R.lo = R.start;
                R.hi = R.end;
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
                var rt = tsOf(x.resetTime);
                var typeId = parseInt(x.alertTypeId, 10);
                out.push({
                    t: t, asset: String(x.assetName || '').trim(), id: x.id,
                    typeId: typeId, fail: typeId === 2, type: x.alertType || '',
                    reset: isNaN(rt) ? null : rt, ack: !!x.acknowledged,
                    text: (x.alertType ? x.alertType + ': ' : '') + (x.causeCode || x.description || 'alert') +
                        (x.acknowledged ? ' (ack)' : ''),
                    kind: 'alert'
                });
            }
            out.sort(function (p, q) { return p.t - q.t; });
            R.alerts = out;
            if (R.mode === 'alert' && R.aroundId != null) {
                R.aroundIdx = -1;
                for (var ai2 = 0; ai2 < out.length; ai2++) if (out[ai2].id === R.aroundId) { R.aroundIdx = ai2; break; }
            }
            R.eventsDirty = true;
            R.failKey = '';
            applyFaults();
            drawTimelineMarks();
            renderAlerts();
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
                    /* server may return everything at once (Pager.Take = -1): stop then */
                    if (raw === ALERT_PAGE && i + 1 < ALERT_MAX_PAGES) page(i + 1);
                    else finish();
                },
                error: function () { finish(); }
            });
        }
        page(0);
    }
    function replayAround(alert) {
        var idx = R.alerts.indexOf(alert);
        if (idx >= 0) openAround(idx, true);
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
                    if (al.asset) {
                        /* v618.21: in the all-assets dock, follow = mark + scroll
                           that asset's card (the list stays); detail view = select */
                        if (R.selected) selectAsset(al.asset);
                        else markCard(al.asset);
                    }
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
        '.sip-rp-dock.sip-rp-dock3{grid-template-columns:minmax(220px,270px) minmax(0,1fr) minmax(200px,250px);}' +
        '.sip-rp-allgrid{display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:8px;padding:8px;}' +
        '.sip-rp-acard{border:1px solid var(--rp-rule);border-radius:8px;background:var(--rp-plot);overflow:hidden;}' +
        '.sip-rp-ahead{display:flex;align-items:center;gap:8px;width:100%;border:none;border-bottom:1px solid var(--rp-rule);background:transparent;color:var(--rp-text);font:inherit;padding:5px 8px;cursor:pointer;text-align:left;}' +
        '.sip-rp-ahead b{font-size:13px;} .sip-rp-ahead span{color:var(--rp-muted);font-size:11.5px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-rp-ahead i{margin-left:auto;font-style:normal;font-size:10px;color:var(--rp-muted);} .sip-rp-ahead:hover b{color:var(--rp-accent);}' +
        '.sip-rp-arow{display:grid;grid-template-columns:minmax(0,1fr) 74px 70px;gap:6px;align-items:center;padding:2px 8px;}' +
        '.sip-rp-arow .n{color:var(--rp-muted);font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.sip-rp-arow .v{text-align:right;font-weight:700;font-size:12px;font-variant-numeric:tabular-nums;}' +
        '.sip-rp-arow svg{display:block;width:100%;height:16px;}' +
        '.sip-rp-acard.follow{border-color:var(--rp-alert);box-shadow:0 0 0 1px var(--rp-alert);}' +
        '.sip-rp-back{border:1px solid var(--rp-rule);background:transparent;color:var(--rp-text);border-radius:6px;height:24px;padding:0 8px;font:inherit;font-size:12px;cursor:pointer;margin-right:4px;}' +
        '.sip-rp-back:hover{border-color:var(--rp-accent);}' +
        '.sip-card.sip-replaying .sip-rp{max-height:52vh;}' +
        '.sip-rp-opts{display:flex;flex-wrap:wrap;gap:6px 12px;padding:7px 10px;border-top:1px solid var(--rp-rule);font-size:12px;}' +
        '.sip-rp-list li.pk span{color:#1F9D55;} .sip-rp-list li.dr span{color:#B88700;}' +
        '.rp-pk{color:#1F9D55;font-weight:700;} .rp-dr{color:#B88700;font-weight:700;}' +
        '.sip-rp-alist li.a span{color:var(--rp-text);font-weight:500;} .sip-rp-alist li.a time{color:var(--rp-muted);}' +
        '.sip-rp-alist li.f span,.sip-rp-alist li.f time{color:var(--rp-alert);}' +
        '.sip-rp-alist li.f{box-shadow:inset 3px 0 0 var(--rp-alert);}' +
        '.sip-rp-alist li.cur{background:var(--rp-sel);}' +
        '.sip-rp-alist small{color:var(--rp-muted);margin-left:4px;}' +
        '.sip-rp-tag{font-style:normal;font-size:10.5px;font-weight:700;color:#fff;background:var(--rp-alert);padding:0 5px;border-radius:3px;}' +
        '.sip-rp-pin.cur{outline:2px solid var(--rp-text);}' +
        '.sip-rp-faults{font-size:12px;font-weight:600;padding:3px 9px;border-radius:5px;color:var(--rp-muted);border:1px solid var(--rp-rule);}' +
        '.sip-rp-faults.on{color:#fff;background:var(--rp-alert);border-color:var(--rp-alert);animation:sipRpBlink 1s steps(2,jump-none) infinite;}' +
        '@keyframes sipRpBlink{50%{opacity:.45;}}' +
        '@media (max-width:1200px){.sip-rp-dock.sip-rp-dock3{grid-template-columns:minmax(220px,280px) minmax(0,1fr);}.sip-rp-dock3 > .sip-rp-col:last-child{grid-column:1 / -1;}}' +
        '@media (max-width:900px){.sip-rp-dock,.sip-rp-dock.sip-rp-dock3{grid-template-columns:1fr;}}';

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
            '  <div class="sip-seg" id="sipRpMode" role="group" aria-label="Replay range">' +
            '    <button type="button" data-rp-mode="yard" aria-pressed="true">Whole yard</button>' +
            '    <button type="button" data-rp-mode="alert" aria-pressed="false">Around alert</button>' +
            '  </div>' +
            '  <span class="sip-rp-status" id="sipRpStatus">Choose a window and load.</span>' +
            '  <span style="flex:1"></span>' +
            '  <span class="sip-rp-faults" id="sipRpFaults">No active failures</span>' +
            '  <button type="button" class="sip-rp-btn" id="sipRpExit"><i class="fas fa-tower-broadcast"></i> Back to live</button>' +
            '</div>' +
            '<div class="sip-rp-row">' +
            '  <button type="button" class="sip-rp-btn" id="sipRpPrev" title="Previous alert"><i class="fas fa-backward-step"></i></button>' +
            '  <button type="button" class="sip-rp-play" id="sipRpPlay" aria-label="Play"><i class="fas fa-play"></i></button>' +
            '  <button type="button" class="sip-rp-btn" id="sipRpNext" title="Next alert"><i class="fas fa-forward-step"></i></button>' +
            '  <div class="sip-rp-tl"><div class="sip-rp-pins" id="sipRpPins"></div><div class="sip-rp-ticks" id="sipRpTicks"></div><div class="sip-rp-buf" id="sipRpBuf"></div>' +
            '    <input type="range" id="sipRpSeek" min="0" max="1000" value="0" step="1" aria-label="Replay time"></div>' +
            '  <div class="sip-seg" id="sipRpSpeed" role="group" aria-label="Playback speed">' + SPEEDS.map(function (s) {
                return '<button type="button" data-rp-speed="' + s + '" aria-pressed="' + (s === R.speed) + '">' +
                    (s === 0.25 ? '&frac14;' : s) + '&times;</button>';
            }).join('') + '</div>' +
            '  <span class="sip-rp-clock" id="sipRpClock">--:--:--</span>' +
            '</div>' +
            '<div class="sip-rp-dock sip-rp-dock3">' +
            '  <div class="sip-rp-col"><h4>Alerts <span class="sip-rp-status" id="sipRpAlCount"></span></h4>' +
            '    <ul class="sip-rp-list sip-rp-alist" id="sipRpAlerts"></ul>' +
            '    <div class="sip-rp-opts">' +
            '      <label>Before <select id="sipRpPre"><option value="60000">1 min</option><option value="120000" selected>2 min</option><option value="300000">5 min</option><option value="600000">10 min</option></select></label>' +
            '      <label>After <select id="sipRpPost"><option value="30000">30 s</option><option value="60000" selected>1 min</option><option value="180000">3 min</option><option value="300000">5 min</option></select></label>' +
            '      <label><input type="checkbox" id="sipRpLoop"> Loop</label>' +
            '      <label><input type="checkbox" id="sipRpFollow" checked> Follow alerts</label>' +
            '    </div></div>' +
            '  <div class="sip-rp-col"><h4 id="sipRpSelHead">Analog and relay values</h4><div class="sip-rp-big" id="sipRpBig"></div><div class="sip-rp-attrs" id="sipRpAttrs"><div class="sip-rp-empty">Click any track, point or signal in the yard to see its values at the cursor.</div></div></div>' +
            '  <div class="sip-rp-col"><h4>Relay events <span class="sip-rp-status" id="sipRpEvCount"></span></h4><ul class="sip-rp-list" id="sipRpEvents"></ul></div>' +
            '</div>';
        var after = byId('sipLegend2') || canvas;   /* v618.4: keep the legend right under the yard */
        after.parentNode.insertBefore(box, after.nextSibling);

        box.addEventListener('click', function (e) {
            var pin = e.target.closest('[data-rp-pin]');
            if (pin) { openAround(+pin.getAttribute('data-rp-pin'), true); return; }
            var mb = e.target.closest('[data-rp-mode]');
            if (mb) {
                if (mb.getAttribute('data-rp-mode') === 'yard') toWholeYard();
                else {
                    var nx = 0;
                    for (var q = 0; q < R.alerts.length; q++) if (R.alerts[q].t >= R.T) { nx = q; break; }
                    if (R.alerts.length) openAround(R.aroundIdx >= 0 ? R.aroundIdx : nx, false);
                }
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
            var pk = e.target.closest('[data-rp-pick]');
            if (pk) { selectAsset(pk.getAttribute('data-rp-pick')); return; }
            if (e.target.closest('[data-rp-all]')) {
                R.selected = null;
                R.selectedCol = null;
                if (window.SipTelemetry && typeof window.SipTelemetry.highlight === 'function') window.SipTelemetry.highlight('');
                renderPanel(true);
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
        byId('sipRpPrev').addEventListener('click', function () { jumpAlert(-1); });
        byId('sipRpNext').addEventListener('click', function () { jumpAlert(1); });
        byId('sipRpLoop').addEventListener('change', function () { R.loop = !!this.checked; });
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
            seek(vlo() + (+this.value / 1000) * (vhi() - vlo()));
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
        if (CHUNK_Q.length) {
            var left = 0;
            for (var qi = 0; qi < CHUNK_Q.length; qi++) left += CHUNK_Q[qi].rows.length - CHUNK_Q[qi].i;
            txt += ', binding ' + left + ' rows';
        }
        if (R.buffering) txt += ', buffering to ' + fmtClock(R.T);
        else if (R.inflight) txt += ', loading';
        else txt += ', buffered to ' + fmtClock(bufferedUntil());
        if (err) txt += ', ' + err + ' failed';
        var empty = 0;
        for (var ei = 0; ei < R.assets.length; ei++) if (R.assets[ei].ptr < 0 && (R.assets[ei].done || R.assets[ei].rows.length)) empty++;
        if (empty) txt += ', ' + empty + ' with no value at cursor';
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
        var lo = vlo(), hi = vhi();
        var span = (hi - lo) || 1;
        var ev = allEvents();
        var step = Math.max(1, Math.ceil(ev.length / 400));
        var html = '';
        var pins = byId('sipRpPins');
        if (pins) {
            /* failure alerts as labelled flags; other alerts as red ticks */
            var ph = '';
            for (var ai = 0; ai < R.alerts.length; ai++) {
                var al = R.alerts[ai];
                if (al.t < lo || al.t > hi) continue;
                var left = (((al.t - lo) / span) * 100).toFixed(2);
                if (al.fail) {
                    ph += '<button type="button" class="sip-rp-pin' + (ai === R.aroundIdx ? ' cur' : '') + '" data-rp-pin="' + ai + '" title="' +
                        esc(fmtClock(al.t) + ' ' + al.asset + ' ' + al.text) + ' -- replay around it" style="left:' + left + '%">' + esc(al.asset || 'Failure') + '</button>';
                } else {
                    html += '<b class="a" style="left:' + left + '%" title="' + esc(fmtClock(al.t) + ' ' + al.asset + ' ' + al.text) + '"></b>';
                }
            }
            pins.innerHTML = ph;
        }
        for (var i = 0; i < ev.length; i += step) {
            if (ev[i].t < lo || ev[i].t > hi) continue;
            html += '<b class="' + (ev[i].kind === 'state' ? 's' : '') + '" style="left:' +
                (((ev[i].t - lo) / span) * 100).toFixed(2) + '%"></b>';
        }
        ticks.innerHTML = html;
        if (buf) buf.style.width = clamp(((bufferedUntil() - lo) / span) * 100, 0, 100).toFixed(2) + '%';
        var cnt = byId('sipRpEvCount');
        if (cnt) cnt.textContent = ev.length ? '(' + ev.length + ')' : '';
        renderEvents(true);
        updateStatus();
    }
    function renderClock() {
        var c = byId('sipRpClock');
        if (c) c.textContent = R.active ? fmtDay(R.T) : '--:--:--';
        var s = byId('sipRpSeek');
        var lo = vlo(), sp = (vhi() - lo) || 1;
        if (s && R.active) s.value = Math.round(((R.T - lo) / sp) * 1000);
        var buf = byId('sipRpBuf');
        if (buf && R.active) buf.style.width = clamp(((bufferedUntil() - lo) / sp) * 100, 0, 100).toFixed(2) + '%';
        if (R.buffering) updateStatus();
    }

    /* v618.14 alerts column: failures first-class, click = replay around */
    function renderAlerts() {
        var ul = byId('sipRpAlerts');
        if (!ul) return;
        var cnt = byId('sipRpAlCount');
        var fails = 0;
        for (var f = 0; f < R.alerts.length; f++) if (R.alerts[f].fail) fails++;
        if (cnt) cnt.textContent = R.alerts.length ? '(' + R.alerts.length + (fails ? ', ' + fails + ' failure' + (fails > 1 ? 's' : '') : '') + ')' : '';
        if (!R.alerts.length) {
            ul.innerHTML = '<li style="cursor:default"><span class="sip-rp-status">' + (R.active ? 'No alerts in this window.' : 'Load a window to replay.') + '</span></li>';
            return;
        }
        var html = '';
        for (var i = 0; i < R.alerts.length; i++) {
            var a = R.alerts[i];
            html += '<li data-rp-pin="' + i + '" class="' + (a.fail ? 'f' : 'a') + (i === R.aroundIdx && R.mode === 'alert' ? ' cur' : '') + '" title="Replay around this alert">' +
                '<time>' + fmtClock(a.t) + '</time><span><b>' + esc(a.asset) + '</b> ' +
                (a.fail ? '<em class="sip-rp-tag">Failure</em> ' : '') + esc(a.text) +
                (a.reset ? '<small> reset ' + fmtClock(a.reset) + '</small>' : '') + '</span></li>';
        }
        ul.innerHTML = html;
        var cur = ul.querySelector('li.cur');
        if (cur && cur.scrollIntoView) cur.scrollIntoView({ block: 'nearest' });
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
                (e.kind === 'state' ? 's' : e.kind === 'alert' ? 'a' : '') + (e.kind === 'relay' ? (e.up ? ' pk' : ' dr') : '') + (i === idx - 1 ? ' now' : '') + '">' +
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
        if (col.isDL) return n >= 0.5 ? '<span class="rp-pk">&uarr; Pickup</span>' : '<span class="rp-dr">&darr; Drop</span>';
        return Math.abs(n) >= 100 ? n.toFixed(1) : n.toFixed(2);
    }

    var _panelSig = '';
    /* v618.21 -- dock default: EVERY asset of the SIP as a card with all its
       readings at the cursor timestamp + a playback history line (cursor
       marked). Built once per data change; on play only the values, states
       and cursor lines are updated in place. Click a card title -> detail. */
    function renderAllAssets(force, host, big, head) {
        var span = (R.end - R.start) || 1;
        var cx = Math.max(0, Math.min(1, (R.T - R.start) / span));
        var sig = 'ALL|' + R.assets.map(function (x) { return x.name + ':' + x.rows.length + ':' + (x.cols ? x.cols.length : 0) + ':' + (x.done ? 1 : 0); }).join(',');
        if (!force && sig === _panelSig) {
            var curs = host.querySelectorAll('line.rp-cur');
            for (var q = 0; q < curs.length; q++) { curs[q].setAttribute('x1', (cx * 200).toFixed(1)); curs[q].setAttribute('x2', (cx * 200).toFixed(1)); }
            var vs = host.querySelectorAll('[data-rp-v]');
            for (var w = 0; w < vs.length; w++) {
                var p = vs[w].getAttribute('data-rp-v').split(':');
                var aa = R.assets[+p[0]];
                if (aa) { var vt = valueText(aa, +p[1]); if (vs[w].innerHTML !== vt) vs[w].innerHTML = vt; }
            }
            var ss = host.querySelectorAll('[data-rp-st]');
            for (var z = 0; z < ss.length; z++) {
                var as = R.assets[+ss[z].getAttribute('data-rp-st')];
                if (as) { var t = describeAsset(as.name); if (ss[z].textContent !== t) ss[z].textContent = t; }
            }
            var ht = head ? head.querySelector('.sip-rp-at') : null;
            if (ht) ht.textContent = fmtDay(R.T);
            return;
        }
        _panelSig = sig;
        if (head) head.innerHTML = 'All assets <span class="sip-rp-status">' + R.assets.length + ' -- values at</span> <span class="sip-rp-at">' + esc(fmtDay(R.T)) + '</span>';
        if (big) big.innerHTML = '';
        var idx = R.assets.map(function (x, i) { return i; }).sort(function (x, y) {
            return R.assets[x].name.localeCompare(R.assets[y].name, undefined, { numeric: true });
        });
        var h = '<div class="sip-rp-allgrid">';
        for (var k = 0; k < idx.length; k++) {
            var ai = idx[k], a = R.assets[ai];
            h += '<div class="sip-rp-acard"><button type="button" class="sip-rp-ahead" data-rp-pick="' + esc(a.name) + '" title="Detail and graph">' +
                '<b>' + esc(a.name) + '</b><span data-rp-st="' + ai + '">' + esc(describeAsset(a.name)) + '</span><i>&#9654;</i></button>';
            if (!a.cols || !a.rows.length) {
                h += '<div class="sip-rp-empty" style="padding:6px 8px">' + (a.err ? esc(a.err) : a.done ? 'No history rows.' : 'Loading...') + '</div></div>';
                continue;
            }
            var order = [];
            for (var c = 0; c < a.cols.length; c++) if (!a.cols[c].skip && !a.cols[c].isDL) order.push(c);
            for (var c2 = 0; c2 < a.cols.length; c2++) if (!a.cols[c2].skip && a.cols[c2].isDL) order.push(c2);
            for (var o = 0; o < order.length; o++) {
                var ci = order[o];
                var sp = sparkFor(a, ci, 200, 16);
                h += '<div class="sip-rp-arow"><span class="n" title="' + esc(colLabel(a, ci)) + '">' + esc(colLabel(a, ci)) + '</span>' +
                    '<span class="v" data-rp-v="' + ai + ':' + ci + '">' + valueText(a, ci) + '</span>' +
                    '<svg viewBox="0 0 200 16" preserveAspectRatio="none" aria-hidden="true">' +
                    '<path class="' + (a.cols[ci].isDL ? 'rp-dl' : 'rp-trace') + '" d="' + sp.d + '" fill="none" stroke-width="1.1" vector-effect="non-scaling-stroke"/>' +
                    '<line class="rp-cur" x1="' + (cx * 200).toFixed(1) + '" x2="' + (cx * 200).toFixed(1) + '" y1="0" y2="16" stroke-width="1" vector-effect="non-scaling-stroke"/></svg></div>';
            }
            h += '</div>';
        }
        host.innerHTML = h + '</div>';
    }
    function markCard(name) {
        if (window.SipTelemetry && typeof window.SipTelemetry.highlight === 'function') window.SipTelemetry.highlight(name, true);
        var host = byId('sipRpAttrs');
        if (!host) return;
        var cards = host.querySelectorAll('.sip-rp-acard');
        for (var i = 0; i < cards.length; i++) {
            var b = cards[i].querySelector('[data-rp-pick]');
            var on = !!b && b.getAttribute('data-rp-pick') === name;
            cards[i].classList.toggle('follow', on);
            if (on && cards[i].scrollIntoView) cards[i].scrollIntoView({ block: 'nearest' });
        }
    }
    function renderPanel(force) {
        var host = byId('sipRpAttrs');
        var big = byId('sipRpBig');
        var head = byId('sipRpSelHead');
        if (!host) return;
        var a = R.selected;
        if (!R.active) {
            if (force) {
                if (head) head.textContent = 'Asset values';
                if (big) big.innerHTML = '';
                host.innerHTML = '<div class="sip-rp-empty">Load a replay window -- every asset of the yard and its readings will show here, moving with the playback.</div>';
            }
            return;
        }
        if (!a) { renderAllAssets(force, host, big, head); return; }
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
        if (head) head.innerHTML = '<button type="button" class="sip-rp-back" data-rp-all="1" title="Back to all assets">&#9664; All assets</button> ' +
            esc(a.name) + (state ? ' <span class="sip-rp-status">' + esc(state) + '</span>' : '') +
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
                '<span class="n" title="' + esc(colLabel(a, ci)) + '">' + esc(colLabel(a, ci)) + '</span>' +
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

    /* v618.18 -- the yard INSPECTOR panel (top-right of the SIP) during a
       replay: selected asset, every value AT THE CURSOR timestamp, a small
       trend per reading with the cursor line, and a larger graph of the
       chosen reading. Rebuilt by sip-telemetry every ~300 ms. */
    function inspectorHtml() {
        var a = R.selected;
        if (!R.active) return '<div class="sip-insp-empty">Load a replay window, then click an asset in the yard.</div>';
        if (!a) return allAssetsHtml();
        var state = describeAsset(a.name);
        var lo = R.start, hi = R.end, span = (hi - lo) || 1;   /* same x scale as sparkFor */
        var cx = Math.max(0, Math.min(1, (R.T - lo) / span));
        var h = '<div class="sip-insp-tools"><button type="button" data-insp-all="1">&#9664; All assets</button></div>' +
            '<div class="sip-insp-head"><b>' + esc(a.name) + '</b>' + (state ? ' <span class="sip-insp-st">' + esc(state) + '</span>' : '') +
            '<span class="sip-insp-time">' + esc(fmtDay(R.T)) + '</span></div>';
        if (!a.cols || !a.rows.length) return h + '<div class="sip-insp-empty">' + (a.done ? 'No history rows in this window.' : 'Loading history...') + '</div>';
        var order = [];
        for (var c = 0; c < a.cols.length; c++) if (!a.cols[c].skip && !a.cols[c].isDL) order.push(c);
        for (var c2 = 0; c2 < a.cols.length; c2++) if (!a.cols[c2].skip && a.cols[c2].isDL) order.push(c2);
        if (R.selectedCol === null && order.length) R.selectedCol = order[0];
        h += '<div class="sip-insp-rows">';
        for (var i = 0; i < order.length; i++) {
            var ci = order[i];
            var sp = sparkFor(a, ci, 200, 22);
            h += '<div class="sip-insp-row' + (ci === R.selectedCol ? ' on' : '') + '" data-insp-col="' + ci + '">' +
                '<span class="n" title="' + esc(colLabel(a, ci)) + '">' + esc(colLabel(a, ci)) + '</span>' +
                '<span class="v">' + valueText(a, ci) + '</span>' +
                '<svg viewBox="0 0 200 22" preserveAspectRatio="none" aria-hidden="true">' +
                '<path class="' + (a.cols[ci].isDL ? 'ins-dl' : 'ins-tr') + '" d="' + sp.d + '" fill="none" stroke-width="1.2" vector-effect="non-scaling-stroke"/>' +
                '<line class="ins-cur" x1="' + (cx * 200).toFixed(1) + '" x2="' + (cx * 200).toFixed(1) + '" y1="0" y2="22" stroke-width="1" vector-effect="non-scaling-stroke"/></svg></div>';
        }
        h += '</div>';
        if (R.selectedCol !== null && a.cols[R.selectedCol]) {
            var col = a.cols[R.selectedCol];
            var bsp = sparkFor(a, R.selectedCol, 400, 90);
            var f = function (v) { return v === null ? '' : (Math.abs(v) >= 100 ? v.toFixed(0) : v.toFixed(2)); };
            h += '<div class="sip-insp-graph"><div class="sip-insp-gh"><b>' + esc(col.name) + '</b><span>' + valueText(a, R.selectedCol) + '</span></div>' +
                '<svg viewBox="0 0 400 90" preserveAspectRatio="none" role="img" aria-label="' + esc(col.name) + ' over the replay">' +
                '<path class="' + (col.isDL ? 'ins-dl' : 'ins-tr') + '" d="' + bsp.d + '" fill="none" stroke-width="1.6" vector-effect="non-scaling-stroke"/>' +
                '<line class="ins-cur" x1="' + (cx * 400).toFixed(1) + '" x2="' + (cx * 400).toFixed(1) + '" y1="0" y2="90" stroke-width="1.5" vector-effect="non-scaling-stroke"/></svg>' +
                '<div class="sip-insp-gf"><span>min ' + f(bsp.lo) + '</span><span>' + fmtClock(lo) + ' to ' + fmtClock(hi) + '</span><span>max ' + f(bsp.hi) + '</span></div></div>';
        }
        return h;
    }
    /* v618.20: default replay view -- EVERY asset of the replay with all its
       readings at the cursor timestamp and a small history line each
       (cursor marked). Click an asset name for its detail + big graph. */
    function allAssetsHtml() {
        var span = (R.end - R.start) || 1;
        var cx = Math.max(0, Math.min(1, (R.T - R.start) / span));
        var list = R.assets.slice().sort(function (x, y) { return x.name.localeCompare(y.name, undefined, { numeric: true }); });
        var h = '<div class="sip-insp-head"><b>All assets</b><span class="sip-insp-st">' + list.length + '</span>' +
            '<span class="sip-insp-time">' + esc(fmtDay(R.T)) + '</span></div>';
        for (var i = 0; i < list.length; i++) {
            var a = list[i];
            var st = describeAsset(a.name);
            h += '<div class="sip-insp-asset"><button type="button" class="sip-insp-ah" data-insp-asset="' + esc(a.name) + '" title="Show detail and graph">' +
                '<b>' + esc(a.name) + '</b>' + (st ? '<span>' + esc(st) + '</span>' : '') + '<i>&#9654;</i></button>';
            if (!a.cols || !a.rows.length) {
                h += '<div class="sip-insp-empty">' + (a.done ? 'No history rows.' : 'Loading...') + '</div></div>';
                continue;
            }
            var order = [];
            for (var c = 0; c < a.cols.length; c++) if (!a.cols[c].skip && !a.cols[c].isDL) order.push(c);
            for (var c2 = 0; c2 < a.cols.length; c2++) if (!a.cols[c2].skip && a.cols[c2].isDL) order.push(c2);
            for (var k = 0; k < order.length; k++) {
                var ci = order[k];
                var sp = sparkFor(a, ci, 200, 18);
                h += '<div class="sip-insp-row sm"><span class="n" title="' + esc(colLabel(a, ci)) + '">' + esc(colLabel(a, ci)) + '</span>' +
                    '<span class="v">' + valueText(a, ci) + '</span>' +
                    '<svg viewBox="0 0 200 18" preserveAspectRatio="none" aria-hidden="true">' +
                    '<path class="' + (a.cols[ci].isDL ? 'ins-dl' : 'ins-tr') + '" d="' + sp.d + '" fill="none" stroke-width="1.1" vector-effect="non-scaling-stroke"/>' +
                    '<line class="ins-cur" x1="' + (cx * 200).toFixed(1) + '" x2="' + (cx * 200).toFixed(1) + '" y1="0" y2="18" stroke-width="1" vector-effect="non-scaling-stroke"/></svg></div>';
            }
            h += '</div>';
        }
        return h;
    }
    /* DataLogger columns named by role id (e.g. 20285) shown by relay name */
    function colLabel(a, ci) {
        var c = a.cols[ci];
        if (c && c.isDL) {
            var dn = c.dlName || (c.dlName = dlNameFor(a, c));
            if (dn && dn.name) return dn.name;
        }
        return c ? c.name : '';
    }
    function inspectorSelect(name) {
        if (name) selectAsset(name);
        else {
            R.selected = null;
            R.selectedCol = null;
            if (window.SipTelemetry && typeof window.SipTelemetry.highlight === 'function') window.SipTelemetry.highlight('');
            renderPanel(true);
        }
    }
    function inspectorPick(ci) {
        R.selectedCol = ci;
        renderPanel(true);
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
        renderAlerts();
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
        inspectorHtml: inspectorHtml,
        inspectorPick: inspectorPick,
        inspectorSelect: inspectorSelect,
        isActive: function () { return !!R.active; },
        open: open,
        close: close,
        /* v618.30: replay a window and select an asset (asset drawer -> Replay) */
        replayAt: function (fromMs, toMs, assetName) {
            open();
            var f = byId('sipRpFrom'), t = byId('sipRpTo');
            if (f) f.value = toLocalInput(fromMs);
            if (t) t.value = toLocalInput(toMs);
            loadWindow();
            if (!assetName) return;
            var tries = 0;
            var iv = setInterval(function () {
                tries++;
                var hit = R.assets && R.assets.some(function (a) { return a.name === assetName && a.rows && a.rows.length; });
                if (hit) { clearInterval(iv); selectAsset(assetName); }
                else if (tries > 40) clearInterval(iv);
            }, 500);
        },
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
        /* v618.9: per-asset data check -- SipReplay.diag() in the console */
        diag: function () {
            var out = R.assets.map(function (a) {
                var dl = (a.cols || []).filter(function (c) { return c.isDL; });
                return {
                    asset: a.name, id: a.id, type: a.typeId, source: a.url.split('/').pop(),
                    seedAt: a.seedT ? new Date(a.seedT).toLocaleString() : '-',
                    rows: a.rows.length, cols: a.cols ? a.cols.length : 0,
                    dlCols: dl.map(function (c) { var n = dlNameFor(a, c); return c.name + '->' + (n.name || '?'); }).join(' '),
                    valueAtCursor: a.ptr >= 0 ? 'yes' : 'NO', done: a.done, error: a.err || ''
                };
            });
            if (window.console && console.table) console.table(out);
            return out;
        },
        _r: R
    };
})();
