using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// The payload a client needs on sign-in.

public sealed record SessionUserDto(Guid Id, string Email, string DisplayName);
public sealed record SessionRemoteCountsDto(
    int InventoryItems,
    int Budgets,
    int MarketObservations,
    int Listings,
    int Tasks,
    int PendingSyncOperations);
/// <param name="Permissions">What the caller may do in the selected organization, so a client offers only what will work.</param>
public sealed record SessionBootstrapDto(
    SessionUserDto User,
    IReadOnlyList<OrganizationDto> Organizations,
    Guid? SelectedOrganizationId,
    SessionRemoteCountsDto RemoteCounts,
    DateTimeOffset ServerTime,
    IReadOnlyList<string> Permissions);
