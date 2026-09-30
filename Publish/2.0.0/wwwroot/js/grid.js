// Global list-grid enhancer. For every data grid it:
//   • adds a top toolbar ABOVE the grid: inline search (start) + total count and pager (end/left),
//   • adds a leading index (#) column,
//   • applies client-side paging (15 rows/page) coordinated with the search.
// Auto-detects list grids (rs-table with a column header, inside a card, not a form/config grid).
// Opt out with data-no-grid; force/size with data-grid="N" (or the legacy data-paginate="N").
(function () {
    var DEFAULT = 15;

    function isPlaceholder(tr) {
        return tr.hasAttribute('data-skip-search') || tr.hasAttribute('data-skip-page') ||
            tr.hasAttribute('data-no-results') ||
            (tr.cells.length === 1 && (tr.cells[0].colSpan || 1) > 1);
    }

    function shouldEnhance(t) {
        if (t.__grid || t.hasAttribute('data-no-grid')) return false;
        var explicit = t.hasAttribute('data-grid') || t.hasAttribute('data-paginate');
        if (!explicit) {
            if (!t.classList.contains('rs-table')) return false;
            var head = t.tHead;
            if (!head || !head.rows.length || head.rows[0].querySelectorAll('th').length < 2) return false;
            if (!t.closest('.card-tile')) return false;
            if (t.closest('form')) return false;          // config/form grids (e.g. privileges) — leave alone
        }
        return !!(t.tHead && t.tHead.rows.length && t.tBodies && t.tBodies[0]);
    }

    function ensureId(t) {
        if (!t.id) t.id = 'grid_' + Math.random().toString(36).slice(2, 9);
        return t.id;
    }

    function findSearch(t) {
        var s = document.querySelector('.list-search[data-target="#' + t.id + '"]');
        if (s) return s;
        var card = t.closest('.card-tile');
        return card ? card.querySelector('.list-search') : null;
    }

    function buildPager(pager, current, pages, go) {
        pager.innerHTML = '';
        if (pages <= 1) return;
        function btn(label, page, opt) {
            opt = opt || {};
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn-sm rs-page' + (opt.active ? ' active' : '');
            b.textContent = label;
            if (opt.disabled) b.disabled = true; else b.addEventListener('click', function () { go(page); });
            pager.appendChild(b);
        }
        function ell() { var s = document.createElement('span'); s.textContent = '…'; s.style.cssText = 'padding:0 4px;color:var(--muted)'; pager.appendChild(s); }
        btn('«', current - 1, { disabled: current === 1 });
        var win = 2, start = Math.max(1, current - win), end = Math.min(pages, current + win);
        if (start > 1) { btn('1', 1, {}); if (start > 2) ell(); }
        for (var p = start; p <= end; p++) btn(String(p), p, { active: p === current });
        if (end < pages) { if (end < pages - 1) ell(); btn(String(pages), pages, {}); }
        btn('»', current + 1, { disabled: current === pages });
    }

    function enhance(t) {
        if (!shouldEnhance(t)) return;
        t.__grid = true;
        ensureId(t);
        var pageSize = parseInt(t.getAttribute('data-grid') || t.getAttribute('data-paginate'), 10) || DEFAULT;
        var body = t.tBodies[0];

        // --- leading index (#) column (header, body, and a blank leading footer cell to keep totals aligned) ---
        var hi = document.createElement('th'); hi.textContent = '#'; hi.className = 'grid-idx';
        t.tHead.rows[0].insertBefore(hi, t.tHead.rows[0].firstChild);
        Array.prototype.forEach.call(body.rows, function (r) {
            if (isPlaceholder(r)) { if (r.cells[0] && (r.cells[0].colSpan || 1) > 1) r.cells[0].colSpan += 1; return; }
            var td = document.createElement('td'); td.className = 'grid-idx'; r.insertBefore(td, r.firstChild);
        });
        if (t.tFoot) {
            Array.prototype.forEach.call(t.tFoot.rows, function (r) {
                var isTh = r.cells[0] && r.cells[0].tagName === 'TH';
                var cell = document.createElement(isTh ? 'th' : 'td');
                cell.className = 'grid-idx'; cell.setAttribute('data-no-sum', '');
                r.insertBefore(cell, r.firstChild);
            });
        }
        // The leading column shifts every column right by one — fix any data-live-sum indices pointing here.
        document.querySelectorAll('[data-live-sum]').forEach(function (el) {
            var parts = (el.getAttribute('data-live-sum') || '').split(':');
            if (parts[0] === '#' + t.id) {
                var col = parseInt(parts[1], 10);
                if (!isNaN(col)) el.setAttribute('data-live-sum', parts[0] + ':' + (col + 1));
            }
        });

        // --- top toolbar: search (start) + count & pager (end) ---
        var toolbar = document.createElement('div'); toolbar.className = 'grid-toolbar';
        var left = document.createElement('div'); left.className = 'grid-search';
        var search = findSearch(t);
        if (search) {
            var bar = search.closest('.list-search-bar');
            left.appendChild(search);
            if (bar && !bar.querySelector('input')) bar.remove();
        } else {
            search = document.createElement('input');
            search.type = 'text'; search.className = 'form-control list-search';
            search.setAttribute('data-target', '#' + t.id);
            search.setAttribute('placeholder', '🔍 بحث في القائمة...');
            left.appendChild(search);
        }
        var right = document.createElement('div'); right.className = 'grid-tools';
        var count = document.createElement('span'); count.className = 'grid-count';
        var pager = document.createElement('div'); pager.className = 'rs-pager';
        right.appendChild(count); right.appendChild(pager);
        toolbar.appendChild(left); toolbar.appendChild(right);

        var anchor = (t.parentElement && /auto|scroll/.test(t.parentElement.style.overflowX || '')) ? t.parentElement : t;
        anchor.parentNode.insertBefore(toolbar, anchor);

        // --- paging + search coordination (search sets style.display; we page the visible rows) ---
        var current = 1;
        function dataRows() { return Array.prototype.slice.call(body.rows).filter(function (r) { return !isPlaceholder(r); }); }
        function render() {
            var rows = dataRows();
            var eligible = rows.filter(function (r) { return r.style.display !== 'none'; });
            var pages = Math.max(1, Math.ceil(eligible.length / pageSize));
            if (current > pages) current = pages;
            if (current < 1) current = 1;
            eligible.forEach(function (r, i) {
                var onPage = Math.floor(i / pageSize) + 1 === current;
                r.classList.toggle('grid-hide', !onPage);
                if (r.cells[0] && r.cells[0].classList.contains('grid-idx')) r.cells[0].textContent = onPage ? (i + 1) : '';
            });
            rows.forEach(function (r) { if (r.style.display === 'none') r.classList.remove('grid-hide'); });
            count.textContent = 'الإجمالي: ' + eligible.length;
            buildPager(pager, current, pages, function (p) { current = p; render(); });
        }
        search.addEventListener('input', function () { current = 1; render(); });
        t.__gridRender = render;
        render();
    }

    function scan(root) {
        (root || document).querySelectorAll('table.rs-table, table[data-grid], table[data-paginate]').forEach(enhance);
    }
    document.addEventListener('DOMContentLoaded', function () { scan(document); });
    window.appEnhanceGrids = scan;
})();
