namespace HaulScout.Domain.Services;

public sealed record RealizedSaleFinancials(
    decimal SoldPrice,
    decimal ShippingCharged,
    decimal SalesRevenue,
    decimal PurchaseCost,
    decimal SellingCosts,
    decimal TotalFeesAndCosts,
    decimal MarketplaceNetBeforeCogs,
    decimal NetProfit,
    decimal RoiPercent,
    decimal MarginPercent);

public static class ResaleFinancialCalculator
{
    public static RealizedSaleFinancials CalculateRealizedSale(
        decimal soldPrice,
        decimal shippingCharged,
        decimal purchaseCost,
        decimal platformFees,
        decimal paymentFees,
        decimal shippingCost,
        decimal suppliesCost,
        decimal otherCosts)
    {
        decimal normalizedSoldPrice = Math.Max(0, soldPrice);
        decimal normalizedShippingCharged = Math.Max(0, shippingCharged);
        decimal salesRevenue = normalizedSoldPrice + normalizedShippingCharged;
        decimal normalizedPurchaseCost = Math.Max(0, purchaseCost);
        decimal sellingCosts = Math.Max(0, platformFees)
                               + Math.Max(0, paymentFees)
                               + Math.Max(0, shippingCost)
                               + Math.Max(0, suppliesCost)
                               + Math.Max(0, otherCosts);

        decimal marketplaceNetBeforeCogs = salesRevenue - sellingCosts;
        decimal netProfit = marketplaceNetBeforeCogs - normalizedPurchaseCost;
        decimal roiPercent = normalizedPurchaseCost > 0 ? netProfit / normalizedPurchaseCost * 100 : 0;
        decimal marginPercent = salesRevenue > 0 ? netProfit / salesRevenue * 100 : 0;

        return new RealizedSaleFinancials(
            RoundMoney(normalizedSoldPrice),
            RoundMoney(normalizedShippingCharged),
            RoundMoney(salesRevenue),
            RoundMoney(normalizedPurchaseCost),
            RoundMoney(sellingCosts),
            RoundMoney(normalizedPurchaseCost + sellingCosts),
            RoundMoney(marketplaceNetBeforeCogs),
            RoundMoney(netProfit),
            RoundPercent(roiPercent),
            RoundPercent(marginPercent));
    }

    /// <summary>
    /// Sales tax we owe the state for a sale. Zero when the marketplace remits it (facilitator pass-through);
    /// otherwise the collected tax becomes our liability. Never affects net profit either way.
    /// </summary>
    public static decimal SalesTaxOwed(decimal salesTaxCollected, bool remittedByPlatform) =>
        remittedByPlatform ? 0m : RoundMoney(Math.Max(0, salesTaxCollected));

    /// <summary>Aggregate refund impact on a sale: money returned to the buyer, fee the platform credited back,
    /// and whether the item returned to sellable inventory (its COGS is no longer consumed by this sale).</summary>
    public sealed record SaleRefundImpact(
        decimal ItemRefunded,
        decimal ShippingRefunded,
        decimal TaxRefunded,
        decimal FeeCredit,
        bool ItemRestocked)
    {
        public static readonly SaleRefundImpact None = new(0, 0, 0, 0, false);
    }

    /// <summary>
    /// Net profit after refunds: the original figures with the refunded item price and buyer shipping removed
    /// from revenue, the platform's fee credit removed from selling costs, and COGS dropped when the item was
    /// returned to inventory. Tax (collected or refunded) never touches net. This is the authoritative stored
    /// <c>Sale.NetProfit</c> whenever a sale carries refunds.
    /// </summary>
    public static decimal NetProfitAfterRefunds(
        decimal soldPrice,
        decimal shippingCharged,
        decimal costOfGoods,
        decimal platformFees,
        decimal paymentFees,
        decimal shippingCost,
        decimal suppliesCost,
        decimal otherCosts,
        SaleRefundImpact refunds) =>
        CalculateRealizedSale(
            soldPrice - refunds.ItemRefunded,
            shippingCharged - refunds.ShippingRefunded,
            refunds.ItemRestocked ? 0 : costOfGoods,
            platformFees - refunds.FeeCredit,
            paymentFees,
            shippingCost,
            suppliesCost,
            otherCosts).NetProfit;

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundPercent(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
