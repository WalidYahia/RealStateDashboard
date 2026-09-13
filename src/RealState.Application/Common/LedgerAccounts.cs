using RealState.Application.Enums;

namespace RealState.Application.Common;

/// <summary>One row of the default chart of accounts used to seed a tenant.</summary>
public sealed record AccountDef(string Code, string Name, AccountType Type, string? ParentCode, bool IsPostable, string? SubKind = null);

/// <summary>
/// Well-known account codes referenced by the accounting engine, plus the default chart of accounts
/// seeded per tenant. Control accounts (A/R, A/P, advances, contractors, cash) are non-postable groups;
/// the engine posts to a per-entity subsidiary child created on demand (<see cref="AccountDef.SubKind"/>).
/// </summary>
public static class LedgerAccounts
{
    // --- control / parent accounts (non-postable) ---
    public const string CashAndBanks = "1100";          // safes are postable children (SubKind=Safe)
    public const string AccountsReceivable = "1200";    // customers (SubKind=Customer)
    public const string EmployeeAdvances = "1300";      // employees (SubKind=Employee)
    public const string AccountsPayable = "2100";       // suppliers (SubKind=Supplier)
    public const string ContractorsPayable = "2200";    // contractors (SubKind=Contractor)

    /// <summary>Goods Received Not Invoiced — the liability credited when stock is received before the supplier invoice.</summary>
    public const string GoodsReceivedNotInvoiced = "2300";

    // --- group parents that user-defined categories (بنود) hang under ---
    public const string RevenueGroup = "4000";
    public const string ExpensesGroup = "5000";

    // --- postable leaf accounts ---
    public const string RealEstateInventory = "1400";
    public const string GoodsInventory = "1410";        // control account; per-warehouse subsidiaries hang under it
    public const string OwnerCapital = "3100";
    public const string OpeningBalanceEquity = "3200";
    public const string SalesRevenue = "4100";
    public const string OtherRevenue = "4900";
    public const string CostOfSales = "5100";
    public const string ProjectCosts = "5200";
    public const string Purchases = "5300";
    public const string ContractingCosts = "5400";
    public const string SalariesAndRewards = "5500";
    public const string GeneralExpenses = "5900";

    /// <summary>Control accounts whose children are per-entity subsidiaries — added only from their own
    /// pages (customers / suppliers / contractors / safes / employees), never hand-added in the chart.</summary>
    public static readonly IReadOnlyList<string> SubsidiaryControls =
        new[] { CashAndBanks, AccountsReceivable, EmployeeAdvances, AccountsPayable, ContractorsPayable, GoodsInventory };

    public static readonly IReadOnlyList<AccountDef> Defaults = new List<AccountDef>
    {
        // Assets
        new("1000", "الأصول", AccountType.Asset, null, false),
        new(CashAndBanks, "النقدية والبنوك", AccountType.Asset, "1000", false),
        new(AccountsReceivable, "العملاء (ذمم مدينة)", AccountType.Asset, "1000", false),
        new(EmployeeAdvances, "سلف الموظفين", AccountType.Asset, "1000", false),
        new(RealEstateInventory, "مخزون العقارات", AccountType.Asset, "1000", true),
        new(GoodsInventory, "المخزون", AccountType.Asset, "1000", false),   // control; per-warehouse subsidiaries

        // Liabilities
        new("2000", "الخصوم", AccountType.Liability, null, false),
        new(AccountsPayable, "الموردون (ذمم دائنة)", AccountType.Liability, "2000", false),
        new(ContractorsPayable, "المقاولون (ذمم دائنة)", AccountType.Liability, "2000", false),
        new(GoodsReceivedNotInvoiced, "بضاعة واردة لم تُفوتر", AccountType.Liability, "2000", true),

        // Equity
        new("3000", "حقوق الملكية", AccountType.Equity, null, false),
        new(OwnerCapital, "رأس المال", AccountType.Equity, "3000", true),
        new(OpeningBalanceEquity, "رصيد افتتاحي", AccountType.Equity, "3000", true),

        // Revenue
        new("4000", "الإيرادات", AccountType.Revenue, null, false),
        new(SalesRevenue, "إيرادات المبيعات", AccountType.Revenue, "4000", true),
        new(OtherRevenue, "إيرادات أخرى", AccountType.Revenue, "4000", true),

        // Expenses
        new("5000", "المصروفات", AccountType.Expense, null, false),
        new(CostOfSales, "تكلفة المبيعات", AccountType.Expense, "5000", true),
        new(ProjectCosts, "مصروفات المشاريع", AccountType.Expense, "5000", true),
        new(Purchases, "المشتريات", AccountType.Expense, "5000", true),
        new(ContractingCosts, "أعمال المقاولات", AccountType.Expense, "5000", true),
        new(SalariesAndRewards, "رواتب ومكافآت", AccountType.Expense, "5000", true),
        new(GeneralExpenses, "مصروفات عامة", AccountType.Expense, "5000", true),
    };
}
