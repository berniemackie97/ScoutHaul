using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Marketplace;

public sealed class EbayMarketplaceLookupService : IMarketplaceLookupService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EbayMarketplaceLookupService> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public EbayMarketplaceLookupService(HttpClient httpClient, IConfiguration configuration, ILogger<EbayMarketplaceLookupService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MarketplaceLookupResult> SearchByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        string token = await GetAccessTokenAsync(cancellationToken);
        string cleanBarcode = barcode.Trim();

        // Search active listings by GTIN (UPC/EAN)
        var activeTask = SearchBrowseApiAsync(token, $"gtin={cleanBarcode}", cancellationToken);
        // Also try keyword search as fallback
        var keywordTask = SearchBrowseApiAsync(token, $"q={Uri.EscapeDataString(cleanBarcode)}", cancellationToken);

        await Task.WhenAll(activeTask, keywordTask);

        List<MarketplaceListingDto> allListings = [];
        allListings.AddRange(await activeTask);

        // Add keyword results if GTIN returned nothing
        if (allListings.Count == 0)
        {
            allListings.AddRange(await keywordTask);
        }

        return BuildResult(cleanBarcode, allListings);
    }

    public async Task<MarketplaceLookupResult> SearchByKeywordAsync(string keyword, CancellationToken cancellationToken = default)
    {
        string token = await GetAccessTokenAsync(cancellationToken);
        var listings = await SearchBrowseApiAsync(token, $"q={Uri.EscapeDataString(keyword.Trim())}", cancellationToken);
        return BuildResult(keyword.Trim(), listings);
    }

    private async Task<List<MarketplaceListingDto>> SearchBrowseApiAsync(string token, string queryParam, CancellationToken cancellationToken)
    {
        try
        {
            bool isSandbox = _configuration.GetValue("Ebay:UseSandbox", false);
            string baseUrl = isSandbox
                ? "https://api.sandbox.ebay.com"
                : "https://api.ebay.com";

            string url = $"{baseUrl}/buy/browse/v1/item_summary/search?{queryParam}&limit=12&fieldgroups=MATCHING_ITEMS";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("eBay Browse API returned {StatusCode}", response.StatusCode);
                return [];
            }

            var result = await response.Content.ReadFromJsonAsync<EbaySearchResponse>(JsonOptions, cancellationToken);
            if (result?.ItemSummaries is null)
            {
                return [];
            }

            return result.ItemSummaries.Select(item => new MarketplaceListingDto(
                Title: item.Title ?? "Unknown",
                Price: ParsePrice(item.Price),
                ShippingCost: item.ShippingOptions?.FirstOrDefault()?.ShippingCost is { } sc ? ParsePrice(sc) : null,
                ImageUrl: item.Image?.ImageUrl ?? item.ThumbnailImages?.FirstOrDefault()?.ImageUrl,
                ItemUrl: item.ItemWebUrl,
                Condition: item.Condition ?? "Unknown",
                Source: "eBay",
                IsSold: false,
                SoldDate: null
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "eBay Browse API search failed for {Query}", queryParam);
            return [];
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            {
                return _accessToken;
            }

            string clientId = _configuration["Ebay:ClientId"]
                ?? throw new InvalidOperationException("Ebay:ClientId is not configured.");
            string clientSecret = _configuration["Ebay:ClientSecret"]
                ?? throw new InvalidOperationException("Ebay:ClientSecret is not configured.");

            bool isSandbox = _configuration.GetValue("Ebay:UseSandbox", false);
            string tokenUrl = isSandbox
                ? "https://api.sandbox.ebay.com/identity/v1/oauth2/token"
                : "https://api.ebay.com/identity/v1/oauth2/token";

            string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "https://api.ebay.com/oauth/api_scope",
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<EbayTokenResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("eBay token response was null.");

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60); // 60s buffer

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static decimal ParsePrice(EbayPrice? price)
    {
        if (price?.Value is null) return 0;
        return decimal.TryParse(price.Value, out decimal result) ? result : 0;
    }

    private static MarketplaceLookupResult BuildResult(string query, List<MarketplaceListingDto> listings)
    {
        decimal[] prices = listings.Where(l => l.Price > 0).Select(l => l.Price).ToArray();
        return new MarketplaceLookupResult(
            Query: query,
            Listings: listings,
            AverageSoldPrice: prices.Length > 0 ? Math.Round(prices.Average(), 2) : null,
            LowestPrice: prices.Length > 0 ? prices.Min() : null,
            HighestPrice: prices.Length > 0 ? prices.Max() : null,
            TotalResults: listings.Count);
    }

    // eBay API response models
    private sealed class EbayTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";
    }

    private sealed class EbaySearchResponse
    {
        public List<EbayItemSummary>? ItemSummaries { get; set; }
        public int? Total { get; set; }
    }

    private sealed class EbayItemSummary
    {
        public string? Title { get; set; }
        public EbayPrice? Price { get; set; }
        public string? Condition { get; set; }
        public string? ItemWebUrl { get; set; }
        public EbayImage? Image { get; set; }
        public List<EbayImage>? ThumbnailImages { get; set; }
        public List<EbayShippingOption>? ShippingOptions { get; set; }
    }

    private sealed class EbayPrice
    {
        public string? Value { get; set; }
        public string? Currency { get; set; }
    }

    private sealed class EbayImage
    {
        public string? ImageUrl { get; set; }
    }

    private sealed class EbayShippingOption
    {
        public EbayPrice? ShippingCost { get; set; }
        public string? ShippingCostType { get; set; }
    }
}
