using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A marketing/advertising campaign. Its performance is tracked over time via CampaignUpdates.</summary>
public class Campaign : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public CampaignPlatform Platform { get; set; }
    public CampaignType Type { get; set; }
    public CampaignObjective Objective { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Active;

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal Budget { get; set; }
    public string? Notes { get; set; }

    public ICollection<CampaignUpdate> Updates { get; set; } = new List<CampaignUpdate>();
}

/// <summary>
/// One incremental performance reading for a campaign. Each row holds the <b>delta</b> since the previous
/// reading, so the campaign's cumulative totals are the SUM of its updates and the full history is preserved.
/// </summary>
public class CampaignUpdate : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    /// <summary>The reading date this delta belongs to.</summary>
    public DateTime Date { get; set; }

    public int Reach { get; set; }         // impressions delta
    public int Leads { get; set; }         // leads delta
    public decimal Cost { get; set; }      // spend delta
    public decimal Sales { get; set; }     // resulting sales delta
    public int Reservations { get; set; }  // reservations delta

    public string? Notes { get; set; }
}
