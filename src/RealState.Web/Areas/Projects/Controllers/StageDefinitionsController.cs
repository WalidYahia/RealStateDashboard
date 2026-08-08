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
public class StageDefinitionsController : Controller
{
    private readonly IApplicationDbContext _db;
    public StageDefinitionsController(IApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _db.StageDefinitions.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct);
        return View(list);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (id is null) return PartialView("_StageDefinitionForm", new StageDefinitionFormModel());
        var s = await _db.StageDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        return PartialView("_StageDefinitionForm", new StageDefinitionFormModel { Id = s.Id, Name = s.Name, SortOrder = s.SortOrder, IsActive = s.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(StageDefinitionFormModel model, CancellationToken ct)
    {
        if (await _db.StageDefinitions.AnyAsync(s => s.Id != model.Id && s.Name == model.Name, ct))
            ModelState.AddModelError(nameof(model.Name), "اسم المرحلة موجود بالفعل.");

        if (!ModelState.IsValid) return PartialView("_StageDefinitionForm", model);

        if (model.Id == Guid.Empty)
            _db.StageDefinitions.Add(new StageDefinition { Name = model.Name, SortOrder = model.SortOrder, IsActive = model.IsActive });
        else
        {
            var s = await _db.StageDefinitions.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name; s.SortOrder = model.SortOrder; s.IsActive = model.IsActive;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.StageDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is not null) { _db.StageDefinitions.Remove(s); await _db.SaveChangesAsync(ct); }
        TempData["StatusMessage"] = "تم الحذف.";
        return RedirectToAction(nameof(Index));
    }
}
