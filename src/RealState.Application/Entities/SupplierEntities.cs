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

/// <summary>
/// A purchase order (أمر توريد) — the first purchasing stage, made up of product lines (الأصناف). It is a
/// plain request document: saving it touches no ledger, stock or safe. The supplier obligation is raised
/// later by one or more <see cref="PurchaseInvoice"/>s that reference it.
/// </summary>
public class SupplierOrder : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed order number per tenant (e.g. 20260001), shown as PO-20260001.</summary>
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }

    /// <summary>Legacy only: orders created before the invoice stage carried a supplier and posted the
    /// payable themselves (Dr المشتريات / Cr الموردون). New orders have no supplier.</summary>
    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Optional project this order is charged to.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public string? Notes { get; set; }

    public ICollection<SupplierOrderItem> Items { get; set; } = new List<SupplierOrderItem>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();

    /// <summary>Total cost = sum of the line items (computed when items are loaded).</summary>
    public decimal TotalCost => Items?.Sum(i => i.Cost * i.Quantity) ?? 0m;

    /// <summary>True for a pre-invoice-stage order that still owns a supplier obligation.</summary>
    public bool IsLegacy => SupplierId.HasValue;
}

/// <summary>A single product line (صنف) on a purchase order: product, unit cost and quantity.</summary>
public class SupplierOrderItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierOrderId { get; set; }
    public SupplierOrder? Order { get; set; }

    /// <summary>The ordered product. Null only on legacy free-text lines.</summary>
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Line label — the product's "code — name" at save time (or the legacy free text).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit cost (تكلفة الوحدة).</summary>
    public decimal Cost { get; set; }

    /// <summary>Quantity ordered (الكمية). Defaults to 1 (legacy rows had no quantity).</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>Line total = unit cost × quantity (الإجمالي).</summary>
    public decimal LineTotal => Cost * Quantity;
}

/// <summary>
/// A purchase invoice (فاتورة مشتريات) — the second purchasing stage. It raises the supplier obligation
/// (Dr بضاعة واردة لم تُفوتر / المشتريات, Cr الموردون) and is what supplier payments settle; its stock-tracked
/// lines are received into <see cref="WarehouseId"/> by an automatic goods receipt (Dr المخزون / Cr بضاعة
/// واردة لم تُفوتر), so the products' cost follows the invoice costs under the costing method. It may
/// reference a purchase order; several invoices can reference the same order.
/// </summary>
public class PurchaseInvoice : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed number per tenant (year*1000000 + seq, e.g. 2026000001), shown as PI-2026000001.</summary>
    public int Number { get; set; }
    public DateTime InvoiceDate { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>Optional project this invoice is charged to.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Optional purchase order this invoice bills.</summary>
    public Guid? PurchaseOrderId { get; set; }
    public SupplierOrder? PurchaseOrder { get; set; }

    /// <summary>Warehouse the stock-tracked lines are received into, through an automatic goods receipt
    /// (<see cref="GoodsReceipt.PurchaseInvoiceId"/>) at the invoice unit costs. Required when the invoice has
    /// stock-tracked products.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? Notes { get; set; }

    public ICollection<PurchaseInvoiceItem> Items { get; set; } = new List<PurchaseInvoiceItem>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();

    /// <summary>Total = sum of the line totals (computed when items are loaded).</summary>
    public decimal TotalCost => Items?.Sum(i => i.LineTotal) ?? 0m;
}

/// <summary>A product line (صنف) on a purchase invoice.</summary>
public class PurchaseInvoiceItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? Invoice { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>The product's "code — name" at save time, so the invoice reads the same after a rename.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit cost (تكلفة الوحدة).</summary>
    public decimal Cost { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Line total = unit cost × quantity, rounded to money precision. Stored, so every sum (ledger,
    /// payments, lists) uses the same figure.</summary>
    public decimal LineTotal { get; set; }
}

/// <summary>A payment made to a supplier (against their overall balance) — a safe Expense movement.</summary>
public class SupplierPayment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    /// <summary>The purchase invoice this payment settles (payments are made per invoice).</summary>
    public Guid? PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? Invoice { get; set; }

    /// <summary>Legacy only: the order this payment settled, from before the invoice stage existed.</summary>
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

/// <summary>A file attached to a supplier order (contract, quotation, invoice scan, …), stored inline.</summary>
public class SupplierOrderAttachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SupplierOrderId { get; set; }
    public SupplierOrder? Order { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
