using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// One communication/action logged against a customer (while a lead or after conversion): a manual note,
/// a status change, the conversion event, or a WhatsApp message. Only the customer's assigned salesperson
/// manages these entries.
/// </summary>
public class CustomerLog : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime Date { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public CustomerLogKind Kind { get; set; } = CustomerLogKind.Manual;

    /// <summary>Name of the person who logged the entry (auto-set from the current user).</summary>
    public string ByName { get; set; } = string.Empty;
    public Guid? ByUserId { get; set; }
}
