using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Common;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// Buyers recorded twice before imports learned to recognise an existing customer are folded back
/// together on boot. Fixing the data automatically, rather than handing the operator a merge tool.
/// </summary>
public class CustomerConvergenceTests
{
    private static HaulScoutDbContext NewDb(string store, Guid org)
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return db;
    }

    /// <summary>
    /// CreatedAt is stamped by SaveChanges, so it is set afterwards where a test needs to control which
    /// record is older.
    /// </summary>
    private static Customer Add(
        HaulScoutDbContext db,
        Guid org,
        string? email,
        string? name,
        DateTimeOffset created,
        string? externalSubject = null)
    {
        Customer customer = new()
        {
            OrganizationId = org,
            Email = email,
            DisplayName = name,
            ExternalSubject = externalSubject,
        };
        db.Customers.Add(customer);
        db.SaveChanges();
        customer.CreatedAt = created;
        db.SaveChanges();
        return customer;
    }

    [Fact]
    public async Task Two_records_of_one_buyer_become_one_with_their_history_intact()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Two_records_of_one_buyer_become_one_with_their_history_intact), org);

        Customer original = Add(db, org, "jane@example.com", "Jane", DateTimeOffset.UtcNow.AddDays(-30));
        // Case differs, as marketplace exports routinely do.
        Customer duplicate = Add(db, org, "JANE@example.com", null, DateTimeOffset.UtcNow.AddDays(-2));
        duplicate.StoreCreditBalance = 15m;
        duplicate.Phone = "(941) 555-0123";
        duplicate.AcceptsMarketing = true;
        db.SaveChanges();

        db.Orders.Add(new Order { OrganizationId = org, CustomerId = duplicate.Id, OrderNumber = "ORD-1" });
        db.CustomerChannelIdentities.Add(new CustomerChannelIdentity
        {
            OrganizationId = org,
            CustomerId = duplicate.Id,
            Channel = "eBay",
            ExternalUserId = "janedoe22",
        });
        db.SaveChanges();

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Customer survivor = Assert.Single(await db.Customers.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(original.Id, survivor.Id);          // no sign-in link anywhere, so the oldest survives
        Assert.Equal(15m, survivor.StoreCreditBalance);  // credit follows the person
        Assert.Equal("(941) 555-0123", survivor.Phone);  // details the survivor lacked are kept
        Assert.True(survivor.AcceptsMarketing);

        // Everything the duplicate owned now points at the survivor.
        Assert.Equal(original.Id, (await db.Orders.IgnoreQueryFilters().SingleAsync()).CustomerId);
        Assert.Equal(original.Id, (await db.CustomerChannelIdentities.IgnoreQueryFilters().SingleAsync()).CustomerId);
    }

    [Fact]
    public async Task The_record_holding_the_sign_in_link_survives_even_when_it_is_newer()
    {
        // The dangerous case: an import created an older record, and the customer's real account —
        // the one they authenticate against — is newer. Folding into the older row would delete the
        // sign-in link and lock them out of the storefront.
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(The_record_holding_the_sign_in_link_survives_even_when_it_is_newer), org);

        Add(db, org, "jane@example.com", "Imported Jane", DateTimeOffset.UtcNow.AddDays(-30));
        Customer signsIn = Add(db, org, "jane@example.com", "Jane", DateTimeOffset.UtcNow, externalSubject: "supabase:abc");

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Customer survivor = Assert.Single(await db.Customers.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(signsIn.Id, survivor.Id);
        Assert.Equal("supabase:abc", survivor.ExternalSubject);
    }

    [Fact]
    public async Task Two_different_sign_in_links_are_left_alone_rather_than_destroying_one()
    {
        // One address, two real accounts. Folding would take somebody's login away, so this is a
        // question for a person, not something to guess.
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Two_different_sign_in_links_are_left_alone_rather_than_destroying_one), org);
        Add(db, org, "shared@example.com", "First", DateTimeOffset.UtcNow.AddDays(-5), externalSubject: "supabase:one");
        Add(db, org, "shared@example.com", "Second", DateTimeOffset.UtcNow, externalSubject: "supabase:two");

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Assert.Equal(2, await db.Customers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Addresses_that_differ_only_by_case_are_still_one_person()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Addresses_that_differ_only_by_case_are_still_one_person), org);
        Add(db, org, "Mixed@Example.com", "One", DateTimeOffset.UtcNow.AddDays(-5));
        Add(db, org, "mixed@example.com", "Two", DateTimeOffset.UtcNow);

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Customer survivor = Assert.Single(await db.Customers.IgnoreQueryFilters().ToListAsync());
        // Stored lowercased so the pair cannot re-form.
        Assert.Equal("mixed@example.com", survivor.Email);
    }

    [Fact]
    public async Task Running_again_changes_nothing()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Running_again_changes_nothing), org);
        Add(db, org, "same@example.com", "One", DateTimeOffset.UtcNow.AddDays(-5));
        Add(db, org, "same@example.com", "Two", DateTimeOffset.UtcNow);
        db.SaveChanges();

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);
        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Assert.Single(await db.Customers.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Different_people_are_left_alone()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Different_people_are_left_alone), org);
        Add(db, org, "one@example.com", "Same Name", DateTimeOffset.UtcNow.AddDays(-5));
        Add(db, org, "two@example.com", "Same Name", DateTimeOffset.UtcNow);
        // Customers with no email cannot be matched at all, and must never be guessed at.
        Add(db, org, null, "Anonymous Buyer", DateTimeOffset.UtcNow);
        Add(db, org, null, "Another Anonymous", DateTimeOffset.UtcNow);
        db.SaveChanges();

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Assert.Equal(4, await db.Customers.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task One_organizations_buyers_are_never_folded_into_anothers()
    {
        Guid org = Guid.NewGuid();
        Guid otherOrg = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(One_organizations_buyers_are_never_folded_into_anothers), org);
        db.Organizations.Add(new Organization { Id = otherOrg, Name = "Someone Else" });
        Add(db, org, "shared@example.com", "Ours", DateTimeOffset.UtcNow.AddDays(-5));
        Add(db, otherOrg, "shared@example.com", "Theirs", DateTimeOffset.UtcNow);
        db.SaveChanges();

        await CustomerConvergenceBootstrap.ConvergeDuplicatesAsync(db, NullLogger.Instance);

        Assert.Equal(2, await db.Customers.IgnoreQueryFilters().CountAsync());
    }
}
