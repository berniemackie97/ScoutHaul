using ScoutHaul.Application.Services;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Tests;

public sealed class MarketValuationTests
{
    [Fact]
    public void Estimate_PrefersSoldCompMedianOverInventoryManualEstimate()
    {
        MarketValuationResult result = MarketValuation.Estimate(
            explicitExpectedSalePrice: null,
            inventoryEstimatedSalePrice: 999m,
            explicitPricingBasis: null,
            observations:
            [
                Observation(MarketObservationType.Sold, 100m),
                Observation(MarketObservationType.Sold, 120m)
            ]);

        Assert.Equal(110m, result.EstimatedSalePrice);
        Assert.Equal(PricingBasisType.LiveValidated, result.PricingBasis);
        Assert.Equal(2, result.SoldCompCount);
    }

    [Fact]
    public void Estimate_DoesNotUseActiveListingsAsSoldPrice()
    {
        MarketValuationResult result = MarketValuation.Estimate(
            explicitExpectedSalePrice: null,
            inventoryEstimatedSalePrice: null,
            explicitPricingBasis: null,
            observations:
            [
                Observation(MarketObservationType.ActiveListing, 200m),
                Observation(MarketObservationType.ManualActive, 180m)
            ]);

        Assert.Null(result.EstimatedSalePrice);
        Assert.Equal(PricingBasisType.Unavailable, result.PricingBasis);
        Assert.Equal(0, result.SoldCompCount);
        Assert.Equal(2, result.ActiveListingCount);
    }

    [Fact]
    public void Estimate_UsesProviderEstimatesWhenSoldCompsAreUnavailable()
    {
        MarketValuationResult result = MarketValuation.Estimate(
            explicitExpectedSalePrice: null,
            inventoryEstimatedSalePrice: 999m,
            explicitPricingBasis: null,
            observations:
            [
                Observation(MarketObservationType.ActiveListing, 220m),
                Observation(MarketObservationType.Estimate, 100m),
                Observation(MarketObservationType.Estimate, 120m)
            ]);

        Assert.Equal(110m, result.EstimatedSalePrice);
        Assert.Equal(PricingBasisType.CachedRecent, result.PricingBasis);
        Assert.Equal(0, result.SoldCompCount);
        Assert.Equal(1, result.ActiveListingCount);
    }

    [Fact]
    public void Estimate_ExplicitExpectedSalePriceIsManualEstimateByDefault()
    {
        MarketValuationResult result = MarketValuation.Estimate(
            explicitExpectedSalePrice: 75m,
            inventoryEstimatedSalePrice: null,
            explicitPricingBasis: null,
            observations: []);

        Assert.Equal(75m, result.EstimatedSalePrice);
        Assert.Equal(PricingBasisType.ManualEstimate, result.PricingBasis);
    }

    private static MarketObservation Observation(MarketObservationType type, decimal totalPrice) =>
        new()
        {
            OrganizationId = Guid.NewGuid(),
            ObservationType = type,
            Source = "Test",
            Title = "Comparable item",
            Price = totalPrice,
            TotalPrice = totalPrice,
            ObservedAt = DateTimeOffset.UtcNow
        };
}
