using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

public interface IPlatformOrderStore
{
    Task<IReadOnlyList<PlatformOrder>> ListAsync(
        Guid organizationId,
        PlatformOrderStatus? status,
        int take,
        CancellationToken cancellationToken);

    Task<PlatformOrder?> GetAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<PlatformOrder?> GetForReconciliationAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<InventoryItem>> ListInventoryForReconciliationAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        CancellationToken cancellationToken);

    Task<Order> GetOrCreateSalesOrderAsync(PlatformOrder platformOrder, CancellationToken cancellationToken);

    Task<IReadOnlyList<Sale>> ListExistingSalesForReconciliationAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> inventoryItemIds,
        string platform,
        string externalOrderId,
        Guid salesOrderId,
        IReadOnlyCollection<SaleStatus> releasedInventoryStatuses,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelListing>> ListChannelListingsAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    Task<MarketObservation?> GetInternalSaleObservationAsync(
        Guid organizationId,
        Guid inventoryItemId,
        DateTimeOffset soldAt,
        CancellationToken cancellationToken);

    Task<Order?> GetSalesOrderAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task SyncSalesOrderAsync(
        Order order,
        IReadOnlyCollection<Sale> changedSales,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? removedSaleIds = null);

    Task PruneEmptyMarketplaceOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task MoveShipmentsAsync(Guid organizationId, Sale survivingSale, IReadOnlyCollection<Sale> duplicateSales, CancellationToken cancellationToken);

    Task<int> CountSaleCorrectionsAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken);

    void AddSale(Sale sale);

    /// <summary>Removes an imported order and its source lines. Only valid while it is unreconciled.</summary>
    void RemoveImportedOrder(PlatformOrder order);

    void RemoveSales(IReadOnlyCollection<Sale> sales);

    void AddMarketObservation(MarketObservation observation);

    void AddSaleCorrection(SaleCorrection correction);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
