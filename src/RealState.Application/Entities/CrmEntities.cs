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

/// <summary>
/// One campaign-lead row imported from a marketing platform export (e.g. a Facebook Lead Ads Excel).
/// Linked to the <see cref="Customer"/> (lead) matched or created by phone. The platform's own record
/// <see cref="ExternalId"/> is unique per tenant so the same export row is never imported twice. The
/// fixed platform columns are stored as fields; any extra form-question columns go into
/// <see cref="ExtraFieldsJson"/> (a header→answer map) so any form layout can be imported as-is.
/// </summary>
public class CampaignLead : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The export's own record id ("id" column) — dedupe key, unique per tenant.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>The lead/customer this row belongs to (matched or created by phone number).</summary>
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Platform "created_time" — also used as the lead's creation time when it is first created.</summary>
    public DateTime CreatedTime { get; set; }

    // Fixed platform (ad / adset / campaign / form) columns — stored as-is from the export.
    public string? AdId { get; set; }
    public string? AdName { get; set; }
    public string? AdsetId { get; set; }
    public string? AdsetName { get; set; }
    public string? CampaignExternalId { get; set; }
    public string? CampaignName { get; set; }
    public string? FormId { get; set; }
    public string? FormName { get; set; }
    public bool IsOrganic { get; set; }
    public string? Platform { get; set; }
    public string? JobTitle { get; set; }
    public string? LeadStatus { get; set; }

    /// <summary>Remaining (form-specific) columns as a JSON object of {header: answer}.</summary>
    public string? ExtraFieldsJson { get; set; }
}
