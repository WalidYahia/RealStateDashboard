using RealState.Application.Entities;

namespace RealState.Application.Inventory;

/// <summary>The current stock position of a product in a warehouse (weighted-average valuation).</summary>
public sealed record StockLevel(decimal Quantity, decimal Value, decimal AverageCost);

/// <summary>
/// Inventory engine: knows stock quantity and valuation (weighted-average), and derives everything from
/// the <see cref="InventoryMovement"/> subledger. Document posting (which also writes GL through the
/// accounting engine) is added in a later phase; this contract covers defaults, numbering and stock reads.
/// </summary>
public interface IInventoryEngine
{
    /// <summary>Seeds the tenant's posting profile, a default warehouse and base units of measure if missing. Commits on its own.</summary>
    Task EnsureDefaultsAsync(CancellationToken ct = default);

    /// <summary>Creates (or renames) the inventory subsidiary account for a warehouse under the control account. No SaveChanges.</summary>
    Task EnsureWarehouseAccountAsync(Entities.Warehouse warehouse, CancellationToken ct = default);

    /// <summary>Weighted-average stock level of a product in a warehouse as of a date (default: now).</summary>
    Task<StockLevel> GetStockAsync(Guid productId, Guid warehouseId, DateTime? asOf = null, CancellationToken ct = default);

    /// <summary>
    /// Next year-prefixed document number (year*1000000 + seq, e.g. 2026000001). Considers unsaved
    /// (local) documents too, so two documents created in one unit of work can't take the same number.
    /// </summary>
    Task<int> NextNumberAsync(IQueryable<int> existingNumbers, IEnumerable<int> localNumbers, int year, CancellationToken ct = default);

    // ---- Document posting (writes movements + GL through the accounting engine; no self-SaveChanges) ----
    Task PostReceiptAsync(GoodsReceipt doc, CancellationToken ct = default);
    Task PostIssueAsync(GoodsIssue doc, CancellationToken ct = default);
    Task PostTransferAsync(StockTransfer doc, CancellationToken ct = default);
    Task PostAdjustmentAsync(InventoryAdjustment doc, CancellationToken ct = default);
    Task PostStockCountAsync(StockCount doc, CancellationToken ct = default);

    /// <summary>
    /// The product's current unit cost (per smallest unit): its weighted average in the warehouse; else across all
    /// warehouses; else the last cost it came in at / was set to. 0 when nothing is known.
    /// </summary>
    Task<decimal> CurrentUnitCostAsync(Guid productId, Guid? warehouseId, CancellationToken ct = default);

    /// <summary>
    /// Posts a cost update: each line's product is revalued to its new unit cost in every warehouse holding it
    /// (value-only movements + one GL entry). Fills the lines' quantity / old cost / value change. No SaveChanges.
    /// </summary>
    Task PostRevaluationAsync(CostRevaluation doc, CancellationToken ct = default);

    /// <summary>Reverses a posted document: removes its movements and its GL entry (no self-SaveChanges).</summary>
    Task ReverseAsync(string sourceType, Guid sourceId, CancellationToken ct = default);

    /// <summary>
    /// (Re)posts a product's opening stock in a warehouse: its previous movement + journal entry are replaced by the
    /// current quantity / cost / date (none when the quantity is 0 — the caller then removes the row). Refused while
    /// stock of that product moved in that warehouse after the old or new date (weighted average has no history rewrite).
    /// </summary>
    Task PostOpeningStockAsync(ProductOpeningStock row, DateTime? previousDate, CancellationToken ct = default);

    /// <summary>Why a product's opening stock in a warehouse can't change now (stock moved after it), else null.</summary>
    Task<string?> OpeningStockLockAsync(Guid productId, Guid warehouseId, Guid? rowId, DateTime since, CancellationToken ct = default);
}

/// <summary>Source-type tags used on movements and journal entries so a document can be reversed.</summary>
public static class InventorySources
{
    /// <summary>Subsidiary-account kind: one inventory child account per warehouse, under the inventory control account.</summary>
    public const string WarehouseSubKind = "Warehouse";

    public const string GoodsReceipt = "GoodsReceipt";
    public const string GoodsIssue = "GoodsIssue";
    public const string StockTransfer = "StockTransfer";
    public const string InventoryAdjustment = "InventoryAdjustment";
    public const string StockCount = "StockCount";
    public const string CostRevaluation = "CostRevaluation";
    /// <summary>«الرصيد الافتتاحي للأصناف» — one per product per warehouse (ProductOpeningStock).</summary>
    public const string OpeningStock = "OpeningStock";
}
