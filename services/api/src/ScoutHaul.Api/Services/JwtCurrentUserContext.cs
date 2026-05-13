using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Services;

public sealed class JwtCurrentUserContext(IHttpContextAccessor httpContextAccessor, ScoutHaulDbContext db) : ICurrentUserContext
{
    private const string OrganizationHeader = "X-ScoutHaul-Organization-Id";
    private ResolvedUserContext? resolved;

    public Guid OrganizationId => Resolve().OrganizationId;
    public Guid UserId => Resolve().UserId;

    public static bool HasAuthenticatedPrincipal(ClaimsPrincipal? user)
    {
        return user?.Identity?.IsAuthenticated == true;
    }

    private ResolvedUserContext Resolve()
    {
        if (resolved is not null)
        {
            return resolved;
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

        AppUser appUser = ResolveUser(externalSubject, email, displayName);
        Guid? requestedOrganizationId = ReadGuidHeader(OrganizationHeader);
        OrganizationMember membership = ResolveMembership(appUser, requestedOrganizationId);

        db.SaveChanges();
        resolved = new ResolvedUserContext(membership.OrganizationId, appUser.Id);
        return resolved;
    }

    private AppUser ResolveUser(string externalSubject, string email, string displayName)
    {
        string providerSubject = $"supabase:{externalSubject}";
        AppUser? appUser = db.Users
            .Include(item => item.ExternalAccounts)
            .SingleOrDefault(item => item.ExternalSubject == providerSubject);

        appUser ??= db.ExternalAuthAccounts
            .Include(item => item.User)
            .Where(item => item.Provider == "supabase" && item.ProviderAccountId == externalSubject)
            .Select(item => item.User)
            .SingleOrDefault();

        appUser ??= db.Users.SingleOrDefault(item => item.Email == email);

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

        bool hasAccount = db.ExternalAuthAccounts.Any(item =>
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

    private OrganizationMember ResolveMembership(AppUser appUser, Guid? requestedOrganizationId)
    {
        if (requestedOrganizationId is not null)
        {
            OrganizationMember? requestedMembership = db.OrganizationMembers.SingleOrDefault(item =>
                item.OrganizationId == requestedOrganizationId.Value && item.UserId == appUser.Id);

            if (requestedMembership is null)
            {
                throw new UnauthorizedAccessException("User is not a member of the requested organization.");
            }

            return requestedMembership;
        }

        OrganizationMember? existingMembership = db.OrganizationMembers
            .Where(item => item.UserId == appUser.Id)
            .OrderBy(item => item.JoinedAt)
            .FirstOrDefault();

        if (existingMembership is not null)
        {
            return existingMembership;
        }

        var organization = new Organization { Name = $"{appUser.DisplayName}'s workspace" };
        var ownerRole = new OrganizationRoleDefinition
        {
            OrganizationId = organization.Id,
            Name = "Owner",
            Description = "Full workspace control, including owners, billing, roles, teams, and business data.",
            IsSystem = true,
            IsOwnerRole = true
        };

        foreach (string permission in ScoutHaulPermissions.All)
        {
            ownerRole.Permissions.Add(new OrganizationRolePermission { Permission = permission });
        }

        var membership = new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = appUser.Id,
            RoleId = ownerRole.Id,
            Role = OrganizationRole.Owner
        };

        db.Organizations.Add(organization);
        db.OrganizationRoles.Add(ownerRole);
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
