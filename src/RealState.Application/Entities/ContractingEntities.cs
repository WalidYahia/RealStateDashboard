using System.ComponentModel.DataAnnotations.Schema;
using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A contractor (مقاول) the company assigns work orders to. Same shape as a supplier, in its own table.</summary>
public class Contractor : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// A work order (أمر شغل) assigned to a contractor and charged to a project. Carries a single work item
/// (بيان/وحدة/كمية/فئة) plus progress fields set after creation: execution %, uplift %, deductions.
/// المستحق = كمية × فئة × نسبة التنفيذ. المستحق بعد التعلية = المستحق × (1 − نسبة التعلية) − الخصومات.
/// </summary>
public class WorkOrder : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Year-prefixed serial per tenant, shown as WO-202600001.</summary>
    public int Number { get; set; }
    public DateTime OrderDate { get; set; }

    public Guid ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    /// <summary>Mandatory project the work order is charged to.</summary>
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    // The single (non-editable-count) work item.
    public string ItemDescription { get; set; } = string.Empty;  // البيان
    public string Unit { get; set; } = string.Empty;             // الوحدة (م³، دفعة، …)
    public decimal Quantity { get; set; }                        // الكمية
    public decimal Rate { get; set; }                            // الفئة (سعر الوحدة)

    // Progress fields — default 0, set later from the orders list (each change is logged).
    public decimal ExecutionPercent { get; set; }   // نسبة التنفيذ (0–100)
    public decimal UpliftPercent { get; set; }       // نسبة التعلية (0–100)
    public decimal Deductions { get; set; }          // الخصومات

    public string? Notes { get; set; }

    /// <summary>الإجمالي = الكمية × الفئة (قيمة البند قبل نسبة التنفيذ).</summary>
    [NotMapped] public decimal Total => Quantity * Rate;
    /// <summary>المستحق = الإجمالي × نسبة التنفيذ.</summary>
    [NotMapped] public decimal Due => Total * (ExecutionPercent / 100m);
    /// <summary>المستحق بعد التعلية (الإجمالي الفعلي) = المستحق × (1 − نسبة التعلية) − الخصومات.</summary>
    [NotMapped] public decimal ActualTotal => Due * (1m - UpliftPercent / 100m) - Deductions;
}

/// <summary>An audit entry recording one change to a work order's progress field (value + when).</summary>
public class WorkOrderLog : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder? Order { get; set; }

    public WorkOrderField Field { get; set; }
    public decimal Value { get; set; }
    public DateTime At { get; set; }
    public string? ByName { get; set; }
}

/// <summary>A payment made to a contractor against a work order — a safe Expense movement.</summary>
public class WorkOrderPayment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    public Guid? WorkOrderId { get; set; }
    public WorkOrder? Order { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaidDate { get; set; }
    public Guid SafeId { get; set; }

    /// <summary>Pay-receipt number — equal to the linked Expense transaction's serial.</summary>
    public int ReceiptNo { get; set; }
    public string? Description { get; set; }
}
