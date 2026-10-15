namespace RealState.Application.Interfaces;

/// <summary>Ambient information about the caller, resolved from the HTTP context / claims.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }

    /// <summary>Tenant the current request operates within. Falls back to the default tenant when unauthenticated.</summary>
    Guid TenantId { get; }
}

/// <summary>Abstraction over the system clock so time-dependent logic stays testable.</summary>
/// <summary>
/// The clock. Moments (journal entries, vouchers, invoices, logs, audit fields) are stored in UTC; <see cref="Zone"/> is
/// the user's time zone (the browser's, in the web app) that <see cref="Now"/> / <see cref="Today"/> and the display use.
/// </summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    /// <summary>Now in <see cref="Zone"/>.</summary>
    DateTime Now { get; }
    /// <summary>Today in <see cref="Zone"/>.</summary>
    DateTime Today { get; }
    TimeZoneInfo Zone { get; }
}
