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
    DbSet<ActivityLog> ActivityLogs { get; }

    DbSet<Country> Countries { get; }
    DbSet<City> Cities { get; }
    DbSet<Currency> Currencies { get; }
    DbSet<Section> Sections { get; }

    DbSet<Project> Projects { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Lead> Leads { get; }
    DbSet<Campaign> Campaigns { get; }
    DbSet<CampaignUpdate> CampaignUpdates { get; }
    DbSet<Employee> Employees { get; }
    DbSet<Department> Departments { get; }
    DbSet<JobRole> JobRoles { get; }
    DbSet<AttendanceSetting> AttendanceSettings { get; }
    DbSet<LateDeductionRule> LateDeductionRules { get; }
    DbSet<EmployeeAttachment> EmployeeAttachments { get; }
    DbSet<Vacation> Vacations { get; }
    DbSet<Advance> Advances { get; }
    DbSet<AdvanceRepayment> AdvanceRepayments { get; }
    DbSet<Reward> Rewards { get; }
    DbSet<SaleContract> SaleContracts { get; }
    DbSet<Installment> Installments { get; }
    DbSet<Safe> Safes { get; }
    DbSet<SafeTransaction> SafeTransactions { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<SupplierOrder> SupplierOrders { get; }
    DbSet<SupplierOrderItem> SupplierOrderItems { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }
    DbSet<StageDefinition> StageDefinitions { get; }
    DbSet<ProjectStage> ProjectStages { get; }
    DbSet<StageActivity> StageActivities { get; }
    DbSet<StageExpense> StageExpenses { get; }
    DbSet<ProjectUnit> ProjectUnits { get; }
    DbSet<ProjectAttachment> ProjectAttachments { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<Income> Incomes { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<TaskItem> Tasks { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Attachment> Attachments { get; }

    DbSet<WorkTask> WorkTasks { get; }
    DbSet<WorkTaskLog> WorkTaskLogs { get; }
    DbSet<WorkTaskAttachment> WorkTaskAttachments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
