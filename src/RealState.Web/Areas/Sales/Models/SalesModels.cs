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
    public DateTime ContractDate { get; set; } = DateTime.Today;

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ الاستلام")]
    public DateTime ReceiveDate { get; set; } = DateTime.Today;

    [Required][DataType(DataType.Date)][Display(Name = "تاريخ أول قسط")]
    public DateTime FirstInstallmentDate { get; set; } = DateTime.Today;

    [Range(1, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "السعر الإجمالي (ج.م)")]
    public decimal TotalPrice { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "المقدم (ج.م)")]
    public decimal DownPayment { get; set; }

    [Range(0, 600, ErrorMessage = "عدد الأقساط بين 0 و600")]
    [Display(Name = "عدد الأقساط")]
    public int InstallmentsCount { get; set; }

    [Display(Name = "دورية القسط")]
    public InstallmentStep Step { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<SelectListItem> Customers { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
    public List<SaleUnitOption> UnitOptions { get; set; } = new(); // rendered with data-project for filtering
}

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
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? PaidDate { get; set; }
    public int? ReceiptNo { get; set; }
    public InstallmentStatus Status { get; set; }

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
    public decimal Remaining => TotalPrice - Paid;
}

/// <summary>The sales list plus its date-range filter.</summary>
public class SalesListVm
{
    public List<SaleListItem> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

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
