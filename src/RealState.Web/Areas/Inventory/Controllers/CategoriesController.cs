using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryView)]
public class CategoriesController : Controller
{
    private readonly IApplicationDbContext _db;
    public CategoriesController(IApplicationDbContext db) => _db = db;

    private bool CanManage() => User.HasClaim("permission", PermissionNames.InventoryManage);

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _db.ProductCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Excel(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return RealState.Web.Common.Xlsx.File($"التصنيفات {DateTime.Now:yyyy-MM-dd}.xlsx", "التصنيفات", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return View("ListPrint", InventoryExport.ToPrint("التصنيفات", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(CancellationToken ct)
    {
        var list = await _db.ProductCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        return (new() { "الاسم", "الترتيب", "الحالة" },
                list.Select(c => new object?[] { c.Name, c.SortOrder, c.IsActive ? "مفعّل" : "متوقف" }).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_CategoryForm", new CategoryFormModel());
        var c = await _db.ProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return PartialView("_CategoryForm", new CategoryFormModel { Id = c.Id, Name = c.Name, SortOrder = c.SortOrder, IsActive = c.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(CategoryFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!ModelState.IsValid) return PartialView("_CategoryForm", model);
        if (model.Id == Guid.Empty)
            _db.ProductCategories.Add(new ProductCategory { Name = model.Name, SortOrder = model.SortOrder, IsActive = model.IsActive });
        else
        {
            var c = await _db.ProductCategories.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (c is null) return NotFound();
            c.Name = model.Name; c.SortOrder = model.SortOrder; c.IsActive = model.IsActive;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.ProductCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (await _db.Products.AnyAsync(p => p.CategoryId == id, ct))
            TempData["ErrorMessage"] = "لا يمكن حذف تصنيف مرتبط بأصناف.";
        else
        {
            _db.ProductCategories.Remove(c);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم حذف التصنيف «{c.Name}».";
        }
        return RedirectToAction(nameof(Index));
    }
}
