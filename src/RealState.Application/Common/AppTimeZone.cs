namespace RealState.Application.Common;

/// <summary>
/// The user's time zone at the database boundary. Every date/time is stored in UTC and handled in code in the user's
/// local time: the DbContext's value converter calls <see cref="ToUtc"/> when saving and <see cref="FromUtc"/> when
/// loading (also for values compared in queries). The web app points <see cref="Current"/> at the browser's zone.
/// </summary>
public static class AppTimeZone
{
    /// <summary>The zone of the current user (request); the server's zone outside a request.</summary>
    public static Func<TimeZoneInfo> Current { get; set; } = () => TimeZoneInfo.Local;

    /// <summary>A local value (the user's zone) → UTC for storage. A time skipped by a DST change moves forward.</summary>
    public static DateTime ToUtc(DateTime local)
    {
        if (local == DateTime.MinValue || local == DateTime.MaxValue) return local;
        var zone = Current();
        var v = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(v)) v = v.AddMinutes(30);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(v, zone), DateTimeKind.Unspecified);
    }

    /// <summary>A stored UTC value → the user's local time.</summary>
    public static DateTime FromUtc(DateTime utc)
    {
        if (utc == DateTime.MinValue || utc == DateTime.MaxValue) return utc;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Current()), DateTimeKind.Unspecified);
    }
}
