using System.Security.Cryptography;
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
/// Posts refund evidence from marketplace CSV exports onto existing reconciled sales. The import is intentionally
/// conservative: only one exact sale match is allowed, ambiguous rows are skipped, and source fingerprints make
/// re-imports idempotent.
/// </summary>
public sealed class MarketplaceRefundImportService(HaulScoutDbContext db, ICurrentUserContext currentUser, TimeProvider timeProvider)
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

            string fingerprint = RefundImportFingerprint(item);
            Refund? alreadyImported = sale.Refunds.FirstOrDefault(refund =>
                refund.Notes?.Contains(fingerprint, StringComparison.OrdinalIgnoreCase) == true);
            if (alreadyImported is not null)
            {
                // Re-imports are how this system converges on the truth, so the dedupe path HEALS instead of just
                // skipping. Two things can be wrong with an already-imported refund: its fee credit was capped when
                // the sale's fees had not been imported yet (credit against zero fees is zero), and an open return
                // it should have settled may still be running. Both are re-derivable from current state.
                bool healed = false;
                string healNote = string.Empty;

                decimal remainingFeeExposure = Math.Max(0,
                    sale.PlatformFees + sale.PaymentFees - sale.Refunds.Sum(refund => refund.FeeCredit));
                decimal missingCredit = Math.Min(item.FeeCredit - alreadyImported.FeeCredit, remainingFeeExposure);
                if (missingCredit > 0)
                {
                    alreadyImported.FeeCredit += RoundMoney(missingCredit);
                    healed = true;
                    healNote = $" Fee credit raised to {alreadyImported.FeeCredit:C} now that the sale's fees are known.";
                }

                Return? stuckReturn = sale.Returns.FirstOrDefault(rma => rma.IsOpen);
                if (stuckReturn is not null)
                {
                    bool restockedNow = stuckReturn.ResolveFromExternalRefund(alreadyImported, timeProvider.GetUtcNow());
                    alreadyImported.ItemReturned = restockedNow;
                    healed = true;
                    healNote += restockedNow
                        ? " Resolved the open return; item restocked."
                        : " Resolved the open return; item not yet received, so nothing was restocked.";
                }

                if (healed)
                {
                    string beforeHeal = SnapshotSale(sale);
                    SaleFinancialStateService.ApplyRefundState(sale);
                    await AddCorrection(sale, beforeHeal, $"Re-imported refund from {item.Source}:{healNote}", cancellationToken);
                    imported++;
                }
                else
                {
                    skipped++;
                }
                continue;
            }

            RefundAllocation allocation = AllocateRefund(sale, item);
            if (allocation.TotalMoved <= 0)
            {
                skipped++;
                warnings.Add($"{item.Source}: skipped \"{item.Title}\" because the matched sale has no remaining refundable amount.");
                continue;
            }

            string before = SnapshotSale(sale);
            string notes = $"{allocation.Notes} Source export has no exact refund timestamp. [import:{fingerprint}]";
            Refund refund = new()
            {
                OrganizationId = currentUser.OrganizationId,
                SaleId = sale.Id,
                Sale = sale,
                Type = allocation.Type,
                ItemAmount = allocation.ItemAmount,
                ShippingRefunded = allocation.ShippingRefunded,
                TaxRefunded = allocation.TaxRefunded,
                FeeCredit = allocation.FeeCredit,
                ItemReturned = false,
                Reason = $"Imported from {item.Source}",
                Notes = notes,
                RefundedAt = timeProvider.GetUtcNow(),
                RecordedByUserId = currentUser.UserId,
            };
            db.Refunds.Add(refund);

            // The marketplace already moved the money. If a return is open for this sale, this refund IS its
            // resolution — leaving it open would strand the workflow at a step reality has already passed. The
            // aggregate decides what "resolved" means at its current stage (restock only if actually received),
            // and the refund is stamped with the outcome BEFORE the state rule derives from it.
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

            SaleFinancialStateService.ApplyRefundState(sale);

            await AddCorrection(sale, before,
                $"Imported refund from {item.Source}: item {allocation.ItemAmount:C}, shipping {allocation.ShippingRefunded:C}, tax {allocation.TaxRefunded:C}, fee credit {allocation.FeeCredit:C}.{returnNote}",
                cancellationToken);
            imported++;
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

    private static RefundAllocation AllocateRefund(Sale sale, MarketplaceRefundReportParser.MarketplaceRefundEvent item)
    {
        decimal remainingItem = Math.Max(0, sale.SoldPrice - sale.Refunds.Sum(refund => refund.ItemAmount));
        decimal remainingShipping = Math.Max(0, sale.ShippingCharged - sale.Refunds.Sum(refund => refund.ShippingRefunded));
        decimal remainingTax = Math.Max(0, sale.SalesTaxCollected - sale.Refunds.Sum(refund => refund.TaxRefunded));
        decimal remainingFeeCredit = Math.Max(0, sale.PlatformFees + sale.PaymentFees - sale.Refunds.Sum(refund => refund.FeeCredit));
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

    private static string RefundImportFingerprint(MarketplaceRefundReportParser.MarketplaceRefundEvent item)
    {
        string raw = string.Join('|',
            item.Platform,
            item.Source,
            item.ExternalItemId,
            item.Sku,
            NormalizeTitle(item.Title),
            item.SoldAt?.ToString("O"),
            item.ItemPrice,
            item.BuyerRefundTotal,
            item.FeeCredit,
            item.FullRefundInferred);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
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
