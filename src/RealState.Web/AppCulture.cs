using System.Globalization;

namespace RealState.Web;

/// <summary>
/// The app's display culture: Arabic (Egypt), but with a Latin dot as the decimal separator so money
/// reads as "2٬000.00" instead of ar-EG's default "2٬000٫00". The Arabic group separator (٬) is kept.
/// Use this everywhere numbers are formatted for display instead of creating a raw CultureInfo("ar-EG").
/// </summary>
public static class AppCulture
{
    public static readonly CultureInfo Ar = Build();

    /// <summary>Date + time ("yyyy/MM/dd HH:mm", the user's time zone); a date without a time shows the date only.</summary>
    public static string When(DateTime d) => d.TimeOfDay == TimeSpan.Zero
        ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
        : d.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public static string When(DateTime? d) => d is DateTime v ? When(v) : "—";

    /// <summary>The date only ("yyyy/MM/dd").</summary>
    public static string Day(DateTime d) => d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private static CultureInfo Build()
    {
        var c = (CultureInfo)new CultureInfo("ar-EG").Clone();
        c.NumberFormat.NumberDecimalSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ".";
        c.NumberFormat.PercentDecimalSeparator = ".";
        // ar-EG's date separator is "‏/" (a right-to-left mark + slash): inside a dir="ltr" cell that mark
        // scrambles "2026/08/22" into "202622/08/". A plain slash renders the same date correctly everywhere.
        c.DateTimeFormat.DateSeparator = "/";
        return CultureInfo.ReadOnly(c);   // shared + immutable, so it is safe as a static
    }
}
