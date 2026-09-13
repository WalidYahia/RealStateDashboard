using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

// =====================================================================================
//  Inventory module — generic stock quantity + valuation, integrated with the GL.
//  Stock is NEVER stored as a running total on Product; it is derived from InventoryMovement.
// =====================================================================================

/// <summary>A grouping for products (تصنيف).</summary>
public class ProductCategory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>Unit of measure (وحدة قياس), e.g. قطعة / كجم / متر.</summary>
public class UnitOfMeasure : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>A stock location (مخزن). Quantity/value is tracked per product per warehouse.</summary>
public class Warehouse : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; }
}

/// <summary>Product / item master (صنف). No global quantity — see <see cref="InventoryMovement"/>.</summary>
public class Product : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    public Guid? UnitOfMeasureId { get; set; }
    public UnitOfMeasure? UnitOfMeasure { get; set; }

    public bool TrackInventory { get; set; } = true;
    public bool TrackCost { get; set; } = true;
    public bool IsActive { get; set; } = true;
    /// <summary>Reorder point — stock at or below this (and above zero) counts as low on the dashboard. 0 = no alert.</summary>
    public decimal ReorderLevel { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// The inventory subledger row — the single source of truth for stock quantity and value history.
/// Each posting writes movements; balances/valuation are always derived from these, never reconstructed
/// from current product quantities.
/// </summary>
public class InventoryMovement : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public DateTime Date { get; set; }
    public InventoryMovementType MovementType { get; set; }

    /// <summary>The document that produced this movement, e.g. "GoodsReceipt", "GoodsIssue".</summary>
    public string ReferenceType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    /// <summary>The source document's human number (e.g. 2026000001) so a movement can be traced back.</summary>
    public int? ReferenceNumber { get; set; }
    /// <summary>True when this movement was generated to reverse a posted document.</summary>
    public bool IsReversal { get; set; }

    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? Memo { get; set; }
}

// -------------------------------------------------------------------------------------
//  Documents — each is a header + lines. Number is year-prefixed (year*1000000 + seq),
//  e.g. 2026000001, matching the system-wide serialization.
// -------------------------------------------------------------------------------------

/// <summary>Goods Receipt (إذن استلام) — adds stock + valuation. Dr Inventory / Cr Purchase/GRNI.</summary>
public class GoodsReceipt : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public Guid? SupplierId { get; set; }
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    public string? Notes { get; set; }
    public List<GoodsReceiptLine> Lines { get; set; } = new();
}

public class GoodsReceiptLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Goods Issue (إذن صرف) — removes stock. Counter-account depends on <see cref="IssueReason"/>.</summary>
public class GoodsIssue : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public IssueReason Reason { get; set; } = IssueReason.Sale;
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    public string? Notes { get; set; }
    public List<GoodsIssueLine> Lines { get; set; } = new();
}

public class GoodsIssueLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid GoodsIssueId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Unit cost is resolved from the costing method at post time.</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Stock Transfer (تحويل مخزني) between warehouses in the same entity — normally no GL impact.</summary>
public class StockTransfer : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid FromWarehouseId { get; set; }
    public Guid ToWarehouseId { get; set; }
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    public string? Notes { get; set; }
    public List<StockTransferLine> Lines { get; set; } = new();
}

public class StockTransferLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StockTransferId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Inventory Adjustment (تسوية مخزون) — corrects quantity. Increase → gain, decrease → loss.</summary>
public class InventoryAdjustment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public AdjustmentReason Reason { get; set; } = AdjustmentReason.Correction;
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    public string? Notes { get; set; }
    public List<InventoryAdjustmentLine> Lines { get; set; } = new();
}

public class InventoryAdjustmentLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid InventoryAdjustmentId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Signed quantity change: positive increases stock, negative decreases it.</summary>
    public decimal QuantityDelta { get; set; }
    /// <summary>Unit cost used for increases (decreases use the current weighted-average cost).</summary>
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Stock Count (جرد) — physical count vs system; posting generates adjustment movements.</summary>
public class StockCount : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    public string? Notes { get; set; }
    public List<StockCountLine> Lines { get; set; } = new();
}

public class StockCountLine : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StockCountId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Quantity the system expected at count time (snapshot).</summary>
    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
}

/// <summary>
/// Per-tenant posting profile mapping inventory operations to Chart-of-Accounts codes, plus the costing
/// method. Codes reference <see cref="Account.Code"/> — what the accounting engine posts to.
/// </summary>
public class InventoryPostingProfile : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public CostingMethod CostingMethod { get; set; } = CostingMethod.WeightedAverage;

    public string InventoryCode { get; set; } = string.Empty;
    public string CogsCode { get; set; } = string.Empty;
    public string PurchaseGrniCode { get; set; } = string.Empty;
    public string AdjustmentGainCode { get; set; } = string.Empty;
    public string AdjustmentLossCode { get; set; } = string.Empty;
    public string SalesRevenueCode { get; set; } = string.Empty;
    public string ConsumptionExpenseCode { get; set; } = string.Empty;
}
