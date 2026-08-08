using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
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
        var model = new TxnFormModel { Safes = await SafesAsync(ct) };
        if (id is not null)
        {
            var t = await _db.SafeTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || t.Type != TxnType || t.Source != TxnSource.Manual) return NotFound();
            model.Id = t.Id; model.Amount = t.Amount; model.OccurredAt = t.OccurredAt; model.Description = t.Description; model.SafeId = t.SafeId;
        }
        return PartialView("_TxnForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(TxnFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? CreatePerm : EditPerm)) return Forbid();
        model.Safes = await SafesAsync(ct);
        if (!ModelState.IsValid) return PartialView("_TxnForm", model);

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
