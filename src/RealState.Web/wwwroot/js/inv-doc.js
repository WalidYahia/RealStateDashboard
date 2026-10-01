// Reusable multi-line editor for inventory documents.
// A row's inputs carry data-field="productId|unitLevel|quantity|unitCost|quantityDelta|countedQty" (quantities and
// costs are in the row's chosen unit; the server converts them to the product's smallest unit).
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

        // Units: picking a product fills the row's unit picker ([data-inv-unit]) from the option's data-units
        // ({"d": default level, "u": [{l, n, f}]}); changing the unit converts a typed unit cost to the new unit.
        function factorOf(sel) {
            var o = sel && sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : null;
            return o ? (parseFloat(o.getAttribute('data-f')) || 1) : 1;
        }
        function fillUnits(tr) {
            var us = tr.querySelector('[data-inv-unit]'), ps = tr.querySelector('[data-field="productId"]');
            if (!us || !ps) return;
            var opt = ps.selectedIndex >= 0 ? ps.options[ps.selectedIndex] : null, data = null;
            try { data = opt && opt.getAttribute('data-units') ? JSON.parse(opt.getAttribute('data-units')) : null; } catch (e) { }
            us.innerHTML = '';
            if (!data || !data.u) return;
            data.u.forEach(function (u) {
                var o = document.createElement('option');
                o.value = u.l; o.textContent = u.n; o.setAttribute('data-f', u.f);
                if (u.l === (data.d || 1)) o.selected = true;
                us.appendChild(o);
            });
            us.setAttribute('data-prev-f', factorOf(us));
        }
        if (tbody) tbody.querySelectorAll('[data-inv-unit]').forEach(function (us) { us.setAttribute('data-prev-f', factorOf(us)); });
        if (tbody) tbody.addEventListener('change', function (e) {
            var t = e.target, tr = t && t.closest ? t.closest('tr') : null;
            if (!tr) return;
            if (t.matches('[data-field="productId"]')) { fillUnits(tr); return; }
            if (t.matches('[data-inv-unit]')) {
                var prev = parseFloat(t.getAttribute('data-prev-f')) || 1, now = factorOf(t);
                var cost = tr.querySelector('[data-field="unitCost"]');
                if (cost && cost.value !== '' && prev !== now) {
                    var v = parseFloat(cost.value);
                    if (!isNaN(v)) cost.value = +(v * now / prev).toFixed(4);
                }
                t.setAttribute('data-prev-f', now);
            }
        });

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
