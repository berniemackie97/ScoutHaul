using System.Globalization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Buyer offers submitted from the public storefront. Offers capture buyer intent only; checkout remains the only
/// path that reserves inventory or moves money.
/// </summary>
public sealed class StorefrontOfferWorkflowService(
    IStorefrontOfferStore store,
    IStorefrontCustomerStore customers,
    TimeProvider timeProvider)
{
    private const int ResponseExpiryDays = 7;

    /// <summary>Offers in each status across the whole book — independent of whichever tab is being viewed.</summary>
    public async Task<StorefrontOfferCountsDto> CountsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<StorefrontOfferStatus, int> counts = await store.CountByStatusAsync(organizationId, cancellationToken);
        int Of(StorefrontOfferStatus status) => counts.GetValueOrDefault(status);

        return new StorefrontOfferCountsDto(
            Of(StorefrontOfferStatus.New),
            Of(StorefrontOfferStatus.Countered),
            Of(StorefrontOfferStatus.Accepted),
            Of(StorefrontOfferStatus.Declined),
            Of(StorefrontOfferStatus.Expired),
            Of(StorefrontOfferStatus.Converted),
            counts.Values.Sum());
    }

    public async Task<IReadOnlyList<StorefrontOfferDto>> ListAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StorefrontOffer> offers = await store.ListOffersAsync(organizationId, status, cancellationToken);
        return offers
            .Select(offer => ToAdminDto(offer, timeProvider.GetUtcNow()))
            .ToArray();
    }

    public async Task<IReadOnlyList<StorefrontCustomerOfferDto>> ListCustomerAsync(
        Guid organizationId,
        StorefrontCustomerIdentity identity,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Customer customer = await customers.GetOrCreateAsync(organizationId, identity, now, cancellationToken);
        IReadOnlyList<StorefrontOffer> offers = await store.ListCustomerOffersAsync(organizationId, customer.Id, cancellationToken);
        return offers.Select(offer => ToCustomerDto(offer, now)).ToArray();
    }

    public async Task<AppResult<StorefrontOfferResultDto>> SubmitAsync(
        Guid organizationId,
        StorefrontCustomerIdentity identity,
        StorefrontOfferRequest request,
        CancellationToken cancellationToken)
    {
        string? message = Text.TrimToNull(request.Message);
        if (request.OfferAmount <= 0)
        {
            return AppResult<StorefrontOfferResultDto>.Validation("Offer amount must be greater than zero.");
        }

        StoreListing? listing = await store.FindListingAsync(organizationId, request.StoreListingId, cancellationToken);
        if (listing is null)
        {
            return AppResult<StorefrontOfferResultDto>.NotFound("Listing was not found.");
        }
        if (!StorefrontPurchasability.IsPurchasable(listing))
        {
            return AppResult<StorefrontOfferResultDto>.Conflict(StorefrontPurchasability.RefusalReason(listing));
        }
        if (!listing.AcceptsOffers)
        {
            return AppResult<StorefrontOfferResultDto>.Conflict("This item is not accepting offers.");
        }

        decimal offerAmount = decimal.Round(request.OfferAmount, 2, MidpointRounding.AwayFromZero);
        if (listing.MinimumOfferAmount is decimal min && offerAmount < min)
        {
            return AppResult<StorefrontOfferResultDto>.Validation($"Minimum offer is {Money(min)}.");
        }
        if (listing.MaximumOfferAmount is decimal max && offerAmount > max)
        {
            return AppResult<StorefrontOfferResultDto>.Validation($"Maximum offer is {Money(max)}.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Customer customer = await customers.GetOrCreateAsync(organizationId, identity, now, cancellationToken);
        StorefrontOffer offer = new()
        {
            OrganizationId = organizationId,
            CustomerId = customer.Id,
            Customer = customer,
            StoreListingId = listing.Id,
            StoreListing = listing,
            InventoryItemId = listing.InventoryItemId,
            InventoryItem = listing.InventoryItem,
            OfferAmount = offerAmount,
            ListingPriceSnapshot = listing.ListPrice,
            BuyerName = customer.DisplayName,
            BuyerEmail = customer.Email,
            BuyerPhone = customer.Phone,
            Message = message,
            OfferedAt = now,
            Status = StorefrontOfferStatus.New,
        };

        await store.AddOfferAsync(offer, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return AppResult<StorefrontOfferResultDto>.Ok(new StorefrontOfferResultDto(
            offer.Id,
            offer.Status.ToString(),
            "Offer received. Check My Offers for the response."));
    }

    public async Task<AppResult<StorefrontOfferDto>> DecideAsync(
        Guid organizationId,
        Guid offerId,
        StorefrontOfferDecisionRequest request,
        CancellationToken cancellationToken)
    {
        StorefrontOffer? offer = await store.FindOfferAsync(organizationId, offerId, cancellationToken);
        if (offer is null)
        {
            return AppResult<StorefrontOfferDto>.NotFound("Offer was not found.");
        }

        string? decision = Text.TrimToNull(request.Decision);
        string? responseMessage = Text.TrimToNull(request.ResponseMessage);
        DateTimeOffset now = timeProvider.GetUtcNow();

        switch (decision?.ToLowerInvariant())
        {
            case "accept":
            case "accepted":
                offer.Status = StorefrontOfferStatus.Accepted;
                offer.ResponseAmount = offer.OfferAmount;
                offer.ResponseMessage = responseMessage ?? $"Accepted at {Money(offer.OfferAmount)}.";
                offer.ExpiresAt = now.AddDays(ResponseExpiryDays);
                break;

            case "decline":
            case "declined":
                offer.Status = StorefrontOfferStatus.Declined;
                offer.ResponseAmount = null;
                offer.ResponseMessage = responseMessage ?? "Declined.";
                offer.ExpiresAt = null;
                break;

            case "counter":
            case "countered":
                if (request.ResponseAmount is null || request.ResponseAmount <= 0)
                {
                    return AppResult<StorefrontOfferDto>.Validation("Counter amount must be greater than zero.");
                }

                decimal counter = decimal.Round(request.ResponseAmount.Value, 2, MidpointRounding.AwayFromZero);
                offer.Status = StorefrontOfferStatus.Countered;
                offer.ResponseAmount = counter;
                offer.ResponseMessage = responseMessage ?? $"Countered at {Money(counter)}.";
                offer.ExpiresAt = now.AddDays(ResponseExpiryDays);
                break;

            default:
                return AppResult<StorefrontOfferDto>.Validation("Decision must be accept, decline, or counter.");
        }

        offer.RespondedAt = now;
        offer.ResponseNotes = offer.ResponseMessage;
        await store.SaveChangesAsync(cancellationToken);

        return AppResult<StorefrontOfferDto>.Ok(ToAdminDto(offer, now));
    }

    private static string Money(decimal amount) => string.Format(CultureInfo.GetCultureInfo("en-US"), "{0:C}", amount);

    private static StorefrontOfferDto ToAdminDto(StorefrontOffer offer, DateTimeOffset now) => new(
        offer.Id,
        offer.CustomerId,
        offer.StoreListingId,
        offer.InventoryItemId,
        offer.StoreListing?.Title ?? offer.InventoryItem?.Title ?? "Storefront listing",
        DisplayStatus(offer, now),
        offer.OfferAmount,
        offer.ListingPriceSnapshot,
        offer.ResponseAmount,
        offer.BuyerName ?? offer.Customer?.DisplayName,
        offer.BuyerEmail ?? offer.Customer?.Email,
        offer.BuyerPhone ?? offer.Customer?.Phone,
        offer.Message,
        offer.OfferedAt,
        offer.RespondedAt,
        offer.ExpiresAt,
        offer.ConvertedAt,
        offer.ResponseMessage,
        offer.ResponseNotes);

    private static StorefrontCustomerOfferDto ToCustomerDto(StorefrontOffer offer, DateTimeOffset now)
    {
        string status = DisplayStatus(offer, now);
        bool canCheckout = IsCheckoutableStatus(status)
            && CartWorkflowService.ApprovedOfferAmount(offer, now) is not null
            && StorefrontPurchasability.IsPurchasable(offer.StoreListing);

        return new StorefrontCustomerOfferDto(
            offer.Id,
            offer.StoreListingId,
            offer.StoreListing?.Title ?? offer.InventoryItem?.Title ?? "Storefront listing",
            offer.StoreListing?.Slug ?? string.Empty,
            status,
            offer.OfferAmount,
            offer.ListingPriceSnapshot,
            offer.ResponseAmount,
            offer.ResponseMessage,
            offer.OfferedAt,
            offer.RespondedAt,
            offer.ExpiresAt,
            canCheckout);
    }

    private static string DisplayStatus(StorefrontOffer offer, DateTimeOffset now) =>
        offer.ExpiresAt is DateTimeOffset expiresAt
            && expiresAt <= now
            && offer.Status is StorefrontOfferStatus.Accepted or StorefrontOfferStatus.Countered
            ? StorefrontOfferStatus.Expired.ToString()
            : offer.Status.ToString();

    private static bool IsCheckoutableStatus(string status) =>
        status is nameof(StorefrontOfferStatus.Accepted) or nameof(StorefrontOfferStatus.Countered);
}
