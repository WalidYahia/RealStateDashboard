using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RealState.Application.Interfaces;

namespace RealState.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the model for migrations/scaffolding without booting the web host.
/// Migration scaffolding only diffs the model against the snapshot — it never connects — so the
/// placeholder connection string is never used to reach a database. Not used at application runtime.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=RealStateDesignTime;Trusted_Connection=True;")
            .Options;
        return new ApplicationDbContext(options, new DesignTimeUser(), new DesignTimeClock());
    }

    private sealed class DesignTimeUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => "design-time";
        public Guid TenantId => Guid.Empty;
    }

    private sealed class DesignTimeClock : IDateTimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime Now => DateTime.Now;
        public DateTime Today => DateTime.Today;
    }
}
