// العُهد العينية — handover popup (Views/MaterialCustody/_MaterialCustodyForm, run through [data-modal-init]).
// Shows the picked asset's category / serial / quantity / unit cost under the picker.
window.mcAssetInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="mcAssetInit"]');
    if (!form) return;
    var sel = form.querySelector('[data-mc-asset]'), info = form.querySelector('[data-mc-asset-info]');
    if (!sel || !info) return;   // editing: the asset is fixed
    function apply() {
        var o = sel.options[sel.selectedIndex];
        if (!o || !o.value) { info.textContent = ''; return; }
        var parts = [];
        if (o.getAttribute('data-category')) parts.push('الفئة: ' + o.getAttribute('data-category'));
        if (o.getAttribute('data-serial')) parts.push('الرقم التسلسلي: ' + o.getAttribute('data-serial'));
        var qty = parseInt(o.getAttribute('data-qty'), 10) || 1;
        if (qty > 1) parts.push('الكمية: ' + qty);
        parts.push('تكلفة الوحدة: ' + (parseFloat(o.getAttribute('data-cost')) || 0).toLocaleString('en-US') + ' ج.م');
        info.textContent = parts.join(' · ');
    }
    sel.addEventListener('change', apply);
    apply();
};
