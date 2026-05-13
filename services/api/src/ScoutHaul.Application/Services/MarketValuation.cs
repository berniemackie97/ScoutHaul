using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Application.Services;

public sealed record MarketValuationResult(
    decimal? EstimatedSalePrice,
    PricingBasisType PricingBasis,
    int SoldCompCount,
    int ActiveListingCount,
    IReadOnlyList<MarketObservation> SoldComps);

public static class MarketValuation
{
    public static MarketValuationResult Estimate(
        decimal? explicitExpectedSalePrice,
        decimal? inventoryEstimatedSalePrice,
        PricingBasisType? explicitPricingBasis,
        IEnumerable<MarketObservation> observations)
    {
        List<MarketObservation> activeObservations = observations
            .Where(observation => !observation.IsArchived)
            .ToList();

        List<MarketObservation> soldComps = activeObservations
            .Where(IsSoldLike)
            .ToList();

        int activeListingCount = activeObservations.Count(IsActiveLike);
        decimal? soldCompMedian = MedianOrNull(soldComps
            .Select(comp => comp.TotalPrice)
            .Where(price => price > 0));
        decimal? estimateMedian = MedianOrNull(activeObservations
            .Where(observation => observation.ObservationType == MarketObservationType.Estimate)
            .Select(comp => comp.TotalPrice)
            .Where(price => price > 0));

        decimal? estimatedSalePrice = explicitExpectedSalePrice
            ?? soldCompMedian
            ?? estimateMedian
            ?? inventoryEstimatedSalePrice;

        PricingBasisType pricingBasis = explicitPricingBasis
            ?? DerivePricingBasis(explicitExpectedSalePrice, soldCompMedian, estimateMedian, inventoryEstimatedSalePrice, soldComps);

        return new MarketValuationResult(
            estimatedSalePrice,
            pricingBasis,
            soldComps.Count,
            activeListingCount,
            soldComps);
    }

    public static bool IsSoldLike(MarketObservation observation) =>
        observation.ObservationType is MarketObservationType.Sold
            or MarketObservationType.ManualSold
            or MarketObservationType.InternalSale;

    public static bool IsActiveLike(MarketObservation observation) =>
        observation.ObservationType is MarketObservationType.ActiveListing
            or MarketObservationType.ManualActive;

    private static PricingBasisType DerivePricingBasis(
        decimal? explicitExpectedSalePrice,
        decimal? soldCompMedian,
        decimal? estimateMedian,
        decimal? inventoryEstimatedSalePrice,
        IReadOnlyCollection<MarketObservation> soldComps)
    {
        if (explicitExpectedSalePrice.HasValue)
        {
            return PricingBasisType.ManualEstimate;
        }

        if (soldCompMedian.HasValue)
        {
            if (soldComps.Any(comp => comp.ObservationType == MarketObservationType.Sold))
            {
                return PricingBasisType.LiveValidated;
            }

            if (soldComps.Any(comp => comp.ObservationType == MarketObservationType.InternalSale))
            {
                return PricingBasisType.InternalHistory;
            }

            return PricingBasisType.ManualEstimate;
        }

        if (estimateMedian.HasValue)
        {
            return PricingBasisType.CachedRecent;
        }

        return inventoryEstimatedSalePrice.HasValue
            ? PricingBasisType.ManualEstimate
            : PricingBasisType.Unavailable;
    }

    private static decimal? MedianOrNull(IEnumerable<decimal> values)
    {
        List<decimal> ordered = values.OrderBy(value => value).ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        int middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? decimal.Round((ordered[middle - 1] + ordered[middle]) / 2, 2, MidpointRounding.AwayFromZero)
            : ordered[middle];
    }
}
