// «تقارير المخزون» tabs page (Areas/Inventory/Views/InventoryReports/Index.cshtml).
//   [data-rep-tab]    tab buttons          [data-rep-panel]  panels; data-src = the URL the panel's content loads from
// A panel loads (X-Requested-With → the report's partial) the first time its tab is opened and keeps its content.
// Its GET filter form and «مسح الفلاتر» link reload only that panel; the address bar follows (?tab=…&filters) so a
// refresh or a shared link reopens the same tab with the same filters.
(function () {
    var root = document.getElementById('repTabs');
    if (!root) return;
    var indexUrl = root.getAttribute('data-index-url') || location.pathname;

    function panelOf(key) { return root.querySelector('[data-rep-panel="' + key + '"]'); }
    function activeKey() { var b = root.querySelector('[data-rep-tab].active'); return b ? b.getAttribute('data-rep-tab') : null; }

    function setUrl(key, url) {
        if (!history.replaceState) return;
        var qs = url && url.indexOf('?') >= 0 ? url.slice(url.indexOf('?') + 1) : '';
        history.replaceState(null, '', indexUrl + '?tab=' + encodeURIComponent(key) + (qs ? '&' + qs : ''));
    }

    // Re-run what a normal page load would do for the injected markup.
    function initPanel(panel) {
        if (window.appEnhanceSearchSelect)
            panel.querySelectorAll('select[data-searchable]').forEach(function (s) { window.appEnhanceSearchSelect(s); });
        if (window.appEnhanceGrids) window.appEnhanceGrids(panel);
    }

    function load(panel, url) {
        panel.setAttribute('data-src', url);
        panel.setAttribute('aria-busy', 'true');
        panel.innerHTML = '<div class="card-tile rep-loading">جارٍ تحميل التقرير…</div>';
        return fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { if (!r.ok) throw new Error(r.status); return r.text(); })
            .then(function (html) {
                panel.innerHTML = html;
                panel.setAttribute('data-loaded', '1');
                initPanel(panel);
            })
            .catch(function () {
                panel.removeAttribute('data-loaded');
                panel.innerHTML = '<div class="card-tile rep-loading" style="color:var(--critical);">تعذّر تحميل التقرير. <a href="#" data-rep-retry>إعادة المحاولة</a></div>';
            })
            .then(function () { panel.removeAttribute('aria-busy'); });
    }

    function show(key) {
        var panel = panelOf(key); if (!panel) return;
        root.querySelectorAll('[data-rep-tab]').forEach(function (b) {
            var on = b.getAttribute('data-rep-tab') === key;
            b.classList.toggle('active', on);
            b.setAttribute('aria-selected', on ? 'true' : 'false');
        });
        root.querySelectorAll('[data-rep-panel]').forEach(function (p) { p.hidden = p !== panel; });
        if (!panel.hasAttribute('data-loaded') && !panel.hasAttribute('aria-busy')) load(panel, panel.getAttribute('data-src'));
        setUrl(key, panel.getAttribute('data-src'));
    }

    root.addEventListener('click', function (e) {
        var tab = e.target.closest('[data-rep-tab]');
        if (tab) { show(tab.getAttribute('data-rep-tab')); return; }

        var panel = e.target.closest('[data-rep-panel]');
        if (!panel) return;
        // «مسح الفلاتر» reloads just this panel with its defaults.
        var reset = e.target.closest('a.fb-reset');
        if (reset) {
            e.preventDefault();
            load(panel, reset.getAttribute('href'));
            setUrl(panel.getAttribute('data-rep-panel'), reset.getAttribute('href'));
            return;
        }
        if (e.target.closest('[data-rep-retry]')) { e.preventDefault(); load(panel, panel.getAttribute('data-src')); }
    });

    // A panel's filter form (GET) refreshes only that panel. Print / Excel links open normally.
    root.addEventListener('submit', function (e) {
        var form = e.target;
        var panel = form.closest ? form.closest('[data-rep-panel]') : null;
        if (!panel || (form.getAttribute('method') || 'get').toLowerCase() !== 'get') return;
        e.preventDefault();   // the preloader skips prevented submits; the panel shows its own loading state
        var action = form.getAttribute('action') || panel.getAttribute('data-src').split('?')[0];
        var qs = new URLSearchParams(new FormData(form)).toString();
        var url = action.split('?')[0] + (qs ? '?' + qs : '');
        load(panel, url);
        setUrl(panel.getAttribute('data-rep-panel'), url);
    });

    // Open the tab the page was asked for (?tab=…; the server already marked it active).
    show(activeKey() || root.querySelector('[data-rep-tab]').getAttribute('data-rep-tab'));
})();
