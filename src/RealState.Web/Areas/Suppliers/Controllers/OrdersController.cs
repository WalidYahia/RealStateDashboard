using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;

namespace RealState.Web.Areas.Suppliers.Controllers;

[Area("Suppliers")]
[Authorize(Policy = PermissionNames.SuppliersView)]
public class OrdersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;

    public OrdersController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // ---------- Orders list ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? supplierId, Guid? projectId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.SuppliersCreate);
        ViewData["CanEdit"] = Can(PermissionNames.SuppliersEdit);
        ViewData["CanDelete"] = Can(PermissionNames.SuppliersDelete);
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.SupplierId = supplierId;
        ViewBag.ProjectId = projectId;
        ViewBag.Suppliers = await _db.Suppliers.OrderBy(s => s.Name)
            .Select(s => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
        ViewBag.Projects = await _db.Projects.OrderBy(p => p.Name)
            .Select(p => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = p.Id.ToString(), Text = p.Name }).ToListAsync(ct);
        return View(await BuildRowsAsync(ct, from, to, supplierId, projectId));
    }

    private async Task<List<OrderListItem>> BuildRowsAsync(CancellationToken ct, DateTime? from = null, DateTime? to = null,
        Guid? supplierId = null, Guid? projectId = null)
    {
        var q = _db.SupplierOrders.AsQueryable();
        if (from.HasValue) q = q.Where(o => o.OrderDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(o => o.OrderDate < to.Value.Date.AddDays(1));
        if (supplierId.HasValue) q = q.Where(o => o.SupplierId == supplierId.Value);
        if (projectId.HasValue) q = q.Where(o => o.ProjectId == projectId.Value);
        var orders = await q.OrderByDescending(o => o.Number).ToListAsync(ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var itemsByOrder = (await _db.SupplierOrderItems.GroupBy(i => i.SupplierOrderId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Cost * x.Quantity), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));
        var paidByOrder = (await _db.SupplierPayments.Where(p => p.SupplierOrderId != null)
            .GroupBy(p => p.SupplierOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var orderIds = orders.Select(o => o.Id).ToList();
        var withAttachments = (await _db.SupplierOrderAttachments
            .Where(a => orderIds.Contains(a.SupplierOrderId)).Select(a => a.SupplierOrderId).Distinct().ToListAsync(ct)).ToHashSet();

        return orders.Select(o =>
        {
            itemsByOrder.TryGetValue(o.Id, out var agg);
            return new OrderListItem
            {
                Id = o.Id,
                Number = o.Number,
                OrderDate = o.OrderDate,
                Supplier = supNames.GetValueOrDefault(o.SupplierId, "—"),
                Project = o.ProjectId.HasValue ? projNames.GetValueOrDefault(o.ProjectId.Value, "—") : "—",
                Total = agg.Sum,
                ItemCount = agg.Count,
                Paid = paidByOrder.GetValueOrDefault(o.Id, 0),
                HasAttachments = withAttachments.Contains(o.Id),
            };
        }).ToList();
    }

    // ---------- Create / edit order (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();
        if (id is null) return PartialView("_OrderForm", await FillAsync(new OrderFormModel { Items = { new OrderItemInput() } }, ct));

        var o = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        var items = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == o.Id).ToListAsync(ct);
        return PartialView("_OrderForm", await FillAsync(new OrderFormModel
        {
            Id = o.Id,
            Number = o.Number,
            SupplierId = o.SupplierId,
            ProjectId = o.ProjectId,
            OrderDate = o.OrderDate,
            Notes = o.Notes,
            Items = items.Select(i => new OrderItemInput { Name = i.Name, Cost = i.Cost, Quantity = i.Quantity }).ToList()
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(OrderFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();

        var items = (model.Items ?? new()).Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
        foreach (var it in items) if (it.Quantity <= 0) it.Quantity = 1m;   // a blank quantity means one unit
        if (items.Count == 0) ModelState.AddModelError(string.Empty, "أضف بندًا واحدًا على الأقل.");
        if (model.SupplierId is null || !await _db.Suppliers.AnyAsync(s => s.Id == model.SupplierId, ct))
            ModelState.AddModelError(nameof(model.SupplierId), "المورد غير موجود.");

        if (!ModelState.IsValid) return PartialView("_OrderForm", await FillAsync(model, ct));

        if (model.Id == Guid.Empty)
        {
            var order = new SupplierOrder
            {
                Number = await NextNumberAsync(model.OrderDate.Year, ct),
                OrderDate = model.OrderDate,
                SupplierId = model.SupplierId!.Value,
                ProjectId = model.ProjectId,
                Notes = model.Notes
            };
            _db.SupplierOrders.Add(order);
            foreach (var it in items)
                _db.SupplierOrderItems.Add(new SupplierOrderItem { SupplierOrderId = order.Id, Name = it.Name, Cost = it.Cost, Quantity = it.Quantity });
            await _accounting.SyncSupplierOrderAsync(order, items.Sum(i => i.LineTotal), ct);   // Dr المشتريات  Cr الموردون
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم إنشاء أمر التوريد PO-{order.Number:D4}.";
        }
        else
        {
            var order = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (order is null) return NotFound();
            // Don't allow the order total to drop below what's already been paid on it (would overpay it).
            var alreadyPaid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == order.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            if (items.Sum(i => i.LineTotal) < alreadyPaid)
            {
                ModelState.AddModelError(string.Empty, $"لا يمكن أن يقل إجمالي الأمر ({items.Sum(i => i.LineTotal):N0}) عن المبلغ المسدَّد عليه ({alreadyPaid:N0}).");
                return PartialView("_OrderForm", await FillAsync(model, ct));
            }
            order.OrderDate = model.OrderDate;
            order.SupplierId = model.SupplierId!.Value;
            order.ProjectId = model.ProjectId;
            order.Notes = model.Notes;
            var old = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == order.Id).ToListAsync(ct);
            foreach (var o in old) _db.SupplierOrderItems.Remove(o);
            foreach (var it in items)
                _db.SupplierOrderItems.Add(new SupplierOrderItem { SupplierOrderId = order.Id, Name = it.Name, Cost = it.Cost, Quantity = it.Quantity });
            await _accounting.SyncSupplierOrderAsync(order, items.Sum(i => i.LineTotal), ct);   // re-sync Dr المشتريات  Cr الموردون
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تعديل أمر التوريد PO-{order.Number:D4}.";
        }
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return NotFound();
        if (await _db.SupplierPayments.AnyAsync(p => p.SupplierOrderId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف أمر توريد له مدفوعات.";
            return RedirectToAction(nameof(Index));
        }
        var items = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == id).ToListAsync(ct);
        foreach (var it in items) _db.SupplierOrderItems.Remove(it);
        foreach (var a in await _db.SupplierOrderAttachments.Where(a => a.SupplierOrderId == id).ToListAsync(ct)) _db.SupplierOrderAttachments.Remove(a);
        await _accounting.RemoveObligationAsync("SupplierOrder", id, ct);   // reverse the purchase journal entry
        _db.SupplierOrders.Remove(order);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف أمر التوريد PO-{order.Number:D4}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Single order (view + print) ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, ct);
        if (order is null) return NotFound();
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        ViewBag.Attachments = await _db.SupplierOrderAttachments
            .Where(a => a.SupplierOrderId == id).OrderBy(a => a.FileName).ToListAsync(ct);
        return View(order);
    }

    // ---------- Order attachments ----------
    private static readonly string[] AttachmentTypes =
    {
        "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf", "text/plain",
        "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };
    private const long MaxAttachmentBytes = 15 * 1024 * 1024;   // 15 MB

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentUpload(Guid orderId, IFormFile? file, CancellationToken ct)
    {
        if (!await _db.SupplierOrders.AnyAsync(o => o.Id == orderId, ct)) return NotFound();
        if (file is { Length: > 0 })
        {
            if (file.Length > MaxAttachmentBytes)
                TempData["StatusMessage"] = "حجم الملف يتجاوز 15 ميجابايت.";
            else if (!AttachmentTypes.Contains(file.ContentType))
                TempData["StatusMessage"] = "صيغة الملف غير مدعومة.";
            else
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                _db.SupplierOrderAttachments.Add(new SupplierOrderAttachment
                {
                    SupplierOrderId = orderId, FileName = file.FileName, ContentType = file.ContentType,
                    Size = file.Length, Data = ms.ToArray()
                });
                await _db.SaveChangesAsync(ct);
                TempData["StatusMessage"] = "تم رفع المرفق.";
            }
        }
        return RedirectToAction(nameof(Details), new { id = orderId });
    }

    [HttpGet]
    public async Task<IActionResult> AttachmentDownload(Guid id, CancellationToken ct)
    {
        var a = await _db.SupplierOrderAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return File(a.Data, a.ContentType ?? "application/octet-stream", a.FileName);
    }

    // Inline preview (no download filename) — the browser renders images / PDF / text.
    [HttpGet]
    public async Task<IActionResult> AttachmentPreview(Guid id, CancellationToken ct)
    {
        var a = await _db.SupplierOrderAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        return File(a.Data, a.ContentType ?? "application/octet-stream");
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachmentDelete(Guid id, Guid orderId, CancellationToken ct)
    {
        var a = await _db.SupplierOrderAttachments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is not null) { _db.SupplierOrderAttachments.Remove(a); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = orderId });
    }

    [HttpGet]
    public async Task<IActionResult> PrintOrder(Guid id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, ct);
        if (order is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintOrder", order);
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? supplierId, Guid? projectId, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(ct, from, to, supplierId, projectId));
    }

    // ---------- Pay an order (expense on the order's project) ----------
    [HttpGet]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersPay)) return Forbid();
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        var (total, paid, supplierName) = await OrderTotalsAsync(order, ct);
        return PartialView("_PayForm", new SupplierPayFormModel
        {
            OrderId = order.Id,
            OrderLabel = $"PO-{order.Number:D4} — {supplierName}",
            Total = total,
            Paid = paid,
            Remaining = total - paid,
            Amount = 0,
            PaidDate = DateTime.Today,
            Safes = await SafesAsync(ct)
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersPay)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(SupplierPayFormModel model, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == model.OrderId, ct);
        if (order is null) return NotFound();
        var (total, paid, supplierName) = await OrderTotalsAsync(order, ct);
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
            model.Total = total;
            model.Paid = paid;
            model.Remaining = remaining;
            model.OrderLabel = $"PO-{order.Number:D4} — {supplierName}";
            return PartialView("_PayForm", model);
        }

        // Record as an Expense on the order's project. The expense's serial IS the pay-receipt number.
        var projPart = "";
        if (order.ProjectId.HasValue)
        {
            var pn = await _db.Projects.Where(p => p.Id == order.ProjectId).Select(p => p.Name).FirstOrDefaultAsync(ct);
            if (pn != null) projPart = $" — مشروع {pn}";
        }
        var desc = $"دفعة للمورد «{supplierName}» على أمر التوريد PO-{order.Number:D4}{projPart}";
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.SupplierPayment,
            model.Amount, model.PaidDate, desc, projectId: order.ProjectId, supplierId: order.SupplierId, ct: ct);

        var payment = new SupplierPayment
        {
            SupplierId = order.SupplierId,
            SupplierOrderId = order.Id,
            Amount = model.Amount,
            PaidDate = model.PaidDate,
            SafeId = model.SafeId.Value,
            ReceiptNo = txn.Serial,
            Description = model.Description
        };
        _db.SupplierPayments.Add(payment);
        txn.Description = $"{desc} (إيصال صرف نقدية رقم {txn.Serial:D5})";
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم سداد {model.Amount:N0} ج.م على أمر التوريد PO-{order.Number:D4} (إيصال صرف نقدية رقم {txn.Serial:D5}).";
        return Json(new { ok = true, openTab = Url.Action("Receipt", new { id = payment.Id }) });
    }

    // The supplier pay receipt IS the unified cash-payment voucher (إيصال صرف نقدية) of the linked
    // expense movement — supplier payments no longer have a separate receipt document.
    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return NotFound();
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(
            t => t.Type == TxnType.Expense && t.Source == TxnSource.SupplierPayment && t.Serial == payment.ReceiptNo, ct)
            ?? new SafeTransaction
            {
                Type = TxnType.Expense, Source = TxnSource.SupplierPayment, Serial = payment.ReceiptNo,
                Amount = payment.Amount, OccurredAt = payment.PaidDate, SafeId = payment.SafeId,
                Description = payment.Description ?? ""
            };
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == payment.SupplierId, ct);
        var order = payment.SupplierOrderId.HasValue
            ? await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == payment.SupplierOrderId, ct) : null;
        ViewBag.PartyLabel = "المورد";
        ViewBag.Party = supplier?.Name;
        ViewBag.About = order != null ? $"سداد أمر توريد PO-{order.Number:D4}" : "سداد للمورد";
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == payment.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    // ---------- helpers ----------
    private async Task<SupplierOrder?> LoadOrderAsync(Guid id, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;
        order.Items = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == id).ToListAsync(ct);
        order.Payments = await _db.SupplierPayments.Where(p => p.SupplierOrderId == id).OrderBy(p => p.PaidDate).ToListAsync(ct);
        order.Supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == order.SupplierId, ct);
        if (order.ProjectId.HasValue)
            order.Project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == order.ProjectId.Value, ct);
        return order;
    }

    private async Task<(decimal Total, decimal Paid, string SupplierName)> OrderTotalsAsync(SupplierOrder order, CancellationToken ct)
    {
        var total = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == order.Id).SumAsync(i => (decimal?)(i.Cost * i.Quantity), ct) ?? 0;
        var paid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == order.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var name = await _db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        return (total, paid, name);
    }

    // Year-prefixed serial (PO-2026 0001 = 20260001), resetting each year.
    private async Task<int> NextNumberAsync(int year, CancellationToken ct)
    {
        var yearBase = year * 10000;
        var maxThisYear = await _db.SupplierOrders.Where(o => o.Number >= yearBase && o.Number < yearBase + 10000)
            .MaxAsync(o => (int?)o.Number, ct) ?? yearBase;
        return maxThisYear + 1;
    }

    private async Task<List<SelectListItem>> SafesAsync(CancellationToken ct)
        => await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);

    private async Task<OrderFormModel> FillAsync(OrderFormModel model, CancellationToken ct)
    {
        model.Suppliers = await _db.Suppliers.OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
        model.Projects = await _db.Projects.OrderBy(p => p.Name)
            .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Code + " — " + p.Name }).ToListAsync(ct);
        return model;
    }
}
