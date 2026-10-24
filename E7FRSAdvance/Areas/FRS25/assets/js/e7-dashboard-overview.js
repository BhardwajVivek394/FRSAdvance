/* ============================================================
   e7-dashboard-overview.js
   Energy7 RDPMS — Dashboard "Network Overview" (Option C)

   Draws the hero KPIs, the 24 h open-alert chart, "where the issues are"
   (zones / divisions ranked, click a zone to filter), the asset health
   table (problem types first, healthy ones folded) and the "needs
   attention" list (failures / predictive tabs) on FRS25/Home/Index.

   DATA: nothing new is fetched. Index.cshtml calls
       e7Overview.update(alerts, assetData)
   from fnBindWithOutAcknowledgementAlert's success handler, i.e. with the
   same GetWithOutAcknowledgementAlert rows (one per asset + alert type)
   and GetAssetList counts that the existing cards and donuts use, on the
   same triggers: first load, Search / station tab, and live alertEvents.

   Page globals it reads (all defined in Index.cshtml): alertPredictive,
   alertFail, assetTypes, getValues, activeTabSiteId, _siteNameById,
   openAlertModal.
   ============================================================ */
(function (window, $) {
    'use strict';
    if (!$) return;


    /* ---------- helpers ---------- */

    function esc(s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function fmt(n) { return (n || 0).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ','); }

    function pctText(v) { return v >= 99.95 ? '100' : v.toFixed(1); }

    // ASP.NET MVC JSON dates come as "/Date(1729751234000)/"; ISO strings also accepted.
    function toDate(v) {
        if (!v) return null;
        if (v instanceof Date) return v;
        var m = /\/Date\((-?\d+)/.exec(String(v));
        if (m) return new Date(parseInt(m[1], 10));
        var d = new Date(v);
        return isNaN(d.getTime()) ? null : d;
    }

    function ago(d) {
        if (!d) return '';
        var s = Math.max(0, (Date.now() - d.getTime()) / 1000);
        if (s < 60) return 'just now';
        if (s < 3600) return Math.floor(s / 60) + ' min ago';
        if (s < 86400) return Math.floor(s / 3600) + ' h ago';
        return Math.floor(s / 86400) + ' d ago';
    }

    function pad(n) { return (n < 10 ? '0' : '') + n; }

    // healthy-rate bar colour: >= 95 % green, >= 85 % amber, below that red
    function rateClass(r) { return r >= 95 ? 'is-ok' : (r >= 85 ? 'is-warn' : 'is-bad'); }

    // "North Western Railway" -> "North Western" for the narrow zone column (full name kept as a tooltip)
    function shortZone(z) { return String(z).replace(/\s+Railways?$/i, ''); }

    function isPred(a) { return String(a.AlertTypeId) === String(window.alertPredictive); }
    function isFail(a) { return String(a.AlertTypeId) === String(window.alertFail); }

    function typeName(a) {
        if (a.AssetType) return a.AssetType;
        var t = (window.assetTypes || []).find(function (x) { return String(x.Id) === String(a.AssetTypeId); });
        return t ? t.Name : 'Asset';
    }

    function where(a) {
        var site = a.SiteName || a.StationCode || '';
        var div = a.DivisionName || '';
        return [site, div].filter(Boolean).join(' · ');
    }

    function message(a) {
        var m = a.SetAlertMessage || a.Description || '';
        m = String(m).replace(/\s+/g, ' ').trim();
        return m.length > 140 ? m.slice(0, 137) + '…' : m;
    }

    /* ---------- scope line ---------- */

    function scopeText() {
        var parts = [];
        var z = window.getValues ? getValues('zone') : [];
        var d = window.getValues ? getValues('division') : [];
        var s = window.getValues ? getValues('site') : [];
        parts.push(z.length ? ($('#txtZone').text() || (z.length + ' zones')) : 'All zones');
        parts.push(d.length ? ($('#txtDivision').text() || (d.length + ' divisions')) : 'All divisions');
        if (window.activeTabSiteId && window._siteNameById && _siteNameById[String(activeTabSiteId)]) {
            parts.push(_siteNameById[String(activeTabSiteId)]);
        } else {
            parts.push(s.length ? ($('#txtSite').text() || (s.length + ' stations')) : 'All stations');
        }
        return parts.join(' · ');
    }

    /* ---------- model ---------- */

    function build(alerts, assetData) {
        alerts = alerts || [];
        var types = window.assetTypes || [];
        var countByType = {};
        (assetData || []).forEach(function (r) { countByType[String(r.AssetTypeId)] = r.Count || 0; });

        var byType = {};
        types.forEach(function (t) { byType[String(t.Id)] = { id: t.Id, name: t.Name, total: countByType[String(t.Id)] || 0, p: {}, f: {} }; });
        alerts.forEach(function (a) {
            var k = String(a.AssetTypeId);
            if (!byType[k]) return;
            if (isPred(a)) byType[k].p[a.AssetId] = 1;
            else if (isFail(a)) byType[k].f[a.AssetId] = 1;
        });

        var tot = { total: 0, p: 0, f: 0 };
        Object.keys(byType).forEach(function (k) {
            var t = byType[k];
            t.pn = Object.keys(t.p).length;
            t.fn = Object.keys(t.f).length;
            t.healthy = Math.max(0, t.total - t.pn - t.fn);   // same rule as BindGraph
            tot.total += t.total; tot.p += t.pn; tot.f += t.fn;
        });
        tot.healthy = Math.max(0, tot.total - tot.p - tot.f);
        return { alerts: alerts, byType: byType, tot: tot };
    }

    /* ---------- hero ---------- */

    function renderHero(m) {
        var t = m.tot;
        var withAssets = Object.keys(m.byType).filter(function (k) { return m.byType[k].total > 0; }).length;
        var since = Date.now() - 24 * 3600 * 1000;
        var newFail = m.alerts.filter(function (a) { var d = toDate(a.SetTimeStamp); return isFail(a) && d && d.getTime() >= since; }).length;
        var newPred = m.alerts.filter(function (a) { var d = toDate(a.SetTimeStamp); return isPred(a) && d && d.getTime() >= since; }).length;

        $('#ovKpiTotal').text(fmt(t.total));
        $('#ovKpiTotalSub').text('Across ' + withAssets + ' asset type' + (withAssets === 1 ? '' : 's'));
        $('#ovKpiHealthy').text(fmt(t.healthy));
        $('#ovKpiHealthySub').text(t.total ? pctText(t.healthy * 100 / t.total) + '% of fleet' : 'No assets in scope');
        $('#ovKpiPred').text(fmt(t.p));
        $('#ovKpiPredSub').text(newPred ? '▲ ' + newPred + ' in last 24 h' : 'None in last 24 h');
        $('#ovKpiFail').text(fmt(t.f));
        $('#ovKpiFailSub').text(newFail ? '▲ ' + newFail + ' in last 24 h' : 'None in last 24 h');

        $('#ovScopeText').text(scopeText());
        var now = new Date();
        $('#ovUpdated').html('<i class="fa fa-rotate-right" aria-hidden="true"></i> Updated ' + pad(now.getHours()) + ':' + pad(now.getMinutes()));

        // 24 hourly buckets, oldest first; the last bucket is the current hour
        var P = [], F = [], i;
        for (i = 0; i < 24; i++) { P.push(0); F.push(0); }
        var end = Date.now();
        m.alerts.forEach(function (a) {
            var d = toDate(a.SetTimeStamp); if (!d) return;
            var hAgo = Math.floor((end - d.getTime()) / 3600000);
            if (hAgo < 0 || hAgo > 23) return;
            var idx = 23 - hAgo;
            if (isFail(a)) F[idx]++; else if (isPred(a)) P[idx]++;
        });
        var max = 1;
        for (i = 0; i < 24; i++) max = Math.max(max, P[i] + F[i]);
        var html = '';
        for (i = 0; i < 24; i++) {
            var ph = P[i] ? Math.max(4, Math.round(P[i] / max * 100)) : 0;
            var fh = F[i] ? Math.max(4, Math.round(F[i] / max * 100)) : 0;
            var label = (23 - i === 0 ? 'This hour' : (23 - i) + ' h ago') + ': ' + P[i] + ' predictive, ' + F[i] + ' failure';
            html += '<div class="ov-bar-col" title="' + esc(label) + '">' +
                (fh ? '<span class="ov-bar-f" style="height:' + fh + '%"></span>' : '') +
                (ph ? '<span class="ov-bar-p" style="height:' + ph + '%"></span>' : '') +
                (!ph && !fh ? '<span class="ov-bar-0"></span>' : '') +
                '</div>';
        }
        $('#ovActivityBars').html(html);
    }

    /* ---------- asset table: types with open alerts first, healthy ones folded ---------- */

    var _showAllRows = false;

    function renderTable(m) {
        var $box = $('#assetTypeCardsContainer');
        var issue = [], clear = [];
        Object.keys(m.byType).forEach(function (k) {
            var t = m.byType[k];
            var $row = $box.find('.asset-type-card[data-asset-type-id="' + k + '"]');
            if (!$row.length) return;
            var rate = t.total ? t.healthy * 100 / t.total : 0;
            $row.find('[data-ov="h"]').text(fmt(t.healthy));
            $row.find('[data-ov="p"]').text(fmt(t.pn)).toggleClass('is-warn', t.pn > 0);
            $row.find('[data-ov="f"]').text(fmt(t.fn)).toggleClass('is-bad', t.fn > 0);
            $row.find('[data-ov="pct"]').text(t.total ? pctText(rate) + '%' : '—');
            $row.find('[data-ov="bar"]').css('width', (t.total ? rate : 0) + '%')
                .attr('class', rateClass(rate));
            $row.toggleClass('ov-row-empty', !t.total);
            $row.attr('aria-label', t.name + ': ' + t.total + ' total, ' + t.healthy + ' healthy, ' + t.pn + ' predictive, ' + t.fn + ' failed');
            (t.pn + t.fn > 0 ? issue : clear).push({ t: t, $row: $row });
        });

        // problem types first (most failures, then most predictive); the rest keep server order
        issue.sort(function (a, b) { return (b.t.fn - a.t.fn) || (b.t.pn - a.t.pn) || (b.t.total - a.t.total); });
        var $foot = $box.find('#ovMoreRows');
        issue.concat(clear).forEach(function (r) { r.$row.insertBefore($foot); });
        issue.forEach(function (r) { r.$row.removeClass('ov-row-folded'); });
        clear.forEach(function (r) { r.$row.toggleClass('ov-row-folded', !_showAllRows); });

        var clearAssets = clear.reduce(function (n, r) { return n + r.t.total; }, 0);
        if (clear.length) {
            $foot.prop('hidden', false).attr('aria-expanded', _showAllRows ? 'true' : 'false').html(_showAllRows
                ? '<i class="fa fa-chevron-up" aria-hidden="true"></i> Hide the ' + clear.length + ' healthy type' + (clear.length === 1 ? '' : 's')
                : '<i class="fa fa-circle-check ov-ok-ic" aria-hidden="true"></i> ' + clear.length + ' more type' + (clear.length === 1 ? '' : 's') +
                  ' with no open alerts · ' + fmt(clearAssets) + ' assets <span>Show</span>');
        } else {
            $foot.prop('hidden', true);
        }
        $('#ovAssetSub').text(issue.length
            ? issue.length + ' of ' + (issue.length + clear.length) + ' types have open alerts · click a row for its alerts'
            : 'No open alerts in this scope · click a row for its history');

        var T = m.tot;
        $('#ovSumTotal').text(fmt(T.total));
        $('#ovSumHealthy').text(fmt(T.healthy));
        $('#ovSumPred').text(fmt(T.p));
        $('#ovSumFail').text(fmt(T.f));
        $('#ovSumPct').text(T.total ? pctText(T.healthy * 100 / T.total) + '%' : '—');
    }

    $(document).on('click', '#ovMoreRows', function () {
        _showAllRows = !_showAllRows;
        if (_last) renderTable(_last);
    });

    /* ---------- where the issues are: zones / divisions ranked ---------- */

    var _group = 'zone';
    var MAX_PLACES = 7;

    function shortType(n) {
        return String(n || '').replace(/\s*circuit$/i, '').replace(/^Main\s+/i, '').replace(/\s+Machine$/i, ' M/c');
    }

    function zoneIdByName(name) {
        var id = null;
        $('#listZone .dropdown-item').each(function () {
            if (String($(this).data('text')) === String(name)) { id = String($(this).data('value')); return false; }
        });
        return id;
    }

    function renderWhere(m) {
        var issues = m.alerts.filter(function (a) { return isPred(a) || isFail(a); });
        var $w = $('#ovWhere');
        if (!issues.length) {
            $w.html('<div class="ov-allclear"><i class="fa fa-circle-check" aria-hidden="true"></i><div><b>No open alerts</b><span>Every monitored asset in ' + esc(scopeText()) + ' is healthy.</span></div></div>');
            $('#ovWhereSub').text('Affected assets with an open alert');
            return;
        }

        var key = _group === 'zone' ? 'ZoneName' : 'DivisionName';
        var g = {};
        issues.forEach(function (a) {
            var n = a[key] || 'Unassigned';
            var r = g[n] = g[n] || { name: n, zoneId: a.ZoneId, zone: a.ZoneName || '', f: {}, p: {}, types: {} };
            r[isFail(a) ? 'f' : 'p'][a.AssetId] = 1;
            var tk = String(a.AssetTypeId);
            r.types[tk] = r.types[tk] || { name: typeName(a), n: {} };
            r.types[tk].n[a.AssetId] = 1;
        });
        var rows = Object.keys(g).map(function (k) {
            var r = g[k];
            r.fn = Object.keys(r.f).length; r.pn = Object.keys(r.p).length;
            r.top = Object.keys(r.types).map(function (t) { return { name: r.types[t].name, n: Object.keys(r.types[t].n).length }; })
                .sort(function (a, b) { return b.n - a.n; }).slice(0, 3);
            return r;
        }).sort(function (a, b) { return (b.fn - a.fn) || (b.pn - a.pn); });

        var max = Math.max.apply(null, rows.map(function (r) { return r.fn + r.pn; })) || 1;
        var shown = rows.slice(0, MAX_PLACES);
        var html = shown.map(function (r) {
            var drill = _group === 'zone' && (r.zoneId || zoneIdByName(r.name));
            var label = _group === 'zone' ? shortZone(r.name) : r.name;
            return '<div class="ov-place' + (drill ? ' is-link' : '') + '"' +
                (drill ? ' role="button" tabindex="0" data-zone-id="' + esc(r.zoneId || zoneIdByName(r.name)) + '" title="Show only ' + esc(r.name) + '"' : '') + '>' +
                '<div class="ov-place-name"><b title="' + esc(r.name) + '">' + esc(label) + '</b>' +
                (_group === 'division' && r.zone ? '<small>' + esc(shortZone(r.zone)) + '</small>' : '') + '</div>' +
                '<div class="ov-place-mid">' +
                '<div class="ov-place-bar"><span class="f" style="width:' + (r.fn / max * 100) + '%"></span><span class="p" style="width:' + (r.pn / max * 100) + '%"></span></div>' +
                '<div class="ov-place-types">' + r.top.map(function (t) { return '<span>' + esc(shortType(t.name)) + ' <b>' + t.n + '</b></span>'; }).join('') + '</div>' +
                '</div>' +
                '<div class="ov-place-num"><b class="t-bad">' + r.fn + '</b><small>failure</small></div>' +
                '<div class="ov-place-num"><b class="t-warn">' + r.pn + '</b><small>predictive</small></div>' +
                (drill ? '<i class="fa fa-chevron-right ov-place-go" aria-hidden="true"></i>' : '<span class="ov-place-go"></span>') +
                '</div>';
        }).join('');
        if (rows.length > MAX_PLACES) {
            var rest = rows.slice(MAX_PLACES), rf = 0, rp = 0;
            rest.forEach(function (r) { rf += r.fn; rp += r.pn; });
            html += '<div class="ov-place-rest">+ ' + rest.length + ' more ' + (_group === 'zone' ? 'zones' : 'divisions') + ' · ' + rf + ' failure · ' + rp + ' predictive</div>';
        }
        $w.html(html);
        $('#ovWhereSub').text(rows.length + ' ' + (_group === 'zone' ? 'zone' : 'division') + (rows.length === 1 ? '' : 's') +
            ' with open alerts' + (_group === 'zone' ? ' · click a zone to filter the dashboard' : ''));
    }

    $(document).on('click', '.ov-seg[data-role="group"] button', function () {
        var g = $(this).data('group'); if (!g) return;
        _group = g;
        $(this).addClass('on').attr('aria-selected', 'true').siblings().removeClass('on').attr('aria-selected', 'false');
        if (_last) renderWhere(_last);
    });

    // zone drill-down: the same path as ticking the zone in the filter and pressing Search
    function drillZone(zoneId) {
        if (!zoneId || typeof window.onSearchClick !== 'function') return;
        $('.cb-zone').each(function () {
            var on = String(this.value) === String(zoneId);
            $(this).prop('checked', on).closest('.dropdown-item').toggleClass('checked', on);
        });
        if (window.updateSelectAllState) updateSelectAllState('zone');
        if (window.updateText) updateText('zone');
        if (window.onFilterChange) onFilterChange('zone');
        window.onSearchClick(document.querySelector('.ov-hero .search'));
        var hero = document.getElementById('ovHero');
        if (hero && hero.scrollIntoView) hero.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    /* ---------- needs attention: failures / predictive tabs ---------- */

    var _kind = 'f';
    var MAX_ITEMS = 4;

    function itemHtml(a, kind) {
        var d = toDate(a.SetTimeStamp);
        var code = a.AssetName || a.Code || '';
        return '<div class="ov-item" role="button" tabindex="0" data-type="' + esc(a.AssetTypeId) + '" data-type-name="' + esc(typeName(a)) + '">' +
            '<span class="ov-item-dot ' + (kind === 'f' ? 'is-bad' : 'is-warn') + '" aria-hidden="true"></span>' +
            '<div class="ov-item-body">' +
            '<div class="ov-item-top"><span class="ov-item-title">' + esc(code || typeName(a)) + '</span>' +
            '<span class="ov-item-code">' + esc(typeName(a)) + '</span>' +
            '<span class="ov-item-time" title="' + (d ? esc(d.toLocaleString()) : '') + '">' + esc(ago(d)) + '</span></div>' +
            (message(a) ? '<div class="ov-item-msg">' + esc(message(a)) + '</div>' : '') +
            '<div class="ov-item-where">' + esc(where(a)) + '</div>' +
            '</div></div>';
    }

    function newestFirst(x, y) {
        var a = toDate(x.SetTimeStamp), b = toDate(y.SetTimeStamp);
        return (b ? b.getTime() : 0) - (a ? a.getTime() : 0);
    }

    function renderRail(m) {
        var fails = m.alerts.filter(isFail).sort(newestFirst);
        var preds = m.alerts.filter(isPred).sort(newestFirst);
        $('#ovAttCount').text(fmt(fails.length));
        $('#ovWatchCount').text(fmt(preds.length));
        var list = _kind === 'f' ? fails : preds;
        $('#ovAttention').html(list.length
            ? list.slice(0, MAX_ITEMS).map(function (a) { return itemHtml(a, _kind); }).join('') +
              (list.length > MAX_ITEMS ? '<div class="ov-list-more">+ ' + fmt(list.length - MAX_ITEMS) + ' more in the alert centre</div>' : '')
            : '<div class="ov-empty ov-empty-ok"><i class="fa fa-circle-check" aria-hidden="true"></i> No ' + (_kind === 'f' ? 'active failures' : 'predictive warnings') + ' in this scope.</div>');
    }

    $(document).on('click', '.ov-seg-alert button', function () {
        _kind = $(this).data('kind');
        $(this).addClass('on').attr('aria-selected', 'true').siblings().removeClass('on').attr('aria-selected', 'false');
        if (_last) renderRail(_last);
    });

    /* ---------- clicks + keyboard ---------- */

    function openFor(el) {
        var id = $(el).data('type'), name = $(el).data('type-name');
        if (id != null && typeof window.openAlertModal === 'function') window.openAlertModal(id, name || 'Alerts');
    }

    $(document).on('click', '#ovAttention .ov-item', function () { openFor(this); });
    $(document).on('click', '#ovWhere .ov-place.is-link', function () { drillZone($(this).data('zone-id')); });
    $(document).on('keydown', '#ovAttention .ov-item, #ovWhere .ov-place.is-link, #assetTypeCardsContainer .asset-type-card', function (e) {
        if (e.key !== 'Enter' && e.key !== ' ') return;
        e.preventDefault();
        $(this).trigger('click');
    });

    // opening a folded chart section: let ECharts measure its now-visible boxes
    // ('toggle' does not bubble, so listen in the capture phase)
    document.addEventListener('toggle', function (e) {
        var d = e.target;
        if (d && d.classList && d.classList.contains('ov-more') && d.open && typeof window.resizeDashboardCharts === 'function') {
            setTimeout(function () { try { window.resizeDashboardCharts(); } catch (x) { } }, 30);
        }
    }, true);

    /* ---------- public ---------- */

    var _last = null;

    window.e7Overview = {
        update: function (alerts, assetData) {
            try {
                var m = build(alerts, assetData);
                _last = m;
                renderHero(m);
                renderTable(m);
                renderWhere(m);
                renderRail(m);
            } catch (e) {
                if (window.console) console.error('[e7Overview]', e);
            }
        }
    };
})(window, window.jQuery);
