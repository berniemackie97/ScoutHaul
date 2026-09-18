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
    PlatformOrderSuggestionService suggestionService,
    PlatformOrderWorkflowService reconciliation,
    PlatformOrderAutoMatchService autoMatch)
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

        int created = 0, updated = 0, skipped = 0, unchanged = 0;
        List<Guid> touched = [];
        // Orders that stayed reconciled but whose imported figures may have moved; their sales are
        // re-synced after the save so corrected fees land without re-queuing them for a person.
        List<Guid> resyncOrderIds = [];

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
                // An identical re-import changes nothing: no lines rewritten, no re-sync, and it is
                // counted as unchanged. It used to count as updated and rewrite every line, so an
                // import history could not say what an export actually changed.
                string factsBefore = OrderFingerprint(order);
                string linesBefore = LinesFingerprint(order.Lines.Select(line => new LineFacts(
                    line.Title, line.UnitPrice, line.Quantity, line.ExternalItemId, line.Sku, line.ShippingCharged, line.TaxCollected,
                    line.PlatformFees, line.SellerShippingCost, line.NetSales, line.TotalSales, line.FeeBreakdownJson)));
                ApplyImportedFields(order, input, channelAccountId);
                string linesAfter = LinesFingerprint(input.Lines.Select(line => new LineFacts(
                    string.IsNullOrWhiteSpace(line.Title) ? "Untitled item" : line.Title.Trim(), line.UnitPrice,
                    line.Quantity <= 0 ? 1 : line.Quantity, Text.TrimToNull(line.ExternalItemId), Text.TrimToNull(line.Sku),
                    line.ShippingCharged, line.TaxCollected, line.PlatformFees, line.SellerShippingCost, line.NetSales,
                    line.TotalSales, Text.TrimToNull(line.FeeBreakdownJson))));

                if (OrderFingerprint(order) == factsBefore && linesAfter == linesBefore)
                {
                    unchanged++;
                    touched.Add(order.Id);
                    continue;
                }

                bool wasReconciled = order.Status == PlatformOrderStatus.Reconciled;
                ReplaceLines(order, input, preserveExistingMappings: wasReconciled);

                // Re-importing used to demote every reconciled order back into the queue, so a weekly
                // export meant redoing matching already done. Demotion conflated two different needs:
                // "a human must map a line" and "the sale's figures are stale". Only the first is a
                // person's job. A reconciled order returns to the queue solely when the re-import
                // left a line unmapped — one the seller added or changed on the platform. When the
                // mappings all survive it stays settled and is re-synced automatically below, so
                // corrected fees still reach the sale without asking anyone.
                if (wasReconciled)
                {
                    if (order.Lines.Any(line => line.InventoryItemId is null))
                    {
                        order.Status = PlatformOrderStatus.Imported;
                    }
                    else
                    {
                        resyncOrderIds.Add(order.Id);
                    }
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

        // Reconciling with no line maps re-applies the order's current platform figures to the sales
        // its lines already point at — the same correction the operator used to trigger by hand after
        // the import demoted the order. Doing it here keeps the money accurate without the busywork.
        foreach (Guid orderId in resyncOrderIds)
        {
            await reconciliation.ReconcileAsync(
                orderId, new ReconcilePlatformOrderRequest([]), cancellationToken);
        }

        IReadOnlyList<PlatformOrder> imported = await store.ListImportedOrdersForDisplayAsync(
            currentUser.OrganizationId,
            touched,
            cancellationToken);

        int autoReconciled = await autoMatch.PlanAndApplyAsync(imported, cancellationToken);

        // Re-read after auto-reconciling so the caller sees final states, not the pre-settle snapshot.
        IReadOnlyList<PlatformOrder> refreshed = autoReconciled == 0
            ? imported
            : await store.ListImportedOrdersForDisplayAsync(currentUser.OrganizationId, touched, cancellationToken);

        Dictionary<Guid, (Guid Id, string Title, bool AlreadySold)> suggestions =
            await suggestionService.BuildSuggestionsAsync(refreshed, cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => PlatformOrderDtoFactory.ToPlatformOrderDto(order, suggestions)).ToList();
        return (new IngestPlatformOrdersResultDto(created, updated, skipped, autoReconciled, dtos, unchanged), null);
    }

    private sealed record LineFacts(
        string Title,
        decimal UnitPrice,
        int Quantity,
        string? ExternalItemId,
        string? Sku,
        decimal? ShippingCharged,
        decimal? TaxCollected,
        decimal? PlatformFees,
        decimal? SellerShippingCost,
        decimal? NetSales,
        decimal? TotalSales,
        string? FeeBreakdownJson);

    /// <summary>The order-level facts an import sets: everything ApplyImportedFields writes except evidence and timestamps.</summary>
    private static string OrderFingerprint(PlatformOrder order) => ImportFingerprint.Of(
        order.ChannelAccountId, order.BuyerName, order.BuyerReference, order.BuyerEmail, order.BuyerNote,
        order.ShipToName, order.ShipToPhone, order.ShipToLine1, order.ShipToLine2, order.ShipToCity, order.ShipToRegion,
        order.ShipToPostalCode, order.ShipToCountry, order.OrderedAt, order.PaidAt, order.ShippedAt, order.ItemsSubtotal,
        order.ShippingCharged, order.TaxCollected, order.FeeTotal, order.SellerShippingCost, order.PayoutAmount, order.Currency,
        order.TaxRemittedByPlatform, ImportFingerprint.Json(order.FeeBreakdownJson), order.Carrier, order.TrackingNumber);

    /// <summary>Line facts in a stable order, so the same lines compare equal however they were listed.</summary>
    private static string LinesFingerprint(IEnumerable<LineFacts> lines) =>
        string.Join((char)0x1E, lines
            .Select(line => ImportFingerprint.Of(line.Title, line.UnitPrice, line.Quantity, line.ExternalItemId, line.Sku,
                line.ShippingCharged, line.TaxCollected, line.PlatformFees, line.SellerShippingCost, line.NetSales, line.TotalSales,
                ImportFingerprint.Json(line.FeeBreakdownJson)))
            .Order(StringComparer.Ordinal));

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
