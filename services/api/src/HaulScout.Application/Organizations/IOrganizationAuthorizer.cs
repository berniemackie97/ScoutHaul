namespace HaulScout.Application.Organizations;

public interface IOrganizationAuthorizer
{
    Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken);

    Task RequireOwner(Guid organizationId, CancellationToken cancellationToken);
}
