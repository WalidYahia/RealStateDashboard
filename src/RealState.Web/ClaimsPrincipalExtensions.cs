using System.Security.Claims;
using RealState.Application.Common;

namespace RealState.Web;

public static class ClaimsPrincipalExtensions
{
    /// <summary>True when the user holds the given permission claim (used to show/hide UI actions).</summary>
    public static bool Can(this ClaimsPrincipal? user, string permission) =>
        user?.HasClaim("permission", permission) ?? false;

    /// <summary>True for the static "host" super-user (not a database account).</summary>
    public static bool IsHost(this ClaimsPrincipal? user) =>
        user?.HasClaim(AppConstants.HostClaimType, "true") ?? false;

    /// <summary>True once a tenant has been selected (a valid tenant_id claim is present).</summary>
    public static bool HasTenant(this ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirst("tenant_id")?.Value, out _);

    /// <summary>The selected tenant's display name, if the tenant_name claim is set.</summary>
    public static string? TenantName(this ClaimsPrincipal? user) =>
        user?.FindFirst(AppConstants.TenantNameClaimType)?.Value;
}
