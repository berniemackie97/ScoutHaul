using Microsoft.Extensions.DependencyInjection;
using HaulScout.Application.Common;
using HaulScout.Application.Inventory;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.ReferenceData;
using HaulScout.Application.Sales;
using HaulScout.Application.Sessions;
using HaulScout.Application.Services;
using HaulScout.Application.Storefront;

namespace HaulScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Per-request tenant holder used by the data layer's org-scoping query filters.
        services.AddScoped<ITenantContext, TenantContext>();

        services.AddScoped<IDealAnalyzer, DealAnalyzer>();
        services.AddScoped<IConfidenceScorer, ConfidenceScorer>();
        services.AddScoped<InventoryReferenceDataService>();
        services.AddScoped<InventoryQueryService>();
        services.AddScoped<StorefrontShippingCalculator>();
        services.AddScoped<CartWorkflowService>();
        services.AddScoped<CheckoutWorkflowService>();
        services.AddScoped<StorefrontOrderService>();
        services.AddScoped<StorefrontOfferWorkflowService>();
        services.AddScoped<StorefrontCatalogService>();
        services.AddScoped<StorePublishingWorkflowService>();
        services.AddScoped<InventoryBundleWorkflowService>();
        services.AddScoped<InventoryDeletionWorkflowService>();
        services.AddScoped<InventoryUpsertWorkflowService>();
        services.AddScoped<InventoryPhotoWorkflowService>();
        services.AddScoped<InventoryStatusWorkflowService>();
        services.AddScoped<InventoryIssueWorkflowService>();
        services.AddScoped<OrganizationInviteCodeWorkflowService>();
        services.AddScoped<OrganizationMemberWorkflowService>();
        services.AddScoped<OrganizationWorkflowService>();
        services.AddScoped<OrganizationRoleWorkflowService>();
        services.AddScoped<OrganizationTeamWorkflowService>();
        services.AddScoped<PlatformOrderSuggestionService>();
        services.AddScoped<MarketplaceRefundImportService>();
        services.AddScoped<PlatformOrderFeeEnrichmentService>();
        services.AddScoped<PlatformOrderImportWorkflowService>();
        services.AddScoped<PlatformOrderIngestionService>();
        services.AddScoped<PlatformOrderWorkflowService>();
        services.AddScoped<PlatformOrderAutoMatchService>();
        services.AddScoped<ReferenceOverrideWorkflowService>();
        services.AddScoped<SalesChannelWorkflowService>();
        services.AddScoped<SessionBootstrapService>();
        services.AddScoped<StoreSettingsWorkflowService>();
        services.AddSingleton<IMarketplaceSearchLinkGenerator, MarketplaceSearchLinkGenerator>();

        return services;
    }
}
