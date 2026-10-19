using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Tax;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Tax;

public sealed class EfSalesTaxStore(HaulScoutDbContext db) : ISalesTaxStore
{
    public Task<SalesTaxRegistration?> FindRegistrationAsync(Guid organizationId, string jurisdiction, CancellationToken cancellationToken) =>
        db.SalesTaxRegistrations
            .Include(item => item.Frequencies)
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Jurisdiction == jurisdiction, cancellationToken);

    public async Task AddRegistrationAsync(SalesTaxRegistration registration, CancellationToken cancellationToken) =>
        await db.SalesTaxRegistrations.AddAsync(registration, cancellationToken);

    public void AddFrequency(SalesTaxFrequencyChange change) => db.SalesTaxFrequencyChanges.Add(change);

    public void RemoveFrequencies(IEnumerable<SalesTaxFrequencyChange> changes) => db.SalesTaxFrequencyChanges.RemoveRange(changes);

    public Task<string?> FindTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        db.Organizations.Where(item => item.Id == organizationId).Select(item => item.TimeZoneId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxSaleRow>> ListSalesAsync(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        await db.Sales
            .AsNoTracking()
            .Where(sale => sale.OrganizationId == organizationId && sale.SoldAt >= fromUtc && sale.SoldAt < toUtc)
            .Select(sale => new SalesTaxSaleRow(
                sale.Id,
                sale.Platform,
                sale.SoldAt,
                sale.Status,
                sale.SoldPrice,
                sale.ShippingCharged,
                sale.SalesTaxCollected,
                sale.TaxRemittedByPlatform,
                sale.OrderId,
                sale.Order != null ? sale.Order.OrderNumber : null,
                sale.Order != null ? sale.Order.DiscountTotal : 0m,
                sale.Order != null ? sale.Order.Lines.Sum(line => line.LineTotal) : 0m,
                sale.Order != null ? sale.Order.ShipToRegion : null,
                sale.Order != null ? sale.Order.ShipToCountry : null,
                sale.Order != null && sale.Order.IsLocalPickup,
                sale.InventoryItem != null ? sale.InventoryItem.Title : "(item removed)"))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxRefundRow>> ListRefundsAsync(
        Guid organizationId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        await db.Refunds
            .AsNoTracking()
            .Where(refund => refund.OrganizationId == organizationId && refund.RefundedAt >= fromUtc && refund.RefundedAt < toUtc)
            .Select(refund => new SalesTaxRefundRow(
                refund.Id,
                refund.SaleId,
                refund.RefundedAt,
                refund.TaxRefunded,
                refund.Sale != null && refund.Sale.TaxRemittedByPlatform,
                refund.Sale != null ? refund.Sale.Platform : ""))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxUseEntry>> ListUseEntriesAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.SalesTaxUseEntries
            .AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId && entry.OccurredOn >= from && entry.OccurredOn <= to)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxAdjustment>> ListAdjustmentsAsync(Guid organizationId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.SalesTaxAdjustments
            .AsNoTracking()
            .Where(adjustment => adjustment.OrganizationId == organizationId && adjustment.PeriodStart >= from && adjustment.PeriodEnd <= to)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxSaleDecision>> ListDecisionsAsync(Guid organizationId, IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken) =>
        saleIds.Count == 0
            ? []
            : await db.SalesTaxSaleDecisions
                .AsNoTracking()
                .Where(decision => decision.OrganizationId == organizationId && saleIds.Contains(decision.SaleId))
                .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesTaxReturn>> ListReturnsAsync(Guid organizationId, string jurisdiction, CancellationToken cancellationToken) =>
        await db.SalesTaxReturns
            .AsNoTracking()
            .Where(filed => filed.OrganizationId == organizationId && filed.Jurisdiction == jurisdiction)
            .ToListAsync(cancellationToken);

    public Task<SalesTaxReturn?> FindReturnAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.SalesTaxReturns.FirstOrDefaultAsync(filed => filed.OrganizationId == organizationId && filed.Id == id, cancellationToken);

    public async Task AddReturnAsync(SalesTaxReturn filed, CancellationToken cancellationToken) =>
        await db.SalesTaxReturns.AddAsync(filed, cancellationToken);

    public Task<SalesTaxUseEntry?> FindUseEntryAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.SalesTaxUseEntries.FirstOrDefaultAsync(entry => entry.OrganizationId == organizationId && entry.Id == id, cancellationToken);

    public async Task AddUseEntryAsync(SalesTaxUseEntry entry, CancellationToken cancellationToken) =>
        await db.SalesTaxUseEntries.AddAsync(entry, cancellationToken);

    public void RemoveUseEntry(SalesTaxUseEntry entry) => db.SalesTaxUseEntries.Remove(entry);

    public Task<SalesTaxAdjustment?> FindAdjustmentAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        db.SalesTaxAdjustments.FirstOrDefaultAsync(adjustment => adjustment.OrganizationId == organizationId && adjustment.Id == id, cancellationToken);

    public async Task AddAdjustmentAsync(SalesTaxAdjustment adjustment, CancellationToken cancellationToken) =>
        await db.SalesTaxAdjustments.AddAsync(adjustment, cancellationToken);

    public void RemoveAdjustment(SalesTaxAdjustment adjustment) => db.SalesTaxAdjustments.Remove(adjustment);

    public Task<SalesTaxSaleDecision?> FindDecisionAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken) =>
        db.SalesTaxSaleDecisions.FirstOrDefaultAsync(decision => decision.OrganizationId == organizationId && decision.SaleId == saleId, cancellationToken);

    public async Task AddDecisionAsync(SalesTaxSaleDecision decision, CancellationToken cancellationToken) =>
        await db.SalesTaxSaleDecisions.AddAsync(decision, cancellationToken);

    public void RemoveDecision(SalesTaxSaleDecision decision) => db.SalesTaxSaleDecisions.Remove(decision);

    public Task<Sale?> FindSaleAsync(Guid organizationId, Guid saleId, CancellationToken cancellationToken) =>
        db.Sales.FirstOrDefaultAsync(sale => sale.OrganizationId == organizationId && sale.Id == saleId, cancellationToken);

    public Task<bool> ExpenseExistsAsync(Guid organizationId, Guid expenseId, CancellationToken cancellationToken) =>
        db.Expenses.AnyAsync(expense => expense.OrganizationId == organizationId && expense.Id == expenseId, cancellationToken);

    public Task<bool> InventoryItemExistsAsync(Guid organizationId, Guid itemId, CancellationToken cancellationToken) =>
        db.InventoryItems.AnyAsync(item => item.OrganizationId == organizationId && item.Id == itemId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
