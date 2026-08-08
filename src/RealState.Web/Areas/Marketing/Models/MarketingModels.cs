using System.ComponentModel.DataAnnotations;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Marketing.Models;

/// <summary>Add / edit a campaign (shown in a modal).</summary>
public class CampaignFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم الحملة مطلوب")]
    [Display(Name = "اسم الحملة")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "المنصة")]
    public CampaignPlatform Platform { get; set; }

    [Display(Name = "النوع")]
    public CampaignType Type { get; set; }

    [Display(Name = "الهدف من الحملة")]
    public CampaignObjective Objective { get; set; }

    [Display(Name = "حالة الحملة")]
    public CampaignStatus Status { get; set; } = CampaignStatus.Active;

    [DataType(DataType.Date)]
    [Display(Name = "تاريخ البدء")]
    public DateTime? StartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "تاريخ الانتهاء")]
    public DateTime? EndDate { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الميزانية (ج.م)")]
    public decimal Budget { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

/// <summary>Insert the latest cumulative reading for a campaign; the controller stores the delta.</summary>
public class CampaignUpdateFormModel
{
    public Guid CampaignId { get; set; }
    public string CampaignName { get; set; } = string.Empty;

    [Required(ErrorMessage = "التاريخ مطلوب")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ القراءة")]
    public DateTime Date { get; set; } = DateTime.Today;

    /// <summary>The most recent reading date already logged (used to warn on back-dated entries).</summary>
    public DateTime? LatestDate { get; set; }

    // Current accumulated totals (read-only reference shown in the modal).
    public int CurrentReach { get; set; }
    public int CurrentLeads { get; set; }
    public decimal CurrentCost { get; set; }
    public decimal CurrentSales { get; set; }
    public int CurrentReservations { get; set; }

    // The new latest cumulative totals entered by the user.
    [Range(0, int.MaxValue, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "إجمالي الوصول (Reach)")]
    public int ReachTotal { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "إجمالي الـLeads")]
    public int LeadsTotal { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "إجمالي التكلفة (ج.م)")]
    public decimal CostTotal { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "إجمالي المبيعات الناتجة (ج.م)")]
    public decimal SalesTotal { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "إجمالي الحجوزات")]
    public int ReservationsTotal { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

/// <summary>Aggregated per-campaign row for the tables/exports.</summary>
public class CampaignRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CampaignPlatform Platform { get; set; }
    public CampaignStatus Status { get; set; }
    public int Reach { get; set; }
    public int Leads { get; set; }
    public decimal Cost { get; set; }
    public decimal Sales { get; set; }
    public int Reservations { get; set; }
    public decimal Budget { get; set; }
    public int UpdatesCount { get; set; }
    public DateTime? LastUpdate { get; set; }
}

public class MarketingDashboardVm
{
    public int TotalLeads { get; set; }
    public int TotalReach { get; set; }
    public decimal TotalCost { get; set; }
    public decimal TotalSales { get; set; }
    public int TotalReservations { get; set; }
    public decimal TotalBudget { get; set; }
    public decimal Cpl => TotalLeads <= 0 ? 0 : Math.Round(TotalCost / TotalLeads, 1);
    public decimal Roas => TotalCost <= 0 ? 0 : Math.Round(TotalSales / TotalCost, 1);

    public List<CampaignRow> Campaigns { get; set; } = new();          // all (table shows top 5)
    public List<SourceSlice> Sources { get; set; } = new();            // leads by platform
    public Guid? DefaultCampaignId { get; set; }                       // last campaign for the chart
}

public record SourceSlice(string Platform, int Leads, int Percent);

/// <summary>A cumulative time series for one campaign (for the performance chart / compare).</summary>
public record CampaignSeries(Guid Id, string Name, List<string> Labels, List<int> Reach, List<int> Leads);
