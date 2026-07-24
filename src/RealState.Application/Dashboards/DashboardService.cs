using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Dashboards;

/// <summary>
/// Aggregates the executive KPIs and chart series from business data. All queries run against the
/// tenant-filtered <see cref="IApplicationDbContext"/>, so results are automatically scoped to the
/// current tenant and exclude soft-deleted rows.
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
        var trendStart = today.AddDays(-29);

        var vm = new DashboardVm();

        // --- KPI cards. Reservations are a separate pipeline, excluded from sales/collection money. ---
        vm.TodaySales = await _db.SalesInvoices
            .Where(i => !i.IsReservation && i.Status != InvoiceStatus.Cancelled
                        && i.InvoiceDate >= today && i.InvoiceDate < today.AddDays(1))
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;

        vm.NewLeads = await _db.Leads.CountAsync(l => l.CreatedAt >= monthStart, ct);

        vm.ReservationsToday = await _db.SalesInvoices
            .CountAsync(i => i.IsReservation && i.InvoiceDate >= today && i.InvoiceDate < today.AddDays(1), ct);

        // --- Sales vs monthly target (donut) ---
        vm.MonthlySales = await _db.SalesInvoices
            .Where(i => !i.IsReservation && i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= monthStart)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;

        vm.MonthlyTarget = await _db.Projects.SumAsync(p => (decimal?)p.TargetAmount, ct) ?? 0m;
        if (vm.MonthlyTarget <= 0) vm.MonthlyTarget = 7_000_000m;

        // --- Collection (donut). Target is the overall sales goal; achieved is cash actually received. ---
        vm.CollectionAchieved = await _db.SalesInvoices
            .Where(i => !i.IsReservation && i.Status != InvoiceStatus.Cancelled)
            .SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0m;
        vm.CollectionTarget = vm.MonthlyTarget;
        vm.TotalCollection = vm.CollectionAchieved;

        // --- Tasks summary ---
        vm.TasksCompleted = await _db.Tasks.CountAsync(t => t.State == TaskState.Completed, ct);
        vm.TasksInProgress = await _db.Tasks.CountAsync(t => t.State == TaskState.InProgress || t.State == TaskState.Pending, ct);
        vm.TasksOverdue = await _db.Tasks.CountAsync(t => t.State == TaskState.Overdue, ct);
        vm.TaskCompletionPercent = vm.TasksTotal == 0 ? 0 : Math.Round((decimal)vm.TasksCompleted / vm.TasksTotal * 100, 0);

        // --- Customer service (approximated from notifications) ---
        vm.UrgentAlerts = await _db.Notifications.CountAsync(n => n.Level == NotificationLevel.Urgent && !n.IsRead, ct);
        vm.NewRequests = await _db.Notifications.CountAsync(n => !n.IsRead, ct);
        vm.OpenComplaints = await _db.Notifications.CountAsync(n => n.Level == NotificationLevel.Warning && !n.IsRead, ct);
        vm.ClosedRequests = await _db.Notifications.CountAsync(n => n.IsRead, ct);

        // --- Leads trend (last 30 days). Group client-side to keep the SQL simple. ---
        var leadDates = await _db.Leads
            .Where(l => l.CreatedAt >= trendStart)
            .Select(l => l.CreatedAt)
            .ToListAsync(ct);
        var byDay = leadDates.GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Count());
        for (var d = trendStart.Date; d <= today.Date; d = d.AddDays(1))
            vm.LeadsTrend.Add(new SeriesPoint(d.ToString("dd/MM"), byDay.TryGetValue(d, out var c) ? c : 0));

        // --- Projects progress bars ---
        var projects = await _db.Projects
            .OrderByDescending(p => p.ProgressPercent)
            .Take(6)
            .Select(p => new { p.Name, p.ProgressPercent })
            .ToListAsync(ct);
        vm.ProjectsProgress = projects.Select(p => new ProjectProgressDto(p.Name, p.ProgressPercent)).ToList();

        // --- Campaigns (leads grouped by marketing source) ---
        var campaigns = await _db.Leads
            .Where(l => l.Source != null)
            .GroupBy(l => l.Source!)
            .Select(g => new { Source = g.Key, Leads = g.Count(), Value = g.Sum(x => x.EstimatedValue) })
            .OrderByDescending(c => c.Leads)
            .Take(6)
            .ToListAsync(ct);
        vm.Campaigns = campaigns.Select(c => new CampaignRowDto(c.Source, c.Leads, c.Value)).ToList();

        // --- Alerts ticker (enum -> string mapped in memory) ---
        var alerts = await _db.Notifications
            .OrderByDescending(n => n.Timestamp)
            .Take(5)
            .Select(n => new { n.Title, n.Message, n.Level, n.Timestamp })
            .ToListAsync(ct);
        vm.Alerts = alerts.Select(a => new AlertDto(a.Title, a.Message, a.Level.ToString(), a.Timestamp)).ToList();

        return vm;
    }
}
