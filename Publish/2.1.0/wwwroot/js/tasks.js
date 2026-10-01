// Cascade the task assignee picker from the selected department. The employee list is embedded in
// the modal as JSON (#taskEmpData); this runs on the parent page because modal inline scripts don't
// execute. Fired on department change and once on modal open (data-cascade-init).
window.taskDeptChange = function (sel) {
    var form = sel.closest('form');
    if (!form) return;
    var emp = form.querySelector('[name="AssigneeEmployeeId"]');
    var dataEl = form.querySelector('#taskEmpData');
    if (!emp || !dataEl) return;

    var all = [];
    try { all = JSON.parse(dataEl.textContent || '[]'); } catch (e) { return; }

    var dep = sel.value;
    var current = emp.getAttribute('data-selected') || emp.value || '';
    var frag = document.createDocumentFragment();
    var ph = document.createElement('option');
    ph.value = ''; ph.textContent = '— اختر الموظف —';
    frag.appendChild(ph);

    all.filter(function (e) { return !dep || e.d === dep; }).forEach(function (e) {
        var o = document.createElement('option');
        o.value = e.i; o.textContent = e.n;
        if (e.i === current) o.selected = true;
        frag.appendChild(o);
    });
    emp.innerHTML = '';
    emp.appendChild(frag);
};
