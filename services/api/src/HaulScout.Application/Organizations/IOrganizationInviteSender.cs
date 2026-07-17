namespace HaulScout.Application.Organizations;

public interface IOrganizationInviteSender
{
    Task<bool> SendInviteAsync(
        string email,
        string displayName,
        Guid organizationId,
        string organizationName,
        CancellationToken cancellationToken);
}
