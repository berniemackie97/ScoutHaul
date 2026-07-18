using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Projects refund facts from marketplace CSV exports onto reconciled sales. Each refund is identified by a stable
/// natural key derived from the sale it settles (<see cref="ExternalRefundKey"/>), so importing is an idempotent
/// upsert: the first import creates the refund, every re-import refreshes its amounts to the latest facts. This is
/// what makes the pipeline converge — a fee credit capped before fees were known, or a return still open, both
/// correct themselves on the next import — without any "skip if already seen / heal if changed" branching. Matching
/// stays conservative: exactly one sale match, or the row is skipped.
/// </summary>
public sealed class MarketplaceRefundImportService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider,
    OrderAssembler orderAssembler)
{
    public async Task<(ImportMarketplaceRefundsResultDto? Result, string? Error)> ImportCsvAsync(
        ImportMarketplaceRefundsCsvRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return (null, "Paste or upload a marketplace refund CSV.");
        }

        MarketplaceRefundReportParser.ParseResult parsed = MarketplaceRefundReportParser.Parse(request.Platform, request.Csv);
        if (parsed.Kind == MarketplaceRefundReportParser.MarketplaceRefundReportKind.Unknown || parsed.Events.Count == 0)
        {
            return (null, parsed.Warning ?? "No refund rows were found.");
        }

        int imported = 0;
        int skipped = parsed.RowsSkipped;
        List<string> warnings = [];
        HashSet<Guid> touchedOrderIds = [];
        foreach (MarketplaceRefundReportParser.MarketplaceRefundEvent item in parsed.Events)
        {
            List<Sale> matches = await FindRefundMatches(item, cancellationToken);
            if (matches.Count != 1)
            {
                skipped++;
                warnings.Add($"{item.Source}: skipped \"{item.Title}\" because it matched {matches.Count} sale(s).");
                continue;
            }

            Sale sale = matches.Single();
            if (sale.Status == SaleStatus.Cancelled)
            {
                skipped++;
                warnings.Add($"{item.Source}: skipped \"{item.Title}\" because the matched sale is cancelled.");
                continue;
            }

            // This refund's stable identity in the source. Re-importing the same report resolves to the same sale
            // and therefore the same key, which is what makes the whole thing an idempotent upsert instead of a
            // pile of "skip if seen / heal if changed" special cases.
            string refundKey = ExternalRefundKey(item.Source, sale);
            Refund? existing = sale.Refunds.FirstOrDefault(refund => refund.ExternalRefundKey == refundKey);
            if (existing is null)
            {
                // One-time legacy adoption. Refunds imported before this key existed identified themselves with a
                // fingerprint smuggled into Notes; on their first re-import we claim the matching one from the same
                // source (nearest amount when several exist) and stamp the real key — so it is upgraded in place
                // rather than duplicated. After this runs once, the keyed path above handles it forever.
                existing = sale.Refunds
                    .Where(refund => refund.ExternalRefundKey == null && refund.Reason == $"Imported from {item.Source}")
                    .OrderBy(refund => Math.Abs(refund.BuyerRefundTotal - item.BuyerRefundTotal))
                    .FirstOrDefault();
                if (existing is not null)
                {
                    existing.ExternalRefundKey = refundKey;
                }
            }

            // Amounts are re-derived every import against the sale's OTHER refunds — never against this one — so a
            // fee credit that was capped at zero because the sale's fees had not been imported yet simply recomputes
            // to the truth once they have. Convergence is a property of recomputing from current facts, not a patch.
            IReadOnlyList<Refund> baseline = sale.Refunds.Where(refund => refund != existing).ToList();
            RefundAllocation allocation = AllocateRefund(sale, baseline, item);
            if (existing is null && allocation.TotalMoved <= 0)
            {
                skipped++;
                warnings.Add($"{item.Source}: skipped \"{item.Title}\" because the matched sale has no remaining refundable amount.");
                continue;
            }

            string before = SnapshotSale(sale);
            Refund refund = existing ?? new Refund
            {
                OrganizationId = currentUser.OrganizationId,
                SaleId = sale.Id,
                Sale = sale,
                ExternalRefundKey = refundKey,
                Reason = $"Imported from {item.Source}",
                RefundedAt = timeProvider.GetUtcNow(),
                RecordedByUserId = currentUser.UserId,
            };
            refund.Type = allocation.Type;
            refund.ItemAmount = allocation.ItemAmount;
            refund.ShippingRefunded = allocation.ShippingRefunded;
            refund.TaxRefunded = allocation.TaxRefunded;
            refund.FeeCredit = allocation.FeeCredit;
            refund.Notes = $"{allocation.Notes} Source export has no exact refund timestamp.";
            if (existing is null)
            {
                sale.Refunds.Add(refund);
                db.Refunds.Add(refund);
            }

            // The marketplace already moved the money. If a return is open for this sale, this refund IS its
            // resolution — the aggregate decides what "resolved" means at its current stage (restock only if the
            // item was actually received). Idempotent: a closed return is left untouched on re-import.
            Return? openReturn = sale.Returns.FirstOrDefault(rma => rma.IsOpen);
            string returnNote = string.Empty;
            if (openReturn is not null)
            {
                bool restocked = openReturn.ResolveFromExternalRefund(refund, timeProvider.GetUtcNow());
                refund.ItemReturned = restocked;
                returnNote = restocked
                    ? " Resolved the open return; item restocked."
                    : " Resolved the open return; item not yet received, so nothing was restocked.";
            }

            // The one rule derives net + status from the whole refund set — always, whether this was a create or a
            // refresh, so the recorded truth cannot drift from the facts.
            SaleFinancialStateService.ApplyRefundState(sale);
            if (sale.OrderId is Guid orderId)
            {
                touchedOrderIds.Add(orderId);
            }

            string verb = existing is null ? "Imported" : "Re-imported";
            await AddCorrection(sale, before,
                $"{verb} refund from {item.Source}: item {allocation.ItemAmount:C}, shipping {allocation.ShippingRefunded:C}, tax {allocation.TaxRefunded:C}, fee credit {allocation.FeeCredit:C}.{returnNote}",
                cancellationToken);
            imported++;
        }

        foreach (Guid orderId in touchedOrderIds)
        {
            Order? order = await db.Orders.SingleOrDefaultAsync(
                item => item.Id == orderId && item.OrganizationId == currentUser.OrganizationId,
                cancellationToken);
            if (order is not null)
            {
                await orderAssembler.SyncSalesOrderAsync(order, [], cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return (new ImportMarketplaceRefundsResultDto(imported, skipped, warnings), null);
    }

    private async Task<List<Sale>> FindRefundMatches(
        MarketplaceRefundReportParser.MarketplaceRefundEvent item,
        CancellationToken cancellationToken)
    {
        if (string.Equals(item.Platform, "eBay", StringComparison.OrdinalIgnoreCase))
        {
            return await FindEbayRefundMatches(item, cancellationToken);
        }
        if (string.Equals(item.Platform, "Depop", StringComparison.OrdinalIgnoreCase))
        {
            return await FindDepopRefundMatches(item, cancellationToken);
        }
        return [];
    }

    private async Task<List<Sale>> FindEbayRefundMatches(
        MarketplaceRefundReportParser.MarketplaceRefundEvent item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ExternalItemId))
        {
            return [];
        }

        List<PlatformOrderLine> lines = await db.PlatformOrderLines
            .Include(line => line.PlatformOrder)
            .Where(line => line.OrganizationId == currentUser.OrganizationId
                && line.ExternalItemId == item.ExternalItemId
                && line.InventoryItemId.HasValue
                && line.PlatformOrder != null
                && line.PlatformOrder.Platform == "eBay"
                && line.PlatformOrder.Status == PlatformOrderStatus.Reconciled
                && line.PlatformOrder.ReconciledSaleGroupId.HasValue)
            .ToListAsync(cancellationToken);

        HashSet<(Guid OrderId, Guid InventoryItemId)> keys = lines
            .Where(line => line.PlatformOrder?.ReconciledSaleGroupId is not null && line.InventoryItemId.HasValue)
            .Select(line => (line.PlatformOrder!.ReconciledSaleGroupId!.Value, line.InventoryItemId!.Value))
            .ToHashSet();
        if (keys.Count == 0)
        {
            return [];
        }

        List<Guid> orderIds = keys.Select(key => key.OrderId).Distinct().ToList();
        List<Guid> inventoryIds = keys.Select(key => key.InventoryItemId).Distinct().ToList();
        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
                .ThenInclude(inv => inv!.BundleComponents)
                    .ThenInclude(component => component.ComponentInventoryItem)
            .Include(sale => sale.Refunds)
            .Include(sale => sale.Returns)
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId
                && sale.OrderId.HasValue
                && orderIds.Contains(sale.OrderId.Value)
                && inventoryIds.Contains(sale.InventoryItemId))
            .ToListAsync(cancellationToken);

        return sales
            .Where(sale => sale.OrderId.HasValue && keys.Contains((sale.OrderId.Value, sale.InventoryItemId)))
            .ToList();
    }

    private async Task<List<Sale>> FindDepopRefundMatches(
        MarketplaceRefundReportParser.MarketplaceRefundEvent item,
        CancellationToken cancellationToken)
    {
        IQueryable<Sale> query = db.Sales
            .Include(sale => sale.InventoryItem)
                .ThenInclude(inv => inv!.BundleComponents)
                    .ThenInclude(component => component.ComponentInventoryItem)
            .Include(sale => sale.Refunds)
            .Include(sale => sale.Returns)
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId && sale.Platform == "Depop");

        if (item.SoldAt.HasValue)
        {
            DateTimeOffset start = new(item.SoldAt.Value.Year, item.SoldAt.Value.Month, item.SoldAt.Value.Day, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset end = start.AddDays(1);
            query = query.Where(sale => sale.SoldAt >= start && sale.SoldAt < end);
        }
        if (item.ItemPrice > 0)
        {
            decimal low = item.ItemPrice - 0.01m;
            decimal high = item.ItemPrice + 0.01m;
            query = query.Where(sale => sale.SoldPrice >= low && sale.SoldPrice <= high);
        }

        List<Sale> candidates = await query.Take(50).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(item.Sku))
        {
            candidates = candidates
                .Where(sale => string.Equals(sale.InventoryItem?.Barcode, item.Sku, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return candidates
            .Where(sale => TitleMatchesRefundEvent(item.Title, sale.InventoryItem?.Title))
            .ToList();
    }

    private sealed record RefundAllocation(
        RefundType Type,
        decimal ItemAmount,
        decimal ShippingRefunded,
        decimal TaxRefunded,
        decimal FeeCredit,
        string Notes)
    {
        public decimal TotalMoved => ItemAmount + ShippingRefunded + TaxRefunded + FeeCredit;
    }

    /// <summary>
    /// The natural key of a refund in its source. For these per-item marketplace reports there is one refund figure
    /// per sold line, so the sale being refunded IS the refund's identity — re-importing matches the same sale and
    /// so produces the same key. The order + item coordinates are used when present (stable and human-legible in the
    /// key); the sale id is the fallback for channels whose refunds carry no order.
    /// </summary>
    private static string ExternalRefundKey(string source, Sale sale) =>
        sale.OrderId is Guid orderId
            ? $"{source}:{orderId}:{sale.InventoryItemId}".ToLowerInvariant()
            : $"{source}:sale:{sale.Id}".ToLowerInvariant();

    /// <param name="baseline">The sale's OTHER refunds — every refund except the one being computed — so a refresh
    /// recomputes cleanly against the rest rather than double-counting itself.</param>
    private static RefundAllocation AllocateRefund(Sale sale, IReadOnlyList<Refund> baseline, MarketplaceRefundReportParser.MarketplaceRefundEvent item)
    {
        decimal remainingItem = Math.Max(0, sale.SoldPrice - baseline.Sum(refund => refund.ItemAmount));
        decimal remainingShipping = Math.Max(0, sale.ShippingCharged - baseline.Sum(refund => refund.ShippingRefunded));
        decimal remainingTax = Math.Max(0, sale.SalesTaxCollected - baseline.Sum(refund => refund.TaxRefunded));
        decimal remainingFeeCredit = Math.Max(0, sale.PlatformFees + sale.PaymentFees - baseline.Sum(refund => refund.FeeCredit));
        decimal feeCredit = Math.Min(item.FeeCredit, remainingFeeCredit);

        if (item.FullRefundInferred)
        {
            return new RefundAllocation(
                RefundType.Full,
                RoundMoney(remainingItem),
                RoundMoney(remainingShipping),
                RoundMoney(remainingTax),
                RoundMoney(feeCredit),
                "Full refund inferred from a zero-sales marketplace fee-credit row.");
        }

        decimal buyerRefundRemaining = item.BuyerRefundTotal;
        decimal itemAmount = Math.Min(remainingItem, buyerRefundRemaining);
        buyerRefundRemaining -= itemAmount;
        decimal shippingRefunded = Math.Min(remainingShipping, Math.Max(0, buyerRefundRemaining));
        buyerRefundRemaining -= shippingRefunded;
        decimal taxRefunded = Math.Min(remainingTax, Math.Max(0, buyerRefundRemaining));
        bool isFull = remainingItem > 0 && itemAmount >= remainingItem - 0.01m;

        return new RefundAllocation(
            isFull ? RefundType.Full : RefundType.Partial,
            RoundMoney(itemAmount),
            RoundMoney(shippingRefunded),
            RoundMoney(taxRefunded),
            RoundMoney(feeCredit),
            buyerRefundRemaining > taxRefunded
                ? $"Buyer refund total was {item.BuyerRefundTotal:C}; marketplace-only buyer fees above seller revenue were ignored."
                : $"Buyer refund total was {item.BuyerRefundTotal:C}.");
    }


    private static bool TitleMatchesRefundEvent(string eventTitle, string? saleTitle)
    {
        string normalizedEvent = NormalizeTitle(eventTitle);
        string normalizedSale = NormalizeTitle(saleTitle);
        return normalizedEvent.Length > 0
            && normalizedSale.Length > 0
            && (normalizedEvent == normalizedSale
                || normalizedEvent.Contains(normalizedSale, StringComparison.Ordinal)
                || normalizedSale.Contains(normalizedEvent, StringComparison.Ordinal));
    }

    private static string NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }


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

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
