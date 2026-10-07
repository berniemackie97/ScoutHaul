using Amazon.Runtime;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HaulScout.Application.Channels;
using HaulScout.Application.Common;
using HaulScout.Application.Inventory;
using HaulScout.Application.Search;
using HaulScout.Application.Sync;
using HaulScout.Application.Sync.Agents;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.ReferenceData;
using HaulScout.Application.Sales;
using HaulScout.Application.Sessions;
using HaulScout.Application.Services;
using HaulScout.Application.Storefront;
using HaulScout.Infrastructure.Channels;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Search;
using HaulScout.Infrastructure.Sync;
using HaulScout.Infrastructure.Marketplace;
using HaulScout.Infrastructure.Organizations;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Common;
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
        // The transaction seam. Scoped, so it shares the request's DbContext and its ambient transaction.
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IInventoryStore, EfInventoryStore>();
        services.AddScoped<IInventoryMergeStore, EfInventoryMergeStore>();
        services.AddScoped<ISearchStore, EfSearchStore>();
        services.AddScoped<ISyncRunStore, EfSyncRunStore>();
        services.AddScoped<ISyncAgentStore, EfSyncAgentStore>();
        services.AddScoped<IStorefrontCartStore, EfStorefrontCartStore>();
        services.AddScoped<IStorefrontCheckoutStore, EfStorefrontCheckoutStore>();
        services.AddScoped<IStorefrontCustomerStore, EfStorefrontCustomerStore>();
        services.AddScoped<IStorefrontOfferStore, EfStorefrontOfferStore>();
        services.AddScoped<IPaymentGateway, StripePaymentGateway>();
        services.AddScoped<IStorefrontCatalogStore, EfStorefrontCatalogStore>();
        services.AddScoped<IStoreListingStore, EfStoreListingStore>();
        services.AddScoped<IStoreDirectory, EfStoreDirectory>();
        services.AddScoped<IInventoryIssueStore, EfInventoryIssueStore>();
        services.AddScoped<IInventoryPhotoStore, EfInventoryPhotoStore>();
        services.AddScoped<IInventoryPhotoSyncStore, EfInventoryPhotoSyncStore>();
        services.AddScoped<IOrganizationAuthorizer, EfOrganizationAuthorizer>();
        services.AddScoped<IOrganizationInviteCodeStore, EfOrganizationInviteCodeStore>();
        services.AddScoped<IOrganizationMemberStore, EfOrganizationMemberStore>();
        services.AddScoped<IOrganizationStore, EfOrganizationStore>();
        services.AddScoped<IOrganizationRoleStore, EfOrganizationRoleStore>();
        services.AddScoped<IOrganizationTeamStore, EfOrganizationTeamStore>();
        services.AddScoped<OrderAssembler>();
        services.AddScoped<EfPlatformOrderStore>();
        services.AddScoped<IPlatformOrderStore>(services => services.GetRequiredService<EfPlatformOrderStore>());
        services.AddScoped<IPlatformOrderImportStore>(services => services.GetRequiredService<EfPlatformOrderStore>());
        services.AddScoped<IPlatformOrderFeeEnrichmentStore>(services => services.GetRequiredService<EfPlatformOrderStore>());
        services.AddScoped<IMarketplaceRefundImportStore>(services => services.GetRequiredService<EfPlatformOrderStore>());
        services.AddScoped<IChannelDelistStore, EfChannelDelistStore>();
        services.AddScoped<ISoldListingStore, EfSoldListingStore>();
        services.AddScoped<IInventoryPhotoBackfillStore, EfInventoryPhotoBackfillStore>();
        services.AddScoped<IPlatformOrderSuggestionStore, EfPlatformOrderSuggestionStore>();
        services.AddScoped<IReferenceOverrideStore, EfReferenceOverrideStore>();
        services.AddScoped<ISalesChannelStore, EfSalesChannelStore>();
        services.AddScoped<ISessionBootstrapStore, EfSessionBootstrapStore>();
        services.AddScoped<IStoreSettingsStore, EfStoreSettingsStore>();
        services.AddHttpClient<IOrganizationInviteSender, SupabaseOrganizationInviteSender>();

        // Where photo images live. Configuring a bucket moves them out of the database, which is what a deployment
        // syncing whole wardrobes needs: image bytes are the only rows here that grow without limit, and in Postgres
        // every one of them is carried by every backup, restore and write-ahead log record.
        services.Configure<PhotoObjectStorageOptions>(configuration.GetSection(PhotoObjectStorageOptions.Section));
        PhotoObjectStorageOptions photoStorage = new();
        configuration.GetSection(PhotoObjectStorageOptions.Section).Bind(photoStorage);
        if (photoStorage.IsConfigured)
        {
            if (photoStorage.DescribeMisconfiguration() is string problem)
            {
                throw new InvalidOperationException($"Photo object storage is misconfigured. {problem}");
            }

            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
                new BasicAWSCredentials(photoStorage.AccessKeyId, photoStorage.SecretAccessKey),
                new AmazonS3Config
                {
                    ServiceURL = photoStorage.ServiceUrl,
                    AuthenticationRegion = photoStorage.Region,
                    ForcePathStyle = true,
                }));
            services.AddSingleton<IPhotoObjectClient, S3PhotoObjectClient>();
            services.AddScoped<IInventoryPhotoBlobStore, ObjectStoreInventoryPhotoBlobStore>();
        }
        else
        {
            services.AddScoped<IInventoryPhotoBlobStore, DatabaseInventoryPhotoBlobStore>();
        }

        // Photo import by URL — fetches an image from an operator-pasted link (their live listing).
        // Short timeout + no redirect-follow-limit surprises: a paste should fail fast, not hang.
        services.AddHttpClient<IRemoteImageFetcher, HttpRemoteImageFetcher>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

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
