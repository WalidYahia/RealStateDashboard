namespace RealState.Application.WhatsApp;

/// <summary>
/// Sends WhatsApp messages and tracks them. Sends are asynchronous on the provider side: a successful
/// call means the message was <b>queued</b>, not delivered — keep <see cref="WhatsAppSendResult.MessageId"/>
/// and poll <see cref="GetMessageAsync"/> (or use webhooks) for the final status.
/// </summary>
/// <remarks>
/// Recipients may be given as local numbers (<c>01001234567</c>), international numbers
/// (<c>+201001234567</c>, <c>00201001234567</c>) or chat ids (<c>201001234567@c.us</c>); they are
/// normalized using the configured default country code. Every method throws
/// <see cref="WhatsAppApiException"/> when the provider rejects the request.
/// </remarks>
public interface IWhatsAppService
{
    /// <summary>True when an API token and default instance are configured.</summary>
    bool IsConfigured { get; }

    Task<WhatsAppSendResult> SendTextAsync(string to, string text,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Image: jpg/jpeg/png/webp/gif, up to 10 MiB on QR instances.</summary>
    Task<WhatsAppSendResult> SendImageAsync(string to, WhatsAppMedia image, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Document/file (pdf, xlsx, zip…), up to 150 MiB on QR instances.</summary>
    Task<WhatsAppSendResult> SendFileAsync(string to, WhatsAppMedia file, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Video: mp4/mov/avi/mkv/webm/3gp, up to 50 MiB on QR instances.</summary>
    Task<WhatsAppSendResult> SendVideoAsync(string to, WhatsAppMedia video, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    Task<WhatsAppSendResult> SendListAsync(string to, WhatsAppList list,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reply buttons (1–3). Official instances only.</summary>
    Task<WhatsAppSendResult> SendButtonsAsync(string to, WhatsAppButtons buttons,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Approved Meta template. Official instances only.</summary>
    Task<WhatsAppSendResult> SendTemplateAsync(string to, WhatsAppTemplate template,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// One-time password through an approved Meta AUTHENTICATION template (body + copy-code button are
    /// built by the provider). <paramref name="otp"/> is 1–15 ASCII letters/digits. Official instances only.
    /// </summary>
    Task<WhatsAppSendResult> SendOtpAsync(string to, string otp, string templateName, string languageCode,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Finds a message in the pending or completed queue; null when it does not exist.</summary>
    Task<WhatsAppMessage?> GetMessageAsync(string messageId,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default);

    Task<WhatsAppPage<WhatsAppMessage>> ListPendingMessagesAsync(int page = 1, int perPage = 20,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default);

    /// <summary>Processed messages — both successful and failed; inspect <see cref="WhatsAppMessage.Status"/>.</summary>
    Task<WhatsAppPage<WhatsAppMessage>> ListDoneMessagesAsync(WhatsAppDoneMessagesQuery? query = null,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default);

    /// <summary>Moves one failed message back to pending. The message id is preserved.</summary>
    Task RetryMessageAsync(string messageId,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-queues failed messages created since <paramref name="since"/> (all when null). With
    /// <paramref name="latestPerChatOnly"/> only the latest failed message per chat is retried.
    /// Returns zero counts when nothing failed.
    /// </summary>
    Task<WhatsAppRetryAllResult> RetryAllFailedAsync(DateTimeOffset? since = null, bool latestPerChatOnly = false,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default);

    /// <summary>Templates of the connected Official account (approved only by default).</summary>
    Task<WhatsAppTemplatePage> ListTemplatesAsync(WhatsAppTemplateQuery? query = null,
        string? instanceId = null, CancellationToken cancellationToken = default);
}
