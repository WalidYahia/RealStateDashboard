using RealState.Application.Common;

namespace RealState.Application.Entities;

/// <summary>A tenant (company) that owns all business data. Not itself tenant-scoped.</summary>
public class Tenant : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>Tenant logo image bytes, shown in the app branding. Null = fall back to the tenant name.</summary>
    public byte[]? LogoData { get; set; }
    public string? LogoContentType { get; set; }
}

/// <summary>A single permission that can be granted to a role.</summary>
public class Permission : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Group { get; set; }
}

/// <summary>Join row granting a permission to a role.</summary>
public class RolePermission : BaseEntity
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Permission? Permission { get; set; }
}

/// <summary>Tenant-scoped key/value application setting.</summary>
public class Setting : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>Immutable record of a data change for auditing.</summary>
public class AuditLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime Timestamp { get; set; }
    public string? IpAddress { get; set; }
}
