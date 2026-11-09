using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// إهلاك الأصول الثابتة — depreciation is recorded by the user, on an asset, when they decide (nothing is calculated or
/// posted automatically): an amount at a date, Dr مصروف الإهلاك / Cr مجمع الإهلاك (the asset category's accounts). It
/// never takes the asset below its salvage value. An entry can be deleted (its entry reversed) while the asset isn't
/// disposed of — a disposal closes the depreciation recorded up to it. This page is the register of every entry; the
/// asset's page records and lists its own.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.FixedAssetsView)]
public class FixedAssetDepreciationController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IFixedAssetAccounting _accounting;

    public FixedAssetDepreciationController(IApplicationDbContext db, IFixedAssetAccounting accounting)
    {
        _db = db;
        _accounting = accounting;
    }

    private bool CanDepreciate() => User.HasClaim("permission", PermissionNames.FixedAssetsDepreciate);
    private static string FA(int number) => "FA-" + number;
    private void Refused() => HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;

    // ---------- Register ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? categoryId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var catNames = await _db.FixedAssetCategories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var assets = await _db.FixedAssets.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);

        var q = _db.FixedAssetDepreciations.AsNoTracking();
        var (fromAt, toAt) = ClientTime.DayRange(from, to);   // [from, to) of the picked days
        if (fromAt.HasValue) q = q.Where(d => d.Date >= fromAt.Value);
        if (toAt.HasValue) q = q.Where(d => d.Date < toAt.Value);
        var entries = await q.OrderByDescending(d => d.Date).ToListAsync(ct);

        ViewData["CanDepreciate"] = CanDepreciate();
        ViewData["CanDeleteDepreciation"] = User.HasClaim("permission", PermissionNames.FixedAssetsDeleteDepreciation);
        return View(new DepreciationVm
        {
            From = from, To = to, CategoryId = categoryId,
            Categories = catNames.OrderBy(c => c.Value).Select(c => new SelectListItem(c.Value, c.Key.ToString())).ToList(),
            DepreciableCount = assets.Values.Count(a => a.IsPosted && !a.IsDisposed),
            Rows = entries
                .Where(d => assets.ContainsKey(d.FixedAssetId))
                .Select(d => (d, a: assets[d.FixedAssetId]))
                .Where(x => categoryId is null || x.a.CategoryId == categoryId)
                .Select(x => new DepreciationRow
                {
                    Id = x.d.Id, Date = x.d.Date, Amount = x.d.Amount, Notes = x.d.Notes, CreatedBy = x.d.CreatedBy,
                    AssetId = x.a.Id, Number = x.a.Number, Name = x.a.Name, AssetDisposed = x.a.IsDisposed,
                    Category = x.a.CategoryId is Guid c ? catNames.GetValueOrDefault(c) : null
                }).ToList()
        });
    }

    // ---------- Record a depreciation (modal — from this page, or from the asset's page with the asset fixed) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? assetId, CancellationToken ct)
    {
        if (!CanDepreciate()) return Forbid();
        var model = new DepreciationFormModel { AssetId = assetId, AssetLocked = assetId.HasValue };
        await FillAsync(model, ct);
        if (assetId is Guid id && model.Assets.All(o => o.Id != id))
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">لا يمكن إهلاك هذا الأصل الآن — غير مُرحَّل محاسبيًا، أو مُستبعد، أو بلغ قيمته التخريدية.</div>", "text/html");
        return PartialView("_DepreciationForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(DepreciationFormModel model, CancellationToken ct)
    {
        if (!CanDepreciate()) return Forbid();
        var a = model.AssetId is Guid id ? await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        if (a is null) ModelState.AddModelError(nameof(model.AssetId), "اختر الأصل.");
        else if (!a.IsPosted) ModelState.AddModelError(nameof(model.AssetId), "رحِّل قيد اقتناء الأصل أولًا.");
        else if (a.IsDisposed) ModelState.AddModelError(nameof(model.AssetId), "الأصل مُستبعد — لا يُهلك.");
        else
        {
            var remaining = FixedAssetSchedule.Remaining(a, await AccumulatedAsync(a, ct));
            if (model.Amount > remaining)
                ModelState.AddModelError(nameof(model.Amount), remaining <= 0
                    ? "بلغ الأصل قيمته التخريدية — لا يتبقى ما يُهلك."
                    : $"المبلغ أكبر من المتبقي للإهلاك ({remaining:N2} ج.م — حتى القيمة التخريدية).");
            if (a.PurchaseDate is DateTime bought && model.Date.Date < bought.Date)
                ModelState.AddModelError(nameof(model.Date), $"تاريخ الإهلاك قبل تاريخ اقتناء الأصل ({AppCulture.Day(bought)}).");
        }
        if (model.Date.Date > ClientTime.Today) ModelState.AddModelError(nameof(model.Date), "لا يمكن تسجيل إهلاك بتاريخ مستقبلي.");
        if (!ModelState.IsValid)
        {
            await FillAsync(model, ct);
            return PartialView("_DepreciationForm", model);
        }

        var d = new FixedAssetDepreciation
        {
            FixedAssetId = a!.Id, Date = model.Date, Amount = model.Amount,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim()
        };
        _db.FixedAssetDepreciations.Add(d);
        await _accounting.PostDepreciationAsync(d, a, ct);   // Dr مصروف الإهلاك / Cr مجمع الإهلاك
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تسجيل إهلاك {model.Amount:N2} ج.م على الأصل {FA(a.Number)} «{a.Name}» وترحيل قيده.";
        return Json(new { ok = true });
    }

    // ---------- Delete an entry (reverses its journal entry) ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, bool toAsset, CancellationToken ct)
    {
        if (!User.HasClaim("permission", PermissionNames.FixedAssetsDeleteDepreciation)) return Forbid();
        var d = await _db.FixedAssetDepreciations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return NotFound();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == d.FixedAssetId, ct);
        IActionResult Back() => toAsset && a is not null ? RedirectToAction("Details", "FixedAssets", new { id = a.Id }) : RedirectToAction(nameof(Index));
        if (a?.IsDisposed == true)
        {
            Refused();
            TempData["ErrorMessage"] = $"لا يمكن حذف إهلاك الأصل {FA(a.Number)}: الأصل مُستبعد والإهلاك مُقفَل في قيد استبعاده — ألغِ الاستبعاد أولًا.";
            return Back();
        }
        await _accounting.RemoveDepreciationAsync(d, ct);
        _db.FixedAssetDepreciations.Remove(d);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف إهلاك {d.Amount:N2} ج.م ({AppCulture.Day(d.Date)})"
            + (a is null ? "" : $" على الأصل {FA(a.Number)}") + " وعكس قيده.";
        return Back();
    }

    // ---------- helpers ----------
    private async Task<decimal> AccumulatedAsync(FixedAsset a, CancellationToken ct) =>
        a.OpeningAccumulatedDepreciation + (await _db.FixedAssetDepreciations.Where(d => d.FixedAssetId == a.Id).SumAsync(d => (decimal?)d.Amount, ct) ?? 0m);

    /// <summary>The assets that can be depreciated now: posted, not disposed, still above their salvage value.</summary>
    private async Task FillAsync(DepreciationFormModel m, CancellationToken ct)
    {
        var catNames = await _db.FixedAssetCategories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var assets = await _db.FixedAssets.AsNoTracking().Where(a => a.AcquisitionMethod != null && a.DisposalDate == null)
            .OrderByDescending(a => a.Number).ToListAsync(ct);
        var recorded = await _db.FixedAssetDepreciations.GroupBy(d => d.FixedAssetId)
            .Select(g => new { g.Key, Sum = g.Sum(d => d.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        m.Assets = assets
            .Select(a => (a, Remaining: FixedAssetSchedule.Remaining(a, a.OpeningAccumulatedDepreciation + recorded.GetValueOrDefault(a.Id))))
            .Where(x => x.Remaining > 0)
            .Select(x => new DepreciableAssetOption(x.a.Id,
                $"{FA(x.a.Number)} — {x.a.Name}" + (x.a.CategoryId is Guid c && catNames.TryGetValue(c, out var cat) ? $" ({cat})" : ""),
                x.Remaining, Math.Min(FixedAssetSchedule.SuggestedMonthly(x.a), x.Remaining)))
            .ToList();
    }
}
