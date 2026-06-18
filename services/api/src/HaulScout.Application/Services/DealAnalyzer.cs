using HaulScout.Application.Contracts;
using HaulScout.Domain;

namespace HaulScout.Application.Services;

public sealed class DealAnalyzer : IDealAnalyzer
{
    public DealAnalysisResult Analyze(DealAnalysisInput input)
    {
        List<string> warnings = new List<string>();

        if (input.PricingBasis == PricingBasisType.Unavailable)
        {
            warnings.Add("Pricing basis is unavailable. The app will not invent a market price.");
            return new DealAnalysisResult(null, null, null, null, null, RecommendationDecisionType.Unavailable, warnings);
        }

        if (!input.ExpectedSalePrice.HasValue || input.ExpectedSalePrice <= 0)
        {
            warnings.Add("Expected sale price is missing. Add sold comps, internal sale history, or a manual expected sale price before making a buy decision.");
            return new DealAnalysisResult(null, null, null, null, null, RecommendationDecisionType.ResearchRequired, warnings);
        }

        if (input.PlatformFeePercent < 0 || input.PaymentFeePercent < 0)
        {
            warnings.Add("Fee percentages cannot be negative.");
            return new DealAnalysisResult(null, null, null, null, null, RecommendationDecisionType.ResearchRequired, warnings);
        }

        if (HasNegativeCostInput(input))
        {
            warnings.Add("Costs, fees, reserves, and desired profit cannot be negative.");
            return new DealAnalysisResult(null, null, null, null, null, RecommendationDecisionType.ResearchRequired, warnings);
        }

        decimal expectedSalePrice = input.ExpectedSalePrice.Value;
        decimal platformFees = expectedSalePrice * NormalizePercent(input.PlatformFeePercent);
        decimal paymentFees = expectedSalePrice * NormalizePercent(input.PaymentFeePercent) + input.FixedPaymentFee;
        decimal variableCosts = platformFees
                                + paymentFees
                                + input.EstimatedShippingCostPaidBySeller
                                + input.SuppliesCost
                                + input.RiskReserve;

        decimal breakEvenPrice = expectedSalePrice - variableCosts;
        if (breakEvenPrice < 0)
        {
            warnings.Add("Estimated fees and costs exceed the expected sale price.");
        }

        decimal profitFloorCeiling = breakEvenPrice - input.DesiredMinimumProfit;

        decimal? marginCeiling = null;
        if (input.TargetMarginPercent.HasValue)
        {
            if (input.TargetMarginPercent is <= 0 or >= 100)
            {
                warnings.Add("Target margin percent must be greater than 0 and less than 100 to produce a margin-based ceiling.");
            }
            else
            {
                decimal requiredProfitForMargin = expectedSalePrice * NormalizePercent(input.TargetMarginPercent.Value);
                marginCeiling = expectedSalePrice - variableCosts - requiredProfitForMargin;
            }
        }

        decimal recommendedMaxBuyPrice = marginCeiling.HasValue
            ? Math.Min(profitFloorCeiling, marginCeiling.Value)
            : profitFloorCeiling;

        recommendedMaxBuyPrice = Math.Max(0, recommendedMaxBuyPrice);

        decimal? buyCost = input.PurchasePrice ?? input.SellerAskingPrice;
        decimal? expectedNetProfit = null;
        decimal? roiPercent = null;
        decimal? marginPercent = null;
        if (buyCost.HasValue && buyCost > 0)
        {
            expectedNetProfit = expectedSalePrice - variableCosts - buyCost.Value;
            roiPercent = expectedNetProfit.Value / buyCost.Value * 100;
            marginPercent = expectedNetProfit.Value / expectedSalePrice * 100;
        }
        else
        {
            warnings.Add("Seller asking price or purchase price is missing. Max buy price is available, but the decision requires a known cost.");
        }

        if (input.ConfidenceScore is < 35)
        {
            warnings.Add("Confidence score is low. Treat this as research required until more reliable sold comps or internal sales are added.");
        }

        if (input.PricingBasis is PricingBasisType.ManualEstimate or PricingBasisType.CachedStale)
        {
            warnings.Add($"Pricing basis is {input.PricingBasis}; verify before buying high-risk inventory.");
        }

        RecommendationDecisionType decision = Decide(input, recommendedMaxBuyPrice, expectedNetProfit, marginPercent, warnings);

        return new DealAnalysisResult(
            RoundMoney(recommendedMaxBuyPrice),
            RoundMoney(expectedNetProfit),
            RoundPercent(roiPercent),
            RoundPercent(marginPercent),
            RoundMoney(breakEvenPrice),
            decision,
            warnings);
    }

    private static RecommendationDecisionType Decide(
        DealAnalysisInput input,
        decimal recommendedMaxBuyPrice,
        decimal? expectedNetProfit,
        decimal? marginPercent,
        IReadOnlyCollection<string> warnings)
    {
        if (!input.SellerAskingPrice.HasValue && !input.PurchasePrice.HasValue)
        {
            return RecommendationDecisionType.ResearchRequired;
        }

        if (input.ConfidenceScore is < 35)
        {
            return RecommendationDecisionType.ResearchRequired;
        }

        decimal cost = input.PurchasePrice ?? input.SellerAskingPrice!.Value;
        bool targetMarginMet = !input.TargetMarginPercent.HasValue
                               || (marginPercent.HasValue && marginPercent.Value >= input.TargetMarginPercent.Value);
        bool profitMet = expectedNetProfit.HasValue && expectedNetProfit.Value >= input.DesiredMinimumProfit;

        if (cost <= recommendedMaxBuyPrice && profitMet && targetMarginMet && warnings.Count == 0)
        {
            return RecommendationDecisionType.Buy;
        }

        if (cost <= recommendedMaxBuyPrice && profitMet && targetMarginMet)
        {
            return RecommendationDecisionType.Buy;
        }

        if (cost <= recommendedMaxBuyPrice * 1.15m && expectedNetProfit is > 0)
        {
            return RecommendationDecisionType.Negotiate;
        }

        return RecommendationDecisionType.Pass;
    }

    private static bool HasNegativeCostInput(DealAnalysisInput input) =>
        input.FixedPaymentFee < 0
        || input.EstimatedShippingCostPaidBySeller < 0
        || input.SuppliesCost < 0
        || input.RiskReserve < 0
        || input.DesiredMinimumProfit < 0;

    private static decimal NormalizePercent(decimal percent) => percent / 100m;
    private static decimal? RoundMoney(decimal? value) => value.HasValue ? decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;
    private static decimal? RoundPercent(decimal? value) => value.HasValue ? decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero) : null;
}
