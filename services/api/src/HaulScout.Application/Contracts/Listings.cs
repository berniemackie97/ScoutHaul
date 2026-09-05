using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Channel-agnostic listing records.
//
// Title and Description are what the buyer reads on the marketplace. They are nullable because a
// listing can be created before its copy is written — the listing generator fills them in later —
// but a listing that reaches Active with neither is a listing nobody can find.

public sealed record CreateListingRequest(
    Guid InventoryItemId,
    string Platform,
    string? ListingUrl,
    decimal ListPrice,
    string? Notes,
    string? Title,
    string? Description);

public sealed record UpdateListingRequest(
    string Platform,
    string? ListingUrl,
    decimal ListPrice,
    ListingStatus Status,
    string? Notes,
    string? Title,
    string? Description);

public sealed record ListingDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    InventoryStatus InventoryItemStatus,
    string Platform,
    string? ListingUrl,
    decimal ListPrice,
    ListingStatus Status,
    DateTimeOffset? ListedAt,
    DateTimeOffset? EndedAt,
    string? Notes,
    string? Title,
    string? Description);
