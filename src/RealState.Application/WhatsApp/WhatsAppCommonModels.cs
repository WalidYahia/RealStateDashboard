namespace RealState.Application.WhatsApp;

/// <summary>
/// The provider exposes two instance families with different send/query URLs: QR (WhatsApp Web
/// session paired by QR code) and Official (Meta WhatsApp Business Platform).
/// </summary>
public enum WhatsAppInstanceKind
{
    Qr = 0,
    Official = 1,
}

/// <summary>Targets a specific instance instead of the configured default.</summary>
/// <param name="Id">The provider's <c>instance_uniquename</c> (numeric database ids are not accepted).</param>
public sealed record WhatsAppInstanceRef(string Id, WhatsAppInstanceKind Kind = WhatsAppInstanceKind.Qr);

/// <summary>Optional per-send settings.</summary>
public sealed class WhatsAppSendOptions
{
    /// <summary>Instance to send from; null uses the configured default.</summary>
    public WhatsAppInstanceRef? Instance { get; init; }

    /// <summary>
    /// De-duplication key (≤ 255 chars) scoped to the token owner, stored without expiry. Re-sending with the
    /// same key on the same instance returns the original message instead of sending again. Use one key per
    /// logical send (e.g. <c>"invoice-123-reminder"</c>). When null a random key is generated so automatic
    /// transport retries stay safe.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Provider message id to reply to (≤ 100 chars).</summary>
    public string? ReplyTo { get; init; }

    /// <summary>Queue priority, non-negative. Provider default is 5.</summary>
    public int? Priority { get; init; }

    /// <summary>Deliver later instead of immediately.</summary>
    public DateTimeOffset? SendAt { get; init; }
}

/// <summary>
/// A file to upload. Use <see cref="FromFile"/> or <see cref="FromBytes"/> where possible: they can be
/// re-read, so the upload is retried automatically on transient failures. A non-seekable stream is sent once.
/// </summary>
public sealed class WhatsAppMedia
{
    private readonly Func<Stream> _open;
    private readonly long? _initialPosition;

    private WhatsAppMedia(Func<Stream> open, string fileName, string? contentType, bool ownsStream, bool canReplay, long? initialPosition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _open = open;
        _initialPosition = initialPosition;
        FileName = fileName;
        ContentType = contentType;
        OwnsStream = ownsStream;
        CanReplay = canReplay;
    }

    /// <summary>File name sent to the provider; its extension drives type validation.</summary>
    public string FileName { get; }

    /// <summary>MIME type; inferred from <see cref="FileName"/> when null.</summary>
    public string? ContentType { get; }

    /// <summary>True when the stream returned by <see cref="Open"/> must be disposed by the caller of <see cref="Open"/>.</summary>
    public bool OwnsStream { get; }

    /// <summary>True when <see cref="Open"/> can be called again for a retry.</summary>
    public bool CanReplay { get; }

    /// <summary>Opens (or rewinds) the content stream for one upload attempt.</summary>
    public Stream Open()
    {
        var stream = _open();
        if (_initialPosition is { } position && stream.CanSeek)
            stream.Position = position;
        return stream;
    }

    public static WhatsAppMedia FromFile(string path, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new(() => File.OpenRead(path), Path.GetFileName(path), contentType, ownsStream: true, canReplay: true);
    }

    public static WhatsAppMedia FromBytes(byte[] content, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(() => new MemoryStream(content, writable: false), fileName, contentType, ownsStream: true, canReplay: true);
    }

    /// <summary>Wraps a caller-owned stream; it is not disposed. Retries are possible only when it is seekable.</summary>
    public static WhatsAppMedia FromStream(Stream content, string fileName, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(() => content, fileName, contentType, ownsStream: false, canReplay: content.CanSeek,
            initialPosition: content.CanSeek ? content.Position : null);
    }
}

/// <summary>One page of results.</summary>
public sealed class WhatsAppPage<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int CurrentPage { get; init; } = 1;
    public int? PerPage { get; init; }
    public int? Total { get; init; }
    public int? LastPage { get; init; }
    public bool HasMore { get; init; }
}
