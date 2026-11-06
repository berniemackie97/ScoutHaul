using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static HaulScout.Api.Helpers.StringHelpers;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Orders;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

public sealed class OrdersWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter,
    ISaleCanceller sales,
    IUnitOfWork unitOfWork)
{
    /// <summary>Matches the inventory browse cap, so one page cannot be asked to carry the whole book.</summary>
    private const int MaxPageSize = 200;

    private static readonly SaleStatus[] FinancialSaleStatuses =
        [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.PartiallyRefunded, SaleStatus.Cancelled];

    /// <summary>
    /// A page of orders, filtered the way the operator asked, plus the queue summary over the whole book.
    ///
    /// Mirrors <c>/api/inventory/browse</c>: the page is the page, and the figures beside it span every
    /// match rather than the rows on screen. The admin portal used to fetch the newest 200 orders,
    /// bucket them in the browser and label the result "Captured revenue · Paid orders" — a number
    /// that stopped being true on the two-hundred-and-first order and never said so.
    ///
    /// The stage is derived rather than stored, so it cannot be a WHERE or a GROUP BY without
    /// restating <see cref="OrderWorkflow"/> in SQL and letting the two drift. Instead only the
    /// columns that rule reads are projected — no lines, no customer, no addresses — the stages are
    /// computed from them, and the resulting ids drive the page.
    /// </summary>
    public async Task<OrderListDto> BrowseAsync(
        string? stage,
        string? search,
        string? channel,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        IReadOnlyDictionary<Guid, OrderWorkflowStage> stages = await StagesAsync(cancellationToken);

        // The summary spans every order, never the filtered set. Narrowing the list is a question
        // about what to look at; it is not a claim that the rest of the book stopped existing, and a
        // "Captured revenue" that falls while you type a search is worse than no figure at all.
        OrderQueueSummaryDto summary = await SummarizeAsync(stages, cancellationToken);

        List<Guid> ordered = await FilteredOrders(status: null, search, channel)
            .OrderByDescending(order => order.CreatedAt)
            .Select(order => order.Id)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(stage) && Enum.TryParse(stage, ignoreCase: true, out OrderWorkflowStage wanted))
        {
            ordered = ordered.Where(id => stages.GetValueOrDefault(id) == wanted).ToList();
        }

        int total = ordered.Count;
        int pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        Guid[] pageIds = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        List<Order> orders = await db.Orders
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .Where(order => pageIds.Contains(order.Id))
            .ToListAsync(cancellationToken);

        HashSet<Guid> openReturns = await OpenReturnOrderIdsAsync(cancellationToken);

        // The page keeps the order the ids were sorted in; the id-based fetch above does not preserve it.
        List<OrderSummaryDto> items = pageIds
            .Select(id => orders.SingleOrDefault(order => order.Id == id))
            .Where(order => order is not null)
            .Select(order => order!.ToSummaryDto(openReturns.Contains(order!.Id)))
            .ToList();

        return new OrderListDto(items, total, page, pageSize, pageCount, summary);
    }

    /// <summary>
    /// The queue counts and captured revenue across the whole book, without a page of orders.
    ///
    /// The same figures <see cref="BrowseAsync"/> returns beside its list, for callers — the home
    /// console — that need the counts and would otherwise fetch a page only to throw it away.
    /// </summary>
    public async Task<OrderQueueSummaryDto> QueueSummaryAsync(CancellationToken cancellationToken) =>
        await SummarizeAsync(await StagesAsync(cancellationToken), cancellationToken);

    private async Task<OrderQueueSummaryDto> SummarizeAsync(
        IReadOnlyDictionary<Guid, OrderWorkflowStage> stages,
        CancellationToken cancellationToken) =>
        Summarize([.. stages.Keys], stages, await PaidTotalsAsync(cancellationToken));

    private static OrderQueueSummaryDto Summarize(
        IReadOnlyList<Guid> orderIds,
        IReadOnlyDictionary<Guid, OrderWorkflowStage> stages,
        IReadOnlyDictionary<Guid, decimal> paidTotals)
    {
        Dictionary<OrderWorkflowStage, int> counts = [];
        foreach (Guid id in orderIds)
        {
            OrderWorkflowStage stage = stages.GetValueOrDefault(id);
            counts[stage] = counts.GetValueOrDefault(stage) + 1;
        }

        // Every stage is reported, including the empty ones: a queue that reads zero is information,
        // and a queue that is simply absent makes the caller guess which it is.
        List<OrderQueueCountDto> stageCounts = Enum.GetValues<OrderWorkflowStage>()
            .Select(stage => new OrderQueueCountDto(stage.ToString(), counts.GetValueOrDefault(stage)))
            .ToList();

        int openWork = counts.GetValueOrDefault(OrderWorkflowStage.ToPack)
            + counts.GetValueOrDefault(OrderWorkflowStage.ToShip)
            + counts.GetValueOrDefault(OrderWorkflowStage.ReadyForPickup);

        return new OrderQueueSummaryDto(
            stageCounts,
            orderIds.Count,
            openWork,
            counts.GetValueOrDefault(OrderWorkflowStage.NeedsAttention),
            orderIds.Sum(id => paidTotals.GetValueOrDefault(id)));
    }

    /// <summary>The orders a caller asked for, before paging and before anything is hydrated.</summary>
    private IQueryable<Order> FilteredOrders(string? status, string? search, string? channel)
    {
        IQueryable<Order> query = db.Orders.Where(order => order.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse(status, ignoreCase: true, out OrderStatus parsed))
        {
            query = query.Where(order => order.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            string wanted = channel.Trim();
            query = query.Where(order => order.Channel == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(order =>
                EF.Functions.ILike(order.OrderNumber, $"%{term}%")
                || (order.Email != null && EF.Functions.ILike(order.Email, $"%{term}%"))
                || (order.BuyerReference != null && EF.Functions.ILike(order.BuyerReference, $"%{term}%"))
                || (order.ExternalOrderId != null && EF.Functions.ILike(order.ExternalOrderId, $"%{term}%"))
                || (order.TrackingNumber != null && EF.Functions.ILike(order.TrackingNumber, $"%{term}%"))
                || order.Lines.Any(line => EF.Functions.ILike(line.TitleSnapshot, $"%{term}%")));
        }

        return query;
    }

    /// <summary>Every order's workflow stage, computed from the narrowest projection the rule needs.</summary>
    private async Task<IReadOnlyDictionary<Guid, OrderWorkflowStage>> StagesAsync(CancellationToken cancellationToken)
    {
        HashSet<Guid> openReturns = await OpenReturnOrderIdsAsync(cancellationToken);

        var rows = await db.Orders
            .Where(order => order.OrganizationId == currentUser.OrganizationId)
            .Select(order => new
            {
                order.Id,
                order.Status,
                order.PaymentStatus,
                order.IsLocalPickup,
                order.PackedAt,
                order.ShippingCarrier,
                order.TrackingNumber,
                order.ShipToLine1,
                order.ShippedAt,
                order.DeliveredAt,
                order.Channel,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.Id,
            row => OrderWorkflow.StageFor(new OrderWorkflowFacts(
                row.Status,
                row.PaymentStatus,
                row.IsLocalPickup,
                IsPacked: row.PackedAt is not null,
                HasCarrier: !string.IsNullOrWhiteSpace(row.ShippingCarrier),
                HasTrackingNumber: !string.IsNullOrWhiteSpace(row.TrackingNumber),
                HasShippingAddress: !string.IsNullOrWhiteSpace(row.ShipToLine1),
                IsShipped: row.ShippedAt is not null,
                IsDelivered: row.DeliveredAt is not null,
                HasOpenReturn: openReturns.Contains(row.Id),
                ChannelHandlesShipping: Order.ChannelHandlesShipping(row.Channel))));
    }

    /// <summary>What each paid order brought in. Unpaid orders are absent rather than zero.</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> PaidTotalsAsync(CancellationToken cancellationToken) =>
        await db.Orders
            .Where(order => order.OrganizationId == currentUser.OrganizationId && order.PaymentStatus == PaymentStatus.Paid)
            .Select(order => new { order.Id, order.GrandTotal })
            .ToDictionaryAsync(row => row.Id, row => row.GrandTotal, cancellationToken);

    private async Task<HashSet<Guid>> OpenReturnOrderIdsAsync(CancellationToken cancellationToken) =>
        [.. await db.Returns
            .Where(entry => entry.OrganizationId == currentUser.OrganizationId
                && entry.OrderId != null
                && (entry.Status == ReturnStatus.Requested
                    || entry.Status == ReturnStatus.LabelIssued
                    || entry.Status == ReturnStatus.Received))
            .Select(entry => entry.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)];

    public async Task<IReadOnlyList<OrderSummaryDto>> ListAsync(
        string? status,
        string? search,
        string? channel,
        CancellationToken cancellationToken)
    {
        List<Order> orders = await FilteredOrders(status, search, channel)
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .OrderByDescending(order => order.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        // One query for the whole page rather than one per row: a return in flight changes the stage,
        // and the operator needs to see that from the list without opening every order.
        Guid[] orderIds = orders.Select(order => order.Id).ToArray();
        HashSet<Guid> ordersWithOpenReturns = [.. await db.Returns
            .Where(entry => entry.OrganizationId == currentUser.OrganizationId
                && entry.OrderId != null
                && orderIds.Contains(entry.OrderId.Value)
                && (entry.Status == ReturnStatus.Requested
                    || entry.Status == ReturnStatus.LabelIssued
                    || entry.Status == ReturnStatus.Received))
            .Select(entry => entry.OrderId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)];

        return orders.Select(order => order.ToSummaryDto(ordersWithOpenReturns.Contains(order.Id))).ToList();
    }

    public async Task<AppResult<OrderDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Order? order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return AppResult<OrderDetailDto>.NotFound();
        }

        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        OrderDetailDto detail = order.ToDetailDto(salesByInventoryItem, await HasOpenReturnAsync(order.Id, cancellationToken));
        return AppResult<OrderDetailDto>.Ok(detail with { TrackingDeliveredAt = await TrackingDeliveredAtAsync(order, cancellationToken) });
    }

    private async Task<DateTimeOffset?> TrackingDeliveredAtAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.DeliveredAt is not null || order.ExternalOrderId is null)
        {
            return null;
        }

        return await db.PlatformOrders
            .Where(platformOrder => platformOrder.OrganizationId == currentUser.OrganizationId
                && platformOrder.Platform == order.Channel
                && platformOrder.ExternalOrderId == order.ExternalOrderId)
            .Select(platformOrder => platformOrder.DeliveredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Payment and cancellation only. Shipping and delivery are recorded through tracking, refunds on the lines,
    /// so each fact has one door and the workflow gate guards it.
    /// </summary>
    public async Task<AppResult<OrderDetailDto>> UpdateStatusAsync(
        Guid id,
        UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        Order? order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return AppResult<OrderDetailDto>.NotFound();
        }

        OrderWorkflowAction? action = request.Status switch
        {
            OrderStatus.Paid => OrderWorkflowAction.RecordPayment,
            OrderStatus.Cancelled => OrderWorkflowAction.Cancel,
            _ => null,
        };
        if (action is null)
        {
            return AppResult<OrderDetailDto>.Validation(
                "Only payment and cancellation are set here. Record shipping and delivery with tracking, and refunds on the order's lines.");
        }

        OrderTransition transition = OrderWorkflow.Evaluate(
            order.ToWorkflowFacts(await HasOpenReturnAsync(order.Id, cancellationToken)),
            action.Value);
        if (!transition.Allowed)
        {
            return AppResult<OrderDetailDto>.Validation(string.Join(" ", transition.Blockers));
        }

        OrderStatus previous = order.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        try
        {
            await unitOfWork.InTransactionAsync(async token =>
            {
                if (action == OrderWorkflowAction.Cancel)
                {
                    await CancelSalesAsync(order, now, token);
                    order.CancelledAt ??= now;
                }
                else
                {
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.PaidAt ??= now;
                }

                order.Status = request.Status;
                db.AuditLogs.Add(new AuditLog
                {
                    OrganizationId = currentUser.OrganizationId,
                    ActorUserId = currentUser.UserId,
                    EntityName = nameof(Order),
                    EntityId = order.Id,
                    Action = "OrderStatusChanged",
                    DetailsJson = JsonSerializer.Serialize(new { order.OrderNumber, From = previous.ToString(), To = request.Status.ToString() }),
                });

                await db.SaveChangesAsync(token);
                return true;
            }, cancellationToken);
        }
        catch (SaleNotCancellable refused)
        {
            return AppResult<OrderDetailDto>.Validation(refused.Message);
        }

        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(order.ToDetailDto(salesByInventoryItem, await HasOpenReturnAsync(order.Id, cancellationToken)));
    }

    /// <summary>
    /// A cancelled order sold nothing: each live sale is reversed the way its own cancel does it, so the item is
    /// restocked and the money leaves the books. Without this the order read cancelled while reports still counted it.
    /// </summary>
    private async Task CancelSalesAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<Guid> live = await db.Sales
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId
                && sale.OrderId == order.Id
                && sale.Status != SaleStatus.Cancelled)
            .Select(sale => sale.Id)
            .ToListAsync(cancellationToken);
        foreach (Guid saleId in live)
        {
            AppResult<bool> result = await sales.CancelAsync(saleId, now, $"Order {order.OrderNumber} cancelled", cancellationToken);
            if (result.Status != AppResultStatus.Ok)
            {
                throw new SaleNotCancellable(result.Message ?? "A sale on this order could not be cancelled.");
            }
        }
    }

    private sealed class SaleNotCancellable(string message) : Exception(message);

    public async Task<AppResult<OrderDetailDto>> UpdateTrackingAsync(
        Guid id,
        UpdateOrderTrackingRequest request,
        CancellationToken cancellationToken)
    {
        Order? order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return AppResult<OrderDetailDto>.NotFound();
        }

        string? carrier = TrimToNull(request.ShippingCarrier);
        string? trackingNumber = TrimToNull(request.TrackingNumber);
        string? trackingUrl = TrimToNull(request.TrackingUrl);

        // A bare "mark packed/shipped/delivered" (bulk, mobile) is a step, not an edit: it must not wipe tracking.
        bool stepOnly = (request.MarkPacked || request.MarkShipped || request.MarkDelivered)
            && carrier is null && trackingNumber is null && trackingUrl is null;
        if (stepOnly)
        {
            carrier = order.ShippingCarrier;
            trackingNumber = order.TrackingNumber;
            trackingUrl = order.TrackingUrl;
        }

        if ((carrier is null) != (trackingNumber is null))
        {
            return AppResult<OrderDetailDto>.Validation("Carrier and tracking number must be supplied together.");
        }

        if (trackingUrl is not null
            && (!Uri.TryCreate(trackingUrl, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            return AppResult<OrderDetailDto>.Validation("Tracking URL must be a valid absolute http(s) URL.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Packing is the gate before shipping, and the domain decides whether it may happen — an
        // unpaid order, or a shipping order with nowhere to send it, is not packable. Asking
        // OrderWorkflow here rather than re-deriving the rule keeps every surface honest.
        DateTimeOffset? packedAt = order.PackedAt;
        if (request.MarkPacked && packedAt is null)
        {
            OrderTransition packable = OrderWorkflow.Evaluate(order.ToWorkflowFacts(), OrderWorkflowAction.MarkPacked);
            if (!packable.Allowed)
            {
                return AppResult<OrderDetailDto>.Validation(string.Join(" ", packable.Blockers));
            }

            packedAt = now;
        }

        // A step records only the stage it names, and only once: a date already on the order is history, and an
        // earlier stage nobody recorded stays unrecorded rather than being stamped with today. Editing the dates
        // themselves is the shipment form's job, which sends no step.
        bool isStep = request.MarkPacked || request.MarkShipped || request.MarkDelivered;
        DateTimeOffset? shippedAt = isStep
            ? order.ShippedAt ?? (request.MarkShipped ? request.ShippedAt ?? now : request.ShippedAt)
            : request.ShippedAt ?? order.ShippedAt;
        DateTimeOffset? deliveredAt = isStep
            ? order.DeliveredAt ?? (request.MarkDelivered ? request.DeliveredAt ?? now : request.DeliveredAt)
            : request.DeliveredAt ?? order.DeliveredAt;

        if (shippedAt > now.AddMinutes(5) || deliveredAt > now.AddMinutes(5))
        {
            return AppResult<OrderDetailDto>.Validation("Shipment timestamps cannot be in the future.");
        }

        if (deliveredAt.HasValue && shippedAt.HasValue && deliveredAt.Value < shippedAt.Value)
        {
            return AppResult<OrderDetailDto>.Validation("Delivered time cannot be earlier than shipped time.");
        }

        bool deliveredChanged = deliveredAt != order.DeliveredAt;

        // Our own shipments are tracked from handover, so delivery cannot be recorded before shipping. A marketplace
        // ships its own labels and may finish an order before anyone opens it here.
        if (deliveredChanged && deliveredAt.HasValue && !shippedAt.HasValue && !order.IsLocalPickup && !Order.ChannelHandlesShipping(order.Channel))
        {
            return AppResult<OrderDetailDto>.Validation("Mark this order shipped before marking it delivered.");
        }

        bool hasShippedEvidence = shippedAt.HasValue;
        bool hasDeliveredEvidence = deliveredAt.HasValue;
        bool shouldAdvanceToDelivered = request.MarkDelivered || hasDeliveredEvidence;
        bool shouldAdvanceToShipped = request.MarkShipped || hasShippedEvidence;

        if (!order.IsLocalPickup
            && !Order.ChannelHandlesShipping(order.Channel)
            && (shouldAdvanceToShipped || shouldAdvanceToDelivered)
            && (carrier is null || trackingNumber is null))
        {
            return AppResult<OrderDetailDto>.Validation("Enter the carrier and tracking number before marking this order shipped or delivered.");
        }

        if (order.Status is OrderStatus.Cancelled && (request.MarkPacked || shouldAdvanceToShipped || shouldAdvanceToDelivered))
        {
            return AppResult<OrderDetailDto>.Validation("This order is cancelled.");
        }

        var previous = new
        {
            order.Status,
            order.FulfillmentStatus,
            order.ShippingCarrier,
            order.TrackingNumber,
            order.TrackingUrl,
            order.PackedAt,
            order.ShippedAt,
            order.DeliveredAt
        };

        order.PackedAt = packedAt;
        order.ShippingCarrier = carrier;
        order.TrackingNumber = trackingNumber;
        order.TrackingUrl = trackingUrl;
        order.ShippedAt = shippedAt;
        order.DeliveredAt = deliveredAt;

        bool canAdvanceFulfillment = order.Status is not OrderStatus.Cancelled and not OrderStatus.Refunded;
        if (canAdvanceFulfillment && shouldAdvanceToDelivered)
        {
            order.Status = OrderStatus.Delivered;
            order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
            if (order.PaymentStatus == PaymentStatus.Pending)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
            }
        }
        else if (canAdvanceFulfillment && shouldAdvanceToShipped)
        {
            order.Status = OrderStatus.Shipped;
            order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
            if (order.PaymentStatus == PaymentStatus.Pending)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
            }
        }

        if (canAdvanceFulfillment && shouldAdvanceToDelivered)
        {
            await PropagateFulfillmentToSalesAsync(order.Id, SaleStatus.Delivered, cancellationToken);
        }
        else if (canAdvanceFulfillment && shouldAdvanceToShipped)
        {
            await PropagateFulfillmentToSalesAsync(order.Id, SaleStatus.Shipped, cancellationToken);
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(Order),
            EntityId = order.Id,
            Action = "OrderTrackingUpdated",
            DetailsJson = JsonSerializer.Serialize(new
            {
                order.OrderNumber,
                Before = previous,
                After = new
                {
                    order.Status,
                    order.FulfillmentStatus,
                    order.ShippingCarrier,
                    order.TrackingNumber,
                    order.TrackingUrl,
                    order.PackedAt,
                    order.ShippedAt,
                    order.DeliveredAt
                }
            }),
        });

        await db.SaveChangesAsync(cancellationToken);
        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(order.ToDetailDto(salesByInventoryItem, await HasOpenReturnAsync(order.Id, cancellationToken)));
    }

    public async Task<AppResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (order is null)
        {
            return AppResult.NotFound();
        }

        List<Sale> linkedSales = await db.Sales
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId && sale.OrderId == order.Id)
            .ToListAsync(cancellationToken);
        foreach (Sale sale in linkedSales)
        {
            sale.OrderId = null;
            sale.Order = null;
        }

        if (order.Lines.Count > 0)
        {
            db.OrderLines.RemoveRange(order.Lines);
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(Order),
            EntityId = order.Id,
            Action = "OrderDeleted",
            DetailsJson = JsonSerializer.Serialize(new
            {
                order.OrderNumber,
                order.Channel,
                order.ExternalOrderId,
                order.Status,
                order.GrandTotal,
                LineCount = order.Lines.Count,
                DetachedSaleIds = linkedSales.Select(sale => sale.Id).ToArray(),
            }),
        });

        db.Orders.Remove(order);
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Ok();
    }

    public async Task<AppResult<OrderDetailDto>> MergeAsync(
        MergeOrdersRequest request,
        CancellationToken cancellationToken)
    {
        List<Guid> ids = request.OrderIds?.Distinct().ToList() ?? [];
        if (ids.Count < 2)
        {
            return AppResult<OrderDetailDto>.Validation("Select at least two orders to merge.");
        }

        List<Order> orders = await db.Orders
            .Include(order => order.Lines)
            .Where(order => ids.Contains(order.Id) && order.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);
        if (orders.Count != ids.Count)
        {
            return AppResult<OrderDetailDto>.NotFound("One or more orders were not found.");
        }

        if (orders.Select(order => order.Channel).Distinct().Count() > 1)
        {
            return AppResult<OrderDetailDto>.Validation("Orders on different channels cannot be merged.");
        }

        Order surviving = request.SurvivingOrderId is Guid pinned
            ? orders.SingleOrDefault(order => order.Id == pinned) ?? orders.OrderBy(order => order.CreatedAt).First()
            : orders.OrderBy(order => order.CreatedAt).First();
        List<Order> merged = orders.Where(order => order.Id != surviving.Id).ToList();
        List<Guid> mergedIds = merged.Select(order => order.Id).ToList();

        List<Sale> movedSales = await db.Sales
            .Where(sale => sale.OrderId != null && mergedIds.Contains(sale.OrderId.Value)
                && sale.OrganizationId == currentUser.OrganizationId)
            .ToListAsync(cancellationToken);

        foreach (Order order in merged.Where(order => order.Lines.Count > 0))
        {
            db.OrderLines.RemoveRange(order.Lines);
        }

        await orderWriter.SyncSalesOrderAsync(surviving, movedSales, cancellationToken);
        db.Orders.RemoveRange(merged);

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(Order),
            EntityId = surviving.Id,
            Action = "OrdersMerged",
            DetailsJson = JsonSerializer.Serialize(new
            {
                SurvivingOrderNumber = surviving.OrderNumber,
                MergedOrderIds = mergedIds,
                MovedSaleCount = movedSales.Count,
            }),
        });

        await db.SaveChangesAsync(cancellationToken);

        Order refreshed = await db.Orders
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .SingleAsync(order => order.Id == surviving.Id && order.OrganizationId == currentUser.OrganizationId, cancellationToken);
        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(surviving.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(refreshed.ToDetailDto(salesByInventoryItem, await HasOpenReturnAsync(surviving.Id, cancellationToken)));
    }

    private Task<Order?> LoadOrderAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders
            .Include(item => item.Customer)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

    private async Task PropagateFulfillmentToSalesAsync(Guid orderId, SaleStatus target, CancellationToken cancellationToken)
    {
        List<Sale> sales = await db.Sales
            .Where(sale => sale.OrderId == orderId
                && sale.OrganizationId == currentUser.OrganizationId
                && !FinancialSaleStatuses.Contains(sale.Status))
            .ToListAsync(cancellationToken);
        foreach (Sale sale in sales)
        {
            sale.Status = target;
        }
    }

    /// <summary>
    /// Whether any return against this order is still in flight. A return outranks whatever stage the
    /// order otherwise sits in — an item coming back needs a person, not a queue position.
    /// </summary>
    private Task<bool> HasOpenReturnAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Returns
            .Where(entry => entry.OrganizationId == currentUser.OrganizationId && entry.OrderId == orderId)
            .AnyAsync(
                entry => entry.Status == ReturnStatus.Requested
                    || entry.Status == ReturnStatus.LabelIssued
                    || entry.Status == ReturnStatus.Received,
                cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, Sale>> SalesByInventoryItemAsync(Guid orderId, CancellationToken cancellationToken)
    {
        List<Sale> sales = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Include(sale => sale.Order)
            .Include(sale => sale.Refunds)
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId && sale.OrderId == orderId)
            .OrderBy(sale => sale.SoldAt)
            .ToListAsync(cancellationToken);

        return sales
            .GroupBy(sale => sale.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.Last());
    }
}
