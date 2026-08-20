using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Identity;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Hr.Models;

namespace RealState.Web.Areas.Hr.Controllers;

[Area("Hr")]
[Authorize(Policy = PermissionNames.HrView)]
public class EmployeesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserService _currentUser;
    public EmployeesController(IApplicationDbContext db, UserManager<ApplicationUser> userManager, ICurrentUserService currentUser)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
    }

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
    public async Task<IActionResult> Print(CancellationToken ct)
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
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", rows);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_EmployeeForm", await FillAsync(new EmployeeFormModel(), ct));
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        var linked = e.UserId is not null ? await _userManager.FindByIdAsync(e.UserId.Value.ToString()) : null;
        var linkedLabel = linked is null ? null
            : string.IsNullOrWhiteSpace(linked.PhoneNumber) || linked.PhoneNumber == linked.UserName
                ? linked.UserName
                : $"{linked.UserName} · {linked.PhoneNumber}";
        return PartialView("_EmployeeForm", await FillAsync(new EmployeeFormModel
        {
            Id = e.Id, FullName = e.FullName, Phone = e.Phone, AltPhone = e.AltPhone, BirthDate = e.BirthDate,
            Nationality = e.Nationality, NationalId = e.NationalId, AcademicQualification = e.AcademicQualification,
            CurrentLocation = e.CurrentLocation, Email = e.Email, DepartmentId = e.DepartmentId, JobRoleId = e.JobRoleId,
            HireDate = e.HireDate, EmploymentType = e.EmploymentType, SocialInsurance = e.SocialInsurance,
            MedicalInsurance = e.MedicalInsurance, BasicSalary = e.BasicSalary, IncentivesCommissions = e.IncentivesCommissions,
            HasLogin = linked is not null, LinkedUserLabel = linkedLabel, CreateLogin = false
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
            if (model.CreateLogin)
            {
                var (userId, confirm) = await ResolveLoginAsync(model);
                if (confirm is not null) return Json(new { ok = false, confirm, confirmField = "ConfirmLinkUser" });
                e.UserId = userId;
            }
            _db.Employees.Add(e);
        }
        else
        {
            var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (e is null) return NotFound();
            Apply(e, model);
            e.Type = isSales ? EmployeeType.Salesperson : EmployeeType.Other;
            // Only create/link a login on edit when the employee has none yet and it was requested.
            if (e.UserId is null && model.CreateLogin)
            {
                var (userId, confirm) = await ResolveLoginAsync(model);
                if (confirm is not null) return Json(new { ok = false, confirm, confirmField = "ConfirmLinkUser" });
                e.UserId = userId;
            }
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ الموظف «{model.FullName}».";
        return Json(new { ok = true });
    }

    /// <summary>
    /// Resolves the login account for a new/updated employee. If a user with the same phone (or, failing
    /// that, the same email) already exists, returns a confirmation message so the caller can ask the
    /// operator whether to link it — unless the operator already confirmed (<see cref="EmployeeFormModel.ConfirmLinkUser"/>).
    /// Otherwise creates a new account with a dummy password and no permissions.
    /// Returns (userId, confirm): when confirm is non-null, nothing was created/linked yet.
    /// </summary>
    private async Task<(Guid? userId, string? confirm)> ResolveLoginAsync(EmployeeFormModel model)
    {
        var email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        var phone = model.Phone?.Trim();
        var tenant = _currentUser.TenantId;

        // Only ever match a user WITHIN the current tenant — never link an employee to another tenant's account.
        ApplicationUser? existing = null;
        if (!string.IsNullOrWhiteSpace(phone))
            existing = await _userManager.Users.FirstOrDefaultAsync(u => u.TenantId == tenant && u.PhoneNumber == phone);
        if (existing is null && email is not null)
        {
            var normEmail = _userManager.NormalizeEmail(email);
            existing = await _userManager.Users.FirstOrDefaultAsync(u => u.TenantId == tenant && u.NormalizedEmail == normEmail);
        }

        if (existing is not null)
        {
            if (!model.ConfirmLinkUser)
                return (null, $"يوجد مستخدم بالفعل بنفس رقم الهاتف: «{existing.FullName}» ({existing.UserName}). هل تريد ربط هذا الحساب بالموظف؟");
            return (existing.Id, null); // operator confirmed the link
        }

        var handle = email ?? phone;
        if (string.IsNullOrWhiteSpace(handle)) return (null, null);

        var user = new ApplicationUser
        {
            UserName = handle,
            Email = email,
            EmailConfirmed = email is not null,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            FullName = model.FullName,
            TenantId = _currentUser.TenantId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        var result = await _userManager.CreateAsync(user, AppConstants.DefaultEmployeePassword);
        return (result.Succeeded ? user.Id : (Guid?)null, null); // no permission claims are granted
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

        // Delete the linked login account too (deleting a user directly does NOT delete its employee).
        if (e.UserId is not null)
        {
            var user = await _userManager.FindByIdAsync(e.UserId.Value.ToString());
            if (user is not null) await _userManager.DeleteAsync(user);
        }

        TempData["StatusMessage"] = $"تم حذف الموظف «{e.FullName}»" + (e.UserId is not null ? " وحسابه المرتبط." : ".");
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
        ViewBag.LinkedUser = e.UserId is not null ? await _userManager.FindByIdAsync(e.UserId.Value.ToString()) : null;
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

    // ---------- Link / unlink an existing employee with an existing user ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.HrManage)]
    public async Task<IActionResult> LinkUserForm(Guid id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        return PartialView("_LinkUserForm", new LinkUserFormModel
        {
            EmployeeId = e.Id, EmployeeName = e.FullName, Users = await UnlinkedUserOptionsAsync(ct)
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LinkUser(LinkUserFormModel model, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == model.EmployeeId, ct);
        if (e is null) return NotFound();
        if (!ModelState.IsValid)
        {
            model.EmployeeName = e.FullName;
            model.Users = await UnlinkedUserOptionsAsync(ct);
            return PartialView("_LinkUserForm", model);
        }
        if (await _db.Employees.AnyAsync(x => x.Id != e.Id && x.UserId == model.UserId, ct))
            return Json(new { ok = false, error = "هذا المستخدم مرتبط بموظف آخر بالفعل." });

        e.UserId = model.UserId;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم ربط الموظف «{e.FullName}» بحساب مستخدم.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlink(Guid id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        e.UserId = null;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم إلغاء ربط الحساب بالموظف (لم يُحذف المستخدم).";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<List<SelectListItem>> UnlinkedUserOptionsAsync(CancellationToken ct)
    {
        var linked = (await _db.Employees.Where(x => x.UserId != null).Select(x => x.UserId).ToListAsync(ct))
            .Select(g => g!.Value).ToList();
        var tenant = _currentUser.TenantId;
        var users = await _userManager.Users
            .Where(u => u.TenantId == tenant && !linked.Contains(u.Id))
            .OrderBy(u => u.FullName).ToListAsync(ct);
        return users.Select(u => new SelectListItem { Value = u.Id.ToString(), Text = $"{u.FullName} ({u.UserName})" }).ToList();
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
