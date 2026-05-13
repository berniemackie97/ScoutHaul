using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Marketplace;

internal sealed class EanDbBarcodeLookupProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<EanDbBarcodeLookupProvider> logger) : IBarcodeLookupProvider
{
    public string Name => "EAN-DB";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["EanDb:Jwt"]);

    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        string? jwt = configuration["EanDb:Jwt"];
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://ean-db.com/api/v2/product/{Uri.EscapeDataString(barcode)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("EAN-DB returned {StatusCode} for barcode {Barcode}", response.StatusCode, barcode);
            return null;
        }

        EanDbResponse? result = await response.Content.ReadFromJsonAsync<EanDbResponse>(cancellationToken: cancellationToken);
        EanProduct? product = result?.Product;
        if (product is null)
        {
            return null;
        }

        string? title = PreferredTitle(product.Titles);
        string? brand = PreferredTitle(product.Manufacturer?.Titles);
        string? category = PreferredTitle(product.Categories?.FirstOrDefault()?.Titles);
        string? imageUrl = product.Images?.FirstOrDefault()?.Url;

        return string.IsNullOrWhiteSpace(title)
            ? null
            : new BarcodeLookupResult(
                Barcode: barcode,
                Title: title,
                Brand: brand,
                Description: null,
                Category: category,
                ImageUrl: imageUrl,
                Upc: product.Barcode?.Length == 12 ? product.Barcode : null,
                Ean: product.Barcode?.Length == 13 ? product.Barcode : null);
    }

    private static string? PreferredTitle(IReadOnlyDictionary<string, string>? titles)
    {
        if (titles is null || titles.Count == 0)
        {
            return null;
        }

        return titles.TryGetValue("en", out string? english) && !string.IsNullOrWhiteSpace(english)
            ? english
            : titles.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private sealed class EanDbResponse
    {
        [JsonPropertyName("product")]
        public EanProduct? Product { get; set; }
    }

    private sealed class EanProduct
    {
        [JsonPropertyName("barcode")]
        public string? Barcode { get; set; }

        [JsonPropertyName("titles")]
        public Dictionary<string, string>? Titles { get; set; }

        [JsonPropertyName("categories")]
        public List<EanCategory>? Categories { get; set; }

        [JsonPropertyName("manufacturer")]
        public EanManufacturer? Manufacturer { get; set; }

        [JsonPropertyName("images")]
        public List<EanImage>? Images { get; set; }
    }

    private sealed class EanCategory
    {
        [JsonPropertyName("titles")]
        public Dictionary<string, string>? Titles { get; set; }
    }

    private sealed class EanManufacturer
    {
        [JsonPropertyName("titles")]
        public Dictionary<string, string>? Titles { get; set; }
    }

    private sealed class EanImage
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }
}
