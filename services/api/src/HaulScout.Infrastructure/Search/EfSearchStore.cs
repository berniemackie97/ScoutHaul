using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Search;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Search;

/// <summary>
/// Search, as SQL.
///
/// Lowered and matched rather than ILike so the same query runs on any provider, which is what lets
/// the behaviour be covered without standing up a database — the Postgres translation tests then
/// confirm it survives the engine that actually serves it.
/// </summary>
public sealed class EfSearchStore(HaulScoutDbContext db) : ISearchStore
{
    public async Task<IReadOnlyList<SearchRecord>> SearchInventoryAsync(
        Guid organizationId, string term, int take, CancellationToken cancellationToken)
    {
        string lowered = term.ToLowerInvariant();

        // Archived items included on purpose: "where did that go" is exactly when one gets looked up.
        return await db.InventoryItems
            .Where(item => item.OrganizationId == organizationId)
            .Where(item =>
                item.Title.ToLower().Contains(lowered)
                || (item.Brand != null && item.Brand.ToLower().Contains(lowered))
                || (item.Model != null && item.Model.ToLower().Contains(lowered))
                || (item.Barcode != null && item.Barcode.ToLower().Contains(lowered))
                || (item.StorageLocation != null && item.StorageLocation.ToLower().Contains(lowered)))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(take)
            .Select(item => new SearchRecord(
                item.Id,
                item.Title,
                Join(item.Brand, item.Model, item.StorageLocation),
                item.Status.ToString()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchRecord>> SearchOrdersAsync(
        Guid organizationId, string term, int take, CancellationToken cancellationToken)
    {
        string lowered = term.ToLowerInvariant();

        return await db.Orders
            .Where(order => order.OrganizationId == organizationId)
            .Where(order =>
                order.OrderNumber.ToLower().Contains(lowered)
                || (order.Email != null && order.Email.ToLower().Contains(lowered))
                || (order.BuyerReference != null && order.BuyerReference.ToLower().Contains(lowered))
                || (order.ExternalOrderId != null && order.ExternalOrderId.ToLower().Contains(lowered))
                || (order.ShipToName != null && order.ShipToName.ToLower().Contains(lowered)))
            .OrderByDescending(order => order.CreatedAt)
            .Take(take)
            .Select(order => new SearchRecord(
                order.Id,
                order.OrderNumber,
                Join(order.ShipToName ?? order.Email, order.Channel, order.ExternalOrderId),
                order.Status.ToString()))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchRecord>> SearchCustomersAsync(
        Guid organizationId, string term, int take, CancellationToken cancellationToken)
    {
        string lowered = term.ToLowerInvariant();

        return await db.Customers
            .Where(customer => customer.OrganizationId == organizationId)
            .Where(customer =>
                (customer.DisplayName != null && customer.DisplayName.ToLower().Contains(lowered))
                || (customer.Email != null && customer.Email.ToLower().Contains(lowered))
                || (customer.Phone != null && customer.Phone.ToLower().Contains(lowered)))
            .OrderByDescending(customer => customer.UpdatedAt)
            .Take(take)
            .Select(customer => new SearchRecord(
                customer.Id,
                customer.DisplayName ?? customer.Email ?? "Unnamed customer",
                Join(customer.DisplayName == null ? null : customer.Email, customer.Phone),
                null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SearchRecord>> SearchPurchasesAsync(
        Guid organizationId, string term, int take, CancellationToken cancellationToken)
    {
        string lowered = term.ToLowerInvariant();

        return await db.AcquisitionLots
            .Where(lot => lot.OrganizationId == organizationId)
            .Where(lot =>
                lot.Name.ToLower().Contains(lowered)
                || (lot.SupplierName != null && lot.SupplierName.ToLower().Contains(lowered))
                || (lot.ExternalReference != null && lot.ExternalReference.ToLower().Contains(lowered)))
            .OrderByDescending(lot => lot.PurchasedAt)
            .Take(take)
            .Select(lot => new SearchRecord(
                lot.Id,
                lot.Name,
                Join(lot.SupplierName, lot.ExternalReference),
                null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The sublabel: whichever parts exist, joined, or nothing when none do.
    ///
    /// Called from the final projection, so EF selects only the columns passed in and runs this in
    /// memory afterwards — the filtering and ordering above it still happen in SQL.
    /// </summary>
    private static string? Join(string? first, string? second = null, string? third = null)
    {
        string joined = string.Join(" · ", new[] { first, second, third }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return joined.Length == 0 ? null : joined;
    }
}
