using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Accounting.Models;

// ---------- Chart of accounts ----------
public record AccountOption(Guid Id, string Code, string Name, int Type);

public class AccountNode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public int Depth { get; set; }
    public bool IsPostable { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }          // built-in / subsidiary account — no edit/delete
    public bool IsControlManaged { get; set; }  // subsidiaries added only from their own pages
    public bool IsSubsidiary { get; set; }      // a per-entity subsidiary leaf — cannot have children
    public decimal RawNet { get; set; }         // rolled-up (debit − credit) of the subtree
    public decimal Balance { get; set; }        // rolled-up net balance in the account's natural direction
    public List<AccountNode> Children { get; set; } = new();
}

public class ChartVm
{
    public List<AccountNode> Roots { get; set; } = new();
    public bool CanManage { get; set; }
}

public class AccountFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "الكود مطلوب")]
    [Display(Name = "الكود")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "اسم الحساب")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "النوع")]
    public AccountType Type { get; set; }

    [Display(Name = "الحساب الأب")]
    public Guid? ParentId { get; set; }

    [Display(Name = "حساب ترحيلي (يقبل القيود)")]
    public bool IsPostable { get; set; } = true;

    [Display(Name = "مفعّل")]
    public bool IsActive { get; set; } = true;

    public List<AccountOption> ParentAccounts { get; set; } = new();
    public bool TypeLocked { get; set; }        // system accounts can't change type/code
}

/// <summary>One node's new position, posted by the drag/drop save.</summary>
public class AccountMove
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
}

// ---------- General ledger ----------
public class LedgerRow
{
    public Guid EntryId { get; set; }
    public DateTime Date { get; set; }
    public int Number { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Memo { get; set; }
    public string AccountName { get; set; } = string.Empty;   // the specific (leaf) account this line hit
    public bool IsManual { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }        // running balance in the account's natural direction
    public decimal Value { get; set; }          // entry total (Σ debits) — used by the "الكل" journal view
}

// ---------- Journal entry details ----------
public class EntryLineVm
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }
}

public class EntryVm
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public bool IsManual { get; set; }
    public List<EntryLineVm> Lines { get; set; } = new();
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
}

// ---------- Manual journal entry (قيد يدوي) ----------
public class ManualLineModel
{
    public Guid? AccountId { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Memo { get; set; }
}

public class ManualEntryModel
{
    public Guid Id { get; set; }

    [DataType(DataType.Date)][Display(Name = "التاريخ")]
    public DateTime Date { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "البيان مطلوب")]
    [Display(Name = "البيان")]
    public string Description { get; set; } = string.Empty;

    public List<ManualLineModel> Lines { get; set; } = new();
    public List<SelectListItem> Accounts { get; set; } = new();
}

public class LedgerVm
{
    public Guid? AccountId { get; set; }
    public string? AccountCode { get; set; }
    public string? AccountName { get; set; }
    public bool IsDebitNormal { get; set; }
    public bool IsGroup { get; set; }           // selected a parent → rows rolled up from descendants
    public bool IsAll { get; set; }             // "الكل" → every account's lines, no running balance
    public bool CanManage { get; set; }
    public List<SelectListItem> Accounts { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public decimal Opening { get; set; }
    public List<LedgerRow> Rows { get; set; } = new();

    public decimal TotalDebit => Rows.Sum(r => r.Debit);
    public decimal TotalCredit => Rows.Sum(r => r.Credit);
    public decimal TotalValue => Rows.Sum(r => r.Value);   // "الكل" journal grand total
    public decimal Closing => Rows.Count > 0 ? Rows[^1].Balance : Opening;
}
