using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RealState.Application.WhatsApp;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary>One call to the WaPilot API.</summary>
internal sealed class WaPilotRequest
{
    public WaPilotRequest(HttpMethod method, string path)
    {
        Method = method;
        Path = path;
    }

    public HttpMethod Method { get; }

    /// <summary>Path relative to <see cref="WhatsAppOptions.BaseUrl"/>, with segments already escaped.</summary>
    public string Path { get; }

    /// <summary>Builds the body for one attempt (called again on every retry).</summary>
    public Func<HttpContent>? Content { get; init; }

    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Retry network errors, timeouts and 5xx. Only safe for reads and idempotency-keyed sends — anything
    /// else may already have been applied by the provider. HTTP 429 is always retried.
    /// </summary>
    public bool RetryTransientFailures { get; init; }

    /// <summary>False when the body cannot be rebuilt (non-seekable upload stream): a single attempt is made.</summary>
    public bool CanReplay { get; init; } = true;

    /// <summary>Return null for HTTP 404 instead of throwing.</summary>
    public bool NotFoundAsNull { get; init; }
}

/// <summary>
/// Low-level WaPilot API v2 transport: <c>token</c> header, JSON/multipart bodies, the
/// <c>{ success, message, code, errors }</c> error contract, and retries (429 + Retry-After, transient failures).
/// </summary>
internal sealed class WaPilotApiClient
{
    private static readonly MediaTypeWithQualityHeaderValue AcceptJson = new("application/json");

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp", [".gif"] = "image/gif",
        [".mp4"] = "video/mp4", [".mov"] = "video/quicktime", [".avi"] = "video/x-msvideo", [".mkv"] = "video/x-matroska",
        [".webm"] = "video/webm", [".3gp"] = "video/3gpp",
        [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4", [".aac"] = "audio/aac",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain", [".csv"] = "text/csv",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".zip"] = "application/zip", [".rar"] = "application/vnd.rar",
    };

    private readonly HttpClient _http;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WaPilotApiClient> _logger;

    public WaPilotApiClient(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<WaPilotApiClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public WhatsAppOptions Options => _options;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ApiToken) && !string.IsNullOrWhiteSpace(_options.InstanceId);

    public string ResolveInstanceId(string? instanceId)
    {
        if (!string.IsNullOrWhiteSpace(instanceId))
            return instanceId.Trim();
        if (!string.IsNullOrWhiteSpace(_options.InstanceId))
            return _options.InstanceId.Trim();
        throw new InvalidOperationException(
            $"No WhatsApp instance was specified and '{WhatsAppOptions.SectionName}:InstanceId' is not configured.");
    }

    public WhatsAppInstanceRef ResolveInstance(WhatsAppInstanceRef? instance) =>
        instance is null
            ? new WhatsAppInstanceRef(ResolveInstanceId(null), _options.InstanceKind)
            : instance with { Id = ResolveInstanceId(instance.Id) };

    public string NormalizeRecipient(string phoneOrChatId) =>
        _options.NormalizePhoneNumbers
            ? WhatsAppPhoneNumber.Normalize(phoneOrChatId, _options.DefaultCountryCode)
            : phoneOrChatId.Trim();

    public static string Escape(string segment) => Uri.EscapeDataString(segment);

    /// <summary>Appends a query string built from the non-empty values.</summary>
    public static string WithQuery(string path, params (string Name, object? Value)[] parameters)
    {
        var pairs = parameters
            .Select(p => (p.Name, Value: p.Value switch
            {
                null => null,
                bool flag => flag ? "true" : "false",
                IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => p.Value.ToString(),
            }))
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{Escape(p.Name)}={Escape(p.Value!)}")
            .ToList();
        return pairs.Count == 0 ? path : $"{path}?{string.Join('&', pairs)}";
    }

    public static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    public Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken) =>
        SendRequiredAsync(new WaPilotRequest(HttpMethod.Get, path) { RetryTransientFailures = true }, cancellationToken);

    public Task<JsonElement?> GetOrNullAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(new WaPilotRequest(HttpMethod.Get, path) { RetryTransientFailures = true, NotFoundAsNull = true }, cancellationToken);

    /// <summary>Sends a JSON body (or none). Transient failures are retried only when an idempotency key is given.</summary>
    public Task<JsonElement> SendJsonAsync(HttpMethod method, string path, JsonNode? body,
        CancellationToken cancellationToken, string? idempotencyKey = null) =>
        SendRequiredAsync(new WaPilotRequest(method, path)
        {
            Content = body is null ? null : () => new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            IdempotencyKey = idempotencyKey,
            RetryTransientFailures = idempotencyKey is not null,
        }, cancellationToken);

    /// <summary>Sends a multipart form with an optional uploaded file.</summary>
    public Task<JsonElement> SendFormAsync(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string?>> fields,
        string mediaField, WhatsAppMedia? media, CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        var fieldList = fields.Where(f => f.Value is not null).ToList();
        return SendRequiredAsync(new WaPilotRequest(method, path)
        {
            Content = () =>
            {
                var form = new MultipartFormDataContent();
                foreach (var (name, value) in fieldList)
                    form.Add(new StringContent(value!, Encoding.UTF8), name);
                if (media is not null)
                    form.Add(CreateMediaContent(media), mediaField, media.FileName);
                return form;
            },
            IdempotencyKey = idempotencyKey,
            RetryTransientFailures = idempotencyKey is not null,
            CanReplay = media?.CanReplay ?? true,
        }, cancellationToken);
    }

    public async Task<JsonElement> SendRequiredAsync(WaPilotRequest request, CancellationToken cancellationToken) =>
        await SendAsync(request, cancellationToken)
        ?? throw new WhatsAppApiException("WhatsApp provider returned an empty response.");

    public async Task<JsonElement?> SendAsync(WaPilotRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiToken))
            throw new InvalidOperationException($"WhatsApp is not configured: set '{WhatsAppOptions.SectionName}:ApiToken'.");

        var maxAttempts = request.CanReplay ? 1 + Math.Max(0, _options.MaxRetries) : 1;

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(request.Method, request.Path);
            message.Headers.TryAddWithoutValidation("token", _options.ApiToken);
            message.Headers.Accept.Add(AcceptJson);
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
                message.Headers.TryAddWithoutValidation("Idempotency-Key", request.IdempotencyKey);
            message.Content = request.Content?.Invoke();

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                if (request.RetryTransientFailures && attempt < maxAttempts)
                {
                    await WaitBeforeRetryAsync(request, attempt, null, ex.GetType().Name, cancellationToken);
                    continue;
                }
                throw new WhatsAppApiException(
                    ex is OperationCanceledException
                        ? "WhatsApp provider did not respond in time."
                        : $"WhatsApp provider could not be reached: {ex.Message}",
                    innerException: ex);
            }

            using (response)
            {
                var status = response.StatusCode;
                var canRetry = attempt < maxAttempts
                               && (status == HttpStatusCode.TooManyRequests
                                   || ((int)status >= 500 && request.RetryTransientFailures));
                if (canRetry)
                {
                    await WaitBeforeRetryAsync(request, attempt, GetRetryAfter(response), $"HTTP {(int)status}", cancellationToken);
                    continue;
                }

                if (status == HttpStatusCode.NotFound && request.NotFoundAsNull)
                    return null;

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var root = TryParse(body);

                if (response.IsSuccessStatusCode && root is { } json && json.GetBool("success") != false)
                    return json;

                throw CreateException(response, root);
            }
        }
    }

    private async Task WaitBeforeRetryAsync(WaPilotRequest request, int attempt, TimeSpan? retryAfter, string reason,
        CancellationToken cancellationToken)
    {
        var max = TimeSpan.FromSeconds(Math.Max(1, _options.MaxRetryDelaySeconds));
        var delay = retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
        if (delay > max)
            delay = max;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        _logger.LogWarning("WhatsApp {Method} {Path} failed ({Reason}) on attempt {Attempt}; retrying in {Delay}.",
            request.Method, StripQuery(request.Path), reason, attempt, delay);
        await Task.Delay(delay, cancellationToken);
    }

    private static HttpContent CreateMediaContent(WhatsAppMedia media)
    {
        var stream = media.Open();
        HttpContent content = media.OwnsStream ? new StreamContent(stream) : new LeaveOpenStreamContent(stream);
        var contentType = media.ContentType
                          ?? (MimeTypes.TryGetValue(Path.GetExtension(media.FileName), out var known) ? known : "application/octet-stream");
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private static JsonElement? TryParse(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WhatsAppApiException CreateException(HttpResponseMessage response, JsonElement? root)
    {
        var message = root?.GetString("message");
        var code = root?.GetString("code");
        Dictionary<string, string[]>? errors = null;

        if (root?.Prop("errors") is { ValueKind: JsonValueKind.Object } errorObject)
        {
            errors = errorObject.EnumerateObject().ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Array
                    ? p.Value.EnumerateArray().Select(e => e.ToString()).ToArray()
                    : new[] { p.Value.ToString() });
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = response.IsSuccessStatusCode
                ? "WhatsApp provider returned an unexpected response."
                : $"WhatsApp provider returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
        }
        if (errors is { Count: > 0 })
            message += " " + string.Join(" ", errors.SelectMany(e => e.Value));

        return new WhatsAppApiException(message, response.StatusCode, code, errors, GetRetryAfter(response));
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static string StripQuery(string path)
    {
        var index = path.IndexOf('?');
        return index < 0 ? path : path[..index];
    }

    /// <summary>Streams a caller-owned stream without disposing it.</summary>
    private sealed class LeaveOpenStreamContent : HttpContent
    {
        private readonly Stream _stream;

        public LeaveOpenStreamContent(Stream stream) => _stream = stream;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _stream.CopyToAsync(stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            _stream.CopyToAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = _stream.CanSeek ? _stream.Length - _stream.Position : 0;
            return _stream.CanSeek;
        }
    }
}
