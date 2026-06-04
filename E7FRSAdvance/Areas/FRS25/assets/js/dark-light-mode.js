/**
 * dark-light-mode.js
 * Energy7 RDPMS — Aurora Day / Night Toggle
 *
 * Single source of truth for day/night mode.
 * Sets data-aurora="dark|light" on <body> AND <html>,
 * and data-theme="dark|light" on #e7App (.e7-aurora wrapper).
 *
 * Reads/writes localStorage key: 'e7-theme-pref'
 * Default: 'dark'
 */

(function () {
    'use strict';

    var STORAGE_KEY = 'e7-theme-pref';

    /* ── Icons ── */
    var MOON_SVG = '<svg width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.808c-.5 5.347-5.849 9.14-11.107 7.983C-.078 18.6 1.15 3.909 11.11 3 6.395 9.296 14.619 17.462 21 12.808M17 5.5h3M18.5 4v3"/></svg>';
    var SUN_SVG = '<svg width="20" height="20" fill="none" stroke="currentColor" stroke-width="1.6" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>';

    /* ── Apply theme to all host elements ── */
    function applyTheme(theme) {
        // 1. body — used by aurora-bridge.css selectors
        document.body.setAttribute('data-aurora', theme);
        // 2. html — used by :root CSS var overrides
        document.documentElement.setAttribute('data-aurora', theme);
        // 3. e7App wrapper — used by e7-aurora-theme.css scoped selectors
        var wrapper = document.getElementById('e7App');
        if (wrapper) wrapper.setAttribute('data-theme', theme);

        // 4. Persist
        try { localStorage.setItem(STORAGE_KEY, theme); } catch (e) { }

        // 5. Update toggle button
        updateToggleBtn(theme);

        // 6. Notify any page-level listeners (Telemetry, etc.)
        try {
            window.dispatchEvent(new CustomEvent('e7ThemeChange', { detail: { theme: theme } }));
        } catch (e) { }
    }

    /* ── Update the toggle button icon + title ── */
    function updateToggleBtn(theme) {
        var btn = document.getElementById('toggle-btn');
        if (!btn) return;
        var isDark = (theme === 'dark');
        btn.title = isDark ? 'Switch to Day mode' : 'Switch to Night mode';
        btn.innerHTML = isDark ? SUN_SVG : MOON_SVG;
    }

    /* ── Read saved or default theme ── */
    function getSavedTheme() {
        try { return localStorage.getItem(STORAGE_KEY) || 'dark'; } catch (e) { return 'dark'; }
    }

    /* ── Initial apply (runs immediately, before DOM ready) ── */
    applyTheme(getSavedTheme());

    /* ── Wire click handler after DOM is ready ── */
    document.addEventListener('DOMContentLoaded', function () {
        var btn = document.getElementById('toggle-btn');
        if (btn) {
            btn.addEventListener('click', function () {
                var current = document.body.getAttribute('data-aurora') || 'dark';
                applyTheme(current === 'dark' ? 'light' : 'dark');
            });
        }

        // Expose globally for pages that need to react
        window.applyAuroraTheme = applyTheme;
        window.getAuroraTheme = function () {
            return document.body.getAttribute('data-aurora') || 'dark';
        };
    });

    // Also expose immediately for inline scripts in pages
    window.applyAuroraTheme = applyTheme;

})();
