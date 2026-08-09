using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Hr.Models;

// ---------- Settings ----------
public class NameFormModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم")]
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true; // kept for entity; not shown in the form
    [Display(Name = "مندوب مبيعات")]
    public bool IsSalesperson { get; set; }    // only meaningful for job roles
}

public class AttendanceFormModel
{
    [Required][DataType(DataType.Time)][Display(Name = "وقت الحضور")]
    public TimeSpan TimeIn { get; set; } = new(9, 0, 0);
    [Required][DataType(DataType.Time)][Display(Name = "وقت الانصراف")]
    public TimeSpan TimeOut { get; set; } = new(17, 0, 0);
    [Display(Name = "الوقت الإضافي")]
    public bool OvertimeEnabled { get; set; }
    [Range(0, 1000)][Display(Name = "الساعة الإضافية تعادل عدد الساعات التالية")]
    public decimal OvertimeNormalHours { get; set; }
}

public class LateRuleInput
{
    public LateBracket Bracket { get; set; }
    public DeductionFraction Fraction { get; set; }
    public bool IsActive { get; set; }
}

public class LateRulesFormModel
{
    public List<LateRuleInput> Rules { get; set; } = new();
}

public class HrSettingsVm
{
    public List<Department> Departments { get; set; } = new();
    public List<JobRole> Roles { get; set; } = new();
    public AttendanceFormModel Attendance { get; set; } = new();
    public List<LateDeductionRule> LateRules { get; set; } = new();
    public decimal DailyHours => (decimal)(Attendance.TimeOut - Attendance.TimeIn).TotalHours;
}

// ---------- Employees ----------
public class EmployeeFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")][Display(Name = "الاسم")]
    public string FullName { get; set; } = string.Empty;
    [Required(ErrorMessage = "رقم الهاتف مطلوب")][Phone(ErrorMessage = "رقم غير صالح")][Display(Name = "رقم الهاتف")]
    public string? Phone { get; set; }
    [Display(Name = "هاتف بديل")] public string? AltPhone { get; set; }
    [DataType(DataType.Date)][Display(Name = "تاريخ الميلاد")] public DateTime? BirthDate { get; set; }
    [Display(Name = "الجنسية")] public string? Nationality { get; set; }
    [Required(ErrorMessage = "الرقم القومي مطلوب")][Display(Name = "الرقم القومي")] public string? NationalId { get; set; }
    [Required(ErrorMessage = "المؤهل الدراسي مطلوب")][Display(Name = "المؤهل الدراسي")] public string? AcademicQualification { get; set; }
    [Display(Name = "محل الإقامة الحالي")] public string? CurrentLocation { get; set; }
    [EmailAddress(ErrorMessage = "بريد غير صالح")][Display(Name = "البريد الإلكتروني")] public string? Email { get; set; }

    [Required(ErrorMessage = "القسم مطلوب")][Display(Name = "القسم")] public Guid? DepartmentId { get; set; }
    [Required(ErrorMessage = "الوظيفة مطلوبة")][Display(Name = "الوظيفة")] public Guid? JobRoleId { get; set; }
    [Required(ErrorMessage = "تاريخ بدء العمل مطلوب")][DataType(DataType.Date)][Display(Name = "تاريخ بدء العمل")] public DateTime? HireDate { get; set; }
    [Display(Name = "نوع التعاقد")] public EmploymentType EmploymentType { get; set; }
    [Display(Name = "تأمين اجتماعي")] public bool SocialInsurance { get; set; }
    [Display(Name = "تأمين طبي")] public bool MedicalInsurance { get; set; }
    [Range(0.01, 999999999999, ErrorMessage = "الراتب الأساسي مطلوب")][Display(Name = "الراتب الأساسي")] public decimal BasicSalary { get; set; }
    [Range(0, 999999999999)][Display(Name = "الحوافز والعمولات")] public decimal IncentivesCommissions { get; set; }

    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Roles { get; set; } = new();
}

public class EmployeeListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Department { get; set; } = "—";
    public string Role { get; set; } = "—";
    public string EmploymentType { get; set; } = string.Empty;
    public decimal BasicSalary { get; set; }
}

// ---------- Vacations ----------
public class VacationFormModel
{
    public Guid Id { get; set; }
    [DataType(DataType.Date)][Display(Name = "تاريخ التقديم")] public DateTime ApplyDate { get; set; } = DateTime.Today;
    [Required(ErrorMessage = "الموظف مطلوب")][Display(Name = "الموظف")] public Guid? EmployeeId { get; set; }
    [Display(Name = "النوع")] public VacationType Type { get; set; }
    [Required][DataType(DataType.Date)][Display(Name = "من")] public DateTime FromDate { get; set; } = DateTime.Today;
    [Required][DataType(DataType.Date)][Display(Name = "إلى")] public DateTime ToDate { get; set; } = DateTime.Today;
    public List<SelectListItem> Employees { get; set; } = new();
}

public class VacationRow
{
    public Guid Id { get; set; }
    public string Employee { get; set; } = string.Empty;
    public VacationType Type { get; set; }
    public DateTime ApplyDate { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Days => (ToDate.Date - FromDate.Date).Days + 1;
}

public class VacationListVm
{
    public List<VacationRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Q { get; set; }
}

// ---------- Advances ----------
public class AdvanceFormModel
{
    public Guid Id { get; set; }
    [DataType(DataType.Date)][Display(Name = "التاريخ")] public DateTime Date { get; set; } = DateTime.Today;
    [Required(ErrorMessage = "الموظف مطلوب")][Display(Name = "الموظف")] public Guid? EmployeeId { get; set; }
    [Range(0.01, 999999999999, ErrorMessage = "المبلغ مطلوب")][Display(Name = "المبلغ")] public decimal Amount { get; set; }
    [Display(Name = "طريقة السداد")] public AdvanceRepaymentMethod RepaymentMethod { get; set; }
    [Range(1, 600)][Display(Name = "عدد أقساط الخصم")] public int InstallmentsCount { get; set; } = 1;
    [DataType(DataType.Date)][Display(Name = "بداية الخصم الشهري")] public DateTime? MonthlyStartDate { get; set; } = DateTime.Today;
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }
    public List<SelectListItem> Employees { get; set; } = new();
}

public class AdvanceRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Employee { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public AdvanceRepaymentMethod RepaymentMethod { get; set; }
    public DisbursementStatus Status { get; set; }
    public decimal Repaid { get; set; }
    public decimal Remaining => Amount - Repaid;
}

public class AdvanceListVm
{
    public List<AdvanceRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Q { get; set; }
}

// ---------- Rewards ----------
public class RewardFormModel
{
    public Guid Id { get; set; }
    [DataType(DataType.Date)][Display(Name = "التاريخ")] public DateTime Date { get; set; } = DateTime.Today;
    [Required(ErrorMessage = "الموظف مطلوب")][Display(Name = "الموظف")] public Guid? EmployeeId { get; set; }
    [Range(0.01, 999999999999, ErrorMessage = "المبلغ مطلوب")][Display(Name = "المبلغ")] public decimal Amount { get; set; }
    [Display(Name = "طريقة الصرف")] public RewardPayVia PayVia { get; set; }
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }
    public List<SelectListItem> Employees { get; set; } = new();
}

public class RewardRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Employee { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public RewardPayVia PayVia { get; set; }
    public PayStatus Status { get; set; }
}

public class RewardListVm
{
    public List<RewardRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Q { get; set; }
}
