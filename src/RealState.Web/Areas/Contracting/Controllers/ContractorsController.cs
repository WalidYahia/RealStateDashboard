using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Contracting.Models;

namespace RealState.Web.Areas.Contracting.Controllers;

[Area("Contracting")]
[Authorize(Policy = PermissionNames.ContractingView)]
public class ContractorsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ContractorsController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await _db.Contractors.OrderBy(c => c.Name).ToListAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.ContractingCreate : PermissionNames.ContractingEdit)) return Forbid();
        if (id is null) return PartialView("_ContractorForm", new ContractorFormModel());
        var c = await _db.Contractors.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return PartialView("_ContractorForm", new ContractorFormModel
        {
            Id = c.Id, Name = c.Name, Phone = c.Phone ?? "", Email = c.Email, Notes = c.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(ContractorFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.ContractingCreate : PermissionNames.ContractingEdit)) return Forbid();
        if (await _db.Contractors.AnyAsync(c => c.Id != model.Id && c.Phone == model.Phone, ct))
            ModelState.AddModelError(nameof(model.Phone), "رقم الهاتف مستخدم بالفعل.");
        if (!ModelState.IsValid) return PartialView("_ContractorForm", model);

        if (model.Id == Guid.Empty)
            _db.Contractors.Add(new Contractor { Name = model.Name, Phone = model.Phone, Email = model.Email, Notes = model.Notes });
        else
        {
            var c = await _db.Contractors.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (c is null) return NotFound();
            c.Name = model.Name; c.Phone = model.Phone; c.Email = model.Email; c.Notes = model.Notes;
        }
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حفظ المقاول «{model.Name}».";
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.ContractingDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.Contractors.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (await _db.WorkOrders.AnyAsync(o => o.ContractorId == id, ct) ||
            await _db.WorkOrderPayments.AnyAsync(p => p.ContractorId == id, ct))
        {
            TempData["ErrorMessage"] = "لا يمكن حذف مقاول لديه أوامر شغل أو مدفوعات.";
            return RedirectToAction(nameof(Index));
        }
        _db.Contractors.Remove(c);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف المقاول «{c.Name}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Account statement (كشف حساب مقاول) ----------
    public async Task<IActionResult> Details(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var contractor = await _db.Contractors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contractor is null) return NotFound();
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        ViewData["CanPay"] = Can(PermissionNames.ContractingPay);
        return View(await BuildStatementAsync(contractor, from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", await _db.Contractors.OrderBy(c => c.Name).ToListAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> PrintStatement(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var contractor = await _db.Contractors.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contractor is null) return NotFound();
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintStatement", await BuildStatementAsync(contractor, from, to, ct));
    }

    private async Task<ContractorStatementVm> BuildStatementAsync(Contractor contractor, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var orders = await _db.WorkOrders.Where(o => o.ContractorId == contractor.Id).ToListAsync(ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var payments = await _db.WorkOrderPayments.Where(p => p.ContractorId == contractor.Id).ToListAsync(ct);

        var rows = new List<ContractorLedgerRow>();
        foreach (var o in orders)
            rows.Add(new ContractorLedgerRow
            {
                Kind = ContractorLedgerKind.Order, Id = o.Id, Number = o.Number, Date = o.OrderDate,
                Project = projNames.GetValueOrDefault(o.ProjectId, "—"),
                Statement = o.ItemDescription, Unit = o.Unit, Quantity = o.Quantity, Rate = o.Rate,
                ExecutionPercent = o.ExecutionPercent, Due = o.Due, UpliftPercent = o.UpliftPercent,
                Deductions = o.Deductions, ActualTotal = o.ActualTotal, Notes = o.Notes
            });
        foreach (var p in payments)
            rows.Add(new ContractorLedgerRow
            {
                Kind = ContractorLedgerKind.Payment, Id = p.Id, Number = p.ReceiptNo, Date = p.PaidDate,
                Statement = $"إيصال صرف نقدية رقم {p.ReceiptNo:D5}", Payment = p.Amount
            });

        // Chronological running balance (owed to contractor): orders before payments on the same date.
        var ordered = rows.OrderBy(r => r.Date).ThenBy(r => r.Kind == ContractorLedgerKind.Order ? 0 : 1).ToList();
        decimal running = 0;
        foreach (var r in ordered)
        {
            running += r.Kind == ContractorLedgerKind.Order ? r.ActualTotal : -r.Payment;
            r.Balance = running;
        }

        bool InRange(DateTime d) => (from is null || d >= from) && (to is null || d < to.Value.Date.AddDays(1));
        decimal closing = to is null ? running
            : ordered.Where(r => r.Date < to.Value.Date.AddDays(1)).Select(r => r.Balance).DefaultIfEmpty(0m).Last();

        var paidByOrder = payments.Where(p => p.WorkOrderId.HasValue)
            .GroupBy(p => p.WorkOrderId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        var hasPayable = orders.Any(o => o.ActualTotal - paidByOrder.GetValueOrDefault(o.Id, 0) > 0);

        return new ContractorStatementVm
        {
            Contractor = contractor, From = from, To = to,
            TotalObligations = orders.Sum(o => o.ActualTotal),
            TotalPaid = payments.Sum(p => p.Amount),
            OrdersCount = orders.Count, PaymentsCount = payments.Count,
            HasPayableOrders = hasPayable,
            Rows = ordered.Where(r => InRange(r.Date)).ToList(),
            ClosingBalance = closing
        };
    }
}
