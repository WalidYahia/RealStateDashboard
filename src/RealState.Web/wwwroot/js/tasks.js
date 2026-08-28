// Cascade the task assignee picker from the selected department. The employee list is embedded in
// the modal as JSON (#taskEmpData); this runs on the parent page because modal inline scripts don't
// execute. Fired on department change and once on modal open (data-cascade-init).
window.taskDeptChange = function (sel) {
    var form = sel.closest('form');
    if (!form) return;
    var emp = form.querySelector('[name="AssigneeEmployeeId"]');
    var dataEl = form.querySelector('#taskEmpData');
    if (!emp || !dataEl) return;

    var all = [];
    try { all = JSON.parse(dataEl.textContent || '[]'); } catch (e) { return; }

    var dep = sel.value;
    var current = emp.getAttribute('data-selected') || emp.value || '';
    var frag = document.createDocumentFragment();
    var ph = document.createElement('option');
    ph.value = ''; ph.textContent = '— اختر الموظف —';
    frag.appendChild(ph);

    all.filter(function (e) { return !dep || e.d === dep; }).forEach(function (e) {
        var o = document.createElement('option');
        o.value = e.i; o.textContent = e.n;
        if (e.i === current) o.selected = true;
        frag.appendChild(o);
    });
    emp.innerHTML = '';
    emp.appendChild(frag);
};

// ----- Generic client-side table pagination -----
// Any <table data-paginate="N"> gets a pager beneath it showing N rows per page. Rows marked
// data-skip-page / data-skip-search (placeholders) are never paged. Runs on load.
(function () {
    function ellipsis() {
        var s = document.createElement('span');
        s.textContent = '…'; s.style.cssText = 'padding:0 6px;color:var(--muted);';
        return s;
    }
    function initPaginate(table) {
        var pageSize = parseInt(table.getAttribute('data-paginate'), 10) || 15;
        var body = table.tBodies && table.tBodies[0];
        if (!body) return;

        var pager = document.createElement('div');
        pager.className = 'rs-pager';
        pager.style.cssText = 'display:flex;gap:6px;flex-wrap:wrap;justify-content:center;align-items:center;margin-top:12px;';
        table.parentNode.insertBefore(pager, table.nextSibling);

        var current = 1;
        function dataRows() {
            return Array.prototype.slice.call(body.rows).filter(function (r) {
                return !r.hasAttribute('data-skip-page') && !r.hasAttribute('data-skip-search');
            });
        }
        function pageButton(label, page, active, disabled) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn-sm rs-page' + (active ? ' active' : '');
            b.textContent = label;
            if (disabled) b.disabled = true;
            else b.addEventListener('click', function () { current = page; render(); });
            return b;
        }
        function render() {
            var rows = dataRows();
            // Rows the search left visible (style.display is set only by the shared list-search filter).
            var eligible = rows.filter(function (r) { return r.style.display !== 'none'; });
            var pages = Math.max(1, Math.ceil(eligible.length / pageSize));
            if (current > pages) current = pages;
            if (current < 1) current = 1;
            eligible.forEach(function (r, i) {
                r.classList.toggle('pg-hidden', (Math.floor(i / pageSize) + 1) !== current);
            });
            rows.forEach(function (r) { if (r.style.display === 'none') r.classList.remove('pg-hidden'); });
            pager.innerHTML = '';
            if (pages <= 1) return;
            pager.appendChild(pageButton('«', current - 1, false, current === 1));
            var win = 2, start = Math.max(1, current - win), end = Math.min(pages, current + win);
            if (start > 1) { pager.appendChild(pageButton('1', 1, false, false)); if (start > 2) pager.appendChild(ellipsis()); }
            for (var p = start; p <= end; p++) pager.appendChild(pageButton(String(p), p, p === current, false));
            if (end < pages) { if (end < pages - 1) pager.appendChild(ellipsis()); pager.appendChild(pageButton(String(pages), pages, false, false)); }
            pager.appendChild(pageButton('»', current + 1, false, current === pages));
        }
        // Re-page when the (optional) search box changes; this runs after the shared list-search filter.
        var searchSel = table.getAttribute('data-search-input');
        var searchInput = searchSel ? document.querySelector(searchSel) : null;
        if (searchInput) searchInput.addEventListener('input', function () { current = 1; render(); });
        table.rsRepage = function () { current = 1; render(); };
        render();
    }
    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('table[data-paginate]').forEach(initPaginate);
    });
})();
