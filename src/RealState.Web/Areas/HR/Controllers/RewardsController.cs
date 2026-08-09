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
public class RewardsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public RewardsController(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var rewards = await _db.Rewards.ToListAsync(ct);
        var rows = rewards.Select(r => new RewardRow
        {
            Id = r.Id, Number = r.Number, Employee = empNames.GetValueOrDefault(r.EmployeeId, "—"), Date = r.Date,
            Amount = r.Amount, PayVia = r.PayVia, Status = r.Status
        }).AsEnumerable();
        if (from.HasValue) rows = rows.Where(r => r.Date >= from.Value);
        if (to.HasValue) rows = rows.Where(r => r.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.Employee.Contains(q, StringComparison.OrdinalIgnoreCase));

        ViewData["CanManage"] = CanManage();
        return View(new RewardListVm { Rows = rows.OrderByDescending(r => r.Number).ToList(), From = from, To = to, Q = q });
    }

    [HttpGet]
    public async Task<IActionResult> Form(CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        return PartialView("_RewardForm", await FillAsync(new RewardFormModel(), ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(RewardFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_RewardForm", await FillAsync(model, ct));
        var number = (await _db.Rewards.MaxAsync(r => (int?)r.Number, ct) ?? 0) + 1;
        _db.Rewards.Add(new Reward
        {
            Number = number, Date = model.Date, EmployeeId = model.EmployeeId!.Value, Amount = model.Amount,
            PayVia = model.PayVia, Notes = model.Notes, Status = PayStatus.NotPaid
        });
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.PayVia == RewardPayVia.Cash
            ? $"تم تسجيل المكافأة RWD-{number:D4} (بانتظار الصرف من صفحة المصروفات)."
            : $"تم تسجيل المكافأة RWD-{number:D4} (تُصرف مع الراتب).";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var r = await _db.Rewards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        if (r.Status == PayStatus.Paid) { TempData["ErrorMessage"] = "لا يمكن حذف مكافأة تم صرفها."; return RedirectToAction(nameof(Index)); }
        _db.Rewards.Remove(r);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المكافأة RWD-{r.Number:D4}.";
        return RedirectToAction(nameof(Index));
    }

    // Mark a salary reward as paid/unpaid (no cash movement — cash rewards are paid from Expenses).
    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePaid(Guid id, CancellationToken ct)
    {
        var r = await _db.Rewards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is not null && r.PayVia == RewardPayVia.Salary)
        {
            r.Status = r.Status == PayStatus.Paid ? PayStatus.NotPaid : PayStatus.Paid;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var r = await _db.Rewards.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return NotFound();
        r.Employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", r);
    }

    private async Task<RewardFormModel> FillAsync(RewardFormModel m, CancellationToken ct)
    {
        m.Employees = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToListAsync(ct);
        return m;
    }
}
