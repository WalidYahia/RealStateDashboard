using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// إهلاك الأصول الثابتة — monthly straight-line depreciation. Running a month posts every month still due up to it
/// (catch-up), one entry per asset per month: Dr مصروف الإهلاك / Cr مجمع الإهلاك (the category's accounts). Only the
/// last posted month can be undone, so the schedule stays continuous. Disposed assets are no longer depreciated.
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

    private bool CanRun() => User.HasClaim("permission", PermissionNames.FixedAssetsDepreciate);

    public async Task<IActionResult> Index(DateTime? period, CancellationToken ct)
    {
        var month = FixedAssetSchedule.MonthOf(period ?? ClientTime.Today);
        var (due, skipped) = await DueAsync(month, ct);
        var records = await _db.FixedAssetDepreciations.AsNoTracking().ToListAsync(ct);
        ViewData["CanRun"] = CanRun();
        return View(new DepreciationVm
        {
            Period = month,
            Due = due.Select(d => d.Row).OrderBy(r => r.Number).ToList(),
            SkippedUnposted = skipped,
            LastPostedPeriod = records.Count == 0 ? null : records.Max(r => r.Period),
            History = records.GroupBy(r => r.Period).OrderByDescending(g => g.Key)
                .Select(g => new DepreciationRunRow { Period = g.Key, Assets = g.Select(x => x.FixedAssetId).Distinct().Count(), Amount = g.Sum(x => x.Amount) })
                .ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(DateTime period, CancellationToken ct)
    {
        if (!CanRun()) return Forbid();
        var month = FixedAssetSchedule.MonthOf(period);
        if (month > FixedAssetSchedule.MonthOf(ClientTime.Today))
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            TempData["ErrorMessage"] = "لا يمكن احتساب إهلاك شهر لم يبدأ بعد.";
            return RedirectToAction(nameof(Index), new { period = month.ToString("yyyy-MM-dd") });
        }

        var (due, _) = await DueAsync(month, ct);
        var entries = 0; decimal total = 0;
        foreach (var (_, asset, months) in due)
            foreach (var (p, amount) in months)
            {
                var d = new FixedAssetDepreciation { FixedAssetId = asset.Id, Period = p, Amount = amount };
                _db.FixedAssetDepreciations.Add(d);
                await _accounting.PostDepreciationAsync(d, asset, ct);
                entries++; total += amount;
            }
        if (entries == 0)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            TempData["StatusMessage"] = $"لا يوجد إهلاك مستحق حتى {month:yyyy/MM}.";
            return RedirectToAction(nameof(Index), new { period = month.ToString("yyyy-MM-dd") });
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم ترحيل إهلاك {due.Count} أصل حتى {month:yyyy/MM}: {entries} قيد بإجمالي {total:N2} ج.م.";
        return RedirectToAction(nameof(Index), new { period = month.ToString("yyyy-MM-dd") });
    }

    /// <summary>Undo the most recent posted month (all assets) — only while no disposal relied on it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoLast(CancellationToken ct)
    {
        if (!CanRun()) return Forbid();
        var last = await _db.FixedAssetDepreciations.MaxAsync(d => (DateTime?)d.Period, ct);
        if (last is null) return RedirectToAction(nameof(Index));
        var records = await _db.FixedAssetDepreciations.Where(d => d.Period == last).ToListAsync(ct);
        var assetIds = records.Select(r => r.FixedAssetId).ToList();
        var disposed = await _db.FixedAssets.Where(a => assetIds.Contains(a.Id) && a.DisposalDate != null).Select(a => "FA-" + a.Number).ToListAsync(ct);
        if (disposed.Count > 0)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            TempData["ErrorMessage"] = $"لا يمكن إلغاء إهلاك {last:yyyy/MM}: أصول استُبعدت بعده ({string.Join("، ", disposed)}) — ألغِ استبعادها أولًا.";
            return RedirectToAction(nameof(Index));
        }
        foreach (var r in records)
        {
            await _accounting.RemoveDepreciationAsync(r, ct);
            _db.FixedAssetDepreciations.Remove(r);
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم إلغاء إهلاك شهر {last:yyyy/MM} ({records.Count} أصل، {records.Sum(r => r.Amount):N2} ج.م) وعكس قيوده.";
        return RedirectToAction(nameof(Index), new { period = last.Value.ToString("yyyy-MM-dd") });
    }

    /// <summary>
    /// Per posted, not-disposed asset: its schedule months up to <paramref name="month"/> not yet posted. Also counts the
    /// assets that can't be depreciated yet (acquisition not posted, or no useful life).
    /// </summary>
    private async Task<(List<(DepreciationPreviewRow Row, FixedAsset Asset, List<(DateTime Period, decimal Amount)> Months)> Due, int Skipped)>
        DueAsync(DateTime month, CancellationToken ct)
    {
        var assets = await _db.FixedAssets.Where(a => a.DisposalDate == null).ToListAsync(ct);
        var catNames = await _db.FixedAssetCategories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var posted = (await _db.FixedAssetDepreciations.Select(d => new { d.FixedAssetId, d.Period, d.Amount }).ToListAsync(ct))
            .ToLookup(d => d.FixedAssetId);

        var due = new List<(DepreciationPreviewRow, FixedAsset, List<(DateTime, decimal)>)>();
        var skipped = 0;
        foreach (var a in assets)
        {
            if (!a.IsPosted || (a.UsefulLifeMonths > 0 && a.DepreciationStartDate is null)) { skipped++; continue; }
            var done = posted[a.Id].Select(p => p.Period).ToHashSet();
            var months = FixedAssetSchedule.Months(a).Where(m => m.Period <= month && !done.Contains(m.Period)).ToList();
            if (months.Count == 0) continue;
            var accumulated = a.OpeningAccumulatedDepreciation + posted[a.Id].Sum(p => p.Amount) + months.Sum(m => m.Amount);
            due.Add((new DepreciationPreviewRow
            {
                AssetId = a.Id, Number = a.Number, Name = a.Name,
                Category = a.CategoryId is Guid c ? catNames.GetValueOrDefault(c) : null,
                Months = months.Count, Amount = months.Sum(m => m.Amount), BookValueAfter = a.TotalCost - accumulated
            }, a, months));
        }
        return (due, skipped);
    }
}
