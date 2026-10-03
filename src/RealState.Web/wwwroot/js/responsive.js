// Phones / narrow screens: a wide table scrolls inside its card instead of widening the whole page.
// Every table in the page content or the shared modal is wrapped once in <div class="table-scroll"> (inline
// overflow-x so grid.js keeps its toolbar outside the scroller). Tables added later (modals, report tabs, AJAX) are
// wrapped as they appear.
(function () {
    function scrollableParent(t) {
        var p = t.parentElement;
        if (!p) return true;
        if (p.classList.contains('table-scroll')) return true;
        var ox = getComputedStyle(p).overflowX;
        return ox === 'auto' || ox === 'scroll';
    }
    function wrap(root) {
        (root || document).querySelectorAll('.content table, #appModalBody table').forEach(function (t) {
            if (t.closest('.table-scroll') || scrollableParent(t)) return;
            var w = document.createElement('div');
            w.className = 'table-scroll';
            w.style.overflowX = 'auto';
            t.parentNode.insertBefore(w, t);
            w.appendChild(t);
        });
    }
    function init() {
        wrap(document);
        var pending = false;
        new MutationObserver(function () {
            if (pending) return;
            pending = true;
            requestAnimationFrame(function () { pending = false; wrap(document); });
        }).observe(document.body, { childList: true, subtree: true });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
