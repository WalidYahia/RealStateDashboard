using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealState.Web.Areas.Inventory.Models;

// ---------- Stock Balance / Valuation ----------
public class StockBalanceRow
{
    public string Sku { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    /// <summary>In the product's smallest unit (<see cref="Unit"/>); <see cref="Breakdown"/> spells it out in all its units.</summary>
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Breakdown { get; set; } = string.Empty;
    /// <summary>Average cost per smallest unit.</summary>
    public decimal AvgCost { get; set; }
    public decimal Value { get; set; }
}

public class StockBalanceVm
{
    public List<StockBalanceRow> Rows { get; set; } = new();
    public decimal TotalValue => Rows.Sum(r => r.Value);
    public bool ByProductOnly { get; set; }   // valuation view rolls warehouses together
}

// ---------- Inventory Movement / Stock Card ----------
public class MovementRow
{
    public DateTime Date { get; set; }
    public string TypeAr { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    /// <summary>In / Out / Balance are in the product's smallest unit (<see cref="Unit"/>).</summary>
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Balance { get; set; }     // running quantity (stock card only)
    public string Unit { get; set; } = string.Empty;
    public string BalanceBreakdown { get; set; } = string.Empty;   // stock card: the balance in all the product's units
}

public class MovementsVm
{
    public Guid? ProductId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<MovementRow> Rows { get; set; } = new();
    public List<SelectListItem> Products { get; set; } = new();
    public List<SelectListItem> Warehouses { get; set; } = new();
    public bool IsStockCard { get; set; }
    public decimal TotalIn => Rows.Sum(r => r.In);
    public decimal TotalOut => Rows.Sum(r => r.Out);
}

// ---------- Inventory / GL Reconciliation ----------
public class ReconciliationVm
{
    public string InventoryAccountCode { get; set; } = string.Empty;
    public string InventoryAccountName { get; set; } = string.Empty;
    public decimal SubledgerValue { get; set; }   // Σ inventory movements value
    public decimal GlValue { get; set; }           // GL inventory account balance
    public decimal Difference => Math.Round(SubledgerValue - GlValue, 2);
    public bool IsReconciled => Difference == 0m;
}

// ---------- Tabbed reports page ----------
/// <summary>One tab of «تقارير المخزون»: its key (?tab=), title and the URL its content is loaded from.</summary>
public record ReportTab(string Key, string Title, string Url);

public class ReportsTabsVm
{
    public string Active { get; set; } = "balance";
    public List<ReportTab> Tabs { get; set; } = new();
}
