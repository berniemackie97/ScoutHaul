using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Common;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.PlatformOrders;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Sales;

namespace HaulScout.Tests;

/// <summary>
/// A marketplace cancelling an order that was already recorded as a sale. Undoing a sale moves stock and money, so
/// nothing happens until a person says what should happen — but the question has to survive until they answer it.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CancelledAfterSaleTests(PostgresFixture postgres)
{
    private sealed class StubCurrentUser(Guid organizationId, Guid userId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId => userId;
        public Task ResolveAsync() => Task.CompletedTask;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required HaulScoutDbContext Db { get; init; }
        public required Guid Org { get; init; }
        public required PlatformOrderCancellationService Cancellations { get; init; }
        public required PlatformOrderIngestionService Ingestion { get; init; }
        public required InventoryItem Item { get; init; }
        public required Sale Sale { get; init; }
        public required PlatformOrder Order { get; init; }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    // A real Postgres: the decision is claimed with a conditional update, which only a real database can perform.
    private async Task<Harness> NewHarnessAsync(DateTimeOffset? cancelledAt)
    {
        (HaulScoutDbContext db, Guid org, Guid user) = await postgres.NewOrganizationAsync();

        Guid group = Guid.NewGuid();
        InventoryItem item = new() { OrganizationId = org, Title = "Denim Jacket", Status = InventoryStatus.Sold, SoldAt = DateTimeOffset.UtcNow.AddDays(-2) };
        Sale sale = new()
        {
            OrganizationId = org,
            InventoryItemId = item.Id,
            InventoryItem = item,
            Platform = "eBay",
            SoldPrice = 80m,
            SoldAt = DateTimeOffset.UtcNow.AddDays(-2),
            Status = SaleStatus.Recorded,
            SaleGroupId = group,
        };
        PlatformOrder order = new()
        {
            OrganizationId = org,
            Platform = "eBay",
            ExternalOrderId = "26-15152-72360",
            OrderedAt = DateTimeOffset.UtcNow.AddDays(-2),
            Status = PlatformOrderStatus.Reconciled,
            ReconciledSaleGroupId = group,
            CancelledOnPlatformAt = cancelledAt,
        };
        db.InventoryItems.Add(item);
        db.Sales.Add(sale);
        db.PlatformOrders.Add(order);
        await db.SaveChangesAsync();

        StubCurrentUser currentUser = new(org, user);
        OrderAssembler assembler = new(db, currentUser);
        SaleTransitionWorkflowService transitions = new(db, currentUser, assembler, new SaleFinancialStateService(db, currentUser));
        return new Harness
        {
            Db = db,
            Org = org,
            Item = item,
            Sale = sale,
            Order = order,
            Cancellations = new PlatformOrderCancellationService(
                new EfPlatformOrderStore(db, assembler),
                currentUser,
                new SaleTransitionCanceller(transitions),
                new EfUnitOfWork(db),
                TimeProvider.System),
            Ingestion = NewIngestion(db, currentUser),
        };
    }

    private static PlatformOrderIngestionService NewIngestion(HaulScoutDbContext db, ICurrentUserContext currentUser)
    {
        PlatformOrderSuggestionService suggestions = new(new EfPlatformOrderSuggestionStore(db, currentUser));
        EfPlatformOrderStore store = new(db, new OrderAssembler(db, currentUser));
        PlatformOrderWorkflowService workflow = new(store, currentUser, suggestions);
        return new PlatformOrderIngestionService(
            store,
            currentUser,
            suggestions,
            workflow,
            new PlatformOrderAutoMatchService(suggestions, workflow, store, currentUser));
    }

    [Fact]
    public async Task Reversing_puts_the_item_back_in_stock_and_takes_the_sale_off_the_books()
    {
        await using Harness harness = await NewHarnessAsync(DateTimeOffset.UtcNow);

        AppResult<PlatformOrderDto> result = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Reversed),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        Sale sale = await harness.Db.Sales.SingleAsync();
        Assert.Equal(SaleStatus.Cancelled, sale.Status);

        InventoryItem item = await harness.Db.InventoryItems.SingleAsync();
        Assert.NotEqual(InventoryStatus.Sold, item.Status);
        Assert.Null(item.SoldAt);

        PlatformOrder order = await harness.Db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderCancellationOutcome.Reversed, order.CancellationOutcome);
        Assert.NotNull(order.CancellationDecidedAt);
        Assert.False(order.AwaitingCancellationDecision);
    }

    [Fact]
    public async Task Keeping_a_cancelled_sale_takes_a_reason_and_leaves_the_sale_alone()
    {
        await using Harness harness = await NewHarnessAsync(DateTimeOffset.UtcNow);

        AppResult<PlatformOrderDto> refused = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Kept),
            CancellationToken.None);
        Assert.Equal(AppResultStatus.Validation, refused.Status);

        AppResult<PlatformOrderDto> kept = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Kept, "Buyer paid by bank transfer instead."),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, kept.Status);
        Assert.Equal(SaleStatus.Recorded, (await harness.Db.Sales.SingleAsync()).Status);
        PlatformOrder order = await harness.Db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderCancellationOutcome.Kept, order.CancellationOutcome);
        Assert.Equal("Buyer paid by bank transfer instead.", order.CancellationNote);
    }

    [Fact]
    public async Task An_order_the_marketplace_has_not_cancelled_cannot_be_settled()
    {
        await using Harness harness = await NewHarnessAsync(cancelledAt: null);

        AppResult<PlatformOrderDto> result = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Reversed),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
        Assert.Equal(SaleStatus.Recorded, (await harness.Db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Answering_twice_does_not_cancel_the_sale_twice()
    {
        await using Harness harness = await NewHarnessAsync(DateTimeOffset.UtcNow);
        await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Kept, "Buyer paid another way."),
            CancellationToken.None);

        AppResult<PlatformOrderDto> again = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Reversed),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, again.Status);
        // The first answer stands: a second one must not quietly undo a sale that was deliberately kept.
        PlatformOrder order = await harness.Db.PlatformOrders.SingleAsync();
        Assert.Equal(PlatformOrderCancellationOutcome.Kept, order.CancellationOutcome);
        Assert.Equal(SaleStatus.Recorded, (await harness.Db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_cancellation_of_a_recorded_sale_is_written_down_rather_than_acted_on()
    {
        // The scan must neither undo the sale nor forget the question: it records it and waits for a person.
        await using Harness harness = await NewHarnessAsync(cancelledAt: null);

        PlatformOrderCancellations applied = await harness.Ingestion.ApplyCancellationsAsync(
            "eBay", [harness.Order.ExternalOrderId], CancellationToken.None);

        Assert.Equal(0, applied.SetAside);
        Assert.Equal([harness.Order.ExternalOrderId], applied.StillRecorded);

        PlatformOrder stored = await harness.Db.PlatformOrders.SingleAsync();
        Assert.True(stored.AwaitingCancellationDecision);
        Assert.Equal(PlatformOrderStatus.Reconciled, stored.Status);
        Assert.Equal(SaleStatus.Recorded, (await harness.Db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_decision_made_elsewhere_first_wins_and_this_one_is_told_so()
    {
        await using Harness harness = await NewHarnessAsync(DateTimeOffset.UtcNow);

        // Someone else settles it between this request reading the order and deciding on it.
        await using (HaulScoutDbContext other = postgres.NewContext(harness.Org))
        {
            PlatformOrder elsewhere = await other.PlatformOrders.SingleAsync(order => order.Id == harness.Order.Id);
            elsewhere.CancellationOutcome = PlatformOrderCancellationOutcome.Kept;
            elsewhere.CancellationDecidedAt = DateTimeOffset.UtcNow;
            elsewhere.CancellationNote = "Buyer paid another way.";
            await other.SaveChangesAsync();
        }

        AppResult<PlatformOrderDto> result = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(PlatformOrderCancellationOutcome.Reversed),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
        // The sale the other decision kept is still standing.
        await using HaulScoutDbContext fresh = postgres.NewContext(harness.Org);
        Assert.Equal(SaleStatus.Recorded, (await fresh.Sales.SingleAsync(sale => sale.Id == harness.Sale.Id)).Status);
        Assert.Equal(PlatformOrderCancellationOutcome.Kept,
            (await fresh.PlatformOrders.SingleAsync(order => order.Id == harness.Order.Id)).CancellationOutcome);
    }

    [Fact]
    public async Task A_reason_too_long_to_store_is_refused_before_anything_is_undone()
    {
        await using Harness harness = await NewHarnessAsync(DateTimeOffset.UtcNow);

        AppResult<PlatformOrderDto> result = await harness.Cancellations.DecideAsync(
            harness.Order.Id,
            new ResolvePlatformOrderCancellationRequest(
                PlatformOrderCancellationOutcome.Reversed,
                new string('x', PlatformOrder.CancellationNoteLimit + 1)),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
        // Refused up front: a note the column cannot hold must not fail the save after the sale is already gone.
        await using HaulScoutDbContext fresh = postgres.NewContext(harness.Org);
        Assert.Equal(SaleStatus.Recorded, (await fresh.Sales.SingleAsync(sale => sale.Id == harness.Sale.Id)).Status);
        Assert.True((await fresh.PlatformOrders.SingleAsync(order => order.Id == harness.Order.Id)).AwaitingCancellationDecision);
    }
}
