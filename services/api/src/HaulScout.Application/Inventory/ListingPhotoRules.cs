using System.Security.Cryptography;
using System.Text;

namespace HaulScout.Application.Inventory;

/// <summary>
/// The rules for taking a marketplace listing's photos as an item's photos: how many are taken, how a set of them
/// is recognised again, and which addresses are followed at all.
/// </summary>
public static class ListingPhotoRules
{
    /// <summary>
    /// The most photos taken from one listing. A storefront gallery is served by a handful, and every photo kept is
    /// a photo stored, so the rest of a long marketplace gallery is weight without a reader.
    /// </summary>
    public const int MaxPhotosPerListing = 8;

    /// <summary>The longest side a photo is kept at. Big enough to look right on a product page, small enough to store.</summary>
    public const int MaxPixels = 1600;

    /// <summary>
    /// The image hosts each marketplace serves its listings' photos from. A listing's photo address is followed
    /// only when it points at one of them: the addresses arrive from a browser, and an address is an instruction to
    /// fetch. A marketplace not named here syncs no photos rather than following wherever it is pointed.
    /// </summary>
    private static readonly Dictionary<string, string[]> ImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eBay"] = [".ebayimg.com", ".ebaystatic.com"],
        ["Vinted"] = [".vinted.net"],
    };

    /// <summary>A short, stable key for one set of photo addresses, so a listing read again with the same photos costs nothing.</summary>
    public static string KeyOf(IEnumerable<string> urls)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', urls)));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    /// <summary>Names the photos an item is showing, so the item itself says which listing they came from.</summary>
    public static string SourceKey(string platform, string externalListingId, string photoUrlsKey) =>
        $"{SourceOwner(platform, externalListingId)}:{photoUrlsKey}";

    /// <summary>The listing a set of photos came from, without which set it was.</summary>
    public static string SourceOwner(string platform, string externalListingId) =>
        $"{platform}:{externalListingId}";

    /// <summary>Whether an item is already showing photos from this listing, whichever set.</summary>
    public static bool CameFrom(string? sourceKey, string platform, string externalListingId) =>
        sourceKey is not null && sourceKey.StartsWith($"{SourceOwner(platform, externalListingId)}:", StringComparison.Ordinal);

    /// <summary>Whether a photo address is one of the marketplace's own, and may be fetched.</summary>
    public static bool IsTrustedImageAddress(string platform, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || address.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return ImageHosts.TryGetValue(platform, out string[]? hosts)
            && hosts.Any(host => address.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase));
    }
}
