namespace HaulScout.Application.Contracts;

/// <summary>
/// One thing the operator might be looking for.
///
/// Deliberately flat and stringly-typed rather than a union of every DTO: a search result only has
/// to be recognisable and openable. Sending whole entities would cost far more and say less.
/// </summary>
/// <param name="Kind">Which list it belongs to — "inventory", "order", "customer", "purchase".</param>
/// <param name="Label">What the operator would call it: an item's title, an order's number.</param>
/// <param name="Sublabel">The line underneath — enough to tell two similar results apart.</param>
/// <param name="Badge">A short status word, where the thing has one.</param>
public sealed record SearchHitDto(
    string Kind,
    Guid Id,
    string Label,
    string? Sublabel,
    string? Badge);

/// <param name="Groups">Only the kinds the caller is allowed to see, and only those with matches.</param>
/// <param name="Truncated">True when a group hit its limit, so the UI can say "keep typing" rather than imply this is all of it.</param>
public sealed record SearchGroupDto(string Kind, string Label, IReadOnlyList<SearchHitDto> Hits, bool Truncated);

public sealed record SearchResultsDto(string Query, IReadOnlyList<SearchGroupDto> Groups, int TotalHits);
