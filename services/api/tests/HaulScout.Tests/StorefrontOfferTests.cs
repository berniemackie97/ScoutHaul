using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Storefront;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Tests;

public sealed class StorefrontOfferTests
{
    private static readonly Guid Org = Guid.NewGuid();

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
        StorefrontOfferWorkflowService service = new(store, TimeProvider.System);
        await service.SubmitAsync(
            Org,
            new StorefrontOfferRequest(listing.Id, "Bernie", "buyer@example.com", null, 80m, "Can pick up today."),
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
        StorefrontOfferWorkflowService service = new(store, TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            new StorefrontOfferRequest(listing.Id, "Bernie", "buyer@example.com", null, 80m, "Can pick up today."),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Ok, result.Status);
        StorefrontOffer offer = Assert.Single(store.Offers);
        Assert.Equal(StorefrontOfferStatus.New, offer.Status);
        Assert.Equal(80m, offer.OfferAmount);
        Assert.Equal(100m, offer.ListingPriceSnapshot);
        Assert.Equal(StoreListingStatus.Active, listing.Status);
    }

    [Fact]
    public async Task Submit_refuses_unavailable_listing()
    {
        StoreListing listing = Listing();
        listing.Status = StoreListingStatus.Reserved;
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            new StorefrontOfferRequest(listing.Id, "Bernie", "buyer@example.com", null, 80m, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Conflict, result.Status);
        Assert.Empty(store.Offers);
    }

    [Theory]
    [InlineData("", "buyer@example.com", 80)]
    [InlineData("Buyer", "not-email", 80)]
    [InlineData("Buyer", "buyer@example.com", 0)]
    public async Task Submit_validates_buyer_and_amount(string name, string email, decimal amount)
    {
        StoreListing listing = Listing();
        FakeOfferStore store = new(listing);
        StorefrontOfferWorkflowService service = new(store, TimeProvider.System);

        AppResult<StorefrontOfferResultDto> result = await service.SubmitAsync(
            Org,
            new StorefrontOfferRequest(listing.Id, name, email, null, amount, null),
            CancellationToken.None);

        Assert.Equal(AppResultStatus.Validation, result.Status);
        Assert.Empty(store.Offers);
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
