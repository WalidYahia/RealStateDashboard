using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>
/// Supplier payments (سداد الموردين). A payment settles one purchase invoice — or, for data from before the
/// invoice stage, one legacy supplier order — and is recorded as a safe Expense movement whose serial is
/// the pay-receipt number.
/// </summary>
[Area("Suppliers")]
[Authorize]
public class PaymentsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly ISafeBalanceGuard _guard;

    public PaymentsController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    /// <summary>What a payment is being made against, resolved from a "I:{id}" / "O:{id}" target.</summary>
    private sealed record Payable(Guid? InvoiceId, Guid? OrderId, Guid SupplierId, Guid? ProjectId,
        string DocLabel, string DocKind, decimal Total, decimal Paid)
    {
        public decimal Remaining => Total - Paid;
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.SuppliersPay)]
    public async Task<IActionResult> Pay(string target, CancellationToken ct)
    {
        var p = await ResolveAsync(target, ct);
        if (p is null) return NotFound();
        return PartialView("_PayForm", new SupplierPayFormModel
        {
            Target = target,
            DocumentLabel = await LabelAsync(p, ct),
            Total = p.Total,
            Paid = p.Paid,
            Remaining = p.Remaining,
            PaidDate = DateTime.Today,
            Safes = await SafesAsync(_db, ct)
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersPay)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(SupplierPayFormModel model, CancellationToken ct)
    {
        var p = await ResolveAsync(model.Target, ct);
        if (p is null) return NotFound();

        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "أدخل مبلغًا أكبر من صفر.");
        if (model.Amount > p.Remaining)
            ModelState.AddModelError(nameof(model.Amount), $"المبلغ يتجاوز المتبقي على {p.DocKind} ({p.Remaining:N2} ج.م).");
        if (ModelState.IsValid && await _guard.CheckWithdrawalAsync(model.SafeId!.Value, model.Amount, ct) is string overdraw)
            ModelState.AddModelError(nameof(model.Amount), overdraw);   // «سحب على المكشوف»

        if (!ModelState.IsValid)
        {
            model.Safes = await SafesAsync(_db, ct);
            model.Total = p.Total;
            model.Paid = p.Paid;
            model.Remaining = p.Remaining;
            model.DocumentLabel = await LabelAsync(p, ct);
            return PartialView("_PayForm", model);
        }

        // Record as an Expense (charged to the document's project). The expense's serial IS the pay-receipt number.
        var supplierName = await _db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var projPart = "";
        if (p.ProjectId.HasValue)
        {
            var pn = await _db.Projects.Where(x => x.Id == p.ProjectId).Select(x => x.Name).FirstOrDefaultAsync(ct);
            if (pn != null) projPart = $" — مشروع {pn}";
        }
        var desc = $"دفعة للمورد «{supplierName}» على {p.DocKind} {p.DocLabel}{projPart}";
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.SupplierPayment,
            model.Amount, model.PaidDate, desc, projectId: p.ProjectId, supplierId: p.SupplierId, ct: ct);

        var payment = new SupplierPayment
        {
            SupplierId = p.SupplierId,
            PurchaseInvoiceId = p.InvoiceId,
            SupplierOrderId = p.OrderId,
            Amount = model.Amount,
            PaidDate = model.PaidDate,
            SafeId = model.SafeId.Value,
            ReceiptNo = txn.Serial,
            Description = model.Description
        };
        _db.SupplierPayments.Add(payment);
        txn.Description = $"{desc} (إيصال صرف نقدية رقم {txn.Serial:D5})";
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم سداد {model.Amount:N2} ج.م للمورد «{supplierName}» على {p.DocKind} {p.DocLabel} (إيصال صرف نقدية رقم {txn.Serial:D5}).";
        return Json(new { ok = true, openTab = Url.Action(nameof(Receipt), new { id = payment.Id }) });
    }

    // The supplier pay receipt IS the unified cash-payment voucher (إيصال صرف نقدية) of the linked expense movement.
    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersView) && !Can(PermissionNames.PurchaseInvoicesView)) return Forbid();
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
        string about = "سداد للمورد";
        if (payment.PurchaseInvoiceId.HasValue)
        {
            var n = await _db.PurchaseInvoices.Where(i => i.Id == payment.PurchaseInvoiceId).Select(i => (int?)i.Number).FirstOrDefaultAsync(ct);
            if (n.HasValue) about = $"سداد فاتورة مشتريات {PI(n.Value)}";
        }
        else if (payment.SupplierOrderId.HasValue)
        {
            var n = await _db.SupplierOrders.Where(o => o.Id == payment.SupplierOrderId).Select(o => (int?)o.Number).FirstOrDefaultAsync(ct);
            if (n.HasValue) about = $"سداد أمر توريد {PO(n.Value)}";
        }
        ViewBag.PartyLabel = "المورد";
        ViewBag.Party = supplier?.Name;
        ViewBag.About = about;
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == payment.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    // ---------- helpers ----------
    private async Task<Payable?> ResolveAsync(string? target, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(target) || target.Length < 3 || target[1] != ':' || !Guid.TryParse(target[2..], out var id)) return null;
        switch (target[0])
        {
            case 'I':
            {
                var inv = await _db.PurchaseInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
                if (inv is null) return null;
                var total = await _db.PurchaseInvoiceItems.Where(i => i.PurchaseInvoiceId == id).SumAsync(i => (decimal?)i.LineTotal, ct) ?? 0;
                var paid = await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId == id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
                // Net of purchase returns (debit notes) and of the cash the supplier refunded through them.
                var r = await RealState.Application.Accounting.InvoiceReturns.ForPurchaseInvoiceAsync(_db, id, ct);
                return new Payable(inv.Id, null, inv.SupplierId, inv.ProjectId, PI(inv.Number), "فاتورة المشتريات", total - r.Returned, paid - r.Refunded);
            }
            case 'O':
            {
                // Legacy orders only — new orders have no supplier and are never paid directly.
                var o = await _db.SupplierOrders.FirstOrDefaultAsync(x => x.Id == id && x.SupplierId != null, ct);
                if (o is null) return null;
                var total = await _db.SupplierOrderItems.Where(i => i.SupplierOrderId == id).SumAsync(i => (decimal?)(i.Cost * i.Quantity), ct) ?? 0;
                var paid = await _db.SupplierPayments.Where(p => p.SupplierOrderId == id).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
                return new Payable(null, o.Id, o.SupplierId!.Value, o.ProjectId, PO(o.Number), "أمر التوريد", Math.Round(total, 2), paid);
            }
            default:
                return null;
        }
    }

    private async Task<string> LabelAsync(Payable p, CancellationToken ct)
    {
        var name = await _db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        return $"{p.DocLabel} — {name}";
    }
}
