using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;

namespace RealState.Web.Areas.Admin.Models;

public class ActivityLogVm
{
    public List<ActivityLog> Rows { get; set; } = new();

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? UserId { get; set; }
    public string? ActionType { get; set; }

    public List<SelectListItem> Users { get; set; } = new();
    public List<SelectListItem> ActionTypes { get; set; } = new();
}
