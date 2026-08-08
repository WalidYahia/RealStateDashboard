namespace RealState.Web;

public static class DateFilterDefaults
{
    /// <summary>
    /// On a fresh page open (empty query string) default a date/datetime range to today —
    /// from the first minute (00:00) to the last minute (23:59). An explicit filter submit
    /// (query string present, even with empty dates) is respected as-is.
    /// </summary>
    public static (DateTime? From, DateTime? To) TodayIfFresh(HttpRequest request, DateTime? from, DateTime? to)
        => request.Query.Count == 0
            ? (DateTime.Today, DateTime.Today.AddDays(1).AddMinutes(-1))
            : (from, to);
}
