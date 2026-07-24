namespace RealState.Application.Common;

/// <summary>
/// Central catalog of permission strings. Used to register authorization policies and to seed the
/// SuperAdmin role with every permission. Grouped by module.
/// </summary>
public static class PermissionNames
{
    public const string DashboardView = "Dashboard.View";

    // Admin / security
    public const string TenantsManage = "Tenants.Manage"; // SuperAdmin only — create/manage tenants
    public const string UsersManage = "Users.Manage";
    public const string RolesManage = "Roles.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AuditLogsView = "AuditLogs.View";

    // Sales
    public const string SalesView = "Sales.View";
    public const string SalesCreate = "Sales.Create";
    public const string SalesEdit = "Sales.Edit";
    public const string SalesDelete = "Sales.Delete";

    // Purchases
    public const string PurchasesView = "Purchases.View";
    public const string PurchasesCreate = "Purchases.Create";
    public const string PurchasesEdit = "Purchases.Edit";
    public const string PurchasesDelete = "Purchases.Delete";

    // Finance
    public const string FinanceView = "Finance.View";
    public const string FinanceCreate = "Finance.Create";
    public const string FinanceEdit = "Finance.Edit";
    public const string FinanceDelete = "Finance.Delete";

    // CRM
    public const string CrmView = "CRM.View";
    public const string CrmCreate = "CRM.Create";
    public const string CrmEdit = "CRM.Edit";
    public const string CrmDelete = "CRM.Delete";

    // HR
    public const string HrView = "HR.View";
    public const string HrCreate = "HR.Create";
    public const string HrEdit = "HR.Edit";
    public const string HrDelete = "HR.Delete";

    /// <summary>Every permission string, for policy registration and SuperAdmin seeding.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        DashboardView,
        TenantsManage, UsersManage, RolesManage, SettingsManage, AuditLogsView,
        SalesView, SalesCreate, SalesEdit, SalesDelete,
        PurchasesView, PurchasesCreate, PurchasesEdit, PurchasesDelete,
        FinanceView, FinanceCreate, FinanceEdit, FinanceDelete,
        CrmView, CrmCreate, CrmEdit, CrmDelete,
        HrView, HrCreate, HrEdit, HrDelete,
    };

    /// <summary>Permissions granted to a TenantAdmin: everything except cross-tenant management.</summary>
    public static IReadOnlyList<string> ForTenantAdmin { get; } =
        All.Where(p => p != TenantsManage).ToArray();
}
