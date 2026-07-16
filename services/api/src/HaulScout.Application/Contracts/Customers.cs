using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Customers and their per-channel identities.

// ── Customers (every channel, not just the storefront) ─────────────────────

/// <summary>How a customer is known on one channel — a storefront email, or an eBay/Depop/Poshmark username.</summary>
public sealed record CustomerChannelIdentityDto(
    Guid Id,
    string Channel,
    string ExternalUserId,
    string? DisplayName,
    string? Email,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public sealed record CustomerAddressDto(
    Guid Id,
    string? Label,
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country,
    string? Phone,
    bool IsDefault);

public sealed record CustomerSummaryDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    string? Phone,
    bool AcceptsMarketing,
    decimal StoreCreditBalance,
    int OrderCount,
    decimal LifetimeSpend,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOrderAt,
    IReadOnlyList<string>? Channels = null);

public sealed record CustomerDetailDto(
    Guid Id,
    string? Email,
    string? DisplayName,
    string? Phone,
    bool AcceptsMarketing,
    decimal StoreCreditBalance,
    int OrderCount,
    decimal LifetimeSpend,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastOrderAt,
    IReadOnlyList<CustomerAddressDto> Addresses,
    IReadOnlyList<OrderSummaryDto> Orders,
    IReadOnlyList<CustomerChannelIdentityDto>? ChannelIdentities = null);
