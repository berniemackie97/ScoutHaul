using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// <see cref="IPhotoObjectClient"/> over an S3-compatible bucket. Written against S3's API rather than one vendor's,
/// so Cloudflare R2, Backblaze B2 or S3 itself all work with nothing but a different endpoint.
/// </summary>
public sealed class S3PhotoObjectClient(IAmazonS3 client, IOptions<PhotoObjectStorageOptions> options) : IPhotoObjectClient
{
    private readonly string bucket = options.Value.Bucket
        ?? throw new InvalidOperationException($"{PhotoObjectStorageOptions.Section}:Bucket is required.");

    public async Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        using MemoryStream content = new(bytes, writable: false);
        await client.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = contentType,
                // The payload is already hashed by the signature; signing it again costs a second pass over every
                // image for nothing, and some S3-compatible services reject the chunked encoding it implies.
                DisablePayloadSigning = true,
            },
            cancellationToken);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            using GetObjectResponse response = await client.GetObjectAsync(bucket, key, cancellationToken);
            await using MemoryStream buffer = new();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        client.DeleteObjectAsync(bucket, key, cancellationToken);
}
