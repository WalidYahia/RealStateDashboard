using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RealState.Web.Models;

namespace RealState.Web.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Site root ("/") and the post-login landing: redirects to the tenant's default/startup page
    /// (Settings → الصفحة الافتتاحية), or a page the user can open when they lack access to it.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Index([FromServices] RealState.Web.Services.IStartupPageService startup, CancellationToken ct)
    {
        var page = await startup.ResolveAsync(User, ct);
        return RedirectToAction(page.Action, page.Controller, new { area = page.Area ?? "" });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
