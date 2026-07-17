using System.Security.Cryptography;
using System.Text;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Organizations;

/// <summary>
/// Shareable invite codes. The plaintext code is shown exactly once at creation and only its hash is stored, so a
/// leaked database cannot be redeemed from. Codes are matched on a hash of the normalized code, which is what lets an
/// operator type it in any case or spacing. A code may never grant owner-level access — that always needs a person.
/// </summary>
public sealed class OrganizationInviteCodeWorkflowService(
    IOrganizationInviteCodeStore inviteCodes,
    IOrganizationRoleStore roles,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer authorizer,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<OrganizationInviteCodeDto>> ListAsync(Guid id, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        IReadOnlyList<OrganizationInviteCode> records = await inviteCodes.ListAsync(id, cancellationToken);

        return records.Select(item => item.ToDto()).ToArray();
    }

    public async Task<AppResult<OrganizationInviteCodeCreatedDto>> CreateAsync(
        Guid id,
        CreateOrganizationInviteCodeRequest request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Invite code name is required.");
        }
        if (request.MaxUses is <= 0)
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Max uses must be greater than zero.");
        }
        if (request.ExpiresAt is not null && request.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Expiration must be in the future.");
        }
        if (!await inviteCodes.OrganizationExistsAsync(id, cancellationToken))
        {
            return AppResult<OrganizationInviteCodeCreatedDto>.NotFound();
        }

        OrganizationTeam? team = null;
        if (request.TeamId is not null)
        {
            team = await inviteCodes.FindTeamAsync(id, request.TeamId.Value, cancellationToken);
            if (team is null)
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Team does not belong to this organization.");
            }
        }

        OrganizationRoleDefinition? role;
        if (request.RoleId is not null)
        {
            role = await roles.FindAsync(id, request.RoleId.Value, cancellationToken);
            if (role is null)
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Role does not belong to this organization.");
            }
            // A code is a bearer token — owner access must never be obtainable by redeeming one.
            if (OrganizationRoleRules.RoleRequiresOwner(role))
            {
                return AppResult<OrganizationInviteCodeCreatedDto>.Validation("Invite codes cannot grant owner-level permissions.");
            }
        }
        else
        {
            role = await roles.FindDefaultInviteRoleAsync(id, cancellationToken);
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
            codeAlreadyExists = await inviteCodes.CodeHashExistsAsync(codeHash, cancellationToken);
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

        await inviteCodes.AddAsync(inviteCode, cancellationToken);
        await inviteCodes.SaveChangesAsync(cancellationToken);

        inviteCode.Team = team;
        inviteCode.RoleDefinition = role;

        // The plaintext code is returned here and never again — only its hash is stored.
        return AppResult<OrganizationInviteCodeCreatedDto>.Ok(new OrganizationInviteCodeCreatedDto(inviteCode.ToDto(), code));
    }

    public async Task<AppResult<OrganizationInviteCodeDto>> DeactivateAsync(Guid id, Guid inviteCodeId, CancellationToken cancellationToken)
    {
        await authorizer.RequirePermission(id, HaulScoutPermissions.ManageMembers, cancellationToken);

        OrganizationInviteCode? inviteCode = await inviteCodes.FindAsync(id, inviteCodeId, cancellationToken);
        if (inviteCode is null)
        {
            return AppResult<OrganizationInviteCodeDto>.NotFound();
        }

        inviteCode.IsActive = false;
        await inviteCodes.SaveChangesAsync(cancellationToken);
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
        OrganizationInviteCode? inviteCode = await inviteCodes.FindByCodeHashAsync(codeHash, cancellationToken);

        // Same message whether the code never existed or belongs to a dead organization — no probing.
        if (inviteCode?.Organization is null)
        {
            return AppResult<OrganizationJoinResultDto>.Validation("Invite code is invalid.");
        }

        if (!inviteCode.IsActive
            || (inviteCode.ExpiresAt.HasValue && inviteCode.ExpiresAt.Value <= timeProvider.GetUtcNow())
            || inviteCode.MaxUses <= inviteCode.UseCount)
        {
            return AppResult<OrganizationJoinResultDto>.Validation("Invite code is expired or inactive.");
        }

        AppUser user = await inviteCodes.FindUserAsync(currentUser.UserId, cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before creating an organization.");

        OrganizationMember? member = await inviteCodes.FindMemberAsync(inviteCode.OrganizationId, user.Id, cancellationToken);

        if (member is null)
        {
            member = new OrganizationMember
            {
                OrganizationId = inviteCode.OrganizationId,
                UserId = user.Id,
                RoleId = inviteCode.RoleId,
                Role = OrganizationRole.Member,
                JoinedAt = timeProvider.GetUtcNow()
            };
            await inviteCodes.AddMemberAsync(member, cancellationToken);
        }

        if (inviteCode.TeamId is not null)
        {
            bool hasTeamMembership = await inviteCodes.TeamMembershipExistsAsync(inviteCode.TeamId.Value, user.Id, cancellationToken);
            if (!hasTeamMembership)
            {
                await inviteCodes.AddTeamMemberAsync(new OrganizationTeamMember
                {
                    TeamId = inviteCode.TeamId.Value,
                    UserId = user.Id,
                    RoleId = inviteCode.RoleId,
                    JoinedAt = timeProvider.GetUtcNow()
                }, cancellationToken);
            }
        }

        inviteCode.UseCount++;
        await inviteCodes.SaveChangesAsync(cancellationToken);

        member.User = user;
        member.RoleDefinition ??= inviteCode.RoleDefinition;

        return AppResult<OrganizationJoinResultDto>.Ok(new OrganizationJoinResultDto(
            new OrganizationDto(inviteCode.Organization.Id, inviteCode.Organization.Name, inviteCode.Organization.CreatedAt),
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
