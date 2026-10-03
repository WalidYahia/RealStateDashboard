// Sales / purchase return form (Views/Shared/Returns/_ReturnForm.cshtml), loaded into the shared modal:
//  • picking the invoice reloads the form with that invoice's lines;
//  • each returned quantity is capped by what's still returnable, and the line totals / return value follow it;
//  • the cash refund keeps within its limits: at least the part of the return the invoice no longer owes
//    (value − remaining), at most the return's value capped by what was paid and not refunded yet.
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

        var remaining = Math.max(num(form.getAttribute('data-ret-remaining')), 0);
        var netPaid = num(form.getAttribute('data-ret-netpaid'));
        var min = Math.max(0, Math.round((total - remaining) * 100) / 100);
        var max = Math.max(0, Math.round(Math.min(total, netPaid) * 100) / 100);
        var refund = form.querySelector('[data-ret-refund]');
        if (refund) {
            refund.min = min; refund.max = max;
            // Follow the minimum until the user types an amount of their own; always keep it within the limits.
            if (!fromRefund && !refund.dataset.touched) refund.value = min.toFixed(2);
            var r = num(refund.value);
            if (r < min) refund.value = min.toFixed(2);
            if (r > max) refund.value = max.toFixed(2);
        }
        var hint = form.querySelector('[data-ret-hint]');
        if (hint) {
            hint.textContent = total <= 0
                ? 'أدخل الكميات المرتجعة؛ قيمة المرتجع تُخصم من المتبقي على الفاتورة، وما يزيد عنه يُرد نقدًا.'
                : 'قيمة المرتجع ' + money(total) + ' ج.م — المبلغ النقدي من ' + money(min) + ' إلى ' + money(max) + ' ج.م'
                  + (min > 0 ? ' (الحد الأدنى = ما يزيد عن المتبقي على الفاتورة ' + money(remaining) + ' ج.م).' : '؛ الباقي يُخصم من الرصيد.');
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
