using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealState.Application.Dashboards;

namespace RealState.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await _dashboard.GetExecutiveDashboardAsync(cancellationToken);
        return View(model);
    }
}
