using System.Text.Json;
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

namespace RealState.Web.Areas.Inventory.Controllers;

[Area("Inventory")]
[Authorize(Policy = PermissionNames.InventoryView)]
public class StockCountsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public StockCountsController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanDoc() => User.HasClaim("permission", PermissionNames.InventoryDocuments);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);   // fresh open = today; an explicit filter is respected
        var q = _db.StockCounts.AsQueryable();
        if (from is DateTime f) q = q.Where(d => d.Date >= f.Date);
        if (to is DateTime t) q = q.Where(d => d.Date < t.Date.AddDays(1));
        var rows = await (from d in q
                          join w in _db.Warehouses on d.WarehouseId equals w.Id into wj
                          from w in wj.DefaultIfEmpty()
                          orderby d.Number descending
                          select new DocListRow
                          {
                              Id = d.Id, Number = d.Number, Date = d.Date, Status = d.Status,
                              Warehouse = w != null ? w.Name : "", LineCount = d.Lines.Count, TotalCost = 0m
                          }).ToListAsync(ct);
        return View(new DocListVm { Rows = rows, From = from, To = to });
    }

    [HttpGet]
    public IActionResult Help() => PartialView("_Help");

    [HttpGet]
    public async Task<IActionResult> Excel(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(from, to, ct);
        return RealState.Web.Common.Xlsx.File($"الجرد {DateTime.Now:yyyy-MM-dd}.xlsx", "الجرد", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(from, to, ct);
        return View("ListPrint", InventoryExport.ToPrint("الجرد", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var src = _db.StockCounts.AsQueryable();
        if (from is DateTime ff) src = src.Where(d => d.Date >= ff.Date);
        if (to is DateTime tt) src = src.Where(d => d.Date < tt.Date.AddDays(1));
        var list = await (from d in src
                          join w in _db.Warehouses on d.WarehouseId equals w.Id into wj
                          from w in wj.DefaultIfEmpty()
                          orderby d.Number descending
                          select new { d.Number, d.Date, W = w != null ? w.Name : "", Lines = d.Lines.Count, d.Status }).ToListAsync(ct);
        var rows = list.Select(x => new object?[] { x.Number.ToString(), x.Date.ToString("yyyy/MM/dd"), x.W, x.Lines, StatusAr(x.Status) }).ToList();
        return (new() { "الرقم", "التاريخ", "المخزن", "الأصناف", "الحالة" }, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);
        var model = new CountFormModel { WarehouseId = await _db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct) };
        await FillAsync(model, ct);
        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
        var model = new CountFormModel { Id = d.Id, Number = d.Number, Date = d.Date, WarehouseId = d.WarehouseId, Notes = d.Notes, Status = d.Status };
        await FillAsync(model, ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        model.ExistingLines = d.Lines.Select(l => new DocLineVm { ProductId = l.ProductId, ProductLabel = names.GetValueOrDefault(l.ProductId, ""), SystemQty = l.SystemQty, CountedQty = l.CountedQty }).ToList();
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CountFormModel model, string? linesJson, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var lines = Parse(linesJson).Where(l => l.ProductId != Guid.Empty).ToList();
        if (model.WarehouseId == Guid.Empty) ModelState.AddModelError(string.Empty, "اختر المخزن.");
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "أضف صنفًا واحدًا على الأقل.");
        if (!ModelState.IsValid)
        {
            await FillAsync(model, ct);
            model.ExistingLines = lines.Select(l => new DocLineVm { ProductId = l.ProductId, CountedQty = l.CountedQty }).ToList();
            return View("Form", model);
        }

        StockCount d;
        if (model.Id == Guid.Empty)
        {
            d = new StockCount { Number = await _engine.NextNumberAsync(_db.StockCounts.Select(x => x.Number), _db.StockCounts.Local.Select(x => x.Number), model.Date.Year, ct) };
            _db.StockCounts.Add(d);
        }
        else
        {
            d = await _db.StockCounts.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (d is null) return NotFound();
            if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
            _db.StockCountLines.RemoveRange(await _db.StockCountLines.Where(l => l.StockCountId == d.Id).ToListAsync(ct));
        }
        d.Date = model.Date; d.WarehouseId = model.WarehouseId; d.Notes = model.Notes; d.Status = InventoryDocStatus.Draft;
        // Snapshot the system quantity at count time for each product.
        foreach (var l in lines)
        {
            var stock = await _engine.GetStockAsync(l.ProductId, model.WarehouseId, model.Date, ct);
            _db.StockCountLines.Add(new StockCountLine { StockCountId = d.Id, ProductId = l.ProductId, SystemQty = stock.Quantity, CountedQty = l.CountedQty });
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { TempData["ErrorMessage"] = "تعذّر الحفظ — قد يكون رقم المستند مستخدمًا بالفعل. أعد المحاولة."; return RedirectToAction(nameof(Index)); }
        TempData["StatusMessage"] = $"تم حفظ الجرد {d.Number} كمسودة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);   // seed/repair config first so posting stays one unit of work
        var d = await _db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "المستند مُرحَّل بالفعل."; return RedirectToAction(nameof(Index)); }
        try { await _engine.PostStockCountAsync(d, ct); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = $"تم ترحيل الجرد {d.Number}."; }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["ErrorMessage"] = "تم تعديل المستند من جلسة أخرى — أعد تحميل الصفحة وحاول مجددًا."; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.StockCounts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Posted) { TempData["ErrorMessage"] = "لا يمكن عكس مستند غير مُرحَّل."; return RedirectToAction(nameof(Index)); }
        try
        {
            await _engine.ReverseAsync(InventorySources.StockCount, d.Id, ct);
            d.Status = InventoryDocStatus.Reversed;
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم عكس الجرد {d.Number}.";
        }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["ErrorMessage"] = "تم تعديل المستند من جلسة أخرى — أعد تحميل الصفحة وحاول مجددًا."; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن حذف مستند مُرحَّل — استخدم العكس."; return RedirectToAction(nameof(Index)); }
        _db.StockCountLines.RemoveRange(d.Lines);
        _db.StockCounts.Remove(d);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف مسودة الجرد {d.Number}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var d = await _db.StockCounts.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var w = await _db.Warehouses.Where(x => x.Id == d.WarehouseId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        return PartialView("_Details", new DocDetailsVm
        {
            Title = "جرد مخزون", Number = d.Number, Date = d.Date, Warehouse = w ?? "", Status = StatusAr(d.Status), Notes = d.Notes,
            // Quantity = counted, UnitCost column repurposed to show the system qty, Total = difference.
            Lines = d.Lines.Select(l => new DocDetailLine { Product = names.GetValueOrDefault(l.ProductId, ""), Quantity = l.CountedQty, UnitCost = l.SystemQty, TotalCost = l.CountedQty - l.SystemQty }).ToList()
        });
    }

    // ---- helpers ----
    private List<CountLineInput> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<CountLineInput>>(json, JsonOpts) ?? new(); } catch { return new(); }
    }
    private async Task FillAsync(CountFormModel model, CancellationToken ct)
    {
        model.Warehouses = await _db.Warehouses.Where(w => w.IsActive).OrderBy(w => w.Name).Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToListAsync(ct);
        model.Products = await _db.Products.Where(p => p.IsActive && p.TrackInventory).OrderBy(p => p.Sku).Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Sku + " — " + p.Name }).ToListAsync(ct);
    }
    private async Task<Dictionary<Guid, string>> ProductNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var set = ids.ToList();
        return await _db.Products.Where(p => set.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku + " — " + p.Name, ct);
    }
    private static string StatusAr(InventoryDocStatus s) => s switch
    {
        InventoryDocStatus.Draft => "مسودة", InventoryDocStatus.Posted => "مُرحَّل", InventoryDocStatus.Reversed => "معكوس", _ => s.ToString()
    };
}
