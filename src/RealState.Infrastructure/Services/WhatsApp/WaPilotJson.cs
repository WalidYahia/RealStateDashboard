using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RealState.Application.WhatsApp;

namespace RealState.Infrastructure.Services.WhatsApp;

/// <summary>
/// JSON settings and response helpers for WaPilot. Responses are snake_case and the provider states rows
/// "may include extra implementation fields", so reading is deliberately lenient about types
/// (numbers as strings, 0/1 booleans, non-ISO dates, null counters).
/// </summary>
internal static class WaPilotJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters =
        {
            new LenientStringConverter(),
            new LenientBooleanConverter(),
            new LenientNumberConverter<int>(),
            new LenientNumberConverter<long>(),
            new LenientNumberConverter<decimal>(),
            new LenientDateTimeOffsetConverter(),
        },
    };

    /// <summary>The named property, or null when missing or JSON null.</summary>
    public static JsonElement? Prop(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    public static T To<T>(this JsonElement element) =>
        element.Deserialize<T>(Options)
        ?? throw new WhatsAppApiException($"WhatsApp provider returned an empty {typeof(T).Name}.");

    /// <summary>Deserializes the required <paramref name="property"/> (default <c>data</c>) of a response.</summary>
    public static T Read<T>(this JsonElement root, string property = "data") =>
        root.Prop(property) is { } value
            ? value.To<T>()
            : throw new WhatsAppApiException($"WhatsApp provider response has no '{property}' field.");

    public static string? GetString(this JsonElement element, string name) =>
        element.Prop(name) is { } value ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()) : null;

    public static int? GetInt(this JsonElement element, string name) =>
        element.Prop(name) is { } value && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    public static bool? GetBool(this JsonElement element, string name) =>
        element.Prop(name) is { } value ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => bool.TryParse(value.ToString(), out var flag) ? flag : null,
        } : null;

    /// <summary>
    /// Reads a list response. Rows are either <c>data</c> (array) or <c>data.data</c> (Laravel paginator);
    /// paging comes from the top-level <c>pagination</c> object, the paginator, or <c>meta</c>/<c>links</c>.
    /// </summary>
    public static WhatsAppPage<T> ToPage<T>(this JsonElement root)
    {
        var data = root.Prop("data");
        var rows = data is { ValueKind: JsonValueKind.Array } ? data
            : data?.Prop("data") is { ValueKind: JsonValueKind.Array } inner ? inner
            : (JsonElement?)null;

        var paging = root.Prop("pagination")
            ?? (data is { ValueKind: JsonValueKind.Object } ? data : null)
            ?? root.Prop("meta");

        var currentPage = paging?.GetInt("current_page") ?? 1;
        var lastPage = paging?.GetInt("last_page");
        var hasMore = paging?.GetBool("has_more")
            ?? (lastPage is { } last ? currentPage < last : root.Prop("links")?.Prop("next") is not null);

        return new WhatsAppPage<T>
        {
            Items = rows?.To<List<T>>() ?? new List<T>(),
            CurrentPage = currentPage,
            PerPage = paging?.GetInt("per_page"),
            Total = paging?.GetInt("total"),
            LastPage = lastPage,
            HasMore = hasMore,
        };
    }

    private static string RawNumber(ref Utf8JsonReader reader) =>
        Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);

    private static string RawJson(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }

    /// <summary>Accepts strings, numbers, booleans and nested JSON (kept as raw text) for string properties.</summary>
    private sealed class LenientStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Number => RawNumber(ref reader),
                JsonTokenType.StartObject or JsonTokenType.StartArray => RawJson(ref reader),
                _ => null,
            };

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    /// <summary>Accepts true/false, 0/1 and "true"/"1"/"yes"; null reads as false.</summary>
    private sealed class LenientBooleanConverter : JsonConverter<bool>
    {
        public override bool HandleNull => true;

        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number => reader.TryGetDecimal(out var number) && number != 0,
                JsonTokenType.String => reader.GetString()?.Trim().ToLowerInvariant() is "true" or "1" or "yes",
                _ => false,
            };

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
            writer.WriteBooleanValue(value);
    }

    /// <summary>Accepts numbers and numeric strings; null/empty/garbage reads as zero.</summary>
    private sealed class LenientNumberConverter<T> : JsonConverter<T> where T : struct, INumber<T>
    {
        public override bool HandleNull => true;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType switch
            {
                JsonTokenType.Number => RawNumber(ref reader),
                JsonTokenType.String => reader.GetString(),
                _ => null,
            };
            return T.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : T.Zero;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString(null, CultureInfo.InvariantCulture));
    }

    /// <summary>ISO-8601 or Laravel-style "yyyy-MM-dd HH:mm:ss" (assumed UTC); empty or invalid reads as null.</summary>
    private sealed class LenientDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        public override bool HandleNull => true;

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String
            && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var value)
                ? value
                : null;

        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is { } date)
                writer.WriteStringValue(date);
            else
                writer.WriteNullValue();
        }
    }
}
