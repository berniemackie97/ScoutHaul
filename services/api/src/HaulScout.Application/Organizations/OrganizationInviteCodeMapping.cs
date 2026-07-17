using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public static class OrganizationInviteCodeMapping
{
    public static OrganizationInviteCodeDto ToDto(this OrganizationInviteCode item) => new(
        item.Id,
        item.OrganizationId,
        item.TeamId,
        item.Team?.Name,
        item.RoleId,
        item.RoleDefinition?.Name,
        item.Name,
        item.CodePrefix,
        item.IsActive,
        item.ExpiresAt,
        item.MaxUses,
        item.UseCount,
        item.CreatedAt);
}
