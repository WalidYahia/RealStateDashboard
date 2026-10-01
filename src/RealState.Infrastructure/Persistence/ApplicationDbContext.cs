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
    public DbSet<ReportTemplate> ReportTemplates => Set<ReportTemplate>();
    public DbSet<TxnCategory> TxnCategories => Set<TxnCategory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<GoodsIssue> GoodsIssues => Set<GoodsIssue>();
    public DbSet<GoodsIssueLine> GoodsIssueLines => Set<GoodsIssueLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<InventoryAdjustmentLine> InventoryAdjustmentLines => Set<InventoryAdjustmentLine>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<InventoryPostingProfile> InventoryPostingProfiles => Set<InventoryPostingProfile>();

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<Section> Sections => Set<Section>();

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerLog> CustomerLogs => Set<CustomerLog>();
    public DbSet<CampaignLead> CampaignLeads => Set<CampaignLead>();
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
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<AdvanceRepayment> AdvanceRepayments => Set<AdvanceRepayment>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<SaleContract> SaleContracts => Set<SaleContract>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Safe> Safes => Set<Safe>();
    public DbSet<SafeTransaction> SafeTransactions => Set<SafeTransaction>();
    public DbSet<SafeTransfer> SafeTransfers => Set<SafeTransfer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierOrder> SupplierOrders => Set<SupplierOrder>();
    public DbSet<SupplierOrderItem> SupplierOrderItems => Set<SupplierOrderItem>();
    public DbSet<SupplierOrderAttachment> SupplierOrderAttachments => Set<SupplierOrderAttachment>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<Contractor> Contractors => Set<Contractor>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderLog> WorkOrderLogs => Set<WorkOrderLog>();
    public DbSet<WorkOrderPayment> WorkOrderPayments => Set<WorkOrderPayment>();
    public DbSet<StageDefinition> StageDefinitions => Set<StageDefinition>();
    public DbSet<ProjectTypeDefinition> ProjectTypes => Set<ProjectTypeDefinition>();
    public DbSet<ProjectStage> ProjectStages => Set<ProjectStage>();
    public DbSet<StageActivity> StageActivities => Set<StageActivity>();
    public DbSet<StageExpense> StageExpenses => Set<StageExpense>();
    public DbSet<ProjectUnit> ProjectUnits => Set<ProjectUnit>();
    public DbSet<ProjectAttachment> ProjectAttachments => Set<ProjectAttachment>();
    public DbSet<ProjectUnitAttachment> ProjectUnitAttachments => Set<ProjectUnitAttachment>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceItem> PurchaseInvoiceItems => Set<PurchaseInvoiceItem>();
    public DbSet<ProductSalesInvoice> ProductSalesInvoices => Set<ProductSalesInvoice>();
    public DbSet<ProductSalesInvoiceItem> ProductSalesInvoiceItems => Set<ProductSalesInvoiceItem>();
    public DbSet<SalesInvoiceCollection> SalesInvoiceCollections => Set<SalesInvoiceCollection>();
    public DbSet<UserNavItem> UserNavItems => Set<UserNavItem>();
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

        // Double-entry ledger: unique code/number per tenant, subsidiary + source lookups, cascade lines.
        builder.Entity<Account>().HasIndex(a => new { a.TenantId, a.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<Account>().HasIndex(a => new { a.TenantId, a.SubKind, a.SubRefId });
        builder.Entity<Account>().HasOne(a => a.Parent).WithMany().HasForeignKey(a => a.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<JournalEntry>().HasIndex(e => new { e.TenantId, e.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<JournalEntry>().HasIndex(e => new { e.SourceType, e.SourceId });
        builder.Entity<JournalLine>().HasOne(l => l.Entry).WithMany(e => e.Lines).HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<JournalLine>().HasOne(l => l.Account).WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Inventory quantities and unit costs need more than 2 decimals (fractional units, low unit costs).
        // Money totals stay (18,2) from the global convention above.
        builder.Entity<InventoryMovement>().Property(m => m.QuantityIn).HasPrecision(18, 4);
        builder.Entity<InventoryMovement>().Property(m => m.QuantityOut).HasPrecision(18, 4);
        builder.Entity<InventoryMovement>().Property(m => m.UnitCost).HasPrecision(18, 6);
        builder.Entity<GoodsReceiptLine>().Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Entity<GoodsReceiptLine>().Property(l => l.UnitCost).HasPrecision(18, 6);
        builder.Entity<GoodsIssueLine>().Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Entity<GoodsIssueLine>().Property(l => l.UnitCost).HasPrecision(18, 6);
        builder.Entity<StockTransferLine>().Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Entity<StockTransferLine>().Property(l => l.UnitCost).HasPrecision(18, 6);
        builder.Entity<InventoryAdjustmentLine>().Property(l => l.QuantityDelta).HasPrecision(18, 4);
        builder.Entity<InventoryAdjustmentLine>().Property(l => l.UnitCost).HasPrecision(18, 6);
        builder.Entity<StockCountLine>().Property(l => l.SystemQty).HasPrecision(18, 4);
        builder.Entity<StockCountLine>().Property(l => l.CountedQty).HasPrecision(18, 4);

        // Multi-unit products: factors to the smallest unit (e.g. كيلو = 1000 متر = 100000 سم), on the product and
        // snapshotted on every document line.
        builder.Entity<Product>().Property(p => p.Unit2Factor).HasPrecision(18, 6);
        builder.Entity<Product>().Property(p => p.Unit3Factor).HasPrecision(18, 6);
        builder.Entity<Product>().Property(p => p.SalePrice).HasPrecision(18, 4);
        builder.Entity<Product>().Property(p => p.SalePrice2).HasPrecision(18, 4);
        builder.Entity<Product>().Property(p => p.SalePrice3).HasPrecision(18, 4);
        builder.Entity<GoodsReceiptLine>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<GoodsIssueLine>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<StockTransferLine>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<InventoryAdjustmentLine>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<StockCountLine>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<SupplierOrderItem>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<PurchaseInvoiceItem>().Property(l => l.UnitFactor).HasPrecision(18, 6);
        builder.Entity<ProductSalesInvoiceItem>().Property(l => l.UnitFactor).HasPrecision(18, 6);

        // Inventory: unique master codes + document numbers per tenant; subledger lookup; cascade doc lines.
        builder.Entity<Product>().HasIndex(p => new { p.TenantId, p.Sku }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<Warehouse>().HasIndex(w => new { w.TenantId, w.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<UnitOfMeasure>().HasIndex(u => new { u.TenantId, u.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<InventoryPostingProfile>().HasIndex(p => p.TenantId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<InventoryMovement>().HasIndex(m => new { m.TenantId, m.ProductId, m.WarehouseId, m.Date });
        builder.Entity<InventoryMovement>().HasIndex(m => new { m.ReferenceType, m.ReferenceId });

        builder.Entity<GoodsReceipt>().HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<GoodsIssue>().HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<StockTransfer>().HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<InventoryAdjustment>().HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<StockCount>().HasIndex(d => new { d.TenantId, d.Number }).IsUnique().HasFilter("[IsDeleted] = 0");

        builder.Entity<GoodsReceiptLine>().HasOne<GoodsReceipt>().WithMany(d => d.Lines).HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<GoodsIssueLine>().HasOne<GoodsIssue>().WithMany(d => d.Lines).HasForeignKey(l => l.GoodsIssueId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<StockTransferLine>().HasOne<StockTransfer>().WithMany(d => d.Lines).HasForeignKey(l => l.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<InventoryAdjustmentLine>().HasOne<InventoryAdjustment>().WithMany(d => d.Lines).HasForeignKey(l => l.InventoryAdjustmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<StockCountLine>().HasOne<StockCount>().WithMany(d => d.Lines).HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);

        // Product FKs restricted (deletes handled in code); movement → product/warehouse restricted.
        builder.Entity<Product>().HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Product>().HasOne(p => p.UnitOfMeasure).WithMany().HasForeignKey(p => p.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Product>().HasOne(p => p.Unit2).WithMany().HasForeignKey(p => p.Unit2Id).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Product>().HasOne(p => p.Unit3).WithMany().HasForeignKey(p => p.Unit3Id).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryMovement>().HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InventoryMovement>().HasOne(m => m.Warehouse).WithMany().HasForeignKey(m => m.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        // Sale contract references three parents — avoid multiple SQL Server cascade paths; deletes are handled in code.
        builder.Entity<SaleContract>().HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SaleContract>().HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SaleContract>().HasOne(s => s.Unit).WithMany().HasForeignKey(s => s.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Installment>().HasOne(i => i.SaleContract).WithMany(s => s.Installments).HasForeignKey(i => i.SaleContractId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SafeTransaction>().HasOne(t => t.Safe).WithMany().HasForeignKey(t => t.SafeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SafeTransaction>().HasOne(t => t.Project).WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);

        // Safe transfers: unique number per tenant; both safes + both movements restricted (deletes handled in code).
        builder.Entity<SafeTransfer>().HasIndex(t => new { t.TenantId, t.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<SafeTransfer>().HasOne(t => t.FromSafe).WithMany().HasForeignKey(t => t.FromSafeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SafeTransfer>().HasOne(t => t.ToSafe).WithMany().HasForeignKey(t => t.ToSafeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SafeTransfer>().HasOne<SafeTransaction>().WithMany().HasForeignKey(t => t.OutTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SafeTransfer>().HasOne<SafeTransaction>().WithMany().HasForeignKey(t => t.InTransactionId).OnDelete(DeleteBehavior.Restrict);

        // Supplier orders reference a supplier and (optionally) a project — restrict to avoid multiple
        // cascade paths; items cascade with their order, payments/safe are restricted (handled in code).
        builder.Entity<SupplierOrder>().HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierOrder>().HasOne(o => o.Project).WithMany().HasForeignKey(o => o.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierOrderItem>().HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.SupplierOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierOrderAttachment>().HasOne(a => a.Order).WithMany().HasForeignKey(a => a.SupplierOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<SupplierOrderItem>().HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierOrderItem>().Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Entity<SupplierOrderItem>().Property(i => i.Cost).HasPrecision(18, 4);

        // Purchase invoices reference a supplier, and optionally a project and a purchase order — all
        // restricted (deletes handled in code); items cascade with their invoice.
        builder.Entity<PurchaseInvoice>().HasIndex(i => new { i.TenantId, i.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<PurchaseInvoice>().HasOne(i => i.Supplier).WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseInvoice>().HasOne(i => i.Project).WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseInvoice>().HasOne(i => i.PurchaseOrder).WithMany().HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseInvoice>().HasOne(i => i.Warehouse).WithMany().HasForeignKey(i => i.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        // An invoice's automatic goods receipts point back at it (SET NULL so a hard tenant purge of invoices can't be blocked).
        builder.Entity<GoodsReceipt>().HasOne<PurchaseInvoice>().WithMany().HasForeignKey(r => r.PurchaseInvoiceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<PurchaseInvoiceItem>().HasOne(i => i.Invoice).WithMany(v => v.Items).HasForeignKey(i => i.PurchaseInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<PurchaseInvoiceItem>().HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PurchaseInvoiceItem>().Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Entity<PurchaseInvoiceItem>().Property(i => i.Cost).HasPrecision(18, 4);

        // Product sales invoices mirror purchase invoices: customer / warehouse restricted (deletes
        // handled in code); items cascade with their invoice; collections restrict.
        builder.Entity<ProductSalesInvoice>().HasIndex(i => new { i.TenantId, i.Number }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<ProductSalesInvoice>().HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductSalesInvoice>().HasOne(i => i.Warehouse).WithMany().HasForeignKey(i => i.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        // An invoice's automatic goods issues point back at it (SET NULL so a hard tenant purge of invoices can't be blocked).
        builder.Entity<GoodsIssue>().HasOne<ProductSalesInvoice>().WithMany().HasForeignKey(r => r.SalesInvoiceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<ProductSalesInvoiceItem>().HasOne(i => i.Invoice).WithMany(v => v.Items).HasForeignKey(i => i.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProductSalesInvoiceItem>().HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProductSalesInvoiceItem>().Property(i => i.Quantity).HasPrecision(18, 4);
        builder.Entity<ProductSalesInvoiceItem>().Property(i => i.Price).HasPrecision(18, 4);
        builder.Entity<SalesInvoiceCollection>().HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SalesInvoiceCollection>().HasOne(c => c.Invoice).WithMany(i => i.Collections).HasForeignKey(c => c.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
        // Navigation shortcuts (favorites / recent pages) per user.
        builder.Entity<UserNavItem>().HasIndex(x => new { x.TenantId, x.UserKey, x.Kind });
        builder.Entity<UserNavItem>().Property(x => x.UserKey).HasMaxLength(100);
        builder.Entity<UserNavItem>().Property(x => x.Key).HasMaxLength(400);
        builder.Entity<UserNavItem>().Property(x => x.Title).HasMaxLength(200);
        builder.Entity<UserNavItem>().Property(x => x.Url).HasMaxLength(400);
        builder.Entity<SalesInvoiceCollection>().HasOne<Safe>().WithMany().HasForeignKey(c => c.SafeId).OnDelete(DeleteBehavior.Restrict);

        // Contracting: work orders reference a contractor + project (restrict); logs cascade with their order;
        // payments restrict (deletes handled in code).
        builder.Entity<WorkOrder>().HasOne(o => o.Contractor).WithMany().HasForeignKey(o => o.ContractorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkOrder>().HasOne(o => o.Project).WithMany().HasForeignKey(o => o.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkOrderLog>().HasOne(l => l.Order).WithMany().HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkOrderPayment>().HasOne(p => p.Contractor).WithMany().HasForeignKey(p => p.ContractorId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkOrderPayment>().HasOne(p => p.Order).WithMany().HasForeignKey(p => p.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkOrderPayment>().HasOne<Safe>().WithMany().HasForeignKey(p => p.SafeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(p => p.Supplier).WithMany().HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(p => p.Order).WithMany(o => o.Payments).HasForeignKey(p => p.SupplierOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne(p => p.Invoice).WithMany(i => i.Payments).HasForeignKey(p => p.PurchaseInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SupplierPayment>().HasOne<Safe>().WithMany().HasForeignKey(p => p.SafeId).OnDelete(DeleteBehavior.Restrict);

        // HR relationships — restrict most (deletes handled in code); employee attachments & advance
        // repayments cascade with their parent.
        builder.Entity<Employee>().HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Employee>().HasOne(e => e.JobRole).WithMany().HasForeignKey(e => e.JobRoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<EmployeeAttachment>().HasOne(a => a.Employee).WithMany(e => e.Attachments).HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Vacation>().HasOne(v => v.Employee).WithMany().HasForeignKey(v => v.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LeaveRequest>().HasOne(l => l.Employee).WithMany().HasForeignKey(l => l.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Advance>().HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<AdvanceRepayment>().HasOne(r => r.Advance).WithMany(a => a.Repayments).HasForeignKey(r => r.AdvanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Reward>().HasOne(r => r.Employee).WithMany().HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // Tasks — restrict the department/assignee references (deletes handled in code); logs and
        // attachments cascade with their parent task.
        builder.Entity<WorkTask>().HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTask>().HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTaskLog>().HasOne(l => l.Task).WithMany(t => t.Logs).HasForeignKey(l => l.WorkTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<WorkTaskAttachment>().HasOne(a => a.Task).WithMany(t => t.Attachments).HasForeignKey(a => a.WorkTaskId).OnDelete(DeleteBehavior.Cascade);

        // Project references its settings-defined type (restrict — a type in use can't be deleted);
        // unit attachments cascade with their unit.
        builder.Entity<Project>().HasOne(p => p.ProjectTypeRef).WithMany().HasForeignKey(p => p.ProjectTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProjectUnitAttachment>().HasOne(a => a.Unit).WithMany().HasForeignKey(a => a.UnitId).OnDelete(DeleteBehavior.Cascade);

        // Customer communication log cascades with its customer.
        builder.Entity<CustomerLog>().HasOne(l => l.Customer).WithMany().HasForeignKey(l => l.CustomerId).OnDelete(DeleteBehavior.Cascade);

        // Imported campaign leads: one per platform record (unique ExternalId per tenant), cascades with its lead/customer.
        builder.Entity<CampaignLead>().HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<CampaignLead>().HasIndex(c => new { c.TenantId, c.ExternalId }).IsUnique();
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

    public IEnumerable<TEntity> PendingAdds<TEntity>() where TEntity : class
        => ChangeTracker.Entries<TEntity>().Where(e => e.State == EntityState.Added).Select(e => e.Entity);

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
