using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Resolves which store an anonymous storefront request belongs to from the X-HaulScout-Store header (domain, or a
/// raw org id for tooling), and scopes the request's tenant to it. The same semantics as the catalog controller —
/// extracted so the commerce controllers don't re-implement them.
/// </summary>
public sealed class StorefrontStoreResolver(HaulScoutDbContext db, ITenantContext tenant)
{
    public const string StoreHeader = "X-HaulScout-Store";

    public async Task<Guid?> ResolveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        string? raw = request.Headers[StoreHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        Guid organizationId;
        if (Guid.TryParse(raw, out Guid id))
        {
            organizationId = await db.Organizations
                .Where(org => org.Id == id)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            string domain = NormalizeDomain(raw);
            organizationId = await db.Organizations
                .Where(org => org.StoreDomain != null && org.StoreDomain.ToLower() == domain)
                .Select(org => org.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (organizationId == Guid.Empty)
        {
            return null;
        }

        tenant.SetOrganization(organizationId);
        return organizationId;
    }

    private static string NormalizeDomain(string value)
    {
        string domain = value.ToLowerInvariant();
        if (domain.StartsWith("www.", StringComparison.Ordinal))
        {
            domain = domain[4..];
        }

        int colon = domain.IndexOf(':');
        return colon >= 0 ? domain[..colon] : domain;
    }
}
