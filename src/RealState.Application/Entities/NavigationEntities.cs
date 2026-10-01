using RealState.Application.Common;

namespace RealState.Application.Entities;

public enum UserNavItemKind : byte
{
    Favorite = 1,   // a navigation page the user starred (Key = page key)
    Recent = 2,     // a page / record the user opened lately (Key = its URL)
}

/// <summary>
/// A per-user navigation preference — a favorite page or a recently visited page. Only shortcuts are stored, never
/// permissions: favorites are shown only while the user can still see the page, and every destination keeps its own
/// authorization. <see cref="UserKey"/> is the user id (or «host» for the static host account), scoped by tenant.
/// </summary>
public class UserNavItem : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string UserKey { get; set; } = string.Empty;
    public UserNavItemKind Kind { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime LastUsedAt { get; set; }
}
