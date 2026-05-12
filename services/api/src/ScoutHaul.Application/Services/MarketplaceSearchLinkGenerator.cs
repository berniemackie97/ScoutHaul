using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public sealed class MarketplaceSearchLinkGenerator : IMarketplaceSearchLinkGenerator
{
    public MarketplaceSearchLinksDto Generate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query is required.", nameof(query));
        }

        string encoded = Uri.EscapeDataString(query.Trim());
        List<MarketplaceSearchLinkDto> links = new List<MarketplaceSearchLinkDto>
        {
            new("eBay", "eBay active listings", $"https://www.ebay.com/sch/i.html?_nkw={encoded}"),
            new("eBay", "eBay sold/completed listings", $"https://www.ebay.com/sch/i.html?_nkw={encoded}&LH_Sold=1&LH_Complete=1"),
            new("Facebook Marketplace", "Facebook Marketplace search", $"https://www.facebook.com/marketplace/search/?query={encoded}"),
            new("Google", "Google web search", $"https://www.google.com/search?q={encoded}"),
            new("Google Shopping", "Google Shopping search", $"https://www.google.com/search?tbm=shop&q={encoded}"),
            new("Amazon", "Amazon search", $"https://www.amazon.com/s?k={encoded}"),
            new("Whatnot", "Whatnot search", $"https://www.whatnot.com/search?q={encoded}"),
            new("Mercari", "Mercari search", $"https://www.mercari.com/search/?keyword={encoded}"),
            new("Craigslist", "Craigslist search", $"https://www.craigslist.org/search/sss?query={encoded}")
        };

        return new MarketplaceSearchLinksDto(query.Trim(), links);
    }
}
