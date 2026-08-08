using Microsoft.Extensions.DependencyInjection;
using RealState.Application.Dashboards;

namespace RealState.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services (dashboard aggregation, validators, mappers).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<Accounting.IAccountingService, Accounting.AccountingService>();
        services.AddScoped<Activity.IActivityLogger, Activity.ActivityLogger>();
        return services;
    }
}
