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

            if (new[] { input.ItemsSubtotal, input.ShippingCharged, input.TaxCollected, input.FeeTotal ?? 0 }.Any(value => value < 0)
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
                ApplyImportedFields(order, input, channelAccountId, isNew: true);
                ReplaceLines(order, MergeLines(input.Lines, []), preserveExistingMappings: false);
                created++;
            }
            else if (order.Status != PlatformOrderStatus.Ignored)
            {
                // An identical re-import changes nothing: no lines rewritten, no re-sync, and it is
                // counted as unchanged. It used to count as updated and rewrite every line, so an
                // import history could not say what an export actually changed.
                string factsBefore = OrderFingerprint(order);
                string linesBefore = LinesFingerprint(order.Lines.Select(Facts));
                List<LineFacts> merged = MergeLines(input.Lines, order.Lines);
                ApplyImportedFields(order, input, channelAccountId, isNew: false);

                if (OrderFingerprint(order) == factsBefore && LinesFingerprint(merged) == linesBefore)
                {
                    unchanged++;
                    touched.Add(order.Id);
                    continue;
                }

                bool wasReconciled = order.Status == PlatformOrderStatus.Reconciled;
                ReplaceLines(order, merged, preserveExistingMappings: wasReconciled);

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

        Dictionary<Guid, PlatformLineSuggestion> suggestions =
            await suggestionService.BuildSuggestionsAsync(refreshed, cancellationToken);
        List<PlatformOrderDto> dtos = refreshed.Select(order => PlatformOrderDtoFactory.ToPlatformOrderDto(order, suggestions)).ToList();
        return (new IngestPlatformOrdersResultDto(created, updated, skipped, autoReconciled, dtos, unchanged), null);
    }

    /// <summary>A line as it will be written. <see cref="RawJson"/> is evidence, so it never counts as a change.</summary>
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
        string? FeeBreakdownJson,
        string? RawJson);

    private static LineFacts Facts(PlatformOrderLine line) => new(
        line.Title, line.UnitPrice, line.Quantity, line.ExternalItemId, line.Sku, line.ShippingCharged, line.TaxCollected,
        line.PlatformFees, line.SellerShippingCost, line.NetSales, line.TotalSales, line.FeeBreakdownJson, line.RawJson);

    /// <summary>
    /// The incoming lines, with every figure the source left out taken from the line already recorded
    /// for the same listing. The All Orders export has no fees; the fee report and a scan do. Without
    /// this, re-importing the export after either wiped the fees — and re-synced the wiped figures onto
    /// the settled sales.
    /// </summary>
    private static List<LineFacts> MergeLines(IEnumerable<IngestPlatformOrderLineInput> incoming, IEnumerable<PlatformOrderLine> recorded)
    {
        Dictionary<string, PlatformOrderLine> recordedByItem = recorded
            .Where(line => !string.IsNullOrWhiteSpace(line.ExternalItemId))
            .GroupBy(line => line.ExternalItemId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return incoming.Select(line =>
        {
            string? externalItemId = Text.TrimToNull(line.ExternalItemId);
            PlatformOrderLine? was = externalItemId is not null ? recordedByItem.GetValueOrDefault(externalItemId) : null;
            return new LineFacts(
                string.IsNullOrWhiteSpace(line.Title) ? "Untitled item" : line.Title.Trim(),
                line.UnitPrice,
                line.Quantity <= 0 ? 1 : line.Quantity,
                externalItemId,
                Text.TrimToNull(line.Sku) ?? was?.Sku,
                line.ShippingCharged ?? was?.ShippingCharged,
                line.TaxCollected ?? was?.TaxCollected,
                line.PlatformFees ?? was?.PlatformFees,
                line.SellerShippingCost ?? was?.SellerShippingCost,
                line.NetSales ?? was?.NetSales,
                line.TotalSales ?? was?.TotalSales,
                Text.TrimToNull(line.FeeBreakdownJson) ?? was?.FeeBreakdownJson,
                Text.TrimToNull(line.RawJson) ?? was?.RawJson);
        }).ToList();
    }

    /// <summary>The order-level facts an import sets: everything ApplyImportedFields writes except evidence and timestamps.</summary>
    private static string OrderFingerprint(PlatformOrder order) => ImportFingerprint.Of(
        order.ChannelAccountId, order.BuyerName, order.BuyerReference, order.BuyerEmail, order.BuyerNote,
        order.ShipToName, order.ShipToPhone, order.ShipToLine1, order.ShipToLine2, order.ShipToCity, order.ShipToRegion,
        order.ShipToPostalCode, order.ShipToCountry, order.OrderedAt, order.PaidAt, order.ShippedAt, order.ItemsSubtotal,
        order.ShippingCharged, order.TaxCollected, order.FeeTotal, order.FeesPending, order.SellerShippingCost, order.PayoutAmount,
        order.Currency, order.TaxRemittedByPlatform, ImportFingerprint.Json(order.FeeBreakdownJson), order.Carrier, order.TrackingNumber);

    /// <summary>Line facts in a stable order, so the same lines compare equal however they were listed.</summary>
    private static string LinesFingerprint(IEnumerable<LineFacts> lines) =>
        string.Join((char)0x1E, lines
            .Select(line => ImportFingerprint.Of(line.Title, line.UnitPrice, line.Quantity, line.ExternalItemId, line.Sku,
                line.ShippingCharged, line.TaxCollected, line.PlatformFees, line.SellerShippingCost, line.NetSales, line.TotalSales,
                ImportFingerprint.Json(line.FeeBreakdownJson)))
            .Order(StringComparer.Ordinal));

    /// <summary>
    /// Writes what this source says about the order. The figures every source carries — when, items,
    /// shipping, tax — are replaced; anything the source leaves out keeps what another source recorded.
    /// </summary>
    private static void ApplyImportedFields(PlatformOrder order, IngestPlatformOrderInput input, Guid? channelAccountId, bool isNew)
    {
        if (channelAccountId.HasValue)
        {
            order.ChannelAccountId = channelAccountId;
        }
        order.BuyerName = Text.TrimToNull(input.BuyerName) ?? order.BuyerName;
        order.BuyerReference = Text.TrimToNull(input.BuyerReference) ?? order.BuyerReference;
        order.BuyerEmail = Text.TrimToNull(input.BuyerEmail) ?? order.BuyerEmail;
        order.BuyerNote = Text.TrimToNull(input.BuyerNote) ?? order.BuyerNote;
        order.ShipToName = Text.TrimToNull(input.ShipToName) ?? order.ShipToName;
        order.ShipToPhone = Text.TrimToNull(input.ShipToPhone) ?? order.ShipToPhone;
        order.ShipToLine1 = Text.TrimToNull(input.ShipToLine1) ?? order.ShipToLine1;
        order.ShipToLine2 = Text.TrimToNull(input.ShipToLine2) ?? order.ShipToLine2;
        order.ShipToCity = Text.TrimToNull(input.ShipToCity) ?? order.ShipToCity;
        order.ShipToRegion = Text.TrimToNull(input.ShipToRegion) ?? order.ShipToRegion;
        order.ShipToPostalCode = Text.TrimToNull(input.ShipToPostalCode) ?? order.ShipToPostalCode;
        order.ShipToCountry = Text.TrimToNull(input.ShipToCountry) ?? order.ShipToCountry;
        order.OrderedAt = input.OrderedAt;
        order.PaidAt = input.PaidAt ?? order.PaidAt;
        order.ShippedAt = input.ShippedAt ?? order.ShippedAt;
        order.ItemsSubtotal = input.ItemsSubtotal;
        order.ShippingCharged = input.ShippingCharged;
        order.TaxCollected = input.TaxCollected;

        if (input.FeeTotal is decimal fees)
        {
            order.FeeTotal = fees;
            order.FeesPending = false;
        }
        else if (isNew)
        {
            order.FeeTotal = 0;
            order.FeesPending = input.FeesPending;
        }

        order.SellerShippingCost = input.ShippingCost ?? order.SellerShippingCost;
        order.PayoutAmount = input.PayoutAmount ?? order.PayoutAmount;
        order.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "USD" : input.Currency.Trim().ToUpperInvariant();
        order.TaxRemittedByPlatform = input.TaxRemittedByPlatform;
        order.FeeBreakdownJson = Text.TrimToNull(input.FeeBreakdownJson) ?? order.FeeBreakdownJson;
        order.Carrier = Text.TrimToNull(input.Carrier) ?? order.Carrier;
        order.TrackingNumber = Text.TrimToNull(input.TrackingNumber) ?? order.TrackingNumber;
        order.RawJson = Text.TrimToNull(input.RawJson) ?? order.RawJson;
        order.ScrapedAt = DateTimeOffset.UtcNow;
    }

    private void ReplaceLines(PlatformOrder order, IReadOnlyList<LineFacts> lines, bool preserveExistingMappings)
    {
        Dictionary<string, Guid?> mappingsByExternalId = preserveExistingMappings
            ? order.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.ExternalItemId) && line.InventoryItemId.HasValue)
                .GroupBy(line => line.ExternalItemId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().InventoryItemId, StringComparer.OrdinalIgnoreCase)
            : [];
        List<PlatformOrderLine> replacementLines = [];

        foreach (LineFacts line in lines)
        {
            replacementLines.Add(new PlatformOrderLine
            {
                OrganizationId = currentUser.OrganizationId,
                PlatformOrderId = order.Id,
                Title = line.Title,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
                ExternalItemId = line.ExternalItemId,
                Sku = line.Sku,
                ShippingCharged = line.ShippingCharged,
                TaxCollected = line.TaxCollected,
                PlatformFees = line.PlatformFees,
                SellerShippingCost = line.SellerShippingCost,
                NetSales = line.NetSales,
                TotalSales = line.TotalSales,
                FeeBreakdownJson = line.FeeBreakdownJson,
                RawJson = line.RawJson,
                InventoryItemId = line.ExternalItemId is not null && mappingsByExternalId.TryGetValue(line.ExternalItemId, out Guid? mapped) ? mapped : null,
            });
        }

        store.ReplacePlatformOrderLines(order, replacementLines);
    }

    /// <summary>
    /// Orders a marketplace shows as cancelled. One not yet turned into sales is set aside, as a
    /// person pressing Ignore would; one already turned into sales cannot be — sales are money, and
    /// are never undone automatically — so its id comes back for a person to correct. An id never
    /// imported is nothing to do.
    /// </summary>
    public async Task<PlatformOrderCancellations> ApplyCancellationsAsync(
        string platform,
        IReadOnlyCollection<string> externalOrderIds,
        CancellationToken cancellationToken)
    {
        string? channel = SalesChannelCatalog.CanonicalValue(platform, await ExistingChannels(cancellationToken));
        HashSet<string> keys = externalOrderIds.Select(id => id.Trim()).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (channel is null || keys.Count == 0)
        {
            return new PlatformOrderCancellations(0, []);
        }

        int setAside = 0;
        List<string> stillRecorded = [];
        foreach (PlatformOrder order in await store.ListImportedOrdersByExternalIdsAsync(currentUser.OrganizationId, keys, cancellationToken))
        {
            if (order.Platform != channel)
            {
                continue;
            }

            if (order.Status == PlatformOrderStatus.Imported)
            {
                order.Status = PlatformOrderStatus.Ignored;
                setAside++;
            }
            else if (order.Status == PlatformOrderStatus.Reconciled)
            {
                stillRecorded.Add(order.ExternalOrderId);
            }
        }

        if (setAside > 0)
        {
            await store.SaveChangesAsync(cancellationToken);
        }

        return new PlatformOrderCancellations(setAside, stillRecorded);
    }

    private async Task<List<string>> ExistingChannels(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> saleChannels = await store.ListKnownSalesChannelsAsync(currentUser.OrganizationId, cancellationToken);
        IReadOnlyList<string> listingChannels = await store.ListKnownListingChannelsAsync(currentUser.OrganizationId, cancellationToken);
        return saleChannels.Concat(listingChannels).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }
}

/// <param name="SetAside">Orders not yet turned into sales, now ignored.</param>
/// <param name="StillRecorded">External ids of orders already turned into sales, for a person to correct.</param>
public sealed record PlatformOrderCancellations(int SetAside, IReadOnlyList<string> StillRecorded);
