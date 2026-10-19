using HaulScout.Application.Contracts;

namespace HaulScout.Application.Services;

/// <summary>Finds resale comps for a product on the web and reads a used price range from them.</summary>
public interface IResaleEstimator
{
    bool IsConfigured { get; }

    /// <summary>The estimate, or null when there is none for any reason.</summary>
    Task<ResaleEstimate?> EstimateAsync(
        string productDescription,
        ResaleSearchHints? searchHints,
        CancellationToken cancellationToken);

    /// <summary>The estimate, or why there is none, worded for the person reading it.</summary>
    Task<ResaleSearchOutcome> SearchAsync(
        string productDescription,
        ResaleSearchHints? searchHints,
        CancellationToken cancellationToken);
}

public sealed record ResaleSearchHints(
    string SearchQuery,
    string EbaySoldCompletedUrl);

public sealed record ResaleSearchOutcome(ResaleEstimate? Estimate, string? Failure)
{
    public static ResaleSearchOutcome Found(ResaleEstimate estimate) => new(estimate, null);
    public static ResaleSearchOutcome NothingFound { get; } = new(null, null);
    public static ResaleSearchOutcome Failed(string failure) => new(null, failure);
}
