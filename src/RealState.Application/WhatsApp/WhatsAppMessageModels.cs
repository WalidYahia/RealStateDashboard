using System.Text.Json;

namespace RealState.Application.WhatsApp;

/// <summary>Result of queueing a message. HTTP success means "accepted into the queue", not "delivered".</summary>
public sealed record WhatsAppSendResult
{
    /// <summary>UUID used to track the message.</summary>
    public string MessageId { get; init; } = string.Empty;

    /// <summary>Uploaded media id (media sends only).</summary>
    public string? MediaId { get; init; }

    public string? IdempotencyKey { get; init; }

    /// <summary>True when the idempotency key matched an earlier send and nothing new was queued.</summary>
    public bool IdempotentReplay { get; init; }

    public string? Message { get; init; }
}

/// <summary>A direct message as stored in the provider's pending or done queue.</summary>
public sealed record WhatsAppMessage
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Instance unique name the message belongs to.</summary>
    public string? InstanceUn { get; init; }

    public string? ChatId { get; init; }

    /// <summary>text, image, file, video, list, buttons, template…</summary>
    public string? Type { get; init; }

    public string? Text { get; init; }
    public string? Caption { get; init; }
    public string? ReplyTo { get; init; }
    public int? Priority { get; init; }
    public DateTimeOffset? SendAt { get; init; }

    /// <summary>Lifecycle status; see <see cref="WhatsAppMessageStatus"/>. New values may appear — handle unknown ones.</summary>
    public string? Status { get; init; }

    public int? Attempts { get; init; }
    public string? ProviderMsgId { get; init; }
    public string? IdempotencyKey { get; init; }

    // Present on completed (done) messages.
    public DateTimeOffset? DeliveredAt { get; init; }
    public int? Ack { get; init; }
    public long? LatencyMs { get; init; }

    /// <summary>For Official messages <c>ACCOUNT_NOT_REGISTERED</c> means the sender connection must be repaired.</summary>
    public string? ErrorCode { get; init; }

    public string? ErrorType { get; init; }

    public JsonElement? Interactive { get; init; }
    public JsonElement? TemplatePayload { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }

    public bool IsPending => WhatsAppMessageStatus.IsPending(Status);
    public bool IsSucceeded => WhatsAppMessageStatus.IsSuccess(Status);
    public bool IsFailed => WhatsAppMessageStatus.IsFailure(Status);
}

/// <summary>Message / campaign-row status values and their grouping as documented by the provider.</summary>
public static class WhatsAppMessageStatus
{
    public const string Pending = "pending";
    public const string Success = "success";
    public const string Accepted = "accepted";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string Canceled = "canceled";

    /// <summary>Misspelled legacy failure status found on older records.</summary>
    public const string LegacyFailed = "faild";

    public static bool IsPending(string? status) => Is(status, Pending);

    public static bool IsSuccess(string? status) =>
        Is(status, Success) || Is(status, Accepted) || Is(status, Sent) || Is(status, Delivered) || Is(status, Read);

    public static bool IsFailure(string? status) =>
        Is(status, Failed) || Is(status, Canceled) || Is(status, LegacyFailed);

    private static bool Is(string? status, string value) => string.Equals(status, value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Filters for completed messages.</summary>
public sealed class WhatsAppDoneMessagesQuery
{
    /// <summary>Exact chat id (normalized like send recipients).</summary>
    public string? ChatId { get; init; }

    /// <summary>Inclusive from the start of this day.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Inclusive to the end of this day.</summary>
    public DateOnly? To { get; init; }

    public int Page { get; init; } = 1;

    /// <summary>1 – 100.</summary>
    public int PerPage { get; init; } = 20;
}

public sealed record WhatsAppRetryAllResult
{
    public int FoundCount { get; init; }
    public int ProcessedCount { get; init; }
    public string? Message { get; init; }
}

/// <summary>Interactive list message.</summary>
public sealed class WhatsAppList
{
    /// <summary>≤ 60 chars.</summary>
    public required string Title { get; init; }

    /// <summary>Label of the button that opens the list, ≤ 20 chars.</summary>
    public required string Button { get; init; }

    /// <summary>1 – 10 sections.</summary>
    public required IReadOnlyList<WhatsAppListSection> Sections { get; init; }

    /// <summary>≤ 1024 chars.</summary>
    public string? Description { get; init; }

    /// <summary>≤ 60 chars.</summary>
    public string? Footer { get; init; }
}

/// <param name="Rows">1 – 50 rows.</param>
/// <param name="Title">Optional, ≤ 60 chars.</param>
public sealed record WhatsAppListSection(IReadOnlyList<WhatsAppListRow> Rows, string? Title = null);

/// <param name="RowId">Case-sensitive identifier returned when the row is picked, ≤ 200 chars.</param>
/// <param name="Title">≤ 60 chars.</param>
/// <param name="Description">Optional, ≤ 1024 chars.</param>
public sealed record WhatsAppListRow(string RowId, string Title, string? Description = null);

/// <summary>Reply-button message (Official instances).</summary>
public sealed class WhatsAppButtons
{
    /// <summary>≤ 1024 chars.</summary>
    public required string Body { get; init; }

    /// <summary>1 – 3 buttons.</summary>
    public required IReadOnlyList<WhatsAppButton> Buttons { get; init; }

    /// <summary>≤ 60 chars.</summary>
    public string? Footer { get; init; }
}

public sealed record WhatsAppButton(string Id, string Title);

/// <summary>Approved Meta template (Official instances).</summary>
public sealed class WhatsAppTemplate
{
    public required string Name { get; init; }

    /// <summary>Exact approved language code, e.g. <c>en_US</c> or <c>ar</c>.</summary>
    public required string LanguageCode { get; init; }

    /// <summary>
    /// Meta template components serialized as-is (e.g. a <c>JsonArray</c> or anonymous objects such as
    /// <c>new[] { new { type = "body", parameters = new[] { new { type = "text", text = "Ahmed" } } } }</c>).
    /// </summary>
    public object? Components { get; init; }
}

public sealed class WhatsAppTemplateQuery
{
    /// <summary>False (default) returns approved templates only.</summary>
    public bool IncludeAll { get; init; }

    public string? Status { get; init; }

    /// <summary>MARKETING, UTILITY or AUTHENTICATION.</summary>
    public string? Category { get; init; }

    /// <summary>Exact language code, e.g. en_US.</summary>
    public string? Language { get; init; }

    /// <summary>Searches template names.</summary>
    public string? Search { get; init; }

    /// <summary>Cursor from <see cref="WhatsAppTemplatePage.NextCursor"/>.</summary>
    public string? After { get; init; }

    /// <summary>1 – 100.</summary>
    public int? Limit { get; init; }
}

public sealed class WhatsAppTemplatePage
{
    public IReadOnlyList<WhatsAppTemplateInfo> Items { get; init; } = Array.Empty<WhatsAppTemplateInfo>();

    /// <summary>Pass to <see cref="WhatsAppTemplateQuery.After"/> for the next page; null when there is none.</summary>
    public string? NextCursor { get; init; }
}

public sealed record WhatsAppTemplateInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Language { get; init; }
    public string? Status { get; init; }
    public string? Category { get; init; }
    public JsonElement? Components { get; init; }
}
