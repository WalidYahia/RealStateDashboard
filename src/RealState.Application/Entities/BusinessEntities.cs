using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A real-estate project/compound being sold.</summary>
public class Project : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public int TotalUnits { get; set; }
    public int SoldUnits { get; set; }
    public decimal TargetAmount { get; set; }
    public decimal AchievedAmount { get; set; }
    public Guid? SectionId { get; set; }

    /// <summary>Completion / sell-through percentage (0–100), shown on the dashboard progress bars.</summary>
    public decimal ProgressPercent { get; set; }
}

public class Customer : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public Guid? CityId { get; set; }
    public Guid? CountryId { get; set; }
}

/// <summary>A potential customer captured from a marketing source.</summary>
public class Lead : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Source { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public decimal EstimatedValue { get; set; }
    public Guid? AssignedToUserId { get; set; }
}

public class SalesInvoice : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime InvoiceDate { get; set; }
    public Guid? SalespersonId { get; set; }
    public bool IsReservation { get; set; }
}

public class PurchaseInvoice : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime InvoiceDate { get; set; }
}

public class Income : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public DateTime Date { get; set; }
    public string? Category { get; set; }
}

public class Expense : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public DateTime Date { get; set; }
    public string? Category { get; set; }
}

public class TaskItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public DateTime? DueDate { get; set; }
    public TaskState State { get; set; } = TaskState.Pending;
    public DateTime? CompletedAt { get; set; }
}

public class Notification : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public NotificationLevel Level { get; set; } = NotificationLevel.Info;
    public bool IsRead { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>A stored file linked to any business entity (contract, invoice, image, PDF).</summary>
public class Attachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? EntityType { get; set; }
    public Guid? EntityId { get; set; }
}
