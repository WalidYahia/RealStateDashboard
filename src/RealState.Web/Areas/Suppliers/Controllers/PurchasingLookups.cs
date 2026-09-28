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
        var products = await (from p in db.Products.Where(p => p.IsActive || keep.Contains(p.Id))
                              join u in db.UnitsOfMeasure on p.UnitOfMeasureId equals u.Id into uj
                              from u in uj.DefaultIfEmpty()
                              orderby p.Sku
                              select new { p.Id, p.Sku, p.Name, p.TrackInventory, Unit = u != null ? u.Name : null }).ToListAsync(ct);
        var stock = await db.InventoryMovements.GroupBy(m => m.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        return products.Select(p => new ProductOption(p.Id, ProductLabel(p.Sku, p.Name), stock.GetValueOrDefault(p.Id), p.TrackInventory, p.Unit)).ToList();
    }

    /// <summary>The product's unit of measure name (الوحدة), or null when it has none.</summary>
    public static async Task<Dictionary<Guid, string?>> UnitsAsync(IApplicationDbContext db, IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        return await (from p in db.Products.Where(p => ids.Contains(p.Id))
                      join u in db.UnitsOfMeasure on p.UnitOfMeasureId equals u.Id into uj
                      from u in uj.DefaultIfEmpty()
                      select new { p.Id, Unit = u != null ? u.Name : null }).ToDictionaryAsync(x => x.Id, x => x.Unit, ct);
    }

    /// <summary>Ordered quantity per product on a purchase order (legacy free-text lines are ignored).</summary>
    public static async Task<Dictionary<Guid, decimal>> OrderedQtyAsync(IApplicationDbContext db, Guid orderId, CancellationToken ct)
        => (await db.SupplierOrderItems.Where(i => i.SupplierOrderId == orderId && i.ProductId != null)
                .GroupBy(i => i.ProductId!.Value).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Qty);

    /// <summary>Quantity per product already billed by the order's purchase invoices (optionally excluding one invoice being edited).</summary>
    public static async Task<Dictionary<Guid, decimal>> InvoicedQtyAsync(IApplicationDbContext db, Guid orderId, Guid? exceptInvoiceId, CancellationToken ct)
    {
        var invoiceIds = db.PurchaseInvoices.Where(v => v.PurchaseOrderId == orderId && (exceptInvoiceId == null || v.Id != exceptInvoiceId)).Select(v => v.Id);
        return (await db.PurchaseInvoiceItems.Where(i => invoiceIds.Contains(i.PurchaseInvoiceId))
                .GroupBy(i => i.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Qty);
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

    /// <summary>
    /// Orders an invoice can still bill: at least one product not yet fully invoiced (ordered − invoiced by the
    /// order's invoices, excluding <paramref name="exceptInvoiceId"/> when editing). <paramref name="keepOrderId"/>
    /// (the invoice's current order) is always listed.
    /// </summary>
    public static async Task<List<SelectListItem>> OpenOrdersAsync(IApplicationDbContext db, Guid? exceptInvoiceId, Guid? keepOrderId, CancellationToken ct)
    {
        var ordered = await db.SupplierOrderItems.Where(i => i.ProductId != null)
            .GroupBy(i => new { i.SupplierOrderId, ProductId = i.ProductId!.Value })
            .Select(g => new { g.Key.SupplierOrderId, g.Key.ProductId, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct);
        var invoiced = (await (from it in db.PurchaseInvoiceItems
                               join v in db.PurchaseInvoices on it.PurchaseInvoiceId equals v.Id
                               where v.PurchaseOrderId != null && (exceptInvoiceId == null || v.Id != exceptInvoiceId)
                               group it by new { OrderId = v.PurchaseOrderId!.Value, it.ProductId } into g
                               select new { g.Key.OrderId, g.Key.ProductId, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => (x.OrderId, x.ProductId), x => x.Qty);
        var open = ordered.Where(o => o.Qty > invoiced.GetValueOrDefault((o.SupplierOrderId, o.ProductId)))
            .Select(o => o.SupplierOrderId).ToHashSet();
        if (keepOrderId is Guid keep) open.Add(keep);

        return (await db.SupplierOrders.Where(o => open.Contains(o.Id)).OrderByDescending(o => o.Number)
                .Select(o => new { o.Id, o.Number, o.OrderDate }).ToListAsync(ct))
            .Select(o => new SelectListItem { Value = o.Id.ToString(), Text = $"{PO(o.Number)} — {o.OrderDate:yyyy/MM/dd}" }).ToList();
    }

    public static Task<List<SelectListItem>> SafesAsync(IApplicationDbContext db, CancellationToken ct)
        => db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
}
