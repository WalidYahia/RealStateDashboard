using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Services;

namespace RealState.Web.Controllers;

public class TxnCategoriesVm
{
    public List<TxnCategory> Expense { get; set; } = new();
    public List<TxnCategory> Income { get; set; } = new();
}

[Authorize(Policy = PermissionNames.TxnCategoriesManage)]
public class TxnCategoriesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ITxnCategoryService _svc;
    private readonly RealState.Application.Accounting.IAccountingService _accounting;

    public TxnCategoriesController(IApplicationDbContext db, ITxnCategoryService svc,
        RealState.Application.Accounting.IAccountingService accounting)
    {
        _db = db;
        _svc = svc;
        _accounting = accounting;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        await _svc.EnsureBuiltInsAsync(ct);
        return View(new TxnCategoriesVm
        {
            Expense = await _svc.ListAsync(TxnType.Expense, activeOnly: false, ct),
            Income = await _svc.ListAsync(TxnType.Income, activeOnly: false, ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TxnType type, string? name, CancellationToken ct)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0) { TempData["ErrorMessage"] = "أدخل اسم البند."; return RedirectToAction(nameof(Index)); }
        if (await _db.TxnCategories.AnyAsync(c => c.Type == type && c.Name == name, ct))
        { TempData["ErrorMessage"] = "يوجد بند بنفس الاسم في هذا النوع."; return RedirectToAction(nameof(Index)); }

        var cat = new TxnCategory
        {
            Type = type, Name = name, IsBuiltIn = false, BuiltInKind = AccountingEntryKind.General, SortOrder = 100, IsActive = true
        };
        _db.TxnCategories.Add(cat);
        await _accounting.EnsureCategoryAccountAsync(cat, ct);   // add it to دليل الحسابات
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تمت إضافة البند.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, string? name, CancellationToken ct)
    {
        var c = await _db.TxnCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.IsBuiltIn) { TempData["ErrorMessage"] = "لا يمكن تعديل بند مدمج."; return RedirectToAction(nameof(Index)); }
        name = (name ?? "").Trim();
        if (name.Length == 0) { TempData["ErrorMessage"] = "أدخل اسم البند."; return RedirectToAction(nameof(Index)); }
        if (await _db.TxnCategories.AnyAsync(x => x.Type == c.Type && x.Name == name && x.Id != c.Id, ct))
        { TempData["ErrorMessage"] = "يوجد بند بنفس الاسم في هذا النوع."; return RedirectToAction(nameof(Index)); }
        c.Name = name;
        await _accounting.EnsureCategoryAccountAsync(c, ct);   // sync the account name
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم تحديث البند.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.TxnCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.IsBuiltIn) { TempData["ErrorMessage"] = "لا يمكن حذف بند مدمج."; return RedirectToAction(nameof(Index)); }
        // Keep any transactions that used it; just unlink the category.
        foreach (var t in await _db.SafeTransactions.Where(t => t.CategoryId == id).ToListAsync(ct)) t.CategoryId = null;
        // Deactivate (not delete) its ledger account — it may carry posted journal lines.
        var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.SubKind == "TxnCategory" && a.SubRefId == id, ct);
        if (acc is not null) acc.IsActive = false;
        _db.TxnCategories.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حذف البند.";
        return RedirectToAction(nameof(Index));
    }
}
