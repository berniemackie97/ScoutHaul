using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IVisionIdentificationService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Identify + value a product. Provide a photo (<paramref name="imageBase64"/>), typed item facts
    /// (<paramref name="context"/>), or both — at least one is required. Photo only = pure visual
    /// identification; facts only = identification from the text (no photo); both = the facts are ground
    /// truth and the photo refines the variant/condition. The prompt adapts to what is given.
    /// </summary>
    Task<VisionIdentifyResult> IdentifyAsync(string? imageBase64, string? context, CancellationToken cancellationToken);
}
