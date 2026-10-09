using System.Text.Json;
using System.Text.Json.Nodes;
using RealState.Application.WhatsApp;
using static RealState.Infrastructure.Services.WhatsApp.WaPilotApiClient;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary>
/// <see cref="IWhatsAppService"/> over WaPilot API v2. QR instances use <c>/{instance}/…</c>, Official
/// instances <c>/official/{instance}/…</c>; buttons, templates and OTP exist on the Official API only.
/// </summary>
internal sealed class WaPilotWhatsAppService : IWhatsAppService
{
    private const int MaxIdempotencyKeyLength = 255;

    private readonly WaPilotApiClient _api;

    public WaPilotWhatsAppService(WaPilotApiClient api) => _api = api;

    public bool IsConfigured => _api.IsConfigured;

    public Task<WhatsAppSendResult> SendTextAsync(string to, string text,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var body = CreateBody(to, options);
        body["text"] = text;
        return SendJsonAsync(MessagePath(options, "send-message"), body, options, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendImageAsync(string to, WhatsAppMedia image, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default) =>
        SendMediaAsync("send-image", to, image, caption, options, cancellationToken);

    public Task<WhatsAppSendResult> SendFileAsync(string to, WhatsAppMedia file, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default) =>
        SendMediaAsync("send-file", to, file, caption, options, cancellationToken);

    public Task<WhatsAppSendResult> SendVideoAsync(string to, WhatsAppMedia video, string? caption = null,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default) =>
        SendMediaAsync("send-video", to, video, caption, options, cancellationToken);

    public Task<WhatsAppSendResult> SendListAsync(string to, WhatsAppList list,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        var sections = new JsonArray();
        foreach (var section in list.Sections)
        {
            var rows = new JsonArray();
            foreach (var row in section.Rows)
            {
                var rowNode = new JsonObject { ["title"] = row.Title, ["rowId"] = row.RowId };
                SetIfNotNull(rowNode, "description", row.Description);
                rows.Add(rowNode);
            }
            var sectionNode = new JsonObject { ["rows"] = rows };
            SetIfNotNull(sectionNode, "title", section.Title);
            sections.Add(sectionNode);
        }

        var interactive = new JsonObject { ["title"] = list.Title, ["button"] = list.Button, ["sections"] = sections };
        SetIfNotNull(interactive, "description", list.Description);
        SetIfNotNull(interactive, "footer", list.Footer);

        var body = CreateBody(to, options);
        body["interactive"] = interactive;
        return SendJsonAsync(MessagePath(options, "send-list"), body, options, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendButtonsAsync(string to, WhatsAppButtons buttons,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        var buttonNodes = new JsonArray();
        foreach (var button in buttons.Buttons)
            buttonNodes.Add(new JsonObject { ["id"] = button.Id, ["title"] = button.Title });

        var interactive = new JsonObject { ["body"] = buttons.Body, ["buttons"] = buttonNodes };
        SetIfNotNull(interactive, "footer", buttons.Footer);

        var body = CreateBody(to, options);
        body["interactive"] = interactive;
        return SendJsonAsync(OfficialPath(options, "send-buttons"), body, options, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendTemplateAsync(string to, WhatsAppTemplate template,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        var templateNode = TemplateNode(template.Name, template.LanguageCode);
        if (template.Components is not null)
            templateNode["components"] = JsonSerializer.SerializeToNode(template.Components);

        var body = CreateBody(to, options);
        body["template"] = templateNode;
        return SendJsonAsync(OfficialPath(options, "send-template"), body, options, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendOtpAsync(string to, string otp, string templateName, string languageCode,
        WhatsAppSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(otp);
        var body = CreateBody(to, options);
        body["otp"] = otp;
        body["template"] = TemplateNode(templateName, languageCode);
        return SendJsonAsync(OfficialPath(options, "send-otp"), body, options, cancellationToken);
    }

    public async Task<WhatsAppMessage?> GetMessageAsync(string messageId,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var root = await _api.GetOrNullAsync(MessagePath(instance, $"messages/{Escape(messageId)}"), cancellationToken);
        return root?.Read<WhatsAppMessage>();
    }

    public async Task<WhatsAppPage<WhatsAppMessage>> ListPendingMessagesAsync(int page = 1, int perPage = 20,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default)
    {
        var path = WithQuery(MessagePath(instance, "messages"), ("page", page), ("per_page", perPage));
        return (await _api.GetAsync(path, cancellationToken)).ToPage<WhatsAppMessage>();
    }

    public async Task<WhatsAppPage<WhatsAppMessage>> ListDoneMessagesAsync(WhatsAppDoneMessagesQuery? query = null,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default)
    {
        query ??= new WhatsAppDoneMessagesQuery();
        var path = WithQuery(MessagePath(instance, "messages/done"),
            ("chat_id", query.ChatId?.Trim()),
            ("start_date", query.From?.ToString("yyyy-MM-dd")),
            ("end_date", query.To?.ToString("yyyy-MM-dd")),
            ("page", query.Page),
            ("per_page", query.PerPage));
        return (await _api.GetAsync(path, cancellationToken)).ToPage<WhatsAppMessage>();
    }

    public Task RetryMessageAsync(string messageId,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        return _api.SendJsonAsync(HttpMethod.Post, MessagePath(instance, $"messages/{Escape(messageId)}/retry"), null, cancellationToken);
    }

    public async Task<WhatsAppRetryAllResult> RetryAllFailedAsync(DateTimeOffset? since = null, bool latestPerChatOnly = false,
        WhatsAppInstanceRef? instance = null, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject { ["last_message"] = latestPerChatOnly };
        if (since is { } start)
            body["start_date"] = FormatUtc(start);

        var root = await _api.SendAsync(new WaPilotRequest(HttpMethod.Post, MessagePath(instance, "messages/retry-all"))
        {
            Content = () => new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
            NotFoundAsNull = true, // 404 = "No failed messages found."
        }, cancellationToken);

        return root?.To<WhatsAppRetryAllResult>() ?? new WhatsAppRetryAllResult { Message = "No failed messages found." };
    }

    public async Task<WhatsAppTemplatePage> ListTemplatesAsync(WhatsAppTemplateQuery? query = null,
        string? instanceId = null, CancellationToken cancellationToken = default)
    {
        query ??= new WhatsAppTemplateQuery();
        var path = WithQuery($"official/{Escape(_api.ResolveInstanceId(instanceId))}/templates",
            ("scope", query.IncludeAll ? "all" : "approved"),
            ("status", query.Status),
            ("category", query.Category),
            ("language", query.Language),
            ("search", query.Search),
            ("after", query.After),
            ("limit", query.Limit));

        var root = await _api.GetAsync(path, cancellationToken);
        return new WhatsAppTemplatePage
        {
            Items = root.Prop("data")?.To<List<WhatsAppTemplateInfo>>() ?? new List<WhatsAppTemplateInfo>(),
            NextCursor = root.Prop("meta")?.GetString("next_cursor"),
        };
    }

    private async Task<WhatsAppSendResult> SendMediaAsync(string action, string to, WhatsAppMedia media, string? caption,
        WhatsAppSendOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        var fields = new Dictionary<string, string?>
        {
            ["chat_id"] = _api.NormalizeRecipient(to),
            ["caption"] = string.IsNullOrWhiteSpace(caption) ? null : caption,
            ["reply_to"] = options?.ReplyTo,
            ["priority"] = options?.Priority?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["send_at"] = options?.SendAt is { } sendAt ? FormatUtc(sendAt) : null,
        };

        var root = await _api.SendFormAsync(HttpMethod.Post, MessagePath(options, action), fields, "media", media,
            cancellationToken, IdempotencyKey(options));
        return root.To<WhatsAppSendResult>();
    }

    private async Task<WhatsAppSendResult> SendJsonAsync(string path, JsonObject body, WhatsAppSendOptions? options,
        CancellationToken cancellationToken)
    {
        var root = await _api.SendJsonAsync(HttpMethod.Post, path, body, cancellationToken, IdempotencyKey(options));
        return root.To<WhatsAppSendResult>();
    }

    private JsonObject CreateBody(string to, WhatsAppSendOptions? options)
    {
        var body = new JsonObject { ["chat_id"] = _api.NormalizeRecipient(to) };
        SetIfNotNull(body, "reply_to", options?.ReplyTo);
        if (options?.Priority is { } priority)
            body["priority"] = priority;
        if (options?.SendAt is { } sendAt)
            body["send_at"] = FormatUtc(sendAt);
        return body;
    }

    private static JsonObject TemplateNode(string name, string languageCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        return new JsonObject { ["name"] = name, ["language"] = new JsonObject { ["code"] = languageCode } };
    }

    /// <summary>
    /// The caller's key, or a fresh one: the provider replays the original message for a repeated key, which
    /// makes automatic retries of a send safe.
    /// </summary>
    private static string IdempotencyKey(WhatsAppSendOptions? options)
    {
        var key = options?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
            return Guid.NewGuid().ToString("N");
        if (key.Length > MaxIdempotencyKeyLength)
            throw new ArgumentException($"Idempotency key must be {MaxIdempotencyKeyLength} characters or fewer.", nameof(options));
        return key;
    }

    private string MessagePath(WhatsAppSendOptions? options, string action) => MessagePath(options?.Instance, action);

    private string MessagePath(WhatsAppInstanceRef? instance, string action)
    {
        var resolved = _api.ResolveInstance(instance);
        var id = Escape(resolved.Id);
        return resolved.Kind == WhatsAppInstanceKind.Official ? $"official/{id}/{action}" : $"{id}/{action}";
    }

    private string OfficialPath(WhatsAppSendOptions? options, string action) =>
        $"official/{Escape(_api.ResolveInstanceId(options?.Instance?.Id))}/{action}";

    private static void SetIfNotNull(JsonObject node, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            node[name] = value;
    }
}
