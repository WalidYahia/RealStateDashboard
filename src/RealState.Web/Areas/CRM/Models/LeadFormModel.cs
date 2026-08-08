using System.ComponentModel.DataAnnotations;
using RealState.Application.Enums;

namespace RealState.Web.Areas.CRM.Models;

public class LeadFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم العميل المحتمل مطلوب")]
    [Display(Name = "الاسم")]
    public string FullName { get; set; } = string.Empty;

    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }

    [Display(Name = "مصدر/حملة")]
    public string? Source { get; set; }

    [Display(Name = "الحالة")]
    public LeadStatus Status { get; set; } = LeadStatus.New;

    [Display(Name = "القيمة المتوقعة (جنيه)")]
    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    public decimal EstimatedValue { get; set; }
}
