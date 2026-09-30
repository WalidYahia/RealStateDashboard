// System-wide preloader: a blocking overlay shown while a save / delete / any submit is in progress.
//  • Normal form submits (full page navigation) — shown until the next page replaces this one.
//  • AJAX saves: any non-GET fetch() (modal forms, ledger, campaigns, …) — shown while requests are pending.
//  • Confirm-guarded forms (data-confirm) — app-modal.js calls appLoader.show() after the user confirms.
// Opt out: <form data-no-loader>, a form/submitter targeting another tab, or fetch(url, { noLoader: true }).
// Custom text: <form data-loader-text="...">. Also blocks double submits while shown.
(function () {
    var DEFAULT_TEXT = 'جارٍ التنفيذ…';
    var pending = 0, sticky = false, safetyTimer = null;

    function node() { return document.getElementById('appLoader'); }

    function setText(text) {
        var t = document.getElementById('appLoaderText');
        if (t) t.textContent = text || DEFAULT_TEXT;
    }

    function render() {
        var n = node(); if (!n) return;
        var on = sticky || pending > 0;
        n.classList.toggle('show', on);
        n.setAttribute('aria-hidden', on ? 'false' : 'true');
        clearTimeout(safetyTimer);
        // Never leave the page locked (e.g. a response that downloads a file instead of navigating).
        if (on) safetyTimer = setTimeout(reset, 45000);
    }

    function reset() { pending = 0; sticky = false; render(); }

    window.appLoader = {
        /** Show until hide() / the page navigates away (used for full-page submits). */
        show: function (text) { sticky = true; setText(text); render(); },
        hide: function () { sticky = false; if (pending === 0) setText(); render(); },
        /** Counted show/hide around an async operation. */
        begin: function (text) { pending++; if (!sticky) setText(text); render(); },
        end: function () { pending = Math.max(0, pending - 1); render(); }
    };

    // ---- normal form submits -------------------------------------------------------------------------
    // Checked after every other handler ran (setTimeout 0): forms handled in JS call preventDefault and are
    // covered by the fetch wrapper / confirm dialog instead.
    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!form || typeof form.getAttribute !== 'function') return;
        setTimeout(function () {
            if (e.defaultPrevented || form.hasAttribute('data-no-loader')) return;
            var target = (e.submitter && e.submitter.getAttribute('formtarget')) || form.getAttribute('target');
            if (target && target !== '_self') return;   // opens in another tab (print / PDF)
            window.appLoader.show(form.getAttribute('data-loader-text'));
        }, 0);
    });

    // ---- AJAX saves: wrap fetch for non-GET requests -------------------------------------------------
    if (window.fetch) {
        var nativeFetch = window.fetch.bind(window);
        window.fetch = function (input, init) {
            var method = ((init && init.method) || (input && input.method) || 'GET').toUpperCase();
            var track = method !== 'GET' && method !== 'HEAD' && !(init && init.noLoader);
            if (!track) return nativeFetch(input, init);
            window.appLoader.begin();
            var p = nativeFetch(input, init);
            p.then(window.appLoader.end, window.appLoader.end);
            return p;
        };
    }

    // Back/forward cache restores the page as it was (overlay shown) — clear it.
    window.addEventListener('pageshow', function (e) { if (e.persisted) reset(); });
})();
