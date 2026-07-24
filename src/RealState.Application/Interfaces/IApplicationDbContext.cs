using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;

namespace RealState.Application.Interfaces;

/// <summary>
/// Abstraction the Application layer uses to reach persisted data without depending on EF/Infrastructure types.
/// Implemented by ApplicationDbContext.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Setting> Settings { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Country> Countries { get; }
    DbSet<City> Cities { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<Section> Sections { get; }

    DbSet<Project> Projects { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Lead> Leads { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<Income> Incomes { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Attachment> Attachments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
