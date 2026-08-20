using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Tasks.Models;

/// <summary>An employee option carrying its department, so the picker can cascade from the department.</summary>
public record EmpOption(Guid Id, string Name, Guid? DepartmentId);

public class TaskFormModel
{
    public Guid Id { get; set; }

    [DataType(DataType.Date)][Display(Name = "تاريخ الإسناد")]
    public DateTime AssignedOn { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "الموظف مطلوب")][Display(Name = "الموظف (المُكلَّف)")]
    public Guid? AssigneeEmployeeId { get; set; }

    [DataType(DataType.Date)][Display(Name = "تاريخ الاستحقاق")]
    public DateTime? DueAt { get; set; }

    [Required(ErrorMessage = "وصف المهمة مطلوب")][Display(Name = "وصف المهمة")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "الأهمية")]
    public TaskSeverity Severity { get; set; } = TaskSeverity.Normal;

    public List<EmpOption> Employees { get; set; } = new();
}

public class TaskRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime AssignedOn { get; set; }
    public string AssignedBy { get; set; } = "—";
    public string Assignee { get; set; } = "—";
    public string Department { get; set; } = "—";
    public DateTime? DueAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public TaskSeverity Severity { get; set; }
    public WorkTaskStatus Status { get; set; }
}

public class TaskListVm
{
    public List<TaskRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? AssigneeId { get; set; }
    public string? AssignedBy { get; set; }
    public Guid? DepartmentId { get; set; }
    public WorkTaskStatus? Status { get; set; }
    public TaskSeverity? Severity { get; set; }

    public List<SelectListItem> Employees { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Assigners { get; set; } = new();
}

public class MyTasksVm
{
    public List<TaskRow> Assigned { get; set; } = new();     // assigned to me
    public List<TaskRow> Delegated { get; set; } = new();    // assigned by me
    public List<TaskRow> SelfAssigned { get; set; } = new(); // assigned by me, to me

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? AssignedBy { get; set; }   // tab 1 filter: who assigned to me (by name)
    public Guid? AssignedToId { get; set; }   // tab 2 filter: who I assigned to
    public WorkTaskStatus? Status { get; set; }
    public TaskSeverity? Severity { get; set; }

    public List<SelectListItem> Assigners { get; set; } = new();
    public List<SelectListItem> Assignees { get; set; } = new();
    public string ActiveTab { get; set; } = "assigned";
}

// ---- Print ----
public record TaskLogLine(DateTime At, string Text, string ByName);

public class TaskPrintRow
{
    public int Number { get; set; }
    public DateTime AssignedOn { get; set; }
    public string AssignedBy { get; set; } = "—";
    public string Assignee { get; set; } = "—";
    public string Department { get; set; } = "—";
    public DateTime? StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public TaskSeverity Severity { get; set; }
    public WorkTaskStatus Status { get; set; }
    public List<TaskLogLine> Logs { get; set; } = new();
}

public class TaskPrintVm
{
    public List<TaskPrintRow> Rows { get; set; } = new();
    public bool WithLog { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class LogFormModel
{
    public Guid TaskId { get; set; }
    [Required(ErrorMessage = "البيان مطلوب")][Display(Name = "البيان")]
    public string Text { get; set; } = string.Empty;
}

public class StatusFormModel
{
    public Guid TaskId { get; set; }
    [Display(Name = "الحالة")] public WorkTaskStatus Status { get; set; }
    [Display(Name = "ملاحظة (اختياري)")] public string? Note { get; set; }
}
