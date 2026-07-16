using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Channel-agnostic listing records.

public sealed record CreateListingRequest(Guid InventoryItemId, string Platform, string? ListingUrl, decimal ListPrice, string? Notes);
public sealed record UpdateListingRequest(string Platform, string? ListingUrl, decimal ListPrice, ListingStatus Status, string? Notes);
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
    string? Notes);
