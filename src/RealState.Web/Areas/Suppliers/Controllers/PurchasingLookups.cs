using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>Lookups and number formats shared by the purchase order / invoice / payment screens.</summary>
internal static class PurchasingLookups
{
    /// <summary>Purchase order label, e.g. PO-20260001.</summary>
    public static string PO(int number) => "PO-" + number.ToString("D4");

    /// <summary>Purchase invoice label, e.g. PI-2026000001.</summary>
    public static string PI(int number) => "PI-" + number;

    /// <summary>"code — name" label a line stores as its snapshot.</summary>
    public static string ProductLabel(string sku, string name) => sku + " — " + name;

    /// <summary>
    /// Active products for a line picker, each with its current on-hand stock across
    /// all warehouses. <paramref name="alsoInclude"/> keeps inactive products already used on the document.
    /// </summary>
    public static async Task<List<ProductOption>> ProductsAsync(IApplicationDbContext db, IEnumerable<Guid> alsoInclude, CancellationToken ct)
    {
        var keep = alsoInclude.Distinct().ToList();
        var products = await db.Products.Where(p => p.IsActive || keep.Contains(p.Id)).OrderBy(p => p.Sku)
            .Select(p => new { p.Id, p.Sku, p.Name, p.TrackInventory }).ToListAsync(ct);
        var stock = await db.InventoryMovements.GroupBy(m => m.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        return products.Select(p => new ProductOption(p.Id, ProductLabel(p.Sku, p.Name), stock.GetValueOrDefault(p.Id), p.TrackInventory)).ToList();
    }

    public static Task<List<SelectListItem>> ProjectsAsync(IApplicationDbContext db, CancellationToken ct)
        => db.Projects.OrderBy(p => p.Name)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Code + " — " + p.Name }).ToListAsync(ct);

    public static Task<List<SelectListItem>> SuppliersAsync(IApplicationDbContext db, CancellationToken ct)
        => db.Suppliers.OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);

    public static async Task<List<SelectListItem>> OrdersAsync(IApplicationDbContext db, CancellationToken ct)
        => (await db.SupplierOrders.OrderByDescending(o => o.Number).Select(o => new { o.Id, o.Number, o.OrderDate }).ToListAsync(ct))
            .Select(o => new SelectListItem { Value = o.Id.ToString(), Text = $"{PO(o.Number)} — {o.OrderDate:yyyy/MM/dd}" }).ToList();

    public static Task<List<SelectListItem>> SafesAsync(IApplicationDbContext db, CancellationToken ct)
        => db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
}
