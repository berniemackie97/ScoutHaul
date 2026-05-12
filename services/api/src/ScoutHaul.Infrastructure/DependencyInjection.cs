using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScoutHaul.Application.Services;
using ScoutHaul.Infrastructure.Marketplace;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        }

        services.AddDbContext<ScoutHaulDbContext>(options => options.UseNpgsql(connectionString));

        // eBay marketplace lookup (only registered when credentials are configured)
        if (!string.IsNullOrWhiteSpace(configuration["Ebay:ClientId"]))
        {
            services.AddHttpClient<IMarketplaceLookupService, EbayMarketplaceLookupService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });
        }
        else
        {
            services.AddSingleton<IMarketplaceLookupService, NoopMarketplaceLookupService>();
        }

        return services;
    }
}
