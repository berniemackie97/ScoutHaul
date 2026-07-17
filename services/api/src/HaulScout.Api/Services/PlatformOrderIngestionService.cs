using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Idempotently imports normalized marketplace orders from CSV parsers or API callers.
/// The service owns validation, upsert, and source-line replacement so controllers stay HTTP-only.
/// </summary>
public sealed class PlatformOrderIngestionService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    PlatformOrderSuggestionService suggestionService)
{
    public async Task<(IngestPlatformOrdersResultDto? Result, string? Error)> IngestAsync(
        string? defaultPlatform,
        IReadOnlyList<IngestPlatformOrderInput> orders,
        Guid? channelAccountId,
        CancellationToken cancellationToken)
    {
        List<string> existingChannels = await ExistingChannels(cancellationToken);
        string? defaultChannel = SalesChannelCatalog.CanonicalValue(defaultPlatform, existingChannels);

        List<(IngestPlatformOrderInput Input, string Channel, string ExternalOrderId)> normalized = [];
        foreach (IngestPlatformOrderInput input in orders)
        {
            string externalOrderId = (input.ExternalOrderId ?? string.Empty).Trim();
            if (externalOrderId.Length == 0)
            {
                return (null, "Every order needs an external order id.");
            }

            string? channel = SalesChannelCatalog.CanonicalValue(input.Platform, existingChannels) ?? defaultChannel;
            if (channel is null)
            {
                return (null, $"Order {externalOrderId} has no recognized sales channel.");
            }

            if (input.Lines is null || input.Lines.Count == 0)
            {
                return (null, $"Order {externalOrderId} has no line items.");
            }

            if (new[] { input.ItemsSubtotal, input.ShippingCharged, input.TaxCollected, input.FeeTotal }.Any(value => value < 0)
                || (input.ShippingCost.HasValue && input.ShippingCost.Value < 0)
                || (input.PayoutAmount.HasValue && input.PayoutAmount.Value < 0)
                || input.Lines.Any(line => line.UnitPrice < 0 || line.Quantity <= 0))
            {
                return (null, $"Order {externalOrderId} has negative money or a non-positive quantity.");
            }

            normalized.Add((input, channel, externalOrderId));
        }

        HashSet<string> keys = normalized.Select(entry => entry.ExternalOrderId).ToHashSet();
        List<PlatformOrder> existing = await db.PlatformOrders
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == currentUser.OrganizationId && keys.Contains(order.ExternalOrderId))
            .ToListAsync(cancellationToken);

        int created = 0, updated = 0, skipped = 0;
        List<Guid> touched = [];

        foreach ((IngestPlatformOrderInput input, string channel, string externalOrderId) in normalized)
        {
            PlatformOrder? order = existing.SingleOrDefault(candidate =>
                candidate.Platform == channel && candidate.ExternalOrderId == externalOrderId);

            if (order is null)
            {
                order = new PlatformOrder
                {
                    OrganizationId = currentUser.OrganizationId,
                    Platform = channel,
                    ExternalOrderId = externalOrderId,
                    ChannelAccountId = channelAccountId,
                };
                db.PlatformOrders.Add(order);
                existing.Add(order);
                ApplyImportedFields(order, input, channelAccountId);
                ReplaceLines(order, input, preserveExistingMappings: false);
                created++;
            }
            else if (order.Status != PlatformOrderStatus.Ignored)
            {
                ApplyImportedFields(order, input, channelAccountId);
                ReplaceLines(order, input, preserveExistingMappings: order.Status == PlatformOrderStatus.Reconciled);
                if (order.Status == PlatformOrderStatus.Reconciled)
                {
                    order.Status = PlatformOrderStatus.Imported;
                }
                updated++;
            }
            else
            {
                skipped++;
                continue;
            }

            touched.Add(order.Id);
        }

        await db.SaveChangesAsync(cancellationToken);

        List<PlatformOrder> refreshed = await db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == currentUser.OrganizationId && touched.Contains(order.Id))
            .ToListAsync(cancellationToken);
        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions =
            await suggestionService.BuildSuggestionsAsync(refreshed, cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => order.ToDto(suggestions)).ToList();
        return (new IngestPlatformOrdersResultDto(created, updated, skipped, dtos), null);
    }

    private static void ApplyImportedFields(PlatformOrder order, IngestPlatformOrderInput input, Guid? channelAccountId)
    {
        if (channelAccountId.HasValue)
        {
            order.ChannelAccountId = channelAccountId;
        }
        order.BuyerName = TrimToNull(input.BuyerName);
        order.BuyerReference = TrimToNull(input.BuyerReference);
        order.BuyerEmail = TrimToNull(input.BuyerEmail);
        order.BuyerNote = TrimToNull(input.BuyerNote);
        order.ShipToName = TrimToNull(input.ShipToName);
        order.ShipToPhone = TrimToNull(input.ShipToPhone);
        order.ShipToLine1 = TrimToNull(input.ShipToLine1);
        order.ShipToLine2 = TrimToNull(input.ShipToLine2);
        order.ShipToCity = TrimToNull(input.ShipToCity);
        order.ShipToRegion = TrimToNull(input.ShipToRegion);
        order.ShipToPostalCode = TrimToNull(input.ShipToPostalCode);
        order.ShipToCountry = TrimToNull(input.ShipToCountry);
        order.OrderedAt = input.OrderedAt;
        order.PaidAt = input.PaidAt;
        order.ShippedAt = input.ShippedAt;
        order.ItemsSubtotal = input.ItemsSubtotal;
        order.ShippingCharged = input.ShippingCharged;
        order.TaxCollected = input.TaxCollected;
        order.FeeTotal = input.FeeTotal;
        order.SellerShippingCost = input.ShippingCost;
        order.PayoutAmount = input.PayoutAmount;
        order.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpperInvariant();
        order.TaxRemittedByPlatform = input.TaxRemittedByPlatform;
        order.FeeBreakdownJson = TrimToNull(input.FeeBreakdownJson);
        order.Carrier = TrimToNull(input.Carrier);
        order.TrackingNumber = TrimToNull(input.TrackingNumber);
        order.RawJson = TrimToNull(input.RawJson);
        order.ScrapedAt = DateTimeOffset.UtcNow;
    }

    private void ReplaceLines(PlatformOrder order, IngestPlatformOrderInput input, bool preserveExistingMappings)
    {
        Dictionary<string, Guid?> mappingsByExternalId = preserveExistingMappings
            ? order.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.ExternalItemId) && line.InventoryItemId.HasValue)
                .GroupBy(line => line.ExternalItemId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().InventoryItemId, StringComparer.OrdinalIgnoreCase)
            : [];
        db.PlatformOrderLines.RemoveRange(order.Lines);

        foreach (IngestPlatformOrderLineInput line in input.Lines)
        {
            string? externalItemId = TrimToNull(line.ExternalItemId);
            db.PlatformOrderLines.Add(new PlatformOrderLine
            {
                OrganizationId = currentUser.OrganizationId,
                PlatformOrderId = order.Id,
                Title = string.IsNullOrWhiteSpace(line.Title) ? "Untitled item" : line.Title.Trim(),
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                ExternalItemId = externalItemId,
                Sku = TrimToNull(line.Sku),
                ShippingCharged = line.ShippingCharged,
                TaxCollected = line.TaxCollected,
                PlatformFees = line.PlatformFees,
                SellerShippingCost = line.SellerShippingCost,
                NetSales = line.NetSales,
                TotalSales = line.TotalSales,
                FeeBreakdownJson = TrimToNull(line.FeeBreakdownJson),
                RawJson = TrimToNull(line.RawJson),
                InventoryItemId = externalItemId is not null && mappingsByExternalId.TryGetValue(externalItemId, out Guid? mapped) ? mapped : null,
            });
        }
    }

    private async Task<List<string>> ExistingChannels(CancellationToken cancellationToken)
    {
        List<string> saleChannels = await db.Sales
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);
        List<string> listingChannels = await db.Listings
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);
        return saleChannels.Concat(listingChannels).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }
}
