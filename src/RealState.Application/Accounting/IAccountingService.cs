using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Application.Accounting;

public interface IAccountingService
{
    /// <summary>Adds a serialized safe transaction to the context (caller saves). Serial is per Type.</summary>
    Task<SafeTransaction> AddTransactionAsync(
        Guid safeId, TxnType type, TxnSource source, decimal amount, DateTime occurredAt, string description,
        Guid? installmentId = null, Guid? stageExpenseId = null, Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Removes any transactions linked to an installment (used when a collection is cancelled).</summary>
    Task RemoveByInstallmentAsync(Guid installmentId, CancellationToken ct = default);
}
