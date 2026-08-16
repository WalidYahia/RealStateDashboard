using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// A work task assigned to an employee. Numbered T-1, T-2, … per tenant. Carries a time-log
/// (auto-appended on every status change) and file attachments.
/// </summary>
public class WorkTask : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Serial within the tenant, shown as T-{Number}.</summary>
    public int Number { get; set; }

    /// <summary>Date the task was assigned.</summary>
    public DateTime AssignedOn { get; set; } = DateTime.Today;

    /// <summary>Department of the assignee (drives the employee picker).</summary>
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Employee the task is assigned to.</summary>
    public Guid AssigneeEmployeeId { get; set; }
    public Employee? Assignee { get; set; }

    /// <summary>User who created/assigned the task (null for the static host). Name is denormalized for display.</summary>
    public Guid? AssignedByUserId { get; set; }
    public string AssignedByName { get; set; } = string.Empty;

    /// <summary>When work should start.</summary>
    public DateTime? StartAt { get; set; }
    /// <summary>Due date.</summary>
    public DateTime? DueAt { get; set; }

    public string Description { get; set; } = string.Empty;
    public TaskSeverity Severity { get; set; } = TaskSeverity.Normal;
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Todo;

    public ICollection<WorkTaskLog> Logs { get; set; } = new List<WorkTaskLog>();
    public ICollection<WorkTaskAttachment> Attachments { get; set; } = new List<WorkTaskAttachment>();
}

/// <summary>A single time-log entry on a task (status change or a manual note).</summary>
public class WorkTaskLog : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkTaskId { get; set; }
    public WorkTask? Task { get; set; }

    public DateTime At { get; set; } = DateTime.Now;
    public string Text { get; set; } = string.Empty;

    /// <summary>Name of the person who added the log (auto-set from the current user).</summary>
    public string ByName { get; set; } = string.Empty;
    public Guid? ByUserId { get; set; }
}

/// <summary>A file attached to a task (stored in the database).</summary>
public class WorkTaskAttachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkTaskId { get; set; }
    public WorkTask? Task { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
