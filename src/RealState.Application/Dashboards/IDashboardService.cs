namespace RealState.Application.Dashboards;

public interface IDashboardService
{
    Task<DashboardVm> GetExecutiveDashboardAsync(CancellationToken cancellationToken = default);
}
