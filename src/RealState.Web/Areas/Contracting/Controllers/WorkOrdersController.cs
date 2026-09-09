using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Contracting.Models;

namespace RealState.Web.Areas.Contracting.Controllers;

[Area("Contracting")]
[Authorize(Policy = PermissionNames.ContractingView)]
public class WorkOrdersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;

    public WorkOrdersController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);
    private string CurrentName() => User.FindFirst("full_name")?.Value ?? User.Identity?.Name ?? "—";

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? contractorId, Guid? projectId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.ContractingCreate);
        ViewData["CanEdit"] = Can(PermissionNames.ContractingEdit);
        ViewData["CanDelete"] = Can(PermissionNames.ContractingDelete);
        ViewData["CanProgress"] = Can(PermissionNames.ContractingEdit);
        ViewData["CanPay"] = Can(PermissionNames.ContractingPay);
        ViewBag.From = from; ViewBag.To = to; ViewBag.ContractorId = contractorId; ViewBag.ProjectId = projectId;
        ViewBag.Contractors = await _db.Contractors.OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync(ct);
        ViewBag.Projects = await _db.Projects.OrderBy(p => p.Name)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name }).ToListAsync(ct);

        return View(await BuildRowsAsync(from, to, contractorId, projectId, ct));
    }

    private async Task<List<WorkOrderListItem>> BuildRowsAsync(DateTime? from, DateTime? to, Guid? contractorId, Guid? projectId, CancellationToken ct)
    {
        var q = _db.WorkOrders.AsQueryable();
        if (from.HasValue) q = q.Where(o => o.OrderDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(o => o.OrderDate < to.Value.Date.AddDays(1));
        if (contractorId.HasValue) q = q.Where(o => o.ContractorId == contractorId.Value);
        if (projectId.HasValue) q = q.Where(o => o.ProjectId == projectId.Value);
        var orders = await q.OrderByDescending(o => o.Number).ToListAsync(ct);
        var conNames = await _db.Contractors.ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var paidByOrder = (await _db.WorkOrderPayments.Where(p => p.WorkOrderId != null)
            .GroupBy(p => p.WorkOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

        return orders.Select(o => new WorkOrderListItem
        {
            Id = o.Id, Number = o.Number, OrderDate = o.OrderDate,
            Contractor = conNames.GetValueOrDefault(o.ContractorId, "—"),
            Project = projNames.GetValueOrDefault(o.ProjectId, "—"),
            ItemDescription = o.ItemDescription, Total = o.Total,
            ExecutionPercent = o.ExecutionPercent, UpliftPercent = o.UpliftPercent,
            Deductions = o.Deductions, ActualTotal = o.ActualTotal,
            Paid = paidByOrder.GetValueOrDefault(o.Id, 0)
        }).ToList();
    }

    // ---------- Prints ----------
    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? contractorId, Guid? projectId, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(from, to, contractorId, projectId, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Csv(DateTime? from, DateTime? to, Guid? contractorId, Guid? projectId, CancellationToken ct)
    {
        var rows = await BuildRowsAsync(from, to, contractorId, projectId, ct);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string P(decimal v) => v.ToString("0.##", inv) + "%";
        var headers = new[] { "رقم الأمر", "التاريخ", "المقاول", "المشروع", "البيان", "الإجمالي", "نسبة التنفيذ", "نسبة التعلية", "الخصومات", "الإجمالي الفعلي", "المدفوع", "المتبقي" };
        var data = rows.Select(o => (IReadOnlyList<object?>)new object?[]
        {
            "WO-" + o.Number, o.OrderDate.ToString("yyyy-MM-dd", inv), o.Contractor, o.Project, o.ItemDescription,
            o.Total, P(o.ExecutionPercent), P(o.UpliftPercent), o.Deductions, o.ActualTotal, o.Paid, o.Remaining
        });
        var totals = new object?[] { "الإجمالي", null, null, null, null, rows.Sum(o => o.Total), null, null,
            rows.Sum(o => o.Deductions), rows.Sum(o => o.ActualTotal), rows.Sum(o => o.Paid), rows.Sum(o => o.Remaining) };
        return RealState.Web.Common.Xlsx.File($"أوامر الشغل {DateTime.Now:yyyy-MM-dd}.xlsx", "أوامر الشغل", headers, data, totals);
    }

    [HttpGet]
    public async Task<IActionResult> PrintOrder(Guid id, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintOrder", new WorkOrderDetailsVm
        {
            Order = o,
            ContractorName = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—",
            ProjectName = await _db.Projects.Where(p => p.Id == o.ProjectId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "—",
            Paid = await _db.WorkOrderPayments.Where(p => p.WorkOrderId == id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0,
            Logs = await _db.WorkOrderLogs.Where(l => l.WorkOrderId == id).OrderByDescending(l => l.At).ToListAsync(ct)
        });
    }

    // ---------- Create / edit (single item, mandatory project) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.ContractingCreate : PermissionNames.ContractingEdit)) return Forbid();
        var model = new WorkOrderFormModel();
        if (id is not null)
        {
            var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return NotFound();
            model = new WorkOrderFormModel
            {
                Id = o.Id, Number = o.Number, ContractorId = o.ContractorId, ProjectId = o.ProjectId,
                OrderDate = o.OrderDate, ItemDescription = o.ItemDescription, Unit = o.Unit,
                Quantity = o.Quantity, Rate = o.Rate, Notes = o.Notes
            };
        }
        await FillListsAsync(model, ct);
        return PartialView("_WorkOrderForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(WorkOrderFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.ContractingCreate : PermissionNames.ContractingEdit)) return Forbid();
        if (model.ContractorId is null || !await _db.Contractors.AnyAsync(c => c.Id == model.ContractorId, ct))
            ModelState.AddModelError(nameof(model.ContractorId), "اختر مقاولًا صالحًا.");
        if (model.ProjectId is null || !await _db.Projects.AnyAsync(p => p.Id == model.ProjectId, ct))
            ModelState.AddModelError(nameof(model.ProjectId), "اختر مشروعًا صالحًا.");

        if (!ModelState.IsValid) { await FillListsAsync(model, ct); return PartialView("_WorkOrderForm", model); }

        WorkOrder order;
        if (model.Id == Guid.Empty)
        {
            var yearBase = model.OrderDate.Year * 100000;
            var last = await _db.WorkOrders.Where(o => o.Number >= yearBase && o.Number < yearBase + 100000)
                .MaxAsync(o => (int?)o.Number, ct);
            order = new WorkOrder
            {
                Number = (last ?? yearBase) + 1,
                ContractorId = model.ContractorId!.Value, ProjectId = model.ProjectId!.Value, OrderDate = model.OrderDate,
                ItemDescription = model.ItemDescription, Unit = model.Unit, Quantity = model.Quantity, Rate = model.Rate,
                Notes = model.Notes
                // ExecutionPercent / UpliftPercent / Deductions default to 0 — set later from the list.
            };
            _db.WorkOrders.Add(order);
        }
        else
        {
            order = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (order is null) return NotFound();
            order.ContractorId = model.ContractorId!.Value; order.ProjectId = model.ProjectId!.Value; order.OrderDate = model.OrderDate;
            order.ItemDescription = model.ItemDescription; order.Unit = model.Unit; order.Quantity = model.Quantity; order.Rate = model.Rate;
            order.Notes = model.Notes;
        }
        // Keep the contractor-cost / payable entry in sync with the order's current الإجمالي الفعلي.
        await _accounting.SyncWorkOrderAsync(order, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ أمر الشغل.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        if (await _db.WorkOrderPayments.AnyAsync(p => p.WorkOrderId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف أمر شغل له مدفوعات.";
            return RedirectToAction(nameof(Index));
        }
        foreach (var l in await _db.WorkOrderLogs.Where(l => l.WorkOrderId == id).ToListAsync(ct)) _db.WorkOrderLogs.Remove(l);
        await _accounting.RemoveObligationAsync("WorkOrder", id, ct);   // reverse the contractor-cost entry
        _db.WorkOrders.Remove(o);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف أمر الشغل WO-{o.Number}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Details (order + its progress logs) ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        ViewData["CanProgress"] = Can(PermissionNames.ContractingEdit);
        ViewData["CanPay"] = Can(PermissionNames.ContractingPay);
        return View(new WorkOrderDetailsVm
        {
            Order = o,
            ContractorName = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—",
            ProjectName = await _db.Projects.Where(p => p.Id == o.ProjectId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "—",
            Paid = await _db.WorkOrderPayments.Where(p => p.WorkOrderId == id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0,
            Logs = await _db.WorkOrderLogs.Where(l => l.WorkOrderId == id).OrderByDescending(l => l.At).ToListAsync(ct)
        });
    }

    // ---------- Update one progress field (popup) → sets the value and appends a log ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ContractingEdit)]
    public async Task<IActionResult> ProgressForm(Guid id, WorkOrderField field, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        var current = field switch
        {
            WorkOrderField.Execution => o.ExecutionPercent,
            WorkOrderField.Uplift => o.UpliftPercent,
            _ => o.Deductions
        };
        return PartialView("_ProgressForm", new ProgressUpdateModel { WorkOrderId = id, Field = field, Value = current });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProgressForm(ProgressUpdateModel model, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == model.WorkOrderId, ct);
        if (o is null) return NotFound();

        if (model.Field is WorkOrderField.Execution or WorkOrderField.Uplift && (model.Value < 0 || model.Value > 100))
            ModelState.AddModelError(nameof(model.Value), "النسبة يجب أن تكون بين 0 و 100.");
        if (model.Field == WorkOrderField.Deductions && model.Value < 0)
            ModelState.AddModelError(nameof(model.Value), "قيمة غير صالحة.");
        if (!ModelState.IsValid) return PartialView("_ProgressForm", model);

        switch (model.Field)
        {
            case WorkOrderField.Execution: o.ExecutionPercent = model.Value; break;
            case WorkOrderField.Uplift: o.UpliftPercent = model.Value; break;
            case WorkOrderField.Deductions: o.Deductions = model.Value; break;
        }
        _db.WorkOrderLogs.Add(new WorkOrderLog
        {
            WorkOrderId = o.Id, Field = model.Field, Value = model.Value, At = DateTime.Now, ByName = CurrentName()
        });
        // Progress changed الإجمالي الفعلي → re-sync the contractor-cost / payable entry.
        await _accounting.SyncWorkOrderAsync(o, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تحديث {model.Field.Ar()}.";
        return Json(new { ok = true });
    }

    // ---------- Edit / delete a log entry ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ContractingEdit)]
    public async Task<IActionResult> LogForm(Guid id, CancellationToken ct)
    {
        var l = await _db.WorkOrderLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();
        return PartialView("_LogForm", new WorkOrderLogEditModel
        {
            Id = l.Id, WorkOrderId = l.WorkOrderId, Field = l.Field, Value = l.Value, At = l.At
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogForm(WorkOrderLogEditModel model, CancellationToken ct)
    {
        var l = await _db.WorkOrderLogs.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (l is null) return NotFound();
        l.Value = model.Value; l.At = model.At;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم تحديث السجل.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogDelete(Guid id, Guid workOrderId, CancellationToken ct)
    {
        var l = await _db.WorkOrderLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is not null) { _db.WorkOrderLogs.Remove(l); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = workOrderId });
    }

    // ---------- Pay a work order (expense on its project) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.ContractingPay)]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        var (total, paid) = await OrderTotalsAsync(o, ct);
        var conName = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        return PartialView("_PayForm", new WorkOrderPayFormModel
        {
            OrderId = o.Id, OrderLabel = $"WO-{o.Number} — {conName}",
            Total = total, Paid = paid, Remaining = total - paid, Amount = total - paid,
            Safes = await SafesAsync(ct)
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingPay)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(WorkOrderPayFormModel model, CancellationToken ct)
    {
        var o = await _db.WorkOrders.FirstOrDefaultAsync(x => x.Id == model.OrderId, ct);
        if (o is null) return NotFound();
        var (total, paid) = await OrderTotalsAsync(o, ct);
        var remaining = total - paid;

        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "أدخل مبلغًا أكبر من صفر.");
        if (model.Amount > remaining)
            ModelState.AddModelError(nameof(model.Amount), $"المبلغ يتجاوز المتبقي على الأمر ({remaining:N0} ج.م).");

        if (!ModelState.IsValid)
        {
            model.Safes = await SafesAsync(ct);
            model.Total = total; model.Paid = paid; model.Remaining = remaining;
            var cn = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct);
            model.OrderLabel = $"WO-{o.Number} — {cn}";
            return PartialView("_PayForm", model);
        }

        var conName = await _db.Contractors.Where(c => c.Id == o.ContractorId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "—";
        var projName = await _db.Projects.Where(p => p.Id == o.ProjectId).Select(p => p.Name).FirstOrDefaultAsync(ct);
        var desc = $"دفعة للمقاول «{conName}» على أمر الشغل WO-{o.Number}" + (projName != null ? $" — مشروع {projName}" : "");
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.ContractorPayment,
            model.Amount, model.PaidDate, desc, projectId: o.ProjectId, contractorId: o.ContractorId, ct: ct);

        _db.WorkOrderPayments.Add(new WorkOrderPayment
        {
            ContractorId = o.ContractorId, WorkOrderId = o.Id, Amount = model.Amount, PaidDate = model.PaidDate,
            SafeId = model.SafeId.Value, ReceiptNo = txn.Serial, Description = model.Description
        });
        txn.Description = $"{desc} (إيصال صرف نقدية رقم {txn.Serial:D5})";
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم سداد {model.Amount:N0} ج.م على أمر الشغل WO-{o.Number} (إيصال رقم {txn.Serial:D5}).";
        return Json(new { ok = true });
    }

    // ---------- helpers ----------
    private async Task<(decimal Total, decimal Paid)> OrderTotalsAsync(WorkOrder o, CancellationToken ct)
    {
        var paid = await _db.WorkOrderPayments.Where(p => p.WorkOrderId == o.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        return (o.ActualTotal, paid);
    }

    private async Task FillListsAsync(WorkOrderFormModel model, CancellationToken ct)
    {
        model.Contractors = await _db.Contractors.OrderBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name }).ToListAsync(ct);
        model.Projects = await _db.Projects.OrderBy(p => p.Name)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name }).ToListAsync(ct);
    }

    private async Task<List<SelectListItem>> SafesAsync(CancellationToken ct)
        => await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
}
