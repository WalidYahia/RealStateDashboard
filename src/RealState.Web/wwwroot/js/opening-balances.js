// الأرصدة الافتتاحية (Areas/Accounting/Views/OpeningBalances/Index): inline save per row. Changing the amount / side
// enables the row's «حفظ»; Enter in the amount saves. The server posts the entry and answers the row's LastUpdateTime.
(function () {
    var table = document.getElementById('obTable');
    if (!table) return;
    var url = table.getAttribute('data-ob-save');
    var tokenEl = document.querySelector('#obToken input[name="__RequestVerificationToken"]');

    function dirty(tr) {
        var amt = tr.querySelector('.ob-amount'), side = tr.querySelector('.ob-side');
        if (!amt || !side) return false;
        return (parseFloat(amt.value) || 0) !== (parseFloat(tr.getAttribute('data-amount')) || 0) || side.value !== tr.getAttribute('data-side');
    }
    function refresh(tr) {
        var btn = tr.querySelector('.ob-save');
        var d = dirty(tr);
        if (btn) btn.disabled = !d;
        tr.classList.toggle('ob-dirty', d);
    }

    function save(tr) {
        var amt = tr.querySelector('.ob-amount'), side = tr.querySelector('.ob-side'), btn = tr.querySelector('.ob-save');
        var value = amt.value.trim() === '' ? 0 : parseFloat(amt.value);
        if (isNaN(value) || value < 0) { window.appAlertError('أدخل رصيدًا صحيحًا (موجبًا) واختر «مدين» أو «دائن».'); return; }
        btn.disabled = true;
        var body = new URLSearchParams({ target: tr.getAttribute('data-target'), amount: String(value), side: side.value, __RequestVerificationToken: tokenEl ? tokenEl.value : '' });
        fetch(url, { method: 'POST', body: body, headers: { 'X-Requested-With': 'XMLHttpRequest', 'Content-Type': 'application/x-www-form-urlencoded' }, noLoader: true })
            .then(function (r) { return r.json(); })
            .then(function (res) {
                if (!res.ok) { window.appAlertError(res.error || 'تعذّر الحفظ.'); refresh(tr); return; }
                tr.setAttribute('data-amount', String(value));
                tr.setAttribute('data-side', side.value);
                var code = tr.querySelector('[data-ob-code]'); if (code && res.code) code.textContent = res.code;
                var up = tr.querySelector('[data-ob-updated]'); if (up) up.textContent = res.updated;
                refresh(tr);
                tr.classList.add('ob-saved'); setTimeout(function () { tr.classList.remove('ob-saved'); }, 1200);
                if (window.appToast) window.appToast('تم حفظ الرصيد الافتتاحي.');
            })
            .catch(function () { window.appAlertError('تعذّر الاتصال بالخادم. حاول مرة أخرى.'); refresh(tr); });
    }

    table.addEventListener('input', function (e) { var tr = e.target.closest('tr[data-target]'); if (tr) refresh(tr); });
    table.addEventListener('change', function (e) { var tr = e.target.closest('tr[data-target]'); if (tr) refresh(tr); });
    table.addEventListener('click', function (e) {
        var btn = e.target.closest('.ob-save'); if (!btn) return;
        save(btn.closest('tr[data-target]'));
    });
    table.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter' || !e.target.classList.contains('ob-amount')) return;
        e.preventDefault();
        var tr = e.target.closest('tr[data-target]');
        if (dirty(tr)) save(tr);
    });
})();
