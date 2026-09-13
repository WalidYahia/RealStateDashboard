using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryView)]
public class DashboardController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public DashboardController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    public async Task<IActionResult> Index(Guid? categoryId, DateTime? asOf, CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        var cutoff = (asOf ?? DateTime.Today).Date;

        var vm = new InventoryDashboardVm { AsOf = cutoff, CategoryId = categoryId };
        vm.Categories = await _db.ProductCategories.OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name, Selected = c.Id == categoryId }).ToListAsync(ct);

        // Products in scope (only stock-tracked ones carry inventory).
        var prodQ = _db.Products.Where(p => p.IsActive && p.TrackInventory);
        if (categoryId is Guid cid) prodQ = prodQ.Where(p => p.CategoryId == cid);
        var products = await prodQ.Select(p => new { p.Id, p.Sku, p.Name, p.ReorderLevel }).ToListAsync(ct);
        var ids = products.Select(p => p.Id).ToList();

        // Quantity + value per product per warehouse, as of the chosen date.
        // Outflow is decided by quantity direction — same rule the engine and reports use.
        var agg = await _db.InventoryMovements
            .Where(m => m.Date < cutoff.AddDays(1) && ids.Contains(m.ProductId))
            .GroupBy(m => new { m.ProductId, m.WarehouseId })
            .Select(g => new
            {
                g.Key.ProductId, g.Key.WarehouseId,
                Qty = g.Sum(x => x.QuantityIn - x.QuantityOut),
                Value = g.Sum(x => x.QuantityOut > 0 ? -x.TotalCost : x.TotalCost)
            }).ToListAsync(ct);

        var warehouses = await _db.Warehouses.Select(w => new { w.Id, w.Name, w.IsActive }).ToListAsync(ct);
        var whName = warehouses.ToDictionary(w => w.Id, w => w.Name);

        // ---- KPI tiles ----
        vm.TotalValue = agg.Sum(a => a.Value);
        vm.TotalProducts = products.Count;
        vm.ActiveWarehouses = warehouses.Count(w => w.IsActive);

        var qtyByProduct = agg.GroupBy(a => a.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        foreach (var p in products)
        {
            var qty = qtyByProduct.GetValueOrDefault(p.Id);
            string? state = null;
            if (qty < 0) { vm.NegativeStock++; state = "سالب"; }
            else if (qty == 0) { vm.OutOfStock++; state = "نفد"; }
            else if (p.ReorderLevel > 0 && qty <= p.ReorderLevel) { vm.LowStock++; state = "منخفض"; }

            if (state is not null)
                vm.Alerts.Add(new StockAlertRow { Sku = p.Sku, Product = p.Name, Quantity = qty, ReorderLevel = p.ReorderLevel, State = state });
        }
        // Worst first: negative, then out, then low — and smallest quantity first within each.
        vm.Alerts = vm.Alerts
            .OrderBy(a => a.State == "سالب" ? 0 : a.State == "نفد" ? 1 : 2)
            .ThenBy(a => a.Quantity)
            .Take(15).ToList();

        // ---- value by warehouse ----
        vm.ByWarehouse = agg.GroupBy(a => a.WarehouseId)
            .Select(g => new WarehouseValueRow
            {
                Warehouse = whName.GetValueOrDefault(g.Key, ""),
                Quantity = g.Sum(x => x.Qty),
                Value = g.Sum(x => x.Value)
            })
            .Where(r => r.Value != 0 || r.Quantity != 0)
            .OrderByDescending(r => r.Value).ToList();

        return View(vm);
    }
}
