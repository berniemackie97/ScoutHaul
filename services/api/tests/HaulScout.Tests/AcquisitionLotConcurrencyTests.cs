using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Controllers;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Tests;

/// <summary>A lot's items are its cost basis; two people changing one lot at once must never lose an item.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AcquisitionLotConcurrencyTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private static UpsertAcquisitionLotRequest LotRequest(IEnumerable<Guid> itemIds, Guid? expectedVersion = null) =>
        new("Estate pickup", DateTimeOffset.UtcNow.AddDays(-1), 100m, 0m, 0m, 0m, 0m, AcquisitionLotAllocationMethod.Equal,
            null, null, null, null, null, null,
            itemIds.Select(id => new AcquisitionLotItemInputDto(id, null, null, null)).ToArray(),
            expectedVersion);

    private async Task<(Guid Org, Guid User, Guid LotId, Guid[] Loose)> SeedAsync(int looseItems)
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();
        await using HaulScoutDbContext _ = db;
        InventoryItem first = new() { OrganizationId = org, Title = "Lamp 0", Status = InventoryStatus.Purchased };
        InventoryItem[] loose = Enumerable.Range(1, looseItems)
            .Select(index => new InventoryItem { OrganizationId = org, Title = $"Lamp {index}", Status = InventoryStatus.Purchased })
            .ToArray();
        db.InventoryItems.Add(first);
        db.InventoryItems.AddRange(loose);
        await db.SaveChangesAsync();

        AcquisitionLotDto lot = Assert.IsType<AcquisitionLotDto>(Assert.IsType<CreatedAtActionResult>(
            (await new AcquisitionLotsController(db, new StubCurrentUser(org, user)).Create(LotRequest([first.Id]), CancellationToken.None)).Result).Value);
        return (org, user, lot.Id, loose.Select(item => item.Id).ToArray());
    }

    private async Task<AcquisitionLot> ReadLotAsync(Guid org, Guid lotId)
    {
        await using HaulScoutDbContext db = postgres.NewContext(org);
        return await db.AcquisitionLots.Include(lot => lot.Items).AsNoTracking().SingleAsync(lot => lot.Id == lotId);
    }

    [Fact]
    public async Task Several_people_attaching_items_at_once_all_land()
    {
        (Guid org, Guid user, Guid lotId, Guid[] loose) = await SeedAsync(looseItems: 4);

        ActionResult<AcquisitionLotDto>[] results = await Task.WhenAll(loose.Select(async itemId =>
        {
            await using HaulScoutDbContext db = postgres.NewContext(org);
            return await new AcquisitionLotsController(db, new StubCurrentUser(org, user))
                .AddItems(lotId, new AddLotItemsRequest(null, [itemId]), CancellationToken.None);
        }));

        Assert.All(results, result => Assert.Null(result.Result));
        AcquisitionLot lot = await ReadLotAsync(org, lotId);
        Assert.Equal(5, lot.Items.Count);
        Assert.Equal(100m, lot.Items.Sum(item => item.AllocatedCost));
    }

    [Fact]
    public async Task An_edit_made_from_an_older_copy_is_refused_and_loses_nothing()
    {
        (Guid org, Guid user, Guid lotId, Guid[] loose) = await SeedAsync(looseItems: 1);
        AcquisitionLot opened = await ReadLotAsync(org, lotId);

        await using (HaulScoutDbContext attaching = postgres.NewContext(org))
        {
            await new AcquisitionLotsController(attaching, new StubCurrentUser(org, user))
                .AddItems(lotId, new AddLotItemsRequest(null, [loose[0]]), CancellationToken.None);
        }

        await using HaulScoutDbContext editing = postgres.NewContext(org);
        ActionResult<AcquisitionLotDto> stale = await new AcquisitionLotsController(editing, new StubCurrentUser(org, user))
            .Update(lotId, LotRequest(opened.Items.Select(item => item.InventoryItemId), opened.Version), CancellationToken.None);

        ObjectResult refused = Assert.IsType<ObjectResult>(stale.Result);
        Assert.Equal(409, refused.StatusCode);
        Assert.Contains("changed since you opened it", Assert.IsType<ProblemDetails>(refused.Value).Detail);
        Assert.Equal(2, (await ReadLotAsync(org, lotId)).Items.Count);
    }

    [Fact]
    public async Task One_item_raced_into_two_lots_ends_up_in_one_and_the_other_is_told()
    {
        (Guid org, Guid user, Guid firstLot, Guid[] loose) = await SeedAsync(looseItems: 1);
        Guid secondLot;
        await using (HaulScoutDbContext db = postgres.NewContext(org))
        {
            InventoryItem other = new() { OrganizationId = org, Title = "Chair", Status = InventoryStatus.Purchased };
            db.InventoryItems.Add(other);
            await db.SaveChangesAsync();
            secondLot = Assert.IsType<AcquisitionLotDto>(Assert.IsType<CreatedAtActionResult>(
                (await new AcquisitionLotsController(db, new StubCurrentUser(org, user)).Create(LotRequest([other.Id]), CancellationToken.None)).Result).Value).Id;
        }

        ActionResult<AcquisitionLotDto>[] results = await Task.WhenAll(new[] { firstLot, secondLot }.Select(async lotId =>
        {
            await using HaulScoutDbContext db = postgres.NewContext(org);
            return await new AcquisitionLotsController(db, new StubCurrentUser(org, user))
                .AddItems(lotId, new AddLotItemsRequest(null, [loose[0]]), CancellationToken.None);
        }));

        Assert.Single(results, result => result.Result is null);
        Assert.Equal(409, Assert.IsType<ObjectResult>(Assert.Single(results, result => result.Result is not null).Result).StatusCode);
        await using HaulScoutDbContext check = postgres.NewContext(org);
        Assert.Equal(1, await check.AcquisitionLotItems.CountAsync(item => item.InventoryItemId == loose[0]));
    }
}
