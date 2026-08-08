using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;

namespace RealState.Web.Areas.CRM.Controllers;

[Area("CRM")]
[Authorize(Policy = PermissionNames.CustomersView)]
public class LeadsController : Controller
{
    private readonly IApplicationDbContext _db;

    public LeadsController(IApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var leads = await _db.Leads.OrderByDescending(l => l.CreatedAt).ToListAsync(ct);
        return View(leads);
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersCreate)]
    public IActionResult Create() => View(new LeadFormModel());

    [HttpPost]
    [Authorize(Policy = PermissionNames.CustomersCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeadFormModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        _db.Leads.Add(new Lead
        {
            FullName = model.FullName,
            Phone = model.Phone,
            Source = model.Source,
            Status = model.Status,
            EstimatedValue = model.EstimatedValue
        });
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تمت إضافة العميل المحتمل «{model.FullName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.CustomersEdit)]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();

        return View(new LeadFormModel
        {
            Id = l.Id,
            FullName = l.FullName,
            Phone = l.Phone,
            Source = l.Source,
            Status = l.Status,
            EstimatedValue = l.EstimatedValue
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CustomersEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(LeadFormModel model, CancellationToken ct)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (l is null) return NotFound();
        if (!ModelState.IsValid) return View(model);

        l.FullName = model.FullName;
        l.Phone = model.Phone;
        l.Source = model.Source;
        l.Status = model.Status;
        l.EstimatedValue = model.EstimatedValue;
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم تحديث العميل المحتمل «{model.FullName}».";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CustomersDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var l = await _db.Leads.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return NotFound();

        _db.Leads.Remove(l);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف العميل المحتمل «{l.FullName}».";
        return RedirectToAction(nameof(Index));
    }
}
