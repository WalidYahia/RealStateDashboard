using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;

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

// ---------- Order CRUD ----------
public class OrderItemInput
{
    // Not [Required]: blank rows are dropped server-side so an empty trailing row is harmless.
    [Display(Name = "البند")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "التكلفة")]
    public decimal Cost { get; set; }
}

public class OrderFormModel
{
    public Guid Id { get; set; }
    public int Number { get; set; } // display only, when editing

    [Required(ErrorMessage = "المورد مطلوب")]
    [Display(Name = "المورد")]
    public Guid? SupplierId { get; set; }

    [Display(Name = "المشروع")]
    public Guid? ProjectId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الأمر")]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<OrderItemInput> Items { get; set; } = new();

    public List<SelectListItem> Suppliers { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
}

// ---------- Orders list ----------
public class OrderListItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }
    public string Supplier { get; set; } = string.Empty;
    public string Project { get; set; } = "—";
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining => Total - Paid;
}

// ---------- Supplier account statement (كشف الحساب) ----------
public enum SupplierLedgerKind { Order, Payment }

/// <summary>One row of the running-balance supplier ledger (an order obligation or a payment).</summary>
public class SupplierLedgerRow
{
    public SupplierLedgerKind Kind { get; set; }
    public Guid Id { get; set; }                    // payment id (for the receipt link)
    public string Source { get; set; } = string.Empty;    // المصدر — e.g. "أمر توريد رقم PO-0002" / "إيصال دفع"
    public DateTime Date { get; set; }
    public string Statement { get; set; } = string.Empty; // البيان
    public int ReceiptNo { get; set; }              // for payments
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }      // running amount owed before this row
    public decimal Balance { get; set; }            // running amount owed after this row
}

public class SupplierStatementVm
{
    public Supplier Supplier { get; set; } = default!;

    // Overall account balances (NOT date-filtered) — shown in the cards.
    public decimal TotalObligations { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining => TotalObligations - TotalPaid;
    public int OrdersCount { get; set; }
    public int PaymentsCount { get; set; }
    /// <summary>True when at least one order still has an outstanding balance (drives the pay button).</summary>
    public bool HasPayableOrders { get; set; }

    // Date-filtered ledger detail — shown in the flat running-balance table.
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SupplierLedgerRow> Rows { get; set; } = new();
    // Closing balance as of the end of the selected range (not necessarily the current total).
    public decimal ClosingBalance { get; set; }

    public bool HasEntries => Rows.Count > 0;
}

// ---------- Supplier-level pay picker (choose an unpaid order from the statement) ----------
public class SupplierPayPickerModel
{
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public List<SupplierOrderOption> Orders { get; set; } = new();
    public List<SelectListItem> Safes { get; set; } = new();
}

public record SupplierOrderOption(Guid Id, string Label, decimal Remaining);

// ---------- Payment form ----------
public class SupplierPayFormModel
{
    public Guid OrderId { get; set; }
    public string OrderLabel { get; set; } = string.Empty;
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
