namespace RealState.Application.Common;

/// <summary>Marker for entities scoped to a tenant. Global query filters isolate rows by TenantId.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}

/// <summary>Base for every persisted entity.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>
/// Base carrying the common audit / soft-delete / concurrency fields required on every business table.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    /// <summary>SQL Server rowversion used as the optimistic-concurrency token.</summary>
    public byte[]? RowVersion { get; set; }
}
