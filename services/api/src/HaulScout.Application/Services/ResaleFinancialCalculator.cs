namespace HaulScout.Application.Services;

public sealed record RealizedSaleFinancials(
    decimal SoldPrice,
    decimal PurchaseCost,
    decimal TotalFeesAndCosts,
    decimal NetProfit,
    decimal RoiPercent,
    decimal MarginPercent);

public static class ResaleFinancialCalculator
{
    public static RealizedSaleFinancials CalculateRealizedSale(
        decimal soldPrice,
        decimal purchaseCost,
        decimal platformFees,
        decimal paymentFees,
        decimal shippingCost,
        decimal suppliesCost,
        decimal otherCosts)
    {
        decimal normalizedSoldPrice = Math.Max(0, soldPrice);
        decimal normalizedPurchaseCost = Math.Max(0, purchaseCost);
        decimal feesAndCosts = Math.Max(0, platformFees)
                               + Math.Max(0, paymentFees)
                               + Math.Max(0, shippingCost)
                               + Math.Max(0, suppliesCost)
                               + Math.Max(0, otherCosts);

        decimal netProfit = normalizedSoldPrice - normalizedPurchaseCost - feesAndCosts;
        decimal roiPercent = normalizedPurchaseCost > 0 ? netProfit / normalizedPurchaseCost * 100 : 0;
        decimal marginPercent = normalizedSoldPrice > 0 ? netProfit / normalizedSoldPrice * 100 : 0;

        return new RealizedSaleFinancials(
            RoundMoney(normalizedSoldPrice),
            RoundMoney(normalizedPurchaseCost),
            RoundMoney(normalizedPurchaseCost + feesAndCosts),
            RoundMoney(netProfit),
            RoundPercent(roiPercent),
            RoundPercent(marginPercent));
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundPercent(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
