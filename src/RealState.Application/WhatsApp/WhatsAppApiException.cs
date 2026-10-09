using System.Net;

namespace RealState.Application.WhatsApp;

/// <summary>Raised when the WhatsApp provider rejects a request or cannot be reached.</summary>
public sealed class WhatsAppApiException : Exception
{
    public WhatsAppApiException(string message, HttpStatusCode? statusCode = null, string? errorCode = null,
        IReadOnlyDictionary<string, string[]>? errors = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Errors = errors ?? new Dictionary<string, string[]>();
        RetryAfter = retryAfter;
    }

    /// <summary>HTTP status, or null when the provider could not be reached.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Provider error code such as <c>TOKEN_INVALID</c> or <c>INSTANCE_NOT_READY</c> (see <see cref="WhatsAppErrorCodes"/>).</summary>
    public string? ErrorCode { get; }

    /// <summary>Field validation errors keyed by request field (HTTP 422).</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Provider's Retry-After hint on HTTP 429.</summary>
    public TimeSpan? RetryAfter { get; }

    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
}

/// <summary>Error codes documented by the provider.</summary>
public static class WhatsAppErrorCodes
{
    public const string TokenInvalid = "TOKEN_INVALID";
    public const string InstanceForbidden = "INSTANCE_FORBIDDEN";
    public const string InstanceNotReady = "INSTANCE_NOT_READY";
    public const string ValidationError = "VALIDATION_ERROR";
    public const string RecipientUnsupported = "RECIPIENT_UNSUPPORTED";
    public const string MessageTypeUnsupported = "MESSAGE_TYPE_UNSUPPORTED";
    public const string IdempotencyKeyConflict = "IDEMPOTENCY_KEY_CONFLICT";
    public const string MessagingServiceUnavailable = "MESSAGING_SERVICE_UNAVAILABLE";
    public const string PasskeyRequired = "PASSKEY_REQUIRED";
    public const string PasskeyConfirmationRequired = "PASSKEY_CONFIRMATION_REQUIRED";
    public const string PasskeyNotPending = "PASSKEY_NOT_PENDING";
    public const string PasskeyAssertionInvalid = "PASSKEY_ASSERTION_INVALID";
    public const string AccountNotRegistered = "ACCOUNT_NOT_REGISTERED";
    public const string CampaignNoInstances = "CAMPAIGN_NO_INSTANCES";
    public const string CampaignFinished = "CAMPAIGN_FINISHED";
    public const string CampaignNotScheduled = "CAMPAIGN_NOT_SCHEDULED";
    public const string CampaignTrialSchedulingNotAllowed = "CAMPAIGN_TRIAL_SCHEDULING_NOT_ALLOWED";
}
