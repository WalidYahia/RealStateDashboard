using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
[Authorize(Policy = PermissionNames.SafesView)]
public class SafesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly RealState.Application.Accounting.IAccountingService _accounting;
    private readonly RealState.Application.Accounting.ISafeBalanceGuard _guard;

    public SafesController(IApplicationDbContext db, ICurrentUserService currentUser, RealState.Application.Accounting.IAccountingService accounting,
        RealState.Application.Accounting.ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(SafeType? type, CancellationToken ct)
    {
        var q = _db.Safes.AsQueryable();
        if (type.HasValue) q = q.Where(s => s.Type == type.Value);
        var safes = await q.OrderBy(s => s.Name).ToListAsync(ct);
        var sums = (await _db.SafeTransactions
            .GroupBy(t => new { t.SafeId, t.Type, IsTransfer = t.Source == TxnSource.SafeTransfer })
            .Select(g => new { g.Key.SafeId, g.Key.Type, g.Key.IsTransfer, Sum = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct));
        decimal Sum(Guid id, TxnType t, bool transfer) =>
            sums.Where(x => x.SafeId == id && x.Type == t && x.IsTransfer == transfer).Sum(x => x.Sum);

        var rows = safes.Select(s => new SafeRow
        {
            Id = s.Id, Name = s.Name, Type = s.Type, IsActive = s.IsActive, AllowOverdraft = s.AllowOverdraft, InitialAmount = s.InitialAmount,
            Income = Sum(s.Id, TxnType.Income, false),
            Expense = Sum(s.Id, TxnType.Expense, false),
            TransferIn = Sum(s.Id, TxnType.Income, true),
            TransferOut = Sum(s.Id, TxnType.Expense, true),
            TxnCount = sums.Where(x => x.SafeId == s.Id).Sum(x => x.Count),
        }).ToList();
        ViewBag.Type = type;
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SafesCreate : PermissionNames.SafesEdit)) return Forbid();
        var canOverdraft = Can(PermissionNames.SafesOverdraft);
        if (id is null) return PartialView("_SafeForm", new SafeFormModel { CanSetOverdraft = canOverdraft });
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        return PartialView("_SafeForm", new SafeFormModel
        {
            Id = s.Id, Name = s.Name, Type = s.Type, InitialAmount = s.InitialAmount, IsActive = s.IsActive,
            AllowOverdraft = s.AllowOverdraft, CanSetOverdraft = canOverdraft
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SafeFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SafesCreate : PermissionNames.SafesEdit)) return Forbid();
        // «سحب على المكشوف» is only ever changed by users holding Safes.Overdraft; for others the posted value is ignored.
        var canOverdraft = Can(PermissionNames.SafesOverdraft);
        model.CanSetOverdraft = canOverdraft;
        if (!Enum.IsDefined(model.Type)) ModelState.AddModelError(nameof(model.Type), "نوع خزنة غير صالح.");
        if (!ModelState.IsValid) return PartialView("_SafeForm", model);
        Safe safe;
        var overdraftBefore = false;
        if (model.Id == Guid.Empty)
        {
            safe = new Safe { Name = model.Name, Type = model.Type, InitialAmount = model.InitialAmount, IsActive = model.IsActive };
            _db.Safes.Add(safe);
        }
        else
        {
            safe = await _db.Safes.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (safe is null) return NotFound();
            // Lowering the opening balance takes money out of the safe — «سحب على المكشوف» applies.
            if (model.InitialAmount < safe.InitialAmount
                && await _guard.CheckChangesAsync(new[] { (safe.Id, model.InitialAmount - safe.InitialAmount) }, ct) is string overdraw)
            {
                ModelState.AddModelError(nameof(model.InitialAmount), overdraw);
                return PartialView("_SafeForm", model);
            }
            overdraftBefore = safe.AllowOverdraft;
            safe.Name = model.Name; safe.Type = model.Type; safe.InitialAmount = model.InitialAmount; safe.IsActive = model.IsActive;
        }
        if (canOverdraft) safe.AllowOverdraft = model.AllowOverdraft;
        await _accounting.PostSafeOpeningBalanceAsync(safe, ct);   // Dr النقدية / Cr رصيد افتتاحي
        await _db.SaveChangesAsync(ct);
        var overdraftPart = safe.AllowOverdraft == overdraftBefore ? ""
            : safe.AllowOverdraft ? " — تم تفعيل السحب على المكشوف" : " — تم إلغاء السحب على المكشوف";
        TempData["StatusMessage"] = model.Id == Guid.Empty
            ? $"تم إنشاء الخزنة «{safe.Name}» ({safe.Type.Ar()}){overdraftPart}."
            : $"تم تعديل الخزنة «{safe.Name}» ({safe.Type.Ar()}){overdraftPart}.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SafesDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        if (await _db.SafeTransactions.AnyAsync(t => t.SafeId == id, ct))
        {
            TempData["StatusMessage"] = "لا يمكن حذف خزنة لها حركات.";
            return RedirectToAction(nameof(Index));
        }
        await _accounting.RemoveObligationAsync("SafeOpening", s.Id, ct);   // its opening-balance entry goes with it
        _db.Safes.Remove(s);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف الخزنة «{s.Name}».";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Movements(Guid id, DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var vm = await BuildMovementsAsync(s, from, to, q, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintMovements(Guid id, DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintMovements", await BuildMovementsAsync(s, from, to, q, ct));
    }

    // Export the safe's (filtered) movements to CSV.
    [HttpGet]
    public async Task<IActionResult> CsvMovements(Guid id, DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        var vm = await BuildMovementsAsync(s, from, to, q, ct);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var headers = new[] { "#", "التاريخ/الوقت", "النوع", "المصدر", "البيان", "المبلغ", "الرصيد بعد" };
        var rows = vm.Transactions.Select(t => (IReadOnlyList<object?>)new object?[]
        {
            t.DocNo ?? t.Serial.ToString(inv),
            t.OccurredAt.ToString("yyyy-MM-dd HH:mm", inv),
            t.IsTransfer ? (t.Type == TxnType.Income ? "تحويل وارد" : "تحويل صادر") : (t.Type == TxnType.Income ? "وارد" : "منصرف"),
            t.Source.Ar(),
            t.Description,
            t.Amount,
            t.RunningBalance
        });
        var totals = new object?[] { null, null, null, null, "الرصيد الحالي", null, vm.Balance };
        return RealState.Web.Common.Xlsx.File($"حركات {vm.SafeName} {DateTime.Now:yyyy-MM-dd}.xlsx", "حركات الخزنة", headers, rows, totals);
    }

    private async Task<SafeMovementsVm> BuildMovementsAsync(Safe s, DateTime? from, DateTime? to, string? q, CancellationToken ct)
    {
        var all = await _db.SafeTransactions.Where(t => t.SafeId == s.Id).ToListAsync(ct);
        var income = all.Where(t => t.Type == TxnType.Income).Sum(t => t.Amount);
        var expense = all.Where(t => t.Type == TxnType.Expense).Sum(t => t.Amount);

        // Running balance after each transaction, computed over ALL movements in chronological order
        // (so it stays correct even when the list is filtered). The final value equals the current balance.
        var balanceAfter = new Dictionary<Guid, decimal>();
        var running = s.InitialAmount;
        foreach (var t in all.OrderBy(t => t.OccurredAt).ThenBy(t => t.CreatedAt))
        {
            running += t.Type == TxnType.Income ? t.Amount : -t.Amount;
            balanceAfter[t.Id] = running;
        }

        // Transfer legs show their transfer number (SF-…) instead of a receipt serial.
        var transferIds = all.Where(t => t.Source == TxnSource.SafeTransfer).Select(t => t.Id).ToList();
        var transferNo = new Dictionary<Guid, string>();
        if (transferIds.Count > 0)
            foreach (var tr in await _db.SafeTransfers
                         .Where(x => transferIds.Contains(x.OutTransactionId) || transferIds.Contains(x.InTransactionId))
                         .Select(x => new { x.Number, x.OutTransactionId, x.InTransactionId }).ToListAsync(ct))
            {
                transferNo[tr.OutTransactionId] = $"SF-{tr.Number}";
                transferNo[tr.InTransactionId] = $"SF-{tr.Number}";
            }

        var filtered = all.AsEnumerable();
        if (from.HasValue) filtered = filtered.Where(t => t.OccurredAt >= from.Value);
        if (to.HasValue) filtered = filtered.Where(t => t.OccurredAt < to.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(q)) filtered = filtered.Where(t => t.Description.Contains(q, StringComparison.OrdinalIgnoreCase));

        return new SafeMovementsVm
        {
            SafeId = s.Id, SafeName = s.Name, InitialAmount = s.InitialAmount, Balance = s.InitialAmount + income - expense,
            From = from, To = to, Q = q,
            Transactions = filtered.OrderBy(t => t.OccurredAt).ThenBy(t => t.CreatedAt).Select(t => new TxnRow
            {
                Id = t.Id, Serial = t.Serial, SafeName = s.Name, Type = t.Type, Source = t.Source,
                Amount = t.Amount, OccurredAt = t.OccurredAt, Description = t.Description,
                DocNo = transferNo.GetValueOrDefault(t.Id),
                RunningBalance = balanceAfter.TryGetValue(t.Id, out var b) ? b : 0m
            }).ToList()
        };
    }
}
