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

    // Dummy password given to the login account auto-created for a new employee. The employee cannot
    // do anything until an admin sets a real password and grants permissions.
    public const string DefaultEmployeePassword = "Welcome@123";

    // Static "host" super-user — not stored in the database. Signs in with a cookie that carries
    // every permission, then picks a tenant to operate within. Override in config if needed.
    public const string HostUserName = "syncro";
    public const string HostPassword = "wwyy_0106116";
    public const string HostClaimType = "host";
    public const string TenantNameClaimType = "tenant_name";
}
