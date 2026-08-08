using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using RealState.Application.Common;

namespace RealState.Web.Security;

/// <summary>
/// Issues the cookie for the static "host" super-user. The host has no database row: its principal is
/// built in-memory with every permission claim, plus a tenant_id once a tenant has been chosen. Because
/// it isn't in the store, security-stamp validation is skipped for it (see Program.cs cookie events).
/// </summary>
public static class HostAuth
{
    public static async Task SignInAsync(HttpContext http, Guid? tenantId, string? tenantName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, AppConstants.HostUserName),
            new("full_name", "Syncro"),
            new(AppConstants.HostClaimType, "true"),
        };

        // Full control across the whole app.
        foreach (var permission in PermissionNames.All)
            claims.Add(new Claim("permission", permission));

        if (tenantId is Guid id)
        {
            claims.Add(new Claim("tenant_id", id.ToString()));
            if (!string.IsNullOrWhiteSpace(tenantName))
                claims.Add(new Claim(AppConstants.TenantNameClaimType, tenantName));
        }

        var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
        await http.SignInAsync(
            IdentityConstants.ApplicationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }

    /// <summary>Matches the configured static host credentials (case-insensitive username).</summary>
    public static bool IsHostLogin(string? userName, string? password) =>
        string.Equals(userName?.Trim(), AppConstants.HostUserName, StringComparison.OrdinalIgnoreCase)
        && password == AppConstants.HostPassword;
}
