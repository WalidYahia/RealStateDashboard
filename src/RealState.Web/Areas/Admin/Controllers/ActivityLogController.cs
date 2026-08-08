using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Activity;
using RealState.Application.Common;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Admin.Models;

namespace RealState.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = PermissionNames.ActivityLogView)]
public class ActivityLogController : Controller
{
    private const int MaxRows = 1000;

    private readonly IApplicationDbContext _db;
    public ActivityLogController(IApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? userId, string? actionType, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);

        var q = _db.ActivityLogs.AsQueryable();
        if (from.HasValue) q = q.Where(l => l.Timestamp >= from.Value.Date);
        if (to.HasValue) q = q.Where(l => l.Timestamp < to.Value.Date.AddDays(1));
        if (userId is { } uid && uid != Guid.Empty) q = q.Where(l => l.UserId == uid);
        if (!string.IsNullOrWhiteSpace(actionType)) q = q.Where(l => l.ActionType == actionType);

        var rows = await q.OrderByDescending(l => l.Timestamp).Take(MaxRows).ToListAsync(ct);

        // Distinct users that appear in the log, for the filter dropdown.
        var users = (await _db.ActivityLogs.Where(l => l.UserId != null)
                .Select(l => new { l.UserId, l.UserName }).Distinct().ToListAsync(ct))
            .OrderBy(u => u.UserName)
            .Select(u => new SelectListItem { Value = u.UserId!.ToString(), Text = u.UserName, Selected = u.UserId == userId })
            .ToList();

        var actionTypes = ActivityActionType.All
            .Select(t => new SelectListItem { Value = t, Text = ActivityActionType.Ar(t), Selected = t == actionType })
            .ToList();

        return View(new ActivityLogVm
        {
            Rows = rows,
            From = from,
            To = to,
            UserId = userId,
            ActionType = actionType,
            Users = users,
            ActionTypes = actionTypes,
        });
    }
}
