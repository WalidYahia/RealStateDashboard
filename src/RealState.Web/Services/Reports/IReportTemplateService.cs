using RealState.Application.Entities;

namespace RealState.Web.Services.Reports;

/// <summary>
/// Manages the per-tenant report/print template (a 1-page PDF used as the background/design layer of
/// every report). One template is active per tenant; reports overlay their content on its rendered image.
/// </summary>
public interface IReportTemplateService
{
    /// <summary>The current tenant's active template metadata (null when none).</summary>
    Task<ReportTemplate?> GetActiveAsync(CancellationToken ct = default);

    /// <summary>True when the current tenant has an active template (cheap check for views).</summary>
    Task<bool> HasActiveAsync(CancellationToken ct = default);

    /// <summary>The rendered PNG bytes of the active template (cached), or null when there is none.</summary>
    Task<byte[]?> GetActiveBackgroundAsync(CancellationToken ct = default);

    /// <summary>Validates and stores a new template, making it the active one. Existing template kept on failure.</summary>
    Task<(bool Ok, string? Error)> UploadAsync(IFormFile? file, CancellationToken ct = default);
}
