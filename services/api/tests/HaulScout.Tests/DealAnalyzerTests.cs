using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;

namespace HaulScout.Tests;

public sealed class DealAnalyzerTests
{
    [Fact]
    public void Analyze_UsesLowerOfProfitAndMarginCeilings()
    {
        var analyzer = new DealAnalyzer();

        var result = analyzer.Analyze(new DealAnalysisInput(
            ExpectedSalePrice: 100m,
            SellerAskingPrice: 40m,
            PurchasePrice: null,
            Platform: "eBay",
            PlatformFeePercent: 13m,
            PaymentFeePercent: 3m,
            FixedPaymentFee: 0.30m,
            EstimatedShippingCostPaidBySeller: 8m,
            SuppliesCost: 2m,
            RiskReserve: 3m,
            DesiredMinimumProfit: 20m,
            TargetMarginPercent: 55m,
            ConfidenceScore: 80,
            PricingBasis: PricingBasisType.LiveValidated));

        Assert.Equal(15.70m, result.RecommendedMaxBuyPrice);
        Assert.Equal(30.70m, result.ExpectedNetProfit);
        Assert.Equal(RecommendationDecisionType.Pass, result.Decision);
    }

    [Fact]
    public void Analyze_DoesNotInventPriceWhenExpectedSalePriceMissing()
    {
        var analyzer = new DealAnalyzer();

        var result = analyzer.Analyze(new DealAnalysisInput(
            ExpectedSalePrice: null,
            SellerAskingPrice: 10m,
            PurchasePrice: null,
            Platform: null,
            PlatformFeePercent: 0,
            PaymentFeePercent: 0,
            FixedPaymentFee: 0,
            EstimatedShippingCostPaidBySeller: 0,
            SuppliesCost: 0,
            RiskReserve: 0,
            DesiredMinimumProfit: 0,
            TargetMarginPercent: null,
            ConfidenceScore: 0,
            PricingBasis: PricingBasisType.Unavailable));

        Assert.Null(result.RecommendedMaxBuyPrice);
        Assert.Equal(RecommendationDecisionType.Unavailable, result.Decision);
    }

    [Fact]
    public void Analyze_RejectsNegativeCostInputs()
    {
        var analyzer = new DealAnalyzer();

        var result = analyzer.Analyze(new DealAnalysisInput(
            ExpectedSalePrice: 100m,
            SellerAskingPrice: 25m,
            PurchasePrice: null,
            Platform: "eBay",
            PlatformFeePercent: 13m,
            PaymentFeePercent: 3m,
            FixedPaymentFee: -0.30m,
            EstimatedShippingCostPaidBySeller: 0,
            SuppliesCost: 0,
            RiskReserve: 0,
            DesiredMinimumProfit: 20m,
            TargetMarginPercent: null,
            ConfidenceScore: 80,
            PricingBasis: PricingBasisType.LiveValidated));

        Assert.Null(result.ExpectedNetProfit);
        Assert.Equal(RecommendationDecisionType.ResearchRequired, result.Decision);
    }
}
