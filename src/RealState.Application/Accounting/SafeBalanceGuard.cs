using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

/// <summary>
/// Enforces «سحب على المكشوف»: money may only leave a safe (expense, payment, transfer out, or removing an
/// income it received) while the safe still holds it — unless the safe has <see cref="Entities.Safe.AllowOverdraft"/>.
/// Callers check BEFORE adding/changing movements; balances are read from saved movements.
/// </summary>
public interface ISafeBalanceGuard
{
    /// <summary>Current balance: opening + Σ inflows − Σ outflows (every source, transfers included).</summary>
    Task<decimal> BalanceAsync(Guid safeId, CancellationToken ct = default);

    /// <summary>Null when <paramref name="amount"/> may be taken out of the safe; otherwise an Arabic reason.</summary>
    Task<string?> CheckWithdrawalAsync(Guid safeId, decimal amount, CancellationToken ct = default);

    /// <summary>
    /// Null when the net per-safe balance changes (negative = money out) leave every non-overdraft safe whose
    /// balance drops at or above zero — e.g. an edit that moves an expense to another safe. Otherwise an Arabic reason.
    /// </summary>
    Task<string?> CheckChangesAsync(IEnumerable<(Guid SafeId, decimal Change)> changes, CancellationToken ct = default);
}

public sealed class SafeBalanceGuard : ISafeBalanceGuard
{
    private readonly IApplicationDbContext _db;
    public SafeBalanceGuard(IApplicationDbContext db) => _db = db;

    public async Task<decimal> BalanceAsync(Guid safeId, CancellationToken ct = default)
    {
        var opening = await _db.Safes.Where(s => s.Id == safeId).Select(s => (decimal?)s.InitialAmount).FirstOrDefaultAsync(ct) ?? 0m;
        var sums = await _db.SafeTransactions.Where(t => t.SafeId == safeId).GroupBy(t => t.Type)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        return opening
            + sums.Where(x => x.Key == TxnType.Income).Sum(x => x.Sum)
            - sums.Where(x => x.Key == TxnType.Expense).Sum(x => x.Sum);
    }

    public Task<string?> CheckWithdrawalAsync(Guid safeId, decimal amount, CancellationToken ct = default)
        => CheckChangesAsync(new[] { (safeId, -amount) }, ct);

    public async Task<string?> CheckChangesAsync(IEnumerable<(Guid SafeId, decimal Change)> changes, CancellationToken ct = default)
    {
        var dropping = changes.GroupBy(c => c.SafeId)
            .Select(g => (SafeId: g.Key, Change: g.Sum(x => x.Change)))
            .Where(c => c.Change < 0).ToList();
        foreach (var (safeId, change) in dropping)
        {
            var safe = await _db.Safes.Where(s => s.Id == safeId).Select(s => new { s.Name, s.AllowOverdraft }).FirstOrDefaultAsync(ct);
            if (safe is null || safe.AllowOverdraft) continue;
            var balance = await BalanceAsync(safeId, ct);
            if (balance + change < 0)
                return $"رصيد خزنة «{safe.Name}» لا يكفي — الرصيد الحالي {balance:N2} ج.م والعملية تُنقصه بمقدار {-change:N2} ج.م " +
                       "(الخزنة غير مسموح لها بالسحب على المكشوف).";
        }
        return null;
    }
}
