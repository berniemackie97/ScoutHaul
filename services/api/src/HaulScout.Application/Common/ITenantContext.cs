namespace HaulScout.Application.Common;

/// <summary>
/// Holds the current request's organization (tenant) so the data layer can enforce
/// org-scoping automatically via EF Core global query filters, rather than relying on
/// every query remembering to filter by <c>OrganizationId</c>.
///
/// Registered scoped (one per request). It is populated once the authenticated user's
/// organization membership has been resolved (see the current-user context). Until then
/// <see cref="OrganizationId"/> is <c>null</c>, and org-scoped queries return no rows
/// (fail closed). When no tenant service is present at all (unit tests, design-time
/// migrations) the data layer leaves queries unfiltered.
/// </summary>
public interface ITenantContext
{
    /// <summary>The resolved organization id, or <c>null</c> before resolution.</summary>
    Guid? OrganizationId { get; }

    /// <summary>True once an organization has been established for this scope.</summary>
    bool HasTenant { get; }

    /// <summary>Sets the active organization for the current scope. Called once after auth resolution.</summary>
    void SetOrganization(Guid organizationId);
}

/// <inheritdoc />
public sealed class TenantContext : ITenantContext
{
    public Guid? OrganizationId { get; private set; }

    public bool HasTenant => OrganizationId is not null;

    public void SetOrganization(Guid organizationId) => OrganizationId = organizationId;
}
