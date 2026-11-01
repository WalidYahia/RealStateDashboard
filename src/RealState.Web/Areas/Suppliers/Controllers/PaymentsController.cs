using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>
/// Supplier payments recorded against one purchase invoice (or a legacy supplier order) — the automatic payment of a
/// cash (نقدي) invoice, and those made before payments moved to the supplier's account. New payments are سند صرف
/// vouchers on the supplier's account (SuppliersController.Pay); this only prints the existing ones.
/// </summary>
[Area("Suppliers")]
[Authorize]
public class PaymentsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PaymentsController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

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
}
