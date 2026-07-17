using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sales;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Services;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Sales;

public sealed class EfSalesChannelStore(HaulScoutDbContext db, ICurrentUserContext currentUser) : ISalesChannelStore
{
    public async Task<IReadOnlyList<string>> ListExistingChannelsAsync(CancellationToken cancellationToken)
    {
        List<string> saleChannels = await db.Sales
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<string> listingChannels = await db.Listings
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Platform)
            .Distinct()
            .ToListAsync(cancellationToken);

        List<string> accountChannels = await db.ChannelAccounts
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId)
            .Select(item => item.Channel)
            .Distinct()
            .ToListAsync(cancellationToken);

        return saleChannels.Concat(listingChannels).Concat(accountChannels).ToArray();
    }

    public async Task<IReadOnlyList<SaleCandidateDto>> ListSaleCandidatesAsync(CancellationToken cancellationToken) =>
        await db.InventoryItems
            .AsNoTracking()
            .Where(item => item.OrganizationId == currentUser.OrganizationId
                && !item.IsArchived
                && item.Status != InventoryStatus.Bundled
                && !item.Sales.Any(sale => !SaleWorkflow.InventoryReleaseStatuses.Contains(sale.Status)))
            .OrderByDescending(item => item.UpdatedAt)
            .Take(500)
            .Select(item => new SaleCandidateDto(
                item.Id,
                item.Title,
                item.Status,
                item.PurchasePrice ?? 0,
                item.EstimatedSalePrice ?? item.SellerAskingPrice,
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => (Guid?)listing.Id)
                    .FirstOrDefault(),
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => listing.Platform)
                    .FirstOrDefault(),
                item.Listings
                    .Where(listing => listing.Status == ListingStatus.Active || listing.Status == ListingStatus.Draft)
                    .OrderByDescending(listing => listing.UpdatedAt)
                    .Select(listing => (decimal?)listing.ListPrice)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
}
