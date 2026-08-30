using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Projects.Models;

namespace RealState.Web.Areas.Projects.Controllers;

[Area("Projects")]
[Authorize(Policy = PermissionNames.SettingsManage)]
public class ProjectTypesController : Controller
{
    private readonly IApplicationDbContext _db;
    public ProjectTypesController(IApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await ProjectTypeDefaults.EnsureAsync(_db, ct);   // covers tenants created after the seed migration
        var list = await _db.ProjectTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (id is null) return PartialView("_ProjectTypeForm", new ProjectTypeFormModel());
        var t = await _db.ProjectTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        return PartialView("_ProjectTypeForm", new ProjectTypeFormModel { Id = t.Id, Name = t.Name, BaseType = t.BaseType, SortOrder = t.SortOrder, IsActive = t.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ProjectTypeFormModel model, CancellationToken ct)
    {
        if (await _db.ProjectTypes.AnyAsync(t => t.Id != model.Id && t.Name == model.Name, ct))
            ModelState.AddModelError(nameof(model.Name), "اسم النوع موجود بالفعل.");

        if (!ModelState.IsValid) return PartialView("_ProjectTypeForm", model);

        if (model.Id == Guid.Empty)
            _db.ProjectTypes.Add(new ProjectTypeDefinition { Name = model.Name, BaseType = model.BaseType, SortOrder = model.SortOrder, IsActive = model.IsActive });
        else
        {
            var t = await _db.ProjectTypes.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (t is null) return NotFound();
            t.Name = model.Name; t.BaseType = model.BaseType; t.SortOrder = model.SortOrder; t.IsActive = model.IsActive;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ نوع المشروع «{model.Name}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await _db.ProjectTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return RedirectToAction(nameof(Index));

        // A type used by any project cannot be deleted.
        var usedCount = await _db.Projects.CountAsync(p => p.ProjectTypeId == id, ct);
        if (usedCount > 0)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف النوع «{t.Name}» لأنه مستخدم في {usedCount} مشروع.";
            return RedirectToAction(nameof(Index));
        }

        _db.ProjectTypes.Remove(t);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف نوع المشروع «{t.Name}».";
        return RedirectToAction(nameof(Index));
    }
}
