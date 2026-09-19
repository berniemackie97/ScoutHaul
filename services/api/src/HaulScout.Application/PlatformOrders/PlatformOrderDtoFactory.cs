using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;

namespace HaulScout.Application.PlatformOrders;

public static class PlatformOrderDtoFactory
{
    public static PlatformOrderDto ToPlatformOrderDto(
        PlatformOrder order,
        IReadOnlyDictionary<Guid, PlatformLineSuggestion>? lineSuggestions = null)
    {
        decimal expectedPayout = ExpectedPayout(order);
        return new PlatformOrderDto(
            order.Id,
            order.Platform,
            order.ExternalOrderId,
            order.BuyerName,
            order.BuyerReference,
            order.BuyerEmail,
            order.BuyerNote,
            order.ShipToName,
            order.ShipToPhone,
            order.ShipToLine1,
            order.ShipToLine2,
            order.ShipToCity,
            order.ShipToRegion,
            order.ShipToPostalCode,
            order.ShipToCountry,
            order.OrderedAt,
            order.ItemsSubtotal,
            order.ShippingCharged,
            order.TaxCollected,
            order.FeeTotal,
            order.SellerShippingCost,
            order.PayoutAmount,
            expectedPayout,
            order.PayoutAmount.HasValue ? order.PayoutAmount.Value - expectedPayout : 0,
            order.Currency,
            order.TaxRemittedByPlatform,
            order.FeeBreakdownJson,
            order.Carrier,
            order.TrackingNumber,
            order.PaidAt,
            order.ShippedAt,
            order.Status,
            order.ReconciledSaleGroupId,
            order.ReconciledAt,
            order.OrderedAt.ToUniversalTime(),
            order.ScrapedAt,
            order.Lines
                .OrderBy(line => line.CreatedAt)
                .Select(line =>
                {
                    PlatformLineSuggestion? suggestion =
                        lineSuggestions is not null && lineSuggestions.TryGetValue(line.Id, out PlatformLineSuggestion? match)
                            ? match
                            : null;
                    return new PlatformOrderLineDto(
                        line.Id,
                        line.Title,
                        line.UnitPrice,
                        line.Quantity,
                        line.ExternalItemId,
                        line.Sku,
                        line.ShippingCharged,
                        line.TaxCollected,
                        line.PlatformFees,
                        line.SellerShippingCost,
                        line.NetSales,
                        line.TotalSales,
                        line.FeeBreakdownJson,
                        line.InventoryItemId,
                        line.InventoryItem?.Title,
                        suggestion?.Id,
                        suggestion?.Title,
                        suggestion?.AlreadySold ?? false,
                        suggestion?.Exact ?? false);
                })
                .ToList(),
            order.FeesPending);
    }

    public static SaleDto ToSaleDto(Sale sale) => new(
        sale.Id,
        sale.InventoryItemId,
        sale.InventoryItem?.Title ?? string.Empty,
        sale.ListingId,
        sale.Platform,
        sale.SoldPrice,
        sale.ShippingCharged,
        sale.CostOfGoods,
        sale.PlatformFees,
        sale.PaymentFees,
        sale.ShippingCost,
        sale.SuppliesCost,
        sale.OtherCosts,
        sale.SalesTaxCollected,
        sale.TaxRemittedByPlatform,
        ResaleFinancialCalculator.SalesTaxOwed(sale.SalesTaxCollected, sale.TaxRemittedByPlatform),
        sale.NetProfit,
        sale.Status,
        sale.SoldAt,
        sale.Notes,
        SaleWorkflow.Next(sale.Status),
        sale.OrderId ?? sale.SaleGroupId,
        sale.Order?.ExternalOrderId ?? sale.Order?.OrderNumber ?? sale.GroupOrderReference,
        sale.Refunds.Sum(refund => refund.ItemAmount + refund.ShippingRefunded),
        sale.Refunds.Sum(refund => refund.FeeCredit),
        sale.Refunds.Count,
        sale.OrderId);

    public static decimal ExpectedPayout(PlatformOrder order) =>
        order.ItemsSubtotal + order.ShippingCharged - order.FeeTotal - (order.SellerShippingCost ?? 0) + (order.TaxRemittedByPlatform ? 0 : order.TaxCollected);
}
