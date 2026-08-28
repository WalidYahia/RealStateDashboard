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
public class LeaveRequestsController : Controller
{
    private readonly IApplicationDbContext _db;
    public LeaveRequestsController(IApplicationDbContext db) => _db = db;

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanManage"] = CanManage();
        return View(await BuildVmAsync(from, to, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, string? q, CancellationToken ct)
        => View("PrintList", await BuildVmAsync(from, to, q, ct));

    [HttpGet]
    public async Task<IActionResult> PrintOne(Guid id, CancellationToken ct)
    {
        var r = await _db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        var name = await _db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        return View("PrintOne", new LeaveRequestRow { Id = r.Id, Employee = name ?? "—", Type = r.Type, Date = r.Date, Hours = r.Hours });
    }

    private async Task<LeaveRequestListVm> BuildVmAsync(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var reqs = await _db.LeaveRequests.ToListAsync(ct);
        var rows = reqs.Select(r => new LeaveRequestRow
        {
            Id = r.Id, Employee = empNames.GetValueOrDefault(r.EmployeeId, "—"), Type = r.Type,
            Date = r.Date, Hours = r.Hours
        }).AsEnumerable();
        if (from.HasValue) rows = rows.Where(r => r.Date >= from.Value);
        if (to.HasValue) rows = rows.Where(r => r.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.Employee.Contains(q, StringComparison.OrdinalIgnoreCase));
        return new LeaveRequestListVm { Rows = rows.OrderByDescending(r => r.Date).ToList(), From = from, To = to, Q = q };
    }

    [HttpGet]
    public async Task<IActionResult> Form(CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        return PartialView("_LeaveRequestForm", await FillAsync(new LeaveRequestFormModel(), ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(LeaveRequestFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_LeaveRequestForm", await FillAsync(model, ct));
        _db.LeaveRequests.Add(new LeaveRequest
        {
            EmployeeId = model.EmployeeId!.Value, Type = model.Type, Date = model.Date, Hours = model.Hours
        });
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم تسجيل طلب الإذن.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await _db.LeaveRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is not null) { _db.LeaveRequests.Remove(r); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = "تم حذف طلب الإذن."; }
        return RedirectToAction(nameof(Index));
    }

    private async Task<LeaveRequestFormModel> FillAsync(LeaveRequestFormModel m, CancellationToken ct)
    {
        m.Employees = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToListAsync(ct);
        return m;
    }
}
