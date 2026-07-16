namespace HaulScout.Application.Security;

public interface ICurrentUserContext
{
    /// <summary>The active organization, or throws if the user is org-less and must onboard.</summary>
    Guid OrganizationId { get; }

    /// <summary>The active organization, or null when the user has no organization yet.</summary>
    Guid? ActiveOrganizationId { get; }

    Guid UserId { get; }

    Task ResolveAsync();
}
