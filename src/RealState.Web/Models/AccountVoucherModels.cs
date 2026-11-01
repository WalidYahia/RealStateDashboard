using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealState.Web.Models;

/// <summary>
/// «تحصيل دفعة» (customer) / «سداد دفعة للمورد» from an account statement: a cash voucher on the party's account — a
/// سند قبض on the customer's account (TxnSource.CustomerAccount) or a سند صرف on the supplier's (TxnSource.SupplierAccount).
/// Payments settle the account balance, not a particular invoice. Views/Shared/_AccountVoucherForm.
/// </summary>
public class AccountVoucherFormModel
{
    public Guid PartyId { get; set; }

    [Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }

    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    /// <summary>Date + time entered by the user (default: now).</summary>
    [DataType(DataType.DateTime)][Display(Name = "التاريخ والوقت")]
    public DateTime OccurredAt { get; set; } = ClientTime.Now;

    [Display(Name = "ملاحظات")]
    public string? Description { get; set; }

    // ---- display ----
    public bool IsSupplier { get; set; }
    public string PartyName { get; set; } = string.Empty;
    /// <summary>The account balance now, in its usual direction: owed by the customer / owed to the supplier (− = the other way).</summary>
    public decimal Balance { get; set; }
    public List<SelectListItem> Safes { get; set; } = new();
}
