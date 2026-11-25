using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>Ranks the inventory items an imported marketplace listing could be, with the reasons for each score.</summary>
public sealed class ListingMatchService(IListingMatchStore store, ICurrentUserContext currentUser)
{
    private const int PoolSize = 2000;
    private const int SearchPoolSize = 300;

    public async Task<AppResult<ListingMatchDto>> RankAsync(Guid listingId, string? search, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        PlatformListing? listing = await store.FindListingAsync(organizationId, listingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<ListingMatchDto>.NotFound("Listing not found.");
        }

        string? term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        IReadOnlySet<Guid> sameListing = await store.ItemsWithExternalListingAsync(organizationId, listing.ExternalListingId, cancellationToken);
        IReadOnlyList<MatchPoolItem> pool = await store.ListPoolAsync(organizationId, term, [.. sameListing], term is null ? PoolSize : SearchPoolSize, cancellationToken);
        IReadOnlySet<Guid> elsewhere = await store.ItemsListedElsewhereOnChannelAsync(
            organizationId, listing.Platform, listing.ExternalListingId, [.. pool.Select(item => item.Id)], cancellationToken);

        MatchListing wanted = new(listing.Title, listing.Brand, listing.Size, listing.Color, listing.CategoryName, listing.Price, listing.ExternalListingId, listing.Status == PlatformListingStatus.Active);
        Dictionary<Guid, MatchPoolItem> byId = pool.ToDictionary(item => item.Id);
        IReadOnlyList<ScoredCandidate> ranked = ListingMatchScorer.Rank(
            wanted,
            pool.Select(item => new MatchCandidate(
                item.Id,
                item.Title,
                item.Brand,
                item.Category,
                item.Colors,
                item.SizeSystem,
                item.SizeValue,
                item.Price,
                InventoryLifecycle.IsSold(item.Status),
                elsewhere.Contains(item.Id),
                sameListing.Contains(item.Id))),
            take: term is null ? 6 : 12,
            floor: term is null ? 25 : 0);

        return AppResult<ListingMatchDto>.Ok(new ListingMatchDto(
            listing.Id,
            [.. ranked.Select(scored => ToDto(scored, byId[scored.Candidate.Id]))]));
    }

    private static ListingMatchCandidateDto ToDto(ScoredCandidate scored, MatchPoolItem item) => new(
        item.Id,
        item.Title,
        item.Status,
        item.Brand,
        item.Category,
        string.IsNullOrWhiteSpace(item.SizeValue) ? null : item.SizeValue,
        item.StorageLocation,
        item.Barcode,
        item.HeroPhotoId is Guid photo ? $"/api/inventory/{item.Id}/photos/{photo}/thumbnail" : null,
        scored.Score,
        scored.Score >= ListingMatchScorer.Strong ? "Strong" : scored.Score >= ListingMatchScorer.Likely ? "Likely" : "Weak",
        [.. scored.Signals.Select(signal => new ListingMatchSignalDto(signal.Kind, signal.Verdict, signal.Text))]);
}
