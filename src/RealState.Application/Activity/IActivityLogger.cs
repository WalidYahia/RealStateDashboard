namespace RealState.Application.Activity;

/// <summary>A single user action to record. UserId/UserName/TenantId fall back to the current user/tenant when null.</summary>
public sealed record ActivityEntry(
    string ActionType,
    string Controller,
    string Action,
    string Method,
    string? Area = null,
    string? Path = null,
    string? Description = null,
    string? IpAddress = null,
    Guid? UserId = null,
    string? UserName = null,
    Guid? TenantId = null);

public interface IActivityLogger
{
    Task LogAsync(ActivityEntry entry, CancellationToken ct = default);
}

/// <summary>Coarse action categories used for filtering the activity log.</summary>
public static class ActivityActionType
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Login = "Login";
    public const string Logout = "Logout";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[] { Create, Update, Delete, Login, Logout, Other };

    /// <summary>Best-effort category from the action name (POST actions only).</summary>
    public static string Classify(string action)
    {
        var a = action.ToLowerInvariant();
        if (a is "logout") return Logout;
        if (a is "login") return Login;
        if (a.Contains("delete") || a is "cancelpayment") return Delete;
        if (a.Contains("create") || a is "pay") return Create;
        if (a.Contains("edit") || a.Contains("update") || a.Contains("toggle")
            || a.Contains("reset") || a.Contains("branding") || a.Contains("upload")
            || a.Contains("selecttenant") || a.Contains("form")) return Update;
        return Other;
    }

    /// <summary>Arabic label for display.</summary>
    public static string Ar(string type) => type switch
    {
        Create => "إضافة",
        Update => "تعديل",
        Delete => "حذف",
        Login => "تسجيل دخول",
        Logout => "تسجيل خروج",
        _ => "أخرى",
    };

    /// <summary>A generic Arabic description used when the action didn't provide a richer one.</summary>
    public static string Describe(string actionType, string controller)
    {
        if (actionType == Login) return "تسجيل الدخول";
        if (actionType == Logout) return "تسجيل الخروج";
        var entity = EntityName(controller);
        return actionType switch
        {
            Create => $"إضافة {entity}",
            Update => $"تعديل {entity}",
            Delete => $"حذف {entity}",
            _ => $"إجراء على {entity}",
        };
    }

    private static string EntityName(string controller) => controller switch
    {
        "Projects" => "مشروع",
        "Sales" => "عقد بيع",
        "Collections" => "تحصيل قسط",
        "Safes" => "خزنة",
        "Expenses" => "مصروف",
        "Incomes" => "إيراد",
        "Customers" => "عميل",
        "Salespersons" => "مندوب",
        "Leads" => "عميل محتمل",
        "Campaigns" => "حملة تسويقية",
        "Users" => "مستخدم",
        "Tenants" => "مؤسسة",
        "Stages" => "مرحلة مشروع",
        "StageDefinitions" => "تعريف مرحلة",
        "Settings" => "الإعدادات",
        "Host" => "المؤسسة الحالية",
        "Account" => "الحساب",
        _ => controller,
    };
}
