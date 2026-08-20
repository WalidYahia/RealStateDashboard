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
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.SuppliersCreate);
        ViewData["CanEdit"] = Can(PermissionNames.SuppliersEdit);
        ViewData["CanDelete"] = Can(PermissionNames.SuppliersDelete);
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        ViewBag.From = from;
        ViewBag.To = to;
        return View(await BuildRowsAsync(ct, from, to));
    }

    private async Task<List<OrderListItem>> BuildRowsAsync(CancellationToken ct, DateTime? from = null, DateTime? to = null)
    {
        var q = _db.SupplierOrders.AsQueryable();
        if (from.HasValue) q = q.Where(o => o.OrderDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(o => o.OrderDate < to.Value.Date.AddDays(1));
        var orders = await q.OrderByDescending(o => o.Number).ToListAsync(ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var itemsByOrder = (await _db.SupplierOrderItems.GroupBy(i => i.SupplierOrderId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.Cost), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));
        var paidByOrder = (await _db.SupplierPayments.Where(p => p.SupplierOrderId != null)
            .GroupBy(p => p.SupplierOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

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
            Items = items.Select(i => new OrderItemInput { Name = i.Name, Cost = i.Cost }).ToList()
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(OrderFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();

        var items = (model.Items ?? new()).Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
        if (items.Count == 0) ModelState.AddModelError(string.Empty, "أضف بندًا واحدًا على الأقل.");
        if (model.SupplierId is null || !await _db.Suppliers.AnyAsync(s => s.Id == model.SupplierId, ct))
            ModelState.AddModelError(nameof(model.SupplierId), "المورد غير موجود.");

        if (!ModelState.IsValid) return PartialView("_OrderForm", await FillAsync(model, ct));

        if (model.Id == Guid.Empty)
        {
            var order = new SupplierOrder
            {
                Number = await NextNumberAsync(ct),
                OrderDate = model.OrderDate,
                SupplierId = model.SupplierId!.Value,
                ProjectId = model.ProjectId,
                Notes = model.Notes
            };
            _db.SupplierOrders.Add(order);
            foreach (var it in items)
                _db.SupplierOrderItems.Add(new SupplierOrderItem { SupplierOrderId = order.Id, Name = it.Name, Cost = it.Cost });
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم إنشاء أمر التوريد PO-{order.Number:D4}.";
        }
        else
        {
            var order = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (order is null) return NotFound();
            // Don't allow the order total to drop below what's already been paid on it (would overpay it).
            var alreadyPaid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == order.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            if (items.Sum(i => i.Cost) < alreadyPaid)
            {
                ModelState.AddModelError(string.Empty, $"لا يمكن أن يقل إجمالي الأمر ({items.Sum(i => i.Cost):N0}) عن المبلغ المسدَّد عليه ({alreadyPaid:N0}).");
                return PartialView("_OrderForm", await FillAsync(model, ct));
            }
            order.OrderDate = model.OrderDate;
            order.SupplierId = model.SupplierId!.Value;
            order.ProjectId = model.ProjectId;
            order.Notes = model.Notes;
            var old = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == order.Id).ToListAsync(ct);
            foreach (var o in old) _db.SupplierOrderItems.Remove(o);
            foreach (var it in items)
                _db.SupplierOrderItems.Add(new SupplierOrderItem { SupplierOrderId = order.Id, Name = it.Name, Cost = it.Cost });
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
        return View(order);
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
    public async Task<IActionResult> PrintList(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(ct));
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
            model.Amount, model.PaidDate, desc, projectId: order.ProjectId, ct: ct);

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
        txn.Description = $"{desc} (إيصال دفع P-{txn.Serial:D5})";
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم سداد {model.Amount:N0} ج.م على أمر التوريد PO-{order.Number:D4} (إيصال دفع P-{txn.Serial:D5}).";
        return Json(new { ok = true, openTab = Url.Action("Receipt", new { id = payment.Id }) });
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        var payment = await _db.SupplierPayments.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (payment is null) return NotFound();
        ViewBag.Supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == payment.SupplierId, ct);
        ViewBag.Order = payment.SupplierOrderId.HasValue
            ? await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == payment.SupplierOrderId, ct) : null;
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Receipt", payment);
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
        var total = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == order.Id).SumAsync(i => (decimal?)i.Cost, ct) ?? 0;
        var paid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == order.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var name = await _db.Suppliers.Where(s => s.Id == order.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        return (total, paid, name);
    }

    private async Task<int> NextNumberAsync(CancellationToken ct)
        => (await _db.SupplierOrders.MaxAsync(o => (int?)o.Number, ct) ?? 0) + 1;

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
