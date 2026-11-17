/**
 * e7-favourite-site.js — one favourite site per user, applied on every page.
 *
 *  1. Header "★ Favourite" pill → searchable picker of the user's sites.
 *     Click a site to make it the favourite (replaces the old one); click the
 *     gold star again to remove it.
 *  2. A star inside every Station checkbox-dropdown row (.dropdown-item /
 *     .alrt-dropdown-item holding a *cb-site checkbox) does the same inline.
 *  3. On each page load the favourite is pre-selected:
 *       - Zone → Division → Station checkbox cascades: ticks each level through
 *         the page's own handlers, then presses that filter bar's Search button.
 *       - <select> site pickers (id/name containing "site"/"station"): selects
 *         the favourite when the picker is still on its default.
 *     Nothing is overridden when the page already has a site chosen, when the
 *     URL carries a siteId (deep links / notifications), or after the user has
 *     touched the picker.
 *
 * Server: /FRS25/FavouriteSite/{Get,Sites,Set,Clear}. The saved value is
 * rendered by _Layout as window.E7_FAV_SITE so no request is needed on load.
 */
(function () {
    'use strict';

    var API = '/FRS25/FavouriteSite/';
    var confirmed = window.E7_FAV_SITE || null;    // last value the server confirmed
    var fav = confirmed;                           // what the UI shows: { SiteId, SiteName, ZoneId, DivisionId }
    var sitesCache = null;

    // A change still being saved when the user changed page is remembered for this
    // tab, so the next page shows it straight away instead of the older server value.
    var PENDING_KEY = 'e7favPending';
    var PENDING_TTL = 3 * 60 * 1000;
    function readPending() {
        try {
            var p = JSON.parse(sessionStorage.getItem(PENDING_KEY) || 'null');
            if (p && p.user === window.E7_FAV_USER && Date.now() - p.at < PENDING_TTL) return p;
        } catch (e) { }
        return null;
    }
    function writePending(f) {
        try { sessionStorage.setItem(PENDING_KEY, JSON.stringify({ user: window.E7_FAV_USER, at: Date.now(), fav: f })); } catch (e) { }
    }
    function clearPending() { try { sessionStorage.removeItem(PENDING_KEY); } catch (e) { } }
    (function () {
        var p = readPending();
        if (!p) { clearPending(); return; }
        var server = confirmed ? String(confirmed.SiteId) : '';
        var wanted = p.fav ? String(p.fav.SiteId) : '';
        if (server === wanted) clearPending();          // server has caught up
        else { fav = p.fav; window.E7_FAV_SITE = fav; }   // still saving: show the user's choice
    })();

    // ── helpers ───────────────────────────────────────────────────────────
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function toast(type, title, msg, ms) {
        try { if (window.E7Theme && E7Theme.toast) return E7Theme.toast(type, title, msg, ms); } catch (e) { }
        return null;
    }
    function isFav(id) { return !!fav && String(fav.SiteId) === String(id); }
    function cssEsc(v) { return String(v).replace(/(["\\])/g, '\\$1'); }
    function visible(el) { return !!(el && (el.offsetWidth || el.offsetHeight || el.getClientRects().length)); }

    function post(url, data) {
        var body = data ? Object.keys(data).map(function (k) {
            return encodeURIComponent(k) + '=' + encodeURIComponent(data[k]);
        }).join('&') : '';
        return fetch(url, {
            method: 'POST', credentials: 'same-origin',
            keepalive: true,   // finish the save even if the user changes page meanwhile
            headers: { 'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8', 'X-Requested-With': 'XMLHttpRequest' },
            body: body
        }).then(function (r) { return r.json(); });
    }

    /** Resolve when `fn()` returns truthy; reject after `timeout` ms. */
    function waitFor(fn, timeout) {
        return new Promise(function (resolve, reject) {
            var t0 = Date.now();
            (function tick() {
                var v = null;
                try { v = fn(); } catch (e) { }
                if (v) return resolve(v);
                if (Date.now() - t0 > (timeout || 10000)) return reject(new Error('timeout'));
                setTimeout(tick, 120);
            })();
        });
    }
    function delay(ms) { return new Promise(function (r) { setTimeout(r, ms); }); }

    // ── styles ────────────────────────────────────────────────────────────
    function injectCss() {
        if (document.getElementById('e7favCss')) return;
        var css =
            '.e7fav-wrap{position:relative;display:inline-flex}' +
            '.e7fav-btn{display:inline-flex;align-items:center;gap:6px;max-width:220px}' +
            '.e7fav-btn .e7fav-ico{color:#f5b301;font-size:15px;line-height:1}' +
            '.e7fav-btn.is-empty .e7fav-ico{color:var(--e7-text-3,#8a94a6)}' +
            '.e7fav-btn .e7fav-lbl{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:600}' +
            '.e7fav-panel{position:absolute;top:calc(100% + 8px);right:0;width:340px;max-width:calc(100vw - 24px);z-index:10050;' +
            'background:var(--e7-surface,#fff);color:var(--e7-text,#1d2433);border:1px solid var(--e7-border,#dfe3ea);border-radius:12px;' +
            'box-shadow:0 12px 32px rgba(0,0,0,.18);display:none;overflow:hidden}' +
            '.e7fav-panel.open{display:block}' +
            '.e7fav-head{padding:12px 14px 8px;border-bottom:1px solid var(--e7-border,#dfe3ea)}' +
            '.e7fav-head h6{margin:0 0 2px;font-size:13px;font-weight:700;display:flex;align-items:center;gap:6px}' +
            '.e7fav-head p{margin:0 0 8px;font-size:11.5px;color:var(--e7-text-3,#8a94a6)}' +
            '.e7fav-cur{display:flex;align-items:center;gap:8px;padding:8px 10px;margin-bottom:8px;border-radius:8px;' +
            'background:var(--e7-accent-soft,rgba(245,179,1,.12));font-size:12.5px}' +
            '.e7fav-cur b{flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}' +
            '.e7fav-cur button{border:0;background:none;color:#d9534f;font-size:11.5px;font-weight:600;cursor:pointer;padding:2px 4px}' +
            '.e7fav-search{width:100%;padding:7px 10px;border-radius:8px;border:1px solid var(--e7-border-2,#cfd5df);' +
            'background:var(--e7-surface-2,#f6f8fb);color:inherit;font-size:12.5px;outline:none}' +
            '.e7fav-search:focus{border-color:var(--e7-accent,#3a8fd6)}' +
            '.e7fav-list{max-height:320px;overflow-y:auto;padding:4px 0}' +
            '.e7fav-row{display:flex;align-items:center;gap:10px;padding:7px 14px;cursor:pointer;font-size:12.5px}' +
            '.e7fav-row:hover,.e7fav-row.kb{background:var(--e7-surface-2,#f2f5f9)}' +
            '.e7fav-row .st{font-size:16px;line-height:1;color:var(--e7-text-3,#b5bcc8);width:18px;text-align:center}' +
            '.e7fav-row.on .st{color:#f5b301}' +
            '.e7fav-row.on{font-weight:700}' +
            '.e7fav-row .nm{flex:1;min-width:0}' +
            '.e7fav-row .nm div{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}' +
            '.e7fav-row .sub{font-size:11px;color:var(--e7-text-3,#8a94a6);font-weight:400}' +
            '.e7fav-empty{padding:18px;text-align:center;font-size:12px;color:var(--e7-text-3,#8a94a6)}' +
            '.e7fav-foot{padding:8px 14px;border-top:1px solid var(--e7-border,#dfe3ea);font-size:11px;color:var(--e7-text-3,#8a94a6)}' +
            /* inline star inside Station dropdown rows */
            '.e7fav-star{margin-left:auto;border:0;background:none;padding:0 2px;font-size:15px;line-height:1;cursor:pointer;' +
            'color:var(--e7-text-3,#b5bcc8);opacity:0;transition:opacity .12s,transform .12s}' +
            '.e7fav-host{display:flex;align-items:center}' +
            '.e7fav-host:hover .e7fav-star{opacity:.75}' +
            '.e7fav-star:hover{transform:scale(1.2);opacity:1!important}' +
            '.e7fav-star.on{color:#f5b301;opacity:1}' +
            /* saving state */
            '.e7fav-btn.is-busy .e7fav-ico{animation:e7favPulse 1s ease-in-out infinite}' +
            '@keyframes e7favPulse{0%,100%{opacity:1}50%{opacity:.3}}' +
            '.e7fav-toast-act{margin-top:6px;border:0;border-radius:6px;padding:3px 10px;font-size:11.5px;font-weight:600;cursor:pointer;' +
            'background:var(--e7-accent,#3a8fd6);color:#fff}' +
            '@media (max-width:768px){.e7fav-btn .e7fav-lbl{display:none}}';
        var st = document.createElement('style');
        st.id = 'e7favCss';
        st.textContent = css;
        document.head.appendChild(st);
    }

    // ── header pill + picker ──────────────────────────────────────────────
    var $btn, $lbl, $panel, $list, $search, $cur, kbIndex = -1;

    function buildHeader() {
        var right = document.querySelector('.e7-header .e7-header-right');
        if (!right || document.getElementById('e7FavWrap')) return;

        var wrap = document.createElement('div');
        wrap.className = 'e7fav-wrap';
        wrap.id = 'e7FavWrap';
        wrap.innerHTML =
            '<button type="button" class="pill e7fav-btn" id="e7FavBtn" aria-haspopup="true" aria-expanded="false">' +
            '<span class="e7fav-ico" aria-hidden="true">&#9733;</span><span class="e7fav-lbl"></span></button>' +
            '<div class="e7fav-panel" id="e7FavPanel" role="dialog" aria-label="Favourite station">' +
            '<div class="e7fav-head"><h6><span style="color:#f5b301">&#9733;</span> Favourite station</h6>' +
            '<p>Pre-selected on every page after you log in. Only one station can be your favourite.</p>' +
            '<div class="e7fav-cur" id="e7FavCur"></div>' +
            '<input type="text" class="e7fav-search" id="e7FavSearch" placeholder="Search station / code / division..." autocomplete="off"></div>' +
            '<div class="e7fav-list" id="e7FavList"><div class="e7fav-empty">Loading stations...</div></div>' +
            '<div class="e7fav-foot">Tip: you can also click the &#9734; next to any station in the Station filter.</div>' +
            '</div>';

        var anchor = document.getElementById('toggle-btn');
        right.insertBefore(wrap, anchor && anchor.parentNode === right ? anchor : right.firstChild);

        $btn = wrap.querySelector('#e7FavBtn');
        $lbl = wrap.querySelector('.e7fav-lbl');
        $panel = wrap.querySelector('#e7FavPanel');
        $list = wrap.querySelector('#e7FavList');
        $search = wrap.querySelector('#e7FavSearch');
        $cur = wrap.querySelector('#e7FavCur');

        $btn.addEventListener('click', function (e) { e.stopPropagation(); togglePanel(); });
        $panel.addEventListener('click', function (e) { e.stopPropagation(); });
        document.addEventListener('click', function () { closePanel(); });
        document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closePanel(); });

        $search.addEventListener('input', function () { kbIndex = -1; renderList(); });
        $search.addEventListener('keydown', function (e) {
            var rows = $list.querySelectorAll('.e7fav-row');
            if (!rows.length) return;
            if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                e.preventDefault();
                kbIndex = (kbIndex + (e.key === 'ArrowDown' ? 1 : -1) + rows.length) % rows.length;
                rows.forEach(function (r, i) { r.classList.toggle('kb', i === kbIndex); });
                rows[kbIndex].scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'Enter') {
                e.preventDefault();
                var r = rows[kbIndex >= 0 ? kbIndex : 0];
                if (r) r.click();
            }
        });
        $list.addEventListener('click', function (e) {
            var row = e.target.closest('.e7fav-row');
            if (!row) return;
            var id = row.getAttribute('data-id');
            if (isFav(id)) clearFavourite(); else setFavourite(id, row.getAttribute('data-name'));
        });
        $cur.addEventListener('click', function (e) {
            if (e.target.closest('[data-act="clear"]')) clearFavourite();
            else if (e.target.closest('[data-act="sip"]')) { closePanel(); openSip(fav, true); }
        });

        refreshHeader();
    }

    function refreshHeader() {
        if (!$btn) return;
        $btn.classList.toggle('is-empty', !fav);
        $lbl.textContent = fav ? fav.SiteName : 'Favourite';
        $btn.title = fav ? ('Favourite station: ' + fav.SiteName + ' (click to change)') : 'Choose a favourite station';
        $cur.innerHTML = fav
            ? '<span style="color:#f5b301">&#9733;</span><b>' + esc(fav.SiteName) + '</b>' +
              (window.E7_CAN_SIP ? '<button type="button" data-act="sip" class="e7fav-sip-btn" title="Open the live SIP of this station full screen">' +
                  '<i class="fa-solid fa-diagram-project"></i> SIP</button>' : '') +
              '<button type="button" data-act="clear">Remove</button>'
            : '<span style="opacity:.7">No favourite yet — pick one below.</span>';
        if (sitesCache) renderList();
    }

    function togglePanel() { $panel.classList.contains('open') ? closePanel() : openPanel(); }
    function openPanel() {
        $panel.classList.add('open');
        $btn.setAttribute('aria-expanded', 'true');
        $search.value = '';
        kbIndex = -1;
        setTimeout(function () { $search.focus(); }, 50);
        loadSites().then(renderList, function (err) {
            var msg = err && err.message && err.message !== 'failed' ? err.message : 'Could not load stations. Please retry.';
            $list.innerHTML = '<div class="e7fav-empty">' + esc(msg) + '</div>';
        });
    }
    function closePanel() {
        if (!$panel || !$panel.classList.contains('open')) return;
        $panel.classList.remove('open');
        $btn.setAttribute('aria-expanded', 'false');
    }

    var sitesLoading = null;
    function loadSites() {
        if (sitesCache) return Promise.resolve(sitesCache);
        if (sitesLoading) return sitesLoading;   // reuse a request already on its way
        sitesLoading = fetch(API + 'Sites', { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) {
                return r.json().catch(function () { throw new Error('Could not load stations (HTTP ' + r.status + ').'); });
            })
            .then(function (d) {
                if (!d || !d.ok) throw new Error((d && d.message) || 'failed');
                sitesCache = d.sites || [];
                return sitesCache;
            });
        // allow a retry after a failure
        sitesLoading.then(function () { sitesLoading = null; }, function () { sitesLoading = null; });
        return sitesLoading;
    }

    function renderList() {
        var q = ($search.value || '').trim().toLowerCase();
        var rows = (sitesCache || []).filter(function (s) {
            if (!q) return true;
            return [s.Name, s.StationCode, s.DivisionName, s.ZoneName].join(' ').toLowerCase().indexOf(q) > -1;
        });
        // favourite first
        rows.sort(function (a, b) { return (isFav(b.Id) ? 1 : 0) - (isFav(a.Id) ? 1 : 0); });
        if (!rows.length) {
            $list.innerHTML = '<div class="e7fav-empty">' + (q ? 'No station matches "' + esc(q) + '"' : 'No stations assigned to you.') + '</div>';
            return;
        }
        $list.innerHTML = rows.map(function (s) {
            var on = isFav(s.Id);
            var sub = [s.StationCode, s.DivisionName, s.ZoneName].filter(Boolean).map(esc).join(' · ');
            return '<div class="e7fav-row' + (on ? ' on' : '') + '" data-id="' + s.Id + '" data-name="' + esc(s.Name) + '"' +
                ' title="' + (on ? 'Click to remove favourite' : 'Click to make favourite') + '">' +
                '<span class="st">' + (on ? '&#9733;' : '&#9734;') + '</span>' +
                '<div class="nm"><div>' + esc(s.Name) + '</div>' + (sub ? '<div class="sub">' + sub + '</div>' : '') + '</div></div>';
        }).join('');
    }

    // ── save / clear (optimistic) ─────────────────────────────────────────
    // The UI changes at once; the server call runs in the background. Saves are
    // sent one at a time and only the latest choice matters. If the server
    // rejects it, the UI goes back to the last value the server confirmed.
    var inFlight = false;
    var queued = null;   // { type: 'set', id } | { type: 'clear' }

    function setSyncing(on) {
        if (!$btn) return;
        $btn.classList.toggle('is-busy', on);
        $btn.title = on ? 'Saving your favourite station…'
            : (fav ? 'Favourite station: ' + fav.SiteName + ' (click to change)' : 'Choose a favourite station');
    }

    function siteMeta(id) {
        var s = (sitesCache || []).filter(function (x) { return String(x.Id) === String(id); })[0];
        return s ? { SiteId: s.Id, SiteName: s.Name, StationCode: s.StationCode, ZoneId: s.ZoneId, DivisionId: s.DivisionId } : null;
    }

    function show(f) {
        fav = f;
        window.E7_FAV_SITE = f;
        onFavChanged();
    }

    function enqueue(action) {
        queued = action;
        setSyncing(true);
        if (!inFlight) pump();
    }

    function pump() {
        if (!queued) { inFlight = false; setSyncing(false); return; }
        var a = queued;
        queued = null;
        inFlight = true;
        var req = a.type === 'set' ? post(API + 'Set', { siteId: a.id }) : post(API + 'Clear');
        req.then(function (d) {
            if (!d || !d.ok) throw new Error((d && d.message) || (a.type === 'set' ? 'Could not save favourite' : 'Could not remove favourite'));
            confirmed = a.type === 'set' ? d.favourite : null;
            if (!queued) {
                clearPending();
                // take the server's copy (adds zone/division/name) if it is still the one shown
                if (a.type === 'set' && d.favourite && isFav(d.favourite.SiteId)) show(d.favourite);
            }
        }).catch(function (err) {
            if (queued) return;   // a newer choice is about to be sent anyway
            clearPending();
            show(confirmed);
            toast('bad', a.type === 'set' ? 'Favourite not saved' : 'Favourite not removed',
                esc(err.message || err) + (confirmed ? '<br>Your favourite is still ' + esc(confirmed.SiteName) + '.' : ''), 7000);
        }).then(pump);
    }

    function setFavourite(siteId, siteName) {
        if (isFav(siteId)) return;
        var prev = fav;
        show(siteMeta(siteId) || { SiteId: Number(siteId), SiteName: siteName || ('Station ' + siteId), ZoneId: 0, DivisionId: 0 });
        writePending(fav);
        enqueue({ type: 'set', id: siteId });

        closePanel();

        // A star clicked in a Station dropdown has no zone/division yet: fill them in quietly.
        if (!siteMeta(siteId)) {
            loadSites().then(function () {
                var m = siteMeta(siteId);
                if (m && isFav(siteId) && !fav.ZoneId) { fav = m; window.E7_FAV_SITE = m; if (inFlight || queued) writePending(m); }
            }, function () { });
        }

        var msg = (prev ? 'Replaced ' + esc(prev.SiteName) + '. ' : '') + 'It will be pre-selected on every page.' +
            '<br><button type="button" class="e7fav-toast-act">Apply on this page</button>';
        var t = toast('ok', '&#9733; ' + esc(fav.SiteName) + ' is your favourite', msg, 6000);
        var b = t && t.querySelector('.e7fav-toast-act');
        if (b) b.addEventListener('click', function () { b.disabled = true; applyAll(true); });
    }

    function clearFavourite() {
        if (!fav) return;
        var old = fav;
        show(null);
        writePending(null);
        enqueue({ type: 'clear' });
        toast('info', 'Favourite removed', esc(old.SiteName) + ' will no longer be pre-selected.');
    }

    function onFavChanged() {
        refreshHeader();
        document.querySelectorAll('.e7fav-star').forEach(paintStar);
        try { document.dispatchEvent(new CustomEvent('e7:favouriteSiteChanged', { detail: fav })); } catch (e) { }
    }

    // ── inline star in Station checkbox dropdowns ─────────────────────────
    var SITE_CB_RE = /(^|\s)([\w-]*?)cb-site(\s|$)/;

    function siteCbIn(item) {
        var cb = item.querySelector('input[type="checkbox"]');
        return cb && SITE_CB_RE.test(cb.className || '') ? cb : null;
    }

    function paintStar(star) {
        var on = isFav(star.getAttribute('data-site'));
        star.classList.toggle('on', on);
        star.innerHTML = on ? '&#9733;' : '&#9734;';
        star.title = on ? 'Favourite station (click to remove)' : 'Make this my favourite station';
    }

    function decorateItems(root) {
        var items = (root || document).querySelectorAll('.dropdown-item, .alrt-dropdown-item');
        for (var i = 0; i < items.length; i++) {
            var item = items[i];
            if (item.querySelector('.e7fav-star')) continue;
            var cb = siteCbIn(item);
            if (!cb || !cb.value || cb.value === '0') continue;
            var star = document.createElement('button');
            star.type = 'button';
            star.className = 'e7fav-star';
            star.setAttribute('data-site', cb.value);
            item.classList.add('e7fav-host');
            item.appendChild(star);
            paintStar(star);
        }
    }

    // Capture phase so the page's own row-click handler never sees the star click.
    document.addEventListener('click', function (e) {
        var star = e.target && e.target.closest && e.target.closest('.e7fav-star');
        if (!star) return;
        e.preventDefault();
        e.stopPropagation();
        var id = star.getAttribute('data-site');
        if (isFav(id)) { clearFavourite(); return; }
        var item = star.parentNode;
        var name = (item.getAttribute('data-text') || (item.querySelector('span') || item).textContent || '').trim();
        setFavourite(id, name);
    }, true);

    // ── auto-apply: checkbox cascades ─────────────────────────────────────
    function cascadePrefixes() {
        var set = {};
        document.querySelectorAll('input[type="checkbox"][class*="cb-zone"], input[type="checkbox"][class*="cb-site"]').forEach(function (cb) {
            var m = /(^|\s)([\w-]*?)cb-(zone|site)(\s|$)/.exec(cb.className || '');
            if (m) set[m[2]] = true;
        });
        return Object.keys(set);
    }
    function cbFor(prefix, level, value) {
        return document.querySelector('input[type="checkbox"].' + prefix + 'cb-' + level + '[value="' + cssEsc(value) + '"]');
    }
    function tick(cb) {
        // Native click: toggles the box, fires change, and bubbles to the page's
        // row handler (which sees target === checkbox and does not re-toggle).
        if (cb && !cb.checked) cb.click();
    }

    /** Filter bar Search button for the dropdown that contains `cb`. */
    function searchButtonFor(cb) {
        var trigger = null;
        for (var n = cb.parentElement; n && n !== document.body && !trigger; n = n.parentElement) {
            if (n.id) trigger = document.querySelector('[data-target="' + cssEsc(n.id) + '"]');
        }
        var sel = '.btn-search, .alrt-search-btn, button.search, [onclick*="onSearchClick"]';
        var start = trigger || cb;
        for (var p = start.parentElement, d = 0; p && p !== document.body && d < 8; p = p.parentElement, d++) {
            var found = Array.prototype.filter.call(p.querySelectorAll(sel), visible);
            if (found.length) return found[0];
        }
        return null;
    }

    function applyCascade(prefix, force) {
        var siteSel = 'input[type="checkbox"].' + prefix + 'cb-site';
        if (!force && document.querySelector(siteSel + ':checked')) return Promise.resolve(false);

        var zoneCb = fav.ZoneId ? cbFor(prefix, 'zone', fav.ZoneId) : null;
        var chain = Promise.resolve();

        // After ticking a level, most pages rebuild the next list via AJAX. Prefer a
        // freshly rendered row; accept the pre-existing one after a grace period
        // (pages whose lists are static never re-render it).
        function nextLevel(level, value, before) {
            var t0 = Date.now();
            return waitFor(function () {
                var el = cbFor(prefix, level, value);
                return el && (el !== before || Date.now() - t0 > 1500) ? el : null;
            }, 10000);
        }

        if (zoneCb) {
            chain = chain.then(function () {
                var oldDiv = cbFor(prefix, 'division', fav.DivisionId);
                tick(zoneCb);
                return nextLevel('division', fav.DivisionId, oldDiv);
            }).then(function (divCb) {
                var oldSite = cbFor(prefix, 'site', fav.SiteId);
                tick(divCb);
                return nextLevel('site', fav.SiteId, oldSite);
            });
        } else {
            chain = chain.then(function () {
                return waitFor(function () { return cbFor(prefix, 'site', fav.SiteId); }, 10000);
            });
        }

        return chain.then(function (siteCb) {
            if (!siteCb) return false;
            if (force) {
                // keep only the favourite ticked
                document.querySelectorAll(siteSel + ':checked').forEach(function (c) { if (c !== siteCb) c.click(); });
            }
            tick(siteCb);
            return delay(150).then(function () {
                var btn = searchButtonFor(siteCb);
                if (btn) btn.click();
                return true;
            });
        }).catch(function () { return false; });
    }

    // ── auto-apply: <select> site pickers ─────────────────────────────────
    var SELECT_RE = /site|station/i;
    var touched = typeof WeakSet !== 'undefined' ? new WeakSet() : null;
    var applied = typeof WeakSet !== 'undefined' ? new WeakSet() : null;

    document.addEventListener('change', function (e) {
        if (e.isTrusted && e.target && e.target.tagName === 'SELECT' && touched) touched.add(e.target);
    }, true);

    function siteSelects() {
        return Array.prototype.filter.call(document.querySelectorAll('select'), function (s) {
            if (s.multiple || s.closest('.modal, .e7fav-panel, [data-no-fav]')) return false;
            return SELECT_RE.test((s.id || '') + ' ' + (s.name || ''));
        });
    }

    function isDefault(sel) {
        var v = sel.value;
        return v === '' || v === '0' || v === '-1' || v === 'all' || sel.selectedIndex <= 0;
    }

    function applySelect(sel, force) {
        if (!force && ((applied && applied.has(sel)) || (touched && touched.has(sel)))) return false;
        if (sel.disabled || !visible(sel)) return false;
        var opt = sel.querySelector('option[value="' + cssEsc(fav.SiteId) + '"]');
        if (!opt) return false;
        if (applied) applied.add(sel);
        if (String(sel.value) === String(fav.SiteId)) return true;
        if (!force && !isDefault(sel)) return false;
        sel.value = String(fav.SiteId);
        sel.dispatchEvent(new Event('change', { bubbles: true }));
        try { if (window.jQuery && jQuery.fn.selectpicker && jQuery(sel).data('selectpicker')) jQuery(sel).selectpicker('refresh'); } catch (e) { }
        return true;
    }

    // ── orchestration ─────────────────────────────────────────────────────
    var cascadeDone = {};

    function urlHasSite() {
        return /[?&](siteid|site|stationid|nofav)=/i.test(location.search);
    }

    function scanOnce(force) {
        if (!fav) return;
        siteSelects().forEach(function (s) { applySelect(s, force); });
        cascadePrefixes().forEach(function (p) {
            if (!force && cascadeDone[p]) return;
            cascadeDone[p] = true;
            applyCascade(p, force);
        });
    }

    function applyAll(force) {
        if (!fav) return;
        if (force) { cascadeDone = {}; if (typeof WeakSet !== 'undefined') applied = new WeakSet(); }
        scanOnce(!!force);
    }

    function startAutoApply() {
        if (!fav || window.E7_FAV_DISABLE || urlHasSite()) return;
        // Pages build filters at different times (inline, on ready, after AJAX):
        // keep looking for a short while after load and after each click (tabs).
        var until = Date.now() + 15000;
        (function loop() {
            scanOnce(false);
            if (Date.now() < until) setTimeout(loop, 500);
        })();
        var t;
        document.addEventListener('click', function () {
            clearTimeout(t);
            t = setTimeout(function () { scanOnce(false); }, 400);
        });
    }

    function startDecorating() {
        decorateItems(document);
        if (!window.MutationObserver) return;
        var pending = false;
        new MutationObserver(function () {
            if (pending) return;
            pending = true;
            setTimeout(function () { pending = false; decorateItems(document); }, 60);
        }).observe(document.body, { childList: true, subtree: true });
    }

    // ── full-screen live SIP of the favourite site ────────────────────────
    // The SIP runs in its own page (/FRS25/Telemetry/SipLive) inside an iframe,
    // so it does not depend on the page underneath; removing the iframe on
    // close also closes its live socket. Opens only when the user presses the
    // "SIP" button in the favourite panel.
    var $sip = null, sipPrevOverflow = '';

    function sipCss() {
        if (document.getElementById('e7favSipCss')) return;
        var st = document.createElement('style');
        st.id = 'e7favSipCss';
        st.textContent =
            '.e7fav-sip{position:fixed;inset:0;z-index:10040;background:#08101c;display:flex;flex-direction:column}' +
            '.e7fav-sip iframe{flex:1;width:100%;border:0;display:block;background:#08101c}' +
            '.e7fav-sip-close{position:absolute;top:8px;right:12px;z-index:2;display:inline-flex;align-items:center;gap:6px;' +
            'padding:6px 12px;border-radius:8px;border:1px solid rgba(148,163,184,.35);background:rgba(15,23,42,.85);color:#e2e8f0;' +
            'font:600 12.5px system-ui,-apple-system,"Segoe UI",sans-serif;cursor:pointer}' +
            '.e7fav-sip-close:hover{background:#ef4444;border-color:#ef4444;color:#fff}' +
            '.e7fav-cur .e7fav-sip-btn{color:var(--e7-accent,#3a8fd6)}';
        document.head.appendChild(st);
    }

    function openSip(f, manual) {
        if (!f || !f.SiteId) return;
        if (!window.E7_CAN_SIP) {
            if (manual) toast('warn', 'SIP not available', 'You do not have access to Telemetry Live.');
            return;
        }
        sipCss();
        var src = '/FRS25/Telemetry/SipLive?siteId=' + encodeURIComponent(f.SiteId);
        if ($sip) {                                   // already open: switch site if needed
            var fr = $sip.querySelector('iframe');
            if (fr.getAttribute('src') !== src) fr.setAttribute('src', src);
            return;
        }
        $sip = document.createElement('div');
        $sip.className = 'e7fav-sip';
        $sip.setAttribute('role', 'dialog');
        $sip.setAttribute('aria-label', 'Live SIP of ' + (f.SiteName || 'favourite station'));
        $sip.innerHTML =
            '<button type="button" class="e7fav-sip-close" title="Close (Esc)"><i class="fa-solid fa-xmark"></i> Close</button>' +
            '<iframe title="Live SIP" allow="fullscreen"></iframe>';
        $sip.querySelector('iframe').setAttribute('src', src);
        $sip.querySelector('.e7fav-sip-close').addEventListener('click', closeSip);
        document.body.appendChild($sip);
        sipPrevOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        setTimeout(function () { try { $sip.querySelector('iframe').focus(); } catch (e) { } }, 50);
    }

    function closeSip() {
        if (!$sip) return;
        var fr = $sip.querySelector('iframe');
        try { fr.setAttribute('src', 'about:blank'); } catch (e) { }   // stop its socket at once
        $sip.parentNode.removeChild($sip);
        $sip = null;
        document.body.style.overflow = sipPrevOverflow;
    }

    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && $sip) closeSip(); });
    window.addEventListener('message', function (e) {
        if (e.origin === window.location.origin && e.data && e.data.type === 'e7-sip-close') closeSip();
    });

    // The SIP is never opened automatically (not at login, page load or when the
    // favourite changes): only by the "SIP" button in the panel.

    function init() {
        injectCss();
        buildHeader();
        startDecorating();
        // Let the page's own ready handlers run (they build/clear filters) first.
        setTimeout(startAutoApply, 300);
    }

    window.E7FavouriteSite = {
        get: function () { return fav; },
        set: setFavourite,
        clear: clearFavourite,
        apply: function () { applyAll(true); },
        openSip: function () { openSip(fav, true); },
        closeSip: closeSip
    };

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();
