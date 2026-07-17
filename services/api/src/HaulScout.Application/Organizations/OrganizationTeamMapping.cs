using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public static class OrganizationTeamMapping
{
    public static OrganizationTeamDto ToDto(this OrganizationTeam item) => new(
        item.Id,
        item.OrganizationId,
        item.Name,
        item.Description,
        item.Members.Count,
        item.CreatedAt);

    public static OrganizationTeamMemberDto ToDto(this OrganizationTeamMember item) => new(
        item.Id,
        item.TeamId,
        item.UserId,
        item.User?.Email ?? string.Empty,
        item.User?.DisplayName ?? string.Empty,
        item.RoleId,
        item.RoleDefinition?.Name,
        item.JoinedAt);
}
