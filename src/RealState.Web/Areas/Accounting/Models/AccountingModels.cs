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

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "الرصيد الافتتاحي (ج.م)")]
    public decimal InitialAmount { get; set; }

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; } = true;
}

public class SafeRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal InitialAmount { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public int TxnCount { get; set; }
    public decimal Balance => InitialAmount + Income - Expense;
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

    // --- HR linkage (advance / reward) ---
    public bool IsExpense { get; set; }
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
    public bool IsManual => Source == TxnSource.Manual;

    /// <summary>Safe balance immediately after this transaction (populated on the movements screen).</summary>
    public decimal RunningBalance { get; set; }
}
