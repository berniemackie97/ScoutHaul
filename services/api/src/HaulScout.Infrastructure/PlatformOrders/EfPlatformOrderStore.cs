using Microsoft.EntityFrameworkCore;
using HaulScout.Application.PlatformOrders;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.PlatformOrders;

public sealed class EfPlatformOrderStore(
    HaulScoutDbContext db,
    OrderAssembler orderAssembler) : IPlatformOrderStore
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
        IQueryable<PlatformOrder> query = db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .Where(order => order.OrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(order => order.Status == status.Value);
        }

        return await query
            .OrderByDescending(order => order.OrderedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<PlatformOrder?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.PlatformOrders
            .Include(order => order.Lines).ThenInclude(line => line.InventoryItem)
            .SingleOrDefaultAsync(order => order.Id == id && order.OrganizationId == organizationId, cancellationToken);

    public Task<PlatformOrder?> GetForReconciliationAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.PlatformOrders
            .Include(order => order.Lines)
            .SingleOrDefaultAsync(order => order.Id == id && order.OrganizationId == organizationId, cancellationToken);

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
            platformOrder.ShippedAt);

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

    public Task SyncSalesOrderAsync(
        Order order,
        IReadOnlyCollection<Sale> changedSales,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? removedSaleIds = null) =>
        orderAssembler.SyncSalesOrderAsync(order, changedSales, cancellationToken, removedSaleIds);

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

    public void RemoveSales(IReadOnlyCollection<Sale> sales) => db.Sales.RemoveRange(sales);

    public void AddMarketObservation(MarketObservation observation) => db.MarketObservations.Add(observation);

    public void AddSaleCorrection(SaleCorrection correction) => db.SaleCorrections.Add(correction);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
