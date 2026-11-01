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
    string StockDocLabel,    // إذن استلام (رجوع للمخزن) / إذن صرف (رد للمورد)
    string InvoiceArea, string InvoiceController,
    string PartyArea, string PartyController, string PartyAction)
{
    public static readonly ReturnKind Sales = new(true, "مرتجعات المبيعات", "مرتجع مبيعات", "SR-", "SI-", "فاتورة المبيعات",
        "العميل", "سعر البيع", "رد نقدية للعميل", "إيصال صرف نقدية", "إذن استلام (رجوع الأصناف للمخزن)",
        "Sales", "SalesInvoices", "CRM", "Customers", "Details");

    public static readonly ReturnKind Purchase = new(false, "مرتجعات المشتريات", "مرتجع مشتريات", "PR-", "PI-", "فاتورة المشتريات",
        "المورد", "تكلفة الوحدة", "استرداد نقدية من المورد", "إيصال استلام نقدية", "إذن صرف (رد الأصناف للمورد)",
        "Suppliers", "PurchaseInvoices", "Suppliers", "Suppliers", "Statement");
}

/// <summary>
/// The party's account for a return — payments are on the account, not on invoices. <paramref name="Balance"/> is the
/// account's balance before the return, in its usual direction: what the customer owes / what's owed to the supplier
/// (− = the other way round). The return lowers it by its value; cash may change hands only for the part that leaves
/// the account in credit.
/// </summary>
public sealed record ReturnPartyPosition(decimal Balance)
{
    /// <summary>The largest refund a return of <paramref name="value"/> allows: what the return pushes past the balance
    /// (value − balance), capped by the value. The rest is credited to the account.</summary>
    public decimal MaxRefund(decimal value) => Math.Clamp(value - Balance, 0m, Math.Max(value, 0m));
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

    /// <summary>Date + time of the document, entered by the user (default: now) — auto-created records take it.</summary>
    [DataType(DataType.DateTime)][Display(Name = "تاريخ ووقت المرتجع")]
    public DateTime ReturnDate { get; set; } = ClientTime.Now;

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
    /// <summary>The party's account before this return (with the invoice picked).</summary>
    public ReturnPartyPosition? Position { get; set; }
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
    public AuditStampVm? Audit { get; set; }
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
    /// <summary>The party's account balance now (after all its movements, this return included).</summary>
    public decimal PartyBalance { get; set; }
    public bool CanDelete { get; set; }
    public bool CanDeleteRefund { get; set; }
}

/// <summary>A document's CreationTime / LastUpdateTime (Views/Shared/_AuditStamp).</summary>
public record AuditStampVm(DateTime CreatedAt, string? CreatedBy, DateTime? UpdatedAt, string? UpdatedBy)
{
    public static AuditStampVm Of(RealState.Application.Common.AuditableEntity e) => new(e.CreatedAt, e.CreatedBy, e.UpdatedAt, e.UpdatedBy);
}
