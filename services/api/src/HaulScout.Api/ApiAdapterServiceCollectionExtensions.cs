using HaulScout.Api.Services;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Application.Storefront;
using HaulScout.Infrastructure.Storefront;

namespace HaulScout.Api;

/// <summary>
/// API-adapter composition root. These services are still hosted by the web project because they touch HTTP,
/// local files, or controller-era workflows that have not yet moved to Application ports.
/// </summary>
public static class ApiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddApiAdapters(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<StorefrontStoreResolver>();
        services.Configure<StorefrontCheckoutOptions>(configuration.GetSection(StorefrontCheckoutOptions.SectionName));
        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.AddScoped<SourcingTripQueryService>();
        services.AddScoped<SourcingTripWorkflowService>();
        services.AddScoped<SourcingTripStopWorkflowService>();
        services.AddScoped<MarketplaceLookupPersistenceService>();
        services.AddScoped<IInventoryPhotoProcessor, InventoryPhotoStorageService>();
        services.AddScoped<OrdersWorkflowService>();
        services.AddScoped<SaleRecordingWorkflowService>();
        services.AddScoped<SaleFinancialStateService>();
        services.AddScoped<SaleReturnWorkflowService>();
        services.AddScoped<SaleQueryService>();
        services.AddScoped<SaleGroupingWorkflowService>();
        services.AddScoped<SaleDeletionWorkflowService>();
        services.AddScoped<SaleTransitionWorkflowService>();
        services.AddScoped<SaleCorrectionWorkflowService>();
        services.AddScoped<SaleRefundWorkflowService>();
        services.AddScoped<ICurrentUserContext, JwtCurrentUserContext>();
        return services;
    }
}
