using RealState.Application.Common;

namespace RealState.Application.Entities;

/// <summary>A supplier or contractor the company buys goods/services from (الموردون والمقاولون).</summary>
public class Supplier : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A purchase/supply order placed with a supplier, made up of line items (البنود).</summary>
public class SupplierOrder : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Sequential order number per tenant, shown as PO-0001.</summary>
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Optional project this order is charged to.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string? Notes { get; set; }

    public ICollection<SupplierOrderItem> Items { get; set; } = new List<SupplierOrderItem>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();

    /// <summary>Total cost = sum of the line items (computed when items are loaded).</summary>
    public decimal TotalCost => Items?.Sum(i => i.Cost) ?? 0m;
}

/// <summary>A single line item (بند) on a supplier order: a description and its cost.</summary>
public class SupplierOrderItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierOrderId { get; set; }
    public SupplierOrder? Order { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; }
}

/// <summary>A payment made to a supplier (against their overall balance) — a safe Expense movement.</summary>
public class SupplierPayment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>The order this payment settles (payments are made per order). Null for legacy supplier-level payments.</summary>
    public Guid? SupplierOrderId { get; set; }
    public SupplierOrder? Order { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }

    /// <summary>The safe the money was paid from.</summary>
    public Guid SafeId { get; set; }

    /// <summary>Pay-receipt number (إيصال الدفع) — equal to the linked Expense transaction's serial.</summary>
    public int ReceiptNo { get; set; }

    public string? Description { get; set; }
}
