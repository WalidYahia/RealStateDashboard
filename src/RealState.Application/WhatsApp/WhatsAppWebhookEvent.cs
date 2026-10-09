using System.Text.Json;
using System.Text.Json.Serialization;

namespace RealState.Application.WhatsApp;

/// <summary>
/// Callback posted by the provider to the webhook URL configured in the WaPilot app. Respond with any
/// 2xx status to acknowledge. The provider does not sign webhooks, so protect the endpoint yourself
/// (e.g. a secret path segment or query value).
/// </summary>
public sealed record WhatsAppWebhookEvent
{
    public const string MessageEvent = "message";
    public const string MessageAnyEvent = "message.any";

    /// <summary><see cref="MessageEvent"/> or <see cref="MessageAnyEvent"/>.</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("instance_id")]
    public string? InstanceId { get; init; }

    /// <summary>Message payload of a <c>message</c> event (id, chat_id, text, …).</summary>
    [JsonPropertyName("message")]
    public JsonElement? Message { get; init; }

    /// <summary>Raw payload of a <c>message.any</c> event.</summary>
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    /// <summary>Reads a string field from <see cref="Message"/> (or <see cref="Payload"/>), e.g. <c>"chat_id"</c>.</summary>
    public string? GetString(string property)
    {
        foreach (var source in new[] { Message, Payload })
        {
            if (source is { ValueKind: JsonValueKind.Object } obj && obj.TryGetProperty(property, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        return null;
    }

    public static WhatsAppWebhookEvent? Parse(string json) => JsonSerializer.Deserialize<WhatsAppWebhookEvent>(json);
}
