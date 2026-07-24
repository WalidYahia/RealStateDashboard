using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RealState.Application.Identity;
using RealState.Infrastructure.Persistence;

namespace RealState.Web.Security;

/// <summary>
/// Enriches the sign-in principal with the tenant id and one "permission" claim per granted
/// permission (aggregated across the user's roles). Authorization policies match on these claims,
/// so a SuperAdmin — who holds every permission — satisfies every policy.
/// </summary>
public sealed class PermissionClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    private readonly ApplicationDbContext _db;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public PermissionClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options,
        ApplicationDbContext db)
        : base(userManager, roleManager, options)
    {
        _db = db;
        _roleManager = roleManager;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim("tenant_id", user.TenantId.ToString()));
        if (!string.IsNullOrWhiteSpace(user.FullName))
            identity.AddClaim(new Claim("full_name", user.FullName));

        var roleNames = await UserManager.GetRolesAsync(user);
        var roleIds = new List<Guid>();
        foreach (var roleName in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is not null) roleIds.Add(role.Id);
        }

        var permissions = await (
            from rp in _db.RolePermissions
            join p in _db.Permissions on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId)
            select p.Name).Distinct().ToListAsync();

        foreach (var permission in permissions)
            identity.AddClaim(new Claim("permission", permission));

        return identity;
    }
}
