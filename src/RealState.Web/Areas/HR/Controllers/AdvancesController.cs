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

    // Create (id null) or edit (id set — only while the advance is still "لم يُصرف").
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_AdvanceForm", await FillAsync(new AdvanceFormModel(), ct));

        var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (a.Status != DisbursementStatus.NotDisbursed)
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">لا يمكن تعديل سلفة تم صرفها.</div>", "text/html");
        return PartialView("_AdvanceForm", await FillAsync(new AdvanceFormModel
        {
            Id = a.Id, Date = a.Date, EmployeeId = a.EmployeeId, Amount = a.Amount,
            RepaymentMethod = a.RepaymentMethod, InstallmentsCount = a.InstallmentsCount == 0 ? 1 : a.InstallmentsCount,
            MonthlyStartDate = a.MonthlyStartDate ?? DateTime.Today, Notes = a.Notes
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(AdvanceFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_AdvanceForm", await FillAsync(model, ct));

        if (model.Id != Guid.Empty)
        {
            var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (a is null) return NotFound();
            if (a.Status != DisbursementStatus.NotDisbursed)
                return Json(new { ok = false, error = "لا يمكن تعديل سلفة تم صرفها." });

            a.Date = model.Date; a.EmployeeId = model.EmployeeId!.Value; a.Amount = model.Amount;
            a.RepaymentMethod = model.RepaymentMethod; a.Notes = model.Notes;
            a.InstallmentsCount = model.RepaymentMethod == AdvanceRepaymentMethod.FromSalary ? Math.Max(1, model.InstallmentsCount) : 0;
            a.MonthlyStartDate = model.RepaymentMethod == AdvanceRepaymentMethod.FromSalary ? model.MonthlyStartDate : null;
            // Regenerate the repayment schedule (nothing is paid while «لم يُصرف»).
            foreach (var r in await _db.AdvanceRepayments.Where(r => r.AdvanceId == a.Id).ToListAsync(ct)) _db.AdvanceRepayments.Remove(r);
            GenerateRepayments(a, model);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تحديث السلفة ADV-{a.Number:D4}.";
            return Json(new { ok = true });
        }

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
        GenerateRepayments(advance, model);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تسجيل السلفة ADV-{number:D4} (بانتظار الصرف من صفحة المصروفات).";
        return Json(new { ok = true });
    }

    // From-salary advances get a repayment schedule (marked paid manually later — no payroll engine).
    private void GenerateRepayments(Advance advance, AdvanceFormModel model)
    {
        if (advance.InstallmentsCount <= 0) return;
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

    [HttpPost]
    [Authorize(Policy = PermissionNames.HrManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var a = await _db.Advances.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();

        // Allowed while nothing has been repaid (whether "لم يُصرف" or "تم الصرف" with المسدَّد = 0); blocked once any repayment is collected.
        var repaid = await _db.AdvanceRepayments.Where(r => r.AdvanceId == id && r.Status == PayStatus.Paid)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        if (repaid > 0)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف السلفة ADV-{a.Number:D4} لوجود مبالغ مسدَّدة.";
            return RedirectToAction(nameof(Index));
        }

        // If it was disbursed, reverse the disbursement expense so the safe balance is restored.
        if (a.ExpenseTxnId is Guid txnId)
        {
            var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == txnId, ct);
            if (txn is not null) _db.SafeTransactions.Remove(txn);
        }

        var reps = await _db.AdvanceRepayments.Where(r => r.AdvanceId == id).ToListAsync(ct);
        foreach (var r in reps) _db.AdvanceRepayments.Remove(r);
        _db.Advances.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = a.Status == DisbursementStatus.Disbursed
            ? $"تم إلغاء السلفة ADV-{a.Number:D4} وعكس مصروف صرفها."
            : $"تم حذف السلفة ADV-{a.Number:D4}.";
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
