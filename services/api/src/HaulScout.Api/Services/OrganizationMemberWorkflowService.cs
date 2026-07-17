using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Who belongs to an organization and what they are. Handing out — or taking away — owner-level membership is
/// owner-only, and an organization can never be left without an owner.
/// </summary>
public sealed class OrganizationMemberWorkflowService(
    HaulScoutDbContext db,
    IOrganizationAuthorizer access,
    OrganizationRoleGuard guard,
    SupabaseAdminInviteService inviteService)
{
    public async Task<IReadOnlyList<OrganizationMemberDto>> ListAsync(Guid id, CancellationToken cancellationToken)
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

    public async Task<AppResult<OrganizationMemberDto>> AddAsync(Guid id, AddOrganizationMemberRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? requestedRole = null;
        if (request.RoleId is not null)
        {
            requestedRole = await guard.LoadRoleAsync(id, request.RoleId.Value, cancellationToken);
            if (requestedRole is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        bool ownerRoleRequested = request.Role == OrganizationRole.Owner || requestedRole?.IsOwnerRole == true;
        if (ownerRoleRequested || OrganizationRoleRules.RoleRequiresOwner(requestedRole))
        {
            await access.RequireOwner(id, cancellationToken);
        }
        else
        {
            await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return AppResult<OrganizationMemberDto>.Validation("Email and display name are required.");
        }

        Organization? organization = await db.Organizations.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (organization is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
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
            return AppResult<OrganizationMemberDto>.Conflict("User is already a member of this organization.");
        }

        OrganizationRoleDefinition? ownerRole = ownerRoleRequested && requestedRole is null
            ? await guard.LoadOwnerRoleAsync(id, cancellationToken)
            : null;
        if (ownerRoleRequested && requestedRole is null && ownerRole is null)
        {
            return AppResult<OrganizationMemberDto>.Validation("Owner role is not configured for this organization.");
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

        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult<OrganizationMemberDto>> ResendInviteAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);

        if (member?.User is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }
        if (!string.IsNullOrWhiteSpace(member.User.ExternalSubject))
        {
            return AppResult<OrganizationMemberDto>.Validation("Member already has an active authentication account.");
        }

        Organization organization = await db.Organizations.SingleAsync(item => item.Id == id, cancellationToken);
        _ = await inviteService.SendInviteAsync(member.User.Email, member.User.DisplayName, id, organization.Name, cancellationToken);
        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult<OrganizationMemberDto>> UpdateRoleAsync(Guid id, Guid memberId, UpdateOrganizationMemberRoleRequest request, CancellationToken cancellationToken)
    {
        OrganizationRoleDefinition? role = null;
        if (request.RoleId is not null)
        {
            role = await guard.LoadRoleAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Role does not belong to this organization.");
            }
        }

        bool ownerRoleRequested = role?.IsOwnerRole == true || request.LegacyRole == OrganizationRole.Owner;
        if (ownerRoleRequested || OrganizationRoleRules.RoleRequiresOwner(role))
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
        if (member is null)
        {
            return AppResult<OrganizationMemberDto>.NotFound();
        }

        if (ownerRoleRequested && role is null)
        {
            role = await guard.LoadOwnerRoleAsync(id, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationMemberDto>.Validation("Owner role is not configured for this organization.");
            }
        }

        member.RoleId = role?.Id;
        member.RoleDefinition = role;
        member.Role = ownerRoleRequested ? OrganizationRole.Owner : request.LegacyRole ?? OrganizationRole.Member;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationMemberDto>.Ok(member.ToDto());
    }

    public async Task<AppResult> RemoveAsync(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == memberId && item.OrganizationId == id, cancellationToken);
        if (member is null)
        {
            return AppResult.NotFound();
        }

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
                return AppResult.Validation("An organization must keep at least one owner.");
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
        return AppResult.Ok();
    }
}
