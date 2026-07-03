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
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrderSummaryDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Orders
            .Include(order => order.Customer)
            .Include(order => order.Lines)
            .Where(order => order.OrganizationId == currentUser.OrganizationId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(order => order.Status == parsed);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(order => EF.Functions.ILike(order.OrderNumber, $"%{term}%") || EF.Functions.ILike(order.Email, $"%{term}%"));
        }

        var orders = await query
            .OrderByDescending(order => order.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        return orders.Select(order => order.ToSummaryDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .Include(item => item.Customer)
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id && item.OrganizationId == currentUser.OrganizationId, cancellationToken);

        return order is null ? NotFound() : order.ToDetailDto();
    }

    /// <summary>
    /// Advance or correct an order's lifecycle. Payment/fulfillment flags and timestamps are derived from the
    /// target status so the order stays internally consistent regardless of which transition an operator picks.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(Guid id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders
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
        return order.ToDetailDto();
    }

    [HttpPatch("{id:guid}/tracking")]
    public async Task<ActionResult<OrderDetailDto>> UpdateTracking(Guid id, UpdateOrderTrackingRequest request, CancellationToken cancellationToken)
    {
        var order = await db.Orders
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
            && (!Uri.TryCreate(trackingUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
        {
            return BadRequest("Tracking URL must be a valid absolute http(s) URL.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? shippedAt = request.ShippedAt ?? (request.MarkShipped || request.MarkDelivered ? now : order.ShippedAt);
        DateTimeOffset? deliveredAt = request.DeliveredAt ?? (request.MarkDelivered ? now : order.DeliveredAt);

        if (shippedAt > now.AddMinutes(5) || deliveredAt > now.AddMinutes(5))
        {
            return BadRequest("Shipment timestamps cannot be in the future.");
        }

        if (deliveredAt.HasValue && shippedAt.HasValue && deliveredAt.Value < shippedAt.Value)
        {
            return BadRequest("Delivered time cannot be earlier than shipped time.");
        }

        if (!order.IsLocalPickup && (request.MarkShipped || request.MarkDelivered) && (carrier is null || trackingNumber is null))
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

        if (request.MarkDelivered)
        {
            order.Status = OrderStatus.Delivered;
            order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
            if (order.PaymentStatus == PaymentStatus.Pending)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
            }
        }
        else if (request.MarkShipped)
        {
            order.Status = OrderStatus.Shipped;
            order.FulfillmentStatus = FulfillmentStatus.Fulfilled;
            if (order.PaymentStatus == PaymentStatus.Pending)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.PaidAt ??= now;
            }
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
        return order.ToDetailDto();
    }
}
