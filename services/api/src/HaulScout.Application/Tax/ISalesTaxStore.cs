using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Tax;

public sealed record SalesTaxSaleRow(
    Guid SaleId,
    string Platform,
    DateTimeOffset SoldAt,
    SaleStatus Status,
    decimal SoldPrice,
    decimal ShippingCharged,
    decimal SalesTaxCollected,
    bool TaxRemittedByPlatform,
    Guid? OrderId,
    string? OrderNumber,
    decimal OrderDiscountTotal,
    decimal OrderLinesTotal,
    string? ShipToRegion,
    string? ShipToCountry,
    bool IsLocalPickup,
    string ItemTitle);

public sealed record SalesTaxRefundRow(
    Guid RefundId,
    Guid SaleId,
    DateTimeOffset RefundedAt,
    decimal TaxRefunded,
    bool SaleTaxRemittedByPlatform,
    string Platform);

public interface ISalesTaxStore
{
    Task<SalesTaxRegistration?> FindRegistrationAsync(Guid organizationId, string jurisdiction, CancellationToken cancellationToken);

    Task AddRegistrationAsync(SalesTaxRegistration registration, CancellationToken cancellationToken);

    void AddFrequency(SalesTaxFrequencyChange change);

    void RemoveFrequencies(IEnumerable<SalesTaxFrequencyChange> changes);

    Task<string?> FindTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxSaleRow>> ListSalesAsync(Guid organizationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxRefundRow>> ListRefundsAsync(Guid organizationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxUseEntry>> ListUseEntriesAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxAdjustment>> ListAdjustmentsAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxSaleDecision>> ListDecisionsAsync(Guid organizationId, IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<SalesTaxReturn>> ListReturnsAsync(Guid organizationId, string jurisdiction, CancellationToken cancellationToken);

    Task<SalesTaxReturn?> FindReturnAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task AddReturnAsync(SalesTaxReturn filed, CancellationToken cancellationToken);

    Task<SalesTaxUseEntry?> FindUseEntryAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task AddUseEntryAsync(SalesTaxUseEntry entry, CancellationToken cancellationToken);

    void RemoveUseEntry(SalesTaxUseEntry entry);

    Task<SalesTaxAdjustment?> FindAdjustmentAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);

    Task AddAdjustmentAsync(SalesTaxAdjustment adjustment, CancellationToken cancellationToken);

    void RemoveAdjustment(SalesTaxAdjustment adjustment);

    Task<SalesTaxSaleDecision?> FindDecisionAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken);

    Task AddDecisionAsync(SalesTaxSaleDecision decision, CancellationToken cancellationToken);

    void RemoveDecision(SalesTaxSaleDecision decision);

    Task<Sale?> FindSaleAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken);

    Task<bool> ExpenseExistsAsync(Guid organizationId, Guid expenseId, CancellationToken cancellationToken);

    Task<bool> InventoryItemExistsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
