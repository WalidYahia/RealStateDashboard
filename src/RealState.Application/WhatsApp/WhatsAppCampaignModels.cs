namespace RealState.Application.WhatsApp;

/// <summary>Campaign list filter. <see cref="Active"/> means currently sending; <see cref="Ended"/> means finished.</summary>
public enum WhatsAppCampaignState
{
    All,
    Active,
    Paused,
    Scheduled,
    Draft,
    Ended,
}

public enum WhatsAppCampaignMediaType
{
    Image,
    Video,
    Document,
    Audio,
}

/// <summary>Which rows to list: still queued, already processed, or both.</summary>
public enum WhatsAppCampaignMessageSource
{
    All,
    Queue,
    Done,
}

/// <summary>Success groups success/accepted/sent/delivered/read; Failed groups failed/canceled.</summary>
public enum WhatsAppCampaignMessageStatusFilter
{
    All,
    Pending,
    Success,
    Failed,
}

/// <summary>Campaign statuses: draft, sending, paused, scheduled, finished.</summary>
public sealed record WhatsAppCampaign
{
    public long Id { get; init; }

    /// <summary>Default message template (supports <c>{{var}}</c> and <c>{{var|default}}</c>).</summary>
    public string? Message { get; init; }

    public string? Status { get; init; }
    public string? CurrentSendingStatus { get; init; }
    public DateTimeOffset? ScheduleTime { get; init; }
    public DateTimeOffset? SleepEnd { get; init; }
    public int? WaitBetweenMessagesFrom { get; init; }
    public int? WaitBetweenMessagesTo { get; init; }
    public int? SleepAfterFrom { get; init; }
    public int? SleepAfterTo { get; init; }
    public int? SleepTimeFrom { get; init; }
    public int? SleepTimeTo { get; init; }
    public int? SendFriendlyEvery { get; init; }
    public int? SendFriendlyCount { get; init; }
    public bool IsHasMedia { get; init; }
    public string? MediaId { get; init; }
    public string? MediaType { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public IReadOnlyList<WhatsAppCampaignInstance> Instances { get; init; } = Array.Empty<WhatsAppCampaignInstance>();
    public int QueueCount { get; init; }
    public int DoneCount { get; init; }
    public int PendingCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
}

public sealed record WhatsAppCampaignInstance
{
    public long Id { get; init; }
    public string? UniqueName { get; init; }
    public string? Name { get; init; }
}

public sealed class WhatsAppCampaignList
{
    public IReadOnlyList<WhatsAppCampaign> Items { get; init; } = Array.Empty<WhatsAppCampaign>();
    public bool HasMore { get; init; }

    /// <summary>Totals per state across all of the user's campaigns.</summary>
    public WhatsAppCampaignCounts? Counts { get; init; }
}

public sealed record WhatsAppCampaignCounts
{
    public int All { get; init; }
    public int Active { get; init; }
    public int Paused { get; init; }
    public int Scheduled { get; init; }
    public int Draft { get; init; }
    public int Ended { get; init; }
}

/// <summary>
/// Pacing in seconds: wait a random <c>WaitBetweenMessages</c> between messages; after a random
/// <c>SleepAfter</c> messages sleep a random <c>SleepTime</c>. Each "To" must be ≥ its "From".
/// </summary>
public sealed record WhatsAppCampaignDelay
{
    /// <summary>≥ 0.</summary>
    public int WaitBetweenMessagesFrom { get; init; }
    public int WaitBetweenMessagesTo { get; init; }

    /// <summary>≥ 1.</summary>
    public int SleepAfterFrom { get; init; }
    public int SleepAfterTo { get; init; }

    /// <summary>≥ 1.</summary>
    public int SleepTimeFrom { get; init; }
    public int SleepTimeTo { get; init; }
}

public sealed class WhatsAppCreateCampaignRequest
{
    /// <summary>Instances to send from (round-robin). Empty uses the configured default instance.</summary>
    public IReadOnlyList<string> InstanceIds { get; init; } = Array.Empty<string>();

    /// <summary>Default text; supports <c>{{var}}</c> and <c>{{var|default}}</c> placeholders.</summary>
    public string? DefaultMessage { get; init; }

    /// <summary>Optional media sent with every normal row (≤ 200 MiB).</summary>
    public WhatsAppMedia? Media { get; init; }

    public WhatsAppCampaignMediaType? MediaType { get; init; }

    /// <summary>Null copies pacing from the first instance's settings.</summary>
    public WhatsAppCampaignDelay? Delay { get; init; }
}

/// <param name="PhoneNumber">Recipient (normalized like direct sends), ≤ 30 chars.</param>
/// <param name="Text">Overrides the campaign default message, ≤ 5000 chars.</param>
/// <param name="Vars">Values for the template placeholders.</param>
public sealed record WhatsAppCampaignRecipient(string PhoneNumber, string? Text = null,
    IReadOnlyDictionary<string, string>? Vars = null);

public sealed record WhatsAppAddCampaignMessagesResult
{
    public int InsertedCount { get; init; }
    public IReadOnlyList<string> SkippedDuplicates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InstancesUsed { get; init; } = Array.Empty<string>();
}

public sealed record WhatsAppCampaignMessage
{
    public long Id { get; init; }
    public long CampaignId { get; init; }
    public string? InstanceUn { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Text { get; init; }

    /// <summary>See <see cref="WhatsAppMessageStatus"/>.</summary>
    public string? Status { get; init; }

    /// <summary>True for automatically inserted "friendly" filler messages.</summary>
    public bool IsFriendly { get; init; }

    public bool IsHasMedia { get; init; }
    public string? MediaType { get; init; }

    /// <summary>queue or done.</summary>
    public string? Source { get; init; }

    public DateTimeOffset? SentAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed class WhatsAppCampaignMessagesQuery
{
    public WhatsAppCampaignMessageSource Source { get; init; } = WhatsAppCampaignMessageSource.All;
    public WhatsAppCampaignMessageStatusFilter Status { get; init; } = WhatsAppCampaignMessageStatusFilter.All;

    /// <summary>False sorts by id, true by created_at.</summary>
    public bool SortByCreatedAt { get; init; }

    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;

    /// <summary>1 – 100.</summary>
    public int PerPage { get; init; } = 20;
}

public sealed record WhatsAppCampaignStats
{
    public int TotalMessages { get; init; }
    public int MessagesInQueue { get; init; }
    public int MessagesInDone { get; init; }
    public int MessagesPending { get; init; }
    public int DoneCount { get; init; }
    public int MessagesSuccess { get; init; }
    public int MessagesFailed { get; init; }
    public decimal PercentageDone { get; init; }
    public decimal MessageUnitPercent { get; init; }
}

public sealed record WhatsAppResetFailedResult
{
    public int ResetCount { get; init; }
    public string? CampaignStatus { get; init; }
}

public sealed record WhatsAppFinishCampaignResult
{
    public int MovedToDone { get; init; }
    public WhatsAppCampaign? Campaign { get; init; }
}
