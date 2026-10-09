using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using RealState.Application.WhatsApp;
using RealState.Infrastructure.Services.WhatsApp;
using RealState.Web.Filters;

namespace RealState.Web.Areas.Admin.Controllers;

/// <summary>
/// Diagnostics page for the WhatsApp (WaPilot) integration: connection status, QR pairing, test sends and message
/// tracking. Host (Syncro) only — it uses the platform's WhatsApp account, not tenant data, so it stays open before a
/// tenant is chosen (see <see cref="HostTenantFilter"/>). Test sends aren't business actions, so they aren't audited.
/// </summary>
[Area("Admin")]
[Authorize]
[SkipActivityLog]
public class WhatsAppTestController : Controller
{
    private const long MaxUploadBytes = 50L * 1024 * 1024;

    private readonly IWhatsAppService _whatsApp;
    private readonly IWhatsAppInstanceService _instances;
    private readonly WhatsAppOptions _options;

    public WhatsAppTestController(IWhatsAppService whatsApp, IWhatsAppInstanceService instances, IOptions<WhatsAppOptions> options)
    {
        _whatsApp = whatsApp;
        _instances = instances;
        _options = options.Value;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!User.IsHost())
            context.Result = Forbid();
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["IsConfigured"] = _whatsApp.IsConfigured;
        return View(_options);
    }

    [HttpGet]
    public Task<IActionResult> Status(CancellationToken ct) =>
        Run(async () => new
        {
            status = await _instances.GetStatusAsync(cancellationToken: ct),
            instance = await _instances.GetAsync(cancellationToken: ct),
        });

    [HttpGet]
    public Task<IActionResult> Instances(CancellationToken ct) =>
        Run(async () => await _instances.ListAsync(ct));

    [HttpGet]
    public Task<IActionResult> QrCode(CancellationToken ct) =>
        Run(async () => new { qr = await _instances.GetQrCodeAsync(cancellationToken: ct) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Restart(CancellationToken ct) =>
        Run(async () => { await _instances.RestartAsync(cancellationToken: ct); return new { done = true }; });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SendText(string phone, string text, CancellationToken ct) =>
        Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("أدخل رقم الهاتف ونص الرسالة.");
            return await _whatsApp.SendTextAsync(phone, text, cancellationToken: ct);
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxUploadBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes + 1024 * 1024)]
    public Task<IActionResult> SendMedia(string phone, string kind, string? caption, IFormFile? file, CancellationToken ct) =>
        Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(phone) || file is null || file.Length == 0)
                throw new ArgumentException("أدخل رقم الهاتف واختر ملفًا.");
            if (file.Length > MaxUploadBytes)
                throw new ArgumentException("الحد الأقصى لحجم الملف في صفحة الاختبار 50 ميجابايت.");

            await using var stream = file.OpenReadStream();
            var media = WhatsAppMedia.FromStream(stream, file.FileName, file.ContentType);
            return kind switch
            {
                "image" => await _whatsApp.SendImageAsync(phone, media, caption, cancellationToken: ct),
                "video" => await _whatsApp.SendVideoAsync(phone, media, caption, cancellationToken: ct),
                _ => await _whatsApp.SendFileAsync(phone, media, caption, cancellationToken: ct),
            };
        });

    [HttpGet]
    public Task<IActionResult> Message(string id, CancellationToken ct) =>
        Run(async () => await _whatsApp.GetMessageAsync(id, cancellationToken: ct)
                        ?? throw new ArgumentException("الرسالة غير موجودة."));

    [HttpGet]
    public Task<IActionResult> Messages(bool done, CancellationToken ct) =>
        Run(async () => done
            ? await _whatsApp.ListDoneMessagesAsync(new WhatsAppDoneMessagesQuery { PerPage = 20 }, cancellationToken: ct)
            : await _whatsApp.ListPendingMessagesAsync(1, 20, cancellationToken: ct));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Retry(string id, CancellationToken ct) =>
        Run(async () => { await _whatsApp.RetryMessageAsync(id, cancellationToken: ct); return new { done = true }; });

    /// <summary>Runs a provider call and returns <c>{ ok, data }</c> or <c>{ ok:false, error, code, httpStatus, errors }</c>.</summary>
    private async Task<IActionResult> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return Json(new { ok = true, data = await call() });
        }
        catch (WhatsAppApiException ex)
        {
            return Json(new { ok = false, error = ex.Message, code = ex.ErrorCode, httpStatus = (int?)ex.StatusCode, errors = ex.Errors });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }
}
