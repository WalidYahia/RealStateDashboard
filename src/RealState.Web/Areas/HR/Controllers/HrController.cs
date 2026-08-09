using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Hr.Models;

namespace RealState.Web.Areas.Hr.Controllers;

[Area("Hr")]
[Authorize(Policy = PermissionNames.HrView)]
public class HrController : Controller
{
    private readonly IApplicationDbContext _db;
    public HrController(IApplicationDbContext db) => _db = db;

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);

    // ---------- Settings landing ----------
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var attendance = await EnsureAttendanceAsync(ct);
        await EnsureLateRulesAsync(ct);
        var vm = new HrSettingsVm
        {
            Departments = await _db.Departments.OrderBy(d => d.Name).ToListAsync(ct),
            Roles = await _db.JobRoles.OrderBy(r => r.Name).ToListAsync(ct),
            Attendance = new AttendanceFormModel
            {
                TimeIn = attendance.TimeIn, TimeOut = attendance.TimeOut,
                OvertimeEnabled = attendance.OvertimeEnabled, OvertimeNormalHours = attendance.OvertimeNormalHours
            },
            LateRules = await _db.LateDeductionRules.OrderBy(r => r.Bracket).ToListAsync(ct),
        };
        ViewData["CanManage"] = CanManage();
        return View(vm);
    }

    // ---------- Departments ----------
    [HttpGet]
    public async Task<IActionResult> DepartmentForm(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        ViewData["FormAction"] = "DepartmentForm";
        if (id is null) return PartialView("_NameForm", new NameFormModel { });
        var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        return PartialView("_NameForm", new NameFormModel { Id = d.Id, Name = d.Name, IsActive = d.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DepartmentForm(NameFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        ViewData["FormAction"] = "DepartmentForm";
        if (!ModelState.IsValid) return PartialView("_NameForm", model);
        if (model.Id == Guid.Empty) _db.Departments.Add(new Department { Name = model.Name, IsActive = model.IsActive });
        else { var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == model.Id, ct); if (d is null) return NotFound(); d.Name = model.Name; d.IsActive = model.IsActive; }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DepartmentDelete(Guid id, CancellationToken ct)
    {
        var d = await _db.Departments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is not null && !await _db.Employees.AnyAsync(e => e.DepartmentId == id, ct)) { _db.Departments.Remove(d); await _db.SaveChangesAsync(ct); }
        else if (d is not null) TempData["ErrorMessage"] = "لا يمكن حذف قسم مرتبط بموظفين.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Job roles ----------
    [HttpGet]
    public async Task<IActionResult> RoleForm(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        ViewData["FormAction"] = "RoleForm";
        if (id is null) return PartialView("_NameForm", new NameFormModel { });
        var r = await _db.JobRoles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        return PartialView("_NameForm", new NameFormModel { Id = r.Id, Name = r.Name, IsActive = r.IsActive, IsSalesperson = r.IsSalesperson });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleForm(NameFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        ViewData["FormAction"] = "RoleForm";
        if (!ModelState.IsValid) return PartialView("_NameForm", model);
        if (model.Id == Guid.Empty) _db.JobRoles.Add(new JobRole { Name = model.Name, IsActive = model.IsActive, IsSalesperson = model.IsSalesperson });
        else { var r = await _db.JobRoles.FirstOrDefaultAsync(x => x.Id == model.Id, ct); if (r is null) return NotFound(); r.Name = model.Name; r.IsActive = model.IsActive; r.IsSalesperson = model.IsSalesperson; }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RoleDelete(Guid id, CancellationToken ct)
    {
        var r = await _db.JobRoles.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is not null && !await _db.Employees.AnyAsync(e => e.JobRoleId == id, ct)) { _db.JobRoles.Remove(r); await _db.SaveChangesAsync(ct); }
        else if (r is not null) TempData["ErrorMessage"] = "لا يمكن حذف وظيفة مرتبطة بموظفين.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Attendance settings ----------
    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAttendance(AttendanceFormModel model, CancellationToken ct)
    {
        var a = await EnsureAttendanceAsync(ct);
        a.TimeIn = model.TimeIn; a.TimeOut = model.TimeOut;
        a.OvertimeEnabled = model.OvertimeEnabled; a.OvertimeNormalHours = model.OvertimeNormalHours;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ إعدادات الحضور.";
        return Redirect(Url.Action(nameof(Index)) + "#att");
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLateRules(LateRulesFormModel model, CancellationToken ct)
    {
        await EnsureLateRulesAsync(ct);
        var rules = await _db.LateDeductionRules.ToListAsync(ct);
        foreach (var input in model.Rules ?? new())
        {
            var rule = rules.FirstOrDefault(r => r.Bracket == input.Bracket);
            if (rule is null) continue;
            rule.Fraction = input.Fraction;
            rule.IsActive = input.IsActive;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ قواعد الخصم.";
        return Redirect(Url.Action(nameof(Index)) + "#att");
    }

    // ---------- helpers ----------
    private async Task<AttendanceSetting> EnsureAttendanceAsync(CancellationToken ct)
    {
        var a = await _db.AttendanceSettings.FirstOrDefaultAsync(ct);
        if (a is null) { a = new AttendanceSetting(); _db.AttendanceSettings.Add(a); await _db.SaveChangesAsync(ct); }
        return a;
    }

    private async Task EnsureLateRulesAsync(CancellationToken ct)
    {
        var existing = await _db.LateDeductionRules.Select(r => r.Bracket).ToListAsync(ct);
        var added = false;
        foreach (LateBracket b in Enum.GetValues<LateBracket>())
            if (!existing.Contains(b)) { _db.LateDeductionRules.Add(new LateDeductionRule { Bracket = b, Fraction = DeductionFraction.None, IsActive = false }); added = true; }
        if (added) await _db.SaveChangesAsync(ct);
    }
}
