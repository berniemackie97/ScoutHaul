namespace HaulScout.Application.Organizations;

public interface IOrganizationAuthorizer
{
    Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the caller holds a permission, without refusing the request if they do not.
    ///
    /// Most endpoints are about one thing and can be gated whole. Search is not: it looks across
    /// inventory, orders, customers and purchases at once, so it has to leave out the parts the
    /// caller may not see rather than either refusing everything or returning everything.
    /// </summary>
    Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken);

    Task RequireOwner(Guid organizationId, CancellationToken cancellationToken);
}
