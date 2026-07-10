using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrganizationAccessService access,
    SupabaseAdminInviteService inviteService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> ListMine(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        List<OrganizationMember> memberships = await db.OrganizationMembers
            .Include(member => member.Organization)
            .Where(member => member.UserId == currentUser.UserId)
            .OrderBy(member => member.Organization!.Name)
            .ToListAsync(cancellationToken);

        return memberships
            .Where(member => member.Organization is not null)
            .Select(member => member.Organization!.ToDto())
            .ToArray();
    }

    [HttpGet("permissions")]
    public ActionResult<PermissionCatalogDto> ListPermissionCatalog()
    {
        return new PermissionCatalogDto(HaulScoutPermissions.All);
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Organization name is required.");
        }

        AppUser user = await db.Users.FindAsync([currentUser.UserId], cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        Organization organization = new() { Name = request.Name.Trim() };
        db.Organizations.Add(organization);
        OrganizationRoleDefinition ownerRole = OrganizationRoleDefaults.CreateOwnerRole(organization.Id);
        OrganizationRoleDefinition operatorRole = OrganizationRoleDefaults.CreateOperatorRole(organization.Id);
        db.OrganizationRoles.AddRange(ownerRole, operatorRole);
        organization.Members.Add(new OrganizationMember
        {
            UserId = user.Id,
            RoleId = ownerRole.Id,
            Role = Domain.OrganizationRole.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = organization.Id }, organization.ToDto());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ViewReports, cancellationToken);
        Organization? organization = await db.Organizations.FindAsync([id], cancellationToken);
        return organization is null ? NotFound() : organization.ToDto();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        List<OrganizationMember> members = await db.OrganizationMembers
            .Include(member => member.User)
            .Include(member => member.RoleDefinition)
            .Where(member => member.OrganizationId == id)
            .OrderBy(member => member.User!.DisplayName)
            .ToListAsync(cancellationToken);

        return members.Select(member => member.ToDto()).ToArray();
    }

    [HttpPost("{id:guid}/members")]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(Guid id, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? requestedRole = null;
        if (request.RoleId is not null)
        {
            requestedRole = await LoadRole(id, request.RoleId.Value, cancellationToken);
            if (requestedRole is null) return BadRequest("Role does not belong to this organization.");
        }

        bool ownerRoleRequested = request.Role == OrganizationRole.Owner || requestedRole?.IsOwnerRole == true;
        bool requiresOwner = ownerRoleRequested || RoleRequiresOwner(requestedRole);
        if (requiresOwner)
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest("Email and display name are required.");
        }

        Organization? organization = await db.Organizations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        string email = request.Email.Trim().ToLowerInvariant();
        AppUser? user = await db.Users.SingleOrDefaultAsync(existing => existing.Email == email, cancellationToken);
        if (user is null)
        {
            user = new AppUser { Email = email, DisplayName = request.DisplayName.Trim() };
            db.Users.Add(user);
        }

        OrganizationMember? existingMember = await db.OrganizationMembers
            .SingleOrDefaultAsync(member => member.OrganizationId == id && member.UserId == user.Id, cancellationToken);

        if (existingMember is not null)
        {
            return Conflict("User is already a member of this organization.");
        }

        OrganizationRoleDefinition? ownerRole = ownerRoleRequested && requestedRole is null
            ? await LoadOwnerRole(id, cancellationToken)
            : null;

        if (ownerRoleRequested && requestedRole is null && ownerRole is null)
        {
            return BadRequest("Owner role is not configured for this organization.");
        }

        OrganizationMember member = new()
        {
            OrganizationId = id,
            UserId = user.Id,
            RoleId = requestedRole?.Id ?? ownerRole?.Id,
            Role = ownerRoleRequested ? OrganizationRole.Owner : request.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };

        db.OrganizationMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);
        _ = await inviteService.SendInviteAsync(user.Email, user.DisplayName, id, organization.Name, cancellationToken);
        member.User = user;
        member.RoleDefinition = requestedRole ?? ownerRole;

        return CreatedAtAction(nameof(ListMembers), new { id }, member.ToDto());
    }

    [HttpPost("{id:guid}/members/{memberId:guid}/resend-invite")]
    public async Task<ActionResult<OrganizationMemberDto>> ResendInvite(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member?.User is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(member.User.ExternalSubject))
        {
            return BadRequest("Member already has an active authentication account.");
        }

        Organization organization = await db.Organizations.SingleAsync(item => item.Id == id, cancellationToken);
        _ = await inviteService.SendInviteAsync(member.User.Email, member.User.DisplayName, id, organization.Name, cancellationToken);
        return member.ToDto();
    }

    [HttpGet("{id:guid}/invite-codes")]
    public async Task<ActionResult<IReadOnlyList<OrganizationInviteCodeDto>>> ListInviteCodes(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        List<OrganizationInviteCode> inviteCodes = await db.OrganizationInviteCodes
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .Where(item => item.OrganizationId == id)
            .OrderByDescending(item => item.IsActive)
            .ThenByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

        return inviteCodes.Select(item => item.ToDto()).ToArray();
    }

    [HttpPost("{id:guid}/invite-codes")]
    public async Task<ActionResult<OrganizationInviteCodeCreatedDto>> CreateInviteCode(
        Guid id,
        CreateOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Invite code name is required.");
        if (request.MaxUses is <= 0) return BadRequest("Max uses must be greater than zero.");
        if (request.ExpiresAt is not null && request.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return BadRequest("Expiration must be in the future.");
        }

        if (!await db.Organizations.AnyAsync(item => item.Id == id, cancellationToken))
        {
            return NotFound();
        }

        OrganizationTeam? team = null;
        if (request.TeamId is not null)
        {
            team = await db.OrganizationTeams.SingleOrDefaultAsync(
                item => item.Id == request.TeamId.Value && item.OrganizationId == id,
                cancellationToken);
            if (team is null) return BadRequest("Team does not belong to this organization.");
        }

        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await LoadRole(id, request.RoleId.Value, cancellationToken);
            if (role is null) return BadRequest("Role does not belong to this organization.");
            if (RoleRequiresOwner(role))
            {
                return BadRequest("Invite codes cannot grant owner-level permissions.");
            }
        }
        else
        {
            role = await LoadDefaultInviteRole(id, cancellationToken);
            if (role is null)
            {
                return BadRequest("Default operator role is not configured. Create a non-owner role before issuing invite codes.");
            }
        }

        string code = string.Empty;
        string codeHash = string.Empty;
        bool codeAlreadyExists = true;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            code = GenerateInviteCode();
            codeHash = HashInviteCode(code);
            codeAlreadyExists = await db.OrganizationInviteCodes.AnyAsync(item => item.CodeHash == codeHash, cancellationToken);
            if (!codeAlreadyExists) break;
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(codeHash) || codeAlreadyExists)
        {
            return Problem("Unable to generate invite code.");
        }

        OrganizationInviteCode inviteCode = new()
        {
            OrganizationId = id,
            TeamId = team?.Id,
            RoleId = role?.Id,
            CreatedByUserId = currentUser.UserId,
            Name = request.Name.Trim(),
            CodeHash = codeHash,
            CodePrefix = BuildCodePrefix(code),
            ExpiresAt = request.ExpiresAt,
            MaxUses = request.MaxUses
        };

        db.OrganizationInviteCodes.Add(inviteCode);
        await db.SaveChangesAsync(cancellationToken);

        inviteCode.Team = team;
        inviteCode.RoleDefinition = role;

        return CreatedAtAction(nameof(ListInviteCodes), new { id }, new OrganizationInviteCodeCreatedDto(inviteCode.ToDto(), code));
    }

    [HttpPatch("{id:guid}/invite-codes/{inviteCodeId:guid}/deactivate")]
    public async Task<ActionResult<OrganizationInviteCodeDto>> DeactivateInviteCode(
        Guid id,
        Guid inviteCodeId,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationInviteCode? inviteCode = await db.OrganizationInviteCodes
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == inviteCodeId && item.OrganizationId == id, cancellationToken);

        if (inviteCode is null) return NotFound();

        inviteCode.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return inviteCode.ToDto();
    }

    [HttpPost("invite-codes/redeem")]
    public async Task<ActionResult<OrganizationJoinResultDto>> RedeemInviteCode(
        RedeemOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        if (string.IsNullOrWhiteSpace(request.Code)) return BadRequest("Invite code is required.");

        string codeHash = HashInviteCode(request.Code);
        OrganizationInviteCode? inviteCode = await db.OrganizationInviteCodes
            .Include(item => item.Organization)
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.CodeHash == codeHash, cancellationToken);

        if (inviteCode?.Organization is null)
        {
            return BadRequest("Invite code is invalid.");
        }

        if (!inviteCode.IsActive
            || (inviteCode.ExpiresAt.HasValue && inviteCode.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            || inviteCode.MaxUses <= inviteCode.UseCount)
        {
            return BadRequest("Invite code is expired or inactive.");
        }

        AppUser user = await db.Users.FindAsync([currentUser.UserId], cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(
                item => item.OrganizationId == inviteCode.OrganizationId && item.UserId == user.Id,
                cancellationToken);

        if (member is null)
        {
            member = new OrganizationMember
            {
                OrganizationId = inviteCode.OrganizationId,
                UserId = user.Id,
                RoleId = inviteCode.RoleId,
                Role = OrganizationRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            };
            db.OrganizationMembers.Add(member);
        }

        if (inviteCode.TeamId is not null)
        {
            bool hasTeamMembership = await db.OrganizationTeamMembers.AnyAsync(
                item => item.TeamId == inviteCode.TeamId.Value && item.UserId == user.Id,
                cancellationToken);

            if (!hasTeamMembership)
            {
                db.OrganizationTeamMembers.Add(new OrganizationTeamMember
                {
                    TeamId = inviteCode.TeamId.Value,
                    UserId = user.Id,
                    RoleId = inviteCode.RoleId,
                    JoinedAt = DateTimeOffset.UtcNow
                });
            }
        }

        inviteCode.UseCount++;
        await db.SaveChangesAsync(cancellationToken);

        member.User = user;
        member.RoleDefinition ??= inviteCode.RoleDefinition;

        return new OrganizationJoinResultDto(
            inviteCode.Organization.ToDto(),
            member.ToDto(),
            inviteCode.TeamId);
    }

    [HttpPatch("{id:guid}/members/{memberId:guid}/role")]
    public async Task<ActionResult<OrganizationMemberDto>> UpdateMemberRole(
        Guid id,
        Guid memberId,
        UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await LoadRole(id, request.RoleId.Value, cancellationToken);
            if (role is null) return BadRequest("Role does not belong to this organization.");
        }

        bool ownerRoleRequested = role?.IsOwnerRole == true || request.LegacyRole == OrganizationRole.Owner;
        bool requiresOwner = ownerRoleRequested || RoleRequiresOwner(role);
        if (requiresOwner)
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member is null) return NotFound();

        if (ownerRoleRequested && role is null)
        {
            role = await LoadOwnerRole(id, cancellationToken);
            if (role is null)
            {
                return BadRequest("Owner role is not configured for this organization.");
            }
        }

        member.RoleId = role?.Id;
        member.RoleDefinition = role;
        member.Role = ownerRoleRequested ? OrganizationRole.Owner : request.LegacyRole ?? OrganizationRole.Member;
        await db.SaveChangesAsync(cancellationToken);

        return member.ToDto();
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member is null) return NotFound();

        bool removingOwner = member.Role == OrganizationRole.Owner || member.RoleDefinition?.IsOwnerRole == true;
        if (removingOwner)
        {
            await access.RequireOwner(id, cancellationToken);

            int ownerCount = await db.OrganizationMembers.CountAsync(item =>
                item.OrganizationId == id
                && (item.Role == OrganizationRole.Owner || (item.RoleDefinition != null && item.RoleDefinition.IsOwnerRole)),
                cancellationToken);

            if (ownerCount <= 1)
            {
                return BadRequest("An organization must keep at least one owner.");
            }
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        List<OrganizationTeamMember> teamMemberships = await db.OrganizationTeamMembers
            .Where(item => item.UserId == member.UserId && item.Team!.OrganizationId == id)
            .ToListAsync(cancellationToken);

        db.OrganizationTeamMembers.RemoveRange(teamMemberships);
        db.OrganizationMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/roles")]
    public async Task<ActionResult<IReadOnlyList<OrganizationRoleDto>>> ListRoles(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        List<OrganizationRoleDefinition> roles = await db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == id)
            .OrderByDescending(role => role.IsOwnerRole)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(role => role.ToDto()).ToArray();
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<ActionResult<OrganizationRoleDto>> CreateRole(
        Guid id,
        UpsertOrganizationRoleRequest request,
        CancellationToken cancellationToken)
    {
        string[] permissions;
        try
        {
            permissions = ValidatePermissions(request.GrantsOwnerAccess ? HaulScoutPermissions.All : request.Permissions);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
        if (permissions.Length == 0) return BadRequest("At least one permission is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Role name is required.");

        if (request.GrantsOwnerAccess || ContainsOwnerGuardedPermission(permissions))
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        OrganizationRoleDefinition role = new()
        {
            OrganizationId = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsOwnerRole = request.GrantsOwnerAccess,
            IsSystem = false
        };

        ReplacePermissions(role, permissions);
        db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(ListRoles), new { id }, role.ToDto());
    }

    [HttpPut("{id:guid}/roles/{roleId:guid}")]
    public async Task<ActionResult<OrganizationRoleDto>> UpdateRole(
        Guid id,
        Guid roleId,
        UpsertOrganizationRoleRequest request,
        CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

        if (role is null) return NotFound();
        if (role.IsSystem) return BadRequest("System roles are protected. Create a custom role instead.");

        string[] permissions;
        try
        {
            permissions = ValidatePermissions(request.GrantsOwnerAccess ? HaulScoutPermissions.All : request.Permissions);
        }
        catch (ArgumentException e)
        {
            return BadRequest(e.Message);
        }
        if (permissions.Length == 0) return BadRequest("At least one permission is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Role name is required.");

        if (request.GrantsOwnerAccess || role.IsOwnerRole || ContainsOwnerGuardedPermission(permissions))
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);
        }

        role.Name = request.Name.Trim();
        role.Description = request.Description?.Trim();
        role.IsOwnerRole = request.GrantsOwnerAccess;
        ReplacePermissions(role, permissions);

        await db.SaveChangesAsync(cancellationToken);
        return role.ToDto();
    }

    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> DeleteRole(Guid id, Guid roleId, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

        if (role is null) return NotFound();
        if (role.IsSystem || role.IsOwnerRole) return BadRequest("System and owner roles cannot be deleted.");
        await access.RequirePermission(id, HaulScoutPermissions.ManageRoles, cancellationToken);

        bool isAssigned = await db.OrganizationMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken)
            || await db.OrganizationTeamMembers.AnyAsync(item => item.RoleId == roleId, cancellationToken);

        if (isAssigned)
        {
            return BadRequest("Role is assigned to members or teams. Reassign them before deleting it.");
        }

        db.OrganizationRoles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/teams")]
    public async Task<ActionResult<IReadOnlyList<OrganizationTeamDto>>> ListTeams(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        List<OrganizationTeam> teams = await db.OrganizationTeams
            .Include(team => team.Members)
            .Where(team => team.OrganizationId == id)
            .OrderBy(team => team.Name)
            .ToListAsync(cancellationToken);

        return teams.Select(team => team.ToDto()).ToArray();
    }

    [HttpPost("{id:guid}/teams")]
    public async Task<ActionResult<OrganizationTeamDto>> CreateTeam(
        Guid id,
        UpsertOrganizationTeamRequest request,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Team name is required.");

        OrganizationTeam team = new()
        {
            OrganizationId = id,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim()
        };

        db.OrganizationTeams.Add(team);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListTeams), new { id }, team.ToDto());
    }

    [HttpPut("{id:guid}/teams/{teamId:guid}")]
    public async Task<ActionResult<OrganizationTeamDto>> UpdateTeam(
        Guid id,
        Guid teamId,
        UpsertOrganizationTeamRequest request,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Team name is required.");

        OrganizationTeam? team = await db.OrganizationTeams
            .Include(item => item.Members)
            .SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == id, cancellationToken);

        if (team is null) return NotFound();

        team.Name = request.Name.Trim();
        team.Description = request.Description?.Trim();
        await db.SaveChangesAsync(cancellationToken);

        return team.ToDto();
    }

    [HttpDelete("{id:guid}/teams/{teamId:guid}")]
    public async Task<IActionResult> DeleteTeam(Guid id, Guid teamId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        OrganizationTeam? team = await db.OrganizationTeams
            .SingleOrDefaultAsync(item => item.Id == teamId && item.OrganizationId == id, cancellationToken);

        if (team is null) return NotFound();

        db.OrganizationTeams.Remove(team);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/teams/{teamId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationTeamMemberDto>>> ListTeamMembers(
        Guid id,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await TeamBelongsToOrganization(id, teamId, cancellationToken)) return NotFound();

        List<OrganizationTeamMember> members = await db.OrganizationTeamMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .Where(item => item.TeamId == teamId)
            .OrderBy(item => item.User!.DisplayName)
            .ToListAsync(cancellationToken);

        return members.Select(item => item.ToDto()).ToArray();
    }

    [HttpPost("{id:guid}/teams/{teamId:guid}/members")]
    public async Task<ActionResult<OrganizationTeamMemberDto>> AddTeamMember(
        Guid id,
        Guid teamId,
        AddTeamMemberRequest request,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await TeamBelongsToOrganization(id, teamId, cancellationToken)) return NotFound();

        bool userBelongsToOrg = await db.OrganizationMembers.AnyAsync(
            item => item.OrganizationId == id && item.UserId == request.UserId,
            cancellationToken);
        if (!userBelongsToOrg) return BadRequest("User must belong to the organization before joining a team.");

        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await LoadRole(id, request.RoleId.Value, cancellationToken);
            if (role is null) return BadRequest("Role does not belong to this organization.");
        }

        if (RoleRequiresOwner(role))
        {
            await access.RequireOwner(id, cancellationToken);
        }

        OrganizationTeamMember member = new()
        {
            TeamId = teamId,
            UserId = request.UserId,
            RoleId = role?.Id
        };

        db.OrganizationTeamMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        member.User = await db.Users.FindAsync([request.UserId], cancellationToken);
        member.RoleDefinition = role;

        return CreatedAtAction(nameof(ListTeamMembers), new { id, teamId }, member.ToDto());
    }

    [HttpDelete("{id:guid}/teams/{teamId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveTeamMember(Guid id, Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageTeams, cancellationToken);
        if (!await TeamBelongsToOrganization(id, teamId, cancellationToken)) return NotFound();

        OrganizationTeamMember? member = await db.OrganizationTeamMembers
            .SingleOrDefaultAsync(item => item.Id == memberId && item.TeamId == teamId, cancellationToken);

        if (member is null) return NotFound();

        db.OrganizationTeamMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<OrganizationRoleDefinition?> LoadRole(Guid organizationId, Guid roleId, CancellationToken cancellationToken)
    {
        return await db.OrganizationRoles
            .Include(role => role.Permissions)
            .SingleOrDefaultAsync(
            role => role.Id == roleId && role.OrganizationId == organizationId,
            cancellationToken);
    }

    private async Task<OrganizationRoleDefinition?> LoadOwnerRole(Guid organizationId, CancellationToken cancellationToken)
    {
        return await db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId && role.IsOwnerRole)
            .OrderBy(role => role.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<OrganizationRoleDefinition?> LoadDefaultInviteRole(Guid organizationId, CancellationToken cancellationToken)
    {
        return await db.OrganizationRoles
            .Include(role => role.Permissions)
            .Where(role => role.OrganizationId == organizationId && role.IsSystem && !role.IsOwnerRole)
            .OrderBy(role => role.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> TeamBelongsToOrganization(Guid organizationId, Guid teamId, CancellationToken cancellationToken)
    {
        return await db.OrganizationTeams.AnyAsync(team => team.Id == teamId && team.OrganizationId == organizationId, cancellationToken);
    }

    private static string[] ValidatePermissions(IEnumerable<string> permissions)
    {
        string[] normalized = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        string? unknown = normalized.FirstOrDefault(permission => !HaulScoutPermissions.IsKnown(permission));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown permission '{unknown}'.");
        }

        return normalized;
    }

    private static void ReplacePermissions(OrganizationRoleDefinition role, IReadOnlyList<string> permissions)
    {
        role.Permissions.Clear();
        foreach (string permission in permissions)
        {
            role.Permissions.Add(new OrganizationRolePermission { RoleId = role.Id, Permission = permission });
        }
    }

    private static bool RoleRequiresOwner(OrganizationRoleDefinition? role)
    {
        return role?.IsOwnerRole == true || role?.Permissions.Any(permission => IsOwnerGuardedPermission(permission.Permission)) == true;
    }

    private static bool ContainsOwnerGuardedPermission(IEnumerable<string> permissions)
    {
        return permissions.Any(IsOwnerGuardedPermission);
    }

    private static bool IsOwnerGuardedPermission(string permission)
    {
        return permission is HaulScoutPermissions.ManageOrganization
            or HaulScoutPermissions.ManageOwners
            or HaulScoutPermissions.ManageRoles;
    }

    private static string GenerateInviteCode()
    {
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        return $"SH-{token[..4]}-{token[4..8]}-{token[8..12]}";
    }

    private static string BuildCodePrefix(string code)
    {
        string normalized = NormalizeInviteCode(code);
        return normalized.Length <= 8 ? normalized : $"{normalized[..8]}...";
    }

    private static string HashInviteCode(string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeInviteCode(code))));
    }

    private static string NormalizeInviteCode(string code)
    {
        return code
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }
}
