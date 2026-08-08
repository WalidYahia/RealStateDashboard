using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Application.Activity;

public sealed class ActivityLogger : IActivityLogger
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public ActivityLogger(IApplicationDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task LogAsync(ActivityEntry e, CancellationToken ct = default)
    {
        var log = new ActivityLog
        {
            // Explicit TenantId (e.g. at login, before the tenant claim exists) wins; otherwise the
            // SaveChanges override stamps the current tenant.
            TenantId = e.TenantId ?? Guid.Empty,
            UserId = e.UserId ?? _currentUser.UserId,
            UserName = e.UserName ?? _currentUser.UserName ?? "—",
            ActionType = e.ActionType,
            Area = e.Area,
            Controller = e.Controller,
            Action = e.Action,
            Method = e.Method,
            Path = e.Path,
            Description = e.Description,
            IpAddress = e.IpAddress,
            Timestamp = _clock.Now,
        };

        _db.ActivityLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }
}
