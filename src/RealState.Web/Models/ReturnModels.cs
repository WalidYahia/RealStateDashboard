using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Enums;

namespace RealState.Web.Models;

// Sales returns (مرتجعات المبيعات) and purchase returns (مرتجعات المشتريات) share these models and the views in
// Views/Shared/Returns — the two differ only in their party (customer / supplier), the direction of the stock and
// cash, and labels (see ReturnKind).

/// <summary>Labels and links that differ between a sales return and a purchase return.</summary>
public sealed record ReturnKind(
    bool IsSales,
    string Title,            // مرتجعات المبيعات
    string One,              // مرتجع مبيعات
    string Prefix,           // SR- / PR-
    string InvoicePrefix,    // SI- / PI-
    string InvoiceOne,       // فاتورة المبيعات
    string PartyLabel,       // العميل / المورد
    string PriceLabel,       // سعر البيع / تكلفة الوحدة
    string RefundLabel,      // رد نقدية للعميل / استرداد نقدية من المورد
    string RefundVoucher,    // إيصال صرف نقدية / إيصال استلام نقدية
    string PaidLabel,        // المحصَّل / المسدَّد
    string StockDocLabel,    // إذن استلام (رجوع للمخزن) / إذن صرف (رد للمورد)
    string InvoiceArea, string InvoiceController,
    string PartyArea, string PartyController, string PartyAction)
{
    public static readonly ReturnKind Sales = new(true, "مرتجعات المبيعات", "مرتجع مبيعات", "SR-", "SI-", "فاتورة المبيعات",
        "العميل", "سعر البيع", "رد نقدية للعميل", "إيصال صرف نقدية", "المحصَّل", "إذن استلام (رجوع الأصناف للمخزن)",
        "Sales", "SalesInvoices", "CRM", "Customers", "Details");

    public static readonly ReturnKind Purchase = new(false, "مرتجعات المشتريات", "مرتجع مشتريات", "PR-", "PI-", "فاتورة المشتريات",
        "المورد", "تكلفة الوحدة", "استرداد نقدية من المورد", "إيصال استلام نقدية", "المسدَّد", "إذن صرف (رد الأصناف للمورد)",
        "Suppliers", "PurchaseInvoices", "Suppliers", "Suppliers", "Statement");
}

/// <summary>An invoice's money position for a return: what it leaves owed, and how much may be refunded.</summary>
public sealed record ReturnInvoicePosition(decimal Total, decimal Returned, decimal Paid, decimal Refunded)
{
    /// <summary>Still owed on the invoice: total − returned − (paid − refunded).</summary>
    public decimal Remaining => Total - Returned - (Paid - Refunded);
    /// <summary>Paid on the invoice and not refunded yet — the most any refund can give back.</summary>
    public decimal NetPaid => Paid - Refunded;
    /// <summary>The smallest refund a return of <paramref name="value"/> needs: the part the invoice no longer owes.</summary>
    public decimal MinRefund(decimal value) => Math.Max(0m, value - Math.Max(Remaining, 0m));
    /// <summary>The largest refund a return of <paramref name="value"/> allows: its value, capped by what was paid.</summary>
    public decimal MaxRefund(decimal value) => Math.Max(0m, Math.Min(value, NetPaid));
}

// ---------- Create (modal) ----------
public class ReturnLineInput
{
    public Guid InvoiceItemId { get; set; }
    /// <summary>Quantity returned; a line left empty (null) isn't returned. Nullable on purpose: a non-nullable decimal
    /// turns every empty line into a model-binding error the form has no place to show.</summary>
    public decimal? Quantity { get; set; }
}

/// <summary>An invoice line as offered on the return form.</summary>
public sealed record ReturnLineVm(Guid InvoiceItemId, string Name, string? Unit, decimal Price, decimal Invoiced, decimal ReturnedBefore, bool Tracked)
{
    public decimal Returnable => Math.Max(0m, Invoiced - ReturnedBefore);
}

public class ReturnFormModel
{
    /// <summary>Display only: the number the new return will take.</summary>
    public int Number { get; set; }

    [Display(Name = "الفاتورة")]
    public Guid? InvoiceId { get; set; }

    [DataType(DataType.Date)][Display(Name = "تاريخ المرتجع")]
    public DateTime ReturnDate { get; set; } = DateTime.Today;

    [Display(Name = "المخزن")]
    public Guid? WarehouseId { get; set; }

    [Display(Name = "سبب المرتجع / ملاحظات")]
    public string? Notes { get; set; }

    [Display(Name = "المبلغ النقدي")]
    public decimal RefundAmount { get; set; }

    [Display(Name = "الخزنة")]
    public Guid? RefundSafeId { get; set; }

    public List<ReturnLineInput> Lines { get; set; } = new();

    // ---- display ----
    public ReturnKind Kind { get; set; } = ReturnKind.Sales;
    public string? InvoiceLabel { get; set; }
    public string? Party { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public ReturnInvoicePosition? Position { get; set; }
    public List<ReturnLineVm> Rows { get; set; } = new();
    public List<SelectListItem> Invoices { get; set; } = new();
    public List<SelectListItem> Warehouses { get; set; } = new();
    public List<SelectListItem> Safes { get; set; } = new();
    public bool HasTrackedLines => Rows.Any(r => r.Tracked);
    public decimal QuantityFor(Guid invoiceItemId) => Lines.FirstOrDefault(l => l.InvoiceItemId == invoiceItemId)?.Quantity ?? 0m;
}

// ---------- List ----------
public class ReturnListItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid InvoiceId { get; set; }
    public int? InvoiceNumber { get; set; }
    public string Party { get; set; } = "—";
    public int ItemCount { get; set; }
    public decimal Total { get; set; }
    public decimal Refund { get; set; }
}

public class ReturnIndexVm
{
    public ReturnKind Kind { get; set; } = ReturnKind.Sales;
    public List<ReturnListItem> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? PartyId { get; set; }
    public List<SelectListItem> Parties { get; set; } = new();
    public bool CanCreate { get; set; }
}

// ---------- Details / print ----------
public sealed record ReturnDetailLine(string Name, string? Unit, decimal Price, decimal Quantity, decimal LineTotal);

/// <summary>The return's automatic stock document (receipt for a sales return, issue for a purchase return).</summary>
public sealed record ReturnStockDocRef(Guid Id, int Number, DateTime Date, InventoryDocStatus Status, decimal Cost);

public class ReturnDetailsVm
{
    public ReturnKind Kind { get; set; } = ReturnKind.Sales;
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid InvoiceId { get; set; }
    public int? InvoiceNumber { get; set; }
    public Guid PartyId { get; set; }
    public string Party { get; set; } = "—";
    public string? PartyPhone { get; set; }
    public string? Warehouse { get; set; }
    public string? Notes { get; set; }
    public List<ReturnDetailLine> Lines { get; set; } = new();
    public decimal Total => Lines.Sum(l => l.LineTotal);
    public decimal Refund { get; set; }
    public long? RefundVoucherNo { get; set; }
    public string? RefundSafe { get; set; }
    /// <summary>The part of the return credited to the party's balance (not paid in cash).</summary>
    public decimal Credited => Total - Refund;
    public List<ReturnStockDocRef> StockDocs { get; set; } = new();
    /// <summary>The invoice after all its returns.</summary>
    public ReturnInvoicePosition? Invoice { get; set; }
    public bool CanDelete { get; set; }
    public bool CanDeleteRefund { get; set; }
}
