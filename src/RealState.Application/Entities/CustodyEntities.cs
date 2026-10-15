using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// A company fixed asset (<see cref="FixedAsset"/>) handed to an employee (عهدة عينية), tracked from handover to return.
/// <see cref="ItemName"/> / <see cref="ItemType"/> / <see cref="SerialNumber"/> / <see cref="Value"/> are a snapshot of the
/// asset (refreshed when the asset is edited) so lists and prints read them directly. A register only — no journal entry.
/// Numbered MC-{year}{4-digit seq} per tenant.
/// </summary>
public class MaterialCustody : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }

    /// <summary>The asset handed over (required by the form; null only on custodies recorded before assets existed).</summary>
    public Guid? FixedAssetId { get; set; }
    public FixedAsset? FixedAsset { get; set; }

    public string ItemName { get; set; } = string.Empty;
    /// <summary>Free-text category (أجهزة، سيارات، أدوات…); the form suggests the types already used.</summary>
    public string? ItemType { get; set; }
    public string? SerialNumber { get; set; }
    public int Quantity { get; set; } = 1;
    /// <summary>Unit value (the asset's unit cost) — informational, for the employee's liability.</summary>
    public decimal? Value { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateTime HandoverDate { get; set; } = DateTime.Today;
    public DateTime? ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public MaterialCustodyStatus Status { get; set; } = MaterialCustodyStatus.HandedOver;

    public string? HandoverCondition { get; set; }
    public string? ReturnCondition { get; set; }
    /// <summary>The custody this one continues (status «حُوِّلت») when the asset was transferred between employees.</summary>
    public Guid? TransferredFromId { get; set; }
    /// <summary>Why a custody recorded by mistake was cancelled (status «أُلغيت»); the record stays in the history.</summary>
    public string? CancellationReason { get; set; }
    public string? Notes { get; set; }

    public ICollection<MaterialCustodyAttachment> Attachments { get; set; } = new List<MaterialCustodyAttachment>();
}

/// <summary>A file attached to a physical custody (handover form, photos, invoice…), stored in the database.</summary>
public class MaterialCustodyAttachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid MaterialCustodyId { get; set; }
    public MaterialCustody? Custody { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// A cash custody / imprest fund (عهدة مالية) held by an employee. Cash is disbursed to the custodian from a safe, then
/// settled with the accountant: approved expenses and cash returned to a safe bring the balance back to zero, after which
/// the custody is closed. Ledger: the custodian's subsidiary of «العُهد المالية» (1350). Numbered FC-{year}{4-digit seq}.
/// </summary>
public class FinancialCustody : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public string Purpose { get; set; } = string.Empty;
    public FinancialCustodyStatus Status { get; set; } = FinancialCustodyStatus.Open;
    public DateTime? ClosedAt { get; set; }
    public string? Notes { get; set; }

    public ICollection<FinancialCustodyMovement> Movements { get; set; } = new List<FinancialCustodyMovement>();
}

/// <summary>
/// One movement on a cash custody. Disbursements and returns move cash through a safe (<see cref="SafeTransactionId"/>,
/// whose voucher posts Dr/Cr «العُهد المالية» against the safe); expenses post Dr the expense account / Cr «العُهد المالية»
/// directly (journal source <c>CustodyExpense</c>) and may carry the receipt image.
/// </summary>
public class FinancialCustodyMovement : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FinancialCustodyId { get; set; }
    public FinancialCustody? Custody { get; set; }

    public CustodyMovementKind Kind { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;

    // Disbursement / return: the safe and its cash voucher (سند صرف / سند قبض).
    public Guid? SafeId { get; set; }
    public Guid? SafeTransactionId { get; set; }

    // Expense: its category (بند) and optional project.
    public Guid? CategoryId { get; set; }
    public Guid? ProjectId { get; set; }

    // Expense receipt (optional).
    public string? ReceiptFileName { get; set; }
    public string? ReceiptContentType { get; set; }
    public byte[]? ReceiptData { get; set; }
}
