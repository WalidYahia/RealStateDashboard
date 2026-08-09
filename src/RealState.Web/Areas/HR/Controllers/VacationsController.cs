using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Hr.Models;

namespace RealState.Web.Areas.Hr.Controllers;

[Area("Hr")]
[Authorize(Policy = PermissionNames.HrView)]
public class VacationsController : Controller
{
    private readonly IApplicationDbContext _db;
    public VacationsController(IApplicationDbContext db) => _db = db;

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var vacs = await _db.Vacations.ToListAsync(ct);
        var rows = vacs.Select(v => new VacationRow
        {
            Id = v.Id, Employee = empNames.GetValueOrDefault(v.EmployeeId, "—"), Type = v.Type,
            ApplyDate = v.ApplyDate, FromDate = v.FromDate, ToDate = v.ToDate
        }).AsEnumerable();
        if (from.HasValue) rows = rows.Where(r => r.ApplyDate >= from.Value);
        if (to.HasValue) rows = rows.Where(r => r.ApplyDate < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.Employee.Contains(q, StringComparison.OrdinalIgnoreCase));

        ViewData["CanManage"] = CanManage();
        return View(new VacationListVm { Rows = rows.OrderByDescending(r => r.ApplyDate).ToList(), From = from, To = to, Q = q });
    }

    [HttpGet]
    public async Task<IActionResult> Form(CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        return PartialView("_VacationForm", await FillAsync(new VacationFormModel(), ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(VacationFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (model.ToDate < model.FromDate) ModelState.AddModelError(nameof(model.ToDate), "تاريخ النهاية قبل البداية.");
        if (!ModelState.IsValid) return PartialView("_VacationForm", await FillAsync(model, ct));
        _db.Vacations.Add(new Vacation
        {
            ApplyDate = model.ApplyDate, EmployeeId = model.EmployeeId!.Value, Type = model.Type,
            FromDate = model.FromDate, ToDate = model.ToDate
        });
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم تسجيل الإجازة.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var v = await _db.Vacations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (v is not null) { _db.Vacations.Remove(v); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = "تم حذف الإجازة."; }
        return RedirectToAction(nameof(Index));
    }

    private async Task<VacationFormModel> FillAsync(VacationFormModel m, CancellationToken ct)
    {
        m.Employees = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToListAsync(ct);
        return m;
    }
}
