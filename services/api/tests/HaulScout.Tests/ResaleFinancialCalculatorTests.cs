using HaulScout.Application.Services;

namespace HaulScout.Tests;

public sealed class ResaleFinancialCalculatorTests
{
    [Fact]
    public void CalculateRealizedSale_ComputesNetProfitRoiAndMargin()
    {
        RealizedSaleFinancials result = ResaleFinancialCalculator.CalculateRealizedSale(
            soldPrice: 100m,
            shippingCharged: 12m,
            purchaseCost: 40m,
            platformFees: 13m,
            paymentFees: 3.30m,
            shippingCost: 8m,
            suppliesCost: 2m,
            otherCosts: 1m);

        Assert.Equal(112m, result.SalesRevenue);
        Assert.Equal(27.30m, result.SellingCosts);
        Assert.Equal(67.30m, result.TotalFeesAndCosts);
        Assert.Equal(84.70m, result.MarketplaceNetBeforeCogs);
        Assert.Equal(44.70m, result.NetProfit);
        Assert.Equal(111.75m, result.RoiPercent);
        Assert.Equal(39.91m, result.MarginPercent);
    }
}
