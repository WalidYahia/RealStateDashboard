using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;

namespace RealState.Web.Areas.CRM.Controllers;

/// <summary>
/// Leads = customers flagged as leads (potential customers). They are created/edited with the shared
/// customer form (flagged as a lead), managed through the customer profile (communication log, status,
/// conversion), and leave this list once converted to a customer.
/// </summary>
[Area("CRM")]
[Authorize(Policy = PermissionNames.LeadsAccessPolicy)]
public class LeadsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public LeadsController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(Guid? salespersonId, string? source, DateTime? from, DateTime? to, CancellationToken ct)
    {
        // Default the creation-date range to today on a fresh open (no query string).
        if (Request.Query.Count == 0) { from = DateTime.Today; to = DateTime.Today; }

        var allRows = await AllLeadRowsAsync(ct);
        var sourceLabels = allRows.Select(r => r.SourceLabel).Where(s => s != "—").Distinct().OrderBy(s => s).ToList();

        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        var salespersons = await _db.Employees
            .Where(e => e.IsActive && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName }).ToListAsync(ct);

        return View(new LeadListVm
        {
            Rows = ApplyFilter(allRows, salespersonId, source, from, to).ToList(),
            SalespersonId = salespersonId, Source = source, From = from, To = to,
            Salespersons = salespersons.Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToList(),
            Sources = sourceLabels.Select(s => new SelectListItem { Value = s, Text = s }).ToList()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid? salespersonId, string? source, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var rows = ApplyFilter(await AllLeadRowsAsync(ct), salespersonId, source, from, to).ToList();
        ViewBag.TenantId = _currentUser.TenantId;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Salesperson = salespersonId.HasValue
            ? await _db.Employees.Where(e => e.Id == salespersonId).Select(e => e.FullName).FirstOrDefaultAsync(ct)
            : null;
        ViewBag.Source = source;
        return View("PrintLeads", rows);
    }

    private async Task<List<LeadRow>> AllLeadRowsAsync(CancellationToken ct)
    {
        var salesNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var leads = await _db.Customers.Where(c => c.IsLead).OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var leadIds = leads.Select(c => c.Id).ToList();
        var counts = (await _db.CustomerLogs.Where(l => leadIds.Contains(l.CustomerId))
                .GroupBy(l => l.CustomerId).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.C);

        return leads.Select(c => new LeadRow
        {
            Id = c.Id, Name = c.FullName, Phone = c.Phone, CreatedOn = c.CreatedAt,
            SalespersonId = c.SalesPersonId, SourceLabel = c.SourceLabel(campNames),
            Salesperson = c.SalesPersonId.HasValue ? salesNames.GetValueOrDefault(c.SalesPersonId.Value, "—") : "—",
            Interest = c.Interest, LogCount = counts.GetValueOrDefault(c.Id, 0)
        }).ToList();
    }

    private static IEnumerable<LeadRow> ApplyFilter(IEnumerable<LeadRow> rows, Guid? salespersonId, string? source, DateTime? from, DateTime? to)
    {
        if (from.HasValue) rows = rows.Where(r => r.CreatedOn >= from.Value.Date);
        if (to.HasValue) rows = rows.Where(r => r.CreatedOn < to.Value.Date.AddDays(1));
        if (salespersonId.HasValue) rows = rows.Where(r => r.SalespersonId == salespersonId);
        if (!string.IsNullOrWhiteSpace(source)) rows = rows.Where(r => r.SourceLabel == source);
        return rows;
    }

    // Analytics landing page for the CRM section.
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var salesNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var leads = await _db.Customers.Where(c => c.IsLead).ToListAsync(ct);
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        return View(new CrmSummaryVm
        {
            TotalLeads = leads.Count,
            NewLeadsThisMonth = leads.Count(l => l.CreatedAt >= monthStart),
            TotalCustomers = await _db.Customers.CountAsync(c => !c.IsLead, ct),
            NewCustomersThisMonth = await _db.Customers.CountAsync(c => !c.IsLead && c.CreatedAt >= monthStart, ct),
            Salespersons = await _db.Employees.CountAsync(e => e.Type == EmployeeType.Salesperson, ct),
            BySource = leads.GroupBy(l => l.SourceLabel(campNames))
                .Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(r => r.Count).ToList(),
            BySalesperson = leads
                .GroupBy(l => l.SalesPersonId.HasValue ? salesNames.GetValueOrDefault(l.SalesPersonId.Value, "—") : "—")
                .Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(r => r.Count).ToList()
        });
    }
}
