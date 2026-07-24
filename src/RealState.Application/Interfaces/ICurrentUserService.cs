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
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateTime Today { get; }
}
