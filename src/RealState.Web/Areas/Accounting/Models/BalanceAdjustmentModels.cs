using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealState.Web.Areas.Accounting.Models;

/// <summary>
/// تسوية رصيد — raise or lower one customer's / supplier's balance without cash (a settlement discount, a written-off
/// remainder, a correction…). Views/BalanceAdjustments/_Form, opened from the party's statement.
/// </summary>
public class BalanceAdjustmentFormModel
{
    /// <summary>"customer" or "supplier".</summary>
    public string Party { get; set; } = "customer";
    public Guid PartyId { get; set; }

    /// <summary>True: the balance goes up (the customer owes more / more is owed to the supplier); false: down.</summary>
    [Display(Name = "نوع التسوية")]
    public bool Increase { get; set; }

    [Range(0.01, 999_999_999_999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")][Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }

    /// <summary>Date + time entered by the user (default: now) — the journal entry takes it.</summary>
    [DataType(DataType.DateTime)][Display(Name = "التاريخ والوقت")]
    public DateTime Date { get; set; } = ClientTime.Now;

    [Display(Name = "السبب / البيان (اختياري)")]
    public string? Reason { get; set; }

    /// <summary>«عميل ومورد» (opened from the customer statement): the customer's linked supplier — its balance and the net
    /// are shown for reference; the adjustment goes on the customer account.</summary>
    public Guid? LinkedSupplierId { get; set; }

    // ---- display ----
    public bool IsSupplier => Party == "supplier";
    public bool IsMerged => LinkedSupplierId is not null;
    public string PartyName { get; set; } = string.Empty;
    /// <summary>The balance now, in the account's usual direction: owed by the customer / owed to the supplier.</summary>
    public decimal Balance { get; set; }
    /// <summary>«عميل ومورد»: each account's balance (customer: owed by them; supplier: owed to them).</summary>
    public decimal CustomerBalance { get; set; }
    public decimal SupplierBalance { get; set; }
    /// <summary>The party's net balance: + = owed by them (مدين), − = owed to them (دائن).</summary>
    public decimal NetBalance => CustomerBalance - SupplierBalance;
}
