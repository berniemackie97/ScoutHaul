using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>A marketplace's text is as long as the marketplace likes; landing it must never fail on a column's width.</summary>
[Collection(PostgresCollection.Name)]
public sealed class IngestColumnLimitTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static PlatformOrderIngestionService Orders(HaulScoutDbContext db, ICurrentUserContext currentUser)
    {
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        EfPlatformOrderStore store = new(db, new OrderAssembler(db, currentUser));
        PlatformOrderWorkflowService workflow = new(store, currentUser, suggestions);
        return new PlatformOrderIngestionService(store, currentUser, suggestions, workflow, new PlatformOrderAutoMatchService(suggestions, workflow, store, currentUser));
    }

    private static IngestPlatformOrderInput Order(string id, IngestPlatformOrderLineInput line, string? tracking = null, string? buyerNote = null, string? city = null) =>
        new(ExternalOrderId: id, OrderedAt: DateTimeOffset.UtcNow.AddDays(-1), ItemsSubtotal: line.UnitPrice, ShippingCharged: 0m, TaxCollected: 0m,
            FeeTotal: 1m, Lines: [line], Platform: "Depop", TrackingNumber: tracking, BuyerNote: buyerNote, ShipToCity: city);

    [Fact]
    public async Task A_description_long_enough_to_overflow_a_title_lands_cut_to_fit()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        string description = string.Join(' ', Enumerable.Repeat("Vintage 90s denim jacket #vintage #denim #y2k", 40));

        (IngestPlatformOrdersResultDto? result, string? error) = await Orders(db, new StubCurrentUser(org, user)).IngestAsync("Depop",
            [Order("1000001", new IngestPlatformOrderLineInput(description, 40m, ExternalItemId: "denim-jacket"), buyerNote: new string('n', 3000), city: new string('c', 300))],
            null, CancellationToken.None);

        Assert.Null(error);
        Assert.Equal(1, result!.Created);
        await using HaulScoutDbContext check = postgres.NewContext(org);
        PlatformOrder order = await check.PlatformOrders.Include(item => item.Lines).SingleAsync(item => item.ExternalOrderId == "1000001");
        string title = Assert.Single(order.Lines).Title;
        Assert.Equal(PlatformOrderLine.Limits.Title, title.Length);
        Assert.EndsWith("…", title);
        Assert.Equal(PlatformOrder.Limits.BuyerNote, order.BuyerNote!.Length);
        Assert.Equal(PlatformOrder.Limits.ShipToCity, order.ShipToCity!.Length);
    }

    [Fact]
    public async Task An_identifier_too_long_for_its_column_is_refused_by_name_not_cut()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;

        (IngestPlatformOrdersResultDto? result, string? error) = await Orders(db, new StubCurrentUser(org, user)).IngestAsync("Depop",
            [Order("1000002", new IngestPlatformOrderLineInput("Jacket", 40m), tracking: new string('9', 200))],
            null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal("Order 1000002 has a tracking number longer than 120 characters.", error);
        await using HaulScoutDbContext check = postgres.NewContext(org);
        Assert.False(await check.PlatformOrders.AnyAsync());
    }

    [Fact]
    public async Task A_listing_with_overlong_text_lands_and_an_overlong_link_is_dropped()
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        IngestPlatformListingInput input = new("denim-jacket", new string('t', 900), 40m, PlatformListingStatus.Active,
            Description: new string('d', 9000), Brand: new string('b', 400), Url: "https://www.depop.com/products/" + new string('x', 600));

        AppResult<IngestPlatformListingsResultDto> result = await new PlatformListingImportService(db, new StubCurrentUser(org, user))
            .IngestAsync(new IngestPlatformListingsRequest("Depop", [input]), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        await using HaulScoutDbContext check = postgres.NewContext(org);
        PlatformListing listing = await check.PlatformListings.SingleAsync();
        Assert.Equal(
            (PlatformListing.Limits.Title, PlatformListing.Limits.Description, PlatformListing.Limits.Brand, (string?)null),
            (listing.Title.Length, listing.Description!.Length, listing.Brand!.Length, listing.Url));
    }
}
