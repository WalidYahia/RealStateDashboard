using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Suppliers.Models;

// ---------- Supplier CRUD ----------
public class SupplierFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المورد مطلوب")]
    [Display(Name = "اسم المورد / المقاول")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "البريد الإلكتروني (اختياري)")]
    public string? Email { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

// ---------- Product picker shared by order + invoice lines ----------
/// <summary>A product offered in a line's searchable picker: total on-hand stock (<see cref="Tracked"/> = stock-tracked) and unit of
/// measure. <see cref="StockByWarehouse"/> (sales invoices) lets the picker show what's available in the chosen warehouse.</summary>
public record ProductOption(Guid Id, string Label, decimal Stock, bool Tracked, string? Unit, IReadOnlyDictionary<Guid, decimal>? StockByWarehouse = null);

/// <summary>One product line (صنف) on an order or invoice form.</summary>
public class DocItemInput
{
    // Not [Required]: rows without a product are dropped server-side so an empty trailing row is harmless.
    [Display(Name = "الصنف")]
    public Guid? ProductId { get; set; }

    /// <summary>Label of a legacy free-text order line (no product); kept as-is when the order is re-saved.</summary>
    public string? LegacyName { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "تكلفة الوحدة")]
    public decimal Cost { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الكمية")]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>Line total = unit cost × quantity, rounded to money precision.</summary>
    public decimal LineTotal => Math.Round(Cost * Quantity, 2);

    public bool IsBlank => ProductId is null && string.IsNullOrWhiteSpace(LegacyName);
}

/// <summary>Input for the shared product-lines editor partial (_DocItems).</summary>
/// <param name="PriceLabel">Header of the unit-price column («تكلفة الوحدة» on purchases, «سعر البيع» on sales invoices).</param>
/// <param name="StockLabel">Header of the stock column (per-warehouse availability on sales invoices).</param>
public record DocItemsVm(List<DocItemInput> Items, List<ProductOption> Products, bool ShowStock, bool ShowCost = true,
    string PriceLabel = "تكلفة الوحدة", string StockLabel = "المخزون الحالي");

// ---------- Order CRUD ----------
public class OrderFormModel
{
    public Guid Id { get; set; }
    /// <summary>Display only: the order's number, or the number the next new order will take.</summary>
    public int Number { get; set; }

    [Display(Name = "المشروع")]
    public Guid? ProjectId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الأمر")]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<DocItemInput> Items { get; set; } = new();

    public List<SelectListItem> Projects { get; set; } = new();
    public List<ProductOption> Products { get; set; } = new();

    /// <summary>Display only: orders are quantity-only; true just for a legacy order that still owns a payable.</summary>
    public bool ShowCost { get; set; }
}

/// <summary>A purchase invoice reference shown on an order (number + link).</summary>
public record InvoiceRef(Guid Id, int Number);

// ---------- Orders list ----------
public class OrderListItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }
    public string Project { get; set; } = "—";
    public decimal TotalQuantity { get; set; }
    public int ItemCount { get; set; }
    public bool HasAttachments { get; set; }
    public List<InvoiceRef> Invoices { get; set; } = new();
}

// ---------- Invoice CRUD ----------
public class InvoiceFormModel
{
    public Guid Id { get; set; }
    /// <summary>Display only: the invoice's number, or the number the next new invoice will take.</summary>
    public int Number { get; set; }

    [Required(ErrorMessage = "المورد مطلوب")]
    [Display(Name = "المورد")]
    public Guid? SupplierId { get; set; }

    [Display(Name = "المشروع")]
    public Guid? ProjectId { get; set; }

    [Display(Name = "أمر التوريد")]
    public Guid? PurchaseOrderId { get; set; }

    [Display(Name = "المخزن (استلام الأصناف المخزنية)")]
    public Guid? WarehouseId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الفاتورة")]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<DocItemInput> Items { get; set; } = new();

    public List<SelectListItem> Suppliers { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
    public List<SelectListItem> Orders { get; set; } = new();
    public List<SelectListItem> Warehouses { get; set; } = new();
    public List<ProductOption> Products { get; set; } = new();
}

// ---------- Invoices list ----------
public class InvoiceListItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime InvoiceDate { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public string Project { get; set; } = "—";
    public Guid? OrderId { get; set; }
    public int? OrderNumber { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining => Total - Paid;
}

/// <summary>A goods receipt generated by an invoice.</summary>
public record InvoiceReceiptRef(Guid Id, int Number, DateTime Date, InventoryDocStatus Status);

/// <summary>Invoice details page: the invoice plus display names resolved for its references.</summary>
public class InvoiceDetailsVm
{
    public PurchaseInvoice Invoice { get; set; } = default!;
    public string Supplier { get; set; } = "—";
    public string? SupplierPhone { get; set; }
    public string? Project { get; set; }
    public int? OrderNumber { get; set; }
    public string? Warehouse { get; set; }
    /// <summary>The invoice's automatic goods receipts — the current (posted) one plus any reversed by edits.</summary>
    public List<InvoiceReceiptRef> Receipts { get; set; } = new();
    public decimal Total => Invoice.Items.Sum(i => i.LineTotal);
    public decimal Paid => Invoice.Payments.Sum(p => p.Amount);
    public decimal Remaining => Total - Paid;
}

// ---------- Supplier account statement (كشف الحساب) ----------
public enum SupplierLedgerKind { Order, Invoice, Payment }

/// <summary>One row of the running-balance supplier ledger (an invoice / legacy order obligation, or a payment).</summary>
public class SupplierLedgerRow
{
    public SupplierLedgerKind Kind { get; set; }
    public Guid Id { get; set; }                    // invoice / order / payment id (for the link)
    public string Source { get; set; } = string.Empty;    // المصدر — e.g. "فاتورة مشتريات رقم PI-2026000001" / "إيصال صرف نقدية"
    public DateTime Date { get; set; }
    public string Statement { get; set; } = string.Empty; // البيان
    public long ReceiptNo { get; set; }             // for payments
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }      // running amount owed before this row
    public decimal Balance { get; set; }            // running amount owed after this row
    public bool IsObligation => Kind != SupplierLedgerKind.Payment;
}

public class SupplierStatementVm
{
    public Supplier Supplier { get; set; } = default!;

    // Overall account balances (NOT date-filtered) — shown in the cards.
    public decimal TotalObligations { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining => TotalObligations - TotalPaid;
    public int InvoicesCount { get; set; }
    public int PaymentsCount { get; set; }
    /// <summary>True when at least one invoice (or legacy order) still has an outstanding balance (drives the pay button).</summary>
    public bool HasPayableDocuments { get; set; }

    // Date-filtered ledger detail — shown in the flat running-balance table.
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SupplierLedgerRow> Rows { get; set; } = new();
    // Closing balance as of the end of the selected range (not necessarily the current total).
    public decimal ClosingBalance { get; set; }

    public bool HasEntries => Rows.Count > 0;
}

// ---------- Supplier-level pay picker (choose an unpaid invoice from the statement) ----------
public class SupplierPayPickerModel
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public List<PayableOption> Documents { get; set; } = new();
    public List<SelectListItem> Safes { get; set; } = new();
}

/// <summary>An unpaid obligation offered for payment. <see cref="Target"/> is "I:{invoiceId}" or "O:{legacyOrderId}".</summary>
public record PayableOption(string Target, string Label, decimal Remaining);

// ---------- Payment form ----------
public class SupplierPayFormModel
{
    /// <summary>What is being paid: "I:{invoiceId}" (purchase invoice) or "O:{orderId}" (legacy order).</summary>
    public string Target { get; set; } = string.Empty;
    public string DocumentLabel { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining { get; set; }

    [Range(0.01, 999999999999, ErrorMessage = "المبلغ غير صالح")]
    [Display(Name = "المبلغ المدفوع (ج.م)")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "اختر الخزنة")]
    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الدفع")]
    public DateTime PaidDate { get; set; } = DateTime.Today;

    [Display(Name = "ملاحظات")]
    public string? Description { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();
}
