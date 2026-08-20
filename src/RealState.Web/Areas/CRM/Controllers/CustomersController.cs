using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;

namespace RealState.Web.Areas.CRM.Controllers;

[Area("CRM")]
// Reachable by customer viewers AND leads-permission holders; each action further narrows access.
[Authorize(Policy = PermissionNames.LeadsAccessPolicy)]
public class CustomersController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CustomersController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // Managing a lead (create/edit/delete) is allowed for Leads.Control as well as the matching Customers.* permission.
    private bool CanManage(bool isLead, string customerPermission)
        => Can(customerPermission) || (isLead && Can(PermissionNames.LeadsControl));

    // ---------- Account statement / lead profile ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        // A real customer's profile needs Customers.View; a lead's profile is open to leads-permission holders.
        if (!customer.IsLead && !Can(PermissionNames.CustomersView)) return Forbid();
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
        ViewBag.Contract = contract;
        ViewBag.Customer = contract is null ? null : await _db.Customers.FirstOrDefaultAsync(c => c.Id == contract.CustomerId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintNotice", inst);
    }

    private async Task<CustomerStatementVm> BuildStatementAsync(Customer customer, CancellationToken ct)
    {
        var contracts = await _db.SaleContracts.Where(s => s.CustomerId == customer.Id).OrderBy(s => s.ReceiveDate).ToListAsync(ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);

        var vm = new CustomerStatementVm { Customer = customer };
        foreach (var c in contracts)
        {
            var insts = await _db.Installments.Where(i => i.SaleContractId == c.Id).OrderBy(i => i.Number).ToListAsync(ct);
            vm.Contracts.Add(new ContractStatement
            {
                Code = c.Code,
                ProjectName = projNames.GetValueOrDefault(c.ProjectId, "—"),
                UnitLabel = unitNames.GetValueOrDefault(c.UnitId, "—"),
                ReceiveDate = c.ReceiveDate,
                TotalPrice = c.TotalPrice,
                DownPayment = c.DownPayment,
                Installments = insts
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
        if (!CanManage(c.IsLead, PermissionNames.CustomersEdit)) return Forbid();
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
            Interest = c.Interest
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
            if (!CanManage(isLead, PermissionNames.CustomersEdit)) return Forbid();
        }
        await ValidateSalespersonAsync(model, ct);
        // Salesperson is optional, except a lead sourced from the "مندوب مبيعات" channel must name one.
        if (model.IsLead && model.Channel == LeadChannel.Salesperson && model.SalesPersonId is null)
            ModelState.AddModelError(nameof(model.SalesPersonId), "يجب اختيار المندوب عندما تكون القناة «مندوب مبيعات».");
        // Phone must be unique across customers.
        if (await _db.Customers.AnyAsync(c => c.Id != model.Id && c.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return PartialView("_CustomerForm", await FillAsync(model, ct));

        if (model.Id == Guid.Empty)
        {
            var c = new Customer
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
                c.CreatedAt = model.CreatedOn.Date == DateTime.Today ? DateTime.Now : model.CreatedOn.Date;
            ApplyChannel(c, model);
            _db.Customers.Add(c);
            if (model.IsLead)
            {
                _db.CustomerLogs.Add(new CustomerLog
                {
                    CustomerId = c.Id, Date = DateTime.Today, Kind = CustomerLogKind.Created,
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
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.IsLead && model.Id == Guid.Empty
            ? $"تمت إضافة العميل المحتمل «{model.FullName}»."
            : $"تم حفظ العميل «{model.FullName}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (!CanManage(c.IsLead, PermissionNames.CustomersDelete)) return Forbid();
        var wasLead = c.IsLead;
        _db.Customers.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف {(wasLead ? "العميل المحتمل" : "العميل")} «{c.FullName}».";
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
                CustomerId = c.Id, Date = DateTime.Today, Kind = CustomerLogKind.Conversion,
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
                CustomerId = c.Id, Date = DateTime.Today, Kind = CustomerLogKind.StatusChange,
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
