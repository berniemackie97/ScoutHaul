using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

/// <summary>
/// Free, open-source barcode lookup via Open Food Facts.
/// Strong coverage for grocery/food/beverage/household items.
/// No API key required. Rate limit: ~100 req/min (be polite).
/// https://wiki.openfoodfacts.org/API
/// </summary>
internal sealed class OpenFoodFactsBarcodeLookupProvider(
    HttpClient httpClient,
    ILogger<OpenFoodFactsBarcodeLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "OpenFoodFacts";
    public bool IsConfigured => true; // always available, no key needed

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://world.openfoodfacts.org/api/v2/product/{Uri.EscapeDataString(barcode.Trim())}.json";

            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("OpenFoodFacts returned {StatusCode} for barcode {Barcode}", response.StatusCode, barcode);
                return null;
            }

            OffResponse? result = await response.Content.ReadFromJsonAsync<OffResponse>(cancellationToken: cancellationToken);
            if (result is null || result.Status != 1 || result.Product is null)
            {
                logger.LogDebug("OpenFoodFacts: product not found for barcode {Barcode}", barcode);
                return null;
            }

            OffProduct product = result.Product;
            string? title = product.ProductName ?? product.ProductNameEn;
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return new BarcodeLookupResult(
                Barcode: barcode,
                Title: title,
                Brand: product.Brands,
                Description: product.GenericName ?? product.GenericNameEn,
                Category: product.Categories,
                ImageUrl: product.ImageFrontUrl ?? product.ImageUrl,
                Upc: barcode.Length == 12 ? barcode : null,
                Ean: barcode.Length == 13 ? barcode : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenFoodFacts lookup failed for barcode {Barcode}", barcode);
            return null;
        }
    }

    private sealed class OffResponse
    {
        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("product")]
        public OffProduct? Product { get; set; }
    }

    private sealed class OffProduct
    {
        [JsonPropertyName("product_name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("product_name_en")]
        public string? ProductNameEn { get; set; }

        [JsonPropertyName("brands")]
        public string? Brands { get; set; }

        [JsonPropertyName("generic_name")]
        public string? GenericName { get; set; }

        [JsonPropertyName("generic_name_en")]
        public string? GenericNameEn { get; set; }

        [JsonPropertyName("categories")]
        public string? Categories { get; set; }

        [JsonPropertyName("image_front_url")]
        public string? ImageFrontUrl { get; set; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }
    }
}
