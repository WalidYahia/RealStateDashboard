using RealState.Application.Common;

namespace RealState.Application.Entities;

// =====================================================================================
//  Returns (المرتجعات) — always against one invoice, line by line, never more than the invoice line minus
//  what earlier returns already took back. Each return is a credit note on its invoice: it lowers what the
//  invoice leaves owed (invoice remaining = total − returns − (paid − refunds)). When the money had already
//  been settled, the return can refund cash through a safe (RefundAmount).
// =====================================================================================

/// <summary>
/// A product sales return (مرتجع مبيعات) against a <see cref="ProductSalesInvoice"/>. Posts Dr مردودات المبيعات /
/// Cr العملاء; its stock-tracked lines come back into <see cref="WarehouseId"/> through an automatic, posted goods
/// receipt (reason: sales return) at their original cost of sale — Dr المخزون / Cr تكلفة المبيعات. A cash refund
/// to the customer is a safe Expense movement (source SalesReturnRefund): Dr العملاء / Cr الخزنة.
/// </summary>
public class ProductSalesReturn : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed number per tenant (year*1000000 + seq), shown as SR-2026000001.</summary>
    public int Number { get; set; }
    public DateTime ReturnDate { get; set; }

    public Guid SalesInvoiceId { get; set; }
    public ProductSalesInvoice? Invoice { get; set; }

    /// <summary>The invoice's customer (copied, for lists and the ledger).</summary>
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Warehouse the returned stock-tracked lines go back into. Required when any line is stock-tracked.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? Notes { get; set; }

    /// <summary>Cash refunded to the customer for this return (0 = credited to the customer's balance only).</summary>
    public decimal RefundAmount { get; set; }
    public Guid? RefundSafeId { get; set; }
    /// <summary>The Expense movement that paid the refund (removed together with the return).</summary>
    public Guid? RefundTransactionId { get; set; }
    /// <summary>The refund voucher number (إيصال صرف نقدية) — the Expense movement's serial.</summary>
    public long? RefundVoucherNo { get; set; }

    public ICollection<ProductSalesReturnItem> Items { get; set; } = new List<ProductSalesReturnItem>();
}

/// <summary>A returned line — a quantity of one sales-invoice line, in that line's unit and at its price.</summary>
public class ProductSalesReturnItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalesReturnId { get; set; }
    public ProductSalesReturn? Return { get; set; }

    /// <summary>The invoice line being returned (caps the quantity: invoiced − returned before).</summary>
    public Guid InvoiceItemId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public byte UnitLevel { get; set; } = 1;
    public decimal UnitFactor { get; set; } = 1m;

    /// <summary>The invoice line's unit selling price.</summary>
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

/// <summary>
/// A purchase return (مرتجع مشتريات) against a <see cref="PurchaseInvoice"/> — goods sent back to the supplier.
/// Its stock-tracked lines leave <see cref="WarehouseId"/> through an automatic, posted goods issue (reason:
/// purchase return) at the invoice cost — Dr بضاعة واردة لم تُفوتر / Cr المخزون — and the return posts
/// Dr الموردون / Cr بضاعة واردة لم تُفوتر (stock lines) / Cr مردودات المشتريات (non-stock lines). Cash the supplier
/// pays back is a safe Income movement (source PurchaseReturnRefund): Dr الخزنة / Cr الموردون.
/// </summary>
public class PurchaseReturn : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed number per tenant (year*1000000 + seq), shown as PR-2026000001.</summary>
    public int Number { get; set; }
    public DateTime ReturnDate { get; set; }

    public Guid PurchaseInvoiceId { get; set; }
    public PurchaseInvoice? Invoice { get; set; }

    /// <summary>The invoice's supplier and project (copied, for lists and the ledger).</summary>
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public Guid? ProjectId { get; set; }

    /// <summary>Warehouse the returned stock-tracked lines leave from. Required when any line is stock-tracked.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? Notes { get; set; }

    /// <summary>Cash the supplier paid back for this return (0 = credited to the supplier's balance only).</summary>
    public decimal RefundAmount { get; set; }
    public Guid? RefundSafeId { get; set; }
    /// <summary>The Income movement that received the refund (removed together with the return).</summary>
    public Guid? RefundTransactionId { get; set; }
    /// <summary>The refund receipt number (إيصال استلام نقدية) — the Income movement's serial.</summary>
    public long? RefundVoucherNo { get; set; }

    public ICollection<PurchaseReturnItem> Items { get; set; } = new List<PurchaseReturnItem>();
}

/// <summary>A returned line — a quantity of one purchase-invoice line, in that line's unit and at its cost.</summary>
public class PurchaseReturnItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid PurchaseReturnId { get; set; }
    public PurchaseReturn? Return { get; set; }

    /// <summary>The invoice line being returned (caps the quantity: invoiced − returned before).</summary>
    public Guid InvoiceItemId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public byte UnitLevel { get; set; } = 1;
    public decimal UnitFactor { get; set; } = 1m;

    /// <summary>The invoice line's unit cost.</summary>
    public decimal Cost { get; set; }
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
