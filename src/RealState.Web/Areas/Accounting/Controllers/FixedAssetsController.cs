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
/// الأصول الثابتة — what the company owns and its accounting value (FA-{year}{seq}). Financial events post through the
/// accounting engine using the asset's category posting profile (<see cref="IFixedAssetAccounting"/>): acquisition (cash
/// from a safe / opening balance), monthly depreciation (FixedAssetDepreciation page) and disposal / write-off. Custody
/// (العُهد العينية) only tracks who is responsible — it never changes the asset's value. Once posted, an asset's financial
/// fields are fixed; mistakes are corrected by deleting (reversing) an unused asset or by disposing of it.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.FixedAssetsView)]
public class FixedAssetsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IFixedAssetAccounting _accounting;
    private readonly ISafeBalanceGuard _guard;

    public FixedAssetsController(IApplicationDbContext db, ICurrentUserService currentUser, IFixedAssetAccounting accounting, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);
    private static string FA(int number) => "FA-" + number;
    private void Refused() => HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;

    // ---------- List ----------
    public async Task<IActionResult> Index(Guid? categoryId, string? state, string? q, CancellationToken ct)
    {
        ViewData["CanManage"] = Can(PermissionNames.FixedAssetsManage);
        return View(await BuildListAsync(categoryId, state, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(Guid? categoryId, string? state, string? q, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View(await BuildListAsync(categoryId, state, q, ct));
    }

    private async Task<FixedAssetListVm> BuildListAsync(Guid? categoryId, string? state, string? q, CancellationToken ct)
    {
        var assets = await _db.FixedAssets.AsNoTracking().ToListAsync(ct);
        var catNames = await _db.FixedAssetCategories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var depreciated = await DepreciatedByAssetAsync(ct);
        var empNames = await _db.Employees.IgnoreQueryFilters().ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var active = (await _db.MaterialCustodies.Where(c => c.Status == MaterialCustodyStatus.HandedOver && c.FixedAssetId != null)
                .Select(c => new { AssetId = c.FixedAssetId!.Value, c.Id, c.Number, c.EmployeeId }).ToListAsync(ct))
            .GroupBy(c => c.AssetId).ToDictionary(g => g.Key, g => g.First());

        var rows = assets.Select(a =>
        {
            var custody = active.GetValueOrDefault(a.Id);
            return new FixedAssetRow
            {
                Id = a.Id, Number = a.Number, Name = a.Name, CategoryId = a.CategoryId, Category = a.CategoryId is Guid c ? catNames.GetValueOrDefault(c) : null,
                SerialNumber = a.SerialNumber, Quantity = a.Quantity, Cost = a.TotalCost, PurchaseDate = a.PurchaseDate,
                Accumulated = a.OpeningAccumulatedDepreciation + depreciated.GetValueOrDefault(a.Id),
                IsActive = a.IsActive, IsPosted = a.IsPosted, IsDisposed = a.IsDisposed,
                Custodian = custody is null ? null : empNames.GetValueOrDefault(custody.EmployeeId, "—"),
                CustodyId = custody?.Id, CustodyNumber = custody?.Number
            };
        }).ToList();

        var filtered = rows.AsEnumerable();
        if (categoryId.HasValue) filtered = filtered.Where(r => r.CategoryId == categoryId);
        filtered = state switch
        {
            "available" => filtered.Where(r => r.IsActive && !r.IsDisposed && r.Custodian is null),
            "custody" => filtered.Where(r => r.Custodian is not null),
            "inactive" => filtered.Where(r => !r.IsActive && !r.IsDisposed),
            "disposed" => filtered.Where(r => r.IsDisposed),
            "unposted" => filtered.Where(r => !r.IsPosted),
            _ => filtered
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            filtered = filtered.Where(r => r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || (r.SerialNumber ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || (r.Custodian ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || FA(r.Number).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var owned = rows.Where(r => !r.IsDisposed).ToList();
        return new FixedAssetListVm
        {
            Rows = filtered.OrderByDescending(r => r.Number).ToList(),
            CategoryId = categoryId, State = state, Q = q,
            Categories = catNames.OrderBy(c => c.Value).Select(c => new SelectListItem(c.Value, c.Key.ToString())).ToList(),
            TotalCost = owned.Sum(r => r.Cost),
            TotalAccumulated = owned.Sum(r => r.Accumulated),
            InCustodyCount = owned.Count(r => r.Custodian is not null),
            AvailableCount = owned.Count(r => r.IsActive && r.Custodian is null),
            UnpostedCount = rows.Count(r => !r.IsPosted),
        };
    }

    // ---------- Details ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        var empNames = await _db.Employees.IgnoreQueryFilters().ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var custodies = await _db.MaterialCustodies.Where(c => c.FixedAssetId == id).OrderByDescending(c => c.Number).ToListAsync(ct);
        var deps = await _db.FixedAssetDepreciations.Where(d => d.FixedAssetId == id).OrderBy(d => d.Period).ToListAsync(ct);
        var serials = await _db.SafeTransactions.Where(t => t.Id == a.PurchaseTransactionId || t.Id == a.DisposalTransactionId)
            .ToDictionaryAsync(t => t.Id, t => t.Serial, ct);

        ViewData["CanManage"] = Can(PermissionNames.FixedAssetsManage);
        ViewData["CanDelete"] = Can(PermissionNames.FixedAssetsDelete);
        ViewData["CanDispose"] = Can(PermissionNames.FixedAssetsDispose);
        return View(new FixedAssetDetailsVm
        {
            Asset = a,
            Category = a.CategoryId is Guid cid ? await _db.FixedAssetCategories.FirstOrDefaultAsync(c => c.Id == cid, ct) : null,
            PurchaseSafe = a.PurchaseSafeId is Guid s ? await _db.Safes.IgnoreQueryFilters().Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            PurchaseVoucherSerial = a.PurchaseTransactionId is Guid p && serials.TryGetValue(p, out var ps) ? ps : null,
            DisposalVoucherSerial = a.DisposalTransactionId is Guid d && serials.TryGetValue(d, out var ds) ? ds : null,
            Accumulated = a.OpeningAccumulatedDepreciation + deps.Sum(x => x.Amount),
            MonthlyDepreciation = FixedAssetSchedule.Months(a).Select(m => m.Amount).FirstOrDefault(),
            MonthsPosted = deps.Count,
            Depreciations = deps,
            Custodies = custodies.Select(c => new MaterialCustodyRow
            {
                Id = c.Id, Number = c.Number, ItemName = c.ItemName, Quantity = c.Quantity, EmployeeId = c.EmployeeId,
                Employee = empNames.GetValueOrDefault(c.EmployeeId, "—"), HandoverDate = c.HandoverDate,
                ExpectedReturnDate = c.ExpectedReturnDate, ReturnDate = c.ReturnDate, Status = c.Status
            }).ToList()
        });
    }

    // ---------- Add / edit ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsManage)) return Forbid();
        var model = new FixedAssetFormModel();
        if (id is Guid aid && aid != Guid.Empty)
        {
            var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == aid, ct);
            if (a is null) return NotFound();
            model = new FixedAssetFormModel
            {
                Id = a.Id, Number = a.Number, Name = a.Name, CategoryId = a.CategoryId, SerialNumber = a.SerialNumber,
                Quantity = a.Quantity, UnitCost = a.UnitCost, PurchaseDate = a.PurchaseDate ?? ClientTime.Today, Location = a.Location,
                Notes = a.Notes, IsActive = a.IsActive, AcquisitionMethod = a.AcquisitionMethod ?? AssetAcquisitionMethod.OpeningBalance,
                SafeId = a.PurchaseSafeId, OpeningAccumulatedDepreciation = a.OpeningAccumulatedDepreciation,
                UsefulLifeMonths = a.UsefulLifeMonths, SalvageValue = a.SalvageValue, DepreciationStartDate = a.DepreciationStartDate,
                FinancialsLocked = a.IsPosted,
                ScheduleLocked = await _db.FixedAssetDepreciations.AnyAsync(d => d.FixedAssetId == a.Id, ct)
            };
        }
        return PartialView("_FixedAssetForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(FixedAssetFormModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsManage)) return Forbid();
        var isEdit = model.Id != Guid.Empty;
        FixedAsset? a = null;
        if (isEdit)
        {
            a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (a is null) return NotFound();
            model.Number = a.Number;
            model.FinancialsLocked = a.IsPosted;
            model.ScheduleLocked = await _db.FixedAssetDepreciations.AnyAsync(d => d.FixedAssetId == a.Id, ct);
            if (a.IsPosted)
            {
                // Fixed once posted — keep the stored values whatever was submitted.
                model.CategoryId = a.CategoryId; model.Quantity = a.Quantity; model.UnitCost = a.UnitCost;
                model.PurchaseDate = a.PurchaseDate ?? model.PurchaseDate; model.OpeningAccumulatedDepreciation = a.OpeningAccumulatedDepreciation;
                ModelState.Remove(nameof(model.UnitCost)); ModelState.Remove(nameof(model.CategoryId)); ModelState.Remove(nameof(model.Quantity));
            }
            if (model.ScheduleLocked)
            {
                model.UsefulLifeMonths = a.UsefulLifeMonths; model.SalvageValue = a.SalvageValue; model.DepreciationStartDate = a.DepreciationStartDate;
            }
            if (a.IsDisposed) model.IsActive = false;
            if (!model.IsActive && a.IsActive && await _db.MaterialCustodies.AnyAsync(c => c.FixedAssetId == a.Id && c.Status == MaterialCustodyStatus.HandedOver, ct))
                ModelState.AddModelError(nameof(model.IsActive), "الأصل في عهدة موظف — استلمه أولًا قبل إخراجه من الخدمة.");
        }

        var cost = model.UnitCost * model.Quantity;
        if (model.CategoryId is Guid catId && !await _db.FixedAssetCategories.AnyAsync(c => c.Id == catId, ct))
            ModelState.AddModelError(nameof(model.CategoryId), "الفئة غير موجودة.");
        if (model.UsefulLifeMonths > 0 && model.SalvageValue + model.OpeningAccumulatedDepreciation >= cost)
            ModelState.AddModelError(nameof(model.SalvageValue), "القيمة التخريدية + الإهلاك السابق يجب أن تقل عن تكلفة الأصل.");
        if (!isEdit || a?.IsPosted == false)
        {
            if (model.AcquisitionMethod == AssetAcquisitionMethod.Cash)
            {
                if (model.SafeId is null) ModelState.AddModelError(nameof(model.SafeId), "اختر الخزنة.");
                else if (ModelState.IsValid && !isEdit && await _guard.CheckWithdrawalAsync(model.SafeId.Value, cost, ct) is string overdraw)
                    ModelState.AddModelError(nameof(model.SafeId), overdraw);
                model.OpeningAccumulatedDepreciation = 0;
            }
            else if (model.OpeningAccumulatedDepreciation > cost)
                ModelState.AddModelError(nameof(model.OpeningAccumulatedDepreciation), "الإهلاك السابق أكبر من تكلفة الأصل.");
        }
        if (!ModelState.IsValid) return PartialView("_FixedAssetForm", await FillAsync(model, ct));

        if (a is null)
        {
            long lo = YearSerial.Base(model.PurchaseDate.Year, 4), hi = YearSerial.End(model.PurchaseDate.Year, 4);
            var max = await _db.FixedAssets.Where(x => x.Number > lo && x.Number < hi).MaxAsync(x => (int?)x.Number, ct);
            a = new FixedAsset { Number = (int)YearSerial.Next(max, model.PurchaseDate.Year, 4) };
            _db.FixedAssets.Add(a);
        }

        a.Name = model.Name.Trim();
        a.SerialNumber = string.IsNullOrWhiteSpace(model.SerialNumber) ? null : model.SerialNumber.Trim();
        a.Location = string.IsNullOrWhiteSpace(model.Location) ? null : model.Location.Trim();
        a.Notes = model.Notes;
        a.IsActive = model.IsActive;
        a.UsefulLifeMonths = model.UsefulLifeMonths;
        a.SalvageValue = model.SalvageValue;
        a.DepreciationStartDate = model.UsefulLifeMonths > 0 ? model.DepreciationStartDate ?? model.PurchaseDate : null;
        if (!a.IsPosted)
        {
            a.CategoryId = model.CategoryId;
            a.Quantity = model.Quantity;
            a.UnitCost = model.UnitCost;
            a.PurchaseDate = model.PurchaseDate;
        }

        string msg;
        if (!isEdit)
        {
            // A new asset is posted right away: cash purchase (سند صرف) or opening balance.
            a.AcquisitionMethod = model.AcquisitionMethod;
            a.PurchaseSafeId = model.AcquisitionMethod == AssetAcquisitionMethod.Cash ? model.SafeId : null;
            a.OpeningAccumulatedDepreciation = model.AcquisitionMethod == AssetAcquisitionMethod.OpeningBalance ? model.OpeningAccumulatedDepreciation : 0;
            await _accounting.PostAcquisitionAsync(a, ct);
            msg = $"تمت إضافة الأصل الثابت {FA(a.Number)} «{a.Name}» وترحيل قيد اقتنائه ({model.AcquisitionMethod.Ar()}).";
        }
        else msg = $"تم تعديل الأصل الثابت {FA(a.Number)} «{a.Name}».";

        // Custodies show the asset's current name / category / serial / value.
        var catName = await _db.FixedAssetCategories.Where(c => c.Id == a.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        foreach (var c in await _db.MaterialCustodies.Where(c => c.FixedAssetId == a.Id).ToListAsync(ct))
        {
            c.ItemName = a.Name; c.ItemType = catName; c.SerialNumber = a.SerialNumber; c.Value = a.UnitCost; c.Quantity = a.Quantity;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = msg;
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = a.Id }) });
    }

    // ---------- Post an asset recorded before the ledger integration ----------
    [HttpGet]
    public async Task<IActionResult> PostForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsManage)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (a.IsPosted) return Content("<div style=\"padding:18px;text-align:center;\">الأصل مُرحَّل بالفعل.</div>", "text/html");
        return PartialView("_PostForm", await FillAsync(new FixedAssetPostModel { Id = a.Id, Number = a.Number, Name = a.Name, Cost = a.TotalCost }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(FixedAssetPostModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsManage)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (a is null) return NotFound();
        if (a.IsPosted) { Refused(); return Json(new { ok = false, error = "الأصل مُرحَّل بالفعل." }); }
        if (a.CategoryId is null) ModelState.AddModelError("", "حدِّد فئة الأصل أولًا (تعديل) — حسابات القيد تأتي من الفئة.");
        if (a.TotalCost <= 0) ModelState.AddModelError("", "حدِّد تكلفة الأصل أولًا (تعديل).");
        if (model.AcquisitionMethod == AssetAcquisitionMethod.Cash)
        {
            if (model.SafeId is null) ModelState.AddModelError(nameof(model.SafeId), "اختر الخزنة.");
            else if (await _guard.CheckWithdrawalAsync(model.SafeId.Value, a.TotalCost, ct) is string overdraw)
                ModelState.AddModelError(nameof(model.SafeId), overdraw);
        }
        else if (model.OpeningAccumulatedDepreciation > a.TotalCost)
            ModelState.AddModelError(nameof(model.OpeningAccumulatedDepreciation), "الإهلاك السابق أكبر من تكلفة الأصل.");
        if (!ModelState.IsValid)
        {
            model.Number = a.Number; model.Name = a.Name; model.Cost = a.TotalCost;
            return PartialView("_PostForm", await FillAsync(model, ct));
        }

        a.AcquisitionMethod = model.AcquisitionMethod;
        a.PurchaseSafeId = model.AcquisitionMethod == AssetAcquisitionMethod.Cash ? model.SafeId : null;
        a.OpeningAccumulatedDepreciation = model.AcquisitionMethod == AssetAcquisitionMethod.OpeningBalance ? model.OpeningAccumulatedDepreciation : 0;
        await _accounting.PostAcquisitionAsync(a, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم ترحيل قيد اقتناء الأصل {FA(a.Number)} ({model.AcquisitionMethod.Ar()}).";
        return Json(new { ok = true });
    }

    // ---------- Dispose / write off ----------
    [HttpGet]
    public async Task<IActionResult> DisposeForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsDispose)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (await DisposeBlockerAsync(a, ct) is string why)
            return Content($"<div style=\"padding:18px;color:var(--warning);text-align:center;\">{System.Net.WebUtility.HtmlEncode(why)}</div>", "text/html");
        return PartialView("_DisposeForm", await FillAsync(new FixedAssetDisposeModel
        {
            Id = a.Id, Number = a.Number, Name = a.Name, Cost = a.TotalCost, Accumulated = await AccumulatedAsync(a, ct)
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispose(FixedAssetDisposeModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsDispose)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (a is null) return NotFound();
        if (await DisposeBlockerAsync(a, ct) is string why) { Refused(); return Json(new { ok = false, error = why }); }
        if (model.Proceeds > 0 && model.SafeId is null) ModelState.AddModelError(nameof(model.SafeId), "اختر الخزنة التي تُورَّد إليها المتحصلات.");
        var lastDep = await _db.FixedAssetDepreciations.Where(d => d.FixedAssetId == a.Id).MaxAsync(d => (DateTime?)d.Period, ct);
        if (lastDep is DateTime ld && model.Date.Date < FixedAssetSchedule.EndOfMonth(ld))
            ModelState.AddModelError(nameof(model.Date), $"تاريخ الاستبعاد قبل آخر إهلاك مُرحَّل ({FixedAssetSchedule.EndOfMonth(ld):yyyy/MM/dd}).");
        var accumulated = await AccumulatedAsync(a, ct);
        if (!ModelState.IsValid)
        {
            model.Number = a.Number; model.Name = a.Name; model.Cost = a.TotalCost; model.Accumulated = accumulated;
            return PartialView("_DisposeForm", await FillAsync(model, ct));
        }

        a.DisposalDate = model.Date;
        a.DisposalProceeds = model.Proceeds;
        a.DisposalSafeId = model.Proceeds > 0 ? model.SafeId : null;
        a.DisposalReason = model.Reason.Trim();
        a.IsActive = false;
        await _accounting.PostDisposalAsync(a, accumulated, ct);
        await _db.SaveChangesAsync(ct);
        var result = model.Proceeds - (a.TotalCost - accumulated);
        TempData["StatusMessage"] = $"تم استبعاد الأصل {FA(a.Number)} «{a.Name}»"
            + (result > 0 ? $" بربح {result:N2} ج.م." : result < 0 ? $" بخسارة {-result:N2} ج.م." : " دون ربح أو خسارة.");
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoDispose(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsDispose)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (!a.IsDisposed) return RedirectToAction(nameof(Details), new { id });
        // Removing the proceeds takes that money back out of the safe — «سحب على المكشوف» applies.
        if (a.DisposalProceeds > 0 && a.DisposalSafeId is Guid safe && await _guard.CheckWithdrawalAsync(safe, a.DisposalProceeds, ct) is string overdraw)
        {
            Refused();
            TempData["ErrorMessage"] = $"لا يمكن إلغاء الاستبعاد: {overdraw}";
            return RedirectToAction(nameof(Details), new { id });
        }
        await _accounting.RemoveDisposalAsync(a, ct);
        a.DisposalDate = null; a.DisposalProceeds = 0; a.DisposalSafeId = null; a.DisposalReason = null; a.IsActive = true;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم إلغاء استبعاد الأصل {FA(a.Number)} وعكس قيده.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Delete (an asset entered by mistake: no custody, depreciation or disposal) ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.FixedAssetsDelete)) return Forbid();
        var a = await _db.FixedAssets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        string? why = a.IsDisposed ? "الأصل مُستبعد — ألغِ الاستبعاد أولًا."
            : await _db.MaterialCustodies.AnyAsync(c => c.FixedAssetId == id, ct) ? "للأصل سجل عُهد — يمكن إخراجه من الخدمة بدلًا من حذفه."
            : await _db.FixedAssetDepreciations.AnyAsync(d => d.FixedAssetId == id, ct) ? "للأصل إهلاك مُرحَّل — ألغِ فترات الإهلاك أولًا."
            : null;
        if (why is not null)
        {
            Refused();
            TempData["ErrorMessage"] = $"لا يمكن حذف الأصل {FA(a.Number)}: {why}";
            return RedirectToAction(nameof(Details), new { id });
        }
        await _accounting.RemoveAcquisitionAsync(a, ct);   // reverses the acquisition entry (and a cash purchase's voucher)
        _db.FixedAssets.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف الأصل الثابت {FA(a.Number)} «{a.Name}» وعكس قيد اقتنائه.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- helpers ----------
    private async Task<string?> DisposeBlockerAsync(FixedAsset a, CancellationToken ct) =>
        a.IsDisposed ? "الأصل مُستبعد بالفعل."
        : !a.IsPosted ? "رحِّل قيد اقتناء الأصل أولًا."
        : await _db.MaterialCustodies.AnyAsync(c => c.FixedAssetId == a.Id && c.Status == MaterialCustodyStatus.HandedOver, ct)
            ? "الأصل في عهدة موظف — استلمه أولًا."
            : null;

    private async Task<decimal> AccumulatedAsync(FixedAsset a, CancellationToken ct) =>
        a.OpeningAccumulatedDepreciation + await _db.FixedAssetDepreciations.Where(d => d.FixedAssetId == a.Id).SumAsync(d => d.Amount, ct);

    private async Task<Dictionary<Guid, decimal>> DepreciatedByAssetAsync(CancellationToken ct) =>
        await _db.FixedAssetDepreciations.GroupBy(d => d.FixedAssetId)
            .Select(g => new { g.Key, Sum = g.Sum(d => d.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

    private async Task<FixedAssetFormModel> FillAsync(FixedAssetFormModel m, CancellationToken ct)
    {
        var cats = await _db.FixedAssetCategories.OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.DefaultUsefulLifeMonths }).ToListAsync(ct);
        m.Categories = cats.Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToList();
        m.CategoryLives = cats.ToDictionary(c => c.Id, c => c.DefaultUsefulLifeMonths);
        m.Safes = await SafeOptionsAsync(ct);
        return m;
    }

    private async Task<FixedAssetPostModel> FillAsync(FixedAssetPostModel m, CancellationToken ct) { m.Safes = await SafeOptionsAsync(ct); return m; }
    private async Task<FixedAssetDisposeModel> FillAsync(FixedAssetDisposeModel m, CancellationToken ct) { m.Safes = await SafeOptionsAsync(ct); return m; }

    /// <summary>Active safes with their current balance in the label.</summary>
    private async Task<List<SelectListItem>> SafeOptionsAsync(CancellationToken ct)
    {
        var safes = await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync(ct);
        var list = new List<SelectListItem>();
        foreach (var s in safes)
            list.Add(new SelectListItem($"{s.Name} — الرصيد {await _guard.BalanceAsync(s.Id, ct):N2}", s.Id.ToString()));
        return list;
    }
}
