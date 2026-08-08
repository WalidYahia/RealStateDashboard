using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A reusable stage name (master list, defined under Settings) that projects draw from.</summary>
public class StageDefinition : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A stage/phase of a project with planned vs actual dates and its own logs.</summary>
public class ProjectStage : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime? PlannedStartDate { get; set; }
    public DateTime? ActualStartDate { get; set; }
    public DateTime? PlannedEndDate { get; set; }
    public DateTime? ActualEndDate { get; set; }

    public string? Notes { get; set; }

    public ICollection<StageActivity> Activities { get; set; } = new List<StageActivity>();
    public ICollection<StageExpense> Expenses { get; set; } = new List<StageExpense>();
}

/// <summary>A dated activity log entry under a stage.</summary>
public class StageActivity : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StageId { get; set; }
    public ProjectStage? Stage { get; set; }

    public string Activity { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

/// <summary>An expense recorded under a stage.</summary>
public class StageExpense : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid StageId { get; set; }
    public ProjectStage? Stage { get; set; }

    public DateTime Date { get; set; }
    public TimeSpan Time { get; set; }
    public decimal Value { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A sellable unit inside a Building/Mall project.</summary>
public class ProjectUnit : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Number { get; set; }
    public UnitStatus Status { get; set; } = UnitStatus.NotReady;

    /// <summary>Total area in square meters.</summary>
    public decimal AreaSqm { get; set; }
    /// <summary>Unit price — pre-fills the sale total when this unit is selected.</summary>
    public decimal Price { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A file attached to a project (image / doc / pdf / excel / text). Stored in the database.</summary>
public class ProjectAttachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}
