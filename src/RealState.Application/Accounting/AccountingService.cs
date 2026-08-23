using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

public class AccountingService : IAccountingService
{
    private readonly IApplicationDbContext _db;
    public AccountingService(IApplicationDbContext db) => _db = db;

    public async Task<SafeTransaction> AddTransactionAsync(
        Guid safeId, TxnType type, TxnSource source, decimal amount, DateTime occurredAt, string description,
        Guid? installmentId = null, Guid? stageExpenseId = null, Guid? projectId = null, CancellationToken ct = default)
    {
        // Serial is year-prefixed and resets each year, per transaction type (e.g. 2026 + 00001 = 202600001).
        var yearBase = occurredAt.Year * 100000;
        var maxThisYear = await _db.SafeTransactions
            .Where(t => t.Type == type && t.Serial >= yearBase && t.Serial < yearBase + 100000)
            .MaxAsync(t => (int?)t.Serial, ct) ?? yearBase;
        var serial = maxThisYear + 1;
        var txn = new SafeTransaction
        {
            SafeId = safeId,
            Type = type,
            Source = source,
            Serial = serial,
            Amount = amount,
            OccurredAt = occurredAt,
            Description = description,
            InstallmentId = installmentId,
            StageExpenseId = stageExpenseId,
            ProjectId = projectId
        };
        _db.SafeTransactions.Add(txn);
        return txn;
    }

    public async Task RemoveByInstallmentAsync(Guid installmentId, CancellationToken ct = default)
    {
        var linked = await _db.SafeTransactions.Where(t => t.InstallmentId == installmentId).ToListAsync(ct);
        foreach (var t in linked) _db.SafeTransactions.Remove(t);
    }
}
