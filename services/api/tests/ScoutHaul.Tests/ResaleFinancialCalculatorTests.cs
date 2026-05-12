using ScoutHaul.Application.Services;

namespace ScoutHaul.Tests;

public sealed class ResaleFinancialCalculatorTests
{
    [Fact]
    public void CalculateRealizedSale_ComputesNetProfitRoiAndMargin()
    {
        RealizedSaleFinancials result = ResaleFinancialCalculator.CalculateRealizedSale(
            soldPrice: 100m,
            purchaseCost: 40m,
            platformFees: 13m,
            paymentFees: 3.30m,
            shippingCost: 8m,
            suppliesCost: 2m,
            otherCosts: 1m);

        Assert.Equal(67.30m, result.TotalFeesAndCosts);
        Assert.Equal(32.70m, result.NetProfit);
        Assert.Equal(81.75m, result.RoiPercent);
        Assert.Equal(32.70m, result.MarginPercent);
    }
}
