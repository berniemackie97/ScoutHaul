using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Mapping;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public sealed class OrganizationsController(
    ScoutHaulDbContext db,
    ICurrentUserContext currentUser,
    OrganizationAccessService access,
    OrganizationRoleSeeder roleSeeder,
    SupabaseAdminInviteService inviteService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> ListMine(CancellationToken cancellationToken)
    {
        var memberships = await db.OrganizationMembers
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
        return new PermissionCatalogDto(ScoutHaulPermissions.All);
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Organization name is required.");
        }

        var user = await db.Users.FindAsync([currentUser.UserId], cancellationToken);
        if (user is null)
        {
            user = new AppUser
            {
                Id = currentUser.UserId,
                Email = $"owner-{currentUser.UserId:N}@scouthaul.local",
                DisplayName = "Owner"
            };
            db.Users.Add(user);
        }

        var organization = new Organization { Name = request.Name.Trim() };
        db.Organizations.Add(organization);
        OrganizationRoleDefinition ownerRole = roleSeeder.EnsureOwnerRole(organization.Id);
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
        await access.RequirePermission(id, ScoutHaulPermissions.ViewReports, cancellationToken);
        var organization = await db.Organizations.FindAsync([id], cancellationToken);
        return organization is null ? NotFound() : organization.ToDto();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<OrganizationMemberDto>>> ListMembers(Guid id, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, ScoutHaulPermissions.ManageMembers, cancellationToken);
        var members = await db.OrganizationMembers
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
            await access.RequirePermission(id, ScoutHaulPermissions.ManageMembers, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest("Email and display name are required.");
        }

        var organization = await db.Organizations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (organization is null)
        {
            return NotFound();
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(existing => existing.Email == email, cancellationToken);
        if (user is null)
        {
            user = new AppUser { Email = email, DisplayName = request.DisplayName.Trim() };
            db.Users.Add(user);
        }

        var existingMember = await db.OrganizationMembers
            .SingleOrDefaultAsync(member => member.OrganizationId == id && member.UserId == user.Id, cancellationToken);

        if (existingMember is not null)
        {
            return Conflict("User is already a member of this organization.");
        }

        OrganizationRoleDefinition? ownerRole = ownerRoleRequested && requestedRole is null
            ? roleSeeder.EnsureOwnerRole(id)
            : null;

        var member = new OrganizationMember
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageMembers, cancellationToken);

        var member = await db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member?.User is null) return NotFound();
        if (!string.IsNullOrWhiteSpace(member.User.ExternalSubject))
        {
            return BadRequest("Member already has an active authentication account.");
        }

        var organization = await db.Organizations.SingleAsync(item => item.Id == id, cancellationToken);
        _ = await inviteService.SendInviteAsync(member.User.Email, member.User.DisplayName, id, organization.Name, cancellationToken);
        return member.ToDto();
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
            await access.RequirePermission(id, ScoutHaulPermissions.ManageMembers, cancellationToken);
        }

        var member = await db.OrganizationMembers
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member is null) return NotFound();

        if (ownerRoleRequested && role is null)
        {
            role = roleSeeder.EnsureOwnerRole(id);
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
        var member = await db.OrganizationMembers
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
            await access.RequirePermission(id, ScoutHaulPermissions.ManageMembers, cancellationToken);
        }

        var teamMemberships = await db.OrganizationTeamMembers
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageRoles, cancellationToken);
        var roles = await db.OrganizationRoles
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
            permissions = ValidatePermissions(request.GrantsOwnerAccess ? ScoutHaulPermissions.All : request.Permissions);
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
            await access.RequirePermission(id, ScoutHaulPermissions.ManageRoles, cancellationToken);
        }

        var role = new OrganizationRoleDefinition
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
        var role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

        if (role is null) return NotFound();
        if (role.IsSystem) return BadRequest("System roles are protected. Create a custom role instead.");

        string[] permissions;
        try
        {
            permissions = ValidatePermissions(request.GrantsOwnerAccess ? ScoutHaulPermissions.All : request.Permissions);
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
            await access.RequirePermission(id, ScoutHaulPermissions.ManageRoles, cancellationToken);
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
        var role = await db.OrganizationRoles
            .Include(item => item.Permissions)
            .SingleOrDefaultAsync(item => item.Id == roleId && item.OrganizationId == id, cancellationToken);

        if (role is null) return NotFound();
        if (role.IsSystem || role.IsOwnerRole) return BadRequest("System and owner roles cannot be deleted.");
        await access.RequirePermission(id, ScoutHaulPermissions.ManageRoles, cancellationToken);

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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        var teams = await db.OrganizationTeams
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Team name is required.");

        var team = new OrganizationTeam
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Team name is required.");

        var team = await db.OrganizationTeams
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        var team = await db.OrganizationTeams
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        if (!await TeamBelongsToOrganization(id, teamId, cancellationToken)) return NotFound();

        var members = await db.OrganizationTeamMembers
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
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

        var member = new OrganizationTeamMember
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
        await access.RequirePermission(id, ScoutHaulPermissions.ManageTeams, cancellationToken);
        if (!await TeamBelongsToOrganization(id, teamId, cancellationToken)) return NotFound();

        var member = await db.OrganizationTeamMembers
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

        string? unknown = normalized.FirstOrDefault(permission => !ScoutHaulPermissions.IsKnown(permission));
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
        return permission is ScoutHaulPermissions.ManageOrganization
            or ScoutHaulPermissions.ManageOwners
            or ScoutHaulPermissions.ManageRoles;
    }
}
