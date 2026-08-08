using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Sales.Models;

namespace RealState.Web.Areas.Sales.Controllers;

[Area("Sales")]
[Authorize(Policy = PermissionNames.SalesView)]
public class SalesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public SalesController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    // Sales summary landing page (analytics cards + charts for all sales).
    public async Task<IActionResult> Summary(CancellationToken ct)
        => View(await BuildSummaryAsync(ct));

    [HttpGet]
    public async Task<IActionResult> SummaryPrint(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("SummaryPrint", await BuildSummaryAsync(ct));
    }

    private static readonly string[] ArMonths =
        { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private async Task<SalesSummaryVm> BuildSummaryAsync(CancellationToken ct)
    {
        var contracts = await _db.SaleContracts.Select(s => new { s.Id, s.ProjectId, s.TotalPrice, s.ContractDate }).ToListAsync(ct);
        var paidById = (await _db.Installments.GroupBy(i => i.SaleContractId)
            .Select(g => new { g.Key, Paid = g.Sum(x => x.PaidAmount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Paid);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        var totalValue = contracts.Sum(c => c.TotalPrice);
        // Down payment is scheduled as an installment now, so "collected" = paid installments only.
        var collected = paidById.Values.Sum();

        var byProject = contracts.GroupBy(c => c.ProjectId)
            .Select(g => new ProjectSales(projNames.GetValueOrDefault(g.Key, "—"), g.Count(), g.Sum(x => x.TotalPrice)))
            .OrderByDescending(x => x.Value).Take(6).ToList();

        var byMonth = contracts
            .GroupBy(c => new { c.ContractDate.Year, c.ContractDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlySales(ArMonths[g.Key.Month], g.Sum(x => x.TotalPrice)))
            .TakeLast(6).ToList();

        // Month-over-month figures (current vs previous calendar month).
        var now = DateTime.Today;
        var thisStart = new DateTime(now.Year, now.Month, 1);
        var nextStart = thisStart.AddMonths(1);
        var prevStart = thisStart.AddMonths(-1);
        decimal SalesIn(DateTime a, DateTime b) => contracts.Where(c => c.ContractDate >= a && c.ContractDate < b).Sum(c => c.TotalPrice);
        int CountIn(DateTime a, DateTime b) => contracts.Count(c => c.ContractDate >= a && c.ContractDate < b);

        var collectedThis = await _db.Installments.Where(i => i.PaidDate >= thisStart && i.PaidDate < nextStart).SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0;
        var collectedPrev = await _db.Installments.Where(i => i.PaidDate >= prevStart && i.PaidDate < thisStart).SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0;

        // Sales pipeline funnel from CRM leads (Lost is excluded from the funnel).
        var leadCounts = (await _db.Leads.GroupBy(l => l.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.C);
        var pipeline = new List<PipelineStage>
        {
            new("عملاء محتملين", leadCounts.GetValueOrDefault(LeadStatus.New)),
            new("تم التواصل", leadCounts.GetValueOrDefault(LeadStatus.Contacted)),
            new("مؤهل", leadCounts.GetValueOrDefault(LeadStatus.Qualified)),
            new("عرض / تفاوض", leadCounts.GetValueOrDefault(LeadStatus.Proposal)),
            new("تعاقد", leadCounts.GetValueOrDefault(LeadStatus.Won)),
        };

        // Latest contracts (stands in for the mockup's "آخر الحجوزات").
        var latestRaw = await _db.SaleContracts.OrderByDescending(s => s.ContractDate)
            .Select(s => new { s.Code, s.CustomerId, s.ProjectId, s.TotalPrice, s.ContractDate }).Take(6).ToListAsync(ct);
        var latest = latestRaw
            .Select(s => new RecentContract(s.Code, custNames.GetValueOrDefault(s.CustomerId, "—"), projNames.GetValueOrDefault(s.ProjectId, "—"), s.TotalPrice, s.ContractDate))
            .ToList();

        return new SalesSummaryVm
        {
            ContractsCount = contracts.Count,
            TotalValue = totalValue,
            TotalCollected = collected,
            TotalRemaining = totalValue - collected,
            CustomersCount = custNames.Count,
            SalespersonsCount = await _db.Employees.CountAsync(e => e.Type == EmployeeType.Salesperson, ct),
            UnitsSold = await _db.ProjectUnits.CountAsync(u => u.Status == UnitStatus.Sold, ct),
            SalesThisMonth = SalesIn(thisStart, nextStart),
            SalesPrevMonth = SalesIn(prevStart, thisStart),
            CollectedThisMonth = collectedThis,
            CollectedPrevMonth = collectedPrev,
            ContractsThisMonth = CountIn(thisStart, nextStart),
            ContractsPrevMonth = CountIn(prevStart, thisStart),
            NewCustomersThisMonth = await _db.Customers.CountAsync(c => c.CreatedAt >= thisStart && c.CreatedAt < nextStart, ct),
            NewCustomersPrevMonth = await _db.Customers.CountAsync(c => c.CreatedAt >= prevStart && c.CreatedAt < thisStart, ct),
            ByProject = byProject,
            ByMonth = byMonth,
            Pipeline = pipeline,
            Latest = latest,
        };
    }

    // The contracts list, with a contract-date range filter (defaults to today on a fresh open).
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var vm = new SalesListVm { Rows = await BuildRowsAsync(from, to, ct), From = from, To = to };

        // unit -> price map so the create modal can pre-fill the total from the chosen unit.
        // Materialize then key by ToString() client-side (lowercase) to match the unit option values.
        var priceUnits = await _db.ProjectUnits.Where(u => u.Status != UnitStatus.Sold)
            .Select(u => new { u.Id, u.Price }).ToListAsync(ct);
        ViewBag.UnitPrices = priceUnits.ToDictionary(u => u.Id.ToString(), u => u.Price);

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", new SalesListVm { Rows = await BuildRowsAsync(from, to, ct), From = from, To = to });
    }

    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesCreate)]
    public async Task<IActionResult> Form(CancellationToken ct)
        => PartialView("_SaleForm", await FillAsync(new SaleFormModel(), ct));

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SaleFormModel model, CancellationToken ct)
    {
        if (model.DownPayment > model.TotalPrice)
            ModelState.AddModelError(nameof(model.DownPayment), "المقدم لا يمكن أن يتجاوز السعر الإجمالي.");

        var unit = model.UnitId is null ? null : await _db.ProjectUnits.FirstOrDefaultAsync(u => u.Id == model.UnitId, ct);
        if (unit is null) ModelState.AddModelError(nameof(model.UnitId), "الوحدة غير موجودة.");
        else if (unit.Status == UnitStatus.Sold) ModelState.AddModelError(nameof(model.UnitId), "الوحدة مباعة بالفعل.");

        if (!ModelState.IsValid) return PartialView("_SaleForm", await FillAsync(model, ct));

        var contract = new SaleContract
        {
            Code = await NextCodeAsync(ct),
            CustomerId = model.CustomerId!.Value,
            ProjectId = unit!.ProjectId,   // derived from the chosen unit
            UnitId = model.UnitId!.Value,
            ContractDate = model.ContractDate,
            ReceiveDate = model.ReceiveDate,
            TotalPrice = model.TotalPrice,
            DownPayment = model.DownPayment,
            InstallmentsCount = model.InstallmentsCount,
            Step = model.Step,
            Notes = model.Notes
        };
        _db.SaleContracts.Add(contract);

        // The down payment is scheduled as installment #0 (due at the contract date) and collected
        // via "تحصيلات المشاريع" like any installment — it is NOT counted as paid up-front.
        if (model.DownPayment > 0)
        {
            _db.Installments.Add(new Installment
            {
                SaleContractId = contract.Id,
                Number = 0,
                DueDate = model.ContractDate,
                Amount = model.DownPayment
            });
        }

        // The remaining balance is split into the requested number of installments.
        var remaining = model.TotalPrice - model.DownPayment;
        if (model.InstallmentsCount > 0 && remaining > 0)
        {
            var per = Math.Round(remaining / model.InstallmentsCount, 2);
            var months = model.Step.Months();
            for (var i = 1; i <= model.InstallmentsCount; i++)
            {
                var amount = i == model.InstallmentsCount ? remaining - per * (model.InstallmentsCount - 1) : per;
                _db.Installments.Add(new Installment
                {
                    SaleContractId = contract.Id,
                    Number = i,
                    // First installment falls on the chosen "تاريخ أول قسط"; each next one steps by the period.
                    DueDate = model.FirstInstallmentDate.AddMonths(months * (i - 1)),
                    Amount = amount
                });
            }
        }

        unit!.Status = UnitStatus.Sold; // reserve the unit
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم إنشاء عقد البيع «{contract.Code}».";
        return Json(new { ok = true, redirect = Url.Action("Details", new { id = contract.Id }) });
    }

    public async Task<IActionResult> Details(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        ViewBag.Installments = await _db.Installments.Where(i => i.SaleContractId == id).OrderBy(i => i.Number).ToListAsync(ct);
        // Carry the caller's date filter so the "رجوع" link can return to the same filtered list.
        ViewBag.From = from;
        ViewBag.To = to;
        return View(c);
    }

    [HttpGet]
    public async Task<IActionResult> Contract(Guid id, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        ViewBag.Installments = await _db.Installments.Where(i => i.SaleContractId == id).OrderBy(i => i.Number).ToListAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Contract", c);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();

        var unit = await _db.ProjectUnits.FirstOrDefaultAsync(u => u.Id == c.UnitId, ct);
        if (unit is not null) unit.Status = UnitStatus.Available; // release the unit
        foreach (var inst in await _db.Installments.Where(i => i.SaleContractId == id).ToListAsync(ct))
            _db.Installments.Remove(inst);
        _db.SaleContracts.Remove(c);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف عقد البيع «{c.Code}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- helpers ----------
    private async Task<SaleContract?> LoadContractAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == c.CustomerId, ct);
        c.Project = await _db.Projects.FirstOrDefaultAsync(x => x.Id == c.ProjectId, ct);
        c.Unit = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == c.UnitId, ct);
        return c;
    }

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await _db.SaleContracts.Select(s => s.Code).ToListAsync(ct);
        var max = codes.Select(c => int.TryParse(c.Replace("S-", ""), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return "S-" + (max + 1).ToString("D4");
    }

    private async Task<List<SaleListItem>> BuildRowsAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var q = _db.SaleContracts.AsQueryable();
        if (from.HasValue) q = q.Where(s => s.ContractDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(s => s.ContractDate < to.Value.Date.AddDays(1));
        var contracts = await q.OrderByDescending(s => s.ContractDate).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);

        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);
        var paidById = (await _db.Installments.GroupBy(i => i.SaleContractId)
            .Select(g => new { g.Key, Paid = g.Sum(x => x.PaidAmount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Paid);

        return contracts.Select(s => new SaleListItem
        {
            Id = s.Id, Code = s.Code, ContractDate = s.ContractDate, ReceiveDate = s.ReceiveDate, TotalPrice = s.TotalPrice,
            Customer = custNames.GetValueOrDefault(s.CustomerId, "—"),
            Project = projNames.GetValueOrDefault(s.ProjectId, "—"),
            Unit = unitNames.GetValueOrDefault(s.UnitId, "—"),
            Paid = paidById.TryGetValue(s.Id, out var p) ? p : 0   // collected installments only (down payment is scheduled)
        }).ToList();
    }

    private async Task<SaleFormModel> FillAsync(SaleFormModel model, CancellationToken ct)
    {
        // Materialize first, then ToString() client-side so ids are lowercase and match the units'
        // data-project attribute (Guid.ToString() inside an EF query returns UPPERCASE on SQL Server).
        var customers = await _db.Customers.OrderBy(c => c.FullName).Select(c => new { c.Id, c.FullName }).ToListAsync(ct);
        model.Customers = customers.Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.FullName }).ToList();

        var projects = await _db.Projects.OrderBy(p => p.Name).Select(p => new { p.Id, p.Code, p.Name }).ToListAsync(ct);
        model.Projects = projects.Select(p => new SelectListItem { Value = p.Id.ToString(), Text = $"#{p.Code} {p.Name}" }).ToList();

        // Unsold units carrying their project id so the units list filters when a project is chosen.
        var units = await _db.ProjectUnits.Where(u => u.Status != UnitStatus.Sold)
            .Select(u => new { u.Id, u.ProjectId, u.Name, u.Number }).ToListAsync(ct);
        model.UnitOptions = units
            .Select(u => new SaleUnitOption(u.Id, u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), u.ProjectId))
            .ToList();
        return model;
    }
}
