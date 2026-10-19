namespace HaulScout.Domain.Tax;

public enum SalesTaxCollector
{
    /// <summary>A registered marketplace provider collects and remits; the sale is excluded (s. 212.05965(4)(a), F.S.).</summary>
    Marketplace,

    /// <summary>The business is the dealer and reports the sale.</summary>
    Business,

    /// <summary>Shipped orders through the platform's checkout are collected by it; local cash sales are the business's.</summary>
    Depends,

    Unknown,
}

/// <summary>Who collects Florida sales tax on each sales channel, so a sale filed under the wrong one is caught.</summary>
public static class FloridaMarketplaceProviders
{
    private static readonly IReadOnlyDictionary<string, SalesTaxCollector> Channels =
        new Dictionary<string, SalesTaxCollector>(StringComparer.OrdinalIgnoreCase)
        {
            ["eBay"] = SalesTaxCollector.Marketplace,
            ["Poshmark"] = SalesTaxCollector.Marketplace,
            ["Depop"] = SalesTaxCollector.Marketplace,
            ["Vinted"] = SalesTaxCollector.Marketplace,
            ["Mercari"] = SalesTaxCollector.Marketplace,
            ["Etsy"] = SalesTaxCollector.Marketplace,
            ["Amazon"] = SalesTaxCollector.Marketplace,
            ["Whatnot"] = SalesTaxCollector.Marketplace,
            ["Facebook Marketplace"] = SalesTaxCollector.Depends,
            ["OfferUp"] = SalesTaxCollector.Depends,
            ["Storefront"] = SalesTaxCollector.Business,
            ["Local POS"] = SalesTaxCollector.Business,
            ["Craigslist"] = SalesTaxCollector.Business,
            ["Wholesale"] = SalesTaxCollector.Business,
            ["Other"] = SalesTaxCollector.Business,
            ["Auction"] = SalesTaxCollector.Depends,
            ["Consignment"] = SalesTaxCollector.Depends,
        };

    public static SalesTaxCollector CollectorFor(string? channel) =>
        channel is not null && Channels.TryGetValue(channel.Trim(), out SalesTaxCollector collector) ? collector : SalesTaxCollector.Unknown;

    /// <summary>Sold face to face with no address: the buyer took it where the business is.</summary>
    public static bool IsInPerson(string? channel) =>
        channel is not null && (channel.Equals("Local POS", StringComparison.OrdinalIgnoreCase)
                                || channel.Equals("Craigslist", StringComparison.OrdinalIgnoreCase));
}
