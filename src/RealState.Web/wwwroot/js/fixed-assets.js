// الأصول الثابتة — popups run through [data-modal-init] (Views/FixedAssets/_FixedAssetForm, _PostForm, _DisposeForm).

// Acquisition: show the safe picker for «شراء نقدي», the prior-depreciation box for «رصيد افتتاحي». A category's default
// useful life fills an empty life field (guidance for recording depreciation).
window.faFormInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="faFormInit"]');
    if (!form) return;
    var cash = form.querySelector('[data-fa-cash]'), opening = form.querySelector('[data-fa-opening]');
    function applyMethod() {
        var picked = form.querySelector('[data-fa-method]:checked');
        var isCash = !picked || picked.value === 'Cash';
        if (cash) cash.style.display = isCash ? '' : 'none';
        if (opening) opening.style.display = isCash ? 'none' : '';
    }
    form.querySelectorAll('[data-fa-method]').forEach(function (r) { r.addEventListener('change', applyMethod); });
    applyMethod();

    var lives = {};
    try { lives = JSON.parse(form.getAttribute('data-lives') || '{}'); } catch (e) { }
    var cat = form.querySelector('[data-fa-category]'), life = form.querySelector('[data-fa-life]');
    if (cat && life && !life.readOnly) {
        cat.addEventListener('change', function () {
            var v = lives[cat.value];
            if (v != null && (!life.value || life.value === '0')) life.value = v;
        });
    }
};

// Depreciation form (Views/FixedAssetDepreciation/_DepreciationForm): picking an asset shows what's left to depreciate
// and fills the suggested amount (straight line over its useful life, capped by what's left) while the amount is empty.
window.faDepreciationInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="faDepreciationInit"]');
    if (!form) return;
    var asset = form.querySelector('[data-fa-dep-asset]'), amount = form.querySelector('[data-fa-dep-amount]'), info = form.querySelector('[data-fa-dep-info]');
    function fmt(v) { return v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function apply(fill) {
        var opt = asset && asset.options[asset.selectedIndex];
        var remaining = opt ? parseFloat(opt.getAttribute('data-remaining')) : NaN;
        var suggested = opt ? parseFloat(opt.getAttribute('data-suggested')) : NaN;
        if (isNaN(remaining)) { if (info) info.textContent = ''; return; }
        amount.max = remaining;
        if (info) info.textContent = 'المتبقي للإهلاك (حتى القيمة التخريدية): ' + fmt(remaining) + ' ج.م'
            + (suggested > 0 ? ' · المقترح شهريًا: ' + fmt(suggested) + ' ج.م' : '');
        if (fill && (!amount.value || parseFloat(amount.value) === 0) && suggested > 0) amount.value = Math.min(suggested, remaining).toFixed(2);
    }
    if (asset) asset.addEventListener('change', function () { amount.value = ''; apply(true); });
    apply(true);
};

// Disposal: proceeds → show the safe picker; live gain / loss against the book value.
window.faDisposeInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="faDisposeInit"]');
    if (!form) return;
    var book = parseFloat(form.getAttribute('data-book')) || 0;
    var proceeds = form.querySelector('[data-fa-proceeds]'), safe = form.querySelector('[data-fa-proceeds-safe]'), out = form.querySelector('[data-fa-result]');
    function fmt(v) { return v.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function apply() {
        var p = parseFloat(proceeds.value) || 0, r = Math.round((p - book) * 100) / 100;
        safe.style.display = p > 0 ? '' : 'none';
        out.className = 'settle-total ' + (r >= 0 ? 'ok' : 'off');
        out.textContent = r > 0 ? 'ربح استبعاد ' + fmt(r) + ' ج.م' : r < 0 ? 'خسارة استبعاد ' + fmt(-r) + ' ج.م' : 'لا ربح ولا خسارة';
    }
    proceeds.addEventListener('input', apply);
    apply();
};
