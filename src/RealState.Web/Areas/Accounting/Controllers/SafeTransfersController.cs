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

/// <summary>
/// Money transfers between safes (تحويل بين الخزائن), numbered SF-2026000001. A transfer writes an outflow
/// movement on the source safe and an inflow on the destination (both <see cref="TxnSource.SafeTransfer"/>,
/// with no income/expense serial), and posts one direct journal entry for the transfer:
/// Dr destination safe / Cr source safe. Viewing needs Safes.View; changes need Safes.Transfer.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.SafesView)]
public class SafeTransfersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly ISafeBalanceGuard _guard;

    public SafeTransfersController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool CanTransfer() => User.HasClaim("permission", PermissionNames.SafesTransfer);
    private static string SF(int number) => "SF-" + number;

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? safeId, string? q, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanTransfer"] = CanTransfer();
        return View(await BuildListAsync(from, to, safeId, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? safeId, string? q, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildListAsync(from, to, safeId, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Excel(DateTime? from, DateTime? to, Guid? safeId, string? q, CancellationToken ct)
    {
        var vm = await BuildListAsync(from, to, safeId, q, ct);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var headers = new[] { "رقم التحويل", "التاريخ/الوقت", "من خزنة", "إلى خزنة", "المبلغ", "ملاحظات", "بواسطة" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            SF(r.Number), r.OccurredAt.ToString("yyyy-MM-dd HH:mm", inv), r.FromSafe, r.ToSafe, r.Amount, r.Notes, r.CreatedBy
        });
        var totals = new object?[] { "الإجمالي", null, null, null, vm.Total, null, null };
        return RealState.Web.Common.Xlsx.File($"التحويلات بين الخزائن {DateTime.Now:yyyy-MM-dd}.xlsx", "التحويلات", headers, rows, totals);
    }

    private async Task<SafeTransferListVm> BuildListAsync(DateTime? from, DateTime? to, Guid? safeId, string? q, CancellationToken ct)
    {
        var query = _db.SafeTransfers.AsQueryable();
        if (from.HasValue) query = query.Where(t => t.OccurredAt >= from.Value.Date);
        if (to.HasValue) query = query.Where(t => t.OccurredAt < to.Value.Date.AddDays(1));
        // One safe filter: transfers where the safe is either the source or the destination.
        if (safeId.HasValue) query = query.Where(t => t.FromSafeId == safeId.Value || t.ToSafeId == safeId.Value);
        var list = await query.OrderByDescending(t => t.Number).ToListAsync(ct);
        var names = await _db.Safes.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var rows = list.Select(t => new SafeTransferRow
        {
            Id = t.Id, Number = t.Number, OccurredAt = t.OccurredAt, Amount = t.Amount, Notes = t.Notes, CreatedBy = t.CreatedBy,
            FromSafe = names.GetValueOrDefault(t.FromSafeId, "—"), ToSafe = names.GetValueOrDefault(t.ToSafeId, "—"),
        });
        if (!string.IsNullOrWhiteSpace(q))
            rows = rows.Where(r => SF(r.Number).Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || (r.Notes ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));

        return new SafeTransferListVm
        {
            Rows = rows.ToList(), From = from, To = to, SafeId = safeId, Q = q,
            SafeOptions = await _db.Safes.OrderBy(s => s.Name)
                .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct)
        };
    }

    // ---------- Create / edit (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanTransfer()) return Forbid();
        if (id is null)
            return PartialView("_TransferForm", await FillAsync(new SafeTransferFormModel { Number = await NextNumberAsync(DateTime.Today.Year, ct) }, ct));

        var t = await _db.SafeTransfers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        return PartialView("_TransferForm", await FillAsync(new SafeTransferFormModel
        {
            Id = t.Id, Number = t.Number, FromSafeId = t.FromSafeId, ToSafeId = t.ToSafeId,
            Amount = t.Amount, OccurredAt = t.OccurredAt, Notes = t.Notes
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SafeTransferFormModel model, CancellationToken ct)
    {
        if (!CanTransfer()) return Forbid();
        var isNew = model.Id == Guid.Empty;

        SafeTransfer? old = null;
        if (!isNew)
        {
            old = await _db.SafeTransfers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (old is null) return NotFound();
        }

        if (model.FromSafeId.HasValue && model.FromSafeId == model.ToSafeId)
            ModelState.AddModelError(nameof(model.ToSafeId), "لا يمكن التحويل إلى نفس الخزنة.");
        foreach (var (field, safeId) in new[] { (nameof(model.FromSafeId), model.FromSafeId), (nameof(model.ToSafeId), model.ToSafeId) })
            if (safeId.HasValue && !await _db.Safes.AnyAsync(s => s.Id == safeId && s.IsActive, ct))
                ModelState.AddModelError(field, "اختر خزنة مفعّلة.");
        if (ModelState.IsValid)
        {
            var err = await CheckBalancesAsync(old, model.FromSafeId!.Value, model.ToSafeId!.Value, model.Amount, ct);
            if (err is not null) ModelState.AddModelError(nameof(model.Amount), err);
        }
        if (!ModelState.IsValid) return PartialView("_TransferForm", await FillAsync(model, ct));


        SafeTransfer tr;
        SafeTransaction outTxn, inTxn;
        if (old is null)
        {
            tr = new SafeTransfer { Number = await NextNumberAsync(model.OccurredAt.Year, ct) };
            outTxn = new SafeTransaction { Type = TxnType.Expense, Source = TxnSource.SafeTransfer };
            inTxn = new SafeTransaction { Type = TxnType.Income, Source = TxnSource.SafeTransfer };
            _db.SafeTransactions.Add(outTxn);
            _db.SafeTransactions.Add(inTxn);
            tr.OutTransactionId = outTxn.Id;
            tr.InTransactionId = inTxn.Id;
            _db.SafeTransfers.Add(tr);
        }
        else
        {
            tr = old;
            outTxn = await _db.SafeTransactions.FirstAsync(x => x.Id == tr.OutTransactionId, ct);
            inTxn = await _db.SafeTransactions.FirstAsync(x => x.Id == tr.InTransactionId, ct);
        }

        tr.FromSafeId = model.FromSafeId!.Value;
        tr.ToSafeId = model.ToSafeId!.Value;
        tr.Amount = model.Amount;
        tr.OccurredAt = model.OccurredAt;
        tr.Notes = model.Notes;

        var names = await _db.Safes.Where(s => s.Id == tr.FromSafeId || s.Id == tr.ToSafeId).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var desc = $"تحويل {SF(tr.Number)} من خزنة «{names[tr.FromSafeId]}» إلى خزنة «{names[tr.ToSafeId]}»"
                   + (string.IsNullOrWhiteSpace(tr.Notes) ? "" : $" — {tr.Notes}");
        // Transfer legs carry no income/expense serial (0) — they aren't receipt vouchers.
        foreach (var (txn, safeId) in new[] { (outTxn, tr.FromSafeId), (inTxn, tr.ToSafeId) })
        {
            txn.SafeId = safeId; txn.Amount = tr.Amount; txn.OccurredAt = tr.OccurredAt; txn.Description = desc; txn.Serial = 0;
        }
        await _accounting.PostSafeTransferAsync(tr, outTxn, inTxn, ct);   // one direct entry: Dr to-safe / Cr from-safe

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Json(new { ok = false, error = "تعذّر الحفظ — قد يكون رقم التحويل مستخدمًا بالفعل. أعد المحاولة." });
        }
        TempData["StatusMessage"] = isNew
            ? $"تم تسجيل التحويل {SF(tr.Number)}: {tr.Amount:N2} ج.م من «{names[tr.FromSafeId]}» إلى «{names[tr.ToSafeId]}»."
            : $"تم تعديل التحويل {SF(tr.Number)}.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanTransfer()) return Forbid();
        var tr = await _db.SafeTransfers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tr is null) return NotFound();
        // Deleting takes the money back out of the destination safe — it must still hold it.
        var err = await CheckBalancesAsync(tr, null, null, 0, ct);
        if (err is not null)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف التحويل {SF(tr.Number)}: {err}";
            return RedirectToAction(nameof(Index));
        }
        var outTxn = await _db.SafeTransactions.FirstAsync(x => x.Id == tr.OutTransactionId, ct);
        var inTxn = await _db.SafeTransactions.FirstAsync(x => x.Id == tr.InTransactionId, ct);
        // The transfer first: it points at its two movements (required FKs), so removing them before it would sever
        // the relationship and EF refuses the save.
        _db.SafeTransfers.Remove(tr);
        await _accounting.RemoveSafeTransferAsync(tr, outTxn, inTxn, ct);   // both movements + the transfer's journal entry
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف التحويل {SF(tr.Number)} ({tr.Amount:N2} ج.م).";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Printable transfer voucher (opens in a new tab) ----------
    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var t = await _db.SafeTransfers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return NotFound();
        var names = await _db.Safes.Where(s => s.Id == t.FromSafeId || s.Id == t.ToSafeId).ToDictionaryAsync(s => s.Id, s => s, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", new SafeTransferRow
        {
            Id = t.Id, Number = t.Number, OccurredAt = t.OccurredAt, Amount = t.Amount, Notes = t.Notes, CreatedBy = t.CreatedBy,
            FromSafe = names.TryGetValue(t.FromSafeId, out var f) ? $"{f.Name} ({f.Type.Ar()})" : "—",
            ToSafe = names.TryGetValue(t.ToSafeId, out var d) ? $"{d.Name} ({d.Type.Ar()})" : "—",
        });
    }

    // ---------- helpers ----------

    /// <summary>
    /// Current balance of every safe = opening + Σ inflows − Σ outflows (transfers included).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> BalancesAsync(CancellationToken ct)
    {
        var safes = await _db.Safes.Select(s => new { s.Id, s.InitialAmount }).ToListAsync(ct);
        var sums = await _db.SafeTransactions.GroupBy(t => new { t.SafeId, t.Type })
            .Select(g => new { g.Key.SafeId, g.Key.Type, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        return safes.ToDictionary(s => s.Id, s => s.InitialAmount
            + sums.Where(x => x.SafeId == s.Id && x.Type == TxnType.Income).Sum(x => x.Sum)
            - sums.Where(x => x.SafeId == s.Id && x.Type == TxnType.Expense).Sum(x => x.Sum));
    }

    /// <summary>
    /// Refuses a change that would overdraw a safe: undoing <paramref name="old"/> (edit/delete) and applying the
    /// new transfer (create/edit) must leave every safe whose balance drops at or above zero — except safes
    /// set to «سحب على المكشوف», which may go negative. Null = OK.
    /// </summary>
    private Task<string?> CheckBalancesAsync(SafeTransfer? old, Guid? fromId, Guid? toId, decimal amount, CancellationToken ct)
    {
        var changes = new List<(Guid, decimal)>();
        if (old is not null) { changes.Add((old.FromSafeId, old.Amount)); changes.Add((old.ToSafeId, -old.Amount)); }
        if (fromId.HasValue && toId.HasValue) { changes.Add((fromId.Value, -amount)); changes.Add((toId.Value, amount)); }
        return _guard.CheckChangesAsync(changes, ct);   // shared «سحب على المكشوف» rule
    }

    // Year-prefixed serial (SF-2026000001 = 2026 × 1000000 + 1), resetting each year.
    private async Task<int> NextNumberAsync(int year, CancellationToken ct)
    {
        var yearBase = year * 1_000_000;
        var max = await _db.SafeTransfers.Where(t => t.Number >= yearBase && t.Number < yearBase + 1_000_000)
            .MaxAsync(t => (int?)t.Number, ct) ?? yearBase;
        return max + 1;
    }

    private async Task<SafeTransferFormModel> FillAsync(SafeTransferFormModel model, CancellationToken ct)
    {
        var balances = await BalancesAsync(ct);
        model.Safes = (await _db.Safes.Where(s => s.IsActive || s.Id == model.FromSafeId || s.Id == model.ToSafeId)
                .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name, s.Type, s.AllowOverdraft }).ToListAsync(ct))
            .Select(s => new SafeOption(s.Id, s.Name, s.Type, balances.GetValueOrDefault(s.Id), s.AllowOverdraft)).ToList();
        if (model.Id == Guid.Empty && model.Number == 0) model.Number = await NextNumberAsync(model.OccurredAt.Year, ct);
        return model;
    }
}
