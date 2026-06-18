using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;

namespace HaulScout.Infrastructure.Marketplace;

internal sealed class SerpApiGoogleShoppingProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<SerpApiGoogleShoppingProvider> logger) : IMarketplacePricingProvider
{
    public string Name => "SerpApi Google Shopping";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["SerpApi:ApiKey"]);

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        return SearchAsync(request.Query, cancellationToken);
    }

    public Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        return SearchAsync(request.Query, cancellationToken);
    }

    private async Task<IReadOnlyList<MarketplaceListingDto>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["SerpApi:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string gl = configuration["SerpApi:Gl"] ?? "us";
        string hl = configuration["SerpApi:Hl"] ?? "en";
        int limit = Math.Clamp(configuration.GetValue("SerpApi:Limit", 20), 1, 40);
        string url = "https://serpapi.com/search.json"
            + "?engine=google_shopping"
            + $"&q={Uri.EscapeDataString(query.Trim())}"
            + $"&api_key={Uri.EscapeDataString(apiKey)}"
            + $"&gl={Uri.EscapeDataString(gl)}"
            + $"&hl={Uri.EscapeDataString(hl)}"
            + "&google_domain=google.com";

        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("SerpApi Google Shopping returned {StatusCode} for {Query}", response.StatusCode, query);
            return [];
        }

        using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("shopping_results", out JsonElement results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<MarketplaceListingDto> listings = [];
        foreach (JsonElement item in results.EnumerateArray().Take(limit))
        {
            decimal price = TryGetDecimal(item, "extracted_price") ?? ParseMoney(TryGetString(item, "price")) ?? 0;
            if (price <= 0)
            {
                continue;
            }

            string title = TryGetString(item, "title") ?? "Google Shopping result";
            string source = TryGetString(item, "source") ?? "Google Shopping";
            string? condition = TryGetString(item, "second_hand_condition");
            decimal? shipping = ParseShipping(TryGetString(item, "delivery"));

            listings.Add(new MarketplaceListingDto(
                Title: TrimTo(title, 300),
                Price: price,
                ShippingCost: shipping,
                ImageUrl: TryGetString(item, "thumbnail") ?? TryGetString(item, "serpapi_thumbnail"),
                ItemUrl: TryGetString(item, "link") ?? TryGetString(item, "product_link"),
                Condition: string.IsNullOrWhiteSpace(condition) ? "Retail listing" : TrimTo(condition, 120),
                Source: string.Equals(source, "Google Shopping", StringComparison.OrdinalIgnoreCase)
                    ? "Google Shopping"
                    : $"Google Shopping: {TrimTo(source, 62)}",
                IsSold: false,
                SoldDate: null,
                // A second-hand condition means it's a used listing; otherwise treat as retail.
                MarketTier: string.IsNullOrWhiteSpace(condition) ? MarketTiers.Retail : MarketTiers.ActiveSecondhand));
        }

        return listings;
    }

    private static decimal? ParseShipping(string? delivery)
    {
        if (string.IsNullOrWhiteSpace(delivery))
        {
            return null;
        }

        if (delivery.Contains("free", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return ParseMoney(delivery);
    }

    private static decimal? TryGetDecimal(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out JsonElement element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out decimal value) => value,
            JsonValueKind.String => ParseMoney(element.GetString()),
            _ => null
        };
    }

    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string numeric = new(value.Where(character => char.IsDigit(character) || character is '.' or ',').ToArray());
        numeric = numeric.Replace(",", "", StringComparison.Ordinal);
        return decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : null;
    }

    private static string? TryGetString(JsonElement root, string key)
    {
        return root.TryGetProperty(key, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static string TrimTo(string value, int maxLength)
    {
        value = value.Trim();
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
