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
 *  v618.42 DOCK: Events (Alerts + Relays + Analog merged, scope Selected /
 *  Whole yard) | Graphs of the SELECTED assets only -- type tab -> asset ->
 *  attribute, grouped by Asset (default) or Attribute, Grow / Follow / Full,
 *  value flags at the line ends; selection glows on the yard in its colour
 *  (SipTelemetry.replay.setMarks). The all-assets card grid is gone.
 *
 *  v618.44: stable dock -- markup is morphed into the live nodes (no rebuild per
 *  play tick), toolbar frozen while its dropdown is open, events list frozen
 *  under the pointer, graph seek on pointer-down with drag-scrub, slider not
 *  fought by playback while held.
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
        else { renderClock(); renderPanel(true); renderEvents(true); }
    }
    function seek(t) {
        R.T = clamp(t, vlo(), vhi());
        applyAt(R.T, false);
        if (!covered(R.T)) { R.buffering = true; pump(); }
        renderClock();
        renderPanel(true);
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
            G.marksKey = '';
            applyMarks();
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
                    /* v618.42: follow = add the alert's asset to the graphs */
                    if (al.asset) addSel(al.asset);
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
        if (typeof RT.setMarks === 'function') RT.setMarks({});
        G.marksKey = '';
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
        /* v618.43: solid dock background (the yard's canvas colour) so nothing behind shows through */
        '.sip-card.sip-replaying .sip-rp{position:relative;z-index:2;background:var(--rp-bg,#0E1828);padding:8px 8px 10px;}' +
        /* v618.43: fullscreen -- the yard shrinks to make room for the dock instead of pushing it off the card */
        '#sipCardSection.fullscreen.sip-replaying{display:flex !important;flex-direction:column !important;overflow:hidden !important;}' +
        '#sipCardSection.fullscreen.sip-replaying > *{flex:none;}' +
        '#sipCardSection.fullscreen.sip-replaying #sipCanvas{flex:1 1 0 !important;height:auto !important;min-height:140px !important;max-height:none !important;}' +
        '#sipCardSection.fullscreen.sip-replaying #sipCanvas svg{height:100% !important;min-height:0 !important;}' +
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
        '.sip-rp-ticks b.m{opacity:1;width:2px;height:12px;top:-2px;}' +
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
        '.sip-rp-dock.sip-rp-dock2{grid-template-columns:minmax(250px,330px) minmax(0,1fr);}' +
        '.sip-card.sip-replaying.sip-rp-compact .sip-rp{max-height:72vh;}' +
        '.rp-g-head{border-bottom:1px solid var(--rp-rule);flex:none;}' +
        '.rp-g-bar{display:flex;flex-wrap:wrap;align-items:center;gap:6px 8px;padding:6px 10px;}' +
        '.rp-g-attrs{border-top:1px dashed var(--rp-rule);}' +
        '.rp-chips{display:flex;flex-wrap:wrap;gap:4px;align-items:center;}' +
        '.rp-chip{display:inline-flex;align-items:center;gap:5px;border:1px solid var(--rp-rule);background:transparent;color:var(--rp-text);border-radius:14px;padding:2px 9px;font:inherit;font-size:12px;cursor:pointer;}' +
        '.rp-chip.sm{font-size:11.5px;padding:1px 8px;}' +
        '.rp-chip.on{border-color:var(--rp-accent);background:var(--rp-sel);font-weight:600;}' +
        '.rp-chip.dl{border-style:dashed;}' +
        '.rp-chip i{width:8px;height:8px;border-radius:50%;display:inline-block;}' +
        '.rp-chip button{border:none;background:none;color:var(--rp-muted);cursor:pointer;font-size:14px;line-height:1;padding:0 0 0 2px;}' +
        '.rp-chip.al{border-color:var(--rp-alert);color:var(--rp-alert);margin-top:6px;text-align:left;}' +
        '.sip-rp select.rp-add{border:1px dashed var(--rp-accent);color:var(--rp-accent);border-radius:14px;padding:2px 6px;font-size:12px;max-width:150px;}' +
        '.rp-link{border:none;background:none;color:var(--rp-accent);font:inherit;font-size:12px;cursor:pointer;padding:2px 4px;}' +
        '.rp-link:hover{text-decoration:underline;}' +
        '.rp-sp{flex:1 1 auto;}' +
        '.rp-lbl{color:var(--rp-muted);font-size:11px;text-transform:uppercase;letter-spacing:.04em;}' +
        '.rp-muted{color:var(--rp-muted);font-size:12px;}' +
        '.rp-pad{padding:4px 0 6px;}' +
        '.sip-rp .sip-seg button small{font-size:10.5px;opacity:.8;margin-left:3px;}' +
        '.rp-g-bar .sip-seg button,.rp-ev-h .sip-seg button,.rp-ev-scope .sip-seg button{height:24px;min-width:0;padding:0 8px;font-size:12px;}' +
        '.rp-graphs{padding:4px 12px 10px;}' +
        '.rp-ablk{border-left:4px solid;border-radius:4px;padding:2px 0 6px 10px;margin:4px 0 10px;}' +
        '.rp-ab-h{display:flex;flex-wrap:wrap;align-items:center;gap:6px;padding:3px 0;}' +
        '.rp-ab-h b{font-size:13.5px;}' +
        '.rp-tag{font-size:10.5px;border-radius:3px;padding:1px 6px;background:var(--rp-hover);color:var(--rp-muted);border:1px solid var(--rp-rule);}' +
        '.rp-tag.bad{color:var(--rp-alert);border-color:var(--rp-alert);}' +
        '.rp-arow{display:grid;grid-template-columns:150px minmax(0,1fr);align-items:center;border-top:1px dashed var(--rp-rule);}' +
        '.rp-al{font-size:12px;color:var(--rp-muted);padding-right:6px;overflow:hidden;}' +
        '.rp-al span{display:block;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}' +
        '.rp-al b{color:var(--rp-text);}' +
        '.rp-lane{border-bottom:1px dashed var(--rp-rule);padding:4px 0;}' +
        '.rp-lane-h{display:flex;flex-wrap:wrap;gap:10px;font-size:12px;align-items:baseline;color:var(--rp-muted);}' +
        '.rp-lane-h b{color:var(--rp-text);}' +
        '.rp-lane-h i{display:inline-block;width:9px;height:9px;margin-right:4px;border-radius:2px;}' +
        '.rp-gsvg{display:block;overflow:visible;cursor:crosshair;}' +
        '.rp-gsvg .rp-grid{stroke:var(--rp-rule);stroke-width:1;}' +
        '.rp-gsvg .rp-ax{fill:var(--rp-muted);font:10px system-ui,sans-serif;}' +
        '.rp-gsvg .rp-future{fill:var(--rp-hover);}' +
        '.rp-gsvg .rp-amark{fill:var(--rp-alert);opacity:.45;}' +
        '.sip-rp .rp-gsvg .rp-cur{stroke:var(--rp-text);stroke-width:1;opacity:.7;}' +
        '.rp-gsvg .rp-flag{fill:#fff;font:600 10px ui-monospace,Consolas,monospace;}' +
        '.rp-ev-h{padding:5px 8px !important;}' +
        '.rp-ev-scope{display:flex;align-items:center;gap:8px;padding:5px 8px;border-bottom:1px solid var(--rp-rule);}' +
        '.sip-rp-list li.past{opacity:.6;}' +
        '.sip-rp-list li.k-alert.f{box-shadow:inset 3px 0 0 var(--rp-alert);}' +
        '.sip-rp-list li.k-alert span{color:var(--rp-alert);}' +
        '.rp-dot{display:inline-block;width:8px;height:8px;border-radius:2px;margin-right:5px;vertical-align:middle;}' +
        '@media (max-width:1100px){.sip-rp-dock.sip-rp-dock2{grid-template-columns:1fr;}.sip-rp-dock2 > .sip-rp-col:first-child{max-height:220px;}}' +
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
            '  <button type="button" class="sip-rp-btn" id="sipRpCompact" title="Smaller yard, more room for graphs">Compact yard</button>' +
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
            '<div class="sip-rp-dock sip-rp-dock2">' +
            '  <div class="sip-rp-col"><h4 class="rp-ev-h"><div class="sip-seg" id="sipRpEvTabs" role="group" aria-label="Event type">' +
            [['all', 'All'], ['alert', 'Alerts'], ['relay', 'Relays'], ['analog', 'Analog']].map(function (t) {
                return '<button type="button" data-rp-evtab="' + t[0] + '" aria-pressed="' + (t[0] === 'all') + '">' + t[1] + '<small>0</small></button>';
            }).join('') + '</div></h4>' +
            '    <div class="rp-ev-scope"><div class="sip-seg" id="sipRpEvScope" role="group" aria-label="Event scope">' +
            '      <button type="button" data-rp-evscope="sel" aria-pressed="true">Selected assets</button>' +
            '      <button type="button" data-rp-evscope="yard" aria-pressed="false">Whole yard</button></div>' +
            '      <span class="sip-rp-status" id="sipRpEvHint"></span></div>' +
            '    <ul class="sip-rp-list" id="sipRpEvents"></ul>' +
            '    <div class="sip-rp-opts">' +
            '      <label>Before <select id="sipRpPre"><option value="60000">1 min</option><option value="120000" selected>2 min</option><option value="300000">5 min</option><option value="600000">10 min</option></select></label>' +
            '      <label>After <select id="sipRpPost"><option value="30000">30 s</option><option value="60000" selected>1 min</option><option value="180000">3 min</option><option value="300000">5 min</option></select></label>' +
            '      <label><input type="checkbox" id="sipRpLoop"> Loop</label>' +
            '      <label title="When playback reaches an alert, add its asset to the graphs"><input type="checkbox" id="sipRpFollow" checked> Follow alerts</label>' +
            '    </div></div>' +
            '  <div class="sip-rp-col"><div class="rp-g-head" id="sipRpSelHead"></div><div class="sip-rp-attrs rp-graphs" id="sipRpAttrs"></div></div>' +
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
                if (nm && G.evScope === 'yard' && !isSel(nm)) addSel(nm);
                seek(+li.getAttribute('data-rp-t'));
                return;
            }
            var d = function (sel) { return e.target.closest(sel); };
            var x;
            if ((x = d('[data-rp-evtab]'))) { G.evTab = x.getAttribute('data-rp-evtab'); renderEvents(true); return; }
            if ((x = d('[data-rp-evscope]'))) { G.evScope = x.getAttribute('data-rp-evscope'); G.evSig = ''; renderEvents(true); return; }
            if ((x = d('[data-rp-unsel]'))) { removeSel(x.getAttribute('data-rp-unsel')); return; }
            if ((x = d('[data-rp-gtype]'))) { G.type = x.getAttribute('data-rp-gtype'); renderPanel(true); return; }
            if ((x = d('[data-rp-neigh]'))) { addNeighbours(); return; }
            if ((x = d('[data-rp-clear]'))) { clearSel(); return; }
            if ((x = d('[data-rp-group]'))) { G.group = x.getAttribute('data-rp-group'); saveDock(); renderPanel(true); return; }
            if ((x = d('[data-rp-view]'))) { G.view = x.getAttribute('data-rp-view'); saveDock(); renderPanel(true); return; }
            if ((x = d('[data-rp-win]'))) { G.win = (+x.getAttribute('data-rp-win') || 15) * 60000; saveDock(); renderPanel(true); return; }
            if ((x = d('[data-rp-coll]'))) { var cn = x.getAttribute('data-rp-coll'); G.collapsed[cn] = !G.collapsed[cn]; renderPanel(true); return; }
            if ((x = d('[data-rp-attr]'))) {
                var m = attrSel(G.type), lb = x.getAttribute('data-rp-attr');
                if (m[lb]) delete m[lb]; else m[lb] = 1;
                G.evSig = ''; saveDock(); renderPanel(true); return;
            }
            if ((x = d('[data-rp-preset2]'))) {
                var pv = x.getAttribute('data-rp-preset2'), list = attrList(G.type), nmap = {};
                for (var pi = 0; pi < list.length; pi++) {
                    if (pv === 'all') nmap[list[pi].label] = 1;
                    else if (pv !== 'none' && (list[pi].dl || PRESETS[G.type][+pv][1].test(list[pi].label))) nmap[list[pi].label] = 1;
                }
                G.attrs[G.type] = nmap; G.attrInit[G.type] = true;
                G.evSig = ''; saveDock(); renderPanel(true); return;
            }
            if ((x = d('[data-rp-start]'))) {
                var sa = R.alerts[+x.getAttribute('data-rp-start')];
                if (!sa) return;
                addSel(sa.asset, true);
                var nb = neighboursOf((assetByName(sa.asset) || {}).name || sa.asset, 3);
                for (var ni = 0; ni < nb.length; ni++) addSel(nb[ni], true);
                selChanged();
                seek(Math.max(vlo(), sa.t - R.pre));
                return;
            }
            /* graph seek: pointer-down handler below */
        });
        box.addEventListener('change', function (e) {
            var s = e.target.closest('select[data-rp-add]');
            if (s && s.value) {
                var v = s.value;
                s.blur();                   /* release the toolbar, then add */
                addSel(v);
            }
        });
        /* v618.44: toolbar catches up when its dropdown closes without a pick */
        box.addEventListener('focusout', function (e) {
            if (e.target && e.target.matches && e.target.matches('select[data-rp-add]')) setTimeout(function () { if (_headPending) renderHead(); }, 0);
        });
        var evUl = byId('sipRpEvents');
        if (evUl) {
            evUl.addEventListener('mouseenter', function () { G.evHover = true; });
            evUl.addEventListener('mouseleave', function () { G.evHover = false; if (G.evPending) renderEvents(true); });
        }
        /* v618.44: graphs seek on pointer-down and scrub while dragged (the
           svg may be re-drawn under the pointer during play -- a click could
           otherwise be lost) */
        var scrub = null;
        function scrubTo(clientX) {
            if (!scrub || !R.active) return;
            var px = clientX - scrub.left - scrub.pad;
            seek(scrub.lo + clamp(px / scrub.iw, 0, 1) * scrub.span);
        }
        box.addEventListener('pointerdown', function (e) {
            if (e.button !== 0) return;
            var sv = e.target.closest && e.target.closest('svg[data-rp-seek]');
            if (!sv || !R.active) return;
            var rr = sv.getBoundingClientRect();
            scrub = { left: rr.left, pad: +sv.getAttribute('data-pad'), iw: +sv.getAttribute('data-iw'), lo: +sv.getAttribute('data-lo'), span: +sv.getAttribute('data-span') };
            e.preventDefault();
            scrubTo(e.clientX);
        });
        window.addEventListener('pointermove', function (e) { if (scrub) scrubTo(e.clientX); });
        window.addEventListener('pointerup', function () { scrub = null; });
        /* v618.44: the time slider is not pushed by playback while it is held */
        var sk = byId('sipRpSeek');
        if (sk) {
            sk.addEventListener('pointerdown', function () { R.dragSeek = true; });
            window.addEventListener('pointerup', function () { R.dragSeek = false; });
        }
        var rsT = null;
        window.addEventListener('resize', function () {
            if (!R.open) return;
            clearTimeout(rsT);
            rsT = setTimeout(function () { renderPanel(true); }, 150);
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
        byId('sipRpCompact').addEventListener('click', function () {
            var card = byId('sipCardSection');
            if (!card) return;
            var on = card.classList.toggle('sip-rp-compact');
            this.textContent = on ? 'Full yard' : 'Compact yard';
            setTimeout(function () { renderPanel(true); }, 80);
        });
        return true;
    }

    /* v618.3: light / dark from the real page background around the card.
       v618.43: follow the yard palette (Night / Control room / Day) first, so
       a dark yard on a light page gets a dark dock; page background is the
       fallback. The dock is filled with the yard's canvas colour. */
    function syncRpTheme() {
        var box = byId('sipReplay');
        if (!box) return;
        var SIPx = window.SIP, light;
        var tn = (SIPx && typeof SIPx.themeName === 'function') ? SIPx.themeName() : '';
        if (tn === 'day') light = true;
        else if (tn === 'night' || tn === 'vdu') light = false;
        else light = (window.SipTelemetry && typeof window.SipTelemetry.pageIsLight === 'function')
            ? window.SipTelemetry.pageIsLight() : false;
        box.classList.toggle('rp-light', !!light);
        var bg = (SIPx && SIPx._theme && SIPx._theme.canvas) ? SIPx._theme.canvas : (light ? '#FAFBFD' : '#0E1828');
        box.style.setProperty('--rp-bg', bg);
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
            if (ev[i].t < lo || ev[i].t > hi || isSel(ev[i].asset)) continue;
            html += '<b class="' + (ev[i].kind === 'state' ? 's' : '') + '" style="left:' +
                (((ev[i].t - lo) / span) * 100).toFixed(2) + '%"></b>';
        }
        /* v618.42: relay / state changes of the selected assets, in their colour */
        for (var si = 0; si < ev.length; si++) {
            if (ev[si].t < lo || ev[si].t > hi || !isSel(ev[si].asset)) continue;
            html += '<b class="m" style="left:' + (((ev[si].t - lo) / span) * 100).toFixed(2) + '%;background:' + colorOf(ev[si].asset) + '"></b>';
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
        if (s && R.active && !R.dragSeek) s.value = Math.round(((R.T - lo) / sp) * 1000);
        var buf = byId('sipRpBuf');
        if (buf && R.active) buf.style.width = clamp(((bufferedUntil() - lo) / sp) * 100, 0, 100).toFixed(2) + '%';
        if (R.buffering) updateStatus();
    }

    /* ======================================================================
       v618.42 REPLAY DOCK -- Events (merged) | Graphs (grouped by asset)
       ----------------------------------------------------------------------
       Replay still loads EVERY SIP asset (the yard needs them), but the dock
       only draws what the user picks:
         yard click / "+ Add" / alert  ->  selection (colour per asset; the
         same colour glows on the yard and draws the graph line)
         type tab -> attribute chips (per type: Track / Signal / Point / Other)
         Group: Asset (one block per asset, its attributes stacked) or
                Attribute (one lane per attribute, selected assets overlaid)
         Graph: Grow (drawn up to the cursor) / Follow (sliding window) / Full
         value flag at the end of every growing line
       Events: Alerts + Relays (+ yard state) + Analog changes in one list,
       scope Selected assets / Whole yard, click = jump (alert = replay around).
       ====================================================================== */
    var PALETTE = ['#0EA5B7', '#E0457B', '#E89B0C', '#6C7CF0', '#2FB35A', '#A066E0', '#E5533D', '#7D8B9C'];
    var TYPES = [['track', 'Track'], ['signal', 'Signal'], ['point', 'Point'], ['other', 'Other']];
    var TYPE_NAME = { track: 'Track', signal: 'Signal', point: 'Point', other: 'Other' };
    var PREFER = {
        track: /(^|[^A-Z])(VR|IR)([^A-Z]|$)|RELAY\s*END|VTC\s*TR\b|\bTR\s*\(?V/i,
        signal: /ECR|LAMP|\bMA\b|CURR/i,
        point: /CURR|OPER|VOLT/i,
        other: /./
    };
    var PRESETS = {
        track: [['Relay end', /RELAY|(^|[^A-Z])(VR|IR)([^A-Z]|$)|\bTR\b|TPR/i], ['Feed end', /FEED|(^|[^A-Z])(VF|IF)([^A-Z]|$)|CHOKE|CH\s*FEED/i], ['Charger', /CHARG|TFC|BATT/i]],
        signal: [['Lamp currents', /ECR|LAMP|MA\b|CURR/i]],
        point: [['Operation', /CURR|OPER|VOLT|\bI\b|\bV\b/i]],
        other: []
    };
    var G = {
        sel: [], color: {}, nextColor: 0, type: 'track', attrs: {}, attrInit: {},
        group: 'asset', view: 'grow', win: 15 * 60000, collapsed: {},
        evTab: 'all', evScope: 'sel', lastGraph: 0, graphTimer: null, evSig: '', marksKey: ''
    };
    try {
        var saved = JSON.parse(window.localStorage.getItem('sipRpDock') || '{}');
        if (saved.group) G.group = saved.group;
        if (saved.view) G.view = saved.view;
        if (saved.win) G.win = saved.win;
    } catch (e) { /* storage blocked */ }
    function saveDock() {
        try { window.localStorage.setItem('sipRpDock', JSON.stringify({ group: G.group, view: G.view, win: G.win })); } catch (e) { /* ignore */ }
    }

    function assetByName(name) {
        if (!name) return null;
        var a = R.byName[name];
        if (a) return a;
        var nk = normKey(name);
        for (var k in R.byName) if (R.byName.hasOwnProperty(k) && normKey(k) === nk) return R.byName[k];
        return null;
    }
    function kindOf(a) {
        if (a.kind) return a.kind;
        var cells = RT.findCells(a.name), k = 'other';
        for (var i = 0; i < cells.length; i++) {
            var t = String(cells[i].type || '');
            if (/PointMachine/.test(t)) { k = 'point'; break; }
            if (/Track/.test(t)) k = 'track';
            else if (k === 'other' && /Signal|Shunt|Shaunt/.test(t)) k = 'signal';
        }
        if (k === 'other' && a.isPM) k = 'point';
        a.kind = k;
        return k;
    }
    function colorOf(name) { return G.color[name] || '#7D8B9C'; }
    function isSel(name) { return G.sel.indexOf(name) !== -1; }
    function selAssets(kind) {
        var out = [];
        for (var i = 0; i < G.sel.length; i++) {
            var a = assetByName(G.sel[i]);
            if (a && (!kind || kindOf(a) === kind)) out.push(a);
        }
        return out;
    }
    function addSel(name, noRender) {
        var a = assetByName(name);
        var nm = a ? a.name : name;
        if (!nm || isSel(nm)) return;
        G.sel.push(nm);
        if (!G.color[nm]) { G.color[nm] = PALETTE[G.nextColor % PALETTE.length]; G.nextColor++; }
        if (a) G.type = kindOf(a);
        if (!noRender) selChanged();
    }
    function removeSel(name) {
        var i = G.sel.indexOf(name);
        if (i === -1) return;
        G.sel.splice(i, 1);
        delete G.color[name];
        selChanged();
    }
    function clearSel() { G.sel = []; G.color = {}; G.nextColor = 0; selChanged(); }
    function selChanged() {
        G.evSig = '';
        applyMarks();
        drawTimelineMarks();
        renderPanel(true);
    }
    function applyMarks() {
        if (!RT || typeof RT.setMarks !== 'function') return;
        var m = {};
        if (R.active) for (var i = 0; i < G.sel.length; i++) m[G.sel[i]] = colorOf(G.sel[i]);
        var key = JSON.stringify(m);
        if (key === G.marksKey) return;
        G.marksKey = key;
        RT.setMarks(m);
    }
    /* yard neighbours: assets whose cells lie within ~45 px of the asset's cells */
    function bboxes(name) {
        var cs = RT.findCells(name), out = [];
        for (var i = 0; i < cs.length; i++) {
            var c = cs[i];
            if (!c || !c.position) continue;
            var w = (c.size && c.size.width) || 40, h = (c.size && c.size.height) || 20;
            out.push({ x0: c.position.x, y0: c.position.y, x1: c.position.x + w, y1: c.position.y + h });
        }
        return out;
    }
    function gap(A, B) {
        var best = Infinity;
        for (var i = 0; i < A.length; i++) for (var j = 0; j < B.length; j++) {
            var dx = Math.max(0, Math.max(A[i].x0, B[j].x0) - Math.min(A[i].x1, B[j].x1));
            var dy = Math.max(0, Math.max(A[i].y0, B[j].y0) - Math.min(A[i].y1, B[j].y1));
            var d = Math.sqrt(dx * dx + dy * dy);
            if (d < best) best = d;
        }
        return best;
    }
    function neighboursOf(name, max) {
        var A = bboxes(name);
        if (!A.length) return [];
        var list = [];
        for (var i = 0; i < R.assets.length; i++) {
            var o = R.assets[i];
            if (o.name === name) continue;
            var d = gap(A, bboxes(o.name));
            if (d <= 45) list.push({ n: o.name, d: d });
        }
        list.sort(function (x, y) { return x.d - y.d; });
        return list.slice(0, max || 4).map(function (x) { return x.n; });
    }
    function addNeighbours() {
        var base = G.sel.slice();
        for (var i = 0; i < base.length; i++) {
            var nb = neighboursOf(base[i], 4);
            for (var j = 0; j < nb.length; j++) addSel(nb[j], true);
        }
        selChanged();
    }

    /* ---- attributes ------------------------------------------------------ */
    function colOrder(a) {
        var order = [];
        if (!a.cols) return order;
        for (var c = 0; c < a.cols.length; c++) if (!a.cols[c].skip && !a.cols[c].isDL) order.push(c);
        for (var c2 = 0; c2 < a.cols.length; c2++) if (!a.cols[c2].skip && a.cols[c2].isDL) order.push(c2);
        return order;
    }
    function attrList(kind) {
        var seen = {}, out = [];
        for (var i = 0; i < R.assets.length; i++) {
            var a = R.assets[i];
            if (!a.cols || kindOf(a) !== kind) continue;
            var o = colOrder(a);
            for (var k = 0; k < o.length; k++) {
                var lb = colLabel(a, o[k]);
                if (!lb || seen[lb]) continue;
                seen[lb] = 1;
                out.push({ label: lb, dl: !!a.cols[o[k]].isDL });
            }
        }
        out.sort(function (x, y) { return (x.dl ? 1 : 0) - (y.dl ? 1 : 0); });
        return out;
    }
    function attrSel(kind) {
        if (!G.attrs[kind]) G.attrs[kind] = {};
        if (!G.attrInit[kind]) {
            var list = attrList(kind);
            if (list.length) {
                var m = {}, n = 0;
                for (var i = 0; i < list.length; i++) if (!list[i].dl && PREFER[kind].test(list[i].label) && n < 3) { m[list[i].label] = 1; n++; }
                for (var j = 0; j < list.length && n === 0; j++) if (!list[j].dl) { m[list[j].label] = 1; if (++n >= 3) break; }
                for (var k = 0; k < list.length; k++) if (list[k].dl) m[list[k].label] = 1;
                G.attrs[kind] = m;
                G.attrInit[kind] = true;
            }
        }
        return G.attrs[kind];
    }
    function colsFor(a) {
        var m = attrSel(kindOf(a)), o = colOrder(a), out = [], seen = {};
        for (var i = 0; i < o.length; i++) {
            var lb = colLabel(a, o[i]);
            if (m[lb] && !seen[lb]) { seen[lb] = 1; out.push(o[i]); }
        }
        return out;
    }
    function unitOf(lb) {
        var m = /\((m?A|V|Ohm|mV|%|Hz)\)\s*$/i.exec(lb) || /\s(mA|V|A|Ohm)\s*$/.exec(lb);
        return m ? m[1] : '';
    }

    /* ---- change list per column (only rows where the value changes) ------ */
    function changes(a, ci) {
        a.chg = a.chg || {};
        var ch = a.chg[ci];
        if (ch && ch.n === a.rows.length) return ch;
        ch = { n: a.rows.length, t: [], v: [], lo: Infinity, hi: -Infinity };
        var last = null, dl = a.cols[ci] && a.cols[ci].isDL;
        for (var r = 0; r < a.rows.length; r++) {
            var v = num(a.rows[r].v[ci]);
            if (v === null) continue;
            if (dl) v = v >= 0.5 ? 1 : 0;
            if (v !== last) { ch.t.push(a.rows[r].t); ch.v.push(v); last = v; }
            if (v < ch.lo) ch.lo = v;
            if (v > ch.hi) ch.hi = v;
        }
        a.chg[ci] = ch;
        return ch;
    }
    function idxAt(arr, t) {   /* last index with arr[i] <= t, -1 if none */
        var lo = 0, hi = arr.length - 1, ans = -1;
        while (lo <= hi) {
            var mid = (lo + hi) >> 1;
            if (arr[mid] <= t) { ans = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return ans;
    }
    function valAt(ch, t) { var i = idxAt(ch.t, t); return i < 0 ? null : ch.v[i]; }
    function fmtV(isDl, v) {
        if (v === null || v === undefined) return '-';
        if (isDl) return v ? '\u2191 Pickup' : '\u2193 Drop';
        return Math.abs(v) >= 100 ? v.toFixed(1) : v.toFixed(2);
    }
    function fmtVHtml(isDl, v) {
        if (v === null || v === undefined) return '-';
        if (isDl) return v ? '<span class="rp-pk">&uarr; Pickup</span>' : '<span class="rp-dr">&darr; Drop</span>';
        return fmtV(false, v);
    }

    /* ---- graph window ---------------------------------------------------- */
    function gWindow() {
        var lo = vlo(), hi = vhi(), upto = R.T;
        if (G.view === 'full') upto = hi;
        if (G.view === 'follow') {
            var w = Math.min(G.win, hi - lo);
            var end = Math.min(hi, Math.max(lo + w, R.T + w * 0.1));
            return { lo: end - w, hi: end, upto: R.T };
        }
        return { lo: lo, hi: hi, upto: upto };
    }
    var TICK_STEPS = [10000, 30000, 60000, 120000, 300000, 600000, 900000, 1800000, 3600000, 7200000, 10800000, 21600000];
    function hhmm(ms, sec) { var d = new Date(ms); return pad(d.getHours()) + ':' + pad(d.getMinutes()) + (sec ? ':' + pad(d.getSeconds()) : ''); }

    /* one step-line SVG. series: [{ch, color, name, dl}] */
    function stepSvg(series, W, H, win, opts) {
        opts = opts || {};
        var dl = series.length && series[0].dl;
        var small = H < 70;
        var pad = 40, top = small ? 8 : 5, bot = opts.noTime ? (small ? 7 : 4) : 15;
        var iw = Math.max(40, W - pad - 6), ih = Math.max(10, H - top - bot);
        var span = (win.hi - win.lo) || 1;
        var upto = Math.min(win.upto, win.hi);
        /* y range */
        var lo = Infinity, hi = -Infinity, s, i;
        if (dl) { lo = 0; hi = 1; }
        else {
            for (s = 0; s < series.length; s++) {
                var ch = series[s].ch;
                if (G.view === 'follow') {
                    var k0 = Math.max(0, idxAt(ch.t, win.lo));
                    for (i = k0; i < ch.t.length && ch.t[i] <= win.hi; i++) { if (ch.v[i] < lo) lo = ch.v[i]; if (ch.v[i] > hi) hi = ch.v[i]; }
                } else { if (ch.lo < lo) lo = ch.lo; if (ch.hi > hi) hi = ch.hi; }
            }
            if (lo === Infinity) { lo = 0; hi = 1; }
            if (hi - lo < 1e-6) { hi += 1; lo -= 1; }
            var m = (hi - lo) * 0.12, l0 = lo;
            lo -= m; hi += m;
            if (l0 >= 0 && lo < 0) lo = 0;
        }
        function X(t) { return pad + (t - win.lo) / span * iw; }
        function Y(v) { return top + (1 - (v - lo) / (hi - lo)) * ih; }
        var g = '';
        /* grid + labels */
        var qs = small ? [0, 1] : [0, 0.5, 1];
        for (i = 0; i < qs.length; i++) {
            var vv = lo + (hi - lo) * qs[i], yy = Y(vv);
            g += '<line class="rp-grid" x1="' + pad + '" x2="' + (pad + iw) + '" y1="' + yy.toFixed(1) + '" y2="' + yy.toFixed(1) + '"/>';
            var lab = dl ? (qs[i] === 0 ? 'Drop' : qs[i] === 1 ? 'Pick' : '') : (Math.abs(vv) >= 100 ? vv.toFixed(0) : vv.toFixed(1));
            g += '<text class="rp-ax" x="' + (pad - 4) + '" y="' + (yy + 3).toFixed(1) + '" text-anchor="end">' + lab + '</text>';
        }
        if (!opts.noTime) {
            var step = TICK_STEPS[TICK_STEPS.length - 1];
            for (i = 0; i < TICK_STEPS.length; i++) if (span / TICK_STEPS[i] <= 7) { step = TICK_STEPS[i]; break; }
            for (var tt = Math.ceil(win.lo / step) * step; tt <= win.hi; tt += step) {
                g += '<text class="rp-ax" x="' + X(tt).toFixed(1) + '" y="' + (H - 3) + '" text-anchor="middle">' + hhmm(tt, step < 60000) + '</text>';
            }
        }
        if (upto < win.hi) g += '<rect class="rp-future" x="' + X(Math.max(upto, win.lo)).toFixed(1) + '" y="' + top + '" width="' + Math.max(0, X(win.hi) - X(Math.max(upto, win.lo))).toFixed(1) + '" height="' + ih + '"/>';
        /* alert marks */
        if (opts.alerts) for (i = 0; i < opts.alerts.length; i++) {
            var at = opts.alerts[i];
            if (at < win.lo || at > win.hi || at > upto) continue;
            g += '<rect class="rp-amark" x="' + (X(at) - 1).toFixed(1) + '" y="0" width="3" height="' + (top + ih) + '"/>';
        }
        /* lines (pixel-bucketed step paths) */
        var flags = [];
        for (s = 0; s < series.length; s++) {
            var c = series[s].ch;
            if (!c.t.length || upto < win.lo) continue;
            var k = idxAt(c.t, win.lo), cur, x0;
            if (k < 0) { k = 0; if (c.t[0] > upto) continue; cur = c.v[0]; x0 = X(c.t[0]); }
            else { cur = c.v[k]; x0 = X(win.lo); }
            var d = 'M' + x0.toFixed(1) + ' ' + Y(cur).toFixed(1);
            var px = Math.round(x0), bmin = null, bmax = null;
            for (i = k + 1; i < c.t.length && c.t[i] <= upto && c.t[i] <= win.hi; i++) {
                var x = X(c.t[i]), rx = Math.round(x), v = c.v[i];
                if (rx === px) {
                    if (bmin === null) { bmin = Math.min(cur, v); bmax = Math.max(cur, v); }
                    else { if (v < bmin) bmin = v; if (v > bmax) bmax = v; }
                    cur = v;
                    continue;
                }
                if (bmin !== null) { d += 'V' + Y(bmin).toFixed(1) + 'V' + Y(bmax).toFixed(1) + 'V' + Y(cur).toFixed(1); bmin = bmax = null; }
                d += 'H' + x.toFixed(1) + 'V' + Y(v).toFixed(1);
                px = rx; cur = v;
            }
            if (bmin !== null) d += 'V' + Y(bmin).toFixed(1) + 'V' + Y(bmax).toFixed(1) + 'V' + Y(cur).toFixed(1);
            var xe = X(Math.min(upto, win.hi));
            d += 'H' + xe.toFixed(1);
            g += '<path d="' + d + '" fill="none" stroke="' + series[s].color + '" stroke-width="1.6" stroke-linejoin="round"/>';
            if (upto < win.hi) {
                g += '<circle cx="' + xe.toFixed(1) + '" cy="' + Y(cur).toFixed(1) + '" r="3" fill="' + series[s].color + '"/>';
                flags.push({ y: Y(cur), y0: Y(cur), c: series[s].color, t: (series.length > 1 ? series[s].name + '  ' : '') + fmtV(dl, cur) + (opts.unit && !dl ? ' ' + opts.unit : '') });
            }
        }
        /* cursor */
        if (R.T >= win.lo && R.T <= win.hi) {
            var cx = X(R.T).toFixed(1);
            g += '<line class="rp-cur" x1="' + cx + '" x2="' + cx + '" y1="0" y2="' + (top + ih) + '" stroke-dasharray="3 2"/>';
        }
        /* value flags at the end of the growing lines */
        if (flags.length) {
            flags.sort(function (p, q) { return p.y - q.y; });
            var FH = 15;
            for (i = 1; i < flags.length; i++) if (flags[i].y - flags[i - 1].y < FH) flags[i].y = flags[i - 1].y + FH;
            var over = flags[flags.length - 1].y + FH / 2 - (top + ih);
            if (over > 0) for (i = 0; i < flags.length; i++) flags[i].y -= over;
            if (flags[0].y < 7) { var dn = 7 - flags[0].y; for (i = 0; i < flags.length; i++) flags[i].y += dn; }
            var fx = X(Math.min(upto, win.hi));
            for (i = 0; i < flags.length; i++) {
                var f = flags[i], tw = f.t.length * 6 + 10;
                var right = fx + 8 + tw < pad + iw + 4;
                var bx = right ? fx + 8 : fx - 8 - tw;
                g += '<line x1="' + fx.toFixed(1) + '" y1="' + f.y0.toFixed(1) + '" x2="' + (right ? bx : bx + tw).toFixed(1) + '" y2="' + f.y.toFixed(1) + '" stroke="' + f.c + '" stroke-width="1" opacity=".7"/>' +
                    '<rect x="' + bx.toFixed(1) + '" y="' + (f.y - 7).toFixed(1) + '" width="' + tw.toFixed(1) + '" height="14" rx="3" fill="' + f.c + '"/>' +
                    '<text class="rp-flag" x="' + (bx + 5).toFixed(1) + '" y="' + (f.y + 3.5).toFixed(1) + '">' + esc(f.t) + '</text>';
            }
        }
        return '<svg class="rp-gsvg" width="' + W + '" height="' + H + '" data-rp-seek="1" data-lo="' + win.lo + '" data-span="' + span +
            '" data-pad="' + pad + '" data-iw="' + iw + '" role="img" aria-label="graph">' + g + '</svg>';
    }

    function alertTimesOf(name) {
        var out = [];
        for (var i = 0; i < R.alerts.length; i++) if (R.alerts[i].asset && normKey(R.alerts[i].asset) === normKey(name)) out.push(R.alerts[i].t);
        return out;
    }
    function alertOf(name) {
        for (var i = 0; i < R.alerts.length; i++) if (R.alerts[i].asset && normKey(R.alerts[i].asset) === normKey(name) && R.alerts[i].t <= R.T) return R.alerts[i];
        return null;
    }

    /* ---- graph panel ----------------------------------------------------- */
    function graphToolbarHtml() {
        var h = '<div class="rp-g-bar"><div class="sip-seg" role="group" aria-label="Asset type">';
        for (var i = 0; i < TYPES.length; i++) {
            var n = selAssets(TYPES[i][0]).length;
            h += '<button type="button" data-rp-gtype="' + TYPES[i][0] + '" aria-pressed="' + (G.type === TYPES[i][0]) + '">' + TYPES[i][1] + (n ? ' <small>' + n + '</small>' : '') + '</button>';
        }
        h += '</div><div class="rp-chips">';
        var shown = G.group === 'asset' ? selAssets() : selAssets(G.type);
        for (var j = 0; j < shown.length; j++) {
            h += '<span class="rp-chip on"><i style="background:' + colorOf(shown[j].name) + '"></i>' + esc(shown[j].name) +
                '<button type="button" data-rp-unsel="' + esc(shown[j].name) + '" aria-label="Remove ' + esc(shown[j].name) + '">&times;</button></span>';
        }
        var opts = R.assets.filter(function (a) { return kindOf(a) === G.type && !isSel(a.name); })
            .sort(function (x, y) { return x.name.localeCompare(y.name, undefined, { numeric: true }); });
        h += '<select class="rp-add" data-rp-add="1" aria-label="Add asset"><option value="">+ Add ' + TYPE_NAME[G.type].toLowerCase() + '</option>' +
            opts.map(function (a) { return '<option>' + esc(a.name) + '</option>'; }).join('') + '</select>' +
            (G.sel.length ? '<button type="button" class="rp-link" data-rp-neigh="1" title="Add the assets next to the selected ones on the yard">+ neighbours</button><button type="button" class="rp-link" data-rp-clear="1">clear</button>' : '') +
            '</div><span class="rp-sp"></span>' +
            '<span class="rp-lbl">Group</span><div class="sip-seg" role="group" aria-label="Group graphs by">' +
            '<button type="button" data-rp-group="asset" aria-pressed="' + (G.group === 'asset') + '">Asset</button>' +
            '<button type="button" data-rp-group="attr" aria-pressed="' + (G.group === 'attr') + '">Attribute</button></div>' +
            '<div class="sip-seg" role="group" aria-label="Graph mode">' +
            '<button type="button" data-rp-view="grow" aria-pressed="' + (G.view === 'grow') + '" title="Lines drawn up to the cursor">Grow</button>' +
            '<button type="button" data-rp-view="follow" aria-pressed="' + (G.view === 'follow') + '" title="Sliding window around the cursor">Follow</button>' +
            '<button type="button" data-rp-view="full" aria-pressed="' + (G.view === 'full') + '" title="Whole window, cursor line">Full</button></div>';
        if (G.view === 'follow') {
            h += '<div class="sip-seg" role="group" aria-label="Follow window">' + [5, 15, 30, 60].map(function (mn) {
                return '<button type="button" data-rp-win="' + mn + '" aria-pressed="' + (G.win === mn * 60000) + '">' + mn + 'm</button>';
            }).join('') + '</div>';
        }
        h += '</div>';
        /* attribute chips of the current type */
        var list = attrList(G.type), m = attrSel(G.type);
        h += '<div class="rp-g-bar rp-g-attrs"><span class="rp-lbl">' + TYPE_NAME[G.type] + ' attributes</span><div class="rp-chips">';
        if (!list.length) h += '<span class="rp-muted">' + (R.assets.some(function (a) { return kindOf(a) === G.type; }) ? 'Loading...' : 'No ' + TYPE_NAME[G.type].toLowerCase() + ' assets on this SIP.') + '</span>';
        for (var k = 0; k < list.length; k++) {
            h += '<button type="button" class="rp-chip sm' + (m[list[k].label] ? ' on' : '') + (list[k].dl ? ' dl' : '') + '" data-rp-attr="' + esc(list[k].label) + '">' + esc(list[k].label) + '</button>';
        }
        if (list.length) {
            var ps = PRESETS[G.type] || [];
            for (var p = 0; p < ps.length; p++) {
                var hit = list.some(function (x) { return ps[p][1].test(x.label); });
                if (hit) h += '<button type="button" class="rp-link" data-rp-preset2="' + p + '">' + esc(ps[p][0]) + '</button>';
            }
            h += '<button type="button" class="rp-link" data-rp-preset2="all">All</button><button type="button" class="rp-link" data-rp-preset2="none">None</button>';
        }
        return h + '</div></div>';
    }

    function emptyGraphHtml() {
        var h = '<div class="sip-rp-empty"><b>Nothing to plot yet.</b><br>Click a track, point or signal on the yard, pick one from <i>+ Add</i>, or start from an alert:';
        var n = 0;
        for (var i = 0; i < R.alerts.length && n < 6; i++) {
            if (!R.alerts[i].asset) continue;
            n++;
            h += '<br><button type="button" class="rp-chip al" data-rp-start="' + i + '">' + esc(fmtClock(R.alerts[i].t) + ' ' + R.alerts[i].asset + ' ' + R.alerts[i].text) + '</button>';
        }
        return h + '</div>';
    }

    function renderGraphs() {
        var host = byId('sipRpAttrs');
        var head = byId('sipRpSelHead');
        if (!host) return;
        renderHead();
        if (!R.active) {
            morph(host, '<div class="sip-rp-empty">Load a replay window, then click assets on the yard -- their readings are drawn here, moving with the playback.</div>');
            return;
        }
        var win = gWindow();
        var W = Math.max(320, host.clientWidth - 24);
        var scroll = host.scrollTop;
        var h = '';
        if (G.group === 'asset') {
            var list = selAssets();
            if (!list.length) { morph(host, emptyGraphHtml()); return; }
            for (var i = 0; i < list.length; i++) {
                var a = list[i], c = colorOf(a.name), kind = kindOf(a), coll = !!G.collapsed[a.name];
                var st = describeAsset(a.name), al = alertOf(a.name);
                var cis = a.cols ? colsFor(a) : [];
                h += '<div class="rp-ablk" style="border-left-color:' + c + '"><div class="rp-ab-h">' +
                    '<button type="button" class="rp-link" data-rp-coll="' + esc(a.name) + '" aria-label="' + (coll ? 'Expand' : 'Collapse') + '">' + (coll ? '&#9656;' : '&#9662;') + '</button>' +
                    '<b style="color:' + c + '">' + esc(a.name) + '</b><span class="rp-tag">' + TYPE_NAME[kind] + '</span>' +
                    (st ? '<span class="rp-tag' + (/occupied/.test(st) ? ' bad' : '') + '">' + esc(st) + '</span>' : '') +
                    (al ? '<span class="rp-tag bad">' + esc(al.text) + ' &middot; ' + fmtClock(al.t) + '</span>' : '');
                if (coll) {
                    h += '<span class="rp-muted">' + cis.map(function (ci) { return esc(colLabel(a, ci)) + ' <b>' + fmtVHtml(a.cols[ci].isDL, valAt(changes(a, ci), R.T)) + '</b>'; }).join(' &middot; ') + '</span>';
                }
                h += '<span class="rp-sp"></span><button type="button" class="rp-link" data-rp-gtype="' + kind + '">attributes</button>' +
                    '<button type="button" class="rp-link" data-rp-unsel="' + esc(a.name) + '" aria-label="Remove">&times;</button></div>';
                if (!coll) {
                    if (!a.cols || !a.rows.length) h += '<div class="rp-muted rp-pad">' + (a.err ? esc(a.err) : a.done ? 'No history rows in this window.' : 'Loading history...') + '</div>';
                    else if (!cis.length) h += '<div class="rp-muted rp-pad">No ' + TYPE_NAME[kind].toLowerCase() + ' attributes picked -- use <i>attributes</i>.</div>';
                    var ats = alertTimesOf(a.name);
                    for (var k = 0; k < cis.length; k++) {
                        var ci = cis[k], last = k === cis.length - 1, isDl = !!a.cols[ci].isDL, lb = colLabel(a, ci);
                        var ch = changes(a, ci);
                        h += '<div class="rp-arow"><div class="rp-al"><span title="' + esc(lb) + '">' + esc(lb) + '</span>' +
                            (G.view === 'full' ? '<b>' + fmtVHtml(isDl, valAt(ch, R.T)) + '</b>' : '') + '</div>' +
                            stepSvg([{ ch: ch, color: c, name: a.name, dl: isDl }], W - 158, isDl ? (last ? 46 : 32) : (last ? 66 : 50), win,
                                { noTime: !last, alerts: ats, unit: unitOf(lb) }) + '</div>';
                    }
                }
                h += '</div>';
            }
        } else {
            var as = selAssets(G.type);
            var labels = attrList(G.type).filter(function (x) { return attrSel(G.type)[x.label]; });
            if (!as.length || !labels.length) { morph(host, as.length ? '<div class="sip-rp-empty">Pick at least one attribute.</div>' : emptyGraphHtml()); return; }
            var allAl = [];
            for (var q = 0; q < as.length; q++) allAl = allAl.concat(alertTimesOf(as[q].name));
            for (var l = 0; l < labels.length; l++) {
                var series = [];
                for (var s = 0; s < as.length; s++) {
                    var aa = as[s];
                    if (!aa.cols) continue;
                    var o = colOrder(aa), cx = -1;
                    for (var z = 0; z < o.length; z++) if (colLabel(aa, o[z]) === labels[l].label) { cx = o[z]; break; }
                    if (cx < 0) continue;
                    series.push({ ch: changes(aa, cx), color: colorOf(aa.name), name: aa.name, dl: labels[l].dl });
                }
                if (!series.length) continue;
                var hh = Math.max(labels[l].dl ? 56 : 96, series.length * 15 + 30);
                h += '<div class="rp-lane"><div class="rp-lane-h"><b>' + esc(labels[l].label) + '</b>' + series.map(function (sr) {
                    return '<span><i style="background:' + sr.color + '"></i>' + esc(sr.name) + (G.view === 'full' ? ' <b>' + fmtVHtml(sr.dl, valAt(sr.ch, R.T)) + '</b>' : '') + '</span>';
                }).join('') + '</div>' + stepSvg(series, W, hh, win, { alerts: allAl, unit: unitOf(labels[l].label) }) + '</div>';
            }
            var others = selAssets().filter(function (x) { return kindOf(x) !== G.type; });
            if (others.length) h += '<div class="rp-muted rp-pad">Also selected: ' + others.map(function (x) { return '<button type="button" class="rp-link" data-rp-gtype="' + kindOf(x) + '">' + esc(x.name) + '</button>'; }).join(' ') + '</div>';
        }
        morph(host, h);
        if (host.scrollTop !== scroll) host.scrollTop = scroll;
    }

    /* v618.44 STABLE DOM -- the dock is never rebuilt with innerHTML while it
       is on screen: the new markup is MORPHED into the live nodes (same tag at
       the same place = node kept, only changed attributes / text written).
       Buttons, chips and the "+ Add" dropdown therefore stay the same element
       across play ticks: no flicker, hover stays, a click is never lost
       between mousedown and mouseup, an open <select> is not closed. */
    function morphAttrs(a, b) {
        var i, n;
        for (i = a.attributes.length - 1; i >= 0; i--) {
            n = a.attributes[i].name;
            if (!b.hasAttribute(n)) a.removeAttribute(n);
        }
        for (i = 0; i < b.attributes.length; i++) {
            n = b.attributes[i];
            if (a.getAttribute(n.name) !== n.value) a.setAttribute(n.name, n.value);
        }
    }
    function morphChildren(a, b) {
        var ac = a.firstChild, bc = b.firstChild, next, nx;
        while (bc) {
            next = bc.nextSibling;
            if (!ac) { a.appendChild(bc); bc = next; continue; }
            if (ac.nodeType === bc.nodeType && (ac.nodeType !== 1 || ac.nodeName === bc.nodeName)) {
                if (ac.nodeType === 1) {
                    morphAttrs(ac, bc);
                    if (ac.nodeName === 'SELECT' && ac === document.activeElement) { /* open dropdown: leave it alone */ }
                    else morphChildren(ac, bc);
                    if (ac.nodeName === 'OPTION' && ac.selected !== bc.selected && ac.parentNode !== document.activeElement) ac.selected = bc.selected;
                } else if (ac.nodeValue !== bc.nodeValue) ac.nodeValue = bc.nodeValue;
                ac = ac.nextSibling;
            } else {
                nx = ac.nextSibling;
                a.replaceChild(bc, ac);
                ac = nx;
            }
            bc = next;
        }
        while (ac) { nx = ac.nextSibling; a.removeChild(ac); ac = nx; }
    }
    function morph(el, html) {
        if (!el) return;
        if (el.__rpHtml === html) return;          /* nothing changed */
        el.__rpHtml = html;
        var t = document.createElement(el.nodeName === 'UL' ? 'ul' : 'div');
        t.innerHTML = html;
        if (!el.firstChild) { el.innerHTML = html; return; }
        morphChildren(el, t);
    }
    /* the toolbar is not touched while its dropdown is open / focused;
       it catches up as soon as the user is done (change / blur) */
    var _headPending = false;
    function renderHead() {
        var head = byId('sipRpSelHead');
        if (!head) return;
        var html = graphToolbarHtml();
        var ae = document.activeElement;
        if (ae && head.contains(ae) && ae.nodeName === 'SELECT') { _headPending = html !== head.__rpHtml; return; }
        _headPending = false;
        morph(head, html);
    }

    /* forced renders (data arriving in chunks) are coalesced; play ticks are
       throttled to ~4 per second */
    function renderPanel(force) {
        var now = Date.now();
        if (force) {
            if (G.graphTimer) return;
            G.graphTimer = setTimeout(function () { G.graphTimer = null; G.lastGraph = Date.now(); renderGraphs(); renderEvents(true); }, 60);
            return;
        }
        if (now - G.lastGraph < 250) return;
        G.lastGraph = now;
        renderGraphs();
    }

    /* ---- merged events list --------------------------------------------- */
    function analogEvents(a) {
        a.aev = a.aev || {};
        var cis = colsFor(a), out = [];
        for (var i = 0; i < cis.length; i++) {
            var ci = cis[i];
            if (a.cols[ci].isDL) continue;
            var key = ci + ':' + a.rows.length;
            if (!a.aev[key]) {
                var ch = changes(a, ci), ev = [], lb = colLabel(a, ci), u = unitOf(lb);
                var rng = (ch.hi - ch.lo) || 1;
                var lastV = ch.v.length ? ch.v[0] : null;
                for (var k = 1; k < ch.t.length; k++) {
                    var v = ch.v[k];
                    if (Math.abs(v - lastV) > Math.max(0.05, rng * 0.08, Math.max(Math.abs(v), Math.abs(lastV)) * 0.15)) {
                        ev.push({ t: ch.t[k], asset: a.name, kind: 'analog', html: esc(lb) + ' ' + fmtV(false, lastV) + ' &rarr; <b>' + fmtV(false, v) + '</b>' + (u ? ' ' + esc(u) : '') });
                        lastV = v;
                    } else if (Math.abs(v - lastV) > Math.max(0.05, rng * 0.08)) lastV = v;
                }
                a.aev[key] = ev;
            }
            out = out.concat(a.aev[key]);
        }
        return out;
    }
    function mergedEvents() {
        var scopeSel = G.evScope === 'sel';
        var sig = [G.evScope, G.sel.join(','), JSON.stringify(G.attrs), R.alerts.length, R.seenEvents.length].concat(R.assets.map(function (a) { return a.rows.length; })).join('|');
        if (sig === G.evSig && G.evCache) return G.evCache;
        var ev = [], i, j;
        for (i = 0; i < R.assets.length; i++) {
            var a = R.assets[i];
            if (scopeSel && !isSel(a.name)) continue;
            if (a.dl) for (j = 0; j < a.dl.length; j++) ev.push({ t: a.dl[j].t, asset: a.name, kind: 'relay', up: a.dl[j].up, html: esc(a.dl[j].text) });
            if (a.cols && a.rows.length) ev = ev.concat(analogEvents(a));
        }
        for (i = 0; i < R.seenEvents.length; i++) {
            var se = R.seenEvents[i];
            if (scopeSel && !isSel(se.asset)) continue;
            ev.push({ t: se.t, asset: se.asset, kind: 'state', html: esc(se.text) });
        }
        for (i = 0; i < R.alerts.length; i++) {
            var al = R.alerts[i];
            if (scopeSel && !(al.asset && isSel((assetByName(al.asset) || {}).name || al.asset))) continue;
            ev.push({ t: al.t, asset: al.asset, kind: 'alert', fail: al.fail, ai: i, html: (al.fail ? '<em class="sip-rp-tag">Failure</em> ' : '') + esc(al.text) + (al.reset ? '<small> reset ' + fmtClock(al.reset) + '</small>' : '') });
        }
        ev.sort(function (x, y) { return x.t - y.t || (x.kind === 'alert' ? -1 : 1); });
        G.evSig = sig;
        G.evCache = ev;
        return ev;
    }
    var _evRenderedFor = -1;
    function renderEvents(force) {
        var ul = byId('sipRpEvents');
        if (!ul) return;
        var ev = R.active ? mergedEvents() : [];
        var grow = G.view !== 'full';
        var cnt = { all: 0, alert: 0, relay: 0, analog: 0 };
        var vis = [];
        var idx = 0;
        for (var i = 0; i < ev.length; i++) {
            if (ev[i].t <= R.T) idx = i + 1;
            if (grow && ev[i].t > R.T) continue;
            var k = ev[i].kind === 'state' ? 'relay' : ev[i].kind;
            cnt.all++; cnt[k]++;
            if (G.evTab === 'all' || G.evTab === k) vis.push(ev[i]);
        }
        var key = idx + '|' + vis.length + '|' + G.evTab + '|' + G.evSig;
        if (!force && key === _evRenderedFor) return;
        /* v618.44: the list holds still while the pointer is on it (reading /
           clicking a row); it catches up on mouse-leave */
        if (G.evHover && R.playing) { G.evPending = true; return; }
        G.evPending = false;
        _evRenderedFor = key;
        var tabs = byId('sipRpEvTabs');
        if (tabs) {
            var bs = tabs.querySelectorAll('button[data-rp-evtab]');
            for (var b = 0; b < bs.length; b++) {
                var tk = bs[b].getAttribute('data-rp-evtab');
                bs[b].setAttribute('aria-pressed', String(tk === G.evTab));
                var sm = bs[b].querySelector('small');
                if (sm) sm.textContent = cnt[tk] || 0;
            }
        }
        var sc = byId('sipRpEvScope');
        if (sc) {
            var ss = sc.querySelectorAll('button[data-rp-evscope]');
            for (var s2 = 0; s2 < ss.length; s2++) ss[s2].setAttribute('aria-pressed', String(ss[s2].getAttribute('data-rp-evscope') === G.evScope));
        }
        var hint = byId('sipRpEvHint');
        if (hint) hint.textContent = grow ? 'up to cursor' : 'whole window';
        if (!vis.length) {
            morph(ul, '<li style="cursor:default"><time></time><span class="sip-rp-status">' +
                (!R.active ? 'Load a window to replay.' : G.evScope === 'sel' && !G.sel.length ? 'No assets selected -- click the yard, or switch to Whole yard.' : 'No events yet.') + '</span></li>');
            return;
        }
        var nowI = -1;
        for (var n = 0; n < vis.length; n++) if (vis[n].t <= R.T) nowI = n;
        var from = grow ? Math.max(0, vis.length - 250) : Math.max(0, nowI - 150);
        var to = grow ? vis.length : Math.min(vis.length, Math.max(nowI, 0) + 60);
        var html = '';
        for (var e = from; e < to; e++) {
            var x = vis[e], sel = x.asset && isSel((assetByName(x.asset) || {}).name || x.asset);
            html += '<li data-rp-t="' + x.t + '" data-rp-asset="' + esc(x.asset || '') + '"' +
                (x.kind === 'alert' ? ' data-rp-alert="' + x.ai + '" title="Replay around this alert"' : '') + ' class="k-' + x.kind +
                (x.kind === 'alert' ? (x.fail ? ' f' : ' a') : '') + (x.kind === 'relay' ? (x.up ? ' pk' : ' dr') : '') + (x.kind === 'state' ? ' s' : '') +
                (e === nowI ? ' now' : (x.t < R.T - 30000 ? ' past' : '')) + '">' +
                '<time>' + fmtClock(x.t) + '</time><span><i class="rp-dot" style="background:' + (sel ? colorOf((assetByName(x.asset) || {}).name || x.asset) : 'transparent') + '"></i><b>' + esc(x.asset || '') + '</b> ' + x.html + '</span></li>';
        }
        morph(ul, html);
        var nowEl = ul.querySelector('li.now') || (grow ? ul.lastElementChild : null);
        if (nowEl && !G.evHover) {
            var r1 = nowEl.getBoundingClientRect(), r0 = ul.getBoundingClientRect();
            if (r1.top < r0.top || r1.bottom > r0.bottom) ul.scrollTop += r1.top - r0.top - ul.clientHeight * 0.7;
        }
    }
    function renderAlerts() {
        G.evSig = '';
        renderEvents(true);
        if (!G.sel.length) renderPanel(true);
    }
    function selectAsset(name) {
        if (!name) return;
        addSel(name);
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
        else clearSel();
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
        /* v618.42: yard click toggles the asset in the graphs */
        onCellClick: function (cell, assetName) {
            var a = assetByName(assetName);
            if (a && isSel(a.name)) removeSel(a.name); else selectAsset(assetName);
        },
        selection: function () { return G.sel.slice(); },
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
