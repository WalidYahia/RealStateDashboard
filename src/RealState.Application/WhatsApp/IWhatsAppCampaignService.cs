namespace RealState.Application.WhatsApp;

/// <summary>
/// Bulk campaigns: create a campaign, add recipients (distributed round-robin across its instances),
/// then start or schedule it. Campaign actions (create/start/schedule/unschedule/pause/reset-failed)
/// are limited by the provider to 10 requests per minute.
/// </summary>
public interface IWhatsAppCampaignService
{
    Task<WhatsAppCampaignList> ListAsync(WhatsAppCampaignState state = WhatsAppCampaignState.All,
        int page = 1, int perPage = 10, CancellationToken cancellationToken = default);

    /// <summary>Creates an empty draft campaign; add recipients with <see cref="AddMessagesAsync"/>.</summary>
    Task<WhatsAppCampaign> CreateAsync(WhatsAppCreateCampaignRequest request, CancellationToken cancellationToken = default);

    Task<WhatsAppCampaignDelay> GetDelayAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Replaces all six pacing values.</summary>
    Task<WhatsAppCampaign> UpdateDelayAsync(long campaignId, WhatsAppCampaignDelay delay, CancellationToken cancellationToken = default);

    Task<WhatsAppPage<WhatsAppCampaignMessage>> ListMessagesAsync(long campaignId,
        WhatsAppCampaignMessagesQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds recipients. Phone numbers already in the campaign (or repeated in the call) are skipped.
    /// Per-row text overrides the campaign default message; vars fill <c>{{var}}</c> / <c>{{var|default}}</c>.
    /// </summary>
    Task<WhatsAppAddCampaignMessagesResult> AddMessagesAsync(long campaignId,
        IEnumerable<WhatsAppCampaignRecipient> recipients, CancellationToken cancellationToken = default);

    /// <summary>Deletes pending queue rows by campaign-message id; returns the number deleted.</summary>
    Task<int> DeletePendingMessagesAsync(long campaignId, IEnumerable<long> messageIds, CancellationToken cancellationToken = default);

    Task<WhatsAppCampaignStats> GetStatsAsync(long campaignId, bool includeFriendly = false, CancellationToken cancellationToken = default);

    Task StartAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Schedules the start; must be in the future. Returns the normalized UTC schedule time.</summary>
    Task<DateTimeOffset?> ScheduleAsync(long campaignId, DateTimeOffset startAt, CancellationToken cancellationToken = default);

    Task UnscheduleAsync(long campaignId, CancellationToken cancellationToken = default);

    Task PauseAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Moves every failed/canceled row back to pending; the campaign becomes paused if any were reset.</summary>
    Task<WhatsAppResetFailedResult> ResetFailedAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Force-finishes the campaign; pending rows move to done as failed.</summary>
    Task<WhatsAppFinishCampaignResult> FinishAsync(long campaignId, CancellationToken cancellationToken = default);

    /// <summary>Copies the campaign (instances + all rows as pending) into a new draft and returns it.</summary>
    Task<WhatsAppCampaign> CopyAsync(long campaignId, CancellationToken cancellationToken = default);
}
