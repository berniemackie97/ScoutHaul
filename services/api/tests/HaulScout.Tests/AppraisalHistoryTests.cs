using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Appraisal;
using HaulScout.Infrastructure.Inventory;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

public sealed class AppraisalHistoryTests
{
    [Fact]
    public async Task An_appraisal_is_kept_on_the_item_and_reads_back_in_the_appraisal_s_terms()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(An_appraisal_is_kept_on_the_item_and_reads_back_in_the_appraisal_s_terms), org);
        InventoryItem item = new() { OrganizationId = org, Title = "Sony WH-1000XM5", Status = InventoryStatus.ReadyToList };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        EfAppraisalHistoryStore store = new(db);

        await store.RecordAsync(org, item.Id, null, Price(PriceBasis.Asking, 199.99m, 40, [Comp(CompKind.Asking), Comp(CompKind.Asking)]), CancellationToken.None);
        await store.RecordAsync(org, item.Id, null, Price(PriceBasis.Sales, 214.99m, 71, [Comp(CompKind.YourSale), Comp(CompKind.Sold), Comp(CompKind.Asking)]), CancellationToken.None);

        IReadOnlyList<AppraisalSnapshotDto> history = await store.ListForItemAsync(org, item.Id, 20, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Contains(history, snapshot => snapshot is { Basis: PriceBasis.Sales, List: 214.99m, Level: ConfidenceLevel.Fair, SaleComps: 2, AskingComps: 1 });
        Assert.Contains(history, snapshot => snapshot is { Basis: PriceBasis.Asking, SaleComps: 0, AskingComps: 2 });
        Assert.False(await store.ItemExistsAsync(Guid.NewGuid(), item.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Setting_the_expected_price_changes_only_the_price()
    {
        Guid org = Guid.NewGuid();
        await using HaulScoutDbContext db = NewDb(nameof(Setting_the_expected_price_changes_only_the_price), org);
        InventoryItem item = new() { OrganizationId = org, Title = "Brass lamp", Condition = "Good", Status = InventoryStatus.ReadyToList, EstimatedSalePrice = 40m };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        InventoryStatusWorkflowService service = new(new EfInventoryStore(db), new StubUser(org), TimeProvider.System);

        AppResult<InventoryItemDto> result = await service.SetExpectedPriceAsync(item.Id, new SetExpectedPriceRequest(54.994m), CancellationToken.None);
        AppResult<InventoryItemDto> refused = await service.SetExpectedPriceAsync(item.Id, new SetExpectedPriceRequest(-1m), CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        Assert.Equal(54.99m, (await db.InventoryItems.SingleAsync()).EstimatedSalePrice);
        Assert.Equal("Good", (await db.InventoryItems.SingleAsync()).Condition);
        Assert.Equal(AppResultStatus.Validation, refused.Status);
    }

    private static PriceResult Price(PriceBasis basis, decimal list, int confidence, IReadOnlyList<Comp> comps) => new(
        basis, list * 0.85m, list, list * 1.1m, confidence, CompPricing.LevelFor(confidence, basis), ["reason"],
        comps.Select(comp => new AssessedComp(comp, true, null)).ToArray());

    private static Comp Comp(CompKind kind) =>
        new(kind, "eBay", "Sony WH-1000XM5", 200m, null, null, null, DateTimeOffset.UtcNow, true);

    private static HaulScoutDbContext NewDb(string store, Guid org)
    {
        DbContextOptions<HaulScoutDbContext> options = new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(store).Options;
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });
        db.SaveChanges();
        return db;
    }

    private sealed class StubUser(Guid org) : ICurrentUserContext
    {
        public Guid OrganizationId => org;
        public Guid? ActiveOrganizationId => org;
        public Guid UserId => Guid.Empty;
        public Task ResolveAsync() => Task.CompletedTask;
    }
}
