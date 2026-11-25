using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Inventory;

/// <summary>An inventory item as the matcher reads it.</summary>
public sealed record MatchPoolItem(
    Guid Id,
    string Title,
    string? Brand,
    string? Category,
    IReadOnlyList<string> Colors,
    string? SizeSystem,
    string? SizeValue,
    decimal? Price,
    InventoryStatus Status,
    string? StorageLocation,
    string? Barcode,
    Guid? HeroPhotoId);

public interface IListingMatchStore
{
    /// <summary>Imported listings still waiting to be matched to an item.</summary>
    Task<int> CountPendingAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<PlatformListing?> FindListingAsync(Guid organizationId, Guid listingId, CancellationToken cancellationToken);

    /// <summary>
    /// Unarchived items to score: the most recently touched when <paramref name="search"/> is empty, else those whose
    /// title, brand, barcode or bin contains it. Plus any item in <paramref name="alwaysInclude"/>.
    /// </summary>
    Task<IReadOnlyList<MatchPoolItem>> ListPoolAsync(Guid organizationId, string? search, IReadOnlyCollection<Guid> alwaysInclude, int take, CancellationToken cancellationToken);

    /// <summary>Items a channel listing with this external id already points at.</summary>
    Task<IReadOnlySet<Guid>> ItemsWithExternalListingAsync(Guid organizationId, string externalListingId, CancellationToken cancellationToken);

    /// <summary>Of the given items, those already listed on <paramref name="channel"/> under a different external id.</summary>
    Task<IReadOnlySet<Guid>> ItemsListedElsewhereOnChannelAsync(Guid organizationId, string channel, string externalListingId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
}
