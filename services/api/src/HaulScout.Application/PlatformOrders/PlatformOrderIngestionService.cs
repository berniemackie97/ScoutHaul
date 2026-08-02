using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Idempotently imports normalized marketplace orders from CSV parsers or API callers.
/// The service owns validation, upsert, and source-line replacement so controllers stay HTTP-only.
/// </summary>
public sealed class PlatformOrderIngestionService(
    IPlatformOrderImportStore store,
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
        List<PlatformOrder> existing = (await store.ListImportedOrdersByExternalIdsAsync(
            currentUser.OrganizationId,
            keys,
            cancellationToken)).ToList();

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
                store.AddPlatformOrder(order);
                existing.Add(order);
                ApplyImportedFields(order, input, channelAccountId);
                ReplaceLines(order, input, preserveExistingMappings: false);
                created++;
            }
            else if (order.Status != PlatformOrderStatus.Ignored)
            {
                bool wasReconciled = order.Status == PlatformOrderStatus.Reconciled;
                ApplyImportedFields(order, input, channelAccountId);
                ReplaceLines(order, input, preserveExistingMappings: wasReconciled);

                // Re-importing the same report used to demote every reconciled order straight back
                // into the queue, so a weekly export meant re-reconciling work already done. A
                // reconciled order only needs a human again when the re-import actually left a line
                // without a mapping — a line the seller added or changed on the platform. Otherwise
                // the mappings still hold and the refreshed figures are corrected in place, which is
                // exactly what the reconcile screen promises.
                if (wasReconciled && order.Lines.Any(line => line.InventoryItemId is null))
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

        await store.SaveChangesAsync(cancellationToken);

        IReadOnlyList<PlatformOrder> refreshed = await store.ListImportedOrdersForDisplayAsync(
            currentUser.OrganizationId,
            touched,
            cancellationToken);
        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions =
            await suggestionService.BuildSuggestionsAsync(refreshed, cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => PlatformOrderDtoFactory.ToPlatformOrderDto(order, suggestions)).ToList();
        return (new IngestPlatformOrdersResultDto(created, updated, skipped, dtos), null);
    }

    private static void ApplyImportedFields(PlatformOrder order, IngestPlatformOrderInput input, Guid? channelAccountId)
    {
        if (channelAccountId.HasValue)
        {
            order.ChannelAccountId = channelAccountId;
        }
        order.BuyerName = Text.TrimToNull(input.BuyerName);
        order.BuyerReference = Text.TrimToNull(input.BuyerReference);
        order.BuyerEmail = Text.TrimToNull(input.BuyerEmail);
        order.BuyerNote = Text.TrimToNull(input.BuyerNote);
        order.ShipToName = Text.TrimToNull(input.ShipToName);
        order.ShipToPhone = Text.TrimToNull(input.ShipToPhone);
        order.ShipToLine1 = Text.TrimToNull(input.ShipToLine1);
        order.ShipToLine2 = Text.TrimToNull(input.ShipToLine2);
        order.ShipToCity = Text.TrimToNull(input.ShipToCity);
        order.ShipToRegion = Text.TrimToNull(input.ShipToRegion);
        order.ShipToPostalCode = Text.TrimToNull(input.ShipToPostalCode);
        order.ShipToCountry = Text.TrimToNull(input.ShipToCountry);
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
        order.FeeBreakdownJson = Text.TrimToNull(input.FeeBreakdownJson);
        order.Carrier = Text.TrimToNull(input.Carrier);
        order.TrackingNumber = Text.TrimToNull(input.TrackingNumber);
        order.RawJson = Text.TrimToNull(input.RawJson);
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
        List<PlatformOrderLine> replacementLines = [];

        foreach (IngestPlatformOrderLineInput line in input.Lines)
        {
            string? externalItemId = Text.TrimToNull(line.ExternalItemId);
            replacementLines.Add(new PlatformOrderLine
            {
                OrganizationId = currentUser.OrganizationId,
                PlatformOrderId = order.Id,
                Title = string.IsNullOrWhiteSpace(line.Title) ? "Untitled item" : line.Title.Trim(),
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity <= 0 ? 1 : line.Quantity,
                ExternalItemId = externalItemId,
                Sku = Text.TrimToNull(line.Sku),
                ShippingCharged = line.ShippingCharged,
                TaxCollected = line.TaxCollected,
                PlatformFees = line.PlatformFees,
                SellerShippingCost = line.SellerShippingCost,
                NetSales = line.NetSales,
                TotalSales = line.TotalSales,
                FeeBreakdownJson = Text.TrimToNull(line.FeeBreakdownJson),
                RawJson = Text.TrimToNull(line.RawJson),
                InventoryItemId = externalItemId is not null && mappingsByExternalId.TryGetValue(externalItemId, out Guid? mapped) ? mapped : null,
            });
        }

        store.ReplacePlatformOrderLines(order, replacementLines);
    }

    private async Task<List<string>> ExistingChannels(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> saleChannels = await store.ListKnownSalesChannelsAsync(currentUser.OrganizationId, cancellationToken);
        IReadOnlyList<string> listingChannels = await store.ListKnownListingChannelsAsync(currentUser.OrganizationId, cancellationToken);
        return saleChannels.Concat(listingChannels).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }
}
