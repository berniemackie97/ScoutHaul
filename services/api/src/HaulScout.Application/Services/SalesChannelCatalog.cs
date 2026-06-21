using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public static class SalesChannelCatalog
{
    public static readonly SaleChannelOptionDto[] Channels =
    [
        new("Storefront", "Own storefront", "Owned channels"),
        new("Local POS", "Local / in-person POS", "Owned channels"),
        new("eBay", "eBay", "Marketplaces"),
        new("Facebook Marketplace", "Facebook Marketplace", "Marketplaces"),
        new("Mercari", "Mercari", "Marketplaces"),
        new("Poshmark", "Poshmark", "Marketplaces"),
        new("Whatnot", "Whatnot", "Marketplaces"),
        new("Etsy", "Etsy", "Marketplaces"),
        new("Amazon", "Amazon", "Marketplaces"),
        new("OfferUp", "OfferUp", "Marketplaces"),
        new("Craigslist", "Craigslist", "Marketplaces"),
        new("Auction", "Auction house", "Other channels"),
        new("Consignment", "Consignment sale", "Other channels"),
        new("Wholesale", "Wholesale", "Other channels"),
        new("Other", "Other controlled channel", "Other channels"),
    ];

    public static string? CanonicalValue(string? value, IEnumerable<string>? legacyValues = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        return Channels.FirstOrDefault(channel => string.Equals(channel.Value, normalized, StringComparison.OrdinalIgnoreCase))?.Value
            ?? legacyValues?.FirstOrDefault(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
    }
}
