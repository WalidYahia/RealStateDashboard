using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Web.Components;

/// <summary>Renders the current user's open assigned-task count as a bell badge in the top bar.</summary>
public class TaskAlertsViewComponent : ViewComponent
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public TaskAlertsViewComponent(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var uid = _currentUser.UserId;
        if (uid is null) return View(0);

        var myEmpIds = await _db.Employees.Where(e => e.UserId == uid).Select(e => e.Id).ToListAsync();
        if (myEmpIds.Count == 0) return View(0);

        var count = await _db.WorkTasks
            .Where(t => myEmpIds.Contains(t.AssigneeEmployeeId) && t.Status != WorkTaskStatus.Completed)
            .CountAsync();
        return View(count);
    }
}
