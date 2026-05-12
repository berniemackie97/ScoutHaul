using Microsoft.Extensions.Primitives;

namespace ScoutHaul.Api.Services;

public sealed class HeaderCurrentUserContext(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : ICurrentUserContext
{
    private const string OrganizationHeader = "X-ScoutHaul-Organization-Id";
    private const string UserHeader = "X-ScoutHaul-User-Id";

    public Guid OrganizationId => ReadGuidHeader(OrganizationHeader) ?? ReadRequiredConfiguredGuid("StubAuth:DefaultOrganizationId");
    public Guid UserId => ReadGuidHeader(UserHeader) ?? ReadRequiredConfiguredGuid("StubAuth:DefaultUserId");

    private Guid? ReadGuidHeader(string headerName)
    {
        HttpContext? httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null || !httpContext.Request.Headers.TryGetValue(headerName, out StringValues value))
        {
            return null;
        }

        return Guid.TryParse(value.FirstOrDefault(), out Guid id) ? id : null;
    }

    private Guid ReadRequiredConfiguredGuid(string key)
    {
        string? value = configuration[key];
        if (!Guid.TryParse(value, out Guid id))
        {
            throw new InvalidOperationException($"{key} must be configured as a GUID while stub auth is enabled.");
        }

        return id;
    }
}
