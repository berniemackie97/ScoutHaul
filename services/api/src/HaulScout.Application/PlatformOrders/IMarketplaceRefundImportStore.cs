using HaulScout.Domain.Entities;

namespace HaulScout.Application.PlatformOrders;

public interface IMarketplaceRefundImportStore
{
    Task<IReadOnlyList<Sale>> ListEbayRefundCandidateSalesAsync(
        Guid organizationId,
        string externalItemId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Sale>> ListDepopRefundCandidateSalesAsync(
        Guid organizationId,
        DateTimeOffset? soldAt,
        decimal itemPrice,
        string? sku,
        int take,
        CancellationToken cancellationToken);

    Task<Order?> GetSalesOrderAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task SyncSalesOrderAsync(Order order, IReadOnlyCollection<Sale> changedSales, CancellationToken cancellationToken);

    void AddRefund(Refund refund);

    Task<int> CountSaleCorrectionsAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken);

    void AddSaleCorrection(SaleCorrection correction);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
