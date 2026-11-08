// Shared filter bar (Views/Shared/_FilterBar.cshtml): quick-range chips + active-state highlighting.
// Presets only fill the from/to inputs (a date-time range gets 00:00 / 23:59); an empty preset («الكل») clears
// them; data-autosubmit applies the filter immediately. Searching still happens only via the بحث button.
(function () {
    function rangeOf(el) { return el.closest('[data-fb-range]'); }

    function inputs(range) {
        var form = range.closest('form');
        return {
            from: form.querySelector('[name="' + range.getAttribute('data-from-name') + '"]'),
            to: form.querySelector('[name="' + range.getAttribute('data-to-name') + '"]')
        };
    }

    function setValue(input, date, isEnd) {
        if (!input) return;
        if (!date) { input.value = ''; return; }
        input.value = input.type === 'datetime-local' ? date + (isEnd ? 'T23:59' : 'T00:00') : date;
    }

    // Highlight the chip whose range equals the current inputs (dates compared without the time part).
    function sync(range) {
        var io = inputs(range);
        var f = io.from ? (io.from.value || '').slice(0, 10) : '';
        var t = io.to ? (io.to.value || '').slice(0, 10) : '';
        range.querySelectorAll('.fb-chip').forEach(function (c) {
            c.classList.toggle('active', c.getAttribute('data-from') === f && c.getAttribute('data-to') === t);
        });
        range.classList.toggle('fb-on', !!(f || t));
    }

    document.addEventListener('click', function (e) {
        var chip = e.target.closest ? e.target.closest('.fb-chip') : null;
        if (!chip) return;
        e.preventDefault();
        var range = rangeOf(chip); if (!range) return;
        var io = inputs(range);
        setValue(io.from, chip.getAttribute('data-from'), false);
        setValue(io.to, chip.getAttribute('data-to'), true);
        sync(range);
        if (chip.hasAttribute('data-autosubmit')) {
            var form = range.closest('form');
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
        }
    });

    // Typing a date / picking a value updates the highlight live.
    document.addEventListener('input', function (e) {
        var el = e.target;
        if (!el || !el.closest) return;
        var range = rangeOf(el);
        if (range) { sync(range); return; }
        var field = el.closest('.fb-field');
        if (field && el.type !== 'checkbox') field.classList.toggle('fb-on', !!el.value);
    });
    document.addEventListener('change', function (e) {
        var el = e.target;
        var field = el && el.closest ? el.closest('.fb-field') : null;
        if (field && field.hasAttribute('data-fb-sort')) return;   // «الترتيب» — handled below
        if (field && el.matches('select')) field.classList.toggle('fb-on', !!el.value);
        // A checkbox group filters only while some (not all) of its options are ticked.
        else if (field && el.matches('input[type="checkbox"]'))
            field.classList.toggle('fb-on', !!field.querySelector('input[type="checkbox"]:checked') && !!field.querySelector('input[type="checkbox"]:not(:checked)'));
    });

    // Phones: tapping the header (or its chevron) folds / unfolds the filters; the reset link keeps its own job.
    document.addEventListener('click', function (e) {
        var head = e.target.closest ? e.target.closest('.fb-collapsible .fb-head') : null;
        if (!head || e.target.closest('.fb-reset')) return;
        var toggle = head.querySelector('.fb-toggle');
        if (!toggle || getComputedStyle(toggle).display === 'none') return;
        var open = head.closest('.fb').classList.toggle('fb-open');
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    });

    function init() { document.querySelectorAll('[data-fb-range]').forEach(sync); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();

// «الترتيب» ([data-fb-sort] in the shared filter bar): sorts the page's tables — every table.rs-table after the bar (up
// to the next filter bar), outside modals — by a column, ascending / descending, in the browser.
//  • The column list is the tables' header labels (merged when there are several tables; each table that has the chosen
//    column is sorted by it). The «#» numbering column and unlabelled (actions) columns are left out.
//  • Numbers, dates (yyyy/MM/dd [HH:mm]) and text each compare as such; a cell's data-sort overrides its text. Empty /
//    «—» cells go last either way.
//  • Placeholder rows stay put: the ones before the first data row (e.g. «الرصيد الافتتاحي للفترة») at the top, the
//    rest at the bottom. Rows marked data-sort-follow move with the data row above them («تفصيلي» item rows).
//  • «الترتيب الافتراضي» restores the order the page came in. grid.js re-pages / re-numbers after each sort.
// The selects post as sortBy / sortDir, so a filter submit (and a reload) keeps the sort.
(function () {
    var collator = new Intl.Collator('ar', { numeric: true, sensitivity: 'base' });
    function norm(s) { return window.appSearchNormalize ? window.appSearchNormalize(s) : (s || '').toLowerCase(); }
    function label(th) { return (th.textContent || '').replace(/\s+/g, ' ').trim(); }

    function isPlaceholder(tr) {
        return tr.hasAttribute('data-skip-search') || tr.hasAttribute('data-no-results') || tr.hasAttribute('data-skip-page')
            || (tr.cells.length === 1 && (tr.cells[0].colSpan || 1) > 1);
    }

    // The tables a bar sorts: after it in the page, before the next bar, not inside a modal / popup or another table.
    function tablesOf(field) {
        var bar = field.closest('.fb');
        var next = null, bars = document.querySelectorAll('.fb');
        for (var i = 0; i < bars.length; i++) {
            if (bars[i] !== bar && (bar.compareDocumentPosition(bars[i]) & Node.DOCUMENT_POSITION_FOLLOWING)) { next = bars[i]; break; }
        }
        return Array.prototype.filter.call(document.querySelectorAll('table.rs-table'), function (t) {
            return (bar.compareDocumentPosition(t) & Node.DOCUMENT_POSITION_FOLLOWING)
                && !(next && (next.compareDocumentPosition(t) & Node.DOCUMENT_POSITION_FOLLOWING))
                && !t.hasAttribute('data-no-sort') && !t.closest('.modal, .swal2-container')
                && !(t.parentElement && t.parentElement.closest('table'))
                && t.tHead && t.tHead.rows.length && t.tBodies.length;
        });
    }

    // The sortable header cells of a table (its last header row): label + column position (colspans counted).
    function headers(table) {
        var row = table.tHead.rows[table.tHead.rows.length - 1], col = 0, out = [];
        Array.prototype.forEach.call(row.cells, function (th) {
            var text = label(th);
            if (text && text !== '#' && !th.classList.contains('grid-idx') && !th.hasAttribute('data-no-sort')) out.push({ text: text, col: col });
            col += th.colSpan || 1;
        });
        return out;
    }

    function cellAt(tr, col) {
        var c = 0;
        for (var i = 0; i < tr.cells.length; i++) {
            if (c === col) return tr.cells[i];
            c += tr.cells[i].colSpan || 1;
            if (c > col) return null;
        }
        return null;
    }

    // Sort keys of one value: a date, a number (one number in the text — a label before it is fine, e.g. «SI-2026…»,
    // a word after it too, e.g. «2 صنف»), or text.
    function dateKey(s) {
        var m = s.match(/^(\d{4})[\/\-](\d{1,2})[\/\-](\d{1,2})(?:[ T]+(\d{1,2}):(\d{2}))?/);
        return m ? ((+m[1] * 100 + +m[2]) * 100 + +m[3]) * 10000 + (+(m[4] || 0)) * 100 + +(m[5] || 0) : null;
    }
    function numberKey(s) {
        var m = s.replace(/−/g, '-').match(/^([^0-9+\-]*)([+\-]?)\s*(\d+(?:\.\d+)?)([^0-9]*)$/);
        if (!m) return null;
        var v = parseFloat(m[3]);
        return m[2] === '-' && m[1].trim() === '' ? -v : v;
    }

    function sortTable(table, text, desc) {
        var body = table.tBodies[0];
        var h = headers(table).filter(function (x) { return x.text === text; })[0];
        var rows = Array.prototype.slice.call(body.rows);
        // The page's own order, remembered on first use («الترتيب الافتراضي»).
        var seen = rows.filter(function (r) { return r.__sortOrig !== undefined; }).length;
        rows.forEach(function (r, i) { if (r.__sortOrig === undefined) r.__sortOrig = seen + i; });

        var lead = [], tail = [], units = [];
        rows.forEach(function (tr) {
            if (!isPlaceholder(tr)) units.push({ row: tr, follow: [] });
            else if (tr.hasAttribute('data-sort-follow') && units.length) units[units.length - 1].follow.push(tr);
            else (units.length ? tail : lead).push(tr);
        });
        if (!h && text) return;   // this table doesn't have the column

        if (!text) units.sort(function (a, b) { return a.row.__sortOrig - b.row.__sortOrig; });
        else {
            var vals = units.map(function (u) {
                var c = cellAt(u.row, h.col);
                var raw = c ? (c.getAttribute('data-sort') || c.textContent || '') : '';
                var s = norm(raw).replace(/\s+/g, ' ').trim();
                return s === '' || s === '—' || s === '-' ? null : s;
            });
            var present = vals.filter(function (v) { return v !== null; });
            var kind = present.length && present.every(function (v) { return dateKey(v) !== null; }) ? 'date'
                : present.length && present.every(function (v) { return numberKey(v) !== null; }) ? 'number' : 'text';
            units.forEach(function (u, i) {
                var v = vals[i];
                u.key = v === null ? null : kind === 'date' ? dateKey(v) : kind === 'number' ? numberKey(v) : v;
            });
            units.sort(function (a, b) {
                if (a.key === null || b.key === null) return a.key === b.key ? a.row.__sortOrig - b.row.__sortOrig : (a.key === null ? 1 : -1);
                var d = kind === 'text' ? collator.compare(a.key, b.key) : a.key - b.key;
                return (desc ? -d : d) || a.row.__sortOrig - b.row.__sortOrig;
            });
        }

        var frag = document.createDocumentFragment();
        lead.forEach(function (r) { frag.appendChild(r); });
        units.forEach(function (u) { frag.appendChild(u.row); u.follow.forEach(function (r) { frag.appendChild(r); }); });
        tail.forEach(function (r) { frag.appendChild(r); });
        body.appendChild(frag);
        if (typeof table.__gridRender === 'function') table.__gridRender();
    }

    function apply(field) {
        var col = field.querySelector('[data-fb-sort-col]'), dir = field.querySelector('[data-fb-sort-dir]');
        field.classList.toggle('fb-on', !!col.value);
        tablesOf(field).forEach(function (t) { sortTable(t, col.value, dir.value === 'desc'); });
    }

    // Fills the column list from the tables' headers (again on open — a tab may have loaded more tables since).
    function fill(field) {
        var col = field.querySelector('[data-fb-sort-col]');
        var current = col.value || col.getAttribute('data-value') || '';
        var labels = [];
        tablesOf(field).forEach(function (t) {
            headers(t).forEach(function (x) { if (labels.indexOf(x.text) < 0) labels.push(x.text); });
        });
        while (col.options.length > 1) col.remove(1);
        labels.forEach(function (text) { col.add(new Option(text, text, false, text === current)); });
        if (current && labels.indexOf(current) < 0) col.value = '';
        field.hidden = labels.length === 0;
        // A bar with nothing else in its body shouldn't show an empty panel.
        var body = field.closest('.fb-body');
        if (body) body.hidden = field.hidden && !body.querySelector('[data-fb-range], .fb-field:not([data-fb-sort])');
        return labels.length > 0;
    }

    function init(field) {
        if (field.__fbSort) return;
        field.__fbSort = true;
        if (fill(field) && field.querySelector('[data-fb-sort-col]').value) apply(field);   // the sort posted with the filter
        field.addEventListener('change', function () { apply(field); });
        field.querySelector('[data-fb-sort-col]').addEventListener('focus', function () { fill(field); });
    }

    function scan(root) { (root || document).querySelectorAll('[data-fb-sort]').forEach(init); }
    // After grid.js has set up the tables (both run on DOMContentLoaded); bars loaded later (report tabs) as they appear.
    function start() {
        setTimeout(function () { scan(document); }, 0);
        new MutationObserver(function (list) {
            list.forEach(function (m) {
                m.addedNodes.forEach(function (n) {
                    if (n.nodeType === 1 && (n.matches('[data-fb-sort]') || n.querySelector('[data-fb-sort]')))
                        setTimeout(function () { scan(n.parentNode || document); }, 0);
                });
            });
        }).observe(document.body, { childList: true, subtree: true });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start); else start();
})();
