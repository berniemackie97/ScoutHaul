using Microsoft.Extensions.Configuration;
using HaulScout.Application.Inventory;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

/// <summary>
/// The image URL comes from a request, so fetching it is an SSRF surface. Every case here is refused
/// before any HTTP call is made, which is why none of these tests touch the network.
///
/// This guard now also covers vision identification: that endpoint used to download the URL with a
/// bare HttpClient.GetByteArrayAsync and no checks at all.
/// </summary>
public sealed class RemoteImageFetcherSsrfTests
{
    private static HttpRemoteImageFetcher Fetcher() =>
        new(new HttpClient(), new ConfigurationBuilder().Build());

    private static async Task<string> Rejects(string url)
    {
        RemoteImageFetchException error = await Assert.ThrowsAsync<RemoteImageFetchException>(
            () => Fetcher().FetchAsync(url, CancellationToken.None));
        return error.Message;
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/image.png")]
    [InlineData("gopher://example.com/image.png")]
    public async Task RefusesSchemesThatAreNotHttp(string url) => await Rejects(url);

    [Theory]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("http://localhost/image.png")]
    [InlineData("http://[::1]/image.png")]
    public async Task RefusesLoopback(string url) => await Rejects(url);

    [Theory]
    // The cloud metadata endpoint — the payload that turns an SSRF into stolen infrastructure
    // credentials on AWS, GCP and Azure alike.
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
    public async Task RefusesCloudMetadata(string url) => await Rejects(url);

    [Theory]
    [InlineData("http://10.0.0.1/image.png")]
    [InlineData("http://192.168.1.1/image.png")]
    [InlineData("http://172.16.0.1/image.png")]
    public async Task RefusesPrivateNetworks(string url) => await Rejects(url);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path.png")]
    public async Task RefusesAnythingThatIsNotAnAbsoluteUrl(string url) => await Rejects(url);

    [Fact]
    public async Task ExplainsTheRefusalInTermsAnOperatorCanActOn()
    {
        string message = await Rejects("gopher://example.com/image.png");
        Assert.Contains("http", message, StringComparison.OrdinalIgnoreCase);
    }
}
