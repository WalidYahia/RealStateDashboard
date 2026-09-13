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
public class TransfersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public TransfersController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanDoc() => User.HasClaim("permission", PermissionNames.InventoryDocuments);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);   // fresh open = today; an explicit filter is respected
        var q = _db.StockTransfers.AsQueryable();
        if (from is DateTime f) q = q.Where(d => d.Date >= f.Date);
        if (to is DateTime t) q = q.Where(d => d.Date < t.Date.AddDays(1));
        var rows = await (from d in q
                          join w in _db.Warehouses on d.FromWarehouseId equals w.Id into wj
                          from w in wj.DefaultIfEmpty()
                          orderby d.Number descending
                          select new DocListRow
                          {
                              Id = d.Id, Number = d.Number, Date = d.Date, Status = d.Status,
                              Warehouse = w != null ? w.Name : "", LineCount = d.Lines.Count,
                              TotalCost = d.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m
                          }).ToListAsync(ct);
        return View(new DocListVm { Rows = rows, From = from, To = to });
    }

    [HttpGet]
    public IActionResult Help() => PartialView("_Help");

    [HttpGet]
    public async Task<IActionResult> Excel(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(from, to, ct);
        return RealState.Web.Common.Xlsx.File($"التحويلات {DateTime.Now:yyyy-MM-dd}.xlsx", "التحويلات", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(from, to, ct);
        return View("ListPrint", InventoryExport.ToPrint("التحويلات المخزنية", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var src = _db.StockTransfers.AsQueryable();
        if (from is DateTime ff) src = src.Where(d => d.Date >= ff.Date);
        if (to is DateTime tt) src = src.Where(d => d.Date < tt.Date.AddDays(1));
        var list = await (from d in src
                          join w in _db.Warehouses on d.FromWarehouseId equals w.Id into wj
                          from w in wj.DefaultIfEmpty()
                          join w2 in _db.Warehouses on d.ToWarehouseId equals w2.Id into w2j
                          from w2 in w2j.DefaultIfEmpty()
                          orderby d.Number descending
                          select new { d.Number, d.Date, From = w != null ? w.Name : "", To = w2 != null ? w2.Name : "", Lines = d.Lines.Count, Total = d.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m, d.Status }).ToListAsync(ct);
        var rows = list.Select(x => new object?[] { x.Number.ToString(), x.Date.ToString("yyyy/MM/dd"), x.From, x.To, x.Lines, x.Total, StatusAr(x.Status) }).ToList();
        return (new() { "الرقم", "التاريخ", "من مخزن", "إلى مخزن", "الأسطر", "التكلفة", "الحالة" }, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);
        var model = new TransferFormModel { FromWarehouseId = await _db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct) };
        await FillAsync(model, ct);
        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
        var model = new TransferFormModel { Id = d.Id, Number = d.Number, Date = d.Date, FromWarehouseId = d.FromWarehouseId, ToWarehouseId = d.ToWarehouseId, Notes = d.Notes, Status = d.Status };
        await FillAsync(model, ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        model.ExistingLines = d.Lines.Select(l => new DocLineVm { ProductId = l.ProductId, ProductLabel = names.GetValueOrDefault(l.ProductId, ""), Quantity = l.Quantity }).ToList();
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(TransferFormModel model, string? linesJson, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var lines = Parse(linesJson).Where(l => l.ProductId != Guid.Empty && l.Quantity > 0).ToList();
        if (model.FromWarehouseId == Guid.Empty || model.ToWarehouseId == Guid.Empty) ModelState.AddModelError(string.Empty, "اختر المخزنين.");
        if (model.FromWarehouseId == model.ToWarehouseId) ModelState.AddModelError(string.Empty, "لا يمكن التحويل إلى نفس المخزن.");
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "أضف سطرًا واحدًا على الأقل بكمية موجبة.");
        if (!ModelState.IsValid)
        {
            await FillAsync(model, ct);
            model.ExistingLines = lines.Select(l => new DocLineVm { ProductId = l.ProductId, Quantity = l.Quantity }).ToList();
            return View("Form", model);
        }

        StockTransfer d;
        if (model.Id == Guid.Empty)
        {
            d = new StockTransfer { Number = await _engine.NextNumberAsync(_db.StockTransfers.Select(x => x.Number), _db.StockTransfers.Local.Select(x => x.Number), model.Date.Year, ct) };
            _db.StockTransfers.Add(d);
        }
        else
        {
            d = await _db.StockTransfers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (d is null) return NotFound();
            if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
            _db.StockTransferLines.RemoveRange(await _db.StockTransferLines.Where(l => l.StockTransferId == d.Id).ToListAsync(ct));
        }
        d.Date = model.Date; d.FromWarehouseId = model.FromWarehouseId; d.ToWarehouseId = model.ToWarehouseId; d.Notes = model.Notes; d.Status = InventoryDocStatus.Draft;
        foreach (var l in lines)
            _db.StockTransferLines.Add(new StockTransferLine { StockTransferId = d.Id, ProductId = l.ProductId, Quantity = l.Quantity });
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { TempData["ErrorMessage"] = "تعذّر الحفظ — قد يكون رقم المستند مستخدمًا بالفعل. أعد المحاولة."; return RedirectToAction(nameof(Index)); }
        TempData["StatusMessage"] = $"تم حفظ التحويل {d.Number} كمسودة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);   // seed/repair config first so posting stays one unit of work
        var d = await _db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "المستند مُرحَّل بالفعل."; return RedirectToAction(nameof(Index)); }
        try { await _engine.PostTransferAsync(d, ct); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = $"تم ترحيل التحويل {d.Number}."; }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["ErrorMessage"] = "تم تعديل المستند من جلسة أخرى — أعد تحميل الصفحة وحاول مجددًا."; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.StockTransfers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Posted) { TempData["ErrorMessage"] = "لا يمكن عكس مستند غير مُرحَّل."; return RedirectToAction(nameof(Index)); }
        try
        {
            await _engine.ReverseAsync(InventorySources.StockTransfer, d.Id, ct);
            d.Status = InventoryDocStatus.Reversed;
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم عكس التحويل {d.Number}.";
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
        var d = await _db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن حذف مستند مُرحَّل — استخدم العكس."; return RedirectToAction(nameof(Index)); }
        _db.StockTransferLines.RemoveRange(d.Lines);
        _db.StockTransfers.Remove(d);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف مسودة التحويل {d.Number}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var d = await _db.StockTransfers.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var from = await _db.Warehouses.Where(x => x.Id == d.FromWarehouseId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var to = await _db.Warehouses.Where(x => x.Id == d.ToWarehouseId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        return PartialView("_Details", new DocDetailsVm
        {
            Title = "تحويل مخزني", Number = d.Number, Date = d.Date, Warehouse = $"{from} ← {to}", Status = StatusAr(d.Status), Notes = d.Notes,
            Lines = d.Lines.Select(l => new DocDetailLine { Product = names.GetValueOrDefault(l.ProductId, ""), Quantity = l.Quantity, UnitCost = l.UnitCost, TotalCost = l.TotalCost }).ToList()
        });
    }

    // ---- helpers ----
    private List<TransferLineInput> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<TransferLineInput>>(json, JsonOpts) ?? new(); } catch { return new(); }
    }
    private async Task FillAsync(TransferFormModel model, CancellationToken ct)
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
