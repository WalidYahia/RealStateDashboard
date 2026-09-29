using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace RealState.Web.Models;

// =====================================================================================================
//  Shared filter bar (Views/Shared/_FilterBar.cshtml + wwwroot/js/filter-bar.js).
//  Every list/report page describes ONLY its own filters with the fluent builder below and renders
//  <partial name="_FilterBar" model="filter" />. The component is purely presentational: it posts the
//  same GET parameter names / values the page always used, so controller filter logic is untouched.
//
//  var filter = FilterBarVm.For(Url.Action("Index"))
//      .DateRange(F(Model.From), F(Model.To), "تاريخ الأمر")
//      .Select("projectId", "المشروع", projects, projId)
//      .Print(Url.Action("PrintList", new { from = F(Model.From), to = F(Model.To), projectId = projId }))
//      .Excel(Url.Action("Excel", new { ... }));
// =====================================================================================================

public enum FilterFieldType { Select, Text, Date }

/// <summary>One filter input (a select, a free-text box or a single date).</summary>
public sealed class FilterField
{
    public string Name { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public FilterFieldType Type { get; init; }
    /// <summary>Current value exactly as the page posts it (e.g. a Guid string, an enum name or an int).</summary>
    public string? Value { get; init; }
    public List<SelectListItem> Options { get; init; } = new();
    /// <summary>Text of the empty ("no filter") option of a select; null = no empty option.</summary>
    public string? AllText { get; init; } = "الكل";
    public string? Placeholder { get; init; }
    public bool Searchable { get; init; } = true;
    /// <summary>Spans two grid columns (long option texts, e.g. accounts).</summary>
    public bool Wide { get; init; }

    public bool IsActive => !string.IsNullOrEmpty(Value);
}

/// <summary>A quick range button (اليوم، آخر أسبوع …). Empty From/To clear the range (e.g. «الكل»).</summary>
public sealed record FilterPreset(string Label, string From, string To);

/// <summary>The from/to date (or date-time) range with its quick presets.</summary>
public sealed class FilterDateRange
{
    public string FromName { get; init; } = "from";
    public string ToName { get; init; } = "to";
    public string? From { get; init; }
    public string? To { get; init; }
    public string Label { get; init; } = "الفترة";
    public string FromLabel { get; init; } = "من";
    public string ToLabel { get; init; } = "إلى";
    /// <summary>"date" or "datetime-local" — presets fill the time as 00:00 / 23:59 for the latter.</summary>
    public string InputType { get; init; } = "date";
    public List<FilterPreset> Presets { get; init; } = new();
    /// <summary>Apply the filter as soon as a preset is chosen (reports); otherwise presets only fill the dates.</summary>
    public bool AutoSubmitPresets { get; init; }
}

/// <summary>A secondary action in the footer (print / Excel …) — a link built with the current filter values.</summary>
public sealed record FilterAction(string Text, string Url, string Icon, bool NewTab);

public sealed class FilterBarVm
{
    public string Action { get; private set; } = string.Empty;
    public string Title { get; private set; } = "تصفية النتائج";
    public string SubmitText { get; private set; } = "بحث";
    public string? FormId { get; private set; }
    public List<KeyValuePair<string, string?>> Hidden { get; } = new();
    public FilterDateRange? Range { get; private set; }
    public List<FilterField> Fields { get; } = new();
    public List<FilterAction> Actions { get; } = new();
    public IHtmlContent? Summary { get; private set; }
    private string? _resetUrl;
    private bool _noReset;

    /// <summary>Number of non-date filters currently applied (shown as a badge).</summary>
    public int ActiveCount => Fields.Count(f => f.IsActive);

    /// <summary>"Clear filters" link: the page itself with only its hidden route values — i.e. a fresh open
    /// (which applies the page's default range, e.g. today).</summary>
    public string? ResetUrl
    {
        get
        {
            if (_noReset) return null;
            if (_resetUrl != null) return _resetUrl;
            var qs = string.Join("&", Hidden.Where(h => !string.IsNullOrEmpty(h.Value))
                .Select(h => Uri.EscapeDataString(h.Key) + "=" + Uri.EscapeDataString(h.Value!)));
            return qs.Length == 0 ? Action : Action + (Action.Contains('?') ? "&" : "?") + qs;
        }
    }

    public static FilterBarVm For(string? action) => new() { Action = action ?? string.Empty };

    public FilterBarVm WithTitle(string title) { Title = title; return this; }
    public FilterBarVm SubmitLabel(string text) { SubmitText = text; return this; }
    public FilterBarVm Id(string id) { FormId = id; return this; }
    public FilterBarVm Hide(string name, object? value) { Hidden.Add(new(name, value?.ToString())); return this; }
    public FilterBarVm Reset(string? url) { _resetUrl = url; return this; }
    public FilterBarVm NoReset() { _noReset = true; return this; }
    public FilterBarVm WithSummary(IHtmlContent html) { Summary = html; return this; }

    /// <summary>From/to range. Values are passed pre-formatted exactly as the page posts them.</summary>
    public FilterBarVm DateRange(string? from, string? to, string? label = null, IEnumerable<FilterPreset>? presets = null,
        bool withTime = false, bool autoSubmitPresets = false, string fromLabel = "من", string toLabel = "إلى")
    {
        Range = new FilterDateRange
        {
            From = from, To = to, Label = string.IsNullOrWhiteSpace(label) ? "الفترة" : $"الفترة ({label})",
            FromLabel = fromLabel, ToLabel = toLabel,
            InputType = withTime ? "datetime-local" : "date",
            Presets = (presets ?? FilterPresets.Standard()).ToList(),
            AutoSubmitPresets = autoSubmitPresets
        };
        return this;
    }

    public FilterBarVm Select(string name, string label, IEnumerable<SelectListItem> options, object? value,
        string? allText = "الكل", bool searchable = true, bool wide = false)
    {
        Fields.Add(new FilterField
        {
            Name = name, Label = label, Type = FilterFieldType.Select, Value = Str(value),
            Options = options.ToList(), AllText = allText, Searchable = searchable, Wide = wide
        });
        return this;
    }

    public FilterBarVm Text(string name, string label, string? value, string? placeholder = null)
    {
        Fields.Add(new FilterField { Name = name, Label = label, Type = FilterFieldType.Text, Value = value, Placeholder = placeholder });
        return this;
    }

    /// <summary>A single date input (e.g. «حتى تاريخ» / «اليوم»); value pre-formatted yyyy-MM-dd.</summary>
    public FilterBarVm Date(string name, string label, string? value)
    {
        Fields.Add(new FilterField { Name = name, Label = label, Type = FilterFieldType.Date, Value = value });
        return this;
    }

    public FilterBarVm Print(string? url, string text = "طباعة (PDF)") => Link(text, url, "🖨", newTab: true);
    public FilterBarVm Excel(string? url, string text = "Excel") => Link(text, url, "⬇", newTab: false);

    public FilterBarVm Link(string text, string? url, string icon, bool newTab)
    {
        if (!string.IsNullOrEmpty(url)) Actions.Add(new FilterAction(text, url, icon, newTab));
        return this;
    }

    /// <summary>Value → the string a select/input compares against (Guid / int / enum / string).</summary>
    private static string? Str(object? v) => v switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString()
    };
}

/// <summary>Common preset sets (dates formatted yyyy-MM-dd; the component adds times for date-time ranges).</summary>
public static class FilterPresets
{
    private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>اليوم · آخر أسبوع · آخر شهر — the system-wide default.</summary>
    public static IEnumerable<FilterPreset> Standard()
    {
        var t = DateTime.Today;
        yield return new("اليوم", D(t), D(t));
        yield return new("آخر أسبوع", D(t.AddDays(-7)), D(t));
        yield return new("آخر شهر", D(t.AddMonths(-1)), D(t));
    }

    public static FilterPreset LastYear()
    {
        var t = DateTime.Today;
        return new("آخر سنة", D(t.AddYears(-1)), D(t));
    }

    /// <summary>Clears both dates (no date filter).</summary>
    public static FilterPreset All(string label = "الكل") => new(label, "", "");

    public static FilterPreset ThisYear() { var t = DateTime.Today; return new("هذا العام", $"{t.Year}-01-01", D(t)); }
    public static FilterPreset PreviousYear() { var y = DateTime.Today.Year - 1; return new("العام السابق", $"{y}-01-01", $"{y}-12-31"); }
    public static FilterPreset ThisMonth() { var t = DateTime.Today; return new("هذا الشهر", D(new DateTime(t.Year, t.Month, 1)), D(t)); }
}
