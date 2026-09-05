using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

public sealed class IdempotencyRecord : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public required string Key { get; set; }
    public required string Method { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// When the request this key belongs to finished. Null means one is running right now.
    ///
    /// The row is written before the work starts, not after, so the unique index on (org, key)
    /// decides which of two racing requests does it. Without that both passed the "have I seen this
    /// key" check, both ran, and the index only caught the second bookkeeping row — after a second
    /// sale had already been recorded.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
