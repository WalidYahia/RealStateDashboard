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

/// <summary>
/// A print/report template (a 1-page PDF used as the background/design layer for every report).
/// Only one is active per tenant. The PDF and its rendered PNG live in private App_Data storage;
/// this row keeps the metadata.
/// </summary>
public class ReportTemplate : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    /// <summary>Base stored name (a GUID); the files on disk are {StoredFileName}.pdf and .png.</summary>
    public string StoredFileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int PageCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
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

/// <summary>
/// A predefined expense/income category (بند) shown when recording a manual transaction. The three
/// built-in behavioural kinds (عام / سلفة / مكافأة) are seeded per tenant and cannot be edited/deleted.
/// </summary>
public class TxnCategory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Enums.TxnType Type { get; set; }                                  // Income or Expense
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }                                      // general/advance/reward — locked
    public Enums.AccountingEntryKind BuiltInKind { get; set; } = Enums.AccountingEntryKind.General;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
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
