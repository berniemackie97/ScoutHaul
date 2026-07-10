using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Services;

public sealed class ConfidenceScorer : IConfidenceScorer
{
    public ConfidenceScoreResult Score(
        IEnumerable<MarketObservation> observations,
        PricingBasisType pricingBasis,
        string? expectedCondition,
        IEnumerable<string>? riskFlags = null,
        DateTimeOffset? now = null)
    {
        DateTimeOffset evaluatedAt = now ?? DateTimeOffset.UtcNow;
        List<MarketObservation> activeObservations = observations.Where(observation => !observation.IsArchived).ToList();
        List<MarketObservation> soldComps = activeObservations.Where(IsSoldLike).ToList();
        List<MarketObservation> activeListings = activeObservations.Where(IsActiveLike).ToList();
        List<string> reasons = [];
        List<string> warnings = [];
        int score = 0;

        if (soldComps.Count == 0)
        {
            warnings.Add("No sold comps or internal sales are available. Active listings are excluded from sold-price confidence.");
        }
        else
        {
            int soldCompScore = Math.Min(35, soldComps.Count * 10);
            score += soldCompScore;
            reasons.Add($"{soldComps.Count} sold/internal comp(s) contributed {soldCompScore} points.");
        }

        if (activeListings.Count > 0)
        {
            int activeScore = Math.Min(10, activeListings.Count * 2);
            score += activeScore;
            reasons.Add($"{activeListings.Count} active listing(s) contributed {activeScore} demand-context points.");
            warnings.Add("Active listings are demand context only and were not treated as sold prices.");
        }

        if (soldComps.Any(comp => comp.ObservationType == MarketObservationType.InternalSale))
        {
            score += 12;
            reasons.Add("Internal sales history added 12 points.");
        }

        score += ScorePricingBasis(pricingBasis, reasons, warnings);
        score += ScoreRecency(soldComps, evaluatedAt, reasons, warnings);
        score += ScoreSpread(soldComps, reasons, warnings);
        score += ScoreCondition(soldComps, expectedCondition, reasons, warnings);

        int riskPenalty = Math.Min(20, (riskFlags ?? []).Count(flag => !string.IsNullOrWhiteSpace(flag)) * 5);
        if (riskPenalty > 0)
        {
            score -= riskPenalty;
            warnings.Add($"Risk flags reduced confidence by {riskPenalty} points.");
        }

        if (pricingBasis == PricingBasisType.ManualEstimate)
        {
            score = Math.Min(score, 65);
            warnings.Add("Manual estimates are capped at 65 confidence until validated by sold comps or internal history.");
        }

        if (pricingBasis == PricingBasisType.Unavailable)
        {
            score = Math.Min(score, 15);
        }

        return new ConfidenceScoreResult(Math.Clamp(score, 0, 100), reasons, warnings);
    }

    private static bool IsSoldLike(MarketObservation observation) =>
        observation.ObservationType is MarketObservationType.Sold
            or MarketObservationType.ManualSold
            or MarketObservationType.InternalSale;

    private static bool IsActiveLike(MarketObservation observation) =>
        observation.ObservationType is MarketObservationType.ActiveListing
            or MarketObservationType.ManualActive;

    private static int ScorePricingBasis(PricingBasisType pricingBasis, List<string> reasons, List<string> warnings)
    {
        return pricingBasis switch
        {
            PricingBasisType.LiveValidated => AddReason(18, "Live validated pricing basis added 18 points.", reasons),
            PricingBasisType.InternalHistory => AddReason(16, "Internal history pricing basis added 16 points.", reasons),
            PricingBasisType.CachedRecent => AddReason(10, "Recent cached pricing basis added 10 points.", reasons),
            PricingBasisType.ManualEstimate => AddReason(4, "Manual estimate pricing basis added 4 points.", reasons),
            PricingBasisType.CachedStale => AddWarning(-8, "Cached data is stale and reduced confidence by 8 points.", warnings),
            PricingBasisType.Unavailable => AddWarning(0, "Pricing basis is unavailable.", warnings),
            _ => 0
        };
    }

    private static int ScoreRecency(IReadOnlyCollection<MarketObservation> soldComps, DateTimeOffset now, List<string> reasons, List<string> warnings)
    {
        if (soldComps.Count == 0)
        {
            return 0;
        }

        double averageAgeDays = soldComps
            .Select(comp => Math.Max(0, (now - (comp.SoldAt ?? comp.ObservedAt)).TotalDays))
            .Average();

        if (averageAgeDays <= 30)
        {
            reasons.Add("Sold comps average 30 days old or newer, adding 15 points.");
            return 15;
        }

        if (averageAgeDays <= 90)
        {
            reasons.Add("Sold comps average 90 days old or newer, adding 8 points.");
            return 8;
        }

        warnings.Add("Sold comps are older than 90 days on average.");
        return 2;
    }

    private static int ScoreSpread(IReadOnlyCollection<MarketObservation> soldComps, List<string> reasons, List<string> warnings)
    {
        if (soldComps.Count < 2)
        {
            warnings.Add("Add at least two sold comps to evaluate price consistency.");
            return 0;
        }

        List<decimal> prices = soldComps.Select(comp => comp.TotalPrice).Where(price => price > 0).OrderBy(price => price).ToList();
        if (prices.Count < 2)
        {
            return 0;
        }

        decimal median = Median(prices);
        if (median <= 0)
        {
            return 0;
        }

        decimal spreadRatio = (prices[^1] - prices[0]) / median;
        if (spreadRatio <= 0.20m)
        {
            reasons.Add("Sold comp prices are tightly clustered, adding 15 points.");
            return 15;
        }

        if (spreadRatio <= 0.45m)
        {
            reasons.Add("Sold comp prices are moderately consistent, adding 8 points.");
            return 8;
        }

        warnings.Add("Sold comp prices have a wide spread.");
        return 2;
    }

    private static int ScoreCondition(IReadOnlyCollection<MarketObservation> soldComps, string? expectedCondition, List<string> reasons, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(expectedCondition))
        {
            warnings.Add("No expected condition supplied, so condition match was not scored.");
            return 0;
        }

        int matchCount = soldComps.Count(comp =>
            !string.IsNullOrWhiteSpace(comp.Condition)
            && string.Equals(comp.Condition.Trim(), expectedCondition.Trim(), StringComparison.OrdinalIgnoreCase));

        if (matchCount == 0)
        {
            warnings.Add("No sold comps match the expected item condition.");
            return 0;
        }

        reasons.Add($"{matchCount} sold comp(s) matched condition, adding 10 points.");
        return 10;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    private static int AddReason(int points, string reason, List<string> reasons)
    {
        reasons.Add(reason);
        return points;
    }

    private static int AddWarning(int points, string warning, List<string> warnings)
    {
        warnings.Add(warning);
        return points;
    }
}
