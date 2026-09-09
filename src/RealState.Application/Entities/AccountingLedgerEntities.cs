using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// A node in the Chart of Accounts (دليل الحسابات). Supports parent/child hierarchy; only postable
/// (leaf) accounts may appear on journal lines. System-managed subsidiary accounts (one per
/// safe / customer / supplier / contractor / employee) carry <see cref="SubKind"/> + <see cref="SubRefId"/>.
/// </summary>
public class Account : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Human account code, e.g. "1101". Unique per tenant.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }

    public Guid? ParentId { get; set; }
    public Account? Parent { get; set; }

    /// <summary>True for leaf accounts that may be used on journal lines.</summary>
    public bool IsPostable { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Ordering among siblings (set by the drag/drop chart-of-accounts editor).</summary>
    public int SortOrder { get; set; }

    /// <summary>System-managed subsidiary account marker: "Safe","Customer","Supplier","Contractor","Employee" — else null.</summary>
    public string? SubKind { get; set; }
    /// <summary>The linked business entity id for a subsidiary account.</summary>
    public Guid? SubRefId { get; set; }

    /// <summary>Debit for Asset/Expense, Credit for Liability/Equity/Revenue.</summary>
    public bool IsDebitNormal => Type is AccountType.Asset or AccountType.Expense;
}

/// <summary>
/// A balanced double-entry journal entry (قيد يومية). Total debit == total credit across its lines.
/// Links back to the business transaction that produced it via <see cref="SourceType"/>/<see cref="SourceId"/>.
/// </summary>
public class JournalEntry : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Sequential, year-prefixed display number (e.g. 202600001), unique per tenant.</summary>
    public int Number { get; set; }

    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public JournalEntryStatus Status { get; set; } = JournalEntryStatus.Posted;

    /// <summary>Originating business record kind, e.g. "SaleContract","Installment","SupplierPayment",...</summary>
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }

    /// <summary>When this entry reverses another, the reversed entry's id.</summary>
    public Guid? ReversalOfId { get; set; }

    public ICollection<JournalLine> Lines { get; set; } = new List<JournalLine>();
}

/// <summary>One debit-or-credit line of a journal entry, carrying optional analysis dimensions.</summary>
public class JournalLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid JournalEntryId { get; set; }
    public JournalEntry? Entry { get; set; }

    public Guid AccountId { get; set; }
    public Account? Account { get; set; }

    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }

    // Analysis dimensions (use instead of proliferating accounts).
    public Guid? ProjectId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? ContractorId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? UnitId { get; set; }
}
