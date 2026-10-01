// Workspace navigation (Views/Shared/_TopNav.cshtml + Views/Workspace/*):
//  • Global search / command palette (Ctrl+K or [data-open-palette]): the user's authorized modules, pages and actions
//    (from #navData, filtered here) + business records from /Workspace/Search (grouped, permission-aware on the server).
//  • Top-bar menus ([data-menu-toggle] → next .tb-pop): «＋ إجراء جديد» and المفضلة (loaded from /Workspace/Favorites).
//  • Favorite stars ([data-fav-key]) on page cards and the current page — toggled per user on the server.
//  • Recent pages: each full page load is recorded (POST /Workspace/Visit, no preloader, not in the activity log).
//  • Quick actions to a list page open its create form: URL #new clicks the page's [data-quick-new] button.
(function () {
    var dataEl = document.getElementById('navData');
    if (!dataEl) return;
    var data = {};
    try { data = JSON.parse(dataEl.textContent || '{}'); } catch (e) { }

    function token() { var t = document.querySelector('input[name="__RequestVerificationToken"]'); return t ? t.value : ''; }
    function post(url, body) {
        return fetch(url, {
            method: 'POST', noLoader: true, credentials: 'same-origin',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'RequestVerificationToken': token(), 'X-Requested-With': 'XMLHttpRequest' },
            body: new URLSearchParams(body || {}).toString()
        });
    }
    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
    var norm = window.appSearchNormalize || function (s) { return String(s || '').toLowerCase(); };

    // ---------------- favorites ----------------
    var favKeys = {};
    function paintStars() {
        document.querySelectorAll('[data-fav-key]').forEach(function (b) {
            var on = !!favKeys[b.getAttribute('data-fav-key')];
            b.classList.toggle('on', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
            b.title = on ? 'إزالة من المفضلة' : 'إضافة إلى المفضلة';
        });
    }
    function loadFavorites() {
        if (!data.favoritesUrl) return;
        fetch(data.favoritesUrl, { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
            .then(function (r) { return r.ok ? r.json() : []; })
            .then(function (list) {
                favKeys = {};
                list.forEach(function (f) { favKeys[f.key] = true; });
                paintStars();
                var box = document.querySelector('[data-fav-list]');
                if (box) box.innerHTML = list.length
                    ? list.map(function (f) { return '<a role="menuitem" href="' + esc(f.url) + '"><span>' + esc(f.title) + '</span><small>' + esc(f.module) + '</small></a>'; }).join('')
                    : '<span class="tb-pop-empty">لا توجد صفحات مفضلة بعد — اضغط ☆ بجوار عنوان أي صفحة.</span>';
            }).catch(function () { });
    }
    document.addEventListener('click', function (e) {
        var b = e.target.closest ? e.target.closest('[data-fav-key]') : null;
        if (!b || !data.toggleUrl) return;
        e.preventDefault();
        var key = b.getAttribute('data-fav-key');
        post(data.toggleUrl, { key: key }).then(function (r) { return r.ok ? r.json() : null; }).then(function (res) {
            if (!res || !res.ok) return;
            if (res.on) favKeys[key] = true; else delete favKeys[key];
            paintStars();
            loadFavorites();   // refresh the menu
        });
    });

    // ---------------- top-bar menus ----------------
    function closeMenus(except) {
        document.querySelectorAll('.tb-menu').forEach(function (m) {
            if (m === except) return;
            var pop = m.querySelector('.tb-pop'), btn = m.querySelector('[data-menu-toggle]');
            if (pop) pop.hidden = true;
            if (btn) btn.setAttribute('aria-expanded', 'false');
        });
    }
    document.addEventListener('click', function (e) {
        var btn = e.target.closest ? e.target.closest('[data-menu-toggle]') : null;
        if (btn) {
            var menu = btn.closest('.tb-menu'), pop = menu.querySelector('.tb-pop');
            var open = pop.hidden;
            closeMenus(menu);
            pop.hidden = !open;
            btn.setAttribute('aria-expanded', open ? 'true' : 'false');
            if (open) { var first = pop.querySelector('a'); if (first) first.focus(); }
            return;
        }
        if (!(e.target.closest && e.target.closest('.tb-pop'))) closeMenus(null);
    });

    // ---------------- command palette ----------------
    var pal = null, input, list, items = [], cursor = 0, timer = 0, seq = 0;
    function buildPalette() {
        pal = document.createElement('div');
        pal.className = 'pal'; pal.hidden = true;
        pal.innerHTML =
            '<div class="pal-box" role="dialog" aria-modal="true" aria-label="البحث في النظام">' +
            '<div class="pal-input"><svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.5-3.5"/></svg>' +
            '<input type="text" placeholder="ابحث عن صفحة أو عميل أو مورد أو صنف أو مشروع أو فاتورة…" autocomplete="off" aria-label="بحث" /><kbd>Esc</kbd></div>' +
            '<div class="pal-list" role="listbox"></div></div>';
        document.body.appendChild(pal);
        input = pal.querySelector('input'); list = pal.querySelector('.pal-list');
        pal.addEventListener('mousedown', function (e) { if (e.target === pal) closePalette(); });
        input.addEventListener('input', function () { render(input.value); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown') { e.preventDefault(); move(1); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); move(-1); }
            else if (e.key === 'Enter') { e.preventDefault(); var it = items[cursor]; if (it) location.href = it.getAttribute('href'); }
            else if (e.key === 'Escape') { e.preventDefault(); closePalette(); }
        });
        list.addEventListener('mousemove', function (e) {
            var a = e.target.closest('.pal-item'); if (a) { cursor = items.indexOf(a); paintCursor(); }
        });
    }
    function move(d) { if (!items.length) return; cursor = (cursor + d + items.length) % items.length; paintCursor(true); }
    function paintCursor(scroll) {
        items.forEach(function (a, i) { a.classList.toggle('on', i === cursor); a.setAttribute('aria-selected', i === cursor ? 'true' : 'false'); });
        if (scroll && items[cursor]) items[cursor].scrollIntoView({ block: 'nearest' });
    }
    function group(title, rows) {
        if (!rows.length) return '';
        return '<div class="pal-group">' + esc(title) + '</div>' + rows.map(function (r) {
            return '<a class="pal-item" role="option" href="' + esc(r.u) + '"><span class="pal-ico">' + (r.i || '') + '</span>' +
                '<span class="pal-text"><span>' + esc(r.t) + '</span>' + (r.s ? '<small>' + esc(r.s) + '</small>' : '') + '</span></a>';
        }).join('');
    }
    function render(q) {
        var nq = norm(q.trim());
        var html;
        if (!nq) {
            html = group('الوحدات', (data.modules || []).map(function (m) { return { t: m.t, u: m.u, i: m.i, s: 'مساحة العمل' }; }))
                 + group('إجراء جديد', (data.actions || []).slice(0, 6).map(function (a) { return { t: a.t, u: a.u, i: a.i }; }));
        } else {
            var hit = function (s) { return norm(s).indexOf(nq) >= 0; };
            var pages = (data.pages || []).filter(function (p) { return hit(p.t) || hit(p.m) || hit(p.d); }).slice(0, 8)
                .map(function (p) { return { t: p.t, u: p.u, i: p.i, s: p.m }; });
            var mods = (data.modules || []).filter(function (m) { return hit(m.t); }).map(function (m) { return { t: m.t, u: m.u, i: m.i, s: 'مساحة العمل' }; });
            var acts = (data.actions || []).filter(function (a) { return hit(a.t); }).slice(0, 5).map(function (a) { return { t: '＋ ' + a.t, u: a.u, i: a.i }; });
            html = group('الصفحات', mods.concat(pages)) + group('إجراء جديد', acts) + '<div class="pal-remote"></div>';
            if (q.trim().length >= 2) searchRecords(q.trim());
        }
        list.innerHTML = html || '<div class="pal-empty">لا توجد نتائج.</div>';
        items = Array.prototype.slice.call(list.querySelectorAll('.pal-item'));
        cursor = 0; paintCursor();
    }
    function searchRecords(q) {
        clearTimeout(timer);
        var my = ++seq;
        timer = setTimeout(function () {
            var box = list.querySelector('.pal-remote');
            if (box) box.innerHTML = '<div class="pal-empty">جارٍ البحث في السجلات…</div>';
            fetch(data.searchUrl + '?q=' + encodeURIComponent(q), { headers: { 'X-Requested-With': 'XMLHttpRequest' }, credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : []; })
                .then(function (groups) {
                    if (my !== seq) return;   // a newer query is on its way
                    var b = list.querySelector('.pal-remote'); if (!b) return;
                    b.innerHTML = groups.map(function (g) {
                        return group(g.title, g.items.map(function (it) { return { t: it.title, s: it.sub, u: it.url, i: g.icon }; }));
                    }).join('');
                    if (!b.innerHTML && !list.querySelector('.pal-item')) list.innerHTML = '<div class="pal-empty">لا توجد نتائج.</div>';
                    var keep = items[cursor] ? items[cursor].getAttribute('href') : null;
                    items = Array.prototype.slice.call(list.querySelectorAll('.pal-item'));
                    cursor = Math.max(0, items.findIndex(function (a) { return a.getAttribute('href') === keep; }));
                    paintCursor();
                }).catch(function () { var b = list.querySelector('.pal-remote'); if (b) b.innerHTML = ''; });
        }, 250);
    }
    function openPalette() {
        if (!pal) buildPalette();
        closeMenus(null);
        pal.hidden = false;
        document.documentElement.classList.add('pal-open');
        input.value = ''; render('');
        setTimeout(function () { input.focus(); }, 0);
    }
    function closePalette() {
        if (!pal) return;
        pal.hidden = true;
        document.documentElement.classList.remove('pal-open');
    }
    window.appOpenPalette = openPalette;
    document.addEventListener('click', function (e) {
        if (e.target.closest && e.target.closest('[data-open-palette]')) { e.preventDefault(); openPalette(); }
    });
    document.addEventListener('keydown', function (e) {
        if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K' || e.code === 'KeyK')) { e.preventDefault(); if (pal && !pal.hidden) closePalette(); else openPalette(); }
        else if (e.key === 'Escape') closeMenus(null);
    });

    // ---------------- recent pages ----------------
    function recordVisit() {
        if (!data.visitUrl) return;
        var title = document.body.getAttribute('data-visit-title') || document.title;
        post(data.visitUrl, { url: location.pathname + location.search, title: title }).catch(function () { });
    }

    // ---------------- quick action → create form ----------------
    function openQuickNew() {
        if (location.hash !== '#new') return;
        if (history.replaceState) history.replaceState(null, '', location.pathname + location.search);
        var b = document.querySelector('[data-quick-new]');
        if (b) setTimeout(function () { b.click(); }, 60);
    }

    function init() { loadFavorites(); openQuickNew(); setTimeout(recordVisit, 400); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
