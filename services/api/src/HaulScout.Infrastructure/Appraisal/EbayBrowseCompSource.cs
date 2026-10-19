using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HaulScout.Application.Appraisal;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>
/// Current fixed-price eBay listings through the official Browse API: what sellers ask today. Off
/// until free developer keys are set (Ebay:ClientId, Ebay:ClientSecret).
/// </summary>
internal sealed class EbayBrowseCompSource(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<EbayBrowseCompSource> logger) : ICompSource
{
    private const string TokenCacheKey = "ebay:application-token";

    public string Name => "eBay listings";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Ebay:ClientId"])
        && !string.IsNullOrWhiteSpace(configuration["Ebay:ClientSecret"]);
    public TimeSpan Deadline => TimeSpan.FromSeconds(10);

    private string BaseUrl => configuration.GetValue("Ebay:UseSandbox", false) ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";

    public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
    {
        string token = await GetTokenAsync(cancellationToken);
        string filter = CompMatching.BandOf(query.Item.Condition) switch
        {
            ConditionBand.New => "buyingOptions:{FIXED_PRICE},conditions:{NEW}",
            ConditionBand.Used => "buyingOptions:{FIXED_PRICE},conditions:{USED}",
            _ => "buyingOptions:{FIXED_PRICE}",
        };

        // A barcode search misses listings without one, so it falls back to the item's name. Never to
        // the digits: eBay matches those against anything, which once priced a ranch cup as a video game.
        List<ItemSummary> items = query.Barcode is { } barcode
            ? await SearchAsync($"gtin={barcode}", filter, token, cancellationToken)
            : [];
        if (items.Count == 0)
        {
            items = await SearchAsync($"q={Uri.EscapeDataString(query.SearchText)}", filter, token, cancellationToken);
        }

        return new CompSourceResult(items
            .Select(item => new Comp(
                CompKind.Asking,
                "eBay",
                item.Title ?? string.Empty,
                Money(item.Price),
                item.ShippingOptions?.FirstOrDefault()?.ShippingCost is { } shipping ? Money(shipping) : null,
                item.Condition,
                item.ItemWebUrl,
                Date: null,
                Verified: true))
            .Where(comp => comp.Price > 0 && comp.Title.Length > 0)
            .ToArray());
    }

    private async Task<List<ItemSummary>> SearchAsync(string search, string filter, string token, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get,
            $"{BaseUrl}/buy/browse/v1/item_summary/search?{search}&filter={Uri.EscapeDataString(filter)}&limit=25");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("eBay Browse search returned {Status}", response.StatusCode);
            throw new CompSourceUnavailableException("eBay could not be searched right now.");
        }

        SearchResponse? result = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);
        return result?.ItemSummaries ?? [];
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(TokenCacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration["Ebay:ClientId"]}:{configuration["Ebay:ClientSecret"]}"));
        using HttpRequestMessage request = new(HttpMethod.Post, $"{BaseUrl}/identity/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "https://api.ebay.com/oauth/api_scope",
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("eBay token request returned {Status}", response.StatusCode);
            throw new CompSourceUnavailableException("eBay refused the app's credentials.");
        }

        TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
            ?? throw new CompSourceUnavailableException("eBay sent an empty sign-in answer.");
        cache.Set(TokenCacheKey, token.AccessToken, TimeSpan.FromSeconds(Math.Max(60, token.ExpiresIn - 120)));
        return token.AccessToken;
    }

    private static decimal Money(Price? price) =>
        decimal.TryParse(price?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : 0;

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("itemSummaries")] public List<ItemSummary>? ItemSummaries { get; set; }
    }

    private sealed class ItemSummary
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("price")] public Price? Price { get; set; }
        [JsonPropertyName("condition")] public string? Condition { get; set; }
        [JsonPropertyName("itemWebUrl")] public string? ItemWebUrl { get; set; }
        [JsonPropertyName("shippingOptions")] public List<ShippingOption>? ShippingOptions { get; set; }
    }

    private sealed class Price
    {
        [JsonPropertyName("value")] public string? Value { get; set; }
    }

    private sealed class ShippingOption
    {
        [JsonPropertyName("shippingCost")] public Price? ShippingCost { get; set; }
    }
}
