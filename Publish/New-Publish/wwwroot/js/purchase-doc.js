// Product-line editor for purchase orders / invoices (loaded by the pages that open those modals —
// scripts inside modal-injected HTML don't run, so everything here is global or delegated).
// Markup contract (see Areas/Suppliers/Views/Shared/_DocItems.cshtml):
//   #pdBody            the <tbody> of line rows          #pdRowTpl  <template> of one row, keyed "__k__"
//   [data-pd-product]  product <select>; options carry data-stock ("" = non-stock product) and data-unit
//   .pd-unit           unit-of-measure cell (الوحدة), filled from the picked product
//   [data-pd-cost]     unit cost input                   [data-pd-qty]  quantity input
//   .pd-stock          current-stock cell (optional)     .pd-line   line-total cell     #pdTotal  grand total
//   #pdQtyTotal        total quantity (quantity-only purchase orders have no cost / totals)
(function () {
    var seq = 1000;

    function fmt(n) { return n.toLocaleString('en-US', { maximumFractionDigits: 2 }); }
    function body() { return document.getElementById('pdBody'); }

    function selectedOption(sel) { return sel && sel.value ? sel.options[sel.selectedIndex] : null; }

    function showStock(tr) {
        var opt0 = selectedOption(tr.querySelector('[data-pd-product]'));
        var unitCell = tr.querySelector('.pd-unit');
        if (unitCell && tr.querySelector('[data-pd-product]')) unitCell.textContent = (opt0 && opt0.getAttribute('data-unit')) || '—';
        var cell = tr.querySelector('.pd-stock'); if (!cell) return;
        var opt = opt0;
        var raw = opt ? opt.getAttribute('data-stock') : null;
        var stock = raw ? parseFloat(raw) : NaN;
        // An empty data-stock marks a non-stock product (not received into a warehouse).
        cell.textContent = opt && raw === '' ? 'غير مخزني' : (isNaN(stock) ? '—' : fmt(stock));
        cell.style.color = !isNaN(stock) && stock <= 0 ? 'var(--critical)' : (opt && raw === '' ? 'var(--muted)' : '');
    }

    window.pdRecalc = function () {
        var tb = body(); if (!tb) return;
        var sum = 0, qtySum = 0;
        tb.querySelectorAll('tr').forEach(function (tr) {
            var qtyEl = tr.querySelector('[data-pd-qty]'); if (!qtyEl) return;
            var qty = parseFloat(qtyEl.value); if (isNaN(qty)) qty = 1;   // blank qty = 1 unit
            qtySum += qty;
            var costEl = tr.querySelector('[data-pd-cost]'); if (!costEl) return;   // quantity-only (purchase order)
            var cost = parseFloat(costEl.value); if (isNaN(cost)) cost = 0;
            var line = Math.round(cost * qty * 100) / 100;
            var lineEl = tr.querySelector('.pd-line');
            if (lineEl) lineEl.textContent = fmt(line);
            sum += line;
        });
        var el = document.getElementById('pdTotal');
        if (el) el.textContent = fmt(sum);
        var qel = document.getElementById('pdQtyTotal');
        if (qel) qel.textContent = fmt(qtySum);
    };

    // Adds a row (optionally pre-filled with { productId, cost, quantity }) and returns it.
    window.pdAddRow = function (line, silent) {
        var tb = body(), tpl = document.getElementById('pdRowTpl');
        if (!tb || !tpl) return null;
        var holder = document.createElement('tbody');
        holder.innerHTML = tpl.innerHTML.replace(/__k__/g, 'k' + (seq++));
        var tr = holder.firstElementChild;
        tb.appendChild(tr);
        var sel = tr.querySelector('[data-pd-product]');
        if (line) {
            if (sel) sel.value = line.productId || '';
            var c = tr.querySelector('[data-pd-cost]'); if (c) c.value = line.cost != null ? line.cost : '';
            var q = tr.querySelector('[data-pd-qty]'); if (q) q.value = line.quantity != null ? line.quantity : 1;
        }
        if (sel && window.appEnhanceSearchSelect) window.appEnhanceSearchSelect(sel);   // after the value is set
        showStock(tr);
        if (!silent) {
            pdRecalc();
            var input = tr.querySelector('.ss-input') || sel;
            if (input) input.focus();
            tr.scrollIntoView({ block: 'nearest' });
        }
        return tr;
    };

    window.pdRemoveRow = function (btn) {
        var tr = btn.closest('tr'); if (tr) tr.remove();
        pdRecalc();
    };

    // Modal init hook (data-modal-init="pdInit"): stock cells + totals for the rendered rows.
    window.pdInit = function () {
        var tb = body(); if (!tb) return;
        tb.querySelectorAll('tr').forEach(showStock);
        pdRecalc();
    };

    // Invoice form: picking a purchase order offers to copy its product lines (and project).
    window.pdOrderPicked = function (sel) {
        var form = sel.closest('form');
        var url = form ? form.getAttribute('data-order-lines-url') : null;
        if (!sel.value || !url) return;
        var tb = body();
        var hasLines = tb && Array.prototype.some.call(tb.querySelectorAll('[data-pd-product]'), function (s) { return !!s.value; });

        function load() {
            // The invoice being edited (if any) is excluded, so the remaining quantities include its own lines.
            var idEl = form.querySelector('input[name="Id"]');
            var except = idEl && idEl.value && idEl.value !== '00000000-0000-0000-0000-000000000000' ? '&exceptInvoiceId=' + encodeURIComponent(idEl.value) : '';
            fetch(url + (url.indexOf('?') < 0 ? '?' : '&') + 'id=' + encodeURIComponent(sel.value) + except, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data) return;
                    if (data.lines && data.lines.length) {
                        tb.innerHTML = '';
                        data.lines.forEach(function (l) { pdAddRow(l, true); });
                        pdRecalc();
                    }
                    // Fill the project from the order when none is chosen yet.
                    var proj = form.querySelector('select[name="ProjectId"]');
                    if (proj && !proj.value && data.projectId) {
                        proj.value = data.projectId;
                        var ss = proj.parentNode.querySelector('.ss-input');
                        if (ss) { var o = proj.options[proj.selectedIndex]; ss.value = o ? o.textContent : ''; }
                    }
                });
        }

        if (!hasLines) { load(); return; }
        var msg = 'استبدال أصناف الفاتورة بأصناف أمر التوريد المختار؟';
        if (!window.Swal) { if (window.confirm(msg)) load(); return; }
        Swal.fire({
            icon: 'question', html: msg, showCancelButton: true, confirmButtonText: 'نعم، انسخ الأصناف',
            cancelButtonText: 'لا، أبقِ الأصناف الحالية', reverseButtons: true,
            confirmButtonColor: '#3987e5', cancelButtonColor: '#6b7280'
        }).then(function (r) { if (r.isConfirmed) load(); });
    };

    // Picking a product shows its current stock. The unit cost (تكلفة الوحدة) is entered by the user.
    document.addEventListener('change', function (e) {
        var sel = e.target;
        if (!sel || !sel.matches || !sel.matches('[data-pd-product]')) return;
        var tr = sel.closest('tr'); if (!tr) return;
        showStock(tr);
        // Next field to fill: the unit cost (invoice) or the quantity (quantity-only order).
        var cost = tr.querySelector('[data-pd-cost]');
        if (cost) { if (!cost.value) cost.focus(); }
        else { var q = tr.querySelector('[data-pd-qty]'); if (q) q.focus(); }
    });
    document.addEventListener('input', function (e) {
        var el = e.target;
        if (el && el.matches && el.matches('[data-pd-cost], [data-pd-qty]')) pdRecalc();
    });
})();
