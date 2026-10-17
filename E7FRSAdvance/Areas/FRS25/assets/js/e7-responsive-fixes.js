/* =====================================================================
   Energy7 RDPMS — responsive behaviour fixes  (v1)
   Pairs with e7-responsive-fixes.css; loaded last in _Layout.cshtml.
   Adds behaviour only — no element is moved or re-ordered.
     1. Drawer (≤991px): dark backdrop; closes on tap-outside, Escape, page choice
     2. AI chat panel: not reachable with Tab while closed
     3. SIP Creation (<992px): "use a larger screen" notice
   ===================================================================== */
(function () {
    'use strict';

    var DRAWER_MAX = 991;
    var body = document.getElementById('e7Body');
    var sidebar = document.getElementById('e7Sidebar');
    var toggle = document.getElementById('e7MenuToggle');
    if (!body || !sidebar) return;

    function isDrawer() { return window.innerWidth <= DRAWER_MAX; }

    /* ---------- 1. Drawer backdrop + close behaviour ---------- */
    var backdrop = document.createElement('div');
    backdrop.className = 'e7-fx-backdrop';
    backdrop.setAttribute('aria-hidden', 'true');
    body.appendChild(backdrop);

    function syncDrawerState() {
        var open = isDrawer() && sidebar.classList.contains('open');
        body.classList.toggle('e7-fx-drawer-open', open);
        if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }

    function closeDrawer() {
        if (!sidebar.classList.contains('open')) return;
        sidebar.classList.remove('open');
        syncDrawerState();
    }

    // The layout's hamburger handler toggles .open; mirror it onto the backdrop
    new MutationObserver(syncDrawerState).observe(sidebar, { attributes: true, attributeFilter: ['class'] });

    backdrop.addEventListener('click', closeDrawer);

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && isDrawer() && sidebar.classList.contains('open')) {
            closeDrawer();
            if (toggle) toggle.focus();
        }
    });

    // Choosing a page closes the drawer; parent items (submenu toggles, href="#") keep it open
    sidebar.addEventListener('click', function (e) {
        var link = e.target.closest ? e.target.closest('a.e7-nav-item, a.e7-sidebar-user') : null;
        if (!link || !isDrawer()) return;
        if (link.classList.contains('has-sub') || link.getAttribute('href') === '#') return;
        closeDrawer();
    });

    // Rotating a tablet to desktop width must not leave the backdrop up
    window.addEventListener('resize', syncDrawerState);
    syncDrawerState();

    /* ---------- 2. Closed AI panel is hidden from keyboard / screen readers ---------- */
    function bindAiPanel() {
        var panel = document.getElementById('aiChatPanel');
        if (!panel) return false;
        var sync = function () {
            var open = panel.classList.contains('open');
            if (open) {
                panel.removeAttribute('inert');
                panel.removeAttribute('aria-hidden');
            } else {
                panel.setAttribute('inert', '');
                panel.setAttribute('aria-hidden', 'true');
            }
        };
        new MutationObserver(sync).observe(panel, { attributes: true, attributeFilter: ['class'] });
        sync();
        return true;
    }

    /* ---------- 3. SIP Creation: desktop/tablet only ---------- */
    function bindSipNotice() {
        var editor = document.querySelector('.e7-body .sip-app');   // Sip/Index.cshtml editor root
        if (!editor || document.getElementById('e7FxSipNotice')) return;
        var note = document.createElement('div');
        note.id = 'e7FxSipNotice';
        note.className = 'e7-fx-sip-notice';
        note.setAttribute('role', 'note');
        note.innerHTML = '<i class="fas fa-desktop" aria-hidden="true"></i>' +
            '<div><b>Use a larger screen</b><span>The SIP drawing editor needs a screen at least 992px wide. ' +
            'Open this page on a tablet in landscape, a laptop or a desktop.</span></div>';
        editor.parentNode.insertBefore(note, editor);
    }

    function onReady() {
        // ai-chat-sidebar.js builds the panel on DOMContentLoaded; retry briefly if it isn't there yet
        if (!bindAiPanel()) {
            var tries = 0;
            var t = setInterval(function () { if (bindAiPanel() || ++tries > 20) clearInterval(t); }, 250);
        }
        bindSipNotice();
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', onReady);
    else onReady();
})();
