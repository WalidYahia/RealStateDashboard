// Sales / purchase return form (Views/Shared/Returns/_ReturnForm.cshtml), loaded into the shared modal:
//  • picking the invoice reloads the form with that invoice's lines;
//  • each returned quantity is capped by what's still returnable, and the line totals / return value follow it;
//  • the cash refund keeps within its limit: payments are on the party's account, so the return lowers the account's
//    balance and cash may change hands only for what it pushes past it (value − balance, at most the value).
// The server applies the same rules.
(function () {
    function num(v) { var n = parseFloat(v); return isFinite(n) ? n : 0; }
    function money(v) { return (Math.round(v * 100) / 100).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }

    function recalc(form, fromRefund) {
        var total = 0;
        form.querySelectorAll('[data-ret-row]').forEach(function (row) {
            var input = row.querySelector('[data-ret-qty]');
            var max = num(input && input.max);
            var q = num(input && input.value);
            if (input && q > max) { q = max; input.value = max; }
            if (input && q < 0) { q = 0; input.value = ''; }
            var line = Math.round(num(row.getAttribute('data-price')) * q * 100) / 100;
            total += line;
            var cell = row.querySelector('[data-ret-line]');
            if (cell) cell.textContent = money(line);
        });
        var t = form.querySelector('[data-ret-total]');
        if (t) t.textContent = money(total);

        // The account's balance before the return: what the customer owes / what's owed to the supplier.
        var balance = num(form.getAttribute('data-ret-balance'));
        var sales = form.getAttribute('data-ret-sales') === '1';
        var max = Math.max(0, Math.round(Math.min(total, total - balance) * 100) / 100);
        var refund = form.querySelector('[data-ret-refund]');
        if (refund) {
            refund.min = 0; refund.max = max;
            // Nothing in cash until the user types an amount of their own; always keep it within the limit.
            if (!fromRefund && !refund.dataset.touched) refund.value = (0).toFixed(2);
            var r = num(refund.value);
            if (r < 0) refund.value = (0).toFixed(2);
            if (r > max) refund.value = max.toFixed(2);
        }
        var hint = form.querySelector('[data-ret-hint]');
        if (hint) {
            var owed = sales ? 'المستحق على العميل' : 'المستحق للمورد';
            hint.textContent = total <= 0
                ? 'أدخل الكميات المرتجعة؛ قيمة المرتجع تُخصم من ' + owed + '، وما يزيد عنه يجوز ' + (sales ? 'رده' : 'استرداده') + ' نقدًا.'
                : 'قيمة المرتجع ' + money(total) + ' ج.م تُخصم من ' + owed + ' (' + money(Math.max(balance, 0)) + ' ج.م)'
                  + (max > 0 ? ' — المبلغ النقدي حتى ' + money(max) + ' ج.م، والباقي يبقى رصيدًا.' : ' — لا يزيد عنه، فلا مبلغ نقدي.');
        }
    }

    // Modal init hook (data-modal-init="retInit").
    window.retInit = function () {
        var form = document.querySelector('#appModalBody form[data-modal-init="retInit"]');
        if (!form) return;
        // Re-rendered after a validation error: keep the amount the user had entered.
        var refund = form.querySelector('[data-ret-refund]');
        if (refund && num(refund.value) > 0) refund.dataset.touched = '1';
        recalc(form, false);
        form.addEventListener('input', function (e) {
            if (e.target.matches('[data-ret-qty]')) recalc(form, false);
            if (e.target.matches('[data-ret-refund]')) e.target.dataset.touched = '1';
        });
        form.addEventListener('change', function (e) {
            if (e.target.matches('[data-ret-refund]')) recalc(form, true);
            if (e.target.matches('[data-ret-invoice]') && e.target.value) {
                var url = form.getAttribute('data-ret-reload') + '?invoiceId=' + encodeURIComponent(e.target.value);
                window.appOpenModal(url, form.getAttribute('data-ret-title') || '', 'xl');
            }
        });
    };
})();
