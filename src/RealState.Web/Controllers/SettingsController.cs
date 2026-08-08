using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Interfaces;
using RealState.Infrastructure.Persistence;
using RealState.Web.Models;

namespace RealState.Web.Controllers;

[Authorize(Policy = PermissionNames.SettingsManage)]
public class SettingsController : Controller
{
    private static readonly string[] AllowedLogoTypes = { "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml" };
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SettingsController(ApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Branding(CancellationToken ct)
    {
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == _currentUser.TenantId, ct);
        if (t is null) return NotFound();
        return View(new BrandingSettingsModel { Name = t.Name, HasLogo = t.LogoData != null });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Branding(BrandingSettingsModel model, IFormFile? logo, CancellationToken ct)
    {
        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == _currentUser.TenantId, ct);
        if (t is null) return NotFound();

        if (logo is { Length: > 0 })
        {
            if (logo.Length > MaxLogoBytes) ModelState.AddModelError("logo", "حجم الشعار يجب ألا يتجاوز 2 ميجابايت.");
            else if (!AllowedLogoTypes.Contains(logo.ContentType)) ModelState.AddModelError("logo", "صيغة الشعار غير مدعومة.");
        }

        if (!ModelState.IsValid) { model.HasLogo = t.LogoData != null; return View(model); }

        t.Name = model.Name;
        if (logo is { Length: > 0 })
        {
            using var ms = new MemoryStream();
            await logo.CopyToAsync(ms, ct);
            t.LogoData = ms.ToArray();
            t.LogoContentType = logo.ContentType;
        }
        else if (model.RemoveLogo)
        {
            t.LogoData = null;
            t.LogoContentType = null;
        }
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = "تم تحديث بيانات المؤسسة.";
        return RedirectToAction(nameof(Branding));
    }
}
