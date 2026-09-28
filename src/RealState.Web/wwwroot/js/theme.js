// Dark / day theme switch.
// The theme lives on <html data-theme="dark|light"> and is saved per browser (localStorage 'appTheme');
// the inline script in _Layout's <head> applies it before first paint, so pages never flash the wrong theme.
// Switching is soft: a circle reveal from the toggle (View Transitions API), else a short color fade.
(function () {
    var KEY = 'appTheme';
    var root = document.documentElement;
    var listeners = [];

    function current() { return root.getAttribute('data-theme') === 'light' ? 'light' : 'dark'; }
    function save(t) { try { localStorage.setItem(KEY, t); } catch (e) { /* private mode: still switches for this page */ } }

    function apply(t) {
        root.setAttribute('data-theme', t);
        listeners.forEach(function (fn) { try { fn(t); } catch (e) { } });
    }

    /** Current value of a theme token, e.g. appThemeColor('--text-secondary'). */
    window.appThemeColor = function (name) { return getComputedStyle(root).getPropertyValue(name).trim(); };

    /** Runs fn(theme) now and after every theme switch — used by charts to repaint with theme colors. */
    window.appOnTheme = function (fn) { listeners.push(fn); try { fn(current()); } catch (e) { } };

    window.appToggleTheme = function (btn) {
        var next = current() === 'light' ? 'dark' : 'light';
        save(next);
        var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (reduce) { apply(next); return; }

        if (document.startViewTransition) {
            var r = btn && btn.getBoundingClientRect ? btn.getBoundingClientRect() : null;
            var x = r ? r.left + r.width / 2 : window.innerWidth / 2;
            var y = r ? r.top + r.height / 2 : 0;
            var end = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
            var vt = document.startViewTransition(function () { apply(next); });
            vt.ready.then(function () {
                root.animate(
                    { clipPath: ['circle(0px at ' + x + 'px ' + y + 'px)', 'circle(' + end + 'px at ' + x + 'px ' + y + 'px)'] },
                    { duration: 600, easing: 'ease-in-out', pseudoElement: '::view-transition-new(root)' });
            }).catch(function () { });
            return;
        }

        // Fallback: ease every color for the length of the switch.
        root.classList.add('theme-fading');
        clearTimeout(window.__themeFadeTimer);
        window.__themeFadeTimer = setTimeout(function () { root.classList.remove('theme-fading'); }, 500);
        apply(next);
    };

    // Chart.js: axis/legend text and grid lines follow the theme on every chart after a switch. Page code
    // themes its own dataset colors through appOnTheme (see the dashboard / sales / campaigns pages).
    listeners.push(function () {
        if (!window.Chart) return;
        var ink = window.appThemeColor('--text-secondary'), grid = window.appThemeColor('--chart-grid');
        Chart.defaults.color = ink;
        Object.values(Chart.instances || {}).forEach(function (c) {
            var scales = c.options.scales || {};
            Object.keys(scales).forEach(function (k) {
                var s = scales[k];
                if (s.grid && s.grid.display !== false) s.grid.color = grid;
                if (s.ticks) s.ticks.color = ink;
            });
            c.update('none');
        });
    });

    // Keep other open tabs in step.
    window.addEventListener('storage', function (e) {
        if (e.key === KEY && (e.newValue === 'light' || e.newValue === 'dark') && e.newValue !== current()) apply(e.newValue);
    });
})();
