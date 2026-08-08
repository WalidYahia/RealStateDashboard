using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Sales.Models;

namespace RealState.Web.Areas.Sales.Controllers;

[Area("Sales")]
[Authorize(Policy = PermissionNames.CollectionsView)]
public class CollectionsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAccountingService _accounting;

    public CollectionsController(IApplicationDbContext db, ICurrentUserService currentUser, IAccountingService accounting)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
    }

    // ---------- Page shell + status cards ----------
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await BuildRowsAsync(ct);
        ViewBag.Safes = await _db.Safes.Where(s => s.IsActive)
            .OrderBy(s => s.Name).Select(s => new { id = s.Id, name = s.Name }).ToListAsync(ct);
        var today = DateTime.Today;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var outstanding = rows.Where(r => r.Status != InstallmentStatus.Paid).ToList();
        var vm = new CollectionsVm
        {
            TotalOutstanding = outstanding.Sum(r => r.Amount - r.PaidAmount),
            OverdueCount = outstanding.Count(r => r.Status == InstallmentStatus.Overdue),
            OverdueAmount = outstanding.Where(r => r.Status == InstallmentStatus.Overdue).Sum(r => r.Amount - r.PaidAmount),
            DueCount = outstanding.Count,
            CollectedThisMonth = rows.Where(r => r.PaidDate != null && r.PaidDate >= monthStart).Sum(r => r.PaidAmount),
        };
        return View(vm);
    }

    // ---------- Tabs (AJAX partials) ----------
    // Currently due = unpaid & due today or earlier.
    public async Task<IActionResult> DueNow(CancellationToken ct)
    {
        var today = DateTime.Today;
        var rows = (await BuildRowsAsync(ct))
            .Where(r => r.Status != InstallmentStatus.Paid && r.DueDate.Date <= today)
            .OrderBy(r => r.DueDate).ToList();
        ViewData["Payable"] = true;
        return PartialView("_CollectionList", rows);
    }

    // Due within the next 7 days (not yet due today).
    public async Task<IActionResult> DueThisWeek(CancellationToken ct)
    {
        var today = DateTime.Today;
        var end = today.AddDays(7);
        var rows = (await BuildRowsAsync(ct))
            .Where(r => r.Status != InstallmentStatus.Paid && r.DueDate.Date > today && r.DueDate.Date <= end)
            .OrderBy(r => r.DueDate).ToList();
        ViewData["Payable"] = true;
        return PartialView("_CollectionList", rows);
    }

    // All installments, grouped + sortable + searchable.
    public async Task<IActionResult> All(string? sort, CancellationToken ct)
    {
        ViewData["Sort"] = sort ?? "unit";
        ViewData["Mode"] = "all";
        return PartialView("_CollectionGroups", Group(await BuildRowsAsync(ct), sort));
    }

    // Collected (paid) installments, grouped + sortable + searchable + cancelable.
    public async Task<IActionResult> Collected(string? sort, CancellationToken ct)
    {
        ViewData["Sort"] = sort ?? "unit";
        ViewData["Mode"] = "collected";
        var rows = (await BuildRowsAsync(ct)).Where(r => r.Status == InstallmentStatus.Paid).ToList();
        return PartialView("_CollectionGroups", Group(rows, sort));
    }

    // ---------- Actions ----------
    [HttpPost]
    [Authorize(Policy = PermissionNames.CollectionsCollect)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(Guid id, Guid safeId, CancellationToken ct)
    {
        var inst = await _db.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inst is null) return NotFound();
        if (safeId == Guid.Empty || !await _db.Safes.AnyAsync(s => s.Id == safeId && s.IsActive, ct))
        {
            TempData["StatusMessage"] = "يجب اختيار خزنة صالحة لاستلام التحصيل.";
            return RedirectToAction(nameof(Index));
        }
        if (inst.PaidAmount < inst.Amount)
        {
            inst.PaidAmount = inst.Amount;
            inst.PaidDate = DateTime.Today;
            var maxNo = await _db.Installments.MaxAsync(i => (int?)i.ReceiptNo, ct) ?? 0;
            inst.ReceiptNo = maxNo + 1;

            // Auto-record the collection as an income movement on the chosen safe.
            var contract = await _db.SaleContracts.FirstOrDefaultAsync(s => s.Id == inst.SaleContractId, ct);
            var custName = "—";
            if (contract != null)
            {
                var cust = await _db.Customers.FirstOrDefaultAsync(c => c.Id == contract.CustomerId, ct);
                custName = cust?.FullName ?? "—";
            }
            await _accounting.AddTransactionAsync(safeId, TxnType.Income, TxnSource.Collection, inst.Amount,
                DateTime.Now, $"تحصيل {(inst.Number <= 0 ? "المقدم" : $"قسط رقم {inst.Number}")} — {custName} (إيصال C-{inst.ReceiptNo:D5})",
                installmentId: inst.Id, ct: ct);

            await _db.SaveChangesAsync(ct);
        }
        return RedirectToAction(nameof(Receipt), new { id });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CollectionsCancel)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelPayment(Guid id, CancellationToken ct)
    {
        var inst = await _db.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inst is null) return NotFound();
        inst.PaidAmount = 0;
        inst.PaidDate = null;
        inst.ReceiptNo = null;
        await _accounting.RemoveByInstallmentAsync(inst.Id, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "تم إلغاء التحصيل وعكس الإيراد المرتبط به.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken ct)
    {
        var inst = await _db.Installments.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inst is null) return NotFound();
        var contract = await _db.SaleContracts.FirstOrDefaultAsync(s => s.Id == inst.SaleContractId, ct);
        ViewBag.Contract = contract;
        ViewBag.Customer = contract is null ? null : await _db.Customers.FirstOrDefaultAsync(c => c.Id == contract.CustomerId, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Receipt", inst);
    }

    [HttpGet]
    public async Task<IActionResult> PrintAll(string? sort, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintAll", (await BuildRowsAsync(ct)).OrderBy(r => r.CustomerName).ThenBy(r => r.DueDate).ToList());
    }

    // ---------- helpers ----------
    private static List<IGrouping<string, CollectionRow>> Group(List<CollectionRow> rows, string? sort) => sort switch
    {
        "customer" => rows.OrderBy(r => r.CustomerName).ThenBy(r => r.DueDate).GroupBy(r => r.CustomerName).ToList(),
        "project" => rows.OrderBy(r => r.ProjectName).ThenBy(r => r.DueDate).GroupBy(r => r.ProjectName).ToList(),
        _ => rows.OrderBy(r => r.UnitLabel).ThenBy(r => r.DueDate).GroupBy(r => r.UnitLabel).ToList(),
    };

    private async Task<List<CollectionRow>> BuildRowsAsync(CancellationToken ct)
    {
        var installments = await _db.Installments.ToListAsync(ct);
        var contracts = await _db.SaleContracts.ToDictionaryAsync(s => s.Id, ct);
        var customers = await _db.Customers.ToDictionaryAsync(c => c.Id, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);

        return installments.Select(i =>
        {
            contracts.TryGetValue(i.SaleContractId, out var c);
            Customer? cust = null;
            if (c != null) customers.TryGetValue(c.CustomerId, out cust);
            return new CollectionRow
            {
                InstallmentId = i.Id,
                ContractId = i.SaleContractId,
                ContractCode = c?.Code ?? "—",
                CustomerName = cust?.FullName ?? "—",
                CustomerPhone = cust?.Phone,
                ProjectName = c != null ? projNames.GetValueOrDefault(c.ProjectId, "—") : "—",
                UnitLabel = c != null ? unitNames.GetValueOrDefault(c.UnitId, "—") : "—",
                Number = i.Number,
                DueDate = i.DueDate,
                Amount = i.Amount,
                PaidAmount = i.PaidAmount,
                PaidDate = i.PaidDate,
                ReceiptNo = i.ReceiptNo,
                Status = i.Status
            };
        }).ToList();
    }
}
