namespace RealState.Web.Areas.Reports.Models;

// ---------- Daily report ----------
public record DailyContractRow(string Code, string Customer, string Unit, decimal Value);
public record DailyOrderRow(string Number, string Supplier, string Project, decimal Value);
public record DailyTxnRow(long Serial, string Description, decimal Amount);
public record SafeBalanceRow(string Name, decimal Balance);

public class DailyReportVm
{
    public DateTime Date { get; set; }
    public List<DailyContractRow> Contracts { get; set; } = new();
    public List<DailyOrderRow> Orders { get; set; } = new();
    public List<DailyTxnRow> Incomes { get; set; } = new();
    public List<DailyTxnRow> Expenses { get; set; } = new();
    public List<SafeBalanceRow> Safes { get; set; } = new();

    public decimal ContractsTotal => Contracts.Sum(x => x.Value);
    public decimal OrdersTotal => Orders.Sum(x => x.Value);
    public decimal IncomeTotal => Incomes.Sum(x => x.Amount);
    public decimal ExpenseTotal => Expenses.Sum(x => x.Amount);
    public decimal NetCash => IncomeTotal - ExpenseTotal;
    public decimal SafesTotal => Safes.Sum(x => x.Balance);
}

// ---------- Customer report ----------
/// <summary>A customer's current account (its statement, all time): what it was charged (مدين), what settled it (دائن)
/// and the balance (+ = owed by them, مدين / − = owed to them, دائن). For a «عميل ومورد» party it is the merged statement —
/// both accounts, so the balance is the net.</summary>
public class CustomerReportRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance => Debit - Credit;
    /// <summary>«عميل ومورد»: also a supplier — its supplier account is included.</summary>
    public bool IsLinked { get; set; }
}

public class CustomerReportVm
{
    public List<CustomerReportRow> Rows { get; set; } = new();

    public decimal TotDebit => Rows.Sum(r => r.Debit);
    public decimal TotCredit => Rows.Sum(r => r.Credit);
    public decimal TotBalance => Rows.Sum(r => r.Balance);
}

// ---------- Projects report ----------
public class ProjectReportRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int UnitsTotal { get; set; }
    public int UnitsSold { get; set; }
    public int UnitsUnsold { get; set; }
    public decimal InventoryValue { get; set; }
    public decimal Expenses { get; set; }
    public decimal Incomes { get; set; }
    public decimal Net => Incomes - Expenses;
}

public class ProjectReportVm
{
    public List<ProjectReportRow> Rows { get; set; } = new();

    public int TotUnits => Rows.Sum(r => r.UnitsTotal);
    public int TotSold => Rows.Sum(r => r.UnitsSold);
    public int TotUnsold => Rows.Sum(r => r.UnitsUnsold);
    public decimal TotInventory => Rows.Sum(r => r.InventoryValue);
    public decimal TotExpenses => Rows.Sum(r => r.Expenses);
    public decimal TotIncomes => Rows.Sum(r => r.Incomes);
    public decimal TotNet => Rows.Sum(r => r.Net);
}

// ---------- Supplier report ----------
/// <summary>A supplier's current account (its statement, all time): what it is owed for (دائن), what settled it (مدين)
/// and the balance (+ = owed to them, دائن / − = owed by them, مدين). For a «عميل ومورد» party it is the merged statement —
/// both accounts, so the balance is the net.</summary>
public class SupplierReportRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public decimal Credit { get; set; }
    public decimal Debit { get; set; }
    public decimal Balance => Credit - Debit;
    /// <summary>«عميل ومورد»: also a customer — its customer account is included.</summary>
    public bool IsLinked { get; set; }
}

public class SupplierReportVm
{
    public List<SupplierReportRow> Rows { get; set; } = new();

    public decimal TotCredit => Rows.Sum(r => r.Credit);
    public decimal TotDebit => Rows.Sum(r => r.Debit);
    public decimal TotBalance => Rows.Sum(r => r.Balance);
}

// ---------- Sales profits report (أرباح المبيعات) ----------
/// <summary>
/// One sales invoice's profit: its value net of its returns, less the cost of what it took out of stock net of what its
/// returns brought back (the same figures the invoice page used to show).
/// </summary>
public class SalesProfitRow
{
    public Guid InvoiceId { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string Customer { get; set; } = "—";
    public decimal Value { get; set; }
    public decimal Returned { get; set; }
    public decimal Net => Value - Returned;
    /// <summary>Cost of sales: the posted goods issue's cost less the cost of the returned goods (costing method).</summary>
    public decimal Cost { get; set; }
    public decimal Profit => Net - Cost;
    public decimal MarginPct => Net > 0 ? Math.Round(Profit / Net * 100, 1) : 0;
}

public class SalesProfitsReportVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? CustomerId { get; set; }
    /// <summary>Invoice-number filter as typed (SI-… or digits; partial numbers match).</summary>
    public string? InvoiceNo { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Customers { get; set; } = new();
    public List<SalesProfitRow> Rows { get; set; } = new();

    public decimal TotValue => Rows.Sum(r => r.Value);
    public decimal TotReturned => Rows.Sum(r => r.Returned);
    public decimal TotNet => Rows.Sum(r => r.Net);
    public decimal TotCost => Rows.Sum(r => r.Cost);
    public decimal TotProfit => Rows.Sum(r => r.Profit);
    public decimal MarginPct => TotNet > 0 ? Math.Round(TotProfit / TotNet * 100, 1) : 0;
}
