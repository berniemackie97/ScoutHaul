using HaulScout.Application.Channels;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Application.Work;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Api.Services;

/// <summary>
/// One inbox for the day's work, in the order a sale moves: reconcile what the marketplaces report, list what is ready,
/// pack, ship, and follow up loose ends. Each task links to the one place it is done and carries its thread (sold →
/// paid → packed → shipped → delivered), so a worker sees where it stands and what comes next. Tasks come from the
/// records themselves — an order to pack, a listing to delist — so finishing the work removes the task; nothing has to
/// be ticked off twice. A task set aside names what it waits on, and a snooze comes back, marked late, on its date.
/// </summary>
public sealed class WorkInboxService(
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer access,
    OrdersWorkflowService orders,
    InventoryQueryService inventory,
    IPlatformOrderStore platformOrders,
    ChannelDelistService delisting,
    SoldListingService soldListings,
    SyncIssueService syncIssues,
    IWorkSnoozeStore snoozes,
    IListingMatchStore listings,
    TimeProvider clock)
{
    private const int PerLane = 50;

    public const string Reconcile = "reconcile";
    public const string List = "list";
    public const string Pack = "pack";
    public const string Ship = "ship";
    public const string FollowUp = "follow";

    private static readonly TimeSpan PackWithin = TimeSpan.FromDays(2);
    private static readonly TimeSpan ShipWithin = TimeSpan.FromDays(1);
    private static readonly TimeSpan ReconcileWithin = TimeSpan.FromDays(1);
    private static readonly TimeSpan DelistWithin = TimeSpan.FromHours(12);

    public async Task<WorkInboxDto> BuildAsync(CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        DateTimeOffset now = clock.GetUtcNow();
        bool canOrders = await access.HasPermission(organizationId, HaulScoutPermissions.ManageOrders, cancellationToken);
        bool canInventory = await access.HasPermission(organizationId, HaulScoutPermissions.ManageInventory, cancellationToken);
        bool canListings = await access.HasPermission(organizationId, HaulScoutPermissions.ManageListings, cancellationToken);
        bool canFinance = await access.HasPermission(organizationId, HaulScoutPermissions.ManageFinance, cancellationToken);

        List<WorkTaskDto> tasks = [];
        int inTransit = 0;

        if (canListings)
        {
            tasks.AddRange((await soldListings.ListAsync(cancellationToken)).Select(sold => new WorkTaskDto(
                $"sold:{sold.PlatformListingId}",
                Reconcile,
                "Sold, not recorded",
                $"Record sale — {sold.ItemTitle}",
                $"{sold.Platform} · {sold.ListedPrice:C}",
                $"{sold.Platform} says it sold. Until the sale is recorded, profit, tax and stock are wrong.",
                "Record sale",
                "/channels/sold",
                sold.MarkedSoldAt,
                sold.MarkedSoldAt + ReconcileWithin,
                Late(sold.MarkedSoldAt, ReconcileWithin, now),
                [])));

            int pendingMatches = await listings.CountPendingAsync(organizationId, cancellationToken);
            if (pendingMatches > 0)
            {
                tasks.Add(new WorkTaskDto(
                    "listings:match",
                    Reconcile,
                    "Unmatched listings",
                    $"Match {pendingMatches} imported {(pendingMatches == 1 ? "listing" : "listings")}",
                    "Ranked suggestions are ready for each.",
                    "A listing with no item cannot sell through your books.",
                    "Review matches",
                    "/imports/listings",
                    null,
                    null,
                    false,
                    [],
                    pendingMatches));
            }

            AppResult<IReadOnlyList<SyncIssueGroupDto>> issues = await syncIssues.ListOpenAsync(cancellationToken);
            tasks.AddRange((issues.Value ?? []).Select(group => new WorkTaskDto(
                $"sync:{group.Platform}:{group.Code}",
                FollowUp,
                "Sync issue",
                $"{group.Title} — {group.Platform}",
                $"{group.Count} {(group.Count == 1 ? "record" : "records")} · {group.Fix}",
                group.Severity == SyncIssueSeverity.Error ? "Some records did not come in." : "Records came in with something missing.",
                "Open issue",
                "/sync#sync-issues",
                group.LatestAt,
                null,
                group.Severity == SyncIssueSeverity.Error && now - group.LatestAt > ReconcileWithin,
                [],
                group.Count)));

            tasks.AddRange((await delisting.ListAsync(cancellationToken)).Select(delist => new WorkTaskDto(
                $"delist:{delist.ChannelListingId}",
                FollowUp,
                "Pending delist",
                $"Delist from {delist.Channel} — {delist.ItemTitle}",
                delist.SoldOnChannel is null ? delist.AccountName : $"Sold on {delist.SoldOnChannel} · still live on {delist.Channel}",
                "A sold item still live elsewhere can sell twice.",
                "Mark delisted",
                "/channels/delist",
                delist.SoldAt,
                delist.SoldAt + DelistWithin,
                Late(delist.SoldAt, DelistWithin, now),
                [])));
        }

        if (canFinance)
        {
            int imported = await platformOrders.CountAsync(organizationId, PlatformOrderStatus.Imported, cancellationToken);
            if (imported > 0)
            {
                tasks.Add(new WorkTaskDto(
                    "imports:orders",
                    Reconcile,
                    "Imported orders",
                    $"Turn {imported} imported {(imported == 1 ? "order" : "orders")} into sales",
                    null,
                    "The item still reads as available while it is in a buyer's hands.",
                    "Reconcile orders",
                    "/imports/orders",
                    null,
                    null,
                    false,
                    [],
                    imported));
            }
        }

        if (canInventory)
        {
            InventoryListDto ready = await inventory.BrowseAsync(
                InventoryStatus.ReadyToList, null, false, null, null, "updated", true, 1, PerLane, null, false, false, cancellationToken);
            tasks.AddRange(ready.Items.Select(item => new WorkTaskDto(
                $"list:{item.Id}",
                List,
                "Ready to list",
                $"List — {item.Title}",
                Text.TrimToNull(string.Join(" · ", new[] { item.Category, item.StorageLocation is null ? null : $"Bin {item.StorageLocation}" }.Where(part => part is not null))),
                "Ready items earn nothing until they are listed everywhere you sell.",
                "Open item",
                $"/inventory/{item.Id}?tab=channels",
                item.UpdatedAt,
                null,
                false,
                [])));
        }

        if (canOrders)
        {
            foreach (OrderSummaryDto order in await OrdersAsync(OrderWorkflowStage.ToPack, cancellationToken))
            {
                DateTimeOffset? since = order.PaidAt ?? order.CreatedAt;
                tasks.Add(OrderTask(order, Pack, "pack", "To pack", "Pack", "Start packing", $"/work/pack?order={order.Id}", since, PackWithin, now,
                    "Paid. Pack it so it can ship on time."));
            }

            foreach (OrderSummaryDto order in await OrdersAsync(OrderWorkflowStage.ReadyForPickup, cancellationToken))
            {
                tasks.Add(OrderTask(order, Pack, "pickup", "Ready for pickup", "Hand over", "Open order", $"/orders/{order.Id}", order.PaidAt ?? order.CreatedAt, TimeSpan.FromDays(3), now,
                    "Packed and waiting for the buyer to collect."));
            }

            foreach (OrderSummaryDto order in await OrdersAsync(OrderWorkflowStage.ToShip, cancellationToken))
            {
                tasks.Add(OrderTask(order, Ship, "ship", "To ship", "Ship", "Add tracking", $"/orders/{order.Id}", order.PaidAt ?? order.CreatedAt, ShipWithin + PackWithin, now,
                    "Packed. Hand it to the carrier and record the tracking number."));
            }

            foreach (OrderSummaryDto order in await OrdersAsync(OrderWorkflowStage.NeedsAttention, cancellationToken))
            {
                tasks.Add(OrderTask(order, FollowUp, "attention", "Needs attention", null, "Open order", $"/orders/{order.Id}", order.CreatedAt, TimeSpan.FromDays(1), now,
                    "A return, refund or problem is open on this order."));
            }

            foreach (OrderSummaryDto order in await OrdersAsync(OrderWorkflowStage.AwaitingPayment, cancellationToken))
            {
                if (now - order.CreatedAt > TimeSpan.FromDays(3))
                {
                    tasks.Add(OrderTask(order, FollowUp, "payment", "Awaiting payment", null, "Open order", $"/orders/{order.Id}", order.CreatedAt, TimeSpan.FromDays(3), now,
                        "Unpaid for three days. Follow up with the buyer or cancel."));
                }
            }

            inTransit = (await OrdersAsync(OrderWorkflowStage.InTransit, cancellationToken)).Count;
        }

        // A task set aside leaves its lane; when the work behind it moves on, its key stops matching and the snooze is moot.
        IReadOnlyList<WorkSnooze> setAside = await snoozes.ListActiveAsync(organizationId, now, cancellationToken);
        HashSet<string> asideKeys = setAside.Select(snooze => snooze.TaskKey).ToHashSet(StringComparer.Ordinal);
        HashSet<string> liveKeys = tasks.Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
        List<WorkTaskDto> open = [.. tasks.Where(task => !asideKeys.Contains(task.Key))];

        WorkLaneDto[] lanes =
        [
            Lane(Reconcile, "Reconcile", "Marketplace facts not yet in your books", open),
            Lane(List, "List", "Ready items not yet listed", open),
            Lane(Pack, "Pack", "Paid, not packed", open),
            Lane(Ship, "Ship", "Packed, not handed to a carrier", open),
            Lane(FollowUp, "Follow up", "Delists, problems, unpaid orders, sync issues", open),
        ];

        return new WorkInboxDto(
            lanes,
            [.. setAside.Where(snooze => liveKeys.Contains(snooze.TaskKey)).Select(snooze => new WorkWaitDto(snooze.TaskKey, snooze.Wait, snooze.Until, snooze.Note, snooze.Title))],
            inTransit,
            open.Sum(task => task.Count),
            open.Count(task => task.IsLate),
            now);
    }

    public async Task<AppResult> SetAsideAsync(SetAsideWorkTaskRequest request, CancellationToken cancellationToken)
    {
        string? key = Text.TrimToNull(request.TaskKey);
        if (key is null || key.Length > 200)
        {
            return AppResult.Validation("Name the task to set aside.");
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (request.Wait == WorkWait.Snoozed && (request.Until is null || request.Until <= now))
        {
            return AppResult.Validation("A snooze needs a date in the future, so the task comes back.");
        }

        Guid organizationId = currentUser.OrganizationId;
        WorkSnooze? snooze = await snoozes.FindAsync(organizationId, key, cancellationToken);
        if (snooze is null)
        {
            snooze = new WorkSnooze { OrganizationId = organizationId, TaskKey = key };
            snoozes.Add(snooze);
        }

        snooze.Wait = request.Wait;
        snooze.Until = request.Until;
        snooze.Note = Text.Fit(Text.TrimToNull(request.Note), 500);
        snooze.Title = Text.Fit(Text.TrimToNull(request.Title), 300);
        snooze.CreatedByUserId = currentUser.UserId;
        await snoozes.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult> BringBackAsync(string taskKey, CancellationToken cancellationToken)
    {
        WorkSnooze? snooze = await snoozes.FindAsync(currentUser.OrganizationId, taskKey, cancellationToken);
        if (snooze is not null)
        {
            snoozes.Remove(snooze);
            await snoozes.SaveChangesAsync(cancellationToken);
        }

        return AppResult.Ok();
    }

    private async Task<IReadOnlyList<OrderSummaryDto>> OrdersAsync(OrderWorkflowStage stage, CancellationToken cancellationToken) =>
        (await orders.BrowseAsync(stage.ToString(), null, null, 1, PerLane, cancellationToken)).Items;

    private static WorkTaskDto OrderTask(
        OrderSummaryDto order,
        string lane,
        string verb,
        string kind,
        string? stepNow,
        string actionLabel,
        string actionHref,
        DateTimeOffset? since,
        TimeSpan within,
        DateTimeOffset now,
        string why)
    {
        string title = order.LeadItemTitle ?? $"Order {order.OrderNumber}";
        string channel = order.Channel;
        return new WorkTaskDto(
            $"order:{order.Id}:{verb}",
            lane,
            kind,
            $"{stepNow ?? kind} — {title}{(order.ItemCount > 1 ? $" +{order.ItemCount - 1}" : string.Empty)}",
            Text.TrimToNull(string.Join(" · ", new[] { channel, order.ExternalOrderId ?? order.OrderNumber, order.CustomerName ?? order.BuyerReference }.Where(part => !string.IsNullOrWhiteSpace(part)))),
            why,
            actionLabel,
            actionHref,
            since,
            since + within,
            Late(since, within, now),
            Thread(order));
    }

    /// <summary>Where the sale stands: each step done with its date, the first undone one is now.</summary>
    private static WorkStepDto[] Thread(OrderSummaryDto order)
    {
        (string Label, DateTimeOffset? At)[] steps =
        [
            ($"Sold on {order.Channel}", order.CreatedAt),
            ("Paid", order.PaidAt),
            (order.IsLocalPickup ? "Packed for pickup" : "Packed", null),
            (order.IsLocalPickup ? "Collected" : "Shipped", order.ShippedAt),
            ("Delivered", order.DeliveredAt),
        ];

        bool packed = order.Stage is nameof(OrderWorkflowStage.ToShip) or nameof(OrderWorkflowStage.ReadyForPickup)
            or nameof(OrderWorkflowStage.InTransit) or nameof(OrderWorkflowStage.Delivered);
        int firstUndone = Array.FindIndex(steps, step => step.At is null && !(step.Label.StartsWith("Packed", StringComparison.Ordinal) && packed));
        return
        [
            .. steps.Select((step, index) => new WorkStepDto(
                step.Label,
                step.At,
                firstUndone < 0 || index < firstUndone ? WorkStepState.Done : index == firstUndone ? WorkStepState.Now : WorkStepState.Later)),
        ];
    }

    private static bool Late(DateTimeOffset? since, TimeSpan within, DateTimeOffset now) => since is not null && now - since > within;

    private static WorkLaneDto Lane(string key, string label, string hint, IReadOnlyList<WorkTaskDto> tasks) =>
        new(key, label, hint, [.. tasks.Where(task => task.Lane == key).OrderByDescending(task => task.IsLate).ThenBy(task => task.DueBy ?? DateTimeOffset.MaxValue).ThenBy(task => task.Since ?? DateTimeOffset.MaxValue)]);
}
