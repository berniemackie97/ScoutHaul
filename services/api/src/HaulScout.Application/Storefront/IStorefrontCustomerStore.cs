using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

public interface IStorefrontCustomerStore
{
    /// <summary>
    /// Resolves a public buyer account into the storefront Customer table for this organization. This never creates
    /// an operator AppUser or organization membership.
    /// </summary>
    Task<Customer> GetOrCreateAsync(
        Guid organizationId,
        StorefrontCustomerIdentity identity,
        DateTimeOffset seenAt,
        CancellationToken cancellationToken);
}
