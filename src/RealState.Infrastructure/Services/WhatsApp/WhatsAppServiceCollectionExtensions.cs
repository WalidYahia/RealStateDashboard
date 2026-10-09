using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RealState.Application.WhatsApp;

namespace RealState.Infrastructure.Services.WhatsApp;

public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IWhatsAppService"/>, <see cref="IWhatsAppInstanceService"/> and
    /// <see cref="IWhatsAppCampaignService"/> (WaPilot) bound to the <c>WhatsApp</c> configuration section.
    /// Missing credentials do not fail startup; calls throw until they are configured
    /// (check <see cref="IWhatsAppService.IsConfigured"/> to skip optional notifications).
    /// </summary>
    public static IServiceCollection AddWhatsApp(this IServiceCollection services, IConfiguration configuration,
        string sectionName = WhatsAppOptions.SectionName)
    {
        services.Configure<WhatsAppOptions>(configuration.GetSection(sectionName));

        services.AddHttpClient<WaPilotApiClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<WhatsAppOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? new WhatsAppOptions().BaseUrl : options.BaseUrl.Trim();
            http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        });

        services.AddTransient<IWhatsAppService, WaPilotWhatsAppService>();
        services.AddTransient<IWhatsAppInstanceService, WaPilotInstanceService>();
        services.AddTransient<IWhatsAppCampaignService, WaPilotCampaignService>();

        return services;
    }
}
