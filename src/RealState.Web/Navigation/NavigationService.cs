using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace RealState.Web.Navigation;

/// <summary><see cref="Group"/>: the group card it sits in; <see cref="CanOpen"/>: false for a «قريبًا» page.</summary>
public sealed record NavPageVm(string Key, string Title, string Description, string Icon, string Url, string ModuleKey, string ModuleTitle, bool Soon,
    string? Group = null, bool CanOpen = true);
public sealed record NavActionVm(string Key, string Title, string Icon, string Url, string ModuleKey);

/// <summary>A group card the user can see: its nested groups and its own pages (only authorized ones).</summary>
public sealed record NavGroupVm(string Key, string Title, string Description, string Icon, string Url, string ModuleKey,
    string? ParentKey, bool Soon, bool CanOpen, IReadOnlyList<NavGroupVm> Groups, IReadOnlyList<NavPageVm> Pages)
{
    /// <summary>Pages inside, nested groups included.</summary>
    public int PageCount => Pages.Count + Groups.Sum(g => g.PageCount);
}

/// <summary>
/// <see cref="Pages"/>: every page shown in the module (its own and shared in); <see cref="Groups"/>: its top-level group
/// cards; <see cref="LoosePages"/>: the pages shown directly on the module workspace (not inside a group).
/// </summary>
public sealed record NavModuleVm(string Key, string Title, string Description, string Icon, string Url,
    IReadOnlyList<NavPageVm> Pages, IReadOnlyList<NavActionVm> Actions, IReadOnlyList<NavGroupVm> Groups, IReadOnlyList<NavPageVm> LoosePages);

/// <summary>
/// Where the current request sits in the navigation (breadcrumbs, favorite star). <see cref="Trail"/>: the group cards
/// between the module and the page (outermost first) — or, on a group workspace, down to that group.
/// </summary>
public sealed record NavLocation(NavModuleVm Module, NavPageVm? Page, bool IsPageItself, IReadOnlyList<NavGroupVm> Trail);

/// <summary>The navigation the current user may see — only authorized modules, pages and actions.</summary>
public sealed class AuthorizedNavigation
{
    public IReadOnlyList<NavModuleVm> Modules { get; init; } = Array.Empty<NavModuleVm>();
    public IReadOnlyList<NavActionVm> Actions { get; init; } = Array.Empty<NavActionVm>();
    /// <summary>Every authorized page once (by key), in module order.</summary>
    public IReadOnlyList<NavPageVm> Pages { get; init; } = Array.Empty<NavPageVm>();
    /// <summary>Every visible group (any depth).</summary>
    public IReadOnlyList<NavGroupVm> Groups { get; init; } = Array.Empty<NavGroupVm>();
    public string WorkspaceUrl { get; init; } = "/";

    public NavModuleVm? Module(string? key) => Modules.FirstOrDefault(m => m.Key == key);
    public NavPageVm? Page(string? key) => Pages.FirstOrDefault(p => p.Key == key);
    public NavGroupVm? Group(string? key) => Groups.FirstOrDefault(g => g.Key == key);

    /// <summary>The group and its parents, outermost first (empty for no group).</summary>
    public List<NavGroupVm> TrailTo(string? groupKey)
    {
        var trail = new List<NavGroupVm>();
        for (var g = Group(groupKey); g is not null; g = Group(g.ParentKey)) trail.Insert(0, g);
        return trail;
    }
}

public interface INavigationService
{
    /// <summary>The user's authorized navigation (computed once per request from their permission claims).</summary>
    AuthorizedNavigation For(ClaimsPrincipal user, IUrlHelper url);

    /// <summary>The module / page the current route belongs to, when it's one the user can see.</summary>
    NavLocation? Locate(AuthorizedNavigation nav, RouteData route, IQueryCollection query);

    /// <summary>Whether the user may see a catalog page (the page's own [Authorize] still decides access).</summary>
    bool CanSee(ClaimsPrincipal user, NavPage page);
}

/// <summary>
/// Turns <see cref="NavigationCatalog"/> into a user's navigation using the existing permission claims ("permission")
/// — no role checks and no second permission model. Hiding a card is only a convenience: every page keeps enforcing
/// its own authorization policy.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private const string ItemsKey = "__AuthorizedNavigation";
    private readonly IHttpContextAccessor _http;
    public NavigationService(IHttpContextAccessor http) => _http = http;

    private static bool Allowed(ClaimsPrincipal user, string[] anyOf)
        => user.Identity?.IsAuthenticated == true && (anyOf.Length == 0 || anyOf.Any(p => user.HasClaim("permission", p)));

    public bool CanSee(ClaimsPrincipal user, NavPage page) => Allowed(user, page.AnyOf);

    public AuthorizedNavigation For(ClaimsPrincipal user, IUrlHelper url)
    {
        var items = _http.HttpContext?.Items;
        if (items?[ItemsKey] is AuthorizedNavigation cached) return cached;

        string PageUrl(NavPage p)
        {
            var values = new RouteValueDictionary { ["area"] = p.Area ?? "" };
            if (p.Query is not null) foreach (var (k, v) in p.Query) values[k] = v;
            return url.Action(p.Action, p.Controller, values) ?? "#";
        }

        var moduleTitles = NavigationCatalog.Modules.ToDictionary(m => m.Key, m => m.Title);
        var pages = NavigationCatalog.Pages.Where(p => Allowed(user, p.AnyOf))
            .Select(p => (Def: p, Vm: new NavPageVm(p.Key, p.Title, p.Description, p.Icon, PageUrl(p), p.Module, moduleTitles[p.Module], p.Soon,
                p.Group, !p.Soon)))
            .ToList();
        var actions = NavigationCatalog.Actions.Where(a => Allowed(user, a.AnyOf) && !a.Soon)
            .Select(a =>
            {
                var href = url.Action(a.Action, a.Controller, new { area = a.Area ?? "" }) ?? "#";
                return new NavActionVm(a.Key, a.Title, a.Icon, a.Fragment is null ? href : href + "#" + a.Fragment, a.Module);
            }).ToList();

        // Group cards, built bottom-up: a group shows while it holds at least one visible page (directly or nested).
        var allGroups = new List<NavGroupVm>();
        NavGroupVm? BuildGroup(NavGroup g)
        {
            var children = NavigationCatalog.Groups.Where(c => c.Parent == g.Key).Select(BuildGroup).OfType<NavGroupVm>().ToList();
            var direct = pages.Where(p => p.Def.Group == g.Key).Select(p => p.Vm).ToList();
            if (children.Count == 0 && direct.Count == 0) return null;
            var vm = new NavGroupVm(g.Key, g.Title, g.Description, g.Icon,
                url.Action("Group", "Workspace", new { area = "", id = g.Key }) ?? "#",
                g.Module, g.Parent, g.Soon, !g.Soon, children, direct);
            allGroups.Add(vm);
            return vm;
        }

        // A module shows when the user can see at least one of its pages (its own or shared into it).
        var modules = NavigationCatalog.Modules.OrderBy(m => m.Sort).Select(m =>
        {
            var mine = pages.Where(p => p.Def.Module == m.Key || (p.Def.AlsoIn?.Contains(m.Key) ?? false)).ToList();
            var groups = NavigationCatalog.Groups.Where(g => g.Module == m.Key && g.Parent is null).Select(BuildGroup).OfType<NavGroupVm>().ToList();
            // A page shared into another module is shown there directly, outside its home module's groups.
            var loose = mine.Where(p => p.Def.Group is null || p.Def.Module != m.Key).Select(p => p.Vm).ToList();
            return new NavModuleVm(m.Key, m.Title, m.Description, m.Icon,
                url.Action("Module", "Workspace", new { area = "", id = m.Key }) ?? "#",
                mine.Select(p => p.Vm).ToList(), actions.Where(a => a.ModuleKey == m.Key).ToList(), groups, loose);
        }).Where(m => m.Pages.Count > 0).ToList();

        var nav = new AuthorizedNavigation
        {
            Modules = modules,
            Actions = actions,
            Pages = pages.Select(p => p.Vm).ToList(),
            Groups = allGroups,
            WorkspaceUrl = url.Action("Index", "Workspace", new { area = "" }) ?? "/",
        };
        if (items is not null) items[ItemsKey] = nav;
        return nav;
    }

    public NavLocation? Locate(AuthorizedNavigation nav, RouteData route, IQueryCollection query)
    {
        string V(string k) => route.Values.TryGetValue(k, out var v) ? v?.ToString() ?? "" : "";
        var area = V("area"); var controller = V("controller"); var action = V("action");
        bool Eq(string? a, string? b) => string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

        // The workspace itself: its module page / group page (or nothing for the home workspace).
        if (Eq(controller, "Workspace") && Eq(area, ""))
        {
            if (Eq(action, "Group") && nav.Group(V("id")) is { } g && nav.Module(g.ModuleKey) is { } gm)
                return new NavLocation(gm, null, false, nav.TrailTo(g.Key));
            var m = Eq(action, "Module") ? nav.Module(V("id")) : null;
            return m is null ? null : new NavLocation(m, null, false, Array.Empty<NavGroupVm>());
        }

        var candidates = NavigationCatalog.Pages
            .Where(p => Eq(p.Area, area) && Eq(p.Controller, controller) && nav.Page(p.Key) is not null).ToList();
        if (candidates.Count == 0) return null;
        bool QueryMatches(NavPage p) => p.Query is null || p.Query.All(q => Eq(query[q.Key].ToString(), q.Value));
        // Exact page (same action, matching identifying query) → that page; another action of the same controller
        // (Details, Create, …) → a record/operation inside the controller's main list page.
        var exact = candidates.FirstOrDefault(p => Eq(p.Action, action) && QueryMatches(p))
                    ?? candidates.FirstOrDefault(p => Eq(p.Action, action) && p.Query is not null && query.Count == 0);
        var def = exact ?? candidates.FirstOrDefault(p => Eq(p.Action, "Index")) ?? candidates[0];
        var page = nav.Page(def.Key)!;
        var module = nav.Module(def.Module) ?? nav.Modules.First(m => m.Pages.Any(x => x.Key == def.Key));
        return new NavLocation(module, page, exact is not null, module.Key == def.Module ? nav.TrailTo(def.Group) : new List<NavGroupVm>());
    }
}
