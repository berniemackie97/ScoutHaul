using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Security;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Orders;

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
        string? trackingNumber = null,
        string? buyerEmail = null,
        string? buyerNote = null,
        string? shipToName = null,
        string? shipToPhone = null,
        string? shipToLine1 = null,
        string? shipToLine2 = null,
        string? shipToCity = null,
        string? shipToRegion = null,
        string? shipToPostalCode = null,
        string? shipToCountry = null,
        DateTimeOffset? paidAt = null,
        DateTimeOffset? shippedAt = null)
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

        Customer? buyer = await ResolveBuyerAsync(channel, buyerReference, buyerName, buyerEmail, cancellationToken);

        if (existing is not null)
        {
            existing.CustomerId ??= buyer?.Id;
            existing.Email ??= TrimToNull(buyerEmail);
            existing.ExternalOrderId ??= normalizedExternalOrderId;
            existing.ExternalOrderUrl ??= TrimToNull(externalOrderUrl);
            existing.BuyerReference ??= TrimToNull(buyerReference);
            existing.ChannelAccountId ??= channelAccountId;
            existing.ShippingCarrier ??= normalizedCarrier;
            existing.TrackingNumber ??= normalizedTrackingNumber;
            existing.CustomerNote ??= TrimToNull(buyerNote);
            existing.PaidAt ??= paidAt ?? orderedAt;
            existing.ShippedAt ??= shippedAt;
            ApplyShipTo(existing, shipToName, shipToPhone, shipToLine1, shipToLine2, shipToCity, shipToRegion, shipToPostalCode, shipToCountry);
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
            Email = TrimToNull(buyerEmail),
            Subtotal = itemsSubtotal,
            ShippingTotal = shippingTotal,
            TaxTotal = taxTotal,
            GrandTotal = itemsSubtotal + shippingTotal + taxTotal,
            // A marketplace order only reaches us once the buyer has already paid.
            Status = OrderStatus.Paid,
            PaymentStatus = PaymentStatus.Paid,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            PaidAt = paidAt ?? orderedAt,
            CreatedAt = orderedAt,
            ShippingCarrier = normalizedCarrier,
            TrackingNumber = normalizedTrackingNumber,
            CustomerNote = TrimToNull(buyerNote),
            ShipToName = TrimToNull(shipToName),
            ShipToLine1 = TrimToNull(shipToLine1),
            ShipToLine2 = TrimToNull(shipToLine2),
            ShipToCity = TrimToNull(shipToCity),
            ShipToRegion = TrimToNull(shipToRegion),
            ShipToPostalCode = TrimToNull(shipToPostalCode),
            ShipToCountry = NormalizeCountry(shipToCountry),
            ShippedAt = shippedAt,
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
        // Money syncs from the sale lines; FULFILLMENT is owned by the Order and set only by its fulfillment
        // actions. Deriving it here would overwrite an operator's "delivered" the next time a sale is edited.
        order.Status = ResolveMoneyStatus(allSales, order.Status);
        order.PaymentStatus = ResolvePaymentStatus(allSales);
    }

    /// <summary>Deletes a non-storefront order that no longer has sale lines. Used when a corrected import moves
    /// sales from a bad/generated order onto the platform's real order number.</summary>
    public async Task PruneEmptyMarketplaceOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        Order? order = await db.Orders
            .Include(item => item.Lines)
            .SingleOrDefaultAsync(
                item => item.Id == orderId && item.OrganizationId == currentUser.OrganizationId && item.Channel != "Storefront",
                cancellationToken);
        if (order is null)
        {
            return;
        }

        bool hasSales = await db.Sales.AnyAsync(
            sale => sale.OrganizationId == currentUser.OrganizationId && sale.OrderId == order.Id,
            cancellationToken);
        if (hasSales)
        {
            return;
        }

        if (order.Lines.Count > 0)
        {
            db.OrderLines.RemoveRange(order.Lines);
        }
        db.Orders.Remove(order);
    }

    /// <summary>Resolves the buyer on a channel by their handle, creating the customer + channel identity on first
    /// sight. Marketplace buyers have no email, so the (channel, username) pair is the durable key.</summary>
    public async Task<Customer?> ResolveBuyerAsync(string channel, string? buyerReference, string? buyerName, string? buyerEmail, CancellationToken cancellationToken)
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
            identity.Email ??= TrimToNull(buyerEmail);
            identity.DisplayName ??= TrimToNull(buyerName);
            if (identity.Customer is not null)
            {
                identity.Customer.Email ??= TrimToNull(buyerEmail);
                identity.Customer.DisplayName ??= TrimToNull(buyerName);
            }
            return identity.Customer;
        }

        // No identity on this channel yet. Before inventing a person, look for one we already have:
        // the same buyer routinely appears first from a storefront checkout and later from a
        // marketplace import, and creating a second row would both split their history and — because
        // Email is unique per organization — fail the insert outright. Joining the new channel
        // identity onto the existing customer converges the two views of one person instead.
        string? email = TrimToNull(buyerEmail);
        Customer? customer = email is null
            ? null
            : await db.Customers.FirstOrDefaultAsync(
                candidate => candidate.OrganizationId == currentUser.OrganizationId
                    && candidate.Email != null
                    && candidate.Email.ToLower() == email.ToLower(),
                cancellationToken);

        if (customer is not null)
        {
            customer.DisplayName ??= string.IsNullOrWhiteSpace(buyerName) ? handle : buyerName.Trim();
        }
        else
        {
            customer = new Customer
            {
                OrganizationId = currentUser.OrganizationId,
                Email = email,
                DisplayName = string.IsNullOrWhiteSpace(buyerName) ? handle : buyerName.Trim(),
            };
            db.Customers.Add(customer);
        }

        db.CustomerChannelIdentities.Add(new CustomerChannelIdentity
        {
            OrganizationId = currentUser.OrganizationId,
            Customer = customer,
            Channel = channel,
            ExternalUserId = handle,
            DisplayName = string.IsNullOrWhiteSpace(buyerName) ? null : buyerName.Trim(),
            Email = email,
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

    private static string NormalizeCountry(string? value)
    {
        string? country = TrimToNull(value);
        if (country is null) return "US";
        if (country.Equals("United States", StringComparison.OrdinalIgnoreCase)
            || country.Equals("United States of America", StringComparison.OrdinalIgnoreCase))
        {
            return "US";
        }
        return country.Length <= 2 ? country.ToUpperInvariant() : country[..2].ToUpperInvariant();
    }

    private static void ApplyShipTo(
        Order order,
        string? shipToName,
        string? shipToPhone,
        string? shipToLine1,
        string? shipToLine2,
        string? shipToCity,
        string? shipToRegion,
        string? shipToPostalCode,
        string? shipToCountry)
    {
        order.ShipToName ??= TrimToNull(shipToName);
        order.ShipToLine1 ??= TrimToNull(shipToLine1);
        order.ShipToLine2 ??= TrimToNull(shipToLine2);
        order.ShipToCity ??= TrimToNull(shipToCity);
        order.ShipToRegion ??= TrimToNull(shipToRegion);
        order.ShipToPostalCode ??= TrimToNull(shipToPostalCode);
        if (order.ShipToCountry is null)
        {
            order.ShipToCountry = NormalizeCountry(shipToCountry);
        }
        _ = shipToPhone; // Phone is retained on PlatformOrder for audit; Order currently has no ship-to phone field.
    }

    // Resolves only the money-and-lifecycle part of OrderStatus from the sale lines. A fulfillment state the
    // order already carries (set by its own fulfillment action) is preserved — never downgraded back to Paid.
    private static OrderStatus ResolveMoneyStatus(IReadOnlyCollection<Sale> sales, OrderStatus current)
    {
        if (sales.Count == 0) return current;
        if (sales.All(sale => sale.Status == SaleStatus.Cancelled)) return OrderStatus.Cancelled;
        if (sales.All(IsFullyRefundedState)) return OrderStatus.Refunded;
        // Keep the operator's fulfillment progress; only lift a pending/paid order to Paid.
        if (current is OrderStatus.Fulfilling or OrderStatus.Shipped or OrderStatus.Delivered) return current;
        return OrderStatus.Paid;
    }

    private static PaymentStatus ResolvePaymentStatus(IReadOnlyCollection<Sale> sales)
    {
        if (sales.Count == 0) return PaymentStatus.Paid;
        if (sales.All(IsFullyRefundedState)) return PaymentStatus.Refunded;
        if (sales.Any(sale => sale.Status == SaleStatus.PartiallyRefunded)) return PaymentStatus.PartiallyRefunded;
        return PaymentStatus.Paid;
    }

    private static bool IsFullyRefundedState(Sale sale) =>
        sale.Status is SaleStatus.Refunded or SaleStatus.Returned;
}
