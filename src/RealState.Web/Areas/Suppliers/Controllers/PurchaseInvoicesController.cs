using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Suppliers.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>
/// Purchase invoices (فواتير المشتريات) — the second purchasing stage. An invoice bills a supplier for
/// products, optionally against a project and/or a purchase order, and posts the supplier obligation:
/// Dr بضاعة واردة لم تُفوتر (stock products) / Dr المشتريات (non-stock products), Cr الموردون.
/// Its stock-tracked lines are received into the chosen warehouse by an automatic, posted goods receipt
/// at the invoice unit costs (Dr المخزون / Cr بضاعة واردة لم تُفوتر), so stock and product cost follow the
/// invoice under the costing method and the GRNI account nets to zero.
/// Supplier payments are made per invoice (see <see cref="PaymentsController"/>).
/// </summary>
[Area("Suppliers")]
[Authorize(Policy = PermissionNames.PurchaseInvoicesView)]
public class PurchaseInvoicesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly IInventoryEngine _inventory;

    public PurchaseInvoicesController(IApplicationDbContext db, ICurrentUserService currentUser,
        IAccountingService accounting, IInventoryEngine inventory)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _inventory = inventory;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // ---------- Invoices list ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? supplierId, Guid? projectId, Guid? orderId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.PurchaseInvoicesCreate);
        ViewData["CanEdit"] = Can(PermissionNames.PurchaseInvoicesEdit);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.SupplierId = supplierId;
        ViewBag.ProjectId = projectId;
        ViewBag.OrderId = orderId;
        ViewBag.Suppliers = await SuppliersAsync(_db, ct);
        ViewBag.Projects = await ProjectsAsync(_db, ct);
        ViewBag.Orders = await OrdersAsync(_db, ct);
        return View(await BuildRowsAsync(ct, from, to, supplierId, projectId, orderId));
    }

    private async Task<List<InvoiceListItem>> BuildRowsAsync(CancellationToken ct, DateTime? from = null, DateTime? to = null,
        Guid? supplierId = null, Guid? projectId = null, Guid? orderId = null)
    {
        var q = _db.PurchaseInvoices.AsQueryable();
        if (from.HasValue) q = q.Where(i => i.InvoiceDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(i => i.InvoiceDate < to.Value.Date.AddDays(1));
        if (supplierId.HasValue) q = q.Where(i => i.SupplierId == supplierId.Value);
        if (projectId.HasValue) q = q.Where(i => i.ProjectId == projectId.Value);
        if (orderId.HasValue) q = q.Where(i => i.PurchaseOrderId == orderId.Value);
        var invoices = await q.OrderByDescending(i => i.Number).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var orderNumbers = await _db.SupplierOrders.ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var itemsByInvoice = (await _db.PurchaseInvoiceItems.Where(i => ids.Contains(i.PurchaseInvoiceId)).GroupBy(i => i.PurchaseInvoiceId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));
        var paidByInvoice = (await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId != null && ids.Contains(p.PurchaseInvoiceId!.Value))
            .GroupBy(p => p.PurchaseInvoiceId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var returns = await InvoiceReturns.PurchaseAsync(_db, ids, ct);

        return invoices.Select(i =>
        {
            itemsByInvoice.TryGetValue(i.Id, out var agg);
            return new InvoiceListItem
            {
                Id = i.Id,
                Number = i.Number,
                InvoiceDate = i.InvoiceDate,
                Supplier = supNames.GetValueOrDefault(i.SupplierId, "—"),
                Project = i.ProjectId.HasValue ? projNames.GetValueOrDefault(i.ProjectId.Value, "—") : "—",
                OrderId = i.PurchaseOrderId,
                OrderNumber = i.PurchaseOrderId.HasValue && orderNumbers.TryGetValue(i.PurchaseOrderId.Value, out var n) ? n : null,
                Total = agg.Sum,
                ItemCount = agg.Count,
                Paid = paidByInvoice.GetValueOrDefault(i.Id, 0),
                Returned = returns.GetValueOrDefault(i.Id).Returned,
                Refunded = returns.GetValueOrDefault(i.Id).Refunded,
            };
        }).ToList();
    }

    // ---------- Create / edit invoice (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, Guid? orderId, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.PurchaseInvoicesCreate : PermissionNames.PurchaseInvoicesEdit)) return Forbid();
        if (id is null)
        {
            var model = new InvoiceFormModel
            {
                Number = await NextNumberAsync(DateTime.Today.Year, ct),
                WarehouseId = await _db.Warehouses.Where(w => w.IsDefault && w.IsActive).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct)
            };
            // Opened from an order: pre-select it and copy its product lines + project.
            var order = orderId.HasValue ? await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct) : null;
            if (order is not null)
            {
                model.PurchaseOrderId = order.Id;
                model.ProjectId = order.ProjectId;
                // Only what's still to be invoiced on the order (ordered − already invoiced), per product.
                model.Items = (await RemainingOrderLinesAsync(order.Id, null, ct))
                    .Select(l => new DocItemInput { ProductId = l.ProductId, Cost = l.Cost ?? 0, Quantity = l.Remaining, UnitLevel = l.UnitLevel }).ToList();
            }
            if (model.Items.Count == 0) model.Items.Add(new DocItemInput());
            return PartialView("_InvoiceForm", await FillAsync(model, ct));
        }

        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inv is null) return NotFound();
        if (await _db.PurchaseReturns.AnyAsync(r => r.PurchaseInvoiceId == inv.Id, ct))
            ModelState.AddModelError(string.Empty, HasReturnsMessage(inv.Number, "تعديل"));
        var items = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == inv.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return PartialView("_InvoiceForm", await FillAsync(new InvoiceFormModel
        {
            Id = inv.Id,
            Number = inv.Number,
            SupplierId = inv.SupplierId,
            ProjectId = inv.ProjectId,
            PurchaseOrderId = inv.PurchaseOrderId,
            WarehouseId = inv.WarehouseId,
            InvoiceDate = inv.InvoiceDate,
            Notes = inv.Notes,
            Items = items.Select(i => new DocItemInput { ProductId = i.ProductId, Cost = i.Cost, Quantity = i.Quantity, UnitLevel = i.UnitLevel }).ToList()
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(InvoiceFormModel model, CancellationToken ct)
    {
        var isNew = model.Id == Guid.Empty;
        if (!Can(isNew ? PermissionNames.PurchaseInvoicesCreate : PermissionNames.PurchaseInvoicesEdit)) return Forbid();

        var items = (model.Items ?? new()).Where(i => i.ProductId.HasValue).ToList();
        foreach (var it in items) if (it.Quantity <= 0) it.Quantity = 1m;   // a blank quantity means one unit
        var productIds = items.Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var total = items.Sum(i => i.LineTotal);
        var units = await ProductUnits.LoadAsync(_db, productIds, ct);   // each line's unit + its factor to the smallest unit
        decimal BaseQty(DocItemInput i) => i.Quantity * ResolveUnit(units, i.ProductId!.Value, i.UnitLevel).Factor;

        if (items.Count == 0) ModelState.AddModelError(string.Empty, "أضف صنفًا واحدًا على الأقل.");
        if (items.Any(i => i.Cost <= 0)) ModelState.AddModelError(string.Empty, "أدخل تكلفة الوحدة (أكبر من صفر) لكل صنف.");
        if (productIds.Count != items.Count) ModelState.AddModelError(string.Empty, "لا يمكن تكرار نفس الصنف في أكثر من سطر — ادمج الكميات في سطر واحد.");
        if (productIds.Count != products.Count) ModelState.AddModelError(string.Empty, "أحد الأصناف المختارة غير موجود.");
        // Stock-tracked lines are received into a warehouse by the invoice's automatic goods receipt.
        var hasStockLines = products.Values.Any(p => p.TrackInventory);
        if (hasStockLines && (model.WarehouseId is null || !await _db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId && w.IsActive, ct)))
            ModelState.AddModelError(nameof(model.WarehouseId), "اختر المخزن الذي تُستلم فيه الأصناف المخزنية.");
        if (model.SupplierId is null || !await _db.Suppliers.AnyAsync(s => s.Id == model.SupplierId, ct))
            ModelState.AddModelError(nameof(model.SupplierId), "المورد غير موجود.");
        if (model.ProjectId.HasValue && !await _db.Projects.AnyAsync(p => p.Id == model.ProjectId, ct))
            ModelState.AddModelError(nameof(model.ProjectId), "المشروع غير موجود.");
        if (model.PurchaseOrderId.HasValue)
        {
            var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == model.PurchaseOrderId, ct);
            if (order is null) ModelState.AddModelError(nameof(model.PurchaseOrderId), "أمر التوريد غير موجود.");
            else
            {
                // An invoice linked to an order may only bill the order's products, and — together with the order's
                // other invoices — no more of each than was ordered.
                var ordered = await OrderedQtyAsync(_db, order.Id, ct);
                var invoiced = await InvoicedQtyAsync(_db, order.Id, isNew ? null : model.Id, ct);
                foreach (var g in items.GroupBy(i => i.ProductId!.Value))
                {
                    var label = products.TryGetValue(g.Key, out var pr) ? ProductLabel(pr.Sku, pr.Name) : "—";
                    var qty = g.Sum(BaseQty);   // compared in the product's smallest unit
                    var u = units.Of(g.Key);
                    if (!ordered.TryGetValue(g.Key, out var orderedQty))
                    {
                        ModelState.AddModelError(string.Empty, $"الصنف «{label}» غير موجود في أمر التوريد {PO(order.Number)}.");
                        continue;
                    }
                    var remaining = orderedQty - invoiced.GetValueOrDefault(g.Key);
                    if (qty > remaining)
                        ModelState.AddModelError(string.Empty,
                            $"كمية الصنف «{label}» ({u.Breakdown(qty)}) تتجاوز المتبقي في أمر التوريد {PO(order.Number)} " +
                            $"({u.Breakdown(Math.Max(remaining, 0))} من {u.Breakdown(orderedQty)} — المُفوتَر في فواتير أخرى {u.Breakdown(invoiced.GetValueOrDefault(g.Key))}).");
                }
            }
        }

        PurchaseInvoice? inv = null;
        if (!isNew)
        {
            inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (inv is null) return NotFound();
            // Returns are tied to the invoice's lines, quantities and costs — editing under them would unbalance them.
            if (await _db.PurchaseReturns.AnyAsync(r => r.PurchaseInvoiceId == inv.Id, ct))
                ModelState.AddModelError(string.Empty, HasReturnsMessage(inv.Number, "تعديل"));
            var payments = await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId == inv.Id).ToListAsync(ct);
            var alreadyPaid = payments.Sum(p => p.Amount);
            if (payments.Count > 0 && model.SupplierId != inv.SupplierId)
                ModelState.AddModelError(nameof(model.SupplierId), "لا يمكن تغيير مورد فاتورة عليها مدفوعات.");
            if (total < alreadyPaid)
                ModelState.AddModelError(string.Empty, $"لا يمكن أن يقل إجمالي الفاتورة ({total:N2}) عن المبلغ المسدَّد عليها ({alreadyPaid:N2}).");
        }

        if (!ModelState.IsValid) return PartialView("_InvoiceForm", await FillAsync(model, ct));

        // Seeds/repairs the GRNI account + posting profile the entry posts to (commits on its own, before our changes).
        await _inventory.EnsureDefaultsAsync(ct);

        if (inv is null)
        {
            inv = new PurchaseInvoice { Number = await NextNumberAsync(model.InvoiceDate.Year, ct) };
            _db.PurchaseInvoices.Add(inv);
        }
        else
        {
            _db.PurchaseInvoiceItems.RemoveRange(await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == inv.Id).ToListAsync(ct));
        }

        inv.InvoiceDate = model.InvoiceDate;
        inv.SupplierId = model.SupplierId!.Value;
        inv.ProjectId = model.ProjectId;
        inv.PurchaseOrderId = model.PurchaseOrderId;
        inv.WarehouseId = model.WarehouseId;
        inv.Notes = model.Notes;
        var newItems = items.Select(it =>
        {
            var p = products[it.ProductId!.Value];
            var unit = ResolveUnit(units, p.Id, it.UnitLevel);   // snapshot the line's unit + factor
            return new PurchaseInvoiceItem
            {
                PurchaseInvoiceId = inv.Id, ProductId = p.Id, Name = ProductLabel(p.Sku, p.Name),
                Unit = unit.Name, UnitLevel = unit.Level, UnitFactor = unit.Factor,
                Cost = it.Cost, Quantity = it.Quantity, LineTotal = it.LineTotal
            };
        }).ToList();
        _db.PurchaseInvoiceItems.AddRange(newItems);
        await _accounting.SyncPurchaseInvoiceAsync(inv, newItems, ct);   // Dr GRNI / المشتريات  Cr الموردون

        ReceiptSync receipt;
        try
        {
            // Dr المخزون / Cr GRNI — brings the stock in at the invoice costs (feeds the costing method).
            receipt = await SyncReceiptAsync(inv, newItems.Where(i => products[i.ProductId].TrackInventory).ToList(), ct);
        }
        catch (InvalidOperationException ex)
        {
            // Engine refusal (future / back-dated posting, reversing goods already issued, …). Nothing is saved.
            ModelState.AddModelError(string.Empty, $"تعذّر استلام الأصناف في المخزن: {ex.Message}");
            return PartialView("_InvoiceForm", await FillAsync(model, ct));
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Json(new { ok = false, error = "تعذّر الحفظ — قد يكون رقم الفاتورة أو إذن الاستلام مستخدمًا بالفعل. أعد المحاولة." });
        }
        var receiptPart = receipt switch
        {
            { Posted: { } r, Reversed: { } old } => $" — عُكس إذن الاستلام {old.Number} وأُنشئ بدلًا منه إذن الاستلام {r.Number}",
            { Posted: { } r } => $" — وأُنشئ إذن الاستلام {r.Number}",
            { Reversed: { } old } => $" — وعُكس إذن الاستلام {old.Number}",
            _ => ""
        };
        TempData["StatusMessage"] = isNew
            ? $"تم إنشاء فاتورة المشتريات {PI(inv.Number)} بإجمالي {total:N2} ج.م{receiptPart}."
            : $"تم تعديل فاتورة المشتريات {PI(inv.Number)}{receiptPart}.";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = inv.Id }) });
    }

    /// <summary>What <see cref="SyncReceiptAsync"/> did: the receipt it posted, the one it reversed, or the one it kept unchanged.</summary>
    private sealed record ReceiptSync(GoodsReceipt? Posted, GoodsReceipt? Reversed, GoodsReceipt? Kept);

    /// <summary>
    /// Keeps the invoice's automatic goods receipt in step with its stock-tracked lines. An unchanged receipt
    /// is kept; otherwise the current one is reversed (the inventory audit trail keeps it as معكوس) and a new
    /// one is created and posted at the invoice date, warehouse and unit costs. Adds to the unit of work
    /// only; throws <see cref="InvalidOperationException"/> when the inventory engine refuses.
    /// </summary>
    private async Task<ReceiptSync> SyncReceiptAsync(PurchaseInvoice inv, List<PurchaseInvoiceItem> stockLines, CancellationToken ct)
    {
        var current = await _db.GoodsReceipts.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.PurchaseInvoiceId == inv.Id && r.Status == InventoryDocStatus.Posted, ct);

        if (current is not null && stockLines.Count > 0
            && current.WarehouseId == inv.WarehouseId && current.Date.Date == inv.InvoiceDate.Date
            && current.Lines.Count == stockLines.Count
            && stockLines.All(l => current.Lines.Any(x => x.ProductId == l.ProductId && x.Quantity == l.Quantity * l.UnitFactor
                                                          && x.TotalCost == l.LineTotal && x.UnitFactor == l.UnitFactor)))
        {
            current.SupplierId = inv.SupplierId;
            return new ReceiptSync(null, null, current);
        }

        if (current is not null)
        {
            await _inventory.ReverseAsync(InventorySources.GoodsReceipt, current.Id, ct);
            current.Status = InventoryDocStatus.Reversed;
        }
        if (stockLines.Count == 0) return new ReceiptSync(null, current, null);

        var receipt = new GoodsReceipt
        {
            Number = await _inventory.NextNumberAsync(_db.GoodsReceipts.Select(x => x.Number), _db.GoodsReceipts.Local.Select(x => x.Number), inv.InvoiceDate.Year, ct),
            Date = inv.InvoiceDate,
            WarehouseId = inv.WarehouseId!.Value,
            SupplierId = inv.SupplierId,
            PurchaseInvoiceId = inv.Id,
            Notes = $"استلام تلقائي من فاتورة المشتريات {PI(inv.Number)}",
            // Stock is received in the smallest unit at the per-smallest-unit cost; TotalCost keeps the invoice line total
            // exact (the GRNI the invoice debits nets to zero), and the line remembers the unit it was entered in.
            Lines = stockLines.Select(l => new GoodsReceiptLine
            {
                ProductId = l.ProductId, Quantity = l.Quantity * l.UnitFactor, UnitCost = Math.Round(l.Cost / l.UnitFactor, 6), TotalCost = l.LineTotal,
                UnitLevel = l.UnitLevel, UnitFactor = l.UnitFactor, UnitName = l.Unit
            }).ToList()
        };
        _db.GoodsReceipts.Add(receipt);
        await _inventory.PostReceiptAsync(receipt, ct);
        return new ReceiptSync(receipt, current, null);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.PurchaseInvoicesDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inv is null) return NotFound();
        if (await _db.SupplierPayments.AnyAsync(p => p.PurchaseInvoiceId == id, ct))
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف فاتورة المشتريات {PI(inv.Number)} لوجود مدفوعات عليها.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (await _db.PurchaseReturns.AnyAsync(r => r.PurchaseInvoiceId == id, ct))
        {
            TempData["ErrorMessage"] = HasReturnsMessage(inv.Number, "حذف");
            return RedirectToAction(nameof(Details), new { id });
        }
        // Take the received stock back out: reverse the invoice's goods receipt (kept as معكوس for the audit trail).
        var receipt = await _db.GoodsReceipts.FirstOrDefaultAsync(r => r.PurchaseInvoiceId == id && r.Status == InventoryDocStatus.Posted, ct);
        if (receipt is not null)
        {
            try { await _inventory.ReverseAsync(InventorySources.GoodsReceipt, receipt.Id, ct); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"لا يمكن حذف فاتورة المشتريات {PI(inv.Number)}: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
            receipt.Status = InventoryDocStatus.Reversed;
        }
        _db.PurchaseInvoiceItems.RemoveRange(await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == id).ToListAsync(ct));
        await _accounting.RemoveObligationAsync(AccountingSources.PurchaseInvoice, id, ct);   // reverse the purchase entry
        _db.PurchaseInvoices.Remove(inv);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = receipt is null
            ? $"تم حذف فاتورة المشتريات {PI(inv.Number)}."
            : $"تم حذف فاتورة المشتريات {PI(inv.Number)} وعكس إذن الاستلام {receipt.Number}.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Deletes a payment (سند صرف) made on the invoice: its expense movement and journal entry are removed (the money
    /// goes back into the safe, Dr الخزنة / Cr الموردون reversed) and the amount is owed on the invoice again.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionNames.PurchaseInvoicesDeletePayment)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePayment(Guid id, CancellationToken ct)
    {
        var p = await _db.SupplierPayments.FirstOrDefaultAsync(x => x.Id == id && x.PurchaseInvoiceId != null, ct);
        if (p is null) return NotFound();
        var invId = p.PurchaseInvoiceId!.Value;
        var invNo = await _db.PurchaseInvoices.Where(i => i.Id == invId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        // Cash the supplier refunded through returns came out of what was paid — paid can't be left below it.
        var refunded = (await InvoiceReturns.ForPurchaseInvoiceAsync(_db, invId, ct)).Refunded;
        var paid = await _db.SupplierPayments.Where(x => x.PurchaseInvoiceId == invId).SumAsync(x => x.Amount, ct);
        if (refunded > 0 && paid - p.Amount < refunded)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف سند الصرف رقم {p.ReceiptNo}: استُرد من المورد {refunded:N2} ج.م في مرتجعات هذه الفاتورة، ولا يجوز أن يقل المسدَّد عنه — احذف استرداد المرتجع أولًا.";
            return RedirectToAction(nameof(Details), new { id = invId });
        }
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(
            t => t.Type == TxnType.Expense && t.Source == TxnSource.SupplierPayment && t.Serial == p.ReceiptNo, ct);
        if (txn is not null) await _accounting.RemoveTransactionAsync(txn, ct);   // the expense + its journal entry
        _db.SupplierPayments.Remove(p);
        await _db.SaveChangesAsync(ct);
        var safe = await _db.Safes.Where(s => s.Id == p.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        TempData["StatusMessage"] = $"تم حذف سند الصرف رقم {p.ReceiptNo} ({p.Amount:N2} ج.م) على فاتورة المشتريات {PI(invNo)} وإعادة المبلغ إلى الخزنة «{safe}».";
        return RedirectToAction(nameof(Details), new { id = invId });
    }

    [HttpGet]
    public IActionResult Help() => PartialView("_Help");

    /// <summary>مرتجعات المشتريات — kept for old links; the returns live in <see cref="PurchaseReturnsController"/>.</summary>
    [HttpGet]
    public IActionResult Returns() => RedirectToAction(nameof(PurchaseReturnsController.Index), "PurchaseReturns");

    // ---------- Single invoice (view + print) ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        if (vm is null) return NotFound();
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        ViewData["CanDeletePayment"] = Can(PermissionNames.PurchaseInvoicesDeletePayment);
        ViewData["CanViewSuppliers"] = Can(PermissionNames.SuppliersView);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintInvoice(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        if (vm is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintInvoice", vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? supplierId, Guid? projectId, Guid? orderId, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(ct, from, to, supplierId, projectId, orderId));
    }

    /// <summary>A purchase order's product lines + project as JSON — the invoice form copies them when an order is picked.</summary>
    [HttpGet]
    public async Task<IActionResult> OrderLines(Guid id, Guid? exceptInvoiceId, CancellationToken ct)
    {
        var order = await _db.SupplierOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        var lines = (await RemainingOrderLinesAsync(id, exceptInvoiceId, ct))
            .Select(l => new { productId = l.ProductId, cost = l.Cost, quantity = l.Remaining, unitLevel = l.UnitLevel });
        return Json(new { projectId = order.ProjectId, lines });
    }

    /// <summary>
    /// An order's products still to be invoiced: per product, ordered − already invoiced by the order's other
    /// invoices (excluding <paramref name="exceptInvoiceId"/> when editing), in order-line order; fully invoiced
    /// products are left out. Cost is null unless the (legacy) order carried one — orders are quantity-only.
    /// </summary>
    // Counted in the smallest unit; returned in the product's first order line's unit.
    private async Task<List<(Guid ProductId, decimal? Cost, decimal Remaining, byte UnitLevel)>> RemainingOrderLinesAsync(Guid orderId, Guid? exceptInvoiceId, CancellationToken ct)
    {
        var lines = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == orderId && i.ProductId != null)
            .OrderBy(i => i.CreatedAt).Select(i => new { ProductId = i.ProductId!.Value, i.Cost, i.UnitLevel, i.UnitFactor }).ToListAsync(ct);
        var ordered = await OrderedQtyAsync(_db, orderId, ct);
        var invoiced = await InvoicedQtyAsync(_db, orderId, exceptInvoiceId, ct);
        var result = new List<(Guid ProductId, decimal? Cost, decimal Remaining, byte UnitLevel)>();
        foreach (var g in lines.GroupBy(l => l.ProductId))
        {
            var remaining = ordered.GetValueOrDefault(g.Key) - invoiced.GetValueOrDefault(g.Key);
            if (remaining <= 0) continue;
            var first = g.First();
            var f = first.UnitFactor > 0 ? first.UnitFactor : 1m;
            var cost = g.Max(x => x.Cost);
            result.Add((g.Key, cost > 0 ? cost : null, Math.Round(remaining / f, 4), first.UnitLevel));
        }
        return result;
    }

    // ---------- helpers ----------
    private async Task<InvoiceDetailsVm?> LoadAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return null;
        inv.Items = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        inv.Payments = await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId == id).OrderBy(p => p.PaidDate).ToListAsync(ct);
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == inv.SupplierId, ct);
        var project = inv.ProjectId.HasValue ? await _db.Projects.FirstOrDefaultAsync(p => p.Id == inv.ProjectId.Value, ct) : null;
        var orderNumber = inv.PurchaseOrderId.HasValue
            ? await _db.SupplierOrders.Where(o => o.Id == inv.PurchaseOrderId.Value).Select(o => (int?)o.Number).FirstOrDefaultAsync(ct)
            : null;
        return new InvoiceDetailsVm
        {
            Invoice = inv,
            Supplier = supplier?.Name ?? "—",
            SupplierPhone = supplier?.Phone,
            Project = project is null ? null : $"#{project.Code} {project.Name}",
            OrderNumber = orderNumber,
            Warehouse = inv.WarehouseId.HasValue
                ? await _db.Warehouses.Where(w => w.Id == inv.WarehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync(ct)
                : null,
            Receipts = (await _db.GoodsReceipts.Where(r => r.PurchaseInvoiceId == id).OrderByDescending(r => r.Number)
                    .Select(r => new { r.Id, r.Number, r.Date, r.Status }).ToListAsync(ct))
                .Select(r => new InvoiceReceiptRef(r.Id, r.Number, r.Date, r.Status)).ToList(),
            Returns = await ReturnRefsAsync(id, ct),
        };
    }

    /// <summary>The invoice's purchase returns (debit notes), oldest first.</summary>
    private async Task<List<RealState.Web.Areas.Sales.Models.InvoiceReturnRef>> ReturnRefsAsync(Guid invoiceId, CancellationToken ct)
    {
        var rets = await _db.PurchaseReturns.Where(r => r.PurchaseInvoiceId == invoiceId).OrderBy(r => r.ReturnDate).ThenBy(r => r.Number).ToListAsync(ct);
        var retIds = rets.Select(r => r.Id).ToList();
        var totals = await _db.PurchaseReturnItems.Where(i => retIds.Contains(i.PurchaseReturnId)).GroupBy(i => i.PurchaseReturnId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        return rets.Select(r => new RealState.Web.Areas.Sales.Models.InvoiceReturnRef(r.Id, r.Number, r.ReturnDate, totals.GetValueOrDefault(r.Id), r.RefundAmount, r.RefundVoucherNo)).ToList();
    }

    private static string HasReturnsMessage(int number, string verb)
        => $"لا يمكن {verb} فاتورة المشتريات {PI(number)} لوجود مرتجعات عليها — احذف المرتجعات أولًا.";


    // Year-prefixed serial (PI-2026000001 = 2026 × 1000000 + 1), resetting each year.
    private Task<int> NextNumberAsync(int year, CancellationToken ct)
        => _inventory.NextNumberAsync(_db.PurchaseInvoices.Select(x => x.Number), _db.PurchaseInvoices.Local.Select(x => x.Number), year, ct);

    private async Task<InvoiceFormModel> FillAsync(InvoiceFormModel model, CancellationToken ct)
    {
        model.Suppliers = await SuppliersAsync(_db, ct);
        model.Projects = await ProjectsAsync(_db, ct);
        // Only orders with something left to bill (plus the invoice's own order when editing).
        model.Orders = await OpenOrdersAsync(_db, model.Id == Guid.Empty ? null : model.Id, model.PurchaseOrderId, ct);
        model.Warehouses = await _db.Warehouses.Where(w => w.IsActive || w.Id == model.WarehouseId).OrderBy(w => w.Name)
            .Select(w => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToListAsync(ct);
        model.Products = await ProductsAsync(_db, model.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value), ct);
        if (model.Id == Guid.Empty && model.Number == 0) model.Number = await NextNumberAsync(model.InvoiceDate.Year, ct);
        return model;
    }
}
