using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

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
            Task.FromResult(world.Carts.FirstOrDefault(cart =>
                cart.AnonymousId == anonymousToken && cart.OrganizationId == organizationId && cart.Status == CartStatus.Active));

        public Task<Cart?> FindActiveByIdAsync(Guid organizationId, Guid cartId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Carts.FirstOrDefault(cart => cart.Id == cartId && cart.Status == CartStatus.Active));

        public Task AddAsync(Cart cart, CancellationToken cancellationToken)
        {
            world.Carts.Add(cart);
            return Task.CompletedTask;
        }

        public void RemoveLine(CartLine line) { }

        public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
            Task.FromResult(world.Listings.FirstOrDefault(listing => listing.Id == storeListingId));

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

        public Task<Order?> FindOrderSummaryBySessionAsync(Guid organizationId, string sessionId, CancellationToken cancellationToken) =>
            FindOrderBySessionAsync(sessionId, cancellationToken);

        public Task<Customer?> FindCustomerByEmailAsync(Guid organizationId, string email, CancellationToken cancellationToken) =>
            Task.FromResult(world.Customers.FirstOrDefault(customer =>
                string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase)));

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
            return Task.FromResult(new HostedCheckout($"cs_test_{Created.Count}", $"https://checkout.stripe.test/{Created.Count}"));
        }

        public Task<string?> FindOpenCheckoutUrlAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(OpenUrl);

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
        return (
            world,
            new CartWorkflowService(cartStore, checkoutStore, Options()),
            new CheckoutWorkflowService(cartStore, checkoutStore, gateway, Options(), clock),
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
    public async Task A_reserved_listing_cannot_be_added_and_says_why()
    {
        var harness = NewHarness();
        StoreListing listing = harness.World.NewListing("Walnut Desk", 200m, 80m);
        listing.Status = StoreListingStatus.Reserved;

        AppResult<StorefrontCartDto> result = await harness.Cart.AddLineAsync(Org, null, new AddCartLineRequest(listing.Id), CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Contains("someone else's checkout", result.Message);
    }

    [Fact]
    public async Task An_item_sold_on_another_channel_is_not_purchasable_even_while_its_listing_says_active()
    {
        var harness = NewHarness();
        StoreListing listing = harness.World.NewListing("Copper Kettle", 60m, 20m, itemStatus: InventoryStatus.Sold);

        AppResult<StorefrontCartDto> result = await harness.Cart.AddLineAsync(Org, null, new AddCartLineRequest(listing.Id), CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Contains("no longer available", result.Message);
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
