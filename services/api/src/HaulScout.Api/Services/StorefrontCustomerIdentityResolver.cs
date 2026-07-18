using System.Security.Claims;
using HaulScout.Application.Storefront;

namespace HaulScout.Api.Services;

/// <summary>
/// HTTP adapter for public storefront buyer identity. It reads the authenticated JWT but deliberately does not touch
/// operator AppUser or OrganizationMember state.
/// </summary>
public sealed class StorefrontCustomerIdentityResolver(IHttpContextAccessor httpContextAccessor)
{
    public StorefrontCustomerIdentity Resolve()
    {
        ClaimsPrincipal user = httpContextAccessor.HttpContext?.User
            ?? throw new UnauthorizedAccessException("Customer sign-in is required.");

        if (user.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("Customer sign-in is required.");
        }

        string subject = ReadRequiredClaim(user, ClaimTypes.NameIdentifier, "sub");
        string? email = ReadClaim(user, ClaimTypes.Email, "email")?.ToLowerInvariant();
        string? displayName = ReadClaim(user, ClaimTypes.Name, "name", "full_name");
        string? phone = ReadClaim(user, ClaimTypes.MobilePhone, ClaimTypes.HomePhone, "phone");

        return StorefrontCustomerIdentity.Supabase(subject, email, displayName, phone);
    }

    private static string? ReadClaim(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string? value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string ReadRequiredClaim(ClaimsPrincipal user, params string[] claimTypes) =>
        ReadClaim(user, claimTypes)
            ?? throw new UnauthorizedAccessException($"Missing '{claimTypes[0]}' claim in customer JWT token.");
}
