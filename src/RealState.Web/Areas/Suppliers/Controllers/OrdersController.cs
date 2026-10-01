using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Suppliers.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>
/// Purchase orders (أوامر التوريد) — the first purchasing stage. An order is a plain request for products:
/// saving it touches no ledger, stock or safe, and it has no supplier. Suppliers are billed through
/// purchase invoices, which may reference an order (several invoices per order).
/// </summary>
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
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? projectId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.SuppliersCreate);
        ViewData["CanEdit"] = Can(PermissionNames.SuppliersEdit);
        ViewData["CanViewInvoices"] = Can(PermissionNames.PurchaseInvoicesView);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.ProjectId = projectId;
        ViewBag.Projects = await ProjectsAsync(_db, ct);
        return View(await BuildRowsAsync(ct, from, to, projectId));
    }

    private async Task<List<OrderListItem>> BuildRowsAsync(CancellationToken ct, DateTime? from = null, DateTime? to = null, Guid? projectId = null)
    {
        var q = _db.SupplierOrders.AsQueryable();
        if (from.HasValue) q = q.Where(o => o.OrderDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(o => o.OrderDate < to.Value.Date.AddDays(1));
        if (projectId.HasValue) q = q.Where(o => o.ProjectId == projectId.Value);
        var orders = await q.OrderByDescending(o => o.Number).ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var itemsByOrder = (await _db.SupplierOrderItems.Where(i => orderIds.Contains(i.SupplierOrderId)).GroupBy(i => i.SupplierOrderId)
            .Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Qty, x.Count));
        var invoicesByOrder = (await _db.PurchaseInvoices.Where(i => i.PurchaseOrderId != null && orderIds.Contains(i.PurchaseOrderId!.Value))
            .OrderBy(i => i.Number).Select(i => new { OrderId = i.PurchaseOrderId!.Value, i.Id, i.Number }).ToListAsync(ct))
            .GroupBy(x => x.OrderId).ToDictionary(g => g.Key, g => g.Select(x => new InvoiceRef(x.Id, x.Number)).ToList());
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
                Project = o.ProjectId.HasValue ? projNames.GetValueOrDefault(o.ProjectId.Value, "—") : "—",
                TotalQuantity = agg.Qty,
                ItemCount = agg.Count,
                HasAttachments = withAttachments.Contains(o.Id),
                Invoices = invoicesByOrder.GetValueOrDefault(o.Id, new()),
            };
        }).ToList();
    }

    // ---------- Create / edit order (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();
        if (id is null)
            return PartialView("_OrderForm", await FillAsync(new OrderFormModel
            {
                Number = await NextNumberAsync(DateTime.Today.Year, ct),
                Items = { new DocItemInput() }
            }, ct));

        var o = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NotFound();
        var items = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == o.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return PartialView("_OrderForm", await FillAsync(new OrderFormModel
        {
            Id = o.Id,
            Number = o.Number,
            ProjectId = o.ProjectId,
            OrderDate = o.OrderDate,
            Notes = o.Notes,
            Items = items.Select(i => new DocItemInput
            {
                ProductId = i.ProductId,
                LegacyName = i.ProductId is null ? i.Name : null,
                Cost = i.Cost,
                Quantity = i.Quantity,
                UnitLevel = i.UnitLevel
            }).ToList()
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(OrderFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();

        var items = (model.Items ?? new()).Where(i => !i.IsBlank).ToList();
        foreach (var it in items) if (it.Quantity <= 0) it.Quantity = 1m;   // a blank quantity means one unit
        var productIds = items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (items.Count == 0) ModelState.AddModelError(string.Empty, "أضف صنفًا واحدًا على الأقل.");
        if (productIds.Count != products.Count) ModelState.AddModelError(string.Empty, "أحد الأصناف المختارة غير موجود.");
        if (model.ProjectId.HasValue && !await _db.Projects.AnyAsync(p => p.Id == model.ProjectId, ct))
            ModelState.AddModelError(nameof(model.ProjectId), "المشروع غير موجود.");

        var units = await ProductUnits.LoadAsync(_db, productIds, ct);   // each line's unit + its factor to the smallest unit

        // An order can't be cut below what its purchase invoices have already billed, product by product (smallest unit).
        if (model.Id != Guid.Empty)
        {
            var invoiced = await InvoicedQtyAsync(_db, model.Id, null, ct);
            if (invoiced.Count > 0)
            {
                var newQty = items.Where(i => i.ProductId.HasValue).GroupBy(i => i.ProductId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity * ResolveUnit(units, g.Key, i.UnitLevel).Factor));
                var labels = await _db.Products.Where(p => invoiced.Keys.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => ProductLabel(p.Sku, p.Name), ct);
                foreach (var (pid, billed) in invoiced.Where(x => x.Value > 0))
                    if (newQty.GetValueOrDefault(pid) < billed)
                        ModelState.AddModelError(string.Empty,
                            $"لا يمكن أن تقل كمية الصنف «{labels.GetValueOrDefault(pid, "—")}» في الأمر ({units.Of(pid).Breakdown(newQty.GetValueOrDefault(pid))}) عن الكمية المُفوتَرة منه ({units.Of(pid).Breakdown(billed)}).");
            }
        }

        if (!ModelState.IsValid) return PartialView("_OrderForm", await FillAsync(model, ct));

        SupplierOrder order;
        if (model.Id == Guid.Empty)
        {
            order = new SupplierOrder { Number = await NextNumberAsync(model.OrderDate.Year, ct) };
            _db.SupplierOrders.Add(order);
        }
        else
        {
            order = (await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == model.Id, ct))!;
            if (order is null) return NotFound();
            if (order.IsLegacy)
            {
                // A legacy order still owns its supplier payable: don't let its total drop below what's paid on it.
                var alreadyPaid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == order.Id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
                var newTotal = items.Sum(i => i.LineTotal);
                if (newTotal < alreadyPaid)
                {
                    ModelState.AddModelError(string.Empty, $"لا يمكن أن يقل إجمالي الأمر ({newTotal:N0}) عن المبلغ المسدَّد عليه ({alreadyPaid:N0}).");
                    return PartialView("_OrderForm", await FillAsync(model, ct));
                }
            }
            _db.SupplierOrderItems.RemoveRange(await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == order.Id).ToListAsync(ct));
        }

        order.OrderDate = model.OrderDate;
        order.ProjectId = model.ProjectId;
        order.Notes = model.Notes;
        foreach (var it in items)
        {
            var name = it.ProductId is Guid pid ? ProductLabel(products[pid].Sku, products[pid].Name) : it.LegacyName!.Trim();
            var unit = it.ProductId is Guid up ? ResolveUnit(units, up, it.UnitLevel) : null;
            _db.SupplierOrderItems.Add(new SupplierOrderItem
            {
                // Orders are quantity-only; only a legacy order (which owns a payable) keeps its unit costs.
                SupplierOrderId = order.Id, ProductId = it.ProductId, Name = name, Cost = order.IsLegacy ? it.Cost : 0m, Quantity = it.Quantity,
                Unit = unit?.Name, UnitLevel = unit?.Level ?? 1, UnitFactor = unit?.Factor ?? 1m
            });
        }
        // New orders post nothing. A legacy order (one with a supplier) keeps its payable in sync with its lines.
        if (order.IsLegacy)
            await _accounting.SyncSupplierOrderAsync(order, Math.Round(items.Sum(i => i.Cost * i.Quantity), 2), ct);

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.Id == Guid.Empty
            ? $"تم إنشاء أمر التوريد {PO(order.Number)}."
            : $"تم تعديل أمر التوريد {PO(order.Number)}.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return NotFound();
        var invoiceCount = await _db.PurchaseInvoices.CountAsync(i => i.PurchaseOrderId == id, ct);
        if (invoiceCount > 0)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف أمر التوريد {PO(order.Number)} لارتباطه بفواتير مشتريات (عدد: {invoiceCount}).";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (await _db.SupplierPayments.AnyAsync(p => p.SupplierOrderId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف أمر توريد له مدفوعات.";
            return RedirectToAction(nameof(Details), new { id });
        }
        _db.SupplierOrderItems.RemoveRange(await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == id).ToListAsync(ct));
        _db.SupplierOrderAttachments.RemoveRange(await _db.SupplierOrderAttachments.Where(a => a.SupplierOrderId == id).ToListAsync(ct));
        await _accounting.RemoveObligationAsync("SupplierOrder", id, ct);   // legacy orders: reverse their purchase entry
        _db.SupplierOrders.Remove(order);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف أمر التوريد {PO(order.Number)}.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Single order (view + print) ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var order = await LoadOrderAsync(id, ct);
        if (order is null) return NotFound();
        ViewData["CanViewInvoices"] = Can(PermissionNames.PurchaseInvoicesView);
        ViewData["CanCreateInvoice"] = Can(PermissionNames.PurchaseInvoicesCreate);
        ViewBag.Attachments = await _db.SupplierOrderAttachments
            .Where(a => a.SupplierOrderId == id).OrderBy(a => a.FileName).ToListAsync(ct);
        ViewBag.Invoices = await InvoicesOfAsync(id, ct);
        var invoiced = await InvoicedQtyAsync(_db, id, null, ct);
        ViewBag.InvoicedQty = invoiced;   // per product, for المُفوتَر / المتبقي columns
        // Fully invoiced = the order has product lines and every product's invoiced quantity reached its ordered
        // quantity — nothing is left to bill, so the «إنشاء فاتورة مشتريات» action is hidden.
        var ordered = await OrderedQtyAsync(_db, id, ct);
        ViewData["FullyInvoiced"] = ordered.Count > 0 && ordered.All(o => invoiced.GetValueOrDefault(o.Key) >= o.Value);
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
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? projectId, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(ct, from, to, projectId));
    }

    // ---------- helpers ----------
    private async Task<SupplierOrder?> LoadOrderAsync(Guid id, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return null;
        order.Items = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        order.Payments = await _db.SupplierPayments.Where(p => p.SupplierOrderId == id).OrderBy(p => p.PaidDate).ToListAsync(ct);
        if (order.SupplierId.HasValue)
            order.Supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == order.SupplierId.Value, ct);
        if (order.ProjectId.HasValue)
            order.Project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == order.ProjectId.Value, ct);
        return order;
    }

    /// <summary>Invoices that reference the order, with their supplier and total.</summary>
    private async Task<List<InvoiceListItem>> InvoicesOfAsync(Guid orderId, CancellationToken ct)
    {
        var invoices = await _db.PurchaseInvoices.Where(i => i.PurchaseOrderId == orderId).OrderBy(i => i.Number).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var totals = (await _db.PurchaseInvoiceItems.Where(i => ids.Contains(i.PurchaseInvoiceId)).GroupBy(i => i.PurchaseInvoiceId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));
        return invoices.Select(i => new InvoiceListItem
        {
            Id = i.Id, Number = i.Number, InvoiceDate = i.InvoiceDate,
            Supplier = supNames.GetValueOrDefault(i.SupplierId, "—"),
            Total = totals.GetValueOrDefault(i.Id).Sum, ItemCount = totals.GetValueOrDefault(i.Id).Count
        }).ToList();
    }

    // Year-prefixed serial (PO-2026 0001 = 20260001), resetting each year.
    private async Task<int> NextNumberAsync(int year, CancellationToken ct)
    {
        var yearBase = year * 10000;
        var maxThisYear = await _db.SupplierOrders.Where(o => o.Number >= yearBase && o.Number < yearBase + 10000)
            .MaxAsync(o => (int?)o.Number, ct) ?? yearBase;
        return maxThisYear + 1;
    }

    private async Task<OrderFormModel> FillAsync(OrderFormModel model, CancellationToken ct)
    {
        model.Projects = await ProjectsAsync(_db, ct);
        model.Products = await ProductsAsync(_db, model.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value), ct);
        // Orders are quantity-only; a legacy order (with a supplier + payable) still edits its unit costs.
        model.ShowCost = model.Id != Guid.Empty && await _db.SupplierOrders.AnyAsync(o => o.Id == model.Id && o.SupplierId != null, ct);
        if (model.Id == Guid.Empty && model.Number == 0) model.Number = await NextNumberAsync(model.OrderDate.Year, ct);
        return model;
    }
}
