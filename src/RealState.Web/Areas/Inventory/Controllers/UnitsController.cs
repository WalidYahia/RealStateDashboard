using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Inventory.Models;

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryView)]
public class UnitsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public UnitsController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.InventoryManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        return View(await _db.UnitsOfMeasure.OrderBy(u => u.Code).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Excel(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return RealState.Web.Common.Xlsx.File($"وحدات القياس {DateTime.Now:yyyy-MM-dd}.xlsx", "وحدات القياس", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return View("ListPrint", InventoryExport.ToPrint("وحدات القياس", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(CancellationToken ct)
    {
        var list = await _db.UnitsOfMeasure.OrderBy(u => u.Code).ToListAsync(ct);
        return (new() { "الكود", "الاسم", "الحالة" },
                list.Select(u => new object?[] { u.Code, u.Name, u.IsActive ? "مفعّل" : "متوقف" }).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_UnitForm", new UnitFormModel());
        var u = await _db.UnitsOfMeasure.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        return PartialView("_UnitForm", new UnitFormModel { Id = u.Id, Code = u.Code, Name = u.Name, IsActive = u.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(UnitFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (await _db.UnitsOfMeasure.AnyAsync(u => u.Code == model.Code && u.Id != model.Id, ct))
            ModelState.AddModelError(nameof(UnitFormModel.Code), "يوجد وحدة بنفس الكود.");
        if (!ModelState.IsValid) return PartialView("_UnitForm", model);
        if (model.Id == Guid.Empty)
            _db.UnitsOfMeasure.Add(new UnitOfMeasure { Code = model.Code, Name = model.Name, IsActive = model.IsActive });
        else
        {
            var u = await _db.UnitsOfMeasure.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (u is null) return NotFound();
            u.Code = model.Code; u.Name = model.Name; u.IsActive = model.IsActive;
        }
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var u = await _db.UnitsOfMeasure.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return NotFound();
        if (await _db.Products.AnyAsync(p => p.UnitOfMeasureId == id, ct))
            TempData["ErrorMessage"] = "لا يمكن حذف وحدة مرتبطة بأصناف.";
        else
        {
            _db.UnitsOfMeasure.Remove(u);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم حذف الوحدة «{u.Name}».";
        }
        return RedirectToAction(nameof(Index));
    }
}
