using RealState.Application.Entities;

namespace RealState.Web.Areas.CRM.Models;

/// <summary>What a customer-ledger row records.</summary>
/// <remarks>ContractCancel reverses a cancelled contract (−); CancelFee is the fee kept from its refund (+); paying the refund
/// back is a <see cref="Refund"/> row. PeriodOpening is «الرصيد الافتتاحي للفترة» — the balance carried in from before a
/// filtered period (first row only). SupplierSide is a movement on the linked supplier account of a «عميل ومورد» party
/// (see <see cref="CustomerLedgerRow.SupplierRow"/>). AdjustmentUp / AdjustmentDown: a balance adjustment (تسوية رصيد) that
/// raises (+) / lowers (−) what the customer owes.</remarks>
public enum CustomerLedgerKind { Contract, Invoice, InstallmentCollection, InvoiceCollection, Return, Refund, AccountReceipt, AccountPayment, OpeningDebit, OpeningCredit, ContractCancel, CancelFee, PeriodOpening, SupplierSide, AdjustmentUp, AdjustmentDown }

/// <summary>One row of the running-balance customer ledger («كشف حساب» — same layout as the supplier's).</summary>
public class CustomerLedgerRow
{
    public CustomerLedgerKind Kind { get; set; }
    /// <summary>Contract / invoice / return id (for the link).</summary>
    public Guid Id { get; set; }
    /// <summary>The cash voucher (سند قبض / سند صرف), when the row is a collection or a refund.</summary>
    public Guid? VoucherId { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Statement { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal BalanceBefore { get; set; }
    public decimal Balance { get; set; }
    /// <summary>The invoice / return lines, for the detailed statement («تفصيلي»; see CustomerLedger.AttachItemsAsync).</summary>
    public List<RealState.Web.Models.StatementItem> Items { get; set; } = new();

    /// <summary>
    /// «عميل ومورد»: the linked supplier's movement this row shows in the merged statement. What raises the amount owed to
    /// the supplier (a purchase invoice) lowers the party's net debit here, and what settles it (a payment) raises it.
    /// </summary>
    public RealState.Web.Areas.Suppliers.Models.SupplierLedgerRow? SupplierRow { get; set; }

    /// <summary>Lowers what the customer owes (shown as −): collections (incl. on-account receipts) and returns.</summary>
    public bool Decreases => SupplierRow is { } s ? !s.Decreases
        : Kind is CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.Return or CustomerLedgerKind.ContractCancel
            or CustomerLedgerKind.AccountReceipt or CustomerLedgerKind.OpeningCredit or CustomerLedgerKind.AdjustmentDown;
    /// <summary>The row's voucher is a سند صرف (printed from المصروفات) rather than a سند قبض.</summary>
    public bool VoucherIsExpense => Kind is CustomerLedgerKind.Refund or CustomerLedgerKind.AccountPayment;
    /// <summary>Same-day order: what's owed first, then collections, returns, refunds.</summary>
    public int DayOrder => SupplierRow?.DayOrder ?? Kind switch
    {
        CustomerLedgerKind.OpeningDebit or CustomerLedgerKind.OpeningCredit => -1,   // the opening balance comes first
        CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.AccountReceipt => 1,
        CustomerLedgerKind.AccountPayment => 3,
        CustomerLedgerKind.AdjustmentUp or CustomerLedgerKind.AdjustmentDown => 4,   // after the day's other movements
        CustomerLedgerKind.Return or CustomerLedgerKind.ContractCancel or CustomerLedgerKind.CancelFee => 2,
        CustomerLedgerKind.Refund => 3,
        _ => 0
    };
}

public class CustomerLedgerVm
{
    public Customer Customer { get; set; } = default!;

    // Overall account balances (NOT date-filtered) — shown in the cards.
    public decimal TotalObligations { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalRemaining => TotalObligations - TotalPaid;
    public int DocumentsCount { get; set; }
    public int CollectionsCount { get; set; }

    // Date-filtered ledger detail.
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<CustomerLedgerRow> Rows { get; set; } = new();
    /// <summary>Balance at the end of the selected range.</summary>
    public decimal ClosingBalance { get; set; }
    public bool HasEntries => Rows.Count > 0;

    // «عميل ومورد»: the linked supplier account merged into this statement (null when not linked, or not shown to the user).
    public Supplier? LinkedSupplier { get; set; }
    public bool IsMerged => LinkedSupplier is not null;
    public decimal SupplierObligations { get; set; }
    public decimal SupplierPaid { get; set; }
    /// <summary>What's owed to the party as a supplier (− = owed by them).</summary>
    public decimal SupplierBalance => SupplierObligations - SupplierPaid;
    public int SupplierInvoicesCount { get; set; }
    /// <summary>The party's net balance: + = owed by them (مدين), − = owed to them (دائن).</summary>
    public decimal NetBalance => TotalRemaining - SupplierBalance;
}
