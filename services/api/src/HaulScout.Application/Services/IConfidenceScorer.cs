using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Services;

public interface IConfidenceScorer
{
    ConfidenceScoreResult Score(
        IEnumerable<MarketObservation> observations,
        PricingBasisType pricingBasis,
        string? expectedCondition,
        IEnumerable<string>? riskFlags = null,
        DateTimeOffset? now = null);
}
