using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
using HaulScout.Infrastructure.Channels;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Marketplace;
using HaulScout.Infrastructure.Organizations;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.ReferenceData;
using HaulScout.Infrastructure.Sales;
using HaulScout.Infrastructure.Sessions;
using HaulScout.Infrastructure.Storefront;
using HaulScout.Infrastructure.Vision;

namespace HaulScout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        services.AddDbContext<HaulScoutDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IChannelAccountService, EfChannelAccountService>();
        services.AddScoped<IInventoryReferenceDataStore, EfInventoryReferenceDataStore>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IInventoryStore, EfInventoryStore>();
        services.AddScoped<IStorefrontCartStore, EfStorefrontCartStore>();
        services.AddScoped<IStorefrontCheckoutStore, EfStorefrontCheckoutStore>();
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<IStorefrontCatalogStore, EfStorefrontCatalogStore>();
        services.AddScoped<IStoreListingStore, EfStoreListingStore>();
        services.AddScoped<IStoreDirectory, EfStoreDirectory>();
        services.AddScoped<IInventoryIssueStore, EfInventoryIssueStore>();
        services.AddScoped<IInventoryPhotoStore, EfInventoryPhotoStore>();
        services.AddScoped<IOrganizationAuthorizer, EfOrganizationAuthorizer>();
        services.AddScoped<IOrganizationInviteCodeStore, EfOrganizationInviteCodeStore>();
        services.AddScoped<IOrganizationMemberStore, EfOrganizationMemberStore>();
        services.AddScoped<IOrganizationStore, EfOrganizationStore>();
        services.AddScoped<IOrganizationRoleStore, EfOrganizationRoleStore>();
        services.AddScoped<IOrganizationTeamStore, EfOrganizationTeamStore>();
        services.AddScoped<IPlatformOrderSuggestionStore, EfPlatformOrderSuggestionStore>();
        services.AddScoped<IReferenceOverrideStore, EfReferenceOverrideStore>();
        services.AddScoped<ISalesChannelStore, EfSalesChannelStore>();
        services.AddScoped<ISessionBootstrapStore, EfSessionBootstrapStore>();
        services.AddScoped<IStoreSettingsStore, EfStoreSettingsStore>();
        services.AddHttpClient<IOrganizationInviteSender, SupabaseOrganizationInviteSender>();

        // In-memory cache (resale estimates, etc.) — protects the shared Gemini free-tier quota.
        services.AddMemoryCache();

        // Barcode lookup — tried in order. Internal DB first (free, instant, learns over time),
        // then external APIs as fallback. Results get saved back to ProductIdentities for next time.
        services.AddScoped<IBarcodeLookupService, CompositeBarcodeLookupService>();
        services.AddScoped<IBarcodeLookupProvider, InternalBarcodeLookupProvider>();
        services.AddHttpClient<IBarcodeLookupProvider, GoUpcBarcodeLookupProvider>(ConfigureJsonClient);
        services.AddHttpClient<IBarcodeLookupProvider, OpenFoodFactsBarcodeLookupProvider>(ConfigureJsonClient);
        services.AddHttpClient<IBarcodeLookupProvider, UpcItemDbLookupService>(ConfigureJsonClient);

        // Vision — Gemini multimodal is the SOLE product identifier + appraiser (reads the photo,
        // returns specific name/brand/model + a conservative used-resale valuation). Needs a longer
        // timeout than plain JSON APIs.
        services.AddHttpClient<IVisionIdentificationService, GeminiProductVisionService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Text-only resale estimator (Gemini) — gives barcode/keyword lookups a value estimate
        // when no live marketplace comps exist. Reuses the Gemini key.
        services.AddHttpClient<IResaleEstimator, GeminiResaleEstimator>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Listing generator (Gemini) — writes title + description and a comp-backed suggested list
        // price from an item's details. Reuses the resale estimator for grounded comps.
        services.AddHttpClient<IListingGenerationService, GeminiListingWriter>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Marketplace pricing — tried in order. PriceCharting for collectibles (free token),
        // Keepa for Amazon price history, SerpApi for Google Shopping. All need API keys.
        services.AddScoped<IMarketplaceLookupService, CompositeMarketplaceLookupService>();
        services.AddHttpClient<IMarketplacePricingProvider, PriceChartingMarketplaceProvider>(ConfigureJsonClient);
        services.AddHttpClient<IMarketplacePricingProvider, KeepaMarketplaceProvider>(ConfigureJsonClient);
        services.AddHttpClient<IMarketplacePricingProvider, SerpApiGoogleShoppingProvider>(ConfigureJsonClient);

        return services;
    }

    private static void ConfigureJsonClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "HaulScout/1.0");
    }
}
