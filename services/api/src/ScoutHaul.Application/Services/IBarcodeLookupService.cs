using ScoutHaul.Application.Contracts;

namespace ScoutHaul.Application.Services;

public interface IBarcodeLookupService
{
    Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}
