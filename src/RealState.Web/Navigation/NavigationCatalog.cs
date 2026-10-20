using static RealState.Application.Common.PermissionNames;

namespace RealState.Web.Navigation;

/// <summary>A business module (workspace): a card on the home workspace, opening its own module workspace.</summary>
public sealed record NavModule(string Key, string Title, string Description, string Icon, int Sort);

/// <summary>
/// A page reachable from the navigation. Visible when the user holds ANY of <see cref="AnyOf"/> (empty = every signed-in
/// user) — the same permission claims the page's own [Authorize] policy checks; visibility is never the security.
/// <see cref="Module"/> is its home module (breadcrumbs); <see cref="AlsoIn"/> lists other module workspaces showing it.
/// <see cref="Query"/> are extra query values that identify the page (e.g. a report tab).
/// <see cref="Group"/> is the key of the <see cref="NavGroup"/> card it sits in (null = directly in the module).
/// <see cref="Soon"/> shows «قريبًا»: not built yet, or built but held back until reviewed. Its card only shows a «قريبًا»
/// message, it stays out of search / favorites / quick actions, and <see cref="Filters.ComingSoonFilter"/> blocks the
/// page itself (a controller whose pages are all «قريبًا» is blocked as a whole). Remove the flag to publish it.
/// </summary>
public sealed record NavPage(string Key, string Title, string Description, string Icon, string Module,
    string? Area, string Controller, string Action, string[] AnyOf,
    IReadOnlyDictionary<string, string>? Query = null, string[]? AlsoIn = null, bool Soon = false, string? Group = null);

/// <summary>
/// A card inside a module workspace that opens its own page of cards (المالية والمحاسبة › المحاسبة › الأصول الثابتة).
/// <see cref="Parent"/> nests it in another group; <see cref="Soon"/>: the card only shows a «قريبًا» message.
/// A group shows only while the user can see at least one page in it (directly or in a nested group).
/// </summary>
public sealed record NavGroup(string Key, string Title, string Description, string Icon, string Module,
    string? Parent = null, bool Soon = false);

/// <summary>A frequent operation («＋ إجراء جديد» / a module's primary actions). <see cref="Fragment"/> «new» opens the
/// page's create form (the page's [data-quick-new] button).</summary>
public sealed record NavAction(string Key, string Title, string Icon, string Module,
    string? Area, string Controller, string Action, string[] AnyOf, string? Fragment = "new", bool Soon = false);

/// <summary>
/// The navigation definition: modules, pages and actions with the permissions that unlock them. The workspace, module
/// workspaces, quick actions, search, favorites, recents, breadcrumbs and the startup page all read from here — add a
/// page once and it appears everywhere it should. Permission names come from the existing catalog
/// (<see cref="RealState.Application.Common.PermissionNames"/>): no roles, no second permission system.
/// </summary>
public static class NavigationCatalog
{
    /// <summary>
    /// The old full side menu (_Layout's sidebar + sidebar.js) is kept implemented but switched off: no ☰ button in the
    /// top bar and always hidden. Set to true to bring it back (opened on demand, remembered per browser).
    /// </summary>
    public const bool LegacySidebar = false;

    private static string[] P(params string[] anyOf) => anyOf;
    private static readonly string[] Everyone = Array.Empty<string>();
    private static Dictionary<string, string> Q(string key, string value) => new() { [key] = value };

    /// <summary>
    /// The «قريبًا» page a request targets, if any: the exact page (area / controller / action), or — for a controller
    /// whose catalog pages are all «قريبًا» — any of its actions (details, forms, prints…).
    /// </summary>
    public static NavPage? SoonPageFor(string? area, string? controller, string? action)
    {
        bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
        var mine = Pages.Where(p => Eq(p.Area, area) && Eq(p.Controller, controller)).ToList();
        if (mine.Count == 0) return null;
        return mine.FirstOrDefault(p => p.Soon && Eq(p.Action, action))
               ?? (mine.All(p => p.Soon) ? mine.FirstOrDefault(p => Eq(p.Action, "Index")) ?? mine[0] : null);
    }

    public static readonly IReadOnlyList<NavModule> Modules = new NavModule[]
    {
        new("home",       "الرئيسية",            "لوحة التحكم ومهامي وحسابي",                          "home",      0),
        new("projects",   "المشاريع",            "المشاريع ووحداتها ومراحل التنفيذ",                    "building",  10),
        new("sales",      "العملاء والمبيعات",   "العقود والتحصيلات وفواتير المبيعات والعملاء",         "handshake", 20),
        new("purchasing", "المشتريات والموردون", "الموردون وأوامر التوريد وفواتير ومرتجعات المشتريات", "cart",      30),
        new("contracting", "المقاولات وأوامر الشغل", "المقاولون وأوامر الشغل والتنفيذ والسداد",          "hardhat",   35),
        new("inventory",  "المخزون",             "الأصناف والمخازن وأذون المخزون وتقاريره",            "package",   40),
        new("finance",    "المالية والمحاسبة",   "الخزائن والسندات، الحسابات والقوائم والأصول الثابتة، والعُهد", "wallet", 50),
        new("marketing",  "التسويق",             "الحملات والعملاء المحتملون",                          "megaphone", 60),
        new("hr",         "الموارد البشرية",     "الموظفون والإجازات والسلف والمكافآت",                "briefcase", 70),
        new("reports",    "التقارير",            "تقارير المبيعات والمشتريات والمخزون والمالية",        "chart",     80),
        new("admin",      "الإدارة",             "المستخدمون والمؤسسات والإعدادات وسجل النشاط",        "settings",  90),
    };

    // Group cards of the «المالية والمحاسبة» workspace.
    private const string GFinance = "fin-money";
    private const string GAccounting = "fin-accounting";
    private const string GAssets = "fin-fixed-assets";
    private const string GCustody = "fin-custody";

    public static readonly IReadOnlyList<NavGroup> Groups = new NavGroup[]
    {
        new(GFinance, "المالية", "الخزائن والتحويلات والإيرادات والمصروفات", "vault", "finance"),
        new(GAccounting, "المحاسبة", "دليل الحسابات والقيود والقوائم المالية والأصول الثابتة", "book", "finance"),
        // Implemented, held as «قريبًا» until its final review.
        new(GAssets, "الأصول الثابتة", "سجل الأصول وإهلاكها وفئاتها", "package", "finance", Parent: GAccounting, Soon: true),
        new(GCustody, "العُهد", "العُهد المالية والعينية للموظفين", "briefcase", "finance"),
    };

    public static readonly IReadOnlyList<NavPage> Pages = new NavPage[]
    {
        // ---- الرئيسية ----
        new("dashboard", "لوحة التحكم", "المؤشرات والرسوم والتنبيهات", "dashboard", "home", "", "Dashboard", "Index", P(DashboardView)),
        new("my-tasks", "مهامي", "المهام المسندة إليّ ومنّي", "tasks", "home", "Tasks", "Tasks", "Mine", Everyone),
        new("tasks", "قائمة المهام", "كل المهام وإسنادها ومتابعتها", "list-checks", "home", "Tasks", "Tasks", "Index", P(TasksView)),
        new("change-password", "تغيير كلمة المرور", "تحديث كلمة مرور حسابك", "key", "home", "", "Account", "ChangePassword", Everyone),

        // ---- المشاريع ----
        new("projects", "المشاريع", "المشاريع والوحدات والمراحل والمرفقات", "building", "projects", "Projects", "Projects", "Index", P(ProjectsView)),
        new("projects-report", "تقرير المشاريع", "الوحدات والمخزون والمصروفات والإيرادات", "chart", "projects", "Reports", "ProjectsReport", "Index", P(ReportsProjects), AlsoIn: new[] { "reports" }),
        new("project-types", "أنواع المشاريع", "تعريف أنواع المشاريع", "layers", "projects", "Projects", "ProjectTypes", "Index", P(SettingsManage)),
        new("stage-definitions", "مراحل المشاريع", "تعريف مراحل التنفيذ", "steps", "projects", "Projects", "StageDefinitions", "Index", P(SettingsManage)),

        // ---- العملاء والمبيعات ----
        new("contracts-summary", "ملخص التعاقدات", "مؤشرات العقود والتحصيل", "pie", "sales", "Sales", "Sales", "Summary", P(SalesView), AlsoIn: new[] { "reports" }),
        new("contracts", "العقود", "عقود بيع الوحدات وجداول الأقساط", "contract", "sales", "Sales", "Sales", "Index", P(SalesView)),
        new("collections", "تحصيلات المشاريع", "الأقساط المستحقة والمحصّلة", "coins", "sales", "Sales", "Collections", "Index", P(CollectionsView), AlsoIn: new[] { "projects" }),
        new("sales-summary", "ملخص المبيعات", "مؤشرات فواتير المبيعات والعملاء", "pie", "sales", "Sales", "SalesInvoices", "Summary", P(SalesInvoicesView), AlsoIn: new[] { "reports" }),
        new("sales-invoices", "فواتير المبيعات", "بيع الأصناف للعملاء والتحصيل", "receipt", "sales", "Sales", "SalesInvoices", "Index", P(SalesInvoicesView)),
        new("sales-returns", "مرتجعات المبيعات", "إرجاع أصناف من العملاء ورد النقدية", "undo", "sales", "Sales", "SalesReturns", "Index", P(SalesReturnsView)),
        new("customers", "العملاء", "بيانات العملاء وكشوف الحساب", "users", "sales", "CRM", "Customers", "Index", P(CustomersView)),
        new("salespersons", "المناديب", "مندوبو المبيعات", "user-check", "sales", "CRM", "Salespersons", "Index", P(SalespersonsView), AlsoIn: new[] { "marketing" }),
        new("leads", "العملاء المحتملون", "إدارة علاقات العملاء والمتابعة", "user-plus", "marketing", "CRM", "Leads", "Index", P(CustomersView, LeadsControl, LeadsConvert), AlsoIn: new[] { "sales" }),
        new("customers-report", "تقرير العملاء", "أرصدة ومستحقات العملاء", "chart", "sales", "Reports", "Reports", "Customers", P(ReportsView), AlsoIn: new[] { "reports" }),

        // ---- المشتريات والموردون ----
        new("suppliers", "الموردون", "بيانات الموردين وكشوف الحساب والسداد", "truck", "purchasing", "Suppliers", "Suppliers", "Index", P(SuppliersView)),
        new("contractors", "المقاولون", "المقاولون وكشوف حساباتهم", "hardhat", "contracting", "Contracting", "Contractors", "Index", P(ContractingView)),
        new("work-orders", "أوامر الشغل", "أوامر الشغل والتنفيذ والسداد", "clipboard", "contracting", "Contracting", "WorkOrders", "Index", P(ContractingView)),
        new("purchase-orders", "أوامر التوريد", "طلبات الأصناف قبل الفوترة", "clipboard", "purchasing", "Suppliers", "Orders", "Index", P(SuppliersView)),
        new("purchase-invoices", "فواتير المشتريات", "فواتير الموردين والاستلام والسداد", "receipt", "purchasing", "Suppliers", "PurchaseInvoices", "Index", P(PurchaseInvoicesView)),
        new("purchase-returns", "مرتجعات المشتريات", "رد أصناف للموردين واسترداد النقدية", "undo", "purchasing", "Suppliers", "PurchaseReturns", "Index", P(PurchaseReturnsView)),
        new("suppliers-report", "تقرير الموردين", "أرصدة ومستحقات الموردين", "chart", "purchasing", "Reports", "Reports", "Suppliers", P(ReportsView), AlsoIn: new[] { "reports" }),

        // ---- المخزون ----
        new("inventory-dashboard", "لوحة المخزون", "القيمة والتنبيهات حسب المخزن", "dashboard", "inventory", "Inventory", "Dashboard", "Index", P(InventoryView)),
        new("products", "الأصناف", "تعريف الأصناف ووحداتها وأسعارها", "package", "inventory", "Inventory", "Products", "Index", P(InventoryView)),
        new("warehouses", "المخازن", "مواقع التخزين", "warehouse", "inventory", "Inventory", "Warehouses", "Index", P(InventoryView)),
        new("goods-receipts", "أذون الاستلام", "إدخال أصناف للمخزن", "inbox-in", "inventory", "Inventory", "GoodsReceipts", "Index", P(InventoryView)),
        new("goods-issues", "أذون الصرف", "صرف أصناف من المخزن", "inbox-out", "inventory", "Inventory", "GoodsIssues", "Index", P(InventoryView)),
        new("stock-transfers", "التحويل المخزني", "نقل الأصناف بين المخازن", "arrows", "inventory", "Inventory", "Transfers", "Index", P(InventoryView)),
        new("adjustments", "التسويات", "تصحيح كميات المخزون", "sliders", "inventory", "Inventory", "Adjustments", "Index", P(InventoryView)),
        new("stock-counts", "الجرد", "الجرد الفعلي ومطابقة الدفاتر", "clipboard-check", "inventory", "Inventory", "StockCounts", "Index", P(InventoryView)),
        new("stock-balance", "أرصدة المخزون", "الأرصدة بالوحدات حسب المخزن", "layers", "inventory", "Inventory", "InventoryReports", "Index", P(InventoryReports), Q("tab", "balance"), AlsoIn: new[] { "reports" }),
        new("stock-movements", "حركة المخزون", "الوارد والمنصرف خلال فترة", "activity", "inventory", "Inventory", "InventoryReports", "Index", P(InventoryReports), Q("tab", "movements")),
        new("stock-card", "بطاقة الصنف", "حركة صنف في مخزن ورصيده", "file", "inventory", "Inventory", "InventoryReports", "Index", P(InventoryReports), Q("tab", "card")),
        new("stock-valuation", "تقييم المخزون", "قيمة المخزون حسب الصنف", "scale", "inventory", "Inventory", "InventoryReports", "Index", P(InventoryReports), Q("tab", "valuation")),
        new("inventory-reconciliation", "مطابقة الأستاذ", "دفتر المخزون مقابل حساب الأستاذ", "book", "inventory", "Inventory", "InventoryReports", "Index", P(InventoryReports), Q("tab", "reconciliation")),
        new("opening-stock", "الرصيد الافتتاحي للأصناف", "كميات الأصناف وتكلفتها عند بدء العمل", "inbox-in", "inventory", "Inventory", "OpeningStock", "Index", P(InventoryOpeningStock)),
        new("cost-update", "تحديث تكلفة الأصناف", "التكلفة الحالية والجديدة وإعادة تقييم المخزون", "tag", "inventory", "Inventory", "CostUpdates", "Index", P(InventoryUpdateCost)),
        new("inventory-settings", "إعدادات المخزون", "الحسابات والتصنيفات ووحدات القياس", "settings", "inventory", "Inventory", "Settings", "Index", P(InventoryManage)),

        // ---- المالية والمحاسبة ----
        new("safes", "الخزائن", "الخزائن والبنوك وحركاتها", "vault", "finance", "Accounting", "Safes", "Index", P(SafesView), Group: GFinance),
        new("safe-transfers", "التحويلات بين الخزائن", "نقل النقدية بين الخزائن", "arrows", "finance", "Accounting", "SafeTransfers", "Index", P(SafesView), Group: GFinance),
        new("incomes", "الإيرادات", "سندات القبض", "trend-up", "finance", "Accounting", "Incomes", "Index", P(IncomesView), Group: GFinance),
        new("expenses", "المصروفات", "سندات الصرف", "trend-down", "finance", "Accounting", "Expenses", "Index", P(ExpensesView), Group: GFinance),
        new("txn-categories", "بنود الإيرادات والمصروفات", "تصنيف السندات", "tag", "finance", "", "TxnCategories", "Index", P(TxnCategoriesManage), Group: GFinance),
        new("chart-of-accounts", "دليل الحسابات", "شجرة الحسابات", "tree", "finance", "Accounting", "ChartOfAccounts", "Index", P(AccountsView), Group: GAccounting),
        new("opening-balances", "الأرصدة الافتتاحية", "الرصيد الافتتاحي لكل حساب (العملاء والموردون والخزائن وغيرها)", "scale", "finance", "Accounting", "OpeningBalances", "Index", P(AccountsView), Group: GAccounting),
        new("general-ledger", "القيود المحاسبية", "دفتر الأستاذ والقيود اليدوية", "book", "finance", "Accounting", "GeneralLedger", "Index", P(AccountsView), Group: GAccounting),
        new("income-statement", "قائمة الدخل", "الإيرادات والمصروفات وصافي الربح", "chart", "finance", "Accounting", "AccountingReports", "IncomeStatement", P(AccountsView), AlsoIn: new[] { "reports" }, Group: GAccounting),
        new("trial-balance", "ميزان المراجعة", "أرصدة الحسابات", "scale", "finance", "Accounting", "AccountingReports", "TrialBalance", P(AccountsView), AlsoIn: new[] { "reports" }, Group: GAccounting),
        new("budget", "الموازنة", "قريبًا", "pie", "finance", "Accounting", "AccountingReports", "Budget", P(AccountsView), Soon: true, Group: GAccounting),
        // Fixed assets + material custody: implemented, held as «قريبًا» until their final review.
        new("fixed-assets", "الأصول الثابتة", "سجل الأصول وتكلفتها وإهلاكها وقيمتها الدفترية", "package", "finance", "Accounting", "FixedAssets", "Index", P(FixedAssetsView), Soon: true, Group: GAssets),
        new("fixed-asset-depreciation", "إهلاك الأصول الثابتة", "احتساب الإهلاك الشهري وترحيله", "trend-down", "finance", "Accounting", "FixedAssetDepreciation", "Index", P(FixedAssetsView), Soon: true, Group: GAssets),
        new("fixed-asset-categories", "فئات الأصول الثابتة", "الفئات وحسابات التكلفة والإهلاك لكل فئة", "layers", "finance", "Accounting", "FixedAssetCategories", "Index", P(FixedAssetsView), Soon: true, Group: GAssets),
        new("financial-custody", "العُهد المالية", "عُهد نقدية للموظفين وتسويتها مع المحاسب", "wallet", "finance", "Accounting", "FinancialCustody", "Index", P(FinancialCustodyView), Group: GCustody),
        new("material-custody", "العُهد العينية", "أصول الشركة المسلَّمة للموظفين واستلامها", "briefcase", "finance", "Accounting", "MaterialCustody", "Index", P(MaterialCustodyView), Soon: true, Group: GCustody),

        // ---- التسويق ----
        new("campaigns", "الحملات", "الحملات التسويقية ونتائجها", "megaphone", "marketing", "Marketing", "Campaigns", "Index", P(CampaignsView)),

        // ---- الموارد البشرية ----
        new("employees", "الموظفون", "بيانات الموظفين", "users", "hr", "Hr", "Employees", "Index", P(HrView)),
        new("vacations", "الإجازات", "طلبات الإجازات", "calendar", "hr", "Hr", "Vacations", "Index", P(HrView)),
        new("leave-requests", "إذن تأخير / انصراف", "الأذونات اليومية", "clock", "hr", "Hr", "LeaveRequests", "Index", P(HrView)),
        new("advances", "السلف", "سلف الموظفين وسدادها", "banknote", "hr", "Hr", "Advances", "Index", P(HrView)),
        new("rewards", "المكافآت", "مكافآت الموظفين", "gift", "hr", "Hr", "Rewards", "Index", P(HrView)),
        new("hr-settings", "إعدادات الموارد البشرية", "الأقسام والوظائف والدوام", "settings", "hr", "Hr", "Hr", "Index", P(HrView)),

        // ---- التقارير ----
        new("daily-report", "التقرير اليومي", "حركة اليوم في كل الأقسام", "calendar", "reports", "Reports", "Reports", "Daily", P(ReportsView)),
        new("sales-profits", "تقرير أرباح المبيعات", "ربح كل فاتورة مبيعات بعد المرتجعات", "chart", "reports", "Reports", "SalesProfits", "Index", P(ReportsSalesProfits), AlsoIn: new[] { "sales" }),

        // ---- الإدارة ----
        new("users", "المستخدمون", "المستخدمون وصلاحياتهم", "user", "admin", "Admin", "Users", "Index", P(UsersView)),
        new("tenants", "المؤسسات", "إدارة المؤسسات", "buildings", "admin", "Admin", "Tenants", "Index", P(TenantsManage)),
        new("branding", "إعدادات المؤسسة", "البيانات والشعار والصفحة الافتتاحية", "palette", "admin", "", "Settings", "Branding", P(SettingsManage)),
        new("activity-log", "سجل النشاط", "عمليات المستخدمين", "history", "admin", "Admin", "ActivityLog", "Index", P(ActivityLogView)),
    };

    /// <summary>Frequent operations, each shown only with its Create / Collect / Post permission.</summary>
    public static readonly IReadOnlyList<NavAction> Actions = new NavAction[]
    {
        new("new-contract", "عقد بيع", "contract", "sales", "Sales", "Sales", "Index", P(SalesCreate)),
        new("new-collection", "تحصيل قسط", "coins", "sales", "Sales", "Collections", "Index", P(CollectionsCollect), Fragment: null),
        new("new-sales-invoice", "فاتورة مبيعات", "receipt", "sales", "Sales", "SalesInvoices", "Index", P(SalesInvoicesCreate)),
        new("new-sales-return", "مرتجع مبيعات", "undo", "sales", "Sales", "SalesReturns", "Index", P(SalesReturnsCreate)),
        new("new-customer", "عميل", "users", "sales", "CRM", "Customers", "Index", P(CustomersCreate)),
        new("new-purchase-order", "أمر توريد", "clipboard", "purchasing", "Suppliers", "Orders", "Index", P(SuppliersCreate)),
        new("new-purchase-invoice", "فاتورة مشتريات", "cart", "purchasing", "Suppliers", "PurchaseInvoices", "Index", P(PurchaseInvoicesCreate)),
        new("new-purchase-return", "مرتجع مشتريات", "undo", "purchasing", "Suppliers", "PurchaseReturns", "Index", P(PurchaseReturnsCreate)),
        new("new-supplier", "مورد", "truck", "purchasing", "Suppliers", "Suppliers", "Index", P(SuppliersCreate)),
        new("new-work-order", "أمر شغل", "hardhat", "contracting", "Contracting", "WorkOrders", "Index", P(ContractingCreate)),
        new("new-product", "صنف", "package", "inventory", "Inventory", "Products", "Index", P(InventoryManage)),
        new("new-goods-receipt", "إذن استلام", "inbox-in", "inventory", "Inventory", "GoodsReceipts", "Create", P(InventoryDocuments), Fragment: null),
        new("new-goods-issue", "إذن صرف", "inbox-out", "inventory", "Inventory", "GoodsIssues", "Create", P(InventoryDocuments), Fragment: null),
        new("new-stock-transfer", "تحويل مخزني", "arrows", "inventory", "Inventory", "Transfers", "Create", P(InventoryDocuments), Fragment: null),
        new("new-income", "سند قبض", "trend-up", "finance", "Accounting", "Incomes", "Index", P(IncomesCreate)),
        new("new-expense", "سند صرف", "trend-down", "finance", "Accounting", "Expenses", "Index", P(ExpensesCreate)),
        new("new-safe-transfer", "تحويل بين الخزائن", "arrows", "finance", "Accounting", "SafeTransfers", "Index", P(SafesTransfer)),
        new("new-journal", "قيد يدوي", "book", "finance", "Accounting", "GeneralLedger", "Index", P(AccountsPostJournal)),
        new("new-financial-custody", "عهدة مالية", "wallet", "finance", "Accounting", "FinancialCustody", "Index", P(FinancialCustodyCreate)),
        new("new-fixed-asset", "أصل ثابت", "package", "finance", "Accounting", "FixedAssets", "Index", P(FixedAssetsManage), Soon: true),
        new("new-material-custody", "عهدة عينية", "briefcase", "finance", "Accounting", "MaterialCustody", "Index", P(MaterialCustodyManage), Soon: true),
        new("new-project", "مشروع", "building", "projects", "Projects", "Projects", "Index", P(ProjectsCreate)),
        new("new-task", "مهمة", "tasks", "home", "Tasks", "Tasks", "Index", P(TasksCreate)),
    };
}
