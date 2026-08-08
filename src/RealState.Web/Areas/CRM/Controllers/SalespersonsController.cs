using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;

namespace RealState.Web.Areas.CRM.Controllers;

[Area("CRM")]
[Authorize(Policy = PermissionNames.SalespersonsView)]
public class SalespersonsController : Controller
{
    private readonly IApplicationDbContext _db;

    public SalespersonsController(IApplicationDbContext db) => _db = db;

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _db.Employees
            .Where(e => e.Type == EmployeeType.Salesperson)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);
        return View(list);
    }

    // Add (id null) or edit (id set) — shown in a modal popup.
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.SalespersonsCreate : PermissionNames.SalespersonsEdit)) return Forbid();
        if (id is null) return PartialView("_SalespersonForm", new SalespersonFormModel());
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id && x.Type == EmployeeType.Salesperson, ct);
        if (e is null) return NotFound();
        return PartialView("_SalespersonForm", new SalespersonFormModel { Id = e.Id, FullName = e.FullName, Phone = e.Phone ?? "", Email = e.Email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SalespersonFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.SalespersonsCreate : PermissionNames.SalespersonsEdit)) return Forbid();
        // Phone must be unique across employees.
        if (await _db.Employees.AnyAsync(e => e.Id != model.Id && e.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");

        if (!ModelState.IsValid) return PartialView("_SalespersonForm", model);

        if (model.Id == Guid.Empty)
        {
            // A salesperson is stored as an Employee (Type = Salesperson) for reuse by the HR module.
            _db.Employees.Add(new Employee
            {
                FullName = model.FullName,
                Phone = model.Phone,
                Email = model.Email,
                Type = EmployeeType.Salesperson,
                IsActive = true
            });
        }
        else
        {
            var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (e is null) return NotFound();
            e.FullName = model.FullName;
            e.Phone = model.Phone;
            e.Email = model.Email;
        }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ المندوب «{model.FullName}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalespersonsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var e = await _db.Employees.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();

        var inUse = await _db.Customers.AnyAsync(c => c.SalesPersonId == id, ct);
        if (inUse)
        {
            TempData["StatusMessage"] = "لا يمكن حذف المندوب لوجود عملاء مرتبطين به.";
            return RedirectToAction(nameof(Index));
        }

        _db.Employees.Remove(e);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المندوب «{e.FullName}».";
        return RedirectToAction(nameof(Index));
    }
}
