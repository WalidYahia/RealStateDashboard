using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealState.Web.Areas.Inventory.Models;

// ---------- Stock Balance / Valuation ----------
public class StockBalanceRow
{
    public string Sku { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Warehouse { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
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
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Balance { get; set; }     // running quantity (stock card only)
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
