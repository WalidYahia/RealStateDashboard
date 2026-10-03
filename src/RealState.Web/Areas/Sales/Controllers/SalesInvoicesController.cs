using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Application.Inventory;
using RealState.Web.Areas.Sales.Models;
using RealState.Web.Areas.Suppliers.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Sales.Controllers;

/// <summary>
/// Product sales invoices (فواتير المبيعات) — selling inventory products to a customer (the same Customers as the
/// real-estate sales module, but unrelated to its contracts). Mirrors purchase invoices:
/// <list type="bullet">
/// <item>The invoice posts Dr العملاء (customer receivable) / Cr إيرادات مبيعات البضائع (the inventory posting
/// profile's sales-revenue account).</item>
/// <item>Its stock-tracked lines leave the chosen warehouse through an automatic, posted goods issue (reason: sale),
/// costed by the costing method: Dr تكلفة المبيعات / Cr المخزون. Non-stock products (services) are revenue only.</item>
/// <item>Collections (تحصيل) are recorded per invoice: a safe Income movement, Dr الخزنة / Cr العملاء.</item>
/// </list>
/// </summary>
[Area("Sales")]
[Authorize(Policy = PermissionNames.SalesInvoicesView)]
public class SalesInvoicesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly IInventoryEngine _inventory;
    private readonly ISafeBalanceGuard _guard;

    public SalesInvoicesController(IApplicationDbContext db, ICurrentUserService currentUser,
        IAccountingService accounting, IInventoryEngine inventory, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _inventory = inventory;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    /// <summary>Sales invoice label, e.g. SI-2026000001.</summary>
    public static string SI(int number) => "SI-" + number;

    // ---------- Summary (landing page of the «المبيعات» menu) ----------
    public async Task<IActionResult> Summary(CancellationToken ct) => View(await BuildSummaryAsync(ct));

    [HttpGet]
    public async Task<IActionResult> SummaryPrint(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("SummaryPrint", await BuildSummaryAsync(ct));
    }

    /// <summary>مرتجعات المبيعات — kept for old links; the returns live in <see cref="SalesReturnsController"/>.</summary>
    [HttpGet]
    public IActionResult Returns() => RedirectToAction(nameof(SalesReturnsController.Index), "SalesReturns");

    private static readonly string[] ArMonths =
        { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private async Task<SalesInvoiceSummaryVm> BuildSummaryAsync(CancellationToken ct)
    {
        var invoices = await _db.ProductSalesInvoices.Select(i => new { i.Id, i.Number, i.CustomerId, i.InvoiceDate }).ToListAsync(ct);
        var totals = (await _db.ProductSalesInvoiceItems.GroupBy(x => x.SalesInvoiceId)
                .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var collections = await _db.SalesInvoiceCollections.Select(c => new { c.SalesInvoiceId, c.Amount, c.CollectedDate }).ToListAsync(ct);
        var collectedByInvoice = collections.GroupBy(c => c.SalesInvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        // Returns are credit notes on their invoice: sales are net of them, and cash refunded through them is no longer collected.
        var returns = await InvoiceReturns.SalesAsync(_db, null, ct);
        // Cost of sales = the invoices' posted goods issues, less the cost that came back with sales returns
        // (reversed documents net to zero and are left out).
        var cogs = (await (from l in _db.GoodsIssueLines
                           join g in _db.GoodsIssues on l.GoodsIssueId equals g.Id
                           where g.SalesInvoiceId != null && g.Status == InventoryDocStatus.Posted
                           select (decimal?)l.TotalCost).SumAsync(ct) ?? 0m)
                   - (await (from l in _db.GoodsReceiptLines
                             join g in _db.GoodsReceipts on l.GoodsReceiptId equals g.Id
                             where g.SalesReturnId != null && g.Status == InventoryDocStatus.Posted
                             select (decimal?)l.TotalCost).SumAsync(ct) ?? 0m);
        var custIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var custNames = await _db.Customers.Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        decimal Total(Guid id) => totals.GetValueOrDefault(id) - returns.GetValueOrDefault(id).Returned;
        decimal Collected(Guid id) => collectedByInvoice.GetValueOrDefault(id) - returns.GetValueOrDefault(id).Refunded;

        var now = DateTime.Today;
        var thisStart = new DateTime(now.Year, now.Month, 1);
        var nextStart = thisStart.AddMonths(1);
        var prevStart = thisStart.AddMonths(-1);
        bool In(DateTime d, DateTime a, DateTime b) => d >= a && d < b;

        // First invoice date per customer → "new buyers" in a month.
        var firstBuy = invoices.GroupBy(i => i.CustomerId).ToDictionary(g => g.Key, g => g.Min(x => x.InvoiceDate));

        // Last 6 calendar months, including months without sales.
        var byMonth = Enumerable.Range(0, 6).Select(k => thisStart.AddMonths(k - 5)).Select(m =>
            new MonthlySales(ArMonths[m.Month], invoices.Where(i => In(i.InvoiceDate, m, m.AddMonths(1))).Sum(i => Total(i.Id)))).ToList();

        var perCustomer = invoices.GroupBy(i => i.CustomerId).Select(g => new
        {
            CustomerId = g.Key,
            Sales = g.Sum(i => Total(i.Id)),
            Remaining = g.Sum(i => Total(i.Id) - Collected(i.Id)),
            Count = g.Count()
        }).ToList();

        return new SalesInvoiceSummaryVm
        {
            InvoicesCount = invoices.Count,
            TotalSales = invoices.Sum(i => Total(i.Id)),
            TotalCollected = invoices.Sum(i => Collected(i.Id)),
            CostOfSales = cogs,
            BuyingCustomers = perCustomer.Count,
            CustomersWithBalance = perCustomer.Count(c => c.Remaining > 0),
            SalesThisMonth = invoices.Where(i => In(i.InvoiceDate, thisStart, nextStart)).Sum(i => Total(i.Id)),
            SalesPrevMonth = invoices.Where(i => In(i.InvoiceDate, prevStart, thisStart)).Sum(i => Total(i.Id)),
            CollectedThisMonth = collections.Where(c => In(c.CollectedDate, thisStart, nextStart)).Sum(c => c.Amount),
            CollectedPrevMonth = collections.Where(c => In(c.CollectedDate, prevStart, thisStart)).Sum(c => c.Amount),
            InvoicesThisMonth = invoices.Count(i => In(i.InvoiceDate, thisStart, nextStart)),
            InvoicesPrevMonth = invoices.Count(i => In(i.InvoiceDate, prevStart, thisStart)),
            NewBuyersThisMonth = firstBuy.Values.Count(d => In(d, thisStart, nextStart)),
            NewBuyersPrevMonth = firstBuy.Values.Count(d => In(d, prevStart, thisStart)),
            ByMonth = byMonth,
            TopCustomers = perCustomer.OrderByDescending(c => c.Sales).Take(6)
                .Select(c => new CustomerAmount(c.CustomerId, custNames.GetValueOrDefault(c.CustomerId, "—"), c.Sales, c.Count)).ToList(),
            TopDebtors = perCustomer.Where(c => c.Remaining > 0).OrderByDescending(c => c.Remaining).Take(6)
                .Select(c => new CustomerAmount(c.CustomerId, custNames.GetValueOrDefault(c.CustomerId, "—"), c.Remaining, c.Count)).ToList(),
            Latest = invoices.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Number).Take(6)
                .Select(i => new RecentSalesInvoice(i.Id, i.Number, custNames.GetValueOrDefault(i.CustomerId, "—"), Total(i.Id), Total(i.Id) - Collected(i.Id), i.InvoiceDate))
                .ToList(),
        };
    }

    // ---------- Invoices list ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? customerId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanCreate"] = Can(PermissionNames.SalesInvoicesCreate);
        ViewData["CanEdit"] = Can(PermissionNames.SalesInvoicesEdit);
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.CustomerId = customerId;
        ViewBag.Customers = await CustomersAsync(null, ct);
        return View(await BuildRowsAsync(ct, from, to, customerId));
    }

    private async Task<List<SalesInvoiceListItem>> BuildRowsAsync(CancellationToken ct, DateTime? from = null, DateTime? to = null,
        Guid? customerId = null)
    {
        var q = _db.ProductSalesInvoices.AsQueryable();
        if (from.HasValue) q = q.Where(i => i.InvoiceDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(i => i.InvoiceDate < to.Value.Date.AddDays(1));
        if (customerId.HasValue) q = q.Where(i => i.CustomerId == customerId.Value);
        var invoices = await q.OrderByDescending(i => i.Number).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();
        var custIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var custNames = await _db.Customers.Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var itemsByInvoice = (await _db.ProductSalesInvoiceItems.Where(i => ids.Contains(i.SalesInvoiceId)).GroupBy(i => i.SalesInvoiceId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));
        var collectedByInvoice = (await _db.SalesInvoiceCollections.Where(c => ids.Contains(c.SalesInvoiceId))
            .GroupBy(c => c.SalesInvoiceId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var returns = await InvoiceReturns.SalesAsync(_db, ids, ct);

        return invoices.Select(i =>
        {
            itemsByInvoice.TryGetValue(i.Id, out var agg);
            return new SalesInvoiceListItem
            {
                Id = i.Id,
                Number = i.Number,
                InvoiceDate = i.InvoiceDate,
                CustomerId = i.CustomerId,
                Customer = custNames.GetValueOrDefault(i.CustomerId, "—"),
                Total = agg.Sum,
                ItemCount = agg.Count,
                Collected = collectedByInvoice.GetValueOrDefault(i.Id, 0),
                Returned = returns.GetValueOrDefault(i.Id).Returned,
                Refunded = returns.GetValueOrDefault(i.Id).Refunded,
            };
        }).ToList();
    }

    // ---------- Create / edit invoice (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, Guid? customerId, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SalesInvoicesCreate : PermissionNames.SalesInvoicesEdit)) return Forbid();
        await _inventory.EnsureDefaultsAsync(ct);   // default warehouse / posting profile / revenue account
        if (id is null)
        {
            var model = new SalesInvoiceFormModel
            {
                Number = await NextNumberAsync(DateTime.Today.Year, ct),
                CustomerId = customerId,
                WarehouseId = await _db.Warehouses.Where(w => w.IsDefault && w.IsActive).Select(w => (Guid?)w.Id).FirstOrDefaultAsync(ct)
            };
            model.Items.Add(new DocItemInput());
            return PartialView("_InvoiceForm", await FillAsync(model, ct));
        }

        var inv = await _db.ProductSalesInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inv is null) return NotFound();
        if (await _db.ProductSalesReturns.AnyAsync(r => r.SalesInvoiceId == inv.Id, ct))
            ModelState.AddModelError(string.Empty, HasReturnsMessage(inv.Number, "تعديل"));
        var items = await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == inv.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        return PartialView("_InvoiceForm", await FillAsync(new SalesInvoiceFormModel
        {
            Id = inv.Id,
            Number = inv.Number,
            CustomerId = inv.CustomerId,
            WarehouseId = inv.WarehouseId,
            InvoiceDate = inv.InvoiceDate,
            Notes = inv.Notes,
            Items = items.Select(i => new DocItemInput { ProductId = i.ProductId, Cost = i.Price, Quantity = i.Quantity, UnitLevel = i.UnitLevel }).ToList()
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SalesInvoiceFormModel model, CancellationToken ct)
    {
        var isNew = model.Id == Guid.Empty;
        if (!Can(isNew ? PermissionNames.SalesInvoicesCreate : PermissionNames.SalesInvoicesEdit)) return Forbid();

        var items = (model.Items ?? new()).Where(i => i.ProductId.HasValue).ToList();
        foreach (var it in items) if (it.Quantity <= 0) it.Quantity = 1m;   // a blank quantity means one unit
        var productIds = items.Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var total = items.Sum(i => i.LineTotal);
        var units = await ProductUnits.LoadAsync(_db, productIds, ct);   // each line's unit + its factor to the smallest unit

        if (items.Count == 0) ModelState.AddModelError(string.Empty, "أضف صنفًا واحدًا على الأقل.");
        if (items.Any(i => i.Cost <= 0)) ModelState.AddModelError(string.Empty, "أدخل سعر البيع (أكبر من صفر) لكل صنف.");
        if (productIds.Count != items.Count) ModelState.AddModelError(string.Empty, "لا يمكن تكرار نفس الصنف في أكثر من سطر — ادمج الكميات في سطر واحد.");
        if (productIds.Count != products.Count) ModelState.AddModelError(string.Empty, "أحد الأصناف المختارة غير موجود.");
        if (model.InvoiceDate.Date > DateTime.Today) ModelState.AddModelError(nameof(model.InvoiceDate), "لا يمكن إصدار فاتورة بتاريخ مستقبلي.");
        if (model.CustomerId is null || !await _db.Customers.AnyAsync(c => c.Id == model.CustomerId && !c.IsLead, ct))
            ModelState.AddModelError(nameof(model.CustomerId), "العميل غير موجود.");

        // Stock-tracked lines are issued from a warehouse by the invoice's automatic goods issue — and can't
        // take more than it holds (the invoice's own current issue is given back first when editing).
        var stockLines = items.Where(i => products.TryGetValue(i.ProductId!.Value, out var p) && p.TrackInventory).ToList();
        if (stockLines.Count > 0)
        {
            if (model.WarehouseId is null || !await _db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId && w.IsActive, ct))
                ModelState.AddModelError(nameof(model.WarehouseId), "اختر المخزن الذي تُصرف منه الأصناف المخزنية.");
            else
            {
                var available = await AvailableAsync(model.WarehouseId.Value, isNew ? null : model.Id, ct);
                foreach (var l in stockLines)
                {
                    // Compared in the product's smallest unit (stock is kept in it).
                    var have = available.GetValueOrDefault(l.ProductId!.Value);
                    var want = l.Quantity * ResolveUnit(units, l.ProductId!.Value, l.UnitLevel).Factor;
                    if (want > have)
                    {
                        var pr = products[l.ProductId!.Value];
                        var u = units.Of(pr.Id);
                        ModelState.AddModelError(string.Empty,
                            $"الكمية المطلوبة من «{ProductLabel(pr.Sku, pr.Name)}» ({u.Breakdown(want)}) أكبر من المتاح في المخزن ({u.Breakdown(Math.Max(have, 0))}).");
                    }
                }
            }
        }

        ProductSalesInvoice? inv = null;
        if (!isNew)
        {
            inv = await _db.ProductSalesInvoices.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (inv is null) return NotFound();
            // Returns are tied to the invoice's lines, quantities and prices — editing under them would unbalance them.
            if (await _db.ProductSalesReturns.AnyAsync(r => r.SalesInvoiceId == inv.Id, ct))
                ModelState.AddModelError(string.Empty, HasReturnsMessage(inv.Number, "تعديل"));
            var collections = await _db.SalesInvoiceCollections.Where(c => c.SalesInvoiceId == inv.Id).ToListAsync(ct);
            var alreadyCollected = collections.Sum(c => c.Amount);
            if (collections.Count > 0 && model.CustomerId != inv.CustomerId)
                ModelState.AddModelError(nameof(model.CustomerId), "لا يمكن تغيير عميل فاتورة عليها تحصيلات.");
            if (total < alreadyCollected)
                ModelState.AddModelError(string.Empty, $"لا يمكن أن يقل إجمالي الفاتورة ({total:N2}) عن المبلغ المحصَّل عليها ({alreadyCollected:N2}).");
        }

        if (!ModelState.IsValid) return PartialView("_InvoiceForm", await FillAsync(model, ct));

        // Seeds/repairs the posting profile + revenue account the entries post to (commits on its own, before our changes).
        await _inventory.EnsureDefaultsAsync(ct);

        if (inv is null)
        {
            inv = new ProductSalesInvoice { Number = await NextNumberAsync(model.InvoiceDate.Year, ct) };
            _db.ProductSalesInvoices.Add(inv);
        }
        else
        {
            _db.ProductSalesInvoiceItems.RemoveRange(await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == inv.Id).ToListAsync(ct));
        }

        inv.InvoiceDate = model.InvoiceDate;
        inv.CustomerId = model.CustomerId!.Value;
        inv.WarehouseId = model.WarehouseId;
        inv.Notes = model.Notes;
        var newItems = items.Select(it =>
        {
            var p = products[it.ProductId!.Value];
            var unit = ResolveUnit(units, p.Id, it.UnitLevel);   // snapshot the line's unit + factor
            return new ProductSalesInvoiceItem
            {
                SalesInvoiceId = inv.Id, ProductId = p.Id, Name = ProductLabel(p.Sku, p.Name),
                Unit = unit.Name, UnitLevel = unit.Level, UnitFactor = unit.Factor,
                Price = it.Cost, Quantity = it.Quantity, LineTotal = it.LineTotal
            };
        }).ToList();
        _db.ProductSalesInvoiceItems.AddRange(newItems);
        await _accounting.SyncSalesInvoiceAsync(inv, newItems, ct);   // Dr العملاء  Cr إيرادات مبيعات البضائع

        IssueSync issue;
        try
        {
            // Dr تكلفة المبيعات / Cr المخزون — takes the stock out at its cost under the costing method.
            issue = await SyncIssueAsync(inv, newItems.Where(i => products[i.ProductId].TrackInventory).ToList(), ct);
        }
        catch (InvalidOperationException ex)
        {
            // Engine refusal (not enough stock as of the date, back-dated posting, …). Nothing is saved.
            ModelState.AddModelError(string.Empty, $"تعذّر صرف الأصناف من المخزن: {ex.Message}");
            return PartialView("_InvoiceForm", await FillAsync(model, ct));
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Json(new { ok = false, error = "تعذّر الحفظ — قد يكون رقم الفاتورة أو إذن الصرف مستخدمًا بالفعل. أعد المحاولة." });
        }
        var issuePart = issue switch
        {
            { Posted: { } r, Reversed: { } old } => $" — عُكس إذن الصرف {old.Number} وأُنشئ بدلًا منه إذن الصرف {r.Number}",
            { Posted: { } r } => $" — وأُنشئ إذن الصرف {r.Number}",
            { Reversed: { } old } => $" — وعُكس إذن الصرف {old.Number}",
            _ => ""
        };
        TempData["StatusMessage"] = isNew
            ? $"تم إنشاء فاتورة المبيعات {SI(inv.Number)} بإجمالي {total:N2} ج.م{issuePart}."
            : $"تم تعديل فاتورة المبيعات {SI(inv.Number)}{issuePart}.";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = inv.Id }) });
    }

    /// <summary>What <see cref="SyncIssueAsync"/> did: the issue it posted, the one it reversed, or the one it kept unchanged.</summary>
    private sealed record IssueSync(GoodsIssue? Posted, GoodsIssue? Reversed, GoodsIssue? Kept);

    /// <summary>
    /// Keeps the invoice's automatic goods issue in step with its stock-tracked lines. An unchanged issue is kept;
    /// otherwise the current one is reversed (the inventory audit trail keeps it as معكوس, and its stock comes back)
    /// and a new one is created and posted at the invoice date and warehouse. Adds to the unit of work only;
    /// throws <see cref="InvalidOperationException"/> when the inventory engine refuses.
    /// </summary>
    private async Task<IssueSync> SyncIssueAsync(ProductSalesInvoice inv, List<ProductSalesInvoiceItem> stockLines, CancellationToken ct)
    {
        var current = await _db.GoodsIssues.Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.SalesInvoiceId == inv.Id && r.Status == InventoryDocStatus.Posted, ct);

        if (current is not null && stockLines.Count > 0
            && current.WarehouseId == inv.WarehouseId && current.Date.Date == inv.InvoiceDate.Date
            && current.Lines.Count == stockLines.Count
            && stockLines.All(l => current.Lines.Any(x => x.ProductId == l.ProductId && x.Quantity == l.Quantity * l.UnitFactor)))
        {
            current.CustomerId = inv.CustomerId;
            return new IssueSync(null, null, current);
        }

        if (current is not null)
        {
            await _inventory.ReverseAsync(InventorySources.GoodsIssue, current.Id, ct);
            current.Status = InventoryDocStatus.Reversed;
        }
        if (stockLines.Count == 0) return new IssueSync(null, current, null);

        var issue = new GoodsIssue
        {
            Number = await _inventory.NextNumberAsync(_db.GoodsIssues.Select(x => x.Number), _db.GoodsIssues.Local.Select(x => x.Number), inv.InvoiceDate.Year, ct),
            Date = inv.InvoiceDate,
            WarehouseId = inv.WarehouseId!.Value,
            Reason = IssueReason.Sale,
            SalesInvoiceId = inv.Id,
            CustomerId = inv.CustomerId,
            Notes = $"صرف تلقائي لفاتورة المبيعات {SI(inv.Number)}",
            // Issued in the smallest unit (cost of sales per the costing method); the line remembers the unit it was sold in.
            Lines = stockLines.Select(l => new GoodsIssueLine
            {
                ProductId = l.ProductId, Quantity = l.Quantity * l.UnitFactor,
                UnitLevel = l.UnitLevel, UnitFactor = l.UnitFactor, UnitName = l.Unit
            }).ToList()
        };
        _db.GoodsIssues.Add(issue);
        await _inventory.PostIssueAsync(issue, ct);
        return new IssueSync(issue, current, null);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesInvoicesDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var inv = await _db.ProductSalesInvoices.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (inv is null) return NotFound();
        if (await _db.SalesInvoiceCollections.AnyAsync(c => c.SalesInvoiceId == id, ct))
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف فاتورة المبيعات {SI(inv.Number)} لوجود تحصيلات عليها — ألغِ التحصيلات أولًا.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (await _db.ProductSalesReturns.AnyAsync(r => r.SalesInvoiceId == id, ct))
        {
            TempData["ErrorMessage"] = HasReturnsMessage(inv.Number, "حذف");
            return RedirectToAction(nameof(Details), new { id });
        }
        // Put the sold stock back: reverse the invoice's goods issue (kept as معكوس for the audit trail).
        var issue = await _db.GoodsIssues.FirstOrDefaultAsync(r => r.SalesInvoiceId == id && r.Status == InventoryDocStatus.Posted, ct);
        if (issue is not null)
        {
            try { await _inventory.ReverseAsync(InventorySources.GoodsIssue, issue.Id, ct); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"لا يمكن حذف فاتورة المبيعات {SI(inv.Number)}: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
            issue.Status = InventoryDocStatus.Reversed;
        }
        _db.ProductSalesInvoiceItems.RemoveRange(await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == id).ToListAsync(ct));
        await _accounting.RemoveObligationAsync(AccountingSources.SalesInvoice, id, ct);   // reverse the sales entry
        _db.ProductSalesInvoices.Remove(inv);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = issue is null
            ? $"تم حذف فاتورة المبيعات {SI(inv.Number)}."
            : $"تم حذف فاتورة المبيعات {SI(inv.Number)} وعكس إذن الصرف {issue.Number} (إرجاع الأصناف للمخزن).";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Help() => PartialView("_Help");

    // ---------- Single invoice (view + print) ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        if (vm is null) return NotFound();
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
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, Guid? customerId, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await BuildRowsAsync(ct, from, to, customerId));
    }

    // ---------- Collections (تحصيل) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesInvoicesCollect)]
    public async Task<IActionResult> Collect(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        if (vm is null) return NotFound();
        return PartialView("_CollectForm", new SalesCollectFormModel
        {
            InvoiceId = id,
            DocumentLabel = $"{SI(vm.Invoice.Number)} — {vm.Customer}",
            Total = vm.Total,
            Collected = vm.Collected,
            Remaining = vm.Remaining,
            Amount = vm.Remaining,
            CollectedDate = DateTime.Today,
            Safes = await SafesAsync(_db, ct)
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesInvoicesCollect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Collect(SalesCollectFormModel model, CancellationToken ct)
    {
        var vm = await LoadAsync(model.InvoiceId, ct);
        if (vm is null) return NotFound();
        var inv = vm.Invoice;

        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "أدخل مبلغًا أكبر من صفر.");
        if (model.Amount > vm.Remaining)
            ModelState.AddModelError(nameof(model.Amount), $"المبلغ يتجاوز المتبقي على الفاتورة ({vm.Remaining:N2} ج.م).");
        if (model.CollectedDate.Date > DateTime.Today)
            ModelState.AddModelError(nameof(model.CollectedDate), "لا يمكن التحصيل بتاريخ مستقبلي.");

        if (!ModelState.IsValid)
        {
            model.Safes = await SafesAsync(_db, ct);
            model.Total = vm.Total;
            model.Collected = vm.Collected;
            model.Remaining = vm.Remaining;
            model.DocumentLabel = $"{SI(inv.Number)} — {vm.Customer}";
            return PartialView("_CollectForm", model);
        }

        // Record as an Income into the safe; its serial IS the receipt number. Dr الخزنة / Cr العملاء.
        var desc = $"تحصيل من العميل «{vm.Customer}» على فاتورة المبيعات {SI(inv.Number)}";
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Income, TxnSource.SalesInvoiceCollection,
            model.Amount, model.CollectedDate, desc, customerId: inv.CustomerId, ct: ct);
        txn.Description = $"{desc} (إيصال استلام نقدية رقم {txn.Serial})";

        var collection = new SalesInvoiceCollection
        {
            CustomerId = inv.CustomerId,
            SalesInvoiceId = inv.Id,
            Amount = model.Amount,
            CollectedDate = model.CollectedDate,
            SafeId = model.SafeId.Value,
            SafeTransactionId = txn.Id,
            ReceiptNo = txn.Serial,
            Description = model.Description
        };
        _db.SalesInvoiceCollections.Add(collection);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم تحصيل {model.Amount:N2} ج.م من العميل «{vm.Customer}» على فاتورة المبيعات {SI(inv.Number)} (إيصال استلام نقدية رقم {txn.Serial}).";
        return Json(new { ok = true, openTab = Url.Action(nameof(CollectionReceipt), new { id = collection.Id }) });
    }

    /// <summary>Cancels a collection: removes its Income movement (and journal entry) — the amount goes back onto the invoice.</summary>
    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesInvoicesDeleteCollection)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelCollection(Guid id, CancellationToken ct)
    {
        var c = await _db.SalesInvoiceCollections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        var invNo = await _db.ProductSalesInvoices.Where(i => i.Id == c.SalesInvoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        // Cash already refunded through returns came out of what was collected — it can't be left larger than that.
        var refunded = (await InvoiceReturns.ForSalesInvoiceAsync(_db, c.SalesInvoiceId, ct)).Refunded;
        var collected = await _db.SalesInvoiceCollections.Where(x => x.SalesInvoiceId == c.SalesInvoiceId).SumAsync(x => x.Amount, ct);
        if (refunded > 0 && collected - c.Amount < refunded)
        {
            TempData["ErrorMessage"] = $"لا يمكن إلغاء التحصيل رقم {c.ReceiptNo}: رُدّ للعميل {refunded:N2} ج.م من مرتجعات هذه الفاتورة، ولا يجوز أن يقل المحصَّل عنه — احذف المرتجع أولًا.";
            return RedirectToAction(nameof(Details), new { id = c.SalesInvoiceId });
        }
        // Taking the money back out of the safe — «سحب على المكشوف» applies.
        if (await _guard.CheckWithdrawalAsync(c.SafeId, c.Amount, ct) is string overdraw)
        {
            TempData["ErrorMessage"] = $"لا يمكن إلغاء التحصيل رقم {c.ReceiptNo}: {overdraw}";
            return RedirectToAction(nameof(Details), new { id = c.SalesInvoiceId });
        }
        var txn = c.SafeTransactionId is Guid tid ? await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        if (txn is not null) await _accounting.RemoveTransactionAsync(txn, ct);
        _db.SalesInvoiceCollections.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم إلغاء التحصيل رقم {c.ReceiptNo} ({c.Amount:N2} ج.م) على فاتورة المبيعات {SI(invNo)}.";
        return RedirectToAction(nameof(Details), new { id = c.SalesInvoiceId });
    }

    // The collection receipt IS the unified cash-receipt voucher (إيصال استلام نقدية) of the linked income movement.
    [HttpGet]
    public async Task<IActionResult> CollectionReceipt(Guid id, CancellationToken ct)
    {
        var c = await _db.SalesInvoiceCollections.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        var txn = (c.SafeTransactionId is Guid tid ? await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) : null)
            ?? new SafeTransaction
            {
                Type = TxnType.Income, Source = TxnSource.SalesInvoiceCollection, Serial = c.ReceiptNo,
                Amount = c.Amount, OccurredAt = c.CollectedDate, SafeId = c.SafeId, Description = c.Description ?? ""
            };
        var invNo = await _db.ProductSalesInvoices.Where(i => i.Id == c.SalesInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct);
        ViewBag.PartyLabel = "العميل";
        ViewBag.Party = await _db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
        ViewBag.About = invNo.HasValue ? $"تحصيل فاتورة مبيعات {SI(invNo.Value)}" : "تحصيل فاتورة مبيعات";
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == c.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    // ---------- helpers ----------
    private async Task<SalesInvoiceDetailsVm?> LoadAsync(Guid id, CancellationToken ct)
    {
        var inv = await _db.ProductSalesInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv is null) return null;
        inv.Items = await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        inv.Collections = await _db.SalesInvoiceCollections.Where(c => c.SalesInvoiceId == id).OrderBy(c => c.CollectedDate).ThenBy(c => c.ReceiptNo).ToListAsync(ct);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == inv.CustomerId, ct);
        var issues = await _db.GoodsIssues.Where(r => r.SalesInvoiceId == id).OrderByDescending(r => r.Number)
            .Select(r => new { r.Id, r.Number, r.Date, r.Status, Cost = r.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m }).ToListAsync(ct);
        var rets = await _db.ProductSalesReturns.Where(r => r.SalesInvoiceId == id).OrderBy(r => r.ReturnDate).ThenBy(r => r.Number).ToListAsync(ct);
        var retIds = rets.Select(r => r.Id).ToList();
        var retTotals = await _db.ProductSalesReturnItems.Where(i => retIds.Contains(i.SalesReturnId)).GroupBy(i => i.SalesReturnId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        // The cost that came back into stock with the returns (their posted goods receipts).
        var returnedCost = await (from l in _db.GoodsReceiptLines
                                  join g in _db.GoodsReceipts on l.GoodsReceiptId equals g.Id
                                  where g.SalesReturnId != null && retIds.Contains(g.SalesReturnId.Value) && g.Status == InventoryDocStatus.Posted
                                  select (decimal?)l.TotalCost).SumAsync(ct) ?? 0m;
        return new SalesInvoiceDetailsVm
        {
            Invoice = inv,
            Customer = customer?.FullName ?? "—",
            CustomerPhone = customer?.Phone,
            Warehouse = inv.WarehouseId.HasValue
                ? await _db.Warehouses.Where(w => w.Id == inv.WarehouseId.Value).Select(w => w.Name).FirstOrDefaultAsync(ct)
                : null,
            Issues = issues.Select(r => new SalesInvoiceIssueRef(r.Id, r.Number, r.Date, r.Status, r.Cost)).ToList(),
            Returns = rets.Select(r => new InvoiceReturnRef(r.Id, r.Number, r.ReturnDate, retTotals.GetValueOrDefault(r.Id), r.RefundAmount, r.RefundVoucherNo)).ToList(),
            ReturnedCost = returnedCost,
        };
    }

    private static string HasReturnsMessage(int number, string verb)
        => $"لا يمكن {verb} فاتورة المبيعات {SI(number)} لوجود مرتجعات عليها — احذف المرتجعات أولًا.";

    // Year-prefixed serial (SI-2026000001 = 2026 × 1000000 + 1), resetting each year.
    private Task<int> NextNumberAsync(int year, CancellationToken ct)
        => _inventory.NextNumberAsync(_db.ProductSalesInvoices.Select(x => x.Number), _db.ProductSalesInvoices.Local.Select(x => x.Number), year, ct);

    /// <summary>
    /// On-hand quantity per product in a warehouse (all dates), plus — when editing — what the invoice's own
    /// current goods issue took from it, since saving gives that back before issuing again.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> AvailableAsync(Guid warehouseId, Guid? invoiceId, CancellationToken ct)
    {
        var stock = await _db.InventoryMovements.Where(m => m.WarehouseId == warehouseId).GroupBy(m => m.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        if (invoiceId is Guid iid)
        {
            var own = await _db.GoodsIssueLines
                .Where(l => _db.GoodsIssues.Any(g => g.Id == l.GoodsIssueId && g.SalesInvoiceId == iid && g.Status == InventoryDocStatus.Posted && g.WarehouseId == warehouseId))
                .GroupBy(l => l.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct);
            foreach (var o in own) stock[o.Key] = stock.GetValueOrDefault(o.Key) + o.Qty;
        }
        return stock;
    }

    /// <summary>Real customers (not leads) for the picker; <paramref name="keep"/> keeps the invoice's own customer listed.</summary>
    private Task<List<SelectListItem>> CustomersAsync(Guid? keep, CancellationToken ct)
        => _db.Customers.Where(c => !c.IsLead || c.Id == keep).OrderBy(c => c.FullName)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Phone != null && c.Phone != "" ? c.FullName + " — " + c.Phone : c.FullName })
            .ToListAsync(ct);

    private async Task<SalesInvoiceFormModel> FillAsync(SalesInvoiceFormModel model, CancellationToken ct)
    {
        model.Customers = await CustomersAsync(model.CustomerId, ct);
        model.Warehouses = await _db.Warehouses.Where(w => w.IsActive || w.Id == model.WarehouseId).OrderBy(w => w.Name)
            .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToListAsync(ct);

        // Products with their stock per warehouse, so the lines show what the chosen warehouse holds. When editing,
        // the invoice's own current issue is added back (saving returns it before issuing again).
        var keep = model.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value).Distinct().ToList();
        var byWh = (await _db.InventoryMovements.GroupBy(m => new { m.ProductId, m.WarehouseId })
                .Select(g => new { g.Key.ProductId, g.Key.WarehouseId, Qty = g.Sum(m => m.QuantityIn - m.QuantityOut) }).ToListAsync(ct))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.WarehouseId, x => x.Qty));
        if (model.Id != Guid.Empty)
        {
            var own = await (from l in _db.GoodsIssueLines
                             join g in _db.GoodsIssues on l.GoodsIssueId equals g.Id
                             where g.SalesInvoiceId == model.Id && g.Status == InventoryDocStatus.Posted
                             select new { l.ProductId, g.WarehouseId, l.Quantity }).ToListAsync(ct);
            foreach (var o in own)
            {
                if (!byWh.TryGetValue(o.ProductId, out var m)) byWh[o.ProductId] = m = new Dictionary<Guid, decimal>();
                m[o.WarehouseId] = m.GetValueOrDefault(o.WarehouseId) + o.Quantity;
            }
        }
        model.Products = (await ProductsAsync(_db, keep, ct))
            .Select(p => p with
            {
                Stock = byWh.TryGetValue(p.Id, out var m) ? m.Values.Sum() : 0,
                StockByWarehouse = p.Tracked ? (byWh.TryGetValue(p.Id, out var m2) ? m2 : new Dictionary<Guid, decimal>()) : null
            }).ToList();

        if (model.Id == Guid.Empty && model.Number == 0) model.Number = await NextNumberAsync(model.InvoiceDate.Year, ct);
        return model;
    }
}
