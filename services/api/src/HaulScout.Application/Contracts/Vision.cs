using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// AI identification and listing generation.

// ── Vision Identification ────────────────────────────────────────

public sealed record VisionIdentifyRequest(
    string? ImageBase64,
    string? ImageUrl,
    // Optional known facts the user already entered (brand, condition, size, notes, …). Fed to the
    // model to sharpen identification + valuation. Null/absent → behaves exactly as before (so older
    // clients that don't send it are unaffected).
    string? Context = null);

public sealed record VisionIdentifyResult(
    ProductIdentification? Product,
    string? SearchQuery,
    string? DetectedText);

/// <summary>
/// Structured product identification + valuation from Gemini — the sole identifier/appraiser for photo or typed details.
/// </summary>
public sealed record ProductIdentification(
    string? Name,
    string? Brand,
    string? Model,
    string? Category,
    string? Color,
    string? Condition,
    decimal? EstimatedResaleLow,
    decimal? EstimatedResaleHigh,
    string? IdentifyingText,
    IReadOnlyList<string> SearchKeywords,
    string Confidence, // identification (variant) confidence — kept name for compatibility
    string? ValuationConfidence = null,
    string? PriceRangeType = null, // tight | moderate | wide
    IReadOnlyList<string>? KeyValueFactors = null,
    IReadOnlyList<string>? MissingInformation = null,
    IReadOnlyList<string>? RecommendedNextSteps = null,
    string? Rationale = null,
    IReadOnlyList<ResaleComp>? Comps = null,
    // Rough condition-quality rating inferred from the photo: new | like-new | good | fair | poor |
    // for-parts. Distinct from the free-text Condition. Additive — null when not assessed.
    string? ConditionQuality = null);

public sealed record GenerateListingRequest(
    string Title,
    string? Brand = null,
    string? Model = null,
    string? Category = null,
    string? Condition = null,
    string? Color = null,
    string? SizeSystem = null,
    string? SizeValue = null,
    string? AttributesJson = null,
    string? Notes = null);

/// <summary>
/// A ready-to-post listing generated from an item's details: title + description copy, plus a
/// competitive suggested list price backed by real sold/active comps and the resale range.
/// </summary>
public sealed record GeneratedListingDto(
    string ListingTitle,
    string ListingDescription,
    decimal? SuggestedListPrice,
    decimal? EstimatedResaleLow,
    decimal? EstimatedResaleHigh,
    string? PriceRationale,
    IReadOnlyList<ResaleComp> Comps);
