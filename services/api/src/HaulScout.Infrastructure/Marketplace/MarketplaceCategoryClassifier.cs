using HaulScout.Application.Contracts;

namespace HaulScout.Infrastructure.Marketplace;

internal static class MarketplaceCategoryClassifier
{
    private static readonly string[] CollectibleTerms =
    [
        "video game",
        "games",
        "gaming",
        "nintendo",
        "playstation",
        "xbox",
        "sega",
        "atari",
        "pokemon",
        "trading card",
        "sports card",
        "tcg",
        "comic",
        "collectible",
        "strategy guide",
        "books",
        "media"
    ];

    public static bool IsCollectiblePricingCandidate(BarcodeLookupResult? product, string query)
    {
        string text = string.Join(' ', product?.Title, product?.Brand, product?.Category, query).ToLowerInvariant();
        return CollectibleTerms.Any(text.Contains);
    }
}
