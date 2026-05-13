using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class UpcDevLookupProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<UpcDevLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "upc.dev";
    public bool IsConfigured => configuration.GetValue("UpcDev:Enabled", false) || !string.IsNullOrWhiteSpace(configuration["UpcDev:ApiKey"]);

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://upc.dev/v1/product/{Uri.EscapeDataString(barcode)}");
        string? apiKey = configuration["UpcDev:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("upc.dev returned {StatusCode} for barcode {Barcode}", response.StatusCode, barcode);
            return null;
        }

        UpcDevProduct? product = await response.Content.ReadFromJsonAsync<UpcDevProduct>(cancellationToken: cancellationToken);
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
            ImageUrl: product.ImageUrl ?? product.Images?.FirstOrDefault(),
            Upc: product.Upc ?? (barcode.Length == 12 ? barcode : null),
            Ean: product.Ean ?? (barcode.Length == 13 ? barcode : null));
    }

    private sealed class UpcDevProduct
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name
        {
            set => Title ??= value;
        }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("image")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }

        [JsonPropertyName("upc")]
        public string? Upc { get; set; }

        [JsonPropertyName("ean")]
        public string? Ean { get; set; }
    }
}
