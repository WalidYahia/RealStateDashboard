using System.Globalization;
using System.Text.Json.Nodes;
using RealState.Application.WhatsApp;
using static RealState.Infrastructure.Services.WhatsApp.WaPilotApiClient;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary><see cref="IWhatsAppCampaignService"/> over WaPilot API v2 (<c>/campaigns</c>; token header only).</summary>
internal sealed class WaPilotCampaignService : IWhatsAppCampaignService
{
    private readonly WaPilotApiClient _api;

    public WaPilotCampaignService(WaPilotApiClient api) => _api = api;

    public async Task<WhatsAppCampaignList> ListAsync(WhatsAppCampaignState state = WhatsAppCampaignState.All,
        int page = 1, int perPage = 10, CancellationToken cancellationToken = default)
    {
        var path = WithQuery("campaigns", ("state", Lower(state)), ("page", page), ("per_page", perPage));
        var root = await _api.GetAsync(path, cancellationToken);
        return new WhatsAppCampaignList
        {
            Items = root.Prop("data")?.To<List<WhatsAppCampaign>>() ?? new List<WhatsAppCampaign>(),
            HasMore = root.Prop("links")?.Prop("next") is not null,
            Counts = root.Prop("meta")?.Prop("counts")?.To<WhatsAppCampaignCounts>(),
        };
    }

    public async Task<WhatsAppCampaign> CreateAsync(WhatsAppCreateCampaignRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instanceIds = request.InstanceIds.Count > 0
            ? request.InstanceIds.Select(id => _api.ResolveInstanceId(id)).ToList()
            : new List<string> { _api.ResolveInstanceId(null) };

        var fields = instanceIds.Select(id => new KeyValuePair<string, string?>("instance_uns[]", id)).ToList();
        fields.Add(new("default_message", request.DefaultMessage));
        fields.Add(new("media_type", request.MediaType is { } mediaType ? Lower(mediaType) : null));
        if (request.Delay is { } delay)
        {
            fields.Add(new("wait_between_messages_from", Invariant(delay.WaitBetweenMessagesFrom)));
            fields.Add(new("wait_between_messages_to", Invariant(delay.WaitBetweenMessagesTo)));
            fields.Add(new("sleep_after_from", Invariant(delay.SleepAfterFrom)));
            fields.Add(new("sleep_after_to", Invariant(delay.SleepAfterTo)));
            fields.Add(new("sleep_time_from", Invariant(delay.SleepTimeFrom)));
            fields.Add(new("sleep_time_to", Invariant(delay.SleepTimeTo)));
        }

        var root = await _api.SendFormAsync(HttpMethod.Post, "campaigns", fields, "media", request.Media, cancellationToken);
        return root.Read<WhatsAppCampaign>();
    }

    public async Task<WhatsAppCampaignDelay> GetDelayAsync(long campaignId, CancellationToken cancellationToken = default) =>
        (await _api.GetAsync(CampaignPath(campaignId, "delay"), cancellationToken)).Read<WhatsAppCampaignDelay>();

    public async Task<WhatsAppCampaign> UpdateDelayAsync(long campaignId, WhatsAppCampaignDelay delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delay);
        var body = new JsonObject
        {
            ["wait_between_messages_from"] = delay.WaitBetweenMessagesFrom,
            ["wait_between_messages_to"] = delay.WaitBetweenMessagesTo,
            ["sleep_after_from"] = delay.SleepAfterFrom,
            ["sleep_after_to"] = delay.SleepAfterTo,
            ["sleep_time_from"] = delay.SleepTimeFrom,
            ["sleep_time_to"] = delay.SleepTimeTo,
        };
        var root = await _api.SendJsonAsync(HttpMethod.Patch, CampaignPath(campaignId, "delay"), body, cancellationToken);
        return root.Read<WhatsAppCampaign>();
    }

    public async Task<WhatsAppPage<WhatsAppCampaignMessage>> ListMessagesAsync(long campaignId,
        WhatsAppCampaignMessagesQuery? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new WhatsAppCampaignMessagesQuery();
        var path = WithQuery(CampaignPath(campaignId, "messages"),
            ("source", Lower(query.Source)),
            ("status", Lower(query.Status)),
            ("sort_by", query.SortByCreatedAt ? "created_at" : "id"),
            ("sort_dir", query.Descending ? "desc" : "asc"),
            ("page", query.Page),
            ("per_page", query.PerPage));
        return (await _api.GetAsync(path, cancellationToken)).ToPage<WhatsAppCampaignMessage>();
    }

    public async Task<WhatsAppAddCampaignMessagesResult> AddMessagesAsync(long campaignId,
        IEnumerable<WhatsAppCampaignRecipient> recipients, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        var messages = new JsonArray();
        foreach (var recipient in recipients)
        {
            var row = new JsonObject { ["phone_number"] = _api.NormalizeRecipient(recipient.PhoneNumber) };
            if (!string.IsNullOrWhiteSpace(recipient.Text))
                row["text"] = recipient.Text;
            if (recipient.Vars is { Count: > 0 } vars)
            {
                var varsNode = new JsonObject();
                foreach (var (name, value) in vars)
                    varsNode[name] = value;
                row["vars"] = varsNode;
            }
            messages.Add(row);
        }
        if (messages.Count == 0)
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));

        var root = await _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "messages"),
            new JsonObject { ["messages"] = messages }, cancellationToken);
        return root.Read<WhatsAppAddCampaignMessagesResult>();
    }

    public async Task<int> DeletePendingMessagesAsync(long campaignId, IEnumerable<long> messageIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageIds);
        var ids = new JsonArray();
        foreach (var id in messageIds.Distinct())
            ids.Add(id);
        if (ids.Count == 0)
            throw new ArgumentException("At least one message id is required.", nameof(messageIds));

        var root = await _api.SendJsonAsync(HttpMethod.Delete, CampaignPath(campaignId, "messages"),
            new JsonObject { ["ids"] = ids }, cancellationToken);
        return root.Prop("data")?.GetInt("deleted") ?? 0;
    }

    public async Task<WhatsAppCampaignStats> GetStatsAsync(long campaignId, bool includeFriendly = false,
        CancellationToken cancellationToken = default)
    {
        var path = WithQuery(CampaignPath(campaignId, "messages/stats"), ("include_friendly", includeFriendly));
        return (await _api.GetAsync(path, cancellationToken)).Read<WhatsAppCampaignStats>();
    }

    public Task StartAsync(long campaignId, CancellationToken cancellationToken = default) =>
        _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "start"), null, cancellationToken);

    public async Task<DateTimeOffset?> ScheduleAsync(long campaignId, DateTimeOffset startAt, CancellationToken cancellationToken = default)
    {
        var root = await _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "schedule"),
            new JsonObject { ["schedule_date"] = FormatUtc(startAt) }, cancellationToken);
        return DateTimeOffset.TryParse(root.Prop("data")?.GetString("schedule_time_utc"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var scheduled) ? scheduled : null;
    }

    public Task UnscheduleAsync(long campaignId, CancellationToken cancellationToken = default) =>
        _api.SendJsonAsync(HttpMethod.Delete, CampaignPath(campaignId, "schedule"), null, cancellationToken);

    public Task PauseAsync(long campaignId, CancellationToken cancellationToken = default) =>
        _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "pause"), null, cancellationToken);

    public async Task<WhatsAppResetFailedResult> ResetFailedAsync(long campaignId, CancellationToken cancellationToken = default) =>
        (await _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "reset-failed"), null, cancellationToken))
        .Read<WhatsAppResetFailedResult>();

    public async Task<WhatsAppFinishCampaignResult> FinishAsync(long campaignId, CancellationToken cancellationToken = default) =>
        (await _api.SendJsonAsync(HttpMethod.Patch, CampaignPath(campaignId, "finish"), null, cancellationToken))
        .Read<WhatsAppFinishCampaignResult>();

    public async Task<WhatsAppCampaign> CopyAsync(long campaignId, CancellationToken cancellationToken = default) =>
        (await _api.SendJsonAsync(HttpMethod.Post, CampaignPath(campaignId, "copy"), null, cancellationToken))
        .Read<WhatsAppCampaign>();

    private static string CampaignPath(long campaignId, string action) =>
        $"campaigns/{campaignId.ToString(CultureInfo.InvariantCulture)}/{action}";

    private static string Lower<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
