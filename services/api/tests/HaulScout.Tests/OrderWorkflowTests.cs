using HaulScout.Domain;

namespace HaulScout.Tests;

public class OrderWorkflowTests
{
    /// <summary>A paid, addressed, unpacked shipping order — the common case each test varies from.</summary>
    private static OrderWorkflowFacts Paid(
        bool isLocalPickup = false,
        bool isPacked = false,
        bool hasCarrier = false,
        bool hasTrackingNumber = false,
        bool hasShippingAddress = true,
        bool isShipped = false,
        bool isDelivered = false,
        bool hasOpenReturn = false,
        OrderStatus status = OrderStatus.Paid,
        PaymentStatus paymentStatus = PaymentStatus.Paid) =>
        new(status, paymentStatus, isLocalPickup, isPacked, hasCarrier, hasTrackingNumber,
            hasShippingAddress, isShipped, isDelivered, hasOpenReturn);

    // ── Stage derivation ──────────────────────────────────────────────

    [Fact]
    public void Unpaid_order_awaits_payment()
        => Assert.Equal(
            OrderWorkflowStage.AwaitingPayment,
            OrderWorkflow.StageFor(Paid(status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending)));

    [Fact]
    public void Paid_but_unpacked_order_is_to_pack()
        => Assert.Equal(OrderWorkflowStage.ToPack, OrderWorkflow.StageFor(Paid()));

    [Fact]
    public void Packed_shipping_order_is_to_ship()
        => Assert.Equal(OrderWorkflowStage.ToShip, OrderWorkflow.StageFor(Paid(isPacked: true)));

    [Fact]
    public void Packed_pickup_order_waits_for_collection_rather_than_a_carrier()
        => Assert.Equal(
            OrderWorkflowStage.ReadyForPickup,
            OrderWorkflow.StageFor(Paid(isLocalPickup: true, isPacked: true)));

    [Fact]
    public void Shipped_order_is_in_transit()
        => Assert.Equal(
            OrderWorkflowStage.InTransit,
            OrderWorkflow.StageFor(Paid(isPacked: true, hasCarrier: true, hasTrackingNumber: true, isShipped: true)));

    [Fact]
    public void Delivered_order_is_delivered()
        => Assert.Equal(
            OrderWorkflowStage.Delivered,
            OrderWorkflow.StageFor(Paid(isPacked: true, isShipped: true, isDelivered: true)));

    [Fact]
    public void Cancelled_order_is_closed()
        => Assert.Equal(
            OrderWorkflowStage.Closed,
            OrderWorkflow.StageFor(Paid(status: OrderStatus.Cancelled)));

    [Fact]
    public void Fully_refunded_order_is_closed()
        => Assert.Equal(
            OrderWorkflowStage.Closed,
            OrderWorkflow.StageFor(Paid(paymentStatus: PaymentStatus.Refunded)));

    [Fact]
    public void Partially_refunded_order_still_counts_as_paid_because_the_buyer_kept_the_goods()
        => Assert.Equal(
            OrderWorkflowStage.ToPack,
            OrderWorkflow.StageFor(Paid(paymentStatus: PaymentStatus.PartiallyRefunded)));

    [Fact]
    public void Open_return_outranks_position_wherever_the_order_got_to()
    {
        Assert.Equal(
            OrderWorkflowStage.NeedsAttention,
            OrderWorkflow.StageFor(Paid(isPacked: true, isShipped: true, isDelivered: true, hasOpenReturn: true)));
        Assert.Equal(
            OrderWorkflowStage.NeedsAttention,
            OrderWorkflow.StageFor(Paid(hasOpenReturn: true)));
    }

    [Fact]
    public void A_cancelled_order_stays_closed_even_with_an_open_return()
        => Assert.Equal(
            OrderWorkflowStage.Closed,
            OrderWorkflow.StageFor(Paid(status: OrderStatus.Cancelled, hasOpenReturn: true)));

    // ── The ship gate: the rule that makes tracking reliable ──────────

    [Fact]
    public void Shipping_is_blocked_until_carrier_and_tracking_are_entered()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(Paid(isPacked: true), OrderWorkflowAction.Ship);

        Assert.False(transition.Allowed);
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("carrier", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("tracking", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Shipping_is_allowed_once_packed_with_carrier_and_tracking()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(
            Paid(isPacked: true, hasCarrier: true, hasTrackingNumber: true), OrderWorkflowAction.Ship);

        Assert.True(transition.Allowed);
        Assert.Empty(transition.Blockers);
    }

    [Fact]
    public void Shipping_an_unpacked_order_is_blocked()
        => Assert.False(OrderWorkflow
            .Evaluate(Paid(hasCarrier: true, hasTrackingNumber: true), OrderWorkflowAction.Ship).Allowed);

    [Fact]
    public void Shipping_an_unpaid_order_is_blocked()
        => Assert.False(OrderWorkflow.Evaluate(
            Paid(isPacked: true, hasCarrier: true, hasTrackingNumber: true,
                 status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending),
            OrderWorkflowAction.Ship).Allowed);

    [Fact]
    public void A_pickup_order_cannot_be_shipped_and_says_why()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(
            Paid(isLocalPickup: true, isPacked: true), OrderWorkflowAction.Ship);

        Assert.False(transition.Allowed);
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("picked up", StringComparison.OrdinalIgnoreCase));
    }

    // ── The pack gate ────────────────────────────────────────────────

    [Fact]
    public void Packing_an_unpaid_order_is_blocked()
        => Assert.False(OrderWorkflow.Evaluate(
            Paid(status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending),
            OrderWorkflowAction.MarkPacked).Allowed);

    [Fact]
    public void Packing_a_shipping_order_without_an_address_is_blocked()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(
            Paid(hasShippingAddress: false), OrderWorkflowAction.MarkPacked);

        Assert.False(transition.Allowed);
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_pickup_order_needs_no_shipping_address_to_be_packed()
        => Assert.True(OrderWorkflow
            .Evaluate(Paid(isLocalPickup: true, hasShippingAddress: false), OrderWorkflowAction.MarkPacked).Allowed);

    // ── Delivery + pickup completion ─────────────────────────────────

    [Fact]
    public void Marking_delivered_before_shipping_is_blocked_the_bug_that_started_this()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(Paid(isPacked: true), OrderWorkflowAction.MarkDelivered);

        Assert.False(transition.Allowed);
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("not shipped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Marking_delivered_after_shipping_is_allowed()
        => Assert.True(OrderWorkflow
            .Evaluate(Paid(isPacked: true, hasCarrier: true, hasTrackingNumber: true, isShipped: true),
                      OrderWorkflowAction.MarkDelivered).Allowed);

    [Fact]
    public void Marking_delivered_twice_is_blocked()
        => Assert.False(OrderWorkflow
            .Evaluate(Paid(isPacked: true, isShipped: true, isDelivered: true), OrderWorkflowAction.MarkDelivered).Allowed);

    [Fact]
    public void Pickup_completion_requires_a_packed_pickup_order()
    {
        Assert.True(OrderWorkflow
            .Evaluate(Paid(isLocalPickup: true, isPacked: true), OrderWorkflowAction.MarkPickedUp).Allowed);
        Assert.False(OrderWorkflow
            .Evaluate(Paid(isLocalPickup: true), OrderWorkflowAction.MarkPickedUp).Allowed);
        Assert.False(OrderWorkflow
            .Evaluate(Paid(isPacked: true), OrderWorkflowAction.MarkPickedUp).Allowed);
    }

    // ── Cancellation ─────────────────────────────────────────────────

    [Fact]
    public void An_unshipped_order_can_be_cancelled()
        => Assert.True(OrderWorkflow.Evaluate(Paid(), OrderWorkflowAction.Cancel).Allowed);

    [Fact]
    public void A_shipped_order_must_be_returned_rather_than_cancelled()
    {
        OrderTransition transition = OrderWorkflow.Evaluate(
            Paid(isPacked: true, isShipped: true), OrderWorkflowAction.Cancel);

        Assert.False(transition.Allowed);
        Assert.Contains(transition.Blockers, blocker => blocker.Contains("return", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_already_cancelled_order_cannot_be_cancelled_again()
        => Assert.False(OrderWorkflow
            .Evaluate(Paid(status: OrderStatus.Cancelled), OrderWorkflowAction.Cancel).Allowed);

    [Fact]
    public void A_closed_order_offers_no_fulfillment_actions()
    {
        OrderWorkflowFacts cancelled = Paid(status: OrderStatus.Cancelled);

        Assert.False(OrderWorkflow.Evaluate(cancelled, OrderWorkflowAction.MarkPacked).Allowed);
        Assert.False(OrderWorkflow.Evaluate(cancelled, OrderWorkflowAction.Ship).Allowed);
        Assert.False(OrderWorkflow.Evaluate(cancelled, OrderWorkflowAction.MarkDelivered).Allowed);
        Assert.Empty(OrderWorkflow.AvailableActions(cancelled));
    }

    // ── Payment ──────────────────────────────────────────────────────

    [Fact]
    public void Payment_can_be_recorded_once_and_not_twice()
    {
        Assert.True(OrderWorkflow.Evaluate(
            Paid(status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending),
            OrderWorkflowAction.RecordPayment).Allowed);
        Assert.False(OrderWorkflow.Evaluate(Paid(), OrderWorkflowAction.RecordPayment).Allowed);
    }

    // ── Offered actions + queue membership ───────────────────────────

    [Fact]
    public void A_paid_unpacked_order_offers_exactly_pack_and_cancel()
    {
        IReadOnlyList<OrderWorkflowAction> actions = OrderWorkflow.AvailableActions(Paid());

        Assert.Equal(
            [OrderWorkflowAction.MarkPacked, OrderWorkflowAction.Cancel],
            actions.OrderBy(action => action).ToArray());
    }

    [Fact]
    public void Every_blocked_transition_explains_itself()
    {
        // A surface must always be able to tell the operator why something is unavailable.
        OrderWorkflowFacts[] states =
        [
            Paid(status: OrderStatus.Pending, paymentStatus: PaymentStatus.Pending),
            Paid(),
            Paid(isPacked: true),
            Paid(isLocalPickup: true, isPacked: true),
            Paid(isPacked: true, isShipped: true),
            Paid(isPacked: true, isShipped: true, isDelivered: true),
            Paid(status: OrderStatus.Cancelled),
            Paid(hasOpenReturn: true),
        ];

        foreach (OrderWorkflowFacts facts in states)
        {
            foreach (OrderWorkflowAction action in Enum.GetValues<OrderWorkflowAction>())
            {
                OrderTransition transition = OrderWorkflow.Evaluate(facts, action);
                if (!transition.Allowed)
                {
                    Assert.NotEmpty(transition.Blockers);
                    Assert.All(transition.Blockers, blocker => Assert.False(string.IsNullOrWhiteSpace(blocker)));
                }
            }
        }
    }

    [Theory]
    [InlineData(OrderWorkflowStage.AwaitingPayment, true)]
    [InlineData(OrderWorkflowStage.ToPack, true)]
    [InlineData(OrderWorkflowStage.ToShip, true)]
    [InlineData(OrderWorkflowStage.ReadyForPickup, true)]
    [InlineData(OrderWorkflowStage.NeedsAttention, true)]
    [InlineData(OrderWorkflowStage.InTransit, false)]
    [InlineData(OrderWorkflowStage.Delivered, false)]
    [InlineData(OrderWorkflowStage.Closed, false)]
    public void Open_work_is_what_the_shift_board_counts(OrderWorkflowStage stage, bool expected)
        => Assert.Equal(expected, OrderWorkflow.IsOpenWork(stage));
}
