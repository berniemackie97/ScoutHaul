using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>
/// Estimates a used-resale price range for a product from its text description, when no live
/// marketplace comps are available. Lets barcode and keyword lookups surface a value without
/// any paid pricing provider.
/// </summary>
public interface IResaleEstimator
{
    bool IsConfigured { get; }
    Task<ResaleEstimate?> EstimateAsync(string productDescription, CancellationToken cancellationToken);
}
