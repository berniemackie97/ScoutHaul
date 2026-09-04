using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(
    OrganizationWorkflowService organizations,
    OrganizationMemberWorkflowService members,
    OrganizationInviteCodeWorkflowService inviteCodes,
    OrganizationRoleWorkflowService roles,
    OrganizationTeamWorkflowService teams) : HaulScoutControllerBase
{
    // ── Organization ───────────────────────────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> ListMine(CancellationToken cancellationToken) =>
        Ok(await organizations.ListMineAsync(cancellationToken));

    [HttpGet("permissions")]
    public ActionResult<PermissionCatalogDto> ListPermissionCatalog() => new PermissionCatalogDto(HaulScoutPermissions.All);

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrganizationDto> result = await organizations.CreateAsync(request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(Get), new { id = value.Id }, value));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationDto>> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await organizations.GetAsync(id, cancellationToken));

    // ── Members ────────────────────────────────────────────────────────────
    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(Guid id, CancellationToken cancellationToken) =>
        Ok(await members.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(Guid id, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrganizationMemberDto> result = await members.AddAsync(id, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListMembers), new { id }, value));
    }

    [HttpPost("{id:guid}/members/{memberId:guid}/resend-invite")]
    public async Task<ActionResult<OrganizationMemberDto>> ResendInvite(Guid id, Guid memberId, CancellationToken cancellationToken) =>
        ToActionResult(await members.ResendInviteAsync(id, memberId, cancellationToken));

    [HttpPatch("{id:guid}/members/{memberId:guid}/role")]
    public async Task<ActionResult<OrganizationMemberDto>> UpdateMemberRole(
        Guid id,
        Guid memberId,
        UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await members.UpdateRoleAsync(id, memberId, request, cancellationToken));

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken cancellationToken) =>
        ToNoContentResult(await members.RemoveAsync(id, memberId, cancellationToken));

    // ── Invite codes ───────────────────────────────────────────────────────
    [HttpGet("{id:guid}/invite-codes")]
    public async Task<ActionResult<IReadOnlyList<OrganizationInviteCodeDto>>> ListInviteCodes(Guid id, CancellationToken cancellationToken) =>
        Ok(await inviteCodes.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/invite-codes")]
    public async Task<ActionResult<OrganizationInviteCodeCreatedDto>> CreateInviteCode(
        Guid id,
        CreateOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<OrganizationInviteCodeCreatedDto> result = await inviteCodes.CreateAsync(id, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListInviteCodes), new { id }, value));
    }

    [HttpPatch("{id:guid}/invite-codes/{inviteCodeId:guid}/deactivate")]
    public async Task<ActionResult<OrganizationInviteCodeDto>> DeactivateInviteCode(
        Guid id,
        Guid inviteCodeId,
        CancellationToken cancellationToken) =>
        ToActionResult(await inviteCodes.DeactivateAsync(id, inviteCodeId, cancellationToken));

    [HttpPost("invite-codes/redeem")]
    public async Task<ActionResult<OrganizationJoinResultDto>> RedeemInviteCode(
        RedeemOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await inviteCodes.RedeemAsync(request, cancellationToken));

    // ── Roles ──────────────────────────────────────────────────────────────
    [HttpGet("{id:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<OrganizationRoleDto>>> ListRoles(Guid id, CancellationToken cancellationToken) =>
        Ok(await roles.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/roles")]
    public async Task<ActionResult<OrganizationRoleDto>> CreateRole(Guid id, UpsertOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrganizationRoleDto> result = await roles.CreateAsync(id, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListRoles), new { id }, value));
    }

    [HttpPut("{id:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<OrganizationRoleDto>> UpdateRole(
        Guid id,
        Guid roleId,
        UpsertOrganizationRoleRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await roles.UpdateAsync(id, roleId, request, cancellationToken));

    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid id, Guid roleId, CancellationToken cancellationToken) =>
        ToNoContentResult(await roles.DeleteAsync(id, roleId, cancellationToken));

    // ── Teams ──────────────────────────────────────────────────────────────
    [HttpGet("{id:guid}/teams")]
    public async Task<ActionResult<IReadOnlyList<OrganizationTeamDto>>> ListTeams(Guid id, CancellationToken cancellationToken) =>
        Ok(await teams.ListAsync(id, cancellationToken));

    [HttpPost("{id:guid}/teams")]
    public async Task<ActionResult<OrganizationTeamDto>> CreateTeam(Guid id, UpsertOrganizationTeamRequest request, CancellationToken cancellationToken)
    {
        AppResult<OrganizationTeamDto> result = await teams.CreateAsync(id, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListTeams), new { id }, value));
    }

    [HttpPut("{id:guid}/teams/{teamId:guid}")]
    public async Task<ActionResult<OrganizationTeamDto>> UpdateTeam(
        Guid id,
        Guid teamId,
        UpsertOrganizationTeamRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await teams.UpdateAsync(id, teamId, request, cancellationToken));

    [HttpDelete("{id:guid}/teams/{teamId:guid}")]
    public async Task<IActionResult> DeleteTeam(Guid id, Guid teamId, CancellationToken cancellationToken) =>
        ToNoContentResult(await teams.DeleteAsync(id, teamId, cancellationToken));

    [HttpGet("{id:guid}/teams/{teamId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationTeamMemberDto>>> ListTeamMembers(
        Guid id,
        Guid teamId,
        CancellationToken cancellationToken) =>
        ToActionResult(await teams.ListMembersAsync(id, teamId, cancellationToken));

    [HttpPost("{id:guid}/teams/{teamId:guid}/members")]
    public async Task<ActionResult<OrganizationTeamMemberDto>> AddTeamMember(
        Guid id,
        Guid teamId,
        AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        AppResult<OrganizationTeamMemberDto> result = await teams.AddMemberAsync(id, teamId, request, cancellationToken);
        return ToActionResult(result, value => CreatedAtAction(nameof(ListTeamMembers), new { id, teamId }, value));
    }

    [HttpDelete("{id:guid}/teams/{teamId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveTeamMember(Guid id, Guid teamId, Guid memberId, CancellationToken cancellationToken) =>
        ToNoContentResult(await teams.RemoveMemberAsync(id, teamId, memberId, cancellationToken));


    private IActionResult ToNoContentResult(AppResult result) =>
        ToActionResult(result);
}
