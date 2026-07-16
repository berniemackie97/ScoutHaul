using System.Text.RegularExpressions;

namespace HaulScout.Application.Services;

/// <summary>
/// The storefront resolves an organization by its bare host, so operator input has to be reduced to exactly that
/// before it is stored or compared. Pure text rules with no persistence — kept here so they can be exercised
/// directly rather than only through an HTTP call.
/// </summary>
public static partial class StoreDomainNormalizer
{
    /// <summary>
    /// Normalizes operator input to a bare lowercase host (strips scheme, <c>www.</c>, port, path, query/fragment,
    /// and any trailing dot) and validates it as a DNS hostname. Returns null when the input is not a valid domain.
    /// </summary>
    public static string? Normalize(string input)
    {
        string value = input.Trim().ToLowerInvariant();
        value = SchemePrefix().Replace(value, "");   // strip http(s)://
        value = value.Split('/')[0];                  // drop any path
        value = value.Split('?')[0].Split('#')[0];    // drop query / fragment
        value = value.Split(':')[0];                  // drop port
        if (value.StartsWith("www.", StringComparison.Ordinal))
        {
            value = value[4..];
        }
        value = value.TrimEnd('.');
        return HostName().IsMatch(value) ? value : null;
    }

    [GeneratedRegex(@"^https?://")]
    private static partial Regex SchemePrefix();

    // RFC-1123-style hostname: dot-separated labels of alphanumerics and hyphens (no leading or
    // trailing hyphen), a TLD of at least two letters, and a total length of at most 253 chars.
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex HostName();
}
