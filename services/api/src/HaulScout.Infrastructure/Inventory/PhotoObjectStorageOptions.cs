namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// Where photo images are kept when they are kept outside the database, read from
/// <c>Storage:Photos:ObjectStorage</c>. Configuring a bucket is what turns object storage on; leaving it unset keeps
/// images in their columns, which is the right default for a deployment small enough not to care.
///
/// Written against S3's API rather than one vendor's, so any S3-compatible service works. Cloudflare R2 is what this
/// is run against: its endpoint is <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c> and its region is "auto".
/// </summary>
public sealed class PhotoObjectStorageOptions
{
    public const string Section = "Storage:Photos:ObjectStorage";

    /// <summary>The bucket photos are written to. Setting this is what enables object storage.</summary>
    public string? Bucket { get; set; }

    /// <summary>The S3-compatible endpoint. Required, because this is never Amazon's own S3 by default.</summary>
    public string? ServiceUrl { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    /// <summary>Signing region. R2 accepts, and documents, "auto".</summary>
    public string Region { get; set; } = "auto";

    /// <summary>
    /// An optional path every key is written under, so one bucket can hold more than one deployment without their
    /// keys colliding. A trailing slash is added when it is missing.
    /// </summary>
    public string? Prefix { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>
    /// Explains why a configured bucket cannot be used, or null when it can. Checked at startup so a deployment
    /// fails immediately and loudly rather than on the first photo someone uploads.
    /// </summary>
    public string? DescribeMisconfiguration()
    {
        if (!IsConfigured)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(ServiceUrl))
        {
            return $"{Section}:ServiceUrl is required when a bucket is configured.";
        }

        if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return $"{Section}:ServiceUrl must be an absolute https URL.";
        }

        if (string.IsNullOrWhiteSpace(AccessKeyId) || string.IsNullOrWhiteSpace(SecretAccessKey))
        {
            return $"{Section}:AccessKeyId and {Section}:SecretAccessKey are required when a bucket is configured.";
        }

        return null;
    }

    /// <summary>The prefix as it is used when building keys: empty, or ending in a single slash.</summary>
    public string NormalizedPrefix =>
        string.IsNullOrWhiteSpace(Prefix) ? string.Empty : $"{Prefix.Trim().Trim('/')}/";
}
