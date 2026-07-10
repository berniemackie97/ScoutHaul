using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Marketplace;

internal sealed class EbayMarketplaceLookupService : IMarketplacePricingProvider
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

    public string Name => "eBay";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Ebay:ClientId"])
        && !string.IsNullOrWhiteSpace(_configuration["Ebay:ClientSecret"]);

    public async Task<IReadOnlyList<MarketplaceListingDto>> SearchByBarcodeAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        string token = await GetAccessTokenAsync(cancellationToken);
        string cleanBarcode = request.Barcode?.Trim() ?? request.Query.Trim();

        // Search by GTIN (UPC/EAN) only — no keyword fallback for barcode lookups.
        // Keyword fallback with raw barcode digits returns unrelated products.
        List<MarketplaceListingDto> listings = await SearchBrowseApiAsync(
            token, $"gtin={cleanBarcode}", cancellationToken);

        // If GTIN returned nothing and we have a resolved product name, try keyword search
        // with the product name (not the raw barcode digits).
        if (listings.Count == 0 && request.Product is not null && !string.IsNullOrWhiteSpace(request.Product.Title))
        {
            string productQuery = string.Join(' ', request.Product.Brand, request.Product.Title).Trim();
            _logger.LogDebug("GTIN lookup empty for {Barcode}, falling back to product keyword: {Query}", cleanBarcode, productQuery);
            listings = await SearchBrowseApiAsync(
                token, $"q={Uri.EscapeDataString(productQuery)}", cancellationToken);
        }

        return listings;
    }

    public async Task<IReadOnlyList<MarketplaceListingDto>> SearchByKeywordAsync(
        MarketplacePricingRequest request,
        CancellationToken cancellationToken)
    {
        string token = await GetAccessTokenAsync(cancellationToken);
        return await SearchBrowseApiAsync(token, $"q={Uri.EscapeDataString(request.Query.Trim())}", cancellationToken);
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

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("eBay Browse API returned {StatusCode}", response.StatusCode);
                return [];
            }

            EbaySearchResponse? result = await response.Content.ReadFromJsonAsync<EbaySearchResponse>(JsonOptions, cancellationToken);
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
                SoldDate: null,
                MarketTier: MarketTiers.ActiveSecondhand // Browse API returns active listings
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

            using HttpRequestMessage request = new(HttpMethod.Post, tokenUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "https://api.ebay.com/oauth/api_scope",
            });

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            EbayTokenResponse tokenResponse = await response.Content.ReadFromJsonAsync<EbayTokenResponse>(JsonOptions, cancellationToken)
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
