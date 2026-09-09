using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

public sealed record LedgerBackfillResult(int Safes, int Units, int Sales, int SupplierOrders, int WorkOrders, int Movements);

public interface ILedgerBackfillService
{
    /// <summary>Reconstructs journal entries for all existing records of the CURRENT tenant. Idempotent
    /// (skips records that already have an entry) and reuses the same posting logic as live actions.</summary>
    Task<LedgerBackfillResult> RunAsync(CancellationToken ct = default);
}

public class LedgerBackfillService : ILedgerBackfillService
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    private readonly IAccountingService _accounting;
    public LedgerBackfillService(IApplicationDbContext db, IAccountingEngine engine, IAccountingService accounting)
    {
        _db = db;
        _engine = engine;
        _accounting = accounting;
    }

    public async Task<LedgerBackfillResult> RunAsync(CancellationToken ct = default)
    {
        await _engine.EnsureChartAsync(ct);

        // Ensure an account exists for every user-defined expense/income category (بند).
        foreach (var cat in await _db.TxnCategories.Where(c => !c.IsBuiltIn).ToListAsync(ct))
            await _accounting.EnsureCategoryAccountAsync(cat, ct);

        var posted = (await _db.JournalEntries.Where(e => e.SourceId != null)
                .Select(e => new { e.SourceType, e.SourceId }).ToListAsync(ct))
            .Select(x => (x.SourceType, x.SourceId!.Value)).ToHashSet();
        bool Has(string type, Guid id) => posted.Contains((type, id));

        int safes = 0, units = 0, sales = 0, orders = 0, works = 0, moves = 0;

        // 1) Safe opening balances
        foreach (var s in await _db.Safes.ToListAsync(ct))
            if (s.InitialAmount != 0 && !Has("SafeOpening", s.Id)) { await _accounting.PostSafeOpeningBalanceAsync(s, ct); safes++; }

        // 2) Unit inventory (units carrying a cost)
        foreach (var u in await _db.ProjectUnits.Where(x => x.Cost > 0).ToListAsync(ct))
            if (!Has("UnitInventory", u.Id)) { await _accounting.SyncUnitInventoryAsync(u, ct); units++; }

        // 3) Sale contracts (A/R + revenue + COGS)
        foreach (var c in await _db.SaleContracts.ToListAsync(ct))
            if (!Has("SaleContract", c.Id)) { await _accounting.PostSaleContractAsync(c, ct); sales++; }

        // 4) Supplier orders (Dr المشتريات / Cr الموردون)
        var orderVals = (await _db.SupplierOrderItems.GroupBy(i => i.SupplierOrderId)
                .Select(g => new { g.Key, V = g.Sum(x => x.Cost * x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.V);
        foreach (var o in await _db.SupplierOrders.ToListAsync(ct))
            if (!Has("SupplierOrder", o.Id)) { await _accounting.SyncSupplierOrderAsync(o, orderVals.GetValueOrDefault(o.Id), ct); orders++; }

        // 5) Work orders (Dr أعمال المقاولات / Cr المقاولون at الإجمالي الفعلي)
        foreach (var o in await _db.WorkOrders.ToListAsync(ct))
            if (!Has("WorkOrder", o.Id)) { await _accounting.SyncWorkOrderAsync(o, ct); works++; }

        // 6) Cash movements — settlement entries with dimensions derived from the linked business records
        var instToContract = await _db.Installments.ToDictionaryAsync(i => i.Id, i => i.SaleContractId, ct);
        var contracts = await _db.SaleContracts.ToDictionaryAsync(c => c.Id, c => c, ct);
        var supPayByReceipt = (await _db.SupplierPayments.ToListAsync(ct)).GroupBy(p => p.ReceiptNo).ToDictionary(g => g.Key, g => g.First());
        var wopByReceipt = (await _db.WorkOrderPayments.ToListAsync(ct)).GroupBy(p => p.ReceiptNo).ToDictionary(g => g.Key, g => g.First());
        var advByExpTxn = (await _db.Advances.Where(a => a.ExpenseTxnId != null).ToListAsync(ct)).ToDictionary(a => a.ExpenseTxnId!.Value, a => a);
        var advEmp = await _db.Advances.ToDictionaryAsync(a => a.Id, a => a.EmployeeId, ct);
        var repByIncTxn = (await _db.AdvanceRepayments.Where(r => r.IncomeTxnId != null).ToListAsync(ct)).ToDictionary(r => r.IncomeTxnId!.Value, r => r);
        var rewByExpTxn = (await _db.Rewards.Where(r => r.ExpenseTxnId != null).ToListAsync(ct)).ToDictionary(r => r.ExpenseTxnId!.Value, r => r);

        foreach (var t in await _db.SafeTransactions.OrderBy(t => t.OccurredAt).ThenBy(t => t.Serial).ToListAsync(ct))
        {
            if (Has("SafeTransaction", t.Id)) continue;
            Guid? projectId = t.ProjectId, customerId = null, supplierId = null, contractorId = null, employeeId = null, unitId = null;
            switch (t.Source)
            {
                case TxnSource.Collection:
                    if (t.InstallmentId is Guid iid && instToContract.TryGetValue(iid, out var cid) && contracts.TryGetValue(cid, out var con))
                    { customerId = con.CustomerId; projectId ??= con.ProjectId; unitId = con.UnitId; }
                    break;
                case TxnSource.SupplierPayment:
                    if (supPayByReceipt.TryGetValue(t.Serial, out var sp)) supplierId = sp.SupplierId;
                    break;
                case TxnSource.ContractorPayment:
                    if (wopByReceipt.TryGetValue(t.Serial, out var wp)) contractorId = wp.ContractorId;
                    break;
                case TxnSource.AdvanceDisbursement:
                    if (advByExpTxn.TryGetValue(t.Id, out var adv)) employeeId = adv.EmployeeId;
                    break;
                case TxnSource.AdvanceRepayment:
                    if (repByIncTxn.TryGetValue(t.Id, out var rep) && advEmp.TryGetValue(rep.AdvanceId, out var eid)) employeeId = eid;
                    break;
                case TxnSource.RewardPayment:
                    if (rewByExpTxn.TryGetValue(t.Id, out var rw)) employeeId = rw.EmployeeId;
                    break;
            }
            await _accounting.PostCashSettlementAsync(t, projectId, customerId, supplierId, contractorId, employeeId, unitId, t.CategoryId, ct);
            moves++;
        }

        await _db.SaveChangesAsync(ct);
        return new LedgerBackfillResult(safes, units, sales, orders, works, moves);
    }
}
