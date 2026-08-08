using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace RealState.Web.Filters;

/// <summary>
/// Central "try/catch" for every controller action. Logs the exception and surfaces a friendly
/// message instead of a raw error page: AJAX/modal requests get a JSON { ok:false, error } that the
/// shared script shows as a SweetAlert; normal navigations get a TempData message + redirect back,
/// which the layout renders as a SweetAlert on the next page.
/// </summary>
public sealed class GlobalExceptionFilter : IExceptionFilter
{
    private const string Message = "حدث خطأ أثناء تنفيذ العملية. يُرجى المحاولة مرة أخرى.";

    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, ITempDataDictionaryFactory tempDataFactory)
    {
        _logger = logger;
        _tempDataFactory = tempDataFactory;
    }

    public void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;
        _logger.LogError(context.Exception, "Unhandled exception in {Method} {Path}", http.Request.Method, http.Request.Path);

        var isAjax = string.Equals(http.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (isAjax)
        {
            // 200 so the modal script reads the body; { ok:false } drives the SweetAlert.
            context.Result = new JsonResult(new { ok = false, error = Message });
            context.ExceptionHandled = true;
            return;
        }

        var tempData = _tempDataFactory.GetTempData(http);
        tempData["ErrorMessage"] = Message;

        // Return the user to where they were (same host only), else the dashboard.
        var referer = http.Request.Headers["Referer"].ToString();
        var target = !string.IsNullOrEmpty(referer)
                     && Uri.TryCreate(referer, UriKind.Absolute, out var u)
                     && string.Equals(u.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            ? referer
            : "/";

        context.Result = new RedirectResult(target);
        context.ExceptionHandled = true;
    }
}
