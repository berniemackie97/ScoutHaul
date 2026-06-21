using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
    public async Task Reference_data_combines_canonical_options_with_existing_organization_values()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Reference_data_combines_canonical_options_with_existing_organization_values));
        db.InventoryItems.Add(new InventoryItem
        {
            Title = "Existing item",
            Category = "Collectablez",
            Brand = "Nintendo",
            Model = "Switch OLED",
            StorageLocation = "Warehouse A / Bin 12",
        });
        await db.SaveChangesAsync();

        var service = new InventoryReferenceDataService(db, new StubCurrentUser(organizationId));
        InventoryReferenceDataDto data = await service.GetAsync(CancellationToken.None);

        Assert.Contains(data.Categories, option => option.Value == "Collectibles" && option.IsLegacy == false);
        Assert.Contains(data.Categories, option => option.Value == "Collectablez" && option.IsLegacy);
        Assert.Contains(data.Brands, option => option.Value == "Nintendo");
        Assert.Contains(data.Models, option => option.Value == "Switch OLED" && option.ParentValue == "Nintendo");
        Assert.Contains(data.StorageLocations, option => option.Value == "Warehouse A / Bin 12");
        Assert.Contains(data.Countries, option => option.Value == "US");
    }

    [Fact]
    public async Task Resolution_canonicalizes_case_and_rejects_unapproved_values()
    {
        Guid organizationId = Guid.NewGuid();
        await using HaulScoutDbContext db = NewContext(organizationId, nameof(Resolution_canonicalizes_case_and_rejects_unapproved_values));
        db.ProductIdentities.Add(new ProductIdentity { Title = "Console", Brand = "Nintendo", Model = "Switch OLED" });
        await db.SaveChangesAsync();

        var service = new InventoryReferenceDataService(db, new StubCurrentUser(organizationId));
        var accepted = await service.ResolveAsync(Request(category: "collectibles", brand: "nintendo", model: "switch oled"), CancellationToken.None);
        var rejected = await service.ResolveAsync(Request(category: "Collectibels", brand: "Nintendo", model: "Switch OLED"), CancellationToken.None);

        Assert.Null(accepted.Error);
        Assert.Equal("Collectibles", accepted.Values!.Category);
        Assert.Equal("Nintendo", accepted.Values.Brand);
        Assert.Equal("Switch OLED", accepted.Values.Model);
        Assert.Contains("Category must be selected", rejected.Error);
    }

    private static HaulScoutDbContext NewContext(Guid organizationId, string databaseName)
    {
        var options = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        var tenant = new TenantContext();
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
        Color: "Black",
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
