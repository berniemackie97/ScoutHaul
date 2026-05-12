using System.Security.Claims;

namespace ScoutHaul.Api.Services;

public sealed class JwtCurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid OrganizationId => ReadRequiredClaim("org_id");
    public Guid UserId => ReadRequiredClaim(ClaimTypes.NameIdentifier);

    private Guid ReadRequiredClaim(string claimType)
    {
        ClaimsPrincipal? user = httpContextAccessor.HttpContext?.User;
        string? value = user?.FindFirst(claimType)?.Value;

        if (!Guid.TryParse(value, out Guid id))
        {
            throw new UnauthorizedAccessException($"Missing or invalid '{claimType}' claim in JWT token.");
        }

        return id;
    }
}
