using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Security;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>
/// A listing carries the words a buyer reads.
///
/// It did not. A listing had a platform, a price and a URL, and nowhere for a title or a description,
/// so the listing generator on mobile produced both, showed them, and offered only "Regenerate" —
/// there was no field on the server to send them to. The copy was retyped by hand into the
/// marketplace or lost when the screen closed.
/// </summary>
public sealed class ListingContentTests
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static (ListingsController Controller, HaulScoutDbContext Db, Guid Item) NewController()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);

        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        InventoryItem item = new() { OrganizationId = org, Title = "Brass Lamp", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        db.SaveChanges();

        return (new ListingsController(db, new StubCurrentUser(org, Guid.NewGuid())), db, item.Id);
    }

    private static CreateListingRequest Create(Guid item, string? title = null, string? description = null) =>
        new(item, "eBay", null, 49.99m, null, title, description);

    private static UpdateListingRequest Update(string? title = null, string? description = null) =>
        new("eBay", null, 49.99m, ListingStatus.Draft, null, title, description);

    private static ListingDto Value(ActionResult<ListingDto> result) => result.Value
        ?? Assert.IsType<ListingDto>(Assert.IsAssignableFrom<ObjectResult>(result.Result).Value);

    [Fact]
    public async Task Keeps_the_copy_a_listing_was_created_with()
    {
        (ListingsController controller, HaulScoutDbContext db, Guid item) = NewController();

        ListingDto created = Value(await controller.Create(
            Create(item, "Vintage Brass Banker's Lamp", "Working. Original green glass shade."),
            CancellationToken.None));

        Assert.Equal("Vintage Brass Banker's Lamp", created.Title);
        Assert.Equal("Working. Original green glass shade.", created.Description);

        // And it is on the row, not only in the response the writer already had in hand.
        Listing stored = await db.Listings.SingleAsync();
        Assert.Equal("Vintage Brass Banker's Lamp", stored.Title);
        Assert.Equal("Working. Original green glass shade.", stored.Description);
    }

    [Fact]
    public async Task Reads_the_copy_back_out()
    {
        // The mapping is the half that silently drops a field: stored fine, never surfaced.
        (ListingsController controller, _, Guid item) = NewController();
        ListingDto created = Value(await controller.Create(Create(item, "Brass Lamp", "Shade intact."), CancellationToken.None));

        ListingDto fetched = Value(await controller.Get(created.Id, CancellationToken.None));
        Assert.Equal("Brass Lamp", fetched.Title);
        Assert.Equal("Shade intact.", fetched.Description);

        ActionResult<IReadOnlyList<ListingDto>> listed = await controller.List(null, CancellationToken.None);
        ListingDto fromList = Assert.Single(listed.Value!);
        Assert.Equal("Brass Lamp", fromList.Title);
        Assert.Equal("Shade intact.", fromList.Description);
    }

    [Fact]
    public async Task Lets_a_generated_draft_be_written_onto_an_existing_listing()
    {
        // The path that matters: a listing created bare, then filled in from the generator.
        (ListingsController controller, _, Guid item) = NewController();
        ListingDto bare = Value(await controller.Create(Create(item), CancellationToken.None));
        Assert.Null(bare.Title);

        ListingDto filled = Value(await controller.Update(
            bare.Id,
            Update("Vintage Brass Banker's Lamp", "Working. Original green glass shade."),
            CancellationToken.None));

        Assert.Equal("Vintage Brass Banker's Lamp", filled.Title);
        Assert.Equal("Working. Original green glass shade.", filled.Description);
    }

    [Fact]
    public async Task Treats_blank_copy_as_no_copy()
    {
        (ListingsController controller, _, Guid item) = NewController();

        ListingDto created = Value(await controller.Create(Create(item, "   ", "\n"), CancellationToken.None));

        Assert.Null(created.Title);
        Assert.Null(created.Description);
    }

    [Fact]
    public async Task Refuses_a_title_longer_than_the_column()
    {
        // Without this the write reaches the database and fails there, as a 500 with no reason.
        (ListingsController controller, _, Guid item) = NewController();

        ActionResult<ListingDto> result = await controller.Create(
            Create(item, new string('x', ListingLimits.TitleMaxLength + 1)),
            CancellationToken.None);

        ProblemAssert.Invalid(result);
    }

    [Fact]
    public async Task Refuses_a_description_longer_than_the_column()
    {
        (ListingsController controller, _, Guid item) = NewController();

        ActionResult<ListingDto> result = await controller.Create(
            Create(item, "Brass Lamp", new string('x', ListingLimits.DescriptionMaxLength + 1)),
            CancellationToken.None);

        ProblemAssert.Invalid(result);
    }

    [Fact]
    public async Task Refuses_over_length_copy_on_an_update_too()
    {
        (ListingsController controller, _, Guid item) = NewController();
        ListingDto created = Value(await controller.Create(Create(item, "Brass Lamp"), CancellationToken.None));

        ActionResult<ListingDto> result = await controller.Update(
            created.Id,
            Update(new string('x', ListingLimits.TitleMaxLength + 1)),
            CancellationToken.None);

        ProblemAssert.Invalid(result);
    }

    [Fact]
    public async Task Accepts_copy_exactly_at_the_limit()
    {
        (ListingsController controller, _, Guid item) = NewController();

        ListingDto created = Value(await controller.Create(
            Create(item, new string('x', ListingLimits.TitleMaxLength), new string('y', ListingLimits.DescriptionMaxLength)),
            CancellationToken.None));

        Assert.Equal(ListingLimits.TitleMaxLength, created.Title!.Length);
        Assert.Equal(ListingLimits.DescriptionMaxLength, created.Description!.Length);
    }
}
