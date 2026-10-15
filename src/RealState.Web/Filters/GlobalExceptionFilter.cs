using Microsoft.AspNetCore.Http.Extensions;
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
        // The browser dropped the request (navigated away / re-submitted): nobody is waiting for an answer — not an
        // error, so no log entry and no «حدث خطأ» message on the user's next page.
        if (context.Exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            context.Result = new EmptyResult();
            context.ExceptionHandled = true;
            return;
        }

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
        var current = http.Request.GetEncodedPathAndQuery();

        // Return the user to where they were (same host only), else the dashboard — but never to the page that just
        // failed, and not again when the page we sent them to fails too (its message is still unread): a page that
        // fails on every load would otherwise bounce the browser in a redirect loop.
        string? target = null;
        if (tempData.Peek("ErrorMessage") as string != Message)
        {
            var referer = http.Request.Headers["Referer"].ToString();
            if (!string.IsNullOrEmpty(referer)
                && Uri.TryCreate(referer, UriKind.Absolute, out var u)
                && string.Equals(u.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(u.PathAndQuery, current, StringComparison.OrdinalIgnoreCase))
                target = referer;
            else if (http.Request.Path != "/")
                target = "/";
        }

        if (target is null)
        {
            // Nowhere safe to go back to: a plain page (no layout — it may be what's failing) instead of a redirect.
            tempData.Remove("ErrorMessage");
            tempData.Save();
            context.Result = new ContentResult { StatusCode = StatusCodes.Status500InternalServerError, ContentType = "text/html; charset=utf-8", Content = FallbackPage(current) };
            context.ExceptionHandled = true;
            return;
        }

        tempData["ErrorMessage"] = Message;
        context.Result = new RedirectResult(target);
        context.ExceptionHandled = true;
    }

    private static string FallbackPage(string current)
    {
        var retry = System.Net.WebUtility.HtmlEncode(current);
        return "<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
             + "<title>خطأ</title></head><body style=\"margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;"
             + "background:#121212;color:#e8e6df;font-family:system-ui,'Segoe UI',Tahoma,sans-serif;\">"
             + "<div style=\"max-width:420px;margin:16px;padding:28px;border:1px solid #333;border-radius:14px;background:#1a1a19;text-align:center;\">"
             + "<div style=\"font-size:40px;\">⚠️</div><h1 style=\"font-size:20px;margin:8px 0 10px;\">حدث خطأ أثناء تنفيذ العملية</h1>"
             + "<p style=\"color:#a9a79e;margin:0 0 18px;\">تعذّر عرض هذه الصفحة. أعد المحاولة بعد قليل، وإن تكرر الخطأ أبلغ مسؤول النظام.</p>"
             + $"<a href=\"{retry}\" style=\"display:inline-block;margin:4px;padding:9px 18px;border-radius:10px;background:#3987e5;color:#fff;text-decoration:none;\">إعادة المحاولة</a>"
             + "<a href=\"/\" style=\"display:inline-block;margin:4px;padding:9px 18px;border-radius:10px;border:1px solid #444;color:#e8e6df;text-decoration:none;\">الصفحة الرئيسية</a>"
             + "</div></body></html>";
    }
}
