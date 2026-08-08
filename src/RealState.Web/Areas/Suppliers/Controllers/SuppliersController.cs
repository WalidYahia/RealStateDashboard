using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;

namespace RealState.Web.Areas.Suppliers.Controllers;

[Area("Suppliers")]
[Authorize(Policy = PermissionNames.SuppliersView)]
public class SuppliersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SuppliersController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var suppliers = await _db.Suppliers.OrderBy(s => s.Name).ToListAsync(ct);
        return View(suppliers);
    }

    // Add (id null) or edit (id set) — shown in a modal popup.
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();
        if (id is null) return PartialView("_SupplierForm", new SupplierFormModel());
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        return PartialView("_SupplierForm", new SupplierFormModel
        {
            Id = s.Id,
            Name = s.Name,
            Phone = s.Phone ?? "",
            Email = s.Email,
            Notes = s.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SupplierFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();
        if (await _db.Suppliers.AnyAsync(s => s.Id != model.Id && s.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return PartialView("_SupplierForm", model);

        if (model.Id == Guid.Empty)
        {
            _db.Suppliers.Add(new Supplier { Name = model.Name, Phone = model.Phone, Email = model.Email, Notes = model.Notes });
        }
        else
        {
            var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name; s.Phone = model.Phone; s.Email = model.Email; s.Notes = model.Notes;
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ المورد «{model.Name}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        if (await _db.SupplierOrders.AnyAsync(o => o.SupplierId == id, ct) ||
            await _db.SupplierPayments.AnyAsync(p => p.SupplierId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف مورد لديه أوامر توريد أو مدفوعات.";
            return RedirectToAction(nameof(Index));
        }
        _db.Suppliers.Remove(s);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المورد «{s.Name}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Account statement (كشف الحساب) ----------
    public async Task<IActionResult> Details(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        return View(await BuildStatementAsync(supplier, from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintStatement(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintStatement", await BuildStatementAsync(supplier, from, to, ct));
    }

    private async Task<SupplierStatementVm> BuildStatementAsync(Supplier supplier, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var orders = await _db.SupplierOrders.Where(o => o.SupplierId == supplier.Id).ToListAsync(ct);
        var itemsByOrder = (await _db.SupplierOrderItems.Where(i => orders.Select(o => o.Id).Contains(i.SupplierOrderId)).ToListAsync(ct))
            .GroupBy(i => i.SupplierOrderId).ToDictionary(g => g.Key, g => g.ToList());
        var payments = await _db.SupplierPayments.Where(p => p.SupplierId == supplier.Id).ToListAsync(ct);

        // Build one ledger row per order (obligation, +) and per payment (settlement, −).
        var rows = new List<SupplierLedgerRow>();
        foreach (var o in orders)
        {
            var items = itemsByOrder.GetValueOrDefault(o.Id, new());
            rows.Add(new SupplierLedgerRow
            {
                Kind = SupplierLedgerKind.Order,
                Id = o.Id,
                Source = $"أمر توريد رقم PO-{o.Number:D4}",
                Date = o.OrderDate,
                Statement = items.Count > 0 ? string.Join("، ", items.Select(i => i.Name)) : "أمر توريد",
                Amount = items.Sum(i => i.Cost)
            });
        }
        foreach (var p in payments)
        {
            rows.Add(new SupplierLedgerRow
            {
                Kind = SupplierLedgerKind.Payment,
                Id = p.Id,
                Source = "إيصال دفع",
                Date = p.PaidDate,
                Statement = $"إيصال دفع رقم P-{p.ReceiptNo:D5}",
                ReceiptNo = p.ReceiptNo,
                Amount = p.Amount
            });
        }

        // Chronological running balance (owed to supplier) over ALL rows — orders before payments on
        // the same date — so each row's balance stays correct even when the list is date-filtered.
        var ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.Kind == SupplierLedgerKind.Order ? 0 : 1).ToList();
        decimal running = 0;
        foreach (var r in ordered)
        {
            r.BalanceBefore = running;
            running += r.Kind == SupplierLedgerKind.Order ? r.Amount : -r.Amount;
            r.Balance = running;
        }

        bool InRange(DateTime d) => (from is null || d >= from) && (to is null || d < to.Value.Date.AddDays(1));

        // Closing balance = running balance after the last movement on or before the range end.
        decimal closing = to is null
            ? running
            : ordered.Where(r => r.Date < to.Value.Date.AddDays(1)).Select(r => r.Balance).DefaultIfEmpty(0m).Last();

        // The pay button shows when ANY single order still has an outstanding balance — independent of
        // the net supplier balance (a supplier can be net-overpaid yet still have an unpaid order).
        var paidByOrder = payments.Where(p => p.SupplierOrderId.HasValue)
            .GroupBy(p => p.SupplierOrderId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var hasPayable = orders.Any(o =>
            (itemsByOrder.TryGetValue(o.Id, out var it) ? it.Sum(x => x.Cost) : 0) - paidByOrder.GetValueOrDefault(o.Id, 0) > 0);

        return new SupplierStatementVm
        {
            Supplier = supplier,
            From = from,
            To = to,
            TotalObligations = orders.Sum(o => itemsByOrder.TryGetValue(o.Id, out var it) ? it.Sum(x => x.Cost) : 0),
            TotalPaid = payments.Sum(p => p.Amount),
            OrdersCount = orders.Count,
            PaymentsCount = payments.Count,
            HasPayableOrders = hasPayable,
            Rows = ordered.Where(r => InRange(r.Date)).ToList(),
            ClosingBalance = closing,
        };
    }

    // ---------- Pay from the statement: pick one of the supplier's not-fully-paid orders ----------
    [HttpGet]
    public async Task<IActionResult> PayForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersPay)) return Forbid();
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        var orders = await _db.SupplierOrders.Where(o => o.SupplierId == id).OrderBy(o => o.Number).ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var itemSums = (await _db.SupplierOrderItems.Where(i => orderIds.Contains(i.SupplierOrderId))
            .GroupBy(i => i.SupplierOrderId).Select(g => new { g.Key, Sum = g.Sum(x => x.Cost) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var paidSums = (await _db.SupplierPayments.Where(p => p.SupplierOrderId != null && orderIds.Contains(p.SupplierOrderId!.Value))
            .GroupBy(p => p.SupplierOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

        var options = orders
            .Select(o => new { o.Number, o.Id, Rem = itemSums.GetValueOrDefault(o.Id, 0) - paidSums.GetValueOrDefault(o.Id, 0) })
            .Where(x => x.Rem > 0)
            .Select(x => new SupplierOrderOption(x.Id, $"PO-{x.Number:D4} — متبقٍ {x.Rem:N0} ج.م", x.Rem))
            .ToList();

        return PartialView("_SupplierPayPicker", new SupplierPayPickerModel
        {
            SupplierId = id,
            SupplierName = supplier.Name,
            Orders = options,
            Safes = await SafesAsync(ct)
        });
    }

    private async Task<List<SelectListItem>> SafesAsync(CancellationToken ct)
        => await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
}
