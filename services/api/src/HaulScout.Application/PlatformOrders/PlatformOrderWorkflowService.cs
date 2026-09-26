using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sales;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Domain.Services;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Application workflow for imported marketplace orders. HTTP controllers pass requests in; this service owns the
/// domain rules for listing, viewing, ignoring, and reconciling imported orders into first-class Orders/Sales.
/// </summary>
public sealed class PlatformOrderWorkflowService(
    IPlatformOrderStore store,
    ICurrentUserContext currentUser,
    PlatformOrderSuggestionService suggestionService)
{
    private static readonly SaleStatus[] ReleasedInventoryStatuses = [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.Cancelled];

    public async Task<IReadOnlyList<PlatformOrderDto>> ListAsync(PlatformOrderStatus? status, CancellationToken cancellationToken)
    {
        IReadOnlyList<PlatformOrder> orders = await store.ListAsync(currentUser.OrganizationId, status, 200, cancellationToken);
        Dictionary<Guid, PlatformLineSuggestion> suggestions =
            await suggestionService.BuildSuggestionsAsync(orders, cancellationToken);
        return orders.Select(order => PlatformOrderDtoFactory.ToPlatformOrderDto(order, suggestions)).ToArray();
    }

    /// <summary>
    /// Every order the marketplace cancelled that nobody has ruled on. Asked for separately because the ordinary
    /// list is capped, and a cancellation that falls off the end of it is a question nobody can answer.
    /// </summary>
    public async Task<IReadOnlyList<PlatformOrderDto>> ListAwaitingCancellationDecisionAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlatformOrder> orders =
            await store.ListAwaitingCancellationDecisionAsync(currentUser.OrganizationId, 200, cancellationToken);
        return orders.Select(order => PlatformOrderDtoFactory.ToPlatformOrderDto(order)).ToArray();
    }

    public async Task<AppResult<PlatformOrderDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await store.GetAsync(currentUser.OrganizationId, id, cancellationToken);
        if (order is null)
        {
            return AppResult<PlatformOrderDto>.NotFound();
        }

        Dictionary<Guid, PlatformLineSuggestion> suggestions =
            await suggestionService.BuildSuggestionsAsync([order], cancellationToken);
        return AppResult<PlatformOrderDto>.Ok(PlatformOrderDtoFactory.ToPlatformOrderDto(order, suggestions));
    }

    /// <summary>
    /// Removes an imported order outright — the wrong file, a test order, a row that should never have
    /// been brought in. Refused once the order is reconciled: sales exist downstream by then, and
    /// deleting the import row would strand them with no record of where they came from. Ignoring
    /// keeps the row and its history; deleting is for rows that should not exist at all.
    /// </summary>
    public async Task<AppResult<PlatformOrderDeletedDto>> DeleteImportedAsync(Guid id, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await store.GetForReconciliationAsync(currentUser.OrganizationId, id, cancellationToken);
        if (order is null)
        {
            return AppResult<PlatformOrderDeletedDto>.NotFound();
        }

        if (order.Status == PlatformOrderStatus.Reconciled)
        {
            return AppResult<PlatformOrderDeletedDto>.Validation(
                "This order has already been matched, so sales were recorded from it. Undo those first, or ignore it instead of deleting.");
        }

        store.RemoveImportedOrder(order);
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<PlatformOrderDeletedDto>.Ok(new PlatformOrderDeletedDto(id));
    }

    public async Task<AppResult<PlatformOrderDto>> IgnoreAsync(Guid id, CancellationToken cancellationToken)
    {
        PlatformOrder? order = await store.GetAsync(currentUser.OrganizationId, id, cancellationToken);
        if (order is null)
        {
            return AppResult<PlatformOrderDto>.NotFound();
        }

        if (order.Status == PlatformOrderStatus.Reconciled)
        {
            return AppResult<PlatformOrderDto>.Conflict("A reconciled order cannot be ignored. Correct its sales instead.");
        }

        order.Status = PlatformOrderStatus.Ignored;
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<PlatformOrderDto>.Ok(PlatformOrderDtoFactory.ToPlatformOrderDto(order));
    }

    public async Task<AppResult<PlatformOrderReconcileResultDto>> ReconcileAsync(
        Guid id,
        ReconcilePlatformOrderRequest request,
        CancellationToken cancellationToken)
    {
        PlatformOrder? order = await store.GetForReconciliationAsync(currentUser.OrganizationId, id, cancellationToken);
        if (order is null)
        {
            return AppResult<PlatformOrderReconcileResultDto>.NotFound();
        }

        // Settling writes the sale's money. Until the marketplace posts the fees, the order's fee
        // figure is a placeholder, and a sale recorded from it would show a profit that is not true —
        // with nothing left to say so once it is settled.
        if (order.FeesPending)
        {
            return AppResult<PlatformOrderReconcileResultDto>.Validation(
                $"{order.Platform} has not posted this order's fees yet. The next sales scan after they appear brings them in; settle it then.");
        }

        Dictionary<Guid, Guid> mapByLine = (request.Lines ?? [])
            .Where(line => line.InventoryItemId.HasValue && !line.CreateItem)
            .GroupBy(line => line.PlatformOrderLineId)
            .ToDictionary(group => group.Key, group => group.First().InventoryItemId!.Value);
        foreach (PlatformOrderLine line in order.Lines.Where(line => line.InventoryItemId.HasValue))
        {
            mapByLine.TryAdd(line.Id, line.InventoryItemId!.Value);
        }

        // Lines whose item was never recorded — sold before it was ever inventory — get one made from
        // the sale itself. It joins the reconciliation below like any matched item, and is saved with
        // it, so nothing is created unless the whole order settles.
        HashSet<Guid> createFor = (request.Lines ?? [])
            .Where(line => line.CreateItem && !mapByLine.ContainsKey(line.PlatformOrderLineId))
            .Select(line => line.PlatformOrderLineId)
            .ToHashSet();
        if (createFor.Any(lineId => order.Lines.All(line => line.Id != lineId)))
        {
            return AppResult<PlatformOrderReconcileResultDto>.NotFound("That line is not part of this order.");
        }

        if (order.Lines.Any(line => !mapByLine.ContainsKey(line.Id) && !createFor.Contains(line.Id)))
        {
            return AppResult<PlatformOrderReconcileResultDto>.Validation("Every order line must be matched to an inventory item, or have one created from the sale, before reconciling.");
        }

        Guid[] matchedIds = mapByLine.Values.ToArray();
        if (matchedIds.Length != matchedIds.Distinct().Count())
        {
            return AppResult<PlatformOrderReconcileResultDto>.Validation("Each inventory item can be matched to only one order line.");
        }

        List<InventoryItem> items = [.. await store.ListInventoryForReconciliationAsync(currentUser.OrganizationId, matchedIds, cancellationToken)];
        if (items.Count != matchedIds.Length)
        {
            return AppResult<PlatformOrderReconcileResultDto>.NotFound("One or more matched inventory items were not found.");
        }

        foreach (PlatformOrderLine line in order.Lines.Where(line => createFor.Contains(line.Id)))
        {
            InventoryItem created = ItemFromSale(order, line);
            store.AddInventoryItem(created);
            if (Text.TrimToNull(line.ExternalItemId) is string listingId && order.ChannelAccountId is Guid accountId)
            {
                // The listing that sold, linked by its id, so a later order or listing import for it
                // matches exactly instead of asking again.
                store.AddChannelListing(new ChannelListing
                {
                    OrganizationId = currentUser.OrganizationId,
                    InventoryItemId = created.Id,
                    ChannelAccountId = accountId,
                    Status = ChannelListingStatus.Sold,
                    ExternalListingId = listingId,
                    ListedPrice = line.UnitPrice,
                    Quantity = line.Quantity,
                    SoldAt = order.OrderedAt,
                    LastSyncedAt = DateTimeOffset.UtcNow,
                });
            }

            mapByLine[line.Id] = created.Id;
            items.Add(created);
        }

        Guid[] itemIds = mapByLine.Values.ToArray();

        Order salesOrder = await store.GetOrCreateSalesOrderAsync(order, cancellationToken);
        IReadOnlyList<Sale> existingSales = await store.ListExistingSalesForReconciliationAsync(
            currentUser.OrganizationId,
            itemIds,
            order.Platform,
            order.ExternalOrderId,
            salesOrder.Id,
            ReleasedInventoryStatuses,
            cancellationToken);
        Dictionary<Guid, Sale> saleByItem = existingSales
            .GroupBy(sale => sale.InventoryItemId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(sale => ExistingSalePriority(sale, salesOrder.Id))
                .ThenByDescending(sale => sale.SoldAt)
                .First());

        List<BundledSaleItemAllocation> allocationInputs = order.Lines
            .Select(line => new BundledSaleItemAllocation(mapByLine[line.Id], line.UnitPrice * line.Quantity, 0))
            .ToList();
        decimal totalSoldPrice = order.Lines.Sum(line => line.UnitPrice * line.Quantity);

        IReadOnlyList<AllocatedSaleLine> lines;
        try
        {
            lines = BundledSaleAllocator.Allocate(
                allocationInputs,
                totalSoldPrice,
                order.ShippingCharged,
                order.FeeTotal,
                paymentFees: 0,
                shippingCost: order.SellerShippingCost ?? 0,
                suppliesCost: 0,
                otherCosts: 0,
                order.TaxCollected);
        }
        catch (InvalidOperationException exception)
        {
            return AppResult<PlatformOrderReconcileResultDto>.Validation(exception.Message);
        }

        DateTimeOffset soldAt = order.OrderedAt;
        Dictionary<Guid, InventoryItem> byId = items.ToDictionary(item => item.Id);
        Dictionary<Guid, PlatformOrderLine> lineByItem = order.Lines.ToDictionary(line => mapByLine[line.Id], line => line);
        List<Sale> resultSales = [];
        List<Guid> removedSaleIdsFromSalesOrder = [];
        Dictionary<Guid, List<Guid>> removedSaleIdsByPreviousOrder = [];
        int createdCount = 0;
        int correctedCount = 0;

        foreach (AllocatedSaleLine allocated in lines)
        {
            InventoryItem item = byId[allocated.InventoryItemId];
            PlatformOrderLine platformLine = lineByItem[item.Id];
            platformLine.InventoryItemId = item.Id;
            AllocatedSaleLine applied = allocated with
            {
                ShippingCharged = platformLine.ShippingCharged ?? allocated.ShippingCharged,
                PlatformFees = platformLine.PlatformFees ?? allocated.PlatformFees,
                ShippingCost = platformLine.SellerShippingCost ?? allocated.ShippingCost,
                SalesTaxCollected = platformLine.TaxCollected ?? allocated.SalesTaxCollected
            };

            if (saleByItem.TryGetValue(item.Id, out Sale? existing))
            {
                IReadOnlyList<Sale> duplicateSameOrderSales = existingSales
                    .Where(sale => sale.InventoryItemId == item.Id
                        && sale.OrderId == salesOrder.Id
                        && sale.Id != existing.Id
                        && CanRemoveSameOrderDuplicate(sale))
                    .ToArray();
                if (duplicateSameOrderSales.Count > 0)
                {
                    await store.MoveShipmentsAsync(currentUser.OrganizationId, existing, duplicateSameOrderSales, cancellationToken);
                    store.RemoveSales(duplicateSameOrderSales);
                    removedSaleIdsFromSalesOrder.AddRange(duplicateSameOrderSales.Select(sale => sale.Id));
                }

                string before = SaleFinancialState.Snapshot(existing);
                existing.Platform = order.Platform;
                existing.SoldPrice = applied.Price;
                existing.ShippingCharged = applied.ShippingCharged;
                existing.PlatformFees = applied.PlatformFees;
                existing.PaymentFees = applied.PaymentFees;
                existing.SalesTaxCollected = applied.SalesTaxCollected;
                existing.TaxRemittedByPlatform = order.TaxRemittedByPlatform;
                if (platformLine.SellerShippingCost.HasValue || order.SellerShippingCost.HasValue)
                {
                    existing.ShippingCost = applied.ShippingCost;
                }
                if (existing.OrderId is Guid previousOrderId && previousOrderId != salesOrder.Id)
                {
                    if (!removedSaleIdsByPreviousOrder.TryGetValue(previousOrderId, out List<Guid>? removedIds))
                    {
                        removedIds = [];
                        removedSaleIdsByPreviousOrder[previousOrderId] = removedIds;
                    }
                    removedIds.Add(existing.Id);
                }
                existing.OrderId = salesOrder.Id;
                existing.Order = salesOrder;
                existing.RecomputeNet();

                await UpdateInternalSaleObservation(item, existing.SoldAt, applied, cancellationToken);
                await AddCorrection(existing, before, $"Reconciled to {order.Platform} order {order.ExternalOrderId} — exact platform fees.", cancellationToken);
                resultSales.Add(existing);
                correctedCount++;
            }
            else
            {
                Sale sale = new()
                {
                    OrganizationId = currentUser.OrganizationId,
                    InventoryItemId = item.Id,
                    Platform = order.Platform,
                    SoldPrice = applied.Price,
                    ShippingCharged = applied.ShippingCharged,
                    CostOfGoods = item.PurchasePrice ?? 0,
                    PlatformFees = applied.PlatformFees,
                    PaymentFees = applied.PaymentFees,
                    ShippingCost = applied.ShippingCost,
                    SuppliesCost = applied.SuppliesCost,
                    OtherCosts = applied.OtherCosts,
                    SalesTaxCollected = applied.SalesTaxCollected,
                    TaxRemittedByPlatform = order.TaxRemittedByPlatform,
                    SoldAt = soldAt,
                    Notes = TrimToNull(request.Notes),
                    OrderId = salesOrder.Id,
                    Order = salesOrder,
                    InventoryItem = item,
                };
                sale.RecomputeNet();
                store.AddSale(sale);
                resultSales.Add(sale);
                createdCount++;

                item.Status = InventoryStatus.Sold;
                item.SoldAt = soldAt;
                InventoryStatePropagation.ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, soldAt);

                store.AddMarketObservation(new MarketObservation
                {
                    OrganizationId = currentUser.OrganizationId,
                    ProductIdentityId = item.ProductIdentityId,
                    InventoryItemId = item.Id,
                    ObservationType = MarketObservationType.InternalSale,
                    Source = order.Platform,
                    Title = item.Title,
                    Price = applied.Price,
                    TotalPrice = applied.Price + applied.ShippingCharged,
                    Condition = item.Condition,
                    ObservedAt = soldAt,
                    SoldAt = soldAt,
                    Notes = $"Reconciled from {order.Platform} order {order.ExternalOrderId}."
                });
            }

            IReadOnlyList<ChannelListing> channelListings = await store.ListChannelListingsAsync(currentUser.OrganizationId, item.Id, cancellationToken);
            ChannelListingSync.ApplySale(channelListings, order.Platform, DateTimeOffset.UtcNow);
        }

        await store.SyncSalesOrderAsync(salesOrder, resultSales, cancellationToken, removedSaleIdsFromSalesOrder);
        foreach ((Guid previousOrderId, List<Guid> removedSaleIds) in removedSaleIdsByPreviousOrder)
        {
            Order? previousOrder = await store.GetSalesOrderAsync(currentUser.OrganizationId, previousOrderId, cancellationToken);
            if (previousOrder is not null)
            {
                await store.SyncSalesOrderAsync(previousOrder, [], cancellationToken, removedSaleIds);
                await store.PruneEmptyMarketplaceOrderAsync(previousOrder.Id, cancellationToken);
            }
        }

        order.Status = PlatformOrderStatus.Reconciled;
        order.ReconciledSaleGroupId = salesOrder.Id;
        order.ReconciledAt = DateTimeOffset.UtcNow;

        await store.SaveChangesAsync(cancellationToken);

        IReadOnlyList<SaleDto> dtos = resultSales.Select(PlatformOrderDtoFactory.ToSaleDto).ToArray();
        return AppResult<PlatformOrderReconcileResultDto>.Ok(new PlatformOrderReconcileResultDto(order.Id, salesOrder.Id, createdCount, correctedCount, dtos, createFor.Count));
    }

    /// <summary>
    /// The item a sale was of, made from what the platform says about it. Its cost is unknown — left
    /// empty, not nought, so it shows as missing rather than as free.
    /// </summary>
    private InventoryItem ItemFromSale(PlatformOrder order, PlatformOrderLine line) => new()
    {
        OrganizationId = currentUser.OrganizationId,
        Title = line.Title.Length <= 300 ? line.Title : line.Title[..300],
        Status = InventoryStatus.Listed,
        Quantity = Math.Max(1, line.Quantity),
        EstimatedSalePrice = line.UnitPrice,
        Barcode = Text.TrimToNull(line.Sku),
        PurchasePrice = null,
        Notes = $"Created from {order.Platform} order {order.ExternalOrderId}: it sold before it was recorded as inventory. Add its cost.",
    };

    private static int ExistingSalePriority(Sale sale, Guid salesOrderId)
    {
        bool sameOrder = sale.OrderId == salesOrderId;
        bool hasReturnOrRefundHistory = sale.Refunds.Count > 0 || sale.Returns.Count > 0;
        if (sameOrder && (ReleasedInventoryStatuses.Contains(sale.Status) || hasReturnOrRefundHistory))
        {
            return 0;
        }
        if (sameOrder)
        {
            return 1;
        }
        return ReleasedInventoryStatuses.Contains(sale.Status) ? 3 : 2;
    }

    private static bool CanRemoveSameOrderDuplicate(Sale sale) =>
        sale.Refunds.Count == 0
        && sale.Returns.Count == 0
        && sale.Corrections.Count == 0;

    private async Task UpdateInternalSaleObservation(InventoryItem item, DateTimeOffset soldAt, AllocatedSaleLine allocated, CancellationToken cancellationToken)
    {
        MarketObservation? observation = await store.GetInternalSaleObservationAsync(currentUser.OrganizationId, item.Id, soldAt, cancellationToken);
        if (observation is null)
        {
            store.AddMarketObservation(new MarketObservation
            {
                OrganizationId = currentUser.OrganizationId,
                ProductIdentityId = item.ProductIdentityId,
                InventoryItemId = item.Id,
                ObservationType = MarketObservationType.InternalSale,
                Source = "Internal",
                Title = item.Title,
                Price = allocated.Price,
                TotalPrice = allocated.Price + allocated.ShippingCharged,
                Condition = item.Condition,
                ObservedAt = soldAt,
                SoldAt = soldAt,
                Notes = "Created from platform-order reconciliation.",
            });
            return;
        }

        observation.Price = allocated.Price;
        observation.TotalPrice = allocated.Price + allocated.ShippingCharged;
    }

    private async Task AddCorrection(Sale sale, string before, string reason, CancellationToken cancellationToken)
    {
        int nextVersion = await store.CountSaleCorrectionsAsync(currentUser.OrganizationId, sale.Id, cancellationToken) + 1;
        store.AddSaleCorrection(new SaleCorrection
        {
            OrganizationId = currentUser.OrganizationId,
            SaleId = sale.Id,
            CorrectedByUserId = currentUser.UserId,
            Reason = reason,
            BeforeJson = before,
            AfterJson = SaleFinancialState.Snapshot(sale),
            Version = nextVersion,
        });
    }

    private static string? TrimToNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
