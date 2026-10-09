namespace RealState.Application.WhatsApp;

/// <summary>
/// Manages WhatsApp instances (connected numbers): discovery, connection state, pairing and pacing.
/// Every <c>instanceId</c> is the provider's <c>instance_uniquename</c>; when omitted the configured
/// default instance is used.
/// </summary>
public interface IWhatsAppInstanceService
{
    /// <summary>All instances owned by the API token (not paginated).</summary>
    Task<IReadOnlyList<WhatsAppInstanceInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Instance details; null when the instance does not exist.</summary>
    Task<WhatsAppInstanceInfo?> GetAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Current connection status (see <see cref="WhatsAppConnectionStatus"/>).</summary>
    Task<WhatsAppInstanceStatus> GetStatusAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// QR code as a <c>data:image/png;base64,…</c> URI, or null when no QR is available (already connected
    /// or stopped). Throws <see cref="WhatsAppApiException"/> with code <c>PASSKEY_REQUIRED</c> /
    /// <c>PASSKEY_CONFIRMATION_REQUIRED</c> when extra verification is needed instead.
    /// </summary>
    Task<string?> GetQrCodeAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    Task StartAsync(string? instanceId = null, CancellationToken cancellationToken = default);
    Task RestartAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Logs the number out. Poll <see cref="GetStatusAsync"/> before re-pairing.</summary>
    Task LogoutAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Recreates/repairs the messaging session.</summary>
    Task TroubleshootAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Minimum delay between outbound queue items. Creates defaults (2000 ms, enabled) when none exist.</summary>
    Task<WhatsAppQueueSettings> GetQueueSettingsAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <param name="minGapMs">0 – 600000 milliseconds.</param>
    /// <param name="enabled">Null keeps the current value.</param>
    Task<WhatsAppQueueSettings> UpdateQueueSettingsAsync(int minGapMs, bool? enabled = null,
        string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Resolves a contact LID (<c>123456789@lid</c>) to its phone/chat id.</summary>
    Task<WhatsAppLidMapping> ResolveLidAsync(string lid, string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Resolves a phone number / chat id to its contact LID.</summary>
    Task<WhatsAppLidMapping> ResolvePhoneAsync(string phoneNumber, string? instanceId = null, CancellationToken cancellationToken = default);

    // Connection verification (status PASSKEY_REQUIRED / PASSKEY_CONFIRMATION_REQUIRED).
    // Limited by the provider to 15 requests per minute per token.

    Task<WhatsAppPasskeyChallenge> GetPasskeyChallengeAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Submits a WebAuthn assertion; returns the provider status (normally <c>PROCESSING</c>).</summary>
    Task<string?> SubmitPasskeyAsync(WhatsAppPasskeyAssertion assertion, string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>The four-digit code to confirm on the phone.</summary>
    Task<string> GetPasskeyConfirmationCodeAsync(string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>Confirms the pending four-digit code; returns the provider status (normally <c>PROCESSING</c>).</summary>
    Task<string?> ConfirmPasskeyAsync(string? instanceId = null, CancellationToken cancellationToken = default);
}
