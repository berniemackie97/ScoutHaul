namespace HaulScout.Application.Inventory;

/// <summary>
/// Fetches an image the operator found somewhere else (their own live listing on eBay, Vinted, a
/// supplier page) so it can be attached to an inventory item without saving it to a device first.
/// Downloading from an operator-supplied URL is a mechanism — and an SSRF surface — so the rules for
/// what may be fetched live in Infrastructure behind this port.
/// </summary>
public interface IRemoteImageFetcher
{
    /// <summary>
    /// Downloads <paramref name="url"/> as an image ready for the photo processor.
    /// Throws <see cref="RemoteImageFetchException"/> with an operator-readable reason when the URL
    /// is not a fetchable public image.
    /// </summary>
    Task<PhotoUpload> FetchAsync(string url, CancellationToken cancellationToken);
}

/// <summary>A remote image could not be fetched. The message is safe to show the operator.</summary>
public sealed class RemoteImageFetchException(string message) : Exception(message);
