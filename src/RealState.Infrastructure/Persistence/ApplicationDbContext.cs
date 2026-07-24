using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Identity;
using RealState.Application.Interfaces;

namespace RealState.Infrastructure.Persistence;

/// <summary>
/// EF Core context. Adds ASP.NET Identity (Guid keys) and applies two global query filters to every
/// tenant-scoped entity: soft-delete (IsDeleted == false) and tenant isolation (TenantId == current).
/// Audit fields, soft-delete conversion and tenant stamping are handled in <see cref="SaveChangesAsync"/>.
/// </summary>
public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IApplicationDbContext
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
        : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Section> Sections => Set<Section>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Money precision for all decimal properties.
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }

        // rowversion concurrency token on every auditable entity.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(AuditableEntity).IsAssignableFrom(t.ClrType)))
        {
            builder.Entity(entityType.ClrType)
                .Property(nameof(AuditableEntity.RowVersion))
                .IsRowVersion();
        }

        // Global query filters: soft-delete + tenant isolation on tenant-scoped business/admin entities.
        // Identity types (ApplicationUser/ApplicationRole) are excluded so login works before a tenant is established.
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => typeof(ITenantEntity).IsAssignableFrom(t.ClrType)
                                 && t.ClrType != typeof(ApplicationUser)
                                 && t.ClrType != typeof(ApplicationRole)
                                 && t.BaseType is null))
        {
            entityType.SetQueryFilter(BuildTenantFilter(entityType.ClrType));
        }

        builder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();
        builder.Entity<RolePermission>().HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
    }

    /// <summary>Builds `e =&gt; !e.IsDeleted &amp;&amp; e.TenantId == currentTenant` for a tenant entity type.</summary>
    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "e");

        // Auditable entities also carry IsDeleted — combine both predicates when present.
        Expression body = Expression.Equal(
            Expression.Property(parameter, nameof(ITenantEntity.TenantId)),
            Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));

        if (typeof(AuditableEntity).IsAssignableFrom(entityType))
        {
            var notDeleted = Expression.Not(Expression.Property(parameter, nameof(AuditableEntity.IsDeleted)));
            body = Expression.AndAlso(notDeleted, body);
        }

        return Expression.Lambda(body, parameter);
    }

    /// <summary>Read by the compiled query filter; re-evaluated per query so tenant switches are honored.</summary>
    public Guid CurrentTenantId => _currentUser.TenantId;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndTenant();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndTenant();
        return base.SaveChanges();
    }

    private void ApplyAuditAndTenant()
    {
        var now = _clock.UtcNow;
        var user = _currentUser.UserName ?? "system";
        var tenantId = _currentUser.TenantId;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantEntity tenantEntity && entry.State == EntityState.Added && tenantEntity.TenantId == Guid.Empty)
                tenantEntity.TenantId = tenantId;

            if (entry.Entity is not AuditableEntity auditable) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    // Preserve explicit values (e.g. historical dates set by the seeder).
                    if (auditable.CreatedAt == default) auditable.CreatedAt = now;
                    auditable.CreatedBy ??= user;
                    break;
                case EntityState.Modified:
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                    break;
                case EntityState.Deleted:
                    // Convert hard deletes to soft deletes.
                    entry.State = EntityState.Modified;
                    auditable.IsDeleted = true;
                    auditable.DeletedAt = now;
                    auditable.DeletedBy = user;
                    break;
            }
        }
    }
}
