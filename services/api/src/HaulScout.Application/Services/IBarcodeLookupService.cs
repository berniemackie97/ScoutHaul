using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

public interface IBarcodeLookupService
{
    Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}
