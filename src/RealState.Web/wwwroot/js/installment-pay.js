// Installment collection (تحصيل) / cancel-collection confirmations — shared by the collections page and the contract page.
// Markup: <form class="pay-form" action=".../Collections/Pay/{id}" data-msg data-partial data-remaining> with hidden
// .pay-safe / .pay-amount inputs, and <form class="cancel-form" data-msg>. A collection asks for the receiving safe (and
// the amount, for a down payment collected in parts), then posts in a new tab that shows the printable receipt.
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
            // The down payment (المقدم) may be collected in parts: show an editable amount field.
            var partial = f.dataset.partial === 'true';
            var remaining = parseFloat(f.dataset.remaining || '0') || 0;
            var html = f.dataset.msg
                + '<div class="swal-field"><label for="swalSafe">الخزنة المستلمة للمبلغ</label><select id="swalSafe" class="form-control">' + safeOptions() + '</select></div>';
            if (partial) {
                html += '<div class="swal-field"><label for="swalAmount">المبلغ المحصّل (المتبقي ' + remaining.toLocaleString('en-US') + ' ج.م)</label>'
                    + '<input id="swalAmount" type="number" step="any" min="0.01" max="' + remaining + '" value="' + remaining + '" class="form-control" dir="ltr" inputmode="decimal"></div>';
            }
            Swal.fire({
                icon: 'question', title: 'تأكيد التحصيل', html: html,
                showCancelButton: true, confirmButtonText: 'نعم، حصّل واطبع الإيصال', cancelButtonText: 'رجوع',
                reverseButtons: true, confirmButtonColor: '#199e70', cancelButtonColor: '#6b7280',
                preConfirm: function () {
                    var safe = document.getElementById('swalSafe').value;
                    if (!safe) { Swal.showValidationMessage('اختر الخزنة'); return false; }
                    var amt = null;
                    if (partial) {
                        amt = parseFloat(document.getElementById('swalAmount').value);
                        if (isNaN(amt) || amt <= 0) { Swal.showValidationMessage('أدخل مبلغًا صحيحًا'); return false; }
                        if (amt > remaining + 0.001) { Swal.showValidationMessage('المبلغ يتجاوز المتبقي'); return false; }
                    }
                    return { safe: safe, amount: amt };
                }
            }).then(function (r) {
                if (!r.isConfirmed) return;
                f.querySelector('.pay-safe').value = r.value.safe;
                if (partial && r.value.amount != null) {
                    var amtEl = f.querySelector('.pay-amount');
                    if (amtEl) amtEl.value = r.value.amount;
                }
                f.target = '_blank';   // open the printable receipt in a new tab
                f.submit();
                // let the caller refresh what it shows once the collection is saved
                if (onCollected) setTimeout(onCollected, 900);
            });
        });
    });
};
