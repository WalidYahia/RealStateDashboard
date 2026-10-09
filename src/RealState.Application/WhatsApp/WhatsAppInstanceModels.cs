using System.Text.Json;
using System.Text.Json.Serialization;

namespace RealState.Application.WhatsApp;

public sealed record WhatsAppInstanceInfo
{
    /// <summary>The id to pass as <c>instanceId</c> everywhere.</summary>
    public string InstanceUniquename { get; init; } = string.Empty;

    public string? InstanceName { get; init; }

    /// <summary>Connection status; see <see cref="WhatsAppConnectionStatus"/>.</summary>
    public string? Status { get; init; }

    public string? SessionStatus { get; init; }
    public string? BotStatus { get; init; }

    /// <summary>Connected number as chat id, e.g. <c>201001234567@c.us</c>.</summary>
    public string? MeId { get; init; }

    public string? MePushName { get; init; }
    public string? SubscriptionStatus { get; init; }
    public bool? IsApi { get; init; }

    /// <summary>An object when a subscription exists, otherwise a descriptive string.</summary>
    public JsonElement? Subscription { get; init; }

    /// <summary>An object when usage exists, otherwise a descriptive string.</summary>
    public JsonElement? Usage { get; init; }

    public string? CanAccessFreeTrial { get; init; }

    public bool IsWorking => WhatsAppConnectionStatus.IsWorking(Status);
}

public sealed record WhatsAppInstanceStatus
{
    /// <summary>See <see cref="WhatsAppConnectionStatus"/>; unknown values may pass through in uppercase.</summary>
    public string? Status { get; init; }

    public string? MeId { get; init; }
    public string? MePushName { get; init; }
    public string? StatusMessage { get; init; }

    public bool IsWorking => WhatsAppConnectionStatus.IsWorking(Status);
}

/// <summary>Known connection statuses. While re-pairing expect STOPPED → STARTING → SCAN_QR_CODE → WORKING.</summary>
public static class WhatsAppConnectionStatus
{
    public const string Stopped = "STOPPED";
    public const string Working = "WORKING";
    public const string Starting = "STARTING";
    public const string ScanQrCode = "SCAN_QR_CODE";
    public const string PasskeyRequired = "PASSKEY_REQUIRED";
    public const string PasskeyConfirmationRequired = "PASSKEY_CONFIRMATION_REQUIRED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";

    public static bool IsWorking(string? status) => string.Equals(status, Working, StringComparison.OrdinalIgnoreCase);
}

public sealed record WhatsAppQueueSettings
{
    public long Id { get; init; }

    /// <summary>Minimum milliseconds between two outbound queue items.</summary>
    public int MinGapMs { get; init; }

    public bool Enabled { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>A contact's LID and its phone chat id.</summary>
public sealed record WhatsAppLidMapping
{
    /// <summary>e.g. <c>123456789@lid</c>.</summary>
    public string? Lid { get; init; }

    /// <summary>Phone chat id, e.g. <c>201001234567@c.us</c>.</summary>
    public string? Pn { get; init; }
}

public sealed record WhatsAppPasskeyChallenge
{
    /// <summary>Base64url challenge.</summary>
    public string? Challenge { get; init; }

    [JsonPropertyName("rpId")]
    public string? RpId { get; init; }

    [JsonPropertyName("userVerification")]
    public string? UserVerification { get; init; }
}

/// <summary>WebAuthn public-key assertion (all binary values base64url-encoded).</summary>
public sealed class WhatsAppPasskeyAssertion
{
    public required string Id { get; init; }
    public required string RawId { get; init; }
    public string Type { get; init; } = "public-key";
    public required string ClientDataJson { get; init; }
    public required string AuthenticatorData { get; init; }
    public required string Signature { get; init; }
    public string? UserHandle { get; init; }
}
