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

    /// <summary>Reverses a posted document: removes its movements and its GL entry (no self-SaveChanges).</summary>
    Task ReverseAsync(string sourceType, Guid sourceId, CancellationToken ct = default);
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
}
