using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A treasury / cash box. Balance = InitialAmount + Σ income − Σ expense of its transactions.</summary>
public class Safe : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal InitialAmount { get; set; }
    public bool IsActive { get; set; } = true;
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

    /// <summary>Sequential number within its Type (income serial / expense serial).</summary>
    public int Serial { get; set; }

    public decimal Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Description { get; set; } = string.Empty;

    // Optional links back to the originating record (for auto-created rows).
    public Guid? InstallmentId { get; set; }
    public Guid? StageExpenseId { get; set; }

    /// <summary>Optional project this expense/income is charged to (project expenses + supplier-order payments).</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
}
