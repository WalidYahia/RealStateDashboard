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
    Opening,        // opening balance
    Revaluation     // cost update (تحديث التكلفة): value changes, quantity doesn't
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
    Other,
    PurchaseReturn  // Dr بضاعة واردة لم تُفوتر — goods sent back to the supplier (purchase return, at the invoice cost)
}

/// <summary>Why stock is being received (drives the GL counter-account).</summary>
public enum ReceiptReason
{
    Purchase,       // Cr بضاعة واردة لم تُفوتر (cleared by the purchase invoice)
    SalesReturn     // Cr تكلفة المبيعات — goods a customer returned, back at their original cost of sale
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
