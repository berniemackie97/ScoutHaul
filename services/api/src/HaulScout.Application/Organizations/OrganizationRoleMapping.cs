using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public static class OrganizationRoleMapping
{
    public static OrganizationRoleDto ToDto(this OrganizationRoleDefinition item) => new(
        item.Id,
        item.Name,
        item.Description,
        item.IsSystem,
        item.IsOwnerRole,
        item.Permissions.Select(permission => permission.Permission).Order().ToArray());
}
