using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

/// <summary>
/// «تحديث تكلفة الأصناف» — the only place besides purchase invoices that sets a product's cost. Each row shows the
/// product's current average cost, an entry for the new cost and the difference. Saving posts a cost-update
/// document (<see cref="CostRevaluation"/>): every warehouse holding the product is revalued to quantity × new
/// cost — Dr المخزون / Cr أرباح تسوية تكلفة المخزون (increase) or Dr خسائر تسوية تكلفة المخزون / Cr المخزون (decrease).
/// Costs are shown and entered per each product's default unit.
/// </summary>
[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryUpdateCost)]
public class CostUpdatesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;

    public CostUpdatesController(IApplicationDbContext db, IInventoryEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var products = await _db.Products.Where(p => p.IsActive && p.TrackInventory).OrderBy(p => p.Sku).ToListAsync(ct);
        var ids = products.Select(p => p.Id).ToList();
        var units = await ProductUnits.LoadAsync(_db, ids, ct);
        var stock = (await _db.InventoryMovements.Where(m => ids.Contains(m.ProductId)).GroupBy(m => m.ProductId)
                .Select(g => new { g.Key, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut), Value = g.Sum(m => m.QuantityOut > 0 ? -m.TotalCost : m.TotalCost) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Qty, x.Value));
        // Each warehouse keeps its own average: shown under the product when it differs between warehouses.
        var whNames = await _db.Warehouses.ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var byWh = (await _db.InventoryMovements.Where(m => ids.Contains(m.ProductId)).GroupBy(m => new { m.ProductId, m.WarehouseId })
                .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut), Value = g.Sum(m => m.QuantityOut > 0 ? -m.TotalCost : m.TotalCost) })
                .ToListAsync(ct))
            .Where(x => x.Qty > 0)
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        // Fallback for products out of stock: the last cost they came in at / were set to.
        var lastCost = (await _db.InventoryMovements
                .Where(m => ids.Contains(m.ProductId) && m.UnitCost > 0 && !m.IsReversal
                            && (m.QuantityIn > 0 || m.MovementType == InventoryMovementType.Revaluation))
                .Select(m => new { m.ProductId, m.UnitCost, m.Date, m.CreatedAt }).ToListAsync(ct))
            .GroupBy(m => m.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Date).ThenByDescending(m => m.CreatedAt).First().UnitCost);

        var rows = products.Select(p =>
        {
            var (qty, value) = stock.GetValueOrDefault(p.Id);
            var baseCost = qty > 0 && value > 0 ? value / qty : lastCost.GetValueOrDefault(p.Id);
            var unit = units.Of(p.Id).Default;
            return new CostUpdateRow
            {
                ProductId = p.Id, Product = p.Sku + " — " + p.Name, Unit = unit.Name, Factor = unit.Factor,
                Quantity = unit.Factor > 0 ? qty / unit.Factor : qty,
                CurrentCost = Math.Round(baseCost * unit.Factor, 4),
                ByWarehouse = byWh.GetValueOrDefault(p.Id)?.Select(w => (whNames.GetValueOrDefault(w.WarehouseId, "—"),
                    unit.Factor > 0 ? w.Qty / unit.Factor : w.Qty, Math.Round(w.Value / w.Qty * unit.Factor, 4))).ToList() ?? new()
            };
        }).ToList();

        var history = await _db.CostRevaluations.OrderByDescending(d => d.Number).Take(20)
            .Select(d => new CostUpdateHistoryRow(d.Id, d.Number, d.Date, d.CreatedBy, d.Notes,
                d.Lines.Count, d.Lines.Sum(l => (decimal?)l.ValueChange) ?? 0m)).ToListAsync(ct);
        return View(new CostUpdatePageVm { Rows = rows, History = history });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(List<CostUpdateInput>? lines, string? notes, CancellationToken ct)
    {
        // Only rows given a new cost that differs from the current one.
        var wanted = (lines ?? new()).Where(l => l.NewCost is > 0).ToList();
        if ((lines ?? new()).Any(l => l.NewCost is <= 0))
        {
            TempData["ErrorMessage"] = "التكلفة الجديدة يجب أن تكون أكبر من صفر.";
            return RedirectToAction(nameof(Index));
        }
        var ids = wanted.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => ids.Contains(p.Id) && p.TrackInventory).ToDictionaryAsync(p => p.Id, ct);
        var units = await ProductUnits.LoadAsync(_db, ids, ct);

        await _engine.EnsureDefaultsAsync(ct);   // posting profile + accounts (commits on its own)
        var doc = new CostRevaluation
        {
            Number = await _engine.NextNumberAsync(_db.CostRevaluations.Select(x => x.Number), _db.CostRevaluations.Local.Select(x => x.Number), DateTime.Today.Year, ct),
            Date = DateTime.Today,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
        };
        foreach (var l in wanted)
        {
            if (!products.ContainsKey(l.ProductId)) continue;
            // Entered per the product's default unit; kept per smallest unit (as stock and costs are).
            var factor = units.Of(l.ProductId).Default.Factor;
            var baseCost = Math.Round(l.NewCost!.Value / (factor > 0 ? factor : 1m), 6);
            var current = await _engine.CurrentUnitCostAsync(l.ProductId, null, ct);
            if (Math.Abs(baseCost - current) < 0.000001m) continue;   // unchanged
            doc.Lines.Add(new CostRevaluationLine { CostRevaluationId = doc.Id, ProductId = l.ProductId, NewUnitCost = baseCost });
        }
        if (doc.Lines.Count == 0)
        {
            TempData["ErrorMessage"] = "لم تُدخل تكلفة جديدة مختلفة لأي صنف.";
            return RedirectToAction(nameof(Index));
        }

        _db.CostRevaluations.Add(doc);
        try { await _engine.PostRevaluationAsync(doc, ct); }
        catch (InvalidOperationException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
        await _db.SaveChangesAsync(ct);

        var change = doc.Lines.Sum(l => l.ValueChange);
        TempData["StatusMessage"] = $"تم تحديث تكلفة {doc.Lines.Count} صنف (مستند {doc.Number}) — فرق قيمة المخزون {change:N2} ج.م"
            + (change > 0 ? " (زيادة: أرباح تسوية تكلفة المخزون)." : change < 0 ? " (نقص: خسائر تسوية تكلفة المخزون)." : ".");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>A posted cost update's lines (popup).</summary>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var d = await _db.CostRevaluations.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var ids = d.Lines.Select(l => l.ProductId).ToList();
        var names = await _db.Products.IgnoreQueryFilters().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku + " — " + p.Name, ct);
        var units = await ProductUnits.LoadAsync(_db, ids, ct);
        return PartialView("_CostDetails", new CostUpdateDetailsVm
        {
            Number = d.Number, Date = d.Date, Notes = d.Notes, By = d.CreatedBy,
            Lines = d.Lines.Select(l =>
            {
                var u = units.Of(l.ProductId).Default;
                return new CostUpdateDetailLine(names.GetValueOrDefault(l.ProductId, "—"), u.Name, l.Quantity / u.Factor,
                    Math.Round(l.OldUnitCost * u.Factor, 4), Math.Round(l.NewUnitCost * u.Factor, 4), l.ValueChange);
            }).ToList()
        });
    }
}
