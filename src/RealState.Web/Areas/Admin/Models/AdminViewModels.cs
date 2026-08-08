using System.ComponentModel.DataAnnotations;

namespace RealState.Web.Areas.Admin.Models;

public class TenantListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool HasLogo { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateTenantViewModel
{
    [Required(ErrorMessage = "اسم المؤسسة مطلوب")]
    [Display(Name = "اسم المؤسسة")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم المدير (للعرض) مطلوب")]
    [Display(Name = "اسم المدير (للعرض)")]
    public string AdminDisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم هاتف المدير مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم هاتف المدير (للدخول)")]
    public string AdminPhone { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "بريد المدير الإلكتروني (للدخول)")]
    public string AdminEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "يجب أن تكون كلمة المرور 8 أحرف على الأقل")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة مرور المدير")]
    public string AdminPassword { get; set; } = string.Empty;
}

public class EditTenantViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المؤسسة مطلوب")]
    [Display(Name = "اسم المؤسسة")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; }

    public bool HasLogo { get; set; }

    [Display(Name = "حذف الشعار الحالي")]
    public bool RemoveLogo { get; set; }
}

public class UserListItem
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public bool IsSuperAdmin { get; set; }
    public int PermissionCount { get; set; }
}

public class CreateUserViewModel
{
    [Required(ErrorMessage = "اسم المستخدم (للعرض) مطلوب")]
    [Display(Name = "اسم المستخدم (للعرض)")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف (للدخول)")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "البريد الإلكتروني (اختياري)")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "يجب أن تكون كلمة المرور 8 أحرف على الأقل")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "المؤسسة")]
    public Guid TenantId { get; set; }

    /// <summary>Permission names granted directly to this user (checkbox privileges).</summary>
    public List<string> SelectedPermissions { get; set; } = new();

    // Populated for the form
    public List<TenantOption> Tenants { get; set; } = new();
    public bool CanChooseTenant { get; set; }
}

public class EditUserViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المستخدم (للعرض) مطلوب")]
    [Display(Name = "اسم المستخدم (للعرض)")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف (للدخول)")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "البريد الإلكتروني (اختياري)")]
    public string? Email { get; set; }

    [Display(Name = "الحساب مفعّل")]
    public bool IsActive { get; set; }

    /// <summary>Permission names granted directly to this user (checkbox privileges).</summary>
    public List<string> SelectedPermissions { get; set; } = new();

    public string TenantName { get; set; } = string.Empty;

    /// <summary>SuperAdmin users hold every privilege implicitly; the checkbox grid is hidden for them.</summary>
    public bool IsSuperAdmin { get; set; }
}

public class ResetPasswordViewModel
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور الجديدة مطلوبة")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "يجب أن تكون كلمة المرور 8 أحرف على الأقل")]
    [DataType(DataType.Password)]
    [Display(Name = "كلمة المرور الجديدة")]
    public string NewPassword { get; set; } = string.Empty;
}

public record TenantOption(Guid Id, string Name);
