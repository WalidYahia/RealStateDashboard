using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;

namespace RealState.Web.Controllers;

/// <summary>Serves tenant logos. Anonymous so the login page can show the default tenant's brand.</summary>
[AllowAnonymous]
public class BrandingController : Controller
{
    private readonly IApplicationDbContext _db;

    public BrandingController(IApplicationDbContext db) => _db = db;

    [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Logo(Guid id, CancellationToken ct)
    {
        var logo = await _db.Tenants
            .Where(t => t.Id == id)
            .Select(t => new { t.LogoData, t.LogoContentType })
            .FirstOrDefaultAsync(ct);

        if (logo?.LogoData is null || logo.LogoData.Length == 0)
            return NotFound();

        return File(logo.LogoData, logo.LogoContentType ?? "image/png");
    }
}
