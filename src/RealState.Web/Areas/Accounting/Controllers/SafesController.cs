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

    public SafesController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var safes = await _db.Safes.OrderBy(s => s.Name).ToListAsync(ct);
        var sums = (await _db.SafeTransactions
            .GroupBy(t => new { t.SafeId, t.Type })
            .Select(g => new { g.Key.SafeId, g.Key.Type, Sum = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct));

        var rows = safes.Select(s => new SafeRow
        {
            Id = s.Id, Name = s.Name, InitialAmount = s.InitialAmount,
            Income = sums.Where(x => x.SafeId == s.Id && x.Type == TxnType.Income).Sum(x => x.Sum),
            Expense = sums.Where(x => x.SafeId == s.Id && x.Type == TxnType.Expense).Sum(x => x.Sum),
            TxnCount = sums.Where(x => x.SafeId == s.Id).Sum(x => x.Count),
        }).ToList();
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SafesCreate : PermissionNames.SafesEdit)) return Forbid();
        if (id is null) return PartialView("_SafeForm", new SafeFormModel());
        var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        return PartialView("_SafeForm", new SafeFormModel { Id = s.Id, Name = s.Name, InitialAmount = s.InitialAmount, IsActive = s.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SafeFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SafesCreate : PermissionNames.SafesEdit)) return Forbid();
        if (!ModelState.IsValid) return PartialView("_SafeForm", model);
        if (model.Id == Guid.Empty)
            _db.Safes.Add(new Safe { Name = model.Name, InitialAmount = model.InitialAmount, IsActive = model.IsActive });
        else
        {
            var s = await _db.Safes.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name; s.InitialAmount = model.InitialAmount; s.IsActive = model.IsActive;
        }
        await _db.SaveChangesAsync(ct);
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
                RunningBalance = balanceAfter.TryGetValue(t.Id, out var b) ? b : 0m
            }).ToList()
        };
    }
}
