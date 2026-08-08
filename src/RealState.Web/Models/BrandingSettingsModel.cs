using System.ComponentModel.DataAnnotations;

namespace RealState.Web.Models;

public class BrandingSettingsModel
{
    [Required(ErrorMessage = "اسم المؤسسة مطلوب")]
    [Display(Name = "اسم المؤسسة")]
    public string Name { get; set; } = string.Empty;

    public bool HasLogo { get; set; }

    [Display(Name = "حذف الشعار الحالي")]
    public bool RemoveLogo { get; set; }
}
