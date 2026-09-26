using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Organizations;
using HaulScout.Application.Search;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Search;

namespace HaulScout.Tests;

/// <summary>
/// Finding a thing without knowing which list it lives in.
///
/// The portal had per-page search on two of twenty-eight screens, so looking up an order meant
/// already knowing it was an order. This is the one question — "where is this?" — across everything
/// the caller is allowed to see, and the permission half matters as much as the matching half: a
/// search that quietly returns records someone cannot open is a leak, not a convenience.
/// </summary>
public sealed class GlobalSearchTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    /// <summary>Grants exactly the permissions it is given, so each section can be tested on its own.</summary>
    private sealed class StubAuthorizer(params string[] granted) : IOrganizationAuthorizer
    {
        public Task RequirePermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            granted.Contains(permission) ? Task.CompletedTask : throw new UnauthorizedAccessException(permission);

        public Task<bool> HasPermission(Guid organizationId, string permission, CancellationToken cancellationToken) =>
            Task.FromResult(granted.Contains(permission));

        public Task<IReadOnlyList<string>> ListPermissions(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(granted);

        public Task RequireOwner(Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static readonly string[] Everything =
    [
        HaulScoutPermissions.ManageInventory,
        HaulScoutPermissions.ManageOrders,
        HaulScoutPermissions.ManageFinance,
    ];

    private static (SearchService Service, HaulScoutDbContext Db, Guid Org) NewController(params string[] granted)
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();

        return (new SearchService(new EfSearchStore(db), new StubCurrentUser(org), new StubAuthorizer(granted)), db, org);
    }

    private static void Seed(HaulScoutDbContext db, Guid org)
    {
        db.InventoryItems.Add(new InventoryItem
        {
            OrganizationId = org,
            Title = "Vintage brass banker's lamp",
            Brand = "Emeralite",
            Barcode = "0123456789",
            StorageLocation = "Shelf B4",
            Status = InventoryStatus.ReadyToList,
        });
        db.Orders.Add(new Order
        {
            OrganizationId = org,
            OrderNumber = "SH-1042",
            Channel = "Storefront",
            Email = "buyer@example.com",
            ShipToName = "Dana Brooks",
            Status = OrderStatus.Paid,
        });
        db.Customers.Add(new Customer { OrganizationId = org, DisplayName = "Dana Brooks", Email = "dana@example.com" });
        db.AcquisitionLots.Add(new AcquisitionLot
        {
            OrganizationId = org,
            Name = "Maple St estate",
            SupplierName = "Northside Auctions",
            PurchaseSubtotal = 100m,
        });
        db.SaveChanges();
    }

    private static Task<SearchResultsDto> Search(SearchService service, string query) =>
        service.SearchAsync(query, CancellationToken.None);

    private static IReadOnlyList<SearchHitDto> Kind(SearchResultsDto results, string kind) =>
        results.Groups.SingleOrDefault(group => group.Kind == kind)?.Hits ?? [];

    [Fact]
    public async Task Finds_an_item_by_its_title()
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        SearchResultsDto results = await Search(controller, "banker");

        Assert.Equal("Vintage brass banker's lamp", Assert.Single(Kind(results, "inventory")).Label);
    }

    [Theory]
    [InlineData("emeralite")]
    [InlineData("0123456789")]
    [InlineData("shelf b4")]
    public async Task Finds_an_item_by_the_other_things_you_might_remember(string query)
    {
        // The barcode you just scanned and the shelf you wrote on the box are how an item gets looked
        // up in practice, at least as often as its title.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        Assert.Single(Kind(await Search(controller, query), "inventory"));
    }

    [Theory]
    [InlineData("sh-1042")]
    [InlineData("buyer@example.com")]
    [InlineData("dana")]
    public async Task Finds_an_order_by_number_email_or_who_it_is_going_to(string query)
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        Assert.NotEmpty(Kind(await Search(controller, query), "order"));
    }

    [Fact]
    public async Task Matching_ignores_case()
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        Assert.Single(Kind(await Search(controller, "BANKER"), "inventory"));
    }

    [Fact]
    public async Task One_query_reaches_every_kind_at_once()
    {
        // The whole point: you type what you remember, not what kind of record it is.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        SearchResultsDto results = await Search(controller, "dana");

        Assert.Contains(results.Groups, group => group.Kind == "order");
        Assert.Contains(results.Groups, group => group.Kind == "customer");
    }

    [Fact]
    public async Task Empty_groups_are_left_out_rather_than_shown_empty()
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        SearchResultsDto results = await Search(controller, "banker");

        Assert.Equal("inventory", Assert.Single(results.Groups).Kind);
        Assert.Equal(1, results.TotalHits);
    }

    [Fact]
    public async Task Says_when_a_group_has_more_behind_the_cut()
    {
        // Six results that are actually forty must not look like six results.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        for (int index = 0; index < 40; index++)
        {
            db.InventoryItems.Add(new InventoryItem
            {
                OrganizationId = org,
                Title = $"Brass lamp {index}",
                Status = InventoryStatus.ReadyToList,
            });
        }
        await db.SaveChangesAsync();

        SearchGroupDto group = Assert.Single((await Search(controller, "brass")).Groups);

        Assert.Equal(6, group.Hits.Count);
        Assert.True(group.Truncated);
    }

    [Fact]
    public async Task Does_not_claim_more_when_a_group_fits()
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        Assert.False(Assert.Single((await Search(controller, "banker")).Groups).Truncated);
    }

    [Fact]
    public async Task A_query_too_short_to_mean_anything_returns_nothing()
    {
        // Not an error — an empty palette is the normal state before anyone has typed.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        foreach (string query in new[] { "", " ", "b" })
        {
            SearchResultsDto results = await Search(controller, query);
            Assert.Empty(results.Groups);
            Assert.Equal(0, results.TotalHits);
        }
    }

    [Fact]
    public async Task Shows_only_the_kinds_the_caller_may_open()
    {
        // A search that returns records someone cannot open is a leak, not a convenience.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(HaulScoutPermissions.ManageInventory);
        Seed(db, org);

        SearchResultsDto results = await Search(controller, "dana");

        Assert.Empty(results.Groups);
    }

    [Fact]
    public async Task Inventory_only_access_still_finds_inventory()
    {
        // The other half of the same rule: missing one permission must not blank the whole feature.
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(HaulScoutPermissions.ManageInventory);
        Seed(db, org);

        Assert.Single(Kind(await Search(controller, "banker"), "inventory"));
    }

    [Fact]
    public async Task Purchases_need_the_finance_permission()
    {
        (SearchService inventoryOnly, HaulScoutDbContext db, Guid org) = NewController(HaulScoutPermissions.ManageInventory);
        Seed(db, org);
        Assert.Empty(Kind(await Search(inventoryOnly, "maple"), "purchase"));

        (SearchService finance, HaulScoutDbContext financeDb, Guid financeOrg) = NewController(HaulScoutPermissions.ManageFinance);
        Seed(financeDb, financeOrg);
        Assert.Single(Kind(await Search(finance, "maple"), "purchase"));
    }

    [Fact]
    public async Task Another_organization_s_records_are_not_findable()
    {
        (SearchService controller, HaulScoutDbContext db, Guid org) = NewController(Everything);
        Seed(db, org);

        // Same database, different tenant. The org filter is the thing being checked here.
        db.InventoryItems.Add(new InventoryItem
        {
            OrganizationId = Guid.NewGuid(),
            Title = "Somebody else's brass lamp",
            Status = InventoryStatus.ReadyToList,
        });
        await db.SaveChangesAsync();

        Assert.Single(Kind(await Search(controller, "brass"), "inventory"));
    }
}
