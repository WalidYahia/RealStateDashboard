// Product-line editor for purchase orders / invoices / sales invoices (loaded by the pages that open those modals —
// scripts inside modal-injected HTML don't run, so everything here is global or delegated).
// Markup contract (see Areas/Suppliers/Views/Shared/_DocItems.cshtml):
//   #pdBody            the <tbody> of line rows          #pdRowTpl  <template> of one row, keyed "__k__"
//   [data-pd-product]  product <select>; options carry data-stock (smallest-unit stock; "" = non-stock product),
//                      data-units = {"d": default level, "u": [{"l": level, "n": name, "f": smallest units per unit}]},
//                      and on sales invoices data-wh-stock = {"warehouseId": qty} (the stock column then follows the
//                      form's WarehouseId select, and a quantity above what's available there is flagged)
//   [data-pd-unit]     the row's unit picker (Items[i].UnitLevel); options carry data-f (smallest units per unit)
//   [data-pd-cost]     unit cost / price input (per the chosen unit)    [data-pd-qty]  quantity input (in the chosen unit)
//   .pd-stock          current-stock cell (optional)     .pd-line   line-total cell     #pdTotal  grand total
//   #pdQtyTotal        total quantity (quantity-only purchase orders have no cost / totals)
//   #pdBody[data-pd-sale="1"]  sales invoice: the price is pre-filled from the product's selling price for the chosen
//                      unit (units' "p"); a pre-filled price is marked data-auto and follows unit changes until edited.
(function () {
    var seq = 1000;

    function fmt(n) { return n.toLocaleString('en-US', { maximumFractionDigits: 2 }); }
    function fmt4(n) { return n.toLocaleString('en-US', { maximumFractionDigits: 4 }); }
    function body() { return document.getElementById('pdBody'); }

    function selectedOption(sel) { return sel && sel.value ? sel.options[sel.selectedIndex] : null; }

    function unitsOf(opt) {
        var raw = opt ? opt.getAttribute('data-units') : null;
        if (!raw) return null;
        try { return JSON.parse(raw); } catch (e) { return null; }
    }

    // The chosen unit's factor (smallest units per one of it) and name; 1 / '' when the row has no unit picker yet.
    function unitOf(tr) {
        var us = tr.querySelector('[data-pd-unit]');
        var o = us && us.selectedIndex >= 0 ? us.options[us.selectedIndex] : null;
        return { f: o ? (parseFloat(o.getAttribute('data-f')) || 1) : 1, n: o ? o.textContent : '' };
    }

    // Fills the row's unit picker from the product's units; keeps `level` (or the product's default) selected.
    function fillUnits(tr, level) {
        var us = tr.querySelector('[data-pd-unit]'); if (!us) return;
        var data = unitsOf(selectedOption(tr.querySelector('[data-pd-product]')));
        us.innerHTML = '';
        if (!data || !data.u || !data.u.length) return;
        var want = level || data.d || 1;
        data.u.forEach(function (u) {
            var o = document.createElement('option');
            o.value = u.l; o.textContent = u.n; o.setAttribute('data-f', u.f);
            if (u.l === want) o.selected = true;
            us.appendChild(o);
        });
        us.setAttribute('data-prev-f', unitOf(tr).f);
    }

    function isSale() { var tb = body(); return !!tb && tb.getAttribute('data-pd-sale') === '1'; }

    // The product's selling price for the row's chosen unit (0 = none set).
    function salePriceOf(tr) {
        var data = unitsOf(selectedOption(tr.querySelector('[data-pd-product]')));
        var us = tr.querySelector('[data-pd-unit]');
        if (!data || !data.u || !us) return 0;
        var lvl = parseInt(us.value, 10), price = 0;
        data.u.forEach(function (u) { if (u.l === lvl) price = parseFloat(u.p) || 0; });
        return price;
    }

    // Sales invoices: put the unit's selling price in the price box (still editable). `force` replaces whatever
    // is there (a new product was picked); otherwise only an empty or still-automatic price is replaced.
    function applySalePrice(tr, force) {
        if (!isSale()) return false;
        var c = tr.querySelector('[data-pd-cost]'); if (!c) return false;
        var price = salePriceOf(tr);
        if (!(price > 0)) { if (force) { c.value = ''; c.removeAttribute('data-auto'); } return false; }
        if (force || c.value === '' || c.getAttribute('data-auto') === '1') {
            c.value = price; c.setAttribute('data-auto', '1');
            return true;
        }
        return false;
    }

    // Stock shown for a picked product (smallest unit): its total on-hand, or — with data-wh-stock — what's available in
    // the warehouse chosen on the form. Returns { nonStock } / { noWarehouse } / { qty, perWarehouse }.
    function stockOf(opt, tr) {
        var raw = opt.getAttribute('data-stock');
        if (raw === '') return { nonStock: true };   // non-stock product (not held in a warehouse)
        var wh = opt.getAttribute('data-wh-stock');
        if (wh) {
            var form = tr.closest('form');
            var whSel = form ? form.querySelector('select[name="WarehouseId"]') : null;
            if (!whSel || !whSel.value) return { noWarehouse: true };
            // GUIDs are compared case-insensitively: option values rendered from SQL (CONVERT) are upper-case,
            // while the stock map's keys come from .NET (lower-case).
            var map = {}; try { map = JSON.parse(wh) || {}; } catch (e) { }
            var key = whSel.value.toLowerCase(), qty = 0;
            Object.keys(map).forEach(function (k) { if (k.toLowerCase() === key) qty = parseFloat(map[k]) || 0; });
            return { qty: qty, perWarehouse: true };
        }
        return { qty: raw ? parseFloat(raw) : NaN };
    }

    function showStock(tr) {
        var opt0 = selectedOption(tr.querySelector('[data-pd-product]'));
        var cell = tr.querySelector('.pd-stock'); if (!cell) return;
        cell.removeAttribute('title');
        if (!opt0) { cell.textContent = '—'; cell.style.color = ''; return; }
        var st = stockOf(opt0, tr);
        if (st.nonStock) { cell.textContent = 'غير مخزني'; cell.style.color = 'var(--muted)'; return; }
        if (st.noWarehouse) { cell.textContent = 'اختر المخزن'; cell.style.color = 'var(--muted)'; return; }
        var u = unitOf(tr);
        var base = st.qty, stock = base / u.f;   // shown in the chosen unit
        cell.textContent = isNaN(stock) ? '—' : fmt4(stock);
        if (!isNaN(base) && u.f !== 1) cell.title = fmt4(base) + ' ' + ((unitsOf(opt0) || { u: [{ n: '' }] }).u[0].n || '') + ' بالوحدة الصغرى';
        var short = false;
        if (st.perWarehouse) {
            // Sales: flag a quantity above what the warehouse holds (compared in the smallest unit; the save is refused otherwise).
            var qEl = tr.querySelector('[data-pd-qty]');
            var q = qEl ? parseFloat(qEl.value) : NaN; if (isNaN(q)) q = 1;
            short = q * u.f > base + 1e-9;
            if (short) { cell.textContent = fmt4(stock) + ' ⚠'; cell.title = 'الكمية المطلوبة أكبر من المتاح في المخزن المختار'; }
        }
        cell.style.color = short || (!isNaN(stock) && stock <= 0) ? 'var(--critical)' : '';
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
            // Hint: the price per smallest unit — what the stock cost is counted in.
            var u = unitOf(tr), us = tr.querySelector('[data-pd-unit]');
            if (u.f !== 1 && cost > 0 && us && us.options.length) {
                costEl.title = '= ' + fmt4(cost / u.f) + ' لكل ' + us.options[0].textContent;
            } else costEl.removeAttribute('title');
            sum += line;
        });
        var el = document.getElementById('pdTotal');
        if (el) el.textContent = fmt(sum);
        var qel = document.getElementById('pdQtyTotal');
        if (qel) qel.textContent = fmt(qtySum);
    };

    // Adds a row (optionally pre-filled with { productId, cost, quantity, unitLevel }) and returns it.
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
        fillUnits(tr, line ? line.unitLevel : null);
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
        tb.querySelectorAll('tr').forEach(function (tr) {
            var us = tr.querySelector('[data-pd-unit]');
            if (us) us.setAttribute('data-prev-f', unitOf(tr).f);
            showStock(tr);
        });
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

    document.addEventListener('change', function (e) {
        var sel = e.target;
        if (!sel || !sel.matches) return;
        if (sel.matches('select[name="WarehouseId"]')) {
            var tb0 = body(); if (tb0) tb0.querySelectorAll('tr').forEach(showStock);   // per-warehouse stock follows the choice
            return;
        }
        var tr = sel.closest('tr'); if (!tr) return;

        // Changing a line's unit converts its typed price to the new unit (5 per متر → 0.05 per سم) and re-shows the stock.
        if (sel.matches('[data-pd-unit]')) {
            var prevF = parseFloat(sel.getAttribute('data-prev-f')) || 1, newF = unitOf(tr).f;
            var costEl = tr.querySelector('[data-pd-cost]');
            // Sales: an automatic price switches to the new unit's own selling price when it has one.
            if (costEl && costEl.getAttribute('data-auto') === '1' && applySalePrice(tr, false)) {
                sel.setAttribute('data-prev-f', newF);
                showStock(tr); pdRecalc();
                return;
            }
            if (costEl && costEl.value !== '' && prevF !== newF) {
                var v = parseFloat(costEl.value);
                if (!isNaN(v)) costEl.value = +(v * newF / prevF).toFixed(4);
            }
            sel.setAttribute('data-prev-f', newF);
            showStock(tr); pdRecalc();
            return;
        }

        // Picking a product fills its units (default pre-selected) and shows its stock. The price is entered by the user.
        if (!sel.matches('[data-pd-product]')) return;
        fillUnits(tr, null);
        applySalePrice(tr, true);   // sales invoice: the default unit's selling price, editable
        showStock(tr); pdRecalc();
        // Next field to fill: the unit cost (invoice) or the quantity (quantity-only order).
        var cost = tr.querySelector('[data-pd-cost]');
        if (cost) { if (!cost.value) cost.focus(); }
        else { var q = tr.querySelector('[data-pd-qty]'); if (q) q.focus(); }
    });
    document.addEventListener('input', function (e) {
        var el = e.target;
        if (el && el.matches && el.matches('[data-pd-cost]')) el.removeAttribute('data-auto');   // typed by the user → kept as is
        if (el && el.matches && el.matches('[data-pd-cost], [data-pd-qty]')) pdRecalc();
        if (el && el.matches && el.matches('[data-pd-qty]')) { var row = el.closest('tr'); if (row) showStock(row); }
    });
})();
