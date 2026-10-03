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

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>
/// Purchase returns (مرتجعات المشتريات) — goods sent back to the supplier, always against one purchase invoice and
/// never more of a line than was invoiced minus earlier returns.
/// <list type="bullet">
/// <item>Its stock-tracked lines leave the warehouse through an automatic, posted goods issue (reason: purchase return)
/// at the invoice cost: Dr بضاعة واردة لم تُفوتر / Cr المخزون.</item>
/// <item>The return posts Dr الموردون / Cr بضاعة واردة لم تُفوتر (stock lines — so GRNI nets to zero) / Cr مردودات
/// المشتريات (non-stock lines): a debit note that lowers what the invoice leaves owed to the supplier.</item>
/// <item>Cash the supplier pays back (optional; required for the part already paid beyond what the invoice still owes)
/// is a safe Income movement: Dr الخزنة / Cr الموردون.</item>
/// </list>
/// A return is not edited: delete it (stock, entries and refund are reversed) and record it again.
/// </summary>
[Area("Suppliers")]
[Authorize(Policy = PermissionNames.PurchaseReturnsView)]
public class PurchaseReturnsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly IInventoryEngine _inventory;
    private readonly ISafeBalanceGuard _guard;

    public PurchaseReturnsController(IApplicationDbContext db, ICurrentUserService currentUser,
        IAccountingService accounting, IInventoryEngine inventory, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _inventory = inventory;
        _guard = guard;
    }

    private static readonly ReturnKind Kind = ReturnKind.Purchase;
    private const string FormView = "~/Views/Shared/Returns/_ReturnForm.cshtml";

    private bool Can(string permission) => User.HasClaim("permission", permission);

    /// <summary>Purchase return label, e.g. PR-2026000001.</summary>
    public static string PR(int number) => "PR-" + number;

    // ---------- List ----------
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? supplierId, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var q = _db.PurchaseReturns.AsQueryable();
        if (from.HasValue) q = q.Where(r => r.ReturnDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(r => r.ReturnDate < to.Value.Date.AddDays(1));
        if (supplierId.HasValue) q = q.Where(r => r.SupplierId == supplierId.Value);
        var rets = await q.OrderByDescending(r => r.Number).ToListAsync(ct);
        var ids = rets.Select(r => r.Id).ToList();
        var invIds = rets.Select(r => r.PurchaseInvoiceId).Distinct().ToList();
        var invNumbers = await _db.PurchaseInvoices.Where(i => invIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var sums = (await _db.PurchaseReturnItems.Where(i => ids.Contains(i.PurchaseReturnId)).GroupBy(i => i.PurchaseReturnId)
                .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal), Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.Sum, x.Count));

        return View("~/Views/Shared/Returns/Index.cshtml", new ReturnIndexVm
        {
            Kind = Kind,
            From = from, To = to, PartyId = supplierId,
            CanCreate = Can(PermissionNames.PurchaseReturnsCreate),
            Parties = await SuppliersAsync(_db, ct),
            Rows = rets.Select(r => new ReturnListItem
            {
                Id = r.Id, Number = r.Number, Date = r.ReturnDate, InvoiceId = r.PurchaseInvoiceId,
                InvoiceNumber = invNumbers.TryGetValue(r.PurchaseInvoiceId, out var n) ? n : null,
                Party = supNames.GetValueOrDefault(r.SupplierId, "—"),
                Total = sums.GetValueOrDefault(r.Id).Sum, ItemCount = sums.GetValueOrDefault(r.Id).Count,
                Refund = r.RefundAmount
            }).ToList()
        });
    }

    // ---------- Create (modal) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.PurchaseReturnsCreate)]
    public async Task<IActionResult> Form(Guid? invoiceId, CancellationToken ct)
    {
        await _inventory.EnsureDefaultsAsync(ct);
        var model = new ReturnFormModel { InvoiceId = invoiceId };
        var inv = invoiceId.HasValue ? await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) : null;
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
    [Authorize(Policy = PermissionNames.PurchaseReturnsCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ReturnFormModel model, CancellationToken ct)
    {
        var inv = model.InvoiceId.HasValue ? await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == model.InvoiceId, ct) : null;
        if (inv is null)
        {
            ModelState.AddModelError(nameof(model.InvoiceId), "اختر فاتورة المشتريات.");
            return PartialView(FormView, await FillAsync(model, ct));
        }

        var invItems = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == inv.Id).ToDictionaryAsync(i => i.Id, ct);
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
                    $"الكمية المرتجعة من «{it.Name}» ({Q(Qty(l))} {it.Unit}) أكبر من المتاح للإرجاع ({Q(Math.Max(returnable, 0))} {it.Unit} — المُشترى {Q(it.Quantity)}، والمرتجع سابقًا {Q(returnedBefore.GetValueOrDefault(it.Id))}).");
        }
        if (model.ReturnDate.Date > DateTime.Today) ModelState.AddModelError(nameof(model.ReturnDate), "لا يمكن تسجيل مرتجع بتاريخ مستقبلي.");
        if (model.ReturnDate.Date < inv.InvoiceDate.Date) ModelState.AddModelError(nameof(model.ReturnDate), "لا يمكن أن يسبق تاريخ المرتجع تاريخ الفاتورة.");

        // Stock-tracked lines leave a warehouse — and can't take more than it holds.
        var tracked = lines.Where(l => invItems.TryGetValue(l.InvoiceItemId, out var it) && products.TryGetValue(it.ProductId, out var p) && p.TrackInventory).ToList();
        if (tracked.Count > 0)
        {
            if (model.WarehouseId is null || !await _db.Warehouses.AnyAsync(w => w.Id == model.WarehouseId && w.IsActive, ct))
                ModelState.AddModelError(nameof(model.WarehouseId), "اختر المخزن الذي تُصرف منه الأصناف المرتجعة للمورد.");
            else
            {
                var units = await ProductUnits.LoadAsync(_db, tracked.Select(l => invItems[l.InvoiceItemId].ProductId).Distinct().ToList(), ct);
                foreach (var l in tracked)
                {
                    var it = invItems[l.InvoiceItemId];
                    var have = (await _inventory.GetStockAsync(it.ProductId, model.WarehouseId.Value, null, ct)).Quantity;
                    var want = Qty(l) * it.UnitFactor;
                    if (want > have)
                    {
                        var u = units.Of(it.ProductId);
                        ModelState.AddModelError(string.Empty,
                            $"الكمية المرتجعة من «{it.Name}» ({u.Breakdown(want)}) أكبر من المتاح في المخزن ({u.Breakdown(Math.Max(have, 0))}).");
                    }
                }
            }
        }

        // Money: the return's value, and the refund it needs / allows on this invoice.
        decimal LineTotal(ReturnLineInput l) => invItems.TryGetValue(l.InvoiceItemId, out var it) ? Math.Round(it.Cost * Qty(l), 2) : 0m;
        var total = lines.Sum(LineTotal);
        var position = await PositionAsync(inv.Id, ct);
        var minRefund = position.MinRefund(total);
        var maxRefund = position.MaxRefund(total);
        if (model.RefundAmount < 0) ModelState.AddModelError(nameof(model.RefundAmount), "المبلغ لا يمكن أن يكون سالبًا.");
        else if (model.RefundAmount < minRefund)
            ModelState.AddModelError(nameof(model.RefundAmount),
                $"يجب استرداد {minRefund:N2} ج.م على الأقل من المورد — المتبقي له على الفاتورة ({Math.Max(position.Remaining, 0):N2} ج.م) أقل من قيمة المرتجع ({total:N2} ج.م).");
        else if (model.RefundAmount > maxRefund)
            ModelState.AddModelError(nameof(model.RefundAmount), $"لا يمكن استرداد أكثر من {maxRefund:N2} ج.م (قيمة المرتجع، وبحد أقصى ما سُدِّد على الفاتورة ولم يُسترد).");
        if (model.RefundAmount > 0 && (model.RefundSafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.RefundSafeId && s.IsActive, ct)))
            ModelState.AddModelError(nameof(model.RefundSafeId), "اختر الخزنة التي يُستلم فيها المبلغ.");

        if (!ModelState.IsValid) return PartialView(FormView, await FillAsync(model, ct));

        await _inventory.EnsureDefaultsAsync(ct);   // posting profile + مردودات المشتريات account (commits on its own)

        var ret = new PurchaseReturn
        {
            Number = await NextNumberAsync(model.ReturnDate.Year, ct),
            ReturnDate = model.ReturnDate,
            PurchaseInvoiceId = inv.Id,
            SupplierId = inv.SupplierId,
            ProjectId = inv.ProjectId,
            WarehouseId = tracked.Count > 0 ? model.WarehouseId : null,
            Notes = model.Notes,
        };
        var items = lines.Select(l =>
        {
            var it = invItems[l.InvoiceItemId];
            return new PurchaseReturnItem
            {
                PurchaseReturnId = ret.Id, InvoiceItemId = it.Id, ProductId = it.ProductId, Name = it.Name,
                Unit = it.Unit, UnitLevel = it.UnitLevel, UnitFactor = it.UnitFactor,
                Cost = it.Cost, Quantity = Qty(l), LineTotal = LineTotal(l)
            };
        }).ToList();
        _db.PurchaseReturns.Add(ret);
        _db.PurchaseReturnItems.AddRange(items);

        // Stock out at the invoice cost: Dr بضاعة واردة لم تُفوتر / Cr المخزون.
        GoodsIssue? issue = null;
        var stockItems = items.Where(i => products.TryGetValue(i.ProductId, out var p) && p.TrackInventory).ToList();
        if (stockItems.Count > 0)
        {
            try { issue = await PostIssueAsync(ret, inv, stockItems, ct); }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, $"تعذّر صرف الأصناف المرتجعة من المخزن: {ex.Message}");
                return PartialView(FormView, await FillAsync(model, ct));
            }
        }
        // Dr الموردون / Cr GRNI (by what the issue took) / Cr مردودات المشتريات.
        await _accounting.SyncPurchaseReturnAsync(ret, inv.Number, items,
            stockItems.Sum(i => i.LineTotal), issue?.Lines.Sum(l => l.TotalCost) ?? 0m, ct);

        // Cash back from the supplier: Dr الخزنة / Cr الموردون.
        if (model.RefundAmount > 0)
        {
            var desc = $"استرداد نقدية من المورد عن مرتجع المشتريات {PR(ret.Number)} على الفاتورة {PI(inv.Number)}";
            var txn = await _accounting.AddTransactionAsync(model.RefundSafeId!.Value, TxnType.Income, TxnSource.PurchaseReturnRefund,
                model.RefundAmount, model.ReturnDate, desc, projectId: inv.ProjectId, supplierId: inv.SupplierId, ct: ct);
            txn.Description = $"{desc} (إيصال استلام نقدية رقم {txn.Serial})";
            ret.RefundAmount = model.RefundAmount;
            ret.RefundSafeId = model.RefundSafeId;
            ret.RefundTransactionId = txn.Id;
            ret.RefundVoucherNo = txn.Serial;
        }

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Json(new { ok = false, error = "تعذّر الحفظ — قد يكون رقم المرتجع أو إذن الصرف مستخدمًا بالفعل. أعد المحاولة." });
        }

        var parts = new List<string>();
        if (issue is not null) parts.Add($"صُرفت الأصناف من المخزن بإذن الصرف {issue.Number}");
        if (ret.RefundAmount > 0) parts.Add($"واستُرد من المورد {ret.RefundAmount:N2} ج.م (إيصال استلام نقدية رقم {ret.RefundVoucherNo})");
        if (total - ret.RefundAmount > 0) parts.Add($"وخُصم {total - ret.RefundAmount:N2} ج.م من المستحق للمورد");
        TempData["StatusMessage"] = $"تم تسجيل مرتجع المشتريات {PR(ret.Number)} بقيمة {total:N2} ج.م على الفاتورة {PI(inv.Number)}"
                                    + (parts.Count > 0 ? " — " + string.Join("، ", parts) : "") + ".";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = ret.Id }) });
    }

    /// <summary>
    /// The return's automatic goods issue (reason: purchase return): each line leaves at its invoice value, so the GRNI
    /// the return clears nets to zero. Adds to the unit of work; throws <see cref="InvalidOperationException"/> when the
    /// inventory engine refuses.
    /// </summary>
    private async Task<GoodsIssue> PostIssueAsync(PurchaseReturn ret, PurchaseInvoice inv, List<PurchaseReturnItem> stockItems, CancellationToken ct)
    {
        var issue = new GoodsIssue
        {
            Number = await _inventory.NextNumberAsync(_db.GoodsIssues.Select(x => x.Number), _db.GoodsIssues.Local.Select(x => x.Number), ret.ReturnDate.Year, ct),
            Date = ret.ReturnDate,
            WarehouseId = ret.WarehouseId!.Value,
            Reason = IssueReason.PurchaseReturn,
            PurchaseReturnId = ret.Id,
            Notes = $"صرف تلقائي لمرتجع المشتريات {PR(ret.Number)} (الفاتورة {PI(inv.Number)})",
            Lines = stockItems.Select(i => new GoodsIssueLine
            {
                ProductId = i.ProductId, Quantity = i.Quantity * i.UnitFactor, TotalCost = i.LineTotal,
                UnitLevel = i.UnitLevel, UnitFactor = i.UnitFactor, UnitName = i.Unit
            }).ToList()
        };
        _db.GoodsIssues.Add(issue);
        await _inventory.PostIssueAsync(issue, ct);
        return issue;
    }

    // ---------- Delete (reverses stock, entries and refund) ----------
    [HttpPost]
    [Authorize(Policy = PermissionNames.PurchaseReturnsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return NotFound();

        // The refund the supplier paid leaves the safe again — «سحب على المكشوف» applies.
        var txn = ret.RefundTransactionId is Guid tid ? await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        if (txn is not null && await _guard.CheckWithdrawalAsync(txn.SafeId, txn.Amount, ct) is string overdraw)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف مرتجع المشتريات {PR(ret.Number)}: {overdraw}";
            return RedirectToAction(nameof(Details), new { id });
        }

        // Bring the returned stock back in (reversal kept as معكوس).
        var issue = await _db.GoodsIssues.FirstOrDefaultAsync(r => r.PurchaseReturnId == id && r.Status == InventoryDocStatus.Posted, ct);
        if (issue is not null)
        {
            try { await _inventory.ReverseAsync(InventorySources.GoodsIssue, issue.Id, ct); }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = $"لا يمكن حذف مرتجع المشتريات {PR(ret.Number)}: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
            issue.Status = InventoryDocStatus.Reversed;
        }
        await _accounting.RemoveObligationAsync(AccountingSources.PurchaseReturn, id, ct);   // the debit note entry
        if (txn is not null) await _accounting.RemoveTransactionAsync(txn, ct);
        _db.PurchaseReturnItems.RemoveRange(await _db.PurchaseReturnItems.Where(i => i.PurchaseReturnId == id).ToListAsync(ct));
        _db.PurchaseReturns.Remove(ret);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف مرتجع المشتريات {PR(ret.Number)}"
            + (issue is not null ? $" وعكس إذن الصرف {issue.Number}" : "")
            + (ret.RefundAmount > 0 ? $" وإلغاء الاسترداد ({ret.RefundAmount:N2} ج.م خرجت من الخزنة)" : "") + ".";
        return RedirectToAction("Details", "PurchaseInvoices", new { id = ret.PurchaseInvoiceId });
    }

/// <summary>
    /// Deletes the return's cash refund only — the return stays, and its value is credited to the supplier's
    /// balance instead. The refund movement and its journal entry are removed (the money leaves the safe again — the overdraft rule applies).
    /// Allowed only while the invoice still owes at least the refund, so it can't end up overpaid.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermissionNames.PurchaseReturnsDeleteRefund)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRefund(Guid id, CancellationToken ct)
    {
        var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return NotFound();
        if (ret.RefundAmount <= 0) { TempData["ErrorMessage"] = "لا يوجد مبلغ نقدي على هذا المرتجع."; return RedirectToAction(nameof(Details), new { id }); }
        var position = await PositionAsync(ret.PurchaseInvoiceId, ct);
        if (position.Remaining - ret.RefundAmount < 0)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف المبلغ النقدي ({ret.RefundAmount:N2} ج.م) للمرتجع {PR(ret.Number)}: المتبقي على الفاتورة ({Math.Max(position.Remaining, 0):N2} ج.م) لا يكفي لخصمه من الرصيد — احذف المرتجع بالكامل بدلًا من ذلك.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var txn = ret.RefundTransactionId is Guid tid ? await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct) : null;
        if (txn is not null && await _guard.CheckWithdrawalAsync(txn.SafeId, txn.Amount, ct) is string overdraw)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف المبلغ النقدي للمرتجع {PR(ret.Number)}: {overdraw}";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (txn is not null) await _accounting.RemoveTransactionAsync(txn, ct);   // the refund movement + its journal entry
        var amount = ret.RefundAmount;
        var voucher = ret.RefundVoucherNo;
        ret.RefundAmount = 0; ret.RefundSafeId = null; ret.RefundTransactionId = null; ret.RefundVoucherNo = null;
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المبلغ النقدي ({amount:N2} ج.م، إيصال استلام نقدية رقم {voucher}) من المرتجع {PR(ret.Number)} — أصبحت قيمته مخصومة من المستحق للمورد.";
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

    /// <summary>The refund receipt IS the unified cash voucher (إيصال استلام نقدية) of the refund's income movement.</summary>
    [HttpGet]
    public async Task<IActionResult> RefundVoucher(Guid id, CancellationToken ct)
    {
        var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret?.RefundTransactionId is not Guid tid) return NotFound();
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == tid, ct);
        if (txn is null) return NotFound();
        var invNo = await _db.PurchaseInvoices.Where(i => i.Id == ret.PurchaseInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct);
        ViewBag.PartyLabel = "المورد";
        ViewBag.Party = await _db.Suppliers.Where(s => s.Id == ret.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.About = $"استرداد نقدية عن مرتجع المشتريات {PR(ret.Number)}" + (invNo.HasValue ? $" (الفاتورة {PI(invNo.Value)})" : "");
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    // ---------- helpers ----------
    private async Task<ReturnDetailsVm?> LoadAsync(Guid id, CancellationToken ct)
    {
        var ret = await _db.PurchaseReturns.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (ret is null) return null;
        var items = await _db.PurchaseReturnItems.Where(i => i.PurchaseReturnId == id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == ret.SupplierId, ct);
        var docs = await _db.GoodsIssues.Where(r => r.PurchaseReturnId == id).OrderByDescending(r => r.Number)
            .Select(r => new { r.Id, r.Number, r.Date, r.Status, Cost = r.Lines.Sum(l => (decimal?)l.TotalCost) ?? 0m }).ToListAsync(ct);
        return new ReturnDetailsVm
        {
            Kind = Kind,
            Id = ret.Id, Number = ret.Number, Date = ret.ReturnDate, Notes = ret.Notes,
            InvoiceId = ret.PurchaseInvoiceId,
            InvoiceNumber = await _db.PurchaseInvoices.Where(i => i.Id == ret.PurchaseInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct),
            PartyId = ret.SupplierId, Party = supplier?.Name ?? "—", PartyPhone = supplier?.Phone,
            Warehouse = ret.WarehouseId.HasValue ? await _db.Warehouses.Where(w => w.Id == ret.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) : null,
            Lines = items.Select(i => new ReturnDetailLine(i.Name, i.Unit, i.Cost, i.Quantity, i.LineTotal)).ToList(),
            Refund = ret.RefundAmount, RefundVoucherNo = ret.RefundVoucherNo,
            RefundSafe = ret.RefundSafeId.HasValue ? await _db.Safes.Where(s => s.Id == ret.RefundSafeId).Select(s => s.Name).FirstOrDefaultAsync(ct) : null,
            StockDocs = docs.Select(d => new ReturnStockDocRef(d.Id, d.Number, d.Date, d.Status, d.Cost)).ToList(),
            Invoice = await PositionAsync(ret.PurchaseInvoiceId, ct),
            CanDelete = Can(PermissionNames.PurchaseReturnsDelete),
            CanDeleteRefund = Can(PermissionNames.PurchaseReturnsDeleteRefund),
        };
    }

    /// <summary>The invoice's money position after all its saved returns.</summary>
    private async Task<ReturnInvoicePosition> PositionAsync(Guid invoiceId, CancellationToken ct)
    {
        var total = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == invoiceId).SumAsync(i => (decimal?)i.LineTotal, ct) ?? 0m;
        var paid = await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId == invoiceId).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var r = await InvoiceReturns.ForPurchaseInvoiceAsync(_db, invoiceId, ct);
        return new ReturnInvoicePosition(total, r.Returned, paid, r.Refunded);
    }

    /// <summary>Quantity already returned per invoice line (in the line's own unit).</summary>
    private async Task<Dictionary<Guid, decimal>> ReturnedQtyAsync(Guid invoiceId, CancellationToken ct)
        => (await (from i in _db.PurchaseReturnItems
                   join r in _db.PurchaseReturns on i.PurchaseReturnId equals r.Id
                   where r.PurchaseInvoiceId == invoiceId
                   group i by i.InvoiceItemId into g
                   select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Qty);

    private Task<int> NextNumberAsync(int year, CancellationToken ct)
        => _inventory.NextNumberAsync(_db.PurchaseReturns.Select(x => x.Number), _db.PurchaseReturns.Local.Select(x => x.Number), year, ct);

    /// <summary>A line's returned quantity — an empty line counts as 0 (not returned).</summary>
    private static decimal Qty(ReturnLineInput l) => l.Quantity ?? 0m;

    private static string Q(decimal v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<ReturnFormModel> FillAsync(ReturnFormModel model, CancellationToken ct)
    {
        model.Kind = Kind;
        model.Number = await NextNumberAsync(model.ReturnDate.Year, ct);

        // Invoices that still have something to return.
        var invoices = await _db.PurchaseInvoices.OrderByDescending(i => i.Number)
            .Select(i => new { i.Id, i.Number, i.InvoiceDate, i.SupplierId }).ToListAsync(ct);
        var bought = await _db.PurchaseInvoiceItems.GroupBy(i => i.PurchaseInvoiceId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var returned = await (from i in _db.PurchaseReturnItems
                              join r in _db.PurchaseReturns on i.PurchaseReturnId equals r.Id
                              group i by r.PurchaseInvoiceId into g
                              select new { g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        model.Invoices = invoices
            .Where(i => i.Id == model.InvoiceId || bought.GetValueOrDefault(i.Id) > returned.GetValueOrDefault(i.Id))
            .Select(i => new SelectListItem
            {
                Value = i.Id.ToString(),
                Text = $"{PI(i.Number)} — {supNames.GetValueOrDefault(i.SupplierId, "—")} — {i.InvoiceDate:yyyy/MM/dd}",
                Selected = i.Id == model.InvoiceId
            }).ToList();

        model.Warehouses = await _db.Warehouses.Where(w => w.IsActive || w.Id == model.WarehouseId).OrderBy(w => w.Name)
            .Select(w => new SelectListItem { Value = w.Id.ToString(), Text = w.Name }).ToListAsync(ct);
        model.Safes = await SafesAsync(_db, ct);

        var inv = model.InvoiceId.HasValue ? await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == model.InvoiceId, ct) : null;
        if (inv is not null)
        {
            model.InvoiceLabel = PI(inv.Number);
            model.InvoiceDate = inv.InvoiceDate;
            model.Party = supNames.GetValueOrDefault(inv.SupplierId, "—");
            var items = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == inv.Id).OrderBy(i => i.CreatedAt).ToListAsync(ct);
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var trackedIds = (await _db.Products.Where(p => productIds.Contains(p.Id) && p.TrackInventory).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
            var before = await ReturnedQtyAsync(inv.Id, ct);
            model.Rows = items.Select(i => new ReturnLineVm(i.Id, i.Name, i.Unit, i.Cost, i.Quantity, before.GetValueOrDefault(i.Id), trackedIds.Contains(i.ProductId))).ToList();
            model.Position = await PositionAsync(inv.Id, ct);
        }
        return model;
    }
}
