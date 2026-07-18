using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>What handling a payment event amounted to — the controller's HTTP answer and the log line.</summary>
public enum PaymentEventOutcome
{
    /// <summary>Not ours or not actionable — acknowledged so the processor stops retrying.</summary>
    Ignored,
    /// <summary>Already in the state this event asks for. Processors redeliver; redelivery must be a no-op.</summary>
    AlreadyProcessed,
    Finalized,
    Released,
    /// <summary>Money arrived for an item this order no longer holds — payment auto-refunded, order cancelled.</summary>
    RefundedConflict,
    /// <summary>The paid amounts don't match the order. Never finalized silently — this demands eyes.</summary>
    AmountMismatch,
}

/// <summary>
/// Completes or unwinds a checkout when the processor reports what happened. This is the only writer of a storefront
/// order's paid state, and it is idempotent from every direction: Stripe redelivers events for days, and the same
/// event must land exactly once no matter how many times it arrives.
/// </summary>
public sealed class StorefrontOrderService(
    IStorefrontCheckoutStore checkout,
    IPaymentGateway payments,
    TimeProvider timeProvider)
{
    public async Task<PaymentEventOutcome> HandleAsync(PaymentEvent paymentEvent, CancellationToken cancellationToken)
    {
        if (paymentEvent.Kind == PaymentEventKind.Unhandled)
        {
            return PaymentEventOutcome.Ignored;
        }

        Order? order = await checkout.FindOrderBySessionAsync(paymentEvent.SessionId, cancellationToken);
        if (order is null)
        {
            // Not an error: another environment's event, or a session created outside this system.
            return PaymentEventOutcome.Ignored;
        }

        return paymentEvent.Kind switch
        {
            PaymentEventKind.CheckoutCompleted => await FinalizeAsync(order, paymentEvent, cancellationToken),
            PaymentEventKind.CheckoutExpired => await ReleaseAsync(order, cancellationToken),
            _ => PaymentEventOutcome.Ignored,
        };
    }

    private async Task<PaymentEventOutcome> FinalizeAsync(Order order, PaymentEvent paymentEvent, CancellationToken cancellationToken)
    {
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return PaymentEventOutcome.AlreadyProcessed;
        }
        if (order.Status != OrderStatus.Pending || order.PaymentStatus != PaymentStatus.Pending)
        {
            return await RefundInactiveCheckoutAsync(order, paymentEvent, cancellationToken);
        }

        // The item subtotal is the one figure this system set; the processor echoing anything else back means the
        // payment and the order disagree, and a disagreement about money never resolves itself silently.
        if (paymentEvent.AmountSubtotalCents is long subtotalCents
            && subtotalCents != CheckoutWorkflowService.ToCents(order.Subtotal))
        {
            return PaymentEventOutcome.AmountMismatch;
        }

        Guid organizationId = order.OrganizationId;
        Guid[] listingIds = order.Lines
            .Where(line => line.StoreListingId is not null)
            .Select(line => line.StoreListingId!.Value)
            .ToArray();

        IReadOnlyList<Guid> conflicted = await checkout.MarkSoldAsync(organizationId, listingIds, cancellationToken);
        if (conflicted.Count > 0)
        {
            // The near-unreachable path (reservation guards it): paid, but the item is gone. The money goes back
            // immediately and loudly rather than shipping nothing.
            return await RefundConflictAsync(order, paymentEvent, listingIds, cancellationToken);
        }

        IReadOnlyList<InventoryItem> items = await checkout.ListItemsForListingsAsync(organizationId, listingIds, cancellationToken);
        if (items.Any(item => !StorefrontPurchasability.IsItemSellable(item)))
        {
            // The reservation held, but the ITEM left the shelf anyway — sold on another channel during the payment
            // window, which store reservations cannot yet block. Booking it again would double-sell one physical
            // thing; refunding is the only honest outcome.
            return await RefundConflictAsync(order, paymentEvent, listingIds, cancellationToken);
        }

        DateTimeOffset paidAt = timeProvider.GetUtcNow();
        ApplyPaidState(order, paymentEvent, paidAt);
        MarkInventorySold(items, paidAt);
        await ConvertCartAsync(order, cancellationToken);
        await AttachCustomerAsync(order, paymentEvent, paidAt, cancellationToken);
        await RecordSalesAsync(order, paymentEvent, paidAt, cancellationToken);
        await MarkOffersConvertedAsync(order, paidAt, cancellationToken);

        await checkout.SaveChangesAsync(cancellationToken);
        return PaymentEventOutcome.Finalized;
    }

    private async Task<PaymentEventOutcome> RefundConflictAsync(
        Order order,
        PaymentEvent paymentEvent,
        Guid[] listingIds,
        CancellationToken cancellationToken)
    {
        if (paymentEvent.PaymentIntentId is string paymentIntentId)
        {
            await payments.RefundAsync(paymentIntentId, $"Order {order.OrderNumber}: item no longer available.", cancellationToken);
        }
        await checkout.ReleaseAsync(order.OrganizationId, listingIds, cancellationToken);
        order.Status = OrderStatus.Cancelled;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.CancelledAt = timeProvider.GetUtcNow();
        order.CustomerNote = "Auto-refunded: an item was no longer available when payment completed.";
        await checkout.SaveChangesAsync(cancellationToken);
        return PaymentEventOutcome.RefundedConflict;
    }

    private async Task<PaymentEventOutcome> RefundInactiveCheckoutAsync(
        Order order,
        PaymentEvent paymentEvent,
        CancellationToken cancellationToken)
    {
        if (paymentEvent.PaymentIntentId is string paymentIntentId)
        {
            await payments.RefundAsync(paymentIntentId, $"Order {order.OrderNumber}: checkout was no longer active.", cancellationToken);
        }
        order.Status = OrderStatus.Cancelled;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.CancelledAt ??= timeProvider.GetUtcNow();
        order.CustomerNote = "Auto-refunded: payment arrived for a checkout session that was no longer active.";
        await checkout.SaveChangesAsync(cancellationToken);
        return PaymentEventOutcome.RefundedConflict;
    }

    private async Task<PaymentEventOutcome> ReleaseAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.Status != OrderStatus.Pending)
        {
            return PaymentEventOutcome.AlreadyProcessed;
        }

        await checkout.ReleaseAsync(
            order.OrganizationId,
            order.Lines.Where(line => line.StoreListingId is not null).Select(line => line.StoreListingId!.Value).ToArray(),
            cancellationToken);
        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = timeProvider.GetUtcNow();
        await checkout.SaveChangesAsync(cancellationToken);
        return PaymentEventOutcome.Released;
    }

    private static void ApplyPaidState(Order order, PaymentEvent paymentEvent, DateTimeOffset paidAt)
    {
        order.Status = OrderStatus.Paid;
        order.PaymentStatus = PaymentStatus.Paid;
        order.PaidAt = paidAt;
        order.StripePaymentIntentId = paymentEvent.PaymentIntentId;
        order.Email = paymentEvent.Email ?? order.Email;
        order.IsLocalPickup = paymentEvent.LocalPickupChosen;

        // Shipping and tax were decided on the payment page; this is where the order learns its true totals.
        order.ShippingTotal = FromCents(paymentEvent.ShippingCents);
        order.TaxTotal = FromCents(paymentEvent.TaxCents);
        order.GrandTotal = paymentEvent.AmountTotalCents is long total ? FromCents(total) : order.Subtotal;

        if (paymentEvent.ShipTo is ShipTo shipTo)
        {
            order.ShipToName = shipTo.Name ?? paymentEvent.CustomerName;
            order.ShipToLine1 = shipTo.Line1;
            order.ShipToLine2 = shipTo.Line2;
            order.ShipToCity = shipTo.City;
            order.ShipToRegion = shipTo.Region;
            order.ShipToPostalCode = shipTo.PostalCode;
            order.ShipToCountry = shipTo.Country;
        }
    }

    private static void MarkInventorySold(IReadOnlyList<InventoryItem> items, DateTimeOffset paidAt)
    {
        foreach (InventoryItem item in items)
        {
            item.Status = InventoryStatus.Sold;
            item.SoldAt = paidAt;
            InventoryStatePropagation.ApplyBundleComponentInventoryStatus(item, InventoryStatus.Sold, paidAt);
        }
    }

    private async Task ConvertCartAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.SourceCartId is not Guid cartId)
        {
            return;
        }

        Cart? cart = await checkout.FindCartByIdAsync(order.OrganizationId, cartId, cancellationToken);
        if (cart is not null)
        {
            cart.Status = CartStatus.Converted;
        }
    }

    private async Task AttachCustomerAsync(Order order, PaymentEvent paymentEvent, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        if (paymentEvent.Email is not string email)
        {
            return;
        }

        if (order.CustomerId is Guid existingCustomerId)
        {
            Customer? existingCustomer = await checkout.FindCustomerByIdAsync(order.OrganizationId, existingCustomerId, cancellationToken);
            if (existingCustomer is not null)
            {
                existingCustomer.Email ??= email;
                existingCustomer.DisplayName = paymentEvent.CustomerName ?? existingCustomer.DisplayName;
                UpsertStorefrontIdentity(existingCustomer, order.OrganizationId, email, paymentEvent.CustomerName, seenAt);
                order.Customer = existingCustomer;
                return;
            }
        }

        Customer? customer = await checkout.FindCustomerByEmailAsync(order.OrganizationId, email, cancellationToken);
        if (customer is null)
        {
            customer = new Customer
            {
                OrganizationId = order.OrganizationId,
                Email = email,
                DisplayName = paymentEvent.CustomerName,
            };
            UpsertStorefrontIdentity(customer, order.OrganizationId, email, paymentEvent.CustomerName, seenAt);
            await checkout.AddCustomerAsync(customer, cancellationToken);
        }
        else
        {
            UpsertStorefrontIdentity(customer, order.OrganizationId, email, paymentEvent.CustomerName, seenAt);
        }

        order.CustomerId = customer.Id;
        order.Customer = customer;
    }

    private static void UpsertStorefrontIdentity(
        Customer customer,
        Guid organizationId,
        string email,
        string? displayName,
        DateTimeOffset seenAt)
    {
        CustomerChannelIdentity? channelIdentity = customer.ChannelIdentities.SingleOrDefault(identity =>
            identity.OrganizationId == organizationId
            && identity.Channel == "Storefront"
            && identity.ExternalUserId == email);

        if (channelIdentity is null)
        {
            customer.ChannelIdentities.Add(new CustomerChannelIdentity
            {
                OrganizationId = organizationId,
                CustomerId = customer.Id,
                Channel = "Storefront",
                ExternalUserId = email,
                Email = email,
                DisplayName = displayName,
                FirstSeenAt = seenAt,
                LastSeenAt = seenAt,
            });
            return;
        }

        channelIdentity.Email = email;
        channelIdentity.DisplayName = displayName ?? channelIdentity.DisplayName;
        channelIdentity.LastSeenAt = seenAt;
    }

    /// <summary>
    /// One Sale per line — the same financial record every other channel produces, so reports need no storefront
    /// special case. Buyer-paid shipping, tax, and the processor fee are allocated across lines by value; tax is
    /// ours to remit (Stripe Tax collects under OUR registrations), so it books as a liability, never as revenue.
    /// </summary>
    private async Task RecordSalesAsync(Order order, PaymentEvent paymentEvent, DateTimeOffset paidAt, CancellationToken cancellationToken)
    {
        long feeCents = 0;
        if (paymentEvent.PaymentIntentId is string paymentIntentId)
        {
            PaymentCapture capture = await payments.GetCaptureAsync(paymentIntentId, cancellationToken);
            feeCents = capture.FeeCents ?? 0;
        }

        decimal[] weights = order.Lines.Select(line => line.LineTotal).ToArray();
        decimal[] shipping = Allocate(FromCents(paymentEvent.ShippingCents), weights);
        decimal[] tax = Allocate(FromCents(paymentEvent.TaxCents), weights);
        decimal[] fees = Allocate(FromCents(feeCents), weights);

        for (int index = 0; index < order.Lines.Count; index++)
        {
            OrderLine line = order.Lines.ElementAt(index);
            Sale sale = new()
            {
                OrganizationId = order.OrganizationId,
                InventoryItemId = line.InventoryItemId,
                Platform = "Storefront",
                SoldPrice = line.UnitPrice,
                ShippingCharged = shipping[index],
                CostOfGoods = line.UnitCostSnapshot ?? 0,
                PaymentFees = fees[index],
                SalesTaxCollected = tax[index],
                TaxRemittedByPlatform = false,
                Status = SaleStatus.Recorded,
                SoldAt = paidAt,
                OrderId = order.Id,
                Order = order,
            };
            sale.RecomputeNet();
            await checkout.AddSaleAsync(sale, cancellationToken);
        }
    }

    private async Task MarkOffersConvertedAsync(Order order, DateTimeOffset paidAt, CancellationToken cancellationToken)
    {
        Guid[] offerIds = order.Lines
            .Where(line => line.StorefrontOfferId is not null)
            .Select(line => line.StorefrontOfferId!.Value)
            .Distinct()
            .ToArray();
        await checkout.MarkOffersConvertedAsync(order.OrganizationId, offerIds, paidAt, cancellationToken);
    }

    /// <summary>Splits a total across lines proportionally to value, cent-exact: rounding residue lands on the
    /// first line so the parts always sum to the whole.</summary>
    public static decimal[] Allocate(decimal total, decimal[] weights)
    {
        if (weights.Length == 0 || total == 0)
        {
            return new decimal[weights.Length];
        }

        decimal weightSum = weights.Sum();
        decimal[] parts = new decimal[weights.Length];
        for (int index = 0; index < weights.Length; index++)
        {
            parts[index] = weightSum == 0
                ? Math.Round(total / weights.Length, 2, MidpointRounding.AwayFromZero)
                : Math.Round(total * (weights[index] / weightSum), 2, MidpointRounding.AwayFromZero);
        }

        parts[0] += total - parts.Sum();
        return parts;
    }

    private static decimal FromCents(long? cents) => (cents ?? 0) / 100m;
}
