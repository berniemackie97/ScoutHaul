using System.Globalization;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public sealed record ListingPriceAdvice(
    decimal? SuggestedListPrice,
    string? PriceRationale,
    IReadOnlyList<ResaleComp> PricingComps);

/// <summary>
/// Converts returned resale comps into a deterministic marketplace list price. LLMs may help find
/// and summarize comps, but the final price needs to be repeatable, auditable, and protected from
/// common mismatches such as raw-vs-graded, language/region, lots, special variants, and outliers.
/// </summary>
public static class ListingPriceAdvisor
{
    public static ListingPriceAdvice Calculate(GenerateListingRequest request, ResaleEstimate? estimate)
    {
        if (estimate is null)
        {
            return new ListingPriceAdvice(null, null, []);
        }

        IReadOnlyList<ResaleComp> allComps = estimate.Comps ?? [];
        MarketProfile item = CompMatching.Profile(
            request.Condition,
            request.Title,
            request.Brand,
            request.Model,
            request.Category,
            request.Color,
            request.SizeSystem,
            request.SizeValue,
            request.AttributesJson,
            request.Notes);
        List<(ResaleComp Comp, bool IsCompatible)> evidence = allComps
            .Select(comp => (comp, comp.Price > 0
                && CompMatching.Mismatches(item, CompMatching.Profile(comp.Condition, comp.Note, comp.SourceUrl)).Count == 0))
            .ToList();

        List<ResaleComp> acceptedSold = evidence
            .Where(entry => IsSoldComp(entry.Comp) && entry.IsCompatible)
            .Select(entry => entry.Comp)
            .ToList();
        List<ResaleComp> soldComps = RemovePriceOutliers(acceptedSold);

        if (soldComps.Count > 0)
        {
            decimal recommendation = RecommendFromSoldComps(soldComps);
            decimal rounded = PriceMath.ToMarketplaceEnding(recommendation);
            decimal low = soldComps.Min(comp => comp.Price);
            decimal high = soldComps.Max(comp => comp.Price);
            int rejectedSoldCount = allComps.Count(IsSoldComp) - soldComps.Count;

            string rationale = $"Based on {soldComps.Count} compatible sold comp{Plural(soldComps.Count)}"
                + $" ({Money(low)}–{Money(high)}); priced near the upper-middle of the sold range"
                + (rejectedSoldCount > 0 ? $" after excluding {rejectedSoldCount} mismatched/outlier sold comp{Plural(rejectedSoldCount)}." : ".");

            return new ListingPriceAdvice(rounded, rationale, soldComps.Take(8).ToArray());
        }

        List<ResaleComp> acceptedActive = evidence
            .Where(entry => !IsSoldComp(entry.Comp) && entry.IsCompatible)
            .Select(entry => entry.Comp)
            .Take(8)
            .ToList();

        decimal? rangePrice = PriceFromRange(estimate);
        if (rangePrice is > 0)
        {
            string rationale = acceptedActive.Count > 0
                ? $"No compatible sold comps were returned; priced from the resale range with {acceptedActive.Count} active comp{Plural(acceptedActive.Count)} as weak support."
                : "No compatible sold comps were returned; priced from the estimated resale range and should be manually verified.";

            return new ListingPriceAdvice(PriceMath.ToMarketplaceEnding(rangePrice.Value), rationale, acceptedActive);
        }

        return new ListingPriceAdvice(null, estimate.Rationale, acceptedActive);
    }

    private static bool IsSoldComp(ResaleComp comp) =>
        string.Equals(comp.Status, "sold", StringComparison.OrdinalIgnoreCase);

    private static decimal? PriceFromRange(ResaleEstimate estimate)
    {
        if (estimate.High <= 0)
        {
            return null;
        }

        if (estimate.Low <= 0 || estimate.Low >= estimate.High)
        {
            return estimate.High;
        }

        return string.Equals(estimate.PriceRangeType, "wide", StringComparison.OrdinalIgnoreCase)
            ? estimate.Low + ((estimate.High - estimate.Low) * 0.60m)
            : estimate.High;
    }

    private static decimal RecommendFromSoldComps(IReadOnlyList<ResaleComp> comps)
    {
        List<decimal> prices = comps.Select(comp => comp.Price).OrderBy(price => price).ToList();
        decimal median = PriceMath.Median(prices);
        if (prices.Count < 3)
        {
            return median;
        }

        decimal upperMiddle = PriceMath.PercentileNearestRank(prices, 0.75m);
        decimal guardedUpper = median * 1.12m;
        return Math.Max(median, Math.Min(upperMiddle, guardedUpper));
    }

    private static List<ResaleComp> RemovePriceOutliers(IReadOnlyList<ResaleComp> comps)
    {
        if (comps.Count < 3)
        {
            return comps.ToList();
        }

        List<decimal> ordered = comps.Select(comp => comp.Price).OrderBy(price => price).ToList();
        decimal median = PriceMath.Median(ordered);
        decimal lowerBound = median * (median < 20m ? 0.20m : 0.40m);
        decimal upperBound = median * 2.50m;

        List<ResaleComp> filtered = comps
            .Where(comp => comp.Price >= lowerBound && comp.Price <= upperBound)
            .ToList();

        return filtered.Count > 0 ? filtered : comps.ToList();
    }

    private static string Money(decimal value) => value.ToString("$0.##", CultureInfo.InvariantCulture);

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
