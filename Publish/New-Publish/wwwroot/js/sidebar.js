// Side menu collapse.
// Desktop: the chevron in the sidebar header folds the menu into an icon rail (<html class="sb-collapsed">).
// On the rail, hovering (or focusing) an icon shows its name; a group icon (المشتريات، المالية…) pops out
// its sub-pages instead, so every page stays one click away. The choice is saved per browser
// (localStorage 'appSidebar') and applied by the inline script in _Layout's <head> before first paint.
// Phones (≤ 720px): the sidebar is an off-canvas drawer (<html class="sb-open">) opened by ☰ in the top bar.
(function () {
    var KEY = 'appSidebar';
    var root = document.documentElement;
    var mq = window.matchMedia('(max-width: 720px)');
    var sidebar = document.querySelector('.sidebar');
    var nav = document.querySelector('.side-nav');
    if (!sidebar || !nav) return;

    function save(v) { try { localStorage.setItem(KEY, v); } catch (e) { /* private mode: still toggles for this page */ } }
    function isRail() { return !mq.matches && root.classList.contains('sb-collapsed'); }
    function sync() {
        var open = mq.matches ? root.classList.contains('sb-open') : !root.classList.contains('sb-collapsed');
        document.querySelectorAll('.sb-toggle, .sb-collapse').forEach(function (b) { b.setAttribute('aria-expanded', open ? 'true' : 'false'); });
        if (!isRail()) hide(true);
    }
    function closeDrawer() { root.classList.remove('sb-open'); sync(); }

    window.appToggleSidebar = function () {
        if (mq.matches) root.classList.toggle('sb-open');
        else save(root.classList.toggle('sb-collapsed') ? 'collapsed' : 'expanded');
        sync();
    };

    // ---- rail hover label / group pop-out ----
    var fly = document.createElement('div');
    fly.className = 'sb-fly';
    document.body.appendChild(fly);
    var current = null, hideTimer = 0;

    function label(link) {
        var c = link.cloneNode(true);
        c.querySelectorAll('span').forEach(function (s) { s.remove(); });   // drop the icon
        return c.textContent.trim();
    }
    function place(anchor, asMenu) {
        var a = anchor.getBoundingClientRect(), s = sidebar.getBoundingClientRect();
        var rtl = getComputedStyle(sidebar).direction === 'rtl';
        var w = fly.offsetWidth, h = fly.offsetHeight, gap = 10;
        var left = rtl ? s.left - w - gap : s.right + gap;
        var top = asMenu ? a.top - 6 : a.top + (a.height - h) / 2;
        top = Math.max(8, Math.min(top, window.innerHeight - h - 8));
        fly.style.left = Math.max(8, left) + 'px';
        fly.style.top = top + 'px';
    }
    function show(item) {
        clearTimeout(hideTimer);
        if (item === current) return;
        current = item;
        var link = item.querySelector('.nav-link') || item;
        var subs = item.classList.contains('nav-group') ? item.querySelectorAll('.subnav .subnav-link') : [];
        fly.innerHTML = '';
        if (subs.length) {
            fly.className = 'sb-fly menu show';
            fly.setAttribute('role', 'menu');
            var t = document.createElement('div');
            t.className = 'sb-fly-title';
            t.textContent = label(link);
            fly.appendChild(t);
            subs.forEach(function (s) { fly.appendChild(s.cloneNode(true)); });
        } else {
            fly.className = 'sb-fly tip show';
            fly.setAttribute('role', 'tooltip');
            fly.textContent = label(link);
        }
        place(item.querySelector('.nav-parent') || item, subs.length > 0);
    }
    function hide(now) {
        clearTimeout(hideTimer);
        var go = function () { fly.className = 'sb-fly'; current = null; };
        if (now) go(); else hideTimer = setTimeout(go, 160);   // time to move the pointer onto a pop-out
    }
    function itemOf(el) {
        var it = el && el.closest ? el.closest('.side-nav > .nav-group, .side-nav > .nav-link') : null;
        return it && nav.contains(it) ? it : null;
    }

    nav.addEventListener('mouseover', function (e) { if (isRail()) { var it = itemOf(e.target); if (it) show(it); } });
    nav.addEventListener('mouseleave', function () { if (isRail()) hide(false); });
    nav.addEventListener('focusin', function (e) { if (isRail()) { var it = itemOf(e.target); if (it) show(it); } });
    // Touch / click on a group icon (its link only toggles the hidden sub-list) → open the pop-out.
    nav.addEventListener('click', function (e) {
        if (!isRail()) return;
        var it = itemOf(e.target);
        if (it && it.classList.contains('nav-group')) { current = null; show(it); }
    });
    fly.addEventListener('mouseenter', function () { clearTimeout(hideTimer); });
    fly.addEventListener('mouseleave', function () { hide(false); });
    sidebar.addEventListener('scroll', function () { hide(true); });
    window.addEventListener('resize', function () { hide(true); });
    document.addEventListener('click', function (e) {
        if (current && !fly.contains(e.target) && !nav.contains(e.target)) hide(true);
    });

    // ---- drawer / shared ----
    var backdrop = document.querySelector('.sb-backdrop');
    if (backdrop) backdrop.addEventListener('click', closeDrawer);
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        if (current) hide(true);
        if (root.classList.contains('sb-open')) closeDrawer();
    });
    if (mq.addEventListener) mq.addEventListener('change', closeDrawer);   // leaving phone width drops the drawer

    // Keep other open tabs in step.
    window.addEventListener('storage', function (e) {
        if (e.key !== KEY) return;
        root.classList.toggle('sb-collapsed', e.newValue === 'collapsed');
        sync();
    });
    sync();
})();
