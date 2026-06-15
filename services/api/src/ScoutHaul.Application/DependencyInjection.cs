using Microsoft.Extensions.DependencyInjection;
using ScoutHaul.Application.Common;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Per-request tenant holder used by the data layer's org-scoping query filters.
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddScoped<IDealAnalyzer, DealAnalyzer>();
        services.AddScoped<IConfidenceScorer, ConfidenceScorer>();
        services.AddSingleton<IMarketplaceSearchLinkGenerator, MarketplaceSearchLinkGenerator>();

        return services;
    }
}
