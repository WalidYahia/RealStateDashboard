// تسوية رصيد (Areas/Accounting/Views/BalanceAdjustments/_Form, via [data-modal-init]). The account is the page the form was
// opened from (customer statement → كعميل, supplier page → كمورد). The counter account shown follows the direction
// («تسويات أرصدة دائنة» when the party's account is debited: customer up / supplier down, «… مدينة» the other way), and the
// balance after the adjustment shows as it's typed (and the net, for «عميل ومورد»).
window.balanceAdjustInit = function () {
    var form = document.querySelector('#appModalBody #appModalForm[data-modal-init="balanceAdjustInit"]');
    if (!form) return;
    var merged = form.getAttribute('data-merged') === '1';
    var customerBalance = parseFloat(form.getAttribute('data-customer-balance')) || 0;   // + = owed by them
    var supplierBalance = parseFloat(form.getAttribute('data-supplier-balance')) || 0;   // + = owed to them
    var amount = form.querySelector('[data-adj-amount]'), out = form.querySelector('[data-adj-result]');
    var counter = form.querySelector('[data-adj-counter]');
    function fmt(v) { return Math.abs(v).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
    function owedBy(v) { return Math.abs(v) < 0.005 ? 'متوازن' : v > 0 ? 'مستحق عليه' : 'مستحق له'; }   // customer / net terms

    function isSupplier() {
        var party = form.querySelector('input[type="hidden"][name="Party"]');
        return !!party && party.value === 'supplier';
    }

    function apply() {
        var supplier = isSupplier();
        var dir = form.querySelector('[data-adj-dir]:checked');
        var up = dir && dir.value === 'true';
        var a = parseFloat(amount.value) || 0;

        form.querySelectorAll('[data-adj-label]').forEach(function (l) {
            l.textContent = l.getAttribute(supplier ? 'data-supplier' : 'data-customer');
        });

        var cust = customerBalance, sup = supplierBalance;
        if (supplier) sup += up ? a : -a; else cust += up ? a : -a;
        cust = Math.round(cust * 100) / 100; sup = Math.round(sup * 100) / 100;
        out.className = 'settle-total ok';
        if (merged) {
            var net = Math.round((cust - sup) * 100) / 100;
            out.textContent = 'بعد التسوية: ' + (supplier ? 'رصيده كمورد ' + fmt(sup) + ' (' + owedBy(-sup) + ')' : 'رصيده كعميل ' + fmt(cust) + ' (' + owedBy(cust) + ')')
                + ' · الصافي ' + fmt(net) + ' ج.م (' + owedBy(net) + ')';
        } else {
            out.textContent = 'الرصيد بعد التسوية: ' + (supplier ? fmt(sup) + ' ج.م (' + owedBy(-sup) + ')' : fmt(cust) + ' ج.م (' + owedBy(cust) + ')');
        }

        // The counter account (information) follows the direction and the account.
        if (counter) counter.textContent = form.getAttribute((supplier ? !up : up) ? 'data-auto-revenue' : 'data-auto-expense');
    }
    form.querySelectorAll('[data-adj-dir]').forEach(function (r) { r.addEventListener('change', apply); });
    amount.addEventListener('input', apply);
    apply();
};
