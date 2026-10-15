namespace RealState.Web.Areas.Inventory.Models;

/// <summary>Inventory value held in one warehouse (used by the "value by warehouse" bars).</summary>
public class WarehouseValueRow
{
    public string Warehouse { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Value { get; set; }
}

/// <summary>A product that is out of stock, below its reorder point, or negative.</summary>
public class StockAlertRow
{
    public string Sku { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReorderLevel { get; set; }
    /// <summary>Quantity / reorder level spelled out in the product's units (both are kept in its smallest unit).</summary>
    public string QuantityText { get; set; } = string.Empty;
    public string ReorderText { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;   // نفد / منخفض / سالب
}

public class InventoryDashboardVm
{
    public DateTime AsOf { get; set; } = ClientTime.Today;

    public decimal TotalValue { get; set; }
    public int TotalProducts { get; set; }
    public int ActiveWarehouses { get; set; }
    public int LowStock { get; set; }
    public int OutOfStock { get; set; }
    public int NegativeStock { get; set; }

    public List<WarehouseValueRow> ByWarehouse { get; set; } = new();
    public List<StockAlertRow> Alerts { get; set; } = new();
}
