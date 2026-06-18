using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HaulScout.Application.Services;
using HaulScout.Infrastructure.Marketplace;
using HaulScout.Infrastructure.Persistence;
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
