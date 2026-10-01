using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryView)]
public class ProductsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public ProductsController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.InventoryManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);   // first inventory page a user lands on seeds the module
        var rows = await (from p in _db.Products
                          join c in _db.ProductCategories on p.CategoryId equals c.Id into cj
                          from c in cj.DefaultIfEmpty()
                          join u in _db.UnitsOfMeasure on p.UnitOfMeasureId equals u.Id into uj
                          from u in uj.DefaultIfEmpty()
                          orderby p.Sku
                          select new ProductRow
                          {
                              Id = p.Id, Sku = p.Sku, Name = p.Name,
                              CategoryName = c != null ? c.Name : null,
                              UnitName = u != null ? u.Name : null,
                              IsActive = p.IsActive
                          }).ToListAsync(ct);
        var units = await ProductUnits.LoadAsync(_db, null, ct);
        foreach (var r in rows)
        {
            var set = units.Of(r.Id);
            r.UnitsChain = set.Chain();
            r.DefaultUnitName = set.Default.Name;
        }
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Excel(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return RealState.Web.Common.Xlsx.File($"الأصناف {DateTime.Now:yyyy-MM-dd}.xlsx", "الأصناف", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return View("ListPrint", InventoryExport.ToPrint("الأصناف", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(CancellationToken ct)
    {
        var list = await (from p in _db.Products
                          join c in _db.ProductCategories on p.CategoryId equals c.Id into cj
                          from c in cj.DefaultIfEmpty()
                          join u in _db.UnitsOfMeasure on p.UnitOfMeasureId equals u.Id into uj
                          from u in uj.DefaultIfEmpty()
                          orderby p.Sku
                          select new { p.Id, p.Sku, p.Name, Cat = c != null ? c.Name : "", p.IsActive })
                         .ToListAsync(ct);
        var units = await ProductUnits.LoadAsync(_db, null, ct);
        var rows = list.Select(x => new object?[] { x.Sku, x.Name, x.Cat, units.Of(x.Id).Chain(), units.Of(x.Id).Default.Name, x.IsActive ? "مفعّل" : "متوقف" }).ToList();
        return (new() { "الكود", "الاسم", "التصنيف", "الوحدات", "الوحدة الافتراضية", "الحالة" }, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var model = new ProductFormModel();
        if (id is not null)
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return NotFound();
            model = new ProductFormModel
            {
                Id = p.Id, Sku = p.Sku, Name = p.Name, CategoryId = p.CategoryId, UnitOfMeasureId = p.UnitOfMeasureId,
                Unit2Id = p.Unit2Id, Unit2Factor = p.Unit2Id is null ? null : p.Unit2Factor,
                Unit3Id = p.Unit3Id, Unit3Factor = p.Unit3Id is null ? null : p.Unit3Factor, DefaultUnitLevel = p.DefaultUnitLevel,
                SalePrice = p.SalePrice > 0 ? p.SalePrice : null, SalePrice2 = p.SalePrice2 > 0 ? p.SalePrice2 : null, SalePrice3 = p.SalePrice3 > 0 ? p.SalePrice3 : null,
                TrackInventory = p.TrackInventory, TrackCost = p.TrackCost, IsActive = p.IsActive, ReorderLevel = p.ReorderLevel, Notes = p.Notes
            };
        }
        await FillListsAsync(model, ct);
        return PartialView("_ProductForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ProductFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (await _db.Products.AnyAsync(p => p.Sku == model.Sku && p.Id != model.Id, ct))
            ModelState.AddModelError(nameof(ProductFormModel.Sku), "يوجد صنف بنفس الكود.");
        await ValidateUnitsAsync(model, ct);
        if (!ModelState.IsValid) { await FillListsAsync(model, ct); return PartialView("_ProductForm", model); }
        // Unused levels are cleared; the default falls back to the smallest unit when its level isn't defined.
        if (model.Unit2Id is null) { model.Unit3Id = null; }
        var u2f = model.Unit2Id is null ? 0m : model.Unit2Factor!.Value;
        var u3f = model.Unit3Id is null ? 0m : model.Unit3Factor!.Value;
        var defLevel = model.DefaultUnitLevel switch { 2 when model.Unit2Id is not null => (byte)2, 3 when model.Unit3Id is not null => (byte)3, _ => (byte)1 };
        var sp1 = model.SalePrice ?? 0m;
        var sp2 = model.Unit2Id is null ? 0m : model.SalePrice2 ?? 0m;
        var sp3 = model.Unit3Id is null ? 0m : model.SalePrice3 ?? 0m;
        if (model.Id == Guid.Empty)
            _db.Products.Add(new Product
            {
                Sku = model.Sku, Name = model.Name, CategoryId = model.CategoryId, UnitOfMeasureId = model.UnitOfMeasureId,
                Unit2Id = model.Unit2Id, Unit2Factor = u2f, Unit3Id = model.Unit3Id, Unit3Factor = u3f, DefaultUnitLevel = defLevel,
                SalePrice = sp1, SalePrice2 = sp2, SalePrice3 = sp3,
                TrackInventory = model.TrackInventory, TrackCost = model.TrackCost, IsActive = model.IsActive, ReorderLevel = model.ReorderLevel, Notes = model.Notes
            });
        else
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (p is null) return NotFound();
            p.Sku = model.Sku; p.Name = model.Name; p.CategoryId = model.CategoryId; p.UnitOfMeasureId = model.UnitOfMeasureId;
            p.Unit2Id = model.Unit2Id; p.Unit2Factor = u2f; p.Unit3Id = model.Unit3Id; p.Unit3Factor = u3f; p.DefaultUnitLevel = defLevel;
            p.SalePrice = sp1; p.SalePrice2 = sp2; p.SalePrice3 = sp3;
            p.TrackInventory = model.TrackInventory; p.TrackCost = model.TrackCost; p.IsActive = model.IsActive; p.ReorderLevel = model.ReorderLevel; p.Notes = model.Notes;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return NotFound();
        // Include reversed/soft-deleted history so a product with any past movement is never removed.
        if (await _db.InventoryMovements.IgnoreQueryFilters().AnyAsync(m => m.ProductId == id, ct))
            TempData["ErrorMessage"] = "لا يمكن حذف صنف له حركات مخزون — يمكن إيقافه بدلًا من حذفه.";
        else if (await _db.PurchaseInvoiceItems.AnyAsync(i => i.ProductId == id, ct) ||
                 await _db.SupplierOrderItems.AnyAsync(i => i.ProductId == id, ct) ||
                 await _db.ProductSalesInvoiceItems.AnyAsync(i => i.ProductId == id, ct))
            TempData["ErrorMessage"] = "لا يمكن حذف صنف مستخدم في أوامر توريد أو فواتير مشتريات أو مبيعات — يمكن إيقافه بدلًا من حذفه.";
        else
        {
            _db.Products.Remove(p);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم حذف الصنف «{p.Name}».";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Unit rules: a bigger unit needs the smallest one (and the biggest needs the bigger one), each with a factor
    /// above 1, and no unit repeated. The smallest unit is what stock and cost are counted in, so it can't change
    /// once the product has stock movements (the bigger units / factors may — documents keep their own factor).
    /// </summary>
    private async Task ValidateUnitsAsync(ProductFormModel m, CancellationToken ct)
    {
        if (m.Unit3Id is not null && m.Unit2Id is null)
            ModelState.AddModelError(nameof(m.Unit3Id), "حدّد «الوحدة الأكبر» أولًا.");
        if (m.Unit2Id is not null)
        {
            if (m.UnitOfMeasureId is null) ModelState.AddModelError(nameof(m.UnitOfMeasureId), "حدّد الوحدة الصغرى أولًا.");
            if (m.Unit2Factor is null || m.Unit2Factor <= 1) ModelState.AddModelError(nameof(m.Unit2Factor), "أدخل عدد الوحدات الصغرى في الوحدة الأكبر (أكبر من 1).");
        }
        if (m.Unit3Id is not null && (m.Unit3Factor is null || m.Unit3Factor <= 1))
            ModelState.AddModelError(nameof(m.Unit3Factor), "أدخل عدد «الوحدة الأكبر» في الوحدة الأكبر منها (أكبر من 1).");
        var chosen = new[] { m.UnitOfMeasureId, m.Unit2Id, m.Unit3Id }.Where(x => x is not null).ToList();
        if (chosen.Count != chosen.Distinct().Count())
            ModelState.AddModelError(string.Empty, "لا يمكن تكرار نفس الوحدة في أكثر من مستوى.");

        if (m.Id != Guid.Empty)
        {
            var current = await _db.Products.Where(p => p.Id == m.Id).Select(p => p.UnitOfMeasureId).FirstOrDefaultAsync(ct);
            if (current != m.UnitOfMeasureId && await _db.InventoryMovements.IgnoreQueryFilters().AnyAsync(x => x.ProductId == m.Id, ct))
                ModelState.AddModelError(nameof(m.UnitOfMeasureId), "لا يمكن تغيير الوحدة الصغرى لصنف له حركات مخزون — الأرصدة والتكلفة محسوبة بها.");
        }
    }

    private async Task FillListsAsync(ProductFormModel model, CancellationToken ct)
    {
        model.Categories = await _db.ProductCategories.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync(ct);
        model.Units = await _db.UnitsOfMeasure.Where(u => u.IsActive).OrderBy(u => u.Code)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.Code + " — " + u.Name }).ToListAsync(ct);
    }
}
