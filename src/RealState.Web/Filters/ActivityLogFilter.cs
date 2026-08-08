using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RealState.Application.Activity;

namespace RealState.Web.Filters;

/// <summary>
/// Records every successful, state-changing (POST) action by an authenticated user. Logins are logged
/// explicitly by the account flow instead (the user isn't authenticated yet during that request).
/// Logging failures never affect the request.
/// </summary>
public sealed class ActivityLogFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        var http = context.HttpContext;
        var req = http.Request;

        if (!HttpMethods.IsPost(req.Method)) return;             // only state changes
        if (executed.Exception != null) return;                  // action failed — not a real "action"
        if (http.User?.Identity?.IsAuthenticated != true) return;

        var rd = context.RouteData.Values;
        var controller = rd.TryGetValue("controller", out var c) ? c?.ToString() ?? "" : "";
        var action = rd.TryGetValue("action", out var a) ? a?.ToString() ?? "" : "";
        var area = rd.TryGetValue("area", out var ar) ? ar?.ToString() : null;

        var actionType = ActivityActionType.Classify(action);
        // The shared modal "Form" action does both create and edit — tell them apart by the Id field.
        if (actionType == ActivityActionType.Update
            && action.Equals("Form", StringComparison.OrdinalIgnoreCase)
            && req.HasFormContentType)
        {
            var id = req.Form.TryGetValue("Id", out var v) ? v.ToString() : null;
            if (string.IsNullOrEmpty(id) || id == Guid.Empty.ToString())
                actionType = ActivityActionType.Create;
        }

        // Prefer the action's own status message (e.g. "تم حذف الخزنة «...»") as the description;
        // otherwise fall back to a generic "verb + entity" description. Peek so the success banner
        // still shows on the next page.
        var tempData = http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);
        var status = tempData.Peek("StatusMessage") as string;
        var description = !string.IsNullOrWhiteSpace(status)
            ? status!
            : ActivityActionType.Describe(actionType, controller);

        try
        {
            var logger = http.RequestServices.GetRequiredService<IActivityLogger>();
            await logger.LogAsync(new ActivityEntry(
                ActionType: actionType,
                Controller: controller,
                Action: action,
                Method: req.Method,
                Area: area,
                Path: req.Path.Value,
                Description: description,
                IpAddress: http.Connection.RemoteIpAddress?.ToString()));
        }
        catch
        {
            // Never let audit logging break the user's request.
        }
    }
}
