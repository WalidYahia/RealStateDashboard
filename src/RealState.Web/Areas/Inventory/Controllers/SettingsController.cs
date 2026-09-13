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
[Authorize(Policy = PermissionNames.InventoryManage)]
public class SettingsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryEngine _engine;
    public SettingsController(IApplicationDbContext db, IInventoryEngine engine) { _db = db; _engine = engine; }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IActionResult> Index(string? tab, CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        var p = await _db.InventoryPostingProfiles.FirstAsync(ct);
        var model = new InventorySettingsModel
        {
            ActiveTab = tab is "categories" or "units" ? tab : "accounts",
            CostingMethod = p.CostingMethod,
            InventoryCode = p.InventoryCode, CogsCode = p.CogsCode, PurchaseGrniCode = p.PurchaseGrniCode,
            AdjustmentGainCode = p.AdjustmentGainCode, AdjustmentLossCode = p.AdjustmentLossCode,
            ConsumptionExpenseCode = p.ConsumptionExpenseCode
        };
        await FillAsync(model, ct);
        return View(model);
    }

    // ---------------- Tab 1: posting profile ----------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(InventorySettingsModel model, CancellationToken ct)
    {
        await _engine.EnsureDefaultsAsync(ct);
        var p = await _db.InventoryPostingProfiles.FirstAsync(ct);

        // The inventory account is the control account that every warehouse sub-account hangs under —
        // it is structural, so it is never taken from the form.
        model.InventoryCode = p.InventoryCode;
        model.CostingMethod = CostingMethod.WeightedAverage;   // the only implemented method
        model.ActiveTab = "accounts";

        await ValidateAsync(model, ct);
        if (!ModelState.IsValid) { await FillAsync(model, ct); return View(model); }

        p.CogsCode = model.CogsCode;
        p.PurchaseGrniCode = model.PurchaseGrniCode;
        p.AdjustmentGainCode = model.AdjustmentGainCode;
        p.AdjustmentLossCode = model.AdjustmentLossCode;
        p.ConsumptionExpenseCode = model.ConsumptionExpenseCode;
        p.CostingMethod = model.CostingMethod;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ إعدادات الترحيل المحاسبي.";
        return RedirectToAction(nameof(Index), new { tab = "accounts" });
    }

    // ---------------- Tab 2: product categories ----------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategories(string? payload, CancellationToken ct)
    {
        var rows = Parse<CategoryRowInput>(payload);
        var errors = new List<string>();

        if (rows.Any(r => string.IsNullOrWhiteSpace(r.Name)))
            errors.Add("اسم التصنيف مطلوب في كل سطر.");
        if (rows.GroupBy(r => (r.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            errors.Add("لا يمكن تكرار اسم التصنيف.");

        var existing = await _db.ProductCategories.ToListAsync(ct);
        var keep = rows.Select(r => ParseId(r.Id)).Where(id => id != Guid.Empty).ToHashSet();

        foreach (var c in existing.Where(c => !keep.Contains(c.Id)))
        {
            if (await _db.Products.AnyAsync(p => p.CategoryId == c.Id, ct))
                errors.Add($"لا يمكن حذف التصنيف «{c.Name}» لارتباطه بأصناف.");
        }
        if (errors.Count > 0)
        {
            TempData["ErrorMessage"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Index), new { tab = "categories" });
        }

        foreach (var c in existing.Where(c => !keep.Contains(c.Id)))
            _db.ProductCategories.Remove(c);

        foreach (var r in rows)
        {
            var id = ParseId(r.Id);
            var name = (r.Name ?? "").Trim();
            var order = int.TryParse(r.SortOrder, out var o) ? o : 0;
            var current = existing.FirstOrDefault(c => c.Id == id);
            if (current is null)
                _db.ProductCategories.Add(new ProductCategory { Name = name, SortOrder = order, IsActive = r.IsActive });
            else { current.Name = name; current.SortOrder = order; current.IsActive = r.IsActive; }
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ تصنيفات الأصناف.";
        return RedirectToAction(nameof(Index), new { tab = "categories" });
    }

    // ---------------- Tab 3: units of measure ----------------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUnits(string? payload, CancellationToken ct)
    {
        var rows = Parse<UnitRowInput>(payload);
        var errors = new List<string>();

        if (rows.Any(r => string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name)))
            errors.Add("الكود والاسم مطلوبان في كل سطر.");
        if (rows.GroupBy(r => (r.Code ?? "").Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            errors.Add("لا يمكن تكرار كود الوحدة.");

        var existing = await _db.UnitsOfMeasure.ToListAsync(ct);
        var keep = rows.Select(r => ParseId(r.Id)).Where(id => id != Guid.Empty).ToHashSet();

        foreach (var u in existing.Where(u => !keep.Contains(u.Id)))
        {
            if (await _db.Products.AnyAsync(p => p.UnitOfMeasureId == u.Id, ct))
                errors.Add($"لا يمكن حذف الوحدة «{u.Name}» لارتباطها بأصناف.");
        }
        if (errors.Count > 0)
        {
            TempData["ErrorMessage"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Index), new { tab = "units" });
        }

        foreach (var u in existing.Where(u => !keep.Contains(u.Id)))
            _db.UnitsOfMeasure.Remove(u);

        foreach (var r in rows)
        {
            var id = ParseId(r.Id);
            var code = (r.Code ?? "").Trim();
            var name = (r.Name ?? "").Trim();
            var current = existing.FirstOrDefault(u => u.Id == id);
            if (current is null)
                _db.UnitsOfMeasure.Add(new UnitOfMeasure { Code = code, Name = name, IsActive = r.IsActive });
            else { current.Code = code; current.Name = name; current.IsActive = r.IsActive; }
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            TempData["ErrorMessage"] = "تعذّر الحفظ — تأكد من عدم تكرار كود الوحدة.";
            return RedirectToAction(nameof(Index), new { tab = "units" });
        }
        TempData["StatusMessage"] = "تم حفظ وحدات القياس.";
        return RedirectToAction(nameof(Index), new { tab = "units" });
    }

    // ---------------- helpers ----------------
    private static List<T> Parse<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new(); } catch { return new(); }
    }

    private static Guid ParseId(string? id) => Guid.TryParse(id, out var g) ? g : Guid.Empty;

    /// <summary>Each mapped account must exist, be postable, and be of the right type for its role.</summary>
    private async Task ValidateAsync(InventorySettingsModel m, CancellationToken ct)
    {
        var codes = new[] { m.CogsCode, m.PurchaseGrniCode, m.AdjustmentGainCode, m.AdjustmentLossCode, m.ConsumptionExpenseCode };
        var accounts = await _db.Accounts.Where(a => codes.Contains(a.Code)).ToListAsync(ct);

        void Check(string? code, string field, string label, AccountType expected)
        {
            var a = accounts.FirstOrDefault(x => x.Code == code);
            if (string.IsNullOrWhiteSpace(code) || a is null) { ModelState.AddModelError(field, $"حساب {label} غير موجود في دليل الحسابات."); return; }
            if (!a.IsPostable) ModelState.AddModelError(field, $"حساب {label} يجب أن يكون حسابًا ترحيليًا (يقبل القيود).");
            else if (a.Type != expected) ModelState.AddModelError(field, $"حساب {label} يجب أن يكون من نوع «{TypeAr(expected)}».");
        }

        Check(m.CogsCode, nameof(m.CogsCode), "تكلفة المبيعات", AccountType.Expense);
        Check(m.PurchaseGrniCode, nameof(m.PurchaseGrniCode), "بضاعة واردة لم تُفوتر", AccountType.Liability);
        Check(m.AdjustmentGainCode, nameof(m.AdjustmentGainCode), "أرباح التسويات", AccountType.Revenue);
        Check(m.AdjustmentLossCode, nameof(m.AdjustmentLossCode), "خسائر التسويات", AccountType.Expense);
        Check(m.ConsumptionExpenseCode, nameof(m.ConsumptionExpenseCode), "مصروف الاستهلاك", AccountType.Expense);
    }

    private static string TypeAr(AccountType t) => t switch
    {
        AccountType.Asset => "أصول",
        AccountType.Liability => "خصوم",
        AccountType.Equity => "حقوق ملكية",
        AccountType.Revenue => "إيرادات",
        AccountType.Expense => "مصروفات",
        _ => t.ToString()
    };

    private async Task FillAsync(InventorySettingsModel model, CancellationToken ct)
    {
        model.Accounts = await _db.Accounts.Where(a => a.IsPostable && a.IsActive).OrderBy(a => a.Code)
            .Select(a => new SelectListItem { Value = a.Code, Text = a.Code + " — " + a.Name }).ToListAsync(ct);
        var inv = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == model.InventoryCode, ct);
        model.InventoryAccountLabel = inv is null ? model.InventoryCode : $"{inv.Code} — {inv.Name}";
        model.Categories = await _db.ProductCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
        model.Units = await _db.UnitsOfMeasure.OrderBy(u => u.Code).ToListAsync(ct);
    }
}
