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
using HaulScout.Infrastructure.Sales;

namespace HaulScout.Tests;

/// <summary>
/// A marketplace cancelling an order that was already recorded as a sale. Undoing a sale moves stock and money, so
/// nothing happens until a person says what should happen — but the question has to survive until they answer it.
/// </summary>
public sealed class CancelledAfterSaleTests
{
    private sealed class StubCurrentUser(Guid organizationId) : ICurrentUserContext
    {
        public Guid OrganizationId => organizationId;
        public Guid? ActiveOrganizationId => organizationId;
        public Guid UserId { get; } = Guid.NewGuid();
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

    private static Harness NewHarness(string name, DateTimeOffset? cancelledAt)
    {
        Guid org = Guid.NewGuid();
        TenantContext tenant = new();
        tenant.SetOrganization(org);
        HaulScoutDbContext db = new(new DbContextOptionsBuilder<HaulScoutDbContext>().UseInMemoryDatabase(name).Options, tenant);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading Company" });

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
        db.SaveChanges();

        StubCurrentUser currentUser = new(org);
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
        await using Harness harness = NewHarness(nameof(Reversing_puts_the_item_back_in_stock_and_takes_the_sale_off_the_books), DateTimeOffset.UtcNow);

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
        await using Harness harness = NewHarness(nameof(Keeping_a_cancelled_sale_takes_a_reason_and_leaves_the_sale_alone), DateTimeOffset.UtcNow);

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
        await using Harness harness = NewHarness(nameof(An_order_the_marketplace_has_not_cancelled_cannot_be_settled), cancelledAt: null);

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
        await using Harness harness = NewHarness(nameof(Answering_twice_does_not_cancel_the_sale_twice), DateTimeOffset.UtcNow);
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
        await using Harness harness = NewHarness(nameof(A_cancellation_of_a_recorded_sale_is_written_down_rather_than_acted_on), cancelledAt: null);

        PlatformOrderCancellations applied = await harness.Ingestion.ApplyCancellationsAsync(
            "eBay", [harness.Order.ExternalOrderId], CancellationToken.None);

        Assert.Equal(0, applied.SetAside);
        Assert.Equal([harness.Order.ExternalOrderId], applied.StillRecorded);

        PlatformOrder stored = await harness.Db.PlatformOrders.SingleAsync();
        Assert.True(stored.AwaitingCancellationDecision);
        Assert.Equal(PlatformOrderStatus.Reconciled, stored.Status);
        Assert.Equal(SaleStatus.Recorded, (await harness.Db.Sales.SingleAsync()).Status);
    }
}
