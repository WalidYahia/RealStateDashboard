using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// فئات الأصول الثابتة — asset categories and their posting profile: which chart-of-accounts accounts the category's
/// assets post to (cost / accumulated depreciation / depreciation expense). Left on «تلقائي», the cost and accumulated
/// depreciation accounts are created under 1500 / 1590 named after the category, and the expense is «مصروف الإهلاك».
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.FixedAssetsView)]
public class FixedAssetCategoriesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IFixedAssetAccounting _assets;

    public FixedAssetCategoriesController(IApplicationDbContext db, IFixedAssetAccounting assets)
    {
        _db = db;
        _assets = assets;
    }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.FixedAssetsManage);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var categories = await _db.FixedAssetCategories.OrderBy(c => c.Name).ToListAsync(ct);
        var accounts = await _db.Accounts.ToDictionaryAsync(a => a.Id, a => $"{a.Code} — {a.Name}", ct);
        var assets = await _db.FixedAssets.Where(a => a.DisposalDate == null)
            .Select(a => new { a.Id, a.CategoryId, Cost = a.UnitCost * a.Quantity, a.OpeningAccumulatedDepreciation }).ToListAsync(ct);
        var depreciated = await _db.FixedAssetDepreciations.GroupBy(d => d.FixedAssetId)
            .Select(g => new { g.Key, Sum = g.Sum(d => d.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        string? Acc(Guid? id) => id is Guid g ? accounts.GetValueOrDefault(g) : null;

        ViewData["CanManage"] = CanManage();
        return View(categories.Select(c =>
        {
            var mine = assets.Where(a => a.CategoryId == c.Id).ToList();
            var cost = mine.Sum(a => a.Cost);
            return new FixedAssetCategoryRow
            {
                Id = c.Id, Name = c.Name, DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
                AssetAccount = Acc(c.AssetAccountId), AccumulatedAccount = Acc(c.AccumulatedDepreciationAccountId),
                ExpenseAccount = Acc(c.DepreciationExpenseAccountId),
                AssetsCount = mine.Count, Cost = cost,
                BookValue = cost - mine.Sum(a => a.OpeningAccumulatedDepreciation + depreciated.GetValueOrDefault(a.Id))
            };
        }).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var model = new FixedAssetCategoryFormModel();
        if (id is Guid cid && cid != Guid.Empty)
        {
            var c = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null) return NotFound();
            model = new FixedAssetCategoryFormModel
            {
                Id = c.Id, Name = c.Name, DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths, AssetAccountId = c.AssetAccountId,
                AccumulatedDepreciationAccountId = c.AccumulatedDepreciationAccountId, DepreciationExpenseAccountId = c.DepreciationExpenseAccountId,
                HasPostings = await _db.FixedAssets.AnyAsync(a => a.CategoryId == c.Id && a.AcquisitionMethod != null, ct)
            };
        }
        return PartialView("_CategoryForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(FixedAssetCategoryFormModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var name = model.Name?.Trim() ?? "";
        if (name.Length > 0 && await _db.FixedAssetCategories.AnyAsync(c => c.Name == name && c.Id != model.Id, ct))
            ModelState.AddModelError(nameof(model.Name), "توجد فئة بنفس الاسم.");
        // Picked accounts must be postable and of the right type (asset / expense).
        foreach (var (id, field, type) in new[]
                 {
                     (model.AssetAccountId, nameof(model.AssetAccountId), AccountType.Asset),
                     (model.AccumulatedDepreciationAccountId, nameof(model.AccumulatedDepreciationAccountId), AccountType.Asset),
                     (model.DepreciationExpenseAccountId, nameof(model.DepreciationExpenseAccountId), AccountType.Expense),
                 })
            if (id is Guid g && !await _db.Accounts.AnyAsync(a => a.Id == g && a.IsPostable && a.Type == type, ct))
                ModelState.AddModelError(field, "اختر حسابًا فرعيًا (قابلًا للترحيل) من النوع الصحيح.");
        if (!ModelState.IsValid) return PartialView("_CategoryForm", await FillAsync(model, ct));

        var isEdit = model.Id != Guid.Empty;
        FixedAssetCategory c;
        if (isEdit)
        {
            var existing = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (existing is null) return NotFound();
            c = existing;
        }
        else
        {
            c = new FixedAssetCategory();
            _db.FixedAssetCategories.Add(c);
        }
        c.Name = name;
        c.DefaultUsefulLifeMonths = model.DefaultUsefulLifeMonths;
        c.AssetAccountId = model.AssetAccountId;
        c.AccumulatedDepreciationAccountId = model.AccumulatedDepreciationAccountId;
        c.DepreciationExpenseAccountId = model.DepreciationExpenseAccountId;
        await _assets.EnsureCategoryAccountsAsync(c, ct);   // «تلقائي» → the category's own accounts

        // Accounts created for this category follow its name.
        foreach (var acc in await _db.Accounts.Where(a => a.SubRefId == c.Id && (a.SubKind == "AssetCategory" || a.SubKind == "AssetCategoryDepreciation")).ToListAsync(ct))
            acc.Name = acc.SubKind == "AssetCategory" ? c.Name : $"مجمع إهلاك — {c.Name}";

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = isEdit ? $"تم تعديل فئة الأصول «{c.Name}»." : $"تمت إضافة فئة الأصول «{c.Name}» وحساباتها.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (await _db.FixedAssets.IgnoreQueryFilters().AnyAsync(a => a.CategoryId == id && !a.IsDeleted, ct))
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;   // refused — nothing changed
            TempData["ErrorMessage"] = $"لا يمكن حذف الفئة «{c.Name}» لوجود أصول عليها.";
            return RedirectToAction(nameof(Index));
        }
        _db.FixedAssetCategories.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف فئة الأصول «{c.Name}».";
        return RedirectToAction(nameof(Index));
    }

    private async Task<FixedAssetCategoryFormModel> FillAsync(FixedAssetCategoryFormModel m, CancellationToken ct)
    {
        var accounts = await _db.Accounts.Where(a => a.IsPostable && a.IsActive && (a.Type == AccountType.Asset || a.Type == AccountType.Expense))
            .OrderBy(a => a.Code).Select(a => new { a.Id, a.Code, a.Name, a.Type }).ToListAsync(ct);
        m.AssetAccounts = accounts.Where(a => a.Type == AccountType.Asset).Select(a => new SelectListItem($"{a.Code} — {a.Name}", a.Id.ToString())).ToList();
        m.ExpenseAccounts = accounts.Where(a => a.Type == AccountType.Expense).Select(a => new SelectListItem($"{a.Code} — {a.Name}", a.Id.ToString())).ToList();
        return m;
    }
}
