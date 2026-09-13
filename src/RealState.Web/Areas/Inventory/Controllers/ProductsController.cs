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
                          select new { p.Sku, p.Name, Cat = c != null ? c.Name : "", Unit = u != null ? u.Name : "", p.IsActive })
                         .ToListAsync(ct);
        var rows = list.Select(x => new object?[] { x.Sku, x.Name, x.Cat, x.Unit, x.IsActive ? "مفعّل" : "متوقف" }).ToList();
        return (new() { "الكود", "الاسم", "التصنيف", "الوحدة", "الحالة" }, rows);
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
        if (!ModelState.IsValid) { await FillListsAsync(model, ct); return PartialView("_ProductForm", model); }
        if (model.Id == Guid.Empty)
            _db.Products.Add(new Product
            {
                Sku = model.Sku, Name = model.Name, CategoryId = model.CategoryId, UnitOfMeasureId = model.UnitOfMeasureId,
                TrackInventory = model.TrackInventory, TrackCost = model.TrackCost, IsActive = model.IsActive, ReorderLevel = model.ReorderLevel, Notes = model.Notes
            });
        else
        {
            var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (p is null) return NotFound();
            p.Sku = model.Sku; p.Name = model.Name; p.CategoryId = model.CategoryId; p.UnitOfMeasureId = model.UnitOfMeasureId;
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
        else
        {
            _db.Products.Remove(p);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم حذف الصنف «{p.Name}».";
        }
        return RedirectToAction(nameof(Index));
    }

    private async Task FillListsAsync(ProductFormModel model, CancellationToken ct)
    {
        model.Categories = await _db.ProductCategories.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync(ct);
        model.Units = await _db.UnitsOfMeasure.Where(u => u.IsActive).OrderBy(u => u.Code)
            .Select(u => new SelectListItem { Value = u.Id.ToString(), Text = u.Code + " — " + u.Name }).ToListAsync(ct);
    }
}
