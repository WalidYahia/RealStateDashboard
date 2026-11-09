using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;
using RealState.Web.Areas.CRM.Controllers;
using RealState.Web.Areas.Suppliers.Controllers;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// تسوية رصيد — raise or lower one customer's / supplier's balance without cash (a settlement discount, a written-off
/// remainder, a correction…), with its reason, from the party's statement. Each one posts a journal entry against
/// «تسويات أرصدة دائنة / مدينة» for its direction (<see cref="IAccountingService.SyncBalanceAdjustmentAsync"/>), and
/// shows as a row of the party's statement (and in its balance everywhere). Deleting one reverses its entry. Customers and
/// suppliers have their own permissions to adjust and to delete.
/// </summary>
[Area("Accounting")]
[Authorize]
public class BalanceAdjustmentsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingService _accounting;

    public BalanceAdjustmentsController(IApplicationDbContext db, IAccountingService accounting)
    {
        _db = db;
        _accounting = accounting;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);
    private static string ADJ(int number) => "ADJ-" + number;
    private static bool IsSupplier(string? party) => string.Equals(party, "supplier", StringComparison.OrdinalIgnoreCase);
    private bool CanAdjust(bool supplier) => Can(supplier ? PermissionNames.SuppliersAdjustBalance : PermissionNames.CustomersAdjustBalance);

    // ---------- New adjustment (modal, from the statement) ----------
    // The account adjusted is the page the form was opened from: the customer statement → the customer account, the
    // supplier page → the supplier account. A «عميل ومورد» party's form (from the customer statement) also shows its
    // supplier balance and the net, for reference.
    [HttpGet]
    public async Task<IActionResult> Form(string party, Guid id, CancellationToken ct)
    {
        var supplier = IsSupplier(party);
        if (!CanAdjust(supplier)) return Forbid();
        var model = new BalanceAdjustmentFormModel { Party = supplier ? "supplier" : "customer", PartyId = id };
        if (!supplier) model.LinkedSupplierId = await LinkedSupplierAsync(id, ct);
        return await FillAsync(model, ct) ? PartialView("_Form", model) : NotFound();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(BalanceAdjustmentFormModel model, CancellationToken ct)
    {
        // «عميل ومورد»: the posted link must be the customer's own.
        if (model.LinkedSupplierId is Guid posted && posted != await LinkedSupplierAsync(model.PartyId, ct))
            return BadRequest();
        if (!await FillAsync(model, ct)) return NotFound();
        var supplier = model.IsSupplier;
        if (!CanAdjust(supplier)) return Forbid();
        if (model.Date.Date > ClientTime.Today) ModelState.AddModelError(nameof(model.Date), "لا يمكن تسجيل تسوية بتاريخ مستقبلي.");
        if (!ModelState.IsValid) return PartialView("_Form", model);

        long lo = YearSerial.Base(model.Date.Year, 4), hi = YearSerial.End(model.Date.Year, 4);
        var max = await _db.BalanceAdjustments.Where(x => x.Number > lo && x.Number < hi).MaxAsync(x => (int?)x.Number, ct);
        var a = new BalanceAdjustment
        {
            Number = (int)YearSerial.Next(max, model.Date.Year, 4),
            CustomerId = supplier ? null : model.PartyId, SupplierId = supplier ? model.PartyId : null,
            // The counter account is always the default for the direction («تسويات أرصدة دائنة / مدينة»).
            Date = model.Date, Increase = model.Increase, Amount = model.Amount, Reason = model.Reason?.Trim() ?? ""
        };
        _db.BalanceAdjustments.Add(a);
        await _accounting.SyncBalanceAdjustmentAsync(a, ct);
        await _db.SaveChangesAsync(ct);

        var who = supplier ? $"المورد «{model.PartyName}»" : $"العميل «{model.PartyName}»";
        var what = supplier
            ? (a.Increase ? "زيادة المستحق له" : "تخفيض المستحق له")
            : (a.Increase ? "زيادة المستحق عليه" : "تخفيض المستحق عليه");
        TempData["StatusMessage"] = $"تم تسجيل تسوية الرصيد {ADJ(a.Number)} على {who}: {what} بمبلغ {a.Amount:N2} ج.م" + (string.IsNullOrWhiteSpace(a.Reason) ? "" : $" — {a.Reason}") + ".";
        return Json(new { ok = true });
    }

    // ---------- Delete (reverses its entry) ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnUrl, CancellationToken ct)
    {
        var a = await _db.BalanceAdjustments.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        var supplier = a.SupplierId is not null;
        if (!Can(supplier ? PermissionNames.SuppliersDeleteAdjustment : PermissionNames.CustomersDeleteAdjustment)) return Forbid();
        await _accounting.RemoveObligationAsync(AccountingSources.BalanceAdjustment, a.Id, ct);
        _db.BalanceAdjustments.Remove(a);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف تسوية الرصيد {ADJ(a.Number)} ({a.Amount:N2} ج.م) وعكس قيدها.";
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return supplier
            ? RedirectToAction("Details", "Suppliers", new { area = "Suppliers", id = a.SupplierId })
            : RedirectToAction("Statement", "Customers", new { area = "CRM", id = a.CustomerId });
    }

    /// <summary>The customer's linked supplier («عميل ومورد») — when the user may see suppliers.</summary>
    private async Task<Guid?> LinkedSupplierAsync(Guid customerId, CancellationToken ct) =>
        Can(PermissionNames.SuppliersView)
            ? await _db.Customers.Where(c => c.Id == customerId && c.SupplierId != null && _db.Suppliers.Any(s => s.Id == c.SupplierId))
                .Select(c => c.SupplierId).FirstOrDefaultAsync(ct)
            : null;

    /// <summary>The party's name and balance(s), the sides the user may adjust, and the counter accounts. False when the
    /// party doesn't exist.</summary>
    private async Task<bool> FillAsync(BalanceAdjustmentFormModel m, CancellationToken ct)
    {
        if (m.IsMerged)
        {
            // «عميل ومورد» from the customer statement: the customer account is adjusted; both balances are shown.
            var name = await _db.Customers.Where(c => c.Id == m.PartyId && !c.IsLead).Select(c => c.FullName).FirstOrDefaultAsync(ct);
            if (name is null) return false;
            m.PartyName = name;
            m.CustomerBalance = await CustomerLedger.BalanceAsync(_db, m.PartyId, ct);
            m.SupplierBalance = (await SupplierLedger.BuildAsync(_db, m.LinkedSupplierId!.Value, ct)).Balance;
            m.Balance = m.CustomerBalance;
        }
        else if (m.IsSupplier)
        {
            var name = await _db.Suppliers.Where(s => s.Id == m.PartyId).Select(s => s.Name).FirstOrDefaultAsync(ct);
            if (name is null) return false;
            m.PartyName = name;
            m.Balance = (await SupplierLedger.BuildAsync(_db, m.PartyId, ct)).Balance;
        }
        else
        {
            var name = await _db.Customers.Where(c => c.Id == m.PartyId && !c.IsLead).Select(c => c.FullName).FirstOrDefaultAsync(ct);
            if (name is null) return false;
            m.PartyName = name;
            m.Balance = await CustomerLedger.BalanceAsync(_db, m.PartyId, ct);
        }
        return true;
    }
}
