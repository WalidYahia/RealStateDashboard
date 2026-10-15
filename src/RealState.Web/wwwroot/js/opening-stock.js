// «الرصيد الافتتاحي للأصناف» (Areas/Inventory/Views/OpeningStock/Index): one opening stock per product per warehouse,
// saved inline per row like «الأرصدة الافتتاحية». Changing a row's quantity / cost / unit enables its «حفظ» (Enter
// saves too); «حفظ التعديلات» saves every changed row in turn. Also keeps the row totals, the summary and the sort order.
(function () {
    var table = document.getElementById('osTable');
    if (!table) return;
    var tbody = table.tBodies[0];
    var url = table.getAttribute('data-save');
    var tokenEl = document.querySelector('#osToken input[name="__RequestVerificationToken"]');
    var dateEl = document.getElementById('osDate'), whEl = document.getElementById('osWarehouse');
    var saveAllBtn = document.getElementById('osSaveAll');
    var fmt = new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

    function esc(t) { var d = document.createElement('div'); d.textContent = t || ''; return d.innerHTML; }   // alerts render HTML
    function num(el) { var v = parseFloat(el && el.value); return isNaN(v) ? 0 : v; }
    function rows() { return Array.prototype.filter.call(tbody.rows, function (tr) { return tr.hasAttribute('data-product'); }); }
    function unitOf(tr) { return tr.querySelector('[data-inv-unit]'); }
    function factorOf(sel) {
        var o = sel && sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : null;
        return o ? (parseFloat(o.getAttribute('data-f')) || 1) : 1;
    }

    // The saved state of a row, to tell when it changed.
    function snapshot(tr) {
        var u = unitOf(tr);
        tr.setAttribute('data-qty', String(num(tr.querySelector('.os-qty'))));
        tr.setAttribute('data-cost', String(num(tr.querySelector('.os-cost'))));
        tr.setAttribute('data-unit', u ? u.value : '');
    }
    function isDirty(tr) {
        if (tr.classList.contains('os-locked')) return false;
        var u = unitOf(tr), q = num(tr.querySelector('.os-qty')), c = num(tr.querySelector('.os-cost'));
        var savedQ = parseFloat(tr.getAttribute('data-qty')) || 0;
        if (q !== savedQ) return true;
        if (q === 0 && savedQ === 0) return false;   // nothing to save either way
        return c !== (parseFloat(tr.getAttribute('data-cost')) || 0) || (u ? u.value : '') !== tr.getAttribute('data-unit');
    }
    function refresh(tr) {
        var q = num(tr.querySelector('.os-qty')), c = num(tr.querySelector('.os-cost'));
        tr.querySelector('.os-total').textContent = q > 0 ? fmt.format(q * c) : '—';
        tr.classList.toggle('os-filled', q > 0);
        tr.classList.toggle('os-missing-cost', q > 0 && c <= 0);
        var d = isDirty(tr), btn = tr.querySelector('.os-save');
        tr.classList.toggle('os-dirty', d);
        if (btn) btn.disabled = !d;
    }
    function summary() {
        var n = 0, sum = 0, dirty = 0;
        rows().forEach(function (tr) {
            var q = num(tr.querySelector('.os-qty'));
            if (q > 0) { n++; sum += q * num(tr.querySelector('.os-cost')); }
            if (tr.classList.contains('os-dirty')) dirty++;
        });
        document.getElementById('osCount').textContent = n;
        document.getElementById('osSum').textContent = fmt.format(sum);
        document.getElementById('osDirty').textContent = dirty;
        if (saveAllBtn) saveAllBtn.disabled = dirty === 0;
    }

    function save(tr) {
        var q = tr.querySelector('.os-qty'), c = tr.querySelector('.os-cost'), u = unitOf(tr), btn = tr.querySelector('.os-save');
        var qty = q.value.trim() === '' ? 0 : parseFloat(q.value), cost = c.value.trim() === '' ? 0 : parseFloat(c.value);
        if (isNaN(qty) || qty < 0) return Promise.resolve('أدخل كمية صحيحة (0 لإلغاء الرصيد).');
        if (qty > 0 && (isNaN(cost) || cost <= 0)) return Promise.resolve('أدخل تكلفة الوحدة للصنف «' + esc(tr.getAttribute('data-name')) + '».');
        if (btn) btn.disabled = true;
        var body = new URLSearchParams({
            productId: tr.getAttribute('data-product'), warehouseId: whEl.value, date: dateEl.value,
            quantity: String(qty), unitLevel: u ? u.value : '0', unitCost: String(qty > 0 ? cost : 0),
            __RequestVerificationToken: tokenEl ? tokenEl.value : ''
        });
        return fetch(url, { method: 'POST', body: body, headers: { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/x-www-form-urlencoded' }, noLoader: true })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                if (!res.ok) { refresh(tr); summary(); return res.error || 'تعذّر الحفظ.'; }
                if (res.removed) { q.value = ''; c.value = ''; }
                snapshot(tr);
                tr.querySelector('[data-os-date]').textContent = res.date;
                var up = tr.querySelector('[data-os-updated]');
                if (up) { up.textContent = res.updated; up.parentElement.style.display = res.removed ? 'none' : ''; }
                refresh(tr); summary();
                tr.classList.add('os-saved'); setTimeout(function () { tr.classList.remove('os-saved'); }, 1200);
                return null;
            })
            .catch(function () { refresh(tr); summary(); return 'تعذّر الاتصال بالخادم. حاول مرة أخرى.'; });
    }
    function saveOne(tr) {
        save(tr).then(function (err) {
            if (err) window.appAlertError(err);
            else if (window.appToast) window.appToast('تم حفظ الرصيد الافتتاحي للصنف.');
        });
    }
    // Every changed row, one after another; failures are listed at the end.
    function saveAll() {
        var list = rows().filter(function (tr) { return tr.classList.contains('os-dirty'); });
        if (!list.length) return;
        saveAllBtn.disabled = true;
        var errors = [], done = 0;
        list.reduce(function (p, tr) {
            return p.then(function () { return save(tr).then(function (err) { if (err) errors.push(esc(tr.getAttribute('data-name')) + ': ' + err); else done++; }); });
        }, Promise.resolve()).then(function () {
            summary();
            if (errors.length) window.appAlertError((done ? 'تم حفظ ' + done + ' صنف. ' : '') + 'تعذّر حفظ:<br>' + errors.join('<br>'));
            else if (window.appToast) window.appToast('تم حفظ ' + done + ' صنف.');
        });
    }

    // Locked rows: nothing to change (the unit picker included).
    rows().forEach(function (tr) {
        var u = unitOf(tr);
        if (u) { u.setAttribute('data-prev-f', factorOf(u)); if (tr.classList.contains('os-locked')) u.disabled = true; }
        snapshot(tr); refresh(tr);
    });
    summary();

    tbody.addEventListener('input', function (e) { var tr = e.target.closest('tr[data-product]'); if (tr) { refresh(tr); summary(); } });
    tbody.addEventListener('change', function (e) {
        var tr = e.target.closest('tr[data-product]'); if (!tr) return;
        if (e.target.matches('[data-inv-unit]')) {
            // A typed unit cost follows the unit (e.g. per كرتونة = 12 × per قطعة).
            var prev = parseFloat(e.target.getAttribute('data-prev-f')) || 1, now = factorOf(e.target), cost = tr.querySelector('.os-cost');
            if (cost.value !== '' && prev !== now) { var v = parseFloat(cost.value); if (!isNaN(v)) cost.value = +(v * now / prev).toFixed(4); }
            e.target.setAttribute('data-prev-f', now);
        }
        refresh(tr); summary();
    });
    tbody.addEventListener('click', function (e) { var b = e.target.closest('.os-save'); if (b) saveOne(b.closest('tr[data-product]')); });
    tbody.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || !e.target.matches('.os-qty, .os-cost')) return;
        e.preventDefault();
        var tr = e.target.closest('tr[data-product]');
        if (isDirty(tr)) saveOne(tr);
    });
    if (saveAllBtn) saveAllBtn.addEventListener('click', saveAll);

    // Another warehouse = another page; unsaved changes are confirmed first.
    if (whEl) whEl.addEventListener('change', function () {
        var go = function () { leaving = true; location.href = whEl.getAttribute('data-url') + '?warehouseId=' + encodeURIComponent(whEl.value); };
        var dirty = rows().some(function (tr) { return tr.classList.contains('os-dirty'); });
        if (!dirty) return go();
        var back = function () { whEl.value = whEl.getAttribute('data-current'); };
        if (window.Swal) Swal.fire({ icon: 'question', title: 'تغييرات غير محفوظة', html: 'تغيير المخزن يتجاهل التعديلات غير المحفوظة. متابعة؟', showCancelButton: true, confirmButtonText: 'نعم، متابعة', cancelButtonText: 'إلغاء', reverseButtons: true })
            .then(function (r) { if (r.isConfirmed) go(); else back(); });
        else if (confirm('تغيير المخزن يتجاهل التعديلات غير المحفوظة. متابعة؟')) go(); else back();
    });
    var leaving = false;
    if (whEl) whEl.setAttribute('data-current', whEl.value);
    window.addEventListener('beforeunload', function (e) {
        if (!leaving && rows().some(function (tr) { return tr.classList.contains('os-dirty'); })) { e.preventDefault(); e.returnValue = ''; }
    });

    var sort = document.getElementById('osSort');
    var collator = new Intl.Collator('ar', { numeric: true, sensitivity: 'base' });
    if (sort) sort.addEventListener('change', function () {
        var key = sort.value, list = rows();
        list.sort(function (a, b) {
            if (key === 'filled') {
                var fa = a.classList.contains('os-filled') ? 0 : 1, fb = b.classList.contains('os-filled') ? 0 : 1;
                if (fa !== fb) return fa - fb;
            }
            var k = key === 'filled' ? 'sku' : key;   // empty values (no category) sort last
            return collator.compare(a.getAttribute('data-' + k) || '￿', b.getAttribute('data-' + k) || '￿')
                || collator.compare(a.getAttribute('data-sku'), b.getAttribute('data-sku'));
        });
        var tail = Array.prototype.filter.call(tbody.rows, function (tr) { return !tr.hasAttribute('data-product'); });
        list.concat(tail).forEach(function (tr) { tbody.appendChild(tr); });
    });
})();
