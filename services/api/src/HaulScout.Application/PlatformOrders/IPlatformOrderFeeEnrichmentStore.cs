using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

public interface IPlatformOrderFeeEnrichmentStore
{
    Task<IReadOnlyList<PlatformOrderLine>> ListEbayOrderLinesByExternalItemIdsAsync(
        Guid organizationId,
        IReadOnlyCollection<string> externalItemIds,
        CancellationToken cancellationToken);

    Task<PlatformOrder?> GetImportedOrderWithLinesAsync(Guid organizationId, Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Sale>> ListActiveSalesForOrderCorrectionAsync(
        Guid organizationId,
        Guid orderId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformOrder>> ListImportedOrdersForDisplayAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken);

    Task<int> CountSaleCorrectionsAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken);

    void AddSaleCorrection(SaleCorrection correction);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
