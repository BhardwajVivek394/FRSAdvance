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


    /* ── Apply theme to all host elements ── */
    function applyTheme(theme, skipPersist) {
        // 1. body — used by aurora-bridge.css selectors
        document.body.setAttribute('data-aurora', theme);
        // 2. html — used by :root CSS var overrides
        document.documentElement.setAttribute('data-aurora', theme);
        document.documentElement.setAttribute('data-theme', theme);
        // 3. e7App wrapper — used by e7-aurora-theme.css scoped selectors
        var wrapper = document.getElementById('e7App');
        if (wrapper) wrapper.setAttribute('data-theme', theme);

        // 4. Persist — only an explicit choice, so a first visit keeps following the OS
        if (!skipPersist) {
            try { localStorage.setItem(STORAGE_KEY, theme); } catch (e) { }
        }

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
        // Energy7 Dashboard 2 pill: icon + the mode it switches TO ("Dark" / "Light")
        btn.title = isDark ? 'Switch to light mode' : 'Switch to dark mode';
        btn.setAttribute('aria-pressed', isDark ? 'true' : 'false');
        btn.innerHTML = isDark
            ? '<i class="fa fa-sun" aria-hidden="true"></i><span class="theme-label">Light</span>'
            : '<i class="fa fa-moon" aria-hidden="true"></i><span class="theme-label">Dark</span>';
    }

    /* ── Read saved or default theme ── */
    function getSavedTheme() {
        // No saved choice → the OS scheme (same rule as the <head> flash-prevention script)
        var fallback = (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) ? 'dark' : 'light';
        try { return localStorage.getItem(STORAGE_KEY) || fallback; } catch (e) { return fallback; }
    }

    /* ── Initial apply (runs immediately, before DOM ready) ── */
    applyTheme(getSavedTheme(), true);

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
