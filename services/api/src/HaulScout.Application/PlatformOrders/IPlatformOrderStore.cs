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

    /// <summary>How many marketplace orders are in a status, across every one imported.</summary>
    Task<int> CountAsync(Guid organizationId, PlatformOrderStatus? status, CancellationToken cancellationToken);

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

    /// <summary>
    /// Writes a cancellation decision onto an order, but only while no decision has been made. False means someone
    /// else got there first, which is the one case where doing nothing is the right answer.
    /// </summary>
    Task<bool> TryClaimCancellationDecisionAsync(PlatformOrder order, CancellationToken cancellationToken);

    /// <summary>Orders the marketplace cancelled that nobody has ruled on, whatever else is in the queue.</summary>
    Task<IReadOnlyList<PlatformOrder>> ListAwaitingCancellationDecisionAsync(Guid organizationId, int take, CancellationToken cancellationToken);

    /// <summary>The sales an order was reconciled into that are still standing, so cancelling skips the settled ones.</summary>
    Task<IReadOnlyList<Guid>> ListLiveSaleIdsInGroupAsync(Guid organizationId, Guid saleGroupId, CancellationToken cancellationToken);

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

    /// <summary>An inventory item created from a sale, for a line whose item was never recorded.</summary>
    void AddInventoryItem(InventoryItem item);

    void AddChannelListing(ChannelListing listing);

    /// <summary>Removes an imported order and its source lines. Only valid while it is unreconciled.</summary>
    void RemoveImportedOrder(PlatformOrder order);

    void RemoveSales(IReadOnlyCollection<Sale> sales);

    void AddMarketObservation(MarketObservation observation);

    void AddSaleCorrection(SaleCorrection correction);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
