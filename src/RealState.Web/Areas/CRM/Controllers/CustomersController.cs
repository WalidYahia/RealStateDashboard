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
[Authorize(Policy = PermissionNames.CustomersView)]
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

    // ---------- Account statement ----------
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        ViewBag.SalesName = customer.SalesPersonId is null ? null
            : await _db.Employees.Where(e => e.Id == customer.SalesPersonId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
        return View(await BuildStatementAsync(customer, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintStatement(Guid id, CancellationToken ct)
    {
        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintStatement", await BuildStatementAsync(customer, ct));
    }

    // Printable paid receipt for a settled installment.
    [HttpGet]
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

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var customers = await _db.Customers.OrderBy(c => c.FullName).ToListAsync(ct);
        ViewBag.SalesNames = await _db.Employees
            .Where(e => e.Type == EmployeeType.Salesperson)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return View(customers);
    }

    // Add (id null) or edit (id set) — shown in a modal popup.
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.CustomersCreate : PermissionNames.CustomersEdit)) return Forbid();
        if (id is null) return PartialView("_CustomerForm", await FillAsync(new CustomerFormModel(), ct));
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return PartialView("_CustomerForm", await FillAsync(new CustomerFormModel
        {
            Id = c.Id,
            FullName = c.FullName,
            Phone = c.Phone ?? "",
            Email = c.Email,
            Source = c.Source,
            SalesPersonId = c.SalesPersonId,
            Notes = c.Notes
        }, ct));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(CustomerFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.CustomersCreate : PermissionNames.CustomersEdit)) return Forbid();
        await ValidateSalespersonAsync(model, ct);
        // Phone must be unique across customers.
        if (await _db.Customers.AnyAsync(c => c.Id != model.Id && c.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return PartialView("_CustomerForm", await FillAsync(model, ct));

        if (model.Id == Guid.Empty)
        {
            _db.Customers.Add(new Customer
            {
                FullName = model.FullName,
                Phone = model.Phone,
                Email = model.Email,
                Source = model.Source,
                SalesPersonId = model.SalesPersonId,
                Notes = model.Notes
            });
        }
        else
        {
            var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (c is null) return NotFound();
            c.FullName = model.FullName;
            c.Phone = model.Phone;
            c.Email = model.Email;
            c.Source = model.Source;
            c.SalesPersonId = model.SalesPersonId;
            c.Notes = model.Notes;
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ العميل «{model.FullName}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CustomersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        _db.Customers.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف العميل «{c.FullName}».";
        return RedirectToAction(nameof(Index));
    }

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
        model.SalesPersons = await _db.Employees
            .Where(e => e.IsActive && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .OrderBy(e => e.FullName)
            .Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName })
            .ToListAsync(ct);
        return model;
    }
}
