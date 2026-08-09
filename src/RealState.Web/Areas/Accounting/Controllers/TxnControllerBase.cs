using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

[Authorize]
public abstract class TxnControllerBase : Controller
{
    protected readonly IApplicationDbContext _db;
    protected readonly IAccountingService _accounting;
    protected readonly ICurrentUserService _currentUser;

    protected TxnControllerBase(IApplicationDbContext db, IAccountingService accounting, ICurrentUserService currentUser)
    {
        _db = db;
        _accounting = accounting;
        _currentUser = currentUser;
    }

    protected abstract TxnType TxnType { get; }

    // Per-subclass permissions (Expenses.* vs Incomes.*).
    protected abstract string ViewPerm { get; }
    protected abstract string CreatePerm { get; }
    protected abstract string EditPerm { get; }
    protected abstract string DeletePerm { get; }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        if (!Can(ViewPerm)) return Forbid();
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(CreatePerm);
        ViewData["CanEdit"] = Can(EditPerm);
        ViewData["CanDelete"] = Can(DeletePerm);
        return View("TxnList", await BuildListAsync(from, to, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? CreatePerm : EditPerm)) return Forbid();
        var model = new TxnFormModel { Safes = await SafesAsync(ct), IsExpense = TxnType == TxnType.Expense };
        if (id is not null)
        {
            var t = await _db.SafeTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || t.Type != TxnType || t.Source != TxnSource.Manual) return NotFound();
            model.Id = t.Id; model.Amount = t.Amount; model.OccurredAt = t.OccurredAt; model.Description = t.Description; model.SafeId = t.SafeId;
        }
        else
        {
            await LoadHrOptionsAsync(model, ct); // only new entries can be advances/rewards
        }
        return PartialView("_TxnForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(TxnFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? CreatePerm : EditPerm)) return Forbid();
        model.Safes = await SafesAsync(ct);
        model.IsExpense = TxnType == TxnType.Expense;

        // HR-linked new entries route through a dedicated flow (advance disbursement / reward payout / advance repayment).
        if (model.Id == Guid.Empty && model.Kind != AccountingEntryKind.General)
        {
            ModelState.Remove(nameof(model.Description));            // auto-generated
            if (TxnType == TxnType.Expense) ModelState.Remove(nameof(model.Amount)); // taken from the advance/reward
            await LoadHrOptionsAsync(model, ct);
            return await SaveHrEntryAsync(model, ct);
        }

        if (!ModelState.IsValid) { await LoadHrOptionsAsync(model, ct); return PartialView("_TxnForm", model); }

        var label = TxnType == TxnType.Expense ? "مصروف" : "إيراد";
        int serial;
        if (model.Id == Guid.Empty)
        {
            var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType, TxnSource.Manual,
                model.Amount, model.OccurredAt, model.Description, ct: ct);
            await _db.SaveChangesAsync(ct);
            serial = txn.Serial;
            TempData["StatusMessage"] = $"تسجيل {label} رقم {serial:D4}";
        }
        else
        {
            var t = await _db.SafeTransactions.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (t is null || t.Source != TxnSource.Manual || t.Type != TxnType) return NotFound();
            t.Amount = model.Amount; t.OccurredAt = model.OccurredAt; t.Description = model.Description; t.SafeId = model.SafeId!.Value;
            await _db.SaveChangesAsync(ct);
            serial = t.Serial;
            TempData["StatusMessage"] = $"تعديل {label} رقم {serial:D4}";
        }
        return Json(new { ok = true });
    }

    // Advance disbursement / reward payout (on the Expenses form) and advance repayment (on the Incomes form).
    private async Task<IActionResult> SaveHrEntryAsync(TxnFormModel model, CancellationToken ct)
    {
        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");

        if (TxnType == TxnType.Expense && model.Kind == AccountingEntryKind.Advance)
        {
            var adv = await _db.Advances.FirstOrDefaultAsync(a => a.Id == model.AdvanceId && a.Status == DisbursementStatus.NotDisbursed, ct);
            if (adv is null) ModelState.AddModelError(nameof(model.AdvanceId), "اختر سلفة غير مصروفة.");
            if (!ModelState.IsValid) return PartialView("_TxnForm", model);
            var empName = await _db.Employees.Where(e => e.Id == adv!.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "—";
            var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.AdvanceDisbursement,
                adv!.Amount, model.OccurredAt, $"صرف سلفة ADV-{adv.Number:D4} للموظف {empName}", ct: ct);
            await _db.SaveChangesAsync(ct);
            adv.Status = DisbursementStatus.Disbursed; adv.ExpenseTxnId = txn.Id;
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم صرف السلفة ADV-{adv.Number:D4} (مصروف رقم {txn.Serial:D4}).";
            return Json(new { ok = true });
        }

        if (TxnType == TxnType.Expense && model.Kind == AccountingEntryKind.Reward)
        {
            var rw = await _db.Rewards.FirstOrDefaultAsync(r => r.Id == model.RewardId && r.PayVia == RewardPayVia.Cash && r.Status == PayStatus.NotPaid, ct);
            if (rw is null) ModelState.AddModelError(nameof(model.RewardId), "اختر مكافأة نقدية غير مصروفة.");
            if (!ModelState.IsValid) return PartialView("_TxnForm", model);
            var empName = await _db.Employees.Where(e => e.Id == rw!.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "—";
            var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.RewardPayment,
                rw!.Amount, model.OccurredAt, $"صرف مكافأة RWD-{rw.Number:D4} للموظف {empName}", ct: ct);
            await _db.SaveChangesAsync(ct);
            rw.Status = PayStatus.Paid; rw.ExpenseTxnId = txn.Id;
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم صرف المكافأة RWD-{rw.Number:D4} (مصروف رقم {txn.Serial:D4}).";
            return Json(new { ok = true });
        }

        if (TxnType == TxnType.Income && model.Kind == AccountingEntryKind.Advance)
        {
            var adv = await _db.Advances.FirstOrDefaultAsync(a => a.Id == model.AdvanceId
                && a.RepaymentMethod == AdvanceRepaymentMethod.Cash && a.Status == DisbursementStatus.Disbursed, ct);
            if (adv is null) ModelState.AddModelError(nameof(model.AdvanceId), "اختر سلفة نقدية مصروفة.");
            var alreadyRepaid = adv is null ? 0 : await _db.AdvanceRepayments.Where(r => r.AdvanceId == adv.Id && r.Status == PayStatus.Paid).SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
            var remaining = adv is null ? 0 : adv.Amount - alreadyRepaid;
            if (adv is not null && (model.Amount <= 0 || model.Amount > remaining))
                ModelState.AddModelError(nameof(model.Amount), $"المبلغ يتجاوز المتبقي على السلفة ({remaining:N0}).");
            if (!ModelState.IsValid) return PartialView("_TxnForm", model);
            var empName = await _db.Employees.Where(e => e.Id == adv!.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "—";
            var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Income, TxnSource.AdvanceRepayment,
                model.Amount, model.OccurredAt, $"سداد سلفة ADV-{adv!.Number:D4} من {empName}", ct: ct);
            var nextSeq = (await _db.AdvanceRepayments.Where(r => r.AdvanceId == adv.Id).MaxAsync(r => (int?)r.SeqNo, ct) ?? 0) + 1;
            _db.AdvanceRepayments.Add(new AdvanceRepayment { AdvanceId = adv.Id, SeqNo = nextSeq, Amount = model.Amount, Status = PayStatus.Paid, PaidDate = model.OccurredAt.Date, IncomeTxnId = txn.Id });
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تحصيل سداد السلفة ADV-{adv.Number:D4} (إيراد رقم {txn.Serial:D4}).";
            return Json(new { ok = true });
        }

        ModelState.AddModelError(string.Empty, "لا يوجد بند مطابق للنوع المحدد.");
        return PartialView("_TxnForm", model);
    }

    private async Task LoadHrOptionsAsync(TxnFormModel model, CancellationToken ct)
    {
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var amounts = new Dictionary<Guid, decimal>();
        if (TxnType == TxnType.Expense)
        {
            var advs = await _db.Advances.Where(a => a.Status == DisbursementStatus.NotDisbursed).OrderBy(a => a.Number).ToListAsync(ct);
            model.AdvanceOptions = advs.Select(a => { amounts[a.Id] = a.Amount; return new SelectListItem { Value = a.Id.ToString(), Text = $"ADV-{a.Number:D4} — {empNames.GetValueOrDefault(a.EmployeeId, "—")} — {a.Amount:N0} ج.م" }; }).ToList();
            var rws = await _db.Rewards.Where(r => r.PayVia == RewardPayVia.Cash && r.Status == PayStatus.NotPaid).OrderBy(r => r.Number).ToListAsync(ct);
            model.RewardOptions = rws.Select(r => { amounts[r.Id] = r.Amount; return new SelectListItem { Value = r.Id.ToString(), Text = $"RWD-{r.Number:D4} — {empNames.GetValueOrDefault(r.EmployeeId, "—")} — {r.Amount:N0} ج.م" }; }).ToList();
        }
        else // Income: cash advances that are disbursed and not fully repaid
        {
            var advs = await _db.Advances.Where(a => a.RepaymentMethod == AdvanceRepaymentMethod.Cash && a.Status == DisbursementStatus.Disbursed).OrderBy(a => a.Number).ToListAsync(ct);
            var paid = (await _db.AdvanceRepayments.Where(r => r.Status == PayStatus.Paid).GroupBy(r => r.AdvanceId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct)).ToDictionary(x => x.Key, x => x.Sum);
            model.AdvanceOptions = advs.Where(a => a.Amount - paid.GetValueOrDefault(a.Id, 0) > 0)
                .Select(a => { var rem = a.Amount - paid.GetValueOrDefault(a.Id, 0); amounts[a.Id] = rem; return new SelectListItem { Value = a.Id.ToString(), Text = $"ADV-{a.Number:D4} — {empNames.GetValueOrDefault(a.EmployeeId, "—")} — متبقٍ {rem:N0} ج.م" }; }).ToList();
        }
        model.AmountsJson = System.Text.Json.JsonSerializer.Serialize(amounts.ToDictionary(k => k.Key.ToString(), v => v.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Can(DeletePerm)) return Forbid();
        var t = await _db.SafeTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is not null && t.Source == TxnSource.Manual) { _db.SafeTransactions.Remove(t); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        if (!Can(ViewPerm)) return Forbid();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildListAsync(from, to, q, ct));
    }

    // Printable voucher for a single income/expense transaction (opens in a new tab).
    [HttpGet]
    public async Task<IActionResult> PrintOne(Guid id, CancellationToken ct)
    {
        if (!Can(ViewPerm)) return Forbid();
        var t = await _db.SafeTransactions.FirstOrDefaultAsync(x => x.Id == id && x.Type == TxnType, ct);
        if (t is null) return NotFound();
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == t.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintOne", t);
    }

    private async Task<TxnListVm> BuildListAsync(DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var safeNames = await _db.Safes.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var txns = await _db.SafeTransactions.Where(t => t.Type == TxnType).ToListAsync(ct);

        var filtered = txns.AsEnumerable();
        if (from.HasValue) filtered = filtered.Where(t => t.OccurredAt >= from.Value);
        if (to.HasValue) filtered = filtered.Where(t => t.OccurredAt < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) filtered = filtered.Where(t => t.Description.Contains(q, StringComparison.OrdinalIgnoreCase));

        return new TxnListVm
        {
            Type = TxnType, From = from, To = to, Q = q,
            Rows = filtered.OrderByDescending(t => t.OccurredAt).Select(t => new TxnRow
            {
                Id = t.Id, Serial = t.Serial, SafeName = safeNames.GetValueOrDefault(t.SafeId, "—"),
                Type = t.Type, Source = t.Source, Amount = t.Amount, OccurredAt = t.OccurredAt, Description = t.Description
            }).ToList()
        };
    }

    private async Task<List<SelectListItem>> SafesAsync(CancellationToken ct) =>
        await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
}
