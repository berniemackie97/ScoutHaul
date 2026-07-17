using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

public static class OrganizationMemberMapping
{
    public static OrganizationMemberDto ToDto(this OrganizationMember item) => new(
        item.Id,
        item.UserId,
        item.User?.Email ?? string.Empty,
        item.User?.DisplayName ?? string.Empty,
        item.Role,
        item.RoleId,
        item.RoleDefinition?.Name,
        string.IsNullOrWhiteSpace(item.User?.ExternalSubject) ? "Pending" : "Active",
        item.JoinedAt);
}
