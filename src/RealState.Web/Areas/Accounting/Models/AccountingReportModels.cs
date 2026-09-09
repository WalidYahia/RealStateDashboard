using RealState.Application.Enums;

namespace RealState.Web.Areas.Accounting.Models;

// ---------- ميزان المراجعة (Trial Balance) ----------
// A working trial balance: opening balance, period movement, and closing balance —
// each split into debit/credit — for every account (parents rolled up from their children).
public class TrialBalanceRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Depth { get; set; }
    public bool IsGroup { get; set; }     // has children → shown bold

    public decimal OpeningDebit { get; set; }
    public decimal OpeningCredit { get; set; }
    public decimal PeriodDebit { get; set; }
    public decimal PeriodCredit { get; set; }
    public decimal ClosingDebit { get; set; }
    public decimal ClosingCredit { get; set; }
}

public class TrialBalanceVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<TrialBalanceRow> Rows { get; set; } = new();

    // Grand totals are summed over leaf (postable) accounts only, to avoid double-counting parents.
    public decimal TotOpeningDebit { get; set; }
    public decimal TotOpeningCredit { get; set; }
    public decimal TotPeriodDebit { get; set; }
    public decimal TotPeriodCredit { get; set; }
    public decimal TotClosingDebit { get; set; }
    public decimal TotClosingCredit { get; set; }

    public bool IsBalanced => Math.Round(TotClosingDebit - TotClosingCredit, 2) == 0m;
}

// ---------- قائمة الدخل (Income Statement) ----------
public class IncomeStatementLine
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class IncomeStatementVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<IncomeStatementLine> Revenues { get; set; } = new();
    public List<IncomeStatementLine> Expenses { get; set; } = new();
    public decimal TotalRevenue => Revenues.Sum(r => r.Amount);
    public decimal TotalExpense => Expenses.Sum(e => e.Amount);
    public decimal NetProfit => TotalRevenue - TotalExpense;
}
