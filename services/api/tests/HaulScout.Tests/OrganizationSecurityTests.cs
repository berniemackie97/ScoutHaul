using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Api.Authorization;
using HaulScout.Api.Controllers;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// The organization surface decides who can do what, so its rules are the ones a refactor must not quietly change.
/// These pin the security-critical behaviour: invite codes are matched on a salt-free hash of the normalized code
/// (so case and dashes never matter), a code that is inactive/expired/used up cannot be redeemed, unknown
/// permissions are rejected, and anything granting owner-level access demands an owner.
/// </summary>
public class OrganizationSecurityTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (HaulScoutDbContext Db, Guid Org, Guid User) NewContext(string store)
    {
        Guid org = Guid.NewGuid();
        Guid user = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.Users.Add(new AppUser { Id = user, Email = "op@x.com", DisplayName = "Operator" });
        db.SaveChanges();
        return (db, org, user);
    }

    private static OrganizationsController NewController(HaulScoutDbContext db, Guid org, Guid user)
    {
        StubCurrentUser currentUser = new(org, user);
        OrganizationAccessService access = new(db, currentUser);
        OrganizationRoleGuard guard = new(db);
        SupabaseAdminInviteService invites = new(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<SupabaseAdminInviteService>.Instance);

        // Same composition DI builds in production — the controller owns no persistence of its own.
        return new OrganizationsController(
            new OrganizationWorkflowService(db, currentUser, access),
            new OrganizationMemberWorkflowService(db, access, guard, invites),
            new OrganizationInviteCodeWorkflowService(db, currentUser, access, guard),
            new OrganizationRoleWorkflowService(db, access, guard),
            new OrganizationTeamWorkflowService(db, access, guard));
    }

    /// <summary>Mirrors the controller's private hashing so a code can be seeded the way redemption will look it up.</summary>
    private static string HashCode(string code)
    {
        string normalized = code.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static OrganizationInviteCode SeedCode(
        HaulScoutDbContext db,
        Guid org,
        string code,
        int maxUses = 5,
        int useCount = 0,
        bool isActive = true,
        DateTimeOffset? expiresAt = null)
    {
        OrganizationInviteCode invite = new()
        {
            OrganizationId = org,
            Name = "Test invite",
            CodeHash = HashCode(code),
            CodePrefix = "SH-TEST",
            MaxUses = maxUses,
            UseCount = useCount,
            IsActive = isActive,
            ExpiresAt = expiresAt,
        };
        db.OrganizationInviteCodes.Add(invite);
        db.SaveChanges();
        return invite;
    }

    [Fact]
    public async Task Invite_code_is_matched_ignoring_case_and_dashes()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Invite_code_is_matched_ignoring_case_and_dashes));
        await using HaulScoutDbContext _ = db;
        SeedCode(db, org, "SH-ABCD-1234-EF56");

        // The operator types it however they like — normalization is what makes the lookup work.
        ActionResult<OrganizationJoinResultDto> result = await NewController(db, org, user)
            .RedeemInviteCode(new RedeemOrganizationInviteCodeRequest(" sh abcd1234 ef56 "), CancellationToken.None);

        Assert.NotNull(result.Value);
        Assert.True(await db.OrganizationMembers.AnyAsync(item => item.OrganizationId == org && item.UserId == user));
    }

    [Fact]
    public async Task Redeeming_increments_the_use_count_and_joins_the_user()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(Redeeming_increments_the_use_count_and_joins_the_user));
        await using HaulScoutDbContext _ = db;
        SeedCode(db, org, "SH-AAAA-BBBB-CCCC", maxUses: 2);

        await NewController(db, org, user).RedeemInviteCode(
            new RedeemOrganizationInviteCodeRequest("SH-AAAA-BBBB-CCCC"), CancellationToken.None);

        OrganizationInviteCode code = await db.OrganizationInviteCodes.SingleAsync();
        Assert.Equal(1, code.UseCount);
        Assert.Equal(1, await db.OrganizationMembers.CountAsync(item => item.OrganizationId == org));
    }

    [Fact]
    public async Task An_expired_invite_code_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_expired_invite_code_is_rejected));
        await using HaulScoutDbContext _ = db;
        SeedCode(db, org, "SH-EXPI-RED0-0000", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        ActionResult<OrganizationJoinResultDto> result = await NewController(db, org, user)
            .RedeemInviteCode(new RedeemOrganizationInviteCodeRequest("SH-EXPI-RED0-0000"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.OrganizationMembers.AnyAsync());
    }

    [Fact]
    public async Task An_invite_code_at_its_use_limit_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_invite_code_at_its_use_limit_is_rejected));
        await using HaulScoutDbContext _ = db;
        SeedCode(db, org, "SH-USED-UPPP-0000", maxUses: 1, useCount: 1);

        ActionResult<OrganizationJoinResultDto> result = await NewController(db, org, user)
            .RedeemInviteCode(new RedeemOrganizationInviteCodeRequest("SH-USED-UPPP-0000"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.OrganizationMembers.AnyAsync());
    }

    [Fact]
    public async Task A_deactivated_invite_code_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_deactivated_invite_code_is_rejected));
        await using HaulScoutDbContext _ = db;
        SeedCode(db, org, "SH-DEAD-0000-0000", isActive: false);

        ActionResult<OrganizationJoinResultDto> result = await NewController(db, org, user)
            .RedeemInviteCode(new RedeemOrganizationInviteCodeRequest("SH-DEAD-0000-0000"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task An_unknown_invite_code_is_rejected()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(An_unknown_invite_code_is_rejected));
        await using HaulScoutDbContext _ = db;

        ActionResult<OrganizationJoinResultDto> result = await NewController(db, org, user)
            .RedeemInviteCode(new RedeemOrganizationInviteCodeRequest("SH-NOPE-NOPE-NOPE"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task A_role_cannot_be_created_with_an_unknown_permission()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_role_cannot_be_created_with_an_unknown_permission));
        await using HaulScoutDbContext _ = db;

        ActionResult<OrganizationRoleDto> result = await NewController(db, org, user).CreateRole(
            org,
            new UpsertOrganizationRoleRequest("Bad role", null, ["inventory.manage", "totally.made.up"], false),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.False(await db.OrganizationRoles.AnyAsync());
    }

    [Fact]
    public async Task A_role_needs_at_least_one_permission()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_role_needs_at_least_one_permission));
        await using HaulScoutDbContext _ = db;

        ActionResult<OrganizationRoleDto> result = await NewController(db, org, user).CreateRole(
            org,
            new UpsertOrganizationRoleRequest("Empty role", null, [], false),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>The heart of it: a non-owner must not be able to mint themselves owner-level access.</summary>
    [Fact]
    public async Task A_non_owner_cannot_create_a_role_granting_owner_access()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_non_owner_cannot_create_a_role_granting_owner_access));
        await using HaulScoutDbContext _ = db;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => NewController(db, org, user).CreateRole(
            org,
            new UpsertOrganizationRoleRequest("Sneaky owner", null, [], true),
            CancellationToken.None));

        Assert.False(await db.OrganizationRoles.AnyAsync());
    }

    [Fact]
    public async Task A_non_owner_cannot_create_a_role_carrying_an_owner_guarded_permission()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = NewContext(nameof(A_non_owner_cannot_create_a_role_carrying_an_owner_guarded_permission));
        await using HaulScoutDbContext _ = db;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => NewController(db, org, user).CreateRole(
            org,
            new UpsertOrganizationRoleRequest("Role editor", null, [HaulScoutPermissions.ManageRoles], false),
            CancellationToken.None));

        Assert.False(await db.OrganizationRoles.AnyAsync());
    }
}
