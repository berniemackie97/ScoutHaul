using Microsoft.EntityFrameworkCore;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Api.Services;

/// <summary>
/// Builds the channel-agnostic <see cref="Order"/> (and its buyer) for any channel — storefront, eBay, Poshmark,
/// Depop… This is the single place an order header is created off the storefront checkout path, so a marketplace
/// order is a first-class <see cref="Order"/> rather than a group id smuggled onto <see cref="Sale"/> rows.
/// </summary>
public sealed class OrderAssembler(HaulScoutDbContext db, ICurrentUserContext currentUser)
{
    /// <summary>Finds the order for this channel + external id, or creates it. Idempotent: re-reconciling the same
    /// marketplace order returns the existing header instead of duplicating it.</summary>
    public async Task<Order> GetOrCreateAsync(
        string channel,
        string? externalOrderId,
        string? externalOrderUrl,
        string? buyerReference,
        string? buyerName,
        DateTimeOffset orderedAt,
        Guid? channelAccountId,
        decimal itemsSubtotal,
        decimal shippingTotal,
        decimal taxTotal,
        CancellationToken cancellationToken,
        string? shippingCarrier = null,
        string? trackingNumber = null)
    {
        string? normalizedExternalOrderId = TrimToNull(externalOrderId);
        string? normalizedTrackingNumber = TrimToNull(trackingNumber);
        string? normalizedCarrier = TrimToNull(shippingCarrier);

        Order? existing = normalizedExternalOrderId is null
            ? null
            : await db.Orders
                .Include(order => order.Lines)
                .SingleOrDefaultAsync(
                order => order.OrganizationId == currentUser.OrganizationId
                    && order.Channel == channel
                    && order.ExternalOrderId == normalizedExternalOrderId,
                cancellationToken);

        if (existing is null && normalizedTrackingNumber is not null)
        {
            existing = await db.Orders
                .Include(order => order.Lines)
                .OrderBy(order => order.CreatedAt)
                .FirstOrDefaultAsync(
                    order => order.OrganizationId == currentUser.OrganizationId
                        && order.Channel == channel
                        && order.TrackingNumber == normalizedTrackingNumber,
                    cancellationToken);
        }

        Customer? buyer = await ResolveBuyerAsync(channel, buyerReference, buyerName, cancellationToken);

        if (existing is not null)
        {
            existing.CustomerId ??= buyer?.Id;
            existing.ExternalOrderId ??= normalizedExternalOrderId;
            existing.ExternalOrderUrl ??= TrimToNull(externalOrderUrl);
            existing.BuyerReference ??= TrimToNull(buyerReference);
            existing.ChannelAccountId ??= channelAccountId;
            existing.ShippingCarrier ??= normalizedCarrier;
            existing.TrackingNumber ??= normalizedTrackingNumber;
            existing.PaidAt ??= orderedAt;
            return existing;
        }

        var order = new Order
        {
            OrganizationId = currentUser.OrganizationId,
            Channel = channel,
            ChannelAccountId = channelAccountId,
            ExternalOrderId = normalizedExternalOrderId,
            ExternalOrderUrl = TrimToNull(externalOrderUrl),
            BuyerReference = TrimToNull(buyerReference),
            CustomerId = buyer?.Id,
            // Marketplaces number their own orders; the storefront generates ours.
            OrderNumber = BuildOrderNumber(channel, normalizedExternalOrderId, normalizedTrackingNumber),
            Email = null,
            Subtotal = itemsSubtotal,
            ShippingTotal = shippingTotal,
            TaxTotal = taxTotal,
            GrandTotal = itemsSubtotal + shippingTotal + taxTotal,
            // A marketplace order only reaches us once the buyer has already paid.
            Status = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Paid,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            PaidAt = orderedAt,
            CreatedAt = orderedAt,
            ShippingCarrier = normalizedCarrier,
            TrackingNumber = normalizedTrackingNumber,
        };
        db.Orders.Add(order);
        return order;
    }

    /// <summary>
    /// Ensures every sale attached to an order has a storefront-compatible order line and keeps the order totals
    /// in step with the sale lines. This is what makes manually recorded marketplace sales show up in Orders.
    /// </summary>
    public async Task SyncSalesOrderAsync(
        Order order,
        IReadOnlyCollection<Sale> changedSales,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? removedSaleIds = null)
    {
        HashSet<Guid> removed = removedSaleIds?.ToHashSet() ?? [];
        foreach (Sale sale in changedSales)
        {
            if (removed.Contains(sale.Id))
            {
                continue;
            }

            sale.OrderId = order.Id;
            sale.Order = order;
        }

        List<Sale> persistedSales = await db.Sales
            .Include(sale => sale.InventoryItem)
            .Where(sale => sale.OrganizationId == currentUser.OrganizationId && sale.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        List<Sale> allSales = persistedSales
            .Concat(changedSales)
            .Where(sale => sale.OrderId == order.Id && !removed.Contains(sale.Id))
            .GroupBy(sale => sale.Id)
            .Select(group => group.First())
            .ToList();

        List<OrderLine> existingLines = await db.OrderLines
            .Where(line => line.OrganizationId == currentUser.OrganizationId && line.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        HashSet<Guid> activeInventoryIds = allSales.Select(sale => sale.InventoryItemId).ToHashSet();
        List<OrderLine> staleLines = existingLines
            .Where(line => !activeInventoryIds.Contains(line.InventoryItemId))
            .ToList();
        if (staleLines.Count > 0)
        {
            db.OrderLines.RemoveRange(staleLines);
            foreach (OrderLine stale in staleLines)
            {
                existingLines.Remove(stale);
            }
        }

        foreach (Sale sale in allSales)
        {
            InventoryItem? item = sale.InventoryItem
                ?? await db.InventoryItems.SingleOrDefaultAsync(candidate => candidate.Id == sale.InventoryItemId && candidate.OrganizationId == currentUser.OrganizationId, cancellationToken);
            if (item is null)
            {
                continue;
            }

            OrderLine? line = existingLines.SingleOrDefault(candidate => candidate.InventoryItemId == sale.InventoryItemId);
            if (line is null)
            {
                line = new OrderLine
                {
                    OrganizationId = currentUser.OrganizationId,
                    OrderId = order.Id,
                    InventoryItemId = sale.InventoryItemId,
                    TitleSnapshot = item.Title,
                    Quantity = 1,
                };
                db.OrderLines.Add(line);
                existingLines.Add(line);
            }

            line.TitleSnapshot = item.Title;
            line.SkuSnapshot = item.Barcode;
            line.UnitPrice = sale.SoldPrice;
            line.LineTotal = sale.SoldPrice;
            line.UnitCostSnapshot = sale.CostOfGoods;
        }

        order.Subtotal = allSales.Sum(sale => sale.SoldPrice);
        order.ShippingTotal = allSales.Sum(sale => sale.ShippingCharged);
        order.TaxTotal = allSales.Sum(sale => sale.SalesTaxCollected);
        order.GrandTotal = order.Subtotal + order.ShippingTotal + order.TaxTotal;
        order.PaidAt = allSales.Count > 0 ? allSales.Min(sale => sale.SoldAt) : order.PaidAt;
        order.CreatedAt = order.PaidAt ?? order.CreatedAt;
        order.Status = ResolveOrderStatus(allSales);
        order.PaymentStatus = ResolvePaymentStatus(allSales);
        order.FulfillmentStatus = ResolveFulfillmentStatus(allSales);
    }

    /// <summary>Resolves the buyer on a channel by their handle, creating the customer + channel identity on first
    /// sight. Marketplace buyers have no email, so the (channel, username) pair is the durable key.</summary>
    public async Task<Customer?> ResolveBuyerAsync(string channel, string? buyerReference, string? buyerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerReference))
        {
            return null;
        }

        string handle = buyerReference.Trim();
        var identity = await db.CustomerChannelIdentities
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(
                item => item.OrganizationId == currentUser.OrganizationId && item.Channel == channel && item.ExternalUserId == handle,
                cancellationToken);

        if (identity is not null)
        {
            identity.LastSeenAt = DateTimeOffset.UtcNow;
            return identity.Customer;
        }

        var customer = new Customer
        {
            OrganizationId = currentUser.OrganizationId,
            Email = null,
            DisplayName = string.IsNullOrWhiteSpace(buyerName) ? handle : buyerName.Trim(),
        };
        db.Customers.Add(customer);

        db.CustomerChannelIdentities.Add(new CustomerChannelIdentity
        {
            OrganizationId = currentUser.OrganizationId,
            Customer = customer,
            Channel = channel,
            ExternalUserId = handle,
            DisplayName = string.IsNullOrWhiteSpace(buyerName) ? null : buyerName.Trim(),
        });

        return customer;
    }

    private static string BuildOrderNumber(string channel, string? externalOrderId, string? trackingNumber)
    {
        string seed = externalOrderId ?? trackingNumber ?? $"{channel[..Math.Min(3, channel.Length)].ToUpperInvariant()}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        string normalized = new(seed.Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-').ToArray());
        return normalized.Length <= 40 ? normalized : normalized[..40];
    }

    private static string? TrimToNull(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static OrderStatus ResolveOrderStatus(IReadOnlyCollection<Sale> sales)
    {
        if (sales.Count == 0) return OrderStatus.Paid;
        if (sales.All(sale => sale.Status == SaleStatus.Cancelled)) return OrderStatus.Cancelled;
        if (sales.All(sale => sale.Status == SaleStatus.Refunded)) return OrderStatus.Refunded;
        if (sales.All(sale => sale.Status == SaleStatus.Delivered)) return OrderStatus.Delivered;
        if (sales.Any(sale => sale.Status is SaleStatus.Shipped or SaleStatus.Delivered)) return OrderStatus.Shipped;
        if (sales.Any(sale => sale.Status == SaleStatus.Packed)) return OrderStatus.Fulfilling;
        return OrderStatus.Paid;
    }

    private static PaymentStatus ResolvePaymentStatus(IReadOnlyCollection<Sale> sales)
    {
        if (sales.Count == 0) return PaymentStatus.Paid;
        if (sales.All(sale => sale.Status == SaleStatus.Refunded)) return PaymentStatus.Refunded;
        if (sales.Any(sale => sale.Status == SaleStatus.PartiallyRefunded)) return PaymentStatus.PartiallyRefunded;
        return PaymentStatus.Paid;
    }

    private static FulfillmentStatus ResolveFulfillmentStatus(IReadOnlyCollection<Sale> sales)
    {
        if (sales.Count == 0) return FulfillmentStatus.Unfulfilled;
        return sales.All(sale => sale.Status is SaleStatus.Shipped or SaleStatus.Delivered)
            ? FulfillmentStatus.Fulfilled
            : sales.Any(sale => sale.Status is SaleStatus.Packed or SaleStatus.Shipped or SaleStatus.Delivered)
                ? FulfillmentStatus.Partial
                : FulfillmentStatus.Unfulfilled;
    }
}
