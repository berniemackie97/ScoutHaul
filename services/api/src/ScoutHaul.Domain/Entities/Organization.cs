using ScoutHaul.Domain.Common;

namespace ScoutHaul.Domain.Entities;

public sealed class Organization : EntityBase
{
    public required string Name { get; set; }
    public ICollection<OrganizationMember> Members { get; set; } = [];
}

public sealed class AppUser : EntityBase
{
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public ICollection<OrganizationMember> Memberships { get; set; } = [];
}

public sealed class OrganizationMember : EntityBase
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
