using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Interfaces;

namespace RealState.Web.Areas.CRM.Controllers;

[Area("CRM")]
[Authorize(Policy = PermissionNames.SalespersonsView)]
public class SalespersonsController : Controller
{
    private readonly IApplicationDbContext _db;

    public SalespersonsController(IApplicationDbContext db) => _db = db;

    // View-only directory of salespersons. A salesperson is an employee whose job role is flagged
    // "مندوب مبيعات" (managed from HR). Creating/editing/deleting happens in the HR → Employees page.
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        var list = await _db.Employees
            .Where(e => e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);
        ViewBag.DeptNames = await _db.Departments.ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        ViewBag.RoleNames = await _db.JobRoles.ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return View(list);
    }
}
