namespace HaulScout.Application.Storefront;

/// <summary>
/// Which store a public request belongs to. The storefront identifies itself by domain (or a raw org id for
/// tooling); resolving that to an organization is a lookup, not a decision, so it sits behind a port and the
/// web layer never touches persistence for it.
/// </summary>
public interface IStoreDirectory
{
    /// <summary>Resolves a raw store header value — a domain like "overhaultrading.com" or an org id — or null.</summary>
    Task<Guid?> ResolveOrganizationIdAsync(string rawStoreValue, CancellationToken cancellationToken);
}
