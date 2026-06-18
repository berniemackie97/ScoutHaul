using Microsoft.Extensions.Logging;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Marketplace;

internal sealed class CompositeBarcodeLookupService(
    IEnumerable<IBarcodeLookupProvider> providers,
    ILogger<CompositeBarcodeLookupService> logger) : IBarcodeLookupService
{
    public async Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        string cleanBarcode = barcode.Trim();
        if (string.IsNullOrWhiteSpace(cleanBarcode))
        {
            return null;
        }

        foreach (IBarcodeLookupProvider provider in providers.Where(provider => provider.IsConfigured))
        {
            try
            {
                BarcodeLookupResult? result = await provider.LookupAsync(cleanBarcode, cancellationToken);
                if (result is not null && !string.IsNullOrWhiteSpace(result.Title))
                {
                    return result;
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "{Provider} barcode lookup failed for {Barcode}", provider.Name, cleanBarcode);
            }
        }

        return null;
    }
}
