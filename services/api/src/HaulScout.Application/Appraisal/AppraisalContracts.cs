namespace HaulScout.Application.Appraisal;

public enum IdentificationSource
{
    /// <summary>A product the business already has on file for this barcode.</summary>
    Catalog,

    /// <summary>A public barcode registry.</summary>
    Barcode,
    Photo,
    Details,
}

public sealed record EstimateDto(decimal Low, decimal High, string Confidence, string? Rationale, bool IsWide);

public sealed record IdentificationDto(
    string Title,
    string? Brand,
    string? Model,
    string? Category,
    string? Color,
    string? Condition,
    string? ConditionGrade,
    string? Barcode,
    IReadOnlyList<string> Identifiers,
    IdentificationCertainty Certainty,
    IdentificationSource Source,
    Guid? ProductIdentityId,
    string? ImageUrl,
    IReadOnlyList<string> MissingInformation,
    EstimateDto? Estimate);

/// <summary>A photo as it arrived: bytes plus the type its signature says it is.</summary>
public sealed record AppraisalPhoto(byte[] Bytes, string MediaType);

public sealed record IdentifyInput(AppraisalPhoto? Photo, string? Barcode, string? Details);

public sealed record AppraiseRequest(
    string Title,
    string? Brand = null,
    string? Model = null,
    string? Category = null,
    string? Color = null,
    string? Condition = null,
    string? Barcode = null,
    IReadOnlyList<string>? Identifiers = null,
    IdentificationCertainty? Certainty = null,
    Guid? ProductIdentityId = null,
    EstimateDto? Estimate = null,
    /// <summary>An item in stock: the appraisal is kept in its history.</summary>
    Guid? InventoryItemId = null);

public sealed record PriceDto(
    PriceBasis Basis,
    decimal? QuickSale,
    decimal? List,
    decimal? High,
    int Confidence,
    ConfidenceLevel Level,
    IReadOnlyList<string> Reasons);

public sealed record CompDto(
    CompKind Kind,
    string Source,
    string Title,
    decimal Price,
    decimal? Shipping,
    string? Condition,
    string? Url,
    DateTimeOffset? Date,
    bool Verified,
    bool Included,
    string? ExcludedBecause);

public sealed record CompSourceReportDto(string Name, CompSourceStatus Status, int Count, string? Message);

public sealed record CompSearchLinkDto(string Label, CompKind Kind, string Url);

public sealed record WebSourceDto(string Url, string? Title);

public sealed record AppraisalDto(
    string SearchText,
    PriceDto Price,
    IReadOnlyList<CompDto> Comps,
    EstimateDto? Estimate,
    IReadOnlyList<CompSourceReportDto> Sources,
    IReadOnlyList<CompSearchLinkDto> SearchLinks,
    IReadOnlyList<WebSourceDto> WebSources,
    DateTimeOffset AppraisedAt);
