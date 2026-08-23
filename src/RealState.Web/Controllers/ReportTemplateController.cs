using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealState.Application.Common;
using RealState.Web.Services.Reports;

namespace RealState.Web.Controllers;

[Authorize]
public class ReportTemplateController : Controller
{
    private readonly IReportTemplateService _svc;

    public ReportTemplateController(IReportTemplateService svc) => _svc = svc;

    // The template lives on the tenant-data (Branding) settings page.
    [HttpPost]
    [Authorize(Policy = PermissionNames.SettingsManage)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile? template, CancellationToken ct)
    {
        var (ok, error) = await _svc.UploadAsync(template, ct);
        if (ok) TempData["StatusMessage"] = "تم رفع قالب الطباعة وتفعيله لكل التقارير.";
        else TempData["ErrorMessage"] = error;
        return RedirectToAction("Branding", "Settings");
    }

    // ---------- Background image used by every print page (tenant-scoped) ----------
    [HttpGet]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
    public async Task<IActionResult> Background(CancellationToken ct)
    {
        var png = await _svc.GetActiveBackgroundAsync(ct);
        return png is null ? NotFound() : File(png, "image/png");
    }
}
