using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Inventory;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public sealed class InventoryReferenceDataServiceTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
        public Task ResolveAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Reference_data_returns_curated_options_without_promoting_legacy_values()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Reference_data_returns_curated_options_without_promoting_legacy_values));
        db.InventoryItems.Add(new InventoryItem
        {
            Title = "Existing item",
            Category = "Collectablez",
            Brand = "Nintendo",
            Model = "Switch OLED",
            StorageLocation = "Warehouse A / Bin 12",
        });
        await db.SaveChangesAsync();

        StubCurrentUser currentUser = new(organizationId);
        InventoryReferenceDataService service = new(new EfInventoryReferenceDataStore(db, currentUser));
        InventoryReferenceDataDto data = await service.GetAsync(CancellationToken.None);

        Assert.Contains(data.Categories, option => option.Value == "Collectibles" && option.IsLegacy == false);
        Assert.DoesNotContain(data.Categories, option => option.Value == "Collectablez");
        Assert.Contains(data.Brands, option => option.Value == "Nintendo");
        Assert.Contains(data.Models, option => option.Value == "Switch OLED" && option.ParentValue == "Nintendo");
        Assert.Contains(data.StorageLocations, option => option.Value == "Warehouse A / Bin 12");
        // "Other" is not a place; the only locations offered are ones in use.
        Assert.DoesNotContain(data.StorageLocations, option => option.Value == "Other");
        Assert.DoesNotContain(data.Brands, option => option.Value == "Other");
        Assert.DoesNotContain(data.Colors, option => option.Value == "Other");
        Assert.Contains(data.Profiles, profile => profile.Key == "apparel" && profile.Categories.Contains("Clothing"));
        Assert.Contains(data.Countries, option => option.Value == "US");
        Assert.DoesNotContain(data.Categories, option => option.IsLegacy);
        Assert.DoesNotContain(data.Brands, option => option.IsLegacy);
        Assert.DoesNotContain(data.Models, option => option.IsLegacy);
    }

    [Fact]
    public async Task Resolution_canonicalizes_case_and_preserves_custom_brand_and_model_values()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Resolution_canonicalizes_case_and_preserves_custom_brand_and_model_values));
        db.ProductIdentities.Add(new ProductIdentity { Title = "Console", Brand = "Nintendo", Model = "Switch OLED" });
        await db.SaveChangesAsync();

        StubCurrentUser currentUser = new(organizationId);
        InventoryReferenceDataService service = new(new EfInventoryReferenceDataStore(db, currentUser));
        (InventoryReferenceDataService.ControlledValues? Values, string? Error) accepted = await service.ResolveAsync(Request(category: "video games", brand: "nintendo", model: "switch oled"), CancellationToken.None);
        (InventoryReferenceDataService.ControlledValues? Values, string? Error) corrected = await service.ResolveAsync(Request(category: "Collectibels", brand: "Nintenndo", model: "Switch-ish"), CancellationToken.None);

        Assert.Null(accepted.Error);
        Assert.Equal("Video Games", accepted.Values!.Category);
        Assert.Equal("Nintendo", accepted.Values.Brand);
        Assert.Equal("Switch OLED", accepted.Values.Model);
        Assert.Null(corrected.Error);
        Assert.Equal("Other", corrected.Values!.Category);
        Assert.Equal("Nintenndo", corrected.Values.Brand);
        Assert.Equal("Switch-ish", corrected.Values.Model);
    }

    [Fact]
    public async Task Placeholders_and_unknown_values_are_left_blank_rather_than_saved_as_other()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Placeholders_and_unknown_values_are_left_blank_rather_than_saved_as_other));
        InventoryReferenceDataService service = new(new EfInventoryReferenceDataStore(db, new StubCurrentUser(organizationId)));

        (InventoryReferenceDataService.ControlledValues? values, _) = await service.ResolveAsync(
            Request(category: "Clothing", brand: "Unbranded", model: "Other") with { Colors = ["Chartreuse-ish"], Condition = "Other", SizeValue = "M" },
            CancellationToken.None);

        Assert.Null(values!.Brand);
        Assert.Null(values.Model);
        Assert.Empty(values.Colors!);
        Assert.Null(values.Condition);
        // A size means nothing without the system it is measured in.
        Assert.Null(values.SizeValue);
    }

    [Fact]
    public async Task Colors_are_read_as_listed_terms_two_at_most_and_left_alone_when_not_sent()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Colors_are_read_as_listed_terms_two_at_most_and_left_alone_when_not_sent));
        InventoryReferenceDataService service = new(new EfInventoryReferenceDataStore(db, new StubCurrentUser(organizationId)));
        UpsertInventoryItemRequest request = Request(category: "Clothing", brand: "Levi's", model: "501");

        (InventoryReferenceDataService.ControlledValues? two, _) = await service.ResolveAsync(request with { Colors = ["grey", "Off-White", "gray"] }, CancellationToken.None);
        (_, string? tooMany) = await service.ResolveAsync(request with { Colors = ["Red", "Blue", "Green"] }, CancellationToken.None);
        (InventoryReferenceDataService.ControlledValues? untouched, _) = await service.ResolveAsync(request with { Colors = null }, CancellationToken.None);

        Assert.Equal(["Gray", "Cream"], two!.Colors);
        Assert.Contains("at most 2 colors", tooMany);
        Assert.Null(untouched!.Colors);
    }

    private static HaulScoutDbContext NewContext(Guid organizationId, string databaseName)
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        TenantContext tenant = new();
        tenant.SetOrganization(organizationId);
        return new HaulScoutDbContext(options, tenant);
    }

    private static UpsertInventoryItemRequest Request(string category, string brand, string model) => new(
        ProductIdentityId: null,
        SourcingTripId: null,
        SourcingTripStopId: null,
        SourceLocationId: null,
        Title: "Test item",
        Description: null,
        Category: category,
        Brand: brand,
        Model: model,
        Colors: ["Black"],
        Condition: "Good",
        SizeSystem: null,
        SizeValue: null,
        AttributesJson: null,
        MeasurementsJson: null,
        StorageLocation: null,
        Accessories: null,
        MissingParts: null,
        TestingNotes: null,
        CleaningNotes: null,
        ListingNotes: null,
        SellerAskingPrice: null,
        PurchasePrice: null,
        EstimatedSalePrice: null,
        Barcode: null,
        Notes: null,
        CountryOfOrigin: "US");
}
