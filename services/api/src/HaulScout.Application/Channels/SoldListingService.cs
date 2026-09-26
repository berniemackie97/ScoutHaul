using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Channels;

/// <summary>
/// Sales the books have not heard about. Poshmark and Depop hand over no sales, so a listing turning sold is the
/// whole of the news from them. Recording it stays the operator's call — only they know the fees.
/// </summary>
public sealed class SoldListingService(ISoldListingStore store, ICurrentUserContext currentUser, TimeProvider clock)
{
    private const int MostToShow = 200;

    public async Task<IReadOnlyList<SoldNotRecordedDto>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<PlatformListing> sold = await store.ListSoldUnrecordedAsync(currentUser.OrganizationId, MostToShow, cancellationToken);
        return sold
            .Select(listing => new SoldNotRecordedDto(
                listing.Id,
                listing.ReconciledInventoryItemId!.Value,
                listing.ReconciledInventoryItem?.Title ?? listing.Title,
                listing.Platform,
                listing.ExternalListingId,
                listing.Url,
                listing.Price,
                listing.MarkedSoldAt))
            .ToArray();
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        store.CountSoldUnrecordedAsync(currentUser.OrganizationId, cancellationToken);

    /// <summary>Settles one without a sale: recorded elsewhere, or never sold. The listing itself is left alone.</summary>
    public async Task<AppResult<SoldNotRecordedDto>> AcknowledgeAsync(Guid platformListingId, CancellationToken cancellationToken)
    {
        PlatformListing? listing = await store.FindAsync(currentUser.OrganizationId, platformListingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<SoldNotRecordedDto>.NotFound("That listing was not found.");
        }

        if (listing.SoldAcknowledgedAt is not null)
        {
            return AppResult<SoldNotRecordedDto>.Validation("That listing has already been dealt with. Reload to see where it stands.");
        }

        listing.SoldAcknowledgedAt = clock.GetUtcNow();
        await store.SaveChangesAsync(cancellationToken);
        return AppResult<SoldNotRecordedDto>.Ok(new SoldNotRecordedDto(
            listing.Id,
            listing.ReconciledInventoryItemId ?? Guid.Empty,
            listing.ReconciledInventoryItem?.Title ?? listing.Title,
            listing.Platform,
            listing.ExternalListingId,
            listing.Url,
            listing.Price,
            listing.MarkedSoldAt));
    }
}
