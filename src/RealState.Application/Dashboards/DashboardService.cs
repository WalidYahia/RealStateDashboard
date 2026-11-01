using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Dashboards;

/// <summary>
/// Aggregates executive KPIs from the implemented modules only (sales contracts, installments,
/// projects/units, marketing campaigns, customers). Tenant-scoped via the query filters.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _db;
    private readonly IDateTimeProvider _clock;

    public DashboardService(IApplicationDbContext db, IDateTimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<DashboardVm> GetExecutiveDashboardAsync(CancellationToken ct = default)
    {
        var today = _clock.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var vm = new DashboardVm();

        // --- Sales ---
        var contracts = await _db.SaleContracts.Where(c => !c.IsCancelled).ToListAsync(ct);   // a cancelled (مفسوخ) contract is no longer a sale
        vm.ContractsCount = contracts.Count;
        vm.TodaySales = contracts.Where(c => c.ReceiveDate.Date == today).Sum(c => c.TotalPrice);
        vm.MonthSales = contracts.Where(c => c.ReceiveDate.Date >= monthStart).Sum(c => c.TotalPrice);
        vm.TotalContractsValue = contracts.Sum(c => c.TotalPrice);

        // --- Collections ---
        // The down payment is scheduled as an installment, so collected = paid installments only.
        // A cancelled contract's schedule is frozen — it's neither collected sales nor outstanding.
        var installments = await _db.Installments.Where(i => !_db.SaleContracts.Any(s => s.Id == i.SaleContractId && s.IsCancelled)).ToListAsync(ct);
        var installmentsPaid = installments.Sum(i => i.PaidAmount);
        vm.TotalCollected = installmentsPaid;
        vm.TotalOutstanding = installments.Where(i => i.PaidAmount < i.Amount).Sum(i => i.Amount - i.PaidAmount);
        vm.OverdueAmount = installments
            .Where(i => i.PaidAmount < i.Amount && i.DueDate.Date < today)
            .Sum(i => i.Amount - i.PaidAmount);
        vm.CollectedThisMonth = installments.Where(i => i.PaidDate != null && i.PaidDate >= monthStart).Sum(i => i.PaidAmount);

        // --- Suppliers: outstanding payables — payments settle a supplier's account, not particular invoices, so this is
        // what's owed on each supplier's account (its subsidiary of الموردون in the journal), summed over those in credit. ---
        var supplierBalances = await (from l in _db.JournalLines
                                      join a in _db.Accounts on l.AccountId equals a.Id
                                      where a.SubKind == "Supplier" && a.SubRefId != null
                                      group l by a.SubRefId into g
                                      select g.Sum(x => x.Credit - x.Debit)).ToListAsync(ct);
        vm.SupplierPayables = supplierBalances.Where(b => b > 0).Sum();

        // --- Projects / units ---
        vm.ProjectsCount = await _db.Projects.CountAsync(ct);
        var units = await _db.ProjectUnits.Select(u => u.Status).ToListAsync(ct);
        vm.UnitsTotal = units.Count;
        vm.UnitsSold = units.Count(s => s == UnitStatus.Sold);
        vm.UnitsAvailable = units.Count(s => s == UnitStatus.Available);

        // --- People / marketing ---
        vm.CustomersCount = await _db.Customers.CountAsync(ct);
        vm.SalespersonsCount = await _db.Employees.CountAsync(e => e.Type == EmployeeType.Salesperson, ct);
        vm.CampaignsCount = await _db.Campaigns.CountAsync(ct);
        vm.CampaignsLeads = await _db.CampaignUpdates.SumAsync(u => (int?)u.Leads, ct) ?? 0;

        // --- Recent sales ---
        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        vm.RecentSales = contracts.OrderByDescending(c => c.CreatedAt).Take(6)
            .Select(c => new RecentSaleRow(
                c.Code,
                custNames.GetValueOrDefault(c.CustomerId, "—"),
                unitNames.GetValueOrDefault(c.UnitId, "—"),
                c.TotalPrice, c.ReceiveDate))
            .ToList();

        // --- Projects sell-through ---
        var unitByProject = (await _db.ProjectUnits
            .GroupBy(u => u.ProjectId)
            .Select(g => new { g.Key, Total = g.Count(), Sold = g.Count(x => x.Status == UnitStatus.Sold) })
            .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Total, x.Sold));
        var projs = await _db.Projects.Select(p => new { p.Id, p.Name }).Take(6).ToListAsync(ct);
        vm.Projects = projs.Select(p =>
        {
            unitByProject.TryGetValue(p.Id, out var u);
            var pct = u.Total <= 0 ? 0 : Math.Round((decimal)u.Sold / u.Total * 100, 0);
            return new ProjectUnitsRow(p.Name, u.Total, u.Sold, pct);
        }).ToList();

        return vm;
    }
}
