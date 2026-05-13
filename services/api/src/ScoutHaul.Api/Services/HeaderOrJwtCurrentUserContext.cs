namespace ScoutHaul.Api.Services;

public sealed class HeaderOrJwtCurrentUserContext(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration,
    JwtCurrentUserContext jwtCurrentUser) : ICurrentUserContext
{
    private const string OrganizationHeader = "X-ScoutHaul-Organization-Id";
    private const string UserHeader = "X-ScoutHaul-User-Id";

    public Guid OrganizationId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (JwtCurrentUserContext.HasAuthenticatedPrincipal(user))
            {
                return jwtCurrentUser.OrganizationId;
            }

            return ReadGuidHeader(OrganizationHeader) ?? ReadRequiredConfiguredGuid("StubAuth:DefaultOrganizationId");
        }
    }

    public Guid UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (JwtCurrentUserContext.HasAuthenticatedPrincipal(user))
            {
                return jwtCurrentUser.UserId;
            }

            return ReadGuidHeader(UserHeader) ?? ReadRequiredConfiguredGuid("StubAuth:DefaultUserId");
        }
    }

    private Guid? ReadGuidHeader(string header)
    {
        string? value = httpContextAccessor.HttpContext?.Request.Headers[header].FirstOrDefault();
        return Guid.TryParse(value, out Guid id) ? id : null;
    }

    private Guid ReadRequiredConfiguredGuid(string key)
    {
        string? value = configuration[key];
        if (Guid.TryParse(value, out Guid id))
        {
            return id;
        }

        throw new InvalidOperationException($"{key} must be configured as a GUID.");
    }
}
