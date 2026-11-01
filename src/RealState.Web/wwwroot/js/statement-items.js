// كشف الحساب «تفصيلي» (customer / supplier statements): each invoice / return row ([data-items]) is followed by a row of
// its lines (Views/Shared/_StatementItemsRow). The row's «الأصناف» button opens / closes its own lines; the list's
// «تفصيلي» checkbox ([data-items-all="#table"]) opens / closes them all, and is carried into the print link and the
// filter form (detailed=true). A lines row shows only while its document row does (list search / grid paging).
(function () {
    function linesOf(tr) {
        var next = tr.nextElementSibling;
        return next && next.classList.contains('stmt-items') ? next : null;
    }

    function sync(table) {
        table.querySelectorAll('tr[data-items]').forEach(function (tr) {
            var lines = linesOf(tr);
            if (!lines) return;
            var open = tr.getAttribute('data-open') === '1';
            var shown = tr.style.display !== 'none' && !tr.classList.contains('grid-hide');
            lines.hidden = !(open && shown);
            var btn = tr.querySelector('[data-items-toggle]');
            if (btn) btn.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    }

    // detailed=true on the print link(s) and in the filter form, so printing and re-filtering keep the choice.
    function carry(box, on) {
        var key = box.getAttribute('data-print-key');
        if (key) {
            document.querySelectorAll('a[href*="' + key + '"]').forEach(function (a) {
                var u = new URL(a.getAttribute('href'), location.href);
                if (on) u.searchParams.set('detailed', 'true'); else u.searchParams.delete('detailed');
                a.setAttribute('href', u.pathname + u.search);
            });
        }
        document.querySelectorAll('form.fb-form').forEach(function (f) {
            var h = f.querySelector('input[name="detailed"]');
            if (on && !h) { h = document.createElement('input'); h.type = 'hidden'; h.name = 'detailed'; h.value = 'true'; f.appendChild(h); }
            if (!on && h) h.remove();
        });
    }

    function init(box) {
        var table = document.querySelector(box.getAttribute('data-items-all'));
        if (!table) return;
        var rows = table.querySelectorAll('tr[data-items]');
        if (rows.length === 0) { var wrap = box.closest('.stmt-detail-toggle'); if (wrap) wrap.hidden = true; }

        // The list checkbox: checked = all open, unchecked = all closed, indeterminate = some.
        function reflect() {
            var open = table.querySelectorAll('tr[data-items][data-open="1"]').length;
            box.indeterminate = open > 0 && open < rows.length;
        }

        box.addEventListener('change', function () {
            rows.forEach(function (tr) { tr.setAttribute('data-open', box.checked ? '1' : '0'); });
            box.indeterminate = false;
            carry(box, box.checked);
            sync(table);
        });
        table.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-items-toggle]');
            if (!btn) return;
            var tr = btn.closest('tr[data-items]');
            tr.setAttribute('data-open', tr.getAttribute('data-open') === '1' ? '0' : '1');
            reflect();
            sync(table);
        });
        // Search hides rows (style.display) and paging hides them (.grid-hide) — keep the lines rows in step.
        var body = table.tBodies[0];
        if (body && window.MutationObserver)
            new MutationObserver(function () { sync(table); })
                .observe(body, { attributes: true, attributeFilter: ['style', 'class'], subtree: true });
        carry(box, box.checked);
        sync(table);
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-items-all]').forEach(init);
    });
})();
