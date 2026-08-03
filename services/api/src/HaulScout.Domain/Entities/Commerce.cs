using HaulScout.Domain.Common;

namespace HaulScout.Domain.Entities;

// ──────────────────────────────────────────────────────────────────────────
// Customers — storefront shoppers. Deliberately separate from AppUser (operators)
// so a public-signup customer identity can never resolve to operator scope.
// ──────────────────────────────────────────────────────────────────────────
public sealed class Customer : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>External auth subject from the storefront (customer) Supabase project, e.g. "supabase:&lt;uuid&gt;".</summary>
    public string? ExternalSubject { get; set; }

    /// <summary>Nullable: a marketplace buyer is identified by their channel username, not an email address.
    /// See <see cref="ChannelIdentities"/>.</summary>
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public bool AcceptsMarketing { get; set; }

    /// <summary>Store credit the customer can spend (refunds, loyalty, goodwill). A liability until used.</summary>
    public decimal StoreCreditBalance { get; set; }

    public ICollection<CustomerAddress> Addresses { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<WishlistItem> WishlistItems { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
    public ICollection<CustomerChannelIdentity> ChannelIdentities { get; set; } = [];
}

/// <summary>
/// How one customer is known on one channel. A storefront shopper has an email; an eBay or Depop buyer has only a
/// username. This is what lets "Customers" mean every buyer across every platform rather than storefront accounts
/// only — and what lets the same human be recognised on two channels and merged.
/// </summary>
public sealed class CustomerChannelIdentity : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>SalesChannelCatalog value: "Storefront", "eBay", "Poshmark"…</summary>
    public required string Channel { get; set; }

    /// <summary>The buyer's id/handle on that channel. Unique per (org, channel).</summary>
    public required string ExternalUserId { get; set; }

    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CustomerAddress : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? Label { get; set; }
    public required string RecipientName { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public required string Region { get; set; }
    public required string PostalCode { get; set; }
    public string Country { get; set; } = "US";
    public string? Phone { get; set; }
    public bool IsDefault { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// Storefront catalog — StoreListing is a published PROJECTION of an InventoryItem,
// not a duplicate product record. One listing per inventory item.
// ──────────────────────────────────────────────────────────────────────────
public sealed class StoreCategory : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? Description { get; set; }
    public string? ImageKey { get; set; }
    public Guid? ParentId { get; set; }
    public StoreCategory? Parent { get; set; }
    public ICollection<StoreCategory> Children { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<StoreListing> Listings { get; set; } = [];
}

public sealed class StoreListing : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? StoreCategoryId { get; set; }
    public StoreCategory? StoreCategory { get; set; }

    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }

    public decimal ListPrice { get; set; }
    public decimal? CompareAtPrice { get; set; }

    public bool AcceptsOffers { get; set; } = true;
    public decimal? MinimumOfferAmount { get; set; }
    public decimal? MaximumOfferAmount { get; set; }
    public StoreShippingPolicy ShippingPolicy { get; set; } = StoreShippingPolicy.StoreDefault;
    public decimal? ShippingAmountOverride { get; set; }

    public StoreListingStatus Status { get; set; } = StoreListingStatus.Draft;
    public bool IsFeatured { get; set; }

    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public int ViewCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public ICollection<Review> Reviews { get; set; } = [];
}

// ──────────────────────────────────────────────────────────────────────────
// Cart
// ──────────────────────────────────────────────────────────────────────────
public sealed class Cart : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>Cookie token for guest carts; merged into the customer cart on login.</summary>
    public string? AnonymousId { get; set; }
    public CartStatus Status { get; set; } = CartStatus.Active;
    public ICollection<CartLine> Lines { get; set; } = [];
}

public sealed class CartLine : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid CartId { get; set; }
    public Cart? Cart { get; set; }
    public Guid StoreListingId { get; set; }
    public StoreListing? StoreListing { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid? StorefrontOfferId { get; set; }
    public StorefrontOffer? StorefrontOffer { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPriceSnapshot { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// Orders. OrderLine carries a specific-identification COGS snapshot so finance
// reporting can compute true gross profit per line at the time of sale.
// ──────────────────────────────────────────────────────────────────────────
public sealed class Order : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    /// <summary>The channel this order came from — a SalesChannelCatalog value ("Storefront", "eBay",
    /// "Poshmark"…). The storefront is simply channel #1; there is no storefront-only order type.</summary>
    public string Channel { get; set; } = "Storefront";

    public Guid? ChannelAccountId { get; set; }
    public ChannelAccount? ChannelAccount { get; set; }

    /// <summary>The marketplace's own order id. Null for storefront orders, where <see cref="OrderNumber"/> is ours.</summary>
    public string? ExternalOrderId { get; set; }
    public string? ExternalOrderUrl { get; set; }

    /// <summary>The buyer's handle on the channel (eBay / Depop / Poshmark username). Marketplaces rarely
    /// expose a buyer email, so this — not <see cref="Email"/> — is the durable buyer key off-storefront.</summary>
    public string? BuyerReference { get; set; }

    public required string OrderNumber { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Unfulfilled;

    /// <summary>Storefront orders always carry one; marketplace buyers usually do not.</summary>
    public string? Email { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal ShippingTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string Currency { get; set; } = "USD";

    public string? StripeCheckoutSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }

    /// <summary>The cart this order was checked out from. Lets a shopper who abandons the payment page resume
    /// their own pending checkout instead of being blocked by their own reservation.</summary>
    public Guid? SourceCartId { get; set; }

    public string? ShipToName { get; set; }
    public string? ShipToLine1 { get; set; }
    public string? ShipToLine2 { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToRegion { get; set; }
    public string? ShipToPostalCode { get; set; }
    public string? ShipToCountry { get; set; }
    public bool IsLocalPickup { get; set; }
    public string? CustomerNote { get; set; }
    public string? ShippingCarrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }

    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>When the order was packed and ready to hand over. Drives the fulfillment stage.</summary>
    public DateTimeOffset? PackedAt { get; set; }

    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public ICollection<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public Guid? StoreListingId { get; set; }
    public Guid? StorefrontOfferId { get; set; }
    public StorefrontOffer? StorefrontOffer { get; set; }

    public required string TitleSnapshot { get; set; }
    public string? SkuSnapshot { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineTotal { get; set; }

    /// <summary>Acquisition cost of the item at time of sale (specific-identification COGS).</summary>
    public decimal? UnitCostSnapshot { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────
// Engagement
// ──────────────────────────────────────────────────────────────────────────
public sealed class WishlistItem : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid StoreListingId { get; set; }
    public StoreListing? StoreListing { get; set; }
}

public sealed class Review : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid StoreListingId { get; set; }
    public StoreListing? StoreListing { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
}

/// <summary>
/// A buyer-submitted offer on a storefront listing. This is not an order and does not reserve inventory; it is an
/// auditable lead the operator can accept, decline, or convert later.
/// </summary>
public sealed class StorefrontOffer : EntityBase, IOrganizationScoped
{
    public Guid OrganizationId { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid StoreListingId { get; set; }
    public StoreListing? StoreListing { get; set; }
    public Guid InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }
    public StorefrontOfferStatus Status { get; set; } = StorefrontOfferStatus.New;
    public decimal OfferAmount { get; set; }
    public decimal ListingPriceSnapshot { get; set; }
    public decimal? ResponseAmount { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerPhone { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset OfferedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RespondedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    /// <summary>Buyer-visible response text. Internal notes belong in audit/activity later, not here.</summary>
    public string? ResponseMessage { get; set; }
    public string? ResponseNotes { get; set; }
}
