using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Shareable invite codes. The plaintext code is shown exactly once at creation and only its hash is stored, so a
/// leaked database cannot be redeemed from. Codes are matched on a hash of the normalized code, which is what lets an
/// operator type it in any case or spacing. A code may never grant owner-level access — that always needs a person.
/// </summary>
public sealed class OrganizationInviteCodeWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrganizationAccessService access,
    OrganizationRoleGuard guard)
{
    public async Task<IReadOnlyList<OrganizationInviteCodeDto>> ListAsync(Guid id, CancellationToken cancellationToken)
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

    public async Task<AppResult<OrganizationInviteCodeCreatedDto>> CreateAsync(
        Guid id,
        CreateOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Invite code name is required.");
        }
        if (request.MaxUses is <= 0)
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Max uses must be greater than zero.");
        }
        if (request.ExpiresAt is not null && request.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Expiration must be in the future.");
        }
        if (!await db.Organizations.AnyAsync(item => item.Id == id, cancellationToken))
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.NotFound();
        }

        OrganizationTeam? team = null;
        if (request.TeamId is not null)
        {
            team = await db.OrganizationTeams.SingleOrDefaultAsync(
                item => item.Id == request.TeamId.Value && item.OrganizationId == id, cancellationToken);
            if (team is null)
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Team does not belong to this organization.");
            }
        }

        OrganizationRoleDefinition? role;
        if (request.RoleId is not null)
        {
            role = await guard.LoadRoleAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Role does not belong to this organization.");
            }
            // A code is a bearer token — owner access must never be obtainable by redeeming one.
            if (OrganizationRoleGuard.RoleRequiresOwner(role))
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Invite codes cannot grant owner-level permissions.");
            }
        }
        else
        {
            role = await guard.LoadDefaultInviteRoleAsync(id, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation(
                    "Default operator role is not configured. Create a non-owner role before issuing invite codes.");
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
            // Effectively unreachable (it would need repeated 48-bit collisions); surfaces as a 500 rather than
            // pretending the caller did something wrong.
            throw new InvalidOperationException("Unable to generate invite code.");
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

        // The plaintext code is returned here and never again — only its hash is stored.
        return AppResult<OrganizationInviteCodeCreatedDto>.Ok(new OrganizationInviteCodeCreatedDto(inviteCode.ToDto(), code));
    }

    public async Task<AppResult<OrganizationInviteCodeDto>> DeactivateAsync(Guid id, Guid inviteCodeId, CancellationToken cancellationToken)
    {
        await access.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationInviteCode? inviteCode = await db.OrganizationInviteCodes
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.Id == inviteCodeId && item.OrganizationId == id, cancellationToken);
        if (inviteCode is null)
        {
            return AppResult<OrganizationInviteCodeDto>.NotFound();
        }

        inviteCode.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<OrganizationInviteCodeDto>.Ok(inviteCode.ToDto());
    }

    public async Task<AppResult<OrganizationJoinResultDto>> RedeemAsync(RedeemOrganizationInviteCodeRequest request, CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return AppResult<OrganizationJoinResultDto>.Validation("Invite code is required.");
        }

        string codeHash = HashInviteCode(request.Code);
        OrganizationInviteCode? inviteCode = await db.OrganizationInviteCodes
            .Include(item => item.Organization)
            .Include(item => item.Team)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.CodeHash == codeHash, cancellationToken);

        // Same message whether the code never existed or belongs to a dead organization — no probing.
        if (inviteCode?.Organization is null)
        {
            return AppResult<OrganizationJoinResultDto>.Validation("Invite code is invalid.");
        }

        if (!inviteCode.IsActive
            || (inviteCode.ExpiresAt.HasValue && inviteCode.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            || inviteCode.MaxUses <= inviteCode.UseCount)
        {
            return AppResult<OrganizationJoinResultDto>.Validation("Invite code is expired or inactive.");
        }

        AppUser user = await db.Users.FindAsync([currentUser.UserId], cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        OrganizationMember? member = await db.OrganizationMembers
            .Include(item => item.User)
            .Include(item => item.RoleDefinition)
            .SingleOrDefaultAsync(item => item.OrganizationId == inviteCode.OrganizationId && item.UserId == user.Id, cancellationToken);

        if (member is null)
        {
            member = new OrganizationMember
            {
                OrganizationId = inviteCode.OrganizationId,
                UserId = user.Id,
                RoleId = inviteCode.RoleId,
                Role = Domain.OrganizationRole.Member,
                JoinedAt = DateTimeOffset.UtcNow
            };
            db.OrganizationMembers.Add(member);
        }

        if (inviteCode.TeamId is not null)
        {
            bool hasTeamMembership = await db.OrganizationTeamMembers.AnyAsync(
                item => item.TeamId == inviteCode.TeamId.Value && item.UserId == user.Id, cancellationToken);
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

        return AppResult<OrganizationJoinResultDto>.Ok(new OrganizationJoinResultDto(
            inviteCode.Organization.ToDto(),
            member.ToDto(),
            inviteCode.TeamId));
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

    private static string HashInviteCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeInviteCode(code))));

    /// <summary>Case, dashes, and stray spaces are presentation — the code itself is what remains.</summary>
    private static string NormalizeInviteCode(string code) =>
        code.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
}
