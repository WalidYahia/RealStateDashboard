using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Identity;
using RealState.Infrastructure.Persistence;
using RealState.Web.Areas.Admin.Models;

namespace RealState.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionNames.TenantsManage)]
public class TenantsController : Controller
{
    private static readonly string[] AllowedLogoTypes = { "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml" };
    private const long MaxLogoBytes = 2 * 1024 * 1024; // 2 MB

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TenantsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userCounts = await _userManager.Users
            .GroupBy(u => u.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var counts = userCounts.ToDictionary(x => x.Key, x => x.Count);

        // Exclude any soft-deleted tenants (Tenant is auditable but not tenant-filtered).
        var tenants = await _db.Tenants
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .Select(t => new TenantListItem
            {
                Id = t.Id,
                Name = t.Name,
                IsActive = t.IsActive,
                HasLogo = t.LogoData != null,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(ct);

        foreach (var t in tenants)
            t.UserCount = counts.TryGetValue(t.Id, out var c) ? c : 0;

        return View(tenants);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateTenantViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTenantViewModel model, IFormFile? logo, CancellationToken ct)
    {
        var (logoData, logoType) = await ReadLogoAsync(logo, ct);

        if (await _userManager.FindByEmailAsync(model.AdminEmail) is not null)
            ModelState.AddModelError(nameof(model.AdminEmail), "البريد الإلكتروني مستخدم بالفعل.");
        if (await _userManager.Users.AnyAsync(u => u.PhoneNumber == model.AdminPhone, ct))
            ModelState.AddModelError(nameof(model.AdminPhone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return View(model);

        // Atomic: the tenant and its admin are created together, or not at all — no orphan tenant rows.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var tenant = new Tenant
            {
                Name = model.Name,
                IsActive = true,
                LogoData = logoData,
                LogoContentType = logoType
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct);

            var admin = new ApplicationUser
            {
                UserName = model.AdminEmail,          // email is the internal login handle
                Email = model.AdminEmail,
                EmailConfirmed = true,
                PhoneNumber = model.AdminPhone,
                PhoneNumberConfirmed = true,
                FullName = model.AdminDisplayName,     // display "user name"
                TenantId = tenant.Id,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(admin, model.AdminPassword);
            if (!result.Succeeded)
            {
                await tx.RollbackAsync(ct);
                foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return View(model);
            }

            await _userManager.AddToRoleAsync(admin, AppConstants.TenantAdminRole);
            await tx.CommitAsync(ct);

            TempData["StatusMessage"] = $"تم إنشاء المؤسسة «{tenant.Name}» ومديرها.";
            return RedirectToAction(nameof(Index));
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, ct);
        if (tenant is null) return NotFound();

        return View(new EditTenantViewModel
        {
            Id = tenant.Id,
            Name = tenant.Name,
            IsActive = tenant.IsActive,
            HasLogo = tenant.LogoData != null
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditTenantViewModel model, IFormFile? logo, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == model.Id && !t.IsDeleted, ct);
        if (tenant is null) return NotFound();

        var (logoData, logoType) = await ReadLogoAsync(logo, ct);
        if (!ModelState.IsValid)
        {
            model.HasLogo = tenant.LogoData != null;
            return View(model);
        }

        tenant.Name = model.Name;
        tenant.IsActive = model.IsActive;
        if (logoData is not null)
        {
            tenant.LogoData = logoData;
            tenant.LogoContentType = logoType;
        }
        else if (model.RemoveLogo)
        {
            tenant.LogoData = null;
            tenant.LogoContentType = null;
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تحديث المؤسسة «{tenant.Name}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (id == AppConstants.DefaultTenantId)
        {
            TempData["StatusMessage"] = "لا يمكن حذف المؤسسة الافتراضية.";
            return RedirectToAction(nameof(Index));
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tenant is null) return NotFound();

        // Hard-delete the tenant and everything scoped to it. ExecuteDelete bypasses the soft-delete
        // interceptor; IgnoreQueryFilters is required so we hit the target tenant's rows, not the caller's.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            await _db.SalesInvoices.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.PurchaseInvoices.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Incomes.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Expenses.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Tasks.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Notifications.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Attachments.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Settings.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Leads.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Customers.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);
            await _db.Projects.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteDeleteAsync(ct);

            // Users (cascades to their roles/claims/logins/tokens via the Identity schema).
            await _db.Users.Where(u => u.TenantId == id).ExecuteDeleteAsync(ct);

            await _db.Tenants.Where(t => t.Id == id).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
            TempData["StatusMessage"] = $"تم حذف المؤسسة «{tenant.Name}» وكل بياناتها ومستخدميها.";
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<(byte[]? data, string? contentType)> ReadLogoAsync(IFormFile? logo, CancellationToken ct)
    {
        if (logo is not { Length: > 0 }) return (null, null);

        if (logo.Length > MaxLogoBytes)
        {
            ModelState.AddModelError("logo", "حجم الشعار يجب ألا يتجاوز 2 ميجابايت.");
            return (null, null);
        }
        if (!AllowedLogoTypes.Contains(logo.ContentType))
        {
            ModelState.AddModelError("logo", "صيغة الشعار غير مدعومة (PNG, JPG, GIF, WEBP, SVG).");
            return (null, null);
        }

        using var ms = new MemoryStream();
        await logo.CopyToAsync(ms, ct);
        return (ms.ToArray(), logo.ContentType);
    }
}
