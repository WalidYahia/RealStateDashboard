// كشف حساب العميل — «تحصيل دفعة» (Views/Customers/_CollectPicker, via [data-modal-init]): the amount starts at the
// picked invoice's remaining balance (and can't go above it).
window.custCollectInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="custCollectInit"]');
    if (!form) return;
    var invoice = form.querySelector('[data-cc-invoice]'), amount = form.querySelector('[data-cc-amount]');
    if (!invoice || !amount) return;
    function apply() {
        var remaining = invoice.options[invoice.selectedIndex].getAttribute('data-remaining');
        amount.value = remaining;
        amount.max = remaining;
    }
    invoice.addEventListener('change', apply);
    apply();
};
