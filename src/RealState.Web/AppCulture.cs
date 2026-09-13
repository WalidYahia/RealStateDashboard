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

    private static CultureInfo Build()
    {
        var c = (CultureInfo)new CultureInfo("ar-EG").Clone();
        c.NumberFormat.NumberDecimalSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ".";
        c.NumberFormat.PercentDecimalSeparator = ".";
        return CultureInfo.ReadOnly(c);   // shared + immutable, so it is safe as a static
    }
}
