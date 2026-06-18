using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;

namespace HaulScout.Infrastructure.Marketplace;

/// <summary>
/// Barcode lookup via Go-UPC (https://go-upc.com).
/// Over 1 billion products across all categories. Excellent data quality.
/// Free trial available; paid plans start at $75/month for 5,000 lookups.
/// Requires config: GoUpc:ApiKey
/// </summary>
internal sealed class GoUpcBarcodeLookupProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GoUpcBarcodeLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "Go-UPC";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GoUpc:ApiKey"]);

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["GoUpc:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            string url = $"https://go-upc.com/api/v1/code/{Uri.EscapeDataString(barcode.Trim())}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Go-UPC returned {StatusCode} for barcode {Barcode}", response.StatusCode, barcode);
                return null;
            }

            GoUpcResponse? result = await response.Content.ReadFromJsonAsync<GoUpcResponse>(cancellationToken: cancellationToken);
            GoUpcProduct? product = result?.Product;

            if (product is null || string.IsNullOrWhiteSpace(product.Name))
            {
                logger.LogDebug("Go-UPC: no product data for barcode {Barcode}", barcode);
                return null;
            }

            return new BarcodeLookupResult(
                Barcode: barcode,
                Title: product.Name,
                Brand: product.Brand,
                Description: product.Description,
                Category: product.Category,
                ImageUrl: product.ImageUrl,
                Upc: result?.CodeType == "UPC" ? result.Code : (barcode.Length == 12 ? barcode : null),
                Ean: result?.CodeType == "EAN" ? result.Code : (barcode.Length == 13 ? barcode : null));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Go-UPC lookup failed for barcode {Barcode}", barcode);
            return null;
        }
    }

    private sealed class GoUpcResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("codeType")]
        public string? CodeType { get; set; }

        [JsonPropertyName("product")]
        public GoUpcProduct? Product { get; set; }

        [JsonPropertyName("barcodeUrl")]
        public string? BarcodeUrl { get; set; }
    }

    private sealed class GoUpcProduct
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("imageUrl")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("upc")]
        public string? Upc { get; set; }

        [JsonPropertyName("ean")]
        public string? Ean { get; set; }
    }
}
