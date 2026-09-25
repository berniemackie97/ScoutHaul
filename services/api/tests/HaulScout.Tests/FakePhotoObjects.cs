using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

/// <summary>
/// A bucket, in a dictionary. It records what it was asked to do and can be told to refuse, which is what a real one
/// eventually does.
/// </summary>
public sealed class FakePhotoObjects : IPhotoObjectClient
{
    public Dictionary<string, (byte[] Bytes, string ContentType)> Stored { get; } = [];

    public List<string> Deleted { get; } = [];

    public int Puts { get; private set; }

    public bool Refuses { get; set; }

    public Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken)
    {
        Refuse();
        Puts++;
        Stored[key] = (bytes, contentType);
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        Refuse();
        return Task.FromResult(Stored.TryGetValue(key, out (byte[] Bytes, string ContentType) found) ? found.Bytes : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Refuse();
        Deleted.Add(key);
        Stored.Remove(key);
        return Task.CompletedTask;
    }

    private void Refuse()
    {
        if (Refuses)
        {
            throw new InvalidOperationException("The bucket is unreachable.");
        }
    }
}
