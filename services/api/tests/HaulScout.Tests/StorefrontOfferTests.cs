using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public sealed class StorefrontOfferTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly StorefrontCustomerIdentity Buyer =
        StorefrontCustomerIdentity.Supabase("buyer-subject", "buyer@example.com", "Buyer Example");

    private sealed class FakeCustomerStore : IStorefrontCustomerStore
    {
        public Customer Customer { get; } = new()
        {
            OrganizationId = Org,
            ExternalSubject = Buyer.ExternalSubject,
            Email = Buyer.Email,
            DisplayName = Buyer.DisplayName,
        };

        public Task<Customer> GetOrCreateAsync(
            Guid organizationId,
            StorefrontCustomerIdentity identity,
            DateTimeOffset seenAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(Customer);
    }

    private sealed class FakeOfferStore(StoreListing? listing) : IStorefrontOfferStore
    {
        public List<StorefrontOffer> Offers { get; } = [];

        public Task<IReadOnlyList<StorefrontOffer>> ListOffersAsync(
            Guid organizationId,
            StorefrontOfferStatus? status,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StorefrontOffer>>(Offers
                .Where(offer => offer.OrganizationId == organizationId && (status is null || offer.Status == status))
                .ToList());

        public Task<int> CountOffersAsync(Guid organizationId, StorefrontOfferStatus? status, CancellationToken cancellationToken) =>
            Task.FromResult(Offers.Count(offer => offer.OrganizationId == organizationId && (status is null || offer.Status == status)));

        public Task<StorefrontOffer?> FindOfferAsync(Guid organizationId, Guid offerId, CancellationToken cancellationToken) =>
            Task.FromResult(Offers.FirstOrDefault(offer => offer.OrganizationId == organizationId && offer.Id == offerId));

        public Task<IReadOnlyList<StorefrontOffer>> ListCustomerOffersAsync(
            Guid organizationId,
            Guid customerId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StorefrontOffer>>(Offers
                .Where(offer => offer.OrganizationId == organizationId && offer.CustomerId == customerId)
                .ToList());

        public Task<StorefrontOffer?> FindCustomerOfferAsync(
            Guid organizationId,
            Guid customerId,
            Guid offerId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Offers.FirstOrDefault(offer =>
                offer.OrganizationId == organizationId && offer.CustomerId == customerId && offer.Id == offerId));

        public Task<StoreListing?> FindListingAsync(Guid organizationId, Guid storeListingId, CancellationToken cancellationToken) =>
            Task.FromResult(listing is not null && listing.OrganizationId == organizationId && listing.Id == storeListingId ? listing : null);

        public Task AddOfferAsync(StorefrontOffer offer, CancellationToken cancellationToken)
        {
            Offers.Add(offer);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task List_returns_operator_offer_rows()
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);
        await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        IReadOnlyList<StorefrontOfferDto> rows = await service.ListAsync(Org, StorefrontOfferStatus.New, CancellationToken.None);

        StorefrontOfferDto row = Assert.Single(rows);
        Assert.Equal(listing.Title, row.ListingTitle);
        Assert.Equal("buyer@example.com", row.BuyerEmail);
        Assert.Equal(80m, row.OfferAmount);
    }

    [Fact]
    public async Task Submit_captures_offer_without_reserving_listing()
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        FakeCustomerStore customers = new();
        StorefrontOfferWorkflowService service = new(store, customers, TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        StorefrontOffer offer = Assert.Single(store.Offers);
        Assert.Equal(StorefrontOfferStatus.New, offer.Status);
        Assert.Equal(80m, offer.OfferAmount);
        Assert.Equal(customers.Customer.Id, offer.CustomerId);
        Assert.Equal("buyer@example.com", offer.BuyerEmail);
        Assert.Equal(100m, offer.ListingPriceSnapshot);
        Assert.Equal(StoreListingStatus.Active, listing.Status);
    }

    [Fact]
    public async Task Submit_refuses_unavailable_listing()
    {
        StoreListing listing = Listing();
        listing.Status = StoreListingStatus.Reserved;
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Empty(store.Offers);
    }

    [Fact]
    public async Task Submit_validates_amount()
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 0m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
        Assert.Empty(store.Offers);
    }

    [Fact]
    public async Task Submit_enforces_listing_offer_policy()
    {
        StoreListing listing = Listing();
        listing.MinimumOfferAmount = 75m;
        listing.MaximumOfferAmount = 95m;
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);

        AppResult<StorefrontOfferResultDto> low = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 70m, null),
            CancellationToken.None);
        AppResult<StorefrontOfferResultDto> valid = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, low.Status);
        Assert.Equal(AppResultStatus.Ok, valid.Status);
        Assert.Single(store.Offers);
    }

    [Fact]
    public async Task Submit_refuses_listings_that_do_not_accept_offers()
    {
        StoreListing listing = Listing();
        listing.AcceptsOffers = false;
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Empty(store.Offers);
    }

    [Fact]
    public async Task Customer_list_only_returns_that_customers_offers()
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        FakeCustomerStore customers = new();
        StorefrontOfferWorkflowService service = new(store, customers, TimeProvider.System);
        await service.SubmitAsync(Org, Buyer, new StorefrontOfferRequest(listing.Id, 80m, null), CancellationToken.None);
        store.Offers.Add(new StorefrontOffer
        {
            OrganizationId = Org,
            CustomerId = Guid.NewGuid(),
            StoreListingId = listing.Id,
            StoreListing = listing,
            InventoryItemId = listing.InventoryItemId,
            InventoryItem = listing.InventoryItem,
            OfferAmount = 70m,
            ListingPriceSnapshot = 100m,
        });

        IReadOnlyList<StorefrontCustomerOfferDto> rows = await service.ListCustomerAsync(Org, Buyer, CancellationToken.None);

        StorefrontCustomerOfferDto row = Assert.Single(rows);
        Assert.Equal(80m, row.OfferAmount);
    }

    [Fact]
    public async Task Admin_can_accept_decline_or_counter_offer()
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, new FakeCustomerStore(), TimeProvider.System);
        AppResult<StorefrontOfferResultDto> submitted = await service.SubmitAsync(
            Org,
            Buyer,
            new StorefrontOfferRequest(listing.Id, 80m, null),
            CancellationToken.None);

        AppResult<StorefrontOfferDto> accepted = await service.DecideAsync(
            Org,
            submitted.Value!.Id,
            new StorefrontOfferDecisionRequest("accept", null, null),
            CancellationToken.None);
        AppResult<StorefrontOfferDto> countered = await service.DecideAsync(
            Org,
            submitted.Value!.Id,
            new StorefrontOfferDecisionRequest("counter", 90m, "Meet at 90."),
            CancellationToken.None);
        AppResult<StorefrontOfferDto> declined = await service.DecideAsync(
            Org,
            submitted.Value!.Id,
            new StorefrontOfferDecisionRequest("decline", null, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, accepted.Status);
        Assert.Equal("Accepted", accepted.Value!.Status);
        Assert.Equal(80m, accepted.Value.ResponseAmount);
        Assert.Equal(AppResultStatus.Ok, countered.Status);
        Assert.Equal("Countered", countered.Value!.Status);
        Assert.Equal(90m, countered.Value.ResponseAmount);
        Assert.Equal("Meet at 90.", countered.Value.ResponseMessage);
        Assert.Equal(AppResultStatus.Ok, declined.Status);
        Assert.Equal("Declined", declined.Value!.Status);
    }

    private static StoreListing Listing()
    {
        InventoryItem item = new()
        {
            OrganizationId = Org,
            Title = "Walnut Desk",
            Status = InventoryStatus.Listed,
        };

        return new StoreListing
        {
            OrganizationId = Org,
            InventoryItemId = item.Id,
            InventoryItem = item,
            Slug = "walnut-desk",
            Title = "Walnut Desk",
            ListPrice = 100m,
            Status = StoreListingStatus.Active,
        };
    }
}
