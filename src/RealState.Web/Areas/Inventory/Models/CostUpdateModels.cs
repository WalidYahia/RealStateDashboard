namespace RealState.Web.Areas.Inventory.Models;

// «تحديث تكلفة الأصناف» (CostUpdatesController). Quantities and costs are per each product's default unit.

/// <summary>A product row: its stock and current average cost; the new cost is typed on the page.</summary>
public class CostUpdateRow
{
    public Guid ProductId { get; set; }
    public string Product { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    /// <summary>Default unit's size in smallest units.</summary>
    public decimal Factor { get; set; } = 1m;
    /// <summary>On hand across all warehouses.</summary>
    public decimal Quantity { get; set; }
    public decimal CurrentCost { get; set; }
    /// <summary>Each warehouse's own average (warehouses holding the product) — the new cost replaces each of them.</summary>
    public List<(string Warehouse, decimal Quantity, decimal Cost)> ByWarehouse { get; set; } = new();
}

/// <summary>A posted line: the product and the new cost per its default unit (empty = not changed).</summary>
public class CostUpdateInput
{
    public Guid ProductId { get; set; }
    public decimal? NewCost { get; set; }
}

public sealed record CostUpdateHistoryRow(Guid Id, int Number, DateTime Date, string? By, string? Notes, int Lines, decimal ValueChange);

public class CostUpdatePageVm
{
    public List<CostUpdateRow> Rows { get; set; } = new();
    public List<CostUpdateHistoryRow> History { get; set; } = new();
}

public sealed record CostUpdateDetailLine(string Product, string Unit, decimal Quantity, decimal OldCost, decimal NewCost, decimal ValueChange);

public class CostUpdateDetailsVm
{
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public string? By { get; set; }
    public List<CostUpdateDetailLine> Lines { get; set; } = new();
}
