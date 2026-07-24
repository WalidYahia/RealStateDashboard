using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;

namespace RealState.Web.Components;

public class BrandVm
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "RealState";
    public bool HasLogo { get; set; }
    public long Version { get; set; }
}

/// <summary>
/// Renders the current tenant's brand (logo if uploaded, otherwise the tenant name). Resolves the
/// tenant from the current user; falls back to the default tenant when unauthenticated (login page).
/// </summary>
public class BrandViewComponent : ViewComponent
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public BrandViewComponent(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var tenantId = _currentUser.TenantId;

        var brand = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new BrandVm
            {
                Id = t.Id,
                Name = t.Name,
                HasLogo = t.LogoData != null,
                Version = (t.UpdatedAt ?? t.CreatedAt).Ticks
            })
            .FirstOrDefaultAsync();

        return View(brand ?? new BrandVm { Id = tenantId });
    }
}
