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
public class WarehousesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public WarehousesController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.InventoryManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        return View(await _db.Warehouses.OrderBy(w => w.Code).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Excel(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return RealState.Web.Common.Xlsx.File($"المخازن {DateTime.Now:yyyy-MM-dd}.xlsx", "المخازن", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(ct);
        return View("ListPrint", InventoryExport.ToPrint("المخازن", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(CancellationToken ct)
    {
        var list = await _db.Warehouses.OrderBy(w => w.Code).ToListAsync(ct);
        return (new() { "الكود", "الاسم", "افتراضي", "الحالة" },
                list.Select(w => new object?[] { w.Code, w.Name, w.IsDefault ? "نعم" : "", w.IsActive ? "مفعّل" : "متوقف" }).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (id is null) return PartialView("_WarehouseForm", new WarehouseFormModel());
        var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return NotFound();
        return PartialView("_WarehouseForm", new WarehouseFormModel { Id = w.Id, Code = w.Code, Name = w.Name, IsDefault = w.IsDefault, IsActive = w.IsActive });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(WarehouseFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (await _db.Warehouses.AnyAsync(x => x.Code == model.Code && x.Id != model.Id, ct))
            ModelState.AddModelError(nameof(WarehouseFormModel.Code), "يوجد مخزن بنفس الكود.");
        if (!ModelState.IsValid) return PartialView("_WarehouseForm", model);
        await _engine.EnsureDefaultsAsync(ct);   // ensures the inventory control account exists
        Warehouse w;
        if (model.Id == Guid.Empty)
        {
            w = new Warehouse { Code = model.Code, Name = model.Name, IsDefault = model.IsDefault, IsActive = model.IsActive };
            _db.Warehouses.Add(w);
        }
        else
        {
            w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (w is null) return NotFound();
            w.Code = model.Code; w.Name = model.Name; w.IsDefault = model.IsDefault; w.IsActive = model.IsActive;
        }
        // Only one default warehouse per tenant.
        if (model.IsDefault)
            await _db.Warehouses.Where(x => x.Id != w.Id && x.IsDefault).ForEachAsync(x => x.IsDefault = false, ct);
        await _engine.EnsureWarehouseAccountAsync(w, ct);   // one inventory account per warehouse
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var w = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return NotFound();
        // Include reversed/soft-deleted history too. Filtering by the warehouse's own id keeps this
        // tenant-safe even with the global filters off (the id belongs to this tenant).
        var hasMovements = await _db.InventoryMovements.IgnoreQueryFilters().AnyAsync(m => m.WarehouseId == id, ct);
        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.SubKind == InventorySources.WarehouseSubKind && a.SubRefId == id, ct);
        var hasEntries = acc is not null && await _db.JournalLines.IgnoreQueryFilters().AnyAsync(l => l.AccountId == acc.Id, ct);

        if (hasMovements || hasEntries)
            TempData["ErrorMessage"] = "لا يمكن حذف مخزن له حركات أو قيود محاسبية — يمكن إيقافه بدلًا من حذفه.";
        else
        {
            if (acc is not null) _db.Accounts.Remove(acc);   // its (empty) inventory account goes with it
            _db.Warehouses.Remove(w);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم حذف المخزن «{w.Name}».";
        }
        return RedirectToAction(nameof(Index));
    }
}
