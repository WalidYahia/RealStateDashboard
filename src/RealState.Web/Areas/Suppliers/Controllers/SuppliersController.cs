using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

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
        if (await _db.PurchaseInvoices.AnyAsync(i => i.SupplierId == id, ct) ||
            await _db.SupplierOrders.AnyAsync(o => o.SupplierId == id, ct) ||
            await _db.SupplierPayments.AnyAsync(p => p.SupplierId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف مورد لديه فواتير مشتريات أو أوامر توريد أو مدفوعات.";
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
        ViewData["CanViewInvoices"] = Can(PermissionNames.PurchaseInvoicesView);
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

    /// <summary>An obligation (purchase invoice, or legacy order) with its total and what's been paid on it.</summary>
    private sealed record Obligation(SupplierLedgerKind Kind, Guid Id, string Label, DateTime Date, string Statement, decimal Total, decimal Paid)
    {
        public decimal Remaining => Total - Paid;
    }

    /// <summary>The supplier's obligations: every purchase invoice, plus legacy orders that still carry the supplier.</summary>
    private async Task<List<Obligation>> ObligationsAsync(Guid supplierId, CancellationToken ct)
    {
        var invoices = await _db.PurchaseInvoices.Where(i => i.SupplierId == supplierId).ToListAsync(ct);
        var invIds = invoices.Select(i => i.Id).ToList();
        var invItems = (await _db.PurchaseInvoiceItems.Where(i => invIds.Contains(i.PurchaseInvoiceId)).ToListAsync(ct))
            .GroupBy(i => i.PurchaseInvoiceId).ToDictionary(g => g.Key, g => g.ToList());

        var orders = await _db.SupplierOrders.Where(o => o.SupplierId == supplierId).ToListAsync(ct);
        var ordIds = orders.Select(o => o.Id).ToList();
        var ordItems = (await _db.SupplierOrderItems.Where(i => ordIds.Contains(i.SupplierOrderId)).ToListAsync(ct))
            .GroupBy(i => i.SupplierOrderId).ToDictionary(g => g.Key, g => g.ToList());

        var payments = await _db.SupplierPayments.Where(p => p.SupplierId == supplierId).ToListAsync(ct);
        var paidByInvoice = payments.Where(p => p.PurchaseInvoiceId.HasValue)
            .GroupBy(p => p.PurchaseInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var paidByOrder = payments.Where(p => p.SupplierOrderId.HasValue)
            .GroupBy(p => p.SupplierOrderId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        string Names(IEnumerable<string> names, string fallback)
        {
            var list = names.ToList();
            return list.Count > 0 ? string.Join("، ", list) : fallback;
        }

        return invoices.Select(i =>
            {
                var items = invItems.GetValueOrDefault(i.Id, new());
                return new Obligation(SupplierLedgerKind.Invoice, i.Id, $"فاتورة مشتريات رقم {PI(i.Number)}", i.InvoiceDate,
                    Names(items.Select(x => x.Name), "فاتورة مشتريات"), items.Sum(x => x.LineTotal), paidByInvoice.GetValueOrDefault(i.Id, 0));
            })
            .Concat(orders.Select(o =>
            {
                var items = ordItems.GetValueOrDefault(o.Id, new());
                return new Obligation(SupplierLedgerKind.Order, o.Id, $"أمر توريد رقم {PO(o.Number)}", o.OrderDate,
                    Names(items.Select(x => x.Name), "أمر توريد"), Math.Round(items.Sum(x => x.Cost * x.Quantity), 2), paidByOrder.GetValueOrDefault(o.Id, 0));
            }))
            .ToList();
    }

    private async Task<SupplierStatementVm> BuildStatementAsync(Supplier supplier, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var obligations = await ObligationsAsync(supplier.Id, ct);
        var payments = await _db.SupplierPayments.Where(p => p.SupplierId == supplier.Id).ToListAsync(ct);

        // Build one ledger row per obligation (+) and per payment (settlement, −).
        var rows = obligations.Select(o => new SupplierLedgerRow
        {
            Kind = o.Kind, Id = o.Id, Source = o.Label, Date = o.Date, Statement = o.Statement, Amount = o.Total
        }).ToList();
        rows.AddRange(payments.Select(p => new SupplierLedgerRow
        {
            Kind = SupplierLedgerKind.Payment,
            Id = p.Id,
            Source = "إيصال صرف نقدية",
            Date = p.PaidDate,
            Statement = $"إيصال صرف نقدية رقم {p.ReceiptNo:D5}",
            ReceiptNo = p.ReceiptNo,
            Amount = p.Amount
        }));

        // Chronological running balance (owed to supplier) over ALL rows — obligations before payments on
        // the same date — so each row's balance stays correct even when the list is date-filtered.
        var ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.IsObligation ? 0 : 1).ToList();
        decimal running = 0;
        foreach (var r in ordered)
        {
            r.BalanceBefore = running;
            running += r.IsObligation ? r.Amount : -r.Amount;
            r.Balance = running;
        }

        bool InRange(DateTime d) => (from is null || d >= from) && (to is null || d < to.Value.Date.AddDays(1));

        // Closing balance = running balance after the last movement on or before the range end.
        decimal closing = to is null
            ? running
            : ordered.Where(r => r.Date < to.Value.Date.AddDays(1)).Select(r => r.Balance).DefaultIfEmpty(0m).Last();

        return new SupplierStatementVm
        {
            Supplier = supplier,
            From = from,
            To = to,
            TotalObligations = obligations.Sum(o => o.Total),
            TotalPaid = payments.Sum(p => p.Amount),
            InvoicesCount = obligations.Count,
            PaymentsCount = payments.Count,
            // The pay button shows when ANY single document still has an outstanding balance — independent of
            // the net supplier balance (a supplier can be net-overpaid yet still have an unpaid invoice).
            HasPayableDocuments = obligations.Any(o => o.Remaining > 0),
            Rows = ordered.Where(r => InRange(r.Date)).ToList(),
            ClosingBalance = closing,
        };
    }

    // ---------- Pay from the statement: pick one of the supplier's not-fully-paid invoices ----------
    [HttpGet]
    public async Task<IActionResult> PayForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersPay)) return Forbid();
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();

        var options = (await ObligationsAsync(id, ct))
            .Where(o => o.Remaining > 0)
            .OrderBy(o => o.Date)
            .Select(o => new PayableOption(
                (o.Kind == SupplierLedgerKind.Invoice ? "I:" : "O:") + o.Id,
                $"{o.Label} — متبقٍ {o.Remaining:N2} ج.م",
                o.Remaining))
            .ToList();

        return PartialView("_SupplierPayPicker", new SupplierPayPickerModel
        {
            SupplierId = id,
            SupplierName = supplier.Name,
            Documents = options,
            Safes = await SafesAsync(_db, ct)
        });
    }
}
