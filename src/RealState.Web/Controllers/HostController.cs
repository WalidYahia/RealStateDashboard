using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;
using RealState.Web.Security;

namespace RealState.Web.Controllers;

/// <summary>
/// Tenant selection for the static host super-user. After the host signs in it must choose a tenant to
/// operate within (all business data is tenant-scoped). If no tenants exist yet, it is sent to the
/// tenants screen to create the first one.
/// </summary>
[Authorize]
public class HostController : Controller
{
    private readonly IApplicationDbContext _db;

    public HostController(IApplicationDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> SelectTenant(CancellationToken ct)
    {
        if (!User.IsHost()) return Forbid();

        var tenants = await _db.Tenants
            .Where(t => !t.IsDeleted && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new SelectListItem { Value = t.Id.ToString(), Text = t.Name })
            .ToListAsync(ct);

        // No tenants yet -> go create the first one; the flow restarts after that.
        if (tenants.Count == 0)
            return RedirectToAction("Index", "Tenants", new { area = "Admin" });

        return View(tenants);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SelectTenant(Guid tenantId, CancellationToken ct)
    {
        if (!User.IsHost()) return Forbid();

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == tenantId && !t.IsDeleted && t.IsActive, ct);
        if (tenant is null)
        {
            TempData["StatusMessage"] = "اختر مؤسسة صالحة.";
            return RedirectToAction(nameof(SelectTenant));
        }

        // Re-issue the host cookie carrying the chosen tenant, then open the app on it.
        await HostAuth.SignInAsync(HttpContext, tenant.Id, tenant.Name);
        return RedirectToAction("Index", "Dashboard", new { area = "" });
    }
}
