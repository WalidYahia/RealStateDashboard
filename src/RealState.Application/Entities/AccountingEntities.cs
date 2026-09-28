using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A treasury / cash box. Balance = InitialAmount + Σ income − Σ expense of its transactions.</summary>
public class Safe : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Kind of safe: عادية (default) / بنك / محفظة إلكترونية / إنستاباي.</summary>
    public SafeType Type { get; set; } = SafeType.Normal;
    public decimal InitialAmount { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>سحب على المكشوف — when set, money may be moved out of the safe beyond its balance (it may go
    /// negative). Only users with Safes.Overdraft can change it.</summary>
    public bool AllowOverdraft { get; set; }
}

/// <summary>
/// A money transfer between two safes (تحويل بين الخزائن), numbered SF-2026000001 (year*1000000 + seq).
/// It writes two safe movements — an outflow on the source safe and an inflow on the destination, both
/// <see cref="TxnSource.SafeTransfer"/> — so every safe balance stays derived from the movements — and one
/// direct journal entry for the transfer: Dr to-safe / Cr from-safe.
/// Transfers are not income or expense and are excluded from those lists.
/// </summary>
public class SafeTransfer : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed number per tenant (e.g. 2026000001), shown as SF-2026000001.</summary>
    public int Number { get; set; }
    public DateTime OccurredAt { get; set; }

    public Guid FromSafeId { get; set; }
    public Safe? FromSafe { get; set; }
    public Guid ToSafeId { get; set; }
    public Safe? ToSafe { get; set; }

    public decimal Amount { get; set; }
    public string? Notes { get; set; }

    /// <summary>The outflow movement on the source safe.</summary>
    public Guid OutTransactionId { get; set; }
    /// <summary>The inflow movement on the destination safe.</summary>
    public Guid InTransactionId { get; set; }
}

/// <summary>
/// A single money movement on a safe — a manual income/expense, an installment collection, or a
/// project-stage expense. Incomes and Expenses pages are views over this ledger filtered by Type.
/// </summary>
public class SafeTransaction : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid SafeId { get; set; }
    public Safe? Safe { get; set; }

    public TxnType Type { get; set; }
    public TxnSource Source { get; set; }

    /// <summary>Voucher number within its Type (سند قبض / سند صرف): year × 10,000,000 + sequence, reset each year
    /// (e.g. 20260000001). 64-bit — 11 digits exceed int. Transfer legs carry 0 (not vouchers).</summary>
    public long Serial { get; set; }

    public decimal Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Description { get; set; } = string.Empty;

    // Optional links back to the originating record (for auto-created rows).
    public Guid? InstallmentId { get; set; }
    public Guid? StageExpenseId { get; set; }

    /// <summary>Optional project this expense/income is charged to (project expenses + supplier-order payments).</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Optional predefined category (بند) chosen when recording a manual expense/income.</summary>
    public Guid? CategoryId { get; set; }
}
