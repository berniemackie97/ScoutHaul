using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class KeepaMarketplaceProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<KeepaMarketplaceProvider> logger) : IMarketplacePricingProvider
{
    private const int AmazonNew = 1;
    private const int AmazonUsed = 2;
    private const int AmazonFbaNew = 10;
    private const int AmazonBuyBox = 18;

    public string Name => "Keepa";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Keepa:ApiKey"]);

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        string? code = NormalizeCode(request.Barcode);
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult<IReadOnlyList<MarketplaceListingDto>>([]);
        }

        return SearchProductAsync($"code={Uri.EscapeDataString(code)}", cancellationToken);
    }

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        string query = request.Query.Trim();
        return IsAsin(query)
            ? SearchProductAsync($"asin={Uri.EscapeDataString(query)}", cancellationToken)
            : Task.FromResult<IReadOnlyList<MarketplaceListingDto>>([]);
    }

    private async Task<IReadOnlyList<MarketplaceListingDto>> SearchProductAsync(string productParameter, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["Keepa:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return [];
        }

        int domainId = configuration.GetValue("Keepa:DomainId", 1);
        int statsDays = Math.Clamp(configuration.GetValue("Keepa:StatsDays", 90), 1, 365);
        string url = $"https://api.keepa.com/product?key={Uri.EscapeDataString(apiKey)}&domain={domainId}&{productParameter}&stats={statsDays}&history=0";

        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Keepa returned {StatusCode} for {ProductParameter}", response.StatusCode, productParameter);
            return [];
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("products", out JsonElement products)
            || products.ValueKind != JsonValueKind.Array
            || products.GetArrayLength() == 0)
        {
            return [];
        }

        JsonElement product = products[0];
        string? asin = TryGetString(product, "asin");
        string title = TryGetString(product, "title") ?? asin ?? "Amazon product";
        string? itemUrl = string.IsNullOrWhiteSpace(asin) ? null : $"https://www.amazon.com/dp/{asin}";
        string? imageUrl = BuildImageUrl(TryGetString(product, "imagesCSV"));

        if (!product.TryGetProperty("stats", out JsonElement stats))
        {
            return [];
        }

        List<MarketplaceListingDto> listings = [];
        AddStatsPrice(listings, stats, "current", AmazonBuyBox, title, "Amazon buy box current", itemUrl, imageUrl);
        AddStatsPrice(listings, stats, "current", AmazonNew, title, "Amazon new current", itemUrl, imageUrl);
        AddStatsPrice(listings, stats, "current", AmazonFbaNew, title, "Amazon FBA new current", itemUrl, imageUrl);
        AddStatsPrice(listings, stats, "current", AmazonUsed, title, "Amazon used current", itemUrl, imageUrl);
        AddStatsPrice(listings, stats, "avg90", AmazonNew, title, "Amazon new 90 day average", itemUrl, imageUrl);
        AddStatsPrice(listings, stats, "avg90", AmazonUsed, title, "Amazon used 90 day average", itemUrl, imageUrl);

        return listings;
    }

    private static void AddStatsPrice(
        List<MarketplaceListingDto> listings,
        JsonElement stats,
        string statsKey,
        int index,
        string title,
        string condition,
        string? itemUrl,
        string? imageUrl)
    {
        if (!stats.TryGetProperty(statsKey, out JsonElement values)
            || values.ValueKind != JsonValueKind.Array
            || values.GetArrayLength() <= index
            || !values[index].TryGetInt32(out int cents)
            || cents <= 0)
        {
            return;
        }

        listings.Add(new MarketplaceListingDto(
            Title: title,
            Price: decimal.Round(cents / 100m, 2, MidpointRounding.AwayFromZero),
            ShippingCost: null,
            ImageUrl: imageUrl,
            ItemUrl: itemUrl,
            Condition: condition,
            Source: "Keepa",
            IsSold: false,
            SoldDate: null));
    }

    private static string? BuildImageUrl(string? imagesCsv)
    {
        string? imageName = imagesCsv?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(imageName)
            ? null
            : $"https://images-na.ssl-images-amazon.com/images/I/{imageName}";
    }

    private static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string trimmed = code.Trim();
        return IsAsin(trimmed)
            ? trimmed
            : new string(trimmed.Where(char.IsDigit).ToArray());
    }

    private static bool IsAsin(string value) =>
        value.Length == 10
        && value.All(char.IsLetterOrDigit)
        && value.Any(char.IsLetter);

    private static string? TryGetString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}
