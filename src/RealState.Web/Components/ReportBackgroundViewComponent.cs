using Microsoft.AspNetCore.Mvc;
using RealState.Web.Services.Reports;

namespace RealState.Web.Components;

public class ReportBackgroundVm
{
    public bool Show { get; set; }
    public string Part { get; set; } = "open";   // "open" (before content) or "close" (after content)
}

/// <summary>
/// Wraps a printed report in the tenant's letterhead: a full-page background image plus a table frame
/// whose repeating thead/tfoot reserve the header/footer safe-area on every page. Invoked twice per
/// report — part="open" right after &lt;body&gt;, part="close" right before &lt;/body&gt;. Renders
/// nothing when the tenant has no active template.
/// </summary>
public class ReportBackgroundViewComponent : ViewComponent
{
    private readonly IReportTemplateService _svc;

    public ReportBackgroundViewComponent(IReportTemplateService svc) => _svc = svc;

    public async Task<IViewComponentResult> InvokeAsync(string part = "open")
    {
        return View(new ReportBackgroundVm
        {
            Show = await _svc.HasActiveAsync(),
            Part = part == "close" ? "close" : "open"
        });
    }
}
