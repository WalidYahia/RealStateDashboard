using RealState.Application.Common;

namespace RealState.Application.Entities;

// =====================================================================================
//  Product sales invoices (فواتير المبيعات) — selling inventory products to a customer, the mirror of
//  purchase invoices. Unrelated to the real-estate sales module (عقود البيع), but billed to the same
//  Customers. (The legacy demo table «SalesInvoices» is a different, unused entity.)
// =====================================================================================

/// <summary>
/// A product sales invoice (فاتورة مبيعات). It raises the customer receivable and the sales revenue
/// (Dr العملاء / Cr إيرادات مبيعات البضائع). Its stock-tracked lines leave <see cref="WarehouseId"/> through an
/// automatic, posted goods issue (reason: sale), which costs them under the costing method and books
/// Dr تكلفة المبيعات / Cr المخزون. Settled by <see cref="SalesInvoiceCollection"/>s (Dr الخزنة / Cr العملاء).
/// </summary>
public class ProductSalesInvoice : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed number per tenant (year*1000000 + seq, e.g. 2026000001), shown as SI-2026000001.</summary>
    public int Number { get; set; }
    public DateTime InvoiceDate { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Warehouse the stock-tracked lines are issued from, through an automatic goods issue
    /// (<see cref="GoodsIssue.SalesInvoiceId"/>). Required when the invoice has stock-tracked products.</summary>
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public string? Notes { get; set; }

    public ICollection<ProductSalesInvoiceItem> Items { get; set; } = new List<ProductSalesInvoiceItem>();
    public ICollection<SalesInvoiceCollection> Collections { get; set; } = new List<SalesInvoiceCollection>();
}

/// <summary>A product line (صنف) on a sales invoice.</summary>
public class ProductSalesInvoiceItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public ProductSalesInvoice? Invoice { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>The product's "code — name" at save time, so the invoice reads the same after a rename.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The product's unit of measure name at save time (الوحدة).</summary>
    public string? Unit { get; set; }

    /// <summary>Unit selling price (سعر البيع), entered on the invoice.</summary>
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>Line total = price × quantity, rounded to money precision. Stored, so every sum (ledger,
    /// collections, lists) uses the same figure.</summary>
    public decimal LineTotal { get; set; }
}

/// <summary>
/// A collection (تحصيل) received from the customer against one sales invoice — a safe Income movement
/// (source <see cref="Enums.TxnSource.SalesInvoiceCollection"/>) whose serial is the receipt number.
/// </summary>
public class SalesInvoiceCollection : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid SalesInvoiceId { get; set; }
    public ProductSalesInvoice? Invoice { get; set; }

    public decimal Amount { get; set; }
    public DateTime CollectedDate { get; set; }

    /// <summary>The safe the money was received into.</summary>
    public Guid SafeId { get; set; }

    /// <summary>The Income movement that recorded the cash (removed together with the collection).</summary>
    public Guid? SafeTransactionId { get; set; }

    /// <summary>Receipt number (إيصال استلام نقدية) — equal to the Income movement's serial (e.g. 20260000001).</summary>
    public long ReceiptNo { get; set; }

    public string? Description { get; set; }
}
