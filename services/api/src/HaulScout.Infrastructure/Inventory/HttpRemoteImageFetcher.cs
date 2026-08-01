using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using HaulScout.Application.Inventory;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Downloads an operator-supplied image URL over HTTP.
///
/// The URL comes from a signed-in operator pasting a link (their own listing on eBay/Vinted/etc.), so
/// this is a deliberate outbound fetch — but it is still attacker-influenceable input, so it is
/// treated as an SSRF surface: only http/https, only public IP destinations (every resolved address
/// is checked, so a public hostname pointing at 127.0.0.1 or a link-local metadata endpoint is
/// rejected), only image content types, and a hard byte cap enforced while streaming.
/// </summary>
internal sealed class HttpRemoteImageFetcher(HttpClient httpClient, IConfiguration configuration) : IRemoteImageFetcher
{
    private long MaxBytes => configuration.GetValue("Storage:InventoryPhotos:MaxBytes", 8_000_000);

    public async Task<PhotoUpload> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri))
        {
            throw new RemoteImageFetchException("That is not a valid image URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new RemoteImageFetchException("Only http and https image links can be fetched.");
        }

        await EnsurePublicDestinationAsync(uri, cancellationToken);

        using HttpRequestMessage request = new(HttpMethod.Get, uri);
        // Some marketplace CDNs reject requests without a browser-ish UA.
        request.Headers.TryAddWithoutValidation("User-Agent", "HaulScout/1.0 (+inventory photo import)");
        request.Headers.TryAddWithoutValidation("Accept", "image/*");

        using HttpResponseMessage response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new RemoteImageFetchException(
                response.StatusCode == HttpStatusCode.NotFound
                    ? "That image link could not be found (404). Copy the image address again."
                    : $"The image could not be downloaded ({(int)response.StatusCode}).");
        }

        string? contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType is null || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteImageFetchException(
                "That link is not a direct image. Use \"Copy image address\" rather than the page URL.");
        }

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxBytes)
        {
            throw new RemoteImageFetchException($"That image is larger than the {MaxBytes / 1_000_000}MB limit.");
        }

        // Buffer with a hard cap — a missing/lying Content-Length must not let an unbounded body through.
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        MemoryStream buffer = new(capacity: (int)Math.Min(declaredLength ?? 256_000, 4_000_000));
        byte[] chunk = new byte[81_920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new RemoteImageFetchException($"That image is larger than the {MaxBytes / 1_000_000}MB limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw new RemoteImageFetchException("That image link returned no data.");
        }

        buffer.Position = 0;
        string fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.Contains('.'))
        {
            fileName = $"imported.{contentType.Split('/').Last()}";
        }

        return new PhotoUpload(buffer, fileName, contentType, buffer.Length);
    }

    /// <summary>Rejects loopback/private/link-local destinations so a pasted URL cannot reach internal services.</summary>
    private static async Task EnsurePublicDestinationAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out IPAddress? literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (SocketException)
            {
                throw new RemoteImageFetchException("That image host could not be resolved.");
            }
        }

        if (addresses.Length == 0 || addresses.Any(IsPrivate))
        {
            throw new RemoteImageFetchException("That image host is not allowed.");
        }
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal) return true;
            if (address.IsIPv4MappedToIPv6) return IsPrivate(address.MapToIPv4());
            return false;
        }

        byte[] octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,                                   // 10.0.0.0/8
            127 => true,                                  // loopback
            169 when octets[1] == 254 => true,             // link-local incl. cloud metadata
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16.0.0/12
            192 when octets[1] == 168 => true,             // 192.168.0.0/16
            0 => true,
            _ => false,
        };
    }
}
