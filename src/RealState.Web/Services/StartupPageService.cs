using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Web.Services;

/// <summary>A page a tenant may choose as its default/startup page, and who can open it.</summary>
public sealed record StartupPage(string Key, string Label, string Group, string? Area, string Controller, string Action,
    Func<ClaimsPrincipal, bool> CanOpen);

/// <summary>
/// Per-tenant default/startup page (الصفحة الافتتاحية): where users land after signing in and on the site root.
/// Stored in the tenant's <see cref="Setting"/> rows under <see cref="SettingKey"/>. A user who can't open the
/// chosen page falls back to the dashboard, then to the first catalog page they can open, then to «مهامي».
/// </summary>
public interface IStartupPageService
{
    IReadOnlyList<StartupPage> Catalog { get; }

    /// <summary>The tenant's chosen page key (default: the dashboard).</summary>
    Task<string> GetTenantKeyAsync(CancellationToken ct = default);

    /// <summary>Saves the tenant's chosen page (no SaveChanges).</summary>
    Task SetTenantKeyAsync(string key, CancellationToken ct = default);

    /// <summary>The page this user should land on: the tenant's choice if they can open it, else a fallback.</summary>
    Task<StartupPage> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

public sealed class StartupPageService : IStartupPageService
{
    public const string SettingKey = "StartupPage";
    public const string DefaultKey = "dashboard";

    private readonly IApplicationDbContext _db;
    public StartupPageService(IApplicationDbContext db) => _db = db;

    private static Func<ClaimsPrincipal, bool> Perm(params string[] anyOf)
        => u => anyOf.Any(p => u.HasClaim("permission", p));

    // «مهامي» is open to every signed-in user, so it is always the last-resort landing page.
    private static readonly StartupPage MyTasks =
        new("my-tasks", "مهامي", "المهام", "Tasks", "Tasks", "Mine", _ => true);

    private static readonly IReadOnlyList<StartupPage> Pages = new[]
    {
        new StartupPage(DefaultKey, "الرئيسية (لوحة القيادة)", "الرئيسية", "", "Dashboard", "Index", Perm(PermissionNames.DashboardView)),

        new StartupPage("projects", "المشاريع", "المشاريع", "Projects", "Projects", "Index", Perm(PermissionNames.ProjectsView)),

        new StartupPage("sales", "عقود البيع", "المبيعات", "Sales", "Sales", "Index", Perm(PermissionNames.SalesView)),
        new StartupPage("collections", "التحصيلات", "المبيعات", "Sales", "Collections", "Index", Perm(PermissionNames.CollectionsView)),
        new StartupPage("customers", "العملاء", "المبيعات", "CRM", "Customers", "Index", Perm(PermissionNames.CustomersView)),
        new StartupPage("leads", "العملاء المحتملون", "المبيعات", "CRM", "Leads", "Index",
            Perm(PermissionNames.CustomersView, PermissionNames.LeadsControl, PermissionNames.LeadsConvert)),
        new StartupPage("campaigns", "الحملات التسويقية", "التسويق", "Marketing", "Campaigns", "Index", Perm(PermissionNames.CampaignsView)),

        new StartupPage("suppliers", "قائمة الموردين", "المشتريات", "Suppliers", "Suppliers", "Index", Perm(PermissionNames.SuppliersView)),
        new StartupPage("purchase-orders", "أوامر التوريد", "المشتريات", "Suppliers", "Orders", "Index", Perm(PermissionNames.SuppliersView)),
        new StartupPage("purchase-invoices", "فواتير المشتريات", "المشتريات", "Suppliers", "PurchaseInvoices", "Index", Perm(PermissionNames.PurchaseInvoicesView)),
        new StartupPage("work-orders", "أوامر الشغل", "المقاولات", "Contracting", "WorkOrders", "Index", Perm(PermissionNames.ContractingView)),

        new StartupPage("safes", "الخزائن", "المالية", "Accounting", "Safes", "Index", Perm(PermissionNames.SafesView)),
        new StartupPage("safe-transfers", "التحويلات بين الخزائن", "المالية", "Accounting", "SafeTransfers", "Index", Perm(PermissionNames.SafesView)),
        new StartupPage("expenses", "المصروفات", "المالية", "Accounting", "Expenses", "Index", Perm(PermissionNames.ExpensesView)),
        new StartupPage("incomes", "الإيرادات", "المالية", "Accounting", "Incomes", "Index", Perm(PermissionNames.IncomesView)),
        new StartupPage("general-ledger", "دفتر الأستاذ العام", "المحاسبة", "Accounting", "GeneralLedger", "Index", Perm(PermissionNames.AccountsView)),

        new StartupPage("inventory", "لوحة المخزون", "المخزون", "Inventory", "Dashboard", "Index", Perm(PermissionNames.InventoryView)),
        new StartupPage("products", "الأصناف", "المخزون", "Inventory", "Products", "Index", Perm(PermissionNames.InventoryView)),

        new StartupPage("employees", "الموظفون", "الموارد البشرية", "Hr", "Employees", "Index", Perm(PermissionNames.HrView)),

        new StartupPage("tasks", "قائمة المهام", "المهام", "Tasks", "Tasks", "Index", Perm(PermissionNames.TasksView)),
        MyTasks,

        new StartupPage("daily-report", "التقرير اليومي", "التقارير", "Reports", "Reports", "Daily", Perm(PermissionNames.ReportsView)),
    };

    public IReadOnlyList<StartupPage> Catalog => Pages;

    public async Task<string> GetTenantKeyAsync(CancellationToken ct = default)
    {
        var key = await _db.Settings.Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return Pages.Any(p => p.Key == key) ? key! : DefaultKey;
    }

    public async Task SetTenantKeyAsync(string key, CancellationToken ct = default)
    {
        if (!Pages.Any(p => p.Key == key)) key = DefaultKey;
        var row = await _db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (row is null) _db.Settings.Add(new Setting { Key = SettingKey, Value = key });
        else row.Value = key;
    }

    public async Task<StartupPage> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var key = await GetTenantKeyAsync(ct);
        var chosen = Pages.First(p => p.Key == key);
        if (chosen.CanOpen(user)) return chosen;
        var dashboard = Pages.First(p => p.Key == DefaultKey);
        if (dashboard.CanOpen(user)) return dashboard;
        return Pages.FirstOrDefault(p => p.CanOpen(user)) ?? MyTasks;
    }
}
