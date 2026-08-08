namespace RealState.Application.Dashboards;

public record RecentSaleRow(string Code, string Customer, string Unit, decimal Total, DateTime Date);
public record ProjectUnitsRow(string Name, int UnitsTotal, int Sold, decimal Percent);

/// <summary>Executive dashboard built from the implemented modules: sales, collections, projects, marketing, customers.</summary>
public class DashboardVm
{
    // Sales
    public decimal TodaySales { get; set; }
    public decimal MonthSales { get; set; }
    public decimal TotalContractsValue { get; set; }
    public int ContractsCount { get; set; }

    // Collections
    public decimal TotalCollected { get; set; }
    public decimal TotalOutstanding { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal CollectedThisMonth { get; set; }

    // Suppliers — outstanding payables (order totals minus payments, for orders not fully paid).
    public decimal SupplierPayables { get; set; }
    public decimal CollectionPercent =>
        TotalContractsValue <= 0 ? 0 : Math.Round(TotalCollected / TotalContractsValue * 100, 0);

    // Projects / units
    public int ProjectsCount { get; set; }
    public int UnitsTotal { get; set; }
    public int UnitsSold { get; set; }
    public int UnitsAvailable { get; set; }

    // People / marketing
    public int CustomersCount { get; set; }
    public int SalespersonsCount { get; set; }
    public int CampaignsCount { get; set; }
    public int CampaignsLeads { get; set; }

    public List<RecentSaleRow> RecentSales { get; set; } = new();
    public List<ProjectUnitsRow> Projects { get; set; } = new();
}
