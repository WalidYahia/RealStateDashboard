using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;

namespace RealState.Web.Components;

public record ProjectMenuItem(Guid Id, string Code, string Name);

/// <summary>Renders the current tenant's projects as sidebar sub-links (tree under المشاريع).</summary>
public class ProjectsMenuViewComponent : ViewComponent
{
    private readonly IApplicationDbContext _db;
    public ProjectsMenuViewComponent(IApplicationDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var items = await _db.Projects
            .OrderBy(p => p.Code)
            .Select(p => new ProjectMenuItem(p.Id, p.Code, p.Name))
            .Take(200)
            .ToListAsync();
        return View(items);
    }
}
