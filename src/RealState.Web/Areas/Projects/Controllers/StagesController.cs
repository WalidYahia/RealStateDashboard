using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Projects.Models;

namespace RealState.Web.Areas.Projects.Controllers;

[Area("Projects")]
[Authorize(Policy = PermissionNames.ProjectsView)]
public class StagesController : Controller
{
    private readonly IApplicationDbContext _db;
    public StagesController(IApplicationDbContext db) => _db = db;

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // ---------- Stages list for a project ----------
    public async Task<IActionResult> Index(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return NotFound();

        ViewBag.Project = project;
        var stages = await _db.ProjectStages
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.PlannedStartDate).ThenBy(s => s.CreatedAt)
            .ToListAsync(ct);

        var ids = stages.Select(s => s.Id).ToList();
        ViewBag.ActivityCounts = (await _db.StageActivities.Where(a => ids.Contains(a.StageId))
            .GroupBy(a => a.StageId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);

        return View(stages);
    }

    // ---------- Add / edit stage (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid projectId, Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        var model = new StageFormModel { ProjectId = projectId, Definitions = await DefinitionsAsync(ct) };
        if (id is not null)
        {
            var s = await _db.ProjectStages.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return NotFound();
            model.Id = s.Id; model.ProjectId = s.ProjectId; model.Name = s.Name;
            model.PlannedStartDate = s.PlannedStartDate; model.ActualStartDate = s.ActualStartDate;
            model.PlannedEndDate = s.PlannedEndDate; model.ActualEndDate = s.ActualEndDate; model.Notes = s.Notes;
        }
        return PartialView("_StageForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(StageFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.ProjectsCreate : PermissionNames.ProjectsEdit)) return Forbid();
        if (!ModelState.IsValid) { model.Definitions = await DefinitionsAsync(ct); return PartialView("_StageForm", model); }

        if (!await _db.Projects.AnyAsync(p => p.Id == model.ProjectId, ct)) return NotFound();

        if (model.Id == Guid.Empty)
            _db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = model.ProjectId, Name = model.Name,
                PlannedStartDate = model.PlannedStartDate, ActualStartDate = model.ActualStartDate,
                PlannedEndDate = model.PlannedEndDate, ActualEndDate = model.ActualEndDate, Notes = model.Notes
            });
        else
        {
            var s = await _db.ProjectStages.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name;
            s.PlannedStartDate = model.PlannedStartDate; s.ActualStartDate = model.ActualStartDate;
            s.PlannedEndDate = model.PlannedEndDate; s.ActualEndDate = model.ActualEndDate; s.Notes = model.Notes;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid projectId, CancellationToken ct)
    {
        var s = await _db.ProjectStages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is not null) { _db.ProjectStages.Remove(s); await _db.SaveChangesAsync(ct); }
        // Stages are managed inline on the project page — return there, on the المراحل tab.
        return Redirect(Url.Action("Details", "Projects", new { area = "Projects", id = projectId }) + "#stages");
    }

    // ---------- Stage details: activities ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stage is null) return NotFound();
        ViewBag.Project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == stage.ProjectId, ct);
        ViewBag.Activities = await _db.StageActivities.Where(a => a.StageId == id).OrderByDescending(a => a.Date).ToListAsync(ct);
        return View(stage);
    }

    [HttpGet]
    public async Task<IActionResult> PrintStage(Guid id, CancellationToken ct)
    {
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (stage is null) return NotFound();
        ViewBag.Project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == stage.ProjectId, ct);
        ViewBag.Activities = await _db.StageActivities.Where(a => a.StageId == id).OrderByDescending(a => a.Date).ToListAsync(ct);
        return View("PrintStage", stage);
    }

    // ---------- Start / end a stage ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    public IActionResult StateForm(Guid stageId, bool end)
        => PartialView("_StageStateForm", new StageStateModel { StageId = stageId, IsEnd = end, Date = DateTime.Today });

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StateForm(StageStateModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return PartialView("_StageStateForm", model);
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == model.StageId, ct);
        if (stage is null) return NotFound();

        if (model.IsEnd)
        {
            if (stage.ActualStartDate is null) stage.ActualStartDate = model.Date; // ending implies started
            stage.ActualEndDate = model.Date;
            _db.StageActivities.Add(new StageActivity { StageId = stage.Id, Activity = "إنهاء المرحلة", Date = model.Date });
            TempData["StatusMessage"] = $"تم إنهاء المرحلة «{stage.Name}».";
        }
        else
        {
            stage.ActualStartDate = model.Date;
            stage.ActualEndDate = null; // re-opening clears the end
            _db.StageActivities.Add(new StageActivity { StageId = stage.Id, Activity = "بدء المرحلة", Date = model.Date });
            TempData["StatusMessage"] = $"تم بدء المرحلة «{stage.Name}».";
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    // ---------- Activities ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    public IActionResult ActivityForm(Guid stageId) => PartialView("_ActivityForm", new ActivityFormModel { StageId = stageId });

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivityForm(ActivityFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return PartialView("_ActivityForm", model);
        _db.StageActivities.Add(new StageActivity { StageId = model.StageId, Activity = model.Activity, Date = model.Date });
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivityDelete(Guid id, Guid stageId, CancellationToken ct)
    {
        var a = await _db.StageActivities.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is not null) { _db.StageActivities.Remove(a); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = stageId });
    }

    private async Task<List<SelectListItem>> DefinitionsAsync(CancellationToken ct) =>
        await _db.StageDefinitions.Where(d => d.IsActive).OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.Name, Text = d.Name }).ToListAsync(ct);
}
