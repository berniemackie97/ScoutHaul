using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain.Entities;
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

        InventoryReferenceDataService service = new(db, new StubCurrentUser(organizationId));
        InventoryReferenceDataDto data = await service.GetAsync(CancellationToken.None);

        Assert.Contains(data.Categories, option => option.Value == "Collectibles" && option.IsLegacy == false);
        Assert.DoesNotContain(data.Categories, option => option.Value == "Collectablez");
        Assert.Contains(data.Brands, option => option.Value == "Nintendo");
        Assert.Contains(data.Models, option => option.Value == "Switch OLED" && option.ParentValue == "Nintendo");
        Assert.Contains(data.StorageLocations, option => option.Value == "Warehouse A / Bin 12");
        Assert.Contains(data.StorageLocations, option => option.Value == "Other");
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

        InventoryReferenceDataService service = new(db, new StubCurrentUser(organizationId));
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
