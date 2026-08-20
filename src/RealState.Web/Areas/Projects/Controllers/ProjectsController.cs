using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Projects.Models;

namespace RealState.Web.Areas.Projects.Controllers;

[Area("Projects")]
[Authorize(Policy = PermissionNames.ProjectsView)]
public class ProjectsController : Controller
{
    private bool Can(string permission) => User.HasClaim("permission", permission);

    private static readonly string[] ImageTypes = { "image/png", "image/jpeg", "image/gif", "image/webp" };
    private static readonly string[] AttachmentTypes =
    {
        "image/png", "image/jpeg", "image/gif", "image/webp",
        "application/pdf", "text/plain",
        "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };
    private const long MaxImageBytes = 3 * 1024 * 1024;   // 3 MB
    private const long MaxFileBytes = 15 * 1024 * 1024;   // 15 MB

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    public ProjectsController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
    }

    // ---------- All-projects summary print ----------
    [HttpGet]
    public async Task<IActionResult> PrintAll(CancellationToken ct)
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync(ct);
        var ids = projects.Select(p => p.Id).ToList();

        var unitStats = (await _db.ProjectUnits.Where(u => ids.Contains(u.ProjectId))
            .GroupBy(u => u.ProjectId)
            .Select(g => new { g.Key, Total = g.Count(), Sold = g.Count(x => x.Status == UnitStatus.Sold) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Total, x.Sold));
        var stageCounts = (await _db.ProjectStages.Where(s => ids.Contains(s.ProjectId))
            .GroupBy(s => s.ProjectId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);

        var expByProject = (await _db.SafeTransactions
            .Where(t => t.Type == TxnType.Expense && t.ProjectId != null && ids.Contains(t.ProjectId!.Value))
            .GroupBy(t => t.ProjectId!.Value).Select(g => new { g.Key, Total = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Total);

        var rows = projects.Select(p => new ProjectSummaryRow
        {
            Code = p.Code, Name = p.Name, Type = p.Type, Location = p.Location,
            UnitsTotal = unitStats.TryGetValue(p.Id, out var u) ? u.Total : 0,
            UnitsSold = unitStats.TryGetValue(p.Id, out var u2) ? u2.Sold : 0,
            StagesCount = stageCounts.TryGetValue(p.Id, out var sc) ? sc : 0,
            TotalExpenses = expByProject.TryGetValue(p.Id, out var ex) ? ex : 0,
            PlannedEnd = p.PlannedEndDate, ActualEnd = p.ActualEndDate
        }).ToList();

        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintAll", rows);
    }

    // ---------- Dashboard + list ----------
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync(ct);
        var ids = projects.Select(p => p.Id).ToList();

        var unitStats = await _db.ProjectUnits
            .Where(u => ids.Contains(u.ProjectId))
            .GroupBy(u => u.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Total = g.Count(),
                Sold = g.Count(x => x.Status == UnitStatus.Sold),
                Available = g.Count(x => x.Status == UnitStatus.Available),
            })
            .ToListAsync(ct);
        var us = unitStats.ToDictionary(x => x.ProjectId);

        var stageCounts = await _db.ProjectStages
            .Where(s => ids.Contains(s.ProjectId))
            .GroupBy(s => s.ProjectId).Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var sc = stageCounts.ToDictionary(x => x.Key, x => x.Count);

        var items = projects.Select(p =>
        {
            us.TryGetValue(p.Id, out var u);
            return new ProjectListItem
            {
                Id = p.Id, Code = p.Code, Name = p.Name, Type = p.Type, Location = p.Location,
                HasHero = p.HeroImageData != null, PlannedEndDate = p.PlannedEndDate, ActualEndDate = p.ActualEndDate,
                UnitsTotal = u?.Total ?? 0, UnitsSold = u?.Sold ?? 0, UnitsAvailable = u?.Available ?? 0,
                StagesCount = sc.TryGetValue(p.Id, out var n) ? n : 0
            };
        }).ToList();

        var vm = new ProjectsIndexVm
        {
            Projects = items,
            TotalProjects = items.Count,
            Buildings = items.Count(i => i.Type == ProjectType.Building),
            Malls = items.Count(i => i.Type == ProjectType.Mall),
            Lands = items.Count(i => i.Type == ProjectType.Land),
            TotalUnits = items.Sum(i => i.UnitsTotal),
            SoldUnits = items.Sum(i => i.UnitsSold),
            AvailableUnits = items.Sum(i => i.UnitsAvailable),
        };
        return View(vm);
    }

    // ---------- Details ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await BuildDetailsVmAsync(id, ct);
        if (vm is null) return NotFound();
        return View(vm);
    }

    private async Task<ProjectDetailsVm?> BuildDetailsVmAsync(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return null;

        var vm = new ProjectDetailsVm
        {
            Project = project,
            Units = await _db.ProjectUnits.Where(u => u.ProjectId == id).OrderBy(u => u.Number).ToListAsync(ct),
            Attachments = await _db.ProjectAttachments.Where(a => a.ProjectId == id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct),
            Stages = await _db.ProjectStages.Where(s => s.ProjectId == id).OrderBy(s => s.PlannedStartDate).ThenBy(s => s.CreatedAt).ToListAsync(ct),
        };

        var stageIds0 = vm.Stages.Select(s => s.Id).ToList();
        vm.StageActivityCounts = (await _db.StageActivities.Where(a => stageIds0.Contains(a.StageId))
            .GroupBy(a => a.StageId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);

        // Project expenses = expense movements charged to this project (manual project expenses +
        // supplier-order payments for orders in this project).
        var expenseTxns = await _db.SafeTransactions
            .Where(t => t.Type == TxnType.Expense && t.ProjectId == id)
            .OrderByDescending(t => t.OccurredAt).ToListAsync(ct);
        vm.TotalExpenses = expenseTxns.Sum(t => t.Amount);
        vm.LastExpenseDate = expenseTxns.Count > 0 ? expenseTxns.Max(t => t.OccurredAt) : null;
        vm.Expenses = expenseTxns.Select(t => new ProjectExpenseRow
        {
            Id = t.Id,
            Serial = t.Serial,
            Date = t.OccurredAt,
            Description = t.Description,
            Amount = t.Amount,
            Source = t.Source,
            // Only manual project expenses (no stage/installment link) can be deleted from here.
            CanDelete = t.Source == TxnSource.ProjectExpense && t.StageExpenseId == null
        }).ToList();

        // Current stage: an in-progress stage (actual start passed, not yet ended), else the last started, else the earliest planned.
        var today = DateTime.Today;
        vm.CurrentStage =
            vm.Stages.Where(s => s.ActualStartDate.HasValue && s.ActualStartDate.Value.Date <= today
                                 && (!s.ActualEndDate.HasValue || s.ActualEndDate.Value.Date >= today))
                     .OrderByDescending(s => s.ActualStartDate).FirstOrDefault()
            ?? vm.Stages.Where(s => s.ActualStartDate.HasValue).OrderByDescending(s => s.ActualStartDate).FirstOrDefault()
            ?? vm.Stages.OrderBy(s => s.PlannedStartDate).FirstOrDefault();

        // Delay notes (any stage started/ended later than planned).
        foreach (var s in vm.Stages)
        {
            if (s.PlannedStartDate.HasValue && s.ActualStartDate.HasValue && s.ActualStartDate.Value.Date > s.PlannedStartDate.Value.Date)
                vm.DelayedStageNotes.Add($"{s.Name}: تأخر البدء {(s.ActualStartDate.Value.Date - s.PlannedStartDate.Value.Date).Days} يوم");
            if (s.PlannedEndDate.HasValue && s.ActualEndDate.HasValue && s.ActualEndDate.Value.Date > s.PlannedEndDate.Value.Date)
                vm.DelayedStageNotes.Add($"{s.Name}: تأخر الانتهاء {(s.ActualEndDate.Value.Date - s.PlannedEndDate.Value.Date).Days} يوم");
        }

        return vm;
    }

    // ---------- Project expenses (المصاريف tab) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    public async Task<IActionResult> ExpenseForm(Guid projectId, CancellationToken ct)
        => PartialView("_ProjectExpenseForm", new ProjectExpenseFormModel { ProjectId = projectId, Safes = await SafesAsync(ct) });

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpenseForm(ProjectExpenseFormModel model, CancellationToken ct)
    {
        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == model.ProjectId, ct);
        if (project is null) return NotFound();
        if (!ModelState.IsValid) { model.Safes = await SafesAsync(ct); return PartialView("_ProjectExpenseForm", model); }

        var desc = string.IsNullOrWhiteSpace(model.Description)
            ? $"مصروف مشروع {project.Name}"
            : $"{model.Description} — مشروع {project.Name}";
        await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.ProjectExpense,
            model.Value, model.Date, desc, projectId: project.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تسجيل مصروف بقيمة {model.Value:N0} ج.م لمشروع {project.Name}.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExpenseDelete(Guid id, Guid projectId, CancellationToken ct)
    {
        // Only manual project expenses (no stage/installment link) may be deleted here.
        var t = await _db.SafeTransactions.FirstOrDefaultAsync(
            x => x.Id == id && x.Type == TxnType.Expense && x.Source == TxnSource.ProjectExpense && x.StageExpenseId == null, ct);
        if (t is not null) { _db.SafeTransactions.Remove(t); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = "تم حذف المصروف."; }
        return RedirectToAction(nameof(Details), new { id = projectId });
    }

    private async Task<List<SelectListItem>> SafesAsync(CancellationToken ct) =>
        await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);

    // ---------- Print (open in new tab, Save as PDF) ----------
    [HttpGet]
    public async Task<IActionResult> PrintSummary(Guid id, CancellationToken ct)
    {
        var vm = await BuildDetailsVmAsync(id, ct);
        return vm is null ? NotFound() : View("PrintSummary", vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintUnits(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();
        ViewBag.Project = project;
        var units = await _db.ProjectUnits.Where(u => u.ProjectId == id).OrderBy(u => u.Number).ToListAsync(ct);
        return View("PrintUnits", units);
    }

    [HttpGet]
    public async Task<IActionResult> PrintAttachments(Guid id, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (project is null) return NotFound();
        ViewBag.Project = project;
        var atts = await _db.ProjectAttachments.Where(a => a.ProjectId == id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        return View("PrintAttachments", atts);
    }

    // ---------- Create / edit (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        if (id is null) return PartialView("_ProjectForm", new ProjectFormModel());
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        return PartialView("_ProjectForm", new ProjectFormModel
        {
            Id = p.Id, Code = p.Code, Name = p.Name, Type = p.Type, Location = p.Location,
            PlannedStartDate = p.PlannedStartDate, ActualStartDate = p.ActualStartDate,
            PlannedEndDate = p.PlannedEndDate, ActualEndDate = p.ActualEndDate,
            Notes = p.Notes, HasHeroImage = p.HeroImageData != null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ProjectFormModel model, IFormFile? heroImage, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        var (imgData, imgType) = await ReadImageAsync(heroImage, ct);

        // Project code is a mandatory, unique (per tenant) user input.
        model.Code = model.Code?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(model.Code)
            && await _db.Projects.AnyAsync(p => p.Id != model.Id && p.Code == model.Code, ct))
            ModelState.AddModelError(nameof(model.Code), "كود المشروع مستخدم بالفعل.");

        if (!ModelState.IsValid) { model.HasHeroImage = model.HasHeroImage || imgData != null; return PartialView("_ProjectForm", model); }

        if (model.Id == Guid.Empty)
        {
            // Actual start/end are computed from the stages (first started / last ended), never from this form.
            var project = new Project
            {
                Name = model.Name, Code = model.Code, Type = model.Type, Location = model.Location,
                PlannedStartDate = model.PlannedStartDate, PlannedEndDate = model.PlannedEndDate,
                Notes = model.Notes, HeroImageData = imgData, HeroImageContentType = imgType
            };
            _db.Projects.Add(project);
        }
        else
        {
            var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (p is null) return NotFound();
            p.Name = model.Name; p.Code = model.Code; p.Type = model.Type; p.Location = model.Location;
            p.PlannedStartDate = model.PlannedStartDate;
            p.PlannedEndDate = model.PlannedEndDate;
            p.Notes = model.Notes;
            if (imgData != null) { p.HeroImageData = imgData; p.HeroImageContentType = imgType; }
            else if (model.RemoveHeroImage) { p.HeroImageData = null; p.HeroImageContentType = null; }
        }

        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> HeroImage(Guid id, CancellationToken ct)
    {
        var p = await _db.Projects.Where(x => x.Id == id).Select(x => new { x.HeroImageData, x.HeroImageContentType }).FirstOrDefaultAsync(ct);
        if (p?.HeroImageData is null) return NotFound();
        return File(p.HeroImageData, p.HeroImageContentType ?? "image/png");
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();

        // A project with any sale contract cannot be deleted (delete/handle the contracts first).
        var contractCount = await _db.SaleContracts.CountAsync(c => c.ProjectId == id, ct);
        if (contractCount > 0)
        {
            // Stay on the project page so the user sees the message in context.
            TempData["ErrorMessage"] = $"لا يمكن حذف المشروع «{p.Name}» لارتباطه بعقود بيع (عدد: {contractCount}). يجب حذف العقود المرتبطة أولًا.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var stageIds = await _db.ProjectStages.Where(s => s.ProjectId == id).Select(s => s.Id).ToListAsync(ct);
        var orderIds = await _db.SupplierOrders.Where(o => o.ProjectId == id).Select(o => o.Id).ToListAsync(ct);

        // Reverse the project-charged money (stage expenses + supplier-order payments) — safe balances
        // recompute since they're derived from these transactions.
        _db.SafeTransactions.RemoveRange(await _db.SafeTransactions.Where(t => t.ProjectId == id).ToListAsync(ct));

        // Supplier orders tied to this project (+ their items and payments).
        _db.SupplierPayments.RemoveRange(await _db.SupplierPayments.Where(x => x.SupplierOrderId != null && orderIds.Contains(x.SupplierOrderId.Value)).ToListAsync(ct));
        _db.SupplierOrderItems.RemoveRange(await _db.SupplierOrderItems.Where(x => orderIds.Contains(x.SupplierOrderId)).ToListAsync(ct));
        _db.SupplierOrders.RemoveRange(await _db.SupplierOrders.Where(o => o.ProjectId == id).ToListAsync(ct));

        // Stages + their activities/expenses, then units and attachments.
        _db.StageActivities.RemoveRange(await _db.StageActivities.Where(a => stageIds.Contains(a.StageId)).ToListAsync(ct));
        _db.StageExpenses.RemoveRange(await _db.StageExpenses.Where(e => stageIds.Contains(e.StageId)).ToListAsync(ct));
        _db.ProjectStages.RemoveRange(await _db.ProjectStages.Where(s => s.ProjectId == id).ToListAsync(ct));
        _db.ProjectUnits.RemoveRange(await _db.ProjectUnits.Where(u => u.ProjectId == id).ToListAsync(ct));
        _db.ProjectAttachments.RemoveRange(await _db.ProjectAttachments.Where(a => a.ProjectId == id).ToListAsync(ct));

        _db.Projects.Remove(p);
        await _db.SaveChangesAsync(ct);   // one transactional soft-delete of the whole graph

        TempData["StatusMessage"] = $"تم حذف المشروع «{p.Name}» وكل بياناته وعكس المصروفات المرتبطة به.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Units ----------
    [HttpGet]
    public async Task<IActionResult> UnitForm(Guid projectId, Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        if (id is null) return PartialView("_UnitForm", new UnitFormModel { ProjectId = projectId });
        var u = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        return PartialView("_UnitForm", new UnitFormModel { Id = u.Id, ProjectId = u.ProjectId, Name = u.Name, Number = u.Number, Status = u.Status, AreaSqm = u.AreaSqm, Price = u.Price, Description = u.Description, Notes = u.Notes });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnitForm(UnitFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        if (!ModelState.IsValid) return PartialView("_UnitForm", model);

        if (model.Id == Guid.Empty)
            _db.ProjectUnits.Add(new ProjectUnit { ProjectId = model.ProjectId, Name = model.Name, Number = model.Number, Status = model.Status, AreaSqm = model.AreaSqm, Price = model.Price, Description = model.Description, Notes = model.Notes });
        else
        {
            var u = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (u is null) return NotFound();
            u.Name = model.Name; u.Number = model.Number; u.Status = model.Status;
            u.AreaSqm = model.AreaSqm; u.Price = model.Price; u.Description = model.Description; u.Notes = model.Notes;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpGet]
    public async Task<IActionResult> UnitPreview(Guid id, CancellationToken ct)
    {
        var u = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        return PartialView("_UnitPreview", u);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnitDelete(Guid id, Guid projectId, CancellationToken ct)
    {
        var u = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return DetailsTab(projectId, "units");

        // A unit that's on a sale contract cannot be deleted — tell the user which contract/customer.
        var contract = await _db.SaleContracts.FirstOrDefaultAsync(c => c.UnitId == id, ct);
        if (contract is not null)
        {
            var custName = await _db.Customers.Where(c => c.Id == contract.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct) ?? "—";
            TempData["ErrorMessage"] = $"لا يمكن حذف الوحدة «{u.Name}» لأنها مرتبطة بعقد بيع رقم «{contract.Code}» للعميل «{custName}». يجب حذف العقد أولًا.";
            return DetailsTab(projectId, "units");
        }

        _db.ProjectUnits.Remove(u);
        await _db.SaveChangesAsync(ct);
        // Descriptive status → the activity-log filter records it as the delete action's description.
        TempData["StatusMessage"] = $"تم حذف الوحدة «{u.Name}»{(string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})")} — المساحة: {u.AreaSqm:0.##} م² — السعر: {u.Price:N0} ج.م — الحالة: {u.Status.Ar()}";
        return DetailsTab(projectId, "units");
    }

    // ---------- Attachments ----------
    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentUpload(Guid projectId, IFormFile? file, CancellationToken ct)
    {
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxFileBytes)
                TempData["StatusMessage"] = "حجم الملف يتجاوز 15 ميجابايت.";
            else if (!AttachmentTypes.Contains(file.ContentType))
                TempData["StatusMessage"] = "صيغة الملف غير مدعومة.";
            else
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                _db.ProjectAttachments.Add(new ProjectAttachment
                {
                    ProjectId = projectId, FileName = file.FileName, ContentType = file.ContentType,
                    Size = file.Length, Data = ms.ToArray()
                });
                await _db.SaveChangesAsync(ct);
                TempData["StatusMessage"] = "تم رفع المرفق.";
            }
        }
        return DetailsTab(projectId, "files");
    }

    [HttpGet]
    public async Task<IActionResult> AttachmentDownload(Guid id, CancellationToken ct)
    {
        var a = await _db.ProjectAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName);
    }

    // Inline preview (no download filename) — browser renders images / PDF / text.
    [HttpGet]
    public async Task<IActionResult> AttachmentPreview(Guid id, CancellationToken ct)
    {
        var a = await _db.ProjectAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return File(a.Data, a.ContentType ?? "application/octet-stream");
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentDelete(Guid id, Guid projectId, CancellationToken ct)
    {
        var a = await _db.ProjectAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is not null) { _db.ProjectAttachments.Remove(a); await _db.SaveChangesAsync(ct); }
        return DetailsTab(projectId, "files");
    }

    /// <summary>Redirect back to Details opening a specific tab (via URL hash).</summary>
    private IActionResult DetailsTab(Guid projectId, string tab)
        => Redirect(Url.Action(nameof(Details), new { id = projectId }) + "#" + tab);

    // ---------- helpers ----------
    private async Task<(byte[]? data, string? type)> ReadImageAsync(IFormFile? img, CancellationToken ct)
    {
        if (img is not { Length: > 0 }) return (null, null);
        if (img.Length > MaxImageBytes) { ModelState.AddModelError("heroImage", "حجم الصورة يجب ألا يتجاوز 3 ميجابايت."); return (null, null); }
        if (!ImageTypes.Contains(img.ContentType)) { ModelState.AddModelError("heroImage", "صيغة الصورة غير مدعومة."); return (null, null); }
        using var ms = new MemoryStream();
        await img.CopyToAsync(ms, ct);
        return (ms.ToArray(), img.ContentType);
    }
}
