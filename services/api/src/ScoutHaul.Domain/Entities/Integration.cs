using ScoutHaul.Domain.Common;

namespace ScoutHaul.Domain.Entities;

public sealed class IdempotencyRecord : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public required string Key { get; set; }
    public required string Method { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
