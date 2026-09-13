namespace RealState.Application.Enums;

/// <summary>The kind of stock movement recorded in the inventory subledger.</summary>
public enum InventoryMovementType
{
    Receipt,        // goods in
    Issue,          // goods out
    TransferIn,     // received into a warehouse via transfer
    TransferOut,    // sent out of a warehouse via transfer
    AdjustmentIn,   // stock increase (gain / count surplus)
    AdjustmentOut,  // stock decrease (loss / count shortage)
    Opening         // opening balance
}

/// <summary>Lifecycle of an inventory document. Draft is editable; Posted affects stock + GL; Reversed is undone.</summary>
public enum InventoryDocStatus
{
    Draft,
    Posted,
    Reversed
}

/// <summary>Why stock is being issued (drives the GL counter-account).</summary>
public enum IssueReason
{
    Sale,           // Dr COGS
    Consumption,    // Dr consumption expense
    Damage,         // Dr adjustment loss
    Other
}

/// <summary>Why an adjustment is being made.</summary>
public enum AdjustmentReason
{
    Opening,        // opening inventory (Cr Opening Balance Equity)
    Count,          // physical-count difference
    Correction      // manual correction
}

/// <summary>Inventory costing method. Only WeightedAverage is implemented for now; the others are reserved.</summary>
public enum CostingMethod
{
    WeightedAverage,
    Fifo,
    SpecificIdentification
}
