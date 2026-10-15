using RealState.Application.Entities;

namespace RealState.Web.Areas.CRM.Models;

/// <summary>What a customer-ledger row records.</summary>
public enum CustomerLedgerKind { Contract, Invoice, InstallmentCollection, InvoiceCollection, Return, Refund, AccountReceipt, AccountPayment, OpeningDebit, OpeningCredit }

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

    /// <summary>Lowers what the customer owes (shown as −): collections (incl. on-account receipts) and returns.</summary>
    public bool Decreases => Kind is CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.Return
        or CustomerLedgerKind.AccountReceipt or CustomerLedgerKind.OpeningCredit;
    /// <summary>The row's voucher is a سند صرف (printed from المصروفات) rather than a سند قبض.</summary>
    public bool VoucherIsExpense => Kind is CustomerLedgerKind.Refund or CustomerLedgerKind.AccountPayment;
    /// <summary>Same-day order: what's owed first, then collections, returns, refunds.</summary>
    public int DayOrder => Kind switch
    {
        CustomerLedgerKind.OpeningDebit or CustomerLedgerKind.OpeningCredit => -1,   // the opening balance comes first
        CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.AccountReceipt => 1,
        CustomerLedgerKind.AccountPayment => 3,
        CustomerLedgerKind.Return => 2,
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
}

/// <summary>A sales invoice with a remaining balance, offered by the statement's «تحصيل دفعة».</summary>
public record CollectableOption(Guid InvoiceId, string Label, decimal Remaining);

public class CustomerCollectPickerModel
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public List<CollectableOption> Documents { get; set; } = new();
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Safes { get; set; } = new();
}
