using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class PriceChartingMarketplaceProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PriceChartingMarketplaceProvider> logger) : IMarketplacePricingProvider
{
    public string Name => "PriceCharting";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["PriceCharting:Token"]);

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        bool lookupAllBarcodes = configuration.GetValue("PriceCharting:LookupAllBarcodes", false);
        if (!lookupAllBarcodes && !MarketplaceCategoryClassifier.IsCollectiblePricingCandidate(request.Product, request.Query))
        {
            return Task.FromResult<IReadOnlyList<MarketplaceListingDto>>([]);
        }

        return SearchAsync(request.Barcode is { Length: > 0 } barcode ? $"upc={Uri.EscapeDataString(barcode)}" : $"q={Uri.EscapeDataString(request.Query)}", cancellationToken);
    }

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        return MarketplaceCategoryClassifier.IsCollectiblePricingCandidate(request.Product, request.Query)
            ? SearchAsync($"q={Uri.EscapeDataString(request.Query)}", cancellationToken)
            : Task.FromResult<IReadOnlyList<MarketplaceListingDto>>([]);
    }

    private async Task<IReadOnlyList<MarketplaceListingDto>> SearchAsync(string queryParameter, CancellationToken cancellationToken)
    {
        string? token = configuration["PriceCharting:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        string url = $"https://www.pricecharting.com/api/product?t={Uri.EscapeDataString(token)}&{queryParameter}";
        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("PriceCharting returned {StatusCode} for {Query}", response.StatusCode, queryParameter);
            return [];
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        if (!TryGetString(root, "status", out string? status) || !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        string? productName = TryGetString(root, "product-name", out string? name) ? name : null;
        if (string.IsNullOrWhiteSpace(productName))
        {
            return [];
        }

        string? consoleName = TryGetString(root, "console-name", out string? console) ? console : null;
        string title = string.IsNullOrWhiteSpace(consoleName)
            ? productName
            : $"{productName} ({consoleName})";

        List<MarketplaceListingDto> listings = [];
        AddPrice(listings, root, title, "loose-price", "Loose");
        AddPrice(listings, root, title, "cib-price", "Complete in box");
        AddPrice(listings, root, title, "new-price", "New sealed");
        AddPrice(listings, root, title, "graded-price", "Graded");
        AddPrice(listings, root, title, "manual-only-price", "Manual only");
        AddPrice(listings, root, title, "box-only-price", "Box only");

        return listings;
    }

    private static void AddPrice(List<MarketplaceListingDto> listings, JsonElement root, string title, string key, string condition)
    {
        if (!root.TryGetProperty(key, out JsonElement element) || !element.TryGetInt32(out int cents) || cents <= 0)
        {
            return;
        }

        listings.Add(new MarketplaceListingDto(
            Title: title,
            Price: decimal.Round(cents / 100m, 2, MidpointRounding.AwayFromZero),
            ShippingCost: null,
            ImageUrl: null,
            ItemUrl: null,
            Condition: condition,
            Source: "PriceCharting",
            IsSold: false,
            SoldDate: null));
    }

    private static bool TryGetString(JsonElement root, string key, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(key, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }
}
