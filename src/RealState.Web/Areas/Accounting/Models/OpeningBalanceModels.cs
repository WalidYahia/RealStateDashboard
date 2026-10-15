namespace RealState.Web.Areas.Accounting.Models;

/// <summary>Which accounts the opening-balances page lists.</summary>
public enum OpeningKind { All, Customers, Suppliers, Safes, Others }

/// <summary>
/// One account on the opening-balances page. <see cref="Target"/> names what is saved: "A:{accountId}" for a chart account,
/// "C:{customerId}" / "S:{supplierId}" / "F:{safeId}" for a customer / supplier / safe — whose subsidiary account is
/// created on save when it doesn't exist yet.
/// </summary>
public class OpeningBalanceRow
{
    public string Target { get; set; } = string.Empty;
    public OpeningKind Kind { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Parent { get; set; }
    public decimal Amount { get; set; }
    public bool IsDebit { get; set; }
    /// <summary>LastUpdateTime of the opening balance (null = never set).</summary>
    public DateTime? UpdatedAt { get; set; }
    /// <summary>Stock / fixed-asset accounts: shown only — their opening value comes from their own pages.</summary>
    public string? ReadOnlySource { get; set; }
}

public class OpeningBalancesVm
{
    public OpeningKind Kind { get; set; }
    public List<OpeningBalanceRow> Rows { get; set; } = new();
    public Dictionary<OpeningKind, int> Counts { get; set; } = new();
    public DateTime OpeningDate { get; set; }
    public bool CanEdit { get; set; }
}
