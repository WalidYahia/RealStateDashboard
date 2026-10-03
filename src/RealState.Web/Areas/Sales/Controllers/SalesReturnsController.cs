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
using RealState.Web.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Sales.Controllers;

/// <summary>
/// Sales returns (مرتجعات المبيعات) — goods a customer brings back, always against one product sales invoice and
/// never more of a line than was invoiced minus earlier returns.
/// <list type="bullet">
/// <item>The return posts Dr مردودات المبيعات / Cr العملاء — a credit note that lowers what the invoice leaves owed.</item>
/// <item>Its stock-tracked lines come back into a warehouse through an automatic, posted goods receipt (reason: sales
/// return) at their original cost of sale, taken from the invoice's goods issue: Dr المخزون / Cr تكلفة المبيعات.</item>
/// <item>Cash refunded to the customer (optional; required for the part the invoice no longer owes) is a safe Expense
/// movement: Dr العملاء / Cr الخزنة.</item>
/// </list>
/// A return is not edited: delete it (stock, entries and refund are reversed) and record it again.
/// </summary>
[Area("Sales")]
[Authorize(Policy = PermissionNames.SalesReturnsView)]
public class SalesReturnsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly IInventoryEngine _inventory;
    private readonly ISafeBalanceGuard _guard;

    public SalesReturnsController(IApplicationDbContext db, ICurrentUserService currentUser,
        IAccountingService accounting, IInventoryEngine inventory, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _inventory = inventory;
        _guard = guard;
    }

    private static readonly ReturnKind Kind = ReturnKind.Sales;
    private const string FormView = "~/Views/Shared/Returns/_ReturnForm.cshtml";

    private bool Can(string permission) => User.HasClaim("permission", permission);

    /// <summary>Sales return label, e.g. SR-2026000001.</summary>
    public static string SR(int number) => "SR-" + number;
    private static string SI(int number) => SalesInvoicesController.SI(number);

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? customerId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var q = _db.ProductSalesReturns.AsQueryable();
        if (from.HasValue) q = q.Where(r => r.ReturnDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(r => r.ReturnDate < to.Value.Date.AddDays(1));
        if (customerId.HasValue) q = q.Where(r => r.CustomerId == customerId.Value);
        var rets = await q.OrderByDescending(r => r.Number).ToListAsync(ct);
        var ids = rets.Select(r => r.Id).ToList();
        var invIds = rets.Select(r => r.SalesInvoiceId).Distinct().ToList();
        var custIds = rets.Select(r => r.CustomerId).Distinct().ToList();
        var invNumbers = await _db.ProductSalesInvoices.Where(i => invIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var custNames = await _db.Customers.Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var sums = (await _db.ProductSalesReturnItems.Where(i => ids.Contains(i.SalesReturnId)).GroupBy(i => i.SalesReturnId)
                .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));

        return View("~/Views/Shared/Returns/Index.cshtml", new ReturnIndexVm
        {
            Kind = Kind,
            From = from, To = to, PartyId = customerId,
            CanCreate = Can(PermissionNames.SalesReturnsCreate),
            Parties = await _db.Customers.Where(c => !c.IsLead).OrderBy(c => c.FullName)
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.FullName }).ToListAsync(ct),
            Rows = rets.Select(r => new ReturnListItem
            {
                Id = r.Id, Number = r.Number, Date = r.ReturnDate, InvoiceId = r.SalesInvoiceId,
                InvoiceNumber = invNumbers.TryGetValue(r.SalesInvoiceId, out var n) ? n : null,
                Party = custNames.GetValueOrDefault(r.CustomerId, "—"),
                Total = sums.GetValueOrDefault(r.Id).Sum, ItemCount = sums.GetValueOrDefault(r.Id).Count,
                Refund = r.RefundAmount
            }).ToList()
        });
    }

    // ---------- Create (modal) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesReturnsCreate)]
    public async Task<IActionResult> Form(Guid? invoiceId, CancellationToken ct)
    {
        await _inventory.EnsureDefaultsAsync(ct);
        var model = new ReturnFormModel { InvoiceId = invoiceId };
        var inv = invoiceId.HasValue ? await _db.ProductSalesInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) : null;
        if (inv is not null)
        {
            model.WarehouseId = inv.WarehouseId;
            if (model.WarehouseId is Guid w && !await _db.Warehouses.AnyAsync(x => x.Id == w && x.IsActive, ct))
                model.WarehouseId = await _db.Warehouses.Where(x => x.IsDefault && x.IsActive).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        }
        else model.InvoiceId = null;
        // The refund follows the quantities as they're typed (wwwroot/js/return-doc.js).
        return PartialView(FormView, await FillAsync(model, ct));
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesReturnsCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ReturnFormModel model, CancellationToken ct)
    {
        var inv = model.InvoiceId.HasValue ? await _db.ProductSalesInvoices.FirstOrDefaultAsync(i => i.Id == model.InvoiceId, ct) : null;
        if (inv is null)
        {
            ModelState.AddModelError(nameof(model.InvoiceId), "اختر فاتورة المبيعات.");
            return PartialView(FormView, await FillAsync(model, ct));
        }

        var invItems = await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == inv.Id).ToDictionaryAsync(i => i.Id, ct);
        var returnedBefore = await ReturnedQtyAsync(inv.Id, ct);
        var lines = (model.Lines ?? new()).Where(l => Qty(l) > 0).ToList();
        var productIds = lines.Where(l => invItems.ContainsKey(l.InvoiceItemId)).Select(l => invItems[l.InvoiceItemId].ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        if (lines.Count == 0) ModelState.AddModelError(string.Empty, "أدخل كمية مرتجعة لصنف واحد على الأقل.");
        foreach (var l in lines)
        {
            if (!invItems.TryGetValue(l.InvoiceItemId, out var it)) { ModelState.AddModelError(string.Empty, "أحد الأسطر لا يخص هذه الفاتورة."); continue; }
            var returnable = it.Quantity - returnedBefore.GetValueOrDefault(it.Id);
            if (Qty(l) > returnable)
                ModelState.AddModelError(string.Empty,
                    $"الكمية المرتجعة من «{it.Name}» ({Q(Qty(l))} {it.Unit}) أكبر من المتاح للإرجاع ({Q(Math.Max(returnable, 0))} {it.Unit} — المُباع {Q(it.Quantity)}، والمرتجع سابقًا {Q(returnedBefore.GetValueOrDefault(it.Id))}).");
        }
        if (model.ReturnDate.Date > DateTime.Today) ModelState.AddModelError(nameof(model.ReturnDate), "لا يمكن تسجيل مرتجع بتاريخ مستقبلي.");
        if (model.ReturnDate.Date < inv.InvoiceDate.Date) ModelState.AddModelError(nameof(model.ReturnDate), "لا يمكن أن يسبق تاريخ المرتجع تاريخ الفاتورة.");

        var tracked = lines.Where(l => invItems.TryGetValue(l.InvoiceItemId, out var it) && products.TryGetValue(it.ProductId, out var p) && p.TrackInventory).ToList();
        if (tracked.Count > 0 && (model.WarehouseId is null || !await _db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId && w.IsActive, ct)))
            ModelState.AddModelError(nameof(model.WarehouseId), "اختر المخزن الذي تعود إليه الأصناف المخزنية.");

        // Money: the return's value, and the refund it needs / allows on this invoice.
        decimal LineTotal(ReturnLineInput l) => invItems.TryGetValue(l.InvoiceItemId, out var it) ? Math.Round(it.Price * Qty(l), 2) : 0m;
        var total = lines.Sum(LineTotal);
        var position = await PositionAsync(inv.Id, ct);
        var minRefund = position.MinRefund(total);
        var maxRefund = position.MaxRefund(total);
        if (model.RefundAmount < 0) ModelState.AddModelError(nameof(model.RefundAmount), "المبلغ لا يمكن أن يكون سالبًا.");
        else if (model.RefundAmount < minRefund)
            ModelState.AddModelError(nameof(model.RefundAmount),
                $"يجب رد {minRefund:N2} ج.م على الأقل نقدًا — المتبقي على الفاتورة ({Math.Max(position.Remaining, 0):N2} ج.م) أقل من قيمة المرتجع ({total:N2} ج.م).");
        else if (model.RefundAmount > maxRefund)
            ModelState.AddModelError(nameof(model.RefundAmount), $"لا يمكن رد أكثر من {maxRefund:N2} ج.م (قيمة المرتجع، وبحد أقصى ما حُصِّل على الفاتورة ولم يُرد).");
        if (model.RefundAmount > 0)
        {
            if (model.RefundSafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.RefundSafeId && s.IsActive, ct))
                ModelState.AddModelError(nameof(model.RefundSafeId), "اختر الخزنة التي يُصرف منها المبلغ.");
            else if (await _guard.CheckWithdrawalAsync(model.RefundSafeId.Value, model.RefundAmount, ct) is string overdraw)
                ModelState.AddModelError(nameof(model.RefundSafeId), overdraw);
        }

        if (!ModelState.IsValid) return PartialView(FormView, await FillAsync(model, ct));

        await _inventory.EnsureDefaultsAsync(ct);   // posting profile + مردودات المبيعات account (commits on its own)

        var ret = new ProductSalesReturn
        {
            Number = await NextNumberAsync(model.ReturnDate.Year, ct),
            ReturnDate = model.ReturnDate,
            SalesInvoiceId = inv.Id,
            CustomerId = inv.CustomerId,
            WarehouseId = tracked.Count > 0 ? model.WarehouseId : null,
            Notes = model.Notes,
        };
        var items = lines.Select(l =>
        {
            var it = invItems[l.InvoiceItemId];
            return new ProductSalesReturnItem
            {
                SalesReturnId = ret.Id, InvoiceItemId = it.Id, ProductId = it.ProductId, Name = it.Name,
                Unit = it.Unit, UnitLevel = it.UnitLevel, UnitFactor = it.UnitFactor,
                Price = it.Price, Quantity = Qty(l), LineTotal = LineTotal(l)
            };
        }).ToList();
        _db.ProductSalesReturns.Add(ret);
        _db.ProductSalesReturnItems.AddRange(items);
        await _accounting.SyncSalesReturnAsync(ret, inv.Number, items, ct);   // Dr مردودات المبيعات  Cr العملاء

        // Stock back in at the original cost of sale: Dr المخزون / Cr تكلفة المبيعات.
        GoodsReceipt? receipt = null;
        var stockItems = items.Where(i => products.TryGetValue(i.ProductId, out var p) && p.TrackInventory).ToList();
        if (stockItems.Count > 0)
        {
            try { receipt = await PostReceiptAsync(ret, inv, stockItems, ct); }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, $"تعذّر إرجاع الأصناف للمخزن: {ex.Message}");
                return PartialView(FormView, await FillAsync(model, ct));
            }
        }

        // Cash back to the customer: Dr العملاء / Cr الخزنة.
        if (model.RefundAmount > 0)
        {
            var desc = $"رد نقدية للعميل عن مرتجع المبيعات {SR(ret.Number)} على الفاتورة {SI(inv.Number)}";
            var txn = await _accounting.AddTransactionAsync(model.RefundSafeId!.Value, TxnType.Expense, TxnSource.SalesReturnRefund,
                model.RefundAmount, model.ReturnDate, desc, customerId: inv.CustomerId, ct: ct);
            txn.Description = $"{desc} (إيصال صرف نقدية رقم {txn.Serial})";
            ret.RefundAmount = model.RefundAmount;
            ret.RefundSafeId = model.RefundSafeId;
            ret.RefundTransactionId = txn.Id;
            ret.RefundVoucherNo = txn.Serial;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Json(new { ok = false, error = "تعذّر الحفظ — قد يكون رقم المرتجع أو إذن الاستلام مستخدمًا بالفعل. أعد المحاولة." });
        }

        var parts = new List<string>();
        if (receipt is not null) parts.Add($"أُعيدت الأصناف للمخزن بإذن الاستلام {receipt.Number}");
        if (ret.RefundAmount > 0) parts.Add($"ورُدّ للعميل {ret.RefundAmount:N2} ج.م (إيصال صرف نقدية رقم {ret.RefundVoucherNo})");
        if (total - ret.RefundAmount > 0) parts.Add($"وخُصم {total - ret.RefundAmount:N2} ج.م من رصيد العميل");
        TempData["StatusMessage"] = $"تم تسجيل مرتجع المبيعات {SR(ret.Number)} بقيمة {total:N2} ج.م على الفاتورة {SI(inv.Number)}"
                                    + (parts.Count > 0 ? " — " + string.Join("، ", parts) : "") + ".";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = ret.Id }) });
    }

    /// <summary>
    /// The return's automatic goods receipt (reason: sales return), posted at the cost each product left the warehouse
    /// at when the invoice sold it (its posted goods issue); a product the invoice issue doesn't cover falls back to the
    /// current average cost. Adds to the unit of work; throws <see cref="InvalidOperationException"/> when it can't.
    /// </summary>
    private async Task<GoodsReceipt> PostReceiptAsync(ProductSalesReturn ret, ProductSalesInvoice inv, List<ProductSalesReturnItem> stockItems, CancellationToken ct)
    {
        var soldCost = (await (from l in _db.GoodsIssueLines
                               join g in _db.GoodsIssues on l.GoodsIssueId equals g.Id
                               where g.SalesInvoiceId == inv.Id && g.Status == InventoryDocStatus.Posted && l.Quantity > 0
                               select new { l.ProductId, l.Quantity, l.TotalCost }).ToListAsync(ct))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalCost) / g.Sum(x => x.Quantity));   // per smallest unit

        var lines = new List<GoodsReceiptLine>();
        foreach (var i in stockItems)
        {
            var qty = i.Quantity * i.UnitFactor;   // smallest unit
            var unitCost = soldCost.GetValueOrDefault(i.ProductId);
            if (unitCost <= 0) unitCost = (await _inventory.GetStockAsync(i.ProductId, ret.WarehouseId!.Value, null, ct)).AverageCost;
            if (unitCost <= 0)
                throw new InvalidOperationException($"لا توجد تكلفة معروفة للصنف «{i.Name}» لإعادته للمخزن.");
            lines.Add(new GoodsReceiptLine
            {
                ProductId = i.ProductId, Quantity = qty, UnitCost = Math.Round(unitCost, 6), TotalCost = Math.Round(unitCost * qty, 2),
                UnitLevel = i.UnitLevel, UnitFactor = i.UnitFactor, UnitName = i.Unit
            });
        }
        var receipt = new GoodsReceipt
        {
            Number = await _inventory.NextNumberAsync(_db.GoodsReceipts.Select(x => x.Number), _db.GoodsReceipts.Local.Select(x => x.Number), ret.ReturnDate.Year, ct),
            Date = ret.ReturnDate,
            WarehouseId = ret.WarehouseId!.Value,
            Reason = ReceiptReason.SalesReturn,
            SalesReturnId = ret.Id,
            Notes = $"استلام تلقائي لمرتجع المبيعات {SR(ret.Number)} (الفاتورة {SI(inv.Number)})",
            Lines = lines
        };
        _db.GoodsReceipts.Add(receipt);
        await _inventory.PostReceiptAsync(receipt, ct);
        return receipt;
    }

    // ---------- Delete (reverses stock, entries and refund) ----------
    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesReturnsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ret = await _db.ProductSalesReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return NotFound();

        // Take the returned stock back out (reversal kept as معكوس) — refused if it has been sold / moved since.
        var receipt = await _db.GoodsReceipts.FirstOrDefaultAsync(r => r.SalesReturnId == id && r.Status == InventoryDocStatus.Posted, ct);
        if (receipt is not null)
        {
            try { await _inventory.ReverseAsync(InventorySources.GoodsReceipt, receipt.Id, ct); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"لا يمكن حذف مرتجع المبيعات {SR(ret.Number)}: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
            receipt.Status = InventoryDocStatus.Reversed;
        }
        await _accounting.RemoveObligationAsync(AccountingSources.SalesReturn, id, ct);   // the credit note entry
        if (ret.RefundTransactionId is Guid tid && await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) is { } txn)
            await _accounting.RemoveTransactionAsync(txn, ct);   // the refund goes back into the safe
        _db.ProductSalesReturnItems.RemoveRange(await _db.ProductSalesReturnItems.Where(i => i.SalesReturnId == id).ToListAsync(ct));
        _db.ProductSalesReturns.Remove(ret);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف مرتجع المبيعات {SR(ret.Number)}"
            + (receipt is not null ? $" وعكس إذن الاستلام {receipt.Number}" : "")
            + (ret.RefundAmount > 0 ? $" وإلغاء رد النقدية ({ret.RefundAmount:N2} ج.م عادت إلى الخزنة)" : "") + ".";
        return RedirectToAction("Details", "SalesInvoices", new { id = ret.SalesInvoiceId });
    }

/// <summary>
    /// Deletes the return's cash refund only — the return stays, and its value is credited to the customer's
    /// balance instead. The refund movement and its journal entry are removed (the money goes back into the safe).
    /// Allowed only while the invoice still owes at least the refund, so it can't end up overpaid.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesReturnsDeleteRefund)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRefund(Guid id, CancellationToken ct)
    {
        var ret = await _db.ProductSalesReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return NotFound();
        if (ret.RefundAmount <= 0) { TempData["ErrorMessage"] = "لا يوجد مبلغ نقدي على هذا المرتجع."; return RedirectToAction(nameof(Details), new { id }); }
        var position = await PositionAsync(ret.SalesInvoiceId, ct);
        if (position.Remaining - ret.RefundAmount < 0)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف المبلغ النقدي ({ret.RefundAmount:N2} ج.م) للمرتجع {SR(ret.Number)}: المتبقي على الفاتورة ({Math.Max(position.Remaining, 0):N2} ج.م) لا يكفي لخصمه من الرصيد — احذف المرتجع بالكامل بدلًا من ذلك.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var txn = ret.RefundTransactionId is Guid tid ? await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        if (txn is not null) await _accounting.RemoveTransactionAsync(txn, ct);   // the refund movement + its journal entry
        var amount = ret.RefundAmount;
        var voucher = ret.RefundVoucherNo;
        ret.RefundAmount = 0; ret.RefundSafeId = null; ret.RefundTransactionId = null; ret.RefundVoucherNo = null;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المبلغ النقدي ({amount:N2} ج.م، إيصال صرف نقدية رقم {voucher}) من المرتجع {SR(ret.Number)} — أصبحت قيمته مخصومة من رصيد العميل.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- View / print ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        return vm is null ? NotFound() : View("~/Views/Shared/Returns/Details.cshtml", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid id, CancellationToken ct)
    {
        var vm = await LoadAsync(id, ct);
        if (vm is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Views/Shared/Returns/Print.cshtml", vm);
    }

    /// <summary>The refund voucher IS the unified cash voucher (إيصال صرف نقدية) of the refund's expense movement.</summary>
    [HttpGet]
    public async Task<IActionResult> RefundVoucher(Guid id, CancellationToken ct)
    {
        var ret = await _db.ProductSalesReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret?.RefundTransactionId is not Guid tid) return NotFound();
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct);
        if (txn is null) return NotFound();
        var invNo = await _db.ProductSalesInvoices.Where(i => i.Id == ret.SalesInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct);
        ViewBag.PartyLabel = "العميل";
        ViewBag.Party = await _db.Customers.Where(c => c.Id == ret.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct);
        ViewBag.About = $"رد نقدية عن مرتجع المبيعات {SR(ret.Number)}" + (invNo.HasValue ? $" (الفاتورة {SI(invNo.Value)})" : "");
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    // ---------- helpers ----------
    private async Task<ReturnDetailsVm?> LoadAsync(Guid id, CancellationToken ct)
    {
        var ret = await _db.ProductSalesReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return null;
        var items = await _db.ProductSalesReturnItems.Where(i => i.SalesReturnId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == ret.CustomerId, ct);
        var docs = await _db.GoodsReceipts.Where(r => r.SalesReturnId == id).OrderByDescending(r => r.Number)
            .Select(r => new { r.Id, r.Number, r.Date, r.Status, Cost = r.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m }).ToListAsync(ct);
        return new ReturnDetailsVm
        {
            Kind = Kind,
            Id = ret.Id, Number = ret.Number, Date = ret.ReturnDate, Notes = ret.Notes,
            InvoiceId = ret.SalesInvoiceId,
            InvoiceNumber = await _db.ProductSalesInvoices.Where(i => i.Id == ret.SalesInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct),
            PartyId = ret.CustomerId, Party = customer?.FullName ?? "—", PartyPhone = customer?.Phone,
            Warehouse = ret.WarehouseId.HasValue ? await _db.Warehouses.Where(w => w.Id == ret.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) : null,
            Lines = items.Select(i => new ReturnDetailLine(i.Name, i.Unit, i.Price, i.Quantity, i.LineTotal)).ToList(),
            Refund = ret.RefundAmount, RefundVoucherNo = ret.RefundVoucherNo,
            RefundSafe = ret.RefundSafeId.HasValue ? await _db.Safes.Where(s => s.Id == ret.RefundSafeId).Select(s => s.Name).FirstOrDefaultAsync(ct) : null,
            StockDocs = docs.Select(d => new ReturnStockDocRef(d.Id, d.Number, d.Date, d.Status, d.Cost)).ToList(),
            Invoice = await PositionAsync(ret.SalesInvoiceId, ct),
            CanDelete = Can(PermissionNames.SalesReturnsDelete),
            CanDeleteRefund = Can(PermissionNames.SalesReturnsDeleteRefund),
        };
    }

    /// <summary>The invoice's money position after all its saved returns.</summary>
    private async Task<ReturnInvoicePosition> PositionAsync(Guid invoiceId, CancellationToken ct)
    {
        var total = await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == invoiceId).SumAsync(i => (decimal?)i.LineTotal, ct) ?? 0m;
        var paid = await _db.SalesInvoiceCollections.Where(c => c.SalesInvoiceId == invoiceId).SumAsync(c => (decimal?)c.Amount, ct) ?? 0m;
        var r = await InvoiceReturns.ForSalesInvoiceAsync(_db, invoiceId, ct);
        return new ReturnInvoicePosition(total, r.Returned, paid, r.Refunded);
    }

    /// <summary>Quantity already returned per invoice line (in the line's own unit).</summary>
    private async Task<Dictionary<Guid, decimal>> ReturnedQtyAsync(Guid invoiceId, CancellationToken ct)
        => (await (from i in _db.ProductSalesReturnItems
                   join r in _db.ProductSalesReturns on i.SalesReturnId equals r.Id
                   where r.SalesInvoiceId == invoiceId
                   group i by i.InvoiceItemId into g
                   select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Qty);

    private Task<int> NextNumberAsync(int year, CancellationToken ct)
        => _inventory.NextNumberAsync(_db.ProductSalesReturns.Select(x => x.Number), _db.ProductSalesReturns.Local.Select(x => x.Number), year, ct);

    /// <summary>A line's returned quantity — an empty line counts as 0 (not returned).</summary>
    private static decimal Qty(ReturnLineInput l) => l.Quantity ?? 0m;

    private static string Q(decimal v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<ReturnFormModel> FillAsync(ReturnFormModel model, CancellationToken ct)
    {
        model.Kind = Kind;
        model.Number = await NextNumberAsync(model.ReturnDate.Year, ct);

        // Invoices that still have something to return.
        var invoices = await _db.ProductSalesInvoices.OrderByDescending(i => i.Number)
            .Select(i => new { i.Id, i.Number, i.InvoiceDate, i.CustomerId }).ToListAsync(ct);
        var sold = await _db.ProductSalesInvoiceItems.GroupBy(i => i.SalesInvoiceId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var returned = await (from i in _db.ProductSalesReturnItems
                              join r in _db.ProductSalesReturns on i.SalesReturnId equals r.Id
                              group i by r.SalesInvoiceId into g
                              select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var custIds = invoices.Select(i => i.CustomerId).Distinct().ToList();
        var custNames = await _db.Customers.Where(c => custIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        model.Invoices = invoices
            .Where(i => i.Id == model.InvoiceId || sold.GetValueOrDefault(i.Id) > returned.GetValueOrDefault(i.Id))
            .Select(i => new SelectListItem
            {
                Value = i.Id.ToString(),
                Text = $"{SI(i.Number)} — {custNames.GetValueOrDefault(i.CustomerId, "—")} — {i.InvoiceDate:yyyy/MM/dd}",
                Selected = i.Id == model.InvoiceId
            }).ToList();

        model.Warehouses = await _db.Warehouses.Where(w => w.IsActive || w.Id == model.WarehouseId).OrderBy(w => w.Name)
            .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToListAsync(ct);
        model.Safes = await SafesAsync(_db, ct);

        var inv = model.InvoiceId.HasValue ? await _db.ProductSalesInvoices.FirstOrDefaultAsync(i => i.Id == model.InvoiceId, ct) : null;
        if (inv is not null)
        {
            model.InvoiceLabel = SI(inv.Number);
            model.InvoiceDate = inv.InvoiceDate;
            model.Party = custNames.GetValueOrDefault(inv.CustomerId)
                          ?? await _db.Customers.Where(c => c.Id == inv.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct);
            var items = await _db.ProductSalesInvoiceItems.Where(i => i.SalesInvoiceId == inv.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var trackedIds = (await _db.Products.Where(p => productIds.Contains(p.Id) && p.TrackInventory).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
            var before = await ReturnedQtyAsync(inv.Id, ct);
            model.Rows = items.Select(i => new ReturnLineVm(i.Id, i.Name, i.Unit, i.Price, i.Quantity, before.GetValueOrDefault(i.Id), trackedIds.Contains(i.ProductId))).ToList();
            model.Position = await PositionAsync(inv.Id, ct);
        }
        return model;
    }
}
