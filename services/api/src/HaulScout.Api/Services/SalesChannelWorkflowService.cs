using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Read-side orchestration for sale channel reference data and sale-entry helpers.
/// Controllers should not know how legacy channels, fee profiles, and sale candidates are discovered.
/// </summary>
public sealed class SalesChannelWorkflowService(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    public async Task<SalesReferenceDataDto> ReferenceDataAsync(CancellationToken cancellationToken)
    {
        List<string> existing = await ExistingChannelsAsync(cancellationToken);
        List<SaleChannelOptionDto> channels = SalesChannelCatalog.Channels.ToList();
        channels.AddRange(existing
            .Where(value => channels.All(option => !string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
            .Select(value => new SaleChannelOptionDto(value, $"{value} (legacy — standardize)", "Legacy data", true, [])));
        return new SalesReferenceDataDto(channels);
    }

    public async Task<string?> CanonicalChannelAsync(string? channel, CancellationToken cancellationToken) =>
        SalesChannelCatalog.CanonicalValue(channel, await ExistingChannelsAsync(cancellationToken));

    public async Task<AppResult<ChannelFeeEstimateDto>> EstimateFeesAsync(
        EstimateChannelFeesRequest request,
        CancellationToken cancellationToken)
    {
        string? channel = await CanonicalChannelAsync(request.Platform, cancellationToken);
        if (channel is null)
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Select a controlled sales channel.");
        }

        if (request.SoldPrice < 0)
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Sold price cannot be negative.");
        }

        ChannelFeeProfileDto? profile = SalesChannelCatalog.FindProfile(channel, request.FeeProfileId);
        if (profile is null
            || (!string.IsNullOrWhiteSpace(request.FeeProfileId)
                && !string.Equals(profile.Id, request.FeeProfileId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return AppResult<ChannelFeeEstimateDto>.Validation("Select a valid fee profile for this sales channel.");
        }

        return AppResult<ChannelFeeEstimateDto>.Ok(ChannelFeeCalculator.Estimate(channel, profile, request.SoldPrice));
    }

    public async Task<IReadOnlyList<SaleCandidateDto>> CandidatesAsync(CancellationToken cancellationToken) =>
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

    public async Task<List<string>> ExistingChannelsAsync(CancellationToken cancellationToken)
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

        return saleChannels
            .Concat(listingChannels)
            .Concat(accountChannels)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
