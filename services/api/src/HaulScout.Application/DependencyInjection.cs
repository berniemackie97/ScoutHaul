using Microsoft.Extensions.DependencyInjection;
using HaulScout.Application.Common;
using HaulScout.Application.Organizations;
using HaulScout.Application.Services;

namespace HaulScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Per-request tenant holder used by the data layer's org-scoping query filters.
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddScoped<IDealAnalyzer, DealAnalyzer>();
        services.AddScoped<IConfidenceScorer, ConfidenceScorer>();
        services.AddScoped<OrganizationRoleWorkflowService>();
        services.AddScoped<OrganizationTeamWorkflowService>();
        services.AddSingleton<IMarketplaceSearchLinkGenerator, MarketplaceSearchLinkGenerator>();

        return services;
    }
}
