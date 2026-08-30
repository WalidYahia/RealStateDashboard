using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Reports.Models;

namespace RealState.Web.Areas.Reports.Controllers;

[Area("Reports")]
[Authorize(Policy = PermissionNames.ReportsProjects)]
public class ProjectsReportController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ProjectsReportController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
        => View(await BuildAsync(ct));

    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", await BuildAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> Csv(CancellationToken ct)
    {
        var vm = await BuildAsync(ct);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var headers = new[] { "الكود", "المشروع", "النوع", "إجمالي الوحدات", "وحدات مباعة", "وحدات غير مباعة", "المصروفات", "الإيرادات", "قيمة المخزون" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<string?>)new[]
        {
            r.Code, r.Name, r.TypeName, r.UnitsTotal.ToString(), r.UnitsSold.ToString(), r.UnitsUnsold.ToString(),
            r.Expenses.ToString("0.##", inv), r.Incomes.ToString("0.##", inv), r.InventoryValue.ToString("0.##", inv)
        });
        return RealState.Web.Common.Csv.File($"projects-report-{DateTime.Now:yyyyMMdd-HHmm}.csv", headers, rows);
    }

    private async Task<ProjectReportVm> BuildAsync(CancellationToken ct)
    {
        var projects = await _db.Projects.OrderBy(p => p.Code).ToListAsync(ct);
        var ids = projects.Select(p => p.Id).ToList();
        var typeNames = await _db.ProjectTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        // Units per project: totals, sold count, and unsold inventory value (sum of prices of non-sold units).
        var unitStats = (await _db.ProjectUnits.Where(u => ids.Contains(u.ProjectId))
            .GroupBy(u => u.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Total = g.Count(),
                Sold = g.Count(x => x.Status == UnitStatus.Sold),
                Inventory = g.Where(x => x.Status != UnitStatus.Sold).Sum(x => (decimal?)x.Price) ?? 0m
            }).ToListAsync(ct))
            .ToDictionary(x => x.ProjectId);

        // Expenses charged directly to each project (manual project expenses + supplier-order payments),
        // matching the project details page.
        var expenseByProject = (await _db.SafeTransactions
            .Where(t => t.Type == TxnType.Expense && t.ProjectId != null && ids.Contains(t.ProjectId!.Value))
            .GroupBy(t => t.ProjectId!.Value)
            .Select(g => new { ProjectId = g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.ProjectId, x => x.Sum);

        // Incomes: installment collections are linked to an Installment, not a project — resolve them
        // via Installment → SaleContract → Project. Also honour any income tagged directly with a project.
        var incomeTxns = await _db.SafeTransactions
            .Where(t => t.Type == TxnType.Income && (t.ProjectId != null || t.InstallmentId != null))
            .Select(t => new { t.Amount, t.ProjectId, t.InstallmentId }).ToListAsync(ct);

        var instIds = incomeTxns.Where(t => t.InstallmentId != null).Select(t => t.InstallmentId!.Value).Distinct().ToList();
        var instToContract = await _db.Installments.Where(i => instIds.Contains(i.Id))
            .Select(i => new { i.Id, i.SaleContractId }).ToListAsync(ct);
        var contractIds = instToContract.Select(i => i.SaleContractId).Distinct().ToList();
        var contractToProject = (await _db.SaleContracts.Where(c => contractIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ProjectId }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.ProjectId);
        var instToProject = instToContract
            .Where(i => contractToProject.ContainsKey(i.SaleContractId))
            .ToDictionary(i => i.Id, i => contractToProject[i.SaleContractId]);

        var incomeByProject = new Dictionary<Guid, decimal>();
        foreach (var t in incomeTxns)
        {
            var pid = t.ProjectId
                ?? (t.InstallmentId is Guid iid && instToProject.TryGetValue(iid, out var mapped) ? mapped : (Guid?)null);
            if (pid is Guid p) incomeByProject[p] = incomeByProject.GetValueOrDefault(p) + t.Amount;
        }

        var vm = new ProjectReportVm
        {
            Rows = projects.Select(p =>
            {
                unitStats.TryGetValue(p.Id, out var u);
                var total = u?.Total ?? 0;
                var sold = u?.Sold ?? 0;
                return new ProjectReportRow
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    TypeName = p.ProjectTypeId is Guid tid && typeNames.TryGetValue(tid, out var n) ? n : p.Type.Ar(),
                    UnitsTotal = total,
                    UnitsSold = sold,
                    UnitsUnsold = total - sold,
                    InventoryValue = u?.Inventory ?? 0m,
                    Expenses = expenseByProject.GetValueOrDefault(p.Id),
                    Incomes = incomeByProject.GetValueOrDefault(p.Id)
                };
            }).ToList()
        };
        return vm;
    }
}
