using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Application.Accounting;

public interface IAccountingService
{
    /// <summary>
    /// Records a cash movement (SafeTransaction) AND posts its balanced journal entry through the ledger
    /// engine — both added to the current unit of work so the caller's SaveChanges commits them atomically.
    /// The optional dimension ids let the entry settle the right subsidiary (customer / supplier / contractor
    /// / employee) and carry analysis dimensions.
    /// </summary>
    Task<SafeTransaction> AddTransactionAsync(
        Guid safeId, TxnType type, TxnSource source, decimal amount, DateTime occurredAt, string description,
        Guid? installmentId = null, Guid? stageExpenseId = null, Guid? projectId = null,
        Guid? customerId = null, Guid? supplierId = null, Guid? contractorId = null, Guid? employeeId = null,
        Guid? unitId = null, Guid? categoryId = null, CancellationToken ct = default);

    /// <summary>Ensures a chart-of-accounts account exists for a user-defined expense/income category (بند).</summary>
    Task EnsureCategoryAccountAsync(TxnCategory category, CancellationToken ct = default);

    Task RemoveByInstallmentAsync(Guid installmentId, CancellationToken ct = default);

    /// <summary>Removes a cash movement AND its journal entry (used when a movement is deleted).</summary>
    Task RemoveTransactionAsync(SafeTransaction txn, CancellationToken ct = default);

    /// <summary>Posts the settlement entry for an existing cash movement (used by the one-time backfill).</summary>
    Task PostCashSettlementAsync(SafeTransaction txn,
        Guid? projectId = null, Guid? customerId = null, Guid? supplierId = null, Guid? contractorId = null, Guid? employeeId = null, Guid? unitId = null,
        Guid? categoryId = null, CancellationToken ct = default);

    /// <summary>Safe opening balance → Dr النقدية / Cr رصيد افتتاحي for the safe's InitialAmount.</summary>
    Task PostSafeOpeningBalanceAsync(Safe safe, CancellationToken ct = default);

    // ---- accrual obligation postings (create the receivable/payable side) ----

    /// <summary>Sale contract → Dr العملاء (ذمم مدينة) / Cr إيرادات المبيعات (accrues revenue + receivable).</summary>
    Task PostSaleContractAsync(SaleContract contract, CancellationToken ct = default);

    /// <summary>Supplier order → Dr المشتريات / Cr الموردون. Re-posts (removes old first) so edits stay in sync.</summary>
    Task SyncSupplierOrderAsync(SupplierOrder order, decimal orderValue, CancellationToken ct = default);

    /// <summary>Work order → Dr أعمال المقاولات / Cr المقاولون at its current الإجمالي الفعلي. Re-posts on progress/edit.</summary>
    Task SyncWorkOrderAsync(WorkOrder order, CancellationToken ct = default);

    /// <summary>Unit cost → Dr مخزون العقارات / Cr رصيد افتتاحي (brings the unit onto inventory). Re-posts on cost change.</summary>
    Task SyncUnitInventoryAsync(ProjectUnit unit, CancellationToken ct = default);

    /// <summary>Removes the journal entries produced by a business record (sale/order/work order) on delete.</summary>
    Task RemoveObligationAsync(string sourceType, Guid sourceId, CancellationToken ct = default);
}
