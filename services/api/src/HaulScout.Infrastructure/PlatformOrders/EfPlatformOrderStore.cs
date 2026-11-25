using Microsoft.EntityFrameworkCore;
using HaulScout.Application.PlatformOrders;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.PlatformOrders;

public sealed class EfPlatformOrderStore(
    HaulScoutDbContext db,
    OrderAssembler orderAssembler) :
    IPlatformOrderStore,
    IPlatformOrderImportStore,
    IPlatformOrderFeeEnrichmentStore,
    IMarketplaceRefundImportStore
{
    public Task<bool> ChannelAccountExistsAsync(Guid organizationId, Guid channelAccountId, CancellationToken cancellationToken) =>
        db.ChannelAccounts.AnyAsync(
            account => account.Id == channelAccountId && account.OrganizationId == organizationId,
            cancellationToken);

    public async Task<IReadOnlyList<PlatformOrder>> ListAsync(
        Guid organizationId,
        PlatformOrderStatus? status,
        int take,
        CancellationToken cancellationToken)
    {
        return await PlatformOrdersIn(organizationId, status)
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .OrderByDescending(order => order.OrderedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(Guid organizationId, PlatformOrderStatus? status, CancellationToken cancellationToken) =>
        PlatformOrdersIn(organizationId, status).CountAsync(cancellationToken);

    /// <summary>The one definition of "marketplace orders in this status", shared by the list and the count.</summary>
    private IQueryable<PlatformOrder> PlatformOrdersIn(Guid organizationId, PlatformOrderStatus? status)
    {
        IQueryable<PlatformOrder> query = db.PlatformOrders.Where(order => order.OrganizationId == organizationId);
        return status.HasValue ? query.Where(order => order.Status == status.Value) : query;
    }

    public Task<PlatformOrder?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .SingleOrDefaultAsync(order => order.Id == id && order.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<string>> ListKnownSalesChannelsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Sales
            .AsNoTracking()
            .Where(sale => sale.OrganizationId == organizationId)
            .Select(sale => sale.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListKnownListingChannelsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await db.Listings
            .AsNoTracking()
            .Where(listing => listing.OrganizationId == organizationId)
            .Select(listing => listing.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PlatformOrder>> ListImportedOrdersByExternalIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<string> externalOrderIds,
        CancellationToken cancellationToken)
    {
        string[] ids = externalOrderIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return await db.PlatformOrders
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == organizationId && ids.Contains(order.ExternalOrderId))
            .ToListAsync(cancellationToken);
    }

    public Task<string?> FindTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.Where(organization => organization.Id == organizationId).Select(organization => (string?)organization.TimeZoneId).SingleOrDefaultAsync(cancellationToken);

    public async Task RekeySalesOrderAsync(Guid organizationId, string channel, string fromExternalOrderId, string toExternalOrderId, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == organizationId && candidate.Channel == channel && candidate.ExternalOrderId == fromExternalOrderId,
            cancellationToken);
        if (order is not null)
        {
            order.ExternalOrderId = toExternalOrderId;
        }
    }

    public async Task<IReadOnlyList<PlatformOrder>> ListOrdersByBuyersAsync(
        Guid organizationId,
        IReadOnlyCollection<string> buyerReferences,
        CancellationToken cancellationToken)
    {
        string[] buyers = buyerReferences.Select(buyer => buyer.ToLower()).Distinct().ToArray();
        return await db.PlatformOrders
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == organizationId
                && order.BuyerReference != null
                && buyers.Contains(order.BuyerReference.ToLower()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformOrder>> ListImportedOrdersForDisplayAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == organizationId && ids.Contains(order.Id))
            .ToListAsync(cancellationToken);
    }

    public void AddPlatformOrder(PlatformOrder order) => db.PlatformOrders.Add(order);

    public void ReplacePlatformOrderLines(PlatformOrder order, IReadOnlyCollection<PlatformOrderLine> replacementLines)
    {
        db.PlatformOrderLines.RemoveRange(order.Lines);
        order.Lines.Clear();

        foreach (PlatformOrderLine line in replacementLines)
        {
            line.PlatformOrderId = order.Id;
            line.PlatformOrder = order;
            order.Lines.Add(line);
            db.PlatformOrderLines.Add(line);
        }
    }

    public Task<PlatformOrder?> GetForReconciliationAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.PlatformOrders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == id && order.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<PlatformOrderLine>> ListEbayOrderLinesByExternalItemIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<string> externalItemIds,
        CancellationToken cancellationToken)
    {
        string[] ids = externalItemIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.PlatformOrderLines
            .Include(line => line.PlatformOrder)
            .Where(line => line.OrganizationId == organizationId
                && line.ExternalItemId != null
                && ids.Contains(line.ExternalItemId)
                && line.PlatformOrder != null
                && line.PlatformOrder.Platform == "eBay")
            .ToListAsync(cancellationToken);
    }

    public Task<PlatformOrder?> GetImportedOrderWithLinesAsync(Guid organizationId, Guid orderId, CancellationToken cancellationToken) =>
        db.PlatformOrders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == orderId && order.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<InventoryItem>> ListInventoryForReconciliationAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        CancellationToken cancellationToken)
    {
        Guid[] ids = inventoryItemIds.Distinct().ToArray();
        return await db.InventoryItems
            .Include(item => item.BundleComponents).ThenInclude(component => component.ComponentInventoryItem)
            .Where(item => ids.Contains(item.Id) && item.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
    }

    public Task<Order> GetOrCreateSalesOrderAsync(PlatformOrder platformOrder, CancellationToken cancellationToken) =>
        orderAssembler.GetOrCreateAsync(
            platformOrder.Platform,
            platformOrder.ExternalOrderId,
            null,
            platformOrder.BuyerReference,
            platformOrder.BuyerName,
            platformOrder.OrderedAt,
            platformOrder.ChannelAccountId,
            platformOrder.ItemsSubtotal,
            platformOrder.ShippingCharged,
            platformOrder.TaxCollected,
            cancellationToken,
            platformOrder.Carrier,
            platformOrder.TrackingNumber,
            platformOrder.BuyerEmail,
            platformOrder.BuyerNote,
            platformOrder.ShipToName,
            platformOrder.ShipToPhone,
            platformOrder.ShipToLine1,
            platformOrder.ShipToLine2,
            platformOrder.ShipToCity,
            platformOrder.ShipToRegion,
            platformOrder.ShipToPostalCode,
            platformOrder.ShipToCountry,
            platformOrder.PaidAt,
            platformOrder.ShippedAt,
            platformOrder.DeliveredAt);

    public async Task<bool> TryClaimCancellationDecisionAsync(PlatformOrder order, CancellationToken cancellationToken)
    {
        // Conditional on the row still being undecided, so two operators answering at once cannot both win.
        int claimed = await db.PlatformOrders
            .Where(candidate => candidate.Id == order.Id
                && candidate.OrganizationId == order.OrganizationId
                && candidate.CancellationOutcome == PlatformOrderCancellationOutcome.None)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(candidate => candidate.CancellationOutcome, order.CancellationOutcome)
                    .SetProperty(candidate => candidate.CancellationDecidedAt, order.CancellationDecidedAt)
                    .SetProperty(candidate => candidate.CancellationNote, order.CancellationNote),
                cancellationToken);
        return claimed == 1;
    }

    public async Task<IReadOnlyList<PlatformOrder>> ListAwaitingCancellationDecisionAsync(Guid organizationId, int take, CancellationToken cancellationToken) =>
        await db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == organizationId
                && order.Status == PlatformOrderStatus.Reconciled
                && order.CancelledOnPlatformAt != null
                && order.CancellationOutcome == PlatformOrderCancellationOutcome.None)
            .OrderBy(order => order.CancelledOnPlatformAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListLiveSaleIdsInGroupAsync(Guid organizationId, Guid saleGroupId, CancellationToken cancellationToken) =>
        await db.Sales
            .Where(sale => sale.OrganizationId == organizationId
                && sale.SaleGroupId == saleGroupId
                && sale.Status != SaleStatus.Cancelled)
            .Select(sale => sale.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListExistingSalesForReconciliationAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        string platform,
        string externalOrderId,
        Guid salesOrderId,
        IReadOnlyCollection<SaleStatus> releasedInventoryStatuses,
        CancellationToken cancellationToken)
    {
        Guid[] ids = inventoryItemIds.Distinct().ToArray();
        SaleStatus[] released = releasedInventoryStatuses.ToArray();
        return await db.Sales
            .Include(sale => sale.Refunds)
            .Include(sale => sale.Returns)
            .Include(sale => sale.Corrections)
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Listing)
            .Include(sale => sale.Order)
            .Where(sale => ids.Contains(sale.InventoryItemId)
                && sale.OrganizationId == organizationId
                && (!released.Contains(sale.Status)
                    || sale.OrderId == salesOrderId
                    || (sale.Order != null
                        && sale.Order.Channel == platform
                        && sale.Order.ExternalOrderId == externalOrderId)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelListing>> ListChannelListingsAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken) =>
        await db.ChannelListings
            .Include(listing => listing.ChannelAccount)
            .Where(listing => listing.InventoryItemId == inventoryItemId && listing.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

    public Task<MarketObservation?> GetInternalSaleObservationAsync(
        Guid organizationId,
        Guid inventoryItemId,
        DateTimeOffset soldAt,
        CancellationToken cancellationToken) =>
        db.MarketObservations
            .Where(observation => observation.InventoryItemId == inventoryItemId
                && observation.ObservationType == MarketObservationType.InternalSale
                && observation.OrganizationId == organizationId
                && observation.SoldAt == soldAt)
            .OrderByDescending(observation => observation.ObservedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Order?> GetSalesOrderAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(order => order.Id == id && order.OrganizationId == organizationId, cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListActiveSalesForOrderCorrectionAsync(
        Guid organizationId,
        Guid orderId,
        CancellationToken cancellationToken) =>
        await db.Sales
            .Include(sale => sale.Refunds)
            .Include(sale => sale.InventoryItem)
            .Where(sale => sale.OrganizationId == organizationId
                && sale.OrderId == orderId
                && sale.Status != SaleStatus.Cancelled)
            .ToListAsync(cancellationToken);

    public Task SyncSalesOrderAsync(
        Order order,
        IReadOnlyCollection<Sale> changedSales,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? removedSaleIds = null) =>
        orderAssembler.SyncSalesOrderAsync(order, changedSales, cancellationToken, removedSaleIds);

    public Task SyncSalesOrderAsync(
        Order order,
        IReadOnlyCollection<Sale> changedSales,
        CancellationToken cancellationToken) =>
        orderAssembler.SyncSalesOrderAsync(order, changedSales, cancellationToken);

    public async Task<IReadOnlyList<Sale>> ListEbayRefundCandidateSalesAsync(
        Guid organizationId,
        string externalItemId,
        CancellationToken cancellationToken)
    {
        List<PlatformOrderLine> lines = await db.PlatformOrderLines
            .Include(line => line.PlatformOrder)
            .Where(line => line.OrganizationId == organizationId
                && line.ExternalItemId == externalItemId
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

        Guid[] orderIds = keys.Select(key => key.OrderId).Distinct().ToArray();
        Guid[] inventoryIds = keys.Select(key => key.InventoryItemId).Distinct().ToArray();
        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
                .ThenInclude(inv => inv!.BundleComponents)
                    .ThenInclude(component => component.ComponentInventoryItem)
            .Include(sale => sale.Refunds)
            .Include(sale => sale.Returns)
            .Where(sale => sale.OrganizationId == organizationId
                && sale.OrderId.HasValue
                && orderIds.Contains(sale.OrderId.Value)
                && inventoryIds.Contains(sale.InventoryItemId))
            .ToListAsync(cancellationToken);

        return sales
            .Where(sale => sale.OrderId.HasValue && keys.Contains((sale.OrderId.Value, sale.InventoryItemId)))
            .ToList();
    }

    public async Task<IReadOnlyList<Sale>> ListDepopRefundCandidateSalesAsync(
        Guid organizationId,
        DateTimeOffset? soldAt,
        decimal itemPrice,
        string? sku,
        int take,
        CancellationToken cancellationToken)
    {
        IQueryable<Sale> query = db.Sales
            .Include(sale => sale.InventoryItem)
                .ThenInclude(inv => inv!.BundleComponents)
                    .ThenInclude(component => component.ComponentInventoryItem)
            .Include(sale => sale.Refunds)
            .Include(sale => sale.Returns)
            .Where(sale => sale.OrganizationId == organizationId && sale.Platform == "Depop");

        if (soldAt.HasValue)
        {
            DateTimeOffset start = new(soldAt.Value.Year, soldAt.Value.Month, soldAt.Value.Day, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset end = start.AddDays(1);
            query = query.Where(sale => sale.SoldAt >= start && sale.SoldAt < end);
        }
        if (itemPrice > 0)
        {
            decimal low = itemPrice - 0.01m;
            decimal high = itemPrice + 0.01m;
            query = query.Where(sale => sale.SoldPrice >= low && sale.SoldPrice <= high);
        }
        if (!string.IsNullOrWhiteSpace(sku))
        {
            string normalizedSku = sku.Trim().ToLowerInvariant();
            query = query.Where(sale => sale.InventoryItem != null
                && sale.InventoryItem.Barcode != null
                && sale.InventoryItem.Barcode.ToLower() == normalizedSku);
        }

        return await query.Take(take).ToListAsync(cancellationToken);
    }

    public Task PruneEmptyMarketplaceOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        orderAssembler.PruneEmptyMarketplaceOrderAsync(orderId, cancellationToken);

    public async Task MoveShipmentsAsync(Guid organizationId, Sale survivingSale, IReadOnlyCollection<Sale> duplicateSales, CancellationToken cancellationToken)
    {
        Guid[] duplicateSaleIds = duplicateSales.Select(sale => sale.Id).ToArray();
        if (duplicateSaleIds.Length == 0)
        {
            return;
        }

        List<Shipment> shipments = await db.Shipments
            .Where(shipment => shipment.OrganizationId == organizationId && duplicateSaleIds.Contains(shipment.SaleId))
            .ToListAsync(cancellationToken);
        foreach (Shipment shipment in shipments)
        {
            shipment.SaleId = survivingSale.Id;
            shipment.Sale = survivingSale;
        }
    }

    public Task<int> CountSaleCorrectionsAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken) =>
        db.SaleCorrections.CountAsync(
            correction => correction.SaleId == saleId && correction.OrganizationId == organizationId,
            cancellationToken);

    public void AddSale(Sale sale) => db.Sales.Add(sale);

    public void AddInventoryItem(InventoryItem item) => db.InventoryItems.Add(item);

    public void AddChannelListing(ChannelListing listing) => db.ChannelListings.Add(listing);

    public void RemoveImportedOrder(PlatformOrder order)
    {
        db.PlatformOrderLines.RemoveRange(order.Lines);
        db.PlatformOrders.Remove(order);
    }

    public void RemoveSales(IReadOnlyCollection<Sale> sales) => db.Sales.RemoveRange(sales);

    public void AddMarketObservation(MarketObservation observation) => db.MarketObservations.Add(observation);

    public void AddSaleCorrection(SaleCorrection correction) => db.SaleCorrections.Add(correction);

    public void AddRefund(Refund refund) => db.Refunds.Add(refund);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
