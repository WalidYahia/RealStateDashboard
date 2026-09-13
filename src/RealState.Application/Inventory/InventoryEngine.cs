using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Inventory;

public class InventoryEngine : IInventoryEngine
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _accounting;
    public InventoryEngine(IApplicationDbContext db, IAccountingEngine accounting) { _db = db; _accounting = accounting; }

    // ================================ defaults / chart ================================

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var changed = false;
        await _accounting.EnsureChartAsync(ct);

        // Inventory control account (non-postable group). Each warehouse gets its own subsidiary child
        // under it, so the chart shows one inventory account per warehouse.
        var inv = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == LedgerAccounts.GoodsInventory, ct);
        if (inv is null)
        {
            var parent = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "1000", ct);
            _db.Accounts.Add(new Account
            {
                Code = LedgerAccounts.GoodsInventory, Name = "المخزون", Type = AccountType.Asset,
                ParentId = parent?.Id, IsPostable = false, IsActive = true, SortOrder = 50
            });
            changed = true;
        }
        else if (inv.IsPostable)   // migrate an earlier postable 1410 into a control group
        {
            inv.IsPostable = false; changed = true;
        }

        // Goods-Received-Not-Invoiced liability (credited by receipts until a supplier invoice is wired).
        if (!await _db.Accounts.AnyAsync(a => a.Code == LedgerAccounts.GoodsReceivedNotInvoiced, ct))
        {
            var liab = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == "2000", ct);
            _db.Accounts.Add(new Account
            {
                Code = LedgerAccounts.GoodsReceivedNotInvoiced, Name = "بضاعة واردة لم تُفوتر", Type = AccountType.Liability,
                ParentId = liab?.Id, IsPostable = true, IsActive = true, SortOrder = 30
            });
            changed = true;
        }

        var profile = await _db.InventoryPostingProfiles.FirstOrDefaultAsync(ct);
        if (profile is null)
        {
            _db.InventoryPostingProfiles.Add(new InventoryPostingProfile
            {
                CostingMethod = CostingMethod.WeightedAverage,
                InventoryCode = LedgerAccounts.GoodsInventory,                 // control; posts to per-warehouse child
                CogsCode = LedgerAccounts.CostOfSales,                         // 5100
                PurchaseGrniCode = LedgerAccounts.GoodsReceivedNotInvoiced,    // 2300 (liability, not an expense)
                AdjustmentGainCode = LedgerAccounts.OtherRevenue,              // 4900
                AdjustmentLossCode = LedgerAccounts.GeneralExpenses,           // 5900
                SalesRevenueCode = LedgerAccounts.SalesRevenue,                // reserved for sales integration
                ConsumptionExpenseCode = LedgerAccounts.GeneralExpenses
            });
            changed = true;
        }
        else if (profile.PurchaseGrniCode == LedgerAccounts.Purchases)
        {
            // Correct earlier profiles that credited an EXPENSE account on receipt.
            profile.PurchaseGrniCode = LedgerAccounts.GoodsReceivedNotInvoiced;
            changed = true;
        }

        if (!await _db.Warehouses.AnyAsync(ct))
        {
            var main = new Warehouse { Code = "MAIN", Name = "المخزن الرئيسي", IsActive = true, IsDefault = true };
            _db.Warehouses.Add(main);
            await EnsureWarehouseAccountAsync(main, ct);
            changed = true;
        }

        if (!await _db.UnitsOfMeasure.AnyAsync(ct))
        {
            _db.UnitsOfMeasure.AddRange(
                new UnitOfMeasure { Code = "PCS", Name = "قطعة", IsActive = true },
                new UnitOfMeasure { Code = "KG", Name = "كيلوجرام", IsActive = true },
                new UnitOfMeasure { Code = "M", Name = "متر", IsActive = true },
                new UnitOfMeasure { Code = "M2", Name = "متر مربع", IsActive = true });
            changed = true;
        }

        // Backfill any warehouse that has no inventory account yet (cheap count probe first).
        var whCount = await _db.Warehouses.CountAsync(ct);
        var accCount = await _db.Accounts.CountAsync(a => a.SubKind == InventorySources.WarehouseSubKind, ct);
        if (whCount > accCount)
        {
            var have = await _db.Accounts.Where(a => a.SubKind == InventorySources.WarehouseSubKind && a.SubRefId != null)
                .Select(a => a.SubRefId!.Value).ToListAsync(ct);
            foreach (var wh in await _db.Warehouses.Where(w => !have.Contains(w.Id)).ToListAsync(ct))
            {
                await EnsureWarehouseAccountAsync(wh, ct);
                changed = true;
            }
        }

        if (changed) await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureWarehouseAccountAsync(Warehouse warehouse, CancellationToken ct = default)
    {
        var acc = await _accounting.ResolveAccountAsync(
            LedgerAccounts.GoodsInventory, InventorySources.WarehouseSubKind, warehouse.Id, $"مخزون - {warehouse.Name}", ct);
        var name = $"مخزون - {warehouse.Name}";
        if (acc.Name != name) acc.Name = name;   // only touch it on rename, so we don't force a pointless UPDATE
    }

    // ================================ stock / valuation ================================

    public async Task<StockLevel> GetStockAsync(Guid productId, Guid warehouseId, DateTime? asOf = null, CancellationToken ct = default)
    {
        var q = _db.InventoryMovements.Where(m => m.ProductId == productId && m.WarehouseId == warehouseId);
        if (asOf is DateTime d) q = q.Where(m => m.Date < d.Date.AddDays(1));

        // Outflow is decided by the quantity direction (not the movement type), so reversing movements
        // and any future movement type sign correctly with no extra rules.
        var qty = await q.SumAsync(m => (decimal?)(m.QuantityIn - m.QuantityOut), ct) ?? 0m;
        var value = await q.SumAsync(m => (decimal?)(m.QuantityOut > 0 ? -m.TotalCost : m.TotalCost), ct) ?? 0m;
        var avg = qty > 0 ? Math.Round(value / qty, 4) : 0m;
        return new StockLevel(qty, value, avg);
    }

    public async Task<int> NextNumberAsync(IQueryable<int> existingNumbers, IEnumerable<int> localNumbers, int year, CancellationToken ct = default)
    {
        var yearBase = year * 1_000_000;                         // e.g. 2026 -> 2026000000
        var maxDb = await existingNumbers
            .Where(n => n >= yearBase && n < yearBase + 1_000_000)
            .MaxAsync(n => (int?)n, ct) ?? yearBase;
        var maxLocal = localNumbers                              // consider not-yet-saved documents too
            .Where(n => n >= yearBase && n < yearBase + 1_000_000)
            .DefaultIfEmpty(yearBase).Max();
        return Math.Max(maxDb, maxLocal) + 1;                    // -> 2026000001, 2026000002, ...
    }

    // ================================ document posting ================================

    public async Task PostReceiptAsync(GoodsReceipt doc, CancellationToken ct = default)
    {
        var p = await ProfileAsync(ct);
        RequireLines(doc.Lines.Count);
        RequireUniqueProducts(doc.Lines.Select(l => l.ProductId));
        await GuardDateAsync(doc.Date, doc.Lines.Select(l => l.ProductId), new[] { doc.WarehouseId }, ct);

        decimal total = 0m;
        foreach (var l in doc.Lines)
        {
            RequirePositive(l.Quantity);
            if (l.UnitCost <= 0)
                throw new InvalidOperationException("أدخل تكلفة وحدة أكبر من صفر لكل صنف — بدونها لا يُنشأ قيد.");
            var cost = Math.Round(l.Quantity * l.UnitCost, 2);
            l.TotalCost = cost; total += cost;
            AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.Receipt,
                InventorySources.GoodsReceipt, doc.Id, doc.Number, l.Quantity, 0, l.UnitCost, cost, doc.Notes);
        }

        var wh = await WhNameAsync(doc.WarehouseId, ct);
        await _accounting.PostAsync(doc.Date, $"إذن استلام {doc.Number}", InventorySources.GoodsReceipt, doc.Id, new[]
        {
            InvLine(p.InventoryCode, doc.WarehouseId, wh, total, 0, "استلام مخزون"),
            new LedgerLine(p.PurchaseGrniCode, 0, total, "بضاعة واردة لم تُفوتر")
        }, ct);
        doc.Status = InventoryDocStatus.Posted;
    }

    public async Task PostIssueAsync(GoodsIssue doc, CancellationToken ct = default)
    {
        var p = await ProfileAsync(ct);
        RequireLines(doc.Lines.Count);
        RequireUniqueProducts(doc.Lines.Select(l => l.ProductId));
        await GuardDateAsync(doc.Date, doc.Lines.Select(l => l.ProductId), new[] { doc.WarehouseId }, ct);

        decimal total = 0m;
        foreach (var l in doc.Lines)
        {
            RequirePositive(l.Quantity);
            var stock = await GetStockAsync(l.ProductId, doc.WarehouseId, doc.Date, ct);
            RequireAvailable(l.Quantity, stock.Quantity, "صرفها");
            var cost = OutflowCost(stock, l.Quantity);
            l.UnitCost = stock.AverageCost; l.TotalCost = cost; total += cost;
            AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.Issue,
                InventorySources.GoodsIssue, doc.Id, doc.Number, 0, l.Quantity, stock.AverageCost, cost, doc.Notes);
        }

        var counter = doc.Reason switch
        {
            IssueReason.Sale => p.CogsCode,
            IssueReason.Damage => p.AdjustmentLossCode,
            _ => p.ConsumptionExpenseCode
        };
        if (total > 0)
        {
            var wh = await WhNameAsync(doc.WarehouseId, ct);
            await _accounting.PostAsync(doc.Date, $"إذن صرف {doc.Number}", InventorySources.GoodsIssue, doc.Id, new[]
            {
                new LedgerLine(counter, total, 0, "تكلفة الصرف"),
                InvLine(p.InventoryCode, doc.WarehouseId, wh, 0, total, "من المخزون")
            }, ct);
        }
        doc.Status = InventoryDocStatus.Posted;
    }

    public async Task PostTransferAsync(StockTransfer doc, CancellationToken ct = default)
    {
        var p = await ProfileAsync(ct);
        if (doc.FromWarehouseId == doc.ToWarehouseId)
            throw new InvalidOperationException("لا يمكن التحويل إلى نفس المخزن.");
        RequireLines(doc.Lines.Count);
        RequireUniqueProducts(doc.Lines.Select(l => l.ProductId));
        await GuardDateAsync(doc.Date, doc.Lines.Select(l => l.ProductId), new[] { doc.FromWarehouseId, doc.ToWarehouseId }, ct);

        decimal total = 0m;
        foreach (var l in doc.Lines)
        {
            RequirePositive(l.Quantity);
            var stock = await GetStockAsync(l.ProductId, doc.FromWarehouseId, doc.Date, ct);
            RequireAvailable(l.Quantity, stock.Quantity, "تحويلها");
            var cost = OutflowCost(stock, l.Quantity);
            l.UnitCost = stock.AverageCost; l.TotalCost = cost; total += cost;
            AddMovement(l.ProductId, doc.FromWarehouseId, doc.Date, InventoryMovementType.TransferOut,
                InventorySources.StockTransfer, doc.Id, doc.Number, 0, l.Quantity, stock.AverageCost, cost, doc.Notes);
            AddMovement(l.ProductId, doc.ToWarehouseId, doc.Date, InventoryMovementType.TransferIn,
                InventorySources.StockTransfer, doc.Id, doc.Number, l.Quantity, 0, stock.AverageCost, cost, doc.Notes);
        }

        // One inventory account per warehouse → a transfer reclassifies value between the two accounts
        // (a balance-sheet move, no P&L): Dr Inventory(destination) / Cr Inventory(source).
        if (total > 0)
        {
            var fromName = await WhNameAsync(doc.FromWarehouseId, ct);
            var toName = await WhNameAsync(doc.ToWarehouseId, ct);
            await _accounting.PostAsync(doc.Date, $"تحويل مخزني {doc.Number}", InventorySources.StockTransfer, doc.Id, new[]
            {
                InvLine(p.InventoryCode, doc.ToWarehouseId, toName, total, 0, "وارد تحويل"),
                InvLine(p.InventoryCode, doc.FromWarehouseId, fromName, 0, total, "صادر تحويل")
            }, ct);
        }
        doc.Status = InventoryDocStatus.Posted;
    }

    public async Task PostAdjustmentAsync(InventoryAdjustment doc, CancellationToken ct = default)
    {
        var p = await ProfileAsync(ct);
        var effective = doc.Lines.Where(l => l.QuantityDelta != 0).ToList();
        RequireLines(effective.Count);
        RequireUniqueProducts(effective.Select(l => l.ProductId));
        await GuardDateAsync(doc.Date, effective.Select(l => l.ProductId), new[] { doc.WarehouseId }, ct);

        var lines = new List<LedgerLine>();
        decimal inc = 0m, dec = 0m;
        foreach (var l in effective)
        {
            if (l.QuantityDelta > 0)
            {
                if (l.UnitCost <= 0)
                    throw new InvalidOperationException("أدخل تكلفة الوحدة للأصناف ذات الزيادة (كمية موجبة) — بدونها لا يُنشأ قيد.");
                var cost = Math.Round(l.QuantityDelta * l.UnitCost, 2);
                l.TotalCost = cost; inc += cost;
                AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.AdjustmentIn,
                    InventorySources.InventoryAdjustment, doc.Id, doc.Number, l.QuantityDelta, 0, l.UnitCost, cost, doc.Notes);
            }
            else
            {
                var qty = -l.QuantityDelta;
                var stock = await GetStockAsync(l.ProductId, doc.WarehouseId, doc.Date, ct);
                RequireAvailable(qty, stock.Quantity, "إنقاصها");
                var cost = OutflowCost(stock, qty);
                l.UnitCost = stock.AverageCost; l.TotalCost = cost; dec += cost;
                AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.AdjustmentOut,
                    InventorySources.InventoryAdjustment, doc.Id, doc.Number, 0, qty, stock.AverageCost, cost, doc.Notes);
            }
        }

        var whA = await WhNameAsync(doc.WarehouseId, ct);
        var gainAccount = doc.Reason == AdjustmentReason.Opening ? LedgerAccounts.OpeningBalanceEquity : p.AdjustmentGainCode;
        if (inc > 0) { lines.Add(InvLine(p.InventoryCode, doc.WarehouseId, whA, inc, 0, "زيادة مخزون")); lines.Add(new LedgerLine(gainAccount, 0, inc, "مقابل الزيادة")); }
        if (dec > 0) { lines.Add(new LedgerLine(p.AdjustmentLossCode, dec, 0, "عجز/خسارة مخزون")); lines.Add(InvLine(p.InventoryCode, doc.WarehouseId, whA, 0, dec, "من المخزون")); }
        if (lines.Count > 0)
            await _accounting.PostAsync(doc.Date, $"تسوية مخزون {doc.Number}", InventorySources.InventoryAdjustment, doc.Id, lines, ct);
        doc.Status = InventoryDocStatus.Posted;
    }

    public async Task PostStockCountAsync(StockCount doc, CancellationToken ct = default)
    {
        var p = await ProfileAsync(ct);
        RequireLines(doc.Lines.Count);
        RequireUniqueProducts(doc.Lines.Select(l => l.ProductId));
        await GuardDateAsync(doc.Date, doc.Lines.Select(l => l.ProductId), new[] { doc.WarehouseId }, ct);

        var lines = new List<LedgerLine>();
        decimal inc = 0m, dec = 0m;
        foreach (var l in doc.Lines)
        {
            if (l.CountedQty < 0) throw new InvalidOperationException("الكمية المجرودة لا يمكن أن تكون سالبة.");
            var stock = await GetStockAsync(l.ProductId, doc.WarehouseId, doc.Date, ct);

            // Re-read the book quantity at POST time — the draft snapshot may be stale, and the count
            // must land the stock exactly on the counted quantity.
            l.SystemQty = stock.Quantity;
            var delta = l.CountedQty - stock.Quantity;
            if (delta == 0) continue;

            if (delta > 0)
            {
                if (stock.AverageCost <= 0)
                    throw new InvalidOperationException("لا توجد تكلفة معروفة لتقييم الزيادة في الجرد — استخدم «تسوية مخزون» وحدّد تكلفة الوحدة.");
                var cost = Math.Round(delta * stock.AverageCost, 2); inc += cost;
                AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.AdjustmentIn,
                    InventorySources.StockCount, doc.Id, doc.Number, delta, 0, stock.AverageCost, cost, doc.Notes);
            }
            else
            {
                var qty = -delta;
                var cost = OutflowCost(stock, qty); dec += cost;
                AddMovement(l.ProductId, doc.WarehouseId, doc.Date, InventoryMovementType.AdjustmentOut,
                    InventorySources.StockCount, doc.Id, doc.Number, 0, qty, stock.AverageCost, cost, doc.Notes);
            }
        }

        var whC = await WhNameAsync(doc.WarehouseId, ct);
        if (inc > 0) { lines.Add(InvLine(p.InventoryCode, doc.WarehouseId, whC, inc, 0, "زيادة جرد")); lines.Add(new LedgerLine(p.AdjustmentGainCode, 0, inc, "مقابل الزيادة")); }
        if (dec > 0) { lines.Add(new LedgerLine(p.AdjustmentLossCode, dec, 0, "عجز جرد")); lines.Add(InvLine(p.InventoryCode, doc.WarehouseId, whC, 0, dec, "من المخزون")); }
        if (lines.Count > 0)
            await _accounting.PostAsync(doc.Date, $"جرد {doc.Number}", InventorySources.StockCount, doc.Id, lines, ct);
        doc.Status = InventoryDocStatus.Posted;
    }

    /// <summary>
    /// Reverses a posted document by ADDING counter-movements and a reversing journal entry — the
    /// original movements and entry are kept, so the audit trail is never erased.
    /// </summary>
    public async Task ReverseAsync(string sourceType, Guid sourceId, CancellationToken ct = default)
    {
        var originals = await _db.InventoryMovements
            .Where(m => m.ReferenceType == sourceType && m.ReferenceId == sourceId && !m.IsReversal).ToListAsync(ct);

        // Reversing an inflow takes stock back out — make sure that doesn't drive any balance negative.
        foreach (var g in originals.GroupBy(m => new { m.ProductId, m.WarehouseId }))
        {
            var net = g.Sum(m => m.QuantityIn - m.QuantityOut);   // stock this document added
            if (net <= 0) continue;
            var stock = await GetStockAsync(g.Key.ProductId, g.Key.WarehouseId, null, ct);
            if (net > stock.Quantity)
                throw new InvalidOperationException(
                    $"لا يمكن عكس المستند: الكمية المطلوب سحبها ({net:N2}) أكبر من المتاح حاليًا ({stock.Quantity:N2}) — تم صرفها أو تحويلها بالفعل.");
        }

        var today = DateTime.Today;
        foreach (var m in originals)
            _db.InventoryMovements.Add(new InventoryMovement
            {
                ProductId = m.ProductId, WarehouseId = m.WarehouseId, Date = today, MovementType = m.MovementType,
                ReferenceType = sourceType, ReferenceId = sourceId, ReferenceNumber = m.ReferenceNumber, IsReversal = true,
                QuantityIn = m.QuantityOut, QuantityOut = m.QuantityIn,   // swapped
                UnitCost = m.UnitCost, TotalCost = m.TotalCost, Memo = "عكس مستند"
            });

        // Post a mirrored journal entry (Dr/Cr swapped) instead of deleting the original.
        var reversalType = ReversalSourceType(sourceType);
        var entries = await _db.JournalEntries
            .Where(e => e.SourceType == sourceType && e.SourceId == sourceId).Select(e => new { e.Id, e.Description }).ToListAsync(ct);
        foreach (var e in entries)
        {
            var els = await _db.JournalLines.Where(l => l.JournalEntryId == e.Id)
                .Select(l => new { l.AccountId, l.Debit, l.Credit, l.Memo }).ToListAsync(ct);
            if (els.Count < 2) continue;
            await _accounting.PostByIdsAsync(today, $"عكس {e.Description}", reversalType,
                els.Select(l => (l.AccountId, l.Credit, l.Debit, l.Memo)).ToList(), ct, sourceId);
        }
    }

    /// <summary>Source-type tag used on the reversing journal entry of a document.</summary>
    public static string ReversalSourceType(string sourceType) => sourceType + ":Reversal";

    // -------------------------------- helpers --------------------------------

    private async Task<InventoryPostingProfile> ProfileAsync(CancellationToken ct)
        // NOTE: does NOT seed/commit — callers run EnsureDefaultsAsync first so posting stays one unit of work.
        => await _db.InventoryPostingProfiles.FirstOrDefaultAsync(ct)
           ?? throw new InvalidOperationException("لم يتم تهيئة إعدادات المخزون بعد — افتح صفحة المخزون أولًا.");

    private static void RequireLines(int count)
    {
        if (count == 0) throw new InvalidOperationException("المستند لا يحتوي على أسطر فعّالة.");
    }

    private static void RequirePositive(decimal qty)
    {
        if (qty <= 0) throw new InvalidOperationException("يجب أن تكون الكمية أكبر من صفر.");
    }

    private static void RequireAvailable(decimal wanted, decimal available, string verb)
    {
        if (wanted > available)
            throw new InvalidOperationException($"الكمية المطلوب {verb} ({wanted:N2}) أكبر من المتاح ({available:N2}).");
    }

    /// <summary>A product may appear once per document, so each line's availability check is exact.</summary>
    private static void RequireUniqueProducts(IEnumerable<Guid> productIds)
    {
        if (productIds.GroupBy(id => id).Any(g => g.Count() > 1))
            throw new InvalidOperationException("لا يمكن تكرار نفس الصنف في أكثر من سطر داخل المستند — ادمج الأسطر في سطر واحد.");
    }

    /// <summary>
    /// Cost of an outflow under weighted average. Taking the whole remaining quantity consumes the whole
    /// remaining value, so rounding residue can never be left behind on a zero-quantity item.
    /// </summary>
    private static decimal OutflowCost(StockLevel stock, decimal qty)
        => qty >= stock.Quantity ? Math.Round(stock.Value, 2) : Math.Round(qty * stock.AverageCost, 2);

    /// <summary>
    /// Weighted average has no history rewrite: a document dated before existing movements would cost
    /// those movements wrongly, so future dates and back-dating behind existing movements are refused.
    /// </summary>
    private async Task GuardDateAsync(DateTime date, IEnumerable<Guid> productIds, IReadOnlyList<Guid> warehouseIds, CancellationToken ct)
    {
        if (date.Date > DateTime.Today)
            throw new InvalidOperationException("لا يمكن ترحيل مستند بتاريخ مستقبلي.");

        var ids = productIds.Distinct().ToList();
        var hasLater = await _db.InventoryMovements
            .AnyAsync(m => ids.Contains(m.ProductId) && warehouseIds.Contains(m.WarehouseId) && m.Date > date.Date, ct);
        if (hasLater)
            throw new InvalidOperationException(
                "توجد حركات لاحقة لهذا التاريخ على نفس الأصناف/المخازن — الترحيل بتاريخ سابق يفسد حساب متوسط التكلفة. استخدم تاريخًا لا يسبق آخر حركة.");
    }

    private async Task<string> WhNameAsync(Guid id, CancellationToken ct)
        => await _db.Warehouses.Where(w => w.Id == id).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "";

    // An inventory GL line that resolves to the warehouse's own subsidiary account under the control account.
    private static LedgerLine InvLine(string code, Guid warehouseId, string whName, decimal debit, decimal credit, string? memo)
        => new(code, debit, credit, memo, SubKind: InventorySources.WarehouseSubKind, SubRefId: warehouseId, SubName: $"مخزون - {whName}");

    private void AddMovement(Guid productId, Guid warehouseId, DateTime date, InventoryMovementType type,
        string refType, Guid refId, int refNumber, decimal qtyIn, decimal qtyOut, decimal unitCost, decimal totalCost, string? memo)
    {
        _db.InventoryMovements.Add(new InventoryMovement
        {
            ProductId = productId, WarehouseId = warehouseId, Date = date, MovementType = type,
            ReferenceType = refType, ReferenceId = refId, ReferenceNumber = refNumber,
            QuantityIn = qtyIn, QuantityOut = qtyOut, UnitCost = unitCost, TotalCost = totalCost, Memo = memo
        });
    }
}
