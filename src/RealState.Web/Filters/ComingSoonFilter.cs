using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RealState.Web.Navigation;

namespace RealState.Web.Filters;

/// <summary>
/// Keeps «قريبًا» pages (<see cref="NavPage.Soon"/>) closed for everyone, host included: the page itself — or every
/// action of a controller whose catalog pages are all «قريبًا» (details, forms, prints, posts) — answers with the
/// coming-soon page (JSON { ok:false, error } for AJAX / modals). Removing the catalog flag publishes the page.
/// </summary>
public sealed class ComingSoonFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var values = context.RouteData.Values;
        var page = NavigationCatalog.SoonPageFor(values["area"]?.ToString(), values["controller"]?.ToString(), values["action"]?.ToString());
        if (page is null) return;

        var http = context.HttpContext;
        http.Items[ActivityLogFilter.SkipItem] = true;
        var message = $"«{page.Title}» قيد الإعداد وستتوفر قريبًا.";
        if (http.Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            context.Result = new JsonResult(new { ok = false, error = message });
            return;
        }

        // Back to the closest open place: the page's group card (skipping «قريبًا» groups), else its module workspace.
        var url = http.RequestServices.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(context);
        var group = NavigationCatalog.Groups.FirstOrDefault(g => g.Key == page.Group);
        while (group is { Soon: true }) group = NavigationCatalog.Groups.FirstOrDefault(g => g.Key == group.Parent);
        var module = NavigationCatalog.Modules.First(m => m.Key == page.Module);
        context.Result = new ViewResult
        {
            ViewName = "~/Views/Shared/ComingSoon.cshtml",
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState)
            {
                ["Title"] = page.Title,
                ["Message"] = message,
                ["BackText"] = group?.Title ?? module.Title,
                ["BackUrl"] = group is not null
                    ? url.Action("Group", "Workspace", new { area = "", id = group.Key })
                    : url.Action("Module", "Workspace", new { area = "", id = module.Key }),
            },
        };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
