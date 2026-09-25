namespace HaulScout.Infrastructure.Inventory;

/// <summary>
/// The three things storing an image in a bucket actually needs. It exists so that the rules about photos — how keys
/// are built, when the database columns are still read, what a missing object means — are written against something
/// small enough to stand a fake, and so the storage SDK is confined to the one adapter that speaks it.
/// </summary>
public interface IPhotoObjectClient
{
    Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken);

    /// <summary>The object's bytes, or null when the bucket does not have it.</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
