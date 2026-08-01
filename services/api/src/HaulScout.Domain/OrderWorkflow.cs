namespace HaulScout.Domain;

/// <summary>
/// Where an order sits in the day's work. Derived from the order's recorded facts — never stored.
/// A stored workflow column would be a second source of truth and would drift from payment and
/// fulfillment reality the first time an import, a webhook, or a refund landed behind its back.
/// </summary>
public enum OrderWorkflowStage
{
    /// <summary>Money not in. Nothing may be picked, packed, or shipped.</summary>
    AwaitingPayment,

    /// <summary>Paid and waiting to be packed — the goods work starts here.</summary>
    ToPack,

    /// <summary>Packed and waiting for a carrier handover; needs carrier + tracking to move on.</summary>
    ToShip,

    /// <summary>Packed local-pickup order waiting for the buyer to collect it.</summary>
    ReadyForPickup,

    /// <summary>Handed to the carrier, not yet delivered.</summary>
    InTransit,

    /// <summary>Delivered or collected. Done unless something comes back.</summary>
    Delivered,

    /// <summary>A return or refund is open — this needs a person, whatever else is true.</summary>
    NeedsAttention,

    /// <summary>Cancelled or fully refunded. No further fulfillment work.</summary>
    Closed,
}

/// <summary>An operator action that moves an order forward. Each maps to one gated transition.</summary>
public enum OrderWorkflowAction
{
    RecordPayment,
    MarkPacked,
    Ship,
    MarkPickedUp,
    MarkDelivered,
    Cancel,
}

/// <summary>
/// The facts a stage decision needs, as plain data. Deliberately not the EF entity: the domain must
/// not depend on which navigation properties a caller happened to load, and a small struct keeps the
/// rules trivially testable.
/// </summary>
/// <param name="HasOpenReturn">A return or refund is in flight on any line of the order.</param>
public readonly record struct OrderWorkflowFacts(
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    bool IsLocalPickup,
    bool IsPacked,
    bool HasCarrier,
    bool HasTrackingNumber,
    bool HasShippingAddress,
    bool IsShipped,
    bool IsDelivered,
    bool HasOpenReturn);

/// <summary>Whether an action may run, and if not, exactly what is missing — in operator language.</summary>
/// <param name="Blockers">Empty when <paramref name="Allowed"/> is true.</param>
public sealed record OrderTransition(bool Allowed, IReadOnlyList<string> Blockers)
{
    public static OrderTransition Ok { get; } = new(true, []);

    public static OrderTransition Blocked(params string[] blockers) => new(false, blockers);
}

/// <summary>
/// The single source of truth for order fulfillment: which stage an order is in, what each stage
/// demands before it can be left, and therefore which actions a surface may offer.
///
/// The rule the whole design rests on: <em>a gate requires exactly what the next stage cannot
/// function without.</em> Shipping cannot function without a carrier and a tracking number, so that
/// is where they are demanded — not asked for politely afterwards and left null forever.
///
/// Every surface (admin, mobile, imports) asks this type rather than re-deriving status logic, which
/// is what previously let the mobile app offer "mark delivered" on an unpaid, unshipped order.
/// </summary>
public static class OrderWorkflow
{
    /// <summary>The stage an order is in, given its recorded facts.</summary>
    public static OrderWorkflowStage StageFor(OrderWorkflowFacts facts)
    {
        // Terminal money states first — a cancelled or fully refunded order has no fulfillment work
        // left, and saying otherwise would put dead rows in someone's queue.
        if (facts.Status is OrderStatus.Cancelled || facts.PaymentStatus is PaymentStatus.Refunded)
        {
            return OrderWorkflowStage.Closed;
        }

        // An open return outranks position: wherever the order got to, a person has to deal with it.
        if (facts.HasOpenReturn)
        {
            return OrderWorkflowStage.NeedsAttention;
        }

        if (facts.IsDelivered)
        {
            return OrderWorkflowStage.Delivered;
        }

        if (facts.IsShipped)
        {
            return OrderWorkflowStage.InTransit;
        }

        if (!IsPaid(facts))
        {
            return OrderWorkflowStage.AwaitingPayment;
        }

        if (!facts.IsPacked)
        {
            return OrderWorkflowStage.ToPack;
        }

        // Packed. A pickup order waits for the buyer; a shipping order waits for the carrier.
        return facts.IsLocalPickup ? OrderWorkflowStage.ReadyForPickup : OrderWorkflowStage.ToShip;
    }

    /// <summary>
    /// Whether <paramref name="action"/> may run against these facts, and what is missing if not.
    /// Surfaces use this both to decide what to offer and to explain what is blocking.
    /// </summary>
    public static OrderTransition Evaluate(OrderWorkflowFacts facts, OrderWorkflowAction action)
    {
        OrderWorkflowStage stage = StageFor(facts);

        if (stage is OrderWorkflowStage.Closed && action is not OrderWorkflowAction.Cancel)
        {
            return OrderTransition.Blocked("This order is cancelled or refunded.");
        }

        return action switch
        {
            OrderWorkflowAction.RecordPayment => IsPaid(facts)
                ? OrderTransition.Blocked("This order is already paid.")
                : OrderTransition.Ok,

            OrderWorkflowAction.MarkPacked => EvaluatePack(facts),
            OrderWorkflowAction.Ship => EvaluateShip(facts),
            OrderWorkflowAction.MarkPickedUp => EvaluatePickedUp(facts),
            OrderWorkflowAction.MarkDelivered => EvaluateDelivered(facts),
            OrderWorkflowAction.Cancel => EvaluateCancel(facts),

            _ => OrderTransition.Blocked("Unknown action."),
        };
    }

    /// <summary>The actions worth offering for these facts, in the order an operator would do them.</summary>
    public static IReadOnlyList<OrderWorkflowAction> AvailableActions(OrderWorkflowFacts facts) =>
        AllActions.Where(action => Evaluate(facts, action).Allowed).ToArray();

    /// <summary>Declared once — <c>Enum.GetValues</c> allocates, and this runs per order in a list.</summary>
    private static readonly OrderWorkflowAction[] AllActions = Enum.GetValues<OrderWorkflowAction>();

    /// <summary>True when the order still has fulfillment work — what the shift board counts.</summary>
    public static bool IsOpenWork(OrderWorkflowStage stage) =>
        stage is OrderWorkflowStage.AwaitingPayment
            or OrderWorkflowStage.ToPack
            or OrderWorkflowStage.ToShip
            or OrderWorkflowStage.ReadyForPickup
            or OrderWorkflowStage.NeedsAttention;

    private static OrderTransition EvaluatePack(OrderWorkflowFacts facts)
    {
        List<string> blockers = [];
        if (!IsPaid(facts))
        {
            blockers.Add("Record payment before packing this order.");
        }

        if (facts.IsShipped)
        {
            blockers.Add("This order has already shipped.");
        }

        // A shipping order with nowhere to send it cannot be packed into a labelled box. Pickup
        // orders are exempt — the buyer is the address.
        if (!facts.IsLocalPickup && !facts.HasShippingAddress)
        {
            blockers.Add("Add a shipping address before packing this order.");
        }

        return Result(blockers);
    }

    /// <summary>
    /// Cancelling is for orders that never went out. Once goods are with the carrier the honest
    /// instrument is a return or a refund — cancelling would silently strand a real parcel.
    /// </summary>
    private static OrderTransition EvaluateCancel(OrderWorkflowFacts facts)
    {
        if (facts.Status is OrderStatus.Cancelled)
        {
            return OrderTransition.Blocked("This order is already cancelled.");
        }

        if (facts.IsDelivered)
        {
            return OrderTransition.Blocked("A delivered order cannot be cancelled — refund or return it instead.");
        }

        return facts.IsShipped
            ? OrderTransition.Blocked("This order has already shipped — open a return instead of cancelling.")
            : OrderTransition.Ok;
    }

    private static OrderTransition EvaluateShip(OrderWorkflowFacts facts)
    {
        if (facts.IsLocalPickup)
        {
            return OrderTransition.Blocked("This is a local-pickup order — mark it picked up instead.");
        }

        List<string> blockers = [];
        if (!IsPaid(facts))
        {
            blockers.Add("Record payment before shipping this order.");
        }

        if (!facts.IsPacked)
        {
            blockers.Add("Pack this order before shipping it.");
        }

        // The gate that makes tracking reliable: it is demanded by the transition, so a shipped
        // order without a way to find the parcel cannot exist.
        if (!facts.HasCarrier)
        {
            blockers.Add("Enter the carrier.");
        }

        if (!facts.HasTrackingNumber)
        {
            blockers.Add("Enter the tracking number.");
        }

        if (facts.IsShipped)
        {
            blockers.Add("This order has already shipped.");
        }

        return Result(blockers);
    }

    private static OrderTransition EvaluatePickedUp(OrderWorkflowFacts facts)
    {
        if (!facts.IsLocalPickup)
        {
            return OrderTransition.Blocked("This order ships — enter tracking and mark it shipped instead.");
        }

        List<string> blockers = [];
        if (!IsPaid(facts))
        {
            blockers.Add("Record payment before releasing this order.");
        }

        if (!facts.IsPacked)
        {
            blockers.Add("Pack this order before handing it over.");
        }

        if (facts.IsDelivered)
        {
            blockers.Add("This order has already been collected.");
        }

        return Result(blockers);
    }

    private static OrderTransition EvaluateDelivered(OrderWorkflowFacts facts)
    {
        List<string> blockers = [];

        // Local pickup is completed by MarkPickedUp, which records delivery itself.
        if (facts.IsLocalPickup)
        {
            return OrderTransition.Blocked("This is a local-pickup order — mark it picked up instead.");
        }

        if (!facts.IsShipped)
        {
            blockers.Add("This order has not shipped yet.");
        }

        if (facts.IsDelivered)
        {
            blockers.Add("This order is already delivered.");
        }

        return Result(blockers);
    }

    /// <summary>Ok when nothing is missing; otherwise the accumulated blockers, in the order checked.</summary>
    private static OrderTransition Result(List<string> blockers) =>
        blockers.Count == 0 ? OrderTransition.Ok : new OrderTransition(false, blockers);

    /// <summary>
    /// Money is in. A partial refund still counts as paid — the buyer kept the goods, so the
    /// fulfillment work is unchanged.
    /// </summary>
    private static bool IsPaid(OrderWorkflowFacts facts) =>
        facts.PaymentStatus is PaymentStatus.Paid or PaymentStatus.PartiallyRefunded;
}
