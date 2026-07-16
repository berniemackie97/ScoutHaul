using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Mapping;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Services;

public sealed class OrdersWorkflowService(
    HaulScoutDbContext db,
    ICurrentUserContext currentUser,
    OrderAssembler orderWriter)
{
    private static readonly SaleStatus[] FinancialSaleStatuses =
        [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.PartiallyRefunded, SaleStatus.Cancelled];

    public async Task<IReadOnlyList<OrderSummaryDto>> ListAsync(
        string? status,
        string? search,
        string? channel,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = db.Orders
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == currentUser.OrganizationId);

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
                || (order.ExternalOrderId != null && EF.Functions.ILike(order.ExternalOrderId, $"%{term}%")));
        }

        List<Order> orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return orders.Select(order => order.ToSummaryDto()).ToList();
    }

    public async Task<AppResult<OrderDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        Order? order = await LoadOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return AppResult<OrderDetailDto>.NotFound();
        }

        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(order.ToDetailDto(salesByInventoryItem));
    }

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

        OrderStatus previous = order.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        order.Status = request.Status;
        switch (request.Status)
        {
            case OrderStatus.Paid:
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
                break;
            case OrderStatus.Fulfilling:
                order.FulfillmentStatus = FulfillmentStatus.Partial;
                break;
            case OrderStatus.Shipped:
                order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                order.ShippedAt ??= now;
                if (order.PaymentStatus == PaymentStatus.Pending)
                {
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.PaidAt ??= now;
                }
                break;
            case OrderStatus.Delivered:
                order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
                order.ShippedAt ??= now;
                order.DeliveredAt ??= now;
                break;
            case OrderStatus.Cancelled:
                order.CancelledAt ??= now;
                break;
            case OrderStatus.Refunded:
                order.PaymentStatus = PaymentStatus.Refunded;
                break;
            case OrderStatus.Pending:
            default:
                break;
        }

        SaleStatus? propagate = request.Status switch
        {
            OrderStatus.Fulfilling => SaleStatus.Packed,
            OrderStatus.Shipped => SaleStatus.Shipped,
            OrderStatus.Delivered => SaleStatus.Delivered,
            _ => null,
        };
        if (propagate is SaleStatus target)
        {
            await PropagateFulfillmentToSalesAsync(order.Id, target, cancellationToken);
        }

        db.AuditLogs.Add(new AuditLog
        {
            OrganizationId = currentUser.OrganizationId,
            ActorUserId = currentUser.UserId,
            EntityName = nameof(Order),
            EntityId = order.Id,
            Action = "OrderStatusChanged",
            DetailsJson = JsonSerializer.Serialize(new { order.OrderNumber, From = previous.ToString(), To = request.Status.ToString() }),
        });

        await db.SaveChangesAsync(cancellationToken);
        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(order.ToDetailDto(salesByInventoryItem));
    }

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
        DateTimeOffset? deliveredAt = request.DeliveredAt ?? (request.MarkDelivered ? now : order.DeliveredAt);
        DateTimeOffset? shippedAt = request.ShippedAt ?? (request.MarkShipped || request.MarkDelivered ? now : order.ShippedAt);
        if (deliveredAt.HasValue && !shippedAt.HasValue)
        {
            shippedAt = deliveredAt;
        }

        if (shippedAt > now.AddMinutes(5) || deliveredAt > now.AddMinutes(5))
        {
            return AppResult<OrderDetailDto>.Validation("Shipment timestamps cannot be in the future.");
        }

        if (deliveredAt.HasValue && shippedAt.HasValue && deliveredAt.Value < shippedAt.Value)
        {
            return AppResult<OrderDetailDto>.Validation("Delivered time cannot be earlier than shipped time.");
        }

        bool hasShippedEvidence = shippedAt.HasValue;
        bool hasDeliveredEvidence = deliveredAt.HasValue;
        bool shouldAdvanceToDelivered = request.MarkDelivered || hasDeliveredEvidence;
        bool shouldAdvanceToShipped = request.MarkShipped || hasShippedEvidence;

        if (!order.IsLocalPickup && (shouldAdvanceToShipped || shouldAdvanceToDelivered) && (carrier is null || trackingNumber is null))
        {
            return AppResult<OrderDetailDto>.Validation("Carrier and tracking number are required before marking a shipped storefront order shipped or delivered.");
        }

        var previous = new
        {
            order.Status,
            order.FulfillmentStatus,
            order.ShippingCarrier,
            order.TrackingNumber,
            order.TrackingUrl,
            order.ShippedAt,
            order.DeliveredAt
        };

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
                    order.ShippedAt,
                    order.DeliveredAt
                }
            }),
        });

        await db.SaveChangesAsync(cancellationToken);
        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return AppResult<OrderDetailDto>.Ok(order.ToDetailDto(salesByInventoryItem));
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
        return AppResult<OrderDetailDto>.Ok(refreshed.ToDetailDto(salesByInventoryItem));
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
