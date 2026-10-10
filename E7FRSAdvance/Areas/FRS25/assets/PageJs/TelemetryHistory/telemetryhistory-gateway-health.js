/*
 * Telemetry History — Station Gateway Down lock.
 *
 * When a Station is selected, subscribe to
 *     {WebSocketBaseUrl}/subscribe/deviceHealth/{siteId}
 * (ws://proxy.energy7.org:8081 on http, the SSL base on https) and watch two
 * things in that feed:
 *
 *   1. the groupHealth frame
 *        { type:"groupHealth", siteId, summary:{ gatewayTotal, gatewayUp, gatewayDown } }
 *      -> the station's gateway is DOWN when gatewayTotal > 0 and
 *         gatewayDown equals gatewayTotal;
 *   2. the per-gateway rows of the Batch snapshot
 *        { deviceType:"Gateway", channel:"GATEWAY_HEALTH", setTime, resetTime, ... }
 *      -> the latest setTime is when the gateway went down; a resetTime at or
 *         after that setTime means it came back up.
 *
 * While the gateway is down (and a setTime is known), no data exists after that
 * moment, so:
 *   - To Date / To Time are set to the gateway setTime (seconds included) and
 *     locked, and fnSearchHistory() re-applies them on every Search -- every
 *     view type builds its endDate from these two pickers, so all of them pass
 *     the setTime as the API "to" date;
 *   - From Date is capped at that day (moved back if it was later);
 *   - a "Station Gateway Down" popup is shown at the top of the page.
 * As soon as groupHealth reports a gateway up, or the gateway row carries a
 * resetTime, the lock is removed and To is restored to "now" so the user can
 * search any range again.
 *
 * Plain DOM on purpose: _Layout.cshtml loads a second jQuery after the view
 * body, which replaces window.$. Station changes are detected by watching
 * #drpSite's value, so programmatic .val(x) (no native event) is caught too.
 * Styles are injected from here so a year-cached CSS file cannot hide them.
 */
(function (window, document) {
    'use strict';

    var LOG = '[THGatewayHealth]';
    var RECONNECT_BASE_MS = 3000;
    var RECONNECT_MAX_MS = 30000;
    var SITE_WATCH_MS = 1000;
    var STYLE_ID = 'thGwStyle';
    var POPUP_ID = 'thGwPopup';
    var LOCK_CLASS = 'th-gw-locked';
    var LOCK_ICON_CLASS = 'th-gw-lock-icon';

    // Single connection for the selected station.
    var conn = null;          // { siteId, ws, timer, attempts, health, gw:{ id:{ set, reset } } }
    var lock = null;          // { siteId, setTime:'yyyy-MM-dd HH:mm:ss' } while To is locked
    var popupShownKey = null; // siteId|setTime the popup was last shown for
    var lastSeenSite = null;
    var stopped = false;

    function trim(s) { return String(s == null ? '' : s).replace(/^\s+|\s+$/g, ''); }
    function pad(n) { return ('0' + n).slice(-2); }

    // ── Config ────────────────────────────────────────────────────────────
    // APP_CONFIG is declared by _Layout.cshtml; read it lazily at connect time.
    function buildUrl(siteId) {
        var cfg = window.APP_CONFIG || {};
        if (!cfg.WebSocketBaseUrl) return null;
        var url = String(cfg.WebSocketBaseUrl).replace(/\/+$/, '') +
            '/subscribe/deviceHealth/' + encodeURIComponent(siteId);
        if (window.location.protocol === 'https:' && cfg.WebSocketAuthToken) {
            url += (url.indexOf('?') > -1 ? '&' : '?') + 'token=' + encodeURIComponent(cfg.WebSocketAuthToken);
        }
        return url;
    }

    function maskUrl(url) {
        return String(url).replace(/\/\/[^\/@]+@/, '//***@').replace(/token=[^&]+/, 'token=***');
    }

    // ── Message parsing ───────────────────────────────────────────────────
    function toInt(v) {
        var n = parseInt(v, 10);
        return isNaN(n) ? null : n;
    }

    function pick(obj, camel) {
        if (obj[camel] !== undefined) return obj[camel];
        return obj[camel.charAt(0).toUpperCase() + camel.slice(1)];
    }

    function sameSite(sid, siteId) {
        return sid == null || sid === '' || String(toInt(sid)) === String(toInt(siteId));
    }

    // Walk the payload (JSON strings are parsed on the way) for the object that
    // carries gatewayTotal -- the groupHealth summary. Depth-limited.
    function findSummary(node, depth, siteId) {
        if (node == null || depth > 8) return null;
        if (typeof node === 'string') {
            var s = trim(node);
            if (s.charAt(0) !== '{' && s.charAt(0) !== '[') return null;
            try { return findSummary(JSON.parse(s), depth + 1, siteId); } catch (e) { return null; }
        }
        if (Array.isArray(node)) {
            // Last entry in a batch is the most recent state.
            for (var i = node.length - 1; i >= 0; i--) {
                var hit = findSummary(node[i], depth + 1, siteId);
                if (hit) return hit;
            }
            return null;
        }
        if (typeof node === 'object') {
            if (!sameSite(pick(node, 'siteId'), siteId)) return null;
            if (pick(node, 'gatewayTotal') !== undefined) return node;
            for (var k in node) {
                if (!Object.prototype.hasOwnProperty.call(node, k)) continue;
                var v = node[k];
                if (v && (typeof v === 'object' || typeof v === 'string')) {
                    var found = findSummary(v, depth + 1, siteId);
                    if (found) return found;
                }
            }
        }
        return null;
    }

    function parseHealth(raw, siteId) {
        var obj = findSummary(raw, 0, siteId);
        if (!obj) return null;
        var total = toInt(pick(obj, 'gatewayTotal'));
        var down = toInt(pick(obj, 'gatewayDown'));
        var up = toInt(pick(obj, 'gatewayUp'));
        if (total === null || (down === null && up === null)) return null;
        if (down === null) down = Math.max(total - up, 0);
        if (up === null) up = Math.max(total - down, 0);
        return { total: total, up: up, down: down };
    }

    // GATEWAY_HEALTH rows (the channel gatewayTotal counts). EXGT_HEALTH
    // (external gateway) is ignored.
    function collectGatewayRows(node, depth, siteId, out) {
        if (node == null || depth > 8) return out;
        if (typeof node === 'string') {
            var s = trim(node);
            if (s.charAt(0) === '{' || s.charAt(0) === '[') {
                try { collectGatewayRows(JSON.parse(s), depth + 1, siteId, out); } catch (e) { }
            }
            return out;
        }
        if (Array.isArray(node)) {
            for (var i = 0; i < node.length; i++) collectGatewayRows(node[i], depth + 1, siteId, out);
            return out;
        }
        if (typeof node === 'object') {
            if (String(pick(node, 'deviceType') || '').toLowerCase() === 'gateway') {
                var ch = String(pick(node, 'channel') || '').toUpperCase();
                if (sameSite(pick(node, 'siteId'), siteId) && (!ch || ch === 'GATEWAY_HEALTH')) out.push(node);
                return out;
            }
            for (var k in node) {
                if (!Object.prototype.hasOwnProperty.call(node, k)) continue;
                var v = node[k];
                if (v && (typeof v === 'object' || typeof v === 'string')) collectGatewayRows(v, depth + 1, siteId, out);
            }
        }
        return out;
    }

    // "2026-09-22 11:04:14" / "2026-09-22T11:04:14" -> normalised text, or ''.
    function normTime(s) {
        var m = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})(?::(\d{2}))?/.exec(trim(s));
        return m ? m[1] + '-' + m[2] + '-' + m[3] + ' ' + m[4] + ':' + m[5] + ':' + (m[6] || '00') : '';
    }

    function applyGatewayRows(c, raw) {
        var rows = collectGatewayRows(raw, 0, c.siteId, []);
        for (var i = 0; i < rows.length; i++) {
            var id = String(pick(rows[i], 'deviceId') || c.siteId);
            // A row is the gateway's full current state: a null setTime/resetTime
            // means "none", so overwrite rather than merge.
            c.gw[id] = {
                set: normTime(pick(rows[i], 'setTime')),
                reset: normTime(pick(rows[i], 'resetTime'))
            };
        }
        return rows.length;
    }

    // Station is fully down from the moment its LAST gateway went down, so use
    // the latest setTime ("yyyy-MM-dd HH:mm:ss" sorts correctly as text). A
    // resetTime at/after that setTime means the gateway is back online.
    function latestDown(c) {
        var best = null;
        for (var id in c.gw) {
            if (!Object.prototype.hasOwnProperty.call(c.gw, id)) continue;
            var g = c.gw[id];
            if (g.set && (!best || g.set > best.set)) best = g;
        }
        if (!best) return { setTime: '', reset: false };
        return { setTime: best.set, reset: !!best.reset && best.reset >= best.set };
    }

    function isAllDown(h) { return !!h && h.total > 0 && h.down >= h.total; }

    // ── Date helpers ──────────────────────────────────────────────────────
    function parseLocal(text) {
        var m = /^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})$/.exec(text || '');
        if (!m) return null;
        var d = new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]);
        return isNaN(d.getTime()) ? null : d;
    }

    function dateVal(d) { return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate()); }
    function timeVal(d, withSec) { return pad(d.getHours()) + ':' + pad(d.getMinutes()) + (withSec ? ':' + pad(d.getSeconds()) : ''); }

    // "2026-09-22 11:04:14" -> "22-09-2026 11:04:14" (server time, shown as-is).
    function displayTime(text) {
        var m = /^(\d{4})-(\d{2})-(\d{2}) (.+)$/.exec(text || '');
        return m ? m[3] + '-' + m[2] + '-' + m[1] + ' ' + m[4] : (text || '');
    }

    function el(id) { return document.getElementById(id); }

    // ── Picker lock ───────────────────────────────────────────────────────
    function labelFor(input) {
        var item = input && input.closest ? input.closest('.filter-item') : null;
        return item ? item.querySelector('span') : null;
    }

    function setLockIcon(input, on) {
        var label = labelFor(input);
        if (!label) return;
        var icon = label.querySelector('.' + LOCK_ICON_CLASS);
        if (on && !icon) {
            icon = document.createElement('i');
            icon.className = 'fas fa-lock ' + LOCK_ICON_CLASS;
            icon.setAttribute('aria-hidden', 'true');
            label.appendChild(icon);
        } else if (!on && icon) {
            icon.parentNode.removeChild(icon);
        }
    }

    // Write the gateway setTime into To Date/Time and lock them. Called when the
    // lock starts and again by fnSearchHistory() before every search.
    function applyToPickers() {
        if (!lock || stopped) return false;
        var toDate = el('txtToDate'), toTime = el('txtToTime');
        var fromDate = el('txtFromDate'), fromTime = el('txtFromTime');
        if (!toDate || !toTime) return false;

        var cap = parseLocal(lock.setTime);
        if (!cap) return false;
        // Never past "now" (server/browser clock skew), or the page's own
        // future-date gate would reject the search.
        var now = new Date();
        if (cap > now) cap = now;

        var title = 'Locked to the gateway down time (' + displayTime(lock.setTime) + ')';
        toDate.value = dateVal(cap);
        toTime.setAttribute('step', '1');          // show / keep the seconds
        toTime.value = timeVal(cap, true);
        [toDate, toTime].forEach(function (inp) {
            inp.disabled = true;
            inp.classList.add(LOCK_CLASS);
            inp.setAttribute('title', title);
        });
        toDate.setAttribute('max', dateVal(cap));
        setLockIcon(toDate, true);
        setLockIcon(toTime, true);

        // From must stay before the lock.
        if (fromDate) {
            fromDate.setAttribute('max', dateVal(cap));
            var fromText = trim(fromDate.value);
            var from = fromText ? new Date(fromText + 'T' + ((fromTime && fromTime.value) || '00:00')) : null;
            if (from && !isNaN(from.getTime()) && from >= cap) {
                fromDate.value = dateVal(cap);
                if (fromTime) fromTime.value = '00:00';
            }
        }
        return true;
    }

    function releasePickers() {
        var toDate = el('txtToDate'), toTime = el('txtToTime'), fromDate = el('txtFromDate');
        if (!toDate || !toTime) return;
        var now = new Date();
        [toDate, toTime].forEach(function (inp) {
            inp.disabled = false;
            inp.classList.remove(LOCK_CLASS);
            inp.removeAttribute('title');
        });
        toTime.removeAttribute('step');
        // Back to the page default (To = now) so the user searches freely again.
        toDate.value = dateVal(now);
        toDate.setAttribute('max', dateVal(now));
        toTime.value = timeVal(now, false);
        toTime.setAttribute('max', timeVal(now, false));
        if (fromDate) fromDate.setAttribute('max', dateVal(now));
        setLockIcon(toDate, false);
        setLockIcon(toTime, false);
    }

    // ── Popup ─────────────────────────────────────────────────────────────
    var CSS =
        '.' + LOCK_CLASS + '{cursor:not-allowed;}' +
        '.' + LOCK_ICON_CLASS + '{margin-left:5px;font-size:10px;color:#dc2626;}' +
        '.th-gw-backdrop{position:fixed;top:0;right:0;bottom:0;left:0;z-index:100000;background:rgba(15,23,42,.35);display:flex;align-items:flex-start;justify-content:center;padding:72px 16px 16px;animation:thGwFade .18s ease-out;}' +
        '.th-gw-card{position:relative;width:100%;max-width:460px;background:#fff;border-radius:12px;border-top:4px solid #dc2626;box-shadow:0 18px 44px rgba(15,23,42,.28);padding:22px 22px 18px;text-align:center;animation:thGwDrop .24s ease-out;}' +
        '.th-gw-close{position:absolute;top:8px;right:10px;border:0;background:transparent;font-size:22px;line-height:1;color:#64748b;cursor:pointer;padding:4px;}' +
        '.th-gw-close:hover{color:#0f172a;}' +
        '.th-gw-icon{width:56px;height:56px;margin:0 auto 12px;border-radius:50%;background:#fee2e2;color:#dc2626;display:flex;align-items:center;justify-content:center;font-size:24px;animation:thGwPulse 1.6s ease-in-out infinite;}' +
        '.th-gw-title{margin:0 0 6px;font-size:18px;font-weight:700;color:#b91c1c;}' +
        '.th-gw-station{margin:0 0 12px;font-size:13px;font-weight:600;color:#334155;}' +
        '.th-gw-time{display:inline-block;margin:0 0 16px;padding:8px 14px;border-radius:8px;background:#fef2f2;border:1px solid #fecaca;font-size:13px;color:#7f1d1d;}' +
        '.th-gw-time strong{font-size:15px;color:#b91c1c;letter-spacing:.2px;}' +
        '.th-gw-actions{margin-top:4px;}' +
        '.th-gw-ok{border:0;border-radius:6px;background:#dc2626;color:#fff;font-size:13px;font-weight:600;padding:8px 22px;cursor:pointer;}' +
        '.th-gw-ok:hover{background:#b91c1c;}' +
        '@keyframes thGwFade{from{opacity:0;}to{opacity:1;}}' +
        '@keyframes thGwDrop{from{opacity:0;transform:translateY(-16px);}to{opacity:1;transform:translateY(0);}}' +
        '@keyframes thGwPulse{0%,100%{box-shadow:0 0 0 0 rgba(220,38,38,.35);}50%{box-shadow:0 0 0 10px rgba(220,38,38,0);}}' +
        '@media (prefers-reduced-motion:reduce){.th-gw-backdrop,.th-gw-card,.th-gw-icon{animation:none;}}';

    function ensureStyle() {
        if (el(STYLE_ID)) return;
        var st = document.createElement('style');
        st.id = STYLE_ID;
        st.appendChild(document.createTextNode(CSS));
        (document.head || document.documentElement).appendChild(st);
    }

    function escapeText(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function stationName() {
        var sel = el('drpSite');
        var opt = sel && sel.selectedIndex > -1 ? sel.options[sel.selectedIndex] : null;
        return opt ? trim(opt.text) : '';
    }

    function closePopup() {
        var p = el(POPUP_ID);
        if (p && p.parentNode) p.parentNode.removeChild(p);
        document.removeEventListener('keydown', onPopupKey, true);
    }

    function onPopupKey(e) {
        if (e.key === 'Escape' || e.keyCode === 27) closePopup();
    }

    function showPopup(setTime) {
        // Off for site-keeping users (set by TelemetryHistory/Index.cshtml).
        if (window.TH_GATEWAY_POPUP_ENABLED === false) return;
        ensureStyle();
        closePopup();
        var name = stationName();
        var p = document.createElement('div');
        p.id = POPUP_ID;
        p.className = 'th-gw-backdrop';
        p.innerHTML =
            '<div class="th-gw-card" role="alertdialog" aria-modal="true" aria-labelledby="thGwTitle"' + (setTime ? ' aria-describedby="thGwTime"' : '') + '>' +
            '<button type="button" class="th-gw-close" aria-label="Close">&times;</button>' +
            '<div class="th-gw-icon"><i class="fas fa-exclamation-triangle"></i></div>' +
            '<h4 class="th-gw-title" id="thGwTitle">Station Gateway Down</h4>' +
            (name ? '<p class="th-gw-station">' + escapeText(name) + '</p>' : '') +
            (setTime ? '<div class="th-gw-time" id="thGwTime">Gateway down since <strong>' + escapeText(displayTime(setTime)) + '</strong></div>' : '') +
            '<div class="th-gw-actions"><button type="button" class="th-gw-ok">OK, got it</button></div>' +
            '</div>';
        p.addEventListener('click', function (e) {
            var t = e.target;
            if (t === p || (t.closest && (t.closest('.th-gw-close') || t.closest('.th-gw-ok')))) closePopup();
        });
        document.body.appendChild(p);
        document.addEventListener('keydown', onPopupKey, true);
        var ok = p.querySelector('.th-gw-ok');
        if (ok) { try { ok.focus(); } catch (e) { } }
    }

    function notify(type, msg, title) {
        var fn = window[type === 'info' ? 'showInfo' : 'showWarning'];
        try { if (typeof fn === 'function') fn(msg, title); } catch (e) { }
    }

    // ── State evaluation ──────────────────────────────────────────────────
    function evaluate() {
        var c = conn;
        var siteId = currentSiteId();
        var relevant = !stopped && c && c.siteId === siteId;
        var down = relevant && isAllDown(c.health);
        var info = relevant ? latestDown(c) : { setTime: '', reset: false };
        if (info.reset) down = false;            // resetTime present -> gateway is up

        var wantLock = down && !!info.setTime;
        var wasLocked = !!lock;

        if (wantLock) {
            var changed = !lock || lock.siteId !== siteId || lock.setTime !== info.setTime;
            lock = { siteId: siteId, setTime: info.setTime };
            if (changed) {
                applyToPickers();
                console.log(LOG, 'site=' + siteId + ' GATEWAY DOWN since ' + info.setTime + ' -> To Date locked', c.health);
            }
        } else if (lock) {
            var sameSiteRecovered = relevant && lock.siteId === siteId;
            lock = null;
            releasePickers();
            if (sameSiteRecovered) {
                console.log(LOG, 'site=' + siteId + ' gateway up -> To Date lock removed', c && c.health);
                closePopup();
                notify('info', 'Station gateway is back online. To Date restriction removed — you can search any range now.', 'Gateway Restored');
            }
        }

        // Popup once per down episode (station + setTime); also when down is
        // known but its setTime has not arrived yet.
        if (down) {
            var key = siteId + '|' + (info.setTime || '');
            if (popupShownKey !== key) {
                popupShownKey = key;
                showPopup(info.setTime);
            }
        } else if (relevant && c.health) {
            popupShownKey = null;
        }
        return wasLocked !== !!lock;
    }

    // ── Socket lifecycle (selected station only) ──────────────────────────
    function closeConn() {
        if (!conn) return;
        if (conn.timer) { clearTimeout(conn.timer); conn.timer = null; }
        if (conn.ws) {
            try {
                conn.ws.onopen = conn.ws.onmessage = conn.ws.onclose = conn.ws.onerror = null;
                conn.ws.close(1000);
            } catch (e) { }
            conn.ws = null;
        }
        conn = null;
    }

    function scheduleReconnect(c) {
        if (stopped || conn !== c) return;
        if (c.timer) clearTimeout(c.timer);
        var delay = Math.min(RECONNECT_BASE_MS * Math.pow(2, c.attempts), RECONNECT_MAX_MS);
        c.attempts++;
        c.timer = setTimeout(function () {
            c.timer = null;
            if (conn === c) connect(c);
        }, delay);
    }

    function connect(c) {
        var url = buildUrl(c.siteId);
        if (!url) { console.warn(LOG, 'APP_CONFIG.WebSocketBaseUrl is empty — cannot subscribe site=' + c.siteId); return; }
        if (typeof window.WebSocket === 'undefined') { console.warn(LOG, 'WebSocket not supported'); return; }

        console.log(LOG, 'subscribing', maskUrl(url));
        var sock;
        try { sock = new WebSocket(url); } catch (e) {
            console.warn(LOG, 'connect failed site=' + c.siteId, e);
            scheduleReconnect(c);
            return;
        }
        c.ws = sock;

        sock.onopen = function () {
            if (c.ws !== sock) return;
            c.attempts = 0;
        };
        sock.onmessage = function (evt) {
            if (c.ws !== sock || conn !== c) return;
            var rows = applyGatewayRows(c, evt.data);   // setTime / resetTime
            var h = parseHealth(evt.data, c.siteId);    // up/down verdict
            if (h) c.health = h;                        // kept across reconnects
            if (h || rows) evaluate();
        };
        sock.onerror = function () { console.warn(LOG, 'socket error site=' + c.siteId); };
        sock.onclose = function () {
            if (c.ws !== sock) return;
            c.ws = null;
            scheduleReconnect(c);
        };
    }

    function currentSiteId() {
        var sel = el('drpSite');
        var v = sel ? trim(sel.value) : '';
        return v === '0' ? '' : v;
    }

    // (Re)subscribe whenever the selected station changes.
    function onSiteChange() {
        var siteId = currentSiteId();
        if (siteId === lastSeenSite) return;
        lastSeenSite = siteId;
        closeConn();
        closePopup();
        popupShownKey = null;
        evaluate();                               // drops the previous station's lock
        if (!siteId || stopped) return;
        conn = { siteId: siteId, ws: null, timer: null, attempts: 0, health: null, gw: {} };
        connect(conn);
    }

    function stop() {
        stopped = true;
        closeConn();
    }

    function init() {
        try {
            ensureStyle();
            document.addEventListener('change', function (e) {
                var id = e.target && e.target.id;
                if (id === 'drpSite' || id === 'drpZones' || id === 'drpDivisions') setTimeout(onSiteChange, 0);
            }, true);
            // Zone/Division rebuild #drpSite without a change event, and other
            // code may set it via jQuery (no native event).
            setInterval(onSiteChange, SITE_WATCH_MS);
            window.addEventListener('beforeunload', stop);
            onSiteChange();
        } catch (e) {
            console.error(LOG, 'init failed', e);
        }
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();

    window.TelemetryHistoryGateway = {
        // Re-applies the lock to To Date/Time; true when a lock is active.
        applyToPickers: applyToPickers,
        // { siteId, setTime:'yyyy-MM-dd HH:mm:ss' } while locked, else null.
        getLock: function () { return lock ? { siteId: lock.siteId, setTime: lock.setTime } : null; },
        displayTime: displayTime,
        state: function () {
            return {
                selectedSite: currentSiteId(),
                lock: lock,
                socket: conn ? { siteId: conn.siteId, readyState: conn.ws ? conn.ws.readyState : null, health: conn.health, gateways: conn.gw } : null
            };
        },
        _parseHealth: parseHealth
    };
})(window, document);
