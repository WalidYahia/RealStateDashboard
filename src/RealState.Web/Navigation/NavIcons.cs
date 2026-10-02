using Microsoft.AspNetCore.Html;

namespace RealState.Web.Navigation;

/// <summary>
/// The workspace navigation's line-icon set (24×24, stroke = currentColor, so it follows the theme and state colors).
/// One consistent family instead of emoji; unknown names fall back to a neutral square.
/// </summary>
public static class NavIcons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["home"] = "<path d='M3 10.5 12 3l9 7.5'/><path d='M5 9.5V21h14V9.5'/><path d='M10 21v-6h4v6'/>",
        ["dashboard"] = "<rect x='3' y='3' width='7' height='9' rx='1.5'/><rect x='14' y='3' width='7' height='5' rx='1.5'/><rect x='14' y='12' width='7' height='9' rx='1.5'/><rect x='3' y='16' width='7' height='5' rx='1.5'/>",
        ["tasks"] = "<rect x='3' y='3' width='18' height='18' rx='2.5'/><path d='m8 12 3 3 5-6'/>",
        ["list-checks"] = "<path d='m3 6 2 2 3-3'/><path d='m3 13 2 2 3-3'/><path d='M12 7h9'/><path d='M12 14h9'/><path d='M12 20h9'/>",
        ["key"] = "<circle cx='8' cy='15' r='4'/><path d='m10.8 12.2 8.7-8.7'/><path d='m17 6 3 3'/><path d='m14.5 8.5 2 2'/>",
        ["building"] = "<rect x='4' y='3' width='16' height='18' rx='1.5'/><path d='M9 21v-4h6v4'/><path d='M8 7h2M14 7h2M8 11h2M14 11h2'/>",
        ["layers"] = "<path d='m12 3 9 5-9 5-9-5 9-5Z'/><path d='m3 13 9 5 9-5'/>",
        ["steps"] = "<path d='M4 20h5v-5h5v-5h6'/><circle cx='20' cy='6' r='2'/>",
        ["handshake"] = "<path d='m11 17 2 2a1.4 1.4 0 0 0 2-2'/><path d='m14 14 2.5 2.5a1.4 1.4 0 0 0 2-2L14.7 10.7a2 2 0 0 0-2.8 0l-.9.9a1.4 1.4 0 0 1-2-2l2.8-2.8a4 4 0 0 1 4.6-.7l1.6.9 3-1'/><path d='m21 3 1 11h-2'/><path d='M3 3 2 14l6.5 6.5a1.4 1.4 0 0 0 2-2'/><path d='M3 4h8'/>",
        ["contract"] = "<path d='M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z'/><path d='M14 3v6h6'/><path d='M8 13h8M8 17h5'/>",
        ["coins"] = "<circle cx='9' cy='9' r='6'/><path d='M18.1 10.4A6 6 0 1 1 10.4 18'/><path d='M8 7h2v4'/>",
        ["receipt"] = "<path d='M5 3v18l2.3-1.5L9.7 21l2.3-1.5 2.3 1.5 2.4-1.5L19 21V3l-2.3 1.5L14.3 3 12 4.5 9.7 3 7.3 4.5Z'/><path d='M9 9h6M9 13h6'/>",
        ["undo"] = "<path d='M9 14 4 9l5-5'/><path d='M4 9h10.5a5.5 5.5 0 0 1 0 11H11'/>",
        ["users"] = "<circle cx='9' cy='8' r='4'/><path d='M2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1'/><path d='M16 3.2a4 4 0 0 1 0 7.6'/><path d='M22 21v-1a6 6 0 0 0-4-5.6'/>",
        ["user-check"] = "<circle cx='9' cy='8' r='4'/><path d='M3 21v-1a6 6 0 0 1 6-6h1'/><path d='m15 19 2 2 4-4'/>",
        ["user-plus"] = "<circle cx='9' cy='8' r='4'/><path d='M3 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 4.5 2'/><path d='M19 14v6M16 17h6'/>",
        ["chart"] = "<path d='M3 3v18h18'/><rect x='7' y='12' width='3' height='6' rx='.5'/><rect x='12' y='8' width='3' height='10' rx='.5'/><rect x='17' y='5' width='3' height='13' rx='.5'/>",
        ["pie"] = "<path d='M21 12a9 9 0 1 1-9-9v9z'/><path d='M15 3.5A9 9 0 0 1 20.5 9H15z'/>",
        ["truck"] = "<path d='M3 6h11v10H3z'/><path d='M14 9h4l3 3v4h-7'/><circle cx='7' cy='18' r='2'/><circle cx='17' cy='18' r='2'/>",
        ["hardhat"] = "<path d='M2 18h20'/><path d='M4 18v-2a8 8 0 0 1 16 0v2'/><path d='M10 8V5h4v3'/>",
        ["clipboard"] = "<rect x='5' y='4' width='14' height='17' rx='2'/><path d='M9 4V3h6v1'/><path d='M9 10h6M9 14h6M9 18h3'/>",
        ["cart"] = "<circle cx='9' cy='20' r='1.5'/><circle cx='18' cy='20' r='1.5'/><path d='M2 3h3l2.6 12.4a1 1 0 0 0 1 .8h9.7a1 1 0 0 0 1-.8L21 7H6'/>",
        ["package"] = "<path d='m12 3 8 4.5v9L12 21l-8-4.5v-9z'/><path d='m4 7.5 8 4.5 8-4.5'/><path d='M12 12v9'/>",
        ["warehouse"] = "<path d='M3 21V8l9-5 9 5v13'/><path d='M7 21v-8h10v8'/><path d='M7 17h10'/>",
        ["inbox-in"] = "<path d='M12 3v10'/><path d='m8 9 4 4 4-4'/><path d='M3 14h5l1.5 3h5L16 14h5v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'/>",
        ["inbox-out"] = "<path d='M12 13V3'/><path d='m8 7 4-4 4 4'/><path d='M3 14h5l1.5 3h5L16 14h5v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z'/>",
        ["arrows"] = "<path d='M4 8h16'/><path d='m16 4 4 4-4 4'/><path d='M20 16H4'/><path d='m8 20-4-4 4-4'/>",
        ["sliders"] = "<path d='M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0'/><circle cx='16' cy='6' r='2'/><circle cx='10' cy='12' r='2'/><circle cx='18' cy='18' r='2'/>",
        ["clipboard-check"] = "<rect x='5' y='4' width='14' height='17' rx='2'/><path d='M9 4V3h6v1'/><path d='m9 13 2 2 4-4'/>",
        ["wallet"] = "<path d='M19 7V5a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-4'/><path d='M21 11h-5a2 2 0 0 0 0 4h5z'/>",
        ["vault"] = "<rect x='3' y='4' width='18' height='16' rx='2'/><circle cx='12' cy='12' r='3.5'/><path d='M12 8.5V7M12 17v-1.5M7 20v1M17 20v1'/>",
        ["trend-up"] = "<path d='m3 17 6-6 4 4 8-8'/><path d='M15 7h6v6'/>",
        ["trend-down"] = "<path d='m3 7 6 6 4-4 8 8'/><path d='M15 17h6v-6'/>",
        ["book"] = "<path d='M4 4.5A2.5 2.5 0 0 1 6.5 2H20v18H6.5A2.5 2.5 0 0 0 4 22.5z'/><path d='M4 19.5V4.5'/><path d='M8 7h8M8 11h6'/>",
        ["tree"] = "<rect x='3' y='3' width='6' height='5' rx='1'/><rect x='15' y='9.5' width='6' height='5' rx='1'/><rect x='15' y='17' width='6' height='5' rx='1'/><path d='M6 8v11.5h9M6 12h9'/>",
        ["scale"] = "<path d='M12 3v18M7 21h10'/><path d='M5 7h14'/><path d='m5 7-3 7a3 3 0 0 0 6 0z'/><path d='m19 7-3 7a3 3 0 0 0 6 0z'/>",
        ["tag"] = "<path d='M3 12V4a1 1 0 0 1 1-1h8l9 9-9 9z'/><circle cx='8' cy='8' r='1.5'/>",
        ["megaphone"] = "<path d='M3 11v3a1 1 0 0 0 1 1h3l6 4V6L7 10H4a1 1 0 0 0-1 1Z'/><path d='M17 9a4 4 0 0 1 0 6'/><path d='M20 6.5a8 8 0 0 1 0 11'/>",
        ["briefcase"] = "<rect x='3' y='7' width='18' height='13' rx='2'/><path d='M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/><path d='M3 13h18'/>",
        ["calendar"] = "<rect x='3' y='5' width='18' height='16' rx='2'/><path d='M3 10h18M8 3v4M16 3v4'/>",
        ["clock"] = "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3 2'/>",
        ["gift"] = "<rect x='3' y='8' width='18' height='4' rx='1'/><path d='M5 12v9h14v-9'/><path d='M12 8v13'/><path d='M12 8H8.5a2.5 2.5 0 1 1 0-5C11 3 12 8 12 8Z'/><path d='M12 8h3.5a2.5 2.5 0 1 0 0-5C13 3 12 8 12 8Z'/>",
        ["banknote"] = "<rect x='2' y='6' width='20' height='12' rx='2'/><circle cx='12' cy='12' r='2.5'/><path d='M6 12h.01M18 12h.01'/>",
        ["settings"] = "<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.6 1.6 0 0 0-1-1.5 1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.6 1.6 0 0 0 1.5-1 1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1Z'/>",
        ["user"] = "<circle cx='12' cy='8' r='4'/><path d='M4 21v-1a7 7 0 0 1 7-7h2a7 7 0 0 1 7 7v1'/>",
        ["buildings"] = "<path d='M3 21V7l7-4v18'/><path d='M10 21V9l11 4v8'/><path d='M2 21h20'/><path d='M6 9h1M6 13h1M6 17h1M14 15h2M14 18h2'/>",
        ["palette"] = "<path d='M12 3a9 9 0 1 0 0 18c1 0 1.5-.7 1.5-1.5 0-.9-.6-1.2-.6-2 0-.8.7-1.5 1.5-1.5H17a4 4 0 0 0 4-4c0-5-4-9-9-9Z'/><circle cx='7.5' cy='11' r='1'/><circle cx='10' cy='7' r='1'/><circle cx='15' cy='7.5' r='1'/>",
        ["activity"] = "<path d='M3 12h4l3-8 4 16 3-8h4'/>",
        ["file"] = "<path d='M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z'/><path d='M14 3v6h6'/>",
        ["star"] = "<path d='m12 3 2.8 5.7 6.2.9-4.5 4.4 1 6.2L12 17.3l-5.5 2.9 1-6.2L3 9.6l6.2-.9z'/>",
        ["search"] = "<circle cx='11' cy='11' r='7'/><path d='m20 20-3.5-3.5'/>",
        ["plus"] = "<path d='M12 5v14M5 12h14'/>",
        ["history"] = "<path d='M3 12a9 9 0 1 0 3-6.7L3 8'/><path d='M3 3v5h5'/><path d='M12 7v5l3 2'/>",
        ["grid"] = "<rect x='3' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='3' width='7' height='7' rx='1.5'/><rect x='3' y='14' width='7' height='7' rx='1.5'/><rect x='14' y='14' width='7' height='7' rx='1.5'/>",
        ["menu"] = "<path d='M4 6h16M4 12h16M4 18h16'/>",
        ["chevron"] = "<path d='m15 6-6 6 6 6'/>",
        ["chevron-down"] = "<path d='m6 9 6 6 6-6'/>",
        ["bell"] = "<path d='M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9'/><path d='M10.3 21a1.9 1.9 0 0 0 3.4 0'/>",
        ["sun"] = "<circle cx='12' cy='12' r='4'/><path d='M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4'/>",
        ["moon"] = "<path d='M20.5 14.5A8.5 8.5 0 1 1 9.5 3.5a7 7 0 0 0 11 11Z'/>",
        ["logout"] = "<path d='M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4'/><path d='m16 17 5-5-5-5'/><path d='M21 12H9'/>",
        ["switch"] = "<path d='M16 3h5v5'/><path d='m21 3-7 7'/><path d='M8 21H3v-5'/><path d='m3 21 7-7'/>",
    };

    public static bool Exists(string? name) => name is not null && Paths.ContainsKey(name);

    /// <summary>The icon as inline SVG (decorative — screen readers read the adjacent title).</summary>
    public static HtmlString Svg(string? name, int size = 20, string? cssClass = null)
    {
        var inner = name is not null && Paths.TryGetValue(name, out var p) ? p : "<rect x='4' y='4' width='16' height='16' rx='3'/>";
        var cls = cssClass is null ? "" : $" class='{cssClass}'";
        return new HtmlString($"<svg{cls} viewBox='0 0 24 24' width='{size}' height='{size}' fill='none' stroke='currentColor' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round' aria-hidden='true' focusable='false'>{inner}</svg>");
    }
}
