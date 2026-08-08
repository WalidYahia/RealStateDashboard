using RealState.Application.Common;

namespace RealState.Application.Entities;

/// <summary>An immutable record of one user action (who did what, when, from where). Tenant-scoped.</summary>
public class ActivityLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;   // denormalized for display

    public string ActionType { get; set; } = string.Empty; // Create / Update / Delete / Login / Logout / Other
    public string? Area { get; set; }
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Description { get; set; }
    public string? IpAddress { get; set; }

    public DateTime Timestamp { get; set; }
}
