using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RealState.Application.Interfaces;
using RealState.Infrastructure.Persistence;
using RealState.Infrastructure.Services;

namespace RealState.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence and infrastructure services. Identity, authentication cookies and the
    /// HTTP-bound <see cref="ICurrentUserService"/> are registered by the Web layer.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                // Target SQL Server 2012 (level 110): avoids OPENJSON-based translation,
                // which does not exist before SQL Server 2016 and triggers "syntax near 'WITH'".
                // Also valid on Azure SQL Database (which supports levels 100–160).
                sql.UseCompatibilityLevel(110);
                // Azure SQL closes idle/throttled connections; retry transient failures so the
                // app (and the startup migration) survive brief drops instead of crashing.
                sql.EnableRetryOnFailure(
                    maxRetryCount: 6,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
            }));

        // Expose the context through the Application-layer abstraction.
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}
