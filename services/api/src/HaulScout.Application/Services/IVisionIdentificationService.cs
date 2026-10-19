using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IVisionIdentificationService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Identifies a product from a photo, typed facts, or both. Typed facts are treated as ground truth
    /// and the photo pins the variant and condition.
    /// </summary>
    Task<VisionIdentifyResult> IdentifyAsync(
        string? imageBase64,
        string? imageMediaType,
        string? context,
        CancellationToken cancellationToken);
}
