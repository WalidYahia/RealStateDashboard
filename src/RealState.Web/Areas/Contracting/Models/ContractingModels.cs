using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Contracting.Models;

// ---------- Contractor ----------
public class ContractorFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المقاول مطلوب")]
    [Display(Name = "اسم المقاول")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Display(Name = "الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [Display(Name = "البريد الإلكتروني")]
    public string? Email { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

// ---------- Work order create/edit ----------
public class WorkOrderFormModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }   // display only, when editing

    [Required(ErrorMessage = "اختر المقاول")]
    [Display(Name = "المقاول")]
    public Guid? ContractorId { get; set; }

    [Required(ErrorMessage = "اختر المشروع")]
    [Display(Name = "المشروع")]
    public Guid? ProjectId { get; set; }

    [Display(Name = "التاريخ")]
    public DateTime OrderDate { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "البيان مطلوب")]
    [Display(Name = "البيان")]
    public string ItemDescription { get; set; } = string.Empty;

    [Required(ErrorMessage = "الوحدة مطلوبة")]
    [Display(Name = "الوحدة")]
    public string Unit { get; set; } = string.Empty;

    [Range(0, 999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الكمية")]
    public decimal Quantity { get; set; }

    [Range(0, 999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الفئة (سعر الوحدة)")]
    public decimal Rate { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<SelectListItem> Contractors { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
}

// ---------- Work order list row ----------
public class WorkOrderListItem
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }
    public string Contractor { get; set; } = "—";
    public string Project { get; set; } = "—";
    public string ItemDescription { get; set; } = "";
    public decimal Total { get; set; }        // الكمية × الفئة
    public decimal ExecutionPercent { get; set; }
    public decimal UpliftPercent { get; set; }
    public decimal Deductions { get; set; }
    public decimal ActualTotal { get; set; }
    public decimal Paid { get; set; }
    public bool HasAttachments { get; set; }
    public decimal Remaining => ActualTotal - Paid;
}

// ---------- Progress field update (popup) ----------
public class ProgressUpdateModel
{
    public Guid WorkOrderId { get; set; }
    public WorkOrderField Field { get; set; }

    [Display(Name = "القيمة")]
    public decimal Value { get; set; }
}

// ---------- Work order log edit ----------
public class WorkOrderLogEditModel
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrderField Field { get; set; }

    [Display(Name = "القيمة")]
    public decimal Value { get; set; }

    [Display(Name = "التاريخ والوقت")]
    public DateTime At { get; set; } = DateTime.Now;
}

// ---------- Work order details view model ----------
public class WorkOrderDetailsVm
{
    public WorkOrder Order { get; set; } = default!;
    public string ContractorName { get; set; } = "—";
    public string ProjectName { get; set; } = "—";
    public decimal Paid { get; set; }
    public List<WorkOrderLog> Logs { get; set; } = new();
}

// ---------- Contractor statement (كشف حساب مقاول) ----------
public enum ContractorLedgerKind { Order, Payment }

public class ContractorLedgerRow
{
    public ContractorLedgerKind Kind { get; set; }
    public Guid Id { get; set; }
    public int Number { get; set; }              // WO number / receipt no
    public DateTime Date { get; set; }
    public string Project { get; set; } = "—";
    public string Statement { get; set; } = "";  // البيان
    public string Unit { get; set; } = "";       // الوحدة
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal ExecutionPercent { get; set; }
    public decimal Due { get; set; }             // المستحق
    public decimal UpliftPercent { get; set; }
    public decimal Deductions { get; set; }
    public decimal ActualTotal { get; set; }     // المستحق بعد التعلية
    public decimal Payment { get; set; }         // الدفعة
    public decimal Balance { get; set; }         // الرصيد
    public string? Notes { get; set; }
}

public class ContractorStatementVm
{
    public Contractor Contractor { get; set; } = default!;
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public decimal TotalObligations { get; set; }
    public decimal TotalPaid { get; set; }
    public int OrdersCount { get; set; }
    public int PaymentsCount { get; set; }
    public bool HasPayableOrders { get; set; }
    public List<ContractorLedgerRow> Rows { get; set; } = new();
    public decimal ClosingBalance { get; set; }
}

// ---------- Pay a work order ----------
public class WorkOrderPayFormModel
{
    public Guid OrderId { get; set; }
    public string OrderLabel { get; set; } = "";
    public decimal Total { get; set; }
    public decimal Paid { get; set; }
    public decimal Remaining { get; set; }

    [Range(0.01, 999999999999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")]
    [Display(Name = "المبلغ")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "اختر الخزنة")]
    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    [Display(Name = "التاريخ")]
    public DateTime PaidDate { get; set; } = DateTime.Today;

    [Display(Name = "ملاحظات")]
    public string? Description { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();
}
