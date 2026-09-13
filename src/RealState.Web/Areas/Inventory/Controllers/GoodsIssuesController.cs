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
public class GoodsIssuesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public GoodsIssuesController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private bool CanDoc() => User.HasClaim("permission", PermissionNames.InventoryDocuments);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);   // fresh open = today; an explicit filter is respected
        var q = _db.GoodsIssues.AsQueryable();
        if (from is DateTime f) q = q.Where(d => d.Date >= f.Date);
        if (to is DateTime t) q = q.Where(d => d.Date < t.Date.AddDays(1));
        var rows = await (from d in q
                          join w in _db.Warehouses on d.WarehouseId equals w.Id into wj
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
        return RealState.Web.Common.Xlsx.File($"أذون الصرف {DateTime.Now:yyyy-MM-dd}.xlsx", "أذون الصرف", h, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Print(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var (h, rows) = await BuildExportAsync(from, to, ct);
        return View("ListPrint", InventoryExport.ToPrint("أذون الصرف", h, rows));
    }

    private async Task<(List<string> Headers, List<object?[]> Rows)> BuildExportAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var src = _db.GoodsIssues.AsQueryable();
        if (from is DateTime ff) src = src.Where(d => d.Date >= ff.Date);
        if (to is DateTime tt) src = src.Where(d => d.Date < tt.Date.AddDays(1));
        var list = await (from d in src
                          join w in _db.Warehouses on d.WarehouseId equals w.Id into wj
                          from w in wj.DefaultIfEmpty()
                          orderby d.Number descending
                          select new { d.Number, d.Date, W = w != null ? w.Name : "", Lines = d.Lines.Count, Total = d.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m, d.Status }).ToListAsync(ct);
        var rows = list.Select(x => new object?[] { x.Number.ToString(), x.Date.ToString("yyyy/MM/dd"), x.W, x.Lines, x.Total, StatusAr(x.Status) }).ToList();
        return (new() { "الرقم", "التاريخ", "المخزن", "الأسطر", "التكلفة", "الحالة" }, rows);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);
        var model = new IssueFormModel { WarehouseId = await _db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstOrDefaultAsync(ct) };
        await FillAsync(model, ct);
        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
        var model = new IssueFormModel { Id = d.Id, Number = d.Number, Date = d.Date, WarehouseId = d.WarehouseId, Reason = d.Reason, Notes = d.Notes, Status = d.Status };
        await FillAsync(model, ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        model.ExistingLines = d.Lines.Select(l => new DocLineVm { ProductId = l.ProductId, ProductLabel = names.GetValueOrDefault(l.ProductId, ""), Quantity = l.Quantity }).ToList();
        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IssueFormModel model, string? linesJson, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var lines = Parse(linesJson).Where(l => l.ProductId != Guid.Empty && l.Quantity > 0).ToList();
        if (model.WarehouseId == Guid.Empty) ModelState.AddModelError(string.Empty, "اختر المخزن.");
        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "أضف سطرًا واحدًا على الأقل بكمية موجبة.");
        if (!ModelState.IsValid)
        {
            await FillAsync(model, ct);
            model.ExistingLines = lines.Select(l => new DocLineVm { ProductId = l.ProductId, Quantity = l.Quantity }).ToList();
            return View("Form", model);
        }

        GoodsIssue d;
        if (model.Id == Guid.Empty)
        {
            d = new GoodsIssue { Number = await _engine.NextNumberAsync(_db.GoodsIssues.Select(x => x.Number), _db.GoodsIssues.Local.Select(x => x.Number), model.Date.Year, ct) };
            _db.GoodsIssues.Add(d);
        }
        else
        {
            d = await _db.GoodsIssues.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (d is null) return NotFound();
            if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن تعديل مستند مُرحَّل."; return RedirectToAction(nameof(Index)); }
            _db.GoodsIssueLines.RemoveRange(await _db.GoodsIssueLines.Where(l => l.GoodsIssueId == d.Id).ToListAsync(ct));
        }
        d.Date = model.Date; d.WarehouseId = model.WarehouseId; d.Reason = model.Reason; d.Notes = model.Notes; d.Status = InventoryDocStatus.Draft;
        foreach (var l in lines)
            _db.GoodsIssueLines.Add(new GoodsIssueLine { GoodsIssueId = d.Id, ProductId = l.ProductId, Quantity = l.Quantity });
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { TempData["ErrorMessage"] = "تعذّر الحفظ — قد يكون رقم المستند مستخدمًا بالفعل. أعد المحاولة."; return RedirectToAction(nameof(Index)); }
        TempData["StatusMessage"] = $"تم حفظ إذن الصرف {d.Number} كمسودة.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        await _engine.EnsureDefaultsAsync(ct);   // seed/repair config first so posting stays one unit of work
        var d = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "المستند مُرحَّل بالفعل."; return RedirectToAction(nameof(Index)); }
        try { await _engine.PostIssueAsync(d, ct); await _db.SaveChangesAsync(ct); TempData["StatusMessage"] = $"تم ترحيل إذن الصرف {d.Number}."; }
        catch (InvalidOperationException ex) { TempData["ErrorMessage"] = ex.Message; }
        catch (DbUpdateConcurrencyException) { TempData["ErrorMessage"] = "تم تعديل المستند من جلسة أخرى — أعد تحميل الصفحة وحاول مجددًا."; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id, CancellationToken ct)
    {
        if (!CanDoc()) return Forbid();
        var d = await _db.GoodsIssues.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Posted) { TempData["ErrorMessage"] = "لا يمكن عكس مستند غير مُرحَّل."; return RedirectToAction(nameof(Index)); }
        try
        {
            await _engine.ReverseAsync(InventorySources.GoodsIssue, d.Id, ct);
            d.Status = InventoryDocStatus.Reversed;
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم عكس إذن الصرف {d.Number}.";
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
        var d = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        if (d.Status != InventoryDocStatus.Draft) { TempData["ErrorMessage"] = "لا يمكن حذف مستند مُرحَّل — استخدم العكس."; return RedirectToAction(nameof(Index)); }
        _db.GoodsIssueLines.RemoveRange(d.Lines);
        _db.GoodsIssues.Remove(d);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف مسودة إذن الصرف {d.Number}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var d = await _db.GoodsIssues.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var w = await _db.Warehouses.Where(x => x.Id == d.WarehouseId).Select(x => x.Name).FirstOrDefaultAsync(ct);
        var names = await ProductNamesAsync(d.Lines.Select(l => l.ProductId), ct);
        return PartialView("_Details", new DocDetailsVm
        {
            Title = "إذن صرف", Number = d.Number, Date = d.Date, Warehouse = w ?? "", Status = StatusAr(d.Status), Notes = d.Notes,
            Lines = d.Lines.Select(l => new DocDetailLine { Product = names.GetValueOrDefault(l.ProductId, ""), Quantity = l.Quantity, UnitCost = l.UnitCost, TotalCost = l.TotalCost }).ToList()
        });
    }

    // ---- helpers ----
    private List<IssueLineInput> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<IssueLineInput>>(json, JsonOpts) ?? new(); } catch { return new(); }
    }
    private async Task FillAsync(IssueFormModel model, CancellationToken ct)
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
