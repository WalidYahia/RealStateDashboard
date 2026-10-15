using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// العُهد العينية — who is responsible for a fixed asset (الأصول الثابتة), numbered MC-{year}{seq}. One active custodian per
/// asset: handover, transfer to another employee (the old custody closes as «حُوِّلت», a new one opens), return (returned /
/// damaged / lost) and cancellation of a custody recorded by mistake (kept with its reason — history is never deleted).
/// The asset and employee of a custody can't be edited; only its dates / conditions / notes. Custody never posts to the
/// ledger and never makes the employee a debtor. Every change is recorded in the activity log.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.MaterialCustodyView)]
public class MaterialCustodyController : Controller
{
    private const long MaxFileBytes = 15 * 1024 * 1024;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public MaterialCustodyController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool CanManage() => User.HasClaim("permission", PermissionNames.MaterialCustodyManage);
    private bool CanDelete() => User.HasClaim("permission", PermissionNames.MaterialCustodyDelete);
    private static string MC(int number) => "MC-" + number;

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? employeeId, MaterialCustodyStatus? status,
        string? itemType, string? q, CancellationToken ct)
    {
        ViewData["CanManage"] = CanManage();
        return View(await BuildListAsync(from, to, employeeId, status, itemType, q, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? employeeId, MaterialCustodyStatus? status,
        string? itemType, string? q, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View(await BuildListAsync(from, to, employeeId, status, itemType, q, ct));
    }

    private async Task<MaterialCustodyListVm> BuildListAsync(DateTime? from, DateTime? to, Guid? employeeId, MaterialCustodyStatus? status,
        string? itemType, string? q, CancellationToken ct)
    {
        var empNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var all = await _db.MaterialCustodies.AsNoTracking().ToListAsync(ct);
        var attCounts = await _db.MaterialCustodyAttachments.GroupBy(a => a.MaterialCustodyId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var assetNos = await _db.FixedAssets.IgnoreQueryFilters().ToDictionaryAsync(a => a.Id, a => a.Number, ct);
        var rows = all.Select(c => new MaterialCustodyRow
        {
            Id = c.Id, Number = c.Number, FixedAssetId = c.FixedAssetId,
            AssetNumber = c.FixedAssetId is Guid fa && assetNos.TryGetValue(fa, out var an) ? an : null,
            ItemName = c.ItemName, ItemType = c.ItemType, SerialNumber = c.SerialNumber,
            Quantity = c.Quantity, Value = c.Value, EmployeeId = c.EmployeeId, Employee = empNames.GetValueOrDefault(c.EmployeeId, "—"),
            HandoverDate = c.HandoverDate, ExpectedReturnDate = c.ExpectedReturnDate, ReturnDate = c.ReturnDate,
            Status = c.Status, AttachmentsCount = attCounts.GetValueOrDefault(c.Id)
        }).ToList();

        var filtered = rows.AsEnumerable();
        if (from.HasValue) filtered = filtered.Where(r => r.HandoverDate >= from.Value.Date);
        if (to.HasValue) filtered = filtered.Where(r => r.HandoverDate < to.Value.Date.AddDays(1));
        if (employeeId.HasValue) filtered = filtered.Where(r => r.EmployeeId == employeeId);
        if (status.HasValue) filtered = filtered.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(itemType)) filtered = filtered.Where(r => r.ItemType == itemType);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            filtered = filtered.Where(r => r.ItemName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || (r.SerialNumber ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || r.Employee.Contains(term, StringComparison.OrdinalIgnoreCase)
                                           || MC(r.Number).Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var held = rows.Where(r => r.Status == MaterialCustodyStatus.HandedOver).ToList();
        return new MaterialCustodyListVm
        {
            Rows = filtered.OrderByDescending(r => r.Number).ToList(),
            From = from, To = to, EmployeeId = employeeId, Status = status, ItemType = itemType, Q = q,
            Employees = await EmployeeOptionsAsync(activeOnly: false, ct),
            ItemTypes = (await ItemTypesAsync(ct)).Select(t => new SelectListItem(t, t)).ToList(),
            HeldCount = held.Count,
            HeldValue = held.Sum(r => (r.Value ?? 0) * r.Quantity),
            OverdueCount = held.Count(r => r.IsOverdue),
            ReturnedCount = rows.Count(r => r.Status == MaterialCustodyStatus.Returned),
            IssueCount = rows.Count(r => r.Status is MaterialCustodyStatus.Damaged or MaterialCustodyStatus.Lost),
        };
    }

    // ---------- Details ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await DetailsAsync(id, ct);
        if (vm is null) return NotFound();
        ViewData["CanManage"] = CanManage();
        ViewData["CanDelete"] = CanDelete();
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintHandover(Guid id, CancellationToken ct)
    {
        var vm = await DetailsAsync(id, ct);
        if (vm is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View(vm);
    }

    private async Task<MaterialCustodyDetailsVm?> DetailsAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        var emp = await _db.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Id == c.EmployeeId, ct);
        return new MaterialCustodyDetailsVm
        {
            Custody = c,
            Asset = c.FixedAssetId is Guid fa ? await _db.FixedAssets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == fa, ct) : null,
            Employee = emp,
            TransferredFrom = c.TransferredFromId is Guid from
                ? await _db.MaterialCustodies.Where(x => x.Id == from).Select(x => new CustodyLink(x.Id, x.Number, null)).FirstOrDefaultAsync(ct) : null,
            TransferredTo = c.Status == MaterialCustodyStatus.Transferred
                ? await _db.MaterialCustodies.Where(x => x.TransferredFromId == c.Id)
                    .Join(_db.Employees.IgnoreQueryFilters(), x => x.EmployeeId, e => e.Id, (x, e) => new CustodyLink(x.Id, x.Number, e.FullName)).FirstOrDefaultAsync(ct)
                : null,
            Department = emp?.DepartmentId is Guid d ? await _db.Departments.Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct) : null,
            Attachments = await _db.MaterialCustodyAttachments.Where(a => a.MaterialCustodyId == id).OrderBy(a => a.FileName)
                .Select(a => new MaterialCustodyAttachment { Id = a.Id, MaterialCustodyId = a.MaterialCustodyId, FileName = a.FileName, ContentType = a.ContentType, Size = a.Size, CreatedAt = a.CreatedAt })
                .ToListAsync(ct)
        };
    }

    // ---------- Hand over (create) / edit an active custody ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var model = new MaterialCustodyFormModel();
        if (id is Guid cid && cid != Guid.Empty)
        {
            var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null) return NotFound();
            if (c.Status != MaterialCustodyStatus.HandedOver)
                return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">العهدة منتهية — لا تُعدَّل (السجل محفوظ كما هو).</div>", "text/html");
            model = new MaterialCustodyFormModel
            {
                Id = c.Id, FixedAssetId = c.FixedAssetId, EmployeeId = c.EmployeeId, HandoverDate = c.HandoverDate,
                ExpectedReturnDate = c.ExpectedReturnDate, HandoverCondition = c.HandoverCondition, Notes = c.Notes
            };
        }
        return PartialView("_MaterialCustodyForm", await FillAsync(model, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(MaterialCustodyFormModel model, List<IFormFile>? files, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (model.ExpectedReturnDate is DateTime exp && exp.Date < model.HandoverDate.Date)
            ModelState.AddModelError(nameof(model.ExpectedReturnDate), "تاريخ الإرجاع المتوقع قبل تاريخ التسليم.");
        ValidateFiles(files);

        // Edit: only the dates / conditions / notes of an active custody (asset and employee are fixed).
        if (model.Id != Guid.Empty)
        {
            var existing = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (existing is null) return NotFound();
            if (existing.Status != MaterialCustodyStatus.HandedOver)
            {
                HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
                return Json(new { ok = false, error = "العهدة منتهية — لا تُعدَّل." });
            }
            if (model.ExpectedReturnDate is DateTime e2 && e2.Date < existing.HandoverDate.Date)
                ModelState.AddModelError(nameof(model.ExpectedReturnDate), "تاريخ الإرجاع المتوقع قبل تاريخ التسليم.");
            ModelState.Remove(nameof(model.FixedAssetId)); ModelState.Remove(nameof(model.EmployeeId));
            if (!ModelState.IsValid)
            {
                model.FixedAssetId = existing.FixedAssetId; model.EmployeeId = existing.EmployeeId; model.HandoverDate = existing.HandoverDate;
                return PartialView("_MaterialCustodyForm", await FillAsync(model, ct));
            }
            existing.ExpectedReturnDate = model.ExpectedReturnDate;
            existing.HandoverCondition = model.HandoverCondition;
            existing.Notes = model.Notes;
            await SaveFilesAsync(existing.Id, files, ct);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تعديل بيانات العهدة العينية {MC(existing.Number)}.";
            return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = existing.Id }) });
        }

        // Create: an in-service asset with no active custodian.
        var asset = model.FixedAssetId is Guid assetId ? await _db.FixedAssets.FirstOrDefaultAsync(a => a.Id == assetId, ct) : null;
        if (model.FixedAssetId is not null)
        {
            if (asset is null) ModelState.AddModelError(nameof(model.FixedAssetId), "الأصل غير موجود.");
            else if (await AssetBlockerAsync(asset, null, ct) is string why) ModelState.AddModelError(nameof(model.FixedAssetId), why);
        }
        if (!ModelState.IsValid) return PartialView("_MaterialCustodyForm", await FillAsync(model, ct));

        var c = new MaterialCustody { Number = await NextNumberAsync(model.HandoverDate, ct), Status = MaterialCustodyStatus.HandedOver };
        await CopyAssetAsync(c, asset!, ct);
        c.EmployeeId = model.EmployeeId!.Value;
        c.HandoverDate = model.HandoverDate;
        c.ExpectedReturnDate = model.ExpectedReturnDate;
        c.HandoverCondition = model.HandoverCondition;
        c.Notes = model.Notes;
        _db.MaterialCustodies.Add(c);
        var added = await SaveFilesAsync(c.Id, files, ct);
        var emp = await EmployeeNameAsync(c.EmployeeId, ct);
        TempData["StatusMessage"] = $"تم تسليم الأصل FA-{asset!.Number} «{c.ItemName}» إلى {emp} — عهدة عينية {MC(c.Number)}"
                                    + (added > 0 ? $" مع {added} مرفق." : ".");
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = c.Id }) });
    }

    // ---------- Transfer to another employee (history kept: this custody closes, a new one opens) ----------
    [HttpGet]
    public async Task<IActionResult> TransferForm(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.Status != MaterialCustodyStatus.HandedOver)
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">لا يُحوَّل إلا أصل لدى الموظف حاليًا.</div>", "text/html");
        return PartialView("_MaterialCustodyTransferForm", await FillAsync(new MaterialCustodyTransferModel
        {
            Id = c.Id, Number = c.Number, ItemName = c.ItemName, FromEmployee = await EmployeeNameAsync(c.EmployeeId, ct),
            ExpectedReturnDate = c.ExpectedReturnDate
        }, c.EmployeeId, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(MaterialCustodyTransferModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var old = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (old is null) return NotFound();
        if (old.Status != MaterialCustodyStatus.HandedOver)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            return Json(new { ok = false, error = "لا يُحوَّل إلا أصل لدى الموظف حاليًا." });
        }
        if (model.ToEmployeeId == old.EmployeeId) ModelState.AddModelError(nameof(model.ToEmployeeId), "اختر موظفًا غير صاحب العهدة الحالي.");
        if (model.Date.Date < old.HandoverDate.Date) ModelState.AddModelError(nameof(model.Date), "تاريخ التحويل قبل تاريخ التسليم.");
        if (model.ExpectedReturnDate is DateTime exp && exp.Date < model.Date.Date)
            ModelState.AddModelError(nameof(model.ExpectedReturnDate), "تاريخ الإرجاع المتوقع قبل تاريخ التحويل.");
        if (!ModelState.IsValid)
        {
            model.Number = old.Number; model.ItemName = old.ItemName; model.FromEmployee = await EmployeeNameAsync(old.EmployeeId, ct);
            return PartialView("_MaterialCustodyTransferForm", await FillAsync(model, old.EmployeeId, ct));
        }

        var toName = await EmployeeNameAsync(model.ToEmployeeId!.Value, ct);
        var next = new MaterialCustody
        {
            Number = await NextNumberAsync(model.Date, ct), Status = MaterialCustodyStatus.HandedOver, TransferredFromId = old.Id,
            FixedAssetId = old.FixedAssetId, ItemName = old.ItemName, ItemType = old.ItemType, SerialNumber = old.SerialNumber,
            Quantity = old.Quantity, Value = old.Value, EmployeeId = model.ToEmployeeId!.Value, HandoverDate = model.Date,
            ExpectedReturnDate = model.ExpectedReturnDate, HandoverCondition = model.Condition
        };
        _db.MaterialCustodies.Add(next);
        old.Status = MaterialCustodyStatus.Transferred;
        old.ReturnDate = model.Date;
        old.ReturnCondition = $"حُوِّلت إلى {toName} — عهدة {MC(next.Number)}" + (string.IsNullOrWhiteSpace(model.Condition) ? "" : $" — {model.Condition}");
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تحويل «{old.ItemName}» من {await EmployeeNameAsync(old.EmployeeId, ct)} إلى {toName} — عهدة جديدة {MC(next.Number)}.";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = next.Id }) });
    }

    // ---------- Receive back (returned / damaged / lost) — or correct a recorded return ----------
    [HttpGet]
    public async Task<IActionResult> ReturnForm(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!CanReturn(c.Status))
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">العهدة مُحوَّلة أو مُلغاة — لا تُستلم.</div>", "text/html");
        return PartialView("_MaterialCustodyReturnForm", new MaterialCustodyReturnModel
        {
            Id = c.Id, Number = c.Number, ItemName = c.ItemName,
            ReturnDate = c.ReturnDate ?? ClientTime.Today,
            Status = c.Status == MaterialCustodyStatus.HandedOver ? MaterialCustodyStatus.Returned : c.Status,
            ReturnCondition = c.ReturnCondition
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(MaterialCustodyReturnModel model, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (c is null) return NotFound();
        if (!CanReturn(c.Status))
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            return Json(new { ok = false, error = "العهدة مُحوَّلة أو مُلغاة — لا تُستلم." });
        }
        if (model.Status is not (MaterialCustodyStatus.Returned or MaterialCustodyStatus.Damaged or MaterialCustodyStatus.Lost))
            ModelState.AddModelError(nameof(model.Status), "اختر حالة الاستلام.");
        if (model.ReturnDate.Date < c.HandoverDate.Date)
            ModelState.AddModelError(nameof(model.ReturnDate), "تاريخ الاستلام قبل تاريخ التسليم.");
        if (!ModelState.IsValid)
        {
            model.Number = c.Number; model.ItemName = c.ItemName;
            return PartialView("_MaterialCustodyReturnForm", model);
        }

        var correction = c.Status != MaterialCustodyStatus.HandedOver;
        c.Status = model.Status;
        c.ReturnDate = model.ReturnDate;
        c.ReturnCondition = model.ReturnCondition;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = correction
            ? $"تم تصحيح بيانات استلام العهدة العينية {MC(c.Number)} ({model.Status.Ar()})."
            : model.Status == MaterialCustodyStatus.Lost
                ? $"تم تسجيل العهدة العينية {MC(c.Number)} «{c.ItemName}» كمفقودة — يمكن استبعاد الأصل من صفحته."
                : $"تم استلام العهدة العينية {MC(c.Number)} «{c.ItemName}» ({model.Status.Ar()}).";
        return Json(new { ok = true });
    }

    private static bool CanReturn(MaterialCustodyStatus s) =>
        s is MaterialCustodyStatus.HandedOver or MaterialCustodyStatus.Returned or MaterialCustodyStatus.Damaged or MaterialCustodyStatus.Lost;

    // ---------- Cancel a custody recorded by mistake (kept in the history with the reason) ----------
    [HttpGet]
    public async Task<IActionResult> CancelForm(Guid id, CancellationToken ct)
    {
        if (!CanDelete()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.Status is MaterialCustodyStatus.Cancelled or MaterialCustodyStatus.Transferred)
            return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">العهدة مُحوَّلة أو مُلغاة بالفعل.</div>", "text/html");
        return PartialView("_MaterialCustodyCancelForm", new MaterialCustodyCancelModel { Id = c.Id, Number = c.Number, ItemName = c.ItemName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(MaterialCustodyCancelModel model, CancellationToken ct)
    {
        if (!CanDelete()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (c is null) return NotFound();
        if (c.Status is MaterialCustodyStatus.Cancelled or MaterialCustodyStatus.Transferred)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;
            return Json(new { ok = false, error = "العهدة مُحوَّلة أو مُلغاة بالفعل." });
        }
        if (!ModelState.IsValid) { model.Number = c.Number; model.ItemName = c.ItemName; return PartialView("_MaterialCustodyCancelForm", model); }
        c.Status = MaterialCustodyStatus.Cancelled;
        c.CancellationReason = model.Reason.Trim();
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم إلغاء العهدة العينية {MC(c.Number)} «{c.ItemName}» — السبب: {c.CancellationReason}.";
        return Json(new { ok = true });
    }

    // ---------- Attachments ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(Guid id, List<IFormFile>? files, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var c = await _db.MaterialCustodies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        ValidateFiles(files);
        if (!ModelState.IsValid)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;   // refused — nothing changed
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Details), new { id });
        }
        var added = await SaveFilesAsync(c.Id, files, ct);
        if (added > 0)
        {
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تمت إضافة {added} مرفق إلى العهدة العينية {MC(c.Number)}.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var a = await _db.MaterialCustodyAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        return a is null ? NotFound() : File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAttachment(Guid id, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        var a = await _db.MaterialCustodyAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        var number = await _db.MaterialCustodies.Where(c => c.Id == a.MaterialCustodyId).Select(c => c.Number).FirstOrDefaultAsync(ct);
        _db.MaterialCustodyAttachments.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المرفق «{a.FileName}» من العهدة العينية {MC(number)}.";
        return RedirectToAction(nameof(Details), new { id = a.MaterialCustodyId });
    }

    // ---------- helpers ----------
    private void ValidateFiles(List<IFormFile>? files)
    {
        var tooBig = files?.Where(f => f.Length > MaxFileBytes).Select(f => Path.GetFileName(f.FileName)).ToList();
        if (tooBig is { Count: > 0 })
            ModelState.AddModelError("files", $"الحد الأقصى لحجم الملف {MaxFileBytes / (1024 * 1024)} ميجابايت: {string.Join("، ", tooBig)}");
    }

    private async Task<int> SaveFilesAsync(Guid custodyId, List<IFormFile>? files, CancellationToken ct)
    {
        var added = 0;
        foreach (var f in files?.Where(f => f is { Length: > 0 } && f.Length <= MaxFileBytes) ?? Enumerable.Empty<IFormFile>())
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            _db.MaterialCustodyAttachments.Add(new MaterialCustodyAttachment
            {
                MaterialCustodyId = custodyId, FileName = Path.GetFileName(f.FileName),
                ContentType = f.ContentType, Size = f.Length, Data = ms.ToArray()
            });
            added++;
        }
        return added;
    }

    private async Task<MaterialCustodyFormModel> FillAsync(MaterialCustodyFormModel m, CancellationToken ct)
    {
        m.Employees = await EmployeeOptionsAsync(activeOnly: true, ct, m.EmployeeId);
        var catNames = await _db.FixedAssetCategories.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        string Label(FixedAsset a) => $"FA-{a.Number} — {a.Name}" + (string.IsNullOrEmpty(a.SerialNumber) ? "" : $" ({a.SerialNumber})");

        if (m.Id != Guid.Empty)
        {
            // Editing: the asset and employee are shown, not changeable.
            m.IsLocked = true;
            var asset = m.FixedAssetId is Guid fa ? await _db.FixedAssets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == fa, ct) : null;
            m.AssetLabel = asset is null ? "—" : Label(asset);
            m.EmployeeName = m.EmployeeId is Guid e ? await EmployeeNameAsync(e, ct) : "—";
            return m;
        }

        var busy = await _db.MaterialCustodies.Where(c => c.FixedAssetId != null && c.Status == MaterialCustodyStatus.HandedOver)
            .Select(c => c.FixedAssetId!.Value).ToListAsync(ct);
        var lost = await LostAssetIdsAsync(ct);
        m.Assets = (await _db.FixedAssets.Where(a => a.IsActive && a.DisposalDate == null && !busy.Contains(a.Id)).OrderBy(a => a.Name).ToListAsync(ct))
            .Where(a => !lost.Contains(a.Id))
            .Select(a => new FixedAssetOption(a.Id, Label(a), a.CategoryId is Guid c ? catNames.GetValueOrDefault(c) : null, a.SerialNumber, a.Quantity, a.UnitCost))
            .ToList();
        return m;
    }

    private async Task<MaterialCustodyTransferModel> FillAsync(MaterialCustodyTransferModel m, Guid currentEmployeeId, CancellationToken ct)
    {
        m.Employees = (await EmployeeOptionsAsync(activeOnly: true, ct, m.ToEmployeeId)).Where(e => e.Value != currentEmployeeId.ToString()).ToList();
        return m;
    }

    /// <summary>Null when the asset can be handed over; otherwise the Arabic reason.</summary>
    private async Task<string?> AssetBlockerAsync(FixedAsset asset, Guid? excludeCustodyId, CancellationToken ct)
    {
        if (!asset.IsActive || asset.IsDisposed) return "الأصل خارج الخدمة أو مُستبعد.";
        var active = await _db.MaterialCustodies
            .Where(c => c.FixedAssetId == asset.Id && c.Status == MaterialCustodyStatus.HandedOver && c.Id != excludeCustodyId)
            .Select(c => new { c.Number, c.EmployeeId }).FirstOrDefaultAsync(ct);
        if (active is not null) return $"الأصل في عهدة {await EmployeeNameAsync(active.EmployeeId, ct)} ({MC(active.Number)}) — استلمه أو حوِّله.";
        if ((await LostAssetIdsAsync(ct)).Contains(asset.Id)) return "الأصل مسجَّل مفقودًا — استبعده من صفحة الأصل أو صحِّح بيانات الاستلام.";
        return null;
    }

    /// <summary>Assets whose most recent custody ended «مفقودة» — not handed over again until written off or corrected.</summary>
    private async Task<HashSet<Guid>> LostAssetIdsAsync(CancellationToken ct) =>
        (await _db.MaterialCustodies.Where(c => c.FixedAssetId != null && c.Status != MaterialCustodyStatus.Cancelled)
            .Select(c => new { AssetId = c.FixedAssetId!.Value, c.Number, c.Status }).ToListAsync(ct))
        .GroupBy(c => c.AssetId)
        .Where(g => g.OrderByDescending(c => c.Number).First().Status == MaterialCustodyStatus.Lost)
        .Select(g => g.Key).ToHashSet();

    private async Task<int> NextNumberAsync(DateTime date, CancellationToken ct)
    {
        long lo = YearSerial.Base(date.Year, 4), hi = YearSerial.End(date.Year, 4);
        var max = await _db.MaterialCustodies.Where(x => x.Number > lo && x.Number < hi).MaxAsync(x => (int?)x.Number, ct);
        var local = _db.MaterialCustodies.Local.Where(x => x.Number > lo && x.Number < hi).Select(x => (int?)x.Number).Max();
        long current = Math.Max(max ?? 0, local ?? 0);
        return (int)YearSerial.Next(current > 0 ? current : null, date.Year, 4);
    }

    /// <summary>The custody shows the asset's name / category / serial / value / quantity (kept in sync on asset edits).</summary>
    private async Task CopyAssetAsync(MaterialCustody c, FixedAsset asset, CancellationToken ct)
    {
        c.FixedAssetId = asset.Id;
        c.ItemName = asset.Name;
        c.ItemType = asset.CategoryId is Guid cat ? await _db.FixedAssetCategories.Where(x => x.Id == cat).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        c.SerialNumber = asset.SerialNumber;
        c.Value = asset.UnitCost;
        c.Quantity = asset.Quantity;
    }

    private async Task<string?> EmployeeNameAsync(Guid id, CancellationToken ct) =>
        await _db.Employees.IgnoreQueryFilters().Where(e => e.Id == id).Select(e => e.FullName).FirstOrDefaultAsync(ct);

    private async Task<List<string>> ItemTypesAsync(CancellationToken ct) =>
        (await _db.MaterialCustodies.Where(c => c.ItemType != null && c.ItemType != "").Select(c => c.ItemType!).Distinct().ToListAsync(ct))
        .OrderBy(t => t).ToList();

    /// <summary>Employees for the pickers; an inactive employee already on the record stays selectable.</summary>
    private async Task<List<SelectListItem>> EmployeeOptionsAsync(bool activeOnly, CancellationToken ct, Guid? keep = null) =>
        (await _db.Employees.Where(e => !activeOnly || e.IsActive || e.Id == keep).OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.FullName }).ToListAsync(ct))
        .Select(e => new SelectListItem(e.FullName, e.Id.ToString())).ToList();
}
