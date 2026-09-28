using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Accounting.Models;

public class SafeFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم الخزنة مطلوب")]
    [Display(Name = "اسم الخزنة")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "نوع الخزنة")]
    public SafeType Type { get; set; } = SafeType.Normal;

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الرصيد الافتتاحي (ج.م)")]
    public decimal InitialAmount { get; set; }

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "سحب على المكشوف")]
    public bool AllowOverdraft { get; set; }

    /// <summary>Display only: whether the current user may change <see cref="AllowOverdraft"/>.</summary>
    public bool CanSetOverdraft { get; set; }
}

// ---------- Safe transfers (تحويل بين الخزائن) ----------
public class SafeTransferFormModel
{
    public Guid Id { get; set; }
    /// <summary>Display only: the transfer's number, or the number the next new transfer will take.</summary>
    public int Number { get; set; }

    [Required(ErrorMessage = "اختر الخزنة المحوَّل منها")]
    [Display(Name = "من خزنة")]
    public Guid? FromSafeId { get; set; }

    [Required(ErrorMessage = "اختر الخزنة المحوَّل إليها")]
    [Display(Name = "إلى خزنة")]
    public Guid? ToSafeId { get; set; }

    [Range(0.01, 999999999999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")]
    [Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }

    [Required][Display(Name = "التاريخ والوقت")]
    public DateTime OccurredAt { get; set; } = DateTime.Now;

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>Active safes with their current balance in the label (and data-balance for the form hint).</summary>
    public List<SafeOption> Safes { get; set; } = new();
}

public record SafeOption(Guid Id, string Name, SafeType Type, decimal Balance, bool AllowOverdraft);

public class SafeTransferRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime OccurredAt { get; set; }
    public string FromSafe { get; set; } = string.Empty;
    public string ToSafe { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public class SafeTransferListVm
{
    public List<SafeTransferRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>Filter: transfers where this safe is the source or the destination.</summary>
    public Guid? SafeId { get; set; }
    public string? Q { get; set; }
    public List<SelectListItem> SafeOptions { get; set; } = new();
    public decimal Total => Rows.Sum(r => r.Amount);
}

public class SafeRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SafeType Type { get; set; }
    public bool IsActive { get; set; }
    public bool AllowOverdraft { get; set; }
    public decimal InitialAmount { get; set; }
    /// <summary>Real income / expense — transfers between safes are excluded and shown as <see cref="TransfersNet"/>.</summary>
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal TransferIn { get; set; }
    public decimal TransferOut { get; set; }
    public decimal TransfersNet => TransferIn - TransferOut;
    public int TxnCount { get; set; }
    public decimal Balance => InitialAmount + Income - Expense + TransfersNet;
}

/// <summary>Create/update a manual income or expense (Type is fixed by the controller).</summary>
public class TxnFormModel
{
    public Guid Id { get; set; }

    [Range(0.01, 999999999999, ErrorMessage = "المبلغ مطلوب")]
    [Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "التاريخ والوقت مطلوبان")]
    [Display(Name = "التاريخ والوقت")]
    public DateTime OccurredAt { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "الوصف مطلوب")]
    [Display(Name = "الوصف")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "الخزنة مطلوبة")]
    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();

    // --- Category (البند) + HR linkage (advance / reward) ---
    public bool IsExpense { get; set; }
    [Display(Name = "البند")]
    public Guid? CategoryId { get; set; }
    public List<RealState.Application.Entities.TxnCategory> Categories { get; set; } = new();
    [Display(Name = "النوع")]
    public AccountingEntryKind Kind { get; set; } = AccountingEntryKind.General;
    [Display(Name = "السلفة")]
    public Guid? AdvanceId { get; set; }
    [Display(Name = "المكافأة")]
    public Guid? RewardId { get; set; }
    public List<SelectListItem> AdvanceOptions { get; set; } = new();
    public List<SelectListItem> RewardOptions { get; set; } = new();
    /// <summary>JSON map of advance/reward id → amount, for client-side amount auto-fill.</summary>
    public string AmountsJson { get; set; } = "{}";
}

public class SafeMovementsVm
{
    public Guid SafeId { get; set; }
    public string SafeName { get; set; } = string.Empty;
    public decimal InitialAmount { get; set; }
    public decimal Balance { get; set; }
    public List<TxnRow> Transactions { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Q { get; set; }
}

/// <summary>List page for incomes or expenses (Type set by the controller).</summary>
public class TxnListVm
{
    public TxnType Type { get; set; }
    public List<TxnRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Q { get; set; }

    /// <summary>Selected «المصدر» filter value (null = all). Encoded as "c:{categoryId}" or "s:{sourceInt}".</summary>
    public string? Source { get; set; }
    /// <summary>«المصدر» options: the predefined categories (بنود) followed by the system sources for this Type.</summary>
    public List<SelectListItem> SourceOptions { get; set; } = new();

    public decimal Total => Rows.Sum(r => r.Amount);
}

public class TxnRow
{
    public Guid Id { get; set; }
    public int Serial { get; set; }
    public string SafeName { get; set; } = string.Empty;
    public TxnType Type { get; set; }
    public TxnSource Source { get; set; }
    public decimal Amount { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    /// <summary>Document number shown instead of the serial — e.g. SF-2026000001 for a transfer movement.</summary>
    public string? DocNo { get; set; }
    public bool IsTransfer => Source == TxnSource.SafeTransfer;
    public bool IsManual => Source == TxnSource.Manual;
    /// <summary>An advance disbursement expense — deletable here to "un-disburse" the advance.</summary>
    public bool IsAdvanceDisbursement => Source == TxnSource.AdvanceDisbursement;
    /// <summary>An advance repayment income (سداد سلفة) — deletable here to reverse that repayment.</summary>
    public bool IsAdvanceRepayment => Source == TxnSource.AdvanceRepayment;

    /// <summary>Safe balance immediately after this transaction (populated on the movements screen).</summary>
    public decimal RunningBalance { get; set; }
}
