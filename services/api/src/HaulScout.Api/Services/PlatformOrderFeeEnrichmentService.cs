using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Applies marketplace sales-report fee enrichment onto imported platform order lines and their reconciled sales.
/// This keeps report-specific accounting orchestration out of the HTTP controller.
/// </summary>
public sealed class PlatformOrderFeeEnrichmentService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    private static readonly SaleStatus[] ReleasedInventoryStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    public async Task<IngestPlatformOrdersResultDto> EnrichEbayListingSalesAsync(
        IReadOnlyList<EbayReportParser.EbayListingSalesMetric> metrics,
        CancellationToken cancellationToken)
    {
        Dictionary<string, EbayReportParser.EbayListingSalesMetric> byItemId = metrics
            .Where(metric => !string.IsNullOrWhiteSpace(metric.ExternalItemId))
            .GroupBy(metric => metric.ExternalItemId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        List<string> itemIds = byItemId.Keys.ToList();

        List<PlatformOrderLine> lines = await db.PlatformOrderLines
            .Include(line => line.PlatformOrder)
            .Where(line => line.OrganizationId == currentUser.OrganizationId
                && line.ExternalItemId != null
                && itemIds.Contains(line.ExternalItemId)
                && line.PlatformOrder != null
                && line.PlatformOrder.Platform == "eBay")
            .ToListAsync(cancellationToken);

        int updatedLines = 0;
        int correctedSales = 0;
        HashSet<Guid> touchedOrderIds = [];
        var linesByItemId = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.ExternalItemId))
            .GroupBy(line => line.ExternalItemId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach ((string itemId, List<PlatformOrderLine> matchedLines) in linesByItemId)
        {
            if (!byItemId.TryGetValue(itemId, out EbayReportParser.EbayListingSalesMetric? metric)
                || metric is null)
            {
                continue;
            }

            IReadOnlyList<decimal> platformFees = AllocateAcrossLines(metric.PlatformFees, matchedLines);
            IReadOnlyList<decimal> sellerShippingCosts = AllocateAcrossLines(metric.SellerShippingCost, matchedLines);
            IReadOnlyList<decimal> netSales = AllocateAcrossLines(metric.NetSales, matchedLines);
            IReadOnlyList<decimal> totalSales = AllocateAcrossLines(metric.TotalSales, matchedLines);
            IReadOnlyList<decimal> shippingCharged = AllocateAcrossLines(metric.ShippingCharged, matchedLines);
            IReadOnlyList<decimal> taxCollected = AllocateAcrossLines(metric.TaxCollected, matchedLines);

            for (int index = 0; index < matchedLines.Count; index++)
            {
                PlatformOrderLine line = matchedLines[index];
                line.PlatformFees = platformFees[index];
                line.SellerShippingCost = sellerShippingCosts[index];
                line.NetSales = netSales[index];
                line.TotalSales = totalSales[index];
                line.ShippingCharged = shippingCharged[index];
                line.TaxCollected = taxCollected[index];
                line.FeeBreakdownJson = metric.FeeBreakdownJson;
                line.RawJson = metric.RawJson;
                updatedLines++;

                if (line.PlatformOrderId != Guid.Empty)
                {
                    touchedOrderIds.Add(line.PlatformOrderId);
                }
            }
        }

        foreach (Guid orderId in touchedOrderIds)
        {
            PlatformOrder? order = await db.PlatformOrders
                .Include(item => item.Lines)
                .SingleOrDefaultAsync(item => item.Id == orderId && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (order is null)
            {
                continue;
            }

            order.FeeTotal = order.Lines.Sum(line => line.PlatformFees ?? 0);
            decimal labelTotal = order.Lines.Sum(line => line.SellerShippingCost ?? 0);
            if (order.Lines.Any(line => line.SellerShippingCost.HasValue))
            {
                order.SellerShippingCost = labelTotal;
            }
            if (order.Lines.Any(line => line.NetSales.HasValue))
            {
                order.PayoutAmount = order.Lines.Sum(line => line.NetSales ?? 0);
            }

            if (order.Status == PlatformOrderStatus.Reconciled && order.ReconciledSaleGroupId.HasValue)
            {
                correctedSales += await CorrectReconciledSales(order, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        List<PlatformOrder> refreshed = touchedOrderIds.Count == 0
            ? []
            : await db.PlatformOrders
                .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
                .Where(order => order.OrganizationId == currentUser.OrganizationId && touchedOrderIds.Contains(order.Id))
                .ToListAsync(cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => order.ToDto()).ToList();
        int skipped = metrics.Count - byItemId.Keys.Count + byItemId.Keys.Count(itemId => !linesByItemId.ContainsKey(itemId));
        return new IngestPlatformOrdersResultDto(0, updatedLines + correctedSales, Math.Max(0, skipped), dtos);
    }

    private async Task<int> CorrectReconciledSales(PlatformOrder order, CancellationToken cancellationToken)
    {
        Guid reconciledOrderId = order.ReconciledSaleGroupId!.Value;
        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId && sale.OrderId == reconciledOrderId && !ReleasedInventoryStatuses.Contains(sale.Status))
            .ToListAsync(cancellationToken);

        int correctedSales = 0;
        foreach (Sale sale in sales)
        {
            PlatformOrderLine? line = order.Lines.FirstOrDefault(item => item.InventoryItemId == sale.InventoryItemId);
            if (line is null || (!line.PlatformFees.HasValue && !line.SellerShippingCost.HasValue))
            {
                continue;
            }

            decimal newFees = line.PlatformFees ?? sale.PlatformFees;
            decimal newShippingCost = line.SellerShippingCost ?? sale.ShippingCost;
            decimal newShippingCharged = line.ShippingCharged ?? sale.ShippingCharged;
            decimal newTax = line.TaxCollected ?? sale.SalesTaxCollected;
            if (sale.PlatformFees == newFees
                && sale.ShippingCost == newShippingCost
                && sale.ShippingCharged == newShippingCharged
                && sale.SalesTaxCollected == newTax)
            {
                continue;
            }

            string before = SnapshotSale(sale);
            sale.PlatformFees = newFees;
            sale.ShippingCost = newShippingCost;
            sale.ShippingCharged = newShippingCharged;
            sale.SalesTaxCollected = newTax;
            sale.NetProfit = ResaleFinancialCalculator.CalculateRealizedSale(
                sale.SoldPrice,
                sale.ShippingCharged,
                sale.CostOfGoods,
                sale.PlatformFees,
                sale.PaymentFees,
                sale.ShippingCost,
                sale.SuppliesCost,
                sale.OtherCosts).NetProfit;
            await AddCorrection(sale, before, $"Imported eBay listing-sales fees for item {line.ExternalItemId}.", cancellationToken);
            correctedSales++;
        }

        return correctedSales;
    }

    private static IReadOnlyList<decimal> AllocateAcrossLines(decimal total, IReadOnlyList<PlatformOrderLine> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }
        if (lines.Count == 1)
        {
            return [RoundMoney(total)];
        }

        decimal roundedTotal = RoundMoney(total);
        decimal basisTotal = lines.Sum(line => Math.Max(0, line.UnitPrice * line.Quantity));
        List<decimal> allocations = new(lines.Count);
        decimal allocated = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            decimal value;
            if (index == lines.Count - 1)
            {
                value = roundedTotal - allocated;
            }
            else if (basisTotal > 0)
            {
                value = RoundMoney(roundedTotal * ((lines[index].UnitPrice * lines[index].Quantity) / basisTotal));
            }
            else
            {
                value = RoundMoney(roundedTotal / lines.Count);
            }
            allocations.Add(value);
            allocated += value;
        }
        return allocations;
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string SnapshotSale(Sale sale) => JsonSerializer.Serialize(new
    {
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
        sale.NetProfit,
        sale.SoldAt,
        Status = sale.Status.ToString(),
        sale.Notes,
    });

    private async Task AddCorrection(Sale sale, string before, string reason, CancellationToken cancellationToken)
    {
        int nextVersion = await db.SaleCorrections
            .CountAsync(item => item.SaleId == sale.Id && item.OrganizationId == currentUser.OrganizationId, cancellationToken) + 1;
        db.SaleCorrections.Add(new SaleCorrection
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            CorrectedByUserId = currentUser.UserId,
            Reason = reason,
            BeforeJson = before,
            AfterJson = SnapshotSale(sale),
            Version = nextVersion,
        });
    }
}
