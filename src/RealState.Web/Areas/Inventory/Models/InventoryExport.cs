using System.Globalization;

namespace RealState.Web.Areas.Inventory.Models;

/// <summary>Turns a header + object-row table (the same data used for Excel) into the shared print model.</summary>
public static class InventoryExport
{
    private static readonly CultureInfo Ar = new("ar-EG");

    public static ListPrintVm ToPrint(string title, IReadOnlyList<string> headers, IEnumerable<object?[]> rows,
        string? subtitle = null, IReadOnlyList<object?>? totals = null)
        => new()
        {
            Title = title,
            Subtitle = subtitle,
            Headers = headers,
            Rows = rows.Select(r => (IReadOnlyList<string>)r.Select(Fmt).ToList()).ToList(),
            Totals = totals?.Select(Fmt).ToList()
        };

    public static string Fmt(object? x) => x switch
    {
        null => "",
        decimal d => d.ToString("N2", Ar),
        double db => db.ToString("N2", Ar),
        int i => i.ToString(Ar),
        long l => l.ToString(Ar),
        DateTime dt => dt.ToString("yyyy/MM/dd"),
        _ => x.ToString() ?? ""
    };
}
