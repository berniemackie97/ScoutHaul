using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Persistence;
using HaulScout.Infrastructure.Storefront;

namespace HaulScout.Tests;

/// <summary>
/// The storefront purchase loop as pure rules — no database, no HTTP, no Stripe. The fakes implement the ports
/// honestly (all-or-nothing reservation, listing/item state) so these tests exercise the same decisions production
/// makes: who wins a one-of-one race, what a webhook may finalize, and where money is refused.
/// </summary>
public class StorefrontCheckoutTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid();

    // ── fakes ──────────────────────────────────────────────────────────────

    private sealed class World
    {
        public List<StoreListing> Listings { get; } = [];
        public List<Cart> Carts { get; } = [];
        public List<Order> Orders { get; } = [];
        public List<Customer> Customers { get; } = [];
        public List<Sale> Sales { get; } = [];

        public StoreListing NewListing(string title, decimal price, decimal cogs, InventoryStatus itemStatus = InventoryStatus.Listed)
        {
            InventoryItem item = new() { OrganizationId = Org, Title = title, PurchasePrice = cogs, Status = itemStatus };
            StoreListing listing = new()
            {
                OrganizationId = Org,
                InventoryItemId = item.Id,
                InventoryItem = item,
                Slug = title.ToLowerInvariant().Replace(' ', '-'),
                Title = title,
                ListPrice = price,
                Status = StoreListingStatus.Active,
            };
            Listings.Add(listing);
            return listing;
        }
    }

    private sealed class FakeCartStore(World world) : IStorefrontCartStore
    {
        public Task<Cart?> FindActiveAsync(Guid organizationId, string anonymousToken, CancellationToken cancellationToken) =>
            Task.FromResult(Hydrate(world.Carts.FirstOrDefault(cart =>
                cart.AnonymousId == anonymousToken && cart.OrganizationId == organizationId && cart.Status == CartStatus.Active)));

        public Task<Cart?> FindActiveReadAsync(Guid organizationId, string anonymousToken, CancellationToken cancellationToken) =>
            FindActiveAsync(organizationId, anonymousToken, cancellationToken);

        public Task<Cart?> FindActiveByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Carts.FirstOrDefault(cart => cart.Id == cartId && cart.Status == CartStatus.Active));

        public Task AddAsync(Cart cart, CancellationToken cancellationToken)
        {
            world.Carts.Add(cart);
            return Task.CompletedTask;
        }

        public void AddLine(Cart cart, CartLine line) => cart.Lines.Add(line);

        public void RemoveLine(CartLine line) { }

        public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Listings.FirstOrDefault(listing => listing.Id == storeListingId));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private Cart? Hydrate(Cart? cart)
        {
            if (cart is null) return null;
            foreach (CartLine line in cart.Lines)
            {
                line.StoreListing ??= world.Listings.FirstOrDefault(listing => listing.Id == line.StoreListingId);
            }

            return cart;
        }
    }

    private sealed class FakeCheckoutStore(World world) : IStorefrontCheckoutStore
    {
        public Task<IReadOnlyList<Guid>> TryReserveAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken)
        {
            List<StoreListing> wanted = world.Listings.Where(listing => storeListingIds.Contains(listing.Id)).ToList();
            List<Guid> losers = storeListingIds
                .Where(id => wanted.FirstOrDefault(listing => listing.Id == id) is not StoreListing candidate
                    || candidate.Status != StoreListingStatus.Active
                    || !StorefrontPurchasability.IsItemSellable(candidate.InventoryItem))
                .ToList();
            if (losers.Count == 0)
            {
                wanted.ForEach(listing => listing.Status = StoreListingStatus.Reserved);   // all-or-nothing, like the SQL
            }
            return Task.FromResult<IReadOnlyList<Guid>>(losers);
        }

        public Task ReleaseAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken)
        {
            foreach (StoreListing listing in world.Listings.Where(listing =>
                storeListingIds.Contains(listing.Id) && listing.Status == StoreListingStatus.Reserved))
            {
                listing.Status = StoreListingStatus.Active;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> MarkSoldAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken)
        {
            List<Guid> notReserved = storeListingIds
                .Where(id => world.Listings.FirstOrDefault(listing => listing.Id == id)?.Status != StoreListingStatus.Reserved)
                .ToList();
            if (notReserved.Count == 0)
            {
                foreach (StoreListing listing in world.Listings.Where(listing => storeListingIds.Contains(listing.Id)))
                {
                    listing.Status = StoreListingStatus.Sold;
                }
            }
            return Task.FromResult<IReadOnlyList<Guid>>(notReserved);
        }

        public Task AddOrderAsync(Order order, CancellationToken cancellationToken)
        {
            world.Orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<Order?> FindPendingOrderByCartAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Orders.FirstOrDefault(order => order.SourceCartId == cartId && order.Status == OrderStatus.Pending));

        public Task<Order?> FindOrderBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Orders.FirstOrDefault(order => order.StripeCheckoutSessionId == sessionId));

        public Task<Order?> FindOrderForOrphanedSessionAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Orders.FirstOrDefault(
                order => order.Id == orderId && order.StripeCheckoutSessionId is null));

        public Task<Order?> FindOrderSummaryBySessionAsync(Guid organizationId, string sessionId, CancellationToken cancellationToken) =>
            FindOrderBySessionAsync(sessionId, cancellationToken);

        public Task<Customer?> FindCustomerByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
            Task.FromResult(world.Customers.FirstOrDefault(customer =>
                string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<Customer?> FindCustomerByIdAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Customers.FirstOrDefault(customer =>
                customer.OrganizationId == organizationId && customer.Id == customerId));

        public Task AddCustomerAsync(Customer customer, CancellationToken cancellationToken)
        {
            world.Customers.Add(customer);
            return Task.CompletedTask;
        }

        public Task AddSaleAsync(Sale sale, CancellationToken cancellationToken)
        {
            world.Sales.Add(sale);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InventoryItem>> ListItemsForListingsAsync(Guid organizationId, IReadOnlyCollection<Guid> storeListingIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>(world.Listings
                .Where(listing => storeListingIds.Contains(listing.Id) && listing.InventoryItem is not null)
                .Select(listing => listing.InventoryItem!)
                .ToList());

        public Task<Cart?> FindCartByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Carts.FirstOrDefault(cart => cart.Id == cartId));

        public Task<string?> GetStoreDomainAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("overhaultrading.com");

        public Task MarkOffersConvertedAsync(
            Guid organizationId,
            IReadOnlyCollection<Guid> storefrontOfferIds,
            DateTimeOffset convertedAt,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCustomerStore(World world) : IStorefrontCustomerStore
    {
        public Task<Customer> GetOrCreateAsync(
            Guid organizationId,
            StorefrontCustomerIdentity identity,
            DateTimeOffset seenAt,
            CancellationToken cancellationToken)
        {
            Customer? customer = world.Customers.FirstOrDefault(item =>
                item.OrganizationId == organizationId && item.ExternalSubject == identity.ExternalSubject);
            if (customer is null)
            {
                customer = new Customer
                {
                    OrganizationId = organizationId,
                    ExternalSubject = identity.ExternalSubject,
                    Email = identity.Email,
                    DisplayName = identity.DisplayName,
                };
                world.Customers.Add(customer);
            }

            return Task.FromResult(customer);
        }
    }

    private sealed class FakeOfferStore : IStorefrontOfferStore
    {
        public Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
            Guid organizationId,
            StorefrontOfferStatus? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StorefrontOffer>>([]);

        public Task<int> CountOffersAsync(Guid organizationId, StorefrontOfferStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<StorefrontOffer?> FindOfferAsync(Guid organizationId, Guid offerId, CancellationToken cancellationToken) =>
            Task.FromResult<StorefrontOffer?>(null);

        public Task<IReadOnlyList<StorefrontOffer>> ListCustomerOffersAsync(
            Guid organizationId,
            Guid customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StorefrontOffer>>([]);

        public Task<StorefrontOffer?> FindCustomerOfferAsync(
            Guid organizationId,
            Guid customerId,
            Guid offerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StorefrontOffer?>(null);

        public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
            Task.FromResult<StoreListing?>(null);

        public Task AddOfferAsync(StorefrontOffer offer, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeGateway : IPaymentGateway
    {
        public List<HostedCheckoutRequest> Created { get; } = [];
        public List<string> Refunded { get; } = [];
        public string? OpenUrl { get; set; }
        public long? FeeCents { get; set; } = 350;

        public Task<HostedCheckout> CreateCheckoutAsync(HostedCheckoutRequest request, CancellationToken cancellationToken)
        {
            Created.Add(request);
            return Task.FromResult(new HostedCheckout($"cs_test_{Created.Count}", $"https://checkout.stripe.test/{Created.Count}", null, null));
        }

        public Task<HostedCheckout?> FindOpenCheckoutAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(OpenUrl is null ? null : new HostedCheckout(sessionId, OpenUrl, null, null));

        public Task<PaymentCapture> GetCaptureAsync(string paymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(new PaymentCapture(paymentIntentId, 0, FeeCents));

        public Task RefundAsync(string paymentIntentId, string reason, CancellationToken cancellationToken)
        {
            Refunded.Add(paymentIntentId);
            return Task.CompletedTask;
        }

        public Task<PaymentEvent> ParseEventAsync(string payload, string signatureHeader, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Unit tests hand events to the service directly.");
    }

    // ── harness ────────────────────────────────────────────────────────────

    private static IOptions<StorefrontCheckoutOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new StorefrontCheckoutOptions { FlatShippingAmount = 9.95m, FreeShippingThreshold = 150m });

    private static (World World, CartWorkflowService Cart, CheckoutWorkflowService Checkout, StorefrontOrderService Orders, FakeGateway Gateway, FakeTimeProvider Clock) NewHarness()
    {
        World world = new();
        FakeGateway gateway = new();
        FakeTimeProvider clock = new(T0);
        FakeCheckoutStore checkoutStore = new(world);
        FakeCartStore cartStore = new(world);
        IOptions<StorefrontCheckoutOptions> options = Options();
        StorefrontShippingCalculator shipping = new(options);
        return (
            world,
            new CartWorkflowService(cartStore, checkoutStore, new FakeCustomerStore(world), new FakeOfferStore(), shipping, options, clock),
            new CheckoutWorkflowService(cartStore, checkoutStore, gateway, shipping, options, clock),
            new StorefrontOrderService(checkoutStore, gateway, clock),
            gateway,
            clock);
    }

    private static async Task<(string Token, StoreListing Listing)> AddToCart(
        (World World, CartWorkflowService Cart, CheckoutWorkflowService Checkout, StorefrontOrderService Orders, FakeGateway Gateway, FakeTimeProvider Clock) harness,
        string title = "Brass Lamp",
        decimal price = 100m,
        decimal cogs = 40m)
    {
        StoreListing listing = harness.World.NewListing(title, price, cogs);
        AppResult<StorefrontCartDto> added = await harness.Cart.AddLineAsync(Org, null, new AddCartLineRequest(listing.Id), CancellationToken.None);
        return (added.Value!.CartToken, listing);
    }

    private static PaymentEvent Completed(Order order, long subtotalCents, long taxCents = 0, long shippingCents = 0, bool pickup = false) =>
        new(PaymentEventKind.CheckoutCompleted, order.StripeCheckoutSessionId!, order.Id, "pi_1",
            subtotalCents + taxCents + shippingCents, subtotalCents, taxCents, shippingCents,
            "buyer@example.com", "Buyer Example",
            new ShipTo("Buyer Example", "1 Main St", null, "Louisville", "KY", "40202", "US"), pickup);

    // ── cart ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Adding_an_item_mints_a_cart_with_an_unguessable_token()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);

        Assert.Equal(64, token.Length);   // 32 random bytes, hex
        Assert.Single(harness.World.Carts);
        Assert.Equal(CartStatus.Active, harness.World.Carts[0].Status);
    }

    [Fact]
    public async Task Reusing_an_emptied_persisted_cart_persists_and_returns_the_next_line()
    {
        Guid org = Guid.NewGuid();
        DbContextOptions<HaulScoutDbContext> dbOptions = new DbContextOptionsBuilder<HaulScoutDbContext>()
            .UseInMemoryDatabase($"{nameof(Reusing_an_emptied_persisted_cart_persists_and_returns_the_next_line)}-{Guid.NewGuid()}")
            .Options;

        TenantContext tenant = new();
        tenant.SetOrganization(org);
        await using HaulScoutDbContext db = new(dbOptions, tenant);

        InventoryItem first = SellableItem("First item", 55m);
        InventoryItem second = SellableItem("Second item", 2m);
        db.Organizations.Add(new Organization { Id = org, Name = "Overhaul Trading" });
        db.InventoryItems.AddRange(first, second);
        await db.SaveChangesAsync();

        Dictionary<Guid, StoreListing> listings = await db.StoreListings.ToDictionaryAsync(listing => listing.InventoryItemId);
        StoreListing firstListing = listings[first.Id];
        StoreListing secondListing = listings[second.Id];
        CartWorkflowService service = new(
            new EfStorefrontCartStore(db),
            new FakeCheckoutStore(new World()),
            new FakeCustomerStore(new World()),
            new FakeOfferStore(),
            new StorefrontShippingCalculator(Options()),
            Options(),
            TimeProvider.System);

        AppResult<StorefrontCartDto> addedFirst =
            await service.AddLineAsync(org, null, new AddCartLineRequest(firstListing.Id), CancellationToken.None);
        string token = addedFirst.Value!.CartToken;
        await service.RemoveLineAsync(org, token, firstListing.Id, CancellationToken.None);

        AppResult<StorefrontCartDto> addedSecond =
            await service.AddLineAsync(org, token, new AddCartLineRequest(secondListing.Id), CancellationToken.None);

        StorefrontCartLineDto line = Assert.Single(addedSecond.Value!.Lines);
        Assert.Equal(secondListing.Id, line.StoreListingId);
        Assert.Equal(secondListing.Slug, line.Slug);
        Assert.Equal("Second item", line.Title);
        Assert.True(line.IsAvailable);
        Assert.Equal(2m, addedSecond.Value.Subtotal);
        Guid cartId = db.Carts.Single(cart => cart.AnonymousId == token).Id;
        Assert.True(await db.CartLines.AnyAsync(saved => saved.CartId == cartId && saved.StoreListingId == secondListing.Id));
    }

    [Fact]
    public async Task A_reserved_listing_cannot_be_added_and_says_why()
    {
        var harness = NewHarness();
        StoreListing listing = harness.World.NewListing("Walnut Desk", 200m, 80m);
        listing.Status = StoreListingStatus.Reserved;

        AppResult<StorefrontCartDto> result = await harness.Cart.AddLineAsync(Org, null, new AddCartLineRequest(listing.Id), CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Contains("someone else's checkout", result.Message);
    }

    private static InventoryItem SellableItem(string title, decimal price) =>
        new()
        {
            Title = title,
            Status = InventoryStatus.Listed,
            EstimatedSalePrice = price,
            Quantity = 1,
            Photos = { new InventoryPhoto { ContentType = "image/jpeg", OriginalBytes = [1], ThumbnailBytes = [1] } },
        };

    [Fact]
    public async Task An_item_sold_on_another_channel_is_not_purchasable_even_while_its_listing_says_active()
    {
        var harness = NewHarness();
        StoreListing listing = harness.World.NewListing("Copper Kettle", 60m, 20m, itemStatus: InventoryStatus.Sold);

        AppResult<StorefrontCartDto> result = await harness.Cart.AddLineAsync(Org, null, new AddCartLineRequest(listing.Id), CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Contains("no longer available", result.Message);
    }

    // ── orphaned session (the session id never got written) ────────────────

    /// <summary>
    /// Checkout saves the order, creates the session at the processor, then saves again to attach the session
    /// id. If that last save fails the processor holds a live, payable session that no session-id lookup can
    /// find — and the completed event was being dismissed as "not ours" and acked, so the shopper paid and
    /// nothing was ever fulfilled. The processor also carries the order id, which is how these recover.
    /// </summary>
    [Fact]
    public async Task Payment_for_an_order_whose_session_id_was_never_saved_still_finalizes()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Order order = Assert.Single(harness.World.Orders);
        string sessionId = order.StripeCheckoutSessionId!;
        order.StripeCheckoutSessionId = null;   // the second save never landed

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(
            Completed(order, 10_000) with { SessionId = sessionId },
            CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.Finalized, outcome);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(StoreListingStatus.Sold, listing.Status);
        // Written down now, so every later event for this session matches the ordinary way.
        Assert.Equal(sessionId, order.StripeCheckoutSessionId);
    }

    [Fact]
    public async Task Recovered_order_is_still_finalized_only_once()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Order order = Assert.Single(harness.World.Orders);
        string sessionId = order.StripeCheckoutSessionId!;
        order.StripeCheckoutSessionId = null;
        PaymentEvent completed = Completed(order, 10_000) with { SessionId = sessionId };

        Assert.Equal(PaymentEventOutcome.Finalized, await harness.Orders.HandleAsync(completed, CancellationToken.None));
        // Stripe redelivers for days; the recovery path must not open a second door to double-finalizing.
        Assert.Equal(PaymentEventOutcome.AlreadyProcessed, await harness.Orders.HandleAsync(completed, CancellationToken.None));
    }

    /// <summary>
    /// A second checkout of the same cart reuses the order and overwrites the session id. The superseded
    /// session then expires, and that stale event must not be adopted — cancelling the order the shopper is
    /// paying for right now.
    /// </summary>
    [Fact]
    public async Task A_superseded_session_event_is_not_adopted_by_the_order_it_belonged_to()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Order order = Assert.Single(harness.World.Orders);
        const string supersededSession = "cs_test_superseded";

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(
            new PaymentEvent(PaymentEventKind.CheckoutExpired, supersededSession, order.Id, null,
                null, null, null, null, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.Ignored, outcome);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(StoreListingStatus.Reserved, listing.Status);
        Assert.Equal("cs_test_1", order.StripeCheckoutSessionId);
    }

    [Fact]
    public async Task An_event_for_an_order_this_system_does_not_have_is_still_ignored()
    {
        var harness = NewHarness();

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(
            new PaymentEvent(PaymentEventKind.CheckoutExpired, "cs_other_env", Guid.NewGuid(), null,
                null, null, null, null, null, null, null, false),
            CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.Ignored, outcome);
    }

    // ── checkout ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Checkout_reserves_creates_a_pending_order_with_cogs_and_returns_the_payment_url()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);

        AppResult<StorefrontCheckoutResultDto> result = await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        Assert.Equal(StoreListingStatus.Reserved, listing.Status);
        Order order = Assert.Single(harness.World.Orders);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("Storefront", order.Channel);
        Assert.StartsWith("STO-", order.OrderNumber);
        OrderLine line = Assert.Single(order.Lines);
        Assert.Equal(40m, line.UnitCostSnapshot);   // specific-identification COGS frozen at checkout
        Assert.Equal("cs_test_1", order.StripeCheckoutSessionId);
        Assert.Equal(100m, order.Subtotal);
    }

    [Fact]
    public async Task Cart_read_treats_this_carts_pending_checkout_hold_as_available()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        StorefrontCartDto cart = await harness.Cart.GetAsync(Org, token, CancellationToken.None);

        StorefrontCartLineDto line = Assert.Single(cart.Lines);
        Assert.True(line.IsAvailable);
        Assert.Equal(100m, cart.Subtotal);
    }

    [Fact]
    public async Task Losing_the_reservation_race_names_the_lost_items_and_creates_no_order()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        listing.Status = StoreListingStatus.Reserved;   // someone else got there first

        AppResult<StorefrontCheckoutResultDto> result = await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Contains("Brass Lamp", result.Message);
        Assert.Empty(harness.World.Orders);
    }

    [Fact]
    public async Task Below_the_threshold_the_flat_rate_is_offered_and_above_it_shipping_is_free()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness, price: 100m);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Assert.Equal(995, harness.Gateway.Created[0].Shipping.AmountCents);

        (string bigToken, _) = await AddToCart(harness, title: "Grandfather Clock", price: 400m, cogs: 150m);
        await harness.Checkout.CheckoutAsync(Org, bigToken, CancellationToken.None);
        Assert.Equal(0, harness.Gateway.Created[1].Shipping.AmountCents);
        Assert.Equal("Free shipping", harness.Gateway.Created[1].Shipping.Label);
    }

    [Fact]
    public async Task Listing_shipping_policy_overrides_the_global_flat_rate_for_single_item_checkout()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness, price: 100m);
        listing.ShippingPolicy = StoreShippingPolicy.FixedAmount;
        listing.ShippingAmountOverride = 4.50m;

        StorefrontCartDto cart = await harness.Cart.GetAsync(Org, token, CancellationToken.None);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);

        Assert.Equal(4.50m, cart.EstimatedShippingAmount);
        Assert.Equal("$4.50 before tax", cart.ShippingSummary);
        Assert.Equal(450, harness.Gateway.Created[0].Shipping.AmountCents);
    }

    // ── webhook: the money paths ───────────────────────────────────────────

    [Fact]
    public async Task A_completed_payment_finalizes_everything_it_touches()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(
            Completed(order, subtotalCents: 10000, taxCents: 600, shippingCents: 995), CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.Finalized, outcome);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(T0, order.PaidAt);
        Assert.Equal(6.00m, order.TaxTotal);
        Assert.Equal(9.95m, order.ShippingTotal);
        Assert.Equal(115.95m, order.GrandTotal);
        Assert.Equal("Louisville", order.ShipToCity);
        Assert.Equal(StoreListingStatus.Sold, listing.Status);
        Assert.Equal(InventoryStatus.Sold, listing.InventoryItem!.Status);
        Assert.Equal(CartStatus.Converted, harness.World.Carts[0].Status);
        Customer customer = Assert.Single(harness.World.Customers);
        Assert.Equal("buyer@example.com", customer.Email);
        Assert.Equal(customer.Id, order.CustomerId);
    }

    [Fact]
    public async Task The_sale_it_records_carries_true_financials()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);   // $100 item, $40 COGS
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];

        await harness.Orders.HandleAsync(Completed(order, 10000, taxCents: 600, shippingCents: 995), CancellationToken.None);

        Sale sale = Assert.Single(harness.World.Sales);
        Assert.Equal("Storefront", sale.Platform);
        Assert.Equal(100m, sale.SoldPrice);
        Assert.Equal(9.95m, sale.ShippingCharged);
        Assert.Equal(40m, sale.CostOfGoods);
        Assert.Equal(3.50m, sale.PaymentFees);          // the processor's real fee, from the capture
        Assert.Equal(6.00m, sale.SalesTaxCollected);
        Assert.False(sale.TaxRemittedByPlatform);       // Stripe Tax collects under OUR registration — we owe it
        Assert.Equal(order.Id, sale.OrderId);
        // 100 + 9.95 revenue − 3.50 fee − 40 COGS; tax never nets.
        Assert.Equal(66.45m, sale.NetProfit);
    }

    [Fact]
    public async Task Redelivery_is_a_no_op()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];
        PaymentEvent paymentEvent = Completed(order, 10000);

        await harness.Orders.HandleAsync(paymentEvent, CancellationToken.None);
        PaymentEventOutcome second = await harness.Orders.HandleAsync(paymentEvent, CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.AlreadyProcessed, second);
        Assert.Single(harness.World.Sales);   // not booked twice
    }

    [Fact]
    public async Task A_payment_whose_subtotal_disagrees_with_the_order_is_never_finalized()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(Completed(order, subtotalCents: 9000), CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.AmountMismatch, outcome);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(StoreListingStatus.Reserved, listing.Status);   // untouched, awaiting a human
        Assert.Empty(harness.World.Sales);
    }

    [Fact]
    public async Task An_expired_session_releases_the_items_and_cancels_the_order()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];

        PaymentEvent expired = new(PaymentEventKind.CheckoutExpired, order.StripeCheckoutSessionId!, order.Id,
            null, null, null, null, null, null, null, null, false);
        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(expired, CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.Released, outcome);
        Assert.Equal(StoreListingStatus.Active, listing.Status);   // back on the shelf
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentEventOutcome.AlreadyProcessed, await harness.Orders.HandleAsync(expired, CancellationToken.None));
    }

    [Fact]
    public async Task Payment_for_an_item_that_left_the_shelf_mid_window_is_refunded_not_double_sold()
    {
        var harness = NewHarness();
        (string token, StoreListing listing) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];
        // While the shopper was on the payment page, the operator sold the physical item on eBay.
        listing.InventoryItem!.Status = InventoryStatus.Sold;

        PaymentEventOutcome outcome = await harness.Orders.HandleAsync(Completed(order, 10000), CancellationToken.None);

        Assert.Equal(PaymentEventOutcome.RefundedConflict, outcome);
        Assert.Equal(["pi_1"], harness.Gateway.Refunded);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Refunded, order.PaymentStatus);
        Assert.Empty(harness.World.Sales);   // one physical item, one sale — the eBay one
    }

    [Fact]
    public async Task Local_pickup_lands_on_the_order()
    {
        var harness = NewHarness();
        (string token, _) = await AddToCart(harness);
        await harness.Checkout.CheckoutAsync(Org, token, CancellationToken.None);
        Order order = harness.World.Orders[0];

        await harness.Orders.HandleAsync(Completed(order, 10000, pickup: true), CancellationToken.None);

        Assert.True(order.IsLocalPickup);
    }

    // ── allocation arithmetic ──────────────────────────────────────────────

    [Theory]
    [InlineData(9.95, new[] { 100.0, 50.0, 25.0 })]
    [InlineData(0.01, new[] { 10.0, 10.0, 10.0 })]
    [InlineData(6.00, new[] { 33.33, 33.33, 33.34 })]
    public void Allocation_is_cent_exact_no_matter_how_awkward_the_split(double totalRaw, double[] weightsRaw)
    {
        decimal total = (decimal)totalRaw;
        decimal[] weights = weightsRaw.Select(weight => (decimal)weight).ToArray();

        decimal[] parts = StorefrontOrderService.Allocate(total, weights);

        Assert.Equal(total, parts.Sum());   // every cent lands exactly once
        Assert.All(parts, part => Assert.Equal(Math.Round(part, 2), part));
    }
}
