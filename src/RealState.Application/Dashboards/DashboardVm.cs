namespace RealState.Application.Dashboards;

/// <summary>A single point in a time series (label + value) for line/bar charts.</summary>
public record SeriesPoint(string Label, decimal Value);

/// <summary>A project progress bar row on the dashboard.</summary>
public record ProjectProgressDto(string Name, decimal Percent);

/// <summary>A marketing-source row (derived from leads grouped by Source).</summary>
public record CampaignRowDto(string Source, int Leads, decimal EstimatedValue);

/// <summary>A recent notification/alert shown in the ticker.</summary>
public record AlertDto(string Title, string? Message, string Level, DateTime Timestamp);

/// <summary>Everything the executive dashboard view needs, aggregated from seeded/business data.</summary>
public class DashboardVm
{
    // Top KPI cards
    public decimal TodaySales { get; set; }
    public int NewLeads { get; set; }
    public int ReservationsToday { get; set; }
    public decimal TotalCollection { get; set; }
    public decimal TaskCompletionPercent { get; set; }
    public int UrgentAlerts { get; set; }

    // Sales vs target (donut)
    public decimal MonthlySales { get; set; }
    public decimal MonthlyTarget { get; set; }
    public decimal SalesAchievedPercent => MonthlyTarget <= 0 ? 0 : Math.Round(MonthlySales / MonthlyTarget * 100, 0);
    public decimal SalesRemaining => Math.Max(0, MonthlyTarget - MonthlySales);

    // Collection (donut)
    public decimal CollectionTarget { get; set; }
    public decimal CollectionAchieved { get; set; }
    public decimal CollectionPercent => CollectionTarget <= 0 ? 0 : Math.Round(CollectionAchieved / CollectionTarget * 100, 0);
    public decimal CollectionRemaining => Math.Max(0, CollectionTarget - CollectionAchieved);

    // Tasks summary (donut)
    public int TasksCompleted { get; set; }
    public int TasksInProgress { get; set; }
    public int TasksOverdue { get; set; }
    public int TasksTotal => TasksCompleted + TasksInProgress + TasksOverdue;

    // Customer service
    public int NewRequests { get; set; }
    public int OpenComplaints { get; set; }
    public int ClosedRequests { get; set; }

    // Charts / tables
    public List<SeriesPoint> LeadsTrend { get; set; } = new();
    public List<ProjectProgressDto> ProjectsProgress { get; set; } = new();
    public List<CampaignRowDto> Campaigns { get; set; } = new();
    public List<AlertDto> Alerts { get; set; } = new();
}
