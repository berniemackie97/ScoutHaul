using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IVisionIdentificationService
{
    bool IsConfigured { get; }

    /// <summary>
    /// Identify + value the product in the image. <paramref name="context"/> is optional known facts
    /// the user supplied (brand/condition/size/notes); null = pure visual identification.
    /// </summary>
    Task<VisionIdentifyResult> IdentifyAsync(string imageBase64, string? context, CancellationToken cancellationToken);
}
