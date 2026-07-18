using System.Net.Mail;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Storefront;

/// <summary>
/// Buyer offers submitted from the public storefront. Offers capture buyer intent only; checkout remains the only
/// path that reserves inventory or moves money.
/// </summary>
public sealed class StorefrontOfferWorkflowService(IStorefrontOfferStore store, TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<StorefrontOfferDto>> ListAsync(
        Guid organizationId,
        StorefrontOfferStatus? status,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StorefrontOffer> offers = await store.ListOffersAsync(organizationId, status, cancellationToken);
        return offers
            .Select(offer => new StorefrontOfferDto(
                offer.Id,
                offer.StoreListingId,
                offer.InventoryItemId,
                offer.StoreListing?.Title ?? offer.InventoryItem?.Title ?? "Storefront listing",
                offer.Status.ToString(),
                offer.OfferAmount,
                offer.ListingPriceSnapshot,
                offer.BuyerName,
                offer.BuyerEmail,
                offer.BuyerPhone,
                offer.Message,
                offer.OfferedAt,
                offer.RespondedAt,
                offer.ResponseNotes))
            .ToArray();
    }

    public async Task<AppResult<StorefrontOfferResultDto>> SubmitAsync(
        Guid organizationId,
        StorefrontOfferRequest request,
        CancellationToken cancellationToken)
    {
        string? buyerName = Text.TrimToNull(request.BuyerName);
        string? buyerEmail = Text.TrimToNull(request.BuyerEmail);
        string? buyerPhone = Text.TrimToNull(request.BuyerPhone);
        string? message = Text.TrimToNull(request.Message);

        if (buyerName is null)
        {
            return AppResult<StorefrontOfferResultDto>.Validation("Name is required.");
        }
        if (!IsEmail(buyerEmail))
        {
            return AppResult<StorefrontOfferResultDto>.Validation("A valid email is required.");
        }
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

        StorefrontOffer offer = new()
        {
            OrganizationId = organizationId,
            StoreListingId = listing.Id,
            StoreListing = listing,
            InventoryItemId = listing.InventoryItemId,
            InventoryItem = listing.InventoryItem,
            OfferAmount = decimal.Round(request.OfferAmount, 2, MidpointRounding.AwayFromZero),
            ListingPriceSnapshot = listing.ListPrice,
            BuyerName = buyerName,
            BuyerEmail = buyerEmail!.Trim().ToLowerInvariant(),
            BuyerPhone = buyerPhone,
            Message = message,
            OfferedAt = timeProvider.GetUtcNow(),
            Status = StorefrontOfferStatus.New,
        };

        await store.AddOfferAsync(offer, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return AppResult<StorefrontOfferResultDto>.Ok(new StorefrontOfferResultDto(
            offer.Id,
            offer.Status.ToString(),
            "Offer received. We will reply by email."));
    }

    private static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!value.Contains('@'))
        {
            return false;
        }

        try
        {
            MailAddress address = new(value);
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
