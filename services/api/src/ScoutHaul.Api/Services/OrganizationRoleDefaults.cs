using ScoutHaul.Api.Authorization;
using ScoutHaul.Domain.Entities;

namespace ScoutHaul.Api.Services;

public static class OrganizationRoleDefaults
{
    public static OrganizationRoleDefinition CreateOwnerRole(Guid organizationId)
    {
        var role = new OrganizationRoleDefinition
        {
            OrganizationId = organizationId,
            Name = "Owner",
            Description = "Full workspace control, including owners, billing, roles, teams, and business data.",
            IsSystem = true,
            IsOwnerRole = true
        };

        foreach (string permission in ScoutHaulPermissions.All)
        {
            role.Permissions.Add(new OrganizationRolePermission { Permission = permission });
        }

        return role;
    }

    public static OrganizationRoleDefinition CreateOperatorRole(Guid organizationId, string name = "Operator")
    {
        var role = new OrganizationRoleDefinition
        {
            OrganizationId = organizationId,
            Name = name,
            Description = "Can run day-to-day sourcing, inventory, listings, budgets, tasks, and reports without owner administration.",
            IsSystem = true,
            IsOwnerRole = false
        };

        foreach (string permission in ScoutHaulPermissions.OperatorDefaults)
        {
            role.Permissions.Add(new OrganizationRolePermission { Permission = permission });
        }

        return role;
    }
}
