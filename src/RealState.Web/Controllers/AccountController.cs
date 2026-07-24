using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Identity;
using RealState.Web.Models;

namespace RealState.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        // Users sign in with their email or phone number (the display "user name" is not a login handle).
        var user = await ResolveUserAsync(model.UserName.Trim());
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
            return RedirectToLocal(model.ReturnUrl);

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "الحساب معطّل أو مقفل مؤقتًا. تواصل مع المسؤول.");
        else
            ModelState.AddModelError(string.Empty, "بيانات الدخول غير صحيحة.");

        return View(model);
    }

    /// <summary>Finds a user by email or phone number (with a username fallback for the seeded admin).</summary>
    private async Task<ApplicationUser?> ResolveUserAsync(string identifier)
    {
        ApplicationUser? user = null;
        if (identifier.Contains('@'))
            user = await _userManager.FindByEmailAsync(identifier);
        user ??= await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == identifier);
        user ??= await _userManager.FindByNameAsync(identifier);
        return user;
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (result.Succeeded)
        {
            await _signInManager.RefreshSignInAsync(user);
            TempData["StatusMessage"] = "تم تغيير كلمة المرور بنجاح.";
            return RedirectToAction(nameof(ChangePassword));
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToLocal(string? returnUrl)
        => Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Dashboard");
}
