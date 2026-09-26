using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sessions;

/// <summary>
/// Builds the payload a client needs on sign-in: who the user is, the organizations they belong to, the active one,
/// and the org-scoped record counts a mobile client reconciles its local store against.
/// </summary>
public sealed class SessionBootstrapService(ISessionBootstrapStore sessions, ICurrentUserContext currentUser, IOrganizationAuthorizer access)
{
    public async Task<SessionBootstrapDto> BuildAsync(CancellationToken cancellationToken)
    {
        await currentUser.ResolveAsync();

        Guid userId = currentUser.UserId;
        // Null when the user is org-less (onboarding). Org-scoped counts then naturally come back 0.
        Guid? organizationId = currentUser.ActiveOrganizationId;

        // EF Core DbContext not thread-safe — sequential queries
        AppUser user = await sessions.FindUserAsync(userId, cancellationToken)
                       ?? throw new InvalidOperationException("User must be resolved before bootstrapping a session.");
        IReadOnlyList<OrganizationDto> organizations = await sessions.ListOrganizationsAsync(userId, cancellationToken);
        SessionRemoteCountsDto counts = await sessions.GetRemoteCountsAsync(organizationId, cancellationToken);
        IReadOnlyList<string> permissions = organizationId is Guid organization ? await access.ListPermissions(organization, cancellationToken) : [];

        return new SessionBootstrapDto(
            new SessionUserDto(user.Id, user.Email, user.DisplayName),
            organizations,
            organizationId,
            counts,
            DateTimeOffset.UtcNow,
            permissions);
    }
}
