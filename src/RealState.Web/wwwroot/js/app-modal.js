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

    function bindForm() {
        // Fire an initial change on cascade selects (e.g. project → units) so dependent lists
        // filter correctly on open and after a validation re-render.
        document.querySelectorAll('#appModalBody select[data-cascade-init]').forEach(function (s) {
            s.dispatchEvent(new Event('change'));
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
})();
