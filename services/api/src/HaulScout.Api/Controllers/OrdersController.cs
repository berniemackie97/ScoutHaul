using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaulScout.Api.Authorization;
using HaulScout.Api.Mapping;
using HaulScout.Api.Services;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using static HaulScout.Api.Helpers.StringHelpers;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/orders")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageOrders)]
public sealed class OrdersController(HaulScoutDbContext db, ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>Orders from every channel — the storefront is simply one of them. Pass <paramref name="channel"/>
    /// to narrow to a single marketplace.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] string? channel,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = db.Orders
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out OrderStatus parsed))
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
            // A marketplace buyer has a username, not an email — search both, plus the platform's own order id.
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

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Customer)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        IReadOnlyDictionary<Guid, Sale> salesByInventoryItem = await SalesByInventoryItemAsync(order.Id, cancellationToken);
        return order.ToDetailDto(salesByInventoryItem);
    }

    /// <summary>
    /// Advance or correct an order's lifecycle. Payment/fulfillment flags and timestamps are derived from the
    /// target status so the order stays internally consistent regardless of which transition an operator picks.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Customer)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (order is null)
        {
            return NotFound();
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

        // The order is the single source of truth for fulfillment; push its state onto the sale lines so the
        // sales views reflect it without being a second place to edit fulfillment.
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
        return order.ToDetailDto(salesByInventoryItem);
    }

    [HttpPatch("{id:guid}/tracking")]
    public async Task<ActionResult<OrderDetailDto>> UpdateTracking(Guid id, UpdateOrderTrackingRequest request, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Customer)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        string? carrier = TrimToNull(request.ShippingCarrier);
        string? trackingNumber = TrimToNull(request.TrackingNumber);
        string? trackingUrl = TrimToNull(request.TrackingUrl);
        if ((carrier is null) != (trackingNumber is null))
        {
            return BadRequest("Carrier and tracking number must be supplied together.");
        }

        if (trackingUrl is not null
            && (!Uri.TryCreate(trackingUrl, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            return BadRequest("Tracking URL must be a valid absolute http(s) URL.");
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
            return BadRequest("Shipment timestamps cannot be in the future.");
        }

        if (deliveredAt.HasValue && shippedAt.HasValue && deliveredAt.Value < shippedAt.Value)
        {
            return BadRequest("Delivered time cannot be earlier than shipped time.");
        }

        bool hasShippedEvidence = shippedAt.HasValue;
        bool hasDeliveredEvidence = deliveredAt.HasValue;
        bool shouldAdvanceToDelivered = request.MarkDelivered || hasDeliveredEvidence;
        bool shouldAdvanceToShipped = request.MarkShipped || hasShippedEvidence;

        if (!order.IsLocalPickup && (shouldAdvanceToShipped || shouldAdvanceToDelivered) && (carrier is null || trackingNumber is null))
        {
            return BadRequest("Carrier and tracking number are required before marking a shipped storefront order shipped or delivered.");
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
        return order.ToDetailDto(salesByInventoryItem);
    }

    /// <summary>
    /// Removes an order and its lines. Broken/phantom orders (a stray marketplace import with no lines, wrong totals,
    /// or the wrong id used as an order number) can be cleared out from here. Sales carry financial history and are
    /// never deleted — any sale attached to the order is detached (its order link is cleared) so it survives as a
    /// standalone sale that can be re-grouped later.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        // Detach — never delete — sales. The DB would SetNull on its own, but tests run on the in-memory provider
        // which has no relational cascade, so we do it explicitly to behave identically everywhere.
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
        return NoContent();
    }

    // Financial states are never overwritten by fulfillment — a refunded/returned/cancelled line stays that way.
    private static readonly SaleStatus[] FinancialSaleStatuses =
        [SaleStatus.Returned, SaleStatus.Refunded, SaleStatus.PartiallyRefunded, SaleStatus.Cancelled];

    /// <summary>Pushes the order's fulfillment onto its (non-financial) sale lines, so the sales views mirror the
    /// order without becoming a second place to edit fulfillment.</summary>
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
