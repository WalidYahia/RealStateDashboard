using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Tasks.Models;

namespace RealState.Web.Areas.Tasks.Controllers;

[Area("Tasks")]
[Authorize]
public class TasksController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private const long MaxFileBytes = 15 * 1024 * 1024;

    public TasksController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool CanView() => User.HasClaim("permission", PermissionNames.TasksView);
    private bool CanCreate() => User.HasClaim("permission", PermissionNames.TasksCreate);
    private bool CanEdit() => User.HasClaim("permission", PermissionNames.TasksEdit);
    private bool CanDelete() => User.HasClaim("permission", PermissionNames.TasksDelete);
    private string CurrentName() => User.FindFirst("full_name")?.Value ?? User.Identity?.Name ?? "—";

    private async Task<List<Guid>> MyEmployeeIdsAsync(CancellationToken ct)
    {
        var uid = _currentUser.UserId;
        if (uid is null) return new List<Guid>();
        return await _db.Employees.Where(e => e.UserId == uid).Select(e => e.Id).ToListAsync(ct);
    }

    // ---------------- List of all tasks ----------------
    [Authorize(Policy = PermissionNames.TasksView)]
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? assigneeId, Guid? assignedById, Guid? departmentId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var depNames = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var q = _db.WorkTasks.AsNoTracking().AsQueryable();
        if (from.HasValue) q = q.Where(t => t.AssignedOn >= from.Value.Date);
        if (to.HasValue) q = q.Where(t => t.AssignedOn < to.Value.Date.AddDays(1));
        if (assigneeId.HasValue) q = q.Where(t => t.AssigneeEmployeeId == assigneeId.Value);
        if (assignedById.HasValue) q = q.Where(t => t.AssignedByUserId == assignedById.Value);
        if (departmentId.HasValue) q = q.Where(t => t.DepartmentId == departmentId.Value);

        var tasks = await q.OrderByDescending(t => t.Number).ToListAsync(ct);

        var vm = new TaskListVm
        {
            Rows = tasks.Select(t => Row(t, empNames, depNames)).ToList(),
            From = from, To = to, AssigneeId = assigneeId, AssignedById = assignedById, DepartmentId = departmentId,
            Employees = await EmployeeOptionsAsync(ct),
            Departments = await DepartmentOptionsAsync(ct),
            Assigners = await AssignerOptionsAsync(ct)
        };
        ViewData["CanCreate"] = CanCreate();
        ViewData["CanEdit"] = CanEdit();
        ViewData["CanDelete"] = CanDelete();
        return View(vm);
    }

    // ---------------- My tasks (two tabs) ----------------
    public async Task<IActionResult> Mine(DateTime? from, DateTime? to, Guid? assignedById, Guid? assignedToId, string? tab, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var depNames = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var myEmpIds = await MyEmployeeIdsAsync(ct);
        var uid = _currentUser.UserId;

        // Tab 1: assigned to me
        var a = _db.WorkTasks.AsNoTracking().Where(t => myEmpIds.Contains(t.AssigneeEmployeeId));
        if (from.HasValue) a = a.Where(t => t.AssignedOn >= from.Value.Date);
        if (to.HasValue) a = a.Where(t => t.AssignedOn < to.Value.Date.AddDays(1));
        if (assignedById.HasValue) a = a.Where(t => t.AssignedByUserId == assignedById.Value);
        var assigned = await a.OrderByDescending(t => t.Number).ToListAsync(ct);

        // Tab 2: assigned by me
        var d = _db.WorkTasks.AsNoTracking().Where(t => t.AssignedByUserId == uid);
        if (from.HasValue) d = d.Where(t => t.AssignedOn >= from.Value.Date);
        if (to.HasValue) d = d.Where(t => t.AssignedOn < to.Value.Date.AddDays(1));
        if (assignedToId.HasValue) d = d.Where(t => t.AssigneeEmployeeId == assignedToId.Value);
        var delegated = await d.OrderByDescending(t => t.Number).ToListAsync(ct);

        var vm = new MyTasksVm
        {
            Assigned = assigned.Select(t => Row(t, empNames, depNames)).ToList(),
            Delegated = delegated.Select(t => Row(t, empNames, depNames)).ToList(),
            From = from, To = to, AssignedById = assignedById, AssignedToId = assignedToId,
            Assigners = await AssignerOptionsAsync(ct),
            Assignees = await EmployeeOptionsAsync(ct),
            ActiveTab = tab == "delegated" ? "delegated" : "assigned"
        };
        ViewData["CanCreate"] = CanCreate();
        return View(vm);
    }

    // ---------------- Details ----------------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (!await CanSeeAsync(t, ct)) return Forbid();

        t.Department = t.DepartmentId.HasValue ? await _db.Departments.FirstOrDefaultAsync(x => x.Id == t.DepartmentId, ct) : null;
        t.Assignee = await _db.Employees.FirstOrDefaultAsync(x => x.Id == t.AssigneeEmployeeId, ct);
        t.Logs = await _db.WorkTaskLogs.Where(l => l.WorkTaskId == id).OrderByDescending(l => l.At).ToListAsync(ct);
        t.Attachments = await _db.WorkTaskAttachments.Where(x => x.WorkTaskId == id).OrderBy(x => x.FileName).ToListAsync(ct);

        ViewData["CanEdit"] = CanEdit();
        ViewData["CanDelete"] = CanDelete();
        ViewData["CanAct"] = await CanActAsync(t, ct);
        return View(t);
    }

    // ---------------- Create / edit ----------------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        var isEdit = id.HasValue && id.Value != Guid.Empty;
        if (isEdit ? !CanEdit() : !CanCreate()) return Forbid();

        var model = new TaskFormModel();
        if (isEdit)
        {
            var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return NotFound();
            model.Id = t.Id; model.AssignedOn = t.AssignedOn; model.DepartmentId = t.DepartmentId;
            model.AssigneeEmployeeId = t.AssigneeEmployeeId; model.StartAt = t.StartAt; model.DueAt = t.DueAt;
            model.Description = t.Description; model.Severity = t.Severity;
        }
        return PartialView("_TaskForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(TaskFormModel model, List<IFormFile>? files, CancellationToken ct)
    {
        var isEdit = model.Id != Guid.Empty;
        if (isEdit ? !CanEdit() : !CanCreate()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_TaskForm", await FillAsync(model, ct));

        if (isEdit)
        {
            var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (t is null) return NotFound();
            t.AssignedOn = model.AssignedOn; t.DepartmentId = model.DepartmentId;
            t.AssigneeEmployeeId = model.AssigneeEmployeeId!.Value; t.StartAt = model.StartAt; t.DueAt = model.DueAt;
            t.Description = model.Description; t.Severity = model.Severity;
            await SaveAttachmentsAsync(t.Id, files, ct);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تحديث المهمة T-{t.Number}.";
        }
        else
        {
            var number = (await _db.WorkTasks.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
            var t = new WorkTask
            {
                Number = number, AssignedOn = model.AssignedOn, DepartmentId = model.DepartmentId,
                AssigneeEmployeeId = model.AssigneeEmployeeId!.Value, StartAt = model.StartAt, DueAt = model.DueAt,
                Description = model.Description, Severity = model.Severity, Status = WorkTaskStatus.Todo,
                AssignedByUserId = _currentUser.UserId, AssignedByName = CurrentName()
            };
            _db.WorkTasks.Add(t);
            _db.WorkTaskLogs.Add(new WorkTaskLog { WorkTaskId = t.Id, At = DateTime.Now, ByName = CurrentName(), ByUserId = _currentUser.UserId, Text = "تم إنشاء المهمة." });
            await SaveAttachmentsAsync(t.Id, files, ct);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم إسناد المهمة T-{number}.";
        }
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.TasksDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        foreach (var l in await _db.WorkTaskLogs.Where(x => x.WorkTaskId == id).ToListAsync(ct)) _db.WorkTaskLogs.Remove(l);
        foreach (var a in await _db.WorkTaskAttachments.Where(x => x.WorkTaskId == id).ToListAsync(ct)) _db.WorkTaskAttachments.Remove(a);
        _db.WorkTasks.Remove(t);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المهمة T-{t.Number}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------------- Status + logs ----------------
    [HttpGet]
    public async Task<IActionResult> StatusForm(Guid id, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (!await CanActAsync(t, ct)) return Forbid();
        return PartialView("_StatusForm", new StatusFormModel { TaskId = t.Id, Status = t.Status });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Status(StatusFormModel model, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == model.TaskId, ct);
        if (t is null) return NotFound();
        if (!await CanActAsync(t, ct)) return Forbid();

        if (t.Status != model.Status)
        {
            var old = t.Status;
            t.Status = model.Status;
            // Changing the status auto-adds one log record.
            var text = $"تغيير الحالة من «{old.Ar()}» إلى «{model.Status.Ar()}»";
            if (!string.IsNullOrWhiteSpace(model.Note)) text += $" — {model.Note.Trim()}";
            _db.WorkTaskLogs.Add(new WorkTaskLog { WorkTaskId = t.Id, At = DateTime.Now, ByName = CurrentName(), ByUserId = _currentUser.UserId, Text = text });
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تحديث حالة المهمة T-{t.Number}.";
        }
        return Json(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> LogForm(Guid id, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        if (!await CanActAsync(t, ct)) return Forbid();
        return PartialView("_LogForm", new LogFormModel { TaskId = t.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Log(LogFormModel model, CancellationToken ct)
    {
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == model.TaskId, ct);
        if (t is null) return NotFound();
        if (!await CanActAsync(t, ct)) return Forbid();
        if (!ModelState.IsValid) return PartialView("_LogForm", model);

        _db.WorkTaskLogs.Add(new WorkTaskLog { WorkTaskId = t.Id, At = DateTime.Now, ByName = CurrentName(), ByUserId = _currentUser.UserId, Text = model.Text.Trim() });
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تمت إضافة السجل.";
        return Json(new { ok = true });
    }

    // ---------------- Attachments ----------------
    [HttpGet]
    public async Task<IActionResult> AttachmentDownload(Guid id, CancellationToken ct)
    {
        var a = await _db.WorkTaskAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        var t = await _db.WorkTasks.FirstOrDefaultAsync(x => x.Id == a.WorkTaskId, ct);
        if (t is null || !await CanSeeAsync(t, ct)) return Forbid();
        return File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentDelete(Guid id, Guid taskId, CancellationToken ct)
    {
        if (!CanEdit()) return Forbid();
        var a = await _db.WorkTaskAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is not null) { _db.WorkTaskAttachments.Remove(a); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = taskId });
    }

    // ---------------- helpers ----------------
    private static TaskRow Row(WorkTask t, IReadOnlyDictionary<Guid, string> empNames, IReadOnlyDictionary<Guid, string> depNames) => new()
    {
        Id = t.Id, Number = t.Number, AssignedOn = t.AssignedOn,
        AssignedBy = string.IsNullOrWhiteSpace(t.AssignedByName) ? "—" : t.AssignedByName,
        Assignee = empNames.GetValueOrDefault(t.AssigneeEmployeeId, "—"),
        Department = t.DepartmentId.HasValue ? depNames.GetValueOrDefault(t.DepartmentId.Value, "—") : "—",
        DueAt = t.DueAt, Description = t.Description, Severity = t.Severity, Status = t.Status
    };

    /// <summary>Can the current user see this task (has all-tasks view, or is its assignee/assigner)?</summary>
    private async Task<bool> CanSeeAsync(WorkTask t, CancellationToken ct)
    {
        if (CanView()) return true;
        if (t.AssignedByUserId == _currentUser.UserId) return true;
        var myEmpIds = await MyEmployeeIdsAsync(ct);
        return myEmpIds.Contains(t.AssigneeEmployeeId);
    }

    /// <summary>Can the current user change status / add logs (assignee, assigner, or an editor)?</summary>
    private async Task<bool> CanActAsync(WorkTask t, CancellationToken ct)
    {
        if (CanEdit()) return true;
        if (t.AssignedByUserId == _currentUser.UserId) return true;
        var myEmpIds = await MyEmployeeIdsAsync(ct);
        return myEmpIds.Contains(t.AssigneeEmployeeId);
    }

    private async Task SaveAttachmentsAsync(Guid taskId, List<IFormFile>? files, CancellationToken ct)
    {
        if (files is null) return;
        foreach (var file in files.Where(f => f is { Length: > 0 } && f.Length <= MaxFileBytes))
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            _db.WorkTaskAttachments.Add(new WorkTaskAttachment
            {
                WorkTaskId = taskId, FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType, Size = file.Length, Data = ms.ToArray()
            });
        }
    }

    private async Task<TaskFormModel> FillAsync(TaskFormModel m, CancellationToken ct)
    {
        m.Departments = await DepartmentOptionsAsync(ct);
        // Materialize before ToString(): Guid.ToString() in an EF projection is UPPERCASE, but the
        // Razor-rendered employee data (below) is lowercase — the JS cascade compares them.
        var emps = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.FullName, e.DepartmentId }).ToListAsync(ct);
        m.Employees = emps.Select(e => new EmpOption(e.Id, e.FullName, e.DepartmentId)).ToList();
        return m;
    }

    private async Task<List<SelectListItem>> DepartmentOptionsAsync(CancellationToken ct)
    {
        var deps = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name }).ToListAsync(ct);
        return deps.Select(d => new SelectListItem { Value = d.Id.ToString(), Text = d.Name }).ToList();
    }

    private async Task<List<SelectListItem>> EmployeeOptionsAsync(CancellationToken ct)
    {
        var emps = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.FullName }).ToListAsync(ct);
        return emps.Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToList();
    }

    private async Task<List<SelectListItem>> AssignerOptionsAsync(CancellationToken ct)
    {
        var rows = await _db.WorkTasks.AsNoTracking()
            .Where(t => t.AssignedByUserId != null)
            .Select(t => new { t.AssignedByUserId, t.AssignedByName })
            .Distinct().ToListAsync(ct);
        return rows
            .GroupBy(r => r.AssignedByUserId!.Value)
            .Select(g => new SelectListItem { Value = g.Key.ToString(), Text = g.First().AssignedByName })
            .OrderBy(s => s.Text).ToList();
    }
}
