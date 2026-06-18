namespace HaulScout.Api.Services;

public interface ICurrentUserContext
{
    /// <summary>The active organization, or throws if the user is org-less (must onboard).</summary>
    Guid OrganizationId { get; }

    /// <summary>The active organization, or null when the user has no organization yet (onboarding).</summary>
    Guid? ActiveOrganizationId { get; }

    Guid UserId { get; }

    Task ResolveAsync();
}
