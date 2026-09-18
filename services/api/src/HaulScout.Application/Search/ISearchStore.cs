namespace HaulScout.Application.Search;

/// <summary>
/// Search's persistence port.
///
/// The Application states which records a term should reach and how many of them to bring back;
/// Infrastructure decides how that becomes SQL. Each method returns the flat hit shape rather than
/// entities, because a search result only has to be recognisable and openable — hydrating whole
/// aggregates to render one line each would cost far more and say less.
/// </summary>
public interface ISearchStore
{
    /// <summary>
    /// Items matching on title, brand, model, barcode or storage location.
    ///
    /// Archived items are included: "where did that go" is exactly the moment someone looks one up.
    /// </summary>
    Task<IReadOnlyList<SearchRecord>> SearchInventoryAsync(Guid organizationId, string term, int take, CancellationToken cancellationToken);

    /// <summary>Orders matching on number, email, buyer reference, marketplace id or ship-to name.</summary>
    Task<IReadOnlyList<SearchRecord>> SearchOrdersAsync(Guid organizationId, string term, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchRecord>> SearchCustomersAsync(Guid organizationId, string term, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchRecord>> SearchPurchasesAsync(Guid organizationId, string term, int take, CancellationToken cancellationToken);
}

/// <summary>
/// One match, already reduced to what a result line shows.
/// </summary>
/// <param name="Label">What the operator would call it: an item's title, an order's number.</param>
/// <param name="Sublabel">The line underneath — enough to tell two similar results apart.</param>
/// <param name="Badge">A short status word, where the record has one.</param>
public sealed record SearchRecord(Guid Id, string Label, string? Sublabel, string? Badge);
