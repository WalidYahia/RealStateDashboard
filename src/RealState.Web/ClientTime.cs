using RealState.Application.Common;
using RealState.Application.Interfaces;

namespace RealState.Web;

/// <summary>
/// The web app's clock: UTC from the system, the time zone from the browser. The layout's script stores the browser's
/// IANA time zone (Intl API) in the «tz» cookie; without it (or with an unknown zone) the configured
/// <c>App:TimeZone</c> is used. Registered as the <see cref="IDateTimeProvider"/>, so the engines' "today" and embedded
/// times are the user's.
/// </summary>
public sealed class ClientClock : IDateTimeProvider
{
    public const string Cookie = "tz";
    private const string ItemKey = "__ClientTimeZone";
    private readonly IHttpContextAccessor _http;
    private readonly TimeZoneInfo _fallback;

    public ClientClock(IHttpContextAccessor http, IConfiguration config)
    {
        _http = http;
        _fallback = Find(config["App:TimeZone"]) ?? TimeZoneInfo.Local;
    }

    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone), DateTimeKind.Unspecified);
    public DateTime Today => Now.Date;

    public TimeZoneInfo Zone
    {
        get
        {
            var ctx = _http.HttpContext;
            if (ctx is null) return _fallback;
            if (ctx.Items[ItemKey] is TimeZoneInfo cached) return cached;
            var zone = Find(ctx.Request.Cookies[Cookie]) ?? _fallback;
            ctx.Items[ItemKey] = zone;
            return zone;
        }
    }

    private static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win))
            try { return TimeZoneInfo.FindSystemTimeZoneById(win); } catch { }
        return null;
    }
}

/// <summary>
/// Static access for views and controllers: the user's now / today. Values in code are the user's local time — the
/// DbContext stores them in UTC (AppTimeZone) — so nothing here converts.
/// </summary>
public static class ClientTime
{
    private static IDateTimeProvider? _clock;
    public static void Configure(IDateTimeProvider clock)
    {
        _clock = clock;
        AppTimeZone.Current = () => clock.Zone;   // the database boundary uses the browser's zone too
    }
    private static IDateTimeProvider Clock => _clock ?? throw new InvalidOperationException("ClientTime not configured.");

    public static DateTime Now => Clock.Now;
    public static DateTime Today => Clock.Today;

    /// <summary>Bounds [from, to) of a day range picked in a filter (to is inclusive of its whole day).</summary>
    public static (DateTime? From, DateTime? To) DayRange(DateTime? fromDay, DateTime? toDay)
        => (fromDay?.Date, toDay?.Date.AddDays(1));
}
