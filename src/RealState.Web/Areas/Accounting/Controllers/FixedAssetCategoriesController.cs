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
/// فئات الأصول الثابتة — asset categories. In the chart of accounts each category is a group under الأصول الثابتة (1500)
/// and مجمع إهلاك الأصول الثابتة (1590), named after it, holding one account per asset (الأصول › الأصول الثابتة › الفئة ›
/// الأصل); its depreciation expense account is picked here (default «مصروف الإهلاك»).
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

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var categories = await _db.FixedAssetCategories.OrderBy(c => c.Name).ToListAsync(ct);
        var accounts = await _db.Accounts.ToDictionaryAsync(a => a.Id, a => $"{a.Code} — {a.Name}", ct);
        var assets = await _db.FixedAssets.Where(a => a.DisposalDate == null)
            .Select(a => new { a.Id, a.CategoryId, Cost = a.UnitCost * a.Quantity, a.OpeningAccumulatedDepreciation }).ToListAsync(ct);
        var depreciated = await _db.FixedAssetDepreciations.GroupBy(d => d.FixedAssetId)
            .Select(g => new { g.Key, Sum = g.Sum(d => d.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        string? Acc(Guid? id) => id is Guid g ? accounts.GetValueOrDefault(g) : null;

        ViewData["CanCreate"] = Can(PermissionNames.FixedAssetCategoriesCreate);
        ViewData["CanEdit"] = Can(PermissionNames.FixedAssetCategoriesEdit);
        ViewData["CanDelete"] = Can(PermissionNames.FixedAssetCategoriesDelete);
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
        if (!Can(id is Guid g && g != Guid.Empty ? PermissionNames.FixedAssetCategoriesEdit : PermissionNames.FixedAssetCategoriesCreate)) return Forbid();
        var model = new FixedAssetCategoryFormModel();
        if (id is Guid cid && cid != Guid.Empty)
        {
            var c = await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null) return NotFound();
            model = new FixedAssetCategoryFormModel
            {
                Id = c.Id, Name = c.Name, DefaultUsefulLifeMonths = c.DefaultUsefulLifeMonths,
                DepreciationExpenseAccountId = c.DepreciationExpenseAccountId,
                HasPostings = await _db.FixedAssets.AnyAsync(a => a.CategoryId == c.Id && a.AcquisitionMethod != null, ct)
            };
        }
        return PartialView("_CategoryForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(FixedAssetCategoryFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id != Guid.Empty ? PermissionNames.FixedAssetCategoriesEdit : PermissionNames.FixedAssetCategoriesCreate)) return Forbid();
        var name = model.Name?.Trim() ?? "";
        if (name.Length > 0 && await _db.FixedAssetCategories.AnyAsync(c => c.Name == name && c.Id != model.Id, ct))
            ModelState.AddModelError(nameof(model.Name), "توجد فئة بنفس الاسم.");
        if (model.DepreciationExpenseAccountId is Guid g && !await _db.Accounts.AnyAsync(a => a.Id == g && a.IsPostable && a.Type == AccountType.Expense, ct))
            ModelState.AddModelError(nameof(model.DepreciationExpenseAccountId), "اختر حساب مصروفات فرعيًا (قابلًا للترحيل).");
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
        c.DepreciationExpenseAccountId = model.DepreciationExpenseAccountId;
        await _assets.EnsureCategoryAccountsAsync(c, ct);   // its groups under 1500 / 1590, and «تلقائي» → مصروف الإهلاك

        // The category's groups follow its name.
        foreach (var acc in await _db.Accounts.Where(a => a.SubRefId == c.Id
                     && (a.SubKind == FixedAssetAccounting.CategoryCostKind || a.SubKind == FixedAssetAccounting.CategoryAccumulatedKind)).ToListAsync(ct))
            acc.Name = acc.SubKind == FixedAssetAccounting.CategoryCostKind ? c.Name : FixedAssetAccounting.CategoryAccumulatedName(c.Name);

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = isEdit ? $"تم تعديل فئة الأصول «{c.Name}»." : $"تمت إضافة فئة الأصول «{c.Name}» وحساباتها.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetCategoriesDelete)) return Forbid();
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
        m.ExpenseAccounts = await _db.Accounts.Where(a => a.IsPostable && a.IsActive && a.Type == AccountType.Expense).OrderBy(a => a.Code)
            .Select(a => new SelectListItem(a.Code + " — " + a.Name, a.Id.ToString())).ToListAsync(ct);
        if (m.Id != Guid.Empty)
        {
            var groups = await _db.Accounts.Where(a => a.SubRefId == m.Id
                    && (a.SubKind == FixedAssetAccounting.CategoryCostKind || a.SubKind == FixedAssetAccounting.CategoryAccumulatedKind))
                .Select(a => new { a.SubKind, Text = a.Code + " — " + a.Name }).ToListAsync(ct);
            m.CostGroup = groups.FirstOrDefault(x => x.SubKind == FixedAssetAccounting.CategoryCostKind)?.Text;
            m.AccumulatedGroup = groups.FirstOrDefault(x => x.SubKind == FixedAssetAccounting.CategoryAccumulatedKind)?.Text;
        }
        return m;
    }
}
