using Microsoft.AspNetCore.Identity;
using RealState.Application.Common;

namespace RealState.Application.Identity;

/// <summary>Application user. Uses Guid keys and carries the owning tenant.</summary>
public class ApplicationUser : IdentityUser<Guid>, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Application role with tenant scoping and a friendly description.</summary>
public class ApplicationRole : IdentityRole<Guid>, ITenantEntity
{
    public ApplicationRole() { }
    public ApplicationRole(string roleName) : base(roleName) { }

    public Guid TenantId { get; set; }
    public string? Description { get; set; }
}
