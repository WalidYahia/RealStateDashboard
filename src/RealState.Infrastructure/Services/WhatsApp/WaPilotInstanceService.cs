using System.Text.Json.Nodes;
using RealState.Application.WhatsApp;
using static RealState.Infrastructure.Services.WhatsApp.WaPilotApiClient;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary><see cref="IWhatsAppInstanceService"/> over WaPilot API v2.</summary>
internal sealed class WaPilotInstanceService : IWhatsAppInstanceService
{
    private readonly WaPilotApiClient _api;

    public WaPilotInstanceService(WaPilotApiClient api) => _api = api;

    public async Task<IReadOnlyList<WhatsAppInstanceInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        (await _api.GetAsync("instances", cancellationToken)).Prop("instances")?.To<List<WhatsAppInstanceInfo>>()
        ?? new List<WhatsAppInstanceInfo>();

    public async Task<WhatsAppInstanceInfo?> GetAsync(string? instanceId = null, CancellationToken cancellationToken = default)
    {
        var id = _api.ResolveInstanceId(instanceId);
        var root = await _api.GetOrNullAsync($"instances/{Escape(id)}", cancellationToken);
        if (root is null)
            return null;

        // The single-instance payload omits instance_uniquename; fill it so callers can keep using the object.
        var instance = root.Value.Read<WhatsAppInstanceInfo>("instance");
        return string.IsNullOrEmpty(instance.InstanceUniquename) ? instance with { InstanceUniquename = id } : instance;
    }

    public async Task<WhatsAppInstanceStatus> GetStatusAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        (await _api.GetAsync(InstancePath(instanceId, "status"), cancellationToken)).To<WhatsAppInstanceStatus>();

    public async Task<string?> GetQrCodeAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        (await _api.GetOrNullAsync(InstancePath(instanceId, "qr-code"), cancellationToken))?.GetString("qr");

    public Task StartAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        PostAsync(InstancePath(instanceId, "start"), cancellationToken);

    public Task RestartAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        PostAsync(InstancePath(instanceId, "restart"), cancellationToken);

    public Task LogoutAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        PostAsync(InstancePath(instanceId, "logout"), cancellationToken);

    public Task TroubleshootAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        PostAsync(InstancePath(instanceId, "troubleshoot"), cancellationToken);

    public async Task<WhatsAppQueueSettings> GetQueueSettingsAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        (await _api.GetAsync(InstancePath(instanceId, "queue-settings"), cancellationToken)).Read<WhatsAppQueueSettings>();

    public async Task<WhatsAppQueueSettings> UpdateQueueSettingsAsync(int minGapMs, bool? enabled = null,
        string? instanceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minGapMs);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minGapMs, 600_000);

        var body = new JsonObject { ["min_gap_ms"] = minGapMs };
        if (enabled is { } flag)
            body["enabled"] = flag;

        var root = await _api.SendJsonAsync(HttpMethod.Put, InstancePath(instanceId, "queue-settings"), body, cancellationToken);
        return root.Read<WhatsAppQueueSettings>();
    }

    public async Task<WhatsAppLidMapping> ResolveLidAsync(string lid, string? instanceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lid);
        return (await _api.GetAsync(LidPath(instanceId, Escape(lid.Trim())), cancellationToken)).Read<WhatsAppLidMapping>();
    }

    public async Task<WhatsAppLidMapping> ResolvePhoneAsync(string phoneNumber, string? instanceId = null, CancellationToken cancellationToken = default)
    {
        var phone = Escape(_api.NormalizeRecipient(phoneNumber));
        return (await _api.GetAsync(LidPath(instanceId, $"pn/{phone}"), cancellationToken)).Read<WhatsAppLidMapping>();
    }

    public async Task<WhatsAppPasskeyChallenge> GetPasskeyChallengeAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        (await _api.GetAsync(InstancePath(instanceId, "passkey/challenge"), cancellationToken)).Read<WhatsAppPasskeyChallenge>();

    public async Task<string?> SubmitPasskeyAsync(WhatsAppPasskeyAssertion assertion, string? instanceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        var body = new JsonObject
        {
            ["id"] = assertion.Id,
            ["rawId"] = assertion.RawId,
            ["type"] = assertion.Type,
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = assertion.ClientDataJson,
                ["authenticatorData"] = assertion.AuthenticatorData,
                ["signature"] = assertion.Signature,
                ["userHandle"] = assertion.UserHandle,
            },
        };
        var root = await _api.SendJsonAsync(HttpMethod.Post, InstancePath(instanceId, "passkey"), body, cancellationToken);
        return root.GetString("status");
    }

    public async Task<string> GetPasskeyConfirmationCodeAsync(string? instanceId = null, CancellationToken cancellationToken = default)
    {
        var root = await _api.GetAsync(InstancePath(instanceId, "passkey/confirmation"), cancellationToken);
        return root.Prop("data")?.GetString("code")
               ?? throw new WhatsAppApiException("WhatsApp provider returned no confirmation code.");
    }

    public async Task<string?> ConfirmPasskeyAsync(string? instanceId = null, CancellationToken cancellationToken = default) =>
        (await _api.SendJsonAsync(HttpMethod.Post, InstancePath(instanceId, "passkey/confirm"), null, cancellationToken))
        .GetString("status");

    private Task PostAsync(string path, CancellationToken cancellationToken) =>
        _api.SendJsonAsync(HttpMethod.Post, path, null, cancellationToken);

    private string InstancePath(string? instanceId, string action) =>
        $"instances/{Escape(_api.ResolveInstanceId(instanceId))}/{action}";

    /// <summary>
    /// Instance-scoped LID route when an instance is known; otherwise the token-scoped route, which works
    /// only when the token owns exactly one active API instance.
    /// </summary>
    private string LidPath(string? instanceId, string tail)
    {
        var id = !string.IsNullOrWhiteSpace(instanceId) ? instanceId.Trim() : _api.Options.InstanceId?.Trim();
        return string.IsNullOrEmpty(id) ? $"lids/{tail}" : $"{Escape(id)}/lids/{tail}";
    }
}
