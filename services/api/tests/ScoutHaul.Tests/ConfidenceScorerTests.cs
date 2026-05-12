using ScoutHaul.Application.Services;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Tests;

public sealed class ConfidenceScorerTests
{
    [Fact]
    public void Score_ActiveListingsOnlyRemainUnavailableConfidence()
    {
        var scorer = new ConfidenceScorer();

        var result = scorer.Score(
            observations:
            [
                new MarketObservation
                {
                    OrganizationId = Guid.NewGuid(),
                    ObservationType = MarketObservationType.ActiveListing,
                    Source = "eBay",
                    Title = "Active listing",
                    Price = 150m,
                    TotalPrice = 150m,
                    ObservedAt = DateTimeOffset.UtcNow
                }
            ],
            pricingBasis: PricingBasisType.Unavailable,
            expectedCondition: "Used");

        Assert.True(result.Score <= 15);
        Assert.Contains(result.Warnings, warning => warning.Contains("Active listings", StringComparison.OrdinalIgnoreCase));
    }
}
