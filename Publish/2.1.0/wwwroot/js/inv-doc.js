// Reusable multi-line editor for inventory documents.
// A row's inputs carry data-field="productId|quantity|unitCost|quantityDelta|countedQty".
// On submit the rows are serialized into #linesJson (skipping rows with no product selected).
(function () {
    function enhanceIn(root) {
        if (!window.appEnhanceSearchSelect || !root) return;
        root.querySelectorAll('select[data-searchable]').forEach(function (s) { window.appEnhanceSearchSelect(s); });
    }

    function init() {
        var form = document.getElementById('invForm');
        if (!form) return;
        var tbody = document.querySelector('#invLines tbody');
        var tpl = document.getElementById('invLineTpl');

        enhanceIn(form);   // make existing searchable selects (header + line rows) type-to-filter

        window.invAddLine = function () {
            if (!tpl || !tbody) return;
            tbody.appendChild(document.importNode(tpl.content, true));
            enhanceIn(tbody.lastElementChild);   // enhance the row just added
        };
        window.invRemoveLine = function (btn) {
            var tr = btn.closest('tr'); if (tr) tr.remove();
        };

        form.addEventListener('submit', function () {
            var out = [];
            (tbody ? tbody.querySelectorAll('tr') : []).forEach(function (tr) {
                var obj = {}, hasProduct = false;
                tr.querySelectorAll('[data-field]').forEach(function (el) {
                    var f = el.getAttribute('data-field');
                    if (f === 'productId') { obj[f] = el.value; if (el.value) hasProduct = true; }
                    else { obj[f] = parseFloat(el.value) || 0; }
                });
                if (hasProduct) out.push(obj);
            });
            var hidden = document.getElementById('linesJson');
            if (hidden) hidden.value = JSON.stringify(out);
        });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
