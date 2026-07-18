namespace HaulScout.Application.Storefront;

/// <summary>
/// Authenticated public storefront buyer identity. This is intentionally not an operator <c>AppUser</c> identity:
/// customers can shop and manage their own offers, but they never receive organization membership or admin roles.
/// </summary>
public sealed record StorefrontCustomerIdentity(
    string Provider,
    string ProviderSubject,
    string ExternalSubject,
    string? Email,
    string? DisplayName,
    string? Phone)
{
    public static StorefrontCustomerIdentity Supabase(
        string subject,
        string? email,
        string? displayName,
        string? phone = null) =>
        new("supabase", subject, $"supabase:{subject}", Normalize(email), Normalize(displayName), Normalize(phone));

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
