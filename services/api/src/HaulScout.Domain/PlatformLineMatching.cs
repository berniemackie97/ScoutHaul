namespace HaulScout.Domain;

/// <summary>
/// How an imported platform line was tied to an inventory item. The distinction that matters is
/// <see cref="IsExact"/>: an identifier match is a fact, a title match is a guess.
/// </summary>
public enum PlatformLineMatchSource
{
    /// <summary>The platform's own item number resolved to an item we have listed. Exact.</summary>
    PlatformItemId,

    /// <summary>The line's SKU equals an inventory item's SKU. Exact.</summary>
    Sku,

    /// <summary>Closest inventory title above the similarity threshold. A guess — never settled silently.</summary>
    Title,
}

/// <param name="AlreadySold">The item is already sold; matching it again usually means a duplicate.</param>
public sealed record PlatformLineMatch(Guid InventoryItemId, PlatformLineMatchSource Source, bool AlreadySold)
{
    /// <summary>
    /// True when the match came from an identifier rather than a resemblance. Only exact matches may
    /// be settled without a person looking: a wrong title guess would post real money against the
    /// wrong item, and a silently wrong sale is worse than a queue entry.
    /// </summary>
    public bool IsExact => Source is PlatformLineMatchSource.PlatformItemId or PlatformLineMatchSource.Sku;

    /// <summary>Safe to reconcile without asking — exact, and not pointing at something already sold.</summary>
    public bool CanAutoSettle => IsExact && !AlreadySold;
}

/// <summary>
/// What is known about an imported line that could identify it. <see cref="Platform"/> is part of it
/// because an item number only means something on the marketplace that issued it: eBay and Vinted
/// both number their listings, and nothing stops the same digits appearing on both.
/// </summary>
public readonly record struct PlatformLineIdentity(string Platform, string? ExternalItemId, string? Sku, string Title);

/// <summary>An inventory item as a match target.</summary>
public readonly record struct InventoryMatchCandidate(Guid Id, string Title, string? Sku, bool AlreadySold);

/// <summary>
/// Resolves an imported platform line to an inventory item by trying every signal we hold, strongest
/// first. One chain, so orders and listings match identically instead of each growing their own
/// partial version — the divergence that made order reconciliation depend on having reconciled the
/// listing import first.
/// </summary>
public static class PlatformLineMatcher
{
    /// <summary>
    /// The best available match, or null when nothing is confident enough to offer.
    /// </summary>
    /// <param name="itemIdToInventoryId">
    /// Listing → inventory item, from listings already tied to an item (imported or channel records),
    /// keyed by <see cref="ListingKey"/>. Supplied rather than searched so the caller can batch one
    /// lookup per import.
    /// </param>
    /// <summary>
    /// How a listing is looked up: its marketplace and its number together. Null when either is
    /// missing, which is simply a line that cannot be matched this way.
    /// </summary>
    public static string? ListingKey(string? platform, string? externalListingId) =>
        string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(externalListingId)
            ? null
            : $"{platform.Trim().ToLowerInvariant()}\u0000{externalListingId.Trim().ToLowerInvariant()}";

    public static PlatformLineMatch? Match(
        PlatformLineIdentity line,
        IReadOnlyDictionary<string, Guid> itemIdToInventoryId,
        IReadOnlyList<InventoryMatchCandidate> candidates)
    {
        // 1. The platform's own item number — the strongest signal, because the seller listed it.
        //    Only on that platform: another marketplace's listing with the same number is a different
        //    thing entirely, and matching across would post a sale against the wrong item.
        if (ListingKey(line.Platform, line.ExternalItemId) is string key
            && itemIdToInventoryId.TryGetValue(key, out Guid byItemId))
        {
            return Build(byItemId, PlatformLineMatchSource.PlatformItemId, candidates);
        }

        // 2. SKU — an operator-assigned code, exact when both sides carry one.
        if (!string.IsNullOrWhiteSpace(line.Sku))
        {
            List<InventoryMatchCandidate> bySku = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Sku)
                    && string.Equals(candidate.Sku, line.Sku, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Only when it is unambiguous. A SKU on two items is a data problem, not a match.
            if (bySku.Count == 1)
            {
                return Build(bySku[0].Id, PlatformLineMatchSource.Sku, candidates);
            }
        }

        // 3. Title resemblance — offered as a suggestion, never settled on its own.
        Guid? byTitle = TitleMatch.Best(line.Title, candidates.Select(candidate => (candidate.Id, candidate.Title)));
        return byTitle is Guid titleId ? Build(titleId, PlatformLineMatchSource.Title, candidates) : null;
    }

    private static PlatformLineMatch? Build(
        Guid id,
        PlatformLineMatchSource source,
        IReadOnlyList<InventoryMatchCandidate> candidates)
    {
        foreach (InventoryMatchCandidate candidate in candidates)
        {
            if (candidate.Id == id)
            {
                return new PlatformLineMatch(id, source, candidate.AlreadySold);
            }
        }

        // The identifier pointed at an item that is not a candidate (archived, or another org's).
        // Offering it would be a dead end, so there is no match.
        return null;
    }
}
