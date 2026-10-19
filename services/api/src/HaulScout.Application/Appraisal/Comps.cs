namespace HaulScout.Application.Appraisal;

public enum CompKind
{
    /// <summary>The business sold a matching item.</summary>
    YourSale,

    /// <summary>A realised sale on a marketplace.</summary>
    Sold,

    /// <summary>A current listing: what sellers want, not what buyers paid.</summary>
    Asking,

    /// <summary>A price guide's figure for a condition, built from many sales.</summary>
    Guide,
}

public enum PriceBasis
{
    Sales,
    Guide,
    Asking,
    Estimate,
    None,
}

/// <summary>
/// One piece of price evidence. <see cref="Verified"/> is false when the comp was found by web
/// search and read by a model rather than taken from a marketplace, the business's own sales, or a
/// person who looked at the listing.
/// </summary>
public sealed record Comp(
    CompKind Kind,
    string Source,
    string Title,
    decimal Price,
    decimal? Shipping,
    string? Condition,
    string? Url,
    DateTimeOffset? Date,
    bool Verified);

/// <summary>A model's own range for the item, used only when there is no evidence to price from.</summary>
public sealed record ModelEstimate(decimal Low, decimal High, string? Rationale, string Confidence, bool IsWide = false);

/// <summary>The item being priced, as far as it has been identified.</summary>
public sealed record ItemFacts(
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Color,
    string? Condition,
    IReadOnlyList<string> Identifiers,
    IdentificationCertainty Certainty);

public enum IdentificationCertainty
{
    High,
    Medium,
    Low,
}

public sealed record AssessedComp(Comp Comp, bool Included, string? ExcludedBecause);

public sealed record PriceResult(
    PriceBasis Basis,
    decimal? QuickSale,
    decimal? List,
    decimal? High,
    int Confidence,
    ConfidenceLevel Level,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<AssessedComp> Comps);

public enum ConfidenceLevel
{
    None,
    Guess,
    Thin,
    Fair,
    Strong,
}
