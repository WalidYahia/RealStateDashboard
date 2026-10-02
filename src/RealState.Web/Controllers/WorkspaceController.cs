using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Filters;
using RealState.Web.Navigation;

namespace RealState.Web.Controllers;

/// <summary>
/// Role-oriented workspace navigation: the home workspace (authorized module cards + favorites + recent pages + quick
/// actions), module workspaces (authorized page cards + primary actions), global search (pages + business records),
/// and the per-user favorites / recent-pages shortcuts. Everything is derived from the user's permission claims via
/// <see cref="INavigationService"/>; every destination keeps enforcing its own authorization.
/// </summary>
[Authorize]
public class WorkspaceController : Controller
{
    private const int MaxRecent = 12;
    private readonly IApplicationDbContext _db;
    private readonly INavigationService _nav;
    private readonly IWorkspaceKpiService _kpis;

    public WorkspaceController(IApplicationDbContext db, INavigationService nav, IWorkspaceKpiService kpis)
    {
        _db = db;
        _nav = nav;
        _kpis = kpis;
    }

    /// <summary>Per-user key for the shortcuts: the user id, or «host» for the static host account.</summary>
    private string UserKey => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "host:" + (User.Identity?.Name ?? "");

    private AuthorizedNavigation Nav => _nav.For(User, Url);

    // ---------------- Home workspace ----------------
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var nav = Nav;
        return View(new WorkspaceHomeVm
        {
            Nav = nav,
            Favorites = await FavoritePagesAsync(nav, ct),
            Recent = await RecentAsync(nav, ct),
        });
    }

    // ---------------- Module workspace ----------------
    public async Task<IActionResult> Module(string id, CancellationToken ct)
    {
        var nav = Nav;
        var module = nav.Module(id);
        if (module is null) return NotFound();   // unknown, or nothing in it the user may see
        var favs = (await FavoritePagesAsync(nav, ct)).Select(f => f.Key).ToHashSet();
        return View(new WorkspaceModuleVm { Nav = nav, Module = module, Kpis = await _kpis.GetAsync(module, ct), FavoriteKeys = favs });
    }

    // ---------------- Favorites ----------------
    /// <summary>The user's favorite pages they can still see (topbar menu + current-page star), as JSON.</summary>
    [HttpGet]
    public async Task<IActionResult> Favorites(CancellationToken ct)
        => Json((await FavoritePagesAsync(Nav, ct)).Select(p => new { key = p.Key, title = p.Title, url = p.Url, module = p.ModuleTitle, icon = NavIcons.Svg(p.Icon, 16).Value }));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [SkipActivityLog]
    public async Task<IActionResult> ToggleFavorite(string key, CancellationToken ct)
    {
        var page = Nav.Page(key);
        if (page is null) return NotFound();   // only pages the user can see can be starred
        var existing = await _db.UserNavItems.FirstOrDefaultAsync(x => x.UserKey == UserKey && x.Kind == UserNavItemKind.Favorite && x.Key == key, ct);
        if (existing is not null)
        {
            await _db.UserNavItems.Where(x => x.Id == existing.Id).ExecuteDeleteAsync(ct);
            return Json(new { ok = true, on = false });
        }
        _db.UserNavItems.Add(new UserNavItem
        {
            UserKey = UserKey, Kind = UserNavItemKind.Favorite, Key = key, Title = page.Title, Url = page.Url, LastUsedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true, on = true });
    }

    // ---------------- Recent pages ----------------
    /// <summary>Records a full-page visit (sent by the layout after load). Only local URLs; keeps the latest few.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [SkipActivityLog]
    public async Task<IActionResult> Visit(string? url, string? title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Url.IsLocalUrl(url) || url.Length > 400) return NoContent();
        var path = url.Split('?', '#')[0];
        if (path is "/" or "" || path.StartsWith("/Workspace", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/Host", StringComparison.OrdinalIgnoreCase))
            return NoContent();
        title = string.IsNullOrWhiteSpace(title) ? path : title.Trim();
        if (title.Length > 200) title = title[..200];

        var key = UserKey;
        var row = await _db.UserNavItems.FirstOrDefaultAsync(x => x.UserKey == key && x.Kind == UserNavItemKind.Recent && x.Key == url, ct);
        if (row is null)
            _db.UserNavItems.Add(new UserNavItem { UserKey = key, Kind = UserNavItemKind.Recent, Key = url, Url = url, Title = title, LastUsedAt = DateTime.UtcNow });
        else { row.Title = title; row.LastUsedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);

        // Keep only the latest few (hard delete — these are disposable shortcuts).
        var stale = await _db.UserNavItems.Where(x => x.UserKey == key && x.Kind == UserNavItemKind.Recent)
            .OrderByDescending(x => x.LastUsedAt).Skip(MaxRecent).Select(x => x.Id).ToListAsync(ct);
        if (stale.Count > 0) await _db.UserNavItems.Where(x => stale.Contains(x.Id)).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    // ---------------- Global search ----------------
    /// <summary>
    /// Business records matching <paramref name="q"/>, grouped by type, only for the types the user may view (pages are
    /// searched in the browser from the authorized navigation). Up to 5 per type.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(string? q, CancellationToken ct)
    {
        q = (q ?? "").Trim();
        var groups = new List<object>();
        if (q.Length < 2) return Json(groups);
        bool Can(string p) => User.HasClaim("permission", p);
        var digits = new string(q.Where(char.IsDigit).ToArray());
        void Add(string title, string icon, IEnumerable<(string Title, string? Sub, string Url)> items)
        {
            var list = items.Select(i => new { title = i.Title, sub = i.Sub, url = i.Url }).ToList();
            if (list.Count > 0) groups.Add(new { title, icon = NavIcons.Svg(icon, 18).Value, items = list });
        }

        if (Can(PermissionNames.CustomersView))
            Add("العملاء", "users", (await _db.Customers.Where(c => !c.IsLead && (c.FullName.Contains(q) || (c.Phone != null && c.Phone.Contains(q))))
                .OrderBy(c => c.FullName).Take(5).Select(c => new { c.Id, c.FullName, c.Phone }).ToListAsync(ct))
                .Select(c => (c.FullName, c.Phone, Url.Action("Details", "Customers", new { area = "CRM", id = c.Id })!)));
        if (Can(PermissionNames.SuppliersView))
            Add("الموردون", "truck", (await _db.Suppliers.Where(s => s.Name.Contains(q) || (s.Phone != null && s.Phone.Contains(q)))
                .OrderBy(s => s.Name).Take(5).Select(s => new { s.Id, s.Name, s.Phone }).ToListAsync(ct))
                .Select(s => (s.Name, s.Phone, Url.Action("Details", "Suppliers", new { area = "Suppliers", id = s.Id })!)));
        if (Can(PermissionNames.InventoryView))
        {
            var canCard = Can(PermissionNames.InventoryReports);
            Add("الأصناف", "package", (await _db.Products.Where(p => p.Name.Contains(q) || p.Sku.Contains(q))
                .OrderBy(p => p.Sku).Take(5).Select(p => new { p.Id, p.Sku, p.Name }).ToListAsync(ct))
                .Select(p => ($"{p.Sku} — {p.Name}", canCard ? "بطاقة الصنف" : null,
                    canCard ? Url.Action("Index", "InventoryReports", new { area = "Inventory", tab = "card", productId = p.Id })!
                            : Url.Action("Index", "Products", new { area = "Inventory" })!)));
        }
        if (Can(PermissionNames.ProjectsView))
            Add("المشاريع", "building", (await _db.Projects.Where(p => p.Name.Contains(q) || p.Code.Contains(q))
                .OrderBy(p => p.Name).Take(5).Select(p => new { p.Id, p.Code, p.Name }).ToListAsync(ct))
                .Select(p => ($"#{p.Code} {p.Name}", (string?)null, Url.Action("Details", "Projects", new { area = "Projects", id = p.Id })!)));
        if (Can(PermissionNames.SalesView))
            Add("العقود", "contract", (await _db.SaleContracts.Where(s => s.Code.Contains(q))
                .OrderByDescending(s => s.ContractDate).Take(5).Select(s => new { s.Id, s.Code }).ToListAsync(ct))
                .Select(s => (s.Code, (string?)null, Url.Action("Details", "Sales", new { area = "Sales", id = s.Id })!)));
        if (digits.Length >= 2)
        {
            if (Can(PermissionNames.SalesInvoicesView))
                Add("فواتير المبيعات", "receipt", (await _db.ProductSalesInvoices.Where(i => i.Number.ToString().Contains(digits))
                    .OrderByDescending(i => i.Number).Take(5).Select(i => new { i.Id, i.Number, i.InvoiceDate }).ToListAsync(ct))
                    .Select(i => ($"SI-{i.Number}", (string?)i.InvoiceDate.ToString("yyyy/MM/dd"), Url.Action("Details", "SalesInvoices", new { area = "Sales", id = i.Id })!)));
            if (Can(PermissionNames.PurchaseInvoicesView))
                Add("فواتير المشتريات", "cart", (await _db.PurchaseInvoices.Where(i => i.Number.ToString().Contains(digits))
                    .OrderByDescending(i => i.Number).Take(5).Select(i => new { i.Id, i.Number, i.InvoiceDate }).ToListAsync(ct))
                    .Select(i => ($"PI-{i.Number}", (string?)i.InvoiceDate.ToString("yyyy/MM/dd"), Url.Action("Details", "PurchaseInvoices", new { area = "Suppliers", id = i.Id })!)));
            if (Can(PermissionNames.SuppliersView))
                Add("أوامر التوريد", "clipboard", (await _db.SupplierOrders.Where(o => o.Number.ToString().Contains(digits))
                    .OrderByDescending(o => o.Number).Take(5).Select(o => new { o.Id, o.Number, o.OrderDate }).ToListAsync(ct))
                    .Select(o => ($"PO-{o.Number:D4}", (string?)o.OrderDate.ToString("yyyy/MM/dd"), Url.Action("Details", "Orders", new { area = "Suppliers", id = o.Id })!)));
        }
        return Json(groups);
    }

    // ---------------- helpers ----------------
    /// <summary>Favorites the user can still see, in the order they were added (a page that lost its permission drops out).</summary>
    private async Task<List<NavPageVm>> FavoritePagesAsync(AuthorizedNavigation nav, CancellationToken ct)
    {
        var keys = await _db.UserNavItems.Where(x => x.UserKey == UserKey && x.Kind == UserNavItemKind.Favorite)
            .OrderBy(x => x.CreatedAt).Select(x => x.Key).ToListAsync(ct);
        return keys.Select(nav.Page).Where(p => p is not null).Select(p => p!).ToList();
    }

    /// <summary>
    /// Latest visited pages. A recent page under a controller the navigation knows is shown only while the user can see
    /// one of that controller's pages; the destination still authorizes the request either way.
    /// </summary>
    private async Task<List<RecentItemVm>> RecentAsync(AuthorizedNavigation nav, CancellationToken ct)
    {
        var rows = await _db.UserNavItems.Where(x => x.UserKey == UserKey && x.Kind == UserNavItemKind.Recent)
            .OrderByDescending(x => x.LastUsedAt).Take(8).ToListAsync(ct);
        string Prefix(string? area, string controller) => ("/" + (string.IsNullOrEmpty(area) ? "" : area + "/") + controller).ToLowerInvariant();
        var known = NavigationCatalog.Pages.GroupBy(p => Prefix(p.Area, p.Controller))
            .ToDictionary(g => g.Key, g => g.Any(p => nav.Page(p.Key) is not null));
        var list = new List<RecentItemVm>();
        foreach (var r in rows)
        {
            var path = r.Url.Split('?', '#')[0].ToLowerInvariant();
            var match = known.Keys.Where(k => path == k || path.StartsWith(k + "/")).OrderByDescending(k => k.Length).FirstOrDefault();
            if (match is not null && !known[match]) continue;
            var page = nav.Pages.FirstOrDefault(p => match is not null && p.Url.Split('?')[0].ToLowerInvariant().StartsWith(match));
            list.Add(new RecentItemVm(r.Title, r.Url, page?.Icon ?? "file", page?.ModuleTitle));
        }
        return list;
    }
}

public sealed record RecentItemVm(string Title, string Url, string Icon, string? Module);

public sealed class WorkspaceHomeVm
{
    public AuthorizedNavigation Nav { get; init; } = new();
    public List<NavPageVm> Favorites { get; init; } = new();
    public List<RecentItemVm> Recent { get; init; } = new();
}

public sealed class WorkspaceModuleVm
{
    public AuthorizedNavigation Nav { get; init; } = new();
    public NavModuleVm Module { get; init; } = default!;
    public Dictionary<string, NavKpi> Kpis { get; init; } = new();
    public HashSet<string> FavoriteKeys { get; init; } = new();
}
