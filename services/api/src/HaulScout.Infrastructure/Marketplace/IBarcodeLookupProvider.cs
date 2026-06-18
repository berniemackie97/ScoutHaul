using HaulScout.Application.Contracts;

namespace HaulScout.Infrastructure.Marketplace;

internal interface IBarcodeLookupProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken);
}
