using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RealState.Infrastructure.Persistence.Migrations;

/// <summary>
/// Converts stored date/time columns between the server's local time (what older code wrote) and UTC. SQL Server 2012
/// has no AT TIME ZONE, so the zone's UTC-offset periods (daylight saving included, 2000–2040) are computed here and
/// applied as a CASE over date ranges. A server on UTC needs no change.
/// </summary>
internal static class UtcConversionSql
{
    public static void Convert(MigrationBuilder migrationBuilder, IEnumerable<(string Table, string Column)> columns, bool toUtc)
    {
        var tz = TimeZoneInfo.Local;
        var periods = Periods(tz);
        if (periods.All(p => p.Offset == 0)) return;   // the server is on UTC — values already are
        var baseOffset = (int)tz.BaseUtcOffset.TotalMinutes;
        foreach (var (table, column) in columns)
            migrationBuilder.Sql(Update(table, column, periods, baseOffset, toUtc));
    }

    /// <summary>The zone's constant-offset periods between 2000 and 2040: [Utc from, Utc to) with the offset in minutes.</summary>
    private static List<(DateTime From, DateTime To, int Offset)> Periods(TimeZoneInfo tz)
    {
        var list = new List<(DateTime, DateTime, int)>();
        var start = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var from = start;
        var off = (int)tz.GetUtcOffset(start).TotalMinutes;
        for (var t = start.AddMinutes(30); t < end; t = t.AddMinutes(30))
        {
            var o = (int)tz.GetUtcOffset(t).TotalMinutes;
            if (o == off) continue;
            list.Add((from, t, off));
            from = t; off = o;
        }
        list.Add((from, end, off));
        return list;
    }

    private static string Lit(DateTime d) => "'" + d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'";

    /// <summary>toUtc: a local value in [from+offset, to+offset) loses the offset; otherwise a UTC value in [from, to) gains
    /// it. Values outside 2000–2040 use the zone's base offset; MIN / MAX dates are left alone.</summary>
    private static string Update(string table, string column, List<(DateTime From, DateTime To, int Offset)> periods, int baseOffset, bool toUtc)
    {
        var sb = new StringBuilder($"UPDATE [{table}] SET [{column}] = CASE");
        foreach (var (from, to, offset) in periods)
        {
            var (a, b) = toUtc ? (from.AddMinutes(offset), to.AddMinutes(offset)) : (from, to);
            sb.Append($" WHEN [{column}] >= {Lit(a)} AND [{column}] < {Lit(b)} THEN DATEADD(minute, {(toUtc ? -offset : offset)}, [{column}])");
        }
        sb.Append($" ELSE DATEADD(minute, {(toUtc ? -baseOffset : baseOffset)}, [{column}]) END");
        sb.Append($" WHERE [{column}] IS NOT NULL AND [{column}] > '0001-01-02' AND [{column}] < '9999-12-30';");
        return sb.ToString();
    }
}
