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
[Authorize(Policy = PermissionNames.UsersManage)]
public class UsersController : Controller
{
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
    private bool IsSuper => User.HasClaim("permission", PermissionNames.TenantsManage);

    private List<string> AssignableRoles() =>
        _roleManager.Roles
            .Select(r => r.Name!)
            .AsEnumerable()
            .Where(r => IsSuper || r != AppConstants.SuperAdminRole)
            .OrderBy(r => r)
            .ToList();

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var tenantNames = await _db.Tenants.ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var query = _userManager.Users.AsQueryable();
        if (!IsSuper) query = query.Where(u => u.TenantId == _currentUser.TenantId);
        var users = await query.OrderBy(u => u.UserName).ToListAsync(ct);

        var items = new List<UserListItem>();
        foreach (var u in users)
        {
            var roles = await _userManager.GetRolesAsync(u);
            items.Add(new UserListItem
            {
                Id = u.Id,
                DisplayName = u.FullName,
                Phone = u.PhoneNumber,
                Email = u.Email,
                IsActive = u.IsActive,
                TenantName = tenantNames.TryGetValue(u.TenantId, out var n) ? n : "—",
                Roles = string.Join("، ", roles)
            });
        }
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new CreateUserViewModel
        {
            CanChooseTenant = IsSuper,
            AllRoles = AssignableRoles(),
            TenantId = _currentUser.TenantId,
            Tenants = await TenantOptionsAsync(ct)
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken ct)
    {
        model.CanChooseTenant = IsSuper;
        model.AllRoles = AssignableRoles();
        model.Tenants = await TenantOptionsAsync(ct);

        if (!IsSuper) model.TenantId = _currentUser.TenantId;

        if (await _userManager.FindByEmailAsync(model.Email) is not null)
            ModelState.AddModelError(nameof(model.Email), "البريد الإلكتروني مستخدم بالفعل.");
        if (await _userManager.Users.AnyAsync(u => u.PhoneNumber == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return View(model);

        var user = new ApplicationUser
        {
            UserName = model.Email,               // email is the internal login handle
            Email = model.Email,
            EmailConfirmed = true,
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

        var roles = model.SelectedRoles.Intersect(AssignableRoles()).ToList();
        if (roles.Count > 0) await _userManager.AddToRolesAsync(user, roles);

        TempData["StatusMessage"] = $"تم إنشاء المستخدم «{user.UserName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
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
            SelectedRoles = (await _userManager.GetRolesAsync(user)).ToList(),
            AllRoles = AssignableRoles(),
            TenantName = tenantName ?? "—"
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditUserViewModel model, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(model.Id.ToString());
        if (user is null || !CanManage(user)) return NotFound();

        model.AllRoles = AssignableRoles();

        // Uniqueness (excluding this user).
        if (await _userManager.Users.AnyAsync(u => u.Id != user.Id && u.Email == model.Email, ct))
            ModelState.AddModelError(nameof(model.Email), "البريد الإلكتروني مستخدم بالفعل.");
        if (await _userManager.Users.AnyAsync(u => u.Id != user.Id && u.PhoneNumber == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return View(model);

        var emailChanged = !string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase);

        user.FullName = model.DisplayName;
        user.PhoneNumber = model.Phone;
        user.IsActive = model.IsActive;
        user.LockoutEnd = model.IsActive ? null : DateTimeOffset.MaxValue;   // block sign-in when disabled
        await _userManager.UpdateAsync(user);

        // Email is the login handle — keep UserName in sync (normalized) when it changes.
        if (emailChanged)
        {
            await _userManager.SetUserNameAsync(user, model.Email);
            await _userManager.SetEmailAsync(user, model.Email);
        }

        var current = await _userManager.GetRolesAsync(user);
        var target = model.SelectedRoles.Intersect(AssignableRoles()).ToList();
        var toAdd = target.Except(current).ToList();
        var toRemove = current.Intersect(AssignableRoles()).Except(target).ToList();
        if (toAdd.Count > 0) await _userManager.AddToRolesAsync(user, toAdd);
        if (toRemove.Count > 0) await _userManager.RemoveFromRolesAsync(user, toRemove);

        TempData["StatusMessage"] = $"تم تحديث المستخدم «{user.UserName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
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
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null || !CanManage(user)) return NotFound();
        return View(new ResetPasswordViewModel { Id = user.Id, UserName = user.UserName ?? "" });
    }

    [HttpPost]
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

    private async Task<List<TenantOption>> TenantOptionsAsync(CancellationToken ct) =>
        await _db.Tenants.OrderBy(t => t.Name)
            .Select(t => new TenantOption(t.Id, t.Name))
            .ToListAsync(ct);
}
