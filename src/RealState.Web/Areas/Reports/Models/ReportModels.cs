namespace RealState.Web.Areas.Reports.Models;

// ---------- Daily report ----------
public record DailyContractRow(string Code, string Customer, string Unit, decimal Value);
public record DailyOrderRow(string Number, string Supplier, string Project, decimal Value);
public record DailyTxnRow(int Serial, string Description, decimal Amount);
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
public class CustomerReportRow
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int Contracts { get; set; }
    public decimal ContractsValue { get; set; }
    public int RemainingInstallments { get; set; }
    public decimal Collected { get; set; }
    public decimal Residual { get; set; }
}

public class CustomerReportVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<CustomerReportRow> Rows { get; set; } = new();

    public int TotContracts => Rows.Sum(r => r.Contracts);
    public decimal TotValue => Rows.Sum(r => r.ContractsValue);
    public int TotRemInst => Rows.Sum(r => r.RemainingInstallments);
    public decimal TotCollected => Rows.Sum(r => r.Collected);
    public decimal TotResidual => Rows.Sum(r => r.Residual);
}

// ---------- Supplier report ----------
public class SupplierReportRow
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public int Orders { get; set; }
    public decimal OrdersValue { get; set; }
    public decimal Paid { get; set; }
    public decimal Residual { get; set; }
}

public class SupplierReportVm
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SupplierReportRow> Rows { get; set; } = new();

    public int TotOrders => Rows.Sum(r => r.Orders);
    public decimal TotValue => Rows.Sum(r => r.OrdersValue);
    public decimal TotPaid => Rows.Sum(r => r.Paid);
    public decimal TotResidual => Rows.Sum(r => r.Residual);
}
