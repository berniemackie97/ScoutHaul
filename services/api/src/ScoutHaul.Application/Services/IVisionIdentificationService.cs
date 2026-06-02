using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public interface IVisionIdentificationService
{
    bool IsConfigured { get; }
    Task<VisionIdentifyResult> IdentifyAsync(string imageBase64, CancellationToken cancellationToken);
}
