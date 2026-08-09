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
public class AdvancesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public AdvancesController(IApplicationDbContext db, ICurrentUserService currentUser) { _db = db; _currentUser = currentUser; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.HrManage);

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var advances = await _db.Advances.ToListAsync(ct);
        var repaid = (await _db.AdvanceRepayments.Where(r => r.Status == PayStatus.Paid)
            .GroupBy(r => r.AdvanceId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

        var rows = advances.Select(a => new AdvanceRow
        {
            Id = a.Id, Number = a.Number, Employee = empNames.GetValueOrDefault(a.EmployeeId, "—"), Date = a.Date,
            Amount = a.Amount, RepaymentMethod = a.RepaymentMethod, Status = a.Status, Repaid = repaid.GetValueOrDefault(a.Id, 0)
        }).AsEnumerable();
        if (from.HasValue) rows = rows.Where(r => r.Date >= from.Value);
        if (to.HasValue) rows = rows.Where(r => r.Date < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) rows = rows.Where(r => r.Employee.Contains(q, StringComparison.OrdinalIgnoreCase));

        ViewData["CanManage"] = CanManage();
        return View(new AdvanceListVm { Rows = rows.OrderByDescending(r => r.Number).ToList(), From = from, To = to, Q = q });
    }

    [HttpGet]
    public async Task<IActionResult> Form(CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        return PartialView("_AdvanceForm", await FillAsync(new AdvanceFormModel(), ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(AdvanceFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_AdvanceForm", await FillAsync(model, ct));

        var number = (await _db.Advances.MaxAsync(a => (int?)a.Number, ct) ?? 0) + 1;
        var advance = new Advance
        {
            Number = number, Date = model.Date, EmployeeId = model.EmployeeId!.Value, Amount = model.Amount,
            RepaymentMethod = model.RepaymentMethod, Notes = model.Notes,
            InstallmentsCount = model.RepaymentMethod == AdvanceRepaymentMethod.FromSalary ? Math.Max(1, model.InstallmentsCount) : 0,
            MonthlyStartDate = model.RepaymentMethod == AdvanceRepaymentMethod.FromSalary ? model.MonthlyStartDate : null,
            Status = DisbursementStatus.NotDisbursed
        };
        _db.Advances.Add(advance);

        // From-salary: generate the repayment schedule (marked paid manually later — no payroll engine).
        if (advance.InstallmentsCount > 0)
        {
            var per = Math.Round(model.Amount / advance.InstallmentsCount, 2);
            var start = model.MonthlyStartDate ?? model.Date;
            for (var i = 1; i <= advance.InstallmentsCount; i++)
            {
                var amount = i == advance.InstallmentsCount ? model.Amount - per * (advance.InstallmentsCount - 1) : per;
                _db.AdvanceRepayments.Add(new AdvanceRepayment
                {
                    AdvanceId = advance.Id, SeqNo = i, Amount = amount, DueDate = start.AddMonths(i - 1), Status = PayStatus.NotPaid
                });
            }
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تسجيل السلفة ADV-{number:D4} (بانتظار الصرف من صفحة المصروفات).";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (a.Status == DisbursementStatus.Disbursed || await _db.AdvanceRepayments.AnyAsync(r => r.AdvanceId == id && r.Status == PayStatus.Paid, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف سلفة تم صرفها أو بدأ سدادها.";
            return RedirectToAction(nameof(Index));
        }
        var reps = await _db.AdvanceRepayments.Where(r => r.AdvanceId == id).ToListAsync(ct);
        foreach (var r in reps) _db.AdvanceRepayments.Remove(r);
        _db.Advances.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف السلفة ADV-{a.Number:D4}.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        a.Employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == a.EmployeeId, ct);
        a.Repayments = await _db.AdvanceRepayments.Where(r => r.AdvanceId == id).OrderBy(r => r.SeqNo).ToListAsync(ct);
        ViewData["CanManage"] = CanManage();
        return View(a);
    }

    // Mark a from-salary repayment installment as paid/unpaid (no cash movement).
    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRepayment(Guid id, Guid advanceId, CancellationToken ct)
    {
        var r = await _db.AdvanceRepayments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is not null && r.IncomeTxnId is null) // cash repayments (with income link) are managed from Incomes
        {
            r.Status = r.Status == PayStatus.Paid ? PayStatus.NotPaid : PayStatus.Paid;
            r.PaidDate = r.Status == PayStatus.Paid ? DateTime.Today : null;
            await _db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Details), new { id = advanceId });
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        a.Employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == a.EmployeeId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", a);
    }

    private async Task<AdvanceFormModel> FillAsync(AdvanceFormModel m, CancellationToken ct)
    {
        m.Employees = await _db.Employees.Where(e => e.IsActive).OrderBy(e => e.FullName)
            .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToListAsync(ct);
        return m;
    }
}
