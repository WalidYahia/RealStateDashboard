using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;
using RealState.Web.Areas.Suppliers.Controllers;
using RealState.Web.Models;

namespace RealState.Web.Areas.CRM.Controllers;

[Area("CRM")]
// Reachable by customer viewers AND leads-permission holders; each action further narrows access.
[Authorize(Policy = PermissionNames.LeadsAccessPolicy)]
public class CustomersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;

    public CustomersController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // Creating a lead is allowed for Leads.Control as well as the matching Customers.* permission.
    private bool CanManage(bool isLead, string customerPermission)
        => Can(customerPermission) || (isLead && Can(PermissionNames.LeadsControl));

    // Editing / deleting use dedicated lead permissions (Leads.Update / Leads.Delete); real customers keep
    // their own Customers.Edit / Customers.Delete permissions.
    private bool CanEditEntity(bool isLead) => Can(isLead ? PermissionNames.LeadsUpdate : PermissionNames.CustomersEdit);
    private bool CanDeleteEntity(bool isLead) => Can(isLead ? PermissionNames.LeadsDelete : PermissionNames.CustomersDelete);

    // ---------- Account statement / lead profile ----------
    public async Task<IActionResult> Details(Guid id, string? returnUrl, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        // A real customer's profile needs Customers.View; a lead's profile is open to leads-permission holders.
        if (!customer.IsLead && !Can(PermissionNames.CustomersView)) return Forbid();
        // A salesperson user may only open leads assigned to them (managers with Leads.Control see all).
        if (customer.IsLead && await IsRestrictedSalespersonAsync(ct) && !await IsRelatedSalespersonAsync(customer, ct))
            return Forbid();
        // Where the "back" button returns to — the filtered list we came from, when provided.
        ViewBag.ReturnUrl = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        ViewBag.SalesName = customer.SalesPersonId is null ? null
            : await _db.Employees.Where(e => e.Id == customer.SalesPersonId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        ViewBag.SourceLabel = customer.SourceLabel(campNames);
        var vm = await BuildStatementAsync(customer, ct);
        vm.Logs = await _db.CustomerLogs.Where(l => l.CustomerId == id)
            .OrderByDescending(l => l.Date).ThenByDescending(l => l.CreatedAt).ToListAsync(ct);
        vm.CanLog = await CanLogAsync(customer, ct);
        vm.CanControl = await CanControlAsync(customer, ct);
        vm.CanConvert = await CanConvertAsync(customer, ct);
        // Campaign-import details attached to this lead/customer (most recent first).
        ViewBag.CampaignLeads = await _db.CampaignLeads.Where(c => c.CustomerId == id)
            .OrderByDescending(c => c.CreatedTime).ToListAsync(ct);
        return View(vm);
    }

    // Printable communication log.
    [HttpGet]
    public async Task<IActionResult> PrintLog(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!c.IsLead && !Can(PermissionNames.CustomersView)) return Forbid();
        ViewBag.Customer = c;
        ViewBag.SalesName = c.SalesPersonId is null ? null
            : await _db.Employees.Where(e => e.Id == c.SalesPersonId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        ViewBag.SourceLabel = c.SourceLabel(campNames);
        ViewBag.TenantId = _currentUser.TenantId;
        var logs = await _db.CustomerLogs.Where(l => l.CustomerId == id)
            .OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        return View("PrintLog", logs);
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> PrintStatement(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintStatement", await BuildStatementAsync(customer, ct));
    }

    // Printable paid receipt for a settled installment.
    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> PrintReceipt(Guid id, CancellationToken ct)
    {
        var inst = await _db.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inst is null || inst.PaidAmount <= 0) return NotFound();
        var contract = await _db.SaleContracts.FirstOrDefaultAsync(s => s.Id == inst.SaleContractId, ct);
        ViewBag.Contract = contract;
        ViewBag.Customer = contract is null ? null : await _db.Customers.FirstOrDefaultAsync(c => c.Id == contract.CustomerId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintReceipt", inst);
    }

    // Printable due-payment notice for a single outstanding installment.
    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> PrintNotice(Guid id, CancellationToken ct)
    {
        var inst = await _db.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inst is null) return NotFound();
        var contract = await _db.SaleContracts.FirstOrDefaultAsync(s => s.Id == inst.SaleContractId, ct);
        if (contract?.IsCancelled == true) return NotFound();   // a cancelled contract's installments aren't due
        ViewBag.Contract = contract;
        ViewBag.Customer = contract is null ? null : await _db.Customers.FirstOrDefaultAsync(c => c.Id == contract.CustomerId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintNotice", inst);
    }

    // ---------- Running-balance account statement (كشف حساب — same layout as the supplier's) ----------
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> Statement(Guid id, DateTime? from, DateTime? to, bool detailed, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsLead, ct);
        if (customer is null) return NotFound();
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        // «تفصيلي»: every invoice's lines open (kept across filtering; each row can still be opened / closed on its own).
        ViewData["Detailed"] = detailed;
        // «فواتير المبيعات» / «تحصيل دفعة» are for customers without contracts (contracts collect through installments).
        var noContracts = !await HasContractsAsync(customer.Id, ct);
        ViewData["ShowInvoiceActions"] = noContracts;
        ViewData["CanCollect"] = noContracts && Can(PermissionNames.SalesInvoicesCollect);
        return View(await BuildLedgerAsync(customer, from, to, ct));
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> PrintLedger(Guid id, DateTime? from, DateTime? to, bool detailed, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsLead, ct);
        if (customer is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        ViewData["Detailed"] = detailed;
        return View("PrintLedger", await BuildLedgerAsync(customer, from, to, ct));
    }

    // ---------- Collect from the statement (customers without contracts): a سند قبض on the customer's account ----------
    // Payments settle the account balance, not a particular invoice. A contract customer pays through the installments
    // (التحصيلات), so the statement offers this only to customers with no contract.
    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesInvoicesCollect)]
    public async Task<IActionResult> CollectForm(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id && !c.IsLead, ct);
        if (customer is null || await HasContractsAsync(customer.Id, ct)) return NotFound();
        var model = new AccountVoucherFormModel { PartyId = customer.Id };
        await FillVoucherAsync(model, customer, ct);
        return PartialView("_AccountVoucherForm", model);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesInvoicesCollect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Collect(AccountVoucherFormModel model, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == model.PartyId && !c.IsLead, ct);
        if (customer is null || await HasContractsAsync(customer.Id, ct)) return NotFound();
        if (model.SafeId is null || !await _db.Safes.AnyAsync(s => s.Id == model.SafeId && s.IsActive, ct))
            ModelState.AddModelError(nameof(model.SafeId), "اختر خزنة صالحة.");
        if (model.Amount <= 0) ModelState.AddModelError(nameof(model.Amount), "أدخل مبلغًا أكبر من صفر.");
        if (model.OccurredAt.Date > ClientTime.Today) ModelState.AddModelError(nameof(model.OccurredAt), "لا يمكن التحصيل بتاريخ مستقبلي.");
        if (!ModelState.IsValid)
        {
            await FillVoucherAsync(model, customer, ct);
            return PartialView("_AccountVoucherForm", model);
        }

        // Dr الخزنة / Cr العملاء (the customer's subsidiary) — the same voucher as «الإيرادات» on a customer's account.
        var desc = string.IsNullOrWhiteSpace(model.Description) ? $"دفعة من العميل «{customer.FullName}»" : model.Description.Trim();
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Income, TxnSource.CustomerAccount,
            model.Amount, model.OccurredAt, desc, customerId: customer.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تحصيل {model.Amount:N2} ج.م من العميل «{customer.FullName}» على الحساب (سند قبض رقم {txn.Serial}).";
        return Json(new { ok = true, openTab = Url.Action(nameof(VoucherReceipt), new { id = txn.Id }) });
    }

    /// <summary>A voucher on the customer's account (سند قبض / سند صرف) — printable by whoever sees the statement.</summary>
    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> VoucherReceipt(Guid id, CancellationToken ct)
    {
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == id && t.Source == TxnSource.CustomerAccount && t.CustomerId != null, ct);
        if (txn is null) return NotFound();
        ViewBag.PartyLabel = "العميل";
        ViewBag.Party = await _db.Customers.Where(c => c.Id == txn.CustomerId).Select(c => c.FullName).FirstOrDefaultAsync(ct);
        ViewBag.About = txn.Type == TxnType.Income ? "تحصيل على الحساب" : "صرف للعميل على الحساب";
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    private async Task FillVoucherAsync(AccountVoucherFormModel model, Customer customer, CancellationToken ct)
    {
        model.PartyName = customer.FullName;
        model.Balance = await CustomerLedger.BalanceAsync(_db, customer.Id, ct);
        model.Safes = await PurchasingLookups.SafesAsync(_db, ct);
    }

    private Task<bool> HasContractsAsync(Guid customerId, CancellationToken ct)
        => _db.SaleContracts.AnyAsync(c => c.CustomerId == customerId, ct);

    /// <summary>
    /// Everything the customer owes (+: contracts, sales invoices, refunds paid back to them) and everything that settles
    /// it (−: installment / invoice collections, sales returns) as one chronological ledger with a running balance —
    /// computed over ALL rows so each row's balance is right even when the list is date-filtered.
    /// </summary>
    private async Task<CustomerLedgerVm> BuildLedgerAsync(Customer customer, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var rows = await CustomerLedger.RowsAsync(_db, customer.Id, ct);

        // «عميل ومورد»: the linked supplier's account joins the same statement (with the suppliers view), each movement
        // signed from the party's side — so the running balance is the net of both accounts.
        Supplier? linkedSupplier = null;
        SupplierLedgerData? supplierData = null;
        if (customer.SupplierId is Guid sid && Can(PermissionNames.SuppliersView)
            && (linkedSupplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == sid, ct)) is not null)
        {
            supplierData = await SupplierLedger.BuildAsync(_db, sid, ct);
            rows.AddRange(supplierData.Rows.Select(s => new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.SupplierSide, SupplierRow = s, Id = s.Id,
                Source = s.Source, Date = s.Date, Statement = s.Statement, Amount = s.Amount
            }));
        }

        var ordered = rows.Where(r => r.Amount != 0).OrderBy(r => r.Date).ThenBy(r => r.DayOrder).ToList();
        decimal running = 0;
        foreach (var r in ordered)
        {
            r.BalanceBefore = running;
            running += r.Decreases ? -r.Amount : r.Amount;
            r.Balance = running;
        }

        bool InRange(DateTime d) => (from is null || d >= from) && (to is null || d < to.Value.Date.AddDays(1));
        var closing = to is null
            ? running
            : ordered.Where(r => r.Date < to.Value.Date.AddDays(1)).Select(r => r.Balance).DefaultIfEmpty(0m).Last();

        var shown = WithPeriodOpening(ordered, from, ordered.Where(r => InRange(r.Date)).ToList());
        await CustomerLedger.AttachItemsAsync(_db, shown, ct);

        return new CustomerLedgerVm
        {
            Customer = customer, From = from, To = to,
            TotalObligations = ordered.Where(r => r.Kind is CustomerLedgerKind.Contract or CustomerLedgerKind.Invoice or CustomerLedgerKind.OpeningDebit or CustomerLedgerKind.CancelFee).Sum(r => r.Amount)
                               - ordered.Where(r => r.Kind is CustomerLedgerKind.Return or CustomerLedgerKind.ContractCancel).Sum(r => r.Amount),
            TotalPaid = ordered.Where(r => r.Kind is CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.AccountReceipt or CustomerLedgerKind.OpeningCredit).Sum(r => r.Amount)
                        - ordered.Where(r => r.Kind is CustomerLedgerKind.Refund or CustomerLedgerKind.AccountPayment).Sum(r => r.Amount),
            // Contracts still in force (each cancelled one also has a ContractCancel row) + sales invoices.
            DocumentsCount = rows.Count(r => r.Kind is CustomerLedgerKind.Contract or CustomerLedgerKind.Invoice) - rows.Count(r => r.Kind == CustomerLedgerKind.ContractCancel),
            CollectionsCount = ordered.Count(r => r.Kind is CustomerLedgerKind.InstallmentCollection or CustomerLedgerKind.InvoiceCollection or CustomerLedgerKind.AccountReceipt),
            Rows = shown,
            ClosingBalance = closing,
            LinkedSupplier = linkedSupplier,
            SupplierObligations = supplierData?.TotalObligations ?? 0,
            SupplierPaid = supplierData?.TotalPaid ?? 0,
            SupplierInvoicesCount = supplierData?.InvoicesCount ?? 0,
        };
    }

    /// <summary>
    /// A period that starts after the account's first movement opens with «الرصيد الافتتاحي للفترة»: the balance
    /// accumulated by every movement before <paramref name="from"/>.
    /// </summary>
    private static List<CustomerLedgerRow> WithPeriodOpening(List<CustomerLedgerRow> all, DateTime? from, List<CustomerLedgerRow> inRange)
    {
        if (from is null || all.LastOrDefault(r => r.Date < from.Value) is not { } last) return inRange;
        inRange.Insert(0, new CustomerLedgerRow
        {
            Kind = CustomerLedgerKind.PeriodOpening, Id = Guid.Empty, Date = from.Value,
            Source = "الرصيد الافتتاحي للفترة",
            Statement = $"رصيد الحساب المُرحَّل قبل {RealState.Web.AppCulture.Day(from.Value)}",
            Amount = last.Balance, BalanceBefore = last.Balance, Balance = last.Balance
        });
        return inRange;
    }

    private async Task<CustomerStatementVm> BuildStatementAsync(Customer customer, CancellationToken ct)
    {
        var contracts = await _db.SaleContracts.Where(s => s.CustomerId == customer.Id).OrderBy(s => s.ReceiveDate).ToListAsync(ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);

        var vm = new CustomerStatementVm { Customer = customer };
        var ids = contracts.Select(c => c.Id).ToList();
        var refunded = (await _db.SafeTransactions
                .Where(t => t.Source == TxnSource.ContractCancelRefund && t.SaleContractId != null && ids.Contains(t.SaleContractId.Value))
                .GroupBy(t => t.SaleContractId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        foreach (var c in contracts)
        {
            var insts = await _db.Installments.Where(i => i.SaleContractId == c.Id).OrderBy(i => i.DueDate).ThenBy(i => i.Type).ThenBy(i => i.Number).ToListAsync(ct);
            vm.Contracts.Add(new ContractStatement
            {
                Code = c.Code,
                ProjectName = projNames.GetValueOrDefault(c.ProjectId, "—"),
                UnitLabel = unitNames.GetValueOrDefault(c.UnitId, "—"),
                ReceiveDate = c.ReceiveDate,
                TotalPrice = c.TotalPrice,
                DownPayment = c.DownPayment,
                // A cancelled contract's schedule is frozen: only what was collected before the cancellation is shown.
                Installments = c.IsCancelled ? insts.Where(i => i.PaidAmount > 0).ToList() : insts,
                IsCancelled = c.IsCancelled,
                CancelFeeAmount = c.CancelFeeAmount,
                RefundDue = c.CancelRefundDue,
                Refunded = refunded.GetValueOrDefault(c.Id)
            });
        }
        return vm;
    }

    [Authorize(Policy = PermissionNames.CustomersView)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        // Leads (potential customers) are listed on the Leads page until converted.
        var customers = await _db.Customers.Where(c => !c.IsLead).OrderBy(c => c.FullName).ToListAsync(ct);
        ViewBag.SalesNames = await _db.Employees
            .Where(e => e.Type == EmployeeType.Salesperson)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        ViewBag.CampaignNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return View(customers);
    }

    // Add (id null) or edit (id set) — shown in a modal popup.
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, bool asLead, CancellationToken ct)
    {
        if (id is null)
        {
            if (!CanManage(asLead, PermissionNames.CustomersCreate)) return Forbid();
            return PartialView("_CustomerForm", await FillAsync(new CustomerFormModel { IsLead = asLead }, ct));
        }
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!CanEditEntity(c.IsLead)) return Forbid();
        return PartialView("_CustomerForm", await FillAsync(new CustomerFormModel
        {
            Id = c.Id,
            FullName = c.FullName,
            Phone = c.Phone ?? "",
            Email = c.Email,
            Source = c.Source,
            Channel = DeriveChannel(c),
            SourceCampaignId = c.SourceCampaignId,
            SalesPersonId = c.SalesPersonId,
            Notes = c.Notes,
            IsLead = c.IsLead,
            Interest = c.Interest,
            IsAlsoSupplier = c.SupplierId.HasValue
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(CustomerFormModel model, CancellationToken ct)
    {
        if (model.Id == Guid.Empty)
        {
            if (!CanManage(model.IsLead, PermissionNames.CustomersCreate)) return Forbid();
        }
        else
        {
            // Use the stored flag (not the posted one) to decide which permission applies.
            var isLead = await _db.Customers.Where(x => x.Id == model.Id).Select(x => (bool?)x.IsLead).FirstOrDefaultAsync(ct) ?? false;
            if (!CanEditEntity(isLead)) return Forbid();
        }
        await ValidateSalespersonAsync(model, ct);
        // Salesperson is optional, except a lead sourced from the "مندوب مبيعات" channel must name one.
        if (model.IsLead && model.Channel == LeadChannel.Salesperson && model.SalesPersonId is null)
            ModelState.AddModelError(nameof(model.SalesPersonId), "يجب اختيار المندوب عندما تكون القناة «مندوب مبيعات».");
        // Phone must be unique across customers.
        if (await _db.Customers.AnyAsync(c => c.Id != model.Id && c.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        // «عميل ومورد»: the supplier record of the same party — the current link, else the supplier with the same phone (the
        // same party), else a new one with the customer's data. Real customers only (a lead isn't a customer yet; the
        // built-in cash customer stays a plain customer).
        var stored = model.Id == Guid.Empty ? null
            : await _db.Customers.Where(x => x.Id == model.Id).Select(x => new { x.IsLead, x.IsDefaultCash, x.SupplierId }).FirstOrDefaultAsync(ct);
        var isLeadRecord = stored?.IsLead ?? model.IsLead;
        var linked = stored?.SupplierId is Guid ls ? await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == ls, ct) : null;
        Supplier? target = null;
        if (model.IsAlsoSupplier && !isLeadRecord && Can(PermissionNames.SuppliersView))
        {
            if (stored?.IsDefaultCash == true)
                ModelState.AddModelError(nameof(model.IsAlsoSupplier), "العميل النقدي الافتراضي لا يمكن أن يكون موردًا.");
            else if (linked is not null) target = linked;
            else if (await _db.Suppliers.FirstOrDefaultAsync(s => s.Phone == model.Phone, ct) is { } same)
            {
                if (same.IsDefaultCash || await _db.Customers.AnyAsync(c => c.SupplierId == same.Id && c.Id != model.Id, ct))
                    ModelState.AddModelError(nameof(model.IsAlsoSupplier), $"رقم الهاتف مستخدم للمورد «{same.Name}» المرتبط بعميل آخر.");
                else target = same;
            }
            else if (!Can(PermissionNames.SuppliersCreate))
                ModelState.AddModelError(nameof(model.IsAlsoSupplier), "لا تملك صلاحية إضافة مورد.");
            // Both records keep the same basic data — the supplier's phone must stay unique too.
            if (target is not null
                && await _db.Suppliers.AnyAsync(s => s.Id != target.Id && s.Phone == model.Phone, ct))
                ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم لمورد آخر.");
        }

        if (!ModelState.IsValid) return PartialView("_CustomerForm", await FillAsync(model, ct));

        Customer customer;
        if (model.Id == Guid.Empty)
        {
            var c = customer = new Customer
            {
                FullName = model.FullName,
                Phone = model.Phone,
                Email = model.Email,
                SalesPersonId = model.SalesPersonId,
                Notes = model.Notes,
                IsLead = model.IsLead,
                Interest = null // status is set later from the lead page
            };
            // Leads carry an editable creation date (defaults to today); use a real timestamp when it's today.
            if (model.IsLead)
                c.CreatedAt = model.CreatedOn.Date == ClientTime.Today ? ClientTime.Now : model.CreatedOn.Date;
            ApplyChannel(c, model);
            _db.Customers.Add(c);
            if (model.IsLead)
            {
                _db.CustomerLogs.Add(new CustomerLog
                {
                    CustomerId = c.Id, Date = ClientTime.Today, Kind = CustomerLogKind.Created,
                    ByName = CurrentName(), ByUserId = _currentUser.UserId, Description = "تم إنشاء العميل المحتمل"
                });
            }
        }
        else
        {
            var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (c is null) return NotFound();
            // Basic data only — IsLead is changed via Convert; Interest via the status action (both logged).
            c.FullName = model.FullName;
            c.Phone = model.Phone;
            c.Email = model.Email;
            c.SalesPersonId = model.SalesPersonId;
            c.Notes = model.Notes;
            ApplyChannel(c, model);
            customer = c;
        }

        // Without the suppliers view the form has no «عميل ومورد» field — leave the link as it is.
        if (!isLeadRecord && Can(PermissionNames.SuppliersView))
        {
            if (model.IsAlsoSupplier)
            {
                if (target is null)
                {
                    target = new Supplier { Name = model.FullName, Phone = model.Phone, Email = model.Email };
                    _db.Suppliers.Add(target);
                }
                else
                {
                    target.Name = model.FullName; target.Phone = model.Phone; target.Email = model.Email;   // same party, same data
                }
                customer.SupplierId = target.Id;
            }
            else customer.SupplierId = null;   // no longer a supplier too — both records stay, unlinked
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.IsLead && model.Id == Guid.Empty
            ? $"تمت إضافة العميل المحتمل «{model.FullName}»."
            : $"تم حفظ العميل «{model.FullName}»." + (customer.SupplierId.HasValue && target is not null ? $" (عميل ومورد — مرتبط بالمورد «{target.Name}»)" : "");
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, string? returnUrl, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!CanDeleteEntity(c.IsLead)) return Forbid();
        if (c.IsDefaultCash)
        {
            HttpContext.Items[RealState.Web.Filters.ActivityLogFilter.SkipItem] = true;   // refused — nothing changed
            TempData["ErrorMessage"] = $"«{c.FullName}» هو العميل النقدي الافتراضي لفواتير المبيعات النقدية — لا يمكن حذفه.";
            return RedirectToAction(nameof(Details), new { id });
        }
        // A customer billed by product sales invoices keeps its receivable history — delete the invoices first.
        // …and so does one with real-estate sale contracts (receivable, installments, collections).
        var hasInvoices = await _db.ProductSalesInvoices.AnyAsync(i => i.CustomerId == id, ct)
                          || await _db.SafeTransactions.AnyAsync(t => t.Source == TxnSource.CustomerAccount && t.CustomerId == id, ct)
                          || await _db.AccountOpeningBalances.AnyAsync(b => b.Account!.SubKind == "Customer" && b.Account.SubRefId == id, ct);
        if (hasInvoices || await _db.SaleContracts.AnyAsync(s => s.CustomerId == id, ct))
        {
            TempData["ErrorMessage"] = hasInvoices
                ? $"لا يمكن حذف العميل «{c.FullName}» لوجود فواتير مبيعات أو سندات أو رصيد افتتاحي على حسابه — احذفها أولًا."
                : $"لا يمكن حذف العميل «{c.FullName}» لوجود عقود بيع عليه — احذف العقود أولًا.";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
            return RedirectToAction(nameof(Details), new { id });
        }
        var wasLead = c.IsLead;
        var name = c.FullName;
        if (wasLead)
        {
            // Leads are hard-deleted (physically removed), not soft-deleted — the row and its campaign-import
            // rows / logs are gone for good (they cascade in the DB), so the same lead can be re-imported later.
            await _db.Customers.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        }
        else
        {
            _db.Customers.Remove(c);   // real customers keep the soft-delete to preserve their linked history
            await _db.SaveChangesAsync(ct);
        }
        TempData["StatusMessage"] = $"تم حذف {(wasLead ? "العميل المحتمل" : "العميل")} «{name}».";
        // Return to the exact filtered list the delete was launched from, when provided.
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return wasLead ? RedirectToAction("Index", "Leads") : RedirectToAction(nameof(Index));
    }

    // ---------- Lead conversion ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!await CanConvertAsync(c, ct)) return Forbid();
        if (c.IsLead)
        {
            c.IsLead = false;
            _db.CustomerLogs.Add(new CustomerLog
            {
                CustomerId = c.Id, Date = ClientTime.Today, Kind = CustomerLogKind.Conversion,
                ByName = CurrentName(), ByUserId = _currentUser.UserId, Description = "تم تحويل العميل المحتمل إلى عميل"
            });
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = $"تم تحويل «{c.FullName}» إلى عميل.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------- Communication log (only the customer's assigned salesperson) ----------
    [HttpGet]
    public async Task<IActionResult> LogForm(Guid customerId, Guid? id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct);
        if (c is null) return NotFound();
        if (!await CanLogAsync(c, ct)) return Forbid();
        if (id is null) return PartialView("_CustomerLogForm", new CustomerLogFormModel { CustomerId = customerId });
        var log = await _db.CustomerLogs.FirstOrDefaultAsync(l => l.Id == id && l.CustomerId == customerId, ct);
        if (log is null) return NotFound();
        return PartialView("_CustomerLogForm", new CustomerLogFormModel { Id = log.Id, CustomerId = customerId, Date = log.Date, Description = log.Description });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLog(CustomerLogFormModel model, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == model.CustomerId, ct);
        if (c is null) return NotFound();
        if (!await CanLogAsync(c, ct)) return Forbid();
        if (!ModelState.IsValid) return PartialView("_CustomerLogForm", model);

        if (model.Id == Guid.Empty)
        {
            _db.CustomerLogs.Add(new CustomerLog
            {
                CustomerId = model.CustomerId, Date = model.Date, Description = model.Description.Trim(),
                Kind = CustomerLogKind.Manual, ByName = CurrentName(), ByUserId = _currentUser.UserId
            });
        }
        else
        {
            var log = await _db.CustomerLogs.FirstOrDefaultAsync(l => l.Id == model.Id && l.CustomerId == model.CustomerId, ct);
            if (log is null) return NotFound();
            log.Date = model.Date;
            log.Description = model.Description.Trim();
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم حفظ السجل.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLog(Guid id, Guid customerId, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == customerId, ct);
        if (c is null) return NotFound();
        if (!await CanLogAsync(c, ct)) return Forbid();
        var log = await _db.CustomerLogs.FirstOrDefaultAsync(l => l.Id == id && l.CustomerId == customerId, ct);
        if (log is not null) { _db.CustomerLogs.Remove(log); await _db.SaveChangesAsync(ct); }
        return RedirectToAction(nameof(Details), new { id = customerId });
    }

    // ---------- Lead status (logs the change) ----------
    [HttpGet]
    public async Task<IActionResult> StatusForm(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!await CanControlAsync(c, ct)) return Forbid();
        return PartialView("_LeadStatusForm", new LeadStatusFormModel { CustomerId = id, Interest = c.Interest });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(LeadStatusFormModel model, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == model.CustomerId, ct);
        if (c is null) return NotFound();
        if (!await CanControlAsync(c, ct)) return Forbid();
        if (c.Interest != model.Interest)
        {
            c.Interest = model.Interest;
            var label = model.Interest.HasValue ? model.Interest.Value.Ar() : "بدون";
            _db.CustomerLogs.Add(new CustomerLog
            {
                CustomerId = c.Id, Date = ClientTime.Today, Kind = CustomerLogKind.StatusChange,
                ByName = CurrentName(), ByUserId = _currentUser.UserId, Description = $"تغيير الحالة إلى: {label}"
            });
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = "تم تحديث الحالة.";
        }
        return Json(new { ok = true });
    }

    private string CurrentName() => User.FindFirst("full_name")?.Value ?? User.Identity?.Name ?? "—";

    private async Task<bool> IsRelatedSalespersonAsync(Customer c, CancellationToken ct)
    {
        var uid = _currentUser.UserId;
        if (uid is null || c.SalesPersonId is null) return false;
        return await _db.Employees.AnyAsync(e => e.Id == c.SalesPersonId && e.UserId == uid, ct);
    }

    /// <summary>True when the current user is a salesperson (linked to a salesperson employee) — such users
    /// are limited to their own assigned leads. Non-salesperson staff (managers/admins) are not restricted.</summary>
    private async Task<bool> IsRestrictedSalespersonAsync(CancellationToken ct)
    {
        var uid = _currentUser.UserId;
        if (uid is null) return false;
        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        return await _db.Employees.AnyAsync(
            e => e.UserId == uid && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value), ct);
    }

    /// <summary>Who may add/edit/delete the communication log (and send WhatsApp): ONLY the customer's
    /// assigned salesperson (the user linked to that employee).</summary>
    private async Task<bool> CanLogAsync(Customer c, CancellationToken ct)
        => await IsRelatedSalespersonAsync(c, ct);

    /// <summary>Who may change the lead status: the assigned salesperson, or anyone granted "Leads.Control".</summary>
    private async Task<bool> CanControlAsync(Customer c, CancellationToken ct)
        => Can(PermissionNames.LeadsControl) || await IsRelatedSalespersonAsync(c, ct);

    /// <summary>Who may convert a lead to a customer: the assigned salesperson, or anyone granted "Leads.Convert".</summary>
    private async Task<bool> CanConvertAsync(Customer c, CancellationToken ct)
        => Can(PermissionNames.LeadsConvert) || await IsRelatedSalespersonAsync(c, ct);

    // Only employees whose job role is flagged "مندوب مبيعات" are selectable as a customer's salesperson.
    private async Task<List<Guid>> SalespersonRoleIdsAsync(CancellationToken ct) =>
        await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);

    private async Task ValidateSalespersonAsync(CustomerFormModel model, CancellationToken ct)
    {
        if (model.SalesPersonId is null) return; // [Required] already reports it
        var salesRoleIds = await SalespersonRoleIdsAsync(ct);
        var exists = await _db.Employees.AnyAsync(
            e => e.Id == model.SalesPersonId && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value), ct);
        if (!exists) ModelState.AddModelError(nameof(model.SalesPersonId), "المندوب المحدد غير موجود.");
    }

    private async Task<CustomerFormModel> FillAsync(CustomerFormModel model, CancellationToken ct)
    {
        var salesRoleIds = await SalespersonRoleIdsAsync(ct);
        var sales = await _db.Employees
            .Where(e => e.IsActive && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName }).ToListAsync(ct);
        model.SalesPersons = sales.Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToList();

        var camps = await _db.Campaigns.Where(x => x.Status == CampaignStatus.Active)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        model.Campaigns = camps.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name }).ToList();
        return model;
    }

    /// <summary>Applies the lead channel to the customer: fixes the source for the salesperson channel,
    /// stores the campaign for the campaign channel, and clears whatever doesn't apply.</summary>
    private static void ApplyChannel(Customer c, CustomerFormModel m)
    {
        if (!c.IsLead)
        {
            c.Source = m.Source; c.Channel = null; c.SourceCampaignId = null;
            return;
        }
        c.Channel = m.Channel;
        switch (m.Channel)
        {
            case LeadChannel.Salesperson:
                c.Source = CustomerSource.Salesperson; c.SourceCampaignId = null; break;
            case LeadChannel.Campaign:
                c.SourceCampaignId = m.SourceCampaignId; c.Source = CustomerSource.Other; break;
            default: // SocialMedia / Other
                c.Source = m.Source; c.SourceCampaignId = null; break;
        }
    }

    private static LeadChannel DeriveChannel(Customer c)
        => c.Channel ?? (c.Source == CustomerSource.Salesperson ? LeadChannel.Salesperson
            : ArabicLabels.SocialMediaSources.Contains(c.Source) ? LeadChannel.SocialMedia
            : LeadChannel.Other);
}
