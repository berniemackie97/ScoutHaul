using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

public sealed class Organization : EntityBase
{
    public required string Name { get; set; }

    /// <summary>
    /// Public storefront domain this organization owns (e.g. "overhaultrading.com").
    /// When set, the public storefront API resolves this org by domain — no hardcoded
    /// org id anywhere. Null = this org has no public storefront.
    /// </summary>
    public string? StoreDomain { get; set; }

    /// <summary>
    /// The IANA time zone the business works in. Marketplace exports that print local dates and times
    /// with no offset — Depop's "Date of sale" and "Time of sale" — are read in this zone, and days
    /// in reports are bucketed by it. Defaults to US Pacific, where the business is.
    /// </summary>
    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public const string DefaultTimeZoneId = "America/Los_Angeles";

    /// <summary>The zone an organization keeps its days in; an unknown id falls back to the default rather than failing.</summary>
    public static TimeZoneInfo ZoneOf(string? timeZoneId) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId ?? DefaultTimeZoneId, out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);

    /// <summary>Whether marketplace listings keep this organization's item photos up to date.</summary>
    public bool MarketplacePhotoSyncEnabled { get; set; }

    public ICollection<OrganizationMember> Members { get; set; } = [];
    public ICollection<OrganizationRoleDefinition> Roles { get; set; } = [];
    public ICollection<OrganizationTeam> Teams { get; set; } = [];
    public ICollection<OrganizationInviteCode> InviteCodes { get; set; } = [];
}

public sealed class AppUser : EntityBase
{
    public string? ExternalSubject { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public ICollection<OrganizationMember> Memberships { get; set; } = [];
    public ICollection<ExternalAuthAccount> ExternalAccounts { get; set; } = [];
}

public sealed class ExternalAuthAccount : EntityBase
{
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public required string Provider { get; set; }
    public required string ProviderAccountId { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganizationMember : EntityBase
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid? RoleId { get; set; }
    public OrganizationRoleDefinition? RoleDefinition { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganizationRoleDefinition : EntityBase
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsOwnerRole { get; set; }
    public ICollection<OrganizationRolePermission> Permissions { get; set; } = [];
}

public sealed class OrganizationRolePermission : EntityBase
{
    public Guid RoleId { get; set; }
    public OrganizationRoleDefinition? Role { get; set; }
    public required string Permission { get; set; }
}

public sealed class OrganizationTeam : EntityBase
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ICollection<OrganizationTeamMember> Members { get; set; } = [];
}

public sealed class OrganizationTeamMember : EntityBase
{
    public Guid TeamId { get; set; }
    public OrganizationTeam? Team { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }
    public Guid? RoleId { get; set; }
    public OrganizationRoleDefinition? RoleDefinition { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganizationInviteCode : EntityBase
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? TeamId { get; set; }
    public OrganizationTeam? Team { get; set; }
    public Guid? RoleId { get; set; }
    public OrganizationRoleDefinition? RoleDefinition { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public required string Name { get; set; }
    public required string CodeHash { get; set; }
    public required string CodePrefix { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UseCount { get; set; }
}
