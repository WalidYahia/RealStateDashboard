// Installment collection (تحصيل) / cancel-collection confirmations — shared by the collections page and the contract page.
// Markup: <form class="pay-form" action=".../Collections/Pay/{id}" data-msg data-remaining> with hidden
// .pay-safe / .pay-amount inputs, and <form class="cancel-form" data-msg>. A collection asks for the receiving safe and
// whether to collect the full remaining or a partial amount, then posts in a new tab that shows the printable receipt.
//   appBindInstallmentPay(rootElement, safes /* [{ id, name }] */, onCollected /* optional callback */)
window.appBindInstallmentPay = function (root, safes, onCollected) {
    if (!root) return;
    function safeOptions() {
        return (safes || []).map(function (s) { return '<option value="' + s.id + '">' + s.name + '</option>'; }).join('');
    }
    root.querySelectorAll('.pay-form, .cancel-form').forEach(function (f) {
        if (f.dataset.payBound) return;
        f.dataset.payBound = '1';
        f.addEventListener('submit', function (e) {
            e.preventDefault();
            if (!window.Swal) { f.submit(); return; }
            if (f.classList.contains('cancel-form')) {
                Swal.fire({
                    icon: 'warning', title: 'إلغاء التحصيل', html: f.dataset.msg,
                    showCancelButton: true, confirmButtonText: 'نعم، إلغاء', cancelButtonText: 'رجوع',
                    reverseButtons: true, confirmButtonColor: '#d03b3b', cancelButtonColor: '#6b7280'
                }).then(function (r) { if (r.isConfirmed) f.submit(); });
                return;
            }
            if (!safes || !safes.length) {
                Swal.fire({ icon: 'warning', title: 'لا توجد خزائن', html: 'أنشئ خزنة أولًا من المحاسبة ← الخزائن قبل التحصيل.' });
                return;
            }
            // Any installment may be collected in full or in part: «كامل المتبقي» / «مبلغ جزئي» — the amount field
            // shows only for a partial collection and may not exceed what's left of the installment.
            var remaining = parseFloat(f.dataset.remaining || '0') || 0;
            var remText = remaining.toLocaleString('en-US');
            var html = f.dataset.msg
                + '<div class="swal-field"><label for="swalSafe">الخزنة المستلمة للمبلغ</label><select id="swalSafe" class="form-control">' + safeOptions() + '</select></div>'
                + '<div class="swal-field"><label>نوع التحصيل</label><div class="swal-choice">'
                + '<label><input type="radio" name="swalMode" value="all" checked> كامل المتبقي (' + remText + ' ج.م)</label>'
                + '<label><input type="radio" name="swalMode" value="partial"> مبلغ جزئي</label>'
                + '</div></div>'
                + '<div class="swal-field" id="swalAmountField" style="display:none;"><label for="swalAmount">المبلغ المحصّل (بحد أقصى ' + remText + ' ج.م)</label>'
                + '<input id="swalAmount" type="number" step="any" min="0.01" max="' + remaining + '" class="form-control" dir="ltr" inputmode="decimal"></div>';
            function isPartial() {
                var m = document.querySelector('input[name="swalMode"]:checked');
                return !!m && m.value === 'partial';
            }
            Swal.fire({
                icon: 'question', title: 'تأكيد التحصيل', html: html,
                showCancelButton: true, confirmButtonText: 'نعم، حصّل واطبع الإيصال', cancelButtonText: 'رجوع',
                reverseButtons: true, confirmButtonColor: '#199e70', cancelButtonColor: '#6b7280',
                didOpen: function () {
                    document.querySelectorAll('input[name="swalMode"]').forEach(function (r) {
                        r.addEventListener('change', function () {
                            var on = isPartial();
                            document.getElementById('swalAmountField').style.display = on ? '' : 'none';
                            Swal.resetValidationMessage();
                            if (on) document.getElementById('swalAmount').focus();
                        });
                    });
                },
                preConfirm: function () {
                    var safe = document.getElementById('swalSafe').value;
                    if (!safe) { Swal.showValidationMessage('اختر الخزنة'); return false; }
                    var amt = null;
                    if (isPartial()) {
                        amt = parseFloat(document.getElementById('swalAmount').value);
                        if (isNaN(amt) || amt <= 0) { Swal.showValidationMessage('أدخل مبلغًا صحيحًا'); return false; }
                        if (amt > remaining + 0.001) { Swal.showValidationMessage('المبلغ يتجاوز المتبقي من القسط (' + remText + ' ج.م)'); return false; }
                    }
                    return { safe: safe, amount: amt };
                }
            }).then(function (r) {
                if (!r.isConfirmed) return;
                f.querySelector('.pay-safe').value = r.value.safe;
                var amtEl = f.querySelector('.pay-amount');
                if (amtEl) amtEl.value = r.value.amount != null ? r.value.amount : '';   // empty = the full remaining
                f.target = '_blank';   // open the printable receipt in a new tab
                f.submit();
                // let the caller refresh what it shows once the collection is saved
                if (onCollected) setTimeout(onCollected, 900);
            });
        });
    });
};
