namespace ScoutHaul.Api.Services;

public interface ICurrentUserContext
{
    Guid OrganizationId { get; }
    Guid UserId { get; }

    Task ResolveAsync();
}
