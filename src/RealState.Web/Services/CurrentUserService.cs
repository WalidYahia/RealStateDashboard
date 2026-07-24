using System.Security.Claims;
using RealState.Application.Common;
using RealState.Application.Interfaces;

namespace RealState.Web.Services;

/// <summary>
/// Resolves the current user and tenant from the HTTP context. When there is no authenticated user
/// (background seeding, login pages) it falls back to the default tenant so query filters still work.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? UserName => User?.Identity?.Name;

    public Guid TenantId
    {
        get
        {
            var value = User?.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var id) ? id : AppConstants.DefaultTenantId;
        }
    }
}
