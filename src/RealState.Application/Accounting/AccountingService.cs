using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

public class AccountingService : IAccountingService
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    public AccountingService(IApplicationDbContext db, IAccountingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<SafeTransaction> AddTransactionAsync(
        Guid safeId, TxnType type, TxnSource source, decimal amount, DateTime occurredAt, string description,
        Guid? installmentId = null, Guid? stageExpenseId = null, Guid? projectId = null,
        Guid? customerId = null, Guid? supplierId = null, Guid? contractorId = null, Guid? employeeId = null,
        Guid? unitId = null, Guid? categoryId = null, CancellationToken ct = default)
    {
        // Serial is year-prefixed and resets each year, per transaction type (e.g. 2026 + 00001 = 202600001).
        var yearBase = occurredAt.Year * 100000;
        var maxThisYear = await _db.SafeTransactions
            .Where(t => t.Type == type && t.Serial >= yearBase && t.Serial < yearBase + 100000)
            .MaxAsync(t => (int?)t.Serial, ct) ?? yearBase;
        var serial = maxThisYear + 1;
        var txn = new SafeTransaction
        {
            SafeId = safeId,
            Type = type,
            Source = source,
            Serial = serial,
            Amount = amount,
            OccurredAt = occurredAt,
            Description = description,
            InstallmentId = installmentId,
            StageExpenseId = stageExpenseId,
            ProjectId = projectId
        };
        _db.SafeTransactions.Add(txn);

        // Collections carry the customer via the installment's contract.
        if (source == TxnSource.Collection && customerId is null && installmentId is Guid iid)
            customerId = await _db.Installments.Where(i => i.Id == iid)
                .Join(_db.SaleContracts, i => i.SaleContractId, c => c.Id, (i, c) => (Guid?)c.CustomerId)
                .FirstOrDefaultAsync(ct);

        await PostCashSettlementAsync(txn, projectId, customerId, supplierId, contractorId, employeeId, unitId, categoryId, ct);

        return txn;
    }

    /// <summary>
    /// Posts the balanced journal entry for a cash movement: the safe's cash account on one side and the
    /// source-specific counter account on the other. Income debits cash / credits the counter; expense
    /// credits cash / debits the counter. Reused by the one-time backfill for existing movements.
    /// </summary>
    public async Task PostCashSettlementAsync(
        SafeTransaction txn,
        Guid? projectId = null, Guid? customerId = null, Guid? supplierId = null, Guid? contractorId = null, Guid? employeeId = null, Guid? unitId = null,
        Guid? categoryId = null, CancellationToken ct = default)
    {
        var description = txn.Description;
        var safeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);

        // The counter (non-cash) side, chosen by source.
        var counter = await BuildCounterAsync(txn.Source, txn, projectId, customerId, supplierId, contractorId, employeeId, unitId, categoryId, ct);

        var cash = new LedgerLine(LedgerAccounts.CashAndBanks, 0, 0, description,
            SubKind: "Safe", SubRefId: txn.SafeId, SubName: safeName, ProjectId: projectId);

        LedgerLine cashLine, counterLine;
        if (txn.Type == TxnType.Income)
        {
            cashLine = cash with { Debit = txn.Amount };
            counterLine = counter with { Credit = txn.Amount };
        }
        else
        {
            cashLine = cash with { Credit = txn.Amount };
            counterLine = counter with { Debit = txn.Amount };
        }

        await _engine.PostAsync(txn.OccurredAt, description, "SafeTransaction", txn.Id,
            new[] { counterLine, cashLine }, ct);
    }

    private async Task<LedgerLine> BuildCounterAsync(
        TxnSource source, SafeTransaction txn,
        Guid? projectId, Guid? customerId, Guid? supplierId, Guid? contractorId, Guid? employeeId, Guid? unitId, Guid? categoryId,
        CancellationToken ct)
    {
        async Task<string?> NameAsync(string kind, Guid id) => kind switch
        {
            "Customer" => await _db.Customers.Where(c => c.Id == id).Select(c => c.FullName).FirstOrDefaultAsync(ct),
            "Supplier" => await _db.Suppliers.Where(s => s.Id == id).Select(s => s.Name).FirstOrDefaultAsync(ct),
            "Contractor" => await _db.Contractors.Where(c => c.Id == id).Select(c => c.Name).FirstOrDefaultAsync(ct),
            "Employee" => await _db.Employees.Where(e => e.Id == id).Select(e => e.FullName).FirstOrDefaultAsync(ct),
            _ => null
        };

        switch (source)
        {
            case TxnSource.Collection when customerId is Guid cust:
                // Cash receipt settles the customer's receivable.
                return new LedgerLine(LedgerAccounts.AccountsReceivable, 0, 0,
                    SubKind: "Customer", SubRefId: cust, SubName: await NameAsync("Customer", cust), CustomerId: cust);

            case TxnSource.SupplierPayment when supplierId is Guid sup:
                return new LedgerLine(LedgerAccounts.AccountsPayable, 0, 0,
                    SubKind: "Supplier", SubRefId: sup, SubName: await NameAsync("Supplier", sup), SupplierId: sup);

            case TxnSource.ContractorPayment when contractorId is Guid con:
                return new LedgerLine(LedgerAccounts.ContractorsPayable, 0, 0,
                    SubKind: "Contractor", SubRefId: con, SubName: await NameAsync("Contractor", con), ContractorId: con);

            case TxnSource.AdvanceDisbursement when employeeId is not null:
            case TxnSource.AdvanceRepayment when employeeId is not null:
            {
                var empId = employeeId.Value;
                return new LedgerLine(LedgerAccounts.EmployeeAdvances, 0, 0,
                    SubKind: "Employee", SubRefId: empId, SubName: await NameAsync("Employee", empId), EmployeeId: empId);
            }

            case TxnSource.RewardPayment:
                return new LedgerLine(LedgerAccounts.SalariesAndRewards, 0, 0, EmployeeId: employeeId);

            case TxnSource.ProjectExpense:
                return new LedgerLine(LedgerAccounts.ProjectCosts, 0, 0, ProjectId: projectId);

            // Manual income / expense, or any source missing its dimension: post to the chosen category (بند)
            // account when it's a user-defined one, else a generic P&L account.
            default:
                if (categoryId is Guid catId)
                {
                    var cat = await _db.TxnCategories.FirstOrDefaultAsync(c => c.Id == catId, ct);
                    if (cat is not null && !cat.IsBuiltIn)
                    {
                        var groupCode = txn.Type == TxnType.Income ? LedgerAccounts.RevenueGroup : LedgerAccounts.ExpensesGroup;
                        return new LedgerLine(groupCode, 0, 0, SubKind: "TxnCategory", SubRefId: catId, SubName: cat.Name, ProjectId: projectId);
                    }
                }
                return txn.Type == TxnType.Income
                    ? new LedgerLine(LedgerAccounts.OtherRevenue, 0, 0, ProjectId: projectId)
                    : new LedgerLine(LedgerAccounts.GeneralExpenses, 0, 0, ProjectId: projectId);
        }
    }

    public async Task EnsureCategoryAccountAsync(TxnCategory category, CancellationToken ct = default)
    {
        if (category.IsBuiltIn) return;   // built-in kinds (عام/سلفة/مكافأة) map to existing accounts
        var groupCode = category.Type == TxnType.Income ? LedgerAccounts.RevenueGroup : LedgerAccounts.ExpensesGroup;
        var acc = await _engine.ResolveAccountAsync(groupCode, "TxnCategory", category.Id, category.Name, ct);
        if (acc.Name != category.Name) acc.Name = category.Name;   // keep in sync on rename
    }

    public async Task RemoveByInstallmentAsync(Guid installmentId, CancellationToken ct = default)
    {
        var linked = await _db.SafeTransactions.Where(t => t.InstallmentId == installmentId).ToListAsync(ct);
        foreach (var t in linked) await RemoveTransactionAsync(t, ct);
    }

    public async Task RemoveTransactionAsync(SafeTransaction txn, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync("SafeTransaction", txn.Id, ct);
        _db.SafeTransactions.Remove(txn);
    }

    public async Task PostSaleContractAsync(SaleContract c, CancellationToken ct = default)
    {
        if (c.TotalPrice <= 0) return;
        var custName = await _db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
        var unitCost = await _db.ProjectUnits.Where(u => u.Id == c.UnitId).Select(u => u.Cost).FirstOrDefaultAsync(ct);
        var desc = $"عقد بيع {c.Code}";

        var lines = new List<LedgerLine>
        {
            new(LedgerAccounts.AccountsReceivable, c.TotalPrice, 0, desc,
                SubKind: "Customer", SubRefId: c.CustomerId, SubName: custName,
                CustomerId: c.CustomerId, ProjectId: c.ProjectId, UnitId: c.UnitId),
            new(LedgerAccounts.SalesRevenue, 0, c.TotalPrice, desc,
                CustomerId: c.CustomerId, ProjectId: c.ProjectId, UnitId: c.UnitId),
        };
        // Relieve the sold unit's inventory to cost of sales.
        if (unitCost > 0)
        {
            lines.Add(new LedgerLine(LedgerAccounts.CostOfSales, unitCost, 0, desc,
                CustomerId: c.CustomerId, ProjectId: c.ProjectId, UnitId: c.UnitId));
            lines.Add(new LedgerLine(LedgerAccounts.RealEstateInventory, 0, unitCost, desc,
                ProjectId: c.ProjectId, UnitId: c.UnitId));
        }
        await _engine.PostAsync(c.ContractDate, desc, "SaleContract", c.Id, lines, ct);
    }

    public async Task SyncUnitInventoryAsync(ProjectUnit unit, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync("UnitInventory", unit.Id, ct);
        if (unit.Cost <= 0) return;
        var desc = $"مخزون وحدة {unit.Name}";
        var date = unit.CreatedAt == default ? DateTime.Today : unit.CreatedAt;
        await _engine.PostAsync(date, desc, "UnitInventory", unit.Id, new[]
        {
            new LedgerLine(LedgerAccounts.RealEstateInventory, unit.Cost, 0, desc, ProjectId: unit.ProjectId, UnitId: unit.Id),
            new LedgerLine(LedgerAccounts.OpeningBalanceEquity, 0, unit.Cost, desc, ProjectId: unit.ProjectId, UnitId: unit.Id),
        }, ct);
    }

    public async Task SyncSupplierOrderAsync(SupplierOrder o, decimal orderValue, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync("SupplierOrder", o.Id, ct);
        if (orderValue <= 0) return;
        var supName = await _db.Suppliers.Where(s => s.Id == o.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        var desc = $"أمر توريد PO-{o.Number:D4}";
        await _engine.PostAsync(o.OrderDate, desc, "SupplierOrder", o.Id, new[]
        {
            new LedgerLine(LedgerAccounts.Purchases, orderValue, 0, desc, SupplierId: o.SupplierId, ProjectId: o.ProjectId),
            new LedgerLine(LedgerAccounts.AccountsPayable, 0, orderValue, desc,
                SubKind: "Supplier", SubRefId: o.SupplierId, SubName: supName, SupplierId: o.SupplierId, ProjectId: o.ProjectId),
        }, ct);
    }

    public async Task SyncWorkOrderAsync(WorkOrder o, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync("WorkOrder", o.Id, ct);
        var amount = o.ActualTotal;
        if (amount <= 0) return;
        var conName = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var desc = $"أمر شغل WO-{o.Number}";
        await _engine.PostAsync(o.OrderDate, desc, "WorkOrder", o.Id, new[]
        {
            new LedgerLine(LedgerAccounts.ContractingCosts, amount, 0, desc, ContractorId: o.ContractorId, ProjectId: o.ProjectId),
            new LedgerLine(LedgerAccounts.ContractorsPayable, 0, amount, desc,
                SubKind: "Contractor", SubRefId: o.ContractorId, SubName: conName, ContractorId: o.ContractorId, ProjectId: o.ProjectId),
        }, ct);
    }

    public Task RemoveObligationAsync(string sourceType, Guid sourceId, CancellationToken ct = default)
        => _engine.RemoveBySourceAsync(sourceType, sourceId, ct);

    public async Task PostSafeOpeningBalanceAsync(Safe safe, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync("SafeOpening", safe.Id, ct);
        if (safe.InitialAmount == 0) return;
        var desc = $"رصيد افتتاحي للخزنة {safe.Name}";
        var abs = Math.Abs(safe.InitialAmount);
        var positive = safe.InitialAmount > 0;
        var date = safe.CreatedAt == default ? DateTime.Today : safe.CreatedAt;
        await _engine.PostAsync(date, desc, "SafeOpening", safe.Id, new[]
        {
            new LedgerLine(LedgerAccounts.CashAndBanks, positive ? abs : 0, positive ? 0 : abs, desc,
                SubKind: "Safe", SubRefId: safe.Id, SubName: safe.Name),
            new LedgerLine(LedgerAccounts.OpeningBalanceEquity, positive ? 0 : abs, positive ? abs : 0, desc),
        }, ct);
    }
}
