using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;
using RealState.Web.Filters;

namespace RealState.Web.Areas.Inventory.Controllers;

/// <summary>
/// «الرصيد الافتتاحي للأصناف» — like «الأرصدة الافتتاحية» of the accounts: every stock product of a warehouse in one
/// table, each with ONE opening stock (<see cref="ProductOpeningStock"/>) that's saved — and posted — per row and can
/// be edited: the row's movement and journal entry (Dr المخزون / Cr رصيد افتتاحي) are replaced. A row locks once the
/// product's stock moved in that warehouse after it (weighted average has no history rewrite); 0 removes it.
/// </summary>
[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryOpeningStock)]
public class OpeningStockController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;

    public OpeningStockController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    public async Task<IActionResult> Index(Guid? warehouseId, CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        var warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsDefault).ThenBy(w => w.Name)
            .Select(w => new { w.Id, w.Name }).ToListAsync(ct);
        var wh = warehouseId is Guid id && warehouses.Any(w => w.Id == id) ? id : warehouses.Select(w => w.Id).FirstOrDefault();
        var vm = new OpeningStockVm
        {
            WarehouseId = wh,
            Warehouses = warehouses.Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToList()
        };
        if (wh != Guid.Empty) vm.Rows = await BuildRowsAsync(wh, ct);
        return View(vm);
    }

    /// <summary>Saves (and posts) one product's opening stock in a warehouse; quantity 0 removes it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Guid productId, Guid warehouseId, DateTime? date, decimal? quantity, byte unitLevel, decimal? unitCost, CancellationToken ct)
    {
        IActionResult Refuse(string error)
        {
            HttpContext.Items[ActivityLogFilter.SkipItem] = true;
            return Json(new { ok = false, error });
        }
        // A value that didn't parse must never read as 0 (which would remove the opening stock).
        if (quantity is null || date is null || !ModelState.IsValid) return Refuse("أدخل الكمية والتاريخ بشكل صحيح (0 لإلغاء الرصيد).");
        var qty = quantity.Value; var cost = unitCost ?? 0m;
        if (qty < 0) return Refuse("كمية الرصيد الافتتاحي موجبة فقط.");
        if (qty > 0 && cost <= 0) return Refuse("أدخل تكلفة الوحدة.");
        if (date.Value.Date > ClientTime.Today) return Refuse("لا يمكن أن يكون تاريخ الرصيد الافتتاحي في المستقبل.");

        var product = await _db.Products.Where(p => p.Id == productId && p.IsActive && p.TrackInventory).Select(p => new { p.Id, p.Sku, p.Name }).FirstOrDefaultAsync(ct);
        var wh = await _db.Warehouses.Where(w => w.Id == warehouseId && w.IsActive).Select(w => w.Name).FirstOrDefaultAsync(ct);
        if (product is null || wh is null) return Refuse("الصنف أو المخزن غير موجود.");
        if (await LegacyAsync(warehouseId, ct) is var legacy && legacy.TryGetValue(productId, out var old))
            return Refuse($"رصيد هذا الصنف الافتتاحي مسجَّل بتسوية مخزون رقم {old.Numbers} — اعكسها من صفحة التسويات أولًا لتعديله هنا.");

        await _engine.EnsureDefaultsAsync(ct);   // posting profile + accounts (commits on its own)
        var row = await _db.ProductOpeningStocks.FirstOrDefaultAsync(o => o.ProductId == productId && o.WarehouseId == warehouseId, ct);
        if (row is null && qty == 0) return Json(new { ok = true, updated = "—", date = "—", removed = true });
        var previous = row?.Date;
        var isNew = row is null;
        row ??= new ProductOpeningStock { ProductId = productId, WarehouseId = warehouseId };

        var u = InvDocUnits.Resolve(await ProductUnits.LoadAsync(_db, new[] { productId }, ct), productId, unitLevel);
        row.Date = date.Value;
        row.Quantity = qty * u.Factor;                                     // kept per the smallest unit
        row.UnitCost = qty > 0 ? Math.Round(cost / u.Factor, 6) : 0m;
        row.TotalCost = Math.Round(qty * cost, 2);                         // exactly as entered
        row.UnitLevel = u.Level; row.UnitFactor = u.Factor; row.UnitName = u.Name;
        try
        {
            await _engine.PostOpeningStockAsync(row, previous, ct);        // replaces the row's movement + journal entry
            if (qty == 0) _db.ProductOpeningStocks.Remove(row);
            else if (isNew) _db.ProductOpeningStocks.Add(row);
            else row.UpdatedAt = ClientTime.Now;
            await _db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex) { return Refuse(ex.Message); }
        catch (DbUpdateException) { return Refuse("تعذّر الحفظ — ربما عُدِّل هذا الصنف من جلسة أخرى. أعد تحميل الصفحة."); }

        HttpContext.Items[ActivityLogFilter.DescriptionItem] = qty == 0
            ? $"إلغاء الرصيد الافتتاحي للصنف {product.Sku} — {product.Name} في {wh}"
            : $"الرصيد الافتتاحي للصنف {product.Sku} — {product.Name} في {wh}: {qty:0.####} {u.Name} × {cost:N2} = {row.TotalCost:N2}";
        return Json(new
        {
            ok = true, removed = qty == 0, total = row.TotalCost,
            updated = qty == 0 ? "—" : AppCulture.When(row.UpdatedAt ?? row.CreatedAt),
            date = qty == 0 ? "—" : AppCulture.When(row.Date)
        });
    }

    // ---- helpers ----
    private async Task<List<OpeningStockRow>> BuildRowsAsync(Guid warehouseId, CancellationToken ct)
    {
        var products = await _db.Products.Where(p => p.IsActive && p.TrackInventory).OrderBy(p => p.Sku)
            .Select(p => new { p.Id, p.Sku, p.Name, Category = p.Category != null ? p.Category.Name : "" }).ToListAsync(ct);
        var units = await ProductUnits.LoadAsync(_db, products.Select(p => p.Id), ct);
        var openings = await _db.ProductOpeningStocks.Where(o => o.WarehouseId == warehouseId).ToDictionaryAsync(o => o.ProductId, ct);
        // The latest other movement of each product here — a row dated before it is locked.
        var lastMove = (await _db.InventoryMovements
                .Where(m => m.WarehouseId == warehouseId && !m.IsReversal && m.ReferenceType != InventorySources.OpeningStock)
                .GroupBy(m => m.ProductId).Select(g => new { g.Key, Last = g.Max(m => m.Date) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Last);
        var legacy = await LegacyAsync(warehouseId, ct);

        return products.Select(p =>
        {
            var row = new OpeningStockRow { ProductId = p.Id, Sku = p.Sku, Name = p.Name, Category = p.Category ?? "", Unit = new UnitSelectVm(units.Of(p.Id), 0) };
            if (openings.TryGetValue(p.Id, out var o))
            {
                var entered = o.UnitFactor > 0 ? Math.Round(o.Quantity / o.UnitFactor, 4) : o.Quantity;
                row.Unit = new UnitSelectVm(units.Of(p.Id), o.UnitLevel);
                row.Quantity = entered;
                row.UnitCost = entered > 0 ? Math.Round(o.TotalCost / entered, 4) : 0m;
                row.Date = o.Date; row.UpdatedAt = o.UpdatedAt ?? o.CreatedAt;
                if (lastMove.TryGetValue(p.Id, out var last) && last > o.Date)
                    row.Locked = $"تحرّك الصنف في هذا المخزن بعد الرصيد الافتتاحي ({AppCulture.When(last)}) — للتعديل استخدم تسوية مخزون.";
            }
            else if (legacy.TryGetValue(p.Id, out var l))
            {
                var f = units.Of(p.Id).Default.Factor;   // shown in the product's default unit
                row.Quantity = Math.Round(l.Quantity / (f > 0 ? f : 1m), 4);
                row.UnitCost = row.Quantity > 0 ? Math.Round(l.Value / row.Quantity, 4) : 0m; row.Date = l.Date;
                row.Locked = $"مسجَّل بتسوية مخزون رقم {l.Numbers} — اعكسها من صفحة التسويات لتعديله هنا.";
            }
            return row;
        }).ToList();
    }

    /// <summary>
    /// Opening stock posted earlier as an adjustment (reason «رصيد افتتاحي», before this page kept one per product):
    /// shown read-only until those adjustments are reversed. Quantities per the smallest unit.
    /// </summary>
    private async Task<Dictionary<Guid, (decimal Quantity, decimal Value, DateTime Date, string Numbers)>> LegacyAsync(Guid warehouseId, CancellationToken ct)
        => (await (from l in _db.InventoryAdjustmentLines
                   join d in _db.InventoryAdjustments on l.InventoryAdjustmentId equals d.Id
                   where d.WarehouseId == warehouseId && d.Reason == AdjustmentReason.Opening && d.Status == InventoryDocStatus.Posted
                   select new { l.ProductId, l.QuantityDelta, l.TotalCost, d.Date, d.Number }).ToListAsync(ct))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => (g.Sum(x => x.QuantityDelta), g.Sum(x => x.TotalCost), g.Min(x => x.Date),
                string.Join("، ", g.Select(x => x.Number).Distinct().OrderBy(n => n))));
}
