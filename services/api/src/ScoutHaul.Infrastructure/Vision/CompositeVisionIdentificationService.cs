using Microsoft.Extensions.Logging;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Infrastructure.Vision;

/// <summary>
/// Orchestrates product identification. Gemini (multimodal reasoning) is the primary
/// identifier; Google Cloud Vision reverse-image matching is an optional enrichment that
/// adds real product/marketplace page URLs. Both providers hit independent HTTP endpoints,
/// so they run in parallel.
/// </summary>
internal sealed class CompositeVisionIdentificationService(
    IProductIdentifier productIdentifier,
    IWebImageMatcher webMatcher,
    ILogger<CompositeVisionIdentificationService> logger) : IVisionIdentificationService
{
    public bool IsConfigured => productIdentifier.IsConfigured || webMatcher.IsConfigured;

    public async Task<VisionIdentifyResult> IdentifyAsync(string imageBase64, CancellationToken cancellationToken)
    {
        // Run both providers in parallel — independent external APIs, no shared state.
        Task<ProductIdentification?> productTask = productIdentifier.IsConfigured
            ? productIdentifier.IdentifyAsync(imageBase64, cancellationToken)
            : Task.FromResult<ProductIdentification?>(null);

        Task<WebMatchResult> matchTask = webMatcher.IsConfigured
            ? webMatcher.MatchAsync(imageBase64, cancellationToken)
            : Task.FromResult(WebMatchResult.Empty);

        await Task.WhenAll(productTask, matchTask);

        ProductIdentification? product = await productTask;
        WebMatchResult match = await matchTask;

        string? searchQuery = BuildSearchQuery(product, match);

        logger.LogInformation(
            "Vision identify: product={Product} confidence={Confidence} webPages={Pages} query={Query}",
            product?.Name ?? "(none)", product?.Confidence ?? "n/a", match.MatchingPages.Count, searchQuery ?? "(none)");

        return new VisionIdentifyResult(
            BestGuessLabel: product?.Name ?? match.BestGuessLabel,
            WebEntities: match.WebEntities,
            MatchingPages: match.MatchingPages,
            SimilarImages: match.SimilarImages,
            Labels: match.Labels,
            DetectedLogos: match.DetectedLogos,
            DetectedText: product?.IdentifyingText ?? match.DetectedText,
            SearchQuery: searchQuery,
            Product: product);
    }

    /// <summary>
    /// Best search query for marketplace comp lookups. Priority:
    /// Gemini brand+model → Gemini name → first keyword → web best-guess → web entities.
    /// </summary>
    private static string? BuildSearchQuery(ProductIdentification? product, WebMatchResult match)
    {
        if (product is not null)
        {
            if (!string.IsNullOrWhiteSpace(product.Brand) && !string.IsNullOrWhiteSpace(product.Model))
            {
                return $"{product.Brand} {product.Model}".Trim();
            }
            if (!string.IsNullOrWhiteSpace(product.Name))
            {
                return product.Name;
            }
            if (product.SearchKeywords.Count > 0)
            {
                return product.SearchKeywords[0];
            }
        }

        if (!string.IsNullOrWhiteSpace(match.BestGuessLabel))
        {
            return match.BestGuessLabel;
        }

        if (match.WebEntities.Count > 0)
        {
            return string.Join(" ", match.WebEntities
                .OrderByDescending(e => e.Score)
                .Take(2)
                .Select(e => e.Description));
        }

        return null;
    }
}
