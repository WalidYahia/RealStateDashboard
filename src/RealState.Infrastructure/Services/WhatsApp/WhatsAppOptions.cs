using RealState.Application.WhatsApp;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary>
/// Settings bound from the <c>WhatsApp</c> configuration section. Keep <see cref="ApiToken"/> out of
/// source control: use user-secrets in development and the <c>WhatsApp__ApiToken</c> environment
/// variable (or the host's secret store) in production.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>WaPilot API v2 root.</summary>
    public string BaseUrl { get; set; } = "https://api.wapilot.net/api/v2/";

    /// <summary>API token from the WaPilot dashboard (sent in the <c>token</c> header).</summary>
    public string? ApiToken { get; set; }

    /// <summary>Default instance's <c>instance_uniquename</c> (see GET /instances).</summary>
    public string? InstanceId { get; set; }

    /// <summary>Whether the default instance is a QR or an Official (Meta) instance.</summary>
    public WhatsAppInstanceKind InstanceKind { get; set; } = WhatsAppInstanceKind.Qr;

    /// <summary>Prefixed to local numbers starting with 0 (Egypt: 20, so 010… → 2010…). Empty disables it.</summary>
    public string? DefaultCountryCode { get; set; } = "20";

    /// <summary>Normalize recipients to international digits before sending.</summary>
    public bool NormalizePhoneNumbers { get; set; } = true;

    /// <summary>Per-attempt HTTP timeout. Large uploads (files up to 150 MiB) need a generous value.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Retries for HTTP 429 (honouring Retry-After) and — for reads and idempotency-keyed sends only —
    /// transient network/5xx failures.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Upper bound for a single retry wait.</summary>
    public int MaxRetryDelaySeconds { get; set; } = 60;
}
