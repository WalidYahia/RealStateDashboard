using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RealState.Web.Filters;

/// <summary>
/// The host (Syncro) works inside one tenant at a time. Until it has chosen one, every tenant-data page is closed —
/// otherwise the data would silently go to the fallback tenant (CurrentUserService's DefaultTenantId). Only signing
/// in/out, choosing a tenant (Host) and managing tenants (Admin/Tenants) stay open.
/// </summary>
public sealed class HostTenantFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (!user.IsHost() || user.HasTenant()) return;

        var area = context.RouteData.Values["area"]?.ToString() ?? "";
        var controller = context.RouteData.Values["controller"]?.ToString() ?? "";
        var open = (area.Length == 0 && controller is "Account" or "Host")
                   || (string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase) && controller == "Tenants");
        if (open) return;

        var isAjax = context.HttpContext.Request.Headers.XRequestedWith == "XMLHttpRequest";
        context.Result = isAjax
            ? new JsonResult(new { ok = false, error = "اختر المؤسسة أولًا (تبديل المؤسسة)." }) { StatusCode = StatusCodes.Status409Conflict }
            : new RedirectToActionResult("SelectTenant", "Host", new { area = "" });
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
