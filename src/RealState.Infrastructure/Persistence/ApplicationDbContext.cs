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
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Section> Sections => Set<Section>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerLog> CustomerLogs => Set<CustomerLog>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignUpdate> CampaignUpdates => Set<CampaignUpdate>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<AttendanceSetting> AttendanceSettings => Set<AttendanceSetting>();
    public DbSet<LateDeductionRule> LateDeductionRules => Set<LateDeductionRule>();
    public DbSet<EmployeeAttachment> EmployeeAttachments => Set<EmployeeAttachment>();
    public DbSet<Vacation> Vacations => Set<Vacation>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<AdvanceRepayment> AdvanceRepayments => Set<AdvanceRepayment>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<SaleContract> SaleContracts => Set<SaleContract>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Safe> Safes => Set<Safe>();
    public DbSet<SafeTransaction> SafeTransactions => Set<SafeTransaction>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierOrder> SupplierOrders => Set<SupplierOrder>();
    public DbSet<SupplierOrderItem> SupplierOrderItems => Set<SupplierOrderItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<StageDefinition> StageDefinitions => Set<StageDefinition>();
    public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
    public DbSet<StageActivity> StageActivities => Set<StageActivity>();
    public DbSet<StageExpense> StageExpenses => Set<StageExpense>();
    public DbSet<ProjectUnit> ProjectUnits => Set<ProjectUnit>();
    public DbSet<ProjectAttachment> ProjectAttachments => Set<ProjectAttachment>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<Income> Incomes => Set<Income>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<WorkTaskLog> WorkTaskLogs => Set<WorkTaskLog>();
    public DbSet<WorkTaskAttachment> WorkTaskAttachments => Set<WorkTaskAttachment>();

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

        builder.Entity<ActivityLog>().HasIndex(l => l.Timestamp);
        builder.Entity<ActivityLog>().HasIndex(l => l.UserId);

        builder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();
        builder.Entity<RolePermission>().HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();

        // Sale contract references three parents — avoid multiple SQL Server cascade paths; deletes are handled in code.
        builder.Entity<SaleContract>().HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SaleContract>().HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SaleContract>().HasOne(s => s.Unit).WithMany().HasForeignKey(s => s.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Installment>().HasOne(i => i.SaleContract).WithMany(s => s.Installments).HasForeignKey(i => i.SaleContractId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SafeTransaction>().HasOne(t => t.Safe).WithMany().HasForeignKey(t => t.SafeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SafeTransaction>().HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // Supplier orders reference a supplier and (optionally) a project — restrict to avoid multiple
        // cascade paths; items cascade with their order, payments/safe are restricted (handled in code).
        builder.Entity<SupplierOrder>().HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierOrder>().HasOne(o => o.Project).WithMany().HasForeignKey(o => o.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierOrderItem>().HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.SupplierOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierPayment>().HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.SupplierOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne<Safe>().WithMany().HasForeignKey(p => p.SafeId).OnDelete(DeleteBehavior.Restrict);

        // HR relationships — restrict most (deletes handled in code); employee attachments & advance
        // repayments cascade with their parent.
        builder.Entity<Employee>().HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().HasOne(e => e.JobRole).WithMany().HasForeignKey(e => e.JobRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAttachment>().HasOne(a => a.Employee).WithMany(e => e.Attachments).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Vacation>().HasOne(v => v.Employee).WithMany().HasForeignKey(v => v.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Advance>().HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AdvanceRepayment>().HasOne(r => r.Advance).WithMany(a => a.Repayments).HasForeignKey(r => r.AdvanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Reward>().HasOne(r => r.Employee).WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // Tasks — restrict the department/assignee references (deletes handled in code); logs and
        // attachments cascade with their parent task.
        builder.Entity<WorkTask>().HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTask>().HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTaskLog>().HasOne(l => l.Task).WithMany(t => t.Logs).HasForeignKey(l => l.WorkTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkTaskAttachment>().HasOne(a => a.Task).WithMany(t => t.Attachments).HasForeignKey(a => a.WorkTaskId).OnDelete(DeleteBehavior.Cascade);

        // Customer communication log cascades with its customer.
        builder.Entity<CustomerLog>().HasOne(l => l.Customer).WithMany().HasForeignKey(l => l.CustomerId).OnDelete(DeleteBehavior.Cascade);
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
