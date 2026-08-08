using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Projects.Models;

public class ProjectFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "كود المشروع مطلوب")]
    [StringLength(50, ErrorMessage = "الكود طويل جدًا")]
    [Display(Name = "الكود")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "اسم المشروع مطلوب")]
    [Display(Name = "اسم المشروع")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "النوع")]
    public ProjectType Type { get; set; }

    [Display(Name = "الموقع")]
    public string? Location { get; set; }

    [DataType(DataType.Date)][Display(Name = "بداية مخططة")] public DateTime? PlannedStartDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "بداية فعلية")] public DateTime? ActualStartDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "نهاية مخططة")] public DateTime? PlannedEndDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "نهاية فعلية")] public DateTime? ActualEndDate { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public bool HasHeroImage { get; set; }
    [Display(Name = "حذف صورة الغلاف الحالية")]
    public bool RemoveHeroImage { get; set; }
}

public class StageDefinitionFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم المرحلة مطلوب")]
    [Display(Name = "اسم المرحلة")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "الترتيب")]
    public int SortOrder { get; set; }

    [Display(Name = "مفعّلة")]
    public bool IsActive { get; set; } = true;
}

public class UnitFormModel
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    [Required(ErrorMessage = "اسم الوحدة مطلوب")]
    [Display(Name = "اسم الوحدة")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "رقم الوحدة")]
    public string? Number { get; set; }

    [Display(Name = "الحالة")]
    public UnitStatus Status { get; set; } = UnitStatus.NotReady;

    [Range(0, 9999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "المساحة (م²)")]
    public decimal AreaSqm { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "سعر الوحدة (ج.م)")]
    public decimal Price { get; set; }

    [Display(Name = "وصف الوحدة")]
    public string? Description { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public class StageFormModel
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }

    [Required(ErrorMessage = "المرحلة مطلوبة")]
    [Display(Name = "المرحلة")]
    public string Name { get; set; } = string.Empty;

    [DataType(DataType.Date)][Display(Name = "بداية مخططة")] public DateTime? PlannedStartDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "بداية فعلية")] public DateTime? ActualStartDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "نهاية مخططة")] public DateTime? PlannedEndDate { get; set; }
    [DataType(DataType.Date)][Display(Name = "نهاية فعلية")] public DateTime? ActualEndDate { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<SelectListItem> Definitions { get; set; } = new();
}

public class ActivityFormModel
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }

    [Required(ErrorMessage = "النشاط مطلوب")]
    [Display(Name = "النشاط")]
    public string Activity { get; set; } = string.Empty;

    [Required][DataType(DataType.Date)][Display(Name = "التاريخ")]
    public DateTime Date { get; set; } = DateTime.Today;
}

public class ExpenseFormModel
{
    public Guid Id { get; set; }
    public Guid StageId { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "التاريخ")]
    public DateTime Date { get; set; } = DateTime.Today;

    [Display(Name = "الوقت")]
    public TimeSpan Time { get; set; }

    [Range(0, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "القيمة (ج.م)")]
    public decimal Value { get; set; }

    [Required(ErrorMessage = "اختر الخزنة")]
    [Display(Name = "الخزنة")]
    public Guid SafeId { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

// A manual project-level expense (added from the project المصاريف tab).
public class ProjectExpenseFormModel
{
    public Guid ProjectId { get; set; }

    [DataType(DataType.Date)][Display(Name = "التاريخ")]
    public DateTime Date { get; set; } = DateTime.Today;

    [Range(0.01, 999999999999, ErrorMessage = "قيمة غير صالحة")]
    [Display(Name = "القيمة (ج.م)")]
    public decimal Value { get; set; }

    [Display(Name = "البيان / الوصف")]
    public string? Description { get; set; }

    [Display(Name = "الخزنة")]
    public Guid? SafeId { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();
}

// One row in the project expenses list.
public class ProjectExpenseRow
{
    public Guid Id { get; set; }
    public int Serial { get; set; }
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public RealState.Application.Enums.TxnSource Source { get; set; }
    public bool CanDelete { get; set; }
}

// Start/end a stage on a chosen date (logged as a stage activity too).
public class StageStateModel
{
    public Guid StageId { get; set; }
    public bool IsEnd { get; set; }

    [Required][DataType(DataType.Date)][Display(Name = "التاريخ")]
    public DateTime Date { get; set; } = DateTime.Today;
}

public class ProjectListItem
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProjectType Type { get; set; }
    public string? Location { get; set; }
    public bool HasHero { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public DateTime? ActualEndDate { get; set; }
    public int UnitsTotal { get; set; }
    public int UnitsSold { get; set; }
    public int UnitsAvailable { get; set; }
    public int StagesCount { get; set; }
}

public class ProjectSummaryRow
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ProjectType Type { get; set; }
    public string? Location { get; set; }
    public int UnitsTotal { get; set; }
    public int UnitsSold { get; set; }
    public int StagesCount { get; set; }
    public decimal TotalExpenses { get; set; }
    public DateTime? PlannedEnd { get; set; }
    public DateTime? ActualEnd { get; set; }
}

public class ProjectsIndexVm
{
    public int TotalProjects { get; set; }
    public int Buildings { get; set; }
    public int Malls { get; set; }
    public int Lands { get; set; }
    public int TotalUnits { get; set; }
    public int SoldUnits { get; set; }
    public int AvailableUnits { get; set; }
    public List<ProjectListItem> Projects { get; set; } = new();
}

public class ProjectDetailsVm
{
    public Project Project { get; set; } = default!;
    public List<ProjectUnit> Units { get; set; } = new();
    public List<ProjectAttachment> Attachments { get; set; } = new();
    public List<ProjectStage> Stages { get; set; } = new();
    public Dictionary<Guid, int> StageActivityCounts { get; set; } = new();

    public decimal TotalExpenses { get; set; }
    public DateTime? LastExpenseDate { get; set; }
    public List<ProjectExpenseRow> Expenses { get; set; } = new();
    public ProjectStage? CurrentStage { get; set; }
    /// <summary>Human notes about any stage delayed vs its planned start/end (drives the alert tooltip).</summary>
    public List<string> DelayedStageNotes { get; set; } = new();

    public int UnitsTotal => Units.Count;
    public int UnitsSold => Units.Count(u => u.Status == UnitStatus.Sold);
    public int UnitsAvailable => Units.Count(u => u.Status == UnitStatus.Available);
    public int UnitsNotReady => Units.Count(u => u.Status == UnitStatus.NotReady);
    public bool HasUnits => Project.Type is ProjectType.Building or ProjectType.Mall;
    public bool HasDelay => DelayedStageNotes.Count > 0;
}
