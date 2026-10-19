namespace HaulScout.Application.Appraisal;

public sealed record CompQuery(
    Guid OrganizationId,
    ItemFacts Item,
    string SearchText,
    string? Barcode,
    Guid? ProductIdentityId);

public sealed record CompSourceResult(
    IReadOnlyList<Comp> Comps,
    ModelEstimate? Estimate = null,
    IReadOnlyList<WebSourceDto>? WebSources = null)
{
    public static readonly CompSourceResult Empty = new([]);
}

public enum CompSourceStatus
{
    Found,
    Empty,
    Failed,
    Off,
}

/// <summary>A failure a source can describe to the user, such as a spent daily quota.</summary>
public sealed class CompSourceUnavailableException(string message) : Exception(message);

/// <summary>Somewhere comps come from. Each runs on its own deadline; one failing never sinks the others.</summary>
public interface ICompSource
{
    string Name { get; }
    bool IsConfigured { get; }
    TimeSpan Deadline { get; }
    Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken);
}

public sealed record CatalogProduct(Guid? ProductIdentityId, string Title, string? Brand, string? Model, string? Category, bool UserVerified);

public interface IProductCatalogStore
{
    Task<CatalogProduct?> FindByBarcodeAsync(Guid organizationId, string barcode, CancellationToken cancellationToken);
}
