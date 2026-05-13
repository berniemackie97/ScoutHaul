using ScoutHaul.Api.Authorization;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Services;

public sealed class OrganizationRoleSeeder(ScoutHaulDbContext db)
{
    public OrganizationRoleDefinition EnsureOwnerRole(Guid organizationId)
    {
        OrganizationRoleDefinition? ownerRole = db.OrganizationRoles
            .FirstOrDefault(item => item.OrganizationId == organizationId && item.IsOwnerRole);

        if (ownerRole is not null)
        {
            return ownerRole;
        }

        ownerRole = new OrganizationRoleDefinition
        {
            OrganizationId = organizationId,
            Name = "Owner",
            Description = "Full workspace control, including owners, billing, roles, teams, and business data.",
            IsSystem = true,
            IsOwnerRole = true
        };

        foreach (string permission in ScoutHaulPermissions.All)
        {
            ownerRole.Permissions.Add(new OrganizationRolePermission { Permission = permission });
        }

        db.OrganizationRoles.Add(ownerRole);
        return ownerRole;
    }
}
