using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Services;

public sealed class JwtCurrentUserContext(IHttpContextAccessor httpContextAccessor, ScoutHaulDbContext db) : ICurrentUserContext
{
    private const string OrganizationHeader = "X-ScoutHaul-Organization-Id";
    private ResolvedUserContext? resolved;

    public Guid OrganizationId => resolved?.OrganizationId
        ?? throw new InvalidOperationException("ResolveAsync must be called before accessing OrganizationId.");

    public Guid UserId => resolved?.UserId
        ?? throw new InvalidOperationException("ResolveAsync must be called before accessing UserId.");

    public static bool HasAuthenticatedPrincipal(ClaimsPrincipal? user)
    {
        return user?.Identity?.IsAuthenticated == true;
    }

    public async Task ResolveAsync()
    {
        if (resolved is not null)
        {
            return;
        }

        ClaimsPrincipal claims = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("Authenticated user context is unavailable.");

        if (!HasAuthenticatedPrincipal(claims))
        {
            throw new UnauthorizedAccessException("Authenticated user context is unavailable.");
        }

        string externalSubject = ReadRequiredClaim(claims, ClaimTypes.NameIdentifier, "sub");
        string email = NormalizeEmail(ReadClaim(claims, ClaimTypes.Email, "email"), externalSubject);
        string displayName = TrimTo(ReadClaim(claims, ClaimTypes.Name, "name", "full_name") ?? email.Split('@')[0], 160);

        AppUser appUser = await ResolveUserAsync(externalSubject, email, displayName);
        Guid? requestedOrganizationId = ReadGuidHeader(OrganizationHeader);
        OrganizationMember membership = await ResolveMembershipAsync(appUser, requestedOrganizationId);

        await db.SaveChangesAsync();
        resolved = new ResolvedUserContext(membership.OrganizationId, appUser.Id);
    }

    private async Task<AppUser> ResolveUserAsync(string externalSubject, string email, string displayName)
    {
        string providerSubject = $"supabase:{externalSubject}";
        AppUser? appUser = await db.Users
            .Include(item => item.ExternalAccounts)
            .SingleOrDefaultAsync(item => item.ExternalSubject == providerSubject);

        appUser ??= await db.ExternalAuthAccounts
            .Include(item => item.User)
            .Where(item => item.Provider == "supabase" && item.ProviderAccountId == externalSubject)
            .Select(item => item.User)
            .SingleOrDefaultAsync();

        appUser ??= await db.Users.SingleOrDefaultAsync(item => item.Email == email);

        if (appUser is null)
        {
            appUser = new AppUser
            {
                ExternalSubject = providerSubject,
                Email = email,
                DisplayName = displayName
            };
            db.Users.Add(appUser);
        }
        else
        {
            appUser.ExternalSubject ??= providerSubject;
            appUser.Email = email;
            appUser.DisplayName = displayName;
        }

        bool hasAccount = await db.ExternalAuthAccounts.AnyAsync(item =>
            item.Provider == "supabase" && item.ProviderAccountId == externalSubject);

        if (!hasAccount)
        {
            db.ExternalAuthAccounts.Add(new ExternalAuthAccount
            {
                UserId = appUser.Id,
                Provider = "supabase",
                ProviderAccountId = externalSubject,
                Email = email,
                DisplayName = displayName
            });
        }

        return appUser;
    }

    private async Task<OrganizationMember> ResolveMembershipAsync(AppUser appUser, Guid? requestedOrganizationId)
    {
        if (requestedOrganizationId is not null)
        {
            OrganizationMember? requestedMembership = await db.OrganizationMembers.SingleOrDefaultAsync(item =>
                item.OrganizationId == requestedOrganizationId.Value && item.UserId == appUser.Id);

            if (requestedMembership is null)
            {
                throw new UnauthorizedAccessException("User is not a member of the requested organization.");
            }

            return requestedMembership;
        }

        OrganizationMember? existingMembership = await db.OrganizationMembers
            .Where(item => item.UserId == appUser.Id)
            .OrderBy(item => item.JoinedAt)
            .FirstOrDefaultAsync();

        if (existingMembership is not null)
        {
            return existingMembership;
        }

        var organization = new Organization { Name = $"{appUser.DisplayName}'s workspace" };
        OrganizationRoleDefinition ownerRole = OrganizationRoleDefaults.CreateOwnerRole(organization.Id);
        OrganizationRoleDefinition operatorRole = OrganizationRoleDefaults.CreateOperatorRole(organization.Id);

        var membership = new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = appUser.Id,
            RoleId = ownerRole.Id,
            Role = OrganizationRole.Owner
        };

        db.Organizations.Add(organization);
        db.OrganizationRoles.AddRange(ownerRole, operatorRole);
        db.OrganizationMembers.Add(membership);
        return membership;
    }

    private static string? ReadClaim(ClaimsPrincipal? user, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string? value = user?.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string ReadRequiredClaim(ClaimsPrincipal? user, params string[] claimTypes)
    {
        return ReadClaim(user, claimTypes)
            ?? throw new UnauthorizedAccessException($"Missing '{claimTypes[0]}' claim in JWT token.");
    }

    private Guid? ReadGuidHeader(string header)
    {
        string? value = httpContextAccessor.HttpContext?.Request.Headers[header].FirstOrDefault();
        return Guid.TryParse(value, out Guid id) ? id : null;
    }

    private static string NormalizeEmail(string? email, string externalSubject)
    {
        return !string.IsNullOrWhiteSpace(email)
            ? TrimTo(email.Trim().ToLowerInvariant(), 320)
            : $"user-{externalSubject}@auth.scouthaul.local";
    }

    private static string TrimTo(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record ResolvedUserContext(Guid OrganizationId, Guid UserId);
}
