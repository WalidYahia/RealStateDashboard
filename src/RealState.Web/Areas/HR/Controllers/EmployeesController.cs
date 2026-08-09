using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Hr.Models;

namespace RealState.Web.Areas.Hr.Controllers;

[Area("Hr")]
[Authorize(Policy = PermissionNames.HrView)]
public class EmployeesController : Controller
{
    private readonly IApplicationDbContext _db;
    public EmployeesController(IApplicationDbContext db) => _db = db;

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);
    private const long MaxFileBytes = 15 * 1024 * 1024;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var deps = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var roles = await _db.JobRoles.ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var rows = (await _db.Employees.OrderBy(e => e.FullName).ToListAsync(ct)).Select(e => new EmployeeListItem
        {
            Id = e.Id, Name = e.FullName, Phone = e.Phone,
            Department = e.DepartmentId.HasValue ? deps.GetValueOrDefault(e.DepartmentId.Value, "—") : "—",
            Role = e.JobRoleId.HasValue ? roles.GetValueOrDefault(e.JobRoleId.Value, "—") : "—",
            EmploymentType = e.EmploymentType.Ar(),
            BasicSalary = e.BasicSalary
        }).ToList();
        ViewData["CanManage"] = CanManage();
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_EmployeeForm", await FillAsync(new EmployeeFormModel(), ct));
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return PartialView("_EmployeeForm", await FillAsync(new EmployeeFormModel
        {
            Id = e.Id, FullName = e.FullName, Phone = e.Phone, AltPhone = e.AltPhone, BirthDate = e.BirthDate,
            Nationality = e.Nationality, NationalId = e.NationalId, AcademicQualification = e.AcademicQualification,
            CurrentLocation = e.CurrentLocation, Email = e.Email, DepartmentId = e.DepartmentId, JobRoleId = e.JobRoleId,
            HireDate = e.HireDate, EmploymentType = e.EmploymentType, SocialInsurance = e.SocialInsurance,
            MedicalInsurance = e.MedicalInsurance, BasicSalary = e.BasicSalary, IncentivesCommissions = e.IncentivesCommissions
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(EmployeeFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_EmployeeForm", await FillAsync(model, ct));

        // Only employees in a salesperson-flagged role are salespersons (assignable to customers).
        var isSales = model.JobRoleId.HasValue && await _db.JobRoles.AnyAsync(r => r.Id == model.JobRoleId && r.IsSalesperson, ct);
        if (model.Id == Guid.Empty)
        {
            var e = Apply(new Employee(), model);
            e.Type = isSales ? EmployeeType.Salesperson : EmployeeType.Other;
            _db.Employees.Add(e);
        }
        else
        {
            var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (e is null) return NotFound();
            Apply(e, model);
            e.Type = isSales ? EmployeeType.Salesperson : EmployeeType.Other;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ الموظف «{model.FullName}».";
        return Json(new { ok = true });
    }

    private static Employee Apply(Employee e, EmployeeFormModel m)
    {
        e.FullName = m.FullName; e.Phone = m.Phone; e.AltPhone = m.AltPhone; e.BirthDate = m.BirthDate;
        e.Nationality = m.Nationality; e.NationalId = m.NationalId; e.AcademicQualification = m.AcademicQualification;
        e.CurrentLocation = m.CurrentLocation; e.Email = m.Email; e.DepartmentId = m.DepartmentId; e.JobRoleId = m.JobRoleId;
        e.HireDate = m.HireDate; e.EmploymentType = m.EmploymentType; e.SocialInsurance = m.SocialInsurance;
        e.MedicalInsurance = m.MedicalInsurance; e.BasicSalary = m.BasicSalary; e.IncentivesCommissions = m.IncentivesCommissions;
        return e;
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        if (await _db.Advances.AnyAsync(a => a.EmployeeId == id, ct) || await _db.Rewards.AnyAsync(r => r.EmployeeId == id, ct)
            || await _db.Vacations.AnyAsync(v => v.EmployeeId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف موظف لديه إجازات أو سلف أو مكافآت.";
            return RedirectToAction(nameof(Index));
        }
        var atts = await _db.EmployeeAttachments.Where(a => a.EmployeeId == id).ToListAsync(ct);
        foreach (var a in atts) _db.EmployeeAttachments.Remove(a);
        _db.Employees.Remove(e);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف الموظف «{e.FullName}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Details + attachments ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        e.Department = e.DepartmentId.HasValue ? await _db.Departments.FirstOrDefaultAsync(d => d.Id == e.DepartmentId, ct) : null;
        e.JobRole = e.JobRoleId.HasValue ? await _db.JobRoles.FirstOrDefaultAsync(r => r.Id == e.JobRoleId, ct) : null;
        ViewBag.Attachments = await _db.EmployeeAttachments.Where(a => a.EmployeeId == id).OrderBy(a => a.Kind).ToListAsync(ct);
        ViewData["CanManage"] = CanManage();
        return View(e);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentUpload(Guid employeeId, EmployeeAttachmentKind kind, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) { TempData["ErrorMessage"] = "اختر ملفًا."; return RedirectToAction(nameof(Details), new { id = employeeId }); }
        if (file.Length > MaxFileBytes) { TempData["ErrorMessage"] = "الملف أكبر من 15 ميجابايت."; return RedirectToAction(nameof(Details), new { id = employeeId }); }
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId, ct)) return NotFound();
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        _db.EmployeeAttachments.Add(new EmployeeAttachment
        {
            EmployeeId = employeeId, Kind = kind, FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType, Size = file.Length, Data = ms.ToArray()
        });
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم رفع المرفق.";
        return RedirectToAction(nameof(Details), new { id = employeeId });
    }

    [HttpGet]
    public async Task<IActionResult> AttachmentDownload(Guid id, CancellationToken ct)
    {
        var a = await _db.EmployeeAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName);
    }

    [HttpGet]
    public async Task<IActionResult> AttachmentPreview(Guid id, CancellationToken ct)
    {
        var a = await _db.EmployeeAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{a.FileName}\"";
        return File(a.Data, a.ContentType ?? "application/octet-stream");
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentDelete(Guid id, Guid employeeId, CancellationToken ct)
    {
        var a = await _db.EmployeeAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is not null) { _db.EmployeeAttachments.Remove(a); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = employeeId });
    }

    private async Task<EmployeeFormModel> FillAsync(EmployeeFormModel m, CancellationToken ct)
    {
        m.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.Id.ToString(), Text = d.Name }).ToListAsync(ct);
        m.Roles = await _db.JobRoles.Where(r => r.IsActive).OrderBy(r => r.Name)
            .Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Name }).ToListAsync(ct);
        return m;
    }
}
