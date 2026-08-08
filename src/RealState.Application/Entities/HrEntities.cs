using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// A company employee. Salespersons (Type = Salesperson) are created from the Customers section;
/// the same entity backs the HR module (attendance, tasks, …) later.
/// </summary>
public class Employee : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public EmployeeType Type { get; set; } = EmployeeType.Salesperson;
    public bool IsActive { get; set; } = true;
    public DateTime? HireDate { get; set; }
}
