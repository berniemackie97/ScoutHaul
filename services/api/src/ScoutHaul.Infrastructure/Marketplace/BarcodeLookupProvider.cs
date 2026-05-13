using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class BarcodeLookupProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<BarcodeLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "Barcode Lookup";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["BarcodeLookup:ApiKey"]);

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        string? apiKey = configuration["BarcodeLookup:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        string url = $"https://api.barcodelookup.com/v3/products?barcode={Uri.EscapeDataString(barcode)}&formatted=y&key={Uri.EscapeDataString(apiKey)}";
        using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Barcode Lookup returned {StatusCode} for barcode {Barcode}", response.StatusCode, barcode);
            return null;
        }

        BarcodeLookupResponse? result = await response.Content.ReadFromJsonAsync<BarcodeLookupResponse>(cancellationToken: cancellationToken);
        BarcodeLookupProduct? product = result?.Products?.FirstOrDefault();
        if (product is null || string.IsNullOrWhiteSpace(product.Title))
        {
            return null;
        }

        return new BarcodeLookupResult(
            Barcode: barcode,
            Title: product.Title,
            Brand: product.Brand,
            Description: product.Description,
            Category: product.Category,
            ImageUrl: product.Images?.FirstOrDefault(),
            Upc: product.BarcodeNumber?.Length == 12 ? product.BarcodeNumber : null,
            Ean: product.BarcodeNumber?.Length == 13 ? product.BarcodeNumber : null);
    }

    private sealed class BarcodeLookupResponse
    {
        [JsonPropertyName("products")]
        public List<BarcodeLookupProduct>? Products { get; set; }
    }

    private sealed class BarcodeLookupProduct
    {
        [JsonPropertyName("barcode_number")]
        public string? BarcodeNumber { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }
    }
}
