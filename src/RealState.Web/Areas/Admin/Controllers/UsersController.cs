using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Identity;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Admin.Models;

namespace RealState.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionNames.UsersView)]
public class UsersController : Controller
{
    private const string PermissionClaimType = "permission";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IApplicationDbContext db,
        ICurrentUserService currentUser)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _currentUser = currentUser;
    }

    // SuperAdmin (holds Tenants.Manage) sees/creates across every tenant; others are scoped to their own.
    private bool IsSuper => User.HasClaim(PermissionClaimType, PermissionNames.TenantsManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var tenantNames = await _db.Tenants.ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var query = _userManager.Users.AsQueryable();
        if (!IsSuper) query = query.Where(u => u.TenantId == _currentUser.TenantId);
        var users = await query.OrderBy(u => u.UserName).ToListAsync(ct);

        var items = new List<UserListItem>();
        foreach (var u in users)
        {
            var isSuperAdmin = await _userManager.IsInRoleAsync(u, AppConstants.SuperAdminRole);
            // Hide SuperAdmin accounts from everyone except a SuperAdmin.
            if (!IsSuper && isSuperAdmin) continue;

            var perms = await EffectivePermissionsAsync(u);
            items.Add(new UserListItem
            {
                Id = u.Id,
                DisplayName = u.FullName,
                Phone = u.PhoneNumber,
                Email = u.Email,
                IsActive = u.IsActive,
                TenantName = tenantNames.TryGetValue(u.TenantId, out var n) ? n : "—",
                IsSuperAdmin = isSuperAdmin,
                PermissionCount = perms.Count,
            });
        }
        return View(items);
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.UsersCreate)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new CreateUserViewModel
        {
            CanChooseTenant = IsSuper,
            TenantId = _currentUser.TenantId,
            Tenants = await TenantOptionsAsync(ct)
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.UsersCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken ct)
    {
        model.CanChooseTenant = IsSuper;
        model.Tenants = await TenantOptionsAsync(ct);

        if (!IsSuper) model.TenantId = _currentUser.TenantId;

        var email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();

        if (email is not null && await _userManager.FindByEmailAsync(email) is not null)
            ModelState.AddModelError(nameof(model.Email), "البريد الإلكتروني مستخدم بالفعل.");
        if (await _userManager.Users.AnyAsync(u => u.PhoneNumber == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = email ?? model.Phone,      // login handle: email when provided, else phone
            Email = email,
            EmailConfirmed = email is not null,
            PhoneNumber = model.Phone,
            PhoneNumberConfirmed = true,
            FullName = model.DisplayName,          // display "user name" (not a login handle)
            TenantId = model.TenantId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        await SetUserPermissionsAsync(user, model.SelectedPermissions);

        TempData["StatusMessage"] = $"تم إنشاء المستخدم «{user.UserName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.UsersEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || !CanManage(user)) return NotFound();

        var tenantName = await _db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
        var model = new EditUserViewModel
        {
            Id = user.Id,
            DisplayName = user.FullName,
            Phone = user.PhoneNumber ?? "",
            Email = user.Email ?? "",
            IsActive = user.IsActive,
            SelectedPermissions = (await EffectivePermissionsAsync(user)).ToList(),
            TenantName = tenantName ?? "—",
            IsSuperAdmin = await _userManager.IsInRoleAsync(user, AppConstants.SuperAdminRole)
        };
        return View(model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.UsersEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(model.Id.ToString());
        if (user is null || !CanManage(user)) return NotFound();

        model.IsSuperAdmin = await _userManager.IsInRoleAsync(user, AppConstants.SuperAdminRole);

        var email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();

        // Uniqueness (excluding this user); email only when provided.
        if (email is not null && await _userManager.Users.AnyAsync(u => u.Id != user.Id && u.Email == email, ct))
            ModelState.AddModelError(nameof(model.Email), "البريد الإلكتروني مستخدم بالفعل.");
        if (await _userManager.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return View(model);

        user.FullName = model.DisplayName;
        user.PhoneNumber = model.Phone;
        user.IsActive = model.IsActive;
        user.LockoutEnd = model.IsActive ? null : DateTimeOffset.MaxValue;   // block sign-in when disabled
        await _userManager.UpdateAsync(user);

        // Login handle = email when provided, else phone. Keep UserName + email in sync.
        var desiredUserName = email ?? model.Phone;
        if (!string.Equals(user.UserName, desiredUserName, StringComparison.OrdinalIgnoreCase))
            await _userManager.SetUserNameAsync(user, desiredUserName);
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            await _userManager.SetEmailAsync(user, email);

        // SuperAdmin holds everything via its role; leave its privileges untouched.
        if (!model.IsSuperAdmin)
            await SetUserPermissionsAsync(user, model.SelectedPermissions);

        TempData["StatusMessage"] = $"تم تحديث المستخدم «{user.UserName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.UsersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(Guid id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || !CanManage(user)) return NotFound();

        user.IsActive = !user.IsActive;
        user.LockoutEnd = user.IsActive ? null : DateTimeOffset.MaxValue;
        await _userManager.UpdateAsync(user);
        TempData["StatusMessage"] = user.IsActive ? "تم تفعيل الحساب." : "تم تعطيل الحساب.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.UsersEdit)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || !CanManage(user)) return NotFound();
        return View(new ResetPasswordViewModel { Id = user.Id, UserName = user.UserName ?? "" });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.UsersEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(model.Id.ToString());
        if (user is null || !CanManage(user)) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError(string.Empty, e.Description);
            return View(model);
        }

        TempData["StatusMessage"] = $"تم إعادة تعيين كلمة مرور «{user.UserName}».";
        return RedirectToAction(nameof(Index));
    }

    private bool CanManage(ApplicationUser user) => IsSuper || user.TenantId == _currentUser.TenantId;

    // ---------- permissions ----------

    /// <summary>
    /// Replaces the user's direct permission claims with exactly the requested set (intersected with
    /// the known catalog) and strips any leftover roles, so the checkbox grid is the single source of
    /// truth. SuperAdmin users are handled separately and never routed here.
    /// </summary>
    private async Task SetUserPermissionsAsync(ApplicationUser user, IEnumerable<string> wanted)
    {
        var wantedSet = wanted.Intersect(PermissionNames.All).ToHashSet();

        var current = (await _userManager.GetClaimsAsync(user))
            .Where(c => c.Type == PermissionClaimType).ToList();
        var currentVals = current.Select(c => c.Value).ToHashSet();

        var toRemove = current.Where(c => !wantedSet.Contains(c.Value)).ToList();
        var toAdd = wantedSet.Where(v => !currentVals.Contains(v))
            .Select(v => new Claim(PermissionClaimType, v)).ToList();

        if (toRemove.Count > 0) await _userManager.RemoveClaimsAsync(user, toRemove);
        if (toAdd.Count > 0) await _userManager.AddClaimsAsync(user, toAdd);

        // Regular users derive their access from claims only — remove any legacy role memberships.
        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Count > 0) await _userManager.RemoveFromRolesAsync(user, roles);
    }

    /// <summary>Direct permission claims plus any permissions inherited from the user's roles.</summary>
    private async Task<HashSet<string>> EffectivePermissionsAsync(ApplicationUser user)
    {
        var direct = (await _userManager.GetClaimsAsync(user))
            .Where(c => c.Type == PermissionClaimType)
            .Select(c => c.Value);

        var result = new HashSet<string>(direct);

        var roleNames = await _userManager.GetRolesAsync(user);
        if (roleNames.Count > 0)
        {
            var roleIds = new List<Guid>();
            foreach (var rn in roleNames)
            {
                var role = await _roleManager.FindByNameAsync(rn);
                if (role is not null) roleIds.Add(role.Id);
            }

            var rolePerms = await (
                from rp in _db.RolePermissions
                join p in _db.Permissions on rp.PermissionId equals p.Id
                where roleIds.Contains(rp.RoleId)
                select p.Name).ToListAsync();

            foreach (var p in rolePerms) result.Add(p);
        }

        return result;
    }

    private async Task<List<TenantOption>> TenantOptionsAsync(CancellationToken ct) =>
        await _db.Tenants.OrderBy(t => t.Name)
            .Select(t => new TenantOption(t.Id, t.Name))
            .ToListAsync(ct);
}
