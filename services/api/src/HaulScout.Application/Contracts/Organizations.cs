using HaulScout.Domain;

namespace HaulScout.Application.Contracts;

// Organizations, membership, roles, teams, and invite codes.

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddOrganizationMemberRequest(string Email, string DisplayName, OrganizationRole Role, Guid? RoleId = null);
/// <param name="StoreDomain">The organization's public storefront address, when it has one.</param>
public sealed record OrganizationDto(Guid Id, string Name, DateTimeOffset CreatedAt, string? StoreDomain);
public sealed record OrganizationMemberDto(Guid Id, Guid UserId, string Email, string DisplayName, OrganizationRole Role, Guid? RoleId, string? RoleName, string InviteStatus, DateTimeOffset JoinedAt);
public sealed record PermissionCatalogDto(IReadOnlyList<string> Permissions);
public sealed record UpsertOrganizationRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, bool GrantsOwnerAccess);
public sealed record OrganizationRoleDto(Guid Id, string Name, string? Description, bool IsSystem, bool IsOwnerRole, IReadOnlyList<string> Permissions);
public sealed record UpdateOrganizationMemberRoleRequest(Guid? RoleId, OrganizationRole? LegacyRole);
public sealed record UpsertOrganizationTeamRequest(string Name, string? Description);
public sealed record OrganizationTeamDto(Guid Id, Guid OrganizationId, string Name, string? Description, int MemberCount, DateTimeOffset CreatedAt);
public sealed record AddTeamMemberRequest(Guid UserId, Guid? RoleId);
public sealed record OrganizationTeamMemberDto(Guid Id, Guid TeamId, Guid UserId, string Email, string DisplayName, Guid? RoleId, string? RoleName, DateTimeOffset JoinedAt);
public sealed record CreateOrganizationInviteCodeRequest(string Name, Guid? TeamId, Guid? RoleId, DateTimeOffset? ExpiresAt, int? MaxUses);
public sealed record OrganizationInviteCodeDto(
    Guid Id,
    Guid OrganizationId,
    Guid? TeamId,
    string? TeamName,
    Guid? RoleId,
    string? RoleName,
    string Name,
    string CodePrefix,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    int UseCount,
    DateTimeOffset CreatedAt);
public sealed record OrganizationInviteCodeCreatedDto(OrganizationInviteCodeDto Invite, string Code);
public sealed record RedeemOrganizationInviteCodeRequest(string Code);
public sealed record OrganizationJoinResultDto(OrganizationDto Organization, OrganizationMemberDto Member, Guid? TeamId);
