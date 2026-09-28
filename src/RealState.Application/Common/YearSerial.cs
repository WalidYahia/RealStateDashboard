namespace RealState.Application.Common;

/// <summary>
/// The system-wide numbering rule: every document number is year-prefixed and its sequence restarts at 1
/// each year — number = year × 10^digits + sequence. Each series keeps its own sequence width, e.g.
/// 4 digits: 20260001 → 20270001 · 5: 202600001 → 202700001 · 6: 2026000001 → 2027000001 ·
/// 7: 20260000001 → 20270000001. The year is the document's own date (not today).
/// </summary>
public static class YearSerial
{
    /// <summary>First value of the year's range (exclusive): year × 10^digits.</summary>
    public static long Base(int year, int digits) => year * Pow10(digits);

    /// <summary>Exclusive upper bound of the year's range: (year + 1) × 10^digits.</summary>
    public static long End(int year, int digits) => (year + 1L) * Pow10(digits);

    /// <summary>Next number given the year's current max (null = none yet → sequence 1).</summary>
    public static long Next(long? maxThisYear, int year, int digits) => (maxThisYear ?? Base(year, digits)) + 1;

    private static long Pow10(int digits)
    {
        long p = 1;
        for (var i = 0; i < digits; i++) p *= 10;
        return p;
    }
}
