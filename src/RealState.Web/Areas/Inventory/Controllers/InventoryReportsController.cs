using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryReports)]
public class InventoryReportsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public InventoryReportsController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    // signed value of a movement for weighted-average valuation (client-side use only — EF queries
    // must inline this condition so it is translatable to SQL).
    private static bool IsOut(InventoryMovementType t) =>
        t is InventoryMovementType.Issue or InventoryMovementType.TransferOut or InventoryMovementType.AdjustmentOut;

    // ---------------- Stock Balance ----------------
    public async Task<IActionResult> StockBalance(DateTime? asOf, CancellationToken ct)
        => View(await BuildBalanceAsync(asOf, byProductOnly: false, ct));

    public async Task<IActionResult> Valuation(DateTime? asOf, CancellationToken ct)
        => View("StockBalance", await BuildBalanceAsync(asOf, byProductOnly: true, ct));

    [HttpGet]
    public async Task<IActionResult> StockBalanceExcel(DateTime? asOf, CancellationToken ct)
    {
        var vm = await BuildBalanceAsync(asOf, byProductOnly: false, ct);
        var headers = new[] { "الكود", "الصنف", "المخزن", "الكمية", "متوسط التكلفة", "القيمة" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Sku, r.Product, r.Warehouse, r.Quantity, r.AvgCost, r.Value });
        var totals = new object?[] { "الإجمالي", null, null, null, null, vm.TotalValue };
        return RealState.Web.Common.Xlsx.File($"أرصدة المخزون {DateTime.Now:yyyy-MM-dd}.xlsx", "أرصدة المخزون", headers, rows, totals);
    }

    [HttpGet]
    public async Task<IActionResult> StockBalancePrint(DateTime? asOf, CancellationToken ct)
        => BalancePrint(await BuildBalanceAsync(asOf, false, ct), "أرصدة المخزون");

    [HttpGet]
    public async Task<IActionResult> ValuationExcel(DateTime? asOf, CancellationToken ct)
    {
        var vm = await BuildBalanceAsync(asOf, true, ct);
        var (h, rows, totals) = BalanceExport(vm);
        return RealState.Web.Common.Xlsx.File($"تقييم المخزون {DateTime.Now:yyyy-MM-dd}.xlsx", "تقييم المخزون", h, rows, totals);
    }

    [HttpGet]
    public async Task<IActionResult> ValuationPrint(DateTime? asOf, CancellationToken ct)
        => BalancePrint(await BuildBalanceAsync(asOf, true, ct), "تقييم المخزون");

    private IActionResult BalancePrint(StockBalanceVm vm, string title)
    {
        var (h, rows, totals) = BalanceExport(vm);
        return View("ListPrint", InventoryExport.ToPrint(title, h, rows, totals: totals));
    }

    private static (List<string>, List<object?[]>, object?[]) BalanceExport(StockBalanceVm vm)
    {
        var h = new List<string> { "الكود", "الصنف", "المخزن", "الكمية", "متوسط التكلفة", "القيمة" };
        var rows = vm.Rows.Select(r => new object?[] { r.Sku, r.Product, r.Warehouse, r.Quantity, r.AvgCost, r.Value }).ToList();
        var totals = new object?[] { "الإجمالي", null, null, null, null, vm.TotalValue };
        return (h, rows, totals);
    }

    private async Task<StockBalanceVm> BuildBalanceAsync(DateTime? asOf, bool byProductOnly, CancellationToken ct)
    {
        var cutoff = (asOf ?? DateTime.Today).Date;
        var moves = await _db.InventoryMovements.Where(m => m.Date < cutoff.AddDays(1))
            .GroupBy(m => new { m.ProductId, m.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId, g.Key.WarehouseId,
                Qty = g.Sum(x => x.QuantityIn - x.QuantityOut),
                // Outflow is decided by the quantity direction, matching InventoryEngine.GetStockAsync.
                Value = g.Sum(x => x.QuantityOut > 0 ? -x.TotalCost : x.TotalCost)
            }).ToListAsync(ct);

        var products = await _db.Products.Select(p => new { p.Id, p.Sku, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var warehouses = await _db.Warehouses.Select(w => new { w.Id, w.Name }).ToDictionaryAsync(w => w.Id, ct);

        var vm = new StockBalanceVm { ByProductOnly = byProductOnly };
        IEnumerable<StockBalanceRow> rows;
        if (byProductOnly)
        {
            rows = moves.GroupBy(m => m.ProductId).Select(g =>
            {
                var qty = g.Sum(x => x.Qty); var val = g.Sum(x => x.Value);
                products.TryGetValue(g.Key, out var p);
                return new StockBalanceRow { Sku = p?.Sku ?? "", Product = p?.Name ?? "", Warehouse = "— الكل —", Quantity = qty, Value = val, AvgCost = qty != 0 ? Math.Round(val / qty, 2) : 0m };
            });
        }
        else
        {
            rows = moves.Select(m =>
            {
                products.TryGetValue(m.ProductId, out var p); warehouses.TryGetValue(m.WarehouseId, out var w);
                return new StockBalanceRow { Sku = p?.Sku ?? "", Product = p?.Name ?? "", Warehouse = w?.Name ?? "", Quantity = m.Qty, Value = m.Value, AvgCost = m.Qty != 0 ? Math.Round(m.Value / m.Qty, 2) : 0m };
            });
        }
        vm.Rows = rows.Where(r => r.Quantity != 0 || r.Value != 0).OrderBy(r => r.Sku).ThenBy(r => r.Warehouse).ToList();
        return vm;
    }

    // ---------------- Inventory Movement ----------------
    public async Task<IActionResult> Movements(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var vm = await BuildMovementsAsync(productId, warehouseId, from, to, stockCard: false, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> MovementsExcel(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildMovementsAsync(productId, warehouseId, from, to, stockCard: false, ct);
        var headers = new[] { "التاريخ", "النوع", "المرجع", "الصنف", "المخزن", "وارد", "منصرف" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[] { r.Date.ToString("yyyy-MM-dd"), r.TypeAr, r.Reference, r.Product, r.Warehouse, r.In, r.Out });
        var totals = new object?[] { "الإجمالي", null, null, null, null, vm.TotalIn, vm.TotalOut };
        return RealState.Web.Common.Xlsx.File($"حركة المخزون {DateTime.Now:yyyy-MM-dd}.xlsx", "حركة المخزون", headers, rows, totals);
    }

    // ---------------- Stock Card ----------------
    public async Task<IActionResult> StockCard(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildMovementsAsync(productId, warehouseId, from, to, stockCard: true, ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> MovementsPrint(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildMovementsAsync(productId, warehouseId, from, to, stockCard: false, ct);
        var h = new List<string> { "التاريخ", "النوع", "المرجع", "الصنف", "المخزن", "وارد", "منصرف" };
        var rows = vm.Rows.Select(r => new object?[] { r.Date.ToString("yyyy/MM/dd"), r.TypeAr, r.Reference, r.Product, r.Warehouse, r.In, r.Out }).ToList();
        var totals = new object?[] { "الإجمالي", null, null, null, null, vm.TotalIn, vm.TotalOut };
        return View("ListPrint", InventoryExport.ToPrint("حركة المخزون", h, rows, totals: totals));
    }

    [HttpGet]
    public async Task<IActionResult> StockCardExcel(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows, totals) = await StockCardExportAsync(productId, warehouseId, from, to, ct);
        return RealState.Web.Common.Xlsx.File($"بطاقة الصنف {DateTime.Now:yyyy-MM-dd}.xlsx", "بطاقة الصنف", h, rows, totals);
    }

    [HttpGet]
    public async Task<IActionResult> StockCardPrint(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows, totals) = await StockCardExportAsync(productId, warehouseId, from, to, ct);
        return View("ListPrint", InventoryExport.ToPrint("بطاقة الصنف", h, rows, totals: totals));
    }

    private async Task<(List<string>, List<object?[]>, object?[])> StockCardExportAsync(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildMovementsAsync(productId, warehouseId, from, to, stockCard: true, ct);
        var h = new List<string> { "التاريخ", "النوع", "المرجع", "وارد", "منصرف", "الرصيد" };
        var rows = vm.Rows.Select(r => new object?[] { r.Date.ToString("yyyy/MM/dd"), r.TypeAr, r.Reference, r.In, r.Out, r.Balance }).ToList();
        var totals = new object?[] { "الإجمالي", null, null, vm.TotalIn, vm.TotalOut, null };
        return (h, rows, totals);
    }

    private async Task<MovementsVm> BuildMovementsAsync(Guid? productId, Guid? warehouseId, DateTime? from, DateTime? to, bool stockCard, CancellationToken ct)
    {
        var vm = new MovementsVm
        {
            ProductId = productId, WarehouseId = warehouseId, From = from, To = to, IsStockCard = stockCard,
            Products = await _db.Products.OrderBy(p => p.Sku).Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Sku + " — " + p.Name, Selected = p.Id == productId }).ToListAsync(ct),
            Warehouses = await _db.Warehouses.OrderBy(w => w.Name).Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name, Selected = w.Id == warehouseId }).ToListAsync(ct)
        };
        // Stock card needs a specific product+warehouse.
        if (stockCard && (productId is null || warehouseId is null)) return vm;

        var q = _db.InventoryMovements.AsQueryable();
        if (productId is Guid pid) q = q.Where(m => m.ProductId == pid);
        if (warehouseId is Guid wid) q = q.Where(m => m.WarehouseId == wid);
        if (from is DateTime f) q = q.Where(m => m.Date >= f.Date);
        if (to is DateTime t) q = q.Where(m => m.Date < t.Date.AddDays(1));

        var list = await q.OrderBy(m => m.Date).ThenBy(m => m.Id)
            .Select(m => new { m.Date, m.MovementType, m.ReferenceType, m.ReferenceNumber, m.IsReversal, m.ProductId, m.WarehouseId, m.QuantityIn, m.QuantityOut }).ToListAsync(ct);
        var products = await _db.Products.Select(p => new { p.Id, p.Sku, p.Name }).ToDictionaryAsync(p => p.Id, ct);
        var warehouses = await _db.Warehouses.Select(w => new { w.Id, w.Name }).ToDictionaryAsync(w => w.Id, ct);

        // Stock card running balance starts from the opening (movements before "from").
        decimal running = 0m;
        if (stockCard && from is DateTime f2)
            running = await _db.InventoryMovements.Where(m => m.ProductId == productId && m.WarehouseId == warehouseId && m.Date < f2.Date)
                .SumAsync(m => (decimal?)(m.QuantityIn - m.QuantityOut), ct) ?? 0m;

        foreach (var m in list)
        {
            products.TryGetValue(m.ProductId, out var p); warehouses.TryGetValue(m.WarehouseId, out var w);
            running += m.QuantityIn - m.QuantityOut;
            vm.Rows.Add(new MovementRow
            {
                Date = m.Date,
                TypeAr = MovementTypeAr(m.MovementType) + (m.IsReversal ? " (عكس)" : ""),
                Reference = $"{RefTypeAr(m.ReferenceType)} {m.ReferenceNumber}".Trim(),
                Product = p != null ? p.Sku + " — " + p.Name : "", Warehouse = w?.Name ?? "",
                In = m.QuantityIn, Out = m.QuantityOut, Balance = stockCard ? running : 0m
            });
        }
        return vm;
    }

    // ---------------- Inventory / GL Reconciliation ----------------
    public async Task<IActionResult> Reconciliation(DateTime? asOf, CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        var cutoff = (asOf ?? DateTime.Today).Date;
        var profile = await _db.InventoryPostingProfiles.FirstAsync(ct);

        var subledger = await _db.InventoryMovements.Where(m => m.Date < cutoff.AddDays(1))
            .SumAsync(m => (decimal?)(m.QuantityOut > 0 ? -m.TotalCost : m.TotalCost), ct) ?? 0m;

        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == profile.InventoryCode, ct);
        decimal gl = 0m;
        if (acc is not null)
        {
            // Inventory is a control account — sum it and its WHOLE subtree (accounts can be re-parented
            // by the chart's drag/drop, so direct children alone would under-count).
            var all = await _db.Accounts.Select(a => new { a.Id, a.ParentId }).ToListAsync(ct);
            var byParent = all.ToLookup(a => a.ParentId);
            var ids = new HashSet<Guid>();
            void Collect(Guid id) { if (ids.Add(id)) foreach (var c in byParent[id]) Collect(c.Id); }
            Collect(acc.Id);

            gl = await (from l in _db.JournalLines
                        join e in _db.JournalEntries on l.JournalEntryId equals e.Id
                        where ids.Contains(l.AccountId) && e.Date < cutoff.AddDays(1)
                        select (decimal?)(l.Debit - l.Credit)).SumAsync(ct) ?? 0m;
        }

        return View(new ReconciliationVm
        {
            InventoryAccountCode = profile.InventoryCode,
            InventoryAccountName = acc?.Name ?? "",
            SubledgerValue = Math.Round(subledger, 2),
            GlValue = Math.Round(gl, 2)
        });
    }

    private static string MovementTypeAr(InventoryMovementType t) => t switch
    {
        InventoryMovementType.Receipt => "استلام",
        InventoryMovementType.Issue => "صرف",
        InventoryMovementType.TransferIn => "تحويل وارد",
        InventoryMovementType.TransferOut => "تحويل صادر",
        InventoryMovementType.AdjustmentIn => "تسوية زيادة",
        InventoryMovementType.AdjustmentOut => "تسوية نقص",
        InventoryMovementType.Opening => "رصيد افتتاحي",
        _ => t.ToString()
    };

    private static string RefTypeAr(string? refType) => refType switch
    {
        InventorySources.GoodsReceipt => "إذن استلام",
        InventorySources.GoodsIssue => "إذن صرف",
        InventorySources.StockTransfer => "تحويل",
        InventorySources.InventoryAdjustment => "تسوية",
        InventorySources.StockCount => "جرد",
        _ => refType ?? ""
    };
}
