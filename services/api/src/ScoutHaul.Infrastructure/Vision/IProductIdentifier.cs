using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Identifies a product from an image using a multimodal LLM (Gemini).
/// This is the primary identification signal — it reasons about the product
/// the way a human would, producing a specific name, brand, model, and value estimate.
/// </summary>
internal interface IProductIdentifier
{
    bool IsConfigured { get; }
    Task<ProductIdentification?> IdentifyAsync(string imageBase64, CancellationToken cancellationToken);
}

/// <summary>
/// Reverse-image web matching (Google Cloud Vision WEB_DETECTION).
/// Secondary signal — supplies real marketplace/product page URLs that the
/// LLM cannot, since it has no live web access.
/// </summary>
internal interface IWebImageMatcher
{
    bool IsConfigured { get; }
    Task<WebMatchResult> MatchAsync(string imageBase64, CancellationToken cancellationToken);
}

internal sealed record WebMatchResult(
    string? BestGuessLabel,
    IReadOnlyList<VisionWebEntity> WebEntities,
    IReadOnlyList<VisionMatchingPage> MatchingPages,
    IReadOnlyList<VisionSimilarImage> SimilarImages,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> DetectedLogos,
    string? DetectedText)
{
    public static WebMatchResult Empty { get; } = new(null, [], [], [], [], [], null);
}
