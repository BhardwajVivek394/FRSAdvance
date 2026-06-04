/**
 * e7-aurora-theme.js
 * Energy7 RDPMS — Aurora Glass Theme Helper
 *
 * Provides:
 *  - Auto day/night detection with manual override
 *  - Theme toggle button builder
 *  - Toast notification helper (e7Toast)
 *  - Persistent preference via localStorage
 *
 * Usage:
 *   // Auto-init on all .e7-aurora wrappers:
 *   E7Theme.init();
 *
 *   // Build a toggle button and append it somewhere:
 *   var btn = E7Theme.buildToggleBtn();
 *   document.getElementById('myToolbar').appendChild(btn);
 *
 *   // Show a toast:
 *   E7Theme.toast('ok',   'Saved',   'Changes saved successfully.');
 *   E7Theme.toast('warn', 'Warning', 'Something to check.');
 *   E7Theme.toast('bad',  'Error',   'Something went wrong.');
 *   E7Theme.toast('info', 'Info',    'Just letting you know.');
 */

var E7Theme = (function () {
    'use strict';

    var STORAGE_KEY = 'e7-theme-pref';
    var ICONS = { dark: 'fa-moon', light: 'fa-sun' };

    // ── Detect effective theme ──────────────────────────────────
    function getSystemTheme() {
        return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches
            ? 'dark' : 'light';
    }

    function getSavedTheme() {
        try { return localStorage.getItem(STORAGE_KEY); } catch (e) { return null; }
    }

    function saveTheme(theme) {
        try { localStorage.setItem(STORAGE_KEY, theme); } catch (e) {}
    }

    function effectiveTheme(wrapper) {
        var forced = wrapper ? wrapper.getAttribute('data-theme') : null;
        if (forced === 'dark' || forced === 'light') return forced;
        var saved = getSavedTheme();
        if (saved === 'dark' || saved === 'light') return saved;
        return getSystemTheme();
    }

    // ── Apply theme to a wrapper ────────────────────────────────
    function applyTheme(wrapper, theme) {
        wrapper.setAttribute('data-theme', theme);
        saveTheme(theme);
        // update any toggle buttons inside this wrapper
        wrapper.querySelectorAll('.e7-theme-toggle').forEach(function (btn) {
            var icon = btn.querySelector('i');
            var lbl  = btn.querySelector('.e7-theme-lbl');
            if (icon) {
                icon.className = 'fas ' + (theme === 'dark' ? ICONS.light : ICONS.dark);
            }
            if (lbl) lbl.textContent = theme === 'dark' ? 'Light Mode' : 'Dark Mode';
            btn.setAttribute('data-current', theme);
        });
    }

    function toggleTheme(wrapper) {
        var current = wrapper.getAttribute('data-theme') || getSavedTheme() || getSystemTheme();
        applyTheme(wrapper, current === 'dark' ? 'light' : 'dark');
    }

    // ── Build toggle button ────────────────────────────────────
    function buildToggleBtn(wrapper) {
        var btn = document.createElement('button');
        btn.className = 'e7-btn e7-btn-icon e7-theme-toggle';
        btn.title = 'Toggle Day / Night';
        btn.style.cssText = 'position:relative;';

        var theme = effectiveTheme(wrapper);
        var iconCls = theme === 'dark' ? ICONS.light : ICONS.dark;
        btn.innerHTML =
            '<i class="fas ' + iconCls + '"></i>' +
            '<span class="e7-theme-lbl" style="display:none;">' +
            (theme === 'dark' ? 'Light Mode' : 'Dark Mode') +
            '</span>';

        btn.setAttribute('data-current', theme);

        btn.addEventListener('click', function () {
            if (wrapper) {
                toggleTheme(wrapper);
            } else {
                // apply to all wrappers on the page
                document.querySelectorAll('.e7-aurora').forEach(function (w) {
                    toggleTheme(w);
                });
            }
        });
        return btn;
    }

    // ── Build a pill toggle (icon + label) ─────────────────────
    function buildTogglePill(wrapper) {
        var pill = document.createElement('div');
        pill.className = 'e7-theme-pill';
        pill.style.cssText =
            'display:flex;align-items:center;gap:4px;' +
            'background:var(--e7-g2);border:1px solid var(--e7-edge);' +
            'border-radius:999px;padding:3px;';

        ['dark','light'].forEach(function (t) {
            var btn = document.createElement('button');
            btn.dataset.t = t;
            btn.style.cssText =
                'width:30px;height:30px;border-radius:50%;' +
                'display:grid;place-items:center;font-size:12px;' +
                'color:var(--e7-t3);transition:all var(--e7-fast);cursor:pointer;' +
                'background:none;border:none;';
            btn.innerHTML = '<i class="fas ' + (t === 'dark' ? ICONS.dark : ICONS.light) + '"></i>';
            btn.addEventListener('click', function () {
                if (wrapper) {
                    applyTheme(wrapper, t);
                } else {
                    document.querySelectorAll('.e7-aurora').forEach(function (w) { applyTheme(w, t); });
                }
                updatePill();
            });
            pill.appendChild(btn);
        });

        function updatePill() {
            var cur = wrapper
                ? (wrapper.getAttribute('data-theme') || getSavedTheme() || getSystemTheme())
                : (getSavedTheme() || getSystemTheme());
            pill.querySelectorAll('button').forEach(function (b) {
                var active = b.dataset.t === cur;
                b.style.background = active
                    ? 'linear-gradient(135deg,#22d3ee,#a78bfa)'
                    : 'none';
                b.style.color = active ? 'white' : 'var(--e7-t3)';
                b.style.boxShadow = active ? '0 2px 8px rgba(34,211,238,0.45)' : 'none';
            });
        }
        updatePill();
        return pill;
    }

    // ── Init ───────────────────────────────────────────────────
    function init() {
        document.querySelectorAll('.e7-aurora').forEach(function (wrapper) {
            // Apply saved or system theme
            var theme = getSavedTheme() || getSystemTheme();
            applyTheme(wrapper, theme);

            // Listen for clicks on any .e7-theme-toggle already in the DOM
            wrapper.addEventListener('click', function (e) {
                var btn = e.target.closest('.e7-theme-toggle');
                if (btn) toggleTheme(wrapper);
            });
        });

        // Watch system theme changes
        if (window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function (e) {
                if (!getSavedTheme()) {
                    // No manual override — follow the system
                    var theme = e.matches ? 'dark' : 'light';
                    document.querySelectorAll('.e7-aurora').forEach(function (w) {
                        if (!w.getAttribute('data-theme')) {
                            applyTheme(w, theme);
                        }
                    });
                }
            });
        }
    }

    // ── Toast ──────────────────────────────────────────────────
    var _toastWrap = null;
    function ensureToastWrap() {
        if (_toastWrap && document.body.contains(_toastWrap)) return _toastWrap;
        _toastWrap = document.createElement('div');
        _toastWrap.className = 'e7-toast-wrap';
        document.body.appendChild(_toastWrap);
        return _toastWrap;
    }

    var TOAST_ICONS = { ok: 'fa-check-circle', warn: 'fa-exclamation-triangle', bad: 'fa-times-circle', info: 'fa-info-circle' };

    function toast(type, title, message, duration) {
        duration = duration || 4000;
        var wrap = ensureToastWrap();
        var t = document.createElement('div');
        t.className = 'e7-toast ' + (type || 'info');
        t.innerHTML =
            '<div class="e7-toast-icon"><i class="fas ' + (TOAST_ICONS[type] || 'fa-info-circle') + '"></i></div>' +
            '<div class="e7-toast-body">' +
            '<div class="e7-toast-title">' + (title || '') + '</div>' +
            (message ? '<div class="e7-toast-msg">' + message + '</div>' : '') +
            '</div>' +
            '<button class="e7-toast-close" aria-label="Close"><i class="fas fa-times"></i></button>';

        t.querySelector('.e7-toast-close').addEventListener('click', function () { dismiss(t); });
        wrap.appendChild(t);
        var timer = setTimeout(function () { dismiss(t); }, duration);
        t._timer = timer;
        return t;
    }

    function dismiss(el) {
        if (!el || !el.parentNode) return;
        clearTimeout(el._timer);
        el.style.transition = 'opacity .25s ease, transform .25s ease';
        el.style.opacity = '0';
        el.style.transform = 'translateX(20px) scale(0.95)';
        setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 280);
    }

    // Public API
    return {
        init:            init,
        applyTheme:      applyTheme,
        toggleTheme:     toggleTheme,
        buildToggleBtn:  buildToggleBtn,
        buildTogglePill: buildTogglePill,
        toast:           toast,
        getSystemTheme:  getSystemTheme,
        getSavedTheme:   getSavedTheme
    };
})();

// Auto-init when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', function () { E7Theme.init(); });
} else {
    E7Theme.init();
}
