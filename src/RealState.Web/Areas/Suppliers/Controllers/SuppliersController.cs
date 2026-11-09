using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;
using RealState.Web.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

[Area("Suppliers")]
[Authorize(Policy = PermissionNames.SuppliersView)]
public class SuppliersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;
    private readonly ISafeBalanceGuard _guard;

    public SuppliersController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting, ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var suppliers = await _db.Suppliers.OrderBy(s => s.Name).ToListAsync(ct);
        ViewBag.LinkedSupplierIds = (await _db.Customers.Where(c => c.SupplierId != null).Select(c => c.SupplierId!.Value).ToListAsync(ct)).ToHashSet();
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
            Notes = s.Notes,
            IsAlsoCustomer = await _db.Customers.AnyAsync(c => c.SupplierId == s.Id, ct)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SupplierFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SuppliersCreate : PermissionNames.SuppliersEdit)) return Forbid();
        if (await _db.Suppliers.AnyAsync(s => s.Id != model.Id && s.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        // «عميل ومورد»: the customer record of the same party — the current link, else the customer with the same phone (the
        // same party), else a new one with the supplier's data.
        var linked = model.Id == Guid.Empty ? null : await _db.Customers.FirstOrDefaultAsync(c => c.SupplierId == model.Id, ct);
        Customer? target = null;
        // Without the customers view the form has no «عميل ومورد» field — leave the link as it is.
        var canLink = Can(PermissionNames.CustomersView);
        if (model.IsAlsoCustomer && canLink)
        {
            if (model.Id != Guid.Empty && await _db.Suppliers.AnyAsync(s => s.Id == model.Id && s.IsDefaultCash, ct))
                ModelState.AddModelError(nameof(model.IsAlsoCustomer), "المورد النقدي الافتراضي لا يمكن أن يكون عميلًا.");
            else if (linked is not null) target = linked;
            else if (await _db.Customers.FirstOrDefaultAsync(c => c.Phone == model.Phone, ct) is { } same)
            {
                if (same.IsLead)
                    ModelState.AddModelError(nameof(model.IsAlsoCustomer), $"رقم الهاتف مستخدم للعميل المحتمل «{same.FullName}» — حوّله إلى عميل أولًا.");
                else if (same.IsDefaultCash || (same.SupplierId is Guid other && other != model.Id))
                    ModelState.AddModelError(nameof(model.IsAlsoCustomer), $"رقم الهاتف مستخدم للعميل «{same.FullName}» المرتبط بمورد آخر.");
                else target = same;
            }
            else if (!Can(PermissionNames.CustomersCreate))
                ModelState.AddModelError(nameof(model.IsAlsoCustomer), "لا تملك صلاحية إضافة عميل.");
            // Both records keep the same basic data — the customer's phone must stay unique too.
            if (target is not null
                && await _db.Customers.AnyAsync(c => c.Id != target.Id && c.Phone == model.Phone, ct))
                ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم لعميل آخر.");
        }

        if (!ModelState.IsValid) return PartialView("_SupplierForm", model);

        Supplier supplier;
        if (model.Id == Guid.Empty)
        {
            supplier = new Supplier { Name = model.Name, Phone = model.Phone, Email = model.Email, Notes = model.Notes };
            _db.Suppliers.Add(supplier);
        }
        else
        {
            var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (s is null) return NotFound();
            s.Name = model.Name; s.Phone = model.Phone; s.Email = model.Email; s.Notes = model.Notes;
            supplier = s;
        }

        if (canLink && model.IsAlsoCustomer)
        {
            if (target is null)
            {
                target = new Customer { FullName = model.Name, Phone = model.Phone, Email = model.Email, Source = CustomerSource.Other };
                _db.Customers.Add(target);
            }
            else
            {
                target.FullName = model.Name; target.Phone = model.Phone; target.Email = model.Email;   // same party, same data
            }
            target.SupplierId = supplier.Id;
        }
        else if (canLink && linked is not null) linked.SupplierId = null;   // no longer a customer too — both records stay, unlinked

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ المورد «{model.Name}»."
            + (canLink && model.IsAlsoCustomer ? $" (عميل ومورد — مرتبط بالعميل «{target!.FullName}»)" : "");
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SuppliersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return NotFound();
        if (s.IsDefaultCash)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;   // refused — nothing changed
            TempData["ErrorMessage"] = $"«{s.Name}» هو المورد النقدي الافتراضي لفواتير المشتريات النقدية — لا يمكن حذفه.";
            return RedirectToAction(nameof(Index));
        }
        if (await _db.PurchaseInvoices.AnyAsync(i => i.SupplierId == id, ct) ||
            await _db.SupplierOrders.AnyAsync(o => o.SupplierId == id, ct) ||
            await _db.SupplierPayments.AnyAsync(p => p.SupplierId == id, ct) ||
            await _db.SafeTransactions.AnyAsync(t => t.Source == TxnSource.SupplierAccount && t.SupplierId == id, ct) ||
            await _db.BalanceAdjustments.AnyAsync(a => a.SupplierId == id, ct) ||
            await _db.AccountOpeningBalances.AnyAsync(b => b.Account!.SubKind == "Supplier" && b.Account.SubRefId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف مورد لديه فواتير مشتريات أو أوامر توريد أو مدفوعات أو رصيد افتتاحي.";
            return RedirectToAction(nameof(Index));
        }
        // «عميل ومورد»: the customer record stays — it just stops being a supplier too.
        foreach (var c in await _db.Customers.Where(c => c.SupplierId == id).ToListAsync(ct)) c.SupplierId = null;
        _db.Suppliers.Remove(s);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المورد «{s.Name}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Account statement (كشف الحساب) ----------
    public async Task<IActionResult> Details(Guid id, DateTime? from, DateTime? to, bool detailed, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();
        // «عميل ومورد»: the party has one merged statement (both accounts, net balance) — shown on the customer side.
        if (await LinkedCustomerIdAsync(id, ct) is Guid customerId)
            return RedirectToAction("Statement", "Customers", new { area = "CRM", id = customerId, from = Day(from), to = Day(to), detailed = detailed ? "true" : null });
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        // «تفصيلي»: every invoice's lines open (kept across filtering; each row can still be opened / closed on its own).
        ViewData["Detailed"] = detailed;
        ViewData["CanPay"] = Can(PermissionNames.SuppliersPay);
        ViewData["CanViewInvoices"] = Can(PermissionNames.PurchaseInvoicesView);
        return View(await BuildStatementAsync(supplier, from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintStatement(Guid id, DateTime? from, DateTime? to, bool detailed, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();
        if (await LinkedCustomerIdAsync(id, ct) is Guid customerId)
            return RedirectToAction("PrintLedger", "Customers", new { area = "CRM", id = customerId, from = Day(from), to = Day(to), detailed = detailed ? "true" : null });
        ViewBag.TenantId = _currentUser.TenantId;
        ViewData["Detailed"] = detailed;
        return View("PrintStatement", await BuildStatementAsync(supplier, from, to, ct));
    }

    /// <summary>The customer record of a «عميل ومورد» supplier — when the user may see customers (else the supplier-only statement).</summary>
    private async Task<Guid?> LinkedCustomerIdAsync(Guid supplierId, CancellationToken ct) =>
        Can(PermissionNames.CustomersView)
            ? await _db.Customers.Where(c => c.SupplierId == supplierId && !c.IsLead).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct)
            : null;

    private static string? Day(DateTime? d) => d?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<SupplierStatementVm> BuildStatementAsync(Supplier supplier, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var data = await SupplierLedger.BuildAsync(_db, supplier.Id, ct);
        var rows = data.Rows;

        // Chronological running balance (owed to supplier) over ALL rows — obligations before payments on
        // the same date — so each row's balance stays correct even when the list is date-filtered.
        var ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.DayOrder).ToList();
        decimal running = 0;
        foreach (var r in ordered)
        {
            r.BalanceBefore = running;
            running += r.Decreases ? -r.Amount : r.Amount;
            r.Balance = running;
        }

        bool InRange(DateTime d) => (from is null || d >= from) && (to is null || d < to.Value.Date.AddDays(1));

        // Closing balance = running balance after the last movement on or before the range end.
        decimal closing = to is null
            ? running
            : ordered.Where(r => r.Date < to.Value.Date.AddDays(1)).Select(r => r.Balance).DefaultIfEmpty(0m).Last();

        var shown = WithPeriodOpening(ordered, from, ordered.Where(r => InRange(r.Date)).ToList());
        await SupplierLedger.AttachItemsAsync(_db, shown, ct);

        return new SupplierStatementVm
        {
            Supplier = supplier,
            From = from,
            To = to,
            TotalObligations = data.TotalObligations,
            TotalPaid = data.TotalPaid,
            InvoicesCount = data.InvoicesCount,
            PaymentsCount = data.PaymentsCount,
            Rows = shown,
            ClosingBalance = closing,
        };
    }

    /// <summary>
    /// A period that starts after the account's first movement opens with «الرصيد الافتتاحي للفترة»: the balance
    /// accumulated by every movement before <paramref name="from"/>.
    /// </summary>
    private static List<SupplierLedgerRow> WithPeriodOpening(List<SupplierLedgerRow> all, DateTime? from, List<SupplierLedgerRow> inRange)
    {
        if (from is null || all.LastOrDefault(r => r.Date < from.Value) is not { } last) return inRange;
        inRange.Insert(0, new SupplierLedgerRow
        {
            Kind = SupplierLedgerKind.PeriodOpening, Date = from.Value,
            Source = "الرصيد الافتتاحي للفترة",
            Statement = $"رصيد الحساب المُرحَّل قبل {AppCulture.Day(from.Value)}",
            Amount = last.Balance, BalanceBefore = last.Balance, Balance = last.Balance
        });
        return inRange;
    }

    // ---------- Pay from the statement: a سند صرف on the supplier's account ----------
    // Payments settle the account balance, not a particular invoice.
    [HttpGet]
    public async Task<IActionResult> PayForm(Guid id, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersPay)) return Forbid();
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (supplier is null) return NotFound();
        var model = new AccountVoucherFormModel { PartyId = id, IsSupplier = true };
        await FillVoucherAsync(model, supplier, ct);
        return PartialView("_AccountVoucherForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(AccountVoucherFormModel model, CancellationToken ct)
    {
        if (!Can(PermissionNames.SuppliersPay)) return Forbid();
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == model.PartyId, ct);
        if (supplier is null) return NotFound();
        model.IsSupplier = true;
        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        else if (model.Amount > 0 && await _guard.CheckWithdrawalAsync(model.SafeId.Value, model.Amount, ct) is string overdraw)
            ModelState.AddModelError(nameof(model.Amount), overdraw);   // «سحب على المكشوف»
        if (model.Amount <= 0) ModelState.AddModelError(nameof(model.Amount), "أدخل مبلغًا أكبر من صفر.");
        if (model.OccurredAt.Date > ClientTime.Today) ModelState.AddModelError(nameof(model.OccurredAt), "لا يمكن السداد بتاريخ مستقبلي.");
        if (!ModelState.IsValid)
        {
            await FillVoucherAsync(model, supplier, ct);
            return PartialView("_AccountVoucherForm", model);
        }

        // Dr الموردون (the supplier's subsidiary) / Cr الخزنة — the same voucher as «المصروفات» on a supplier's account.
        var desc = string.IsNullOrWhiteSpace(model.Description) ? $"دفعة للمورد «{supplier.Name}»" : model.Description.Trim();
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.SupplierAccount,
            model.Amount, model.OccurredAt, desc, supplierId: supplier.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم سداد {model.Amount:N2} ج.م للمورد «{supplier.Name}» على الحساب (سند صرف رقم {txn.Serial}).";
        return Json(new { ok = true, openTab = Url.Action(nameof(VoucherReceipt), new { id = txn.Id }) });
    }

    /// <summary>A voucher on the supplier's account (سند صرف / سند قبض) — printable by whoever sees the statement.</summary>
    [HttpGet]
    public async Task<IActionResult> VoucherReceipt(Guid id, CancellationToken ct)
    {
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == id && t.Source == TxnSource.SupplierAccount && t.SupplierId != null, ct);
        if (txn is null) return NotFound();
        ViewBag.PartyLabel = "المورد";
        ViewBag.Party = await _db.Suppliers.Where(s => s.Id == txn.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.About = txn.Type == TxnType.Expense ? "سداد على الحساب" : "قبض من المورد على الحساب";
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    private async Task FillVoucherAsync(AccountVoucherFormModel model, Supplier supplier, CancellationToken ct)
    {
        model.PartyName = supplier.Name;
        model.Balance = (await SupplierLedger.BuildAsync(_db, supplier.Id, ct)).Balance;
        model.Safes = await SafesAsync(_db, ct);
    }
}
