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

    // Auto-generated activities that mark a stage's start/end (their presence tracks the stage status).
    private const string StartActivity = "بدء المرحلة";
    private const string EndActivity = "إنهاء المرحلة";

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

        // Actual start/end dates are owned by the stage state action, not this form — never written here.
        if (model.Id == Guid.Empty)
            _db.ProjectStages.Add(new ProjectStage
            {
                ProjectId = model.ProjectId, Name = model.Name,
                PlannedStartDate = model.PlannedStartDate, PlannedEndDate = model.PlannedEndDate, Notes = model.Notes
            });
        else
        {
            var s = await _db.ProjectStages.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name;
            s.PlannedStartDate = model.PlannedStartDate;
            s.PlannedEndDate = model.PlannedEndDate;
            s.Notes = model.Notes;
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
            _db.StageActivities.Add(new StageActivity { StageId = stage.Id, Activity = EndActivity, Date = model.Date });
            TempData["StatusMessage"] = $"تم إنهاء المرحلة «{stage.Name}».";
        }
        else
        {
            stage.ActualStartDate = model.Date;
            stage.ActualEndDate = null; // re-opening clears the end
            _db.StageActivities.Add(new StageActivity { StageId = stage.Id, Activity = StartActivity, Date = model.Date });
            TempData["StatusMessage"] = $"تم بدء المرحلة «{stage.Name}».";
        }
        await RecalcProjectActualsAsync(stage.ProjectId, ct);
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    // ---------- Activities ----------
    // Activities can only be added after the stage is started.
    [HttpGet]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    public async Task<IActionResult> ActivityForm(Guid stageId, CancellationToken ct)
    {
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == stageId, ct);
        if (stage is null) return NotFound();
        if (stage.ActualStartDate is null)
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">لا يمكن إضافة نشاط قبل بدء المرحلة. ابدأ المرحلة أولًا.</div>", "text/html");
        return PartialView("_ActivityForm", new ActivityFormModel { StageId = stageId });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ProjectsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivityForm(ActivityFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return PartialView("_ActivityForm", model);
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == model.StageId, ct);
        if (stage is null) return NotFound();
        if (stage.ActualStartDate is null)
            return Json(new { ok = false, error = "لا يمكن إضافة نشاط قبل بدء المرحلة." });

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
        if (a is null) return RedirectToAction(nameof(Details), new { id = stageId });
        var stage = await _db.ProjectStages.FirstOrDefaultAsync(s => s.Id == stageId, ct);

        if (a.Activity == StartActivity)
        {
            // The "start" activity can only be removed when it's the last one; removing it un-starts the stage.
            var others = await _db.StageActivities.CountAsync(x => x.StageId == stageId && x.Id != id, ct);
            if (others > 0)
            {
                TempData["ErrorMessage"] = "لا يمكن حذف نشاط «بدء المرحلة» قبل حذف باقي أنشطة المرحلة.";
                return RedirectToAction(nameof(Details), new { id = stageId });
            }
            if (stage is not null) { stage.ActualStartDate = null; stage.ActualEndDate = null; } // → «لم تبدأ»
        }
        else if (a.Activity == EndActivity && stage is not null)
        {
            stage.ActualEndDate = null; // removing the end reverts the stage to «قيد التنفيذ»
        }

        _db.StageActivities.Remove(a);
        if (stage is not null) await RecalcProjectActualsAsync(stage.ProjectId, ct);
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Details), new { id = stageId });
    }

    // The project's actual start = the earliest stage actual start; actual end = the latest stage
    // actual end, but only once every stage has ended. Kept in sync as stages start/end.
    private async Task RecalcProjectActualsAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null) return;
        var stages = await _db.ProjectStages.Where(s => s.ProjectId == projectId).ToListAsync(ct);
        var starts = stages.Where(s => s.ActualStartDate.HasValue).Select(s => s.ActualStartDate!.Value).ToList();
        project.ActualStartDate = starts.Count > 0 ? starts.Min() : (DateTime?)null;
        project.ActualEndDate = stages.Count > 0 && stages.All(s => s.ActualEndDate.HasValue)
            ? stages.Max(s => s.ActualEndDate!.Value) : (DateTime?)null;
    }

    private async Task<List<SelectListItem>> DefinitionsAsync(CancellationToken ct) =>
        await _db.StageDefinitions.Where(d => d.IsActive).OrderBy(d => d.SortOrder).ThenBy(d => d.Name)
            .Select(d => new SelectListItem { Value = d.Name, Text = d.Name }).ToListAsync(ct);
}
