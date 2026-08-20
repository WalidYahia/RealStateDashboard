namespace RealState.Application.Enums;

/// <summary>How a lead reached the business (drives the source sub-selection).</summary>
public enum LeadChannel
{
    SocialMedia = 0,  // fill source with social-media platforms
    Campaign = 1,     // fill source with active marketing campaigns
    Salesperson = 2,  // source fixed to "مندوب مبيعات"
    Other = 3,        // fill source with the remaining sources
}

/// <summary>Optional interest level of a lead.</summary>
public enum LeadInterest
{
    Interested = 0,     // مهتم
    NotInterested = 1,  // غير مهتم
}

/// <summary>Kind of a customer/lead communication-log entry (drives the auto vs manual label).</summary>
public enum CustomerLogKind
{
    Manual = 0,
    Created = 1,
    StatusChange = 2,
    Conversion = 3,
    WhatsApp = 4,
}
