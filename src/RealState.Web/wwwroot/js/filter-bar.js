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
        if (field) field.classList.toggle('fb-on', !!el.value);
    });
    document.addEventListener('change', function (e) {
        var el = e.target;
        var field = el && el.closest ? el.closest('.fb-field') : null;
        if (field && el.matches('select')) field.classList.toggle('fb-on', !!el.value);
    });

    function init() { document.querySelectorAll('[data-fb-range]').forEach(sync); }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
