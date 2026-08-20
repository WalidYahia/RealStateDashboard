// Reusable modal CRUD + system-wide SweetAlert confirmations, errors and toasts.
// Usage: appOpenModal('/Area/Controller/Form?id=...', 'Title')
// The loaded partial must contain a <form id="appModalForm">. The POST action returns
//   { ok: true }            -> success (page reloads / redirects)
//   { ok: false, error: '' } -> error (SweetAlert shown, form stays open)
//   partial HTML            -> validation errors (modal body is replaced, form re-bound)
// Any normal <form data-confirm="msg"> is intercepted and confirmed with SweetAlert first
// (add data-danger for a red "delete" style).

// Sidebar collapsible groups (Projects tree, Settings).
window.toggleNavGroup = function (el) {
    var g = el.closest('.nav-group');
    if (g) g.classList.toggle('open');
};

// ---- shared SweetAlert helpers ----
window.appAlertError = function (msg) {
    if (window.Swal) {
        Swal.fire({ icon: 'error', title: 'خطأ', html: msg || 'حدث خطأ أثناء تنفيذ العملية.', confirmButtonText: 'حسنًا', confirmButtonColor: '#d03b3b' });
    } else {
        alert(msg || 'حدث خطأ أثناء تنفيذ العملية.');
    }
};
window.appToast = function (msg, icon) {
    if (!window.Swal) return;
    Swal.fire({ toast: true, position: 'top', icon: icon || 'success', title: msg, showConfirmButton: false, timer: 2800, timerProgressBar: true });
};

(function () {
    let modal;
    function ensureModal() {
        return modal || (modal = new bootstrap.Modal(document.getElementById('appModal')));
    }

    window.appOpenModal = function (url, title) {
        var m = ensureModal();
        document.getElementById('appModalTitle').textContent = title || '';
        document.getElementById('appModalBody').innerHTML = '<div style="padding:24px;text-align:center;color:var(--muted)">جارٍ التحميل…</div>';
        m.show();
        fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) { return r.text(); })
            .then(function (html) { document.getElementById('appModalBody').innerHTML = html; bindForm(); })
            .catch(function () { document.getElementById('appModalBody').innerHTML = '<div style="padding:24px;color:var(--danger)">تعذّر تحميل النموذج.</div>'; });
    };

    // Ensure a hidden input with the given name exists on the form and set its value (used to carry a
    // server-requested confirmation flag back on resubmit).
    function setHiddenField(form, name, value) {
        var input = form.querySelector('[name="' + name + '"]');
        if (!input) { input = document.createElement('input'); input.type = 'hidden'; input.name = name; form.appendChild(input); }
        input.value = value;
    }

    function submitForm(form) {
        var btn = form.querySelector('button[type="submit"]');
        if (btn) btn.disabled = true;
        // Pre-open a tab synchronously (still inside the user gesture) so { ok, openTab } isn't popup-blocked.
        var preTab = form.hasAttribute('data-open-tab') ? window.open('about:blank', '_blank') : null;
        fetch(form.action, { method: 'POST', body: new FormData(form), headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (r) {
                var ct = r.headers.get('content-type') || '';
                return ct.indexOf('application/json') >= 0
                    ? r.json().then(function (j) { return { json: j }; })
                    : r.text().then(function (t) { return { html: t }; });
            })
            .then(function (res) {
                if (res.json) {
                    if (res.json.ok) {
                        if (res.json.openTab) {
                            if (preTab) { preTab.location.href = res.json.openTab; } else { window.open(res.json.openTab, '_blank'); }
                            window.location.reload();
                        }
                        else if (res.json.redirect) { window.location.href = res.json.redirect; }
                        else { window.location.reload(); }
                        return;
                    }
                    // { ok:false, confirm } -> ask, then resubmit with a hidden confirm flag set.
                    if (res.json.confirm) {
                        if (preTab) preTab.close();
                        if (btn) btn.disabled = false;
                        var field = res.json.confirmField || 'Confirmed';
                        var proceed = function (yes) { if (yes) { setHiddenField(form, field, 'true'); submitForm(form); } };
                        if (!window.Swal) { proceed(window.confirm(res.json.confirm)); return; }
                        Swal.fire({
                            icon: 'question', title: 'تأكيد', html: res.json.confirm,
                            showCancelButton: true, confirmButtonText: 'نعم', cancelButtonText: 'إلغاء',
                            reverseButtons: true, confirmButtonColor: '#3987e5', cancelButtonColor: '#6b7280'
                        }).then(function (r) { proceed(r.isConfirmed); });
                        return;
                    }
                    // { ok:false, error }
                    if (preTab) preTab.close();
                    if (btn) btn.disabled = false;
                    window.appAlertError(res.json.error);
                    return;
                }
                if (res.html) { if (preTab) preTab.close(); document.getElementById('appModalBody').innerHTML = res.html; bindForm(); }
            })
            .catch(function () { if (preTab) preTab.close(); if (btn) btn.disabled = false; window.appAlertError('تعذّر الاتصال بالخادم. حاول مرة أخرى.'); });
    }

    // Progressive-enhance a <select data-searchable> into a type-to-filter combobox. The native
    // <select> is kept (hidden) as the value holder, so form submission and validation are unchanged.
    window.appEnhanceSearchSelect = function (select) {
        if (!select || select.dataset.ssEnhanced) return;
        select.dataset.ssEnhanced = '1';
        var placeholder = select.getAttribute('data-search-placeholder') || '🔍 بحث...';

        var wrap = document.createElement('div');
        wrap.className = 'ss-wrap';
        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(select);
        select.classList.add('ss-native');

        var input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-control ss-input';
        input.setAttribute('autocomplete', 'off');
        input.placeholder = placeholder;

        var menu = document.createElement('div');
        menu.className = 'ss-menu';
        menu.style.display = 'none';

        wrap.appendChild(input);
        wrap.appendChild(menu);

        function currentText() {
            var o = select.options[select.selectedIndex];
            return (o && o.value) ? o.textContent : '';
        }
        function buildMenu(filter) {
            menu.innerHTML = '';
            var nf = window.appSearchNormalize(filter || '');
            var count = 0;
            Array.prototype.forEach.call(select.options, function (opt) {
                if (opt.value === '') return;   // skip the placeholder option
                if (nf && window.appSearchNormalize(opt.textContent).indexOf(nf) < 0) return;
                var it = document.createElement('div');
                it.className = 'ss-opt';
                it.textContent = opt.textContent;
                if (opt.value === select.value) it.classList.add('ss-active');
                it.addEventListener('mousedown', function (e) {
                    e.preventDefault();   // select before the input loses focus
                    select.value = opt.value;
                    input.value = opt.textContent;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                    menu.style.display = 'none';
                });
                menu.appendChild(it);
                count++;
            });
            if (count === 0) {
                var em = document.createElement('div');
                em.className = 'ss-empty'; em.textContent = 'لا نتائج';
                menu.appendChild(em);
            }
        }

        input.value = currentText();
        input.addEventListener('focus', function () { buildMenu(''); menu.style.display = ''; });
        input.addEventListener('input', function () { buildMenu(input.value); menu.style.display = ''; });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                var first = menu.querySelector('.ss-opt');
                if (first) { first.dispatchEvent(new MouseEvent('mousedown')); }
            } else if (e.key === 'Escape') { menu.style.display = 'none'; }
        });
        input.addEventListener('blur', function () {
            setTimeout(function () { input.value = currentText(); menu.style.display = 'none'; }, 150);
        });
    };

    function bindForm() {
        // Fire an initial change on cascade selects (e.g. project → units) so dependent lists
        // filter correctly on open and after a validation re-render.
        document.querySelectorAll('#appModalBody select[data-cascade-init]').forEach(function (s) {
            s.dispatchEvent(new Event('change'));
        });
        // Enhance any searchable selects in the freshly-loaded modal content.
        document.querySelectorAll('#appModalBody select[data-searchable]').forEach(function (s) {
            window.appEnhanceSearchSelect(s);
        });

        var form = document.querySelector('#appModalBody #appModalForm');
        if (!form) return;
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            // Optional pre-save warning: data-warn-fn names a global fn(form) -> message|null.
            var fnName = form.getAttribute('data-warn-fn');
            var warn = (fnName && typeof window[fnName] === 'function') ? window[fnName](form) : null;
            if (warn && window.Swal) {
                Swal.fire({
                    icon: 'warning', html: warn, showCancelButton: true,
                    confirmButtonText: 'نعم، احفظ', cancelButtonText: 'إلغاء', reverseButtons: true,
                    confirmButtonColor: '#3987e5', cancelButtonColor: '#6b7280'
                }).then(function (r) { if (r.isConfirmed) submitForm(form); });
            } else {
                submitForm(form);
            }
        });
    }

    // System-wide confirmation for any normal <form data-confirm="...">.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || typeof form.getAttribute !== 'function') return;
        var msg = form.getAttribute('data-confirm');
        if (msg === null) return;                       // not a confirm-guarded form
        e.preventDefault();
        var danger = form.hasAttribute('data-danger');
        function go() { form.removeAttribute('data-confirm'); form.submit(); }
        if (!window.Swal) { if (window.confirm(msg)) go(); return; }
        Swal.fire({
            icon: danger ? 'warning' : 'question',
            title: danger ? 'تأكيد الحذف' : 'تأكيد',
            html: msg,
            showCancelButton: true,
            confirmButtonText: danger ? 'نعم، حذف' : 'نعم، متابعة',
            cancelButtonText: 'إلغاء',
            reverseButtons: true,
            confirmButtonColor: danger ? '#d03b3b' : '#199e70',
            cancelButtonColor: '#6b7280'
        }).then(function (r) { if (r.isConfirmed) go(); });
    }, true);

    // Server-pushed error (TempData) shown once on load.
    document.addEventListener('DOMContentLoaded', function () {
        if (window.__appError) window.appAlertError(window.__appError);
    });

    // Normalize text for searching: lowercase, convert Arabic-Indic digits to Latin, and drop
    // thousands separators so a money value shown as "300,000" is matched by typing "300000".
    window.appSearchNormalize = function (s) {
        return (s || '')
            .toLowerCase()
            .replace(/[٠-٩]/g, function (d) { return String.fromCharCode(d.charCodeAt(0) - 0x0660 + 48); })
            .replace(/[۰-۹]/g, function (d) { return String.fromCharCode(d.charCodeAt(0) - 0x06F0 + 48); })
            .replace(/[,٫٬،]/g, '');   // Latin comma, Arabic decimal/thousands sep, Arabic comma
    };

    // Parse a number out of displayed cell text (handles "300,000", "+2,000", "−500", Arabic digits).
    function lsParseNumber(text) {
        var raw = window.appSearchNormalize(text).replace(/−/g, '-').replace(/[^0-9.\-]/g, '');
        if (raw === '' || raw === '-' || raw === '.') return null;
        var v = parseFloat(raw);
        return isNaN(v) ? null : v;
    }

    function lsFormatNumber(n) {
        return n.toLocaleString('en-US', { maximumFractionDigits: 2 });
    }

    // Rows that currently count toward totals: visible, and not placeholder rows.
    function lsVisibleRows(table) {
        var out = [];
        table.querySelectorAll('tbody > tr').forEach(function (tr) {
            if (tr.hasAttribute('data-skip-search')) return;
            if (tr.style.display === 'none') return;
            out.push(tr);
        });
        return out;
    }

    // Recompute any list totals so they reflect the current (searched) rows.
    // 1) <table data-live-totals>: every single-column <tfoot> cell is replaced with the sum of its
    //    column over the visible rows (cells whose column isn't fully numeric, or marked data-no-sum,
    //    are left alone — so label cells stay put).
    // 2) [data-live-count="<tableSelector>"]: set to the number of visible rows.
    // 3) [data-live-sum="<tableSelector>:<colIndex>"]: set to the sum of that column over visible rows.
    function lsRefreshAggregates() {
        document.querySelectorAll('table[data-live-totals]').forEach(function (table) {
            var tfoot = table.tFoot; if (!tfoot) return;
            var rows = lsVisibleRows(table);
            Array.prototype.forEach.call(tfoot.rows, function (footRow) {
                var col = 0;
                Array.prototype.forEach.call(footRow.cells, function (cell) {
                    var span = cell.colSpan || 1;
                    if (span === 1 && !cell.hasAttribute('data-no-sum')) {
                        var sum = 0, numeric = rows.length > 0;
                        rows.forEach(function (tr) {
                            var c = tr.children[col];
                            var v = c ? lsParseNumber(c.textContent) : null;
                            if (v === null) numeric = false; else sum += v;
                        });
                        if (numeric) cell.textContent = lsFormatNumber(sum);
                    }
                    col += span;
                });
            });
        });
        document.querySelectorAll('[data-live-count]').forEach(function (el) {
            var t = document.querySelector(el.getAttribute('data-live-count'));
            if (t) el.textContent = lsVisibleRows(t).length;
        });
        document.querySelectorAll('[data-live-sum]').forEach(function (el) {
            var parts = (el.getAttribute('data-live-sum') || '').split(':');
            var t = document.querySelector(parts[0]);
            var col = parseInt(parts[1], 10);
            if (!t || isNaN(col)) return;
            var sum = 0;
            lsVisibleRows(t).forEach(function (tr) {
                var v = tr.children[col] ? lsParseNumber(tr.children[col].textContent) : null;
                if (v !== null) sum += v;
            });
            el.textContent = lsFormatNumber(sum);
        });
    }
    window.appRefreshListTotals = lsRefreshAggregates;

    // Generic client-side list search. Any <input class="list-search"> filters the rows of its table
    // (the one named by data-target, else the nearest .rs-table in the same card) by matching the typed
    // text against the WHOLE row's text — i.e. every column. Rows/cells marked data-skip-search are
    // ignored (empty-state / no-results placeholders); a [data-no-results] row is toggled when nothing matches.
    document.addEventListener('input', function (e) {
        var box = e.target;
        if (!box || !box.classList || !box.classList.contains('list-search')) return;
        var sel = box.getAttribute('data-target');
        var scope = box.closest('.card-tile') || document;
        var table = sel ? document.querySelector(sel) : scope.querySelector('.rs-table');
        if (!table) return;
        var q = window.appSearchNormalize(box.value.trim());
        var shown = 0, total = 0;
        table.querySelectorAll('tbody > tr').forEach(function (tr) {
            if (tr.hasAttribute('data-skip-search')) return;
            total++;
            var match = window.appSearchNormalize(tr.textContent).indexOf(q) >= 0;
            tr.style.display = match ? '' : 'none';
            if (match) shown++;
        });
        var nr = table.querySelector('[data-no-results]');
        if (nr) nr.style.display = (total && shown === 0) ? '' : 'none';
        lsRefreshAggregates();
    }, true);
})();
