using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Marketplace;

internal sealed class UpcItemDbLookupService : IBarcodeLookupProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpcItemDbLookupService> _logger;

    public UpcItemDbLookupService(HttpClient httpClient, ILogger<UpcItemDbLookupService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public string Name => "UPCitemdb";
    public bool IsConfigured => true;

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        try
        {
            string cleanBarcode = barcode.Trim();
            string url = $"https://api.upcitemdb.com/prod/trial/lookup?upc={cleanBarcode}";

            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("UPCitemdb returned {StatusCode} for barcode {Barcode}", response.StatusCode, cleanBarcode);
                return null;
            }

            UpcItemDbResponse? result = await response.Content.ReadFromJsonAsync<UpcItemDbResponse>(cancellationToken: cancellationToken);
            UpcItem? item = result?.Items?.FirstOrDefault();

            if (item is null)
            {
                _logger.LogDebug("UPCitemdb returned no items for barcode {Barcode}", cleanBarcode);
                return null;
            }

            return new BarcodeLookupResult(
                Barcode: cleanBarcode,
                Title: item.Title,
                Brand: item.Brand,
                Description: item.Description,
                Category: item.Category,
                ImageUrl: item.Images?.FirstOrDefault(),
                Upc: item.Upc,
                Ean: item.Ean);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UPCitemdb lookup failed for barcode {Barcode}", barcode);
            return null;
        }
    }

    private sealed class UpcItemDbResponse
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("items")]
        public List<UpcItem>? Items { get; set; }
    }

    private sealed class UpcItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("upc")]
        public string? Upc { get; set; }

        [JsonPropertyName("ean")]
        public string? Ean { get; set; }

        [JsonPropertyName("images")]
        public List<string>? Images { get; set; }
    }
}
