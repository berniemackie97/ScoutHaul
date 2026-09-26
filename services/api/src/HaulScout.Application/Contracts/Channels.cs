using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Channel accounts and channel listings.

// ── Channel accounts (marketplace presence foundation) ─────────────────────
public sealed record ChannelAccountDto(
    Guid Id,
    string Channel,
    string DisplayName,
    string? ExternalAccountId,
    string? DefaultFeeProfileId,
    bool IsNativelySynced,
    bool IsActive,
    DateTimeOffset? ConnectedAt,
    string? Notes,
    int ActiveListingCount);

public sealed record UpsertChannelAccountRequest(
    string Channel,
    string DisplayName,
    string? ExternalAccountId,
    string? DefaultFeeProfileId,
    bool IsActive,
    string? Notes);

public sealed record ChannelListingDto(
    Guid Id,
    Guid InventoryItemId,
    string InventoryItemTitle,
    Guid ChannelAccountId,
    string ChannelAccountName,
    string Channel,
    ChannelListingStatus Status,
    string? ExternalListingId,
    string? ExternalUrl,
    decimal? ListedPrice,
    int? Quantity,
    string? ImageHash,
    DateTimeOffset? ListedAt,
    DateTimeOffset? SoldAt,
    DateTimeOffset? LastSyncedAt,
    string? LastError);

/// <summary>Create or update the state of one item on one channel account (the channel-presence panel writes this).</summary>
public sealed record UpsertChannelListingRequest(
    Guid ChannelAccountId,
    ChannelListingStatus Status,
    decimal? ListedPrice,
    int? Quantity,
    string? ExternalListingId,
    string? ExternalUrl,
    string? ImageHash);

/// <summary>
/// A listing still live on a marketplace for an item that has already sold somewhere else. The operator takes it
/// down themselves, on the marketplace; this carries what they need to do that and to know why.
/// </summary>
public sealed record PendingDelistDto(
    Guid ChannelListingId,
    Guid InventoryItemId,
    string ItemTitle,
    string Channel,
    string? AccountName,
    string? ExternalListingId,
    string? ExternalUrl,
    decimal? ListedPrice,
    string? SoldOnChannel,
    DateTimeOffset? SoldAt);
