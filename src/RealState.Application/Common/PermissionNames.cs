namespace RealState.Application.Common;

/// <summary>Metadata for a single permission: its stable name, Arabic label and the Arabic group it belongs to.</summary>
public sealed record PermissionInfo(string Name, string Display, string Group);

/// <summary>
/// Central catalog of granular permissions. Each permission is one authorization policy and one
/// assignable privilege (checkbox) on the user screen. Grouped by feature so the UI can render
/// sections. The SuperAdmin role is seeded with every permission.
/// </summary>
public static class PermissionNames
{
    // ----- Home / dashboard -----
    public const string DashboardView = "Dashboard.View";

    // ----- Admin / security -----
    public const string TenantsManage = "Tenants.Manage";   // SuperAdmin only — create/manage tenants
    public const string SettingsManage = "Settings.Manage";

    public const string UsersView = "Users.View";
    public const string UsersCreate = "Users.Create";
    public const string UsersEdit = "Users.Edit";
    public const string UsersDelete = "Users.Delete";

    public const string ActivityLogView = "ActivityLog.View";

    // ----- Projects (projects + units + stages) -----
    public const string ProjectsView = "Projects.View";
    public const string ProjectsCreate = "Projects.Create";
    public const string ProjectsEdit = "Projects.Edit";
    public const string ProjectsDelete = "Projects.Delete";

    // ----- Sales contracts -----
    public const string SalesView = "Sales.View";
    public const string SalesCreate = "Sales.Create";
    public const string SalesDelete = "Sales.Delete";

    // ----- Collections -----
    public const string CollectionsView = "Collections.View";
    public const string CollectionsCollect = "Collections.Collect";
    public const string CollectionsCancel = "Collections.Cancel";

    // ----- Accounting: safes -----
    public const string SafesView = "Safes.View";
    public const string SafesCreate = "Safes.Create";
    public const string SafesEdit = "Safes.Edit";
    public const string SafesDelete = "Safes.Delete";

    // ----- Accounting: expenses -----
    public const string ExpensesView = "Expenses.View";
    public const string ExpensesCreate = "Expenses.Create";
    public const string ExpensesEdit = "Expenses.Edit";
    public const string ExpensesDelete = "Expenses.Delete";

    // ----- Accounting: incomes -----
    public const string IncomesView = "Incomes.View";
    public const string IncomesCreate = "Incomes.Create";
    public const string IncomesEdit = "Incomes.Edit";
    public const string IncomesDelete = "Incomes.Delete";

    // ----- CRM: customers -----
    public const string CustomersView = "Customers.View";
    public const string CustomersCreate = "Customers.Create";
    public const string CustomersEdit = "Customers.Edit";
    public const string CustomersDelete = "Customers.Delete";

    // ----- CRM: salespersons -----
    public const string SalespersonsView = "Salespersons.View";
    public const string SalespersonsCreate = "Salespersons.Create";
    public const string SalespersonsEdit = "Salespersons.Edit";
    public const string SalespersonsDelete = "Salespersons.Delete";

    // ----- Suppliers / contractors + orders -----
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersCreate = "Suppliers.Create";
    public const string SuppliersEdit = "Suppliers.Edit";
    public const string SuppliersDelete = "Suppliers.Delete";
    public const string SuppliersPay = "Suppliers.Pay";

    // ----- Human Resources ----- (HR.View already exists in some DBs; keep that exact name to avoid
    // a case-insensitive unique-index collision on the Permissions table)
    public const string HrView = "HR.View";     // view HR pages (employees, vacations, advances, rewards, settings)
    public const string HrManage = "HR.Manage"; // create/edit/delete + manage settings

    // ----- Reports -----
    public const string ReportsView = "Reports.View";

    // ----- Tasks management ----- (personal "my tasks" is available to every signed-in user;
    // these gate the all-tasks list and its management actions)
    public const string TasksView = "Tasks.View";
    public const string TasksCreate = "Tasks.Create";
    public const string TasksEdit = "Tasks.Edit";
    public const string TasksDelete = "Tasks.Delete";

    // ----- Marketing: campaigns -----
    public const string CampaignsView = "Campaigns.View";
    public const string CampaignsCreate = "Campaigns.Create";
    public const string CampaignsEdit = "Campaigns.Edit";
    public const string CampaignsDelete = "Campaigns.Delete";

    // Group labels (Arabic).
    private const string GHome = "الرئيسية";
    private const string GProjects = "المشاريع";
    private const string GSales = "المبيعات (العقود)";
    private const string GCollections = "التحصيلات";
    private const string GSafes = "الخزائن";
    private const string GExpenses = "المصروفات";
    private const string GIncomes = "الإيرادات";
    private const string GCustomers = "العملاء";
    private const string GSalespersons = "مندوبو المبيعات";
    private const string GSuppliers = "الموردون والمقاولون";
    private const string GReports = "التقارير";
    private const string GTasks = "إدارة المهام";
    private const string GHr = "الموارد البشرية";
    private const string GMarketing = "التسويق";
    private const string GUsers = "المستخدمون والصلاحيات";
    private const string GAdmin = "إدارة النظام";

    /// <summary>Full catalog with Arabic labels and groups, in display order.</summary>
    public static IReadOnlyList<PermissionInfo> Catalog { get; } = new[]
    {
        new PermissionInfo(DashboardView, "عرض الصفحة الرئيسية (لوحة القيادة)", GHome),

        new PermissionInfo(ProjectsView,   "عرض المشاريع",                    GProjects),
        new PermissionInfo(ProjectsCreate, "إضافة مشروع / وحدة / مرحلة",       GProjects),
        new PermissionInfo(ProjectsEdit,   "تعديل المشاريع والوحدات والمراحل", GProjects),
        new PermissionInfo(ProjectsDelete, "حذف المشاريع والوحدات والمراحل",   GProjects),

        new PermissionInfo(SalesView,   "عرض عقود البيع", GSales),
        new PermissionInfo(SalesCreate, "إنشاء عقد بيع",  GSales),
        new PermissionInfo(SalesDelete, "حذف عقود البيع", GSales),

        new PermissionInfo(CollectionsView,    "عرض التحصيلات", GCollections),
        new PermissionInfo(CollectionsCollect, "تحصيل الأقساط", GCollections),
        new PermissionInfo(CollectionsCancel,  "إلغاء التحصيل", GCollections),

        new PermissionInfo(SafesView,   "عرض الخزائن وحركاتها", GSafes),
        new PermissionInfo(SafesCreate, "إضافة خزنة",           GSafes),
        new PermissionInfo(SafesEdit,   "تعديل الخزائن",        GSafes),
        new PermissionInfo(SafesDelete, "حذف الخزائن",          GSafes),

        new PermissionInfo(ExpensesView,   "عرض المصروفات",  GExpenses),
        new PermissionInfo(ExpensesCreate, "إضافة مصروف",    GExpenses),
        new PermissionInfo(ExpensesEdit,   "تعديل المصروفات", GExpenses),
        new PermissionInfo(ExpensesDelete, "حذف المصروفات",  GExpenses),

        new PermissionInfo(IncomesView,   "عرض الإيرادات",  GIncomes),
        new PermissionInfo(IncomesCreate, "إضافة إيراد",    GIncomes),
        new PermissionInfo(IncomesEdit,   "تعديل الإيرادات", GIncomes),
        new PermissionInfo(IncomesDelete, "حذف الإيرادات",  GIncomes),

        new PermissionInfo(CustomersView,   "عرض العملاء وكشوف الحساب", GCustomers),
        new PermissionInfo(CustomersCreate, "إضافة عميل",               GCustomers),
        new PermissionInfo(CustomersEdit,   "تعديل العملاء",            GCustomers),
        new PermissionInfo(CustomersDelete, "حذف العملاء",              GCustomers),

        new PermissionInfo(SalespersonsView,   "عرض المندوبين", GSalespersons),
        new PermissionInfo(SalespersonsCreate, "إضافة مندوب",   GSalespersons),
        new PermissionInfo(SalespersonsEdit,   "تعديل المندوبين", GSalespersons),
        new PermissionInfo(SalespersonsDelete, "حذف المندوبين", GSalespersons),

        new PermissionInfo(SuppliersView,   "عرض الموردين وأوامر التوريد وكشوف الحساب", GSuppliers),
        new PermissionInfo(SuppliersCreate, "إضافة مورد / أمر توريد",                  GSuppliers),
        new PermissionInfo(SuppliersEdit,   "تعديل الموردين وأوامر التوريد",           GSuppliers),
        new PermissionInfo(SuppliersDelete, "حذف الموردين وأوامر التوريد",             GSuppliers),
        new PermissionInfo(SuppliersPay,    "سداد دفعات للموردين",                     GSuppliers),

        new PermissionInfo(ReportsView,     "عرض التقارير (اليومي، العملاء، الموردين)", GReports),

        new PermissionInfo(TasksView,   "عرض كل المهام", GTasks),
        new PermissionInfo(TasksCreate, "إسناد / إنشاء مهمة", GTasks),
        new PermissionInfo(TasksEdit,   "تعديل المهام", GTasks),
        new PermissionInfo(TasksDelete, "حذف المهام", GTasks),

        new PermissionInfo(HrView,   "عرض الموارد البشرية (الموظفون، الإجازات، السلف، المكافآت)", GHr),
        new PermissionInfo(HrManage, "إدارة الموارد البشرية والإعدادات",                        GHr),

        new PermissionInfo(CampaignsView,   "عرض الحملات التسويقية", GMarketing),
        new PermissionInfo(CampaignsCreate, "إضافة حملة",            GMarketing),
        new PermissionInfo(CampaignsEdit,   "تعديل الحملات وتحديث بياناتها", GMarketing),
        new PermissionInfo(CampaignsDelete, "حذف الحملات",           GMarketing),

        new PermissionInfo(UsersView,   "عرض المستخدمين",           GUsers),
        new PermissionInfo(UsersCreate, "إضافة مستخدم",             GUsers),
        new PermissionInfo(UsersEdit,   "تعديل المستخدمين وصلاحياتهم", GUsers),
        new PermissionInfo(UsersDelete, "حذف / تعطيل المستخدمين",   GUsers),

        new PermissionInfo(ActivityLogView, "عرض سجل نشاط المستخدمين", GAdmin),
        new PermissionInfo(SettingsManage,  "إدارة الإعدادات وبيانات المؤسسة", GAdmin),
        new PermissionInfo(TenantsManage,  "إدارة المؤسسات (مدير عام)",       GAdmin),
    };

    /// <summary>Every permission string, for policy registration and SuperAdmin seeding.</summary>
    public static IReadOnlyList<string> All { get; } = Catalog.Select(p => p.Name).ToArray();

    /// <summary>Permissions granted to a TenantAdmin: everything except cross-tenant management.</summary>
    public static IReadOnlyList<string> ForTenantAdmin { get; } =
        All.Where(p => p != TenantsManage).ToArray();
}
