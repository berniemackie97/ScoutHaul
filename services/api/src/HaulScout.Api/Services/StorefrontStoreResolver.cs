using HaulScout.Application.Common;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Services;

/// <summary>
/// Resolves which store an anonymous storefront request belongs to from the X-HaulScout-Store header and scopes the
/// request's tenant to it. Pure HTTP-to-application glue: header extraction here, the lookup behind the port.
/// </summary>
public sealed class StorefrontStoreResolver(IStoreDirectory directory, ITenantContext tenant)
{
    public const string StoreHeader = "X-HaulScout-Store";

    public async Task<Guid?> ResolveAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        string? raw = request.Headers[StoreHeader].FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        Guid? organizationId = await directory.ResolveOrganizationIdAsync(raw, cancellationToken);
        if (organizationId is Guid resolved)
        {
            tenant.SetOrganization(resolved);
        }

        return organizationId;
    }
}
