namespace RealState.Application.Common;

/// <summary>Seed defaults and well-known ids shared across layers.</summary>
public static class AppConstants
{
    // Deterministic id so seeded rows and query filters stay stable across runs/migrations.
    public static readonly Guid DefaultTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public const string DefaultTenantName = "SuperTenant";

    public const string SuperAdminRole = "SuperAdmin";
    public const string TenantAdminRole = "TenantAdmin";

    public const string DefaultAdminUserName = "admin";
    public const string DefaultAdminEmail = "admin@realstate.local";
    public const string DefaultAdminPassword = "ChangeMe123!";
}
