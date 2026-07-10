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
        CancellationToken cancellationToken)
    {
        Order? existing = externalOrderId is null
            ? null
            : await db.Orders.SingleOrDefaultAsync(
                order => order.OrganizationId == currentUser.OrganizationId
                    && order.Channel == channel
                    && order.ExternalOrderId == externalOrderId,
                cancellationToken);

        Customer? buyer = await ResolveBuyerAsync(channel, buyerReference, buyerName, cancellationToken);

        if (existing is not null)
        {
            existing.CustomerId ??= buyer?.Id;
            return existing;
        }

        var order = new Order
        {
            OrganizationId = currentUser.OrganizationId,
            Channel = channel,
            ChannelAccountId = channelAccountId,
            ExternalOrderId = externalOrderId,
            ExternalOrderUrl = externalOrderUrl,
            BuyerReference = buyerReference,
            CustomerId = buyer?.Id,
            // Marketplaces number their own orders; the storefront generates ours.
            OrderNumber = externalOrderId ?? $"{channel[..Math.Min(3, channel.Length)].ToUpperInvariant()}-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
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
        };
        db.Orders.Add(order);
        return order;
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
}
