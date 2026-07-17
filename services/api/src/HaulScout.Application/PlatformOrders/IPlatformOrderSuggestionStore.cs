namespace HaulScout.Application.PlatformOrders;

public interface IPlatformOrderSuggestionStore
{
    Task<IReadOnlyList<PlatformOrderInventorySuggestionCandidate>> ListInventoryCandidatesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformOrderListingSuggestionCandidate>> ListListingCandidatesAsync(
        IReadOnlyList<string> externalListingIds,
        CancellationToken cancellationToken);
}
