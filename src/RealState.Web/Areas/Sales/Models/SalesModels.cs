using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Sales.Models;

public class SaleFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "العميل مطلوب")]
    [Display(Name = "العميل")]
    public Guid? CustomerId { get; set; }

    // Project is chosen to filter the units list; the saved ProjectId is derived from the unit.
    [Display(Name = "المشروع")]
    public Guid? ProjectId { get; set; }

    [Required(ErrorMessage = "الوحدة مطلوبة")]
    [Display(Name = "الوحدة")]
    public Guid? UnitId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ العقد")]
    public DateTime ContractDate { get; set; } = ClientTime.Today;

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الاستلام")]
    public DateTime ReceiveDate { get; set; } = ClientTime.Today;

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ أول قسط")]
    public DateTime FirstInstallmentDate { get; set; } = ClientTime.Today;

    [Range(1, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "السعر الإجمالي (ج.م)")]
    public decimal TotalPrice { get; set; }

    // ---- Payment schedule ----
    // Manual rows (date, amount, type) are entered by hand in parallel lists, one entry per table row.
    // Whatever they leave of the total price (المتبقي) is split into InstallmentsCount auto «قسط» rows,
    // starting FirstInstallmentDate and spaced by Step.
    public List<DateTime?> ManualDates { get; set; } = new();
    public List<decimal?> ManualAmounts { get; set; } = new();
    public List<InstallmentType?> ManualTypes { get; set; } = new();

    [Range(0, 600, ErrorMessage = "عدد الأقساط بين 0 و600")]
    [Display(Name = "عدد الأقساط")]
    public int InstallmentsCount { get; set; }

    [Display(Name = "دورية القسط")]
    public InstallmentStep Step { get; set; }

    /// <summary>The saved schedule, shown read-only when <see cref="LockFinancials"/>.</summary>
    public List<SaleScheduleRow> LockedSchedule { get; set; } = new();

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<SelectListItem> Customers { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
    public List<SaleUnitOption> UnitOptions { get; set; } = new(); // rendered with data-project for filtering

    // --- Edit mode ---
    /// <summary>Display name of the (locked) customer when editing.</summary>
    public string? CustomerName { get; set; }
    /// <summary>Display name of the (locked) unit when editing.</summary>
    public string? UnitName { get; set; }
    /// <summary>True when a collection already exists — financial fields & schedule can't change (dates/notes only).</summary>
    public bool LockFinancials { get; set; }
}

/// <summary>فسخ التعاقد popup: the fee percentage (of what the customer paid) and when / why.</summary>
public class SaleCancelFormModel
{
    public Guid Id { get; set; }

    [Range(0, 100, ErrorMessage = "النسبة بين 0 و100")]
    [Display(Name = "رسوم الفسخ (% من المدفوع)")]
    public decimal FeePercent { get; set; }

    [Required(ErrorMessage = "تاريخ الفسخ مطلوب")]
    [Display(Name = "تاريخ ووقت الفسخ")]
    public DateTime CancelledAt { get; set; } = ClientTime.Now;

    [StringLength(500)]
    [Display(Name = "سبب الفسخ / ملاحظات")]
    public string? Reason { get; set; }

    // --- display ---
    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal TotalPrice { get; set; }
    /// <summary>What the customer has paid so far (the fee is a percentage of it).</summary>
    public decimal Paid { get; set; }
}

/// <summary>A refund (سند صرف) paid to the customer of a cancelled contract.</summary>
public class SaleRefundFormModel
{
    public Guid ContractId { get; set; }

    [Required(ErrorMessage = "اختر الخزنة")]
    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    [Range(0.01, 999999999999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")]
    [Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "التاريخ مطلوب")]
    [Display(Name = "تاريخ ووقت الصرف")]
    public DateTime PaidAt { get; set; } = ClientTime.Now;

    [StringLength(300)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    // --- display ---
    public string Code { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal Due { get; set; }
    public decimal Refunded { get; set; }
    public decimal Remaining => Due - Refunded;
    public List<SelectListItem> Safes { get; set; } = new();
}

/// <summary>A refund voucher on a cancelled contract's page.</summary>
public record SaleRefundRow(Guid Id, long Serial, DateTime Date, string SafeName, decimal Amount, string Description);

/// <summary>One row of a contract's payment schedule.</summary>
public record SaleScheduleRow(DateTime DueDate, decimal Amount, InstallmentType Type, bool IsAuto);

/// <summary>An available unit option carrying its project id so the units list can be filtered client-side.</summary>
public record SaleUnitOption(Guid Id, string Label, Guid ProjectId);

public class CollectionRow
{
    public Guid InstallmentId { get; set; }
    public Guid ContractId { get; set; }
    public string ContractCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
    public int Number { get; set; }
    public InstallmentType Type { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? PaidDate { get; set; }
    public int? ReceiptNo { get; set; }
    public InstallmentStatus Status { get; set; }
    /// <summary>The installment's contract was cancelled (فسخ) — its collections are frozen.</summary>
    public bool IsCancelledContract { get; set; }

    /// <summary>Lowercased haystack for the client-side search box.</summary>
    public string Search => $"{CustomerName} {CustomerPhone} {ProjectName} {UnitLabel} {ContractCode}".ToLower();
}

public class CollectionsVm
{
    public decimal TotalOutstanding { get; set; }
    public decimal OverdueAmount { get; set; }
    public int OverdueCount { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public int DueCount { get; set; }
    public List<CollectionRow> Outstanding { get; set; } = new();
}

/// <summary>A row in the sales list.</summary>
public class SaleListItem
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public DateTime ContractDate { get; set; }
    public DateTime ReceiveDate { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal Paid { get; set; }
    /// <summary>Still owed by the customer — nothing once the contract is cancelled.</summary>
    public decimal Remaining => IsCancelled ? 0 : TotalPrice - Paid;

    public bool IsCancelled { get; set; }
    /// <summary>Cancelled: still owed back to the customer (refund due − refunded).</summary>
    public decimal RefundRemaining { get; set; }
}

/// <summary>Contracts-list status filter (الحالة).</summary>
public enum SaleContractState
{
    Active = 0,      // سارٍ
    Cancelled = 1,   // مفسوخ
}

/// <summary>The sales list plus its date-range and status filters.</summary>
public class SalesListVm
{
    public List<SaleListItem> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>Chosen statuses; empty = all.</summary>
    public List<SaleContractState> Status { get; set; } = new();

    public decimal TotalValue => Rows.Sum(r => r.TotalPrice);
    public decimal TotalPaid => Rows.Sum(r => r.Paid);
    public decimal TotalRemaining => Rows.Sum(r => r.Remaining);
}

/// <summary>Analytics cards + chart series for the sales summary page.</summary>
public class SalesSummaryVm
{
    public int ContractsCount { get; set; }
    public decimal TotalValue { get; set; }
    public decimal TotalCollected { get; set; }
    public decimal TotalRemaining { get; set; }
    public int CustomersCount { get; set; }
    public int SalespersonsCount { get; set; }
    public int UnitsSold { get; set; }
    public decimal AvgContractValue => ContractsCount == 0 ? 0 : TotalValue / ContractsCount;

    // Current calendar month vs previous month, for the KPI-card deltas (٪ عن الشهر الماضي).
    public decimal SalesThisMonth { get; set; }
    public decimal SalesPrevMonth { get; set; }
    public decimal CollectedThisMonth { get; set; }
    public decimal CollectedPrevMonth { get; set; }
    public int ContractsThisMonth { get; set; }
    public int ContractsPrevMonth { get; set; }
    public int NewCustomersThisMonth { get; set; }
    public int NewCustomersPrevMonth { get; set; }

    public List<ProjectSales> ByProject { get; set; } = new();
    public List<MonthlySales> ByMonth { get; set; } = new();
    public List<PipelineStage> Pipeline { get; set; } = new();
    public List<RecentContract> Latest { get; set; } = new();

    /// <summary>Month-over-month change as a percentage (0 previous ⇒ +100% when current &gt; 0).</summary>
    public static decimal DeltaPct(decimal cur, decimal prev)
        => prev == 0 ? (cur > 0 ? 100 : 0) : Math.Round((cur - prev) / prev * 100, 1);
}

public record ProjectSales(string Project, int Count, decimal Value);
public record MonthlySales(string Label, decimal Value);
public record PipelineStage(string Label, int Count);
public record RecentContract(string Code, string Customer, string Project, decimal Value, DateTime Date);
